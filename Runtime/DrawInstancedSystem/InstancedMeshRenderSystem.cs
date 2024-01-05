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
using static Unity.Mathematics.math;

namespace Com.Rendering
{
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public sealed class InstancedMeshRenderSystem : IDisposable
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
        NativeList<float4x4> batchLocalToWorldBuffer;
        /// <summary>
        /// [job system input] 所有 batch 的基点被写入
        /// </summary>
        NativeList<bool> batchLocalToWorldDirtyMask;
        /// <summary>
        /// [job system input] 每 batch 的实际绘制数目
        /// </summary>
        NativeList<int> batchCountBuffer;

        /// <summary>
        /// [job system input] 所有 batch 的本地包围盒
        /// </summary>
        NativeList<Bounds> batchLocalBoundsBuffer;

        /// <summary>
        /// [job system input] 所有 instance 相对 batch 的本地偏移
        /// </summary>
        NativeList<float4x4> instanceLocalOffsetBuffer;

        /// <summary>
        /// [job system output] 所有 instance 的本地空间到世界空间变换。
        ///缓存这个变换，移除物体时可以不重计算变换。
        /// </summary>
        NativeList<float4x4> instanceLocalToWorldBuffer;
        /// <summary>
        /// [job system output] 所有 instance 的世界空间到本地空间变换。
        /// 缓存这个变换，移除物体时可以不重计算变换。
        /// </summary>
        NativeList<float4x4> instanceWorldToLocalBuffer;

        /// <summary>
        /// 所有 keeper 持有的实例的颜色
        /// </summary>
        NativeList<float4> instanceColorBuffer;

        /// <summary>
        /// [job system output] 所有 instance 从分片内存传递到连续内存的映射
        /// </summary>
        NativeList<int> instanceIndirectIndexMap;

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

            Release(batchLocalToWorldBuffer);
            Release(batchLocalToWorldDirtyMask);
            Release(batchCountBuffer);

            Release(instanceLocalOffsetBuffer);
            //Release(instanceVisibleBuffer);
            Release(instanceLocalToWorldBuffer);
            Release(instanceWorldToLocalBuffer);
            Release(batchLocalBoundsBuffer);
            Release(instanceColorBuffer);

            Release(instanceIndirectIndexMap);

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
                var batchLocalToWorldReader = batchLocalToWorldBuffer.AsParallelReader();

                // 移动了物体，或者有物体改变形状
                if (batchLocalToWorldDirty || instanceLocalOffsetDirty)
                {
                    // trs = mul(localOffset, localToWorld)
                    new MulTrsJobFor
                    {
                        batchSize = batchSize,
                        batchLocalToWorld = batchLocalToWorldReader,
                        batchLocalToWorldDirty = batchLocalToWorldDirtyMask.AsParallelReader(),
                        batchCount = batchCountBuffer.AsParallelReader(),
                        instLocalOffset = instanceLocalOffsetBuffer.AsParallelReader(),
                        instLocalToWorld = instanceLocalToWorldBuffer.AsParallelWriter(),
                        instWorldToLocal = instanceWorldToLocalBuffer.AsParallelWriter(),
                    }.Schedule(instanceNumber, 64, default).Complete();

                    instanceVisibleDirty = true;
                }

