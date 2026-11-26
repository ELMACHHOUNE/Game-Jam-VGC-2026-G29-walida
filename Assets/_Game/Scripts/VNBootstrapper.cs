using UnityEngine;

public static class VNBootstrapper
{
    private static bool _initialized;

    // With domain reload disabled (Enter Play Mode Options) statics survive between play sessions.
    // Reset so every play session bootstraps exactly like a build does.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _initialized = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_initialized) return;
        _initialized = true;

        var globalRoot = new GameObject("[VN_GlobalServices]");
        Object.DontDestroyOnLoad(globalRoot);

        globalRoot.AddComponent<VNSaveManager>();
        globalRoot.AddComponent<VNSettingsManager>();
    }
}
