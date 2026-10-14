using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;
using AuraSync.Space;
using AuraSync.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Notes
{
    /// <summary>
    /// Notes attached to parts (V3 Phase 5): which notes the model on screen has, adding, editing
    /// and deleting them, sharing them with the room, and placing a new one with a tap.
    ///
    /// **Where notes live (D4).** On the author's phone (<see cref="PartNoteStore"/>). In a room,
    /// each author's "Everyone" notes for the model on screen are put in room properties, one key
    /// per note: <c>"PN:" + model hash + ":" + note id + ":" + author's actor number</c>. So:
    ///   - a note is in the room while its author is; when they leave, the master client clears
    ///     their keys, and every phone hides notes of absent authors straight away;
    ///   - coming back (or hosting the model next week) shares them again;
    ///   - the actor number in the key keeps a rejoin's fresh keys apart from the old ones the
    ///     master is clearing, so the clean-up can never wipe a note that was just shared again.
    /// "Only me" notes never leave the phone.
    ///
    /// **Who may change a note:** its author, or the presenter (Phase 1's rule). A presenter's edit
    /// or delete reaches the author's phone through the same key and is saved there, so it sticks.
    /// That works because a note is only in the room while its author is.
    ///
    /// **Hiding** (all notes, or one) is this phone only and lasts for the app session.
    /// </summary>
    public class PartNoteManager : MonoBehaviourPunCallbacks
    {
        public static PartNoteManager Instance { get; private set; }

        public const int MaxTextChars   = 300;
        public const int MaxNotesPerAuthorPerModel = 30;

        /// <summary>Raised when the list of notes, or any note, changes (including hiding).</summary>
        public static event Action OnChanged;

        /// <summary>Raised when placing starts or stops, for the "tap the spot" banner.</summary>
        public static event Action OnPlacingChanged;

        /// <summary>A spot was tapped while placing: (part path, point and direction in the part's space).</summary>
        public static event Action<string, Vector3, Vector3> OnSpotPicked;

        public static bool IsPlacing { get; private set; }

        private static int _consumedTapFrame = -1;

        /// <summary>True when a tap on the model is placing a note this frame, so it must not also select a part.</summary>
        public static bool ConsumesTap => IsPlacing || Time.frameCount == _consumedTapFrame;

        // Session-only, this phone only.
        public static bool HideAll { get; private set; }
        private static readonly HashSet<string> _hidden = new HashSet<string>();

        private string _modelKey = "";
        private List<PartNote> _mine = new List<PartNote>();          // this phone's notes on the current model
        private readonly HashSet<string> _myDeletes = new HashSet<string>(); // keys I nulled, until the echo
        private SpaceInteractionController _sic;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnStateChange;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnStateChange;
            if (_sic != null) _sic.OnTap -= OnTap;
            if (Instance == this) Instance = null;
            IsPlacing = false;
        }

        void Update()
        {
            // The interaction controller belongs to the scene and may arrive after this.
            if (_sic == null && SpaceInteractionController.Instance != null)
            {
                _sic = SpaceInteractionController.Instance;
                _sic.OnTap -= OnTap;
                _sic.OnTap += OnTap;
            }

            if (IsPlacing && AuraSync.Challenge.ReassemblyGame.IsActive) StopPlacing();
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            _modelKey = c.value as string ?? "";
            _mine = PartNoteStore.Load(_modelKey);
            StopPlacing();
            ShareMine();
            Raise();
        }

        private static void Raise() => OnChanged?.Invoke();

        // ── Reading ────────────────────────────────────────────────────────────

        public string ModelKey => _modelKey;

        private static string Prefix(string modelKey) =>
            RoomKeys.PartNotePrefix + ModelStateMemory.ModelHash(modelKey) + ":";

        private static string KeyFor(PartNote n, int actor) => Prefix(n.modelKey) + n.id + ":" + actor;

        /// <summary>
        /// Every note to show on this model: the room's notes from authors who are present, plus this
        /// phone's own (its private ones, and its shared ones before they reach the room).
        /// </summary>
        public List<PartNote> All()
        {
            if (_replayNotes != null) return new List<PartNote>(_replayNotes);

            var byId = new Dictionary<string, PartNote>();

            if (PhotonNetwork.InRoom && !string.IsNullOrEmpty(_modelKey))
            {
                string prefix = Prefix(_modelKey);
                foreach (var kv in PhotonNetwork.CurrentRoom.CustomProperties)
                {
                    if (!(kv.Key is string key) || !key.StartsWith(prefix)) continue;
                    if (!(kv.Value is Hashtable h)) continue; // null = deleted or cleared
                    PartNote n = FromRoom(h, _modelKey);
                    if (n == null || !ParseKey(key, out _, out _, out n.authorActor)) continue;
                    if (PhotonNetwork.CurrentRoom.GetPlayer(n.authorActor) == null) continue; // author has left
                    byId[n.id] = n;
                }
            }

            foreach (PartNote n in _mine)
                if (!byId.ContainsKey(n.id)) byId[n.id] = n;

            return byId.Values.OrderBy(n => n.created).ToList();
        }

        public PartNote Find(string id) => All().FirstOrDefault(n => n.id == id);

        public static bool IsMine(PartNote n) => n != null && n.authorDevice == PartNoteStore.DeviceId;

        private static bool LocalIsPresenter =>
            PhotonNetwork.InRoom && NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;

        /// <summary>The author, or (for a shared note) the presenter.</summary>
        public static bool CanEdit(PartNote n) =>
            _replayNotes == null && (IsMine(n) || (n != null && !n.isPrivate && LocalIsPresenter));

        private static List<PartNote> _replayNotes;

        /// <summary>
        /// A replay (Phase 9) shows the recording's notes instead of the live ones, read-only and never
        /// saved. null goes back to the live notes.
        /// </summary>
        public static void SetReplayNotes(List<PartNote> notes)
        {
            if (notes == null && _replayNotes == null) return;
            _replayNotes = notes;
            Raise();
        }

        public static bool IsHidden(PartNote n) => n != null && (HideAll || _hidden.Contains(n.id));
        public static bool IsHiddenOne(PartNote n) => n != null && _hidden.Contains(n.id);

        // ── Changing ───────────────────────────────────────────────────────────

        /// <summary>Adds a note written on this phone. Returns null at the per-model limit.</summary>
        public PartNote Add(string path, Vector3 localPoint, Vector3 localNormal, string text, string colorHex, bool isPrivate)
        {
            if (string.IsNullOrEmpty(_modelKey)) return null;
            if (_mine.Count >= MaxNotesPerAuthorPerModel)
            {
                Toast.Show($"You have {MaxNotesPerAuthorPerModel} notes on this model already. Delete one first.");
                return null;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var n = new PartNote
            {
                modelKey = _modelKey, path = path, LocalPoint = localPoint, LocalNormal = localNormal.normalized,
                text = Clip(text), colorHex = colorHex, isPrivate = isPrivate,
                authorName = MyName(), authorDevice = PartNoteStore.DeviceId, created = now, edited = now,
            };

            _mine.Add(n);
            PartNoteStore.Save(_modelKey, _mine);
            if (!isPrivate) PutInRoom(n);
            NotesLog.Write("add", n);
            Raise();
            SharedStateBus.Publish(StateKeys.NoteAdded, n.path, isLocal: true);
            return n;
        }

        /// <summary>
        /// Changes a note's text and colour (author or presenter) and, for the author, whether it is
        /// shared. A presenter cannot make someone else's note private.
        /// </summary>
        public void Edit(PartNote note, string text, string colorHex, bool isPrivate)
        {
            if (!CanEdit(note)) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (IsMine(note))
            {
                PartNote mine = _mine.FirstOrDefault(m => m.id == note.id);
                if (mine == null) return;
                bool wasPrivate = mine.isPrivate;
                mine.text = Clip(text);
                mine.colorHex = colorHex;
                mine.isPrivate = isPrivate;
                mine.edited = now;
                PartNoteStore.Save(_modelKey, _mine);

                if (!isPrivate) PutInRoom(mine);
                else if (!wasPrivate) TakeFromRoom(mine, PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 0);
                NotesLog.Write("edit", mine);
            }
            else
            {
                // The presenter's edit of someone else's note: rewrite its key; the author's phone
                // saves the change when it arrives (OnRoomPropertiesUpdate).
                PartNote copy = note.Clone();
                copy.text = Clip(text);
                copy.colorHex = colorHex;
                copy.edited = now;
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyFor(copy, copy.authorActor), ToRoom(copy) } });
                NotesLog.Write("edit-by-presenter", copy);
            }
            Raise();
        }

        public void Delete(PartNote note)
        {
            if (!CanEdit(note)) return;

            if (IsMine(note))
            {
                _mine.RemoveAll(m => m.id == note.id);
                PartNoteStore.Save(_modelKey, _mine);
                if (!note.isPrivate && PhotonNetwork.InRoom) TakeFromRoom(note, PhotonNetwork.LocalPlayer.ActorNumber);
                NotesLog.Write("delete", note);
            }
            else
            {
                TakeFromRoom(note, note.authorActor);
                NotesLog.Write("delete-by-presenter", note);
            }
            _hidden.Remove(note.id);
            Raise();
        }

        public static void SetHideAll(bool hide)
        {
            if (HideAll == hide) return;
            HideAll = hide;
            Raise();
        }

        public static void SetHidden(PartNote n, bool hide)
        {
            if (n == null) return;
            if (hide ? _hidden.Add(n.id) : _hidden.Remove(n.id)) Raise();
        }

        private static string Clip(string s)
        {
            s = (s ?? "").Trim();
            return s.Length <= MaxTextChars ? s : s.Substring(0, MaxTextChars);
        }

        private static string MyName()
        {
            if (PhotonNetwork.InRoom) return NetworkManager.DisplayName(PhotonNetwork.LocalPlayer);
            return string.IsNullOrEmpty(PhotonNetwork.NickName) ? "Me" : PhotonNetwork.NickName;
        }

        // ── Placing ────────────────────────────────────────────────────────────

        /// <summary>The next tap on the model says where the new note goes.</summary>
        public void BeginPlacing()
        {
            if (string.IsNullOrEmpty(_modelKey) || AuraSync.Challenge.ReassemblyGame.IsActive) return;
            IsPlacing = true;
            OnPlacingChanged?.Invoke();
        }

        public void StopPlacing()
        {
            if (!IsPlacing) return;
            IsPlacing = false;
            OnPlacingChanged?.Invoke();
        }

        private void OnTap(Vector2 screenPos)
        {
            if (!IsPlacing) return;
            _consumedTapFrame = Time.frameCount; // the selector must not treat this tap as a pick

            Transform root = _sic != null ? _sic.ModelRoot : null;
            Camera cam = Camera.main;
            if (root == null || cam == null) return;

            if (!ModelRaycaster.Raycast(cam.ScreenPointToRay(screenPos), root, out Renderer hit, out Vector3 point, out Vector3 normal))
            {
                Toast.Show("Tap on the model itself, where the note should go.");
                return; // still placing
            }

            Transform part = hit.transform;
            string path = ModelRaycaster.GetPartPath(root, part);
            if (string.IsNullOrEmpty(path)) return;

            StopPlacing();
            OnSpotPicked?.Invoke(path, part.InverseTransformPoint(point), part.InverseTransformDirection(normal).normalized);
        }

        // ── The room ───────────────────────────────────────────────────────────

        /// <summary>Puts every "Everyone" note this phone has on the current model into the room.</summary>
        private void ShareMine()
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(_modelKey)) return;

            var props = new Hashtable();
            string name = MyName();
            foreach (PartNote n in _mine)
            {
                if (n.isPrivate) continue;
                n.authorName = name; // the name in this room
                props[KeyFor(n, PhotonNetwork.LocalPlayer.ActorNumber)] = ToRoom(n);
            }
            if (props.Count == 0) return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[PartNotes] Shared {props.Count} saved note(s) on '{_modelKey}' with the room.");
        }

        private void PutInRoom(PartNote n)
        {
            if (!PhotonNetwork.InRoom) return; // shared next time this phone is in a room on the model
            n.authorName = MyName();
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyFor(n, PhotonNetwork.LocalPlayer.ActorNumber), ToRoom(n) } });
        }

        private void TakeFromRoom(PartNote n, int actor)
        {
            if (!PhotonNetwork.InRoom) return;
            string key = KeyFor(n, actor);
            if (IsMine(n)) _myDeletes.Add(key);
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { key, null } });
        }

        private static Hashtable ToRoom(PartNote n) => new Hashtable
        {
            { "i", n.id }, { "p", n.path }, { "x", n.point }, { "n", n.normal },
            { "t", n.text }, { "c", n.colorHex }, { "a", n.authorName }, { "d", n.authorDevice },
            { "s", n.created }, { "e", n.edited },
        };

        private static PartNote FromRoom(Hashtable h, string modelKey)
        {
            if (!(h["i"] is string id)) return null;
            var n = new PartNote
            {
                id = id, modelKey = modelKey,
                path = h["p"] as string ?? "",
                point = h["x"] as float[] ?? new float[3],
                normal = h["n"] as float[] ?? new[] { 0f, 0f, 1f },
                text = h["t"] as string ?? "",
                colorHex = h["c"] as string ?? "#FFE066",
                authorName = h["a"] as string ?? "",
                authorDevice = h["d"] as string ?? "",
                created = h["s"] is long s ? s : 0,
                edited = h["e"] is long e ? e : 0,
            };
            return n.point.Length == 3 && n.normal.Length == 3 ? n : null;
        }

        /// <summary>Splits "PN:hash:id:actor" into id and actor.</summary>
        private static bool ParseKey(string key, out string modelHash, out string id, out int actor)
        {
            modelHash = id = ""; actor = 0;
            if (!key.StartsWith(RoomKeys.PartNotePrefix)) return false;
            string[] parts = key.Substring(RoomKeys.PartNotePrefix.Length).Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[2], out actor)) return false;
            modelHash = parts[0]; id = parts[1];
            return true;
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            bool any = false;
            bool savedMine = false;

            foreach (var kv in changed)
            {
                if (!(kv.Key is string key) || !ParseKey(key, out string hash, out string id, out int actor)) continue;
                any = true;

                if (_myDeletes.Remove(key)) continue;                                    // my own delete echoing back
                if (hash != ModelStateMemory.ModelHash(_modelKey)) continue;             // another model
                if (!PhotonNetwork.InRoom || actor != PhotonNetwork.LocalPlayer.ActorNumber) continue; // not my key

                PartNote mine = _mine.FirstOrDefault(m => m.id == id);
                if (mine == null) continue;

                if (kv.Value == null)
                {
                    // Not my own delete, and the master only clears people who left: the presenter.
                    _mine.Remove(mine);
                    savedMine = true;
                    Toast.Show("The presenter deleted one of your notes.");
                }
                else if (kv.Value is Hashtable h && FromRoom(h, _modelKey) is PartNote remote &&
                         (remote.text != mine.text || remote.colorHex != mine.colorHex))
                {
                    mine.text = remote.text;
                    mine.colorHex = remote.colorHex;
                    mine.edited = remote.edited;
                    savedMine = true;
                    Toast.Show("The presenter edited one of your notes.");
                }
            }

            if (savedMine) PartNoteStore.Save(_modelKey, _mine);
            if (any) Raise();
        }

        public override void OnJoinedRoom()
        {
            ShareMine();
            Raise();
        }

        public override void OnLeftRoom()
        {
            _myDeletes.Clear();
            Raise();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => Raise();

        public override void OnPlayerLeftRoom(Player other)
        {
            // The leaver's notes vanish from every screen at once (All() checks who is present);
            // the master also clears their keys, for every model, so the room does not fill up.
            if (PhotonNetwork.IsMasterClient)
            {
                var clear = new Hashtable();
                foreach (var kv in PhotonNetwork.CurrentRoom.CustomProperties)
                    if (kv.Key is string key && kv.Value != null && ParseKey(key, out _, out _, out int actor) && actor == other.ActorNumber)
                        clear[key] = null;
                if (clear.Count > 0) PhotonNetwork.CurrentRoom.SetCustomProperties(clear);
            }
            Raise();
        }

        // ── Paper metric: drift ────────────────────────────────────────────────

        /// <summary>
        /// How far each visible note sits from its part's actual surface right now, as a share of
        /// the part's size. Should be zero (to rounding) whatever the model is doing: exploded,
        /// isolated, turned. Measured by casting from just outside the note back onto its own part.
        /// </summary>
        public string MeasureDrift()
        {
            Transform root = _sic != null ? _sic.ModelRoot : null;
            if (root == null) return "No model.";

            float explode = ModelExploder.Instance != null ? ModelExploder.Instance.Amount : 0f;
            string isolated = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";
            var lines = new List<string>();
            float worst = 0f;

            foreach (PartNote n in All())
            {
                Transform part = ModelRaycaster.FindPart(root, n.path);
                if (part == null || !ModelRaycaster.TryGetLocalBounds(part, out Bounds b)) continue;

                Vector3 p = part.TransformPoint(n.LocalPoint);
                Vector3 nn = part.TransformDirection(n.LocalNormal).normalized;
                float size = Mathf.Max(part.TransformVector(b.size).magnitude, 1e-6f);
                float eps = size * 0.02f;

                if (!ModelRaycaster.Raycast(new Ray(p + nn * eps, -nn), part, out _, out Vector3 hit)) continue;
                float drift = Vector3.Distance(hit, p) / size;
                worst = Mathf.Max(worst, drift);
                lines.Add($"{n.id} drift {drift * 100f:0.000}%");
                NotesLog.Drift(n, drift, explode, isolated);
            }

            return lines.Count == 0
                ? "No measurable notes on screen."
                : $"Note drift (explode {explode:0.##}, isolated '{isolated}'): worst {worst * 100f:0.000}% of part size. " + string.Join("; ", lines);
        }
    }

    /// <summary>
    /// The paper's note numbers, on the device only: adds, edits, deletes and drift measurements.
    /// <c>&lt;persistentDataPath&gt;/NotesLog.jsonl</c>, one JSON object per line.
    /// </summary>
    public static class NotesLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "NotesLog.jsonl");

        [Serializable]
        private class Line
        {
            public string time, evt, id, model, path, isolated;
            public bool isPrivate;
            public float drift, explode;
        }

        public static void Write(string evt, PartNote n) => Append(new Line
        {
            time = DateTime.UtcNow.ToString("o"), evt = evt, id = n.id, model = n.modelKey, path = n.path, isPrivate = n.isPrivate,
        });

        public static void Drift(PartNote n, float drift, float explode, string isolated) => Append(new Line
        {
            time = DateTime.UtcNow.ToString("o"), evt = "drift", id = n.id, model = n.modelKey, path = n.path,
            drift = drift, explode = explode, isolated = isolated,
        });

        private static void Append(Line l)
        {
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(l) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[NotesLog] Could not write the log: {ex.Message}"); }
        }

        public static string Summary()
        {
            if (!File.Exists(FilePath)) return $"No notes logged yet ({FilePath}).";
            var lines = new List<Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            var drifts = lines.Where(l => l.evt == "drift").Select(l => l.drift).ToList();
            return $"Notes: {lines.Count(l => l.evt == "add")} added, {lines.Count(l => l.evt.StartsWith("edit"))} edited, " +
                   $"{lines.Count(l => l.evt.StartsWith("delete"))} deleted; drift measured {drifts.Count} time(s), " +
                   $"worst {(drifts.Count > 0 ? drifts.Max() * 100f : 0f):0.000}% of part size. File: {FilePath}";
        }
    }
}
