using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization;

public class PanelUIRenderer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text playerLabelText;
    [SerializeField] private GameObject characterData;
    [SerializeField] private Image characterImage;
    [SerializeField] private TMP_Text characterNameText;
    [SerializeField] private Transform skinIconContainer;
    [SerializeField] private GameObject skinIconPrefab;
    [SerializeField] private GameObject joinInstruction;
    [SerializeField] private LocalizedString playerLabelLocalized;

    private CharacterData[] characters;
    private int playerIndex = -1;

    private readonly List<GameObject> skinIcons = new();
    private readonly List<Image> selectionIndicators = new(); // 👈 store Image directly

    private PanelState state;
    private PanelInputHandler inputHandler;

    // ---------------- INITIALIZE ----------------
    public void Initialize(PanelState panelState, int index, CharacterData[] characters, PanelInputHandler handler)
    {
        this.playerIndex = index;
        this.state = panelState;
        this.characters = characters;
        this.inputHandler = handler;

        ApplyPlayerLabel();
        ResetUI();
        SetupSkins();
        ShowSkinOptions(false); // Skins hidden until a character is selected
    }

    private void ApplyPlayerLabel()
    {
        if (playerLabelText != null)
        {
            playerLabelLocalized.Arguments = new object[] { playerIndex + 1 };
            playerLabelLocalized.StringChanged += (val) => playerLabelText.text = val;
            playerLabelLocalized.RefreshString();
        }
    }

    public void ResetUI()
    {
        characterData.SetActive(false);
        joinInstruction.SetActive(true);
        ShowSkinOptions(false);

        foreach (var go in skinIcons) Destroy(go);
        skinIcons.Clear();
        selectionIndicators.Clear();
    }

    public void ShowJoinedState()
    {
        joinInstruction.SetActive(false);
        characterData.SetActive(true);

        Debug.Log($"[PanelUIRenderer] ShowJoinedState called by Player {playerIndex}");
    }

    public void ShowJoinPrompt()
    {
        joinInstruction.SetActive(true);
        characterData.SetActive(false);
        ShowSkinOptions(false);

        Debug.Log($"[PanelUIRenderer] ShowJoinPrompt called by Player {playerIndex}");
    }

    // ---------------- CHARACTER DATA ----------------
    public void SetCharacterData(CharacterData[] allCharacters)
    {
        characters = allCharacters;
        UpdateDisplay();
        SetupSkins();
        ShowSkinOptions(false); // Keep hidden until confirmed
    }

    public void UpdateDisplay()
    {
        if (characters == null || characters.Length == 0) return;

        var character = characters[state.CharacterIndex];
        var skin = character.skins[state.SkinIndex];

        characterImage.sprite = skin.previewSprite;
        characterNameText.text = character.characterName;

        UpdateSkinIndicators();
    }

    public void SetupSkins()
    {
        if (characters == null || characters.Length == 0) return;

        var character = characters[state.CharacterIndex];
        var skins = character.skins;

        while (skinIcons.Count < skins.Length)
        {
            var go = Instantiate(skinIconPrefab, skinIconContainer);
            skinIcons.Add(go);

            var indicator = go.transform.GetComponentInChildren<SelectorIndicator>(true);
            var img = indicator != null ? indicator.GetComponent<Image>() : null;
            selectionIndicators.Add(img);
        }

        for (int i = 0; i < skins.Length; i++)
        {
            var skin = skins[i];
            var go = skinIcons[i];
            go.SetActive(true);
            go.GetComponent<Image>().sprite = skin.skinIcon;
        }

        for (int i = skins.Length; i < skinIcons.Count; i++)
        {
            skinIcons[i].SetActive(false);
            if (selectionIndicators[i] != null)
                selectionIndicators[i].gameObject.SetActive(false);
        }

        UpdateSkinIndicators();
    }

    public void UpdateSkinIndicators()
    {
        for (int i = 0; i < skinIcons.Count; i++)
        {
            var img = selectionIndicators[i];
            if (img == null) continue;

            if (i == state.SkinIndex)
            {
                img.gameObject.SetActive(true);
                img.color = state.HasConfirmedSkin ? Color.yellow : Color.white;
            }
            else
            {
                img.gameObject.SetActive(false);
            }
        }
    }

    // ---------------- SKINS VISIBILITY ----------------
    public void ShowSkinOptions(bool visible)
    {
        if (skinIconContainer != null)
            skinIconContainer.gameObject.SetActive(visible);
    }

    // ---------------- INPUT SIGNATURE ----------------
    public (string scheme, int[] deviceIds) GetInputSignature()
    {
        return inputHandler != null
            ? inputHandler.GetInputSignature()
            : (null, System.Array.Empty<int>());
    }
}