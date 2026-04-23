using UnityEngine;

/// <summary>
/// Robust PID Drone Controller with cascaded attitude control and recovery mode.
/// Handles smooth hovering and projectile impact recovery through angular velocity damping.
/// </summary>
[System.Serializable]
public class PIDGains
{
    [Tooltip("Proportional gain - controls response strength")]
    public float kP = 1.0f;

    [Tooltip("Integral gain - eliminates steady-state error")]
    public float kI = 0.0f;

    [Tooltip("Derivative gain - dampens oscillations")]
    public float kD = 0.0f;

    public PIDGains() { }

    public PIDGains(float p, float i, float d)
    {
        kP = p;
        kI = i;
        kD = d;
    }

    public void Clamp(float min = 0f, float max = 100f)
    {
        kP = Mathf.Clamp(kP, min, max);
        kI = Mathf.Clamp(kI, min, max);
        kD = Mathf.Clamp(kD, min, max);
    }

    public PIDGains Clone() => new PIDGains(kP, kI, kD);
}

/// <summary>
/// Robust PID Controller Implementation
/// </summary>
public class PIDDroneController2 : MonoBehaviour, IDroneController
{
    // ========== VELOCITY PID (Outer Loop) ==========
    [Header("Velocity PID - Outer Loop")]
    public PIDGains velocityXZ = new PIDGains(2.0f, 0.1f, 0.5f);
    public PIDGains velocityY = new PIDGains(3.0f, 0.2f, 0.8f);

    // ========== ATTITUDE PID (Inner Loop - Cascaded) ==========
    [Header("Attitude PID - Inner Loop (Angle → Rate)")]
    public PIDGains rollGains = new PIDGains(5.0f, 0.1f, 1.5f);
    public PIDGains pitchGains = new PIDGains(5.0f, 0.1f, 1.5f);
    public PIDGains yawGains = new PIDGains(3.0f, 0.0f, 0.5f);

    // ========== ANGULAR VELOCITY PID (Direct Rate Control) ==========
    [Header("Angular Rate PID - Recovery Mode")]
    public PIDGains rollRateGains = new PIDGains(0.8f, 0.05f, 0.15f);
    public PIDGains pitchRateGains = new PIDGains(0.8f, 0.05f, 0.15f);
    public PIDGains yawRateGains = new PIDGains(0.5f, 0.0f, 0.1f);

    // ========== RECOVERY & STABILITY ==========
    [Header("Recovery Mode")]
    [Tooltip("Angular velocity threshold (rad/s) to trigger recovery mode")]
    public float recoveryAngularVelocityThreshold = 1.5f;
    [Tooltip("How aggressively to damp angular velocity in recovery")]
    public float recoveryDampingMultiplier = 4.0f;
    [Tooltip("Blend rate between normal and recovery controller")]
    public float recoveryBlendRate = 12.0f;
    [Tooltip("Time to stay in recovery mode after angular velocity drops below threshold")]
    public float recoveryHoldTime = 0.5f;

    [Header("Impact Response")]
    [Tooltip("Enable instant recovery response on high angular velocity")]
    public bool instantRecoveryResponse = true;
    [Tooltip("Maximum angular velocity the controller will try to correct (rad/s)")]
    public float maxCorrectableAngularVelocity = 15f;
    [Tooltip("Thrust boost during recovery to maintain altitude")]
    public float recoveryAltitudeBoost = 0.15f;

    [Header("Stabilization Priority")]
    [Tooltip("When true, ignores velocity commands during recovery and focuses on stabilization")]
    public bool stabilizeFirstDuringRecovery = true;
    [Tooltip("How fast to return to normal control after stabilization")]
    public float recoverySettleRate = 3.0f;

    [Header("Rate Limiting")]
    public float maxTiltCommandRate = 90f;
    public float maxRateCommandRate = 5f;

