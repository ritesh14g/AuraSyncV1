# AuraSync V3 – Phase 7 (Simulation: assembly and working animations): build notes and test plan

**SIMULATE** on the left rail offers two animations:

- **Assembly:** parts come off one at a time in the right order, or go back on, with a caption
  for each step.
- **Working:** the jet engine's rotors spin with a throttle, and air streams through the core.
  The House gets the **time of day**: the sun crosses the sky.

Everything else keeps working while it runs: turning the model, selecting, PARTS, isolate,
the section cut, notes, the AI. In a room, every phone shows the same moment, worked out from
Photon's shared clock.

> **Status:** compiled clean (0 errors). **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions made at the start of this phase (2026-10-13)

| Question | Answer |
|---|---|
| The House has no assembly order (a scan in chunks). What does its Assembly animation do? | **Build it bottom-up:** for any model without a reviewed order, the order is made from its shape. Lowest chunk first, each dropping in from above, so assembling reads like the building going up. Captions say *"section 3 goes on"* (no part names). The jet engine uses its reviewed order. |

Defaults chosen without asking (say if any should change):

- **Sharing works like the other controls:** the **Simulation** holder (presenter by default)
  gets *"Show this simulation to everyone?"* when they choose a mode, press PLAY / PAUSE / a step /
  speed, or let go of a slider. **KEEP SHARING** stops the asking. Nothing is sent while it
  plays: each phone animates from the shared start time.
- A viewer can run their own simulation (exploring); Catch Up brings back the room's.
- **AIRFLOW on/off is per phone** (how you view it).
- **Assembly** starts paused at step 0, in TAKE APART. Removed parts slide out along their reviewed
  direction and **stay out** (they do not vanish), so the empty space and the parts are both visible.
- **Working** starts playing: the engine at 50% throttle, the House at 8:00. A full day takes one
  minute at 1x. The throttle runs the rotors from idle (0.15 turns a second) to full (2.4 turns a
  second).
- **AI** in Assembly asks *"Why is the \<part\> removed / fitted at step k of n?"* about the current
  step. In Working it opens the chat on the selected part, or the whole model.
- **Switching model stops the simulation** for everyone (the new model starts at rest).
- The plan reserved event code 24; it is not needed, because the state is one room property.

## 2. What was built

| File | What |
|---|---|
| `Simulation/SimulationController.cs` | **New.** The shared state (mode, playing, speed, throttle, phase, start time), the clock, sharing, Catch Up, late joiners. |
| `Simulation/AssemblyPlayer.cs` | **New.** Steps from the reviewed order (or bottom-up), sliding parts out and in, captions. |
| `Simulation/WorkingPlayer.cs` | **New.** Spins / slides / swings parts with motion data about their reviewed pivot; the House's sun. |
| `Simulation/AirflowEffect.cs` | **New.** About 110 glowing specks along the rotors' axis, intake to exhaust, blue → white → orange → red. |
| `Simulation/SimLog.cs` | **New.** Paper numbers: sync samples, the one-phone sync check, AI motion-hint accuracy. |
| `UI/SimulationPanelUI.cs` | **New.** SIMULATE on the rail; the chooser, Assembly and Working pages. |
| `UI/PartChatUI.cs` | `AskAbout(part, question)`: open the chat and ask straight away. |
| `Networking/ControlOwnership.cs` | **Simulation** joins the controls (ROOM CONTROLS, badge menu). |
| `Space/SpaceInteractionController.cs`, `Space/ModelStateMemory.cs`, `Space/SpaceSceneManager.cs`, `Space/SharedStateBus.cs`, `Networking/EventCodes.cs`, drawer / notes / section toolbar, debug menu | Hooks. SIMULATE, PARTS, NOTES and SECTION share the lower-left: opening one hides the others. The simulation **keeps running** when its panel is hidden. |

**Every part moves through `PartPoseComposer`'s Simulation layer** (Phase 0). That is why an
isolated rotor keeps spinning in place, an exploded model can still animate, and BACK puts the
model back **exactly**.

**Setup in Unity:** none.

---

## 3. Tests

Jet Engine unless stated. One device unless the ID starts with **SR** (2–3 devices).

### 3.1 Opening and leaving

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SM-01 | P0 | Tap **SIMULATE**. | A panel above the bottom buttons: **ASSEMBLY ANIMATION**, **WORKING ANIMATION**, X. |
| SM-02 | P0 | Choose either mode, then **BACK**. | The animation stops; the model is **exactly** as before (run **log back stack**: `parts moved by the composer: 0` if nothing else is on). The chooser shows again. |
| SM-03 | P0 | BACK (or phone Back) again. | The panel closes. The rail reads "SIMULATE". |
| SM-04 | P1 | A mode running: open PARTS (or NOTES, or SECTION). | The panel hides; **the animation keeps running**. SIMULATE brings the panel back on the same mode. The rail reads *"SIMULATE (on)"* while it runs. |

### 3.2 Assembly animation

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SA-01 | P0 | ASSEMBLY ANIMATION. | Paused at step 0, TAKE APART lit. Caption: *"Step 1 of 11: remove the fan rotor"* (the last part fitted comes off first). STEP slider 0…11. |
| SA-02 | P0 | **PLAY**. | Parts slide out one after another along their reviewed directions, about 2 s each; the caption names each. At the end: *"Done: every part is off."*, button reads **REPLAY**. |
| SA-03 | P0 | **>|** and **|<** while paused. | One whole step forward / back each tap; parts snap to that step. |
| SA-04 | P0 | Drag the STEP slider. | Parts move with the knob (scrubbing); the bubble shows the step. |
| SA-05 | P0 | **PUT TOGETHER**, PLAY. | Starts with every part out; they slide in, first-fitted first (*"fit the turbine rotor"* …). Ends *"Done: the model is assembled."*, exactly assembled. |
| SA-06 | P0 | **1x** button. | Cycles 2x → 0.5x → 1x; playback speed changes without jumping. |
| SA-07 | P0 | Mid-animation: tap **AI**. | The chat opens on the current step's part and asks *"Why is the … removed at step k of 11?"*; an answer arrives. |
| SA-08 | P0 | **House**: ASSEMBLY → PUT TOGETHER → PLAY. | Chunks drop in from above, lowest first; the house rises. Captions *"Step k of 21: section k goes on"*. |
| SA-09 | P1 | House: TAKE APART → PLAY. | Chunks lift off from the top down. |
| SA-10 | P1 | Isolate a part mid-animation; explode a little; select parts. | All work; nothing jumps when they are undone. |

