using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Linq;

public class OptionsMenu : MonoBehaviour, ISettingsProvider
{
    void Start()
    {
        StartCoroutine(InitializeAfterLocalizationReady());
    }

    private IEnumerator InitializeAfterLocalizationReady()
    {
        yield return LocalizationSettings.InitializationOperation;

        string savedLang = PlayerPrefs.GetString(SettingsKeys.LanguageKey, null);
        if (!string.IsNullOrEmpty(savedLang))
        {
            var locale = LocalizationSettings.AvailableLocales.Locales
                .FirstOrDefault(l => l.Identifier.Code == savedLang);
            if (locale != null)
                LocalizationSettings.SelectedLocale = locale;
        }

        yield return new WaitForEndOfFrame();

        foreach (var selector in FindObjectsByType<OptionSelectorSettingHandler>(FindObjectsSortMode.None))
            selector.RefreshUI();

        foreach (var toggle in FindObjectsByType<ToggleSettingHandler>(FindObjectsSortMode.None))
            toggle.RefreshUI();
    }

    public string[] GetOptions(SettingType type)
    {
        return type switch
        {
            SettingType.MusicVolumeKey or SettingType.SoundVolumeKey
                => Enumerable.Range(0, 11).Select(i => i.ToString()).ToArray(),

            SettingType.PacmanLivesKey
                => Enumerable.Range(1, GameConstants.MaxLives).Select(i => i.ToString()).ToArray(),

            SettingType.ExtraKey
                => GameConstants.ExtraPoints.Select(p => GetLocalized(p == 0 ? "None" : p.ToString())).ToArray(),

            SettingType.LanguageKey
                => LocalizationSettings.AvailableLocales.Locales
                    .Select(locale =>
                        locale.Identifier.CultureInfo?.NativeName?.Split('(')[0].Trim()
                        ?? locale.Identifier.Code.ToUpperInvariant())
                    .ToArray(),

            _ => new string[0]
        };
    }

    public int GetSavedIndex(SettingType type)
    {
        return type switch
        {
            SettingType.MusicVolumeKey
                => Mathf.RoundToInt(PlayerPrefs.GetFloat(SettingsKeys.MusicVolumeKey, 0.8f) * 10f),

            SettingType.SoundVolumeKey
                => Mathf.RoundToInt(PlayerPrefs.GetFloat(SettingsKeys.SoundVolumeKey, 0.8f) * 10f),

            SettingType.PacmanLivesKey
                => PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, 3) - 1,

            SettingType.FullscreenKey
                => PlayerPrefs.GetInt(SettingsKeys.FullscreenKey, Screen.fullScreen ? 1 : 0),

            SettingType.ExtraKey
                => System.Array.IndexOf(GameConstants.ExtraPoints, PlayerPrefs.GetInt(SettingsKeys.ExtraKey, 0)),

            SettingType.LanguageKey
                => LocalizationSettings.AvailableLocales.Locales
                    .FindIndex(locale => locale.Identifier.Code == LocalizationSettings.SelectedLocale.Identifier.Code),

            _ => 0
        };
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

            case SettingType.FullscreenKey:
                bool isFullscreen = index == 1;
                Screen.fullScreen = isFullscreen;
                PlayerPrefs.SetInt(SettingsKeys.FullscreenKey, isFullscreen ? 1 : 0);
                break;

            case SettingType.ExtraKey:
                if (index >= 0 && index < GameConstants.ExtraPoints.Length)
                    PlayerPrefs.SetInt(SettingsKeys.ExtraKey, GameConstants.ExtraPoints[index]);
                break;

            case SettingType.LanguageKey:
                if (index >= 0 && index < LocalizationSettings.AvailableLocales.Locales.Count)
                {
                    var selectedLocale = LocalizationSettings.AvailableLocales.Locales[index];
                    PlayerPrefs.SetString(SettingsKeys.LanguageKey, selectedLocale.Identifier.Code);
                    StartCoroutine(SetLanguageAsync(selectedLocale));
                }
                break;
        }
    }

    public void SaveSetting(SettingType type, int index)
    {
        ApplySetting(type, index);
        PlayerPrefs.Save();
    }

    private void SetAndSaveVolume(SettingType type, float value)
    {
        PlayerPrefs.SetFloat(SettingsKeys.Get(type), value);
        AudioManager.Instance?.SetVolume(type, value);
    }

    private string GetLocalized(string key)
    {
        var localized = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", key);
        return string.IsNullOrEmpty(localized) ? key : localized;
    }

    private IEnumerator SetLanguageAsync(Locale locale)
    {
        LocalizationSettings.SelectedLocale = locale;
        yield return null;
    }
}