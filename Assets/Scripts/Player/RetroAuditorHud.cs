using TheCopy.Networking;
using Unity.Netcode;
using UnityEngine;

public class RetroAuditorHud : MonoBehaviour
{
    [Header("Налаштування ЕПТ-візора 80-х (CRT Scanlines)")]
    [SerializeField] private bool showCrtScanlines = true;
    [Range(0f, 0.25f)]
    [SerializeField] private float scanlineOpacity = 0.06f;
    [SerializeField] private bool showCenterReticle = true;

    private PlayerController _localPlayer;
    private VoiceNoiseEmitter _localVoice;

    private Texture2D _whiteTex;
    private Texture2D _scanlineTex;
    private float _displayedNoiseLevel;
    private float _whistleSpikeTimer;

    private void Awake()
    {
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();

        // Генеруємо текстуру тонких горизонтальних ліній ЕПТ-монітора (4 пікселі заввишки)
        _scanlineTex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
        _scanlineTex.filterMode = FilterMode.Point;
        _scanlineTex.wrapMode = TextureWrapMode.Repeat;
        _scanlineTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
        _scanlineTex.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
        _scanlineTex.SetPixel(0, 2, new Color(0f, 0f, 0f, 0.35f));
        _scanlineTex.SetPixel(0, 3, new Color(0f, 0f, 0f, 0f));
        _scanlineTex.Apply();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

        // Автоматично знаходимо локального гравця після спавну
        if (_localPlayer == null &&
            NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            GameObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
            _localPlayer = playerObj.GetComponent<PlayerController>();
            _localVoice = playerObj.GetComponent<VoiceNoiseEmitter>();
        }

        if (_localPlayer == null) return;

        if (Input.GetKeyDown(KeyCode.V))
        {
            _whistleSpikeTimer = 0.6f;
        }

        if (_whistleSpikeTimer > 0f)
        {
            _whistleSpikeTimer -= Time.deltaTime;
        }

        float voiceNoise = _localVoice != null ? _localVoice.SyncedVoiceLoudness.Value : 0f;
        float moveNoise = _localPlayer.CurrentMovementNoise;
        float whistleNoise = _whistleSpikeTimer > 0f ? 1.0f : 0f;

        float targetNoise = Mathf.Max(voiceNoise, moveNoise, whistleNoise);
        float lerpSpeed = targetNoise > _displayedNoiseLevel ? 18f : 4.5f;
        _displayedNoiseLevel = Mathf.Lerp(_displayedNoiseLevel, targetNoise, lerpSpeed * Time.deltaTime);
    }

    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        bool isInGame = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        if (!isInGame || _localPlayer == null) return;

