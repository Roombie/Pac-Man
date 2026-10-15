using UnityEngine;

/// <summary>
/// Frightened (blue) ghosts wander: a pseudo-random exit at every node (Pac-Man Dossier),
/// not a smart escape from Pac-Man. Red zones don't apply while frightened
/// </summary>
[RequireComponent(typeof(Ghost))]
public class GhostFrightened : GhostNodeSteering
{
    protected override Ghost.Mode SteeringMode => Ghost.Mode.Frightened;

    protected override Vector2 ChooseDirection(Node node, Vector2 current)
    {
        return GhostNavigation.ChooseRandom(node, current);
    }
}
