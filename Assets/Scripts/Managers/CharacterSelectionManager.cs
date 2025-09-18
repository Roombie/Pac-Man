using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

public class CharacterSelectionManager : MonoBehaviour
{
    public static CharacterSelectionManager Instance { get; private set; }

    [Header("Panels")]
    public PanelUIRenderer[] panelRenderers;
    public PanelInputHandler[] panelInputs;

    [Header("Characters")]
    public CharacterData[] allCharacters;

    [Header("Events")]
    public UnityEvent OnAllPlayersSelected;
    public UnityEvent OnAllPlayersConfirmed;
    public UnityEvent OnAnyPlayerDeselected;
    public UnityEvent OnAnySkinDeselected;
    public UnityEvent OnPlayerCancelledBeforeSelection;

    public UnityEvent<CharacterData> OnCharacterSelected;
    public UnityEvent<CharacterSkin> OnSkinSelected;

    private readonly Dictionary<int, PanelState> states = new();

    private int readyCount = 0;
    private bool allSelectedFired = false;
    private bool allConfirmedFired = false;

    public bool IsSinglePlayer
    {
        get
        {
            int expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, -1);
            if (expectedPlayers <= 0)
                expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1);

            return expectedPlayers <= 1;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void OnEnable()
    {
        ResetPanels();
    }

    private void OnDisable()
    {
        // clear state so next OnEnable starts fresh
        foreach (var renderer in panelRenderers)
            renderer.gameObject.SetActive(false);
        
        foreach (var input in panelInputs)
        {
            if (input != null)
                input.ForceDejoin();
        }

        states.Clear();
        
        readyCount = 0;
        allSelectedFired = false;
        allConfirmedFired = false;
    }

