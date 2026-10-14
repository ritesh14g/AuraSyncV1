# AuraSync V3 – Feature Implementation Plan

Twelve new features on top of V2, split into two tracks:

- **Track A, single device:** built now. Every test can be run in the Unity Editor or on one phone.
- **Track B, multiuser:** built only **after** all multiuser testing is finished: V2's multiuser
  cases first, then everything Track A left on the pending list.

> **Status (2026-10-15):** Track A done, checkpoint passed, **all phases passed**.
> **V3 COMPLETE (2026-10-15):** every phase built and **every test passed**, including the combined
> pass (`V3_COMBINED_TEST_PASS.md`: the Phase 8 cone follow-up, 9 with the presenter's voice, 10, 11
> and 13). Next: UI refinement.

---

## 0. How this plan is worked

1. **One phase at a time, only on your order.** You say *"start Phase N"*, and only then does work on
   Phase N begin. Nothing is built ahead of time.
2. **A phase cannot start until the previous one has passed its tests.** *(Exception you made on
   2026-10-13: Phase 5 was started before Phase 3b was tested; the two are tested in one pass.)* You run that phase's test
   sheet (`V3_PHASE_N_TESTS.md`, written with the code) and report the results. Every P0 case must
   pass. P1/P2 failures can be carried forward if you agree, and are written down as known issues.
3. **In Track A, only the single-device tests are the gate.** Any case in a Track A phase that needs
   two or more phones is written into **`V3_PENDING_DEVICE_TESTS.md`** instead of its phase sheet,
   with the phase it came from. Nothing is skipped silently.
4. **Track B does not start until the device-testing checkpoint (§3.3) is complete.**
5. **Every code change is compile-checked before it reaches you**, using the `gen-rsp.sh` recipe
   (0 errors). Then the scripts are mirrored to the Desktop staging folder.
6. **Each phase re-runs the previous phases' P0 cases that it could break.** Each test sheet ends
   with a *Regression* section listing exactly which ones.
7. **Each phase logs the numbers the research paper needs** (the *Paper metrics* line in each
   phase), so evaluation data is collected while building, not reconstructed afterwards.
8. Scene, prefab and asset files are not edited by text tools. Anything that needs the Unity Editor
   is listed under *Setup in Unity* for you to do.

> Phase numbers are **names, not the build order.** They are kept as they were first planned so
> earlier references stay valid. The build order is the order of the two tables below.

### Track A: single device (build now)

