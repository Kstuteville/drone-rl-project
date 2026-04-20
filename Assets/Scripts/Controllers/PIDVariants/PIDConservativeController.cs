using UnityEngine;

/// <summary>
/// Conservative PID variant: smoother, slower response. Lower proportional, higher
/// integral and derivative for anti-oscillation. Narrower tilt envelope.
/// Attach as a sibling to PIDDroneController. Defaults are gain multipliers, not
/// absolute values — when Jessenth tunes base gains, this variant auto-scales.
/// </summary>
public class PIDConservativeController : PIDGainModifier
{
    void Reset()
    {
        gainMultipliers = new Vector3(0.7f, 1.2f, 1.3f);
        maxTiltMultiplier = 0.7f;
    }
}
