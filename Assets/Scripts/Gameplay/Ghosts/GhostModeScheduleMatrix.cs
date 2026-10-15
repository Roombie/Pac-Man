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

        // Speeds as a fraction of full speed (Pac-Man Dossier table)
        [Header("Speeds (fraction of full speed)")]
        public float pacmanSpeed = 0.80f;
        [Tooltip("Pac-Man while ghosts are frightened.")]
        public float pacmanFrightSpeed = 0.90f;
        public float ghostSpeed = 0.75f;
        public float ghostFrightSpeed = 0.50f;
        public float ghostTunnelSpeed = 0.40f;
        public float elroy1Speed = 0.80f;
        public float elroy2Speed = 0.85f;
    }

    public List<Row> rows = new();

    [Tooltip("World units per second that equal 100% speed. Arcade 100% is ~9.47 tiles/s; " +
             "10 keeps Level 1 Pac-Man at 8 units/s.")]
    public float fullSpeed = 10f;

    [Tooltip("Dots left when Blinky becomes Elroy 1, per level (index 0 = level 1). The last value repeats. " +
             "Elroy 2 starts at half of this.")]
    public int[] elroyDotsByLevel = Array.Empty<int>();

    [Tooltip("Frightened seconds per level (index 0 = level 1). The last value repeats for higher levels. " +
             "0 = ghosts only reverse, they don't turn blue. Overrides the row's 'frightened' when not empty.")]
    public float[] frightenedByLevel = Array.Empty<float>();

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
        frightenedSeconds = GetFrightenedSecondsForLevel(level, row);
        return row.phases ?? Array.Empty<GhostPhase>();
    }

    private float GetFrightenedSecondsForLevel(int level, Row row)
    {
        if (frightenedByLevel == null || frightenedByLevel.Length == 0) return row.frightened;

        int i = Mathf.Clamp(level - 1, 0, frightenedByLevel.Length - 1);
        return Mathf.Max(0f, frightenedByLevel[i]);
    }

    public string GetBandLabelForLevel(int level)
    {
        var row = SelectRow(level);
        return $"L{row.fromLevel}+";
    }

    /// <summary>Movement speeds for a level, in world units per second</summary>
    public struct LevelSpeeds
    {
        public float pacman, pacmanFright;
        public float ghost, ghostFright, ghostTunnel;
        public float elroy1, elroy2;
    }

    public LevelSpeeds GetSpeedsForLevel(int level)
    {
        var row = SelectRow(level);
        return new LevelSpeeds
        {
            pacman = fullSpeed * row.pacmanSpeed,
            pacmanFright = fullSpeed * row.pacmanFrightSpeed,
            ghost = fullSpeed * row.ghostSpeed,
            ghostFright = fullSpeed * row.ghostFrightSpeed,
            ghostTunnel = fullSpeed * row.ghostTunnelSpeed,
            elroy1 = fullSpeed * row.elroy1Speed,
            elroy2 = fullSpeed * row.elroy2Speed,
        };
    }

    public int GetElroyDotsForLevel(int level)
    {
        if (elroyDotsByLevel == null || elroyDotsByLevel.Length == 0) return 20;

        int i = Mathf.Clamp(level - 1, 0, elroyDotsByLevel.Length - 1);
        return Mathf.Max(0, elroyDotsByLevel[i]);
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