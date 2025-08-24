#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostEyes))]
[CanEditMultipleObjects]
public class GhostEyesEditor : Editor
{
    // Names of the Elroy-only fields in your GhostEyes
    static readonly string[] ElroyProps = {
        "elroyUp","elroyDown","elroyLeft","elroyRight"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Show the script field (read-only)
        using (new EditorGUI.DisabledScope(true))
        {
            var mb = (MonoBehaviour)target;
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(mb), typeof(MonoScript), false);
        }

        bool anyMissingGhost, mixedTypes;
        bool allBlinky = AreAllTargetsBlinky(out anyMissingGhost, out mixedTypes);

        // Iterate real properties; never draw a null property
        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script") continue; // already drawn above

            // Hide Elroy fields unless ALL selected ghosts are Blinky
            if (!allBlinky && System.Array.IndexOf(ElroyProps, prop.name) >= 0)
                continue;

            EditorGUILayout.PropertyField(prop, includeChildren: true);
        }

        if (targets.Length > 1 && mixedTypes)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Elroy eyes are editable only when ALL selected Ghosts are Blinky.", MessageType.Info);
        }

        if (anyMissingGhost)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Some selected GhostEyes have no Ghost component on them or a parent.", MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();
    }

    bool AreAllTargetsBlinky(out bool anyMissingGhost, out bool mixedTypes)
    {
        anyMissingGhost = false;
        mixedTypes = false;

        GhostType? firstType = null;

        foreach (var o in targets)
        {
            var eyes = o as GhostEyes;
            if (eyes == null) continue;

            // Ghost can be on the same object or on a parent
            var ghost = eyes.GetComponent<Ghost>() ?? eyes.GetComponentInParent<Ghost>();
            if (ghost == null)
            {
                anyMissingGhost = true;
                return false; // can't guarantee Blinky without a Ghost reference
            }

            var t = ghost.Type;

            if (firstType == null) firstType = t;
            else if (firstType.Value != t) mixedTypes = true;

            if (t != GhostType.Blinky)
                return false; // as soon as one isn't Blinky, hide Elroy fields
        }

        return true; // all are Blinky
    }
}
#endif