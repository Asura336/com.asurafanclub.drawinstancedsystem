using System.Collections;
using UnityEngine;

namespace Com.Rendering.Test
{
    public class DrumColumnRandomSpawner : RandomSpawnerBase<InstancedMeshRenderTokenExample>
    {
        protected override void ApplyInst(InstancedMeshRenderTokenExample o, float pointX, float pointZ)
        {
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
        }
    }
}