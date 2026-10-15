using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arcade ghost decision rules shared by Scatter, Chase and Frightened (Pac-Man Dossier):
/// decide only at maze nodes, never reverse by choice, tie-break Up > Left > Down > Right
/// </summary>
public static class GhostNavigation
{
    // Arcade tie-break order: Up, Left, Down, Right
    public static readonly Vector2[] Order = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    private static Node[] nodes;
    private static readonly System.Random rng = new();

    /// <summary>All maze nodes. Re-scans if the cache is empty or from a previous scene</summary>
    public static Node[] AllNodes
    {
        get
        {
            if (nodes == null || nodes.Length == 0 || !nodes[0])
                nodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return nodes;
        }
    }

    /// <summary>Closest node within radius of a position, or null</summary>
    public static Node NodeNear(Vector2 position, float radius)
    {
        Node best = null;
        float maxSq = radius * radius;

        foreach (var n in AllNodes)
        {
            if (!n) continue;
            float d2 = ((Vector2)n.transform.position - position).sqrMagnitude;
            if (d2 <= maxSq) { maxSq = d2; best = n; }
        }
        return best;
    }

    /// <summary>
    /// Exit that brings the ghost closest (straight-line) to the target, measured from the
    /// tile past the node. noUp forbids turning up (red zones in Scatter/Chase)
    /// </summary>
    public static Vector2 ChooseTowardTarget(Node node, Vector2 current, Vector2 target, bool noUp)
    {
        var options = node.availableDirections;
        Vector2 best = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in Order)
        {
            if (!IsAllowed(options, d, current)) continue;
            if (noUp && d == Vector2.up) continue;

            float score = ((Vector2)node.transform.position + d - target).sqrMagnitude;
            if (score < bestScore) { bestScore = score; best = d; }
        }

        return best != Vector2.zero ? best : Fallback(options, current);
    }

    /// <summary>
    /// Frightened: pseudo-random direction. If it's a wall or the reverse, try the next
    /// one in Up, Left, Down, Right order (Dossier)
    /// </summary>
    public static Vector2 ChooseRandom(Node node, Vector2 current)
    {
        var options = node.availableDirections;
        int start = rng.Next(Order.Length);

        for (int i = 0; i < Order.Length; i++)
        {
            var d = Order[(start + i) % Order.Length];
            if (IsAllowed(options, d, current)) return d;
        }

        return Fallback(options, current);
    }

    /// <summary>Any open direction except reversing (used if a ghost ends up against a wall between nodes)</summary>
    public static Vector2 AnyOpenDirection(Movement move)
    {
        Vector2 current = move.direction;
        foreach (var d in Order)
            if (d != -current && d != current && !move.Occupied(d)) return d;

        return !move.Occupied(-current) ? -current : Vector2.zero;
    }

    private static bool IsAllowed(List<Vector2> options, Vector2 d, Vector2 current)
    {
        if (options == null || !options.Contains(d)) return false;
        return current == Vector2.zero || d != -current; // no reversing by choice
    }

    // Dead end: reverse; still nothing: first open exit
    private static Vector2 Fallback(List<Vector2> options, Vector2 current)
    {
        if (options == null) return Vector2.zero;
        if (current != Vector2.zero && options.Contains(-current)) return -current;

        foreach (var d in Order)
            if (options.Contains(d)) return d;
        return Vector2.zero;
    }
}
