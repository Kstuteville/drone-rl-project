// =============================================================
// PIDDroneController.cs — Hand-tuned PID baseline controller
// Drop into: Assets/Scripts/Controllers/
//
// Architecture: Cascaded PID (standard quadrotor control)
//
//   Planner target velocity
//         │
//    ┌────▼─────┐
//    │ Velocity  │  Outer loop: velocity error → desired tilt
//    │   PID     │  angles + collective thrust
//    └────┬─────┘
//         │ desired roll, pitch, yaw_rate, collective
//    ┌────▼─────┐
//    │ Attitude  │  Inner loop: orientation error → torque
//    │   PID     │  commands (roll, pitch, yaw corrections)
//    └────┬─────┘
//         │ roll_cmd, pitch_cmd, yaw_cmd, collective
//    ┌────▼─────┐
//    │  Motor    │  Maps 4 abstract commands → 4 motor thrusts
//    │  Mixer    │  Respects motorsActive[] mask
//    └────┬─────┘
//         │ float[4] in [-1, 1]
//         ▼
//    DroneBody applies as Rigidbody forces
//
// =============================================================

using UnityEngine;

[System.Serializable]
public class PIDGains
{
    public float kP = 1.0f;
    public float kI = 0.0f;
    public float kD = 0.1f;

    public PIDGains(float p, float i, float d)
    {
        kP = p;
        kI = i;
        kD = d;
    }
}

public class PIDDroneController : MonoBehaviour, IDroneController
{
    // ─────────────────────────────────────────────
    // Tunable gains — exposed in Inspector for tuning
    // ─────────────────────────────────────────────

    [Header("Velocity PID (Outer Loop)")]
    [Tooltip("Horizontal velocity tracking (X/Z)")]
    public PIDGains velocityXZ = new PIDGains(2.0f, 0.1f, 0.5f);

    [Tooltip("Vertical velocity tracking (Y / altitude)")]
    public PIDGains velocityY = new PIDGains(3.0f, 0.2f, 0.8f);

    [Header("Attitude PID (Inner Loop)")]
    [Tooltip("Roll stabilization")]
    public PIDGains rollGains = new PIDGains(4.0f, 0.05f, 1.5f);

    [Tooltip("Pitch stabilization")]
    public PIDGains pitchGains = new PIDGains(4.0f, 0.05f, 1.5f);

    [Tooltip("Yaw rate damping")]
    public PIDGains yawGains = new PIDGains(2.0f, 0.0f, 0.5f);

    [Header("Limits")]
    [Tooltip("Max tilt angle (degrees) the velocity PID can request")]
    public float maxTiltAngle = 30f;

    [Tooltip("Max yaw rate (deg/s) — keeps yaw from spinning wildly")]
    public float maxYawRate = 90f;

    [Tooltip("Anti-windup: clamp integral term to this magnitude")]
    public float integralClamp = 5.0f;

    // ─────────────────────────────────────────────
    // Internal state
    // ─────────────────────────────────────────────

    private DroneConfig _config;

    // Velocity PID integral/derivative state
    private Vector3 _velErrorIntegral;
    private Vector3 _velErrorPrev;

    // Attitude PID integral/derivative state
    private float _rollErrorIntegral, _rollErrorPrev;
    private float _pitchErrorIntegral, _pitchErrorPrev;
    private float _yawErrorIntegral, _yawErrorPrev;

    // Hover thrust: fraction of max thrust needed to hover
    // = (mass * gravity) / (4 * maxThrustPerMotor)
    private float _hoverThrust;

    // ─────────────────────────────────────────────
    // IDroneController implementation
    // ─────────────────────────────────────────────

    public void Initialize(DroneConfig config)
    {
        _config = config;
        _hoverThrust = (config.mass * Physics.gravity.magnitude)
                       / (4f * config.maxThrustPerMotor);
        OnEpisodeReset();
    }

    public void OnEpisodeReset()
    {
        _velErrorIntegral = Vector3.zero;
        _velErrorPrev = Vector3.zero;
        _rollErrorIntegral = 0f;
        _rollErrorPrev = 0f;
        _pitchErrorIntegral = 0f;
        _pitchErrorPrev = 0f;
        _yawErrorIntegral = 0f;
        _yawErrorPrev = 0f;
    }

