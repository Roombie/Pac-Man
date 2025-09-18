using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDeviceManager : MonoBehaviour
{
    public static PlayerDeviceManager Instance { get; private set; }

    [SerializeField] private string[] keyboardSchemes = { "P1Keyboard", "P2Keyboard" };
    [SerializeField] private string[] bothKeyboardGroups = { "P1Keyboard", "P2Keyboard" };

    private readonly HashSet<string> claimedKeyboardSchemes = new();
    private readonly HashSet<int> claimedGamepadIds = new();

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

    // ----------------- KEYBOARD -----------------
    public bool TryReserveKeyboardScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme)) return false;

        if (claimedKeyboardSchemes.Contains(scheme))
            return false;

        claimedKeyboardSchemes.Add(scheme);
        Debug.Log($"[PlayerDeviceManager] Reserved keyboard scheme: {scheme}");
        return true;
    }

    public void ReleaseKeyboardScheme(string scheme)
    {
        if (!string.IsNullOrEmpty(scheme) && claimedKeyboardSchemes.Contains(scheme))
        {
            claimedKeyboardSchemes.Remove(scheme);
            Debug.Log($"[PlayerDeviceManager] Released keyboard scheme: {scheme}");
        }
    }

    public string GetKeyboardSchemeForControl(InputAction action, InputControl control, bool isSinglePlayer = false)
    {
        if (action == null || control == null) return null;

        int idx = action.GetBindingIndexForControl(control);
        if (idx < 0) return null;

        var groups = action.bindings[idx].groups;
        if (string.IsNullOrEmpty(groups)) return null;

        // In singleplayer, allow any group from bothKeyboardGroups
        if (isSinglePlayer)
        {
            foreach (var scheme in bothKeyboardGroups)
                if (groups.Contains(scheme))
                    return scheme;
        }

        // In multiplayer, use only the normal schemes
        foreach (var scheme in keyboardSchemes)
            if (groups.Contains(scheme))
                return scheme;

        return null;
    }

    public string ReserveNextKeyboardScheme(bool allowDuplicateIfExhausted = true)
    {
        foreach (var s in keyboardSchemes)
        {
            if (!claimedKeyboardSchemes.Contains(s))
            {
                claimedKeyboardSchemes.Add(s);
                return s;
            }
        }

        return allowDuplicateIfExhausted && keyboardSchemes.Length > 0
            ? keyboardSchemes[0]
            : null;
    }

    // ----------------- GAMEPAD -----------------
    public bool TryReserveGamepad(Gamepad gamepad)
    {
        if (gamepad == null) return false;
        int id = gamepad.deviceId;

        if (claimedGamepadIds.Contains(id))
            return false;

        claimedGamepadIds.Add(id);
        Debug.Log($"[PlayerDeviceManager] Reserved gamepad: {gamepad.displayName}");
        return true;
    }

    public void ReleaseGamepad(Gamepad gamepad)
    {
        if (gamepad == null) return;
        if (claimedGamepadIds.Remove(gamepad.deviceId))
            Debug.Log($"[PlayerDeviceManager] Released gamepad: {gamepad.displayName}");
    }

    public void ReleaseGamepadById(int id)
    {
        if (claimedGamepadIds.Remove(id))
            Debug.Log($"[PlayerDeviceManager] Released gamepad id {id}");
    }
}