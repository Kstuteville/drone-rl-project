// RotorSpin.cs — Visual rotor animation
// Original by Jessenth, adapted to read from DroneBody instead of DronePhysics.
// Attach to each rotor child object on the drone prefab.

using UnityEngine;

public class RotorSpin : MonoBehaviour
{
    [Tooltip("Which motor index this rotor corresponds to (0=FL, 1=FR, 2=RL, 3=RR)")]
    public int rotorIndex;

    [Tooltip("Spin direction: 1 = CCW, -1 = CW")]
    public float spinDirection = 1f;

    private DroneBody droneBody;

    void Awake()
    {
        droneBody = transform.root.GetComponent<DroneBody>();
        if (droneBody == null)
            Debug.LogError($"[RotorSpin] No DroneBody found on root of {transform.root.name}");
    }

    void Update()
    {
        if (droneBody == null || droneBody.MotorOutputs == null) return;

        float output = Mathf.Abs(droneBody.MotorOutputs[rotorIndex]);
        float visualRPM = output * 1000f;
        transform.Rotate(0f, spinDirection * visualRPM * Time.deltaTime, 0f);
    }
}
