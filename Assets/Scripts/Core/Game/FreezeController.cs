using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the layered freeze system for gameplay pauses (ghost eaten, level clear, etc.).
/// Supports hard freezes (Time.timeScale = 0) and soft freezes (disable specific components).
/// Attach to the same GameObject as GameManager.
/// </summary>
public class FreezeController : MonoBehaviour
{
    public static FreezeController Instance { get; private set; }

    [Serializable]
    private struct FreezeFrame
    {
        public bool hard;
        public bool freezeTimers;
        public List<Behaviour> disabled;
        public List<Rigidbody2D> pausedBodies;
    }

    private int timersCount = 0;
    private readonly Stack<FreezeFrame> freeze = new();
    private float savedTimeScale = 1f;
    private int hardCount = 0;
    private readonly Dictionary<Behaviour, int> softDisableRef = new();

    // Set by GameManager so FreezeController can freeze ghost timers
    public GlobalGhostModeController GhostModeController { private get; set; }

    // FreezeController needs to know the current Pacman and ghosts for PushFreezeSoftAllowEyes
    public Func<Pacman> GetCurrentPacman { private get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Pushes a freeze layer. Hard = stops time. Soft = disables specific components.
    /// </summary>
    public void PushFreeze(bool hard, bool freezeTimers = false, IEnumerable<Behaviour> toDisable = null)
    {
        var frame = new FreezeFrame
        {
            hard = hard,
            freezeTimers = freezeTimers,
            disabled = toDisable != null ? new List<Behaviour>(toDisable) : null
        };
        freeze.Push(frame);

        if (hard)
        {
            if (hardCount++ == 0)
                savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (freezeTimers && ++timersCount == 1)
            GhostModeController?.SetTimersFrozen(true);

        if (!hard && frame.disabled != null)
        {
            foreach (var b in frame.disabled)
            {
                if (!b) continue;
                softDisableRef.TryGetValue(b, out var n);
                softDisableRef[b] = n + 1;
                if (n == 0) b.enabled = false;
            }
        }

        GameEvents.FreezePushed(hard);
    }

    /// <summary>
    /// Soft freeze that keeps Eaten ghosts (eyes) moving — used during ghost-eaten sequence.
    /// Freezes Pacman, non-eaten ghosts, and their Rigidbodies, but not timers for eyes routing.
    /// </summary>
    public void PushFreezeSoftAllowEyes()
    {
        var disabled = new List<Behaviour>();
        var bodies   = new List<Rigidbody2D>();

        if (++timersCount == 1)
            GhostModeController?.SetTimersFrozen(true);

        var pacman = GetCurrentPacman?.Invoke();
        if (pacman)
        {
            if (pacman.movement && pacman.movement.enabled)
            {
                pacman.movement.enabled = false;
                disabled.Add(pacman.movement);
            }
            var anim = pacman.GetComponentInChildren<Animator>();
            if (anim && anim.enabled)
            {
                anim.enabled = false;
                disabled.Add(anim);
            }
            var rb = pacman.movement ? pacman.movement.rb : null;
            if (rb && rb.simulated)
            {
                rb.linearVelocity  = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated       = false;
                bodies.Add(rb);
            }
        }

        if (GhostModeController)
        {
            foreach (var ghost in GhostModeController.ghosts)
            {
                if (!ghost) continue;
                if (ghost.CurrentMode == Ghost.Mode.Eaten) continue;

                if (ghost.movement && ghost.movement.enabled)
                {
                    ghost.movement.enabled = false;
                    disabled.Add(ghost.movement);
                }
                foreach (var a in ghost.GetComponentsInChildren<Animator>(true))
                {
                    if (a.enabled) { a.enabled = false; disabled.Add(a); }
                }
                var rb = ghost.movement ? ghost.movement.rb : null;
                if (rb && rb.simulated)
                {
                    rb.linearVelocity  = Vector2.zero;
                    rb.angularVelocity = 0f;
                    rb.simulated       = false;
                    bodies.Add(rb);
                }
            }
        }

        freeze.Push(new FreezeFrame { hard = false, freezeTimers = true, disabled = disabled, pausedBodies = bodies });
        GameEvents.FreezePushed(false);
    }

    /// <summary>
    /// Pops the most recent freeze layer and restores its state.
    /// </summary>
    public void PopFreeze()
    {
        if (freeze.Count == 0) return;
        var top = freeze.Pop();

        if (top.hard)
        {
            if (--hardCount == 0)
                Time.timeScale = savedTimeScale;
        }
        else
        {
            if (top.pausedBodies != null)
            {
                foreach (var rb in top.pausedBodies)
                {
                    if (!rb) continue;
                    rb.angularVelocity = 0f;
                    rb.linearVelocity  = Vector2.zero;
                    rb.simulated       = true;
                }
            }
            if (top.disabled != null)
            {
                foreach (var b in top.disabled)
                    if (b) b.enabled = true;
            }
        }

        if (top.freezeTimers && --timersCount == 0)
            GhostModeController?.SetTimersFrozen(false);

        GameEvents.FreezePopped();
    }
}