using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

[RequireComponent(typeof(Rigidbody))]
public class DroneTelemetryLogger : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The tag of the floor/ground plane to trigger the crash dump")]
    public string groundTag = "Ground";
    
    [Tooltip("How many seconds of data to keep before the crash")]
    public float keepLastSeconds = 10f;

    private Rigidbody _rb;
    private bool _hasCrashed = false;
    private float _fixedDeltaTime;

    // The data structure for a single physics frame
    private struct TelemetryFrame
    {
        public float time;
        public float pitch; // X
        public float yaw;   // Y
        public float roll;  // Z
        public float angVelX;
        public float angVelY;
        public float angVelZ;
        public float velocityY;
        public float altitude;
    }

    private Queue<TelemetryFrame> _flightData = new Queue<TelemetryFrame>();

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _fixedDeltaTime = Time.fixedDeltaTime;
    }

    void FixedUpdate()
    {
        if (_hasCrashed) return;

        // Record the exact physics state this frame
        TelemetryFrame frame = new TelemetryFrame
        {
            time = Time.time,
            pitch = NormalizeAngle(transform.eulerAngles.x),
            yaw = transform.eulerAngles.y,
            roll = NormalizeAngle(transform.eulerAngles.z),
            angVelX = _rb.angularVelocity.x,
            angVelY = _rb.angularVelocity.y,
            angVelZ = _rb.angularVelocity.z,
            velocityY = _rb.velocity.y,
            altitude = transform.position.y
        };

        _flightData.Enqueue(frame);

        // Keep the buffer clean so we don't run out of memory during long flights
        int maxFrames = Mathf.CeilToInt(keepLastSeconds / _fixedDeltaTime);
        while (_flightData.Count > maxFrames)
        {
            _flightData.Dequeue();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (_hasCrashed) return;

        // Check if we hit the ground
        if (collision.gameObject.CompareTag(groundTag))
        {
            _hasCrashed = true;
            DumpDataToCSV();
        }
    }

    private void DumpDataToCSV()
    {
        // Save the file directly into your Unity project folder (outside of Assets so it doesn't cause constant re-imports)
        string filePath = Path.Combine(Application.dataPath, "../DroneCrashReport.csv");
        
        StringBuilder sb = new StringBuilder();
        
        // CSV Header
        sb.AppendLine("Time,Pitch(Deg),Roll(Deg),Yaw(Deg),AngVelX(PitchSpeed),AngVelZ(RollSpeed),AngVelY(YawSpeed),VerticalVelocity,Altitude");

        // Dump all frames
        foreach (var frame in _flightData)
        {
            sb.AppendLine($"{frame.time:F3},{frame.pitch:F2},{frame.roll:F2},{frame.yaw:F2},{frame.angVelX:F3},{frame.angVelZ:F3},{frame.angVelY:F3},{frame.velocityY:F3},{frame.altitude:F3}");
        }

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"<color=red><b>CRASH DETECTED.</b></color> Telemetry dumped to: {filePath}");
        
        // Optional: Pause the editor automatically so you can inspect the exact frame
        Debug.Break(); 
    }

    // Helper to keep angles between -180 and 180 for easier graphing
    private float NormalizeAngle(float a)
    {
        while (a > 180f) a -= 360f;
        while (a < -180f) a += 360f;
        return a;
    }
}