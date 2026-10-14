# AuraSync V3 – Phase 8 (Collaborative attention awareness): build notes and test plan

In a room, you see **where everyone else is looking** on the model:

- **View cones:** a thin see-through cone in each person's colour, coming from the direction they
  look from and ending in a ring on the spot they look at, with their name. People who look at the
  same spot from the same angle (viewers following the presenter) share one cone: *"Asha + 2"*.
- **Look here:** hold one finger still on the model for about half a second. A ring pulses on that
  spot on every phone for 3 seconds, with your name.
- **VIEWS** on the left rail hides or shows the cones on your phone only. Pings still show.
- **FIND** (presenter only) runs the user-study task: everyone else is asked to find a named part
  on their model, and each person's time is logged, with cones on or off.

> **Status:** compiled clean (0 errors). **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions made at the start of this phase (2026-10-14)

| Question | Answer |
|---|---|
| How does another person's view look? | **Cone + ring + name tag**, as planned. A spot on the far side of its part gets a faint tag, *"· behind"*. |
| Viewers following the presenter look at exactly the same spot. How do their cones show? | **Merged into one:** *"Asha + 2"*. |
| How is the paper's *"time to find a named part, with and without cones and pings"* collected? | **A FIND task tool:** the presenter picks a part and starts the task with cones ON or OFF; each person's time to tap that part is logged. |
| How is *Look here* triggered? | **Long-press** (finger still for 0.6 s) on the model. |

Defaults chosen without asking (say if any should change):

- **"Where you are looking" is the spot at the centre of your screen.** Phones have no eye tracking.
  If the centre of your screen is beside the model, the spot is in the air level with the model's
  middle.
- A cone that **matches your own view** is drawn as its ring only (a cone seen end-on is just a
  ring anyway), tagged *"(same view)"*.
- **No cone** for a spot on a part your isolation has hidden, or that your section cut removes.
- **VIEWS** appears only in a room. Hiding the cones lasts until the app closes.
- **Pings:** one per person at a time (a new one replaces the old). Your own ping shows on your
  phone too (*"You: look here"*), also without a room. A ping you cannot see is still shown: pinned
  to the nearest screen edge *"(off screen)"*, faint *"(other side)"*, or *"(hidden here)"* when your
  isolation hides its part. Pinging needs a part under your finger.
- **FIND:**
  - Only a **tap on the model** counts. Picking the part in PARTS or search does not.
  - At the start, each searcher **stops following** the presenter, so the presenter turning the model
    does not give the part away. Their PARTS drawer closes, and the presenter's shared highlight is
    cleared.
  - **CONES OFF** hides cones **and** pings on the searchers' phones for that task.
  - The task ends when everyone has found the part, after **2 minutes**, or on **END**.
  - Results are logged **on the presenter's phone**.
- **Network:** each phone sends its view up to 5 times a second, only when it has moved, plus a
  heartbeat every second (about 60 bytes, unreliable). A view not heard for 2.5 s disappears.
  Pings use event 26, as planned. The FIND task uses a new event code, **30**.

## 2. What was built

| File | What |
|---|---|
| `Networking/AttentionSync.cs` | **New.** Sends this phone's view (event 25) and receives everyone else's; long-press detection and pings (event 26); VIEWS on/off; debug test viewers. |
| `Space/AttentionVisuals.cs` | **New.** Cones and rings (plain scene objects, **not** inside the model, so part paths, PARTS and taps never see them), merging, name tags, pulsing ping rings. |
| `Networking/FindTask.cs` | **New.** The FIND task (event 30) and `AttentionLog` (the paper numbers). |
| `UI/AttentionUI.cs` | **New.** VIEWS and FIND on the rail, the FIND panel (presenter), the FIND banner (searchers). |
| `Networking/EventCodes.cs`, `Space/SharedStateBus.cs` | Event 30; `attention.view`, `attention.ping` and `attention.find` on the state bus, ready for recording in Phase 9. |
| `Space/SpaceSceneManager.cs`, `UI/SpaceSessionHUD.Slots.cs` | Creates the four components; debug menu items (below) and an *Attention* stats line. |

