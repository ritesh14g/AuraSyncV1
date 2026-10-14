# AuraSync V3 – Phase 0 (Foundations): build notes and test plan

Phase 0 adds no new feature. It lays the groundwork every later phase builds on, and changes how
two existing features (exploded view and isolation) move parts. So most of this sheet checks that
**nothing you already had has changed**, plus one bug that is now fixed.

> **Status:** written and **compiled clean** against this project's own references (Unity
> 2022.3.62f3's Roslyn, all 115 `Assembly-CSharp` sources: 0 errors). **Not yet run in Unity.**
> Unity will add the six new files to its project on first open; that is expected.
>
> **Gate for the next phase:** every **P0** case in section 4 passes. Multi-device cases are not
> here; they are in `V3_PENDING_DEVICE_TESTS.md` and run at the device-testing checkpoint.

---

## 1. What was built

| File | Change |
|---|---|
| `Scripts/Space/PartPoseComposer.cs` | **New.** The one place that moves a model's parts. Features hand it offsets on their own layer (Explode, Simulation, Challenge, Isolation) and it adds them up on top of the part's resting pose. |
| `Scripts/Space/ModelExploder.cs` | Moves parts through the composer instead of setting their positions directly. |
| `Scripts/Space/ModelIsolator.cs` | Same. Also **re-centres the isolated part when the explode amount changes** (the bug fix below). |
| `Scripts/Space/SharedStateBus.cs` | **New.** One stream of "what just changed" (explode, selection, isolation, model loaded). Nothing listens yet; recording (Phase 9) and the heatmap (Phase 10) will. |
| `Scripts/Space/ModelPartSelector.cs`, `SpaceSceneManager.cs` | Publish their changes on the bus. The scene manager resets the composer on each model load and creates the back-button handler. |
| `Scripts/UI/PanelNavigator.cs` | **New.** The back stack. Every panel that opens is pushed; Back closes the top one. |
| `Scripts/UI/BackButtonHandler.cs` | **New.** Android back button (Escape in the Editor) → close the top panel, or leave to the model list if none is open. 3D viewer only. |
| `Scripts/UI/SpaceSessionHUD.Slots.cs` | **New.** Button slots for V3 features: a **left rail** and a **top-right corner row**, plus Phase 0 debug tools. |
| `Scripts/UI/SpaceSessionHUD.cs` | Builds the slots. **BACK now closes the top panel first.** Part chip narrowed. |
| `Scripts/UI/ModelPartsDrawerUI.cs`, `RoomLobbyUI.cs` | Register with the back stack. The drawer now opens a little further right. |
| `Scripts/Networking/EventCodes.cs` | **New.** Every Photon event code and room-property key in one file, V3's reserved too. The V2 constants now point here, **values unchanged**. |
| `Scripts/Core/InputCompat.cs` | Adds `BackPressedThisFrame`. |

**Undo:** the original versions of every changed file are in `backups/pre-phase0/` (Desktop staging folder), so Phase 0 can be rolled back in one
step if needed. Ask and it will be done.

---

## 2. What you will notice (decisions worth knowing before you test)

- **BACK closes panels first.** With the parts drawer (or the room panel) open, BACK now closes it.
  Press BACK again with nothing open to leave to the model list, as before. The phone's own back
  button does exactly the same.
- **The feature rail is on the left, not the right** as the plan sketched. The right edge already
  holds the player badges in a room. The rail sits at the left edge between the bottom buttons and
  the top bar, and is **empty and invisible** in Phase 0 until you switch on the placeholders.
- **The parts drawer opens a little further right** (it starts beside the rail instead of at the
  screen edge). Same size, same behaviour.
- **The selected-part chip under the top bar is shorter**, so it never runs under the new top-right
  Settings / Reports buttons. Long part names are cut with `...` sooner.
- **Bug fixed: moving the explode slider while a part is isolated.** Before, the isolated part slid
  away from the centre with the explosion, often out of view. Now it stays centred.

---

## 3. Debug tools for this phase

All are on the **SpaceSessionHUD** component's context menu: in Play mode, select
`AuraSync_SpaceSessionHUD` in the Hierarchy, then click the **three dots** (⋮) at the top right of the
SpaceSessionHUD component in the Inspector.

| Menu item | What it does |
|---|---|
| **V3 debug: toggle placeholder buttons** | Shows dummy buttons in every V3 slot: AI, SIMULATE, SECTION, NOTES, VIEWS on the rail; SETTINGS and REPORTS (with a red **1**) in the corner. Tapping one opens a stackable test panel. |
| **V3 debug: toggle state-bus log** | Writes every published change to the Console as `[StateBus] key = value (local/room)`. |
| **V3 debug: log back stack** | Prints the back stack, bottom to top, and how many parts the composer is currently moving. |

