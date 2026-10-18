using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using TMPro;
using UnityEngine.EventSystems;

public class ToggleSettingHandler : MonoBehaviour, ISettingHandler, IPointerClickHandler
{
    [Header("Setting Config")]
    [SettingTypeFilter(SettingType.ShowIndicatorKey, SettingType.FullscreenKey, SettingType.ShowGhostTargetsKey)]
    public SettingType settingType;
    [SerializeField] private AudioClip toggleSound;

    [Header("UI")]
    public Toggle toggle;
    public TextMeshProUGUI label;
    [SerializeField] private LocalizedString onText;
    [SerializeField] private LocalizedString offText;

    private bool currentValue;
    private bool initialized;          // Saved value applied (localization ready)
    private bool lastScreenFullscreen; // Last real fullscreen state seen, to catch Esc / Alt+Enter

    public SettingType SettingType => settingType;

    private System.Action<Locale> localeChangedHandler;

    private ISettingsProvider settingsProvider;


    void Awake()
    {
        // Phones are always fullscreen; switching it off only reveals the system bars
        // Mobile browsers keep it: there fullscreen can only be requested from this toggle
        if (settingType == SettingType.FullscreenKey && TouchInput.IsMobile &&
            Application.platform != RuntimePlatform.WebGLPlayer)
            SelectableNavigation.HideAndRouteAround(gameObject);
    }

    void OnEnable()
    {
        // The menu may have been closed while fullscreen changed
        if (initialized) SyncWithScreen();
    }

    void Update()
    {
        if (initialized) SyncWithScreen();
    }

    /// <summary>
    /// Follows fullscreen changes made outside the toggle (Esc in the browser, Alt+Enter on desktop)
    /// Only reacts when the real state changes, since the toggle's own request takes effect a bit later
    /// </summary>
    private void SyncWithScreen()
    {
        if (settingType != SettingType.FullscreenKey) return;

        bool fullscreen = Screen.fullScreen;
        if (fullscreen == lastScreenFullscreen) return;
        lastScreenFullscreen = fullscreen;

        if (fullscreen == currentValue) return;
        Apply(fullscreen);
        Save();
    }

    // Tapping the label flips the setting too, not just the small toggle box (which handles its own taps)
    public void OnPointerClick(PointerEventData eventData)
    {
        if (toggle != null && toggle.interactable) toggle.isOn = !toggle.isOn;
    }

    void Start()
    {
        localeChangedHandler = _ => RefreshUI();
        LocalizationSettings.SelectedLocaleChanged += localeChangedHandler;

        if (toggle != null)
            toggle.onValueChanged.AddListener(OnToggleChanged);

        settingsProvider = FindFirstObjectByType<OptionsMenu>();
        if (settingsProvider == null)
            Debug.LogError($"[{nameof(ToggleSettingHandler)}] No ISettingsProvider found in scene.");

        StartCoroutine(InitializeAfterLocalization());
    }

    private System.Collections.IEnumerator InitializeAfterLocalization()
    {
        yield return LocalizationSettings.InitializationOperation;

        ApplyFromSaved();
        RefreshUI();

        lastScreenFullscreen = Screen.fullScreen;
        initialized = true;
    }

    private void OnDestroy()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(OnToggleChanged);

        LocalizationSettings.SelectedLocaleChanged -= localeChangedHandler;
    }

    private void OnToggleChanged(bool value)
    {
        // Update local/UI state
        Apply(value);
        Save();

        settingsProvider?.ApplySetting(settingType, value ? 1 : 0);

        if (toggleSound != null && AudioManager.Instance != null)
            AudioManager.Instance.Play(toggleSound, SoundCategory.SFX);
        else
            Debug.LogWarning("Either a toggleSound wasn't referenced or the AudioManager isn't on the scene");
    }

    // Programmatic toggle (e.g., bound to a button)
    public void Toggle()
    {
        bool newValue = !currentValue;
        currentValue = newValue;

        if (toggle != null)
            toggle.SetIsOnWithoutNotify(currentValue);

        RefreshUI();
        Save();
        settingsProvider?.ApplySetting(settingType, currentValue ? 1 : 0);

        if (toggleSound != null && AudioManager.Instance != null)
            AudioManager.Instance.Play(toggleSound, SoundCategory.SFX);
    }

    public void Apply(bool value)
    {
        currentValue = value;

        if (toggle != null)
            toggle.SetIsOnWithoutNotify(currentValue);

        RefreshUI();
    }

    public void Apply(int index) { /* not used by toggles */ }

    public void ApplyFromSaved()
    {
        int def = settingType switch
        {
            SettingType.FullscreenKey => Screen.fullScreen ? 1 : 0,
            SettingType.ShowGhostTargetsKey => 0, // Helper, off unless asked for
            _ => 1,
        };
        currentValue = PlayerPrefs.GetInt(SettingsKeys.Get(settingType), def) == 1;

        // On web fullscreen isn't restored from prefs (needs a user gesture), so show the real state
        if (settingType == SettingType.FullscreenKey && Application.platform == RuntimePlatform.WebGLPlayer)
            currentValue = Screen.fullScreen;

        if (toggle != null)
            toggle.SetIsOnWithoutNotify(currentValue);

        Apply(currentValue);
    }

    public void Save()
    {
        PlayerPrefs.SetInt(SettingsKeys.Get(settingType), currentValue ? 1 : 0);
    }

    public void RefreshUI()
    {
        if (!label) return;

        // Use the synchronous path so the label updates instantly even when
        // Time.timeScale == 0 (e.g. during pause). GetLocalizedStringAsync
        // relies on a coroutine internally that never advances at timeScale 0.
        LocalizedString reference = (toggle != null && toggle.isOn) ? onText : offText;
        string result = reference.GetLocalizedString();
        if (label) label.text = result;
    }
}