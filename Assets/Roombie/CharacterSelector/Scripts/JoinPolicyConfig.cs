using UnityEngine;

namespace Roombie.CharacterSelect
{
    [CreateAssetMenu(fileName = "JoinPolicyConfig", menuName = "Roombie/Input/Join Policy Config")]
    public class JoinPolicyConfig : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Seconds to wait after a successful claim before another panel can claim.")]
        public float claimDebounceSeconds = 0.20f;

        [Header("Gamepad")]
        [Tooltip("If true, gamepads can only claim the lowest-index free active panel.")]
        public bool gamepadJoinsFirstFree = true;

        [Header("Keyboard")]
        [Tooltip("In singleplayer, accept any keyboard group and normalize to panel 0.")]
        public bool spAcceptAllKeyboardGroups = true;

        [Tooltip("In multiplayer, each panel only accepts its own keyboard group.")]
        public bool mpStrictKeyboardByPanel = true;
    }
}