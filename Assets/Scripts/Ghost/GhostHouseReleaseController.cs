using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implements original Pac-Man ghost-house release rules:
/// - Per-ghost dot counters at level start (Pinky=0; Inky/Clyde level-dependent)
/// - Global dot counter after life lost (7/17/32 with Clyde-at-32 reset)
/// - Idle timer forces most-preferred inside ghost to leave (4s on L1–4, 3s L5+)
/// Priority: Pinky > Inky > Clyde (Blinky is never managed here).
/// </summary>
public class GhostHouseReleaseController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PelletManager pelletManager;
    [SerializeField] private GlobalGhostModeController ghostModes;
    [SerializeField] private Ghost blinky;
    [SerializeField] private Ghost pinky;
    [SerializeField] private Ghost inky;
    [SerializeField] private Ghost clyde;

    [Header("Level / timing")]
    [SerializeField] private int currentLevel = 1;
    [Tooltip("Seconds with no dots before we force the most-preferred inside ghost out.")]
    [SerializeField] private float idleTimerLimit = 4f; // will be 3f from level 5+

    // Personal counters (only count while that ghost is the active counter and is inside Home)
    private readonly Dictionary<Ghost, int> personalCount = new();
    private readonly Dictionary<Ghost, int> personalLimit = new();

    // Global counter after life lost
    private bool globalActive;
    private int globalCount;

    // Timer (resets whenever a dot is eaten)
    private float idleTimer;

    // Cache array for priority
    private Ghost[] prefOrder;

    void Awake()
    {
        prefOrder = new[] { pinky, inky, clyde };
        foreach (var g in prefOrder) personalCount[g] = 0;
        ApplyLevel(currentLevel); // sets personal limits and idleTimerLimit
    }

    void OnEnable()
    {
        if (pelletManager) pelletManager.OnPelletConsumedGlobal += OnAnyPelletEaten;
        // Optional: subscribe to your GameManager life-lost event; call OnLifeLost() from there.
    }

    void OnDisable()
    {
        if (pelletManager) pelletManager.OnPelletConsumedGlobal -= OnAnyPelletEaten;
    }

    void Update()
    {
        // Advance idle timer only while actually playing (optional: guard with your GameState)
        idleTimer += Time.deltaTime;

        if (idleTimer >= idleTimerLimit)
        {
            ForceReleaseMostPreferredInside();
            idleTimer = 0f;
        }
    }

    // --------- Public API you call from GameManager ---------

    /// <summary>Call at the beginning of a level.</summary>
    public void OnLevelStart(int level)
    {
        currentLevel = Mathf.Max(1, level);
        ApplyLevel(currentLevel);

        // Reset personal counters (not limits), disable global
        foreach (var g in prefOrder) personalCount[g] = 0;
        globalActive = false;
        globalCount = 0;
        idleTimer = 0f;
    }

    /// <summary>Call right after a life is lost (before play resumes).</summary>
    public void OnLifeLost()
    {
        // Disable (but do not reset) personal counters; enable global and reset it.
        globalActive = true;
        globalCount = 0;
        idleTimer = 0f;
    }

    // --------- Core logic ---------

    private void OnAnyPelletEaten()
    {
        idleTimer = 0f; // reset idle timer every dot

        if (globalActive)
        {
            HandleGlobalCounterDot();
        }
        else
        {
            HandlePersonalCounterDot();
        }
    }

    private void HandlePersonalCounterDot()
    {
        var active = GetMostPreferredInside(); // Pinky>Inky>Clyde inside Home
        if (active == null) return;

        personalCount[active]++;

        // Release if limit reached/exceeded (0-limit leaves immediately)
        if (personalCount[active] >= personalLimit[active])
        {
            ReleaseGhostIfInside(active);
            // Next dot eaten will advance the next preferred inside ghost's counter automatically
        }
    }

    private void HandleGlobalCounterDot()
    {
        globalCount++;

        // Pinky at 7, Inky at 17, Clyde special at 32
        if (globalCount == 7) ReleaseGhostIfInside(pinky);
        else if (globalCount == 17) ReleaseGhostIfInside(inky);
        else if (globalCount == 32)
        {
            bool clydeInside = IsInside(clyde);
            if (clydeInside) ReleaseGhostIfInside(clyde);

            // Special: if Clyde is inside at 32, the global counter is reset and deactivated
            if (clydeInside)
            {
                globalCount = 0;
                globalActive = false;
            }
            // else: keep global active forever (the famous “stuck in house” quirk)
        }
    }

    private void ForceReleaseMostPreferredInside()
    {
        var g = GetMostPreferredInside();
        if (g != null)
        {
            ReleaseGhostIfInside(g);
            // Timer resets done by caller (Update) via idleTimer = 0
        }
    }

    private Ghost GetMostPreferredInside()
    {
        foreach (var g in prefOrder)
            if (IsInside(g)) return g;
        return null;
    }

    private bool IsInside(Ghost g)
    {
        return g && g.CurrentMode == Ghost.Mode.Home && g.gameObject.activeInHierarchy;
    }

    private void ReleaseGhostIfInside(Ghost g)
    {
        if (!IsInside(g)) return;

        // Tell Home to leave immediately if present; otherwise just flip mode and nudge UP.
        var home = g.GetComponent<GhostHome>();
        if (home != null)
        {
            // You can implement this tiny method in GhostHome (see snippet below).
            home.RequestImmediateExit();
        }
        else
        {
            g.SetMode(Ghost.Mode.Scatter);
            if (g.movement) g.movement.SetDirection(Vector2.up, forced: true);
        }
    }

    private void ApplyLevel(int level)
    {
        // Personal dot limits by level:
        // L1: Pinky=0, Inky=30, Clyde=60
        // L2: Pinky=0, Inky=0,  Clyde=50
        // L3+: all 0
        personalLimit[pinky] = 0;

        if (level == 1)
        {
            personalLimit[inky] = 30;
            personalLimit[clyde] = 60;
        }
        else if (level == 2)
        {
            personalLimit[inky] = 0;
            personalLimit[clyde] = 50;
        }
        else
        {
            personalLimit[inky] = 0;
            personalLimit[clyde] = 0;
        }

        // Idle timer: 4s on L1–4, 3s on L5+
        idleTimerLimit = (level >= 5) ? 3f : 4f;
    }
}
