using UnityEngine;

public class TurretController : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 30f;

    [Header("Firing")]
    public GameObject bulletPrefab;
    public Transform muzzlePoint;
    public float fireRate = 1.5f;
    public float bulletSpeed = 40f;

    private float _fireCooldown;

    void Update()
    {
        DroneBody target = FindNearestDrone();
        if (target == null)
            return;

        _fireCooldown -= Time.deltaTime;
        if (_fireCooldown <= 0f)
        {
            Fire(target.transform.position);
            _fireCooldown = 1f / fireRate;
        }
    }

    DroneBody FindNearestDrone()
    {
        DroneBody[] allDrones = FindObjectsOfType<DroneBody>();
        DroneBody nearest = null;
        float nearestDist = float.MaxValue;

        foreach (DroneBody drone in allDrones)
        {
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
