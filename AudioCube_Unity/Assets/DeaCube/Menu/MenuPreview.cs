using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SPEC v5 §4 (package T): a gallery song's hook, heard from the title menu's shelf. <see cref="Build"/> reads a <see cref="SongState"/> the
/// way the world plays it — columns (SongManager.NormalizeColumns' rules, bars equalised per column, SPEC §2.5 repeat passes: an island plays
/// passes 0 .. repeat − 1 of its column, the path restarting each pass), each island's tile pitches (KeyBlock's table: root + inversion_x[row]
/// + 12 × register over the mood / climate view), every cube's path (the durations engine: per-node lengths, loop / ping-pong / once, reverse,
/// phase, the swing warp; or the legacy step engine with its necklace), the stickers (rest, ghost, accent, ratchets, coin, tie, lift), the
/// octave, VoiceRules' fold, velocity and gate policy, pad tones, followers, echoes, Moons' drum pieces under the section's energy —
/// and flattens a few bars (from the entry's hook bar: 4-8 bars, at most ~16 s) into a note list in beats. <see cref="Play"/> loops that list
/// through the Synth facade under its own owner (<see cref="Owner"/>), scheduling ≤ <see cref="Horizon"/> s ahead with a lead (a note whose
/// time has already passed after a hitch is dropped, never sent late), with the gallery song's own mix on the slots; <see cref="Stop"/>
/// cancels the owner (queued notes dropped, sounding ones released) and gives the loaded song its mix back. Main thread only.
/// A preview, not the engine: stacks, the bass anchor and fills are left out (the world plays the song itself once it is opened).
/// </summary>
public class MenuPreview
{
    /// <summary>The preview's Synth owner (far from the cubes' instance-id range and MenuMusic's 1900000021..23).</summary>
    public const int Owner = 1900000031;
    public const double Lead = 0.12, Horizon = 0.6, MinLead = 0.03;
    public const int MaxNotes = 2400;
    const int TPB = ProjectConfig.TicksPerBeat;
    const int SlotPluck = 1, SlotPad = 2, SlotLead = 3, SlotBass = 4, SlotBells = 5, SlotPiano = 8;

    /// <summary>One note of a preview, in beats from the preview's start.</summary>
    public struct Note { public int slot, midi, vel; public double on, off;
        /// <summary>v9 (G): the cube that plays it (its index in the song): a mono voice releases at ITS cube's next note, as the engine's owners do.</summary>
        public int cube; }

    /// <summary>A built preview: the notes of <see cref="bars"/> bars from <see cref="startBar"/>, looped by the player.</summary>
    public class Track
    {
        public string id = "";
        public float bpm = 100f;
        public int beatsPerBar = 4;
        public int startBar, bars, songBars;
        public double lengthBeats;
        public readonly List<Note> notes = new List<Note>();
        /// <summary>The chord sounding at the preview's start (MIDI, root position): the leave's impact plays it.</summary>
        public int[] chord;
        /// <summary>The song's mix per instrument role (applied to the synth slots while the preview plays).</summary>
        public readonly float[] gain = new float[Instruments.Count];
        public readonly bool[] muted = new bool[Instruments.Count];
        public float tone = 1f, space = 1f;
        public double Seconds => lengthBeats * 60.0 / Mathf.Max(1f, bpm);
        /// <summary>Notes on <paramref name="slot"/> (tests).</summary>
        public int CountOn(int slot) { int k = 0; foreach (var n in notes) if (n.slot == slot) k++; return k; }
    }

    // ------------------------------------------------------------------ building
    class Isl { public int reg, rows, cols, energy; public bool sleep, moon; public int[,] midi; public int root; public List<int> semis;
        /// <summary>v9 (G): a keyboard (kind 2: its keys play as melodies, VoiceRules.FoldMelody); a Moon's kit, groove bars and start column.</summary>
        public bool keys; public int[] kit; public int bars = 1, startCol; }
    struct Win { public double start, len; public Isl isl; public int pass; public bool moon; }
    struct Step { public double at, len; public int node; }

    static readonly List<Win> wins = new List<Win>(16);
    static readonly List<Step> steps = new List<Step>(64);

