# AuraSync V3 – the combined test pass (Phases 8 follow-up, 9, 10, 11, 13)

On 2026-10-14 you asked for the remaining phases to be built back to back and **tested together**.
Everything below is built and compile-checked, and **none of it has been run in Unity yet**. This
page sets up the pass, gives an order, and lists what to check first if something looks broken.

---

## 1. Before you start (once)

1. **Upload to Hugging Face** (the dataset with the mobile files), from the Desktop folder:
   `JetEngine_v2_preview.glb`, `House_preview.glb`, and the updated `models.json`.
   The only change in `models.json` is three new fields per model: `previewUrl`, `previewBytes`
   and `glbBytes`.
2. **Open the project in Unity** and wait for the import. Then check the **Console** for red errors.
   - Two shaders are not compile-checked offline: `AuraSync/AttentionBeam` (new) and
     `AuraSync/SectionClip`. A shader error shows here.
3. **Build the APK.** The build hook adds `AuraSync/AttentionBeam` to Always Included Shaders by
   itself. *AuraSync > Fix glTF Shader Inclusion* does it on demand.
4. **Phones:** at least two (three is better for the quiz and the attention merge). Microphone
   permission on, Photon Voice working, as in the earlier passes.

## 2. Order

Each step builds on the one before, so a failure early on explains failures later.

| # | Sheet | Cases | Devices | Why this order |
|---|---|---|---|---|
| 1 | `V3_PHASE_8_TESTS.md` §6 | **CF-01 – CF-09** | 2–3 | The cone fix. Phase 9's replay shows cones. |
| 2 | `V3_PHASE_9_TESTS.md` §3.1 | **RS-01 – RS-16** | 1 | Settings, recording and replay alone. |
| 3 | `V3_PHASE_9_TESTS.md` §6 | **RV-01, RV-02, RV-05 – RV-09** | 1 | Your voice in recordings (alone). |
| 4 | `V3_PHASE_10_TESTS.md` §3.1 | **H-01 – H-08** | 1 | Heatmap alone (H-08 needs a recording from step 2). |
| 5 | `V3_PHASE_13_TESTS.md` | **L-01 – L-04, L-07 – L-12** | 1 | Preview → full on one phone. |
| 6 | `V3_PHASE_9_TESTS.md` §3.2, §6 | **RR-01 – RR-09, RV-03, RV-04** | 2–3 | Recording in a room, with the presenter's voice. |
| 7 | `V3_PHASE_10_TESTS.md` §3.2 | **HR-01 – HR-03** | 2–3 | The class's heatmap. |
| 8 | `V3_PHASE_11_TESTS.md` | **Q-01 – Q-31** | 2–3 (D for Q-16) | The quiz, then the reports (and their heatmap section). |
| 9 | `V3_PHASE_13_TESTS.md` | **L-05, L-06** | 2 | A viewer on the preview in a live room. |
| 10 | Regression sections of each sheet | as listed | | |

**Gate:** every P0 passes. Failures in P1 cases can be carried forward if you agree, written down as
known issues.

## 3. If something is wrong

| What you see | Likely cause | Where |
|---|---|---|
| Cones still solid | The new shader is missing from the build | Run *AuraSync > Fix glTF Shader Inclusion*, rebuild; check the Console for `AuraSync/AttentionBeam` errors |
| A model opens with no preview / pill | `models.json` on Hugging Face not updated, or the full model is already cached | Upload step 1; delete the model from the phone |
| *"PARTS"* list or notes wrong after the swap | The preview's tree differs | Re-run `python optimize-model.py <name>_mobile.glb --preview` (it refuses a differing tree) |
| GENERATE fails *"not found"* | The deployed AI server is older than its code | `npx --yes wrangler@4.148.0 deploy` in `ai-server` (the live call on 2026-10-14 worked) |
| A replay has no sound | Recorded with the mic off, or the fallback voice system | The REC toast says whether your voice is recorded |
| SHARE does nothing on a phone | Android 9 or older (no MediaStore path) | The toast says where the copy went instead |

## 4. Paper data collected during this pass

From each phone's persistent data folder (or the stats menu item in the Editor):

| File | Contents |
|---|---|
| `AttentionLog.jsonl` | FIND task times, cones on vs off |
| `Recordings/*.aurarec` + `.wav` | Recording size per minute, with and without sound |
| `Reports/*.json` | Quiz scores per part against attention per part |
| `QuizLog.jsonl` | AI question quality (kept unedited) |
| `LoadLog.jsonl` | Time to first render, preview vs full |
| `SimLog.jsonl`, `ControlLog.jsonl` | From the earlier phases |