                // 移动了物体，或者有物体改变包围盒尺寸
                if (batchLocalToWorldDirty || batchLocalBoundsDirty)
                {
                    using var outputMinMax = new NativeArray<float3x2>(batchNumber,
                        Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var job = new TransposeBoundsFor
                    {
                        localToWorld = batchLocalToWorldReader,
                        inputLocalBounds = batchLocalBoundsBuffer.AsParallelReader().Reinterpret<float3x2>(),
                        outputWorldMinMax = outputMinMax
                    }.Schedule(batchNumber, 64, default);

                    var vMinMax = new NativeArray<float3>(2,
                      Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    vMinMax[0] = float3(float.MaxValue);
                    vMinMax[1] = float3(float.MinValue);
                    new BoundsMinMaxJobFor
                    {
                        srcMinMax = outputMinMax,
                        minMax2 = vMinMax
                    }.Schedule(batchNumber, job).Complete();

                    var pMinMax = (float3*)vMinMax.GetUnsafeReadOnlyPtr();
                    MinMax2Bounds(pMinMax[0], pMinMax[1], ref cachedWorldBounds);
                    vMinMax.Dispose();
                }

                // reset dirty
                instanceLocalOffsetDirty =
                batchLocalToWorldDirty =
                batchLocalBoundsDirty = false;

                UnsafeUtility.MemClear(batchLocalToWorldDirtyMask.GetUnsafePtr(), batchNumber);
            }

            if (instanceVisibleDirty)
            {
                MakeIndirectIndexMap(instanceNumber, batchSize,
                    batchCountBuffer.GetUnsafePtr(),
                    ref visibleNumber, instanceIndirectIndexMap.GetUnsafePtr());

                using var instanceLocalToWorldIndirectBuffer = new NativeArray<float4x4>(instanceNumber,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                using var instanceWorldToLocalIndirectBuffer = new NativeArray<float4x4>(instanceNumber,
                    Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var _job = default(JobHandle);
                _job = new CopyMatrixBufferFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsParallelReader(),
                    src = instanceLocalToWorldBuffer.AsReadOnly(),
                    dst = instanceLocalToWorldIndirectBuffer,
                }.Schedule(visibleNumber, 128, _job);
                _job = new CopyMatrixBufferFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsParallelReader(),
                    src = instanceWorldToLocalBuffer.AsReadOnly(),
                    dst = instanceWorldToLocalIndirectBuffer,
                }.Schedule(visibleNumber, 128, _job);
                _job.Complete();

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
                new CopyVectorFieldsFor
                {
                    indirectIndexMap = instanceIndirectIndexMap.AsParallelReader(),
                    src = instanceColorBuffer.AsReadOnly(),
                    dst = instanceIndirectColorBuffer,
                }.Schedule(visibleNumber, 128).Complete();
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
        ///<see cref="NativeList{T}"/> 的内存分配器实现一定会分配容量为2次幂的长度，不用刻意凑整。
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
                //Realloc(ref instanceVisibleBuffer, instanceCapacity);
                Realloc(ref instanceLocalToWorldBuffer, instanceCapacity);
                Realloc(ref instanceWorldToLocalBuffer, instanceCapacity);
                Realloc(ref instanceColorBuffer, instanceCapacity);

                Realloc(ref instanceIndirectIndexMap, instanceCapacity);

                Realloc(ref batchLocalToWorldBuffer, capacity);
                Realloc(ref batchLocalToWorldDirtyMask, capacity);
                Realloc(ref batchCountBuffer, capacity);

                Realloc(ref batchLocalBoundsBuffer, capacity);

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
            var ptr = (Matrix4x4*)instanceLocalOffsetBuffer.GetUnsafePtr();
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
            batchLocalBoundsBuffer[index] = localBounds;
            batchLocalBoundsDirty = true;
        }

        /// <summary>
        /// 指定某个批次的基点世界空间变换
        /// </summary>
        /// <param name="index"></param>
        /// <param name="localToWorld"></param>
        public unsafe void WriteBatchLocalToWorldAt(int index, in Matrix4x4 localToWorld)
        {
            var ptr = (Matrix4x4*)batchLocalToWorldBuffer.GetUnsafePtr();
            bool* maskPtr = (bool*)batchLocalToWorldDirtyMask.GetUnsafePtr();
            ptr[index] = localToWorld;
            maskPtr[index] = true;
            batchLocalToWorldDirty = true;
        }

        public unsafe void WriteBatchLocalToWorld(TransformAccessArray transforms)
        {
            var anyDirty = new NativeArray<bool>(1, Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            new SyncBatchLocalToWorldFor
            {
                length = batchNumber,
                batchLocalToWorldBuffer = batchLocalToWorldBuffer.AsArray().Reinterpret<float4x4>(),
                batchLocalToWorldDirtyMask = batchLocalToWorldDirtyMask.AsArray(),
                anyDirty = anyDirty,
            }
            .ScheduleReadOnly(transforms, 64).Complete();

            batchLocalToWorldDirty = *(bool*)anyDirty.GetUnsafePtr();
        }

        /// <summary>
        /// 为某个批次指定绘制实例个数
        /// </summary>
        /// <param name="index"></param>
        /// <param name="count"></param>
        public unsafe void WriteBatchCountAt(int index, int count)
        {
            int* ptr = (int*)batchCountBuffer.GetUnsafePtr();
            ptr[index] = count;
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
            var ptr = (Color*)instanceColorBuffer.GetUnsafePtr();
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
            var ptr = (Color*)instanceColorBuffer.GetUnsafePtr();
            ptr[batchSize * batchIndex + indexOffset] = color;
        }

        public void EraseAt(int batchIndex)
        {
            --batchNumber;
            instanceNumber = batchNumber * batchSize;

            Erase(batchLocalToWorldBuffer, batchIndex, batchNumber);
            Erase(batchLocalToWorldDirtyMask, batchIndex, batchNumber);
            Erase(batchCountBuffer, batchIndex, batchNumber);
            //batchLocalToWorldDirty = true;

            Erase(batchLocalBoundsBuffer, batchIndex, batchNumber);
            batchLocalBoundsDirty = true;

            int instanceStart = batchIndex * batchSize;
            for (int i = 0; i < batchSize; i++)
            {
                int removeIdx = i + instanceStart;
                int lastIdx = i + instanceNumber;
                Erase(instanceLocalOffsetBuffer, removeIdx, lastIdx);
                Erase(instanceColorBuffer, removeIdx, lastIdx);
                Erase(instanceLocalToWorldBuffer, removeIdx, lastIdx);
                Erase(instanceWorldToLocalBuffer, removeIdx, lastIdx);
            }
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
                Assert.IsTrue(value < batchCapacity + 1, $"length <= {batchCapacity}");
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
            renderingLayerMask = GraphicsSettings.defaultRenderingLayerMask,
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