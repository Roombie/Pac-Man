using UnityEngine;

/// <summary>
/// Base for the ghost modes that steer through the maze (Scatter, Chase, Frightened)
/// Decisions are made once per node; subclasses only say which exit to take
/// </summary>
[RequireComponent(typeof(Ghost))]
public abstract class GhostNodeSteering : MonoBehaviour
{
    [Header("Node decision")]
    [Tooltip("Max distance to a Node center to consider we're 'at' the node and commit a turn.")]
    [SerializeField] protected float commitRadius = 0.18f;

    protected Ghost ghost;
    protected Movement move;
    private Node lastDecisionNode;

    // Stuck detection for the safety net
    private const float StuckSeconds = 0.15f;
    private Vector2 lastPosition;
    private float stuckTime;

    /// <summary>The mode this behaviour steers in</summary>
    protected abstract Ghost.Mode SteeringMode { get; }

    /// <summary>Exit to take at this node; current is the ghost's heading</summary>
    protected abstract Vector2 ChooseDirection(Node node, Vector2 current);

    protected Vector2 Position => (move && move.rb) ? move.rb.position : (Vector2)transform.position;

    protected virtual void Awake()
    {
        ghost = GetComponent<Ghost>();
        move = ghost.movement ? ghost.movement : GetComponent<Movement>();
    }

    protected virtual void OnEnable()
    {
        lastDecisionNode = null;
        stuckTime = 0f;
        if (!ghost || !move) return;
        lastPosition = Position;

        // Entering this mode while sitting on a node: decide right away
        if (ghost.CurrentMode == SteeringMode && move.enabled)
        {
            var n = GhostNavigation.NodeNear(Position, commitRadius);
            if (n) DecideAt(n);
        }
    }

    protected virtual void Update()
    {
        if (!ghost || !move) return;
        if (ghost.CurrentMode != SteeringMode || !move.enabled) return;

        Vector2 pos = Position;
        bool moved = (pos - lastPosition).sqrMagnitude > 0.0001f;
        lastPosition = pos;

        var node = GhostNavigation.NodeNear(pos, commitRadius);
        if (node)
        {
            stuckTime = 0f;
            if (node != lastDecisionNode) DecideAt(node);
            return;
        }

        // Left the node: the next visit (e.g. after a reversal) decides again
        lastDecisionNode = null;

        // Safety net: only if the ghost is actually pinned against a wall between nodes
        // (Movement.isBlocked looks 1.5 tiles ahead, so it can't be used for this)
        if (moved || Time.timeScale <= 0f) { stuckTime = 0f; return; }

        stuckTime += Time.deltaTime;
        if (stuckTime >= StuckSeconds)
        {
            stuckTime = 0f;
            var d = GhostNavigation.AnyOpenDirection(move);
            if (d != Vector2.zero) Commit(d);
        }
    }

    protected void DecideAt(Node node)
    {
        lastDecisionNode = node;
        var dir = ChooseDirection(node, move.direction);
        if (dir != Vector2.zero) Commit(dir);
    }

    protected void Commit(Vector2 dir)
    {
        // Forced so the turn happens at the node instead of relying on Movement's own queueing
        move.SetDirection(dir, forced: true);
        if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
    }

    /// <summary>Red zones: Scatter/Chase ghosts may not turn up at these nodes</summary>
    protected static bool IsRedZone(Node node)
    {
        var gm = GameManager.Instance;
        return gm != null && gm.globalGhostModeController != null && gm.globalGhostModeController.IsRedZone(node);
    }
}