    /// <summary>The hook of <paramref name="st"/> from bar <paramref name="hookBar"/>: at least <paramref name="minBars"/> bars (when the song has
    /// them), at most <paramref name="maxBars"/> and ~<paramref name="maxSeconds"/> s. Null when the state has no islands.</summary>
    public static Track Build(SongState st, int hookBar = 0, int minBars = 4, int maxBars = 8, float maxSeconds = 16f)
    {
        if (st == null || st.measures == null || st.measures.Length == 0) return null;
        var tr = new Track();
        int bpb = Mathf.Clamp(st.beatsPerBar <= 0 ? 4 : st.beatsPerBar, 1, 16);
        float bpm = st.bpm > 1f ? Mathf.Clamp(st.bpm, 20f, 400f) : 100f;
        tr.bpm = bpm; tr.beatsPerBar = bpb;
        double spb = 60.0 / bpm;
        int climate = Mathf.Clamp(st.climate, 0, 3);

        // the islands (a null entry is the C major island NormalizeColumns would put there)
        int n = st.measures.Length;
        var ms = new MeasureState[n];
        for (int i = 0; i < n; i++) ms[i] = st.measures[i] ?? new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7 }, bars = 1 };
        var isl = new Isl[n];
        for (int i = 0; i < n; i++) isl[i] = ms[i].kind == 2 ? KeyboardIsland(ms[i]) : ChordIsland(ms[i], climate);   // v9 (G): keyboards too
        int nm = st.moons != null ? st.moons.Length : 0;
        var moons = new Isl[nm];
        for (int i = 0; i < nm; i++) moons[i] = MoonIsland(st.moons[i]);

        // columns: start, pass length, passes, anchor
        var colOf = ColumnsOf(ms);
        int C = 0; foreach (int c in colOf) C = Math.Max(C, c + 1);
        var colBars = new int[C]; var passes = new int[C]; var anchor = new int[C];
        for (int c = 0; c < C; c++) { colBars[c] = 1; passes[c] = 1; anchor[c] = -1; }
        for (int i = 0; i < n; i++)
        {
            int c = colOf[i];
            colBars[c] = Math.Max(colBars[c], Math.Min(4, OffsetBars(ms[i]) + Mathf.Clamp(ms[i].bars, 1, 4)));   // v9 (G): a column lasts to its longest grid's END (v8 barOffset)
            passes[c] = Math.Max(passes[c], Mathf.Clamp(ms[i].repeat, 1, 4));
            if (anchor[c] < 0) anchor[c] = i;
        }
        var colStart = new double[C + 1]; var passLen = new double[C];
        for (int c = 0; c < C; c++) { passLen[c] = colBars[c] * bpb; colStart[c + 1] = colStart[c] + passLen[c] * passes[c]; }
        double total = colStart[C];
        int songBars = Math.Max(1, (int)Math.Round(total / bpb));
        tr.songBars = songBars;

        // the span: whole bars from the hook
        int want = Math.Min(minBars, songBars);
        int startBar = Mathf.Clamp(hookBar, 0, songBars - 1);
        if (songBars - startBar < want) startBar = songBars - want;
        int bars = Math.Min(maxBars, songBars - startBar);
        double secPerBar = bpb * spb;
        while (bars > want && bars * secPerBar > maxSeconds + 1e-6) bars--;
        bars = Math.Max(1, bars);
        double s0 = startBar * (double)bpb, s1 = Math.Min(total, s0 + bars * (double)bpb);
        tr.startBar = startBar; tr.bars = bars; tr.lengthBeats = s1 - s0;

        // the chord sounding at the start (the anchor of the column there)
        int c0 = ColumnAt(colStart, C, s0);
        var a0 = anchor[c0] >= 0 ? isl[anchor[c0]] : isl[0];
        tr.chord = new int[a0.semis.Count];
        for (int k = 0; k < a0.semis.Count; k++) tr.chord[k] = a0.root + a0.semis[k];

        // the song's mix
        for (int i = 0; i < Instruments.Count; i++)
        {
            tr.gain[i] = st.instVolume != null && i < st.instVolume.Length ? Mathf.Clamp01(st.instVolume[i]) : 1f;
            tr.muted[i] = st.instMuted != null && i < st.instMuted.Length && st.instMuted[i];
        }
        tr.tone = st.tone <= 0f ? 1f : Mathf.Clamp(st.tone, 0.15f, 1f);
        tr.space = Mathf.Clamp(st.space, 0f, 1.6f);

        // every cube's notes inside the span
        var key = Nudge.KeyOf(st);   // v9 (N)
        var ctx = new Ctx { st = st, spb = spb, bpb = bpb, s0 = s0, s1 = s1, swing = Mathf.Clamp(st.swing, 0f, 1f) / 3f, transpose = Mathf.Clamp(st.transpose, -12, 12), colStart = colStart, anchor = anchor, isl = isl, C = C, notes = tr.notes,
                            keyTonic = key.tonic, keyMinor = key.minor };
        int cubeIndex = -1;
        if (st.cubes != null)
            foreach (var c in st.cubes)
            {
                cubeIndex++;
                if (c == null || c.muted || c.xs == null || c.zs == null) continue;
                int nn = Math.Min(c.xs.Length, c.zs.Length);
                if (nn == 0) continue;
                int inst = Mathf.Clamp(c.instrument, 0, Instruments.Count - 1);
                if (tr.muted[inst]) continue;
                wins.Clear();
                if (c.moon >= 0)
                {
                    if (c.moon >= nm) continue;
                    // a Moon cube plays its Moon's SECTION — its start column to the next later Moon start, or the song's end (v6 §11) — in windows of
                    // the Moon's bars (v9: a groove may run several bars), its path restarting each window (SongManager.RecomputeMeasureStarts); one
                    // Moon at column 0 with one bar = a window every bar, the whole song long
                    var mo = moons[c.moon];
                    int sc = Mathf.Clamp(mo.startCol, 0, C - 1);
                    double ms0 = colStart[sc], ms1 = total;
                    for (int j = 0; j < nm; j++) { int sj = Mathf.Clamp(moons[j].startCol, 0, C - 1); if (sj > sc) ms1 = Math.Min(ms1, colStart[sj]); }
                    double wl = Math.Max(1, mo.bars) * (double)bpb;
                    for (double ws = ms0; ws < ms1 - 1e-9; ws += wl)
                    {
                        double wlen = Math.Min(wl, ms1 - ws);
                        if (ws + wlen <= s0 + 1e-9 || ws >= s1 - 1e-9) continue;
                        wins.Add(new Win { start = ws, len = wlen, isl = mo, pass = (int)Math.Round(ws / bpb), moon = true });
                    }
                }
                else
                {
                    if (c.measure < 0 || c.measure >= n) continue;
                    int home = c.measure;
                    AddPasses(ctx, colOf[home], home, passLen[colOf[home]]);   // v7 §21: a cube stays on its grid (a rider no longer plays the other columns)
                }
                ctx.cube = cubeIndex;
                foreach (var w in wins) RunWindow(ctx, c, nn, inst, w);
                if (tr.notes.Count >= MaxNotes) break;
            }
        Tidy(tr, spb);
        return tr;
    }

    class Ctx
    {
        public SongState st; public double spb, s0, s1; public int bpb, transpose, C, cube; public float swing;
        public double[] colStart; public int[] anchor; public Isl[] isl; public List<Note> notes;
        /// <summary>v9 (N): the song's key (Nudge.KeyOf: keyTonic / keyMinor, else the first island's chord) — a nudged step's scale steps.</summary>
        public int keyTonic; public bool keyMinor;
    }

    /// <summary>Windows of island <paramref name="i"/> in column <paramref name="col"/>: one per pass it plays (SPEC §2.5), sleeping islands none.
    /// v9 (G): as SongManager.RecomputeMeasureStarts builds them — a chord grid shorter than its column plays its own measures from its barOffset
    /// (v8 per-grid measures: start = the column's start + pass × the pass length + barOffset bars, length = min(the pass, its own bars)); a grid
    /// as long as its column, at offset 0 (every v5 song), keeps the whole pass exactly as before.</summary>
    static void AddPasses(Ctx x, int col, int i, double passLength)
    {
        if (i < 0) return;
        var s = x.isl[i];
        if (s.sleep) return;
        var m = x.st.measures[i];
        if (m != null && m.kind == 3) return;   // v9 (G): a stairs run is SongManager's (StairPitches / StairPath): the preview leaves it out
        int reps = Mathf.Clamp(m != null ? m.repeat : 1, 1, 4);
        double off = OffsetBars(m) * (double)x.bpb;
        double own = m != null && Offsettable(m) ? Math.Min(passLength, Mathf.Clamp(m.bars, 1, 4) * (double)x.bpb) : passLength;
        for (int p = 0; p < reps; p++)
        {
            double a = x.colStart[col] + p * passLength + off;
            if (a + own <= x.s0 || a >= x.s1) continue;
            wins.Add(new Win { start = a, len = own, isl = s, pass = p });
        }
    }

    /// <summary>v9 (G): SongManager.OffsetBars on a MeasureState: a chord grid's, keyboard's or stairs' first measure inside its column (0..3); a Moon or
    /// a phrase starts at the column start.</summary>
    static int OffsetBars(MeasureState m) => m != null && Offsettable(m) ? Mathf.Clamp(m.barOffset, 0, 3) : 0;
    static bool Offsettable(MeasureState m) => m.kind == 0 || m.kind == 2 || m.kind == 3;

    static int ColumnAt(double[] colStart, int C, double beat)
    {
        for (int c = 0; c < C; c++) if (beat < colStart[c + 1] - 1e-9) return c;
        return Math.Max(0, C - 1);
    }

    /// <summary>SongManager.NormalizeColumns' column rules on a copy: a file without columns = one column per island in order; an island without
    /// one gets a new column right after the previous island's; then contiguous 0..C-1.</summary>
    public static int[] ColumnsOf(MeasureState[] ms)
    {
        int n = ms.Length;
        var col = new int[n];
        bool legacy = true;
        for (int i = 0; i < n; i++) if (ms[i].col >= 0) { legacy = false; break; }
        if (legacy) { for (int i = 0; i < n; i++) col[i] = i; return col; }
        for (int i = 0; i < n; i++) col[i] = ms[i].col;
        for (int i = 0; i < n; i++)
        {
            if (col[i] >= 0) continue;
            int c = i > 0 ? col[i - 1] + 1 : 0;
            for (int j = 0; j < n; j++) if (j != i && col[j] >= c) col[j]++;
            col[i] = c;
        }
        var keys = new List<int>();
        foreach (int c in col) if (!keys.Contains(c)) keys.Add(c);
        keys.Sort();
        for (int i = 0; i < n; i++) col[i] = keys.IndexOf(col[i]);
        return col;
    }

    /// <summary>KeyBlock.BuildFromData + Build's pitch table for a chord island.</summary>
    static Isl ChordIsland(MeasureState m, int climate)
    {
        var s = new Isl { reg = Mathf.Clamp(m.reg, -2, 2), energy = Mathf.Clamp(m.energy, 0, 3), sleep = m.sleep };
        int root = m.root;
        if (root < 36) root += 24;
        if (root > 84) root -= 12;
        s.root = root;
        var stored = new List<int>(m.semis != null && m.semis.Length > 0 ? m.semis : new[] { 0, 4, 7 });
        if (!stored.Contains(0)) stored.Insert(0, 0);
        stored.Sort();
        var eff = new List<int>(MusicTheory.EffectiveSemis(stored.ToArray(), Mathf.Clamp(m.mood, 0, 3), climate));
        if (!eff.Contains(0)) eff.Insert(0, 0);
        eff.Sort();
        s.semis = eff;
        s.cols = ProjectConfig.numInversions; s.rows = eff.Count;
        s.midi = new int[s.cols, s.rows];
        var inv = new List<int>(eff);
        for (int x = 0; x < s.cols; x++)
        {
            if (x > 0) { int low = inv[0]; inv.RemoveAt(0); inv.Add(low + 12); inv.Sort(); }
            for (int z = 0; z < s.rows; z++) s.midi[x, z] = root + inv[z] + 12 * s.reg;
        }
        return s;
    }

    static Isl MoonIsland(MeasureState m)
    {
        var s = new Isl { moon = true, cols = ProjectConfig.MoonCols, rows = ProjectConfig.MoonRows, energy = 2, root = 36, semis = new List<int> { 0 } };
        s.midi = new int[s.cols, s.rows];
        for (int x = 0; x < s.cols; x++) for (int z = 0; z < s.rows; z++) s.midi[x, z] = SynthBank.DrumForRow(z, s.rows);
        // v9 (G): the Moon's own kit, its groove's bars (KeyBlock.BuildFromData: 1..MaxMoonBars) and its start column (a file's -1 = column 0)
        if (m != null)
        {
            s.kit = m.kit != null && m.kit.Length > 0 ? m.kit : null;
            s.bars = Mathf.Clamp(m.bars <= 0 ? 1 : m.bars, 1, SongManager.MaxMoonBars);
            s.startCol = Math.Max(0, m.col);
        }
        return s;
    }

    /// <summary>v9 (G): KeyBlock.BuildFromData + Build's key table for a KEYBOARD (kind 2): keyCount keys (25 when unset; 13..61) from its lowest key
    /// (the root, kept inside the piano A0..C8 as BuildFromData keeps it), key k = root + k + 12 × register; one row.</summary>
    static Isl KeyboardIsland(MeasureState m)
    {
        var s = new Isl { keys = true, reg = Mathf.Clamp(m.reg, -2, 2), energy = Mathf.Clamp(m.energy, 0, 3), sleep = m.sleep, semis = new List<int> { 0 } };
        int count = m.keyCount <= 0 ? ProjectConfig.KeyboardKeys : Mathf.Clamp(m.keyCount, ProjectConfig.KeyboardMinKeys, ProjectConfig.KeyboardMaxKeys);
        int root = m.root > 0 ? m.root : SongManager.KeyboardLowestKey();
        while (root < 21) root += 12;
        while (root + count - 1 > 108) root -= 12;
        s.root = root; s.cols = count; s.rows = 1;
        s.midi = new int[s.cols, 1];
        for (int x = 0; x < s.cols; x++) s.midi[x, 0] = root + x + 12 * s.reg;
        return s;
    }

    static int NodeForHit(int h, int n, int mode, bool reverse)
    {
        if (n == 0 || h < 0) return -1;
        int idx;
        switch (mode)
        {
            case 1: if (n == 1) idx = 0; else { int period = 2 * n - 2, m = h % period; idx = m < n ? m : period - m; } break;   // ping-pong
            case 2: idx = h < n ? h : -1; break;                                                                                   // once
            default: idx = h % n; break;
        }
        if (idx < 0) return -1;
        return reverse ? n - 1 - idx : idx;
    }

    static float StepBeats(int step)
    {
        switch (step) { case 0: return 2f; case 2: return 0.5f; case 3: return 0.25f; case 4: return 1f / 3f; default: return 1f; }
    }

    /// <summary>The path's steps inside one window (local beats): AudioCube's durations engine, or the legacy uniform steps + necklace.</summary>
    static void PathSteps(CubeState c, int nn, double len, float swing, int bpb)
    {
        steps.Clear();
        int mode = Mathf.Clamp(c.mode, 0, 2), ph = Math.Max(0, c.phase);
        bool durEngine = c.durs != null && c.durs.Length == nn;
        if (durEngine)
        {
            int P = mode == 1 ? Math.Max(1, 2 * nn - 2) : (mode == 2 ? Math.Max(0, nn - ph) : nn);
            if (P == 0) return;
            var node = new int[P]; var start = new long[P + 1];
            long t = 0;
            for (int r = 0; r < P; r++)
            {
                int nd = Mathf.Clamp(NodeForHit(r + ph, nn, mode, c.reverse), 0, nn - 1);
                node[r] = nd; start[r] = t;
                t += Math.Max(1, AudioCube.ClampTicks(c.durs[nd]));
            }
            start[P] = t;
            long cycle = t;
            for (int k = 0; k < 4096; k++)
            {
                if (mode == 2 && k >= P) break;
                long q = k / P; int r = k % P;
                long raw = q * cycle + start[r], rawNext = q * cycle + start[r + 1];
                double at = AudioCube.Warp(raw / (float)TPB, swing);
                if (at >= len - 1e-4) break;
                double next = AudioCube.Warp(rawNext / (float)TPB, swing);
                steps.Add(new Step { at = at, len = next - at, node = node[r] });
            }
            return;
        }
        // legacy: uniform steps (8th pairs swung), the necklace's hits advance the path
        float sb = StepBeats(c.step);
        float swingK = sb <= 0.5f && c.step != 4 ? swing : 0f;   // AudioCube.SwingK: 8ths / 16ths only, never triplets
        int perBar = Mathf.Clamp(Mathf.RoundToInt(bpb / sb), 1, 24);
        var pat = Rhythm.Pattern(c.hits, c.rot, c.mask, perBar);
        int hitIdx = -1;
        for (int k = 0; k < 4096; k++)
        {
            int pair = k / 2; double b = pair * 2.0 * sb;
            double at = (k % 2 == 0) ? b : b + (1.0 + swingK) * sb;
            if (at >= len - 1e-4) break;
            if (!pat[k % pat.Length]) continue;
            hitIdx++;
            int nd = NodeForHit(hitIdx + ph, nn, mode, c.reverse);
            if (nd < 0) break;
            steps.Add(new Step { at = at, len = sb, node = nd });
        }
    }

    static void RunWindow(Ctx x, CubeState c, int nn, int inst, Win w)
    {
        int slot = Instruments.SlotOf(inst, c.voice);   // v6: the cube's own voice; the role rules below follow its GROUP
        int grp = SynthBank.GroupOf(slot);
        bool drums = SynthBank.Def(slot).drums;
        PathSteps(c, nn, w.len, x.swing, x.bpb);
        if (steps.Count == 0) return;
        var mods = c.ModsOrDerived();
        int seed = c.SeedOrDerived();
        double spb = x.spb, wEnd = w.start + w.len;
        double eps = 0.005 / spb, sixteenth = 0.25;
        float volume = Mathf.Clamp01(c.volume);
        // which steps fire (for ties: the next firing step's start)
        var fires = new bool[steps.Count];
        for (int k = 0; k < steps.Count; k++)
        {
            int nd = steps[k].node, mod = nd < mods.Length ? mods[nd] : 0;
            bool audible = mod != 1;
            if (mod == 6 && (Rhythm.Hash(w.pass, nd, seed) & 1u) != 0u) audible = false;
            fires[k] = audible;
        }
        for (int k = 0; k < steps.Count; k++)
        {
            if (!fires[k]) continue;
            var s = steps[k];
            double on = w.start + s.at;
            if (on < x.s0 - 1e-9 || on >= x.s1 - 1e-9) continue;
            int nd = s.node, mod = nd < mods.Length ? mods[nd] : 0;
            int weight = mod == 2 ? 0 : (mod == 3 ? 2 : 1), ratchet = mod == 4 ? 2 : (mod == 5 ? 3 : 1);
            bool tie = mod == 7, lift = mod == 8;
            var isl = w.isl;
            int gx = Mathf.Clamp(c.xs[nd], 0, isl.cols - 1), gz = Mathf.Clamp(c.zs[nd], 0, isl.rows - 1);
            // the section (a Moon cube: the anchor of the column playing at that beat)
            int energy = isl.energy;
            if (w.moon) { int col = ColumnAt(x.colStart, x.C, on); int a = x.anchor[col]; energy = a >= 0 ? x.isl[a].energy : 2; }
            if (w.moon && drums)
            {
                bool offBeat = Math.Abs(s.at - Math.Round(s.at)) > 1e-3;
                int mw = Rhythm.MoonWeight(energy, gz, offBeat);
                if (mw < 0) continue;
                if (mw == 0) weight = 0; else if (mw == 2) ratchet = Math.Max(ratchet, 2);
            }
            double rounded = Math.Round(s.at);
            bool onBeat = Math.Abs(s.at - rounded) < 1e-3;
            bool downbeat = onBeat && (((int)rounded % x.bpb) + x.bpb) % x.bpb == 0;
            float accent01 = (onBeat ? (downbeat ? 0.30f : 0.12f) : 0f) + (weight == 2 ? 0.35f : 0f);
            float es = Rhythm.EnergyScale(energy), ghost = weight == 0 ? 0.45f : 1f;
            double sub = s.len / ratchet;
            if (drums)
            {
                // v9 (G): a keyboard's key picks the piece (VoiceRules.DrumPieceOf); a Moon plays its own kit
                int piece = isl.keys ? SynthBank.DrumKit[((gx % SynthBank.DrumKit.Length) + SynthBank.DrumKit.Length) % SynthBank.DrumKit.Length] : VoiceRules.DrumPiece(gx, gz, isl.rows, isl.kit);
                bool kick = isl.keys ? piece == 36 : gz == 0;
                float col01 = isl.keys ? 0.82f : 0.55f + 0.09f * Mathf.Clamp(gx, 0, 5);
                int v0 = kick ? 100 + (weight == 2 ? 10 : 0) : SynthBank.Velocity(slot, col01 * volume * es * ghost, accent01);
                for (int j = 0; j < ratchet; j++)
                {
                    double oj = on + j * sub;
                    if (oj >= wEnd - 0.02 / spb) break;
                    Add(x, slot, piece, Mathf.RoundToInt(v0 * RatchetVel(j)), oj, Math.Min(oj + 0.1 / spb, wEnd - eps));
                }
                continue;
            }
            if (energy == 0 && VoiceRules.IsSustaining(slot)) continue;       // hush silences the pads
            // v9 (N): the step's nudge — scale steps of the song key above / below its tile (VoiceRules.Resolve rule 2); a nudged note plays alone
            int nz = c.nudges != null && c.nudges.Length == nn && nd < c.nudges.Length ? c.nudges[nd] : 0;
            int ns = nz != 0 ? Nudge.Semis(isl.midi[gx, gz], nz, x.keyTonic, x.keyMinor) : 0;
            int midi = isl.midi[gx, gz] + ns + x.transpose + 12 * c.octave + (lift ? 12 : 0);
            midi = isl.keys ? VoiceRules.FoldMelody(slot, midi, isl.reg) : VoiceRules.Fold(slot, midi, isl.reg);   // v9 (G): a key plays its own pitch
            midi = VoiceRules.Layered(midi, c.layer);   // v7: an octave layer sounds 12 x layer from its fold
            int vel = Mathf.Clamp(SynthBank.Velocity(slot, volume * es * ghost, accent01), 1, 127);
            // gate (VoiceRules.GateSeconds), in beats
            int g = Mathf.Clamp(c.gate, 0, 2);
            bool sustain = VoiceRules.IsSustaining(slot);
            if (sustain && g == 0) g = 1;
            if (grp == SlotBells && g == 2) g = 1;
            double gate;
            if (tie || g == 2)
            {
                double next = double.PositiveInfinity;
                for (int j = k + 1; j < steps.Count; j++) if (fires[j]) { next = w.start + steps[j].at; break; }
                gate = double.IsInfinity(next) ? wEnd - on : Math.Max(0.03 / spb, next - 0.01 / spb - on);
            }
            else gate = (g == 0 ? 0.3 : 0.92) * s.len;
            if (grp == SlotBass) gate = Math.Max(gate, 0.12 / spb);
            if (sustain) gate = Math.Max(gate, 1.0);
            if (grp == SlotPluck || grp == SlotBells || grp == SlotPiano) gate = Math.Min(gate, 2.0 * s.len);
            for (int j = 0; j < ratchet; j++)
            {
                double oj = on + j * sub;
                if (oj >= wEnd - 0.02 / spb) break;
                double gj = j < ratchet - 1 ? Math.Min(gate, sub - eps) : gate;
                double off = ClampOff(sustain, oj, oj + gj, wEnd, eps, sixteenth, spb);
                int vj = Mathf.Clamp(Mathf.RoundToInt(vel * RatchetVel(j)), 1, 127);
                Add(x, slot, midi, vj, oj, off);
                if (grp == SlotPad && !isl.moon && !isl.keys && ns == 0) PadTones(x, c, isl, slot, gx, gz, lift, vj, oj, off);   // never on a keyboard (VoiceRules); v9 (N): nor around a nudged note
                if (c.follow != 0 && !isl.moon && !isl.keys && ns == 0)   // v9: a keyboard has one row: no shadow tile (VoiceRules.ShadowTile); N: a nudged note plays alone
                {
                    int vs = Mathf.Clamp(Mathf.RoundToInt(vj * 0.6f), 1, 127);
                    if (c.follow == 1 || c.follow == 3) Follower(x, c, isl, slot, gx, gz, true, lift, vs, oj, off);
                    if (c.follow == 2 || c.follow == 3) Follower(x, c, isl, slot, gx, gz, false, lift, vs, oj, off);
                }
            }
            // echo: repeats on the step grid, −35 % each, dropped past the window end
            int echoes = Mathf.Clamp(c.echo, 0, 2);
            float ev = vel;
            double eg = Math.Min(gate, 0.92 * s.len);
            for (int i = 1; i <= echoes; i++)
            {
                double oe = on + i * s.len;
                if (oe >= wEnd - 0.02 / spb) break;
                ev *= 0.65f;
                Add(x, slot, midi, Mathf.Clamp(Mathf.RoundToInt(ev), 1, 127), oe, ClampOff(sustain, oe, oe + eg, wEnd, eps, sixteenth, spb));
            }
        }
    }

    static float RatchetVel(int j) => j == 0 ? 1f : (j == 1 ? 0.82f : 0.68f);

    static double ClampOff(bool sustain, double on, double off, double wEnd, double eps, double sixteenth, double spb)
    {
        double cap = wEnd - eps;
        if (sustain) cap = Math.Min(cap, wEnd - sixteenth);
        off = Math.Min(off, cap);
        off = Math.Max(off, on + 0.03 / spb);
        off = Math.Min(off, wEnd - eps);
        if (off <= on) off = on + 0.02 / spb;
        return off;
    }

    /// <summary>VoiceRules.PadTones: the two chord tones below in the same column (wrapping an octave down), softer.</summary>
    static void PadTones(Ctx x, CubeState c, Isl isl, int slot, int gx, int gz, bool lift, int vel, double on, double off)
    {
        if (isl.rows < 2) return;
        int v = Mathf.Clamp(Mathf.RoundToInt(vel * 0.65f), 1, 127);
        int z = gz, o12 = 0;
        for (int k = 0; k < 2 && k < isl.rows - 1; k++)
        {
            z--;
            if (z < 0) { z += isl.rows; o12 -= 12; }
            int m = isl.midi[gx, z] + o12 + x.transpose + 12 * c.octave + (lift ? 12 : 0);
            Add(x, slot, VoiceRules.Fold(slot, m, isl.reg), v, on, off);
        }
    }

    /// <summary>A follower (VoiceRules.ShadowTile): the nearest chord-tone row above / below in the same column at least 3 semitones away.</summary>
    static void Follower(Ctx x, CubeState c, Isl isl, int slot, int gx, int gz, bool up, bool lift, int vel, double on, double off)
    {
        int basePitch = isl.midi[gx, gz];
        for (int d = 1; d <= isl.rows; d++)
        {
            int z = up ? gz + d : gz - d, o = 0;
            while (z >= isl.rows) { z -= isl.rows; o += 12; }
            while (z < 0) { z += isl.rows; o -= 12; }
            int p = isl.midi[gx, z] + o;
            if (Math.Abs(p - basePitch) < 3) continue;
            int m = p + x.transpose + 12 * c.octave + (lift ? 12 : 0);
            Add(x, slot, VoiceRules.Fold(slot, m, isl.reg), vel, on, off);
            return;
        }
    }

    static void Add(Ctx x, int slot, int midi, int vel, double on, double off)
    {
        if (x.notes.Count >= MaxNotes) return;
        x.notes.Add(new Note { slot = slot, midi = Mathf.Clamp(midi, 0, 127), vel = Mathf.Clamp(vel, 1, 127), on = on - x.s0, off = off - x.s0, cube = x.cube });
    }

    /// <summary>Sorted by onset; a note never outlives the loop, a mono slot (lead, bass) releases at its next note, and two notes of one key
    /// never overlap (a note-off would cut the later one short).</summary>
    static void Tidy(Track tr, double spb)
    {
        var l = tr.notes;
        for (int i = 0; i < l.Count; i++) { var e = l[i]; e.off = Math.Min(e.off, tr.lengthBeats - 0.01); if (e.off <= e.on) e.off = e.on + 0.02 / spb; l[i] = e; }
        var sorted = new List<Note>(l);
        sorted.Sort((a, b) => a.on != b.on ? a.on.CompareTo(b.on) : (a.slot != b.slot ? a.slot.CompareTo(b.slot) : a.midi.CompareTo(b.midi)));
        var lastOfKey = new Dictionary<int, int>(); var lastOfMono = new Dictionary<long, int>();
        for (int i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            int key = e.slot * 128 + e.midi, j;
            if (lastOfKey.TryGetValue(key, out j))
            {
                var p = sorted[j];
                if (p.off > e.on - 0.005) { p.off = Math.Max(p.on + 0.02 / spb, e.on - 0.01); sorted[j] = p; }
                if (Math.Abs(p.on - e.on) < 1e-6) { sorted.RemoveAt(i); i--; continue; }   // the same key twice at once: one note
            }
            lastOfKey[key] = i;
            int eg = SynthBank.GroupOf(e.slot);   // v6: mono by group (every lead / bass voice)
            if (eg == SlotLead || eg == SlotBass)
            {
                long mk = ((long)e.cube << 8) | (long)(e.slot & 255);   // v9 (G): per cube, like the engine's mono owners (VoiceRules.Dispatch)
                if (lastOfMono.TryGetValue(mk, out j) && sorted[j].midi != e.midi && sorted[j].on < e.on - 1e-6)
                {
                    var p = sorted[j];
                    if (p.off > e.on) { p.off = Math.Max(p.on + 0.02 / spb, e.on); sorted[j] = p; }
                }
                lastOfMono[mk] = i;
            }
        }
        l.Clear(); l.AddRange(sorted);
    }

    // ------------------------------------------------------------------ playing
    Track track; int cursor, loop; double t0, spb; bool mixApplied;

    public bool Playing => track != null;
    public Track Current => track;
    public string Id => track != null ? track.id : "";
    /// <summary>Notes sent to the synth since the last <see cref="Play"/> / over the session.</summary>
    public int NotesScheduled { get; private set; }
    public int TotalScheduled { get; private set; }
    /// <summary>Notes skipped because their time had passed (a hitch longer than the horizon); never sent late.</summary>
    public int Dropped { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public int Loops => loop;
    /// <summary>DSP time of the preview's beat 0.</summary>
    public double StartDsp => t0;
    /// <summary>DSP time of the latest note-on scheduled so far (tests: nothing is queued past a stop).</summary>
    public double LastScheduledOn { get; private set; }
    /// <summary>Beats since the preview began (NaN while silent): the wall hops on it.</summary>
    public double Beat => track != null ? (AudioSettings.dspTime - t0) / spb : double.NaN;
    public int BeatsPerBar => track != null ? track.beatsPerBar : 4;

    /// <summary>Starts looping <paramref name="t"/> (a previous preview stops first). False when there is nothing to play or no synth.</summary>
    public bool Play(Track t)
    {
        Stop();
        if (t == null || t.notes.Count == 0 || t.lengthBeats <= 0.0 || !Synth.Ready) return false;
        track = t; cursor = 0; loop = 0; NotesScheduled = 0;
        spb = 60.0 / Mathf.Max(1f, t.bpm);
        t0 = AudioSettings.dspTime + Lead;
        ApplyMix(t);
        Starts++;
        Tick();
        return true;
    }

    /// <summary>Queues the notes due within the horizon (call every frame).</summary>
    public void Tick()
    {
        if (track == null) return;
        double now = AudioSettings.dspTime, horizon = now + Horizon;
        var l = track.notes;
        for (int guard = 0; guard < 512; guard++)
        {
            if (cursor >= l.Count) { cursor = 0; loop++; }
            var e = l[cursor];
            double baseBeat = loop * track.lengthBeats;
            double on = t0 + (baseBeat + e.on) * spb;
            if (on > horizon) break;
            cursor++;
            if (on < now + MinLead) { Dropped++; continue; }
            double off = t0 + (baseBeat + e.off) * spb;
            Synth.Note(e.slot, e.midi, e.vel, on, off, Owner);
            NotesScheduled++; TotalScheduled++;
            LastScheduledOn = on;
        }
    }

    /// <summary>Stops at once: the owner's queued notes are dropped and its sounding ones released; the loaded song's mix comes back.</summary>
    public void Stop()
    {
        if (track == null) return;
        track = null;
        Synth.CancelOwner(Owner);
        Stops++;
        RestoreMix();
    }

    /// <summary>The gallery song's own levels, mutes, sends and tone on the synth while it previews (the loaded song's come back on stop).</summary>
    void ApplyMix(Track t)
    {
        if (!Synth.Ready) return;
        Instruments.PushMix(t.gain, t.muted, t.space);   // v6: every voice slot of each group
        Synth.SetMasterCutoff01(t.tone);
        mixApplied = true;
    }

    void RestoreMix()
    {
        if (!mixApplied) return;
        mixApplied = false;
        Instruments.PushToSynth();
        Synth.SetMasterCutoff01(SongManager.Tone);
    }
}
