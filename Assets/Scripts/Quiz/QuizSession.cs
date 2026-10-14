using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.AI;
using AuraSync.Analytics;
using AuraSync.Networking;
using AuraSync.Reports;
using AuraSync.Space;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Quiz
{
    [Serializable]
    public class QuizItem
    {
        public string   q = "";
        public string[] o = new string[4];
        public string   part = "";
        public int      a = -1;    // the right option; -1 while the quiz runs (students cannot see it)
        public string   e = "";    // explanation, also only after the end
    }

    /// <summary>The quiz as it travels in the room property <see cref="RoomKeys.Quiz"/>.</summary>
    [Serializable]
    public class QuizState
    {
        public string id = "";
        public int    teacher;
        public string teacherName = "";
        public string state = "running";   // "running" or "ended"
        public int    endsAt;              // Photon server time (ms) the time limit runs out; 0 = none
        public string modelHash = "";
        public string difficulty = "";
        public string reason = "";         // why it ended
        public List<QuizItem> questions = new List<QuizItem>();

        public bool Running => state == "running";
    }

    /// <summary>
    /// The AI quiz in a room (V3 Phase 11).
    ///
    /// **Teacher** (the presenter): generates questions (QuizGenerator), reviews them, then
    /// <see cref="Send"/>s. The quiz goes out as a room property, questions only, so a student who
    /// joins late can still take it. Each answer comes back as it is given (event 29), so the live
    /// panel shows who has finished and a student who leaves still counts. The quiz ends when
    /// everyone has finished, when the time runs out, or on END. The room property then gets the
    /// answers and explanations, every student sees their score, and the teacher's phone saves a
    /// report (ReportStore), with the heatmap's attention per part beside each part's results.
    ///
    /// **Students:** a banner, then one question at a time (QuizStudentUI).
    /// </summary>
    public class QuizSession : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static QuizSession Instance { get; private set; }
        public static event Action OnChanged;

        // ── Teacher ────────────────────────────────────────────────────────────

        public class StudentRun
        {
            public int actor;
            public string name = "";
            public int[] choices;
            public bool finished, present = true;
            public int Answered => choices.Count(c => c >= 0);
        }

        public class Hosted
        {
            public QuizState state;
            public List<QuizQuestion> full;          // with answers
            public readonly Dictionary<int, StudentRun> students = new Dictionary<int, StudentRun>();
            public Report report;                    // once it has ended
        }

        public Hosted Hosting { get; private set; }

        // ── Student ────────────────────────────────────────────────────────────

        public class Taking
        {
            public QuizState state;
            public int[] choices;
            public int index;
            public bool started, finished, ended, stopped;
            public int Correct => state.questions.Select((q, i) => q.a >= 0 && choices[i] == q.a ? 1 : 0).Sum();
        }

        public Taking Current { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnBus;
        }

        void Start() => ReadRoom(); // already in a room with a quiz running (late joiner)

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnBus;
            if (Instance == this) Instance = null;
        }

        public static bool LocalIsTeacher => PhotonNetwork.InRoom && AttentionSync.LocalIsPresenter;

        private void OnBus(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            string hash = ModelStateMemory.ModelHash(c.value as string ?? "");
            if (Hosting != null && Hosting.state.Running && Hosting.state.modelHash != hash) End("model changed");
            if (Current != null && Current.state.modelHash != hash && !Current.ended) { Current = null; OnChanged?.Invoke(); }
            if (Current == null) ReadRoom();
        }

        // ── Teacher: send and end ──────────────────────────────────────────────

        public bool Send(List<QuizQuestion> questions, int timeLimitSeconds, string difficulty)
        {
            if (!LocalIsTeacher || questions == null || questions.Count == 0) return false;
            if (Hosting != null && Hosting.state.Running) End("ended");

            var st = new QuizState
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 10),
                teacher = PhotonNetwork.LocalPlayer.ActorNumber,
                teacherName = NetworkManager.DisplayName(PhotonNetwork.LocalPlayer),
                modelHash = AttentionSync.CurrentModelHash,
                difficulty = difficulty ?? "",
                endsAt = timeLimitSeconds > 0 ? unchecked(PhotonNetwork.ServerTimestamp + timeLimitSeconds * 1000) : 0,
            };
            foreach (QuizQuestion q in questions)
                st.questions.Add(new QuizItem { q = q.question, o = (string[])q.options.Clone(), part = q.partPath ?? "" });

            Hosting = new Hosted { state = st, full = questions.Select(q => q.Clone()).ToList() };
            foreach (Player p in PhotonNetwork.PlayerListOthers) Student(p.ActorNumber, NetworkManager.DisplayName(p));

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.Quiz, JsonUtility.ToJson(st) } });
            QuizLog.Write("sent", st.id, questions.Count, Hosting.students.Count);
            Debug.Log($"[Quiz] Sent {questions.Count} question(s) to {Hosting.students.Count} student(s).");
            OnChanged?.Invoke();
            return true;
        }

        private StudentRun Student(int actor, string name)
        {
            if (!Hosting.students.TryGetValue(actor, out StudentRun s))
                Hosting.students[actor] = s = new StudentRun { actor = actor, name = name, choices = Enumerable.Repeat(-1, Hosting.full.Count).ToArray() };
            return s;
        }

        public void End(string reason)
        {
            Hosted h = Hosting;
            if (h == null || !h.state.Running) return;

            h.state.state = "ended";
            h.state.reason = reason;
            for (int i = 0; i < h.state.questions.Count; i++)
            {
                h.state.questions[i].a = h.full[i].answer;
                h.state.questions[i].e = h.full[i].explanation ?? "";
            }
            if (PhotonNetwork.InRoom)
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.Quiz, JsonUtility.ToJson(h.state) } });

            h.report = BuildReport(h);
            ReportStore.Save(h.report);
            QuizLog.Write("ended:" + reason, h.state.id, h.full.Count, h.students.Count);
            Debug.Log($"[Quiz] Ended ({reason}). Class average {h.report.Average * 100f:0}%.");
            OnChanged?.Invoke();
        }

        /// <summary>The teacher has looked at the results: back to a new quiz.</summary>
        public void ClearFinished()
        {
            if (Hosting != null && !Hosting.state.Running) { Hosting = null; OnChanged?.Invoke(); }
        }

        void Update()
        {
            Hosted h = Hosting;
            if (h != null && h.state.Running && h.state.endsAt != 0 && PhotonNetwork.InRoom &&
                unchecked(PhotonNetwork.ServerTimestamp - h.state.endsAt) >= 0)
                End("time up");
        }

        /// <summary>Seconds left on the time limit (-1 = no limit).</summary>
        public static float SecondsLeft(QuizState st)
        {
            if (st == null || st.endsAt == 0 || !PhotonNetwork.InRoom) return -1f;
            return Mathf.Max(0f, unchecked(st.endsAt - PhotonNetwork.ServerTimestamp) / 1000f);
        }

        private void CheckAllFinished()
        {
            Hosted h = Hosting;
            if (h == null || !h.state.Running) return;
            var present = h.students.Values.Where(s => s.present).ToList();
            if (present.Count > 0 && present.All(s => s.finished)) End("everyone finished");
        }

        // ── Student: answering ─────────────────────────────────────────────────

        public void Begin()
        {
            if (Current == null || Current.ended) return;
            Current.started = true;
            OnChanged?.Invoke();
        }

        public void Answer(int choice)
        {
            Taking t = Current;
            if (t == null || t.ended || t.finished || t.index >= t.choices.Length) return;
            t.choices[t.index] = choice;
            int qi = t.index;
            t.index++;
            if (t.index >= t.choices.Length) t.finished = true;

            if (PhotonNetwork.InRoom)
                PhotonNetwork.RaiseEvent(EventCodes.QuizAnswers, new object[] { t.state.id, qi, choice, t.finished ? 1 : 0 },
                    new RaiseEventOptions { TargetActors = new[] { t.state.teacher } }, SendOptions.SendReliable);
            OnChanged?.Invoke();
        }

        /// <summary>The student has seen their results.</summary>
        public void Dismiss()
        {
            if (Current != null && (Current.ended || Current.stopped)) { Current = null; OnChanged?.Invoke(); }
        }

        // ── The room ───────────────────────────────────────────────────────────

        private static QuizState ReadState(object raw)
        {
            if (!(raw is string json) || json.Length == 0) return null;
            try { return JsonUtility.FromJson<QuizState>(json); } catch { return null; }
        }

        private void ReadRoom()
        {
            if (!PhotonNetwork.InRoom) return;
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeys.Quiz, out object raw)) Receive(ReadState(raw), lateJoin: true);
        }

        private void Receive(QuizState st, bool lateJoin)
        {
            if (st == null || PhotonNetwork.LocalPlayer == null || st.teacher == PhotonNetwork.LocalPlayer.ActorNumber) return;
            if (st.modelHash != AttentionSync.CurrentModelHash) return;

            if (Current != null && Current.state.id == st.id)
            {
                if (!st.Running && !Current.ended)
                {
                    Current.state = st; // now with the answers
                    Current.ended = true;
                    OnChanged?.Invoke();
                }
                return;
            }

            // A quiz that already ended before this phone saw it is not offered.
            if (!st.Running) return;
            if (PhotonNetwork.CurrentRoom.GetPlayer(st.teacher) == null) return;

            Current = new Taking { state = st, choices = Enumerable.Repeat(-1, st.questions.Count).ToArray() };
            Debug.Log($"[Quiz] Received a quiz of {st.questions.Count} question(s) from {st.teacherName}{(lateJoin ? " (joined late)" : "")}.");
            OnChanged?.Invoke();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (changed.TryGetValue(RoomKeys.Quiz, out object raw)) Receive(ReadState(raw), lateJoin: false);
        }

        public override void OnJoinedRoom() => ReadRoom();

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.QuizAnswers) return;
            Hosted h = Hosting;
            if (h == null || !h.state.Running) return;
            if (!(e.CustomData is object[] d) || d.Length < 4 || !(d[0] is string id) || id != h.state.id) return;
            if (!(d[1] is int qi) || !(d[2] is int choice) || qi < 0 || qi >= h.full.Count) return;

            Player p = PhotonNetwork.CurrentRoom.GetPlayer(e.Sender);
            StudentRun s = Student(e.Sender, p != null ? NetworkManager.DisplayName(p) : $"Player {e.Sender}");
            s.choices[qi] = choice;
            s.finished |= d[3] is int f && f != 0;
            OnChanged?.Invoke();
            CheckAllFinished();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            // Late joiners read the room property themselves; list them for the live panel.
            if (Hosting != null && Hosting.state.Running) { Student(newPlayer.ActorNumber, NetworkManager.DisplayName(newPlayer)); OnChanged?.Invoke(); }
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            if (Current != null && !Current.ended && Current.state.teacher == other.ActorNumber)
            {
                Current.stopped = true; // the teacher left: no results will come
                OnChanged?.Invoke();
            }
            if (Hosting != null && Hosting.students.TryGetValue(other.ActorNumber, out StudentRun s))
            {
                s.present = false;
                OnChanged?.Invoke();
                CheckAllFinished();
            }
        }

        public override void OnLeftRoom()
        {
            if (Hosting != null && Hosting.state.Running) End("left room");
            if (Current != null && !Current.ended) Current.stopped = true;
            OnChanged?.Invoke();
        }

        // ── Report ─────────────────────────────────────────────────────────────

        private static Report BuildReport(Hosted h)
        {
            string key = SpaceSceneManager.CurrentModelKey ?? "";
            var r = new Report
            {
                kind = "quiz", model = key, modelName = System.IO.Path.GetFileNameWithoutExtension(key),
                room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "", difficulty = h.state.difficulty,
            };
            r.title = $"Quiz · {r.modelName} · {DateTime.Now:d MMM HH:mm}";
            int n = h.full.Count;

            foreach (StudentRun s in h.students.Values.OrderBy(s => s.name))
            {
                if (!s.present && s.Answered == 0) continue; // left before answering anything
                int correct = 0;
                for (int i = 0; i < n; i++) if (s.choices[i] == h.full[i].answer) correct++;
                r.students.Add(new StudentScore
                {
                    name = s.name, actor = s.actor, correct = correct, answered = s.Answered, total = n,
                    finished = s.finished, note = s.present ? (s.finished ? "" : "did not finish") : "left",
                });
            }

            for (int i = 0; i < n; i++)
            {
                QuizQuestion q = h.full[i];
                var stat = new QuestionStat
                {
                    text = q.question, partPath = q.partPath ?? "", options = (string[])q.options.Clone(), answer = q.answer,
                    explanation = q.explanation ?? "",
                    partLabel = string.IsNullOrEmpty(q.partPath) ? "Whole model" : HeatmapView.Label(q.partPath),
                };
                foreach (StudentRun s in h.students.Values)
                {
                    if (s.choices[i] < 0) continue;
                    stat.answered++;
                    if (s.choices[i] == q.answer) stat.correct++;
                }
                r.questions.Add(stat);
            }

            // Per part: how often its questions were missed, beside the attention it got (Phase 10).
            Dictionary<string, float> attention = InteractionTracker.Instance != null
                ? InteractionTracker.Instance.Combined(-1).ToDictionary(kv => kv.Key, kv => kv.Value.Score)
                : new Dictionary<string, float>();
            foreach (var group in r.questions.Where(q => q.partPath.Length > 0).GroupBy(q => q.partPath))
            {
                float att = attention.Where(kv => kv.Key == group.Key || kv.Key.StartsWith(group.Key + "/")).Sum(kv => kv.Value);
                r.parts.Add(new PartRow
                {
                    path = group.Key, label = group.First().partLabel, attention = att, questions = group.Count(),
                    asked = group.Sum(q => q.answered), missed = group.Sum(q => q.answered - q.correct),
                });
            }
            r.parts = r.parts.OrderByDescending(p => p.asked > 0 ? (float)p.missed / p.asked : 0f).ToList();
            return r;
        }
    }

    /// <summary>
    /// The paper's quiz numbers (<c>QuizLog.jsonl</c>): quizzes sent and ended, and how many
    /// AI-written questions the teacher kept without editing (question quality).
    /// </summary>
    public static class QuizLog
    {
        public static string FilePath => System.IO.Path.Combine(Application.persistentDataPath, "QuizLog.jsonl");

        [Serializable] private class Line { public string time, evt, quiz; public int questions, students, generated, edited, deleted, regenerated; }

        public static void Write(string evt, string quiz, int questions, int students) =>
            Append(new Line { time = DateTime.UtcNow.ToString("o"), evt = evt, quiz = quiz, questions = questions, students = students });

        /// <summary>At Send: how the AI's questions fared in the teacher's review.</summary>
        public static void Review(int generated, int edited, int deleted, int regenerated) =>
            Append(new Line { time = DateTime.UtcNow.ToString("o"), evt = "review", generated = generated, edited = edited, deleted = deleted, regenerated = regenerated });

        private static void Append(Line l)
        {
            try { System.IO.File.AppendAllText(FilePath, JsonUtility.ToJson(l) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[QuizLog] {ex.Message}"); }
        }

        public static string Summary()
        {
            if (!System.IO.File.Exists(FilePath)) return "Quiz: none yet.";
            var lines = new List<Line>();
            foreach (string raw in System.IO.File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            var reviews = lines.Where(l => l.evt == "review").ToList();
            int gen = reviews.Sum(l => l.generated), touched = reviews.Sum(l => l.edited + l.deleted + l.regenerated);
            string quality = gen > 0 ? $"{gen - touched} of {gen} AI questions kept unedited ({100 * (gen - touched) / gen}%)" : "no reviews yet";
            return $"Quiz: {lines.Count(l => l.evt == "sent")} sent; {quality}. File: {FilePath}";
        }
    }
}
