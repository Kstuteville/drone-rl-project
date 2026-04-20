using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Reads the most recent ControllerEvaluator run from disk (files sharing the
/// latest yyyyMMdd_HHmmss_ prefix) and prints a Markdown comparison table to
/// Debug.Log. Column choice mirrors the CSV SUMMARY rows written by the evaluator.
/// </summary>
public static class EvaluationSummary
{
    public static void SummarizeLastRun(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Debug.LogWarning($"[EvaluationSummary] Directory does not exist: {directory}");
            return;
        }

        var files = Directory.GetFiles(directory, "*.csv");
        if (files == null || files.Length == 0)
        {
            Debug.LogWarning($"[EvaluationSummary] No CSV files in {directory}");
            return;
        }

        string latestPrefix = FindLatestRunPrefix(files);
        if (string.IsNullOrEmpty(latestPrefix))
        {
            Debug.LogWarning("[EvaluationSummary] Could not infer latest run prefix from filenames.");
            return;
        }

        var rows = new List<string[]>();
        foreach (var path in files)
        {
            string fname = Path.GetFileName(path);
            if (!fname.StartsWith(latestPrefix)) continue;

            string[] lines = File.ReadAllLines(path);
            string summary = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("SUMMARY,")) { summary = line; break; }
            }
            if (summary == null) continue;

            string[] parts = summary.Split(',');
            if (parts.Length >= 10) rows.Add(parts);
        }

        if (rows.Count == 0)
        {
            Debug.LogWarning($"[EvaluationSummary] No SUMMARY rows found under prefix {latestPrefix}");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[EvaluationSummary] Run {latestPrefix} — {rows.Count} controller(s)");
        sb.AppendLine();
        sb.AppendLine("| controller | mean duration | mean alt err | mean vel err | peak tilt | mean tilt | crash rate | smoothness | mean drift |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            sb.AppendLine($"| {r[1]} | {r[2]} | {r[3]} | {r[4]} | {r[5]} | {r[6]} | {r[7]} | {r[8]} | {r[9]} |");
        }

        Debug.Log(sb.ToString());
    }

    static string FindLatestRunPrefix(string[] files)
    {
        string latest = null;
        foreach (var path in files)
        {
            string name = Path.GetFileName(path);
            int underscore = name.IndexOf('_');
            if (underscore < 0) continue;
            int second = name.IndexOf('_', underscore + 1);
            if (second < 0) continue;
            string prefix = name.Substring(0, second + 1);
            if (latest == null || string.Compare(prefix, latest) > 0)
                latest = prefix;
        }
        return latest;
    }
}
