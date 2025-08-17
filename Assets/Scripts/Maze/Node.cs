using UnityEngine;
using System.Collections.Generic;

public class Node : MonoBehaviour
{
    [Header("Cardinal neighbours (auto linked)")]
    public Node up, left, down, right;

    [Header("Optional")]
    public bool outerDoorNode;
    public bool innerDoorNode;

    public static float TileSize = 1f;

    // Registry
    static readonly Dictionary<Vector2Int, Node> byCell = new();
    static Node[] all;

    public static Vector2Int CellOf(Vector3 p)
        => new(Mathf.RoundToInt(p.x / TileSize), Mathf.RoundToInt(p.y / TileSize));

    void OnEnable()  { Register(this); }
    void OnDisable() { Unregister(this); }

    static void Register(Node n)
    {
        var k = CellOf(n.transform.position);
        byCell[k] = n;
    }

    static void Unregister(Node n)
    {
        var k = CellOf(n.transform.position);
        if (byCell.TryGetValue(k, out var cur) && cur == n) byCell.Remove(k);
    }

    public static bool TryGet(Vector3 worldPos, out Node n)
        => byCell.TryGetValue(CellOf(worldPos), out n);

    /// <summary>
    /// Call once after your RuleTiles/Nodes are spawned (e.g. from a bootstrap).
    /// </summary>
    public static void RebuildGraph()
    {
        all = GetAllSceneNodes();
        byCell.Clear();
        foreach (var n in all) Register(n);

        foreach (var n in all)
        {
            var c = CellOf(n.transform.position);
            n.up    = GetAt(c + Vector2Int.up);
            n.left  = GetAt(c + Vector2Int.left);
            n.down  = GetAt(c + Vector2Int.down);
            n.right = GetAt(c + Vector2Int.right);
        }
    }

    static Node GetAt(Vector2Int cell)
    {
        byCell.TryGetValue(cell, out var n);
        return n;
    }

    static Node[] GetAllSceneNodes()
    {
#if UNITY_2023_1_OR_NEWER
        // New API
        return FindObjectsByType<Node>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#elif UNITY_2020_1_OR_NEWER
        // Older API with includeInactive flag
        return FindObjectsOfType<Node>(true);
#else
        // Fallback for very old versions – filter to scene objects only
        var list = new List<Node>();
        foreach (var n in Resources.FindObjectsOfTypeAll<Node>())
        {
            if (n != null && n.gameObject.scene.IsValid()) // exclude assets/prefabs not in scene
                list.Add(n);
        }
        return list.ToArray();
#endif
    }
}