Every spot is stored **in its part's own space**, as notes are, and every direction in the
**model's** space. So a cone lands on the same part on every phone, however each phone has turned,
exploded or isolated the model.

**New debug menu items** (three dots on *SpaceSessionHUD* in the Inspector, Play mode):

- **V3 debug: attention - toggle test viewers** adds three pretend viewers, so the cones can be
  checked on one device: *Test 1* looking left of centre, and *Test 2* and *Test 3* sharing one
  view right of centre. Use it again to remove them.
- **V3 debug: attention - test ping** pings the spot in the middle of the screen as *Test 1*.

**Setup in Unity:** none.

---

## 3. Tests

Jet Engine unless stated. **AV** cases need one device (the debug items need the Editor). **AR**
and **FT** cases need 2–3 devices; A is the presenter.

### 3.1 One device

| ID | Pri | Steps | Expected |
|---|---|---|---|
| AV-01 | P0 | Open the model, no room. | **No VIEWS or FIND** on the rail. |
| AV-02 | P0 | Debug: **toggle test viewers**. | Two cones appear. Each is see-through and ends in a solid ring on the model, with a name tag above it. One is tagged *"Test 1"*. The other is tagged *"Test 2 + 1"* (two viewers merged). |
| AV-03 | P0 | Turn the model slowly, all the way round. | Rings stay on **their** spots and turn with the model. When a spot is on the far side, its tag fades and reads *"· behind"*, and the model hides its cone. |
| AV-04 | P0 | Explode the model half way. Then isolate a part that has **no** ring on it. | Exploding: each ring rides with its part. Isolating: rings on the hidden parts **disappear** with them. Show all: they come back. |
| AV-05 | P1 | Isolate the part **under** a ring. | The ring stays on the same spot of the enlarged, centred part. |
| AV-06 | P1 | Zoom in and out. | Cones and rings grow and shrink with the model. |
| AV-07 | P1 | SECTION: move the cut through one of the spots. | That cone disappears while its spot is cut away. |
| AV-08 | P0 | Debug: **toggle test viewers** again. | All test cones gone. |
| AV-09 | P0 | Hold the mouse (finger) **still** on a part for about half a second. | Two rings pulse out from the spot, with *"You: look here"*. They fade after 3 s. **No part gets selected.** |
| AV-10 | P0 | Hold on empty space. Then press on the model and drag at once. | Empty space: no ping. Drag: the model turns, no ping. |
| AV-11 | P1 | Long-press on a rail button. Long-press while placing a note (NOTES → ADD). PLAY (challenge) and long-press a part. | No ping in any of them. The note is placed as usual, and the challenge part drags as usual. |
| AV-12 | P1 | Debug: **test ping**. Then turn the model so the spot goes round the back, then pan it off screen. | *"Test 1: look here"*. At the back it is faint, *"(other side)"*. Off screen it is pinned to the nearest edge, *"(off screen)"*. |
| AV-13 | P1 | SIMULATE → Working. Test viewers on, with a ring on the fan. | The ring rides round with the fan blade. |

