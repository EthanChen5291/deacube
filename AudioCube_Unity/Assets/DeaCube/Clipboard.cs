using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v6 (SPEC v6 §2.3 / §5.3, package H): the pattern clipboard — "any given cube path to be pasted onto the next grid even if it's a different chord
/// progression (adapted within the context of the genre)". Copy keeps a cube's pattern (notes, lengths, stickers, settings, sound); paste places an
/// ADAPTED copy on another grid as a new cube: grid → grid keeps the shape and moves by the inversion that stays closest in pitch
/// (Harmony.AdaptGrid; a bass line — the bass group or a pattern on the lowest row — keeps the root on its first note); a keyboard melody moves as a
/// sequence by scale steps onto the target chord (Harmony.AdaptMelody), a grid pattern pasted on a keyboard becomes that melody on its keys, a melody
/// pasted on a grid lands on the nearest tiles (Harmony.MelodyToGrid); drum patterns go as they are (x / z clamped). The pasted cube keeps
/// instrument, voice, lengths, stickers and settings (new id, new seed, no twin link); the paste is one History entry and looks like magic
/// (v6: ghost beads flew across; v7 §21: the new cube APPEARS on its target grid — the materialise pop and a sparkle there, nothing flies).
/// The adaptation core (<see cref="Adapt"/>) is shared: a carried cube's windows (AudioCube.TileAt, hop k adapted from hop k − 1) and the stamps
/// (PathManager.StampTo / StampToAll) use it too, so what the pattern ghost shows, what a paste places, what a carry plays and what a stamp copies agree.
/// v7 (SPEC v7 §2.9 / §5.2 / §17.3, package H): PASTE MODES — an ECHO an octave above / below (the adapted pattern in the octave layer ± 1:
/// "some other cubes above if higher and below if lower octave phase in and plays it ... add this as an option to copy and paste grids"; on
/// the source's own grid a doubling with no adaptation; <see cref="EchoLate"/> lands it an eighth late: a leading rest, the tail shortened), an
/// ANSWER (the adapted pattern whose last note resolves to the chord's root / 3rd: Harmony.Answer), and STEP up / down (a SEQUENCE: the pattern
/// one scale step higher / lower, Harmony.StepShift, onto the next grid's nearest tiles / exact keys / cells). Every mode appears like a
/// paste (an echo in its layer). Melody PHRASES (kind 4) paste phrase to phrase keeping every note's time cell; stairs take no paste (a runner's
/// path is its stairs' run).
/// </summary>
public static class Clipboard
{
    /// <summary>The copied cube's settings (xs / zs = its home tiles, measure = its home island index then).</summary>
    public static CubeState State { get; private set; }
    /// <summary>The pattern's sounding pitches (tile pitch incl. register, + 12 × octave), note weights (Harmony.WeightsOf) and the chord it sat on.</summary>
    public static int[] Midi { get; private set; }
    public static float[] Weights { get; private set; }
    public static int SourceKind { get; private set; }     // 0 chord island, 1 Moon, 2 keyboard
    public static int SourceRows { get; private set; }
    public static int SourceRoot { get; private set; }
    public static int[] SourceSemis { get; private set; }
    public static bool HasPattern => State != null && State.xs != null && State.xs.Length > 0;
    public static int Instrument => State != null ? State.instrument : 0;
    public static int Voice => State != null ? State.voice : 0;
    public static bool IsDrums => State != null && Instruments.IsDrums(State.instrument);
    /// <summary>Fires after a copy or a clear.</summary>
    public static event Action OnChanged;

    // ---- v6 H additions
    /// <summary>Bumped by every copy / clear (the pattern ghost and the cards' stars rebuild on it).</summary>
    public static int Version { get; private set; }
    /// <summary>The copied cube's home island and the cube itself (null once gone): "the next grid" follows the island, the flight starts at its beads.</summary>
    public static KeyBlock SourceIsland { get; private set; }
    public static AudioCube SourceCube { get; private set; }
    /// <summary>Fires after a paste placed a cube (after its History entry).</summary>
    public static event Action<AudioCube> OnPasted;
    /// <summary>Pastes placed so far and the last pasted cube (tests).</summary>
    public static int PasteCount { get; private set; }
    public static AudioCube LastPasted { get; private set; }
    /// <summary>v6: seconds the paste flight took — v7 §21: no flight (the pasted cube appears at once); kept for older callers.</summary>
    public const float FlightSeconds = 0.55f;
    /// <summary>The instrument group whose patterns follow the bass rule (VoiceRules' bass slot).</summary>
    public const int BassGroup = 4;

