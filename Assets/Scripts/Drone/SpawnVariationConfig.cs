using UnityEngine;

/// <summary>
/// Per-drone spawn randomization config. Drives position jitter, tilt, velocity,
/// and optional deterministic seeding so multiple controllers can face identical
/// spawn sequences during evaluation/playtest.
/// </summary>
[System.Serializable]
public class SpawnVariationConfig
{
    [Tooltip("+/- X and Z position jitter in meters (Y handled by yBase/yJitter).")]
    public Vector3 positionJitter = Vector3.zero;

    [Tooltip("Base spawn altitude (meters).")]
    public float yBase = 5f;

    [Tooltip("+/- altitude jitter (meters). Actual Y = yBase +/- yJitter, clamped to >= yMin.")]
    public float yJitter = 0f;

    [Tooltip("Hard floor: spawn Y will never be below this.")]
    public float yMin = 1.5f;

    [Tooltip("+/- tilt (degrees) on roll and pitch at spawn.")]
    public float maxTiltDegrees = 10f;

    [Tooltip("If true, spawn yaw is randomized +/- maxYawDegrees. If false, yaw = 0.")]
    public bool randomizeYaw = true;

    [Tooltip("+/- yaw range (degrees). 180 means full rotation either direction.")]
    public float maxYawDegrees = 180f;

    [Tooltip("+/- linear velocity per axis (m/s).")]
    public Vector3 linearVelocityRange = new Vector3(0.5f, 0.3f, 0.5f);

    [Tooltip("+/- angular velocity per axis (deg/s). Converted to rad/s for Rigidbody.")]
    public Vector3 angularVelocityRange = Vector3.zero;

    [Tooltip("If true, spawn draws from System.Random(seedValue + episodeIndex) for cross-controller reproducibility.")]
    public bool useFixedSeed = false;

    [Tooltip("Seed for deterministic spawning. Playtest preset uses 42.")]
    public int seedValue = 0;

    public SpawnVariationConfig Clone()
    {
        return new SpawnVariationConfig
        {
            positionJitter = this.positionJitter,
            yBase = this.yBase,
            yJitter = this.yJitter,
            yMin = this.yMin,
            maxTiltDegrees = this.maxTiltDegrees,
            randomizeYaw = this.randomizeYaw,
            maxYawDegrees = this.maxYawDegrees,
            linearVelocityRange = this.linearVelocityRange,
            angularVelocityRange = this.angularVelocityRange,
            useFixedSeed = this.useFixedSeed,
            seedValue = this.seedValue,
        };
    }

    /// <summary>Matches the pre-refactor RandomizeSpawn numerically: fixed (0,5,0), tilt 10, yaw full, linVel (0.5/0.3/0.5), no angVel.</summary>
    public static SpawnVariationConfig Easy()
    {
        return new SpawnVariationConfig
        {
            positionJitter = Vector3.zero,
            yBase = 5f,
            yJitter = 0f,
            yMin = 1.5f,
            maxTiltDegrees = 10f,
            randomizeYaw = true,
            maxYawDegrees = 180f,
            linearVelocityRange = new Vector3(0.5f, 0.3f, 0.5f),
            angularVelocityRange = Vector3.zero,
            useFixedSeed = false,
            seedValue = 0,
        };
    }

    public static SpawnVariationConfig Mixed()
    {
        return new SpawnVariationConfig
        {
            positionJitter = new Vector3(2f, 0f, 2f),
            yBase = 5f,
            yJitter = 2f,
            yMin = 1.5f,
            maxTiltDegrees = 15f,
            randomizeYaw = true,
            maxYawDegrees = 180f,
            linearVelocityRange = new Vector3(0.7f, 0.4f, 0.7f),
            angularVelocityRange = new Vector3(20f, 20f, 20f),
            useFixedSeed = false,
            seedValue = 0,
        };
    }

    public static SpawnVariationConfig Stress()
    {
        return new SpawnVariationConfig
        {
            positionJitter = new Vector3(4f, 0f, 4f),
            yBase = 5f,
            yJitter = 3f,
            yMin = 1.5f,
            maxTiltDegrees = 25f,
            randomizeYaw = true,
            maxYawDegrees = 180f,
            linearVelocityRange = new Vector3(1.0f, 0.5f, 1.0f),
            angularVelocityRange = new Vector3(45f, 45f, 45f),
            useFixedSeed = false,
            seedValue = 0,
        };
    }

    /// <summary>
    /// Matched-sequence spawn for cross-controller playtest comparison. Seed = 42 so
    /// all 3 PPO + 3 PID variants face identical episode sequences.
    /// </summary>
    public static SpawnVariationConfig Playtest()
    {
        var cfg = Mixed();
        cfg.useFixedSeed = true;
        cfg.seedValue = 42;
        return cfg;
    }
}
