using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace NSM
{
    [ExecuteInEditMode]
    public class NetworkId : MonoBehaviour
    {
        public byte networkId = 0;

        [SerializeField]
        private string uniqueID;

        public string GUID
        {
            get { return uniqueID; }
        }

#if UNITY_EDITOR
        private void Awake()
        {
            if (Application.isPlaying)
            {
                return;
            }

            // This ensures that a new ID is generated if it's empty or missing
            if (string.IsNullOrEmpty(uniqueID))
            {
                GenerateNewID();
            }
        }

        private void GenerateNewID()
        {
            Undo.RecordObject(this, "Update GUID");

            uniqueID = Guid.NewGuid().ToString();

            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        private async Awaitable DeferredSet()
        {
            await Awaitable.MainThreadAsync();
            await Awaitable.NextFrameAsync();
            GenerateNewID();
        }

        // Ensure ID uniqueness when the component is validated in the editor
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(uniqueID))
            {
                _ = DeferredSet();
            }

            NetworkId[] uniqueIDComponents = FindObjectsByType<NetworkId>(FindObjectsSortMode.None);
            foreach (NetworkId idComponent in uniqueIDComponents)
            {
                if (idComponent != this && idComponent.uniqueID == uniqueID)
                {
                    _ = DeferredSet();
                    Debug.LogWarning("Duplicate UniqueID found, generating a new one.");
                    break;
                }
            }
        }
#endif
    }
}