    [Header("Anti-Windup")]
    public float velocityIntegralLimit = 5.0f;
    public float attitudeIntegralLimit = 10.0f;
    public float rateIntegralLimit = 5.0f;

    [Header("Derivative Filtering")]
    public float velocityDerivativeFilter = 0.3f;
    public float rateDerivativeFilter = 0.4f;

    [Header("Limits")]
    public float maxTiltAngle = 30f;
    public float maxAngularVelocity = 6f;

    [Header("Tilt Boost")]
    public float tiltBoostFactor = 1.0f;

    [Header("Deadband")]
    public float velocityDeadband = 0.1f;
    public float angleDeadband = 0.5f;

    [Header("Debug (Read Only)")]
    [SerializeField] private float _debugRecoveryBlend;
    [SerializeField] private float _debugAngularSpeed;
    [SerializeField] private Vector3 _debugVelocityError;
    [SerializeField] private bool _debugIsStabilizing;
    [SerializeField] private float _debugStabilizationProgress;
    [SerializeField] private float _debugRecoveryTimer;

    // ========== INTERNAL STATE ==========
    private DroneConfig _config;
    private float _hoverThrust;

    // Velocity PID state
    private Vector3 _velErrorIntegral;
    private Vector3 _velErrorPrev;
    private Vector3 _velDerivFiltered;

    // Attitude PID state (angle error)
    private float _rollErrorIntegral, _rollErrorPrev;
    private float _pitchErrorIntegral, _pitchErrorPrev;
    private float _yawErrorIntegral, _yawErrorPrev;
    private float _rollDerivFiltered, _pitchDerivFiltered, _yawDerivFiltered;

    // Rate PID state (direct angular velocity control)
    private float _rollRateIntegral, _rollRatePrev;
    private float _pitchRateIntegral, _pitchRatePrev;
    private float _yawRateIntegral, _yawRatePrev;
    private float _rollRateDerivFiltered, _pitchRateDerivFiltered, _yawRateDerivFiltered;

    // Command smoothing
    private float _rollCmdPrev, _pitchCmdPrev;
    private float _rollRateCmdPrev, _pitchRateCmdPrev;

    // Recovery mode state
    private float _recoveryBlend = 0f;
    private Vector3 _lastAngularVelocity;
    private float _recoveryTimer = 0f;
    private bool _wasInRecovery = false;
    private bool _isStabilizing = false;
    private float _stabilizationProgress = 0f;

    // Motor failure tracking
    private bool[] _motorHealthy = new bool[4] { true, true, true, true };

    public void Initialize(DroneConfig config)
    {
        _config = config;
        _hoverThrust = (config.mass * Mathf.Abs(Physics.gravity.y)) / (4f * config.maxThrustPerMotor);
        OnEpisodeReset();
    }

    public void OnEpisodeReset()
    {
        _velErrorIntegral = Vector3.zero;
        _velErrorPrev = Vector3.zero;
        _velDerivFiltered = Vector3.zero;

        _rollErrorIntegral = _rollErrorPrev = 0f;
        _pitchErrorIntegral = _pitchErrorPrev = 0f;
        _yawErrorIntegral = _yawErrorPrev = 0f;
        _rollDerivFiltered = _pitchDerivFiltered = _yawDerivFiltered = 0f;

        _rollRateIntegral = _rollRatePrev = 0f;
        _pitchRateIntegral = _pitchRatePrev = 0f;
        _yawRateIntegral = _yawRatePrev = 0f;
        _rollRateDerivFiltered = _pitchRateDerivFiltered = _yawRateDerivFiltered = 0f;

        _rollCmdPrev = _pitchCmdPrev = 0f;
        _rollRateCmdPrev = _pitchRateCmdPrev = 0f;

        _recoveryBlend = 0f;
        _lastAngularVelocity = Vector3.zero;

        for (int i = 0; i < 4; i++) _motorHealthy[i] = true;
    }

