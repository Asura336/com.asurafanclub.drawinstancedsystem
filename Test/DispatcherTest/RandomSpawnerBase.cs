using System.Collections;
using Com.Rendering;
using UnityEngine;

public abstract class RandomSpawnerBase<TPrefab> : MonoBehaviour
    where TPrefab : UnityEngine.Object
{
    public TPrefab prefab;
    public int count = 2500;
    public float xstep = 2.5f, zstep = 10;
    public float xmax = 200;

    private IEnumerator Start()
    {
        var instantiateRoot = transform;

        float pointX = 0, pointZ = 0;
        for (int i = 0; i < count; i++)
        {
            ApplyInst(Instantiate(prefab, instantiateRoot), pointX, pointZ);

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

    protected abstract void ApplyInst(TPrefab o, float pointX, float pointZ);

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
