using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostVisuals : MonoBehaviour
{
    [Header("Children")]
    [SerializeField] private GameObject eyes;
    [SerializeField] private GameObject body;
    [SerializeField] private GameObject fright; // blue
    [SerializeField] private GameObject white;  // flashing white

    [Header("Frightened")]
    [SerializeField] private float fallbackFrightenedSeconds = 6f;
    [SerializeField] private float flashThreshold = 2f;   // start flicker when <= this many seconds remain
    [SerializeField] private float flashInterval  = 0.4f; // flicker cadence

    private Ghost ghost;
    private Ghost.Mode lastMode;

    // Visual-only timers (NOT the authoritative controller timers)
    private Timer frightenedTimer;   // counts down for flicker threshold
    private Timer flashTimer;        // toggles blue/white

    private bool whitePhase;
    private bool paused;

    // Saved state for score popup hide
    private bool savedBody, savedEyes, savedFright, savedWhite;
    private bool hasSavedState;

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        lastMode = ghost.CurrentMode;
        ApplyForMode(lastMode);
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

        if (paused) return; // freeze visuals (used during 1s ghost-score and level clear)

        // Drive frightened flicker locally (controller owns real frightened end)
        if (mode == Ghost.Mode.Frightened && frightenedTimer != null)
        {
            frightenedTimer.Tick(Time.deltaTime);

            if (frightenedTimer.RemainingSeconds <= flashThreshold)
            {
                if (flashTimer == null)
                {
                    flashTimer = new Timer(flashInterval);
                    flashTimer.OnTimerEnd += ToggleFlash;
                }
                flashTimer.Tick(Time.deltaTime);
            }
        }
    }

    private void OnModeChanged(Ghost.Mode prev, Ghost.Mode next)
    {
        // Reset visual state and local timers for the new mode
        if (next == Ghost.Mode.Frightened)
        {
            StartFrightenedTimer(fallbackFrightenedSeconds);
            SetActive(body: false, eyes: false, fright: true, white: false); // show blue immediately
        }
        else if (next == Ghost.Mode.Eaten)
        {
            KillFrightVisuals();
            SetActive(body: false, eyes: true, fright: false, white: false); // eyes only
        }
        else // Scatter / Chase / Home
        {
            KillFrightVisuals();
            SetActive(body: true, eyes: true, fright: false, white: false);  // normal
        }
    }

    private void ApplyForMode(Ghost.Mode mode)
    {
        if (mode == Ghost.Mode.Frightened)
        {
            StartFrightenedTimer(fallbackFrightenedSeconds);
            SetActive(body:false, eyes:false, fright:true, white:false);
        }
        else if (mode == Ghost.Mode.Eaten)
        {
            KillFrightVisuals();
            SetActive(body:false, eyes:true, fright:false, white:false);
        }
        else
        {
            KillFrightVisuals();
            SetActive(body:true, eyes:true, fright:false, white:false);
        }
    }

    private void StartFrightenedTimer(float seconds)
    {
        frightenedTimer = new Timer(seconds);
        flashTimer = null;     // will be created on demand at threshold
        whitePhase = false;
    }

    private void ToggleFlash()
    {
        // If we become paused exactly when the timer fires, skip toggling
        if (paused) return;

        whitePhase = !whitePhase;
        SetActive(body:false, eyes:false, fright:!whitePhase, white:whitePhase);
        flashTimer.Reset(flashInterval);
    }

    private void KillFrightVisuals()
    {
        flashTimer = null;
        frightenedTimer = null;
        whitePhase = false;

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

    // --- Public API ---

    /// <summary>Called by controller when frightened starts; shows blue immediately.</summary>
    public void OnFrightenedStart(float durationSeconds)
    {
        StartFrightenedTimer(durationSeconds);
        // Show blue RIGHT NOW (don’t depend on mode-change ordering)
        SetActive(body:false, eyes:false, fright:true, white:false);
    }

    /// <summary>Freeze/unfreeze visual timers (used during 1s score pause & level clear).</summary>
    public void SetPaused(bool pause) => paused = pause;

    /// <summary>Hide all parts (for the 1s score popup), remembering previous state.</summary>
    public void HideAllForScore()
    {
        if (!hasSavedState)
        {
            savedBody   = body   ? body.activeSelf   : false;
            savedEyes   = eyes   ? eyes.activeSelf   : false;
            savedFright = fright ? fright.activeSelf : false;
            savedWhite  = white  ? white.activeSelf  : false;
            hasSavedState = true;
        }
        SetActive(false, false, false, false);
    }

    /// <summary>Restore the visuals after HideAllForScore().</summary>
    public void ShowAfterScore()
    {
        if (hasSavedState)
        {
            SetActive(savedBody, savedEyes, savedFright, savedWhite);
            hasSavedState = false;
        }
        else
        {
            // Fallback if nothing saved
            ApplyForMode(ghost ? ghost.CurrentMode : Ghost.Mode.Scatter);
        }
    }
}