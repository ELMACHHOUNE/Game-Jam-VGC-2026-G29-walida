using System;
using System.Collections.Generic;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Walida.Audio
{
    /// <summary>
    /// Central FMOD audio runtime for L'Walida. Auto-bootstraps before scene load, manages
    /// one-shot playback with volume/pitch control, ticks all registered <see cref="FMODLoopHandle"/>
    /// instances, and drives scene-level ambience beds across all 4 scenes.
    /// </summary>
    public sealed class FMODAudioManager : MonoBehaviour
    {
        private static FMODAudioManager _instance;
        private static readonly HashSet<string> LoggedMissingEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<FMODLoopHandle> _managedLoops = new List<FMODLoopHandle>(16);
        private readonly FMODLoopHandle _sceneAmbiencePrimary = new FMODLoopHandle();
        private readonly FMODLoopHandle _sceneAmbienceSecondary = new FMODLoopHandle();
        private bool _vnBgmActive;

        public static FMODAudioManager Instance
        {
            get
            {
                if (_instance == null && Application.isPlaying)
                    Bootstrap();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var go = new GameObject("[FMODAudioManager]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FMODAudioManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private void Start()
        {
            ApplySceneAmbience(SceneManager.GetActiveScene().name);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _sceneAmbiencePrimary.Tick(dt);
            _sceneAmbienceSecondary.Tick(dt);

            for (int i = _managedLoops.Count - 1; i >= 0; i--)
            {
                FMODLoopHandle handle = _managedLoops[i];
                if (handle == null)
                {
                    _managedLoops.RemoveAt(i);
                    continue;
                }

                handle.Tick(dt);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            StopAllManagedLoops();
        }

        private void OnApplicationQuit()
        {
            StopAllManagedLoops();
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            _vnBgmActive = false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                _vnBgmActive = false;
                ApplySceneAmbience(scene.name);
            }
        }

        /// <summary>
        /// Sets the default ambient FMOD bed for each scene in Build Settings.
        /// </summary>
        private void ApplySceneAmbience(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return;

            if (string.Equals(sceneName, "MedinaMainLevel", StringComparison.OrdinalIgnoreCase))
            {
                _sceneAmbiencePrimary.Start(FMODEvents.NeighborhoodLoop, targetVolume: 0.28f, fadeInSeconds: 0.9f, autoRestartLoop: true);
                _sceneAmbienceSecondary.Stop(0.5f);
            }
            else if (string.Equals(sceneName, "PhotographingMinigame", StringComparison.OrdinalIgnoreCase))
            {
                _sceneAmbiencePrimary.Start(FMODEvents.NeighborhoodLoop, targetVolume: 0.45f, fadeInSeconds: 0.8f, autoRestartLoop: true);
                _sceneAmbienceSecondary.Start(FMODEvents.Pigeons, targetVolume: 0.16f, fadeInSeconds: 1.4f, autoRestartLoop: true);
            }
            else if (string.Equals(sceneName, "AudioRecordingMinigame", StringComparison.OrdinalIgnoreCase))
            {
                _sceneAmbiencePrimary.Start(FMODEvents.SoukLoop, targetVolume: 0.24f, fadeInSeconds: 0.8f, autoRestartLoop: true);
                _sceneAmbienceSecondary.Stop(0.5f);
            }
            else if (string.Equals(sceneName, "Game", StringComparison.OrdinalIgnoreCase))
            {
                if (!_vnBgmActive)
                {
                    _sceneAmbiencePrimary.Start(FMODEvents.QuietRoomTone, targetVolume: 0.30f, fadeInSeconds: 0.9f, autoRestartLoop: true);
                }
                _sceneAmbienceSecondary.Stop(0.5f);
            }
            else
            {
                _sceneAmbiencePrimary.Stop(0.6f);
                _sceneAmbienceSecondary.Stop(0.6f);
            }
        }

        /// <summary>
        /// Called by <c>VNStoryController</c> when Ink starts or stops its own BGM/ambience,
        /// smoothly ducking the default scene room tone so tracks don't clash.
        /// </summary>
        public void SetVNBgmOverrideActive(bool active)
        {
            _vnBgmActive = active;
            if (active)
            {
                _sceneAmbiencePrimary.Stop(0.6f);
                _sceneAmbienceSecondary.Stop(0.6f);
            }
            else if (string.Equals(SceneManager.GetActiveScene().name, "Game", StringComparison.OrdinalIgnoreCase))
            {
                _sceneAmbiencePrimary.Start(FMODEvents.QuietRoomTone, targetVolume: 0.28f, fadeInSeconds: 0.8f, autoRestartLoop: true);
            }
        }

        public void RegisterLoop(FMODLoopHandle handle)
        {
            if (handle != null && !_managedLoops.Contains(handle))
                _managedLoops.Add(handle);
        }

        public void UnregisterLoop(FMODLoopHandle handle, bool releaseImmediate = true)
        {
            if (handle == null)
                return;

            _managedLoops.Remove(handle);
            if (releaseImmediate)
                handle.ReleaseImmediate();
        }

        public FMODLoopHandle CreateManagedLoop()
        {
            var handle = new FMODLoopHandle();
            _managedLoops.Add(handle);
            return handle;
        }

        public static void SetAllPaused(bool paused)
        {
            if (!RuntimeManager.IsInitialized)
                return;

            RuntimeManager.PauseAllEvents(paused);
        }

        /// <summary>
        /// Plays a one-shot FMOD event with optional volume and pitch scaling.
        /// Accepts either an exact <c>event:/...</c> path or a friendly alias.
        /// </summary>
        public static bool PlayOneShot(string idOrPath, float volume = 1f, float pitch = 1f)
        {
            string resolved = FMODEvents.Resolve(idOrPath);
            if (string.IsNullOrEmpty(resolved))
                return false;

            if (!TryCreateInstance(resolved, out EventInstance instance))
                return false;

            instance.setVolume(Mathf.Clamp01(volume));
            if (!Mathf.Approximately(pitch, 1f))
                instance.setPitch(Mathf.Clamp(pitch, 0.25f, 4f));

            instance.start();
            instance.release();
            return true;
        }

        /// <summary>
        /// Plays a one-shot FMOD event using an optional Inspector <see cref="EventReference"/> override,
        /// falling back to <paramref name="fallbackIdOrPath"/> when unassigned.
        /// </summary>
        public static bool PlayOneShot(EventReference eventRef, string fallbackIdOrPath, float volume = 1f, float pitch = 1f)
        {
            if (eventRef.IsNull)
                return PlayOneShot(fallbackIdOrPath, volume, pitch);

            if (!TryCreateInstance(eventRef, out EventInstance instance))
                return PlayOneShot(fallbackIdOrPath, volume, pitch);

            instance.setVolume(Mathf.Clamp01(volume));
            if (!Mathf.Approximately(pitch, 1f))
                instance.setPitch(Mathf.Clamp(pitch, 0.25f, 4f));

            instance.start();
            instance.release();
            return true;
        }

        public static bool TryCreateInstance(string pathOrId, out EventInstance instance)
        {
            instance = default;
            if (!Application.isPlaying)
                return false;

            string resolved = FMODEvents.Resolve(pathOrId);
            if (string.IsNullOrEmpty(resolved))
                return false;

            try
            {
                instance = RuntimeManager.CreateInstance(resolved);
                return instance.isValid();
            }
            catch (EventNotFoundException)
            {
                if (LoggedMissingEvents.Add(resolved))
                    Debug.LogWarning($"[FMODAudioManager] Event not found in loaded banks: '{resolved}'");
                return false;
            }
            catch (Exception ex)
            {
                if (LoggedMissingEvents.Add(resolved))
                    Debug.LogWarning($"[FMODAudioManager] Could not create FMOD instance for '{resolved}': {ex.Message}");
                return false;
            }
        }

        public static bool TryCreateInstance(EventReference eventRef, out EventInstance instance)
        {
            instance = default;
            if (!Application.isPlaying || eventRef.IsNull)
                return false;

            try
            {
                instance = RuntimeManager.CreateInstance(eventRef);
                return instance.isValid();
            }
            catch (EventNotFoundException)
            {
                string key = eventRef.ToString();
                if (LoggedMissingEvents.Add(key))
                    Debug.LogWarning($"[FMODAudioManager] EventReference not found in loaded banks: '{key}'");
                return false;
            }
            catch (Exception ex)
            {
                string key = eventRef.ToString();
                if (LoggedMissingEvents.Add(key))
                    Debug.LogWarning($"[FMODAudioManager] Could not create FMOD instance for '{key}': {ex.Message}");
                return false;
            }
        }

        private void StopAllManagedLoops()
        {
            _sceneAmbiencePrimary.ReleaseImmediate();
            _sceneAmbienceSecondary.ReleaseImmediate();

            for (int i = 0; i < _managedLoops.Count; i++)
                _managedLoops[i]?.ReleaseImmediate();

            _managedLoops.Clear();
        }
    }
}
