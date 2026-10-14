# AuraSync V3 – Phase 11 (AI quiz + Reports): build notes and test plan

**Teacher (presenter): Settings → AI QUIZ.**
1. Choose what the questions are about (the whole model or the selected part), how many (5 / 10
   / 15), the difficulty, and a time limit.
2. **GENERATE**, then review the questions: **EDIT**, **NEW ONE** (an AI replacement) or
   **DELETE** any of them.
3. **SEND**. A live panel shows each student's progress; **END QUIZ** stops it early.
4. When the quiz is over, a **report** is saved on the teacher's phone.

**Student:** a banner reads *"New quiz from Asha. Tap to start."* Questions come one at a time, the
model stays visible above them, and a question about a part lights that part. At the end the
student sees their score and every right answer, with why.

**REPORTS** (top-right corner, and on the model list) has a red count of reports not opened yet.
Each quiz report holds:
- the class average and a bar for each student;
- correct % per question and the weak parts;
- attention against score per part, from Phase 10.

**EXPORT CSV** and **EXPORT IMAGE** go through the share sheet.

> **Status:** compiled clean (0 errors). The AI server's `/quiz` was called live (2026-10-14): a
> valid question came back in about 10 s. **Not yet run in Unity.**
>
> **Gate:** every **P0** case in section 3 passes.

---

## 1. Decisions (defaults; nothing was asked)

- **Who:** the presenter only, in a room (D6: alone there is nobody to send it to).
- **Questions** come from the part knowledge file through the AI server's `/quiz` (Gemini first,
  Groq as backup). *About the selected part* uses that part and the parts it connects to. The
  House has whole-model questions only.
- **NEW ONE** asks for one replacement question on the same part.
- The quiz travels as a **room property**: a student who **joins late can still take it**. Answers
  are hidden from students until the end.
- Each answer goes to the teacher **as it is given** (event 29). A student who leaves still counts,
  with what they answered (*"left"*).
- **Ends** when everyone present has finished, when the time runs out, or on END QUIZ (also: the
  teacher switches model or leaves).
- **Students see no marks until the end**, then their score and each question with the right
  answer and the explanation.
- **The part highlight** for a question shows only on that student's phone (never shared, even if
  they hold Highlight).
- **Reports** are saved on the phone that made them (`Reports/<id>.json`). They open with no room and
  no model. **REPORTS** in the 3D view shows for the presenter (or alone), or for anyone with a
  report on their phone. It is also always on the model list.
- **EXPORT IMAGE** captures the report as it is on screen: scroll first to choose what it shows.
  **EXPORT CSV** has every number.
- **Question quality** for the paper: how many AI questions the teacher sent untouched
  (`QuizLog.jsonl`).
- The corner row's buttons are now 100 px wide so REC, SETTINGS and REPORTS fit together.

## 2. What was built

| File | What |
|---|---|
| `Quiz/QuizSession.cs` | **New.** The quiz in the room, answers, the end, the report; `QuizLog`. |
| `Quiz/QuizGenerator.cs` | **New.** The `/quiz` request from the part knowledge. |
| `AI/AiClient.cs` | `Quiz()` and the question types. |
| `UI/QuizTeacherUI.cs` | **New.** Setup, generating, review (edit / new one / delete), live, results; the edit box. |
| `UI/QuizStudentUI.cs` | **New.** Banner, one question at a time, results. |
| `Reports/ReportStore.cs` | **New.** Reports on disk, unseen count, CSV. |
| `UI/ReportsUI.cs` | **New.** List, quiz and heatmap reports, bar charts (`SimpleBarChart`), export; REPORTS buttons. |
| `UI/ModelRepositoryUI.cs` | REPORTS on the model list (made in code; the scene is untouched). |
| `Space/ModelPartSelector.cs` | `ShowLocalHighlight` for quiz questions. |
| `Networking/EventCodes.cs` | `RoomKeys.Quiz`; event 28 noted unused (room property instead). |

**Setup in Unity:** none. *(If GENERATE says the server does not know `/quiz`, redeploy the AI
server: `npx --yes wrangler@4.148.0 deploy` in `ai-server`.)*

---

## 3. Tests

Jet Engine. 2–3 devices: A teacher (presenter), B and C students.

### 3.1 Making the quiz

