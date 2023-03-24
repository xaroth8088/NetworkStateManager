using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public class PhysicsStateDTO : INetworkSerializable
    {
        private Dictionary<byte, RigidBodyStateDTO> _rigidBodyStates;

        public Dictionary<byte, RigidBodyStateDTO> RigidBodyStates
        {
            get => _rigidBodyStates ??= new();
            set => _rigidBodyStates = value;
        }

        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            if (serializer.IsWriter)
            {
                RigidBodyStateDTO[] rigidBodyStates = RigidBodyStates.Values.ToArray();
                serializer.SerializeValue(ref rigidBodyStates);
            }
            else if (serializer.IsReader)
            {
                RigidBodyStateDTO[] rigidBodyStates = new RigidBodyStateDTO[0];
                serializer.SerializeValue(ref rigidBodyStates);
                _rigidBodyStates = new();
                foreach (RigidBodyStateDTO rigidBodyState in rigidBodyStates)
                {
                    _rigidBodyStates[rigidBodyState.networkId] = rigidBodyState;
                }
            }
        }

        public void TakeSnapshot(List<Rigidbody> rigidbodies)
        {
            if (_rigidBodyStates == null)
            {
                _rigidBodyStates = new();
            }

            foreach (Rigidbody body in rigidbodies)
            {
                NetworkId networkIdComponent = body.gameObject.GetComponent<NetworkId>();
                if (networkIdComponent == null)
                {
                    continue;
                }

                _rigidBodyStates[networkIdComponent.networkId] = new RigidBodyStateDTO(body);
            }
        }

        public PhysicsStateDTO GenerateDelta(PhysicsStateDTO newerState)
        {
            PhysicsStateDTO deltaState = new();
            deltaState.RigidBodyStates = new();

            // TODO: there's an opportunity to get _even more_ aggressive by doing a field-by-field delta
            //       for each RigidBodyStateDTO.
            foreach (KeyValuePair<byte, RigidBodyStateDTO> item in newerState.RigidBodyStates)
            {
                if (RigidBodyStates.GetValueOrDefault(item.Key, new RigidBodyStateDTO()).Equals(item.Value))
                {
                    continue;
                }

                deltaState.RigidBodyStates[item.Key] = item.Value;
            }

            return deltaState;
        }

        public void ApplyDelta(PhysicsStateDTO deltaState)
        {
            if (_rigidBodyStates == null)
            {
                _rigidBodyStates = new();
            }

            foreach (KeyValuePair<byte, RigidBodyStateDTO> item in deltaState.RigidBodyStates)
            {
                _rigidBodyStates[item.Key] = item.Value;
            }
        }
    }
}