using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEaten : MonoBehaviour
{
    [Header("Home target (ghost house center/door)")]
    [SerializeField] private Node homeTarget;
    [SerializeField] private float arriveRadius = 0.15f;

    private Ghost g;
    private Movement move;

    private static readonly Vector2[] DIRS = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        g = GetComponent<Ghost>();
        move = g.movement ?? GetComponent<Movement>();
    }

    void Update()
    {
        if (!g || !move || !homeTarget) return;
        if (g.CurrentMode != Ghost.Mode.Eaten) return;

        Vector2 dir = ChooseDirectionToward(homeTarget.transform.position);
        move.SetDirection(dir);

        // Arrived at home? Switch to Home mode so your gate logic can take over
        if (((Vector2)move.rb.position - (Vector2)homeTarget.transform.position).sqrMagnitude <= arriveRadius * arriveRadius)
        {
            g.SetMode(Ghost.Mode.Home);
        }
    }

    bool CanGo(Vector2 d) => move != null && !move.Occupied(d);

    // Minimize distance to home; avoid reverse unless forced
    Vector2 ChooseDirectionToward(Vector3 target)
    {
        Vector2 current = move.direction;
        Vector2 best = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in DIRS)
        {
            if (d == -current) continue;
            if (!CanGo(d)) continue;

            Vector3 next = (Vector3)move.rb.position + (Vector3)d;
            float score = (next - target).sqrMagnitude;

            if (score < bestScore) { bestScore = score; best = d; }
        }

        if (best == Vector2.zero && CanGo(-current)) best = -current;
        return best == Vector2.zero ? current : best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!homeTarget) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(homeTarget.transform.position, 0.1f);
    }
#endif
}