using System;
using System.Collections.Generic;
using System.Linq;
using TMPro; // TMP_Text
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.Utilities; // for onAnyButtonPress.Call
using UnityEngine.Localization;
using UnityEngine.UI;

public class DisconnectManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text messageLabel;
    [SerializeField] private Button exitButton;

    [Header("Localization")]
    [SerializeField] private LocalizedString playerDisconnected; // SmartString: "Player {playerName} disconnected"

    [Header("Behavior")]
    [Tooltip("Freeze the game only if the disconnected slot is the active turn.")]
    [SerializeField] private bool freezeOnlyIfActivePlayer = true;

    [Tooltip("Show an Exit button to return to Main Menu.")]
    [SerializeField] private bool showExitButton = true;

    // Track disconnected slots by 1-based player number (PlayerInput.playerIndex + 1)
    private readonly HashSet<int> disconnectedSet = new HashSet<int>();
    private bool frozeGame;

    // Observable subscription to any-button presses
    private IDisposable anyButtonSubscription;
    private bool listeningForAnyPress;

    private PlayerInputManager playerInputManager;

    void Awake()
    {
        if (exitButton)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(ExitToMainMenu);
        }
        HidePanelImmediate();
    }

    void OnEnable()
    {
        SubscribeAllPlayerInputs();

#if UNITY_2023_1_OR_NEWER
        playerInputManager = FindFirstObjectByType<PlayerInputManager>();
        if (!playerInputManager) playerInputManager = FindAnyObjectByType<PlayerInputManager>();
#else
#pragma warning disable CS0618
        playerInputManager = FindObjectOfType<PlayerInputManager>();
#pragma warning restore CS0618
#endif
        if (playerInputManager != null)
        {
            playerInputManager.onPlayerJoined += OnPlayerJoined;
            playerInputManager.onPlayerLeft   += OnPlayerLeft;
        }
    }

    void OnDisable()
    {
        UnsubscribeAllPlayerInputs();

        if (playerInputManager != null)
        {
            playerInputManager.onPlayerJoined -= OnPlayerJoined;
            playerInputManager.onPlayerLeft   -= OnPlayerLeft;
            playerInputManager = null;
        }

        StopListeningForAnyPress();
    }

    private void OnPlayerJoined(PlayerInput pi)  => Subscribe(pi);
    private void OnPlayerLeft(PlayerInput pi)    => Unsubscribe(pi);

    private void SubscribeAllPlayerInputs()
    {
#if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable CS0618
        var all = FindObjectsOfType<PlayerInput>(true);
#pragma warning restore CS0618
#endif
        foreach (var pi in all) Subscribe(pi);
    }

    private void UnsubscribeAllPlayerInputs()
    {
#if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable CS0618
        var all = FindObjectsOfType<PlayerInput>(true);
#pragma warning restore CS0618
#endif
        foreach (var pi in all) Unsubscribe(pi);
    }

    private void Subscribe(PlayerInput pi)
    {
        if (!pi) return;
        pi.onDeviceLost -= OnDeviceLost;
        pi.onDeviceRegained -= OnDeviceRegained;
        pi.onDeviceLost += OnDeviceLost;
        pi.onDeviceRegained += OnDeviceRegained;
    }

    private void Unsubscribe(PlayerInput pi)
    {
        if (!pi) return;
        pi.onDeviceLost -= OnDeviceLost;
        pi.onDeviceRegained -= OnDeviceRegained;
    }

    private void OnDeviceLost(PlayerInput pi)
    {
        int playerNumber = ResolvePlayerNumber(pi);
        disconnectedSet.Add(playerNumber);

        UpdateMessage(playerNumber);
        ShowPanel();
        StartListeningForAnyPress();

        bool isActiveTurn = IsActiveTurn(playerNumber);
        if ((!freezeOnlyIfActivePlayer || isActiveTurn) && !frozeGame && GameManager.Instance)
        {
            GameManager.Instance.PushFreeze(hard: true, freezeTimers: true);
            frozeGame = true;
        }
    }

    private void OnDeviceRegained(PlayerInput pi)
    {
        int playerNumber = ResolvePlayerNumber(pi);
        disconnectedSet.Remove(playerNumber);

        if (disconnectedSet.Count == 0)
        {
            StopListeningForAnyPress();
            HidePanelImmediate();

            if (frozeGame && GameManager.Instance)
            {
                GameManager.Instance.PopFreeze();
                frozeGame = false;
            }
        }
        else
        {
            UpdateMessage(GetAnyDisconnectedPlayer());
        }
    }

    private void StartListeningForAnyPress()
    {
        if (listeningForAnyPress) return;
        listeningForAnyPress = true;

        // onAnyButtonPress is IObservable<InputControl>. Use .Call(Action<InputControl>) to subscribe.
        anyButtonSubscription = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);
    }

    private void StopListeningForAnyPress()
    {
        if (!listeningForAnyPress) return;
        listeningForAnyPress = false;

        anyButtonSubscription?.Dispose();
        anyButtonSubscription = null;
    }

    private void OnAnyButtonPressed(InputControl control)
    {
        if (disconnectedSet.Count == 0) return;

        var device = control?.device;
        if (device == null) return;

        int targetPlayer = ResolveTargetSlot();
        var targetPI = GetPlayerInputForHotSeatOrByNumber(targetPlayer);
        if (targetPI == null) return;

        // Reject devices already paired to another PlayerInput
#if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable CS0618
        var all = FindObjectsOfType<PlayerInput>(true);
#pragma warning restore CS0618
#endif
        foreach (var other in all)
        {
            if (other == targetPI) continue;
            if (other.user.valid && IsDevicePairedTo(other, device))
                return; // device is already in use
        }

        // --- HOT-SEAT SAFE SWAP: unpair target, steal device, pair, clamp, save ---
        var user = targetPI.user;
        if (!user.valid) return;

        // 1) Hard reset: unpair everything from the target user (avoid both pads controlling)
        if (user.pairedDevices.Count > 0)
            user.UnpairDevices();

        // 2) Steal the pressed device from any other users
        foreach (var u in InputUser.all)
        {
            if (u.pairedDevices.Contains(device))
                u.UnpairDevice(device);
        }

        // 3) Pair this device to the target player
        InputUser.PerformPairingWithDevice(device, user);

        // 4) Choose a control scheme that supports this device
        string schemeName = FindSchemeForDevice(targetPI, device);
        if (string.IsNullOrEmpty(schemeName))
            schemeName = "Gamepad"; // fallback: update to your exact scheme name if different

        // 5) Clamp the action asset to EXACTLY this device
        targetPI.actions.Disable();
        targetPI.actions.devices = new[] { device };
        targetPI.actions.Enable();

        // 6) Activate scheme on the PlayerInput (prevents auto-hop)
        targetPI.neverAutoSwitchControlSchemes = true;
        targetPI.SwitchCurrentControlScheme(schemeName, device);

        // 7) Persist mapping for this logical player (so it survives scenes/turns)
        PlayerPrefs.SetString($"P{targetPlayer}_Scheme", schemeName);
        PlayerPrefs.SetString($"P{targetPlayer}_Devices", device.deviceId.ToString());
        PlayerPrefs.Save();

        Debug.Log($"[DisconnectManager] Repaired P{targetPlayer} → {device.displayName}#{device.deviceId}, scheme={schemeName}");

        // Done → treat as regained (unfreeze/hide panel if everyone is back)
        OnDeviceRegained(targetPI);
    }

    private static bool IsDevicePairedTo(PlayerInput pi, InputDevice device)
    {
        var arr = pi.user.pairedDevices;
        for (int i = 0; i < arr.Count; i++)
            if (arr[i] == device) return true;
        return false;
    }

    private static string FindSchemeForDevice(PlayerInput pi, InputDevice device)
    {
        if (pi == null || pi.actions == null) return null;

        // If current scheme already supports the device, keep it
        var currentName = pi.currentControlScheme;
        if (!string.IsNullOrEmpty(currentName))
        {
            foreach (var s in pi.actions.controlSchemes)
                if (s.name == currentName && s.SupportsDevice(device))
                    return currentName;
        }

        // Otherwise scan all schemes for a compatible one
        foreach (var s in pi.actions.controlSchemes)
            if (s.SupportsDevice(device))
                return s.name;

        return null;
    }

    private bool IsHotSeatMode()
    {
    #if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    #else
        var all = FindObjectsOfType<PlayerInput>(true);
    #endif
        return all != null && all.Length == 1; // one PlayerInput => hot-seat
    }

    private int ResolvePlayerNumber(PlayerInput pi)
    {
        // In hot-seat, the "logical player" is whoever's turn it is.
        if (IsHotSeatMode())
        {
            int active = GetActivePlayerNumber(); // 1-based via GameManager
            return active > 0 ? active : 1;
        }
        return pi.playerIndex + 1;
    }

    private PlayerInput GetPlayerInputForHotSeatOrByNumber(int playerNumber)
    {
    #if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    #else
        var all = FindObjectsOfType<PlayerInput>(true);
    #endif
        if (all == null || all.Length == 0) return null;
        if (IsHotSeatMode()) return all[0]; // the single PlayerInput that controls Pac-Man
        foreach (var pi in all)
            if (pi.playerIndex + 1 == playerNumber) return pi;
        return null;
    }

    private int ResolveTargetSlot()
    {
        int active = GetActivePlayerNumber();
        if (active > 0 && disconnectedSet.Contains(active))
            return active;
        return GetAnyDisconnectedPlayer();
    }

    private int GetAnyDisconnectedPlayer()
    {
        foreach (var pn in disconnectedSet) return pn;
        return -1;
    }

    private PlayerInput GetPlayerInputByNumber(int playerNumber)
    {
#if UNITY_2023_1_OR_NEWER
        var all = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
#pragma warning disable CS0618
        var all = FindObjectsOfType<PlayerInput>(true);
#pragma warning restore CS0618
#endif
        foreach (var pi in all)
            if (pi.playerIndex + 1 == playerNumber) return pi;
        return null;
    }

    private void UpdateMessage(int playerNumber)
    {
        if (playerNumber <= 0) return;

        if (playerDisconnected != null)
        {
            playerDisconnected.Arguments = new object[] { playerNumber };
            playerDisconnected.GetLocalizedStringAsync().Completed += op =>
            {
                if (messageLabel) messageLabel.text = op.Result ?? $"Player {playerNumber} disconnected";
            };
        }
        else if (messageLabel)
        {
            messageLabel.text = $"Player {playerNumber} disconnected";
        }
    }

    private void ShowPanel()
    {
        if (!panel) return;
        panel.SetActive(true);
        if (exitButton) exitButton.gameObject.SetActive(showExitButton);
    }

    private void HidePanelImmediate()
    {
        if (!panel) return;
        panel.SetActive(false);
        if (exitButton) exitButton.gameObject.SetActive(false);
    }

    private bool IsActiveTurn(int playerNumber) => GetActivePlayerNumber() == playerNumber;

    private int GetActivePlayerNumber()
    {
        if (GameManager.Instance == null) return -1;
        return GameManager.Instance.CurrentIndex + 1; // 0-based → 1-based
    }

    public void ExitToMainMenu()
    {
        if (frozeGame && GameManager.Instance)
        {
            GameManager.Instance.PopFreeze();
            frozeGame = false;
        }
        if (GameManager.Instance)
            GameManager.Instance.ExitLevel();
    }
}