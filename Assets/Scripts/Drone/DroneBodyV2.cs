using UnityEngine;
using Unity.MLAgents;

[RequireComponent(typeof(Rigidbody))]
public class DroneBodyV2 : MonoBehaviour
{
    public const string CurriculumStageKey = "drone_curriculum_stage";

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

    [Header("Damage Control (Scripted Compensation)")]
    [Tooltip("When enabled, boosts diagonal-opposite motor and increases drag to slow spin on motor death. Toggle off for pure RL.")]
    public bool enableScriptedCompensation = false;
    [Tooltip("How much extra thrust multiplier the opposite motor gets (1.0 = no boost, 1.4 = 40% boost).")]
    [Range(1f, 2f)]
    public float compensationThrustMultiplier = 1.35f;

    [Header("Motor Death VFX")]
    [Tooltip("Particle prefab spawned at the motor position when it dies. Assign a smoke/spark prefab.")]
    public GameObject motorDeathVFXPrefab;

    [Header("Planner Input")]
    public Vector3 targetVelocity;

    [Header("Hover / guidance target")]
    [Tooltip("If null, target world position defaults to (0, 5, 0) to match spawn.")]
    public Transform targetTransform;

    [Header("Motor Failure Behaviour")]
    [Tooltip("Cut ALL thrust when 2+ motors die. Enable for PPO (prevents over-compensation). Disable for PID (lets remaining motors create a realistic spin-crash).")]
    public bool cutThrustOnDoubleMotorLoss = true;

    [Header("Position Leash (User Scene)")]
    [Tooltip("Pulls drone back toward spawn point. Enable in user test scene, disable during training.")]
    public bool enablePositionLeash = false;
    [Tooltip("Spring strength — how hard it pulls toward home (N/m).")]
    public float leashSpringK = 3f;
    [Tooltip("Velocity damping — prevents oscillating past home.")]
    public float leashDampK = 1.5f;
    [Tooltip("Deadzone radius — no force applied within this distance from home.")]
    public float leashDeadzone = 1.5f;

    [Header("Debug")]
    public bool drawMotorForces = true;

    [Header("Training Mode")]
    public bool randomizeSpawnOnEpisodeBegin = false;

    [Header("Spawn Randomization")]
    [Tooltip("Legacy fallback used only if spawnConfig is null.")]
    public float maxSpawnTilt = 10f;

    [Tooltip("Preset-driven spawn variation. If null at Awake, defaults to SpawnVariationConfig.Easy().")]
    public SpawnVariationConfig spawnConfig;

    private Rigidbody _rb;
    private System.Random _spawnRng;
    private int _episodeIndex;
    private IDroneController _controller;
    private float[] _lastMotorOutputs = new float[4];
    private float[] _currentThrusts = new float[4];
    private float[] _actualThrusts = new float[4];
    private float _episodeTime;
    private float _scheduledFailureTime = -1f;

    private Vector3[] _baseMotorLocal;
    private float _baseMass;
    private Vector3 _baseInertiaTensor;
    private float _baseMaxThrustPerMotor;
    private float _baseMotorLagTimeConstant;
    private Vector3 _windForceWorld;
    private Vector3 _homePosition;

#if UNITY_EDITOR
    static bool s_LoggedAltitudeRaycastFallback;
#endif

    public float[] MotorOutputs => _lastMotorOutputs;

    public static int ReadCurriculumStage()
    {
        if (Academy.IsInitialized && Academy.Instance.EnvironmentParameters != null)
        {
            float v = Academy.Instance.EnvironmentParameters.GetWithDefault(CurriculumStageKey, 1f);
            return Mathf.Clamp(Mathf.RoundToInt(v), 1, 4);
        }
        return 1;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;

        if (spawnConfig == null)
            spawnConfig = SpawnVariationConfig.Easy();

        _baseMotorLocal = new Vector3[motorPositions.Length];
        for (int i = 0; i < motorPositions.Length; i++)
            _baseMotorLocal[i] = motorPositions[i];
        _baseMass = _rb.mass;
        _baseInertiaTensor = _rb.inertiaTensor;
        _baseMaxThrustPerMotor = maxThrustPerMotor;
        _baseMotorLagTimeConstant = motorLagTimeConstant;

        for (int i = 0; i < 4; i++)
        {
            _currentThrusts[i] = 0f;
            _actualThrusts[i] = 0f;
            _lastMotorOutputs[i] = 0f;
        }

        var ppo = GetComponent<PPODroneControllerV2>();
        if (ppo != null && ppo.enabled)
            _controller = ppo;
        else
        {
            var pid = GetComponent<PIDDroneController2>();
            _controller = pid;
        }
        if (_controller == null)
            Debug.LogError("[DroneBodyV2] No PPODroneControllerV2 or PIDDroneController2 found on this GameObject!");

        var cfg = BuildConfig();
        _controller?.Initialize(cfg);
        GetComponent<PIDDroneController2>()?.Initialize(cfg);
    }

