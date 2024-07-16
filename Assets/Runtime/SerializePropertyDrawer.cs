using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;
using Unity.Netcode;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public class SerializePropertyAttribute : PropertyAttribute
{
    public SerializePropertyAttribute() { }
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

        EditorUtility.SetDirty(target);
    }
}