# AuraSync V2: Collaborative Room & Live Session Specification

> **Assigned Branch:** `feature/user-sharing`  
> **Target Developer:** Lead Collaborative Feature Developer  
> **Stack:** Unity 2022.3.62f3 Built-in Render Pipeline, Photon PUN2, Photon Voice 2, ARCore Extensions, TextMeshPro

---

## 1. Feature Vision & Architecture Overview

The **Live Collaborative Session Module** transforms AuraSync from a static viewer into a live multi-user interactive room (similar to a 3D Canva / Figma meeting space). 

Key Feature Highlights:
1. **Room Invite Links & Code System**: Create rooms, generate shareable links (`aurasync.app/room/XXXX`), and join via room code.
2. **Real-Time Voice Chat (Mic On/Off)**: Integrated spatial audio with a quick-action Mute/Unmute microphone toggle button.
3. **Canva-Style User Pointers**: Floating 2D/3D colored cursor badges with user names showing where participants are tapping, pointing, and looking in real time.
4. **Admin Model Manipulation & Live Sync**: Presenter/Admin manipulates 3D models (rotation, scaling, part highlighting) while changes mirror instantly on all participant screens.
5. **"Catch Up" / Follow Admin Button**: One-click camera/focus alignment button that teleports participants to the Admin's exact view vector if they get disoriented.
6. **Dedicated Overlay UI**: Clean, non-intrusive floating HUD overlaid on top of 3D model scenes.

```
+-----------------------------------------------------------------------------------+
|                            AuraSync Live Meeting HUD                              |
+-----------------------------------------------------------------------------------+
|  [<- Back]  Room: #AURA-4892 [Share Link]   (Mic ON/OFF)  [Users: 4]  (Admin: Ritesh)|
+-----------------------------------------------------------------------------------+
|                                                                                   |
|                                    [ 3D MODEL ]                                   |
|                              (Rotating / Manipulating)                            |
|                                                                                   |
|            (Pointer: Alex - Blue) -------------> [Tapped Part A]                  |
|            (Pointer: Sarah - Green) ------------> [Inspecting Side]                |
|                                                                                   |
+-----------------------------------------------------------------------------------+
| [ CATCH UP TO ADMIN ]         [ Model Parts ]          [ Voice Activity (||||) ]  |
+-----------------------------------------------------------------------------------+
```

---

## 2. Component & UI Architecture Breakdown

### A. Room Invite & Link System (`RoomLinkManager.cs`)
- **Generate Room Code**: `AURA-<4-DIGIT-HEX>` (e.g. `AURA-82F1`).
- **Share Link Generation**: Generates deep link URL `https://aurasync.app/join?room=AURA-82F1`.
- **UI Component (`RoomShareUI.cs`)**:
  - **Copy Link Button**: Copies deep link to device clipboard with toast feedback: *"Meeting link copied!"*.
  - **Room Code Display**: Displays readable room code at the top bar with one-tap copy.
  - **Join Modal**: Text input field allowing users to paste a room link or type a 4-digit code.

---

### B. Real-Time Voice Chat (`VoiceChatController.cs`)
- **Integration**: Photon Voice 2 (`Recorder` & `VoiceConnection`).
- **UI Controls (`VoiceUI.cs`)**:
  - **Mic Toggle Button**: Floating mic icon button.
    - **State ON (Unmuted)**: Mic icon active green/white with pulsing ripple animation when talking.
    - **State OFF (Muted)**: Red mic muted icon with slash (`mic_off`).
  - **Speaking Indicator Badge**: Small indicator next to user avatars showing who is currently speaking.
  - **Audio Mute Local**: Option to mute incoming room audio if desired.

---

### C. Canva-Style Live User Pointers (`CanvaPointerManager.cs`)
- **Design Inspiration**: Figma / Canva live multiplayer cursors.
- **Visual Badge Prefab (`UserCursorPrefab`)**:
  - Colored pointer arrow icon + User Name label pill (e.g., `🟦 Alex`, `🟩 Sarah`, `🟪 Ritesh`).
- **Sync Protocol (`UserPresence.cs`)**:
  - Sends local touch/gaze coordinates at **10Hz (every 100ms)** via Photon RPC `RPC_UpdateUserPointer(Vector3 worldPos, Vector2 screenPos, bool isTouching)`.
- **Screen & World Projection**:
  - In 3D Space Scene: Raycasts onto model surface to render cursor pill pinned to the model surface.
  - In UI Overlay: Displays 2D screen pointer when user taps or holds UI elements.

---

### D. Admin Interactive Model Sync (`AdminModelController.cs`)
- **Role Permissions**:
  - Room Creator default = **Admin / Presenter**.
  - Admin can pass presenter role to any participant (`RPC_PromoteToAdmin(int actorNumber)`).
