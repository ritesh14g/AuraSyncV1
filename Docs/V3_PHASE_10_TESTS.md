# AuraSync V3 – Phase 10 (Interaction heatmap per part): build notes and test plan

**Settings → HEATMAP** recolours the model by how much attention each part got: red = most, then
yellow and green, blue = least, grey = never touched. You can show **Everyone** or one person, see
the **5 most** and **5 least** looked-at parts, **SAVE TO REPORTS**, and **DONE** puts the normal
look back. A heatmap can also be made **from a recording** (RECORDINGS → HEATMAP).

> **Status:** compiled clean (0 errors). **Not yet run in Unity.** Built straight after Phase 9,
> untested, at your request: everything is tested together (see `V3_COMBINED_TEST_PASS.md`).
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions (defaults; you asked to build without stopping, so none were asked)

- **What counts, per part, per person:**

  | Counted | Weight |
  |---|---|
  | picks (selecting it, on the model or in PARTS) | 3 each |
  | notes added on it | 5 each |
  | AI questions asked about it | 5 each |
  | seconds selected | 0.2 each |
  | seconds isolated | 0.2 each |
  | seconds at the centre of the screen (the same "where you look" as the cones) | 0.1 each |

  One number per part: deliberate actions weigh most, and a part people only looked at still warms
  up. The colours are relative: the hottest part is always red.
- **A group's count warms every part inside it** (picking a group in PARTS).
- **Sharing:** every phone in a room sends its totals to the presenter every 10 s (event 27). The
  presenter's heatmap has everyone, including people who have left. A viewer's HEATMAP (only alone,
  per D6) has their own counts.
- Counts **start again on another model**. A replay is not counted. The full model replacing the
  preview (Phase 13) keeps the counts.
- The colour goes on **over** the materials (not swapping them), so the **section cut keeps
  working** under a heatmap.
- The lists rank the knowledge file's labelled parts (Jet Engine: 11). For the House, its mesh
  chunks.
- **From a recording:** the presenter's picks and selected / isolated time, notes, and every
  viewer's looking time from their recorded cones. Painted on the recording's model, which must be
  open.

## 2. What was built

| File | What |
|---|---|
| `Analytics/InteractionTracker.cs` | **New.** Counting, the 10 s send to the presenter, everyone's totals, counts from a recording. |
| `Analytics/HeatmapView.cs` | **New.** Recolouring (per-renderer colour override), the colour ramp, ranking parts. |
| `UI/HeatmapUI.cs` | **New.** The panel; also the shared UI helpers (`UIBits`) used by Phases 10–13. |
| `UI/SettingsPanelUI.cs` | *CLASS TOOLS*: **HEATMAP** and **AI QUIZ** (AI QUIZ only in a room). |
| `UI/RecordingsUI.cs` | **HEATMAP** on each recording. |
| `Notes/PartNoteManager.cs`, `UI/PartChatUI.cs`, `Space/SharedStateBus.cs` | Notes added and AI questions asked are published, so they count. |

**Setup in Unity:** none.

---

## 3. Tests

Jet Engine unless stated. **H** = one device; **HR** = 2–3 devices, A presenter.

### 3.1 One device

| ID | Pri | Steps | Expected |
|---|---|---|---|
| H-01 | P0 | Alone: Settings. | *CLASS TOOLS* with **HEATMAP** (no AI QUIZ alone). |
| H-02 | P0 | Tap the **fan rotor** 5 times (select, deselect...), look at it for a while, add a note on it, ask the AI one question about it. Then Settings → HEATMAP. | The fan rotor is **red**; parts you never touched are **grey**; others blue to yellow. MOST list: *1. Fan rotor*. LEAST list shows untouched parts as *never*. |
| H-03 | P0 | **DONE** (or Back). | The model looks exactly as before: textures and colours back. |
| H-04 | P0 | Explode the model, isolate a part, then open HEATMAP. | The colours are right on the exploded / isolated parts; DONE restores them. |
| H-05 | P1 | SECTION cut on, then HEATMAP. | The cut still works; the cut parts show heat colours. DONE: the cut is still there with normal colours. |
| H-06 | P1 | **SAVE TO REPORTS**. | Toast *"Heatmap saved to REPORTS."*; REPORTS shows *"Heatmap · JetEngine_v2_mobile · Everyone"* with a bar per part. |
| H-07 | P1 | Switch to the House, open HEATMAP. | Counts started again: everything grey until you use the House. |
| H-08 | P1 | With a recording of this model (Phase 9): RECORDINGS → **HEATMAP**. | The panel opens titled with the recording's name, coloured by the recording. < > steps through the people in it. Opening it on another model: *"Open … first"*. |

### 3.2 In a room

| ID | Pri | Steps | Expected |
|---|---|---|---|
| HR-01 | P0 | B (viewer) spends a minute on the **combustor** (looking, selecting it); C on the **fan rotor**. Wait 15 s. A: HEATMAP. | **Everyone:** both parts warm. **<  >** → *B*: the combustor red, fan grey; *C*: the other way round; *A*: A's own. |
| HR-02 | P0 | C leaves. A: HEATMAP → C. | C's counts are still there (*"C (left)"*). |
| HR-03 | P1 | A switches model and back. | Counts on the old model are gone (it is a new session on that model). |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_6_TESTS.md` | **S-01** | The colour override sits on top of the section cut's materials. |
| `V3_PHASE_5_TESTS.md` | **N-04** | Notes now publish *added*. |
| `V3_PHASE_3_TESTS.md` | **C-01** | The chat now publishes each question. |
| `V3_PHASE_1_TESTS.md` | **G-01** | The corner row's buttons are narrower (100 px) to fit REC, SETTINGS and REPORTS. |

## 5. Paper metrics

- **Attention per part** (the heatmap, or a SAVE TO REPORTS snapshot's CSV), set against quiz
  correctness per part in Phase 11's report.
