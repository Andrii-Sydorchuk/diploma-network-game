using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class NetworkReadableNote : NetworkBehaviour, IInteractable
{
    public static NetworkReadableNote ActiveLocalNote { get; private set; }
    public static bool IsAnyNoteOpen => ActiveLocalNote != null;

    [Header("Зміст документа / записки 80-х")]
    [SerializeField] private string noteTitle = "АРХІВНИЙ ПРОТОКОЛ №14-Б";
    [SerializeField] private string noteAuthor = "Ст. інженер О. Ковальчук (14.11.1987)";

    [TextArea(5, 12)]
    [SerializeField] private string noteBodyText =
        "Службова записка для зміни охорони:\n\n" +
        "Через збій у поведінковій матриці Об'єкта «Копія» центральний гермошлюз переведено на ручне кодування.\n\n" +
        "Тимчасовий PIN-код термінала «Електроніка-88»: 1987.\n\n" +
        "УВАГА: Не розмовляйте голосно поблизу вентиляційних шахт Сектора Б — акустичні сенсори прототипу реагують на будь-який шум!";

    [Header("Звук шелесту паперу для NPC")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pickupPaperNoise = 0.12f;

    [Header("Кольори паперу (Непрочитана / Прочитана)")]
    [SerializeField] private Color unreadColor = new Color(0.95f, 0.9f, 0.72f);
    [SerializeField] private Color discoveredColor = new Color(0.75f, 0.85f, 0.75f);

    // Синхронізуємо факт того, що один із гравців уже знайшов і прочитав цю записку
    public NetworkVariable<bool> HasBeenDiscovered = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private MeshRenderer _renderer;
    private Texture2D _paperBgTex;
    private Texture2D _borderTex;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        CreatePaperTextures();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        HasBeenDiscovered.OnValueChanged += OnDiscoveredChanged;
        UpdateVisual(HasBeenDiscovered.Value);
    }

    public override void OnNetworkDespawn()
    {
        HasBeenDiscovered.OnValueChanged -= OnDiscoveredChanged;
        if (ActiveLocalNote == this)
        {
            CloseNoteUi();
        }
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (ActiveLocalNote != this) return;

        // Дозволяємо закрити записку повторним натисканням [Q] або [Space] (крім [Esc], який обробляє PlayerController)
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Space))
        {
            CloseNoteUi();
        }
    }

    public string GetPromptText()
    {
        string statusTag = HasBeenDiscovered.Value ? " (Прочитано)" : "";
        return $"[E] Прочитати: {noteTitle}{statusTag}";
    }

    public void Interact()
    {
        if (ActiveLocalNote == this)
        {
            CloseNoteUi();
            return;
        }

        OpenNoteUi();

        if (IsSpawned)
        {
            NotifyNoteReadServerRpc();
        }
    }

    public void OpenNoteUi()
    {
        ActiveLocalNote = this;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseNoteUi()
    {
        if (ActiveLocalNote == this)
        {
            ActiveLocalNote = null;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyNoteReadServerRpc()
    {
        if (!HasBeenDiscovered.Value)
        {
            HasBeenDiscovered.Value = true;
        }

        if (pickupPaperNoise > 0.01f)
        {
            NoiseDispatcher.EmitOnServer(new NoiseEvent
            {
                Position = transform.position,
                Loudness = pickupPaperNoise,
                Source = NoiseSource.Environmental
            });
        }
    }

    private void OnDiscoveredChanged(bool previousValue, bool newValue)
    {
        UpdateVisual(newValue);
    }

    private void UpdateVisual(bool discovered)
    {
        if (_renderer != null)
        {
            _renderer.material.color = discovered ? discoveredColor : unreadColor;
        }
    }

    private void CreatePaperTextures()
    {
        _paperBgTex = new Texture2D(1, 1);
        _paperBgTex.SetPixel(0, 0, new Color(0.12f, 0.14f, 0.11f, 0.96f));
        _paperBgTex.Apply();

        _borderTex = new Texture2D(1, 1);
        _borderTex.SetPixel(0, 0, new Color(0.35f, 0.85f, 0.45f, 0.8f));
        _borderTex.Apply();
    }

    private void OnGUI()
    {
        if (ActiveLocalNote != this || NetworkBootstrapUI.IsPauseMenuOpen) return;

        float width = 520f;
        float height = 420f;
        float x = (Screen.width - width) / 2f;
        float y = (Screen.height - height) / 2f;

        // Зовнішня зелена рамка в стилі ЕПТ-монітора / папки "ТАЄМНО"
        GUI.DrawTexture(new Rect(x - 2f, y - 2f, width + 4f, height + 4f), _borderTex);
        GUI.DrawTexture(new Rect(x, y, width, height), _paperBgTex);

        GUILayout.BeginArea(new Rect(x + 28f, y + 22f, width - 56f, height - 44f));

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        headerStyle.normal.textColor = new Color(0.35f, 1f, 0.5f);

        GUIStyle subHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleCenter
        };
        subHeaderStyle.normal.textColor = new Color(0.65f, 0.85f, 0.65f);

        GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            wordWrap = true,
            richText = true,
            alignment = TextAnchor.UpperLeft
        };
        bodyStyle.normal.textColor = new Color(0.88f, 0.96f, 0.88f);

        GUILayout.Label("МІНІСТЕРСТВО АВТОМАТИЗАЦІЇ — ПРОЄКТ «СИНХРОНІЗАЦІЯ»", subHeaderStyle);
        GUILayout.Space(4);
        GUILayout.Label(noteTitle, headerStyle);
        GUILayout.Label($"Автор: {noteAuthor}", subHeaderStyle);

        GUILayout.Space(14);
        GUI.DrawTexture(GUILayoutUtility.GetRect(width - 56f, 1f), _borderTex);
        GUILayout.Space(14);

        GUILayout.Label(noteBodyText, bodyStyle, GUILayout.ExpandHeight(true));

        GUILayout.Space(10);
        if (GUILayout.Button("ЗАКРИТИ ДОКУМЕНТ [Esc / Q]", GUILayout.Height(36)))
        {
            CloseNoteUi();
        }

        GUILayout.EndArea();
    }
}