    public float[] ComputeMotorThrusts(DroneState state)
    {
        float dt = _config.dt > 0f ? _config.dt : Time.fixedDeltaTime;
        dt = Mathf.Max(dt, 0.001f);

        if (state.motorsActive != null)
        {
            for (int i = 0; i < 4; i++)
                _motorHealthy[i] = state.motorsActive[i];
        }

        _debugVelocityError = state.targetVelocity - state.velocity;

        // Get angular velocity in body frame
        Vector3 angularVelocity = GetAngularVelocityInBodyFrame(state);
        _debugAngularSpeed = angularVelocity.magnitude;

        // Update recovery mode state
        UpdateRecoveryMode(angularVelocity, dt, state);
        _debugRecoveryBlend = _recoveryBlend;
        _debugIsStabilizing = _isStabilizing;
        _debugStabilizationProgress = _stabilizationProgress;
        _debugRecoveryTimer = _recoveryTimer;

        // Determine if we should stabilize (ignore velocity commands)
        bool shouldStabilize = _isStabilizing || (_recoveryBlend > 0.3f && stabilizeFirstDuringRecovery);

        // OUTER LOOP: Velocity → Desired Tilt + Collective
        // During recovery, prioritize stabilization over velocity tracking
        Vector3 effectiveTargetVelocity = shouldStabilize ? Vector3.zero : state.targetVelocity;
        Vector3 velError = ComputeDeadbandError(effectiveTargetVelocity - state.velocity, velocityDeadband);

        float collectiveThrust = ComputeCollectiveThrust(state, dt, velError, shouldStabilize);

        float pitchCmd, rollCmd, yawRateCmd;

        if (shouldStabilize)
        {
            // During stabilization: try to level out (pitchCmd = 0, rollCmd = 0)
            pitchCmd = 0f;
            rollCmd = 0f;
            yawRateCmd = 0f;
        }
        else
        {
            pitchCmd = ComputeDesiredPitch(velError, dt);
            rollCmd = ComputeDesiredRoll(velError, dt);
            yawRateCmd = ComputeDesiredYawRate(state, dt);
        }

        pitchCmd = RateLimit(pitchCmd, _pitchCmdPrev, maxTiltCommandRate * dt);
        rollCmd = RateLimit(rollCmd, _rollCmdPrev, maxTiltCommandRate * dt);
        _pitchCmdPrev = pitchCmd;
        _rollCmdPrev = rollCmd;

        float pitchRateCmd = ComputeDesiredPitchRate(pitchCmd, state.eulerAngles.x, angularVelocity.y, dt);
        float rollRateCmd = ComputeDesiredRollRate(rollCmd, state.eulerAngles.z, angularVelocity.x, dt);

        rollRateCmd = RateLimit(rollRateCmd, _rollRateCmdPrev, maxRateCommandRate * dt);
        pitchRateCmd = RateLimit(pitchRateCmd, _pitchRateCmdPrev, maxRateCommandRate * dt);
        _rollRateCmdPrev = rollRateCmd;
        _pitchRateCmdPrev = pitchRateCmd;

        float rollCorr, pitchCorr, yawCorr;

        if (_recoveryBlend > 0.1f)
        {
            var recoveryCorr = ComputeRecoveryControl(rollRateCmd, pitchRateCmd, yawRateCmd, angularVelocity, dt, _recoveryBlend);
            rollCorr = recoveryCorr.x;
            pitchCorr = recoveryCorr.y;
            yawCorr = recoveryCorr.z;
        }
        else
        {
            var normalCorr = ComputeNormalControl(pitchCmd, rollCmd, yawRateCmd, state, angularVelocity, dt);
            rollCorr = normalCorr.x;
            pitchCorr = normalCorr.y;
            yawCorr = normalCorr.z;
        }

        float[] thrust = ComputeMotorMixing(collectiveThrust, rollCorr, pitchCorr, yawCorr);

        for (int i = 0; i < 4; i++)
        {
            if (!_motorHealthy[i]) thrust[i] = 0f;
            thrust[i] = Mathf.Clamp(thrust[i], -0.3f, 1f); // Allow more negative thrust during recovery
        }

        return thrust;
    }

