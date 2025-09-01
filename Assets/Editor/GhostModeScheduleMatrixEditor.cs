using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GhostModeScheduleMatrix))]
public class GhostModeScheduleMatrixEditor : Editor
{
    // Column widths
    const float W_LVL = 52f, W_FR = 54f, W_PC = 34f, W_ND = 58f;

    // Labels + tooltips
    static readonly GUIContent LC_TITLE = new GUIContent("Ghost Mode Schedule Matrix");
    static readonly GUIContent LC_HELP = new GUIContent("Show Help");
    static readonly GUIContent LC_FROM = new GUIContent("From", "Level this row starts at (inclusive). The row with the greatest 'From' ≤ current level is used.");
    static readonly GUIContent LC_PHASES = new GUIContent("Phases","Ordered list of phases for this level band.\nExample: Scatter(7), Chase(20), Scatter(7), Chase(20), Scatter(5), Chase(0)\nTip: Use duration 0 for a final infinite CHASE.");
    static readonly GUIContent LC_FRGT = new GUIContent("Frgt", "Frightened duration (seconds) for this band.");
    static readonly GUIContent LC_P = new GUIContent("P", "Pinky personal dot limit. 0 = leaves immediately.");
    static readonly GUIContent LC_I = new GUIContent("I", "Inky personal dot limit. (Arcade: 30 on L1, 0 on L2+)");
    static readonly GUIContent LC_C = new GUIContent("C", "Clyde personal dot limit. (Arcade: 60 on L1, 50 on L2, 0 on L3+)");
    static readonly GUIContent LC_NODOT = new GUIContent("NoDot", "If Pac-Man eats NO dots for this many seconds, the preferred in-pen ghost is forced to leave.\nArcade: 4s on L1–4, 3s on L5+.");

    bool showHelp;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var rowsProp = serializedObject.FindProperty("rows");
        if (rowsProp == null) { DrawDefaultInspector(); return; }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(LC_TITLE, EditorStyles.boldLabel);

        // Help panel toggle
        showHelp = EditorGUILayout.Foldout(showHelp, LC_HELP, true);
        if (showHelp)
        {
            EditorGUILayout.HelpBox(
                "HOW IT WORKS\n" +
                "• Rows define per-level-band behavior. The game selects the row with the greatest 'From' ≤ current level.\n" +
                "• Phases run IN ORDER. Use multiple Scatter/Chase entries (e.g., Scatter1, Chase1, Scatter2...). " +
                "Set the LAST Chase duration to 0 for an infinite chase to level end.\n" +
                "• Frgt is frightened (blue) duration in seconds for this band.\n" +
                "• House Exit: personal dot limits + NoDot timer control leaving the pen:\n" +
                "    – Only one personal counter is active at a time (preference: Pinky → Inky → Clyde).\n" +
                "    – When that counter reaches its limit, that ghost leaves; then the next one becomes preferred.\n" +
                "    – If Pac-Man stalls (eats no dots) for NoDot seconds, the preferred in-pen ghost is forced to leave.\n" +
                "Tip: Typical arcade values → L1: P=0, I=30, C=60, NoDot=4 • L2: P=0, I=0, C=50, NoDot=4 • L3–4: P=0, I=0, C=0, NoDot=4 • L5+: P=0, I=0, C=0, NoDot=3.",
                MessageType.Info
            );
        }

        EditorGUILayout.Space(4);

        // Header row
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(LC_FROM, GUILayout.Width(W_LVL));
            GUILayout.Label(LC_PHASES, GUILayout.ExpandWidth(true));
            GUILayout.Label(LC_FRGT, GUILayout.Width(W_FR));
            GUILayout.Label(LC_P, GUILayout.Width(W_PC));
            GUILayout.Label(LC_I, GUILayout.Width(W_PC));
            GUILayout.Label(LC_C, GUILayout.Width(W_PC));
            GUILayout.Label(LC_NODOT,  GUILayout.Width(W_ND));
        }

        EditorGUILayout.Space(2);

        // Each data row
        for (int i = 0; i < rowsProp.arraySize; i++)
        {
            var row = rowsProp.GetArrayElementAtIndex(i);
            var fromLevel = row.FindPropertyRelative("fromLevel");
            var phases = row.FindPropertyRelative("phases");
            var frightened = row.FindPropertyRelative("frightened");
            var pinkyDot = row.FindPropertyRelative("pinkyDotLimit");
            var inkyDot = row.FindPropertyRelative("inkyDotLimit");
            var clydeDot = row.FindPropertyRelative("clydeDotLimit");
            var noDotRel = row.FindPropertyRelative("noDotRelease");

            using (new EditorGUILayout.HorizontalScope())
            {
                fromLevel.intValue = EditorGUILayout.IntField(fromLevel.intValue, GUILayout.Width(W_LVL));

                // Phases as a labeled array (tooltip on label)
                EditorGUILayout.PropertyField(phases, LC_PHASES, true, GUILayout.ExpandWidth(true));

                frightened.floatValue = EditorGUILayout.FloatField(frightened.floatValue, GUILayout.Width(W_FR));
                pinkyDot.intValue = EditorGUILayout.IntField(pinkyDot.intValue,       GUILayout.Width(W_PC));
                inkyDot.intValue = EditorGUILayout.IntField(inkyDot.intValue,        GUILayout.Width(W_PC));
                clydeDot.intValue = EditorGUILayout.IntField(clydeDot.intValue,       GUILayout.Width(W_PC));
                noDotRel.floatValue = EditorGUILayout.FloatField(noDotRel.floatValue,   GUILayout.Width(W_ND));
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
        }

        // Add/Remove controls
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Add Row", "Add a new band (levels ≥ 'From').")))
                rowsProp.InsertArrayElementAtIndex(rowsProp.arraySize);

            using (new EditorGUI.DisabledScope(rowsProp.arraySize == 0))
            {
                if (GUILayout.Button(new GUIContent("Remove Last", "Remove the last row.")))
                    rowsProp.DeleteArrayElementAtIndex(rowsProp.arraySize - 1);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}