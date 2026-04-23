using UnityEngine;

public class PIDDroneController2 : MonoBehaviour, IDroneController
{
    [System.Serializable]
    public class PIDGains {
        public float kP = 1.0f, kI = 0.0f, kD = 0.0f;
        public PIDGains(float p, float i, float d) { kP = p; kI = i; kD = d; }
    }

    [Header("Gains")]
    public PIDGains liftGains = new PIDGains(1.5f, 0.4f, 0.2f); // Added I-gain for hover
    public PIDGains attitudeGains = new PIDGains(3.5f, 0.0f, 0.0f);
    public PIDGains rateGains = new PIDGains(0.7f, 0.1f, 0.4f); 

    private DroneConfig _config;
    private float _hoverThrust;
    private Vector3 _lastAngVel;
    private float _pInt, _rInt, _yInt, _liftInt;
    private bool _inRecovery = false;

    public void Initialize(DroneConfig config) {
        _config = config;
        _hoverThrust = (config.mass * Mathf.Abs(Physics.gravity.y)) / (4f * config.maxThrustPerMotor);
    }

    public void OnEpisodeReset() { 
        _pInt = _rInt = _yInt = _liftInt = 0; 
        _lastAngVel = Vector3.zero; 
        _inRecovery = false; 
    }

    public float[] ComputeMotorThrusts(DroneState state) {
        float dt = Mathf.Max(_config.dt, 0.001f);
        Vector3 angVel = Quaternion.Inverse(state.orientation) * state.angularVelocityWorld;
        float currentTilt = Vector3.Angle(Vector3.up, state.orientation * Vector3.up);

        // 1. IMPACT DETECTION
        if (angVel.magnitude > 2.0f || currentTilt > 45f) {
            _inRecovery = true;
            // We DON'T kill lift integral here anymore, so it remembers to stay up
        } else if (angVel.magnitude < 0.2f && currentTilt < 5f) {
            _inRecovery = false;
        }

        // 2. LIFT CONTROL (REPAIRED)
        float vErr = -state.velocity.y;
        
        // The "Hover Battery": Increases thrust as long as we are sinking
        _liftInt = Mathf.Clamp(_liftInt + vErr * dt, -0.2f, 0.2f);
        
        float liftOffset = (vErr * liftGains.kP) + (_liftInt * liftGains.kI);
        liftOffset = Mathf.Clamp(liftOffset, -0.3f, 0.4f); 
        
        float collective = _hoverThrust + liftOffset;

        // 3. ATTITUDE -> RATE
        float pTarget = 0, rTarget = 0;
        if (!_inRecovery) {
            pTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.x) * 0.1f * attitudeGains.kP, -8f, 8f);
            rTarget = Mathf.Clamp(NormalizeAngle(-state.eulerAngles.z) * 0.1f * attitudeGains.kP, -8f, 8f);
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
        // Standard X Mixer signs
        t[0] = collective - rCorr - pCorr - yCorr; // FL
        t[1] = collective + rCorr - pCorr + yCorr; // FR
        t[2] = collective - rCorr + pCorr + yCorr; // RL
        t[3] = collective + rCorr + pCorr - yCorr; // RR

        for (int i = 0; i < 4; i++) t[i] = Mathf.Clamp(t[i], 0.0f, 1.0f);
        
        return t;
    }

    private float NormalizeAngle(float a) => (a > 180) ? a - 360 : (a < -180) ? a + 360 : a;
}