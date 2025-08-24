using UnityEngine;
using System;
using System.Collections.Generic;

[AddComponentMenu("Pacman/Global Ghost Mode Controller")]
[DisallowMultipleComponent]
public class GlobalGhostModeController : MonoBehaviour
{
    [Header("Targets")]
    public Ghost[] ghosts;

    [Header("Phase schedule (fallback if no Matrix)")]
    [SerializeField] private GhostPhase[] phases;

    [Header("Level-based Matrix")]
    [SerializeField] private GhostModeScheduleMatrix matrix;
    private int currentLevel = 1;
    private float previewFrightenedSeconds = 6f;
    private Timer phaseTimer;
    private int phaseIndex = -1;
    private Ghost.Mode currentPhaseMode;
    private Timer frightenedTimer;
    private bool isFrightenedActive;

    [Header("Gates")]
    [SerializeField] private bool houseReleaseEnabled = false;

    [Header("Mode speed multipliers")]
    [SerializeField, Tooltip("Scatter/Chase relative to base (incl. Elroy).")]
    private float scatterChaseMult = 1f;

    [SerializeField, Tooltip("Frightened speed multiplier (blue).")]
    private float frightenedMult = 0.50f;

    [SerializeField, Tooltip("Eyes (Eaten) speed multiplier.")]
    private float eatenMult = 1.5f;

    [Header("Pause/Freeze")]
    [SerializeField] private bool timersFrozen = false;
    public bool IsFrightenedActive => isFrightenedActive;
    public float FrightenedRemainingSeconds =>
        (isFrightenedActive && frightenedTimer != null) ? frightenedTimer.RemainingSeconds : 0f;

    // House exit params (loaded from matrix per level band)
    private int exitPinky = 0, exitInky = 0, exitClyde = 0;
    private float exitNoDotSeconds = 4f;

    // Runtime counters/timer
    private float noDotTimer = 0f;        // seconds since last dot
    private int personalDotCounter = 0;   // counts dots toward the preferred Home ghost

    // Global house counter mode (after life lost)
    private bool useGlobalCounter = false;
    private int globalDotCounter = 0;

    // Eyes audio control
    private int eatenActiveCount = 0;
    [SerializeField] private bool eyesAudioAllowed = true;

    [Header("Frightened options")]
    [Tooltip("If active, ghosts within Home are also affected (slower + blue/white visuals) during Frightened.")]
    [SerializeField] private bool affectHomeGhostsDuringFrightened = false;

    // Track who we slowed while in Home during Frightened (and painted blue)
    private readonly HashSet<Ghost> homeFrightened = new HashSet<Ghost>();
    // Track who re-formed from Eyes → Home while Frightened is active; they should NOT enter Frightened when exiting
    private readonly HashSet<Ghost> reformedFromEaten = new HashSet<Ghost>();

    void Awake()
    {
        ForEachGhost(g =>
        {
            if (!g) return;
            g.ModeChanged -= OnGhostModeChanged;
            g.ModeChanged += OnGhostModeChanged;
        });

        // Initial eaten count + audio state
        eatenActiveCount = 0;
        ForEachGhost(g => { if (g && g.CurrentMode == Ghost.Mode.Eaten) eatenActiveCount++; });
        UpdateEyesAudio();
    }

    void OnDestroy()
    {
        ForEachGhost(g => { if (g) g.ModeChanged -= OnGhostModeChanged; });
    }

    private void Start() => ApplyLevel(currentLevel);

    private void Update()
    {
        // Do NOT tick any timers while frozen (e.g., during READY intro)
        if (timersFrozen) return;

        if (!isFrightenedActive && phaseTimer != null) phaseTimer.Tick(Time.deltaTime);
        if (isFrightenedActive && frightenedTimer != null) frightenedTimer.Tick(Time.deltaTime);

        // Stall timer for house release (only while someone is inside Home)
        if (houseReleaseEnabled && !useGlobalCounter && GetPreferredHomeGhost() != null)
        {
            noDotTimer += Time.deltaTime;
            TryReleaseByStall();
        }
        else if (!useGlobalCounter)
        {
            // Only reset these counters when we're not frozen
            noDotTimer = 0f;
            personalDotCounter = 0;
        }
    }

