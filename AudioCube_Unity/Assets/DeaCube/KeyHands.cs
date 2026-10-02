using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v8 (the user, replacing v7's cat pianist: "maybe instead of the cat, just do spheres as hands (like floating spheres) (multiple spheres) that move
/// around on the piano and play the notes like bouncing and if there's no notes it just lets the piano key it was on go up and sits there" / "so no
/// more cat"): THE HANDS. Every keyboard island has a few floating spheres that play its notes.
/// ONE SPHERE PER VOICE: each cube that plays on the keyboard gets its own sphere in the cube's colour (the lowest voice the leftmost sphere); a
/// keyboard always has at least <see cref="MinHands"/> (the spare ones in cream, free for the empty hand's presses) and at most <see cref="MaxHands"/>
/// (a fifth voice borrows a free sphere; with none free its note is dropped).
/// PLAYING (a pure function of the song beat, read with the audio's own step math — the score is every audible note of every cube window on the
/// keyboard): a sphere BOUNCES onto every note — it crouches, hops in an arc from the key it sits on to the next key and lands exactly on the note's
/// onset, squashing with a jelly wobble while the key goes down; it holds the key down while the note sounds; when the note ends the key goes back up
/// and the sphere SITS on it until its next note (after the song's last note, or when the song stops, it stays on the key it last played). Fast hops
/// stretch along the motion. Drawn on twos (12 drawings a second), the landings on ones, like the rest of the world.
/// PRESSES: an empty-hand press of a key, or a key a keyboard draft places, sends the nearest free sphere there at once (a quick hop, a landing) and
/// it stays on that key. A seek, a play / stop or a rebuild glides the spheres to where they belong instead of jumping.
/// The spheres are toon-lit with the world's ink outline (DeaCube/Toon, smooth shading), cast shadows on the keys and have no colliders (a click goes
/// through to the key). A manager created at runtime: it follows SongManager (OnSongRebuilt, OnColumnsChanged, OnGroundsChanged, OnKeyRangeChanged),
/// the cubes, the clock and PathManager (OnTilePressed, OnDraftKey), and re-scores every half second as a safety net. It reads islands and cubes and
/// presses keys (the look only); it edits nothing.
/// </summary>
[DefaultExecutionOrder(960)]   // after the islands and the keys moved this frame
public class KeyHands : MonoBehaviour
{
    // ------------------------------------------------------------------ the look
    /// <summary>Spheres per keyboard: at least MinHands, at most MaxHands.</summary>
    public const int MinHands = 2, MaxHands = 4;
    /// <summary>A sphere's radius: RadiusOfKey × the white keys' pitch, clamped to RMin..RMax world units.</summary>
    public const float RadiusOfKey = 0.42f, RMin = 0.08f, RMax = 0.26f;
    /// <summary>A spare sphere's colour (no voice of its own) and how far a voice's sphere is lifted toward cream from its cube's colour.</summary>
    public static readonly Color SpareColor = Comic.Cream;
    public const float CreamLift = 0.12f;
    /// <summary>Drawings per second (on twos); within OnesS of a landing it draws on ones.</summary>
    public const float PoseFps = 12f, OnesS = 0.15f;
    // timing (seconds; converted to beats with the tempo)
    /// <summary>A hop takes half the time between two onsets, clamped to MinHopS..MaxHopS (never more than the time left after MinDownS on the key
    /// before); the crouch before a hop from a key the sphere sits on lasts up to CrouchS; a landing's jelly wobble decays in WobbleS.</summary>
    public const float MinHopS = 0.09f, MaxHopS = 0.32f, MinDownS = 0.05f, CrouchS = 0.07f, WobbleS = 0.09f;
    /// <summary>A hop's height: HopBase × the radius + HopPer × the distance, at most HopMax × the radius.</summary>
    public const float HopBase = 1.1f, HopPer = 0.22f, HopMax = 3.2f;
    /// <summary>The landing squash (fraction of the height), the crouch, the longest stretch along a fast hop, a sitting sphere's breath.</summary>
    public const float Squash = 0.34f, Crouch = 0.2f, StretchMax = 0.45f, Breath = 0.025f;
    /// <summary>A press / a draft key: the hop there (s), how long the sphere holds the key down after landing (s).</summary>
    public const float LiveHopS = 0.11f, LiveHoldS = 0.16f;
    /// <summary>A seek / play / stop / rebuild: the glide to where a sphere belongs (s).</summary>
    public const float GlideS = 0.18f;

    // ------------------------------------------------------------------ the pose (a pure function of the song beat while playing)
    public enum Phase { Sit, Crouch, Air, Down, Live }

    /// <summary>One sphere's pose: what it does, on / toward which key, and where its bottom meets it.</summary>
    public struct HandPose
    {
        public Phase phase;
        /// <summary>The key the sphere is on (Sit / Crouch / Down / Live) or flying to (Air); null only on a keyboard with no keys.</summary>
        public TileInteraction key;
        /// <summary>The key a hop left from (Air).</summary>
        public TileInteraction from;
        /// <summary>Progress of a hop (0..1).</summary>
        public float u;
        /// <summary>The onset (song beat) of the note it plays / flies to; NaN when none.</summary>
        public float on;
        /// <summary>+ squashed (a landing, a crouch) .. − stretched up (the wobble's rebound), as a fraction of the height.</summary>
        public float squash;
        /// <summary>True while it holds a grid note's key down (a layer cube's note leaves the key still).</summary>
        public bool holds;
        /// <summary>World point under the sphere: on the key at rest (Sit / Crouch / Down / Live), on the arc (Air).</summary>
        public Vector3 contact;
    }

    // ------------------------------------------------------------------ the manager
    public static KeyHands I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { Ensure(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { LivePresses = 0; DraftKeys = 0; }

    /// <summary>The instance (created on first use in Play mode).</summary>
    public static KeyHands Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<KeyHands>();
            if (I == null && Application.isPlaying) I = new GameObject("KeyHands").AddComponent<KeyHands>();
        }
        return I;
    }

    /// <summary>Keyboards with hands (one set per keyboard island).</summary>
    public static int Count => I != null ? I.sets.Count : 0;
    /// <summary>The sets, one per keyboard island (Islands order).</summary>
    public static IReadOnlyList<Set> Sets => I != null ? (IReadOnlyList<Set>)I.sets : Array.Empty<Set>();
    /// <summary>The hands of keyboard <paramref name="kb"/> (null when it has none).</summary>
    public static Set Of(KeyBlock kb)
    {
        if (I == null || kb == null) return null;
        foreach (var s in I.sets) if (s.Island == kb) return s;
        return null;
    }
    /// <summary>Tests: re-syncs the sets with the song and re-scores them now.</summary>
    public static void RefreshNow()
    {
        var k = Ensure(); if (k == null) return;
        k.Sync();
        foreach (var s in k.sets) s.Rescore();
    }
    /// <summary>Main-thread milliseconds of the last frame (all keyboards) and the running average per keyboard.</summary>
    public static double MsLast => I != null ? I.msLast : 0.0;
    public static double MsPerKeyboard { get { if (I == null || I.sets.Count == 0) return 0.0; double s = 0; foreach (var c in I.sets) s += c.AvgMs; return s / I.sets.Count; } }
    /// <summary>Empty-hand presses and keyboard-draft keys the spheres played (tests).</summary>
    public static int LivePresses, DraftKeys;

    readonly List<Set> sets = new List<Set>();
    SongManager hooked;
    bool syncDirty = true;
    float sigClock;
    int sig;
    double msLast;
    readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
    static Mesh sphereMesh;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void OnEnable()
    {
        AudioCube.OnAnyChanged += HandleCube;
        PathManager.OnTilePressed += HandlePress;
        PathManager.OnDraftKey += HandleDraftKey;
        GlobalClock.OnLoop += HandleClock; GlobalClock.OnTempoChanged += HandleClock; GlobalClock.OnMeterChanged += HandleClock; GlobalClock.OnRegionChanged += HandleClock;
    }

    void OnDisable()
    {
        AudioCube.OnAnyChanged -= HandleCube;
        PathManager.OnTilePressed -= HandlePress;
        PathManager.OnDraftKey -= HandleDraftKey;
        GlobalClock.OnLoop -= HandleClock; GlobalClock.OnTempoChanged -= HandleClock; GlobalClock.OnMeterChanged -= HandleClock; GlobalClock.OnRegionChanged -= HandleClock;
        Unhook();
    }

    void OnDestroy()
    {
        foreach (var s in sets) s.Dispose();
        sets.Clear();
        if (I == this) I = null;
    }

    void Hook()
    {
        var sm = SongManager.I;
        if (sm == hooked) return;
        Unhook();
        if (sm == null) return;
        sm.OnSongRebuilt += HandleSong;
        sm.OnColumnsChanged += HandleSong;
        sm.OnGroundsChanged += HandleSong;
        sm.OnKeyRangeChanged += HandleKeys;
        hooked = sm;
        syncDirty = true;
    }

    void Unhook()
    {
        if (hooked == null) return;
        hooked.OnSongRebuilt -= HandleSong;
        hooked.OnColumnsChanged -= HandleSong;
        hooked.OnGroundsChanged -= HandleSong;
        hooked.OnKeyRangeChanged -= HandleKeys;
        hooked = null;
    }

    void HandleSong() { syncDirty = true; foreach (var s in sets) s.MarkDirty(); }
    void HandleKeys(KeyBlock kb) { syncDirty = true; var s = Of(kb); if (s != null) s.MarkDirty(); }
    void HandleCube(AudioCube cube) { foreach (var s in sets) s.MarkDirty(); }
    void HandleClock() { foreach (var s in sets) s.MarkDirty(); }
    void HandlePress(TileInteraction t)
    {
        if (t == null || t.island == null || !t.island.IsKeyboard) return;
        var s = Of(t.island);
        if (s != null && s.LivePress(t)) LivePresses++;
    }
    /// <summary>A key a keyboard draft just added and sounded (PathManager.OnDraftKey): a sphere plays the note being placed.</summary>
    void HandleDraftKey(TileInteraction t)
    {
        if (t == null || t.island == null || !t.island.IsKeyboard) return;
        var s = Of(t.island);
        if (s != null && s.LivePress(t)) DraftKeys++;
    }

    /// <summary>A hash of the keyboard islands (a keyboard that came or went without an event still gets / loses its hands within 0.3 s).</summary>
    static int Signature()
    {
        var sm = SongManager.I;
        if (sm == null) return 0;
        unchecked
        {
            int h = 17;
            foreach (var kb in sm.Islands) if (kb != null && kb.IsKeyboard) h = h * 31 + kb.GetInstanceID();
            return h;
        }
    }

    /// <summary>One set per keyboard island of the song; sets of islands that are gone (a rebuild re-creates every island) are removed.</summary>
    void Sync()
    {
        syncDirty = false;
        sig = Signature();
        var sm = SongManager.I;
        for (int i = sets.Count - 1; i >= 0; i--)
        {
            var kb = sets[i].Island;
            if (kb == null || !kb.IsKeyboard || sm == null || sm.Islands.IndexOf(kb) < 0) { sets[i].Dispose(); sets.RemoveAt(i); }
        }
        if (sm == null) return;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || !kb.IsKeyboard || Of(kb) != null) continue;
            sets.Add(new Set(kb, transform));
        }
        sets.Sort((x, y) => sm.Islands.IndexOf(x.Island).CompareTo(sm.Islands.IndexOf(y.Island)));   // Islands order (stable for tests)
    }

    void LateUpdate()
    {
        sw.Reset(); sw.Start();
        Hook();
        sigClock += Time.unscaledDeltaTime;
        if (sigClock > 0.3f) { sigClock = 0f; if (Signature() != sig) syncDirty = true; }
        if (syncDirty) Sync();
        foreach (var s in sets) s.Tick();
        sw.Stop();
        msLast = sw.Elapsed.TotalMilliseconds;
    }

    static Mesh SphereMesh
    {
        get
        {
            if (sphereMesh == null)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // the built-in sphere (diameter 1); its collider never lives a frame
                sphereMesh = g.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(g);
            }
            return sphereMesh;
        }
    }

    static float Hash01(int x)
    {
        unchecked
        {
            uint h = (uint)x * 0x9E3779B1u; h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; h *= 0xC2B2AE3Du; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

    // ------------------------------------------------------------------ one keyboard's hands
    internal struct Note { public float on, off; public TileInteraction tile; public int cube; public bool grid, wrapped; public float x; }

    /// <summary>One sphere: its notes, its voice, its look and what it drew last.</summary>
    public sealed class Hand
    {
        /// <summary>The sphere's GameObject (a child of the KeyHands manager, never of the island).</summary>
        public Transform Root => go != null ? go.transform : null;
        /// <summary>The cube whose voice this sphere plays (0 = a spare sphere) and its colour.</summary>
        public int Voice { get; internal set; }
        public Color Color { get; internal set; }
        /// <summary>The sphere's centre and radius as drawn, its scale along its squash / stretch axis (1 = round) and the key it is on or flying to.</summary>
        public Vector3 Center { get; internal set; }
        public float Radius { get; internal set; }
        public float AxisScale { get; internal set; } = 1f;
        public TileInteraction Key => shown.key;
        /// <summary>The pose drawn this frame.</summary>
        public HandPose Shown => shown;
        /// <summary>Notes on this sphere (wrapped copies included) and one of them (tests).</summary>
        public int NoteCount => notes.Count;
        public bool NoteOf(int i, out float on, out float off, out TileInteraction tile, out bool wrapped)
        {
            if (i < 0 || i >= notes.Count) { on = off = 0f; tile = null; wrapped = false; return false; }
            var n = notes[i]; on = n.on; off = n.off; tile = n.tile; wrapped = n.wrapped; return true;
        }
        /// <summary>The key it sits on when the song is stopped (the last key it played or was sent to).</summary>
        public TileInteraction RestKey => restKey;

        internal readonly List<Note> notes = new List<Note>();
        internal TileInteraction restKey;
        internal HandPose shown;
        internal bool hasDrawn;
        internal Vector3 glideFrom; internal float glideT = 99f; internal bool glidePending;
        internal TileInteraction liveKey; internal float liveT0 = -99f; internal Vector3 liveFrom; internal bool liveOn;
        internal float meanX;
        readonly GameObject go; readonly Material mat; readonly MeshRenderer mr;

        internal Hand(Transform parent, string name)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh;
            mr = go.AddComponent<MeshRenderer>();
            mat = Fx.Lit(SpareColor, 0.55f);
            if (mat.HasProperty("_FaceSnap")) mat.SetFloat("_FaceSnap", 0f);   // round: smooth shading, not box faces
            mat.name = "KeyHand";
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Color = SpareColor;
        }

        internal void SetColor(Color c) { if (c == Color) return; Color = c; mat.SetColor("_BaseColor", c); }
        internal void SetVisible(bool on) { if (go.activeSelf != on) go.SetActive(on); }
        internal void Place(Vector3 centre, Quaternion rot, Vector3 scale)
        {
            var t = go.transform;
            t.SetPositionAndRotation(centre, rot);
            t.localScale = scale;
        }
        internal Bounds WorldBounds => mr != null ? mr.bounds : new Bounds();
        /// <summary>Asks for a glide: the next drawing eases from where the sphere is to its new spot when that is more than half a radius away
        /// (a glide already under way just keeps easing toward the new target).</summary>
        internal void StartGlide() { if (hasDrawn) glidePending = true; }
        internal void Dispose()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            if (mat != null) UnityEngine.Object.Destroy(mat);
        }
    }

    /// <summary>The hands of one keyboard island: the score split into voices, a sphere per voice, the pose and the drawing.</summary>
    public sealed class Set
    {
        public KeyBlock Island { get; private set; }
        /// <summary>The spheres (left to right by their voices' keys).</summary>
        public IReadOnlyList<Hand> Hands => hands;
        /// <summary>Notes in the score (wrapped copies apart), the notes no sphere was free for, the voices (cubes) heard, re-scores so far.</summary>
        public int NoteCount { get; private set; }
        public int Dropped { get; private set; }
        public int Voices { get; private set; }
        public int Rescores { get; private set; }
        /// <summary>v9 perf: a hash of the score's notes (onset, end, key, voice): changes only when the music on the keyboard changed (the 0.5 s
        /// safety re-read leaves it alone; KeyStage keys its runs on it).</summary>
        public int ScoreSig { get; private set; }
        /// <summary>Milliseconds of this keyboard's last frame and the running average.</summary>
        public double LastMs { get; private set; }
        public double AvgMs { get; private set; }
        /// <summary>A sphere's radius on this keyboard (world units).</summary>
        public float Radius => radius;
        /// <summary>Times the spheres were drawn (tests).</summary>
        public int Builds { get; private set; }
        /// <summary>World bounds of the drawn spheres.</summary>
        public Bounds DrawnBounds
        {
            get
            {
                bool any = false; var b = new Bounds();
                foreach (var h in hands) { if (!h.hasDrawn) continue; var hb = h.WorldBounds; if (!any) { b = hb; any = true; } else b.Encapsulate(hb); }
                return b;
            }
        }

        readonly List<Hand> hands = new List<Hand>();
        readonly Transform parent;
        readonly List<Note> tmp = new List<Note>();
        readonly Dictionary<int, AudioCube> cubeOf = new Dictionary<int, AudioCube>();
        readonly List<int> voiceIds = new List<int>();
        readonly Dictionary<int, float> voiceX = new Dictionary<int, float>();
        bool dirty = true; float scoredAt = -99f;
        float lastPose = float.NegativeInfinity; bool wasPlaying, frozen; float frozenBeat;
        float radius = 0.18f, halfKeys = 3.6f;
        Vector3 isUp = Vector3.up, isFwd = Vector3.forward, isRight = Vector3.right, mid;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        internal Set(KeyBlock kb, Transform parent)
        {
            Island = kb; this.parent = parent;
        }

        internal void Dispose() { foreach (var h in hands) h.Dispose(); hands.Clear(); }
        internal void MarkDirty() { dirty = true; }

        // ---------------------------------------------------------------- the score
        /// <summary>Re-reads the keyboard's notes from the cubes (the audio's own step math), splits them into voices and gives every voice a sphere.</summary>
        public void Rescore()
        {
            dirty = false; scoredAt = Time.unscaledTime; Rescores++;
            tmp.Clear(); cubeOf.Clear(); voiceIds.Clear(); voiceX.Clear();
            var kb = Island;
            if (kb == null) { NoteCount = 0; return; }
            UpdateFrame();
            var cubes = SequenceMaster.Cubes;
            for (int ci = 0; ci < cubes.Count; ci++)
            {
                var cube = cubes[ci];
                if (cube == null || !cube.isFinalized || cube.Hologram || cube.nodes.Count == 0 || cube.IsOnMoon) continue;
                var wins = cube.windows;
                for (int w = 0; w < wins.Count; w++)
                {
                    var win = wins[w];
                    if (win.silent) continue;
                    var isl = win.island != null ? win.island : cube.Island;
                    if (isl != kb) continue;
                    AddWindow(cube, w, win);
                }
            }
            NoteCount = tmp.Count;
            tmp.Sort((x, y) => x.on != y.on ? x.on.CompareTo(y.on) : x.x.CompareTo(y.x));
            unchecked
            {
                int sig = 17 + tmp.Count;
                foreach (var n in tmp) { sig = sig * 31 + Mathf.RoundToInt(n.on * 64f); sig = sig * 31 + Mathf.RoundToInt(n.off * 64f); sig = sig * 31 + (n.tile != null ? n.tile.gridX : -1); sig = sig * 31 + n.cube; sig = sig * 31 + (n.wrapped ? 1 : 0); }
                ScoreSig = sig;
            }
            // the voices, left to right by their mean key
            var sums = new Dictionary<int, Vector2>();
            foreach (var n in tmp) { Vector2 s; sums.TryGetValue(n.cube, out s); sums[n.cube] = s + new Vector2(n.x, 1f); }
            foreach (var kv in sums) { voiceIds.Add(kv.Key); voiceX[kv.Key] = kv.Value.x / Mathf.Max(1f, kv.Value.y); }
            voiceIds.Sort((p, q) => voiceX[p] != voiceX[q] ? voiceX[p].CompareTo(voiceX[q]) : p.CompareTo(q));
            Voices = voiceIds.Count;
            EnsureHands(Mathf.Clamp(Mathf.Max(MinHands, voiceIds.Count), MinHands, MaxHands));
            Assign();
            Wrap();
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                // the colour: the voice's cube (a little lifted toward cream), a spare sphere cream
                AudioCube c;
                h.SetColor(h.Voice != 0 && cubeOf.TryGetValue(h.Voice, out c) && c != null ? Color.Lerp(c.Color, Comic.Cream, CreamLift) : SpareColor);
                // where it waits: the key it sat on (if still there), else its first note's key, else spread over the white keys
                if (!KeyAlive(h.restKey))
                {
                    TileInteraction first = null;
                    foreach (var n in h.notes) if (!n.wrapped) { first = n.tile; break; }
                    h.restKey = first != null ? first : SpreadKey(i, hands.Count);
                }
            }
        }

        void AddWindow(AudioCube cube, int w, AudioCube.Window win)
        {
            float length = win.length;
            int pass = cube.PassFor(w, 0);
            bool dur = cube.HasDurations;
            bool region = GlobalClock.HasRegion;
            float r0 = (float)GlobalClock.LoopStartBeat, r1 = (float)GlobalClock.LoopEndBeat;
            int id = cube.id != 0 ? cube.id : cube.GetInstanceID();
            cubeOf[id] = cube;
            int last = -1;
            for (int k = 0; k < 4096; k++)
            {
                float s = cube.StepStartLocalBeat(k);
                if (s >= length - 1e-4f) break;
                var h = cube.Decide(w, k, pass);
                if (!h.hit) continue;
                float e;
                if (dur) e = Mathf.Min(length, cube.StepStartLocalBeat(k + 1));
                else { int nh = cube.NextPatternHitStep(k, length); e = nh >= 0 ? cube.StepStartLocalBeat(nh) : length; }
                if (!h.fires) continue;
                var t = cube.TileAt(w, h.node);
                if (t == null || t.island != Island) continue;
                float on = win.start + s, off = win.start + Mathf.Max(s + 0.01f, e);
                if (region && (on < r0 - 1e-4f || on >= r1)) continue;
                // a tie on the same key holds the note before (no new bounce)
                if (h.tie && last >= 0 && tmp[last].tile == t && Mathf.Abs(tmp[last].off - on) < 1e-3f) { var n0 = tmp[last]; n0.off = off; tmp[last] = n0; continue; }
                tmp.Add(new Note { on = on, off = off, tile = t, cube = id, grid = cube.layer == 0, x = RelX(t.Top) });
                last = tmp.Count - 1;
            }
        }

        /// <summary>A sphere per voice (left to right); a voice past MaxHands (or a note whose sphere is busy) borrows the free sphere nearest its key;
        /// with none free the note is dropped.</summary>
        void Assign()
        {
            foreach (var h in hands) { h.notes.Clear(); h.Voice = 0; }
            for (int v = 0; v < voiceIds.Count && v < hands.Count; v++) { hands[v].Voice = voiceIds[v]; hands[v].meanX = voiceX[voiceIds[v]]; }
            for (int v = voiceIds.Count; v < hands.Count; v++) hands[v].meanX = hands.Count > 1 ? -1f + 2f * v / (hands.Count - 1) : 0f;
            var busy = new float[hands.Count]; var lastX = new float[hands.Count];
            for (int i = 0; i < hands.Count; i++) { busy[i] = float.NegativeInfinity; lastX[i] = hands[i].meanX; }
            int dropped = 0;
            foreach (var n in tmp)
            {
                int want = -1;
                for (int i = 0; i < hands.Count; i++) if (hands[i].Voice == n.cube) { want = i; break; }
                if (want < 0 || busy[want] > n.on + 1e-3f)
                {
                    want = -1; float best = float.MaxValue;
                    for (int i = 0; i < hands.Count; i++)
                    {
                        if (busy[i] > n.on + 1e-3f) continue;
                        float d = Mathf.Abs(lastX[i] - n.x) + (hands[i].Voice != 0 && hands[i].Voice != n.cube ? 0.5f : 0f);   // a spare sphere first
                        if (d < best) { best = d; want = i; }
                    }
                }
                if (want < 0) { dropped++; continue; }
                busy[want] = n.off; lastX[want] = n.x;
                hands[want].notes.Add(n);
            }
            Dropped = dropped;
        }

        /// <summary>A looping song (or focus loop): the first notes again one loop later and the last ones one loop earlier, so a sphere heads for the
        /// loop's first note before the wrap and lands on it exactly at the loop point.</summary>
        void Wrap()
        {
            if (!GlobalClock.LoopSong) return;
            float s0 = (float)GlobalClock.LoopStartBeat, s1 = (float)GlobalClock.LoopEndBeat, len = s1 - s0;
            if (len < 0.5f) return;
            float edge = Mathf.Min(4f, len * 0.5f);
            foreach (var h in hands)
            {
                var L = h.notes; int n = L.Count;
                for (int i = 0; i < n; i++)
                {
                    var x = L[i];
                    if (x.on < s0 + edge) { var c = x; c.on += len; c.off += len; c.wrapped = true; L.Add(c); }
                    if (x.on >= s1 - edge) { var c = x; c.on -= len; c.off -= len; c.wrapped = true; L.Add(c); }
                }
                L.Sort((p, q) => p.on.CompareTo(q.on));
            }
        }

        void EnsureHands(int n)
        {
            while (hands.Count > n) { hands[hands.Count - 1].Dispose(); hands.RemoveAt(hands.Count - 1); }
            while (hands.Count < n) hands.Add(new Hand(parent, "KeyHand " + Island.measureIndex + "." + hands.Count));
        }

        static int LastAtOrBefore(List<Note> L, float b)
        {
            int lo = 0, hi = L.Count - 1, r = -1;
            while (lo <= hi) { int m = (lo + hi) >> 1; if (L[m].on <= b + 1e-5f) { r = m; lo = m + 1; } else hi = m - 1; }
            return r;
        }

        /// <summary>The latest onset in (from, to] on any sphere (the stepping never skips a landing); −∞ when none.</summary>
        float LatestOnset(float from, float to)
        {
            float best = float.NegativeInfinity;
            foreach (var h in hands)
            {
                int i = LastAtOrBefore(h.notes, to);
                if (i >= 0 && h.notes[i].on > from && h.notes[i].on > best) best = h.notes[i].on;
            }
            return best;
        }

        // ---------------------------------------------------------------- the frame of reference (live: keys, island)
        void UpdateFrame()
        {
            var kb = Island; if (kb == null) return;
            var t = kb.transform;
            isUp = t.up; isFwd = t.forward; isRight = t.right;
            float e = KeyBlock.EdgeInset;
            halfKeys = Mathf.Max(1f, 0.5f * kb.KeysCaseWidth - ProjectConfig.KeyPadX);   // v8: the keys' span (a long keyboard's case is centred, one measure wide)
            mid = t.TransformPoint(new Vector3(-e + kb.Width * 0.5f, 0f, 0f));
            int white = 0;
            foreach (var k in kb.tiles) if (k != null && !k.IsBlackKey) white++;
            float pitch = 2f * halfKeys / Mathf.Max(1, white);
            radius = Mathf.Clamp(RadiusOfKey * pitch, RMin, RMax);
        }

        /// <summary>A key's x from the keys' middle as a fraction of their half span (−1 the lowest end .. +1 the highest).</summary>
        float RelX(Vector3 world) => Vector3.Dot(world - mid, isRight) / halfKeys;

        static bool KeyAlive(TileInteraction t) => t != null && t.island != null && t.gameObject.activeInHierarchy;

        /// <summary>Sphere <paramref name="i"/> of <paramref name="n"/>'s waiting key with no notes: spread evenly over the white keys.</summary>
        TileInteraction SpreadKey(int i, int n)
        {
            var kb = Island; if (kb == null) return null;
            var whites = new List<TileInteraction>();
            foreach (var k in kb.tiles) if (k != null && !k.IsBlackKey) whites.Add(k);
            if (whites.Count == 0) return kb.tiles.Count > 0 ? kb.tiles[0] : null;
            int at = Mathf.Clamp(Mathf.RoundToInt((i + 1f) / (n + 1f) * (whites.Count - 1)), 0, whites.Count - 1);
            return whites[at];
        }

        /// <summary>Where a sphere sits on key <paramref name="t"/> (its rest top): toward its FRONT end (a white key in front of the black keys).</summary>
        public Vector3 ContactOf(TileInteraction t)
        {
            if (t == null) return mid;
            float len = 1.6f;
            var bc = t.GetComponent<BoxCollider>();
            if (bc != null) len = bc.size.z;
            return t.Top - isFwd * (len * (t.IsBlackKey ? 0.22f : 0.3f));
        }

        /// <summary>The key's sink (and hover lift) right now: the sphere rides the key.</summary>
        static Vector3 KeyShift(TileInteraction t)
        {
            if (t == null) return Vector3.zero;
            return t.transform.position + t.transform.up * (ProjectConfig.TileThickness * 0.5f) - t.Top;
        }

        Vector3 Arc(Vector3 from, Vector3 to, float u)
        {
            u = Mathf.Clamp01(u);
            float d = Vector3.Distance(from, to);
            float h = Mathf.Min(HopBase * radius + HopPer * d, HopMax * radius);
            return Vector3.Lerp(from, to, Smooth(u)) + isUp * (h * 4f * u * (1f - u));
        }

        // ---------------------------------------------------------------- the pose
        static float Bps => (float)Math.Max(0.1, GlobalClock.BeatsPerSecond);

        /// <summary>Sphere <paramref name="i"/>'s pose at song beat <paramref name="beat"/> while playing (a pure function of the beat and the song).</summary>
        public HandPose PoseAt(int i, float beat) { UpdateFrame(); if (dirty) Rescore(); return i >= 0 && i < hands.Count ? Evaluate(hands[i], beat) : default(HandPose); }

        /// <summary>Tests / captures: draws the pose of song beat <paramref name="beat"/> now; the next frame draws the live pose again unless
        /// <see cref="Freeze"/> holds it.</summary>
        public void DrawAt(float beat)
        {
            UpdateFrame(); if (dirty) Rescore();
            foreach (var h in hands) { h.glideT = 99f; h.liveOn = false; Draw(h, Evaluate(h, beat), beat, true); }
            Builds++;
        }
        public void Freeze(float beat) { frozen = true; frozenBeat = beat; }
        public void Unfreeze() { frozen = false; }
        public bool Frozen => frozen;

        /// <summary>Sitting on key <paramref name="t"/> (the key up), breathing a little.</summary>
        HandPose Sit(TileInteraction t, float sec)
        {
            return new HandPose { phase = Phase.Sit, key = t, on = float.NaN, contact = ContactOf(t), squash = Breath * Mathf.Sin(sec * Mathf.PI * 2f / 3.1f) };
        }

        HandPose Evaluate(Hand h, float b)
        {
            float bps = Bps;
            var L = h.notes; int n = L.Count;
            if (n == 0) return Sit(KeyAlive(h.restKey) ? h.restKey : null, b / bps);
            int i = LastAtOrBefore(L, b);
            bool hasCur = i >= 0, hasNext = i + 1 < n;
            Note cur = hasCur ? L[i] : default(Note), nx = hasNext ? L[i + 1] : default(Note);
            TileInteraction seat = hasCur ? cur.tile : nx.tile;   // before its first note it waits on that note's key
            if (hasNext)
            {
                float avail = hasCur ? nx.on - cur.on - MinDownS * bps : float.PositiveInfinity;
                float span = hasCur ? nx.on - cur.on : MaxHopS * bps * 2f;
                float hop = Mathf.Clamp(span * 0.5f, MinHopS * bps, MaxHopS * bps);
                hop = Mathf.Max(Mathf.Min(hop, avail), 0.02f * bps);
                float hs = nx.on - hop;
                if (b >= hs)
                {
                    float u = Mathf.Clamp01((b - hs) / hop);
                    return new HandPose { phase = Phase.Air, key = nx.tile, from = seat, u = u, on = nx.on, contact = Arc(ContactOf(seat), ContactOf(nx.tile), u) };
                }
                if (hasCur && b < cur.off) return Down(cur, b, bps);
                // sitting on the key it played (the key back up); a crouch just before it takes off
                float since = hasCur ? Mathf.Max(cur.off, cur.on + MinDownS * bps) : float.NegativeInfinity;
                float crouch = Mathf.Min(CrouchS * bps, Mathf.Max(0f, hs - since));
                var p = Sit(seat, b / bps);
                if (crouch > 1e-4f && b >= hs - crouch) { p.phase = Phase.Crouch; p.on = nx.on; p.squash = Crouch * Smooth((b - (hs - crouch)) / crouch); }
                return p;
            }
            if (hasCur && b < cur.off) return Down(cur, b, bps);
            return Sit(seat, b / bps);   // after its last note: it stays on that key
        }

        /// <summary>On key n's note: landed at its onset (a squash with a jelly wobble), holding the key down while it sounds.</summary>
        HandPose Down(Note nt, float b, float bps)
        {
            float t = Mathf.Max(0f, b - nt.on) / bps;   // seconds since the landing
            float wob = Mathf.Exp(-t / WobbleS) * Mathf.Cos(t * 30f);
            return new HandPose { phase = Phase.Down, key = nt.tile, on = nt.on, contact = ContactOf(nt.tile), squash = Squash * wob, holds = nt.grid };
        }

        // ---------------------------------------------------------------- presses: the empty hand's and a keyboard draft's keys
        /// <summary>Key <paramref name="t"/> of this keyboard was pressed (or placed into a draft): the nearest sphere not holding a song note hops there
        /// at once, lands, holds it LiveHoldS and stays on it. False when the key is not this keyboard's.</summary>
        public bool LivePress(TileInteraction t)
        {
            if (t == null || t.island != Island) return false;
            UpdateFrame();
            if (dirty) Rescore();
            if (hands.Count == 0) return false;
            Vector3 k = ContactOf(t);
            int best = -1; float bd = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
                for (int i = 0; i < hands.Count; i++)
                {
                    var h = hands[i];
                    if (pass == 0 && GlobalClock.IsPlaying && h.shown.phase == Phase.Down) continue;   // first: a sphere that is not playing a note
                    float d = h.hasDrawn ? Vector3.Distance(h.Center, k) : Mathf.Abs(h.meanX - RelX(t.Top));
                    if (h.liveOn && h.liveKey == t) d = -1f;   // the same key again: the sphere already there
                    if (d < bd) { bd = d; best = i; }
                }
            var hh = hands[best];
            // from where it is drawn (its bottom); already sitting on that key: no hop, just the press
            hh.liveFrom = hh.hasDrawn && !(hh.shown.key == t && hh.shown.phase != Phase.Air) ? hh.Center - isUp * hh.Radius : k;
            hh.liveKey = t; hh.liveT0 = Time.unscaledTime; hh.liveOn = true; hh.restKey = t;
            return true;
        }

        /// <summary>A press in progress: the hop there (real time), the landing, the hold; then it sits there (false: the press is over).</summary>
        bool LivePose(Hand h, out HandPose p)
        {
            p = default(HandPose);
            if (!h.liveOn) return false;
            float age = Time.unscaledTime - h.liveT0;
            if (!KeyAlive(h.liveKey) || age >= LiveHopS + LiveHoldS) { h.liveOn = false; h.StartGlide(); return false; }   // playing: glide back into the song
            Vector3 k = ContactOf(h.liveKey);
            bool hop = Vector3.Distance(h.liveFrom, k) > 1e-3f;
            if (hop && age < LiveHopS)
            {
                float u = age / LiveHopS;
                float d = Vector3.Distance(h.liveFrom, k), ht = Mathf.Min(0.8f * radius + 0.15f * d, 2f * radius);
                p = new HandPose { phase = Phase.Air, key = h.liveKey, u = u, on = float.NaN, contact = Vector3.Lerp(h.liveFrom, k, Smooth(u)) + isUp * (ht * 4f * u * (1f - u)) };
                return true;
            }
            float t = hop ? age - LiveHopS : age;
            float wob = Mathf.Exp(-t / WobbleS) * Mathf.Cos(t * 30f);
            p = new HandPose { phase = Phase.Live, key = h.liveKey, on = float.NaN, contact = k, squash = Squash * wob, holds = true };
            return true;
        }

        // ---------------------------------------------------------------- the frame
        internal void Tick()
        {
            sw.Reset(); sw.Start();
            var kb = Island;
            bool vis = kb != null && kb.isActiveAndEnabled;
            foreach (var h in hands) h.SetVisible(vis);
            if (vis)
            {
                UpdateFrame();
                if (dirty || Time.unscaledTime - scoredAt > 0.5f)
                {
                    bool was = dirty;
                    Rescore();
                    if (was) foreach (var h in hands) h.StartGlide();   // an edit / a rebuild: glide to the new spot
                }
                bool playing = GlobalClock.IsPlaying;
                float pb = GlobalClock.SongBeat, stepB = Bps / PoseFps;
                if (playing != wasPlaying) foreach (var h in hands) h.StartGlide();   // play / stop: glide, never jump
                if (playing)
                {
                    float beat = pb;
                    if (Look.OnTwos && stepB > 0f) pb = Mathf.Floor(beat / stepB) * stepB;
                    float on = LatestOnset(pb, beat);
                    if (on > pb) pb = on;
                    float lo = LatestOnset(float.NegativeInfinity, beat);
                    if (!float.IsInfinity(lo) && beat - lo < OnesS * Bps) pb = beat;   // the landing and its squash on ones
                    if (wasPlaying && (pb < lastPose - 0.5f && !(GlobalClock.LoopSong && pb <= (float)GlobalClock.LoopStartBeat + 0.5f) || pb - lastPose > 1.5f))
                        foreach (var h in hands) h.StartGlide();   // a seek
                    lastPose = pb;
                }
                wasPlaying = playing;
                if (frozen) { pb = frozenBeat; playing = true; }
                float sec = Look.OnTwos ? Look.Stepped(Time.unscaledTime, PoseFps) : Time.unscaledTime;
                foreach (var h in hands)
                {
                    HandPose p;
                    if (frozen || !LivePose(h, out p))
                        p = playing ? Evaluate(h, pb) : Sit(KeyAlive(h.restKey) ? h.restKey : null, sec);
                    if (playing && !frozen && p.key != null)
                        h.restKey = p.phase == Phase.Air ? (p.u >= 0.5f ? p.key : (p.from != null ? p.from : p.key)) : p.key;   // stopped, it stays where it was
                    if (p.holds && p.key != null && p.key.island != null && p.key.island.enabled) p.key.Press(1f);   // held down while the note sounds
                    Draw(h, p, pb, playing);
                }
                Builds++;
            }
            sw.Stop();
            LastMs = sw.Elapsed.TotalMilliseconds;
            AvgMs = AvgMs <= 0.0 ? LastMs : AvgMs * 0.95 + LastMs * 0.05;
        }

        /// <summary>Draws sphere <paramref name="h"/> in pose <paramref name="p"/>: sitting on its key's live top (riding the key's sink), squashed on a
        /// landing / crouch, stretched along a fast hop; a glide (seek, play / stop, rebuild) eases it there in a low arc.</summary>
        void Draw(Hand h, HandPose p, float beat, bool playing)
        {
            float r = radius;
            h.Radius = r;
            Vector3 axis = isUp; float along;
            Vector3 centre;
            if (p.phase == Phase.Air)
            {
                // the stretch: along the motion, by its speed (the pose a hair earlier)
                Vector3 vel = Vector3.zero;
                if (p.on == p.on && playing)
                {
                    const float eps = 0.02f;
                    var q = Evaluate(h, beat - eps);
                    vel = (p.contact - q.contact) / (eps / Bps);
                }
                else if (h.liveOn) vel = (ContactOf(h.liveKey) - h.liveFrom) / LiveHopS;
                float speed = vel.magnitude;
                along = 1f + StretchMax * (1f - Mathf.Exp(-speed / Mathf.Max(1e-3f, r * 25f)));
                if (speed > 1e-4f) axis = vel / speed;
                centre = p.contact + isUp * r;
            }
            else
            {
                along = 1f - p.squash;   // + squash flattens, − stretches up
                centre = p.contact + KeyShift(p.key) + isUp * (r * along);   // its bottom stays on the key
            }
            if (h.glidePending)
            {
                h.glidePending = false;
                if (h.glideT >= GlideS && Vector3.Distance(h.Center, centre) > 0.5f * r) { h.glideFrom = h.Center; h.glideT = 0f; }
            }
            if (h.glideT < GlideS)
            {
                float u = Smooth(h.glideT / GlideS);
                float d = Vector3.Distance(h.glideFrom, centre);
                centre = Vector3.Lerp(h.glideFrom, centre, u) + isUp * (Mathf.Min(0.5f * d, 2f * r) * 4f * u * (1f - u));
                h.glideT += Time.unscaledDeltaTime;
            }
            float perp = 1f / Mathf.Sqrt(Mathf.Max(0.2f, along));
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, axis);
            h.Place(centre, rot, new Vector3(perp, along, perp) * (2f * r));
            h.Center = centre; h.AxisScale = along; h.shown = p; h.hasDrawn = true;
        }
    }
}
