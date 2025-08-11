using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Linq;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [Header("Startup")]
    [Tooltip("Temporarily disables all Canvases in the first scene until the saved locale is applied, preventing a one-frame flicker in the wrong language.")]
    public bool blockUIUntilLocaleApplied = true;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Set locale immediately to prevent flicker
        var savedCode = PlayerPrefs.GetString(SettingsKeys.LanguageKey, null);
        if (!string.IsNullOrEmpty(savedCode))
        {
            var list = LocalizationSettings.AvailableLocales.Locales;
            var saved = list.FirstOrDefault(l => l.Identifier.Code == savedCode);
            if (saved != null)
                LocalizationSettings.SelectedLocale = saved; // This should make an immediate assignment, avoiding language flicker
        }

        // Ensure language + settings are applied on every scene load
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        // Apply language ASAP (prevents flicker), then apply other settings for the initial scene
        StartCoroutine(BootstrapLocalizationAndSettings());
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-apply runtime settings whenever a new scene loads (volumes, indicators, fullscreen)
        ApplyAllFromPrefs();
    }

    private IEnumerator BootstrapLocalizationAndSettings()
    {
        Canvas[] canvases = null;

        if (blockUIUntilLocaleApplied)
        {
            canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var c in canvases) c.enabled = false;
        }

        // Wait for localization to be ready
        yield return LocalizationSettings.InitializationOperation;

        // Apply saved language immediately (before UI shows)
        var savedCode = PlayerPrefs.GetString(SettingsKeys.LanguageKey, null);
        if (!string.IsNullOrEmpty(savedCode))
        {
            var list = LocalizationSettings.AvailableLocales.Locales;
            var saved = list.FirstOrDefault(l => l.Identifier.Code == savedCode);
            if (saved != null)
                LocalizationSettings.SelectedLocale = saved;
        }

        // Apply the rest of the settings (volumes, fullscreen, gameplay flags)
        ApplyAllFromPrefs();

        // Re-enable UI now that the locale is in place
        if (canvases != null)
            foreach (var c in canvases) c.enabled = true;
    }

    /// <summary>Apply all persisted settings into the current scene.</summary>
    public void ApplyAllFromPrefs()
    {
        // Fullscreen
        Screen.fullScreen = PlayerPrefs.GetInt(SettingsKeys.FullscreenKey, 1) == 1;

        // Volumes
        float music = PlayerPrefs.GetFloat(SettingsKeys.MusicVolumeKey, 0.8f);
        float sfx   = PlayerPrefs.GetFloat(SettingsKeys.SoundVolumeKey, 0.8f);
        AudioManager.Instance?.SetVolume(SettingType.MusicVolumeKey, music);
        AudioManager.Instance?.SetVolume(SettingType.SoundVolumeKey, sfx);

        // Gameplay
        bool showIndicator = PlayerPrefs.GetInt(SettingsKeys.ShowIndicatorKey, 1) == 1;
        foreach (var p in FindObjectsByType<Pacman>(FindObjectsSortMode.None))
            p.UpdateIndicatorVisibility(showIndicator);
    }

    // ---------- Static convenience for any scene/UI ----------
    public static void Apply(SettingType type, bool on) => Apply(type, on ? 1 : 0);
    public static void Apply(SettingType type, int index)
    {
        if (Instance == null) return;
        Instance.ApplySetting(type, index);
    }

    // ---------- Core application + persistence ----------
    public void ApplySetting(SettingType type, bool on) => ApplySetting(type, on ? 1 : 0);

    public void ApplySetting(SettingType type, int index)
    {
        switch (type)
        {
            case SettingType.MusicVolumeKey:
            {
                float v = Mathf.Clamp01(index / 10f);
                PlayerPrefs.SetFloat(SettingsKeys.MusicVolumeKey, v);
                AudioManager.Instance?.SetVolume(type, v);
                break;
            }
            case SettingType.SoundVolumeKey:
            {
                float v = Mathf.Clamp01(index / 10f);
                PlayerPrefs.SetFloat(SettingsKeys.SoundVolumeKey, v);
                AudioManager.Instance?.SetVolume(type, v);
                break;
            }
            case SettingType.PacmanLivesKey:
                PlayerPrefs.SetInt(SettingsKeys.PacmanLivesKey, index + 1);
                break;

            case SettingType.FullscreenKey:
            {
                bool isFullscreen = (index == 1);
                Screen.fullScreen = isFullscreen;
                PlayerPrefs.SetInt(SettingsKeys.FullscreenKey, isFullscreen ? 1 : 0);
                break;
            }

            case SettingType.ExtraLifeThresholdKey:
                if (index >= 0 && index < GameConstants.ExtraPoints.Length)
                    PlayerPrefs.SetInt(SettingsKeys.ExtraLifeThresholdKey, GameConstants.ExtraPoints[index]);
                break;

            case SettingType.LanguageKey:
            {
                // Persist and apply via coroutine to ensure localization is initialized
                var locales = LocalizationSettings.AvailableLocales;
                if (index >= 0 && index < locales.Locales.Count)
                {
                    var selected = locales.Locales[index];
                    PlayerPrefs.SetString(SettingsKeys.LanguageKey, selected.Identifier.Code);
                    PlayerPrefs.Save();
                    StartCoroutine(SetLanguageAsync(selected));
                }
                break;
            }

            case SettingType.ShowIndicatorKey:
            {
                bool on = (index == 1);
                PlayerPrefs.SetInt(SettingsKeys.ShowIndicatorKey, on ? 1 : 0);
                foreach (var p in FindObjectsByType<Pacman>(FindObjectsSortMode.None))
                    p.UpdateIndicatorVisibility(on);
                break;
            }
        }

        PlayerPrefs.Save();
    }

    private IEnumerator SetLanguageAsync(Locale locale)
    {
        // Make sure localization is ready, then set the locale
        yield return LocalizationSettings.InitializationOperation;

        LocalizationSettings.SelectedLocale = locale;

        // After switching language, refresh any options UI present in this scene
        foreach (var selector in FindObjectsByType<OptionSelectorSettingHandler>(FindObjectsSortMode.None))
            selector.RefreshUI();
        foreach (var toggle in FindObjectsByType<ToggleSettingHandler>(FindObjectsSortMode.None))
            toggle.RefreshUI();
    }
}