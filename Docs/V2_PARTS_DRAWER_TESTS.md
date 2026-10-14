# AuraSync V2 — Parts drawer, exploded view and item sync: build notes and test plan

Three features, in the order they were built:

| Section | Feature | Status |
|---|---|---|
| 2, 3 | **Model Parts drawer** (`MULTIUSER_COLLABORATION_SPEC.md` §F) | single-device tests passed |
| 5 | **Exploded view** (spec §D) | untested |
| 6 | **AR edit and delete sync** | untested |

With these, every item on the spec list has code behind it. What remains is verification.

> **Status:** written and **compiled clean** against this project's own references (Unity
> 2022.3.62f3's Roslyn, all 106 `Assembly-CSharp` sources, 327 references: 0 errors, 0 warnings in
> the new files). **Not yet run in Unity and not yet seen on a screen.** Everything below section 3
> is still to do.

---

## 1. What was built

| File | Change |
|---|---|
| `Scripts/UI/ModelPartsDrawerUI.cs` | **New.** The drawer: a **PARTS** button, a scrolling list of the model's parts, a filter box, and a CLEAR / X header. Built procedurally, like the rest of the Space HUD. |
| `Scripts/Space/ModelPartCatalog.cs` | **New.** Flattens the loaded model's hierarchy into a list of pickable parts. Skips empty transforms, keeps hierarchy order, indents by depth, caps at 400. |
| `Scripts/Space/ModelPartSelector.cs` | Added `SelectByPath`, `ClearSelection`, `PresenterPath` / `LocalPath`, `IsPresenting`. Tapping the model now routes through the same `SelectByPath` the drawer calls, so there is one selection rule, not two. |
| `Scripts/Space/ModelRaycaster.cs` | Added `TryGetLocalBounds`, which boxes a part **and everything under it**. |
| `Scripts/Space/SpaceSceneManager.cs` | Creates the drawer alongside the other Space singletons, and re-catalogues the parts each time a model finishes loading. |

### Decisions worth knowing before you test

- **Picking a row is exactly a tap on the part.** Both go through `ModelPartSelector.SelectByPath`,
  so the presenter's pick is shared with the room in **gold**, anyone else's stays on their own
  device in **cyan**, and picking what is already selected **clears** it. No new sync path was
  added, so nothing that already worked can be broken by the drawer.
- **Groups are now highlighted too.** Previously a part with no mesh of its own (most of a glTF
  hierarchy is grouping nodes) showed its name and no box. A list makes those reachable by name, so
  `TryGetLocalBounds` now boxes a group around the meshes inside it. **This also changes 3D tapping**
  — it is the one existing behaviour this work touches, so P-01 to P-06 in `V2_BUILD_NOTES.md` §4.5
  are worth re-running.
- **No dark backdrop.** The point of the list is to watch the model highlight as you move down it,
  so the drawer does not dim or cover the model, and it does **not** close when you tap away — only
  the **X** or the **PARTS** button close it. This is deliberately unlike the ROOM panel.
- **It sits on the left**, clear of the player badges on the right. If it lands on one of
  SpaceScene's own controls on your screen, both positions are adjustable: add a `ModelPartsDrawerUI`
  component to a GameObject in SpaceScene yourself and set *Button Anchor Min/Max* and *Panel Anchor
  Min/Max*. The runtime one is only created when the scene has none.
- **Caps.** 400 parts catalogued, 150 rows built at a time. Past that the status line tells you, and
  the filter box is the way into a big model. Thousands of rows would stutter on a phone and nobody
  could find anything in them.

---

## 2. Single-device tests — run these first

**All of these work in the Unity Editor with a mouse**, which is the quickest way through them; the
ones marked **touch** need a phone or a touchscreen. Drag = hold and move more than ~24 px.

No room is needed: solo, every pick is a local (cyan) one.

### 2.1 Appearing and opening

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-01 | P0 | Open SpaceScene with a model, and watch while it loads. | **No PARTS button** during loading. It appears once the model is on screen. |
| D-02 | P0 | Tap **PARTS**. | The drawer opens on the left. Header reads `MODEL PARTS`, with `CLEAR` and `X`. The button's label becomes **CLOSE**. |
| D-03 | P0 | Tap **CLOSE**. | The drawer closes; the label returns to **PARTS**. |
| D-04 | P0 | Tap the **X** in the drawer header. | The drawer closes. The label returns to **PARTS**. |
| D-05 | P1 | Open and close five times. | Works every time. No stuck panel, no doubled panel. |
| D-06 | P1 | With the drawer open, tap the model (not the drawer). | The drawer **stays open** — tapping away does not dismiss it (unlike the ROOM panel). |
| D-07 | P0 | Look at the PARTS button and the open drawer against SpaceScene's own UI (Back, model name, loading overlay) and the corner ROOM button. | Nothing overlaps or is unreachable. If it does, see *Decisions* above for the anchors to change. |