    public void ApplyLevel(int level)
    {
        currentLevel = Mathf.Max(1, level);

        if (matrix != null)
        {
            float fr;
            var built = matrix.GetPhasesForLevel(currentLevel, out fr);
            previewFrightenedSeconds = fr;

            var hx = matrix.GetHouseExitForLevel(currentLevel);
            exitPinky = hx.pinky;
            exitInky = hx.inky;
            exitClyde = hx.clyde;
            exitNoDotSeconds = hx.noDotSeconds;

            ResetSchedule(built);
#if UNITY_EDITOR
            Debug.Log($"[GhostModes] {matrix.GetBandLabelForLevel(currentLevel)}  Exit(P:{exitPinky}, I:{exitInky}, C:{exitClyde}, NoDot:{exitNoDotSeconds:0.##}s)");
#endif
        }
        else
        {
            ResetSchedule(phases);
            exitPinky = 0;
            exitInky = (currentLevel <= 1) ? 30 : 0;
            exitClyde = (currentLevel == 1) ? 60 : (currentLevel == 2 ? 50 : 0);
            exitNoDotSeconds = (currentLevel <= 4) ? 4f : 3f;
        }

        // reset counters at level start
        useGlobalCounter = false;
        globalDotCounter = 0;
        noDotTimer = 0f;
        personalDotCounter = 0;
        homeFrightened.Clear();
        reformedFromEaten.Clear();
    }

    public void ApplyModeSpeed(Ghost g, Ghost.Mode mode)
    {
        if (!g || !g.movement) return;

        float m =
            (mode == Ghost.Mode.Frightened) ? frightenedMult :
            (mode == Ghost.Mode.Eaten) ? eatenMult :
            scatterChaseMult;

        g.movement.SetBaseSpeedMultiplier(m);
    }

    private void SetGhostAnimatorsSpeed(float speed)
    {
        ForEachGhost(ghost =>
        {
            if (!ghost) return;

            foreach (var a in ghost.GetComponentsInChildren<Animator>(true))
                a.speed = speed;

            var animSprites = ghost.GetComponentsInChildren<AnimatedSprite>(true);
            foreach (var spr in animSprites)
            {
                if (Mathf.Approximately(speed, 0f)) spr.PauseAnimation();
                else spr.ResumeAnimation();
            }
        });
    }

    public void PauseAllGhostAnimations() => SetGhostAnimatorsSpeed(0f);
    public void ResumeAllGhostAnimations() => SetGhostAnimatorsSpeed(1f);

    public void SetHomeExitsPaused(bool paused)
    {
        ForEachGhost(g =>
        {
            if (!g) return;
            var home = g.GetComponent<GhostHome>();
            if (home) home.SetExitPaused(paused);
        });
    }

    public void SetTimersFrozen(bool frozen)
    {
        timersFrozen = frozen;

        // Pause visual flicker & anims so they freeze too
        ForEachGhost(ghost =>
        {
            var vis = ghost ? ghost.GetComponent<GhostVisuals>() : null;
            if (vis) vis.SetPaused(frozen);
        });

        if (frozen) PauseAllGhostAnimations();
        else ResumeAllGhostAnimations();
    }

    private void OnGhostModeChanged(Ghost g, Ghost.Mode prev, Ghost.Mode next)
    {
        if (prev != Ghost.Mode.Eaten && next == Ghost.Mode.Eaten) eatenActiveCount++;
        if (prev == Ghost.Mode.Eaten && next != Ghost.Mode.Eaten) eatenActiveCount = Mathf.Max(0, eatenActiveCount - 1);
        UpdateEyesAudio();

        // Eyes → Home while Frightened is active ⇒ mark so we DON'T re-enter Frightened on exit
        if (prev == Ghost.Mode.Eaten && next == Ghost.Mode.Home && isFrightenedActive)
            reformedFromEaten.Add(g);
    }

    /// <summary>Allow or suppress the "eyes returning" loop. Use from GameManager during death/ready.</summary>
    public void SetEyesAudioAllowed(bool allowed)
    {
        eyesAudioAllowed = allowed;
        UpdateEyesAudio();
    }

