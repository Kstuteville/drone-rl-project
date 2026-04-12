#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Duplicates root objects whose name starts with "Arena1" until there are 32 arenas,
/// laid out on a grid in XZ. Run once from the menu with DroneRL_Stage1 (or similar) open.
/// </summary>
public static class DuplicateDroneTrainingArenas
{
    const int TargetArenaCount = 32;
    const float SpacingX = 45f;
    const float SpacingZ = 45f;

    [MenuItem("Tools/Drone RL/Arrange Arenas To 32 (Grid)")]
    public static void DuplicateTo32()
    {
        var roots = Object.FindObjectsOfType<GameObject>();
        var arenas = System.Array.FindAll(roots, go =>
            go.transform.parent == null && go.name.StartsWith("Arena1"));

        if (arenas.Length == 0)
        {
            EditorUtility.DisplayDialog("Duplicate Arenas", "No root GameObject named Arena1* found.", "OK");
            return;
        }

        System.Array.Sort(arenas, (a, b) => string.CompareOrdinal(a.name, b.name));

        if (arenas.Length >= TargetArenaCount)
        {
            ArrangeGrid(arenas, TargetArenaCount);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[DuplicateDroneTrainingArenas] Arranged {TargetArenaCount} arenas on grid.");
            return;
        }

        var template = arenas[0];
        int toCreate = TargetArenaCount - arenas.Length;
        Undo.SetCurrentGroupName("Duplicate training arenas");
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < toCreate; i++)
        {
            var copy = Object.Instantiate(template);
            Undo.RegisterCreatedObjectUndo(copy, "Duplicate arena");
            copy.name = $"Arena1 (dup_{i})";
        }

        Undo.CollapseUndoOperations(undoGroup);

        roots = Object.FindObjectsOfType<GameObject>();
        arenas = System.Array.FindAll(roots, go =>
            go.transform.parent == null && go.name.StartsWith("Arena1"));
        System.Array.Sort(arenas, (a, b) => string.CompareOrdinal(a.name, b.name));

        ArrangeGrid(arenas, TargetArenaCount);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[DuplicateDroneTrainingArenas] Now have {arenas.Length} arenas (target {TargetArenaCount}).");
    }

    static void ArrangeGrid(GameObject[] arenas, int maxCount)
    {
        int n = Mathf.Min(maxCount, arenas.Length);
        int cols = 8;
        for (int i = 0; i < n; i++)
        {
            int row = i / cols;
            int col = i % cols;
            var t = arenas[i].transform;
            Undo.RecordObject(t, "Grid arrange arena");
            t.position = new Vector3(col * SpacingX, t.position.y, row * SpacingZ);
        }
    }
}
#endif
