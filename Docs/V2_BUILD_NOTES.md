# AuraSync V2 – Build Notes

What changed in `Scripts/` in this pass, what you must set up outside the scripts, and a test plan for mobile phones.

> **Status:** written and syntax-checked against Unity's engine DLLs (no compile errors other than the
> third-party types the checker cannot see: Photon, TMP, AR Foundation, glTFast). **Not run in Unity and not
> tested on a device.** Treat the first Console after pasting `Scripts/` in as the real compile, then work
> through the phone test plan in section 4 (about 90 cases; the P0 ones first).

## 1. Fixed

| Area | Change |
|---|---|
| Startup race | `SceneBootstrapper` creates networking/voice/lobby *before* the AR wait. Lobby and HUD wait for managers before subscribing. |
| Double button clicks | Procedural UIs no longer wire handlers twice (Mic / Follow no longer cancel out). |
| Admin source | `ZoneManager` is created if the scene lacks one. Items register with it. |
| Solo placement | Items are only shared when in a room; cloud anchor IDs are now persisted. |
| Late joiners | Master re-sends shared (cloud-anchored) items; receivers dedupe by item ID. |
| Model sync | A device-independent model key is broadcast, not a local path. Viewers outside `SpaceScene` are routed into it. |
| Admin sync | Sends only while changing (unreliable) + 1 s reliable heartbeat. Rotation no longer jumps when a viewer breaks sync. |
| Presenter handoff | `NetworkManager.PromoteToAdmin`, master-switch handling, state reset on leave/disconnect. |
| Names | `PhotonNetwork.NickName` set (saved in PlayerPrefs, editable in the lobby). |
| Pointers | No `Shader.Find`; smoothing moved out of the RPC. AR gaze is exchanged relative to a shared Cloud-Anchor origin. |
| Voice | 8 kHz μ-law + silence gate (~64 kbps only while talking, was ~256 kbps always). Mic starts on room join, not at launch. Mute state is a player property. |
| Perf | Mesh simplification yields between meshes and shares results; model cache capped at 512 MB (LRU). |

## 2. New

