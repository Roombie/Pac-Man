using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostHome : MonoBehaviour
{
    [Header("Door targets (assign in Inspector)")]
    [SerializeField] private Transform insideDoor;   // point just INSIDE the door
    [SerializeField] private Transform outsideDoor;  // point just OUTSIDE the door

    [Header("Timing")]
    [SerializeField] private float launchDelaySeconds = 4f;   // counts only after movement is enabled
    [SerializeField] private float alignStepDuration = 0.20f; // per axis (Y then X)
    [SerializeField] private float doorStepDuration  = 0.40f; // inside→outside

    [Header("Home speed")]
    [Tooltip("Base speed multiplier while the ghost is in Home (pacing).")]
    [SerializeField] private float homeSpeedMult = 0.75f;     // tweak to taste; 0.75 feels right

    private Ghost ghost;
    private Movement move;
    private GhostEyes eyes;

    private float launchTimer;
    private bool exiting;
    private bool queuedExit;
    private bool pacingApplied;
    private Vector2 initialPaceDir;

    public bool IsExiting => exiting;

    private int obstacleLayer;

    private void Awake()
    {
        ghost = GetComponent<Ghost>();
        move  = ghost.movement ?? GetComponent<Movement>();
        obstacleLayer = LayerMask.NameToLayer("Obstacle");
        eyes = GetComponent<GhostEyes>();
    }

    private void OnEnable()
    {
        if (!ghost || !move) return;

        exiting = false;
        queuedExit = false;
        pacingApplied = false;

        // Decide pacing direction now; apply when movement actually starts.
        initialPaceDir = (ghost.Type == GhostType.Pinky) ? Vector2.down : Vector2.up;

        // Reset the timer; start counting only after movement is enabled.
        launchTimer = launchDelaySeconds;

        // While in Home we:
        //  1) disable Movement pre-checks (collide + bounce via OnCollisionEnter2D)
        //  2) apply a fixed Home speed multiplier for consistent pacing
        move.SetObstacleMask(0);                // no pre-checks → let collisions bounce
        move.SetBaseSpeedMultiplier(homeSpeedMult);

        if (queuedExit) { queuedExit = false; BeginExit(); }
    }

    private void OnDisable()
    {
        if (move)
        {
            move.ClearObstacleMask();           // restore normal pre-checks
            move.SetBaseSpeedMultiplier(1f);    // clear home speed on disable (safety)
        }
        pacingApplied = false;
    }

    private void Update()
    {
        if (!ghost || !move) return;

        if (ghost.CurrentMode != Ghost.Mode.Home)
        {
            enabled = false;
            return;
        }

        // Wait for gameplay start
        if (!move.enabled) return;

        if (!pacingApplied)
        {
            move.SetDirection(initialPaceDir, true);
            pacingApplied = true;
        }

        if (!exiting)
        {
            launchTimer -= Time.deltaTime;
            if (launchTimer <= 0f) BeginExit();
        }
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!ghost || !move) return;
        if (ghost.CurrentMode != Ghost.Mode.Home || exiting) return;

        if (col.collider.gameObject.layer == obstacleLayer)
        {
            if (Mathf.Abs(move.direction.y) > Mathf.Abs(move.direction.x))
                move.SetDirection(-move.direction, true);
        }
    }

    private IEnumerator MoveToExact(Vector3 target, float duration)
    {
        var rb2d = move ? move.rb : null;
        Vector2 curr = rb2d ? rb2d.position : (Vector2)transform.position;
        float dist = Vector2.Distance(curr, (Vector2)target);
        float speed = (duration <= 0f) ? Mathf.Infinity : dist / duration;
        const float eps2 = 0.00004f;

        while (true)
        {
            curr = rb2d ? rb2d.position : (Vector2)transform.position;
            Vector2 diff = ((Vector2)target - curr);
            if (diff.sqrMagnitude <= eps2) break;

            // make the eyes “move”: face the dominant axis toward the target
            if (eyes)
            {
                Vector2 dir = (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
                            ? new Vector2(Mathf.Sign(diff.x), 0f)
                            : new Vector2(0f, Mathf.Sign(diff.y));
                eyes.LookAt(dir);  // updates the sprite instantly
            }

            Vector3 next = Vector3.MoveTowards(curr, target, speed * Time.deltaTime);
            if (rb2d) rb2d.MovePosition(next); else transform.position = next;

            yield return null;
        }

        if (rb2d) rb2d.position = target; else transform.position = target;
    }

    public void RequestExit()
    {
        if (exiting || ghost.CurrentMode != Ghost.Mode.Home) return;

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            queuedExit = true;
            return;
        }

        BeginExit();
    }

    private void BeginExit()
    {
        if (exiting) return;
        exiting = true;

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            queuedExit = true;
            exiting = false;
            return;
        }

        if (!insideDoor || !outsideDoor)
        {
            move.ClearObstacleMask();
            move.SetBaseSpeedMultiplier(1f);
            move.SetDirection(Vector2.up, true);
            ghost.SetMode(Ghost.Mode.Scatter);
            enabled = false;
            return;
        }

        StartCoroutine(ExitRoutine());
    }

    private IEnumerator ExitRoutine()
    {
        // pause physics & movement
        if (move.rb)
        {
    #if UNITY_2023_1_OR_NEWER
            move.rb.linearVelocity = Vector2.zero;
    #else
            move.rb.velocity = Vector2.zero;
    #endif
            move.rb.bodyType = RigidbodyType2D.Kinematic;
        }
        move.enabled = false;

        // 1) Align Y
        Vector3 now = transform.position;
        Vector3 yAligned = new Vector3(now.x, insideDoor.position.y, now.z);
        yield return MoveToExact(yAligned, alignStepDuration);

        // 2) Align X
        Vector3 xAligned = new Vector3(insideDoor.position.x, insideDoor.position.y, now.z);
        yield return MoveToExact(xAligned, alignStepDuration);

        // 3) Inside -> Outside
        yield return MoveToExact(outsideDoor.position, doorStepDuration);

        // outside: resume normal play
        ghost.SetMode(Ghost.Mode.Scatter);

        if (move.rb) move.rb.bodyType = RigidbodyType2D.Dynamic;
        move.enabled = true;

        var dir = ghost.ConsumeExitShouldGoRight() ? Vector2.right : Vector2.left;
        if (move.Occupied(dir)) dir = -dir;
        move.SetDirection(dir, true);

        enabled = false;
    }

    private IEnumerator LerpTo(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, duration);
        while (t < dur)
        {
            transform.position = Vector3.Lerp(start, target, t / dur);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = target;
    }
}
