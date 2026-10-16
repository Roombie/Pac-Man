using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    public AudioMixer mixer;
    public AudioMixerGroup sfxMixerGroup;
    public AudioMixerGroup musicMixerGroup;

    public int poolSize = 10;

    [Tooltip("Mixer level at 100% volume. Below 0 dB leaves headroom so a full slider isn't full scale")]
    [Range(-30f, 0f)] public float maxVolumeDb = -12f;

    [Header("Music Clips")]
    public AudioClip gameMusic;
    public AudioClip zeroSirenLoop;
    public AudioClip firstSirenLoop;
    public AudioClip secondSirenLoop;
    public AudioClip thirdSirenLoop;
    public AudioClip frightenedMusic;

    [Header("SFX Clips")]
    public AudioClip credit;
    public AudioClip pelletEatenSound1;
    public AudioClip pelletEatenSound2;
    public AudioClip fruitEaten;
    public AudioClip ghostEaten;
    public AudioClip pacmanDeath;
    public AudioClip eyes;
    public AudioClip extend;
    public AudioClip creditReverse;

    // SFX pool: a source is free when it's neither playing nor paused, so one-shots need no
    // coroutine to hand them back and a source can never be handed out twice
    private readonly List<AudioSource> sfxSources = new();
    private AudioSource[] allSources = System.Array.Empty<AudioSource>(); // every AudioSource on this object
    private AudioSource musicSource;
    private readonly List<AudioSource> pausedSources = new();
    private readonly Dictionary<AudioClip, AudioSource> activeSounds = new();
    private readonly List<AudioClip> clipsToRemove = new(); // reused by StopCategory

    public AudioClip CurrentMusic { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializePools();
    }

    // AudioMixer.SetFloat is ignored during Awake, so volumes are applied here
    private void Start() => ApplySavedVolumes();

    private void InitializePools()
    {
        for (int i = 0; i < poolSize; i++)
            CreateSfxSource();
    }

    private AudioSource CreateSfxSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = sfxMixerGroup;
        sfxSources.Add(source);
        allSources = GetComponents<AudioSource>();
        return source;
    }

    private void ApplySavedVolumes()
    {
        var audioTypes = new SettingType[]
        {
            SettingType.MusicVolumeKey,
            SettingType.SoundVolumeKey,
        };

        foreach (SettingType type in audioTypes)
        {
            float volume = GetVolume(type, 0.6f);
            SetVolume(type, volume);
        }
    }

    public void SetVolume(SettingType type, float volume)
    {
        string param = type switch
        {
            //  SettingType    =>    AudioMixers Exposed parameters
            SettingType.MusicVolumeKey => "MusicVolume",
            SettingType.SoundVolumeKey => "SoundVolume",
            _ => null
        };

        if (string.IsNullOrEmpty(param))
        {
            Debug.LogWarning($"[AudioManager] Mixer param not found for {type}");
            return;
        }

        // Vout = voltage, Vin = input voltage
        // Standard formula dB = 20 dB × log10(Vout / Vin)
        float dB = Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f)) * 20f + maxVolumeDb;
        mixer.SetFloat(param, dB);

        // Written to disk by SettingsManager after each change (and by Unity on quit), not on every slider tick
        PlayerPrefs.SetFloat(SettingsKeys.Get(type), volume);
    }

    public float GetVolume(SettingType type, float fallback = 1f)
        => PlayerPrefs.GetFloat(SettingsKeys.Get(type), fallback);

    public void Play(AudioClip clip, SoundCategory category = SoundCategory.SFX, float volume = 1f, float pitch = 1f, bool loop = false)
    {
        if (clip == null) return;

        AudioSource source = GetAudioSource(category);
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.loop = loop;
        source.Play();

        activeSounds[clip] = source;

        if (category == SoundCategory.Music)
            CurrentMusic = clip;
    }

    public void PlayOnce(AudioClip clip, SoundCategory category = SoundCategory.SFX, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        if (!IsPlaying(clip))
            Play(clip, category, volume, pitch);
    }

    public void PlayOrReplace(AudioClip clip, SoundCategory category, bool loop = false)
    {
        if (clip == null) return;

        // Stop any other clip currently playing in this category (optional)
        StopCategory(category);

        Play(clip, category, 1f, 1f, loop);
    }

    public void StopCategory(SoundCategory category)
    {
        // Stop all the clips registered on the selected category
        clipsToRemove.Clear();

        foreach (var (clip, src) in activeSounds)
        {
            if (src != null && GetCategory(src.outputAudioMixerGroup) == category)
            {
                src.Stop();
                clipsToRemove.Add(clip);
            }
        }

        foreach (var clip in clipsToRemove)
        {
            activeSounds.Remove(clip);
        }

        // Force musicSource off
        if (category == SoundCategory.Music && musicSource != null && musicSource.isPlaying)
        {
            musicSource.Stop();

            // Also delete the registered clip
            if (musicSource.clip != null && activeSounds.ContainsKey(musicSource.clip))
                activeSounds.Remove(musicSource.clip);

            CurrentMusic = null;
        }
    }

    private SoundCategory GetCategory(AudioMixerGroup group)
    {
        if (group == musicMixerGroup) return SoundCategory.Music;
        return SoundCategory.SFX;
    }

    private AudioSource GetAudioSource(SoundCategory category)
    {
        if (category == SoundCategory.Music)
        {
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.outputAudioMixerGroup = musicMixerGroup;
                allSources = GetComponents<AudioSource>();
            }

            return musicSource;
        }

        for (int i = 0; i < sfxSources.Count; i++)
        {
            var source = sfxSources[i];
            if (source != null && !source.isPlaying && !pausedSources.Contains(source))
            {
                source.outputAudioMixerGroup = sfxMixerGroup;
                return source;
            }
        }

        // Everything busy: grow the pool (it settles at the peak number of overlapping sounds)
        return CreateSfxSource();
    }

    public void Stop(AudioClip clip)
    {
        if (clip == null || !activeSounds.ContainsKey(clip))
        {
            return;
        }

        AudioSource source = activeSounds[clip];
        if (source != null && source.clip == clip) // the source may have been reused for another clip since
        {
            source.Stop();
        }

        activeSounds.Remove(clip);
    }


    public bool IsPlaying(AudioClip clip)
    {
        foreach (var src in allSources)
            if (src != null && src.clip == clip && src.isPlaying) return true;
        return false;
    }

    public void PauseAll() => PauseSources(allSources);
    public void ResumeAll() => ResumeSources(pausedSources);

    public void PauseCategory(SoundCategory category)
    {
        foreach (var source in allSources)
        {
            if (source != null && source.isPlaying && GetCategory(source) == category)
            {
                source.Pause();
                if (!pausedSources.Contains(source))
                    pausedSources.Add(source);
            }
        }
    }

    public void ResumeCategory(SoundCategory category)
    {
        for (int i = pausedSources.Count - 1; i >= 0; i--)
        {
            var source = pausedSources[i];
            if (source == null) { pausedSources.RemoveAt(i); continue; }
            if (GetCategory(source) != category) continue;

            source.UnPause();
            pausedSources.RemoveAt(i); // resumed sources must be free for the pool again
        }
    }

    public void StopAll()
    {
        foreach (var source in allSources)
            if (source != null) source.Stop();

        // Stopped sources are free again; nothing left to resume
        pausedSources.Clear();
        activeSounds.Clear();

        // keep state consistent for music
        if (musicSource != null) musicSource.clip = null;
        CurrentMusic = null;
    }

    public void PlayBackgroundMusic(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = true)
    {
        if (musicSource != null && musicSource.isPlaying)
            StartCoroutine(FadeOutMusic(musicSource, 1f));

        Play(clip, SoundCategory.Music, volume, pitch, loop);
    }

    private IEnumerator FadeOutMusic(AudioSource source, float duration)
    {
        float startVolume = source.volume;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            source.volume = Mathf.Lerp(startVolume, 0, t / duration);
            yield return null;
        }
        source.volume = 0;
        source.Stop();
    }

    private void PauseSources(AudioSource[] sources)
    {
        foreach (var source in sources)
        {
            if (source != null && source.isPlaying)
            {
                source.Pause();
                if (!pausedSources.Contains(source))
                    pausedSources.Add(source);
            }
        }
    }

    private void ResumeSources(List<AudioSource> sources)
    {
        foreach (var source in sources)
        {
            if (source != null)
                source.UnPause();
        }
        sources.Clear();
    }

    private SoundCategory GetCategory(AudioSource source)
    {
        if (source.outputAudioMixerGroup == musicMixerGroup) return SoundCategory.Music;
        return SoundCategory.SFX;
    }
    
    public void PlayPelletEatenSound(bool alternate)
    {
        AudioClip clip = alternate ? pelletEatenSound1 : pelletEatenSound2;
        Play(clip, SoundCategory.SFX);
    }
}

public enum SoundCategory
{
    SFX,
    Music,
}