    /// <summary>Start/stop the eyes loop based on allowance + active eaten count.</summary>
    private void UpdateEyesAudio()
    {
        if (!AudioManager.Instance) return;

        if (!eyesAudioAllowed)
        {
            AudioManager.Instance.Stop(AudioManager.Instance.eyes);
            return;
        }

        if (eatenActiveCount > 0)
        {
            if (!AudioManager.Instance.IsPlaying(AudioManager.Instance.eyes))
            {
                AudioManager.Instance.Play(AudioManager.Instance.eyes, SoundCategory.SFX, 1f, 1f, loop: true);
            }
        }
        else
        {
            AudioManager.Instance.Stop(AudioManager.Instance.eyes);
        }
    }

    /// <summary>Used by GhostVisuals to know if a Home ghost should render blue/white.</summary>
    public bool ShowHomeFrightened(Ghost g)
    {
        return isFrightenedActive
            && affectHomeGhostsDuringFrightened
            && homeFrightened.Contains(g);
    }

    public void TriggerFrightened(float? duration = null)
    {
        SetEyesAudioAllowed(true);
        float dur = duration ?? previewFrightenedSeconds;

        if (!isFrightenedActive)
            ReverseAll(skipHome: true);

        isFrightenedActive = true;
        if (frightenedTimer == null) frightenedTimer = new Timer(dur);
        else frightenedTimer.Reset(dur);

        frightenedTimer.OnTimerEnd -= EndFrightened;
        frightenedTimer.OnTimerEnd += EndFrightened;

        ForEachGhost(g =>
        {
            if (!g) return;

            // Home ghosts → slow + paint (optional), but DO NOT change mode
            if (g.CurrentMode == Ghost.Mode.Home)
            {
                if (affectHomeGhostsDuringFrightened && g.movement)
                {
                    g.movement.SetEnvSpeedMultiplier(frightenedMult);
                    homeFrightened.Add(g);

                    // ensure visuals go blue and will flicker as time runs out
                    var vis = g.GetComponent<GhostVisuals>();
                    if (vis) vis.OnFrightenedStart();
                }
                return;
            }

            // Outside Home: normal Frightened (skip Eyes)
            if (g.CurrentMode == Ghost.Mode.Eaten) return;

            g.SetMode(Ghost.Mode.Frightened);
            ApplyModeSpeed(g, Ghost.Mode.Frightened);

            var v = g.GetComponent<GhostVisuals>();
            if (v) v.OnFrightenedStart();
        });
    }

    public void ResetSchedule(GhostPhase[] newPhases)
    {
        phases = newPhases ?? Array.Empty<GhostPhase>();
        phaseIndex = -1;
        phaseTimer = null;
        AdvancePhase(initial: true);
    }

    private void EnsureExit(Ghost ghost)
    {
        if (!ghost) return;
        if (!houseReleaseEnabled) return;
        if (!ghost.gameObject.activeInHierarchy) return;

        var home = ghost.GetComponent<GhostHome>();
        if (!home) return;

        home.RequestExit();
    }

    public void ResetAllGhosts()
    {
        ForEachGhost(g =>
        {
            if (!g) return;
            var startMode = (g.Type == GhostType.Blinky) ? currentPhaseMode : Ghost.Mode.Home;
            g.ResetStateTo(startMode);
        });

        useGlobalCounter = false;
        globalDotCounter = 0;
        noDotTimer = 0f;
        personalDotCounter = 0;
        eatenActiveCount = 0;
        homeFrightened.Clear();
        reformedFromEaten.Clear();
        UpdateEyesAudio();
    }

    public void ActivateAllGhosts()
    {
        isFrightenedActive = false;
        frightenedTimer = null;

        eatenActiveCount = 0;
        UpdateEyesAudio();

        ForEachGhost(g =>
        {
            if (!g) return;

            if (!g.gameObject.activeSelf)
                g.gameObject.SetActive(true);

            var startMode = (g.Type == GhostType.Blinky)
                ? currentPhaseMode
                : Ghost.Mode.Home;

            g.ResetStateTo(startMode);
        });

        useGlobalCounter = false;
        globalDotCounter = 0;
        noDotTimer = 0f;
        personalDotCounter = 0;
        homeFrightened.Clear();
        reformedFromEaten.Clear();
    }

