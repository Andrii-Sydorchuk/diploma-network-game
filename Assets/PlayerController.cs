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
        // Налаштування точок спавну для власника персонажа
        if (IsOwner)
        {
            // Якщо ми Сервер/Хост — шукаємо точку 1, якщо Клієнт — точку 2
            string targetSpawnName = IsServer ? "SpawnPoint_1" : "SpawnPoint_2";
            GameObject spawnPoint = GameObject.Find(targetSpawnName);

            if (spawnPoint != null)
            {
                transform.position = spawnPoint.transform.position;
                transform.rotation = spawnPoint.transform.rotation;
            }

            // Ховаємо курсор миші для гри від першої особи
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // Якщо це персонаж іншого гравця — вимикаємо його камеру та мікрофон
            if (cameraHolder != null)
            {
                Camera cam = cameraHolder.GetComponentInChildren<Camera>();
                if (cam != null) cam.enabled = false;

                AudioListener listener = cameraHolder.GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // 1. Огляд мишкою
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        if (cameraHolder != null)
        {
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -80f, 80f);
            cameraHolder.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        // 2. Рух WASD
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.right * horizontal + transform.forward * vertical).normalized;

        if (moveDirection.magnitude >= 0.1f)
        {
            transform.position += moveDirection * moveSpeed * Time.deltaTime;
        }

        // Звільнення мишки по Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}