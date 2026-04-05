using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class DroneAgent : Agent, IDroneController
{
    private DroneConfig _config;
    private float[] _currentThrusts = new float[] { 0f, 0f, 0f, 0f };
    private DroneState _lastState;

    [Header("Action Settings")]
    public float maxDelta = 0.5f;

    public void Initialize(DroneConfig config)
    {
        _config = config;
    }

    public void OnEpisodeReset()
    {
        for (int i = 0; i < 4; i++) _currentThrusts[i] = 0f;
        EndEpisode();
    }

    public float[] ComputeMotorThrusts(DroneState state)
    {
        _lastState = state;
        return _currentThrusts;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(_lastState.velocity);
        sensor.AddObservation(_lastState.angularVelocity);
        sensor.AddObservation(_lastState.eulerAngles);
        sensor.AddObservation(_lastState.targetVelocity);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        for (int i = 0; i < 4; i++)
        {
            _currentThrusts[i] = Mathf.Clamp(
                _currentThrusts[i] + actions.ContinuousActions[i]
                * maxDelta * Time.fixedDeltaTime,
                -1f, 1f);
        }

        float velError = Vector3.Distance(
            _lastState.velocity,
            _lastState.targetVelocity);
        float velocityReward = Mathf.Pow(1f - Mathf.Clamp01(velError / 5f), 2f);

        Vector3 euler = _lastState.eulerAngles;
        float tilt = Mathf.Abs(NormalizeAngle(euler.x)) +
                     Mathf.Abs(NormalizeAngle(euler.z));
        float stabilityPenalty = -tilt / 180f * 0.1f;

        float energyPenalty = 0f;
        foreach (float t in _currentThrusts)
            energyPenalty += Mathf.Abs(t);
        energyPenalty *= -0.001f;

        AddReward(velocityReward + stabilityPenalty + energyPenalty);
    }

    public override void OnEpisodeBegin()
    {
        for (int i = 0; i < 4; i++) _currentThrusts[i] = 0f;
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}