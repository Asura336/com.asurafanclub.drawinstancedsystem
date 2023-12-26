using System.Collections;
using UnityEngine;

namespace Com.Rendering.Test
{
    public class TestInstancedSystem : MonoBehaviour
    {
        /* 指定唯一性的依据？
         *   如果使用材质实例。。。
         *   材质实例 + 一些可改的属性
         * 
         * 调度器 => 持有一个材质实例，可以构造若干 system，表示从这个材质可以绘制的实例
         *          调度器使用字符串名称寻址，应该有静态方法传递名称返回实例或者异常
         *          
         * system => 持有网格引用、材质实例和若干缓冲区，最终绘制若干实例，初始化决定每批次大小，运行期可以扩张批次数目或者收缩让出内存
         *           
         * token => 挂在节点上表示一个绘制批次，记录调度器名称和最大实例数，以此为索引依据。
         *          
         * 
         * 使用体验
         *   预先指定调度器作为预制体，调度器持有材质和网格实例，初始化阶段构造调度器
         *   调度器构造后可以按名称寻址，名称在运行前指定，视为运行期常量
         *   调度器持有若干 system，每个 system 是具有不同批次大小的实例，懒加载
         *     64, 128, 256, 512, 1024, 2048
         *   
         *   挂在节点上的 token 记录寻址需要的调度器（按名称）和最大实例数，运行期从调度器注册和注销
         *     如果运行期最大实例数变化？
         *       变大或者变小
         *       先考虑设置最大实例数时立刻评估迁移到不同的 system
         *     
         *     调度器保存的信息
         *       system[]，定长数组，成员是不同批次大小的 system 实例
         *       dic{ 成功注册的 token, { token 连接的 system, token 在 system 中的序号 } }
         *       
         *     调度器行为
         *       static void Evaluate(Token):
         *         检查 token 是否是成功注册的实例
         *           如果不是，注册
         *           如果是，检查 token 当前是否需要重新定向（检查对应调度器名称，检查批次最大实例）
         *             如果是，注销，然后重新注册
         *       
         *     token 保存的信息
         *       调度器名称 
         *       批次最大实例
         * 
         * 
         * 如何处理绘制层、投射阴影和接收阴影等的变体？
         *   比如生成预览图时
         *   运行期生成一个临时的调度器？
         * 
         * 
         * 持有绘制实体的脚本（keeper）应该有什么
         * 调度如何连接持有绘制批次的实体（keeper）
         * 
         */

        [SerializeField] Mesh mesh;
        [SerializeField] Material matSrc;

        InstancedMeshRenderSystem sys;
        Transform[] handles;

        Matrix4x4[] localOffsetBuffer;

        Bounds batchBounds;
        Vector3[] batchOffsets;
        Color[] batchColors;

        [SerializeField] [Range(1, 50)] int batchX = 5;
        [SerializeField] [Range(1, 50)] int batchZ = 5;
        int batchNumber;

