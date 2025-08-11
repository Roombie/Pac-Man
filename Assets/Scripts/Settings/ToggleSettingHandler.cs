using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;
using TMPro;

public class ToggleSettingHandler : MonoBehaviour, ISettingHandler
{
    [Header("Setting Config")]
    [SettingTypeFilter(SettingType.ShowIndicatorKey, SettingType.FullscreenKey)]
    public SettingType settingType;
    [SerializeField] private AudioClip toggleSound;

    [Header("UI")]
    public Toggle toggle;
    public TextMeshProUGUI label;
    [SerializeField] private string onTextKey = "Toggle_On";
    [SerializeField] private string offTextKey = "Toggle_Off";

    private bool currentValue;

    public SettingType SettingType => settingType;

    private System.Action<UnityEngine.Localization.Locale> localeChangedHandler;

    private ISettingsProvider settingsProvider;

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
        Apply(!currentValue);
        Save();

        // Apply to game immediately
        settingsProvider?.ApplySetting(settingType, currentValue ? 1 : 0);

        if (toggle != null)
            toggle.isOn = currentValue;
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
        int def = (settingType == SettingType.FullscreenKey) ? (Screen.fullScreen ? 1 : 0) : 1;
        currentValue = PlayerPrefs.GetInt(SettingsKeys.Get(settingType), def) == 1;

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
        if (label == null) return;
        string key = (toggle != null && toggle.isOn) ? onTextKey : offTextKey;
        label.text = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", key);
    }
}