    #region Outer Loop Methods

    private Vector3 ComputeDeadbandError(Vector3 error, float deadband)
    {
        return new Vector3(
            ApplyDeadband(error.x, deadband),
            ApplyDeadband(error.y, deadband),
            ApplyDeadband(error.z, deadband)
        );
    }

    private float ApplyDeadband(float value, float deadband)
    {
        if (Mathf.Abs(value) < deadband) return 0f;
        return value - Mathf.Sign(value) * deadband;
    }

    private float ComputeCollectiveThrust(DroneState state, float dt, Vector3 velError, bool shouldStabilize)
    {
        _velErrorIntegral = ClampVector(_velErrorIntegral + velError * dt, velocityIntegralLimit);
        Vector3 velDeriv = FilterDerivative((velError - _velErrorPrev) / dt, _velDerivFiltered, velocityDerivativeFilter);
        _velErrorPrev = velError;

        float collectiveOffset = PID(velError.y, _velErrorIntegral.y, velDeriv.y, velocityY);

        // During stabilization, add altitude boost to maintain height
        float altitudeBoost = shouldStabilize ? recoveryAltitudeBoost : 0f;

        float tiltAngle = GetTiltAngle(state.orientation);
        float cosTilt = Mathf.Max(Mathf.Cos(tiltAngle * Mathf.Deg2Rad), 0.2f);
        float tiltBoost = tiltAngle > 5f ? (_hoverThrust / cosTilt - _hoverThrust) * tiltBoostFactor : 0f;

        return Mathf.Clamp(_hoverThrust + collectiveOffset + tiltBoost + altitudeBoost, 0f, 1f);
    }

    private float ComputeDesiredPitch(Vector3 velError, float dt)
    {
        float pitchErr = velError.z;
        _pitchErrorIntegral = Mathf.Clamp(_pitchErrorIntegral + pitchErr * dt, -attitudeIntegralLimit, attitudeIntegralLimit);
        float deriv = FilterDerivative((pitchErr - _pitchErrorPrev) / dt, _pitchDerivFiltered, velocityDerivativeFilter);
        _pitchErrorPrev = pitchErr;

        return Mathf.Clamp(PID(pitchErr, _pitchErrorIntegral, deriv, velocityXZ), -maxTiltAngle, maxTiltAngle);
    }

    private float ComputeDesiredRoll(Vector3 velError, float dt)
    {
        float rollErr = -velError.x;
        _rollErrorIntegral = Mathf.Clamp(_rollErrorIntegral + rollErr * dt, -attitudeIntegralLimit, attitudeIntegralLimit);
        float deriv = FilterDerivative((rollErr - _rollErrorPrev) / dt, _rollDerivFiltered, velocityDerivativeFilter);
        _rollErrorPrev = rollErr;

        return Mathf.Clamp(-PID(rollErr, _rollErrorIntegral, deriv, velocityXZ), -maxTiltAngle, maxTiltAngle);
    }

    private float ComputeDesiredYawRate(DroneState state, float dt)
    {
        float yawError = -state.angularVelocityWorld.y;
        _yawErrorIntegral = Mathf.Clamp(_yawErrorIntegral + yawError * dt, -attitudeIntegralLimit, attitudeIntegralLimit);
        float deriv = FilterDerivative((yawError - _yawErrorPrev) / dt, _yawDerivFiltered, velocityDerivativeFilter);
        _yawErrorPrev = yawError;

        return Mathf.Clamp(PID(yawError, _yawErrorIntegral, deriv, yawGains), -2f, 2f);
    }

    #endregion

    #region Cascaded Attitude Control

