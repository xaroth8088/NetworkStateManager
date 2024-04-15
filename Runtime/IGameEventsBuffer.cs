using System.Collections.Generic;
using Unity.Netcode;

namespace NSM
{
    public interface IGameEventsBuffer
    {
        HashSet<IGameEvent> this[int i] { get; set; }

        void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter;
    }
}