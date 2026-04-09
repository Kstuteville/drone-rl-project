using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneBody : MonoBehaviour
{
    [Header("Physics")]
    [Tooltip("Max thrust per motor in Newtons (set so hover ~50% for maneuvering room)")]
    public float maxThrustPerMotor = 7.5f;

    [Tooltip("Motor positions relative to center of mass (local space). Matches Jess's drone geometry.")]
    public Vector3[] motorPositions = new Vector3[]
    {
        new Vector3(-0.88f, 0.60f,  0.88f),
        new Vector3( 0.88f, 0.60f,  0.88f),
        new Vector3(-0.88f, 0.60f, -0.88f),
        new Vector3( 0.88f, 0.60f, -0.88f),
    };

    [Header("Motor Dynamics")]
    public float maxThrustDeltaRate = 2.0f;
    public float motorLagTimeConstant = 0.05f;

    [Header("Aerodynamics")]
    public float linearDragCoeff = 0.5f;
    public float angularDragCoeff = 2.0f;

    [Header("Motor Health")]
    public bool[] motorsActive = new bool[] { true, true, true, true };

    [Header("Planner Input")]
    public Vector3 targetVelocity;

    [Header("Debug")]
    public bool drawMotorForces = true;

    [Header("Training Mode")]
    public bool randomizeSpawnOnEpisodeBegin = false;

    [Header("Spawn Randomization")]
    public float maxSpawnTilt = 1f;

    private Rigidbody _rb;
    private IDroneController _controller;
    private float[] _lastMotorOutputs = new float[4];
    private float[] _currentThrusts = new float[4];
    private float[] _actualThrusts = new float[4];
    private float _episodeTime;

    public float[] MotorOutputs => _lastMotorOutputs;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;
        
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
    }

    void FixedUpdate()
    {
        if (_controller == null) return;

        _episodeTime += Time.fixedDeltaTime;

        DroneState state = BuildState();
        float[] targetThrusts = _controller.ComputeMotorThrusts(state);

        for (int i = 0; i < 4; i++)
        {
            if (!motorsActive[i])
            {
                _currentThrusts[i] = 0f;
                _actualThrusts[i] = 0f;
                continue;
            }

            float delta = targetThrusts[i] - _currentThrusts[i];
            float maxDelta = maxThrustDeltaRate * Time.fixedDeltaTime;
            delta = Mathf.Clamp(delta, -maxDelta, maxDelta);
            _currentThrusts[i] = Mathf.Clamp(_currentThrusts[i] + delta, -1f, 1f);

            float alpha = Time.fixedDeltaTime / motorLagTimeConstant;
            _actualThrusts[i] = Mathf.Lerp(_actualThrusts[i], _currentThrusts[i], alpha);

            float forceN = _actualThrusts[i] * maxThrustPerMotor;
            Vector3 worldMotorPos = transform.TransformPoint(motorPositions[i]);
            _rb.AddForceAtPosition(transform.up * forceN, worldMotorPos, ForceMode.Force);
        }

        _lastMotorOutputs = (float[])_actualThrusts.Clone();

        _rb.AddForce(-linearDragCoeff * _rb.velocity);
        _rb.AddTorque(-angularDragCoeff * _rb.angularVelocity);
    }

    // ─────────────────────────────────────────────
    // Spawn — tilt only, everything else fixed
    // ─────────────────────────────────────────────

    public void RandomizeSpawn()
    {
        // Fixed position at exact target altitude, center of arena
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        transform.position = new Vector3(0f, 5f, 0f);

        // Only tilt is random
        transform.rotation = Quaternion.Euler(
            Random.Range(-maxSpawnTilt, maxSpawnTilt),
            Random.Range(0f, 360f),
            Random.Range(-maxSpawnTilt, maxSpawnTilt)
        );
    }

    private DroneState BuildState()
    {
        float alt = 0f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 100f))
            alt = hit.distance;

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

    public void SetTargetVelocity(Vector3 target)
    {
        targetVelocity = target;
    }

    public void DisableMotor(int motorIndex)
    {
        if (motorIndex >= 0 && motorIndex < 4)
        {
            motorsActive[motorIndex] = false;
            Debug.Log($"[DroneBody] Motor {motorIndex} disabled!");
        }
    }

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

    void OnDrawGizmos()
    {
        if (!drawMotorForces || motorPositions == null) return;

        for (int i = 0; i < motorPositions.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(motorPositions[i]);
            bool alive = motorsActive != null && i < motorsActive.Length && motorsActive[i];
            Gizmos.color = alive ? Color.green : Color.red;
            Gizmos.DrawSphere(worldPos, 0.03f);

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