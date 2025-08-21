using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEaten : MonoBehaviour
{
    [Header("Door targets (assign in Inspector)")]
    [SerializeField] private Transform insideDoor;   // just INSIDE the door
    [SerializeField] private Transform outsideDoor;  // just OUTSIDE the door (street side)

    [Header("Node steering")]
    [SerializeField, Tooltip("Distance to consider we 'reached' a node center.")]
    private float commitRadius = 0.18f;

    [Header("Door transition (hand-move)")]
    [SerializeField, Tooltip("Time to move to the outsideDoor point (from the gate node).")]
    private float toOutsideDuration = 0.18f;
    [SerializeField, Tooltip("Time to move from outsideDoor to insideDoor.")]
    private float toInsideDuration = 0.22f;

    [Header("Layers")]
    [SerializeField] private string eyesLayerName = "GhostEyes";

    private Ghost ghost;
    private Movement move;
    private GhostEyes eyes;

    private Node[] allNodes;
    private Node lastDecisionNode;
    private Node gateNode;

    private bool entering;
    private Coroutine enterCo;

    private int originalLayer = -1;
    private int eyesLayer = -1;

    // Arcade tie-break: Up, Left, Down, Right
    private static readonly Vector2[] TIE = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move = ghost.movement ?? GetComponent<Movement>();
        eyes = GetComponent<GhostEyes>();

        allNodes = FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        eyesLayer = LayerMask.NameToLayer(eyesLayerName);
    }

    void OnEnable()
    {
        entering = false;
        lastDecisionNode = null;

        originalLayer = gameObject.layer;
        if (eyesLayer >= 0) gameObject.layer = eyesLayer;

        foreach (var c in GetComponents<CircleCollider2D>()) c.enabled = true;

        // Choose the gate node = nearest node to outsideDoor (or to us if door missing)
        Vector3 targetPos = outsideDoor ? outsideDoor.position : transform.position;
        gateNode = NearestNodeTo(targetPos);

        // If we spawn into Eaten with no heading, head toward gate right away
        if (move && move.direction == Vector2.zero)
            EmergencyPickTowardGate(forceTurn: true);
    }

    void OnDisable()
    {
        if (enterCo != null) { StopCoroutine(enterCo); enterCo = null; }
        entering = false;

        if (originalLayer >= 0) gameObject.layer = originalLayer;
    }

    void Update()
    {
        if (ghost.CurrentMode != Ghost.Mode.Eaten) return;
        if (entering) return;

        // 1) Start door transition ONLY when we reach the gate node center
        if (gateNode && Near(gateNode.transform.position, commitRadius))
        {
            BeginEnterDoor();
            return;
        }

        // 2) Node-based steering toward gate
        var n = ClosestNodeWithin(commitRadius);
        if (n && n != lastDecisionNode)
        {
            DecideAtNode(n, forceTurn: true);
            lastDecisionNode = n;
        }
        else
        {
            if (move && move.direction != Vector2.zero && move.Occupied(move.direction))
                EmergencyPickTowardGate(forceTurn: true);
        }
    }

    private void DecideAtNode(Node node, bool forceTurn)
    {
        if (!node || move == null) return;

        IList<Vector2> options = node.availableDirections;
        Vector3 targetPos = GateTargetPos();

        Vector2 current = move.direction;
        Vector2 bestDir = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in TIE)
        {
            if (!options.Contains(d)) continue;
            if (current != Vector2.zero && d == -current) continue;

            Vector3 nextCenter = node.transform.position + (Vector3)d;
            float score = (nextCenter - targetPos).sqrMagnitude;
            if (score < bestScore) { bestScore = score; bestDir = d; }
        }

        if (bestDir == Vector2.zero && current != Vector2.zero && options.Contains(-current))
            bestDir = -current;

        if (bestDir == Vector2.zero)
            foreach (var d in TIE) { if (options.Contains(d)) { bestDir = d; break; } }

        if (bestDir != Vector2.zero)
        {
            move.SetDirection(bestDir, forced: forceTurn || current == Vector2.zero);
            if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
        }
    }

    private void EmergencyPickTowardGate(bool forceTurn)
    {
        if (move == null) return;

        Vector3 targetPos = GateTargetPos();
        Vector2 current = move.direction;
        Vector2 bestDir = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in TIE)
        {
            if (current != Vector2.zero && d == -current) continue;
            if (move.Occupied(d)) continue;

            Vector3 nextPos = (Vector2)transform.position + d;
            float score = (nextPos - targetPos).sqrMagnitude;
            if (score < bestScore) { bestScore = score; bestDir = d; }
        }

        if (bestDir == Vector2.zero && current != Vector2.zero && !move.Occupied(-current))
            bestDir = -current;

        if (bestDir != Vector2.zero)
        {
            move.SetDirection(bestDir, forced: forceTurn || current == Vector2.zero);
            if (move.rb) { move.rb.simulated = true; move.rb.WakeUp(); }
        }
    }

    private Vector3 GateTargetPos()
    {
        if (gateNode)    return gateNode.transform.position;
        if (outsideDoor) return outsideDoor.position;
        return transform.position; // fallback
    }

    private void BeginEnterDoor()
    {
        if (entering) return;
        if (!outsideDoor || !insideDoor) { ghost.SetMode(Ghost.Mode.Home); return; }

        entering = true;
        if (enterCo != null) StopCoroutine(enterCo);
        enterCo = StartCoroutine(EnterDoorRoutine());
    }

    private System.Collections.IEnumerator EnterDoorRoutine()
    {
        if (move && move.rb)
        {
#if UNITY_2023_1_OR_NEWER
            move.rb.linearVelocity = Vector2.zero;
#else
            move.rb.velocity = Vector2.zero;
#endif
            move.rb.bodyType = RigidbodyType2D.Kinematic;
        }
        if (move) move.enabled = false;

        // A) gate node -> outsideDoor (exact)
        FaceToward(outsideDoor.position);
        yield return MoveToExact(outsideDoor.position, toOutsideDuration);

        // B) outsideDoor -> insideDoor
        FaceToward(insideDoor.position);
        yield return MoveToExact(insideDoor.position, toInsideDuration);

        // Switch to Home; Home script handles bounce/exit later
        ghost.SetMode(Ghost.Mode.Home);

        if (originalLayer >= 0) gameObject.layer = originalLayer;

        if (move && move.rb) move.rb.bodyType = RigidbodyType2D.Dynamic;
        if (move) move.enabled = true;

        var initial = (ghost.Type == GhostType.Pinky) ? Vector2.down : Vector2.up;
        if (move) move.SetDirection(initial, forced: true);

        if (eyes) eyes.ClearOverrideFacing();

        entering = false;
        enterCo = null;
        enabled = false; // Eaten done
    }

    private bool Near(Vector3 p, float r)
    {
        Vector2 a = CurrentPos();
        Vector2 b = p;
        return (a - b).sqrMagnitude <= r * r;
    }

    private Vector2 CurrentPos() => (move && move.rb) ? move.rb.position : (Vector2)transform.position;

    private void FaceToward(Vector3 target)
    {
        if (!eyes) return;
        Vector2 diff = ((Vector2)target - CurrentPos());
        if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.y))
            eyes.SetOverrideFacing(new Vector2(Mathf.Sign(diff.x), 0f));
        else
            eyes.SetOverrideFacing(new Vector2(0f, Mathf.Sign(diff.y)));
    }

    private System.Collections.IEnumerator MoveToExact(Vector3 target, float duration)
    {
        var rb2d = move ? move.rb : null;
        Vector2 curr = CurrentPos();
        float dist  = Vector2.Distance(curr, (Vector2)target);
        float speed = (duration <= 0f) ? Mathf.Infinity : dist / Mathf.Max(0.0001f, duration);
        const float eps2 = 0.00004f;

        while (true)
        {
            curr = CurrentPos();
            Vector2 diff = ((Vector2)target - curr);
            if (diff.sqrMagnitude <= eps2) break;

            FaceToward(target);

            Vector3 next = Vector3.MoveTowards(curr, target, speed * Time.deltaTime);
            if (rb2d) rb2d.MovePosition(next); else transform.position = next;
            yield return null;
        }

        if (rb2d) rb2d.position = target; else transform.position = target;
    }

    private Node ClosestNodeWithin(float radius)
    {
        if (allNodes == null || allNodes.Length == 0) return null;

        Vector2 p = CurrentPos();
        Node best = null;
        float maxSq = radius * radius;

        for (int i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            float d2 = ((Vector2)n.transform.position - p).sqrMagnitude;
            if (d2 <= maxSq) { maxSq = d2; best = n; }
        }
        return best;
    }

    private Node NearestNodeTo(Vector3 worldPos)
    {
        if (allNodes == null || allNodes.Length == 0) return null;

        Node best = null;
        float bestSq = float.PositiveInfinity;
        Vector2 w = worldPos;

        for (int i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            float d2 = ((Vector2)n.transform.position - w).sqrMagnitude;
            if (d2 < bestSq) { bestSq = d2; best = n; }
        }
        return best;
    }
}