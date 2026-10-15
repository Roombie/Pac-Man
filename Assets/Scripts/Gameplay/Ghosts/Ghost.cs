using UnityEngine;
using System;

public class Ghost : MonoBehaviour
{
    [Header("Core")]
    public Movement movement;
    public Pacman CurrentPacman => PacmanSpawner.Instance != null ? PacmanSpawner.Instance.CurrentPacman : null;

    [SerializeField] private GhostType ghostType = GhostType.Blinky;
    public GhostType Type => ghostType;
    private bool settingMode;

    private Vector3 startingPosition;

    // Only used by Inky
    public Ghost blinky;

    public enum Mode { Scatter, Chase, Frightened, Home, Eaten }
    public Mode CurrentMode { get; private set; } = Mode.Scatter;

    [Header("Per-mode behaviours (assign in Inspector)")]
    [SerializeField] private GhostScatter scatter;
    [SerializeField] private GhostChase chase;
    [SerializeField] private GhostFrightened frightened;
    [SerializeField] private GhostHome home;
    [SerializeField] private GhostEaten eaten;

    private GhostEyes eyes;
    private GhostVisuals visuals;

    [Header("Elroy (Blinky only)")]
    [SerializeField, Range(0, 2)] private int elroyStage = 0;
    public int ElroyStage => (Type == GhostType.Blinky) ? elroyStage : 0;
    public bool IsElroy => Type == GhostType.Blinky && elroyStage > 0;

    private float levelSpeed = -1f;     // normal ghost speed for the current level (set by the controller)
    private float elroySpeedMult = 1f;  // only applied in Scatter/Chase
    private int homeModeFlipCount = 0;
    public event Action<Ghost, Mode, Mode> ModeChanged;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (ghostType != GhostType.Inky) blinky = null;
        if (!movement) movement = GetComponent<Movement>();
        elroyStage = Mathf.Clamp(elroyStage, 0, 2);
    }
#endif

    private void Awake()
    {
        if (!movement) movement = GetComponent<Movement>();
        if (!eyes) eyes = GetComponent<GhostEyes>(); 
        if (!visuals) visuals = GetComponent<GhostVisuals>();
        if (!scatter) scatter = GetComponent<GhostScatter>();
        if (!chase) chase = GetComponent<GhostChase>();
        if (!frightened) frightened = GetComponent<GhostFrightened>();
        if (!home) home = GetComponent<GhostHome>();
        if (!eaten) eaten = GetComponent<GhostEaten>();

        startingPosition = transform.position;

        if (movement) levelSpeed = movement.speed;

        EnableOnly(CurrentMode);
    }

    private void OnEnable() => EnableOnly(CurrentMode);

    public void SetMode(Mode newMode)
    {
        if (CurrentMode == newMode) return;
        if (settingMode) return;

        settingMode = true;
        var prev = CurrentMode;

        CurrentMode = newMode;
        EnableOnly(newMode);
        RefreshSpeed(); // Elroy speed only counts in Scatter/Chase

        settingMode = false;

        ModeChanged?.Invoke(this, prev, newMode);
    }

    /// <summary>Normal (non-Elroy) speed for the current level, in world units per second</summary>
    public void SetLevelSpeed(float speed)
    {
        levelSpeed = speed;
        RefreshSpeed();
    }

    /// <summary>Elroy 0/1/2; speedMult is relative to the level's normal ghost speed.</summary>
    public void SetElroyStage(int stage, float speedMult)
    {
        if (Type != GhostType.Blinky || movement == null) return;

        elroyStage = Mathf.Clamp(stage, 0, 2);
        elroySpeedMult = (elroyStage == 0) ? 1f : Mathf.Max(0.01f, speedMult);
        RefreshSpeed();
    }

    public void ResetEyesFacingForMode(Mode mode)
    {
        if (!eyes) eyes = GetComponent<GhostEyes>();
        if (!eyes) return;

        // Pick a sensible initial facing per mode
        Vector2 initDir = Vector2.left; // outside ghosts default left

        if (mode == Mode.Home)
            initDir = (Type == GhostType.Pinky) ? Vector2.down : Vector2.up; // arcade intro bounce
        else if (mode == Mode.Eaten)
            initDir = Vector2.up; // heading toward the door by default

        eyes.ResetEyes(initDir);
    }

    public void ResetStateTo(Mode mode)
    {
        transform.position = startingPosition;

        if (movement)
        {
            movement.enabled = false;
            movement.SetDirection(Vector2.zero, true);

            movement.SetEnvSpeedMultiplier(1f);
        }

        homeModeFlipCount = 0;
        SetMode(mode);
        ResetEyesFacingForMode(mode);
    }

    private int pacmanLayer = -1;
    private bool IsPacmanObject(GameObject go)
    {
        if (!go) return false;
        if (pacmanLayer == -1) pacmanLayer = LayerMask.NameToLayer("Pacman");
        
        // Check if it's the current active pacman
        Pacman currentPacman = CurrentPacman;
        if (currentPacman != null && go == currentPacman.gameObject)
            return true;
            
        return go.layer == pacmanLayer || go.GetComponent<Pacman>() != null || go.CompareTag("Player");
    }

    private void HandlePacmanContact()
    {
        if (CurrentMode == Mode.Eaten) return;

        if (CurrentMode == Mode.Frightened)
        {
            if (GhostInteractionHandler.Instance != null) GhostInteractionHandler.Instance.GhostEaten(this);
        }
        else
        {
            Pacman currentPacman = CurrentPacman;
            if (currentPacman != null) 
                currentPacman.Death();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPacmanObject(other.gameObject)) HandlePacmanContact();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsPacmanObject(collision.gameObject)) HandlePacmanContact();
    }

    private void EnableOnly(Mode mode)
    {
        if (scatter) scatter.enabled = mode == Mode.Scatter;
        if (chase) chase.enabled = mode == Mode.Chase;
        if (frightened) frightened.enabled = mode == Mode.Frightened;
        if (home) home.enabled = mode == Mode.Home;
        if (eaten) eaten.enabled = mode == Mode.Eaten;
    }

    // Mode multipliers (frightened, eyes, tunnel) are applied on top through Movement's multipliers
    private void RefreshSpeed()
    {
        if (movement == null || levelSpeed <= 0f) return;

        bool elroyApplies = CurrentMode == Mode.Scatter || CurrentMode == Mode.Chase;
        movement.speed = levelSpeed * (elroyApplies ? elroySpeedMult : 1f);
    }

    /// <summary>Called by the controller when a Scatter↔Chase flip occurs.</summary>
    public void NotifyModeFlipWhileHome()
    {
        if (CurrentMode == Mode.Home) homeModeFlipCount++;
    }

    /// <summary>Returns true if any flip occurred while in Home, then clears the counter.</summary>
    public bool ConsumeExitShouldGoRight()
    {
        bool right = homeModeFlipCount > 0;
        homeModeFlipCount = 0;
        return right;
    }
}