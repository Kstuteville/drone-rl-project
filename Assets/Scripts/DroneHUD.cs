using UnityEngine;

public class DroneHUD : MonoBehaviour
{
    public Transform drone;
    public Rigidbody droneRb;

    private float pitch, roll, altitude, speed;

    void Update()
    {
        if (drone == null) return;

        pitch = drone.eulerAngles.x;
        roll = drone.eulerAngles.z;
        if (pitch > 180f) pitch -= 360f;
        if (roll > 180f) roll -= 360f;

        speed = droneRb.linearVelocity.magnitude;

        if (Physics.Raycast(drone.position, Vector3.down, out RaycastHit hit, 1000f))
            altitude = hit.distance;
    }

    void OnGUI()
    {
        float panelX = 20;
        float panelY = 20;
        float panelW = 200;
        float panelH = 320;

        // Panel background
        GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, panelH), Texture2D.whiteTexture);

        // Panel border
        GUI.color = new Color(0f, 0.8f, 1f, 0.4f);
        DrawBorder(new Rect(panelX, panelY, panelW, panelH), 1);

        GUI.color = Color.white;

        // Title
        GUIStyle title = new GUIStyle(GUI.skin.label);
        title.fontSize = 11;
        title.normal.textColor = new Color(0f, 0.8f, 1f, 1f);
        title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(panelX, panelY + 8, panelW, 20), "◈  FLIGHT DATA  ◈", title);

        // Divider
        GUI.color = new Color(0f, 0.8f, 1f, 0.3f);
        GUI.DrawTexture(new Rect(panelX + 10, panelY + 30, panelW - 20, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = panelY + 40;
        float innerX = panelX + 12;
        float innerW = panelW - 24;

        // Stats
        DrawStat(innerX, y,       innerW, "ALT",   $"{altitude:F1} m",  new Color(0.2f, 1f, 0.4f));
        DrawStat(innerX, y + 28,  innerW, "SPD",   $"{speed:F1} m/s",   new Color(1f, 0.8f, 0.1f));
        DrawStat(innerX, y + 56,  innerW, "PITCH", $"{pitch:F1}°",      new Color(1f, 0.4f, 0.4f));
        DrawStat(innerX, y + 84,  innerW, "ROLL",  $"{roll:F1}°",       new Color(0.4f, 0.6f, 1f));

        // Divider
        GUI.color = new Color(0f, 0.8f, 1f, 0.3f);
        GUI.DrawTexture(new Rect(panelX + 10, y + 118, panelW - 20, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float barY = y + 128;

        // Bars — all clamped to innerW
        DrawBar(innerX, barY,       innerW, "ROLL",  roll / 90f,              new Color(0.4f, 0.6f, 1f),  true);
        DrawBar(innerX, barY + 48,  innerW, "PITCH", pitch / 90f,             new Color(1f, 0.4f, 0.4f),  true);
        DrawBar(innerX, barY + 96,  innerW, "ALT",   altitude / 50f,          new Color(0.2f, 1f, 0.4f),  false);
        DrawBar(innerX, barY + 144, innerW, "SPD",   speed / 20f,             new Color(1f, 0.8f, 0.1f),  false);
    }

    void DrawStat(float x, float y, float w, string label, string value, Color color)
    {
        GUIStyle lbl = new GUIStyle(GUI.skin.label);
        lbl.fontSize = 11;
        lbl.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        GUIStyle val = new GUIStyle(GUI.skin.label);
        val.fontSize = 13;
        val.fontStyle = FontStyle.Bold;
        val.normal.textColor = color;
        val.alignment = TextAnchor.MiddleRight;

        GUI.Label(new Rect(x, y, w, 22), label, lbl);
        GUI.Label(new Rect(x, y, w, 22), value, val);
    }

    void DrawBar(float x, float y, float w, string label, float value, Color color, bool centered)
    {
        GUIStyle lbl = new GUIStyle(GUI.skin.label);
        lbl.fontSize = 10;
        lbl.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        GUI.Label(new Rect(x, y, w, 16), label, lbl);

        float barY = y + 16;
        float barH = 10;

        // Bar background
        GUI.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(x, barY, w, barH), Texture2D.whiteTexture);

        if (centered)
        {
            float center = x + w / 2f;
            float clamped = Mathf.Clamp(value, -1f, 1f);
            float barW = Mathf.Abs(clamped) * (w / 2f);
            barW = Mathf.Min(barW, w / 2f); // hard clamp so it never overflows

            GUI.color = color;
            if (clamped >= 0)
                GUI.DrawTexture(new Rect(center, barY, barW, barH), Texture2D.whiteTexture);
            else
                GUI.DrawTexture(new Rect(center - barW, barY, barW, barH), Texture2D.whiteTexture);

            // Center tick
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(center - 1, barY, 2, barH), Texture2D.whiteTexture);
        }
        else
        {
            float clamped = Mathf.Clamp01(value);
            float barW = Mathf.Min(clamped * w, w); // hard clamp
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, barY, barW, barH), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
    }

    void DrawBorder(Rect r, float thickness)
    {
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
    }
}