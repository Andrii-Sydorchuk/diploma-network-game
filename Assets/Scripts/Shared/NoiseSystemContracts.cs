using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TheCopy.Shared
{
    public enum NoiseSource : byte
    {
        PlayerVoice = 0,     // Позиційний голосовий чат (VoIP)
        PlayerFootstep = 1,  // Звук бігу/ходьби
        PhysicalImpact = 2,  // Падіння ящика, рух дверей
        Environmental = 3    // Сирени, генератори, клацання реле
    }

    public enum NpcMode : byte
    {
        Disabled = 0,
        Idle = 1,
        AcousticPatrol = 2,
        InvestigateSound = 3,
        Cooperate = 4,
        SecurityLockdown = 5
    }

    /// <summary>
    /// Структура події шуму. Реалізує INetworkSerializable для передачі через NGO RPC.
    /// </summary>
    public struct NoiseEvent : INetworkSerializable
    {
        public Vector3 Position;
        public float Loudness; // Нормалізоване значення: 0.0f - 1.0f
        public NoiseSource Source;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Loudness);
            serializer.SerializeValue(ref Source);
        }
    }

    public interface INoiseListener
    {
        void OnNoise(NoiseEvent noiseEvent);
    }

    public interface INetworkInteractable
    {
        void Interact(ulong clientId);
        bool CanInteract(ulong clientId);
    }

    /// <summary>
    /// Легковаговий серверний диспетчер шуму без алокацій пам'яті (Zero-GC).
    /// </summary>
    public static class NoiseDispatcher
    {
        private static readonly HashSet<INoiseListener> Listeners = new HashSet<INoiseListener>();

        public static void Register(INoiseListener listener) => Listeners.Add(listener);
        public static void Unregister(INoiseListener listener) => Listeners.Remove(listener);

        public static void EmitOnServer(NoiseEvent noiseEvent)
        {
            foreach (var listener in Listeners)
            {
                listener?.OnNoise(noiseEvent);
            }
        }
    }
}