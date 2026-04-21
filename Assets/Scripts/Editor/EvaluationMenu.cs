#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor entry points for ControllerEvaluator. Run Evaluation only works in Play Mode
/// because instantiating prefabs + calling Agent.EndEpisode() requires the runtime loop.
/// </summary>
public static class EvaluationMenu
{
    [MenuItem("Tools/Drone RL/Evaluation/Run Controller Evaluation")]
    public static void RunEvaluation()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Run Controller Evaluation",
                "Enter Play Mode first — evaluation needs the physics/runtime loop to drive episodes.",
                "OK");
            return;
        }

        var evaluator = Object.FindObjectOfType<ControllerEvaluator>();
        if (evaluator == null)
        {
            EditorUtility.DisplayDialog("Run Controller Evaluation",
                "No ControllerEvaluator in the active scene. Add a GameObject with the ControllerEvaluator component and configure its 'controllers' list.",
                "OK");
            return;
        }
        evaluator.StartRun();
    }

    [MenuItem("Tools/Drone RL/Evaluation/Summarize Last Run")]
    public static void Summarize()
    {
        string dir = GetConfiguredOutputDirectory();
        EvaluationSummary.SummarizeLastRun(dir);
    }

    [MenuItem("Tools/Drone RL/Evaluation/Open Logs Folder")]
    public static void OpenLogsFolder()
    {
        string dir = GetConfiguredOutputDirectory();
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    static string GetConfiguredOutputDirectory()
    {
        var evaluator = Object.FindObjectOfType<ControllerEvaluator>();
        if (evaluator != null && !string.IsNullOrEmpty(evaluator.outputDirectory))
            return evaluator.outputDirectory;
        return "Assets/EvaluationLogs";
    }
}
#endif
