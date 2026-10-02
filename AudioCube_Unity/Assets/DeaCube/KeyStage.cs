using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v8 (the user: "the piano should also have an effect where if it's just on and off (like in the midi) it will appear (pop in from the ocean) and
/// then disappear after its part. like take note from games like geometry dash where it's not just the music but the effects too"): THE STAGE of the
/// keyboards. While the song plays, a keyboard whose music comes in PARTS — its notes (KeyHands' score: every audible note of every cube on it)
/// grouped where they are less than <see cref="PartGapBars"/> measures apart — pops up from the sea just before each part (a splash, up past its
/// place, settling: done <see cref="LeadBeats"/> before the first note) and sinks after it (a little bob, then down, listing like a boat, with
/// ring waves, foam and bubbles: KeyBlock's tilt and UpdateWater). A keyboard that
/// plays the whole loop (no gap) or has no notes yet stays up; every keyboard stays up while the song is stopped (a stop or a start glides there).
/// The pose is a pure function of the song beat (KeyBlock.PartDepth): seeks, loops (a part at the loop's start pops in before the wrap) and a
/// replay land exactly; the drag between checkpoints and the spheres ride along. <see cref="Drives"/> tells the present mode which keyboards are ours.
/// RUNS (a gallery song puts one keyboard per voice per section column): keyboards in CONSECUTIVE columns, in the same lane (|Δpz| &lt; RunLaneTol)
/// playing the same voices (the (instrument, voice) set of their non-layer cubes) are ONE part source — their notes are scored together, so a piano
/// that plays through the whole song never sinks at a section boundary, and an 8-bar run of two keyboards rises once and sinks once.
/// v9 FRAGMENTS (the user: "there are times where a melody appears for 1-2 measures only - they should be separate grids that appear and are
/// overlayed"): any other island kind joins the stage when it is a FRAGMENT — it plays less than its column's pass (a grid on a later measure or
/// shorter than its column). Its parts are its play windows (the measures it sounds in each pass it plays), and its run takes in the same-voice
/// islands of its lane in the same or the next column (a fragment leading into a full grid of the same voices rises once and stays up through
/// it). A run with no fragment, or one that covers the whole loop, never pops: a bass grid in every bar stays up, and an ordinary song is unchanged.
/// An island with no cubes yet stays up.
/// v9 round 3 (the gallery song: 189 of 190 islands were driven, the bass lane sank in a one-bar break): a non-keyboard run links by TIME, not by
/// column — an island joins the lane's earlier island that shares a voice with it (not the identical set) and ends less than PartGapBars before it
/// starts (an empty bar or a fill in between does not cut the lane); and a run is driven only when it plays less than <see cref="DriveMaxCover"/>
/// of the loop and its silence (the gap round the loop point included) reaches PartGapBars.
/// </summary>
[DefaultExecutionOrder(970)]   // after KeyHands (its score) and the islands' own pose
public class KeyStage : MonoBehaviour
{
    /// <summary>Notes less than this many measures apart belong to one part.</summary>
    public const float PartGapBars = 2f;
    /// <summary>The pop (s), how long before a part's first note it has settled (beats), the wait after its last note (beats), the sink (s).</summary>
    public const float PopS = 0.45f, LeadBeats = 0.5f, TailBeats = 0.5f, SinkS = 0.55f;
    /// <summary>A play / stop / seek eases the stage there over this long (s) instead of jumping.</summary>
    public const float EaseS = 0.3f;
    /// <summary>Two keyboards are in the same lane when their pz differ by less than this.</summary>
    public const float RunLaneTol = 0.5f;
    /// <summary>v9: a non-keyboard run that plays at least this share of the loop is never driven (a lane that plays nearly the whole song stays up).</summary>
    public const float DriveMaxCover = 0.75f;
    /// <summary>Off: every keyboard stays up (tests of other features may switch it off).</summary>
    public static bool Enabled = true;

    public static KeyStage I { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I == null && Application.isPlaying) I = new GameObject("KeyStage").AddComponent<KeyStage>(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Enabled = true; Pops = 0; Sinks = 0; }

    /// <summary>Pops and sinks shown so far (tests); v9 perf: how often the runs were rebuilt.</summary>
    public static int Pops, Sinks, RunRebuilds;

    class Stage { public KeyBlock kb; public readonly List<Vector2> parts = new List<Vector2>(); public int scored = -1; public float shown, from; public float easeT = 9f; public bool up = true; public bool fragment; public int gapSig = int.MinValue; public bool gap; }
    readonly Dictionary<KeyBlock, Stage> stages = new Dictionary<KeyBlock, Stage>();
    readonly List<KeyBlock> gone = new List<KeyBlock>();
    bool wasPlaying; double lastBeat;

    void Awake() { if (I != null && I != this) { Destroy(gameObject); return; } I = this; }
    void OnDestroy() { if (I == this) I = null; }

    /// <summary>True when the stage drives island <paramref name="kb"/> (a keyboard that plays in parts with a gap, v9 a fragment's run with a gap:
    /// it pops in and sinks out with them) — the present mode leaves those to it.</summary>
    public static bool Drives(KeyBlock kb)
    {
        if (!Enabled || I == null || !Stageable(kb)) return false;
        var st = I.StageOf(kb);
        return st != null && I.HasGap(st);
    }

    /// <summary>The parts of keyboard <paramref name="kb"/> (song beats: first onset, last note's end), for tests.</summary>
    public static IReadOnlyList<Vector2> PartsOf(KeyBlock kb) { var st = I != null ? I.StageOf(kb) : null; return st != null ? (IReadOnlyList<Vector2>)st.parts : new Vector2[0]; }

    /// <summary>The depth keyboard <paramref name="kb"/> takes at song beat <paramref name="beat"/> while playing (pure: 0 up, 1 under the sea,
    /// below 0 = the pop's overshoot); 0 when the stage does not drive it.</summary>
    public static float DepthAt(KeyBlock kb, double beat)
    {
        if (I == null) return 0f;
        var st = I.StageOf(kb);
        return st != null && Enabled && I.HasGap(st) ? I.Depth(st, beat) : 0f;
    }

    /// <summary>v9: the islands the stage may drive — keyboards and every other grid kind (never a Moon or a phrase).</summary>
    static bool Stageable(KeyBlock kb) => kb != null && !kb.IsMoon && !kb.IsPhrase;

    /// <summary>v9: a non-keyboard island that plays less than its column's pass (a later measure, or fewer measures than the column).</summary>
    public static bool IsFragment(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (!Stageable(kb) || kb.IsKeyboard || sm == null) return false;
        float pass = sm.PassLength(kb.column);
        return pass > 0f && (SongManager.OffsetBars(kb) > 0 || kb.LengthBeats < pass - 1e-3f);
    }

    Stage StageOf(KeyBlock kb)
    {
        if (!Stageable(kb)) return null;
        EnsureRuns();
        Stage st;
        if (!stages.TryGetValue(kb, out st)) { st = new Stage { kb = kb }; stages[kb] = st; st.scored = -1; }
        if (st.scored != runSig) { st.scored = runSig; Score(st, RunOf(kb)); }
        return st;
    }

    /// <summary>The islands of <paramref name="kb"/>'s run (itself first when alone; left to right): keyboards with keyboards, v9 other kinds with
    /// other kinds.</summary>
    public static IReadOnlyList<KeyBlock> RunMembers(KeyBlock kb) { if (I == null) return new KeyBlock[0]; I.EnsureRuns(); return I.RunOf(kb); }

    // ---- runs: same lane, consecutive columns, the same voices
    readonly Dictionary<KeyBlock, List<KeyBlock>> runs = new Dictionary<KeyBlock, List<KeyBlock>>();
    int runSig = int.MinValue, runFrame = -1;

    List<KeyBlock> RunOf(KeyBlock kb)
    {
        List<KeyBlock> r;
        if (runs.TryGetValue(kb, out r)) return r;
        r = new List<KeyBlock> { kb }; runs[kb] = r;
        return r;
    }

    /// <summary>Rebuilds the runs when the keyboards, their places or their scores changed (at most once a frame).</summary>
    void EnsureRuns()
    {
        if (runFrame == Time.frameCount) return;
        runFrame = Time.frameCount;
        var sm = SongManager.I;
        if (sm == null) return;
        int sig;
        unchecked
        {
            sig = 17;
            foreach (var kb in sm.Islands)
            {
                if (!Stageable(kb)) continue;
                sig = sig * 31 + kb.GetInstanceID(); sig = sig * 31 + kb.column; sig = sig * 31 + Mathf.RoundToInt(kb.pz * 10f);
                if (kb.IsKeyboard) { var hs = KeyHands.Of(kb); sig = sig * 31 + (hs != null ? hs.ScoreSig : 0); continue; }   // v9 perf: the notes, not every re-read
                // v9: a fragment's parts are its play windows
                sig = sig * 31 + Mathf.RoundToInt(kb.startBeatOffset * 8f); sig = sig * 31 + Mathf.RoundToInt(kb.LengthBeats * 8f); sig = sig * 31 + kb.Passes;
                sig = sig * 31 + Mathf.RoundToInt(sm.PassLength(kb.column) * 8f); sig = sig * 31 + sm.ColumnPasses(kb.column);
            }
            // v9: the voices of the other kinds (KeyHands' rescores cover the keyboards')
            var cubes = SequenceMaster.Cubes;
            for (int i = 0; i < cubes.Count; i++)
            {
                var c = cubes[i];
                if (c == null || c.Island == null || c.Island.IsKeyboard) continue;
                sig = sig * 31 + c.Island.GetInstanceID(); sig = sig * 31 + c.instrument * 1000 + c.voice; sig = sig * 31 + c.layer;
                sig = sig * 31 + (c.Hologram ? 1 : 0) + (c.nodes.Count > 0 ? 2 : 0) + c.windows.Count * 4;
            }
            sig = sig * 31 + Mathf.RoundToInt(GlobalClock.BeatsPerBar);
        }
        if (sig == runSig) return;
        runSig = sig;
        RunRebuilds++;
        runs.Clear(); runParts.Clear();
        // the voices of every island in ONE pass over the cubes (was a pass per island per run member: heavy on a 190-island song)
        voices.Clear();
        var allCubes = SequenceMaster.Cubes;
        for (int i = 0; i < allCubes.Count; i++)
        {
            var c = allCubes[i];
            if (c == null || c.layer != 0 || c.Hologram || c.nodes.Count == 0) continue;
            int v = c.instrument * 1000 + c.voice;
            AddVoice(c.Island, v);
            foreach (var w in c.windows) if (w.island != null && w.island != c.Island && !w.silent) AddVoice(w.island, v);
        }
        var kbs = new List<KeyBlock>();
        foreach (var kb in sm.Islands) if (Stageable(kb)) kbs.Add(kb);
        kbs.Sort((x, y) => x.column != y.column ? x.column.CompareTo(y.column) : x.startBeatOffset != y.startBeatOffset ? x.startBeatOffset.CompareTo(y.startBeatOffset) : x.pz.CompareTo(y.pz));
        float gapBeats = PartGapBars * Mathf.Max(1, GlobalClock.BeatsPerBar);
        foreach (var kb in kbs)
        {
            KeyBlock prev = null;
            var vk = VoiceSet(kb);
            if (vk != null)
            {
                if (kb.IsKeyboard)
                {
                    // keyboards: the same voices in the same lane of the column before
                    foreach (var o in kbs)
                        if (o.IsKeyboard && o != kb && o.column == kb.column - 1 && Mathf.Abs(o.pz - kb.pz) < RunLaneTol && VoiceSet(o) != null && VoiceSet(o).SetEquals(vk)) { prev = o; break; }
                }
                else
                {
                    // v9 round 3: the lane's earlier island that shares a voice and ends less than PartGapBars before this one starts (the latest end)
                    float bestEnd = float.NegativeInfinity;
                    foreach (var o in kbs)
                    {
                        if (o == kb || o.IsKeyboard || Mathf.Abs(o.pz - kb.pz) >= RunLaneTol || o.startBeatOffset >= kb.startBeatOffset - 1e-3f) continue;
                        var vo = VoiceSet(o);
                        if (vo == null || !vo.Overlaps(vk)) continue;
                        float end = PlayEnd(o, sm);
                        if (end >= kb.startBeatOffset - gapBeats && end > bestEnd) { bestEnd = end; prev = o; }
                    }
                }
            }
            List<KeyBlock> r;
            if (prev != null && runs.TryGetValue(prev, out r)) r.Add(kb);
            else r = new List<KeyBlock>();
            if (r.Count == 0) r.Add(kb);
            runs[kb] = r;
        }
    }

    readonly Dictionary<KeyBlock, HashSet<int>> voices = new Dictionary<KeyBlock, HashSet<int>>();
    readonly Dictionary<List<KeyBlock>, KeyValuePair<List<Vector2>, bool>> runParts = new Dictionary<List<KeyBlock>, KeyValuePair<List<Vector2>, bool>>();
    void AddVoice(KeyBlock kb, int v)
    {
        if (!Stageable(kb)) return;
        HashSet<int> set;
        if (!voices.TryGetValue(kb, out set)) { set = new HashSet<int>(); voices[kb] = set; }
        set.Add(v);
    }
    /// <summary>The (instrument, voice) set of the cubes that play on <paramref name="kb"/> (octave layers apart); null = none.</summary>
    HashSet<int> VoiceSet(KeyBlock kb) { HashSet<int> set; return kb != null && voices.TryGetValue(kb, out set) && set.Count > 0 ? set : null; }

    /// <summary>The end of a non-keyboard's last play window (song beats).</summary>
    static float PlayEnd(KeyBlock m, SongManager sm)
    {
        float pl = sm.PassLength(m.column);
        if (pl <= 0f) return m.startBeatOffset + m.LengthBeats;
        int reps = Mathf.Max(1, Mathf.Min(m.Passes, sm.ColumnPasses(m.column)));
        return m.startBeatOffset + (reps - 1) * pl + Mathf.Min(pl, m.LengthBeats);
    }

    /// <summary>The parts of a run: every member's notes (wrapped copies apart; v9 a non-keyboard's play windows) in time order, grouped where they
    /// are less than PartGapBars apart.</summary>
    void Score(Stage st, List<KeyBlock> run)
    {
        st.parts.Clear();
        st.fragment = false;
        KeyValuePair<List<Vector2>, bool> cached;
        if (runParts.TryGetValue(run, out cached)) { st.parts.AddRange(cached.Key); st.fragment = cached.Value; return; }   // scored once per run
        var notes = new List<Vector2>();
        var sm = SongManager.I;
        foreach (var m in run)
        {
            if (!m.IsKeyboard)
            {
                // v9: the measures it sounds in each pass it plays (SongManager's windows: a grid plays its own measures, resting in the column's rest)
                if (sm == null || VoiceSet(m) == null) continue;
                if (IsFragment(m)) st.fragment = true;
                float pl = sm.PassLength(m.column);
                if (pl <= 0f) continue;
                int reps = Mathf.Min(m.Passes, sm.ColumnPasses(m.column));
                float own = Mathf.Min(pl, m.LengthBeats);
                for (int p = 0; p < reps; p++) notes.Add(new Vector2(m.startBeatOffset + p * pl, m.startBeatOffset + p * pl + own));
                continue;
            }
            var hs = KeyHands.Of(m);
            if (hs == null) continue;
            foreach (var h in hs.Hands)
                for (int i = 0; i < h.NoteCount; i++)
                {
                    float on, off; TileInteraction t; bool w;
                    if (h.NoteOf(i, out on, out off, out t, out w) && !w) notes.Add(new Vector2(on, off));
                }
        }
        notes.Sort((a, b) => a.x.CompareTo(b.x));
        float gap = PartGapBars * Mathf.Max(1, GlobalClock.BeatsPerBar);
        foreach (var n in notes)
        {
            int k = st.parts.Count - 1;
            if (k >= 0 && n.x - st.parts[k].y < gap) st.parts[k] = new Vector2(st.parts[k].x, Mathf.Max(st.parts[k].y, n.y));
            else st.parts.Add(n);
        }
        runParts[run] = new KeyValuePair<List<Vector2>, bool>(new List<Vector2>(st.parts), st.fragment);
    }

    static float Bps => (float)System.Math.Max(0.1, GlobalClock.BeatsPerSecond);
    static double LoopLen => GlobalClock.LoopEndBeat - GlobalClock.LoopStartBeat;

    /// <summary>True when the keyboard has notes and the time it is shown (each part from its pop to the end of its sink) leaves a gap in the loop.</summary>
    /// <summary>v9 perf: <see cref="GapNow"/> cached until the runs, the loop or the tempo change.</summary>
    bool HasGap(Stage st)
    {
        int sig;
        unchecked { sig = runSig * 31 + Mathf.RoundToInt((float)LoopLen * 8f); sig = sig * 31 + Mathf.RoundToInt((float)GlobalClock.LoopStartBeat * 8f); sig = sig * 31 + Mathf.RoundToInt(Bps * 100f); }
        if (st.gapSig != sig) { st.gapSig = sig; st.gap = GapNow(st); }
        return st.gap;
    }

    bool GapNow(Stage st)
    {
        if (st.parts.Count == 0 || (!st.kb.IsKeyboard && !st.fragment)) return false;   // v9: a run of full grids never pops
        double len = LoopLen;
        if (len <= 1e-3) return false;
        if (!st.kb.IsKeyboard)
        {
            // v9 round 3: a run that plays most of the loop stays up, and so does one whose only silence is shorter than PartGapBars (round the loop)
            double l0 = GlobalClock.LoopStartBeat, l1 = GlobalClock.LoopEndBeat, play = 0, gap = PartGapBars * Mathf.Max(1, GlobalClock.BeatsPerBar);
            foreach (var p in st.parts) play += System.Math.Max(0.0, System.Math.Min(p.y, l1) - System.Math.Max(p.x, l0));
            if (play >= DriveMaxCover * len) return false;
            if (st.parts.Count == 1 && (st.parts[0].x - l0) + (l1 - st.parts[0].y) < gap) return false;
        }
        float bps = Bps, pop = PopS * bps + LeadBeats, sink = TailBeats + SinkS * bps;
        double covered = 0, lastEnd = double.NegativeInfinity;
        foreach (var p in st.parts)
        {
            double a = p.x - pop, b = p.y + sink;
            if (a < lastEnd) a = lastEnd;
            if (b > a) covered += b - a;
            lastEnd = System.Math.Max(lastEnd, b);
        }
        return covered < len - 0.25;
    }

    /// <summary>The pure depth at <paramref name="beat"/>: the most-up of every part (and its copies one loop earlier / later).</summary>
    float Depth(Stage st, double beat)
    {
        float bps = Bps, popB = PopS * bps, sinkB = SinkS * bps;
        double len = LoopLen; bool loop = (GlobalClock.LoopSong || GlobalClock.HasRegion) && len > 1e-3;
        float best = 1f;
        foreach (var p in st.parts)
            for (int w = loop ? -1 : 0; w <= (loop ? 1 : 0); w++)
            {
                double s = p.x + w * len, e = p.y + w * len;
                double popStart = s - LeadBeats - popB, popEnd = s - LeadBeats, sinkStart = e + TailBeats, sinkEnd = sinkStart + sinkB;
                float d;
                if (beat < popStart || beat >= sinkEnd) d = 1f;
                else if (beat < popEnd) d = 1f - Ease.OutBack((float)((beat - popStart) / popB), KeyBlock.PopOvershoot);   // up past its place, settling
                else if (beat < sinkStart) d = 0f;
                else
                {
                    float u = (float)((beat - sinkStart) / sinkB);
                    d = u < 0.25f ? -0.035f * Mathf.Sin(u / 0.25f * Mathf.PI * 0.5f) : Mathf.Lerp(-0.035f, 1f, Ease.InCubic((u - 0.25f) / 0.75f));   // a bob, then down
                }
                if (d < best) best = d;
            }
        return best;
    }

    static readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
    /// <summary>v9 perf: Stopwatch ticks spent in the stage's LateUpdate since Play (tests sample its change per frame).</summary>
    public static long Ticks => watch.ElapsedTicks;

    void LateUpdate()
    {
        watch.Start();
        try { StepAll(); } finally { watch.Stop(); }
    }

    void StepAll()
    {
        var sm = SongManager.I;
        if (sm == null) return;
        bool playing = GlobalClock.IsPlaying;
        double beat = GlobalClock.SongBeatD;
        bool jumped = playing != wasPlaying || (playing && (beat < lastBeat - 0.5 && !(beat <= GlobalClock.LoopStartBeat + 0.5) || beat - lastBeat > 1.5));
        wasPlaying = playing; lastBeat = beat;
        float dt = Time.unscaledDeltaTime;
        foreach (var kb in sm.Islands)
        {
            if (!Stageable(kb)) continue;
            var st = StageOf(kb);
            float target = playing && Enabled && HasGap(st) ? Depth(st, beat) : 0f;
            if (jumped && Mathf.Abs(target - st.shown) > 0.05f) { st.from = st.shown; st.easeT = 0f; }   // a play / stop / seek: ease there
            float shown = target;
            if (st.easeT < 1f) { st.easeT = Mathf.Min(1f, st.easeT + dt / EaseS); shown = Mathf.Lerp(st.from, target, Ease.InOutCubic(st.easeT)); }
            // the splash as it breaks the surface (up) / the ripple as it goes under
            bool up = shown < 0.6f;
            if (up != st.up && playing && st.easeT >= 1f)
            {
                if (up) Pops++; else Sinks++;   // the splash, the waves, the foam and the bubbles: KeyBlock.UpdateWater (on the section's floor)
            }
            st.up = up;
            st.shown = shown;
            kb.PartDepth = shown;
        }
        // forget keyboards that are gone (a rebuild re-creates every island)
        gone.Clear();
        foreach (var k in stages.Keys) if (k == null || sm.Islands.IndexOf(k) < 0) gone.Add(k);
        foreach (var k in gone) stages.Remove(k);
    }
}
