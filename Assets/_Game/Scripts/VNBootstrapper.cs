using UnityEngine;

public static class VNBootstrapper
{
    private static bool _initialized;

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