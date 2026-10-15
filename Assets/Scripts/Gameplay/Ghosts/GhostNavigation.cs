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

    // Nodes bucketed by the 1x1 world cell they sit in, with cached positions (maze nodes never move)
    private struct NodeEntry
    {
        public Node node;
        public Vector2 position;
    }
    private static readonly Dictionary<Vector2Int, List<NodeEntry>> grid = new();

    /// <summary>All maze nodes. Re-scans if the cache is empty or from a previous scene</summary>
    public static Node[] AllNodes
    {
        get
        {
            if (nodes == null || nodes.Length == 0 || !nodes[0])
            {
                nodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                BuildGrid();
            }
            return nodes;
        }
    }

    private static void BuildGrid()
    {
        grid.Clear();
        foreach (var n in nodes)
        {
            if (!n) continue;
            Vector2 p = n.transform.position;
            var cell = Cell(p);
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<NodeEntry>(1);
            list.Add(new NodeEntry { node = n, position = p });
        }
    }

    private static Vector2Int Cell(Vector2 p) => new(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

    /// <summary>
    /// Closest node within radius of a position, or null. Called by every ghost every frame,
    /// so it only checks the cells the radius can reach instead of every node
    /// </summary>
    public static Node NodeNear(Vector2 position, float radius)
    {
        _ = AllNodes; // (re)build the cache if needed

        Node best = null;
        float maxSq = radius * radius;
        int reach = Mathf.Max(1, Mathf.CeilToInt(radius));
        var center = Cell(position);

        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dy = -reach; dy <= reach; dy++)
            {
                if (!grid.TryGetValue(new Vector2Int(center.x + dx, center.y + dy), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    float d2 = (e.position - position).sqrMagnitude;
                    if (d2 <= maxSq && e.node) { maxSq = d2; best = e.node; }
                }
            }
        }
        return best;
    }

    /// <summary>Closest node to a position at any distance, or null</summary>
    public static Node ClosestNode(Vector2 position)
    {
        Node best = null;
        float bestSq = float.PositiveInfinity;
        foreach (var list in GridLists())
        {
            for (int i = 0; i < list.Count; i++)
            {
                float d2 = (list[i].position - position).sqrMagnitude;
                if (d2 < bestSq && list[i].node) { bestSq = d2; best = list[i].node; }
            }
        }
        return best;
    }

    private static Dictionary<Vector2Int, List<NodeEntry>>.ValueCollection GridLists()
    {
        _ = AllNodes;
        return grid.Values;
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
