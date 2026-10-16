using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;

public enum NavigationAxis
{
    Horizontal,
    Vertical
}

public class OptionSelectorSettingHandler : MonoBehaviour, ISettingHandler, ISelectHandler, IDeselectHandler, IPointerClickHandler
{
    [SettingTypeFilter(SettingType.MusicVolumeKey, SettingType.SoundVolumeKey, SettingType.PacmanLivesKey, SettingType.ExtraLifeThresholdKey, SettingType.LanguageKey)]
    public SettingType settingType;
    public TextMeshProUGUI label;
    [SerializeField] private NavigationAxis navigationAxis = NavigationAxis.Horizontal;

    [SerializeField] private GameObject leftArrow;
    [SerializeField] private GameObject rightArrow;
    [SerializeField] private AudioClip navigateSound;
    [SerializeField] private AudioClip confirmSound;
    public string[] options;
    public int currentIndex;
    public bool applyImmediately = true;

    public SettingType SettingType => settingType;

    private ArrowSelector arrowSelector;
    private PacmanInputAction inputActions;
    private bool isSelected = false;
    private static OptionSelectorSettingHandler currentlySelecting = null;
    private ISettingsProvider settingsProvider;

    // Mobile: the arrows sit either side of the value (◄ 5 ►), always visible and tappable
    private bool touchArrows;
    private const float TouchArrowGap = 6f;       // canvas units between the value and each arrow
    private const float TouchArrowScale = 1.5f;   // the key-mode arrows are tiny for a finger
    private const float TouchArrowPadding = 14f;  // extra hit area around each arrow

    void Awake() => arrowSelector = FindFirstObjectByType<ArrowSelector>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        var allowed = new SettingType[]
        {
            SettingType.MusicVolumeKey,
            SettingType.SoundVolumeKey,
            SettingType.PacmanLivesKey,
            SettingType.LanguageKey,
            SettingType.ExtraLifeThresholdKey
        };

        if (!System.Array.Exists(allowed, t => t == settingType))
        {
            Debug.LogWarning($"{nameof(OptionSelectorSettingHandler)}: Invalid SettingType '{settingType}' assigned. Resetting to default.");
            settingType = allowed[0];
        }
    }
