// =============================================================
// IDroneController.cs — Shared interface for PPO and PID
// Drop into: Assets/Scripts/Controllers/
//
// CRITICAL CONTRACT: This file defines the boundary between
// the planner (what the drone wants to do) and the controller
// (how it moves). Both PPO and PID implement this identically.
// =============================================================

using UnityEngine;

/// <summary>
/// Immutable snapshot of the drone's physics state, passed to
/// controllers every FixedUpdate. Both PPO and PID see the
/// exact same struct — no hidden information advantage.
/// </summary>
public struct DroneState
{
    /// World-space linear velocity (m/s)
    public Vector3 velocity;

    /// World-space angular velocity (rad/s)
    public Vector3 angularVelocity;

    /// Current orientation quaternion
    public Quaternion orientation;

    /// Euler angles (degrees) — convenience for PID error calc
    public Vector3 eulerAngles;

    /// World-space position
    public Vector3 position;

    /// Which motors are currently functional (true = alive)
    /// Index: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
    public bool[] motorsActive;

    /// Current normalized thrust output per motor [-1, 1]
    public float[] currentMotorOutputs;

    /// Target velocity from the shared planner (world-space, m/s)
    public Vector3 targetVelocity;

    /// Altitude above ground (raycast down)
    public float altitude;
}

/// <summary>
/// Every controller — PPO or PID — implements this interface.
/// The DroneBody calls ComputeMotorThrusts() in FixedUpdate and
/// applies the returned values as Rigidbody forces. Swapping
/// controllers is a single-line change.
/// </summary>
public interface IDroneController
{
    /// <summary>
    /// Given the current drone state, return 4 normalized thrust
    /// values in [-1, 1]. The DroneBody maps these to actual
    /// Newton forces via: force = thrust * maxThrustPerMotor.
    /// </summary>
    float[] ComputeMotorThrusts(DroneState state);

    /// <summary>
    /// Called once when the controller is first attached.
    /// Use for PID integral reset, PPO model loading, etc.
    /// </summary>
    void Initialize(DroneConfig config);

    /// <summary>
    /// Called when the episode resets (training or evaluation).
    /// PID resets integral terms; PPO resets hidden state.
    /// </summary>
    void OnEpisodeReset();
}

/// <summary>
/// Shared drone configuration. Both controllers see the same
/// physical parameters so neither has an information advantage.
/// </summary>
[System.Serializable]
public struct DroneConfig
{
    /// Mass in kg
    public float mass;

    /// Max thrust per motor in Newtons
    public float maxThrustPerMotor;

    /// Motor positions relative to center of mass (local space)
    /// Index: 0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight
    public Vector3[] motorPositions;

    /// Arm length in meters (distance from CoM to motor)
    public float armLength;

    /// Physics timestep (should match Unity's fixedDeltaTime)
    public float dt;
}
