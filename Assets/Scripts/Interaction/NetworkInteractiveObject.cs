using System;
using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class NetworkInteractiveObject : NetworkBehaviour, IInteractable
{
    [Header("Налаштування перемикача / рубильника")]
    [SerializeField] private string objectName = "Рубильник живлення";
    [SerializeField] private float cooldownSeconds = 0.5f;
    [SerializeField] private float maxServerInteractDistance = 4.5f;

    [Tooltip("Гучність клацання рубильника для NPC (0 = безшумно)")]
    [Range(0f, 1f)]
    [SerializeField] private float switchNoiseLoudness = 0.4f;

    // Синхронізована змінна: читають усі, змінювати дозволено лише серверу
    public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Подія для пов'язаних механізмів (гермодверей, ламп, терміналів)
    public event Action<bool> OnPowerStateChanged;

    private MeshRenderer _meshRenderer;
    private float _lastToggleTime;

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        IsActive.OnValueChanged += OnStateChanged;
        UpdateVisual(IsActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsActive.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(bool previousValue, bool newValue)
    {
        UpdateVisual(newValue);
        OnPowerStateChanged?.Invoke(newValue);
    }

    private void UpdateVisual(bool state)
    {
        if (_meshRenderer != null)
        {
            _meshRenderer.material.color = state ? Color.green : Color.red;
        }
    }

    public string GetPromptText()
    {
        return IsActive.Value
            ? $"[E] Вимкнути ({objectName})"
            : $"[E] Увімкнути ({objectName})";
    }

    public void Interact()
    {
        ToggleStateServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleStateServerRpc(ServerRpcParams rpcParams = default)
    {
        // 1. Захист від спаму (мeрежевий кулдаун за ТЗ)
        if (Time.time - _lastToggleTime < cooldownSeconds) return;

        // 2. Серверна перевірка дистанції до гравця (захист від Race Conditions за ТЗ)
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
            if (dist > maxServerInteractDistance) return;
        }

        _lastToggleTime = Time.time;
        IsActive.Value = !IsActive.Value;

        // 3. Генеруємо звук клацання реле для ШІ (NoiseSource.Environmental)
        if (switchNoiseLoudness > 0.01f)
        {
            NoiseDispatcher.EmitOnServer(new NoiseEvent
            {
                Position = transform.position,
                Loudness = switchNoiseLoudness,
                Source = NoiseSource.Environmental
            });
        }
    }
}