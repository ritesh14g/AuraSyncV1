using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// Tools → AuraSync → Part Labeler: makes a model's part knowledge file.
    ///
    /// 1. **Build part list** from the model loaded in Play mode (SpaceScene).
    /// 2. **Label with AI**: one Gemini call for the whole model, then one per part, each with five
    ///    pictures (the part alone from three sides, the whole model, and the whole model without
    ///    the part, so the AI can see where it sits).
    /// 3. **Work out geometry**: which parts touch, motion axes, and the assembly order.
    /// 4. **Review** every entry, correct what is wrong, tick it.
    /// 5. **Export** the JSON, upload it next to the .glb, and add its name as the model's
    ///    "partsUrl" in models.json.
    ///
    /// The draft is saved after every AI answer (Library/AuraSyncPartLabeler), so a closed window,
    /// an exited Play mode or a used-up daily quota loses nothing: load the draft and carry on.
    /// </summary>
    public class PartLabelerWindow : EditorWindow
    {
        private const string PrefKey = "AuraSync.PartLabeler.GeminiKey";
        private const string PrefModel = "AuraSync.PartLabeler.GeminiModel";
        private const string PrefDelay = "AuraSync.PartLabeler.DelaySeconds";
        private const int ImageSize = 512;

        [SerializeField] private PartKnowledgeFile draft = new PartKnowledgeFile();
        [SerializeField] private string draftModelKey = "";
        [SerializeField] private List<bool> expanded = new List<bool>();

        /// <summary>
        /// Accuracy test for the paper: on a model whose file already has real part names, hide
        /// those names from the AI so its labels can be scored against them fairly.
        /// </summary>
        [SerializeField] private bool blindTest;

        private string _apiKey = "";
        // Tried in order. Free Gemini models are often "experiencing high demand" (503) or out of
        // quota (429), and each model has its own quota, so a list keeps labelling going.
        private const string DefaultModel = "gemini-3.8-flash, gemini-3.5-flash, gemini-3.1-flash-lite";

        private string _geminiModel = DefaultModel;
        private float _delaySeconds = 7f;
        private bool _showKey;

        private Vector2 _scroll;
        private string _status = "";
        private MessageType _statusType = MessageType.Info;
        private bool _busy;
        private CancellationTokenSource _cancel;
        private readonly Dictionary<string, Texture2D[]> _previews = new Dictionary<string, Texture2D[]>();

        [MenuItem("Tools/AuraSync/Part Labeler")]
        public static void Open()
        {
            var w = GetWindow<PartLabelerWindow>("Part Labeler");
            w.minSize = new Vector2(460, 500);
        }

        private void OnEnable()
        {
            _apiKey = EditorPrefs.GetString(PrefKey, "");
            _geminiModel = EditorPrefs.GetString(PrefModel, DefaultModel);

            // Google retired the 2.5 models for new accounts (October 2026): a saved 2.5 name
            // would make every call fail with 404, so move it to the current default.
            // Also upgrade the earlier single-model default to the list.
            if (_geminiModel.StartsWith("gemini-2.5") || _geminiModel.Trim() == "gemini-3.8-flash")
            {
                _geminiModel = DefaultModel;
                EditorPrefs.SetString(PrefModel, _geminiModel);
            }
            _delaySeconds = EditorPrefs.GetFloat(PrefDelay, 7f);
        }

        private void OnDisable()
        {
            _cancel?.Cancel();
        }

        // ── GUI ────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSetup();
            EditorGUILayout.Space(8);
            DrawModel();
            EditorGUILayout.Space(8);
            DrawActions();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);

            EditorGUILayout.Space(8);
            DrawReview();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSetup()
        {
            EditorGUILayout.LabelField("1. Gemini (free tier)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _apiKey = _showKey
                    ? EditorGUILayout.TextField("API key", _apiKey)
                    : EditorGUILayout.PasswordField("API key", _apiKey);
                if (GUILayout.Button(_showKey ? "Hide" : "Show", GUILayout.Width(50))) _showKey = !_showKey;
            }
            _geminiModel = EditorGUILayout.TextField(new GUIContent("Models (in order)", "Comma-separated, tried in order. When one is busy (503), out of quota (429) or retired (404), the next one is used. List current ones at generativelanguage.googleapis.com/v1beta/models?key=YOUR_KEY"), _geminiModel);
            _delaySeconds = EditorGUILayout.Slider(new GUIContent("Pause between calls (s)", "Keeps under the free tier's requests-per-minute limit."), _delaySeconds, 0f, 30f);

            if (GUI.changed)
            {
                EditorPrefs.SetString(PrefKey, _apiKey.Trim());
                EditorPrefs.SetString(PrefModel, _geminiModel.Trim());
                EditorPrefs.SetFloat(PrefDelay, _delaySeconds);
            }
            EditorGUILayout.HelpBox("The key is stored on this computer only (EditorPrefs). It is never built into the app.\nGet one free at aistudio.google.com/apikey.", MessageType.None);
        }

        private void DrawModel()
        {
            EditorGUILayout.LabelField("2. Model", EditorStyles.boldLabel);

            Transform root = ModelRoot();
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode in SpaceScene and open the model you want to label.", MessageType.Warning);
            }
            else if (root == null)
            {
                EditorGUILayout.HelpBox("Waiting for a model to finish loading in SpaceScene...", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Loaded", ShortName(SpaceSceneManager.CurrentModelKey));
                if (!string.IsNullOrEmpty(draftModelKey) && draftModelKey != SpaceSceneManager.CurrentModelKey)
                {
                    EditorGUILayout.HelpBox($"The draft below is for {ShortName(draftModelKey)}, not the loaded model. Build the part list again, or open that model.", MessageType.Warning);
                }
            }

            draft.context = EditorGUILayout.TextField(new GUIContent("What it is", "A few words, e.g. 'Turbofan jet engine'. Leave empty and the AI suggests one."), draft.context);
            draft.wholeModelOnly = EditorGUILayout.ToggleLeft(new GUIContent("Whole model only (no part labels)", "For models whose pieces are not real parts, such as a 3D scan cut into chunks (the House)."), draft.wholeModelOnly);
            blindTest = EditorGUILayout.ToggleLeft(new GUIContent("Blind test: hide the file's part and material names from the AI", "For the paper's accuracy test on models whose files already have real part names. The AI then labels from the pictures alone, and its labels can be scored against the real names."), blindTest);

            EditorGUILayout.LabelField("Summary");
            draft.summary = EditorGUILayout.TextArea(draft.summary, WrapArea, GUILayout.MinHeight(48));
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("3. Steps", EditorStyles.boldLabel);
            bool ready = EditorApplication.isPlaying && ModelRoot() != null;

            using (new EditorGUI.DisabledScope(_busy || !ready))
            {
                if (GUILayout.Button("Build part list from the loaded model")) BuildPartList();

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_apiKey)))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Label with AI (unlabelled only)")) _ = RunLabelling(false);
                        if (GUILayout.Button("Relabel everything", GUILayout.Width(150))) _ = RunLabelling(true);
                    }
                }

                using (new EditorGUI.DisabledScope(draft.wholeModelOnly || draft.parts.Count == 0))
                {
                    if (GUILayout.Button("Work out geometry (touching parts, motion axes, assembly order)")) RunGeometry();
                }
            }

            if (_busy && GUILayout.Button("Stop")) _cancel?.Cancel();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_busy))
                {
                    if (GUILayout.Button("Save draft")) SaveDraft(true);
                    if (GUILayout.Button("Load draft")) LoadDraft();
                    if (GUILayout.Button("Correction report")) CorrectionReport();
                    if (GUILayout.Button("Export...")) Export();
                }
            }
        }

        private void DrawReview()
        {
            if (draft.wholeModelOnly)
            {
                EditorGUILayout.HelpBox("Whole model only: just the 'What it is' and 'Summary' fields are exported.", MessageType.None);
                return;
            }
            if (draft.parts.Count == 0) return;

            int reviewed = draft.parts.Count(p => p.reviewed);
            EditorGUILayout.LabelField($"4. Review  ({reviewed} of {draft.parts.Count} checked)", EditorStyles.boldLabel);

            while (expanded.Count < draft.parts.Count) expanded.Add(false);

            for (int i = 0; i < draft.parts.Count; i++)
            {
                PartKnowledgeEntry e = draft.parts[i];
                string title = $"{(e.reviewed ? "[x]" : "[ ]")}  {(string.IsNullOrEmpty(e.label) ? "(not labelled)" : e.label)}   -  {e.node}  ({e.path})";
                expanded[i] = EditorGUILayout.Foldout(expanded[i], title, true);
                if (!expanded[i]) continue;

                EditorGUI.indentLevel++;
                DrawEntry(e);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }
        }

        private void DrawEntry(PartKnowledgeEntry e)
        {
            if (_previews.TryGetValue(e.path, out Texture2D[] pics) && pics != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15);
                    foreach (Texture2D t in pics)
                        if (t != null) GUILayout.Label(t, GUILayout.Width(72), GUILayout.Height(72));
                }
            }

            e.label = EditorGUILayout.TextField("Label", e.label);
            AiHint(e.ai.label, e.label);

            string aliases = string.Join(", ", e.aliases ?? new List<string>());
            string newAliases = EditorGUILayout.TextField("Other names", aliases);
            if (newAliases != aliases) e.aliases = SplitList(newAliases);

            EditorGUILayout.LabelField("What it does");
            e.function = EditorGUILayout.TextArea(e.function, WrapArea, GUILayout.MinHeight(36));
            AiHint(e.ai.function, e.function);

            e.material = EditorGUILayout.TextField("Material", e.material);
            AiHint(e.ai.material, e.material);

            int typeIndex = Math.Max(0, Array.IndexOf(MotionTypes, e.motion.type));
            int newType = EditorGUILayout.Popup("Motion", typeIndex, MotionTypes);
            if (newType != typeIndex) e.motion.type = MotionTypes[newType];
            AiHint(e.ai.motion, e.motion.type);

            if (e.motion.type != "none")
            {
                e.motion.axis = EditorGUILayout.Vector3Field("Axis (part space)", e.motion.axis);
                e.motion.pivot = EditorGUILayout.Vector3Field("Pivot (part space)", e.motion.pivot);
                e.motion.speed = EditorGUILayout.FloatField("Speed", e.motion.speed);
                if (e.motion.type != "rotate") e.motion.range = EditorGUILayout.FloatField(e.motion.type == "hinge" ? "Opens (degrees)" : "Travel (x length)", e.motion.range);
                e.motion.group = EditorGUILayout.TextField("Moves with group", e.motion.group);
            }

            e.assemblyStep = EditorGUILayout.IntField("Assembly step", e.assemblyStep);
            e.removeDirection = EditorGUILayout.Vector3Field("Comes out along (model space)", e.removeDirection);

            if (e.connectsTo != null && e.connectsTo.Count > 0)
            {
                string names = string.Join(", ", e.connectsTo.Select(p => draft.parts.FirstOrDefault(x => x.path == p)?.label ?? p));
                EditorGUILayout.LabelField("Touches", names, EditorStyles.wordWrappedLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                e.reviewed = EditorGUILayout.ToggleLeft("Checked by me", e.reviewed);
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Show in scene", GUILayout.Width(110))) ShowInScene(e.path);
                }
            }
        }

        private static readonly string[] MotionTypes = { "none", "rotate", "slide", "hinge" };

        private static GUIStyle _wrapArea;
        private static GUIStyle WrapArea => _wrapArea ?? (_wrapArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true });

        private static void AiHint(string ai, string current)
        {
            if (string.IsNullOrEmpty(ai) || string.Equals(ai.Trim(), (current ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return;
            EditorGUILayout.LabelField(" ", $"AI said: {ai}", EditorStyles.miniLabel);
        }

        // ── Step 1: part list ──────────────────────────────────────────────────

        private void BuildPartList()
        {
            Transform root = ModelRoot();
            if (root == null) return;
            ResetModelState();

            // A different model starts a fresh draft. Otherwise the previous model's description
            // and summary would carry over, and labelling would skip the new model's own AI call.
            string loadedKey = SpaceSceneManager.CurrentModelKey;
            if (!string.IsNullOrEmpty(draftModelKey) && draftModelKey != loadedKey)
            {
                SaveDraft(false); // keep the old model's work on disk (Load draft brings it back)
                draft = new PartKnowledgeFile();
                _previews.Clear();
            }

            var old = draft.parts
                .GroupBy(p => p.node + "|" + p.path)
                .ToDictionary(g => g.Key, g => g.First());
            var parts = new List<PartKnowledgeEntry>();

            foreach (ModelPartCatalog.Entry entry in ModelPartCatalog.Build(root, out _))
            {
                if (!entry.hasMesh) continue; // groups have no picture of their own
                Transform t = ModelRaycaster.FindPart(root, entry.path);
                if (t == null) continue;

                string key = t.name + "|" + entry.path;
                parts.Add(old.TryGetValue(key, out PartKnowledgeEntry kept)
                    ? kept
                    : new PartKnowledgeEntry { path = entry.path, node = t.name });
            }

            draft.parts = parts;
            draftModelKey = SpaceSceneManager.CurrentModelKey;
            draft.glb = ShortName(draftModelKey);
            expanded = new List<bool>(new bool[parts.Count]);

            if (ModelCatalog.Instance != null)
            {
                ModelCatalog.Instance.FindByGlbUrl(draftModelKey, m =>
                {
                    if (m == null) return;
                    draft.modelId = m.id;
                    if (string.IsNullOrEmpty(draft.context)) draft.context = m.description;
                    Repaint();
                });
            }

            SetStatus($"{parts.Count} part(s) with their own mesh. Kept the labels of any that were already in the draft.", MessageType.Info);
            SaveDraft(false);
        }

        // ── Step 2: AI labels ──────────────────────────────────────────────────

        private async Task RunLabelling(bool relabelAll)
        {
            Transform root = ModelRoot();
            if (root == null || _busy) return;

            int layer = PartRenderer.FindFreeLayer();
            if (layer < 0) { SetStatus("No free layer (8-31) for rendering. Free one in Tags and Layers.", MessageType.Error); return; }

            ResetModelState();
            _busy = true;
            _cancel = new CancellationTokenSource();
            CancellationToken ct = _cancel.Token;
            int calls = 0;

            try
            {
                // The whole model first: its summary goes into every part's prompt.
                if (relabelAll || string.IsNullOrEmpty(draft.summary))
                {
                    SetStatus("Asking about the whole model...", MessageType.Info);
                    var shots = new List<byte[]>();
                    foreach (Vector3 v in PartRenderer.ModelViews)
                        shots.Add(PartRenderer.Render(root, r => true, v, ImageSize, layer, out _));

                    string json = await CallWithRetry(ModelPrompt(), shots, ct);
                    calls++;
                    var reply = JsonUtility.FromJson<AiModelReply>(json);
                    if (reply != null)
                    {
                        if (string.IsNullOrEmpty(draft.context) || relabelAll) draft.context = reply.context;
                        draft.summary = reply.summary;
                    }
                    SaveDraft(false);
                }

                if (!draft.wholeModelOnly)
                {
                    var todo = draft.parts.Where(p => relabelAll || string.IsNullOrEmpty(p.ai.label)).ToList();
                    for (int i = 0; i < todo.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        PartKnowledgeEntry e = todo[i];
                        Transform part = ModelRaycaster.FindPart(root, e.path);
                        if (part == null) continue;

                        if (calls > 0 && _delaySeconds > 0)
                        {
                            SetStatus($"Waiting {_delaySeconds:0}s (free-tier pace)... next: part {i + 1} of {todo.Count}", MessageType.Info);
                            await Task.Delay(TimeSpan.FromSeconds(_delaySeconds), ct);
                        }

                        SetStatus($"Labelling part {i + 1} of {todo.Count}: {e.node}...", MessageType.Info);
                        var pics = new List<byte[]>();
                        var previews = new List<Texture2D>();
                        foreach (Vector3 v in PartRenderer.PartViews)
                        {
                            pics.Add(PartRenderer.Render(root, r => PartRenderer.IsUnder(r, part), v, ImageSize, layer, out Texture2D tex));
                            previews.Add(tex);
                        }
                        pics.Add(PartRenderer.Render(root, r => true, PartRenderer.ModelViews[0], ImageSize, layer, out Texture2D whole));
                        pics.Add(PartRenderer.Render(root, r => !PartRenderer.IsUnder(r, part), PartRenderer.ModelViews[0], ImageSize, layer, out Texture2D without));
                        previews.Add(whole);
                        previews.Add(without);
                        _previews[e.path] = previews.ToArray();
                        pics.RemoveAll(p => p == null);

                        string json = await CallWithRetry(PartPrompt(e, part), pics, ct);
                        calls++;
                        ApplyPartReply(e, JsonUtility.FromJson<AiPartReply>(json));
                        SaveDraft(false);
                        Repaint();
                    }
                }

                SetStatus($"Done: {calls} AI call(s). Now work out the geometry, then review each part.", MessageType.Info);
            }
            catch (OperationCanceledException)
            {
                SetStatus($"Stopped after {calls} call(s). Everything so far is in the draft; press Label again to carry on.", MessageType.Warning);
            }
            catch (GeminiVisionClient.GeminiException ex) when (ex.IsQuota || ex.IsTransient || ex.IsRetired)
            {
                string why = ex.IsQuota ? "Every model in the list is out of free quota for now"
                           : ex.IsRetired ? "The last model in the list is not available to this key"
                           : "Google's free Gemini models are all too busy right now";
                SetStatus($"{why} ({calls} call(s) made). Nothing is lost: the draft is saved. Press Label with AI again in a few minutes and it carries on from the next unlabelled part.\n{ex.Message}", MessageType.Error);
            }
            catch (GeminiVisionClient.GeminiException ex) when (ex.IsBadKey)
            {
                SetStatus($"Gemini refused the API key. Check it at aistudio.google.com/apikey.\n{ex.Message}", MessageType.Error);
            }
            catch (Exception ex)
            {
                SetStatus($"Labelling stopped: {ex.Message}\nThe draft is saved; you can carry on.", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                // Record every model that actually answered (the paper reports which model labelled).
                if (_modelsUsed.Count > 0)
                {
                    var all = new HashSet<string>(SplitList(draft.generatedBy));
                    all.UnionWith(_modelsUsed);
                    draft.generatedBy = string.Join(", ", all);
                    SaveDraft(false);
                }
                _busy = false;
                _cancel?.Dispose();
                _cancel = null;
                Repaint();
            }
        }

        /// <summary>
        /// One AI call that survives Google's free tier being busy. Each model in the list gets up to
        /// three tries when Google says it is busy (503), with growing waits; a model that is out of
        /// quota (429) or retired (404) hands straight over to the next. A bad key stops everything.
        /// </summary>
        private async Task<string> CallWithRetry(string prompt, List<byte[]> pics, CancellationToken ct)
        {
            List<string> models = SplitList(_geminiModel);
            if (models.Count == 0) models = SplitList(DefaultModel);

            int[] waits = { 10, 30 };
            GeminiVisionClient.GeminiException last = null;

            foreach (string model in models)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        string json = await GeminiVisionClient.GenerateJson(_apiKey, model, prompt, pics, ct);
                        _modelsUsed.Add(model);
                        return json;
                    }
                    catch (GeminiVisionClient.GeminiException ex) when (ex.IsTransient && attempt < waits.Length)
                    {
                        last = ex;
                        SetStatus($"Google says {model} is busy (high demand). Waiting {waits[attempt]}s and trying again ({attempt + 2} of 3)...", MessageType.Warning);
                        await Task.Delay(TimeSpan.FromSeconds(waits[attempt]), ct);
                    }
                    catch (GeminiVisionClient.GeminiException ex) when (ex.IsTransient || ex.IsQuota || ex.IsRetired)
                    {
                        last = ex;
                        string why = ex.IsQuota ? "is out of free quota" : ex.IsRetired ? "is not available to this key" : "is still busy";
                        SetStatus($"{model} {why}; trying the next model...", MessageType.Warning);
                        break; // next model
                    }
                }
            }

            throw last ?? new GeminiVisionClient.GeminiException(0, "No Gemini model is set.");
        }

        private readonly HashSet<string> _modelsUsed = new HashSet<string>();

        private string ModelPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are preparing a 3D model for an educational app used by college students.");
            sb.AppendLine("The four images show the whole model from different angles.");
            if (!string.IsNullOrEmpty(draft.context)) sb.AppendLine($"The author says it is: {draft.context}.");
            sb.AppendLine();
            sb.AppendLine("Reply with only this JSON object:");
            sb.AppendLine("{\"context\": \"what this object is, 2 to 6 words\", \"summary\": \"3 or 4 plain sentences for a student: what it is, what it is used for, and how it works\"}");
            return sb.ToString();
        }

        private string PartPrompt(PartKnowledgeEntry e, Transform part)
        {
            Renderer r = part.GetComponent<Renderer>();
            string material = r != null && r.sharedMaterial != null ? r.sharedMaterial.name.Replace(" (Instance)", "") : "";
            var done = draft.parts.Where(p => p != e && !string.IsNullOrEmpty(p.label)).Select(p => p.label).Distinct().ToList();

            var sb = new StringBuilder();
            sb.AppendLine("You are labelling the parts of a 3D model for an educational app used by college students.");
            sb.AppendLine($"The model is: {(string.IsNullOrEmpty(draft.context) ? "unknown" : draft.context)}.");
            if (!string.IsNullOrEmpty(draft.summary)) sb.AppendLine($"About the model: {draft.summary}");
            sb.AppendLine($"It has {draft.parts.Count} parts. You are labelling one of them.");
            sb.AppendLine();
            sb.AppendLine("Images 1 to 3: this part on its own, from three angles.");
            sb.AppendLine("Image 4: the whole model.");
            sb.AppendLine("Image 5: the whole model with this part removed. Compare it with image 4 to see where the part sits.");
            if (!blindTest)
                sb.AppendLine($"In the model file the part is called '{e.node}'{(string.IsNullOrEmpty(material) ? "" : $" and its material is called '{material}'")}. These names may be meaningless.");
            if (done.Count > 0) sb.AppendLine($"Other parts already labelled: {string.Join(", ", done)}. Do not reuse one of these labels unless it is truly the same kind of part.");
            sb.AppendLine();
            sb.AppendLine("Reply with only this JSON object:");
            sb.AppendLine("{\"label\": \"short name, 1 to 4 words\", \"aliases\": [\"up to 4 other names a student might use\"], \"function\": \"what it does, 1 or 2 sentences for a student\", \"material\": \"its likely real-world material\", \"motion\": \"none or rotate or slide or hinge: how it moves when the real object is working\", \"confidence\": 0.0}");
            sb.AppendLine("confidence is 0 to 1: how sure you are of the label.");
            return sb.ToString();
        }

        private static void ApplyPartReply(PartKnowledgeEntry e, AiPartReply reply)
        {
            if (reply == null) return;
            string motion = MotionTypes.Contains(reply.motion) ? reply.motion : "none";

            e.ai = new PartAiProposal
            {
                label = reply.label ?? "",
                aliases = reply.aliases != null ? reply.aliases.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() : new List<string>(),
                function = reply.function ?? "",
                material = reply.material ?? "",
                motion = motion,
                confidence = reply.confidence,
            };

            // Fill the editable fields from the AI's proposal; review then corrects them.
            e.label = e.ai.label;
            e.aliases = new List<string>(e.ai.aliases);
            e.function = e.ai.function;
            e.material = e.ai.material;
            e.motion.type = motion;
            e.reviewed = false;
        }

        [Serializable] private class AiModelReply { public string context; public string summary; }
        [Serializable] private class AiPartReply
        {
            public string label;
            public string[] aliases;
            public string function;
            public string material;
            public string motion;
            public float confidence;
        }

        // ── Step 3: geometry ───────────────────────────────────────────────────

        private void RunGeometry()
        {
            Transform root = ModelRoot();
            if (root == null) return;
            int layer = PartRenderer.FindFreeLayer();
            if (layer < 0) { SetStatus("No free layer (8-31). Free one in Tags and Layers.", MessageType.Error); return; }

            ResetModelState();
            var byTransform = new Dictionary<Transform, PartKnowledgeEntry>();
            foreach (PartKnowledgeEntry e in draft.parts)
            {
                Transform t = ModelRaycaster.FindPart(root, e.path);
                if (t != null) byTransform[t] = e;
            }

            try
            {
                using (var geo = new PartGeometry(root, byTransform.Keys, layer))
                {
                    int i = 0;
                    foreach (var kv in byTransform)
                    {
                        EditorUtility.DisplayProgressBar("Part Labeler", $"Touching parts and motion: {kv.Value.node}", i++ / (float)byTransform.Count * 0.5f);
                        kv.Value.connectsTo = geo.Neighbours(kv.Key)
                            .Where(byTransform.ContainsKey).Select(t => byTransform[t].path).ToList();

                        PartGeometry.EstimateMotion(kv.Key, kv.Value.motion.type, out Vector3 axis, out Vector3 pivot);
                        kv.Value.motion.axis = axis;
                        kv.Value.motion.pivot = pivot;
                        if (kv.Value.motion.type == "hinge" && kv.Value.motion.range <= 0) kv.Value.motion.range = 90f;
                        if (kv.Value.motion.type == "slide" && kv.Value.motion.range <= 0) kv.Value.motion.range = 0.3f;
                    }

                    EditorUtility.DisplayProgressBar("Part Labeler", "Working out the assembly order...", 0.6f);
                    List<PartGeometry.Removal> removal = geo.PlanDisassembly(byTransform.Keys.ToList());

                    // Removal order reversed is assembly order.
                    int n = removal.Count;
                    for (int k = 0; k < n; k++)
                    {
                        PartKnowledgeEntry e = byTransform[removal[k].part];
                        e.assemblyStep = n - k;
                        e.removeDirection = removal[k].directionInModel;
                    }

                    int forced = removal.Count(r => r.forced);
                    SetStatus($"Geometry done. Assembly order worked out for {n} part(s)" +
                              (forced > 0 ? $"; {forced} could not slide out cleanly and were forced (check their step)." : ".") +
                              " Motion axes filled for moving parts.", forced > 0 ? MessageType.Warning : MessageType.Info);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Geometry failed: {ex.Message}", MessageType.Error);
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                SaveDraft(false);
            }
        }

        // ── Step 5: export ─────────────────────────────────────────────────────

        private void Export()
        {
            if (string.IsNullOrEmpty(draft.glb))
            {
                SetStatus("Build the part list first.", MessageType.Warning);
                return;
            }

            int unchecked_ = draft.wholeModelOnly ? 0 : draft.parts.Count(p => !p.reviewed);
            if (unchecked_ > 0 &&
                !EditorUtility.DisplayDialog("Part Labeler", $"{unchecked_} part(s) are not checked yet. Students will see the AI's labels for those. Export anyway?", "Export", "Cancel"))
                return;

            string stem = Path.GetFileNameWithoutExtension(draft.glb).Replace("_mobile", "");
            string path = EditorUtility.SaveFilePanel("Export part knowledge", "", stem + "_parts.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var file = JsonUtility.FromJson<PartKnowledgeFile>(JsonUtility.ToJson(draft)); // copy
            file.generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (file.wholeModelOnly)
            {
                file.parts = new List<PartKnowledgeEntry>();
                file.assemblyOrder = new List<string>();
            }
            else
            {
                file.assemblyOrder = file.parts.Where(p => p.assemblyStep > 0).OrderBy(p => p.assemblyStep).Select(p => p.path).ToList();
            }

            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            SetStatus($"Exported {Path.GetFileName(path)}.\nNext: upload it to Hugging Face next to {draft.glb}, and add \"partsUrl\": \"{Path.GetFileName(path)}\" to that model's entry in models.json.", MessageType.Info);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>How much of the AI's proposal was changed in review: a number for the paper.</summary>
        private void CorrectionReport()
        {
            var labelled = draft.parts.Where(p => !string.IsNullOrEmpty(p.ai.label)).ToList();
            if (labelled.Count == 0) { SetStatus("No AI labels yet.", MessageType.Info); return; }

            int Changed(Func<PartKnowledgeEntry, string> ai, Func<PartKnowledgeEntry, string> now) =>
                labelled.Count(p => !string.Equals((ai(p) ?? "").Trim(), (now(p) ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

            int n = labelled.Count;
            int label = Changed(p => p.ai.label, p => p.label);
            int function = Changed(p => p.ai.function, p => p.function);
            int material = Changed(p => p.ai.material, p => p.material);
            int motion = Changed(p => p.ai.motion, p => p.motion.type);
            int reviewed = labelled.Count(p => p.reviewed);

            string report =
                $"Correction report for {draft.glb} ({n} AI-labelled part(s), {reviewed} checked{(blindTest ? ", blind test" : "")}):\n" +
                $"  label changed:     {label}/{n}  ({100f * label / n:0}%)\n" +
                $"  function changed:  {function}/{n}  ({100f * function / n:0}%)\n" +
                $"  material changed:  {material}/{n}  ({100f * material / n:0}%)\n" +
                $"  motion changed:    {motion}/{n}  ({100f * motion / n:0}%)\n" +
                $"  mean AI confidence: {labelled.Average(p => p.ai.confidence):0.00}";
            Debug.Log("[PartLabeler] " + report);
            SetStatus(report + "\n(Also in the Console.)", MessageType.Info);
        }

        // ── Draft ──────────────────────────────────────────────────────────────

        private string DraftPath()
        {
            string name = string.IsNullOrEmpty(draft.glb) ? "unnamed" : Path.GetFileNameWithoutExtension(draft.glb);
            return Path.Combine(Application.dataPath, "..", "Library", "AuraSyncPartLabeler", name + ".json");
        }

        private void SaveDraft(bool tell)
        {
            try
            {
                string path = DraftPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(new Draft { modelKey = draftModelKey, file = draft }, true));
                if (tell) SetStatus($"Draft saved to {Path.GetFullPath(path)}", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not save the draft: {ex.Message}", MessageType.Error);
            }
        }

        private void LoadDraft()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "AuraSyncPartLabeler"));
            Directory.CreateDirectory(dir);
            string path = EditorUtility.OpenFilePanel("Load Part Labeler draft", dir, "json");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var d = JsonUtility.FromJson<Draft>(File.ReadAllText(path));
                if (d == null || d.file == null) throw new Exception("not a Part Labeler draft");
                draft = d.file;
                draftModelKey = d.modelKey;
                expanded = new List<bool>(new bool[draft.parts.Count]);
                SetStatus($"Loaded draft for {draft.glb}: {draft.parts.Count} part(s).", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not load the draft: {ex.Message}", MessageType.Error);
            }
        }

        [Serializable] private class Draft { public string modelKey; public PartKnowledgeFile file; }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Transform ModelRoot()
        {
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) return null;
            return SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
        }

        /// <summary>Pictures and geometry must see the model as authored: assembled, nothing isolated.</summary>
        private static void ResetModelState()
        {
            ModelIsolator.Instance?.ShowAll();
            if (ModelExploder.Instance != null && ModelExploder.Instance.Amount > 0f)
                ModelExploder.Instance.SetAmount(0f, broadcast: false);
        }

        private static void ShowInScene(string path)
        {
            Transform root = ModelRoot();
            Transform t = ModelRaycaster.FindPart(root, path);
            if (t == null) return;
            Selection.activeTransform = t;
            EditorGUIUtility.PingObject(t);
            ModelPartSelector selector = ModelPartSelector.Instance;
            if (selector != null && selector.LocalPath != path) selector.SelectByPath(path);
        }

        private void SetStatus(string text, MessageType type)
        {
            _status = text;
            _statusType = type;
            Repaint();
        }

        private static List<string> SplitList(string s) =>
            (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        private static string ShortName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try { return Path.GetFileName(new Uri(url).AbsolutePath); }
            catch { return Path.GetFileName(url); }
        }
    }
}
