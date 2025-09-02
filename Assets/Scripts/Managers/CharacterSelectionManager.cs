using System.Collections.Generic;
using System.Linq;
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

    [Header("Keyboard Schemes")]
    [Tooltip("Must match control scheme / binding group names in your Input Actions asset.")]
    [SerializeField] private string[] keyboardSchemesOrder = { "P1Keyboard", "P2Keyboard" };

    [Tooltip("Used for 1P selection so P1 can use both clusters.")]
    [SerializeField] private string[] bothKeyboardGroups = { "P1Keyboard", "P2Keyboard" };

    private readonly HashSet<string> claimedKeyboardSchemes = new();
    private readonly HashSet<int> claimedGamepadDeviceIds = new();

    private int expectedPlayers = 1;
    private int readyCount = 0;
    private bool allSelectedFired = false;
    private bool allConfirmedFired = false;

    private Dictionary<int, PlayerStatus> joinedPlayers = new();

    public bool IsSinglePlayer => expectedPlayers <= 1;
    public string[] BothKeyboardGroups => bothKeyboardGroups;

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
        int count = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, -1);
        if (count <= 0) count = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1);
        expectedPlayers = Mathf.Clamp(count, 1, playerPanels.Length);
        Debug.Log($"[CharacterSelectionManager] GameMode={expectedPlayers}P");

        foreach (var panel in playerPanels)
        {
            panel.ResetPanelState();
            panel.gameObject.SetActive(false);
            panel.SetPanelActive(false);
        }

        joinedPlayers.Clear();
        readyCount = 0;
        allSelectedFired = false;
        allConfirmedFired = false;

        claimedKeyboardSchemes.Clear();
        claimedGamepadDeviceIds.Clear();

        for (int i = 0; i < expectedPlayers; i++)
        {
            SetupPanelForPlayer(i);
            if (!joinedPlayers.ContainsKey(i))
            {
                var panel = playerPanels[i];
                RegisterPlayer(i, panel);
                panel.SetPanelActive(true);
            }
        }

        RefreshReadinessFromPanelState();
        CheckAllPlayersSelected();
        TryFireFinalConfirmation();
    }

    private void RefreshReadinessFromPanelState()
    {
        readyCount = 0;

        var keys = new List<int>(joinedPlayers.Keys);
        foreach (int index in keys)
        {
            var panel = joinedPlayers[index].Panel;

            var status = joinedPlayers[index];
            status.HasSelectedCharacter = panel.HasSelectedCharacter;
            status.HasConfirmedSkin     = panel.HasConfirmedSkin;
            status.IsReady              = status.HasSelectedCharacter && status.HasConfirmedSkin;
            joinedPlayers[index] = status;

            if (status.IsReady) readyCount++;
        }

        Debug.Log($"[CharacterSelectionManager] Refreshed readiness. ReadyCount: {readyCount}/{expectedPlayers}");
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
            Debug.LogWarning($"[CharacterSelectionManager] No panel for player index {index}.");
            return;
        }

        SetupPanelForPlayer(index);
        input.transform.SetParent(playerPanels[index].transform, false);
        RegisterPlayer(index, playerPanels[index]);
    }

    public void OnPlayerJoinedManual(int index, CharacterSelectorPanel panel)
    {
        if (index >= playerPanels.Length) return;
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

        if (globalPlayerStyles != null && index < globalPlayerStyles.Length)
            panel.ApplyGlobalPlayerStyle(globalPlayerStyles[index]);

        panel.SetPanelActive(joinedPlayers.ContainsKey(index) && joinedPlayers[index].HasJoined);
    }

    public void ConfirmAllSelections()
    {
        var playerIndices = joinedPlayers.Keys.OrderBy(k => k).ToArray();
        int count = playerIndices.Length;

        int previousCount = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 0);

        PlayerPrefs.SetInt(SettingsKeys.PlayerCountKey, count);
        PlayerPrefs.SetInt(SettingsKeys.GameModeKey, count);

        int slot = 1;
        foreach (var idx in playerIndices)
        {
            var panel = joinedPlayers[idx].Panel;

            var character = panel.SelectedCharacter;
            var skin = panel.SelectedSkin;
            if (character != null) PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Name", character.characterName);
            if (skin != null)      PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Skin",  skin.skinName);

            var sig    = panel.GetInputSignature();
            var scheme = string.IsNullOrEmpty(sig.scheme) ? "P1Keyboard" : sig.scheme;
            var csv    = (sig.deviceIds != null && sig.deviceIds.Length > 0) ? string.Join(",", sig.deviceIds) : "";

            PlayerPrefs.SetString($"P{slot}_Scheme",  scheme);
            PlayerPrefs.SetString($"P{slot}_Devices", csv);

            slot++;
        }

        for (int stale = count + 1; stale <= previousCount; stale++)
        {
            PlayerPrefs.DeleteKey($"P{stale}_Scheme");
            PlayerPrefs.DeleteKey($"P{stale}_Devices");
            PlayerPrefs.DeleteKey($"SelectedCharacter_Player{stale}_Name");
            PlayerPrefs.DeleteKey($"SelectedCharacter_Player{stale}_Skin");
        }

        PlayerPrefs.Save();
        Debug.Log($"All selections saved. Players={count} (previous={previousCount})");
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
        foreach (var kvp in joinedPlayers)
        {
            if (kvp.Value.Panel == panel)
            {
                joinedPlayers[kvp.Key] = new PlayerStatus
                {
                    HasJoined = true,
                    IsReady = true,
                    HasSelectedCharacter = panel.HasSelectedCharacter,
                    HasConfirmedSkin = panel.HasConfirmedSkin,
                    Panel = panel
                };
                break;
            }
        }
        TryFireFinalConfirmation();
    }

    public void NotifyPlayerDeselected(CharacterSelectorPanel panel)
    {
        OnAnyPlayerDeselected?.Invoke();

        foreach (var kvp in joinedPlayers)
        {
            if (kvp.Value.Panel == panel)
            {
                var status = kvp.Value;
                status.HasSelectedCharacter = false;
                status.HasConfirmedSkin = false;
                status.IsReady = false;
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
        ConfirmAllSelections();
        OnAllPlayersConfirmed?.Invoke();
    }

    public string GetKeyboardSchemeForIndex(int index)
    {
        if (keyboardSchemesOrder != null && index >= 0 && index < keyboardSchemesOrder.Length)
            return keyboardSchemesOrder[index];
        return (keyboardSchemesOrder != null && keyboardSchemesOrder.Length > 0) ? keyboardSchemesOrder[0] : null;
    }

    public string ReserveNextKeyboardScheme(bool allowDuplicateIfExhausted = true)
    {
        foreach (var s in keyboardSchemesOrder)
            if (!claimedKeyboardSchemes.Contains(s))
            {
                claimedKeyboardSchemes.Add(s);
                return s;
            }

        return allowDuplicateIfExhausted && keyboardSchemesOrder.Length > 0 ? keyboardSchemesOrder[0] : null;
    }

    public void ReleaseKeyboardScheme(string scheme)
    {
        if (!string.IsNullOrEmpty(scheme))
            claimedKeyboardSchemes.Remove(scheme);
    }

    public bool TryReserveGamepad(InputDevice device)
    {
        if (device == null) return false;
        int id = device.deviceId;
        if (claimedGamepadDeviceIds.Contains(id)) return false;
        claimedGamepadDeviceIds.Add(id);
        return true;
    }

    public void ReleaseGamepads(params int[] deviceIds)
    {
        if (deviceIds == null) return;
        foreach (var id in deviceIds) claimedGamepadDeviceIds.Remove(id);
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
