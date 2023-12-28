using System;
using UnityEngine;

namespace Com.Rendering
{
    [AddComponentMenu("Com/Rendering/2ÖáÕóÁÐÊµÀý")]
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [RequireComponent(typeof(InstancedMeshRenderToken))]
    public sealed class Grid2DInstances : BaseGridInstances
    {
        [Serializable]
        public enum AxisUseage
        {
            XY,
            YZ,
            XZ,
        }

        // 64 * 64 = 4096
        public const int maxNumber = 64;

        [SerializeField] AxisUseage axisUseage;
        [SerializeField][Range(1, maxNumber)] int xNumber = 1;
        [SerializeField][Range(1, maxNumber)] int yNumber = 1;
        [SerializeField][Range(1, maxNumber)] int zNumber = 1;

        public override int XNumber
        {
            get => axisUseage switch
            {
                AxisUseage.XY or AxisUseage.XZ => xNumber,
                _ => 1
            };
            set => xNumber = Mathf.Clamp(value, 1, maxNumber);
        }
        public override int YNumber
        {
            get => axisUseage switch
            {
                AxisUseage.XY or AxisUseage.YZ => yNumber,
                _ => 1
            }; set => yNumber = Mathf.Clamp(value, 1, maxNumber);
        }
        public override int ZNumber
        {
            get => axisUseage switch
            {
                AxisUseage.YZ or AxisUseage.XZ => zNumber,
                _ => 1
            }; set => zNumber = Mathf.Clamp(value, 1, maxNumber);
        }
    }
}