# AuraSync V3 – Phase 9 (Settings + session record and replay): build notes and test plan

**SETTINGS** (top right) holds **Record & Replay**. It shows for the presenter, and for anyone using
the app alone.

- **Record:** START RECORDING. A red **REC 0:42** shows in the top-right corner. Everyone in the
  room sees **REC** and a toast saying who is recording. STOP asks for a name, and the recording is
  saved on this phone.
- **Replay:** Settings → RECORDINGS → **PLAY**. The model loads and the session plays back in 3D:
  model movement, explode, isolation, the highlighted part, the section cut, the simulation, notes,
  everyone's view cones and pings. A caption line shows what just happened and who is speaking. You
  get a timeline to scrub, PLAY / PAUSE, 0.5x / 1x / 2x, and **PRESENTER VIEW** or **FREE CAMERA**.
- **Share:** SHARE saves a copy to the phone's *Download/AuraSync* folder and opens Android's share
  sheet.

Also in this round: the **Phase 8 cone follow-up** (see `V3_PHASE_8_TESTS.md` §6, cases CF-01 – CF-09).

> **Status:** compiled clean (0 errors), including the Android-only share code. The new cone
> shader cannot be compiled outside Unity. **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes, and CF-01 – CF-06.

---

## 1. Decisions made at the start of this phase (2026-10-14)

| Question | Answer |
|---|---|
| **D6:** someone using the app alone has no presenter. | **Alone, you count as your own presenter:** SETTINGS with Record & Replay (and Heatmap in Phase 10). AI Quiz will need a room. |
| Where can a recording be replayed? | **On this phone, outside a room.** In a room the list says *"Leave the room to replay"* (SHARE still works). No *Replay to room* in V3. |
| How are recordings exported? | **Android's share sheet, without plugins:** a copy goes to *Download/AuraSync*, then the share sheet opens. No importing on another phone in V3. |
| What happens to a recording on a model switch or Back? | **It stops** (one recording = one model) and is saved under a suggested name. |

Defaults chosen without asking (say if any should change):

- **What is recorded is what the presenter's phone shows.** The presenter's model angle and zoom
  are the "presenter's camera". A viewer exploring on their own phone is not recorded, but their
  view **cone** is.
- **The presenter's voice is recorded** (added 2026-10-14 at your request, see §6). Other people's
  voices are not, only *when* they spoke, shown in replay as *"Speaking: Asha"*.
