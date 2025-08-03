using UnityEngine;

public class CharacterSelectionManager : MonoBehaviour
{
    [Header("Character Selection Panels")]
    public GameObject panelContainer;

    private CharacterSelectorPanel[] playerPanels;
    private int activePlayers = 1;

    private void OnEnable()
    {        
        int gameMode = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 0); // 0 = singleplayer, 1 = multiplayer

        if (gameMode == 0)
        {
            activePlayers = 1;
        }
        else
        {
            // This is used to determine number of active players in multiplayer
            activePlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 2); // Default to 2
        }

        playerPanels = panelContainer.GetComponentsInChildren<CharacterSelectorPanel>(true);
        InitializePanels();
    }

   private void InitializePanels()
    {
        for (int i = 0; i < playerPanels.Length; i++)
        {
            bool isActive = i < activePlayers;
            playerPanels[i].gameObject.SetActive(isActive);
        }
    }

    public void ConfirmAllSelections()
    {
        for (int i = 0; i < activePlayers; i++)
        {
            var panel = playerPanels[i];
            var character = panel.SelectedCharacter;
            var skin = panel.SelectedSkin;

            PlayerPrefs.SetString($"SelectedCharacter_Player{i + 1}_Name", character.characterName);
            PlayerPrefs.SetString($"SelectedCharacter_Player{i + 1}_Skin", skin.skinName);
        }

        PlayerPrefs.Save();
        Debug.Log("All selections saved.");
    }
} 