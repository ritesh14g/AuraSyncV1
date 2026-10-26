using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.AI
{
    /// <summary>
    /// One model's part knowledge file (<c>&lt;Model&gt;_parts.json</c>): what each part is called, what
    /// it does, how it moves, and the order the model comes apart in.
    ///
    /// Made once per model by the Part Labeler (Tools → AuraSync → Part Labeler): an AI proposes
    /// the labels from rendered pictures of each part, geometry works out the motion axes and the
    /// assembly order, and a person reviews everything before it is published next to the .glb on
    /// Hugging Face. The app only ever reads it.
    ///
    /// Written and read with JsonUtility, so: no dictionaries (parts are a list), and Vector3s
    /// appear as {"x":..,"y":..,"z":..}.
    /// </summary>
    [Serializable]
    public class PartKnowledgeFile
    {
        public int version = 1;

        /// <summary>Catalogue id, e.g. "jet-engine".</summary>
        public string modelId = "";

        /// <summary>The .glb file this was made for. Part paths are only valid for that file.</summary>
        public string glb = "";

        /// <summary>What the model is, in a few words: "Turbofan jet engine".</summary>
        public string context = "";

        /// <summary>A few sentences about the whole model, for students and for the AI's prompts.</summary>
        public string summary = "";

        /// <summary>
        /// True for models whose pieces are not real parts (a 3D scan cut into chunks): only the
        /// whole-model fields are filled, and part features stay hidden in the app.
        /// </summary>
        public bool wholeModelOnly;

        /// <summary>
        /// True for machines whose turning parts move no air (gears, an orrery, a rover's wheels): the
        /// Working animation then leaves out the airflow effect, which any model with a rotating part
        /// gets otherwise. Older files lack it, which reads as false, so the jet engine keeps its airflow.
        /// </summary>
        public bool noAirflow;

        /// <summary>The AI model that proposed the labels, e.g. "gemini-2.5-flash".</summary>
        public string generatedBy = "";

        /// <summary>ISO date the file was exported.</summary>
        public string generatedAt = "";

        public List<PartKnowledgeEntry> parts = new List<PartKnowledgeEntry>();

        /// <summary>Part paths in the order the model is put together: first fitted first.</summary>
        public List<string> assemblyOrder = new List<string>();
    }

    [Serializable]
    public class PartKnowledgeEntry
    {
        /// <summary>
        /// The part's position in the hierarchy, e.g. "0/0/4/0" (ModelRaycaster.GetPartPath). This is
        /// what every other feature uses to name a part.
        /// </summary>
        public string path = "";

        /// <summary>
        /// The part's node name in the .glb, e.g. "Object_6.1". A safety check: if the file is
        /// re-optimised and nodes move, the path can be wrong while the name is still right.
        /// </summary>
        public string node = "";

        /// <summary>Short display name: "Fan".</summary>
        public string label = "";

        /// <summary>Other names a student might search for: "intake fan", "front fan".</summary>
        public List<string> aliases = new List<string>();

        /// <summary>What it does, in one or two sentences for a student.</summary>
        public string function = "";

        /// <summary>Its likely real-world material: "Titanium alloy".</summary>
        public string material = "";

        /// <summary>Paths of the parts it touches (worked out from geometry).</summary>
        public List<string> connectsTo = new List<string>();

        public PartMotion motion = new PartMotion();

        /// <summary>1 = fitted first. 0 = not worked out.</summary>
        public int assemblyStep;

        /// <summary>
        /// The direction it slides out in when the model is taken apart, in the model's own space
        /// (not the part's). Zero when not worked out.
        /// </summary>
        public Vector3 removeDirection;

        /// <summary>True once a person has checked this entry.</summary>
        public bool reviewed;

        /// <summary>What the AI proposed, before review. Kept so the paper can report how much needed correcting.</summary>
        public PartAiProposal ai = new PartAiProposal();
    }

    [Serializable]
    public class PartMotion
    {
        /// <summary>"none", "rotate", "slide" or "hinge".</summary>
        public string type = "none";

        /// <summary>Axis it turns or slides along, in the part's own local space.</summary>
        public Vector3 axis;

        /// <summary>Point it turns about, in the part's own local space.</summary>
        public Vector3 pivot;

        /// <summary>Relative speed: 1 = normal. Parts in one group share a speed.</summary>
        public float speed = 1f;

        /// <summary>Hinge: degrees it opens. Slide: travel as a fraction of the part's length.</summary>
        public float range;

        /// <summary>Parts that move together share a group name, e.g. "lowSpool".</summary>
        public string group = "";
    }

    [Serializable]
    public class PartAiProposal
    {
        public string label = "";
        public List<string> aliases = new List<string>();
        public string function = "";
        public string material = "";
        public string motion = "";
        public float confidence;
    }
}
