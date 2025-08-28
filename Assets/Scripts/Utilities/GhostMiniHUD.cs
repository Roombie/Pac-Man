using System.Text;
using UnityEngine;
using TMPro;

public class GhostMiniHUDUI : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] GlobalGhostModeController controller;
    [SerializeField] TMP_Text textTarget;

    [Header("Behavior")]
    [SerializeField] bool startVisible = true;
    [SerializeField] bool showInReleaseBuild = true;   // if false, Editor/Dev builds only
    [SerializeField] KeyCode toggleKey = KeyCode.F3;

    bool visible;

    void Awake()
    {
        if (!controller)
        {
            var gm = GameManager.Instance;
            if (gm && gm.globalGhostModeController) controller = gm.globalGhostModeController;
            if (!controller)
            {
#if UNITY_2023_1_OR_NEWER
                controller = Object.FindFirstObjectByType<GlobalGhostModeController>();
                if (!controller) controller = Object.FindAnyObjectByType<GlobalGhostModeController>();
#else
                controller = Object.FindObjectOfType<GlobalGhostModeController>();
#endif
            }
        }

        visible = startVisible && AllowedToShow();
        ApplyVisibility();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible && AllowedToShow();
            ApplyVisibility();
        }

        if (!visible || !textTarget) return;
        textTarget.text = BuildDebugText();
    }

    bool AllowedToShow()
    {
        if (showInReleaseBuild) return true;
        // Editor or Development Build only
        return Application.isEditor || Debug.isDebugBuild;
    }

    void ApplyVisibility()
    {
        if (!textTarget) return;
        var root = textTarget.gameObject;
        if (root) root.SetActive(visible);
    }

    string BuildDebugText()
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("<b><size=22>Ghost Debug</size></b>");
        sb.AppendLine($"TimeScale: {Time.timeScale:0.###}    FPS~: {1f / Mathf.Max(Time.smoothDeltaTime, 1e-5f):0}");

        if (!controller)
        {
            sb.AppendLine("No GlobalGhostModeController found.");
            return sb.ToString();
        }

        // --- Global summary (simple & reflection-free) ---
        // If frightened, show remaining time; else infer mode from any ghost outside Home/Eaten.
        string globalMode = "—";
        if (controller.IsFrightenedActive)
        {
            globalMode = "Frightened";
            sb.AppendLine($"Global Mode: <b>{globalMode}</b>   Remaining: {controller.FrightenedRemainingSeconds:0.00}s");
            sb.AppendLine("Frightened: <b>ON</b>");
        }
        else
        {
            var inferred = InferOutsideMode(controller);
            if (inferred.HasValue) globalMode = inferred.Value.ToString();
            sb.AppendLine($"Global Mode: <b>{globalMode}</b>");
            sb.AppendLine("Frightened: OFF");
        }

        sb.AppendLine();

        // Per-ghost
        var ghosts = controller.ghosts;
        if (ghosts == null || ghosts.Length == 0)
        {
            sb.AppendLine("No ghosts assigned on controller.");
            return sb.ToString();
        }

        for (int i = 0; i < ghosts.Length; i++)
        {
            var g = ghosts[i];
            if (!g) continue;

            // Colored name header
            string name = g.Type.ToString();
            string col = GetGhostHex(g.Type);
            sb.AppendLine($"<b><color=#{col}>{name}</color></b>");

            // Mode
            sb.Append("  Mode: ").Append(g.CurrentMode);

            // Movement info
            var mv = g.movement;
            if (mv)
            {
                float step = mv.speed * mv.speedMultiplier * mv.envMultiplier;
                sb.Append($"    Speed: {mv.speed:0.##}   Mult: base {mv.speedMultiplier:0.###} · env {mv.envMultiplier:0.###}  => step {step:0.###}/s");
                sb.AppendLine();
                sb.Append($"  Dir: ({mv.direction.x:0}, {mv.direction.y:0})   Next: ({mv.nextDirection.x:0}, {mv.nextDirection.y:0})");
                sb.Append($"   Blocked: {mv.isBlocked}");
                sb.Append($"   Enabled: {mv.enabled}");
            }
            else
            {
                sb.Append("    (no Movement)");
            }

            // Rigidbody & Home state (still useful for quick checks)
            // I needed it to detect issues with Inky
            Rigidbody2D rb = mv ? mv.rb : null;
            var home = g.GetComponent<GhostHome>();
            bool exiting = home ? home.IsExiting : false;
            sb.AppendLine();
            sb.Append("  RB: ").Append(rb ? $"sim:{rb.simulated} type:{rb.bodyType}" : "none");
            sb.Append($"   HomeExiting:{exiting}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    static string GetGhostHex(GhostType t)
    {
        // Classic-ish palette
        switch (t)
        {
            case GhostType.Blinky: return "FF0000"; // red
            case GhostType.Pinky:  return "FFB8FF"; // pink
            case GhostType.Inky:   return "00FFFF"; // cyan
            case GhostType.Clyde:  return "FFB852"; // orange
            default:               return "FFFFFF"; // fallback
        }
    }

    static Ghost.Mode? InferOutsideMode(GlobalGhostModeController c)
    {
        if (c == null || c.ghosts == null) return null;

        for (int i = 0; i < c.ghosts.Length; i++)
        {
            var g = c.ghosts[i];
            if (!g) continue;
            var m = g.CurrentMode;
            if (m != Ghost.Mode.Home && m != Ghost.Mode.Eaten)
                return m;
        }
        return null;
    }

    // Public helpers if you want to control it from elsewhere
    public void SetVisible(bool v)
    {
        visible = v && AllowedToShow();
        ApplyVisibility();
    }

    public bool IsVisible() => visible;
}