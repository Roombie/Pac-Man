using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // InputControlPath

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Device & scheme reservation service + debounce + helper lookups.
    /// Policy (SP/MP rules) is delegated to IJoinPolicy.
    /// </summary>
    public class PlayerDeviceManager : MonoBehaviour
    {
        public static PlayerDeviceManager Instance { get; private set; }

        [Header("Data")]
        [SerializeField] private KeyboardSchemeRegistry keyboardRegistry;
        [SerializeField] private JoinPolicyConfig joinPolicyConfig;

        // Policy strategy (default uses JoinPolicyConfig)
        private IJoinPolicy joinPolicy;

        // Reservations
        private readonly HashSet<string> reservedKeyboardSchemes = new();
        private readonly HashSet<int> reservedGamepads = new();

        // Debounce
        [SerializeField, Tooltip("Seconds to wait after a successful claim before another panel can claim.")]
        private float fallbackClaimDebounceSeconds = 0.20f;
        private float lastClaimTime = -999f;

        // Temp array to avoid allocations in tight loops
        private readonly string[] oneCandidate = new string[1];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Build default policy (can be swapped if you want custom behavior)
            joinPolicy = new DefaultJoinPolicy(joinPolicyConfig);
        }

        #region Debounce
        public bool CanClaimNow()
        {
            float debounce = joinPolicyConfig != null ? joinPolicyConfig.claimDebounceSeconds : fallbackClaimDebounceSeconds;
            return Time.unscaledTime - lastClaimTime >= debounce;
        }

        public void MarkClaimed()
        {
            lastClaimTime = Time.unscaledTime;
        }
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

        public string ReserveNextKeyboardScheme()
        {
            if (keyboardRegistry == null || keyboardRegistry.schemeNames == null) return null;

            for (int i = 0; i < keyboardRegistry.schemeNames.Count; i++)
            {
                var name = keyboardRegistry.schemeNames[i];
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

        #region Scheme resolution (delegates to policy)
        public bool GamepadMustUseFirstFreeSlot() => joinPolicy.GamepadMustUseFirstFreeSlot;

        /// <summary>
        /// Resolve which scheme should be reserved for this keypress according to the active policy.
        /// </summary>
        public string GetKeyboardSchemeForControl(
            InputAction action,
            InputControl control,
            bool isSinglePlayer,
            int panelIndex)
        {
            return joinPolicy.ResolveKeyboardScheme(
                action,
                control,
                isSinglePlayer,
                panelIndex,
                ForPanel,
                SinglePlayerCandidates,
                FirstMatchingKeyboardGroup
            );
        }
        #endregion

        #region Registry helpers
        public string ForPanel(int panelIndex)
        {
            return keyboardRegistry != null ? keyboardRegistry.ForPanel(panelIndex) : null;
        }

        public string[] SinglePlayerCandidates()
        {
            return keyboardRegistry != null ? keyboardRegistry.AllAsArray() : null;
        }

        public string[] ForPanelAsArray(int panelIndex)
        {
            oneCandidate[0] = ForPanel(panelIndex);
            return oneCandidate;
        }
        #endregion

        #region Binding group match helpers (exact token + effective path)
        /// <summary>
        /// Returns the first candidate group that both:
        /// 1) appears as an exact token in the binding's groups, and
        /// 2) matches the pressed control via binding.effectivePath.
        /// </summary>
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

        /// <summary>
        /// Exact token match on binding groups + effective path match against the pressed control.
        /// Avoids substring false-positives and ensures the right binding triggered.
        /// </summary>
        public static bool GroupsContain(InputAction action, InputControl control, string groupName)
        {
            if (action == null || control == null || string.IsNullOrEmpty(groupName)) return false;

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (string.IsNullOrEmpty(b.groups)) continue;

                // Split tokens and check exact equality
                var tokens = b.groups.Split(';');
                bool groupMatch = false;
                for (int t = 0; t < tokens.Length; t++)
                {
                    if (tokens[t].Trim() == groupName)
                    {
                        groupMatch = true;
                        break;
                    }
                }
                if (!groupMatch) continue;

                // Then ensure the pressed control actually matches the binding path
                if (InputControlPath.Matches(b.effectivePath, control))
                    return true;
            }
            return false;
        }
        #endregion
    }
}