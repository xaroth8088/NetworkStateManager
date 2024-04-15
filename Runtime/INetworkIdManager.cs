using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NSM
{
    public interface INetworkIdManager
    {
        IEnumerable<GameObject> GetAllNetworkIdGameObjects();
        GameObject GetGameObjectByNetworkId(byte networkId);
        void RegisterGameObject(GameObject gameObject, byte networkId = 0);
        void ReleaseNetworkId(byte networkId);
        byte ReserveNetworkId();
        void Reset();
        void SetupInitialNetworkIds(Scene scene);
    }
}