- `ModelRaycaster`, `ModelPartSelector` – tap a part: presenter's selection is shared (gold), a viewer's own is local (cyan).
- `CanvaPointerManager` – name pill on the model surface at each person's touch point (10 Hz).
- `SharedSpaceOrigin` – shared AR frame (presenter's first hosted Cloud Anchor).
- `DeepLinkHandler` – join from an invite link.
- `PointerVisuals` – shader-safe coloured materials.
- Lobby doubles as the share panel when in a room (code, copy link, leave). AR HUD has an always-visible *Multiplayer* button.
- Space HUD: Following/Catch-up state, mic + speaking per player, tap a badge **twice** to hand over presenter, selected-part chip.
- `FollowAdminController` now just exposes the Space sync lock. It no longer moves the camera.

## 2d. Model Parts drawer

The spec's "Model Explorer / Sub-parts" drawer (`MULTIUSER_COLLABORATION_SPEC.md` §F): a **PARTS**
button in the Space scene opens a searchable list of the loaded model's parts, so a part that is
hidden behind another, too small to hit on a phone, or known only by name can still be picked.
Picking a row runs through the same selection as tapping the part, so the presenter's pick is still
shared in gold and everyone else's stays local in cyan. Grouping nodes are now highlighted as well,
boxed around the meshes they contain — that is the one change to existing 3D tapping.

New: `Scripts/UI/ModelPartsDrawerUI.cs`, `Scripts/Space/ModelPartCatalog.cs`.

## 2e. Exploded view

An **EXPLODE** slider in the parts drawer slides every part outward from the model's centre, by a
distance scaled to the model's size. In a room only the presenter may use it — a model cannot be
assembled and exploded at once — and the amount travels as a room property, so late joiners arrive
at the same explosion as everyone else. New: `Scripts/Space/ModelExploder.cs`.

## 2f. AR edit and delete sync

Edits and deletions of notes and objects now reach the rest of the room, not just placement. Hooked
into `PersistenceManager`, which every edit and delete already passes through to be saved, so each
screen gets it without having to ask. Private items and items with no Cloud Anchor are never sent.
An echo guard stops a change bouncing back to its sender. New: `Scripts/Networking/ItemSync.cs`.

**This closes the last item in section 5's "not built" list.**

**Test plan for 2d to 2f: `V2_PARTS_DRAWER_TESTS.md`** (single-device cases run in the Editor).

## 2b. Round 2 – fixes from phone testing

| # | Reported | Cause found in code | Fix |
|---|---|---|---|
| 1 | Presenter's moves only reach viewers when they press Catch Up | (a) The old **Admin Mode** toggle in `SpaceScene` let anyone make themselves "admin" locally, which stopped them following and made them broadcast too. (b) A previous presenter kept broadcasting after handing over. (c) Any touch that moved more than 1 pixel, including a tap, pointing, or pressing a HUD button, silently switched a viewer out of follow mode. | Toggle hidden; the presenter is decided by the room only. Only the current presenter broadcasts, and viewers ignore anyone else. A touch must move **24 px** before it counts as a drag (taps and pointing no longer break following), and gestures that start on a button are ignored. |
| 2 | Room join must be on the first screen; joining should open the shared model | The panel only opened from a button. | The room panel **opens at app start**. Joining takes you straight into the shared 3D view (the model downloads, with progress), or waits and opens automatically once the presenter picks a model. Clear status messages; the panel closes itself when the 3D view opens. |
| 3 | Viewers cannot hear the presenter; audio cheap and laggy | (a) A **fixed silence threshold**: a quieter mic (phone held away while handling the model) was treated as silence and nothing was sent. (b) On a slow frame, most of the mic backlog was **thrown away**. (c) 8 kHz narrowband audio. (d) Photon held voice packets for up to ~33 ms before sending. | Adaptive silence gate that follows the background noise; backlog of up to 400 ms is sent instead of dropped; **16 kHz wideband**; packets flushed immediately; playback delay capped at ~180 ms; a diagnostics log line every 5 s. **For call quality, switch to Photon Voice 2 (section 3).** |
| 4 | Pointers appear only for some interactions | Position was sampled every 100 ms and hidden the instant the finger lifted, so quick taps rarely showed. Touches beside the model were dropped. Decorative UI panels counted as "over UI". | First touch is sent immediately; the pointer stays visible ~1 s after release; touches beside the model show on a plane through the model's centre; only real controls block touches. |

## 2c. Round 3 - fixes from phone testing

| # | Reported | Cause found in code | Fix |
|---|---|---|---|
| 1 | Real-time sync only works **while the Catch Up / Follow button is held down** | **Confirmed root cause.** The project uses the new Input System (`GestureDetector.cs` imports `UnityEngine.InputSystem` unconditionally), so Active Input Handling is *not* set to the old manager. Calling `UnityEngine.Input.*` in that configuration throws `InvalidOperationException`, and an exception part way through `Update()` **silently abandons the rest of that Update**. `SpaceInteractionController.Update()` read the legacy mouse whenever no finger was touching the screen - above the code that mirrors the presenter. So with a finger down the mirror ran, and the moment the finger lifted it stopped. | All input now goes through the new `Core/InputCompat.cs`, which reads touches, mouse and scroll through whichever backend the project is set to, and never touches the mouse APIs on a phone. Mirroring was also moved to `LateUpdate()`, so a viewer keeps following even if anything in the input path misbehaves. |
| 1b | Residual features fighting the sync | `SpaceModelViewerFix` was an early standalone script attached to **ModelAnchor**: it rotated the model with its own input handling and forced every material to unlit, on top of what `SpaceInteractionController` and `GLTFastMaterialHelper` already do. Two controls also existed for one state: a *Follow Admin* button in the AR HUD and the Space HUD's *FOLLOWING / CATCH UP*. | `SpaceModelViewerFix` is retired - it now logs a warning and deletes itself at startup (delete it from ModelAnchor when convenient). The AR HUD's *Follow Admin* and *Rooms* buttons are gone; the Space HUD owns the single follow control. |
| 2 | Presenter's voice still not heard on the viewer's phone | Not provable from here without logs, so the **three mechanisms that can cause exactly this** were each fixed, and the phone now shows which one is at fault. (a) **Permission timeout:** the mic permission request gave up after 12 s and left that phone with no microphone for the whole session - silent to everyone while still hearing them. A presenter reading out a room code is exactly who misses that window. (b) **Android call mode:** `setMode(MODE_IN_COMMUNICATION)` gives hardware echo cancellation but on some phones reroutes capture so Unity records pure digital silence. (c) **Silence gate:** could hold back a mic whose level never crosses the threshold. | (a) The permission wait now lasts as long as the player is in the room, and reports `MIC PERMISSION DENIED` on screen if refused. (b) Call mode is **off by default**. (c) A **microphone watchdog** detects a mic that is "recording" but delivering nothing and works through restart -> leave call mode -> native sample rate, logging each step; a **gate watchdog** disables the silence gate automatically if the mic is clearly picking up sound but nothing has been sent for 8 s. |
| 2b | No way to tell which phone is at fault (adb would not connect) | Diagnostics existed only in logcat. | A one-line **voice readout on screen**, above the new corner button, while in a room: `VOICE live \| lvl 0.031 gate 0.004 \| tx 412 rx 388`. Read it on both phones - see the table in section 3. |
| 3 | Need a corner button that opens the join-room panel, dismissed by tapping elsewhere | The panel could only be opened from a HUD button that disappeared once in a room. | A small **ROOM** button sits in the lower-left corner in every scene, on its own canvas above everything else. Tapping it opens the panel, tapping it again closes it, and tapping anywhere outside the card closes it too. The button never goes away, so the panel is always one tap from anywhere. Position is adjustable on `RoomLobbyUI` (*Launcher Anchor Min / Max*). |

## 3. Setup you must do outside the scripts

### Deep links (Android) – optional, skip this if you only want to test the app itself

> **Warning:** a wrong custom manifest makes the APK **install but show no icon, with the Open button greyed out**
> (Android finds no launchable activity). Do deep links last, and only after the plain build launches fine.

How to do it safely:

1. *Player Settings → Publishing Settings → tick* **Custom Main Manifest**. Unity generates `Assets/Plugins/Android/AndroidManifest.xml`.
2. **Keep everything Unity generated.** Do not replace the file with just the snippet.
3. Add the deep-link filter as a **second, separate** `<intent-filter>` inside the same `<activity>`, **next to** the existing `MAIN` / `LAUNCHER` filter. Never merge the two, and never put `<data>` inside the launcher filter.

A correct result looks like this (Unity 2022.3, Activity entry point):

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android" xmlns:tools="http://schemas.android.com/tools">
  <application>
    <activity android:name="com.unity3d.player.UnityPlayerActivity"
              android:theme="@style/UnityThemeSelector"
              android:exported="true">

      <!-- 1. Launcher entry. Without this the app has no icon and cannot be opened. -->
      <intent-filter>
        <action android:name="android.intent.action.MAIN" />
        <category android:name="android.intent.category.LAUNCHER" />
      </intent-filter>

      <!-- 2. Invite links. Separate filter. -->
      <intent-filter>
        <action android:name="android.intent.action.VIEW" />
        <category android:name="android.intent.category.DEFAULT" />
        <category android:name="android.intent.category.BROWSABLE" />
        <data android:scheme="aurasync" android:host="join" />
      </intent-filter>

      <meta-data android:name="unityplayer.UnityActivity" android:value="true" />
    </activity>
  </application>
</manifest>
```

If your generated manifest differs (for example it names `GameActivity`), keep Unity's activity name and attributes and only add filter 2.
Then `aurasync://join?room=AURA-AB12` opens the app and joins.

**Decision needed:** the copied link is `https://aurasync.app/join?room=…`. That only opens the app if you own
`aurasync.app` and host an `assetlinks.json` (Android App Links). Until then it is just a string the recipient can
paste into the lobby (which already works). Either set up the domain, or change `RoomLinkManager.BaseJoinUrl` to `aurasync://join?room=`.

#### If the app installs but has no icon / Open is greyed out
Check in this order:

1. **Custom manifest** (above): the `MAIN` + `LAUNCHER` filter must exist as its own filter and the activity needs `android:exported="true"`. Quick test: delete `Assets/Plugins/Android/AndroidManifest.xml` (and its `.meta`), untick *Custom Main Manifest*, rebuild. If the icon returns, the manifest was the cause.
2. **Other manifests merged into the build:** look for any `AndroidManifest.xml` under `Assets/Plugins/Android/` or inside packages (ARCore Extensions, Firebase, Photon) that declares its own `UnityPlayerActivity` or removes the launcher.
3. **Confirm on the phone** (USB debugging on). Replace `<package>` with your Bundle Identifier:
   `adb shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER <package>`
   No activity listed = no launcher entry in the installed APK. You can also run
   `adb shell dumpsys package <package>` and look for `android.intent.action.MAIN` under the activity.
4. **Inspect the built APK** to see the final merged manifest: Android Studio → *Build → Analyze APK* → `AndroidManifest.xml`.
5. **Entry point mismatch:** *Player Settings → Other Settings → Application Entry Point* must match the activity named in the manifest (`Activity` ↔ `UnityPlayerActivity`, `GameActivity` ↔ `UnityPlayerGameActivity`).
6. **Stale install:** uninstall the old app completely (`adb uninstall <package>`) before reinstalling after manifest changes.

### Voice: Photon Voice 2 (installed 2026-10-11)

The built-in voice cannot reach WhatsApp quality: Unity's microphone API adds delay on Android, it has no echo
cancellation, and mu-law is a 1970s telephone codec. **Photon Voice 2 (v2.64, with PUN 2.56) is now installed**
and `VoiceChatManager` uses it automatically. Everything is built in code, with no prefab and no scripting define
(the package defines `PHOTON_VOICE_R4` itself):

- **Connection** (`AuraSyncVoiceClient`, a `PunVoiceClient`): starts as soon as the room is joined, so a phone
  hears the room even before (or without) granting the mic. Follows PUN into the same region and a voice room
  named after the PUN room. If the voice connection drops while still in the PUN room, it retries
  (5 s, 10 s, 20 s, 30 s...).
- **Microphone** (Recorder): Opus 48 kHz, 20 ms frames, 32 kbps, captured with **Unity's microphone**.
  Photon's native Android call-mode mic was tried first (2026-10-11) and made received voices inaudible: it
  takes Android audio focus for a "voice call" away from Unity and moves the audio route (often to the
  earpiece), while frames kept arriving. It is still available behind `useAndroidCallModeMic` but is off.
  The mic is lent to voice search and given back.
- **Processing** (WebRtcAudioDsp): echo cancellation (AEC3, referenced to exactly what Unity plays), noise
  suppression, gain control, voice activity detection (nothing is sent while silent) and a high-pass filter.
- **Network**: forward error correction (one recovery packet per 4 frames), adaptive playback buffer of 150-250 ms.

Only one manual setting is needed: **App Id Voice** in `PhotonServerSettings` (Photon Dashboard → Create a new
app → type **Voice**). If it is empty, the log says so and every phone falls back to the built-in voice together.

On-screen voice line with Photon Voice: `VOICE live | Opus | lvl 0.35 | tx 1200 rx 950 lost 1% | spk 1/1 out 0.30`
- `lvl` stays 0.000 while you talk → that phone's mic gives nothing.
- `tx` climbs on A but `rx` does not on B → the network between them.
- `lost` above ~10% → poor Wi-Fi/mobile data; error correction cannot hide that much.
- `spk playing/linked`: one per other talker. `rx` climbing but `spk 0/0` → voices arrive but no speaker was made;
  `1/1 out 0.00` while the other person talks → they are sending silence (check their `lvl`); `out` > 0 but
  nothing heard → this phone's volume/route.
- `voice offline (...)` → the voice server connection failed; the reason is in brackets, and it retries by itself.

Notes:
- **Importing a Photon package overwrites `PhotonServerSettings`** with a blank copy (App Id PUN, App Id Voice
  and Dev Region are lost). After any Photon update, re-enter them and *File → Save Project*.
- Echo cancellation removes a phone's own speaker. It cannot remove *another* phone's speaker in the same room:
  phones close together with speakers on will hear each other twice. Use earphones, distance, or the
  "mute incoming audio" button when everyone is physically together.
- Expect roughly 150-300 ms end-to-end delay on a decent connection, similar to a WhatsApp call.

### Voice settings and troubleshooting (built-in voice)

`VoiceChatManager` is created at runtime by `SceneBootstrapper`. To change its settings, add a GameObject with
**VoiceChatManager** to the `V1` scene yourself; the bootstrapper then uses that one.

| Setting | Default | When to change |
|---|---|---|
| Network Sample Rate | 16000 | 8000 halves data use on poor connections (lower quality) |
| Use Android Communication Mode | **off** | On gives hardware echo cancellation, but on some phones it makes the mic record silence. The watchdog turns it off by itself if that happens. |
| Min Silence Threshold | 0.002 | Lower if a quiet speaker is cut off |
| Always Transmit | off | Turn on to rule out the silence gate entirely while testing |
| Log Diagnostics | on | Turn off for release builds |

The microphone and gate watchdogs run on their own and need no settings. They log every step they take
(`[AuraSync Voice]`), and the on-screen readout shows the result.

**One-way audio: read the on-screen voice line on BOTH phones.** It sits just above the corner ROOM
button whenever you are in a room, and needs no cable:

```
VOICE live | lvl 0.031 gate 0.004 | tx 412 rx 388
      ^state       ^mic level  ^gate   ^sent ^received
```

Have the silent person talk, then compare the two phones:

| What the readout shows | Meaning | Action |
|---|---|---|
| `MIC PERMISSION DENIED` | That phone never got the microphone | Android Settings > Apps > AuraSync > Permissions > Microphone |
| `lvl` stays `0.000` while they talk | The mic is running but giving Unity nothing | The watchdog is already retrying (`recovering: ...`). If it ends at `MIC GIVES NO AUDIO`, close other apps that use the mic and rejoin |
| `lvl` healthy but `tx` does not climb | The silence gate is holding the audio back | The gate watchdog opens it after 8 s by itself; `live (gate forced open)` means it did |
| Their `tx` climbs, your `rx` does not | The packets are not crossing the network | Check both phones show the same room code; check data saver / battery saver / a restrictive Wi-Fi |
| Their `tx` climbs, your `rx` climbs, still silence | Playback problem on your phone | Turn the media volume up; try *Use Android Communication Mode* on |
| `mic not running` / `no microphone` | No capture device | Another app holds the mic, or the device has none |

The same information is in logcat every 5 s if you do get adb working
(`adb logcat -s Unity | findstr "AuraSync Voice"`), with the recovery stage appended.

### Permissions
Microphone permission is requested at room join. Make sure `RECORD_AUDIO` is in the manifest (Unity adds it when `Microphone` is used).

### Photon
`NetworkManager` has *Fixed Region* (`asia`) and *App Version* (`1.0`) fields. All players must match. Leave region empty to use the settings asset.

## 4. Testing on mobile phones

### 4.1 Before you start

**Devices**
- Minimum **2** ARCore-supported Android phones (A and B). Cases marked **3 devices** need a third (C).
- Install the **same APK** on all of them (same Photon *App Version* and *Fixed Region*, see section 3).
- Use a **Development Build** so you can read logs: `adb logcat -s Unity` (one terminal per device).

**Roles used below**
- **A** = creates the room, becomes the presenter (admin).
- **B / C** = join as viewers.

**Environment**
- Internet on every phone. Same Wi-Fi is not required (Photon is cloud-hosted), but test at least once with one phone on mobile data.
- A well-lit room with textured surfaces (wood, books, posters) for AR and Cloud Anchor tests. Blank white walls make Cloud Anchors fail.
- Cloud Anchors need the **ARCore Cloud Anchor API enabled** for your API key in Google Cloud.
- Headphones for voice tests (otherwise phones feed back into each other).
- Two `.glb` models available: a small one (for example `House.glb`) and a heavy one (for example `JetEngine.glb`).

**Log lines worth watching**

| Tag | Meaning |
|---|---|
| `[AuraSync Network]` | connect, create/join/leave, presenter changes |
| `[AuraSync ModelSync]` | model broadcast and routing |
| `[SpaceSceneManager]` | model load, cache |
| `[AuraSync Voice]` | mic start, permission, receivers |
| `[AuraSync Presence]` | AR gaze pointers |
| `[AuraSync]` (cloud) | Cloud Anchor host/resolve |

**How to read the tables:** *Priority* P0 = must pass before anyone else tests it, P1 = should pass, P2 = nice to have. Record results with the template in 4.10.

---

### 4.2 Smoke tests (single device)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-00 | P0 | Install the APK. | The app **icon appears in the launcher** and the install screen's **Open** button is enabled. If not, see "If the app installs but has no icon" in section 3. |
| S-01 | P0 | Fresh install. Launch the app. | AR scene loads, camera feed shows, no crash. **No microphone permission prompt yet.** |
| S-02 | P0 | Launch the app. | The **room panel opens by itself** (name, Create, Join, *Continue without a room*). After closing it, a **Multiplayer** button top-left reopens it. |
| S-03 | P0 | Offline (not in a room): place a sticky note and a 3D object. | Placed on the surface. **No "Hosting … to Cloud" message** appears. |
| S-04 | P0 | Force-close the app and reopen it. | Both items are restored in place. |
| S-05 | P1 | Open Space menu → Your Models → pick a model. | `SpaceScene` loads, loading overlay shows progress, model appears fitted and upright. |
| S-06 | P1 | In Space: drag to rotate, pinch to zoom, two-finger drag to pan. | Smooth, no freeze. |
| S-07 | P1 | Back button from Space. | Returns to the AR scene (scene name is `V1`). |
| S-08 | P1 | Load the heavy model for the first time, then again. | First load shows download/optimise stages without the app freezing ("not responding"); second load is faster (cache). |

---

### 4.3 Rooms and identity

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| R-01 | P0 | A | On the panel that opens at launch, type a name. Tap **Create New Room**. | Status "Creating room…" then the panel switches to the in-room view showing `AURA-XXXX` and `1/5 players`. |
| R-02 | P0 | A, B | On B's launch panel, enter A's code, tap **Join Room**. | B joins; A's player count becomes `2/5`; B's panel shows the same code. |
| R-03 | P0 | A, B | B joins by **pasting the full invite link** (`https://aurasync.app/join?room=AURA-XXXX`). | Join succeeds (code is extracted from the link). |
| R-04 | P1 | B | Enter `AURA-0000` (non-existent). | Red error "Room … not found". No crash. |
| R-05 | P1 | B | Enter garbage like `hello`. | Red error asking for a valid code. |
| R-06 | P1 | A, B | Check names: A = "Alice", B = "Bob". | Each sees the other's real name on badges/pointers (not "Player 2"). Name persists after restarting the app. |
| R-07 | P1 | A | While in a room, open the panel. | Shows **Copy Invite Link**, **Copy Room Code**, **Leave Room** (no create/join). |
| R-08 | P1 | A | Tap Copy Invite Link, then paste into any text field. | Pasted text is `https://aurasync.app/join?room=AURA-XXXX`. |
| R-09 | P1 | B | Tap Leave Room. | B returns to the lobby view; A's count drops to `1/5`. |
| R-10 | P1 | B | Rejoin the same room after leaving. | Works. |
| R-11 | P1 | A | Try **Create** again while already in a room (if reachable). | Message "already in a room"; no second room. |
| R-12 | P2 | 6 devices | Fill the room with 5, try a 6th. | Sixth gets "Room is Full". (Skip if you do not have 6 clients.) |
| R-13 | P1 | A, B | On first launch, tap Create/Join **immediately** (within 1–2 s). | Either works, or a clear "not ready" message. No silent failure. |
| R-14 | P0 | A, B | A has a model open in a room. B launches the app fresh and joins A's code from the launch panel. | B's panel says "Loading the shared model…", the 3D view opens by itself, the model downloads (progress shown) and appears; the panel closes. |
| R-15 | P1 | A, B | A creates a room but has **not** opened a model. B joins. Then A opens a model. | B sees "Waiting for the presenter to open a model…". When A opens one, B is taken into the 3D view automatically. |
| R-16 | P1 | B | On the launch panel tap **Continue without a room**. | Panel closes; normal solo AR app. |
| R-17 | P0 | A | Launch the app. Look at the lower-left corner in every screen: launch screen, AR view, 3D model view. | A small **ROOM** button is present in all of them and never disappears. |
| R-18 | P0 | A | Tap the ROOM button. | The join/share panel opens. |
| R-19 | P0 | A | With the panel open, tap the dark area **outside** the card. | The panel closes. The ROOM button is still there. |
| R-20 | P0 | A | Tap the ROOM button again. | The panel opens again. Repeat open/close five times - it works every time. |
| R-21 | P1 | A | With the panel open, tap the ROOM button itself. | The panel closes (the button toggles). |
| R-22 | P1 | A | With the panel open, tap **inside** the card but not on a control (for example the title). | The panel stays open. Only taps outside it dismiss it. |
| R-23 | P1 | A | Open the panel while the 3D model is on screen, dismiss it, then drag the model. | The model did not rotate while the panel was open, and rotates normally afterwards. |
| R-24 | P2 | A | In the Space scene check the ROOM button does not sit on top of Leave Meeting or the part chip. | No overlap. If it does overlap on your screen size, change *Launcher Anchor Min / Max* on `RoomLobbyUI`. |

---

### 4.4 Presenter / viewer and model sync (Space scene)

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| M-01 | P0 | A, B | A opens a model in Space, then creates a room from the Share button. B (in the **AR scene**) joins A's code. | **B is taken into `SpaceScene` automatically** showing A's model. |
| M-02 | P0 | A, B | B is already in `SpaceScene` (own model), then joins the room. | B's model switches to A's model. |
| M-03 | P0 | A, B | A rotates, zooms and pans the model. | B's model mirrors smoothly, within about half a second. Top bar shows A as PRESENTER and B as VIEWER. |
| M-04 | P0 | A, B | B drags the model. | Mirroring stops (B explores freely). **No sudden jump** of B's model when the drag starts. A is unaffected. |
| M-05 | P0 | A, B | After M-04, B taps **Catch Up with Presenter**. | B's model snaps to A's view; the button changes to **FOLLOWING PRESENTER**. |
| M-06 | P1 | A, B | While following, B taps the button again. | Following stops, button returns to "Catch up". |
| M-07 | P1 | A, B | A stops moving the model for 5 s while B has been following. | B stays exactly on A's final pose (heartbeat keeps it in sync). |
| M-08 | P1 | A, B, C | A and B are in the room. C joins afterwards. | C sees A's current model **and** current rotation/zoom immediately. |
| M-09 | P1 | A, B | A switches to a different model. | B loads the new model automatically; the old part highlight (if any) is cleared. |
| M-10 | P0 | A, B | A taps B's badge **once**. | Badge shows "Tap again: make presenter" for about 3 s, nothing else happens. |
| M-11 | P0 | A, B | A taps B's badge twice within 3 s. | B becomes PRESENTER, A becomes VIEWER on both screens; B's moves now drive A. |
| M-12 | P1 | A, B | After M-11, A rotates the model. | A's model does **not** drive B; A is now in follower mode. |
| M-13 | P0 | A, B, C | A (presenter) force-closes the app. | Within a few seconds one of B/C becomes PRESENTER automatically; the room keeps working. |
| M-14 | P1 | A, B | A taps **Leave Meeting**. | A leaves; B is promoted to presenter. A's HUD returns to offline layout. |
| M-15 | P1 | B | B is a viewer and is not in a room: open a model. | No Catch Up button is shown (only shown in a room as viewer). |
| M-16 | P0 | A, B | Look at the top of the 3D view on both phones. | There is **no "Admin Mode" toggle** any more. Roles show only as PRESENTER / VIEWER. |
| M-17 | P0 | A, B | B (following) taps a part, and holds a finger still on the model to point. A then rotates the model. | B is **still following**: B's model keeps moving with A's. Button still says FOLLOWING. |
| M-18 | P0 | A, B | B (following) presses Mic and Copy Link. A then rotates. | B still follows. Pressing buttons never breaks following. |
| M-19 | P1 | A, B | B (following) drags clearly across the screen. | Only now does B switch to free exploring (button: CATCH UP). |
| M-20 | P1 | A, B, C | A hands presenter to B (M-11). A then rotates A's own model. | C's model does **not** move with A, only with B. |
| M-21 | P0 | A, B | B is following. A rotates the model continuously. B **does not touch the screen at all** for 20 s. | B's model keeps mirroring A the whole time. This is the round-3 regression test: before the fix, mirroring only ran while a finger was on the screen. |
| M-22 | P0 | A, B | B presses and **holds** the Catch Up / Following button for 5 s while A rotates, then lets go. | Mirroring looks exactly the same during the hold and after releasing. No difference. |
| M-23 | P1 | A, B | Watch Unity's log on B while mirroring (optional, if adb works). | No `InvalidOperationException` about `UnityEngine.Input` anywhere. |
| M-24 | P1 | A | Open a model in the Space scene and read the log once. | If a warning appears saying `SpaceModelViewerFix is retired`, delete that component from **ModelAnchor** in SpaceScene. Model rotation must still work normally. |
| M-25 | P1 | A | In the AR scene while in a room, look at the HUD. | There is **no Follow Admin button and no Rooms button** there any more. Mic, room code, copy and the role badge remain. |

---

### 4.5 Part highlight and live pointers

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| P-01 | P0 | A, B | A **taps** a part of the model. | The part gets a **gold** translucent box on both devices; the chip shows "Presenter: <part name>". |
| P-02 | P1 | A, B | A taps the same part again, then taps empty space. | Highlight clears on both. |
| P-03 | P1 | A, B | B (viewer) taps a part. | **Cyan** highlight on B only; chip shows "You: <name>". A sees nothing. |
| P-04 | P1 | A, B | A and B select different parts. | Both highlights visible on B (gold + cyan). |
| P-05 | P1 | A, B, C | C joins while A has a part selected. | C sees the gold highlight on arrival. |
| P-06 | P1 | A, B | A switches models while a part is selected. | Selection cleared. |
| P-07 | P1 | A | Tap on a HUD button (Share, Mic). | The button works and **no part is selected** behind it. |
| P-08 | P1 | A | Drag to rotate, release. | Releasing a drag is **not** treated as a tap. |
| P-09 | P1 | A, B | A **holds** a finger on the model. | B sees a coloured dot with A's name pill at that spot on the model. |
| P-10 | P1 | A, B | A lifts the finger. | The pill stays about 1 s on B, then fades out. |
| P-11 | P1 | A, B | B rotates and zooms their own copy, A touches the same feature (for example a door). | The pill lands on **the same feature** on B's model, wherever it now is on screen. |
| P-12 | P1 | A, B | While B's finger is on the pill's location. | The pill never blocks B's touches. |
| P-13 | P2 | A, B | Heavy model: hold and drag a finger around. | Pointer still follows; no visible frame-rate collapse. |
| P-14 | P0 | A, B | A does a **quick tap** on the model (under a quarter second). | B still sees A's pill appear at that spot for about 1 s. |
| P-15 | P1 | A, B | A touches the background **beside** the model. | B still sees A's pill near the model at the matching screen spot. |
| P-16 | P1 | A, B | A touches a non-button area of the HUD (top bar background, part chip). | Pointer still sent (decorative panels no longer block touches). |

---

### 4.6 Voice

Use headphones on every device.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| V-01 | P0 | A | Create a room. | Microphone permission prompt appears **now** (not before). Grant it. |
| V-02 | P0 | A, B | Both unmuted, A speaks, then B speaks. | Each hears the other clearly, no echo, no repeating words. |
| V-03 | P0 | A, B | A taps **MIC ON** → **MIC OFF**. | A's badge on B shows "(muted)"; B hears nothing from A. Unmute restores it. |
| V-04 | P1 | A, B | A speaks. | On B, A's badge **brightens** while A talks and returns to normal when A stops. |
| V-05 | P1 | A, B | A stays silent for 30 s. | B hears nothing (no hiss/noise). Log shows no steady voice packets. |
| V-06 | P1 | A, B | Speak a short sentence; measure by ear. | Delay under about 0.5 s; no clipped word endings. |
| V-07 | P1 | A, B, C | All three talk (not at once, then overlapping). | All are audible; no crash, no runaway echo. |
| V-08 | P1 | A | Deny the microphone permission. | Button shows **NO MIC**; the rest of the app keeps working; no crash. |
| V-09 | P1 | A | Leave the room, then play music in another app. | Phone audio returns to normal (no "call mode" volume). |
| V-10 | P1 | A, B | One phone on mobile data, speak for 2 minutes. | Usable quality; note data used (built-in voice: roughly 1 MB per minute of continuous speech, nothing while silent; Photon Voice uses less). |
| V-11 | P2 | A, B | Take a phone call or switch apps mid-session, then return. | Voice recovers, or a clear failure; no permanent silence without a way to fix it (toggle mute twice). |
| V-12 | P1 | A, B | A mutes, then B joins later. | B sees A as muted straight away. |
| V-13 | P0 | A, B | Presenter (A) speaks normally while holding the phone at arm's length, as when handling the model. | **B hears A.** If not, compare both phones' `[AuraSync Voice] 5s` log lines (see section 3, voice troubleshooting). |
| V-14 | P1 | A, B | Read the `[AuraSync Voice] 5s` line on both phones during V-02. | Speaker: `micRecording=True`, `packetsSent` > 0 while talking. Listener: `packetsReceived` shows the speaker's actor. |
| V-15 | P1 | A, B | Photon Voice 2 set up (section 3). Repeat V-02, V-06, V-07. | Log says `Using Photon Voice 2 (Opus)` and `Microphone added`. Clear, natural voice, no echo, delay comparable to a phone call. |
| V-16 | P0 | A, B | Both in a room. Look above the corner ROOM button on each phone. | A voice line is shown, for example `VOICE live \| lvl 0.012 gate 0.004 \| tx 0 rx 0`. |
| V-17 | P0 | A, B | **The one-way audio test.** A (presenter) talks for 10 s. Read both phones' voice lines. | A's `lvl` rises above `gate` and A's `tx` climbs; B's `rx` climbs. Whichever of those three does not happen names the broken step - look it up in the table in section 3. |
| V-18 | P0 | A, B | Repeat V-17 the other way round: B talks, read both lines. | Mirror image. Audio must work in **both** directions, not just one. |
| V-19 | P0 | A | Join a room for the first time after a fresh install, and **wait 30 s before tapping Allow** on the microphone prompt. | Voice still works after allowing. The permission wait no longer gives up; this was a real cause of a presenter being silent for a whole session. |
| V-20 | P1 | A | Deny the microphone permission, then look at the voice line. | It reads `MIC PERMISSION DENIED`. The rest of the app keeps working. |
| V-21 | P1 | A, B | If a phone's `lvl` stays `0.000`, keep watching the voice line for 15 s. | It passes through `recovering: restarting mic`, `recovering: leaving call mode`, `recovering: native sample rate`. If any of those restores sound, note **which one** - that tells us what this phone needs. |
| V-22 | P1 | A, B | Speak very quietly, just above a whisper, for 10 s. | Either the gate passes it, or within 8 s the line shows `live (gate forced open)` and the other phone starts hearing it. |

---

### 4.7 AR collaboration (Cloud Anchors)

Needs good surfaces and a few seconds of scanning before placing.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| C-01 | P0 | A, B | In the room, A places a **shared** (not private) note. | "Hosting Note to Cloud…" then "Cloud Anchor Hosted!". |
| C-02 | P0 | A, B | B points at the same area. | The note appears on B at the **same physical spot** (within a few cm), readable. |
| C-03 | P1 | A, B | A places a **private** note. | It appears only on A; no cloud hosting message. |
| C-04 | P1 | A, B | A places a 3D object. | Same as C-01/C-02 for objects. |
| C-05 | P1 | A | Place a shared note, force-close the app, reopen **within a day**, rejoin the room. | Note restored; its cloud ID survived (log shows it re-sent to a new joiner). |
| C-06 | P1 | A, B, C | A has 2 shared items placed. C joins later. | C receives and aligns **both** items; no duplicates on A or B. |
| C-07 | P1 | A, B | B moves around the room. | On A, B's gaze pointer (cone + name) appears **in the right place** once the shared origin exists, and follows B. |
| C-08 | P1 | A, B | B joins but has not yet resolved any anchor. | No gaze pointer is shown for B at wrong coordinates (hidden until aligned). |
| C-09 | P1 | A | Place a shared item with no internet on A (after joining). | "Failed to host Cloud Anchor…" message; app keeps working; no crash. |
| C-10 | P2 | A, B | Wait over 24 h, then rejoin and resolve an old anchor. | Resolve fails gracefully ("Falling back"); no crash. |
| C-11 | P2 | A, B | A deletes a shared note (long press). | Deleted on A only (known gap: deletes/edits are not synced). |
| C-12 | P1 | A | Restart the app and look at restored items as you walk around. | Items stay put (restored items now carry an anchor). |

---

### 4.8 Invite links (deep links)

Requires the manifest intent filter from section 3 (skip this whole group if you did not add it). Replace `AURA-XXXX` with a live room code.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| L-01 | P1 | B | App **closed**. Run `adb shell am start -a android.intent.action.VIEW -d "aurasync://join?room=AURA-XXXX"`. | App launches and joins the room. |
| L-02 | P1 | B | App **open in the background**, run the same command. | App comes forward and joins. |
| L-03 | P1 | B | Already in a room, run the command. | Ignored; stays in the current room. |
| L-04 | P1 | B | Send `aurasync://join?room=BAD`. | Ignored; no crash, no wrong join. |
| L-05 | P2 | B | Tap an `https://aurasync.app/join?...` link in a chat app. | Opens the app **only if** App Links are set up. Otherwise opens the browser (expected for now). |

---

### 4.9 Robustness and performance

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| X-01 | P0 | A, B | Put B in the background for 30 s, then return. | B is still in the room, or shows a clear message; can rejoin with the code. |
| X-02 | P0 | A, B | Toggle B's airplane mode on for 10 s, then off. | B shows a disconnect message; rejoining works; A is unaffected. |
| X-03 | P1 | A, B | Lock/unlock B's screen mid-session. | AR/Space resumes; no black screen. |
| X-04 | P1 | A, B | Kill and relaunch B, rejoin quickly. | Works; B does not appear twice on A. |
| X-05 | P1 | A | Run 10 minutes in a room with Space + voice. | No crash; device not excessively hot. |
| X-06 | P1 | A, B | Heavy model + voice + pointers together. | Stays interactive (aim for 25 fps or better on a mid-range phone). Note device model and fps. |
| X-07 | P2 | A | Check `adb logcat` for repeated exceptions. | No `NullReferenceException`, `MissingReferenceException`, or duplicate-PhotonView errors during normal use. |
| X-08 | P2 | A, B | Rotate the phone / change window size in Space. | HUD stays on screen and tappable. |
| X-09 | P1 | A, B | Two people join at the exact same moment. | Both land in the room; counts are correct. |
| X-10 | P2 | A | Fill the model cache (open several large models). | App does not run out of storage; oldest cached models are removed above 512 MB. |

---

### 4.10 Results template and bug reports

Copy this table per test session.

| Field | Value |
|---|---|
| Build / date | |
| Devices (model, Android version) | A: · B: · C: |
| Network (Wi-Fi / mobile data) | |

| Test ID | Result (Pass / Fail / Blocked) | Notes / log excerpt |
|---|---|---|
| S-01 | | |
| *(add one row per case run)* | | |

**For every failure, capture:**
1. Test ID and exact steps.
2. Which device (A/B/C) showed the problem.
3. `adb logcat -s Unity` from **all** devices for that run (the other phone's log often explains it).
4. A screen recording if it is visual (pointers, highlights, AR alignment).
5. Whether it repeats (always / sometimes / once).

**Suggested order:** S-01–S-04 → R-01–R-03 → M-01–M-05 → V-01–V-03 → P-01 → C-01–C-02 → everything else. If any P0 in a group fails, fix it before continuing with that group.

---

### 4.11 Optional desktop pre-flight

Photon runs on desktop, so a Windows standalone build plus the Editor can check the non-AR cases (R-*, M-*, P-*, most V-*) before you go to the phones. It cannot replace phone testing for permissions, audio-mode behaviour, Cloud Anchors, performance or backgrounding.


## 5. Known gaps / not built

- ~~Editing or deleting an AR note/object is **not** synced to other players.~~ **Built, see 2f.**
  Two gaps remain inside it: a player who was in the room but missed the message (backgrounded, or a
  dropped packet) is not caught up, and two people editing the same item at once is last-write-wins
  with no conflict resolution. Tests I-31 and I-32 cover both.
- Cloud Anchors made with an API key expire after 1 day.
- Firestore is still unauthenticated; KIRI capture is still a mock.
- `SpaceModelViewerFix.cs` is retired: it deletes itself at startup and logs a warning. Remove the component from **ModelAnchor** in SpaceScene when convenient.
- Voice: the presenter-silent problem is addressed by three fixes plus two watchdogs, but the actual cause on your phones is **not yet confirmed**. Run V-17 to V-21 and the on-screen readout will name it.
- Built-in voice cannot reach phone-call quality. Photon Voice 2 (section 3) is still the route to that.
- UI strings that used emoji were replaced with plain text in `SpaceSessionHUD` because TMP's default font has no emoji glyphs. `SessionHUDUI` may still contain some.
- `UIDebugger.cs` was changed by a teammate and was not reviewed.
