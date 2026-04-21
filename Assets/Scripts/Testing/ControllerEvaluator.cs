using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Unity.Barracuda;
using Unity.MLAgents;
using Unity.MLAgents.Policies;

/// <summary>
/// Runs a set of controllers (PID or PPO) through matched spawn sequences and writes
/// per-episode metrics to CSV. Same SpawnVariationConfig is applied to every controller
/// with useFixedSeed=true so episode sequences are identical across runs — this is the
/// fairness backbone for PPO-vs-PID playtest comparison.
/// </summary>
public class ControllerEvaluator : MonoBehaviour
{
    [Serializable]
    public class ControllerSetup
    {
        [Tooltip("Short name used in CSV filename and summary table.")]
        public string name = "controller";

        [Tooltip("Prefab containing DroneBody + a controller component (PIDDroneController or PPODroneController/Agent).")]
        public GameObject dronePrefab;

        [Tooltip("Optional ONNX model to load onto BehaviorParameters at runtime. Leave null for scripted/PID controllers.")]
        public NNModel onnxModel;

        [Tooltip("BehaviorType to force on BehaviorParameters. Default InferenceOnly (for ONNX). Use HeuristicOnly for PID-only prefabs that still carry a BehaviorParameters component.")]
        public BehaviorType behaviorType = BehaviorType.InferenceOnly;

        [Tooltip("Optional spawn override for drone. Leave empty to reuse evaluator-level spawnConfig.")]
        public Vector3 spawnAnchor = Vector3.zero;
    }

    [Header("Controllers to compare")]
    public List<ControllerSetup> controllers = new List<ControllerSetup>();

    [Header("Spawn config (will be cloned; useFixedSeed forced true for matched sequences)")]
    public SpawnVariationConfig spawnConfig = SpawnVariationConfig.Playtest();

    [Header("Episode budget")]
    public int episodesPerController = 20;
    public float maxEpisodeSeconds = 30f;

    [Header("Output")]
    [Tooltip("Relative to project root (Unity writes here; for a build use Application.persistentDataPath).")]
    public string outputDirectory = "Assets/EvaluationLogs";

    [Header("Run control")]
    [Tooltip("If true, auto-runs RunEvaluation on Start. Normally driven by Tools/Drone RL/Evaluation menu.")]
    public bool runOnStart = false;

    string _runTimestamp;
    bool _running;

    void Start()
    {
        if (runOnStart) StartRun();
    }

    /// <summary>Entry point invoked by the editor menu.</summary>
    public void StartRun()
    {
        if (_running)
        {
            Debug.LogWarning("[ControllerEvaluator] Already running; ignoring request.");
            return;
        }
        StartCoroutine(RunEvaluation());
    }

    IEnumerator RunEvaluation()
    {
        _running = true;
        _runTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(outputDirectory);

        Debug.Log($"[ControllerEvaluator] Run {_runTimestamp} starting: {controllers.Count} controller(s), {episodesPerController} episode(s) each.");

        foreach (var setup in controllers)
        {
            if (setup == null || setup.dronePrefab == null)
            {
                Debug.LogWarning($"[ControllerEvaluator] Skipping null setup entry.");
                continue;
            }
            yield return EvaluateController(setup);
        }

        Debug.Log($"[ControllerEvaluator] Run {_runTimestamp} complete. Summarize via Tools/Drone RL/Evaluation/Summarize Last Run.");
        _running = false;
    }

    IEnumerator EvaluateController(ControllerSetup setup)
    {
        Debug.Log($"[ControllerEvaluator] -> {setup.name}");

        var drone = Instantiate(setup.dronePrefab, setup.spawnAnchor, Quaternion.identity);
        drone.name = $"EvalDrone_{setup.name}";

        var body = drone.GetComponent<DroneBody>();
        if (body == null)
        {
            Debug.LogError($"[ControllerEvaluator] '{setup.name}' prefab is missing DroneBody. Skipping.");
            Destroy(drone);
            yield break;
        }

        var agent = drone.GetComponent<Agent>();
        var behaviorParams = drone.GetComponent<BehaviorParameters>();

        if (behaviorParams != null)
        {
            if (setup.onnxModel != null)
            {
                behaviorParams.BehaviorType = BehaviorType.InferenceOnly;
                if (agent != null)
                    agent.SetModel(behaviorParams.BehaviorName, setup.onnxModel);
                else
                    behaviorParams.Model = setup.onnxModel;
            }
            else
            {
                behaviorParams.BehaviorType = setup.behaviorType;
            }
        }

        var cfg = spawnConfig != null ? spawnConfig.Clone() : SpawnVariationConfig.Playtest();
        cfg.useFixedSeed = true;
        body.SetSpawnConfig(cfg, resetEpisodeIndex: true);
        body.randomizeSpawnOnEpisodeBegin = true;

        var rows = new List<string>();
        rows.Add("episodeIndex,controllerName,durationSec,meanAltitudeError,meanVelocityError,peakTiltDegrees,meanTiltDegrees,crashed,smoothnessRMS,meanPositionDriftXZ");

        var summary = new Summary();

        for (int ep = 0; ep < episodesPerController; ep++)
        {
            yield return RunEpisode(body, agent, setup.name, ep, rows, summary);
        }

        rows.Add(SummaryRow(setup.name, summary, episodesPerController));

        string path = Path.Combine(outputDirectory, $"{_runTimestamp}_{SanitizeFileName(setup.name)}.csv");
        File.WriteAllLines(path, rows);
        Debug.Log($"[ControllerEvaluator] Wrote {path} ({summary.completed} episodes, {summary.crashes} crashes).");

        Destroy(drone);
        yield return new WaitForFixedUpdate();
    }

