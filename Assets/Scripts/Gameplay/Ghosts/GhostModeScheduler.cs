using UnityEngine;
using System;

public class GhostModeScheduler : MonoBehaviour
{
    [System.Serializable]
    public struct Phase
    {
        public Ghost.Mode mode;        // Scatter or Chase
        public float durationSeconds;  // <= 0 means "infinite"
    }

    [Header("Targets")]
    [SerializeField] private Ghost[] ghosts;

    [Header("Phase schedule (per level)")]
    [Tooltip("Example Level 1: Scatter 7, Chase 20, Scatter 7, Chase 20, Scatter 5, then Chase ∞")]
    [SerializeField] private Phase[] phases;

    [Header("Frightened")]
    [SerializeField] private float frightenedDuration = 6f;

    private Timer phaseTimer;
    private int phaseIndex = -1;
    private Ghost.Mode currentPhaseMode;

    // Frightened overlay
    private Timer frightenedTimer;
    private bool isFrightenedActive;

    private void Start()
    {
        // Kick off the first phase
        AdvancePhase(initial:true);
    }

    private void Update()
    {
        // Tick the underlying phase timer continuously (even during frightened)
        if (phaseTimer != null) phaseTimer.Tick(Time.deltaTime);
        if (isFrightenedActive && frightenedTimer != null) frightenedTimer.Tick(Time.deltaTime);
    }

    // === Public API ===

    public void TriggerFrightened(float? customDuration = null)
    {
        // Reverse once when entering frightened (arcade)
        ReverseAll();

        isFrightenedActive = true;
        frightenedTimer = new Timer(customDuration ?? frightenedDuration);
        frightenedTimer.OnTimerEnd += EndFrightened;

        // Switch all non-eaten/home ghosts to Frightened
        ForEachGhost(g =>
        {
            if (g.CurrentMode != Ghost.Mode.Eaten && g.CurrentMode != Ghost.Mode.Home)
                g.SetMode(Ghost.Mode.Frightened);
        });
    }

    public void ResetSchedule(Phase[] newPhases)
    {
        phases = newPhases;
        phaseIndex = -1;
        phaseTimer = null;
        AdvancePhase(initial:true);
    }

    // === Internals ===

    private void AdvancePhase(bool initial = false)
    {
        var prevMode = currentPhaseMode;

        phaseIndex++;
        if (phaseIndex >= phases.Length)
        {
            // If schedule ended, stick to last mode "forever"
            phaseIndex = phases.Length - 1;
        }

        var phase = phases[phaseIndex];
        currentPhaseMode = phase.mode;

        // Start/restart phase timer (unless infinite)
        phaseTimer = (phase.durationSeconds > 0f) ? new Timer(phase.durationSeconds) : null;
        if (phaseTimer != null) phaseTimer.OnTimerEnd += OnPhaseTimerEnd;

        // When switching Scatter <-> Chase, reverse once (not on initial)
        if (!initial && IsScatterChasePair(prevMode, currentPhaseMode))
            ReverseAll();

        // If frightened is active, we DO NOT change visible mode yet
        // (arcade behavior: schedule continues underneath)
        if (!isFrightenedActive)
            SetAll(currentPhaseMode);
    }

    private void OnPhaseTimerEnd()
    {
        // Next phase; schedule continues regardless of frightened state
        AdvancePhase();
    }

    private void EndFrightened()
    {
        isFrightenedActive = false;
        frightenedTimer = null;

        // Do NOT reverse here (arcade). Resume whatever the schedule says now.
        SetAll(currentPhaseMode);
    }

    private static bool IsScatterChasePair(Ghost.Mode a, Ghost.Mode b)
    {
        return (a == Ghost.Mode.Scatter && b == Ghost.Mode.Chase)
            || (a == Ghost.Mode.Chase   && b == Ghost.Mode.Scatter);
    }

    private void SetAll(Ghost.Mode mode)
    {
        ForEachGhost(g =>
        {
            if (g.CurrentMode == Ghost.Mode.Eaten || g.CurrentMode == Ghost.Mode.Home) return;
            g.SetMode(mode);
        });
    }

    private void ReverseAll()
    {
        ForEachGhost(g =>
        {
            if (g.movement == null) return;
            var dir = g.movement.direction;
            if (dir != Vector2.zero) g.movement.SetDirection(-dir, forced:true);
        });
    }

    private void ForEachGhost(Action<Ghost> action)
    {
        if (ghosts == null) return;
        foreach (var g in ghosts)
            if (g != null) action(g);
    }
}