using UnityEngine;

public class TurretControllerVaried : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 30f;

    [Header("Firing")]
    public GameObject bulletPrefab;
    public Transform muzzlePoint;
    public float fireRate = 1.5f;
    public float bulletSpeed = 40f;

    [Header("Aiming")]
    public bool leadTarget = true;
    [Tooltip("Random spread radius in meters — 0 = perfect aim, 0.5 = realistic inaccuracy")]
    public float aimSpread = 0.4f;

    private float _fireCooldown;
    private DroneBody _lastTarget;

    void Update()
    {
        DroneBody target = FindNearestDroneExcluding(_lastTarget);
        if (target == null)
            target = FindNearestDroneExcluding(null);

        if (target == null)
            return;

        Vector3 cleanTarget = leadTarget ? PredictPosition(target) : target.transform.position;

        _fireCooldown -= Time.deltaTime;
        if (_fireCooldown <= 0f)
        {
            Vector3 aimPos = cleanTarget + UnityEngine.Random.insideUnitSphere * aimSpread;
            Fire(aimPos);
            _lastTarget = target;
            _fireCooldown = 1f / fireRate;
        }
    }

    DroneBody FindNearestDroneExcluding(DroneBody exclude)
    {
        DroneBody[] allDrones = FindObjectsOfType<DroneBody>();
        DroneBody nearest = null;
        float nearestDist = float.MaxValue;

        foreach (DroneBody drone in allDrones)
        {
            if (drone == exclude)
                continue;

            float dist = Vector3.Distance(drone.transform.position, transform.position);
            if (dist > detectionRange)
                continue;

            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = drone;
            }
        }

        return nearest;
    }

    Vector3 PredictPosition(DroneBody drone)
    {
        Rigidbody droneRb = drone.GetComponent<Rigidbody>();
        if (droneRb == null)
            return drone.transform.position;

        Vector3 origin = muzzlePoint != null ? muzzlePoint.position : transform.position;
        float dist = Vector3.Distance(origin, drone.transform.position);
        float timeToImpact = dist / Mathf.Max(bulletSpeed, 1f);

        return drone.transform.position + droneRb.velocity * timeToImpact;
    }

    void Fire(Vector3 aimPos)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[TurretController] No bullet prefab assigned!");
            return;
        }

        Vector3 spawnPos = muzzlePoint != null ? muzzlePoint.position : transform.position;
        Vector3 dir = (aimPos - spawnPos).normalized;
        Debug.DrawLine(spawnPos, aimPos, Color.red, 0.5f);

        GameObject b = Instantiate(bulletPrefab, spawnPos, Quaternion.LookRotation(dir));
        Rigidbody rb = b.GetComponent<Rigidbody>();
        if (rb != null)
            rb.velocity = dir * bulletSpeed;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
