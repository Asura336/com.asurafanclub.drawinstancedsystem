using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static Com.Rendering.DrawInstancedSystemTools;

namespace Com.Rendering
{
    /// <summary>
    /// 绘制实例的调度器，以名称索引，需要在初始化阶段主动构造。
    /// </summary>
    [AddComponentMenu("Com/Rendering/绘制实例调度器")]
    public sealed class InstancedMeshRenderDispatcher : MonoBehaviour
    {
        static readonly Dictionary<string, InstancedMeshRenderDispatcher> sharedInstances =
            new Dictionary<string, InstancedMeshRenderDispatcher>();

        /// <summary>
        /// 在按名称查找之前，需要确保实例已经生成。
        ///设计预期的做法是将调度器做成预制体，在初始化阶段构造，后续依赖调度器的物件再加载。
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static InstancedMeshRenderDispatcher FindInstanceOrNothing(string name)
        {
            return sharedInstances.TryGetValue(name, out var v) ? v : null;
        }

        public static void TrimExcess()
        {
            foreach (var v in sharedInstances.Values)
            {
                v.InstanceTrimExcess();
            }
        }

        /// <summary>
        /// 内存统计，按非托管缓冲区消耗 
        /// </summary>
        /// <returns></returns>
        public static long GetNativeUsedMemory()
        {
            long mem = 0;
            foreach (var aliveDispatcher in sharedInstances.Values)
            {
                var levels = aliveDispatcher.levels;
                int count = levels.Length;
                for (int i = 0; i < count; i++)
                {
                    if (levels[i] is null) { continue; }
                    var sys = levels[i].system;
                    mem += sys.UsedBufferMemory();
                }
            }
            return mem;
        }


        static readonly int[] batchSizeLevels = new int[]
        {
            1, 2, 4, 8, 16, 32, 64, 128,
            256, 512, 1024, 2048, 4096,
        };
        static int BatchSizeToLevel(InstancedMeshRenderToken token) => token.BatchSize switch
        {
            1 => 0,
            2 => 1,
            4 => 2,
            8 => 3,
            16 => 4,
            32 => 5,
            64 => 6,
            128 => 7,
            256 => 8,
            512 => 9,
            1024 => 10,
            2048 => 11,
            4096 => 12,
            _ => DefaultSystemIndex(token)
        };
        static int DefaultSystemIndex(InstancedMeshRenderToken token)
        {
            if (Debug.isDebugBuild)
            {
                Debug.LogWarning($"[{token.DispatcherName}] size = {token.BatchSize} not power of 2 or out of range");
            }
            return 10;  // 1024
        }


        struct TokenInfo : IEquatable<TokenInfo>
        {
            public InstancedMeshRenderDispatcher savedDispatcher;
            /// <summary>
            /// token 按照 batchSize 对应的 system 序号
            /// </summary>
            public int systemLevel;
            /// <summary>
            /// token 在某个 system 中的序号，和在调度器缓存数组的索引相同
            /// </summary>
            public int batchIndex;

            public TokenInfo(InstancedMeshRenderToken token)
            {
                savedDispatcher = FindInstanceOrNothing(token.DispatcherName);
                systemLevel = BatchSizeToLevel(token);
                batchIndex = token.BatchIndex;
            }

            public readonly bool Equals(TokenInfo other) => savedDispatcher == other.savedDispatcher
                && systemLevel == other.systemLevel
                && batchIndex == other.batchIndex;
        }
        static readonly Dictionary<InstancedMeshRenderToken, TokenInfo> savedTokenInfos =
            new Dictionary<InstancedMeshRenderToken, TokenInfo>();

