using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Com.Rendering
{
    public static class InstancedMeshRenderExtensions
    {
        /// <summary>
        /// 使用数组或者列表内容为设置一个绘制批次的实例本地变换并然后提交改动
        /// </summary>
        /// <param name="token"></param>
        /// <param name="localOffsets"></param>
        public static void SetInstancesOffsets(this InstancedMeshRenderToken token, IList<Matrix4x4> localOffsets)
        {
            int count =
            token.Count = localOffsets.Count;

            for (int i = 0; i < count; i++)
            {
                token.LocalOffsetRefAt(i) = localOffsets[i];
            }
            token.ClearLocalOffsetsOutOfCount();
            token.UpdateLocalOffsets();
            token.CheckDispatch();
        }

        /// <summary>
        /// 使用数组或者列表内容为设置一个绘制批次的实例本地变换并然后提交改动
        /// </summary>
        /// <param name="token"></param>
        /// <param name="localOffsets"></param>
        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, Matrix4x4* localOffsets,
            int start, int length)
        {
            token.Count = length;
            for (int i = 0; i < length; i++)
            {
                token.LocalOffsetRefAt(i) = localOffsets[i];
            }
            token.ClearLocalOffsetsOutOfCount();
            token.UpdateLocalOffsets();
            token.CheckDispatch();
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeArray<Matrix4x4> localOffsets,
            int start, int length)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), start, length);
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeArray<Matrix4x4> localOffsets)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), 0, localOffsets.Length);
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeSlice<Matrix4x4> localOffsets,
            int start, int length)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), start, length);
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeSlice<Matrix4x4> localOffsets)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), 0, localOffsets.Length);
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeList<Matrix4x4> localOffsets,
            int start, int length)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), start, length);
        }

        public static unsafe void SetInstancesOffsets(this InstancedMeshRenderToken token, NativeList<Matrix4x4> localOffsets)
        {
            token.SetInstancesOffsets((Matrix4x4*)localOffsets.GetUnsafePtr(), 0, localOffsets.Length);
        }
    }
}