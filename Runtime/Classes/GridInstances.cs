using UnityEngine;

namespace Com.Rendering
{
    [AddComponentMenu("Com/Rendering/3轴阵列实例")]
    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    [RequireComponent(typeof(InstancedMeshRenderToken))]
    public sealed class GridInstances : BaseGridInstances
    {
        // 16 * 16 * 16 = 4096
        public const int maxNumber = 32;

        [SerializeField][Range(1, maxNumber)] int xNumber = 1;
        [SerializeField][Range(1, maxNumber)] int yNumber = 1;
        [SerializeField][Range(1, maxNumber)] int zNumber = 1;

        public override int XNumber { get => xNumber; set => xNumber = Mathf.Clamp(value, 1, maxNumber); }
        public override int YNumber { get => yNumber; set => yNumber = Mathf.Clamp(value, 1, maxNumber); }
        public override int ZNumber { get => zNumber; set => zNumber = Mathf.Clamp(value, 1, maxNumber); }
    }
}