using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement & Stealth Settings")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private Transform cameraHolder;

    [Header("Footstep Noise for NPC")]
    [Tooltip("Гучність тупоту при бігу на [Left Shift] (ходьба і присідання — безшумні)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float sprintFootstepLoudness = 0.65f;
    [SerializeField] private float footstepInterval = 0.38f;

    [Header("Interaction Settings")]
    [SerializeField] private float interactDistance = 3f;

    [Header("Flashlight")]
    [SerializeField] private Light flashlightLight;

    public NetworkVariable<bool> IsFlashlightOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Публічні властивості для відображення на ретро-візорі аудитора (RetroAuditorHud)
    public bool IsCrouching { get; private set; }
    public bool IsSprinting { get; private set; }
    public float CurrentMovementNoise { get; private set; }

    private float verticalRotation = 0f;
    private IInteractable currentInteractable;

    private bool isSeated = false;
    private ChessSeat currentSeat = null;

    private float _defaultCamLocalY = 0.6f;
    private float _nextFootstepTime;

    private void Awake()
    {
        if (cameraHolder != null)
        {
            _defaultCamLocalY = cameraHolder.localPosition.y;
        }
    }

    public override void OnNetworkSpawn()
    {
        IsFlashlightOn.OnValueChanged += OnFlashlightChanged;
        if (flashlightLight != null)
        {
            flashlightLight.enabled = IsFlashlightOn.Value;
        }

        if (IsOwner)
        {
            string targetSpawnName = IsServer ? "SpawnPoint_1" : "SpawnPoint_2";
            GameObject spawnPoint = GameObject.Find(targetSpawnName);

            if (spawnPoint != null)
            {
                transform.position = spawnPoint.transform.position;
                transform.rotation = spawnPoint.transform.rotation;
            }

            NetworkBootstrapUI.SetPauseMenu(false);
        }
        else
        {
            if (cameraHolder != null)
            {
                Camera cam = cameraHolder.GetComponentInChildren<Camera>();
                if (cam != null) cam.enabled = false;

                AudioListener listener = cameraHolder.GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        IsFlashlightOn.OnValueChanged -= OnFlashlightChanged;
    }

    private void OnFlashlightChanged(bool previousValue, bool newValue)
    {
        if (flashlightLight != null)
        {
            flashlightLight.enabled = newValue;
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // 0. Обробка клавіші [Escape] (спочатку закриває записку, термінал або стіл, інакше відкриває паузу)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (NetworkReadableNote.IsAnyNoteOpen)
            {
                NetworkReadableNote.ActiveLocalNote.CloseNoteUi();
                return;
            }

            if (NetworkKeypadTerminal.IsAnyTerminalOpen)
            {
                NetworkKeypadTerminal.ActiveLocalTerminal.CloseTerminalUi();
                return;
            }

            if (isSeated)
            {
                if (currentSeat != null)
                {
                    currentSeat.StandUp();
                }
                return;
            }

            NetworkBootstrapUI.TogglePauseMenu();
            return;
        }

        // Якщо відкрита пауза, термінал, записка або гравець сидить за столом — рух заблоковано
        if (NetworkBootstrapUI.IsPauseMenuOpen ||
            NetworkKeypadTerminal.IsAnyTerminalOpen ||
            NetworkReadableNote.IsAnyNoteOpen ||
            isSeated)
        {
            IsSprinting = false;
            CurrentMovementNoise = Mathf.MoveTowards(CurrentMovementNoise, 0f, 3f * Time.deltaTime);
            return;
        }

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

        // 2. Рух WASD + Біг [Left Shift] + Тихе присідання [Left Ctrl]
        IsCrouching = Input.GetKey(KeyCode.LeftControl);
        IsSprinting = !IsCrouching && Input.GetKey(KeyCode.LeftShift);

        float currentSpeed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);

        // Плавне присідання камери
        if (cameraHolder != null)
        {
            float targetCamY = IsCrouching ? (_defaultCamLocalY - 0.45f) : _defaultCamLocalY;
            Vector3 camPos = cameraHolder.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, targetCamY, 12f * Time.deltaTime);
            cameraHolder.localPosition = camPos;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.right * horizontal + vertical * transform.forward).normalized;
        if (moveDirection.magnitude >= 0.1f)
        {
            transform.position += moveDirection * currentSpeed * Time.deltaTime;

            // Якщо гравець БІЖИТЬ — генеруємо шум кроків для NPC
            if (IsSprinting && Time.time >= _nextFootstepTime)
            {
                _nextFootstepTime = Time.time + footstepInterval;
                CurrentMovementNoise = sprintFootstepLoudness;
                EmitFootstepNoiseServerRpc(sprintFootstepLoudness);
            }
        }

        // Плавне згасання індикатора шуму кроків на HUD
        CurrentMovementNoise = Mathf.MoveTowards(CurrentMovementNoise, 0f, 1.8f * Time.deltaTime);

        // 3. Взаємодія (Raycast)
        CheckForInteractable();

        if (Input.GetKeyDown(KeyCode.E) && currentInteractable != null)
        {
            currentInteractable.Interact();
        }

        // 4. Ліхтарик на [F]
        if (Input.GetKeyDown(KeyCode.F))
        {
            ToggleFlashlightServerRpc();
        }
    }

    [ServerRpc]
    private void EmitFootstepNoiseServerRpc(float loudness)
    {
        NoiseDispatcher.EmitOnServer(new NoiseEvent
        {
            Position = transform.position,
            Loudness = Mathf.Clamp01(loudness),
            Source = NoiseSource.PlayerFootstep
        });
    }

    public void EnterSeat(ChessSeat seat, Transform seatViewPoint)
    {
        isSeated = true;
        currentSeat = seat;

        transform.position = seatViewPoint.position;
        transform.rotation = seatViewPoint.rotation;

        if (cameraHolder != null)
        {
            verticalRotation = 25f;
            cameraHolder.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ExitSeat()
    {
        isSeated = false;
        currentSeat = null;

        if (cameraHolder != null)
        {
            verticalRotation = 0f;
            cameraHolder.localRotation = Quaternion.identity;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    [ServerRpc]
    private void ToggleFlashlightServerRpc()
    {
        IsFlashlightOn.Value = !IsFlashlightOn.Value;
    }

    private void CheckForInteractable()
    {
        if (cameraHolder == null) return;

        Ray ray = new Ray(cameraHolder.position, cameraHolder.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            currentInteractable = hit.collider.GetComponent<IInteractable>();
        }
        else
        {
            currentInteractable = null;
        }
    }

    private void OnGUI()
    {
        if (!IsOwner ||
            NetworkBootstrapUI.IsPauseMenuOpen ||
            NetworkKeypadTerminal.IsAnyTerminalOpen ||
            NetworkReadableNote.IsAnyNoteOpen) return;

        GUIStyle style = new GUIStyle
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.white;

        float screenX = Screen.width / 2f;
        float screenY = Screen.height / 2f + 40f;

        if (isSeated)
        {
            GUI.Label(new Rect(screenX - 150, screenY, 300, 30), "[Escape] Встати з-за столу", style);
            return;
        }

        if (currentInteractable != null)
        {
            string prompt = currentInteractable.GetPromptText();
            if (!string.IsNullOrEmpty(prompt))
            {
                GUI.Label(new Rect(screenX - 150, screenY, 300, 30), prompt, style);
            }
        }
    }
}