# AuraSync V3 – Phase 6 (Shared section cuts): build notes and test plan

**SECTION** on the left rail cuts the model open with a plane. Everything on one side disappears
so you can see inside, and the cut faces are filled with a flat orange so parts read as solid. A
toolbar steers the cut. In a room, whoever holds the **Section cut** control shares it like the
exploded view.

> **Status:** C# compiled clean (0 errors). **The shader could not be compiled outside Unity.**
> Section 3.1 checks it first. **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions made at the start of this phase (2026-10-13)

| Question | Answer |
|---|---|
| How is the cut steered on a phone? | A **toolbar** above the bottom buttons: **X / Y / Z** (snap the cut to an axis), **MOVE** and **TILT** sliders, **FLIP**, **FILL**, **OFF**. One finger on the model still turns it. No handles on the model. |
| Prototype the shader first? | **No: the whole phase in one go**, with an automatic **fallback** if the shader fails on a phone. |

Defaults chosen without asking (say if any should change):

- **Sharing works like explode:** the Section holder (presenter by default) gets *"Show this
  section cut to everyone?"* when they let go of a slider or press a button. **KEEP SHARING** makes
  it live, and then drags also stream to followers about 10 times a second.
- A viewer can **cut on their own phone** (exploring); Catch Up brings back the room's cut.
- **FILL is per phone** (how you view the cut, not part of it). It is on by default.
- **Opening SECTION with no cut starts one**, half way through the model along X. Closing the
  toolbar **keeps** the cut; **OFF** removes it.
- The cut is **remembered per model**, like explode and isolation.
- The reassembly challenge turns the cut off (it is never shared).
- TILT goes up to **60°** either way, around one fixed hinge per axis.

## 2. What was built

| File | What |
|---|---|
| `Assets/Shaders/AuraSyncSectionClip.shader` | **New.** `AuraSync/SectionClip`: draws a glTF material the way glTFast does (same property names and texture maths), plus the cut and the filled cut faces. No shader keywords, so including it in builds stays cheap (unlike Standard). |
| `Space/SectionCutter.cs` | **New.** The cut: plane maths in the model's own terms, swapping materials only while a cut is on, the camera fallback, the thin frame, sharing (room property `SectionPlane`, event 23 while live), Catch Up, frame-time stats. |
| `UI/SectionCutUI.cs` | **New.** SECTION on the rail and the toolbar. |
| `Networking/ControlOwnership.cs` | **Section cut** joins the controls: it appears in ROOM CONTROLS and in the badge menu. |
| `Space/ModelRaycaster.cs` | Taps go through the cut-away half (selecting, placing notes). |
| `Space/ModelStateMemory.cs` | The cut is remembered per model and travels with model switches. |
| `UI/PartNotePins.cs` | Notes on the cut-away half are not drawn. |
| `Editor/GltfShaderInclusion.cs` | Adds `AuraSync/SectionClip` to Always Included Shaders on every build. |
| `Space/SpaceInteractionController.cs`, `Space/SpaceSceneManager.cs`, `Space/SharedStateBus.cs`, `Networking/EventCodes.cs`, drawer, notes list, debug menu | Hooks. The toolbar, the parts drawer and the notes list close each other, since they share the lower-left of the screen. |

**How the cut is shared:** as five numbers (on/off, axis, move, tilt, flip), measured against the
model's assembled size. Every phone works out the same cut through the same parts, however each
person has turned their model.

**Fallback:** if the shader is missing or unsupported, the camera's near plane is laid onto the
cut. That works with any material, but **cut faces are not filled**, and it only cuts while you look
from the removed side. The toolbar says so when it is in use.

**Setup in Unity:** nothing to do by hand. The build hook adds the shader to *Always Included
Shaders*; *AuraSync > Fix glTF Shader Inclusion* does the same immediately.

---

## 3. Tests

