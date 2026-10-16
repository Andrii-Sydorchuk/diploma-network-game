using System.Collections.Generic;
using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;

namespace TheCopy.Networking
{
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTriggerZone : NetworkBehaviour
    {
        [Header("Умови спрацьовування зони")]
        [SerializeField] private string sectorName = "Сектор Б (Лабораторія)";

        [Tooltip("Якщо увімкнено — зона спрацює тільки коли ОБИДВА гравці зайдуть всередину")]
        [SerializeField] private bool requireAllConnectedPlayers = false;

        [Tooltip("Спрацювати лише один раз за гру")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Дія 1: Пробудження NPC при вході в зону")]
        [SerializeField] private DummyNpc targetNpc;
        [SerializeField] private bool sleepNpcUntilTriggered = true;
        [SerializeField] private NpcMode modeOnEnter = NpcMode.AcousticPatrol;

        [Header("Дія 2: Подати живлення на вузол / двері (необов'язково)")]
        [SerializeField] private NetworkInteractiveObject linkedPowerNode;
        [SerializeField] private bool activateNodeOnEnter = true;

        public NetworkVariable<int> PlayersInsideCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> HasTriggered = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private readonly HashSet<ulong> _playersInZone = new HashSet<ulong>();
        private BoxCollider _boxCollider;
        private float _bannerTimer;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;

            // Переводимо зону в шар Ignore Raycast (2), щоб вона НІКОЛИ не блокувала кнопку [E]!
            gameObject.layer = 2;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            HasTriggered.OnValueChanged += OnTriggeredChanged;

            // На старті присипляємо NPC, поки гравець не зайде в цю зону за дверима
            if (IsServer && targetNpc != null && sleepNpcUntilTriggered && !HasTriggered.Value)
            {
                targetNpc.CurrentMode.Value = NpcMode.Disabled;
            }
        }

        public override void OnNetworkDespawn()
        {
            HasTriggered.OnValueChanged -= OnTriggeredChanged;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (_bannerTimer > 0f)
            {
                _bannerTimer -= Time.deltaTime;
            }

            if (!IsServer || !IsSpawned || _boxCollider == null) return;

            CheckPlayersInsideBoundsOnServer();
        }

        // Математична перевірка входження в куб (працює на 100% без Rigidbody!)
        private void CheckPlayersInsideBoundsOnServer()
        {
            if (NetworkManager.Singleton == null) return;

            _playersInZone.Clear();
            Bounds zoneBounds = _boxCollider.bounds;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;

                Vector3 playerPos = client.PlayerObject.transform.position;
                if (zoneBounds.Contains(playerPos))
                {
                    _playersInZone.Add(client.ClientId);
                }
            }

            if (PlayersInsideCount.Value != _playersInZone.Count)
            {
                PlayersInsideCount.Value = _playersInZone.Count;
            }

            if (_playersInZone.Count > 0)
            {
                EvaluateTriggerCondition();
            }
        }

        private void EvaluateTriggerCondition()
        {
            if (triggerOnce && HasTriggered.Value) return;

            int requiredCount = 1;
            if (requireAllConnectedPlayers && NetworkManager.Singleton != null)
            {
                requiredCount = Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count);
            }

            if (_playersInZone.Count >= requiredCount)
            {
                HasTriggered.Value = true;
                ExecuteScenarioEventOnServer();
            }
        }

        private void ExecuteScenarioEventOnServer()
        {
            Debug.Log($"[ScenarioTrigger] Активовано '{sectorName}'! Гравців у зоні: {_playersInZone.Count}");

            if (targetNpc != null)
            {
                targetNpc.CurrentMode.Value = modeOnEnter;

                // Одразу подаємо стартовий сигнал тривоги в центр зони, щоб NPC пішов перевіряти сектор
                targetNpc.OnNoise(new NoiseEvent
                {
                    Position = transform.position,
                    Loudness = 1.0f,
                    Source = NoiseSource.Environmental
                });
            }

            if (linkedPowerNode != null)
            {
                linkedPowerNode.IsActive.Value = activateNodeOnEnter;
            }
        }

        private void OnTriggeredChanged(bool previousValue, bool newValue)
        {
            if (newValue)
            {
                _bannerTimer = 4.0f;
            }
        }

        private void OnGUI()
        {
            if (_bannerTimer <= 0f) return;

            GUIStyle style = new GUIStyle
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(1f, 0.35f, 0.2f);

            GUI.Label(
                new Rect(Screen.width / 2f - 250f, 80f, 500f, 40f),
                $"УВАГА: Вхід у {sectorName} — Активовано протокол NPC!",
                style
            );
        }

        private void OnDrawGizmos()
        {
            BoxCollider col = GetComponent<BoxCollider>();
            if (col == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = (Application.isPlaying && HasTriggered.Value)
                ? new Color(0f, 1f, 0.3f, 0.25f)
                : new Color(1f, 0.6f, 0f, 0.25f);
            Gizmos.DrawCube(col.center, col.size);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(col.center, col.size);
        }
    }
}