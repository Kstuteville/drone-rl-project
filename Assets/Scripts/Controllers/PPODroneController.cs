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

    // ─────────────────────────────────────────────
    // ML-Agents Agent overrides
    // ─────────────────────────────────────────────

    public override void OnEpisodeBegin()
    {
        OnEpisodeReset();
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
            _motorOutputs[i] = Mathf.Clamp(continuous[i], -1f, 1f);

        // ═══════════════════════════════════════════
        // REWARD SHAPING — Kaylie, customize this!
        //
        // Suggested reward components:
        //   +1.0 * velocityTrackingReward   (how close to target velocity)
        //   -0.1 * angularVelocityPenalty    (penalize spinning/wobble)
        //   -0.01 * actionSmoothnessPenalty  (penalize thrust oscillation)
        //   -1.0 * crashPenalty              (large negative if crashed)
        // ═══════════════════════════════════════════

        // Velocity tracking reward (example — tune weights as needed)
        float velError = Vector3.Distance(_latestState.velocity, _latestState.targetVelocity);
        float velReward = Mathf.Exp(-velError); // 1.0 when perfect, decays with error
        AddReward(velReward * 0.1f);

        // Stability penalty — penalize excessive angular velocity
        float angVelMag = _latestState.angularVelocity.magnitude;
        AddReward(-angVelMag * 0.01f);

        // Crash detection — end episode if too tilted or too low
        float tilt = Vector3.Angle(Vector3.up, _latestState.orientation * Vector3.up);
        if (tilt > 80f || _latestState.altitude < 0.2f)
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
