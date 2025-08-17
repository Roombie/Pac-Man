using UnityEngine;
using System;

[AddComponentMenu("Pacman/Global Ghost Mode Controller")]
[DisallowMultipleComponent]
public class GlobalGhostModeController : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Ghost[] ghosts;

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

    private void Start() => ApplyLevel(currentLevel);

    private void Update()
    {
        if (phaseTimer != null) phaseTimer.Tick(Time.deltaTime);
        if (isFrightenedActive && frightenedTimer != null) frightenedTimer.Tick(Time.deltaTime);
    }

    public void ApplyLevel(int level)
    {
        currentLevel = Mathf.Max(1, level);

        if (matrix != null)
        {
            float fr;
            var built = matrix.GetPhasesForLevel(currentLevel, out fr);
            previewFrightenedSeconds = fr;
            ResetSchedule(built);
#if UNITY_EDITOR
            Debug.Log($"[GhostModes] Applied band: {matrix.GetBandLabelForLevel(currentLevel)} (level {currentLevel})");
#endif
        }
        else
        {
            ResetSchedule(phases);
        }
    }

    private void SetGhostAnimatorsSpeed(float s)
    {
        ForEachGhost(g =>
        {
            if (!g) return;
            foreach (var a in g.GetComponentsInChildren<Animator>(true))
                a.speed = s;
        });
    }

    public void PauseAllGhostAnimations()  => SetGhostAnimatorsSpeed(0f);
    public void ResumeAllGhostAnimations() => SetGhostAnimatorsSpeed(1f);

    public void TriggerFrightened(float? duration = null)
    {
        ReverseAll();

        float dur = duration ?? previewFrightenedSeconds;

        isFrightenedActive = true;
        frightenedTimer = new Timer(dur);
        frightenedTimer.OnTimerEnd += EndFrightened;

        ForEachGhost(g =>
        {
            if (g.CurrentMode == Ghost.Mode.Eaten) return;

            var vis = g.GetComponent<GhostVisuals>();
            if (vis) vis.OnFrightenedStart(dur);

            g.SetMode(Ghost.Mode.Frightened);
        });
    }

    public void ResetSchedule(GhostPhase[] newPhases)
    {
        phases = newPhases ?? Array.Empty<GhostPhase>();
        phaseIndex = -1;
        phaseTimer = null;
        AdvancePhase(initial: true);
    }

    public void ActivateAllGhosts()
    {
        // cancel frightened if any
        isFrightenedActive = false;
        frightenedTimer = null;

        ForEachGhost(g =>
        {
            if (!g) return;

            if (!g.gameObject.activeSelf)
                g.gameObject.SetActive(true);

            // Per-ghost start mode:
            //  - Blinky starts outside (use current scheduled phase)
            //  - Others start inside (Home)
            var startMode = (g.Type == GhostType.Blinky)
                ? currentPhaseMode
                : Ghost.Mode.Home;

            g.ResetStateTo(startMode);

            // Keep colliders off until we actually "start"
            foreach (var c in g.GetComponents<CircleCollider2D>())
                c.enabled = false;
        });
    }

    /// <summary>
    /// Starts movement for all ghosts (enables Movement + colliders).
    /// Does NOT reposition; assumes ActivateAllGhosts() was called first.
    /// </summary>
    public void StartAllGhosts(bool enableColliders = true, bool pushHomeUp = false)
    {
        ForEachGhost(g =>
        {
            if (!g) return;

            if (!g.gameObject.activeSelf)
                g.gameObject.SetActive(true);

            if (enableColliders)
            {
                // keep eyes harmless; keep Home non-colliding too until they leave
                bool shouldEnable = g.CurrentMode != Ghost.Mode.Eaten
                                    && g.CurrentMode != Ghost.Mode.Home;
                foreach (var c in g.GetComponents<Collider2D>())
                    c.enabled = shouldEnable;
            }

            if (g.movement)
            {
                g.movement.enabled = true;

                // Only at intro end do we push Home ghosts upward
                if (pushHomeUp && g.CurrentMode == Ghost.Mode.Home)
                    g.movement.SetDirection(Vector2.up, forced: true);

                if (g.movement.direction == Vector2.zero && g.movement.nextDirection != Vector2.zero)
                    g.movement.SetDirection(g.movement.nextDirection);
            }

            // resume animators
            ResumeAllGhostAnimations();

            // don’t yank Home or Eaten out of their states
            if (!isFrightenedActive && g.CurrentMode != Ghost.Mode.Home && g.CurrentMode != Ghost.Mode.Eaten)
                g.SetMode(currentPhaseMode);
        });
    }

    // Turn off gameobjects
    public void DeactivateAllGhosts()
    {
        ForEachGhost(g =>
        {
            if (g && g.gameObject.activeSelf)
                g.gameObject.SetActive(false);
        });
    }

    /// <summary>
    /// Stops the movement of all ghosts. Optionally disable your colliders
    /// and set the address to (0,0) to “freeze” them completely.
    /// </summary>
    public void StopAllGhosts(bool disableColliders = true, bool zeroDirection = true)
    {
        ForEachGhost(g =>
        {
            if (!g) return;

            if (g.movement)
            {
                if (zeroDirection) g.movement.SetDirection(Vector2.zero);
                g.movement.enabled = false;
            }

            // pause all child animators
            PauseAllGhostAnimations();

            if (disableColliders)
            {
                var cols = g.GetComponents<Collider2D>();
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            }
        });
    }

    // Internals
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
            ReverseAll();

        // If frightened is active, do not visibly change modes; schedule continues underneath
        if (!isFrightenedActive)
            SetAll(currentPhaseMode);
    }

    private void OnPhaseTimerEnd() => AdvancePhase();

    private void EndFrightened()
    {
        isFrightenedActive = false;
        frightenedTimer = null;

        // Arcade: no reverse when frightened ends; resume current scheduled mode
        SetAll(currentPhaseMode);
    }

    private static bool IsScatterChasePair(Ghost.Mode a, Ghost.Mode b) =>
        (a == Ghost.Mode.Scatter && b == Ghost.Mode.Chase) ||
        (a == Ghost.Mode.Chase   && b == Ghost.Mode.Scatter);

    private void SetAll(Ghost.Mode mode)
    {
        ForEachGhost(g =>
        {
            if (g.CurrentMode == Ghost.Mode.Eaten || g.CurrentMode == Ghost.Mode.Home) return;
            g.SetMode(mode); // or g.SwitchMode(mode) if you added that wrapper
        });
    }

    private void ReverseAll()
    {
        ForEachGhost(g =>
        {
            if (g.movement == null) return;
            var dir = g.movement.direction;
            if (dir != Vector2.zero) g.movement.SetDirection(-dir, forced: true);
        });
    }

    private void ForEachGhost(Action<Ghost> action)
    {
        if (ghosts == null) return;
        foreach (var g in ghosts)
            if (g != null) action(g);
    }
}
