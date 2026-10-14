# AuraSync V3 – Phase 5 (Notes attached to parts): build notes and test plan

Put a note on an exact spot of a part. It stays stuck there while the model turns, explodes or
is isolated. A note is shared with the room ("Everyone") or kept to yourself ("Only me"), and is
saved on your phone, so it comes back next time.

> **Status:** compiled clean (0 errors). **Not yet run in Unity.**
>
> **Combined test pass (your choice, 2026-10-13).** Phase 3b was not tested before Phase 5
> started. Run **`V3_PHASE_3B_TESTS.md` first**, then this sheet. If a case fails, its sheet
> says which phase it belongs to.
>
> **Gate:** every **P0** case in `V3_PHASE_3B_TESTS.md` §3 **and** in section 3 below passes.

---

## 1. Decisions made at the start of this phase (2026-10-13)

| Question | Answer |
|---|---|
| **D4:** do notes outlive the session? | **Yes, on the author's phone**, per model. Whenever the author is in a room on that model, their "Everyone" notes are shared again, whether they are the presenter or a student. No backend. |
| What does a note look like? | A **pin with a short label** (the first few words). Tap it to read the whole note. |
| What does a note hold? | **Text (up to 300 characters) and one of five sticky-note colours.** |

Consequences of D4, and defaults chosen without asking (say if any should change):

- **A note is in the room only while its author is.** When a student leaves, their notes leave
  with them, and come back when they return. That is also what makes the presenter's edits and
  deletes stick: the author's phone is there to save them.
