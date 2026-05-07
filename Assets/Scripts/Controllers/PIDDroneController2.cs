using UnityEngine;

public class PIDDroneController2 : MonoBehaviour, IDroneController
{
    [System.Serializable]
    public class PIDGains {
        public float kP = 1.0f, kI = 0.0f, kD = 0.0f;
        public PIDGains(float p, float i, float d) { kP = p; kI = i; kD = d; }
    }

    [Header("Gains")]
    public PIDGains liftGains     = new PIDGains(1.5f, 0.4f, 0.25f);
    public PIDGains attitudeGains = new PIDGains(3.5f, 0.0f, 0.0f);
    public PIDGains rateGains     = new PIDGains(0.7f, 0.1f, 0.4f);

    [Header("Wobble / Realism")]
    [Tooltip("Small noise on attitude corrections each step — looks like a real drone.")]
    [Range(0f, 0.08f)]
    public float wobbleIntensity = 0.03f;

    [Header("Horizontal Velocity Damping")]
    [Tooltip("Resists being pushed sideways. 0 = pure attitude hold (drifts forever). ~0.4 = gentle braking.")]
    [Range(0f, 1f)]
    public float horizDampGain = 0.7f;

    private DroneConfig _config;
    private Vector3 _lastAngVel;
    private float _pInt, _rInt, _yInt, _liftInt;
    private float _liftPrev;
    private bool _inRecovery = false;
    private int _lastActiveCount = 4;

    public void Initialize(DroneConfig config) {
        _config = config;
    }

    public void OnEpisodeReset() {
        _pInt = _rInt = _yInt = _liftInt = _liftPrev = 0f;
        _lastAngVel = Vector3.zero;
        _inRecovery = false;
        _lastActiveCount = 4;
    }

    public float[] ComputeMotorThrusts(DroneState state) {
        float dt = Mathf.Max(_config.dt, 0.001f);
        Vector3 angVel = Quaternion.Inverse(state.orientation) * state.angularVelocityWorld;
        float currentTilt = Vector3.Angle(Vector3.up, state.orientation * Vector3.up);

        // 1. IMPACT DETECTION
        if (angVel.magnitude > 4.0f || currentTilt > 45f)
            _inRecovery = true;
        else if (angVel.magnitude < 0.3f && currentTilt < 8f)
            _inRecovery = false;

        // 2. LIFT CONTROL — hover thrust recalculated per-frame based on active motors
        int activeCount = 0;
        if (state.motorsActive != null)
            foreach (bool b in state.motorsActive) if (b) activeCount++;
        activeCount = Mathf.Max(activeCount, 1);

        if (activeCount < _lastActiveCount)
        {
            float oldHover = Mathf.Clamp01((_config.mass * Mathf.Abs(Physics.gravity.y)) /
                (_lastActiveCount * Mathf.Max(_config.maxThrustPerMotor, 1e-6f)));
            float newHover = Mathf.Clamp01((_config.mass * Mathf.Abs(Physics.gravity.y)) /
                (activeCount * Mathf.Max(_config.maxThrustPerMotor, 1e-6f)));
            Debug.Log($"[PID2] Motor lost! activeMotors={activeCount} | " +
                      $"hoverThrust {oldHover:F3} → {newHover:F3} | " +
                      $"liftInt was {_liftInt:F3} (reset to 0) | " +
                      $"mass={_config.mass:F3} maxThrust={_config.maxThrustPerMotor:F3}");
            _liftInt  = 0f;
            _liftPrev = 0f;
        }
        _lastActiveCount = activeCount;

        float hoverThrust = Mathf.Clamp01(
            (_config.mass * Mathf.Abs(Physics.gravity.y)) /
            (activeCount * Mathf.Max(_config.maxThrustPerMotor, 1e-6f)));

        float vErr = -state.velocity.y;
        float vD   = (vErr - _liftPrev) / dt;
        _liftPrev  = vErr;
        _liftInt   = Mathf.Clamp(_liftInt + vErr * dt, -0.2f, 0.2f);
        float liftOffset = vErr * liftGains.kP + _liftInt * liftGains.kI + vD * liftGains.kD;
        liftOffset = Mathf.Clamp(liftOffset, -0.3f, 0.4f);
        float collective = hoverThrust + liftOffset;

        // 3. ATTITUDE -> RATE
        float pTarget = 0f, rTarget = 0f;
        if (!_inRecovery) {
            pTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.x) * 0.1f * attitudeGains.kP, -8f, 8f);
            rTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.z) * 0.1f * attitudeGains.kP, -8f, 8f);
        }

        // Horizontal damping always active — even in recovery, resist lateral drift
        if (horizDampGain > 0f) {
            pTarget = Mathf.Clamp(pTarget - state.velocity.z * horizDampGain * 0.1f, -8f, 8f);
            rTarget = Mathf.Clamp(rTarget + state.velocity.x * horizDampGain * 0.1f, -8f, 8f);
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

        // 5. WOBBLE
        if (wobbleIntensity > 0f) {
            pCorr += Random.Range(-wobbleIntensity, wobbleIntensity);
            rCorr += Random.Range(-wobbleIntensity, wobbleIntensity);
        }

        // 6. MIXING
        float[] t = new float[4];
        t[0] = collective - rCorr - pCorr - yCorr; // FL
        t[1] = collective + rCorr - pCorr + yCorr; // FR
        t[2] = collective - rCorr + pCorr + yCorr; // RL
        t[3] = collective + rCorr + pCorr - yCorr; // RR

        for (int i = 0; i < 4; i++) t[i] = Mathf.Clamp(t[i], 0.0f, 1.0f);

        return t;
    }

    private float NormalizeAngle(float a) => (a > 180) ? a - 360 : (a < -180) ? a + 360 : a;
}
