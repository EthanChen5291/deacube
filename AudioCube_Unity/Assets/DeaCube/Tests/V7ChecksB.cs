using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package B (v7: island builds & structure visuals) Play-mode verification, SPEC v7 §2.4 / §3.1 / §13.3 / §14.3 / §17.1: <c>V7ChecksB.RunAll()</c>
/// starts a coroutine; poll <c>V7ChecksB.Done</c> or Captures/b7_report.txt (numbered PASS / FAIL lines, INFO lines with numbers). A test song in
/// C minor (the recording's circle: Fm7 B♭7 E♭maj7 A♭maj7 twice, then Dm7♭5 G7; sections 0 / 4 / 8) with a falling spark stair of 4 on B♭7, a
/// climbing scale stair of 3 on A♭maj7 and a two-measure melody phrase over Fm7 → B♭7. Covers: the stairs (tiles = steps, gridX, colliders tagged
/// "Tile", step tops at −i × rise / (i + 1) × rise, every block down to the base, the base moved under a fall, pitches = SongManager.StairPitches
/// and monotone, the direction glyph on the top step, RefreshStairs), the phrase (cells = beats × 24 / grid, 15 rows, the key's scale over two
/// octaves, linear time cells, measure / beat lines, the piano, two chord segments with their vibe tints and the safe-note dots on exactly the
/// chord-tone rows, PhraseCellAt, a 16ths phrase), the beat pips (4 per measure on every grid but Moons, the downbeat bigger, the pure lighting and
/// the lit pip while playing), the plinths (one per section, letters by content, measure blocks over their columns, numbers 1..n, ghosts of a short
/// section, ruler pips, grounds, PlinthAt / Pick / Bounds, the playhead while playing), frame costs, no console errors. Captures b7_*.png.
/// Runs with PathManager.AutoHand = false. Never writes the user's save; reloads the fixture at the end.
/// </summary>
public static class V7ChecksB
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "b7_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" B7-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); Line(sb, d == null, name, d ?? "ok"); }
        catch (Exception e) { Line(sb, false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    static string F(float v) => v.ToString("F3");
    static string F(double v) => v.ToString("F3");
    static SongManager SM => SongManager.I;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test song
    static readonly int[] Roots = { 53, 58, 63, 56, 53, 58, 63, 56, 62, 55 };
    static readonly string[] Names = { "Fm7", "Bb7", "Ebmaj7", "Abmaj7", "Fm7", "Bb7", "Ebmaj7", "Abmaj7", "Dm7b5", "G7" };
    static int[] SemisOf(string n)
    {
        if (n.EndsWith("maj7")) return new[] { 0, 4, 7, 11 };
        if (n.EndsWith("m7b5")) return new[] { 0, 3, 6, 10 };
        if (n.EndsWith("m7")) return new[] { 0, 3, 7, 10 };
        return new[] { 0, 4, 7, 10 };
    }
    static MeasureState Chord(int col, int i) => new MeasureState { chordKey = Names[i], root = Roots[i], semis = SemisOf(Names[i]), bars = 1, kind = 0, col = col, repeat = 1, energy = 2,
        secRole = col == 0 ? 2 : (col == 4 ? 3 : 0) };   // §17.1: section A = a verse, the second A = a chorus, B unnamed
    static MeasureState Stairs(int col, int dir, int steps, int type) => new MeasureState { chordKey = "Stairs", root = 60, semis = new[] { 0 }, bars = 1, kind = 3, col = col, stairDir = dir, stairSteps = steps, stairType = type, stairRate = 12, stairLead = true, repeat = 1, energy = 2 };
    static MeasureState Keys(int col) => new MeasureState { chordKey = "Keys", root = 60, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2 };
    static MeasureState Phrase(int col, int beats, int grid) => new MeasureState { chordKey = "Melody", root = 60, semis = new[] { 0 }, bars = 1, kind = 4, col = col, phraseBeats = beats, phraseGrid = grid, repeat = 1, energy = 2 };

    /// <summary>The test song (C minor, 10 columns, sections 0 / 4 / 8; a fall of 4 on column 1, a climb of 3 on column 3, a 2-measure phrase at
    /// column 0; <paramref name="phraseBeats"/> / <paramref name="grid"/> vary the phrase).</summary>
    public static SongState TestSong(int phraseBeats = 8, int grid = 12)
    {
        var m = new List<MeasureState>();
        for (int c = 0; c < Roots.Length; c++)
        {
            m.Add(Chord(c, c));
            if (c == 1) m.Add(Stairs(1, -1, 4, 2));
            if (c == 3) m.Add(Stairs(3, 1, 3, 1));
            if (c == 5) m.Add(Keys(5));
            if (c == 0) m.Add(Phrase(0, phraseBeats, grid));
        }
        return new SongState
        {
            version = SongState.CurrentVersion, name = "b7 structure", bpm = 120f, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = new CubeState[0],
            moons = new MeasureState[0], keyTonic = 0, keyMinor = true, sections = new[] { 0, 4, 8 }, tone = 1f, space = 1f
        };
    }

    /// <summary>Loads the test song (sections refreshed at once).</summary>
    public static void LoadTest(int phraseBeats = 8, int grid = 12)
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        SongState.Apply(TestSong(phraseBeats, grid));
        History.Reset(); History.Push();
        SectionPlinth.RefreshNow();
    }

    static List<KeyBlock> Kind(int k) => SM.Islands.Where(kb => kb != null && kb.kind == k).ToList();
    static KeyBlock ChordAt(int col) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == col);

    /// <summary>Exploration helper: loads the test song and frames <paramref name="what"/> (0 overview, 1 the fall, 2 the climb, 3 the phrase,
    /// 4 section A, 5 the short section C), captures Captures/<paramref name="file"/> when given.</summary>
    public static string Demo(int what, string file = null)
    {
        if (SequenceMaster.I == null) return "no play mode";
        SequenceMaster.I.StartCoroutine(DemoRoutine(what, file));
        return "started";
    }
    static IEnumerator DemoRoutine(int what, string file)
    {
        Prepare();
        if (SM.Islands.Count(kb => kb != null && kb.kind == 3) != 2) LoadTest();
        yield return null; yield return null;
        Frame(what);
        yield return Wait(0.8f);
        if (!string.IsNullOrEmpty(file)) yield return Shot(file);
    }

    static void Frame(int what)
    {
        if (OrbitCamera.I == null || SM == null) return;
        Bounds b;
        switch (what)
        {
            case 1: { var s = Kind(3).First(k => k.stairDir < 0); b = s.WorldBounds; b.Encapsulate(ChordAt(s.column).WorldBounds); b.Expand(new Vector3(4f, 0f, 1f)); OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.8f); return; }
            case 2: { var s = Kind(3).First(k => k.stairDir > 0); b = s.WorldBounds; b.Encapsulate(ChordAt(s.column).WorldBounds); b.Expand(new Vector3(4f, 0f, 1f)); OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.8f); return; }
            case 3: { var p = Kind(4).First(); b = p.WorldBounds; OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.85f); return; }
            case 4: b = SectionPlinth.Bounds(0); OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.8f); return;
            case 5: b = SectionPlinth.Bounds(2); OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.9f); return;
            default: b = SM.SongBounds; OrbitCamera.I.FrameBounds(b, 0.1f, true, 1f); return;
        }
    }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(AllRoutine(captures));
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        // v7 (integration): the real mouse is ignored — a cursor resting over the game view hovered a step and lifted it 0.09 mid-check
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void Restore()
    {
        PathManager.SimOnly = false;
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
    }

    static IEnumerator Shot(string file)
    {
        yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator UntilBeat(double b, float timeout = 10f) { float t0 = Time.realtimeSinceStartup; while (GlobalClock.SongBeatD < b && Time.realtimeSinceStartup - t0 < timeout) yield return null; }

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("V7ChecksB ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        Prepare();
        LoadTest();
        yield return null; yield return null; yield return null;
        SectionPlinth.RefreshNow();
        yield return null;

        // ---------------------------------------------------------------- stairs
        var fall = Kind(3).FirstOrDefault(k => k.stairDir < 0);
        var climb = Kind(3).FirstOrDefault(k => k.stairDir > 0);
        Run(sb, "stairs: the song holds a falling stair of 4 (column 1) and a climbing stair of 3 (column 3)", () =>
        {
            if (fall == null || climb == null) return "stairs islands missing: kinds " + string.Join(" ", SM.Islands.Select(k => k.kind + "@" + k.column));
            if (fall.cols != 4 || fall.rows != 1) return "fall grid " + fall.cols + "x" + fall.rows;
            if (climb.cols != 3 || climb.rows != 1) return "climb grid " + climb.cols + "x" + climb.rows;
            return null;
        });
        foreach (var st in new[] { fall, climb })
        {
            if (st == null) continue;
            string tag = st.stairDir < 0 ? "fall" : "climb";
            Run(sb, "stairs (" + tag + "): one tile per step (gridX = step, gridZ 0), tagged Tile, a BoxCollider from its top to the base", () =>
            {
                if (st.tiles.Count != st.cols) return "tiles " + st.tiles.Count;
                float baseTop = KeyBlock.StairBaseTopOf(st.stairDir, st.cols);
                for (int i = 0; i < st.cols; i++)
                {
                    var t = st.GetTile(i, 0);
                    if (t == null) return "no tile " + i;
                    if (t.gridX != i || t.gridZ != 0) return "grid " + t.gridX + "," + t.gridZ;
                    if (!t.CompareTag("Tile")) return "tag " + t.tag;
                    var bc = t.GetComponent<BoxCollider>();
                    if (bc == null) return "collider " + i;
                    // the collider's top and bottom in island-local y
                    float top = t.transform.localPosition.y + bc.center.y + bc.size.y * 0.5f, bottom = t.transform.localPosition.y + bc.center.y - bc.size.y * 0.5f;
                    if (Mathf.Abs(top - st.StairTopY(i)) > 0.01f) return "collider top " + i + " " + F(top) + " vs " + F(st.StairTopY(i));
                    if (Mathf.Abs(bottom - baseTop) > 0.01f) return "collider bottom " + i + " " + F(bottom) + " vs base " + F(baseTop);
                }
                return null;
            });
            Run(sb, "stairs (" + tag + "): the run's ends at " + (st.stairDir < 0 ? "−i" : "(i + 1)") + " × StairStepRise above the root, v9 the steps between at their pitch's place between the ends (TileInteraction.Top)", () =>
            {
                var d = new StringBuilder();
                int n = st.cols;
                float a = st.stairDir < 0 ? 0f : ProjectConfig.StairStepRise, b = st.stairDir < 0 ? -(n - 1) * ProjectConfig.StairStepRise : n * ProjectConfig.StairStepRise;
                int p0 = st.GetTile(0, 0).midi, p1 = st.GetTile(n - 1, 0).midi;
                for (int i = 0; i < n; i++)
                {
                    float y = st.transform.InverseTransformPoint(st.GetTile(i, 0).Top).y;
                    float u = p1 != p0 ? Mathf.Clamp01((st.GetTile(i, 0).midi - p0) / (float)(p1 - p0)) : (n > 1 ? i / (float)(n - 1) : 0f);
                    float want = Mathf.Lerp(a, b, u);
                    d.Append(F(y)).Append(' ');
                    if (Mathf.Abs(y - want) > 0.005f) return "step " + i + " top " + F(y) + " want " + F(want);
                }
                Info(sb, tag + " step tops " + d);
                return null;
            });
            Run(sb, "stairs (" + tag + "): pitches = SongManager.StairPitches, strictly " + (st.stairDir < 0 ? "falling" : "rising"), () =>
            {
                st.RefreshStairs();
                var want = SM.StairPitches(st);
                var got = Enumerable.Range(0, st.cols).Select(i => st.GetTile(i, 0).midi).ToArray();
                Info(sb, tag + " pitches " + string.Join(" ", got) + " (StairPitches " + string.Join(" ", want) + ", chord " + (SM.ChordOfColumn(st.column) != null ? SM.ChordOfColumn(st.column).assignedChord : "-") + ")");
                if (!want.SequenceEqual(got)) return "tiles " + string.Join(",", got) + " vs " + string.Join(",", want);
                for (int i = 1; i < got.Length; i++) if (st.stairDir < 0 ? got[i] >= got[i - 1] : got[i] <= got[i - 1]) return "not monotone at " + i;
                return null;
            });
            Run(sb, "stairs (" + tag + "): brightness follows pitch; ink-stone (not the chord's vibe)", () =>
            {
                Color lo = Color.white, hi = Color.black; int mlo = 999, mhi = -1;
                foreach (var t in st.tiles) { if (t.midi < mlo) { mlo = t.midi; lo = t.BaseColor; } if (t.midi > mhi) { mhi = t.midi; hi = t.BaseColor; } }
                float vl, vh, h, s; Color.RGBToHSV(lo, out h, out s, out vl); Color.RGBToHSV(hi, out h, out s, out vh);
                if (vh <= vl) return "high step not brighter: " + F(vh) + " vs " + F(vl);
                if (st.PlatformColor != KeyBlock.StairBaseStone) return "base colour " + st.PlatformColor;
                return null;
            });
        }
        Run(sb, "stairs: a fall's base (the platform) sits under its lowest step (BaseDrop), a climb's on the default platform", () =>
        {
            if (fall == null || climb == null) return "missing";
            float want = (fall.cols - 1) * ProjectConfig.StairStepRise + KeyBlock.StairLowest - 0.03f;
            if (Mathf.Abs(fall.BaseDrop - want) > 0.005f) return "fall BaseDrop " + F(fall.BaseDrop) + " want " + F(want);
            if (Mathf.Abs(climb.BaseDrop) > 1e-4f) return "climb BaseDrop " + F(climb.BaseDrop);
            var plat = fall.transform.Find("Platform");
            if (plat == null) return "no platform";
            float platTop = plat.localPosition.y + ProjectConfig.PlatformThickness * 0.5f;
            if (Mathf.Abs(platTop - KeyBlock.StairBaseTopOf(-1, fall.cols)) > 0.01f) return "platform top " + F(platTop);
            if (Mathf.Abs(fall.BaseUndersideY - (fall.UndersideY - fall.BaseDrop)) > 1e-4f) return "BaseUndersideY";
            return null;
        });
        Run(sb, "stairs: the direction glyph (↓ / ↑) on the top step's front face", () =>
        {
            if (fall == null || climb == null) return "missing";
            var g0 = fall.GetTile(0, 0).transform.Find("StairGlyph"); var g1 = climb.GetTile(climb.cols - 1, 0).transform.Find("StairGlyph");
            if (g0 == null || g1 == null) return "glyph missing (fall " + (g0 != null) + ", climb " + (g1 != null) + ")";
            if (g0.GetComponent<Renderer>().sharedMaterial != StructureLook.ArrowMaterial(false)) return "fall glyph not ↓";
            if (g1.GetComponent<Renderer>().sharedMaterial != StructureLook.ArrowMaterial(true)) return "climb glyph not ↑";
            float zf = fall.transform.InverseTransformPoint(g0.position).z;
            if (Mathf.Abs(zf - (-KeyBlock.EdgeInset + KeyBlock.StairLedge)) > 0.05f) return "glyph not on the front face: z " + F(zf);
            return null;
        });
        Run(sb, "stairs: RefreshStairs re-pitches from StairPitches (idempotent)", () =>
        {
            if (fall == null) return "missing";
            var t0 = fall.GetTile(1, 0); int keep = t0.midi;
            t0.midi = 1;   // a stale pitch
            bool changed = fall.RefreshStairs();
            if (!changed || fall.GetTile(1, 0).midi != keep) return "not restored: " + fall.GetTile(1, 0).midi + " (changed " + changed + ")";
            // stairShown still held the true run, so force: a second refresh must be a no-op
            if (fall.RefreshStairs()) return "second refresh changed";
            return null;
        });
        if (captures)
        {
            Frame(1); yield return Wait(0.6f); yield return Shot("b7_stairs_down.png");
            Frame(2); yield return Wait(0.6f); yield return Shot("b7_stairs_up.png");
        }

        // ---------------------------------------------------------------- phrase
        var ph = Kind(4).FirstOrDefault();
        var key = MusicTheory.KeyOfSong();
        Run(sb, "phrase: cells = beats × 24 / grid (8 beats of eighths = 16), 15 rows, one tile per cell (gridX = cell, gridZ = row)", () =>
        {
            if (ph == null) return "no phrase";
            if (ph.cols != 16 || ph.rows != ProjectConfig.PhraseRows) return "grid " + ph.cols + "x" + ph.rows;
            if (ph.tiles.Count != 16 * 15) return "tiles " + ph.tiles.Count;
            for (int x = 0; x < ph.cols; x++) for (int z = 0; z < ph.rows; z++) { var t = ph.GetTile(x, z); if (t == null || t.gridX != x || t.gridZ != z || !t.CompareTag("Tile") || t.GetComponent<BoxCollider>() == null) return "cell " + x + "," + z; }
            return null;
        });
        Run(sb, "phrase: rows = the song key's scale over two octaves around 60..84 (C minor: C4 D Eb F G Ab Bb … C6), row z's pitch on every cell", () =>
        {
            if (ph == null) return "no phrase";
            int[] want = KeyBlock.PhraseScale(key.tonic, key.minor, ph.register);
            int[] cMinor = { 60, 62, 63, 65, 67, 68, 70, 72, 74, 75, 77, 79, 80, 82, 84 };
            if (key.tonic == 0 && key.minor && !want.SequenceEqual(cMinor)) return "scale " + string.Join(",", want);
            for (int z = 0; z < ph.rows; z++) for (int x = 0; x < ph.cols; x++) if (ph.GetTile(x, z).midi != want[z]) return "cell " + x + "," + z + " midi " + ph.GetTile(x, z).midi;
            Info(sb, "phrase rows " + string.Join(" ", want) + " (key " + key + ")");
            return null;
        });
        Run(sb, "phrase: time cells are linear over the platform after the piano (equal widths, beat x = PhraseBeatX), rows further back and a touch higher", () =>
        {
            if (ph == null) return "no phrase";
            float cw = ph.PhraseCellWidth;
            for (int x = 0; x < ph.cols; x++)
            {
                var t = ph.GetTile(x, 0);
                float lx = ph.transform.InverseTransformPoint(t.Top).x, want = -KeyBlock.EdgeInset + KeyBlock.PhrasePianoWidth + (x + 0.5f) * cw;
                if (Mathf.Abs(lx - want) > 0.003f) return "cell " + x + " x " + F(lx) + " want " + F(want);
            }
            float span = cw * ph.cols, wantSpan = ph.PhraseWidth - KeyBlock.PhrasePianoWidth - KeyBlock.PhraseEndPad;
            if (Mathf.Abs(span - wantSpan) > 0.003f) return "span " + F(span) + " vs " + F(wantSpan);
            for (int z = 1; z < ph.rows; z++)
            {
                Vector3 a = ph.transform.InverseTransformPoint(ph.GetTile(0, z - 1).Top), b = ph.transform.InverseTransformPoint(ph.GetTile(0, z).Top);
                if (b.z - a.z < ProjectConfig.PhraseRowPitch - 0.001f || b.y <= a.y) return "row " + z + " not further back / higher";
            }
            Info(sb, "phrase width " + F(ph.PhraseWidth) + ", cell " + F(cw) + ", beat 4 at x " + F(ph.PhraseBeatX(4)));
            return null;
        });
        Run(sb, "phrase: the roll lines up with the columns (its first cell starts at its column's west edge, its measure line at column 1's)", () =>
        {
            if (ph == null) return "no phrase";
            float cellsW = ph.PhraseCellWidth * ph.cols;
            if (Mathf.Abs(cellsW - ph.PhraseCellsWidth) > 0.003f) return "cells span " + F(cellsW) + " vs PhraseCellsWidth " + F(ph.PhraseCellsWidth);
            float start = ph.transform.TransformPoint(new Vector3(ph.PhraseBeatX(0f), 0f, 0f)).x, bar = ph.transform.TransformPoint(new Vector3(ph.PhraseBeatX(4f), 0f, 0f)).x;
            float w0 = SM.ColumnWestEdge(ph.column) + ph.phraseOffset / (float)ProjectConfig.TicksPerBeat / GlobalClock.BeatsPerBar * KeyBlock.IslandWidth, w1 = SM.ColumnWestEdge(ph.column + 1);
            Info(sb, "phrase start x " + F(start) + " (column " + ph.column + " west " + F(w0) + "), measure 2 x " + F(bar) + " (column " + (ph.column + 1) + " west " + F(w1) + ")");
            if (Mathf.Abs(start - w0) > 0.05f || Mathf.Abs(bar - w1) > 0.05f) return "misaligned: start " + F(start - w0) + ", measure line " + F(bar - w1);
            return null;
        });
        Run(sb, "phrase: bold measure lines at the measure boundaries (1 for 2 measures), thin beat lines on the other beats (6), the tiny piano (15 keys, tonic houses)", () =>
        {
            if (ph == null) return "no phrase";
            var roll = ph.transform.Find("Roll");
            if (roll == null) return "no roll";
            int bars = 0, beats = 0, keys = 0, houses = 0;
            foreach (Transform c in roll) { if (c.name == "MeasureLine") bars++; else if (c.name == "BeatLine") beats++; else if (c.name.StartsWith("PianoKey")) { keys++; if (c.Find("KeyHouse") != null) houses++; } }
            if (bars != 1 || beats != 6) return "lines " + bars + " bold, " + beats + " thin";
            if (keys != 15) return "piano keys " + keys;
            if (houses != 3) return "tonic houses " + houses + " (want 3: C4 C5 C6)";
            var ml = roll.Find("MeasureLine");
            if (Mathf.Abs(ml.localPosition.x - ph.PhraseBeatX(4)) > 0.005f) return "measure line x " + F(ml.localPosition.x);
            return null;
        });
        Run(sb, "phrase: one chord segment per column it spans (Fm7 then B♭7), each tinting its cells and the platform under them with its vibe", () =>
        {
            if (ph == null) return "no phrase";
            ph.RefreshPhraseChords();
            var segs = ph.PhraseSegments;
            if (segs.Count != 2) return "segments " + segs.Count;
            var c0 = ChordAt(0); var c1 = ChordAt(1);
            if (segs[0].root != c0.chordRootMIDI || segs[1].root != c1.chordRootMIDI) return "roots " + segs[0].root + " " + segs[1].root;
            if (Mathf.Abs(segs[0].b1 - 4f) > 1e-3f || Mathf.Abs(segs[1].b0 - 4f) > 1e-3f) return "split at " + F(segs[0].b1);
            var marks = ph.KindMarks;
            if (marks == null || marks.BandCount != 2) return "bands " + (marks != null ? marks.BandCount : -1);
            if (!Near(marks.BandColor(0), KeyBlock.PlatformColorOf(c0.chordColor)) || !Near(marks.BandColor(1), KeyBlock.PlatformColorOf(c1.chordColor))) return "band colours";
            // a cell's hue follows its segment's vibe
            float h0, h1, hv0, hv1, s, v;
            Color.RGBToHSV(ph.PhraseCellColor(1, 7), out h0, out s, out v); Color.RGBToHSV(ph.PhraseCellColor(12, 7), out h1, out s, out v);
            Color.RGBToHSV(c0.chordColor, out hv0, out s, out v); Color.RGBToHSV(c1.chordColor, out hv1, out s, out v);
            if (Mathf.Abs(Mathf.DeltaAngle(h0 * 360f, hv0 * 360f)) > 12f || Mathf.Abs(Mathf.DeltaAngle(h1 * 360f, hv1 * 360f)) > 12f) return "cell hues " + F(h0) + "/" + F(h1) + " vs " + F(hv0) + "/" + F(hv1);
            return null;
        });
        Run(sb, "phrase: a SAFE-NOTE dot on every beat's first cell of exactly the chord-tone rows of its segment (Fm7: C Eb F Ab rows; B♭7: D F Ab B♭)", () =>
        {
            if (ph == null) return "no phrase";
            int want = 0, bad = 0; string first = null;
            for (int x = 0; x < ph.cols; x++)
            {
                var seg = ph.PhraseSegments[ph.PhraseSegmentOf(x)];
                bool beat = (x * ph.phraseGrid) % ProjectConfig.TicksPerBeat == 0;
                for (int z = 0; z < ph.rows; z++)
                {
                    int rel = Harmony.Pc(ph.GetTile(x, z).midi - seg.root);
                    bool tone = seg.semis.Any(q => Harmony.Pc(q) == rel);
                    bool should = beat && tone;
                    if (should) want++;
                    if (should != ph.PhraseSafeDot(x, z)) { bad++; if (first == null) first = x + "," + z; }
                }
            }
            Info(sb, "safe dots " + ph.PhraseSafeDotsShown + " (want " + want + ")");
            if (bad > 0) return bad + " cells wrong, first " + first;
            if (want != 68) return "expected 68 dots for Fm7 + Bb7 in C minor, the rule gives " + want;
            return null;
        });
        Run(sb, "phrase: PhraseCellAt maps a world point back to its cell", () =>
        {
            if (ph == null) return "no phrase";
            int x, z;
            var w = ph.transform.TransformPoint(ph.PhraseCellLocal(5, 9));
            if (!ph.PhraseCellAt(w, out x, out z) || x != 5 || z != 9) return "got " + x + "," + z;
            if (ph.PhraseCellAt(ph.transform.TransformPoint(new Vector3(-KeyBlock.EdgeInset + 0.1f, 0f, 0f)), out x, out z)) return "the piano is not a cell";
            return null;
        });
        if (captures) { Frame(3); yield return Wait(0.6f); yield return Shot("b7_phrase.png"); }

        // ---------------------------------------------------------------- beat pips
        Run(sb, "beat pips: every grid but Moons wears one pip per beat (4 per measure; a phrase: phraseBeats), the downbeats bigger", () =>
        {
            foreach (var kb in SM.Islands)
            {
                if (kb == null) continue;
                var m = kb.KindMarks;
                if (m == null) return kb.name + " has no marks";
                int want = kb.IsPhrase ? kb.phraseBeats : Mathf.Max(1, kb.bars) * GlobalClock.BeatsPerBar;
                if (m.PipCount != want) return kb.name + " (kind " + kb.kind + ") pips " + m.PipCount + " want " + want;
                for (int i = 0; i < m.PipCount; i++)
                {
                    bool down = i % GlobalClock.BeatsPerBar == 0;
                    if (m.PipIsDown(i) != down) return kb.name + " pip " + i + " downbeat flag";
                    if (down && m.PipRestSize(i) <= m.PipRestSize((i + 1) % m.PipCount) + 1e-4f) return kb.name + " downbeat not bigger";
                }
            }
            foreach (var mo in SM.Moons) if (mo != null && mo.KindMarks != null) return "a Moon wears pips";
            return null;
        });
        Run(sb, "beat pips: along the front margin, one per beat's slice of the platform, left → right", () =>
        {
            var kb = ChordAt(2);
            var m = kb.KindMarks;
            for (int i = 0; i < m.PipCount; i++)
            {
                var p = m.Pip(i).localPosition;
                float want = -KeyBlock.EdgeInset + (i + 0.5f) * kb.Width / m.PipCount;
                if (Mathf.Abs(p.x - want) > 0.01f) return "pip " + i + " x " + F(p.x) + " want " + F(want);
                if (p.z > -KeyBlock.EdgeInset + ProjectConfig.PlatformPad) return "pip " + i + " not on the front margin: z " + F(p.z);
            }
            return null;
        });
        Run(sb, "beat pips: a keyboard (its keys reach the lip) wears them standing on its platform's front face", () =>
        {
            var k = Kind(2).FirstOrDefault();
            if (k == null) return "no keyboard";
            var m = k.KindMarks;
            if (m == null || m.PipCount != 4) return "pips " + (m != null ? m.PipCount : -1);
            var p = m.Pip(0);
            if (p.GetComponent<MeshFilter>().sharedMesh != MeshFactory.VerticalQuad()) return "not standing";
            if (Mathf.Abs(p.localPosition.z - (-KeyBlock.EdgeInset - 0.012f)) > 0.01f) return "not on the front face: z " + F(p.localPosition.z);
            return null;
        });
        Run(sb, "beat pips: the lit pip is a pure function of the song beat (the island's passes; a phrase's own span)", () =>
        {
            var kb = ChordAt(2);
            float s = kb.startBeatOffset;
            int[] want = { -1, 0, 0, 1, 2, 3, -1 };
            double[] at = { s - 0.1, s, s + 0.99, s + 1.0, s + 2.5, s + 3.99, s + 4.0 };
            for (int i = 0; i < at.Length; i++) { int got = StructureLook.PipAt(kb, at[i], true); if (got != want[i]) return "beat " + at[i] + " → " + got + " want " + want[i]; }
            if (StructureLook.PipAt(kb, s + 1.5, false) != -1) return "lit while stopped";
            if (ph != null) { float ps = ph.startBeatOffset; if (StructureLook.PipAt(ph, ps + 5.5, true) != 5 || StructureLook.PipAt(ph, ps + 8.1, true) != -1) return "phrase pips"; }
            return null;
        });

        Run(sb, "draft fill (§19.1): SetDraftFill(used, next note's end) lights the used beats (teal; a partly used beat smaller), the next note's beats as ghosts, coral past the grid's length; (-1, 0) clears", () =>
        {
            var kb = ChordAt(8); var m = kb.KindMarks;
            Func<string> states = () => string.Join("", Enumerable.Range(0, m.PipCount).Select(i => m.FillStateOf(i).ToString()).ToArray());
            kb.SetDraftFill(2.5f, 3.5f);
            if (states() != "1112") return "2.5 → 3.5: " + states();
            if (m.PipMaterial(0) != StructureLook.PipFillMaterial || m.PipMaterial(3) != StructureLook.PipNextMaterial) return "materials";
            if (m.Pip(2).localScale.x >= m.Pip(1).localScale.x * 0.9f * (m.PipRestSize(2) / m.PipRestSize(1))) return "the partly used beat is not smaller";
            kb.SetDraftFill(3f, 5f);
            if (states() != "1113") return "3 → 5: " + states();
            kb.SetDraftFill(4f, 5f);
            if (states() != "1113") return "full → 5: " + states();
            if (m.PipMaterial(3) != StructureLook.PipOverMaterial) return "full grid's last pip not coral";
            kb.SetDraftFill(-1f, 0f);
            if (states() != "0000" || m.PipMaterial(0) != StructureLook.PipDownMaterial || m.PipMaterial(1) != StructureLook.PipRestMaterial) return "not cleared: " + states();
            return null;
        });

        if (captures)
        {
            var kf = ChordAt(2); var pf = Kind(4).FirstOrDefault();
            kf.SetDraftFill(2.5f, 3.5f);
            if (pf != null) pf.SetDraftFill(5f, 7f);
            if (OrbitCamera.I != null) { var fb = kf.WorldBounds; fb.Encapsulate(ChordAt(3).WorldBounds); OrbitCamera.I.FrameBounds(fb, 0.1f, true, 0.75f); }
            yield return Wait(0.6f); yield return Shot("b7_draft_fill.png");
            kf.SetDraftFill(-1f, 0f); if (pf != null) pf.SetDraftFill(-1f, 0f);
        }

        // ---------------------------------------------------------------- plinths
        SectionPlinth.RefreshNow();
        yield return null;
        Run(sb, "plinths: one per section (" + SM.SectionCount + "), letters by content (A, A, B: the repeated progression shares its letter)", () =>
        {
            if (SectionPlinth.Count != SM.SectionCount) return "plinths " + SectionPlinth.Count + " sections " + SM.SectionCount + " (starts " + string.Join(",", SM.SectionStarts()) + ")";
            if (SM.SectionCount != 3) return "sections " + SM.SectionCount + " (starts " + string.Join(",", SM.SectionStarts()) + ")";
            string l = SectionPlinth.LetterOf(0) + SectionPlinth.LetterOf(1) + SectionPlinth.LetterOf(2);
            if (l != "AAB") return "letters " + l;
            return null;
        });
        Run(sb, "plinths: a section's NAME (§17.1) beside its letter — U1's picture in ink on its tint's disc + the lowercase word (verse, chorus); none on an unnamed one", () =>
        {
            if (SectionPlinth.RoleShown(0) != 2 || SectionPlinth.RoleShown(1) != 3 || SectionPlinth.RoleShown(2) != 0) return "roles " + SectionPlinth.RoleShown(0) + SectionPlinth.RoleShown(1) + SectionPlinth.RoleShown(2) + " (SectionRole " + SM.SectionRole(0) + SM.SectionRole(1) + SM.SectionRole(2) + ")";
            if (SectionPlinth.RoleWordShown(0) != "verse" || SectionPlinth.RoleWordShown(1) != "chorus" || SectionPlinth.RoleWordShown(2) != "") return "words " + SectionPlinth.RoleWordShown(0) + "/" + SectionPlinth.RoleWordShown(1) + "/" + SectionPlinth.RoleWordShown(2);
            var disc = SectionPlinth.RoleIconShown(1);
            if (disc == null || SectionPlinth.RoleIconShown(2) != null) return "icons";
            if (!Near(StructureLook.TintOf(disc.GetComponent<Renderer>(), Color.clear), SectionRoles.Tint(3))) return "disc tint";
            var icon = disc.Find("RoleIcon");
            if (icon == null || icon.GetComponent<Renderer>().sharedMaterial.mainTexture != IconFactory.GetTexture(SectionRoles.Glyph(3))) return "icon glyph";
            var fl = SectionPlinth.FrontLeft(1); var b = SectionPlinth.Bounds(1);
            if (Mathf.Abs(fl.x - b.min.x) > 0.05f || Mathf.Abs(fl.z - b.min.z) > 0.05f) return "FrontLeft " + fl + " vs bounds min " + b.min;
            return null;
        });
        Run(sb, "plinths: one block per measure over its column's platform (4, 4, 2), numbered 1..n along the front, 4 ruler pips per measure", () =>
        {
            int[] bars = { 4, 4, 2 };
            for (int s = 0; s < 3; s++)
            {
                if (SectionPlinth.MeasuresShown(s) != bars[s]) return "section " + s + " measures " + SectionPlinth.MeasuresShown(s);
                for (int m = 0; m < bars[s]; m++)
                {
                    float x0, x1; bool ghost; int col;
                    SectionPlinth.MeasureSpan(s, m, out x0, out x1, out ghost, out col);
                    var a = SM.AnchorOf(col);
                    if (ghost || a == null) return "section " + s + " measure " + m + " ghost / no column";
                    if (col != SM.SectionFirst(s) + m) return "section " + s + " measure " + m + " column " + col;
                    float w0 = SM.ColumnWestEdge(col), w1 = w0 + SM.ColumnWidth(col);
                    if (Mathf.Abs(x0 - w0) > 0.02f || Mathf.Abs(x1 - w1) > 0.02f || Mathf.Abs(a.WestEdge - w0) > 0.02f) return "section " + s + " measure " + m + " span " + F(x0) + ".." + F(x1) + " vs column " + F(w0) + ".." + F(w1) + " (anchor west " + F(a.WestEdge) + ")";
                }
                var nums = SectionPlinth.NumbersShown(s);
                string want = string.Join(" ", Enumerable.Range(1, bars[s] + SectionPlinth.GhostsShown(s)).Select(i => i.ToString()));
                if (string.Join(" ", nums) != want) return "section " + s + " numbers " + string.Join(" ", nums);
                if (SectionPlinth.PipsShown(s) != (bars[s] + SectionPlinth.GhostsShown(s)) * GlobalClock.BeatsPerBar) return "section " + s + " pips " + SectionPlinth.PipsShown(s);
            }
            return null;
        });
        Run(sb, "plinths: the short section (2 measures) shows 2 GHOST measures after its last column; full sections none", () =>
        {
            if (SectionPlinth.GhostsShown(0) != 0 || SectionPlinth.GhostsShown(1) != 0) return "ghosts on full sections";
            if (SectionPlinth.GhostsShown(2) != 2) return "ghosts " + SectionPlinth.GhostsShown(2);
            float x0, x1, gx0, gx1; bool ghost; int col;
            SectionPlinth.MeasureSpan(2, 1, out x0, out x1, out ghost, out col);
            SectionPlinth.MeasureSpan(2, 2, out gx0, out gx1, out ghost, out col);
            if (!ghost) return "measure 3 not a ghost";
            if (gx0 < x1 - 0.02f || Mathf.Abs((gx1 - gx0) - KeyBlock.IslandWidth) > 0.02f) return "ghost span " + F(gx0) + ".." + F(gx1);
            return null;
        });
        Run(sb, "plinths: under every island and phrase of the section (x / z inside its footprint), its top ground − 0.95 per column (under a falling stair's base)", () =>
        {
            for (int s = 0; s < SM.SectionCount; s++)
            {
                var b = SectionPlinth.Bounds(s);
                foreach (var kb in SM.Islands)
                {
                    if (kb == null || kb.IsMoon || kb.IsPhrase || SM.SectionOf(kb.column) != s) continue;
                    if (kb.WestEdge < b.min.x - 0.01f || kb.EastEdge > b.max.x + 0.01f || kb.FrontEdge < b.min.z - 0.01f || kb.BackEdge > b.max.z + 0.01f) return kb.name + " outside plinth " + s;
                    if (SectionPlinth.PlinthAt(kb.Center) != s) return "PlinthAt(" + kb.name + ") = " + SectionPlinth.PlinthAt(kb.Center);
                }
                for (int m = 0; m < SectionPlinth.MeasuresShown(s); m++)
                {
                    float x0, x1; bool ghost; int col;
                    SectionPlinth.MeasureSpan(s, m, out x0, out x1, out ghost, out col);
                    float drop = SectionPlinth.TopDrop;
                    foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && !kb.IsPhrase && kb.column == col) drop = Mathf.Max(drop, 0.03f + ProjectConfig.PlatformThickness + kb.BaseDrop + SectionPlinth.BaseClear);
                    float want = SM.AnchorOf(col).GroundY - drop;
                    if (Mathf.Abs(SectionPlinth.TopOf(s, m) - want) > 0.01f) return "section " + s + " measure " + m + " top " + F(SectionPlinth.TopOf(s, m)) + " want " + F(want);
                }
            }
            if (ph != null && SectionPlinth.PlinthAt(ph.Center) != 0) return "the phrase's plinth " + SectionPlinth.PlinthAt(ph.Center);
            Info(sb, "plinth bounds: " + string.Join(" | ", Enumerable.Range(0, SectionPlinth.Count).Select(s => { var b = SectionPlinth.Bounds(s); return F(b.min.x) + ".." + F(b.max.x) + " z " + F(b.min.z) + ".." + F(b.max.z) + " y " + F(b.max.y); })));
            return null;
        });
        Run(sb, "plinths: Pick(ray) finds the plinth a camera ray hits; the sea ahead of the song is no plinth", () =>
        {
            var cam = Camera.main;
            if (cam == null) return "no camera";
            float x0, x1; bool ghost; int col;
            SectionPlinth.MeasureSpan(1, 2, out x0, out x1, out ghost, out col);
            var target = new Vector3((x0 + x1) * 0.5f, SectionPlinth.TopOf(1, 2), SectionPlinth.Bounds(1).min.z + 0.3f);
            var ray = new Ray(cam.transform.position, (target - cam.transform.position).normalized);
            int s = SectionPlinth.Pick(ray);
            if (s != 1) return "pick " + s;
            var far = new Vector3(SectionPlinth.Bounds(0).min.x - 30f, -1f, 0f);
            if (SectionPlinth.PlinthAt(far) != -1 || SectionPlinth.Pick(new Ray(cam.transform.position, (far - cam.transform.position).normalized)) != -1) return "the sea is a plinth";
            return null;
        });
        // ---------------------------------------------------------------- a live resize (§19.1 auto-expand): pips, fill, stairs, plinth
        {
            var kb8 = ChordAt(8);
            kb8.SetDraftFill(3f, 4f);
            bool resized = SM.ResizeColumnLive(8, 2);
            yield return null; yield return null;
            SectionPlinth.RefreshNow();
            Run(sb, "live resize: a column grown to 2 measures (ResizeColumnLive) re-lays its grids' pips (8, two downbeats), keeps the draft fill, and its plinth shows 3 measures + 1 ghost", () =>
            {
                if (!resized) return "ResizeColumnLive refused";
                var m = kb8.KindMarks;
                if (m == null || m.PipCount != 8) return "pips " + (m != null ? m.PipCount : -1);
                if (!m.PipIsDown(0) || !m.PipIsDown(4) || m.PipIsDown(2)) return "downbeats";
                if (Mathf.Abs(kb8.DraftFillBeats - 3f) > 1e-3f || m.FillStateOf(2) != 1 || m.FillStateOf(3) != 2 || m.FillStateOf(4) != 0) return "fill after resize " + kb8.DraftFillBeats + " states " + m.FillStateOf(2) + m.FillStateOf(3) + m.FillStateOf(4);
                float x7 = m.Pip(7).localPosition.x, want = -KeyBlock.EdgeInset + 7.5f * kb8.Width / 8f;
                if (Mathf.Abs(x7 - want) > 0.02f) return "pip 7 x " + F(x7) + " want " + F(want);
                if (SectionPlinth.MeasuresShown(2) != 3 || SectionPlinth.GhostsShown(2) != 1) return "plinth C measures " + SectionPlinth.MeasuresShown(2) + " ghosts " + SectionPlinth.GhostsShown(2);
                kb8.SetDraftFill(-1f, 0f);
                return null;
            });
            var fallCol = fall != null ? fall.column : 1;
            bool resizedFall = SM.ResizeColumnLive(fallCol, 2);
            yield return null; yield return null;
            var fall2 = Kind(3).FirstOrDefault(k => k.stairDir < 0);
            Run(sb, "live resize: a 2-measure falling stair keeps its steps in its LAST measure (a lead-in into the next chord) and a landing covers the first", () =>
            {
                if (!resizedFall || fall2 == null) return "resize refused / no stair";
                float w0 = fall2.transform.InverseTransformPoint(fall2.GetTile(0, 0).Top).x;
                if (w0 < -KeyBlock.EdgeInset + KeyBlock.IslandWidth) return "step 0 at x " + F(w0) + " (not in the second measure)";
                var land = fall2.tilesContainer != null ? fall2.tilesContainer.Find("StairLanding") : null;
                if (land == null) return "no landing";
                float top = land.localPosition.y + land.GetComponent<MeshFilter>().sharedMesh.bounds.max.y;
                if (Mathf.Abs(top - fall2.StairTopY(0)) > 0.01f) return "landing top " + F(top);
                if (fall2.KindMarks.PipCount != 8) return "pips " + fall2.KindMarks.PipCount;
                for (int i = 0; i < fall2.cols; i++) if (Mathf.Abs(fall2.transform.InverseTransformPoint(fall2.GetTile(i, 0).Top).y - fall2.StairTopY(i)) > 0.005f) return "step " + i + " height";
                return null;
            });
            if (captures) { Frame(1); yield return Wait(0.6f); yield return Shot("b7_stairs_two_measures.png"); }
            LoadTest();
            yield return null; yield return null;
            SectionPlinth.RefreshNow();
            fall = Kind(3).FirstOrDefault(k => k.stairDir < 0); climb = Kind(3).FirstOrDefault(k => k.stairDir > 0); ph = Kind(4).FirstOrDefault();
            // a chord change under the phrase (SetChord: a rebuild) re-tints its segment and moves its safe dots; the stair re-pitches into it
            int c1 = SM.Islands.IndexOf(ChordAt(1));
            var before = fall != null ? SM.StairPitches(fall) : new int[0];
            SM.SetChord(c1, 61, new[] { 0, 4, 7, 11 });   // B♭7 → D♭maj7
            yield return null; yield return null;
            var ph2 = Kind(4).FirstOrDefault(); var fall3 = Kind(3).FirstOrDefault(k => k.stairDir < 0);
            Run(sb, "a chord change (B♭7 → D♭maj7 under the phrase's second measure) re-tints that segment, moves its safe dots; the stairs on it re-pitch", () =>
            {
                if (ph2 == null || fall3 == null) return "missing";
                var seg = ph2.PhraseSegments.Count > 1 ? ph2.PhraseSegments[1] : default(KeyBlock.PhraseSeg);
                if (Harmony.Pc(seg.root) != 1) return "segment 1 root " + seg.root;
                // row 1 = D (a B♭7 tone, not a D♭maj7 one): no dot now; row 5 = A♭ (a tone of both): a dot
                int x = 8;   // beat 4's first cell
                if (ph2.PhraseSafeDot(x, 1)) return "D row still safe";
                if (!ph2.PhraseSafeDot(x, 5)) return "A♭ row not safe";
                var now = Enumerable.Range(0, fall3.cols).Select(i => fall3.GetTile(i, 0).midi).ToArray();
                if (!now.SequenceEqual(SM.StairPitches(fall3))) return "stair not re-pitched: " + string.Join(",", now);
                Info(sb, "stair over D♭maj7: " + string.Join(" ", now) + " (was " + string.Join(" ", before) + " over B♭7)");
                return null;
            });
            LoadTest();
            yield return null; yield return null;
            SectionPlinth.RefreshNow();
            fall = Kind(3).FirstOrDefault(k => k.stairDir < 0); climb = Kind(3).FirstOrDefault(k => k.stairDir > 0); ph = Kind(4).FirstOrDefault();
        }
        if (captures)
        {
            Frame(4); yield return Wait(0.6f); yield return Shot("b7_plinth_section.png");
            if (OrbitCamera.I != null) { var lb = new Bounds(SectionPlinth.FrontLeft(1) + new Vector3(4f, 0f, 2.5f), new Vector3(9f, 1f, 6f)); OrbitCamera.I.FrameBounds(lb, 0.1f, true, 0.8f); }
            yield return Wait(0.6f); yield return Shot("b7_plinth_names.png");
            Frame(5); yield return Wait(0.6f); yield return Shot("b7_plinth_ghosts.png");
            Frame(0); yield return Wait(0.6f); yield return Shot("b7_overview.png");
        }

        // ---------------------------------------------------------------- playing: the lit pips and the playhead
        SM.RecomputeMeasureStarts();
        var c5 = ChordAt(5);
        double target5 = SM.ColumnStart(5) + 1.5;
        GlobalClock.Seek(SM.ColumnStart(5) - 0.2);
        GlobalClock.Play();
        yield return UntilBeat(target5);
        yield return null;
        double beatNow = GlobalClock.SongBeatD;
        Run(sb, "playing: the lit column's grids light the pip of the beat (column 5, beat 1 → pip 1); others dark", () =>
        {
            int want = StructureLook.PipAt(c5, beatNow, true);
            if (want != 1) return "pure pip " + want + " at " + F(beatNow);
            if (c5.KindMarks.LitPip != want) return "lit " + c5.KindMarks.LitPip + " at beat " + F(beatNow);
            var c2 = ChordAt(2);
            if (c2.KindMarks.LitPip != -1) return "column 2 lit " + c2.KindMarks.LitPip;
            return null;
        });
        Run(sb, "playing: the section's ruler lights measure 2 beat 1 (pip 5 of plinth 1) and its playhead sweeps at the song's time", () =>
        {
            if (SectionPlinth.PlayingSection != 1) return "playing section " + SectionPlinth.PlayingSection;
            if (SectionPlinth.LitRulerPip != 5 || !SectionPlinth.RulerPipLit(1, 5)) return "lit ruler pip " + SectionPlinth.LitRulerPip;
            Vector3 a, b; Color c;
            if (!SectionPlinth.Playhead(out a, out b, out c)) return "no playhead";
            float x0, x1; bool ghost; int col;
            SectionPlinth.MeasureSpan(1, 1, out x0, out x1, out ghost, out col);
            float frac = Mathf.Repeat((float)beatNow - SM.ColumnStart(5), SM.PassLength(5)) / SM.PassLength(5);
            float want = Mathf.Lerp(x0, x1, frac) + c5.VisualOffset.x;
            if (Mathf.Abs(a.x - want) > 0.35f) return "playhead x " + F(a.x) + " want ≈ " + F(want);
            if (b.z <= a.z) return "playhead not front → back";
            Info(sb, "playhead at beat " + F(beatNow) + ": x " + F(a.x) + " (column 5 spans " + F(x0) + ".." + F(x1) + "), z " + F(a.z) + ".." + F(b.z));
            return null;
        });
        if (captures) { if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(SectionPlinth.Bounds(1), 0.1f, true, 0.8f); yield return Wait(0.25f); yield return Shot("b7_playing.png"); }
        // the costs while playing
        {
            double sum = 0; int n = 0; float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.2f) { sum += SectionPlinth.LastMs; n++; yield return null; }
            Info(sb, "plinth LateUpdate " + (sum / Math.Max(1, n)).ToString("F3") + " ms avg over " + n + " frames; last relayout " + SectionPlinth.LastLayoutMs.ToString("F2") + " ms; relayouts " + SectionPlinth.Layouts);
            Run(sb, "playing: the plinths cost ≤ 0.3 ms a frame and never relayout while the song just plays", () =>
            {
                int before = SectionPlinth.Layouts;
                return sum / Math.Max(1, n) > 0.3 ? "avg " + (sum / Math.Max(1, n)).ToString("F3") + " ms" : null;
            });
        }
        GlobalClock.Stop();
        yield return null;
        Run(sb, "stopped: no lit pip, no playhead", () =>
        {
            Vector3 a, b; Color c;
            if (SectionPlinth.Playhead(out a, out b, out c)) return "playhead while stopped";
            foreach (var kb in SM.Islands) if (kb != null && kb.KindMarks != null && kb.KindMarks.LitPip >= 0) return kb.name + " lit";
            return null;
        });

        // ---------------------------------------------------------------- a 16ths phrase and a long one (the cost of many cells)
        LoadTest(16, 6);
        yield return null; yield return null;
        var ph16 = Kind(4).FirstOrDefault();
        Run(sb, "phrase of 16ths: 4 measures = 64 cells × 15 rows, 16 pips, 3 bold + 12 thin lines, 4 segments", () =>
        {
            if (ph16 == null) return "no phrase";
            if (ph16.cols != 64 || ph16.tiles.Count != 64 * 15) return "cells " + ph16.cols + " tiles " + ph16.tiles.Count;
            if (ph16.KindMarks.PipCount != 16) return "pips " + ph16.KindMarks.PipCount;
            int bars = 0, beats = 0; foreach (Transform c in ph16.transform.Find("Roll")) { if (c.name == "MeasureLine") bars++; else if (c.name == "BeatLine") beats++; }
            if (bars != 3 || beats != 12) return "lines " + bars + "/" + beats;
            if (ph16.PhraseSegments.Count != 4) return "segments " + ph16.PhraseSegments.Count;
            return null;
        });
        {
            float t0 = Time.realtimeSinceStartup; int n = 0;
            while (Time.realtimeSinceStartup - t0 < 1f) { n++; yield return null; }
            Info(sb, "frame rate with a 960-cell phrase: " + (n / (Time.realtimeSinceStartup - t0)).ToString("F0") + " fps (editor)");
        }

        // ---------------------------------------------------------------- done
        Run(sb, "no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3)));
        Application.logMessageReceived -= OnLog;
        try { GlobalClock.Stop(); SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); History.Reset(); History.Push(); } catch (Exception e) { Info(sb, "fixture reload: " + e.Message); }
        Restore();
        int pass = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS")), fail = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append("DONE ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }

    static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;
}
