using System;
using Unity.Netcode;

namespace NSM
{
    public struct GameEventDTO : INetworkSerializable
    {
        public IGameEvent gameEvent;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (gameEvent == null)
            {
                Type gameEventType = TypeStore.Instance.GameEventType;
                gameEvent = (IGameEvent)Activator.CreateInstance(gameEventType);
            }

            gameEvent.NetworkSerialize(serializer);
        }
    }
}