### 2.2 The list

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-08 | P0 | Open the drawer on a model with several named parts. | One row per part, in the model's own order. The status line at the bottom reads `<n> parts`. |
| D-09 | P1 | Compare the rows against the model's hierarchy in the Editor's Hierarchy window. | The same parts, in the same order, indented to match the nesting. Empty transforms with nothing visible under them are **not** listed. |
| D-10 | P1 | Look at rows that hold a mesh versus rows that only group others. | Group rows are **dimmer**; mesh rows are white. |
| D-11 | P1 | Open a model whose parts have no names (or odd ones). | Unnamed parts read `(unnamed)`. Long names are cut with `...` rather than overflowing. |
| D-12 | P2 | Open a model with no separate parts at all (a single mesh). | Either one row, or the status line reads `This model reports no named parts.` No empty broken panel. |
| D-13 | P0 | **touch** Drag up and down **on the list**. | The list scrolls. **The model does not rotate.** |
| D-14 | P0 | **touch** Drag **on the model**, beside the drawer. | The model rotates as usual. |

### 2.3 Picking a part

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-15 | P0 | Tap a row that holds a mesh. | A **cyan** translucent box appears **on that part** on the model. The row turns cyan and bold. The chip under the top bar reads `You: <part name>`. |
| D-16 | P0 | Tap the **same row** again. | The highlight and the chip clear; the row returns to normal. |
| D-17 | P0 | Tap a different row. | The highlight **moves**. Only ever one cyan box at a time. |
| D-18 | P0 | Tap a **group** row (a dimmer one with children). | The box encloses **the whole group**, not one child and not nothing. This is the new behaviour — worth a careful look. |
| D-19 | P0 | With something selected, tap **CLEAR**. | The highlight and the chip clear. Tapping CLEAR again does nothing bad. |
| D-20 | P0 | Rotate and zoom the model, then tap a row. | The box lands on the **correct part**, wherever it now is on screen. |
| D-21 | P1 | Tap a part **in the 3D view** while the drawer is open. | That part's **row lights up**, and the list **scrolls to bring it into view**. |
| D-22 | P1 | Tap empty space beside the model, in the 3D view. | The selection clears, and no row is lit. |
| D-23 | P1 | Select a row, close the drawer, reopen it. | The same row is still lit, and the box is still on the model. |

### 2.4 The filter

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-24 | P0 | Type part of a part's name into **Filter parts...**. | The list narrows as you type. Status reads `<x> of <n> match`. Case does not matter. |
| D-25 | P1 | Type something no part is called (`zzzz`). | Status reads `No part matches "zzzz".` Empty list, no error. |
| D-26 | P0 | Clear the filter box. | The whole list comes back, indentation and all. |
| D-27 | P1 | Filter down to one row, then tap it. | It selects and highlights normally, exactly as from the full list. |
| D-28 | P2 | **touch** Tap the filter box. | The on-screen keyboard opens over the drawer. Dismissing it leaves the drawer working and the list where it was. |
| D-29 | P2 | While filtering, look at indentation. | Matches are flush left, not indented — by design, since matches come from all over the tree. |

### 2.5 Model switching, heavy models, hygiene

| ID | Pri | Steps | Expected |
|---|---|---|---|
| D-30 | P0 | Select a part, then go Back and open a **different** model. | The drawer is **closed**, the list is rebuilt for the new model, and the old highlight and chip are gone. |
| D-31 | P0 | Open the drawer on the **heavy** model (for example `JetEngine.glb`). | It opens without the app hanging. The log line `[PartsDrawer] Catalogued <n> parts` matches the status line. |
| D-32 | P1 | On a model with more than 400 parts, read the status line. | It ends with `(+<n> not listed)`. On more than 150 matching rows: `showing 150, filter to narrow`. Filtering still reaches the rest. |
| D-33 | P1 | Scroll and pick through a long list for a minute. | Stays responsive. No frame-rate collapse. |
| D-34 | P0 | Watch the Console (or `adb logcat -s Unity`) through all of the above. | No `NullReferenceException`, no `MissingReferenceException`, no TMP input-field errors. |
| D-35 | P1 | Re-run **P-01 to P-08** from `V2_BUILD_NOTES.md` §4.5 (tap-to-highlight in the 3D view). | Still as before — except that tapping a grouping node now draws a box where it used to draw none. |

