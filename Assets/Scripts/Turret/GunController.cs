using UnityEngine;

public class GunController : MonoBehaviour
{
    [Header("Firing")]
    public GameObject bulletPrefab;
    public float bulletSpeed = 80f;
    public float fireRate = 3f;

    [Header("Barrel Offset")]
    public Vector3 barrelOffset = new Vector3(0.3f, -0.1f, 0.5f); // right, down, forward in camera space

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

        // Raycast from screen center to find the actual aim target
        Ray centerRay = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        
        // Find target point — either a hit surface or a far point
        Vector3 targetPoint;
        if (Physics.Raycast(centerRay, out RaycastHit hit, 500f))
            targetPoint = hit.point;
        else
            targetPoint = centerRay.origin + centerRay.direction * 500f;

        // Spawn at barrel offset position (in camera local space)
        Vector3 spawnPos = _cam.transform.TransformPoint(barrelOffset);

        // Aim from barrel toward the target point
        Vector3 fireDir = (targetPoint - spawnPos).normalized;

        GameObject b = Instantiate(bulletPrefab, spawnPos, Quaternion.LookRotation(fireDir));
        Rigidbody rb = b.GetComponent<Rigidbody>();
        if (rb != null)
            rb.velocity = fireDir * bulletSpeed;
    }

    void OnGUI()
    {
        Rect pr = _cam.pixelRect;
        float cx = pr.x + pr.width * 0.5f;
        float cy = Screen.height - (pr.y + pr.height * 0.5f);
        float size = 12f;
        float thickness = 2f;

        GUI.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        GUI.DrawTexture(new Rect(cx - thickness / 2f, cy - size, thickness, size * 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - size, cy - thickness / 2f, size * 2, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
    }
}