using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostFrightened : MonoBehaviour
{
    private Ghost g;
    private Movement move;

    // Arcade tie-break order: Up, Left, Down, Right
    private static readonly Vector2[] DIRS = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        g = GetComponent<Ghost>();
        move = g.movement ?? GetComponent<Movement>();
    }

    void Update()
    {
        if (!g || !move || !g.pacman) return;
        if (g.CurrentMode != Ghost.Mode.Frightened) return;

        Vector3 threat = g.pacman.transform.position;
        Vector2 dir = ChooseDirectionAway(threat);
        move.SetDirection(dir); // uses Movement.SetDirection(...) :contentReference[oaicite:2]{index=2}
    }

    bool CanGo(Vector2 d) => move != null && !move.Occupied(d); // Movement.Occupied(...) :contentReference[oaicite:3]{index=3}

    // Maximize distance to Pac-Man; avoid reverse unless all else blocked
    Vector2 ChooseDirectionAway(Vector3 threat)
    {
        Vector2 current = move.direction; // Movement.direction :contentReference[oaicite:4]{index=4}
        Vector2 best = Vector2.zero;
        float bestScore = float.NegativeInfinity;

        foreach (var d in DIRS)
        {
            if (d == -current) continue;   // don't reverse unless forced
            if (!CanGo(d)) continue;

            Vector3 next = (Vector3)move.rb.position + (Vector3)d; // Movement.rb :contentReference[oaicite:5]{index=5}
            float score = (next - threat).sqrMagnitude; // larger = farther = better

            if (score > bestScore) { bestScore = score; best = d; }
        }

        // If everything else is blocked, reverse as last resort
        if (best == Vector2.zero && CanGo(-current)) best = -current;

        return best == Vector2.zero ? current : best;
    }
}