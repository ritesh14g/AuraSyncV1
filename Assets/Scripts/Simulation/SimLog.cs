using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using AuraSync.AI;

namespace AuraSync.Simulation
{
    /// <summary>
    /// The paper's simulation numbers, on the device only (<c>&lt;persistentDataPath&gt;/SimLog.jsonl</c>):
    ///
    ///   - **Sync drift.** Every 10 s while the working animation runs in a room, the server time
    ///     and the first rotor's angle are written down. Two phones' logs line up by server time;
    ///     the angle each predicts for the same instant differs by the drift (expected: rounding).
    ///     <see cref="DriftCheck"/> also does it on one phone: the angle the shared state predicts
    ///     against the angle actually on screen.
    ///   - **AI motion hints.** How many parts' AI-proposed motion matched the reviewed one.
    /// </summary>
    public static class SimLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "SimLog.jsonl");

        private const float SampleEvery = 10f;
        private static float _next;

        [Serializable]
        private class Line
        {
            public string time, model, room;
            public int serverMs;
            public float angle, throttle, speed;
        }

        public static void Sample(SimulationController sim)
        {
            if (sim == null || sim.State.mode != SimMode.Working || !sim.State.playing || !PhotonNetwork.InRoom) return;
            if (sim.Working.IsDayCycle || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + SampleEvery;

            int now = PhotonNetwork.ServerTimestamp;
            var line = new Line
            {
                time = DateTime.UtcNow.ToString("o"), model = AuraSync.Space.SpaceSceneManager.CurrentModelKey,
                room = PhotonNetwork.CurrentRoom.Name, serverMs = now,
                angle = sim.Working.FirstRotorAngle(sim.WorkingPhase(sim.State)),
                throttle = sim.State.value, speed = sim.State.speed,
            };
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[SimLog] Could not write the log: {ex.Message}"); }
        }

        /// <summary>
        /// Angle the room's shared state predicts now against the angle this phone drew last frame,
        /// in degrees. Near zero means the phone is exactly where every other phone is.
        /// </summary>
        public static string DriftCheck(SimulationController sim)
        {
            if (sim == null || sim.State.mode != SimMode.Working || sim.Working.IsDayCycle) return "Working animation (machine) is not running.";
            float shown = sim.Working.FirstRotorAngle(sim.Working.LastPhase);
            float predicted = sim.Working.FirstRotorAngle(sim.WorkingPhase(PhotonNetwork.InRoom ? SimulationController.ReadRoom() : sim.State));
            float drift = Mathf.Abs(Mathf.DeltaAngle(shown, predicted));
            float perFrame = sim.State.playing ? 360f * WorkingPlayer.RevolutionsPerSecond(sim.State.value) * sim.State.speed * Time.smoothDeltaTime : 0f;
            return $"Sync: shown {shown:0.0} deg, shared state says {predicted:0.0} deg, difference {drift:0.0} deg " +
                   $"(one frame of spin is {perFrame:0.0} deg).";
        }

        public static string Summary()
        {
            string hints = MotionHintAccuracy();
            if (!File.Exists(FilePath)) return $"Simulation: no sync samples yet. {hints}";
            int n = File.ReadAllLines(FilePath).Count(l => !string.IsNullOrWhiteSpace(l));
            return $"Simulation: {n} sync sample(s) in {FilePath}. {hints}";
        }

        /// <summary>Share of parts whose AI-proposed motion type matched the reviewed one.</summary>
        public static string MotionHintAccuracy()
        {
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk == null || !pk.HasParts) return "AI motion hints: no part knowledge on this model.";
            var parts = pk.AllParts.Where(e => e.ai != null && !string.IsNullOrEmpty(e.ai.motion)).ToList();
            if (parts.Count == 0) return "AI motion hints: none recorded in this model's file.";
            int right = parts.Count(e => string.Equals(e.ai.motion.Trim(), (e.motion?.type ?? "none").Trim(), StringComparison.OrdinalIgnoreCase));
            return $"AI motion hints: {right} of {parts.Count} matched the reviewed motion ({100 * right / parts.Count}%).";
        }
    }
}
