using System;
using UnityEngine;

namespace Com.Rendering
{
    [AddComponentMenu("Com/Rendering/线性排列实例")]
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [RequireComponent(typeof(InstancedMeshRenderToken))]
    public sealed class LineInstances : BaseGridInstances
    {
        [Serializable]
        public enum AxisUseage
        {
            X,
            Y,
            Z,
        }

        public const int maxNumber = 16384;

        [SerializeField] AxisUseage axisUseage;
        [SerializeField][Range(1, maxNumber)] int xNumber = 1;
        [SerializeField][Range(1, maxNumber)] int yNumber = 1;
        [SerializeField][Range(1, maxNumber)] int zNumber = 1;

        public override int XNumber
        {
            get => axisUseage switch
            {
                AxisUseage.X => xNumber,
                _ => 1
            };
            set => xNumber = Mathf.Clamp(value, 1, maxNumber);
        }
        public override int YNumber
        {
            get => axisUseage switch
            {
                AxisUseage.Y => yNumber,
                _ => 1
            }; set => yNumber = Mathf.Clamp(value, 1, maxNumber);
        }
        public override int ZNumber
        {
            get => axisUseage switch
            {
                AxisUseage.Z => zNumber,
                _ => 1
            }; set => zNumber = Mathf.Clamp(value, 1, maxNumber);
        }
    }
}