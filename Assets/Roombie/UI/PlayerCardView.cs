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
            if (playerLabelLocalized == null) return;

            playerLabelLocalized.Arguments = new object[] { _playerIndex + 1 };
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