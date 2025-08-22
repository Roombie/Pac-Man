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
    [SerializeField] private float flashInterval = 0.4f; // flicker cadence

    private Ghost ghost;
    private Ghost.Mode lastMode;
    private bool paused;
    private GlobalGhostModeController ctrl;

    void Awake()
    {
        ghost = GetComponent<Ghost>();
        ctrl = GameManager.Instance.globalGhostModeController;
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

        if (paused) return;

        // Deterministic, stateless flicker driven by controller time
        if (mode == Ghost.Mode.Frightened && ctrl != null && ctrl.IsFrightenedActive)
        {
            float remaining = ctrl.FrightenedRemainingSeconds;

            if (remaining > flashThreshold)
            {
                SetActive(body: false, eyes: false, fright: true, white: false);
            }
            else
            {
                float elapsedIntoFlash = flashThreshold - remaining;
                int phaseIndex = Mathf.FloorToInt(elapsedIntoFlash / flashInterval);
                bool showWhite = (phaseIndex % 2) == 1;

                SetActive(body: false, eyes: false, fright: !showWhite, white: showWhite);
            }
            return; // don't run any local-timer logic
        }
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
            KillFrightVisuals();
            SetActive(body: true, eyes: true, fright: false, white: false);
        }
    }

    private void ApplyForMode(Ghost.Mode mode)
    {
        if (mode == Ghost.Mode.Frightened)
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
    }

    private void KillFrightVisuals()
    {
        if (white) white.SetActive(false);
        if (fright) fright.SetActive(false);
    }

    private void SetActive(bool body, bool eyes, bool fright, bool white)
    {
        if (this.body) this.body.SetActive(body);
        if (this.eyes) this.eyes.SetActive(eyes);
        if (this.fright) this.fright.SetActive(fright);
        if (this.white) this.white.SetActive(white);
    }

    /// <summary>Called by controller when frightened starts; shows blue immediately.</summary>
    public void OnFrightenedStart()
    {
        SetActive(body: false, eyes: false, fright: true, white: false);
    }

    /// <summary>Freeze/unfreeze visual timers (used during 1s score pause & level clear).</summary>
    public void SetPaused(bool pause) => paused = pause;

    /// <summary>Hide all parts (for the 1s score popup), remembering previous state.</summary>
    public void HideAllForScore()
    {
        SetActive(false, false, false, false);
    }

    public void ShowForCurrentMode() => ApplyForMode(ghost ? ghost.CurrentMode : Ghost.Mode.Scatter);
}