    private float ComputeDesiredPitchRate(float pitchCmd, float currentPitch, float currentPitchRate, float dt)
    {
        float pitchError = NormalizeAngle(pitchCmd - currentPitch);
        pitchError = ApplyAngleDeadband(pitchError, angleDeadband);
        float desiredRate = pitchError * rollGains.kP * 0.5f;
        return Mathf.Clamp(desiredRate, -maxAngularVelocity, maxAngularVelocity);
    }

    private float ComputeDesiredRollRate(float rollCmd, float currentRoll, float currentRollRate, float dt)
    {
        float rollError = NormalizeAngle(rollCmd - currentRoll);
        rollError = ApplyAngleDeadband(rollError, angleDeadband);
        float desiredRate = rollError * pitchGains.kP * 0.5f;
        return Mathf.Clamp(desiredRate, -maxAngularVelocity, maxAngularVelocity);
    }

    private Vector3 ComputeNormalControl(float pitchCmd, float rollCmd, float yawRateCmd, DroneState state, Vector3 angularVelocity, float dt)
    {
        float pitchRateError = NormalizeAngle(pitchCmd - state.eulerAngles.x);
        float rollRateError = NormalizeAngle(rollCmd - state.eulerAngles.z);
        float yawRateError = yawRateCmd - angularVelocity.z;

        pitchRateError = ApplyAngleDeadband(pitchRateError, angleDeadband);
        rollRateError = ApplyAngleDeadband(rollRateError, angleDeadband);

        float rollDeriv = FilterDerivative((rollRateError - _rollRatePrev) / dt, _rollRateDerivFiltered, rateDerivativeFilter);
        float pitchDeriv = FilterDerivative((pitchRateError - _pitchRatePrev) / dt, _pitchRateDerivFiltered, rateDerivativeFilter);
        float yawDeriv = FilterDerivative((yawRateError - _yawRatePrev) / dt, _yawRateDerivFiltered, rateDerivativeFilter);

        _rollRatePrev = rollRateError;
        _pitchRatePrev = pitchRateError;
        _yawRatePrev = yawRateError;

        _rollRateIntegral = Mathf.Clamp(_rollRateIntegral + rollRateError * dt, -rateIntegralLimit, rateIntegralLimit);
        _pitchRateIntegral = Mathf.Clamp(_pitchRateIntegral + pitchRateError * dt, -rateIntegralLimit, rateIntegralLimit);
        _yawRateIntegral = Mathf.Clamp(_yawRateIntegral + yawRateError * dt, -rateIntegralLimit, rateIntegralLimit);

        float rollCorr = PID(rollRateError, _rollRateIntegral, rollDeriv, rollRateGains);
        float pitchCorr = PID(pitchRateError, _pitchRateIntegral, pitchDeriv, pitchRateGains);
        float yawCorr = PID(yawRateError, _yawRateIntegral, yawDeriv, yawRateGains);

        return new Vector3(rollCorr, pitchCorr, yawCorr);
    }

    #endregion

    #region Recovery Mode