        /// <summary>
        /// <see cref="InstancedMeshRenderToken">token</see> 激活、休眠等动作都会触发重新评估。
        ///先检查 token 当前状态（在活动、预期的绘制调度器、预期的批次大小等），再比对缓存的状态副本，
        ///决定下一步操作（添加、移除、覆写）。
        /// </summary>
        /// <param name="token"></param>
        public static void Evaluate(InstancedMeshRenderToken token)
        {
            bool exist = savedTokenInfos.TryGetValue(token, out var savedInfo);
            bool currentAlive = token && !token.forceRenderingOff && token.enabled && token.gameObject.activeInHierarchy;
            if (currentAlive)
            {
                var thisTokenInfo = new TokenInfo(token);
                var targetDispatcher = thisTokenInfo.savedDispatcher;
                if (!targetDispatcher)
                {
                    throw new MissingReferenceException($"没有找到调度器 \"{token.DispatcherName}\"");
                }

                if (exist && thisTokenInfo.Equals(savedInfo))
                {
                    WriteToRenderSystem(savedInfo.savedDispatcher.levels[savedInfo.systemLevel].system,
                        token, thisTokenInfo.batchIndex);
                    // 不需要改动批次，直接退出
                    return;
                }

                if (exist)
                {
                    // remove anyway...
                    ReleaseToken(savedInfo);
                }

                // then add...
                var systemWithTokens = targetDispatcher.GetSystemAtLevel(thisTokenInfo.systemLevel);
                systemWithTokens.AppendToken(token);
                WriteToRenderSystem(systemWithTokens.system, token, token.BatchIndex);

                // finally
                savedTokenInfos[token] = new TokenInfo
                {
                    savedDispatcher = targetDispatcher,
                    systemLevel = thisTokenInfo.systemLevel,
                    batchIndex = token.BatchIndex
                };
            }
            else
            {
                // just remove
                if (exist)
                {
                    ReleaseToken(savedInfo);
                    savedTokenInfos.Remove(token);
                }
            }
        }

        static void WriteToRenderSystem(InstancedMeshRenderSystem renderSystem, InstancedMeshRenderToken token, int batchIndex)
        {
            renderSystem.WriteBatchLocalToWorldAt(batchIndex, token.LocalToWorld);
            renderSystem.WriteBatchCountAt(batchIndex, token.Count);
            renderSystem.WriteLocalBoundsAt(batchIndex, token.LocalBounds);
            renderSystem.WriteLocalOffsetAt(batchIndex, token.localOffsets, 0, token.Count);
            renderSystem.WriteBatchColorAt(batchIndex, token.InstanceColorGamma);
            // if other material properties...
            //...
        }

        static void ReleaseToken(TokenInfo savedInfo)
        {
            var savedDispatcher = savedInfo.savedDispatcher;
            if (savedDispatcher)
            {
                var renderSystem = savedDispatcher.levels[savedInfo.systemLevel];
                renderSystem.EraseTokenAt(savedInfo.batchIndex);
            }
        }


        [Header("字段在预制体中指定，应视为运行期常量")]
        [SerializeField] string dispatcherName;
        [SerializeField] Mesh instanceMesh;
        [SerializeField] Material instanceMaterial;
        [SerializeField] string renderType = null;
        [SerializeField] int defaultRenderSystemCapacity = 64;
        [Header("每个调度器使用固定的阴影和绘制层选项，如果需要变体，实例化额外的预制体")]
        [SerializeField] ShadowCastingMode shadowCastingMode = ShadowCastingMode.On;
        [SerializeField] bool recieveShadows = true;
        [SerializeField] int layer = 0;

        sealed class SystemWithTokens : IDisposable
        {
            const int defaultTokenCapacity = 1024;

            public readonly InstancedMeshRenderSystem system;
            public InstancedMeshRenderToken[] savedTokens = new InstancedMeshRenderToken[defaultTokenCapacity];
            public Matrix4x4[] tokenLocalToWorlds = new Matrix4x4[defaultTokenCapacity];
            public int count = 0;

            public float accessTimeSinceStartup = 0;

            public SystemWithTokens(InstancedMeshRenderSystem system)
            {
                this.system = system;
                accessTimeSinceStartup = Time.realtimeSinceStartup;
            }
            ~SystemWithTokens()
            {
                Dispose(false);
            }