    void Start()
    {
        _homePosition = transform.position;
        Debug.Log($"[DroneBodyV2] Starting velocity: {_rb.velocity}");
        Debug.Log($"[DroneBodyV2] Starting position: {transform.position}");
    }

    void FixedUpdate()
    {
        if (_controller == null) return;

        _episodeTime += Time.fixedDeltaTime;

        if (_scheduledFailureTime > 0f && _episodeTime >= _scheduledFailureTime)
        {
            int[] active = System.Array.FindAll(new int[] { 0, 1, 2, 3 }, i => motorsActive[i]);
            if (active.Length > 0)
            {
                DisableMotor(active[Random.Range(0, active.Length)]);
                Debug.Log($"[DroneBodyV2] Mid-flight motor failure at t={_episodeTime:F2}s");
            }
            _scheduledFailureTime = -1f;
        }

        int deadCount = 0;
        foreach (bool b in motorsActive) if (!b) deadCount++;

        DroneState state = BuildState();
        // Always call so OnActionReceived keeps firing (crash detection + episode reset)
        float[] targetThrusts = _controller.ComputeMotorThrusts(state);

        for (int i = 0; i < 4; i++)
        {
            if ((cutThrustOnDoubleMotorLoss && deadCount >= 2) || !motorsActive[i])
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

        if (_windForceWorld.sqrMagnitude > 1e-8f)
            _rb.AddForce(_windForceWorld, ForceMode.Force);

        if (enablePositionLeash && deadCount < 2)
        {
            Vector3 toHome = _homePosition - transform.position;
            float dist = toHome.magnitude;
            if (dist > leashDeadzone)
            {
                Vector3 spring = toHome.normalized * (dist - leashDeadzone) * leashSpringK;
                Vector3 damp   = -_rb.velocity * leashDampK;
                _rb.AddForce(spring + damp, ForceMode.Force);
            }
        }
    }

    public void SetSpawnConfig(SpawnVariationConfig cfg, bool resetEpisodeIndex = true)
    {
        spawnConfig = cfg != null ? cfg : SpawnVariationConfig.Easy();
        if (resetEpisodeIndex) _episodeIndex = 0;
        _spawnRng = null;
    }

    public Vector3 GetTargetPosition()
    {
        if (targetTransform != null)
            return targetTransform.position;
        return new Vector3(_homePosition.x, 5f, _homePosition.z);
    }

    public DroneState GetStateSnapshot()
    {
        return BuildState();
    }

    public void RandomizeSpawn()
    {
        if (spawnConfig == null)
            spawnConfig = SpawnVariationConfig.Easy();

        _rb.angularVelocity = Vector3.zero;

        if (spawnConfig.useFixedSeed)
            _spawnRng = new System.Random(spawnConfig.seedValue + _episodeIndex);
        else
            _spawnRng = null;

        float x = _homePosition.x + SampleSym(spawnConfig.positionJitter.x);
        float z = _homePosition.z + SampleSym(spawnConfig.positionJitter.z);
        float y = spawnConfig.yBase + SampleSym(spawnConfig.yJitter);
        y = Mathf.Max(y, spawnConfig.yMin);
        transform.position = new Vector3(x, y, z);

        float tiltX = SampleSym(spawnConfig.maxTiltDegrees);
        float tiltZ = SampleSym(spawnConfig.maxTiltDegrees);
        float yaw = spawnConfig.randomizeYaw ? SampleSym(spawnConfig.maxYawDegrees) : 0f;
        transform.rotation = Quaternion.Euler(tiltX, yaw, tiltZ);

        _rb.velocity = new Vector3(
            SampleSym(spawnConfig.linearVelocityRange.x),
            SampleSym(spawnConfig.linearVelocityRange.y),
            SampleSym(spawnConfig.linearVelocityRange.z));

        Vector3 angVelDeg = new Vector3(
            SampleSym(spawnConfig.angularVelocityRange.x),
            SampleSym(spawnConfig.angularVelocityRange.y),
            SampleSym(spawnConfig.angularVelocityRange.z));
        _rb.angularVelocity = angVelDeg * Mathf.Deg2Rad;

        int activeCount = 0;
        foreach (bool b in motorsActive) if (b) activeCount++;
        activeCount = Mathf.Max(activeCount, 1);
        float hoverThrust = Mathf.Clamp01(
            (_rb.mass * Mathf.Abs(Physics.gravity.y)) / (activeCount * Mathf.Max(maxThrustPerMotor, 1e-6f)));
        for (int i = 0; i < 4; i++)
        {
            float t = motorsActive[i] ? hoverThrust : 0f;
            _currentThrusts[i] = t;
            _actualThrusts[i] = t;
            _lastMotorOutputs[i] = t;
        }

        _episodeIndex++;
    }

    private float SampleSym(float halfRange)
    {
        if (halfRange <= 0f) return 0f;
        if (_spawnRng != null)
            return (float)((_spawnRng.NextDouble() * 2.0 - 1.0) * halfRange);
        return Random.Range(-halfRange, halfRange);
    }

    void ApplyDomainRandomizationForEpisode()
    {
        int stage = ReadCurriculumStage();

        for (int i = 0; i < 4; i++)
        {
            motorPositions[i] = _baseMotorLocal[i];
            motorsActive[i] = true;
        }
        _rb.mass = _baseMass;
        _rb.inertiaTensor = _baseInertiaTensor;
        maxThrustPerMotor = _baseMaxThrustPerMotor;
        motorLagTimeConstant = _baseMotorLagTimeConstant;
        _windForceWorld = Vector3.zero;

        if (stage >= 2)
        {
            float armScale = Random.Range(0.8f, 1.5f);
            for (int i = 0; i < 4; i++)
                motorPositions[i] = _baseMotorLocal[i] * armScale;

            _rb.mass = _baseMass * Mathf.Pow(armScale, 3f);
            Vector3 scaledInertia = _baseInertiaTensor * Mathf.Pow(armScale, 5f);
            _rb.inertiaTensor = new Vector3(
                Mathf.Max(scaledInertia.x, 1e-6f),
                Mathf.Max(scaledInertia.y, 1e-6f),
                Mathf.Max(scaledInertia.z, 1e-6f));

            float thrustMul = Random.Range(0.7f, 1.3f);
            maxThrustPerMotor = _baseMaxThrustPerMotor * thrustMul;
        }

        if (stage >= 3)
        {
            float windMag = Random.Range(0f, 5f);
            _windForceWorld = Random.onUnitSphere * windMag;
            motorLagTimeConstant = Random.Range(0.03f, 0.08f);
        }

        // Stage 4: 70% mid-flight failure, 30% clean — enough failure to learn recovery,
        // enough clean episodes for stable positive reward signal.
        _scheduledFailureTime = -1f;
        if (stage >= 4 && Random.value < 0.7f)
        {
            _scheduledFailureTime = Random.Range(1f, 6f);
            Debug.Log($"[DroneBodyV2] Stage 4 DR: mid-flight failure scheduled at t={_scheduledFailureTime:F2}s.");
        }

        var cfg = BuildConfig();
        _controller?.Initialize(cfg);
        GetComponent<PIDDroneController2>()?.Initialize(cfg);
    }

    float SampleAltitudeAboveGround()
    {
        const float maxRay = 500f;
        Vector3 origin = transform.position;
        float fallback = Mathf.Max(0f, origin.y);

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxRay,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        if (hits != null && hits.Length > 0)
        {
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (h.rigidbody == _rb) continue;
                if (h.collider.transform == transform || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best)
                    best = h.distance;
            }
        }

        if (best < float.MaxValue)
            return best;

#if UNITY_EDITOR
        if (!s_LoggedAltitudeRaycastFallback && Application.isPlaying)
        {
            s_LoggedAltitudeRaycastFallback = true;
            int n = hits != null ? hits.Length : 0;
            Debug.LogWarning(
                $"[DroneBodyV2] Altitude raycast: no usable ground hit (hits={n}, pos={origin}). " +
                $"Using fallback altitude = world y = {fallback:F2}.");
        }
#endif
        return fallback;
    }

