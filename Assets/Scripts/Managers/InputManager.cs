using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Linq;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.LowLevel;
using Roombie.CharacterSelect;

/// <summary>
/// Centralized manager for handling all player input logic across scenes
/// Handles device pairing/unpairing, control scheme configuration
/// multiplayer setup, and the global dejoin/rejoin system
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private bool enableHotSwap = true;  // Whether device hot-swapping is allowed
    [SerializeField] private bool debugInputEvents = false; // Enable extra debug logs for input events

    // Events for reacting to device connection/loss
    public event Action<InputDevice> OnDeviceLost;
    public event Action<InputDevice> OnDeviceRegained;  // now invoked on reconnect and after rejoin
    public event Action<InputDevice> OnNewDevicePaired;

    // Internal state tracking for input system configuration
    private bool hotSwapEnabled = false;
    private bool listeningForUnpaired = false;
    private bool deviceChangeHooked = false;

    // Global rejoin system state
    [HideInInspector] public bool waitingForRejoin = false; // True if game is waiting for player(s) to reconnect
    private int rejoinSlot; // The slot that initiated the rejoin (for logs only)
    private bool rejoinSawKeyboard; // Tracks if a keyboard triggered rejoin
    private bool rejoinSawGamepad; // Tracks if a gamepad triggered rejoin

    #region Initialization
    private void Awake()
    {
        // Enforce singleton pattern — only one InputManager should exist at any time
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Ensure PlayerDeviceManager exists (used to track reserved devices)
        if (PlayerDeviceManager.Instance == null)
        {
            Debug.LogError("[InputManager] PlayerDeviceManager instance not found!");
        }

        // Persist across scenes (used by gameplay, menus, etc.)
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // Cleanup hooks when destroyed (e.g., on scene unload)
        if (Instance == this)
        {
            Instance = null;
            DisableHotSwap();
        }
    }
    #endregion

    #region Public Interface - Player Configuration
    /// <summary>
    /// Configures a PlayerInput instance (used by Pacman or UI players)
    /// according to stored preferences or PlayerDeviceManager reservations
    /// </summary>
    public void ApplyPlayerInputConfiguration(PlayerInput playerInput, int slot, bool isTwoPlayerMode)
    {
        if (playerInput == null)
        {
            Debug.LogWarning("[InputManager] PlayerInput is null.");
            return;
        }

        // Clone the InputActionAsset to avoid binding conflicts between multiple players
        playerInput.actions = ScriptableObject.Instantiate(playerInput.actions);

        // Retrieve the last saved scheme and device IDs for this player
        string scheme = PlayerPrefs.GetString($"P{slot}_Scheme", "");
        string devicesCsv = PlayerPrefs.GetString($"P{slot}_Devices", "");

        Debug.Log($"[InputManager] Configuring Player {slot}: Scheme='{scheme}', Devices='{devicesCsv}', 2P={isTwoPlayerMode}");

        // Disable auto-switching to avoid Unity overriding assigned devices
        playerInput.neverAutoSwitchControlSchemes = true;
        playerInput.enabled = false;

        // Unpair any previously linked device
        if (playerInput.user.valid)
            playerInput.user.UnpairDevices();

        // Resolve which InputDevice to pair with (keyboard/gamepad)
        InputDevice chosenDevice = ResolveDeviceUsingPlayerDeviceManager(scheme, devicesCsv, slot - 1);

        if (chosenDevice != null)
        {
            try
            {
                // Officially pair the device with this PlayerInput user
                InputUser.PerformPairingWithDevice(chosenDevice, playerInput.user);

                // Apply correct binding mask and control scheme
                ConfigureBindingMask(playerInput, scheme, slot - 1, isTwoPlayerMode);
                ConfigureControlScheme(playerInput, chosenDevice);

                // Reserve the device in PlayerDeviceManager to prevent double usage
                MakeReservationsInPlayerDeviceManager(chosenDevice, scheme, slot - 1);

                Debug.Log($"[InputManager] Player {slot} configured with: {chosenDevice.displayName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[InputManager] Error configuring PlayerInput for slot {slot}: {e.Message}");
            }
        }

        playerInput.enabled = true;
    }

    /// <summary>
    /// Determines the correct InputDevice for a given player
    /// based on previously saved device IDs or fallback logic
    /// </summary>
    private InputDevice ResolveDeviceUsingPlayerDeviceManager(string scheme, string devicesCsv, int playerIndex)
    {
        InputDevice chosenDevice = null;

        // Try to match saved device IDs first
        if (!string.IsNullOrEmpty(devicesCsv))
        {
            var deviceIds = devicesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var device in InputSystem.devices)
            {
                if (deviceIds.Contains(device.deviceId.ToString()))
                {
                    chosenDevice = device;
                    Debug.Log($"[InputManager] Found saved device: {device.displayName}");
                    break;
                }
            }
        }

        // Fallback if no previous device found
        if (chosenDevice == null)
        {
            if (!string.IsNullOrEmpty(scheme) && scheme.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                chosenDevice = Gamepad.current;
                Debug.Log($"[InputManager] Using current gamepad: {chosenDevice?.displayName ?? "NOT FOUND"}");
            }
            else
            {
                chosenDevice = Keyboard.current;
                Debug.Log($"[InputManager] Using keyboard: {chosenDevice?.displayName ?? "NOT FOUND"}");
            }
        }

        return chosenDevice;
    }

    /// <summary>
    /// Applies appropriate binding masks (keyboard splits, etc.)
    /// depending on the control scheme and multiplayer mode
    /// </summary>
    private void ConfigureBindingMask(PlayerInput playerInput, string scheme, int playerIndex, bool isTwoPlayerMode)
    {
        if (!isTwoPlayerMode && scheme.Contains("Keyboard"))
        {
            // In singleplayer keyboard mode, allow all bindings
            playerInput.actions.bindingMask = default;
            Debug.Log($"[InputManager] SP Keyboard mode - No binding mask");
        }
        else if (!string.IsNullOrEmpty(scheme))
        {
            // In multiplayer, mask inputs so each player uses only their assigned scheme
            playerInput.actions.bindingMask = InputBinding.MaskByGroup(scheme);
            Debug.Log($"[InputManager] MP Binding mask: {scheme}");
        }
        else
        {
            // Fallback case
            playerInput.actions.bindingMask = default;
            Debug.Log($"[InputManager] Default binding mask applied");
        }
    }

    /// <summary>
    /// Switches the PlayerInput to the correct control scheme (Keyboard or Gamepad)
    /// </summary>
    private void ConfigureControlScheme(PlayerInput playerInput, InputDevice device)
    {
        try
        {
            if (device is Gamepad)
                playerInput.SwitchCurrentControlScheme("Gamepad", device);
            else if (device is Keyboard)
                playerInput.SwitchCurrentControlScheme("Keyboard", device);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InputManager] Failed to switch control scheme: {e.Message}");
        }

        // Ensure only gameplay map is active
        playerInput.actions.FindActionMap("Player", false)?.Enable();
        playerInput.actions.FindActionMap("UI", false)?.Disable();
    }

    /// <summary>
    /// Marks a device as "reserved" so it cannot be used by another player simultaneously
    /// </summary>
    private void MakeReservationsInPlayerDeviceManager(InputDevice device, string scheme, int playerIndex)
    {
        if (device is Gamepad gamepad)
        {
            PlayerDeviceManager.Instance?.TryReserveGamepad(gamepad);
            Debug.Log($"[InputManager] Reserved gamepad for player {playerIndex + 1}");
        }
        else if (device is Keyboard && !string.IsNullOrEmpty(scheme))
        {
            PlayerDeviceManager.Instance?.TryReserveKeyboardScheme(scheme);
            Debug.Log($"[InputManager] Reserved keyboard scheme '{scheme}' for player {playerIndex + 1}");
        }
    }

    /// <summary>
    /// Enables gameplay actions and disables UI inputs for a PlayerInput
    /// </summary>
    public void SwitchToGameplayMap(PlayerInput playerInput)
    {
        if (playerInput == null) return;
        playerInput.actions.FindActionMap("UI", false)?.Disable();
        playerInput.actions.FindActionMap("Player", false)?.Enable();
    }

    /// <summary>
    /// Enables UI input and disables gameplay actions for a PlayerInput
    /// </summary>
    public void SwitchToUIMap(PlayerInput playerInput)
    {
        if (playerInput == null) return;
        playerInput.actions.FindActionMap("Player", false)?.Disable();
        playerInput.actions.FindActionMap("UI", false)?.Enable();
    }

    #endregion

    #region Game Integration API
    /// <summary>
    /// Called by GameManager to initialize all Pacman players in the gameplay scene
    /// Applies device and scheme settings to each PlayerInput component
    /// </summary>
    public void ConfigurePacmanInputs(Pacman[] pacmans, bool isTwoPlayerMode)
    {
        for (int i = 0; i < pacmans.Length; i++)
        {
            var pacman = pacmans[i];
            if (pacman == null) continue;

            // Ensure each Pacman has a PlayerInput component
            var playerInput = pacman.GetComponent<PlayerInput>() ?? pacman.gameObject.AddComponent<PlayerInput>();

            playerInput.neverAutoSwitchControlSchemes = true;
            playerInput.enabled = false;
            playerInput.actions = ScriptableObject.Instantiate(playerInput.actions);

            // Configure this player
            ApplyPlayerInputConfiguration(playerInput, i + 1, isTwoPlayerMode);
            playerInput.enabled = (i == 0);
        }

        Debug.Log($"[InputManager] Configured {pacmans.Length} Pacman players.");
    }

    /// <summary>
    /// Triggers the dejoin process (pause + waiting for rejoin)
    /// Any player calling this will affect all players
    /// </summary>
    public void RequestDejoin(GameManager gameManager, int currentPlayer, bool isTwoPlayerMode, int totalPlayers)
    {
        if (waitingForRejoin) return;

        // Pause game if currently playing
        if (gameManager.CurrentGameState == GameManager.GameState.Playing)
            gameManager.PauseGame();

        // Hide pause menu and show disconnect overlay instead
        gameManager.pauseUI.HidePause();

        int slot = Mathf.Max(1, isTwoPlayerMode ? currentPlayer : 1);

        // Clear current device preferences and start rejoin flow
        ClearPlayerDevicesAndPrefs(slot);
        StartRejoinProcess(slot);

        // Show overlay with per-player device cards
        gameManager.disconnectOverlay?.InitializeIfNeeded();
        gameManager.disconnectOverlay?.RebuildCards(totalPlayers);
        gameManager.disconnectOverlay?.SetPresenceForSlot(gameManager.CurrentIndex, false, false);
        gameManager.disconnectOverlay?.Show(true);

        waitingForRejoin = true;
        Debug.Log($"[InputManager] Global dejoin triggered by Player {slot}. All players can rejoin.");
    }
    #endregion

    #region Rejoin System (Global)
    /// <summary>
    /// Starts the rejoin process globally. Once started, all players can reconnect
    /// </summary>
    public void StartRejoinProcess(int requestingSlot)
    {
        if (waitingForRejoin) return;

        waitingForRejoin = true;
        rejoinSlot = requestingSlot;
        rejoinSawKeyboard = false;
        rejoinSawGamepad = false;

        Debug.Log($"[InputManager] Rejoin process started by Player {requestingSlot}. All players can now rejoin.");
    }

    /// <summary>
    /// Called every frame during rejoin mode to detect key or button presses
    /// If any device input is detected, rejoin is completed globally
    /// </summary>
    public void PollRejoinInput()
    {
        if (!waitingForRejoin) return;

        var kb = Keyboard.current;
        var gp = Gamepad.current;

        bool kbPressed = kb != null && kb.anyKey.wasPressedThisFrame;
        bool gpPressed = gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame);

        if (kbPressed)
        {
            rejoinSawKeyboard = true;
            rejoinSawGamepad = false;
            OnNewDevicePaired?.Invoke(kb);
            FinishRejoin("Auto"); // use Auto so flags are read
        }
        else if (gpPressed)
        {
            rejoinSawKeyboard = false;
            rejoinSawGamepad = true;
            OnNewDevicePaired?.Invoke(gp);
            FinishRejoin("Auto"); // use Auto so flags are read
        }
    }

    /// <summary>
    /// Completes the global rejoin process, reassigning device data for all players
    /// scheme can be "Keyboard", "Gamepad", or "Auto" to select based on last detected device
    /// Also emits OnDeviceRegained with the device that won the rejoin
    /// </summary>
    public void FinishRejoin(string scheme)
    {
        if (!waitingForRejoin) return;

        string detectedScheme = scheme;

        // Auto-resolve scheme from the flags set by PollRejoinInput
        if (detectedScheme.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (rejoinSawGamepad && Gamepad.current != null)
                detectedScheme = "Gamepad";
            else if (rejoinSawKeyboard && Keyboard.current != null)
                detectedScheme = "Keyboard";
            else
                detectedScheme = Gamepad.current != null ? "Gamepad" : "Keyboard"; // fallback
        }

        Debug.Log($"[InputManager] Finishing global rejoin with scheme: {detectedScheme}");

        // Apply to all current players
        int totalPlayers = GameManager.Instance != null ? GameManager.Instance.TotalPlayers : 2;

        for (int i = 1; i <= totalPlayers; i++)
        {
            PlayerPrefs.SetString($"P{i}_Scheme", detectedScheme);

            if (detectedScheme == "Gamepad" && Gamepad.current != null)
                PlayerPrefs.SetString($"P{i}_Devices", Gamepad.current.deviceId.ToString());
            else if (detectedScheme == "Keyboard" && Keyboard.current != null)
                PlayerPrefs.SetString($"P{i}_Devices", Keyboard.current.deviceId.ToString());
        }

        PlayerPrefs.Save();

        // Fire OnDeviceRegained with the winning device for listeners (e.g., UI, overlays)
        if (detectedScheme == "Gamepad" && Gamepad.current != null)
            OnDeviceRegained?.Invoke(Gamepad.current);
        else if (detectedScheme == "Keyboard" && Keyboard.current != null)
            OnDeviceRegained?.Invoke(Keyboard.current);

        waitingForRejoin = false;
        rejoinSawKeyboard = false; // flags consumed
        rejoinSawGamepad = false;

        Debug.Log("[InputManager] Global rejoin completed for all players.");
    }

    /// <summary>
    /// Returns true if the game is currently in rejoin mode
    /// </summary>
    public bool IsWaitingForRejoin => waitingForRejoin;
    #endregion

    #region Hot Swap
    /// <summary>
    /// Enables hot-swap system if the option is enabled in inspector
    /// </summary>
    public void EnableHotSwapIfNeeded()
    {
        if (enableHotSwap && !hotSwapEnabled)
            EnableHotSwap();
    }

    /// <summary>
    /// Hooks into InputSystem events to detect new unpaired devices and disconnections
    /// </summary>
    private void EnableHotSwap()
    {
        if (hotSwapEnabled) return;

        if (!listeningForUnpaired)
        {
            InputUser.listenForUnpairedDeviceActivity++;
            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
            listeningForUnpaired = true;
        }

        if (!deviceChangeHooked)
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            deviceChangeHooked = true;
        }

        hotSwapEnabled = true;
        if (debugInputEvents) Debug.Log("[InputManager] Hot swap enabled");
    }

    /// <summary>
    /// Unhooks all listeners related to device hot-swapping
    /// </summary>
    private void DisableHotSwap()
    {
        if (!hotSwapEnabled) return;

        if (listeningForUnpaired)
        {
            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
            InputUser.listenForUnpairedDeviceActivity--;
            listeningForUnpaired = false;
        }

        if (deviceChangeHooked)
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            deviceChangeHooked = false;
        }

        hotSwapEnabled = false;
        if (debugInputEvents) Debug.Log("[InputManager] Hot swap disabled");
    }

    /// <summary>
    /// Called automatically when a device is disconnected or reconnected (we care about gamepads)
    /// Emits OnDeviceLost on disconnect and OnDeviceRegained on reconnect
    /// </summary>
    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is not Gamepad) return;

        if (change == InputDeviceChange.Disconnected || change == InputDeviceChange.Removed)
        {
            if (!waitingForRejoin)
                OnDeviceLost?.Invoke(device);
        }
        else if (change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled || change == InputDeviceChange.Added)
        {
            // Notify listeners that a previously missing device is back
            OnDeviceRegained?.Invoke(device);
        }
    }

    /// <summary>
    /// Triggered when a new unpaired device sends input (used for hot-swap)
    /// </summary>
    private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
    {
        if (control?.device is not Gamepad gp) return;
        var owner = InputUser.FindUserPairedToDevice(gp);
        if (owner.HasValue && owner.Value.valid) return;
        OnNewDevicePaired?.Invoke(gp);
    }
    #endregion

    #region Utility
    /// <summary>
    /// Clears stored PlayerPrefs for the specified player slot and resets reservations
    /// Used before rejoin or when resetting input configuration
    /// </summary>
    public void ClearPlayerDevicesAndPrefs(int playerSlot)
    {
        PlayerPrefs.DeleteKey($"P{playerSlot}_Scheme");
        PlayerPrefs.DeleteKey($"P{playerSlot}_Devices");
        PlayerPrefs.Save();
        PlayerDeviceManager.Instance?.ResetAllReservations();
    }
    #endregion
}