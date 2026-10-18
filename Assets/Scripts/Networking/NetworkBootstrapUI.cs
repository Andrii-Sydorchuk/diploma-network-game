using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class NetworkBootstrapUI : MonoBehaviour
{
    public static bool IsPauseMenuOpen { get; private set; }

    private string _joinCodeInput = "";
    private string _statusMessage = "Ініціалізація сервісів...";
    private string _createdJoinCode = "";
    private bool _showSettingsInMainMenu = false;
    private float _masterVolume = 1.0f;
    private bool _codeCopiedFeedback = false;

    private async void Start()
    {
        IsPauseMenuOpen = false;
        _masterVolume = AudioListener.volume;

        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            _statusMessage = "Готово до підключення (Unity Relay)";
        }
        catch (Exception ex)
        {
            _statusMessage = "Помилка мережі: " + ex.Message;
        }
    }

    public static void TogglePauseMenu()
    {
        SetPauseMenu(!IsPauseMenuOpen);
    }

    public static void SetPauseMenu(bool open)
    {
        IsPauseMenuOpen = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        bool isInGame = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;

        if (!isInGame)
        {
            DrawMainMenu();
        }
        else
        {
            DrawInGameHud();

            if (IsPauseMenuOpen)
            {
                DrawPauseMenu();
            }
        }
    }

    private void DrawMainMenu()
    {
        // Гарантуємо вільний курсор у головному меню
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        float width = 380f;
        float height = 390f;
        float x = (Screen.width - width) / 2f;
        float y = (Screen.height - height) / 2f;

        GUI.Box(new Rect(x, y, width, height), "");

        GUILayout.BeginArea(new Rect(x + 25f, y + 20f, width - 50f, height - 40f));

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };

        GUILayout.Label("ПРОЄКТ «КОПІЯ» (THE COPY)", titleStyle);
        GUILayout.Label("Статус: " + _statusMessage, subStyle);
        GUILayout.Space(15);

        if (!_showSettingsInMainMenu)
        {
            if (GUILayout.Button("СТВОРИТИ ГРУ (Гравець 1 — Хост)", GUILayout.Height(40)))
            {
                CreateRelayLobby();
            }

            GUILayout.Space(12);
            GUILayout.Label("Код кімнати напарника:");
            _joinCodeInput = GUILayout.TextField(_joinCodeInput.ToUpper(), 6, GUILayout.Height(28));

            if (GUILayout.Button("ПРИЄДНАТИСЯ ЗА КОДОМ (Гравець 2)", GUILayout.Height(38)))
            {
                JoinRelayLobby(_joinCodeInput);
            }

            GUILayout.Space(15);

            if (GUILayout.Button("НАЛАШТУВАННЯ", GUILayout.Height(34)))
            {
                _showSettingsInMainMenu = true;
            }

            GUILayout.Space(6);

            if (GUILayout.Button("ВИЙТИ З ГРИ", GUILayout.Height(34)))
            {
                QuitGame();
            }
        }
        else
        {
            GUILayout.Label("<b>НАЛАШТУВАННЯ АУДІО</b>");
            GUILayout.Space(10);
            GUILayout.Label($"Загальна гучність гри: {Mathf.RoundToInt(_masterVolume * 100)}%");
            _masterVolume = GUILayout.HorizontalSlider(_masterVolume, 0f, 1f);
            AudioListener.volume = _masterVolume;

            GUILayout.Space(25);
            GUILayout.Label("Керування в грі:\n[WASD] — Рух | [E] — Взаємодія\n[F] — Ліхтарик | [T] — Рація | [V] — Свист\n[Esc] — Меню паузи");
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Назад у меню", GUILayout.Height(36)))
            {
                _showSettingsInMainMenu = false;
            }
        }

        GUILayout.EndArea();
    }

    private void DrawInGameHud()
    {
        GUILayout.BeginArea(new Rect(15, 15, 280, 75), GUI.skin.box);
        if (!string.IsNullOrEmpty(_createdJoinCode))
        {
            GUILayout.Label($"Код кімнати: <b><color=yellow>{_createdJoinCode}</color></b>");
        }
        else
        {
            GUILayout.Label("<color=green>Підключено до сесії</color>");
        }
        GUILayout.Label("<size=11>Натисніть [Esc] для меню паузи</size>");
        GUILayout.EndArea();
    }

    private void DrawPauseMenu()
    {
        // Напівпрозорий фон
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

        float width = 360f;
        float height = 340f;
        float x = (Screen.width - width) / 2f;
        float y = (Screen.height - height) / 2f;

        GUI.Box(new Rect(x, y, width, height), "");

        GUILayout.BeginArea(new Rect(x + 25f, y + 20f, width - 50f, height - 40f));

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        GUILayout.Label("ПАУЗА", headerStyle);
        GUILayout.Space(10);

        if (!string.IsNullOrEmpty(_createdJoinCode))
        {
            string copyBtnText = _codeCopiedFeedback
                ? $"Скопійовано: {_createdJoinCode} ✓"
                : $"Скопіювати код кімнати ({_createdJoinCode})";

            if (GUILayout.Button(copyBtnText, GUILayout.Height(30)))
            {
                GUIUtility.systemCopyBuffer = _createdJoinCode;
                _codeCopiedFeedback = true;
            }
            GUILayout.Space(8);
        }

        if (GUILayout.Button("ПРОДОВЖИТИ ГРУ", GUILayout.Height(38)))
        {
            SetPauseMenu(false);
        }

        GUILayout.Space(12);
        GUILayout.Label($"Загальна гучність: {Mathf.RoundToInt(_masterVolume * 100)}%");
        _masterVolume = GUILayout.HorizontalSlider(_masterVolume, 0f, 1f);
        AudioListener.volume = _masterVolume;
        GUILayout.Space(15);

        if (GUILayout.Button("ВИЙТИ В ГОЛОВНЕ МЕНЮ", GUILayout.Height(35)))
        {
            DisconnectToMainMenu();
        }

        GUILayout.Space(6);

        if (GUILayout.Button("ВИЙТИ НА РОБОЧИЙ СТІЛ", GUILayout.Height(35)))
        {
            QuitGame();
        }

        GUILayout.EndArea();
    }

    private void DisconnectToMainMenu()
    {
        IsPauseMenuOpen = false;
        _createdJoinCode = "";
        _codeCopiedFeedback = false;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        _statusMessage = "Ви вийшли з сесії";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private async void CreateRelayLobby()
    {
        _statusMessage = "Створення кімнати...";
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            _createdJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _codeCopiedFeedback = false;

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            IsPauseMenuOpen = false;
            NetworkManager.Singleton.StartHost();
            _statusMessage = "Кімнату створено!";
        }
        catch (Exception ex)
        {
            _statusMessage = "Помилка створення: " + ex.Message;
        }
    }

    private async void JoinRelayLobby(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            _statusMessage = "Введіть 6-значний код кімнати!";
            return;
        }

        _statusMessage = "Підключення до кімнати...";
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

            IsPauseMenuOpen = false;
            NetworkManager.Singleton.StartClient();
            _statusMessage = "Підключення успішне!";
        }
        catch (Exception ex)
        {
            _statusMessage = "Не вдалося зайти: " + ex.Message;
        }
    }
}