---

## 3. Multi-device tests — for when devices are available

Same roles as `V2_BUILD_NOTES.md` §4: **A** creates the room and is the presenter, **B** and **C**
join as viewers. Gold = the room's (presenter's) pick, cyan = your own.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| D-40 | P0 | A, B | A opens the drawer and picks a part. | **Gold** box on that part on **both** devices. B's chip reads `Presenter: <name>`. B's row for it is gold. |
| D-41 | P0 | A, B | A picks the **same row** again. | Clears on both. |
| D-42 | P0 | A, B | B (viewer) opens the drawer and picks a **different** part. | **Cyan** on B only. A sees nothing new. Both boxes are visible on B (gold + cyan), and both rows are coloured. |
| D-43 | P0 | A, B | A picks a part from the drawer; B then taps the **same part in the 3D view**. | Consistent: one gold box, one cyan box, on the same part on B. No flicker, no fight. |
| D-44 | P0 | A, B | A picks a **group** row. | The group box appears on B too, around the same meshes. |
| D-45 | P0 | A, B | A taps **CLEAR**. | Clears on both. |
| D-46 | P1 | A, B | B taps **CLEAR**. | Clears **only B's own** cyan. A's gold stays on both. |
| D-47 | P0 | A, B, C | A has a part selected from the drawer. C joins afterwards. | C arrives with the gold box already on, and that row gold when C opens the drawer. |
| D-48 | P0 | A, B | **B is FOLLOWING.** B opens the drawer and picks several rows. | **B is still following** — the model keeps mirroring A, the button still says FOLLOWING. Picking from the drawer must never break follow. |
| D-49 | P0 | A, B | **B is FOLLOWING.** B drags **on the list** to scroll it. | B still follows. Scrolling the list is not a drag on the model. |
| D-50 | P0 | A, B | A hands presenter to B (double-tap B's badge). B then picks a row. | **B's** picks now go gold for everyone. **A's** picks no longer broadcast — A's own picks are cyan and local. |
| D-51 | P1 | A, B | A switches to a different model while B is in the room with the drawer open. | Both drawers close and rebuild for the new model. No stale rows, no stale highlight. |
| D-52 | P1 | A, B | B scrolls down a long list. A then changes selection. | B's list **does not jump**. The gold row changes where it is, but the scroll position is left alone. |
| D-53 | P1 | A, B | A picks a row while B is not in `SpaceScene` yet, then B joins and opens the drawer. | B's gold row matches A's selection. |
| D-54 | P2 | A, B | A picks rows rapidly, twenty times in a few seconds. | B keeps up and ends on the same part. No growing lag, no leaked highlight boxes (check B's Hierarchy for stacked `PartHighlight` objects). |
| D-55 | P2 | A, B | A leaves the room while a part is selected. | B's gold clears or is taken over cleanly by whoever becomes presenter. No orphan gold box. |

---

## 4. If something fails

Record it the way `V2_BUILD_NOTES.md` §4.10 asks: test ID, which device, the log from **all**
devices, and whether it repeats. For this feature the log lines worth watching are:

| Tag | Meaning |
|---|---|
| `[PartsDrawer]` | how many parts were catalogued, and how many fell past the 400 cap |
| `[SpaceSceneManager]` | model load and cache, which drives the re-catalogue |

Two things to suspect first if picking misbehaves:

1. **The wrong part highlights** — the part path (`ModelRaycaster.GetPartPath`) is a child-index
   path, so it only means the same thing on two devices if both built the **same** hierarchy from the
   same GLB. Confirm both devices really loaded the same file.
2. **A group's box looks wrong** — that is `TryGetLocalBounds`. Note the part's name and whether its
   children are rotated or scaled; that is where this would show up first.

---

## 5. Exploded view

An **EXPLODE** slider at the top of the parts drawer. Each part slides outward along the line from
the model's centre through its own centre, by a distance scaled to the model's size, so one slider
behaves the same on a house and on a jet engine.

**The rule to keep in mind while testing (decided 2026-10-08):** in a room, **only the presenter's
explosion is shared**. A viewer may still move the slider, but that explodes **their own copy** and
reaches nobody else, exactly like rotating or zooming. Doing so breaks them out of following the
presenter, and **Catch Up** brings back the presenter's explosion along with the presenter's view.
Solo, it is yours.

New: `Scripts/Space/ModelExploder.cs`. The amount travels as a Photon room property, the same way
the presenter's selected part does.

### 5.1 Single device

Runs in the Editor. No room needed — solo, you are free to explode.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| E-01 | P0 | Open a model, open the drawer. | An **EXPLODE** row sits above the filter box: a label and a slider. |
| E-02 | P0 | Look at the label before touching anything. | `EXPLODE 0%`, slider at the far left, model assembled. |
| E-03 | P0 | Drag the slider slowly to the right. | Parts slide **outward from the centre**, smoothly, following your finger. Nothing jumps or flips. |
| E-04 | P1 | Watch the label while dragging. | It tracks the slider: `EXPLODE 45%`, and so on. |
| E-05 | P0 | Drag all the way right, then all the way back to 0. | The model returns to **exactly** its original shape. No part left behind, no drift, no gaps. Worth repeating three or four times — drift would accumulate. |
| E-06 | P0 | Explode halfway, then rotate, pinch-zoom and two-finger pan. | All behave exactly as when assembled. The parts stay spread apart correctly as the model turns; the explosion does not "swim" or re-aim. |
| E-07 | P0 | Explode halfway, then tap a part in the 3D view. | The **part you tapped** highlights — not the one that used to be in that spot. |
| E-08 | P0 | Explode halfway, then pick a row from the list. | The wireframe box lands on the right part, **in its moved position**. |
| E-09 | P1 | Explode, then use the drawer's CLEAR. | The highlight clears; the explosion is unaffected. The two are independent. |
| E-10 | P0 | Explode, go BACK, open a different model. | The new model is **assembled**, slider back at 0. |
| E-11 | P1 | Open a model that is one single mesh. | The label reads `EXPLODE n/a` and the slider does nothing. No error. |
| E-12 | P1 | Explode, close the drawer, reopen it. | The slider still shows the same value and the model is still exploded. |
| E-13 | P1 | Open the heavy model, explode slowly across the full range. | Stays responsive. No frame-rate collapse, no stall. |
| E-14 | P2 | Read the Console after a model loads. | `[ModelExploder] <n> movable part(s)` — roughly in line with the part count the drawer reports. |
| E-15 | P2 | Explode a model whose parts are nested several levels deep. | Parts separate sensibly rather than a handful flying far while the rest stay put. |
| E-16 | P0 | Watch the Console through all of the above. | No `NullReferenceException`, no `MissingReferenceException`. |

### 5.2 Multi-device

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| E-20 | P0 | A, B | A (presenter) drags the slider to about half. | **B's model explodes to the same amount**, within about half a second. |
| E-21 | P0 | A, B | B (viewer) moves the EXPLODE slider. | **B's** model explodes; **A's does not change**. B drops out of FOLLOWING (the button reads CATCH UP). |
| E-21b | P0 | A, B | Continuing E-21: B taps **Catch Up**. | B's explosion snaps to **A's current amount** and B is following again. |
| E-22 | P0 | A, B, C | A explodes. C joins afterwards and opens a model. | C **arrives already exploded**, matching A. |
| E-23 | P0 | A, B | A slides back to 0. | B's model reassembles. |
| E-24 | P0 | A, B | A hands presenter to B (double-tap B's badge). | **B's** slider now feeds the room; A becomes a viewer. B explodes and **A's model follows** (A taps Catch Up if A had broken away). |
| E-25 | P0 | A, B | B is FOLLOWING. A explodes, then rotates the model. | B is exploded **and** still following A's rotation. The two do not interfere. |
| E-26 | P1 | A, B | A explodes, then taps a part. | On B: parts spread apart **and** the gold highlight on the right part. |
| E-27 | P1 | A, B | A switches model while exploded. | Both devices load the new model **assembled**, sliders at 0. |
| E-28 | P2 | A, B | A drags the slider rapidly back and forth for ten seconds. | B keeps up and settles on A's final value. No runaway lag, no stuck half-exploded state. |

---

## 6. AR edit and delete sync

Until now only **placement** was shared. Retyping a note, recolouring an object or deleting one
stayed on the device that did it, and the room silently drifted apart with nothing on screen to say
so. This closes that.

It is wired into `PersistenceManager` rather than into each screen, because every edit and delete
already passes through there to be saved — note editor, object customiser, selection toolbar, drag
handler and long-press delete alike. New: `Scripts/Networking/ItemSync.cs`.

**Two rules that explain most of the expected results below:**
- **Private items are never sent**, exactly as with placement.
- **An item with no Cloud Anchor is never sent.** It cannot be located on another device, so there
  is nothing there to update. Anchor hosting must have succeeded for sync to apply.

### 6.1 Single device — regression check

The hook sits on the save path that *every* note and object change uses, so the first thing to
confirm is that solo editing still works. Run these before anything else.

| ID | Pri | Steps | Expected |
|---|---|---|---|
| I-01 | P0 | Offline, place a note. Edit its text. Force-close and reopen the app. | The edited text is there. |
| I-02 | P0 | Offline, place an object. Change its colour and opacity. Force-close and reopen. | The new colour and opacity are there. |
| I-03 | P0 | Offline, long-press a note to delete it. Force-close and reopen. | It stays deleted. |
| I-04 | P0 | Offline, delete an object the same way. Reopen. | It stays deleted. |
| I-05 | P1 | Offline, drag a placed item to move it. Reopen. | It is where you left it. |
| I-06 | P1 | Offline, tick and untick checklist rows on a checklist note. Reopen. | The ticks are as you left them. |
| I-07 | P0 | Watch the Console through I-01 to I-06. | No exception, and **no** `[AuraSync] Note updated from the room:` lines — nothing is arriving from a room you are not in. |
| I-08 | P1 | Place a **private** note while in a room, then edit it. | Works normally. Nothing is sent (see I-24). |

### 6.2 Multi-device

A places shared, cloud-anchored items; B resolves them. Both must see the item **before** the edit
tests mean anything.

| ID | Pri | Devices | Steps | Expected |
|---|---|---|---|---|
| I-20 | P0 | A, B | A places a shared note; B sees it. A edits the text. | B's note shows the **new text** within about a second. |
| I-21 | P0 | A, B | A changes the note's colour. | B sees the new colour. |
| I-22 | P0 | A, B | A deletes the shared note. | It **disappears on B**. |
| I-23 | P0 | A, B | B edits a shared note's text. | **A** sees the change. Sync must work in both directions, not only from the presenter. |
| I-24 | P0 | A, B | A places a **private** note and edits it. | B never sees the note at all, and nothing changes on B. |
| I-25 | P0 | A, B | A places a shared object, changes colour and opacity, then deletes it. | B follows each step. |
| I-26 | P0 | A, B | **The loop check.** A edits a note once. Read the Console on both devices. | Exactly **one** `[AuraSync] Note updated from the room:` on B, and **none** on A. A continuous stream on either device means the echo guard has failed — stop and report it. |
| I-27 | P1 | A, B | A moves a shared item by dragging it. | B sees it move. |
| I-28 | P1 | A, B, C | A edits a note, then C joins the room. | C arrives with the **edited** version, not the original. |
| I-29 | P1 | A, B, C | A deletes a shared note, then C joins. | C never sees the deleted note. |
| I-30 | P1 | A, B | A places an item where Cloud Anchor hosting **fails** (point at a blank wall). A then edits it. | No crash and no error on either device. B never had the item, so nothing changes there. |
| I-31 | P2 | A, B | Put B in the background. A edits a note. Bring B back. | **Known gap:** B may still show the old version. Edits are not replayed to someone who was already in the room and missed the message. Note what you see — if this matters we will add a catch-up. |
| I-32 | P2 | A, B | A and B edit the **same** note at almost the same moment. | **Known gap:** last message to arrive wins, and the two devices may disagree. Record what happens; there is no conflict resolution yet. |

### 6.3 If edits do not arrive

Check in this order:

1. **Is the item shared and anchored?** A private item, or one whose Cloud Anchor failed to host, is
   deliberately never sent. Placing it should have shown "Cloud Anchor Hosted!".
2. **Did B ever have the item?** If placement never reached B, no edit will either. Fix placement first.
3. **The log line.** `[AuraSync] Note updated from the room: <id>` on the receiver means the message
   arrived and the problem is in applying it; its absence means the message never arrived.
