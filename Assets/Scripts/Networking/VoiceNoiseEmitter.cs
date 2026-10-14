using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheCopy.Networking
{
    [DisallowMultipleComponent]
    public class VoiceNoiseEmitter : NetworkBehaviour
    {
        [Header("Налаштування мікрофона та Noise Gate")]
        [Tooltip("Мінімальний поріг гучності (0.005 ловить звичайну мову)")]
        [Range(0.001f, 0.1f)]
        [SerializeField] private float noiseGateThreshold = 0.005f;

        [Tooltip("Рівень RMS, який вважається гучним голосом (1.0)")]
        [Range(0.02f, 0.5f)]
        [SerializeField] private float maxLoudnessRms = 0.08f;

        [Tooltip("Інтервал відправки пакетів шуму на сервер (секунди)")]
        [Range(0.05f, 0.25f)]
        [SerializeField] private float networkSyncInterval = 0.12f;

        [Header("Діагностика мікрофона (Read-Only)")]
        [SerializeField] private string activeMicrophoneName = "Не підключено";
        [SerializeField] private float rawMicRms;
        [SerializeField] private float currentNormalizedLoudness;

        public NetworkVariable<float> SyncedVoiceLoudness = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private AudioClip _micClip;
        private string _micDeviceName;
        private float[] _sampleBuffer;
        private const int SampleWindow = 256;
        private const int SampleRate = 16000;
        private float _nextSyncTime;
        private bool _wasSpeakingLastTick;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                InitializeMicrophone();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && !string.IsNullOrEmpty(_micDeviceName) && Microphone.IsRecording(_micDeviceName))
            {
                Microphone.End(_micDeviceName);
            }

            base.OnNetworkDespawn();
        }

        private void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoiceNoiseEmitter] Мікрофон не знайдено! Використовуй клавішу [V] для звукової приманки.");
                return;
            }

            _micDeviceName = Microphone.devices[0];
            activeMicrophoneName = _micDeviceName;
            _sampleBuffer = new float[SampleWindow];
            _micClip = Microphone.Start(_micDeviceName, true, 1, SampleRate);

            Debug.Log($"[VoiceNoiseEmitter] Підключено мікрофон: '{_micDeviceName}'. Говори або натисни [V] для тесту шуму.");
        }

        private void Update()
        {
            if (!IsOwner || !IsSpawned) return;

            // Перевірка кнопки [V] — звукова приманка / свист (за GDD)
            if (IsWhistlePressed())
            {
                currentNormalizedLoudness = 1.0f;
                ReportVoiceNoiseServerRpc(1.0f);
                Debug.Log("[VoiceNoiseEmitter] Використано звукову приманку [V] (Гучність: 1.0)");
                return;
            }

            if (_micClip == null) return;

            rawMicRms = CalculateMicrophoneRms();

            if (rawMicRms < noiseGateThreshold)
            {
                currentNormalizedLoudness = 0f;
            }
            else
            {
                currentNormalizedLoudness = Mathf.Clamp01(
                    (rawMicRms - noiseGateThreshold) / Mathf.Max(0.001f, maxLoudnessRms - noiseGateThreshold)
                );
            }

            if (Time.time >= _nextSyncTime)
            {
                _nextSyncTime = Time.time + networkSyncInterval;

                bool isSpeaking = currentNormalizedLoudness > 0.01f;
                if (isSpeaking || _wasSpeakingLastTick)
                {
                    ReportVoiceNoiseServerRpc(currentNormalizedLoudness);
                    _wasSpeakingLastTick = isSpeaking;
                }
            }
        }

        private bool IsWhistlePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                return true;
#endif
            return Input.GetKeyDown(KeyCode.V);
        }

        private float CalculateMicrophoneRms()
        {
            int micPosition = Microphone.GetPosition(_micDeviceName) - SampleWindow + 1;
            if (micPosition < 0) return 0f;

            _micClip.GetData(_sampleBuffer, micPosition);

            float sum = 0f;
            for (int i = 0; i < SampleWindow; i++)
            {
                float sample = _sampleBuffer[i];
                sum += sample * sample;
            }

            return Mathf.Sqrt(sum / SampleWindow);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportVoiceNoiseServerRpc(float normalizedLoudness, ServerRpcParams rpcParams = default)
        {
            normalizedLoudness = Mathf.Clamp01(normalizedLoudness);
            SyncedVoiceLoudness.Value = normalizedLoudness;

            if (normalizedLoudness <= 0.01f) return;

            NoiseEvent voiceNoise = new NoiseEvent
            {
                Position = transform.position,
                Loudness = normalizedLoudness,
                Source = NoiseSource.PlayerVoice
            };

            NoiseDispatcher.EmitOnServer(voiceNoise);
        }
    }
}