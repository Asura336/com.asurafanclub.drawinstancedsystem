using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Jobs;
using UnityEngine.Rendering;
using static Com.Rendering.DrawInstancedSystemTools;
using static Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility;
using static Unity.Mathematics.math;

namespace Com.Rendering
{
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public unsafe sealed class InstancedMeshRenderSystem : IDisposable
    {
        /// <summary>
        /// 每批次数量，内部缓冲区按这个长度分片
        /// </summary>
        public readonly int batchSize = 1024;

        public readonly Mesh instanceMesh;

        int batchCapacity = -1;
        /// <summary>
        /// 最终的实例数目 = batchNumber * batchSize
        /// </summary>
        int batchNumber = -1;
        int instanceNumber = -1;

        public bool Valid => batchCapacity > -1
            && batchNumber > -1
            && instanceNumber == batchNumber * batchSize;

        int visibleNumber = 0;

        /// <summary>
        /// [job system input] 所有 batch 的基点变换
        /// </summary>
        NativeArray<float4x4> batchLocalToWorldBuffer;
        float4x4* batchLocalToWorldBufferPnt;
        /// <summary>
        /// [job system input] 所有 batch 的基点被写入
        /// </summary>
        NativeArray<bool> batchLocalToWorldDirtyMask;
        bool* batchLocalToWorldDirtyMaskPnt;
        /// <summary>
        /// [job system input] 每 batch 的实际绘制数目
        /// </summary>
        NativeArray<int> batchCountBuffer;
        int* batchCountBufferPnt;

        /// <summary>
        /// [job system input] 所有 batch 的本地包围盒
        /// </summary>
        NativeArray<Bounds> batchLocalBoundsBuffer;
        Bounds* batchLocalBoundsBufferPnt;

        /// <summary>
        /// [job system input] 所有 instance 相对 batch 的本地偏移
        /// </summary>
        NativeArray<float4x4> instanceLocalOffsetBuffer;
        float4x4* instanceLocalOffsetBufferPnt;

        /// <summary>
        /// [job system output] 所有 instance 的本地空间到世界空间变换。
        ///缓存这个变换，移除物体时可以不重计算变换。
        /// </summary>
        NativeArray<float4x4> instanceLocalToWorldBuffer;
        float4x4* instanceLocalToWorldBufferPnt;
        /// <summary>
        /// [job system output] 所有 instance 的世界空间到本地空间变换。
        /// 缓存这个变换，移除物体时可以不重计算变换。
        /// </summary>
        NativeArray<float4x4> instanceWorldToLocalBuffer;
        float4x4* instanceWorldToLocalBufferPnt;

        /// <summary>
        /// 所有 keeper 持有的实例的颜色
        /// </summary>
        NativeArray<float4> instanceColorBuffer;
        float4* instanceColorBufferPnt;

        /// <summary>
        /// [job system output] 所有 instance 从分片内存传递到连续内存的映射
        /// </summary>
        NativeArray<int> instanceIndirectIndexMap;

        bool _disposed;

        bool instanceLocalOffsetDirty;
        bool instanceColorDirty;
        bool instanceVisibleDirty;
        bool batchLocalToWorldDirty;
        bool batchLocalBoundsDirty;

        Bounds cachedWorldBounds;

        readonly MaterialPropertyBlock props;
        GraphicsBuffer loadlToWorldBuffer;
        GraphicsBuffer worldToLocalBuffer;
        GraphicsBuffer colorsBuffer;
        public readonly Material instandedMaterial;

        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        public bool recieveShadows = true;
        public int layer = 0;
        public ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        public LightProbeProxyVolume lightProbeProxyVolume = null;
        public LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes;

        public int defaultBatchNumber = 32;

        private InstancedMeshRenderSystem()
        {
            props = new MaterialPropertyBlock();
            props.SetFloat("_UniqueID", UnityEngine.Random.value);
        }

        public InstancedMeshRenderSystem(Mesh mesh, Material mat, int batchSize) : this()
        {
            instanceMesh = mesh;
            instandedMaterial = mat;
            this.batchSize = batchSize;
        }

        public InstancedMeshRenderSystem(Mesh mesh, Material mat, string overrideRenderType, int batchSize)
            : this(mesh, mat, batchSize)
        {
            instandedMaterial.SetOverrideTag("RenderType", overrideRenderType);
        }

        ~InstancedMeshRenderSystem()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        void Dispose(bool disposing)
        {
            if (_disposed) { return; }
            if (disposing)
            {
                // managed
                batchNumber = 0;
                instanceNumber = 0;
                visibleNumber = 0;
            }

            // unmanaged
            loadlToWorldBuffer?.Dispose();
            loadlToWorldBuffer = null;
            worldToLocalBuffer?.Dispose();
            worldToLocalBuffer = null;
            colorsBuffer?.Dispose();
            colorsBuffer = null;

            Release(ref batchLocalToWorldBuffer); batchLocalToWorldBufferPnt = null;
            Release(ref batchLocalToWorldDirtyMask); batchLocalToWorldDirtyMaskPnt = null;
            Release(ref batchCountBuffer); batchCountBufferPnt = null;

            Release(ref instanceLocalOffsetBuffer); instanceLocalOffsetBufferPnt = null;
            Release(ref instanceLocalToWorldBuffer); instanceLocalOffsetBufferPnt = null;
            Release(ref instanceWorldToLocalBuffer); instanceWorldToLocalBufferPnt = null;
            Release(ref batchLocalBoundsBuffer); batchLocalBoundsBufferPnt = null;
            Release(ref instanceColorBuffer); instanceColorBufferPnt = null;

            Release(ref instanceIndirectIndexMap);

            _disposed = true;
        }

        public unsafe void Update()
        {
            /* 每批次
             *   检查写入过本地包围盒
             *   检查写入过本地偏移量
             *   检查写入过基点变换
             * 
             * 如果 写入任何本地包围盒
             *   重计算世界空间包围盒
             * 如果 写入过任何本地偏移量 或者 写入过任何基点变换
             *   重计算实例世界空间变换
             * 
             * 如果 重计算过实例世界空间变换
             *   重新传递材质属性缓冲区内容
             * 如果 重计算过世界空间包围盒
             *   更新总包围盒
             */
            if (batchNumber < 1) { return; }

            if (instanceLocalOffsetDirty || batchLocalToWorldDirty || batchLocalBoundsDirty)
            {
                var batchLocalToWorldReader = batchLocalToWorldBuffer.AsReadOnly();

                // 移动了物体，或者有物体改变形状
                if (batchLocalToWorldDirty || instanceLocalOffsetDirty)
                {
                    // trs = mul(localOffset, localToWorld)
                    var job_mulTrs = new MulTrsJobFor
                    {
                        batchSize = batchSize,
                        batchLocalToWorld = batchLocalToWorldReader,
                        batchLocalToWorldDirty = batchLocalToWorldDirtyMask.AsReadOnly(),
                        batchCount = batchCountBuffer.AsReadOnly(),
                        instLocalOffset = instanceLocalOffsetBuffer.AsReadOnly(),
                        instLocalToWorld = instanceLocalToWorldBuffer,
                        instWorldToLocal = instanceWorldToLocalBuffer,
                    }; job_mulTrs.ScheduleByRef(instanceNumber, 64, default).Complete();

                    instanceVisibleDirty = true;
                }

                // 移动了物体，或者有物体改变包围盒尺寸
                if (batchLocalToWorldDirty || batchLocalBoundsDirty)
                {
                    using var outputMinMax = new NativeArray<float3x2>(batchNumber,
                        Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    JobHandle job = default;
                    var job_transBounds = new TransposeBoundsFor
                    {
                        localToWorld = batchLocalToWorldReader,
                        inputLocalBounds = batchLocalBoundsBuffer.AsReadOnly().Reinterpret<float3x2>(),
                        outputWorldMinMax = outputMinMax
                    };
                    job = job_transBounds.ScheduleByRef(batchNumber, 64, job);

                    var vMinMax = new NativeArray<float3>(2,
                      Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    vMinMax[0] = float3(float.MaxValue);
                    vMinMax[1] = float3(float.MinValue);
                    var job_minMax = new BoundsMinMaxJobFor
                    {
                        srcMinMax = outputMinMax,
                        minMax2 = vMinMax
                    };
                    job = job_minMax.ScheduleByRef(batchNumber, job);
                    job.Complete();

                    var pMinMax = (float3*)vMinMax.GetUnsafeReadOnlyPtr();
                    MinMax2Bounds(pMinMax[0], pMinMax[1], ref cachedWorldBounds);
                    vMinMax.Dispose();
                }

                // reset dirty
                instanceLocalOffsetDirty =
                batchLocalToWorldDirty =
                batchLocalBoundsDirty = false;

                UnsafeUtility.MemClear(batchLocalToWorldDirtyMaskPnt, batchNumber);
            }

            if (instanceVisibleDirty)
            {
                MakeIndirectIndexMap(instanceNumber, batchSize,
                    batchCountBufferPnt,
                    ref visibleNumber, (int*)GetUnsafeBufferPointerWithoutChecks(instanceIndirectIndexMap));

                using var instanceLocalToWorldIndirectBuffer = new NativeArray<float4x4>(instanceNumber,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                using var instanceWorldToLocalIndirectBuffer = new NativeArray<float4x4>(instanceNumber,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var job = default(JobHandle);
                var job_copyLocalToWorld = new CopyMatrixBufferFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsReadOnly(),
                    src = instanceLocalToWorldBuffer.AsReadOnly(),
                    dst = instanceLocalToWorldIndirectBuffer,
                }; job = job_copyLocalToWorld.ScheduleByRef(visibleNumber, 128, job);
                var job_copyWorldToLocal = new CopyMatrixBufferFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsReadOnly(),
                    src = instanceWorldToLocalBuffer.AsReadOnly(),
                    dst = instanceWorldToLocalIndirectBuffer,
                }; job = job_copyWorldToLocal.ScheduleByRef(visibleNumber, 128, job);
                job.Complete();

                SetData(loadlToWorldBuffer, instanceLocalToWorldIndirectBuffer, visibleNumber);
                SetData(worldToLocalBuffer, instanceWorldToLocalIndirectBuffer, visibleNumber);

                instanceVisibleDirty = false;
                // 序列变化过，重新写入材质字段
                instanceColorDirty = true;
            }

            if (instanceColorDirty)
            {
                using var instanceIndirectColorBuffer = new NativeArray<float4>(instanceNumber,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var job_copyInstanceColor = new CopyVectorFieldsFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsReadOnly(),
                    src = instanceColorBuffer.AsReadOnly(),
                    dst = instanceIndirectColorBuffer,
                }; job_copyInstanceColor.ScheduleByRef(visibleNumber, 128).Complete();
                SetData(colorsBuffer, instanceIndirectColorBuffer, visibleNumber);
                instanceColorDirty = false;
            }

            DrawMesh();
        }
        [BurstCompile]
        static unsafe void MakeIndirectIndexMap(int length,
            int batchSize, [NoAlias] int* batchCount,
            ref int counter, [NoAlias] int* indirectIndexMap)
        {
            counter = 0;
            for (int i = 0; i < length; i++)
            {
                int batchIndex = i / batchSize;
                bool inRange = (i % batchSize) < batchCount[batchIndex];
                if (inRange)
                {
                    indirectIndexMap[counter] = i;
                    counter++;
                }
            }
        }

        /// <summary>
        /// 重设长度，不会改动已有的数据，没有安全检查。
        /// </summary>
        /// <param name="capacity"></param>
        public void Setup(int capacity)
        {
            if (_disposed) { return; }

            int instanceCapacity = capacity * batchSize;
            if (capacity != batchCapacity)
            {
                // grows up
                Realloc(ref instanceLocalOffsetBuffer, instanceCapacity);
                instanceLocalOffsetBufferPnt = (float4x4*)GetUnsafeBufferPointerWithoutChecks(instanceLocalOffsetBuffer);
                //Realloc(ref instanceVisibleBuffer, instanceCapacity);
                Realloc(ref instanceLocalToWorldBuffer, instanceCapacity);
                instanceLocalToWorldBufferPnt = (float4x4*)GetUnsafeBufferPointerWithoutChecks(instanceLocalToWorldBuffer);
                Realloc(ref instanceWorldToLocalBuffer, instanceCapacity);
                instanceWorldToLocalBufferPnt = (float4x4*)GetUnsafeBufferPointerWithoutChecks(instanceWorldToLocalBuffer);
                Realloc(ref instanceColorBuffer, instanceCapacity);
                instanceColorBufferPnt = (float4*)GetUnsafeBufferPointerWithoutChecks(instanceColorBuffer);

                Realloc(ref instanceIndirectIndexMap, instanceCapacity);

                Realloc(ref batchLocalToWorldBuffer, capacity);
                batchLocalToWorldBufferPnt = (float4x4*)GetUnsafeBufferPointerWithoutChecks(batchLocalToWorldBuffer);
                Realloc(ref batchLocalToWorldDirtyMask, capacity);
                batchLocalToWorldDirtyMaskPnt = (bool*)GetUnsafeBufferPointerWithoutChecks(batchLocalToWorldDirtyMask);
                Realloc(ref batchCountBuffer, capacity);
                batchCountBufferPnt = (int*)GetUnsafeBufferPointerWithoutChecks(batchCountBuffer);

                Realloc(ref batchLocalBoundsBuffer, capacity);
                batchLocalBoundsBufferPnt = (Bounds*)GetUnsafeBufferPointerWithoutChecks(batchLocalBoundsBuffer);

                // 重分配，附加缓冲区
                loadlToWorldBuffer?.Dispose();
                loadlToWorldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    instanceCapacity, sizeofFloat4x4);
                props.SetBuffer(id_LocalToWorldBuffer, loadlToWorldBuffer);
                worldToLocalBuffer?.Dispose();
                worldToLocalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    instanceCapacity, sizeofFloat4x4);
                props.SetBuffer(id_WorldToLocalBuffer, worldToLocalBuffer);

                colorsBuffer?.Dispose();
                colorsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    instanceCapacity, sizeofFloat4);
                props.SetBuffer(id_Colors, colorsBuffer);
            }

            batchCapacity = capacity;
            batchNumber = capacity;
            instanceNumber = instanceCapacity;

            // 初始化需要更新序列
            instanceVisibleDirty = true;
        }
        /// <summary>
        /// 使用 <see cref="defaultBatchNumber"/> 重设缓冲区长度。
        /// 见 <see cref="Setup(int)"/>
        /// </summary>
        public void Setup() => Setup(defaultBatchNumber);

