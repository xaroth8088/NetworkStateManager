using NSM;
using Unity.Netcode;
using UnityEngine;

public struct GameEventDTO : IGameEvent
{
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
    }
}

public struct GameStateDTO : IGameState
{
    public byte[] GetBinaryRepresentation()
    {
        return new byte[0];
    }

    public void RestoreFromBinaryRepresentation(byte[] bytes)
    {
        // Intentionally do nothing (right now)
    }
}

public struct PlayerInputDTO : IPlayerInput
{
    public bool Equals(IPlayerInput other)
    {
        return true;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
    }
}

public class GameManager : MonoBehaviour
{
    private NetworkStateManager networkStateManager;

    private void Start()
    {
        NetworkManager.Singleton.StartHost();

        // Cache this for easy usage later
        networkStateManager = FindObjectOfType<NetworkStateManager>();

        // Attach lifecycle events (as needed)

        // Start up the game engine
        networkStateManager.StartNetworkStateManager(typeof(GameStateDTO), typeof(PlayerInputDTO), typeof(GameEventDTO));
    }
}