    private void UpdateRecoveryMode(Vector3 angularVelocity, float dt, DroneState state)
    {
        float angularSpeed = angularVelocity.magnitude;

        // Check if we should be in recovery mode
        bool shouldBeInRecovery = angularSpeed > recoveryAngularVelocityThreshold;

        if (shouldBeInRecovery)
        {
            // Enter or stay in recovery
            _recoveryBlend = Mathf.Lerp(_recoveryBlend, 1f, recoveryBlendRate * dt);
            _recoveryTimer = recoveryHoldTime;

            // Check if angular velocity is too high (unrecoverable)
            if (angularSpeed > maxCorrectableAngularVelocity)
            {
                // Still try to damp, but acknowledge we're in a critical state
                _recoveryBlend = 1f;
            }
        }
        else
        {
            // Countdown the hold timer
            if (_recoveryTimer > 0f)
            {
                _recoveryTimer -= dt;
            }

            // Only exit recovery after hold time and angular speed is low
            if (_recoveryTimer <= 0f)
            {
                _recoveryBlend = Mathf.Lerp(_recoveryBlend, 0f, recoveryBlendRate * dt * recoverySettleRate);
            }
        }

        // Handle stabilization state machine
        if (_recoveryBlend > 0.5f)
        {
            // In active recovery - start stabilization
            _isStabilizing = true;
            _stabilizationProgress = 0f;
            _wasInRecovery = true;
        }
        else if (_wasInRecovery && _recoveryBlend < 0.2f)
        {
            // Recovery ending - check if stabilized
            float tiltAngle = GetTiltAngle(state.orientation);

            // Stabilized when tilt is low and angular velocity is low
            if (tiltAngle < 10f && angularSpeed < 0.5f)
            {
                _stabilizationProgress += dt * recoverySettleRate;
                if (_stabilizationProgress >= 1f)
                {
                    _isStabilizing = false;
                    _wasInRecovery = false;
                    _stabilizationProgress = 0f;
                }
            }
            else
            {
                // Not stabilized yet - reset progress
                _stabilizationProgress = 0f;
            }
        }
        else if (_recoveryBlend < 0.05f)
        {
            // Fully recovered - reset everything
            _isStabilizing = false;
            _wasInRecovery = false;
            _stabilizationProgress = 0f;
        }

        _lastAngularVelocity = angularVelocity;
    }

    private Vector3 ComputeRecoveryControl(float rollRateCmd, float pitchRateCmd, float yawRateCmd, Vector3 angularVelocity, float dt, float blend)
    {
        float angularSpeed = angularVelocity.magnitude;

        // During recovery, prioritize stopping rotation over achieving desired rate
        // This is the key to surviving impacts - aggressively damp angular velocity

        // Scale damping based on how severe the angular velocity is
        float severityFactor = Mathf.Clamp01(angularSpeed / maxCorrectableAngularVelocity);
        float adaptiveDamping = recoveryDampingMultiplier * (1f + severityFactor);

        // Compute rate errors (we want angular velocity to go to 0)
        float rollRateError = -angularVelocity.x;
        float pitchRateError = -angularVelocity.y;
        float yawRateError = -angularVelocity.z;

        // Add aggressive damping
        float rollDamping = -angularVelocity.x * adaptiveDamping;
        float pitchDamping = -angularVelocity.y * adaptiveDamping;
        float yawDamping = -angularVelocity.z * adaptiveDamping * 0.5f;

        // During high angular velocity, reduce the derivative term to avoid reacting to our own corrections
        float effectiveRateDerivFilter = rateDerivativeFilter * (1f + severityFactor);
        effectiveRateDerivFilter = Mathf.Min(effectiveRateDerivFilter, 0.9f);

        float rollDeriv = FilterDerivative((rollRateError - _rollRatePrev) / dt, _rollRateDerivFiltered, effectiveRateDerivFilter);
        float pitchDeriv = FilterDerivative((pitchRateError - _pitchRatePrev) / dt, _pitchRateDerivFiltered, effectiveRateDerivFilter);
        float yawDeriv = FilterDerivative((yawRateError - _yawRatePrev) / dt, _yawRateDerivFiltered, effectiveRateDerivFilter);

        _rollRatePrev = rollRateError;
        _pitchRatePrev = pitchRateError;
        _yawRatePrev = yawRateError;

        // During recovery, use higher integral limits to correct large errors faster
        float recoveryIntegralLimit = rateIntegralLimit * 2f;
        _rollRateIntegral = Mathf.Clamp(_rollRateIntegral + rollRateError * dt, -recoveryIntegralLimit, recoveryIntegralLimit);
        _pitchRateIntegral = Mathf.Clamp(_pitchRateIntegral + pitchRateError * dt, -recoveryIntegralLimit, recoveryIntegralLimit);
        _yawRateIntegral = Mathf.Clamp(_yawRateIntegral + yawRateError * dt, -recoveryIntegralLimit, recoveryIntegralLimit);

        // Boost P and D gains during recovery for faster response
        float recoveryGainsBoost = 1f + blend * 0.5f;
        float rollCorr = PID(rollRateError, _rollRateIntegral, rollDeriv, rollRateGains) * recoveryGainsBoost + rollDamping;
        float pitchCorr = PID(pitchRateError, _pitchRateIntegral, pitchDeriv, pitchRateGains) * recoveryGainsBoost + pitchDamping;
        float yawCorr = PID(yawRateError, _yawRateIntegral, yawDeriv, yawRateGains) * recoveryGainsBoost + yawDamping;

        // Blend with normal control (though during recovery, blend should be near 1)
        var normalCorr = ComputeNormalControl(_pitchCmdPrev, _rollCmdPrev, yawRateCmd, new DroneState { eulerAngles = Vector3.zero }, angularVelocity, dt);

        rollCorr = Mathf.Lerp(normalCorr.x, rollCorr, blend);
        pitchCorr = Mathf.Lerp(normalCorr.y, pitchCorr, blend);
        yawCorr = Mathf.Lerp(normalCorr.z, yawCorr, blend);

        return new Vector3(rollCorr, pitchCorr, yawCorr);
    }

