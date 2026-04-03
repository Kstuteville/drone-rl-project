using UnityEngine;

public class DronePhysics : MonoBehaviour
{
    [Header("Rotor Positions (local space)")]
    private Vector3[] rotorPositions = new Vector3[]
    {
        new Vector3(-0.88f, 0.60f,  0.88f), // FL
        new Vector3( 0.88f, 0.60f,  0.88f), // FR
        new Vector3(-0.88f, 0.60f, -0.88f), // RL
        new Vector3( 0.88f, 0.60f, -0.88f), // RR
    };

    // FL=CCW, FR=CW, RL=CW, RR=CCW
    private int[] rotorDirections = new int[] { 1, -1, -1, 1 };

    [Header("Motor Config")]
    public float kT = 0.0003f;
    public float kQ = 0.00001f;
    public float motorLag = 0.05f;
    public float maxRPM = 100f;

    [Header("Drag")]
    public float linearDrag = 0.5f;
    public float angularDrag = 2.0f;

    [Header("Runtime")]
    public float[] throttles = new float[4];
    public float[] currentRPM = new float[4];

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        ApplyRotorForces();
        ApplyDrag();
    }

    void ApplyRotorForces()
    {
        for (int i = 0; i < 4; i++)
        {
            float targetRPM = Mathf.Max(0f, throttles[i] * maxRPM);
            currentRPM[i] = Mathf.Lerp(currentRPM[i], targetRPM, Time.fixedDeltaTime / motorLag);

            float rpm = currentRPM[i];
            float thrust = kT * rpm * rpm;
            float torque = kQ * rpm * rpm * rotorDirections[i];

            Vector3 worldPos = transform.TransformPoint(rotorPositions[i]);
            rb.AddForceAtPosition(transform.up * thrust, worldPos);
            rb.AddTorque(transform.up * torque);
        }
    }

    void ApplyDrag()
{
    rb.AddForce(-linearDrag * rb.linearVelocity);
    rb.AddTorque(-angularDrag * rb.angularVelocity);
}

    public void SetThrottles(float fl, float fr, float rl, float rr)
    {
        throttles[0] = fl;
        throttles[1] = fr;
        throttles[2] = rl;
        throttles[3] = rr;
    }
}