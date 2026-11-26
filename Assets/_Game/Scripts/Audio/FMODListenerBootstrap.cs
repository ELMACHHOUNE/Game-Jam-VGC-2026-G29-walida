using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Walida.Audio
{
    /// <summary>
    /// Makes sure every scene has an FMOD <see cref="StudioListener"/> so 3D FMOD events are heard,
    /// without having to edit each scene. The listener is added next to the scene's Unity AudioListener
    /// (usually the main camera). The Unity AudioListener is kept, because the existing
    /// AudioSource-based systems (VN audio, audio recording minigame) still rely on it.
    /// </summary>
    internal static class FMODListenerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureListener();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureListener();

        private static void EnsureListener()
        {
            if (Object.FindAnyObjectByType<StudioListener>() != null)
                return;

            GameObject host = null;
            var unityListener = Object.FindAnyObjectByType<AudioListener>();
            if (unityListener != null)
                host = unityListener.gameObject;
            else if (Camera.main != null)
                host = Camera.main.gameObject;

            if (host != null)
                host.AddComponent<StudioListener>();
        }
    }
}
