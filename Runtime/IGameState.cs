using System;
using Unity.Netcode;

namespace NSM
{
    public interface IGameState : INetworkSerializable, ICloneable
    {
    }
}