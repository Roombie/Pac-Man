using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

public class PanelUIRenderer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text playerLabelText;
    [SerializeField] private GameObject characterData;   // container for character info
    [SerializeField] private Image characterImage;
    [SerializeField] private TMP_Text characterNameText;
    [SerializeField] private Transform skinIconContainer;
    [SerializeField] private GameObject skinIconPrefab;
    [SerializeField] private GameObject joinInstruction;
    [SerializeField] private LocalizedString playerLabelLocalized;

    private PanelState state;
    private CharacterData[] characters;
    private readonly List<Image> skinIcons = new List<Image>();
    private readonly List<GameObject> selectionIndicators = new List<GameObject>();
    private int playerIndex;

    // Keep a reference to detach the localization callback cleanly
    private bool labelSubscribed = false;

    // ---------------- Initialization ----------------
    public void Initialize(PanelState state, int index, CharacterData[] characters, PanelInputHandler input)
    {
        this.state = state;
        this.characters = characters;
        this.playerIndex = index;

        ApplyPlayerLabel();
        ShowJoinPrompt();       // default state
        ShowSkinOptions(false); // skins hidden until the character is selected
    }

    private void OnDestroy()
    {
        // Best-effort cleanup for the localized label (avoid leaks / double binds on scene reloads)
        if (labelSubscribed && playerLabelLocalized != null)
        {
            playerLabelLocalized.StringChanged -= OnPlayerLabelStringChanged;
            labelSubscribed = false;
        }
    }

    // ---------------- States ----------------
    public void ShowJoinPrompt()
    {
        if (joinInstruction != null) joinInstruction.SetActive(true);
        if (characterData   != null) characterData.SetActive(false);

        // Hide skin strip and clear any transient highlights
        ShowSkinOptions(false);
        ClearSkinIndicators();
    }

    public void ShowJoinedState()
    {
        if (joinInstruction != null) joinInstruction.SetActive(false);
        if (characterData   != null) characterData.SetActive(true);

        // Show current character snapshot (safe if arrays not set yet)
        if (characters != null && characters.Length > 0)
        {
            int ci = Mathf.Clamp(state.CharacterIndex, 0, characters.Length - 1);
            var character = characters[ci];
            UpdateCharacterAndSkins(character, state.SkinIndex);
        }

        // Keep skins hidden until player selects the character
        ShowSkinOptions(false);
    }

    public void ShowSkinOptions(bool show)
    {
        if (skinIconContainer != null)
            skinIconContainer.gameObject.SetActive(show);
    }

    /// <summary>
    /// Update character name, preview image, and rebuild the skin icons.
    /// </summary>
    public void UpdateCharacterAndSkins(CharacterData character, int skinIndex)
    {
        if (character == null) return;

        // Clamp skin index to valid range
        int clampedSkinIndex = 0;
        if (character.skins != null && character.skins.Length > 0)
            clampedSkinIndex = Mathf.Clamp(skinIndex, 0, character.skins.Length - 1);

        if (characterImage != null && character.skins != null && character.skins.Length > 0)
            characterImage.sprite = character.skins[clampedSkinIndex].previewSprite;

        if (characterNameText != null)
            characterNameText.text = character.characterName;

        InitializeSkins(character);
        UpdateSkinHighlight(clampedSkinIndex);
    }

    /// <summary>
    /// Update only skin image + highlight; does not rebuild the skin strip.
    /// Safe even if skins were not initialized yet.
    /// </summary>
    public void UpdateSkinOnly(CharacterData character, int skinIndex)
    {
        if (character == null || character.skins == null || character.skins.Length == 0)
            return;

        int clamped = Mathf.Clamp(skinIndex, 0, character.skins.Length - 1);

        if (characterImage != null)
            characterImage.sprite = character.skins[clamped].previewSprite;

        // Only update highlight if the strip exists
        if (selectionIndicators.Count > 0)
        {
            UpdateSkinHighlight(clamped);
        }
    }

    // ---------------- Skins ----------------
    private void InitializeSkins(CharacterData character)
    {
        if (skinIconContainer == null || skinIconPrefab == null) return;

        // Clear old icons
        for (int i = skinIconContainer.childCount - 1; i >= 0; i--)
        {
            var child = skinIconContainer.GetChild(i);
            if (child != null) Destroy(child.gameObject);
        }

        skinIcons.Clear();
        selectionIndicators.Clear();

        if (character.skins == null || character.skins.Length == 0) return;

        for (int i = 0; i < character.skins.Length; i++)
        {
            var skin = character.skins[i];

            GameObject iconGO = Instantiate(skinIconPrefab, skinIconContainer);

            // Skin icon image
            var icon = iconGO.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = skin.skinIcon;
                icon.color  = Color.white;
                skinIcons.Add(icon);
            }
            else
            {
                skinIcons.Add(null);
            }

            // Indicator child (optional)
            var indicator = iconGO.transform.GetComponentInChildren<SelectorIndicator>(true);
            if (indicator != null)
            {
                selectionIndicators.Add(indicator.gameObject);
                var img = indicator.GetComponent<Image>();
                if (img != null) img.color = Color.white;
                indicator.gameObject.SetActive(false);
            }
            else
            {
                selectionIndicators.Add(null);
            }
        }
    }

    /// <summary>
    /// Turn on the highlight indicator for the hovered/selected skin.
    /// </summary>
    public void UpdateSkinHighlight(int selectedIndex)
    {
        if (selectionIndicators.Count == 0) return;

        for (int i = 0; i < selectionIndicators.Count; i++)
        {
            var go = selectionIndicators[i];
            if (go != null)
                go.SetActive(i == selectedIndex);
        }
    }

    /// <summary>
    /// Toggle confirmation color (yellow) for the confirmed skin; white otherwise.
    /// </summary>
    public void UpdateSkinConfirmationIndicator(int confirmedIndex, bool hasConfirmedSkin)
    {
        if (selectionIndicators.Count == 0) return;

        for (int i = 0; i < selectionIndicators.Count; i++)
        {
            var go = selectionIndicators[i];
            if (go == null) continue;

            var img = go.GetComponent<Image>();
            if (img != null)
                img.color = (hasConfirmedSkin && i == confirmedIndex) ? Color.yellow : Color.white;
        }
    }

    private void ClearSkinIndicators()
    {
        if (selectionIndicators.Count == 0) return;
        for (int i = 0; i < selectionIndicators.Count; i++)
        {
            var go = selectionIndicators[i];
            if (go != null)
            {
                var img = go.GetComponent<Image>();
                if (img != null) img.color = Color.white;
                go.SetActive(false);
            }
        }
    }

    // ---------------- Player Label ----------------
    private void ApplyPlayerLabel()
    {
        if (playerLabelText == null || playerLabelLocalized == null) return;

        playerLabelLocalized.Arguments = new object[] { playerIndex + 1 };

        // Avoid double subscription if Initialize is called again
        if (!labelSubscribed)
        {
            playerLabelLocalized.StringChanged += OnPlayerLabelStringChanged;
            labelSubscribed = true;
        }

        playerLabelLocalized.RefreshString();
    }

    private void OnPlayerLabelStringChanged(string val)
    {
        if (playerLabelText != null)
            playerLabelText.text = val;
    }
}