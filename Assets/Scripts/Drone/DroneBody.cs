// =============================================================
// DroneBody.cs — Drone physics + controller bridge
//
// Attach to the drone GameObject (needs Rigidbody).
// In the Inspector, drag in either PIDDroneController or
// the ML-Agents PPODroneController as the "controller" field.
// Everything else stays identical.
//
// Motor dynamics pipeline:
//   Controller output (absolute target [-1,1])
//     → Slew-rate limiter (prevents instant jumps)
//     → First-order low-pass filter (motor response lag)
//     → Force application at motor positions
// =============================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneBody : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // Inspector fields
    // ─────────────────────────────────────────────

    [Header("Physics")]
    [Tooltip("Max thrust per motor in Newtons (set so hover ~50% for maneuvering room)")]
    public float maxThrustPerMotor = 7.5f;

    [Tooltip("Motor positions relative to center of mass (local space). Matches Jess's drone geometry.")]
    public Vector3[] motorPositions = new Vector3[]
    {
        new Vector3(-0.88f, 0.60f,  0.88f),  // 0: Front-Left
        new Vector3( 0.88f, 0.60f,  0.88f),  // 1: Front-Right
        new Vector3(-0.88f, 0.60f, -0.88f),  // 2: Rear-Left
        new Vector3( 0.88f, 0.60f, -0.88f),  // 3: Rear-Right
    };

    [Header("Motor Dynamics")]
    [Tooltip("Max thrust change per second (normalized). Lower = smoother, less oscillation.")]
    public float maxThrustDeltaRate = 2.0f;

    [Tooltip("Motor response time constant in seconds (first-order lag). Matches real motor inertia.")]
    public float motorLagTimeConstant = 0.05f;

    [Header("Aerodynamics")]
    [Tooltip("Linear drag coefficient applied to velocity")]
    public float linearDragCoeff = 0.5f;

    [Tooltip("Angular drag coefficient applied to angular velocity")]
    public float angularDragCoeff = 2.0f;

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
    private float[] _currentThrusts = new float[4];  // after slew-rate limit
    private float[] _actualThrusts = new float[4];   // after low-pass filter
    private float _episodeTime;

    /// <summary>
    /// Current motor outputs (post-lag). Use for visual effects (rotor spin).
    /// </summary>
    public float[] MotorOutputs => _lastMotorOutputs;

    void Awake()
{
    _rb = GetComponent<Rigidbody>();
    _rb.useGravity = true;
    
    // Force zero thrust on start
    for (int i = 0; i < 4; i++)
    {
        _currentThrusts[i] = 0f;
        _actualThrusts[i] = 0f;
        _lastMotorOutputs[i] = 0f;
    }
    
    _controller = GetComponent<IDroneController>();
    if (_controller == null)
        Debug.LogError("[DroneBody] No IDroneController found on this GameObject!");

    _controller?.Initialize(BuildConfig());
}

void Start()
{
    Debug.Log($"Starting velocity: {_rb.velocity}");
    Debug.Log($"Starting position: {transform.position}");

    _rb.velocity = Vector3.zero;
    _rb.angularVelocity = Vector3.zero;
    Debug.Log($"Starting velocity: {_rb.velocity}");
    Debug.Log($"Starting position: {transform.position}");
}

    void FixedUpdate()
    {
        if (_controller == null) return;

        _episodeTime += Time.fixedDeltaTime;
        
         

        // Build state snapshot
        DroneState state = BuildState();

        // Ask controller for motor commands (absolute targets in [-1, 1])
        float[] targetThrusts = _controller.ComputeMotorThrusts(state);

        // Motor dynamics pipeline
        for (int i = 0; i < 4; i++)
        {
            if (!motorsActive[i])
            {
                _currentThrusts[i] = 0f;
                _actualThrusts[i] = 0f;
                continue;
            }

            // Stage 1: Slew-rate limiter (prevents instant thrust jumps / oscillation)
            float delta = targetThrusts[i] - _currentThrusts[i];
            float maxDelta = maxThrustDeltaRate * Time.fixedDeltaTime;
            delta = Mathf.Clamp(delta, -maxDelta, maxDelta);
            _currentThrusts[i] = Mathf.Clamp(_currentThrusts[i] + delta, -1f, 1f);

            // Stage 2: First-order low-pass filter (motor response lag)
            float alpha = Time.fixedDeltaTime / motorLagTimeConstant;
            _actualThrusts[i] = Mathf.Lerp(_actualThrusts[i], _currentThrusts[i], alpha);

            // Stage 3: Apply force at motor position
            float forceN = _actualThrusts[i] * maxThrustPerMotor;
            Vector3 worldMotorPos = transform.TransformPoint(motorPositions[i]);
            _rb.AddForceAtPosition(transform.up * forceN, worldMotorPos, ForceMode.Force);
        }

    

        _lastMotorOutputs = (float[])_actualThrusts.Clone();

        // Apply aerodynamic drag (matches Jess's physics model)
        _rb.AddForce(-linearDragCoeff * _rb.velocity);
        _rb.AddTorque(-angularDragCoeff * _rb.angularVelocity);
    }

    // ─────────────────────────────────────────────
    // State + config builders
    // ─────────────────────────────────────────────

    private DroneState BuildState()
    {
        // Altitude via raycast
        float alt = 0f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 100f))
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
            currentMotorOutputs = (float[])_actualThrusts.Clone(),
            targetVelocity = targetVelocity,
            altitude = alt,
            episodeTime = _episodeTime,
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
            motorLagTimeConstant = motorLagTimeConstant,
            maxThrustDeltaRate = maxThrustDeltaRate,
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
    /// Reset all motors and episode state.
    /// </summary>
    public void ResetMotors()
    {
        for (int i = 0; i < 4; i++)
        {
            motorsActive[i] = true;
            _currentThrusts[i] = 0f;
            _actualThrusts[i] = 0f;
        }
        _episodeTime = 0f;
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

    void OnGUI()
    {
        if (!drawMotorForces) return;
        GUI.Label(new Rect(10, Screen.height - 60, 500, 25),
            $"Target vel: {targetVelocity:F2} | Actual vel: {_rb?.velocity:F2}");
        GUI.Label(new Rect(10, Screen.height - 35, 500, 25),
            $"Motors: [{_actualThrusts[0]:F2}, {_actualThrusts[1]:F2}, {_actualThrusts[2]:F2}, {_actualThrusts[3]:F2}]");
    }
}
