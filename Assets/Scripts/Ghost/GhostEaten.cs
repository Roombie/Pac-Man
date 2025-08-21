using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEaten : MonoBehaviour
{
    [Header("Home targets (assign in Inspector)")]
    [SerializeField] private Transform outsideDoor;  // first waypoint (just OUTSIDE gate)
    [SerializeField] private Transform insideDoor;   // revival point (just INSIDE gate)

    [Header("Layers & Masks")]
    [SerializeField] private string eyesLayerName = "GhostEyes";
    [SerializeField] private string normalLayerName = "Ghost";
    [Tooltip("Mask used while Eaten (Obstacle ONLY). Exclude the Door layer so eyes can enter.")]
    [SerializeField] private LayerMask eyesObstacleMask;

    [Header("Arrival")]
    [SerializeField] private float arriveRadius = 0.15f;

    private Ghost g;
    private Movement move;
    private int normalLayer;
    private int eyesLayer;

    private static readonly Vector2[] DIRS = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        g    = GetComponent<Ghost>();
        move = g.movement ?? GetComponent<Movement>();
        normalLayer = LayerMask.NameToLayer(normalLayerName);
        eyesLayer   = LayerMask.NameToLayer(eyesLayerName);
    }

    void OnEnable()
    {
        if (!g) return;
        if (g.CurrentMode != Ghost.Mode.Eaten) return;

        // Make eyes harmless but still collide with walls: swap to GhostEyes layer
        if (eyesLayer >= 0) gameObject.layer = eyesLayer;

        // Allow eyes to pass the pen door but not walls
        if (move && eyesObstacleMask.value != 0) move.SetObstacleMask(eyesObstacleMask);

        // Make sure movement runs while eaten
        if (move) move.enabled = true;
    }

    void OnDisable()
    {
        // Restore layer & obstacle mask when leaving Eaten
        if (normalLayer >= 0) gameObject.layer = normalLayer;
        if (move) move.ClearObstacleMask();
    }

    void Update()
    {
        if (!g || !move) return;
        if (g.CurrentMode != Ghost.Mode.Eaten) return;

        // Choose target: outside first (to align), then inside (revival)
        Transform target = null;

        if (outsideDoor && !IsAt(outsideDoor.position))
            target = outsideDoor;
        else if (insideDoor)
            target = insideDoor;
        else if (outsideDoor)
            target = outsideDoor;
        else
            return; // no targets assigned

        // If we’ve reached the revival point, switch to Home
        if (insideDoor && IsAt(insideDoor.position))
        {
            // Restore normal movement mask before switching mode
            if (move) move.ClearObstacleMask();
            g.SetMode(Ghost.Mode.Home);
            return;
        }

        // Steer toward the current target with simple tile-wise greedy choice
        Vector2 dir = ChooseDirectionToward(target.position);
        move.SetDirection(dir);
    }

    private bool IsAt(Vector3 pos)
    {
        if (!move || !move.rb) return false;
        return (((Vector2)move.rb.position - (Vector2)pos).sqrMagnitude <= arriveRadius * arriveRadius);
    }

    private bool CanGo(Vector2 d) => move != null && !move.Occupied(d);

    private Vector2 ChooseDirectionToward(Vector3 target)
    {
        Vector2 current = move.direction;
        Vector2 best = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < DIRS.Length; i++)
        {
            var d = DIRS[i];
            if (d == -current) continue;     // avoid reversing unless needed
            if (!CanGo(d)) continue;

            Vector3 next = (Vector3)move.rb.position + (Vector3)d;
            float score = (next - target).sqrMagnitude;

            if (score < bestScore) { bestScore = score; best = d; }
        }

        // If no forward option, allow reversing as a last resort
        if (best == Vector2.zero && CanGo(-current)) best = -current;
        return best == Vector2.zero ? current : best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (outsideDoor) Gizmos.DrawWireSphere(outsideDoor.position, 0.1f);
        Gizmos.color = Color.green;
        if (insideDoor) Gizmos.DrawWireSphere(insideDoor.position, 0.1f);
    }
#endif
}