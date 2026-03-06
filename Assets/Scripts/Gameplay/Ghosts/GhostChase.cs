using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostChase : MonoBehaviour
{
    private Ghost ghost;
    private Movement move;
    private Movement pacMove;

    [Header("Clyde (optional)")]
    [Tooltip("If set, Clyde will target this corner when near Pac-Man (< 8 tiles). If unset, Clyde just chases Pac-Man.")]
    [SerializeField] private Transform clydeScatterCorner;

    // Arcade tie-break order: Up, Left, Down, Right
    private static readonly Vector2[] DIRS = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move = ghost.movement ?? GetComponent<Movement>();
        if (ghost.CurrentPacman) pacMove = ghost.CurrentPacman.GetComponent<Movement>();
    }

    void Update()
    {
        if (!ghost || !ghost.CurrentPacman || !move) return;
        if (ghost.CurrentMode != Ghost.Mode.Chase) return;
        
        Vector3 target = GetChaseTarget();
        Vector2 dir = ChooseDirectionToward(target);
        move.SetDirection(dir);
    }

    // Targeting per ghost type (arcade rules)
    // https://pacman.holenet.info
    Vector3 GetChaseTarget()
    {
        Vector3 pacPos = ghost.CurrentPacman.transform.position;
        Vector2 pdir   = (pacMove && pacMove.direction != Vector2.zero) ? pacMove.direction : Vector2.right;

        switch (ghost.Type)
        {
            case GhostType.Blinky:
                // Directly target Pac-Man
                return pacPos;

            case GhostType.Pinky:
                // 4 tiles ahead, with the classic "up bug" (4 up + 4 left when facing up)
                return pacPos + (Vector3)AheadWithUpBug(pdir, 4);

            case GhostType.Inky:
            {
                // Take the point 2 tiles ahead of Pac-Man (with up bug),
                // then vector from Blinky to that point, doubled.
                Vector3 twoAhead = pacPos + (Vector3)AheadWithUpBug(pdir, 2);
                Vector3 blinkyPos = (ghost.blinky && ghost.blinky.movement)
                    ? (Vector3)ghost.blinky.movement.rb.position
                    : ghost.transform.position; // fallback
                Vector3 v = twoAhead - blinkyPos;
                return blinkyPos + v * 2f;
            }

            case GhostType.Clyde:
            {
                float distTiles = Vector3.Distance(transform.position, pacPos);
                if (distTiles >= 8f)
                    return pacPos; // chase like Blinky when far
                // when near, target scatter corner if provided (authentic),
                // otherwise just keep chasing Pac-Man as a safe fallback.
                return clydeScatterCorner ? clydeScatterCorner.position : pacPos;
            }

            default:
                return pacPos;
        }
    }

    // Implements the arcade "up bug" offsets (Pinky=4, Inky=2)
    Vector2 AheadWithUpBug(Vector2 dir, int tiles)
    {
        // Normalize to cardinal grid direction
        Vector2 d = (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));

        if (d == Vector2.up)    return new Vector2(-tiles,  tiles); // up bug
        if (d == Vector2.right) return new Vector2( tiles,  0f);
        if (d == Vector2.left)  return new Vector2(-tiles,  0f);
        /* down */              return new Vector2( 0f,    -tiles);
    }

    // --- Greedy chooser (arcade style) ---
    bool CanGo(Vector2 d) => move != null && !move.Occupied(d);

    // Pick the next direction that most reduces distance to the target.
    // Avoid reversing unless forced; tie-break order is U, L, D, R.
    Vector2 ChooseDirectionToward(Vector3 worldTarget)
    {
        if (move == null) return Vector2.zero;

        Vector2 current = move.direction;
        Vector2 best    = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in DIRS)
        {
            if (d == -current) continue;  // don't reverse unless forced
            if (!CanGo(d)) continue;      // blocked by wall

            // 1 unit step is fine (constant factor doesn't affect argmin)
            Vector3 next = (Vector3)move.rb.position + (Vector3)d;
            float score  = (next - worldTarget).sqrMagnitude;

            if (score < bestScore)
            {
                bestScore = score;
                best = d;
            }
        }

        // If all non-reverse options are blocked, allow reverse as last resort
        if (best == Vector2.zero && CanGo(-current))
            best = -current;

        // If still nothing, keep current (Movement will handle stalls)
        return best == Vector2.zero ? current : best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!ghost || !ghost.CurrentPacman) return;
        Gizmos.color = Color.yellow;
        Vector3 target = Application.isPlaying ? GetChaseTarget() : ghost.CurrentPacman.transform.position;
        Gizmos.DrawLine(transform.position, target);
        Gizmos.DrawWireSphere(target, 0.15f);
    }
#endif
}