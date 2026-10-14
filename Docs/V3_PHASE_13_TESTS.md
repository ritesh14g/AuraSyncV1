# AuraSync V3 – Phase 13 (Bandwidth-aware model loading): build notes and test plan

When a model is not on the phone yet, a **light preview** opens first. An orange pill reads
**PREVIEW QUALITY**. The full model downloads in the background and **swaps in where the preview
was without interrupting anything**: selection, notes, the section cut, the simulation, the
recording, cones and the quiz all carry on.

> **Status:** compiled clean (0 errors). The two preview files are **made and checked** (below).
> **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions (defaults; nothing was asked)

- **The preview is made from the finished mobile file** by `optimize-model.py <name>_mobile.glb
  --preview`:
  - textures cut to 256 px;
  - triangles simplified to 25%, kept **only if** the part tree is identical: the same nodes in
    the same order with the same names and the **same part transforms**, because notes, cones and
    pings are stored in a part's own space;
  - otherwise the full geometry is kept;
  - the script writes `previewUrl`, `previewBytes` and `glbBytes` into the model's entry in
    `models.json`.

  | Model | Full (mobile) | Preview | Parts |
  |---|---|---|---|
  | Jet Engine | 12.84 MB | **1.35 MB** (9.5x smaller) | 11, identical tree |
  | House | 7.07 MB | 4.86 MB (1.5x) | 21, identical tree |

  The House is mostly geometry (a 3D scan cut into chunks). The aggressive simplification that
  would shrink it to 0.3 MB changes the part tree, so it was refused. Correctness first.
- **When the full file comes:**
  - at once on **Wi-Fi**;
  - on **mobile data**, at once if it is 8 MB or less or the preview downloaded at 1.5 MB/s or
    more; otherwise only when you tap the pill → **GET FULL NOW**;
  - **SAVE DATA** (on the pill's card) keeps the preview until GET FULL, on every model.
- **The full model already on the phone opens directly** (no preview). Once downloaded it is kept
  for next time.
- **Offline**, with only the preview downloaded before: the preview opens, *"offline"*.
- **The swap keeps:** the angle and zoom, explode, isolation, your own selection, the section cut,
  the simulation (same moment), following / exploring. The room is not told anything: the model's
  key is its full URL throughout, so presenter and viewers can be on different versions at once.
- **During the reassembly challenge** the swap waits until the game ends.
- The quality setting lives on the pill's card (no separate settings screen).

## 2. What was built

| File | What |
|---|---|
| `optimize-model.py` | `--preview` (with `--preview-texture`, `--preview-simplify`). Its own buffer relayout understands the meshopt-compressed mobile files (the old texture shrinker would have corrupted them). The part-tree-and-transform check. |
| `JetEngine_v2_preview.glb`, `House_preview.glb`, `models.json` | **Made** (in the Desktop folder). |
| `Data/ModelData.cs`, `Data/ModelCatalog.cs` | `previewUrl`, `previewBytes`, `glbBytes`. |
| `Space/ModelVariants.cs` | **New.** The policy, the state for the pill, `LoadLog` (the paper's timings). |
| `Space/SpaceSceneManager.cs` | Preview first; the full file in the background; **the in-place swap**. The after-load steps became `AfterModelPlaced(swap)`. |
| `UI/ModelQualityUI.cs` | **New.** The pill and its card. |

## 3. Setup (you)

1. Upload **`JetEngine_v2_preview.glb`**, **`House_preview.glb`** and the updated **`models.json`**
   from the Desktop folder to the Hugging Face dataset (same place as the mobile files).
2. To test the first-download path again later: delete the model from the phone (the model list's
   delete) or clear the app's data.

---

## 4. Tests

| ID | Pri | Steps | Expected |
|---|---|---|---|
| L-01 | P0 | **Phone on Wi-Fi**, Jet Engine never downloaded: open it. | *"Checking for a quick preview…"*, then the engine appears within a few seconds (blurry textures). Orange pill: *"PREVIEW QUALITY · full model NN%"* counting up. Then *"Full quality model loaded."*, the pill disappears, the textures are sharp. |
| L-02 | P0 | Repeat L-01, but during the download turn the model, select a part, explode a little, isolate a part, add a note, start SIMULATE → Working. | When the swap happens nothing jumps or resets: same angle, same selection, explosion, isolation, the note pin in place, the rotors still spinning at the same speed. |
| L-03 | P0 | Open the Jet Engine again. | Opens straight in full quality (no preview, no pill). |
| L-04 | P0 | **Mobile data** (Wi-Fi off; or Developer options → network throttling), engine deleted first. | The preview opens. The pill reads *"tap for full (12.8 MB)"* unless the connection is fast. Tap → card → **GET FULL NOW**: it downloads and swaps. |
| L-05 | P0 | **Room:** A presenter (full model cached), B joins on slow data with the engine not downloaded. | B sees the preview within seconds and follows A normally (pose, explode, isolate, selection, cones, notes) **while still on the preview**. When B gets the full model nothing on B changes but the textures. A notices nothing. |
| L-06 | P0 | Repeat L-05 with A sharing a section cut and recording (Settings → START RECORDING) before B joins. | On B the cut stays on through the swap. A's recording carries on; replayed later it shows B's cone throughout. |
| L-07 | P1 | Card → **SAVE DATA: ON**. Delete the House, open it. | Preview only, *"saving data, tap for full"*. GET FULL still works. SAVE DATA OFF goes back to automatic. |
| L-08 | P1 | Open the preview once, then airplane mode, delete only the full model (or never get it), reopen. | The preview opens, *"PREVIEW QUALITY · offline"*. |
| L-09 | P1 | Start PLAY (challenge) on a preview, while the full model downloads. | The swap waits until the game ends, then happens. |
| L-10 | P1 | During the full download, switch to another model. | The download stops; the new model loads normally. |
| L-11 | P1 | Run the stats item (Editor) after a few first loads. | *"Loading (downloads, not cache): first render with preview N s, with the full file M s; full file in place after a preview K s"*. |
| L-12 | P1 | `python optimize-model.py JetEngine_v2_mobile.glb --preview` on the PC. | *"parts 11 (identical tree: 14 nodes)"*, *"12.84 MB -> 1.35 MB"*, models.json updated. |

**Regression (after the swap):** `V3_PHASE_5_TESTS.md` N-04 (note pin), `V3_PHASE_6_TESTS.md`
S-01 (cut), `V3_PHASE_7_TESTS.md` SW-01 (spinning), `V3_PHASE_8_TESTS.md` AR-02 (cones),
`V3_PHASE_9_TESTS.md` RS-06 (recording), `V3_PENDING_DEVICE_TESTS.md` MS-01 (model switch).

## 5. Paper metrics

- **Time to first render** (model on screen and usable): preview vs full file, on Wi-Fi vs mobile
  data. Also **time until the full file is in place**. All in `LoadLog.jsonl` (one line per load,
  with the network type, bytes and KB/s) and on the stats line.
- **Size:** 12.84 MB → 1.35 MB for the engine (above).
