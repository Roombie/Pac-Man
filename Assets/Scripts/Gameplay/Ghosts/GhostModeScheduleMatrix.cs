using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Pacman/Ghost Mode Schedule Matrix")]
public class GhostModeScheduleMatrix : ScriptableObject
{
    [Serializable]
    public class Row
    {
        [Tooltip("This row applies starting at this level (inclusive).")]
        public int fromLevel = 1;

        [Tooltip("Scatter/Chase phases for this band.")]
        public GhostPhase[] phases;

        [Tooltip("Frightened duration (seconds) for this band.")]
        public float frightened = 6f;

        // House exit (dot counters & stall timer)
        [Header("House Exit")]
        [Tooltip("Pinky dot limit (usually 0 on all bands).")]
        public int pinkyDotLimit = 0;

        [Tooltip("Inky dot limit (e.g., 30 on L1, 0 on L2+).")]
        public int inkyDotLimit = 0;

        [Tooltip("Clyde dot limit (e.g., 60 on L1, 50 on L2, 0 on L3+).")]
        public int clydeDotLimit = 0;

        [Tooltip("No-dot release seconds for this band (4s on L1–4, 3s on L5+).")]
        public float noDotRelease = 4f;
    }

    public List<Row> rows = new();

    // Select row with greatest fromLevel <= level
    private Row SelectRow(int level)
    {
        if (rows == null || rows.Count == 0)
            return new Row { fromLevel = 1, phases = Array.Empty<GhostPhase>(), frightened = 6f };

        Row sel = rows[0];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (level >= r.fromLevel) sel = r;
        }
        return sel;
    }

    public GhostPhase[] GetPhasesForLevel(int level, out float frightenedSeconds)
    {
        var row = SelectRow(level);
        frightenedSeconds = row.frightened;
        return row.phases ?? Array.Empty<GhostPhase>();
    }

    public string GetBandLabelForLevel(int level)
    {
        var row = SelectRow(level);
        return $"L{row.fromLevel}+";
    }

    // house-exit params per level band
    [Serializable]
    public struct HouseExitParams
    {
        public int pinky, inky, clyde;
        public float noDotSeconds;
    }

    public HouseExitParams GetHouseExitForLevel(int level)
    {
        var row = SelectRow(level);
        return new HouseExitParams
        {
            pinky = row.pinkyDotLimit,
            inky = row.inkyDotLimit,
            clyde = row.clydeDotLimit,
            noDotSeconds = row.noDotRelease
        };
    }
}