### 3.2 In a room (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| AR-01 | P0 | A creates a room on the Jet Engine; B joins. | **VIEWS** on both rails. **FIND** on A's only. |
| AR-02 | P0 | B turns the model (stops following) to look at the fan. | A: a cone in **B's colour**, coming from B's side, ring on the fan, tag *"B"*. Same the other way round for A's view on B. Cones glide smoothly, with no jumping. |
| AR-03 | P0 | A and B turn their models **differently**. Each points the centre of the screen at a different part. | On each phone the other's ring sits on the **same part spot** the other is looking at, whatever angle each phone shows. |
| AR-04 | P0 | B taps **Catch Up** (follows A). Add C following too. | B: A's view is a ring only, *"A (same view)"*. A: one ring, *"B + 1 (same view)"*. |
| AR-05 | P0 | A explodes and shares (ACCEPT). B isolates a part on their own. | Rings still land on the right parts. On B, rings on parts B's isolation hides disappear. |
| AR-06 | P0 | B taps **VIEWS**. | B: toast, rail *"VIEWS (off)"*, no cones. A still sees B's cone (hiding is local). VIEWS again: cones back. |
| AR-07 | P0 | A long-presses a spot. | B (and C): pulsing rings in **A's colour**, *"A: look here"*, for 3 s, **also with VIEWS off**. On the far side: *"(other side)"*. Off B's screen: pinned to the edge. |
| AR-08 | P0 | B leaves the room (or closes the app). | B's cone disappears from A within about 3 s. |
| AR-09 | P1 | B goes back to the model list, then reopens the model. | B's cone vanishes on A within about 3 s, and comes back when B is in again. |
| AR-10 | P1 | A switches model. | No cones are left over from the old model. New cones appear as the others load the new one. |
| AR-11 | P1 | As many phones as available (5 ideal), everyone turning the model. | Smooth frame rate on the slowest phone; cones update promptly. |

### 3.3 FIND task (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| FT-01 | P0 | A taps **FIND**. | A panel at the top centre: *FIND TASK*, a part name with **<**, **>**, **RANDOM**, then **START · CONES ON** and **START · CONES OFF**. < > step through the engine's labelled parts, alphabetically. |
| FT-02 | P0 | A picks *Fan rotor* → **START · CONES ON**. | B, C: banner *"FIND: Fan rotor"* / *"Tap it on the model · 0:00"*, counting. Their Catch Up turns orange (no longer following) and PARTS closes. A: *"Find: Fan rotor · cones ON · 0:03"*, with *"B - searching..."* and *"C - searching..."*. |
| FT-03 | P0 | B taps a wrong part, then the fan rotor. | B: the banner turns green, *"Found it in x s. Waiting for the others..."*. A: *"B - x s (1 wrong)"*. |
| FT-04 | P0 | C finds it too. | The task ends by itself. A: both times, *"No pings during the task."*, and **NEW TASK**. B, C: *"Found it in x s."*, gone about 4 s later. |
| FT-05 | P0 | **NEW TASK** → another part → **START · CONES OFF**. During it, A long-presses the part. | B, C: **no cones and no ping** while searching. When the task ends, cones come back. |
| FT-06 | P0 | Start a task; A taps **END** before anyone finds it. | B, C: *"The task is over."*. A: *"not found (ended)"* for each. |
| FT-07 | P1 | During a task, B leaves the room. | A: *"B - not found (left)"*. The task ends once C has found it. |
| FT-08 | P1 | Start a task and let nobody tap. | It ends at **2:00**: *"not found (time up)"*. |
| FT-09 | P1 | During a task, B opens PARTS and picks the fan rotor there. | Does **not** count. Only a tap on the model does. |
| FT-10 | P1 | A presses BACK while a task runs, then taps FIND. | BACK closes the panel and the task carries on. FIND opens it again on the live view. |
| FT-11 | P1 | A pings during a cones-ON task. | When it ends, A's panel reads *"Pings during the task: 1."* |

### 3.4 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| AP-01 | P1 | After some FIND tasks, run **V3 debug: AI, search, challenge and control stats** on A (Editor), or copy `AttentionLog.jsonl` from A's persistent data folder. | *"Attention: N FIND result(s); cones-on: x of y found, median t s; cones-off: ...; pings sent ..."*. The file has one JSON line per searcher per task: part, condition, found, seconds, wrong taps, pings, players. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_5_TESTS.md` | **N-02 – N-04** | A tap places a note; a long-press must not ping while placing. |
| `V3_PHASE_12_TESTS.md` | **R-10, R-11, R-14** | Holding a loose part still must drag it, not ping. |
| `V3_PENDING_DEVICE_TESTS.md` | **VW-01, VW-03** | FIND stops following; Catch Up must still work afterwards. |
| `V3_PHASE_1_TESTS.md` | **G-01** | The rail gained two buttons next to the badges' column; nothing should overlap. |
| `V2_PARTS_DRAWER_TESTS.md` | tap-to-select cases | Taps and long-presses now share the same finger. |

