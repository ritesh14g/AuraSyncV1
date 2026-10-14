# AuraSync V3 – Phase 3 (AI part assistant): build notes and test plan

Select a part, tap **ASK AI** beside it, and a chat opens next to the part. It shows the part's facts
straight away, offers suggested questions, and answers your own questions with the AI. The
conversation carries on when you close and reopen it.

> **Status:** compiled clean (game and Editor code: 0 errors). Server change deployed and checked
> live (29 of 29 Worker tests pass). **Not yet run in Unity.**
>
> **Gate for the next phase:** every **P0** case in section 3 passes.

---

## 1. What was built

| File | What |
|---|---|
| `Scripts/UI/PartChatUI.cs` | **New.** The ASK AI button beside the selected part, the chat panel with its line to the part, suggestion chips, the question box, ISOLATE, and *Helpful? Yes / No* under each answer. |
| `Scripts/AI/AiClient.cs` | **New.** Talks to the AI server. Turns failures into plain messages (no internet, busy, too slow). Cleans up characters the app's font cannot show. |
| `Scripts/AI/PartChat.cs` | **New.** The conversations (one per part, plus one for the whole model) and a feedback log on the device for the paper's numbers. |
| `Scripts/AI/PartNames.cs` | **New.** One place that decides a part's display name: the reviewed label, written consistently ("Fan rotor", "Inlet guide vanes"). |
| `Assets/Resources/AuraSyncAI.json` | **New.** The server address and app key. **Listed in `.gitignore`**, so the key stays out of the repository. |
| `ModelPartsDrawerUI.cs` | Rows show the **label** with the file name in grey after it; the filter searches labels and other names too. |
| `ModelPartSelector.cs` | The selected-part chip shows the label ("You: Fan rotor"). |
| `PartKnowledge.cs`, `SpaceSceneManager.cs`, `SpaceSessionHUD.Slots.cs` | Model name for chat titles; creates the chat; new debug item **V3 debug: AI chat stats**. |
| `ai-server` | A whole-model question no longer arrives with an empty "selected part" (deployed). |

### What you will notice

- **An "AI" button on the left rail** (the first real rail button). With a part selected it opens that part's chat; with nothing selected it opens the chat about the **whole model**.
- **ASK AI**: a purple button that appears beside whichever part is selected.
- **The parts drawer and the chip now use names**: "Fan rotor  `Object_6.1`" instead of just "Object_6.1". Typing *fan* in the filter finds it.
- **The House** has no part labels (whole model only), so on the House every chat is about the house as a whole.
- **Where the chat sits:** on the opposite half of the screen from the part (bottom if the part is high, top if it is low; left or right in landscape), with a thin purple line to the part. It only switches sides when the part moves well past the middle, so it does not jump about as you turn the model.

---

## 2. Before testing

1. Let Unity recompile (it will pick up `Assets/Resources/AuraSyncAI.json` automatically).
2. The Worker must be deployed (it is) and your PC online for the AI cases. Section 3.4 covers offline.

---

## 3. Tests

Editor with the mouse unless marked **phone**. Jet Engine unless the case says House.

### 3.1 Opening and the facts card

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-01 | P0 | Open the Jet Engine. Tap the fan. | A purple **ASK AI** button appears just right of the fan. The chip reads **You: Fan rotor**. |
| C-02 | P0 | Tap **ASK AI**. | The chat opens on the **other half** of the screen from the fan, with a thin purple line to it. Title **Fan rotor**. The first bubble (teal) shows instantly: what it does, **Material**, **Touches** (other parts by name), **Moves: spins...** |
| C-03 | P0 | Look under the facts card. | Three chips: *Explain it simply*, *How fast does it spin?*, *What happens if it fails?* |
| C-04 | P0 | Rotate the model so the fan moves across the screen. | The line follows the fan. The panel only moves to the other half once the fan is well past the middle. |
| C-05 | P0 | Close with **X**. Open it again on the same part. | Same conversation, nothing lost. |
| C-06 | P0 | Tap the left-rail **AI** with **nothing** selected. | Chat titled **Jet Engine** with the model's summary. Chips: *How does it work?* and so on. No line (it is the whole model). |
| C-07 | P0 | Open the **House**. Tap any chunk, then ASK AI (or the rail AI). | The chat is about **the House as a whole** (title House, its summary), never about a chunk. |
| C-08 | P1 | Select the `Object_6` **group** row in PARTS, then ASK AI. | Title like "Fan rotor and Turbine rotor"; the card says *A group of parts. It holds: ...* |
| C-09 | P1 | Check `Object_5`, `Object_10` and a few others. | Every part's card shows its own facts; titles are written consistently (capital first letter only). |

