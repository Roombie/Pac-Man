using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Movement : MonoBehaviour
{
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
        enabled = true;
    }

    private void Update()
    {
        // Try to move in the next direction while it's queued to make movements
        // more responsive
        if (nextDirection != Vector2.zero)
        {
            SetDirection(nextDirection);
        }
    }

    private void FixedUpdate()
    {
        // isBlocked = Occupied(direction);
        Vector2 position = rb.position;
        Vector2 translation = speed * speedMultiplier * envMultiplier * Time.fixedDeltaTime * direction;
        rb.MovePosition(position + translation);
    }
    
    public void SetObstacleMask(LayerMask mask) { activeObstacleMask = mask; }
    public void ClearObstacleMask() { activeObstacleMask = obstacleLayer; }

    public void SetDirection(Vector2 direction, bool forced = false)
    {
        // Disallow immediate 180° unless forced (used for global reversals/frightened enter)
        if (!forced && direction == -this.direction) { nextDirection = direction; return; }

        // otherwise we set it as the next direction so it'll automatically be
        // set when it does become available
        if (forced || !Occupied(direction))
        {
            this.direction = direction;
            nextDirection = Vector2.zero;
        }
        else
        {
            nextDirection = direction;
        }
    }
    
    public void SetNextDirection(Vector2 dir)
    {
        nextDirection = dir;
    }

    public bool Occupied(Vector2 dir)
    {
        return Physics2D.BoxCast(transform.position, Vector2.one * 0.75f, 0f, dir, 1.5f, activeObstacleMask).collider != null;
    }

    /// <summary>Sets the “base” multiplier (modes/elroy). Does not touch env multiplier.</summary>
    public void SetBaseSpeedMultiplier(float m) => speedMultiplier = Mathf.Max(0f, m);

    /// <summary>Sets the environment multiplier (slow zones). Does not touch base multiplier.</summary>
    public void SetEnvSpeedMultiplier(float m) => envMultiplier = Mathf.Max(0f, m);
}