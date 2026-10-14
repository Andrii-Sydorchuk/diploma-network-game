using Unity.Netcode;
using UnityEngine;

public class ChessSeat : NetworkBehaviour, IInteractable
{
    [SerializeField] private string seatSideName = "Стілець 1 (Білі)";
    [SerializeField] private Transform sitTransform;

    // ulong.MaxValue означає, що стілець вільний
    public NetworkVariable<ulong> OccupantClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool isLocalSeated = false;

    public string GetPromptText()
    {
        if (OccupantClientId.Value != ulong.MaxValue)
        {
            return ""; // Якщо місце вже зайняте іншим гравцем
        }
        return $"[E] Сісти ({seatSideName})";
    }

    public void Interact()
    {
        if (OccupantClientId.Value == ulong.MaxValue)
        {
            RequestSitServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSitServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (OccupantClientId.Value == ulong.MaxValue)
        {
            OccupantClientId.Value = senderId;
            ConfirmSitClientRpc(senderId);
        }
    }

    [ClientRpc]
    private void ConfirmSitClientRpc(ulong seatedClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == seatedClientId)
        {
            isLocalSeated = true;
            if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                PlayerController player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
                if (player != null)
                {
                    player.EnterSeat(this, sitTransform != null ? sitTransform : transform);
                }
            }
        }
    }

    public void StandUp()
    {
        if (isLocalSeated)
        {
            RequestStandUpServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStandUpServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (OccupantClientId.Value == senderId)
        {
            OccupantClientId.Value = ulong.MaxValue;
            ConfirmStandUpClientRpc(senderId);
        }
    }

    [ClientRpc]
    private void ConfirmStandUpClientRpc(ulong standingClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == standingClientId)
        {
            isLocalSeated = false;
            if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                PlayerController player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
                if (player != null)
                {
                    player.ExitSeat();
                }
            }
        }
    }
}