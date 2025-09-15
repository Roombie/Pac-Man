using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Localization;
using UnityEngine.InputSystem;

public class CharacterSelectorPanel : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text playerLabelText;
    public GameObject characterData;
    public Image characterImage;
    public TMP_Text characterNameText;
    public Transform skinIconContainer;
    public GameObject skinIconPrefab;
    public GameObject joinInstruction;
    public LocalizedString playerLabelLocalized;

    [Header("Visual Style References")]
    public Image backgroundImage;

    // Input actions
    private InputAction submit;
    private InputAction cancel;
    private InputAction move;
    private InputAction dejoin;
    private PlayerInput playerInput;

    [Header("Navigation Tuning")]
    [SerializeField] private float moveDeadzone = 0.5f;
    [SerializeField] private float initialRepeatDelay = 0.35f;
    [SerializeField] private float repeatInterval = 0.12f;
    private int lastMoveSign = 0;
    private float nextRepeatTime = 0f;

    // Character state
    private CharacterData[] characters;
    private int playerIndex = -1;
    private int currentIndex = 0;
    private int currentSkinIndex = 0;
    private bool hasSelectedCharacter = false;
    private bool hasConfirmedSkin = false;
    private bool hasConfirmedFinal = false;

    private readonly List<GameObject> instantiatedSkinIcons = new();
    private readonly List<GameObject> selectionIndicators = new();

    // Claim state
    private string chosenScheme;
    private int[] chosenDeviceIds = System.Array.Empty<int>();
    private bool hasClaimedInputs = false;
    private int[] reservedGamepadIds = System.Array.Empty<int>();
    private string reservedKeyboardScheme;

    public CharacterData SelectedCharacter => characters != null && characters.Length > 0 ? characters[currentIndex] : null;
    public CharacterSkin SelectedSkin => SelectedCharacter != null && SelectedCharacter.skins.Length > 0 ? SelectedCharacter.skins[currentSkinIndex] : null;
    public bool HasSelectedCharacter => hasSelectedCharacter;
    public bool HasConfirmedSkin => hasConfirmedSkin;
    public int PlayerIndex => playerIndex;
    public bool HasConfirmedFinal() => hasConfirmedFinal;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.defaultControlScheme = null;
            playerInput.neverAutoSwitchControlSchemes = true;
        }
    }

    private void OnEnable()
    {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (playerInput == null) return;

        var asset = playerInput.actions;
        asset.Disable();
        asset.bindingMask = null; // neutral
        asset.Enable();

        submit = asset["Submit"];
        cancel = asset["Cancel"];
        move = asset["Move"];
        dejoin = asset["Select"];

        submit.performed += OnSubmitPerformed;
        cancel.performed += OnCancelPerformed;
        move.performed += OnMovePerformed;
        move.canceled += OnMoveCanceled;
        dejoin.performed += OnDejoinPerformed;
    }

    private void OnDisable()
    {
        if (submit != null) submit.performed -= OnSubmitPerformed;
        if (cancel != null) cancel.performed -= OnCancelPerformed;
        if (move != null) move.performed   -= OnMovePerformed;
        if (dejoin != null) dejoin.performed -= OnDejoinPerformed;

        ReleaseReservations();
    }

    // Panel state handle
    public void SetPlayerIndex(int index)
    {
        playerIndex = index;
        ApplyPlayerLabel();
        NeutralizeInput();
    }

    private void NeutralizeInput()
    {
        hasClaimedInputs = false;
        chosenScheme = null;
        chosenDeviceIds = System.Array.Empty<int>();

        ReleaseReservations();

        if (playerInput != null && playerInput.actions != null)
        {
            playerInput.actions.Disable();
            playerInput.actions.bindingMask = null;
            playerInput.actions.Enable();
        }

        UpdateJoinInstructionState();
    }

    private void ReleaseReservations()
    {
        if (CharacterSelectionManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(reservedKeyboardScheme))
                CharacterSelectionManager.Instance.ReleaseKeyboardScheme(reservedKeyboardScheme);

            if (reservedGamepadIds.Length > 0)
                CharacterSelectionManager.Instance.ReleaseGamepads(reservedGamepadIds);
        }

        reservedKeyboardScheme = null;
        reservedGamepadIds = System.Array.Empty<int>();
    }

    private void UpdateJoinInstructionState()
    {
        if (joinInstruction) joinInstruction.SetActive(!hasClaimedInputs);
        if (characterData) characterData.SetActive(hasClaimedInputs);
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

    // Input
    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        if (TryClaimFromContext(ctx)) return;
        if (!hasClaimedInputs || !IsFromClaimedDevice(ctx)) return;
        ConfirmSelection();
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (TryClaimFromContext(ctx)) return;
        if (!hasClaimedInputs || !IsFromClaimedDevice(ctx)) return;
        CancelSelection();
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        if (TryClaimFromContext(ctx)) return;
        if (!hasClaimedInputs || !IsFromClaimedDevice(ctx)) return;

        Vector2 v = ctx.ReadValue<Vector2>();
        float x = v.x;

        if (Mathf.Abs(x) < moveDeadzone)
        {
            lastMoveSign = 0;
            return;
        }

        int sign = x > 0 ? +1 : -1;
        bool isNewTilt = (lastMoveSign == 0) || (sign != lastMoveSign);
        bool canRepeat = Time.time >= nextRepeatTime;

        if (isNewTilt || canRepeat)
        {
            nextRepeatTime = Time.time + (isNewTilt ? initialRepeatDelay : repeatInterval);
            lastMoveSign = sign;

            if (!hasSelectedCharacter)
            {
                AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
                currentIndex = (currentIndex + (sign > 0 ? 1 : -1) + characters.Length) % characters.Length;
                currentSkinIndex = 0;
                UpdateDisplay();
                InitializeSkins();
            }
            else if (!hasConfirmedSkin)
            {
                AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
                currentSkinIndex = (currentSkinIndex + (sign > 0 ? 1 : -1) + SelectedCharacter.skins.Length) % SelectedCharacter.skins.Length;
                UpdateSkinHighlight();
                UpdateDisplay();
            }
        }
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        if (!IsFromClaimedDevice(ctx)) return;
        lastMoveSign = 0;
        nextRepeatTime = 0f;
    }

    private void OnDejoinPerformed(InputAction.CallbackContext ctx)
    {
        ResetPanelState();
        Debug.Log($"Player {playerIndex + 1} dejoined, waiting for rejoin.");
    }

    // Claim logic
    private bool TryClaimFromContext(InputAction.CallbackContext ctx)
    {
        if (hasClaimedInputs) return false;

        var mgr = CharacterSelectionManager.Instance;
        var control = ctx.control;
        if (control == null) return false;

        // Gamepad
        if (control.device is Gamepad gamepad)
        {
            if (mgr.TryReserveGamepad(gamepad))
            {
                chosenScheme = "Gamepad";
                reservedGamepadIds = new[] { gamepad.deviceId };
                FinalizeClaim();
                return true;
            }
            return false;
        }

        // Keyboard
        if (control.device is Keyboard)
        {
            string groups = GetGroupsForTriggeredBinding(ctx.action, control);
            string[] known = mgr?.BothKeyboardGroups ?? new[] { "P1Keyboard", "P2Keyboard" };

            if (mgr.IsSinglePlayer)
            {
                var match = FirstMatch(groups, known);
                if (!string.IsNullOrEmpty(match) && mgr.TryReserveKeyboardScheme(match))
                {
                    chosenScheme = match;
                    reservedKeyboardScheme = match;
                    FinalizeClaim();
                    return true;
                }
            }
            else
            {
                string target = mgr.GetKeyboardSchemeForIndex(playerIndex);
                if (GroupsContain(groups, target) && mgr.TryReserveKeyboardScheme(target))
                {
                    chosenScheme = target;
                    reservedKeyboardScheme = target;
                    FinalizeClaim();
                    return true;
                }
            }
        }

        return false;
    }

    private void FinalizeClaim()
    {
        hasClaimedInputs = true;

        if (chosenScheme == "Gamepad")
        {
            chosenDeviceIds = playerInput.devices.OfType<Gamepad>().Select(d => d.deviceId).ToArray();
        }
        else
        {
            chosenDeviceIds = (Keyboard.current != null) ? new[] { Keyboard.current.deviceId } : System.Array.Empty<int>();
        }

        if (!string.IsNullOrEmpty(chosenScheme) && playerInput.actions != null)
        {
            var asset = playerInput.actions;
            asset.Disable();
            asset.bindingMask = InputBinding.MaskByGroup(chosenScheme); // lock scheme
            asset.Enable();
        }

        UpdateJoinInstructionState();
        Debug.Log($"[Panel {playerIndex}] Claimed {chosenScheme}");
    }

    public void SetPanelActive(bool active)
    {
        gameObject.SetActive(active);
        UpdateJoinInstructionState();
    }

    public void ResetPanelState()
    {
        currentIndex = 0;
        currentSkinIndex = 0;
        hasSelectedCharacter = false;
        hasConfirmedSkin = false;
        hasConfirmedFinal = false;

        NeutralizeInput();
        UpdateDisplay();
        ShowSkinOptions(false);
    }

    // Allows manager to push character data array into the panel
    public void SetCharacterData(CharacterData[] characterData)
    {
        characters = characterData;
        currentIndex = 0;
        currentSkinIndex = 0;
        hasSelectedCharacter = false;
        hasConfirmedSkin = false;
        hasConfirmedFinal = false;

        UpdateSkinConfirmationIndicator();
        ShowSkinOptions(false);

        if (gameObject.activeInHierarchy)
        {
            UpdateDisplay();
            InitializeSkins();
        }
    }

    // Allows manager to apply a style (colors etc.)
    public void ApplyGlobalPlayerStyle(PlayerStyle style)
    {
        if (backgroundImage != null) backgroundImage.color = style.backgroundColor;
        if (playerLabelText != null) playerLabelText.color = style.playerTextColor;
    }

    // Allows manager to read the reserved scheme & devices for saving
    public (string scheme, int[] deviceIds) GetInputSignature()
    {
        if (hasClaimedInputs && !string.IsNullOrEmpty(chosenScheme))
            return (chosenScheme, chosenDeviceIds ?? System.Array.Empty<int>());

        if (playerInput == null)
            return (null, System.Array.Empty<int>());

        var ids = new List<int>();
        foreach (var d in playerInput.devices) ids.Add(d.deviceId);
        var scheme = playerInput.currentControlScheme;
        return (string.IsNullOrEmpty(scheme) ? null : scheme, ids.ToArray());
    }

    private bool IsFromClaimedDevice(InputAction.CallbackContext ctx)
    {
        if (!hasClaimedInputs) return false;
        var dev = ctx.control?.device;
        if (dev == null) return false;
        return chosenDeviceIds.Contains(dev.deviceId);
    }

    private static string GetGroupsForTriggeredBinding(InputAction action, InputControl control)
    {
        if (action == null || control == null) return null;
        int idx = action.GetBindingIndexForControl(control);
        return idx >= 0 ? action.bindings[idx].groups : null;
    }

    // ---------------- CHARACTER LOGIC ----------------
    public void ConfirmSelection()
    {
        if (!hasSelectedCharacter) SelectCharacter();
        else if (!hasConfirmedSkin) ConfirmSkin();
        else if (!hasConfirmedFinal) ConfirmFinalSelection();
    }

    private void SelectCharacter()
    {
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        hasSelectedCharacter = true;
        ShowSkinOptions(true);
        InitializeSkins();
        UpdateSkinHighlight();
    }

    private void ConfirmSkin()
    {
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        hasConfirmedSkin = true;
        UpdateSkinConfirmationIndicator();
        CharacterSelectionManager.Instance?.CheckAllPlayersSelected();
    }

    private void ConfirmFinalSelection()
    {
        hasConfirmedFinal = true;
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound2, SoundCategory.SFX);
        CharacterSelectionManager.Instance?.TryFireFinalConfirmation();
    }

    public void CancelSelection()
    {
        if (!hasSelectedCharacter && !hasConfirmedSkin)
        {
            CharacterSelectionManager.Instance?.OnPlayerCancelledBeforeSelection?.Invoke();
            return;
        }

        if (hasConfirmedSkin) HandleSkinDeselection();
        else HandleCharacterDeselection();

        UpdateDisplay();
    }

    private void HandleSkinDeselection()
    {
        hasConfirmedSkin = false;
        UpdateSkinConfirmationIndicator();
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        CharacterSelectionManager.Instance?.NotifyPlayerUnready(this);
        CharacterSelectionManager.Instance?.CheckAllPlayersSelected();
    }

    private void HandleCharacterDeselection()
    {
        hasSelectedCharacter = false;
        AudioManager.Instance.Play(AudioManager.Instance.pelletEatenSound1, SoundCategory.SFX);
        ShowSkinOptions(false);
        CharacterSelectionManager.Instance?.NotifyPlayerDeselected(this);
        CharacterSelectionManager.Instance?.CheckAllPlayersSelected();
    }

    public void InitializeSkins()
    {
        var skins = SelectedCharacter.skins;

        while (instantiatedSkinIcons.Count < skins.Length)
        {
            var iconGO = Instantiate(skinIconPrefab, skinIconContainer);
            instantiatedSkinIcons.Add(iconGO);
            var indicator = iconGO.transform.GetComponentInChildren<SelectorIndicator>(true);
            selectionIndicators.Add(indicator != null ? indicator.gameObject : null);
        }

        for (int i = 0; i < skins.Length; i++)
        {
            var skin = skins[i];
            var iconGO = instantiatedSkinIcons[i];
            iconGO.SetActive(true);
            iconGO.GetComponent<Image>().sprite = skin.skinIcon;
        }

        for (int i = skins.Length; i < instantiatedSkinIcons.Count; i++)
        {
            instantiatedSkinIcons[i].SetActive(false);
            if (selectionIndicators[i] != null)
                selectionIndicators[i].SetActive(false);
        }

        UpdateSkinHighlight();
    }

    private void ShowSkinOptions(bool visible) => skinIconContainer.gameObject.SetActive(visible);

    private void UpdateSkinHighlight()
    {
        for (int i = 0; i < selectionIndicators.Count; i++)
            if (selectionIndicators[i] != null)
                selectionIndicators[i].SetActive(i == currentSkinIndex);
    }

    private void UpdateSkinConfirmationIndicator()
    {
        for (int i = 0; i < selectionIndicators.Count; i++)
        {
            if (selectionIndicators[i] != null)
            {
                var indicator = selectionIndicators[i].GetComponent<SelectorIndicator>();
                var image = indicator?.GetComponent<Image>();

                if (image != null)
                {
                    if (hasConfirmedSkin && i == currentSkinIndex)
                        image.color = Color.yellow;
                    else
                        image.color = Color.white;
                }
            }
        }
    }

    private void UpdateDisplay()
    {
        var data = SelectedCharacter;
        var skin = SelectedSkin;
        if (data == null || skin == null) return;

        characterImage.sprite = skin.previewSprite;
        characterNameText.text = data.characterName;

        if (CharacterSelectionManager.Instance != null &&
            CharacterSelectionManager.Instance.IsSelectionTaken(this, data, skin))
        {
            characterImage.color = new Color(1.2f, 1.2f, 1.2f, 1f);
        }
        else
        {
            characterImage.color = Color.white;
        }
    }

    // ---------------- HELPERS ----------------
    private static bool GroupsContain(string groups, string candidate)
    {
        if (string.IsNullOrEmpty(groups) || string.IsNullOrEmpty(candidate)) return false;
        return groups.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FirstMatch(string groups, string[] candidates)
    {
        if (string.IsNullOrEmpty(groups) || candidates == null) return null;
        foreach (var c in candidates)
            if (GroupsContain(groups, c)) return c;
        return null;
    }
}