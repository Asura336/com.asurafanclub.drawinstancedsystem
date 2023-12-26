using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static Com.Rendering.DrawInstancedSystemTools;
using static Unity.Mathematics.math;

namespace Com.Rendering
{
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
        /// [job system output] 所有 instance 的世界空间变换。
        ///缓存这个变换，移除物体时可以不重计算变换。
        ///因为内存排列上按定长度分片，不是所有的成员都代表有效值，用零值的变换表示无效值，
        ///在着色器里判断矩阵的最后一列（行）是 (0,0,0,0) 还是 (0,0,0,1) 略麻烦，不用传递 3x4 矩阵的优化做法。
        /// </summary>
        NativeList<float4x4> instanceTrsBuffer;

        /// <summary>
        /// 所有 keeper 持有的实例的颜色
        /// </summary>
        NativeList<float4> instanceColorsBuffer;

        bool _disposed;

        bool instanceLocalOffsetDirty;
        bool instanceColorDirty;
        bool batchLocalToWorldDirty;
        bool batchLocalBoundsDirty;

        Bounds cachedWorldBounds;

        readonly MaterialPropertyBlock props;
        ComputeBuffer matricesBuffer;
        ComputeBuffer colorsBuffer;
        public readonly Material instandedMaterial;

        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        public bool recieveShadows = true;
        public int layer = 0;

        private InstancedMeshRenderSystem()
        {
            props = new MaterialPropertyBlock();
            props.SetFloat("_UniqueID", UnityEngine.Random.value);
        }

        public InstancedMeshRenderSystem(Mesh mesh, Material mat, int batchSize) : this()
        {
            instanceMesh = mesh;
            instandedMaterial = new Material(mat);
            instandedMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
            instandedMaterial.EnableKeyword("INSTANCED_COLOR");
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
            }

            // unmanaged
            matricesBuffer?.Dispose();
            matricesBuffer = null;
            colorsBuffer?.Dispose();
            colorsBuffer = null;
            Material.Destroy(instandedMaterial);

            release(batchLocalToWorldBuffer);
            release(batchLocalToWorldDirtyMask);
            release(batchCountBuffer);

            release(instanceLocalOffsetBuffer);
            release(instanceTrsBuffer);
            release(batchLocalBoundsBuffer);
            release(instanceColorsBuffer);

            _disposed = true;

            static void release<T>(NativeList<T> list) where T : unmanaged
            {
                if (list.IsCreated) { list.Dispose(); }
            }
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
                        instTrs = instanceTrsBuffer.AsParallelWriter()
                    }.Schedule(instanceNumber, 64, default).Complete();

                    //matricesBuffer.SetData(outputTrs);
                    SetData(matricesBuffer, instanceTrsBuffer.AsArray(), instanceNumber);
                }

                // 移动了物体，或者有物体改变包围盒尺寸
                if (batchLocalToWorldDirty || batchLocalBoundsDirty)
                {
                    var outputMinMax = new NativeArray<float3x2>(batchNumber,
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
                    var vmin = pMinMax[0];
                    var vmax = pMinMax[1];

                    outputMinMax.Dispose();
                    vMinMax.Dispose();

                    fixed (Bounds* pWorldBounds = &cachedWorldBounds)
                    {
                        var pCenter = (Vector3*)pWorldBounds;
                        var pExtents = pCenter + 1;
                        // center = lerp(min, max, 0.5)
                        // extents = max - min
                        Average3((float*)&vmin, (float*)&vmax, (float*)pCenter);
                        Minus3((float*)&vmax, (float*)&vmin, (float*)pExtents);
                        Mul3((float*)pExtents, 0.5f, (float*)pExtents);
                    }
                }

                // reset dirty
                instanceLocalOffsetDirty =
                batchLocalToWorldDirty =
                batchLocalBoundsDirty = false;

                UnsafeUtility.MemClear(batchLocalToWorldDirtyMask.GetUnsafePtr(), batchNumber);
            }

            if (instanceColorDirty)
            {
                SetData(colorsBuffer, instanceColorsBuffer.AsArray(), instanceNumber);
                instanceColorDirty = false;
            }

            // draw finally
            DrawMesh();
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
                Realloc(ref instanceColorsBuffer, instanceCapacity);
                Realloc(ref instanceTrsBuffer, instanceCapacity);

                Realloc(ref batchLocalToWorldBuffer, capacity);
                Realloc(ref batchLocalToWorldDirtyMask, capacity);
                Realloc(ref batchCountBuffer, capacity);

                Realloc(ref batchLocalBoundsBuffer, capacity);

                // 重分配，附加缓冲区
                matricesBuffer?.Dispose();
                matricesBuffer = new ComputeBuffer(instanceCapacity, sizeofFloat4x4,
                    ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
                props.SetBuffer(id_Matrices, matricesBuffer);

                colorsBuffer?.Dispose();
                colorsBuffer = new ComputeBuffer(instanceCapacity, sizeofFloat4,
                    ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
                props.SetBuffer(id_Colors, colorsBuffer);
            }

            batchCapacity = capacity;
            batchNumber = capacity;
            instanceNumber = instanceCapacity;
        }

        public void TrimExcess()
        {
            if (batchNumber == 0 && batchCapacity > 1024)
            {
                Setup(1024);
                batchNumber = 0;
                instanceNumber = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void Realloc<T>(ref NativeList<T> nativeList, int capacity) where T : unmanaged
        {
            if (nativeList.IsCreated)
            {
                nativeList.ResizeUninitialized(capacity);
                nativeList.Length = capacity;
                if (nativeList.Capacity > capacity)
                {
                    nativeList.TrimExcess();
                }
            }
            else
            {
                nativeList = new NativeList<T>(capacity, AllocatorManager.Persistent)
                {
                    Length = capacity
                };
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
            var ptr = (Color*)instanceColorsBuffer.GetUnsafePtr();
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
            var ptr = (Color*)instanceColorsBuffer.GetUnsafePtr();
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
            //batchLocalBoundsDirty = true;

            int instanceStart = batchIndex * batchSize;
            for (int i = 0; i < batchSize; i++)
            {
                int removeIdx = i + instanceStart;
                int lastIdx = i + instanceNumber;
                Erase(instanceLocalOffsetBuffer, removeIdx, lastIdx);
                Erase(instanceColorsBuffer, removeIdx, lastIdx);
                Erase(instanceTrsBuffer, removeIdx, lastIdx);
            }
            //instanceLocalOffsetDirty = true;

            // index changed...
            // batchNumber => batchIndex
        }

        public long UsedBufferMemory()
        {
            return UsedMemory(batchLocalToWorldBuffer)
                + UsedMemory(batchLocalToWorldDirtyMask)
                + UsedMemory(batchCountBuffer)
                + UsedMemory(batchLocalBoundsBuffer)
                + UsedMemory(instanceLocalOffsetBuffer)
                + UsedMemory(instanceTrsBuffer)
                + UsedMemory(instanceColorsBuffer)
                + UsedMemory(matricesBuffer)
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
            if (instanceNumber < 1) { return; }
            int subMeshCount = instanceMesh.subMeshCount;
            if (subMeshCount == 1)
            {
                Draw(0);
            }
            else
            {
                for (int i = 0; i < subMeshCount; i++)
                {
                    Draw(i);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Draw(int subMeshIndex)
        {
            var worldBounds = cachedWorldBounds;
            Graphics.DrawMeshInstancedProcedural(instanceMesh,
                subMeshIndex,
                instandedMaterial,
                worldBounds,
                instanceNumber,
                props,
                shadowCastingMode,
                recieveShadows,
                layer);
        }
    }
}