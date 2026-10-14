# AuraSync V3 – Phase 3b (AI chat: Share to room): build notes and test plan

In a room, every AI answer gets a **SHARE** button. A shared answer appears on everyone's screen
on a card beside the part it is about, with who asked and what they asked. Anyone can dismiss the
card on their own phone. The asker or the presenter can remove it for everyone.

> **Status:** compiled clean (0 errors). **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions made at the start of this phase (2026-10-12)

| Question | Answer |
|---|---|
| D5: private or shared? | **Private by default**; SHARE under each answer (as planned). |
| Who can share? | **Anyone** in the room. The **asker or the presenter** can remove a shared answer for everyone; anyone can dismiss it on their own phone. |
| How does it appear? | A **card beside the part** with a line to it: asker, question, answer, X. Several answers: the newest shows, **‹ 1 of 3 ›** steps through them. |
| On model switch? | **Remembered per model**: hidden on switching, back when the presenter returns to that model, for as long as the room lasts. |

Defaults chosen without asking (say if any should change):

- **ASK MORE** on the card opens *your own* chat on that part, with the shared question and
  answer already in it, so your follow-up question carries on from them.
- At most **10 shared answers per model**. Sharing an 11th drops the oldest.
- Sharing sends **that one question and answer**, not the whole conversation.
- The card **hides while your chat panel is open** and during the reassembly challenge.
- Outside a room there is no SHARE button.

## 2. What was built

| File | What |
|---|---|
| `AI/SharedAnswers.cs` | **New.** The shared answers: share, remove, dismiss, read. One room property per answer (`SA:` + model hash + id), so two people sharing at the same moment cannot overwrite each other. |
| `UI/SharedAnswerCard.cs` | **New.** The card beside the part. |
| `UI/PartChatUI.cs` | SHARE / SHARED under each answer (in a room); ASK MORE support. |
| `AI/PartChat.cs` | Remembers which answers were shared; the AI log counts shares. |
| `Networking/EventCodes.cs`, `Space/ModelStateMemory.cs`, `Space/SpaceSceneManager.cs` | The room key, a shared model hash, and creating the card. |

**Change from the plan:** the plan reserved event code 21 for shared answers. It is not used:
room properties already reach everyone and late joiners, so one mechanism does both.

**Setup in Unity:** none.

---

## 3. Tests

**A** = presenter, **B**, **C** = viewers. Use the Jet Engine (it has part knowledge) unless the
case says House.

### 3.1 Sharing

| ID | Pri | Steps | Expected |
|---|---|---|---|
| A-01 | P0 | Not in a room: open the chat on a part and ask something. | Answer with Helpful? Yes / No, and **no SHARE button**. |
| A-02 | P0 | A and B in a room. A selects the fan, ASK AI, asks a question. | The answer has **SHARE** next to Helpful?. |
| A-03 | P0 | A taps **SHARE**. | A: toast *"Shared with the room."*; the button reads **SHARED** (greyed). **B**: a card appears beside the fan with a line to it: *"A asked about Fan rotor"*, the question, the answer. |
| A-04 | P0 | A closes the chat. | A sees the same card too. |
| A-05 | P0 | **B** asks about another part and shares. | The card on **both** phones jumps to B's answer, beside that part; it reads **2 of 2**. |
| A-06 | P0 | Tap **‹**, then **›**. | Steps between A's and B's answers; the card moves to each answer's part. |
| A-07 | P1 | Turn and zoom the model. | The card follows its part and switches sides when the part crosses the middle. It never covers the left rail, top bar or bottom buttons. |
| A-08 | P1 | Open the **House** (whole-model chat), ask, share. | The card says *"asked about the whole model"*, sits top-right, no line. |
| A-09 | P1 | Isolate a part other than the one a shared answer is about. | That answer's card moves top-right with no line (its part is hidden). |
| A-10 | P2 | A long answer. | The answer scrolls inside the card; the card does not grow. |

### 3.2 Dismiss and remove

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-01 | P0 | B taps **X** on a card. | Gone on **B only**; B sees the next older answer, or no card if none is left. A still sees it. |
| D-02 | P0 | B looks at an answer **A** shared. | No REMOVE button (B is not the asker or presenter). |
| D-03 | P0 | B taps **REMOVE** on B's own answer. | Toast *"Removed for everyone."*; gone on every phone. |
| D-04 | P0 | A (presenter) taps **REMOVE** on B's answer. | Gone on every phone. In B's chat that answer's button reads **SHARE** again. |
| D-05 | P1 | B dismissed an answer (D-01); someone shares a new one. | The new one shows; the dismissed one stays hidden. |

### 3.3 ASK MORE

| ID | Pri | Steps | Expected |
|---|---|---|---|
| M-01 | P0 | B taps **ASK MORE** on A's shared answer about the fan. | B's chat opens on the fan with *"A asked: ..."* and A's answer in it (no Helpful? / SHARE under that one). The card hides while the chat is open. |
| M-02 | P0 | B asks a follow-up ("why?"). | The AI answers following on from A's answer. |
| M-03 | P1 | B closes the chat and taps ASK MORE on the same card again. | The shared Q and A are **not** added a second time. |

### 3.4 Late joiners and model switches

| ID | Pri | Steps | Expected |
|---|---|---|---|
| L-01 | P0 | Two answers shared. **C** joins. | Once the model loads, C sees the card with the newest answer, **2 of 2**. |
| L-02 | P0 | A switches to another model. | No cards on any phone (that model has no shared answers yet). |
| L-03 | P0 | A goes back to the first model. | The first model's shared answers are back on every phone. |
| L-04 | P1 | Share 11 answers on one model. | 10 remain; the oldest has gone. |
| L-05 | P1 | B leaves the room. | B's cards disappear; B's chat answers no longer show SHARE. |
| L-06 | P1 | A and B tap SHARE within the same second. | **Both** answers appear (2 of 2) on every phone. |

### 3.5 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| P-01 | P2 | After sharing, run **V3 debug: AI, search, challenge and control stats**. | The AI line ends with *"Shared to a room: N"*. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_3_TESTS.md` | **C-01, C-02, C-20, C-43** | The chat's answer row and Open() changed. |
| `V3_PHASE_4_TESTS.md` | **S-40** | The chat header row. |
| `V3_PHASE_1_TESTS.md` | **G-02, G-07, R-01** | Toasts and the badge menu sit near the card. |

---

## 5. Follow-up 2026-10-13: minimize the shared-answer card

The combined 3b + 5 pass passed. You then asked for a way to get the card out of the way of the
model. The card's header now has **_** (minimize) beside X.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| MN-01 | P0 | Tap **_** on the card. | The card and its line disappear. A small purple pill **SHARED ANSWERS (n)** sits at the top right, clear of the top bar and corner buttons. The model is unobstructed. |
| MN-02 | P0 | Tap the pill. | The card is back on the same answer, beside its part. |
| MN-03 | P0 | Minimize, then someone shares a new answer. | The card stays folded; the pill reads **NEW** and pulses. Opening it shows the new answer. |
| MN-04 | P1 | Minimize, switch model and come back (or open another model that has shared answers). | Still minimized (it stays folded until you open it, for as long as the app is open). |
| MN-05 | P1 | Minimize on A. | B's card is unaffected (minimizing is per phone). |