        public void TrimExcess()
        {
            if (batchNumber == 0 && batchCapacity > defaultBatchNumber)
            {
                Setup(defaultBatchNumber);
                batchNumber = 0;
                instanceNumber = 0;
            }
        }

        /// <summary>
        /// 指定某个批次内实例的本地空间变换（相对于批次的基点）
        /// </summary>
        /// <param name="index"></param>
        /// <param name="src"></param>
        public unsafe void WriteLocalOffsetAt(int index, Matrix4x4[] src)
        {
            WriteLocalOffsetAt(index, src, 0, Mathf.Min(src.Length, batchSize));
        }

        /// <summary>
        /// 指定某个批次内实例的本地空间变换（相对于批次的基点）
        /// </summary>
        /// <param name="index"></param>
        /// <param name="src"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        public unsafe void WriteLocalOffsetAt(int index, Matrix4x4[] src, int start, int length)
        {
            fixed (Matrix4x4* pSrc = src)
            {
                WriteLocalOffsetAt(index, pSrc, start, length);
            }
        }

        /// <summary>
        /// 指定某个批次内实例的本地空间变换（相对于批次的基点）
        /// </summary>
        /// <param name="index"></param>
        /// <param name="src"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        public unsafe void WriteLocalOffsetAt(int index, NativeArray<Matrix4x4> src, int start, int length)
        {
            WriteLocalOffsetAt(index, (Matrix4x4*)src.GetUnsafeReadOnlyPtr(), start, length);
        }