#endif

    void Start()
    {
        settingsProvider = FindFirstObjectByType<OptionsMenu>();
        if (settingsProvider == null)
        {
            Debug.LogError($"[{nameof(OptionSelectorSettingHandler)}] No ISettingsProvider found in scene.");
            return;
        }

        LoadLocalizedOptions();
        if (TouchInput.IsMobile) SetupTouchArrows();
        ApplyFromSaved();

        SetArrowVisibility(false);
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnEnable()
    {
        inputActions ??= new PacmanInputAction();
        inputActions.UI.Navigate.performed += OnNavigate;
        inputActions.UI.Submit.performed += OnSubmit;
        inputActions.Enable();

        LayoutTouchArrows(); // the value may have changed language while the menu was closed
    }

    void OnDisable()
    {
        if (inputActions != null)
        {
            inputActions.UI.Navigate.performed -= OnNavigate;
            inputActions.UI.Submit.performed -= OnSubmit;
            inputActions.Disable();
        }
    }

    void OnDestroy()
    {
        inputActions?.Disable();
        inputActions?.Dispose();
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    private void OnLocaleChanged(UnityEngine.Localization.Locale locale)
    {
        LoadLocalizedOptions();
        RefreshUI();

        foreach (var toggle in Resources.FindObjectsOfTypeAll<ToggleSettingHandler>())
        {
            if (toggle.gameObject.scene.IsValid())
                toggle.RefreshUI();
        }
    }

    private void OnNavigate(InputAction.CallbackContext context)
    {
        if (!isSelected) return;

        Vector2 input = context.ReadValue<Vector2>();
        switch (navigationAxis)
        {
            case NavigationAxis.Horizontal:
                if (input.x < 0) ChangeOption(-1);
                else if (input.x > 0) ChangeOption(1);
                break;

            case NavigationAxis.Vertical:
                if (input.y < 0) ChangeOption(-1);
                else if (input.y > 0) ChangeOption(1);
                break;
        }
    }

    private void OnSubmit(InputAction.CallbackContext context)
    {
        if (EventSystem.current.currentSelectedGameObject != gameObject) return;

        if (!isSelected)
        {
            if (currentlySelecting != null && currentlySelecting != this) return;
            AudioManager.Instance?.Play(navigateSound, SoundCategory.SFX);
            isSelected = true;
            currentlySelecting = this;
            EventManager<object>.TriggerEvent(EventKey.UIInteractionChanged, true);
            EventSystem.current.SetSelectedGameObject(gameObject);
            EventSystem.current.sendNavigationEvents = false;
            SetArrowVisibility(true);
            arrowSelector?.SetSelecting(true);
        }
        else
        {
            AudioManager.Instance?.Play(confirmSound, SoundCategory.SFX);
            isSelected = false;
            currentlySelecting = null;
            EventManager<object>.TriggerEvent(EventKey.UIInteractionChanged, false);
            EventSystem.current.sendNavigationEvents = true;
            SetArrowVisibility(false);
            Save();
            settingsProvider?.ApplySetting(settingType, currentIndex);

            if (arrowSelector != null)
            {
                arrowSelector.SetSelecting(false);
                StartCoroutine(DelayedMoveArrow());
            }
        }
    }

    private IEnumerator DelayedMoveArrow()
    {
        yield return null;
        arrowSelector.MoveIndicator(arrowSelector.lastSelected);
    }

    private void SetArrowVisibility(bool visible)
    {
        visible |= touchArrows;
        if (leftArrow != null) leftArrow.SetActive(visible);
        if (rightArrow != null) rightArrow.SetActive(visible);
    }

    private void ChangeOption(int delta)
    {
        if (options == null || options.Length == 0) return;

        int newIndex = currentIndex + delta;
        if (newIndex < 0 || newIndex >= options.Length) return;

        currentIndex = newIndex;
        Apply(currentIndex);

        AudioManager.Instance?.Play(navigateSound, SoundCategory.SFX);
    }

    public void Next() => ChangeOption(1);
    public void Prev() => ChangeOption(-1);

    public void Apply(int index)
    {
        if (options == null || index < 0 || index >= options.Length)
        {
            Debug.LogWarning($"[{nameof(OptionSelectorSettingHandler)}] Invalid index {index} for {settingType}.");
            return;
        }

        currentIndex = index;
        label.text = options[currentIndex];
        LayoutTouchArrows();

        if (applyImmediately && settingsProvider != null)
        {
            settingsProvider.ApplySetting(settingType, currentIndex);
        }
    }

    public void Apply(bool value) { }

    public void ApplyFromSaved()
    {
        int saved = settingsProvider != null
            ? settingsProvider.GetSavedIndex(settingType)
            : PlayerPrefs.GetInt(SettingsKeys.Get(settingType), 0);

        currentIndex = saved;
        Apply(saved);
    }

    public void Save()
    {
        settingsProvider?.SaveSetting(settingType, currentIndex);
    }

    public void RefreshUI()
    {
        if (settingsProvider != null)
        {
            options = settingsProvider.GetOptions(settingType);
        }

        label.text = (options != null && currentIndex >= 0 && currentIndex < options.Length)
            ? options[currentIndex]
            : "";
        LayoutTouchArrows();
    }

    private void LoadLocalizedOptions()
    {
        options = settingsProvider?.GetOptions(settingType);
    }

    /// <summary>
    /// Touch/click on the row: left of the value steps it down, right of it up, applied right away
    /// (keyboard and gamepad use the Submit -> Navigate -> Submit edit mode instead)
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position, eventData.pressEventCamera, out var local))
            return;

        StepFromTouch(local.x < ValueCenterInRow().x ? -1 : 1);
    }

    private void StepFromTouch(int delta)
    {
        if (isSelected) return; // already being edited with keys/buttons

        int before = currentIndex;
        ChangeOption(delta);
        if (currentIndex == before) return;

        Save();
        settingsProvider?.ApplySetting(settingType, currentIndex);
    }

    /// <summary>
    /// Turns the key-mode arrows into horizontal ◄ ► buttons beside the value
    /// </summary>
    private void SetupTouchArrows()
    {
        if (leftArrow == null || rightArrow == null || label == null) return;

        SetupTouchArrow(leftArrow, -1);
        SetupTouchArrow(rightArrow, 1);
        touchArrows = true;
        SetArrowVisibility(true);
    }

    private void SetupTouchArrow(GameObject arrow, int delta)
    {
        // The sprite points right; mirroring it gives the left arrow (as in character select)
        arrow.transform.localRotation = Quaternion.identity;
        arrow.transform.localScale = new Vector3(delta * TouchArrowScale, TouchArrowScale, 1f);

        if (arrow.TryGetComponent<Image>(out var image))
        {
            image.raycastTarget = true;
            image.raycastPadding = -Vector4.one * TouchArrowPadding; // negative padding grows the hit area
        }

        if (!arrow.TryGetComponent<Button>(out var button)) button = arrow.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => StepFromTouch(delta));
    }

    /// <summary>
    /// Keeps the arrows hugging the value text, whose width changes ("5" vs "Espanol")
    /// </summary>
    private void LayoutTouchArrows()
    {
        if (!touchArrows) return;

        label.ForceMeshUpdate();
        Vector2 center = ValueCenterInRow();
        float half = label.textBounds.size.x / 2f;

        PlaceTouchArrow(leftArrow, center, -(half + TouchArrowGap));
        PlaceTouchArrow(rightArrow, center, half + TouchArrowGap);
    }

    // offset: from the value's centre to the arrow's inner edge (negative = left side)
    private static void PlaceTouchArrow(GameObject arrow, Vector2 valueCenter, float offset)
    {
        var rect = (RectTransform)arrow.transform;
        float halfWidth = rect.rect.width * TouchArrowScale / 2f;
        float x = valueCenter.x + offset + Mathf.Sign(offset) * halfWidth;
        rect.localPosition = new Vector3(x, valueCenter.y, rect.localPosition.z);
    }

    private Vector2 ValueCenterInRow()
    {
        var bounds = label.textBounds;
        Vector3 center = bounds.size.x > 0f ? bounds.center : label.rectTransform.rect.center;
        return transform.InverseTransformPoint(label.transform.TransformPoint(center));
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (isSelected)
            SetArrowVisibility(true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        EventManager<object>.TriggerEvent(EventKey.UIInteractionChanged, false);

        SetArrowVisibility(false);
    }
    
    // True when any instance is in mid-edit mode — used by PauseUIController
    public static bool IsCurrentlySelecting => currentlySelecting != null;

    // Called by PauseUIController to forward Left/Right to the active selector
    public static void NavigateCurrent(int delta)
    {
        currentlySelecting?.ChangeOption(delta);
    }

    public static bool TryCancelCurrentSelection()
    {
        if (currentlySelecting == null) return false;
        currentlySelecting.CancelSelectingWithoutSave();
        return true;
    }

    private void CancelSelectingWithoutSave()
    {
        if (!isSelected) return;

        isSelected = false;
        currentlySelecting = null;

        // Re-enable global UI nav
        EventManager<object>.TriggerEvent(EventKey.UIInteractionChanged, false);
        if (EventSystem.current != null)
        {
            EventSystem.current.sendNavigationEvents = true;
            // Keep focus on this row so the EventSystem knows where to go next
            EventSystem.current.SetSelectedGameObject(gameObject);
        }

        // Hide arrows & exit "selecting" visuals
        SetArrowVisibility(false);
        if (arrowSelector != null)
        {
            arrowSelector.SetSelecting(false);
            StartCoroutine(DelayedMoveArrow()); // keep your existing arrow follow
        }
    }
}