        private void Start()
        {
            batchNumber = batchX * batchZ;
            int batchSize = 128;

            sys = new InstancedMeshRenderSystem(mesh, matSrc, batchSize);
            sys.Setup(batchNumber);
            localOffsetBuffer = new Matrix4x4[batchSize];

            int countX = 5, countY = 5, countZ = 5;
            float space = 1.5f;
            Vector3 pivotBoundingSize = new Vector3(space * (countX - 1),
                space * (countY - 1),
                space * (countZ - 1));
            Vector3 pivot = -(pivotBoundingSize / 2);
            Matrix4x4 trs = Matrix4x4.TRS(default, Quaternion.identity, Vector3.one);
            Vector4 translation = new Vector4(0, 0, 0, 1);
            int index = 0;
            for (int x = 0; x < countX; x++)
            {
                for (int y = 0; y < countY; y++)
                {
                    for (int z = 0; z < countZ; z++)
                    {
                        translation.x = pivot.x + x * space;
                        translation.y = pivot.y + y * space;
                        translation.z = pivot.z + z * space;
                        trs.SetColumn(3, translation);
                        localOffsetBuffer[index] = trs;
                        index++;
                    }
                }
            }

            Vector3 size = pivotBoundingSize + Vector3.one;
            batchBounds = new Bounds(Vector3.zero, size);
            handles = new Transform[batchNumber];
            int batchIndex = 0;
            Transform thisTransform = transform;
            Vector3 batchStartPoint = Vector3.zero;
            Vector3 batchPositionOffset = size + Vector3.one * 0.5f;
            batchOffsets = new Vector3[batchNumber];
            batchColors = new Color[batchNumber];
            for (int x = 0; x < batchX; x++)
            {
                for (int z = 0; z < batchZ; z++)
                {
                    Transform t = new GameObject().transform;
                    t.SetParent(thisTransform);
                    t.localPosition = batchStartPoint + Vector3.Scale(batchPositionOffset, new Vector3(x, 0, z));
                    t.localRotation = Quaternion.identity;
                    handles[batchIndex] = t;

                    var itemColor = new Color((float)x / batchX, 0.5f, (float)z / batchZ);

                    batchOffsets[batchIndex] = t.localPosition;
                    batchColors[batchIndex] = itemColor;

                    sys.WriteLocalOffsetAt(batchIndex, localOffsetBuffer);
                    sys.WriteLocalBoundsAt(batchIndex, batchBounds);
                    sys.WriteBatchColorAt(batchIndex, itemColor);
                    batchIndex++;
                }
            }

            StartCoroutine(Act(batchNumber, new WaitForSeconds(0.2f)));


            //// test...
            //var nList = new NativeList<int>(256, AllocatorManager.Temp) { Length = 256 };
            //Assert.IsTrue(nList.Capacity == 256, nList.Capacity.ToString());
            //Realloc(ref nList, 512);
            //Assert.IsTrue(nList.Capacity == 512, nList.Capacity.ToString());
            //Realloc(ref nList, 64);
            //Assert.IsTrue(nList.Capacity == 64, nList.Capacity.ToString());
            //nList.Dispose();
        }
        //static void Realloc<T>(ref NativeList<T> nativeList, int capacity) where T : unmanaged
        //{
        //    if (nativeList.IsCreated)
        //    {
        //        nativeList.ResizeUninitialized(capacity);
        //        if (nativeList.Capacity > capacity)
        //        {
        //            nativeList.TrimExcess();
        //        }
        //    }
        //    else
        //    {
        //        nativeList = new NativeList<T>(capacity, AllocatorManager.Persistent);
        //    }
        //    nativeList.Length = capacity;
        //}

        private void OnDestroy()
        {
            sys.Dispose();
        }

        private void Update()
        {
            //for (int i = 0; i < batchNumber; i++)
            //{
            //    if (handles[i].hasChanged)
            //    {
            //        sys.WriteBatchLocalToWorldAt(i, handles[i].localToWorldMatrix);
            //        handles[i].hasChanged = false;
            //    }
            //}
            sys.Update();
        }

        IEnumerator Act(int count, YieldInstruction delay)
        {
            for (int i = 0; i < count; i++)
            {
                sys.WriteBatchLocalToWorldAt(i, handles[i].localToWorldMatrix);
                sys.WriteBatchCountAt(i, 125);
            }
            for (; ; )
            {
                for (int i = 0; i < count; i++)
                {
                    int removeIndex = count - i - 1;
                    sys.EraseAt(removeIndex);
                    yield return delay;
                }
                Matrix4x4 trs = Matrix4x4.TRS(default, Quaternion.identity, Vector3.one);
                for (int i = 0; i < count; i++)
                {
                    sys.BatchNumber = i + 1;
                    Vector4 translation = batchOffsets[i];
                    translation.w = 1;
                    handles[i].localPosition = translation;
                    trs.SetColumn(3, translation);
                    sys.WriteBatchLocalToWorldAt(i, trs);
                    sys.WriteLocalOffsetAt(i, localOffsetBuffer);
                    sys.WriteLocalBoundsAt(i, batchBounds);
                    sys.WriteBatchColorAt(i, batchColors[i]);
                    yield return delay;
                }
            }
        }
    }
}