### 3.2 Asking

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-20 | P0 | Tap the chip **Explain it simply**. | Your question appears on the right (blue); an italic *Thinking...* bubble appears; within about 1–3 s a grey answer replaces it, about the **fan**. New chips appear. |
| C-21 | P0 | Type your own question (for example *Why titanium?*) and tap **SEND** (or press Enter). | Same as C-20. The box clears. |
| C-22 | P0 | Ask a follow-up that only makes sense with context: *And why not aluminium?* | The answer understands "it" means the fan and continues the conversation. |
| C-23 | P0 | While *Thinking...* shows, try to send another question. | The box and SEND are disabled; the only chip is **Stop waiting**. |
| C-24 | P1 | Tap **Stop waiting**. | *Stopped...* message, and a **Try again** chip that asks the same question again. |
| C-25 | P0 | Ask something unrelated (*Who won the World Cup?*). | A polite answer that it can only help with this model, plus a related suggestion. |
| C-26 | P1 | Ask a question whose answer is long. | The list scrolls; the newest message is in view. Drag the list: it scrolls and **the model does not rotate**. |
| C-27 | P1 | Look at answers for odd characters. | No empty boxes and no stray `**`: dashes and quotes show normally. |
| C-28 | P0 | Under an AI answer tap **Yes**, then **No**, then **No** again. | The tapped one is highlighted; tapping the highlighted one again clears it. |

### 3.3 Moving between parts, isolate, back

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-40 | P0 | With the fan's chat open, tap another part (for example the exhaust). | The chat switches to that part's conversation (title changes, its own facts card). |
| C-41 | P0 | Go back to the fan (tap it). | The fan's earlier conversation is still there. |
| C-42 | P0 | Ask a question, then close the chat before the answer arrives. Reopen. | The answer is there. |
| C-43 | P0 | In the chat header tap **ISOLATE**. | The part is isolated (centred, enlarged); the button reads **SHOW ALL**; the chat stays open. **SHOW ALL** brings the model back. |
| C-44 | P0 | With the chat open press **Escape** (or **phone** back). Then again. | First: the chat closes. Second: as before (closes the next panel, or leaves). |
| C-45 | P1 | Open PARTS and the chat together. Escape twice. | The chat closes first, then the drawer. |
| C-46 | P0 | Switch to the House and back. | No chat open; the jet engine's earlier conversations are gone (new model, fresh start). |
| C-47 | P1 | Tap empty space while the chat is open. | The selection clears; the chat stays on its part. |

### 3.4 Failures

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-60 | P0 | Turn the PC's internet off. Open a part's chat. | The facts card and chips still appear (they need no AI). |
| C-61 | P0 | Still offline, ask a question. | Red bubble: *No internet connection. The part facts above work offline...* and a **Try again** chip. Turn internet on, tap Try again: it answers. |
| C-62 | P1 | Rename `Assets/Resources/AuraSyncAI.json` (add `.off`), replay, ask. | *AI chat is not set up in this version of the app.* Rename it back afterwards. |
| C-63 | P0 | Console throughout. | No exceptions. |

### 3.5 Drawer and chip names

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-80 | P0 | Open PARTS on the Jet Engine. | Rows read like **Fan rotor** `Object_6.1` (file name grey). For a moment right after loading they may show only file names; they switch to labels when the knowledge arrives. |
| C-81 | P0 | Type **fan** in the filter. | Fan rotor and Fan casing appear (matched by label), not only rows whose file name contains "fan". |
| C-82 | P1 | Type a label's other name (see the part's "Other names" in the Part Labeler). | That part is found. |
| C-83 | P1 | Open PARTS on the House. | Rows show the file names only (the House has no part labels). |

### 3.6 Phone and paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| C-90 | P0 | **phone** Do C-01, C-02, C-20, C-21. | The keyboard opens for the question box; SEND (or the keyboard's Done) asks; the answer arrives. The panel and line are readable on the phone's screen. |
| C-91 | P1 | **phone** Rotate the phone to landscape with the chat open. | The panel moves to the side opposite the part. |
| C-92 | P1 | After some questions, context menu **V3 debug: AI chat stats**. | One line: number of answers, median and slowest answer time, which provider answered, and the % rated helpful. **Keep this for the paper.** The file it reads is `AiFeedback.jsonl` on the device. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_0_TESTS.md` | **F-20 to F-30** (back stack) | The chat is a new panel on the stack. |
| `V2_PARTS_DRAWER_TESTS.md` | **D-08 to D-27** | Drawer rows and filter changed. |
| `V3_PHASE_2_TESTS.md` | **K-01, K-02** | Part knowledge loading is now used for real. |

## 5. Multi-device cases

In `V3_PENDING_DEVICE_TESTS.md` under *From Phase 3*.
