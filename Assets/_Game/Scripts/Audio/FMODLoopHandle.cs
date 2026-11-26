using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Walida.Audio
{
    /// <summary>
    /// Managed wrapper around an <see cref="EventInstance"/> for loops, beds, and controllable sounds.
    /// Automatically handles smooth volume fades, stereo panning via the event's <see cref="ChannelGroup"/>,
    /// and seamless restart for FMOD events exported as one-shot timelines without an internal loop region.
    /// </summary>
    public sealed class FMODLoopHandle
    {
        private EventInstance _instance;
        private string _eventPath;
        private EventReference _eventRef;
        private bool _useEventRef;

        private float _currentVolume;
        private float _targetVolume;
        private float _fadeSpeed;
        private float _pan;
        private float _pitch = 1f;
        private bool _hasPanOverride;
        private bool _hasPitchOverride;
        private bool _autoRestartLoop;
        private bool _stopping;
        private bool _isPaused;
        private bool _isActive;

        public bool IsActive => _isActive && !_stopping;
        public bool IsPlayingOrFading => _instance.isValid();
        public string EventPath => _eventPath;
        public float CurrentVolume => _currentVolume;
        public float TargetVolume => _targetVolume;

        public bool Start(string pathOrId, float targetVolume = 1f, float fadeInSeconds = 0f, bool autoRestartLoop = true)
        {
            string resolved = FMODEvents.Resolve(pathOrId);
            if (string.IsNullOrEmpty(resolved))
                return false;

            if (_instance.isValid() && !_useEventRef && _eventPath == resolved)
            {
                _stopping = false;
                _isActive = true;
                _autoRestartLoop = autoRestartLoop;
                SetVolume(targetVolume, fadeInSeconds);
                EnsureStarted();
                return true;
            }

            ReleaseImmediate();

            if (!FMODAudioManager.TryCreateInstance(resolved, out _instance))
                return false;

            _eventPath = resolved;
            _useEventRef = false;
            BeginPlayback(targetVolume, fadeInSeconds, autoRestartLoop);
            return true;
        }

        public bool Start(EventReference eventRef, string fallbackPathOrId, float targetVolume = 1f, float fadeInSeconds = 0f, bool autoRestartLoop = true)
        {
            if (eventRef.IsNull)
                return Start(fallbackPathOrId, targetVolume, fadeInSeconds, autoRestartLoop);

            if (_instance.isValid() && _useEventRef && _eventRef.Guid == eventRef.Guid)
            {
                _stopping = false;
                _isActive = true;
                _autoRestartLoop = autoRestartLoop;
                SetVolume(targetVolume, fadeInSeconds);
                EnsureStarted();
                return true;
            }

            ReleaseImmediate();

            if (!FMODAudioManager.TryCreateInstance(eventRef, out _instance))
                return Start(fallbackPathOrId, targetVolume, fadeInSeconds, autoRestartLoop);

            _eventRef = eventRef;
            _eventPath = eventRef.ToString();
            _useEventRef = true;
            BeginPlayback(targetVolume, fadeInSeconds, autoRestartLoop);
            return true;
        }

        private void BeginPlayback(float targetVolume, float fadeInSeconds, bool autoRestartLoop)
        {
            _autoRestartLoop = autoRestartLoop;
            _stopping = false;
            _isActive = true;
            _isPaused = false;

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

            _instance.setVolume(_currentVolume);
            if (_hasPitchOverride)
                _instance.setPitch(_pitch);

            _instance.start();
            ApplyPanIfReady();
        }

        public void SetVolume(float volume, float fadeSeconds = 0f)
        {
            _targetVolume = Mathf.Clamp01(volume);
            if (fadeSeconds <= 0.001f)
            {
                _currentVolume = _targetVolume;
                _fadeSpeed = 0f;
                if (_instance.isValid())
                    _instance.setVolume(_currentVolume);
            }
            else
            {
                float diff = Mathf.Abs(_targetVolume - _currentVolume);
                _fadeSpeed = diff > 0.0001f ? diff / fadeSeconds : 0f;
            }
        }

        public void SetVolumeImmediate(float volume)
        {
            SetVolume(volume, 0f);
        }

        public void SetPan(float pan)
        {
            _pan = Mathf.Clamp(pan, -1f, 1f);
            _hasPanOverride = true;
            ApplyPanIfReady();
        }

        public void SetPitch(float pitch)
        {
            _pitch = Mathf.Clamp(pitch, 0.25f, 4f);
            _hasPitchOverride = true;
            if (_instance.isValid())
                _instance.setPitch(_pitch);
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (_instance.isValid())
                _instance.setPaused(paused);
        }

        public void Stop(float fadeOutSeconds = 0.25f)
        {
            if (!_instance.isValid())
            {
                _isActive = false;
                _stopping = false;
                return;
            }

            if (fadeOutSeconds <= 0.001f || _currentVolume <= 0.001f)
            {
                ReleaseImmediate();
                return;
            }

            _stopping = true;
            _isActive = false;
            _targetVolume = 0f;
            _fadeSpeed = Mathf.Max(0.01f, _currentVolume / fadeOutSeconds);
        }

        public void ReleaseImmediate()
        {
            _isActive = false;
            _stopping = false;
            _isPaused = false;
            _currentVolume = 0f;
            _targetVolume = 0f;
            _fadeSpeed = 0f;

            if (_instance.isValid())
            {
                _instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                _instance.release();
                _instance.clearHandle();
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_instance.isValid())
                return;

            if (_fadeSpeed > 0f && !Mathf.Approximately(_currentVolume, _targetVolume))
            {
                _currentVolume = Mathf.MoveTowards(_currentVolume, _targetVolume, _fadeSpeed * deltaTime);
                _instance.setVolume(_currentVolume);
            }

            if (_stopping)
            {
                if (_currentVolume <= 0.001f)
                {
                    ReleaseImmediate();
                }
                return;
            }

            if (!_isPaused && _isActive)
            {
                if (_autoRestartLoop)
                {
                    if (_instance.getPlaybackState(out PLAYBACK_STATE state) == RESULT.OK &&
                        state == PLAYBACK_STATE.STOPPED)
                    {
                        _instance.start();
                    }
                }

                if (_hasPanOverride)
                    ApplyPanIfReady();
            }
        }

        private void EnsureStarted()
        {
            if (!_instance.isValid())
                return;

            if (_instance.getPlaybackState(out PLAYBACK_STATE state) == RESULT.OK &&
                (state == PLAYBACK_STATE.STOPPED || state == PLAYBACK_STATE.STOPPING))
            {
                _instance.start();
            }
        }

        private void ApplyPanIfReady()
        {
            if (!_hasPanOverride || !_instance.isValid())
                return;

            if (_instance.getChannelGroup(out ChannelGroup group) == RESULT.OK)
            {
                group.setPan(_pan);
            }
        }
    }
}