    #endregion

    #region Motor Mixing

    private float[] ComputeMotorMixing(float collective, float rollCorr, float pitchCorr, float yawCorr)
    {
        float[] t = new float[4];

        float rollMix = rollCorr * 0.5f;
        float pitchMix = pitchCorr * 0.5f;

        t[0] = collective + rollMix + pitchMix - yawCorr; // FL
        t[1] = collective - rollMix + pitchMix + yawCorr; // FR
        t[2] = collective + rollMix - pitchMix + yawCorr; // RL
        t[3] = collective - rollMix - pitchMix - yawCorr; // RR

        // Recovery braking: add differential thrust to actively counteract rotation
        if (_recoveryBlend > 0.3f)
        {
            // Calculate how much each motor should contribute to counteracting rotation
            float brakingIntensity = _recoveryBlend * 0.5f;

            // Get the dominant rotation axis and apply counter-thrust
            Vector3 angVel = _lastAngularVelocity;

            // For roll (rotation around X axis): adjust FL/FR vs RL/RR
            // For pitch (rotation around Y axis): adjust FL/RL vs FR/RR
            // For yaw (rotation around Z axis): adjust diagonals

            float rollBraking = angVel.x * 0.25f; // Counter-roll with differential front/back
            float pitchBraking = angVel.y * 0.25f; // Counter-pitch with differential left/right

            // Differential braking for yaw (opposite corners)
            float yawBrakingFL = angVel.z * 0.15f;
            float yawBrakingRR = angVel.z * 0.15f;

            t[0] += rollBraking + pitchBraking - yawBrakingFL; // FL: counter roll right + pitch right + yaw CCW
            t[1] += rollBraking - pitchBraking + yawBrakingFL; // FR: counter roll right + pitch left + yaw CW
            t[2] += -rollBraking + pitchBraking - yawBrakingRR; // RL: counter roll left + pitch right + yaw CCW
            t[3] += -rollBraking - pitchBraking + yawBrakingRR; // RR: counter roll left + pitch left + yaw CW

            // Apply overall braking force to reduce total lift (helps with rapid deceleration)
            if (_recoveryBlend > 0.6f)
            {
                float brakeAmount = (_recoveryBlend - 0.6f) * 0.4f;
                t[0] -= brakeAmount;
                t[1] -= brakeAmount;
                t[2] -= brakeAmount;
                t[3] -= brakeAmount;
            }
        }

        // Motor failure compensation
        for (int i = 0; i < 4; i++)
        {
            if (!_motorHealthy[i])
            {
                float deficit = Mathf.Max(0, -t[i]);
                float excess = Mathf.Max(0, t[i] - 0.5f);
                float redistribution = (deficit + excess) / 3f;

                for (int j = 0; j < 4; j++)
                {
                    if (j != i && _motorHealthy[j])
                        t[j] += redistribution;
                }
            }
        }

        return t;
    }

