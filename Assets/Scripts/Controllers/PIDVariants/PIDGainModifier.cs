using UnityEngine;

/// <summary>
/// Base class for PID variant controllers (Conservative, Aggressive). Scales the gains
/// on a sibling PIDDroneController at Awake so the base controller auto-picks up
/// Jessenth's tuned values — variants never hold concrete gain numbers, only multipliers.
///
/// Design note (deviation from plan): variants are gain-modifier MonoBehaviours, not
/// IDroneControllers. Rationale: DroneBody.Awake selects its controller via
/// GetComponent&lt;PIDDroneController&gt;(); a sibling IDroneController that wraps the
/// base would require DroneBody edits to be picked up, and would run the PID math
/// twice per frame. A pure gain-scaler gives identical observable behavior with
/// zero DroneBody changes.
/// </summary>
[RequireComponent(typeof(PIDDroneController))]
public abstract class PIDGainModifier : MonoBehaviour
{
    [Header("Gain multipliers (kP, kI, kD) — applied to every PID channel")]
    public Vector3 gainMultipliers = Vector3.one;

    [Header("Max tilt angle multiplier — allows wider/narrower maneuver envelope")]
    public float maxTiltMultiplier = 1f;

    private PIDDroneController _base;

    // Cached originals for restore-on-disable
    private PIDGains _origVelocityXZ;
    private PIDGains _origVelocityY;
    private PIDGains _origRoll;
    private PIDGains _origPitch;
    private PIDGains _origYaw;
    private float _origMaxTilt;
    private bool _scaledApplied;

    void Awake()
    {
        _base = GetComponent<PIDDroneController>();
        if (_base == null)
        {
            Debug.LogError($"[{GetType().Name}] Requires sibling PIDDroneController.");
            return;
        }
        ApplyScaling();
    }

    void OnDisable()
    {
        RestoreOriginals();
    }

    void ApplyScaling()
    {
        if (_scaledApplied || _base == null) return;

        _origVelocityXZ = Copy(_base.velocityXZ);
        _origVelocityY = Copy(_base.velocityY);
        _origRoll = Copy(_base.rollGains);
        _origPitch = Copy(_base.pitchGains);
        _origYaw = Copy(_base.yawGains);
        _origMaxTilt = _base.maxTiltAngle;

        _base.velocityXZ = Scale(_base.velocityXZ, gainMultipliers);
        _base.velocityY = Scale(_base.velocityY, gainMultipliers);
        _base.rollGains = Scale(_base.rollGains, gainMultipliers);
        _base.pitchGains = Scale(_base.pitchGains, gainMultipliers);
        _base.yawGains = Scale(_base.yawGains, gainMultipliers);
        _base.maxTiltAngle = _origMaxTilt * maxTiltMultiplier;

        _scaledApplied = true;
    }

    void RestoreOriginals()
    {
        if (!_scaledApplied || _base == null) return;
        _base.velocityXZ = _origVelocityXZ;
        _base.velocityY = _origVelocityY;
        _base.rollGains = _origRoll;
        _base.pitchGains = _origPitch;
        _base.yawGains = _origYaw;
        _base.maxTiltAngle = _origMaxTilt;
        _scaledApplied = false;
    }

    static PIDGains Copy(PIDGains g) => new PIDGains(g.kP, g.kI, g.kD);

    static PIDGains Scale(PIDGains g, Vector3 m) =>
        new PIDGains(g.kP * m.x, g.kI * m.y, g.kD * m.z);
}
