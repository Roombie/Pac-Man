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

    private Queue<AudioSource> sfxPool;
    private AudioSource musicSource;
    private readonly List<AudioSource> pausedSources = new();
    private readonly Dictionary<AudioClip, AudioSource> activeSounds = new();

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
        ApplySavedVolumes();
    }

    private void InitializePools()
    {
        sfxPool = new Queue<AudioSource>();
        for (int i = 0; i < poolSize; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            sfxPool.Enqueue(source);
        }
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
            float volume = GetVolume(type, 1f);
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
        float dB = Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f)) * 20f;
        mixer.SetFloat(param, dB);

        PlayerPrefs.SetFloat(SettingsKeys.Get(type), volume);
        PlayerPrefs.Save();
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

        // Only auto-return one-shots. Loops manage their own lifetime.
        if (category == SoundCategory.SFX && !loop)
            StartCoroutine(ReturnToPoolAfterPlayback(source, clip.length / Mathf.Abs(pitch)));
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
        var toRemove = new List<AudioClip>();

        foreach (var (clip, src) in activeSounds)
        {
            if (src != null && GetCategory(src.outputAudioMixerGroup) == category)
            {
                src.Stop();
                toRemove.Add(clip);
            }
        }

        foreach (var clip in toRemove)
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
            }

            return musicSource;
        }

        AudioSource source = (category == SoundCategory.SFX && sfxPool.Count > 0)
            ? sfxPool.Dequeue()
            : gameObject.AddComponent<AudioSource>();

        source.outputAudioMixerGroup = category switch
        {
            _ => sfxMixerGroup
        };

        source.playOnAwake = false;
        return source;
    }

    public void Stop(AudioClip clip)
    {
        if (clip == null || !activeSounds.ContainsKey(clip))
        {
            return;
        }

        AudioSource source = activeSounds[clip];
        if (source != null)
        {
            source.Stop();
        }

        activeSounds.Remove(clip);
    }


    public bool IsPlaying(AudioClip clip)
        => System.Array.Exists(GetComponents<AudioSource>(), src => src.clip == clip && src.isPlaying);

    public void PauseAll() => PauseSources(GetComponents<AudioSource>());
    public void ResumeAll() => ResumeSources(pausedSources);

    public void PauseCategory(SoundCategory category)
    {
        foreach (var source in GetComponents<AudioSource>())
        {
            if (source.isPlaying && GetCategory(source) == category)
            {
                source.Pause();
                if (!pausedSources.Contains(source))
                    pausedSources.Add(source);
            }
        }
    }

    public void ResumeCategory(SoundCategory category)
    {
        foreach (var source in pausedSources)
        {
            if (source != null && GetCategory(source) == category)
                source.UnPause();
        }
    }

    public void StopAll()
    {
        foreach (var source in GetComponents<AudioSource>())
        {
            source.Stop();
            if (!sfxPool.Contains(source) && source != musicSource)
                sfxPool.Enqueue(source);
        }
        // keep state consistent for music
        if (musicSource != null) musicSource.clip = null;
        CurrentMusic = null;
    }

    private IEnumerator ReturnToPoolAfterPlayback(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.Stop();
        sfxPool.Enqueue(source);
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
            if (source.isPlaying)
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