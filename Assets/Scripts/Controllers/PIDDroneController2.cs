using UnityEngine;

public class PIDDroneController2 : MonoBehaviour, IDroneController
{
    [System.Serializable]
    public class PIDGains {
        public float kP = 1.0f, kI = 0.0f, kD = 0.0f;
        public PIDGains(float p, float i, float d) { kP = p; kI = i; kD = d; }
    }

    [Header("Gains")]
    public PIDGains liftGains     = new PIDGains(1.5f, 0.4f, 0.2f);
    public PIDGains attitudeGains = new PIDGains(3.5f, 0.0f, 0.0f);
    public PIDGains rateGains     = new PIDGains(0.7f, 0.1f, 0.4f);

    [Header("Position Hold")]
    [Tooltip("How aggressively to tilt toward home. Keep this small (0.02–0.08).")]
    public float positionKP = 0.04f;
    [Tooltip("Max tilt angle (deg) the position controller can command.")]
    public float maxReturnTilt = 6f;
    [Tooltip("Horizontal distance (m) within which position hold is considered 'close enough'.")]
    public float positionDeadzone = 0.3f;

    private DroneConfig _config;
    private float       _hoverThrust;
    private Vector3     _lastAngVel;
    private float       _pInt, _rInt, _yInt, _liftInt;
    private bool        _inRecovery = false;

    private Vector3 _homePosition;   // world-space XZ target

    public void Initialize(DroneConfig config) {
        _config      = config;
        _hoverThrust = (config.mass * Mathf.Abs(Physics.gravity.y)) / (4f * config.maxThrustPerMotor);
        _homePosition = transform.position;   // capture spawn once
    }

    public void OnEpisodeReset() {
        _pInt = _rInt = _yInt = _liftInt = 0;
        _lastAngVel = Vector3.zero;
        _inRecovery = false;
        _homePosition = transform.position;   // re-anchor on reset
    }

    public float[] ComputeMotorThrusts(DroneState state) {
        float dt = Mathf.Max(_config.dt, 0.001f);
        Vector3 angVel      = Quaternion.Inverse(state.orientation) * state.angularVelocityWorld;
        float   currentTilt = Vector3.Angle(Vector3.up, state.orientation * Vector3.up);

        // 1. IMPACT DETECTION
float angMag = angVel.magnitude;
if (angMag > 2.0f || currentTilt > 45f) {
    if (!_inRecovery) {
        // Fresh impact — flush the wound-up integrals so they don't fight recovery
        _pInt = _rInt = _liftInt = 0f;
    }
    _inRecovery = true;
} else if (angMag < 0.2f && currentTilt < 5f) {
    _inRecovery = false;
}

        // 2. LIFT
        float vErr   = -state.velocity.y;
        _liftInt     = Mathf.Clamp(_liftInt + vErr * dt, -0.2f, 0.2f);
        float liftOffset = Mathf.Clamp(vErr * liftGains.kP + _liftInt * liftGains.kI, -0.3f, 0.4f);
        float collective = _hoverThrust + liftOffset;

        // 3. POSITION → ATTITUDE SETPOINT
        // Only nudge when stable enough to actually track position
        float pTarget = 0f, rTarget = 0f;

        if (!_inRecovery) {
            // World-space horizontal offset to home
            Vector3 worldPos  = state.position;                          // need this in DroneState (see note)
            Vector3 toHome    = _homePosition - worldPos;
            toHome.y          = 0f;                                      // XZ only

            if (toHome.magnitude > positionDeadzone) {
    // Clamp how much error we feed in — big hits don't get big commands
    Vector3 clampedError = Vector3.ClampMagnitude(toHome, 5f);
    Vector3 localErr = Quaternion.Inverse(state.orientation) * clampedError;

    // Horizontal velocity in local frame for damping
    Vector3 localVel = Quaternion.Inverse(state.orientation) * new Vector3(state.velocity.x, 0f, state.velocity.z);

    float positionKD = 0.4f; // tune this, higher = more braking

    float pitchCmd = Mathf.Clamp( localErr.z * positionKP - localVel.z * positionKD, -maxReturnTilt, maxReturnTilt);
    float rollCmd  = Mathf.Clamp(-localErr.x * positionKP + localVel.x * positionKD, -maxReturnTilt, maxReturnTilt);

    pTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.x) * 0.1f * attitudeGains.kP + pitchCmd, -8f, 8f);
    rTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.z) * 0.1f * attitudeGains.kP + rollCmd,  -8f, 8f);
} else {
                // Close enough — pure attitude hold
                pTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.x) * 0.1f * attitudeGains.kP, -8f, 8f);
                rTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.z) * 0.1f * attitudeGains.kP, -8f, 8f);
            }
        }

        // 4. RATE LOOP
        float pErr = pTarget - angVel.x;
        float rErr = rTarget - angVel.z;
        float yErr = -angVel.y;

        float pD = (angVel.x - _lastAngVel.x) / dt;
        float rD = (angVel.z - _lastAngVel.z) / dt;
        _lastAngVel = angVel;

        _pInt = Mathf.Clamp(_pInt + pErr * dt, -0.5f, 0.5f);
        _rInt = Mathf.Clamp(_rInt + rErr * dt, -0.5f, 0.5f);

        float pCorr = Mathf.Clamp(pErr * rateGains.kP + _pInt * rateGains.kI - pD * rateGains.kD, -0.4f, 0.4f);
        float rCorr = Mathf.Clamp(rErr * rateGains.kP + _rInt * rateGains.kI - rD * rateGains.kD, -0.4f, 0.4f);
        float yCorr = Mathf.Clamp(yErr * rateGains.kP, -0.2f, 0.2f);

        // 5. MIXING
        float[] t = new float[4];
        t[0] = collective - rCorr - pCorr - yCorr; // FL
        t[1] = collective + rCorr - pCorr + yCorr; // FR
        t[2] = collective - rCorr + pCorr + yCorr; // RL
        t[3] = collective + rCorr + pCorr - yCorr; // RR

        for (int i = 0; i < 4; i++) t[i] = Mathf.Clamp(t[i], 0f, 1f);

        return t;
    }

    private float NormalizeAngle(float a) => (a > 180) ? a - 360 : (a < -180) ? a + 360 : a;
}