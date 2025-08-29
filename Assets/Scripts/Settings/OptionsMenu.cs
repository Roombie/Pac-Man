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
        // Ensure localization system is ready (so language option labels are correct)
        yield return LocalizationSettings.InitializationOperation;

        // Give UI a frame to spawn (if this is an options scene)
        yield return new WaitForEndOfFrame();

        // Refresh any handlers present in this scene
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

            SettingType.ExtraLifeThresholdKey
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
                => PlayerPrefs.GetInt(SettingsKeys.PacmanLivesKey, GameConstants.MaxLives) - 1,

            SettingType.FullscreenKey
                => PlayerPrefs.GetInt(SettingsKeys.FullscreenKey, Screen.fullScreen ? 1 : 0),

            SettingType.ExtraLifeThresholdKey
                => System.Array.IndexOf(GameConstants.ExtraPoints, PlayerPrefs.GetInt(SettingsKeys.ExtraLifeThresholdKey, GameConstants.ExtraPoints[0])),

            SettingType.LanguageKey
                => LocalizationSettings.AvailableLocales.Locales
                    .FindIndex(locale => locale.Identifier.Code ==
                        (PlayerPrefs.GetString(SettingsKeys.LanguageKey,
                            LocalizationSettings.SelectedLocale.Identifier.Code))),

            SettingType.ShowIndicatorKey
                => PlayerPrefs.GetInt(SettingsKeys.ShowIndicatorKey, 1),

            _ => 0
        };
    }

    // Convenience overload for toggles
    public void ApplySetting(SettingType type, bool on) => ApplySetting(type, on ? 1 : 0);

    public void ApplySetting(SettingType type, int index)
    {
        // Delegate everything to the global manager (including Language)
        SettingsManager.Apply(type, index);
    }

    public void SaveSetting(SettingType type, int index)
    {
        ApplySetting(type, index); // manager persists where needed
        PlayerPrefs.Save();
    }

    private string GetLocalized(string key)
    {
        var localized = LocalizationSettings.StringDatabase.GetLocalizedString("GameText", key);
        return string.IsNullOrEmpty(localized) ? key : localized;
    }
}