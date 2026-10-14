# AuraSync V3 – Phase 2 (Part knowledge + AI server): build notes and test plan

Phase 2 builds the data the AI features run on. Nothing new appears for students yet. What you get:

- the jet engine split so its fan is a separate part,
- a free AI server (Cloudflare Worker: Gemini, with Groq as backup),
- the **Part Labeler** in the Unity Editor, which makes each model's part knowledge file,
- the app loading that file and checking it against the model.

> **Status:** Unity code compiled clean (game: 0 errors; Editor tools: 0 errors). Worker: **22 of 22
> offline tests pass**, and its Cloudflare build check passes. **Not yet run in Unity, and the Worker
> is not deployed**: deploying needs your Cloudflare account.
>
> **Gate for the next phase:** every **P0** case in section 3 passes, and the jet engine's knowledge
> file is published.

---

## 1. What was built

| Where | What |
|---|---|
| `optimize-model.py` | New `--split NODE` (cuts one mesh into parts by its loose pieces) and `--out NAME`. Re-optimising no longer drops extra catalogue fields (`partsUrl`). |
| `JetEngine_v2_mobile.glb` | **New file.** The engine with its "blades" mesh split into `Object_6.1` (fan blades + hub, 38 pieces) and `Object_6.2` (rear disk). 11 parts, 12.8 MB. Every other part keeps its exact position in the hierarchy. |
| `models.json` | The jet engine entry now points at `JetEngine_v2_mobile.glb`. Nothing else changed. |
| `ai-server/` | **New.** The Cloudflare Worker: `/health`, `/ask`, `/find`, `/quiz`. Tests, smoke test, and a step-by-step `README.md`. |
| `Scripts/AI/PartKnowledgeData.cs` | **New.** The knowledge file format. |
| `Scripts/AI/PartKnowledge.cs` | **New.** Downloads the model's knowledge file, caches it on the device, and checks every part against the loaded model. |
| `Scripts/Editor/PartLabeler/*` | **New.** *Tools → AuraSync → Part Labeler* (four files). |
| `ModelData.cs`, `ModelCatalog.cs` | New `partsUrl` field; look up a catalogue entry by model URL. |
| `SpaceSceneManager.cs` | Exposes the loaded model's key; starts the knowledge download after each model load. |
| `SpaceSessionHUD.Slots.cs` | New debug item: **V3 debug: log part knowledge**. |

**Why a new file name for the engine:** phones cache models by URL and never re-download one they
already have. Under the old name, any phone that had opened the engine before would keep the unsplit
version forever.

**Undo:** `backups/pre-phase2/` has the old `models.json` and `optimize-model.py`. The Unity
changes to existing files are small additions (listed above); no separate copy was taken of them.

---

## 2. Your steps, in this order

Each step needs the one before it.

| # | Step | Details |
|---|---|---|
| 1 | **Upload** `JetEngine_v2_mobile.glb` to the Hugging Face repo, **then** `models.json` | In that order: the catalogue must never point at a file that is not there yet. |
| 2 | **Deploy the AI server** | `ai-server/README.md`, *One-time setup*: about 15 minutes, three free accounts, no card. Run the smoke test. **Send me the Worker's address** (never the keys). |
| 3 | **Label the jet engine** | Unity → Play SpaceScene → open Jet Engine → *Tools → AuraSync → Part Labeler* → paste your Gemini key → **Build part list** → **Label with AI** (12 calls, about 2 minutes) → **Work out geometry** → review all 11 parts → **Export** as `JetEngine_v2_parts.json`. |
| 4 | **Label the House** (whole model only) | Open House → tick **Whole model only** → **Label with AI** (1 call) → check the summary → **Export** as `House_parts.json`. |
| 5 | **Send me both JSON files** (or put them in the AuraSync folder) | I add `partsUrl` to `models.json`; you upload the two JSONs, then `models.json`. |
| 6 | Run section 3.4 | Confirms the app loads the labels. |

**Later, for the paper (D8):** find **2–3 more models whose parts are real, separate pieces with
sensible names**, for example on Sketchfab: downloadable, CC-BY, a hierarchy with names like *Piston*
or *Crankshaft*. Good candidates are an engine block, a gearbox, a drone or a bicycle. Optimise them
with `optimize-model.py`, then label them with **Blind test** ticked. Comparing the AI's labels with
the real names is the accuracy result.

---

## 3. Tests

### 3.1 AI server

| ID | Pri | Steps | Expected |
|---|---|---|---|
| W-01 | P0 | In `ai-server`: `npm test` | `# pass 28`, `# fail 0`. *(Already run: passes.)* |
| W-02 | P0 | After deploying: `node smoke-test.mjs <worker url> <app key>` | Five `PASS` lines. `/ask` answers about a jet engine fan; `/find` returns `0/0/4/0` for "the part that sucks air in"; `/quiz` returns 3 questions with 4 options each. Chat and search say `groq` and take about 1–2 s; the quiz says `gemini`. *(Already run against the live Worker on 2026-10-09: all PASS.)* |
| W-03 | P0 | Open `https://aurasync-ai.aurasync-ai.workers.dev/health` in a browser. | `"gemini": true, "groq": true, "appKeySet": true`, and the model lists. No key is shown. |
| W-04 | P0 | Run the smoke test with a **wrong** app key. | Every POST fails with HTTP 401 `bad_key`. No AI quota used. |
| W-05 | P1 | *Checking the backup* in the README (local, Groq key only). | All five `PASS`, every answer says `groq`. |
| W-06 | P2 | `npx wrangler tail` in one terminal while running the smoke test in another. | Requests show in the live log. |

