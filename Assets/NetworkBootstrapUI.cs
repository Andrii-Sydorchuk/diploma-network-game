using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class NetworkBootstrapUI : MonoBehaviour
{
    private string joinCodeInput = "";
    private string statusMessage = "Очікування дій...";
    private string createdJoinCode = "";

    private async void Start()
    {
        // Ініціалізація хмарних сервісів Unity та гостьова авторизація
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            statusMessage = "Підключено до мережі Unity";
        }
        catch (Exception ex)
        {
            statusMessage = "Помилка ініціалізації: " + ex.Message;
        }
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        GUILayout.BeginArea(new Rect(20, 20, 320, 260), GUI.skin.box);

        GUILayout.Label("<b>МЕРЕЖЕВЕ ЛОБІ (RELAY)</b>");
        GUILayout.Label("Статус: " + statusMessage);
        GUILayout.Space(10);

        // Якщо гра ще не запущена як Host або Client
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Створити кімнату (Гравець 1)", GUILayout.Height(35)))
            {
                CreateRelayLobby();
            }

            GUILayout.Space(15);
            GUILayout.Label("Код підключення:");
            joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Height(25));

            if (GUILayout.Button("Приєднатися за кодом (Гравець 2)", GUILayout.Height(35)))
            {
                JoinRelayLobby(joinCodeInput);
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(createdJoinCode))
            {
                GUILayout.Label($"<b>ТВІЙ КОД КІМНАТИ:</b>\n<size=22><color=yellow>{createdJoinCode}</color></size>");
            }
            else
            {
                GUILayout.Label("<color=green>Ви успішно підключені до сесії!</color>");
            }
        }

        GUILayout.EndArea();
    }

    private async void CreateRelayLobby()
    {
        statusMessage = "Створення виділеного сервера...";
        try
        {
            // Максимум 1 додатковий підключений гравець (разом 2 гравці)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            createdJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
            statusMessage = "Кімнату створено! Поділіться кодом";
        }
        catch (Exception ex)
        {
            statusMessage = "Помилка створення: " + ex.Message;
        }
    }

    private async void JoinRelayLobby(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            statusMessage = "Введіть валідний код кімнати!";
            return;
        }

        statusMessage = "Підключення до кімнати...";
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
            statusMessage = "Підключення успішне!";
        }
        catch (Exception ex)
        {
            statusMessage = "Не вдалося зайти: " + ex.Message;
        }
    }
}