using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostScatter : MonoBehaviour
{
    [SerializeField] private Transform cornerTarget;

    private Ghost ghost;
    private Movement move;
    private static readonly Vector2[] dirs = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move  = ghost.movement ?? GetComponent<Movement>();
    }

    void Update()
    {
        if (!move || !cornerTarget) return;

        Vector2 current = move.direction;
        Vector2 best = current;
        float bestScore = float.PositiveInfinity;

        // Greedy choice toward the unreachable target; avoid reversing unless forced
        foreach (var d in dirs)
        {
            if (d == -current) continue;
            if (move.Occupied(d)) continue;

            Vector3 next = (Vector3)move.rb.position + (Vector3)d;
            float score = (next - cornerTarget.position).sqrMagnitude;
            if (score < bestScore) { bestScore = score; best = d; }
        }

        // If all non-reverse options blocked, allow reverse as last resort
        if (best == current && move.Occupied(best) && !move.Occupied(-current))
            best = -current;

        move.SetDirection(best);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!cornerTarget) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireCube(cornerTarget.position, Vector3.one * 0.9f);
    }
#endif
}