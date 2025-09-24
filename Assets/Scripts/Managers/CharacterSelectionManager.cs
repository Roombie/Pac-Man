using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class CharacterSelectionManager : MonoBehaviour
{
    public static CharacterSelectionManager Instance { get; private set; }

    [Header("Characters")]
    [SerializeField] private CharacterData[] allCharacters;

    [Header("Panels")]
    [SerializeField] private PanelUIRenderer[] panelRenderers;
    [SerializeField] private PanelInputHandler[] panelInputs;

    [Header("Navigation Settings")]
    [SerializeField] private float moveDeadzone = 0.5f;
    [SerializeField] private float initialRepeatDelay = 0.35f;
    [SerializeField] private float repeatInterval = 0.12f;
    [SerializeField] private bool allowHoldRepeat = true;

    [Header("Events")]
    public UnityEvent<CharacterData> OnCharacterSelected;
    public UnityEvent<CharacterSkin> OnSkinSelected;
    public UnityEvent OnAnyPlayerDeselected;
    public UnityEvent OnAnySkinDeselected;
    public UnityEvent OnAllPlayersSelected;   // fired when all expected players are joined + skin-confirmed
    public UnityEvent OnAllPlayersConfirmed;  // fired by one extra Submit from ANY player after the above
    public UnityEvent OnPlayerCancelledBeforeSelection;

    private PanelState[] states;
    private readonly Dictionary<int, PanelState> joinedPlayers = new();

    // Expected players comes from your preceding menu via PlayerPrefs
    private int expectedPlayers = 1;

    private bool allSelectedFired;
    private bool allConfirmedFired;

    public bool IsSinglePlayer => expectedPlayers == 1;

    private void Awake()
    {
        Instance = this;
        expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1);
    }

    private void OnEnable() => ResetPanels();

    private void OnDisable()
    {
        if (panelInputs == null) return;
        foreach (var input in panelInputs)
        {
            if (input != null) UnsubscribeFromInput(input);
        }
    }

    private void Start() => ResetPanels();

    /// <summary>
    /// Initialize only the active panels (0..expectedPlayers-1) and subscribe inputs for them.
    /// Extra panels are left unsubscribed and not initialized to avoid interfering with state.
    /// </summary>
    private void ResetPanels()
    {
        joinedPlayers.Clear();
        allSelectedFired = false;
        allConfirmedFired = false;

        int count = Mathf.Min(panelRenderers.Length, panelInputs.Length);
        states = new PanelState[count];

        for (int i = 0; i < count; i++)
        {
            states[i] = new PanelState();

            bool isActive = i < expectedPlayers;

            // Always set renderer active state according to expected players
            if (panelRenderers[i] != null)
                panelRenderers[i].gameObject.SetActive(isActive);

            // Unsubscribe defensively (in case ResetPanels is called more than once)
            if (panelInputs[i] != null)
                UnsubscribeFromInput(panelInputs[i]);

            if (isActive)
            {
                // Initialize renderer & input ONLY for active panels
                if (panelRenderers[i] != null)
                {
                     panelInputs[i].ResetJoinCompletely(true);
                    panelRenderers[i].Initialize(states[i], i, allCharacters, panelInputs[i]);
                    panelRenderers[i].ShowJoinPrompt();
                }
                if (panelInputs[i] != null)
                {
                    panelInputs[i].Initialize(i, moveDeadzone, initialRepeatDelay, repeatInterval, allowHoldRepeat);
                    SubscribeToInput(panelInputs[i]);
                }
            }
            else
            {
                // For non-active panels, also clean state so they don't hold reservations
                if (panelInputs[i] != null)
                    panelInputs[i].ResetJoinCompletely(true);
            }
        }
    }

    private void SubscribeToInput(PanelInputHandler input)
    {
        input.OnSubmit += HandleSubmit;
        input.OnCancel += HandleCancel;
        input.OnMove += HandleMove;
        input.OnDejoin += HandleDejoin;
    }

    private void UnsubscribeFromInput(PanelInputHandler input)
    {
        input.OnSubmit -= HandleSubmit;
        input.OnCancel -= HandleCancel;
        input.OnMove -= HandleMove;
        input.OnDejoin -= HandleDejoin;
    }

    #region Input Handlers

    private void HandleSubmit(int index)
    {
        if (!TryGetValidState(index, out var state)) return;

        // If not joined yet, claim this panel and show joined UI
        if (!panelInputs[index].HasJoined)
        {
            panelInputs[index].TryClaimFromContext(default);
            panelRenderers[index].ShowJoinedState();
            NotifyPanelJoined(index);
            return;
        }

        // Selection flow: Character -> Skin -> Final Confirmation (extra Submit)
        if (!state.HasSelectedCharacter)
        {
            SelectCharacter(index, state);
        }
        else if (!state.HasConfirmedSkin)
        {
            ConfirmSkin(index, state);
        }
        else if (allSelectedFired && !allConfirmedFired)
        {
            // One extra Submit from ANY player after all selected → finalize
            FinalConfirmation();
        }

        CheckAllPlayersSelected();
    }

    private void HandleCancel(int index)
    {
        if (!TryGetValidState(index, out var state)) return;

        if (!state.HasSelectedCharacter)
        {
            // User cancelled before selecting a character
            OnPlayerCancelledBeforeSelection?.Invoke();
        }
        else if (state.HasConfirmedSkin)
        {
            // Back from skin confirmed → back to skin selection (unconfirm)
            state.HasConfirmedSkin = false;

            panelRenderers[index].ShowSkinOptions(true);
            panelRenderers[index].UpdateSkinConfirmationIndicator(state.SkinIndex, false);
            panelRenderers[index].UpdateSkinHighlight(state.SkinIndex);

            OnAnySkinDeselected?.Invoke();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);

            // Allow recomputation of "all selected"
            allConfirmedFired = false;
            allSelectedFired = false;
        }
        else
        {
            // Back from skin selection → back to character selection
            state.HasSelectedCharacter = false;

            panelRenderers[index].ShowSkinOptions(false);
            OnAnyPlayerDeselected?.Invoke();
            AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);

            // Allow recomputation of "all selected"
            allConfirmedFired = false;
            allSelectedFired = false;
        }

        panelInputs[index].ResetMoveRepeat();
        CheckAllPlayersSelected();
    }

    private void HandleMove(int index, int dir)
    {
        if (!TryGetValidState(index, out var state)) return;

        // Extra safety: if a move sneaks in before join, claim and show joined UI
        if (!panelInputs[index].HasJoined)
        {
            panelInputs[index].TryClaimFromContext(default);
            panelRenderers[index].ShowJoinedState();
            NotifyPanelJoined(index);
            // Continue to handle the move below if you want immediate navigation after join
        }

        if (!state.HasSelectedCharacter)
        {
            // Navigate characters
            ChangeCharacter(index, state, dir);
        }
        else if (!state.HasConfirmedSkin)
        {
            // Navigate skins
            ChangeSkin(index, state, dir);
        }

        CheckAllPlayersSelected();
    }

    private void HandleDejoin(int index)
    {
        if (!joinedPlayers.ContainsKey(index)) return;

        joinedPlayers.Remove(index);
        states[index].Reset();
        panelRenderers[index].ShowJoinPrompt();
        panelInputs[index].ConfirmDejoin();

        // Reset global flags so the group must re-confirm
        allSelectedFired = false;
        allConfirmedFired = false;

        Debug.Log($"[CharacterSelectionManager] Player {index} dejoined");
    }

    #endregion

    #region Actions

    private void SelectCharacter(int index, PanelState state)
    {
        state.HasSelectedCharacter = true;
        panelRenderers[index].ShowSkinOptions(true);

        OnCharacterSelected?.Invoke(allCharacters[state.CharacterIndex]);

        panelInputs[index].ResetMoveRepeat();
        AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
    }

    private void ConfirmSkin(int index, PanelState state)
    {
        state.HasConfirmedSkin = true;
        var skin = allCharacters[state.CharacterIndex].skins[state.SkinIndex];

        OnSkinSelected?.Invoke(skin);
        panelRenderers[index].UpdateSkinConfirmationIndicator(state.SkinIndex, true);

        panelInputs[index].ResetMoveRepeat();
        AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
    }

    private void ChangeCharacter(int index, PanelState state, int dir)
    {
        state.CharacterIndex = (state.CharacterIndex + dir + allCharacters.Length) % allCharacters.Length;
        state.SkinIndex = 0;

        panelRenderers[index].UpdateCharacterAndSkins(allCharacters[state.CharacterIndex], state.SkinIndex);
        OnCharacterSelected?.Invoke(allCharacters[state.CharacterIndex]);
    }

    private void ChangeSkin(int index, PanelState state, int dir)
    {
        var skins = allCharacters[state.CharacterIndex].skins;
        state.SkinIndex = (state.SkinIndex + dir + skins.Length) % skins.Length;

        panelRenderers[index].UpdateSkinOnly(allCharacters[state.CharacterIndex], state.SkinIndex);
        OnSkinSelected?.Invoke(skins[state.SkinIndex]);
    }

    private void FinalConfirmation()
    {
        OnAllPlayersConfirmed?.Invoke();
        allConfirmedFired = true;
        Debug.Log("[CharacterSelectionManager] Final confirmation triggered.");
    }

    #endregion

    #region Helpers

    private bool TryGetValidState(int index, out PanelState state)
    {
        state = (index >= 0 && index < states.Length) ? states[index] : null;
        return state != null;
    }

    /// <summary>
    /// Multiplayer rule: exactly "expectedPlayers" must be joined AND confirmed.
    /// Singleplayer rule: at least 1 joined AND confirmed.
    /// Only panels [0..expectedPlayers-1] are considered.
    /// </summary>
    private void CheckAllPlayersSelected()
    {
        int joined = 0, confirmed = 0;

        int limit = Mathf.Min(expectedPlayers, panelInputs.Length);
        for (int i = 0; i < limit; i++)
        {
            var input = panelInputs[i];
            if (input == null || !input.HasJoined) continue;

            joined++;
            if (states[i].HasConfirmedSkin) confirmed++;
        }

        bool allReady = IsSinglePlayer
            ? (joined >= 1 && confirmed >= 1)
            : (joined == expectedPlayers && confirmed == expectedPlayers);

        if (allReady && !allSelectedFired)
        {
            OnAllPlayersSelected?.Invoke();
            allSelectedFired = true;
            Debug.Log("[CharacterSelectionManager] All players selected (expected-players rule).");
        }
        else if (!allReady)
        {
            allSelectedFired = false;
            allConfirmedFired = false;
        }
    }

    public void NotifyPanelJoined(int index)
    {
        if (!joinedPlayers.ContainsKey(index))
            joinedPlayers[index] = states[index];

        panelRenderers[index].ShowJoinedState();
        OnCharacterSelected?.Invoke(allCharacters[states[index].CharacterIndex]);
    }

    #endregion
}