using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkCarryableBattery : NetworkBehaviour, IInteractable
{
    [Header("Налаштування перенесення")]
    [SerializeField] private string batteryName = "Енергоблок А-80";
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, -0.1f, 1.35f);
    [SerializeField] private float socketSnapRadius = 2.6f;

    [Header("Звук падіння для NPC")]
    [Range(0f, 1f)]
    [SerializeField] private float dropNoiseLoudness = 0.75f;

    [Header("Кольори індикації")]
    [SerializeField] private Color idleColor = new Color(1f, 0.75f, 0.1f);
    [SerializeField] private Color carriedColor = new Color(1f, 0.9f, 0.4f);
    [SerializeField] private Color pluggedColor = new Color(0.1f, 0.9f, 1f);

    public NetworkVariable<ulong> CarrierClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsPluggedIntoSocket = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Collider _collider;
    private MeshRenderer _renderer;
    private NetworkInteractiveObject _connectedSocket;
    private float _pickupTime;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _renderer = GetComponent<MeshRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CarrierClientId.OnValueChanged += OnCarrierChanged;
        IsPluggedIntoSocket.OnValueChanged += OnPluggedChanged;

        UpdateVisualAndCollider();
    }

    public override void OnNetworkDespawn()
    {
        CarrierClientId.OnValueChanged -= OnCarrierChanged;
        IsPluggedIntoSocket.OnValueChanged -= OnPluggedChanged;

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        ulong carrierId = CarrierClientId.Value;

        if (carrierId != ulong.MaxValue)
        {
            Transform carrierTransform = GetPlayerTransform(carrierId);
            if (carrierTransform != null)
            {
                Vector3 targetPos = carrierTransform.position
                                    + carrierTransform.forward * holdOffset.z
                                    + Vector3.up * holdOffset.y;

                transform.position = Vector3.Lerp(transform.position, targetPos, 25f * Time.deltaTime);
                transform.rotation = Quaternion.Lerp(transform.rotation, carrierTransform.rotation, 20f * Time.deltaTime);
            }

            // Дозволяємо покласти/вставити батарею на [G] або [E], навіть не цілячись у неї камерою
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.LocalClientId == carrierId &&
                Time.time - _pickupTime > 0.25f &&
                IsDropOrPlacePressed())
            {
                RequestInteractServerRpc();
            }
        }
    }

    public string GetPromptText()
    {
        if (CarrierClientId.Value == ulong.MaxValue)
        {
            return IsPluggedIntoSocket.Value
                ? $"[E] Вийняти {batteryName} (Знеструмити вузол)"
                : $"[E] Підняти {batteryName}";
        }

        return "";
    }

    public void Interact()
    {
        if (CarrierClientId.Value == ulong.MaxValue)
        {
            _pickupTime = Time.time;
            RequestInteractServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // 1. Піднімаємо батарею (і знеструмлюємо щиток, якщо вона була в ньому)
        if (CarrierClientId.Value == ulong.MaxValue)
        {
            if (_connectedSocket != null)
            {
                _connectedSocket.IsActive.Value = false;
                _connectedSocket = null;
            }

            IsPluggedIntoSocket.Value = false;
            CarrierClientId.Value = senderId;
            return;
        }

        // 2. Кладемо батарею або підключаємо до щитка
        if (CarrierClientId.Value == senderId)
        {
            CarrierClientId.Value = ulong.MaxValue;

            NetworkInteractiveObject nearSocket = FindNearestSocket();
            if (nearSocket != null)
            {
                _connectedSocket = nearSocket;
                _connectedSocket.IsActive.Value = true;
                IsPluggedIntoSocket.Value = true;

                // Ставимо рівно на підлогу перед щитком, щоб не залазила всередину куба!
                Vector3 snapPos = nearSocket.transform.position - nearSocket.transform.forward * 0.85f;
                snapPos.y = 0.35f;
                transform.position = snapPos;
                transform.rotation = nearSocket.transform.rotation;
            }
            else
            {
                IsPluggedIntoSocket.Value = false;
                Vector3 pos = transform.position;
                pos.y = 0.35f;
                transform.position = pos;

                NoiseDispatcher.EmitOnServer(new NoiseEvent
                {
                    Position = transform.position,
                    Loudness = dropNoiseLoudness,
                    Source = NoiseSource.PhysicalImpact
                });
            }
        }
    }

    private NetworkInteractiveObject FindNearestSocket()
    {
        NetworkInteractiveObject[] sockets = FindObjectsByType<NetworkInteractiveObject>(FindObjectsSortMode.None);
        NetworkInteractiveObject closest = null;
        float minDist = socketSnapRadius;

        foreach (var s in sockets)
        {
            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d < minDist)
            {
                minDist = d;
                closest = s;
            }
        }

        return closest;
    }

    private Transform GetPlayerTransform(ulong clientId)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            return client.PlayerObject.transform;
        }
        return null;
    }

    private void OnCarrierChanged(ulong previousValue, ulong newValue)
    {
        if (NetworkManager.Singleton != null && newValue == NetworkManager.Singleton.LocalClientId)
        {
            _pickupTime = Time.time;
        }
        UpdateVisualAndCollider();
    }

    private void OnPluggedChanged(bool previousValue, bool newValue)
    {
        UpdateVisualAndCollider();
    }

    private void UpdateVisualAndCollider()
    {
        bool isCarried = CarrierClientId.Value != ulong.MaxValue;

        if (_collider != null)
        {
            _collider.isTrigger = isCarried;
            // Коли батарея в руках — ставимо Ignore Raycast (2), коли на землі — Default (0)
            gameObject.layer = isCarried ? 2 : 0;
        }

        if (_renderer != null)
        {
            if (isCarried)
                _renderer.material.color = carriedColor;
            else if (IsPluggedIntoSocket.Value)
                _renderer.material.color = pluggedColor;
            else
                _renderer.material.color = idleColor;
        }
    }

    private void OnGUI()
    {
        if (!IsSpawned || NetworkManager.Singleton == null) return;
        if (CarrierClientId.Value != NetworkManager.Singleton.LocalClientId) return;

        GUIStyle style = new GUIStyle
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.yellow;

        NetworkInteractiveObject nearSocket = FindNearestSocket();
        string hint = nearSocket != null
            ? "[E] або [G] Підключити енергоблок до вузла"
            : "[E] або [G] Покласти енергоблок на підлогу";

        GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height / 2f + 70f, 400f, 30f), hint, style);
    }

    private static bool IsDropOrPlacePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            return Keyboard.current.gKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame;
        }
#endif
        return Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.E);
    }
}