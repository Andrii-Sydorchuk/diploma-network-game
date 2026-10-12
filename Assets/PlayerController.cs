using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private Transform cameraHolder;

    private float verticalRotation = 0f;

    public override void OnNetworkSpawn()
    {
        // Якщо це чужий гравець — вимикаємо його камеру та мікрофон слухача
        if (!IsOwner)
        {
            if (cameraHolder != null)
            {
                Camera cam = cameraHolder.GetComponentInChildren<Camera>();
                if (cam != null) cam.enabled = false;

                AudioListener listener = cameraHolder.GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
        else
        {
            // Ховаємо курсор миші для FPS-керування
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // 1. Поворот мишкою
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Поворот тіла вліво/вправо
        transform.Rotate(Vector3.up * mouseX);

        // Поворот голови вгору/вниз
        if (cameraHolder != null)
        {
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -80f, 80f);
            cameraHolder.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        // 2. Пересування від першої особи (відносно погляду)
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.right * horizontal + transform.forward * vertical).normalized;

        if (moveDirection.magnitude >= 0.1f)
        {
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
        }

        // Клавіша Escape для повернення курсора миші
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}