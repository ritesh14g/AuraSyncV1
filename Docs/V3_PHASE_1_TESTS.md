# AuraSync V3 – Phase 1 (Fine-grained ownership): build notes and test plan

The presenter can hand **one control** to a student without giving up the presenter role ("Asha,
you drive the explode slider"). A viewer can ask for a control; the presenter allows or denies it
in one tap. The "show this to everyone?" popup also gets a **KEEP SHARING** button, so whoever
drives a control can stop being asked every time.

This is the **first Track B phase**. Most of its tests need two or three devices. The Unity Editor
can be one of them (Editor + one phone is enough for most two-device cases).

> **Status:** compiled clean (0 errors). **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions made at the start of this phase (2026-10-12)

| Question | Answer |
|---|---|
| How does a holder's change reach the room? | Through the same **ACCEPT / DENY** popup the presenter gets, plus a third button **KEEP SHARING**: share it and stop asking for that control. Applies to the presenter too. |
| What does the presenter see while a student holds a control? | They **watch** the student's changes; their own slider / ISOLATE is locked, and **TAKE BACK** returns the control in one tap. |
| How does the presenter grant? | **Tap a player's badge** → a menu: tick Explode / Isolate / Highlight, or MAKE PRESENTER (tap twice). Replaces the old "tap the badge twice". |
| How does a viewer ask? | **ASK TO DRIVE** in the Parts drawer, next to the explode slider. The presenter gets ALLOW / DENY, which declines by itself after 20 s. |

Defaults chosen without asking (say if any should change):

- Controls in this phase: **Explode, Isolate, Highlight**. Isolate was added because it has been
  shared since the 2026-10-12 fixes. Section cut and Simulation get their slot now and are wired
  up in Phases 6 and 7. "Only the author or presenter edits a note" waits for Phase 5.
- KEEP SHARING is **per control**, and stays on until STOP LIVE, losing the control, or leaving
  the room. A live explode is shared when the finger lets go of the slider, not continuously.
- **Highlight has no popup** (the presenter's highlight never had one; a holder's works the same).
- A holder **stays following** the presenter's view while using their control.
- Controls **stay with the student across model switches**, and when the presenter role is handed
  to someone else. Controls the presenter held move to the new presenter.

## 2. What was built

| File | What |
|---|---|
| `Networking/ControlOwnership.cs` | **New.** Who drives each control. Request / Allow / Deny messages. Check-and-set writes, clean-up when a holder leaves, and the paper log (`ControlLog.jsonl`). |
| `Networking/LiveSharing.cs` | **New.** KEEP SHARING: which controls this phone shares without asking. |
| `UI/PlayerControlMenu.cs` | **New.** The menu that opens when the presenter taps a badge. |
| `UI/ModelPartsDrawerUI.Controls.cs` | **New.** The **ROOM CONTROLS** box in the Parts drawer. |
| `UI/Toast.cs` | **New.** Short messages above the bottom buttons ("You now control: Explode"). |
| `UI/ShareWithRoomPrompt.cs` | Third button (KEEP SHARING), custom labels (ALLOW / DENY) and a countdown. |
| `Space/ModelExploder.cs`, `ModelIsolator.cs`, `ModelPartSelector.cs` | "Is this phone the presenter?" became "does this phone hold the control?". |
| `UI/SpaceSessionHUD.cs` | A badge tap opens the menu. Badges show what each student drives. The part chip names the Highlight holder. |
| `Networking/EventCodes.cs` | Room keys `CT:explode`, `CT:isolate`, `CT:highlight` (and section / simulation). |
| `Core/SceneBootstrapper.cs`, `Space/SpaceSceneManager.cs`, `UI/PartChatUI.Search.cs`, `UI/SpaceSessionHUD.Slots.cs` | Small hooks. |

**Setup in Unity:** none. Everything is built at runtime.

### How it works (for reading the results)

- **The state** is one room property per control. It holds the student's actor number, or 0 for
  "the presenter". A holder who has left counts as the presenter at once.
- **No race conditions:** every change is a Photon check-and-set (`expectedProperties`). If two
  changes cross, the server applies one and rejects the other. The rejection is logged as a
  *conflict prevented*.
- **Messages** (event code 20) carry only requests and declines. A grant is the room property
  changing.

---

## 3. Tests

**A** = presenter (created the room), **B** and **C** = viewers who joined. "Drawer" = PARTS. The
**ROOM CONTROLS** box sits under the explode slider and only shows in a room.

### 3.1 One device (Editor is fine)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| O-01 | P0 | Open a model, **not** in a room. Open PARTS. Explode, isolate. | No ROOM CONTROLS box, no popups. Everything works as before. |
| O-02 | P0 | Create a room (alone). Open PARTS. | ROOM CONTROLS shows Explode, Isolate and Highlight, each **"you (presenter)"**, with no buttons. |
| O-03 | P0 | Drag the explode slider and let go. | Popup with **three** buttons: DENY, ACCEPT, KEEP SHARING. The body explains all three. |
| O-04 | P0 | Tap **KEEP SHARING**. | Toast *"Explode is shared live..."*. The Explode row reads **LIVE** with a **STOP LIVE** button. |
| O-05 | P0 | Drag and let go again, twice. | **No popup.** (That the room really receives each release is checked with two devices in G-06.) |
| O-06 | P0 | Isolate a part. | The popup **does** appear: KEEP SHARING is per control. |
| O-07 | P0 | Tap **STOP LIVE** on the Explode row, then drag and let go. | The row loses LIVE; the popup is back. |
| O-08 | P1 | Popup open → phone Back (or Escape). | Closes as DENY. |
| O-09 | P1 | KEEP SHARING on Explode, then **leave** the room and create a new one. | Not live any more: the popup is asked again. |

### 3.2 Granting and taking back (A, B)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| G-01 | P0 | A taps B's badge **once**. | The menu opens: B's name; three rows `[  ] Explode`, `[  ] Isolate`, `[  ] Highlight`; MAKE PRESENTER; CLOSE. Nothing else changes. |
| G-02 | P0 | A ticks **Explode**. | The row turns green `[x]`. B gets the toast **"You now control: Explode"**. A's badge for B reads `B · Explode`. |
| G-03 | P0 | Both open PARTS. | **B:** Explode row "you drive it" + GIVE BACK. **A:** Explode row "B drives it" + TAKE BACK. A's slider is **locked** and its label shows B's name. |
| G-04 | P0 | B drags the explode slider and lets go → ACCEPT. | B **stays following** (Catch Up stays green). **A's** model and slider move to B's amount. Other viewers (C, if there) who are following move too. |
| G-05 | P0 | B → DENY instead. | Only B is exploded. |
| G-06 | P0 | B: KEEP SHARING, then drag again. | B gets no popup; A follows each release. B's row reads LIVE + STOP LIVE + GIVE BACK. |
| G-07 | P0 | A taps **TAKE BACK** on the Explode row (or unticks it in the menu). | B: toast *"Explode went back to the presenter"*, row back to ASK TO DRIVE, not live any more. A's slider unlocks. |
| G-08 | P0 | Give B Explode again; B taps **GIVE BACK**. | Same result as G-07. |
| G-09 | P1 | A gives B **Isolate**. B picks a part, ISOLATE → ACCEPT. | A (and following viewers) see only that part. A's ISOLATE button is greyed out. |
| G-10 | P1 | While B holds Isolate, A taps ISOLATE in the AI chat or on a FIND result. | Nothing isolates; toast *"B drives Isolate. TAKE BACK is in the Parts drawer."* |
| G-11 | P1 | A opens the menu and taps MAKE PRESENTER **once**, waits 4 s. | The button reads "TAP AGAIN: MAKE PRESENTER", then goes back. Nothing changes. |
| G-12 | P0 | A taps MAKE PRESENTER **twice** within 3 s. | B becomes PRESENTER, A becomes VIEWER (replaces V2 case M-11). The menu closes. |
| G-13 | P2 | A double-taps a row quickly. | One change, no "changed hands" toast. |

### 3.3 Asking for a control (A, B)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| R-01 | P0 | B: PARTS → **ASK TO DRIVE** on the Explode row. | B: toast *"Asked the presenter..."*; the button reads ASKED... (greyed). A: popup **"B asks to drive Explode"** with ALLOW / DENY and a countdown. |
| R-02 | P0 | A taps **ALLOW**. | B: *"You now control: Explode"*, as in G-02. |
| R-03 | P0 | Repeat with Isolate; A taps **DENY**. | B: *"The presenter kept Isolate."*; the button goes back to ASK TO DRIVE. |
| R-04 | P0 | B asks; A does nothing for 20 s. | A's popup closes by itself; B is told the presenter kept it. |
| R-05 | P1 | B asks; A presses phone Back. | Counts as DENY. |
| R-06 | P1 | B asks for Explode while **C** holds it. | A's popup adds *"It is taken from C."*. ALLOW moves it to B; C is told *"Explode was handed to B."* |
| R-07 | P1 | B asks; before A answers, A gives B the same control from the menu; then A taps ALLOW. | Nothing extra happens. No error toast. |

### 3.4 Highlight (A, B)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| H-01 | P0 | A gives B **Highlight**. B taps a part. | The part is **gold** on both phones (no popup). The part chip reads **"B: \<part\>"** instead of "Presenter:". |
| H-02 | P0 | A taps a part while B holds Highlight. | Cyan on A's phone only (A is now picking for themselves, like a viewer). B's gold pick is still shown on A. |
| H-03 | P1 | A takes Highlight back and taps a part. | Gold on both again; chip reads "Presenter:". |
| H-04 | P1 | B holds Isolate, is isolated, has no own pick. A (holding Highlight) taps other parts. | B gets **no** popups: an isolation holder follows their own picks, not somebody else's. |

### 3.5 Leaving, joining, presenter change (A, B, C)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| L-01 | P0 | B holds Explode **while dragging**; B leaves the room (Leave Meeting, or kill the app). | A: Explode row back to "you (presenter)" within a moment; A's slider unlocks. The room keeps B's last shared amount. |
| L-02 | P0 | B holds Explode. A hands the presenter role to **C** (menu → MAKE PRESENTER twice). | **C** is presenter and holds Isolate and Highlight. **B still holds Explode**. A is a viewer with ASK TO DRIVE buttons. |
| L-03 | P0 | Continue L-02: C opens the menu for B. | Explode is ticked for B; C can take it back. |
| L-04 | P1 | B holds Explode, then A hands the presenter role **to B**. | B is presenter; the Explode row reads "you (presenter)" (not "you drive it"). A later hand-over from B to someone else moves Explode with it. |
| L-05 | P1 | B holds Isolate. **A** leaves the room. | The master client becomes presenter (V2 rule). B still holds Isolate. |
| L-06 | P1 | B holds Explode. C joins. | C's drawer shows "B drives it" for Explode; C's badge for B reads `B · Explode`. |
| L-07 | P1 | B holds Explode. A switches model (Back → another model). | B still holds Explode on the new model and can explode it for the room. |

### 3.6 Two at once (A, B, C)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| X-01 | P0 | B and C both tap ASK TO DRIVE on **Isolate** within a second. A taps ALLOW on the first popup, then ALLOW on the second straight away. | **Exactly one** of B / C gets Isolate. A sees *"Isolate changed hands meanwhile, so \<other\> was not given it."*; the other viewer sees *"...changed hands while you were asking."* |
| X-02 | P0 | After X-01, run **V3 debug: AI, search, challenge and control stats** on A. | The Controls line shows **1 conflict prevented** (or more if repeated). |
| X-03 | P1 | B holds Explode. A taps TAKE BACK at the same moment as B taps GIVE BACK. | Explode ends with A. No error. One of the two may be logged as a conflict prevented. |

### 3.7 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| P-01 | P1 | After 3.3, run the stats menu item on **B**. | Controls line: requests, a median **request-to-grant** time in ms. |
| P-02 | P1 | Copy `ControlLog.jsonl` from each phone's persistent data folder. | One JSON line per request / allow / deny / grant / conflict. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PENDING_DEVICE_TESTS.md` | **SH-01 to SH-08, VW-01 to VW-04, MS-01, MS-03** | The share popup and "who shares" were rewritten. With nothing handed out, the results must be exactly as before, apart from the extra KEEP SHARING button. |
| `V2_BUILD_NOTES.md` §4 | **M-10, M-11** now read as **G-01, G-12**; **M-14** unchanged | The badge tap now opens a menu. |
| `V3_PHASE_0_TESTS.md` | **F-01, F-02, F-06, F-09** | Isolate's entry points changed. |
| `V3_PHASE_3_TESTS.md` / `V3_PHASE_4_TESTS.md` | **C-43, S-17** | The ISOLATE buttons in the chat and FIND results. |

## 5. Paper metrics

- **Conflicts prevented:** check-and-set writes the server rejected (X-01). Housekeeping writes are
  left out.
- **Request-to-grant time:** from ASK TO DRIVE to the grant arriving on the requester's phone,
  and the presenter's own decision time on the Allow / Deny lines.

Both come from `ControlLog.jsonl` and the stats menu item.
