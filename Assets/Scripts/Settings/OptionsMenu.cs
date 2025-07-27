using UnityEngine;
using UnityEngine.Localization;
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

    private void SetAndSaveVolume(SettingType type, float value)
    {
        PlayerPrefs.SetFloat(SettingsKeys.Get(type), value);
        AudioManager.Instance?.SetVolume(type, value);
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
                return GetLocalizedStrings(ExtraValues);

            case SettingType.LanguageKey:
                return LocalizationSettings.AvailableLocales.Locales
                    .Select(locale =>
                    {
                        var native = locale.Identifier.CultureInfo?.NativeName;
                        return string.IsNullOrEmpty(native)
                            ? locale.Identifier.Code.ToUpperInvariant()
                            : native.Split('(')[0].Trim();
                    })
                    .ToArray();

            default:
                return new string[0];
        }
    }

    private string[] GetLocalizedStrings(params string[] tableKeys)
    {
        return tableKeys.Select(key =>
        {
            var localized = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", key);
            return string.IsNullOrEmpty(localized) ? key : localized;
        }).ToArray();
    }

    private IEnumerator SetLanguageAsync(Locale locale)
    {
        LocalizationSettings.SelectedLocale = locale;
        yield return null;
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
            
            case SettingType.FullscreenKey:
                return PlayerPrefs.GetInt(SettingsKeys.FullscreenKey, Screen.fullScreen ? 1 : 0);

            case SettingType.ExtraKey:
                string saved = PlayerPrefs.GetString(SettingsKeys.ExtraKey, "None");
                int index = System.Array.IndexOf(ExtraValues, saved);
                return index >= 0 ? index : ExtraValues.Length - 1;

            case SettingType.LanguageKey:
                string currentCode = LocalizationSettings.SelectedLocale.Identifier.Code;
                var locales = LocalizationSettings.AvailableLocales.Locales;
                for (int i = 0; i < locales.Count; i++)
                {
                    if (locales[i].Identifier.Code == currentCode)
                        return i;
                }
                return 0;

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

           case SettingType.FullscreenKey:
                bool isFullscreen = index == 1;
                Screen.fullScreen = isFullscreen;
                PlayerPrefs.SetInt(SettingsKeys.FullscreenKey, isFullscreen ? 1 : 0);
                break;

            case SettingType.ExtraKey:
                if (index >= 0 && index < ExtraValues.Length)
                {
                    string value = ExtraValues[index];
                    PlayerPrefs.SetString(SettingsKeys.ExtraKey, value);
                }
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
            
            case SettingType.FullscreenKey:
                PlayerPrefs.SetInt(SettingsKeys.FullscreenKey, index == 1 ? 1 : 0);
                break;

            case SettingType.ExtraKey:
                if (index >= 0 && index < ExtraValues.Length)
                {
                    string value = ExtraValues[index];
                    PlayerPrefs.SetString(SettingsKeys.ExtraKey, value);
                }
                break;

            case SettingType.LanguageKey:
                if (index >= 0 && index < LocalizationSettings.AvailableLocales.Locales.Count)
                {
                    var locale = LocalizationSettings.AvailableLocales.Locales[index];
                    PlayerPrefs.SetString(SettingsKeys.LanguageKey, locale.Identifier.Code);
                }
                break;
        }

        PlayerPrefs.Save();
    }
}