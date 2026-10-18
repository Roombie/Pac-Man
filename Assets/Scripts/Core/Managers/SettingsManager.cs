using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Linq;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // The saved language is picked at startup by the Player Pref Locale Selector in Localization Settings
        // (key "LanguageKey"); the locale list isn't loaded yet here, so it can't be set from Awake

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        // Ensure localization is fully initialized, then apply scene-level settings once.
        StartCoroutine(InitializeAndApplySettings());
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-apply runtime settings on each scene load
        ApplyAllFromPrefs();
        RefreshSettingsUI();
    }

    private IEnumerator InitializeAndApplySettings()
    {
        // Finish localization bootstrapping (tables/assets ready)
        yield return LocalizationSettings.InitializationOperation;

        // Apply the rest of the settings (volumes, fullscreen, gameplay flags)
        ApplyAllFromPrefs();
        RefreshSettingsUI();
    }

    /// <summary>Apply all persisted settings into the current scene.</summary>
    public void ApplyAllFromPrefs()
    {
        // Fullscreen
        // Phones are always fullscreen (the option is hidden there)
        // Browsers only allow fullscreen from a click/keypress, so on web it's only set from the options toggle
        if (Application.platform != RuntimePlatform.WebGLPlayer)
            Screen.fullScreen = TouchInput.IsMobile
                || PlayerPrefs.GetInt(SettingsKeys.FullscreenKey, 1) == 1;

        // Volumes
        float music = PlayerPrefs.GetFloat(SettingsKeys.MusicVolumeKey, 0.6f);
        float sfx = PlayerPrefs.GetFloat(SettingsKeys.SoundVolumeKey, 0.6f);
        AudioManager.Instance?.SetVolume(SettingType.MusicVolumeKey, music);
        AudioManager.Instance?.SetVolume(SettingType.SoundVolumeKey, sfx);

        // Gameplay
        bool showIndicator = PlayerPrefs.GetInt(SettingsKeys.ShowIndicatorKey, 1) == 1;
        foreach (var p in FindObjectsByType<Pacman>(FindObjectsSortMode.None))
            p.UpdateIndicatorVisibility(showIndicator);

        GhostTargetMarker.Visible = PlayerPrefs.GetInt(SettingsKeys.ShowGhostTargetsKey, 0) == 1;
    }

    public static void Apply(SettingType type, bool on) => Apply(type, on ? 1 : 0);
    public static void Apply(SettingType type, int index)
    {
        if (Instance == null) return;
        Instance.ApplySetting(type, index);
    }

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
                // Persist and apply immediately; wait for init only if needed.
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

            case SettingType.ShowGhostTargetsKey:
            {
                bool on = (index == 1);
                PlayerPrefs.SetInt(SettingsKeys.ShowGhostTargetsKey, on ? 1 : 0);
                GhostTargetMarker.Visible = on;
                break;
            }
        }

        PlayerPrefs.Save();
    }

    private IEnumerator SetLanguageAsync(Locale locale)
    {
        // Switch immediately so UI updates right away.
        LocalizationSettings.SelectedLocale = locale;

        // If initialization isn't finished yet, wait; then refresh controls.
        var op = LocalizationSettings.InitializationOperation;
        if (!op.IsDone)
            yield return op;

        RefreshSettingsUI();
    }

    private void RefreshSettingsUI()
    {
        foreach (var selector in FindObjectsByType<OptionSelectorSettingHandler>(FindObjectsSortMode.None))
            selector.RefreshUI();
        foreach (var toggle in FindObjectsByType<ToggleSettingHandler>(FindObjectsSortMode.None))
            toggle.RefreshUI();
    }
}