using TheCopy.Shared;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkAirlockDoor : NetworkBehaviour, IInteractable
{
    [Header("Логічний зв'язок (Кооперативний пазл)")]
    [Tooltip("Перетягни сюди один або кілька рубильників (Interactive_Terminal), які живлять ці двері")]
    [SerializeField] private NetworkInteractiveObject[] requiredPowerSwitches;

    [Header("Параметри руху гермодверей")]
    [Tooltip("На яку висоту піднімаються двері при відкритті (метри)")]
    [SerializeField] private Vector3 openOffset = new Vector3(0f, 3.2f, 0f);

    [Tooltip("Швидкість відкриття/закриття дверей")]
    [SerializeField] private float doorSpeed = 2.5f;

    public NetworkVariable<bool> IsDoorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 _closedPosition;
    private Vector3 _openPosition;
    private NavMeshObstacle _navMeshObstacle;

    private void Awake()
    {
        _closedPosition = transform.position;
        _openPosition = _closedPosition + openOffset;
        _navMeshObstacle = GetComponent<NavMeshObstacle>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        IsDoorOpen.OnValueChanged += OnDoorStateChanged;
        UpdateNavMeshObstacle(IsDoorOpen.Value);

        if (IsServer && requiredPowerSwitches != null)
        {
            foreach (var sw in requiredPowerSwitches)
            {
                if (sw != null)
                {
                    sw.OnPowerStateChanged += OnLinkedSwitchChanged;
                }
            }

            EvaluatePowerStateOnServer();
        }
    }

    public override void OnNetworkDespawn()
    {
        IsDoorOpen.OnValueChanged -= OnDoorStateChanged;

        if (IsServer && requiredPowerSwitches != null)
        {
            foreach (var sw in requiredPowerSwitches)
            {
                if (sw != null)
                {
                    sw.OnPowerStateChanged -= OnLinkedSwitchChanged;
                }
            }
        }

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        Vector3 targetPos = IsDoorOpen.Value ? _openPosition : _closedPosition;
        if (Vector3.Distance(transform.position, targetPos) > 0.001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, doorSpeed * Time.deltaTime);
        }
    }

    private void OnLinkedSwitchChanged(bool _)
    {
        if (!IsServer) return;
        EvaluatePowerStateOnServer();
    }

    private void EvaluatePowerStateOnServer()
    {
        bool allPowered = HasFullPower(out _, out _);
        if (IsDoorOpen.Value != allPowered)
        {
            IsDoorOpen.Value = allPowered;

            // Звук руху важких дверей для NPC
            NoiseDispatcher.EmitOnServer(new NoiseEvent
            {
                Position = _closedPosition,
                Loudness = 0.6f,
                Source = NoiseSource.PhysicalImpact
            });
        }
    }

    private bool HasFullPower(out int activeCount, out int totalCount)
    {
        activeCount = 0;
        totalCount = 0;

        if (requiredPowerSwitches == null || requiredPowerSwitches.Length == 0)
        {
            return true;
        }

        foreach (var sw in requiredPowerSwitches)
        {
            if (sw == null) continue;
            totalCount++;
            if (sw.IsActive.Value) activeCount++;
        }

        return totalCount == 0 || activeCount == totalCount;
    }

    public string GetPromptText()
    {
        if (!HasFullPower(out int active, out int total))
        {
            return $"[Заблоковано] Подайте живлення з рубильника ({active}/{total})";
        }

        return IsDoorOpen.Value ? "[E] Зачинити гермодвері" : "[E] Відчинити гермодвері";
    }

    public void Interact()
    {
        if (!HasFullPower(out _, out _)) return;
        RequestToggleDoorServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleDoorServerRpc()
    {
        if (!HasFullPower(out _, out _)) return;
        IsDoorOpen.Value = !IsDoorOpen.Value;
    }

    private void OnDoorStateChanged(bool previousValue, bool newValue)
    {
        UpdateNavMeshObstacle(newValue);
    }

    private void UpdateNavMeshObstacle(bool isOpen)
    {
        // Для ШІ напарника (Тиждень 4 ТЗ): коли двері відкриті — прохід для NPC вільний
        if (_navMeshObstacle != null)
        {
            _navMeshObstacle.carving = true;
            _navMeshObstacle.enabled = !isOpen;
        }
    }
}