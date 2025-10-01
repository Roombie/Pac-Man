using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        #endregion

        #region Registry helpers
        /// <summary>
        /// Returns the exact binding-group token this panel should accept, e.g. "P1Keyboard".
        /// Accepts registry values that might be format templates: "P{0}Keyboard", "P{index}Keyboard", etc.
        /// Falls back to default "P{n}Keyboard" if the registry is missing/invalid.
        /// </summary>
        public string ForPanel(int panelIndex)
        {
            int n = panelIndex + 1;
            string raw = keyboardRegistry != null ? keyboardRegistry.ForPanel(panelIndex) : null;

            string resolved = ResolvePanelGroupToken(raw, n);
            if (string.IsNullOrEmpty(resolved))
                resolved = $"P{n}Keyboard";

            // final safety: only accept sane tokens like P1Keyboard..P8Keyboard
            if (!Regex.IsMatch(resolved, @"^P[1-8]Keyboard$"))
                resolved = $"P{n}Keyboard";

            return resolved;
        }

        public static string ResolvePanelGroupToken(string token, int oneBasedIndex)
        {
            if (string.IsNullOrEmpty(token)) return null;

            // common placeholders
            string result = token
                .Replace("{index}", oneBasedIndex.ToString())
                .Replace("{0}", oneBasedIndex.ToString());

            // Also support forms like "P{1}Keyboard" (hard-coded number) → replace with current index
            result = Regex.Replace(result, @"P\{\d+\}Keyboard", $"P{oneBasedIndex}Keyboard");

            // Trim leftover braces/spaces just in case
            result = result.Replace("{", "").Replace("}", "").Trim();

            return result;
        }

        public string[] SinglePlayerCandidates()
        {
            // SP path still uses all groups as candidates
            if (keyboardRegistry == null)
                return new[] { "P1Keyboard", "P2Keyboard" };
            return keyboardRegistry.AllAsArray();
        }

        public string[] ForPanelAsArray(int panelIndex)
        {
            oneCandidate[0] = ForPanel(panelIndex);
            return oneCandidate;
        }
        #endregion

        #region Scheme resolution (PANEL-STRICT in MP; exact token only)
        public string GetKeyboardSchemeForControl(
            InputAction action,
            InputControl control,
            bool isSinglePlayer,
            int panelIndex)
        {
            if (action == null || control == null) return null;

            if (isSinglePlayer)
                return FirstMatchingKeyboardGroup(action, control, SinglePlayerCandidates());

            // MP strict: expect this panel's exact token
            var expected = ForPanel(panelIndex);
            if (!string.IsNullOrEmpty(expected) && GroupsContainForPanel(action, control, expected, panelIndex))
                return expected;

            return null;
        }
        #endregion

        #region Binding-group helpers
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

        public static bool GroupsContain(InputAction action, InputControl control, string groupName)
        {
            if (action == null || control == null || string.IsNullOrEmpty(groupName)) return false;

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];

                if (!InputControlPath.Matches(b.effectivePath, control))
                    continue;

                // direct token
                if (!string.IsNullOrEmpty(b.groups))
                {
                    foreach (var tok in b.groups.Split(';'))
                        if (tok.Trim() == groupName) return true;
                }

                // composite parent
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
                        if (!bindings[p].isPartOfComposite) break;
                    }
                }
            }
            return false;
        }

        public static bool GroupsContainForPanel(InputAction action, InputControl control, string groupName, int panelIndex)
        {
            if (action == null || control == null || string.IsNullOrEmpty(groupName)) return false;

            int oneBased = panelIndex + 1;
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (!InputControlPath.Matches(b.effectivePath, control))
                    continue;

                // direct / template tokens on this binding
                if (!string.IsNullOrEmpty(b.groups))
                {
                    foreach (var tok in b.groups.Split(';'))
                    {
                        var t = tok.Trim();
                        if (t == groupName) return true;

                        // resolve templates like "P{0}Keyboard" or "P{index}Keyboard" or "P{2}Keyboard"
                        var resolved = ResolvePanelGroupToken(t, oneBased);
                        if (!string.IsNullOrEmpty(resolved) && resolved == groupName) return true;
                    }
                }

                // composite parent case
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
                                {
                                    var t = tok.Trim();
                                    if (t == groupName) return true;
                                    var resolved = ResolvePanelGroupToken(t, oneBased);
                                    if (!string.IsNullOrEmpty(resolved) && resolved == groupName) return true;
                                }
                            }
                            break;
                        }
                        if (!bindings[p].isPartOfComposite) break;
                    }
                }
            }
            return false;
        }

        public void ResetAllReservations()
        {
            reservedKeyboardSchemes.Clear();
            reservedGamepads.Clear();
            lastClaimTime = -999f;
            Debug.Log("[PlayerDeviceManager] ResetAllReservations: cleared schemes, gamepads, debounce.");
        }

        public static void DebugLogGroupMismatch(InputAction action, InputControl control, int panelIndex)
        {
            var expected = Instance.ForPanel(panelIndex);
            string fired = null, resolved = null;

            if (action != null && control != null)
            {
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (string.IsNullOrEmpty(b.groups)) continue;
                    if (!InputControlPath.Matches(b.effectivePath, control)) continue;

                    foreach (var tok in b.groups.Split(';'))
                    {
                        var t = tok.Trim();
                        if (string.IsNullOrEmpty(t)) continue;
                        fired = t;
                        resolved = ResolvePanelGroupToken(t, panelIndex + 1);
                        break;
                    }
                    if (fired != null) break;
                }
            }

            Debug.LogWarning(
            $"[PlayerDeviceManager] Panel {panelIndex} expected '{expected}', " +
            $"binding group was '{(fired ?? "<none>")}', resolved='{(resolved ?? "<n/a>")}'. " +
            "Check Input Actions group tokens (root composite + parts).");
        }
        #endregion
    }
}