        /// <summary>
        /// 指定某个批次内实例的本地空间变换（相对于批次的基点）
        /// </summary>
        /// <param name="index"></param>
        /// <param name="pSrc"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        public unsafe void WriteLocalOffsetAt(int index, Matrix4x4* pSrc, int start, int length)
        {
            var ptr = (Matrix4x4*)instanceLocalOffsetBufferPnt;
            int instanceStart = index * batchSize;
            UnsafeUtility.MemCpy(ptr + instanceStart, pSrc + start, sizeofFloat4x4 * length);
            instanceLocalOffsetDirty = true;
        }

        /// <summary>
        /// 指定某个批次的本地空间包围
        /// </summary>
        /// <param name="index"></param>
        /// <param name="localBounds"></param>
        public void WriteLocalBoundsAt(int index, in Bounds localBounds)
        {
            batchLocalBoundsBufferPnt[index] = localBounds;
            batchLocalBoundsDirty = true;
        }

        /// <summary>
        /// 指定某个批次的基点世界空间变换
        /// </summary>
        /// <param name="index"></param>
        /// <param name="localToWorld"></param>
        public unsafe void WriteBatchLocalToWorldAt(int index, in Matrix4x4 localToWorld)
        {
            var ptr = (Matrix4x4*)batchLocalToWorldBufferPnt;
            bool* maskPtr = batchLocalToWorldDirtyMaskPnt;
            ptr[index] = localToWorld;
            maskPtr[index] = true;
            batchLocalToWorldDirty = true;
        }
        internal unsafe void WriteBatchLocalToWorldAtPnt(int index, Matrix4x4* localToWorld)
        {
            var ptr = batchLocalToWorldBufferPnt;
            UnsafeUtility.MemCpy(ptr + index, localToWorld, sizeofFloat4x4);
            batchLocalToWorldDirtyMaskPnt[index] = true;
            batchLocalToWorldDirty = true;
        }