The test panels each have **OPEN ANOTHER** (stacks a new panel on top) and **CLOSE** (closes that one
panel). Every open and close logs the stack.

---

## 4. Single-device tests

Run in the Unity Editor (Escape = phone back button) unless marked **phone**. No room is needed
unless the case says so; *alone in a room* means create a room on this one device.

### 4.1 Part movement (the composer)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| F-01 | P0 | Open a model. Open PARTS, pick a mesh part, tap **ISOLATE**. | Everything else disappears; the part is **centred** and **enlarged**, exactly as before Phase 0. |
| F-02 | P0 | Tap ISOLATE again (show all). | The whole model returns. The part is **exactly** back in place: no gap, no overlap, no size change. |
| F-03 | P0 | Repeat F-01 and F-02 five times on the same part. | No drift. The last return is as exact as the first. |
| F-04 | P0 | Explode to about 50%. Isolate a part. | The part is centred and enlarged. |
| F-05 | P0 | From F-04, show all. | The model returns **still exploded at 50%**, the part back in its **exploded** position (not its assembled one). |
| F-06 | P0 | **The fix.** Isolate a part. Then move the EXPLODE slider slowly across its whole range. | The isolated part **stays centred** the whole time. (Before Phase 0 it slid away.) |
| F-07 | P0 | From F-06, leave the slider at some value and show all. | The model appears at that explode amount, every part in the right place. |
| F-08 | P0 | Isolate a **group** row (a dimmer one with children). Move the explode slider. | The group stays roughly centred; its own pieces spread apart within it. |
| F-09 | P1 | Isolate one part, then pick a different part in the drawer while isolated. | Isolation moves to the new part (centred, enlarged); the first part goes back in place. |
| F-10 | P0 | Explode to 100%, back to 0%. Isolate and show all a part. Explode to 100% and back to 0% again. | The model is **exactly** its original shape at the end. |
| F-11 | P1 | With the model assembled and nothing isolated, run **log back stack**. | Ends with `parts moved by the composer: 0`. |
| F-12 | P1 | Explode to 50% and run **log back stack**. Then slide back to 0% and run it again. | First: a number roughly equal to `[ModelExploder] <n> movable part(s)` from the load log. Second: `0`. |
| F-13 | P0 | Explode and isolate, then BACK out and open a **different** model. | The new model is assembled and whole. **log back stack** shows `parts moved by the composer: 0`. |
| F-14 | P1 | Isolate a part, rotate, pinch-zoom and pan. | Behaves exactly as before Phase 0. |

### 4.2 Back stack

| ID | Pri | Steps | Expected |
|---|---|---|---|
| F-20 | P0 | Open a model. Open PARTS. Press the on-screen **BACK**. | The drawer **closes**. You stay in the 3D view. *(New behaviour.)* |
| F-21 | P0 | Press **BACK** again. | You leave to the model list, as before. |
| F-22 | P0 | Open PARTS. Press **Escape**. Press **Escape** again. | First: the drawer closes. Second: back to the model list. Console shows `[Back] Closed top panel...` then `[Back] No panel open.` |
| F-23 | P0 | In the 3D view, open the room panel with the **ROOM** button. Press Escape. | The room panel closes. |
| F-24 | P0 | Open PARTS, then open the room panel. Press Escape twice. | First press closes the **room panel**, second closes the **drawer**. Third would leave. |
| F-25 | P1 | Open PARTS, then close it with its own **X**. Then press Escape. | You leave to the model list (the drawer is not left behind on the stack). Same with the **PARTS/CLOSE** button. |
| F-26 | P0 | Placeholders on. Tap **AI** → OPEN ANOTHER → OPEN ANOTHER. Press Escape three times. | Panels close **newest first**: 3, then 2, then 1. Each press logs the stack. |
| F-27 | P1 | Open three test panels. Tap **CLOSE** on panel 2 (the middle one, partly hidden). Then press Escape twice. | CLOSE removes only panel 2. Escape then closes 3, then 1. |
| F-28 | P1 | Open PARTS, then a test panel. Tap the **PARTS/CLOSE** button. Press Escape. | The drawer closes; Escape closes the test panel; nothing else. |
| F-29 | P1 | Open PARTS and two test panels. Press **BACK** (on-screen) three times. | Same order as Escape: test panel 2, test panel 1, drawer. |
| F-30 | P0 | Open PARTS and a test panel. Use the on-screen BACK until you leave. Open a model again. Run **log back stack**. | `Back stack: (empty)`. Nothing from the previous visit survives. |
| F-31 | P1 | At the model list (AR scene) with the room panel open, press Escape. | Nothing new happens here. The back button only acts in the 3D viewer in Phase 0. |
| F-32 | P0 | **phone** Open PARTS. Press the phone's **back button** (or back gesture). Then again. | First: the drawer closes. Second: back to the model list. |
| F-33 | P1 | **phone** Open the room panel in the 3D view. Phone back. | The room panel closes. |

