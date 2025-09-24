using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PanelInputHandler : MonoBehaviour
{
    public event Action<int> OnSubmit;
    public event Action<int> OnCancel;
    public event Action<int, int> OnMove; // (playerIndex, direction)
    public event Action<int> OnDejoin;    // intent to dejoin (manager decides)

    private PlayerInput playerInput;
    private int playerIndex;

    private string chosenScheme;
    private int[] chosenDeviceIds = Array.Empty<int>();

    private InputAction submit;
    private InputAction cancel;
    private InputAction move;
    private InputAction dejoin;

    // -------- Navigation tuning (provided by CharacterSelectionManager) --------
    private float moveDeadzone;
    private float initialRepeatDelay;
    private float repeatInterval;
    private bool allowHoldRepeat;

    private int lastMoveSign = 0;                 // -1, 0, +1
    private float nextRepeatTime = 0f;            // time when next repeat can fire
    private Vector2 lastMoveValue = Vector2.zero; // current move axis (for hold repeat)

    // -------- Reservation tracking --------
    private string reservedKeyboardScheme;
    private Gamepad reservedGamepad;

    private bool hasJoined = false;
    private bool waitForFreshPress = false;

    // NEW: After joining via Move, require axis to go neutral once before allowing navigation
    private bool requireNeutralAfterJoinViaMove = false;

    private void OnEnable()
    {
        waitForFreshPress = false; // reset when menu opens
        if (playerInput != null && playerInput.actions != null)
            playerInput.actions.Enable();
    }

    private void Update()
    {
        // Block hold-to-repeat if we still require neutral after join-via-move
        if (requireNeutralAfterJoinViaMove) return;

        // Hold-to-repeat loop: only when joined, allowed, and after an initial press
        if (!hasJoined) return;
        if (!allowHoldRepeat) return;
        if (lastMoveSign == 0) return;

        // Still holding past deadzone?
        int currentSign = Mathf.Abs(lastMoveValue.x) > moveDeadzone ? (int)Mathf.Sign(lastMoveValue.x) : 0;
        if (currentSign == 0)
        {
            // Back to neutral -> stop repeating
            lastMoveSign = 0;
            return;
        }

        if (Time.time >= nextRepeatTime)
        {
            nextRepeatTime = Time.time + repeatInterval;
            OnMove?.Invoke(playerIndex, currentSign);
        }
    }

    public void Initialize(int index, float deadzone, float repeatDelay, float repeatInterval, bool allowRepeat)
    {
        ResetJoinCompletely(requireFreshPressOnRejoin: false);
        playerIndex = index;
        playerInput = GetComponent<PlayerInput>();

        // Apply tuning from manager
        moveDeadzone = deadzone;
        initialRepeatDelay = repeatDelay;
        this.repeatInterval = repeatInterval;
        allowHoldRepeat = allowRepeat;

        var actions = playerInput.actions;
        submit = actions["Submit"];
        cancel = actions["Cancel"];
        move = actions["Move"];
        dejoin = actions["Select"];

        submit.performed += OnSubmitPerformed;
        cancel.performed += OnCancelPerformed;
        move.performed += OnMovePerformed;
        move.canceled += OnMoveCanceled;
        dejoin.performed += OnDejoinPerformed;
        dejoin.canceled += OnDejoinCanceled;

        actions.Enable();
    }

    private void OnDisable()
    {
        if (submit != null) submit.performed -= OnSubmitPerformed;
        if (cancel != null) cancel.performed -= OnCancelPerformed;
        if (move != null)
        {
            move.performed -= OnMovePerformed;
            move.canceled -= OnMoveCanceled;
        }
        if (dejoin != null)
        {
            dejoin.performed -= OnDejoinPerformed;
            dejoin.canceled -= OnDejoinCanceled;
        }

        if (playerInput != null && playerInput.actions != null)
            playerInput.actions.Disable();

        ReleaseReservations();
        waitForFreshPress = false;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;

        // Ensure we don't stay joined if this GO is deactivated between menus
        ResetJoinCompletely(requireFreshPressOnRejoin: true);
    }

    // ---------------- Input Handlers ----------------
    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined)
        {
            TryClaimFromContext(ctx);
            return;
        }
        OnSubmit?.Invoke(playerIndex);
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined)
        {
            TryClaimFromContext(ctx);
            return;
        }
        OnCancel?.Invoke(playerIndex);
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        // Allow join via Move if not yet joined.
        // IMPORTANT: do NOT navigate on this same press; require a neutral first.
        if (!hasJoined)
        {
            TryClaimFromContext(ctx);
            if (hasJoined)
            {
                // Consume this first move: navigation is blocked until axis returns to neutral once
                requireNeutralAfterJoinViaMove = true;
                lastMoveSign = 0;
                lastMoveValue = Vector2.zero;
            }
            return;
        }

        Vector2 mv = ctx.ReadValue<Vector2>();
        lastMoveValue = mv;

        // If we just joined via Move and haven't gone neutral yet, ignore navigation
        if (requireNeutralAfterJoinViaMove)
            return;

        int sign = Mathf.Abs(mv.x) > moveDeadzone ? (int)Mathf.Sign(mv.x) : 0;

        // Fire once on fresh press; hold-to-repeat handled in Update()
        if (sign != 0 && lastMoveSign == 0)
        {
            lastMoveSign = sign;
            nextRepeatTime = Time.time + initialRepeatDelay;
            OnMove?.Invoke(playerIndex, sign);
            Debug.Log("Move fired once: " + sign);
        }
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        lastMoveSign = 0;             // allow next press
        lastMoveValue = Vector2.zero; // stop hold-to-repeat

        // If we were waiting for neutral after join-via-move, this is the neutral.
        if (requireNeutralAfterJoinViaMove)
        {
            requireNeutralAfterJoinViaMove = false;
            // After this cancel, the next Move press will navigate normally.
        }
    }

    private void OnDejoinPerformed(InputAction.CallbackContext ctx)
    {
        if (hasJoined)
        {
            OnDejoin?.Invoke(playerIndex);
            Debug.Log($"[PanelInputHandler] Player {playerIndex} requested dejoin");
        }
    }

    private void OnDejoinCanceled(InputAction.CallbackContext ctx)
    {
        if (waitForFreshPress)
        {
            waitForFreshPress = false;
            Debug.Log($"[PanelInputHandler] Player {playerIndex} fresh press reset, can rejoin");
        }
    }

    // ---------------- Claim Logic ----------------
    public void TryClaimFromContext(InputAction.CallbackContext ctx)
    {
        if (waitForFreshPress) return;
        var control = ctx.control;
        if (control == null) return;

        // Gamepad
        if (control.device is Gamepad gamepad)
        {
            if (PlayerDeviceManager.Instance.TryReserveGamepad(gamepad))
            {
                reservedGamepad = gamepad;
                chosenScheme = "Gamepad";
                chosenDeviceIds = new[] { gamepad.deviceId };
                hasJoined = true;

                Debug.Log($"[PanelInputHandler] Player {playerIndex} joined with Gamepad");
                CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
            }
            return;
        }

        // Keyboard
        if (control.device is Keyboard)
        {
            var scheme = PlayerDeviceManager.Instance.GetKeyboardSchemeForControl(
                ctx.action,
                control,
                isSinglePlayer: CharacterSelectionManager.Instance.IsSinglePlayer
            );

            if (string.IsNullOrEmpty(scheme))
                scheme = PlayerDeviceManager.Instance.ReserveNextKeyboardScheme();

            if (!string.IsNullOrEmpty(scheme) && PlayerDeviceManager.Instance.TryReserveKeyboardScheme(scheme))
            {
                reservedKeyboardScheme = scheme;
                chosenScheme = scheme;
                chosenDeviceIds = new[] { control.device.deviceId };
                hasJoined = true;

                Debug.Log($"[PanelInputHandler] Player {playerIndex} joined with {scheme}");
                CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
            }
        }
    }

    private void ReleaseReservations()
    {
        if (!string.IsNullOrEmpty(reservedKeyboardScheme))
        {
            PlayerDeviceManager.Instance.ReleaseKeyboardScheme(reservedKeyboardScheme);
            reservedKeyboardScheme = null;
        }

        if (reservedGamepad != null)
        {
            PlayerDeviceManager.Instance.ReleaseGamepad(reservedGamepad);
            reservedGamepad = null;
        }

        chosenScheme = null;
        chosenDeviceIds = Array.Empty<int>();
    }

    // ---------------- API ----------------
    public (string scheme, int[] deviceIds) GetInputSignature()
    {
        return (chosenScheme, chosenDeviceIds);
    }

    public int PlayerIndex => playerIndex;
    public bool HasJoined => hasJoined;

    public void ConfirmDejoin()
    {
        if (!hasJoined) return;
        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = true;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;
        Debug.Log($"[PanelInputHandler] Player {playerIndex} dejoined confirmed");
    }

    public void ForceDejoin()
    {
        if (!hasJoined) return;
        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = true;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;
        OnDejoin?.Invoke(playerIndex);
        Debug.Log($"[PanelInputHandler] Player {playerIndex} force-dejoined");
    }

    // Helper to reset move repeat state
    public void ResetMoveRepeat()
    {
        lastMoveSign = 0;
        nextRepeatTime = 0f;
        lastMoveValue = Vector2.zero;
    }

    public void ResetJoinCompletely(bool requireFreshPressOnRejoin = true)
    {
        // Release any device reservations (keyboard scheme / gamepad)
        ReleaseReservations();

        // Clear join-related flags
        hasJoined = false;
        waitForFreshPress = requireFreshPressOnRejoin;
        requireNeutralAfterJoinViaMove = false;

        // Clear move-repeat state
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;
        nextRepeatTime = 0f;
    }
}