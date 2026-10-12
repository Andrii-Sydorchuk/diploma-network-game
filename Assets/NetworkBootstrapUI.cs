using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrapUI : MonoBehaviour
{
    private void OnGUI()
    {
        // Якщо гра зупиняється або менеджер ще не створений — нічого не малюємо
        if (NetworkManager.Singleton == null) return;

        GUILayout.BeginArea(new Rect(20, 20, 220, 120));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Start Host (Гравець 1)", GUILayout.Height(40)))
            {
                NetworkManager.Singleton.StartHost();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Start Client (Гравець 2)", GUILayout.Height(40)))
            {
                NetworkManager.Singleton.StartClient();
            }
        }

        GUILayout.EndArea();
    }
}