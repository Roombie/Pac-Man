using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Reflection; // 👈 for reflective reads

public class GhostMiniHUDUI : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] GlobalGhostModeController controller;
    [SerializeField] TMP_Text textTarget;

    [Header("Behavior")]
    [SerializeField] bool startVisible = true;
    [SerializeField] bool showInReleaseBuild = true;  // if false, Editor/Dev builds only
    [SerializeField] KeyCode toggleKey = KeyCode.F3;
    [SerializeField] bool autoCreateUI = true;        // create Canvas + TMP if not assigned

    [Header("Auto-create UI layout")]
    [SerializeField] Vector2 anchoredPos = new Vector2(16, -16);
    [SerializeField] Vector2 size = new Vector2(900, 450);
    [SerializeField] int fontSize = 18;

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

        if (!textTarget && autoCreateUI) CreateRuntimeUI();
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
        if (!controller) { sb.AppendLine("No GlobalGhostModeController found."); return sb.ToString(); }

        // ---- Global mode summary (Frightened overrides Scatter/Chase) ----
        Ghost.Mode effectiveMode = Ghost.Mode.Scatter;
        float remaining = 0f;
        bool frozen = ReflectTimersFrozen(controller);

        if (controller.IsFrightenedActive)
        {
            effectiveMode = Ghost.Mode.Frightened;
            remaining = controller.FrightenedRemainingSeconds;
        }
        else
        {
            // Try to read the private currentPhaseMode and phaseTimer.RemainingSeconds
            if (!ReflectCurrentPhaseMode(controller, out effectiveMode))
            {
                // Fallback: infer from any outside ghost (not Home/Eaten)
                var inferred = InferOutsideMode(controller);
                if (inferred.HasValue) effectiveMode = inferred.Value;
            }
            remaining = ReflectPhaseRemainingSeconds(controller);
        }

        sb.AppendLine(
            $"Global Mode: <b>{effectiveMode}</b>   " +
            $"Remaining: {(remaining > 0f ? remaining.ToString("0.00") + "s" : "—")}   " +
            $"Frozen:{frozen}"
        );

        // Keep explicit frightened line if you like the visual cue
        if (controller.IsFrightenedActive)
            sb.AppendLine($"Frightened: <b>ON</b>  Remaining: {controller.FrightenedRemainingSeconds:0.00}s");
        else
            sb.AppendLine("Frightened: OFF");

        sb.AppendLine();

        // ---- Per-ghost ----
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

            // Rigidbody & Home state
            Rigidbody2D rb = mv ? mv.rb : null;
            var home = g.GetComponent<GhostHome>();
            bool exiting = home ? home.IsExiting : false;
            sb.AppendLine();
            sb.Append("  RB: ");
            if (rb)
                sb.Append($"sim:{rb.simulated} type:{rb.bodyType}");
            else
                sb.Append("none");

            sb.Append($"   HomeExiting:{exiting}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    void CreateRuntimeUI()
    {
        // Find existing canvas named "DebugHUD_Canvas" if any
        Canvas canvas = null;
        var found = GameObject.Find("DebugHUD_Canvas");
        if (found) canvas = found.GetComponent<Canvas>();

        if (!canvas)
        {
            var goCanvas = new GameObject("DebugHUD_Canvas");
            canvas = goCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            goCanvas.AddComponent<CanvasScaler>();
            goCanvas.AddComponent<GraphicRaycaster>();

            // Optional: keep through scene loads if you like
            // DontDestroyOnLoad(goCanvas);
        }

        // Create the TMP text holder if missing
        var goText = new GameObject("GhostMiniHUD_Text");
        goText.transform.SetParent(canvas.transform, false);

        var rect = goText.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        var tmp = goText.AddComponent<TextMeshProUGUI>();
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.fontSize = fontSize;
        tmp.text = "Ghost HUD…";

        textTarget = tmp;
    }

    // Public helpers if you want to control it from elsewhere
    public void SetVisible(bool v)
    {
        visible = v && AllowedToShow();
        ApplyVisibility();
    }

    public bool IsVisible() => visible;

    // ----------------- Helpers (colors & reflection) -----------------

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

    static bool ReflectCurrentPhaseMode(GlobalGhostModeController c, out Ghost.Mode mode)
    {
        mode = Ghost.Mode.Scatter;
        if (!c) return false;

        var fi = typeof(GlobalGhostModeController).GetField("currentPhaseMode",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (fi == null) return false;

        object val = fi.GetValue(c);
        if (val == null) return false;

        mode = (Ghost.Mode)val;
        return true;
    }

    static float ReflectPhaseRemainingSeconds(GlobalGhostModeController c)
    {
        if (!c) return 0f;

        var fi = typeof(GlobalGhostModeController).GetField("phaseTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (fi == null) return 0f;

        var timerObj = fi.GetValue(c);
        if (timerObj == null) return 0f;

        // Timer has a public RemainingSeconds property in your codebase
        var pi = timerObj.GetType().GetProperty("RemainingSeconds",
            BindingFlags.Instance | BindingFlags.Public);
        if (pi == null) return 0f;

        var val = pi.GetValue(timerObj, null);
        return val is float f ? f : 0f;
    }

    static bool ReflectTimersFrozen(GlobalGhostModeController c)
    {
        if (!c) return false;
        var fi = typeof(GlobalGhostModeController).GetField("timersFrozen",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (fi == null) return false;

        var val = fi.GetValue(c);
        return val is bool b && b;
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
}