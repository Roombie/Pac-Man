using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Roombie.CharacterSelect;
using UnityEngine.InputSystem.Users;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Events")]
    public System.Action<InputDevice> OnDeviceLost;
    public System.Action<InputDevice> OnNewDevicePaired;

    private int rejoinSlot = -1;
    private bool waitingForRejoin = false;
    private bool rejoinSawKeyboard = false;
    private bool rejoinSawGamepad = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += HandleDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= HandleDeviceChange;
    }

    private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Disconnected:
            case InputDeviceChange.Removed:
                OnDeviceLost?.Invoke(device);
                break;
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Added:
                OnNewDevicePaired?.Invoke(device);
                break;
        }
    }

    /// <summary>
    /// Uses the SAME logic as character selection to determine input ownership
    /// </summary>
    public bool ShouldHandleInputForPlayer(InputAction action, InputControl control, int playerSlot)
    {
        if (action == null || control == null) 
        {
            Debug.LogWarning("[InputManager] ShouldHandleInputForPlayer: action or control is null");
            return false;
        }

        // Convert to 0-based for character selection logic
        int panelIndex = playerSlot - 1;

        // For keyboards, use EXACTLY the same logic as character selection
        if (control.device is Keyboard)
        {
            var scheme = PlayerDeviceManager.Instance.GetKeyboardSchemeForControl(
                action, control,
                isSinglePlayer: !GameManager.Instance.IsTwoPlayerMode,
                panelIndex: panelIndex
            );

            if (string.IsNullOrEmpty(scheme))
            {
                Debug.Log($"[InputManager] Keyboard input rejected - no matching scheme for Player {playerSlot}");
                return false;
            }

            // Check if this keyboard scheme is reserved for this player
            bool reserved = PlayerDeviceManager.Instance.TryReserveKeyboardScheme(scheme);
            Debug.Log($"[InputManager] Keyboard scheme '{scheme}' for Player {playerSlot} - Reserved: {reserved}");
            return reserved;
        }

        // For gamepads, use the same reservation logic as character selection
        if (control.device is Gamepad gamepad)
        {
            bool reserved = PlayerDeviceManager.Instance.TryReserveGamepad(gamepad);
            Debug.Log($"[InputManager] Gamepad {gamepad.deviceId} for Player {playerSlot} - Reserved: {reserved}");
            return reserved;
        }

        Debug.LogWarning($"[InputManager] Unknown device type: {control.device.GetType()}");
        return false;
    }

    /// <summary>
    /// Apply the SAME binding masks as character selection to PlayerInput
    /// </summary>
    public void ApplyCharacterSelectionInputRules(PlayerInput playerInput, int playerSlot)
    {
        if (playerInput == null) 
        {
            Debug.LogWarning("[InputManager] ApplyCharacterSelectionInputRules: playerInput is null");
            return;
        }

        Debug.Log($"[InputManager] Applying character selection rules for Player {playerSlot}");

        // Apply the SAME binding masks as character selection
        if (!GameManager.Instance.IsTwoPlayerMode)
        {
            playerInput.actions.bindingMask = default; // No restrictions in SP
            Debug.Log($"[InputManager] SinglePlayer mode - no binding restrictions");
        }
        else
        {
            // MP: Only this player's keyboard group + Gamepad (EXACTLY like character selection)
            string keyboardGroup = PlayerDeviceManager.Instance.ForPanel(playerSlot - 1);
            playerInput.actions.bindingMask = InputBinding.MaskByGroups(keyboardGroup, "Gamepad");
            Debug.Log($"[InputManager] MultiPlayer mode - binding mask: {keyboardGroup}, Gamepad");
        }

        // Refresh to apply mask (same as character selection)
        playerInput.actions.Disable();
        playerInput.actions.Enable();
    }

    /// <summary>
    /// Apply devices to PlayerInput based on PlayerPrefs (existing logic)
    /// </summary>
    public void ApplyActivePlayerInputDevices(int slot, bool isTwoPlayer)
    {
        string schemeKey = $"P{slot}_Scheme";
        string devicesKey = $"P{slot}_Devices";

        string scheme = PlayerPrefs.GetString(schemeKey, "");
        string deviceIds = PlayerPrefs.GetString(devicesKey, "");

        Debug.Log($"[InputManager] ApplyActivePlayerInputDevices -> Slot={slot}, TwoP={isTwoPlayer}, Scheme='{scheme}', Device='{deviceIds}'");

        // Find the current Pacman's PlayerInput
        var gameManager = GameManager.Instance;
        if (gameManager == null || gameManager.Pacman == null) return;

        var playerInput = gameManager.Pacman.GetComponent<PlayerInput>();
        if (playerInput == null) return;

        // Apply character selection input rules FIRST
        ApplyCharacterSelectionInputRules(playerInput, slot);

        // Then handle device pairing based on scheme
        if (!string.IsNullOrEmpty(scheme))
        {
            if (playerInput.user.valid)
            {
                playerInput.user.UnpairDevices();
            }

            InputDevice[] devices = null;

            if (scheme == "Gamepad" && Gamepad.current != null)
            {
                devices = new InputDevice[] { Gamepad.current };
                Debug.Log($"[InputManager] Paired Gamepad: {Gamepad.current.displayName}");
            }
            else if ((scheme == "P1Keyboard" || scheme == "P2Keyboard" || scheme == "Keyboard&Mouse") && Keyboard.current != null)
            {
                devices = Mouse.current != null 
                    ? new InputDevice[] { Keyboard.current, Mouse.current } 
                    : new InputDevice[] { Keyboard.current };
                Debug.Log($"[InputManager] Paired Keyboard/Mouse");
            }

            if (devices != null)
            {
                foreach (var device in devices)
                {
                    InputUser.PerformPairingWithDevice(device, playerInput.user);
                }

                try
                {
                    playerInput.SwitchCurrentControlScheme(scheme, devices);
                    Debug.Log($"[InputManager] Switched to control scheme: {scheme}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[InputManager] Failed to switch control scheme: {e.Message}");
                }
            }
        }

        // Apply the binding mask again to ensure it's active
        ApplyCharacterSelectionInputRules(playerInput, slot);
    }

    /// <summary>
    /// Start the rejoin process for a specific slot
    /// </summary>
    public void StartRejoinProcess(int slot)
    {
        rejoinSlot = slot;
        waitingForRejoin = true;
        rejoinSawKeyboard = false;
        rejoinSawGamepad = false;
        Debug.Log($"[InputManager] Started rejoin process for slot {slot}");
    }

    /// <summary>
    /// Poll for rejoin input during the rejoin process
    /// </summary>
    public void PollRejoinInput()
    {
        if (!waitingForRejoin) return;

        // Check for keyboard input
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            rejoinSawKeyboard = true;
            Debug.Log("[InputManager] Rejoin: Keyboard detected");
        }

        // Check for gamepad input
        if (Gamepad.current != null && (Gamepad.current.aButton.wasPressedThisFrame || 
                                        Gamepad.current.startButton.wasPressedThisFrame ||
                                        Gamepad.current.buttonSouth.wasPressedThisFrame))
        {
            rejoinSawGamepad = true;
            Debug.Log("[InputManager] Rejoin: Gamepad detected");
        }
    }

    /// <summary>
    /// Clear device preferences for a specific slot
    /// </summary>
    public void ClearActivePlayerDevicesAndPrefs(int slot)
    {
        string schemeKey = $"P{slot}_Scheme";
        string devicesKey = $"P{slot}_Devices";

        PlayerPrefs.DeleteKey(schemeKey);
        PlayerPrefs.DeleteKey(devicesKey);
        PlayerPrefs.Save();

        Debug.Log($"[InputManager] Cleared input prefs for slot {slot}");
    }

    /// <summary>
    /// Set player input reference (optional)
    /// </summary>
    public void SetPlayerInputReference(PlayerInput playerInput)
    {
        // Optional: Store reference if needed
        Debug.Log($"[InputManager] Set player input reference: {playerInput}");
    }

    /// <summary>
    /// Get the current rejoin state
    /// </summary>
    public (bool sawKeyboard, bool sawGamepad) GetRejoinState()
    {
        return (rejoinSawKeyboard, rejoinSawGamepad);
    }

    /// <summary>
    /// Reset rejoin state
    /// </summary>
    public void ResetRejoinState()
    {
        waitingForRejoin = false;
        rejoinSawKeyboard = false;
        rejoinSawGamepad = false;
        rejoinSlot = -1;
        Debug.Log("[InputManager] Reset rejoin state");
    }
}