    #endregion

    #region Utility Methods

    private Vector3 GetAngularVelocityInBodyFrame(DroneState state)
    {
        Quaternion worldToBody = Quaternion.Inverse(state.orientation);
        return worldToBody * state.angularVelocityWorld;
    }

    private float GetTiltAngle(Quaternion orientation)
    {
        Vector3 up = orientation * Vector3.up;
        return Vector3.Angle(Vector3.up, up);
    }

    private float NormalizeAngle(float a)
    {
        while (a > 180f) a -= 360f;
        while (a < -180f) a += 360f;
        return a;
    }

    private float ApplyAngleDeadband(float angleError, float deadband)
    {
        if (Mathf.Abs(angleError) < deadband) return 0f;
        return angleError - Mathf.Sign(angleError) * deadband;
    }

    private float RateLimit(float current, float previous, float maxChange)
    {
        float delta = current - previous;
        delta = Mathf.Clamp(delta, -maxChange, maxChange);
        return previous + delta;
    }

    private Vector3 ClampVector(Vector3 v, float limit)
    {
        return new Vector3(
            Mathf.Clamp(v.x, -limit, limit),
            Mathf.Clamp(v.y, -limit, limit),
            Mathf.Clamp(v.z, -limit, limit)
        );
    }

    private float FilterDerivative(float newDeriv, float filteredDeriv, float filterFactor)
    {
        return Mathf.Lerp(filteredDeriv, newDeriv, filterFactor);
    }

    private Vector3 FilterDerivative(Vector3 newDeriv, Vector3 filteredDeriv, float filterFactor)
    {
        return Vector3.Lerp(filteredDeriv, newDeriv, filterFactor);
    }

    private float PID(float error, float integral, float derivative, PIDGains gains)
    {
        return gains.kP * error + gains.kI * integral + gains.kD * derivative;
    }

    #endregion

    #region Debug Gizmos

#if UNITY_EDITOR
    [Header("Gizmo Settings")]
    [SerializeField] private bool _showDebugGizmos = true;
    [SerializeField] private Color _velocityColor = Color.blue;
    [SerializeField] private Color _angularVelColor = Color.yellow;
    [SerializeField] private float _gizmoScale = 0.5f;

    private void OnDrawGizmos()
    {
        if (!_showDebugGizmos || !Application.isPlaying) return;

        var state = new DroneState
        {
            velocity = _debugVelocityError,
            angularVelocityWorld = _lastAngularVelocity,
            targetVelocity = Vector3.zero,
            orientation = transform.rotation
        };

        // Current velocity
        Gizmos.color = _velocityColor;
        Gizmos.DrawRay(transform.position, state.velocity * _gizmoScale);

        // Angular velocity indicator
        Gizmos.color = _angularVelColor;
        Vector3 angOffset = Vector3.right * 1.5f;
        Gizmos.DrawRay(transform.position + angOffset, state.angularVelocityWorld * _gizmoScale * 0.2f);

        // Angular speed sphere
        if (state.angularVelocityWorld.magnitude > 0.1f)
        {
            Gizmos.color = new Color(_angularVelColor.r, _angularVelColor.g, _angularVelColor.b, 0.3f);
            Gizmos.DrawSphere(transform.position + angOffset, state.angularVelocityWorld.magnitude * _gizmoScale * 0.05f);
        }

        // Recovery mode indicator
        if (_recoveryBlend > 0.1f)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, _recoveryBlend * 0.5f);
            Gizmos.DrawWireSphere(transform.position, 0.5f + _recoveryBlend * 0.3f);
        }
    }
#endif

    #endregion
}