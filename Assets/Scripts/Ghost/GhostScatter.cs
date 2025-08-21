using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostScatter : MonoBehaviour
{
    [Header("Scatter target (corner)")]
    [Tooltip("Place the exact corner NODE the ghost should aim at during Scatter.")]
    [SerializeField] private Node cornerNode;

    [Header("Node decision")]
    [Tooltip("Max distance to a Node center to consider we're 'at' the node and commit a turn.")]
    [SerializeField] private float commitRadius = 0.18f;

    [Tooltip("If true, Blinky (Elroy) will chase Pac-Man even during Scatter.")]
    [SerializeField] private bool elroyChasesDuringScatter = true;

    private Ghost ghost;
    private Movement move;

    private Node[] allNodes;
    private Node lastDecisionNode;
    private CircleCollider2D circleCollider2D;

    // Arcade tie-break order: Up, Left, Down, Right
    private static readonly Vector2[] TIE = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move = ghost.movement ?? GetComponent<Movement>();

        circleCollider2D = GetComponent<CircleCollider2D>();
#if UNITY_2023_1_OR_NEWER
        allNodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        allNodes = FindObjectsOfType<Node>(false);
#endif
    }

    void OnEnable()
    {
        lastDecisionNode = null;

        // If we spawn into Scatter, choose an initial heading IMMEDIATELY (even if movement is disabled).
        if (ghost && ghost.CurrentMode == Ghost.Mode.Scatter)
        {
            var n = ClosestNodeWithin(commitRadius);
            if (n) DecideAtNode(n, forceTurn: true);  // <<< force the first pick so there's no stall
        }
        circleCollider2D.enabled = true;
    }

    void Update()
    {
        if (!ghost || !move) return;
        if (ghost.CurrentMode != Ghost.Mode.Scatter) return;

        // Only decide at nodes
        var n = ClosestNodeWithin(commitRadius);
        if (!n || n == lastDecisionNode) return;

        DecideAtNode(n, forceTurn:true);            // <<< force turns at node centers to avoid parking
        lastDecisionNode = n;
    }

    private void DecideAtNode(Node node, bool forceTurn)
    {
        if (!node || move == null) return;

        IList<Vector2> options = node.availableDirections;

        // Corner target; Blinky (Elroy) can chase Pac-Man if desired
        Vector3 targetPos =
            (elroyChasesDuringScatter && ghost.Type == GhostType.Blinky && ghost.IsElroy && ghost.pacman)
            ? ghost.pacman.transform.position
            : (cornerNode ? cornerNode.transform.position : transform.position);

        Vector2 current = move.direction;
        Vector2 bestDir = Vector2.zero;
        float bestScore = float.PositiveInfinity;
        int viable = 0;

        // Prefer non-reverse options in tie-break order
        foreach (var d in TIE)
        {
            if (!options.Contains(d)) continue;
            if (current != Vector2.zero && d == -current) continue; // avoid 180 for now
            viable++;

            Vector3 nextCenter = node.transform.position + (Vector3)d;
            float score = (nextCenter - targetPos).sqrMagnitude;
            if (score < bestScore) { bestScore = score; bestDir = d; }
        }

        // If corner/dead end left only the reverse, allow it
        if (bestDir == Vector2.zero && current != Vector2.zero && options.Contains(-current))
            bestDir = -current;

        // Still nothing? take first available by tie-break
        if (bestDir == Vector2.zero)
            foreach (var d in TIE) { if (options.Contains(d)) { bestDir = d; break; } }

        if (bestDir != Vector2.zero)
        {
            // FORCE the turn at the node so we don’t rely on Movement’s own commit radius
            move.SetDirection(bestDir, forced: forceTurn || current == Vector2.zero);

            // Make sure physics is awake when gameplay begins
            if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
        }
    }

    private Node ClosestNodeWithin(float radius)
    {
        if (allNodes == null || allNodes.Length == 0) return null;

        Vector2 p = (move && move.rb) ? move.rb.position : (Vector2)transform.position;
        Node best = null;
        float maxSq = radius * radius;

        for (int i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            float d2 = ((Vector2)n.transform.position - p).sqrMagnitude;
            if (d2 <= maxSq) { maxSq = d2; best = n; }
        }
        return best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (cornerNode)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(cornerNode.transform.position, Vector3.one * 0.9f);
        }
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, commitRadius);
    }
#endif
}