- **Shared AI answers** appear in replay as a caption (*"Asha shared an AI answer on Fan rotor:
  …"*), not as the card.
- **Notes** in replay are the recording's notes, read-only. Your own notes come back when the
  replay ends.
- **While replaying,** the left rail is hidden (the replay drives the model). Joining a room ends
  the replay.
- **Leaving the replay** (EXIT, Back) puts the model back exactly as it was before PLAY.
- **A recording of another model** opens that model first (downloading it if needed), then plays.
- **If the app is killed mid-recording,** everything up to the last second is kept. The list shows
  it as *"(recovered) …"*.
- **STOP** asks for a name, with a suggestion such as *"JetEngine_v2_mobile 14 Oct 10:42"*: SAVE,
  or KEEP SUGGESTED.
- **DELETE** needs two taps (*SURE?*).
- **Files:** `<persistentDataPath>/Recordings/rec_<date>.aurarec`. Each is a text file with one
  event per line.

## 2. What was built

| File | What |
|---|---|
| `Recording/SessionRecording.cs` | **New.** The file format, listing / loading / deleting, recovered recordings, the size-per-minute stats line. |
| `Recording/SessionRecorder.cs` | **New.** Records changes as they happen, flushes every second, sets the room-visible REC flag, stops on a model switch. |
| `Recording/SessionPlayer.cs` | **New.** Replays by seeking to state (scrubbing is instant), drives each feature locally, captions, *Speaking*, presenter view / free camera, restores the model afterwards. |
| `Recording/RecordingExport.cs` | **New.** SHARE: Download/AuraSync via MediaStore plus the share sheet (Android 10+), else a copy in `Recordings/Export`. |
| `UI/SettingsPanelUI.cs` | **New.** SETTINGS and REC in the corner row, the Settings panel, the name box. |
| `UI/RecordingsUI.cs` | **New.** The recordings list and the replay bar. |
| `Networking/EventCodes.cs` | `PlayerKeys.Recording`, a **player** property, so REC disappears by itself if the recording phone drops out. |
| `Notes/PartNoteManager.cs`, `Simulation/SimulationController.cs`, `Networking/AttentionSync.cs`, `Space/AttentionVisuals.cs`, `Space/ModelStateMemory.cs`, `UI/SpaceSessionHUD.Slots.cs`, `Space/SpaceSceneManager.cs` | Replay hooks: the recording's notes (read-only), the simulation state, cones / pings / names, *"don't remember a replay as how I left the model"*, hiding the rail, creating the components, a *Recording* stats line. |

**Setup in Unity:** none (the Phase 8 cone shader is added to Always Included Shaders by the build hook).

---

## 3. Tests

Jet Engine unless stated. **RS** cases: one device, no room. **RR** cases: 2–3 devices; A is the
presenter.

### 3.1 Settings and recording, alone

| ID | Pri | Steps | Expected |
|---|---|---|---|
| RS-01 | P0 | Open a model, no room. | **SETTINGS** in the top-right corner. Tap it: *SETTINGS* panel, *RECORD & REPLAY*, **START RECORDING**, a hint (model and your voice), **RECORDINGS (0)**. X / Back closes it. |
| RS-02 | P0 | START RECORDING. | Toast *"Recording."*; a red **REC 0:01** in the corner, counting. |
| RS-03 | P0 | For about a minute: turn and zoom, explode, isolate a part and show all, select parts, section cut on and moved, Simulate → Working for a few seconds, add a note, long-press a ping. | Everything works as normal while recording. |
| RS-04 | P0 | Tap **REC** (or Settings → STOP RECORDING). | *"Name this recording"*, with a suggested name. Type *"Test 1"* → SAVE. Toast *"Recording saved…"*; REC disappears. |
| RS-05 | P0 | Settings → RECORDINGS. | *"Test 1"*, with *JetEngine_v2_mobile · date · 1:0x · NN KB (NN KB/min)*, and **PLAY**, **SHARE**, **DELETE**. |
| RS-06 | P0 | PLAY. | The rail disappears. The replay bar shows *REPLAY · Test 1*, EXIT, a caption line, the timeline, **PAUSE**, **1x**, **PRESENTER VIEW**. The model repeats **everything from RS-03 in order**: turning, explode, isolate, selection, the cut, the spinning engine, the note appearing (caption *"You added a note…"*), and the ping rings (*"You: look here"*). |
| RS-07 | P0 | Drag the timeline back and forth. | The model jumps straight to each moment: the right explode, isolation, cut, note set and simulation step. No replaying from the start. |
| RS-08 | P0 | PAUSE / PLAY; **1x** → 2x → 0.5x. | It freezes and continues (the spinning engine too). Speed changes take effect at once. At the end the button reads REPLAY, which starts it again. |
| RS-09 | P0 | **PRESENTER VIEW** → FREE CAMERA, then turn and zoom the model yourself while it plays. | You control the angle; explode, isolate etc. still play. Back to PRESENTER VIEW: the recorded angle returns. |
| RS-10 | P0 | Note the model's state before PLAY (e.g. exploded a little, a part selected). PLAY, then **EXIT** (or phone Back). | The rail returns and the model is **exactly** as before PLAY. The recording's note is gone; your own notes are back. |
| RS-11 | P1 | Record on the **House**, then open the **Jet Engine** and PLAY the House recording. | The House opens, then the replay starts by itself. |
| RS-12 | P1 | Record, then switch model / press Back mid-recording. | Recording stops. Toast *"Recording stopped: the model changed…"* (switch). The recording is in the list under its suggested name. |
| RS-13 | P1 | Record for 20 s, then kill the app from the recent-apps screen. Reopen and go to RECORDINGS. | *"(recovered) …"* with about 20 s. It replays. |
| RS-14 | P1 | DELETE once, then again. | First tap: *SURE?*. Second tap within 3 s: gone. One tap and a wait: nothing deleted. |
| RS-15 | P0 | **Phone:** SHARE. | Toast *"Saved to Download/AuraSync/Test 1.aurarec."* and Android's share sheet opens. Send it to yourself (Drive / WhatsApp / email): the file arrives. The Files app shows it under Download/AuraSync. |
| RS-16 | P1 | Editor: SHARE. | Toast *"Saved to …/Recordings/Export/Test 1.aurarec"*. |

### 3.2 In a room (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| RR-01 | P0 | A (presenter) and B in a room. | A has SETTINGS; **B does not**. |
| RR-02 | P0 | A: START RECORDING. | A: *"Recording, with your voice. Everyone in the room sees REC."*, REC with a timer. B: red **REC** in the corner and a toast *"A is recording this session, with their own voice. Other voices are not recorded."*. B taps REC: who is recording and what is recorded. |
| RR-03 | P0 | C joins while A records. | C sees REC at once. |
| RR-04 | P0 | For 2–3 minutes: A shares explode / isolate / section / simulation. B turns away (cone), pings, adds an *Everyone* note, shares an AI answer, speaks. Both talk. A runs a short FIND task. C leaves. | Normal session. |
| RR-05 | P0 | A stops, names it, leaves the room, and PLAYs it. | Replay shows A's model angle, the shared states, **B's cone** moving, B's ping (*"B: look here"*), B's note, and captions *"B shared an AI answer on …"*, *"FIND: …"*, *"C left"*. *"Speaking: A"* / *"Speaking: B"* appear when each talked. |
| RR-06 | P0 | A in the room: Settings → RECORDINGS. | *"Leave the room to replay a recording. You can still share one."*; PLAY greyed, SHARE works. |
| RR-07 | P1 | A's phone drops out of the room while recording (airplane mode). | B's REC disappears within a few seconds. A's recording keeps going locally. |
| RR-08 | P1 | A hands the presenter role to B mid-recording. | A's recording carries on. B gets SETTINGS; A loses it but keeps REC (tap = stop). |
| RR-09 | P1 | Replay at 2x with several cones and pings. | Cones and pings follow at the replay speed; pings fade out properly. |

### 3.3 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| RP-01 | P1 | Record a 10-minute two-phone session using every feature. Screen-record the same session on A with Android's screen recorder. | Compare the recording's **KB/min** (in the list) with the screen recording's MB/min. |
| RP-02 | P1 | Record **30 minutes**. | The list shows its size: expected a few MB at most. Replay and scrubbing stay smooth. |
| RP-03 | P1 | Debug: **V3 debug: AI, search, challenge and control stats** (Editor). | A *"Recording: N recording(s), … average NN KB per minute …"* line. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_8_TESTS.md` | **CF-01 – CF-09**, **AR-02, AR-07** | Cone follow-up; replay drives cones and pings. |
| `V3_PHASE_5_TESTS.md` | **N-02 – N-04** | Notes gained a replay mode (read-only). |
| `V3_PHASE_7_TESTS.md` | **SW-01, SR-01** | The simulation gained a replay entry point. |
| `V3_PENDING_DEVICE_TESTS.md` | **MS-01** | Model switches stop a recording, and per-model memory now skips a replay. |
| `V3_PHASE_1_TESTS.md` | **G-01** | The top-right corner row now holds REC and SETTINGS. |

## 5. Paper metrics

- **Recording size per minute** against a screen recording of the same session (RP-01). The
  list shows KB/min for every recording 30 s or longer, and the stats line gives the average.

---

## 6. Added 2026-10-14: the presenter's voice

You pointed out that a replay is of little use without the explanation. A recording now keeps
**the recording phone's own voice** (your choice: presenter only), at **speech quality, about
1 MB a minute** (your choice).

- **In a room** it is the stream Photon Voice already sends, after echo cancelling and noise
  suppression, taken through a hook on the voice Recorder (`VoiceRecordingTap`, the way Photon's
  own *save outgoing stream* sample does it). There is no second microphone. **While the presenter is
  muted nothing is kept:** Photon does not process a muted mic at all, and the recorder checks MIC OFF too.
- **Alone,** the phone's microphone is opened directly, so you can record a narrated walkthrough.
  Voice search borrows the mic for a moment; the recording is silent for that moment and then
  carries on.
- **The file:** `rec_<date>.wav` beside the `.aurarec`. It is 16 kHz µ-law (a standard WAV any phone
  plays). Gaps are filled with silence, so the voice stays on the timeline.
- **Replay** plays it in step: scrubbing, PAUSE and the speed move it too (at 0.5x / 2x the pitch
  shifts with it; Unity cannot keep pitch while changing speed).
- **SHARE** sends both files. DELETE removes both. The list says *"with voice"* or *"no sound"*, and
  KB/min counts both files.
- **Everyone is told:** the REC toast says the presenter's voice is recorded and nobody else's.
- A recording made with the fallback voice system (Photon Voice not set up) has no sound.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| RV-01 | P0 | **Phone, alone:** START RECORDING (allow the microphone if asked). Talk while turning and exploding the model for 30 s. STOP, SAVE. | The list shows *"with voice"*. PLAY: you hear yourself **in step** with what the model does. |
| RV-02 | P0 | During RV-01's replay: drag the timeline back, PAUSE, PLAY, then 2x and 0.5x. | The voice jumps with the timeline, stops and resumes with PAUSE, and speeds up / slows down (pitch changes). It never runs ahead or behind the model by more than a moment. |
| RV-03 | P0 | **Room:** A (presenter) records while explaining for 1–2 minutes. B also talks. A mutes for 10 s in the middle, talking anyway, then unmutes. | Replay on A (out of the room): **A's explanation** is heard in step. **B is not heard** (*"Speaking: B"* still shows). The muted 10 s are **silent**. The voice stays in step after the gap. |
| RV-04 | P0 | RV-03's toasts. | A: *"Recording, with your voice…"* (or *"Your mic is off…"* if A started muted). B: *"A is recording this session, with their own voice. Other voices are not recorded."* |
| RV-05 | P1 | Alone, recording: open the AI chat and use voice search, then carry on talking. | Search works. The recording has a short silence there, then your voice continues in step. |
| RV-06 | P1 | Replay at the end of a recording, and EXIT mid-sentence. | The voice stops with the replay; nothing keeps playing after EXIT. |
| RV-07 | P0 | **Phone:** SHARE a recording with voice. | The share sheet sends **two files**. The `.wav` plays in the phone's music/files app on its own. |
| RV-08 | P1 | DELETE it. | Both files are gone from `Recordings`. |
| RV-09 | P1 | A 10-minute recording with talking. | The list shows about 1 MB a minute (sound) plus the 3D events. |

**Regression:** `V3_PENDING_DEVICE_TESTS.md` voice cases (**VX**): the room's voice chat must sound
exactly as before. The tap only reads the stream.
