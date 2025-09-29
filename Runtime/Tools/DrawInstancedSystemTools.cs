using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using static Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility;
using static Unity.Mathematics.math;

namespace Com.Rendering
{
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    internal static class DrawInstancedSystemTools
    {
        public const int sizeofFloat4 = sizeof(float) * 4;
        public const int sizeofFloat4x4 = sizeof(float) * 16;
        public static readonly int id_Color = Shader.PropertyToID("_BaseColor");
        public static readonly int id_Colors = Shader.PropertyToID("_BaseColors");
        public static readonly int id_LocalToWorldBuffer = Shader.PropertyToID("_LocalToWorldBuffer");
        public static readonly int id_WorldToLocalBuffer = Shader.PropertyToID("_WorldToLocalBuffer");


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetData<T>(ComputeBuffer dst, NativeArray<T> src, int length, int sizeT) where T : unmanaged
        {
            var dstHandle = dst.BeginWrite<T>(0, length);
            UnsafeUtility.MemCpy(dstHandle.GetUnsafePtr(), src.GetUnsafeReadOnlyPtr(), length * sizeT);
            dst.EndWrite<T>(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetData(ComputeBuffer dst, NativeArray<float4x4> src, int length)
        {
            SetData(dst, src, length, sizeofFloat4x4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetData(ComputeBuffer dst, NativeArray<float4> src, int length)
        {
            SetData(dst, src, length, sizeofFloat4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetData<T>(GraphicsBuffer dst, NativeArray<T> src, int length) where T : unmanaged
        {
            dst.SetData(src, 0, 0, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Erase<T>(NativeArray<T> buffer, int index, int last) where T : unmanaged
        {
            //buffer[index] = buffer[last];
            var ptr = (T*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(buffer);
            ptr[index] = ptr[last];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Erase<T>(T* ptr, int index, int last) where T : unmanaged
        {
            //buffer[index] = buffer[last];
            ptr[index] = ptr[last];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Erase(float4x4* ptr, int index, int last)
        {
            UnsafeUtility.MemCpy(ptr + index, ptr + last, sizeofFloat4x4);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Erase(float4* ptr, int index, int last)
        {
            UnsafeUtility.MemCpy(ptr + index, ptr + last, sizeofFloat4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long UsedMemory<T>(NativeArray<T> list) where T : unmanaged
        {
            return list.IsCreated ? sizeof(T) * list.Length : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long UsedMemory<T>(NativeList<T> list) where T : unmanaged
        {
            return list.IsCreated ? sizeof(T) * list.Capacity : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long UsedMemory(ComputeBuffer buffer)
        {
            return buffer is null ? 0 : buffer.count * buffer.stride;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long UsedMemory(GraphicsBuffer buffer)
        {
            return buffer is null ? 0 : buffer.count * buffer.stride;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void CombineHash(int* seed, int hashValue)
        {
            // boost combine hash
            // seed ^= hash_value(v) + 0x9e3779b9 + (seed << 6) + (seed >> 2);
            uint useed = *(uint*)seed;
            uint v = *(uint*)&hashValue + 0x9e3779b9 + (useed << 6) + (useed >> 2);
            *seed ^= *(int*)&v;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool EqualsBounds([NoAlias] Bounds* a, [NoAlias] Bounds* b)
        {
            var aa = (float3x2*)a;
            var bb = (float3x2*)b;
            return aa->c0.Equals(bb->c0) && aa->c1.Equals(bb->c1);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsColor(in Color lhs, in Color rhs)
        {
            float num = lhs.r - rhs.r;
            float num2 = lhs.g - rhs.g;
            float num3 = lhs.b - rhs.b;
            float num4 = lhs.a - rhs.a;
            float num5 = num * num + num2 * num2 + num3 * num3 + num4 * num4;
            return num5 < 9.99999944E-11f;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool EqualsMatrix4x4(in Matrix4x4 lhs, in Matrix4x4 rhs)
        {
            return lhs.m00 == rhs.m00 && lhs.m01 == rhs.m01 && lhs.m02 == rhs.m02 && lhs.m03 == rhs.m03
                && lhs.m10 == rhs.m10 && lhs.m11 == rhs.m11 && lhs.m12 == rhs.m12 && lhs.m13 == rhs.m13
                && lhs.m20 == rhs.m20 && lhs.m21 == rhs.m21 && lhs.m22 == rhs.m22 && lhs.m23 == rhs.m23
                && lhs.m30 == rhs.m30 && lhs.m31 == rhs.m31 && lhs.m32 == rhs.m32 && lhs.m33 == rhs.m33;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Realloc<T>(ref T[] dst, int size)
        {
            if (dst is null) { dst = new T[size]; }
            else if (dst.Length != size) { Array.Resize(ref dst, size); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Realloc(ref TransformAccessArray transformAccessArray, int size)
        {
            if (transformAccessArray.isCreated)
            {
                transformAccessArray.capacity = size;
            }
            else
            {
                TransformAccessArray.Allocate(size, -1, out transformAccessArray);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Realloc<T>(ref NativeArray<T> nativeArray, int capacity) where T : unmanaged
        {
            if (nativeArray.IsCreated)
            {
                var newArray = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                UnsafeUtility.MemCpy(GetUnsafeBufferPointerWithoutChecks(newArray),
                    GetUnsafeBufferPointerWithoutChecks(nativeArray),
                    sizeof(T) * min(capacity, nativeArray.Length));
                nativeArray.Dispose();
                nativeArray = newArray;
            }
            else
            {
                nativeArray = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release<T>(ref NativeArray<T> array) where T : unmanaged
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Release(ref TransformAccessArray array)
        {
            if (array.isCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T* UnsafeGetPoint<T>(this ref NativeList<T> list) where T : unmanaged
        {
            return (T*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref list);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MultiplyPoint3x4(this in Matrix4x4 mul, in Vector3 point, ref Vector3 result)
        {
            result.x = mul.m00 * point.x + mul.m01 * point.y + mul.m02 * point.z + mul.m03;
            result.y = mul.m10 * point.x + mul.m11 * point.y + mul.m12 * point.z + mul.m13;
            result.z = mul.m20 * point.x + mul.m21 * point.y + mul.m22 * point.z + mul.m23;
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void GetBoundsVerticesUnsafe(this in Bounds b, in Vector3* vector8)
        {
            //Vector3 center = b.center, extents = b.extents;
            //Vector3 min = default, max = default;
            //Minus3((float*)&center, (float*)&extents, (float*)&min);
            //Plus3((float*)&center, (float*)&extents, (float*)&max);
            Vector3 min = b.min, max = b.max;

            float minX = min.x; float maxX = max.x;
            float minY = min.y; float maxY = max.y;
            float minZ = min.z; float maxZ = max.z;

            vector8[0] = float3(min.x, min.y, min.z);
            vector8[1] = float3(min.x, min.y, max.z);
            vector8[2] = float3(min.x, max.y, min.z);
            vector8[3] = float3(min.x, max.y, max.z);
            vector8[4] = float3(max.x, min.y, min.z);
            vector8[5] = float3(max.x, min.y, max.z);
            vector8[6] = float3(max.x, max.y, min.z);
            vector8[7] = float3(max.x, max.y, max.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilPow2(int x)
        {
            x -= 1;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x + 1;
        }

        [BurstCompile(CompileSynchronously = true,
           FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public struct MulTrsJobFor : IJobParallelFor
        {
            /* 计算每批次内所有绘制实例的变换
             */

            public int batchSize;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeArray<float4x4>.ReadOnly batchLocalToWorld;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeArray<bool>.ReadOnly batchLocalToWorldDirty;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeArray<int>.ReadOnly batchCount;
            [ReadOnly] public NativeArray<float4x4>.ReadOnly instLocalOffset;
            [WriteOnly] public NativeArray<float4x4> instLocalToWorld;
            [WriteOnly] public NativeArray<float4x4> instWorldToLocal;

            public unsafe void Execute(int index)
            {
                int batchIndex = index / batchSize;
                bool inRange = (index % batchSize) < batchCount[batchIndex];
                if (inRange)
                {
                    if (batchLocalToWorldDirty[batchIndex])
                    {
                        var localToWorld = mul(batchLocalToWorld[batchIndex], instLocalOffset[index]);
                        instLocalToWorld[index] = localToWorld;
                        instWorldToLocal[index] = inverse(localToWorld);
                    }
                }
                else
                {
                    instLocalToWorld[index] = 0;
                    instWorldToLocal[index] = 0;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public struct SyncBatchLocalToWorldFor : IJobParallelForTransform
        {
            public int length;
            public NativeArray<float4x4> batchLocalToWorldBuffer;

            [WriteOnly]
            public NativeArray<bool> batchLocalToWorldDirtyMask;

            [WriteOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<bool> anyDirty;

            public unsafe void Execute(int index, TransformAccess transform)
            {
                if (Hint.Likely(index < length))
                {
                    var currLocalToWorld = transform.localToWorldMatrix;
                    var currLocalToWorld4x4 = *(float4x4*)&currLocalToWorld;

                    if (!currLocalToWorld4x4.Equals(batchLocalToWorldBuffer[index]))
                    {
                        batchLocalToWorldBuffer[index] = currLocalToWorld4x4;
                        batchLocalToWorldDirtyMask[index] = true;
                        anyDirty[0] = true;
                    }
                }
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public struct CopyMatrixBufferFor : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int>.ReadOnly indirectIndexMap;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeArray<float4x4>.ReadOnly src;

            [WriteOnly] public NativeArray<float4x4> dst;

            public void Execute(int index)
            {
                dst[index] = src[indirectIndexMap[index]];
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public struct CopyVectorFieldsFor : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int>.ReadOnly indirectIndexMap;
            [NativeDisableParallelForRestriction]
            [ReadOnly] public NativeArray<float4>.ReadOnly src;

            [WriteOnly] public NativeArray<float4> dst;

            public void Execute(int index)
            {
                dst[index] = src[indirectIndexMap[index]];
            }
        }

        [BurstCompile(FloatPrecision = FloatPrecision.Standard,
           FloatMode = FloatMode.Fast,
           CompileSynchronously = true)]
        public struct TransposeBoundsFor : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4x4>.ReadOnly localToWorld;
            [ReadOnly] public NativeArray<float3x2>.ReadOnly inputLocalBounds;
            [WriteOnly] public NativeArray<float3x2> outputWorldMinMax;

            public unsafe void Execute(int index)
            {
                var bounds = inputLocalBounds[index];
                float3 center = bounds.c0, extents = bounds.c1;
                float3 bMin = center - extents, bMax = center + extents;

                float3 worldMin = float3(float.MaxValue), worldMax = float3(float.MinValue);
                var _localToWorld = localToWorld[index];
                MulAndMinMax(_localToWorld, bMin.x, bMin.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMin.x, bMin.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMin.x, bMax.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMin.x, bMax.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMax.x, bMin.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMax.x, bMin.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMax.x, bMax.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(_localToWorld, bMax.x, bMax.y, bMax.z, ref worldMin, ref worldMax);

                outputWorldMinMax[index] = float3x2(worldMin, worldMax);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void MulAndMinMax(in float4x4 matrix, float px, float py, float pz,
                ref float3 minP, ref float3 maxP)
            {
                var wp = mul(matrix, float4(px, py, pz, 1)).xyz;
                minP = min(wp, minP);
                maxP = max(wp, maxP);
            }
        }

        [BurstCompile(FloatPrecision = FloatPrecision.Standard,
          FloatMode = FloatMode.Fast,
          CompileSynchronously = true)]
        public struct BoundsMinMaxJobFor : IJobFor
        {
            [ReadOnly] public NativeArray<float3x2> srcMinMax;
            [NativeDisableParallelForRestriction]
            public NativeArray<float3> minMax2;

            public void Execute(int index)
            {
                var item = srcMinMax[index];
                float3 pMin = item.c0, pMax = item.c1;
                minMax2[0] = min(minMax2[0], pMin);
                minMax2[1] = max(minMax2[1], pMax);
            }
        }

        [BurstCompile(CompileSynchronously = true,
            FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public unsafe static void EncapsulateBounds(in int length,
            float3x2* minMaxArray,
            ref Bounds bounds)
        {
            float3 min = minMaxArray[0].c0, max = minMaxArray[0].c1;
            for (int i = 1; i < length; ++i)
            {
                min = math.min(min, minMaxArray[i].c0);
                max = math.max(max, minMaxArray[i].c1);
            }
            var extents = (max - min) * 0.5f;
            var center = min + extents;
            bounds.extents = extents; bounds.center = center;
        }

        [BurstCompile(CompileSynchronously = true,
            FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        public unsafe static void MinMax2Bounds(in float3 min, in float3 max, ref Bounds bounds)
        {
            var extents = (max - min) * 0.5f;
            var center = min + extents;
            bounds.extents = extents; bounds.center = center;
        }
    }
}