using System;
using Unity.Netcode;

namespace NSM
{
    // This is a thin wrapper around the unknown/unknowable-until-runtime Player Input object,
    // for use in sending across the wire.
    internal struct PlayerInputDTO : INetworkSerializable
    {
        public IPlayerInput input;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsReader)
            {
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                input = (IPlayerInput)Activator.CreateInstance(playerInputType);
            }

            input.NetworkSerialize(serializer);
        }
    }
}