            public void AppendToken(InstancedMeshRenderToken token)
            {
                if (DisposeValue) { return; }

                Assert.AreEqual(token.BatchSize, system.batchSize);

                int addIndex = system.BatchNumber;
                Assert.AreEqual(addIndex, count);
                Assert.AreEqual(token.BatchIndex, -1);

                //Debug.Log($"add[level = {BatchSizeToLevel(system.batchSize)}]：{count}");

                // grows it up
                if (addIndex + 1 > system.BatchCapacity)
                {
                    system.Setup(system.BatchCapacity << 1);
                    //Debug.Log($"level[{BatchSizeToLevel(system.batchSize)}] grows => {system.BatchCapacity}");
                }
                if (system.BatchCapacity > savedTokens.Length)
                {
                    int newCapacity = system.BatchCapacity;
                    Realloc(ref savedTokens, newCapacity);
                    Realloc(ref tokenLocalToWorlds, newCapacity);
                }
                system.BatchNumber = addIndex + 1;
                savedTokens[count] = token;
                tokenLocalToWorlds[count] = token.LocalToWorld;
                token.BatchIndex = addIndex;
                count++;

                accessTimeSinceStartup = Time.realtimeSinceStartup;
            }

            public void EraseTokenAt(int batchIndex)
            {
                if (DisposeValue) { return; }

                Assert.AreEqual(system.BatchNumber, count);
                Assert.IsNotNull(savedTokens[count - 1]);
                Assert.IsTrue(batchIndex < count);

                //Debug.Log($"move[level = {BatchSizeToLevel(system.batchSize)}]：{batchIndex}/{count}");

                var token = savedTokens[batchIndex];
                int index = token.BatchIndex;
                Assert.AreEqual(index, batchIndex);
                system.EraseAt(index);
                count--;
                Assert.AreEqual(count, system.BatchNumber);
                savedTokens[index] = savedTokens[count];
                savedTokens[index].BatchIndex = index;
                savedTokens[count] = default;
                tokenLocalToWorlds[index] = tokenLocalToWorlds[count];
                token.BatchIndex = -1;

                // 同步全局记录，因为擦除的做法影响了其他元素的顺序
                if (savedTokens[index] is InstancedMeshRenderToken existToken)
                {
                    var record = savedTokenInfos[existToken];
                    record.batchIndex = index;
                    savedTokenInfos[existToken] = record;
                    existToken.CheckDispatch();
                }

                accessTimeSinceStartup = Time.realtimeSinceStartup;
            }

            /// <summary>
            /// 控制持有的<see cref="InstancedMeshRenderToken">绘制实例符号</see>压缩内存，
            ///再控制持有的<see cref="system">绘制实例系统</see>压缩内存
            /// </summary>
            public void TrimExcess()
            {
                for (int iToken = count - 1; iToken >= 0; iToken--)
                {
                    var token = savedTokens[iToken];
                    token.TrimExcess();
                    if (token.InstanceUpdated)
                    {
                        token.CheckDispatch();
                    }
                }
                system.TrimExcess();
            }

