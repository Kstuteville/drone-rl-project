// =============================================================
// TestPlanner.cs — Simple planner for testing PID in isolation
// Drop into: Assets/Scripts/Planner/
//
// Sends the drone a sequence of target velocities so you can
// verify PID tracking, stability, and motor failure response
// before the real arena/shooter exist.
//
// Attach to the same GameObject as DroneBody, or any object
// in the scene (just drag DroneBody into the Inspector slot).
// =============================================================

using UnityEngine;

public class TestPlanner : MonoBehaviour
{
    [Header("Drone Reference")]
    public DroneBody drone;

    [Header("Test Pattern")]
    public TestMode mode = TestMode.Hover;

    public enum TestMode
    {
        Hover,          // Hold position at spawn altitude
        Square,         // Fly a square pattern
        RandomDodge,    // Random velocity changes (simulates evasion)
        MotorFailure,   // Hover, then kill a motor after delay
    }

    [Header("Square Pattern")]
    public float squareSpeed = 3f;
    public float legDuration = 3f;

    [Header("Random Dodge")]
    public float dodgeSpeed = 4f;
    public float dodgeInterval = 1.5f;

    [Header("Motor Failure Test")]
    public int motorToKill = 0;
    public float failureDelay = 5f;

    // Runtime
    private float _timer;
    private int _squareLeg;
    private Vector3 _currentTarget;
    private bool _motorKilled;

    void Start()
    {
        if (drone == null)
            drone = GetComponent<DroneBody>();
    }

    /// <summary>Called from PPODroneController each ML-Agents episode so motor-failure can re-trigger.</summary>
    public void EpisodeReset()
    {
        _timer = 0f;
        _motorKilled = false;
        motorToKill = Random.Range(0, 4);
    }

    void Update()
    {
        if (drone == null) return;
        _timer += Time.deltaTime;

        TestMode effective = mode;
        if (DroneBody.ReadCurriculumStage() >= 4)
            effective = TestMode.MotorFailure;

        switch (effective)
        {
            case TestMode.Hover:
                _currentTarget = Vector3.zero;
                break;

            case TestMode.Square:
                UpdateSquare();
                break;

            case TestMode.RandomDodge:
                UpdateRandomDodge();
                break;

            case TestMode.MotorFailure:
                _currentTarget = Vector3.zero; // hover
                if (!_motorKilled && _timer >= failureDelay)
                {
                    drone.DisableMotor(motorToKill);
                    _motorKilled = true;
                    Debug.Log($"[TestPlanner] Killed motor {motorToKill} at t={_timer:F1}s");
                }
                break;
        }

        drone.SetTargetVelocity(_currentTarget);
    }

    private void UpdateSquare()
    {
        float legTime = _timer % (legDuration * 4f);
        int leg = Mathf.FloorToInt(legTime / legDuration);

        _currentTarget = leg switch
        {
            0 => Vector3.forward * squareSpeed,
            1 => Vector3.right * squareSpeed,
            2 => Vector3.back * squareSpeed,
            3 => Vector3.left * squareSpeed,
            _ => Vector3.zero,
        };
    }

    private float _nextDodgeTime;

    private void UpdateRandomDodge()
    {
        if (_timer >= _nextDodgeTime)
        {
            _currentTarget = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-0.3f, 0.3f),
                Random.Range(-1f, 1f)
            ).normalized * dodgeSpeed;

            _nextDodgeTime = _timer + dodgeInterval;
        }
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 25),
            $"Mode: {mode} | Target: {_currentTarget:F2}");
        GUI.Label(new Rect(10, 35, 400, 25),
            $"Drone vel: {drone?.targetVelocity:F2}");
    }
}