    private System.Collections.IEnumerator ClearEyesOverrideNextFrame(GhostEyes eyes)
    {
        // wait one frame so Movement has its seeded direction
        yield return null;
        if (eyes) eyes.ClearOverrideFacing();
    }

    public void StartAllGhosts()
    {
        ForEachGhost(g =>
        {
            if (!g) return;

            // make sure the GO is active
            if (!g.gameObject.activeSelf)
                g.gameObject.SetActive(true);

            var home = g.GetComponent<GhostHome>();
            bool isExitingHome = home && home.IsExiting;
            var eyes = g.GetComponent<GhostEyes>();

            if (g.movement)
            {
                if (g.CurrentMode != Ghost.Mode.Home && g.CurrentMode != Ghost.Mode.Eaten)
                {
                    // Outside ghosts: seed LEFT (arcade), fallback to RIGHT if blocked
                    var seedDir = Vector2.left;
                    if (g.movement.Occupied(seedDir)) seedDir = -seedDir;

                    g.movement.SetDirection(seedDir, forced: true);
                    if (eyes) eyes.ResetEyes(seedDir);   // keep visuals consistent on first frame
                }
                else if (g.CurrentMode == Ghost.Mode.Home && !isExitingHome)
                {
                    // Inside Home (not exiting): seed pacing direction + eyes
                    var init = (g.Type == GhostType.Pinky) ? Vector2.down : Vector2.up;
                    g.movement.SetDirection(init, forced: true);
                    if (eyes) eyes.ResetEyes(init);
                }
            }

            // Only enable Movement if we are NOT in the scripted exit
            if (g.movement)
                g.movement.enabled = !isExitingHome;

            // enable colliders
            var cols = g.GetComponents<CircleCollider2D>();
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = true;

            // resume anims
            ResumeAllGhostAnimations();

            // Apply global phase only to outside, non-eyes ghosts (when not frightened)
            if (!isFrightenedActive && g.CurrentMode != Ghost.Mode.Home && g.CurrentMode != Ghost.Mode.Eaten)
            {
                g.SetMode(currentPhaseMode);
                ApplyModeSpeed(g, currentPhaseMode);
            }

            // If resuming Home behaviour and not exiting, pacing direction was already seeded above

            // make sure exit routine (if any) isn't paused
            if (home) home.SetExitPaused(false);

            // visuals component on
            if (eyes) eyes.enabled = true;
        });
    }

    public void DeactivateAllGhosts()
    {
        isFrightenedActive = false;
        frightenedTimer = null;

        eatenActiveCount = 0;
        UpdateEyesAudio();

        ForEachGhost(g =>
        {
            if (g && g.gameObject.activeSelf) g.gameObject.SetActive(false);
        });
    }

    public void StopAllGhosts(bool disableColliders = true, bool zeroDirection = true, bool pauseHomeExit = false)
    {
        ForEachGhost(g =>
        {
            if (!g) return;

            var eyes = g.GetComponent<GhostEyes>();
            if (eyes) eyes.enabled = false;

            if (g.movement)
            {
                if (zeroDirection)
                    g.movement.SetDirection(Vector2.zero, true);

                if (g.movement.rb) g.movement.rb.linearVelocity = Vector2.zero;
                g.movement.enabled = false;
            }

            PauseAllGhostAnimations();

            if (disableColliders)
            {
                var cols = g.GetComponents<CircleCollider2D>();
                foreach (var c in cols) c.enabled = false;
            }

            if (pauseHomeExit)
            {
                var home = g.GetComponent<GhostHome>();
                if (home) home.SetExitPaused(true);
            }
        });

        eatenActiveCount = 0;
        UpdateEyesAudio();
    }

