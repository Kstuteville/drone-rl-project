using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Observation space: 23 (same as PPODroneController — no change to YAML needed)
//   relativePositionToTarget : 3
//   rotation 6D              : 6
//   velocity                 : 3
//   angularVelocity          : 3
//   motorOutputs x4          : 4  (-1.0 sentinel = dead motor, 0–1 = live thrust)
//   targetVelocity           : 3
//   altitude                 : 1
public class PPODroneControllerV2 : Agent, IDroneController
{
    public const float ActionDeviationScale = 0.5f;

    private const float SmoothnessCoeff = 0.4f;
    private const float TimePenaltyPerStep = 0.001f;
    private const float AltitudeExpK = 1.5f;
    private const float HorizontalGuidanceExpK = 0.35f;

    private DroneState _latestState;
    private float[] _motorOutputs = new float[4];
    private float[] _previousActions = new float[4];
    private DroneBodyV2 _droneBody;

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

    [Tooltip("Weight on horizontal velocity error penalty.")]
    public float driftWeight = 0.05f;

    [Tooltip("Weight on |v_y| penalty.")]
    public float verticalPenaltyWeight = 0.05f;

    public void Initialize(DroneConfig _)
    {
        _droneBody = GetComponent<DroneBodyV2>();
    }

    void Awake()
    {
        _droneBody = GetComponent<DroneBodyV2>();
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
        _droneBody.ResetMotors();
        _droneBody.RandomizeSpawn();
    }

    public void OnEpisodeReset()
    {
        Rigidbody rb = _droneBody.GetComponent<Rigidbody>();

        // Count active motors so hover command is correct even when one is dead at episode start.
        int activeCount = 0;
        foreach (bool b in _droneBody.motorsActive) if (b) activeCount++;
        activeCount = Mathf.Max(activeCount, 1);

        float hoverCmd = (rb.mass * Mathf.Abs(Physics.gravity.y))
            / (activeCount * Mathf.Max(_droneBody.maxThrustPerMotor, 1e-6f));
        hoverCmd = Mathf.Clamp(hoverCmd, 0f, 1f);

        for (int i = 0; i < 4; i++)
        {
            _motorOutputs[i] = _droneBody.motorsActive[i] ? hoverCmd : 0f;
            _previousActions[i] = _motorOutputs[i];
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
        sensor.AddObservation(_latestState.relativePositionToTarget);         // 3
        AddRotationMatrix6D(sensor, _latestState.orientation);                 // 6

        Vector3 vObs = _latestState.velocity;
        Vector3 wObs = _latestState.angularVelocity;
        if (DroneBodyV2.ReadCurriculumStage() >= 3)
        {
            vObs = ObservationNoise(vObs, 0.02f, 0.05f);
            wObs = ObservationNoise(wObs, 0.02f, 0.05f);
        }

        sensor.AddObservation(vObs);                                           // 3
        sensor.AddObservation(wObs);                                           // 3

        // Dead motor encoded as -1.0 (live thrust is 0–1), so policy can distinguish dead from zero-thrust.
        for (int i = 0; i < 4; i++)
        {
            bool alive = _latestState.motorsActive == null || i >= _latestState.motorsActive.Length || _latestState.motorsActive[i];
            float output = alive ? (_latestState.currentMotorOutputs != null ? _latestState.currentMotorOutputs[i] : 0f) : -1f;
            sensor.AddObservation(output);                                     // 4
        }

        sensor.AddObservation(_latestState.targetVelocity);                   // 3
        sensor.AddObservation(_latestState.altitude);                         // 1
                                                                               // Total: 23
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

        // Survival bonus: only active when a motor is dead — directly rewards staying airborne after failure.
        // Zero effect on normal 4-motor episodes.
        if (_latestState.motorsActive != null)
        {
            foreach (bool active in _latestState.motorsActive)
                if (!active) { AddReward(0.3f); break; }
        }

        float tiltAngle = Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up);
        float worldY = _droneBody != null ? _droneBody.transform.position.y : transform.position.y;
        if (tiltAngle > 80f || worldY < 0.5f || worldY > 20f)
        {
            Debug.Log($"[CRASH] alt={_latestState.altitude:F3} tilt={tiltAngle:F1} vel={_latestState.velocity} " +
                      $"motors=[{_motorOutputs[0]:F3},{_motorOutputs[1]:F3},{_motorOutputs[2]:F3},{_motorOutputs[3]:F3}] " +
                      $"motorHealth=[{(_latestState.motorsActive?[0] == true ? 1 : 0)}," +
                      $"{(_latestState.motorsActive?[1] == true ? 1 : 0)}," +
                      $"{(_latestState.motorsActive?[2] == true ? 1 : 0)}," +
                      $"{(_latestState.motorsActive?[3] == true ? 1 : 0)}] step={StepCount}");
            AddReward(-10f);
            EndEpisode();
        }

        if (StepCount > 0 && StepCount % 500 == 0)
        {
            Debug.Log(
                $"[PPODroneV2Debug] step={StepCount} mass={_droneBody.GetComponent<Rigidbody>().mass:F3} " +
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
            Debug.Log($"[PPODroneV2Debug] Heuristic first call: DroneBodyV2={(_droneBody != null)} PIDDroneController found={hasPid}");
        }

        if (_droneBody == null || !TryGetComponent<PIDDroneController>(out var pid))
        {
            for (int i = 0; i < 4; i++)
                c[i] = 0f;
            return;
        }

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
