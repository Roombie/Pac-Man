using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// One per player slot, alive for the whole match and never deactivated
/// Owns that player's copy of the gameplay actions and forwards them to the Pacman it drives,
/// so device assignment survives turn swaps, deaths and Pacman being toggled on/off
/// </summary>
public class PlayerInputController : MonoBehaviour
{
    public int SlotIndex { get; private set; } = -1;
    public InputActionAsset Actions { get; private set; }
    public Pacman Target { get; private set; }
    public InputDevice LastUsedDevice { get; private set; } // Used in 1P to know which device the player is holding

    private InputActionMap playerMap;
    private InputAction moveAction;
    private InputAction pauseAction;
    private InputAction selectAction;

    private readonly SwipeDetector swipe = new();
    private bool touchEnabled; // 1P only: touch has no device of its own to hand out between players
    private Vector2 pendingDirection; // Last direction pressed/swiped while Pacman couldn't move, applied once it can

    public void Initialize(int slotIndex, InputActionAsset template)
    {
        SlotIndex = slotIndex;

        // Each player gets its own clone so device/mask restrictions don't leak between players
        Actions = Instantiate(template);
        Actions.name = $"{template.name} (P{slotIndex + 1})";

        playerMap = Actions.FindActionMap("Player", true);
        moveAction = playerMap.FindAction("Move", true);
        pauseAction = playerMap.FindAction("Pause", true);
        selectAction = playerMap.FindAction("Select", true);

        moveAction.performed += OnMove;
        moveAction.canceled += OnMove;
        pauseAction.performed += OnPause;
        selectAction.performed += OnSelect;
    }

    private void OnDestroy()
    {
        if (Actions == null) return;

        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;
        pauseAction.performed -= OnPause;
        selectAction.performed -= OnSelect;

        Actions.Disable();
        Destroy(Actions);
    }

    /// <summary>
    /// Restricts this player to one device and control scheme (2P),
    /// or opens it to every device and binding when device is null (1P)
    /// </summary>
    public void Bind(InputDevice device, string scheme)
    {
        bool wasEnabled = playerMap.enabled;
        Actions.Disable();

        if (device == null)
        {
            Actions.devices = null;
            Actions.bindingMask = null;
        }
        else
        {
            Actions.devices = new ReadOnlyArray<InputDevice>(new[] { device });
            Actions.bindingMask = string.IsNullOrEmpty(scheme) ? null : InputBinding.MaskByGroup(scheme);
        }

        LastUsedDevice = device;
        touchEnabled = device == null;
        if (wasEnabled) playerMap.Enable();
    }

    private void Update()
    {
        if (playerMap == null || !playerMap.enabled) return;

        if (touchEnabled && swipe.TryGetSwipe(out var direction))
        {
            LastUsedDevice = Touchscreen.current;
            if (!TargetActive || !Target.ApplyMoveInput(direction)) pendingDirection = direction;
            else pendingDirection = Vector2.zero;
        }

        if (!TargetActive) return;

        // Re-apply the held direction every frame: Move only fires when its value changes, so a key
        // held through a freeze (ghost eaten, READY, pause) would otherwise be lost once it ends
        Vector2 held = moveAction.ReadValue<Vector2>();
        if (held != Vector2.zero)
        {
            if (Target.ApplyMoveInput(held)) pendingDirection = Vector2.zero; // Held wins over an older press
        }
        // A direction tapped and released during the freeze still counts once Pacman can move
        else if (pendingDirection != Vector2.zero && Target.ApplyMoveInput(pendingDirection))
        {
            pendingDirection = Vector2.zero;
        }
    }

    public void SetTarget(Pacman pacman)
    {
        Target = pacman;
        pendingDirection = Vector2.zero;
    }

    public void SetInputEnabled(bool enabled)
    {
        if (playerMap == null) return;
        if (enabled) playerMap.Enable();
        else playerMap.Disable();
    }

    public void ClearLastUsedDevice() => LastUsedDevice = null;

    // Pacman only reacts while it's actually in the maze (it gets hidden during sequences)
    private bool TargetActive => Target != null && Target.gameObject.activeInHierarchy;

    private void OnMove(InputAction.CallbackContext ctx)
    {
        Track(ctx);

        if (ctx.performed)
        {
            // Pacman is hidden or frozen: remember the press instead of dropping it
            Vector2 direction = ctx.ReadValue<Vector2>();
            if (direction == Vector2.zero) return;

            if (TargetActive && Target.ApplyMoveInput(direction)) pendingDirection = Vector2.zero;
            else pendingDirection = direction;
        }
        else if (TargetActive)
        {
            Target.OnMove(ctx);
        }
    }

    private void OnPause(InputAction.CallbackContext ctx)
    {
        Track(ctx);
        if (!TargetActive) return;

        var state = GameManager.Instance != null ? GameManager.Instance.CurrentGameState : GameManager.GameState.Intro;
        if (state == GameManager.GameState.Playing || state == GameManager.GameState.Paused)
            PauseMenuController.Instance.TogglePause();
    }

    private void OnSelect(InputAction.CallbackContext ctx)
    {
        Track(ctx);
        if (!TargetActive) return;

        var gm = GameManager.Instance;
        if (gm != null)
            InputManager.Instance.RequestDejoin(gm, SlotIndex + 1, gm.IsTwoPlayerMode, gm.TotalPlayers);
    }

    private void Track(InputAction.CallbackContext ctx)
    {
        if (ctx.control != null) LastUsedDevice = ctx.control.device;
    }
}
