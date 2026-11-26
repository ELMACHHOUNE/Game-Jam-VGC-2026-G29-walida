# Writing VN scenes in Ink

How `VNStoryController` reads the `.ink` files. Each knot is a scene that the game jumps to by name
(`=== allal_greeting ===` → set `dialogueKnotName: allal_greeting` on the interactable).
The Ink package recompiles the `.json` when you save the `.ink` in Unity.

In Ink, a tag on its own line applies to the **next line of text**. Tags on the same line as text also work:
`Hello. #speaker:allal`.

---

## Who is speaking

The dialogue text always starts at the same spot. The name header above it keeps its space even when no name
is shown.

| What you write | Result |
|---|---|
| A plain line | **Narrator / first person**: no name is shown. Every scene starts this way. |
| `#speaker:allal` | Allal speaks this line **and every line after it** until you change it. The name stays put and only the text changes. |
| `#speaker:none` or `#narrator` | Back to narrator (no name) from this line on. |
| `Allal: Hello there.` | One line only, then back to whoever the current `#speaker` is. |
| `#as:khadija` / `#as:none` | One-line override without changing the current `#speaker`. |

Priority for a line: `#as` › `Name:` prefix › current `#speaker` › narrator.

The ID is matched against **Character Data** assets (`characterId`, any case). When it matches:
- the header shows that character's **Display Name** in their **Name Color**;
- their portrait (if on stage) is lit and the others are dimmed.

If the ID isn't in the character database, the text you wrote is shown as-is.

The `Name:` prefix only counts when the name is a single word of up to 28 characters
(no spaces, dots or commas). Use `Lalla_Amina:`, not `Lalla Amina:`.
Watch out for narration like `Note: the door is locked`: it would be read as a speaker called "Note".
Put `#as:none` on that line.

```ink
=== allal_greeting ===
#char:allal:neutral:right
#speaker:allal
Wildi! Is that really you?
It's been years. Look at you.
#narrator
He hasn't changed a bit.
#speaker:allal
Come, come, sit down.
#as:none
(I can't say no to him.)
Still taking your tea with too much sugar?
-> END
```

---

## Characters on stage

| Tag | Effect |
|---|---|
| `#char:allal` | Allal enters (fades in) with the `neutral` expression in an automatic slot. |
| `#char:allal:happy` | Enters, or **swaps expression** if already on stage. |
| `#char:allal:happy:left` | Same, in a fixed slot: `left`, `center`, `right` (or `auto`). |
| `#hide:allal` | Allal exits (fades out); the others slide to fill the space. |
| `#hide:all` | Everyone exits. |
| `#clear` | Everyone exits and the event CG is hidden. |

- To swap characters, hide one and show the other on the same line:
  `#hide:allal` + `#char:khadija:neutral:right`.
- Expression keys come from the **Expressions** list on the Character Data asset.
  An unknown key falls back to the first expression.
- Showing a character does **not** make them the speaker. Add `#speaker:` for that.
- Each character needs a Character Data asset (*Create → VN → Character Data*) added to
  **VN_System → VNStageView → Character Database**. An unregistered ID logs
  `Unknown character ID` and shows nothing.

---

## Backgrounds and CGs

| Tag | Effect |
|---|---|
| `#bg:medina_street` | Crossfade to that background. |
| `#bg:medina_street:instant` | Cut instantly. |
| `#bg:none` | Remove the background (transparent: the 2.5D world shows behind the box). |
| `#cg:letter` / `#cg:letter:instant` | Show a full-screen event CG over the characters. |
| `#cg:hide` | Hide the CG. |

Background and CG IDs come from **VN_System → VNStoryController → Backgrounds / Event CGs**.

---

## Audio (FMOD)

| Tag | Effect |
|---|---|
| `#bgm:event:/BGM/OpeningBGM` | Start music (replaces the current track). |
| `#bgm:event:/BGM/OpeningBGM:30` | Same, auto-stop after 30 s. |
| `#bgm_stop` or `#bgm:none` | Stop the music. |
| `#sfx:event:/SFX/PhoneRing` | One-shot sound. |
| `#sfx_loop:event:/SFX/RadioStatic` | Looping sound (optional `:seconds` to auto-stop). |
| `#sfx_param:DistortionLevel:0.7` | Set a parameter on the looping sound. |
| `#sfx_stop` | Stop the looping sound. |

---

## Pacing

| Tag / text | Effect |
|---|---|
| `#wait 2` or `#wait:2` | Pause 2 s before the line (the box is cleared while waiting). |
| `[continue]` | A beat with no text: runs its tags, then carries on without a click. |
| `[blank]` | An empty line that still waits for a click. |
| `#continue` | Auto-advance to the next line as soon as this one finishes typing (no click). |
| `#glitch` / `#glitch:0.5` | Screen glitch effect (seconds). |

Choices use normal Ink syntax (`* [Text]` / `+ [Text]`).
When a knot reaches `-> END` (or runs out of content), the VN closes and the game fades back to the 2.5D world.

---

## Denial score

There are 8 denial choices in the game. When the score goes **above 5**, the game turns "off":
- the world distorts (chromatic aberration, grain, lens warp, desaturation) and glitches in short bursts;
- VN dialogue gets tunnel vision and dimming;
- the sound is distorted through an FMOD snapshot;
- the music swaps to its horror version.

Put a `#denial:` tag at the end of the choice line. Don't add a second `[...]`: Ink rejects `+ [It's okay...] [+0]`.

```ink
* [It's okay... she's fine.] #denial:1
    Yeah. She always pulls through.
* [I should call the doctor.] #denial:0
    I'll ring them first thing tomorrow.
```

- `#denial:1`, `#denial:+1`, `#denial:-1` and `#denial:0` all work. `0` is optional and is just a note for yourself.
- The score is saved and loaded with the game either way.
- Optional: declare `VAR denial_score = 0` once, in the root `.ink` file. The score then lives in Ink, so you can also
  change it the plain Ink way (`~ denial_score += 1`) and branch on it:

```ink
{ denial_score > 5:
    The street looks wrong. Too quiet.
- else:
    The street is just as I remember it.
}
```

Effect settings (threshold, intensities, FMOD snapshot, horror music) are on **VN_System → VNDenialEffects**.
Testing:
- In Play mode, the panel at the top of that component shows the live score, with a slider, −1 / +1, Reset and
  High buttons, and **Trigger Glitch Burst**.
- Outside Play mode, tick **Force Active In Editor**.
