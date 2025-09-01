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

    // Navigation tuning
    [Header("Navigation Tuning")]
    [SerializeField] private float moveDeadzone = 0.5f;         // how far you must tilt
    [SerializeField] private float initialRepeatDelay = 0.35f;  // delay before auto-repeat
    [SerializeField] private float repeatInterval = 0.12f;      // rate while held
    private int lastMoveSign = 0;                               // -1, 0, +1 (left, neutral, right)
    private float nextRepeatTime = 0f;                          // when we’re allowed to repeat again

    private CharacterData[] characters;
    private int playerIndex = -1;
    private bool playerIndexSet = false;
    private int currentIndex = 0;
    private int currentSkinIndex = 0;
    private bool hasSelectedCharacter = false;
    private bool hasConfirmedSkin = false;
    private bool hasConfirmedFinal = false;

    private PlayerInput playerInput;
    private readonly List<GameObject> instantiatedSkinIcons = new();
    private readonly List<GameObject> selectionIndicators = new();

    // First-input claim state
    private string chosenScheme;                                   // "Gamepad", "P1Keyboard", "P2Keyboard", ...
    private int[] chosenDeviceIds = System.Array.Empty<int>();     // exact device ids present when claimed
    private bool hasClaimedInputs = false;

    public CharacterData SelectedCharacter => characters != null && characters.Length > 0 ? characters[currentIndex] : null;
    public CharacterSkin SelectedSkin => SelectedCharacter != null && SelectedCharacter.skins.Length > 0 ? SelectedCharacter.skins[currentSkinIndex] : null;
    public bool HasSelectedCharacter => hasSelectedCharacter;
    public bool HasConfirmedSkin => hasConfirmedSkin;
    public int PlayerIndex => playerIndex;
    public bool HasConfirmedFinal() => hasConfirmedFinal;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
    }

    private void OnEnable()
    {
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();

        if (playerInput != null)
        {
            submit = playerInput.actions["Submit"];
            cancel = playerInput.actions["Cancel"];
            move   = playerInput.actions["Move"];

            if (!submit.enabled) submit.Enable();
            if (!cancel.enabled) cancel.Enable();
            if (!move.enabled)   move.Enable();

            submit.performed += OnSubmitPerformed;
            cancel.performed += OnCancelPerformed;
            move.performed   += OnMovePerformed;
            move.canceled    += OnMoveCanceled;

            playerInput.onDeviceLost     += OnDeviceLost;
            playerInput.onDeviceRegained += OnDeviceRegained;
        }
        else
        {
            Debug.LogError($"[CharacterSelectorPanel] No PlayerInput on {gameObject.name}");
        }

        UpdateJoinInstructionState();
        // NOTE: no global auto-claim here; we only auto-claim for panel index 0 inside SetPlayerIndex.
    }

    private void OnDisable()
    {
        if (submit != null) submit.performed -= OnSubmitPerformed;
        if (cancel != null) cancel.performed -= OnCancelPerformed;
        if (move != null)
        {
            move.performed -= OnMovePerformed;
            move.canceled  -= OnMoveCanceled;
        }

        if (playerInput != null)
        {
            playerInput.onDeviceLost     -= OnDeviceLost;
            playerInput.onDeviceRegained -= OnDeviceRegained;
        }
    }

    private void OnDeviceLost(PlayerInput input)
    {
        Debug.Log($"[CharacterSelectorPanel] Device lost on player {playerIndex + 1}");
        UpdateJoinInstructionState();
    }

    private void OnDeviceRegained(PlayerInput input)
    {
        Debug.Log($"[CharacterSelectorPanel] Device regained on player {playerIndex + 1}");
        UpdateJoinInstructionState();
    }

    private void UpdateJoinInstructionState()
    {
        bool hasControl = hasClaimedInputs;
        if (joinInstruction) joinInstruction.SetActive(!hasControl);
        if (characterData)   characterData.SetActive(hasControl);
    }

    public void SetPlayerIndex(int index)
    {
        playerIndex = index;
        playerIndexSet = true;
        ApplyPlayerLabel();
        Debug.Log($"Player {playerIndex} set: {playerIndexSet}");

        // This auto-claims P1Keyboard only for panel 0 (which is player 1's panel, will not affect other players)
        if (index == 0 && !hasClaimedInputs)
        {
            string scheme = "P1Keyboard";
            if (CharacterSelectionManager.Instance != null)
                scheme = CharacterSelectionManager.Instance.GetKeyboardSchemeForIndex(0) ?? "P1Keyboard";
            ForceClaimKeyboard(scheme);
        }
    }

    public void ApplyGlobalPlayerStyle(PlayerStyle style)
    {
        if (backgroundImage != null) backgroundImage.color = style.backgroundColor;
        if (playerLabelText != null) playerLabelText.color = style.playerTextColor;
    }

    private void ApplyPlayerLabel()
    {
        if (playerLabelText != null)
        {
            playerLabelLocalized.Arguments = new object[] { playerIndex + 1 };
            playerLabelLocalized.StringChanged += (localizedValue) => playerLabelText.text = localizedValue;
            playerLabelLocalized.RefreshString();
        }
    }

    public void SetCharacterData(CharacterData[] characterData)
    {
        characters = characterData;
        currentIndex = 0;
        currentSkinIndex = 0;
        hasSelectedCharacter = false;
        hasConfirmedSkin = false;
        UpdateSkinConfirmationIndicator();
        ShowSkinOptions(false);

        if (gameObject.activeInHierarchy)
        {
            UpdateDisplay();
            InitializeSkins();
        }
    }

    public void ConfirmSelection()
    {
        if (!hasSelectedCharacter) SelectCharacter();
        else if (!hasConfirmedSkin) ConfirmSkin();
        else if (!hasConfirmedFinal) ConfirmFinalSelection();
        else Debug.LogWarning($"[ConfirmSelection] Player {playerIndex + 1} already confirmed final. Ignoring duplicate input.");
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
        Debug.Log($"[ConfirmSelection] Player {playerIndex + 1} final confirmation.");
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
        else                  HandleCharacterDeselection();

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

    private void OnSubmitPerformed(InputAction.CallbackContext ctx)
    {
        if (TryClaimFromContext(ctx)) return;
        if (!IsFromClaimedDevice(ctx)) return;
        if (!ctx.performed) return;
        ConfirmSelection();
    }

    private void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (TryClaimFromContext(ctx)) return;
        if (!IsFromClaimedDevice(ctx)) return;
        if (!ctx.performed) return;
        CancelSelection();
    }

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        if (TryClaimFromContext(context)) return;
        if (!IsFromClaimedDevice(context)) return;
        if (!context.performed || !hasClaimedInputs) return;

        Vector2 v = context.ReadValue<Vector2>();
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

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        if (!IsFromClaimedDevice(context)) return;
        lastMoveSign = 0;
        nextRepeatTime = 0f;
    }

    public void ResetPanelState()
    {
        currentIndex = 0;
        currentSkinIndex = 0;
        hasSelectedCharacter = false;
        hasConfirmedSkin = false;
        UpdateSkinConfirmationIndicator();
        hasConfirmedFinal = false;

        // Reset claim UI
        hasClaimedInputs = false;
        chosenScheme = null;
        chosenDeviceIds = System.Array.Empty<int>();

        // Clear any previous binding mask
        if (playerInput != null && playerInput.actions != null)
        {
            var asset = playerInput.actions;
            asset.Disable();
            asset.bindingMask = default; // remove mask
            asset.Enable();
        }

        UpdateJoinInstructionState();

        Debug.Log($"[ResetPanelState] Panel {playerIndex + 1}: hasConfirmedFinal = {hasConfirmedFinal}");

        UpdateDisplay();
        ShowSkinOptions(false);
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

    public void SetPanelActive(bool active)
    {
        UpdateJoinInstructionState();
    }

    private void UpdateSkinHighlight()
    {
        for (int i = 0; i < selectionIndicators.Count; i++)
        {
            if (selectionIndicators[i] != null)
                selectionIndicators[i].SetActive(i == currentSkinIndex);
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

    // === Force-claim for P1 ===
    private void ForceClaimKeyboard(string scheme)
    {
        chosenScheme = scheme;
        hasClaimedInputs = true;

        var kb = Keyboard.current;
        chosenDeviceIds = (kb != null) ? new[] { kb.deviceId } : System.Array.Empty<int>();

        playerInput.neverAutoSwitchControlSchemes = true;

        if (!string.IsNullOrEmpty(chosenScheme) && playerInput.actions != null)
        {
            var asset = playerInput.actions;
            asset.Disable();
            asset.bindingMask = InputBinding.MaskByGroup(chosenScheme);
            asset.Enable();
        }

        UpdateJoinInstructionState();
    }

    // === Claim helpers (for everyone else) ===

    private void FinalizeClaim()
    {
        hasClaimedInputs = true;
        chosenDeviceIds = playerInput.devices.Select(d => d.deviceId).ToArray();
        playerInput.neverAutoSwitchControlSchemes = true;

        if (!string.IsNullOrEmpty(chosenScheme) && playerInput.actions != null)
        {
            var asset = playerInput.actions;
            asset.Disable();
            asset.bindingMask = InputBinding.MaskByGroup(chosenScheme);
            asset.Enable();
        }

        UpdateJoinInstructionState();
    }

    private bool TryClaimFromContext(InputAction.CallbackContext ctx)
    {
        if (hasClaimedInputs) return false;
        var control = ctx.control;
        if (control == null) return false;

        // Gamepad
        if (control.device is Gamepad)
        {
            if (CharacterSelectionManager.Instance == null || CharacterSelectionManager.Instance.TryReserveGamepad(control.device))
            {
                chosenScheme = "Gamepad";
                FinalizeClaim();
                return true;
            }
            return false;
        }

        // Keyboard: only if the binding that fired belongs to THIS panel's scheme
        if (control.device is Keyboard)
        {
            string targetScheme = "P1Keyboard";
            if (CharacterSelectionManager.Instance != null)
                targetScheme = CharacterSelectionManager.Instance.GetKeyboardSchemeForIndex(playerIndex) ?? "P1Keyboard";

            string groups = GetGroupsForTriggeredBinding(ctx.action, control);
            if (string.IsNullOrEmpty(groups) || !groups.Contains(targetScheme))
                return false;

            chosenScheme = targetScheme;
            FinalizeClaim();
            return true;
        }

        return false;
    }

    private bool IsFromClaimedDevice(InputAction.CallbackContext ctx)
    {
        if (!hasClaimedInputs) return true;
        var dev = ctx.control?.device;
        if (dev == null) return false;
        if (chosenDeviceIds == null || chosenDeviceIds.Length == 0) return true;
        return chosenDeviceIds.Contains(dev.deviceId);
    }

    private static string GetGroupsForTriggeredBinding(InputAction action, InputControl control)
    {
        if (action == null || control == null) return null;

        int idx = action.GetBindingIndexForControl(control);
        if (idx < 0) return null;

        var bindings = action.bindings;
        var groups = bindings[idx].groups;

        if (string.IsNullOrEmpty(groups) && bindings[idx].isPartOfComposite)
        {
            for (int i = idx - 1; i >= 0; i--)
            {
                if (bindings[i].isComposite)
                {
                    groups = bindings[i].groups;
                    break;
                }
            }
        }
        return groups;
    }

    // Exposed to manager: the claimed signature
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
}