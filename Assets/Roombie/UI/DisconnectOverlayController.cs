using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Roombie.UI
{
    /// <summary>
    /// UI overlay that displays connection status for each player slot
    /// Shows per-player device presence (keyboard/gamepad). InputManager detects which device
    /// was pressed and confirms the rejoin, this class only renders it
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
        [SerializeField] TMP_Text confirmHintText; // Shown once the player has picked a device

        [Header("View Root")]
        [SerializeField] GameObject viewRoot; // Root GameObject of the overlay UI

        // Runtime data
        readonly List<PlayerCardView> cards = new();
        Sprite iconKeyboardRuntime;
        Sprite iconGamepadRuntime;
        bool initialized;
        int expectedPlayers;

        // Slots that must pick a device during this rejoin
        HashSet<int> requiredSlots = new();

        void Awake()
        {
            // Ensure the overlay starts hidden
            if (!viewRoot) viewRoot = gameObject;
            viewRoot.SetActive(false);
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
        public void SetPresenceForSlot(int slotIndex, bool hasKeyboard, bool hasGamepad, string keyboardScheme = null)
        {
            if (slotIndex < 0) return;
            if (slotIndex >= cards.Count)
                RebuildCards(Mathf.Max(slotIndex + 1, expectedPlayers > 0 ? expectedPlayers : cards.Count));

            var card = cards[slotIndex];
            if (!card) return;

            card.Show(true);
            card.SetPresence(hasKeyboard, hasGamepad, keyboardScheme);

            Debug.Log($"[DisconnectOverlay] Slot {slotIndex} - Keyboard: {hasKeyboard} ({keyboardScheme ?? "-"}), Gamepad: {hasGamepad}");
        }

        /// <summary>
        /// Shows the overlay with every card empty, waiting for the player to press a device
        /// </summary>
        public void ShowWaitingForPresence()
        {
            if (!viewRoot) return;

            if (cards.Count == 0)
                RebuildCards(expectedPlayers > 0 ? expectedPlayers : 1);

            viewRoot.SetActive(true);

            ClearAllPresence();
            RefreshAll();
            ShowConfirmHint(false);
            Debug.Log("[DisconnectOverlay] Overlay shown - waiting for device presence");
        }

        /// <summary>
        /// Toggles the "press A to confirm" hint once a device has been picked
        /// </summary>
        public void ShowConfirmHint(bool show)
        {
            if (confirmHintText) confirmHintText.gameObject.SetActive(show);
        }

        /// <summary>
        /// Toggles the entire overlay on or off
        /// </summary>
        public void Show(bool show)
        {
            if (!viewRoot) return;

            if (show)
            {
                ShowWaitingForPresence();
            }
            else
            {
                viewRoot.SetActive(false);
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
    }
}