            /// <summary>
            /// 轮询期间调用，传递时间戳，如果有一段时间（150s，两分半钟）没有增删对象，压缩内存并刷新访问时间
            /// </summary>
            /// <param name="timer"></param>
            public void TrimExcessOnUpdate()
            {
                // 如果有一段时间（150s，两分半钟）没有增删对象，调用一次出让内存
                const float autoTrimExcessTime = 150;

                float timer = Time.realtimeSinceStartup;
                if (timer - accessTimeSinceStartup > autoTrimExcessTime)
                {
                    TrimExcess();
                    accessTimeSinceStartup = timer;
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            void Dispose(bool disposing)
            {
                if (DisposeValue) { return; }

                if (disposing)
                {
                    savedTokens = null;
                    tokenLocalToWorlds = null;
                }
                system.Dispose();
                DisposeValue = true;
            }

            public bool DisposeValue { get; private set; }
        }

        public bool Valid { get; private set; }

        readonly SystemWithTokens[] levels = new SystemWithTokens[batchSizeLevels.Length];
        SystemWithTokens GetSystemAtLevel(int level)
        {
            if (levels[level] is null)
            {
                int batchSize = batchSizeLevels[level];
                var sys = string.IsNullOrEmpty(renderType)
                    ? new InstancedMeshRenderSystem(instanceMesh, instanceMaterial, batchSize)
                    : new InstancedMeshRenderSystem(instanceMesh, instanceMaterial, renderType, batchSize);
                sys.shadowCastingMode = shadowCastingMode;
                sys.recieveShadows = recieveShadows;
                sys.layer = layer;

                var o = new SystemWithTokens(sys);
                o.system.Setup(defaultRenderSystemCapacity);
                o.system.BatchNumber = 0;
                levels[level] = o;
            }
            return levels[level];
        }

        private void Awake()
        {
            if (FindInstanceOrNothing(dispatcherName))
            {
                throw new ArgumentException($"调度器 \"{dispatcherName}\" 名称冲突或者重复注册");
            }
            sharedInstances.Add(dispatcherName, this);
            if (!transform.parent)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            sharedInstances.Remove(dispatcherName);
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                levels[i]?.Dispose();
            }
        }

        private void Update()
        {
            int sysLen = levels.Length;
            for (int i = 0; i < sysLen; i++)
            {
                var item = levels[i];
                if (item != null)
                {
                    var system = item.system;
                    var tokenLocalToWorlds = item.tokenLocalToWorlds;
                    int count = item.count;
                    for (int ti = 0; ti < count; ti++)
                    {
                        var token = item.savedTokens[ti];
                        //if (token is null) { continue; }
                        int batchIndex = token.BatchIndex;

                        Matrix4x4 tokenLocalToWorld = default;
                        token.GetLocalToWorld(ref tokenLocalToWorld);
                        //var tokenLocalToWorld = token.LocalToWorld;
                        if (!EqualsMatrix4x4(tokenLocalToWorld, tokenLocalToWorlds[ti]))
                        {
                            tokenLocalToWorlds[ti] = tokenLocalToWorld;
                            system.WriteBatchLocalToWorldAt(batchIndex, tokenLocalToWorld);
                        }

                        // 形状变化的情况在调用 Evaluate() 里处理
                        //if (token.InstanceUpdated)
                        //{
                        //    system.WriteBatchCountAt(batchIndex, token.Count);
                        //    system.WriteLocalOffsetAt(batchIndex, token.localOffsets, 0, token.Count);
                        //}
                        if (token.VolumeUpdated)
                        {
                            system.WriteLocalBoundsAt(batchIndex, token.LocalBounds);
                        }
                        if (token.MaterialPropertyUpdated)
                        {
                            system.WriteBatchColorAt(batchIndex, token.InstanceColorGamma);
                            // if other properties...
                        }
                    }
                    system.Update();

                    // 如果有一段时间（150s，两分半钟）没有增删对象，调用一次出让内存
                    item.TrimExcessOnUpdate();
                }
            }
        }

        public void InstanceTrimExcess()
        {
            /* 先压缩连接的 token 的缓存
             *   token 的 batchSize 可能改动
             *   可能会改动 system 的缓冲区尺寸
             * 再压缩已实例化的 system 的缓存
             */

            // 倒序，也就是先压缩每批次数目更多的缓冲区
            for (int iLevels = levels.Length - 1; iLevels >= 0; iLevels--)
            {
                levels[iLevels]?.TrimExcess();
            }
        }

        public Mesh InstancedMesh => instanceMesh;
        public Material InstancedMaterial => instanceMaterial;

        public ShadowCastingMode ShadowCastingMode
        {
            get => shadowCastingMode;
            set => shadowCastingMode = value;
        }

        public bool RecieveShadows
        {
            get => recieveShadows;
            set => recieveShadows = value;
        }
    }
}