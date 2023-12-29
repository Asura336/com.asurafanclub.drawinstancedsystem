using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Com.Rendering
{
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public abstract class BaseGridInstances : MonoBehaviour
    {
        protected InstancedMeshRenderToken token;

        // 需要在继承类指定每维度的数量限制
        [SerializeField] protected Vector3 basePoint;
        [SerializeField] protected Vector3 distances;
        [SerializeField] protected Vector3 euler;
        [SerializeField] protected Vector3 scale = Vector3.one;

        protected virtual void Awake()
        {
            token = GetComponent<InstancedMeshRenderToken>();
        }

        private void OnEnable()
        {
            StartCoroutine(OnEnable_Coroutine());
        }
        IEnumerator OnEnable_Coroutine()
        {
            const int waitNumber = 3;

            yield return null;
            for (int i = 0; i < waitNumber; i++)
            {
                if (!token) { continue; }
                if (token.Active)
                {
                    ApplyMatricesAndBounds();
                    yield break;
                }
            }
        }

        protected Mesh GetMesh()
        {
            var dispatcher = InstancedMeshRenderDispatcher.FindInstanceOrNothing(token.DispatcherName);
            if (dispatcher != null)
            {
                return dispatcher.InstancedMesh;
            }
            return null;
        }

        public unsafe void ApplyMatricesAndBounds()
        {
            int length = XNumber * YNumber * ZNumber;
            using var matrices = new NativeArray<float4x4>(length, Allocator.TempJob,
                AllocOptions);
            InternalApplyMatrices(length, (float4x4*)matrices.GetUnsafePtr(),
                basePoint, math.int3(XNumber, YNumber, ZNumber), distances, euler, scale);
            var matricesAsMatrix4x4 = matrices.Reinterpret<Matrix4x4>();
            token.SetInstancesOffsets(matricesAsMatrix4x4);
            token.LocalBounds = CalculateLocalBounds(matricesAsMatrix4x4);
        }

        protected virtual unsafe Bounds CalculateLocalBounds(NativeArray<Matrix4x4> matrices)
        {
            var meshBounds = GetMesh() switch
            {
                Mesh _mesh => _mesh.bounds,
                _ => default(Bounds),
            };

            int length = matrices.Length;
            // 编辑器下分配的缓冲区清空内存，否则可能出现奇怪的结果
            using var minMaxBuffer = new NativeArray<float3x2>(length, Allocator.TempJob,
                AllocOptions);
            using var minMax2 = new NativeArray<float3>(2, Allocator.TempJob,
                AllocOptions);
            var job = default(JobHandle);
            job = new CalculateBoundsFor
            {
                matrices = matrices.Reinterpret<float4x4>().AsReadOnly(),
                meshBounds = math.float3x2(meshBounds.center, meshBounds.size),
                outputMinMax = minMaxBuffer,
            }.Schedule(length, job);
            job = new DrawInstancedSystemTools.BoundsMinMaxJobFor
            {
                srcMinMax = minMaxBuffer,
                minMax2 = minMax2,
            }.Schedule(length, job);
            job.Complete();

            var pMinMax = (float3*)minMax2.GetUnsafeReadOnlyPtr();

            Bounds o = default;
            DrawInstancedSystemTools.MinMax2Bounds(pMinMax[0], pMinMax[1], ref o);
            return o;
        }


        public abstract int XNumber { get; set; }
        public abstract int YNumber { get; set; }
        public abstract int ZNumber { get; set; }


        [BurstCompile]
        protected unsafe static void InternalApplyMatrices(int length, [WriteOnly] float4x4* matrices,
            in float3 center, in int3 numbers, in float3 distances, in float3 euler, in float3 scale)
        {
            var instRotation = quaternion.Euler(math.radians(euler));
            var trs = float4x4.TRS(default, instRotation, scale);
            int index = 0;
            for (int x = 0; x < numbers.x; x++)
            {
                for (int y = 0; y < numbers.y; y++)
                {
                    for (int z = 0; z < numbers.z; z++)
                    {
                        var vector = distances * math.int3(x, y, z);
                        var translation = vector + center;
                        trs.c3 = math.float4(translation, 1);
                        matrices[index] = trs;
                        index++;
                    }
                }
            }
        }

        /// <summary>
        /// 编辑器下分配的缓冲区清空内存，否则可能出现奇怪的结果
        /// </summary>
        NativeArrayOptions AllocOptions => Application.isPlaying
                ? NativeArrayOptions.UninitializedMemory
                : NativeArrayOptions.ClearMemory;

        [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
        struct CalculateBoundsFor : IJobFor
        {
            [ReadOnly] public float3x2 meshBounds;
            [ReadOnly] public NativeArray<float4x4>.ReadOnly matrices;
            [WriteOnly] public NativeArray<float3x2> outputMinMax;

            public void Execute(int index)
            {
                float3 center = meshBounds.c0, extents = meshBounds.c1;
                float3 bMin = center - extents, bMax = center + extents;

                float3 worldMin = math.float3(float.MaxValue), worldMax = math.float3(float.MinValue);
                var matrix = matrices[index];
                MulAndMinMax(matrix, bMin.x, bMin.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMin.x, bMin.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMin.x, bMax.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMin.x, bMax.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMax.x, bMin.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMax.x, bMin.y, bMax.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMax.x, bMax.y, bMin.z, ref worldMin, ref worldMax);
                MulAndMinMax(matrix, bMax.x, bMax.y, bMax.z, ref worldMin, ref worldMax);

                outputMinMax[index] = math.float3x2(worldMin, worldMax);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void MulAndMinMax(in float4x4 matrix, float px, float py, float pz,
                ref float3 minP, ref float3 maxP)
            {
                var wp = math.mul(matrix, math.float4(px, py, pz, 1)).xyz;
                minP = math.min(wp, minP);
                maxP = math.max(wp, maxP);
            }
        }
    }
}