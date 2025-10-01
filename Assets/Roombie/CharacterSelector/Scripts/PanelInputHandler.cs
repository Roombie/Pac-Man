using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Roombie.CharacterSelect;
using UnityEngine.InputSystem.Users;

[RequireComponent(typeof(PlayerInput))]
public class PanelInputHandler : MonoBehaviour
{
    public event Action<int> OnSubmit;
    public event Action<int> OnCancel;
    public event Action<int, int> OnMove;
    public event Action<int> OnDejoin;

    private PlayerInput playerInput;
    private InputAction submit;
    private InputAction cancel;
    private InputAction move;
    private InputAction dejoin;

    private int playerIndex;

    // nav tuning
    private float moveDeadzone;
    private float initialRepeatDelay;
    private float repeatInterval;
    private bool allowHoldRepeat;

    // repeat state
    private int   lastMoveSign   = 0;
    private float nextRepeatTime = 0f;
    private Vector2 lastMoveValue = Vector2.zero;

    // reservations
    private string reservedKeyboardScheme;
    private Gamepad reservedGamepad;

    private bool hasJoined = false;
    private bool waitForFreshPress = false;
    private bool requireNeutralAfterJoinViaMove = false;

    private string   chosenScheme;
    private int[]    chosenDeviceIds = Array.Empty<int>();

    private void OnEnable() { waitForFreshPress = false; }

    private void Update()
    {
        if (requireNeutralAfterJoinViaMove) return;
        if (!hasJoined || !allowHoldRepeat || lastMoveSign == 0) return;

        int currentSign = Mathf.Abs(lastMoveValue.x) > moveDeadzone ? (int)Mathf.Sign(lastMoveValue.x) : 0;
        if (currentSign == 0) { lastMoveSign = 0; return; }

        if (Time.time >= nextRepeatTime)
        {
            nextRepeatTime = Time.time + repeatInterval;
            OnMove?.Invoke(playerIndex, currentSign);
        }
    }

    public void Initialize(int index, float deadzone, float repeatDelay, float repeatInterval, bool allowRepeat)
    {
        ResetJoinCompletely(false);

        playerIndex = index;
        playerInput = GetComponent<PlayerInput>();

        if (playerInput != null)
        {
            // DO NOT let PlayerInput auto-switch schemes on device activity
            playerInput.neverAutoSwitchControlSchemes = true;

            // Start with no devices paired to this PlayerInput
            if (playerInput.user.valid)
                playerInput.user.UnpairDevices();

            // Clear current control scheme (keeps the inspector debug honest)
            try { playerInput.SwitchCurrentControlScheme(null, (InputDevice[])null); } catch { }
            playerInput.actions.devices = null;
        }

        moveDeadzone = deadzone;
        initialRepeatDelay = repeatDelay;
        this.repeatInterval = repeatInterval;
        allowHoldRepeat = allowRepeat;

        var asset = playerInput.actions;

        // PRE-JOIN MASK:
        // SP: open. MP: only this panel's keyboard group + Gamepad
        if (CharacterSelectionManager.Instance.IsSinglePlayer)
        {
            asset.bindingMask = default;
        }
        else
        {
            var group = PlayerDeviceManager.Instance.ForPanel(playerIndex); // "P1Keyboard"/"P2Keyboard"
            asset.bindingMask = string.IsNullOrEmpty(group)
                                ? default
                                : InputBinding.MaskByGroups(group, "Gamepad");
        }

        submit = asset["Submit"];
        cancel = asset["Cancel"];
        move = asset["Move"];
        dejoin = asset["Select"];

        submit.performed += OnSubmitPerformed;
        cancel.performed += OnCancelPerformed;
        move.performed += OnMovePerformed;
        move.canceled += OnMoveCanceled;
        dejoin.performed += OnDejoinPerformed;
        dejoin.canceled += OnDejoinCanceled;

        asset.Disable();
        asset.Enable(); // apply mask immediately
    }