    private DroneState BuildState()
    {
        float alt = SampleAltitudeAboveGround();

        Vector3 wWorld = _rb.angularVelocity;
        Vector3 wBody = transform.InverseTransformDirection(wWorld);
        Vector3 tPos = GetTargetPosition();

        return new DroneState
        {
            velocity = _rb.velocity,
            angularVelocity = wBody,
            angularVelocityWorld = wWorld,
            orientation = transform.rotation,
            eulerAngles = transform.eulerAngles,
            position = transform.position,
            targetPosition = tPos,
            relativePositionToTarget = transform.position - tPos,
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

    // Diagonal motor pairs: 0<->3, 1<->2
    private static readonly int[] _diagonalOpposite = { 3, 2, 1, 0 };

    public void DisableMotor(int motorIndex)
    {
        if (motorIndex < 0 || motorIndex >= 4) return;

        motorsActive[motorIndex] = false;
        Debug.Log($"[DroneBodyV2] Motor {motorIndex} disabled!");

        // VFX: spawn smoke/spark at motor world position
        if (motorDeathVFXPrefab != null)
        {
            Vector3 worldPos = transform.TransformPoint(motorPositions[motorIndex]);
            GameObject vfx = Instantiate(motorDeathVFXPrefab, worldPos, Quaternion.identity);
            Destroy(vfx, 3f);
        }

        // Scripted compensation: boost diagonal-opposite motor thrust cap
        if (enableScriptedCompensation)
        {
            int opposite = _diagonalOpposite[motorIndex];
            if (motorsActive[opposite])
            {
                // Temporarily increase max thrust for the opposite motor so it can generate more lift
                maxThrustPerMotor *= compensationThrustMultiplier;
                // Increase angular drag to dampen the yaw spin caused by torque imbalance
                angularDragCoeff *= 2.5f;
                Debug.Log($"[DroneBodyV2] Compensation active: boosting motor {opposite}, angular drag increased.");
            }
        }
    }

    private int _hitCount = 0;

    public void RegisterHit()
    {
        _hitCount++;
        // Hit 5: first motor dies. Hit 7: second motor dies (crash).
        if (_hitCount == 5 || _hitCount == 7)
        {
            int[] active = System.Array.FindAll(new int[] { 0, 1, 2, 3 }, i => motorsActive[i]);
            if (active.Length > 0)
                DisableMotor(active[Random.Range(0, active.Length)]);
        }
    }

    public void ResetHits()
    {
        _hitCount = 0;
    }

    public void ResetMotors()
    {
        ResetHits();
        for (int i = 0; i < 4; i++)
        {
            motorsActive[i] = true;
            _currentThrusts[i] = 0f;
            _actualThrusts[i] = 0f;
        }
        _episodeTime = 0f;

        // Reset compensation-modified values back to inspector baselines
        maxThrustPerMotor = _baseMaxThrustPerMotor;
        angularDragCoeff = 2.0f;

        ApplyDomainRandomizationForEpisode();
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
                Gizmos.DrawLine(worldPos, worldPos + transform.up * _lastMotorOutputs[i] * 0.5f);
            }
        }
    }

