using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    [MemoryPackable]
    public partial struct PhysicsStateDTO
    {
        public Dictionary<byte, RigidBodyStateDTO> RigidBodyStates;

        public void TakeSnapshot(List<Rigidbody> rigidbodies)
        {
            RigidBodyStates = new();

            foreach (Rigidbody body in rigidbodies)
            {
                if (!body.gameObject.TryGetComponent<NetworkId>(out var networkIdComponent))
                {
                    continue;
                }

                RigidBodyStates[networkIdComponent.networkId] = new RigidBodyStateDTO(body);
            }
        }
    }
}