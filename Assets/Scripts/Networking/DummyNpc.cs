using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

namespace TheCopy.Networking
{
    [RequireComponent(typeof(NetworkObject))]
    public class DummyNpc : NetworkBehaviour, INoiseListener
    {
        [Header("Параметри чуття та руху (Server-Only)")]
        [Tooltip("Мінімальний радіус чутності навіть для тихого голосу (метри)")]
        [SerializeField] private float minHearingRadius = 10f;

        [Tooltip("Максимальний радіус чутності при гучності 1.0 (метри)")]
        [SerializeField] private float maxHearingRadius = 25f;

        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float stoppingDistance = 1.5f;

        [Header("Візуалізація стану")]
        [SerializeField] private Renderer npcRenderer;
        [SerializeField] private Color idleColor = Color.gray;
        [SerializeField] private Color alertColor = Color.red;

        public NetworkVariable<NpcMode> CurrentMode = new NetworkVariable<NpcMode>(
            NpcMode.AcousticPatrol,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<Vector3> LastHeardPosition = new NetworkVariable<Vector3>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private float _alertTimer;
        private Material _instanceMaterial;

        private void Awake()
        {
            if (npcRenderer == null) npcRenderer = GetComponentInChildren<Renderer>();
            if (npcRenderer != null)
            {
                _instanceMaterial = npcRenderer.material;
                _instanceMaterial.EnableKeyword("_EMISSION");
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            CurrentMode.OnValueChanged += OnModeChanged;
            UpdateVisualColor(CurrentMode.Value);

            if (IsServer)
            {
                NoiseDispatcher.Register(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentMode.OnValueChanged -= OnModeChanged;

            if (IsServer)
            {
                NoiseDispatcher.Unregister(this);
            }

            base.OnNetworkDespawn();
        }

        public void OnNoise(NoiseEvent noiseEvent)
        {
            if (!IsServer || CurrentMode.Value == NpcMode.Disabled) return;

            float distance = Vector3.Distance(transform.position, noiseEvent.Position);
            float effectiveHearingRange = Mathf.Lerp(minHearingRadius, maxHearingRadius, noiseEvent.Loudness);

            if (distance <= effectiveHearingRange)
            {
                LastHeardPosition.Value = noiseEvent.Position;
                CurrentMode.Value = NpcMode.InvestigateSound;
                _alertTimer = 4.0f;

                Debug.Log($"[DummyNpc - Server] Почув {noiseEvent.Source} на дистанції {distance:F1}м (Гучність: {noiseEvent.Loudness:F2}) -> Рухаюсь до гравця!");
            }
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            if (CurrentMode.Value == NpcMode.InvestigateSound)
            {
                Vector3 target = new Vector3(LastHeardPosition.Value.x, transform.position.y, LastHeardPosition.Value.z);
                float dist = Vector3.Distance(transform.position, target);

                if (dist > stoppingDistance)
                {
                    Vector3 dir = (target - transform.position).normalized;
                    transform.position += dir * (moveSpeed * Time.deltaTime);
                    if (dir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
                    }
                }

                _alertTimer -= Time.deltaTime;
                if (_alertTimer <= 0f)
                {
                    CurrentMode.Value = NpcMode.AcousticPatrol;
                }
            }
        }

        private void OnModeChanged(NpcMode previousValue, NpcMode newValue)
        {
            UpdateVisualColor(newValue);
        }

        private void UpdateVisualColor(NpcMode mode)
        {
            if (_instanceMaterial == null) return;

            bool isAlert = mode == NpcMode.InvestigateSound;
            Color targetColor = isAlert ? alertColor : idleColor;

            _instanceMaterial.SetColor("_BaseColor", targetColor);
            _instanceMaterial.SetColor("_EmissionColor", isAlert ? alertColor * 2.5f : Color.black);
        }
    }
}