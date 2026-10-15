using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System;
using System.Linq;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.LowLevel;
using Roombie.CharacterSelect;

/// <summary>
/// Centralized manager for handling all player input logic across scenes
/// Handles device pairing/unpairing, control scheme configuration
/// multiplayer setup, and the dejoin/rejoin system
/// </summary>
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public const string GamepadScheme = "Gamepad";

    [Header("Actions")]
    [SerializeField] private InputActionAsset gameplayActions; // Cloned once per player (PacmanInputAction)

    [Header("Settings")]
    [SerializeField] private bool enableHotSwap = true;  // Whether device hot-swapping is allowed
    [SerializeField] private bool debugInputEvents = false; // Enable extra debug logs for input events
    [SerializeField] private bool dejoinOnGamepadDisconnect = true; // 2P: rejoin overlay / 1P: pause, when the active gamepad is unplugged

    // Events for reacting to device connection/loss
    public event Action<InputDevice> OnDeviceLost;
    public event Action<InputDevice> OnDeviceRegained;  // invoked on gamepad reconnect
    public event Action<InputDevice> OnNewDevicePaired;
    public event Action<int> OnRejoinCompleted;         // zero-based slot that confirmed its new device

    // Internal state tracking for input system configuration
    private bool hotSwapEnabled = false;
    private bool listeningForUnpaired = false;
    private bool deviceChangeHooked = false;

    /// <summary>Device + control scheme assigned to a player slot during gameplay</summary>
    private struct SlotBinding
    {
        public string scheme;
        public InputDevice device;
    }

    private SlotBinding[] slotBindings = Array.Empty<SlotBinding>();
    private bool isTwoPlayerMode;
    private InputActionAsset actionsTemplate; // Used to look up which keys belong to each keyboard scheme

    private PlayerInputController[] controllers = Array.Empty<PlayerInputController>();
    private int activeSlot;

    // Rejoin system state
    [HideInInspector] public bool waitingForRejoin = false; // True while the rejoin overlay is open
    private int rejoinSlot = -1;          // Zero-based slot that must pick a device
    private int rejoinStartFrame;         // Presses on this frame belong to the dejoin itself
    private string pendingScheme;         // Device chosen on the overlay, not yet confirmed
    private InputDevice pendingDevice;
    private int pendingAssignFrame;       // The press that picks a device must not also confirm it

    #region Initialization

    private void Awake()
    {
        // Destroy only this component: InputManager lives on the GameManager object, so
        // destroying the GameObject would take GameManager, PacmanSpawner, etc. with it
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        if (PlayerDeviceManager.Instance == null)
        {
            Debug.LogError("[InputManager] PlayerDeviceManager instance not found!");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            DisableHotSwap();
        }
    }

    private void Update()
    {
        if (dejoinOnGamepadDisconnect) CheckActiveGamepadDisconnected();
    }

    public bool IsTwoPlayerMode => isTwoPlayerMode;
    #endregion

    #region Public Interface - Player Controllers
    /// <summary>
    /// Creates one PlayerInputController per player. Called once by PacmanSpawner
    /// 1P: P1 can use every device and both keyboard halves
    /// 2P: each player is locked to the device/keyboard half chosen in Character Select
    /// </summary>
    public void SetupPlayers(Pacman[] pacmans, bool isTwoPlayerMode)
    {
        this.isTwoPlayerMode = isTwoPlayerMode;
        waitingForRejoin = false;
        actionsTemplate = ResolveActionsTemplate();

        foreach (var c in controllers)
            if (c != null) Destroy(c.gameObject);

        controllers = new PlayerInputController[pacmans.Length];
        slotBindings = new SlotBinding[pacmans.Length];

        PlayerDeviceManager.Instance?.ResetAllReservations();
        if (isTwoPlayerMode)
        {
            for (int i = 0; i < pacmans.Length; i++)
            {
                slotBindings[i] = ResolveSlotBinding(i);
                MakeReservation(slotBindings[i]);
            }
        }

        if (actionsTemplate == null)
        {
            Debug.LogError("[InputManager] No gameplay InputActionAsset assigned. Players will have no input.");
            return;
        }

        for (int i = 0; i < pacmans.Length; i++)
        {
            var go = new GameObject($"PlayerInput P{i + 1}");
            go.transform.SetParent(transform, false);

            var controller = go.AddComponent<PlayerInputController>();
            controller.Initialize(i, actionsTemplate);
            controller.SetTarget(pacmans[i]);

            if (isTwoPlayerMode) controller.Bind(slotBindings[i].device, slotBindings[i].scheme);
            else controller.Bind(null, null);

            controllers[i] = controller;
        }

        Debug.Log($"[InputManager] Created {controllers.Length} player controller(s). 2P={isTwoPlayerMode}");
    }

    /// <summary>
    /// Gives input to the player whose turn it is; the other player's controller is idle
    /// </summary>
    public void SetActivePlayer(int slotIndex)
    {
        activeSlot = slotIndex;
        for (int i = 0; i < controllers.Length; i++)
            controllers[i]?.SetInputEnabled(i == slotIndex);
    }

    /// <summary>
    /// Turns the active player's gameplay actions on/off (pause menu, rejoin overlay)
    /// Pause navigation is handled by PauseMenuController's own polling
    /// </summary>
    public void SetGameplayInputEnabled(bool enabled)
    {
        if (activeSlot >= 0 && activeSlot < controllers.Length)
            controllers[activeSlot]?.SetInputEnabled(enabled);
    }

    private InputActionAsset ResolveActionsTemplate()
    {
        if (gameplayActions != null) return gameplayActions;

        // Fallback: the EventSystem's UI module uses the same PacmanInputAction asset
        var module = UnityEngine.EventSystems.EventSystem.current != null
            ? UnityEngine.EventSystems.EventSystem.current.currentInputModule as InputSystemUIInputModule
            : null;
        if (module != null && module.actionsAsset != null)
        {
            Debug.LogWarning("[InputManager] gameplayActions not assigned, using the EventSystem's actions asset");
            return module.actionsAsset;
        }
        return null;
    }

    #endregion

    #region Slot Bindings
    /// <summary>
    /// Returns the binding for a slot, re-resolving the device if it went missing
    /// </summary>
    private SlotBinding GetSlotBinding(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotBindings.Length) return default;

        var binding = slotBindings[slotIndex];
        if (binding.device == null || !binding.device.added)
        {
            binding = ResolveSlotBinding(slotIndex);
            slotBindings[slotIndex] = binding;
        }
        return binding;
    }

    /// <summary>
    /// Builds a slot binding from the PlayerPrefs written by Character Select (or a previous rejoin)
    /// </summary>
    private SlotBinding ResolveSlotBinding(int slotIndex)
    {
        int slot = slotIndex + 1;
        string scheme = NormalizeScheme(PlayerPrefs.GetString($"P{slot}_Scheme", ""), slotIndex);
        string devicesCsv = PlayerPrefs.GetString($"P{slot}_Devices", "");
        bool wantsGamepad = scheme == GamepadScheme;

        InputDevice device = null;

        // Saved device IDs first (only accept a device of the right type)
        if (!string.IsNullOrEmpty(devicesCsv))
        {
            var deviceIds = devicesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            device = InputSystem.devices.FirstOrDefault(d =>
                deviceIds.Contains(d.deviceId.ToString()) && (wantsGamepad ? d is Gamepad : d is Keyboard));
        }

        // Fallback: a gamepad no other slot is using, never just Gamepad.current
        if (device == null && wantsGamepad)
            device = Gamepad.all.FirstOrDefault(gp => !IsDeviceUsedByOtherSlot(gp, slotIndex));

        if (device == null)
        {
            if (wantsGamepad)
            {
                Debug.LogWarning($"[InputManager] Slot {slot}: saved gamepad not found, falling back to keyboard");
                scheme = DefaultKeyboardSchemeForSlot(slotIndex);
            }
            device = Keyboard.current;
        }

        Debug.Log($"[InputManager] Slot {slot} resolved: Scheme='{scheme}', Device='{device?.displayName ?? "NONE"}'");
        return new SlotBinding { scheme = scheme, device = device };
    }

    /// <summary>
    /// Maps stored scheme names to schemes that exist in the actions asset
    /// Older builds stored "Keyboard"/"Keyboard&Mouse", which the asset doesn't define
    /// </summary>
    private static string NormalizeScheme(string scheme, int slotIndex)
    {
        if (!string.IsNullOrEmpty(scheme) && scheme.IndexOf(GamepadScheme, StringComparison.OrdinalIgnoreCase) >= 0)
            return GamepadScheme;

        if (!string.IsNullOrEmpty(scheme) && System.Text.RegularExpressions.Regex.IsMatch(scheme, @"^P[1-8]Keyboard$"))
            return scheme;

        return DefaultKeyboardSchemeForSlot(slotIndex);
    }

    private static string DefaultKeyboardSchemeForSlot(int slotIndex)
    {
        return PlayerDeviceManager.Instance != null
            ? PlayerDeviceManager.Instance.ForPanel(slotIndex)
            : $"P{slotIndex + 1}Keyboard";
    }

    private static bool IsKeyboardScheme(string scheme) =>
        !string.IsNullOrEmpty(scheme) && scheme.EndsWith("Keyboard", StringComparison.Ordinal);

    private bool IsDeviceUsedByOtherSlot(InputDevice device, int slotIndex)
    {
        for (int i = 0; i < slotBindings.Length; i++)
            if (i != slotIndex && slotBindings[i].device == device) return true;
        return false;
    }

    private bool IsKeyboardSchemeUsedByOtherSlot(string scheme, int slotIndex)
    {
        for (int i = 0; i < slotBindings.Length; i++)
            if (i != slotIndex && slotBindings[i].scheme == scheme) return true;
        return false;
    }

    /// <summary>
    /// Stores a slot's device in memory and in PlayerPrefs, and updates reservations
    /// </summary>
    private void SetSlotBinding(int slotIndex, string scheme, InputDevice device)
    {
        var previous = slotBindings[slotIndex];
        ReleaseReservation(previous);

        slotBindings[slotIndex] = new SlotBinding { scheme = scheme, device = device };

        int slot = slotIndex + 1;
        PlayerPrefs.SetString($"P{slot}_Scheme", scheme);
        PlayerPrefs.SetString($"P{slot}_Devices", device.deviceId.ToString());
        PlayerPrefs.Save();

        MakeReservation(slotBindings[slotIndex]);
    }

    private void MakeReservation(SlotBinding binding)
    {
        var pdm = PlayerDeviceManager.Instance;
        if (pdm == null) return;

        if (binding.device is Gamepad gp) pdm.TryReserveGamepad(gp);
        else if (IsKeyboardScheme(binding.scheme)) pdm.TryReserveKeyboardScheme(binding.scheme);
    }

    private void ReleaseReservation(SlotBinding binding)
    {
        var pdm = PlayerDeviceManager.Instance;
        if (pdm == null) return;

        if (binding.device is Gamepad gp) pdm.ReleaseGamepad(gp);
        else if (IsKeyboardScheme(binding.scheme)) pdm.ReleaseKeyboardScheme(binding.scheme);
    }
    #endregion

    #region Game Integration API
    /// <summary>
    /// Opens the rejoin overlay for the player that pressed Select (Special)
    /// Only that player's device is released; the others keep theirs
    /// </summary>
    public void RequestDejoin(GameManager gameManager, int currentPlayer, bool isTwoPlayerMode, int totalPlayers)
    {
        if (waitingForRejoin || gameManager == null) return;
        if (!this.isTwoPlayerMode) return; // 1P already accepts every device, nothing to reassign
        if (gameManager.CurrentGameState != GameManager.GameState.Playing) return;

        PauseMenuController.Instance.Pause();
        PauseMenuController.Instance.HideMenu(); // overlay replaces the pause menu

        rejoinSlot = Mathf.Clamp(currentPlayer - 1, 0, Mathf.Max(0, slotBindings.Length - 1));
        rejoinStartFrame = Time.frameCount;
        pendingScheme = null;
        pendingDevice = null;
        waitingForRejoin = true;

        // Release this slot's device so it can be picked again (or by someone else)
        if (rejoinSlot < slotBindings.Length)
            ReleaseReservation(slotBindings[rejoinSlot]);

        var overlay = gameManager.disconnectOverlay;
        if (overlay != null)
        {
            overlay.InitializeIfNeeded();
            overlay.SetMode(totalPlayers, new[] { rejoinSlot });
            overlay.ShowWaitingForPresence();

            // Other players keep showing the device they already have
            for (int i = 0; i < slotBindings.Length; i++)
            {
                if (i == rejoinSlot) continue;
                var d = slotBindings[i].device;
                overlay.SetPresenceForSlot(i, d is Keyboard, d is Gamepad);
            }
        }

        Debug.Log($"[InputManager] Dejoin triggered by Player {rejoinSlot + 1}. Waiting for a device.");
    }
    #endregion

    #region Rejoin System
    /// <summary>
    /// Called every frame while the rejoin overlay is open
    /// Any button on a free gamepad, or a key from a free keyboard scheme, picks the device
    /// (pressing on another device swaps it). A (Confirm) on the picked device finishes
    /// </summary>
    public void PollRejoinInput()
    {
        if (!waitingForRejoin) return;
        if (Time.frameCount <= rejoinStartFrame) return; // ignore the Select press that opened the overlay

        if (pendingDevice != null && Time.frameCount > pendingAssignFrame && WasConfirmPressed(pendingScheme, pendingDevice))
        {
            FinishRejoin();
            return;
        }

        if (!TryDetectDevicePress(out var scheme, out var device)) return;
        if (scheme == pendingScheme && device == pendingDevice) return;

        pendingScheme = scheme;
        pendingDevice = device;
        pendingAssignFrame = Time.frameCount;
        OnNewDevicePaired?.Invoke(device);

        var overlay = GameManager.Instance?.disconnectOverlay;
        overlay?.SetPresenceForSlot(rejoinSlot, device is Keyboard, device is Gamepad);
        overlay?.ShowConfirmHint(true);

        Debug.Log($"[InputManager] Rejoin: slot {rejoinSlot + 1} picked {scheme} ({device.displayName})");
    }

    /// <summary>
    /// Finds which device (and which keyboard half) was pressed this frame, skipping
    /// devices that belong to other players
    /// </summary>
    private bool TryDetectDevicePress(out string scheme, out InputDevice device)
    {
        scheme = null;
        device = null;

        foreach (var gp in Gamepad.all)
        {
            if (IsDeviceUsedByOtherSlot(gp, rejoinSlot)) continue;
            if (AnyGamepadButtonPressed(gp))
            {
                scheme = GamepadScheme;
                device = gp;
                return true;
            }
        }

        var kb = Keyboard.current;
        if (kb == null || !kb.anyKey.wasPressedThisFrame) return false;

        if (!isTwoPlayerMode)
        {
            // Single player owns the whole keyboard
            scheme = DefaultKeyboardSchemeForSlot(rejoinSlot);
            device = kb;
            return true;
        }

        // Prefer this slot's own half of the keyboard, then any free half
        string own = DefaultKeyboardSchemeForSlot(rejoinSlot);
        foreach (var candidate in new[] { own }.Concat(KeyboardSchemes().Where(s => s != own)))
        {
            if (IsKeyboardSchemeUsedByOtherSlot(candidate, rejoinSlot)) continue;
            if (AnyKeyOfSchemePressed(kb, candidate, null))
            {
                scheme = candidate;
                device = kb;
                return true;
            }
        }
        return false;
    }

    private static bool AnyGamepadButtonPressed(Gamepad gp)
    {
        return gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame
            || gp.buttonWest.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame
            || gp.startButton.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame
            || gp.leftShoulder.wasPressedThisFrame || gp.rightShoulder.wasPressedThisFrame;
    }

    /// <summary>
    /// A (Confirm) on the picked device: South on its gamepad, or the scheme's UI/Submit key
    /// </summary>
    private bool WasConfirmPressed(string scheme, InputDevice device)
    {
        if (device is Gamepad gp) return gp.buttonSouth.wasPressedThisFrame;

        if (device is Keyboard kb)
        {
            if (!isTwoPlayerMode)
                return KeyboardSchemes().Any(s => AnyKeyOfSchemePressed(kb, s, "Submit"));
            return AnyKeyOfSchemePressed(kb, scheme, "Submit");
        }
        return false;
    }

    /// <summary>
    /// True if a key bound to the given keyboard scheme was pressed this frame
    /// actionName limits the check to one action (e.g. "Submit"); null checks every action
    /// </summary>
    private bool AnyKeyOfSchemePressed(Keyboard kb, string scheme, string actionName)
    {
        if (actionsTemplate == null) return actionName == null && kb.anyKey.wasPressedThisFrame;

        foreach (var action in actionsTemplate)
        {
            if (actionName != null && action.name != actionName) continue;

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || !BindingInGroup(binding, scheme)) continue;

                var control = InputControlPath.TryFindControl(kb, binding.effectivePath) as KeyControl;
                if (control != null && control.wasPressedThisFrame) return true;
            }
        }
        return false;
    }

    private static bool BindingInGroup(InputBinding binding, string group)
    {
        if (string.IsNullOrEmpty(binding.groups)) return false;
        foreach (var token in binding.groups.Split(InputBinding.Separator))
            if (token.Trim() == group) return true;
        return false;
    }

    private string[] KeyboardSchemes()
    {
        if (actionsTemplate == null) return new[] { "P1Keyboard", "P2Keyboard" };
        return actionsTemplate.controlSchemes.Select(s => s.bindingGroup).Where(IsKeyboardScheme).ToArray();
    }

    /// <summary>
    /// Stores the confirmed device for the rejoining slot and notifies listeners
    /// </summary>
    public void FinishRejoin()
    {
        if (!waitingForRejoin || pendingDevice == null) return;

        int slot = rejoinSlot;
        SetSlotBinding(slot, pendingScheme, pendingDevice);
        if (slot < controllers.Length) controllers[slot]?.Bind(pendingDevice, pendingScheme);

        waitingForRejoin = false;
        pendingScheme = null;
        pendingDevice = null;
        rejoinSlot = -1;

        Debug.Log($"[InputManager] Rejoin completed for Player {slot + 1}: {slotBindings[slot].scheme} / {slotBindings[slot].device.displayName}");
        OnRejoinCompleted?.Invoke(slot);
    }

    /// <summary>
    /// Reacts when the playing slot's gamepad is unplugged
    /// 2P: opens the rejoin overlay for that player. 1P: just pauses (keyboard still works)
    /// </summary>
    private void CheckActiveGamepadDisconnected()
    {
        if (waitingForRejoin) return;

        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentGameState != GameManager.GameState.Playing) return;
        if (activeSlot < 0 || activeSlot >= controllers.Length || controllers[activeSlot] == null) return;

        if (!isTwoPlayerMode)
        {
            var controller = controllers[activeSlot];
            if (controller.LastUsedDevice is Gamepad lost && !lost.added)
            {
                Debug.Log("[InputManager] Gamepad disconnected in 1P. Pausing.");
                controller.ClearLastUsedDevice();
                OnDeviceLost?.Invoke(lost);
                PauseMenuController.Instance.Pause();
            }
            return;
        }

        var device = slotBindings[activeSlot].device;
        if (device is Gamepad && !device.added)
        {
            Debug.Log($"[InputManager] Player {activeSlot + 1}'s gamepad disconnected. Opening rejoin overlay.");
            OnDeviceLost?.Invoke(device);
            RequestDejoin(gm, activeSlot + 1, isTwoPlayerMode, gm.TotalPlayers);
        }
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
}
