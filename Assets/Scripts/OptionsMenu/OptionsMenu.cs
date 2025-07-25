using UnityEngine;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Linq;

public class OptionsMenu : MonoBehaviour, ISettingsProvider
{
    private static readonly string[] ExtraValues = { "10000", "15000", "20000", "None" };

    void Start()
    {
        StartCoroutine(InitializeAfterLocalizationReady());
    }

    private IEnumerator InitializeAfterLocalizationReady()
    {
        yield return LocalizationSettings.InitializationOperation;
        yield return new WaitForEndOfFrame();

        foreach (var selector in FindObjectsByType<OptionSelectorSettingHandler>(FindObjectsSortMode.None))
            selector.RefreshUI();

        foreach (var toggle in FindObjectsByType<ToggleSettingHandler>(FindObjectsSortMode.None))
            toggle.RefreshUI();
    }

    private void SetAndSaveVolume(SettingType type, float value)
    {
        PlayerPrefs.SetFloat(SettingsKeys.Get(type), value);
        AudioManager.Instance?.SetVolume(type, value);
    }

    public void ResetSettingsToDefault()
    {
        SetAndSaveVolume(SettingType.MusicVolumeKey, 0.8f);
        SetAndSaveVolume(SettingType.SoundVolumeKey, 0.8f);
        PlayerPrefs.SetInt(SettingsKeys.PacmanLivesKey, 3);
        PlayerPrefs.SetInt(SettingsKeys.ShowIndicatorKey, 1);
        PlayerPrefs.SetInt(SettingsKeys.FullscreenKey, 0);
        PlayerPrefs.SetString(SettingsKeys.ExtraKey, "None");

        PlayerPrefs.Save();

        foreach (var selector in FindObjectsByType<OptionSelectorSettingHandler>(FindObjectsSortMode.None))
            selector.ApplyFromSaved();

        foreach (var toggle in FindObjectsByType<ToggleSettingHandler>(FindObjectsSortMode.None))
            toggle.ApplyFromSaved();

        Debug.Log("Settings reset to default.");
    }

    public string[] GetOptions(SettingType type)
    {
        switch (type)
        {
            case SettingType.MusicVolumeKey:
            case SettingType.SoundVolumeKey:
                return Enumerable.Range(0, 11).Select(i => i.ToString()).ToArray();

            case SettingType.PacmanLivesKey:
                return Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray();

            case SettingType.ExtraKey:
                return new[]
                {
                    "10000",
                    "15000",
                    "20000",
                    LocalizationSettings.StringDatabase.GetLocalizedString("GameText", "Extra_None")
                };

            default:
                return new string[0];
        }
    }

    public int GetSavedIndex(SettingType type)
    {
        switch (type)
        {
            case SettingType.MusicVolumeKey:
                return Mathf.RoundToInt(PlayerPrefs.GetFloat(SettingsKeys.MusicVolumeKey, 0.8f) * 10f);

            case SettingType.SoundVolumeKey:
                return Mathf.RoundToInt(PlayerPrefs.GetFloat(SettingsKeys.SoundVolumeKey, 0.8f) * 10f);

            case SettingType.PacmanLivesKey:
                return PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, 3) - 1;

            case SettingType.ExtraKey:
                string saved = PlayerPrefs.GetString(SettingsKeys.ExtraKey, "None");
                return saved switch
                {
                    "10000" => 0,
                    "15000" => 1,
                    "20000" => 2,
                    _ => 3 // Default to "None"
                };

            default:
                return 0;
        }
    }

    public void ApplySetting(SettingType type, int index)
    {
        switch (type)
        {
            case SettingType.MusicVolumeKey:
            case SettingType.SoundVolumeKey:
                SetAndSaveVolume(type, index / 10f);
                break;

            case SettingType.PacmanLivesKey:
                PlayerPrefs.SetInt(SettingsKeys.PacmanLivesKey, index + 1);
                break;

            case SettingType.ExtraKey:
                if (index >= 0 && index < ExtraValues.Length)
                    PlayerPrefs.SetString(SettingsKeys.ExtraKey, ExtraValues[index]);
                break;
        }
    }

    public void SaveSetting(SettingType type, int index)
    {
        switch (type)
        {
            case SettingType.MusicVolumeKey:
                PlayerPrefs.SetFloat(SettingsKeys.MusicVolumeKey, index / 10f);
                break;

            case SettingType.SoundVolumeKey:
                PlayerPrefs.SetFloat(SettingsKeys.SoundVolumeKey, index / 10f);
                break;

            case SettingType.PacmanLivesKey:
                PlayerPrefs.SetInt(SettingsKeys.PacmanLivesKey, index + 1);
                break;

            case SettingType.ExtraKey:
                if (index >= 0 && index < ExtraValues.Length)
                    PlayerPrefs.SetString(SettingsKeys.ExtraKey, ExtraValues[index]);
                break;
        }

        PlayerPrefs.Save();
    }
}