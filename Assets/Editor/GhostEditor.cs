#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Ghost))]
[CanEditMultipleObjects]
public class GhostEditor : Editor
{
    static readonly string[] ElroyProps = { "elroy1Multiplier", "elroy2Multiplier", "elroyStage" };

    SerializedProperty ghostTypeProp;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Read-only script field
        using (new EditorGUI.DisabledScope(true))
        {
            var mb = (MonoBehaviour)target;
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(mb), typeof(MonoScript), false);
        }

        // Draw Ghost Type first (so the rest can react to it)
        ghostTypeProp = serializedObject.FindProperty("ghostType");
        bool mixedTypes = ghostTypeProp != null && ghostTypeProp.hasMultipleDifferentValues;

        if (ghostTypeProp != null)
        {
            EditorGUILayout.PropertyField(ghostTypeProp, new GUIContent("Ghost Type"));
        }

        // Multi-selection aware: show conditional fields only if ALL selected match
        bool allBlinky = AllTargetsAre(GhostType.Blinky);
        bool allInky   = AllTargetsAre(GhostType.Inky);

        // Iterate all serialized properties; skip those we draw specially
        SerializedProperty p = serializedObject.GetIterator();
        bool enterChildren = true;
        while (p.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (p.name == "m_Script" || p.name == "ghostType") continue;

            bool isElroyProp   = System.Array.IndexOf(ElroyProps, p.name) >= 0; // Blinky-only
            bool isBlinkyLink  = p.name == "blinky";                             // Inky-only

            if (isElroyProp && !allBlinky) continue;     // hide Elroy unless ALL are Blinky
            if (isBlinkyLink && !allInky) continue;      // hide Blinky link unless ALL are Inky

            EditorGUILayout.PropertyField(p, includeChildren: true);
        }

        if (mixedTypes)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Conditional fields are hidden because selection contains different Ghost Types.", MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    bool AllTargetsAre(GhostType want)
    {
        foreach (var o in targets)
        {
            var g = o as Ghost;
            if (g == null || g.Type != want) return false;
        }
        return true;
    }
}
#endif