using UnityEngine;

/// <summary>
/// Physical bullet projectile. Requires a Rigidbody — mass set here drives the impulse
/// the drone receives on impact. Auto-destroys after lifetime or on collision.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Tooltip("Bullet mass in kg. Higher = more force transferred to the drone on impact.")]
    public float mass = 0.1f;

    [Tooltip("Seconds before auto-destroying if no collision")]
    public float lifetime = 5f;

    void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetupTracer();

        Destroy(gameObject, lifetime);
    }

    void SetupTracer()
    {
        // Use existing TrailRenderer if already on the prefab, otherwise add one
        TrailRenderer trail = gameObject.GetComponent<TrailRenderer>();
        if (trail == null)
            trail = gameObject.AddComponent<TrailRenderer>();

        trail.time = 0.15f;
        trail.startWidth = 0.4f;
        trail.endWidth = 0f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            trail.material = new Material(shader);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(1f, 0f, 0f), 0f),
                new GradientColorKey(new Color(0.6f, 0f, 0f), 1f),
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        trail.colorGradient = gradient;
    }

    void OnCollisionEnter(Collision collision)
{
    Rigidbody hitRb = collision.rigidbody;
    if (hitRb != null)
    {
        // Register hit on drone (handles motor disable logic on hit 3 and 4)
        DroneBody drone = collision.gameObject.GetComponent<DroneBody>();
        if (drone != null)
            drone.RegisterHit();

        // Full impulse force unchanged
        Vector3 contactPoint = collision.contacts[0].point;
        Vector3 impactDir = GetComponent<Rigidbody>().velocity.normalized;
        float impactForce = GetComponent<Rigidbody>().mass * GetComponent<Rigidbody>().velocity.magnitude;
        hitRb.AddForceAtPosition(impactDir * impactForce, contactPoint, ForceMode.Impulse);
        Debug.Log($"[Bullet] Hit: {collision.gameObject.name} at {contactPoint}");
    }
    Destroy(gameObject);
}
    }
