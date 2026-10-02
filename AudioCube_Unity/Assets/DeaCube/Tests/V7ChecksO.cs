using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package O (v7: the song's structural ops — SongOps + GridSelection) Play-mode verification: <c>V7ChecksO.RunAll()</c> starts a coroutine; poll
/// <c>V7ChecksO.Done</c> or Captures/o7_report.txt (numbered PASS / FAIL lines, INFO lines with numbers). Every op on a synthetic fixture (C major,
/// eight one-bar columns C Am F G7 | Dm G7 C F in two sections, a keys cube, a bass cube, a Moon with a kick): the snapshot after it (kinds,
/// columns, sections, cubes, registers, stairs runs, phrase lengths and notes), exactly ONE History entry (GrowPhrase: none — the stroke pushes),
/// undo restoring, the selection following its grids; STAIRS (in a column / as a new column / inserted, the type the chord stars, the runner =
/// SongManager.StairPath, SetStair re-deriving it, the ground after it), PHRASES (add + the hand, set length trimming / re-padding notes,
/// auto-grow ½ → 1 → 2 → 4 → 8 inside its section / past it joining the next measures / past the song's end appending SuggestNext chords, double,
/// move, cells), SECTIONS (split, join, duplicate with the name, delete, move, names), MANY GRIDS (select, copy, paste at the end / inside a
/// section by the landing rule, delete, transpose, duplicate, repeat, flow, launch, Moons), and ops while the song plays (Synth late / errors).
/// Captures o7_*.png. Runs with PathManager.AutoHand = false. Never writes the user's save; reloads the fixture song at the end.
/// </summary>
public static class V7ChecksO
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static StringBuilder sb;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "o7_report.txt");

    static SongManager SM => SongManager.I;
    static string F(float v) => v.ToString("F3");

    static void Line(bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" O7-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(); }
    static void Flush() { try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, sb.ToString() + (Done ? "" : "...running\n")); } catch (Exception) { } }

    /// <summary>Runs <paramref name="check"/> (null = PASS, else the failure) and writes its line; an exception is a FAIL with its first frame.</summary>
    static void Check(string name, Func<string> check)
    {
        string d;
        try { d = check(); }
        catch (Exception e) { d = "threw " + e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e); }
        Line(d == null || d.StartsWith("ok"), name, d ?? "ok");
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    /// <summary>A PASS detail with numbers ("ok …"), or the failure.</summary>
    static string Ok(bool ok, string numbers) => ok ? "ok " + numbers : numbers;

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; num = 0; sb = new StringBuilder();
        SequenceMaster.I.StartCoroutine(Routine(captures));
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        GridSelection.Clear();
    }

    static void Restore()
    {
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
        GridSelection.Clear();
        SongOps.ClearCopiedGrids();
    }

    // ------------------------------------------------------------------ the fixture
    static CubeState Cube(int instrument, int measure, int[] xs, int[] zs, int[] durs)
    {
        int n = xs.Length;
        return new CubeState { instrument = instrument, measure = measure, moon = -1, xs = xs, zs = zs, rests = new bool[n], mods = new int[n], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1, echoOf = -1 };
    }

    /// <summary>C major, eight one-bar columns C Am F G7 | Dm G7 C F (sections 0, 4); a keys cube on column 0, a bass cube on column 4, a Moon at
    /// column 0 with a kick.</summary>
    static SongState Fixture()
    {
        var chords = new[]
        {
            MusicTheory.Chord(60, ChordQuality.Major7), MusicTheory.Chord(57, ChordQuality.Minor7), MusicTheory.Chord(65, ChordQuality.Major7), MusicTheory.Chord(67, ChordQuality.Dominant7),
            MusicTheory.Chord(62, ChordQuality.Minor7), MusicTheory.Chord(67, ChordQuality.Dominant7), MusicTheory.Chord(60, ChordQuality.Major7), MusicTheory.Chord(65, ChordQuality.Major7)
        };
        var ms = new MeasureState[chords.Length];
        for (int c = 0; c < ms.Length; c++) { var m = MeasureState.From(chords[c]); m.col = c; m.placed = true; m.px = c * 12f; m.pz = 0f; m.kind = 0; ms[c] = m; }
        var keys = Cube(0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 }); keys.id = 1;
        var bass = Cube(4, 4, new[] { 0, 0 }, new[] { 0, 2 }, new[] { 48, 48 }); bass.id = 2;
        var kick = new CubeState { instrument = 9, measure = 0, moon = 0, xs = new[] { 3 }, zs = new[] { 0 }, rests = new bool[1], step = 1, gate = 1, volume = 1f, hits = 4, twinOf = -1, echoOf = -1, id = 3 };
        var moon = new MeasureState { chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1, col = 0, px = 0f, pz = -12f };
        return new SongState
        {
            version = SongState.CurrentVersion, name = "o7 fixture", bpm = 120f, beatsPerBar = 4, loop = true, measures = ms, cubes = new[] { keys, bass, kick },
            moons = new[] { moon }, keyTonic = 0, keyMinor = false, sections = new[] { 0, 4 }, tone = 1f, space = 1f, demoSeed = 7
        };
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        GridSelection.Clear();
        SongOps.ClearCopiedGrids();
        if (PathManager.I != null) PathManager.I.PutDown();
        SongState.Apply(Fixture());
        History.Reset(); History.Push();
    }

    // ------------------------------------------------------------------ helpers
    static string Secs() => string.Join(",", SM.SectionStarts().Select(x => x.ToString()).ToArray());
    static string Kinds() { var s = new StringBuilder(); foreach (var kb in SM.Islands) s.Append(kb.kind).Append('@').Append(kb.column).Append(' '); return s.ToString().Trim(); }
    static int H => History.UndoCount;
    static List<AudioCube> CubesOn(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && c.isFinalized && !c.IsOnMoon && c.Island == kb).ToList();
    static KeyBlock Anchor(int col) => SM.AnchorOf(col);
    static List<KeyBlock> Grids(int c0, int c1) => SM.Islands.Where(k => k != null && !k.IsMoon && k.column >= c0 && k.column <= c1).ToList();
    static int Pc(int m) => ((m % 12) + 12) % 12;
    static string ChordSig(KeyBlock kb) => kb == null ? "-" : Pc(kb.chordRootMIDI) + ":" + string.Join(".", (kb.storedSemis ?? new int[0]).Select(x => x.ToString()).ToArray());
    static string ColumnChords(int c0, int c1) { var l = new List<string>(); for (int c = c0; c <= c1; c++) l.Add(ChordSig(Anchor(c))); return string.Join(" ", l.ToArray()); }
    static string Arr(IEnumerable<int> a) => a == null ? "null" : string.Join(",", a.Select(x => x.ToString()).ToArray());
    static bool Same(IList<int> a, IList<int> b) => a != null && b != null && a.Count == b.Count && a.SequenceEqual(b);
    static int SumDurs(CubeState c) => c != null && c.durs != null ? c.durs.Sum() : 0;
    static CubeState StateOf(AudioCube c) => c != null ? c.ToState() : null;
    static string Notes(CubeState c) => c == null ? "null" : string.Join(" ", PhraseRoll.NotesOf(c).Select(n => n.start + ":" + n.row + "x" + n.len).ToArray());

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

    static IEnumerator Shot(string file, Bounds b)
    {
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(b, 0.1f, true, 1.1f);
        yield return Wait(0.5f);
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return Frames(3);
        yield return Wait(0.4f);
    }
    static Bounds ColumnsBounds(int c0, int c1)
    {
        var b = SM.ColumnBounds(Mathf.Clamp(c0, 0, SM.ColumnCount - 1));
        for (int c = c0 + 1; c <= c1 && c < SM.ColumnCount; c++) b.Encapsulate(SM.ColumnBounds(c));
        foreach (var kb in SM.Islands) if (kb != null && kb.IsPhrase && kb.column >= c0 && kb.column <= c1) b.Encapsulate(kb.FootprintBounds);
        return b;
    }

    /// <summary>A phrase cube: <paramref name="notes"/> (start, row, len) on a phrase of <paramref name="beats"/> beats (cells of eighths).</summary>
    static AudioCube PutNotes(KeyBlock phrase, int instrument, int[][] notes)
    {
        var list = notes.Select(n => new PhraseNote(n[0], n[1], n[2])).ToList();
        int total = phrase.phraseBeats * ProjectConfig.TicksPerBeat, grid = phrase.phraseGrid == 6 ? 6 : 12;
        int[] xs, zs, mods, durs;
        PhraseRoll.Encode(list, total, grid, Mathf.Max(1, total / grid), out xs, out zs, out mods, out durs);
        var cs = new CubeState { instrument = instrument, measure = SM.Islands.IndexOf(phrase), moon = -1, xs = xs, zs = zs, mods = mods, durs = durs, rests = mods.Select(m => m == 1).ToArray(), step = 1, gate = 1, volume = 1f, hits = -1, twinOf = -1, echoOf = -1 };
        var c = PathManager.I.RestoreCube(cs);
        SM.RecomputeMeasureStarts();
        History.Push();
        return c;
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine(bool captures)
    {
        sb.Append("V7ChecksO ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Flush();
        Prepare();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        yield return Guard(Stairs(captures), "stairs");
        yield return Guard(Phrases(captures), "phrases");
        yield return Guard(Sections(captures), "sections");
        yield return Guard(ManyGrids(captures), "many grids");
        yield return Guard(Capacity(), "capacity");
        yield return Guard(Edges(), "edges");
        yield return Guard(Playing(), "playing");
        Restore();
        try { LoadFixture(); } catch (Exception) { }
        int fails = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append(string.Format("DONE {0} checks, {1} failed; ops {2}; synth late +{3} errors +{4} over the whole run ({5})\n", num, fails, SongOps.OpCount, Synth.LateEvents - late0, Synth.Errors - err0, Synth.Stats()));
        Done = true;
        Report = sb.ToString();
        Flush();
    }

    /// <summary>Runs a group; an exception in it becomes a FAIL line (the run goes on with the next group).</summary>
    static IEnumerator Guard(IEnumerator e, string name)
    {
        while (true)
        {
            object cur;
            try { if (!e.MoveNext()) yield break; cur = e.Current; }
            catch (Exception ex) { Line(false, name + " threw", ex.GetType().Name + " " + ex.Message + " @ " + FirstFrame(ex)); yield break; }
            yield return cur;
        }
    }

    static int ExpectedStairType(int col)
    {
        var ch = SM.ChordOfColumn(col);
        IList<int> semis = Harmony.SemisOf(ch); int job = Harmony.Job(ch);
        int best = 0; float bf = -1f;
        for (int t = 0; t < 6; t++) { float f = Harmony.StairFit(t, semis, job); if (f > bf + 1e-4f) { bf = f; best = t; } }
        return best;
    }

    // ------------------------------------------------------------------ STAIRS
    static IEnumerator Stairs(bool captures)
    {
        LoadFixture();
        yield return Frames(2);
        Check("fixture: 8 one-bar columns C Am F G7 | Dm G7 C F, sections 0,4, a keys + a bass cube, a Moon with a kick", () =>
            Ok(SM.ColumnCount == 8 && Secs() == "0,4" && SM.Moons.Count == 1 && CubesOn(Anchor(0)).Count == 1 && CubesOn(Anchor(4)).Count == 1 && H == 1,
               "columns " + SM.ColumnCount + ", sections " + Secs() + ", moons " + SM.Moons.Count + ", kinds " + Kinds() + ", history " + H));

        int stairEvents = 0;
        Action<KeyBlock> onStair = kb => stairEvents++;
        SM.OnStairChanged += onStair;
        try
        {
            int h0 = H, expType = ExpectedStairType(1);
            int at = SongOps.AddStairIsland(SM.ColumnFirst(1), false, -1);
            Check("AddStairIsland into Am's column: a kind 3 island in column 1 (the column's bars), 8 columns, sections kept, ONE History entry", () =>
            {
                var kb = at >= 0 && at < SM.Islands.Count ? SM.Islands[at] : null;
                if (kb == null) return "returned " + at;
                return Ok(kb.IsStairs && kb.column == 1 && SM.ColumnCount == 8 && SM.ColumnIslands(1).Count == 2 && H == h0 + 1 && Secs() == "0,4" && kb.bars == 1,
                    "index " + at + " kind " + kb.kind + " column " + kb.column + " bars " + kb.bars + ", column 1 holds " + SM.ColumnIslands(1).Count + ", columns " + SM.ColumnCount + ", history +" + (H - h0) + ", sections " + Secs());
            });
            Check("its run: the type Harmony.StairFit stars over Am (" + Harmony.StairTypeWord(expType) + "), falling, 4 steps of eighths, a lead-in", () =>
            {
                var kb = SM.Islands[at];
                return Ok(kb.stairType == expType && kb.stairDir == -1 && kb.stairSteps == 4 && kb.stairRate == 12 && kb.stairLead,
                    "type " + kb.stairType + " dir " + kb.stairDir + " steps " + kb.stairSteps + " rate " + kb.stairRate + " lead " + kb.stairLead);
            });
            Check("ONE runner of the lead group: its path = SongManager.StairPath = SongOps.StairPathOf (a rest, then the 4 steps ending at the bar line), rows 0, the brush voice", () =>
            {
                var kb = SM.Islands[at];
                var cs = CubesOn(kb);
                if (cs.Count != 1) return "cubes on it " + cs.Count;
                var s = cs[0].ToState();
                int[] xs, mods, durs; SM.StairPath(kb, out xs, out mods, out durs);
                int[] xs2, mods2, durs2; SongOps.StairPathOf(kb.stairSteps, kb.stairRate, kb.bars, kb.stairLead, out xs2, out mods2, out durs2);
                bool ok = s.instrument == SongOps.LeadGroup && Same(s.xs, xs) && Same(s.durs, durs) && Same(s.ModsOrDerived(), mods) && Same(xs, xs2) && Same(durs, durs2) && Same(mods, mods2)
                    && s.zs.All(z => z == 0) && s.voice == Instruments.BrushVoiceOf(SongOps.LeadGroup) && s.durs.Sum() == 96;
                return Ok(ok, "instrument " + s.instrument + " xs [" + Arr(s.xs) + "] mods [" + Arr(s.ModsOrDerived()) + "] durs [" + Arr(s.durs) + "] (StairPath xs [" + Arr(xs) + "] durs [" + Arr(durs) + "]), Σ " + s.durs.Sum() + " ticks, voice " + s.voice);
            });
            Check("OnStairChanged fired for the new island", () => Ok(stairEvents == 1, "events " + stairEvents));

            int at2 = SongOps.AddStairIsland(SM.ColumnFirst(3), true, 4);
            Check("on a DOMINANT (G7) the type is SPARK — the ♭9 fall into the next chord; the runner is the group given (bass)", () =>
            {
                var kb = at2 >= 0 ? SM.Islands[at2] : null;
                if (kb == null) return "returned " + at2;
                var cs = CubesOn(kb);
                var pitches = SM.StairPitches(kb);
                bool falling = pitches.Length == 4; for (int i = 1; i < pitches.Length; i++) if (pitches[i] >= pitches[i - 1]) falling = false;
                return Ok(kb.stairType == 2 && cs.Count == 1 && cs[0].instrument == 4 && falling, "type " + kb.stairType + " (" + Harmony.StairTypeWord(kb.stairType) + "), runner group " + (cs.Count > 0 ? cs[0].instrument : -1) + ", pitches [" + Arr(pitches) + "]");
            });
            int hb = H;
            History.Undo();
            yield return Frames(2);
            Check("undo: the second stairs island is gone (one entry back)", () =>
            {
                int n = SM.Islands.Count(k => k.IsStairs);
                return Ok(n == 1 && H == hb - 1 && SM.ColumnIslands(3).Count == 1, "stairs islands " + n + ", history " + H + " (was " + hb + "), column 3 holds " + SM.ColumnIslands(3).Count);
            });

            int si = SM.Islands.FindIndex(k => k.IsStairs);
            h0 = H; stairEvents = 0;
            float g1 = SM.GroundOf(1), g2 = SM.GroundOf(2);
            var before = StateOf(CubesOn(SM.Islands[si]).FirstOrDefault());
            SongOps.SetStair(si, 1, 1, 6, 6, 0);
            yield return Frames(2);
            Check("SetStair(scale, climbing, 6 steps, sixteenths, from the start): the run changes, the runner re-derived = StairPath (6 steps, the last holding), one History entry, OnStairChanged", () =>
            {
                var kb = SM.Islands.FirstOrDefault(k => k.IsStairs);
                if (kb == null) return "no stairs";
                var s = StateOf(CubesOn(kb).FirstOrDefault());
                int[] xs, mods, durs; SM.StairPath(kb, out xs, out mods, out durs);
                bool ok = kb.stairType == 1 && kb.stairDir == 1 && kb.stairSteps == 6 && kb.stairRate == 6 && !kb.stairLead && s != null && Same(s.xs, xs) && Same(s.durs, durs)
                    && s.xs.Length == 6 && s.durs.Sum() == 96 && !Same(s.xs, before.xs) && H == h0 + 1 && stairEvents == 1;
                return Ok(ok, "type " + kb.stairType + " dir " + kb.stairDir + " steps " + kb.stairSteps + " rate " + kb.stairRate + " lead " + kb.stairLead + "; runner xs [" + Arr(s != null ? s.xs : null) + "] durs [" + Arr(s != null ? s.durs : null) + "] (was xs [" + Arr(before.xs) + "]); history +" + (H - h0) + ", events " + stairEvents);
            });
            Check("the ground after it follows (K): the next column stands 6 × StairStepRise higher", () =>
            {
                float rise = SM.GroundOf(2) - SM.GroundOf(1);
                return Ok(Mathf.Abs(rise - 6 * ProjectConfig.StairStepRise) < 1e-3f, "GroundOf(1) " + F(SM.GroundOf(1)) + " → GroundOf(2) " + F(SM.GroundOf(2)) + " (before " + F(g1) + " → " + F(g2) + ")");
            });
            h0 = H;
            SongOps.SetStair(si, -1, 0, 0, 0, -1);
            yield return Frames(1);
            Check("SetStair with nothing to change: no History entry", () => Ok(H == h0, "history +" + (H - h0)));

            h0 = H;
            int at3 = SongOps.AddStairIsland(-1, false, -1);
            yield return Frames(2);
            Check("AddStairIsland(−1): a new column after the last — the landing rule starts a new section (the last one has 4 measures) — with its runner; one History entry", () =>
            {
                var kb = at3 >= 0 ? SM.Islands[at3] : null;
                return Ok(kb != null && kb.IsStairs && kb.column == 8 && SM.ColumnCount == 9 && SM.ColumnIslands(8).Count == 1 && Secs() == "0,4,8" && CubesOn(kb).Count == 1 && H == h0 + 1,
                    "index " + at3 + " column " + (kb != null ? kb.column : -1) + ", columns " + SM.ColumnCount + ", sections " + Secs() + ", runners " + (kb != null ? CubesOn(kb).Count : 0) + ", history +" + (H - h0));
            });
            if (captures) yield return Shot("o7_stairs.png", ColumnsBounds(0, 3));
        }
        finally { SM.OnStairChanged -= onStair; }

        LoadFixture();
        yield return Frames(2);
        int h1 = H;
        int at4 = SongOps.InsertStairColumn(2, -1);
        yield return Frames(2);
        Check("InsertStairColumn(2): a stairs-only column inside section A joins it (sections 0,5), with its runner; one History entry", () =>
        {
            var kb = at4 >= 0 ? SM.Islands[at4] : null;
            return Ok(kb != null && kb.IsStairs && kb.column == 2 && SM.ColumnCount == 9 && SM.ColumnIslands(2).Count == 1 && Secs() == "0,5" && CubesOn(kb).Count == 1 && H == h1 + 1,
                "index " + at4 + " column " + (kb != null ? kb.column : -1) + ", columns " + SM.ColumnCount + ", sections " + Secs() + ", history +" + (H - h1));
        });
        Check("RefreshRunners: a stairs island made 2 bars long gets its runner re-derived one pass (2 bars) long", () =>
        {
            var st = SongState.Capture();
            int i = Array.FindIndex(st.measures, m => m.kind == 3);
            st.measures[i].bars = 2;
            int n = SongOps.RefreshRunners(st);
            var c = st.cubes.First(x => x.moon < 0 && x.measure == i);
            return Ok(n == 1 && SumDurs(c) == 2 * 4 * ProjectConfig.TicksPerBeat, "changed " + n + ", Σ " + SumDurs(c) + " ticks");
        });
    }

    // ------------------------------------------------------------------ PHRASES
    static IEnumerator Phrases(bool captures)
    {
        LoadFixture();
        yield return Frames(2);
        int h0 = H;
        int pi = SongOps.AddPhrase(0, -1);
        yield return Frames(2);
        Check("AddPhrase(0): a ONE-measure phrase (kind 4) last in column 0 — offset 0, eighths, no cube; the hand holds the lead group; one History entry", () =>
        {
            var kb = pi >= 0 ? SM.Islands[pi] : null;
            var pm = PathManager.I;
            if (kb == null) return "returned " + pi;
            var col = SM.ColumnIslands(0);
            return Ok(kb.IsPhrase && kb.column == 0 && col[col.Count - 1] == kb && kb.phraseBeats == 4 && kb.phraseOffset == 0 && kb.phraseGrid == 12 && CubesOn(kb).Count == 0
                && pm.CubeInHand && pm.selectedInstrument == SongOps.LeadGroup && H == h0 + 1 && SongOps.GrownPhrase == kb && Secs() == "0,4" && SM.ColumnCount == 8,
                "index " + pi + ", beats " + kb.phraseBeats + ", cubes " + CubesOn(kb).Count + ", hand " + pm.Hand + " group " + pm.selectedInstrument + ", history +" + (H - h0) + ", sections " + Secs());
        });
        if (PathManager.I != null) PathManager.I.PutDown();
        PutNotes(SM.Islands[pi], 3, new[] { new[] { 0, 5, 12 }, new[] { 24, 7, 24 }, new[] { 72, 9, 12 } });
        yield return Frames(1);
        Check("(setup) three notes drawn on it", () => { var c = StateOf(CubesOn(SM.Islands[pi]).FirstOrDefault()); return Ok(Notes(c) == "0:5x12 24:7x24 72:9x12" && SumDurs(c) == 96, "notes " + Notes(c) + ", Σ " + SumDurs(c)); });

        h0 = H;
        SongOps.SetPhraseLength(SM.Islands[pi], 2);
        yield return Frames(2);
        Check("SetPhraseLength(½ measure): the note past the end dropped, the path re-padded to 48 ticks; one History entry", () =>
        {
            var kb = SongOps.GrownPhrase; var c = StateOf(CubesOn(kb).FirstOrDefault());
            return Ok(kb.phraseBeats == 2 && Notes(c) == "0:5x12 24:7x24" && SumDurs(c) == 48 && H == h0 + 1, "beats " + kb.phraseBeats + ", notes " + Notes(c) + ", Σ " + SumDurs(c) + ", history +" + (H - h0));
        });
        SongOps.SetPhraseLength(SongOps.GrownPhrase, 4);
        yield return Frames(2);
        Check("SetPhraseLength(1 measure): the trailing rest re-padded to 96 ticks, the notes kept", () =>
        {
            var c = StateOf(CubesOn(SongOps.GrownPhrase).FirstOrDefault());
            return Ok(SongOps.GrownPhrase.phraseBeats == 4 && SumDurs(c) == 96 && Notes(c) == "0:5x12 24:7x24", "beats " + SongOps.GrownPhrase.phraseBeats + ", Σ " + SumDurs(c) + ", notes " + Notes(c));
        });

        h0 = H;
        var old = SongOps.GrownPhrase;
        bool grew = SongOps.GrowPhrase(old, 5);
        yield return Frames(2);
        Check("GrowPhrase(5 beats) → 2 measures (the next of ½ · 1 · 2 · 4 · 8) inside its section: sections kept, NO History entry, the rebuilt phrase in GrownPhrase, the path re-padded", () =>
        {
            var kb = SongOps.GrownPhrase; var c = StateOf(CubesOn(kb).FirstOrDefault());
            return Ok(grew && kb != null && kb != old && kb.IsPhrase && kb.phraseBeats == 8 && Secs() == "0,4" && H == h0 && SumDurs(c) == 192 && Notes(c) == "0:5x12 24:7x24",
                "grew " + grew + ", beats " + kb.phraseBeats + ", sections " + Secs() + ", history +" + (H - h0) + ", Σ " + SumDurs(c) + ", notes " + Notes(c));
        });
        grew = SongOps.GrowPhrase(SongOps.GrownPhrase, 12);
        yield return Frames(2);
        Check("GrowPhrase(12 beats) → 4 measures, never 3 (§17.2 musical units); still one section", () => Ok(grew && SongOps.GrownPhrase.phraseBeats == 16 && Secs() == "0,4", "beats " + SongOps.GrownPhrase.phraseBeats + ", sections " + Secs()));
        bool again = SongOps.GrowPhrase(SongOps.GrownPhrase, 16);
        Check("GrowPhrase to the length it has: false (nothing rebuilt)", () => Ok(!again, "returned " + again));
        History.Push();   // the stroke ends

        // past its section: section B's first measures join section A; B keeps its name
        LoadFixture();
        yield return Frames(2);
        SongOps.SetSectionRole(1, 3);
        int p2 = SongOps.AddPhrase(2, 3);
        yield return Frames(2);
        h0 = H;
        grew = SongOps.GrowPhrase(SM.Islands[p2], 12);
        yield return Frames(2);
        Check("a phrase at column 2 grown to 4 measures runs past section A's end: A takes columns 4 and 5 (sections 0,6: ONE grid over four chords), B goes on from 6 with its name; no History entry", () =>
        {
            var kb = SongOps.GrownPhrase;
            return Ok(grew && kb.phraseBeats == 16 && kb.column == 2 && Secs() == "0,6" && SM.ColumnCount == 8 && H == h0 && SM.SectionRole(1) == 3 && SM.SectionRole(0) == 0,
                "beats " + kb.phraseBeats + ", column " + kb.column + ", sections " + Secs() + ", columns " + SM.ColumnCount + ", roles " + SM.SectionRole(0) + "/" + SM.SectionRole(1) + ", history +" + (H - h0));
        });
        if (captures) yield return Shot("o7_phrase_grown.png", ColumnsBounds(0, 7));
        History.Push();

        // past the song's end: measures appended with SuggestNext's chords, in the last section
        int lastRoot = Anchor(7).chordRootMIDI;
        var exp1 = MusicTheory.SuggestNext(MusicTheory.KeyOfSong(), lastRoot, 1)[0];
        var exp2 = MusicTheory.SuggestNext(MusicTheory.KeyOfSong(), exp1.chordRootMIDI, 1)[0];
        int p3 = SongOps.AddPhrase(6, 3);
        yield return Frames(2);
        h0 = H;
        grew = SongOps.GrowPhrase(SM.Islands[p3], 16);
        yield return Frames(2);
        Check("a phrase at column 6 grown to 4 measures runs past the song's end: two one-bar measures appended (SuggestNext's chords) in the last section; no History entry", () =>
        {
            var kb = SongOps.GrownPhrase; var a8 = Anchor(8); var a9 = Anchor(9);
            return Ok(grew && SM.ColumnCount == 10 && a8 != null && a9 != null && Pc(a8.chordRootMIDI) == Pc(exp1.chordRootMIDI) && Pc(a9.chordRootMIDI) == Pc(exp2.chordRootMIDI) && a8.bars == 1 && a9.bars == 1
                && Secs() == "0,6" && H == h0 && kb.column == 6 && kb.phraseBeats == 16,
                "columns " + SM.ColumnCount + ", appended " + ChordSig(a8) + " " + ChordSig(a9) + " (SuggestNext roots " + Pc(exp1.chordRootMIDI) + ", " + Pc(exp2.chordRootMIDI) + "), sections " + Secs() + ", history +" + (H - h0));
        });
        History.Push();
        int hs = H;
        History.Undo();
        yield return Frames(2);
        Check("undo of that stroke: the appended measures and the growth go (one entry)", () => Ok(SM.ColumnCount == 8 && H == hs - 1 && SM.Islands.Where(k => k.IsPhrase).All(k => k.column != 6 || k.phraseBeats == 4), "columns " + SM.ColumnCount + ", history " + H + " (was " + hs + ")"));

        // double, move, cells
        LoadFixture();
        yield return Frames(2);
        int p4 = SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        PutNotes(SM.Islands[p4], 3, new[] { new[] { 0, 5, 12 }, new[] { 24, 7, 24 } });
        yield return Frames(1);
        h0 = H;
        SongOps.DoublePhrase(SM.Islands[p4]);
        yield return Frames(2);
        Check("DoublePhrase: 2 measures with the notes again in the new half; one History entry", () =>
        {
            var kb = SongOps.GrownPhrase; var c = StateOf(CubesOn(kb).FirstOrDefault());
            return Ok(kb.phraseBeats == 8 && Notes(c) == "0:5x12 24:7x24 96:5x12 120:7x24" && SumDurs(c) == 192 && H == h0 + 1, "beats " + kb.phraseBeats + ", notes " + Notes(c) + ", Σ " + SumDurs(c) + ", history +" + (H - h0));
        });
        string notes0 = Notes(StateOf(CubesOn(SongOps.GrownPhrase).FirstOrDefault()));
        h0 = H;
        SongOps.MovePhrase(SongOps.GrownPhrase, 5, 48);
        yield return Frames(2);
        Check("MovePhrase(column 5, half a measure in): it starts there, its notes unchanged; one History entry", () =>
        {
            var kb = SongOps.GrownPhrase; var c = StateOf(CubesOn(kb).FirstOrDefault());
            return Ok(kb.column == 5 && kb.phraseOffset == 48 && Notes(c) == notes0 && H == h0 + 1 && Secs() == "0,4", "column " + kb.column + " offset " + kb.phraseOffset + ", notes " + Notes(c) + ", sections " + Secs() + ", history +" + (H - h0));
        });
        SongOps.MovePhrase(SongOps.GrownPhrase, 5, 100);
        yield return Frames(2);
        Check("an offset past its column's end (100 → 96 ticks, snapped to half measures) continues in the next column", () => Ok(SongOps.GrownPhrase.column == 6 && SongOps.GrownPhrase.phraseOffset == 0, "column " + SongOps.GrownPhrase.column + " offset " + SongOps.GrownPhrase.phraseOffset));
        h0 = H;
        SongOps.SetPhraseGrid(SongOps.GrownPhrase, 6);
        yield return Frames(2);
        Check("SetPhraseGrid(sixteenths): the notes keep their times on twice the cells; one History entry", () =>
        {
            var kb = SongOps.GrownPhrase; var c = StateOf(CubesOn(kb).FirstOrDefault());
            var mods = c.ModsOrDerived(); var noteXs = new List<int>();
            for (int i = 0; i < c.xs.Length; i++) if (mods[i] != 1) noteXs.Add(c.xs[i]);
            return Ok(kb.phraseGrid == 6 && Notes(c) == notes0 && Arr(noteXs) == "0,4,16,20" && H == h0 + 1, "grid " + kb.phraseGrid + ", notes " + Notes(c) + ", note cells [" + Arr(noteXs) + "], history +" + (H - h0));
        });
    }

    // ------------------------------------------------------------------ SECTIONS
    static IEnumerator Sections(bool captures)
    {
        LoadFixture();
        yield return Frames(2);
        int h0 = H;
        SongOps.SplitSectionAt(2);
        yield return Frames(2);
        Check("SplitSectionAt(2): sections 0,2,4; one History entry", () => Ok(Secs() == "0,2,4" && SM.ColumnCount == 8 && H == h0 + 1, "sections " + Secs() + ", history +" + (H - h0)));
        h0 = H;
        SongOps.SplitSectionAt(4);
        yield return Frames(1);
        Check("SplitSectionAt(4) — already a section start: refused, nothing changes", () => Ok(Secs() == "0,2,4" && H == h0, "sections " + Secs() + ", history +" + (H - h0)));
        h0 = H;
        SongOps.JoinSectionAt(2);
        yield return Frames(2);
        Check("JoinSectionAt(2) — the boundary AT column 2 goes (U1: JoinSectionAt(SectionFirst(s + 1))): sections 0,4; one History entry", () => Ok(Secs() == "0,4" && H == h0 + 1, "sections " + Secs() + ", history +" + (H - h0)));
        h0 = H;
        SongOps.JoinSectionAt(1);
        yield return Frames(2);
        Check("JoinSectionAt(1) — not a section start: its section joins the next (one section of 8 measures); one History entry", () => Ok(Secs() == "0" && SM.SectionBars(0) == 8 && H == h0 + 1, "sections " + Secs() + ", measures " + SM.SectionBars(0) + ", history +" + (H - h0)));

        LoadFixture();
        yield return Frames(2);
        int ph = SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        SongOps.GrowPhrase(SM.Islands[ph], 8);
        History.Push();
        yield return Frames(2);
        h0 = H;
        SongOps.SplitSectionAt(1);
        yield return Frames(1);
        Check("a split under a two-measure phrase (columns 0–1) is refused: a phrase never crosses a section boundary", () => Ok(Secs() == "0,4" && H == h0, "sections " + Secs() + ", history +" + (H - h0)));

        LoadFixture();
        yield return Frames(2);
        int secEvents = 0;
        Action onSec = () => secEvents++;
        SM.OnSectionsChanged += onSec;
        h0 = H;
        SongOps.SetSectionRole(1, 3);
        SM.OnSectionsChanged -= onSec;
        Check("SetSectionRole(1, chorus): SongManager.SectionRole(1) = 3, OnSectionsChanged; one History entry", () => Ok(SM.SectionRole(1) == 3 && SM.SectionRole(0) == 0 && secEvents >= 1 && H == h0 + 1, "roles " + SM.SectionRole(0) + "/" + SM.SectionRole(1) + ", events " + secEvents + ", history +" + (H - h0)));

        h0 = H;
        int bass0 = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.instrument == 4);
        string chordsB = ColumnChords(4, 7);
        int ns = SongOps.DuplicateSection(1);
        yield return Frames(2);
        Check("DuplicateSection(1): its 4 columns copied right after it as a NEW section (0,4,8) — the same chords (so the same letter), the name kept, its bass cube copied with a new id; one History entry", () =>
        {
            var ids = SequenceMaster.Cubes.Where(c => c != null && c.isFinalized).Select(c => c.id).ToList();
            int bass = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.instrument == 4);
            bool onCopy = CubesOn(Anchor(8)).Any(c => c.instrument == 4);
            return Ok(ns == 2 && SM.ColumnCount == 12 && Secs() == "0,4,8" && ColumnChords(8, 11) == chordsB && SM.SectionRole(2) == 3 && bass == bass0 + 1 && onCopy && ids.Distinct().Count() == ids.Count && H == h0 + 1,
                "returned " + ns + ", columns " + SM.ColumnCount + ", sections " + Secs() + ", copy [" + ColumnChords(8, 11) + "] = B [" + chordsB + "], role " + SM.SectionRole(2) + ", bass cubes " + bass0 + " → " + bass + ", ids unique " + (ids.Distinct().Count() == ids.Count) + ", history +" + (H - h0));
        });
        if (captures) yield return Shot("o7_section_dup.png", ColumnsBounds(4, 11));
        h0 = H;
        SongOps.DeleteSection(2);
        yield return Frames(2);
        Check("DeleteSection(2): back to 8 columns (sections 0,4), the copied cube gone; one History entry", () =>
        {
            int bass = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.instrument == 4);
            return Ok(SM.ColumnCount == 8 && Secs() == "0,4" && bass == bass0 && H == h0 + 1, "columns " + SM.ColumnCount + ", sections " + Secs() + ", bass cubes " + bass + ", history +" + (H - h0));
        });

        string chordsA = ColumnChords(0, 3);
        h0 = H;
        SongOps.MoveSection(1, 0);
        yield return Frames(2);
        Check("MoveSection(1 → 0): B (Dm G7 C F) first, A after it; B's name and bass cube travel with it; one History entry", () =>
        {
            bool bassFirst = CubesOn(Anchor(0)).Any(c => c.instrument == 4);
            return Ok(ColumnChords(0, 3) == chordsB && ColumnChords(4, 7) == chordsA && Secs() == "0,4" && SM.SectionRole(0) == 3 && SM.SectionRole(1) == 0 && bassFirst && H == h0 + 1,
                "first [" + ColumnChords(0, 3) + "] then [" + ColumnChords(4, 7) + "], sections " + Secs() + ", roles " + SM.SectionRole(0) + "/" + SM.SectionRole(1) + ", bass on column 0 " + bassFirst + ", history +" + (H - h0));
        });
        Info("the Moon's start column after the move: " + SM.MoonStartColumn(0) + " (SongManager's remap: a Moon follows the islands of its start column)");
        int hm = H;
        History.Undo();
        yield return Frames(2);
        Check("undo: A first again, B's name on section 1", () => Ok(ColumnChords(0, 3) == chordsA && SM.SectionRole(1) == 3 && H == hm - 1, "first [" + ColumnChords(0, 3) + "], role " + SM.SectionRole(1)));

        SongOps.JoinSectionAt(4);
        yield return Frames(2);
        h0 = H;
        SongOps.DeleteSection(0);
        yield return Frames(1);
        Check("DeleteSection of the only section is refused (the song keeps one)", () => Ok(SM.ColumnCount == 8 && H == h0, "columns " + SM.ColumnCount + ", history +" + (H - h0)));
    }

    // ------------------------------------------------------------------ MANY GRIDS
    static IEnumerator ManyGrids(bool captures)
    {
        LoadFixture();
        yield return Frames(2);
        int changes = 0;
        Action onSel = () => changes++;
        GridSelection.OnChanged += onSel;
        try
        {
            GridSelection.SelectSection(1);
            Check("GridSelection.SelectSection(1): its 4 grids in song order, no Moon; IsWholeSection → 1; OnChanged", () =>
            {
                int s; bool whole = GridSelection.IsWholeSection(out s);
                var sel = GridSelection.Selected;
                return Ok(sel.Count == 4 && sel.Select(k => k.column).SequenceEqual(new[] { 4, 5, 6, 7 }) && !sel.Any(k => k.IsMoon) && whole && s == 1 && changes == 1 && GridSelection.LastColumn == 7,
                    "count " + sel.Count + ", columns [" + Arr(sel.Select(k => k.column)) + "], whole " + whole + " (" + s + "), events " + changes);
            });
            GridSelection.Toggle(Anchor(5));
            int afterOut = GridSelection.Count;
            GridSelection.Toggle(Anchor(5));
            Check("Toggle: a grid out, then in again (song order kept)", () => Ok(afterOut == 3 && GridSelection.Count == 4 && GridSelection.Selected[1] == Anchor(5), "out " + afterOut + ", in " + GridSelection.Count));
            GridSelection.Clear();
            Check("Clear: none", () => Ok(!GridSelection.Any && changes == 4, "count " + GridSelection.Count + ", events " + changes));
        }
        finally { GridSelection.OnChanged -= onSel; }

        int h0 = H;
        bool copied = SongOps.CopyGrids(Grids(0, 3));
        Check("CopyGrids(section A): the grid clipboard holds 4 grids in 4 columns; the song does not change (no History entry)", () =>
            Ok(copied && SongOps.HasCopiedGrids && SongOps.CopiedGridCount == 4 && SongOps.CopiedColumnCount == 4 && H == h0, "copied " + copied + ", grids " + SongOps.CopiedGridCount + ", columns " + SongOps.CopiedColumnCount + ", history +" + (H - h0)));
        string chordsA = ColumnChords(0, 3);
        h0 = H;
        var pasted = SongOps.PasteGrids(8);
        yield return Frames(2);
        Check("PasteGrids(8) at the end: 4 new columns — a NEW section (the last one is full: 0,4,8) — the same chords, the keys cube copied (same path, new id), the pasted grids selected; one History entry", () =>
        {
            var copy = CubesOn(Anchor(8)).FirstOrDefault(c => c.instrument == 0);
            var orig = CubesOn(Anchor(0)).FirstOrDefault(c => c.instrument == 0);
            bool path = copy != null && orig != null && Same(copy.ToState().xs, orig.ToState().xs) && Same(copy.ToState().zs, orig.ToState().zs) && Same(copy.ToState().durs, orig.ToState().durs) && copy.id != orig.id;
            var sel = GridSelection.Selected;
            return Ok(pasted.Count == 4 && SM.ColumnCount == 12 && Secs() == "0,4,8" && ColumnChords(8, 11) == chordsA && path && sel.Count == 4 && sel.All(k => k.column >= 8) && H == h0 + 1,
                "pasted " + pasted.Count + ", columns " + SM.ColumnCount + ", sections " + Secs() + ", chords [" + ColumnChords(8, 11) + "], keys copy " + path + " (ids " + (orig != null ? orig.id : -1) + " → " + (copy != null ? copy.id : -1) + "), selected " + sel.Count + ", history +" + (H - h0));
        });
        if (captures) yield return Shot("o7_paste.png", ColumnsBounds(4, 11));
        int hp = H;
        History.Undo();
        yield return Frames(2);
        Check("undo: 8 columns, sections 0,4; the selection lost the pasted grids", () => Ok(SM.ColumnCount == 8 && Secs() == "0,4" && H == hp - 1 && !GridSelection.Any, "columns " + SM.ColumnCount + ", sections " + Secs() + ", selected " + GridSelection.Count));
        h0 = H;
        SongOps.PasteGrids(2);
        yield return Frames(2);
        Check("PasteGrids(2) inside section A: the 4 columns join it (sections 0,8); one History entry", () => Ok(SM.ColumnCount == 12 && Secs() == "0,8" && ColumnChords(2, 5) == chordsA && H == h0 + 1, "columns " + SM.ColumnCount + ", sections " + Secs() + ", chords 2..5 [" + ColumnChords(2, 5) + "], history +" + (H - h0)));

        LoadFixture();
        yield return Frames(2);
        string c0 = ChordSig(Anchor(0)), c3 = ChordSig(Anchor(3));
        GridSelection.Set(new List<KeyBlock> { Anchor(1), Anchor(2) });
        h0 = H;
        SongOps.DeleteGrids(GridSelection.Selected.ToList());
        yield return Frames(2);
        Check("DeleteGrids(columns 1, 2): 6 columns, section A keeps 2 (sections 0,2), the selection cleared; one History entry", () =>
            Ok(SM.ColumnCount == 6 && Secs() == "0,2" && ChordSig(Anchor(0)) == c0 && ChordSig(Anchor(1)) == c3 && !GridSelection.Any && H == h0 + 1, "columns " + SM.ColumnCount + ", sections " + Secs() + ", first [" + ColumnChords(0, 1) + "], selected " + GridSelection.Count + ", history +" + (H - h0)));
        h0 = H;
        SongOps.DeleteGrids(SM.Islands.ToList());
        yield return Frames(1);
        Check("deleting every grid is refused (the song keeps one): nothing changes", () => Ok(SM.ColumnCount == 6 && H == h0, "columns " + SM.ColumnCount + ", history +" + (H - h0)));

        LoadFixture();
        yield return Frames(2);
        GridSelection.SelectSection(1);
        int midiB = Anchor(4).GetTile(0, 0).midi, midiA = Anchor(0).GetTile(0, 0).midi;
        h0 = H;
        SongOps.TransposeGrids(GridSelection.Selected.ToList(), 1);
        yield return Frames(2);
        Check("TransposeGrids(section B, +1): its grids' register 1 (tiles +12), section A untouched; the selection follows the rebuilt grids; one History entry", () =>
        {
            var sel = GridSelection.Selected;
            return Ok(Grids(4, 7).All(k => k.register == 1) && Grids(0, 3).All(k => k.register == 0) && Anchor(4).GetTile(0, 0).midi == midiB + 12 && Anchor(0).GetTile(0, 0).midi == midiA
                && sel.Count == 4 && sel.All(k => k.register == 1 && SM.Islands.Contains(k)) && H == h0 + 1,
                "registers B [" + Arr(Grids(4, 7).Select(k => k.register)) + "] A [" + Arr(Grids(0, 3).Select(k => k.register)) + "], tile " + midiB + " → " + Anchor(4).GetTile(0, 0).midi + ", selected " + sel.Count + ", history +" + (H - h0));
        });
        SongOps.TransposeGrids(GridSelection.Selected.ToList(), 1);
        yield return Frames(2);
        h0 = H;
        SongOps.TransposeGrids(GridSelection.Selected.ToList(), 1);
        yield return Frames(1);
        Check("at +2 another octave up is refused (register −2..+2): no History entry", () => Ok(Grids(4, 7).All(k => k.register == 2) && H == h0, "registers [" + Arr(Grids(4, 7).Select(k => k.register)) + "], history +" + (H - h0)));
        History.Undo();
        yield return Frames(2);
        Check("undo (a rebuild O did not do): the selection stays on the same grids", () => Ok(GridSelection.Count == 4 && GridSelection.Selected.All(k => k.column >= 4 && k.register == 1 && SM.Islands.Contains(k)), "selected " + GridSelection.Count + " registers [" + Arr(GridSelection.Selected.Select(k => k.register)) + "]"));

        LoadFixture();
        yield return Frames(2);
        string chords12 = ColumnChords(1, 2);
        h0 = H;
        var dup = SongOps.DuplicateGrids(new List<KeyBlock> { Anchor(1), Anchor(2) });
        yield return Frames(2);
        Check("DuplicateGrids(columns 1, 2): copies right after the last selected column (3, 4), joined into section A (sections 0,6), the copies selected; one History entry", () =>
            Ok(dup.Count == 2 && SM.ColumnCount == 10 && ColumnChords(3, 4) == chords12 && Secs() == "0,6" && GridSelection.Count == 2 && GridSelection.Selected.All(k => k.column == 3 || k.column == 4) && H == h0 + 1,
               "copies " + dup.Count + ", columns " + SM.ColumnCount + ", chords 3..4 [" + ColumnChords(3, 4) + "] (source [" + chords12 + "]), sections " + Secs() + ", selected " + GridSelection.Count + ", history +" + (H - h0)));

        LoadFixture();
        yield return Frames(2);
        h0 = H;
        SongOps.SetRepeatGrids(Grids(0, 3), 3);
        yield return Frames(2);
        Check("SetRepeatGrids(section A, 3): each plays 3 passes (the columns' passes 3); one History entry", () => Ok(Grids(0, 3).All(k => k.repeat == 3) && SM.ColumnPasses(0) == 3 && SM.ColumnPasses(4) == 1 && H == h0 + 1, "repeats [" + Arr(Grids(0, 3).Select(k => k.repeat)) + "], passes " + SM.ColumnPasses(0) + ", history +" + (H - h0)));

        int carryEvents = 0, launchEvents = 0;
        Action<KeyBlock> onCarry = kb => carryEvents++, onLaunch = kb => launchEvents++;
        SM.OnCarryChanged += onCarry; SM.OnLaunchChanged += onLaunch;
        try
        {
            h0 = H;
            SongOps.SetFlowGrids(Grids(4, 7), 1);
            yield return Frames(2);
            Check("SetFlowGrids(section B, on): its first grid flows over the next 3 chords (carry 3, flow style), the others untouched; OnCarryChanged; one History entry", () =>
                Ok(Anchor(4).carry == 3 && Anchor(4).carryStyle == 1 && Grids(5, 7).All(k => k.carry == 0) && carryEvents >= 1 && H == h0 + 1, "carry " + Anchor(4).carry + " style " + Anchor(4).carryStyle + ", others [" + Arr(Grids(5, 7).Select(k => k.carry)) + "], events " + carryEvents + ", history +" + (H - h0)));
            h0 = H;
            SongOps.SetFlowGrids(Grids(4, 7), 0);
            yield return Frames(2);
            Check("SetFlowGrids(section B, off): carry 0; one History entry", () => Ok(Grids(4, 7).All(k => k.carry == 0) && H == h0 + 1, "carries [" + Arr(Grids(4, 7).Select(k => k.carry)) + "], history +" + (H - h0)));
            h0 = H;
            SongOps.SetLaunchGrids(Grids(4, 7), true);
            yield return Frames(2);
            Check("SetLaunchGrids(section B, on): only its last column's grid launches (into what follows); OnLaunchChanged; one History entry", () =>
                Ok(Anchor(7).launch && Grids(4, 6).All(k => !k.launch) && launchEvents >= 1 && H == h0 + 1, "launch [" + string.Join(",", Grids(4, 7).Select(k => k.launch ? "1" : "0").ToArray()) + "], events " + launchEvents + ", history +" + (H - h0)));
            SongOps.SetLaunchGrids(Grids(4, 7), false);
            yield return Frames(2);
            Check("SetLaunchGrids(section B, off): none launches", () => Ok(Grids(4, 7).All(k => !k.launch), "launch [" + string.Join(",", Grids(4, 7).Select(k => k.launch ? "1" : "0").ToArray()) + "]"));
        }
        finally { SM.OnCarryChanged -= onCarry; SM.OnLaunchChanged -= onLaunch; }

        LoadFixture();
        yield return Frames(2);
        var withMoon = Grids(0, 3); withMoon.Add(SM.Moons[0]);
        SongOps.CopyGrids(withMoon);
        var pasted2 = SongOps.PasteGrids(8);
        yield return Frames(2);
        Check("a selected Moon is copied and pasted too: a second Moon starting at the pasted section's first column, with its kick, selected", () =>
        {
            var m2 = SM.Moons.Count > 1 ? SM.Moons[1] : null;
            int kicks = m2 != null ? SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.Moon == m2) : 0;
            return Ok(m2 != null && SM.MoonStartColumn(1) == 8 && kicks == 1 && pasted2.Contains(m2) && GridSelection.Contains(m2), "moons " + SM.Moons.Count + ", start " + SM.MoonStartColumn(1) + ", kicks " + kicks + ", selected " + GridSelection.Count);
        });
        h0 = H;
        SongOps.DeleteGrids(new List<KeyBlock> { SM.Moons[1] });
        yield return Frames(2);
        Check("DeleteGrids(that Moon): one Moon left with its kick, the islands untouched; one History entry", () =>
        {
            int kicks = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.IsOnMoon);
            return Ok(SM.Moons.Count == 1 && kicks == 1 && SM.ColumnCount == 12 && H == h0 + 1, "moons " + SM.Moons.Count + ", Moon cubes " + kicks + ", columns " + SM.ColumnCount + ", history +" + (H - h0));
        });

        LoadFixture();
        yield return Frames(2);
        int pp = SongOps.AddPhrase(1, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        PutNotes(SM.Islands[pp], 3, new[] { new[] { 0, 4, 24 }, new[] { 48, 6, 24 } });
        yield return Frames(1);
        string pn = Notes(StateOf(CubesOn(SM.Islands[pp]).FirstOrDefault()));
        h0 = H;
        var dupP = SongOps.DuplicateGrids(new List<KeyBlock> { SM.Islands[pp] });
        yield return Frames(2);
        Check("DuplicateGrids(a one-measure melody): the copy starts where it ends (column 2, offset 0), notes and all; one History entry", () =>
        {
            var d = dupP.Count == 1 ? dupP[0] : null;
            var c = d != null ? StateOf(CubesOn(d).FirstOrDefault()) : null;
            return Ok(d != null && d.IsPhrase && d.column == 2 && d.phraseOffset == 0 && Notes(c) == pn && SM.ColumnCount == 8 && H == h0 + 1, "copy column " + (d != null ? d.column : -1) + " offset " + (d != null ? d.phraseOffset : -1) + ", notes " + Notes(c) + " (source " + pn + "), history +" + (H - h0));
        });
    }

    // ------------------------------------------------------------------ §19.1 capacity: expand / unexpand
    static IEnumerator Capacity()
    {
        LoadFixture();
        yield return Frames(2);
        int h0 = H;
        bool ok = SongOps.ExpandGrid(Anchor(3));
        yield return Frames(2);
        Check("ExpandGrid(a chord grid): its column 2 bars (the section grows past 4 measures, no new boundary); one History entry", () =>
            Ok(ok && Anchor(3).bars == 2 && SM.ColumnBars(3) == 2 && Secs() == "0,4" && SM.SectionBars(0) == 5 && H == h0 + 1 && SongOps.ExpandedGrid == Anchor(3), "grew " + ok + ", bars " + Anchor(3).bars + ", section A " + SM.SectionBars(0) + " measures, sections " + Secs() + ", history +" + (H - h0)));
        int st = SongOps.AddStairIsland(SM.ColumnFirst(5), false, -1);
        yield return Frames(2);
        SongOps.ExpandGrid(SM.Islands[st]);
        yield return Frames(2);
        Check("ExpandGrid(a stairs island): its column 2 bars and its runner re-derived one pass (2 bars) long", () =>
        {
            var kb = SM.Islands.First(k => k.IsStairs);
            var c = StateOf(CubesOn(kb).FirstOrDefault());
            int[] xs, mods, durs; SM.StairPath(kb, out xs, out mods, out durs);
            return Ok(kb.bars == 2 && SumDurs(c) == 192 && Same(c.durs, durs), "bars " + kb.bars + ", runner Σ " + SumDurs(c) + " (StairPath Σ " + durs.Sum() + ")");
        });
        int pi = SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        h0 = H;
        ok = SongOps.ExpandGrid(SM.Islands[pi]);
        yield return Frames(2);
        Check("ExpandGrid(a phrase): + 1 measure; one History entry", () => Ok(ok && SongOps.GrownPhrase.phraseBeats == 8 && H == h0 + 1, "beats " + SongOps.GrownPhrase.phraseBeats + ", history +" + (H - h0)));
        SongOps.GrowPhrase(SongOps.GrownPhrase, 16);
        yield return Frames(2);
        h0 = H;
        bool shrank = SongOps.ShrinkPhraseForDraft(SongOps.GrownPhrase, 5);
        yield return Frames(2);
        Check("ShrinkPhraseForDraft(5 beats): back to 2 measures (the next musical length that holds it), NO History entry", () => Ok(shrank && SongOps.GrownPhrase.phraseBeats == 8 && H == h0, "shrank " + shrank + ", beats " + SongOps.GrownPhrase.phraseBeats + ", history +" + (H - h0)));
        bool never = SongOps.ShrinkPhraseForDraft(SongOps.GrownPhrase, 12);
        Check("ShrinkPhraseForDraft never grows", () => Ok(!never && SongOps.GrownPhrase.phraseBeats == 8, "returned " + never));
    }

    // ------------------------------------------------------------------ edges: phrases riding along, emptied columns, full columns, repeats, glue
    static IEnumerator Edges()
    {
        LoadFixture();
        yield return Frames(2);
        int pi = SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        PutNotes(SM.Islands[pi], 3, new[] { new[] { 0, 3, 24 }, new[] { 24, 5, 48 } });
        yield return Frames(1);
        string pn = Notes(StateOf(CubesOn(SM.Islands[pi]).FirstOrDefault()));
        SongOps.CopyGrids(Grids(0, 3));
        int grids = SongOps.CopiedGridCount, cols = SongOps.CopiedColumnCount;
        SongOps.PasteGrids(8);
        yield return Frames(2);
        Check("a phrase copied with its column's grids rides along: it lands last in the pasted column 8, notes and all", () =>
        {
            var ph = SM.Islands.FirstOrDefault(k => k.IsPhrase && k.column == 8);
            var col = SM.ColumnIslands(8);
            var c = ph != null ? StateOf(CubesOn(ph).FirstOrDefault()) : null;
            return Ok(grids == 5 && cols == 4 && ph != null && col[col.Count - 1] == ph && col[0].kind == 0 && Notes(c) == pn, "copied " + grids + " grids in " + cols + " columns, phrase in column 8 " + (ph != null) + ", column 8 kinds [" + Arr(col.Select(k => k.kind)) + "], notes " + Notes(c));
        });

        LoadFixture();
        yield return Frames(2);
        int p1 = SongOps.AddPhrase(1, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        string next = ChordSig(Anchor(2));
        SongOps.DeleteGrids(new List<KeyBlock> { Anchor(1) });
        yield return Frames(2);
        Check("deleting the only lane of a phrase's column: the column goes, the phrase moves on to the next column (the one that slid into its place)", () =>
        {
            var ph = SM.Islands.FirstOrDefault(k => k.IsPhrase);
            return Ok(SM.ColumnCount == 7 && ph != null && ph.column == 1 && ph.phraseOffset == 0 && ChordSig(Anchor(1)) == next && Secs() == "0,3", "columns " + SM.ColumnCount + ", phrase column " + (ph != null ? ph.column : -1) + ", column 1 [" + ChordSig(Anchor(1)) + "], sections " + Secs());
        });

        LoadFixture();
        yield return Frames(2);
        for (int k = 0; k < 3; k++) { SM.AddIslandInColumn(SM.ColumnFirst(0), k % 2 == 0); yield return Frames(1); }
        int lanes = SM.LaneCount(0);
        int st = SongOps.AddStairIsland(SM.ColumnFirst(0), false, -1);
        yield return Frames(2);
        Check("AddStairIsland beside a full column (4 lanes): a new column after the last instead (a new section: the last one is full)", () =>
        {
            var kb = st >= 0 ? SM.Islands[st] : null;
            return Ok(lanes == ProjectConfig.MaxLanes && kb != null && kb.IsStairs && kb.column == 8 && SM.LaneCount(0) == ProjectConfig.MaxLanes && Secs() == "0,4,8", "lanes " + lanes + ", stairs column " + (kb != null ? kb.column : -1) + ", sections " + Secs());
        });

        LoadFixture();
        yield return Frames(2);
        int pr = SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        SM.SetRepeat(pr, 2);
        yield return Frames(2);
        SongOps.GrowPhrase(SM.Islands.First(k => k.IsPhrase), 8);
        yield return Frames(2);
        string s1 = Secs();
        SongOps.GrowPhrase(SongOps.GrownPhrase, 16);
        yield return Frames(2);
        History.Push();
        Check("a phrase that repeats twice spans beats × repeat: 2 measures × 2 fill section A; 4 × 2 take section B in too (one section)", () =>
            Ok(s1 == "0,4" && SongOps.GrownPhrase.phraseBeats == 16 && SongOps.GrownPhrase.repeat == 2 && Secs() == "0" && SM.ColumnCount == 8, "after 2 measures " + s1 + ", after 4 measures " + Secs() + ", beats " + SongOps.GrownPhrase.phraseBeats + " × " + SongOps.GrownPhrase.repeat));

        var fx = Fixture();
        fx.measures[1].group = 7; fx.measures[2].group = 7; fx.measures[2].pz = fx.measures[1].pz;
        GlobalClock.Stop(); GridSelection.Clear();
        SongState.Apply(fx); History.Reset(); History.Push();
        yield return Frames(2);
        bool glued0 = SM.Glued(1);
        string cA = ChordSig(Anchor(0));
        SongOps.CopyGrids(Grids(0, 0));
        SongOps.PasteGrids(2);
        yield return Frames(2);
        Check("a paste aimed inside a glued run (merge group on columns 1–2) lands past it (column 3) and the glue stays", () =>
            Ok(SM.Glued(1) && ChordSig(Anchor(3)) == cA && SM.ColumnCount == 9 && Secs() == "0,5", "glued 1|2 " + SM.Glued(1) + ", column 3 [" + ChordSig(Anchor(3)) + "], columns " + SM.ColumnCount + ", sections " + Secs()));
        History.Undo();
        yield return Frames(2);
        SongOps.SplitSectionAt(2);
        yield return Frames(2);
        Check("a split through a merge group cuts the glue there (a group lives inside its section): sections 0,2,4, columns 1 | 2 no longer glued", () =>
            Ok(glued0 && !SM.Glued(1) && Secs() == "0,2,4", "glued before " + glued0 + ", after " + SM.Glued(1) + ", groups " + Anchor(1).group + "/" + Anchor(2).group + ", sections " + Secs()));
    }

    // ------------------------------------------------------------------ while the song plays
    static IEnumerator Playing()
    {
        LoadFixture();
        yield return Frames(2);
        SongOps.AddPhrase(0, 3);
        yield return Frames(2);
        if (PathManager.I != null) PathManager.I.PutDown();
        int late0 = Synth.LateEvents, err0 = Synth.Errors, log0 = Synth.LateLogCount;
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return Wait(1.2f);
        int lateQuiet = Synth.LateEvents - late0;
        double b0 = GlobalClock.SongBeatD;
        int lateOps0 = Synth.LateEvents, ops0 = SongOps.OpCount;
        SongOps.SetRepeatGrids(Grids(4, 5), 2); yield return Wait(0.3f);
        SongOps.TransposeGrids(Grids(1, 2), 1); yield return Wait(0.3f);
        SongOps.CopyGrids(Grids(0, 1)); SongOps.PasteGrids(SM.ColumnCount); yield return Wait(0.3f);
        SongOps.SetFlowGrids(Grids(0, 3), 1); yield return Wait(0.3f);
        SongOps.GrowPhrase(SM.Islands.First(k => k.IsPhrase), 8); History.Push(); yield return Wait(0.3f);
        SongOps.AddStairIsland(SM.ColumnFirst(3), false, -1); yield return Wait(0.3f);
        SongOps.SplitSectionAt(2); yield return Wait(0.3f);
        int ops = SongOps.OpCount - ops0;
        int lateOps = Synth.LateEvents - lateOps0, errs = Synth.Errors - err0;
        bool playing = GlobalClock.IsPlaying; double b1 = GlobalClock.SongBeatD;
        int late1 = Synth.LateEvents;
        yield return Wait(1.5f);
        int lateAfter = Synth.LateEvents - late1;
        double b2 = GlobalClock.SongBeatD;
        GlobalClock.Stop();
        Check("ops while the song plays (repeat, transpose, paste, flow, grow, stairs, split): it keeps playing through every rebuild, Synth errors +0, late +0 before / after; during the " + ops + " rebuilds at most one step per re-seek (by design)", () =>
            Ok(playing && errs == 0 && lateQuiet == 0 && lateAfter == 0 && lateOps <= ops && b1 > b0 && b2 > b1, "beat " + b0.ToString("F2") + " → " + b1.ToString("F2") + " → " + b2.ToString("F2") + ", errors +" + errs + ", late quiet " + lateQuiet + " / during the ops " + lateOps + " / after " + lateAfter));
        if (Synth.LateLogCount > log0) Info("late log during the ops: " + Synth.LateLog(log0));
    }
}
