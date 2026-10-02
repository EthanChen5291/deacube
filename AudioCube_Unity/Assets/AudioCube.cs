using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

public enum StepLen { Half = 0, Quarter = 1, Eighth = 2, Sixteenth = 3, Triplet = 4 }
public enum Gate { Short = 0, Normal = 1, Long = 2 }
public enum PathMode { Loop = 0, PingPong = 1, Once = 2 }

/// <summary>
/// A travelling note block. Hops along its tile path on the hits of its necklace (SPEC §3.2), plays the pitch of
/// each tile through VoiceRules with sample-accurate scheduling, stacks on other cubes instead of intersecting
/// them, and carries its own rhythm settings (step length, gate, path mode, stickers, modifiers, volume).
/// Audio (scheduled 0.22 s early), the Land visuals and the ring pre-light all read one pure function, <see cref="Decide"/>.
/// v4 (SPEC v4 §4 R1-R3): with <see cref="durs"/> set (one length per node, 24 ticks = a beat) the cube runs the durations engine: every
/// step is a note of its node's own length (one period precomputed in ticks, swing as a per-beat time warp); without them the legacy
/// uniform-step + necklace engine runs unchanged. The body's size follows the note it is on (<see cref="SizeOf"/>, a springy transition,
/// long notes breathe, dotted notes carry a satellite), and a draft is a hologram until <see cref="Solidify"/> (or <see cref="Dissolve"/>).
/// v6 (SPEC v6 §2.4 / §5.4, package H — "transcribed to the next chord … recognizable when played"): a cube of a carrying island also plays
/// its pattern on the next chords (K's carried windows). There <see cref="TileAt"/> returns the ADAPTED tiles (Clipboard.Adapt, chained: hop k
/// adapts from hop k − 1 so the line moves smoothly; cached, rebuilt when the path, the windows or a chord change), and the body TRAVELS: before a
/// window on another island it flies there along Magic.ArcPoint (<see cref="FlightBeats"/>, ending where that window starts, with a Magic.Trail),
/// lands with Magic.Land, and after its last carried window it flies home the same way — a pure function of the song beat (seeks and loops land
/// right); the audio is scheduled from the windows as always. A path that visits a tile more than once fans its beads out
/// (<see cref="BeadPosition"/>: a keyboard's repeated note along the key) so every node stays visible. <see cref="SetVoice"/> picks the sound.
/// v7 (SPEC v7 §5.4 / §12.1, package H): an OCTAVE LAYER cube (<see cref="layer"/> ≠ 0: an octave copy / echo) rides its tile's pose +
/// Magic.LayerOffset (the sky above the grid, the deep below it), never occupies tiles, and is faint glass at rest that PHASES IN (solid, glowing)
/// just before each of its notes and back out after ("some other cubes above ... below ... phase in and plays it"); a harmony cube's bent node
/// tilts the body toward its bend. The motions are pure functions of the song beat: a RUNNER on a stairs island drops (or climbs) step to step;
/// a REWIND island's pass ends with the body zipping backward through its path to its first tile; a VARIED pass plays Harmony.Vary's tiles
/// (<see cref="TileAt"/>). §21 (the user: "the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each
/// grid. only explore grids moving from left to right with the long grid"): a cube NEVER leaves its own grid. A carry run is ONE LONG GRID: the
/// pattern walks on to the right measure by measure, re-voiced by the pedal rule (Harmony.AdaptPedal), and stepping into the next measure is an
/// ordinary hop (no flights, flings, glides or runner exits any more). HOLD THEN RETURN (the user: "whenever a grid ends, it shouldnt teleport
/// back to its starting position, they should only do that when the song resets (or if the grid is playing again), and no teleportation, just
/// movement"): outside its windows a cube HOLDS where its last window left it (a walking cube on the long grid's measure it ended on) and travels
/// home along its grid only right before it plays again (landing on the next window's first note; consecutive windows = a hop across the
/// boundary like any next note) or right after the song resets (each cube a little out of sync); a stop sends it home in real time instead of
/// the old snap.
/// </summary>
public partial class AudioCube : MonoBehaviour   // partial: the sphere piece lives in DeaCube/SphereBody.cs
{
    public static event Action<AudioCube> OnAnyChanged;

    public int instrument;
    /// <summary>v6 (SPEC v6 §2.7): the sound inside the instrument group (Instruments.SlotOf(instrument, voice)); 0 = the group's first sound.</summary>
    public int voice;
    public int assignedGridIndex;
    public List<TileInteraction> nodes = new List<TileInteraction>();
    public List<bool> rests = new List<bool>();          // legacy mirror of mods (rest == 1); mods are the truth
    public StepLen step = StepLen.Quarter;
    public Gate gate = Gate.Normal;
    public PathMode mode = PathMode.Loop;
    [Range(0f, 1f)] public float volume = 1f;
    public bool muted;
    public int moon = -1;
    public List<int> mods = new List<int>();   // per node sticker: 0 none 1 rest 2 ghost 3 accent 4 ratchet2 5 ratchet3 6 coin 7 tie 8 lift
    public int hits = -1, rot, mask, octave, phase, follow, echo, seed, twinOf = -1, id;
    public bool reverse, shimmer, rider;
    /// <summary>SPEC v4 §2.4: per-node note length in ticks (24 = one beat); empty = the legacy uniform-step engine (step + necklace).</summary>
    public List<int> durs = new List<int>();
    /// <summary>v7 (SPEC v7 §2.1 / §2.9): the octave layer (−2..+2): the cube sounds 12 × layer higher and rides above (sky) / below (deep) its grid
    /// (Magic.LayerOffset). An octave copy / echo gets ±1 from its source.</summary>
    public int layer;
    /// <summary>CLIMB (DeaCube/Climb.cs): 0 off, 1..3 = how many of the path's high points climb to the next chord tone above (every time it plays).</summary>
    public int climb;
    /// <summary>v7: per node, semitones added to the tile's pitch (a harmony cube's 4ths / 5ths that are not tiles); empty = none. Kept aligned with the
    /// nodes like <see cref="durs"/> (a misaligned list is ignored: <see cref="BendAt"/> = 0).</summary>
    public List<int> bend = new List<int>();
    /// <summary>v7: the id of the cube this one echoes / doubles / harmonises (−1 none): the world draws the link.</summary>
    public int echoOf = -1;
    /// <summary>v9 (N, DeaCube/Nudge.cs): per node, scale steps of the song key (−2..+2) the step plays above / below its tile ("higher" / "lower":
    /// a passing note the chord has no tile for); empty = none. Kept aligned with the nodes like <see cref="bend"/> (misaligned: <see cref="NudgeAt"/> = 0).</summary>
    public List<int> nudges = new List<int>();
    /// <summary>The Moon this cube lives on (null for island residents; set by the Moon package).</summary>
    public KeyBlock Moon;
    /// <summary>Grid offsets of the nodes on their home island; a Rider replays them on the island of every window (rows clamped).</summary>
    public readonly List<int> gridX = new List<int>(), gridZ = new List<int>();

    /// <summary>One playable time span of this cube in song beats (SPEC §2.10). Written only by SongManager.RecomputeMeasureStarts via SetWindows.</summary>
    public struct Window { public float start, length; public KeyBlock island; public int order; public bool silent;
        /// <summary>v6 carry (SPEC v6 §2.4): the window plays the home pattern adapted onto another island's chord; hop = 1.. its place in the chain.</summary>
        public bool carried; public int hop;
        /// <summary>v7 (SPEC v7 §2.6 / §2.7): the pass of its column this window plays (0..; a carried window: the target column's pass); a flow
        /// window (a carried window of a flow-style island: pedal-adapted tiles, the cube glides in).</summary>
        public int pass; public bool flow; }
    /// <summary>
    /// The pure rhythm decision for one step (§3.2). fires = a note is dispatched; node = where the cube is on this step (on a
    /// wait step: the node of the last hit); weight 0 ghost / 1 normal / 2 accent; ratchet = sub-hits (1 = single);
    /// hit = pattern hit (the cube lands on node; false = a wait step: it stays and crouches); mod = the node's sticker;
    /// echo = effective repeats (cube.echo, at least 1 inside a fill's last bar); slip = a coin that failed this pass.
    /// </summary>
    public struct Hit { public bool fires; public int node; public int weight; public int ratchet; public bool tie, lift, slip; public bool hit; public int mod; public int echo;
        /// <summary>v9 (N): the node's nudge (scale steps −2..+2 of the song key; VoiceRules turns it into semitones on the tile it plays).</summary>
        public int nudge; }
    public readonly List<Window> windows = new List<Window>();

    public bool isFinalized;
    public bool isReadyToPlay;
    public float startBeatOffset;
    public float measureLength = 4f;
    public List<Vector3> pathNodes = new List<Vector3>();   // legacy

    /// <summary>World units a cube floats per octave (height = pitch, §3.1 #9); the world package owns the number.</summary>
    public const float OctaveLift = ProjectConfig.OctaveRise;

    public Color Color => Instruments.Colors[Mathf.Clamp(instrument, 0, Instruments.Count - 1)];
    public KeyBlock Island => nodes.Count > 0 && nodes[0] != null ? nodes[0].island : null;
    public float StepBeats { get { switch (step) { case StepLen.Half: return 2f; case StepLen.Eighth: return 0.5f; case StepLen.Sixteenth: return 0.25f; case StepLen.Triplet: return 1f / 3f; default: return 1f; } } }
    public bool IsSelected => selected;
    public bool IsOnMoon => moon >= 0;
    public bool IsDrums => Instruments.IsDrums(instrument);
    /// <summary>v7 (SPEC v7 §2.4): a runner — a cube on a stairs island (its path is always SongManager.StairPath).</summary>
    public bool IsRunner => !IsOnMoon && Island != null && Island.IsStairs;
    /// <summary>v7: the cube lives in an octave layer (above / below its grid): it never occupies tiles.</summary>
    public bool IsLayered => layer != 0;
    /// <summary>v7: the semitones node <paramref name="node"/> adds to its tile's pitch (0 without bends or when the list is misaligned).</summary>
    public int BendAt(int node) => bend.Count == nodes.Count && node >= 0 && node < bend.Count ? bend[node] : 0;
    /// <summary>v7: the bend node <paramref name="node"/> adds on <paramref name="tile"/> — its bend when <paramref name="tile"/> is the node's own
    /// (home) tile, else 0: a carried / flow / varied window plays another tile, where the bend would only detune it.</summary>
    public int BendOn(TileInteraction tile, int node) => tile != null && node >= 0 && node < nodes.Count && nodes[node] == tile ? BendAt(node) : 0;
    /// <summary>v9 (N): the nudge of node <paramref name="node"/> (scale steps −2..+2; 0 without nudges or when the list is misaligned). Unlike a bend
    /// it plays on whatever tile the step lands on (a carried / varied window too): a scale step is always in key.</summary>
    public int NudgeAt(int node) => nudges.Count == nodes.Count && node >= 0 && node < nudges.Count ? nudges[node] : 0;
    /// <summary>v9 (N): true when any node is nudged.</summary>
    public bool HasNudges => nudges.Count == nodes.Count && Nudge.Any(nudges);
    /// <summary>Steps per bar n = BeatsPerBar / StepBeats (the necklace length, §3.2), 1..24.</summary>
    public int StepsPerBar => Mathf.Clamp(Mathf.RoundToInt(GlobalClock.BeatsPerBar / StepBeats), 1, 24);
    /// <summary>The node the cube rests on before its first hit (node 0; the last node when reversed).</summary>
    public int HomeNode => nodes.Count == 0 ? -1 : (reverse ? nodes.Count - 1 : 0);
    /// <summary>The node a Once-mode pass ends on.</summary>
    public int EndNode => nodes.Count == 0 ? -1 : (reverse ? 0 : nodes.Count - 1);

    // visuals
    Renderer rend; Material mat; Mesh bodyMesh;
    float glow, hoverAmt, hoverTarget; bool selected; bool asleep;
    float squashT = 9f, squashKind = 1f;
    Transform halo; Renderer haloRend; MaterialPropertyBlock haloMpb; Material haloMat;
    LineRenderer line; Material lineMat;
    readonly List<Transform> markers = new List<Transform>();
    readonly List<Renderer> markerRends = new List<Renderer>();
    readonly List<float> markerPulse = new List<float>();
    readonly List<float> markerBase = new List<float>();
    readonly List<Transform> glyphs = new List<Transform>();
    readonly List<Renderer> glyphRends = new List<Renderer>();
    readonly List<Transform> tieBars = new List<Transform>();
    readonly List<Transform> ticks = new List<Transform>();
    MaterialPropertyBlock markerMpb;
    Material matDot, matRing, matRest, matTick, matTie;
    readonly Dictionary<string, Material> glyphMats = new Dictionary<string, Material>();
    // modifiers in the world: shadow ghosts (follow), the octave shadow disc, echo afterimages
    readonly Transform[] ghosts = new Transform[2]; readonly Vector3[] ghostDelta = new Vector3[2]; Material ghostMat;
    Transform disc; Material discMat;
    readonly Transform[] echoQuads = new Transform[2]; readonly Renderer[] echoRends = new Renderer[2]; readonly float[] echoAge = { 9f, 9f }; Material echoMat; MaterialPropertyBlock echoMpb;

    // motion state
    TileInteraction restingTile; int lastStep = -1, lastWindow = -1; int windowIdx; float yVel; bool wasActive;
    Vector3 hopFrom; bool hopping; TileInteraction hopTarget;
    // v5 (K's repeat belt): the island's transform jerks one slot at a pass boundary, so the starts of a hop / a preview hop / a drop are kept
    // island-local (IslandLocal / IslandWorld) and brought back to world space every frame: the cube rides the belt with its island
    Transform hopSpace; Vector3 hopFromLocal, previewFromLocal, dropFromLocal;
    Vector3 leanDir; float leanT;
    int ratchetLeft; float ratchetNextLocal, ratchetSub; TileInteraction ratchetTile;
    int echoLeft, echoIdx; float echoNextLocal; TileInteraction echoTile;
    TileInteraction dropTarget; Vector3 dropFrom; float dropT = 9f;
    TileInteraction previewTarget; Vector3 previewFrom; float previewT = 9f;

