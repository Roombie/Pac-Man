#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostSlowZone))]
[CanEditMultipleObjects]
public class GhostSlowZoneEditor : Editor
{
    SerializedProperty slowMultiplier, affectAllGhosts, only, except;

    void OnEnable()
    {
        slowMultiplier = serializedObject.FindProperty("slowMultiplier");
        affectAllGhosts = serializedObject.FindProperty("affectAllGhosts");
        only = serializedObject.FindProperty("only");
        except = serializedObject.FindProperty("except");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(slowMultiplier, new GUIContent("Slow Multiplier"));
        EditorGUILayout.PropertyField(affectAllGhosts, new GUIContent("Affect All Ghosts"));

        if (!affectAllGhosts.boolValue)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(only, new GUIContent("Only"));
                EditorGUILayout.PropertyField(except, new GUIContent("Except"));
            }
        }

        DrawColliderHelper();
        serializedObject.ApplyModifiedProperties();
    }

    void DrawColliderHelper()
    {
        foreach (Object o in targets)
        {
            var zone = o as GhostSlowZone; if (!zone) continue;
            var col = zone.GetComponent<Collider2D>();
            if (!col)
            {
                EditorGUILayout.HelpBox("Requires a Collider2D on this GameObject.", MessageType.Error);
                if (GUILayout.Button("Add BoxCollider2D")) Undo.AddComponent<BoxCollider2D>(zone.gameObject);
                continue;
            }
            if (!col.isTrigger)
            {
                EditorGUILayout.HelpBox("Collider2D should be set to Is Trigger.", MessageType.Warning);
                if (GUILayout.Button("Fix: Set IsTrigger = true"))
                {
                    Undo.RecordObject(col, "Set IsTrigger");
                    col.isTrigger = true;
                    EditorUtility.SetDirty(col);
                }
            }
        }
    }
}
#endif