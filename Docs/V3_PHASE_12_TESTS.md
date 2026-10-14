# AuraSync V3 – Phase 12 (Reassembly challenge): build notes and test plan

A single-device game: the model's parts fly apart, and the student drags each one back to where it
belongs. Three levels, hints, a score with up to three stars, and a best score per model and level.

This is the **last Track A phase**. After it, work pauses for the device-testing checkpoint
(`V3_FEATURE_PLAN.md` §3.3) before any Track B phase.

> **Status:** compiled clean (game and Editor code: 0 errors). **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. What was built

| File | What |
|---|---|
| `Scripts/Challenge/ReassemblyGame.cs` | **New.** The game: which parts come loose, scattering them, dragging, snapping, assembly order (Hard), hints, scoring, best scores, and a log for the paper. |
| `Scripts/UI/ChallengeUI.cs` | **New.** **PLAY** on the left rail, the level menu, the game bar (time, fitted, moves, HINT, QUIT) and the results card. |
| `SpaceInteractionController.cs` | A small hook: a game can claim a drag that starts on one of its pieces, so the model does not turn under it. |
| `ModelPartSelector.cs`, `PartChatUI.cs`, `ModelPartsDrawerUI.cs` | During a game: taps do not select parts, and the AI and PARTS buttons are hidden. |
| `SpaceSceneManager.cs`, `SpaceSessionHUD.Slots.cs` | Create the game; the stats debug item adds challenge results. |
| `PartChatUI.cs`, `PartChatUI.Voice.cs` | *(Phase 4 follow-up)* MIC on the ASK tab, and the fix for the oversized header row. Tested by `V3_PHASE_4_TESTS.md` S-40 to S-45. |

### The rules

| Level | Parts that come loose | Outline | Snapping | Order |
|---|---|---|---|---|
| **Easy** | The 6 largest | Faint blue outline of the whole model | Generous | Any |
| **Medium** | All (except tiny ones) | Outline | Normal | Any |
| **Hard** | All (except tiny ones) | **None** | Normal | **The real assembly order**: a part fitted too early bounces back and counts as a mistake |

- **Tiny parts** (under 4% of the model's size, like the jet engine's pipe brackets) stay in place:
  too small to grab on a phone.
- **Hard needs the model's assembly order** (from its knowledge file). The **House** has none, so
  Hard is greyed out there with the reason.
- **Moving:** drag a loose part to move it. Drag empty space to turn the model; pinch to zoom. The
  model is shown a little smaller during the game so the scattered parts fit on screen.
- **HINT:** the next part (in assembly order where known, otherwise the biggest left) pulses, and
  its place lights up yellow for 3 seconds.
- **Score:** starts at 1000 plus 50 per part, minus time over the target (8–10 s per part), hints,
  mistakes and wasted moves. **3 stars** = no hints, no mistakes, within target time.
- **Ending:** finished or QUIT, every part goes back exactly where it was.

---

## 2. Tests

Editor with the mouse unless marked **phone**. No room (the challenge is hidden in a room).

### 2.1 Starting

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-01 | P0 | Open the Jet Engine (not in a room). | A **PLAY** button on the left rail, under AI. |
| R-02 | P0 | Tap **PLAY**. | A dimmed screen with the menu: EASY, MEDIUM, HARD, each with a description. CANCEL closes it; so does Escape / phone back. |
| R-03 | P0 | Tap **EASY**. | The parts fly out onto a ring around the model, which shrinks slightly. Faint blue outlines show where the loose parts belong. The game bar shows `0:00  Fitted 0/6  Moves 0` with HINT and QUIT. The AI, PLAY and PARTS buttons are gone. |
| R-04 | P0 | Open the **House**, PLAY. | **HARD** is greyed out with *Hard needs the model's assembly order...*. Easy and Medium work (with the House's big scan pieces). |
| R-05 | P1 | Create a room on this device, look for PLAY. | Not shown. (Leave the room: it comes back.) |

### 2.2 Playing

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-10 | P0 | Drag a loose part. | It follows the pointer smoothly. **The model does not turn.** |
| R-11 | P0 | Drag empty space. Pinch (phone) or scroll (Editor). | The model turns / zooms as usual; the loose parts turn with it. |
| R-12 | P0 | Drop a part far from its outline. | It stays where dropped; Moves goes up by 1. |
| R-13 | P0 | Drop a part close to its outline. | It **snaps** exactly into place, its outline disappears, the message says *Fitted: <name> (n of 6)*, and Fitted counts up. |
| R-14 | P0 | Try to drag a part that is already fitted. | It does not move; the drag turns the model instead. |
| R-15 | P0 | Tap **HINT**. | One loose part pulses and its outline turns **yellow** for about 3 s. The message names it. |
| R-16 | P0 | Fit all 6. | The game ends; the results card shows stars, time (with the target), moves, hints, score and *New best!* on the first finish. |
| R-17 | P0 | After R-16 look at the model, then open PARTS and use EXPLODE. | The model is whole and exactly as before the game; the exploded view works normally. |

### 2.3 Medium and Hard

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-20 | P0 | **MEDIUM** on the Jet Engine. | More parts come loose (all but the tiny ones); outlines shown. Finish it. |
| R-21 | P0 | **HARD** on the Jet Engine. | No outlines. The message says to start with the part that goes in first. |
| R-22 | P0 | In Hard, drop a part that is **not** next in the order onto its place. | It **bounces back** to the ring; *Not yet: something else goes in before...*; Mistakes counts up. |
| R-23 | P0 | In Hard, tap HINT. | The **next** part in the order pulses and its place shows a yellow outline for 3 s. Fitting it works. |
| R-24 | P1 | Finish Hard. | Results include Mistakes. |

### 2.4 Leaving and hygiene

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-30 | P0 | During a game tap **QUIT**. | Parts return instantly; the model is back to normal size; buttons come back. |
| R-31 | P0 | During a game press Escape / phone back. | Same as QUIT. |
| R-32 | P0 | During a game press the on-screen **BACK**. | Same as QUIT (the first press); a second BACK leaves to the model list. |
| R-33 | P1 | Results card: **PLAY AGAIN**. | The level menu opens, now showing *Best: ...* for the level you played. |
| R-34 | P1 | Play Easy again and finish worse. | *Best <score>* (not *New best!*); the best score is unchanged. |
| R-35 | P1 | Stop and restart Play mode, PLAY. | Best scores are still there. |
| R-36 | P1 | After a game, tap parts. | Selection, ASK AI and FIND all work again. |
| R-37 | P0 | Console throughout. | No exceptions. |

### 2.5 Phone and paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-40 | P0 | **phone** Easy on the Jet Engine with a finger. | Dragging parts and turning the model both work; the scattered ring fits on screen (pinch out if a part is off the edge). |
| R-41 | P1 | **phone** Rotate to landscape during a game. | Still playable. |
| R-42 | P1 | Context menu **V3 debug: AI, search and challenge stats**. | Adds: number of challenges, how many completed, and per level the median time and average hints. **Paper metric** (plan: completion time and hints, before and after using the AI assistant). |

---

## 3. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_0_TESTS.md` | **F-01 to F-14** | The game is a third user of PartPoseComposer (Challenge layer). |
| `V2_BUILD_NOTES.md` §4.5 | **P-07, P-08** | The viewer's gesture start has a new hook. |
| `V3_PHASE_3_TESTS.md` | **C-01, C-02, C-20** | Chat after a game. |

## 4. Multi-device cases

None new: the challenge only exists on one device. (R-05 covers it being hidden in a room.)
