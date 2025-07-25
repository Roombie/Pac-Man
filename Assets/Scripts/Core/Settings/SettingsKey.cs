using System.Collections.Generic;

public enum SettingType
{
    MusicVolumeKey,
    SoundVolumeKey,
    ShowIndicatorKey,
    FullscreenKey,
    PacmanLivesKey,
    ExtraKey
}

public static class SettingsKeys
{
    private static readonly Dictionary<SettingType, string> keys = new()
    {
        { SettingType.MusicVolumeKey, MusicVolumeKey },
        { SettingType.SoundVolumeKey, SoundVolumeKey },
        { SettingType.ShowIndicatorKey, ShowIndicatorKey },
        { SettingType.FullscreenKey, FullscreenKey },
        { SettingType.PacmanLivesKey, PacmanLivesKey },
        { SettingType.ExtraKey, ExtraKey },
    };

    public static string Get(SettingType type) => keys.TryGetValue(type, out var value) ? value : type.ToString();

    // Audio
    public const string MusicVolumeKey = "MusicVolume";
    public const string SoundVolumeKey = "SoundVolume";

    // General
    public const string ShowIndicatorKey = "ShowIndicator";
    public const string FullscreenKey = "Fullscreen";
    public const string PacmanLivesKey = "PacmanLives";
    public const string ExtraKey = "Extra";
}