    // audio: notes go to the SoundFont engine (Synth) tagged with this cube's owner id; without a
    // SoundFont one lazily created AudioSource plays the legacy pitched sample instead.
    public int owner;                              // Synth owner id = |instanceID| + 16 (always >= 16; 0..15 are reserved, §4)
    AudioClip clip; AudioMixerGroup group; double lastScheduledDsp = -1.0;
    AudioSource fallback; double fallbackStopAt = double.MaxValue, fallbackStartAt = -1.0; float fallbackVol;
    const double Lookahead = 0.22;
    /// <summary>v5: how far ahead <see cref="ScheduleAllNow"/> queues (a rebuilt or freshly started big song's first frames can outlast Lookahead).</summary>
    public const double RebuildLookahead = 0.45;
    static double lookNow = Lookahead;   // the lookahead in force (RebuildLookahead only inside ScheduleAllNow)

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        rend = GetComponent<Renderer>();
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; rb.detectCollisions = false; }
        var mf = GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = MeshFactory.RoundedBox(Vector3.one, 0.14f, 4);
        bodyMesh = mf != null ? mf.sharedMesh : null;
        transform.localScale = Vector3.one * ProjectConfig.CubeSize;
        owner = Mathf.Abs(GetInstanceID()) + 16;
        // The prefabs carry an AudioSource with playOnAwake set, which fires the raw sample on spawn: silence and drop it.
        foreach (var s in GetComponents<AudioSource>()) { s.Stop(); s.enabled = false; Destroy(s); }
    }

    void OnEnable()
    {
        GlobalClock.OnStop += HandleStop; GlobalClock.OnPause += CancelPending; GlobalClock.OnSeek += HandleSeek; GlobalClock.OnTempoChanged += HandleTempo; GlobalClock.OnPlay += HandlePlay;
        SongManager.OnTransposeChanged += CancelPending;   // a transpose re-pitches every note: release the sounding ones and re-schedule under the new pitch
        GlobalClock.OnRegionChanged += SoftReschedule;     // v4 R4: a focus loop set / moved / cleared re-decides what the lookahead queued past its edge
    }
    void OnDisable()
    {
        GlobalClock.OnStop -= HandleStop; GlobalClock.OnPause -= CancelPending; GlobalClock.OnSeek -= HandleSeek; GlobalClock.OnTempoChanged -= HandleTempo; GlobalClock.OnPlay -= HandlePlay;
        SongManager.OnTransposeChanged -= CancelPending;
        GlobalClock.OnRegionChanged -= SoftReschedule;
    }

    public void Init(int instrumentIndex)
    {
        instrument = Mathf.Clamp(instrumentIndex, 0, Instruments.Count - 1);
        mat = rend.material;
        mat.EnableKeyword("_EMISSION");
        ApplyInstrumentLook();
        BuildHalo();
        BuildLine();
        SequenceMaster.Register(this);
    }

    /// <summary>What the body emits when it lands: the colour's HUE at full value and at least 0.7 saturation (U1's pastel palette: emitting
    /// the pastel itself clipped every channel to white on a hit, so a playing island lost its hues).</summary>
    Color emitColor = Color.white;

    void ApplyInstrumentLook()
    {
        clip = Instruments.ClipOf(instrument); group = Instruments.GroupOf(instrument);
        float eh, es, ev; Color.RGBToHSV(Color, out eh, out es, out ev);
        emitColor = Color.HSVToRGB(eh, Mathf.Max(es, 0.7f), 1f);
        if (mat != null)
        {
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.62f);
        }
        if (fallback != null) { fallback.clip = clip; fallback.outputAudioMixerGroup = group; }
        if (lineMat != null) lineMat.SetColor(ColorId, Color);
        if (matDot != null)
        {
            matDot.SetColor(ColorId, Palette.A(Color, 0.9f)); matRing.SetColor(ColorId, Palette.A(Color, 0.95f)); matRest.SetColor(ColorId, Palette.A(Color, 0.55f));
            matTick.SetColor(ColorId, Palette.A(Color, 0.5f)); matTie.SetColor(ColorId, Palette.A(Color, 0.55f));
        }
        foreach (var kv in glyphMats) kv.Value.SetColor(ColorId, GlyphColor);
        if (ghostMat != null) ghostMat.SetColor(ColorId, Palette.A(Color, 1f));
        if (echoMat != null) echoMat.SetColor(ColorId, Color);
        if (haloRend != null) { haloMpb.SetColor(ColorId, Color); haloRend.SetPropertyBlock(haloMpb); }
    }

    Color GlyphColor => Palette.A(Color.Lerp(Color, Color.white, 0.55f), 0.95f);

    void BuildHalo()
    {
        var go = new GameObject("Halo");
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Ring(0.72f, 1f, 48);
        haloRend = go.AddComponent<MeshRenderer>();
        haloMat = Fx.Additive(IconFactory.GetTexture("ringBand"));
        haloRend.sharedMaterial = haloMat;
        haloRend.shadowCastingMode = ShadowCastingMode.Off; haloRend.receiveShadows = false;
        haloMpb = new MaterialPropertyBlock();
        haloMpb.SetColor(ColorId, Color); haloMpb.SetFloat(IntensityId, 0f);
        haloRend.SetPropertyBlock(haloMpb);
        halo = go.transform;
        go.SetActive(false);
    }

    void BuildLine()
    {
        var go = new GameObject("Path");
        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.085f;
        line.numCornerVertices = 5; line.numCapVertices = 5;
        line.textureMode = LineTextureMode.Tile;
        line.alignment = LineAlignment.View;
        lineMat = Fx.Flow(Color, 1f, 1.4f, 1.7f, 0.22f);
        line.material = lineMat;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        line.positionCount = 0;
        matDot = Fx.Alpha(IconFactory.GetTexture("ringThin"), Palette.A(Color, 0.9f));
        matRing = Fx.Alpha(IconFactory.GetTexture("ring"), Palette.A(Color, 0.95f));
        matRest = Fx.Alpha(IconFactory.GetTexture("rest"), Palette.A(Color, 0.55f));
        matTick = Fx.Alpha(IconFactory.GetTexture("dotSmall"), Palette.A(Color, 0.5f));
        matTie = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Color, 0.55f));
    }

    Transform MakeQuad(string name, Material m, out Renderer r)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return go.transform;
    }

    // ------------------------------------------------------------------ path
    public void SetPath(List<TileInteraction> tiles)
    {
        nodes = new List<TileInteraction>(tiles);
        SyncLists();
        assignedGridIndex = Island != null ? Island.measureIndex : assignedGridIndex;
        pathNodes.Clear(); foreach (var n in nodes) pathNodes.Add(n.Top);
        isReadyToPlay = true;
        RefreshLine();
    }

    public void SetPath(List<Vector3> worldNodes) { pathNodes = new List<Vector3>(worldNodes); }   // legacy signature

    /// <summary>Keeps mods / rests / grid offsets the length of nodes (mods are the truth; rests mirror rest == 1).</summary>
    void SyncLists()
    {
        int n = nodes.Count;
        while (mods.Count < n) mods.Add(0);
        while (mods.Count > n) mods.RemoveAt(mods.Count - 1);
        while (rests.Count < n) rests.Add(false);
        while (rests.Count > n) rests.RemoveAt(rests.Count - 1);
        if (durs.Count > 0)
        {
            // SPEC v4 R1: a durations cube keeps one length per node (a new tail node repeats the last length)
            while (durs.Count < n) durs.Add(durs[durs.Count - 1]);
            while (durs.Count > n) durs.RemoveAt(durs.Count - 1);
        }
        if (bend.Count > 0)
        {
            // v7: a harmony cube keeps one bend per node (a new node has none)
            while (bend.Count < n) bend.Add(0);
            while (bend.Count > n) bend.RemoveAt(bend.Count - 1);
        }
        if (nudges.Count > 0)
        {
            // v9 (N): one nudge per node (a new node has none)
            while (nudges.Count < n) nudges.Add(0);
            while (nudges.Count > n) nudges.RemoveAt(nudges.Count - 1);
        }
        gridX.Clear(); gridZ.Clear();
        for (int i = 0; i < n; i++) { gridX.Add(nodes[i] != null ? nodes[i].gridX : 0); gridZ.Add(nodes[i] != null ? nodes[i].gridZ : 0); }
    }

    /// <summary>The sticker on a node (0 none 1 rest 2 ghost 3 accent 4 ratchet2 5 ratchet3 6 coin 7 tie 8 lift).</summary>
    public int ModOf(int node) => node >= 0 && node < mods.Count ? mods[node] : 0;

    /// <summary>IconFactory glyph of a sticker (null for none / rest: the rest is the hollow bead itself).</summary>
    public static string StickerIcon(int mod)
    {
        switch (mod) { case 2: return "ghost"; case 3: return "accent"; case 4: return "ratchet2"; case 5: return "ratchet3"; case 6: return "coin"; case 7: return "tie"; case 8: return "lift"; }
        return null;
    }

    Material GlyphMat(string icon)
    {
        Material m;
        if (!glyphMats.TryGetValue(icon, out m) || m == null) { m = Fx.Alpha(IconFactory.GetTexture(icon), GlyphColor); m.renderQueue = pathQueue; glyphMats[icon] = m; }
        return m;
    }

    /// <summary>Tick dots drawn between two consecutive beads: StepBeats / 0.25 - 1 (Triplet: one small tick).</summary>
    public int TicksPerStep => step == StepLen.Triplet ? 1 : Mathf.Max(0, Mathf.RoundToInt(StepBeats / 0.25f) - 1);

    public void RefreshLine()
    {
        if (line == null) return;
        int n = nodes.Count;
        if (n < 2) line.positionCount = 0;
        else
        {
            // v5: no allocation (K re-places the lines every frame while an island rides its belt)
            if (line.positionCount != n) line.positionCount = n;
            for (int i = 0; i < n; i++) line.SetPosition(i, BeadPosition(i) + Vector3.up * 0.05f);
        }
        while (markers.Count < n)
        {
            Renderer mr, gr;
            markers.Add(MakeQuad("Node", matDot, out mr)); markerRends.Add(mr); markerPulse.Add(0f); markerBase.Add(0f); beadPop.Add(9f);
            glyphs.Add(MakeQuad("Sticker", matDot, out gr)); glyphRends.Add(gr);
            Renderer tr; var tb = MakeQuad("Tie", matTie, out tr); tb.gameObject.SetActive(false); tieBars.Add(tb);
        }
        // v4 R3: beads of nodes added since the last refresh pop in (0 -> 1.2 -> 1) on a draft or an edited cube (not on a fresh restore)
        if (hologram || beadsShown > 0) for (int i = beadsShown; i < n && i < beadPop.Count; i++) beadPop[i] = 0f;
        beadsShown = n;
        int tps = HasDurations ? 0 : TicksPerStep;   // v4 R2: a durations cube shows its lengths by bead size, no tick dots
        int tickCount = n >= 2 ? (n - 1) * tps : 0;
        while (ticks.Count < tickCount) { Renderer r; ticks.Add(MakeQuad("Tick", matTick, out r)); }
        for (int i = 0; i < markers.Count; i++)
        {
            bool on = i < n;
            markers[i].gameObject.SetActive(on);
            if (!on) { glyphs[i].gameObject.SetActive(false); tieBars[i].gameObject.SetActive(false); continue; }
            int mod = ModOf(i);
            bool rest = mod == 1;
            float wscale = mod == 2 ? 0.75f : (mod == 3 ? 1.3f : 1f);          // bead radius = weight
            float s = (i == 0 ? 0.46f : (rest ? 0.34f : 0.27f)) * wscale * SizeOf(DurBeats(i)) * FootOf(nodes[i]);   // v4 R2: x the note's length (legacy: the step); v6: smaller on keys
            Vector3 bead = BeadPosition(i);   // v6: repeated visits of a tile fan out
            int nd = NudgeAt(i);   // v9 (N): a nudged bead sits a touch higher (a lowered one keeps the tile: its ▼ says so)
            bead += Vector3.up * (0.04f * Mathf.Max(0, nd));
            markers[i].position = bead + Vector3.up * 0.03f;
            markerBase[i] = s;
            markers[i].localScale = new Vector3(s, 1f, s);
            markerRends[i].sharedMaterial = rest ? matRest : (i == 0 ? matRing : matDot);
            string icon = nd != 0 ? Nudge.Icon(nd) : StickerIcon(mod);   // v9 (N): ▲ / ▼ over the sticker glyph (the bead's size still shows the sticker)
            bool showGlyph = icon != null;
            glyphs[i].gameObject.SetActive(showGlyph);
            if (showGlyph)
            {
                float gs = Mathf.Max(0.17f, s * 0.7f);
                glyphs[i].position = bead + Vector3.up * 0.045f;
                glyphs[i].localScale = new Vector3(gs, 1f, gs);
                glyphRends[i].sharedMaterial = GlyphMat(icon);
            }
            bool tie = mod == 7 && i + 1 < n;
            tieBars[i].gameObject.SetActive(tie);
            if (tie)
            {
                Vector3 a = bead, b = BeadPosition(i + 1);
                Vector3 d = b - a; d.y = 0f;
                float len = d.magnitude;
                tieBars[i].position = (a + b) * 0.5f + Vector3.up * 0.035f;
                tieBars[i].localScale = new Vector3(Mathf.Max(0.05f, len - 0.3f), 1f, 0.06f);
                tieBars[i].rotation = len > 1e-4f ? Quaternion.AngleAxis(-Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg, Vector3.up) : Quaternion.identity;
            }
            // v4 R2: a dotted length carries a small satellite dot beside its bead
            bool dotted = HasDurations && IsDottedTicks(durs[i]);
            while (dotMarks.Count <= i && dotted) { Renderer dr; var dm = MakeQuad("Dotted", matDot, out dr); dm.gameObject.SetActive(false); dotMarks.Add(dm); dotRends.Add(dr); }
            if (i < dotMarks.Count)
            {
                dotMarks[i].gameObject.SetActive(dotted);
                if (dotted)
                {
                    dotMarks[i].position = bead + new Vector3(s * 0.62f, 0.05f, s * 0.62f);
                    float ds = Mathf.Max(0.09f, s * 0.36f);
                    dotMarks[i].localScale = new Vector3(ds, 1f, ds);
                    dotRends[i].sharedMaterial = GlyphMat("dot");
                }
            }
        }
        for (int i = n; i < dotMarks.Count; i++) dotMarks[i].gameObject.SetActive(false);
        int t = 0;
        for (int i = 0; i + 1 < n; i++)
            for (int j = 0; j < tps; j++)
            {
                var tr = ticks[t++];
                tr.gameObject.SetActive(true);
                float u = (j + 1f) / (tps + 1f);
                tr.position = Vector3.Lerp(TopOf(nodes[i]), TopOf(nodes[i + 1]), u) + Vector3.up * 0.03f;
                float ts = step == StepLen.Triplet ? 0.07f : 0.09f;
                tr.localScale = new Vector3(ts, 1f, ts);
            }
        for (; t < ticks.Count; t++) ticks[t].gameObject.SetActive(false);
    }

    // ---- v6: repeated visits of a tile (a keyboard's repeated note) fan their beads out
    /// <summary>Spacing (world units) of the beads of repeated visits of one tile.</summary>
    public const float RepeatStep = 0.3f;
    /// <summary>World position of node <paramref name="i"/>'s bead: the tile's top, fanned out when the path visits the tile more than once — a
    /// keyboard's repeated note steps along the key (the first visit furthest back), a grid tile's visits sit around its centre.</summary>
    public Vector3 BeadPosition(int i)
    {
        if (i < 0 || i >= nodes.Count || nodes[i] == null) return transform.position;
        var t = nodes[i];
        Vector3 top = TopOf(t);   // v7: a layer cube's beads sit in its layer (on W's glass grid)
        int occ = 0, total = 0;
        for (int j = 0; j < nodes.Count; j++) if (nodes[j] == t) { if (j < i) occ++; total++; }
        if (total <= 1) return top;
        if (t.island != null && t.island.IsKeyboard) return top + new Vector3(0f, 0.02f * occ, ((total - 1) * 0.5f - occ) * RepeatStep);
        float a = (90f + 360f * occ / total) * Mathf.Deg2Rad;
        return top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.22f;
    }

    /// <summary>v7: roughly where the cube's octave layer rides relative to its grid (Magic.LayerOffset: exact for the sky; the deep layer is a
    /// band in front of the platform — <see cref="TopOf"/> is the exact point per tile). Zero on the grid itself.</summary>
    public Vector3 LayerLift => layer != 0 ? Magic.LayerOffset(layer) : Vector3.zero;
    /// <summary>v7: the top of <paramref name="t"/> in this cube's layer — where it stands, lands, flies to and draws its beads: W's glass tile of
    /// that tile (Magic.LayerPoint: the sky above the grid, the deep's reflection band in front of and below the platform); the tile's top on the
    /// grid itself. The one place a layer's geometry is read.</summary>
    public Vector3 TopOf(TileInteraction t) => t == null ? transform.position : layer != 0 ? Magic.LayerPoint(t, layer) : t.Top;

    public void Finalize()
    {
        bool live = hologram && GlobalClock.IsPlaying;   // v4 R3: a draft that has been playing keeps its schedule (no step fired twice)
        isFinalized = true;
        if (!live) { lastStep = -1; lastScheduledDsp = -1.0; }
        if (id == 0) id = NextId();
        if (seed == 0) seed = ToState().SeedOrDerived();
        RefreshLine();
        TileInteraction.RefreshKitLook();
        OnAnyChanged?.Invoke(this);
    }

    public void PlaceOn(TileInteraction tile, bool drop)
    {
        if (drop)
        {
            dropTarget = tile; dropT = 0f;
            dropFrom = tile.Top + Vector3.up * ProjectConfig.CubeDropHeight;
            dropFromLocal = IslandLocal(tile, dropFrom);
            transform.position = dropFrom;
            SetResting(tile);
        }
        else
        {
            SetResting(tile);
            transform.position = RestPosition(tile, StackSlot(tile));
            CubeSmear.Cut(owner);   // a placement is a jump, not a motion
        }
    }

    public void PreviewHopTo(TileInteraction tile)
    {
        previewFrom = transform.position; previewTarget = tile; previewT = 0f;
        previewFromLocal = IslandLocal(tile, previewFrom);
        SetResting(null);
    }

    /// <summary>v5: <paramref name="world"/> in the space of <paramref name="tile"/>'s island (its transform rides the repeat belt).</summary>
    static Vector3 IslandLocal(TileInteraction tile, Vector3 world) => tile != null && tile.island != null ? tile.island.transform.InverseTransformPoint(world) : world;
    /// <summary>v5: an island-local point of <paramref name="tile"/>'s island back in world space, where the island is this frame.</summary>
    static Vector3 IslandWorld(TileInteraction tile, Vector3 local) => tile != null && tile.island != null ? tile.island.transform.TransformPoint(local) : local;

    /// <summary>Rest position on a tile for a stack slot; the octave floats the cube +-OctaveLift (height = pitch). v4 R2: the cubes below
    /// stack by their visual sizes and this cube's floor sits on them (its centre lifts by half its own size).</summary>
    Vector3 RestPosition(TileInteraction tile, int slot)
    {
        if (layer != 0) return TopOf(tile) + Vector3.up * (ProjectConfig.CubeSize * sizeNow * FootOf(tile) * 0.5f + octave * OctaveLift);   // v7: in its layer, never stacked
        return tile.Top + Vector3.up * (SequenceMaster.StackHeight(tile, slot, this) + ProjectConfig.CubeSize * sizeNow * FootOf(tile) * 0.5f + octave * OctaveLift);
    }

    // ---- v6 (K's request): a keyboard's keys are ~0.45 u wide, so a cube on a keyboard wears a smaller footprint — the whole size language scaled
    // by KeyFoot (bigger = longer still reads); it eases between islands (a carried flight, a drag) instead of snapping
    /// <summary>The scale of a cube (and its beads) on a keyboard island's keys.</summary>
    public const float KeyFoot = 0.6f;
    float foot = 1f; bool footInit;
    /// <summary>The footprint scale on <paramref name="tile"/>'s island (KeyFoot on a keyboard, else 1).</summary>
    public static float FootOf(TileInteraction tile) => tile != null && tile.island != null && tile.island.IsKeyboard ? KeyFoot : 1f;
    /// <summary>The footprint scale the body shows now (eased).</summary>
    public float Foot => foot;
    void UpdateFoot(float dt)
    {
        KeyBlock at = restingTile != null ? restingTile.island : (hopping && hopTarget != null ? hopTarget.island : Island);
        float to = at != null && at.IsKeyboard ? KeyFoot : 1f;
        if (!footInit) { footInit = true; foot = to; return; }
        foot = Mathf.MoveTowards(foot, to, dt * 2.5f);
    }
    int StackSlot(TileInteraction tile) { int idx = SequenceMaster.IndexOn(tile, this); return idx >= 0 ? idx : SequenceMaster.CountOn(tile, this); }
    /// <summary>Where this cube rests right now (its resting tile, else its home node) for its stack slot; for tests and the world package.</summary>
    public Vector3 RestPositionNow
    {
        get
        {
            if (tripOn) return tripPos;   // v7: on a trip (home, to its next window, a flight, the fling, the zip, after a stop): that pose this frame
            var t = restingTile != null ? restingTile : (nodes.Count > 0 ? nodes[Mathf.Max(0, HomeNode)] : null);
            return t != null ? RestPosition(t, StackSlot(t)) : transform.position;
        }
    }
    void SetResting(TileInteraction tile) => SetResting(tile, true);
    /// <summary>v7: <paramref name="occupy"/> false = the cube stands on the tile as a guest (a cube holding on a grid it does not live on): it sits
    /// on top of that tile's cubes but is not in their stack (rule 3's octave lift must never change the host's notes). Layer cubes never occupy.</summary>
    void SetResting(TileInteraction tile, bool occupy)
    {
        bool occ = occupy && layer == 0 && tile != null;
        if (restingTile == tile && restOccupies == occ) return;
        if (restingTile != null) SequenceMaster.Release(restingTile, this);
        restingTile = tile; restOccupies = occ;
        if (occ) SequenceMaster.Occupy(tile, this);
    }
    bool restOccupies;

    // ------------------------------------------------------------------ rhythm math
    float SwingK => StepBeats <= 0.5f && step != StepLen.Triplet ? GlobalClock.Swing / 3f : 0f;   // 8ths/16ths only, never triplets

    public float StepStartLocalBeat(int k)
    {
        if (HasDurations) return Warp(RawStart(k), GlobalClock.Swing / 3f);   // SPEC v4 R1: W(raw(k))
        float sb = StepBeats; int pair = k / 2; float b = pair * 2f * sb;
        return (k % 2 == 0) ? b : b + (1f + SwingK) * sb;
    }

    void ComputeStep(float local, out int k, out float progress)
    {
        if (HasDurations)
        {
            // SPEC v4 R1: invert the swing warp, then find the event by binary search in the precomputed period
            float sw = GlobalClock.Swing / 3f;
            k = RawStep(WarpInv(Mathf.Max(0f, local), sw));
            float a0 = Warp(RawStart(k), sw), a1 = Warp(RawStart(k + 1), sw);
            progress = Mathf.Clamp01((local - a0) / Mathf.Max(1e-4f, a1 - a0));
            return;
        }
        float sb = StepBeats; float kw = SwingK; float pairLen = 2f * sb;
        int pair = Mathf.FloorToInt(local / pairLen);
        float r = local - pair * pairLen;
        float first = (1f + kw) * sb;
        if (r < first) { k = pair * 2; progress = r / first; }
        else { k = pair * 2 + 1; progress = (r - first) / Mathf.Max(1e-4f, (1f - kw) * sb); }
        progress = Mathf.Clamp01(progress);
    }

    /// <summary>Today's node-for-index mapping over the path mode (Loop / PingPong / Once); v2 feeds it the hit index (§3.2).</summary>
    public int NodeForStep(int k)
    {
        int n = nodes.Count;
        if (n == 0 || k < 0) return -1;
        switch (mode)
        {
            case PathMode.PingPong:
                if (n == 1) return 0;
                int period = 2 * n - 2; int m = k % period;
                return m < n ? m : period - m;
            case PathMode.Once:
                return k < n ? k : -1;
            default:
                return k % n;
        }
    }

    // ------------------------------------------------------------------ necklace (pure, SPEC §3.2)
    bool[] pattern; int[] prefix; int patHits = int.MinValue, patRot, patMask, patN = 1, patPerBar;

    /// <summary>The per-bar hit pattern (Rhythm.Pattern of hits/rot/mask over StepsPerBar), cached until a field changes.</summary>
    bool[] Pattern()
    {
        int n = StepsPerBar;
        if (pattern == null || patHits != hits || patRot != rot || patMask != mask || patN != n)
        {
            pattern = Rhythm.Pattern(hits, rot, mask, n);
            prefix = new int[n + 1];
            for (int j = 0; j < n; j++) prefix[j + 1] = prefix[j] + (pattern[j] ? 1 : 0);
            patPerBar = prefix[n];
            patHits = hits; patRot = rot; patMask = mask; patN = n;
        }
        return pattern;
    }

    /// <summary>True when step k of a window is a necklace hit (the cube advances one node). Durations engine: every event is a hit
    /// (a Once pass ends after its events).</summary>
    public bool PatternHit(int k)
    {
        if (k < 0) return false;
        if (HasDurations) { EnsureDurCache(); return dcOnce ? k < dcP : dcP > 0; }
        var p = Pattern(); return p[k % p.Length];
    }

    /// <summary>The hit index h(k): hits among steps 0..k minus one (-1 before the first hit). Durations engine: k (a Once pass holds its
    /// last event).</summary>
    public int HitIndex(int k)
    {
        if (k < 0) return -1;
        if (HasDurations) { EnsureDurCache(); return dcOnce ? Mathf.Min(k, dcP - 1) : k; }
        Pattern();
        return (k / patN) * patPerBar + prefix[k % patN + 1] - 1;
    }

    /// <summary>Node of hit index h: NodeForStep over the hit index, mirrored when reversed (-1 when a Once pass is over).</summary>
    public int NodeForHit(int h)
    {
        int idx = NodeForStep(h);
        if (idx < 0) return -1;
        return reverse ? nodes.Count - 1 - idx : idx;
    }

    /// <summary>The next necklace hit step after k inside a window of <paramref name="length"/> beats (-1 if none).</summary>
    public int NextPatternHitStep(int k, float length)
    {
        for (int j = k + 1; j < k + 256; j++)
        {
            if (StepStartLocalBeat(j) >= length - 1e-4f) return -1;
            if (PatternHit(j)) return j;
        }
        return -1;
    }

    /// <summary>Steps inside window w (n x bars; durations engine: the events that start inside it).</summary>
    public int StepsInWindow(int w)
    {
        float len = w >= 0 && w < windows.Count ? windows[w].length : measureLength;
        if (HasDurations) { int k; float p; ComputeStep(len - 1e-4f, out k, out p); return Mathf.Max(1, k + 1); }
        return Mathf.Max(1, Mathf.RoundToInt(len / StepBeats));
    }

    /// <summary>The chord island lit while step k of window w plays (a Moon reads the lit island's energy and fill). v4 R5: the ANCHOR of the
    /// column playing at that beat (every island of a column is lit; the anchor carries the section controls a Moon follows).</summary>
    KeyBlock LitIslandAt(int w, int k)
    {
        var sm = SongManager.I;
        if (sm == null || sm.Islands.Count == 0) return null;
        float beat = (w >= 0 && w < windows.Count ? windows[w].start : startBeatOffset) + StepStartLocalBeat(k);
        var anchor = sm.AnchorOf(sm.ActiveColumn(beat));
        if (anchor != null) return anchor;
        int idx = sm.ActiveMeasureIndex(beat);
        return idx >= 0 && idx < sm.Islands.Count ? sm.Islands[idx] : null;
    }

    /// <summary>Step k lies in the last bar of window w. v5 (K's repeat belt: one window per PASS): only in the island's last pass — a belt's
    /// earlier passes play on without the fill, which leads out of the section (with the column-end crash when the island has the column's
    /// full repeat).</summary>
    bool InLastBar(int w, int k)
    {
        float len = w >= 0 && w < windows.Count ? windows[w].length : measureLength;
        return StepStartLocalBeat(k) >= len - GlobalClock.BeatsPerBar - 1e-4f && IsLastPassWindow(w);
    }

    /// <summary>v5: window w is its island's last pass in its column (always true without a belt).</summary>
    bool IsLastPassWindow(int w)
    {
        var sm = SongManager.I;
        if (sm == null || w < 0 || w >= windows.Count) return true;
        var win = windows[w];
        var isl = win.island;
        if (isl == null || isl.IsMoon) return true;
        int col = isl.column;
        float pass = sm.PassLength(col);
        if (col < 0 || pass <= 0f) return true;
        int passes = Mathf.Min(isl.Passes, sm.ColumnPasses(col));
        return win.start + win.length >= sm.ColumnStart(col) + passes * pass - 1e-3f;
    }

    /// <summary>For a Moon cube: step k of window w lies in the last bar of the chord island lit at that beat. A Moon window is one bar,
    /// so InLastBar would say yes in every bar of a multi-bar island; this reads the section's own span in song beats. v5 (K's belt): the
    /// section's span is its whole column (every pass), so the Moon fills once, in the column's last bar, leading into the next column
    /// with K's fill crash (not in the last bar of every pass).</summary>
    bool InLastBarOf(KeyBlock section, int w, int k)
    {
        float songBeat = (w >= 0 && w < windows.Count ? windows[w].start : startBeatOffset) + StepStartLocalBeat(k);
        float end = section.startBeatOffset + section.LengthBeats;
        var sm = SongManager.I;
        int col = section.column;
        if (sm != null && col >= 0 && col < sm.ColumnCount && sm.ColumnLength(col) > 0f) end = sm.ColumnStart(col) + sm.ColumnLength(col);
        return songBeat >= end - GlobalClock.BeatsPerBar - 1e-4f;
    }

    bool IsAmongLastHits(int w, int k, int count)
    {
        int total = HitIndex(StepsInWindow(w) - 1) + 1;
        return HitIndex(k) >= total - count;
    }

    /// <summary>
    /// The pure rhythm decision for step <paramref name="k"/> of window <paramref name="windowIndex"/> (§3.2): the necklace says
    /// whether the step is a hit, the hit index (+ phase, reversed) picks the node, the node's sticker shapes the hit, a coin
    /// flips on Rhythm.Hash(pass, node, seed), fill/energy of the section apply. <paramref name="pass"/> =
    /// GlobalClock.LoopIndex * 64 + window.order. Audio, Land visuals and the ring pre-light all read this.
    /// </summary>
    public Hit Decide(int windowIndex, int k, int pass)
    {
        var h = new Hit { node = -1, weight = 1, ratchet = 1, echo = IsDrums ? 0 : echo };   // drums have no echo costume (VoiceRules rule 1): the visuals agree
        if (nodes.Count == 0 || k < 0) return h;
        int hi = HitIndex(k);
        h.node = hi < 0 ? HomeNode : NodeForHit(hi + Mathf.Max(0, phase));
        h.hit = PatternHit(k) && hi >= 0 && h.node >= 0;
        if (!h.hit) return h;                                   // a wait step: the cube stays on `node` and crouches
        bool silent = false; KeyBlock island = Island;
        if (windowIndex >= 0 && windowIndex < windows.Count) { var w = windows[windowIndex]; silent = w.silent; if (w.island != null) island = w.island; }
        int mod = ModOf(h.node); h.mod = mod;
        h.nudge = NudgeAt(h.node);   // v9 (N)
        switch (mod)
        {
            case 2: h.weight = 0; break;
            case 3: h.weight = 2; break;
            case 4: h.ratchet = 2; break;
            case 5: h.ratchet = 3; break;
            case 7: h.tie = true; break;
            case 8: h.lift = true; break;
        }
        bool audible = mod != 1;
        if (mod == 6 && (Rhythm.Hash(pass, h.node, seed) & 1u) != 0u) { audible = false; h.slip = true; }
        // section rules (§3.4, §3.6): the chord island lit during this step
        KeyBlock section = IsOnMoon ? LitIslandAt(windowIndex, k) : island;
        if (section != null && section.fill && (IsOnMoon ? InLastBarOf(section, windowIndex, k) : InLastBar(windowIndex, k)))
        {
            if (IsDrums) { if (IsAmongLastHits(windowIndex, k, 2)) h.ratchet = Mathf.Max(h.ratchet, 2); }
            else h.echo = Mathf.Max(h.echo, 1);
        }
        if (IsOnMoon && IsDrums && section != null)
        {
            int row = h.node < gridZ.Count ? gridZ[h.node] : 0;
            float local = StepStartLocalBeat(k);
            bool offBeat = Mathf.Abs(local - Mathf.Round(local)) > 1e-3f;
            int mw = Rhythm.MoonWeight(section.energy, row, offBeat);
            if (mw < 0) audible = false; else if (mw == 0) h.weight = 0; else if (mw == 2) h.ratchet = Mathf.Max(h.ratchet, 2);
        }
        h.fires = audible && !muted && !Instruments.Muted[instrument] && !silent;
        return h;
    }

    /// <summary>16th indices (0 .. bars*BeatsPerBar*4) of the firing steps inside a window, from StepStartLocalBeat and Decide (ring pre-light, strip).</summary>
    public List<int> HitSteps16(int windowIndex, int pass)
    {
        var result = new List<int>();
        if (windowIndex < 0 || windowIndex >= windows.Count) return result;
        float length = windows[windowIndex].length;
        int max16 = Mathf.Max(1, Mathf.RoundToInt(length * 4f));
        for (int k = 0; k < 4096; k++)
        {
            float sl = StepStartLocalBeat(k);
            if (sl >= length - 1e-4f) break;
            if (!Decide(windowIndex, k, pass).fires) continue;
            int s16 = Mathf.Clamp(Mathf.RoundToInt(sl * 4f), 0, max16 - 1);
            if (result.Count == 0 || result[result.Count - 1] != s16) result.Add(s16);
        }
        return result;
    }

    /// <summary>
    /// Replaces the windows list (called only by SongManager.RecomputeMeasureStarts). When the list actually changes it
    /// resets the step/window cursors and cancels this cube's pending notes; an identical list is a no-op so drawing
    /// another cube never cuts the sounding ones. startBeatOffset/measureLength mirror windows[0].
    /// </summary>
    public void SetWindows(List<Window> w)
    {
        bool same = w != null && w.Count == windows.Count;
        if (same)
            for (int i = 0; i < w.Count; i++)
            {
                Window a = w[i], b = windows[i];
                if (a.start != b.start || a.length != b.length || a.island != b.island || a.order != b.order || a.silent != b.silent || a.carried != b.carried || a.hop != b.hop || a.pass != b.pass || a.flow != b.flow) { same = false; break; }
            }
        if (!same) { windows.Clear(); if (w != null) windows.AddRange(w); }
        if (windows.Count > 0) { startBeatOffset = windows[0].start; measureLength = windows[0].length; }
        if (same) return;
        hasCarried = false;
        for (int i = 0; i < windows.Count; i++) if (windows[i].carried) { hasCarried = true; break; }
        hopSig = int.MinValue; hopFrame = -1;   // v6: the adapted tiles are rebuilt on the next read
        motionVersion++;                        // v7: the fling / runner plans follow the windows
        lastStep = -1; lastWindow = -1; windowIdx = 0; hopping = false;
        CancelPending();
    }

    /// <summary>Cubes that never received windows (drawn before the first RecomputeMeasureStarts) get one from the legacy fields.</summary>
    void EnsureWindow()
    {
        if (windows.Count == 0) windows.Add(new Window { start = startBeatOffset, length = measureLength, island = Island, order = Mathf.Max(0, assignedGridIndex), silent = false });
    }

    /// <summary>The tile node <paramref name="node"/> plays in window <paramref name="w"/>: the home tile for a resident or Moon
    /// cube; for a Rider, the window island's tile at the same grid offsets (rows clamped).</summary>
    public TileInteraction TileAt(int w, int node)
    {
        if (node < 0 || node >= nodes.Count) return null;
        TileInteraction home = nodes[node];
        if (w < 0 || w >= windows.Count) return home;
        if (windows[w].carried)
        {
            // v6: a carried window plays the pattern adapted onto its island's chord (hop k from hop k - 1); v7: a flow window by pedal
            var ht = HopTiles(windows[w].hop);
            if (ht != null && node < ht.Count && ht[node] != null) return ht[node];
        }
        else if (climb > 0 && windows[w].island != null && windows[w].island == Island && ClimbedAt(node) != null) return ClimbedAt(node);   // CLIMB: a high point rises
        else if (windows[w].pass >= 1 && windows[w].island != null && windows[w].island == Island && windows[w].island.vary >= 1)
        {
            // v7: a later pass of a varying island plays the varied notes (Harmony.Vary over the island's pitches, cached per pass)
            var vt = VariedTiles(windows[w].pass);
            if (vt != null && node < vt.Count && vt[node] != null) return vt[node];
        }
        KeyBlock isl = windows[w].island;
        if (isl == null || isl == Island || home == null || isl.cols <= 0 || isl.rows <= 0) return home;
        int x = Mathf.Clamp(node < gridX.Count ? gridX[node] : home.gridX, 0, isl.cols - 1);
        int z = Mathf.Clamp(node < gridZ.Count ? gridZ[node] : home.gridZ, 0, isl.rows - 1);
        var t = isl.GetTile(x, z);
        return t != null ? t : home;
    }

    /// <summary>v6 (H implements, SPEC v6 §2.4): the tiles this cube plays on <paramref name="island"/> — its home tiles, or the adapted tiles of a
    /// carried / rider window there (in node order; empty when it never plays there). The world magic draws its ghost stars on them.</summary>
    public List<TileInteraction> TilesOn(KeyBlock island)
    {
        var r = new List<TileInteraction>();
        if (island == null) return r;
        if (island == Island) { r.AddRange(nodes); return r; }
        for (int w = 0; w < windows.Count; w++)
            if (windows[w].island == island) { for (int n = 0; n < nodes.Count; n++) { var t = TileAt(w, n); if (t != null) r.Add(t); } break; }
        return r;
    }

    // ------------------------------------------------------------------ v6 carry (SPEC v6 §2.4 / §5.4): the adapted tiles of the carried windows
    bool hasCarried;
    readonly List<TileInteraction>[] hopTiles = new List<TileInteraction>[ProjectConfig.MaxCarry + 2];
    readonly KeyBlock[] hopIsland = new KeyBlock[ProjectConfig.MaxCarry + 2];
    int hopSig = int.MinValue, hopFrame = -1;
    /// <summary>True when the cube has carried windows (its island carries its patterns onto the next chords).</summary>
    public bool HasCarried => hasCarried;
    /// <summary>Adapted-tile rebuilds so far (tests: the cache holds between changes).</summary>
    public static int HopBuilds;

    /// <summary>The tiles the pattern plays on carry hop <paramref name="hop"/> (1 = the first next chord), node for node; null when there is none.</summary>
    public List<TileInteraction> HopTiles(int hop)
    {
        if (!hasCarried || hop < 1 || hop >= hopTiles.Length) return null;
        if (hopFrame != Time.frameCount || hopSig == int.MinValue)
        {
            hopFrame = Time.frameCount;
            int sig = HopSignature();
            if (sig != hopSig) { hopSig = sig; RebuildHops(); }
        }
        return hopTiles[hop];
    }

    /// <summary>What the adapted tiles depend on: the path, the lengths, the group, the song key and every carried window's island (its chord, register
    /// and grid). A rebuilt island is a new object, so a chord / mood / register edit changes it.</summary>
    int HopSignature()
    {
        unchecked
        {
            int h = 17 + 7 * instrument;
            for (int i = 0; i < nodes.Count; i++) h = h * 31 + (nodes[i] != null ? nodes[i].GetInstanceID() : 0);
            for (int i = 0; i < durs.Count; i++) h = h * 31 + durs[i];
            for (int i = 0; i < mods.Count; i++) h = h * 31 + (mods[i] == 1 ? 1 : 0);
            var key = MusicTheory.KeyOfSong(); h = h * 31 + key.tonic * 2 + (key.minor ? 1 : 0);
            var home = Island; if (home != null) { h = h * 31 + home.GetInstanceID(); h = h * 31 + home.chordRootMIDI; h = h * 31 + home.column; }
            for (int w = 0; w < windows.Count; w++)
            {
                var wi = windows[w];
                if (!wi.carried) continue;
                h = h * 31 + wi.hop; h = h * 31 + (wi.flow ? 7 : 3);
                var isl = wi.island;
                if (isl == null) continue;
                h = h * 31 + isl.GetInstanceID(); h = h * 31 + isl.chordRootMIDI; h = h * 31 + isl.register; h = h * 31 + isl.column;
                h = h * 31 + isl.tiles.Count; h = h * 31 + isl.semitoneList.Count;
            }
            return h;
        }
    }

    void RebuildHops()
    {
        HopBuilds++;
        for (int h = 0; h < hopTiles.Length; h++) { hopTiles[h] = null; hopIsland[h] = null; }
        for (int w = 0; w < windows.Count; w++)
        {
            var wi = windows[w];
            if (wi.carried && wi.island != null && wi.hop >= 1 && wi.hop < hopIsland.Length && hopIsland[wi.hop] == null) hopIsland[wi.hop] = wi.island;
        }
        // v7 §21.3: every carried window is a measure of the LONG GRID (carryStyle is ignored): the pattern goes on by the flow rule — hop k by
        // PEDAL from hop k − 1 ("the harmony basically repeats and sounds like its flowing"): a note the next chord keeps (in that measure's pitches
        // and fitting its chord) stays, only what has to move moves (to the nearest pitch that fits)
        IList<TileInteraction> prev = nodes; KeyBlock prevIsl = Island;
        var key = MusicTheory.KeyOfSong();
        var wts = Harmony.WeightsOf(HasDurations ? durs : null, nodes.Count);
        for (int h = 1; h < hopIsland.Length; h++)
        {
            var target = hopIsland[h];
            if (target == null || prevIsl == null || target.cols <= 0 || target.rows <= 0) break;   // the chain stops where the targets stop
            var list = new List<TileInteraction>(nodes.Count);
            var src = new int[nodes.Count];
            for (int i = 0; i < src.Length; i++) src[i] = ModOf(i) == 1 || i >= prev.Count || prev[i] == null ? -1 : prev[i].midi;
            int root; IList<int> semis;
            if (!Harmony.ChordOf(target, out root, out semis) || semis == null) { root = target.chordRootMIDI; semis = target.semitoneList; }
            var ped = Harmony.AdaptPedal(src, wts, root, semis, key.tonic, key.minor, Harmony.Pool(target));
            for (int i = 0; i < src.Length; i++)
            {
                var p0 = i < prev.Count ? prev[i] : null;
                var near = target.GetTile(Mathf.Clamp(p0 != null ? p0.gridX : (i < gridX.Count ? gridX[i] : 0), 0, target.cols - 1), Mathf.Clamp(p0 != null ? p0.gridZ : (i < gridZ.Count ? gridZ[i] : 0), 0, target.rows - 1));
                list.Add(ped[i] < 0 ? near : (CubeOps.TileFor(target, ped[i], near, false) ?? near));
            }
            hopTiles[h] = list; prev = list; prevIsl = target;
        }
    }

    // ---- CLIMB (DeaCube/Climb.cs): the tiles the path's climbing high points rise to (null = the node stays) — rebuilt when the path, its rests,
    // the climb or the grid (its chord, register, column, tiles) change
    readonly List<TileInteraction> climbed = new List<TileInteraction>();
    int climbSig = int.MinValue, climbFrame = -1;
    /// <summary>Climbed-tile rebuilds so far (tests: the cache holds between changes).</summary>
    public static int ClimbBuilds;

    /// <summary>The tile node <paramref name="node"/> climbs to while the path plays on its own grid, or null when it stays (climb off, not one of
    /// the climbing high points, or nowhere higher on the grid).</summary>
    public TileInteraction ClimbedAt(int node)
    {
        if (climb <= 0 || node < 0 || node >= nodes.Count) return null;
        if (climbFrame != Time.frameCount || climbSig == int.MinValue)
        {
            climbFrame = Time.frameCount;
            int sig = ClimbSignature();
            if (sig != climbSig) { climbSig = sig; BuildClimbed(); }
        }
        return node < climbed.Count ? climbed[node] : null;
    }

    int ClimbSignature()
    {
        unchecked
        {
            var kb = Island;
            int h = 29 + 13 * climb;
            for (int i = 0; i < nodes.Count; i++) h = h * 31 + (nodes[i] != null ? nodes[i].GetInstanceID() : 0);
            for (int i = 0; i < mods.Count; i++) h = h * 31 + (mods[i] == 1 ? 1 : 0);
            if (kb != null)
            {
                h = h * 31 + kb.GetInstanceID(); h = h * 31 + kb.chordRootMIDI; h = h * 31 + kb.register; h = h * 31 + kb.column; h = h * 31 + kb.tiles.Count;
                foreach (int s in kb.semitoneList) h = h * 31 + s;
            }
            return h;
        }
    }

    void BuildClimbed()
    {
        ClimbBuilds++;
        climbed.Clear();
        var kb = Island;
        int n = nodes.Count;
        for (int i = 0; i < n; i++) climbed.Add(null);
        if (kb == null || n == 0) return;
        var src = new int[n];
        for (int i = 0; i < n; i++) src[i] = ModOf(i) == 1 || nodes[i] == null ? -1 : nodes[i].midi;
        int root; IList<int> semis;
        if (!Harmony.ChordOf(kb, out root, out semis) || semis == null) { root = kb.chordRootMIDI; semis = kb.semitoneList; }
        var avail = new HashSet<int>();
        foreach (var t in kb.tiles) if (t != null) avail.Add(t.midi);
        var plan = Climb.Plan(src, climb, root, semis, avail);
        for (int i = 0; i < n; i++)
        {
            if (plan[i] < 0 || nodes[i] == null) continue;
            var t = CubeOps.TileFor(kb, plan[i], nodes[i], false);
            if (t != null && t.midi == plan[i]) climbed[i] = t;
        }
    }

    // ---- v7 vary (SPEC v7 §2.6): "the same grid ... repeating itself ... with some differences" — a later pass of an island with vary ≥ 1
    // plays Harmony.Vary's notes (one or two light notes an octave away or a neighbour; the answer on the last pass with vary 2) on the tiles of
    // those pitches nearest the original ones (a phrase keeps each note's time cell); cached per pass, rebuilt when the path, the lengths, the
    // seed, the key or the island (its chord, register, vary, passes) change.
    readonly List<List<TileInteraction>> varied = new List<List<TileInteraction>>();
    int varySig = int.MinValue, varyFrame = -1;
    /// <summary>Varied-tile rebuilds so far (tests: the cache holds between changes).</summary>
    public static int VaryBuilds;

    /// <summary>v7: the tiles pass <paramref name="pass"/> (1..) of this cube's island plays when the island varies its repeats, node for node
    /// (null when it does not vary: pass 0, vary 0).</summary>
    public List<TileInteraction> VariedTiles(int pass)
    {
        var kb = Island;
        if (kb == null || kb.vary <= 0 || pass <= 0 || nodes.Count == 0 || IsOnMoon || IsDrums) return null;
        if (varyFrame != Time.frameCount || varySig == int.MinValue)
        {
            varyFrame = Time.frameCount;
            int sig = VarySignature();
            if (sig != varySig) { varySig = sig; varied.Clear(); }
        }
        while (varied.Count <= pass) varied.Add(null);
        if (varied[pass] == null) varied[pass] = BuildVaried(pass);
        return varied[pass];
    }

    /// <summary>v7: node <paramref name="node"/> plays another tile than its own in window <paramref name="w"/> (a varied note: W's sparkle).</summary>
    public bool IsVaried(int w, int node)
    {
        if (w < 0 || w >= windows.Count || node < 0 || node >= nodes.Count || windows[w].carried) return false;
        var t = TileAt(w, node);
        return t != null && t != nodes[node];
    }

    /// <summary>The passes this cube plays on its home island (its home windows).</summary>
    int HomePasses()
    {
        int n = 0; var kb = Island;
        for (int w = 0; w < windows.Count; w++) if (!windows[w].carried && windows[w].island == kb) n = Mathf.Max(n, windows[w].pass + 1);
        return Mathf.Max(n, kb != null ? Mathf.Min(kb.Passes, Mathf.Max(1, kb.ColumnPasses)) : 1);
    }

    int VarySignature()
    {
        unchecked
        {
            var kb = Island;
            int h = 23 + 7 * seed;
            for (int i = 0; i < nodes.Count; i++) h = h * 31 + (nodes[i] != null ? nodes[i].GetInstanceID() : 0);
            for (int i = 0; i < durs.Count; i++) h = h * 31 + durs[i];
            for (int i = 0; i < mods.Count; i++) h = h * 31 + (mods[i] == 1 ? 1 : 0);
            var key = MusicTheory.KeyOfSong(); h = h * 31 + key.tonic * 2 + (key.minor ? 1 : 0);
            if (kb != null) { h = h * 31 + kb.GetInstanceID(); h = h * 31 + kb.chordRootMIDI; h = h * 31 + kb.register; h = h * 31 + kb.vary; h = h * 31 + kb.semitoneList.Count; }
            h = h * 31 + HomePasses();
            return h;
        }
    }

    List<TileInteraction> BuildVaried(int pass)
    {
        VaryBuilds++;
        var kb = Island;
        int n = nodes.Count;
        var src = new int[n];
        for (int i = 0; i < n; i++) src[i] = ModOf(i) == 1 || nodes[i] == null ? -1 : nodes[i].midi;
        int root; IList<int> semis;
        if (!Harmony.ChordOf(kb, out root, out semis) || semis == null) { root = kb.chordRootMIDI; semis = kb.semitoneList; }
        var key = MusicTheory.KeyOfSong();
        var v = Harmony.Vary(src, Harmony.WeightsOf(HasDurations ? durs : null, n), pass, HomePasses(), seed, kb.vary, root, semis, key.tonic, key.minor, Harmony.Pool(kb));
        var r = new List<TileInteraction>(n);
        for (int i = 0; i < n; i++)
        {
            if (v[i] < 0 || v[i] == src[i] || nodes[i] == null) { r.Add(nodes[i]); continue; }
            r.Add(CubeOps.TileFor(kb, v[i], nodes[i], false) ?? nodes[i]);
        }
        return r;
    }

    // ---- v7 §21 (the user: "the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid. only explore
    // grids moving from left to right with the long grid that i mentioned earlier"): a cube NEVER leaves its own grid. A carry run is ONE LONG GRID
    // (K joins the grid and the grids it extends over into one platform): the pattern walks on to the right, re-voiced for every measure by the
    // pedal rule (TileAt), and crossing into the next measure is an ORDINARY STEP — the same hop as inside a grid. The v6 carry flights, the v7
    // flow glide, the launch's fling, the stairs runner's exit and every trip onto another grid are gone; a window on a foreign grid (an old rider
    // window) never moves the body.
    /// <summary>v6 legacy, v7 §21: the beats of the trip before a window on a later measure of the long grid when there is a gap before it.</summary>
    public const float FlightBeats = 0.5f;
    /// <summary>v6: the body flying between islands — v7 §21: never (kept for older callers; the positions are the last trip's ends).</summary>
    public bool Flying => false;
    public float FlightU => 0f;
    public KeyBlock FlightFrom => null;
    public KeyBlock FlightTo => null;
    public Vector3 FlightStartPos => tripFrom;
    public Vector3 FlightEndPos => tripTo;
    /// <summary>v6 counters (tests): flights begun / landed — v7 §21: they stay 0.</summary>
    public static int FlightCount, LandCount;

    // ================================================================== v7 (SPEC v7 §5.4 / §12.1 / §21): the motions and HOLD THEN RETURN
    /// <summary>v7: beats of the trip home right after the song resets (over its own grid; a long grid's far measures take longer: ≤ 2 beats).</summary>
    public const float ResetTripBeats = 1f;
    /// <summary>v7: seconds of the trip home after a stop (real time, each cube a little out of sync).</summary>
    public const float StopTripSeconds = 0.5f;
    /// <summary>v7: a layer cube phases in this many beats before each of its notes ...</summary>
    public const float PhaseInBeats = 0.12f;
    /// <summary>... and back out over this many after the note.</summary>
    public const float PhaseOutBeats = 0.2f;
    /// <summary>v7: a layer cube's glass at rest (alpha).</summary>
    public const float GlassAlpha = 0.3f;
    /// <summary>v7 §21: a return trip along the (long) grid skips back in small hops, about one per this many world units.</summary>
    public const float TripHopSpan = 1.6f;

    /// <summary>v7 (tests / the world magic): the body is on a trip this frame (home along its grid, to its next window, the rewind zip, after a
    /// stop), holding outside its windows (and where), zipping (and how far), crossing into the next measure of its long grid.</summary>
    public bool Tripping => tripOn;
    public bool Holding => holdOn;
    public TileInteraction HoldTile => holdOn ? restingTile : null;
    public bool Zipping => zipOn;
    public float ZipU => zipU;
    public bool StopTripping => stopTripT < stopTripDelay + StopTripSeconds;
    /// <summary>v7 §21: the body is stepping over into the next measure of its long grid right now (an ordinary hop).</summary>
    public bool CrossingMeasure => crossOn;
    /// <summary>v7 §21 retired motions (kept for older callers): always false — no glide between grids, no fling, no runner exit.</summary>
    public bool Gliding => false;
    public float GlideU => 0f;
    public bool Flinging => false;
    public bool RunnerExiting => false;
    /// <summary>v7: counters (tests): zips begun, return trips begun (a new trip key), stop trips begun, measure crossings begun; flings stay 0 (§21).</summary>
    public static int FlingCount, ZipCount, TripCount, StopTripCount, CrossCount;
    bool tripOn, holdOn, zipOn, crossOn; Vector3 tripPos, tripFrom, tripTo; float zipU; int zipKey = -1, tripKey = -1, crossKey = -1;
    /// <summary>Bumped by every change that moves the motions' plans (windows, path, lengths, settings).</summary>
    int motionVersion;
    readonly List<Vector3> zipPts = new List<Vector3>(16);

    /// <summary>v7: this cube's delay (beats, 0..ResetStaggerBeats) before its trip home after a reset: "not in sync, but slightly out of sync".</summary>
    public float ResetStagger => SongManager.ResetStagger(id != 0 ? id : owner);   // K's rule: belts, towers, Moons and cubes stagger alike
    /// <summary>v7: the tile the cube lives on when it is home (its home node's tile).</summary>
    public TileInteraction HomeTile => nodes.Count > 0 ? nodes[Mathf.Clamp(HomeNode, 0, nodes.Count - 1)] : null;

    /// <summary>v7 §21: island <paramref name="kb"/> is this cube's grid — its own island or a measure of its LONG GRID (an island its carried windows
    /// play on: K joins those into one platform). Anything else is foreign: the body never stands there.</summary>
    public bool OnMyGrid(KeyBlock kb)
    {
        if (kb == null || kb == Island) return true;
        for (int i = 0; i < windows.Count; i++) if (windows[i].carried && windows[i].island == kb) return true;
        return false;
    }

    /// <summary>v7: where window <paramref name="w"/> leaves the cube: the tile it ends on — on a later measure of its long grid for a carried window.
    /// <paramref name="guest"/> = that measure is not its own island: it stands there without joining that tile's stack (rule 3's octave lift never
    /// re-pitches the measure's own cubes).</summary>
    public TileInteraction EndTileOf(int w, out bool guest)
    {
        guest = false;
        if (w < 0 || w >= windows.Count) return HomeTile;
        var t = TileAtLocal(w, windows[w].length - 1e-3f);
        if (t == null || !OnMyGrid(t.island)) t = HomeTile;
        guest = t != null && t.island != Island;
        return t;
    }

    /// <summary>v7: the last audible window that ended at / before <paramref name="song"/> in this pass through the song (inside the focus loop's
    /// region when one is set); else — <paramref name="wrapped"/> — the latest-ending one of the previous pass (its end one loop earlier).</summary>
    bool PrevAudible(float song, out int p, out double pEnd, out bool wrapped)
    {
        p = -1; pEnd = double.NegativeInfinity; wrapped = false;
        bool region = GlobalClock.HasRegion;
        double lo = GlobalClock.RegionStart, hi = GlobalClock.RegionEnd;
        for (int i = 0; i < windows.Count; i++)
        {
            var wi = windows[i];
            if (wi.silent || !OnMyGrid(wi.island)) continue;   // §21: a window on another grid is never the body's
            if (region && (wi.start < lo - 1e-4 || wi.start >= hi - 1e-4)) continue;
            double end = wi.start + wi.length;
            if (end > song + 1e-4) continue;
            if (end > pEnd) { pEnd = end; p = i; }
        }
        if (p >= 0) return true;
        if (!GlobalClock.LoopSong && !region) return false;
        for (int i = 0; i < windows.Count; i++)
        {
            var wi = windows[i];
            if (wi.silent || !OnMyGrid(wi.island)) continue;
            if (region && (wi.start < lo - 1e-4 || wi.start >= hi - 1e-4)) continue;
            double end = wi.start + wi.length;
            if (end > pEnd) { pEnd = end; p = i; }
        }
        if (p < 0) return false;
        pEnd -= region ? hi - lo : GlobalClock.TotalBeats;
        wrapped = true;
        return true;
    }

    /// <summary>
    /// v7 (SPEC v7 §12.1 / §21) — outside every audible window: the cube HOLDS where its last window left it (its last tile; a walking cube on the
    /// long grid's measure it ended on) and goes home only (a) right before its grid plays again — a trip that lands exactly on the next window's
    /// first note (≤ ReturnBeats; FlightBeats before a later measure of its long grid) — or (b) right after the song resets (the loop point): every
    /// holding cube travels home ALONG its grid over ≈ a beat (a far measure of a long grid up to two), each ResetStagger late; a cube whose next
    /// window starts at the reset has already travelled in the last beats before it (a). The first time through (no loop yet) a cube that has not
    /// played is home. A pure function of the song beat: seeks and loops land right. Never onto another grid.
    /// </summary>
    void OutsidePose(float song, int nW, double nStart, float dt)
    {
        int p; double pEnd; bool wrapped;
        bool hasPrev = PrevAudible(song, out p, out pEnd, out wrapped);
        if (hasPrev && wrapped && GlobalClock.LoopIndex <= 0) hasPrev = false;   // nothing has played yet this time through: the cube is home
        if (nW >= 0 && !OnMyGrid(windows[nW].island)) nW = -1;                    // §21: a window on another grid: the body stays on its own
        var home = HomeTile;
        bool guest = false;
        TileInteraction hold = hasPrev ? EndTileOf(p, out guest) : home;
        if (hold == null) hold = home;
        if (hold == null) return;
        double since = hasPrev ? pEnd : double.NegativeInfinity;
        // (b) the song reset: home along its grid, slightly out of sync
        if (hasPrev && wrapped && hold != home && home != null)
        {
            double rs = GlobalClock.LoopStartBeat + ResetStagger;
            double re = rs + ResetTripOf(hold, home);
            bool nextFirst = nW >= 0 && nStart - ReturnBeatsFor(nW) < re + 1e-3;   // its grid plays before the trip would end: straight there (a)
            if (!nextFirst)
            {
                if (song < rs) { Hold(hold, guest, dt); return; }
                if (song < re) { Trip(hold, home, (float)((song - rs) / (re - rs)), 900000 + p); return; }
                hold = home; guest = false; since = re;
            }
        }
        // (a) right before it plays again: a trip that lands on the next window's first note
        if (nW >= 0)
        {
            var to = TileAtLocal(nW, 0f);
            if (to != null && to != hold && OnMyGrid(to.island))
            {
                double T = Math.Min(ReturnBeatsFor(nW), nStart - since);
                if (T > 1e-3 && song >= nStart - T)
                {
                    Trip(hold, to, (float)((song - (nStart - T)) / T), 800000 + nW * 4 + (hasPrev ? 1 : 0));
                    return;
                }
            }
        }
        Hold(hold, guest, dt);
    }

    /// <summary>Beats of the trip before window <paramref name="w"/>: FlightBeats before a later measure of the long grid, else ReturnBeats.</summary>
    float ReturnBeatsFor(int w) => w >= 0 && w < windows.Count && windows[w].carried ? FlightBeats : ProjectConfig.ReturnBeats;

    /// <summary>Beats of the trip home after a reset from <paramref name="from"/>: a beat over its own measure, up to two from a far measure of its long grid.</summary>
    float ResetTripOf(TileInteraction from, TileInteraction to)
    {
        if (from == null || to == null || from.island == to.island) return ResetTripBeats;
        Vector3 d = to.Top - from.Top; d.y = 0f;
        return Mathf.Clamp(d.magnitude / 10f, ResetTripBeats, 2f);
    }

    Vector3 PoseOn(TileInteraction t) => t != null ? RestPosition(t, StackSlot(t)) : transform.position;

    /// <summary>Holds on <paramref name="t"/> (a guest does not join the tile's stack).</summary>
    void Hold(TileInteraction t, bool guest, float dt)
    {
        holdOn = true;
        SettleOn(t, dt, !guest);
    }

    /// <summary>A return trip from <paramref name="from"/> onto <paramref name="to"/> at <paramref name="u"/> (0..1), always along the cube's own
    /// (long) grid: up its stairs step by step on a stairs island, else a hop-glide over the platform (small hops, no arc).</summary>
    void Trip(TileInteraction from, TileInteraction to, float u, int key)
    {
        if (to == null || from == null) return;
        if (key != tripKey) { tripKey = key; TripCount++; }
        if (from.island == to.island && from.island != null && from.island.IsStairs) StairClimb(from, to, u);
        else HopGlide(PoseOn(from), to, u);
        tripOn = true; tripPos = transform.position;
    }

    /// <summary>v7 §21: a hop-glide from <paramref name="from"/> onto <paramref name="toTile"/> along the platform — eased, skipping back in small
    /// hops (about one per <see cref="TripHopSpan"/>, each a quarter-roll), never a big arc: the trip home over its own grid / long grid.</summary>
    void HopGlide(Vector3 from, TileInteraction toTile, float u)
    {
        hopping = false; SetResting(null); leanDir = Vector3.zero;
        u = Mathf.Clamp01(u);
        Vector3 to = RestPosition(toTile, StackSlot(toTile));
        Vector3 d = to - from; d.y = 0f;
        int hops = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / TripHopSpan));
        float e = u * u * (3f - 2f * u);
        float hu = e * hops, frac = hu - Mathf.Floor(hu);
        if (u >= 1f) frac = 0f;
        float h = 0.32f + Mathf.Max(0f, to.y - from.y) * 0.5f / hops;
        transform.position = Vector3.Lerp(from, to, e) + Vector3.up * (Mathf.Sin(frac * Mathf.PI) * h);
        transform.rotation = d.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(90f * hu, Vector3.Cross(Vector3.up, d.normalized)) : Quaternion.identity;
        tripFrom = from; tripTo = to;
    }

    /// <summary>v7 §21: a runner climbing back up its own stairs (after the reset, or before its stairs play again): one hop per step, from
    /// <paramref name="from"/>'s step to <paramref name="to"/>'s, evenly over the trip.</summary>
    void StairClimb(TileInteraction from, TileInteraction to, float u)
    {
        hopping = false; SetResting(null); leanDir = Vector3.zero;
        u = Mathf.Clamp01(u);
        var kb = from.island;
        int a = from.gridX, b = to.gridX, n = Mathf.Abs(b - a);
        if (n == 0) { HopGlide(PoseOn(from), to, u); return; }
        int dir = b > a ? 1 : -1;
        float hu = u * n;
        int j = Mathf.Min(n - 1, Mathf.FloorToInt(hu));
        float f = u >= 1f ? 1f : hu - j;
        var ta = kb.GetTile(a + dir * j, 0); var tb = kb.GetTile(a + dir * (j + 1), 0);
        if (ta == null || tb == null) { HopGlide(PoseOn(from), to, u); return; }
        Vector3 pa = RestPosition(ta, StackSlot(ta)), pb = RestPosition(tb, StackSlot(tb));
        float fe = f * f * (3f - 2f * f);
        float arc = Mathf.Max(0.3f, (pb.y - pa.y) + 0.28f);
        transform.position = Vector3.Lerp(pa, pb, fe) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * arc);
        Vector3 d = pb - pa; d.y = 0f;
        transform.rotation = d.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(90f * fe, Vector3.Cross(Vector3.up, d.normalized)) : Quaternion.identity;
        tripFrom = PoseOn(from); tripTo = PoseOn(to);
    }

    /// <summary>v7 (tests): the tile window <paramref name="w"/>'s first note stands on.</summary>
    public TileInteraction FirstTileOf(int w) => w >= 0 && w < windows.Count ? TileAtLocal(w, 0f) : null;

    /// <summary>v7 (SPEC v7 §5.4) — a runner's move from step to step: on a falling stair it DROPS (off the edge, then falling faster under gravity
    /// onto the lower step — the landing squash and bounce come with the note); on a climbing stair it hops up with an arc that clears the step.</summary>
    void StairHop(TileInteraction ta, TileInteraction tb, float f)
    {
        if (!hopping)
        {
            hopping = true;
            hopFrom = RestPosition(ta, StackSlot(ta));
            hopSpace = ta != null && ta.island != null ? ta.island.transform : null;
            hopFromLocal = hopSpace != null ? hopSpace.InverseTransformPoint(hopFrom) : hopFrom;
            SetResting(null);
        }
        else if (hopSpace != null) hopFrom = hopSpace.TransformPoint(hopFromLocal);
        hopTarget = tb;
        f = Mathf.Clamp01(f);
        Vector3 end = RestPosition(tb, SequenceMaster.CountOn(tb, this));
        Vector3 d = end - hopFrom; d.y = 0f;
        Vector3 axis = d.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, d.normalized) : Vector3.right;
        float drop = hopFrom.y - end.y;
        if (drop > 0.05f)
        {
            // off the edge (x / z ease out) and a short fall under gravity: a small lift, then y ∝ f² down onto the step
            float xe = 1f - (1f - f) * (1f - f);
            Vector3 p = Vector3.Lerp(hopFrom, end, xe);
            p.y = hopFrom.y + 4f * 0.3f * f * (1f - f) - drop * f * f;   // v9 (S17 / -60): a taller pre-fall lift for the taller steps
            transform.position = p;
            transform.rotation = Quaternion.AngleAxis(-18f * Mathf.Sin(f * Mathf.PI), axis) * Quaternion.AngleAxis(25f * f, axis);   // leans into the fall, lands tipped forward
        }
        else
        {
            float fe = f * f * (3f - 2f * f);
            float arc = Mathf.Max(ProjectConfig.cubeHopIntensity, -drop + 0.28f);
            transform.position = Vector3.Lerp(hopFrom, end, fe) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * arc);
            transform.rotation = Quaternion.AngleAxis(90f * fe, axis);
        }
    }

    /// <summary>
    /// v7 (SPEC v7 §2.6 / §5.4) — REWIND: "the grid is repeating itself and time unwinding to the beginning everytime". While SongManager.RewindPhase
    /// runs for this window's island (the last RewindBeats of every pass but its last) the body zips BACKWARD through its path — from where the
    /// unwind found it, back over the path's tiles to its first (slow, then fast) with a backward roll — and sits on the first note's tile of the
    /// next pass at the boundary. The notes still sound (the audio is untouched). True when it zipped.
    /// </summary>
    bool RewindZip(int a, float song)
    {
        var win = windows[a];
        var kb = win.island;
        var sm = SongManager.I;
        if (kb == null || !kb.rewind || sm == null) return false;
        int rp;
        float ph = sm.RewindPhase(kb, song, out rp);
        if (ph < 0f) return false;
        ZipPose(a, Mathf.Max(0f, win.length - (float)SongManager.RewindBeatsOf(win.length)), Mathf.Clamp01(ph));
        return true;
    }

    /// <summary>The zip's pose at <paramref name="phase"/> (0..1) of the unwind that starts at window-local beat <paramref name="zipStart"/> of
    /// window <paramref name="a"/> (public for tests: <see cref="ZipPoint"/>).</summary>
    void ZipPose(int a, float zipStart, float phase)
    {
        Vector3 pos, dir; float u;
        ZipPoint(a, zipStart, phase, out pos, out dir, out u);
        hopping = false; SetResting(null); leanDir = Vector3.zero;
        transform.position = pos;
        Vector3 axis = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.right;
        transform.rotation = Quaternion.AngleAxis(-360f * u, axis);   // one backward roll: the tape runs backward
        int key = a * 2 + 1;
        if (key != zipKey) { zipKey = key; ZipCount++; }
        zipOn = true; zipU = u;
        tripOn = true; tripPos = pos;
    }

    /// <summary>v7: the rewind zip's point at <paramref name="phase"/> 0..1 of window <paramref name="a"/>'s unwind (from where the body is at
    /// window-local <paramref name="zipStart"/>, back through the path to the next pass's first tile): <paramref name="u"/> = the eased distance
    /// along it (slow then fast), <paramref name="dir"/> = the travel direction there.</summary>
    public void ZipPoint(int a, float zipStart, float phase, out Vector3 pos, out Vector3 dir, out float u)
    {
        pos = transform.position; dir = Vector3.zero; u = 0f;
        if (a < 0 || a >= windows.Count || nodes.Count == 0) return;
        zipPts.Clear();
        zipPts.Add(PosAtLocal(a, zipStart));
        int k0; float pr;
        ComputeStep(Mathf.Clamp(zipStart, 0f, Mathf.Max(0f, windows[a].length - 1e-3f)), out k0, out pr);
        var h0 = Decide(a, k0, PassOf(a, 0));
        int n = nodes.Count, cur = Mathf.Clamp(h0.node >= 0 ? h0.node : EndNode, 0, n - 1), home = Mathf.Clamp(HomeNode, 0, n - 1);
        int stepDir = reverse ? 1 : -1;
        for (int j = cur, guard = 0; guard <= n; j += stepDir, guard++)
        {
            if (j < 0 || j >= n) break;
            var t = TileAt(a, j);
            if (t != null) zipPts.Add(RestPosition(t, StackSlot(t)));
            if (j == home) break;
        }
        // the end: the next pass's first note (its first tile)
        int w; double nStart; int loops;
        var win = windows[a];
        if (NextAudibleWindowFrom(win.start + win.length - 1e-4f, out w, out nStart, out loops) && Math.Abs(nStart - (win.start + win.length)) < 1e-3 && windows[w].island == win.island)
        {
            var t = TileAtLocal(w, 0f);
            if (t != null) zipPts.Add(RestPosition(t, StackSlot(t)));
        }
        u = Mathf.Pow(Mathf.Clamp01(phase), 2.2f);   // slow, then fast
        float total = 0f;
        for (int i = 1; i < zipPts.Count; i++) total += (zipPts[i] - zipPts[i - 1]).magnitude;
        float at = u * total;
        pos = zipPts[zipPts.Count - 1];
        for (int i = 1; i < zipPts.Count; i++)
        {
            float seg = (zipPts[i] - zipPts[i - 1]).magnitude;
            if (at <= seg || i == zipPts.Count - 1)
            {
                pos = seg > 1e-5f ? Vector3.Lerp(zipPts[i - 1], zipPts[i], Mathf.Clamp01(at / seg)) : zipPts[i];
                dir = zipPts[i] - zipPts[i - 1]; dir.y = 0f;
                break;
            }
            at -= seg;
        }
        pos += Vector3.up * (0.18f * Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI));
    }

    /// <summary>v7 — how a window ends: the rewind zip (time unwinding), else a step across the boundary when the next window starts right here on
    /// the same grid or the next measure of its long grid. True when one of them took the pose.</summary>
    bool TransitionOut(int a, float song, float local, int k, Hit cur)
    {
        if (RewindZip(a, song)) return true;
        return CrossStep(a, local, k, cur);
    }

    /// <summary>v7 / §21: the next window starts right where this one ends — the next pass on the same grid (a repeat, the belt; a single window
    /// across the loop point) or the next MEASURE of its long grid: the cube steps from its last tile onto that window's first tile at the end of its
    /// last note, the same hop as any next note (a runner: the same drop / climb). True when it stepped.</summary>
    bool CrossStep(int a, float local, int k, Hit cur)
    {
        var win = windows[a];
        if (StepStartLocalBeat(k + 1) < win.length - 1e-4f) return false;   // not its last stay in the window yet
        int w; double nStart; int loops;
        if (!NextAudibleWindowFrom(win.start + win.length - 1e-4f, out w, out nStart, out loops) || w == a && loops == 0) return false;
        if (Math.Abs(nStart - (win.start + win.length)) > 1e-3) return false;   // a gap: OutsidePose travels in it
        var nw = windows[w];
        if (nw.island != win.island && !(OnMyGrid(nw.island) && OnMyGrid(win.island))) return false;   // §21: never onto another grid
        var ta = TileAt(a, cur.node >= 0 ? cur.node : EndNode);
        var tb = TileAtLocal(w, 0f);
        if (ta == null || tb == null || ta == tb) return false;
        float s0 = StepStartLocalBeat(k), len = Mathf.Max(1e-3f, win.length - s0);
        float pr = Mathf.Clamp01((local - s0) / len);
        float hs = Mathf.Max(ProjectConfig.snapThreshold, 1f - ProjectConfig.HopMaxBeats / len);
        if (pr < hs) return false;
        float f = (pr - hs) / Mathf.Max(1e-4f, 1f - hs);
        if (win.island != null && win.island.IsStairs) StairHop(ta, tb, f);
        else Hop(ta, tb, f, 1f, len <= 0.25f ? 0.6f : 1f);
        if (nw.island != win.island)
        {
            crossOn = true;   // the long grid's next measure
            int key = w * 8 + loops;
            if (key != crossKey) { crossKey = key; CrossCount++; }
        }
        return true;
    }

    // ---- v7 the octave layer: glass at rest, phased in around its notes (a pure function of the song beat)
    float layerPhase, glassPhase; bool glassOn; Material glassMat; ShadowCastingMode glassShadow = ShadowCastingMode.On;
    float birthT = 9f;
    /// <summary>v7: how far the layer cube is phased in right now (0 glass … 1 solid); 1 for a cube on its grid.</summary>
    public float LayerPhase => layer != 0 ? glassPhase : 1f;
    /// <summary>v7: true while the body is drawn as layer glass.</summary>
    public bool GlassShown => glassOn;

    /// <summary>The phase inside window <paramref name="a"/> at local beat <paramref name="local"/> (step <paramref name="k"/>): solid through a
    /// sounding note, fading out <see cref="PhaseOutBeats"/> after it, phasing in <see cref="PhaseInBeats"/> before the next one.</summary>
    float PhaseInWindow(int a, int k, float local, Hit cur, int pass)
    {
        if (layer == 0) return 1f;
        var win = windows[a];
        float ph = 0f;
        float s0 = StepStartLocalBeat(k);
        if (cur.hit && cur.fires)
        {
            float end = win.length;
            if (HasDurations) end = Mathf.Min(win.length, StepStartLocalBeat(k + 1));
            else { int nx = NextPatternHitStep(k, win.length); if (nx >= 0) end = StepStartLocalBeat(nx); }
            ph = local < end ? 1f : 1f - Mathf.Clamp01((local - end) / PhaseOutBeats);
        }
        else if (k > 0 && local - s0 < PhaseOutBeats)
        {
            var prev = Decide(a, k - 1, pass);
            if (prev.hit && prev.fires) ph = 1f - Mathf.Clamp01((local - s0) / PhaseOutBeats);
        }
        int nh = NextPatternHitStep(k, win.length);
        if (nh >= 0)
        {
            float d = StepStartLocalBeat(nh) - local;
            if (d < PhaseInBeats && Decide(a, nh, pass).fires) ph = Mathf.Max(ph, 1f - Mathf.Clamp01(d / PhaseInBeats));
        }
        else
        {
            int w; double nStart; int loops;
            if (NextAudibleWindowFrom(win.start + win.length - 1e-4f, out w, out nStart, out loops) && !(w == a && loops == 0))
                ph = Mathf.Max(ph, LeadInPhase(w, nStart, win.start + local));
        }
        return ph;
    }

    /// <summary>The phase-in before window <paramref name="w"/>'s first note (starting at <paramref name="nStart"/>) at song beat <paramref name="song"/>.</summary>
    float LeadInPhase(int w, double nStart, float song)
    {
        if (layer == 0 || w < 0 || w >= windows.Count) return 1f;
        double d = nStart - song;
        if (d >= PhaseInBeats || d < 0) return 0f;
        var h = Decide(w, 0, PassOf(w, 0));
        return h.hit && h.fires ? 1f - (float)(d / PhaseInBeats) : 0f;
    }

    /// <summary>The layer glass: the hologram shader at rest alpha <see cref="GlassAlpha"/> (bright rounded edges, a fresnel rim — "faint glass"),
    /// brightening toward solid as the cube phases in; fully phased in, the solid toon body with its glow. A new copy phases in at birth.</summary>
    void UpdateGlass(float dt)
    {
        if (birthT < 9f) birthT += dt;
        float birth = birthT < 0.6f ? 1f - birthT / 0.6f : 0f;
        glassPhase = Mathf.Max(layerPhase, birth);
        bool want = layer != 0 && !holoOn && dissolveT < 0f && mat != null && rend != null && glassPhase < 0.999f;
        if (!want) { if (glassOn) GlassOff(); return; }
        if (!glassOn) GlassOn();
        float e = Ease.OutCubic(glassPhase);
        glassMat.SetColor(ColorId, Color);
        glassMat.SetFloat(AlphaId, Mathf.Lerp(GlassAlpha, 1f, e));
        glassMat.SetFloat(FlashId, 0.4f * e);
        glassMat.SetFloat(GlitchId, 0f);
        glassMat.SetFloat(WipeId, 0f);
    }

    void GlassOn()
    {
        glassOn = true;
        if (holoShader == null) holoShader = Shader.Find("DeaCube/Hologram");
        if (glassMat == null)
        {
            glassMat = holoShader != null ? new Material(holoShader) : Fx.Additive(IconFactory.GetTexture("white"), Color, 0.6f);
            glassMat.SetFloat(SeedId, owner % 97 + 5);
            if (glassMat.HasProperty("_ScanDensity")) glassMat.SetFloat("_ScanDensity", 70f);
        }
        if (rend != null) { glassShadow = rend.shadowCastingMode; rend.sharedMaterial = glassMat; rend.shadowCastingMode = ShadowCastingMode.Off; }
    }

    void GlassOff()
    {
        glassOn = false;
        if (rend != null && mat != null) { rend.sharedMaterial = mat; rend.shadowCastingMode = glassShadow; }
    }

    // ---- v7 the birth glide (an octave copy / harmony cube leaving its source) and the stop trip (real time)
    Vector3 appearFrom, appearOff, extraApplied; float appearT = 9f, appearDur = 0.45f; bool appearFirst;
    /// <summary>v7: the body glides from <paramref name="from"/> into its pose over <paramref name="seconds"/> (real time; an additive offset that
    /// decays, so it composes with whatever the song does meanwhile) and phases in: a copy rising / sinking out of its source into its layer.</summary>
    public void Appear(Vector3 from, float seconds)
    {
        appearFrom = from; appearT = 0f; appearDur = Mathf.Max(0.05f, seconds); appearFirst = true; birthT = 0f;
    }

    void ApplyAppear(float dt)
    {
        extraApplied = Vector3.zero;
        if (appearT >= appearDur) return;
        if (appearFirst) { appearFirst = false; appearOff = appearFrom - transform.position; CubeSmear.Cut(owner); }
        appearT += dt;
        float u = Mathf.Clamp01(appearT / appearDur);
        extraApplied = appearOff * (1f - Ease.OutCubic(u)) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.35f);
        transform.position += extraApplied;
    }

    float stopTripT = 9f, stopTripDelay; Vector3 stopFrom, stopOff; bool stopFirst; Quaternion stopFromRot = Quaternion.identity; float stopArc; int stopHops = 1;
    bool forcedHome;
    /// <summary>v7: the transport was stopped / reset (cubes stay home, a seek included) until the next play; a pause keeps the pose of its beat.
    /// Shared, so a cube made while stopped joins its neighbours at home.</summary>
    static bool transportHome = true;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMotionStatics() { transportHome = true; FlingCount = 0; ZipCount = 0; TripCount = 0; StopTripCount = 0; VaryBuilds = 0; }

    /// <summary>v7: stopped (or reset): home — after a stop the trip there runs in real time (<see cref="StopTripSeconds"/>, each cube a little out of
    /// sync), a hop-glide over its grid or an arc from another one. No snap.</summary>
    void StoppedPose(float dt)
    {
        wasActive = false; lastStep = -1; lastWindow = -1; hopping = false; ratchetLeft = 0; echoLeft = 0; leanDir = Vector3.zero;
        asleep = false; noteNode = HomeNode; noteProgress = 0f; layerPhase = 0f;
        var home = HomeTile;
        if (home == null) return;
        SettleOn(home, dt);
    }

    /// <summary>v7: the trip home after a stop / reset, in real time — an offset from where the cube stood to its pose that decays over
    /// <see cref="StopTripSeconds"/> (after its small delay), with a hop's lift: the cube moves home whether the song then stays stopped or plays
    /// on from the start (the transport's rewind). Applied after the pose; taken off before the next one.</summary>
    void ApplyStopTrip()
    {
        if (stopTripT >= stopTripDelay + StopTripSeconds) return;
        if (stopFirst)
        {
            stopFirst = false; stopOff = stopFrom - transform.position;
            if (stopOff.sqrMagnitude < 0.0025f) { stopTripT = 9f; return; }   // the new pose is where it stood (a play from there): nothing to travel
        }
        stopTripT += Time.unscaledDeltaTime;
        float u = Mathf.Clamp01((stopTripT - stopTripDelay) / StopTripSeconds);
        float e = u * u * (3f - 2f * u);
        float hu = e * stopHops, frac = u >= 1f ? 0f : hu - Mathf.Floor(hu);
        Vector3 off = stopOff * (1f - e) + Vector3.up * (Mathf.Sin(frac * Mathf.PI) * stopArc);
        transform.position += off;
        extraApplied += off;
        transform.rotation = Quaternion.Slerp(stopFromRot, transform.rotation, e);
        tripOn = true; tripPos = transform.position;
    }

    /// <summary>v7 tests: every cube straight home (the v6 snap: no trip).</summary>
    public static void SnapAllHome() { foreach (var c in SequenceMaster.Cubes) if (c != null) c.SnapHome(); }
    /// <summary>v7: straight home, no trip (the v6 ResetToStart).</summary>
    public void SnapHome()
    {
        ResetToStart();
        stopTripT = 9f;
        var home = HomeTile;
        if (home != null && dropT >= 1f && previewT >= 1f)
        {
            SetResting(home);
            transform.position = RestPosition(home, StackSlot(home));
            transform.rotation = Quaternion.identity;
            CubeSmear.Cut(owner);
        }
    }

    // ---- v7 runners (SPEC v7 §2.4): a stairs island's cube always plays its run (SongManager.StairPath); when the run changes under it (the meter,
    // the island's bars, a stairs field) it takes the new path — O's SetStair re-derives it in the same rebuild; this is the safety net
    int runnerSig = int.MinValue;
    /// <summary>v7: re-derives a runner's path from SongManager.StairPath when it differs (no History: the op that changed the run pushes). True
    /// when it changed.</summary>
    public bool RefreshRunner()
    {
        var sm = SongManager.I;
        var kb = Island;
        if (sm == null || kb == null || !kb.IsStairs || IsOnMoon || !isFinalized) return false;
        int[] xs, ms, ds;
        sm.StairPath(kb, out xs, out ms, out ds);
        if (xs == null || xs.Length == 0) return false;
        bool same = xs.Length == nodes.Count && HasDurations;
        for (int i = 0; same && i < xs.Length; i++) same = nodes[i] != null && nodes[i].gridX == xs[i] && ModOf(i) == ms[i] && durs[i] == ds[i];
        if (same) return false;
        var tiles = new List<TileInteraction>(xs.Length);
        for (int i = 0; i < xs.Length; i++) { var t = kb.GetTile(Mathf.Clamp(xs[i], 0, kb.cols - 1), 0); if (t != null) tiles.Add(t); }
        if (tiles.Count != xs.Length) return false;
        SetPathAndMods(tiles, new List<int>(ms), ds, new List<int>());
        return true;
    }

    void CheckRunner()
    {
        var kb = Island;
        if (kb == null || !kb.IsStairs || !isFinalized) return;
        int sig;
        unchecked { sig = kb.GetInstanceID() * 31 + kb.stairSteps * 7 + kb.stairRate * 131 + (kb.stairLead ? 1 : 2) * 1009 + kb.bars * 17 + GlobalClock.BeatsPerBar * 3; }
        if (sig == runnerSig) return;
        runnerSig = sig;
        RefreshRunner();
    }

    /// <summary>The tile the cube stands on at window-local beat <paramref name="local"/> of window <paramref name="w"/> (after a Once pass: its end).</summary>
    TileInteraction TileAtLocal(int w, float local)
    {
        var win = windows[w];
        int k; float pr;
        ComputeStep(Mathf.Clamp(local, 0f, Mathf.Max(0f, win.length - 1e-3f)), out k, out pr);
        var h = Decide(w, k, PassOf(w, 0));
        int node = h.node >= 0 ? h.node : EndNode;
        return TileAt(w, Mathf.Clamp(node, 0, nodes.Count - 1));
    }

    /// <summary>Where the playback pose puts the body at window-local beat <paramref name="local"/> of window <paramref name="w"/> (on its tile, or
    /// mid-hop toward the next note): a pure read of UpdatePlayback's math (where a flight leaves from).</summary>
    Vector3 PosAtLocal(int w, float local)
    {
        var win = windows[w];
        local = Mathf.Clamp(local, 0f, Mathf.Max(0f, win.length - 1e-3f));
        int k; float p;
        ComputeStep(local, out k, out p);
        int pass = PassOf(w, 0);
        Hit cur = Decide(w, k, pass);
        var ta = TileAt(w, cur.node >= 0 ? cur.node : EndNode);
        if (ta == null) return transform.position;
        Vector3 pa = RestPosition(ta, StackSlot(ta));
        int nh = NextPatternHitStep(k, win.length);
        bool stepEndsWindow = StepStartLocalBeat(k + 1) >= win.length - 1e-4f;
        Hit nb = nh >= 0 ? Decide(w, nh, pass) : new Hit { node = -1 };
        float hs = ProjectConfig.snapThreshold, arcScale = StepBeats <= 0.25f ? 0.6f : 1f;
        if (HasDurations)
        {
            float len = StepLenBeats(k);
            hs = Mathf.Max(ProjectConfig.snapThreshold, 1f - ProjectConfig.HopMaxBeats / Mathf.Max(1e-4f, len));
            arcScale = len <= 0.25f ? 0.6f : 1f;
        }
        if (cur.node < 0 || !(nh == k + 1 && nb.node >= 0 && nb.node != cur.node && !stepEndsWindow && p >= hs)) return pa;
        var tb = TileAt(w, nb.node);
        if (tb == null) return pa;
        float f = (p - hs) / Mathf.Max(1e-4f, 1f - hs), fe = f * f * (3f - 2f * f);
        return Vector3.Lerp(pa, RestPosition(tb, StackSlot(tb)), fe) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * ProjectConfig.cubeHopIntensity * arcScale * HopArcMul(nb));
    }

    // ------------------------------------------------------------------ v6 voice
    /// <summary>v6 (SPEC v6 §2.7): the cube's sound inside its group (clamped to the group's voices); the queued notes are re-decided with it
    /// (the sounding ones ring out). Notifies; no History (the gesture owner pushes).</summary>
    public void SetVoice(int v)
    {
        int nv = Mathf.Clamp(v, 0, Mathf.Max(0, Instruments.VoiceCount(instrument) - 1));
        if (nv == voice) return;
        voice = nv;
        SoftReschedule();
        OnAnyChanged?.Invoke(this);
    }

    int PassOf(int w, int loops) => (GlobalClock.LoopIndex + loops) * 64 + (w >= 0 && w < windows.Count ? windows[w].order : 0);

    static bool Contains(Window w, float song) => song >= w.start && song < w.start + w.length;

    int FindWindow(float song)
    {
        int n = windows.Count;
        if (n == 0) return -1;
        if (windowIdx >= 0 && windowIdx < n && Contains(windows[windowIdx], song)) return windowIdx;
        for (int i = 0; i < n; i++) if (Contains(windows[i], song)) { windowIdx = i; return i; }
        return -1;
    }

    /// <summary>The first audible window starting at/after <paramref name="beat"/>, else (looping) the first audible one next pass.
    /// v4 R4: inside a focus loop only windows starting in the region count, and the next pass starts one region length later.</summary>
    bool NextAudibleWindowFrom(float beat, out int w, out double startBeat, out int loops)
    {
        w = -1; startBeat = -1.0; loops = 0;
        bool region = GlobalClock.HasRegion;
        double lo = GlobalClock.RegionStart, hi = GlobalClock.RegionEnd;
        int best = -1;
        for (int i = 0; i < windows.Count; i++)
        {
            var wi = windows[i];
            if (wi.silent || wi.start < beat - 1e-4f) continue;
            if (region && (wi.start < lo - 1e-4 || wi.start >= hi - 1e-4)) continue;
            if (best < 0 || wi.start < windows[best].start) best = i;
        }
        if (best >= 0) { w = best; startBeat = windows[best].start; return true; }
        if (!GlobalClock.LoopSong && !region) return false;
        for (int i = 0; i < windows.Count; i++)
        {
            var wi = windows[i];
            if (wi.silent) continue;
            if (region && (wi.start < lo - 1e-4 || wi.start >= hi - 1e-4)) continue;
            if (best < 0 || wi.start < windows[best].start) best = i;
        }
        if (best < 0) return false;
        w = best; startBeat = windows[best].start + (region ? hi - lo : GlobalClock.TotalBeats); loops = 1;
        return true;
    }

    int LastEndedWindow(float song)
    {
        int best = -1; float bestEnd = float.NegativeInfinity;
        for (int i = 0; i < windows.Count; i++)
        {
            float end = windows[i].start + windows[i].length;
            if (end <= song + 1e-4f && end > bestEnd) { bestEnd = end; best = i; }
        }
        return best;
    }

    // ------------------------------------------------------------------ v4 per-note length (SPEC v4 §2.4 / §4 R1-R3)
    /// <summary>Ticks of the five UI lengths: 16th, 8th, quarter, half, whole (dotted = x1.5).</summary>
    public static readonly int[] BaseTicks = { 6, 12, 24, 48, 96 };
    /// <summary>The ten UI lengths in ticks, shortest first (16th, dotted 16th, 8th, dotted 8th, quarter, dotted quarter, half, dotted half,
    /// whole, dotted whole).</summary>
    public static readonly int[] UiTicks = { 6, 9, 12, 18, 24, 36, 48, 72, 96, 144 };
    const int TPB = ProjectConfig.TicksPerBeat;

    /// <summary>True when every node carries its own length (the durations engine); false = the legacy uniform-step engine.</summary>
    public bool HasDurations => durs.Count > 0 && durs.Count == nodes.Count;
    /// <summary>Length of node <paramref name="node"/>'s note in beats (legacy: the uniform step).</summary>
    public float DurBeats(int node) => HasDurations && node >= 0 && node < durs.Count ? durs[node] / (float)TPB : StepBeats;

    /// <summary>Sets node <paramref name="node"/>'s length (ticks, clamped 3..192). The first rhythm edit of a legacy cube bakes it into the
    /// durations engine first (<see cref="BakeDurations"/>). Drops and re-decides the queued notes; the gesture owner pushes History.</summary>
    public void SetDuration(int node, int ticks)
    {
        if (node < 0 || node >= nodes.Count) return;
        if (!HasDurations) BakeDurations();
        if (!HasDurations) return;
        ticks = ClampTicks(ticks);
        if (durs[node] == ticks) return;
        durs[node] = ticks;
        lastStep = -1;
        Changed();
    }

    /// <summary>Sets every node's length at once (entry i = node i; a short list repeats its last entry). A legacy cube switches to the
    /// durations engine (its necklace resets to every step); null or empty switches back to the legacy engine.</summary>
    public void SetAllDurations(IList<int> ticks)
    {
        if (ticks == null || ticks.Count == 0)
        {
            if (durs.Count == 0) return;
            durs.Clear(); pattern = null; lastStep = -1;
            Changed();
            return;
        }
        bool wasLegacy = !HasDurations;
        durs.Clear();
        for (int i = 0; i < nodes.Count; i++) durs.Add(ClampTicks(ticks[Mathf.Min(i, ticks.Count - 1)]));
        if (wasLegacy && HasDurations) { hits = -1; rot = 0; pattern = null; }
        lastStep = -1;
        Changed();
    }

    /// <summary>
    /// SPEC v4 R1: converts a legacy cube to the durations engine, keeping how it sounds: per node, the gap from its first hit to the next
    /// hit in window 0 (the window end after the last hit), snapped to the nearest of the ten UI lengths; nodes never hit there get the
    /// step length; the necklace resets to every step (hits -1, rot 0). No-op for a durations cube. The gesture owner pushes History.
    /// </summary>
    public void BakeDurations()
    {
        if (HasDurations || nodes.Count == 0) return;
        int n = nodes.Count;
        var baked = new int[n]; var have = new bool[n];
        float length = windows.Count > 0 ? windows[0].length : measureLength;
        int pass = PassOf(0, 0);
        for (int k = 0; k < 4096; k++)
        {
            float st = StepStartLocalBeat(k);
            if (st >= length - 1e-4f) break;
            var h = Decide(0, k, pass);
            if (!h.hit || h.node < 0 || h.node >= n || have[h.node]) continue;
            int nh = NextPatternHitStep(k, length);
            float e = nh >= 0 ? StepStartLocalBeat(nh) : length;
            baked[h.node] = SnapTicks((e - st) * TPB); have[h.node] = true;
        }
        for (int i = 0; i < n; i++) if (!have[i]) baked[i] = SnapTicks(StepBeats * TPB);
        durs.Clear(); durs.AddRange(baked);
        hits = -1; rot = 0; pattern = null; lastStep = -1;
        Changed();
    }

    /// <summary>Length of step k in beats (swing applied).</summary>
    public float StepLenBeats(int k) => StepStartLocalBeat(k + 1) - StepStartLocalBeat(k);

    /// <summary>Cube size for a note of <paramref name="beats"/>: ProjectConfig.SizeTable on log2 of the length (16th … whole), clamped.</summary>
    public static float SizeOf(float beats)
    {
        var t = ProjectConfig.SizeTable;
        float x = Mathf.Log(Mathf.Max(1e-3f, beats) / 0.25f, 2f);   // 0 = 16th … 4 = whole
        if (x <= 0f) return t[0];
        if (x >= t.Length - 1) return t[t.Length - 1];
        int i = Mathf.FloorToInt(x);
        return Mathf.Lerp(t[i], t[i + 1], x - i);
    }
    public static int TicksOf(int size, bool dotted) { int b = BaseTicks[Mathf.Clamp(size, 0, BaseTicks.Length - 1)]; return dotted ? b * 3 / 2 : b; }
    /// <summary>The UI length of <paramref name="ticks"/> (false when it is none of the ten).</summary>
    public static bool TryDecode(int ticks, out int size, out bool dotted)
    {
        for (int s = 0; s < BaseTicks.Length; s++)
        {
            if (ticks == BaseTicks[s]) { size = s; dotted = false; return true; }
            if (ticks == BaseTicks[s] * 3 / 2) { size = s; dotted = true; return true; }
        }
        size = 2; dotted = false; return false;
    }
    /// <summary>The nearest of the ten UI lengths to <paramref name="ticks"/> (log scale; a tie goes to the shorter).</summary>
    public static int SnapTicks(float ticks)
    {
        if (!(ticks > 0f)) return 24;
        float l = Mathf.Log(ticks, 2f); int best = 4; float bd = float.MaxValue;
        for (int i = 0; i < UiTicks.Length; i++) { float d = Mathf.Abs(Mathf.Log(UiTicks[i], 2f) - l); if (d < bd - 1e-5f) { bd = d; best = i; } }
        return UiTicks[best];
    }
    /// <summary>The engine's accepted range of a note length: 3 .. 192 ticks (an 8th of a beat .. two whole notes).</summary>
    public static int ClampTicks(int ticks) => Mathf.Clamp(ticks, 3, 192);
    public static bool IsDottedTicks(int ticks) { int s; bool d; return TryDecode(ticks, out s, out d) && d; }

    // ---- the durations engine (R1): one period of events precomputed in ticks, re-built when the lengths, mode, phase or direction change
    int[] dcDurs = new int[0]; int dcMode = -1, dcPhase = -1, dcN = -1; bool dcRev;
    bool dcOnce; int dcP; long dcCycle;                      // events per period (Once: in the pass); ticks of one period
    int[] dcNode = new int[0]; long[] dcStart = new long[1];  // node of event r (phase applied); raw start of event r in ticks, dcStart[dcP] = dcCycle

    void EnsureDurCache()
    {
        int n = durs.Count;
        bool same = dcDurs.Length == n && dcN == nodes.Count && dcMode == (int)mode && dcPhase == phase && dcRev == reverse;
        if (same) for (int i = 0; i < n; i++) if (dcDurs[i] != durs[i]) { same = false; break; }
        if (same) return;
        if (dcDurs.Length != n) dcDurs = new int[n];
        for (int i = 0; i < n; i++) dcDurs[i] = durs[i];
        dcN = nodes.Count; dcMode = (int)mode; dcPhase = phase; dcRev = reverse;
        int ph = Mathf.Max(0, phase);
        dcOnce = mode == PathMode.Once;
        dcP = n == 0 ? 0 : (mode == PathMode.PingPong ? Mathf.Max(1, 2 * n - 2) : (dcOnce ? Mathf.Max(0, n - ph) : n));
        if (dcNode.Length < dcP) dcNode = new int[dcP];
        if (dcStart.Length < dcP + 1) dcStart = new long[dcP + 1];
        long t = 0;
        for (int r = 0; r < dcP; r++)
        {
            int node = Mathf.Clamp(NodeForHit(r + ph), 0, n - 1);
            dcNode[r] = node; dcStart[r] = t;
            t += Mathf.Max(1, dcDurs[node]);
        }
        dcStart[dcP] = t;
        dcCycle = t;
    }

    /// <summary>Unwarped start of step k in beats (durations engine). After a Once pass (or with nothing to play) the steps are 1-beat fillers.</summary>
    float RawStart(int k)
    {
        EnsureDurCache();
        if (k <= 0) return 0f;
        if (dcP == 0) return k;
        if (dcOnce) return k <= dcP ? dcStart[k] / (float)TPB : dcCycle / (float)TPB + (k - dcP);
        long q = k / dcP; int r = k % dcP;
        return (q * dcCycle + dcStart[r]) / (float)TPB;
    }

    /// <summary>The step whose unwarped span holds <paramref name="raw"/> beats (durations engine; binary search in the period).</summary>
    int RawStep(float raw)
    {
        EnsureDurCache();
        if (raw <= 0f) return 0;
        if (dcP == 0) return Mathf.FloorToInt(raw);
        double t = raw * (double)TPB + 1e-3;   // a hair past a tick boundary that float math left just below it
        if (dcOnce)
        {
            if (t >= dcCycle) return dcP + (int)Math.Floor((t - dcCycle) / TPB);
            return EventAt(t);
        }
        long q = (long)Math.Floor(t / dcCycle);
        return (int)(q * dcP) + EventAt(t - q * (double)dcCycle);
    }

    int EventAt(double ticks)
    {
        int lo = 0, hi = dcP - 1;
        while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (dcStart[mid] <= ticks) lo = mid; else hi = mid - 1; }
        return lo;
    }

    /// <summary>SPEC v4 R1 swing as a per-beat time warp W(t): the first half of every beat stretches by (1 + s), the second shrinks by
    /// (1 - s); s = Swing / 3 (the legacy 8th swing amount). Identity for s = 0.</summary>
    public static float Warp(float t, float s)
    {
        if (s <= 0f) return t;
        float b = Mathf.Floor(t), f = t - b;
        return b + (f < 0.5f ? f * (1f + s) : 0.5f * (1f + s) + (f - 0.5f) * (1f - s));
    }
    /// <summary>The inverse of <see cref="Warp"/>.</summary>
    public static float WarpInv(float u, float s)
    {
        if (s <= 0f) return u;
        float b = Mathf.Floor(u), g = u - b, m = 0.5f * (1f + s);
        return b + (g < m ? g / (1f + s) : 0.5f + (g - m) / (1f - s));
    }

    // ---- size (R2): the body follows the note it is on through a springy transition
    public const float SizeOmega = 18f, SizeZeta = 0.35f, SizeZetaShrink = 0.75f, BeadPopSeconds = 0.3f;
    float sizeNow = 1f, sizeVel, sizeTarget = -1f; bool sizeInit;
    float sizeHold = -1f, sizeHoldUntil; float lastSizeFx = -9f;
    int noteNode = -1; float noteProgress; float breathe = 1f; bool dottedNow;
    float echoStep = 1f;
    int beadsShown; readonly List<float> beadPop = new List<float>();
    readonly List<Transform> dotMarks = new List<Transform>(); readonly List<Renderer> dotRends = new List<Renderer>();
    Transform sat; Material satMat; float satShow;

    /// <summary>The cube's current animated size (1 = the v3 cube): the spring toward SizeOf(its note) times the long-note breath (v6: times the
    /// keyboard footprint, <see cref="Foot"/>).</summary>
    public float SizeFactor => sizeNow * breathe * foot;
    /// <summary>The size the spring is heading for (tests).</summary>
    public float SizeTarget => sizeTarget;
    /// <summary>The node whose note sets the size right now (the note being played, else the home node).</summary>
    public int NoteNode => noteNode >= 0 && noteNode < nodes.Count ? noteNode : HomeNode;
    /// <summary>True while the dotted-note satellite is shown or popping.</summary>
    public bool SatelliteShown => satShow > 0f;

    /// <summary>Shows <paramref name="size"/> for <paramref name="seconds"/> whatever the note (the draft picks a length: it grows / shrinks at once).</summary>
    public void HoldSize(float size, float seconds) { sizeHold = Mathf.Max(0.1f, size); sizeHoldUntil = Time.unscaledTime + Mathf.Max(0f, seconds); }

    /// <summary>SPEC v4 R3: the draft hologram beams in where it stands: a springy pop from a sliver, a glitch spike and a blip.</summary>
    public void Materialize()
    {
        sizeInit = true; sizeNow = 0.12f; sizeVel = 0f;
        glitchUntil = Time.time + 0.2f;
        AudioPool.UI(ProceduralAudio.Blip(), 0.28f, 1.35f);
    }

    static float BeadPopCurve(float u)
    {
        u = Mathf.Clamp01(u);
        return u < 0.6f ? 1.2f * Ease.OutQuad(u / 0.6f) : 1.2f - 0.2f * Ease.InOutCubic((u - 0.6f) / 0.4f);
    }

    void UpdateSize(float dt)
    {
        int node = NoteNode;
        float len = node >= 0 ? DurBeats(node) : 1f;
        float target = SizeOf(len);
        if (sizeHold > 0f) { if (Time.unscaledTime < sizeHoldUntil) target = sizeHold; else sizeHold = -1f; }
        if (!sizeInit) { sizeInit = true; sizeNow = target; sizeVel = 0f; sizeTarget = target; }
        if (Mathf.Abs(target - sizeTarget) > 1e-4f)
        {
            float prev = sizeTarget;
            sizeTarget = target;
            if (prev > 0f && bodyVisible && Time.time - lastSizeFx > 0.06f) { lastSizeFx = Time.time; SizeFx(target > prev); }
        }
        // damped spring (omega 18, zeta 0.35: growing inflates with an overshoot); shrinking is damped harder (zeta 0.75: a quick pop down with a
        // squash instead of a dip far below the new size). Semi-implicit Euler in <= 8 ms substeps.
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float zeta = sizeNow > sizeTarget && sizeVel < 0f ? SizeZetaShrink : SizeZeta;
            float acc = -SizeOmega * SizeOmega * (sizeNow - sizeTarget) - 2f * zeta * SizeOmega * sizeVel;
            sizeVel += acc * h; sizeNow += sizeVel * h;
        }
        sizeNow = Mathf.Clamp(sizeNow, 0.1f, 2.4f);
        // long notes breathe: +4 % over the note (a half note or longer; a dotted quarter half as much)
        float longness = Mathf.Clamp01(len - 1f);
        breathe = 1f + 0.04f * longness * Mathf.Sin(Mathf.PI * Mathf.Clamp01(noteProgress));
        dottedNow = HasDurations && node >= 0 && node < durs.Count && IsDottedTicks(durs[node]);
    }

    /// <summary>Growing: a soft puff ring on the tile. Shrinking: a quick squash-pop and a few ink specks.</summary>
    void SizeFx(bool grow)
    {
        Vector3 floor = transform.position - Vector3.up * (ProjectConfig.CubeSize * sizeNow * foot * 0.5f);
        if (grow) Fx.Ripple(floor, Palette.A(Color, 0.75f), 0.55f + 0.5f * sizeTarget, 0.35f);
        else
        {
            Squash(0.55f);
            Fx.Burst(transform.position, hologram ? Palette.A(Color, 0.8f) : Look.InkColor, 4, 1.3f);
        }
    }

    /// <summary>Dotted notes (R2): a small ink-outlined dot orbits the cube on twos; it pops in and out.</summary>
    void UpdateSatellite(float dt)
    {
        bool want = dottedNow && nodes.Count > 0 && dissolveT < 0f;
        satShow = Mathf.MoveTowards(satShow, want ? 1f : 0f, dt / (want ? 0.18f : 0.12f));
        if (satShow <= 0f) { if (sat != null && sat.gameObject.activeSelf) sat.gameObject.SetActive(false); return; }
        if (sat == null) BuildSatellite();
        if (!sat.gameObject.activeSelf) { sat.gameObject.SetActive(true); ApplyVisibility(); }
        float pop = want ? Ease.OutBack(satShow) : satShow;
        float a = Look.Time12 * Mathf.PI * 1.6f + (owner % 7) * 0.9f;
        float half = ProjectConfig.CubeSize * sizeNow * foot * 0.5f;
        float r = half * 1.3f + 0.2f;
        // a tilted orbit around the upper half: behind the cube it rides above the top edge, so it is never hidden for long
        sat.position = transform.position + new Vector3(Mathf.Cos(a) * r, half * 0.55f + Mathf.Sin(a) * r * 0.45f, Mathf.Sin(a) * r);
        sat.localScale = Vector3.one * (0.17f * pop);
    }

    void BuildSatellite()
    {
        var go = new GameObject("DottedSatellite");
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(Vector3.one, 0.5f, 3);
        var mr = go.AddComponent<MeshRenderer>();
        satMat = Fx.Lit(new Color(1f, 0.99f, 0.95f), 0.3f, 0f, true);   // a white dot with the ink outline
        mr.sharedMaterial = satMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        sat = go.transform;
        go.SetActive(false);
    }

    // ---- v5 (R): the draft hologram peeks toward the tile the pointer is on ("that one next?"): it leans toward it and hops in place when the
    // target changes. An additive offset on the pose (position + lean), taken off at the start of the next Update, so it composes with the
    // playback hops and the settle springs; visual only.
    TileInteraction peekTile; float peekAmt, peekHopT = 9f; Vector3 peekDir = Vector3.forward, peekPosApplied; Quaternion peekRotApplied = Quaternion.identity;
    public const float PeekLean = 13f, PeekShift = 0.08f, PeekHop = 0.13f, PeekHopSeconds = 0.2f;
    /// <summary>True while the cube is in a hop between two notes (tests).</summary>
    public bool Hopping => hopping;
    /// <summary>The tile the cube peeks toward (null none).</summary>
    public TileInteraction PeekTile => peekTile;
    /// <summary>How far into its peek the cube is (0 rest … 1 leaning).</summary>
    public float PeekAmount => peekAmt;
    /// <summary>Leans the cube toward <paramref name="tile"/> with a small hop when the target changes; null eases back.</summary>
    public void Peek(TileInteraction tile)
    {
        if (tile == peekTile) return;
        peekTile = tile;
        if (tile != null) peekHopT = 0f;
    }

    void UnapplyPeek()
    {
        if (extraApplied != Vector3.zero) { transform.position -= extraApplied; extraApplied = Vector3.zero; }   // v7: the birth glide's offset
        if (peekPosApplied == Vector3.zero && peekRotApplied == Quaternion.identity) return;
        transform.position -= peekPosApplied;
        transform.rotation = Quaternion.Inverse(peekRotApplied) * transform.rotation;
        peekPosApplied = Vector3.zero; peekRotApplied = Quaternion.identity;
    }

    void ApplyPeek(float dt)
    {
        bool want = peekTile != null && dissolveT < 0f;
        peekAmt = Mathf.MoveTowards(peekAmt, want ? 1f : 0f, dt / (want ? 0.12f : 0.18f));
        if (peekHopT < PeekHopSeconds) peekHopT += dt;
        if (peekAmt <= 0f && peekHopT >= PeekHopSeconds) return;
        if (want)
        {
            Vector3 d = peekTile.Top - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) peekDir = d.normalized;
        }
        float e = Ease.OutCubic(peekAmt);
        float hop = peekHopT < PeekHopSeconds ? Mathf.Sin(peekHopT / PeekHopSeconds * Mathf.PI) * PeekHop * Mathf.Max(0.6f, sizeNow) : 0f;
        peekPosApplied = peekDir * (PeekShift * e) + Vector3.up * hop;
        peekRotApplied = Quaternion.AngleAxis(PeekLean * e, Vector3.Cross(Vector3.up, peekDir));
        transform.position += peekPosApplied;
        transform.rotation = peekRotApplied * transform.rotation;
    }

    // ---- the draft hologram (R3): semi-transparent in the instrument colour, scanlines, glitch bands, a chromatic split
    bool hologram;
    /// <summary>The draft look (semi-transparent glitch hologram) while a path is being drawn.</summary>
    public bool Hologram => hologram;
    public const float SolidifySeconds = 0.35f, DissolveSeconds = 0.3f;
    float solidT = 9f, dissolveT = -1f, glitchUntil = -9f;
    bool holoOn; Material holoMat; ShadowCastingMode bodyShadow = ShadowCastingMode.On;
    readonly Transform[] holoGhost = new Transform[2]; readonly Renderer[] holoGhostR = new Renderer[2]; readonly Material[] holoGhostMat = new Material[2];
    static Shader holoShader;
    static readonly int GlitchId = Shader.PropertyToID("_Glitch"), FlashId = Shader.PropertyToID("_Flash"), WipeId = Shader.PropertyToID("_Wipe"),
        AlphaId = Shader.PropertyToID("_Alpha"), SeedId = Shader.PropertyToID("_Seed"), SrcBlendId = Shader.PropertyToID("_SrcBlend"), DstBlendId = Shader.PropertyToID("_DstBlend");
    /// <summary>True while the solidify sequence runs.</summary>
    public bool Solidifying => solidT < SolidifySeconds;
    /// <summary>True while a cancelled draft wipes away.</summary>
    public bool Dissolving => dissolveT >= 0f;
    /// <summary>True while the body is drawn with the hologram material (drafting, solidifying or dissolving).</summary>
    public bool HologramShown => holoOn;

    public void SetHologram(bool on)
    {
        if (on == hologram) return;
        hologram = on;
        if (!on) solidT = 9f;   // off without the sequence: straight back to the solid look (UpdateHolo restores it)
    }

    /// <summary>Hologram → solid cube (the finish): a 0.35 s glitch, the chromatic split converges, a white flash, then the solid toon cube
    /// with an ink burst and a pop. The cube is solid for the song at once (Hologram false); the sequence is visual.</summary>
    public void Solidify()
    {
        if (!hologram) return;
        hologram = false;
        solidT = 0f;
    }

    /// <summary>SPEC v4 R3 cancel: the hologram wipes away (a scanline front sweeps down) and the cube destroys itself. Silent at once; it
    /// leaves the cube registry and its tile immediately. A solid cube just pops.</summary>
    public void Dissolve()
    {
        if (dissolveT >= 0f) return;
        CancelPending(); StopFallback();
        SequenceMaster.Unregister(this);
        SetResting(null);
        isFinalized = false;
        if (line != null) line.enabled = false;
        foreach (var m in markers) if (m != null) m.gameObject.SetActive(false);
        foreach (var g in glyphs) if (g != null) g.gameObject.SetActive(false);
        foreach (var t in ticks) if (t != null) t.gameObject.SetActive(false);
        foreach (var t in tieBars) if (t != null) t.gameObject.SetActive(false);
        foreach (var d in dotMarks) if (d != null) d.gameObject.SetActive(false);
        if (sat != null) sat.gameObject.SetActive(false);
        if (!hologram && !holoOn) { Fx.Pop(transform.position, Color); AudioPool.UI(ProceduralAudio.Pop(), 0.5f); Destroy(gameObject); dissolveT = 0f; return; }
        dissolveT = 0f;
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.2f, 1.7f);
    }

    void UpdateHolo(float dt)
    {
        bool want = hologram || solidT < SolidifySeconds || dissolveT >= 0f;
        if (!want) { if (holoOn) HoloOff(); return; }
        if (!holoOn) HoloOn();
        float glitch = 0.22f, split = 0.045f, flash = 0f, wipe = 0f, alpha = 0.78f;
        // idle glitch: short bursts on twos (seeded per cube), and a spike when the hologram beams in
        int tq = Mathf.FloorToInt(Time.time * 12f);
        if (Time.time < glitchUntil || Rhythm.Hash01(tq, owner, 91) > 0.86f) { glitch = 0.85f; split = 0.12f; }
        if (solidT < SolidifySeconds)
        {
            solidT += dt;
            float u = Mathf.Clamp01(solidT / SolidifySeconds);
            if (u < 0.72f) { glitch = 1f; split = 0.15f * (1f - Ease.InQuad(u / 0.72f)); }            // glitch, the split converges
            else { glitch = 0.1f; split = 0f; flash = Ease.OutQuad((u - 0.72f) / 0.28f); alpha = 1f; }  // white flash
            if (solidT >= SolidifySeconds) { HoloOff(); SolidifyFx(); return; }
        }
        if (dissolveT >= 0f)
        {
            dissolveT += dt;
            float u = Mathf.Clamp01(dissolveT / DissolveSeconds);
            wipe = u; glitch = 0.7f; split = 0.05f + 0.25f * u;
            if (u >= 1f) { Destroy(gameObject); return; }
        }
        Color c = Color;
        holoMat.SetColor(ColorId, c);
        holoMat.SetFloat(GlitchId, glitch); holoMat.SetFloat(FlashId, flash); holoMat.SetFloat(WipeId, wipe); holoMat.SetFloat(AlphaId, alpha);
        var cam = Camera.main;
        Vector3 right = cam != null ? cam.transform.right : Vector3.right;
        float jitter = glitch > 0.5f ? (Rhythm.Hash01(tq, owner, 17) - 0.5f) * 0.05f : 0f;
        for (int i = 0; i < 2; i++)
        {
            if (holoGhost[i] == null) continue;
            float sgn = i == 0 ? -1f : 1f;
            holoGhost[i].localPosition = transform.InverseTransformVector(right * (sgn * split) + Vector3.up * jitter * sgn);
            var gm = holoGhostMat[i];
            gm.SetFloat(GlitchId, glitch); gm.SetFloat(WipeId, wipe); gm.SetFloat(FlashId, 0f); gm.SetFloat(AlphaId, 0.3f * (1f - flash));
        }
    }

    void HoloOn()
    {
        if (glassOn) GlassOff();   // v7: the draft look takes over from the layer glass
        holoOn = true;
        if (holoShader == null) holoShader = Shader.Find("DeaCube/Hologram");
        if (holoMat == null)
        {
            holoMat = holoShader != null ? new Material(holoShader) : Fx.Additive(IconFactory.GetTexture("white"), Color, 0.8f);
            holoMat.SetFloat(SeedId, owner % 97);
        }
        if (rend != null) { bodyShadow = rend.shadowCastingMode; rend.sharedMaterial = holoMat; rend.shadowCastingMode = ShadowCastingMode.Off; }
        var mf = GetComponent<MeshFilter>();
        for (int i = 0; i < 2; i++)
        {
            if (holoGhost[i] == null)
            {
                var go = new GameObject(i == 0 ? "HoloSplitRed" : "HoloSplitCyan");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf != null ? mf.sharedMesh : MeshFactory.RoundedBox(Vector3.one, 0.14f, 4);
                var r = go.AddComponent<MeshRenderer>();
                var m = holoShader != null ? new Material(holoShader) : Fx.Additive(IconFactory.GetTexture("white"));
                m.SetFloat(SrcBlendId, 1f); m.SetFloat(DstBlendId, 1f);   // additive copies: where they overlap the body the colours sum back
                m.SetColor(ColorId, i == 0 ? new Color(1f, 0.16f, 0.42f) : new Color(0.12f, 0.86f, 1f));
                m.SetFloat(SeedId, owner % 97 + 31 * (i + 1));
                m.renderQueue = 3010;
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                holoGhost[i] = go.transform; holoGhostR[i] = r; holoGhostMat[i] = m;
            }
            holoGhost[i].gameObject.SetActive(true);
        }
        ApplyVisibility();
    }

    void HoloOff()
    {
        holoOn = false;
        if (rend != null && mat != null) { rend.sharedMaterial = mat; rend.shadowCastingMode = bodyShadow; }
        for (int i = 0; i < 2; i++) if (holoGhost[i] != null) holoGhost[i].gameObject.SetActive(false);
    }

    void SolidifyFx()
    {
        glow = 1.6f;
        Squash(1.25f);
        Vector3 p = transform.position;
        Fx.Starburst(p, Color, 0.75f * sizeNow);          // the ink burst
        Fx.Burst(p, Look.InkColor, 7, 2.6f);
        Fx.Burst(p, Color, 6, 2.2f);
        AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 1.15f);
    }

    /// <summary>One note of the cube's timeline inside a window: step k plays node for len beats from start (window-local); rest = silent.</summary>
    public struct TimelineEvent { public int k, node; public float start, len; public bool rest; public int mod; }
    /// <summary>The notes the cube plays in window <paramref name="window"/> (loop 0), from the same step math the audio uses (both engines).
    /// Durations engine: len = the note's own (swung) length, clipped at the window end; legacy: the gap to the next hit.</summary>
    public int TimelineEvents(int window, List<TimelineEvent> into)
    {
        into.Clear();
        if (nodes.Count == 0) return 0;
        float length = window >= 0 && window < windows.Count ? windows[window].length : measureLength;
        int pass = PassOf(window, 0);
        bool dur = HasDurations;
        for (int k = 0; k < 4096; k++)
        {
            float s = StepStartLocalBeat(k);
            if (s >= length - 1e-4f) break;
            var h = Decide(window, k, pass);
            if (!h.hit) continue;
            float e;
            if (dur) e = Mathf.Min(length, StepStartLocalBeat(k + 1));
            else { int nh = NextPatternHitStep(k, length); e = nh >= 0 ? StepStartLocalBeat(nh) : length; }
            into.Add(new TimelineEvent { k = k, node = h.node, start = s, len = Mathf.Max(0.01f, e - s), rest = h.mod == 1, mod = h.mod });
        }
        return into.Count;
    }

    // ------------------------------------------------------------------ v3 contract (SPEC v3 §2.7): pose read-out, landing event, visibility
    /// <summary>What the cube is doing at the current song beat: a pure read of the math UpdatePlayback uses.</summary>
    public struct CubePose
    {
        public bool active;              // playing and inside an audible window of this cube
        public int window;               // index into windows (-1 when not active)
        public int step;                 // step k inside the window
        public float stepFrac;           // progress through step k (0..1)
        public float stepBeat;           // song beat where step k starts
        public TileInteraction tile;     // tile of step k's node (the tile the cube stands on or is leaving)
        public Hit hit;                  // Decide for step k
        public int nextStep;             // the next necklace hit step inside the window (-1 if none)
        public float nextBeat;           // song beat where nextStep starts (-1 if none)
        public TileInteraction nextTile; // tile of the next hit's node (null if none)
        public Hit nextHit;              // Decide for nextStep
    }

    /// <summary>Fills <paramref name="p"/> for the current song beat. False when the clock is stopped, the cube has no nodes, or no
    /// audible window of this cube contains the beat.</summary>
    public bool TryGetPose(out CubePose p)
    {
        p = new CubePose { window = -1, step = -1, nextStep = -1, nextBeat = -1f };
        if (!isFinalized || nodes.Count == 0 || windows.Count == 0 || !GlobalClock.IsPlaying) return false;
        float song = GlobalClock.SongBeat;
        int a = -1;
        for (int i = 0; i < windows.Count; i++) if (Contains(windows[i], song)) { a = i; break; }
        if (a < 0 || windows[a].silent) return false;
        Window win = windows[a];
        int k; float prog;
        ComputeStep(song - win.start, out k, out prog);
        int pass = PassOf(a, 0);
        p.active = true; p.window = a; p.step = k; p.stepFrac = prog; p.stepBeat = win.start + StepStartLocalBeat(k);
        p.hit = Decide(a, k, pass);
        p.tile = p.hit.node >= 0 ? TileAt(a, p.hit.node) : null;
        int nh = NextPatternHitStep(k, win.length);
        p.nextStep = nh;
        if (nh >= 0)
        {
            p.nextBeat = win.start + StepStartLocalBeat(nh);
            p.nextHit = Decide(a, nh, pass);
            p.nextTile = p.nextHit.node >= 0 ? TileAt(a, p.nextHit.node) : null;
        }
        return true;
    }

    /// <summary>The pass value Decide expects for window <paramref name="w"/>, <paramref name="loopsAhead"/> song loops from now.</summary>
    public int PassFor(int w, int loopsAhead) => PassOf(w, loopsAhead);

    /// <summary>Step index and progress at a window-local beat (swing aware), exactly as UpdatePlayback computes them.</summary>
    public void StepAt(float localBeat, out int k, out float progress) { ComputeStep(localBeat, out k, out progress); }

    /// <summary>Raised by the landing visuals for every necklace hit step: (cube, window, step, hit).</summary>
    public static event Action<AudioCube, int, int, Hit> OnLanded;

    bool bodyVisible = true, pathVisible = true, visApplied = true;
    public bool BodyVisible => bodyVisible;
    public bool PathVisible => pathVisible;

    /// <summary>Hides / shows the cube body (mesh, halo, shadow ghosts, echo afterimages, octave disc) and its path (line, beads, sticker
    /// glyphs, ticks, tie bars). Audio and motion are unaffected.</summary>
    public void SetWorldVisible(bool body, bool path)
    {
        bodyVisible = body; pathVisible = path;
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        bool b = bodyVisible, pth = pathVisible;
        if (rend != null) rend.enabled = b;
        if (haloRend != null) haloRend.enabled = b;
        for (int i = 0; i < ghosts.Length; i++) SetRendererOn(ghosts[i], b);
        SetRendererOn(disc, b);
        for (int i = 0; i < echoRends.Length; i++) if (echoRends[i] != null) echoRends[i].enabled = b;
        if (line != null) line.enabled = pth;
        for (int i = 0; i < markerRends.Count; i++) if (markerRends[i] != null) markerRends[i].enabled = pth;
        for (int i = 0; i < glyphRends.Count; i++) if (glyphRends[i] != null) glyphRends[i].enabled = pth;
        for (int i = 0; i < ticks.Count; i++) SetRendererOn(ticks[i], pth);
        for (int i = 0; i < tieBars.Count; i++) SetRendererOn(tieBars[i], pth);
        for (int i = 0; i < dotRends.Count; i++) if (dotRends[i] != null) dotRends[i].enabled = pth;
        SetRendererOn(sat, b);
        for (int i = 0; i < holoGhostR.Length; i++) if (holoGhostR[i] != null) holoGhostR[i].enabled = b;
        visApplied = b && pth;
    }

    static void SetRendererOn(Transform t, bool on)
    {
        if (t == null) return;
        var r = t.GetComponent<Renderer>();
        if (r != null) r.enabled = on;
    }

    // overlays are created lazily (beads on RefreshLine, ghosts / echoes on demand): keep a hidden cube hidden
    void LateUpdate() { if (!bodyVisible || !pathVisible || !visApplied) ApplyVisibility(); }

    // ------------------------------------------------------------------ update
    void Update()
    {
        float dt = Time.deltaTime;
        UnapplyPeek();                                       // v5: the peek is an additive offset on top of the pose (taken off before the pose runs)
        if (dissolveT >= 0f) { UpdateHolo(dt); return; }   // a cancelled draft wipes away, then destroys itself
        if (dropT < 1f) UpdateDrop(dt);
        else if (previewT < 1f) UpdatePreview(dt);
        else if (nodes.Count > 0 && (isFinalized || (hologram && GlobalClock.IsPlaying))) UpdatePlayback(dt);   // v4 R3: the draft plays live
        else if (nodes.Count > 0) { noteNode = nodes.Count - 1; noteProgress = 0f; SettleOn(restingTile != null ? restingTile : nodes[nodes.Count - 1], dt); }
        UpdateVisuals(dt);
        UpdateHolo(dt);
        UpdateGlass(dt);                             // v7: a layer cube's glass / phase-in
        ApplyAppear(dt);                             // v7: a new copy gliding out of its source
        ApplyStopTrip();                             // v7: the trip home after a stop / reset (real time)
        SpherePose(dt);                              // a sphere rolls by the distance it moved, stays upright (SphereBody.cs)
        ApplyPeek(dt);
        // v4 (the user): a hand-drawn paint smear behind a fast hop / drop (CubeSmear decides by the screen speed; not on a hologram)
        if (bodyVisible && !hologram && !holoOn && nodes.Count > 0)
        {
            // v5: the belt's jerk carries the whole island (every cube on it): that is not the cube's own motion, no paint smear across it
            var ri = restingTile != null ? restingTile.island : (hopTarget != null ? hopTarget.island : Island);
            if (ri != null && ri.Riding) CubeSmear.Cut(owner);
            else CubeSmear.Track(owner, transform, bodyMesh, Color);
        }
        UpdateFallback();
    }

    void UpdateDrop(float dt)
    {
        dropT += dt / 0.26f;
        float e = Ease.InQuad(Mathf.Min(dropT, 1f));
        Vector3 target = RestPosition(dropTarget, StackSlot(dropTarget));
        dropFrom = IslandWorld(dropTarget, dropFromLocal);
        transform.position = Vector3.Lerp(dropFrom, target, e);
        if (dropT >= 1f) { transform.position = target; LandFx(dropTarget); }
    }

    void UpdatePreview(float dt)
    {
        previewT += dt / 0.17f;
        float t = Mathf.Min(previewT, 1f);
        Vector3 target = RestPosition(previewTarget, SequenceMaster.CountOn(previewTarget, this));
        previewFrom = IslandWorld(previewTarget, previewFromLocal);
        transform.position = Vector3.Lerp(previewFrom, target, Ease.OutCubic(t)) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.35f;
        if (previewT >= 1f) { SetResting(previewTarget); transform.position = RestPosition(previewTarget, StackSlot(previewTarget)); squashT = 0f; squashKind = 0.8f; }
    }

    /// <summary>
    /// Finds the active window (cached index), runs the step logic inside it (Decide for the step, hit-advance hop toward the
    /// next hit, crouch on wait steps), schedules every step inside the lookahead and the first step of the next window
    /// (windows[0] + TotalBeats on loop). Silent windows keep the cube asleep. v7 (SPEC v7 §12.1 / §21): outside its windows the cube HOLDS where its
    /// last window left it and goes home along its grid only right before it plays again or right after the song resets (<see cref="OutsidePose"/>);
    /// a window's end may zip back (rewind) or step on into the next pass / the next measure of its long grid (<see cref="TransitionOut"/>); it never
    /// stands on another grid. Paused, the pose of the paused beat stays (no notes are scheduled); stopped, the cube is home — after a stop it
    /// travels there in real time (<see cref="ResetToStart"/>).
    /// </summary>
    void UpdatePlayback(float dt)
    {
        EnsureWindow();
        bool playing = GlobalClock.IsPlaying;
        tripOn = false; holdOn = false; zipOn = false; crossOn = false;
        CheckRunner();
        if (playing) { forcedHome = false; transportHome = false; }
        if (!playing && (forcedHome || transportHome || GlobalClock.SongBeatD <= 1e-6)) { StoppedPose(dt); return; }
        float song = GlobalClock.SongBeat;
        double now = GlobalClock.DspNow;
        int a = FindWindow(song);
        bool active = a >= 0 && !windows[a].silent;

        if (!active)
        {
            wasActive = false; lastStep = -1; lastWindow = -1; hopping = false; ratchetLeft = 0; echoLeft = 0; leanDir = Vector3.zero;
            int nW; double nStart; int nLoops;
            float from = a >= 0 ? windows[a].start + windows[a].length : song;
            if (NextAudibleWindowFrom(from, out nW, out nStart, out nLoops)) { if (playing) TrySchedule(nW, 0, GlobalClock.DspTimeOfBeat(nStart), 0f, nLoops); }
            else nW = -1;
            asleep = playing;
            noteNode = HomeNode; noteProgress = 0f;
            layerPhase = nW >= 0 ? LeadInPhase(nW, nStart, song) : 0f;
            OutsidePose(song, nW, nStart, dt);   // v7: hold where the last window left it; home only before it plays again / after the reset
            return;
        }
        asleep = false; wasActive = true;

        Window win = windows[a];
        float local = song - win.start;
        int pass = PassOf(a, 0);
        int k; float p;
        ComputeStep(local, out k, out p);
        Hit cur = Decide(a, k, pass);

        if (!OnMyGrid(win.island))
        {
            // v7 §21: a window on another grid (an old rider window) never moves the body — it stays home on its own grid; its notes are the
            // timeline's business (K plays riders on their own grid now)
            if (playing) ScheduleFrom(a, k, now);
            noteNode = HomeNode; noteProgress = 0f; layerPhase = 0f;
            Hold(HomeTile, false, dt);
            return;
        }
        if (playing)
        {
            if (k != lastStep || a != lastWindow) { lastStep = k; lastWindow = a; Land(a, k, cur, pass); }
            ScheduleFrom(a, k, now);
            UpdateSubHits(local, win.length);
        }

        noteNode = cur.node >= 0 ? cur.node : EndNode; noteProgress = p;
        layerPhase = PhaseInWindow(a, k, local, cur, pass);
        if (TransitionOut(a, song, local, k, cur)) return;   // v7: the rewind zip, a step across a pass boundary / into the next measure of its long grid
        if (cur.node < 0) { hopping = false; SettleOn(TileAt(a, EndNode), dt); return; }
        TileInteraction ta = TileAt(a, cur.node);
        if (ta == null) return;
        int nh = NextPatternHitStep(k, win.length);
        bool stepEndsWindow = StepStartLocalBeat(k + 1) >= win.length - 1e-4f;
        Hit nb = nh >= 0 ? Decide(a, nh, pass) : new Hit { node = -1 };
        // v4 R1: a durations note sits, then hops in at most HopMaxBeats (the hop starts at max(snap * len, len - HopMaxBeats))
        float hs = ProjectConfig.snapThreshold, arcScale = StepBeats <= 0.25f ? 0.6f : 1f;
        if (HasDurations)
        {
            float len = StepLenBeats(k);
            hs = Mathf.Max(ProjectConfig.snapThreshold, 1f - ProjectConfig.HopMaxBeats / Mathf.Max(1e-4f, len));
            arcScale = len <= 0.25f ? 0.6f : 1f;
        }
        if (sphere && nb.node >= 0) hs = SphereHopStart(hs, ta, TileAt(a, nb.node), HasDurations ? StepLenBeats(k) : 0f);   // a far leap takes longer in the air
        bool moving = nh == k + 1 && nb.node >= 0 && nb.node != cur.node && !stepEndsWindow && p >= hs;
        if (!moving) { hopping = false; SettleOn(ta, dt); }
        else
        {
            TileInteraction tb = TileAt(a, nb.node);
            if (tb == null) { SettleOn(ta, dt); return; }
            float f = (p - hs) / Mathf.Max(1e-4f, 1f - hs);
            if (win.island != null && win.island.IsStairs) StairHop(ta, tb, f);   // v7: a runner drops (climbs) step to step
            else Hop(ta, tb, f, HopArcMul(nb), arcScale);
        }
    }

    /// <summary>Schedules the current step (a play/seek can land inside it) and every later step inside the lookahead; past the
    /// window end it continues with the first step of the next audible window (SPEC §2.10).</summary>
    void ScheduleFrom(int a, int k, double now)
    {
        Window win = windows[a];
        float kLocal = StepStartLocalBeat(k);
        double dspK = GlobalClock.DspTimeOfBeat(win.start + kLocal);
        if (dspK >= now - 0.03) TrySchedule(a, k, dspK, kLocal, 0);
        for (int j = k + 1; j < k + 64; j++)
        {
            float sl = StepStartLocalBeat(j);
            if (sl >= win.length - 1e-4f)
            {
                int w; double startBeat; int loops;
                if (NextAudibleWindowFrom(win.start + win.length, out w, out startBeat, out loops)) TrySchedule(w, 0, GlobalClock.DspTimeOfBeat(startBeat), 0f, loops);
                break;
            }
            double dsp = GlobalClock.DspTimeOfBeat(win.start + sl);
            if (dsp > now + lookNow) break;
            TrySchedule(a, j, dsp, sl, 0);
        }
    }

    /// <summary>Ratchet mini-bounces and echo afterimages on the step grid (visual twins of the events VoiceRules scheduled).</summary>
    void UpdateSubHits(float local, float length)
    {
        if (ratchetLeft > 0 && local >= ratchetNextLocal)
        {
            ratchetLeft--; ratchetNextLocal += ratchetSub;
            squashT = 0f; squashKind = 0.6f;
            if (ratchetTile != null) { if (layer == 0) { ratchetTile.Press(0.5f); ratchetTile.Flash(Color, 0.55f); } Fx.Ripple(TopOf(ratchetTile), Color, 0.5f, 0.25f); }
        }
        if (echoLeft > 0 && local >= echoNextLocal)
        {
            if (echoNextLocal < length - 1e-3f && echoTile != null) SpawnAfterimage(echoTile, echoIdx);
            echoIdx++; echoLeft--; echoNextLocal += echoStep;
        }
    }

    void SettleOn(TileInteraction tile, float dt, bool occupy = true)
    {
        if (tile == null) return;
        SetResting(tile, occupy);
        int slot = StackSlot(tile);
        Vector3 target = RestPosition(tile, slot);
        Vector3 pos = transform.position;
        float lift = ProjectConfig.CubeSize * sizeNow * FootOf(tile) * 0.5f;   // v4 R2: smooth the floor, not the centre (a growing cube grows up from the tile)
        float baseY = tile.island != null ? tile.island.transform.position.y : 0f;   // v5: relative to the island (its belt lift / register spring is followed exactly)
        float y = Mathf.SmoothDamp(pos.y - baseY - lift, target.y - baseY - lift, ref yVel, 0.07f) + lift + baseY;
        if (Mathf.Abs(pos.y - target.y) > 2f) { y = target.y; CubeSmear.Cut(owner); }
        transform.position = new Vector3(target.x, y, target.z);
        float yaw = slot == 0 ? 0f : Mathf.Sin(slot * 1.7f) * 7f;
        Quaternion want = Quaternion.Euler(0f, yaw, 0f);
        int bn = BendOn(tile, noteNode);
        if (bn != 0) want = Quaternion.AngleAxis(-Mathf.Clamp(bn * 2.5f, -14f, 14f), Vector3.forward) * want;   // v7: a bent note tilts the body toward its bend (up = right)
        if (leanDir.sqrMagnitude > 0.5f)
        {
            leanT = Mathf.Min(1f, leanT + dt / 0.18f);
            want = Quaternion.AngleAxis(9f * Ease.OutCubic(leanT), Vector3.Cross(Vector3.up, leanDir)) * want;   // the crouch leans toward the next node
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-dt * 20f));
    }

    /// <summary>The hop's height factor for the landing <paramref name="nb"/>: a lift doubles it; v9 (N) a nudge adds / takes 30 % a step
    /// (higher = a higher hop, lower = a flatter one).</summary>
    public static float HopArcMul(Hit nb) => (nb.lift ? 2f : 1f) * Mathf.Max(0.3f, 1f + 0.3f * nb.nudge);

    void Hop(TileInteraction ta, TileInteraction tb, float f, float arcMul, float arcScale)
    {
        if (!hopping)
        {
            hopping = true;
            hopFrom = RestPosition(ta, StackSlot(ta));
            hopSpace = ta != null && ta.island != null ? ta.island.transform : null;   // v5: island-local, so a belt jerk mid-hop carries the hop along
            hopFromLocal = hopSpace != null ? hopSpace.InverseTransformPoint(hopFrom) : hopFrom;
            SetResting(null);
        }
        else if (hopSpace != null) hopFrom = hopSpace.TransformPoint(hopFromLocal);
        hopTarget = tb;
        int slotB = SequenceMaster.CountOn(tb, this);
        Vector3 end = RestPosition(tb, slotB);
        float fe = sphere ? f : f * f * (3f - 2f * f);   // a sphere flies at a constant ground speed (it rolls at a constant rate)
        Vector3 basePos = Vector3.Lerp(hopFrom, end, fe);
        float arc = sphere ? SphereArc(ta, tb, f) * arcScale * arcMul : Mathf.Sin(f * Mathf.PI) * ProjectConfig.cubeHopIntensity * arcScale * arcMul;
        transform.position = basePos + Vector3.up * arc;
        Vector3 dir = end - hopFrom; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, dir.normalized);
            transform.rotation = Quaternion.AngleAxis(90f * fe, axis);
        }
    }

    /// <summary>Visuals of one step, from the same Decide the audio used: hit steps land (squash, press, flash, pillar, bead pulse,
    /// accent starburst, ratchet/echo timers), wait steps crouch and lean, rests press softly, slips puff.</summary>
    void Land(int w, int k, Hit hit, int pass)
    {
        if (hit.node < 0) return;
        TileInteraction tile = TileAt(w, hit.node);
        if (tile == null) return;
        bool grid = layer == 0;                  // v7: a layer cube lands in its layer: the grid's tile below stays still
        Vector3 at = TopOf(tile);
        leanDir = Vector3.zero; leanT = 0f;
        float length = windows[w].length;
        if (!hit.hit)
        {
            squashT = 0f; squashKind = 0.35f;
            int nh = NextPatternHitStep(k, length);
            if (nh >= 0)
            {
                var nt = TileAt(w, Decide(w, nh, pass).node);
                if (nt != null && nt != tile) { Vector3 d = nt.Top - tile.Top; d.y = 0f; if (d.sqrMagnitude > 1e-4f) leanDir = d.normalized; }
            }
            return;
        }
        bool rest = hit.mod == 1;
        OnLanded?.Invoke(this, w, k, hit);
        squashT = 0f; squashKind = rest ? 0.5f : (hit.weight == 0 ? 0.7f : (hit.weight == 2 ? 1.25f : 1f));
        if (hit.node < markerPulse.Count) markerPulse[hit.node] = 1f;
        if (grid) tile.Press(hit.fires ? 1f : 0.35f);
        if (hit.fires)
        {
            float ws = hit.weight == 0 ? 0.45f : (hit.weight == 2 ? 1.35f : 1f);
            KeyBlock isl = windows[w].island != null ? windows[w].island : Island;
            float es = Rhythm.EnergyScale(isl != null ? isl.energy : 2);
            float hk = hologram ? 0.45f : 1f;   // v4 R3: a draft traces its path with lighter landings (the hologram stays readable on its tile)
            if (grid) tile.Flash(Color, (hit.weight == 0 ? 0.5f : 1f) * hk);
            Fx.Ripple(at, Color, (tile.island != null && tile.island.IsKeyboard ? 0.45f : 0.9f) * ws, 0.42f);   // v6: a narrow key gets a smaller ring
            if (!hologram) LandBeam(at, hit.weight, ws, es);
            if (hit.weight == 2) Fx.Starburst(at + Vector3.up * 0.4f, Color);
            glow = hit.tie ? 1.3f : 1f;
            float startLocal = StepStartLocalBeat(k);
            float stepLen = HasDurations ? StepLenBeats(k) : StepBeats;   // v4 R1: a ratchet splits the note, echoes repeat at its length
            if (hit.ratchet > 1) { ratchetLeft = hit.ratchet - 1; ratchetSub = stepLen / hit.ratchet; ratchetNextLocal = startLocal + ratchetSub; ratchetTile = tile; }
            if (hit.echo > 0) { echoLeft = hit.echo; echoIdx = 0; echoStep = stepLen; echoNextLocal = startLocal + stepLen; echoTile = tile; }
        }
        else if (hit.slip) Fx.Burst(at + Vector3.up * 0.25f, Palette.A(Color, 0.6f), 5, 0.9f);   // a failed coin: hop, no flash, a soft puff
        else if (rest) Fx.Ripple(at, Palette.A(Color, 0.5f), 0.5f, 0.3f);
    }

    /// <summary>Tests / A-B captures: true restores the v3 landing beam (1.3-2.8 u, 0.5 s, on every hit).</summary>
    public static bool BeamsV3;

    /// <summary>
    /// The landing beam (v4, orchestrator review): a note-beam on every hit made the island a forest of white sticks that hid the cube sizes
    /// and the hologram. Now only an accent gets a full beam (short-lived); a normal hit gets a short, thin stub that fades fast; a ghost gets
    /// none (the squash, the tile flash and the ripple carry it). Visual only: audio and timing are untouched.
    /// </summary>
    void LandBeam(Vector3 top, int weight, float ws, float es)
    {
        if (BeamsV3) { Fx.Pillar(top, Color, 1.3f + 1.1f * ws * volume * es, 0.5f, 0.5f); return; }
        if (weight == 2) Fx.Pillar(top, Color, 1.2f + 0.6f * volume * es, 0.28f, 0.45f);
        else if (weight == 1) Fx.Pillar(top, Color, 0.45f + 0.3f * volume * es, 0.2f, 0.26f);
    }

    void LandFx(TileInteraction tile)
    {
        squashT = 0f; squashKind = 1f;
        tile.Press(1f);
        Fx.Ripple(tile.Top, Color, 1f, 0.45f);
        Fx.Burst(tile.Top, Color, 8, 1.8f);
        AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.1f);
    }

    // ------------------------------------------------------------------ audio
    readonly VoiceRules.NoteEvent[] events = new VoiceRules.NoteEvent[32];

    /// <summary>
    /// Schedules step <paramref name="k"/> of window <paramref name="w"/> at DSP time <paramref name="dsp"/> (the step's start;
    /// <paramref name="startLocal"/> is that start in beats relative to the window; <paramref name="loops"/> = 1 for the next
    /// pass of the song). Guards: inside the lookahead and strictly later than the last scheduled trigger (monotonic: one
    /// decision per step). Then Decide → tile → stack → VoiceRules.Resolve → VoiceRules.Dispatch, the only Synth.Note path for cubes (SPEC §4.2).
    /// </summary>
    void TrySchedule(int w, int k, double dsp, float startLocal, int loops)
    {
        double now = GlobalClock.DspNow;
        if (dsp > now + lookNow) return;
        if (dsp <= lastScheduledDsp + 1e-4) return;
        lastScheduledDsp = dsp;
        if (w < 0 || w >= windows.Count) return;
        Window win = windows[w];
        int pass = PassOf(w, loops);
        Hit hit = Decide(w, k, pass);
        if (!hit.fires) return;
        TileInteraction tile = TileAt(w, hit.node);
        if (tile == null) return;
        int stack = layer != 0 ? 0 : SequenceMaster.CountOn(tile, this);   // v7: a layer cube never stacks (rule 3's lift must not add to its octave)
        double bps = GlobalClock.BeatsPerSecond;
        double stepSec = (HasDurations ? StepLenBeats(k) : StepBeats) / bps;   // v4 R1: the note's own (swung) length; legacy: the step
        double windowEndDsp = dsp + (win.length - startLocal) / bps;      // pass-independent: derived from this step's own time
        double nextHitDsp = NextHitDsp(w, k, pass, dsp, startLocal, win.length);
        int n = VoiceRules.Resolve(this, win, tile, hit, stack, pass, dsp, stepSec, windowEndDsp, nextHitDsp, events);
        if (n <= 0) return;
        if (Synth.Ready) { VoiceRules.Dispatch(events, n, owner); if (!Performance.StutterActive) TrackLive(events, n, now); return; }

        // No SoundFont: the legacy pitched sample on a single AudioSource, hard-gated from Update (first event only).
        if (clip == null) return;
        var v = Fallback();
        v.Stop();
        v.clip = clip; v.outputAudioMixerGroup = group;
        v.pitch = tile.GetPitch();
        fallbackVol = volume * Instruments.Gain(instrument) * ProjectConfig.MaxSystemVolume;
        v.volume = fallbackVol;
        v.PlayScheduled(Math.Max(events[0].onDsp, now));
        fallbackStartAt = events[0].onDsp;
        fallbackStopAt = events[0].offDsp;
    }

    /// <summary>DSP time of the cube's next hit step after k inside the same window (+inf if none): for ties and Long gates. A rest or a
    /// slipped coin is a hit too (the cube lands there): a rest is silent (SPEC 3.1 #7), so a Long or tied note stops at it instead of ringing through.</summary>
    double NextHitDsp(int w, int k, int pass, double dsp, float startLocal, float length)
    {
        double bps = GlobalClock.BeatsPerSecond;
        for (int j = k + 1; j < k + 256; j++)
        {
            float sl = StepStartLocalBeat(j);
            if (sl >= length - 1e-4f) return double.PositiveInfinity;
            if (Decide(w, j, pass).hit) return dsp + (sl - startLocal) / bps;
        }
        return double.PositiveInfinity;
    }

    AudioSource Fallback()
    {
        if (fallback == null)
        {
            fallback = gameObject.AddComponent<AudioSource>();
            fallback.playOnAwake = false; fallback.spatialBlend = 0f; fallback.dopplerLevel = 0f; fallback.loop = false;
        }
        return fallback;
    }

    void UpdateFallback()
    {
        if (fallback == null || fallbackStopAt == double.MaxValue) return;
        double remain = fallbackStopAt - GlobalClock.DspNow;
        if (remain <= 0.0) StopFallback();
        else if (remain < 0.07) fallback.volume = fallbackVol * (float)(remain / 0.07);
    }

    void StopFallback()
    {
        if (fallback != null) fallback.Stop();
        fallbackStopAt = double.MaxValue; fallbackStartAt = -1.0;
    }

    /// <summary>How many times any cube dropped its pending notes (CancelOwner); a debug counter for the world tests (SPEC 9.2 item 5).</summary>
    public static int CancelCount;

    /// <summary>Drops this cube's queued notes and releases its sounding ones; the next Update re-schedules from the clock.</summary>
    void CancelPending()
    {
        CancelCount++;
        Synth.CancelOwner(owner);
        live.Clear();
        if (fallback != null && fallbackStartAt > GlobalClock.DspNow) StopFallback();
        lastScheduledDsp = -1.0;
    }

    /// <summary>
    /// An edit while playing (path, stickers, necklace, step, mode, octave, modifiers): the queued notes from 60 ms on are dropped and
    /// their steps re-decided under the new settings; notes already sounding or due within 60 ms stay untouched (their queued
    /// note-offs are re-issued), so an edit never re-triggers the current step late or cuts a note short. Stopped: as CancelPending.
    /// </summary>
    void SoftReschedule()
    {
        if (!GlobalClock.IsPlaying || !Synth.Ready || Performance.StutterActive) { CancelPending(); return; }
        CancelCount++;
        double now = GlobalClock.DspNow;
        double cut = now + TempoCutAhead;
        Synth.CancelOwnerAfter(owner, cut);
        for (int i = live.Count - 1; i >= 0; i--)
        {
            LiveNote ln = live[i];
            if (ln.offDsp <= now || ln.onDsp >= cut) { live.RemoveAt(i); continue; }   // over, or its note-on was just dropped (re-scheduled below)
            if (ln.offDsp >= cut) Synth.NoteOff(ln.slot, ln.midi, ln.offDsp, owner);    // CancelOwnerAfter dropped its queued off
        }
        if (fallback != null && fallbackStartAt > now) StopFallback();
        if (lastScheduledDsp >= cut) lastScheduledDsp = cut - 1e-3;                    // steps from the cut on are scheduled again
    }

    /// <summary>Tests: over every cube, the live (sounding or queued) notes and the worst offDsp − (winEndDsp − 5 ms) among them (positive = a note-off past its window end).</summary>
    public static string LiveReport()
    {
        int n = 0; double worst = double.NegativeInfinity; double now = GlobalClock.DspNow;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null) continue;
            foreach (var ln in c.live) { if (ln.offDsp <= now) continue; n++; worst = Math.Max(worst, ln.offDsp - (ln.winEndDsp - 0.005)); }
        }
        return "live=" + n + " worstOffPastWindow=" + (n > 0 ? worst.ToString("F4") : "n/a");
    }
    /// <summary>Tests: the worst offDsp − (winEndDsp − 5 ms) over every cube's live notes (−1 when none).</summary>
    public static double WorstLiveOverrun()
    {
        double worst = -1.0, now = GlobalClock.DspNow;
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; foreach (var ln in c.live) if (ln.offDsp > now) worst = Math.Max(worst, ln.offDsp - (ln.winEndDsp - 0.005)); }
        return worst;
    }

    /// <summary>Lets every cube schedule again from <paramref name="fromDsp"/> on (after a stutter release dropped its notes); steps before it stay skipped.</summary>
    public static void ResumeAll(double fromDsp)
    {
        foreach (var c in SequenceMaster.Cubes) if (c != null) c.lastScheduledDsp = fromDsp - 1e-3;
    }

    /// <summary>v5: queues every playing cube's notes up to <see cref="RebuildLookahead"/> ahead NOW. A rebuild while playing (a drag, a repeat,
    /// undo) calls it right after <see cref="ResumeAll"/>, and a song start calls it after GlobalClock.Play: the re-created cubes would otherwise
    /// schedule in their first Update, and a big song's rebuild plus the new world's first frame can outlast the normal lookahead (a 46-island
    /// song: a few notes 4-17 ms late).</summary>
    public static void ScheduleAllNow()
    {
        if (!GlobalClock.IsPlaying) return;
        lookNow = RebuildLookahead;   // the next frames may be long (a new world renders for the first time): queue further ahead once
        try
        {
            foreach (var c in SequenceMaster.Cubes)
                if (c != null && c.isFinalized && c.nodes.Count > 0 && c.dissolveT < 0f && c.dropT >= 1f && c.previewT >= 1f) c.ScheduleNow();
        }
        finally { lookNow = Lookahead; }
    }

    /// <summary>The audio half of UpdatePlayback: schedules from the current step of the active window (or the next audible window's first step).</summary>
    void ScheduleNow()
    {
        EnsureWindow();
        float song = GlobalClock.SongBeat;
        double now = GlobalClock.DspNow;
        int a = FindWindow(song);
        if (a >= 0 && !windows[a].silent)
        {
            int k; float p;
            ComputeStep(song - windows[a].start, out k, out p);
            ScheduleFrom(a, k, now);
            return;
        }
        int w; double startBeat; int loops;
        float from = a >= 0 ? windows[a].start + windows[a].length : song;
        if (NextAudibleWindowFrom(from, out w, out startBeat, out loops)) TrySchedule(w, 0, GlobalClock.DspTimeOfBeat(startBeat), 0f, loops);
    }

    // ---- notes this cube dispatched that may still be sounding or sit queued inside the lookahead: a tempo change retimes their note-offs
    struct LiveNote { public int slot, midi; public double onDsp, offDsp, winEndDsp; }
    readonly List<LiveNote> live = new List<LiveNote>(64);
    /// <summary>Queued events closer than this to a tempo change stay as they are (the audio thread may already have applied them; one DSP buffer is at most ~43 ms).</summary>
    const double TempoCutAhead = 0.06;

    void TrackLive(VoiceRules.NoteEvent[] ev, int n, double now)
    {
        for (int i = live.Count - 1; i >= 0; i--) if (live[i].offDsp <= now) live.RemoveAt(i);
        for (int i = 0; i < n && i < ev.Length; i++)
            live.Add(new LiveNote { slot = ev[i].slot, midi = ev[i].midi, onDsp = ev[i].onDsp, offDsp = ev[i].offDsp, winEndDsp = ev[i].winEndDsp });
    }

    /// <summary>
    /// Tempo / swing change (half-time punch-in, dial, tap). The queued notes from shortly after now on are dropped and their steps are
    /// scheduled again under the new tempo (nothing already queued for the next few ms is re-fired). The sounding notes keep playing, but
    /// their note-offs are re-issued at min(old off, new window end − ε), so a faster tempo can never let this window's chord ring over
    /// the next island's downbeat (SPEC 2.8 / 2.9 rule 2); under a slower tempo the old, earlier offs simply stay.
    /// </summary>
    void HandleTempo()
    {
        double now = GlobalClock.DspNow;
        double cut = now + TempoCutAhead;                 // in the old clock's time; everything queued at/after it is dropped ...
        Synth.CancelOwnerAfter(owner, cut);
        double bps = GlobalClock.BeatsPerSecond;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            LiveNote ln = live[i];
            if (ln.offDsp <= now || ln.onDsp >= cut) { live.RemoveAt(i); continue; }   // over, or its note-on was just dropped (its step is re-scheduled below)
            ln.winEndDsp = GlobalClock.RetimeDsp(ln.winEndDsp);                          // the window end under the new tempo (a true DSP time again, ready for the next change)
            double end = ln.winEndDsp - (VoiceRules.IsSustaining(ln.slot) ? 0.25 / bps : 0.005);
            double off = Math.Max(Math.Max(Math.Min(ln.offDsp, end), now + 0.03), ln.onDsp + 0.02);
            Synth.NoteOff(ln.slot, ln.midi, off, owner);   // CancelOwnerAfter dropped the queued off: re-issue it, retimed to the new window end
            ln.offDsp = off; live[i] = ln;
        }
        if (fallback != null && fallbackStartAt > now) StopFallback();
        lastScheduledDsp = GlobalClock.RetimeDsp(cut);    // ... and re-scheduled from exactly that point in the new clock's time
    }

    void HandleStop() { CancelPending(); StopFallback(); lastStep = -1; lastWindow = -1; windowIdx = 0; hopping = false; ratchetLeft = 0; echoLeft = 0; leanDir = Vector3.zero; }
    void HandlePlay() { lastScheduledDsp = -1.0; lastStep = -1; lastWindow = -1; transportHome = false; }
    /// <summary>v7: a seek jumps the song: the queued notes go and a hop in progress starts over from where the new beat puts the cube.</summary>
    void HandleSeek() { CancelPending(); hopping = false; }

    // ------------------------------------------------------------------ visuals
    void UpdateVisuals(float dt)
    {
        UpdateSize(dt);
        squashT += dt;
        float w = Ease.Wobble(squashT, 17f, 7.5f);
        float sk = sphere ? squashKind * SphereSquash : squashKind;   // a ball squashes more when it lands
        float sy = 1f - 0.34f * w * sk;
        float sxz = 1f + 0.2f * w * sk;
        UpdateFoot(dt);
        transform.localScale = new Vector3(sxz, sy, sxz) * (ProjectConfig.CubeSize * sizeNow * breathe * foot);
        UpdateSatellite(dt);

        glow *= Mathf.Exp(-dt * 6.5f);
        hoverAmt = Mathf.Lerp(hoverAmt, hoverTarget, 1f - Mathf.Exp(-dt * 14f));
        bool dimmed = Performance.SpotlightCube != null && Performance.SpotlightCube != this;
        if (mat != null)
        {
            float baseGlow = asleep ? 0.05f : 0.14f;
            float sel = selected ? 0.22f + 0.08f * Mathf.Sin(Time.time * 7f) : 0f;
            float m = (muted || Instruments.Muted[instrument]) ? 0.35f : 1f;
            if (dimmed) m *= 0.3f;                                             // spotlight: every other cube at 30 % emission
            m *= StageLights.CubeGlow(this);                                   // v9 (L) the stage lights: a lit lead grid's cubes glow at full strength
            mat.SetColor(EmissionId, emitColor * ((baseGlow + glow * 1.1f + hoverAmt * 0.35f + sel) * m));   // the hue, not the pastel: hits keep their colour
        }
        if (halo != null)
        {
            bool show = selected || hoverAmt > 0.05f;
            if (halo.gameObject.activeSelf != show) halo.gameObject.SetActive(show);
            if (show)
            {
                halo.position = new Vector3(transform.position.x, transform.position.y - ProjectConfig.CubeSize * sizeNow * foot * 0.5f + 0.03f, transform.position.z);
                float s = ProjectConfig.CubeSize * sizeNow * foot * (1.35f + (selected ? 0.08f * Mathf.Sin(Time.time * 5f) : 0f));
                halo.localScale = new Vector3(s, 1f, s);
                haloMpb.SetColor(ColorId, Color); haloMpb.SetFloat(IntensityId, selected ? 1.3f : hoverAmt * 0.8f);
                haloRend.SetPropertyBlock(haloMpb);
            }
        }
        if (markerMpb == null) markerMpb = new MaterialPropertyBlock();
        var home = Island;
        Color beadCol = home != null && home.IsKeyboard ? Color.Lerp(Color, Look.InkColor, 0.42f) : Color;   // v6: pastel beads read on ivory keys when inked deeper
        for (int i = 0; i < markers.Count && i < nodes.Count; i++)
        {
            float p = markerPulse[i];
            markerPulse[i] = p * Mathf.Exp(-dt * 5.5f);
            float pop = 1f;
            if (i < beadPop.Count && beadPop[i] < 1f) { pop = BeadPopCurve(beadPop[i]); beadPop[i] += dt / BeadPopSeconds; }
            float s = markerBase[i] * (1f + 0.9f * p) * pop;
            markers[i].localScale = new Vector3(s, 1f, s);
            bool rest = ModOf(i) == 1;
            Color baseC = Palette.A(beadCol, rest ? 0.55f : 0.9f);
            markerMpb.SetColor(ColorId, Color.Lerp(baseC, Color.white, p * 0.85f));
            markerRends[i].SetPropertyBlock(markerMpb);
        }
        if (lineMat != null)
        {
            float li = selected ? 1.7f : 0.95f;
            if (muted || Instruments.Muted[instrument]) li *= 0.35f;
            lineMat.SetFloat(IntensityId, li);
        }
        UpdateGhosts(dt);
        UpdateDisc();
        UpdateEchoQuads(dt);
    }

    // ---- shadow ghosts: translucent copies hopping on the shadow row(s) of the current node (follow 1 above, 2 below, 3 both)
    void BuildGhost(int i)
    {
        var go = new GameObject(i == 0 ? "ShadowAbove" : "ShadowBelow");
        go.AddComponent<MeshFilter>().sharedMesh = BodyMeshFor(sphere);
        var mr = go.AddComponent<MeshRenderer>();
        if (ghostMat == null) ghostMat = Fx.Additive(IconFactory.GetTexture("white"), Palette.A(Color, 1f), 0.28f);
        mr.sharedMaterial = ghostMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        ghosts[i] = go.transform;
    }

    void UpdateGhosts(float dt)
    {
        bool up = follow == 1 || follow == 3, down = follow == 2 || follow == 3;
        TileInteraction cur = restingTile != null ? restingTile : hopTarget;
        for (int i = 0; i < 2; i++)
        {
            bool want = (i == 0 ? up : down) && cur != null && nodes.Count > 0 && !IsDrums;
            if (!want) { if (ghosts[i] != null && ghosts[i].gameObject.activeSelf) ghosts[i].gameObject.SetActive(false); continue; }
            if (ghosts[i] == null) BuildGhost(i);
            int off;
            var st = VoiceRules.ShadowTile(cur, i == 0, out off);
            if (st == null) { if (ghosts[i].gameObject.activeSelf) ghosts[i].gameObject.SetActive(false); continue; }
            if (!ghosts[i].gameObject.activeSelf) ghosts[i].gameObject.SetActive(true);
            ghostDelta[i] = Vector3.Lerp(ghostDelta[i], st.Top - cur.Top, 1f - Mathf.Exp(-dt * 10f));
            ghosts[i].position = transform.position + ghostDelta[i];
            ghosts[i].rotation = transform.rotation;
            ghosts[i].localScale = transform.localScale * 0.9f;
        }
    }

    // ---- octave -1: a dark disc around the sunk cube (height = pitch)
    void UpdateDisc()
    {
        bool want = octave < 0 && nodes.Count > 0;
        if (!want) { if (disc != null && disc.gameObject.activeSelf) disc.gameObject.SetActive(false); return; }
        if (disc == null)
        {
            Renderer r;
            if (discMat == null) discMat = Fx.Alpha(IconFactory.GetTexture("shadow"), new Color(0.02f, 0.01f, 0.05f, 0.6f));
            disc = MakeQuad("OctaveShadow", discMat, out r);
        }
        if (!disc.gameObject.activeSelf) disc.gameObject.SetActive(true);
        float s = ProjectConfig.CubeSize * sizeNow * foot * 1.5f;
        disc.position = transform.position + Vector3.up * 0.07f;
        disc.localScale = new Vector3(s, 1f, s);
    }

    // ---- echo afterimages: fading glow quads on the original tile at each repeat
    void SpawnAfterimage(TileInteraction tile, int idx)
    {
        int i = ((idx % 2) + 2) % 2;
        if (echoQuads[i] == null)
        {
            if (echoMat == null) echoMat = Fx.Additive(IconFactory.GetTexture("glow"), Color, 1f);
            Renderer r;
            echoQuads[i] = MakeQuad("Afterimage", echoMat, out r);
            echoRends[i] = r;
        }
        echoAge[i] = 0f;
        echoQuads[i].position = TopOf(tile) + Vector3.up * 0.06f;
        echoQuads[i].gameObject.SetActive(true);
        if (layer == 0) tile.Flash(Color, 0.35f);
    }

    void UpdateEchoQuads(float dt)
    {
        for (int i = 0; i < 2; i++)
        {
            if (echoQuads[i] == null) continue;
            if (echoAge[i] >= 0.55f) { if (echoQuads[i].gameObject.activeSelf) echoQuads[i].gameObject.SetActive(false); continue; }
            if (echoMpb == null) echoMpb = new MaterialPropertyBlock();
            echoAge[i] += dt;
            float t = Mathf.Clamp01(echoAge[i] / 0.55f);
            float s = ProjectConfig.CubeSize * (1.1f + 1.2f * t);
            echoQuads[i].localScale = new Vector3(s, 1f, s);
            echoMpb.SetColor(ColorId, Color); echoMpb.SetFloat(IntensityId, (1f - t) * (1f - t) * 1.4f);
            echoRends[i].SetPropertyBlock(echoMpb);
        }
    }

    public void SetSelected(bool s) { selected = s; }
    public void SetHover(bool h) { hoverTarget = h ? 1f : 0f; }

    // ------------------------------------------------------------------ v3 package A: inspector support + path editing (SPEC v3 §2.2-§2.5)
    /// <summary>The tile the cube is standing on right now (null mid-hop).</summary>
    public TileInteraction RestingTile => restingTile;
    /// <summary>The tile the cube stands on, else its home node's tile.</summary>
    public TileInteraction CurrentOrHomeTile => restingTile != null ? restingTile : (nodes.Count > 0 ? nodes[Mathf.Clamp(HomeNode, 0, nodes.Count - 1)] : null);
    /// <summary>The body renderer (the hover rule projects its bounds).</summary>
    public Renderer BodyRenderer => rend;
    /// <summary>A landing-style squash of the body (kind 1 = a normal landing).</summary>
    public void Squash(float kind = 1f) { squashT = 0f; squashKind = kind; }

    int pathQueue = -1;
    /// <summary>While inspected, the path line, beads, ticks and sticker glyphs draw after the inspector's dim (render queue 3500),
    /// so the cube's own path stays lit in the dimmed world; off restores the shaders' queues.</summary>
    public void SetInspectHighlight(bool on)
    {
        pathQueue = on ? 3500 : -1;
        SetQueue(lineMat); SetQueue(matDot); SetQueue(matRing); SetQueue(matRest); SetQueue(matTick); SetQueue(matTie);
        foreach (var kv in glyphMats) SetQueue(kv.Value);
    }
    void SetQueue(Material m) { if (m != null) m.renderQueue = pathQueue; }

    /// <summary>Replaces the whole path with its stickers in one go (mods[i] belongs to tiles[i]); rests and grid offsets follow.
    /// The flat path grid does its insert / move / remove surgery through this, so every list stays aligned with nodes.</summary>
    public void SetPathAndMods(List<TileInteraction> tiles, List<int> newMods) { SetPathAndMods(tiles, newMods, null); }

    /// <summary>
    /// SPEC v4 R1: <see cref="SetPathAndMods(List{TileInteraction}, List{int})"/> with the per-node lengths (ticks) of the new path.
    /// <paramref name="newDurs"/> null: a durations cube keeps its lengths by position (a node past the old end repeats the last length;
    /// right for a pencil restart or appended tail, which is how the flat grid edits), a legacy cube stays legacy; given: the cube runs the
    /// durations engine with them (missing entries repeat the last one).
    /// </summary>
    public void SetPathAndMods(List<TileInteraction> tiles, List<int> newMods, IList<int> newDurs) { SetPathAndMods(tiles, newMods, newDurs, null); }

    /// <summary>v7: <see cref="SetPathAndMods(List{TileInteraction}, List{int}, IList{int})"/> with the per-node bends of the new path (entry i =
    /// node i). <paramref name="newBend"/> null: a bent cube keeps its bends by position (like the lengths; a node past the old end gets none).</summary>
    public void SetPathAndMods(List<TileInteraction> tiles, List<int> newMods, IList<int> newDurs, IList<int> newBend) { SetPathAndMods(tiles, newMods, newDurs, newBend, null); }

    /// <summary>v9 (N): <see cref="SetPathAndMods(List{TileInteraction}, List{int}, IList{int}, IList{int})"/> with the per-node nudges of the new path
    /// (entry i = node i). <paramref name="newNudge"/> null: a nudged cube keeps its nudges by position (a node past the old end gets none).</summary>
    public void SetPathAndMods(List<TileInteraction> tiles, List<int> newMods, IList<int> newDurs, IList<int> newBend, IList<int> newNudge)
    {
        var t = new List<TileInteraction>();
        var m = new List<int>();
        var d = new List<int>();
        var b = new List<int>();
        var g = new List<int>();
        bool dur = newDurs != null && newDurs.Count > 0 || HasDurations;
        bool bent = newBend != null ? HasAny(newBend) : bend.Count > 0;
        bool nudged = newNudge != null ? HasAny(newNudge) : nudges.Count > 0;
        var oldDurs = new List<int>(durs);
        var oldBend = new List<int>(bend);
        var oldNudge = new List<int>(nudges);
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i] == null) continue;
            t.Add(tiles[i]);
            m.Add(newMods != null && i < newMods.Count ? Mathf.Clamp(newMods[i], 0, 8) : 0);
            if (bent) b.Add(newBend != null ? (i < newBend.Count ? Mathf.Clamp(newBend[i], -24, 24) : 0) : (i < oldBend.Count ? oldBend[i] : 0));
            if (nudged) g.Add(newNudge != null ? (i < newNudge.Count ? Nudge.Clamp(newNudge[i]) : 0) : (i < oldNudge.Count ? oldNudge[i] : 0));
            if (!dur) continue;
            int v;
            if (newDurs != null && newDurs.Count > 0) v = newDurs[Mathf.Min(i, newDurs.Count - 1)];
            else v = oldDurs.Count == 0 ? 24 : oldDurs[Mathf.Min(i, oldDurs.Count - 1)];
            d.Add(ClampTicks(v));
        }
        if (t.Count == 0) return;
        bool wasLegacy = !HasDurations;
        nodes = t;
        mods.Clear(); rests.Clear();
        for (int i = 0; i < m.Count; i++) { mods.Add(m[i]); rests.Add(m[i] == 1); }
        durs.Clear();
        if (dur) { durs.AddRange(d); if (wasLegacy) { hits = -1; rot = 0; pattern = null; } }
        bend.Clear();
        if (bent && HasAny(b)) bend.AddRange(b);
        nudges.Clear();
        if (nudged && HasAny(g)) nudges.AddRange(g);
        PathEdited();
    }

    static bool HasAny(IList<int> l) { if (l == null) return false; for (int i = 0; i < l.Count; i++) if (l[i] != 0) return true; return false; }
    /// <summary>The per-node bends of this cube as a new list (null when it has none).</summary>
    List<int> BendCopy() => bend.Count == nodes.Count && bend.Count > 0 ? new List<int>(bend) : null;
    /// <summary>v9 (N): the per-node nudges of this cube as a new list (null when it has none).</summary>
    List<int> NudgeCopy() => nudges.Count == nodes.Count && nudges.Count > 0 ? new List<int>(nudges) : null;

    /// <summary>SPEC v4 R3: the draft's path and lengths in one go (existing stickers stay on their nodes, new nodes get none).</summary>
    public void SetPathAndDurations(List<TileInteraction> tiles, IList<int> newDurs)
    {
        var m = new List<int>(tiles != null ? tiles.Count : 0);
        if (tiles != null) for (int i = 0; i < tiles.Count; i++) m.Add(ModOf(i));
        SetPathAndMods(tiles, m, newDurs);
    }

    /// <summary>The per-node lengths of this cube as a new list (empty for a legacy cube).</summary>
    List<int> DursCopy() => HasDurations ? new List<int>(durs) : null;

    /// <summary>Inserts a node (with its sticker) at <paramref name="index"/> (Count = append). A durations cube gives it
    /// <paramref name="ticks"/> (-1: the length of the node before it, or the first node's).</summary>
    public void InsertNode(int index, TileInteraction tile, int mod = 0, int ticks = -1)
    {
        if (tile == null) return;
        SyncLists();
        var t = new List<TileInteraction>(nodes); var m = new List<int>(mods); var d = DursCopy(); var b = BendCopy(); var g = NudgeCopy();
        index = Mathf.Clamp(index, 0, t.Count);
        t.Insert(index, tile); m.Insert(index, Mathf.Clamp(mod, 0, 8));
        if (d != null) d.Insert(index, ticks > 0 ? ClampTicks(ticks) : (d.Count == 0 ? 24 : d[Mathf.Clamp(index - 1, 0, d.Count - 1)]));
        if (b != null) b.Insert(index, 0);   // v7: a new note has no bend
        if (g != null) g.Insert(index, 0);   // v9: nor a nudge
        SetPathAndMods(t, m, d, b, g ?? new List<int>());
    }

    /// <summary>Removes node <paramref name="index"/> and its sticker (at least one node always stays).</summary>
    public bool RemoveNode(int index)
    {
        if (index < 0 || index >= nodes.Count || nodes.Count <= 1) return false;
        SyncLists();
        var t = new List<TileInteraction>(nodes); var m = new List<int>(mods); var d = DursCopy(); var b = BendCopy(); var g = NudgeCopy();
        t.RemoveAt(index); m.RemoveAt(index);
        if (d != null) d.RemoveAt(index);
        if (b != null) b.RemoveAt(index);
        if (g != null) g.RemoveAt(index);
        SetPathAndMods(t, m, d, b != null ? b : new List<int>(), g ?? new List<int>());
        return true;
    }

    /// <summary>Moves node <paramref name="index"/> onto <paramref name="tile"/>; its sticker moves with it.</summary>
    public bool MoveNode(int index, TileInteraction tile)
    {
        if (tile == null || index < 0 || index >= nodes.Count || nodes[index] == tile) return false;
        SyncLists();
        var t = new List<TileInteraction>(nodes); var m = new List<int>(mods); var b = BendCopy();
        t[index] = tile;
        if (b != null) b[index] = 0;   // v7: moved onto another tile, the note plays that tile (its bend belonged to the old one)
        SetPathAndMods(t, m, DursCopy(), b != null ? b : new List<int>(), null);   // v9: a nudge is relative — it moves with its note
        return true;
    }

    /// <summary>Reverses the node order together with the stickers.</summary>
    public void ReversePath()
    {
        if (nodes.Count < 2) return;
        SyncLists();
        var t = new List<TileInteraction>(nodes); var m = new List<int>(mods); var d = DursCopy(); var b = BendCopy(); var g = NudgeCopy();
        t.Reverse(); m.Reverse();
        if (d != null) d.Reverse();   // each length travels with its node
        if (b != null) b.Reverse();   // v7: and each bend
        if (g != null) g.Reverse();   // v9: and each nudge
        SetPathAndMods(t, m, d, b != null ? b : new List<int>(), g ?? new List<int>());
    }

    /// <summary>True when <see cref="ShiftPath"/>(dx, dz) would keep every node inside the island grid.</summary>
    public bool CanShift(int dx, int dz)
    {
        if (nodes.Count == 0 || (dx == 0 && dz == 0)) return false;
        var isl = nodes[0] != null ? nodes[0].island : null;
        if (isl == null) return false;
        foreach (var n in nodes)
        {
            if (n == null || n.island != isl) return false;
            int x = n.gridX + dx, z = n.gridZ + dz;
            if (x < 0 || z < 0 || x >= isl.cols || z >= isl.rows) return false;
        }
        return true;
    }

    /// <summary>Moves the whole path by (dx, dz) cells on its island when every node stays inside the grid; false (nothing
    /// changes) otherwise.</summary>
    public bool ShiftPath(int dx, int dz)
    {
        if (nodes.Count == 0 || (dx == 0 && dz == 0)) return false;
        var isl = nodes[0] != null ? nodes[0].island : null;
        if (isl == null) return false;
        SyncLists();
        var t = new List<TileInteraction>(nodes.Count);
        foreach (var n in nodes)
        {
            if (n == null || n.island != isl) return false;
            int x = n.gridX + dx, z = n.gridZ + dz;
            if (x < 0 || z < 0 || x >= isl.cols || z >= isl.rows) return false;
            var nt = isl.GetTile(x, z);
            if (nt == null) return false;
            t.Add(nt);
        }
        SetPathAndMods(t, new List<int>(mods), DursCopy(), BendCopy() ?? new List<int>());
        return true;
    }

    /// <summary>After any path edit: grid offsets, legacy positions, the line and beads, pending notes and the step cursor.</summary>
    void PathEdited()
    {
        motionVersion++;
        SyncLists();
        pathNodes.Clear(); foreach (var n in nodes) if (n != null) pathNodes.Add(n.Top);
        lastStep = -1; lastWindow = -1; hopping = false; ratchetLeft = 0; echoLeft = 0; leanDir = Vector3.zero;
        SoftReschedule();
        RefreshLine();
        OnAnyChanged?.Invoke(this);
    }

    // ------------------------------------------------------------------ editing API
    public int NodeIndexOf(TileInteraction t)
    {
        for (int i = 0; i < nodes.Count; i++) if (nodes[i] == t) return i;
        return -1;
    }
    /// <summary>The click gesture: rest on/off (mods 0 <-> 1; another sticker becomes a rest); rests[i] mirrors it.</summary>
    public void ToggleRest(int i)
    {
        if (i < 0 || i >= nodes.Count) return;
        SyncLists();
        mods[i] = mods[i] == 1 ? 0 : 1;
        rests[i] = mods[i] == 1;
        SoftReschedule();   // v3: the rest applies from the next step on (queued notes re-decided)
        RefreshLine();
        OnAnyChanged?.Invoke(this);
    }
    public void SetInstrument(int i)
    {
        int ni = Mathf.Clamp(i, 0, Instruments.Count - 1);
        if (ni != instrument) voice = Instruments.BrushVoiceOf(ni);   // v6: a new group, its brush sound
        instrument = ni; ApplyInstrumentLook(); CancelPending(); RefreshLine(); TileInteraction.RefreshKitLook(); OnAnyChanged?.Invoke(this);
    }
    public void SetStep(StepLen s) { step = s; lastStep = -1; hopping = false; SoftReschedule(); RefreshLine(); OnAnyChanged?.Invoke(this); }
    public void SetGate(Gate g) { gate = g; OnAnyChanged?.Invoke(this); }
    public void SetMode(PathMode m) { mode = m; lastStep = -1; hopping = false; SoftReschedule(); RefreshLine(); OnAnyChanged?.Invoke(this); }
    public void SetVolume(float v) { volume = Mathf.Clamp01(v); OnAnyChanged?.Invoke(this); }
    public void SetMuted(bool m) { muted = m; CancelPending(); OnAnyChanged?.Invoke(this); }

    // v2 setters (SPEC §3.1): each clamps, drops pending notes, redraws the path and notifies. History.Push is the gesture owner's job.
    void Changed() { motionVersion++; SoftReschedule(); RefreshLine(); OnAnyChanged?.Invoke(this); }
    /// <summary>Necklace hits: -1 = every step, -2 = explicit mask, 1..n Euclidean.</summary>
    public void SetHits(int k) { hits = k < -2 ? -1 : Mathf.Min(k, 24); lastStep = -1; Changed(); }
    /// <summary>Necklace rotation, wrapped into [0, StepsPerBar).</summary>
    public void SetRot(int r) { int n = StepsPerBar; rot = ((r % n) + n) % n; lastStep = -1; Changed(); }
    /// <summary>Explicit per-bar hit bits (bit j = step j), used when hits == -2.</summary>
    public void SetMask(int m) { mask = m; lastStep = -1; Changed(); }
    /// <summary>Sticker of a node (0 none 1 rest 2 ghost 3 accent 4 ratchet2 5 ratchet3 6 coin 7 tie 8 lift); rests[node] follows (rest == 1).</summary>
    public void SetMod(int node, int mod)
    {
        if (node < 0 || node >= nodes.Count) return;
        SyncLists();
        mods[node] = Mathf.Clamp(mod, 0, 8);
        rests[node] = mods[node] == 1;
        Changed();
    }
    public void SetOctave(int o) { octave = Mathf.Clamp(o, -1, 1); Changed(); }
    /// <summary>v9 (N): the nudge of a node (scale steps of the song key, −2..+2; 0 = the tile's note). The list is dropped when every node is at 0.
    /// Notifies; no History (the gesture owner pushes). False when nothing changed.</summary>
    public bool SetNudge(int node, int n)
    {
        if (node < 0 || node >= nodes.Count) return false;
        SyncLists();
        n = Nudge.Clamp(n);
        if (NudgeAt(node) == n) return false;
        if (nudges.Count != nodes.Count) { nudges.Clear(); for (int i = 0; i < nodes.Count; i++) nudges.Add(0); }
        nudges[node] = n;
        if (!HasAny(nudges)) nudges.Clear();
        Changed();
        return true;
    }
    public void SetFollow(int f) { follow = Mathf.Clamp(f, 0, 3); Changed(); }
    public void SetEcho(int e) { echo = Mathf.Clamp(e, 0, 2); Changed(); }
    public void SetShimmer(bool s) { shimmer = s; Changed(); }
    public void SetReverse(bool r) { reverse = r; lastStep = -1; hopping = false; Changed(); }
    public void SetPhase(int p) { phase = Mathf.Clamp(p, 0, 63); lastStep = -1; hopping = false; Changed(); }
    /// <summary>Rider (replays on every Route island; its windows come from SongManager); pitched cubes only.</summary>
    public void SetRider(bool r) { rider = r && !IsDrums; Changed(); SequenceMaster.RecalculateTimeline(); }   // the windows come from SongManager: recompute at once, or the toggle is inaudible until the next rebuild

    /// <summary>
    /// A twin's state (§3.1 #10): this cube's state with twinOf = id and id = 0, modified by variant
    /// 0 = half speed (step index -1, clamped to Half; a Triplet source drops to Eighth) + octave +1 (clamped to +1);
    /// 1 = double speed (step index +1, clamped to Sixteenth, Triplet never entered unless the source is Triplet);
    /// 2 = reversed; 3 = +1 hit phase. Pure.
    /// </summary>
    public CubeState TwinState(int variant)
    {
        var s = ToState();
        s.twinOf = id; s.id = 0;
        switch (variant)
        {
            case 0:
                s.step = s.step == (int)StepLen.Triplet ? (int)StepLen.Eighth : Mathf.Max((int)StepLen.Half, s.step - 1);
                s.octave = Mathf.Min(1, s.octave + 1);
                if (s.durs != null) for (int i = 0; i < s.durs.Length; i++) s.durs[i] = SnapTicks(s.durs[i] * 2f);     // v4: half speed = twice as long
                break;
            case 1:
                s.step = s.step == (int)StepLen.Triplet ? (int)StepLen.Triplet : Mathf.Min((int)StepLen.Sixteenth, s.step + 1);
                if (s.durs != null) for (int i = 0; i < s.durs.Length; i++) s.durs[i] = SnapTicks(s.durs[i] * 0.5f);    // double speed = half as long
                break;
            case 2: s.reverse = !s.reverse; break;
            case 3: s.phase = s.phase + 1; break;
        }
        return s;
    }

    public void Delete(bool fx = true)
    {
        if (fx) { Fx.Pop(transform.position, Color); AudioPool.UI(ProceduralAudio.Pop(), 0.5f); }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        Synth.CancelOwner(owner);
        CubeSmear.Forget(owner);
        SequenceMaster.Unregister(this);
        if (restingTile != null) SequenceMaster.Release(restingTile, this);
        if (line != null) Destroy(line.gameObject);
        if (halo != null) Destroy(halo.gameObject);
        foreach (var m in markers) if (m != null) Destroy(m.gameObject);
        foreach (var g in glyphs) if (g != null) Destroy(g.gameObject);
        foreach (var t in tieBars) if (t != null) Destroy(t.gameObject);
        foreach (var t in ticks) if (t != null) Destroy(t.gameObject);
        foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
        foreach (var q in echoQuads) if (q != null) Destroy(q.gameObject);
        if (disc != null) Destroy(disc.gameObject);
        foreach (var d in dotMarks) if (d != null) Destroy(d.gameObject);
        if (sat != null) Destroy(sat.gameObject);
        // materials are per cube (Fx.* creates one per call; rend.material is an instance): Unity does not free them with the GameObject
        Kill(mat); Kill(lineMat); Kill(matDot); Kill(matRing); Kill(matRest); Kill(matTick); Kill(matTie); Kill(ghostMat); Kill(echoMat); Kill(haloMat); Kill(discMat);
        Kill(holoMat); Kill(satMat); Kill(glassMat); for (int i = 0; i < holoGhostMat.Length; i++) Kill(holoGhostMat[i]);
        foreach (var kv in glyphMats) Kill(kv.Value);
        glyphMats.Clear();
        if (PathManager.I != null && PathManager.I.selectedCube == this) PathManager.I.Deselect();
    }

    static void Kill(UnityEngine.Object o) { if (o != null) Destroy(o); }

    /// <summary>The stop / reset: the cube's cursors reset and its queued notes dropped, and it goes HOME — v7 (SPEC v7 §12.1: "no teleportation, just
    /// movement"): from where it stands it travels there in real time (<see cref="StopTripSeconds"/>, a hop-glide over its grid, an arc from another
    /// one, each cube a little out of sync); already home (or hidden) it just settles. <see cref="SnapHome"/> = the v6 snap.</summary>
    public void ResetToStart()
    {
        lastStep = -1; lastWindow = -1; hopping = false; ratchetLeft = 0; echoLeft = 0; leanDir = Vector3.zero; CancelPending(); StopFallback();
        noteNode = HomeNode; noteProgress = 0f;
        forcedHome = true; transportHome = true;
        if (nodes.Count > 0 && dropT >= 1f && previewT >= 1f)
        {
            var home = HomeTile;
            if (home != null)
            {
                var wasOn = restingTile != null ? restingTile : (hopTarget != null ? hopTarget : home);
                Vector3 from = transform.position - extraApplied - peekPosApplied;
                SetResting(home);
                Vector3 to = RestPosition(home, StackSlot(home));
                Vector3 d = to - from; float flat = new Vector2(d.x, d.z).magnitude;
                if (d.magnitude > 0.05f && Application.isPlaying && isActiveAndEnabled && bodyVisible)
                {
                    stopFrom = from; stopFirst = true;
                    stopFromRot = transform.rotation;
                    stopTripT = 0f;
                    float bps = Mathf.Max(0.5f, (float)GlobalClock.BeatsPerSecond);
                    stopTripDelay = Mathf.Min(0.25f, ResetStagger / bps);
                    stopHops = Mathf.Max(1, Mathf.RoundToInt(flat / TripHopSpan)); stopArc = 0.32f;   // §21: along its grid in small hops, never an arc
                    StopTripCount++;
                }
                else
                {
                    stopTripT = 9f;
                    transform.position = to;
                    transform.rotation = Quaternion.identity;
                    CubeSmear.Cut(owner);   // already home: no motion, no smear
                }
            }
        }
        isReadyToPlay = true;
    }

    // ------------------------------------------------------------------ state
    /// <summary>A fresh stable id: one more than the largest id among live cubes.</summary>
    public static int NextId()
    {
        int max = 0;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.id > max) max = c.id;
        return max + 1;
    }

    public CubeState ToState()
    {
        var s = new CubeState
        {
            instrument = instrument, measure = assignedGridIndex, step = (int)step, gate = (int)gate, mode = (int)mode, volume = volume, muted = muted,
            moon = moon, hits = hits, rot = rot, mask = mask, octave = octave, reverse = reverse, phase = phase, follow = follow, echo = echo,
            shimmer = shimmer, rider = rider, seed = seed, twinOf = twinOf, id = id, voice = voice,
            layer = layer, echoOf = echoOf,   // SPEC v7 §2.1
            climb = climb,
            sphere = sphere   // SphereBody.cs
        };
        s.durs = HasDurations ? durs.ToArray() : null;   // SPEC v4 §2.1
        s.bend = bend.Count == nodes.Count && bend.Count > 0 ? bend.ToArray() : null;   // SPEC v7 §2.1
        s.nudges = HasNudges ? nudges.ToArray() : null;   // v9 (N)
        s.xs = new int[nodes.Count]; s.zs = new int[nodes.Count]; s.rests = new bool[nodes.Count]; s.mods = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            s.xs[i] = nodes[i].gridX; s.zs[i] = nodes[i].gridZ;
            int m = ModOf(i);
            s.rests[i] = m == 1; s.mods[i] = m;
        }
        return s;
    }

    public void ApplySettings(CubeState s)
    {
        step = (StepLen)Mathf.Clamp(s.step, 0, 4); gate = (Gate)Mathf.Clamp(s.gate, 0, 2); mode = (PathMode)Mathf.Clamp(s.mode, 0, 2);
        volume = s.volume <= 0f && !s.muted ? 1f : Mathf.Clamp01(s.volume); muted = s.muted;
        SyncLists();
        var m = s.ModsOrDerived();
        for (int i = 0; i < nodes.Count; i++)
        {
            int mi = i < m.Length ? Mathf.Clamp(m[i], 0, 8) : 0;
            mods[i] = mi;
            rests[i] = mi == 1;
        }
        moon = s.moon; hits = s.hits < -2 ? -1 : s.hits; rot = s.rot; mask = s.mask; octave = Mathf.Clamp(s.octave, -1, 1);
        reverse = s.reverse; phase = Mathf.Clamp(s.phase, 0, 63); follow = Mathf.Clamp(s.follow, 0, 3); echo = Mathf.Clamp(s.echo, 0, 2); shimmer = s.shimmer; rider = s.rider && !IsDrums;
        seed = s.SeedOrDerived(); twinOf = s.twinOf; id = s.id;
        voice = Mathf.Clamp(s.voice, 0, Mathf.Max(0, Instruments.VoiceCount(instrument) - 1));   // SPEC v6 §2.7
        layer = IsDrums ? 0 : Mathf.Clamp(s.layer, -ProjectConfig.MaxLayer, ProjectConfig.MaxLayer); echoOf = s.echoOf;   // SPEC v7 §2.1
        climb = IsDrums ? 0 : Mathf.Clamp(s.climb, 0, Climb.MaxLevel);
        SetShape(s.sphere);   // SphereBody.cs
        bend.Clear();
        if (s.bend != null && s.bend.Length == nodes.Count) for (int i = 0; i < s.bend.Length; i++) bend.Add(Mathf.Clamp(s.bend[i], -24, 24));
        nudges.Clear();   // v9 (N): per-node nudges only when they line up with the restored nodes (an old save has none)
        if (s.nudges != null && s.nudges.Length == nodes.Count && Nudge.Any(s.nudges)) for (int i = 0; i < s.nudges.Length; i++) nudges.Add(Nudge.Clamp(s.nudges[i]));
        durs.Clear();   // SPEC v4 §2.1: per-node lengths only when they line up with the restored nodes
        if (s.durs != null && s.durs.Length == nodes.Count) for (int i = 0; i < s.durs.Length; i++) durs.Add(ClampTicks(s.durs[i]));
        pattern = null;
        motionVersion++;
        RefreshLine();
    }
}
