using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

namespace Roombie.UI
{
    /// <summary>
    /// UI overlay that displays connection status for each player slot
    /// Shows per-player device presence (keyboard/gamepad) and manages the rejoin confirmation process
    /// </summary>
    public class DisconnectOverlayController : MonoBehaviour
    {
        [Header("Structure")]
        [SerializeField] Transform cardsParent;
        [SerializeField] PlayerCardView cardPrefab;

        [Header("Sprites")]
        [SerializeField] Sprite iconKeyboard;
        [SerializeField] Sprite iconGamepad;

        [Header("Per-Player Tints (wraps if fewer than players)")]
        [SerializeField] List<Color> slotTints = new() { Color.red, Color.blue, Color.green, Color.cyan };

        [Header("Hint")]
        [SerializeField] TMP_Text confirmHintText; // Text shown to instruct players to confirm rejoin

        [Header("View Root")]
        [SerializeField] GameObject viewRoot; // Root GameObject of the overlay UI

        [Header("Confirm Input")]
        [SerializeField] InputActionReference submitAction; // Input action for confirming rejoin (usually UI/Submit)

        // Runtime data
        readonly List<PlayerCardView> cards = new();
        Sprite iconKeyboardRuntime;
        Sprite iconGamepadRuntime;
        bool initialized;
        int expectedPlayers;
        InputAction submitActionRuntime;
        bool submitHooked;

        // Tracks which slots must be present before rejoin is allowed
        HashSet<int> requiredSlots = new();

        void Awake()
        {
            // Ensure the overlay starts hidden and set up the input listener
            if (!viewRoot) viewRoot = gameObject;
            viewRoot.SetActive(false);
            SetupSubmitAction();
        }

        void OnDestroy()
        {
            // Unhook the submit action when destroyed
            if (submitActionRuntime != null)
                submitActionRuntime.performed -= OnSubmitPerformed;
            HookSubmit(false);
        }

        /// <summary>
        /// Initializes and hooks the submit InputAction used to confirm rejoin
        /// </summary>
        void SetupSubmitAction()
        {
            submitActionRuntime = submitAction ? submitAction.action : null;
            if (submitActionRuntime == null)
            {
                Debug.LogWarning("[DisconnectOverlay] No submitActionRef assigned (UI/Submit recommended)");
                return;
            }

            submitActionRuntime.performed -= OnSubmitPerformed;
            submitActionRuntime.performed += OnSubmitPerformed;
        }

        /// <summary>
        /// Called when the player presses the submit input (e.g., Start or Enter)
        /// Checks if all required players are present before completing rejoin
        /// </summary>
        void OnSubmitPerformed(InputAction.CallbackContext ctx)
        {
            if (!viewRoot || !viewRoot.activeInHierarchy) return;

            Debug.Log($"[DisconnectOverlay] Submit pressed - Required slots present: {AreRequiredSlotsPresent()}");

            if (AreRequiredSlotsPresent())
            {
                Debug.Log("[DisconnectOverlay] Finishing global rejoin process");
                InputManager.Instance.FinishRejoin("Auto");
                Show(false);
            }
            else
            {
                Debug.Log("[DisconnectOverlay] Submit pressed but required slots not present");
            }
        }

        /// <summary>
        /// Initializes keyboard and gamepad icons once if needed
        /// </summary>
        public void InitializeIfNeeded()
        {
            iconKeyboardRuntime = iconKeyboard;
            iconGamepadRuntime = iconGamepad;
            initialized = true;
            PushIconsToCards();
        }

        /// <summary>
        /// Sets up the overlay for a specific number of players and defines required slots
        /// </summary>
        public void SetMode(int totalPlayers, IEnumerable<int> requiredSlotsInput)
        {
            if (totalPlayers < 1) totalPlayers = 1;

            requiredSlots.Clear();
            if (requiredSlotsInput != null)
            {
                foreach (var r in requiredSlotsInput)
                    requiredSlots.Add(Mathf.Max(0, r));
            }

            RebuildCards(totalPlayers);
        }

        /// <summary>
        /// Recreates the card layout for all player slots
        /// </summary>
        public void RebuildCards(int count)
        {
            if (count < 1) count = 1;
            expectedPlayers = count;
            if (!initialized) InitializeIfNeeded();
            ClearCards();
            BuildCards(expectedPlayers);
        }