### 3.3 Working animation

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SW-01 | P0 | WORKING ANIMATION. | Fan and turbine rotors spin **together** about the engine's axis, each **in place** (no wobble, no orbiting). Caption *"Moving: Fan rotor, Turbine rotor"*. Glowing specks stream through the core, blue at the front to red at the back. THROTTLE at 50%. |
| SW-02 | P0 | Drag THROTTLE to 0 and to 100. | The rotors and airflow slow to a crawl and speed up **smoothly** (no jump in angle). |
| SW-03 | P0 | PAUSE / PLAY. | Rotors and airflow freeze and continue from the same angle. |
| SW-04 | P0 | **AIRFLOW** off/on. | The specks disappear / return; rotors unaffected. |
| SW-05 | P0 | Isolate the **fan rotor** while running. | It stands alone, centred and enlarged, **still spinning in place**. |
| SW-06 | P0 | Section cut (SECTION) through the engine while running. | The rotors spin inside the cut; the airflow is visible through the opening. |
| SW-07 | P0 | A note on a fan blade while running. | The pin rides round with the blade. |
| SW-08 | P0 | **House**: WORKING ANIMATION. | The caption says *"Time of day…"*; the slider is **TIME 0…24**, the bubble shows the hour (08:00 at start) and advances (a day a minute at 1x). Light and sky change: warm at 06:00 and 18:00, bright at noon, dim blue at night (model still visible). |
| SW-09 | P0 | House: drag TIME; then BACK. | The sun follows the slider. After BACK, the light is **exactly** as before. |
| SW-10 | P1 | AI while running, with a part selected / nothing selected. | The chat opens on that part / the whole model (no automatic question). |
| SW-11 | P1 | Phone, airflow on, rotors at full throttle, turning the model. | Smooth. |
| SW-12 | P1 | Switch from Working to Assembly (BACK → ASSEMBLY). | Rotors stop at rest; the assembly starts from a correct, assembled model. |

### 3.4 In a room (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SR-01 | P0 | A (presenter): WORKING ANIMATION. | A: popup *"Show this simulation to everyone?"*. ACCEPT → B's engine spins too. |
| SR-02 | P0 | Put A and B side by side, running, for **2 minutes**. Pause A (ACCEPT). | Throughout, the fans look the same; after the pause both stop at the **same angle**. |
| SR-03 | P0 | Run **V3 debug: simulation sync check** on B while it runs. | *"difference"* is about one frame of spin or less. |
| SR-04 | P0 | A moves THROTTLE and lets go → ACCEPT. | B's rotors change speed with no jump. |
| SR-05 | P0 | A: KEEP SHARING; then PLAY / PAUSE / steps. | No more popups; B follows each one. |
| SR-06 | P0 | A: ASSEMBLY, PLAY (shared). | B's parts come off in step with A's, same caption. |
| SR-07 | P0 | **C joins** mid-animation (either mode). | C lands on the same step / angle as the others at once. |
| SR-08 | P0 | A gives B **Simulation** (badge menu) mid-play. B pauses and steps → ACCEPT. | Everyone (A included) follows B. A's panel is greyed: *"B drives the simulation…"*. TAKE BACK returns it. |
| SR-09 | P0 | B (viewer, no control) starts their own simulation. | Only B's model animates; B's Catch Up turns orange. Catch Up → B gets the room's simulation (or stops, if the room has none). |
| SR-10 | P1 | A switches model while a simulation runs. | Every phone opens the new model at rest. |
| SR-11 | P1 | House day cycle shared. | B's sun moves with A's. |
| SR-12 | P1 | A → BACK (stop) → ACCEPT. | B's model returns to rest exactly. |

### 3.5 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| SP-01 | P1 | After SR-02, run the stats item on both phones. | *"Simulation: N sync sample(s)…"*, and *"AI motion hints: x of y matched the reviewed motion"*. Copy both phones' `SimLog.jsonl` to compare angles at the same server time. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_0_TESTS.md` | **F-01 – F-13** | The Simulation layer of the composer is used for the first time. |
| `V3_PHASE_12_TESTS.md` | **R-01, R-03, R-10** | The challenge turns any simulation off and shares parts with it. |
| `V3_PHASE_6_TESTS.md` | **S-07, S-14** | The section toolbar, drawer, notes and simulation panel hide each other. |
| `V3_PHASE_1_TESTS.md` | **G-01, R-01** | ROOM CONTROLS and the badge menu gained a Simulation row. |
| `V3_PHASE_3_TESTS.md` | **C-01, C-20** | The chat's new `AskAbout`. |
| `V3_PENDING_DEVICE_TESTS.md` | **MS-01, MS-03** | Model switches now also stop the simulation. |

## 5. Paper metrics

- **Sync drift between phones** (degrees of fan rotation): the sync check (SR-03), and the
  `SimLog.jsonl` samples (angle at a server time) compared across phones.
- **AI motion hints right before review:** the stats line, from the knowledge file's AI proposals
  against the reviewed motion.
