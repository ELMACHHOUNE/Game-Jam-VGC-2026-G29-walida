# Linking minigames to the Journal

The Journal works with **entry ids**: plain strings. A minigame *unlocks* an id, and the medina Journal shows
every slot whose **Entry Id** has been unlocked. The ids are the only link, so the minigame scene and the
medina scene never reference each other.

```
Minigame (PhotographingMinigame)                 Medina (Medina_HUD prefab)
NeighborhoodPhotoTarget                          MedinaHUDController > Journal Photographs
  Target Id        = khadija_home      ---->       Entry Id     = khadija_home
  Journal Entry Id = (empty -> Target Id)          Caption      = Khadija's Home
  Curated Book Photo                               Photo Texture (or Photo sprite)
```

## Photo minigame (already wired)

1. Each spot to photograph is a **NeighborhoodPhotoTarget** (paired with a glitching **MemoryHotspot**).
   - Its **Journal Entry Id** is the entry it unlocks.
   - Leave it empty to use the **Target Id**.
2. Photographing the spot unlocks the entry. The **"You've unlocked a Journal entry!"** popup appears, showing the
   curated photo, title and caption. Click / Space / Enter / E / Esc returns the player to the 2.5D medina,
   where they entered.
3. In **Medina_HUD > Journal Photographs**, a slot with the same **Entry Id** shows its photo.
   - Until then it shows the empty mount and its **Locked Caption**.
   - A slot with an empty Entry Id is always shown.

Current links: `lwalida_home`, `khadija_home`, `neighborhood_stairs`, `blue_sky` (Journal slots 0-3).

Spots already unlocked on earlier visits count as photographed, so each visit asks for a new one.

## Sound recording minigame

Set **SoukAudioSegmentController > Journal Entry Id**. Recording the target sound then unlocks that entry and
shows the same popup (text-only, since there's no photo). Add a Journal slot with that Entry Id to show it in
the medina.

## Any other minigame or event

Call `JournalProgress.Unlock("your_entry_id");` from code, and add a Journal slot with that Entry Id.
If the scene has a **MinigameReturnToMedina** (added automatically to the photo and Souk minigames),
the popup and return happen on their own.

## Tuning

**MinigameReturnToMedina** settings (add the component to the minigame scene to edit them):
- **Return After Journal Unlock**
- **Unlock Title**
- **Unlock Continue Hint**
- **Popup Delay** (time after the photo before the popup appears)
- **Popup Auto Continue** (0 = wait for the player)

## Scene swapping (isolated minigames)

The minigames are separate scenes that know nothing about the medina. The game swaps whole scenes, and
`MinigameSession` carries the state across in memory:

1. **Medina → minigame:** `MedinaLevelManager.LaunchMinigameScene` snapshots the medina with
   `MinigameSession.BeginMinigame`, then loads the minigame scene. The snapshot holds:
   - story and Ink state;
   - player position;
   - which NPCs were talked to;
   - the denial score;
   - the intro flag;
   - journal entries.
2. **In the minigame:** results are recorded with `MinigameSession.RecordUnlock` and `MarkCompleted`.
3. **Minigame → medina:** `MinigameReturnToMedina` calls `MinigameSession.EndMinigame`, then loads
   `MedinaMainLevel` with launch mode `ReturnFromMinigame`. The medina rebuilds itself from the snapshot plus
   the new journal entries. The player reappears where they left, and the opening and intro line don't replay.

No save file is written or read during the swap. Your autosave slot is untouched.

## Checking whether the player captured a photo

**From C#, in any scene:**

```csharp
MinigameSession.HasCaptured("khadija_home");   // captured in this visit or an earlier one
MinigameSession.LastResult.completed;          // did the last visit succeed?
MinigameSession.LastResult.unlockedEntries;    // e.g. ["khadija_home"]
MinigameSession.LastResult.sceneName;          // "PhotographingMinigame" / "AudioRecordingMinigame"
```

**From Ink:** declare a boolean named `captured_<entryId>`. It's set to `true` automatically whenever the
medina loads with that entry captured.

```ink
VAR captured_khadija_home = false

=== khadija_after_photo ===
{ captured_khadija_home:
    You found my old house! Let me see...
- else:
    Have you been to my old house yet?
}
-> END
```

## Saving

Unlocked entries are part of the normal save data (`VNSaveData.unlockedJournalEntries`). They're saved and
loaded with the game, and cleared on New Game.