    private void ResetPanels()
    {
        int expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, -1);
        if (expectedPlayers <= 0)
            expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1);

        expectedPlayers = Mathf.Clamp(expectedPlayers, 1, panelRenderers.Length);

        for (int i = 0; i < panelRenderers.Length; i++)
        {
            var state = states.ContainsKey(i) ? states[i] : new PanelState { PlayerIndex = i };
            state.Reset();
            states[i] = state;

            panelRenderers[i].Initialize(state, i, allCharacters, panelInputs[i]);
            panelInputs[i].Initialize(i);

            panelRenderers[i].gameObject.SetActive(i < expectedPlayers);
        }

        readyCount = 0;
        allSelectedFired = false;
        allConfirmedFired = false;
    }

    private void Start()
    {
        // detect expected players from PlayerPrefs
        int expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, -1);
        if (expectedPlayers <= 0)
            expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.GameModeKey, 1);

        expectedPlayers = Mathf.Clamp(expectedPlayers, 1, panelRenderers.Length);

        for (int i = 0; i < panelRenderers.Length; i++)
        {
            var state = new PanelState { PlayerIndex = i };
            states[i] = state;

            panelRenderers[i].Initialize(state, i, allCharacters, panelInputs[i]);
            panelInputs[i].Initialize(i);
            SubscribeToInput(panelInputs[i]);

            panelRenderers[i].gameObject.SetActive(i < expectedPlayers);
        }
    }

    private void SubscribeToInput(PanelInputHandler input)
    {
        input.OnSubmit += HandleSubmit;
        input.OnCancel += HandleCancel;
        input.OnMove += HandleMove;
        input.OnDejoin += HandleDejoin;
    }

    private bool TryGetValidState(int index, out PanelState state)
    {
        state = null;

        if (!states.TryGetValue(index, out var s))
        {
            Debug.LogWarning($"[CharacterSelectionManager] Input ignored: index {index} has no state.");
            return false;
        }

        if (!panelInputs[index].HasJoined)
        {
            Debug.Log($"[CharacterSelectionManager] Input ignored: player {index} not joined.");
            return false;
        }

        state = s;
        return true;
    }

    private void HandleSubmit(int index)
    {
        if (!TryGetValidState(index, out var state)) return;

        if (!state.HasSelectedCharacter)
        {
            state.HasSelectedCharacter = true;
            panelRenderers[index].ShowSkinOptions(true);
            OnCharacterSelected?.Invoke(allCharacters[state.CharacterIndex]);
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        }
        else if (!state.HasConfirmedSkin)
        {
            state.HasConfirmedSkin = true;
            panelRenderers[index].UpdateSkinIndicators();
            OnSkinSelected?.Invoke(allCharacters[state.CharacterIndex].skins[state.SkinIndex]);
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        }
        else if (!state.HasConfirmedFinal)
        {
            state.HasConfirmedFinal = true;
            readyCount++;
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        }

        CheckAllPlayersSelected();
        TryFireFinalConfirmation();
    }

    private void HandleCancel(int index)
    {
        if (!TryGetValidState(index, out var state)) return;

        if (!state.HasSelectedCharacter)
        {
            OnPlayerCancelledBeforeSelection?.Invoke();
        }
        else if (state.HasConfirmedSkin)
        {
            state.HasConfirmedSkin = false;
            readyCount = Mathf.Max(readyCount - 1, 0);
            panelRenderers[index].UpdateSkinIndicators();
            OnAnySkinDeselected?.Invoke();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        }
        else
        {
            state.HasSelectedCharacter = false;
            readyCount = Mathf.Max(readyCount - 1, 0);
            panelRenderers[index].ShowSkinOptions(false);
            OnAnyPlayerDeselected?.Invoke();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        }

        allSelectedFired = false;
        allConfirmedFired = false;
    }

    private void HandleMove(int index, int dir)
    {
        if (!TryGetValidState(index, out var state)) return;

        if (!state.HasSelectedCharacter)
        {
            state.CharacterIndex = (state.CharacterIndex + dir + allCharacters.Length) % allCharacters.Length;
            state.SkinIndex = 0; // Reset skin index to the first one when changing character
            panelRenderers[index].UpdateDisplay();
            panelRenderers[index].SetupSkins();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        }
        else if (!state.HasConfirmedSkin)
        {
            var character = allCharacters[state.CharacterIndex];
            state.SkinIndex = (state.SkinIndex + dir + character.skins.Length) % character.skins.Length;
            panelRenderers[index].UpdateDisplay();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        }
    }

    private void HandleDejoin(int index)
    {
        if (!states.ContainsKey(index)) return;

        var state = states[index];
        state.Reset();
        states[index] = state;

        readyCount = Mathf.Max(readyCount - 1, 0);

        panelRenderers[index].ShowJoinPrompt();
        allSelectedFired = false;
        allConfirmedFired = false;

        // finalize input state here
        panelInputs[index].ConfirmDejoin();

        AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        Debug.Log($"[CharacterSelectionManager] Player {index + 1} dejoined -> ShowJoinPrompt()");
    }

    // ---------------- Events and persistence ----------------
    private void CheckAllPlayersSelected()
    {
        // Only check joined players
        foreach (var kv in states)
        {
            var s = kv.Value;
            if (!panelInputs[s.PlayerIndex].HasJoined) continue;

            if (!s.HasSelectedCharacter || !s.HasConfirmedSkin)
                return;
        }

        if (HasDuplicateSelections()) return;

        if (!allSelectedFired)
        {
            allSelectedFired = true;
            OnAllPlayersSelected?.Invoke();
        }
    }

    private void TryFireFinalConfirmation()
    {
        foreach (var kv in states)
        {
            var s = kv.Value;
            if (!panelInputs[s.PlayerIndex].HasJoined) continue;

            if (!s.HasConfirmedFinal) return;
        }

        if (!allConfirmedFired)
        {
            allConfirmedFired = true;
            ConfirmAllSelections();
            OnAllPlayersConfirmed?.Invoke();
        }
    }

    private void ConfirmAllSelections()
    {
        var joinedStates = states.Values.Where(s => panelInputs[s.PlayerIndex].HasJoined).ToList();
        int count = joinedStates.Count;
        int previousCount = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 0);

        PlayerPrefs.SetInt(SettingsKeys.PlayerCountKey, count);
        PlayerPrefs.SetInt(SettingsKeys.GameModeKey, count);

        int slot = 1;
        foreach (var s in joinedStates.OrderBy(s => s.PlayerIndex))
        {
            var character = allCharacters[s.CharacterIndex];
            var skin = character.skins[s.SkinIndex];

            PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Name", character.characterName);
            PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Skin", skin.skinName);

            var sig = panelRenderers[s.PlayerIndex].GetInputSignature();
            var scheme = string.IsNullOrEmpty(sig.scheme) ? "P1Keyboard" : sig.scheme;
            var csv = (sig.deviceIds != null && sig.deviceIds.Length > 0) ? string.Join(",", sig.deviceIds) : "";

            PlayerPrefs.SetString($"P{slot}_Scheme", scheme);
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

    private bool HasDuplicateSelections()
    {
        var taken = new HashSet<string>();
        foreach (var kv in states)
        {
            var s = kv.Value;
            if (!panelInputs[s.PlayerIndex].HasJoined) continue;

            var c = allCharacters[s.CharacterIndex];
            var skin = c.skins[s.SkinIndex];
            string key = $"{c.characterName}:{skin.skinName}";

            if (taken.Contains(key)) return true;
            taken.Add(key);
        }
        return false;
    }

    public void NotifyPanelJoined(int index)
    {
        if (index >= 0 && index < panelRenderers.Length)
        {
            panelRenderers[index].ShowJoinedState();
        }
    }
}