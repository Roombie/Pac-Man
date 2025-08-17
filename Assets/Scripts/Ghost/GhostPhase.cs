using UnityEngine;

[System.Serializable]
public struct GhostPhase
{
    public Ghost.Mode mode;        // Scatter or Chase
    public float durationSeconds;  // <= 0 means "infinite" (for final Chase)
}