        /// <summary>
        /// Updates presence state for a given player slot (keyboard/gamepad icons)
        /// </summary>
        public void SetPresenceForSlot(int slotIndex, bool hasKeyboard, bool hasGamepad)
        {
            if (slotIndex < 0) return;
            if (slotIndex >= cards.Count)
                RebuildCards(Mathf.Max(slotIndex + 1, expectedPlayers > 0 ? expectedPlayers : cards.Count));

            var card = cards[slotIndex];
            if (!card) return;

            card.Show(true);
            card.SetPresence(hasKeyboard, hasGamepad);

            Debug.Log($"[DisconnectOverlay] Slot {slotIndex} - Keyboard: {hasKeyboard}, Gamepad: {hasGamepad}");
        }

        /// <summary>
        /// Toggles the entire overlay on or off
        /// When shown, it resets presence and enables UI input for rejoin
        /// </summary>
        public void Show(bool show)
        {
            if (!viewRoot) return;

            if (show && cards.Count == 0)
                RebuildCards(expectedPlayers > 0 ? expectedPlayers : 1);

            viewRoot.SetActive(show);
            HookSubmit(show);

            if (show)
            {
                ClearAllPresence();
                RefreshAll();
                EnableUIInput();
                Debug.Log("[DisconnectOverlay] Overlay shown - waiting for submit input");
            }
            else
            {
                Debug.Log("[DisconnectOverlay] Overlay hidden");
            }
        }

        // ---------- Internal Helpers ----------

        /// <summary>
        /// Pushes the assigned keyboard and gamepad icons to all player cards
        /// </summary>
        void PushIconsToCards()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.SetIcons(iconKeyboardRuntime, iconGamepadRuntime);
            }
        }

        /// <summary>
        /// Instantiates PlayerCardView elements for each player slot
        /// </summary>
        void BuildCards(int count)
        {
            if (!cardsParent || !cardPrefab) return;

            for (int i = 0; i < count; i++)
            {
                var card = Instantiate(cardPrefab, cardsParent);
                card.SetIndex(i);
                card.SetIcons(iconKeyboardRuntime, iconGamepadRuntime);

                if (slotTints != null && slotTints.Count > 0)
                    card.ApplyTint(slotTints[i % slotTints.Count]);

                card.Show(true);
                card.SetPresence(false, false);

                cards.Add(card);
            }
        }

        /// <summary>
        /// Hooks or unhooks the submit InputAction depending on visibility
        /// </summary>
        void HookSubmit(bool hook)
        {
            if (submitActionRuntime == null)
            {
                Debug.LogWarning("[DisconnectOverlay] No submit action available to hook");
                return;
            }

            if (hook && !submitHooked)
            {
                submitActionRuntime.Enable();
                submitActionRuntime.performed += OnSubmitPerformed;
                submitHooked = true;
                Debug.Log("[DisconnectOverlay] Submit action hooked and enabled");
            }
            else if (!hook && submitHooked)
            {
                submitActionRuntime.performed -= OnSubmitPerformed;
                submitActionRuntime.Disable();
                submitHooked = false;
                Debug.Log("[DisconnectOverlay] Submit action unhooked and disabled");
            }
        }

        /// <summary>
        /// Destroys all existing player cards before rebuilding
        /// </summary>
        void ClearCards()
        {
            for (int i = cards.Count - 1; i >= 0; i--)
                if (cards[i]) Destroy(cards[i].gameObject);
            cards.Clear();
        }

        /// <summary>
        /// Refreshes visibility of all cards without changing presence state
        /// </summary>
        void RefreshAll()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.Show(true);
            }
        }

        /// <summary>
        /// Enables UI input map for all PlayerInput components during rejoin
        /// </summary>
        private void EnableUIInput()
        {
            var playerInputs = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
            foreach (var playerInput in playerInputs)
            {
                if (playerInput != null)
                {
                    playerInput.actions.FindActionMap("Player", false)?.Disable();
                    playerInput.actions.FindActionMap("UI", false)?.Enable();
                    Debug.Log($"[DisconnectOverlay] Enabled UI action map for {playerInput.gameObject.name}");
                }
            }
        }

        /// <summary>
        /// Clears presence indicators on all cards (removes icons)
        /// </summary>
        public void ClearAllPresence()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.SetPresence(false, false);
            }
        }

        /// <summary>
        /// Checks if all required player slots are present before allowing confirmation
        /// If no required slots exist, at least one player must be connected
        /// </summary>
        bool AreRequiredSlotsPresent()
        {
            if (cards.Count == 0) return false;

            // Check required slots first if defined
            if (requiredSlots.Count > 0)
            {
                foreach (var slot in requiredSlots)
                {
                    if (slot < 0 || slot >= cards.Count) return false;
                    var c = cards[slot];
                    if (!c || !c.HasAnyPresence) return false;
                }
                return true;
            }

            // Fallback: at least one player must have any device
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c && c.HasAnyPresence) return true;
            }
            return false;
        }
    }
}