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

    // Reservation tracking
    private string reservedKeyboardScheme;
    private Gamepad reservedGamepad;

    private bool hasJoined = false;

    // Fresh press control: prevents instant re-join after dejoin
    private bool waitForFreshPress = false;

    private void OnEnable()
    {
        waitForFreshPress = false; // reset when menu opens
    }

    public void Initialize(int index)
    {
        playerIndex = index;
        playerInput = GetComponent<PlayerInput>();

        var actions = playerInput.actions;
        submit = actions["Submit"];
        cancel = actions["Cancel"];
        move = actions["Move"];
        dejoin = actions["Select"];

        submit.performed += ctx =>
        {
            if (!hasJoined)
            {
                TryClaimFromContext(ctx);
                return; // first press only joins
            }
            OnSubmit?.Invoke(playerIndex);
        };

        cancel.performed += ctx =>
        {
            if (!hasJoined)
            {
                TryClaimFromContext(ctx);
                return;
            }
            OnCancel?.Invoke(playerIndex);
        };

        move.performed += ctx =>
        {
            var x = ctx.ReadValue<Vector2>().x;
            if (Mathf.Abs(x) <= 0.5f) return;

            if (!hasJoined)
            {
                TryClaimFromContext(ctx);
                return;
            }
            OnMove?.Invoke(playerIndex, x > 0 ? +1 : -1);
        };

        // Notify intent to dejoin, but don't finalize here
        dejoin.performed += ctx =>
        {
            if (hasJoined)
            {
                OnDejoin?.Invoke(playerIndex);
                Debug.Log($"[PanelInputHandler] Player {playerIndex} requested dejoin");
            }
        };

        // Fresh press reset when button is released
        dejoin.canceled += ctx =>
        {
            if (waitForFreshPress)
            {
                waitForFreshPress = false;
                Debug.Log($"[PanelInputHandler] Player {playerIndex} fresh press reset, can rejoin");
            }
        };
    }

    private void OnDisable()
    {
        ReleaseReservations();
        waitForFreshPress = false; // allow joining next time
    }

    // ---------------- Claim Logic ----------------
    private void TryClaimFromContext(InputAction.CallbackContext ctx)
    {
        if (waitForFreshPress) return;

        var control = ctx.control;
        if (control == null) return;

        // 🎮 Gamepad
        if (control.device is Gamepad gamepad)
        {
            if (PlayerDeviceManager.Instance.TryReserveGamepad(gamepad))
            {
                reservedGamepad = gamepad;
                chosenScheme = "Gamepad";
                chosenDeviceIds = new[] { gamepad.deviceId };
                hasJoined = true;

                LockActionsToScheme(chosenScheme);

                Debug.Log($"[PanelInputHandler] Player {playerIndex} joined with Gamepad");
                CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
            }
            return;
        }

        // ⌨️ Keyboard
        if (control.device is Keyboard)
        {
            string scheme = PlayerDeviceManager.Instance.GetKeyboardSchemeForControl(
                ctx.action,
                control,
                CharacterSelectionManager.Instance.IsSinglePlayer,
                playerIndex
            );

            // fallback in singleplayer
            if (string.IsNullOrEmpty(scheme) && CharacterSelectionManager.Instance.IsSinglePlayer)
            {
                scheme = PlayerDeviceManager.Instance.ReserveNextKeyboardScheme();
            }

            if (!string.IsNullOrEmpty(scheme) && PlayerDeviceManager.Instance.TryReserveKeyboardScheme(scheme))
            {
                reservedKeyboardScheme = scheme;
                chosenScheme = scheme;
                chosenDeviceIds = new[] { control.device.deviceId };
                hasJoined = true;

                LockActionsToScheme(chosenScheme);

                Debug.Log($"[PanelInputHandler] Player {playerIndex} joined with {scheme}");
                CharacterSelectionManager.Instance.NotifyPanelJoined(playerIndex);
            }
        }
    }

    private void LockActionsToScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme) || playerInput == null) return;

        var actions = playerInput.actions;
        actions.Disable();
        actions.bindingMask = InputBinding.MaskByGroup(scheme); // 🔒 lock actions
        actions.Enable();

        Debug.Log($"[PanelInputHandler] Player {playerIndex} locked to scheme {scheme}");
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

    /// <summary>
    /// Called by CharacterSelectionManager to finalize a dejoin.
    /// </summary>
    public void ConfirmDejoin()
    {
        if (!hasJoined) return;

        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = true;

        Debug.Log($"[PanelInputHandler] Player {playerIndex} dejoined confirmed");
    }

    public void ForceDejoin()
    {
        if (!hasJoined) return;

        ReleaseReservations();
        hasJoined = false;
        waitForFreshPress = true;
        OnDejoin?.Invoke(playerIndex);

        Debug.Log($"[PanelInputHandler] Player {playerIndex} force-dejoined");
    }
}