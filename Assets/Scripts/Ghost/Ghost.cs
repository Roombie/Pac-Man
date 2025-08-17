using UnityEngine;

public class Ghost : MonoBehaviour
{
    [Header("Core")]
    public Movement movement;
    public Pacman pacman;

    [SerializeField] private GhostType ghostType = GhostType.Blinky;
    public GhostType Type => ghostType;

    private Vector3 startingPosition;

    // Only used by Inky
    public Ghost blinky;

    public enum Mode { Home, Scatter, Chase, Frightened, Eaten }
    [SerializeField] private Mode currentMode = Mode.Home;
    public Mode CurrentMode => currentMode;

    [Header("Elroy (Blinky only)")]
    [SerializeField] private float elroy1Multiplier = 1.08f;
    [SerializeField] private float elroy2Multiplier = 1.16f;
    [SerializeField, Tooltip("0=Off, 1=Elroy1, 2=Elroy2")]
    private int elroyStage = 0; // serialized for debugging in Inspector

    public bool IsElroyActive => (ghostType == GhostType.Blinky) && elroyStage > 0;
    public int ElroyStage => elroyStage;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (ghostType != GhostType.Inky) blinky = null;
        if (!movement) movement = GetComponent<Movement>();
        if (!pacman) pacman = FindAnyObjectByType<Pacman>();
        elroyStage = Mathf.Clamp(elroyStage, 0, 2);
        RecomputeSpeed(); // keep Inspector edits consistent
    }
#endif

    void Awake()
    {
        if (!movement) movement = GetComponent<Movement>();
        if (!pacman) pacman = FindAnyObjectByType<Pacman>();
        startingPosition = transform.position;
        RecomputeSpeed();
    }

    public void SetMode(Mode mode)
    {
        currentMode = mode;
        RecomputeSpeed(); // handles speed adjustments

        bool enableCols = mode != Mode.Eaten; // enable colliders only if the mode isn't eaten
        foreach (var c in GetComponents<CircleCollider2D>())
            c.enabled = enableCols;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer != LayerMask.NameToLayer("Pacman")) return;

        // Only react during active play
        var gm = GameManager.Instance;
        if (!gm || gm.CurrentGameState != GameManager.GameState.Playing) return;

        // If we're frightened, Pac-Man should eat US (set Eaten; scoring handled elsewhere)
        if (currentMode == Mode.Frightened)
        {
            SetMode(Mode.Eaten);
            return;
        }

        // If we're already eyes/home, ignore
        if (currentMode == Mode.Eaten || currentMode == Mode.Home) return;

        // Otherwise we are dangerous: Pac-Man starts it's losing animation and then lose a life
        gm.pacman.Death();
    }

    public void SetElroyStage(int stage)
    {
        int clamped = Mathf.Clamp(stage, 0, 2);
        if (clamped == elroyStage) return;
        elroyStage = clamped;
        RecomputeSpeed();
    }

    private void RecomputeSpeed()
    {
        if (!movement) return;

        float baseMul = (currentMode == Mode.Frightened) ? 0.5f : 1f;

        float elroyMul = 1f;
        if (ghostType == GhostType.Blinky)
            elroyMul = (elroyStage == 2) ? elroy2Multiplier :
                    (elroyStage == 1) ? elroy1Multiplier : 1f;

        movement.SetBaseSpeedMultiplier(baseMul * elroyMul);
    }

    /// <summary>
    /// Reset this ghost to its spawn and freeze movement, then set the given mode.
    /// </summary>
    public void ResetStateTo(Mode mode)
    {
        // Position back to spawn
        transform.position = startingPosition;

        // Freeze movement
        if (movement)
        {
            movement.enabled = false;
            movement.SetDirection(Vector2.zero);
        }

        // Set visible/logic mode
        SetMode(mode);
    }
}