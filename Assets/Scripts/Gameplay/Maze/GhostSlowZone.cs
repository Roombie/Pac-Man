using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class GhostSlowZone : MonoBehaviour
{
    [Range(0f, 2f)] public float slowMultiplier = 0.5f;

    [Header("Filtering")]
    public bool affectAllGhosts = true;
    public GhostType[] only;    // optional whitelist
    public GhostType[] except;  // blacklist

    void OnTriggerEnter2D(Collider2D other)
    {
        var ghost = other.GetComponent<Ghost>();
        if (!ghost) return;

        if (Affects(ghost.Type) &&
            ghost.CurrentMode != Ghost.Mode.Frightened &&
            ghost.CurrentMode != Ghost.Mode.Eaten)
        {
            ghost.movement?.SetEnvSpeedMultiplier(slowMultiplier);
        }
    }

    // If ghost mode change inside the slow zone like on Frightened or eat a ghost inside
    void OnTriggerStay2D(Collider2D other)
    {
        var ghost = other.GetComponent<Ghost>();
        if (!ghost) return;

        bool shouldAffect =
            Affects(ghost.Type) &&
            ghost.CurrentMode != Ghost.Mode.Frightened &&
            ghost.CurrentMode != Ghost.Mode.Eaten;

        ghost.movement?.SetEnvSpeedMultiplier(shouldAffect ? slowMultiplier : 1f);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        var ghost = other.GetComponent<Ghost>();
        if (!ghost) return;
        if (ghost && Affects(ghost.Type))
            ghost.movement?.SetEnvSpeedMultiplier(1f);
    }

    bool Affects(GhostType t)
    {
        if (affectAllGhosts) return true;

        // whitelist (if provided) AND not blacklisted
        bool whitelisted = (only == null || only.Length == 0) || System.Array.IndexOf(only, t) >= 0;
        bool blacklisted = (except != null && System.Array.IndexOf(except, t) >= 0);
        return whitelisted && !blacklisted;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var col = GetComponent<CircleCollider2D>();
        if (col) col.isTrigger = true;
    }
#endif
}