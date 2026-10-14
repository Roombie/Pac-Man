using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostScatter : GhostNodeSteering
{
    [Header("Scatter target (corner)")]
    [Tooltip("Place the exact corner NODE the ghost should aim at during Scatter.")]
    [SerializeField] private Node cornerNode;

    [Tooltip("If true, Blinky (Elroy) will chase Pac-Man even during Scatter.")]
    [SerializeField] private bool elroyChasesDuringScatter = true;

    private CircleCollider2D circleCollider2D;

    // Track movement-enabled edge & initial commit
    private bool lastMoveEnabled = false;
    private bool blinkyInitialCommitted = false;

    protected override Ghost.Mode SteeringMode => Ghost.Mode.Scatter;

    protected override void Awake()
    {
        base.Awake();
        circleCollider2D = GetComponent<CircleCollider2D>();
    }

    protected override void OnEnable()
    {
        blinkyInitialCommitted = false;
        if (circleCollider2D) circleCollider2D.enabled = true;
        if (move) lastMoveEnabled = move.enabled;

        base.OnEnable();
    }

    protected override void Update()
    {
        if (!ghost || !move) return;

        // Rising edge: movement just became enabled. On Blinky's first live frame he heads LEFT (arcade start)
        bool nowEnabled = move.enabled;
        bool risingEdge = nowEnabled && !lastMoveEnabled;
        lastMoveEnabled = nowEnabled;

        if (risingEdge && !blinkyInitialCommitted &&
            ghost.Type == GhostType.Blinky &&
            ghost.CurrentMode == Ghost.Mode.Scatter &&
            move.direction == Vector2.zero)
        {
            var dir = move.Occupied(Vector2.left) ? Vector2.right : Vector2.left;
            Commit(dir);
            blinkyInitialCommitted = true;
            return; // don't also decide at a node this frame and override LEFT
        }

        base.Update();
    }

    protected override Vector2 ChooseDirection(Node node, Vector2 current)
    {
        // Elroy keeps chasing Pac-Man during Scatter
        bool targetPacman =
            elroyChasesDuringScatter &&
            ghost.Type == GhostType.Blinky &&
            ghost.IsElroy &&
            ghost.CurrentPacman;

        Vector2 target = targetPacman
            ? (Vector2)ghost.CurrentPacman.transform.position
            : (cornerNode ? (Vector2)cornerNode.transform.position : Position);

        return GhostNavigation.ChooseTowardTarget(node, current, target, noUp: IsRedZone(node));
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
