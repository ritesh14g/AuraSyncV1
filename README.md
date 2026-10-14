# AuraSync

**Collaborative AR and 3D model exploration for the classroom.** A presenter opens a 3D model (a jet
engine, a house scan, any GLB) and a room of students explores it with them in real time, on their
own phones. Students can follow the presenter's view or turn the model their own way. They can take
it apart, cut through it and watch it run, ask an AI about any part, leave notes on it, and be
quizzed on it afterwards.

Built with **Unity 2022.3.62f3** (Built-in render pipeline) for **Android**, with **Photon PUN 2**
and **Photon Voice 2** for multiuser sessions.

---

## Features

### V1: AR and the 3D viewer
- AR placement of models, scans and notes on real surfaces (AR Foundation / ARCore, cloud anchors).
- 3D viewer: rotate, zoom, pan.
- Model catalogue served from a Hugging Face dataset, with on-device caching.

### V2: multiuser
- **Rooms** with a 6-letter code or a share link (deep link). A presenter, viewers, a player list.
- **Follow the presenter** (pose mirrored in real time) or explore freely; **Catch Up** glides back.
- **Live pointers** (Figma-style) on the model surface, in each player's colour.
- **Voice chat** on Photon Voice 2 (Opus, echo cancelling); speaking indicators.
- **Parts drawer**, tap-to-select, **isolate** a part, **exploded view**, per-model state memory.
- **Share with room** prompts, so the presenter decides what everyone sees.

### V3: twelve teaching features
| # | Feature | What it does |
|---|---|---|
| 0 | Foundations | One owner for part movement (`PartPoseComposer`), a shared state bus, the left feature rail, a back stack. |
| 1 | Fine-grained ownership | Hand one control (explode, isolate, highlight, section, simulation) to a student; check-and-set tokens, ASK TO DRIVE, KEEP SHARING. |
| 2 | Part knowledge + AI server | A per-model *Semantic Part Index* (labels, functions, materials, motion, assembly order), AI-proposed and human-reviewed in the Part Labeler (Editor). A Cloudflare Worker proxies free Gemini / Groq models. |
| 3 / 3b | AI part assistant | Chat about any part or the whole model; share an answer to the room. |
| 4 | Semantic search | Find a part by meaning, typed or spoken (Whisper). |
| 5 | Notes on parts | Pinned to the exact spot, follow the part through explode, isolate and animation; public or private. |
| 6 | Section cuts | A shared cutting plane with filled cut faces (custom clipping shader). |
| 7 | Simulation | Assembly / disassembly animation and working animation (rotors, airflow; time of day for the house), in sync across phones from the server clock. |
| 8 | Attention awareness | View cones and "look here" pings; a FIND task tool for user studies. |
| 9 | Record & replay | Event-based session recording with the presenter's voice; replay in 3D with scrubbing and free camera; share as a file. |
| 10 | Interaction heatmap | Attention per part, per person or everyone, live or from a recording. |
| 11 | AI quiz + Reports | Teacher generates, reviews and sends a quiz; students answer on their phones; reports with charts, CSV / image export. |
| 12 | Reassembly challenge | A single-player puzzle: put the model back together. |
| 13 | Bandwidth-aware loading | A light preview first, the full model swapped in place in the background. |

Each phase has a build-and-test sheet in [`Docs/`](Docs/). The plan and every design decision are in
[`Docs/V3_FEATURE_PLAN.md`](Docs/V3_FEATURE_PLAN.md).

---

## Repository layout

