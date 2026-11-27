using System.IO;
using UnityEngine;

public enum VNLaunchMode
{
    NewGame,
    ContinueLatest,
    LoadSpecificSlot,
    /// <summary>Coming back from a minigame scene: restore the medina from MinigameSession's in-memory snapshot.</summary>
    ReturnFromMinigame
}

public static class VNSessionBridge
{
    public static VNLaunchMode LaunchMode { get; set; } = VNLaunchMode.NewGame;
    public static string TargetSlotId { get; set; } = string.Empty;
    public static bool HasPlayedBootSplash { get; set; }
    public static string CachedInkStateJson { get; set; } = string.Empty;
    // Only used when the Ink story doesn't declare the denial variable itself
    public static int CachedDenialScore { get; set; }

    public static bool HasAnySaveData()
    {
        if (VNSaveManager.Instance != null)
            return VNSaveManager.Instance.HasAnySaves();

        string dir = Path.Combine(Application.persistentDataPath, "saves");
        return Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0;
    }
}