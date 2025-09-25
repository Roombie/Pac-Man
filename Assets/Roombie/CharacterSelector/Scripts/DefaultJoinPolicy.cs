using System;
using UnityEngine.InputSystem;

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Default join policy driven by JoinPolicyConfig:
    /// - Singleplayer:
    ///     * spAcceptAllKeyboardGroups = true  → accept any configured keyboard group, normalize to P1
    ///     * false → only accept P1's group
    /// - Multiplayer:
    ///     * mpStrictKeyboardByPanel = true   → only accept the panel's own group (recommended)
    ///     * false → accept any group but still normalize reservation to THIS panel
    /// - Gamepad:
    ///     * gamepadJoinsFirstFree = true     → must claim the lowest-index free active panel
    /// </summary>
    [Serializable]
    public sealed class DefaultJoinPolicy : IJoinPolicy
    {
        private readonly JoinPolicyConfig cfg;

        public DefaultJoinPolicy(JoinPolicyConfig config) => cfg = config;

        public bool GamepadMustUseFirstFreeSlot =>
            cfg == null || cfg.gamepadJoinsFirstFree;

        public string ResolveKeyboardScheme(
            InputAction action,
            InputControl control,
            bool isSinglePlayer,
            int panelIndex,
            Func<int, string> forPanel,
            Func<string[]> singlePlayerCandidates,
            Func<InputAction, InputControl, string[], string> firstMatching
        )
        {
            if (control == null || action == null) return null;

            if (isSinglePlayer)
            {
                // Strict SP: only P1 group
                if (cfg != null && !cfg.spAcceptAllKeyboardGroups)
                {
                    var expected = forPanel(0);
                    var match = firstMatching(action, control, new[] { expected });
                    return match != null ? expected : null;
                }

                // Default SP: accept any configured group → normalize to P1
                var all = singlePlayerCandidates();
                var matched = firstMatching(action, control, all);
                return matched != null ? forPanel(0) : null;
            }
            else
            {
                var expected = forPanel(panelIndex);
                if (string.IsNullOrEmpty(expected)) return null;

                // Flexible MP (not recommended, but supported)
                if (cfg != null && !cfg.mpStrictKeyboardByPanel)
                {
                    var all = singlePlayerCandidates();
                    var matched = firstMatching(action, control, all);
                    return matched != null ? expected : null;
                }

                // Strict MP: only this panel's group
                var m = firstMatching(action, control, new[] { expected });
                return m != null ? expected : null;
            }
        }
    }
}