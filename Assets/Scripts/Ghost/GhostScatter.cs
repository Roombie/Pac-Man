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

    // Track movement-enabled edge & initial commit
    private bool lastMoveEnabled = false;
    private bool blinkyInitialCommitted = false;

    // Arcade tie-break order: Up, Left, Down, Right
    private static readonly Vector2[] TIE = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move  = ghost.movement ?? GetComponent<Movement>();

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
        blinkyInitialCommitted = false;
        circleCollider2D.enabled = true;

        if (!ghost || !move) return;

        lastMoveEnabled = move.enabled;

        // If we spawn into Scatter and movement is already live, allow an immediate commit at node
        if (ghost.CurrentMode == Ghost.Mode.Scatter && move.enabled)
        {
            // If this is Blinky's very first live frame, we still want LEFT; that is handled in Update's rising-edge block
            var n = ClosestNodeWithin(commitRadius);
            if (n) DecideAtNode(n, forceTurn: true);
        }
    }

    void Update()
    {
        if (!ghost || !move) return;

        // Detect rising edge: movement just became enabled
        bool nowEnabled = move.enabled;
        if (nowEnabled && !lastMoveEnabled)
        {
            // On the first enabled frame in Scatter, make Blinky commit LEFT (arcade start) if he has no heading yet.
            if (!blinkyInitialCommitted &&
                ghost.Type == GhostType.Blinky &&
                ghost.CurrentMode == Ghost.Mode.Scatter &&
                move.direction == Vector2.zero)
            {
                // Prefer to commit via node if we're close enough and LEFT is valid there
                var n = ClosestNodeWithin(commitRadius);
                bool committed = false;

                if (n)
                {
                    var options = n.availableDirections;
                    bool leftAllowed = options != null && options.Contains(Vector2.left) &&
                                       !(move.direction != Vector2.zero && Vector2.left == -move.direction);

                    if (leftAllowed)
                    {
                        move.SetDirection(Vector2.left, true);
                        if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
                        lastDecisionNode = n;
                        committed = true;
                    }
                }

                // Fallback: commit LEFT if not blocked; else go RIGHT
                if (!committed)
                {
                    var dir = Vector2.left;
                    if (move.Occupied(dir)) dir = Vector2.right;
                    move.SetDirection(dir, true);
                    if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
                }

                blinkyInitialCommitted = true;
                // Return so we don't also do a same-frame DecideAtNode that could override LEFT
                lastMoveEnabled = nowEnabled;
                return;
            }
        }
        lastMoveEnabled = nowEnabled;

        if (ghost.CurrentMode != Ghost.Mode.Scatter) return;
        if (!move.enabled) return; // still frozen

        // Normal scatter decisions at nodes
        var node = ClosestNodeWithin(commitRadius);
        if (!node || node == lastDecisionNode) return;

        DecideAtNode(node, forceTurn: true); // force at node centers to avoid parking
        lastDecisionNode = node;
    }

    private void DecideAtNode(Node node, bool forceTurn)
    {
        if (!node || move == null) return;

        IList<Vector2> options = node.availableDirections;

        // Corner target; DO NOT chase Pac-Man during Scatter (even if Elroy),
        // but allow it outside Scatter if you call BestDirAtNode from other modes.
        bool isScatter = ghost && ghost.CurrentMode == Ghost.Mode.Scatter;
        bool targetPacman =
            elroyChasesDuringScatter &&
            ghost.Type == GhostType.Blinky &&
            ghost.IsElroy &&
            ghost.pacman &&
            !isScatter; // ← gate chase OFF during Scatter

        Vector3 targetPos = targetPacman
            ? ghost.pacman.transform.position
            : (cornerNode ? cornerNode.transform.position : transform.position);

        Vector2 current = move.direction;
        Vector2 bestDir = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        // Prefer non-reverse options in tie-break order
        foreach (var d in TIE)
        {
            if (options == null || !options.Contains(d)) continue;
            if (current != Vector2.zero && d == -current) continue; // avoid 180 for now

            Vector3 nextCenter = node.transform.position + (Vector3)d;
            float score = (nextCenter - targetPos).sqrMagnitude;
            if (score < bestScore) { bestScore = score; bestDir = d; }
        }

        // If corner/dead end left only the reverse, allow it
        if (bestDir == Vector2.zero && current != Vector2.zero && options != null && options.Contains(-current))
            bestDir = -current;

        // Still nothing? take first available by tie-break
        if (bestDir == Vector2.zero && options != null)
            foreach (var d in TIE) { if (options.Contains(d)) { bestDir = d; break; } }

        if (bestDir != Vector2.zero)
        {
            // FORCE the turn at the node so we don’t rely on Movement’s own commit radius
            move.SetDirection(bestDir, forced: forceTurn || current == Vector2.zero);

            // Ensure physics is awake
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