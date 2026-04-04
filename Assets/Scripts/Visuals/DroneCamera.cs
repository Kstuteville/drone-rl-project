// DroneCamera.cs — Third-person chase camera
// Original by Jessenth, integrated into shared architecture.

using UnityEngine;

public class DroneCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 1.5f, -3f);
    public float positionSmoothSpeed = 5f;

    void LateUpdate()
    {
        if (target == null) return;

        Quaternion yawOnly = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3 targetPosition = target.position + yawOnly * offset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            positionSmoothSpeed * Time.deltaTime
        );

        Vector3 dirToTarget = target.position - transform.position;
        if (dirToTarget.sqrMagnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(dirToTarget);
        }
    }
}
