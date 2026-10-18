using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostChase : GhostNodeSteering
{
    [Header("Clyde (optional)")]
    [Tooltip("If set, Clyde will target this corner when near Pac-Man (< 8 tiles). If unset, Clyde just chases Pac-Man.")]
    [SerializeField] private Transform clydeScatterCorner;

    protected override Ghost.Mode SteeringMode => Ghost.Mode.Chase;

    /// <summary>Tile this ghost is heading for right now (needs a current Pac-Man)</summary>
    public Vector2 Target => GetChaseTarget();

    protected override Vector2 ChooseDirection(Node node, Vector2 current)
    {
        if (!ghost.CurrentPacman) return current;
        return GhostNavigation.ChooseTowardTarget(node, current, GetChaseTarget(), noUp: IsRedZone(node));
    }

    // Targeting per ghost type (arcade rules)
    // https://pacman.holenet.info
    Vector3 GetChaseTarget()
    {
        // Always read the CURRENT Pac-Man (2P swaps the active one)
        var pacman  = ghost.CurrentPacman;
        var pacMove = pacman.movement;
        Vector3 pacPos = pacman.transform.position;
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