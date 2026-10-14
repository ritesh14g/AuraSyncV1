namespace AuraSync.Networking
{
    /// <summary>
    /// Every Photon event code AuraSync sends, in one place.
    ///
    /// Two features that pick the same code receive each other's messages and misread them, and
    /// nothing warns you: the bytes just decode as garbage on the other side. Keeping the whole list
    /// here means a new feature can see at a glance which numbers are taken.
    ///
    /// The V2 classes keep their own constant names (<c>CanvaPointerManager.EventCodePointer</c> and so
    /// on) as aliases of these, so nothing that already compiled had to change.
    ///
    /// Photon reserves 200 and above for itself.
    /// </summary>
    public static class EventCodes
    {
        // ── V2 ─────────────────────────────────────────────────────────────────
        public const byte AdminTransform = 1;   // presenter's model transform
        public const byte Voice          = 2;   // built-in voice audio frames
        //                                 3       unused
        public const byte Pointer        = 4;   // Canva-style touch pointers
        public const byte Speaking       = 5;   // voice activity

        // ── V3 (reserved by V3_FEATURE_PLAN.md §2.4) ───────────────────────────
        public const byte ControlOwnership  = 20; // Phase 1:  control request / decline (grants are room properties)
        public const byte SharedAiAnswer    = 21; // Phase 3b: unused - shared answers travel as room properties (SA:)
        public const byte PartNote          = 22; // Phase 5:  unused - notes travel as room properties (PN:)
        public const byte SectionPlane      = 23; // Phase 6:  section plane while dragging
        public const byte Simulation        = 24; // Phase 7:  unused - the state is a room property, animated from the shared clock
        public const byte Attention         = 25; // Phase 8:  view direction + focus point (AttentionSync)
        public const byte LookHerePing      = 26; // Phase 8:  "look here" (AttentionSync)
        public const byte InteractionCounts = 27; // Phase 10: per-part counters to the presenter (InteractionTracker)
        public const byte QuizSent          = 28; // Phase 11: unused - the quiz travels as a room property (Quiz)
        public const byte QuizAnswers       = 29; // Phase 11: each answer to the teacher (QuizSession)
        public const byte FindTask          = 30; // Phase 8:  FIND study task: start / found / end (FindTask)
    }

    /// <summary>
    /// Every room custom-property key AuraSync uses, for the same reason as <see cref="EventCodes"/>:
    /// two features writing the same key silently overwrite each other's state.
    ///
    /// Room properties are for state a late joiner must arrive into. Momentary things travel as
    /// events instead.
    /// </summary>
    public static class RoomKeys
    {
        // ── V2 ─────────────────────────────────────────────────────────────────
        public const string AdminActor       = "AdminActorNumber";
        public const string RoomCode         = "RoomCode";
        public const string CurrentModelUrl  = "CurrentModelUrl";
        public const string CurrentModelName = "CurrentModelName";
        public const string SelectedPart     = "SelectedPart";
        public const string OriginAnchor     = "OriginAnchorId";
        public const string ExplodeAmount    = "ExplodeAmount";

        // ── Fixes 2026-10-12 ───────────────────────────────────────────────────
        /// <summary>Part path the presenter has isolated and chosen to share ("" = whole model).</summary>
        public const string IsolatedPart     = "IsolatedPart";
        /// <summary>
        /// Prefix of the per-model memory: "MS:" + a hash of the model key holds that model's
        /// shared explode / isolation / selection / pose from when the presenter last left it.
        /// </summary>
        public const string ModelStatePrefix = "MS:";

        // ── V3 Phase 1 ─────────────────────────────────────────────────────────
        /// <summary>
        /// Prefix of the control tokens: "CT:explode" etc. hold the actor number of the student who
        /// drives that control, or 0 for the presenter (see ControlOwnership).
        /// </summary>
        public const string ControlPrefix    = "CT:";

        // ── V3 Phase 3b ────────────────────────────────────────────────────────
        /// <summary>
        /// Prefix of shared AI answers: "SA:" + model hash + ":" + id, one key per answer, so two
        /// people sharing at once cannot overwrite each other (see SharedAnswers). null = removed.
        /// </summary>
        public const string SharedAnswerPrefix = "SA:";

        // ── V3 Phase 5 ─────────────────────────────────────────────────────────
        /// <summary>
        /// Prefix of part notes: "PN:" + model hash + ":" + note id + ":" + author's actor number.
        /// One key per note, present while its author is in the room (see PartNoteManager).
        /// </summary>
        public const string PartNotePrefix   = "PN:";

        // ── V3 Phase 6 ─────────────────────────────────────────────────────────
        /// <summary>The shared section cut: float[5] {active, axis, move, tilt, flip} (SectionCutter.CutState).</summary>
        public const string SectionPlane     = "SectionPlane";

        // ── V3 Phase 7 ─────────────────────────────────────────────────────────
        /// <summary>The shared simulation: mode, playing, speed, throttle, phase and start time (SimState).</summary>
        public const string Simulation       = "Simulation";

        // ── V3 Phase 8 ─────────────────────────────────────────────────────────
        /// <summary>The presenter's "cones for everyone" switch: false = no view cones on any phone. Missing = on.</summary>
        public const string AttentionCones   = "AttentionCones";

        // ── V3 Phase 11 ────────────────────────────────────────────────────────
        /// <summary>
        /// The quiz the teacher sent, as JSON (QuizState): questions without their answers while it
        /// runs, so a late joiner can take it; answers and explanations added when it ends.
        /// </summary>
        public const string Quiz             = "Quiz";
    }

    /// <summary>
    /// Player custom-property keys. These live on one player, so they vanish by themselves when that
    /// player leaves: right for "this phone is doing X" flags.
    /// </summary>
    public static class PlayerKeys
    {
        // ── V3 Phase 9 ─────────────────────────────────────────────────────────
        /// <summary>true while this player's phone records the session (everyone shows "REC").</summary>
        public const string Recording = "Rec";
    }
}
