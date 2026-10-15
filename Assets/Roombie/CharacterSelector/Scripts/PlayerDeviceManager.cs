using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Device & scheme reservation service + debounce + helper lookups.
    /// Any device joins the first free panel: a gamepad, or either keyboard half (P1Keyboard/P2Keyboard),
    /// which keeps its own scheme whichever panel it lands on. Debounce follows JoinPolicyConfig.
    /// </summary>
    public class PlayerDeviceManager : MonoBehaviour
    {
        public static PlayerDeviceManager Instance { get; private set; }

        [Header("Data")]
        [SerializeField] private KeyboardSchemeRegistry keyboardRegistry;
        [SerializeField] private JoinPolicyConfig joinPolicyConfig;

        // Reservations
        private readonly HashSet<string> reservedKeyboardSchemes = new();
        private readonly HashSet<int> reservedGamepads = new();

        // Debounce
        [SerializeField, Tooltip("Seconds to wait after a successful claim before another panel can claim.")]
        private float fallbackClaimDebounceSeconds = 0.20f;
        private float lastClaimTime = -999f;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
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

        /// <summary>Every keyboard half's scheme, e.g. { "P1Keyboard", "P2Keyboard" }</summary>
        public string[] AllKeyboardSchemes()
        {
            if (keyboardRegistry == null)
                return new[] { "P1Keyboard", "P2Keyboard" };
            return keyboardRegistry.AllAsArray();
        }

        #endregion

        #region Scheme resolution
        /// <summary>
        /// Which keyboard half a press came from (e.g. "P2Keyboard"), or null if the key isn't bound
        /// to any keyboard scheme. Not tied to a panel: the half keeps its scheme on whichever panel it joins
        /// </summary>
        public string GetKeyboardSchemeForControl(InputAction action, InputControl control)
        {
            if (action == null || control == null) return null;
            return FirstMatchingKeyboardGroup(action, control, AllKeyboardSchemes());
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

        public void ResetAllReservations()
        {
            reservedKeyboardSchemes.Clear();
            reservedGamepads.Clear();
            lastClaimTime = -999f;
            Debug.Log("[PlayerDeviceManager] ResetAllReservations: cleared schemes, gamepads, debounce.");
        }

        public static void DebugLogGroupMismatch(InputAction action, InputControl control)
        {
            Debug.LogWarning(
                $"[PlayerDeviceManager] '{control?.path}' on action '{action?.name}' isn't bound to any keyboard scheme " +
                $"({string.Join(", ", Instance.AllKeyboardSchemes())}). Check Input Actions group tokens (root composite + parts).");
        }
        #endregion
    }
}