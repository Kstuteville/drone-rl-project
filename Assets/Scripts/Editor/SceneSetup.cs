#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SceneSetup
{
    // ─────────────────────────────────────────────────────
    // PRIMARY: Use Jessenth's Drone_final prefab + environment
    // ─────────────────────────────────────────────────────
    [MenuItem("Tools/Setup Drone Test Scene (Jessenth Prefab)")]
    public static void SetupWithJessePrefab()
    {
        // Load Jessenth's prefab
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Migrate/Prefabs/Drone_final.prefab");

        if (prefab == null)
        {
            Debug.LogError("[SceneSetup] Drone_final.prefab not found at Assets/Migrate/Prefabs/. " +
                "Falling back to placeholder scene.");
            SetupPlaceholderScene();
            return;
        }

        // Open Jessenth's SampleScene (has buildings and environment)
        string scenePath = "Assets/Scenes/SampleScene.unity";
        if (System.IO.File.Exists(scenePath))
        {
            EditorSceneManager.OpenScene(scenePath);
        }
        else
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            AddGroundAndWalls();
        }

        // Find existing drone in scene and remove it (Jess's scene might have one)
        var existingDrones = Object.FindObjectsOfType<Transform>();
        foreach (var t in existingDrones)
        {
            if (t.name == "Drone_final" || t.name == "Drone")
                Object.DestroyImmediate(t.gameObject);
        }

        // Instantiate the prefab
        GameObject drone = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        drone.transform.position = new Vector3(0f, 5f, 0f);
        drone.transform.rotation = Quaternion.identity;

        // Unpack prefab so we can modify components freely
        PrefabUtility.UnpackPrefabInstance(drone, PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);

        // ─── Remove Jessenth's physics/controller scripts (broken refs) ───
        // These will show as "Missing Script" since we don't have his
        // DronePhysics.cs and DroneController.cs. Remove them all.
        RemoveMissingScripts(drone);

        // Also remove any surviving DronePhysics or DroneController if somehow present
        foreach (var comp in drone.GetComponents<Component>())
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName == "DronePhysics" || typeName == "DroneController" ||
                typeName == "PrintPositions")
            {
                Object.DestroyImmediate(comp);
            }
        }

        // ─── Configure Rigidbody ───
        Rigidbody rb = drone.GetComponent<Rigidbody>();
        if (rb == null) rb = drone.AddComponent<Rigidbody>();
        rb.mass = 1.5f;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // ─── Add Peter's architecture components ───
        if (drone.GetComponent<DroneBody>() == null)
            drone.AddComponent<DroneBody>();
        if (drone.GetComponent<PIDDroneController2>() == null)
            drone.AddComponent<PIDDroneController2>();

        DroneBody droneBody = drone.GetComponent<DroneBody>();

        TestPlanner planner = drone.GetComponent<TestPlanner>();
        if (planner == null) planner = drone.AddComponent<TestPlanner>();
        planner.drone = droneBody;
        planner.mode = TestPlanner.TestMode.Hover;

        DroneReset droneReset = drone.GetComponent<DroneReset>();
        if (droneReset == null) droneReset = drone.AddComponent<DroneReset>();
        droneReset.droneBody = droneBody;

        // ─── Fix RotorSpin on all Mount children ───
        // Jessenth's prefab hierarchy: Drone_final > Arm_XX > Base_XX > Mount > Rotor
        // The RotorSpin scripts will be "missing" because they referenced
        // Jess's DronePhysics. Remove them and add our version.
        FixRotorSpins(drone);

        // ─── Remove the Camera child from the prefab (we use our own) ───
        Transform camChild = drone.transform.Find("Camera");
        if (camChild != null)
            Object.DestroyImmediate(camChild.gameObject);

        // ─── Setup Camera ───
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            mainCam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }
        // Remove existing DroneCamera if re-running
        DroneCamera existingDroneCam = mainCam.GetComponent<DroneCamera>();
        if (existingDroneCam != null) Object.DestroyImmediate(existingDroneCam);

        DroneCamera droneCam = mainCam.gameObject.AddComponent<DroneCamera>();
        droneCam.target = drone.transform;

        // ─── Setup HUD ───
        GameObject hudObj = GameObject.Find("HUD");
        if (hudObj == null) hudObj = new GameObject("HUD");
        DroneHUD hud = hudObj.GetComponent<DroneHUD>();
        if (hud == null) hud = hudObj.AddComponent<DroneHUD>();
        hud.drone = drone.transform;
        hud.droneRb = rb;

        // ─── Directional Light ───
        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // ─── Select and frame ───
        Selection.activeGameObject = drone;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SceneSetup] Drone test scene created with Jessenth's prefab. " +
            "Components wired: DroneBody + PIDDroneController2 + TestPlanner + DroneReset + " +
            "RotorSpin (x4) + DroneCamera + DroneHUD. Hit Play to test PID hover.");
    }

    // ─────────────────────────────────────────────────────
    // FALLBACK: Placeholder drone (no prefab needed)
    // ─────────────────────────────────────────────────────
    [MenuItem("Tools/Setup Drone Test Scene (Placeholder)")]
    public static void SetupPlaceholderScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        AddGroundAndWalls();

        // Create drone from primitives
        GameObject drone = new GameObject("Drone");
        drone.transform.position = new Vector3(0f, 5f, 0f);

        Rigidbody rb = drone.AddComponent<Rigidbody>();
        rb.mass = 1.5f;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        rb.useGravity = true;

        drone.AddComponent<DroneBody>();
        drone.AddComponent<PIDDroneController2>();

        TestPlanner planner = drone.AddComponent<TestPlanner>();
        planner.drone = drone.GetComponent<DroneBody>();
        planner.mode = TestPlanner.TestMode.Hover;

        DroneReset droneReset = drone.AddComponent<DroneReset>();
        droneReset.droneBody = drone.GetComponent<DroneBody>();

        // Placeholder body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(drone.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);

        // Placeholder rotors
        Vector3[] rotorPositions = new Vector3[]
        {
            new Vector3(-0.88f, 0.60f,  0.88f),
            new Vector3( 0.88f, 0.60f,  0.88f),
            new Vector3(-0.88f, 0.60f, -0.88f),
            new Vector3( 0.88f, 0.60f, -0.88f),
        };
        float[] spinDirs = new float[] { 1f, -1f, -1f, 1f };
        string[] rotorNames = new string[] { "Rotor_FL", "Rotor_FR", "Rotor_RL", "Rotor_RR" };

        for (int i = 0; i < 4; i++)
        {
            GameObject rotor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rotor.name = rotorNames[i];
            rotor.transform.SetParent(drone.transform);
            rotor.transform.localPosition = rotorPositions[i];
            rotor.transform.localScale = Vector3.one * 0.1f;
            Object.DestroyImmediate(rotor.GetComponent<Collider>());

            RotorSpin spin = rotor.AddComponent<RotorSpin>();
            spin.rotorIndex = i;
            spin.spinDirection = spinDirs[i];
        }

        // Camera
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            DroneCamera droneCam = mainCam.gameObject.AddComponent<DroneCamera>();
            droneCam.target = drone.transform;
        }

        // HUD
        GameObject hudObj = new GameObject("HUD");
        DroneHUD hud = hudObj.AddComponent<DroneHUD>();
        hud.drone = drone.transform;
        hud.droneRb = rb;

        Selection.activeGameObject = drone;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[SceneSetup] Placeholder drone test scene created. Hit Play to test PID hover.");
    }

    // ─────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────

    private static void AddGroundAndWalls()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        float arenaHalf = 10f;
        float wallHeight = 10f;
        float wallThickness = 0.2f;

        CreateWall("Wall_North", new Vector3(0f, wallHeight / 2f, arenaHalf),
            new Vector3(arenaHalf * 2f, wallHeight, wallThickness));
        CreateWall("Wall_South", new Vector3(0f, wallHeight / 2f, -arenaHalf),
            new Vector3(arenaHalf * 2f, wallHeight, wallThickness));
        CreateWall("Wall_East", new Vector3(arenaHalf, wallHeight / 2f, 0f),
            new Vector3(wallThickness, wallHeight, arenaHalf * 2f));
        CreateWall("Wall_West", new Vector3(-arenaHalf, wallHeight / 2f, 0f),
            new Vector3(wallThickness, wallHeight, arenaHalf * 2f));
    }

    private static void CreateWall(string name, Vector3 position, Vector3 size)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = size;
    }

    private static void RemoveMissingScripts(GameObject go)
    {
        // Remove all missing (null) MonoBehaviour components
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        // Recurse into children
        foreach (Transform child in go.transform)
        {
            RemoveMissingScripts(child.gameObject);
        }
    }

    private static void FixRotorSpins(GameObject drone)
    {
        // Jessenth's prefab hierarchy:
        //   Drone_final
        //     Arm_RR > Base_RR > Mount > Rotor2
        //     Arm_RL > Base_RL > Mount > Rotor2
        //     Arm_FR > Base_FR > Mount > Rotor2
        //     Arm_FL > Base_FL (or similar) > Mount > Rotor1/Rotor2
        //
        // RotorSpin mapping: FL=0(CCW), FR=1(CW), RL=2(CW), RR=3(CCW)

        string[] armNames = { "Arm_FL", "Arm_FR", "Arm_RL", "Arm_RR" };
        int[] rotorIndices = { 0, 1, 2, 3 };
        float[] spinDirs = { 1f, -1f, -1f, 1f };  // FL=CCW, FR=CW, RL=CW, RR=CCW

        for (int i = 0; i < armNames.Length; i++)
        {
            Transform arm = FindChildRecursive(drone.transform, armNames[i]);
            if (arm == null)
            {
                Debug.LogWarning($"[SceneSetup] Could not find {armNames[i]} in prefab");
                continue;
            }

            // Find any Mount or rotor-like child recursively
            Transform mount = FindChildRecursive(arm, "Mount");
            Transform rotorTarget = mount != null ? mount : arm;

            // Find the actual rotor mesh (Rotor1 or Rotor2)
            Transform rotorMesh = FindChildRecursive(rotorTarget, "Rotor");
            if (rotorMesh == null)
                rotorMesh = FindChildRecursive(rotorTarget, "Rotor1");
            if (rotorMesh == null)
                rotorMesh = FindChildRecursive(rotorTarget, "Rotor2");

            // Add RotorSpin to the rotor mesh, or to the mount if no rotor mesh
            GameObject spinTarget = rotorMesh != null ? rotorMesh.gameObject : rotorTarget.gameObject;

            // Remove any existing (possibly broken) RotorSpin
            RotorSpin existingSpin = spinTarget.GetComponent<RotorSpin>();
            if (existingSpin != null) Object.DestroyImmediate(existingSpin);

            // Remove missing scripts from this object too
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(spinTarget);

            RotorSpin spin = spinTarget.AddComponent<RotorSpin>();
            spin.rotorIndex = rotorIndices[i];
            spin.spinDirection = spinDirs[i];

            Debug.Log($"[SceneSetup] RotorSpin added to {spinTarget.name} " +
                $"(index={rotorIndices[i]}, dir={spinDirs[i]})");
        }
    }

    private static Transform FindChildRecursive(Transform parent, string namePrefix)
    {
        foreach (Transform child in parent)
        {
            if (child.name.StartsWith(namePrefix))
                return child;

            Transform found = FindChildRecursive(child, namePrefix);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
