using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

namespace Roombie.UI
{
    public class PlayerCardView : MonoBehaviour
    {
        [Header("Header (localized)")]
        [SerializeField] TMP_Text titleText;
        [SerializeField] LocalizedString playerLabelLocalized;

        [Header("Single Icon Image")]
        [SerializeField] Image deviceIconImage;

        [Header("Sprites")]
        [SerializeField] Sprite iconKeyboard;
        [SerializeField] Sprite iconGamepad;

        [Header("Tintables")]
        [SerializeField] Image frameImage;
        public bool HasAnyPresence { get; private set; }

        int _playerIndex = -1;
        bool _labelSubscribed;

        void Start()
        {
            // Only hide if SetPresence hasn't already shown an icon.
            // Start fires the frame after Instantiate, so it would overwrite
            // a presence that was set immediately after BuildCards.
            if (!HasAnyPresence)
                deviceIconImage.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_labelSubscribed && playerLabelLocalized != null)
            {
                playerLabelLocalized.StringChanged -= OnLabelChanged;
                _labelSubscribed = false;
            }
        }

        public void SetIndex(int zeroBasedIndex)
        {
            _playerIndex = zeroBasedIndex;

            // Write the label right away; the prefab's text is a hard-coded "P1" and the async
            // StringChanged callback isn't guaranteed to land (cards are built mid-input-callback,
            // paused, under an inactive overlay), which left every card reading "P1"
            if (titleText) titleText.text = $"P{_playerIndex + 1}";
            if (playerLabelLocalized == null || playerLabelLocalized.IsEmpty) return;

            playerLabelLocalized.Arguments = new object[] { _playerIndex + 1 };
            if (titleText) titleText.text = playerLabelLocalized.GetLocalizedString(_playerIndex + 1);

            // Keep listening so the label follows language changes
            if (!_labelSubscribed)
            {
                playerLabelLocalized.StringChanged += OnLabelChanged;
                _labelSubscribed = true;
            }
            playerLabelLocalized.RefreshString();
        }

        public void SetIcons(Sprite keyboard, Sprite gamepad)
        {
            iconKeyboard = keyboard;
            iconGamepad = gamepad;
        }

        /// <summary>Keyboard has priority; hides image if neither present.</summary>
        public void SetPresence(bool hasKeyboard, bool hasGamepad)
        {
            HasAnyPresence = hasKeyboard || hasGamepad;

            if (!deviceIconImage) return;

            if (hasKeyboard)
            {
                if (iconKeyboard != null)
                {
                    deviceIconImage.sprite  = iconKeyboard;
                    deviceIconImage.gameObject.SetActive(true);
                }
                else deviceIconImage.gameObject.SetActive(false);

                return;
            }

            if (hasGamepad)
            {
                if (iconGamepad != null)
                {
                    deviceIconImage.sprite = iconGamepad;
                    deviceIconImage.gameObject.SetActive(true);
                }
                else deviceIconImage.gameObject.SetActive(false);

                return;
            }

            // none
            deviceIconImage.gameObject.SetActive(false);
        }

        public void ApplyTint(Color c)
        {
            if (frameImage) frameImage.color = c;
            if (titleText)  titleText.color  = c;
        }

        public void Show(bool show) => gameObject.SetActive(show);

        void OnLabelChanged(string value)
        {
            if (titleText) titleText.text = value;
        }
    }
}