using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;


public class PlayerDeviceManager : MonoBehaviour
{
    public static PlayerDeviceManager Instance { get; private set; }

    [Header("Keyboard schemes (order defines which panel gets which scheme)")]
    [Tooltip("Example: [\"P1Keyboard\", \"P2Keyboard\", \"P3Keyboard\", \"P4Keyboard\"]")]
    [SerializeField] private string[] keyboardSchemes = { "P1Keyboard", "P2Keyboard" };

    // --- Reservations ---
    // Reserve keyboard "schemes" logically by name (NOT by device id). This allows sharing a single physical keyboard.
    private readonly HashSet<string> reservedKeyboardSchemes = new HashSet<string>();
    // Reserve gamepads exclusively by device id.
    private readonly HashSet<int> reservedGamepads = new HashSet<int>();

    // --- Claim debounce (prevents two panels from claiming simultaneously on the same frame) ---
    [Header("Claim Debounce")]
    [SerializeField, Tooltip("Seconds to wait after a successful claim before another panel can claim")]
    private float claimDebounceSeconds = 0.2f;
    private float lastClaimTime = -999f;

    // Reusable 1-element array to avoid GC when we need a single candidate scheme
    private readonly string[] oneCandidate = new string[1];

    public IReadOnlyList<string> KeyboardSchemes => keyboardSchemes;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #region Public API — Debounce

    /// <summary>
    /// Returns true if enough time has elapsed since the last successful claim.
    /// Use this to prevent two panels from claiming at the same time.
    /// </summary>
    public bool CanClaimNow()
    {
        return Time.unscaledTime - lastClaimTime >= claimDebounceSeconds;
    }

    /// <summary>
    /// Call this immediately after a successful claim (keyboard or gamepad).
    /// </summary>
    public void MarkClaimed()
    {
        lastClaimTime = Time.unscaledTime;
    }

    #endregion

    #region Public API — Keyboard reservations (logical, by scheme name)

    /// <summary>
    /// Try to reserve a logical keyboard scheme by name (e.g., "P1Keyboard", "P2Keyboard").
    /// This does NOT block other schemes from using the same physical keyboard device.
    /// </summary>
    public bool TryReserveKeyboardScheme(string schemeName)
    {
        if (string.IsNullOrEmpty(schemeName)) return false;
        if (reservedKeyboardSchemes.Contains(schemeName)) return false; // one player per scheme
        reservedKeyboardSchemes.Add(schemeName);
        return true;
    }

    /// <summary>
    /// Release the reservation for a logical keyboard scheme.
    /// </summary>
    public void ReleaseKeyboardScheme(string schemeName)
    {
        if (string.IsNullOrEmpty(schemeName)) return;
        reservedKeyboardSchemes.Remove(schemeName);
    }

    /// <summary>
    /// Returns the next available logical keyboard scheme, in the order of the serialized array.
    /// If none is available, returns null.
    /// </summary>
    public string ReserveNextKeyboardScheme()
    {
        for (int i = 0; i < keyboardSchemes.Length; i++)
        {
            var name = keyboardSchemes[i];
            if (string.IsNullOrEmpty(name)) continue;
            if (!reservedKeyboardSchemes.Contains(name))
            {
                reservedKeyboardSchemes.Add(name);
                return name;
            }
        }
        return null;
    }

    #endregion

    #region Public API — Gamepad reservations (exclusive, by device id)

    /// <summary>
    /// Try to reserve a gamepad exclusively by device id.
    /// </summary>
    public bool TryReserveGamepad(Gamepad gp)
    {
        if (gp == null) return false;
        if (reservedGamepads.Contains(gp.deviceId)) return false;
        reservedGamepads.Add(gp.deviceId);
        return true;
    }

    /// <summary>
    /// Release the reservation for a gamepad.
    /// </summary>
    public void ReleaseGamepad(Gamepad gp)
    {
        if (gp == null) return;
        reservedGamepads.Remove(gp.deviceId);
    }

    #endregion

    #region Scheme resolution helpers (Singleplayer vs Multiplayer)