### 4.3 HUD layout

Check each case in the Game view at **1080×1920**, **1080×2400** (tall phones) and **720×1280**.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| F-40 | P0 | Open a model, turn the placeholders on. | Left edge: a column of **five** buttons (AI, SIMULATE, SECTION, NOTES, VIEWS). Top right under Share: **SETTINGS** and **REPORTS**, REPORTS with a red **1**. All labels readable, none cut off. |
| F-41 | P0 | With placeholders on, look at the rail against the ROOM, PARTS and BACK buttons at the bottom, and the top bar. | **No overlaps.** The rail ends well above the bottom buttons. |
| F-42 | P0 | Open PARTS with placeholders on. | The drawer opens **to the right of the rail** and covers none of its buttons. The rail stays tappable. |
| F-43 | P0 | Select a part with a long name. | The chip under the top bar ends **before** the SETTINGS button; the name is cut with `...`. |
| F-44 | P0 | Alone in a room, placeholders on. | The top bar, Share, SETTINGS/REPORTS and the player badges area do not overlap. |
| F-45 | P1 | Turn the placeholders off. | Every placeholder disappears. The HUD is exactly as before Phase 0, except the chip width and drawer position (section 2). |
| F-46 | P1 | Placeholders on: drag on the model beside the rail and the test panel; then drag starting **on** a rail button and on a test panel. | Beside them: the model rotates. On them: it does **not**. |
| F-47 | P1 | **phone** Open a model and open PARTS. Select a part with a long name. | The drawer sits slightly in from the left edge and is fully on screen; the part chip is cut with `...` and overlaps nothing. (The placeholders need the Inspector, so they are Editor-only.) |

### 4.4 State bus

Turn on **toggle state-bus log** first.

| ID | Pri | Steps | Expected Console lines |
|---|---|---|---|
| F-50 | P1 | Open a model. | `[StateBus] model.loaded = "<model key>" (local)` |
| F-51 | P1 | Move the explode slider. | `[StateBus] model.explode = 0.42 (local)` and so on while you drag. |
| F-52 | P1 | Tap a part, then tap it again. | `part.select.local = "<path>" (local)`, then `part.select.local = "" (local)`. |
| F-53 | P1 | Isolate a part, then show all. | `part.isolate = "<path>" (local)`, then `part.isolate = "" (local)`. |
| F-54 | P1 | Alone in a room (you are presenter), tap a part. | `part.select.room = "<path>" (local)`: the presenter's pick, which is shared. |
| F-55 | P2 | Turn the log off and repeat F-51. | No `[StateBus]` lines. |

### 4.5 Hygiene

| ID | Pri | Steps | Expected |
|---|---|---|---|
| F-60 | P0 | Watch the Console through **all** of the above. | No `NullReferenceException`, no `MissingReferenceException`, no other exceptions. |
| F-61 | P0 | First open of the project in Unity after this phase. | Compiles with **no errors**. (Warnings about unassigned `[SerializeField]` fields are normal and were there before.) |

---

## 5. Regression: V2 single-device cases to re-run

Phase 0 changed how parts move and how panels close, so these existing cases must still pass:

| Sheet | Cases | Why |
|---|---|---|
| `V2_PARTS_DRAWER_TESTS.md` §2 | **D-01 to D-35**, P0s at least | The drawer registers with the back stack and moved right. D-07 (overlaps) and D-13/D-14 (touch) matter most. |
| `V2_PARTS_DRAWER_TESTS.md` §5.1 | **E-01 to E-16** | The exploder now moves parts through the composer. E-05 (exact return, repeated) is the key one. |
| `V2_BUILD_NOTES.md` §4.5 | **P-07, P-08** | Single-device tap behaviour around HUD buttons. |

---

## 6. Multi-device cases

Not run in this phase. They are in **`V3_PENDING_DEVICE_TESTS.md`** under *From Phase 0*, and are run
at the device-testing checkpoint before Track B starts.

---

## 7. If something fails

| Symptom | Most likely cause | What to send |
|---|---|---|
| A part does not return exactly (F-02, F-05, F-10, E-05) | A layer was not cleared | The case ID, and the output of **log back stack** after the failure |
| Isolated part still drifts with the slider (F-06) | Re-centring did not run | Whether the Console shows any exception when you move the slider |
| Back closes the wrong panel, or nothing (F-20 to F-30) | A panel opened without registering | The case ID and the `[Back]` / test-panel log lines |
| Layout overlaps (F-41 to F-44) | Anchor values on your screen size | A screenshot and the Game view resolution |
