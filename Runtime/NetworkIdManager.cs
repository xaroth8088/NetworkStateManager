using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NSM
{
    public class NetworkIdManager
    {
        private GameObject[] networkIdGameObjectCache = new GameObject[256];
        private bool[] reservedNetworkIds = new bool[256];
        private NetworkStateManager _networkStateManager;

        public NetworkIdManager(NetworkStateManager networkStateManager)
        {
            _networkStateManager = networkStateManager;
            Reset();
        }

        public void Reset()
        {
            networkIdGameObjectCache = new GameObject[256];
            reservedNetworkIds = new bool[256];
        }

        public void RegisterGameObject(GameObject gameObject, byte networkId = 0)
        {
            if (networkId != 0)
            {
                if (reservedNetworkIds[networkId])
                {
                    ReleaseNetworkId(networkId);
                }
            }
            else
            {
                networkId = ReserveNetworkId();
            }

            _networkStateManager.VerboseLog("Registering network ID " + networkId + " to " + gameObject.name);

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
                if (reservedNetworkIds[i])
                {
                    continue;
                }

                _networkStateManager.VerboseLog("Reserved network ID " + i);
                reservedNetworkIds[i] = true;

                return i;
            }

            throw new Exception("Out of network ids!");
        }

        public void ReleaseNetworkId(byte networkId)
        {
            _networkStateManager.VerboseLog("Releasing network id " + networkId);

            if (!reservedNetworkIds[networkId])
            {
                Debug.LogWarning("Tried to release network ID " + networkId + ", but this is not a registered network ID.  This may happen during rollback scenarios.");
                return;
            }

            reservedNetworkIds[networkId] = false;

            if (networkIdGameObjectCache[networkId] != null)
            {
                _networkStateManager.VerboseLog("A game object was found with this network ID, so resetting its network ID to 0");

                if (networkIdGameObjectCache[networkId].TryGetComponent<NetworkId>(out NetworkId networkIdComponent))
                {
                    networkIdComponent.networkId = 0;
                }

                networkIdGameObjectCache[networkId] = null;
            }
        }

        public IEnumerable<GameObject> GetAllNetworkIdGameObjects()
        {
            return networkIdGameObjectCache.Where(value => value != null);
        }

        public GameObject GetGameObjectByNetworkId(byte networkId)
        {
            return networkIdGameObjectCache[networkId];
        }
    }
}