    IEnumerator RunEpisode(DroneBody body, Agent agent, string name, int epIndex,
        List<string> rows, Summary summary)
    {
        if (agent != null)
        {
            agent.EndEpisode();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }
        else
        {
            body.ResetMotors();
            body.RandomizeSpawn();
            yield return new WaitForFixedUpdate();
        }

        Vector3 spawnPos = body.transform.position;
        float startTime = Time.time;

        float altErrorSum = 0f, velErrorSum = 0f, tiltSum = 0f;
        float peakTilt = 0f, driftSum = 0f, smoothnessSum = 0f;
        int samples = 0, smoothnessSamples = 0;
        bool crashed = false;
        float[] prevMotors = null;

        while (Time.time - startTime < maxEpisodeSeconds)
        {
            var state = body.GetStateSnapshot();
            float targetAlt = body.GetTargetPosition().y;
            float altErr = Mathf.Abs(state.altitude - targetAlt);
            float velErr = Vector3.Distance(state.velocity, state.targetVelocity);
            float tilt = Vector3.Angle(Vector3.up, state.orientation * Vector3.up);
            float driftXZ = new Vector2(state.position.x - spawnPos.x, state.position.z - spawnPos.z).magnitude;

            altErrorSum += altErr;
            velErrorSum += velErr;
            tiltSum += tilt;
            peakTilt = Mathf.Max(peakTilt, tilt);
            driftSum += driftXZ;
            samples++;

            if (prevMotors != null && state.currentMotorOutputs != null)
            {
                float d2 = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float d = state.currentMotorOutputs[i] - prevMotors[i];
                    d2 += d * d;
                }
                smoothnessSum += d2;
                smoothnessSamples++;
            }
            prevMotors = state.currentMotorOutputs;

            float worldY = body.transform.position.y;
            if (tilt > 80f || worldY < 0.5f || worldY > 20f)
            {
                crashed = true;
                break;
            }

            yield return new WaitForFixedUpdate();
        }

        float duration = Time.time - startTime;
        float meanAlt = samples > 0 ? altErrorSum / samples : 0f;
        float meanVel = samples > 0 ? velErrorSum / samples : 0f;
        float meanTilt = samples > 0 ? tiltSum / samples : 0f;
        float meanDrift = samples > 0 ? driftSum / samples : 0f;
        float smoothnessRMS = smoothnessSamples > 0 ? Mathf.Sqrt(smoothnessSum / smoothnessSamples) : 0f;

        rows.Add(string.Join(",", new string[]
        {
            epIndex.ToString(CultureInfo.InvariantCulture),
            name,
            duration.ToString("F3", CultureInfo.InvariantCulture),
            meanAlt.ToString("F4", CultureInfo.InvariantCulture),
            meanVel.ToString("F4", CultureInfo.InvariantCulture),
            peakTilt.ToString("F2", CultureInfo.InvariantCulture),
            meanTilt.ToString("F2", CultureInfo.InvariantCulture),
            crashed ? "true" : "false",
            smoothnessRMS.ToString("F5", CultureInfo.InvariantCulture),
            meanDrift.ToString("F3", CultureInfo.InvariantCulture),
        }));

        summary.Accumulate(duration, meanAlt, meanVel, peakTilt, meanTilt, crashed, smoothnessRMS, meanDrift);
    }

    static string SummaryRow(string name, Summary s, int totalEpisodes)
    {
        int completed = Mathf.Max(1, s.completed);
        float meanDur = s.durationSum / completed;
        float meanAlt = s.altErrorSum / completed;
        float meanVel = s.velErrorSum / completed;
        float peakTilt = s.peakTilt;
        float meanTilt = s.tiltSum / completed;
        float crashRate = totalEpisodes > 0 ? (float)s.crashes / totalEpisodes : 0f;
        float smoothness = s.smoothnessSum / completed;
        float meanDrift = s.driftSum / completed;

        return string.Join(",", new string[]
        {
            "SUMMARY",
            name,
            meanDur.ToString("F3", CultureInfo.InvariantCulture),
            meanAlt.ToString("F4", CultureInfo.InvariantCulture),
            meanVel.ToString("F4", CultureInfo.InvariantCulture),
            peakTilt.ToString("F2", CultureInfo.InvariantCulture),
            meanTilt.ToString("F2", CultureInfo.InvariantCulture),
            crashRate.ToString("F3", CultureInfo.InvariantCulture),
            smoothness.ToString("F5", CultureInfo.InvariantCulture),
            meanDrift.ToString("F3", CultureInfo.InvariantCulture),
        });
    }

    static string SanitizeFileName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unnamed";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    class Summary
    {
        public int completed;
        public int crashes;
        public float durationSum;
        public float altErrorSum;
        public float velErrorSum;
        public float tiltSum;
        public float peakTilt;
        public float smoothnessSum;
        public float driftSum;

        public void Accumulate(float dur, float meanAlt, float meanVel,
            float peakTiltEp, float meanTilt, bool crashed, float smoothness, float drift)
        {
            completed++;
            if (crashed) crashes++;
            durationSum += dur;
            altErrorSum += meanAlt;
            velErrorSum += meanVel;
            tiltSum += meanTilt;
            peakTilt = Mathf.Max(peakTilt, peakTiltEp);
            smoothnessSum += smoothness;
            driftSum += drift;
        }
    }
}
