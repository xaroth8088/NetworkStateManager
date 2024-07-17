using System;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class BaseSerializePropertyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        // Get the target object
        MonoBehaviour targetObject = (MonoBehaviour)target;

        // Get all properties of the target object, including inherited properties
        PropertyInfo[] properties = targetObject.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        foreach (PropertyInfo property in properties)
        {
            // Check if the property is decorated with the SerializeProperty attribute
            if (property.IsDefined(typeof(SerializePropertyAttribute), false))
            {
                // Get the property value
                object value = property.GetValue(targetObject);

                // Draw the property label and value
                EditorGUILayout.LabelField(property.Name, value != null ? value.ToString() : "null");
            }
        }

        bool isPlaying = EditorApplication.isPlaying;
        bool prefabAssetTypeIsNotAPrefab = PrefabUtility.GetPrefabAssetType(targetObject.gameObject) == PrefabAssetType.NotAPrefab;
        bool inLiveScene = targetObject.gameObject.scene.isLoaded;
        bool prefabStageIsNull = (PrefabStageUtility.GetPrefabStage(targetObject.gameObject) == null);

        if (
            isPlaying &&
            prefabAssetTypeIsNotAPrefab &&
            prefabStageIsNull &&
            inLiveScene
        )
        {
            EditorUtility.SetDirty(target);
        }
    }
}

[CustomEditor(typeof(MonoBehaviour), true)]
[CanEditMultipleObjects]
public class MonoBehaviourSerializePropertyEditor : BaseSerializePropertyEditor
{
}

[CustomEditor(typeof(NetworkBehaviour), true)]
[CanEditMultipleObjects]
public class NetworkBehaviourSerializePropertyEditor : BaseSerializePropertyEditor
{
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class SerializePropertyAttribute : PropertyAttribute
{
    public SerializePropertyAttribute()
    { }
}