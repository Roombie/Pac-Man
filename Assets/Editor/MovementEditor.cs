#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Movement)), CanEditMultipleObjects]
public class MovementEditor : Editor
{
    SerializedProperty speed;
    SerializedProperty initialDirection;
    SerializedProperty obstacleLayer;

    SerializedProperty corneringEnabled;
    SerializedProperty cornerWindow;
    SerializedProperty snapEpsilon;

    void OnEnable()
    {
        speed  = serializedObject.FindProperty("speed");
        initialDirection = serializedObject.FindProperty("initialDirection");
        obstacleLayer  = serializedObject.FindProperty("obstacleLayer");

        corneringEnabled = serializedObject.FindProperty("corneringEnabled");
        cornerWindow = serializedObject.FindProperty("cornerWindow");
        snapEpsilon = serializedObject.FindProperty("snapEpsilon");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(speed);
        EditorGUILayout.PropertyField(initialDirection);
        EditorGUILayout.PropertyField(obstacleLayer);

        EditorGUILayout.Space(8f);
        EditorGUILayout.PropertyField(corneringEnabled, new GUIContent("Cornering Enabled"));

        if (corneringEnabled.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cornerWindow, new GUIContent("Corner Window"));
            EditorGUILayout.PropertyField(snapEpsilon, new GUIContent("Snap Epsilon"));
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif