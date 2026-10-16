using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TheCopy.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(AudioLowPassFilter))]
    public class VoiceNoiseEmitter : NetworkBehaviour
    {
        [Header("1. Чутливість та Підсилення мікрофона")]
        [Tooltip("Поріг активації голосу (відсікає тишу)")]
        [Range(0.0005f, 0.03f)]
        [SerializeField] private float micActivationThreshold = 0.0015f;

        [Tooltip("Підсилення гучності мікрофона")]
        [Range(1.0f, 15.0f)]
        [SerializeField] private float micBoostGain = 6.0f;

        [Tooltip("Час утримання мікрофона відкритим між словами (сек) — щоб мова не переривалася")]
        [Range(0.2f, 1.0f)]
        [SerializeField] private float voiceHoldDuration = 0.6f;

        [Tooltip("Рівень RMS, який вважається максимально гучним для NPC (1.0)")]
        [Range(0.02f, 0.3f)]
        [SerializeField] private float maxLoudnessRms = 0.06f;

        [Header("2. Просторовий 3D-чат, Фільтр шуму та Рація")]
        [Tooltip("Максимальна дистанція чутності прямого мовлення (за ТЗ: 15-20м)")]
        [SerializeField] private float maxProximityDistance = 20f;

        [Tooltip("Частота чистого голосу (3800 Гц прибирає сичання мікрофона)")]
        [SerializeField] private float cleanVoiceCutoffHz = 3800f;

        [Tooltip("Частота рації [T] (Гц)")]
        [SerializeField] private float radioVoiceCutoffHz = 2200f;

        [Tooltip("Частота за стіною/дверима (Гц) — робить звук глухим")]
        [SerializeField] private float occludedCutoffFrequency = 850f;

        [Tooltip("УВІМКНИ ТІЛЬКИ ДЛЯ ТЕСТУ ОДНОМУ: щоб чути власний голос у навушниках")]
        [SerializeField] private bool testHearYourself = false;

        [Header("3. Діагностика (Read-Only)")]
        [SerializeField] private string activeMicrophoneName = "Не підключено";
        [SerializeField] private float rawMicRms;
        [SerializeField] private float currentNormalizedLoudness;
        [SerializeField] private bool isTransmittingVoice;
        [SerializeField] private bool isRadioActive;

        public NetworkVariable<float> SyncedVoiceLoudness = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // 11025 Гц, 16-bit PCM, пакети по 50 мс (550 семплів = 1100 байтів)
        private const int SampleRate = 11025;
        private const int ChunkSamples = 550;
        private const int BufferSeconds = 6;
        private const int TotalBufferSamples = SampleRate * BufferSeconds;
        private const int PreBufferChunks = 3; // Чекаємо 3 пакети (150 мс) перед стартом для безшовного звуку

        private AudioClip _micClip;
        private string _micDeviceName;
        private int _lastMicSamplePos;
        private float[] _captureBuffer;
        private byte[] _compressedPacket;
        private float _voiceHoldTimer;

        // Відтворення без розривів
        private AudioSource _voiceAudioSource;
        private AudioLowPassFilter _lowPassFilter;
        private AudioClip _playbackClip;
        private float[] _decompressBuffer;
        private float[] _silenceChunk;
        private float[] _fullSilenceBuffer;
        private int _writePos;
        private int _bufferedChunksCount;
        private float _lastPacketReceivedTime;
        private bool _remoteRadioMode;

        private void Awake()
        {
            _voiceAudioSource = GetComponent<AudioSource>();
            _lowPassFilter = GetComponent<AudioLowPassFilter>();

            ConfigureSpatialAudio();
        }

        private void ConfigureSpatialAudio()
        {
            _voiceAudioSource.playOnAwake = false;
            _voiceAudioSource.loop = true;
            _voiceAudioSource.volume = 1.0f;
            _voiceAudioSource.spatialBlend = 1.0f;
            _voiceAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _voiceAudioSource.minDistance = 2.0f;
            _voiceAudioSource.maxDistance = maxProximityDistance;
            _voiceAudioSource.dopplerLevel = 0f;

            _lowPassFilter.cutoffFrequency = cleanVoiceCutoffHz;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _decompressBuffer = new float[ChunkSamples];
            _silenceChunk = new float[ChunkSamples * 2];
            _fullSilenceBuffer = new float[TotalBufferSamples];

            // stream: false — гарантує відсутність провалів звуку кожні 0.5 сек
            _playbackClip = AudioClip.Create("VoIP_Buffer", TotalBufferSamples, 1, SampleRate, false);
            _voiceAudioSource.clip = _playbackClip;

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

            if (_voiceAudioSource != null && _voiceAudioSource.isPlaying)
            {
                _voiceAudioSource.Stop();
            }

            base.OnNetworkDespawn();
        }

        private void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[VoIP] Мікрофон не знайдено! Використовуй [V] для звукової приманки.");
                return;
            }

            _micDeviceName = Microphone.devices[0];
            activeMicrophoneName = _micDeviceName;
            _captureBuffer = new float[ChunkSamples];
            _compressedPacket = new byte[ChunkSamples * 2];

            _micClip = Microphone.Start(_micDeviceName, true, 2, SampleRate);
            _lastMicSamplePos = 0;

            Debug.Log($"[VoIP] Голосовий чат активний ('{_micDeviceName}'). [T] — Рація, [V] — Свист.");
        }

        private void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner)
            {
                ProcessLocalMicrophone();
            }

            UpdatePlaybackAndFilter();
        }

        private void ProcessLocalMicrophone()
        {
            isRadioActive = IsRadioHeld();

            if (IsWhistlePressed())
            {
                currentNormalizedLoudness = 1.0f;
                EmitNoiseOnlyServerRpc(1.0f);
                Debug.Log("[VoIP] Використано звукову приманку [V] (Гучність: 1.0)");
            }

            if (_micClip == null) return;

            int currentMicPos = Microphone.GetPosition(_micDeviceName);
            if (currentMicPos < 0) return;

            int availableSamples = currentMicPos - _lastMicSamplePos;
            if (availableSamples < 0)
            {
                availableSamples += _micClip.samples;
            }

            while (availableSamples >= ChunkSamples)
            {
                _micClip.GetData(_captureBuffer, _lastMicSamplePos);
                _lastMicSamplePos = (_lastMicSamplePos + ChunkSamples) % _micClip.samples;
                availableSamples -= ChunkSamples;

                rawMicRms = CalculateRms(_captureBuffer);

                if (rawMicRms >= micActivationThreshold)
                {
                    _voiceHoldTimer = voiceHoldDuration;
                }
                else
                {
                    _voiceHoldTimer -= (float)ChunkSamples / SampleRate;
                }

                isTransmittingVoice = _voiceHoldTimer > 0f;

                if (!isTransmittingVoice)
                {
                    currentNormalizedLoudness = 0f;
                }
                else
                {
                    currentNormalizedLoudness = Mathf.Clamp01(
                        (rawMicRms - micActivationThreshold) / Mathf.Max(0.001f, maxLoudnessRms - micActivationThreshold)
                    );

                    CompressToCleanPcm16(_captureBuffer, _compressedPacket, micBoostGain, micActivationThreshold * 0.4f);
                    SendVoicePacketServerRpc(_compressedPacket, Mathf.Max(0.05f, currentNormalizedLoudness), isRadioActive);
                }
            }
        }

        private void UpdatePlaybackAndFilter()
        {
            // Зупиняємо плеєр, коли пакети не надходять понад 0.35 сек
            if (_voiceAudioSource.isPlaying && Time.time - _lastPacketReceivedTime > 0.35f)
            {
                _voiceAudioSource.Stop();
                _bufferedChunksCount = 0;
            }

            if (!_voiceAudioSource.isPlaying) return;

            if (_remoteRadioMode || (IsOwner && testHearYourself))
            {
                _voiceAudioSource.spatialBlend = 0f;
                _lowPassFilter.cutoffFrequency = _remoteRadioMode ? radioVoiceCutoffHz : cleanVoiceCutoffHz;
                return;
            }

            _voiceAudioSource.spatialBlend = 1.0f;

            Camera localCam = Camera.main;
            if (localCam != null)
            {
                Vector3 listenerPos = localCam.transform.position;
                Vector3 speakerPos = transform.position + Vector3.up * 0.6f;

                bool isBlockedByWall = false;
                if (Physics.Linecast(listenerPos, speakerPos, out RaycastHit hit))
                {
                    if (hit.transform != transform && hit.transform.root != localCam.transform.root)
                    {
                        isBlockedByWall = true;
                    }
                }

                float targetCutoff = isBlockedByWall ? occludedCutoffFrequency : cleanVoiceCutoffHz;
                _lowPassFilter.cutoffFrequency = Mathf.Lerp(_lowPassFilter.cutoffFrequency, targetCutoff, 12f * Time.deltaTime);
            }
        }

        [ServerRpc]
        private void SendVoicePacketServerRpc(byte[] pcm16Data, float normalizedLoudness, bool radioMode)
        {
            normalizedLoudness = Mathf.Clamp01(normalizedLoudness);
            SyncedVoiceLoudness.Value = normalizedLoudness;

            if (normalizedLoudness > 0.01f)
            {
                NoiseDispatcher.EmitOnServer(new NoiseEvent
                {
                    Position = transform.position,
                    Loudness = normalizedLoudness,
                    Source = NoiseSource.PlayerVoice
                });
            }

            ReceiveVoicePacketClientRpc(pcm16Data, radioMode);
        }

        [ServerRpc(RequireOwnership = false)]
        private void EmitNoiseOnlyServerRpc(float normalizedLoudness)
        {
            normalizedLoudness = Mathf.Clamp01(normalizedLoudness);
            SyncedVoiceLoudness.Value = normalizedLoudness;

            NoiseDispatcher.EmitOnServer(new NoiseEvent
            {
                Position = transform.position,
                Loudness = normalizedLoudness,
                Source = NoiseSource.PlayerVoice
            });
        }

        [ClientRpc]
        private void ReceiveVoicePacketClientRpc(byte[] pcm16Data, bool radioMode)
        {
            if (IsOwner && !testHearYourself) return;
            if (pcm16Data == null || pcm16Data.Length != ChunkSamples * 2) return;

            _remoteRadioMode = radioMode;
            _lastPacketReceivedTime = Time.time;

            DecompressPcm16ToFloat(pcm16Data, _decompressBuffer);

            if (!_voiceAudioSource.isPlaying)
            {
                if (_bufferedChunksCount == 0)
                {
                    _playbackClip.SetData(_fullSilenceBuffer, 0);
                    _writePos = 0;
                }

                _playbackClip.SetData(_decompressBuffer, _writePos);
                _writePos = (_writePos + ChunkSamples) % TotalBufferSamples;
                _bufferedChunksCount++;

                // Запускаємо відтворення, коли накопичили 3 пакети (150 мс запасу)
                if (_bufferedChunksCount >= PreBufferChunks)
                {
                    _voiceAudioSource.timeSamples = 0;
                    _voiceAudioSource.Play();
                }
            }
            else
            {
                int playPos = _voiceAudioSource.timeSamples;
                int leadSamples = (_writePos - playPos + TotalBufferSamples) % TotalBufferSamples;

                // Якщо мережа сильно лагнула і буфер збився — вирівнюємо на 3 чанки вперед
                if (leadSamples < ChunkSamples / 2 || leadSamples > ChunkSamples * 10)
                {
                    _writePos = (playPos + ChunkSamples * PreBufferChunks) % TotalBufferSamples;
                }

                // Записуємо поточний пакет голосу
                _playbackClip.SetData(_decompressBuffer, _writePos);
                _writePos = (_writePos + ChunkSamples) % TotalBufferSamples;

                // Одразу затираємо наступну ділянку попереду тишею, щоб старі фрази ніколи не повторилися
                _playbackClip.SetData(_silenceChunk, _writePos);
            }
        }

        private static float CalculateRms(float[] samples)
        {
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float s = samples[i];
                sum += s * s;
            }
            return Mathf.Sqrt(sum / samples.Length);
        }

        private static void CompressToCleanPcm16(float[] input, byte[] output, float gain, float noiseFloor)
        {
            for (int i = 0; i < input.Length; i++)
            {
                float s = input[i];
                if (Mathf.Abs(s) < noiseFloor)
                {
                    s *= 0.2f;
                }

                float boosted = (float)System.Math.Tanh(s * gain);
                short val = (short)(boosted * 32767f);

                output[i * 2] = (byte)(val & 0xFF);
                output[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
            }
        }

        private static void DecompressPcm16ToFloat(byte[] input, float[] output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                short val = (short)(input[i * 2] | (input[i * 2 + 1] << 8));
                output[i] = val / 32767f;
            }
        }

        private static bool IsWhistlePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.V);
#endif
        }

        private static bool IsRadioHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tKey.isPressed;
#else
            return Input.GetKey(KeyCode.T);
#endif
        }
    }
}