- **Edit and delete:** the author, or the presenter for a shared note (Phase 1's rule). The
  presenter cannot make someone else's note private.
- **Anyone can add notes.** There is no control token for notes.
- **Hiding is on your phone only** and lasts until the app closes: HIDE ALL in the list, or HIDE
  on one note's card. The plan said long-press for one note; a HIDE button on the card is easier
  to find.
- **Tapping a note in the list turns the model** so the note faces you, then opens it. For a
  viewer who was following, that counts as exploring (Catch Up brings them back). When the
  presenter does it, everyone following turns too.
- Notes work outside a room too ("Everyone" notes wait until you are next in a room on that model).
- At most **30 notes per person per model**.
- The AR scene's note editor could not be reused here (it is wired into the V1 scene), so the 3D
  viewer has its own, in the same sticky-note colours.

## 2. What was built

| File | What |
|---|---|
| `Notes/PartNote.cs` | **New.** A note (spot and direction in the part's own coordinates), and the file of your own notes per model (`AuraSync/PartNotes/<model>.json`). |
| `Notes/PartNoteManager.cs` | **New.** Add / edit / delete, permissions, sharing through room properties, placing with a tap, hiding, the drift measurement and `NotesLog.jsonl`. |
| `UI/PartNotePins.cs` | **New.** The pins on the model, and the "tap the spot" banner. |
| `UI/PartNotesPanelUI.cs` | **New.** NOTES on the left rail: the list, the note card, the editor. |
| `Space/ModelRaycaster.cs` | A tap now also reports the surface direction, so a note knows which way it faces. |
| `Space/SpaceInteractionController.cs` | `TurnToFace`: turn the model so a spot faces you. |
| `Space/ModelPartSelector.cs` | The tap that places a note does not also select a part. |
| `UI/ModelPartsDrawerUI.cs`, `Space/SpaceSceneManager.cs`, `Networking/EventCodes.cs`, `UI/SpaceSessionHUD.Slots.cs` | Small hooks: the drawer and the notes list close each other, creating the notes, the room key, debug items. |

**How notes travel:** one room property per note,
`PN:<model>:<note id>:<author's actor number>`. The plan reserved event code 22; it is not used,
for the same reason as in Phase 3b. Pins are drawn on screen at the note's spot every frame. They
are not objects inside the model, so the parts list, explode and isolate never see them.

**Setup in Unity:** none.

---

## 3. Tests

**A** = presenter, **B**, **C** = viewers. One device unless the ID starts with **N2** (2–3
devices). Jet Engine unless stated.

### 3.1 Adding and reading (one device, Editor is fine)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| N-01 | P0 | Open a model. | **NOTES** on the left rail. |
| N-02 | P0 | NOTES → list *"No notes on this model yet..."* → **ADD**. | The list closes; a purple banner *"Tap the spot on the model..."* with CANCEL. |
| N-03 | P0 | Tap a spot on a fan blade. | **No part gets selected.** The editor opens: *"New note on Fan rotor"*, text box, 5 colours, EVERYONE / ONLY ME, CANCEL / SAVE (greyed until you type). |
| N-04 | P0 | Type "Check the blade root", pick green, SAVE. | Toast *"Note saved."*; a green diamond on the exact spot with *"Check the blade root"* beside it. The NOTES button shows **1**. |
| N-05 | P0 | Tap the pin. | The card: green strip, *"Note by \<you\>"*, *"On: Fan rotor"*, the text; EDIT, DELETE, HIDE, CLOSE. |
| N-06 | P1 | ADD → tap empty space. | Toast *"Tap on the model itself..."*; still placing. CANCEL (or Back) ends it. |
| N-07 | P1 | Write 300+ characters. | Stops at 300. The pin label shows the first ~22 characters and "...". |

### 3.2 Sticking to the spot (one device)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| N-10 | P0 | Turn and zoom the model. | The pin stays exactly on its spot. On the far side it turns faint and loses its label. |
| N-11 | P0 | Explode slowly 0 → 100% → 0. | The pin rides along with its blade the whole way. |
| N-12 | P0 | Isolate the fan blade's part. | The pin stays on the spot of the enlarged, centred part. |
| N-13 | P0 | Isolate a **different** part. | The note's pin disappears (its part is hidden); SHOW ALL brings it back. |
| N-14 | P0 | Back to the model list, open the Jet Engine again. | The note is back on the same spot. |
| N-15 | P0 | Close the app completely, reopen, open the Jet Engine. | The note is still there (saved on the phone). |
| N-16 | P1 | Run **V3 debug: measure note drift** assembled, exploded 100%, and isolated. | Each time *"worst 0.0xx% of part size"* or less (rounding only). |

### 3.3 Editing, deleting, hiding (one device)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| N-20 | P0 | Card → EDIT → change text and colour → SAVE. | The pin and card show the new text and colour. |
| N-21 | P0 | Card → DELETE once. | It reads *"TAP AGAIN"* for 3 s; nothing deleted yet. |
| N-22 | P0 | Tap DELETE again within 3 s. | Toast *"Note deleted."*; the pin is gone; reopening the model does not bring it back. |
| N-23 | P0 | Add two notes. NOTES → **HIDE ALL**. | Both pins vanish; the rail reads *"NOTES (off)"*. SHOW ALL brings them back. |
| N-24 | P1 | Card → **HIDE** on one note. | Only that pin goes; in the list it says *"hidden"*. Tapping its row shows it again and opens it. |
| N-25 | P1 | Turn the model so a note is on the far side. Tap its row in the list. | The model turns until the note faces you; the card opens. |
| N-26 | P1 | Open PARTS while NOTES is open, and the other way round. | Each closes the other. |
| N-27 | P1 | Start the reassembly challenge. | No pins, no NOTES button. They come back when the game ends. |

### 3.4 In a room (2–3 devices)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| N2-01 | P0 | A adds an **Everyone** note. | B sees the pin at the same spot within a moment. B's card shows *"Note by A"*, with **only HIDE and CLOSE** (no EDIT / DELETE). |
| N2-02 | P0 | B adds an **Only me** note. | A **never** sees it. B's card says *"only you"*. |
| N2-03 | P0 | B adds an Everyone note. A (presenter) opens it. | A has EDIT and DELETE. |
| N2-04 | P0 | A edits B's note (text + colour). | B: toast *"The presenter edited one of your notes."*; the change shows everywhere. B closes and reopens the app and model: the **edited** version is there. |
| N2-05 | P0 | A deletes B's note. | Gone on all phones; B: *"The presenter deleted one of your notes."*; it does not come back on B after restarting. |
| N2-06 | P0 | C (another viewer) opens B's note. | No EDIT / DELETE. |
| N2-07 | P0 | B hides all notes. | Only B's screen changes. |
| N2-08 | P0 | B has two Everyone notes. **B leaves** the room. | A no longer sees B's notes. |
| N2-09 | P0 | B rejoins. | B's notes are back for everyone, at the same spots. |
| N2-10 | P0 | A adds notes, everyone leaves, room closed. Next day A creates a room on the Jet Engine and B joins. | B sees A's notes (A's phone shared them again). |
| N2-11 | P0 | A explodes and shares (Accept). | On B, A's and B's pins ride along with their parts. |
| N2-12 | P1 | Notes on the Jet Engine. A switches to the House, then back. | House: none of the engine's notes. Back on the engine: they are there again. |
| N2-13 | P1 | C joins while notes exist. | C sees every note of the authors present. |
| N2-14 | P1 | B makes their Everyone note **Only me** (EDIT → ONLY ME → SAVE). | It disappears on A and C; B still has it. Then back to EVERYONE: it reappears. |
| N2-15 | P1 | A taps a note in the list (the model turns). | B, following A, turns with A. |
| N2-16 | P2 | B adds a note while **not** in a room, then joins A's room on that model. | A sees B's note once B's model has loaded. |

### 3.5 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| P-01 | P1 | After 3.2, run **V3 debug: AI, search, challenge and control stats**. | A *"Notes:"* line: added / edited / deleted, drift measurements and the worst drift. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_3B_TESTS.md` | **all P0** (run first) | Combined pass. |
| `V2_PARTS_DRAWER_TESTS.md` / `V3_PHASE_0_TESTS.md` | Tap-to-select cases, **F-01, F-09** | A tap now goes through a "placing a note?" check first; the raycaster gained a surface direction. |
| `V3_PHASE_12_TESTS.md` | **R-01, R-03** | The challenge hides the notes. |
| `V3_PHASE_1_TESTS.md` | **H-01, H-02** | Highlighting by tap. |

## 5. Paper metrics

**Note placement drift:** distance from each note to its part's real surface, as a share of the
part's size, measured in each state (assembled, exploded, isolated) by the debug item and logged
to `NotesLog.jsonl`. Expected to be zero up to rounding, because notes are stored in the part's
own coordinates.
