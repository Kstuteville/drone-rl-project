using UnityEngine;

public class DroneReset : MonoBehaviour
{
    private Vector3 startPosition;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.T) && Input.GetKeyDown(KeyCode.Space) ||
            Input.GetKey(KeyCode.Space) && Input.GetKeyDown(KeyCode.T))
        {
            Reset();
        }
    }

    void Reset()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = new Vector3(transform.position.x, startPosition.y, transform.position.z);
        transform.rotation = Quaternion.identity;
    }
}