```
Assets/
  Scripts/
    Core/          app state, input, persistence, AR placement
    Data/          model catalogue (Hugging Face manifest), model data
    Networking/    rooms, presenter sync, voice, control ownership, attention, event codes
    Space/         the 3D viewer: selection, isolate, explode, section cut, model loading
    AI/            part knowledge, AI client, chat, search, speech input, shared answers
    Notes/         notes attached to parts
    Simulation/    assembly and working animations, airflow
    Challenge/     reassembly game
    Recording/     session recorder, player, voice track, export
    Analytics/     interaction tracker, heatmap
    Quiz/          quiz session and generator
    Reports/       report storage and CSV
    UI/            every panel (built in code), HUD, drawers, toasts
    Editor/        Part Labeler, shader-inclusion build hook, model optimizer hook
  Shaders/         AuraSync/SectionClip (section cut), AuraSync/AttentionBeam (view cones)
  Photon/          PUN 2 and Photon Voice 2 (third-party)
  Scenes/          V1 (AR + menus), SpaceScene (3D viewer)
ai-server/         Cloudflare Worker: /ask, /find, /quiz, /transcribe (see its README)
Catalog/           models.json and the part knowledge files, as published to Hugging Face
Tools/             optimize-model.py (mobile + preview GLBs), gen-rsp.sh (offline compile check)
Docs/              feature plan, build notes and test sheets
```

---

## Getting started

1. **Unity 2022.3.62f3** with Android Build Support. Open the project folder; first import takes a
   while.
2. **Photon:** App IDs for PUN and Voice are in
   `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset`
   (*Window → Photon Unity Networking → Highlight Server Settings*).
3. **AI features** need the AI server (below) and a file the app reads at runtime, which is **not in
   the repository** (it holds the app key):

   `Assets/Resources/AuraSyncAI.json`
   ```json
   { "serverUrl": "https://<your-worker>.workers.dev", "appKey": "<the APP_KEY secret>" }
   ```
   Without it the app runs normally, and the AI buttons say the AI is not set up.
4. **Build:** *File → Build Settings* → Android → scenes `V1` (index 0) and `SpaceScene` (index 1).
   A build hook adds the glTF and AuraSync shaders to *Always Included Shaders* by itself.

### AI server (`ai-server/`)
A Cloudflare Worker that holds the AI keys, so no key ships in the APK. It uses free Gemini and Groq
models, in an order set in `wrangler.toml`.

```bash
cd ai-server
npx wrangler secret put GEMINI_API_KEY
npx wrangler secret put GROQ_API_KEY
npx wrangler secret put APP_KEY
npx --yes wrangler@4.148.0 deploy
```

### Adding a model
```bash
python Tools/optimize-model.py Turbine.glb --name "Turbine" --desc "Steam turbine" --manifest Catalog/models.json
python Tools/optimize-model.py Turbine_mobile.glb --preview --manifest Catalog/models.json
```
Upload `Turbine_mobile.glb`, `Turbine_preview.glb` and `Catalog/models.json` to the Hugging Face
dataset. Then label the parts in Unity (*Tools → AuraSync → Part Labeler*, in Play mode) and publish
the knowledge file the same way. Use a **new file name** whenever the parts change: phones cache
models by URL.

### Checking code without opening Unity
`Tools/gen-rsp.sh` builds a compiler response file from the project's `.csproj`, so the scripts can
be compile-checked with Unity's bundled Roslyn (see the comments in the script).

---

## Version control

This project is kept in **two** systems side by side:

- **Git / GitHub** (this repository): code, settings, docs, tools.
- **Unity Version Control** (Plastic SCM, workspace in `.plastic/`).

They ignore each other's data: `.plastic/` is in `.gitignore`, and `.git` is in `ignore.conf`.
`lock.conf` makes scenes, prefabs and binary assets exclusive check-outs in Unity Version Control.

**Never committed, in either system:** `Assets/Resources/AuraSyncAI.json` (app key),
`ai-server/.dev.vars` (API keys), `Library/`, `Temp/`, `UserSettings/`, builds. 3D models (`*.glb`)
live in the Hugging Face dataset, not in git.

---

## Status

V3 is complete: every phase built and passed its device tests (October 2026). Next: a UI refinement
pass across the whole app.