    /// <summary>
    /// Returns the canonical scheme name for a given panel index, based on the serialized list.
    /// </summary>
    public string ForPanel(int panelIndex)
    {
        if (panelIndex < 0 || panelIndex >= keyboardSchemes.Length) return null;
        return keyboardSchemes[panelIndex];
    }

    /// <summary>
    /// Returns a cached 1-element candidate array for the given panel.
    /// Only use this from gameplay (single-thread) code.
    /// </summary>
    public string[] ForPanelAsArray(int panelIndex)
    {
        oneCandidate[0] = ForPanel(panelIndex);
        return oneCandidate;
    }

    /// <summary>
    /// Returns all scheme names as candidates for singleplayer (WASD or Arrows etc.).
    /// In singleplayer we still normalize to panel 0's scheme name after matching.
    /// </summary>
    public string[] SinglePlayerCandidates()
    {
        return keyboardSchemes; // ok to return the array; we do not modify it
    }

    /// <summary>
    /// Final resolver used by PanelInputHandler:
    /// - Singleplayer: accept any group in 'keyboardSchemes', but normalize to scheme of panel 0. (a friend suggested that)
    /// - Multiplayer: only accept the scheme that corresponds to this panel index.
    /// Returns the scheme name to reserve, or null if the control doesn't match allowed groups.
    /// </summary>
    public string GetKeyboardSchemeForControl(InputAction action, InputControl control, bool isSinglePlayer, int panelIndex)
    {
        if (control == null) return null;

        if (isSinglePlayer)
        {
            // In SP we allow any of the configured groups (e.g., P1Keyboard or P2Keyboard),
            // but we always normalize the result to panel 0's scheme (usually "P1Keyboard").
            var match = FirstMatchingKeyboardGroup(action, control, SinglePlayerCandidates());
            return match != null ? ForPanel(0) : null;
        }
        else
        {
            // In MP each panel can only claim its own scheme (by index).
            var expected = ForPanel(panelIndex);
            if (string.IsNullOrEmpty(expected)) return null;
            var match = FirstMatchingKeyboardGroup(action, control, ForPanelAsArray(panelIndex));
            return match != null ? expected : null;
        }
    }

    #endregion

    #region Binding group match helpers

    /// <summary>
    /// Returns the first candidate group that "contains" the given action/control, or null if none match.
    /// This relies on your action map having bindings grouped by binding 'groups' that match scheme names
    /// (e.g., binding groups named \"P1Keyboard\", \"P2Keyboard\", etc.).
    /// </summary>
    public static string FirstMatchingKeyboardGroup(InputAction action, InputControl control, string[] candidates)
    {
        if (action == null || control == null || candidates == null) return null;

        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            if (GroupsContain(action, control, c))
                return c;
        }
        return null;
    }

    /// <summary>
    /// Checks whether the given action/control is bound under a binding group with name 'groupName'.
    /// This uses Input System binding 'groups' (a semicolon-separated list on each binding).
    /// Note: Adjust this if your project uses a different grouping strategy.
    /// </summary>
    public static bool GroupsContain(InputAction action, InputControl control, string groupName)
    {
        if (action == null || control == null || string.IsNullOrEmpty(groupName))
            return false;

        // The control that triggered the callback (e.g. "<Keyboard>/w")
        string controlPath = control.path;

        // Scan bindings on the action and look for:
        //  1) an exact match of the binding group token
        //  2) AND the control matching the binding's effective path
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (string.IsNullOrEmpty(b.groups)) continue;

            // groups is a semicolon-separated list: "P1Keyboard;Gamepad"
            // Do an exact token compare (no substring false-positives).
            bool groupMatch = false;
            var groups = b.groups.Split(';');
            for (int g = 0; g < groups.Length; g++)
            {
                if (groups[g].Trim() == groupName)
                {
                    groupMatch = true;
                    break;
                }
            }
            if (!groupMatch) continue;

            // Path match: did THIS control satisfy THIS binding?
            // InputControlPath.Matches handles layout path expansion properly.
            if (InputControlPath.Matches(b.effectivePath, control))
            {
                Debug.Log($"[GroupsContain] control={controlPath} matched binding={b.effectivePath} groups={b.groups} for {groupName}");
                return true;
            } 
        }

        return false;
    }
    #endregion
}