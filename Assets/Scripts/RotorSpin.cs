using UnityEngine;

public class RotorSpin : MonoBehaviour
{
    public int rotorIndex;
    public float spinDirection = 1f; // 1 = CCW, -1 = CW
    private DronePhysics dronePhysics;

    void Awake()
{
    dronePhysics = transform.root.GetComponent<DronePhysics>();
}

    void Update()
    {
        float rpm = dronePhysics.currentRPM[rotorIndex];
        transform.Rotate(0f, spinDirection * rpm * Time.deltaTime * 100f, 0f);
    }
}