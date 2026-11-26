# FMOD banks

`Build/Desktop/` holds the banks exported from FMOD Studio 2.04 (`Master.bank`, `Master.strings.bank`).
The FMOD for Unity integration (`Packages/com.fmod.fmod-for-unity.v204-2.4.0.tgz`) is linked to this
folder as a **Single Platform Build** (see `Assets/_Game/Scripts/Audio/Editor/FMODBankAutoSetup.cs`).

To update audio: re-export from FMOD Studio and overwrite the files in `Build/Desktop/`.
Unity picks up the change automatically (or use **FMOD > Refresh Banks**). On build, FMOD copies the
banks into `StreamingAssets` for you.

Play events from code:

```csharp
FMODUnity.RuntimeManager.PlayOneShot("event:/Tea_kettle", transform.position);
```

or add a **Studio Event Emitter** component and pick the event from the browser.
