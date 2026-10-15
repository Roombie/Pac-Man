using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Movement : MonoBehaviour
{
    [Header("Base movement")]
    public float speed = 8f;
    public float speedMultiplier = 1f;
    public float envMultiplier = 1f;
    public Vector2 initialDirection = new Vector2(1, 0); // always start going to the right
    public LayerMask obstacleLayer;
    private LayerMask activeObstacleMask;

    public Rigidbody2D rb { get; private set; }
    public Vector2 direction { get; private set; }
    public Vector2 nextDirection { get; private set; }
    public Vector3 startingPosition { get; private set; }
    /// <summary>True if the next tile ahead is a wall. Computed on demand (only the debug HUD reads it)</summary>
    public bool isBlocked => direction != Vector2.zero && Occupied(direction);

    [Header("Cornering")]
    [SerializeField] private bool corneringEnabled = true;
    [Tooltip("How far from the new corridor centerline you may pre/post-turn (world units).")]
    [SerializeField] private float cornerWindow = 0.25f;
    [Tooltip("Snap tolerance when locking to the new corridor centerline.")]
    [SerializeField] private float snapEpsilon = 0.02f;

    [Header("Grid Movement (arcade Pac-Man)")]
    [Tooltip("Tile-based movement: turns are allowed anywhere inside the intersection tile (arcade pre-turns/post-turns), " +
             "Pac-Man glides diagonally onto the new corridor's centerline, and stops at a tile center facing a wall. " +
             "The collider should exclude the obstacle layer so physics doesn't fight the diagonal.")]
    [SerializeField] private bool centerTurns = false;

    private bool cornering = false;
    private Vector2 cornerDir = Vector2.zero;  // the perpendicular nextDirection we’re cornering into

    private float stallTime; // seconds left standing still (Pac-Man eating dots)

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
        activeObstacleMask = obstacleLayer;
        direction = Snap4(initialDirection);
        nextDirection = Vector2.zero;

        transform.position = startingPosition;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.None;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = startingPosition;
            rb.rotation = 0f;
            rb.WakeUp();
        }

        cornering = false;
        cornerDir = Vector2.zero;
        stallTime = 0f;
        enabled = true;

        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// Hard reset for scripted start/restart/new-round movement.
    /// This bypasses normal turn logic and guarantees Pac-Man begins moving immediately.
    /// </summary>
    public void ForceStartMove(Vector2 dir)
    {
        dir = Snap4(dir);

        direction = dir;
        nextDirection = Vector2.zero;
        cornering = false;
        cornerDir = Vector2.zero;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.None;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = transform.position;
            rb.WakeUp();
        }

        Physics2D.SyncTransforms();
    }

    private void Update()
    {
        if (nextDirection != Vector2.zero)
        {
            SetDirection(nextDirection);

            if (!centerTurns &&
                corneringEnabled &&
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

    }

    private void FixedUpdate()
    {
        if (!enabled) return;
        if (rb == null || !rb.simulated) return;

        if (stallTime > 0f)
        {
            stallTime -= Time.fixedDeltaTime;
            return;
        }

        Vector2 pos = rb.position;
        float step = speed * speedMultiplier * envMultiplier * Time.fixedDeltaTime;

        if (direction == Vector2.zero)
            return;

        if (centerTurns)
        {
            GridStep(pos, step);
            return;
        }

        if (cornering)
        {
            Vector2 baseStep = direction * step;
            Vector2 extraStep = cornerDir * step;

            if (Occupied(cornerDir))
            {
                cornering = false;
                cornerDir = Vector2.zero;
                rb.MovePosition(pos + baseStep);
                return;
            }

            Vector2 nextPos = pos + baseStep + extraStep;

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
            Vector2 translation = direction * step;
            rb.MovePosition(pos + translation);
        }
    }

    /// <summary>Stops movement for a moment without changing direction (arcade dot-eating pause)</summary>
    public void Stall(float seconds)
    {
        stallTime = Mathf.Max(stallTime, seconds);
    }

    public void SetObstacleMask(LayerMask mask)
    {
        activeObstacleMask = mask;
    }

    public void ClearObstacleMask()
    {
        activeObstacleMask = obstacleLayer;
    }

    /// <summary>
    /// Disallow instant 180s unless forced. Otherwise, set immediately if free; else queue.
    /// </summary>
    public void SetDirection(Vector2 dir, bool forced = false)
    {
        dir = Snap4(dir);

        if (forced && dir == Vector2.zero)
        {
            direction = Vector2.zero;
            nextDirection = Vector2.zero;
            cornering = false;
            cornerDir = Vector2.zero;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            return;
        }

        if (dir == Vector2.zero)
            return;

        if (!forced && dir == -direction)
        {
            nextDirection = dir;
            return;
        }

        // Grid movement: queue it, FixedUpdate turns once Pac-Man is inside a tile that opens that way
        if (centerTurns && !forced && direction != Vector2.zero && IsPerpendicular(direction, dir))
        {
            nextDirection = dir;
            return;
        }

        if (forced || !Occupied(dir))
        {
            direction = dir;
            nextDirection = Vector2.zero;

            if (forced)
            {
                cornering = false;
                cornerDir = Vector2.zero;

                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.WakeUp();
                }
            }
            else if (cornering && dir == cornerDir)
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

    public void SetNextDirection(Vector2 dir)
    {
        nextDirection = Snap4(dir);
    }

    public void ClearNextDirection()
    {
        nextDirection = Vector2.zero;
    }

    public bool Occupied(Vector2 dir)
    {
        if (dir == Vector2.zero)
            return false;

        Vector2 origin = rb != null ? rb.position : (Vector2)transform.position;

        return Physics2D.BoxCast(
            origin,
            Vector2.one * 0.75f,
            0f,
            dir,
            1.5f,
            activeObstacleMask
        ).collider != null;
    }

    /// <summary>
    /// One physics step of arcade grid movement (Pac-Man Dossier "Cornering"):
    ///   - A queued perpendicular turn is taken anywhere inside the current tile if that side is open,
    ///     before the center (pre-turn) or after it (post-turn)
    ///   - Every step also slides the off-axis coordinate back onto the corridor centerline at the same
    ///     rate, so a pre/post-turn moves at 45 degrees until it's centered
    ///   - Facing a wall, Pac-Man stops on the tile center instead of pushing into it
    /// </summary>
    private void GridStep(Vector2 pos, float step)
    {
        Vector2 tileCenter = new Vector2(TileCenter(pos.x), TileCenter(pos.y));

        if (nextDirection != Vector2.zero &&
            IsPerpendicular(direction, nextDirection) &&
            !CellBlocked(tileCenter, nextDirection))
        {
            direction = nextDirection;
            nextDirection = Vector2.zero;
        }

        bool horizontal = direction.x != 0f;
        float along = horizontal ? pos.x : pos.y;
        float cross = horizontal ? pos.y : pos.x;
        float alongCenter = horizontal ? tileCenter.x : tileCenter.y;
        float crossCenter = horizontal ? tileCenter.y : tileCenter.x;
        float sign = horizontal ? direction.x : direction.y;

        float newAlong = along + sign * step;
        if (CellBlocked(tileCenter, direction))
            newAlong = sign > 0f ? Mathf.Min(newAlong, alongCenter) : Mathf.Max(newAlong, alongCenter);

        float newCross = Mathf.MoveTowards(cross, crossCenter, step);

        rb.MovePosition(horizontal ? new Vector2(newAlong, newCross) : new Vector2(newCross, newAlong));
    }

    /// <summary>True if the neighbouring cell in dir is a wall, checked from a tile center</summary>
    private bool CellBlocked(Vector2 tileCenter, Vector2 dir)
    {
        return Physics2D.BoxCast(tileCenter, Vector2.one * 0.75f, 0f, dir, 1f, activeObstacleMask).collider != null;
    }

    private static float TileCenter(float v) => Mathf.Floor(v) + 0.5f;

    public void SetBaseSpeedMultiplier(float m)
    {
        speedMultiplier = Mathf.Max(0f, m);
    }

    public void SetEnvSpeedMultiplier(float m)
    {
        envMultiplier = Mathf.Max(0f, m);
    }

    private static bool IsPerpendicular(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(Vector2.Dot(Snap4(a), Snap4(b))) < 0.5f;
    }

    private static Vector2 Snap4(Vector2 d)
    {
        if (d == Vector2.zero)
            return Vector2.zero;

        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y))
            return new Vector2(Mathf.Sign(d.x), 0f);

        return new Vector2(0f, Mathf.Sign(d.y));
    }

    private bool NearCenterlineFor(Vector2 turnDir)
    {
        Vector2 p = rb != null ? rb.position : (Vector2)transform.position;

        if (Mathf.Abs(turnDir.y) > 0f)
        {
            float centerX = Mathf.Round(p.x);
            return Mathf.Abs(p.x - centerX) <= cornerWindow;
        }
        else
        {
            float centerY = Mathf.Round(p.y);
            return Mathf.Abs(p.y - centerY) <= cornerWindow;
        }
    }

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