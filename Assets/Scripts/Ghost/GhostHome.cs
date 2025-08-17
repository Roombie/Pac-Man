using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostHome : MonoBehaviour
{
    [Header("Pen geometry")]
    [SerializeField] private Node doorNode;       // place at the door, just *outside* the box
    [SerializeField] private float arriveRadius = 0.15f;

    [Header("Vertical bounce inside house")]
    [SerializeField] private float bounceTopYOffset = 0.25f;   // relative to starting Y
    [SerializeField] private float bounceBottomYOffset = -0.25f;
    [SerializeField] private float bounceSpeed = 2f;

    [Header("Launch delay per-ghost")]
    [SerializeField] private float launchDelaySeconds = 4f; // e.g. Pinky=4, Inky=8, Clyde=12

    [SerializeField] private bool forceExitNow;

    private Ghost g;
    private Movement move;

    private enum PenState { Idle, Waiting, Exiting }
    private PenState state = PenState.Idle;

    private float t;                  // timer for Waiting
    private float startY;
    private Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
    private static readonly Vector2[] DIRS = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    public void RequestImmediateExit() => forceExitNow = true;

    void Awake()
    {
        g = GetComponent<Ghost>();
        move = g.movement ? g.movement : GetComponent<Movement>();
        startY = transform.position.y;
    }

    void OnEnable()
    {
        // whenever we re-enter Home, restart the wait
        if (g && g.CurrentMode == Ghost.Mode.Home)
        {
            state = PenState.Idle;
            t = launchDelaySeconds;
        }
    }

    void Update()
    {
        if (!g || !move) return;

        if (g.CurrentMode != Ghost.Mode.Home)
        {
            state = PenState.Idle;
            return;
        }

        if (g.CurrentMode == Ghost.Mode.Home)
        {
            // if forceExitNow -> bypass any launch delay and head to door
            if (forceExitNow)
            {
                // set your internal state to 'exiting' immediately
                // e.g., state = PenState.Exiting; launchDelaySeconds = 0f; etc.
                // and give it an upward nudge:
                if (g.movement) g.movement.SetDirection(Vector2.up, forced: true);
            }
        }

        if (!doorNode)
        {
            // Safe fallback: stop movement if we can't exit
            move.SetDirection(Vector2.zero, true);
            return;
        }

        switch (state)
        {
            case PenState.Idle:
                // Arrived from Eaten or level start → stop and begin waiting
                move.SetDirection(Vector2.zero, true);
                t = launchDelaySeconds;
                state = PenState.Waiting;
                break;

            case PenState.Waiting:
            {
                // Classic up/down “bobbing” while waiting the release timer
                t -= Time.deltaTime;

                float top = startY + bounceTopYOffset;
                float bottom = startY + bounceBottomYOffset;

                // sine ping-pong
                float y = Mathf.Lerp(bottom, top, 0.5f * (1f + Mathf.Sin(Time.time * bounceSpeed)));
                Vector2 target = new Vector2(move.rb.position.x, y);
                Vector2 dir = (target - move.rb.position).normalized;

                // choose the dominant axis: up or down
                move.SetDirection(Mathf.Sign(dir.y) >= 0 ? Vector2.up : Vector2.down);

                if (t <= 0f)
                    state = PenState.Exiting;
                break;
            }

            case PenState.Exiting:
            {
                // Greedy toward the door (same chooser style as Chase/Scatter)
                Vector2 d = DecideToward(doorNode.transform.position);
                move.SetDirection(d);

                // Reached the door? Switch to Scatter and reset
                if (((Vector2)move.rb.position - (Vector2)doorNode.transform.position).sqrMagnitude <= arriveRadius * arriveRadius)
                {
                    g.SetMode(Ghost.Mode.Scatter);
                    state = PenState.Idle;
                }
                break;
            }
        }
    }

    // ---- greedy chooser (once per tile) ----
    Vector2Int WorldToCell(Vector2 p)
    {
        return new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
    }

    bool CanGo(Vector2 d) => move != null && !move.Occupied(d);

    Vector2 DecideToward(Vector3 worldTarget)
    {
        var cell = WorldToCell(move.rb.position);
        if (cell == lastCell) return move.direction;
        lastCell = cell;

        Vector2 current = move.direction;
        Vector2 best = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in DIRS)
        {
            if (d == -current) continue;
            if (!CanGo(d)) continue;

            Vector3 next = (Vector3)move.rb.position + (Vector3)d;
            float score = (next - worldTarget).sqrMagnitude;
            if (score < bestScore) { bestScore = score; best = d; }
        }

        if (best == Vector2.zero && CanGo(-current)) best = -current;
        return best == Vector2.zero ? current : best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!doorNode) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(doorNode.transform.position, 0.12f);
        Gizmos.DrawLine(transform.position, doorNode.transform.position);
    }
#endif
}