### 3.2 Split jet engine (in the app)

Needs step 1 done.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-01 | P0 | Open the Jet Engine. Open PARTS. | **11** parts with a mesh. `Object_6` is a dimmer group row with `Object_6.1` and `Object_6.2` under it. |
| S-02 | P0 | Pick `Object_6.1`. | The fan at the front (blades and hub) highlights, and nothing else. `Object_6.2` is a ring/disk at the rear. |
| S-03 | P0 | Explode to 100% and back to 0%. | Both new parts move outward and return exactly (Phase 0's F-10 on the new model). |
| S-04 | P0 | Isolate `Object_6.1`. | The fan alone, centred and enlarged. |
| S-05 | P1 | Look at the engine as a whole, assembled. | It looks exactly like the old engine: no gaps or missing pieces where the blades were split. |

### 3.3 Part Labeler (Unity Editor)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| L-01 | P0 | *Tools → AuraSync → Part Labeler*, outside Play mode. | Window opens and says to enter Play mode. Step buttons are disabled. |
| L-02 | P0 | Play SpaceScene, open the Jet Engine, then **Build part list**. | "11 part(s) with their own mesh." Eleven rows in Review, each showing its node name and path. |
| L-03 | P0 | Paste the Gemini key, then **Label with AI**. | Status counts through the model, then parts 1 to 11, with a pause between calls. Each part shows five small pictures: three of the part alone, the whole engine, and the engine without that part. |
| L-04 | P0 | Open a few parts after labelling. | Each has a label, other names, what it does, a material and a motion type. The fan (`Object_6.1`) should be **rotate**. Write down how many labels you think are wrong (paper metric). |
| L-05 | P0 | During labelling, press **Stop**. Then **Label with AI** again. | Stops cleanly ("Everything so far is in the draft"). The second run only does the unlabelled parts. |
| L-06 | P0 | **Work out geometry.** Open the fan entry. | Motion axis about **(1, 0, 0)**, i.e. along the engine. Every part has an assembly step from 1 to 11. Status says whether any had to be forced. |
| L-07 | P1 | Look at the assembly steps. | Plausible: outer casing pieces come out first (highest steps), inner parts last. Note any you would change. |
| L-08 | P0 | Correct one label, tick **Checked by me** on all parts. | The header count reaches `11 of 11 checked`. The corrected field shows `AI said: ...` under it. |
| L-09 | P0 | **Correction report.** | Shows the % of labels, functions, materials and motions you changed. Also written to the Console. **Keep this number for the paper.** |
| L-10 | P0 | **Export...** as `JetEngine_v2_parts.json`. | The file is saved and the folder opens. Status says what to upload and which `partsUrl` line to add. |
| L-11 | P0 | Stop Play mode, start it again, open the window. | Everything is still there (the draft survives). **Load draft** also brings it back if needed. |
| L-12 | P0 | Open the House, tick **Whole model only**, **Label with AI**, **Export**. | **1** AI call. A context like "two-storey house" and a short summary. The exported file has no parts. |
| L-13 | P1 | Explode the model and isolate a part, then press **Label with AI** or **Work out geometry**. | The tool first reassembles and un-isolates the model, so the pictures and geometry are of the whole, assembled model. |
| L-14 | P1 | Use a wrong API key. | "Gemini refused the API key", with no crash. |
| L-15 | P2 | After a run, look at the Scene's layers and colliders. | No `MeshCollider` left on the model; layers are as before. |

### 3.4 App loads the knowledge

Needs step 5 done (JSONs and `models.json` uploaded).

| ID | Pri | Steps | Expected |
|---|---|---|---|
| K-01 | P0 | Open the Jet Engine. Context menu **V3 debug: log part knowledge**. | `'Jet Engine': 11 part(s) labelled (downloaded).` followed by all 11 rows with your reviewed labels. |
| K-02 | P0 | Open the House, then the debug item. | `whole-model knowledge only`, with the context and summary. |
| K-03 | P1 | Open the Jet Engine once, then turn the PC's internet off, stop and replay, and open it again. | `(from the device cache ...)`. Labels still there. |
| K-04 | P1 | Open a model with no `partsUrl` (any local scan). | `... has no part knowledge file yet.` No error. |
| K-05 | P2 | Edit a downloaded JSON so one entry's `path` is wrong (keep its `node`), host it somewhere, and point at it. | Warning: `1 found by name because their position changed`. The label still appears on the right part. |
| K-06 | P0 | Console throughout. | No exceptions. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_0_TESTS.md` | **F-01 to F-14** on the **new** jet engine | New part structure (a group with two children). |
| `V2_PARTS_DRAWER_TESTS.md` | **D-08, D-15 to D-23, E-01 to E-08** on the new jet engine | Same. |

---

## 5. Multi-device cases

In `V3_PENDING_DEVICE_TESTS.md` under *From Phase 2*.