        // 1. Малюємо легкі ЕПТ-лінії (CRT Scanlines) поверх екрана
        if (showCrtScanlines && scanlineOpacity > 0.001f)
        {
            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, scanlineOpacity);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, Screen.width, Screen.height),
                _scanlineTex,
                new Rect(0, 0, 1f, Screen.height / 4f)
            );
            GUI.color = prevColor;
        }

        if (NetworkBootstrapUI.IsPauseMenuOpen) return;

        // 2. Центральна точка прицілу (ховаємо, якщо відкритий термінал або записка)
        if (showCenterReticle &&
            !NetworkKeypadTerminal.IsAnyTerminalOpen &&
            !NetworkReadableNote.IsAnyNoteOpen)
        {
            DrawCenterReticle();
        }

        // 3. Панель акустичного монітора аудитора (лівий нижній кут)
        DrawAcousticVisorPanel();
    }

    private void DrawCenterReticle()
    {
        float cx = Screen.width / 2f;
        float cy = Screen.height / 2f;

        Color prev = GUI.color;
        GUI.color = new Color(0.4f, 1f, 0.55f, 0.65f);
        GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), _whiteTex);
        GUI.color = prev;
    }

    private void DrawAcousticVisorPanel()
    {
        float panelW = 315f;
        float panelH = 122f;
        float x = 15f;
        float y = Screen.height - panelH - 15f;

        Color prev = GUI.color;

        // Темно-зелена підкладка приладу 80-х
        GUI.color = new Color(0.05f, 0.09f, 0.06f, 0.88f);
        GUI.DrawTexture(new Rect(x, y, panelW, panelH), _whiteTex);

        // Тонка рамка приладу
        GUI.color = new Color(0.25f, 0.75f, 0.4f, 0.6f);
        GUI.DrawTexture(new Rect(x, y, panelW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y + panelH - 2f, panelW, 2f), _whiteTex);
        GUI.DrawTexture(new Rect(x, y, 2f, panelH), _whiteTex);
        GUI.DrawTexture(new Rect(x + panelW - 2f, y, 2f, panelH), _whiteTex);
        GUI.color = prev;

        GUILayout.BeginArea(new Rect(x + 12f, y + 8f, panelW - 24f, panelH - 16f));

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.4f, 0.95f, 0.55f);

        int dbPercent = Mathf.RoundToInt(_displayedNoiseLevel * 100f);
        GUILayout.Label($"АКУСТИЧНИЙ СЕНСОР «ЛУНА-88» | ШУМ: {dbPercent}%", titleStyle);
        GUILayout.Space(4);

        // Малюємо 14 світлодіодних сегментів (LED Bar)
        Rect barArea = GUILayoutUtility.GetRect(panelW - 24f, 18f);
        int segments = 14;
        float spacing = 3f;
        float segWidth = (barArea.width - spacing * (segments - 1)) / segments;

        for (int i = 0; i < segments; i++)
        {
            float segThreshold = (i + 1) / (float)segments;
            bool isLit = _displayedNoiseLevel >= (i / (float)segments) && _displayedNoiseLevel > 0.02f;

            Color segColor;
            if (segThreshold <= 0.5f)
                segColor = new Color(0.2f, 0.95f, 0.4f); // Зелений (безпечно)
            else if (segThreshold <= 0.78f)
                segColor = new Color(1f, 0.75f, 0.15f); // Бурштиновий (увага)
            else
                segColor = new Color(1f, 0.2f, 0.2f);   // Червоний (тривога NPC!)

            GUI.color = isLit ? segColor : new Color(0.15f, 0.22f, 0.16f, 0.8f);
            Rect segRect = new Rect(barArea.x + i * (segWidth + spacing), barArea.y, segWidth, barArea.height);
            GUI.DrawTexture(segRect, _whiteTex);
        }
        GUI.color = prev;

        GUILayout.Space(8);

        // Статус режиму руху та рації
        bool isRadioHeld = Input.GetKey(KeyCode.T);
        string radioText = isRadioHeld
            ? "<color=#FFD700>РАЦІЯ [T]: ПЕРЕДАЧА В ЕФІР (2200 Гц)</color>"
            : "<color=#88CC99>РАЦІЯ [T]: ОЧІКУВАННЯ (3D ЧАТ 20М)</color>";

        string postureText;
        if (_localPlayer.IsCrouching)
            postureText = "<color=#55FF77>РЕЖИМ: ТИХИЙ СТЕЛС [Ctrl]</color>";
        else if (_localPlayer.IsSprinting)
            postureText = "<color=#FF5555>РЕЖИМ: ШУМНИЙ БІГ [Shift]</color>";
        else
            postureText = "<color=#AADDAA>РЕЖИМ: ШТАТНА ХОДЬБА</color>";

        string flashText = _localPlayer.IsFlashlightOn.Value
            ? "<color=#FFEE77>ЛІХТАР [F]: УВІМК</color>"
            : "<color=#779977>ЛІХТАР [F]: ВИМК</color>";

        GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            richText = true
        };

        GUILayout.Label(radioText, infoStyle);
        GUILayout.Label($"{postureText}  |  {flashText}", infoStyle);

        GUILayout.EndArea();
    }
}