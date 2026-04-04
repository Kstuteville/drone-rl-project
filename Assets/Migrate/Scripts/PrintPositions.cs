using UnityEngine;

public class PrintPositions : MonoBehaviour
{
    public Transform[] mounts;

    void Start()
    {
        foreach (var mount in mounts)
        {
            Debug.Log($"{mount.name}: {transform.InverseTransformPoint(mount.position)}");
        }
    }
}