    private void AdvancePhase(bool initial = false)
    {
        var prevMode = currentPhaseMode;

        phaseIndex++;
        if (phaseIndex >= phases.Length) phaseIndex = phases.Length - 1;

        var phase = (phases.Length > 0)
            ? phases[phaseIndex]
            : new GhostPhase { mode = Ghost.Mode.Chase, durationSeconds = 0f };

        currentPhaseMode = phase.mode;

        phaseTimer = (phase.durationSeconds > 0f) ? new Timer(phase.durationSeconds) : null;
        if (phaseTimer != null) phaseTimer.OnTimerEnd += OnPhaseTimerEnd;

        // Reverse only when Scatter <-> Chase flips (not on initial)
        if (!initial && IsScatterChasePair(prevMode, currentPhaseMode))
        {
            // mark flips for ghosts currently in Home (for exit direction)
            ForEachGhost(g => { if (g.CurrentMode == Ghost.Mode.Home) g.NotifyModeFlipWhileHome(); });

            ReverseAll();
        }

        if (!isFrightenedActive)
            SetAll(currentPhaseMode);
    }

    private void OnPhaseTimerEnd() => AdvancePhase();

    private void EndFrightened()
    {
        isFrightenedActive = false;
        frightenedTimer = null;

        // restore Home slowdowns (optional feature)
        foreach (var g in homeFrightened)
            if (g && g.movement) g.movement.SetEnvSpeedMultiplier(1f);

        homeFrightened.Clear();
        reformedFromEaten.Clear();

        SetAll(currentPhaseMode);
    }

    public void OnGhostExitedHomeDoor(Ghost g)
    {
        if (!g) return;

        // Clear any Home slowdown we applied
        if (homeFrightened.Remove(g) && g.movement)
            g.movement.SetEnvSpeedMultiplier(1f);

        // decide if this ghost should enter Frightened on exit
        bool shouldEnterFrightened =
            isFrightenedActive &&
            affectHomeGhostsDuringFrightened &&
            !reformedFromEaten.Contains(g); // eyes→home re-forms do NOT become frightened

        if (shouldEnterFrightened)
        {
            g.SetMode(Ghost.Mode.Frightened);
            ApplyModeSpeed(g, Ghost.Mode.Frightened);

            // ensure visuals are blue + flicker after exiting
            var vis = g.GetComponent<GhostVisuals>();
            if (vis) vis.OnFrightenedStart();
        }
        else
        {
            g.SetMode(currentPhaseMode);
            ApplyModeSpeed(g, currentPhaseMode);
        }

        // once used, clear the re-formed flag
        reformedFromEaten.Remove(g);
    }

    private static bool IsScatterChasePair(Ghost.Mode a, Ghost.Mode b) =>
        (a == Ghost.Mode.Scatter && b == Ghost.Mode.Chase) ||
        (a == Ghost.Mode.Chase && b == Ghost.Mode.Scatter);

    private void SetAll(Ghost.Mode mode)
    {
        ForEachGhost(g =>
        {
            if (g.CurrentMode == Ghost.Mode.Eaten || g.CurrentMode == Ghost.Mode.Home) return;
            g.SetMode(mode);
            ApplyModeSpeed(g, mode);
        });
    }

    private void ReverseAll(bool skipHome = false)
    {
        ForEachGhost(g =>
        {
            if (!g || g.movement == null) return;
            if (skipHome && g.CurrentMode == Ghost.Mode.Home) return;

            var dir = g.movement.direction;
            if (dir != Vector2.zero)
                g.movement.SetDirection(-dir, forced: true);
        });
    }

    private void ForEachGhost(Action<Ghost> action)
    {
        if (ghosts == null) return;
        foreach (var g in ghosts)
            if (g != null) action(g);
    }

    public void ResetElroy()
    {
        if (ghosts == null) return;
        foreach (var g in ghosts)
        {
            if (g && g.Type == GhostType.Blinky)
            {
                g.SetElroyStage(0, 1f);
                break;
            }
        }
    }

    /// <summary>Call with pellets remaining to compute Elroy stage.</summary>
    public void OnPelletCountChanged(int pelletsRemaining)
    {
        if (ghosts == null) return;

        Ghost blinky = null;
        foreach (var g in ghosts)
            if (g && g.Type == GhostType.Blinky) { blinky = g; break; }
        if (!blinky) return;

        int stage = (pelletsRemaining <= 10) ? 2 :
                    (pelletsRemaining <= 20) ? 1 : 0;

        float mult = (stage == 2) ? (0.85f / 0.75f) :
                     (stage == 1) ? (0.80f / 0.75f) : 1f;

        blinky.SetElroyStage(stage, mult);
    }

