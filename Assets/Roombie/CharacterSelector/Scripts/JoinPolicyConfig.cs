using UnityEngine;

namespace Roombie.CharacterSelect
{
    [CreateAssetMenu(fileName = "JoinPolicyConfig", menuName = "Roombie/Input/Join Policy Config")]
    public class JoinPolicyConfig : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Seconds to wait after a successful claim before another panel can claim.")]
        public float claimDebounceSeconds = 0.20f;
    }
}