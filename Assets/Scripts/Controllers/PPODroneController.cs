// =============================================================
// PPODroneController.cs — ML-Agents PPO controller stub
//
// Implements both Agent (ML-Agents) and IDroneController so it
// plugs directly into DroneBody with zero changes.
//
// Kaylie: Fill in reward shaping, curriculum, and training config.
// The observation space and action space are ready to use.
//
// Observation space (25 floats):
//   velocity (3) + angularVelocity (3) + orientation quat (4) +
//   position (3) + motorHealth (4) + currentOutputs (4) +
//   targetVelocity (3) + altitude (1)
//
// Action space (4 continuous):
//   Motor thrust targets [-1, 1] for FL, FR, RL, RR
//   DroneBody handles slew-rate limiting and motor lag.
// =============================================================

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

    // ─────────────────────────────────────────────
    // IDroneController implementation
    // ─────────────────────────────────────────────

    public void Initialize(DroneConfig config)
    {
        _config = config;
        _droneBody = GetComponent<DroneBody>();
    }

    public float[] ComputeMotorThrusts(DroneState state)
    {
        _latestState = state;
        RequestDecision();
        return _motorOutputs;
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

    // ─────────────────────────────────────────────
    // ML-Agents Agent overrides
    // ─────────────────────────────────────────────

    public override void OnEpisodeBegin()
    {
            
        OnEpisodeReset();
        // Reset drone position
        DroneReset droneReset = GetComponent<DroneReset>();
        if (droneReset != null)
            droneReset.ResetDrone();

    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Velocity (3)
        sensor.AddObservation(_latestState.velocity);

        // Angular velocity (3)
        sensor.AddObservation(_latestState.angularVelocity);

        // Orientation as quaternion (4) — more stable than Euler for NN
        sensor.AddObservation(_latestState.orientation);

        // Position (3)
        sensor.AddObservation(_latestState.position);

        // Motor health mask (4)
        for (int i = 0; i < 4; i++)
            sensor.AddObservation(_latestState.motorsActive != null && _latestState.motorsActive[i] ? 1f : 0f);

        // Current actual motor outputs post-lag (4)
        for (int i = 0; i < 4; i++)
            sensor.AddObservation(_latestState.currentMotorOutputs != null ? _latestState.currentMotorOutputs[i] : 0f);

        // Target velocity from planner (3)
        sensor.AddObservation(_latestState.targetVelocity);

        // Altitude (1)
        sensor.AddObservation(_latestState.altitude);

        // Total: 25 observations
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
       var continuous = actions.ContinuousActions;
for (int i = 0; i < 4; i++)
{
    float target = Mathf.Clamp(continuous[i], -1f, 1f);
    _motorOutputs[i] = Mathf.Lerp(_motorOutputs[i], target, 0.05f);
}

        // ═══════════════════════════════════════════
        // REWARD SHAPING — Kaylie, customize this!
        //
        // Suggested reward components:
        //   +1.0 * velocityTrackingReward   (how close to target velocity)
        //   -0.1 * angularVelocityPenalty    (penalize spinning/wobble)
        //   -0.01 * actionSmoothnessPenalty  (penalize thrust oscillation)
        //   -1.0 * crashPenalty              (large negative if crashed)
        // ═══════════════════════════════════════════

    // 1. Altitude keeping — must stay near spawn height
    float targetAltitude = 5f;
    float altitudeError = Mathf.Abs(_latestState.altitude - targetAltitude);
    float altitudeReward = Mathf.Exp(-altitudeError * 1.5f);

    // 2. Velocity tracking — must match planner target
    float velError = Vector3.Distance(_latestState.velocity, _latestState.targetVelocity);
    float velocityReward = Mathf.Exp(-velError);

    // 3. Stability — must stay upright
    Vector3 euler = _latestState.eulerAngles;
    float tilt = Mathf.Abs(NormalizeAngle(euler.x)) +
                 Mathf.Abs(NormalizeAngle(euler.z));
    float stabilityPenalty = -tilt / 180f * 1.0f;

    // 4. Energy — discourage oscillation
    float energyPenalty = 0f;
    foreach (float t in _motorOutputs)
        energyPenalty += Mathf.Abs(t);
    energyPenalty *= -0.001f;

    // 5. Survival bonus
    float survivalBonus = 0.005f;

    // 6 Vertical velocity penalty — no oscillating
    float verticalVelPenalty = Mathf.Abs(_latestState.velocity.y) * 0.05f;
    

    // Horizontal drift penalty
    float horizontalVel = new Vector2(_latestState.velocity.x, _latestState.velocity.z).magnitude;
    float horizontalPenalty = -horizontalVel * 0.05f;
    

    // Combined
    AddReward(altitudeReward * 0.5f + velocityReward * 0.5f + stabilityPenalty + energyPenalty + survivalBonus - verticalVelPenalty + horizontalPenalty);
    // Crash detection
    float tiltAngle = Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up);
    if (tiltAngle > 80f || _latestState.altitude < 0.2f || _latestState.altitude > 15f)
    {
        AddReward(-1.0f);
        EndEpisode();
    }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Fallback when not training: output zero (hover via PID-like behavior)
        var c = actionsOut.ContinuousActions;
        for (int i = 0; i < 4; i++)
            c[i] = 0f;
    }
}
