using NSM;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct GameStateDTO : IGameState
{
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
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

public struct GameEventDTO : IGameEvent
{
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
    }
}

public class GameManager : MonoBehaviour
{
    NetworkStateManager networkStateManager;

    void Start()
    {
        NetworkManager.Singleton.StartHost();

        // Cache this for easy usage later
        networkStateManager = FindObjectOfType<NetworkStateManager>();

        // Attach lifecycle events (as needed)

        // Start up the game engine
        networkStateManager.StartNetworkStateManager(typeof(GameStateDTO), typeof(PlayerInputDTO), typeof(GameEventDTO));
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