    /// <summary>Invoke whenever Pac-Man eats a pellet.</summary>
    public void OnPelletEaten()
    {
        if (!houseReleaseEnabled) return;

        if (useGlobalCounter)
        {
            globalDotCounter++;
            TryReleaseByGlobalCounter();
            return;
        }

        noDotTimer = 0f;
        personalDotCounter++;
        TryReleaseByCounter();
    }

    /// <summary>Call when a life is lost (before resuming control).</summary>
    public void StartGlobalHouseCounterMode()
    {
        useGlobalCounter = true;
        globalDotCounter = 0;
        // personal counters are disabled in this mode
        noDotTimer = 0f;
        personalDotCounter = 0;
    }

    /// <summary>Call when global counting should end (e.g., after Clyde leaves or no one remains in Home).</summary>
    public void StopGlobalHouseCounterMode()
    {
        useGlobalCounter = false;
        globalDotCounter = 0;
        noDotTimer = 0f;
        personalDotCounter = 0;
    }

    private Ghost GetPreferredHomeGhost()
    {
        Ghost pinky = null, inky = null, clyde = null;
        ForEachGhost(g =>
        {
            if (!g) return;
            if (!g.gameObject.activeInHierarchy) return; // avoid counting deactivated ghosts
            if (g.CurrentMode != Ghost.Mode.Home) return;
            if (g.Type == GhostType.Pinky) pinky = g;
            else if (g.Type == GhostType.Inky) inky = g;
            else if (g.Type == GhostType.Clyde) clyde = g;
        });
        if (pinky) return pinky;
        if (inky) return inky;
        if (clyde) return clyde;
        return null;
    }

    private Ghost GetHomeGhostByType(GhostType type)
    {
        Ghost found = null;
        ForEachGhost(g => { if (g && g.Type == type && g.CurrentMode == Ghost.Mode.Home) found = g; });
        return found;
    }

    public void SetHouseReleaseEnabled(bool enabled)
    {
        houseReleaseEnabled = enabled;
        // reset counters whenever we flip the gate
        noDotTimer = 0f;
        personalDotCounter = 0;
        if (!enabled) { useGlobalCounter = false; globalDotCounter = 0; }
    }

    private int PersonalDotLimit(Ghost g)
    {
        if (!g) return 0;
        return g.Type switch
        {
            GhostType.Pinky => exitPinky,
            GhostType.Inky => exitInky,
            GhostType.Clyde => exitClyde,
            _ => 0
        };
    }

    private float CurrentNoDotLimit() => exitNoDotSeconds;

    private void TryReleaseByCounter()
    {
        var pref = GetPreferredHomeGhost();
        if (!pref) { personalDotCounter = 0; return; }

        int limit = PersonalDotLimit(pref);
        if (personalDotCounter >= limit)
        {
            EnsureExit(pref);
            personalDotCounter = 0;
            noDotTimer = 0f;
        }
    }

    private void TryReleaseByStall()
    {
        var pref = GetPreferredHomeGhost();
        if (!pref) { noDotTimer = 0f; return; }

        if (noDotTimer >= CurrentNoDotLimit())
        {
            EnsureExit(pref);
            noDotTimer = 0f;
            personalDotCounter = 0;
        }
    }

    // Global (post-death) counter: Pinky 7, Inky 17, Clyde 32
    private void TryReleaseByGlobalCounter()
    {
        // If nobody is inside, stop global mode
        if (GetPreferredHomeGhost() == null)
        {
            StopGlobalHouseCounterMode();
            return;
        }

        // Pinky at 7
        if (globalDotCounter >= 7)
        {
            var pinky = GetHomeGhostByType(GhostType.Pinky);
            if (pinky) { EnsureExit(pinky); return; }
        }
        // Inky at 17
        if (globalDotCounter >= 17)
        {
            var inky = GetHomeGhostByType(GhostType.Inky);
            if (inky) { EnsureExit(inky); return; }
        }
        // Clyde at 32
        if (globalDotCounter >= 32)
        {
            var clyde = GetHomeGhostByType(GhostType.Clyde);
            if (clyde) EnsureExit(clyde);
            StopGlobalHouseCounterMode();
        }
    }
}