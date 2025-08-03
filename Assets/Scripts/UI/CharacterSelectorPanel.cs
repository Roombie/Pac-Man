using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterSelectorPanel : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text playerLabel;
    public Image characterImage;
    public TMP_Text characterNameText;

    [Header("Character Data")]
    public CharacterData[] characters;
    private int currentIndex = 0;
    private int currentSkinIndex = 0;

    public CharacterData SelectedCharacter => characters[currentIndex];
    public CharacterSkin SelectedSkin => SelectedCharacter.skins[currentSkinIndex];

    private void OnEnable()
    {
        currentIndex = 0;
        currentSkinIndex = 0;
        UpdateDisplay();
    }

    public void NextCharacter()
    {
        currentIndex = (currentIndex + 1) % characters.Length;
        currentSkinIndex = 0;
        UpdateDisplay();
    }

    public void PreviousCharacter()
    {
        currentIndex = (currentIndex - 1 + characters.Length) % characters.Length;
        currentSkinIndex = 0;
        UpdateDisplay();
    }

    public void NextSkin()
    {
        currentSkinIndex = (currentSkinIndex + 1) % SelectedCharacter.skins.Length;
        UpdateDisplay();
    }

    public void PreviousSkin()
    {
        currentSkinIndex = (currentSkinIndex - 1 + SelectedCharacter.skins.Length) % SelectedCharacter.skins.Length;
        UpdateDisplay();
    }

    public void ConfirmSelection()
    {
        Debug.Log($"{playerLabel.text} selected {SelectedCharacter.characterName} ({SelectedSkin.skinName})");
        // Save to PlayerPrefs or GameSettings if needed
    }

    private void UpdateDisplay()
    {
        var data = SelectedCharacter;
        var skin = SelectedSkin;

        characterImage.sprite = skin.lifeIconSprite;
        characterNameText.text = $"{data.characterName}";
    }
}