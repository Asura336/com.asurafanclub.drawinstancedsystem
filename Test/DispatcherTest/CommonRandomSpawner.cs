using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CommonRandomSpawner : RandomSpawnerBase<GameObject>
{

    protected override void ApplyInst(GameObject o, float pointX, float pointZ)
    {
        o.transform.localPosition = new Vector3(pointX, Random.Range(-0.5f, 0.5f), pointZ);
    }
}
