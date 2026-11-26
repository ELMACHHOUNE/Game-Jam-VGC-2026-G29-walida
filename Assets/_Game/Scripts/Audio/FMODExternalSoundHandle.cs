using System;
using System.Collections.Generic;
using System.IO;
using FMOD;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Walida.Audio
{
    /// <summary>
    /// Loads and plays external audio files (<c>.ogg</c>, <c>.mp3</c>, <c>.wav</c>) directly through
    /// FMOD's Core System (<see cref="RuntimeManager.CoreSystem"/>). Works even when Unity's built-in
    /// audio engine is disabled (<c>m_DisableAudio: 1</c>) and requires no FMOD Studio bank rebuild.
    /// Shared <see cref="Sound"/> instances are cached per file so multiple scattered map emitters can
    /// play concurrently with independent volume, pan, pitch, and randomized start offsets.
    /// </summary>
    public static class FMODExternalSoundBank
    {
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly string Path;
            public readonly bool Loop;

            public CacheKey(string path, bool loop)
            {
                Path = path;
                Loop = loop;
            }

            public bool Equals(CacheKey other) => Loop == other.Loop && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => (StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty) * 397) ^ (Loop ? 1 : 0);
        }

        private static readonly Dictionary<CacheKey, Sound> SoundCache = new Dictionary<CacheKey, Sound>();
        private static readonly HashSet<string> LoggedMissingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReleaseAll();
            LoggedMissingFiles.Clear();
        }

        public static string ResolveFilePath(string relativeOrFileName)
        {
            if (string.IsNullOrWhiteSpace(relativeOrFileName))
                return null;

            string trimmed = relativeOrFileName.Trim();
            if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
                return trimmed;

            string inMedinaSfx = Path.Combine(Application.streamingAssetsPath, "MedinaSFX", trimmed);
            if (File.Exists(inMedinaSfx))
                return inMedinaSfx;

            string inStreamingAssets = Path.Combine(Application.streamingAssetsPath, trimmed);
            if (File.Exists(inStreamingAssets))
                return inStreamingAssets;

            string inGameSounds = Path.Combine(Application.dataPath, "_Game", "Media", "Sounds", trimmed);
            if (File.Exists(inGameSounds))
                return inGameSounds;

            return null;
        }

        public static bool TryGetOrLoadSound(string relativeOrFileName, bool loop, out Sound sound)
        {
            sound = default;
            if (!Application.isPlaying || !RuntimeManager.IsInitialized)
                return false;

            string fullPath = ResolveFilePath(relativeOrFileName);
            if (string.IsNullOrEmpty(fullPath))
            {
                if (LoggedMissingFiles.Add(relativeOrFileName ?? string.Empty))
                    Debug.LogWarning($"[FMODExternalSoundBank] Audio file not found in StreamingAssets/MedinaSFX: '{relativeOrFileName}'");
                return false;
            }

            var key = new CacheKey(fullPath, loop);
            if (SoundCache.TryGetValue(key, out Sound cached) && cached.hasHandle())
            {
                sound = cached;
                return true;
            }

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            if (!coreSystem.hasHandle())
                return false;

            MODE baseMode = MODE._2D | (loop ? MODE.LOOP_NORMAL : MODE.LOOP_OFF);
            RESULT result = coreSystem.createSound(fullPath, baseMode | MODE.CREATECOMPRESSEDSAMPLE, out sound);
            if (result != RESULT.OK)
            {
                result = coreSystem.createSound(fullPath, baseMode | MODE.DEFAULT, out sound);
            }

            if (result != RESULT.OK || !sound.hasHandle())
            {
                if (LoggedMissingFiles.Add(fullPath))
                    Debug.LogWarning($"[FMODExternalSoundBank] FMOD failed to load '{relativeOrFileName}' ({result}).");
                return false;
            }

            SoundCache[key] = sound;
            return true;
        }

        public static bool PlayOneShot(string relativeOrFileName, float volume = 1f, float pan = 0f, float pitch = 1f)
        {
            if (!TryGetOrLoadSound(relativeOrFileName, loop: false, out Sound sound))
                return false;

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            coreSystem.getMasterChannelGroup(out ChannelGroup masterGroup);

            RESULT result = coreSystem.playSound(sound, masterGroup, paused: true, out Channel channel);
            if (result != RESULT.OK || !channel.hasHandle())
                return false;

            channel.setVolume(Mathf.Clamp01(volume));
            channel.setPan(Mathf.Clamp(pan, -1f, 1f));
            if (!Mathf.Approximately(pitch, 1f))
                channel.setPitch(Mathf.Clamp(pitch, 0.25f, 4f));

            channel.setPaused(false);
            return true;
        }

        public static void ReleaseAll()
        {
            if (SoundCache.Count == 0)
                return;

            foreach (KeyValuePair<CacheKey, Sound> kvp in SoundCache)
            {
                if (kvp.Value.hasHandle())
                    kvp.Value.release();
            }

            SoundCache.Clear();
        }
    }

    /// <summary>
    /// Managed looping channel handle for an external audio file loaded via <see cref="FMODExternalSoundBank"/>.
    /// Mirrors <see cref="FMODLoopHandle"/> with smooth fading, stereo panning, pitch control,
    /// and randomized playback start offset so scattered instances never phase-align.
    /// </summary>
    public sealed class FMODExternalLoopHandle
    {
        private Channel _channel;
        private string _fileName;

        private float _currentVolume;
        private float _targetVolume;
        private float _fadeSpeed;
        private float _pan;
        private float _pitch = 1f;
        private bool _stopping;
        private bool _isActive;

        public bool IsActive => _isActive && !_stopping;
        public string FileName => _fileName;

        public bool Start(string relativeOrFileName, float targetVolume = 1f, float fadeInSeconds = 0.5f, float pitch = 1f, bool randomizeStartOffset = true)
        {
            if (string.IsNullOrWhiteSpace(relativeOrFileName))
                return false;

            _pitch = Mathf.Clamp(pitch, 0.25f, 4f);

            if (IsChannelPlaying() && string.Equals(_fileName, relativeOrFileName, StringComparison.OrdinalIgnoreCase))
            {
                _stopping = false;
                _isActive = true;
                SetVolume(targetVolume, fadeInSeconds);
                _channel.setPitch(_pitch);
                return true;
            }

            StopImmediate();

            if (!FMODExternalSoundBank.TryGetOrLoadSound(relativeOrFileName, loop: true, out Sound sound))
                return false;

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            coreSystem.getMasterChannelGroup(out ChannelGroup masterGroup);

            RESULT result = coreSystem.playSound(sound, masterGroup, paused: true, out _channel);
            if (result != RESULT.OK || !_channel.hasHandle())
                return false;

            _fileName = relativeOrFileName;
            _stopping = false;
            _isActive = true;
            _targetVolume = Mathf.Clamp01(targetVolume);

            if (fadeInSeconds > 0.001f)
            {
                _currentVolume = 0f;
                _fadeSpeed = Mathf.Max(0.01f, _targetVolume / fadeInSeconds);
            }
            else
            {
                _currentVolume = _targetVolume;
                _fadeSpeed = 0f;
            }

            if (randomizeStartOffset && sound.getLength(out uint lengthMs, TIMEUNIT.MS) == RESULT.OK && lengthMs > 500)
            {
                uint offsetMs = (uint)UnityEngine.Random.Range(0, (int)(lengthMs - 200));
                _channel.setPosition(offsetMs, TIMEUNIT.MS);
            }

            _channel.setVolume(_currentVolume);
            _channel.setPan(_pan);
            _channel.setPitch(_pitch);
            _channel.setPaused(false);
            return true;
        }

        public void SetVolume(float volume, float fadeSeconds = 0f)
        {
            _targetVolume = Mathf.Clamp01(volume);
            if (fadeSeconds <= 0.001f)
            {
                _currentVolume = _targetVolume;
                _fadeSpeed = 0f;
                if (IsChannelPlaying())
                    _channel.setVolume(_currentVolume);
            }
            else
            {
                float diff = Mathf.Abs(_targetVolume - _currentVolume);
                _fadeSpeed = diff > 0.0001f ? diff / fadeSeconds : 0f;
            }
        }

        public void SetVolumeImmediate(float volume) => SetVolume(volume, 0f);

        public void SetPan(float pan)
        {
            _pan = Mathf.Clamp(pan, -1f, 1f);
            if (IsChannelPlaying())
                _channel.setPan(_pan);
        }

        public void SetPitch(float pitch)
        {
            _pitch = Mathf.Clamp(pitch, 0.25f, 4f);
            if (IsChannelPlaying())
                _channel.setPitch(_pitch);
        }

        public void Stop(float fadeOutSeconds = 0.4f)
        {
            if (!IsChannelPlaying())
            {
                _isActive = false;
                _stopping = false;
                _channel.clearHandle();
                return;
            }

            if (fadeOutSeconds <= 0.001f || _currentVolume <= 0.001f)
            {
                StopImmediate();
                return;
            }

            _stopping = true;
            _isActive = false;
            _targetVolume = 0f;
            _fadeSpeed = Mathf.Max(0.01f, _currentVolume / fadeOutSeconds);
        }

        public void StopImmediate()
        {
            _isActive = false;
            _stopping = false;
            _currentVolume = 0f;
            _targetVolume = 0f;
            _fadeSpeed = 0f;

            if (_channel.hasHandle())
            {
                _channel.stop();
                _channel.clearHandle();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_channel.hasHandle())
                return;

            if (!IsChannelPlaying())
            {
                _channel.clearHandle();
                _isActive = false;
                _stopping = false;
                return;
            }

            if (_fadeSpeed > 0f && !Mathf.Approximately(_currentVolume, _targetVolume))
            {
                _currentVolume = Mathf.MoveTowards(_currentVolume, _targetVolume, _fadeSpeed * deltaTime);
                _channel.setVolume(_currentVolume);
            }

            if (_stopping && _currentVolume <= 0.001f)
            {
                StopImmediate();
            }
        }

        private bool IsChannelPlaying()
        {
            return _channel.hasHandle() && _channel.isPlaying(out bool playing) == RESULT.OK && playing;
        }
    }
}
