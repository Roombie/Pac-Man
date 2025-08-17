using UnityEngine;

[RequireComponent(typeof(Ghost))]
public class GhostVisuals : MonoBehaviour
{
    [Header("Children")]
    [SerializeField] private GameObject eyes;
    [SerializeField] private GameObject body;
    [SerializeField] private GameObject fright;
    [SerializeField] private GameObject white; 

    [Header("Frightened")]
    [SerializeField] private float fallbackFrightenedSeconds = 6f;
    [SerializeField] private float flashThreshold = 2f;   // start flicker on x seconds of the end
    [SerializeField] private float flashInterval  = 0.4f; // 5 flickers before turning back to normal

    private Ghost ghost;
    private Ghost.Mode lastMode;

    private Timer frightenedTimer;
    private Timer flashTimer;       // alternate fright/white

    private bool whitePhase;

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

        // Control countdown and flicker during Frightened
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
        // Restart visual state and timers according to the new mode
        if (next == Ghost.Mode.Frightened)
        {
            StartFrightenedTimer(fallbackFrightenedSeconds);
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
        if (flashTimer != null) flashTimer = null;
        whitePhase = false;
    }

    private void ToggleFlash()
    {
        whitePhase = !whitePhase;
        SetActive(body:false, eyes:false, fright:!whitePhase, white:whitePhase);
        flashTimer.Reset(flashInterval);
    }

    private void KillFrightVisuals()
    {
        flashTimer = null;
        frightenedTimer = null;
        whitePhase = false;
        if (white) white.SetActive(false);
        if (fright) fright.SetActive(false);
    }

    private void SetActive(bool body, bool eyes, bool fright, bool white)
    {
        if (this.body)   this.body.SetActive(body);
        if (this.eyes)   this.eyes.SetActive(eyes);
        if (this.fright) this.fright.SetActive(fright);
        if (this.white)  this.white.SetActive(white);
    }

    public void OnFrightenedStart(float durationSeconds)
    {
        StartFrightenedTimer(durationSeconds);
        if (ghost.CurrentMode == Ghost.Mode.Frightened)
            SetActive(body:false, eyes:false, fright:true, white:false);
    }
}