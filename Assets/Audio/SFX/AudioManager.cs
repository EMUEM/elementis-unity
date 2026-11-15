using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Elementis SFX front-end (Assets/Audio/SFX/).
///
/// Contract for the combat system and the UI:
///   AudioManager.Play("water_splash");            // by clip name (file name, no extension)
///   AudioManager.Play("fire_whoosh", 0.8f, 1.05f); // volume 0..1, pitch multiplier
///   AudioManager.Muted = true;                    // mute toggle the UI calls; persisted in PlayerPrefs
///   AudioManager.HasClip("lightning_zap");        // true once the clips are wired
///
/// Wiring: put the AudioManagerSetup component on a GameObject in the fight scene
/// (or any scene that plays SFX) and drag the 18 WAVs from Assets/Audio/SFX/
/// into its Clips array. Setup runs in Awake and registers the clips by name.
/// If nothing is wired, Play() logs one warning and does nothing (never throws),
/// so combat code can call it unconditionally from day one.
///
/// All clips are synthesised locally (numpy -> WAV, 44.1 kHz mono, no downloads,
/// no copyrighted audio). See Assets/VFX/VFX-NOTES.md for the full SFX list.
/// </summary>
public static class AudioManager
{
    public const string MutedPlayerPrefsKey = "elementis_muted";
    private const int PoolSize = 8;

    private static readonly Dictionary<string, AudioClip> Clips =
        new Dictionary<string, AudioClip>();

    private static readonly List<AudioSource> Pool = new List<AudioSource>();
    private static GameObject _host;
    private static bool _warned;
    private static bool _muted; // loaded from PlayerPrefs on first access
    private static bool _mutedLoaded;

    /// <summary>Master SFX mute. Persisted to PlayerPrefs ("elementis_muted").</summary>
    public static bool Muted
    {
        get
        {
            if (!_mutedLoaded)
            {
                _muted = PlayerPrefs.GetInt(MutedPlayerPrefsKey, 0) == 1;
                _mutedLoaded = true;
            }
            return _muted;
        }
        set
        {
            _muted = value;
            _mutedLoaded = true;
            PlayerPrefs.SetInt(MutedPlayerPrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMute();
        }
    }

    /// <summary>Register clips (called by AudioManagerSetup.Awake). Re-registering replaces by name.</summary>
    public static void Initialize(IEnumerable<AudioClip> clips, Transform parent = null)
    {
        if (clips == null) return;
        foreach (var clip in clips)
        {
            if (clip != null) Clips[clip.name] = clip;
        }
        EnsureHost(parent);
        ApplyMute();
    }

    public static bool HasClip(string clipName)
    {
        return !string.IsNullOrEmpty(clipName) && Clips.ContainsKey(clipName);
    }

    /// <summary>Play a registered clip by name. Safe to call before wiring (no-op + one warning).</summary>
    public static void Play(string clipName, float volume = 1f, float pitch = 1f)
    {
        if (Muted) return;
        AudioClip clip;
        if (string.IsNullOrEmpty(clipName) || !Clips.TryGetValue(clipName, out clip) || clip == null)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[AudioManager] clip not registered (is AudioManagerSetup wired?): " + clipName);
            }
            return;
        }
        var src = GetSource();
        if (src == null) return;
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private static void EnsureHost(Transform parent)
    {
        if (_host != null) return;
        _host = parent != null ? parent.gameObject : new GameObject("AudioManager");
        if (parent == null) Object.DontDestroyOnLoad(_host);
        for (var i = 0; i < PoolSize; i++)
        {
            var src = _host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D SFX
            Pool.Add(src);
        }
    }

    private static AudioSource GetSource()
    {
        EnsureHost(null);
        foreach (var src in Pool)
        {
            if (!src.isPlaying) return src;
        }
        return Pool.Count > 0 ? Pool[0] : null; // pool busy: steal the oldest voice, never drop the call
    }

    private static void ApplyMute()
    {
        if (_host == null) return;
        foreach (var src in Pool) src.mute = _muted;
    }
}

/// <summary>
/// Wiring component: drop on a GameObject in the fight scene, drag the
/// Assets/Audio/SFX/*.wav clips into Clips, done. Also exposes the mute
/// toggle for the settings UI (Mute checkbox -> AudioManager.Muted).
/// </summary>
public class AudioManagerSetup : MonoBehaviour
{
    [Tooltip("Drag all clips from Assets/Audio/SFX here. Registered by file name (no extension).")]
    public AudioClip[] Clips;

    [Tooltip("Start muted (e.g. restored from a settings screen).")]
    public bool StartMuted;

    private void Awake()
    {
        if (StartMuted) AudioManager.Muted = true;
        AudioManager.Initialize(Clips, transform);
    }
}
