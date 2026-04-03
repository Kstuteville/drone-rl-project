using UnityEngine;
using UnityEngine.InputSystem;

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
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.tKey.isPressed && kb.spaceKey.wasPressedThisFrame ||
            kb.spaceKey.isPressed && kb.tKey.wasPressedThisFrame)
        {
            Reset();
        }
    }

    void Reset()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = new Vector3(transform.position.x, startPosition.y, transform.position.z);
        transform.rotation = Quaternion.identity;
    }
}