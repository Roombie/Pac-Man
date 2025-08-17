using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEyes : MonoBehaviour
{
    [SerializeField] public SpriteRenderer eyesRenderer;

    [Header("Normal eyes")]
    public Sprite rightEyes, leftEyes, upEyes, downEyes;

    [Header("Elroy eyes")]
    public Sprite rightEyesElroy, leftEyesElroy, upEyesElroy, downEyesElroy;

    Ghost ghost;
    Movement mover;
    Vector2 lastDir = Vector2.right;
    Facing lastFacing = Facing.Right;
    bool lastUsingElroy = false;

    void Awake()
    {
        ghost  = GetComponent<Ghost>();
        mover  = ghost.movement ? ghost.movement : GetComponent<Movement>();
        if (!eyesRenderer) eyesRenderer = GetComponentInChildren<SpriteRenderer>(true);
    }

    void LateUpdate()
    {
        if (!mover || !eyesRenderer || !eyesRenderer.enabled) return;

        Vector2 dir = mover.direction;
        if (dir == Vector2.zero) dir = lastDir;

        var facing = MajorFacing(dir);
        bool useElroy = ShouldUseElroySprites();

        if (facing != lastFacing || useElroy != lastUsingElroy)
        {
            eyesRenderer.sprite = SelectSprite(facing, useElroy);
            lastFacing = facing;
            lastUsingElroy = useElroy;
        }

        lastDir = dir;
    }

    // --- public helpers for reset/teleport/etc. ---
    public void LookAt(Vector2 dir)
    {
        if (!eyesRenderer) return;
        if (dir == Vector2.zero) dir = lastDir;
        lastDir = dir;

        var facing = MajorFacing(dir);
        eyesRenderer.sprite = SelectSprite(facing, ShouldUseElroySprites());
        lastFacing = facing;
        lastUsingElroy = ShouldUseElroySprites();
    }

    public void ResetEyes(Vector2 initialDir)
    {
        // call from Ghost.ResetState()
        LookAt(initialDir);
    }

    // --- internals ---
    enum Facing { Right, Left, Up, Down }

    Facing MajorFacing(Vector2 d)
    {
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return d.x >= 0f ? Facing.Right : Facing.Left;
        else
            return d.y >= 0f ? Facing.Up : Facing.Down;
    }

    bool ShouldUseElroySprites()
    {
        // Elroy visuals only make sense when the body is visible.
        if (ghost.CurrentMode == Ghost.Mode.Eaten) return false;
        return ghost.IsElroyActive;
    }

    Sprite SelectSprite(Facing f, bool elroy)
    {
        if (elroy)
        {
            switch (f)
            {
                case Facing.Right: return rightEyesElroy ? rightEyesElroy : rightEyes;
                case Facing.Left:  return leftEyesElroy  ? leftEyesElroy  : leftEyes;
                case Facing.Up:    return upEyesElroy    ? upEyesElroy    : upEyes;
                case Facing.Down:  return downEyesElroy  ? downEyesElroy  : downEyes;
            }
        }
        else
        {
            switch (f)
            {
                case Facing.Right: return rightEyes;
                case Facing.Left:  return leftEyes;
                case Facing.Up:    return upEyes;
                case Facing.Down:  return downEyes;
            }
        }
        return rightEyes;
    }
}