    void OnGUI()
    {
        if (!drawMotorForces) return;

        bool isPPO = GetComponent<PPODroneControllerV2>() != null && GetComponent<PPODroneControllerV2>().enabled;
        float xOffset = isPPO ? Screen.width * 0.5f + 10f : 10f;

        GUI.Label(new Rect(xOffset, Screen.height - 60, 500, 25),
            $"Target vel: {targetVelocity:F2} | Actual vel: {_rb?.velocity:F2}");
        GUI.Label(new Rect(xOffset, Screen.height - 35, 500, 25),
            $"Motors: [{_actualThrusts[0]:F2}, {_actualThrusts[1]:F2}, {_actualThrusts[2]:F2}, {_actualThrusts[3]:F2}]");

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 16;
        style.fontStyle = FontStyle.Bold;
        for (int i = 0; i < 4; i++)
        {
            style.normal.textColor = motorsActive[i] ? Color.green : Color.red;
            GUI.Label(new Rect(xOffset, Screen.height - 90 - (i * 20), 200, 20),
                $"Motor {i}: {(motorsActive[i] ? "OK" : "DEAD")} | Hits: {_hitCount}");
        }

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 18;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = isPPO ? Color.cyan : Color.yellow;
        GUI.Label(new Rect(xOffset, Screen.height - 115, 200, 25),
            isPPO ? "PPO V2 Controller" : "PID Controller");
    }
}
