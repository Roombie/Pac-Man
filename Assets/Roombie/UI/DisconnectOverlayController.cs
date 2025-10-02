using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

namespace Roombie.UI
{
    /// <summary>
    /// Multi-slot overlay that shows per-player presence (keyboard/gamepad).
    /// Supports "required" slots (must be present) and optional slots (may re-bind while open).
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
        [SerializeField] TMP_Text confirmHintText; // e.g. "PRESS SUBMIT WHEN READY"

        [Header("View Root")]
        [SerializeField] GameObject viewRoot;

        [Header("Confirm Input")]
        [SerializeField] InputActionReference submitAction; // drag your UI/Submit (or Player/Submit)

        // Runtime
        readonly List<PlayerCardView> cards = new();
        Sprite _iconKeyboard;
        Sprite _iconGamepad;
        bool _initialized;
        int _expectedPlayers;
        InputAction _submitAction;
        bool _submitHooked;

        // Required slots set (only these must present to allow Submit)
        HashSet<int> _requiredSlots = new();

        void Awake()
        {
            if (!viewRoot) viewRoot = gameObject;
            viewRoot.SetActive(false);
            SetupSubmitAction();
        }

        void OnDestroy()
        {
            if (_submitAction != null)
                _submitAction.performed -= OnSubmitPerformed;
            HookSubmit(false);
        }

        void SetupSubmitAction()
        {
            _submitAction = submitAction ? submitAction.action : null;
            if (_submitAction == null)
            {
                Debug.LogWarning("[DisconnectOverlay] No confirmActionRef assigned (UI/Submit recommended).");
                return;
            }
            _submitAction.performed -= OnSubmitPerformed;
            _submitAction.performed += OnSubmitPerformed;
        }

        void OnSubmitPerformed(InputAction.CallbackContext _)
        {
            if (!viewRoot || !viewRoot.activeInHierarchy) return;
            if (AreRequiredSlotsPresent())
                GameManager.Instance.FinishRejoin();
        }

        // ---------- Public API (what GameManager calls) ----------

        /// <summary>Initialize icons once, if using inspector defaults.</summary>
        public void InitializeIfNeeded()
        {
            _iconKeyboard = iconKeyboard;
            _iconGamepad = iconGamepad;
            _initialized = true;
            PushIconsToCards();
        }

        /// <summary>
        /// Define how many cards to build and which slots are required in the current rejoin session.
        /// </summary>
        public void SetMode(int totalPlayers, IEnumerable<int> requiredSlots)
        {
            if (totalPlayers < 1) totalPlayers = 1;

            _requiredSlots.Clear();
            if (requiredSlots != null)
            {
                foreach (var r in requiredSlots)
                    _requiredSlots.Add(Mathf.Max(0, r));
            }

            RebuildCards(totalPlayers);
        }

        /// <summary>Rebuild card list to exactly this count.</summary>
        public void RebuildCards(int count)
        {
            if (count < 1) count = 1;
            _expectedPlayers = count;
            if (!_initialized) InitializeIfNeeded();
            ClearCards();
            BuildCards(_expectedPlayers);
        }

        /// <summary>Set keyboard/gamepad presence for a slot (0-based).</summary>
        public void SetPresenceForSlot(int slotIndex, bool hasKeyboard, bool hasGamepad)
        {
            if (slotIndex < 0) return;
            if (slotIndex >= cards.Count)
                RebuildCards(Mathf.Max(slotIndex + 1, _expectedPlayers > 0 ? _expectedPlayers : cards.Count));

            var card = cards[slotIndex];
            if (!card) return;

            // keep card visible; just swap/hide icon internally
            card.Show(true);
            card.SetPresence(hasKeyboard, hasGamepad);
        }

        /// <summary>Toggle overlay visibility; when showing, reset presence and enable Submit.</summary>
        public void Show(bool show)
        {
            if (!viewRoot) return;

            if (show && cards.Count == 0)
                RebuildCards(_expectedPlayers > 0 ? _expectedPlayers : 1);

            viewRoot.SetActive(show);
            HookSubmit(show);

            if (show)
            {
                // IMPORTANT: start from a blank state so previous icons don't linger
                ClearAllPresence();
                RefreshAll();
            }
        }

        // ---------- Internals ----------

        void PushIconsToCards()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.SetIcons(_iconKeyboard, _iconGamepad);
            }
        }

        void BuildCards(int count)
        {
            if (!cardsParent || !cardPrefab) return;

            for (int i = 0; i < count; i++)
            {
                var card = Instantiate(cardPrefab, cardsParent);
                card.SetIndex(i);
                card.SetIcons(_iconKeyboard, _iconGamepad);

                if (slotTints != null && slotTints.Count > 0)
                    card.ApplyTint(slotTints[i % slotTints.Count]);

                card.Show(true);
                // start with "no device" (this must hide the icon in PlayerCardView)
                card.SetPresence(false, false);

                cards.Add(card);
            }
        }

        void HookSubmit(bool hook)
        {
            if (!submitAction || submitAction.action == null) return;

            if (hook && !_submitHooked)
            {
                submitAction.action.Enable();
                submitAction.action.performed += OnSubmitPerformed;
                _submitHooked = true;
            }
            else if (!hook && _submitHooked)
            {
                submitAction.action.performed -= OnSubmitPerformed;
                submitAction.action.Disable();
                _submitHooked = false;
            }
        }

        void ClearCards()
        {
            for (int i = cards.Count - 1; i >= 0; i--)
                if (cards[i]) Destroy(cards[i].gameObject);
            cards.Clear();
        }

        void RefreshAll()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.Show(true); // presence is fed externally via SetPresenceForSlot
            }
        }

        /// <summary>Disable the icon on all cards and clear presence flags.</summary>
        public void ClearAllPresence()
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c) continue;
                c.SetPresence(false, false); // must hide image inside PlayerCardView
            }
        }

        bool AreRequiredSlotsPresent()
        {
            if (cards.Count == 0) return false;

            // If there are explicitly required slots, only check those.
            if (_requiredSlots.Count > 0)
            {
                foreach (var slot in _requiredSlots)
                {
                    if (slot < 0 || slot >= cards.Count) return false;
                    var c = cards[slot];
                    if (!c || !c.HasAnyPresence) return false;
                }
                return true;
            }

            // Fallback: require at least one card with presence
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c && c.HasAnyPresence) return true;
            }
            return false;
        }
    }
}