        public unsafe void WriteBatchLocalToWorld(TransformAccessArray transforms)
        {
            var anyDirty = new NativeArray<bool>(1, Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            new SyncBatchLocalToWorldFor
            {
                length = batchNumber,
                batchLocalToWorldBuffer = batchLocalToWorldBuffer.Reinterpret<float4x4>(),
                batchLocalToWorldDirtyMask = batchLocalToWorldDirtyMask,
                anyDirty = anyDirty,
            }
            .ScheduleReadOnly(transforms, 64).Complete();

            batchLocalToWorldDirty = *(bool*)GetUnsafeBufferPointerWithoutChecks(anyDirty);
            anyDirty.Dispose();
        }

        /// <summary>
        /// 为某个批次指定绘制实例个数
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        public unsafe void WriteBatchCountAt(int index, int count)
        {
            batchCountBufferPnt[index] = count;
            batchLocalToWorldDirty = true;
        }

        /// <summary>
        /// 写入某个批次的颜色
        /// </summary>
        /// <param name="index"></param>
        /// <param name="color"></param>
        public unsafe void WriteBatchColorAt(int index, in Color color)
        {
            int start = batchSize * index;
            var ptr = (Color*)instanceColorBufferPnt;
            fixed (Color* pColor = &color)
            {
                UnsafeUtility.MemCpyReplicate(ptr + start, pColor, sizeofFloat4, batchSize);
            }
            instanceColorDirty = true;
        }

        /// <summary>
        /// 写入某一批次的某个实例的颜色
        /// </summary>
        /// <param name="batchIndex"></param>
        /// <param name="indexOffset"></param>
        /// <param name="color"></param>
        public unsafe void WriteInstanceColorAt(int batchIndex, int indexOffset, in Color color)
        {
            var ptr = (Color*)instanceColorBufferPnt;
            ptr[batchSize * batchIndex + indexOffset] = color;
        }

        public void EraseAt(int batchIndex)
        {
            --batchNumber;
            instanceNumber = batchNumber * batchSize;

            Erase(batchLocalToWorldBufferPnt, batchIndex, batchNumber);
            Erase(batchLocalToWorldDirtyMaskPnt, batchIndex, batchNumber);
            Erase(batchCountBufferPnt, batchIndex, batchNumber);
            //batchLocalToWorldDirty = true;

            Erase(batchLocalBoundsBufferPnt, batchIndex, batchNumber);
            batchLocalBoundsDirty = true;

            int instanceStart = batchIndex * batchSize;
            int lastStart = instanceNumber;
            long batchSize_4x4 = sizeofFloat4x4 * batchSize,
                batchSize_4 = sizeofFloat4 * batchSize;
            UnsafeUtility.MemCpy(instanceLocalOffsetBufferPnt + instanceStart,
                instanceLocalOffsetBufferPnt + lastStart, batchSize_4x4);
            UnsafeUtility.MemCpy(instanceLocalToWorldBufferPnt + instanceStart,
              instanceLocalToWorldBufferPnt + lastStart, batchSize_4x4);
            UnsafeUtility.MemCpy(instanceWorldToLocalBufferPnt + instanceStart,
                instanceWorldToLocalBufferPnt + lastStart, batchSize_4x4);
            UnsafeUtility.MemCpy(instanceColorBufferPnt + instanceStart,
                instanceColorBufferPnt + lastStart, batchSize_4);

            //int instanceStart = batchIndex * batchSize;
            //for (int i = 0; i < batchSize; i++)
            //{
            //    int removeIdx = i + instanceStart;
            //    int lastIdx = i + instanceNumber;
            //    Erase(instanceLocalOffsetBufferPnt, removeIdx, lastIdx);
            //    Erase(instanceColorBufferPnt, removeIdx, lastIdx);
            //    Erase(instanceLocalToWorldBufferPnt, removeIdx, lastIdx);
            //    Erase(instanceWorldToLocalBufferPnt, removeIdx, lastIdx);
            //}

            // 序列变化了
            instanceVisibleDirty = true;
        }

        public long UsedBufferMemory()
        {
            return UsedMemory(batchLocalToWorldBuffer)
                + UsedMemory(batchLocalToWorldDirtyMask)
                + UsedMemory(batchCountBuffer)
                + UsedMemory(batchLocalBoundsBuffer)

                + UsedMemory(instanceLocalOffsetBuffer)
                + UsedMemory(instanceLocalToWorldBuffer)
                + UsedMemory(instanceWorldToLocalBuffer)
                + UsedMemory(instanceColorBuffer)

                + UsedMemory(instanceIndirectIndexMap)

                + UsedMemory(loadlToWorldBuffer)
                + UsedMemory(worldToLocalBuffer)
                + UsedMemory(colorsBuffer);
        }

        public int BatchNumber
        {
            get => batchNumber;
            set
            {
                Assert.IsTrue(value < batchCapacity + 1);  // $"length <= {batchCapacity}"
                batchNumber = value;
                instanceNumber = batchNumber * batchSize;
                //Debug.Log($"set batch number = {value}, batchSize = {batchSize}");
            }
        }

        public int BatchCapacity => batchCapacity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void DrawMesh()
        {
            if (visibleNumber < 1) { return; }

            var renderParams = GetRenderParams(null);

            int subMeshCount = instanceMesh.subMeshCount;
            if (subMeshCount == 1)
            {
                Graphics.RenderMeshPrimitives(renderParams, instanceMesh, 0, visibleNumber);
            }
            else
            {
                for (int i = 0; i < subMeshCount; i++)
                {
                    Graphics.RenderMeshPrimitives(renderParams, instanceMesh, i, visibleNumber);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        RenderParams GetRenderParams(Camera targetCamera) => new RenderParams
        {
            material = instandedMaterial,
            matProps = props,
            camera = targetCamera,
            worldBounds = cachedWorldBounds,
            layer = layer,
            shadowCastingMode = shadowCastingMode,
            receiveShadows = recieveShadows,
            rendererPriority = 0,
#if UNITY_6000_0_OR_NEWER
            renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
#else
            renderingLayerMask = GraphicsSettings.defaultRenderingLayerMask,
#endif
            reflectionProbeUsage = reflectionProbeUsage,
            motionVectorMode = MotionVectorGenerationMode.Camera,
            lightProbeProxyVolume = lightProbeProxyVolume,
            lightProbeUsage = lightProbeUsage,
        };

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //void Draw(int subMeshIndex)
        //{
        //    var worldBounds = cachedWorldBounds;
        //    Graphics.DrawMeshInstancedProcedural(instanceMesh,
        //        subMeshIndex,
        //        instandedMaterial,
        //        worldBounds,
        //        visibleNumber,
        //        props,
        //        shadowCastingMode,
        //        recieveShadows,
        //        layer);
        //}
    }
}