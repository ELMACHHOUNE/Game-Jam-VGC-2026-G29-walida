using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Outcome of one minigame visit.</summary>
[Serializable]
public class MinigameResult
{
    /// <summary>Scene the result comes from, e.g. "PhotographingMinigame".</summary>
    public string sceneName;
    /// <summary>True when the player did what the minigame asked (correct photo taken / sound recorded).</summary>
    public bool completed;
    /// <summary>Journal entries unlocked during this visit (e.g. "khadija_home").</summary>
    public List<string> unlockedEntries = new();
}

/// <summary>
/// In-memory hand-off between the 2.5D medina and the isolated minigame scenes (Photography, Recording).
///
/// Medina -> minigame: MedinaLevelManager calls <see cref="BeginMinigame"/> with a snapshot of the medina
/// (story, player position, NPC progress, denial score, journal), then swaps to the minigame scene.
/// Minigame -> medina: the minigame only records results (<see cref="RecordUnlock"/>, <see cref="EndMinigame"/>)
/// and swaps back; the medina rebuilds itself from the snapshot plus those results.
/// Nothing is written to save files on the way.
///
/// Did the player capture a photo?
///   MinigameSession.HasCaptured("khadija_home")          (any time, any scene)
///   MinigameSession.LastResult.completed / unlockedEntries (right after returning)
///   Ink: VAR captured_khadija_home = false               (set to true automatically)
/// </summary>
public static class MinigameSession
{
    /// <summary>Prefix of the Ink boolean set when an entry is captured: captured_&lt;entryId&gt;.</summary>
    public const string InkCapturedPrefix = "captured_";

    /// <summary>The medina as it was when the current minigame started (null when not inside a minigame).</summary>
    public static VNSaveData MedinaSnapshot { get; private set; }

    /// <summary>Scene name of the minigame currently being played (null in the medina).</summary>
    public static string ActiveMinigame { get; private set; }

    public static bool IsInMinigame => !string.IsNullOrEmpty(ActiveMinigame);

    /// <summary>Result of the most recent minigame visit (null until one has finished).</summary>
    public static MinigameResult LastResult { get; private set; }

    private static MinigameResult _current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        MedinaSnapshot = null;
        ActiveMinigame = null;
        LastResult = null;
        _current = null;
    }

    /// <summary>True when this journal entry (photo / recording) has been captured, in this or an earlier visit.</summary>
    public static bool HasCaptured(string entryId) => JournalProgress.IsUnlocked(entryId);

    /// <summary>Called by the medina right before it swaps to a minigame scene.</summary>
    public static void BeginMinigame(string sceneName, VNSaveData medinaSnapshot)
    {
        ActiveMinigame = sceneName;
        MedinaSnapshot = medinaSnapshot;
        _current = new MinigameResult { sceneName = sceneName };
    }

    /// <summary>Called by a minigame when it unlocks a journal entry.</summary>
    public static void RecordUnlock(string entryId)
    {
        if (_current == null || string.IsNullOrWhiteSpace(entryId)) return;
        if (!_current.unlockedEntries.Contains(entryId))
            _current.unlockedEntries.Add(entryId);
        _current.completed = true;
    }

    /// <summary>Called by a minigame when its objective is done (even without a journal entry).</summary>
    public static void MarkCompleted()
    {
        if (_current != null) _current.completed = true;
    }

    /// <summary>Called by the minigame right before it swaps back to the medina.</summary>
    public static void EndMinigame()
    {
        LastResult = _current ?? new MinigameResult { sceneName = ActiveMinigame };
        _current = null;
        ActiveMinigame = null;
    }

    /// <summary>
    /// Used by the medina's boot: returns the snapshot updated with everything gained in the minigame
    /// (journal entries), and clears it so it is applied exactly once.
    /// </summary>
    public static VNSaveData TakeSnapshotForReturn()
    {
        VNSaveData snapshot = MedinaSnapshot;
        MedinaSnapshot = null;
        if (snapshot != null)
            snapshot.unlockedJournalEntries = JournalProgress.ToList();
        return snapshot;
    }

    /// <summary>Forgets any pending hand-off (New Game, Load, Quit to menu).</summary>
    public static void Clear()
    {
        MedinaSnapshot = null;
        ActiveMinigame = null;
        _current = null;
    }
}
