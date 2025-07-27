using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;

public enum NavigationAxis
{
    Horizontal,
    Vertical
}

public class OptionSelectorSettingHandler : MonoBehaviour, ISettingHandler, ISelectHandler, IDeselectHandler
{
    [SettingTypeFilter(SettingType.MusicVolumeKey, SettingType.SoundVolumeKey, SettingType.PacmanLivesKey, SettingType.ExtraKey, SettingType.LanguageKey)]
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
    private Pacman inputActions;
    private bool isSelected = false;
    private static OptionSelectorSettingHandler currentlySelecting = null;
    private ISettingsProvider settingsProvider;

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
            SettingType.ExtraKey
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
        ApplyFromSaved();

        SetArrowVisibility(false);
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnEnable()
    {
        inputActions ??= new Pacman();
        inputActions.UI.Navigate.performed += OnNavigate;
        inputActions.UI.Submit.performed += OnSubmit;
        inputActions.Enable();
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
    }

    private void LoadLocalizedOptions()
    {
        options = settingsProvider?.GetOptions(settingType);
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
}
