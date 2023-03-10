using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    public class NetworkIdManager
    {
        private readonly Dictionary<byte, GameObject> networkIdGameObjectCache = new();
        private readonly Dictionary<byte, bool> reservedNetworkIds = new();

        public NetworkIdManager()
        {
            Reset();
        }

        public void Reset()
        {
            networkIdGameObjectCache.Clear();
            reservedNetworkIds.Clear();
        }

        public void RegisterGameObject(GameObject gameObject, byte networkId = 0)
        {
            if (networkId != 0)
            {
                reservedNetworkIds.TryGetValue(networkId, out bool isReserved);
                if (isReserved)
                {
                    ReleaseNetworkId(networkId);
                }
            }
            else
            {
                networkId = ReserveNetworkId();
            }

            VerboseLog("Registering network ID " + networkId + " to " + gameObject.name);

            if (!gameObject.TryGetComponent<NetworkId>(out NetworkId networkIdComponent))
            {
                networkIdComponent = gameObject.AddComponent<NetworkId>();
            }

            networkIdComponent.networkId = networkId;
            reservedNetworkIds[networkId] = true;
            networkIdGameObjectCache[networkId] = gameObject;
        }

        public byte ReserveNetworkId()
        {
            // Yes, this means that 0 can't be used, but that's ok - we need it as a flag to mean "hasn't been assigned one yet"
            for (byte i = 1; i < 255; i++)
            {
                reservedNetworkIds.TryGetValue(i, out bool isReserved);
                if (isReserved)
                {
                    continue;
                }

                VerboseLog("Reserved network ID " + i);
                reservedNetworkIds[i] = true;

                return i;
            }

            throw new Exception("Out of network ids!");
        }

        public void ReleaseNetworkId(byte networkId)
        {
            VerboseLog("Releasing network id " + networkId);

            reservedNetworkIds.TryGetValue(networkId, out bool isReserved);
            if (!isReserved)
            {
                throw new Exception("Tried to release network ID " + networkId + ", but this is not a registered network ID");
            }

            reservedNetworkIds.Remove(networkId);

            if (networkIdGameObjectCache.TryGetValue(networkId, out GameObject gameObject))
            {
                VerboseLog("A game object was found with this network ID, so resetting its network ID to 0");

                if (gameObject.TryGetComponent<NetworkId>(out NetworkId networkIdComponent))
                {
                    networkIdComponent.networkId = 0;
                }

                networkIdGameObjectCache.Remove(networkId);
            }
        }

        public Dictionary<byte, GameObject>.ValueCollection GetAllNetworkIdGameObjects()
        {
            return networkIdGameObjectCache.Values;
        }

        public GameObject GetGameObjectByNetworkId(byte networkId)
        {
            networkIdGameObjectCache.TryGetValue(networkId, out GameObject gameObject);

            return gameObject;
        }

        private void VerboseLog(string message)
        {
#if UNITY_EDITOR
            Debug.Log(message);
#endif
        }
    }
}
