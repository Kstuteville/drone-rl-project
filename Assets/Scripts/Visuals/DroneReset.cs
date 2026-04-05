// DroneReset.cs — Reset drone position/rotation on keypress
// Original by Jessenth, adapted: uses classic Input (no InputSystem dependency)
// and calls DroneBody.ResetMotors() for proper episode reset.

using UnityEngine;

public class DroneReset : MonoBehaviour
{
    [Tooltip("Reference to DroneBody for full motor reset")]
    public DroneBody droneBody;

    private Vector3 startPosition;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;

        if (droneBody == null)
            droneBody = GetComponent<DroneBody>();
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.T) && Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKey(KeyCode.Space) && Input.GetKeyDown(KeyCode.T))
        {
            ResetDrone();
        }
    }

    public void ResetDrone()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = startPosition; // fixed — resets ALL axes
        transform.rotation = Quaternion.identity;
        droneBody?.ResetMotors();
    }

    
}