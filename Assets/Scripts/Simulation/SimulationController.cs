using System;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Space;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Simulation
{
    public enum SimMode { None = 0, Assembly = 1, Working = 2 }

    /// <summary>
    /// Everything that defines what the simulation shows, small enough to share as one room
    /// property. Nothing in it changes while it plays: the moving quantity is worked out from the
    /// clock, so it only changes when someone presses something.
    /// </summary>
    [Serializable]
    public struct SimState
    {
        public SimMode mode;
        public bool    playing;
        public float   speed;     // 0.5, 1 or 2
        public float   value;     // Working: throttle 0..1 (machines). Unused otherwise.
        public float   basePhase; // Assembly: steps done. Working: revolutions, or the hour of day.
        public int     start;     // clock (ms) when basePhase was true
        public bool    assemble;  // Assembly: true = putting together, false = taking apart

        public static SimState Off => new SimState { mode = SimMode.None, speed = 1f };

        public Hashtable ToRoom() => new Hashtable
        {
            { "m", (int)mode }, { "p", playing }, { "s", speed }, { "v", value },
            { "b", basePhase }, { "t", start }, { "a", assemble },
        };

        public static SimState FromRoom(object raw)
        {
            if (!(raw is Hashtable h)) return Off;
            return new SimState
            {
                mode      = h["m"] is int m ? (SimMode)m : SimMode.None,
                playing   = h["p"] is bool p && p,
                speed     = h["s"] is float s ? s : 1f,
                value     = h["v"] is float v ? v : 0.5f,
                basePhase = h["b"] is float b ? b : 0f,
                start     = h["t"] is int t ? t : 0,
                assemble  = h["a"] is bool a && a,
            };
        }

        public bool SameAs(SimState o) =>
            mode == o.mode && playing == o.playing && Mathf.Approximately(speed, o.speed) &&
            Mathf.Approximately(value, o.value) && Mathf.Approximately(basePhase, o.basePhase) &&
            start == o.start && assemble == o.assemble;
    }

    /// <summary>
    /// Simulation (V3 Phase 7): the assembly animation (parts come off, or go on, one at a time) and
    /// the working animation (rotors spin with the throttle, or the sun crosses the sky over the
    /// House).
    ///
    /// **Shared clock, not shared frames.** The state says "at clock time T the animation stood at
    /// phase P, moving at rate R". Every phone works the current phase out from Photon's server
    /// clock, which all phones in a room share, and animates it locally. Nothing is sent while it
    /// plays, every phone shows the fan at the same angle, and a late joiner lands on the right
    /// step and phase at once. Outside a room the phone's own clock does the same job.
    ///
    /// **Who drives it** follows Phase 1: whoever holds the Simulation control; changes go through
    /// the "show to everyone?" question, or straight out with KEEP SHARING. Followers take the
    /// room's simulation; a viewer running their own is exploring (Catch Up brings back the
    /// room's); a presenter who handed the control out watches the holder's.
    ///
    /// Parts move only through <see cref="PartPoseComposer"/>'s Simulation layer, so explode,
    /// isolate, the section cut and notes all keep working on moving parts, and stopping clears the
    /// layer: the model is back exactly as it was.
    /// </summary>
    public class SimulationController : MonoBehaviourPunCallbacks
    {
        public static SimulationController Instance { get; private set; }

        /// <summary>Seconds per assembly step at speed 1.</summary>
        public const float StepSeconds = 2.2f;

        public static readonly float[] Speeds = { 0.5f, 1f, 2f };

        public SimState State { get; private set; } = SimState.Off;

        public event Action OnChanged;

        public AssemblyPlayer Assembly { get; private set; }
        public WorkingPlayer  Working  { get; private set; }

        private bool _wasInRoom;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Assembly = new AssemblyPlayer();
            Working  = new WorkingPlayer();
            ControlOwnership.OnChanged += OnOwnershipChanged;
        }

        void OnDestroy()
        {
            ControlOwnership.OnChanged -= OnOwnershipChanged;
            Assembly.Stop();
            Working.Stop();
            if (Instance == this) Instance = null;
        }

        /// <summary>Called by SpaceSceneManager when a model loads: the old model's parts are gone.</summary>
        public void OnModelLoaded()
        {
            Assembly.Forget();
            Working.Forget();
            State = SimState.Off;

            // A viewer arriving at a model the room is simulating joins in at the right moment.
            if (FollowsRoom()) Apply(ReadRoom(), isLocal: false);
            else OnChanged?.Invoke();
        }

        // ── The clock ──────────────────────────────────────────────────────────

        /// <summary>Milliseconds on the clock every phone in the room shares (this phone's own outside one).</summary>
        public static int Now => PhotonNetwork.InRoom ? PhotonNetwork.ServerTimestamp : (int)(Time.realtimeSinceStartupAsDouble * 1000.0);

        /// <summary>Seconds of playing time since the state was last set (0 while paused).</summary>
        public static float Elapsed(SimState s) => s.playing ? unchecked(Now - s.start) / 1000f : 0f;

        /// <summary>Assembly steps done (0..steps) at this moment.</summary>
        public float AssemblyPosition(SimState s) =>
            Mathf.Clamp(s.basePhase + Elapsed(s) * s.speed / StepSeconds, 0f, Assembly.StepCount);

        /// <summary>Working phase at this moment: revolutions of a speed-1 part, or the hour of day.</summary>
        public float WorkingPhase(SimState s) =>
            Working.IsDayCycle
                ? Mathf.Repeat(s.basePhase + Elapsed(s) * s.speed * WorkingPlayer.HoursPerSecond, 24f)
                : s.basePhase + Elapsed(s) * s.speed * WorkingPlayer.RevolutionsPerSecond(s.value);

        /// <summary>The state with its phase brought up to now, so a change takes over smoothly.</summary>
        public SimState Rebased(SimState s)
        {
            if (s.mode == SimMode.Assembly) s.basePhase = AssemblyPosition(s);
            else if (s.mode == SimMode.Working) s.basePhase = WorkingPhase(s);
            s.start = Now;
            return s;
        }

        // ── Who drives it ──────────────────────────────────────────────────────

        public static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        public static bool DrivesRoom => ControlOwnership.LocalDrivesRoom(Control.Simulation);

        /// <summary>False for a presenter who handed Simulation out: they watch until TAKE BACK.</summary>
        public static bool LocalPlayerMaySimulate => !(IsPresenting && !DrivesRoom);

        private static bool FollowsRoom()
        {
            if (!PhotonNetwork.InRoom || DrivesRoom) return false;
            if (IsPresenting) return true;
            var sic = SpaceInteractionController.Instance;
            return sic == null || sic.IsSyncLocked;
        }

        public static SimState ReadRoom()
        {
            if (!PhotonNetwork.InRoom) return SimState.Off;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeys.Simulation, out object v)
                ? SimState.FromRoom(v) : SimState.Off;
        }

        // ── Changing it ────────────────────────────────────────────────────────

        /// <summary>
        /// A change from this phone's panel: applied at once, then offered to the room (or kept here,
        /// for a viewer, who has gone off exploring). <paramref name="dragging"/> = a slider is still
        /// moving; the question waits for <see cref="Commit"/> on release.
        /// </summary>
        public void Change(Func<SimState, SimState> edit, bool dragging = false)
        {
            if (!LocalPlayerMaySimulate) { OnChanged?.Invoke(); return; }

            SimState s = edit(Rebased(State));
            Apply(s, isLocal: true);

            if (!PhotonNetwork.InRoom) return;
            if (!DrivesRoom)
            {
                var sic = SpaceInteractionController.Instance;
                if (sic != null && sic.IsSyncLocked) sic.BreakSync();
                return;
            }
            if (!dragging) Commit();
        }

        /// <summary>Offers the current state to the room, if it differs from what the room has.</summary>
        public void Commit()
        {
            if (!DrivesRoom || State.SameAs(ReadRoom())) return;

            bool on = State.mode != SimMode.None;
            LiveSharing.Offer(Control.Simulation,
                on ? "Show this simulation to everyone?" : "Stop the simulation for everyone?",
                on ? "Accept: everyone following you sees it, in step with you.\nDeny: only you see it."
                   : "Accept: everyone following you sees the model back at rest.\nDeny: only you do.",
                share: () =>
                {
                    if (DrivesRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.Simulation, State.ToRoom() } });
                });
        }

        /// <summary>A replay (Phase 9) sets the state directly, on this phone only.</summary>
        public void ApplyReplay(SimState s) => Apply(s, isLocal: false);

        /// <summary>Catch Up: take the room's simulation whatever this phone was doing.</summary>
        public void SyncFromRoom()
        {
            if (IsPresenting) return;
            Apply(ReadRoom(), isLocal: false);
        }

        private void Apply(SimState s, bool isLocal)
        {
            // The challenge moves parts by hand; it and the simulation cannot share a model.
            if (AuraSync.Challenge.ReassemblyGame.IsActive) s = SimState.Off;

            SimMode before = State.mode;
            bool changed = !s.SameAs(State);
            State = s;

            if (before != s.mode)
            {
                if (before == SimMode.Assembly) Assembly.Stop();
                if (before == SimMode.Working)  Working.Stop();
                if (s.mode == SimMode.Assembly) Assembly.Begin();
                if (s.mode == SimMode.Working)  Working.Begin();
            }

            if (changed) SharedStateBus.Publish(StateKeys.Simulation, (int)s.mode, isLocal);
            OnChanged?.Invoke();
        }

        // ── Every frame ────────────────────────────────────────────────────────

        void Update()
        {
            if (State.mode != SimMode.None && AuraSync.Challenge.ReassemblyGame.IsActive) Apply(SimState.Off, isLocal: false);

            // Joining or leaving a room swaps the clock (server time vs this phone's): carry the
            // phase across so nothing jumps.
            bool inRoom = PhotonNetwork.InRoom;
            if (inRoom != _wasInRoom)
            {
                _wasInRoom = inRoom;
                if (State.mode != SimMode.None && !FollowsRoom())
                {
                    float phase = State.mode == SimMode.Assembly ? Assembly.LastPosition : Working.LastPhase;
                    SimState s = State; s.basePhase = phase; s.start = Now; State = s;
                }
            }

            switch (State.mode)
            {
                case SimMode.Assembly: Assembly.Tick(State.assemble, AssemblyPosition(State)); break;
                case SimMode.Working:  Working.Tick(WorkingPhase(State), State.value, State.playing ? State.speed : 0f); break;
            }

            SimLog.Sample(this);
        }

        // ── The room ───────────────────────────────────────────────────────────

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!changed.TryGetValue(RoomKeys.Simulation, out object raw)) return;
            if (DrivesRoom) return; // our own state coming back
            if (FollowsRoom()) Apply(SimState.FromRoom(raw), isLocal: false);
        }

        private void OnOwnershipChanged()
        {
            if (PhotonNetwork.InRoom && !DrivesRoom && FollowsRoom()) Apply(ReadRoom(), isLocal: false);
            else OnChanged?.Invoke();
        }

        public override void OnLeftRoom() => OnChanged?.Invoke();

        public override void OnJoinedRoom()
        {
            if (FollowsRoom()) Apply(ReadRoom(), isLocal: false);
        }
    }
}
