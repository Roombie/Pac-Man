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

    [Header("Localization Keys")]
    public string onTextKey = "Toggle_On";
    public string offTextKey = "Toggle_Off";

    private bool currentValue;

    public SettingType SettingType => settingType;

    private System.Action<UnityEngine.Localization.Locale> localeChangedHandler;
    
   void Start()
    {
        localeChangedHandler = _ => RefreshUI();
        LocalizationSettings.SelectedLocaleChanged += localeChangedHandler;

        if (toggle != null)
            toggle.onValueChanged.AddListener(OnToggleChanged);

        StartCoroutine(InitializeAfterLocalization());
    }

    private System.Collections.IEnumerator InitializeAfterLocalization()
    {
        yield return LocalizationSettings.InitializationOperation;

        ApplyFromSaved();
    }

    private void OnDestroy()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(OnToggleChanged);

        LocalizationSettings.SelectedLocaleChanged -= localeChangedHandler;
    }

    private void OnToggleChanged(bool value)
    {
        Apply(value);
        Save();

        if (toggleSound != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.Play(toggleSound, SoundCategory.SFX);
        }
        else
        {
            Debug.LogWarning("Either a toggleSound wasn't referenced or the AudioManager isn't on the scene");
        }
    }

    public void Toggle()
    {
        Apply(!currentValue);
        Save();

        if (toggle != null)
            toggle.isOn = currentValue;
    }

    public void Apply(bool value)
    {
        currentValue = value;

        if (toggle != null)
            toggle.SetIsOnWithoutNotify(currentValue);

        RefreshUI();

        // SettingsApplier.ApplyBoolSetting(settingType, currentValue);
    }

    public void Apply(int index) { }

    public void ApplyFromSaved()
    {
        currentValue = PlayerPrefs.GetInt(SettingsKeys.Get(settingType), 0) == 1;
        Apply(currentValue);
    }

    public void Save()
    {
        PlayerPrefs.SetInt(SettingsKeys.Get(settingType), currentValue ? 1 : 0);
    }

    public void RefreshUI()
    {
        if (label == null) return;
        string key = toggle.isOn ? onTextKey : offTextKey;
        label.text = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", key);
    }
}