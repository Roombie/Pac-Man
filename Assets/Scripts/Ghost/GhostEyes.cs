using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEyes : MonoBehaviour
{
    [Header("Renderer")]
    [SerializeField] private SpriteRenderer eyesRenderer;

    [Header("Normal eyes")]
    [SerializeField] private Sprite normalUp;
    [SerializeField] private Sprite normalDown;
    [SerializeField] private Sprite normalLeft;
    [SerializeField] private Sprite normalRight;

    [Header("Elroy eyes")]
    [SerializeField] private Sprite elroyUp;
    [SerializeField] private Sprite elroyDown;
    [SerializeField] private Sprite elroyLeft;
    [SerializeField] private Sprite elroyRight;

    private Ghost ghost;
    private Movement move;
    private Rigidbody2D rb;

    // Optional facing override (used by GhostHome while aligning/exiting)
    private bool   hasOverride;
    private Vector2 overrideDir;

    // Cache to avoid one-frame wrong direction & jitter
    private Vector2 lastDir = Vector2.left;
    private Vector2 prevPos;

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move  = ghost ? ghost.movement : GetComponent<Movement>();
        rb    = move ? move.rb : GetComponent<Rigidbody2D>();

        if (!eyesRenderer) eyesRenderer = GetComponentInChildren<SpriteRenderer>(true);
        prevPos = transform.position;

        // If Ghost prepared an initial facing for our current mode, it should have called ResetEyes().
        // If not, use whatever we can infer right now so we don't ever flash to "right".
        if (!hasOverride)
        {
            Vector2 seed = InferBestDir();
            if (seed == Vector2.zero) seed = Vector2.left;
            ApplyFacing(seed);
        }
    }

    void Update()
    {
        Vector2 dir = hasOverride ? overrideDir : InferBestDir();

        if (dir == Vector2.zero)
        {
            // keep last good direction if we're stationary this frame
            dir = lastDir;
        }

        ApplyFacing(dir);
    }

    /// <summary>Force an initial facing (called from Ghost.ResetEyesFacingForMode).</summary>
    public void ResetEyes(Vector2 initialDir)
    {
        hasOverride = false; // explicit reset clears any old override
        if (initialDir == Vector2.zero) initialDir = Vector2.left;
        ApplyFacing(initialDir);
        prevPos = transform.position;
    }

    /// <summary>Temporarily override where the eyes look (Home door alignment, etc.).</summary>
    public void SetOverrideFacing(Vector2 dir)
    {
        hasOverride = true;
        if (dir == Vector2.zero) dir = lastDir;
        overrideDir = dir;
        ApplyFacing(dir);
    }

    /// <summary>Release any temporary override (eyes go back to movement/velocity driven).</summary>
    public void ClearOverrideFacing()
    {
        hasOverride = false;
    }

    private Vector2 InferBestDir()
    {
        // Movement direction (authoritative once gameplay is live)
        if (move && move.enabled && move.direction != Vector2.zero)
            return move.direction;

        // Physics velocity
        if (rb && rb.simulated && rb.linearVelocity.sqrMagnitude > 0.0001f)
            return rb.linearVelocity;

        // Transform delta (in case physics/movement hasn't ticked yet)
        Vector2 delta = (Vector2)transform.position - prevPos;
        prevPos = transform.position;
        if (delta.sqrMagnitude > 0.000001f)
            return delta;

        // No signal; return zero so caller keeps lastDir
        return Vector2.zero;
    }

    private void ApplyFacing(Vector2 dir)
    {
        // Choose cardinal by dominant axis (tie-break keeps previous)
        Vector2 cardinal = ToCardinal(dir, lastDir);
        lastDir = cardinal;

        bool useElroy = (ghost && ghost.Type == GhostType.Blinky && ghost.IsElroy);

        Sprite sprite = SelectSprite(cardinal, useElroy);
        if (sprite && eyesRenderer) eyesRenderer.sprite = sprite;
    }

    private static Vector2 ToCardinal(Vector2 v, Vector2 fallback)
    {
        if (v == Vector2.zero) return fallback;

        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);

        if (ax > ay) return (v.x >= 0f) ? Vector2.right : Vector2.left;
        if (ay > ax) return (v.y >= 0f) ? Vector2.up    : Vector2.down;

        // Perfect tie: stick with previous to avoid flicker
        return fallback != Vector2.zero ? fallback : (v.x >= 0f ? Vector2.right : Vector2.left);
    }

    private Sprite SelectSprite(Vector2 cardinal, bool elroy)
    {
        // Prefer Elroy set when available; fall back to normal if a slot is missing
        if (elroy)
        {
            if (cardinal == Vector2.up    && elroyUp)    return elroyUp;
            if (cardinal == Vector2.down  && elroyDown)  return elroyDown;
            if (cardinal == Vector2.left  && elroyLeft)  return elroyLeft;
            if (cardinal == Vector2.right && elroyRight) return elroyRight;
            // fall through to normal if an elroy sprite is not set
        }

        if (cardinal == Vector2.up    && normalUp)    return normalUp;
        if (cardinal == Vector2.down  && normalDown)  return normalDown;
        if (cardinal == Vector2.left  && normalLeft)  return normalLeft;
        if (cardinal == Vector2.right && normalRight) return normalRight;

        return null;
    }
}