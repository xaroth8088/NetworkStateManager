using System;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public struct RigidBodyStateDTO : INetworkSerializable, IEquatable<RigidBodyStateDTO>
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public byte networkId;
        public bool isSleeping;

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

        public void ApplyState(GameObject gameObject)
        {
            // Find the associated rigidbody (if any)
            if (gameObject == null || gameObject.activeInHierarchy == false)
            {
                // This object no longer exists in the scene
                Debug.LogError("Attempted to restore state to a GameObject that no longer exists");
                // TODO: this seems like it'll lead to some bugs later with objects that disappeared recently
                return;
            }

            Rigidbody rigidbody = gameObject.GetComponentInChildren<Rigidbody>();

            // Apply the state
            gameObject.transform.SetPositionAndRotation(position, rotation);
            try
            {
                rigidbody.transform.position = position;
                rigidbody.transform.rotation = rotation;
                
                if (rigidbody.isKinematic == false)
                {
                    rigidbody.velocity = velocity;
                    rigidbody.angularVelocity = angularVelocity;
                }

                if (isSleeping)
                {
                    rigidbody.Sleep();
                } else
                {
                    rigidbody.WakeUp();
                }
            }
            catch
            {
                Debug.LogError("Tried to apply rigidbody info to gameobject, and failed");
                Debug.LogError(gameObject);
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
    }
}