# AuraSync V3 – Phase 4 (Voice and text search): build notes and test plan

The AI panel now has two tabs, **FIND** and **ASK**. FIND finds a part from its name, what it does,
or a spoken request ("show me the turbine"), then selects it on the model.

> **Status:** compiled clean (game and Editor code: 0 errors). Server: 33 of 33 tests pass, and the
> new `/transcribe` route is deployed and checked live with real speech (about 1 s, transcribed
> exactly). **Not yet run in Unity.**
>
> **Gate for the next phase:** every **P0** case in section 3 passes.

---

## 1. What was built

| File | What |
|---|---|
| `Scripts/UI/PartChatUI.Search.cs` | **New.** The FIND tab: search box, **MIC**, **GO**, results with ASK and ISOLATE. |
| `Scripts/UI/PartChatUI.cs` | FIND / ASK tabs in the panel header. The rail **AI** button opens ASK when a part is selected, FIND when nothing is. |
| `Scripts/AI/PartSearch.cs` | **New.** Search on the phone (instant, offline): typos, plurals and filler words ("show me the") are handled. Also the search log for the paper. |
| `Scripts/AI/SpeechInput.cs` | **New.** Records up to 5 s, stops by itself about a second after you stop talking, asks for the microphone permission on Android, never sends silence. |
| `Scripts/AI/AiClient.cs` | New calls: search by meaning (`/find`) and speech to text (`/transcribe`). |
| `Scripts/Networking/VoiceChatManager.cs` | Can lend its microphone to voice search for a few seconds and take it back. (Original saved in `backups/pre-phase2/VoiceChatManager.pre-phase4.cs`.) |
| `ai-server` | New `/transcribe` route: Groq Whisper, Gemini as backup. The part names go along as a hint, so technical words are heard right. Deployed. |

### How a search works

1. **As you type**, results come from the phone alone, instantly. Every word must match a part's
   name or other name; small typos and plurals are fine.
2. **GO (or Enter)**: if the phone is sure and there is one clear winner, it is **selected straight
   away**. If several fit equally ("turbine": casing or rotor?), you pick. If nothing fits by name
   ("the part that burns fuel"), the **AI searches by meaning**, and those results say *by meaning*.
3. **MIC**: say it. The words appear in the box and are searched as if you had pressed GO.

Checked on the real jet engine labels: *inlet guide vanes*, *guide vane*, *exhaust* and *where are the
brackets* are picked straight away; *turbine*, *fan*, *pipes* and *casing* list the candidates; the typo
*turbin* still finds both turbine parts; *the part that burns fuel* goes to the AI.

---

## 2. Before testing

