#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostChase)), CanEditMultipleObjects]
public class GhostChaseEditor : Editor
{
    SerializedProperty clydeCornerProp;

    void OnEnable()
    {
        clydeCornerProp = serializedObject.FindProperty("clydeScatterCorner");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Determine selection makeup
        bool anyClyde = false;
        bool allClyde = true;

        foreach (var t in targets)
        {
            var comp = t as GhostChase;
            if (!comp) continue;
            var g = comp.GetComponent<Ghost>();
            bool isClyde = g && g.Type == GhostType.Clyde;
            anyClyde |= isClyde;
            allClyde &= isClyde;
        }

        if (anyClyde)
        {
            // Show the field (caption varies if you selected mixed types)
            var label = allClyde
                ? new GUIContent("Clyde Scatter Corner")
                : new GUIContent("Clyde Scatter Corner (only applies to Clyde)");
            EditorGUILayout.PropertyField(clydeCornerProp, label);

            if (!allClyde)
            {
                EditorGUILayout.HelpBox(
                    "This property is ignored for non-Clyde ghosts in the current selection.",
                    MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No Clyde ghosts selected — nothing to configure here.",
                MessageType.None);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif