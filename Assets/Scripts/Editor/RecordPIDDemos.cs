#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;

/// <summary>
/// One-click setup for recording PIDHover demos (Heuristic + stable spawn tilt), and restore for PPO training.
/// </summary>
public static class RecordPIDDemos
{
    const string DemoAssetPath = "Assets/Demonstrations/PIDHover.demo";
    const string TrainingYamlRelativePath = "config/drone_training.yaml";
    const long MinDemoBytes = 1024;
    const float RecordingSpawnTilt = 1f;
    const float TrainingSpawnTilt = 10f;

    [MenuItem("Tools/Drone RL/Record PID Demos")]
    public static void PrepareRecording()
    {
        LogDemoFileStatus("Before recording");

        var behaviors = UnityEngine.Object.FindObjectsByType<BehaviorParameters>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (behaviors.Length == 0)
        {
            Debug.LogWarning("[RecordPIDDemos] No BehaviorParameters in the active scene.");
            return;
        }

        foreach (var bp in behaviors)
        {
            Undo.RecordObject(bp, "Record PID Demos");
            bp.BehaviorType = BehaviorType.HeuristicOnly;
            EditorUtility.SetDirty(bp);

            var recorder = bp.GetComponent<DemonstrationRecorder>();
            if (recorder == null)
            {
                Debug.LogWarning($"[RecordPIDDemos] No DemonstrationRecorder on '{bp.name}'. Add one or recording will not run.");
                continue;
            }

            Undo.RecordObject(recorder, "Record PID Demos");
            recorder.Record = true;
            EditorUtility.SetDirty(recorder);

            var body = bp.GetComponent<DroneBody>() ?? bp.GetComponentInChildren<DroneBody>();
            if (body != null)
            {
                Undo.RecordObject(body, "Record PID Demos");
                body.maxSpawnTilt = RecordingSpawnTilt;
                EditorUtility.SetDirty(body);
            }
            else
            {
                Debug.LogWarning($"[RecordPIDDemos] No DroneBody on '{bp.name}'.");
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        CommentBehavioralCloningInTrainingYaml();
        Debug.Log("[RecordPIDDemos] Ready to record. Press Play now. Stop after ~60 seconds (or your target step count), then run Tools > Drone RL > Stop Recording & Restore Training.");
    }

    [MenuItem("Tools/Drone RL/Stop Recording & Restore Training")]
    public static void StopRecordingAndRestore()
    {
        var behaviors = UnityEngine.Object.FindObjectsByType<BehaviorParameters>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var bp in behaviors)
        {
            Undo.RecordObject(bp, "Stop PID recording");
            bp.BehaviorType = BehaviorType.Default;
            EditorUtility.SetDirty(bp);

            var recorder = bp.GetComponent<DemonstrationRecorder>();
            if (recorder != null)
            {
                Undo.RecordObject(recorder, "Stop PID recording");
                recorder.Record = false;
                EditorUtility.SetDirty(recorder);
            }

            var body = bp.GetComponent<DroneBody>() ?? bp.GetComponentInChildren<DroneBody>();
            if (body != null)
            {
                Undo.RecordObject(body, "Stop PID recording");
                body.maxSpawnTilt = TrainingSpawnTilt;
                EditorUtility.SetDirty(body);
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        UncommentBehavioralCloningInTrainingYaml();
        Debug.Log("[RecordPIDDemos] Recording stopped. Agents restored to training mode (Default, Record off, maxSpawnTilt=10).");
        LogDemoFileStatus("After recording");
    }

    [MenuItem("Tools/Drone RL/Check PIDHover.demo Size")]
    public static void CheckDemoMenu()
    {
        LogDemoFileStatus("Manual check");
    }

    static void LogDemoFileStatus(string context)
    {
        string abs = Path.Combine(Application.dataPath, "Demonstrations", "PIDHover.demo");
        if (!File.Exists(abs))
        {
            Debug.LogWarning($"[RecordPIDDemos] ({context}) Missing {DemoAssetPath} — record demos first (Heuristic + Play).");
            return;
        }

        long len = new FileInfo(abs).Length;
        Debug.Log($"[RecordPIDDemos] ({context}) {DemoAssetPath} size = {len} bytes ({len / 1024f:F1} KB).");
        if (len < MinDemoBytes)
            Debug.LogWarning($"[RecordPIDDemos] ({context}) Demo is under {MinDemoBytes} bytes — likely empty or corrupt; re-record.");
    }

    static string GetDroneTrainingYamlPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
        return Path.Combine(projectRoot, TrainingYamlRelativePath);
    }

    /// <summary>Spaces after leading #, or spaces from line start when line does not start with #.</summary>
    static int YamlIndentDepth(string line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;
        if (line[0] == '#')
        {
            int i = 1;
            while (i < line.Length && line[i] == ' ')
                i++;
            return i - 1;
        }

        int j = 0;
        while (j < line.Length && line[j] == ' ')
            j++;
        return j;
    }

    static string LogicalLineAfterHash(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        if (i < line.Length && line[i] == '#')
        {
            i++;
            while (i < line.Length && line[i] == ' ')
                i++;
        }

        return i <= line.Length ? line.Substring(i) : "";
    }

    static bool IsBehavioralCloningHeaderLine(string line)
    {
        string logical = LogicalLineAfterHash(line);
        return logical.TrimStart().StartsWith("behavioral_cloning:", StringComparison.Ordinal);
    }

    /// <summary>True if this non-empty line is a YAML key at the same indent as a sibling (not a prose # comment).</summary>
    static bool IsSiblingYamlKeyLine(string line, int baseDepth)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        if (YamlIndentDepth(line) != baseDepth)
            return false;
        string logical = LogicalLineAfterHash(line).TrimStart();
        if (logical.Length == 0 || logical[0] == '#')
            return false;
        int colon = logical.IndexOf(':');
        if (colon <= 0)
            return false;
        for (int z = 0; z < colon; z++)
        {
            char ch = logical[z];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }

    static bool TryFindBehavioralCloningBlock(List<string> lines, out int start, out int endExclusive)
    {
        start = 0;
        endExclusive = 0;
        for (int idx = 0; idx < lines.Count; idx++)
        {
            if (!IsBehavioralCloningHeaderLine(lines[idx]))
                continue;
            start = idx;
            int baseDepth = YamlIndentDepth(lines[idx]);
            endExclusive = idx + 1;
            while (endExclusive < lines.Count)
            {
                string ln = lines[endExclusive];
                if (string.IsNullOrWhiteSpace(ln))
                    break;

                if (IsSiblingYamlKeyLine(ln, baseDepth))
                    break;
                int d = YamlIndentDepth(ln);
                if (d <= baseDepth)
                    break;
                endExclusive++;
            }

            return true;
        }

        return false;
    }

    static string CommentOneLineIfNeeded(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line;
        int i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        if (i < line.Length && line[i] == '#')
            return line;
        return "#" + line;
    }

    static string UncommentOneLineIfNeeded(string line)
    {
        if (string.IsNullOrEmpty(line))
            return line;
        int i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        if (i >= line.Length || line[i] != '#')
            return line;
        return line.Substring(0, i) + line.Substring(i + 1);
    }

    static void CommentBehavioralCloningInTrainingYaml()
    {
        string yamlPath = GetDroneTrainingYamlPath();
        if (!File.Exists(yamlPath))
        {
            Debug.LogWarning($"[RecordPIDDemos] Training YAML not found (skip BC comment): {yamlPath}");
            return;
        }

        string raw = File.ReadAllText(yamlPath);
        bool useCrlf = raw.Contains("\r\n");
        string newline = useCrlf ? "\r\n" : "\n";
        var lines = new List<string>(raw.Replace("\r\n", "\n").Split('\n'));
        if (!TryFindBehavioralCloningBlock(lines, out int start, out int endExclusive))
        {
            Debug.LogWarning($"[RecordPIDDemos] behavioral_cloning block not found in {yamlPath} — YAML unchanged.");
            return;
        }

        for (int k = start; k < endExclusive; k++)
            lines[k] = CommentOneLineIfNeeded(lines[k]);

        File.WriteAllText(yamlPath, string.Join(newline, lines), new UTF8Encoding(false));
        Debug.Log($"[RecordPIDDemos] Commented behavioral_cloning in {yamlPath} (safe while re-recording demos).");
    }

    static void UncommentBehavioralCloningInTrainingYaml()
    {
        string yamlPath = GetDroneTrainingYamlPath();
        if (!File.Exists(yamlPath))
        {
            Debug.LogWarning($"[RecordPIDDemos] Training YAML not found (skip BC uncomment): {yamlPath}");
            return;
        }

        string raw = File.ReadAllText(yamlPath);
        bool useCrlf = raw.Contains("\r\n");
        string newline = useCrlf ? "\r\n" : "\n";
        var lines = new List<string>(raw.Replace("\r\n", "\n").Split('\n'));
        if (!TryFindBehavioralCloningBlock(lines, out int start, out int endExclusive))
        {
            Debug.LogWarning($"[RecordPIDDemos] behavioral_cloning block not found in {yamlPath} — YAML unchanged.");
            return;
        }

        for (int k = start; k < endExclusive; k++)
            lines[k] = UncommentOneLineIfNeeded(lines[k]);

        File.WriteAllText(yamlPath, string.Join(newline, lines), new UTF8Encoding(false));
        Debug.Log($"[RecordPIDDemos] Uncommented behavioral_cloning in {yamlPath} (ready for BC training).");
    }
}
#endif
