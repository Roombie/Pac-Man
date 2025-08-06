using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class CharacterSelectionManager : MonoBehaviour
{
    public static CharacterSelectionManager Instance { get; private set; }

    [Header("Character Selection Panels")]
    public CharacterSelectorPanel[] playerPanels;

    [Header("Character Data")]
    public CharacterData[] allCharacters;

    [Header("Player Styles")]
    public PlayerStyle[] globalPlayerStyles;

    [Header("Unity Events")]
    public UnityEvent OnAllPlayersSelected;
    public UnityEvent OnAllPlayersConfirmed;
    public UnityEvent OnAnyPlayerDeselected;
    public UnityEvent OnAnySkinDeselected;
    public UnityEvent OnPlayerCancelledBeforeSelection;

    private int expectedPlayers = 1;
    private int readyCount = 0;
    private bool allSelectedFired = false;
    private bool allConfirmedFired = false;

    private Dictionary<int, PlayerStatus> joinedPlayers = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        ResetPanels();
    }

    public void ResetPanels()
    {
        expectedPlayers = Mathf.Clamp(PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1), 1, playerPanels.Length);
        Debug.Log($"[CharacterSelectionManager] GameMode={expectedPlayers}P");

        foreach (var panel in playerPanels)
        {
            panel.gameObject.SetActive(false);
            panel.SetPanelActive(false);
        }

        joinedPlayers.Clear();
        readyCount = 0;
        allSelectedFired = false;
        allConfirmedFired = false;

        for (int i = 0; i < expectedPlayers; i++)
        {
            SetupPanelForPlayer(i);

            // Force player i as manually joined if there's no player input
            if (!joinedPlayers.ContainsKey(i))
            {
                var panel = playerPanels[i];
                RegisterPlayer(i, panel);
                panel.SetPanelActive(true);
            }
        }
    }

    private void Start()
    {
        if (!gameObject.activeSelf) return;

        for (int i = 0; i < expectedPlayers && i < playerPanels.Length; i++)
        {
            if (!playerPanels[i].gameObject.activeSelf)
                SetupPanelForPlayer(i);
        }
    }

    public bool IsPlayerJoined(int index)
    {
        return joinedPlayers.TryGetValue(index, out var status) && status.HasJoined;
    }

    public void OnPlayerJoined(PlayerInput input)
    {
        int index = input.playerIndex;

        if (index >= playerPanels.Length)
        {
            Debug.LogWarning($"[CharacterSelectionManager] No panel available for player with index {index}.");
            return;
        }

        SetupPanelForPlayer(index);
        input.transform.SetParent(playerPanels[index].transform, false);

        RegisterPlayer(index, playerPanels[index]);
    }

    public void OnPlayerJoinedManual(int index, CharacterSelectorPanel panel)
    {
        if (index >= playerPanels.Length)
            return;

        RegisterPlayer(index, panel);
        panel.SetPanelActive(true);
    }

    private void RegisterPlayer(int index, CharacterSelectorPanel panel)
    {
        if (!joinedPlayers.ContainsKey(index))
        {
            joinedPlayers[index] = new PlayerStatus
            {
                HasJoined = true,
                Panel = panel
            };
        }
        else
        {
            var status = joinedPlayers[index];
            status.HasJoined = true;
            status.Panel = panel;
            joinedPlayers[index] = status;
        }
    }

    private void SetupPanelForPlayer(int index)
    {
        var panel = playerPanels[index];

        panel.gameObject.SetActive(true);
        panel.SetCharacterData(allCharacters);
        panel.SetPlayerIndex(index);
        panel.ApplyGlobalPlayerStyle(globalPlayerStyles[index]);

        if (joinedPlayers.ContainsKey(index) && joinedPlayers[index].HasJoined)
        {
            panel.SetPanelActive(true);
        }
        else
        {
            panel.SetPanelActive(false);
        }
    }

    public void ConfirmAllSelections()
    {
        foreach (var kvp in joinedPlayers)
        {
            var i = kvp.Key;
            var panel = kvp.Value.Panel;
            var character = panel.SelectedCharacter;
            var skin = panel.SelectedSkin;

            PlayerPrefs.SetString($"SelectedCharacter_Player{i + 1}_Name", character.characterName);
            PlayerPrefs.SetString($"SelectedCharacter_Player{i + 1}_Skin", skin.skinName);
        }

        PlayerPrefs.Save();
        Debug.Log("All selections saved.");
    }

    public bool IsSelectionTaken(CharacterSelectorPanel requester, CharacterData character, CharacterSkin skin)
    {
        foreach (var kvp in joinedPlayers)
        {
            var panel = kvp.Value.Panel;
            if (panel == requester) continue;

            if (panel.SelectedCharacter == character && panel.SelectedSkin == skin)
                return true;
        }

        return false;
    }

    public void NotifyPlayerReady(CharacterSelectorPanel panel)
    {
        readyCount++;
        Debug.Log($"{panel.name} is ready. Total ready: {readyCount}/{expectedPlayers}");

        foreach (var kvp in joinedPlayers)
        {
            if (kvp.Value.Panel == panel)
            {
                var status = kvp.Value;
                status.IsReady = true;
                joinedPlayers[kvp.Key] = status;
                break;
            }
        }

        TryFireFinalConfirmation();
    }

    public void NotifyPlayerDeselected(CharacterSelectorPanel panel)
    {
        Debug.Log($"{panel.name} has deselected character.");
        OnAnyPlayerDeselected?.Invoke();

        foreach (var kvp in joinedPlayers)
        {
            if (kvp.Value.Panel == panel)
            {
                var status = kvp.Value;
                status.HasSelectedCharacter = false;
                status.HasConfirmedSkin = false;
                joinedPlayers[kvp.Key] = status;
                break;
            }
        }

        allSelectedFired = false;
        CheckAllPlayersSelected();
    }

    public void NotifyPlayerUnready(CharacterSelectorPanel panel)
    {
        readyCount = Mathf.Max(readyCount - 1, 0);
        OnAnySkinDeselected?.Invoke();
        Debug.Log($"{panel.name} is no longer ready. Total ready: {readyCount}/{expectedPlayers}");

        foreach (var kvp in joinedPlayers)
        {
            if (kvp.Value.Panel == panel)
            {
                var status = kvp.Value;
                status.IsReady = false;
                joinedPlayers[kvp.Key] = status;
                break;
            }
        }

        allSelectedFired = false;
    }

    public void CheckAllPlayersSelected()
    {
        if (joinedPlayers.Count < expectedPlayers) return;

        foreach (var kvp in joinedPlayers)
        {
            if (!kvp.Value.HasJoined) continue;
            var panel = kvp.Value.Panel;
            if (!panel.HasSelectedCharacter || !panel.HasConfirmedSkin)
                return;
        }

        if (!allSelectedFired)
        {
            allSelectedFired = true;
            Debug.Log("All joined players have selected and confirmed skin.");
            OnAllPlayersSelected?.Invoke();
        }
    }

    public void TryFireFinalConfirmation()
    {
        if (allConfirmedFired) return;

        if (joinedPlayers.Count < expectedPlayers) return;

        foreach (var kvp in joinedPlayers)
        {
            if (!kvp.Value.HasJoined) continue;
            var panel = kvp.Value.Panel;
            if (!panel.HasSelectedCharacter || !panel.HasConfirmedSkin)
                return;
        }

        allConfirmedFired = true;
        Debug.Log("OnAllPlayersConfirmed fired!");
        ConfirmAllSelections();
        OnAllPlayersConfirmed?.Invoke();
    }
}

public struct PlayerStatus
{
    public bool HasJoined;
    public bool HasSelectedCharacter;
    public bool HasConfirmedSkin;
    public bool IsReady;
    public CharacterSelectorPanel Panel;
}