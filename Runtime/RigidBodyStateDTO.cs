using MemoryPack;
using UnityEngine;

namespace NSM
{
    [MemoryPackable]
    public partial struct RigidBodyStateDTO
    {
        public Vector3 angularVelocity;
        public bool isSleeping;
        public byte networkId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;

        // TODO: we probably need to track the body's active/inactive state (beyond just sleeping)
        public RigidBodyStateDTO(Rigidbody rigidbody)
        {
            position = rigidbody.gameObject.transform.position;
            rotation = rigidbody.gameObject.transform.rotation;
            velocity = rigidbody.velocity;
            angularVelocity = rigidbody.angularVelocity;
            isSleeping = rigidbody.IsSleeping();

            try
            {
                networkId = rigidbody.gameObject.GetComponent<NetworkId>().networkId;
            }
            catch
            {
                Debug.LogError("Found a rigidbody that doesn't have a NetworkId: " + rigidbody.gameObject);
                networkId = 0;
            }
        }

        public void ApplyState(Rigidbody rigidbody)
        {
            if (isSleeping)
            {
                rigidbody.Sleep();
            }
            else
            {
                rigidbody.WakeUp();
            }

            rigidbody.gameObject.transform.SetPositionAndRotation(position, rotation);

            if (rigidbody.isKinematic == false)
            {
                rigidbody.velocity = velocity;
                rigidbody.angularVelocity = angularVelocity;
            }
        }
    }
}