using UnityEngine;

public class GunController : MonoBehaviour
{
    [Header("Firing")]
    public GameObject bulletPrefab;
    public float bulletSpeed = 80f;
    public float fireRate = 3f;

    private float _fireCooldown;
    private Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = Camera.main;
    }

    void Update()
    {
        _fireCooldown -= Time.deltaTime;

        if ((Input.GetButtonDown("Fire1") || Input.GetKeyDown(KeyCode.Space)) && _fireCooldown <= 0f)
        {
            Fire();
            _fireCooldown = 1f / fireRate;
        }
    }

    void Fire()
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[GunController] No bullet prefab assigned!");
            return;
        }

        Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 spawnPos = ray.origin + ray.direction * 0.5f;
        GameObject b = Instantiate(bulletPrefab, spawnPos, Quaternion.LookRotation(ray.direction));
        Rigidbody rb = b.GetComponent<Rigidbody>();
        if (rb != null)
            rb.velocity = ray.direction * bulletSpeed;
    }

    void OnGUI()
    {
        // Use camera's actual pixel rect so crosshair is correct in split screen
        Rect pr = _cam.pixelRect;
        float cx = pr.x + pr.width * 0.5f;
        // pixelRect Y is bottom-up; GUI Y is top-down
        float cy = Screen.height - (pr.y + pr.height * 0.5f);
        float size = 12f;
        float thickness = 2f;

        GUI.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - size, thickness, size * 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - size, cy - thickness / 2f, size * 2, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
    }
}
