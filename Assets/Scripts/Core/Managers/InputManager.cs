using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System;
using System.Collections.Generic;
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
    public event Action OnRejoinCompleted;              // every slot has a device again and someone confirmed

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
    private readonly Dictionary<(int deviceId, string scheme, string action), ButtonControl[]> controlCache = new();

    private PlayerInputController[] controllers = Array.Empty<PlayerInputController>();
    private int activeSlot;

    // Rejoin system state
    [HideInInspector] public bool waitingForRejoin = false; // True while the rejoin overlay is open
    private SlotBinding[] draftBindings = Array.Empty<SlotBinding>(); // Roster being edited on the overlay
    private int rejoinStartFrame;         // Presses on this frame belong to the dejoin itself
    private int lastRosterChangeFrame;    // The press that joins/dejoins must not also confirm

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
        if (dejoinOnGamepadDisconnect) CheckGamepadDisconnected();
        CheckIdlePlayersDejoin();
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
        controlCache.Clear();

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
    /// Opens the rejoin overlay because a player pressed Select (Special), whether or not it's their turn
    /// Only that player's device is released; the others keep theirs but can still dejoin from the overlay
    /// </summary>
    public void RequestDejoin(GameManager gameManager, int playerNumber, bool isTwoPlayerMode, int totalPlayers)
    {
        if (waitingForRejoin || gameManager == null) return;
        if (!this.isTwoPlayerMode) return; // 1P already accepts every device, nothing to reassign
        if (gameManager.CurrentGameState != GameManager.GameState.Playing) return;

        PauseMenuController.Instance.Pause();
        PauseMenuController.Instance.HideMenu(); // overlay replaces the pause menu

        int slot = Mathf.Clamp(playerNumber - 1, 0, Mathf.Max(0, slotBindings.Length - 1));
        draftBindings = (SlotBinding[])slotBindings.Clone();
        rejoinStartFrame = Time.frameCount;
        lastRosterChangeFrame = Time.frameCount;
        waitingForRejoin = true;

        var overlay = gameManager.disconnectOverlay;
        if (overlay != null)
        {
            overlay.InitializeIfNeeded();
            overlay.SetMode(totalPlayers, new[] { slot });
            overlay.ShowWaitingForPresence();
        }

        // Every other player keeps showing the device they already have
        for (int i = 0; i < draftBindings.Length; i++)
            if (i != slot) RefreshOverlaySlot(i);

        ClearDraftSlot(slot);

        Debug.Log($"[InputManager] Dejoin triggered by Player {slot + 1}. Waiting for a device.");
    }
    #endregion

    #region Rejoin System
    /// <summary>
    /// Called every frame while the rejoin overlay is open, Nintendo controller-screen style:
    ///   - Select on a player's own device dejoins that player (frees the slot)
    ///   - Any button on a free gamepad / free keyboard half joins the lowest empty slot
    ///   - A (Confirm) on any joined device resumes once every slot has a device
    /// </summary>
    public void PollRejoinInput()
    {
        if (!waitingForRejoin) return;
        if (Time.frameCount <= rejoinStartFrame) return; // ignore the Select press that opened the overlay

        // A joined gamepad was unplugged while the overlay is open
        for (int i = 0; i < draftBindings.Length; i++)
            if (draftBindings[i].device != null && !draftBindings[i].device.added)
                ClearDraftSlot(i);

        // Dejoin: a joined player pressed Select on their device
        for (int i = 0; i < draftBindings.Length; i++)
        {
            if (!HasDevice(draftBindings[i])) continue;
            if (!WasActionPressed(draftBindings[i], "Select")) continue;

            ClearDraftSlot(i);
            Debug.Log($"[InputManager] Rejoin: Player {i + 1} dejoined");
            return; // the same press must not rejoin straight away
        }

        // Resume: every slot is filled and someone confirmed (not with the press that just joined)
        if (Time.frameCount > lastRosterChangeFrame && AllDraftSlotsFilled())
        {
            for (int i = 0; i < draftBindings.Length; i++)
            {
                if (!WasActionPressed(draftBindings[i], "Submit")) continue;
                FinishRejoin();
                return;
            }
        }

        // Join: a free device fills the lowest empty slot
        int emptySlot = Array.FindIndex(draftBindings, b => !HasDevice(b));
        if (emptySlot < 0) return;
        if (!TryDetectFreeDevicePress(out var scheme, out var device)) return;

        draftBindings[emptySlot] = new SlotBinding { scheme = scheme, device = device };
        MakeReservation(draftBindings[emptySlot]);
        lastRosterChangeFrame = Time.frameCount;
        OnNewDevicePaired?.Invoke(device);
        RefreshOverlaySlot(emptySlot);

        Debug.Log($"[InputManager] Rejoin: Player {emptySlot + 1} joined with {scheme} ({device.displayName})");
    }

    private static bool HasDevice(SlotBinding binding) => binding.device != null && binding.device.added;

    private bool AllDraftSlotsFilled() => draftBindings.All(HasDevice);

    /// <summary>
    /// Frees a slot on the overlay and releases its device so anyone can pick it up
    /// </summary>
    private void ClearDraftSlot(int slot)
    {
        ReleaseReservation(draftBindings[slot]);
        draftBindings[slot] = default;
        lastRosterChangeFrame = Time.frameCount;
        RefreshOverlaySlot(slot);
    }

    private void RefreshOverlaySlot(int slot)
    {
        var overlay = GameManager.Instance != null ? GameManager.Instance.disconnectOverlay : null;
        if (overlay == null) return;

        var device = HasDevice(draftBindings[slot]) ? draftBindings[slot].device : null;
        overlay.SetPresenceForSlot(slot, device is Keyboard, device is Gamepad, draftBindings[slot].scheme);
        overlay.ShowConfirmHint(AllDraftSlotsFilled());
    }

    /// <summary>
    /// Finds a gamepad or keyboard half pressed this frame that no player on the overlay owns
    /// </summary>
    private bool TryDetectFreeDevicePress(out string scheme, out InputDevice device)
    {
        scheme = null;
        device = null;

        foreach (var gp in Gamepad.all)
        {
            if (draftBindings.Any(b => b.device == gp)) continue;
            if (AnyGamepadButtonPressed(gp))
            {
                scheme = GamepadScheme;
                device = gp;
                return true;
            }
        }

        var kb = Keyboard.current;
        if (kb == null || !kb.anyKey.wasPressedThisFrame) return false;

        foreach (var candidate in KeyboardSchemes())
        {
            if (draftBindings.Any(b => b.device is Keyboard && b.scheme == candidate)) continue;
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
    /// True if the given action was pressed this frame on a slot's own device / keyboard half
    /// </summary>
    private bool WasActionPressed(SlotBinding binding, string actionName)
    {
        if (!HasDevice(binding)) return false;

        if (binding.device is Gamepad gp && actionsTemplate == null)
            return actionName == "Submit" ? gp.buttonSouth.wasPressedThisFrame
                 : actionName == "Select" && gp.selectButton.wasPressedThisFrame;

        string scheme = binding.device is Gamepad ? GamepadScheme : binding.scheme;
        return AnyKeyOfSchemePressed(binding.device, scheme, actionName);
    }

    /// <summary>
    /// True if a control bound to the given scheme was pressed this frame on the device
    /// actionName limits the check to one action (e.g. "Submit"); null checks every action
    /// </summary>
    private bool AnyKeyOfSchemePressed(InputDevice device, string scheme, string actionName)
    {
        if (actionsTemplate == null)
            return actionName == null && device is Keyboard kb && kb.anyKey.wasPressedThisFrame;

        var controls = ControlsFor(device, scheme, actionName);
        for (int i = 0; i < controls.Length; i++)
            if (controls[i].wasPressedThisFrame) return true;
        return false;
    }

    /// <summary>
    /// The buttons a scheme binds to an action on a device (actionName null = every action).
    /// Resolving binding paths is slow and this is polled every frame, so results are cached
    /// </summary>
    private ButtonControl[] ControlsFor(InputDevice device, string scheme, string actionName)
    {
        var key = (device.deviceId, scheme, actionName);
        if (controlCache.TryGetValue(key, out var cached)) return cached;

        var found = new List<ButtonControl>();
        foreach (var action in actionsTemplate)
        {
            if (actionName != null && action.name != actionName) continue;

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || !BindingInGroup(binding, scheme)) continue;

                if (InputControlPath.TryFindControl(device, binding.effectivePath) is ButtonControl control && !found.Contains(control))
                    found.Add(control);
            }
        }

        var result = found.ToArray();
        controlCache[key] = result;
        return result;
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
    /// Commits the overlay's roster: stores each slot's device and rebinds the players that changed
    /// Reservations were already moved while players joined/dejoined on the overlay
    /// </summary>
    private void FinishRejoin()
    {
        if (!waitingForRejoin || !AllDraftSlotsFilled()) return;

        for (int i = 0; i < draftBindings.Length; i++)
        {
            var binding = draftBindings[i];
            bool changed = binding.device != slotBindings[i].device || binding.scheme != slotBindings[i].scheme;
            slotBindings[i] = binding;

            int slot = i + 1;
            PlayerPrefs.SetString($"P{slot}_Scheme", binding.scheme);
            PlayerPrefs.SetString($"P{slot}_Devices", binding.device.deviceId.ToString());

            if (changed && i < controllers.Length) controllers[i]?.Bind(binding.device, binding.scheme);
            Debug.Log($"[InputManager] Player {slot}: {binding.scheme} / {binding.device.displayName}");
        }
        PlayerPrefs.Save();

        waitingForRejoin = false;
        draftBindings = Array.Empty<SlotBinding>();

        Debug.Log("[InputManager] Rejoin completed");
        OnRejoinCompleted?.Invoke();
    }

    /// <summary>
    /// In 2P, lets any player dejoin during play, not just the one whose turn it is
    /// (the waiting player's actions are disabled, so their Select is polled here instead)
    /// </summary>
    private void CheckIdlePlayersDejoin()
    {
        if (!isTwoPlayerMode || waitingForRejoin) return;

        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentGameState != GameManager.GameState.Playing) return;

        for (int i = 0; i < slotBindings.Length; i++)
        {
            if (i == activeSlot) continue; // the active player's Select goes through PlayerInputController
            if (!WasActionPressed(slotBindings[i], "Select")) continue;

            RequestDejoin(gm, i + 1, isTwoPlayerMode, gm.TotalPlayers);
            return;
        }
    }

    /// <summary>
    /// Reacts when a player's gamepad is unplugged
    /// 2P: opens the rejoin overlay for that player, whoever's turn it is. 1P: just pauses (keyboard still works)
    /// </summary>
    private void CheckGamepadDisconnected()
    {
        if (waitingForRejoin) return;

        var gm = GameManager.Instance;
        if (gm == null || gm.CurrentGameState != GameManager.GameState.Playing) return;

        if (!isTwoPlayerMode)
        {
            if (activeSlot < 0 || activeSlot >= controllers.Length || controllers[activeSlot] == null) return;

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

        for (int i = 0; i < slotBindings.Length; i++)
        {
            var device = slotBindings[i].device;
            if (device is not Gamepad || device.added) continue;

            Debug.Log($"[InputManager] Player {i + 1}'s gamepad disconnected. Opening rejoin overlay.");
            OnDeviceLost?.Invoke(device);
            RequestDejoin(gm, i + 1, isTwoPlayerMode, gm.TotalPlayers);
            return;
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
