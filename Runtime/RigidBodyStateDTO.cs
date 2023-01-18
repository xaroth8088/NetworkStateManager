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

        // TODO: we probably need to track the body's active/inactive state
        public RigidBodyStateDTO(Rigidbody _rigidbody)
        {
            position = _rigidbody.position;
            rotation = _rigidbody.rotation;
            velocity = _rigidbody.velocity;
            angularVelocity = _rigidbody.angularVelocity;

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
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
            serializer.SerializeValue(ref networkId);
        }

        public void ApplyState(GameObject gameObject)
        {
            Rigidbody rigidbody = gameObject.GetComponentInChildren<Rigidbody>();

            // Find the associated rigidbody (if any)
            if (gameObject.activeInHierarchy == false)
            {
                // This object no longer exists in the scene
                Debug.Log("Attempted to restore state to a GameObject that no longer exists");
                // TODO: this seems like it'll lead to some bugs later with objects that disappeared recently
                return;
            }

            // Apply the state
            gameObject.transform.SetPositionAndRotation(position, rotation);
            try
            {
                rigidbody.position = position;
                rigidbody.rotation = rotation;

                if (rigidbody.isKinematic == false)
                {
                    rigidbody.velocity = velocity;
                    rigidbody.angularVelocity = angularVelocity;
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
                angularVelocity.Equals(other.angularVelocity)
            );
        }
    }
}