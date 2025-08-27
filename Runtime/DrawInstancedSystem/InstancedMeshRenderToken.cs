using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static Com.Rendering.DrawInstancedSystemTools;

namespace Com.Rendering
{
    /// <summary>
    /// 保存绘制实例的信息，按名称索引调度器并提交信息给调度器。
    ///修改内容后由调度器再次分配。
    /// </summary>
    [AddComponentMenu("Com/Rendering/绘制实例符号")]
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [BurstCompile(CompileSynchronously = true,
        FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public sealed class InstancedMeshRenderToken : MonoBehaviour
    {
        const int defaultBufferSize = 64;
        const int minBatchSize = 1;

        [SerializeField] string dispatcherName;
        [SerializeField] Bounds localBounds;
        [SerializeField] Color color = Color.white;
        internal Matrix4x4[] localOffsets;
        /// <summary>
        /// 预期分配的缓冲区长度，数组长度不小于这个值
        /// </summary>
        [SerializeField] int batchSize = defaultBufferSize;
        /// <summary>
        /// 实际绘制的实例个数，这个数小于缓冲区长度
        /// </summary>
        [SerializeField] int count;

        [SerializeField] int virtualBatchIndex;
        Matrix4x4 cachedLocalToWorld = Matrix4x4.identity;

        Transform cachedTransform;
        bool instanceUpdated;
        bool volumeUpdated;
        bool materialPropertyUpdated;

        bool m_forceRenderingOff = false;
        int m_bacthIndex = -1;

        bool enableCalled = false;

        private void Awake()
        {
            InstancedMeshRenderDispatcher.OnDispatcherEnabled += Dispatcher_OnDispatcherEnabled;
            InstancedMeshRenderDispatcher.OnBeforeDispatcherDisable += Dispatcher_OnBeforeDispatcherDisable;

            cachedTransform = transform;
            //batchSize = defaultBufferSize;
            Realloc(ref localOffsets, batchSize);

            // 第一次，初始化缓冲区内容
            InitLocalOffsets();
        }
        private void Start()
        {
            if (!enableCalled)
            {
                Wakeup();
            }
        }
        private void Dispatcher_OnDispatcherEnabled(string dispatcherName)
        {
            //print($"{gameObject.name} listened {dispatcherName} enabled");
            Wakeup();
        }
        private void Dispatcher_OnBeforeDispatcherDisable(string dispatcherName)
        {
            //print($"{gameObject.name} listened {dispatcherName} disabled");
            InstancedMeshRenderDispatcher.RemoveForce(this);
        }

        private void OnEnable()
        {
            Active = true;
            Wakeup();
            enableCalled = true;
        }

        private void OnDestroy()
        {
            InstancedMeshRenderDispatcher.OnDispatcherEnabled -= Dispatcher_OnDispatcherEnabled;
            InstancedMeshRenderDispatcher.OnBeforeDispatcherDisable -= Dispatcher_OnBeforeDispatcherDisable;
        }

        private unsafe void OnDrawGizmosSelected()
        {
            /* Bounds
             *   m_Center : float3
             *   m_Extents : float3
             */
            var lb = localBounds;
            float* hExtents = (float*)&lb + 3;

            // if Bounds.size != 0:
            if (hExtents[0] != 0 && hExtents[1] != 0 && hExtents[2] != 0)
            {
                var vs8 = stackalloc Vector3[8];
                lb.GetBoundsVerticesUnsafe(vs8);
                var localToWorld = transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 worldVec = default;
                    localToWorld.MultiplyPoint3x4(vs8[i], ref worldVec);
                    vs8[i] = worldVec;
                }

                /* 0 0 0
                 * 0 0 1
                 * 0 1 0
                 * 0 1 1
                 * 
                 * 1 0 0
                 * 1 0 1
                 * 1 1 0
                 * 1 1 1
                 */

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(vs8[0], vs8[1]);
                Gizmos.DrawLine(vs8[2], vs8[3]);
                Gizmos.DrawLine(vs8[4], vs8[5]);
                Gizmos.DrawLine(vs8[6], vs8[7]);

                Gizmos.DrawLine(vs8[0], vs8[2]);
                Gizmos.DrawLine(vs8[1], vs8[3]);
                Gizmos.DrawLine(vs8[4], vs8[6]);
                Gizmos.DrawLine(vs8[5], vs8[7]);

                Gizmos.DrawLine(vs8[0], vs8[4]);
                Gizmos.DrawLine(vs8[1], vs8[5]);
                Gizmos.DrawLine(vs8[2], vs8[6]);
                Gizmos.DrawLine(vs8[3], vs8[7]);
            }
        }

        void Wakeup()
        {
            batchSize = batchSize < 2
                ? minBatchSize
                : Mathf.Max(minBatchSize, CeilToPow2(count));
            // set dirty...
            instanceUpdated = true;
            volumeUpdated = true;
            materialPropertyUpdated = true;
            CheckDispatch();
        }

        private void OnDisable()
        {
            Active = false;
            InstancedMeshRenderDispatcher.RemoveForce(this);
        }

        /// <summary>
        /// 向全局的调度器提交自身，如果有改动，由调度器进一步处理
        /// </summary>
        [ContextMenu("check")]
        public void CheckDispatch()
        {
            if (Application.isEditor)
            {
                if (InstancedMeshRenderDispatcher.FindInstanceOrNothing(dispatcherName) == null)
                {
                    Debug.LogWarning($"调度器 \"{dispatcherName}\" 没有加入场景或者没有激活");
                    return;
                }
            }
            InstancedMeshRenderDispatcher.Evaluate(this);
        }

        /// <summary>
        /// 将当前缓冲区内容置为 <see cref="Matrix4x4.identity"/>，
        /// 可在调用此方法之前调整批次大小。
        ///写入值后要看到改动，需要手动调用 <see cref="UpdateLocalOffsets"/>
        /// </summary>
        public unsafe void InitLocalOffsets()
        {
            fixed (Matrix4x4* pLocalOffs = localOffsets)
            {
                var identity = Matrix4x4.identity;
                UnsafeUtility.MemCpyReplicate(pLocalOffs, &identity, sizeofFloat4x4, localOffsets.Length);
            }
        }

        /// <summary>
        /// 设置此批次仅有一个实体，改动会立即生效，通常在编辑期间调用。
        /// </summary>
        public void MakeSingleInstance()
        {
            batchSize = 1;
            count = 1;
            InitLocalOffsets();
            instanceUpdated = true;
            CheckDispatch();
        }

        /// <summary>
        /// 可以按序号读写某个实例的本地空间变换。
        ///写入值后要看到改动，需要手动调用 <see cref="UpdateLocalOffsets"/> 和 <see cref="CheckDispatch"/>
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public ref Matrix4x4 LocalOffsetRefAt(int index) => ref localOffsets[index];

        /// <summary>
        /// 设置脏标记，表示绘制实例的信息已写入
        /// </summary>
        public void UpdateLocalOffsets() => instanceUpdated = true;

        /// <summary>
        /// 清空索引在 <see cref="Count"/> 及之后的绘制实例本地空间变换
        /// </summary>
        public unsafe void ClearLocalOffsetsOutOfCount()
        {
            if (localOffsets != null)
            {
                fixed (Matrix4x4* pLocalOffsets = localOffsets)
                {
                    long size = sizeofFloat4x4 * (localOffsets.Length - count);
                    UnsafeUtility.MemClear(pLocalOffsets + count, size);
                }
                instanceUpdated = true;
            }
        }

        /// <summary>
        /// 压缩缓冲区内存，随后需要调用 <see cref="CheckDispatch"/>
        ///才会按照当前的容量重新由调度器分配。
        /// </summary>
        public void TrimExcess()
        {
            int batchNums = Mathf.Max(minBatchSize, CeilToPow2(count));
            if (batchNums < localOffsets.Length * 0.9f)
            {
                Realloc(ref localOffsets, batchNums);
                batchSize = batchNums;
                instanceUpdated = true;
            }
        }

        /// <summary>
        /// 对应的调度器名称，需要保证对应的调度器在此实体之前构造完毕。
        ///写入值后要看到改动，需要手动调用 <see cref="UpdateLocalOffsets"/>
        /// </summary>
        public string DispatcherName
        {
            get => dispatcherName ?? string.Empty;
            set
            {
                if (!(dispatcherName ?? string.Empty).Equals(value, StringComparison.Ordinal))
                {
                    dispatcherName = value;
                }
            }
        }

        /// <summary>
        /// 绘制的实例个数。绘制的网格和材质取决于调度器持有的网格和材质。
        ///写入值后要看到改动，需要手动调用 <see cref="CheckDispatch"/>
        /// </summary>
        public int Count
        {
            get => count;
            set
            {
                if (count != value)
                {
                    batchSize = Mathf.Max(minBatchSize, CeilToPow2(value));
                    // realloc?
                    if (batchSize > Capacity)
                    {
                        Realloc(ref localOffsets, batchSize);
                    }

                    count = value;
                    instanceUpdated = true;
                }
            }
        }

        /// <summary>
        /// 绘制批次最大实例数，期待是2的次幂，设计预期 [16, 32, 64, 128, ..., 2048]
        /// </summary>
        public int BatchSize => batchSize;

        public bool IsSingleInstance => batchSize == 1 && count == 1
            && Capacity > 0 && EqualsMatrix4x4(localOffsets[0], Matrix4x4.identity);

        public int Capacity => localOffsets is null ? 0 : localOffsets.Length;

        public Matrix4x4 LocalToWorld => cachedTransform.localToWorldMatrix;
        public void ReadLocalToWorld(ref Matrix4x4 localToWorld)
        {
            localToWorld = cachedTransform.localToWorldMatrix;
        }

        public bool Active { get; private set; }

        /// <summary>
        /// Same as <see cref="Renderer.forceRenderingOff"/>
        /// </summary>
#pragma warning disable IDE1006 // 命名样式
        public bool forceRenderingOff
#pragma warning restore IDE1006 // 命名样式
        {
            get => m_forceRenderingOff;
            set
            {
                if (m_forceRenderingOff != value)
                {
                    m_forceRenderingOff = value;
                    CheckDispatch();
                }
            }
        }

        /// <summary>
        /// 本地空间包围盒，这个值影响相机剪裁，确保包围盒能包裹所有绘制实例。
        /// </summary>
        public Bounds LocalBounds
        {
            get => localBounds;
            set
            {
                var prevB = localBounds;
                unsafe
                {
                    if (!EqualsBounds(&prevB, &value))
                    {
                        localBounds = value;
                        volumeUpdated = true;
                    }
                }
            }
        }

        public ref readonly Bounds ReadLocalBounds => ref localBounds;

        /// <summary>
        /// 实例的颜色，简单起见为单个批次应用相同的颜色。
        /// </summary>
        public Color InstanceColor
        {
            get => color;
            set
            {
                if (!EqualsColor(value, color))
                {
                    color = value;
                    materialPropertyUpdated = true;
                }
            }
        }

        /// <summary>
        /// 传递给缓冲区的色值，取决于项目颜色空间
        /// </summary>
        public Color InstanceColorGamma => QualitySettings.activeColorSpace switch
        {
            ColorSpace.Gamma => color.gamma,
            ColorSpace.Linear => color.linear,
            _ => color
        };

        internal bool VolumeUpdated
        {
            get
            {
                bool o = volumeUpdated;
                volumeUpdated = false;
                return o;
            }
        }

        internal bool InstanceUpdated
        {
            get
            {
                bool o = instanceUpdated;
                instanceUpdated = false;
                return o;
            }
        }

        internal bool MaterialPropertyUpdated
        {
            get
            {
                bool o = materialPropertyUpdated;
                materialPropertyUpdated = false;
                return o;
            }
        }

        internal int BatchIndex { get => m_bacthIndex; set => m_bacthIndex = virtualBatchIndex = value; }
    }
}