    public float[] ComputeMotorThrusts(DroneState state)
    {
        float dt = _config.dt;
        if (dt <= 0f) dt = Time.fixedDeltaTime;

        // ═══════════════════════════════════════════
        // STEP 1: Velocity PID → desired tilt + collective thrust
        // ═══════════════════════════════════════════

        Vector3 velError = state.targetVelocity - state.velocity;

        // Integrate (with anti-windup clamp)
        _velErrorIntegral += velError * dt;
        _velErrorIntegral = ClampVector(_velErrorIntegral, integralClamp);

        // Derivative
        Vector3 velErrorDerivative = (velError - _velErrorPrev) / dt;
        _velErrorPrev = velError;

        // Horizontal PID output → desired tilt angles
        // X velocity error → pitch (tilting forward/back accelerates in X)
        // Z velocity error → roll  (tilting left/right accelerates in Z)
        float pitchCmd = PIDStep(velError.z, _velErrorIntegral.z,
            velErrorDerivative.z, velocityXZ);
        float rollCmd  = -PIDStep(velError.x, _velErrorIntegral.x,
            velErrorDerivative.x, velocityXZ);

        // Clamp to max tilt
        pitchCmd = Mathf.Clamp(pitchCmd, -maxTiltAngle, maxTiltAngle);
        rollCmd  = Mathf.Clamp(rollCmd, -maxTiltAngle, maxTiltAngle);

        // Vertical PID output → collective thrust offset from hover
        float collectiveOffset = PIDStep(velError.y, _velErrorIntegral.y,
            velErrorDerivative.y, velocityY);
        float collective = _hoverThrust + collectiveOffset;
        collective = Mathf.Clamp(collective, 0f, 1f);

        // ═══════════════════════════════════════════
        // STEP 2: Attitude PID → torque corrections
        // ═══════════════════════════════════════════

        // Current euler angles
        float currentRoll  = NormalizeAngle(state.eulerAngles.z);
        float currentPitch = NormalizeAngle(state.eulerAngles.x);
        float currentYaw   = state.angularVelocityWorld.y * Mathf.Rad2Deg;

        // Roll error: desired vs actual
        float rollError = rollCmd - currentRoll;
        _rollErrorIntegral = Mathf.Clamp(
            _rollErrorIntegral + rollError * dt, -integralClamp, integralClamp);
        float rollDerivative = (rollError - _rollErrorPrev) / dt;
        _rollErrorPrev = rollError;
        float rollCorrection = PIDStep(rollError, _rollErrorIntegral,
            rollDerivative, rollGains);

        // Pitch error: desired vs actual
        float pitchError = pitchCmd - currentPitch;
        _pitchErrorIntegral = Mathf.Clamp(
            _pitchErrorIntegral + pitchError * dt, -integralClamp, integralClamp);
        float pitchDerivative = (pitchError - _pitchErrorPrev) / dt;
        _pitchErrorPrev = pitchError;
        float pitchCorrection = PIDStep(pitchError, _pitchErrorIntegral,
            pitchDerivative, pitchGains);

        // Yaw: just damp rotation rate (no target heading)
        float yawError = 0f - currentYaw; // target = 0 yaw rate
        _yawErrorIntegral = Mathf.Clamp(
            _yawErrorIntegral + yawError * dt, -integralClamp, integralClamp);
        float yawDerivative = (yawError - _yawErrorPrev) / dt;
        _yawErrorPrev = yawError;
        float yawCorrection = PIDStep(yawError, _yawErrorIntegral,
            yawDerivative, yawGains);

        // ═══════════════════════════════════════════
        // STEP 3: Motor mixing
        // ═══════════════════════════════════════════
        //
        // Standard quadrotor "X" configuration:
        //
        //     FL (0)──────FR (1)
        //         \      /
        //          \    /
        //           ──
        //          /    \
        //         /      \
        //     RL (2)──────RR (3)
        //
        // FL, RR spin clockwise   (positive yaw torque)
        // FR, RL spin counter-CW  (negative yaw torque)

        float[] thrusts = new float[4];

        thrusts[0] = collective + rollCorrection + pitchCorrection - yawCorrection; // FL
        thrusts[1] = collective - rollCorrection + pitchCorrection + yawCorrection; // FR
        thrusts[2] = collective + rollCorrection - pitchCorrection + yawCorrection; // RL
        thrusts[3] = collective - rollCorrection - pitchCorrection - yawCorrection; // RR

        // ═══════════════════════════════════════════
        // STEP 4: Apply motor failure mask + clamp to [-1, 1]
        // ═══════════════════════════════════════════

        for (int i = 0; i < 4; i++)
        {
            // Dead motors output zero — PID cannot adapt beyond
            // its fixed gains (this is the key asymmetry vs PPO)
            if (state.motorsActive != null && !state.motorsActive[i])
            {
                thrusts[i] = 0f;
            }

            thrusts[i] = Mathf.Clamp(thrusts[i], -1f, 1f);
        }

        return thrusts;
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private float PIDStep(float error, float integral, float derivative,
        PIDGains gains)
    {
        return gains.kP * error + gains.kI * integral + gains.kD * derivative;
    }

    /// Normalize angle to [-180, 180]
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    private Vector3 ClampVector(Vector3 v, float maxMagnitude)
    {
        return new Vector3(
            Mathf.Clamp(v.x, -maxMagnitude, maxMagnitude),
            Mathf.Clamp(v.y, -maxMagnitude, maxMagnitude),
            Mathf.Clamp(v.z, -maxMagnitude, maxMagnitude)
        );
    }
}
