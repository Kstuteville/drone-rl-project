using UnityEngine;

public class DroneController : MonoBehaviour
{
    private DronePhysics dronePhysics;

    public float throttleSensitivity = 0.5f;
    public float pitchSensitivity = 0.1f;
    public float rollSensitivity = 0.1f;
    public float yawSensitivity = 0.1f;

    private float throttle = 0f;

    void Awake()
    {
        dronePhysics = GetComponent<DronePhysics>();
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.W)) throttle += throttleSensitivity * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) throttle -= throttleSensitivity * Time.deltaTime;
        throttle = Mathf.Clamp(throttle, 0f, 1f);

        float pitch = 0f;
        float roll = 0f;
        float yaw = 0f;

        if (Input.GetKey(KeyCode.UpArrow))    pitch =  pitchSensitivity;
        if (Input.GetKey(KeyCode.DownArrow))  pitch = -pitchSensitivity;
        if (Input.GetKey(KeyCode.LeftArrow))  roll =  rollSensitivity;
        if (Input.GetKey(KeyCode.RightArrow)) roll = -rollSensitivity;
        if (Input.GetKey(KeyCode.A)) yaw =  yawSensitivity;
        if (Input.GetKey(KeyCode.D)) yaw = -yawSensitivity;

        float fl = throttle - pitch - roll - yaw;
        float fr = throttle - pitch + roll + yaw;
        float rl = throttle + pitch - roll + yaw;
        float rr = throttle + pitch + roll - yaw;

        fl = Mathf.Clamp(fl, -1f, 1f);
        fr = Mathf.Clamp(fr, -1f, 1f);
        rl = Mathf.Clamp(rl, -1f, 1f);
        rr = Mathf.Clamp(rr, -1f, 1f);

        dronePhysics.SetThrottles(fl, fr, rl, rr);
    }
}