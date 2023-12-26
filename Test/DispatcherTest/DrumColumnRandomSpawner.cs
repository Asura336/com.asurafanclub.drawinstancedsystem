using System.Collections;
using UnityEngine;

namespace Com.Rendering.Test
{
    public class DrumColumnRandomSpawner : MonoBehaviour
    {
        public InstancedMeshRenderTokenExample prefab;
        public int count = 2500;


        private IEnumerator Start()
        {
            var instantiateRoot = transform;
            float xstep = 2.5f, zstep = 10;
            float xmax = 200;
            float pointX = 0, pointZ = 0;
            for (int i = 0; i < count; i++)
            {
                var o = Instantiate(prefab, instantiateRoot);
                o.transform.localPosition = new Vector3(pointX, Random.Range(-0.5f, 0.5f), pointZ);
                o.color = Random.ColorHSV();
                o.step = Random.Range(0.05f, 0.2f);
                o.number = Random.Range(13, 120);
                //o.number = 64;
                Vector3 extents = default;
                extents.y = o.scale.x * 0.5f;
                extents.x = o.scale.y;
                extents.z = o.step * (o.number - 1) * 0.5f + 0.1f;

                o.localBounds = new Bounds
                {
                    extents = extents,
                    center = new Vector3(0, 0, extents.z - 0.05f)
                };

                pointX += xstep;
                if (pointX > xmax)
                {
                    pointX = 0;
                    pointZ += zstep;
                }

                if (i % 100 == 99)
                {
                    yield return null;
                }
            }
        }

        [ContextMenu("used memory")]
        public void PrintUsedMemory()
        {
            Debug.Log($"Native: {InstancedMeshRenderDispatcher.GetNativeUsedMemory()}");
        }

        [ContextMenu("trim excess")]
        public void CallTrimExcess()
        {
            InstancedMeshRenderDispatcher.TrimExcess();
        }
    }
}