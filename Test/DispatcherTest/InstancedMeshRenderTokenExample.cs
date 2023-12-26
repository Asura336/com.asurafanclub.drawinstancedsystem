using UnityEngine;

namespace Com.Rendering.Test
{
    [RequireComponent(typeof(InstancedMeshRenderToken))]
    public class InstancedMeshRenderTokenExample : MonoBehaviour
    {
        [Range(0, 500)] public int number = 10;
        [Range(0, 5f)] public float step = 0.5f;
        [SerializeField] Vector3 euler;
        public Vector3 scale = Vector3.one;
        public Color color = Color.white;
        public Bounds localBounds = new Bounds
        {
            extents = Vector3.one * 0.5f
        };

        InstancedMeshRenderToken token;

        private void Awake()
        {
            token = GetComponent<InstancedMeshRenderToken>();
        }

        private void Start()
        {
            Apply();
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            token.Count = number;
            if (number != 0)
            {
                Matrix4x4 trs = Matrix4x4.TRS(default, Quaternion.Euler(euler), scale);
                Vector4 transaction = new Vector4(0, 0, 0, 1);
                for (int i = 0; i < number; i++)
                {
                    trs.SetColumn(3, transaction);
                    token.LocalOffsetRefAt(i) = trs;
                    transaction.z += step;
                }
                token.ClearLocalOffsetsOutOfCount();
                token.UpdateLocalOffsets();
                token.CheckDispatch();
            }

            token.InstanceColor = color;
            token.LocalBounds = localBounds;
        }
    }
}