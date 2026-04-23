using UnityEngine;

public class SplitScreenManager : MonoBehaviour
{
    [Header("Left Side (PID)")]
    public FPSController leftFPS;
    public GunController leftGun;
    public Camera leftCamera;

    [Header("Right Side (PPO)")]
    public FPSController rightFPS;
    public GunController rightGun;
    public Camera rightCamera;

    [Header("Settings")]
    public KeyCode switchKey = KeyCode.Y;

    private bool _leftIsActive = true;

    void Awake()
    {
        if (!leftFPS)  Debug.LogError("[SplitScreen] leftFPS is not assigned!");
        if (!rightFPS) Debug.LogError("[SplitScreen] rightFPS is not assigned!");
        if (!leftGun)  Debug.LogError("[SplitScreen] leftGun is not assigned!");
        if (!rightGun) Debug.LogError("[SplitScreen] rightGun is not assigned!");

        Apply();
    }

    void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            _leftIsActive = !_leftIsActive;
            Apply();
            Debug.Log($"[SplitScreen] Switched — active side: {(_leftIsActive ? "LEFT (PID)" : "RIGHT (PPO)")}");
        }
    }

    void Apply()
    {
        if (leftFPS)  leftFPS.enabled  = _leftIsActive;
        if (leftGun)  leftGun.enabled  = _leftIsActive;
        if (rightFPS) rightFPS.enabled = !_leftIsActive;
        if (rightGun) rightGun.enabled = !_leftIsActive;
    }

    void OnGUI()
    {
        float w = Screen.width * 0.5f;
        float h = Screen.height;
        float borderThickness = 4f;

        if (_leftIsActive)
        {
            DrawBorder(new Rect(0, 0, w, h), new Color(0.2f, 1f, 0.2f, 0.8f), borderThickness);
            DrawOverlay(new Rect(w, 0, w, h), new Color(0, 0, 0, 0.35f));
        }
        else
        {
            DrawBorder(new Rect(w, 0, w, h), new Color(0.2f, 1f, 0.2f, 0.8f), borderThickness);
            DrawOverlay(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.35f));
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperCenter
        };

        style.normal.textColor = _leftIsActive ? Color.green : new Color(1, 1, 1, 0.4f);
        GUI.Label(new Rect(0, 8, w, 30), "PID Controller", style);

        style.normal.textColor = !_leftIsActive ? Color.green : new Color(1, 1, 1, 0.4f);
        GUI.Label(new Rect(w, 8, w, 30), "PPO Controller", style);

        GUIStyle hint = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.LowerCenter,
            normal = { textColor = new Color(1, 1, 1, 0.6f) }
        };
        GUI.Label(new Rect(0, h - 28, Screen.width, 24), $"[{switchKey}] Switch Side", hint);
    }

    static void DrawBorder(Rect r, Color c, float t)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static void DrawOverlay(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
