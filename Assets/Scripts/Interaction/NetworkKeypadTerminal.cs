using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkKeypadTerminal : NetworkBehaviour, IInteractable
{
    public static NetworkKeypadTerminal ActiveLocalTerminal { get; private set; }
    public static bool IsAnyTerminalOpen => ActiveLocalTerminal != null;

    [Header("Налаштування кодового термінала 80-х")]
    [SerializeField] private string terminalTitle = "ТЕРМІНАЛ ДОСТУПУ «ЕЛЕКТРОНІКА-88»";
    [SerializeField] private string secretPinCode = "1987";

    [Header("Що відкриває правильний пароль")]
    [Tooltip("Перетягни сюди Гермодвері (Airlock_Door), які відкриються після вводу коду")]
    [SerializeField] private NetworkAirlockDoor targetDoor;

    [Tooltip("Або перетягни сюди вузол живлення (Interactive_Terminal)")]
    [SerializeField] private NetworkInteractiveObject targetPowerNode;

    [Header("Шум сигналізації при помилці вводу")]
    [Range(0f, 1f)]
    [SerializeField] private float wrongCodeAlarmLoudness = 0.9f;

    public NetworkVariable<bool> IsUnlocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private MeshRenderer _renderer;
    private string _currentInput = "";
    private string _screenStatus = "ВВЕДІТЬ 4-ЗНАЧНИЙ КОД ДОСТУПУ";

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        IsUnlocked.OnValueChanged += OnUnlockedChanged;
        UpdateVisual(IsUnlocked.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsUnlocked.OnValueChanged -= OnUnlockedChanged;
        if (ActiveLocalTerminal == this)
        {
            CloseTerminalUi();
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (ActiveLocalTerminal != this) return;

        // Дозволяємо вводити цифри також прямо з клавіатури
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
            {
                AppendDigit(i.ToString());
            }
        }

        if (Input.GetKeyDown(KeyCode.Backspace) && _currentInput.Length > 0)
        {
            _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SubmitCode();
        }
    }

    public string GetPromptText()
    {
        return IsUnlocked.Value
            ? "[Розблоковано] Доступ до сектора відкрито"
            : "[E] Ввести код доступу (Термінал)";
    }

    public void Interact()
    {
        if (IsUnlocked.Value) return;
        OpenTerminalUi();
    }

    public void OpenTerminalUi()
    {
        ActiveLocalTerminal = this;
        _currentInput = "";
        _screenStatus = "ВВЕДІТЬ 4-ЗНАЧНИЙ КОД ДОСТУПУ";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseTerminalUi()
    {
        if (ActiveLocalTerminal == this)
        {
            ActiveLocalTerminal = null;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void AppendDigit(string digit)
    {
        if (_currentInput.Length < 6)
        {
            _currentInput += digit;
        }
    }

    private void SubmitCode()
    {
        SubmitPinServerRpc(_currentInput);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitPinServerRpc(string enteredPin, ServerRpcParams rpcParams = default)
    {
        if (IsUnlocked.Value) return;

        if (enteredPin == secretPinCode)
        {
            IsUnlocked.Value = true;

            if (targetDoor != null)
            {
                targetDoor.IsDoorOpen.Value = true;
            }

            if (targetPowerNode != null)
            {
                targetPowerNode.IsActive.Value = true;
            }

            NotifyPinResultClientRpc(true, rpcParams.Receive.SenderClientId);
        }
        else
        {
            // Помилка пароля -> вмикається сирена і кличе NPC!
            NoiseDispatcher.EmitOnServer(new NoiseEvent
            {
                Position = transform.position,
                Loudness = wrongCodeAlarmLoudness,
                Source = NoiseSource.Environmental
            });

            NotifyPinResultClientRpc(false, rpcParams.Receive.SenderClientId);
        }
    }

    [ClientRpc]
    private void NotifyPinResultClientRpc(bool success, ulong callerClientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != callerClientId) return;

        if (success)
        {
            _screenStatus = "ДОСТУП ДОЗВОЛЕНО! ШЛЮЗ ВІДКРИТО";
            CloseTerminalUi();
        }
        else
        {
            _currentInput = "";
            _screenStatus = "ПОМИЛКА КОДУ! СПРАЦЮВАЛА ТРИВОГА!";
        }
    }

    private void OnUnlockedChanged(bool previousValue, bool newValue)
    {
        UpdateVisual(newValue);
        if (newValue && ActiveLocalTerminal == this)
        {
            CloseTerminalUi();
        }
    }

    private void UpdateVisual(bool unlocked)
    {
        if (_renderer != null)
        {
            _renderer.material.color = unlocked ? Color.green : new Color(0.15f, 0.45f, 0.25f);
        }
    }

    private void OnGUI()
    {
        if (ActiveLocalTerminal != this || NetworkBootstrapUI.IsPauseMenuOpen) return;

        float width = 360f;
        float height = 430f;
        float x = (Screen.width - width) / 2f;
        float y = (Screen.height - height) / 2f;

        GUI.Box(new Rect(x, y, width, height), "");

        GUILayout.BeginArea(new Rect(x + 20f, y + 15f, width - 40f, height - 30f));

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        headerStyle.normal.textColor = new Color(0.2f, 1f, 0.4f);

        GUIStyle displayStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        displayStyle.normal.textColor = new Color(0.2f, 1f, 0.4f);

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };
        statusStyle.normal.textColor = _screenStatus.Contains("ПОМИЛКА") ? Color.red : new Color(0.6f, 1f, 0.6f);

        GUILayout.Label(terminalTitle, headerStyle);
        GUILayout.Label(_screenStatus, statusStyle);
        GUILayout.Space(8);

        string maskedDisplay = string.IsNullOrEmpty(_currentInput) ? "[ _ _ _ _ ]" : _currentInput;
        GUILayout.Box(maskedDisplay, displayStyle, GUILayout.Height(48), GUILayout.ExpandWidth(true));
        GUILayout.Space(12);

        // Цифрова клавіатура 3x3
        for (int row = 0; row < 3; row++)
        {
            GUILayout.BeginHorizontal();
            for (int col = 1; col <= 3; col++)
            {
                int num = row * 3 + col;
                if (GUILayout.Button(num.ToString(), GUILayout.Height(48)))
                {
                    AppendDigit(num.ToString());
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        // Нижній ряд: СТЕРТИ | 0 | ВВІД
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("СТЕРТИ", GUILayout.Height(48)))
        {
            _currentInput = "";
        }
        if (GUILayout.Button("0", GUILayout.Height(48)))
        {
            AppendDigit("0");
        }
        if (GUILayout.Button("ВВІД [OK]", GUILayout.Height(48)))
        {
            SubmitCode();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(12);
        if (GUILayout.Button("Вийти з термінала [Esc]", GUILayout.Height(32)))
        {
            CloseTerminalUi();
        }

        GUILayout.EndArea();
    }
}