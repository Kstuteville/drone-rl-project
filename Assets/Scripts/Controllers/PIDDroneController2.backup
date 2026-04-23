using UnityEngine;

public class PIDDroneController2 : MonoBehaviour, IDroneController
{
    [System.Serializable]
    public class PIDGains {
        public float kP = 1.0f, kI = 0.0f, kD = 0.0f;
        public PIDGains(float p, float i, float d) { kP = p; kI = i; kD = d; }
    }

    public PIDGains velocityY = new PIDGains(1.2f, 0.0f, 0.3f); // Zero Integral by default
    public PIDGains rollGains = new PIDGains(5.0f, 0.0f, 0.0f);
    public PIDGains pitchGains = new PIDGains(5.0f, 0.0f, 0.0f);
    public PIDGains rateGains = new PIDGains(0.8f, 0.05f, 0.1f);

    private DroneConfig _config;
    private float _hoverThrust;
    private float _recoveryBlend = 0f;
    private Vector3 _velPrev;
    private float _pIntegral, _rIntegral, _yIntegral;

    public void Initialize(DroneConfig config) {
        _config = config;
        _hoverThrust = (config.mass * Mathf.Abs(Physics.gravity.y)) / (4f * config.maxThrustPerMotor);
    }

    public void OnEpisodeReset() { _recoveryBlend = 0; _pIntegral = _rIntegral = _yIntegral = 0; }

    public float[] ComputeMotorThrusts(DroneState state) {
        float dt = Mathf.Max(_config.dt, 0.001f);
        Vector3 angVel = Quaternion.Inverse(state.orientation) * state.angularVelocityWorld;
        
        // Recovery logic: Aggressive damping if spinning
        _recoveryBlend = Mathf.MoveTowards(_recoveryBlend, angVel.magnitude > 1.5f ? 1f : 0f, 5f * dt);
        float tilt = Vector3.Angle(Vector3.up, state.orientation * Vector3.up);

        // 1. Vertical Control: If tilted > 45 deg, STOP CLIMBING.
        float vErr = -state.velocity.y; // Target vertical velocity 0
        float collective = _hoverThrust + (vErr * velocityY.kP);
        if (tilt > 45f) collective = _hoverThrust * 0.5f; // Force a slight drop to prevent rocketing

        // 2. Simple Attitude -> Rate
        float pTarget = _recoveryBlend > 0.5f ? 0 : NormalizeAngle(-state.eulerAngles.x) * 0.1f * pitchGains.kP;
        float rTarget = _recoveryBlend > 0.5f ? 0 : NormalizeAngle(-state.eulerAngles.z) * 0.1f * rollGains.kP;
        
        // 3. Rate Controller
        float pErr = pTarget - angVel.x;
        float rErr = rTarget - angVel.z;
        float yErr = -angVel.y;

        _pIntegral = Mathf.Clamp(_pIntegral + pErr * dt, -1, 1);
        _rIntegral = Mathf.Clamp(_rIntegral + rErr * dt, -1, 1);
        
        // Capped corrections
        float pCorr = Mathf.Clamp(pErr * rateGains.kP + _pIntegral * rateGains.kI, -0.3f, 0.3f);
        float rCorr = Mathf.Clamp(rErr * rateGains.kP + _rIntegral * rateGains.kI, -0.3f, 0.3f);
        float yCorr = Mathf.Clamp(yErr * rateGains.kP, -0.2f, 0.2f);

        // 4. Mixing
        float[] t = new float[4];
        t[0] = collective - rCorr - pCorr - yCorr; // FL
        t[1] = collective + rCorr - pCorr + yCorr; // FR
        t[2] = collective - rCorr + pCorr + yCorr; // RL
        t[3] = collective + rCorr + pCorr - yCorr; // RR

        for (int i = 0; i < 4; i++) t[i] = Mathf.Clamp01(t[i]);
        return t;
    }

    private float NormalizeAngle(float a) => (a > 180) ? a - 360 : (a < -180) ? a + 360 : a;
}