#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SceneSetup
{
    [MenuItem("Tools/Setup Drone Test Scene")]
    public static void SetupDroneTestScene()
    {
        // Create a fresh scene
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // ─────────────────────────────────────────────
        // Ground Plane
        // ─────────────────────────────────────────────
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        // ─────────────────────────────────────────────
        // Arena Walls (4 thin cubes, 20m wide box, 10m tall)
        // ─────────────────────────────────────────────
        float arenaHalf = 10f;  // half-width = 10m → 20m total
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

        // ─────────────────────────────────────────────
        // Drone
        // ─────────────────────────────────────────────
        GameObject drone = new GameObject("Drone");
        drone.transform.position = new Vector3(0f, 5f, 0f);

        // Rigidbody
        Rigidbody rb = drone.AddComponent<Rigidbody>();
        rb.mass = 1.5f;
        rb.drag = 0f;
        rb.angularDrag = 0f;
        rb.useGravity = true;

        // Controller components
        drone.AddComponent<DroneBody>();
        drone.AddComponent<PIDDroneController>();

        TestPlanner planner = drone.AddComponent<TestPlanner>();
        planner.drone = drone.GetComponent<DroneBody>();
        planner.mode = TestPlanner.TestMode.Hover;

        DroneReset droneReset = drone.AddComponent<DroneReset>();
        droneReset.droneBody = drone.GetComponent<DroneBody>();

        // Placeholder body (small cube)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(drone.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);

        // Rotor positions and spin directions matching DroneBody defaults
        // FL=0 CCW(1), FR=1 CW(-1), RL=2 CW(-1), RR=3 CCW(1)
        Vector3[] rotorPositions = new Vector3[]
        {
            new Vector3(-0.88f, 0.60f,  0.88f),  // 0: FL
            new Vector3( 0.88f, 0.60f,  0.88f),  // 1: FR
            new Vector3(-0.88f, 0.60f, -0.88f),  // 2: RL
            new Vector3( 0.88f, 0.60f, -0.88f),  // 3: RR
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

            // Remove collider on rotors so they don't interfere with physics
            Object.DestroyImmediate(rotor.GetComponent<Collider>());

            RotorSpin spin = rotor.AddComponent<RotorSpin>();
            spin.rotorIndex = i;
            spin.spinDirection = spinDirs[i];
        }

        // ─────────────────────────────────────────────
        // Camera
        // ─────────────────────────────────────────────
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            mainCam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
        }

        DroneCamera droneCam = mainCam.gameObject.AddComponent<DroneCamera>();
        droneCam.target = drone.transform;

        // ─────────────────────────────────────────────
        // HUD
        // ─────────────────────────────────────────────
        GameObject hudObj = new GameObject("HUD");
        DroneHUD hud = hudObj.AddComponent<DroneHUD>();
        hud.drone = drone.transform;
        hud.droneRb = rb;

        // ─────────────────────────────────────────────
        // Directional Light (DefaultGameObjects includes one, but just in case)
        // ─────────────────────────────────────────────
        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject lightObj = new GameObject("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // ─────────────────────────────────────────────
        // Select drone and frame in scene view
        // ─────────────────────────────────────────────
        Selection.activeGameObject = drone;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        // Mark scene dirty so user is prompted to save
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[SceneSetup] Drone test scene created. Hit Play to test PID hover.");
    }

    private static void CreateWall(string name, Vector3 position, Vector3 size)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = size;
    }
}
#endif
