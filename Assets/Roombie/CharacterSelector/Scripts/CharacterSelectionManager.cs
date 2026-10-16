using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Roombie.CharacterSelect;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using TMPro;

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

    [Header("Persistence (optional)")]
    [Tooltip("If assigned, selections are saved through this asset; otherwise PlayerPrefs is used.")]
    [SerializeField] private SelectionPersistenceAsset persistence;

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

    // Touch (1P only): tap = join/confirm, swipe left/right = change, swipe down = back
    private readonly SwipeDetector swipe = new();
    private int panelsResetFrame; // the tap that opened this menu must not also join

    public bool IsSinglePlayer => expectedPlayers == 1;

    private void Awake()
    {
        Instance = this;
        expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1);
    }

    private void OnEnable()
    {
        ResetPanels();
    }

    private void OnDisable()
    {
        if (panelInputs == null) return;
        foreach (var input in panelInputs)
        {
            if (input != null) UnsubscribeFromInput(input);
        }
    }

    private void Start()
    {
        ResetPanels();
        if (TouchInput.IsMobile) SetupMobileControls();
    }

    private void Update()
    {
        if (!IsSinglePlayer || Time.frameCount <= panelsResetFrame) return;
        if (Touchscreen.current == null || panelInputs.Length == 0 || panelInputs[0] == null) return;

        var panel = panelInputs[0];

        // A phone has a single touch player, so there's nobody to wait for: P1 joins on arrival
        if (TouchInput.IsMobile && !panel.HasJoined)
        {
            panel.ClaimTouch(Touchscreen.current);
            return;
        }

        if (TouchInput.IsMobile) RefreshMobileHints();

        if (TouchInput.TappedThisFrame())
        {
            if (!panel.HasJoined) panel.ClaimTouch(Touchscreen.current);
            else HandleSubmit(0);
            return;
        }

        if (!panel.HasJoined || !swipe.TryGetSwipe(out var direction)) return;

        if (direction.x != 0f) HandleMove(0, (int)direction.x);
        else if (direction.y < 0f) HandleCancel(0);
    }

    /// <summary>
    /// Back from the on-screen button (mobile): steps back through skin -> character -> previous menu
    /// </summary>
    public void TouchBack()
    {
        if (panelInputs.Length > 0 && panelInputs[0] != null && panelInputs[0].HasJoined) HandleCancel(0);
        else OnPlayerCancelledBeforeSelection?.Invoke();
    }

    /// <summary>
    /// Mobile only, for P1:
    ///   - the character arrows become tap targets (bigger than they look); the keyboard key-cap icons go
    ///   - tapping a skin picks it, tapping it again confirms it; tapping the confirmed (yellow) skin
    ///     un-confirms it and tapping another one switches to it
    ///   - tapping the character portrait goes back to choosing the character
    ///   - hint lines under the panels say what a tap does at each step
    /// </summary>
    private void SetupMobileControls()
    {
        if (panelRenderers.Length == 0 || panelRenderers[0] == null) return;
        var panel = panelRenderers[0].transform;

        MakeTouchArrow(FindDeep(panel, "Left Arrow Image"), -1);
        MakeTouchArrow(FindDeep(panel, "Right Arrow Image"), 1);

        foreach (var icon in new[] { "Left Input Icon Image", "Right Input Icon Image" })
        {
            var t = FindDeep(panel, icon);
            if (t != null) t.gameObject.SetActive(false);
        }

        panelRenderers[0].OnSkinIconTapped += OnSkinIconTapped;
        panelRenderers[0].OnPortraitTapped += OnPortraitTapped;

        // The hints copy the menu's "Ready?" text so they keep the arcade font and colour
        var menu = transform.parent;
        var readyText = menu != null ? FindDeep(menu, "Ready? Text (TMP)") : null;
        if (readyText != null)
        {
            mainHint = CreateHint(readyText, "Tap Hint Text (TMP)", MobileHintY);
            mainHintKey = "Mobile_TapToConfirm";
            LocalizedTextKey.Set(mainHint, mainHintKey);
            changeSkinHint = CreateHint(readyText, "Change Skin Hint Text (TMP)", MobileHintY - MobileHintLineSpacing);
            changeSkinHint.GetComponent<TMP_Text>().color = ArcadeWhite;
            LocalizedTextKey.Set(changeSkinHint, "Mobile_TapSkinToChange");
            changeSkinHint.SetActive(false);
        }
    }

    private GameObject CreateHint(Transform source, string name, float y)
    {
        var hint = Instantiate(source.gameObject, source.parent);
        hint.name = name;

        var rect = (RectTransform)hint.transform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        rect.sizeDelta = new Vector2(MobileHintWidth, rect.sizeDelta.y);

        // One line that shrinks to fit, whatever the language
        var text = hint.GetComponent<TMP_Text>();
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = text.fontSize * 0.6f;
        text.enableAutoSizing = true;

        hint.SetActive(true);
        return hint;
    }

    private void RefreshMobileHints()
    {
        if (mainHint == null || states.Length == 0) return;

        bool confirmed = states[0].HasConfirmedSkin;
        string key = confirmed ? "Mobile_TapToPlay" : "Mobile_TapToConfirm";
        if (key != mainHintKey)
        {
            mainHintKey = key;
            LocalizedTextKey.Set(mainHint, key);
        }

        if (changeSkinHint.activeSelf != confirmed) changeSkinHint.SetActive(confirmed);
    }

    private void OnSkinIconTapped(int skinIndex)
    {
        if (!TryGetValidState(0, out var state) || !state.HasSelectedCharacter) return;

        if (state.HasConfirmedSkin)
        {
            HandleCancel(0); // un-confirm
            if (skinIndex != state.SkinIndex) SelectSkin(state, skinIndex);
        }
        else if (skinIndex == state.SkinIndex) HandleSubmit(0); // second tap confirms
        else SelectSkin(state, skinIndex);
    }

    private void OnPortraitTapped()
    {
        if (!TryGetValidState(0, out var state)) return;

        if (!state.HasSelectedCharacter)
        {
            HandleSubmit(0); // same as tapping anywhere: pick this character
            return;
        }

        // Back to choosing the character, from either skin step
        if (state.HasConfirmedSkin) HandleCancel(0);
        HandleCancel(0);
    }

    private void SelectSkin(PanelState state, int skinIndex) => ChangeSkin(0, state, skinIndex - state.SkinIndex);

    private GameObject mainHint;
    private GameObject changeSkinHint;
    private string mainHintKey;

    private const float MobileHintY = -150f;          // between the panels and the cherry at the bottom
    private const float MobileHintLineSpacing = 22f;
    private const float MobileHintWidth = 420f;       // the portrait canvas is 450 wide
    private static readonly Color ArcadeWhite = new(0.87f, 0.87f, 1f);
    private const float ArrowTouchPadding = 18f; // canvas units added around each arrow for fingers

    private void MakeTouchArrow(Transform arrow, int direction)
    {
        if (arrow == null) return;

        var image = arrow.GetComponent<Image>();
        if (image != null)
        {
            image.raycastTarget = true;
            image.raycastPadding = -Vector4.one * ArrowTouchPadding; // negative padding grows the hit area
        }

        // A Button so taps on it count as UI (TouchInput ignores them) instead of as "confirm"
        var button = arrow.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => HandleMove(0, direction));
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    /// <summary>
    /// Initialize only the active panels (0..expectedPlayers-1) and subscribe inputs for them.
    /// Extra panels are left unsubscribed and not initialized to avoid interfering with state.
    /// </summary>
    private void ResetPanels()
    {
        Instance = this;
        panelsResetFrame = Time.frameCount;
        expectedPlayers = PlayerPrefs.GetInt(SettingsKeys.PlayerCountKey, 1);
        InputUser.listenForUnpairedDeviceActivity = 1; // 1 = true, 0 = false

        PlayerDeviceManager.Instance?.ResetAllReservations();

        joinedPlayers.Clear();
        allSelectedFired = false;
        allConfirmedFired = false;

        int count = Mathf.Min(panelRenderers.Length, panelInputs.Length);
        states = new PanelState[count];

        for (int i = 0; i < count; i++)
        {
            states[i] = new PanelState();

            bool isActive = i < expectedPlayers;

            if (panelRenderers[i] != null)
                panelRenderers[i].gameObject.SetActive(isActive);

            if (panelInputs[i] != null)
                UnsubscribeFromInput(panelInputs[i]);

            if (isActive)
            {
                if (panelInputs[i] != null)
                {
                    panelInputs[i].ResetJoinCompletely(true);
                    panelInputs[i].Initialize(i, moveDeadzone, initialRepeatDelay, repeatInterval, allowHoldRepeat);
                    SubscribeToInput(panelInputs[i]);
                }
                if (panelRenderers[i] != null)
                {
                    panelRenderers[i].Initialize(states[i], i, allCharacters, panelInputs[i]);
                    panelRenderers[i].ShowJoinPrompt();
                }
            }
            else
            {
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
            // One extra Submit from ANY player after the above → finalize
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
            state.Phase = PanelJoinState.CharacterSelected;

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
            state.Phase = PanelJoinState.Joined;

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

        AudioManager.Instance.Play(AudioManager.Instance.creditReverse);

        // Snapshot previous phase to emit the right “deselected” event
        var prevPhase = states[index].Phase;
        bool hadSelected = states[index].HasSelectedCharacter;
        bool hadConfirmed = states[index].HasConfirmedSkin;

        // Forget this player
        joinedPlayers.Remove(index);

        // Clear persisted selection if any
        ClearSelectionForPlayer(index);

        // Reset panel state & UI
        states[index].Reset();
        panelRenderers[index].ShowSkinOptions(false);
        panelRenderers[index].ShowJoinPrompt();

        // Tell input to actually dejoin (releases reservations, requires fresh press)
        panelInputs[index].ConfirmDejoin();

        // Emit deselect events in correct order
        if (hadConfirmed)
            OnAnySkinDeselected?.Invoke();     // was at SkinConfirmed -> now none
        else if (hadSelected)
            OnAnyPlayerDeselected?.Invoke();   // was at CharacterSelected -> now none

        // Invalidate group-level “all ready / confirmed” flags
        allSelectedFired = false;
        allConfirmedFired = false;

        // Recompute readiness (will be false now)
        CheckAllPlayersSelected();

        Debug.Log($"[CharacterSelectionManager] Player {index} dejoined (prevPhase={prevPhase}).");
    }

    private void PersistInputChoicesForGameplay()
    {
        // expectedPlayers is already set; slots are 1-based in gameplay prefs
        for (int i = 0; i < expectedPlayers && i < panelInputs.Length; i++)
        {
            var input = panelInputs[i];
            if (input == null || !input.HasJoined) continue;

            var (scheme, deviceIds) = input.GetInputSignature();   // from PanelInputHandler
            int slot = i + 1;

            // Normalize nulls
            scheme ??= string.Empty;
            var ids = (deviceIds != null && deviceIds.Length > 0)
                    ? string.Join(",", deviceIds)
                    : string.Empty;

            PlayerPrefs.SetString($"P{slot}_Scheme", scheme);
            PlayerPrefs.SetString($"P{slot}_Devices", ids);
        }

        // If you want to be tidy, clear unused slots up to 2 players (or more if you support more)
        for (int i = expectedPlayers; i < 2; i++)
        {
            int slot = i + 1;
            PlayerPrefs.DeleteKey($"P{slot}_Scheme");
            PlayerPrefs.DeleteKey($"P{slot}_Devices");
        }

        PlayerPrefs.Save();
    }

    #endregion

    #region Actions

    private void SelectCharacter(int index, PanelState state)
    {
        state.HasSelectedCharacter = true;
        state.Phase = PanelJoinState.CharacterSelected;
        panelRenderers[index].ShowSkinOptions(true);

        OnCharacterSelected?.Invoke(allCharacters[state.CharacterIndex]);

        panelInputs[index].ResetMoveRepeat();
        AudioManager.Instance?.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
    }

    private void ConfirmSkin(int index, PanelState state)
    {
        state.HasConfirmedSkin = true;
        state.Phase = PanelJoinState.SkinConfirmed;
        var skin = allCharacters[state.CharacterIndex].skins[state.SkinIndex];

        // Persist selection for this player
        SaveSelectionForPlayer(index);

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
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        OnCharacterSelected?.Invoke(allCharacters[state.CharacterIndex]);
    }

    private void ChangeSkin(int index, PanelState state, int dir)
    {
        var skins = allCharacters[state.CharacterIndex].skins;
        state.SkinIndex = (state.SkinIndex + dir + skins.Length) % skins.Length;

        panelRenderers[index].UpdateSkinOnly(allCharacters[state.CharacterIndex], state.SkinIndex);
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        OnSkinSelected?.Invoke(skins[state.SkinIndex]);
    }

    private void FinalConfirmation()
    {
        // Ensure all active players’ selections are saved
        int limit = Mathf.Min(expectedPlayers, panelInputs.Length);
        for (int i = 0; i < limit; i++)
            if (states[i].Phase == PanelJoinState.SkinConfirmed)
                SaveSelectionForPlayer(i);

        PersistInputChoicesForGameplay();
        // Flush persistence if needed
        FlushPersistence();

        OnAllPlayersConfirmed?.Invoke();
        allConfirmedFired = true;
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        Debug.Log("[CharacterSelectionManager] Final confirmation triggered.");
    }

    #endregion

    #region Helpers

    private bool TryGetValidState(int index, out PanelState state)
    {
        state = (index >= 0 && index < states.Length) ? states[index] : null;
        return state != null;
    }

    private bool AreAllExpectedJoinedAndConfirmed()
    {
        int limit = Mathf.Min(expectedPlayers, panelInputs.Length);
        for (int i = 0; i < limit; i++)
        {
            var input = panelInputs[i];
            if (input == null || !input.HasJoined) return false; // must be joined
            if (states[i].Phase != PanelJoinState.SkinConfirmed) return false; // must have confirmed skin
        }
        return true;
    }

    /// <summary>
    /// Multiplayer rule: exactly "expectedPlayers" must be joined AND confirmed.
    /// Singleplayer rule: at least 1 joined AND confirmed.
    /// Only panels [0..expectedPlayers-1] are considered.
    /// </summary>
    private void CheckAllPlayersSelected()
    {
        bool allReady = AreAllExpectedJoinedAndConfirmed();
        if (allReady && !allSelectedFired)
        {
            OnAllPlayersSelected?.Invoke();
            allSelectedFired = true;
            Debug.Log("[CharacterSelectionManager] All players selected (phase-based).");
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
        states[index].Phase = PanelJoinState.Joined;
        AudioManager.Instance.Play(AudioManager.Instance.credit);
        OnCharacterSelected?.Invoke(allCharacters[states[index].CharacterIndex]);
    }

    public PanelInputHandler GetPanelInput(int index) =>
        (index >= 0 && index < panelInputs.Length) ? panelInputs[index] : null;

    /// <summary>
    /// Returns the lowest-index active panel (0..expectedPlayers-1) that is visible and not yet joined.
    /// If all active panels are joined, returns -1.
    /// </summary>
    public int GetNextFreeJoinSlot()
    {
        int limit = Mathf.Min(expectedPlayers, panelInputs.Length);

        // Primary pass: panel must be visible, enabled, and not joined
        for (int i = 0; i < limit; i++)
        {
            if (panelRenderers[i] != null && panelRenderers[i].gameObject.activeInHierarchy &&
                panelInputs[i] != null && panelInputs[i].enabled &&
                !panelInputs[i].HasJoined)
            {
                return i;
            }
        }

        // Fallback (shouldn’t be needed, but avoids deadlocks if a flag desyncs)
        for (int i = 0; i < limit; i++)
        {
            if (panelRenderers[i] != null && panelRenderers[i].gameObject.activeInHierarchy &&
                panelInputs[i] != null && !panelInputs[i].HasJoined)
            {
                return i;
            }
        }

        return -1;
    }

    // ---------- Persistence helpers ----------
    private void SaveSelectionForPlayer(int index)
    {
        // index is 0-based; PlayerPrefs keys expect 1-based
        int slot = index + 1;

        var character = allCharacters[states[index].CharacterIndex];
        var skin = character.skins[states[index].SkinIndex];

        if (persistence != null)
        {
            persistence.Save(slot, character.characterName, skin.skinName);
            persistence.Flush();
        }
        else
        {
            PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Name", character.characterName);
            PlayerPrefs.SetString($"SelectedCharacter_Player{slot}_Skin", skin.skinName);
            PlayerPrefs.Save();
        }
    }

    private void ClearSelectionForPlayer(int index)
    {
        int slot = index + 1;

        if (persistence != null)
        {
            persistence.Clear(slot);
            persistence.Flush();
        }
        else
        {
            PlayerPrefs.DeleteKey($"SelectedCharacter_Player{slot}_Name");
            PlayerPrefs.DeleteKey($"SelectedCharacter_Player{slot}_Skin");
            PlayerPrefs.Save();
        }
    }

    private void FlushPersistence()
    {
        if (persistence != null)
            persistence.Flush();
        else
            PlayerPrefs.Save();
    }

    #endregion
}