### 3.1 The shader (do these first, one device)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| T-01 | P0 | Open the project in Unity. Look at the Console, and select `Assets/Shaders/AuraSyncSectionClip.shader`. | **No shader errors.** The Inspector shows *AuraSync/SectionClip* without red error text. *(If there are errors, copy them to me; everything else still works, using the fallback.)* |
| T-02 | P0 | Editor, Jet Engine, not in a room. Tap **SECTION**. | The toolbar opens; the engine is **cut in half along X**; the cut faces are **flat orange**; a thin cyan frame marks the cut. The rail reads *"SECTION (on)"*. |
| T-03 | P0 | Compare the uncut half's colours and textures with the model before cutting. | **They look the same** (same textures, colours, shine). No pink, no white, no stretched or shifted textures. |
| T-04 | P0 | Same on the **phone** (build and run). | As T-02 and T-03. The toolbar's grey help line does **not** say "Simple cut on this phone". |
| T-05 | P1 | Run **V3 debug: AI, search, challenge and control stats**. | A *"Section cut: shader cut; frame time … without a cut, … with one"* line. |
| T-06 | P1 | Run **V3 debug: toggle section-cut fallback** with a cut on. Turn the model so the cut side faces you, then away. | Cut faces become hollow (not filled) and the help line says "Simple cut…". From the cut side the cut works; from the other side the whole model shows. Toggle again: back to the shader cut. |

### 3.2 Using the toolbar (one device)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-01 | P0 | Drag **MOVE** from one end to the other. | The cut slides through the whole model: at one end everything is gone, at the other nothing is cut. The label shows -100 … 100. |
| S-02 | P0 | Tap **Y**, then **Z**. | The cut turns to face the new axis (TILT resets to 0); the frame follows. |
| S-03 | P0 | Drag **TILT**. | The cut tilts smoothly up to ±60. |
| S-04 | P0 | Tap **FLIP**. | The other half disappears instead. |
| S-05 | P0 | Tap **FILL** (off). | Cut faces are no longer orange: you see the parts' inner surfaces. Tap again to refill. |
| S-06 | P0 | Turn and zoom the model with a cut on. | The cut stays fixed **to the model** (it turns with it). Dragging on the toolbar never turns the model. |
| S-07 | P0 | Close the toolbar (X). | The cut **stays**, and the frame disappears. SECTION reopens the toolbar on the same cut. |
| S-08 | P0 | **OFF**. | The whole model is back exactly as before; the toolbar closes; the rail reads "SECTION". |
| S-09 | P0 | Cut, then **explode** 0 → 100%. | Parts slide through the fixed cut; filled faces stay correct. |
| S-10 | P0 | Cut, then **isolate** a part. | The isolated (enlarged, centred) part is cut too. |
| S-11 | P0 | With a cut on, tap a part on the **removed** side's screen area where a kept part is visible behind it. | The **visible** part is selected, not the invisible cut-away one. |
| S-12 | P1 | Add a note on a part, then move the cut over it. | The pin disappears when its spot is cut away and comes back when uncovered. |
| S-13 | P1 | Cut, back to the model list, open another model, come back. | The other model opens uncut; back on the first one, the cut is as you left it. |
| S-14 | P1 | Open PARTS while the toolbar is open, and the other way round; same with NOTES. | Each closes the other. |
| S-15 | P1 | **House** model: cut along each axis. | Works the same (the House is one scanned shell: with FILL on, its inside surfaces show orange). |
| S-16 | P1 | Cut on, start the reassembly challenge. | The cut turns off and SECTION disappears during the game. |
| S-17 | P1 | **Phone**, heavier model (Jet Engine), cut on, move the sliders, turn the model. | Stays smooth. Compare the two frame times from T-05. |

