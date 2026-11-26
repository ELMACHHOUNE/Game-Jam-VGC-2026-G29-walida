using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public class SavedCharacterState
{
    public string characterId;
    public string expression;
    public StageSlot slot;
}

[Serializable]
public class VNBacklogEntry
{
    public string speaker;
    public string text;
    public bool isChoice;
}

[Serializable]
public class VNSaveData
{
    public string slotId;
    public string label;
    public string timestamp;
    public long unixTimestamp;
    // Dialogue & choice history
    public List<VNBacklogEntry> backlog = new();

    // Ink runtime state
    public string inkStateJson;
    public List<string> visitedChoicePaths = new();

    // Current dialogue beat
    public string speaker;
    public string stickySpeaker;
    public string dialogue;
    public bool isWaitingForChoice;

    // Visual & Audio stage state
    public string backgroundId;
    public string eventCgId;
    public string bgmId;
    public string activeSpeakerId;
    public List<SavedCharacterState> characters = new();

    // 2.5D exploration state
    public bool inWorld;
    public bool hasPlayerPosition;
    public Vector3 playerPosition;
    public bool hasSeenWorldIntro;
    public int denialScore;
}

public readonly struct VNSaveSlotSummary
{
    public readonly string SlotId;
    public readonly string Title;
    public readonly string Timestamp;
    public readonly string Preview;
    public readonly bool IsEmpty;

    public VNSaveSlotSummary(string slotId, string title, string timestamp, string preview, bool isEmpty)
    {
        SlotId = slotId;
        Title = title;
        Timestamp = timestamp;
        Preview = preview;
        IsEmpty = isEmpty;
    }
}

public class VNSaveManager : MonoBehaviour
{
    public const string AutosaveSlotId = "autosave";
    public static readonly string[] ManualSlotIds = { "slot_1", "slot_2", "slot_3" };

    public static VNSaveManager Instance { get; private set; }

    public string SaveDirectory => Path.Combine(Application.persistentDataPath, "saves");

    // Statics survive play sessions when domain reload is disabled, so reset them explicitly.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    // Not placed in any scene: create one persistent instance before the first scene loads
    // so the main menu, the HUD and the VN all share it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject(nameof(VNSaveManager));
        go.AddComponent<VNSaveManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(SaveDirectory);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public string GetSlotPath(string slotId) => Path.Combine(SaveDirectory, $"{slotId}.json");

    public bool HasAnySaves()
    {
        if (!Directory.Exists(SaveDirectory))
            return false;

        return Directory.GetFiles(SaveDirectory, "*.json").Length > 0;
    }

    public void SaveToSlot(string slotId, VNSaveData data)
    {
        if (string.IsNullOrWhiteSpace(slotId) || data == null)
            return;

        Directory.CreateDirectory(SaveDirectory);

        DateTime now = DateTime.Now;
        data.slotId = slotId;
        data.timestamp = now.ToString("yyyy-MM-dd  HH:mm");
        data.unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        string path = GetSlotPath(slotId);
        string tempPath = path + ".tmp";

        // Atomic write so a crash mid-save never corrupts an existing save file
        File.WriteAllText(tempPath, json);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
    }

    public VNSaveData LoadFromSlot(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            return null;

        string path = GetSlotPath(slotId);
        if (!File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<VNSaveData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VNSaveManager] Failed to parse save '{slotId}': {ex.Message}");
            return null;
        }
    }

    public VNSaveData GetLatestSave()
    {
        if (!Directory.Exists(SaveDirectory))
            return null;

        VNSaveData newest = null;
        foreach (string file in Directory.GetFiles(SaveDirectory, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            VNSaveData candidate = LoadFromSlot(id);
            if (candidate == null) continue;

            if (newest == null || candidate.unixTimestamp > newest.unixTimestamp)
                newest = candidate;
        }

        return newest;
    }

    public List<VNSaveSlotSummary> GetAllSlotSummaries(bool includeAutosave)
    {
        var list = new List<VNSaveSlotSummary>();

        if (includeAutosave)
            list.Add(BuildSummary(AutosaveSlotId, "CHECKPOINT (AUTO)"));

        for (int i = 0; i < ManualSlotIds.Length; i++)
            list.Add(BuildSummary(ManualSlotIds[i], $"SLOT 0{i + 1}"));

        return list;
    }

    private VNSaveSlotSummary BuildSummary(string slotId, string defaultTitle)
    {
        VNSaveData data = LoadFromSlot(slotId);
        if (data == null)
            return new VNSaveSlotSummary(slotId, defaultTitle, "EMPTY", "No saved data", isEmpty: true);

        string cleanPreview = StripRichTextTags(data.dialogue);
        if (cleanPreview.Length > 68)
            cleanPreview = cleanPreview.Substring(0, 65) + "...";

        string prefix = string.IsNullOrWhiteSpace(data.speaker) ? string.Empty : $"{data.speaker}: ";
        return new VNSaveSlotSummary(slotId, defaultTitle, data.timestamp, prefix + cleanPreview, isEmpty: false);
    }

    private static string StripRichTextTags(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty).Trim();
    }
}