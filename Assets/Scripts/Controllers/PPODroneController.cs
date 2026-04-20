using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class PPODroneController : Agent, IDroneController
{
    /// <summary>Maps policy action a to thrust: u = hoverCmd + clamp(a) * scale (must match Heuristic inverse).</summary>
    public const float ActionDeviationScale = 0.5f;

    private const float SmoothnessCoeff = 0.4f;
    private const float TimePenaltyPerStep = 0.001f;
    private const float AltitudeExpK = 1.5f;
    private const float HorizontalGuidanceExpK = 0.35f;

    private DroneState _latestState;
    private float[] _motorOutputs = new float[4];
    private float[] _previousActions = new float[4];
    private DroneBody _droneBody;

    static bool s_LoggedPidPresence;

    [Header("Reward Weights (~55% guidance / ~25% vel+stab / smoothness via fixed -0.4·‖Δu‖²)")]
    [Tooltip("Weight on exp(-|alt - targetAlt| * k).")]
    public float altitudeWeight = 0.22f;

    [Tooltip("Weight on exp(-‖v - v_target‖).")]
    public float velocityWeight = 0.15f;

    [Tooltip("Weight on exp(-‖horizontal rel pos‖ * k) toward hover point.")]
    public float positionGuidanceWeight = 0.28f;

    [Tooltip("Weight on stability term (-tilt/180).")]
    public float stabilityWeight = 0.22f;

    [Tooltip("Weight on horizontal velocity error penalty (negative distance).")]
    public float driftWeight = 0.05f;

    [Tooltip("Weight on |v_y| penalty.")]
    public float verticalPenaltyWeight = 0.05f;

    public void Initialize(DroneConfig _)
    {
        _droneBody = GetComponent<DroneBody>();
    }

    void Awake()
    {
        _droneBody = GetComponent<DroneBody>();
    }

    public float[] ComputeMotorThrusts(DroneState state)
    {
        _latestState = state;
        RequestDecision();
        return _motorOutputs;
    }

    public override void OnEpisodeBegin()
    {
        GetComponent<TestPlanner>()?.EpisodeReset();
        // Do not call OnEpisodeReset() here — ResetMotors() invokes it after domain
        // randomization so hover uses correct mass/thrust; RandomizeSpawn() then
        // aligns body motor arrays before the first FixedUpdate.
        _droneBody.ResetMotors();
        if (_droneBody.randomizeSpawnOnEpisodeBegin)
            _droneBody.RandomizeSpawn();
    }

    public void OnEpisodeReset()
    {
        Rigidbody rb = _droneBody.GetComponent<Rigidbody>();
        float hoverCmd = (rb.mass * Mathf.Abs(Physics.gravity.y))
            / (4f * Mathf.Max(_droneBody.maxThrustPerMotor, 1e-6f));
        hoverCmd = Mathf.Clamp(hoverCmd, 0f, 1f);
        for (int i = 0; i < 4; i++)
        {
            _motorOutputs[i] = hoverCmd;
            _previousActions[i] = hoverCmd;
        }
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    private static void AddRotationMatrix6D(VectorSensor sensor, Quaternion q)
    {
        Matrix4x4 m = Matrix4x4.Rotate(q);
        Vector3 c0 = m.GetColumn(0);
        Vector3 c1 = m.GetColumn(1);
        sensor.AddObservation(c0);
        sensor.AddObservation(c1);
    }

    private static Vector3 ObservationNoise(Vector3 v, float relMin, float relMax)
    {
        float rel = Random.Range(relMin, relMax);
        float scale = rel * (v.magnitude + 0.5f);
        return v + Random.insideUnitSphere * scale;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(_latestState.relativePositionToTarget);
        AddRotationMatrix6D(sensor, _latestState.orientation);

        Vector3 vObs = _latestState.velocity;
        Vector3 wObs = _latestState.angularVelocity;
        if (DroneBody.ReadCurriculumStage() >= 3)
        {
            vObs = ObservationNoise(vObs, 0.02f, 0.05f);
            wObs = ObservationNoise(wObs, 0.02f, 0.05f);
        }

        sensor.AddObservation(vObs);
        sensor.AddObservation(wObs);

        for (int i = 0; i < 4; i++)
            sensor.AddObservation(_latestState.currentMotorOutputs != null ? _latestState.currentMotorOutputs[i] : 0f);

        sensor.AddObservation(_latestState.targetVelocity);
        sensor.AddObservation(_latestState.altitude);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuous = actions.ContinuousActions;

        float hoverCmd = (_droneBody.GetComponent<Rigidbody>().mass * Mathf.Abs(Physics.gravity.y))
            / (4f * Mathf.Max(_droneBody.maxThrustPerMotor, 1e-6f));
        hoverCmd = Mathf.Clamp(hoverCmd, 0f, 1f);

        float diffSq = 0f;
        for (int i = 0; i < 4; i++)
        {
            float u = hoverCmd + Mathf.Clamp(continuous[i], -1f, 1f) * ActionDeviationScale;
            u = Mathf.Clamp(u, -1f, 1f);
            float d = u - _previousActions[i];
            diffSq += d * d;
            _motorOutputs[i] = u;
            _previousActions[i] = u;
        }

        float targetAltitude = _droneBody != null ? _droneBody.GetTargetPosition().y : 5f;
        float altitudeError = Mathf.Abs(_latestState.altitude - targetAltitude);
        float altitudeReward = Mathf.Exp(-altitudeError * AltitudeExpK);

        float velError = Vector3.Distance(_latestState.velocity, _latestState.targetVelocity);
        float velocityReward = Mathf.Exp(-velError);

        Vector2 relH = new Vector2(
            _latestState.relativePositionToTarget.x,
            _latestState.relativePositionToTarget.z);
        float horizRelDist = relH.magnitude;
        float positionGuidanceReward = Mathf.Exp(-horizRelDist * HorizontalGuidanceExpK);

        Vector3 euler = _latestState.eulerAngles;
        float tilt = Mathf.Abs(NormalizeAngle(euler.x)) +
                     Mathf.Abs(NormalizeAngle(euler.z));
        float stabilityPenalty = -tilt / 180f;

        float verticalVelPenalty = Mathf.Abs(_latestState.velocity.y);

        Vector2 actualHorizontal = new Vector2(_latestState.velocity.x, _latestState.velocity.z);
        Vector2 targetHorizontal = new Vector2(_latestState.targetVelocity.x, _latestState.targetVelocity.z);
        float horizontalVelError = Vector2.Distance(actualHorizontal, targetHorizontal);
        float horizontalPenalty = -horizontalVelError;

        AddReward(altitudeReward * altitudeWeight
                + velocityReward * velocityWeight
                + positionGuidanceReward * positionGuidanceWeight
                + stabilityPenalty * stabilityWeight
                - verticalVelPenalty * verticalPenaltyWeight
                + horizontalPenalty * driftWeight
                - TimePenaltyPerStep
                - SmoothnessCoeff * diffSq);

        float tiltAngle = Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up);
        float worldY = _droneBody != null ? _droneBody.transform.position.y : transform.position.y;
        if (tiltAngle > 80f || worldY < 0.5f || worldY > 20f)
        {
            Debug.Log($"[CRASH] {gameObject.name} alt={_latestState.altitude:F3} tilt={Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up):F1} vel={_latestState.velocity} motors=[{_motorOutputs[0]:F3},{_motorOutputs[1]:F3},{_motorOutputs[2]:F3},{_motorOutputs[3]:F3}] step={StepCount}");
            AddReward(-10f);
            // EndEpisode(); // disabled for turret testing
        }

        // TEMP debug: throttle with agent step counter (ML-Agents StepCount).
        if (StepCount > 0 && StepCount % 500 == 0)
        {
            Debug.Log(
                $"[PPODroneDebug] step={StepCount} mass={_droneBody.GetComponent<Rigidbody>().mass:F3} " +
                $"maxThrustPerMotor={_droneBody.maxThrustPerMotor:F3} hoverCmd={hoverCmd:F4} " +
                $"rawA=[{continuous[0]:F3},{continuous[1]:F3},{continuous[2]:F3},{continuous[3]:F3}] " +
                $"motorU=[{_motorOutputs[0]:F3},{_motorOutputs[1]:F3},{_motorOutputs[2]:F3},{_motorOutputs[3]:F3}] " +
                $"alt={_latestState.altitude:F3} tiltDeg={tiltAngle:F1}");
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var c = actionsOut.ContinuousActions;
        if (!s_LoggedPidPresence)
        {
            s_LoggedPidPresence = true;
            bool hasPid = GetComponent<PIDDroneController>() != null;
            Debug.Log($"[PPODroneDebug] Heuristic first call: DroneBody={(_droneBody != null)} PIDDroneController found={hasPid}");
        }

        if (_droneBody == null || !TryGetComponent<PIDDroneController>(out var pid))
        {
            for (int i = 0; i < 4; i++)
                c[i] = 0f;
            return;
        }

        // Expert actions for demonstration recording: inverse of OnActionReceived mapping.
        DroneState state = _droneBody.GetStateSnapshot();
        float[] thrusts = pid.ComputeMotorThrusts(state);
        Rigidbody rb = _droneBody.GetComponent<Rigidbody>();
        float hoverCmd = (rb.mass * Mathf.Abs(Physics.gravity.y))
            / (4f * Mathf.Max(_droneBody.maxThrustPerMotor, 1e-6f));
        hoverCmd = Mathf.Clamp(hoverCmd, 0f, 1f);
        for (int i = 0; i < 4; i++)
        {
            float inv = (thrusts[i] - hoverCmd) / ActionDeviationScale;
            c[i] = Mathf.Clamp(inv, -1f, 1f);
        }
    }
}