- Unity recompiles by itself. Your PC needs a **microphone** for the voice cases (a headset or the
  laptop's own mic is fine).
- Windows may ask Unity for microphone access the first time. Allow it.

---

## 3. Tests

Jet Engine unless the case says House. *Rail AI* means the **AI** button on the left rail.

### 3.1 Opening and tabs

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-01 | P0 | Nothing selected. Tap rail **AI**. | The panel opens on **FIND** (lit). Search box, **MIC**, **GO**, and the hint *Type or say a part's name...*. The box is ready to type in. |
| S-02 | P0 | Select a part, then tap rail **AI**. | Opens on **ASK** with that part's chat (as in Phase 3). |
| S-03 | P0 | In the panel tap **FIND**, then **ASK**. | The pages switch; the lit tab follows. ASK shows the chat for the current selection (or the whole model). |
| S-04 | P0 | Tap **ASK AI** beside a selected part. | Always opens the **ASK** tab. |
| S-05 | P0 | Open the **House**, rail **AI**. | FIND says *This model has no part names to search. Use ASK...*; the box, MIC and GO are disabled. ASK works as before. |

### 3.2 Typing

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-10 | P0 | Type `inlet guide vanes`, press **GO**. | The part is **selected** straight away (highlight on the model, chip shows it). Status: *Selected: Inlet guide vanes...*. The panel's line points at it. |
| S-11 | P0 | Type `turbine` letter by letter. | Results appear as you type. At `turbine`: **Turbine casing** and **Turbine rotor**. |
| S-12 | P0 | Press **GO** on `turbine`. | Not auto-picked (two equal matches): *2 matches. Tap the one you mean.* Tap one: it is selected and its row is highlighted. |
| S-13 | P0 | Type `turbin` (typo). | Still finds both turbine parts. |
| S-14 | P0 | Type `the part that burns fuel`, press **GO**. | *Searching by meaning...*, then within about 1–2 s results tagged **by meaning** with a short reason (expect the turbine casing / combustor area). If exactly one comes back it is selected. |
| S-15 | P1 | Type `zzzz` and press GO. | *No part matches that...* No crash. |
| S-16 | P0 | On a result tap **ASK**. | The part is selected and the ASK tab opens with its chat. |
| S-17 | P0 | On a result tap **ISOLATE**. Then again. | The part is isolated; tapping again shows the whole model. |
| S-18 | P1 | Open PARTS while a Find result is selected. | The same part is lit in the drawer. |

### 3.3 Voice

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-30 | P0 | Tap **MIC** and say *"show me the exhaust"*. Stay quiet. | MIC turns red and reads **STOP**; status *Listening... 5s* counts down with a level meter that moves while you talk. About a second after you stop, it stops by itself: *Working out what you said...*, then `show me the exhaust` appears in the box and the exhaust nozzle is **selected**. |
| S-31 | P0 | MIC, say *"turbine"*, then tap **STOP** straight away. | Stops at once and carries on as S-30 (two matches listed). |
| S-32 | P0 | MIC, say nothing for 5 s. | *Didn't hear anything...* Nothing is sent. |
| S-33 | P0 | MIC, say *"the part that burns fuel"*. | Heard correctly, then searched by meaning as S-14. |
| S-34 | P1 | MIC, say a technical name: *"inlet guide vanes"*. | Heard correctly (the part names are sent along as a hint) and selected. |
| S-35 | P1 | MIC, then close the panel with **X** while listening. | Recording stops; nothing happens afterwards; no error. |
| S-36 | P0 | **phone** First MIC tap on a fresh install. | Android asks for microphone permission. Allow: listening starts. (Deny on a second install: *Microphone permission is off...*) |
| S-37 | P0 | **phone** S-30 and S-33 on the phone. | Work as in the Editor. |
| S-38 | P1 | Turn internet off, MIC, say something. | *No internet connection...* Typing still finds parts by name (offline). |

### 3.5 Voice in ASK, and the panel layout (added after your first Phase 4 test round)

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-40 | P0 | Open any part's ASK tab on the phone. | The header row (FIND, ASK, title, ISOLATE/SHOW ALL, X) is **one normal button-height row**, not a tall block. The facts card is fully readable (scroll it if long). |
| S-41 | P0 | On ASK tap **MIC** and say *"how fast does it spin?"* | MIC turns red (**STOP**); the question box's grey hint shows *Listening... 4s ||||*. When you stop talking: *Working out what you said...*, then your words appear as **your message** and the answer follows, **with no extra tap**, exactly like a typed question. |
| S-42 | P0 | Speak a follow-up with MIC (*"and what is it made of?"*). | Answered in context, like C-22. |
| S-43 | P1 | MIC on ASK, say nothing. | The hint shows *Didn't hear anything...* for a few seconds, then returns to *Ask your own question...*. Nothing is sent. |
| S-44 | P1 | MIC on ASK, then tap **FIND** while listening. | Listening stops; nothing is sent. |
| S-45 | P1 | While an answer is still coming, look at MIC and SEND. | Both disabled until the answer arrives. |

### 3.4 Back, model switch, paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| S-50 | P0 | With FIND open, Escape (or phone back). | The panel closes. |
| S-51 | P0 | Select a part with FIND, switch to the House and back. | No panel open; FIND starts empty. |
| S-52 | P1 | After some searches: context menu **V3 debug: AI chat and search stats**. | Adds a line: number of searches, % that ended with a part picked, median seconds to pick, how many used voice and how many needed the AI. **Keep this for the paper.** |
| S-53 | P0 | Console throughout. | No exceptions. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_3_TESTS.md` | **C-01 to C-47** P0s | The chat now lives on the ASK tab of the same panel. |
| `V3_PHASE_0_TESTS.md` | **F-20 to F-30** | Back stack. |

## 5. Multi-device cases

In `V3_PENDING_DEVICE_TESTS.md` under *From Phase 4*. The important one: voice search while in a room
with voice chat on.
