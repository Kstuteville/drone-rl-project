// =============================================================
// DroneBody.cs — Drone physics + controller bridge
// Drop into: Assets/Scripts/Drone/
//
// Attach to the drone GameObject (needs Rigidbody).
// In the Inspector, drag in either PIDDroneController or
// the ML-Agents PPODroneController as the "controller" field.
// Everything else stays identical.
// =============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneBody : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // Inspector fields
    // ─────────────────────────────────────────────

    [Header("Physics")]
    [Tooltip("Max thrust per motor in Newtons")]
    public float maxThrustPerMotor = 5f;

    [Tooltip("Motor positions relative to center of mass (local space)")]
    public Vector3[] motorPositions = new Vector3[]
    {
        new Vector3(-0.25f, 0f,  0.25f),  // 0: Front-Left
        new Vector3( 0.25f, 0f,  0.25f),  // 1: Front-Right
        new Vector3(-0.25f, 0f, -0.25f),  // 2: Rear-Left
        new Vector3( 0.25f, 0f, -0.25f),  // 3: Rear-Right
    };

    [Header("Motor Health")]
    [Tooltip("Set to false to simulate motor failure")]
    public bool[] motorsActive = new bool[] { true, true, true, true };

    [Header("Planner Input")]
    [Tooltip("Set by the planner script each frame")]
    public Vector3 targetVelocity;

    [Header("Debug")]
    public bool drawMotorForces = true;

    // ─────────────────────────────────────────────
    // Runtime
    // ─────────────────────────────────────────────

    private Rigidbody _rb;
    private IDroneController _controller;
    private float[] _lastMotorOutputs = new float[4];

    void Awake()
{
    _rb = GetComponent<Rigidbody>();
    _rb.useGravity = true;

    _controller = GetComponent<IDroneController>();
    if (_controller == null)
    {
        Debug.LogError("[DroneBody] No IDroneController found on this GameObject!");
    }

    _controller?.Initialize(BuildConfig());
}

    void FixedUpdate()
    {
        if (_controller == null) return;

        // Build state snapshot
        DroneState state = BuildState();

        // Ask controller for motor commands
        float[] thrusts = _controller.ComputeMotorThrusts(state);
        _lastMotorOutputs = thrusts;

        // Apply forces at motor positions
        for (int i = 0; i < 4; i++)
        {
            if (!motorsActive[i])
            {
                thrusts[i] = 0f; // double-check: body also enforces mask
            }

            // Convert normalized [-1, 1] to Newtons
            float forceN = thrusts[i] * maxThrustPerMotor;

            // Apply thrust in the motor's local "up" direction
            Vector3 worldMotorPos = transform.TransformPoint(motorPositions[i]);
            Vector3 forceDir = transform.up;

            _rb.AddForceAtPosition(forceDir * forceN, worldMotorPos,
                ForceMode.Force);
        }
    }

    // ─────────────────────────────────────────────
    // State + config builders
    // ─────────────────────────────────────────────

    private DroneState BuildState()
    {
        // Altitude via raycast
        float alt = 0f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit,
            100f))
        {
            alt = hit.distance;
        }

        return new DroneState
        {
            velocity = _rb.velocity,
            angularVelocity = _rb.angularVelocity,
            orientation = transform.rotation,
            eulerAngles = transform.eulerAngles,
            position = transform.position,
            motorsActive = (bool[])motorsActive.Clone(),
            currentMotorOutputs = (float[])_lastMotorOutputs.Clone(),
            targetVelocity = targetVelocity,
            altitude = alt,
        };
    }

    private DroneConfig BuildConfig()
    {
        return new DroneConfig
        {
            mass = _rb.mass,
            maxThrustPerMotor = maxThrustPerMotor,
            motorPositions = motorPositions,
            armLength = motorPositions[0].magnitude,
            dt = Time.fixedDeltaTime,
        };
    }

    // ─────────────────────────────────────────────
    // Public API — called by Planner / DamageSystem
    // ─────────────────────────────────────────────

    /// <summary>
    /// Called by the planner every frame to set the target.
    /// </summary>
    public void SetTargetVelocity(Vector3 target)
    {
        targetVelocity = target;
    }

    /// <summary>
    /// Called by the damage system to kill a motor.
    /// Index: 0=FL, 1=FR, 2=RL, 3=RR
    /// </summary>
    public void DisableMotor(int motorIndex)
    {
        if (motorIndex >= 0 && motorIndex < 4)
        {
            motorsActive[motorIndex] = false;
            Debug.Log($"[DroneBody] Motor {motorIndex} disabled!");
        }
    }

    /// <summary>
    /// Reset all motors to active (episode reset).
    /// </summary>
    public void ResetMotors()
    {
        for (int i = 0; i < 4; i++) motorsActive[i] = true;
        _controller?.OnEpisodeReset();
    }

    // ─────────────────────────────────────────────
    // Debug visualization
    // ─────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!drawMotorForces || motorPositions == null) return;

        for (int i = 0; i < motorPositions.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(motorPositions[i]);

            // Motor position
            bool alive = motorsActive != null && i < motorsActive.Length
                && motorsActive[i];
            Gizmos.color = alive ? Color.green : Color.red;
            Gizmos.DrawSphere(worldPos, 0.03f);

            // Thrust vector
            if (_lastMotorOutputs != null && i < _lastMotorOutputs.Length)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(worldPos,
                    worldPos + transform.up * _lastMotorOutputs[i] * 0.5f);
            }
        }
    }
}