    static Source copied;
    // v7: the copied cube's notes of one period in play order (node index, ticks, sticker) — what a late echo delays
    static readonly List<int> orderNode = new List<int>(), orderTicks = new List<int>(), orderMods = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { State = null; Midi = null; Weights = null; SourceIsland = null; SourceCube = null; LastPasted = null; PasteCount = 0; Version = 0; copied = default(Source); EchoLate = false; LastMode = PasteMode.Plain; orderNode.Clear(); orderTicks.Clear(); orderMods.Clear(); appearLooked = false; appearM = null; gridPaths.Clear(); PathsSource = null; PathsLast = false; PathsVersion = 0; PathsPasteCount = 0; LastPathsPasted.Clear(); }

    /// <summary>Copies <paramref name="c"/>'s pattern (a finished cube with nodes). False when there is nothing to copy.</summary>
    public static bool Copy(AudioCube c)
    {
        if (c == null || c.nodes.Count == 0 || c.nodes[0] == null) return false;
        var kb = c.Island;
        var s = c.ToState();
        var midi = new int[c.nodes.Count];
        for (int i = 0; i < midi.Length; i++) midi[i] = (c.nodes[i] != null ? c.nodes[i].midi : 60) + 12 * Mathf.Clamp(c.octave, -1, 1);
        int root; IList<int> semis;
        if (kb != null && Harmony.ChordOf(kb, out root, out semis)) { SourceRoot = root; SourceSemis = new List<int>(semis).ToArray(); }
        else { SourceRoot = 60; SourceSemis = new[] { 0, 4, 7, 11 }; }
        State = s; Midi = midi;
        Weights = Harmony.WeightsOf(c.durs.Count == c.nodes.Count ? c.durs : null, c.nodes.Count);
        SourceKind = kb != null ? kb.kind : 0; SourceRows = kb != null ? kb.rows : 4;
        SourceIsland = kb; SourceCube = c;
        copied = SourceOf(c.nodes, kb, c.instrument, c.HasDurations ? c.durs : null);
        copied.root = SourceRoot; copied.semis = SourceSemis;   // the chord it sat on when copied (a keyboard's column may change later)
        int total; CubeOps.PlayOrder(c, orderNode, orderTicks, orderMods, out total);   // v7: what a late echo delays
        PathsLast = false;   // v7: ⌘V now pastes this cube, not a grid's paths
        Version++;
        OnChanged?.Invoke();
        return true;
    }

    public static void Clear() { State = null; Midi = null; Weights = null; SourceIsland = null; SourceCube = null; copied = default(Source); orderNode.Clear(); orderTicks.Clear(); orderMods.Clear(); Version++; OnChanged?.Invoke(); }

    /// <summary>True when the pattern can be pasted on <paramref name="kb"/>: pitched patterns on chord islands and keyboards; drum patterns on Moons
    /// (and, when they were copied from a chord island, on chord islands).</summary>
    public static bool CanPasteOn(KeyBlock kb)
    {
        if (!HasPattern || kb == null || kb.cols <= 0 || kb.rows <= 0) return false;
        if (kb.IsStairs) return false;                                           // v7: a stairs island's cubes are its runners (their path is the run)
        if (kb.IsPhrase && SourceKind != 4) return false;                        // v7: a phrase takes a phrase (every note keeps its time cell)
        if (IsDrums) return kb.IsMoon || (SourceKind == 0 && !kb.IsMoon && !kb.IsKeyboard && !kb.IsPhrase);
        return !kb.IsMoon;
    }

    /// <summary>The adapted cube state the pattern would become on <paramref name="kb"/> (not placed; null when it cannot go there): the exact
    /// tiles a paste places (the pattern ghost shows them).</summary>
    public static CubeState AdaptedFor(KeyBlock kb)
    {
        if (!CanPasteOn(kb) || SongManager.I == null) return null;
        int[] xs, zs;
        if (!Adapt(copied, kb, out xs, out zs)) return null;
        var s = CubeState.Clone(State);
        s.xs = xs; s.zs = zs;
        s.id = 0; s.seed = 0; s.twinOf = -1;
        if (kb != SourceIsland) s.bend = null;   // v7: a bend belongs to the source's tiles (a doubling on its own grid keeps it)
        s.echoOf = -1;
        if (kb.IsMoon) { s.moon = Mathf.Max(0, SongManager.I.Moons.IndexOf(kb)); s.instrument = DrumsGroup; }
        else { s.moon = -1; s.measure = SongManager.I.Islands.IndexOf(kb); if (s.measure < 0) return null; }
        return s;
    }