    private void OnDisable()
    {
        if (submit != null) submit.performed -= OnSubmitPerformed;
        if (cancel != null) cancel.performed -= OnCancelPerformed;
        if (move != null) { move.performed -= OnMovePerformed; move.canceled -= OnMoveCanceled; }
        if (dejoin != null) { dejoin.performed -= OnDejoinPerformed; dejoin.canceled -= OnDejoinCanceled; }

        if (playerInput != null && playerInput.actions != null)
        {
            playerInput.actions.bindingMask = default;
            playerInput.actions.Disable();
        }

        ReleaseReservations();
        waitForFreshPress = false;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;

        ResetJoinCompletely(true);
    }

    // -------- handlers --------
    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined) { TryClaimFromContext(ctx); return; }
        if (!IsEventFromReserved(ctx)) return;
        OnSubmit?.Invoke(playerIndex);
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined) { TryClaimFromContext(ctx); return; }
        if (!IsEventFromReserved(ctx)) return;
        OnCancel?.Invoke(playerIndex);
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined)
        {
            TryClaimFromContext(ctx);
            if (hasJoined)
            {
                requireNeutralAfterJoinViaMove = true;
                lastMoveSign = 0;
                lastMoveValue = Vector2.zero;
            }
            return;
        }

        if (!IsEventFromReserved(ctx)) return;

        Vector2 mv = ctx.ReadValue<Vector2>();
        lastMoveValue = mv;

        if (requireNeutralAfterJoinViaMove) return;

        int sign = Mathf.Abs(mv.x) > moveDeadzone ? (int)Mathf.Sign(mv.x) : 0;
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
        if (hasJoined && !IsEventFromReserved(ctx)) return;

        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;

        if (requireNeutralAfterJoinViaMove)
            requireNeutralAfterJoinViaMove = false;
    }

    private void OnDejoinPerformed(InputAction.CallbackContext ctx)
    {
        if (!hasJoined) return;
        if (!IsEventFromReserved(ctx)) return;
        OnDejoin?.Invoke(playerIndex);
        Debug.Log($"[PanelInputHandler] Player {playerIndex} requested dejoin");
    }

    private void OnDejoinCanceled(InputAction.CallbackContext ctx)
    {
        if (waitForFreshPress)
        {
            waitForFreshPress = false;
            Debug.Log($"[PanelInputHandler] Player {playerIndex} fresh press reset, can rejoin");
        }
    }

    // -------- claim logic --------
    public void TryClaimFromContext(InputAction.CallbackContext ctx)
    {
        if (waitForFreshPress) return;
        var control = ctx.control;
        if (control == null) return;

        if (!CharacterSelectionManager.Instance) return;

        // Gamepad → first free slot (P1 first, then P2)
        if (control.device is Gamepad gamepad)
        {
            if (!PlayerDeviceManager.Instance.CanClaimNow()) return;

            int nextFree = CharacterSelectionManager.Instance.GetNextFreeJoinSlot();
            if (nextFree < 0) return;

            if (playerIndex != nextFree)
            {
                // Route this gamepad press to the correct (first free) panel
                var target = CharacterSelectionManager.Instance.GetPanelInput(nextFree);
                if (target != null) target.TryClaimSpecificGamepad(gamepad);
                return;
            }

            // I'm the first free panel; claim directly
            TryClaimSpecificGamepad(gamepad);
            return;
        }

        // Keyboard → panel-strict in MP, flexible in SP
        if (control.device is Keyboard kb)
        {
            var scheme = PlayerDeviceManager.Instance.GetKeyboardSchemeForControl(
                ctx.action, control,
                isSinglePlayer: CharacterSelectionManager.Instance.IsSinglePlayer,
                panelIndex: playerIndex
            );

            if (string.IsNullOrEmpty(scheme))
            {
                PlayerDeviceManager.DebugLogGroupMismatch(ctx.action, control, playerIndex);
                return;
            }

            if (PlayerDeviceManager.Instance.TryReserveKeyboardScheme(scheme))
            {
                reservedKeyboardScheme = scheme;
                chosenScheme = scheme;
                chosenDeviceIds = new[] { kb.deviceId };
                hasJoined = true;

                if (playerInput != null)
                {
                    if (playerInput.user.valid) playerInput.user.UnpairDevices(); // defensive reset
                    var devices = Mouse.current != null
                        ? new InputDevice[] { Keyboard.current, Mouse.current }
                        : new InputDevice[] { Keyboard.current };

                    foreach (var d in devices) InputUser.PerformPairingWithDevice(d, playerInput.user);

                    // Use your real scheme name here ("Keyboard&Mouse" or "Keyboard")
                    try { playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", devices); } catch { }

                    // Keep the mask strict to this panel's keyboard group
                    playerInput.actions.bindingMask = InputBinding.MaskByGroup(reservedKeyboardScheme);
                    playerInput.actions.Disable(); playerInput.actions.Enable();
                }

                LockToReservation();

                Debug.Log($"[PanelInputHandler] P{playerIndex + 1} joined with {scheme}");
                CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
                PlayerDeviceManager.Instance.MarkClaimed(); // mark only on successful claim
            }
            return;
        }
    }

    public void TryClaimSpecificGamepad(Gamepad gamepad)
    {
        if (gamepad == null) return;
        if (!PlayerDeviceManager.Instance.CanClaimNow()) return;
        if (!PlayerDeviceManager.Instance.TryReserveGamepad(gamepad)) return;

        reservedGamepad = gamepad;
        chosenScheme = "Gamepad";
        chosenDeviceIds = new[] { gamepad.deviceId };
        hasJoined = true;

        if (playerInput != null)
        {
            var user = playerInput.user;
            try { if (user.valid) user.UnpairDevices(); } catch { }
            InputUser.PerformPairingWithDevice(gamepad, user);

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.SwitchCurrentControlScheme("Gamepad", gamepad);

            playerInput.actions.bindingMask = InputBinding.MaskByGroup("Gamepad");
            playerInput.actions.Disable(); playerInput.actions.Enable();
        }

        Debug.Log($"[PanelInputHandler] P{playerIndex + 1} joined with Gamepad (routed).");
        CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
        PlayerDeviceManager.Instance.MarkClaimed();
    }

    private void ReleaseReservations()
    {
        if (playerInput != null && playerInput.user.valid)
        {
            try { playerInput.user.UnpairDevices(); } catch { }
        }

        if (!string.IsNullOrEmpty(reservedKeyboardScheme))
        {
            PlayerDeviceManager.Instance.ReleaseKeyboardScheme(reservedKeyboardScheme);
            reservedKeyboardScheme = null;
        }

        if (reservedGamepad != null)
        {
            // Unpair from our user if we can
            if (playerInput != null)
            {
                var user = playerInput.user;
                try { user.UnpairDevices(); } catch { }
            }
            // Always release globally so the device can be re-claimed
            PlayerDeviceManager.Instance.ReleaseGamepad(reservedGamepad);
            reservedGamepad = null;
        }

        chosenScheme = null;
        chosenDeviceIds = Array.Empty<int>();
    }

    private bool IsEventFromReserved(InputAction.CallbackContext ctx)
    {
        if (CharacterSelectionManager.Instance.IsSinglePlayer)
        {
            // In SP, if a gamepad claimed the panel, only it can control; else any keyboard is allowed.
            if (reservedGamepad != null)
                return ctx.control.device is Gamepad gp && gp.deviceId == reservedGamepad.deviceId;
            return ctx.control.device is Keyboard;
        }

        // MP strict
        var control = ctx.control;
        if (control == null) return false;

        if (reservedGamepad != null)
            return control.device is Gamepad g && g.deviceId == reservedGamepad.deviceId;

        if (!string.IsNullOrEmpty(reservedKeyboardScheme))
            return PlayerDeviceManager.GroupsContainForPanel(ctx.action, control, reservedKeyboardScheme, playerIndex);

        return false;
    }

    // ---------- helpers for safe scheme/mask ops ----------
    private void NeutralizeSchemeIfReady()
    {
        if (playerInput == null) return;
        playerInput.neverAutoSwitchControlSchemes = true;
        try { playerInput.SwitchCurrentControlScheme(null, (InputDevice[])null); } catch { }
        playerInput.actions.devices = null;
    }

    // Lock/clear masks --------------------------------------------------------
    private void LockToReservation()
    {
        if (playerInput == null || playerInput.actions == null) return;

        if (CharacterSelectionManager.Instance.IsSinglePlayer)
        {
            playerInput.actions.bindingMask = reservedGamepad != null
                ? InputBinding.MaskByGroup("Gamepad")
                : default;
        }
        else
        {
            if (!string.IsNullOrEmpty(reservedKeyboardScheme))
                playerInput.actions.bindingMask = InputBinding.MaskByGroup(reservedKeyboardScheme);
            else if (reservedGamepad != null)
                playerInput.actions.bindingMask = InputBinding.MaskByGroup("Gamepad");
        }
        playerInput.actions.Disable(); playerInput.actions.Enable();
    }

    private void ApplyPreJoinMask()
    {
        // Put PlayerInput into a neutral, autoswitchable state if available
        NeutralizeSchemeIfReady();

        if (playerInput == null || playerInput.actions == null) return;

        if (CharacterSelectionManager.Instance != null && !CharacterSelectionManager.Instance.IsSinglePlayer)
        {
            var group = PlayerDeviceManager.Instance.ForPanel(playerIndex);
            playerInput.actions.bindingMask = string.IsNullOrEmpty(group)
                ? default
                : InputBinding.MaskByGroups(group, "Gamepad");
        }
        else
        {
            playerInput.actions.bindingMask = default;
        }

        playerInput.actions.Disable();
        playerInput.actions.Enable();
    }

    private IEnumerator ClearWaitNextFrame()
    {
        yield return null; // let any canceled events happen if they will
        waitForFreshPress = false; // always clear, even if cancel never fired (e.g., device unpaired)
    }

    // ---------------- API ----------------
    public (string scheme, int[] deviceIds) GetInputSignature() => (chosenScheme, chosenDeviceIds);
    public int PlayerIndex => playerIndex;
    public bool HasJoined => hasJoined;

    public void ConfirmDejoin()
    {
        if (!hasJoined) return;

        bool wasGamepad = reservedGamepad != null;

        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = wasGamepad;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;

        // Neutralize scheme (only if playerInput exists), then restore pre-join mask
        NeutralizeSchemeIfReady();
        ApplyPreJoinMask();

        // Lift the fresh-press gate next frame
        StartCoroutine(ClearWaitNextFrame());

        Debug.Log($"[PanelInputHandler] Player {playerIndex} dejoined confirmed (wasGamepad={wasGamepad})");
    }

    public void ForceDejoin()
    {
        if (!hasJoined) return;

        bool wasGamepad = reservedGamepad != null;

        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = true;
        requireNeutralAfterJoinViaMove = false;
        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;
        OnDejoin?.Invoke(playerIndex);

        NeutralizeSchemeIfReady();
        ApplyPreJoinMask();

        StartCoroutine(ClearWaitNextFrame());

        Debug.Log($"[PanelInputHandler] Player {playerIndex} force-dejoined (wasGamepad={wasGamepad})");
    }

    public void ResetMoveRepeat()
    {
        lastMoveSign = 0;
        nextRepeatTime = 0f;
        lastMoveValue = Vector2.zero;
    }

    public void ResetJoinCompletely(bool requireFreshPressOnRejoin = true)
    {
        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = requireFreshPressOnRejoin;
        requireNeutralAfterJoinViaMove = false;

        lastMoveSign = 0;
        lastMoveValue = Vector2.zero;
        nextRepeatTime = 0f;

        // If called before Initialize(), playerInput is null — safe now
        NeutralizeSchemeIfReady();
        ApplyPreJoinMask();
    }

    public bool IsUsingGamepad(Gamepad gp)
    {
        return HasJoined && reservedGamepad != null && gp != null && reservedGamepad.deviceId == gp.deviceId;
    }

    public Gamepad ReservedGamepad => reservedGamepad;
}