#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Applies a SpawnVariationConfig preset to every DroneBody in the active scene.
/// Marks the scene dirty so the change persists on save.
/// </summary>
public static class SpawnPresetMenu
{
    [MenuItem("Tools/Drone RL/Set Spawn Preset/Easy")]
    public static void SetEasy() => Apply(SpawnVariationConfig.Easy(), "Easy");

    [MenuItem("Tools/Drone RL/Set Spawn Preset/Mixed")]
    public static void SetMixed() => Apply(SpawnVariationConfig.Mixed(), "Mixed");

    [MenuItem("Tools/Drone RL/Set Spawn Preset/Stress")]
    public static void SetStress() => Apply(SpawnVariationConfig.Stress(), "Stress");

    [MenuItem("Tools/Drone RL/Set Spawn Preset/Playtest (seeded)")]
    public static void SetPlaytest() => Apply(SpawnVariationConfig.Playtest(), "Playtest");

    static void Apply(SpawnVariationConfig preset, string label)
    {
        var drones = Object.FindObjectsOfType<DroneBody>();
        if (drones == null || drones.Length == 0)
        {
            EditorUtility.DisplayDialog("Set Spawn Preset",
                "No DroneBody components found in the active scene.", "OK");
            return;
        }

        Undo.RecordObjects(drones, $"Set Spawn Preset: {label}");
        int updated = 0;
        foreach (var d in drones)
        {
            d.spawnConfig = preset.Clone();
            EditorUtility.SetDirty(d);
            updated++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[SpawnPresetMenu] Applied '{label}' preset to {updated} DroneBody component(s).");
    }
}
#endif
