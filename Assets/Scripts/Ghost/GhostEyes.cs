using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEyes : MonoBehaviour
{
    [SerializeField] public SpriteRenderer eyesRenderer;

    [Header("Normal eyes")]
    public Sprite rightEyes, leftEyes, upEyes, downEyes;

    [Header("Elroy eyes")]
    public Sprite rightEyesElroy, leftEyesElroy, upEyesElroy, downEyesElroy;

    private Ghost ghost;
    private Movement mover;

    private Vector2 lastDir = Vector2.right;
    private Facing lastFacing;
    private bool lastUsingElroy = false;

    private bool overrideActive;
    private Vector2 overrideDir = Vector2.right;

    // for transform-delta facing when kinematic/scripted
    [SerializeField] private float motionEpsilon = 0.0005f;
    private Vector3 lastPos;
    private bool haveLastPos = false;

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        mover = ghost.movement ? ghost.movement : GetComponent<Movement>();
        if (!eyesRenderer) eyesRenderer = GetComponentInChildren<SpriteRenderer>(true);

        // Start Blinky looking left (until real motion takes over)
        if (ghost && ghost.Type == GhostType.Blinky)
            lastDir = Vector2.left;

        lastPos = transform.position;
        haveLastPos = true;
    }

    void OnEnable()
    {
        // Reset position baseline on re-enable to avoid a big first delta
        lastPos = transform.position;
        haveLastPos = true;
    }

    void LateUpdate()
    {
        if (!eyesRenderer || !eyesRenderer.enabled) return;

        Vector2 dir;

        if (overrideActive)
        {
            dir = overrideDir;
        }
        else
        {
            // Prefer transform delta (works when rb is Kinematic / scripted motion)
            Vector2 delta = Vector2.zero;
            if (haveLastPos)
                delta = (Vector2)(transform.position - lastPos);

            if (delta.sqrMagnitude > motionEpsilon * motionEpsilon)
            {
                dir = delta;
            }
            else if (mover && mover.rb && mover.rb.bodyType == RigidbodyType2D.Dynamic &&
                     mover.rb.linearVelocity.sqrMagnitude > motionEpsilon * motionEpsilon)
            {
                // Use real physics velocity when dynamic
                dir = mover.rb.linearVelocity;
            }
            else
            {
                // Fall back to planned heading; then stickiness
                dir = mover ? mover.direction : Vector2.zero;
                if (dir == Vector2.zero) dir = lastDir;
            }
        }

        var facing = MajorFacing(dir);
        bool useElroy = ShouldUseElroySprites();

        if (facing != lastFacing || useElroy != lastUsingElroy)
        {
            eyesRenderer.sprite = SelectSprite(facing, useElroy);
            lastFacing = facing;
            lastUsingElroy = useElroy;
        }

        if (dir.sqrMagnitude > 0.0001f)
            lastDir = dir;
            
        lastPos = transform.position;
        haveLastPos = true;
    }

    /// <summary>Force the eye facing (e.g., during GhostHome ExitRoutine).</summary>
    public void SetOverrideFacing(Vector2 dir)
    {
        overrideActive = true;
        overrideDir = Snap4(dir);
        // apply immediately
        var f = MajorFacing(overrideDir);
        eyesRenderer.sprite = SelectSprite(f, ShouldUseElroySprites());
        lastFacing = f;
        lastDir = overrideDir;
        lastUsingElroy = ShouldUseElroySprites();
    }

    /// <summary>Return control back to movement-driven facing.</summary>
    public void ClearOverrideFacing() => overrideActive = false;

    /// <summary>Directly set facing once (no persistent override).</summary>
    public void LookAt(Vector2 dir)
    {
        if (!eyesRenderer) return;
        if (dir == Vector2.zero) dir = lastDir;
        lastDir = dir;

        var f = MajorFacing(dir);
        eyesRenderer.sprite = SelectSprite(f, ShouldUseElroySprites());
        lastFacing = f;
        lastUsingElroy = ShouldUseElroySprites();
    }

    public void ResetEyes(Vector2 initialDir) => LookAt(initialDir);

    enum Facing { Right, Left, Up, Down }

    private Facing MajorFacing(Vector2 d)
    {
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return d.x >= 0f ? Facing.Right : Facing.Left;
        return d.y >= 0f ? Facing.Up : Facing.Down;
    }

    private Vector2 Snap4(Vector2 d)
    {
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return new Vector2(Mathf.Sign(d.x), 0f);
        return new Vector2(0f, Mathf.Sign(d.y));
    }

    private bool ShouldUseElroySprites()
    {
        // Eyes use normal sprites while Eaten; Elroy variant only when body is visible.
        if (ghost && ghost.CurrentMode == Ghost.Mode.Eaten) return false;
        return ghost && ghost.IsElroy;
    }

    private Sprite SelectSprite(Facing f, bool elroy)
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