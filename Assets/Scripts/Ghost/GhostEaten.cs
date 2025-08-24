using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostEaten : MonoBehaviour
{
    [Header("Door targets (assign in Inspector)")]
    [SerializeField] private Transform insideDoor;   // just INSIDE the door
    [SerializeField] private Transform outsideDoor;  // just OUTSIDE the door

    [Header("Node steering")]
    [SerializeField, Tooltip("Max distance to a Node center to consider we're at the node and can commit a turn.")]
    private float commitRadius = 0.18f;

    [Header("Door transition (hand-move)")]
    [SerializeField, Tooltip("Seconds to move from gate node to OUTSIDE door point.")]
    private float outsideStepSeconds = 0.20f;
    [SerializeField, Tooltip("Seconds to move from OUTSIDE to INSIDE door point.")]
    private float insideStepSeconds = 0.20f;

    [Header("Layers")]
    [SerializeField] private string eyesLayerName = "GhostEyes";
    [SerializeField] private string ghostLayerName = "Ghost";

    private Ghost ghost;
    private Movement move;
    private GhostEyes eyes;
    private Node[] allNodes;
    private Node lastDecisionNode;
    private bool transitioning;

    private int originalLayer = -1;
    private int eyesLayer = -1;
    private int ghostLayer = -1;

    private static readonly Vector2[] TIE = { Vector2.up, Vector2.left, Vector2.down, Vector2.right };

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        move = ghost.movement ?? GetComponent<Movement>();
        eyes = GetComponent<GhostEyes>();
#if UNITY_2023_1_OR_NEWER
        allNodes = Object.FindObjectsByType<Node>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        allNodes = FindObjectsOfType<Node>(false);
#endif
        originalLayer = gameObject.layer;
        eyesLayer = LayerMask.NameToLayer(eyesLayerName);
        ghostLayer = LayerMask.NameToLayer(ghostLayerName);
        if (eyesLayer < 0) Debug.LogWarning($"[GhostEaten] Layer '{eyesLayerName}' not found.", this);
        if (ghostLayer < 0) Debug.LogWarning($"[GhostEaten] Layer '{ghostLayerName}' not found. Will restore to original layer ({originalLayer}).", this);
    }

    void OnEnable()
    {
        lastDecisionNode = null;
        transitioning = false;
        if (ghost && ghost.CurrentMode == Ghost.Mode.Eaten) ApplyEyesLayer();
    }

    void OnDisable()
    {
        RestoreGhostLayer();
    }

    void Update()
    {
        if (!ghost || !move) return;

        if (ghost.CurrentMode == Ghost.Mode.Eaten) ApplyEyesLayer();
        else
        {
            RestoreGhostLayer();
            return;
        }

        if (transitioning) return;
        if (!move.enabled) return;

        if (AtGateNode())
        {
            StartCoroutine(EnterDoorSequence());
            return;
        }

        var n = ClosestNodeWithin(commitRadius);
        if (!n || n == lastDecisionNode) return;

        Vector3 targetPos = GateTargetPos();
        Vector2 dir = BestDirAtNode(n, targetPos, move.direction);

        if (dir != Vector2.zero)
        {
            move.SetDirection(dir, true);
            move.rb?.WakeUp();
            lastDecisionNode = n;
        }
    }

    private Vector3 GateTargetPos()
    {
        if (!insideDoor && !outsideDoor) return transform.position;
        if (insideDoor && outsideDoor)
        {
            Vector2 p = move && move.rb ? move.rb.position : (Vector2)transform.position;
            float dOut = (p - (Vector2)outsideDoor.position).sqrMagnitude;
            float dIn = (p - (Vector2)insideDoor.position).sqrMagnitude;
            return dOut <= dIn ? outsideDoor.position : insideDoor.position;
        }
        return outsideDoor ? outsideDoor.position : insideDoor.position;
    }

    private bool AtGateNode()
    {
        if (!outsideDoor) return false;
        var gateNode = ClosestNodeTo((Vector2)outsideDoor.position);
        var here = ClosestNodeWithin(commitRadius);
        return gateNode && here && gateNode == here;
    }

    private Vector2 BestDirAtNode(Node node, Vector3 targetPos, Vector2 current)
    {
        if (!node) return Vector2.zero;

        IList<Vector2> options = node.availableDirections;
        Vector2 bestDir = Vector2.zero;
        float bestScore = float.PositiveInfinity;

        foreach (var d in TIE)
        {
            if (options == null || !options.Contains(d)) continue;
            if (current != Vector2.zero && d == -current) continue;

            Vector3 nextCenter = node.transform.position + (Vector3)d;
            float score = (nextCenter - targetPos).sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                bestDir = d;
            }
        }

        if (bestDir == Vector2.zero && current != Vector2.zero && options != null && options.Contains(-current))
            bestDir = -current;

        if (bestDir == Vector2.zero && options != null)
            foreach (var d in TIE) { if (options.Contains(d)) { bestDir = d; break; } }

        return bestDir;
    }

    private IEnumerator EnterDoorSequence()
    {
        transitioning = true;

        var rb2d = move ? move.rb : null;
        var prevBT = rb2d ? rb2d.bodyType : RigidbodyType2D.Dynamic;

        if (move) move.enabled = false;
        if (rb2d)
        {
            rb2d.linearVelocity = Vector2.zero;
            rb2d.bodyType = RigidbodyType2D.Kinematic;
        }

        if (outsideDoor) yield return MoveToExact(outsideDoor.position, outsideStepSeconds);
        if (insideDoor) yield return MoveToExact(insideDoor.position, insideStepSeconds);

        if (rb2d) rb2d.bodyType = prevBT;

        ghost.SetMode(Ghost.Mode.Home);

        if (move) move.enabled = true;
        var initial = ghost.Type == GhostType.Pinky ? Vector2.down : Vector2.up;
        if (move) move.SetDirection(initial, true);

        if (eyes) eyes.ClearOverrideFacing();

        RestoreGhostLayer();
        transitioning = false;
    }

    private IEnumerator MoveToExact(Vector3 target, float seconds)
    {
        var rb2d = move ? move.rb : null;
        Vector2 start = rb2d ? rb2d.position : (Vector2)transform.position;
        float dist = Vector2.Distance(start, (Vector2)target);
        float speed = seconds <= 0f ? Mathf.Infinity : dist / Mathf.Max(0.0001f, seconds);

        while (true)
        {
            Vector2 curr = rb2d ? rb2d.position : (Vector2)transform.position;
            Vector2 delta = (Vector2)target - curr;
            float d = delta.magnitude;
            if (d <= 0.0015f) break;

            if (eyes && delta != Vector2.zero) eyes.SetOverrideFacing(delta);

            Vector2 step = delta.normalized * speed * Time.deltaTime;
            if (step.sqrMagnitude > delta.sqrMagnitude) step = delta;

            Vector2 next = curr + step;
            if (rb2d) rb2d.position = next; else transform.position = next;
            yield return null;
        }

        if (rb2d) rb2d.position = target; else transform.position = target;
    }

    private void ApplyEyesLayer()
    {
        if (eyesLayer < 0) return;
        if (gameObject.layer == eyesLayer) return;
        SetLayer(gameObject, eyesLayer);
    }

    private void RestoreGhostLayer()
    {
        int target = ghostLayer >= 0 ? ghostLayer : originalLayer;
        if (target < 0) return;
        if (gameObject.layer == target) return;
        SetLayer(gameObject, target);
    }

    private static void SetLayer(GameObject go, int layer)
    {
        if (!go) return;
        go.layer = layer;

        var stack = new Stack<Transform>();
        stack.Push(go.transform);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            foreach (Transform child in t)
            {
                child.gameObject.layer = layer;
                stack.Push(child);
            }
        }
    }

    private Node ClosestNodeWithin(float radius)
    {
        if (allNodes == null || allNodes.Length == 0) return null;
        Vector2 p = move && move.rb ? move.rb.position : (Vector2)transform.position;
        Node best = null;
        float maxSq = radius * radius;

        for (int i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            float d2 = ((Vector2)n.transform.position - p).sqrMagnitude;
            if (d2 <= maxSq)
            {
                maxSq = d2;
                best = n;
            }
        }
        return best;
    }

    private Node ClosestNodeTo(Vector2 worldPos)
    {
        if (allNodes == null || allNodes.Length == 0) return null;
        Node best = null;
        float bestSq = float.PositiveInfinity;

        for (int i = 0; i < allNodes.Length; i++)
        {
            var n = allNodes[i];
            float d2 = ((Vector2)n.transform.position - worldPos).sqrMagnitude;
            if (d2 < bestSq)
            {
                bestSq = d2;
                best = n;
            }
        }
        return best;
    }
}