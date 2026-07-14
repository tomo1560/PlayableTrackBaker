# VRChat bug report draft

Submitted to: https://feedback.vrchat.com/bug-reports (draft prepared 2026-07-14)

---

**Title:**
Timeline audio bypasses all volume sliders when the PlayableDirector starts playing after world load (SecurityScan's unbound-AudioPlayableOutput fix never runs)

**Describe the bug:**

Audio played by a Timeline `AudioTrack` whose track binding resolves to no `AudioSource` is completely unaffected by the client's volume controls — neither the World volume slider nor the Master volume slider changes or mutes it. Users have no way to turn the sound down short of muting the entire application at the OS level.

The client already contains a mitigation for exactly this case. `WorldValidation.SecurityScan` (SDK `com.vrchat.base`, `Runtime/VRCSDK/Dependencies/VRChat/Scripts/Validation/WorldValidation.cs`) walks each `PlayableDirector` and fixes up any `AudioPlayableOutput` whose target is null, with the comment "AudioPlayableOutput without a target source will bypass client volume controls." It attaches a generated `AudioSource` routed to the game audio mixer group and calls `output.SetTarget(addedSrc)`.

However, that fix-up is guarded by:

```csharp
if (!playableDirector.playableGraph.IsValid())
    return;
```

The scan runs once at world load. A director with `playOnAwake = false` has no PlayableGraph at that moment, so it is skipped entirely. When Udon later calls `director.Play()` (or performs the common `Stop() → time = t → Play()` seek pattern, which rebuilds the graph), the new `AudioPlayableOutput` with a null target is created *after* the scan and is never fixed. The result is 2D audio delivered straight to the listener, outside every volume category.

This is not a hypothetical: play-button-synced Timeline shows (director started by Udon from `Networking.GetServerTimeInSeconds()`) are a standard pattern, and any of them loses volume control the moment the AudioTrack binding is missing — which itself happens through ordinary Unity operations (duplicating a TimelineAsset and assigning the copy to the director silently drops all track bindings, because bindings are keyed by track object reference).

For contrast, the equivalent mitigation for `VideoPlayer` with `audioOutputMode = Direct` in the same `SecurityScan` works reliably, because it inspects serialized component state rather than live graph state and therefore does not depend on playback having started.

**To reproduce:**

1. In Unity (2022.3.22f1, SDK3 Worlds 3.10.4), create a Timeline with an `AudioTrack` and an audio clip, and leave the track's binding empty (or bind it, duplicate the TimelineAsset, and assign the duplicate to the `PlayableDirector` — the bindings are dropped silently).
2. Set the director's `playOnAwake` to **false**.
3. From an Udon behaviour, call `director.Play()` on Interact.
4. Build & Test / upload, join the world, press the button so the music starts.
5. Open the main menu audio settings and move the **World** slider to 0, then the **Master** slider to 0.

**Expected behavior:**
The Timeline audio is attenuated/muted by the sliders — or the load-time fix-up ("Fixing up AudioPlayableOutput without a target source.") also covers directors that start playing after the scan.

**Actual behavior:**
The audio keeps playing at full volume with both sliders at 0. No fix-up log is emitted for the director. (Per the code path, the same director with `playOnAwake = true` should be fixed by the scan — the defense only misses graphs created after the scan.)

**Suggested fix:**
Detect graph (re)construction instead of scanning once — e.g. re-run the null-target fix-up when a scanned `PlayableDirector` transitions to playing (`PlayableDirector.played` callback), or periodically for directors that were skipped because their graph was not valid at scan time. Inspecting the serialized scene bindings of the TimelineAsset's audio tracks at load (the way the VideoPlayer Direct check inspects serialized state) would also close the gap without any runtime hook.

**Additional context:**
We hit this in a published world via an editor build-time tool that cloned a TimelineAsset (`AssetDatabase.CopyAsset`) and swapped `director.playableAsset` without carrying the track bindings over; the world's own bug is fixed on our side. We kept a minimal repro world (four toggles: bound AudioTrack / unbound AudioTrack / VideoPlayer Direct / plain AudioSource, all same clip and effective volume) and can share it or a video if useful. Because the bypass is trivial to create with standard Unity operations, it can also be abused deliberately to ship audio that users cannot mute, which is why we are reporting it rather than just fixing our world.
