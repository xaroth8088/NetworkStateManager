using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    [MemoryPackable]
    public partial struct PhysicsStateDTO
    {
        private Dictionary<byte, RigidBodyStateDTO> _rigidBodyStates;

        public Dictionary<byte, RigidBodyStateDTO> RigidBodyStates
        {
            get => _rigidBodyStates ??= new();
            set => _rigidBodyStates = value;
        }

        public void TakeSnapshot(List<Rigidbody> rigidbodies)
        {
            _rigidBodyStates ??= new();

            foreach (Rigidbody body in rigidbodies)
            {
                if (!body.gameObject.TryGetComponent<NetworkId>(out var networkIdComponent))
                {
                    continue;
                }

                _rigidBodyStates[networkIdComponent.networkId] = new RigidBodyStateDTO(body);
            }
        }

        public byte[] GetBinaryRepresentation()
        {
            return MemoryPackSerializer.Serialize(this);
        }

        public void RestoreFromBinaryRepresentation(byte[] bytes)
        {
            MemoryPackSerializer.Deserialize(bytes, ref this);
        }
    }
}