| Order | Phase | Your feature | Why it fits Track A |
|---|---|---|---|
| A1 | 0 | Foundations (no visible feature) | Explode, isolate and the HUD can be checked in the Editor |
| A2 | 2 | Part knowledge file + AI server | Editor tool and backend only, no phones |
| A3 | 3 | AI part assistant chat (#1) | Fully usable on one device. Only *Share to room* is multiuser, and it moves to Phase 3b |
| A4 | 4 | Voice/text semantic search (#7) | Standalone by design. Typed search tests in the Editor, voice on one phone |
| A5 | 12 | Reassembly challenge (#8) | Single-device feature by design |

### Device-testing checkpoint (between the tracks)

V2 multiuser tests, then `V3_PENDING_DEVICE_TESTS.md`, then fixes. See §3.3.

### Track B: multiuser (after the checkpoint)

| Order | Phase | Your feature |
|---|---|---|
| B1 | 1 | Fine-grained ownership (#10) |
| B2 | 3b | AI chat: Share to room |
| B3 | 5 | Notes attached to parts (#2) |
| B4 | 6 | Shared section cuts (#11) |
| B5 | 7 | Simulation, assembly + working animation (#3) |
| B6 | 8 | Collaborative attention awareness (#4) |
| B7 | 9 | Settings button + session record and replay (#5) |
| B8 | 10 | Interaction heatmap per part (#6) |
| B9 | 11 | AI quiz + Reports (#9) |
| B10 | 13 | Bandwidth-aware model loading (#12) |

Notes, section cut, simulation, recording and the heatmap also work on one device. They are in Track
B because their main job is shared state between phones, and their tests mostly need several devices.

**Why this order:** some features depend on others.

- The AI chat, search, challenge, quiz and simulation all read the same per-part knowledge file, so
  that file is built first in Track A.
- The reassembly challenge needs the knowledge file's assembly order, so it comes after Phase 2.
- In Track B, section cut, simulation and notes all need to know *who may control what*, so ownership
  comes first.
- Simulation must work *with* isolation and section cut, so it comes after section cut.
- Recording and the heatmap capture what every other feature does, so they come after those
  features exist.
- Bandwidth-aware loading swaps the model mid-session, and every feature has to survive that swap,
  so it is built last and tested against all of them at once.

---

## 1. Decisions

### Settled (2026-10-09)

| # | Decision |
|---|---|
| D1 | **Free AI only:** Google Gemini and Groq free tiers, each covering for the other. *Updated after the live test on 2026-10-09:* the Gemini 2.5 models are retired for new accounts and Groq's Llama 3.3 is Enterprise-only, so the Worker now uses ordered model lists (`gemini-3.8-flash` for labelling and quizzes, `gemini-3.1-flash-lite` for backup chat, Groq `openai/gpt-oss-120b`). Measured chat speed (Groq 1.4 s against Gemini 7–23 s) put **Groq first for chat and search, Gemini first for quizzes**. Grok and OpenAI were ruled out: neither has a lasting free API. Free-tier content may be used for training, so the chat shows a privacy notice. |
| D2 | **Cloudflare Worker** (free, never sleeps), in `ai-server/`. |
| D7 | **AI proposes, you review** in the Part Labeler; the correction rate is a paper metric. |
| D3 | **Voice input: Groq Whisper through the AI server** (`/transcribe`, Gemini as backup), chosen 2026-10-09 over Android's built-in recogniser because it can be tested in the Unity Editor and behaves the same on every phone. Measured: about 1 s per spoken query, transcribed exactly. |
| D8 | *(New, from the Phase 2 model audit.)* The House is a 3D scan cut into arbitrary chunks, and the jet engine was split by material. So: the engine's blade mesh is **split automatically** (the fan is now its own part, `JetEngine_v2_mobile.glb`); the House gets **whole-model knowledge only**; **2–3 new models with real named parts** are added later for the accuracy evaluation (labelled with the blind-test switch). |

### Still to decide (raised only when their phase is about to start)

These are needed before the phase named. Each has a recommendation. **Each decision is raised only
when its phase is about to start** (after the phase before it has passed its tests), and the options
are researched together at that point, not earlier.

| # | Decision | Needed by | Recommendation |
|---|---|---|---|
| ~~D1~~ | *Settled above.* **Which AI model, and who pays.** The live chat and quiz need an AI call at runtime. | Phase 2 | A small server holds the API key (see D2). Use a vision-capable model for the one-off part labelling, and a cheaper text model for chat and quizzes. Decide between a paid API (best answers) and a free Hugging Face hosted model (free, rate-limited, weaker answers). |
| D2 | **Where the AI server runs.** An API key inside the APK can be extracted, so it must not ship in the app. | Phase 2 | A **Hugging Face Space** (free tier, same account as the models, key stored as a Space secret). A Cloudflare Worker is the alternative. |
| D3 | **Voice input.** Unity has no built-in speech-to-text on Android. | Phase 4 | **Android's built-in speech recognizer** through a small Java bridge: free, fast, no server cost. Speech-to-text on the server (Whisper) is the fallback if the bridge misbehaves on your phones. |
| D4 | **Do notes outlive the session?** Hugging Face is read-only, so there is no shared database. | Phase 5 | Notes are saved **on the author's phone**, per model. When a presenter hosts that model again, their saved notes are re-shared to the room. Works the way AR items already do, with no backend. |
| D5 | **Are AI chat answers private or shared?** | Phase 3b | Private by default, with a **Share to room** button on each answer. Shared answers appear in a box for everyone. |
| D6 | **Solo user and Settings.** Settings items are presenter-only, but a solo user has no presenter. | Phase 9 | A solo user counts as their own presenter: Record/Replay and Heatmap work. AI Quiz needs a room, since there is nobody to send it to. |
| D7 | **Who writes assembly order and motion data?** | Phase 2 | The AI proposes it, **you review and correct it in an Editor tool**, then publish. The research claim (AI-generated) and the demo reliability (human-checked) are both kept, and the paper reports how much correction was needed. |

---

## 2. Architecture shared by all phases

### 2.1 The part knowledge file (built in Phase 2, used by 3, 4, 7, 11, 12)

One JSON file per model, published to Hugging Face next to the GLB and linked from `models.json`
(`"partsUrl": "JetEngine_parts.json"`). It is keyed by the same part path the app already uses
(`ModelRaycaster.GetPartPath`), so it lines up with the Parts drawer, selection, isolation and
explode without any mapping.

```json
{
  "model": "jet-engine",
  "context": "Turbofan jet engine",
  "parts": {
    "Root/Fan/FanBlades": {
      "label": "Fan blades",
      "aliases": ["fan", "front fan", "intake fan"],
      "function": "Draws air into the engine; most of the thrust comes from this bypass air.",
      "material": "Titanium alloy",
      "connectsTo": ["Root/Fan/Spinner", "Root/Core/LPShaft"],
      "motion": { "type": "rotate", "axis": [0,0,1], "speed": 3.0, "group": "lowSpool" },
      "assemblyStep": 4,
      "removeDirection": [0,0,1],
      "reviewed": true
    }
  },
  "assemblyOrder": ["Root/Casing/Cowl", "Root/Fan/Spinner", "..."]
}
```

This is the **Semantic Part Index**, the core research contribution. It is generated **once, at publish
time** (like `optimize-model.py`), not on the phone.

### 2.2 The AI server (Phase 2)

A small HTTPS endpoint (D2) with three routes. The app never sees an API key.

| Route | Used by | Input | Output |
|---|---|---|---|
| `/ask` | AI part assistant (3) | model context, part entry, chat history, question | answer text |
| `/find` | Semantic search (4), fallback only | query, list of part labels/aliases | best-matching part paths |
| `/quiz` | AI quiz (11) | part entries in scope, count, difficulty | multiple-choice questions as JSON |

A limit on requests per user per minute keeps cost bounded.

### 2.3 One owner for part movement (Phase 0)

Today `ModelExploder` and `ModelIsolator` **both write `part.localPosition` directly**. Simulation
would be a third writer, and the reassembly game a fourth. They would overwrite each other: a part
isolated during a working animation would snap back or drift.

Phase 0 adds **`PartPoseComposer`**. Every feature hands it an *offset* instead of moving the part,
and the composer adds them up once per frame:

```
final pose = original pose  + explode offset  + simulation offset  (+ isolation fit, applied last)
```

Notes, the section-cut plane and attention markers are attached to parts in the part's own
coordinates, so they follow the part through every one of those states automatically.

### 2.4 Network message codes (reserved now so nothing collides)

V2 uses event codes 1, 2, 4, 5. V3 reserves **20–39**:

| Code | Phase | Purpose | Delivery |
|---|---|---|---|
| 20 | 1 | Control request / grant / revoke | reliable |
| 21 | 3b | Shared AI answer | reliable |
| 22 | 5 | Note created / edited / deleted | reliable |
| 23 | 6 | Section plane while it is being dragged | unreliable, ≤10 Hz |
| 24 | 7 | Simulation command (play, pause, step, speed) | reliable |
| 25 | 8 | Attention (view direction + focus point) | unreliable, 5 Hz |
| 26 | 8 | "Look here" ping | reliable |
| 27 | 10 | Part interaction counters → presenter | reliable, every 10 s |
| 28 | 11 | Quiz sent to students | reliable |
| 29 | 11 | Quiz answers → presenter | reliable |
| 30 | 8 | FIND study task: start / found / end *(added when Phase 8 was built)* | reliable |

**Room properties** hold the latest state of anything a late joiner must arrive into (section plane,
simulation state, control owners, notes index), following the `ExplodeAmount` pattern already in V2.

### 2.5 HUD layout and back navigation (Phase 0)

The Space HUD gains about eight buttons, which will not fit as loose buttons on a phone. Phase 0
restructures it:

```
 TOP BAR     [<- Back]  Room / Mic / Hear / Players            [Reports (1)] [Settings]
 LEFT RAIL   [AI]  [Simulate]  [Section]  [Notes]  [Attention eye]
 BOTTOM-LEFT [ROOM]  [PARTS]  [BACK]  (as today; the drawer opens right of the rail)
 BOTTOM      [Catch Up]  (viewers, as today)
```

- **The rail is on the left**, not the right: the right edge already holds the player badges in a
  room (found while building Phase 0).
- **`PanelNavigator`:** a stack. Every panel opened (AI chat → follow-up, Simulate → Working
  animation → part info) is pushed, and **Back** always closes the top panel and returns to the one
  under it. The device's Android back button does the same. This satisfies the "proper back and
  forth" requirement once, for every feature.
- Buttons a user may not use (for example Settings for a viewer) are hidden, not greyed out.
- **Reports** is also placed on the **main model-list screen**, so it is reachable with no model open
  and no room joined.

---

## 3. The phases

Each phase lists: what the user sees, how it works, files, multiuser and single-device behaviour, the
test gate, and paper metrics.

---

## 3.1 Track A: single-device phases

### Phase 0 – Foundations

**What you see:** nothing new, except the reorganised HUD.

**Work:**
- `Space/PartPoseComposer.cs` (new). `ModelExploder` and `ModelIsolator` are changed to submit
  offsets through it instead of writing transforms.
- `UI/PanelNavigator.cs` (new): the back stack plus Android back-button handling.
- `UI/SpaceSessionHUD.cs`: left rail and top-right slots, with placeholder buttons hidden until
  their phase ships.
- `Networking/EventCodes.cs` (new): every event code and room-property key in one file (V2's
  existing ones move there too, values unchanged).
- `Space/SharedStateBus.cs` (new): one place every shared action passes through (`Publish("explode",
  0.6)`). The recorder (Phase 9) and heatmap (Phase 10) subscribe to it later, so they never need
  hooks into each feature.

**Test gate (single device):** a re-run of the **V2 single-device P0 cases for explode, isolate, parts
drawer and selection**. This phase changes how parts move, so the existing behaviour must be
identical. Plus new cases: back-stack order, Android back button, HUD on a small phone screen (no
overlaps).

**Pending device tests:** follow/catch-up and presenter-shared explode/selection after the refactor
(the V2 multiuser P0 cases that touch moved code).

---

### Phase 2 – Part knowledge file + AI server (backend)

**What you see:** nothing in the app yet. This builds the data the next phases use.

**Work:**
1. **Audit the two models' part split.** Working animation needs, for example, the jet engine's fan
   as its own part. Count and list parts. If a key part is merged into another, fix it in Blender
   before going further, and confirm `optimize-model.py` keeps the hierarchy (part count before =
   after).
2. **AI server** (D1, D2): the three routes in §2.2, with a rate limit.
3. **Editor tool `Tools → AuraSync → Part Labeler`** (`Editor/PartLabeler.cs`). It runs inside Unity
   so part paths come out **exactly** as the app sees them (same glTFast import):
   - For each part: render 4 views of the part alone and 1 view of it highlighted inside the full
     model.
   - Send the images and the model context to the vision model → label, aliases, function,
     material, connections, motion hint.
   - **Assembly order** from geometry: repeatedly find a part that can slide out along some
     direction without hitting the others (collision sweep), remove it, repeat. The reverse of that
     list is the assembly order.
   - A **review window**: a table of every part with editable fields. You correct anything wrong,
     tick *reviewed*, and export `<Model>_parts.json`.
4. Upload the JSON to Hugging Face and add `partsUrl` to `models.json`. `ModelCatalog` loads it
   alongside the model (missing file = AI features hidden for that model, nothing breaks).

**Test gate:** JSON loads in the app for both models; every path in it resolves with
`ModelRaycaster.FindPart`; server answers all three routes; a request with no key or over the rate
limit is refused cleanly.

**Paper metrics:** labelling accuracy (AI label vs your corrected label, with and without the
"in-context" image), share of fields corrected in review, assembly-order correctness.

---

### Phase 3 – AI part assistant (#1)

**Flow (your spec):**
1. Select a part (tap or Parts drawer) → an **AI** button appears beside it.
2. Tap **AI** → a **chat window opens next to the part**: it sits beside the part's on-screen
   position, with a thin leader line to the part, and moves aside if it would cover the part or run
   off-screen.
3. The first message is the part's info from the knowledge file: what it is, what it does, material,
   what it connects to. It appears **instantly, with no AI call**.
4. **Suggested follow-up questions** appear as tappable chips (*"Why is it made of titanium?"*,
   *"What happens if it fails?"*), plus a text box for your own question.
5. Each answer is generated by the AI using the part's info, the model context and the conversation
   so far. **The conversation carries on**: closing and reopening the window on the same part
   resumes it. Switching parts starts a new thread, and the old one is kept for the session.
6. **Isolation tie-in:** an *Isolate* button in the chat header isolates the part while you keep
   talking about it.
7. *Share to room* is not part of this phase. It is Phase 3b in Track B.

**Single device:** works fully. **Offline:** step 3 still works (it is in the file), and steps 4–5 show
*"AI needs internet"*.

**Files:** `AI/AiClient.cs` (talks to the server), `AI/PartKnowledge.cs` (loads and looks up the JSON),
`UI/PartChatUI.cs` (the window).

**Test gate (single device):** open/close/resume; switch parts; long answers scroll; window placement
on parts at the screen edges; no internet; server slow (spinner, can cancel); Back returns to the
model with the selection intact.

**Pending device tests:** the chat works for a viewer in a room while the presenter changes the
selection, without the window jumping or closing.

**Paper metrics:** answer latency, answers rated helpful (thumbs up/down under each answer).

---

### Phase 4 – Voice / text semantic search (#7)

**What you see:** the **AI** button on the left rail opens a panel with two tabs, **Find** and **Ask**.
*Find* is the search, and *Ask* is the Phase 3 chat for the selected part. This is the "joined with
the AI query button" requirement.

**How it works:**
- Type or tap the mic and say *"show me the turbine"*.
- **Step 1, on the phone, instant:** match against every part's label and aliases from the knowledge
  file (tolerant of typos and word order).
- **Step 2, only if step 1 finds nothing confident:** ask the server's `/find` route, which matches
  by meaning (*"the part that burns fuel"* → combustor).
- Result: the part is selected, highlighted and framed, with an **Isolate** and **Ask AI** shortcut.
  Several matches → a short list to pick from.
- In a room, the presenter's search selects for everyone (gold, as today). A viewer's search is
  local (cyan).
- **Standalone:** works with no room. Step 1 works offline.

**Files:** `AI/PartSearch.cs`, `AI/SpeechInput.cs` + a small Android Java bridge (D3), `UI/AiPanelUI.cs`.

**Test gate (single device):** exact name, alias, typo, meaning-only query, no match, voice in a quiet
and a noisy room, mic permission denied, offline.

**Pending device tests:** the presenter's search highlights the part for viewers in gold; a viewer's
search stays on their own phone in cyan.

**Paper metrics:** search success rate and time-to-find versus scrolling the Parts drawer.

---

### Phase 12 – Reassembly challenge (#8, single device only)

**What you see:** from the model screen (not shown in a room), tap **Challenge** → choose Easy /
Medium / Hard → the parts **scatter** around the model's base, and a faint ghost outline shows where
the model belongs. Drag each part to its place. It **snaps** in when close enough and turns green.
Timer, move counter, and a **Hint** button (highlights the next part in assembly order and its
target). At the end: time, hints used and mistakes give a score and up to three stars. Best scores
are saved per model.

- **Easy:** only the main parts, generous snapping, and the ghost outline is shown.
- **Medium:** all parts, normal snapping.
- **Hard:** must follow the real assembly order (an out-of-order part bounces back), and no ghost.

**How it works:** uses the assembly order and remove directions from the knowledge file, drags on a
plane facing the camera, and moves parts through `PartPoseComposer`.

**Files:** `Challenge/ReassemblyGame.cs`, `UI/ChallengeUI.cs`.

**Test gate (single device):** complete on both models at each difficulty; snapping on small parts;
hints; quit and restart; the challenge button is hidden in a room (one phone alone in a room is
enough); best score saved.

**Paper metrics:** completion time and hints used, before and after studying with the AI assistant.

---

## 3.2 End of Track A

Work pauses here until the test devices arrive.

---

## 3.3 Device-testing checkpoint (between Track A and Track B)

Start this when the test devices are available. Track B starts only after it is finished.

1. **V2 multiuser tests:** `V2_BUILD_NOTES.md` §4 and the multiuser sections of
   `V2_PARTS_DRAWER_TESTS.md` (exploded view sync, AR edit and delete sync). P0 first.
2. **V3 pending list:** every case in `V3_PENDING_DEVICE_TESTS.md`, oldest phase first.
3. **Fix before continuing.** Every failure gets a fix and a re-test. A V2 sync bug found here is fixed
   in V2's code, not worked around in V3's.
4. **Sign-off:** you confirm the checkpoint is complete. Then *"start Phase 1"* opens Track B.

---

## 3.4 Track B: multiuser phases

### Phase 1 – Fine-grained ownership (#10)

**What you see:** the presenter can hand **one control** to a student without giving up the whole
presenter role. Example: *"Asha, you drive the section cut."*

**How it works:**
- Shared controls become **control tokens**: `explode`, `section`, `simulation`, `highlight`. The
  presenter holds all of them by default.
- **Grant:** long-press a player's badge → a list of controls → tick the ones to hand over. The
  student sees *"You now control: Section cut"*.
- **Request:** a viewer taps a locked control → *"Request control?"* → the presenter gets a
  one-tap Allow / Deny prompt.
- **Revoke:** the presenter can take any token back at any time. Tokens return to the presenter
  automatically if the holder leaves.
- **Notes:** only the author or the presenter can edit or delete a note.
- **No race conditions:** token changes use Photon's check-and-set room properties
  (`expectedProperties`). If two people grab a control in the same instant, exactly one wins.
- Solo: you hold every token.

**Files:** `Networking/ControlOwnership.cs` (new); `ModelExploder` and the HUD badge changed.

> **As built (2026-10-12):** tokens for Explode, **Isolate** (shared since the 2026-10-12 fixes)
> and Highlight; Section and Simulation reserved. The badge is a **tap** that opens a menu (not a
> long-press). Requests come from **ASK TO DRIVE** in the Parts drawer. The share popup gained
> **KEEP SHARING** for the presenter and holders alike. The notes rule waits for Phase 5. See
> `V3_PHASE_1_TESTS.md` §1–2.

**Test gate:** grant / revoke / request / deny with 2–3 phones; holder leaves mid-use; presenter
hands over while a student holds a token; two phones grab the same free control together.

**Paper metrics:** conflicts prevented (attempts rejected by check-and-set), request-to-grant time.

---

### Phase 3b – AI chat: Share to room

**What you see:** a **Share to room** button (D5) under any AI answer. Everyone in the room sees that
answer in a box next to the part, with the asker's name. The box can be dismissed per person.

**How it works:** code 21 carries the part path, question and answer text. Late joiners get the
shared answers of the current model from a room property.

**Files:** `UI/PartChatUI.cs` (button), `AI/SharedAnswers.cs` (new).

> **As built (2026-10-12):** D5 settled as recommended. Anyone shares; the asker or presenter
> removes for everyone; a card beside the part with paging; **remembered per model** rather than
> cleared. Room properties (one per answer) instead of event 21. ASK MORE carries the shared answer
> into your own chat. See `V3_PHASE_3B_TESTS.md` §1–2.

**Test gate:** share from presenter and from viewer; box appears next to the right part on every
phone; dismiss is local; late joiner sees earlier shared answers; switching models clears them.

---

### Phase 5 – Notes attached to parts (#2)

**What you see:**
- Select a part → **Notes** → **Add note** → tap the exact spot on the part → type → choose
  **Everyone** or **Only me** → save.
- A note shows as a small pin on the part with a label. It **stays stuck to its spot** when the model
  rotates, explodes, is isolated, is cut, or is animating.
- **Hide / unhide:** the eye icon on the Notes button hides or shows all notes. A long-press on one
  note hides just that one. Hiding is **per person**: it never hides notes for anybody else.
- The Notes panel lists every note on the model. Tapping one flies the camera to it.

**How it works:** each note stores the part path, the point and the surface direction **in the
part's own coordinates**, so it follows the part through every pose (§2.3). It reuses the existing
note styles and editor (`NoteEditorUI`) where possible. Sync uses code 22 plus a notes index in room
properties for late joiners. Persistence follows D4.

**Files:** `Notes/PartNote.cs`, `Notes/PartNoteManager.cs`, `UI/PartNotesPanelUI.cs`.

> **As built (2026-10-13):** D4 settled as recommended, widened to every author (not only the
> presenter): notes live on the author's phone and are shared again whenever the author is in a
> room on that model, so a note is in the room only while its author is. Pin with a short label,
> tap to open; text + colour; HIDE on the note's card instead of long-press. Room properties
> (`PN:`) instead of event 22; pins are screen-space, not objects in the model. See
> `V3_PHASE_5_TESTS.md` §1–2.

**Test gate:** add / edit / delete with 2–3 phones; only author or presenter may edit; private note
never reaches another phone; hide-all and hide-one are local only; note follows the part through
explode, isolate and a model reload; late joiner sees existing notes; notes come back next session
(D4).

**Paper metrics:** note placement drift (should be zero) across explode/isolate states.

---

### Phase 6 – Shared section cuts (#11)

**What you see:** **Section** button → a translucent cutting plane appears through the model with
drag handles. Drag to move it, twist to tilt it, and use the X / Y / Z buttons to snap it to an axis.
Everything on one side disappears, so you see inside. The cut faces are filled with a solid colour,
so the model reads as solid rather than as a hollow shell. **Flip** switches which half is removed.
The holder of the `section` token (Phase 1) drives it for the room.

**How it works:**
- **Highest technical risk in this plan.** The model's materials use glTFast's shaders, which have no
  clipping support. Approach: a clipping copy of the glTF PBR shader (`AuraSync/ClippablePBR`),
  swapped in by `GLTFastMaterialHelper` only while a cut is active, plus a back-face pass in a flat
  colour to fill the cut faces. **This is prototyped on day one of the phase, on a real phone,
  before anything else is built on it.**
- The plane is stored **in the model's coordinates**, so a viewer who has rotated the model
  differently still sees the same cut through the same parts.
- Dragging sends code 23 at ≤10 Hz, and releasing sets the room property.

**Files:** `Space/SectionCutter.cs`, `Shaders/ClippablePBR.shader`, `UI/SectionCutUI.cs`.

> **As built (2026-10-13):** a toolbar (X / Y / Z, MOVE, TILT, FLIP, FILL, OFF) instead of
> on-model handles; built in one go with an automatic camera-cut fallback instead of a
> prototype-first step (both your choices). The shader is `AuraSync/SectionClip`
> (`Assets/Shaders/AuraSyncSectionClip.shader`). It is keyword-free and fills cut faces from back
> faces in the same pass. The cut is shared as five numbers against the model's assembled bounds,
> through the Phase 1 popup / KEEP SHARING flow, and remembered per model. Added to Always Included
> Shaders by the build hook. See `V3_PHASE_6_TESTS.md` §1–2.

**Setup in Unity:** add the new shader to *Always Included Shaders* (as with the glTF shaders).

**Test gate:** cut on both models; flip; axis snaps; viewer with a differently rotated model sees the
same cut; cut + isolate; cut + explode; notes on hidden parts are hidden too; token handover; late
joiner; frame rate on the heavier model stays usable.

---

### Phase 7 – Simulation (#3, Level A and B)

**Flow (your spec):**

```
[Simulate] ──> panel:  [Assembly animation]   [Working animation]   [X]
                              │                        │
                              v                        v
               Play  Pause  <Step  Step>   Play  Pause  Throttle slider  Speed
               Step 3 of 12: "Fit the fan"     [AI assist]
               [Back]                          [Back]
```

**Assembly animation (Level A):**
- Parts come off one at a time in the knowledge file's `assemblyOrder` (reversed for disassembly),
  each sliding out along its `removeDirection`, with a step caption.
- Play / Pause / Previous step / Next step / speed, plus a toggle between **Disassemble** and
  **Assemble**.

**Working animation (Level B):**
- Parts with a `motion` entry move: rotate (fans, shafts), slide (pistons), or swing on a hinge
  (doors). Parts in the same `group` move together.
- **Throttle slider** (jet engine) or **time-of-day slider** (house).
- **Airflow particles** for the jet engine along the core path (intake → compressor → combustor →
  turbine → exhaust), shifting colour cold → hot.

**Both modes keep every other interaction working, as you asked:**
- Rotate and zoom, select a part, Parts drawer.
- **Isolate** a moving part: it keeps animating on its own (made possible by `PartPoseComposer`).
- **Section cut** while running, to see the airflow inside.
- Notes stay attached to moving parts.
- **AI assist** button: opens an info box for the selected part, or for the whole model if nothing is
  selected. In assembly mode it explains the current step (*"The spinner is fitted last because…"*).
  Same chat as Phase 3, so follow-ups work.

**Multiuser:** the `simulation` token holder drives. State (`mode, step, playing, speed, throttle,
startServerTime`) is a room property. Each phone runs the animation itself from the shared start
time (`PhotonNetwork.ServerTimestamp`), so every phone shows the fan at the same angle with almost no
network traffic. Late joiners land at the correct step and phase.

**Single device:** fully standalone, with no room needed.

**Back and forth:** Back from either mode returns to the two-option panel. Back again closes
Simulation and restores the model exactly as it was (positions, explode amount, isolation).

**Files:** `Simulation/SimulationController.cs`, `Simulation/AssemblyPlayer.cs`,
`Simulation/WorkingPlayer.cs`, `Simulation/AirflowEffect.cs`, `UI/SimulationPanelUI.cs`.

> **As built (2026-10-13):** models without a reviewed assembly order (the House) build bottom-up
> from their shape (your choice). The House's working animation is the time of day. The state is
> one room property animated from the shared clock (event 24 unused). Sharing goes through the
> Phase 1 popup / KEEP SHARING. Airflow is ~110 billboarded quads on the built-in Standard
> material, along the rotors' axis (the intake is the end nearer the biggest rotor). A model
> switch stops the simulation. See `V3_PHASE_7_TESTS.md` §1–2.

**Test gate:** each mode on both models; every control; switch modes mid-play; isolate / cut / notes /
AI assist during each mode; two phones stay in step for 2 minutes (no drift); late joiner; token
handover mid-play; exit restores the model exactly; frame rate with particles on.

**Paper metrics:** sync drift between phones (degrees of fan rotation), share of motion hints the AI
got right before review.

---

### Phase 8 – Collaborative attention awareness (#4)

**What you see:**
- A thin coloured **view cone** for each other person (in their pointer colour), showing where on
  the model they are looking, with a small name tag where it meets the surface.
- **Look here:** long-press a spot → a pulsing ring appears there on everyone's model, with your
  name, for 3 seconds.
- **Eye button** on the left rail: hide or show all cones (local only). Pings still show while
  cones are hidden, since they are deliberate calls for attention.

**How it works:** every 0.2 s each phone sends its view direction and focus point **in the model's
coordinates** (code 25). That is about 20 bytes, so five people cost almost nothing. It reuses
`CanvaPointerManager`'s colours and names.

**Files:** `Networking/AttentionSync.cs`, `Space/AttentionVisuals.cs` (as built, also
`Networking/FindTask.cs` and `UI/AttentionUI.cs`).

> **As built (2026-10-14):** your choices: cone + ring + name tag (*"· behind"* on the far side);
> people sharing one view merge into one cone (*"Asha + 2"*); long-press pings; and a **FIND task
> tool** for the user study. The presenter picks a part and starts it with cones ON or OFF; each
> searcher's time to tap the part is logged (`AttentionLog.jsonl`, event 30). "Where you look" is
> the centre of your screen. Spots are stored in part space, as notes are. Cones are scene objects
> outside the model. See `V3_PHASE_8_TESTS.md` §1–2.

**Test gate:** cones correct when the two phones rotate the model differently; with explode/isolate;
hide/show; ping; a player leaving removes their cone; performance with 5 players.

**Paper metrics:** this is the **user-study feature**: time to find a named part with and without
cones and pings.

---

### Phase 9 – Settings button + session record and replay (#5)

**Settings (top-right gear):** holds exactly three items, **Record & Replay**, **Heatmap** and
**AI Quiz**, shown to the presenter only (and to a solo user, per D6, except AI Quiz).

**Record & Replay:**
- **Record:** a red dot and timer show in the top bar while recording, and everyone in the room sees
  *"Session is being recorded"*. Stop → name the recording → saved on the presenter's phone.
- **What is recorded:** everything that passes through `SharedStateBus` (Phase 0): model movement,
  selection, explode, section cut, simulation, notes, shared AI answers, pings, attention cones,
  voice-activity markers. **Voice audio is not recorded** in V3 (size and consent). Can be a later
  option.
- **Replay:** Settings → Recordings → pick one → the model loads and the session replays in 3D.
  Timeline scrubber, Play/Pause, 0.5×/1×/2×, and the presenter's camera view or **free camera**
  (rotate it yourself while it plays).
- Replay is on the presenter's phone. **Replay to room** (everyone watches together) is a stretch goal
  for the end of the phase if time allows.
- Recordings can be exported and shared as a small file.

**Files:** `Recording/SessionRecorder.cs`, `Recording/SessionPlayer.cs`, `UI/SettingsPanelUI.cs`,
`UI/RecordingsUI.cs`.

> **As built (2026-10-14):** D6 settled as recommended: alone, you are your own presenter. Your
> other choices: replay on this phone outside a room only (no *Replay to room*); SHARE through
> Android's share sheet with a copy in Download/AuraSync, no plugins and no import; a model switch
> or Back stops the recording. Recordings are event files (`.aurarec`, one JSON line per change)
> polled from each feature rather than only from `SharedStateBus`. Replay seeks by state, so
> scrubbing is instant. REC is a player property, so it vanishes if the recorder drops out. See
> `V3_PHASE_9_TESTS.md` §1–2.
>
> **Changed the same day, at your request: the presenter's voice IS recorded** (this replaces "Voice
> audio is not recorded" above). It is the recording phone's own voice only, at speech quality
> (~1 MB/min, 16 kHz µ-law WAV beside the recording). In a room it is tapped from Photon Voice's
> outgoing stream, so nothing is kept while muted; alone it comes from the microphone. It is
> replayed in step with the timeline. See `V3_PHASE_9_TESTS.md` §6.

**Test gate:** record a 10-minute two-phone session that uses every feature, then replay it and check
each action appears at the right moment; scrub; speed; free camera; replay of a model not in the
cache (downloads first); 30-minute recording size; app killed mid-recording (partial recording
recovered).

**Paper metrics:** recording size per minute versus a screen recording of the same session.

---

### Phase 10 – Interaction heatmap per part (#6)

**What you see:** Settings → **Heatmap** → choose **Everyone** or one person → the model recolours:
red = most attention, blue = least, grey = never touched. A legend and a **Top 5 / Bottom 5 parts**
list. **Done** restores the normal colours.

**How it works:** each phone counts, per part: taps/selections, seconds isolated or selected, notes
added, AI questions asked, seconds in view (from Phase 8). Every 10 s it sends the totals to the
presenter (code 27). The heatmap can also be generated **from a recording**. Snapshots are saved into
**Reports** (Phase 11).

**Files:** `Analytics/InteractionTracker.cs`, `Analytics/HeatmapView.cs`, `UI/HeatmapUI.cs`.

> **As built (2026-10-14):** counts per part per person (picks, seconds selected / isolated /
> looked at, notes, AI questions) weighted into one score; colour applied over the materials (a
> per-renderer override), so the section cut keeps working; MOST / LEAST 5; SAVE TO REPORTS; a
> heatmap from a recording. See `V3_PHASE_10_TESTS.md` §1–2.

**Test gate:** counts match a scripted session (tap part A 5 times → A is hottest); per-person filter;
a player who left still counts; colours restore; works on isolated and exploded models.

**Paper metrics:** attention per part, later correlated with quiz scores per part (Phase 11).

---

### Phase 11 – AI quiz + Reports (#9)

**Teacher flow:**
1. Settings → **AI Quiz** → choose: whole model or selected parts, number of questions (5/10/15),
   difficulty, time limit (optional).
2. **Generate** → questions appear for review. The teacher can edit, delete or regenerate any
   question, then tap **Send**.
3. A live panel shows who has finished. The quiz ends when everyone finishes, when time runs out, or
   when the teacher taps **End quiz**.

**Student flow:**
1. A banner: *"New quiz from <teacher>. Tap to start."*
2. The quiz window: one multiple-choice question at a time. The model stays visible, and a question
   about a part highlights that part.
3. After the quiz ends, the student sees their score and which answers were right.

**Report (teacher):**
- **Reports** button (top bar **and** main menu) with a **red badge** counting unseen reports.
- The report holds: the class average; a **score per student** bar chart; **correct % per question**;
  **weak parts** (parts whose questions were missed most); and, if a heatmap exists for the session,
  **attention vs score per part** side by side.
- Reports are **saved on the teacher's phone** and open **any time**, with no room and no model
  loaded.
- Export a report as an image or CSV to share.

**Files:** `Quiz/QuizGenerator.cs`, `Quiz/QuizSession.cs`, `UI/QuizTeacherUI.cs`,
`UI/QuizStudentUI.cs`, `Reports/ReportStore.cs`, `UI/ReportsUI.cs`, `UI/SimpleBarChart.cs` (charts
drawn with plain UI, no extra library).

> **As built (2026-10-14):** the quiz travels as a room property (late joiners can take it; event
> 28 unused), each answer goes to the teacher as event 29; review with EDIT / NEW ONE / DELETE;
> reports with plain-UI bar charts, CSV and screenshot export through the share sheet; REPORTS on
> the model list too (added in code). See `V3_PHASE_11_TESTS.md` §1–2.

**Test gate:** generate on both models; edit/regenerate; 3 phones take a quiz; one student leaves
mid-quiz; late joiner during a quiz; time limit; badge counts and clears; reports open from the main
menu with no room; app restart keeps reports; export.

**Paper metrics:** quiz score per part vs attention per part (Phase 10); question quality (share of
AI questions the teacher kept unedited).

---

### Phase 13 – Bandwidth-aware model loading (#12)

**What you see:** joining a room on a slow connection, a **lighter version of the model appears within
seconds**, labelled *"Preview quality"*. The full mobile version downloads in the background and
**swaps in without interrupting** anything (selection, notes, cut, simulation, recording all carry
on).

**How it works:**
- `optimize-model.py` produces a third file, `<Model>_preview.glb` (much lower detail, small
  textures), **with the identical part hierarchy**. Part count is checked: before = after.
- `models.json` lists the variants with their sizes.
- The app measures download speed on the preview, then decides whether to fetch the mobile version
  now, later on Wi-Fi, or not at all. A setting lets the user force a quality.
- The swap re-applies every feature's state by part path (each feature already handles "model
  loaded").

> **As built (2026-10-14):** `optimize-model.py --preview` makes the preview from the mobile file
> and refuses any part-tree or part-transform difference (Jet Engine 12.84 → 1.35 MB; House 7.07 →
> 4.86 MB, since simplifying the scan further changed its tree). Full file at once on Wi-Fi, on
> mobile data if small or fast, else on GET FULL; SAVE DATA keeps previews. The swap happens in
> place under the same model key, so the room never sees it. See `V3_PHASE_13_TESTS.md` §1–2.

**Test gate:** the swap performed with **every** feature active, one at a time and then all
together; slow network (Android developer options → network throttling); offline with the cached
model; late joiner on mobile data.

**Paper metrics:** time-to-first-render and time-to-interactive, preview vs mobile, on 4G vs Wi-Fi.

---

## 4. Risks to know about

| Risk | Phase | Plan |
|---|---|---|
| Section cut needs a custom shader. glTFast's shaders cannot clip. | 6 | Prototype on a real phone first. Fallback: a single-plane camera clipping trick (no cut-face fill). |
| A key part (for example the fan) is merged into another mesh | 2 | Audit first. Fix in Blender before labelling. |
| AI cost or rate limits during a class | 2, 3, 11 | Server-side rate limit; part info and search step 1 work without AI; the quiz is generated once and then sent as data. |
| Too many buttons on a small phone | 0 | Left rail plus hidden-until-allowed buttons; checked on the smallest test phone. |
| Photon message limits with 5 players | 8, 10 | Unreliable low-rate attention data; counters batched every 10 s. |
| Android speech recognizer behaves differently per phone | 4 | Server-side speech-to-text fallback (D3). |

---

## 5. Paper evaluation summary

What the phases above produce for the results section:

1. **AI part labelling accuracy** on unlabeled GLB models, with and without assembly context (Phase 2).
2. **Search effectiveness:** time-to-find with semantic search vs the parts list (Phase 4).
3. **Attention awareness user study:** task time with and without cones and pings (Phase 8).
4. **Recording efficiency:** event recording vs screen recording, size per minute (Phase 9).
5. **Learning analytics:** attention per part vs quiz correctness per part (Phases 10 and 11).
6. **Synchronisation:** simulation drift across devices (Phase 7); ownership conflicts prevented
   (Phase 1).
7. **Loading:** time-to-interactive with progressive variants (Phase 13).
8. **Usability:** SUS and NASA-TLX questionnaires after the final build.