| ID | Pri | Steps | Expected |
|---|---|---|---|
| Q-01 | P0 | Alone: Settings. | No AI QUIZ. In a room as presenter: **AI QUIZ** under CLASS TOOLS. B (viewer) has no Settings. |
| Q-02 | P0 | A selects the fan rotor → AI QUIZ. | The panel shows: *About*: WHOLE MODEL / SELECTED: FAN ROTOR; *Questions* 5/10/15; *Difficulty*; *Time limit* NONE/2/5/10 MIN; GENERATE. |
| Q-03 | P0 | WHOLE MODEL, 5, MEDIUM, NONE → GENERATE. | *"Writing 5 questions..."*, then the review: 5 questions, each with A–D, the right one green *(right)*, the part. |
| Q-04 | P0 | EDIT on question 2: change the wording, make C the right answer, SAVE. | It shows the new text, C marked right, *(edited)*. |
| Q-05 | P0 | NEW ONE on question 3. DELETE question 5. | Q3 is replaced (*(new)*); 4 questions left. |
| Q-06 | P1 | SELECTED: FAN ROTOR, 10, HARD → GENERATE. | Questions about the fan rotor and the parts it connects to. |
| Q-07 | P1 | Turn the phone offline, GENERATE. | An error under GENERATE; nothing breaks. |

### 3.2 Taking the quiz

| ID | Pri | Steps | Expected |
|---|---|---|---|
| Q-10 | P0 | A: SEND TO 2 STUDENTS (2 MIN limit). | A: *QUIZ RUNNING*, *2:00 left*, *B - 0 of 4*, *C - 0 of 4*. B, C: banner *"New quiz from A · 4 questions · 1:59 / Tap to start"*. |
| Q-11 | P0 | B taps the banner and answers. | One question at a time at the bottom, the model visible above. A question about a part **lights that part on B only** (A and C see nothing). A's panel counts B's answers live. |
| Q-12 | P0 | B answers all. | B: *"All answered. Waiting for the quiz to end…"*. A: *B - finished*. |
| Q-13 | P0 | C answers all. | The quiz ends by itself (*everyone finished*). B and C: **"Your score: x / 4"** and every question with their answer (green right / red wrong), the right answer and why. A: *QUIZ OVER*, the class average and each score, *Saved to REPORTS*. |
| Q-14 | P0 | New quiz; A taps **END QUIZ** after B answered 2. | B and C see their results at once; B's 2 answers are marked. |
| Q-15 | P0 | New quiz with a 2 MIN limit; nobody finishes. | At 0:00 it ends (*time up*). |
| Q-16 | P0 | New quiz; **D joins** while it runs. | D gets the banner and can take it. |
| Q-17 | P1 | New quiz; B leaves the room halfway. | A: *B … (left)*. The quiz ends when C finishes. The report lists B with their answers, *"left"*. |
| Q-18 | P1 | New quiz; A leaves the room. | B, C: *"The teacher left, so the quiz stopped."* CLOSE. |
| Q-19 | P1 | B presses Back during the quiz. | *"Answer the questions first…"* (the quiz stays). |

### 3.3 Reports

| ID | Pri | Steps | Expected |
|---|---|---|---|
| Q-20 | P0 | After Q-13: look at A's REPORTS button. | A red **1** (more after more quizzes). |
| Q-21 | P0 | REPORTS → the quiz (or OPEN REPORT on the results). | *Class average NN%*; SCORE PER STUDENT bars; CORRECT PER QUESTION bars (red to green); WEAK PARTS; ATTENTION VS SCORE PER PART (orange attention, green right %) if the heatmap counted anything. The red count drops. |
| Q-22 | P0 | Leave the room, go back to the **model list**: **REPORTS** (top right of the list). | The reports open with no model and no room. |
| Q-23 | P0 | Close the app completely, reopen, REPORTS. | Still there. |
| Q-24 | P0 | **Phone:** EXPORT CSV, then EXPORT IMAGE. | The share sheet each time. The CSV opens in a spreadsheet with every number. The PNG is the report as on screen. |
| Q-25 | P1 | DELETE a report (twice). | Gone. |

### 3.4 Paper numbers

| ID | Pri | Steps | Expected |
|---|---|---|---|
| Q-30 | P1 | Stats item (Editor) after some quizzes. | *"Quiz: N sent; x of y AI questions kept unedited (z%)"*. |
| Q-31 | P1 | A quiz report's CSV beside a heatmap of the same session. | Attention per part next to missed % per part: the paper's learning-analytics table. |

---

## 4. Regression

| Sheet | Cases | Why |
|---|---|---|
| `V3_PHASE_3_TESTS.md` | **C-01** | The AI client gained `/quiz`. |
| `V3_PHASE_1_TESTS.md` | **G-01** | Corner row sizes. |
| `V2_PARTS_DRAWER_TESTS.md` | open the model list | REPORTS added to the model list. |

## 5. Paper metrics

- **Quiz score per part against attention per part** (the report's last section and its CSV).
- **Question quality:** the share of AI questions sent unedited (`QuizLog.jsonl`, stats line).
