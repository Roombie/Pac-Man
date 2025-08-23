using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Movement : MonoBehaviour
{
    [Header("Base movement")]
    public float speed = 8f;
    public float speedMultiplier = 1f;
    public float envMultiplier = 1f;
    public Vector2 initialDirection;
    public LayerMask obstacleLayer;
    private LayerMask activeObstacleMask;

    public Rigidbody2D rb { get; private set; }
    public Vector2 direction { get; private set; }
    public Vector2 nextDirection { get; private set; }
    public Vector3 startingPosition { get; private set; }
    public bool isBlocked { get; private set; }

    [Header("Cornering")]
    [SerializeField] private bool corneringEnabled = true;
    [Tooltip("How far from the new corridor centerline you may pre/post-turn (world units).")]
    [SerializeField] private float cornerWindow = 0.25f;
    [Tooltip("Snap tolerance when locking to the new corridor centerline.")]
    [SerializeField] private float snapEpsilon = 0.02f;

    private bool cornering = false;
    private Vector2 cornerDir = Vector2.zero; // the perpendicular nextDirection we’re cornering into

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        startingPosition = transform.position;
        activeObstacleMask = obstacleLayer;
    }

    private void Start()
    {
        ResetState();
    }

    public void ResetState()
    {
        speedMultiplier = 1f;
        activeObstacleMask = obstacleLayer;
        direction = initialDirection;
        nextDirection = Vector2.zero;
        transform.position = startingPosition;
        rb.bodyType = RigidbodyType2D.Dynamic;
        cornering = false;
        cornerDir = Vector2.zero;
        enabled = true;
    }

    private void Update()
    {
        // Keep trying queued turns for responsiveness
        if (nextDirection != Vector2.zero)
        {
            // If we can just take it now, do it
            SetDirection(nextDirection);

            // Otherwise, arm cornering if conditions are right
            if (corneringEnabled &&
                !cornering &&
                direction != Vector2.zero &&
                IsPerpendicular(direction, nextDirection) &&
                NearCenterlineFor(nextDirection) &&
                !Occupied(nextDirection))
            {
                cornering = true;
                cornerDir = Snap4(nextDirection);
            }
        }
        
        isBlocked = Occupied(direction);
    }

    private void FixedUpdate()
    {
        Vector2 pos = rb.position;
        float step = speed * speedMultiplier * envMultiplier * Time.fixedDeltaTime;

        if (cornering)
        {
            // “One pixel along old dir + one pixel along new dir”
            Vector2 baseStep  = direction  * step;
            Vector2 extraStep = cornerDir  * step;

            // Be safe: if the new corridor is actually blocked, abort cornering
            if (Occupied(cornerDir))
            {
                cornering = false;
                cornerDir = Vector2.zero;
                rb.MovePosition(pos + baseStep);
                return;
            }

            Vector2 nextPos = pos + baseStep + extraStep;

            // Detect crossing the new corridor centerline, snap, and commit the turn
            if (CrossedCenterline(pos, nextPos, cornerDir, out Vector2 snapped))
            {
                pos = snapped;
                direction = cornerDir;
                nextDirection = Vector2.zero;
                cornering = false;
                cornerDir = Vector2.zero;
                rb.MovePosition(pos);
            }
            else
            {
                rb.MovePosition(nextPos);
            }
        }
        else
        {
            // normal motion
            Vector2 translation = direction * step;
            rb.MovePosition(pos + translation);
        }
    }

    public void SetObstacleMask(LayerMask mask) { activeObstacleMask = mask; }
    public void ClearObstacleMask() { activeObstacleMask = obstacleLayer; }

    /// <summary>
    /// Disallow instant 180s unless forced. Otherwise, set immediately if free; else queue.
    /// </summary>
    public void SetDirection(Vector2 dir, bool forced = false)
    {
        dir = Snap4(dir);
        if (dir == Vector2.zero) return;

        // prevent instant reverse unless forced (global reversals, frightened enter, etc.)
        if (!forced && dir == -direction) { nextDirection = dir; return; }

        if (forced || !Occupied(dir))
        {
            direction = dir;
            nextDirection = Vector2.zero;
            // if we just took the queued turn, cornering is moot
            if (cornering && dir == cornerDir)
            {
                cornering = false;
                cornerDir = Vector2.zero;
            }
        }
        else
        {
            nextDirection = dir;
        }
    }

    public void SetNextDirection(Vector2 dir) => nextDirection = Snap4(dir);

    public bool Occupied(Vector2 dir)
    {
        return Physics2D.BoxCast(transform.position, Vector2.one * 0.75f, 0f, dir, 1.5f, activeObstacleMask).collider != null;
    }

    /// <summary>Sets the “base” multiplier (modes/elroy). Does not touch env multiplier.</summary>
    public void SetBaseSpeedMultiplier(float m) => speedMultiplier = Mathf.Max(0f, m);

    /// <summary>Sets the environment multiplier (slow zones). Does not touch base multiplier.</summary>
    public void SetEnvSpeedMultiplier(float m) => envMultiplier = Mathf.Max(0f, m);

    private static bool IsPerpendicular(Vector2 a, Vector2 b) =>
        Mathf.Abs(Vector2.Dot(Snap4(a), Snap4(b))) < 0.5f;

    private static Vector2 Snap4(Vector2 d)
    {
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return new Vector2(Mathf.Sign(d.x), 0f);
        return new Vector2(0f, Mathf.Sign(d.y));
    }

    // Are we within the side-to-centerline window for the *new* corridor?
    private bool NearCenterlineFor(Vector2 turnDir)
    {
        Vector2 p = rb ? rb.position : (Vector2)transform.position;

        if (Mathf.Abs(turnDir.y) > 0f)
        {
            // turning vertical -> we must be near x = round(x)
            float centerX = Mathf.Round(p.x);
            return Mathf.Abs(p.x - centerX) <= cornerWindow;
        }
        else
        {
            // turning horizontal -> we must be near y = round(y)
            float centerY = Mathf.Round(p.y);
            return Mathf.Abs(p.y - centerY) <= cornerWindow;
        }
    }

    // Did the segment from prev->next cross the target centerline? Output snapped pos.
    private bool CrossedCenterline(Vector2 prev, Vector2 next, Vector2 turnDir, out Vector2 snapped)
    {
        snapped = next;

        if (Mathf.Abs(turnDir.y) > 0f)
        {
            float cx = Mathf.Round(prev.x);
            float a = prev.x - cx;
            float b = next.x - cx;
            if (Mathf.Sign(a) != Mathf.Sign(b) || Mathf.Abs(b) <= snapEpsilon)
            {
                snapped = new Vector2(cx, next.y);
                return true;
            }
        }
        else
        {
            float cy = Mathf.Round(prev.y);
            float a = prev.y - cy;
            float b = next.y - cy;
            if (Mathf.Sign(a) != Mathf.Sign(b) || Mathf.Abs(b) <= snapEpsilon)
            {
                snapped = new Vector2(next.x, cy);
                return true;
            }
        }
        return false;
    }
}