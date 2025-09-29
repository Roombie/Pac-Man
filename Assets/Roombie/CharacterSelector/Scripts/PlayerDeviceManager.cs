using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // InputControlPath

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Device & scheme reservation service + debounce + helper lookups.
    /// Keyboard resolution is PANEL-STRICT in MP (exact token match only).
    /// Gamepad + debounce follow JoinPolicyConfig.
    /// </summary>
    public class PlayerDeviceManager : MonoBehaviour
    {
        public static PlayerDeviceManager Instance { get; private set; }

        [Header("Data")]
        [SerializeField] private KeyboardSchemeRegistry keyboardRegistry;
        [SerializeField] private JoinPolicyConfig joinPolicyConfig;

        // Optional policy (used for gamepad & debounce rules only; not for keyboard scheme resolution)
        private IJoinPolicy joinPolicy;

        // Reservations
        private readonly HashSet<string> reservedKeyboardSchemes = new();
        private readonly HashSet<int> reservedGamepads = new();

        // Debounce
        [SerializeField, Tooltip("Seconds to wait after a successful claim before another panel can claim.")]
        private float fallbackClaimDebounceSeconds = 0.20f;
        private float lastClaimTime = -999f;

        // Temp array to avoid allocs
        private readonly string[] oneCandidate = new string[1];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            joinPolicy = new DefaultJoinPolicy(joinPolicyConfig);
        }

        #region Debounce
        public bool CanClaimNow()
        {
            float debounce = joinPolicyConfig != null ? joinPolicyConfig.claimDebounceSeconds : fallbackClaimDebounceSeconds;
            return Time.unscaledTime - lastClaimTime >= debounce;
        }

        public void MarkClaimed() => lastClaimTime = Time.unscaledTime;
        #endregion

        #region Keyboard reservations
        public bool TryReserveKeyboardScheme(string schemeName)
        {
            if (string.IsNullOrEmpty(schemeName)) return false;
            if (reservedKeyboardSchemes.Contains(schemeName)) return false;
            reservedKeyboardSchemes.Add(schemeName);
            return true;
        }

        public void ReleaseKeyboardScheme(string schemeName)
        {
            if (string.IsNullOrEmpty(schemeName)) return;
            reservedKeyboardSchemes.Remove(schemeName);
        }
        #endregion

        #region Gamepad reservations
        public bool TryReserveGamepad(Gamepad gp)
        {
            if (gp == null) return false;
            if (reservedGamepads.Contains(gp.deviceId)) return false;
            reservedGamepads.Add(gp.deviceId);
            return true;
        }

        public void ReleaseGamepad(Gamepad gp)
        {
            if (gp == null) return;
            reservedGamepads.Remove(gp.deviceId);
        }

        public bool GamepadMustUseFirstFreeSlot() => joinPolicy != null && joinPolicy.GamepadMustUseFirstFreeSlot;
        #endregion

        #region Registry helpers
        public string ForPanel(int panelIndex)
        {
            var n = keyboardRegistry != null ? keyboardRegistry.ForPanel(panelIndex) : null;
            return string.IsNullOrEmpty(n) ? $"P{panelIndex + 1}Keyboard" : n; // safe default
        }

        public string[] SinglePlayerCandidates()
        {
            if (keyboardRegistry == null)
            {
                Debug.LogError("[PlayerDeviceManager] KeyboardSchemeRegistry is not assigned. " +
                               "Create/assign it to PlayerDeviceManager.");
                return new[] { "P1Keyboard", "P2Keyboard" }; // temporary safe default
            }
            return keyboardRegistry.AllAsArray();
        }

        public string[] ForPanelAsArray(int panelIndex)
        {
            oneCandidate[0] = ForPanel(panelIndex);
            return oneCandidate;
        }
        #endregion

        #region Scheme resolution (PANEL-STRICT in MP; exact token only)
        /// <summary>
        /// MP: ONLY this panel's keyboard group is accepted (panel 0→"P1Keyboard", panel 1→"P2Keyboard", ...).
        /// SP: accept any registered keyboard group that truly fired the binding (exact token match).
        /// </summary>
        public string GetKeyboardSchemeForControl(
            InputAction action,
            InputControl control,
            bool isSinglePlayer,
            int panelIndex)
        {
            if (action == null || control == null) return null;

            if (isSinglePlayer)
            {
                return FirstMatchingKeyboardGroup(action, control, SinglePlayerCandidates());
            }

            // MP strict: expect this panel's exact token
            var expected = ForPanel(panelIndex);
            if (!string.IsNullOrEmpty(expected) && GroupsContain(action, control, expected))
                return expected;

            // No fallback: must be exact
            return null;
        }
        #endregion

        #region Binding-group helpers
        /// Returns the first candidate group that both:
        /// 1) appears as an exact token in the binding's groups, and
        /// 2) matches the pressed control via binding.effectivePath.
        public static string FirstMatchingKeyboardGroup(InputAction action, InputControl control, string[] candidates)
        {
            if (action == null || control == null || candidates == null) return null;

            for (int i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrEmpty(c)) continue;
                if (GroupsContain(action, control, c)) return c;
            }
            return null;
        }

        /// Exact token match on binding groups + effective path match against the pressed control.
        public static bool GroupsContain(InputAction action, InputControl control, string groupName)
        {
            if (action == null || control == null || string.IsNullOrEmpty(groupName)) return false;

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];

                // Must match the control path (part binding for composites, simple binding otherwise)
                if (!InputControlPath.Matches(b.effectivePath, control))
                    continue;

                // 1) Direct token on this binding?
                if (!string.IsNullOrEmpty(b.groups))
                {
                    foreach (var tok in b.groups.Split(';'))
                        if (tok.Trim() == groupName) return true;
                }

                // 2) If it's a part of a composite, look up the composite parent token
                if (b.isPartOfComposite)
                {
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (bindings[p].isComposite)
                        {
                            var parentGroups = bindings[p].groups;
                            if (!string.IsNullOrEmpty(parentGroups))
                            {
                                foreach (var tok in parentGroups.Split(';'))
                                    if (tok.Trim() == groupName) return true;
                            }
                            break;
                        }
                        if (!bindings[p].isPartOfComposite) break; // safety
                    }
                }
            }
            return false;
        }

        /// Big, friendly log when a keypress was ignored for a panel (to help fix the asset).
        public static void DebugLogGroupMismatch(InputAction action, InputControl control, int panelIndex)
        {
            var expected = Instance.ForPanel(panelIndex);

            // Try to detect which token actually fired (if any)
            string fired = null;
            if (action != null && control != null)
            {
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (string.IsNullOrEmpty(b.groups)) continue;
                    if (!InputControlPath.Matches(b.effectivePath, control)) continue;

                    var tokens = b.groups.Split(';');
                    for (int t = 0; t < tokens.Length; t++)
                    {
                        var tok = tokens[t].Trim();
                        if (!string.IsNullOrEmpty(tok)) { fired = tok; break; }
                    }
                    if (fired != null) break;
                }
            }

            Debug.LogWarning(
                $"[PlayerDeviceManager] Panel {panelIndex} expected EXACT group '{expected}', but keypress was from '{(fired ?? "<none>")}'. " +
                $"Fix the Input Actions Group tokens on Move/Submit/Cancel/Select to match exactly.");
        }
        #endregion
    }
}
