# AuraSync V3 – Pending device tests

Every multi-device case that a Track A phase could not run on one device. Nothing here was skipped:
each case was moved here instead of being left out of its phase's sheet.

**When:** at the device-testing checkpoint (`V3_FEATURE_PLAN.md` §3.3), **after** the V2 multiuser
tests (`V2_BUILD_NOTES.md` §4 and the multi-device sections of `V2_PARTS_DRAWER_TESTS.md`).
Run oldest phase first. Track B does not start until every P0 here passes.

Devices: **A** = presenter, **B** and **C** = viewers.

---

## From Phase 0 – Foundations

Phase 0 changed how parts move (exploded view and isolation now go through `PartPoseComposer`) and
added the back stack. These cases check the shared behaviour that depends on that code.

### Re-run from V2 (the code under them changed)

| Sheet | Cases | Why |
|---|---|---|
| `V2_PARTS_DRAWER_TESTS.md` §5.2 | **E-20 to E-28** | Explode sync now applies through the composer. |
| `V2_BUILD_NOTES.md` §4.5 | **P-01 to P-06** | Selection now publishes to the state bus on every change. |

> **E-21, E-21b and E-24 were rewritten on 2026-10-08.** You chose that a viewer may explode their
> own copy (it breaks following; Catch Up restores the presenter's explosion). The code already did
> this, so only the test sheet and its description changed. Run the rewritten versions.

### New cases

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| PD0-01 | P0 | A, B | B is FOLLOWING. B isolates a part. A moves the explode slider. | B's isolated part **stays centred** while A's explosion changes underneath it. B is still following. |
| PD0-02 | P0 | A, B | From PD0-01, B shows all. | B's model appears at **A's current** explode amount, every part in place. |
| PD0-03 | P0 | A, B | B explodes their own copy (breaks following), isolates a part, then taps **Catch Up**. | B's explosion snaps to A's; the isolated part stays centred through it. |
| PD0-04 | P1 | A, B | A explodes and isolates a part. | B sees A's explosion but **not** A's isolation (isolation is local, as in V2). |
| PD0-05 | P0 | A, B | B opens the room panel in the 3D view and presses the phone's back button. | The room panel closes. B stays in the room and in the 3D view. |
| PD0-06 | P1 | A, B | B opens PARTS. A switches model. | B's drawer closes, the new model loads; one more back press on B leaves to the model list (the stale drawer entry is gone). |
| PD0-07 | P1 | A + 4 viewers (or as many as you have) | All in a room. On A, check the top-right corner of the HUD with the player badges listed. | Badges, Share and the space for SETTINGS/REPORTS do not overlap. |

---

## From Phase 2 – Part knowledge

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| PD2-01 | P0 | A, B | A opens the **Jet Engine v2** in a room; B joins. On B, read the log (`adb logcat -s Unity`) for `[PartKnowledge]`. | B downloads the same knowledge file: `11 part(s) labelled`. |
| PD2-02 | P0 | A, B | A switches to the House. | B's log shows `whole-model knowledge only`. No stale engine labels. |
| PD2-03 | P1 | A, B | B had opened the **old** jet engine on an earlier day (cached). A opens v2. | B downloads `JetEngine_v2_mobile.glb` (new URL) and shows 11 parts, not the cached 10. |
| PD2-04 | P1 | B | On a phone, open the Jet Engine with mobile data, then again in airplane mode. | Second time: `(from the device cache ...)`. |

---

## From Phase 3 – AI part assistant

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| PD3-01 | P0 | A, B | B (viewer) opens the fan's chat and asks a question. Meanwhile A selects a different part. | B's chat stays on the fan and does not jump or close. B's ASK AI button is hidden while the chat is open. |
| PD3-02 | P0 | A, B | B has nothing selected; A selects the exhaust. B taps ASK AI. | B's chat opens on **A's** part (the presenter's pick is used when B has none of its own). |
| PD3-03 | P1 | A, B | A and B both chat at once. | Each phone has its own conversation; nothing appears on the other phone (sharing an answer is Phase 3b). |
| PD3-04 | P1 | A, B | A switches the model while B's chat is open. | B's chat closes; B's next chat is about the new model. |

---

## From Phase 4 – Voice and text search

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| PD4-01 | P0 | A, B | In a room with voice chat on (both MIC ON). B uses FIND's **MIC**. | B's search hears B. For those few seconds A does not hear B (B's voice chat is paused); afterwards A hears B again **without anyone touching anything**. |
| PD4-02 | P0 | A, B | A (presenter) finds a part with FIND. | B sees it highlighted in **gold**. |
| PD4-03 | P0 | A, B | B (viewer) finds a part. | Highlighted in **cyan** on B only; A sees nothing. |
| PD4-04 | P1 | A, B | B taps MIC, and while listening taps MIC ON/OFF in the top bar. | No crash. After the search, B's mic is in whatever state B last chose. |
| PD4-05 | P1 | A, B | B uses voice search during a room call (Photon Voice build). | Voice search records and transcribes normally. A does not hear B **while** B's search is recording (the mic is lent), then hears B again within ~1 s after. B's voice line shows `mic lent to voice search` during it. |

---

## Voice fix – Photon Voice 2 (2026-10-11)

Voice moved from the built-in mu-law path to Photon Voice 2 (Opus, call-mode mic, echo cancellation, error
correction). Problem it fixes: viewers' voices sometimes never reached the presenter; quality below WhatsApp.
Watch the on-screen voice line on every phone (`VOICE live | Opus | lvl .. | tx .. rx .. lost ..% | spk ../.. out ..`).
First run (2026-10-11, native call-mode mic): frames flowed both ways (`rx 300, lost 0%`) but nothing was audible. Capture moved to Unity's mic; re-run from VX-01.
**Test with phones in different rooms or with earphones** (see the same-room note in `V2_BUILD_NOTES.md`).

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| VX-01 | P0 | A, B, C | All join one room. On each phone check the log or voice line. | Log: `Using Photon Voice 2 (Opus)` then `Microphone added`. Voice line reaches `live` within ~5 s on every phone. |
| VX-02 | P0 | A, B, C | Each person in turn talks for 20 s while the others listen. Repeat the round **5 times**, leaving and rejoining the room between rounds. | Every speaker heard by everyone, every round. **No round where a viewer is unheard** (this is the original bug). |
| VX-03a | P0 | A, B | A talks; look at B's voice line. | B shows `spk 1/1` and `out` rising above ~0.05 while A talks, and B **hears** A from the loudspeaker at normal volume. |
| VX-03 | P0 | A, B | Talk normally back and forth like a phone call for 3 minutes. | Clear, natural voice; no robotic sound, clicks or gaps; delay similar to a WhatsApp call. |
| VX-04 | P0 | A, B | A on loudspeaker, B talks for 30 s. | B does not hear their own voice echoed back from A. |
| VX-05 | P1 | A, B | Fresh install on B. Join the room and leave the mic permission prompt open for 20 s while A talks; then allow. | B hears A **while the prompt is still open**. After allowing, A hears B within a few seconds. |
| VX-06 | P1 | A, B | B denies the mic permission. | B still hears A. B's voice line says `listening only - no mic permission`; B's HUD shows NO MIC. |
| VX-07 | P1 | A, B | B mutes, talks, unmutes, talks. Then B uses "mute incoming", A talks. | Muted: A hears nothing and B's speaking badge stays off. Mute incoming: B hears nothing; other phones unaffected. |
| VX-08 | P1 | A, B | A switches scene (AR <-> Space) while B talks. | B is still heard after the switch, without echo. |
| VX-09 | P1 | A, B | B turns Wi-Fi off for 10 s, then on (still in the PUN room or rejoining it). | Voice returns by itself within ~30 s; log shows `Reconnecting` if the voice connection had dropped. |
| VX-10 | P1 | A, B | B plugs in wired earphones (or Bluetooth) mid-call, then removes them. | Audio moves to the earphones and back to the loudspeaker; the call continues both ways. |
| VX-11 | P2 | A, B | B on mobile data, 2 min call. | Usable; `lost` stays in single digits on a decent signal. |


---

## Fixes 2026-10-12 – model switching, shared isolation, "show to everyone?"

**Model switching bug (root cause).** V1's `AuraSyncManagers` carries a PhotonView with scene id 1 and is kept
across scenes. Every return to the model list loaded a copy with the same id, and PUN destroyed the *older*
object on a duplicate id - the live one, with ZoneManager / NetworkManager / PersistenceManager on it. The
presenter stopped being presenter, so the next model they picked was replaced by the room's model and nobody
followed. Fix: `UserPresence` moves the kept view to its own id (998).

**New behaviour.**
- Presenter explodes (on letting go of the slider) or isolates / shows all → popup **"Show ... to everyone?"**
  ACCEPT → every viewer who is following gets it. DENY → presenter's phone only. Back button = Deny.
- Isolation is now shared like explosion (it was local-only before).
- A viewer's own explode / isolate stays on their phone and stops them following; **Catch Up** brings back the
  presenter's shared view, explosion **and isolation**.
- Each model remembers how it was left (explosion, isolation, selected part, position/rotation/zoom). The
  presenter going back to a model brings it back that way for everyone.

> V2 cases E-20 to E-28 assumed the presenter's explosion reaches viewers live. It now reaches them after the
> presenter lets go of the slider and taps ACCEPT. Read those cases with that in mind.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| MS-01 | P0 | A, B | A opens model 1, room created, B joins. A: Back → model list → model 2. | A opens **model 2** (not model 1) and still shows PRESENTER. B switches to model 2 by itself. |
| MS-02 | P0 | A, B | Repeat MS-01 three times between three models. | Every switch followed on both phones; A stays PRESENTER throughout. |
| MS-03 | P0 | A, B | On model 1, A explodes (Accept), isolates a part (Accept) and turns the model. A switches to model 2, then back to model 1. | Model 2 opens assembled and whole on both. Back on model 1, **both** phones show it exploded, isolated and turned as A left it. |
| MS-04 | P1 | A, B | On model 1, A explodes and taps **Deny**. Switch to model 2 and back. | A sees model 1 exploded again (own view remembered); B sees it as last shared (not exploded). |
| MS-05 | P1 | A, B, C | After MS-03, C joins the room. | C opens the current model in its shared state (explosion / isolation as everyone else). |
| SH-01 | P0 | A, B | A drags the explode slider and lets go. | Popup "Show this exploded view to everyone?". During the drag B does **not** move. |
| SH-02 | P0 | A, B | SH-01 → ACCEPT. | B's model moves apart to A's amount. |
| SH-03 | P0 | A, B | SH-01 → DENY. | Only A is exploded; B unchanged. |
| SH-04 | P0 | A, B | A selects a part, ISOLATE → ACCEPT. Then SHOW ALL → ACCEPT. | **A and B** both show only that part (centred, enlarged), and A's part stays selected after tapping ACCEPT; then both return to the whole model. (Re-test 2026-10-12: tapping ACCEPT used to fall through to the 3D view and undo A's own isolation.) |
| SH-05 | P0 | A, B | A isolates → DENY. | Only A isolated. B unchanged. |
| SH-06 | P1 | A, B | A drags the slider twice quickly without answering the first popup. | One popup (not two); ACCEPT shares the final amount. |
| SH-07 | P1 | A, B | A isolates (Accept), then taps a different part while isolated. | Popup again; ACCEPT moves B's isolation to the new part. |
| SH-08 | P1 | A | Popup open → press the phone's Back button. | Popup closes as DENY; Back did not leave the 3D view. |
| VW-01 | P0 | A, B | B isolates a part / explodes on their own. | Only B changes; A unaffected. B stops following (Catch Up button turns orange). |
| VW-02 | P0 | A, B | After VW-01, A shares an isolation + explosion (Accept). | B keeps their own view (not yanked). |
| VW-03 | P0 | A, B | B taps **Catch Up**. | B glides to A's view and takes A's shared explosion **and** isolation. |
| VW-04 | P1 | A, B | A has a shared isolation; B is following. A taps another part (popup opens) - look at B before A answers. | B's isolation stays put until A taps ACCEPT, then moves to the new part. |
