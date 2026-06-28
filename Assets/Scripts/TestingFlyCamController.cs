using UnityEngine;

public class TestingFlyCamController : MonoBehaviour
{
    [Header("Movement Metrics")]
    public float movementSpeed = 5.0f;
    public float lookSensitivity = 2.5f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Start()
    {
        // Vector check: Start position near back wall looking into the room space
        transform.position = new Vector3(0f, 1.6f, -4.5f);
        Vector3 currentRotation = transform.localRotation.eulerAngles;
        rotationX = currentRotation.y;
        rotationY = currentRotation.x;
    }

    void Update()
    {
        // 🕹️ KEYBOARD NAVIGATION: Move around inside your 1:1 chamber
        float moveForwardBackward = Input.GetAxis("Vertical") * movementSpeed * Time.deltaTime; // W / S Keys
        float moveLeftRight = Input.GetAxis("Horizontal") * movementSpeed * Time.deltaTime;  // A / D Keys
        
        transform.Translate(Vector3.forward * moveForwardBackward);
        transform.Translate(Vector3.right * moveLeftRight);

        // Vertical Elevation adjustments using E and Q key commands
        if (Input.GetKey(KeyCode.E)) transform.Translate(Vector3.up * movementSpeed * Time.deltaTime);
        if (Input.GetKey(KeyCode.Q)) transform.Translate(Vector3.down * movementSpeed * Time.deltaTime);

        // 🖱️ MOUSE CAMERA ORIENTATION LOOK: Hold Right-Click to pan your vision coordinates
        if (Input.GetMouseButton(1))
        {
            rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
            rotationY -= Input.GetAxis("Mouse Y") * lookSensitivity;
            rotationY = Mathf.Clamp(rotationY, -85f, 85f); // Lock target limits to prevent camera flips

            transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0f);
        }
    }
}

