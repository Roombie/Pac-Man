using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "GhostModeScheduleMatrix", menuName = "Pacman/Ghost Mode Schedule Matrix")]
public class GhostModeScheduleMatrix : ScriptableObject
{
    [Serializable]
    public class Row
    {
        [HideInInspector] public string name; // deprecated
        public int fromLevel = 1;                 // first level in this band (inclusive)

        // Phase durations
        public float s1 = 7f, c1 = 20f, s2 = 7f, c2 = 20f, s3 = 5f, c3 = 0f, s4 = 0f, c4 = 0f;

        // Frightened duration
        public float frightened = 6f;
        [TextArea(1, 3)] public string notes;
    }

    public List<Row> rows = new();

    // Build phases for a given 'currentLevel'
    public GhostPhase[] GetPhasesForLevel(int currentLevel, out float frightenedDuration)
    {
        frightenedDuration = 6f;
        if (rows == null || rows.Count == 0)
            return new[] { new GhostPhase { mode = Ghost.Mode.Chase, durationSeconds = 0f } };

        var sorted = rows.OrderBy(r => r.fromLevel).ToList();

        // pick last row whose 'level' <= currentLevel
        Row sel = sorted[0];
        foreach (var r in sorted) if (currentLevel >= r.fromLevel) sel = r;

        frightenedDuration = sel.frightened;

        var list = new List<GhostPhase>();
        void AddDur(float dur, Ghost.Mode m)
        {
            if (m == Ghost.Mode.Scatter) { if (dur > 0f) list.Add(new GhostPhase { mode = m, durationSeconds = dur }); return; }
            if (dur > 0f) list.Add(new GhostPhase { mode = m, durationSeconds = dur });
            else if (list.Count == 0 || (list[^1].mode == Ghost.Mode.Chase && list[^1].durationSeconds <= 0f)) { /* avoid dup ∞ */ }
            else list.Add(new GhostPhase { mode = Ghost.Mode.Chase, durationSeconds = 0f });
        }

        AddDur(sel.s1, Ghost.Mode.Scatter); AddDur(sel.c1, Ghost.Mode.Chase);
        AddDur(sel.s2, Ghost.Mode.Scatter); AddDur(sel.c2, Ghost.Mode.Chase);
        AddDur(sel.s3, Ghost.Mode.Scatter); AddDur(sel.c3, Ghost.Mode.Chase);
        AddDur(sel.s4, Ghost.Mode.Scatter); AddDur(sel.c4, Ghost.Mode.Chase);

        if (list.Count == 0)
            list.Add(new GhostPhase { mode = Ghost.Mode.Chase, durationSeconds = 0f });

        return list.ToArray();
    }
    
    public string GetBandLabelForLevel(int levelValue)
    {
        if (rows == null || rows.Count == 0) return "Default";

        var sorted = rows.OrderBy(r => r.fromLevel).ToList();

        // pick the last row whose 'level' <= levelValue
        int idx = 0;
        for (int i = 0; i < sorted.Count; i++)
            if (levelValue >= sorted[i].fromLevel) idx = i;

        int start = Mathf.Max(1, sorted[idx].fromLevel);
        int end = (idx + 1 < sorted.Count)
            ? Mathf.Max(start, sorted[idx + 1].fromLevel - 1)
            : int.MaxValue;

        return end == int.MaxValue
            ? $"Level {start}+"
            : (start == end ? $"Level {start}" : $"Levels {start}–{end}");
    }
}