- **Interactive Controls**:
  - **Transform Sync**: Rotation, translation, and pinch-to-scale sync in real time via Photon View (`PhotonTransformView`).
  - **Exploded View / Sub-part Select**: Admin taps a component (e.g., "Engine Piston") $\rightarrow$ RPC `RPC_HighlightModelPart(string partName, Color highlightColor)` highlights the exact sub-mesh on all student screens.
  - **Lock Manipulation**: Non-admin participants can view and explore freely, but Admin transform overrides during live presentation.

---

### E. "Catch Up" / Follow Admin System (`CatchUpManager.cs`)
- **Dual Exploration Modes**:
  1. **Independent Exploration Mode (Default)**: Participants can freely rotate, scale, pan, and tap model sub-parts on their own device without affecting others or being forced to match the Admin.
  2. **Live Follow Admin Mode (Triggered by Catch Up)**: When a participant taps **"⚡ Catch Up to Admin"**, their view immediately aligns to and live-mirrors the Admin's interactions.
- **Catch Up Behavior**:
  - **Camera & Focus Alignment**: Smoothly Slerps camera angle, distance, and focal target to match the Admin's exact view.
  - **Live Interaction Mirroring**: While Following Admin, any rotation, scaling, or sub-part selection performed by the Admin is mirrored live on the participant's screen in real time.
  - **Exit Follow Mode**: If the participant manually drags/rotates the model or taps **"Exit Follow Mode"**, follow mode disengages and returns them to Independent Exploration mode until they tap **Catch Up** again.
- **UI Button (`CatchUpButton.cs`)**:
  - Floating action button labeled **"⚡ Catch Up to Admin"** (changes to **"🔴 Following Admin (Tap to Break)"** when active).


---

### F. Dedicated Overlay UI (`LiveSessionUI.cs`)
Overlaid directly on top of `SpaceScene.unity` and AR scenes:

1. **Header Bar (Top)**:
   - `Exit Session` Button.
   - `Room Code` label & `Copy Link` button.
   - `Mic Toggle` (Mute/Unmute) button.
   - `Active Users Count` pill (e.g., `👥 4/5`).
   - `Presenter Badge` indicating current Admin.
2. **Main Overlay Layer (Middle)**:
   - Canva-style user pointer layer (`CanvaPointerManager`).
   - Highlight labels for selected 3D model parts.
3. **Footer Toolbar (Bottom)**:
   - **"⚡ Catch Up to Admin"** button (pulsing notification if Admin changes model view).
   - **"Mic Mute/Unmute"** quick toggle.
   - **"Model Explorer / Sub-parts"** drawer button.
   - **"Invite Team"** share button.

---

## 3. Data Schema & RPC Specifications

### `RoomSessionData.cs`
```csharp
[System.Serializable]
public class RoomSessionData
{
    public string roomId;             // e.g., "AURA-82F1"
    public string roomInviteUrl;      // "https://aurasync.app/join?room=AURA-82F1"
    public int adminActorNumber;      // Photon ActorNumber of Host/Presenter
    public string activeModelId;      // e.g., "JetEngine.glb"
    public string selectedSubPartId;  // Currently highlighted model part
}
```

### Key Photon RPC Signatures
```csharp
// 1. Sync Canva Pointer Position
[PunRPC]
public void RPC_UpdateUserPointer(int actorNumber, Vector3 worldPos, string userName, string colorHex, bool isTouching);

// 2. Admin Model Highlight & Interaction Sync
[PunRPC]
public void RPC_SyncModelPartSelection(string partName, bool isHighlighted);

// 3. Admin View Anchor for Catch Up
[PunRPC]
public void RPC_BroadcastAdminCameraView(Vector3 cameraPos, Quaternion cameraRot, Vector3 targetFocusPoint);

// 4. Voice Mic Mute Status
[PunRPC]
public void RPC_UpdateMicStatus(int actorNumber, bool isMuted);
```

---

## 4. Implementation Checklist for Developer (`feature/user-sharing`)

- [ ] **Task 1: Room Link & Join UI**
  - Implement `RoomLinkManager.cs` to generate `AURA-XXXX` code and copy meeting links to clipboard.
- [ ] **Task 2: Photon Voice 2 Integration**
  - Add Photon Voice recorder component and build `Mic Toggle Button` (Mute/Unmute).
- [ ] **Task 3: Canva-Style User Cursors**
  - Create `UserCursorPrefab` with name pill + color arrow.
  - Implement 10Hz position broadcast in `CanvaPointerManager.cs`.
- [ ] **Task 4: Admin Interactive Model Sync**
  - Implement sub-part mesh selection and RPC transform sync in `AdminModelController.cs`.
- [ ] **Task 5: "Catch Up to Admin" Button**
  - Implement camera Slerp animation to match Admin's position/orientation on tap in `CatchUpManager.cs`.
- [ ] **Task 6: Live Session Overlay UI**
  - Construct the dedicated header and footer HUD on top of `SpaceScene.unity`.
- [ ] **Task 7: Verification & Branch Check-In**
  - Verify full multi-user experience across 2+ test devices and check in to `feature/user-sharing`.
