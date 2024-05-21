using System;
using Unity.Netcode;

namespace NSM
{
    public interface IPlayerInput : INetworkSerializable, IEquatable<IPlayerInput>
    {
    }
}