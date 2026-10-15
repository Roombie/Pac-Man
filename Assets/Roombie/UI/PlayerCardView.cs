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
        [Tooltip("Optional: shows which keyboard half the player has (WASD / ARROWS). Created under the icon if empty.")]
        [SerializeField] TMP_Text keyboardHalfText;

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

        /// <summary>
        /// Keyboard has priority; hides image if neither present.
        /// keyboardScheme (e.g. "P1Keyboard") labels which half of the keyboard the player has
        /// </summary>
        public void SetPresence(bool hasKeyboard, bool hasGamepad, string keyboardScheme = null)
        {
            HasAnyPresence = hasKeyboard || hasGamepad;
            ShowKeyboardHalf(hasKeyboard ? keyboardScheme : null);

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
            if (keyboardHalfText) keyboardHalfText.color = c;
        }

        /// <summary>"WASD" for the left half, "ARROWS" for the right; hidden for gamepads / no device</summary>
        void ShowKeyboardHalf(string keyboardScheme)
        {
            string label = KeyboardHalfLabel(keyboardScheme);
            if (string.IsNullOrEmpty(label))
            {
                if (keyboardHalfText) keyboardHalfText.gameObject.SetActive(false);
                return;
            }

            EnsureKeyboardHalfText();
            if (!keyboardHalfText) return;

            keyboardHalfText.text = label;
            keyboardHalfText.gameObject.SetActive(true);
        }

        static string KeyboardHalfLabel(string scheme) => scheme switch
        {
            "P1Keyboard" => "WASD",
            "P2Keyboard" => "ARROWS",
            _ => null
        };

        // Built once under the icon (its parent stacks children vertically), styled like the title
        void EnsureKeyboardHalfText()
        {
            if (keyboardHalfText || !deviceIconImage) return;

            var go = new GameObject("Keyboard Half Label", typeof(RectTransform));
            go.transform.SetParent(deviceIconImage.transform.parent, false);
            go.transform.SetSiblingIndex(deviceIconImage.transform.GetSiblingIndex() + 1);
            ((RectTransform)go.transform).sizeDelta = new Vector2(deviceIconImage.rectTransform.sizeDelta.x * 2f, 14f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (titleText)
            {
                tmp.font = titleText.font;
                tmp.fontSize = titleText.fontSize;
                tmp.color = titleText.color;
            }
            else tmp.fontSize = 16f; // should probably just make it a normal text object at this point ngl
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;

            keyboardHalfText = tmp;
        }

        public void Show(bool show) => gameObject.SetActive(show);

        void OnLabelChanged(string value)
        {
            if (titleText) titleText.text = value;
        }
    }
}