## 5. Paper metrics

- **Attention user study:** time to find a named part, **cones on vs off**, from FIND
  (`AttentionLog.jsonl`: one line per searcher per task, with the condition, the seconds, wrong taps
  and pings during the task). Run the same parts in both conditions, swapping the order between
  groups.
- **Network cost:** a view message is about 60 bytes, sent at most 5 times a second per person and
  only when it changes, plus a 1 s heartbeat.

---

## 6. Follow-up after the Phase 8 pass (2026-10-14): cone look and who sees whom

You asked for these changes after the pass:

- **The cone looked solid and cheap.** It was meant to be 28% see-through, but Unity's Standard
  shader loses its see-through variant in a phone build, so it drew solid. The cones and rings now
  use a small shader of our own, `AuraSync/AttentionBeam` (`Assets/Shaders/AuraSyncAttentionBeam.shader`).
  It is **30%** opaque, fades towards the cone's tip, and the build hook adds it to Always
  Included Shaders.
- **Half the size**, and the cone **no longer touches the model**. It floats off the spot and
  points at it; a faint ring marks the spot itself. The name sits at the cone's outer end.
- **Zoom moves the cone:** each view now carries the viewer's camera distance. A viewer who zooms
  out has their cone float farther from the model; one who zooms in, nearer.
- **Who sees whom:** the presenter sees every viewer's cone. A viewer sees **only the
  presenter's**, and only after they stop following (Catch Up orange). Viewers now send their view
  to the presenter only.
- **Cones off for everyone:** the presenter's **VIEWS** opens a *VIEW CONES* card with two
  switches, **On my phone** and **For everyone in the room**. A viewer's VIEWS stays a one-tap
  switch for their own phone. Pings are not affected.

**Setup in Unity:** if the cones still look solid on a phone, run **AuraSync > Fix glTF Shader
Inclusion** once, then rebuild. The build does this by itself; this is only a check.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| CF-01 | P0 | **Phone build**, room: A presenter, B turns the model (stops following). Look at A's screen. | B's cone is **see-through** (about 30%), fading to nothing at its tip, half the old size. It **floats off** the model and points at a faint ring on the spot. *"B"* sits at the cone's outer end. From the side it reads as a soft beam, not a solid spike. |
| CF-02 | P0 | B zooms out, then in. | On A, B's cone moves **farther from** the model, then **nearer** (never touching it). |
| CF-03 | P0 | B follows A (Catch Up). | B sees **no** cone. A sees B as a ring, *"B (same view)"*. |
| CF-04 | P0 | B stops following (turns the model). | B sees **only A's** cone. With C in the room too, B never sees C's cone; A sees both. |
| CF-05 | P0 | A: VIEWS. | A card *VIEW CONES*: **On my phone** ON, **For everyone in the room** ON, and a hint. X / BACK closes it. |
| CF-06 | P0 | A: **For everyone** → OFF. | No cones on any phone, A included. B gets a toast: *"The presenter turned view cones off for everyone."* B's rail reads *"VIEWS (off)"*; tapping it explains why. **Pings still show.** ON again: cones return, B gets a toast. |
| CF-07 | P1 | A: **On my phone** → OFF. | Only A stops seeing cones; B still sees A's (when not following). |
| CF-08 | P1 | A opens FIND while the VIEWS card is open (or the other way round). | One replaces the other. |
| CF-09 | P1 | Editor: debug **toggle test viewers**, zoom in and out. | The test cones float, half size, see-through, and stay clear of the model. |