### 3.3 In a room (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-01 | P0 | A (presenter) taps SECTION. | A: popup *"Show this section cut to everyone?"* (ACCEPT / DENY / KEEP SHARING). B unchanged until A answers. |
| R-02 | P0 | A → **ACCEPT**. | B (following) sees the same cut. |
| R-03 | P0 | B turns the model differently from A (exploring), then taps **Catch Up**. | B gets A's view and the cut, through the **same parts**. |
| R-04 | P0 | Before Catch Up in R-03: compare where the cut goes on A and B. | Same parts cut on both, even with the models turned differently. *(Use the House or engine and a clear landmark part.)* |
| R-05 | P0 | A moves MOVE and lets go → ACCEPT. | B's cut moves to the new spot. During A's drag, B does **not** move (not live). |
| R-06 | P0 | A → **KEEP SHARING**, then drags MOVE slowly. | B's cut follows **during** the drag. A's help line reads *"Shared live with the room."* |
| R-07 | P0 | A → OFF → ACCEPT. | B's cut disappears too. |
| R-08 | P0 | B (a viewer) cuts on their own. | Only B changes; B's Catch Up turns orange (exploring). A unaffected. |
| R-09 | P0 | A gives B **Section cut** (badge menu). B opens SECTION and cuts → ACCEPT. | A and other followers see B's cut. A's toolbar follows B's cut, locked (greyed), with *"B drives the cut. TAKE BACK is in the Parts drawer."* B stays following A's view. |
| R-10 | P0 | A: TAKE BACK (Parts drawer → ROOM CONTROLS → Section cut). | B: *"Section cut went back to the presenter."*; A's toolbar unlocks. |
| R-11 | P1 | Cut shared. **C** joins. | C arrives at the cut. |
| R-12 | P1 | B holds Section and leaves mid-drag. | A's toolbar unlocks; the room keeps the last shared cut. |
| R-13 | P1 | A cuts (Accept) on model 1, switches to model 2 and back. | Model 2 uncut on all phones; model 1's cut comes back for everyone. |
| R-14 | P2 | A cuts → **DENY**. | Only A is cut. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_0_TESTS.md` | **F-01, F-02, F-06, F-10, F-13** | Explode / isolate with materials swapped and back. |
| `V3_PHASE_1_TESTS.md` | **G-01, G-02, G-07** | The badge menu and ROOM CONTROLS gained a Section cut row. |
| `V3_PHASE_5_TESTS.md` | **N-03, N-10, N-14** | Placing a note uses the raycaster (now cut-aware). |
| `V3_PENDING_DEVICE_TESTS.md` | **MS-01, MS-03** | Model switches now also carry the cut. |
| `V2_PARTS_DRAWER_TESTS.md` | Tap-to-select | Raycaster change. |

## 5. Paper metrics

- **Frame time with and without a cut** on each phone (T-05 / S-17).
- Fallback use: whether any test phone needed the camera cut (T-04).

---

## 6. Follow-up 2026-10-13: restyled sliders

All Phase 6 tests passed. You then asked for sleeker MOVE / TILT sliders, like the middle slider in
your reference image. The first version's fill kept Unity's default 100 x 100 size, which is why it
spread over the labels as a big blue block. New `UI/SleekSlider.cs`: a thin rounded track, a bright
blue fill up to a round knob, the value in a small bubble above the knob, and the ends labelled
(-100 / 100, -60 / 60). The toolbar now fits its rows, with no empty band at the bottom.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SL-01 | P0 | Open SECTION. | Each row: *MOVE* / *TILT* on the left, then *-100*, a thin rounded track, *100*. The blue fill runs from the left end to a round blue knob; the value sits in a small purple bubble above the knob. No blue block over the labels. |
| SL-02 | P0 | Drag each knob end to end. | The fill follows exactly to the knob's centre; the knob never hangs past the track's ends; the bubble rides along showing the value. |
| SL-03 | P0 | Tap anywhere on a track (not on the knob). | The knob jumps there (the whole row height is grabbable). |
| SL-04 | P1 | Holder / presenter lock (R-09). | Locked sliders grey out but keep the same shape. |
| SL-05 | P1 | Phone. | Round shapes are smooth (no jagged edges); the toolbar ends right below the TILT row. |
