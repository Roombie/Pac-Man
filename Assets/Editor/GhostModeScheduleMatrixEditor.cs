#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

[CustomEditor(typeof(GhostModeScheduleMatrix))]
public class GhostModeScheduleMatrixEditor : Editor
{
    const float W_LABEL = 120, W_LEVEL = 48, W_CELL = 40, W_FRGT = 45;
    static bool showLegend;

    // Column headers with tooltips
    static readonly GUIContent H_LEVELS   = new GUIContent("Levels",
        "Auto label derived from Level: [Level … next-1]. The last row is Level+.");
    static readonly GUIContent H_LEVELCOL = new GUIContent("Level",
        "First level this row applies to (inclusive). Next row’s Level − 1 is the end of this band.");

    static readonly GUIContent H_S1   = new GUIContent("S1", "Scatter phase 1 (seconds). ≤ 0 = skip.");
    static readonly GUIContent H_C1   = new GUIContent("C1", "Chase phase 1 (seconds). ≤ 0 = infinite (final Chase).");
    static readonly GUIContent H_S2   = new GUIContent("S2", "Scatter phase 2 (seconds). ≤ 0 = skip.");
    static readonly GUIContent H_C2   = new GUIContent("C2", "Chase phase 2 (seconds). ≤ 0 = infinite (final Chase).");
    static readonly GUIContent H_S3   = new GUIContent("S3", "Scatter phase 3 (seconds). ≤ 0 = skip.");
    static readonly GUIContent H_C3   = new GUIContent("C3", "Chase phase 3 (seconds). ≤ 0 = infinite (final Chase).");
    static readonly GUIContent H_S4   = new GUIContent("S4", "Scatter phase 4 (seconds). ≤ 0 = skip.");
    static readonly GUIContent H_C4   = new GUIContent("C4", "Chase phase 4 (seconds). ≤ 0 = infinite (final Chase).");
    static readonly GUIContent H_FRGT = new GUIContent("Frgt",
        "Frightened duration (seconds) when a power pellet is eaten. The Scatter/Chase schedule keeps running underneath.");

    public override void OnInspectorGUI()
    {
        var so = (GhostModeScheduleMatrix)target;
        if (so.rows == null) so.rows = new List<GhostModeScheduleMatrix.Row>();

        // Legend
        EditorGUILayout.HelpBox(
            "• Levels: Auto label from the Level value (e.g., “Level 1”, “Levels 2–4”, “Level 5+”).\n" +
            "• Level: First level (inclusive) this row applies to. Next row’s Level − 1 is the last level in the band.\n" +
            "• S# (Scatter): Duration of each Scatter phase (seconds). ≤ 0 means the phase is skipped.\n" +
            "• C# (Chase): Duration of each Chase phase (seconds). ≤ 0 means the Chase is infinite (final phase).\n" +
            "• Frgt: Frightened (blue) time (seconds). Schedule continues underneath; no reversal when Frightened ends.",
            MessageType.Info);

        // Utilities
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Sort by Level", GUILayout.Height(22)))
            {
                so.rows.Sort((a, b) => a.fromLevel.CompareTo(b.fromLevel));
                GUI.changed = true;
            }
            if (GUILayout.Button("Make Level Unique", GUILayout.Height(22)))
            {
                MakeUnique(so.rows);
                GUI.changed = true;
            }
            if (GUILayout.Button("Renumber 1..", GUILayout.Height(22)))
            {
                RenumberSequential(so.rows, 1);
                GUI.changed = true;
            }
        }

        // Warn about duplicates
        var dupes = so.rows.GroupBy(r => r.fromLevel).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"Duplicate Level values: {string.Join(", ", dupes)}. Labels may be ambiguous.",
                MessageType.Warning);
        }

        // Always render sorted
        so.rows.Sort((a, b) => a.fromLevel.CompareTo(b.fromLevel));

        // Header
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(H_LEVELS,   GUILayout.Width(W_LABEL));
            GUILayout.Label(H_LEVELCOL, GUILayout.Width(W_LEVEL));
            GUILayout.Label(H_S1,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_C1,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_S2,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_C2,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_S3,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_C3,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_S4,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_C4,       GUILayout.Width(W_CELL));
            GUILayout.Label(H_FRGT,     GUILayout.Width(W_FRGT));
        }

        // Rows
        for (int i = 0; i < so.rows.Count; i++)
        {
            var r = so.rows[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(AutoLabelFor(so, i), GUILayout.Width(W_LABEL));
                r.fromLevel  = EditorGUILayout.IntField(r.fromLevel, GUILayout.Width(W_LEVEL));
                r.s1         = EditorGUILayout.FloatField(r.s1,   GUILayout.Width(W_CELL));
                r.c1         = EditorGUILayout.FloatField(r.c1,   GUILayout.Width(W_CELL));
                r.s2         = EditorGUILayout.FloatField(r.s2,   GUILayout.Width(W_CELL));
                r.c2         = EditorGUILayout.FloatField(r.c2,   GUILayout.Width(W_CELL));
                r.s3         = EditorGUILayout.FloatField(r.s3,   GUILayout.Width(W_CELL));
                r.c3         = EditorGUILayout.FloatField(r.c3,   GUILayout.Width(W_CELL));
                r.s4         = EditorGUILayout.FloatField(r.s4,   GUILayout.Width(W_CELL));
                r.c4         = EditorGUILayout.FloatField(r.c4,   GUILayout.Width(W_CELL));
                r.frightened = EditorGUILayout.FloatField(r.frightened, GUILayout.Width(W_FRGT));

                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    so.rows.RemoveAt(i);
                    GUI.changed = true;
                    break;
                }
            }
        }

        if (GUILayout.Button("+ Add Row", GUILayout.Height(22)))
        {
            so.rows.Add(new GhostModeScheduleMatrix.Row { fromLevel = NextLevel(so.rows) });
            GUI.changed = true;
        }

        if (GUI.changed) EditorUtility.SetDirty(so);
    }

    private static int NextLevel(List<GhostModeScheduleMatrix.Row> rows) =>
        rows.Count == 0 ? 1 : Mathf.Max(1, rows.Max(r => r.fromLevel) + 1);

    private static string AutoLabelFor(GhostModeScheduleMatrix m, int i)
    {
        int start = Mathf.Max(1, m.rows[i].fromLevel);
        int end   = (i + 1 < m.rows.Count) ? Mathf.Max(start, m.rows[i + 1].fromLevel - 1) : int.MaxValue;
        return end == int.MaxValue ? $"Level {start}+" :
               (start == end ? $"Level {start}" : $"Levels {start}–{end}");
    }

    private static void MakeUnique(List<GhostModeScheduleMatrix.Row> rows)
    {
        rows.Sort((a, b) => a.fromLevel.CompareTo(b.fromLevel));
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].fromLevel <= rows[i - 1].fromLevel)
                rows[i].fromLevel = rows[i - 1].fromLevel + 1;
    }

    private static void RenumberSequential(List<GhostModeScheduleMatrix.Row> rows, int startAt)
    {
        rows.Sort((a, b) => a.fromLevel.CompareTo(b.fromLevel));
        int cur = Mathf.Max(1, startAt);
        for (int i = 0; i < rows.Count; i++)
            rows[i].fromLevel = cur++;
    }
}
#endif