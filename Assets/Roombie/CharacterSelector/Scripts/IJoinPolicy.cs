using System;
using UnityEngine.InputSystem;

namespace Roombie.CharacterSelect
{
    /// <summary>
    /// Strategy interface for join rules. Decides who may claim and how keyboard groups resolve.
    /// Implementations should be stateless or read-only (read ScriptableObject configs).
    /// </summary>
    public interface IJoinPolicy
    {
        /// <summary>
        /// Resolve which keyboard scheme (e.g., "P1Keyboard") should be reserved for this press,
        /// or return null if the press is not valid for this panel under the current rules.
        /// </summary>
        /// <param name="action">InputAction that received the event</param>
        /// <param name="control">Concrete control used (e.g., Keyboard.aKey)</param>
        /// <param name="isSinglePlayer">Whether the selection scene is in SP mode</param>
        /// <param name="panelIndex">Panel index attempting to claim (0-based)</param>
        /// <param name="forPanel">Helper: maps panel index -> scheme name</param>
        /// <param name="singlePlayerCandidates">Helper: all keyboard schemes eligible in SP</param>
        /// <param name="firstMatching">
        /// Helper: returns the first candidate scheme that matches this action/control, or null if none.
        /// </param>
        string ResolveKeyboardScheme(
            InputAction action,
            InputControl control,
            bool isSinglePlayer,
            int panelIndex,
            Func<int, string> forPanel,
            Func<string[]> singlePlayerCandidates,
            Func<InputAction, InputControl, string[], string> firstMatching
        );

        /// <summary>
        /// True if gamepads must claim the lowest-index free active panel (first-free-slot rule).
        /// </summary>
        bool GamepadMustUseFirstFreeSlot { get; }
    }
}