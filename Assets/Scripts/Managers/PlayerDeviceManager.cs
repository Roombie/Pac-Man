using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDeviceManager : MonoBehaviour
{
    public static PlayerDeviceManager Instance { get; private set; }

    private readonly HashSet<string> reservedKeyboardSchemes = new();
    private readonly HashSet<int> reservedGamepads = new();

    public string[] BothKeyboardGroups => new[] { "P1Keyboard", "P2Keyboard" };

    [SerializeField] private bool singlePlayer = false;
    public bool IsSinglePlayer => singlePlayer;

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

    // ---------------- GAMEPAD ----------------
    public bool TryReserveGamepad(Gamepad gamepad)
    {
        if (gamepad == null) return false;
        if (reservedGamepads.Contains(gamepad.deviceId)) return false;

        reservedGamepads.Add(gamepad.deviceId);
        Debug.Log($"[PlayerDeviceManager] Reserved Gamepad (ID {gamepad.deviceId})");
        return true;
    }

    public void ReleaseGamepad(Gamepad gamepad)
    {
        if (gamepad == null) return;
        if (reservedGamepads.Remove(gamepad.deviceId))
        {
            Debug.Log($"[PlayerDeviceManager] Released Gamepad (ID {gamepad.deviceId})");
        }
    }

    // ---------------- KEYBOARD ----------------
    /// <summary>
    /// Reserve a keyboard scheme, with optional playerIndex enforcement.
    /// </summary>
    public bool TryReserveKeyboardScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme)) return false;
        if (reservedKeyboardSchemes.Contains(scheme)) return false;

        reservedKeyboardSchemes.Add(scheme);
        Debug.Log($"[PlayerDeviceManager] Reserved keyboard scheme: {scheme}");
        return true;
    }

    /// <summary>
    /// Overload that enforces per-player scheme rules in multiplayer.
    /// </summary>
    public bool TryReserveKeyboardScheme(string scheme, int playerIndex)
    {
        if (string.IsNullOrEmpty(scheme)) return false;

        if (!IsSinglePlayer)
        {
            string expected = GetKeyboardSchemeForIndex(playerIndex);
            if (expected != scheme)
            {
                Debug.LogWarning($"[PlayerDeviceManager] Player {playerIndex} tried to claim {scheme}, but only {expected} is allowed.");
                return false;
            }
        }

        if (reservedKeyboardSchemes.Contains(scheme))
        {
            Debug.LogWarning($"[PlayerDeviceManager] Scheme {scheme} already reserved.");
            return false;
        }

        reservedKeyboardSchemes.Add(scheme);
        Debug.Log($"[PlayerDeviceManager] Reserved keyboard scheme: {scheme} for Player {playerIndex}");
        return true;
    }

    public void ReleaseKeyboardScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme)) return;
        if (reservedKeyboardSchemes.Remove(scheme))
        {
            Debug.Log($"[PlayerDeviceManager] Released keyboard scheme: {scheme}");
        }
    }

    /// <summary>
    /// Returns the next available keyboard scheme (mainly for singleplayer).
    /// </summary>
    public string ReserveNextKeyboardScheme()
    {
        foreach (var scheme in BothKeyboardGroups)
        {
            if (TryReserveKeyboardScheme(scheme)) return scheme;
        }
        return null;
    }

    // ---------------- KEYBOARD SCHEME RESOLUTION ----------------
    public string GetKeyboardSchemeForIndex(int index)
    {
        if (index == 0) return "P1Keyboard";
        if (index == 1) return "P2Keyboard";
        return null;
    }

    /// <summary>
    /// Original version (3 args), kept for backward compatibility.
    /// </summary>
    public string GetKeyboardSchemeForControl(InputAction action, InputControl control, bool isSinglePlayer)
    {
        if (isSinglePlayer)
        {
            return FirstMatchingKeyboardGroup(action, control, BothKeyboardGroups);
        }
        else
        {
            // Without playerIndex context, return null
            return null;
        }
    }

    /// <summary>
    /// New version (4 args) that enforces per-player keyboard mapping.
    /// </summary>
    public string GetKeyboardSchemeForControl(InputAction action, InputControl control, bool isSinglePlayer, int playerIndex)
    {
        if (isSinglePlayer)
        {
            string scheme = FirstMatchingKeyboardGroup(action, control, BothKeyboardGroups);
            if (!string.IsNullOrEmpty(scheme))
                Debug.Log($"[PlayerDeviceManager] Singleplayer match: {scheme}");
            return scheme;
        }
        else
        {
            string targetScheme = GetKeyboardSchemeForIndex(playerIndex);
            if (GroupsContain(action, control, targetScheme))
            {
                Debug.Log($"[PlayerDeviceManager] Multiplayer match: {targetScheme} for player {playerIndex}");
                return targetScheme;
            }
            return null;
        }
    }

    // ---------------- HELPERS ----------------
    private static bool GroupsContain(InputAction action, InputControl control, string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || action == null || control == null) return false;
        int idx = action.GetBindingIndexForControl(control);
        if (idx < 0) return false;
        string groups = action.bindings[idx].groups;
        return !string.IsNullOrEmpty(groups) &&
               groups.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FirstMatchingKeyboardGroup(InputAction action, InputControl control, string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (GroupsContain(action, control, c)) return c;
        }
        return null;
    }
}