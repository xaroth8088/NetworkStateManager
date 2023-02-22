using System;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public struct RigidBodyStateDTO : INetworkSerializable, IEquatable<RigidBodyStateDTO>
    {
        public Vector3 angularVelocity;
        public bool isSleeping;
        public byte networkId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;

        // TODO: we probably need to track the body's active/inactive state (beyond just sleeping)
        public RigidBodyStateDTO(Rigidbody _rigidbody)
        {
            position = _rigidbody.transform.position;
            rotation = _rigidbody.transform.rotation;
            velocity = _rigidbody.velocity;
            angularVelocity = _rigidbody.angularVelocity;
            isSleeping = _rigidbody.IsSleeping();

            try
            {
                networkId = _rigidbody.gameObject.GetComponent<NetworkId>().networkId;
            }
            catch
            {
                Debug.LogError("Found a rigidbody that doesn't have a NetworkId: " + _rigidbody.gameObject);
                networkId = 0;
            }
        }

        public void ApplyState(Rigidbody rigidbody)
        {
            rigidbody.transform.SetPositionAndRotation(position, rotation);

            if (rigidbody.isKinematic == false)
            {
                rigidbody.velocity = velocity;
                rigidbody.angularVelocity = angularVelocity;
            }

            if (isSleeping)
            {
                rigidbody.Sleep();
            }
            else
            {
                rigidbody.WakeUp();
            }
        }

        public bool Equals(RigidBodyStateDTO other)
        {
            return (
                networkId == other.networkId &&
                position.Equals(other.position) &&
                rotation.Equals(other.rotation) &&
                velocity.Equals(other.velocity) &&
                angularVelocity.Equals(other.angularVelocity) &&
                isSleeping.Equals(other.isSleeping)
            );
        }

        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            // TODO: if the rigidbody is asleep, maybe we don't need to send any data at all about it beyond that?
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
            serializer.SerializeValue(ref networkId);
            serializer.SerializeValue(ref isSleeping);
        }
    }
}