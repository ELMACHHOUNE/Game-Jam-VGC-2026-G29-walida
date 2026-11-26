using System.IO;
using UnityEngine;

public enum VNLaunchMode
{
    NewGame,
    ContinueLatest,
    LoadSpecificSlot
}

public static class VNSessionBridge
{
    public static VNLaunchMode LaunchMode { get; set; } = VNLaunchMode.NewGame;
    public static string TargetSlotId { get; set; } = string.Empty;
    public static bool HasPlayedBootSplash { get; set; }
    public static string CachedInkStateJson { get; set; } = string.Empty;

    public static bool HasAnySaveData()
    {
        if (VNSaveManager.Instance != null)
            return VNSaveManager.Instance.HasAnySaves();

        string dir = Path.Combine(Application.persistentDataPath, "saves");
        return Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0;
    }
}