using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class NetworkInteractiveObject : NetworkBehaviour, IInteractable
{
    [SerializeField] private string defaultPrompt = "[E] Взаємодіяти";

    // Синхронізована змінна: читають усі, змінювати дозволено лише серверу
    public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private MeshRenderer meshRenderer;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        // Підписуємося на оновлення стану для обох гравців
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
    }

    private void UpdateVisual(bool state)
    {
        if (meshRenderer != null)
        {
            // Червоний = вимкнено/закрито, Зелений = увімкнено/відкрито
            meshRenderer.material.color = state ? Color.green : Color.red;
        }
    }

    // Текст для підказки на екрані
    public string GetPromptText()
    {
        return IsActive.Value ? "[E] Вимкнути" : "[E] Увімкнути";
    }

    // Гравець тисне [E] на своєму комп'ютері
    public void Interact()
    {
        ToggleStateServerRpc();
    }

    // Запит до сервера змінити мережеву змінну
    [ServerRpc(RequireOwnership = false)]
    private void ToggleStateServerRpc()
    {
        IsActive.Value = !IsActive.Value;
    }
}