    /// <summary>Pastes the adapted pattern on <paramref name="kb"/> as a new cube: one History entry, the magic flight (ghost beads arc from the
    /// source's beads to the new tiles; the cube materialises as they land); a playing song plays it at once. Null when refused.</summary>
    public static AudioCube PasteOn(KeyBlock kb)
    {
        var pm = PathManager.I;
        if (pm == null) return null;
        var s = AdaptedFor(kb);
        if (s == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); if (kb != null) kb.Shake(0.12f); return null; }
        return Place(pm, s, PasteMode.Plain);
    }

    /// <summary>Places the pasted cube. v7 §21 (the user: "the cubes jumping form grid to grid doesnt look good ... just keep cubes on each
    /// grid"): nothing flies across any more — the new cube APPEARS on its target grid at once: the materialise pop and a sparkle there (W's
    /// Magic.Appear when it exists, else the landing burst on its first bead).</summary>
    static AudioCube Place(PathManager pm, CubeState s, PasteMode mode)
    {
        if (pm.IsDrawing) pm.FinishPath();
        var c = pm.RestoreCube(s);
        if (c == null) return null;
        SequenceMaster.RecalculateTimeline();
        History.Push();
        c.Materialize();
        if (!MagicAppear(c)) Magic.Land(c.BeadPosition(0), c.Color);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.3f, mode == PasteMode.EchoUp || mode == PasteMode.StepUp ? 1.35f : (mode == PasteMode.EchoDown || mode == PasteMode.StepDown ? 0.95f : 1.2f));
        PasteCount++; LastPasted = c; LastMode = mode;
        OnPasted?.Invoke(c);
        return c;
    }

    // W's Magic.Appear(AudioCube) (the §21 paste look) is called when it exists; until then the landing burst stands in
    static bool appearLooked; static System.Reflection.MethodInfo appearM;
    static bool MagicAppear(AudioCube c)
    {
        if (!appearLooked) { appearLooked = true; try { appearM = typeof(Magic).GetMethod("Appear", new[] { typeof(AudioCube) }); } catch (Exception) { appearM = null; } }
        if (appearM == null) return false;
        try { appearM.Invoke(null, new object[] { c }); return true; } catch (Exception e) { Debug.LogWarning("Magic.Appear: " + (e.InnerException ?? e).Message); return false; }
    }

    // ---- v7 (SPEC v7 §2.9 / §5.2 / §17.3): paste modes — an ECHO an octave above / below (the octave layer: "some other cubes above if higher and
    // below if lower octave phase in and plays it ... add this as an option to copy and paste"), an ANSWER (the phrase ends at home), a STEP up /
    // down (the same shape one scale step higher / lower: a sequence). Values keep their numbers (StepUp / StepDown appended).
    public enum PasteMode { Plain, EchoUp, EchoDown, Answer, StepUp, StepDown }
    /// <summary>v7: echoes land an eighth late (a delayed echo, like the recording's D6 → D4): a leading rest of <see cref="CubeOps.EchoLateTicks"/>.</summary>
    public static bool EchoLate;
    /// <summary>v7: the mode of the last paste (tests / tips).</summary>
    public static PasteMode LastMode { get; private set; }
    /// <summary>v7: the copied cube's octave layer (an echo goes one further).</summary>
    public static int SourceLayer => State != null ? State.layer : 0;
    /// <summary>v7: the copied source's cube id (an echo's echoOf).</summary>
    public static int SourceId => State != null ? State.id : 0;
    public static bool IsEcho(PasteMode mode) => mode == PasteMode.EchoUp || mode == PasteMode.EchoDown;

    /// <summary>v7: <see cref="CanPasteOn(KeyBlock)"/> for a mode. Echoes need a pitched pattern and a layer left (± MaxLayer) and may target the
    /// source's own island (a doubling in the next layer); answer / step need a pitched pattern.</summary>
    public static bool CanPasteOn(KeyBlock kb, PasteMode mode)
    {
        if (!CanPasteOn(kb)) return false;
        switch (mode)
        {
            case PasteMode.EchoUp:
            case PasteMode.EchoDown:
                if (IsDrums || kb.IsMoon) return false;
                int l = SourceLayer + (mode == PasteMode.EchoUp ? 1 : -1);
                return l >= -ProjectConfig.MaxLayer && l <= ProjectConfig.MaxLayer;
            case PasteMode.Answer:
            case PasteMode.StepUp:
            case PasteMode.StepDown:
                return !IsDrums && !kb.IsMoon;
        }
        return true;
    }

    /// <summary>v7: the cube state a paste in <paramref name="mode"/> places on <paramref name="kb"/> (not placed; null when it cannot go there).</summary>
    public static CubeState AdaptedFor(KeyBlock kb, PasteMode mode)
    {
        if (mode == PasteMode.Plain) return AdaptedFor(kb);
        if (!CanPasteOn(kb, mode)) return null;
        var s = AdaptedFor(kb);
        if (s == null) return null;
        int n = s.xs.Length;
        var mods = State.ModsOrDerived();
        if (IsEcho(mode))
        {
            bool doubling = kb == SourceIsland;
            if (doubling) { s.xs = (int[])State.xs.Clone(); s.zs = (int[])State.zs.Clone(); s.bend = State.bend != null ? (int[])State.bend.Clone() : null; }   // no adaptation
            s.layer = Mathf.Clamp(SourceLayer + (mode == PasteMode.EchoUp ? 1 : -1), -ProjectConfig.MaxLayer, ProjectConfig.MaxLayer);
            s.echoOf = SourceId > 0 ? SourceId : -1;
            if (EchoLate && orderNode.Count > 0)
            {
                var xs = s.xs; var zs = s.zs; var bend = s.bend; var nudge = s.nudges;   // v9 (N): the nudges travel with their notes
                if (!CubeOps.ApplyDelay(s, orderNode, orderTicks, orderMods, xs, zs, bend, nudge, CubeOps.EchoLateTicks)) return null;
            }
            return s;
        }
        s.echoOf = -1;
        var key = MusicTheory.KeyOfSong();
        if (mode == PasteMode.Answer)
        {
            // the adapted notes, the last sounding one resolved to the target chord's root / 3rd (Harmony.Answer over the grid's pitches)
            var p = new int[n];
            for (int i = 0; i < n; i++) { var t = kb.GetTile(s.xs[i], s.zs[i]); p[i] = i < mods.Length && mods[i] == 1 || t == null ? -1 : t.midi; }
            int root; IList<int> semis;
            if (!Harmony.ChordOf(kb, out root, out semis) || semis == null) { root = kb.chordRootMIDI; semis = kb.semitoneList; }
            var a = Harmony.Answer(p, Weights, root, semis, Harmony.Pool(kb));
            Retile(kb, a, s.xs, s.zs);
            return s;
        }
        // StepUp / StepDown: the source's own pitches one scale step up / down, onto the target's nearest tiles (a keyboard's keys / a phrase's cells exactly)
        var src = new int[n];
        for (int i = 0; i < n; i++) src[i] = i < mods.Length && mods[i] == 1 || Midi == null || i >= Midi.Length ? -1 : Midi[i] - 12 * Mathf.Clamp(State.octave, -1, 1);
        var line = Harmony.StepShift(src, mode == PasteMode.StepUp ? 1 : -1, key.tonic, key.minor);
        int lastPitch = -1;
        for (int i = 0; i < n; i++) if (line[i] >= 0) { lastPitch = line[i]; break; }
        for (int i = 0; i < n; i++) { if (line[i] < 0) line[i] = lastPitch; else lastPitch = line[i]; }   // rests are placed with their neighbours
        if (lastPitch < 0) return null;
        if (kb.IsPhrase)
        {
            for (int i = 0; i < n; i++)
            {
                int x = Mathf.Clamp(State.xs[i], 0, kb.cols - 1);
                var t = CubeOps.TileFor(kb, line[i], kb.GetTile(x, Mathf.Clamp(State.zs[i], 0, kb.rows - 1)), false);
                s.xs[i] = x; s.zs[i] = t != null ? t.gridZ : 0;
            }
        }
        else { int[] xs, zs; Harmony.MelodyToGrid(line, Harmony.MidiTable(kb), out xs, out zs); s.xs = xs; s.zs = zs; }
        return s;
    }

    /// <summary>Moves node i onto the tile of pitch <paramref name="want"/>[i] nearest to where it is (a phrase keeps the time cell) wherever it
    /// differs; −1 keeps the node.</summary>
    static void Retile(KeyBlock kb, IList<int> want, int[] xs, int[] zs)
    {
        for (int i = 0; i < xs.Length && i < want.Count; i++)
        {
            if (want[i] < 0) continue;
            var cur = kb.GetTile(Mathf.Clamp(xs[i], 0, kb.cols - 1), Mathf.Clamp(zs[i], 0, kb.rows - 1));
            if (cur != null && cur.midi == want[i]) continue;
            var t = CubeOps.TileFor(kb, want[i], cur, false);
            if (t != null) { xs[i] = t.gridX; zs[i] = t.gridZ; }
        }
    }

    /// <summary>v7: <see cref="PasteOn(KeyBlock)"/> in a mode: one History entry, the paste flight (echoes fly up / down into their layer); a playing
    /// song plays it at once. Null when refused.</summary>
    public static AudioCube PasteOn(KeyBlock kb, PasteMode mode)
    {
        var pm = PathManager.I;
        if (pm == null) return null;
        var s = AdaptedFor(kb, mode);
        if (s == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); if (kb != null) kb.Shake(0.12f); return null; }
        return Place(pm, s, mode);
    }

    /// <summary>v7: <see cref="PasteNext()"/> in a mode (onto "the next grid" after the copied cube's home).</summary>
    public static AudioCube PasteNext(PasteMode mode)
    {
        var t = NextGrid(SourceIsland);
        return t != null ? PasteOn(t, mode) : null;
    }

    /// <summary>Pastes onto "the next grid" — the island after the copied cube's home in the progression (<see cref="NextGrid"/>). Null when there is none.</summary>
    public static AudioCube PasteNext()
    {
        var t = NextGrid(SourceIsland);
        return t != null ? PasteOn(t) : null;
    }

    /// <summary>"The next grid" after <paramref name="home"/>: in the next column (wrapping at the song's end; a column without one is skipped) the island
    /// nearest in z of the same family (chord islands → chord islands; keyboards → keyboards, else chord islands) — CarryTargets' first hop; for a Moon
    /// the other Moon. Null when there is none the pattern can go on.</summary>
    public static KeyBlock NextGrid(KeyBlock home)
    {
        var sm = SongManager.I;
        if (home == null || sm == null) return null;
        if (home.IsMoon)
        {
            int i = sm.Moons.IndexOf(home), n = sm.Moons.Count;
            for (int k = 1; i >= 0 && k < n; k++) { var m = sm.Moons[(i + k) % n]; if (m != null && m != home && CanPasteOn(m)) return m; }
            return null;
        }
        int cols = sm.ColumnCount;
        if (cols <= 0 || home.column < 0) return null;
        var lane = new List<KeyBlock>(4);
        for (int k = 1; k <= cols; k++)
        {
            int col = (home.column + k) % cols;
            sm.ColumnIslands(col, lane);
            for (int pass = 0; pass < 2; pass++)
            {
                KeyBlock best = null; float bestD = float.MaxValue;
                foreach (var kb in lane)
                {
                    if (kb == null || kb == home || kb.IsMoon || !CanPasteOn(kb)) continue;
                    bool same = kb.IsKeyboard == home.IsKeyboard;
                    if (pass == 0 ? !same : (kb.IsKeyboard || !home.IsKeyboard)) continue;   // pass 1: a keyboard's pattern falls back to chord islands
                    float d = Mathf.Abs(kb.Center.z - home.Center.z);
                    if (d < bestD - 1e-4f) { bestD = d; best = kb; }
                }
                if (best != null) return best;
            }
        }
        return null;
    }

    static int DrumsGroup { get { for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) return i; return Instruments.Count - 1; } }

    // ================================================================== v7: GRID PATHS (the user, 2026-10-01)
    // "where is the feature that allows me to copy a certain grid's paths to a different chord progression … add the grid level edition and also
    // make it a button": every path of a grid copied at once (its finished cubes, every octave layer and harmony) and pasted onto another grid,
    // each ADAPTED to that grid's chord exactly as a single paste is (Adapt: a grid pattern keeps its shape by voice leading, a bass line keeps
    // the root, a melody moves by scale steps, drums go as they are). One History entry; the new cubes appear on the target grid (§21: nothing
    // flies). The island header's copy-paths / paste-paths stickers and ⌘C / ⌘V over a grid's empty tiles drive it.
    struct PathEntry { public CubeState state; public Source src; public int kind; }
    static readonly List<PathEntry> gridPaths = new List<PathEntry>();
    /// <summary>The grid whose paths were copied (null once cleared; Unity-null once rebuilt away).</summary>
    public static KeyBlock PathsSource { get; private set; }
    /// <summary>The number of paths copied (0 = none).</summary>
    public static int PathsCount => gridPaths.Count;
    public static bool HasPaths => gridPaths.Count > 0;
    /// <summary>True when the last copy was a grid's paths (⌘V pastes them), false after a cube's pattern was copied.</summary>
    public static bool PathsLast { get; private set; }
    /// <summary>Bumped by every grid copy / clear (the header's paste-paths sticker repaints on it).</summary>
    public static int PathsVersion { get; private set; }
    /// <summary>Grid pastes placed so far and the cubes the last one placed (tests).</summary>
    public static int PathsPasteCount { get; private set; }
    public static readonly List<AudioCube> LastPathsPasted = new List<AudioCube>();
    /// <summary>Fires after a grid copy or a clear.</summary>
    public static event Action OnPathsChanged;

    /// <summary>The paths of <paramref name="kb"/>: its finished cubes with notes (every octave layer and harmony), in the order they were made;
    /// none for a stairs island (its cubes are its runners: their path is the run).</summary>
    public static List<AudioCube> PathsOf(KeyBlock kb)
    {
        var r = new List<AudioCube>();
        if (kb == null || kb.IsStairs) return r;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && c.nodes.Count > 0 && c.nodes[0] != null && c.Island == kb) r.Add(c);
        r.Sort((a, b) => a.id.CompareTo(b.id));
        return r;
    }

    /// <summary>How many paths <paramref name="kb"/> has (<see cref="PathsOf"/> without the list: the header asks every frame).</summary>
    public static int PathCount(KeyBlock kb)
    {
        if (kb == null || kb.IsStairs) return 0;
        int n = 0;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0 && c.nodes[0] != null && c.Island == kb) n++;
        return n;
    }

    /// <summary>Copies every path of <paramref name="kb"/> (<see cref="PathsOf"/>); ⌘V and the paste-paths sticker then paste them. False when it has none.</summary>
    public static bool CopyPaths(KeyBlock kb)
    {
        var cubes = PathsOf(kb);
        if (cubes.Count == 0) return false;
        gridPaths.Clear();
        foreach (var c in cubes)
            gridPaths.Add(new PathEntry { state = c.ToState(), src = SourceOf(c.nodes, kb, c.instrument, c.HasDurations ? c.durs : null), kind = kb.kind });
        PathsSource = kb; PathsLast = true; PathsVersion++;
        OnPathsChanged?.Invoke();
        return true;
    }

    public static void ClearPaths() { gridPaths.Clear(); PathsSource = null; PathsLast = false; PathsVersion++; OnPathsChanged?.Invoke(); }

    // where one copied path can go — the single paste's rule (CanPasteOn) for that path
    static bool PathCanGo(PathEntry e, KeyBlock kb)
    {
        if (kb == null || kb.cols <= 0 || kb.rows <= 0 || kb.IsStairs) return false;
        if (kb.IsPhrase && e.kind != 4) return false;
        if (Instruments.IsDrums(e.state.instrument)) return kb.IsMoon || (e.kind == 0 && !kb.IsMoon && !kb.IsKeyboard && !kb.IsPhrase);
        return !kb.IsMoon;
    }

    // the adapted cube state one copied path becomes on kb (AdaptedFor's rules: new id and seed, no twin / echo link, no bend off its own tiles)
    static CubeState PathFor(PathEntry e, KeyBlock kb)
    {
        var sm = SongManager.I;
        if (sm == null || !PathCanGo(e, kb)) return null;
        int[] xs, zs;
        if (!Adapt(e.src, kb, out xs, out zs)) return null;
        var s = CubeState.Clone(e.state);
        s.xs = xs; s.zs = zs;
        s.id = 0; s.seed = 0; s.twinOf = -1; s.bend = null; s.echoOf = -1;
        if (kb.IsMoon) { s.moon = Mathf.Max(0, sm.Moons.IndexOf(kb)); s.instrument = DrumsGroup; }
        else { s.moon = -1; s.measure = sm.Islands.IndexOf(kb); if (s.measure < 0) return null; }
        return s;
    }

    /// <summary>How many of the copied paths can go on <paramref name="kb"/> (0 = none — the grid they were copied from takes none: it has them).</summary>
    public static int PathsFitOn(KeyBlock kb)
    {
        if (!HasPaths || kb == null || kb == PathsSource) return 0;
        int n = 0;
        foreach (var e in gridPaths) if (PathCanGo(e, kb)) n++;
        return n;
    }
    public static bool CanPastePathsOn(KeyBlock kb) => PathsFitOn(kb) > 0;

    /// <summary>The adapted cube states a grid paste would place on <paramref name="kb"/> (not placed; tests).</summary>
    public static List<CubeState> PathsAdaptedFor(KeyBlock kb)
    {
        var r = new List<CubeState>();
        if (PathsFitOn(kb) == 0) return r;
        foreach (var e in gridPaths) { var s = PathFor(e, kb); if (s != null) r.Add(s); }
        return r;
    }

    /// <summary>Pastes every copied path that can go on <paramref name="kb"/>, each adapted to its chord, alongside the cubes already there: ONE History
    /// entry, they appear there (a playing song plays them at once). Returns the new cubes; refused (none can go there) = a thud, the grid shakes.</summary>
    public static List<AudioCube> PastePathsOn(KeyBlock kb)
    {
        LastPathsPasted.Clear();
        var pm = PathManager.I;
        var states = pm != null ? PathsAdaptedFor(kb) : new List<CubeState>();
        if (states.Count == 0) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); if (kb != null) kb.Shake(0.12f); return new List<AudioCube>(); }
        if (pm.IsDrawing) pm.FinishPath();
        foreach (var s in states) { var c = pm.RestoreCube(s); if (c != null) LastPathsPasted.Add(c); }
        if (LastPathsPasted.Count == 0) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return new List<AudioCube>(); }
        SequenceMaster.RecalculateTimeline();
        History.Push();
        foreach (var c in LastPathsPasted) { c.Materialize(); if (!MagicAppear(c)) Magic.Land(c.BeadPosition(0), c.Color); }
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.32f, 1.2f);
        PathsPasteCount++;
        foreach (var c in LastPathsPasted) OnPasted?.Invoke(c);
        return new List<AudioCube>(LastPathsPasted);
    }

    // ================================================================== the adaptation core (paste, carry, stamps)
    /// <summary>A pattern to adapt: its tile pitches (register included, the cube's octave NOT: the copy keeps its octave setting), grid positions,
    /// note weights, the kind / rows of the grid it sits on, the chord it sits over and the cube's instrument group.</summary>
    public struct Source
    {
        public int[] midi, xs, zs; public float[] w; public int kind, rows, root; public int[] semis; public int instrument;
        public bool Valid => xs != null && xs.Length > 0 && zs != null && zs.Length == xs.Length && midi != null && midi.Length == xs.Length;
    }

    /// <summary>The pattern of <paramref name="tiles"/> (on <paramref name="island"/>, over its chord: Harmony.ChordOf) played by
    /// <paramref name="instrument"/> with lengths <paramref name="durs"/> (null = equal).</summary>
    public static Source SourceOf(IList<TileInteraction> tiles, KeyBlock island, int instrument, IList<int> durs)
    {
        int n = tiles != null ? tiles.Count : 0;
        var s = new Source { midi = new int[n], xs = new int[n], zs = new int[n], instrument = instrument, kind = island != null ? island.kind : 0, rows = island != null ? island.rows : 1 };
        for (int i = 0; i < n; i++) { var t = tiles[i]; s.midi[i] = t != null ? t.midi : 60; s.xs[i] = t != null ? t.gridX : 0; s.zs[i] = t != null ? t.gridZ : 0; }
        s.w = Harmony.WeightsOf(durs != null && durs.Count == n ? durs : null, n);
        int root; IList<int> semis;
        if (island != null && Harmony.ChordOf(island, out root, out semis)) { s.root = root; s.semis = semis != null ? new List<int>(semis).ToArray() : new[] { 0, 4, 7 }; }
        else { s.root = 60; s.semis = new[] { 0, 4, 7 }; }
        return s;
    }

    /// <summary>
    /// The tiles pattern <paramref name="src"/> becomes on <paramref name="target"/> (grid positions, node for node). Drums (or a Moon target): as
    /// they are, clamped. Grid → grid: Harmony.AdaptGrid (the bass rule for the bass group or a pattern on the lowest row: the first note on the
    /// target root). Anything with a keyboard: the melody by scale steps onto the target chord (Harmony.AdaptMelody; a keyboard's chord = its
    /// column's chord island, Harmony.ChordOf), in the target's pitch range, then onto its tiles (Harmony.MelodyToGrid: a keyboard's exact keys, a
    /// grid's nearest tiles). False when there is nothing to adapt.
    /// </summary>
    public static bool Adapt(Source src, KeyBlock target, out int[] xs, out int[] zs)
    {
        xs = zs = null;
        if (!src.Valid || target == null || target.cols <= 0 || target.rows <= 0) return false;
        int n = src.xs.Length;
        if (Instruments.IsDrums(src.instrument) || target.IsMoon || src.kind == 1)
        {
            xs = new int[n]; zs = new int[n];
            for (int i = 0; i < n; i++) { xs[i] = Mathf.Clamp(src.xs[i], 0, target.cols - 1); zs[i] = Mathf.Clamp(src.zs[i], 0, target.rows - 1); }
            return true;
        }
        var table = Harmony.MidiTable(target);
        int tgtRoot; IList<int> tgtSemis;
        if (!Harmony.ChordOf(target, out tgtRoot, out tgtSemis) || tgtSemis == null) { tgtRoot = target.chordRootMIDI; tgtSemis = target.semitoneList; }
        bool srcKeys = src.kind == 2 || src.kind == 4, tgtKeys = target.IsKeyboard || target.IsPhrase;   // v7: a phrase is a melody (time along x)
        if (!srcKeys && !tgtKeys)
        {
            // the root rule is for BASS lines only (the bass group): a pattern running along row 0 climbs the inversion columns an octave
            // at a time (an arpeggio, not a low line) and keeps the voice-led shape like every other comping pattern (v6 integration)
            bool bassLike = src.instrument == BassGroup;
            Harmony.AdaptGrid(src.midi, src.xs, src.zs, src.w, src.rows, table, bassLike, bassLike ? Harmony.Pc(tgtRoot) : -1, out xs, out zs);
            return true;
        }
        int lo = int.MaxValue, hi = int.MinValue;
        for (int x = 0; x < table.GetLength(0); x++) for (int z = 0; z < table.GetLength(1); z++) { lo = Mathf.Min(lo, table[x, z]); hi = Mathf.Max(hi, table[x, z]); }
        var key = MusicTheory.KeyOfSong();
        var line = Harmony.AdaptMelody(src.midi, src.w, src.root, src.semis, tgtRoot, tgtSemis, key.tonic, key.minor, lo, hi);
        if (target.IsPhrase)
        {
            // v7: onto a phrase every note keeps its time cell (x) and takes the row of its pitch there
            xs = new int[n]; zs = new int[n];
            for (int i = 0; i < n; i++)
            {
                int x = Mathf.Clamp(src.xs[i], 0, target.cols - 1);
                var t = CubeOps.TileFor(target, line[i], target.GetTile(x, Mathf.Clamp(src.zs[i], 0, target.rows - 1)), false);
                xs[i] = x; zs[i] = t != null ? t.gridZ : 0;
            }
            return true;
        }
        Harmony.MelodyToGrid(line, table, out xs, out zs);
        return true;
    }

    /// <summary>The pitches (tile pitches, register included) pattern <paramref name="src"/> sounds on <paramref name="target"/> (tests / the
    /// cards' stars); null when it cannot be adapted.</summary>
    public static int[] AdaptedMidi(Source src, KeyBlock target)
    {
        int[] xs, zs;
        if (!Adapt(src, target, out xs, out zs)) return null;
        var r = new int[xs.Length];
        for (int i = 0; i < r.Length; i++) { var t = target.GetTile(xs[i], zs[i]); r[i] = t != null ? t.midi : 0; }
        return r;
    }

    /// <summary>The copied pattern as an adaptation source (tests).</summary>
    public static Source Copied => copied;
}
