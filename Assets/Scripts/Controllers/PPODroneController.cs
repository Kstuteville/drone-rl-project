using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class PPODroneController : Agent, IDroneController
{
    private DroneConfig _config;
    private DroneState _latestState;
    private float[] _motorOutputs = new float[4];
    private DroneBody _droneBody;

    [Header("Reward Weights")]
    public float altitudeWeight = 0.5f;
    public float velocityWeight = 0.5f;
    public float stabilityWeight = 1.0f;
    public float energyWeight = 0.001f;
    public float driftWeight = 0.1f;
    public float verticalPenaltyWeight = 0.05f;
    public float survivalBonus = 0.005f;

    public void Initialize(DroneConfig config)
    {
        _config = config;
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
        OnEpisodeReset();
        _droneBody.ResetMotors();
        if (_droneBody.randomizeSpawnOnEpisodeBegin)
            _droneBody.RandomizeSpawn();
    }

    public void OnEpisodeReset()
    {
        for (int i = 0; i < 4; i++)
            _motorOutputs[i] = 0f;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(_latestState.velocity);
        sensor.AddObservation(_latestState.angularVelocity);
        sensor.AddObservation(_latestState.orientation);
        sensor.AddObservation(_latestState.position);

        for (int i = 0; i < 4; i++)
            sensor.AddObservation(_latestState.motorsActive != null && _latestState.motorsActive[i] ? 1f : 0f);

        for (int i = 0; i < 4; i++)
            sensor.AddObservation(_latestState.currentMotorOutputs != null ? _latestState.currentMotorOutputs[i] : 0f);

        sensor.AddObservation(_latestState.targetVelocity);
        sensor.AddObservation(_latestState.altitude);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var continuous = actions.ContinuousActions;
        for (int i = 0; i < 4; i++)
        {
            float target = Mathf.Clamp(continuous[i], -1f, 1f);
            _motorOutputs[i] = Mathf.Lerp(_motorOutputs[i], target, 0.05f);
        }

        // 1. Altitude keeping
        float targetAltitude = 5f;
        float altitudeError = Mathf.Abs(_latestState.altitude - targetAltitude);
        float altitudeReward = Mathf.Exp(-altitudeError * 1.5f);

        // 2. Velocity tracking
        float velError = Vector3.Distance(_latestState.velocity, _latestState.targetVelocity);
        float velocityReward = Mathf.Exp(-velError);

        // 3. Stability
        Vector3 euler = _latestState.eulerAngles;
        float tilt = Mathf.Abs(NormalizeAngle(euler.x)) +
                     Mathf.Abs(NormalizeAngle(euler.z));
        float stabilityPenalty = -tilt / 180f;

        // 4. Energy
        float energyPenalty = 0f;
        foreach (float t in _motorOutputs)
            energyPenalty += Mathf.Abs(t);
        energyPenalty *= -1f;

        // 5. Vertical velocity penalty
        float verticalVelPenalty = Mathf.Abs(_latestState.velocity.y);

        // 6. Horizontal drift penalty
        Vector2 actualHorizontal = new Vector2(_latestState.velocity.x, _latestState.velocity.z);
        Vector2 targetHorizontal = new Vector2(_latestState.targetVelocity.x, _latestState.targetVelocity.z);
        float horizontalError = Vector2.Distance(actualHorizontal, targetHorizontal);
        float horizontalPenalty = -horizontalError;

        // Combined — weights controlled from inspector
        AddReward(altitudeReward * altitudeWeight
                + velocityReward * velocityWeight
                + stabilityPenalty * stabilityWeight
                + energyPenalty * energyWeight
                + survivalBonus
                - verticalVelPenalty * verticalPenaltyWeight
                + horizontalPenalty * driftWeight);

        // Crash detection
        float tiltAngle = Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up);
        if (tiltAngle > 120f || _latestState.altitude < 0.2f || _latestState.altitude > 25f)
        {
            AddReward(-1.0f);
            EndEpisode();
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var c = actionsOut.ContinuousActions;
        for (int i = 0; i < 4; i++)
            c[i] = 0f;
    }
}