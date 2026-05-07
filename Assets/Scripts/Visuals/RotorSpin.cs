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
    private DroneBodyV2 droneBodyV2;
    private float _smoothedRPM = 0f;

    void Awake()
    {
        droneBody = GetComponentInParent<DroneBody>();
        droneBodyV2 = GetComponentInParent<DroneBodyV2>();
        if (droneBody == null && droneBodyV2 == null)
            Debug.LogError($"[RotorSpin] No DroneBody or DroneBodyV2 found in parents of {name} (root={transform.root.name})");
    }

    void Update()
    {
        float[] outputs = droneBodyV2 != null ? droneBodyV2.MotorOutputs
                        : droneBody != null    ? droneBody.MotorOutputs
                        : null;

        if (outputs == null || rotorIndex >= outputs.Length) return;

        // Check if this motor is physically dead (not just outputting zero thrust)
        bool isDead = droneBodyV2 != null
            && droneBodyV2.motorsActive != null
            && rotorIndex < droneBodyV2.motorsActive.Length
            && !droneBodyV2.motorsActive[rotorIndex];

        if (isDead)
            // Freespin: slowly wind down like a real unpowered prop
            _smoothedRPM = Mathf.Lerp(_smoothedRPM, 0f, Time.deltaTime * 0.4f);
        else
            _smoothedRPM = Mathf.Lerp(_smoothedRPM, Mathf.Abs(outputs[rotorIndex]) * 1000f, Time.deltaTime * 12f);

        transform.Rotate(0f, spinDirection * _smoothedRPM * Time.deltaTime, 0f);
    }
}
