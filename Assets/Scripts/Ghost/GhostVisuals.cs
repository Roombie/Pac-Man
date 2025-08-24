using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostVisuals : MonoBehaviour
{
    [Header("Ghost Parts")]
    [SerializeField] private GameObject eyes;
    [SerializeField] private GameObject body;
    [SerializeField] private GameObject fright; // blue
    [SerializeField] private GameObject white;  // flashing white

    [Header("Frightened")]
    [SerializeField] private float flashThreshold = 2f;   // start flicker when <= this many seconds remain
    [SerializeField] private float flashInterval = 0.4f;  // flicker cadence

    private Ghost ghost;
    private Ghost.Mode lastMode;
    private bool paused;
    private GlobalGhostModeController ctrl;

    // Track if we are currently rendering "frightened while Home"
    private bool frightWhileHomeActive = false;

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        lastMode = ghost ? ghost.CurrentMode : Ghost.Mode.Scatter;
        ApplyForMode(lastMode);
        BindControllerIfNeeded();  // safe in Awake
    }

    void Start()
    {
        // In case GameManager was created after our Awake
        BindControllerIfNeeded();
    }

    void Update()
    {
        if (!ghost) return;

        var mode = ghost.CurrentMode;
        if (mode != lastMode)
        {
            OnModeChanged(lastMode, mode);
            lastMode = mode;
        }

        if (paused) return;

        // Should we show frightened visuals *either* because the mode is Frightened,
        // *or* because we're in Home but the controller says this ghost is "home-frightened"?
        bool showHomeFright = (mode == Ghost.Mode.Home && ctrl != null && ctrl.ShowHomeFrightened(ghost));
        bool showAnyFright  = (mode == Ghost.Mode.Frightened) || showHomeFright;

        if (showAnyFright && ctrl != null && ctrl.IsFrightenedActive)
        {
            float remaining = ctrl.FrightenedRemainingSeconds;

            if (remaining > flashThreshold)
            {
                SetActive(body: false, eyes: false, fright: true, white: false);
            }
            else
            {
                float elapsedIntoFlash = flashThreshold - remaining;
                int   phaseIndex       = Mathf.FloorToInt(elapsedIntoFlash / flashInterval);
                bool  showWhite        = (phaseIndex % 2) == 1;

                SetActive(body: false, eyes: false, fright: !showWhite, white: showWhite);
            }

            // Remember whether we’re actively showing “frightened while Home”
            frightWhileHomeActive = showHomeFright;
            return;
        }

        // Not frightened anymore (or controller not active): if we were showing frightened-while-home,
        // restore visuals for the current mode.
        if (frightWhileHomeActive)
        {
            ApplyForMode(mode);
            frightWhileHomeActive = false;
        }
    }

    private void BindControllerIfNeeded()
    {
        if (ctrl) return;

        // Prefer the GameManager singleton if it's alive
        var gm = GameManager.Instance;
        if (gm) ctrl = gm.globalGhostModeController;
        if (ctrl) return;

        // Version-safe scene search fallback
#if UNITY_2023_1_OR_NEWER
        ctrl = Object.FindFirstObjectByType<GlobalGhostModeController>();
        if (!ctrl) ctrl = Object.FindAnyObjectByType<GlobalGhostModeController>();
#else
        ctrl = Object.FindObjectOfType<GlobalGhostModeController>();
#endif
    }

    private void OnModeChanged(Ghost.Mode prev, Ghost.Mode next)
    {
        if (next == Ghost.Mode.Frightened)
        {
            SetActive(body: false, eyes: false, fright: true, white: false);
        }
        else if (next == Ghost.Mode.Eaten)
        {
            KillFrightVisuals();
            SetActive(body: false, eyes: true, fright: false, white: false);
        }
        else // Scatter / Chase / Home
        {
            // If entering Home *while frightened is running* and controller marks us as home-frightened,
            // show blue in Home; otherwise normal body+eyes.
            bool homeFright = (next == Ghost.Mode.Home && ctrl != null && ctrl.ShowHomeFrightened(ghost));

            if (homeFright)
            {
                SetActive(body: false, eyes: false, fright: true, white: false);
                frightWhileHomeActive = true;
            }
            else
            {
                KillFrightVisuals();
                SetActive(body: true, eyes: true, fright: false, white: false);
                frightWhileHomeActive = false;
            }
        }
    }

    private void ApplyForMode(Ghost.Mode mode)
    {
        bool homeFright = (mode == Ghost.Mode.Home && ctrl != null && ctrl.ShowHomeFrightened(ghost));

        if (mode == Ghost.Mode.Frightened || homeFright)
        {
            SetActive(body: false, eyes: false, fright: true, white: false);
        }
        else if (mode == Ghost.Mode.Eaten)
        {
            KillFrightVisuals();
            SetActive(body: false, eyes: true, fright: false, white: false);
        }
        else
        {
            KillFrightVisuals();
            SetActive(body: true, eyes: true, fright: false, white: false);
        }

        frightWhileHomeActive = homeFright;
    }

    private void KillFrightVisuals()
    {
        if (white)  white.SetActive(false);
        if (fright) fright.SetActive(false);
    }

    private void SetActive(bool body, bool eyes, bool fright, bool white)
    {
        if (this.body)   this.body.SetActive(body);
        if (this.eyes)   this.eyes.SetActive(eyes);
        if (this.fright) this.fright.SetActive(fright);
        if (this.white)  this.white.SetActive(white);
    }

    public void OnFrightenedStart()
    {
        SetActive(body: false, eyes: false, fright: true, white: false);
    }

    public void SetPaused(bool pause) => paused = pause;

    public void HideAllForScore() => SetActive(false, false, false, false);

    public void ShowForCurrentMode() => ApplyForMode(ghost ? ghost.CurrentMode : Ghost.Mode.Scatter);
}