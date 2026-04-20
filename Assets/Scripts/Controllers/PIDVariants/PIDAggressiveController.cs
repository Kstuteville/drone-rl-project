using UnityEngine;

/// <summary>
/// Aggressive PID variant: snappier, more oscillation-prone. Higher proportional,
/// lower integral and derivative. Wider tilt envelope for quicker maneuvers.
/// Attach as a sibling to PIDDroneController. Defaults are gain multipliers, not
/// absolute values — when Jessenth tunes base gains, this variant auto-scales.
/// </summary>
public class PIDAggressiveController : PIDGainModifier
{
    void Reset()
    {
        gainMultipliers = new Vector3(1.4f, 0.8f, 0.7f);
        maxTiltMultiplier = 1.3f;
    }
}
