using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MenuMusicController : MonoBehaviour
{
    [Header("Саундтрек головного меню")]
    [Tooltip("Перетягни сюди свій mp3/wav файл із папки Assets/Audio")]
    [SerializeField] private AudioClip menuMusicClip;

    [Range(0f, 1f)]
    [SerializeField] private float menuVolume = 0.5f;

    [Header("Поведінка музики після старту гри")]
    [Tooltip("Якщо увімкнено — музика плавно вимкнеться при вході в гру. Якщо вимкнено — гратиме тихо на фоні.")]
    [SerializeField] private bool stopMusicInGame = true;

    [Range(0f, 0.5f)]
    [SerializeField] private float inGameAmbientVolume = 0.08f;

    [SerializeField] private float fadeSpeed = 0.8f;

    private AudioSource _musicSource;
    private AudioListener _menuAudioListener;

    private void Awake()
    {
        _musicSource = GetComponent<AudioSource>();
        _musicSource.clip = menuMusicClip;
        _musicSource.loop = true;
        _musicSource.playOnAwake = false;
        _musicSource.spatialBlend = 0f; // 2D звук (грає рівномірно в обох навушниках)
        _musicSource.volume = menuVolume;

        // Додаємо тимчасовий AudioListener для меню, поки гравець ще не заспавнився
        _menuAudioListener = GetComponent<AudioListener>();
        if (_menuAudioListener == null)
        {
            _menuAudioListener = gameObject.AddComponent<AudioListener>();
        }
    }

    private void Start()
    {
        if (menuMusicClip != null)
        {
            _musicSource.Play();
        }
    }

    private void Update()
    {
        bool isInGame = NetworkManager.Singleton != null &&
                        (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

        // Вимикаємо слухач меню, як тільки з'явився гравець зі своїми "вухами", щоб не було дублювання
        if (_menuAudioListener != null)
        {
            _menuAudioListener.enabled = !isInGame;
        }

        if (!_musicSource.isPlaying) return;

        float targetVolume = isInGame
            ? (stopMusicInGame ? 0f : inGameAmbientVolume)
            : menuVolume;

        _musicSource.volume = Mathf.MoveTowards(_musicSource.volume, targetVolume, fadeSpeed * Time.deltaTime);

        if (isInGame && stopMusicInGame && _musicSource.volume <= 0.001f)
        {
            _musicSource.Stop();
        }
    }
}