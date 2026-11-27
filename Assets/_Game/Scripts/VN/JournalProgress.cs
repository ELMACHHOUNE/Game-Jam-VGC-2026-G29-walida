using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's unlocked Journal entries, shared by every scene (minigames unlock, the medina journal shows).
///
/// LINKING: an entry is just a string id. A minigame unlocks an id, and any journal slot with the same
/// Entry Id shows its photo. For the photo minigame, a NeighborhoodPhotoTarget unlocks its Journal Entry Id
/// (or its Target Id when that is empty) and Medina_HUD > Journal Photographs > Entry Id must match it.
///
/// Persistence: saved in VNSaveData.unlockedJournalEntries by VNStoryController; cleared on New Game.
/// </summary>
public static class JournalProgress
{
    private static readonly HashSet<string> Unlocked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised with the entry id when an entry is unlocked for the first time.</summary>
    public static event Action<string> OnEntryUnlocked;

    /// <summary>The most recent entry unlocked this session (for "new entry" UI).</summary>
    public static string LastUnlockedEntry { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Unlocked.Clear();
        OnEntryUnlocked = null;
        LastUnlockedEntry = null;
    }

    public static bool IsUnlocked(string entryId) =>
        !string.IsNullOrWhiteSpace(entryId) && Unlocked.Contains(entryId.Trim());

    /// <summary>Unlocks an entry. Returns true when it was newly unlocked.</summary>
    public static bool Unlock(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return false;

        string id = entryId.Trim();
        if (!Unlocked.Add(id))
            return false;

        LastUnlockedEntry = id;
        OnEntryUnlocked?.Invoke(id);
        return true;
    }

    public static List<string> ToList() => new(Unlocked);

    /// <summary>Replaces the unlocked set (used when a save is loaded).</summary>
    public static void LoadFrom(IEnumerable<string> entries)
    {
        Unlocked.Clear();
        if (entries == null) return;
        foreach (string e in entries)
            if (!string.IsNullOrWhiteSpace(e)) Unlocked.Add(e.Trim());
    }

    public static void Clear()
    {
        Unlocked.Clear();
        LastUnlockedEntry = null;
    }

    /// <summary>
    /// Adds the current unlocks to an existing save slot (used when a minigame returns through the
    /// checkpoint written before it started, so the new entry isn't lost on the way back).
    /// </summary>
    public static void MergeIntoSlot(string slotId)
    {
        if (VNSaveManager.Instance == null || string.IsNullOrWhiteSpace(slotId))
            return;

        VNSaveData data = VNSaveManager.Instance.LoadFromSlot(slotId);
        if (data == null)
            return;

        var merged = new HashSet<string>(data.unlockedJournalEntries ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        merged.UnionWith(Unlocked);
        data.unlockedJournalEntries = new List<string>(merged);
        VNSaveManager.Instance.SaveToSlot(slotId, data);
    }
}
