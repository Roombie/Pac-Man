using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.LowLevel;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Input References")]
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private InputActionAsset originalActionsAsset;

    [Header("Settings")]
    [SerializeField] private bool enableHotSwap = true;
    [SerializeField] private bool debugInputEvents = false;

    // Events
    public event Action<InputDevice> OnDeviceLost;
    public event Action<InputDevice> OnDeviceRegained;
    public event Action<InputDevice> OnNewDevicePaired;
    public event Action<int, string, InputDevice> OnControlSchemeChanged;

    // State
    private bool _hotSwapEnabled = false;
    private bool _listeningForUnpaired = false;
    private bool _deviceChangeHooked = false;
    private InputUser _pacmanUser;
    private InputSystemUIInputModule _cachedUiModule;
    private bool _cachedUiModuleEnabled;
    private bool _uiLockedForRejoin;

    // Rejoin state
    private bool _waitingForRejoin = false;
    private int _rejoinSlot;
    private bool _rejoinSawKeyboard;
    private bool _rejoinSawGamepad;

    // Cache
    private string[] _allKeyboardSchemes;
    private Coroutine _rejoinCoroutine;

    #region Initialization
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        var deviceMgr = Roombie.CharacterSelect.PlayerDeviceManager.Instance;
        _allKeyboardSchemes = deviceMgr != null 
            ? deviceMgr.SinglePlayerCandidates() 
            : new[] { "P1Keyboard", "P2Keyboard" };

        if (playerInput == null)
        {
            playerInput = FindFirstObjectByType<PlayerInput>();
            if (playerInput == null)
            {
                Debug.LogError("[InputManager] No PlayerInput found in scene!");
                return;
            }
        }

        originalActionsAsset = playerInput.actions;
        _pacmanUser = playerInput.user;
    }

    private void Start()
    {
        InitializeInputSystem();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            DisableHotSwap();
        }
    }

    public void InitializeInputSystem()
    {
        DisableHotSwap(); // Clean up any existing listeners

        // Apply initial device locking
        ApplyBootLock();
        
        // Set up UI input module
        WireUIToPlayerInput();
        
        if (enableHotSwap)
        {
            EnableHotSwap();
        }
    }
    #endregion

    #region Public Interface - Device Management
    public void ApplyActivePlayerInputDevices(int slot, bool isTwoPlayerMode)
    {
        var pi = playerInput;
        if (pi == null || pi.actions == null)
        {
            Debug.LogWarning("[InputManager] PlayerInput missing.");
            return;
        }

        string scheme = PlayerPrefs.GetString($"P{slot}_Scheme", "");
        string devicesCsv = PlayerPrefs.GetString($"P{slot}_Devices", "");
        var deviceIds = devicesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        // Resolve chosen device (prefer gamepad if present)
        InputDevice chosen = null;
        foreach (var d in InputSystem.devices)
        {
            if (deviceIds.Contains(d.deviceId.ToString()))
                chosen = d is Gamepad ? d : (chosen ?? d);
        }
        if (chosen == null)
        {
            if (!string.IsNullOrEmpty(scheme) && scheme.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0)
                chosen = Gamepad.current;
            else
                chosen = Keyboard.current;
        }

        // Lock devices & (maybe) binding mask
        pi.neverAutoSwitchControlSchemes = true;
        var actions = pi.actions;
        actions.Disable();

        // Unpair all first
        if (pi.user.valid)
            pi.user.UnpairDevices();

        // Pair ONLY the chosen device
        if (chosen != null)
            InputUser.PerformPairingWithDevice(chosen, pi.user);

        // Filter actions to that device
        actions.devices = chosen != null ? new InputDevice[] { chosen } : null;

        // --- THE IMPORTANT PART ---
        // Single-player + keyboard => NO MASK so both P1Keyboard & P2Keyboard bindings work
        if (!isTwoPlayerMode && !string.IsNullOrEmpty(scheme) &&
            scheme.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            actions.bindingMask = default; // allow all keyboard groups
        }
        else if (!string.IsNullOrEmpty(scheme))
        {
            // For multiplayer or non-keyboard scheme, be strict
            actions.bindingMask = InputBinding.MaskByGroup(scheme);
        }
        else
        {
            actions.bindingMask = default;
        }

        actions.Enable();

        // Optional: if you DO have control schemes named "Gamepad"/"Keyboard&Mouse", this is fine.
        // If your "scheme" is actually just a binding group (P1Keyboard), you can omit this.
        if (!string.IsNullOrEmpty(scheme))
        {
            try
            {
                pi.SwitchCurrentControlScheme(
                    pi.user.pairedDevices.ToArray()
                );
            }
            catch { /* ignore if names don't match control scheme names */ }
        }

        Debug.Log($"[InputManager] ApplyActivePlayerInputDevices -> Slot={slot}, TwoP={isTwoPlayerMode}, " +
                  $"Scheme='{scheme}', Device='{chosen?.displayName}', Mask={(actions.bindingMask.HasValue ? actions.bindingMask.Value.groups : "<none>")}");
    }

    public void ClearActivePlayerDevicesAndPrefs(int playerSlot)
    {
        if (!TryGetUser(out var user))
        {
            Debug.LogWarning("[InputManager] ClearActivePlayerDevicesAndPrefs: no valid user; skipping unpair.");
        }
        else
        {
            var toUnpair = new List<InputDevice>(user.pairedDevices);
            foreach (var d in toUnpair)
            {
                try 
                { 
                    user.UnpairDevice(d); 
                    if (debugInputEvents) Debug.Log($"[InputManager] Unpaired device: {d.displayName}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[InputManager] UnpairDevice failed ({d?.displayName}): {e.Message}");
                }
            }
        }

        // Clear persisted choice
        PlayerPrefs.DeleteKey($"P{playerSlot}_Scheme");
        PlayerPrefs.DeleteKey($"P{playerSlot}_Devices");
        PlayerPrefs.Save();

        // Reset actions device filter
        if (playerInput != null && playerInput.actions != null)
        {
            playerInput.actions.Disable();
            playerInput.actions.devices = null;
            playerInput.actions.bindingMask = default;
            playerInput.actions.Enable();
        }

        if (debugInputEvents)
        {
            Debug.Log($"[InputManager] Cleared devices and prefs for slot {playerSlot}");
        }
    }

    public void SwitchToGameplayMap()
    {
        var map = playerInput?.actions?.FindActionMap("Player", false);
        playerInput?.actions?.FindActionMap("UI", false)?.Disable();
        map?.Enable();
    }

    public void SwitchToUIMap()
    {
        var map = playerInput?.actions?.FindActionMap("UI", false);
        playerInput?.actions?.FindActionMap("Player", false)?.Disable();
        map?.Enable();
    }

    public void DisableAllInput()
    {
        if (playerInput == null) return;

        playerInput.actions?.Disable();
        if (debugInputEvents) Debug.Log("[InputManager] Disabled all input");
    }

    public void EnableAllInput()
    {
        if (playerInput == null) return;

        playerInput.actions?.Enable();
        if (debugInputEvents) Debug.Log("[InputManager] Enabled all input");
    }
    #endregion

    #region Public Interface - Rejoin System
    public void StartRejoinProcess(int playerSlot)
    {
        if (_waitingForRejoin) return;

        _waitingForRejoin = true;
        _rejoinSlot = playerSlot;
        _rejoinSawKeyboard = false;
        _rejoinSawGamepad = false;

        // Disable input maps
        if (playerInput != null)
        {
            playerInput.actions.FindActionMap("Player", false)?.Disable();
            playerInput.actions.FindActionMap("UI", false)?.Disable();
        }

        // Lock UI module
        /*var es = EventSystem.current;
        _cachedUiModule = es ? es.GetComponent<InputSystemUIInputModule>() : null;
        if (_cachedUiModule != null)
        {
            _cachedUiModuleEnabled = _cachedUiModule.enabled;
            _cachedUiModule.enabled = false;
            _uiLockedForRejoin = true;
        }

        if (debugInputEvents) Debug.Log($"[InputManager] Started rejoin process for slot {playerSlot}");*/
    }

    public void PollRejoinInput()
    {
        if (!_waitingForRejoin) return;

        var kb = Keyboard.current;
        var gp = Gamepad.current;

        bool kbPressed = kb != null && kb.anyKey.wasPressedThisFrame;
        bool gpPressed = AnyGamepadPressedThisFrame(gp);

        if (kbPressed)
        {
            _rejoinSawKeyboard = true;
            _rejoinSawGamepad = false;
            OnNewDevicePaired?.Invoke(kb);
            if (debugInputEvents) Debug.Log("[InputManager] Keyboard detected during rejoin");
        }
        else if (gpPressed)
        {
            _rejoinSawKeyboard = false;
            _rejoinSawGamepad = true;
            OnNewDevicePaired?.Invoke(gp);
            if (debugInputEvents) Debug.Log("[InputManager] Gamepad detected during rejoin");
        }

        if ((_rejoinSawKeyboard || _rejoinSawGamepad) && 
            ConfirmPressedThisFrame(kb, gp, _rejoinSawKeyboard, _rejoinSawGamepad))
        {
            return;
        }
    }

    public void FinishRejoin()
    {
        if (!_waitingForRejoin) return;

        int slot = _rejoinSlot;
        string rejoinScheme = _rejoinSawGamepad ? "Gamepad" : (_rejoinSawKeyboard ? "P1Keyboard" : "");

        if (!string.IsNullOrEmpty(rejoinScheme))
        {
            PlayerPrefs.SetString($"P{slot}_Scheme", rejoinScheme);
            if (_rejoinSawGamepad && Gamepad.current != null)
                PlayerPrefs.SetString($"P{slot}_Devices", Gamepad.current.deviceId.ToString());
            else if (_rejoinSawKeyboard && Keyboard.current != null)
                PlayerPrefs.SetString($"P{slot}_Devices", Keyboard.current.deviceId.ToString());
            PlayerPrefs.Save();
        }

        // Restore UI module
        var es = EventSystem.current;
        if (es)
        {
            var uiModule = es.GetComponent<InputSystemUIInputModule>();
            if (uiModule && _uiLockedForRejoin)
            {
                uiModule.enabled = _cachedUiModuleEnabled;
                _uiLockedForRejoin = false;
            }
        }

        // Re-enable gameplay input
        if (playerInput != null)
        {
            playerInput.actions.FindActionMap("Player", false)?.Enable();
        }

        _waitingForRejoin = false;
        _rejoinSawKeyboard = false;
        _rejoinSawGamepad = false;

        if (debugInputEvents) Debug.Log($"[InputManager] Finished rejoin process for slot {slot} with scheme: {rejoinScheme}");
    }

    public bool IsWaitingForRejoin => _waitingForRejoin;
    #endregion

    #region Core Implementation
    private void ApplyBootLock()
    {
        if (playerInput == null || playerInput.actions == null) return;

        int slot = 1; // Default to player 1 for boot

        var scheme = PlayerPrefs.GetString($"P{slot}_Scheme", "");
        var devicesCsv = PlayerPrefs.GetString($"P{slot}_Devices", "");
        var deviceIds = devicesCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        InputDevice chosen = null;
        foreach (var d in InputSystem.devices)
            if (deviceIds.Contains(d.deviceId.ToString()))
                chosen = d is Gamepad ? d : (chosen ?? d);

        if (chosen == null)
        {
            if (!string.IsNullOrEmpty(scheme) && scheme.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0)
                chosen = Gamepad.current;
            else
                chosen = Keyboard.current;
        }

        if (string.IsNullOrEmpty(scheme))
            scheme = (chosen is Gamepad) ? "Gamepad" : "P1Keyboard";

        playerInput.neverAutoSwitchControlSchemes = true;
        BootLockMaps();
        ForceSingleDevice(chosen, scheme, false); // false for boot (treated as single player)

        if (debugInputEvents)
        {
            Debug.Log($"[InputManager] Boot lock applied: {chosen?.displayName}, Scheme: {scheme}");
        }
    }

    private void BootLockMaps()
    {
        if (playerInput == null || playerInput.actions == null) return;

        var actions = playerInput.actions;
        var playerMap = actions.FindActionMap("Player", throwIfNotFound: false);
        var uiMap = actions.FindActionMap("UI", throwIfNotFound: false);

        actions.Disable();
        uiMap?.Disable();
        playerMap?.Enable();
        actions.Enable();
    }

    private void ForceSingleDevice(InputDevice chosen, string scheme, bool isTwoPlayerMode)
    {
        if (playerInput == null || playerInput.actions == null) return;

        var acts = playerInput.actions;
        acts.Disable();

        // Unpair previous devices
        if (playerInput.user.valid)
            playerInput.user.UnpairDevices();

        // Pair only the chosen device
        if (chosen != null)
            InputUser.PerformPairingWithDevice(chosen, playerInput.user);

        // Set device filter
        acts.devices = chosen != null ? new InputDevice[] { chosen } : null;

        if (!isTwoPlayerMode && IsKeyboardSchemeName(scheme))
        {
            // 1P + keyboard: no binding mask to allow both P1Keyboard and P2Keyboard
            acts.bindingMask = default;
        }
        else
        {
            // Gamepad or 2P: strict binding
            if (!string.IsNullOrEmpty(scheme))
            {
                acts.bindingMask = InputBinding.MaskByGroup(scheme);
                playerInput.SwitchCurrentControlScheme(scheme, playerInput.user.pairedDevices.ToArray());
            }
            else
            {
                acts.bindingMask = default;
            }
        }

        acts.Enable();

        if (debugInputEvents)
        {
            var maskText = acts.bindingMask.HasValue
                ? (string.IsNullOrEmpty(acts.bindingMask.Value.groups) ? "<none>" : acts.bindingMask.Value.groups)
                : "<none>";
            var paired = string.Join(", ", playerInput.user.pairedDevices.Select(d => d.displayName));
            Debug.Log($"[InputManager] ForceSingleDevice -> Scheme={scheme} | Device={chosen?.displayName} | Paired={paired} | Mask={maskText}");
        }

        OnControlSchemeChanged?.Invoke(_rejoinSlot, scheme, chosen);
    }

    private void WireUIToPlayerInput()
    {
        var es = EventSystem.current;
        if (es == null) return;

        var uiModule = es.GetComponent<InputSystemUIInputModule>();
        if (uiModule == null) return;

        if (uiModule.actionsAsset == null || uiModule.actionsAsset != playerInput.actions)
        {
            uiModule.actionsAsset = playerInput.actions;
            if (debugInputEvents) Debug.Log("[InputManager] Wired UI to player input actions");
        }
    }
    #endregion

    #region Hot Swap System
    private void EnableHotSwap()
    {
        if (_hotSwapEnabled) return;
        if (playerInput == null) return;

        _pacmanUser = playerInput.user;

        if (!_listeningForUnpaired)
        {
            InputUser.listenForUnpairedDeviceActivity =
                Mathf.Max(0, InputUser.listenForUnpairedDeviceActivity) + 1;

            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
            _listeningForUnpaired = true;
        }

        if (!_deviceChangeHooked)
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            _deviceChangeHooked = true;
        }

        playerInput.onDeviceLost += OnPairedDeviceLost;
        playerInput.onDeviceRegained += OnPairedDeviceRegained;

        _hotSwapEnabled = true;

        if (debugInputEvents) Debug.Log("[InputManager] Hot swap enabled");
    }

    private void DisableHotSwap()
    {
        if (!_hotSwapEnabled) return;

        if (playerInput != null)
        {
            playerInput.onDeviceLost -= OnPairedDeviceLost;
            playerInput.onDeviceRegained -= OnPairedDeviceRegained;
        }

        if (_listeningForUnpaired)
        {
            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
            if (InputUser.listenForUnpairedDeviceActivity > 0)
                InputUser.listenForUnpairedDeviceActivity -= 1;
            _listeningForUnpaired = false;
        }

        if (_deviceChangeHooked)
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            _deviceChangeHooked = false;
        }

        _hotSwapEnabled = false;

        if (debugInputEvents) Debug.Log("[InputManager] Hot swap disabled");
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is not Gamepad) return;
        if (change != InputDeviceChange.Disconnected && change != InputDeviceChange.Removed) return;
        
        if (!IsDeviceRelevantToPlayer(device)) return;

        // Clean saved device list if it contains this device ID
        int slot = _rejoinSlot;
        string keyCsv = $"P{slot}_Devices";
        var savedCsv = PlayerPrefs.GetString(keyCsv, "");
        if (!string.IsNullOrEmpty(savedCsv))
        {
            var list = new List<string>(savedCsv.Split(','));
            list.RemoveAll(s => int.TryParse(s, out var id) && id == device.deviceId);
            if (list.Count == 0) PlayerPrefs.DeleteKey(keyCsv);
            else PlayerPrefs.SetString(keyCsv, string.Join(",", list));
            PlayerPrefs.Save();
        }

        if (!_waitingForRejoin)
        {
            OnDeviceLost?.Invoke(device);
            if (debugInputEvents) Debug.Log($"[InputManager] Device lost: {device.displayName}");
        }
    }

    private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
    {
        if (control?.device is not Gamepad gp) return;

        var owner = InputUser.FindUserPairedToDevice(gp);
        if (owner.HasValue && owner.Value.valid && TryGetUser(out var myUser) && owner.Value != myUser)
            return;

        if (!TryGetUser(out var user)) return;

        // Unpair ALL devices from this user first
        foreach (var d in new List<InputDevice>(user.pairedDevices))
            user.UnpairDevice(d);

        // Pair ONLY the new gamepad
        InputUser.PerformPairingWithDevice(gp, user);

        if (playerInput != null)
        {
            playerInput.actions.Disable();
            playerInput.actions.devices = new InputDevice[] { gp };
            playerInput.actions.bindingMask = InputBinding.MaskByGroup("Gamepad");
            playerInput.actions.Enable();
            playerInput.SwitchCurrentControlScheme("Gamepad", gp);
        }

        int slot = _rejoinSlot;
        PlayerPrefs.SetString($"P{slot}_Scheme", "Gamepad");
        PlayerPrefs.SetString($"P{slot}_Devices", gp.deviceId.ToString());
        PlayerPrefs.Save();

        OnNewDevicePaired?.Invoke(gp);

        if (debugInputEvents) Debug.Log($"[InputManager] Paired new gamepad: {gp.displayName} to slot {slot}");
    }

    private void OnPairedDeviceLost(PlayerInput input)
    {
        OnDeviceLost?.Invoke(null); // Device specific info might not be available
        if (debugInputEvents) Debug.Log("[InputManager] Paired device lost");
    }

    private void OnPairedDeviceRegained(PlayerInput input)
    {
        OnDeviceRegained?.Invoke(null);
        if (debugInputEvents) Debug.Log("[InputManager] Paired device regained");
    }

    private bool IsDeviceRelevantToPlayer(InputDevice device)
    {
        if (playerInput == null) return false;

        // Check actions.devices filter
        var devs = playerInput.actions.devices;
        if (devs.HasValue)
            foreach (var d in devs.Value)
                if (ReferenceEquals(d, device)) return true;

        // Check user pairings
        if (TryGetUser(out var user))
            return user.pairedDevices.Contains(device);

        return false;
    }
    #endregion

    #region Utility Methods
    private static bool IsKeyboardSchemeName(string scheme) =>
        !string.IsNullOrEmpty(scheme) &&
        scheme.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool AnyGamepadPressedThisFrame(Gamepad gp)
    {
        if (gp == null) return false;
        var d = gp.dpad;
        return gp.startButton.wasPressedThisFrame ||
            gp.buttonSouth.wasPressedThisFrame ||
            gp.buttonEast.wasPressedThisFrame ||
            gp.buttonWest.wasPressedThisFrame ||
            gp.buttonNorth.wasPressedThisFrame ||
            d.up.wasPressedThisFrame ||
            d.down.wasPressedThisFrame ||
            d.left.wasPressedThisFrame ||
            d.right.wasPressedThisFrame ||
            gp.leftStickButton.wasPressedThisFrame ||
            gp.rightStickButton.wasPressedThisFrame;
    }

    private static bool ConfirmPressedThisFrame(Keyboard kb, Gamepad gp, bool sawKb, bool sawGp)
    {
        bool kbConfirm = kb != null &&
            (kb.spaceKey.wasPressedThisFrame ||
            kb.enterKey.wasPressedThisFrame ||
            kb.numpadEnterKey.wasPressedThisFrame ||
            (sawKb && kb.anyKey.wasPressedThisFrame));

        bool gpConfirm = gp != null &&
            (gp.startButton.wasPressedThisFrame ||
            gp.buttonSouth.wasPressedThisFrame ||
            (sawGp && AnyGamepadPressedThisFrame(gp)));

        return kbConfirm || gpConfirm;
    }

    private bool TryGetUser(out InputUser user)
    {
        if (playerInput != null && playerInput.user.valid)
        {
            user = playerInput.user;
            return true;
        }
        user = default;
        return false;
    }

    public void SetPlayerInputReference(PlayerInput playerInput)
    {
        if (playerInput == null) return;

        // Clean up existing references
        DisableHotSwap();

        this.playerInput = playerInput;
        originalActionsAsset = playerInput.actions;
        _pacmanUser = playerInput.user;

        // Re-initialize with new reference
        InitializeInputSystem();

        if (debugInputEvents) Debug.Log("[InputManager] Updated PlayerInput reference");
    }
    #endregion
}