using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float sprintSpeed = 20f;

    [Header("Look")]
    public float mouseSensitivity = 2f;
    public Camera playerCamera;

    private CharacterController _cc;
    private float _pitch = 0f;
    private float _yaw = 0f;

    void Start()
    {
        _cc = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = !Cursor.visible;
        }

        Look();
        Move();
    }

    void Look()
    {
        _yaw   += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        _pitch -= Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        _pitch  = Mathf.Clamp(_pitch, -89f, 89f);

        transform.localEulerAngles = new Vector3(0f, _yaw, 0f);
        playerCamera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
    }

    void Move()
    {
        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : moveSpeed;

        Vector3 move = transform.right * Input.GetAxisRaw("Horizontal")
                     + transform.forward * Input.GetAxisRaw("Vertical");

        move = move.normalized * speed;
        move.y = -9.81f;

        _cc.Move(move * Time.deltaTime);
    }
}
