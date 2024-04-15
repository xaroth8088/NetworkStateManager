using UnityEngine;
using System.Collections.Generic;

namespace NSM
{
    public static class PhysicsManager
    {
        public static void InitPhysics()
        {
            // In order for NSM to work, we'll need to fully control physics (Muahahaha)
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;
        }

        public static void SimulatePhysics(float deltaTime)
        {
            Physics.Simulate(deltaTime);
        }

        public static void ApplyPhysicsState(PhysicsStateDTO physicsState, NetworkIdManager networkIdManager)
        {
            // TODO: VerboseLog("Applying physics state");

            // Set each object into the world
            foreach ((byte networkId, RigidBodyStateDTO rigidBodyState) in physicsState.RigidBodyStates)
            {
                GameObject networkedGameObject = networkIdManager.GetGameObjectByNetworkId(networkId);
                if (networkedGameObject == null || networkedGameObject.activeInHierarchy == false)
                {
                    // This object no longer exists in the scene
                    Debug.LogError("Attempted to restore state to a GameObject that no longer exists");
                    // TODO: this seems like it'll lead to some bugs later with objects that disappeared recently
                    continue;
                }

                rigidBodyState.ApplyState(networkedGameObject.GetComponentInChildren<Rigidbody>());
            }
        }

        public static List<Rigidbody> GetNetworkedRigidbodies(NetworkIdManager networkIdManager)
        {
            List<Rigidbody> rigidbodies = new();
            foreach (GameObject gameObject in networkIdManager.GetAllNetworkIdGameObjects())
            {
                if (gameObject.TryGetComponent(out Rigidbody rigidbody))
                {
                    rigidbodies.Add(rigidbody);
                }
            }
            return rigidbodies;
        }

        public static void SyncTransforms()
        {
            Physics.SyncTransforms();
        }
    }
}