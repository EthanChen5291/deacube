using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flow E2 — A QUIET WORLD: Play-mode verification. <c>QuietChecks.RunAll(captures)</c> starts a coroutine; poll <c>QuietChecks.Done</c> or
/// Captures/quiet_report.txt (numbered PASS / FAIL lines, INFO lines). A test song in C major — one section of four chord grids: Am7 with a
/// pluck path, Dm7 with THE SAME path (a repeat), G7 with another path (a change), Cmaj7 empty — then a keyboard, a staircase and a Moon.
///  f. quiet grids: the empty grid rests flush and a step paler, wakes under the pointer and with a cube in the hand; the repeated grid wears
///     the one repeat mark (its cubes' tiles keep their height), the changed one does not; a column that plays looks normal; nothing about the
///     layout (tile tops, colliders, island places), the saved song or the notes handed to the synth over one pass changes;
///  a. the ribbon at rest: five controls on a chord grid (move, mute, repeat, copy, ⋯), a keyboard, a staircase (direction instead of repeat),
///     three on a Moon; the repeat's −, rewind and vary only from ×2 with the pointer on repeat; every moved control in the ⋯ card and working;
///  b. no ribbon and no + ghosts with a cube / a pattern in the hand (hover, pin, selection), back with an empty hand; no left / right +;
///  c. the selection bar draws four (copy, paste, repeat, trash), the section header four (letter = the name fan, copy, trash, ⋯) and split /
///     join / drums / build-up work from its ⋯;
///  d. no punch pads while playing; the stutter punch-in still engages;
///  e. no destructive control draws the cross (the trash can everywhere; the cross only closes);
///  g. no console errors. Captures quiet_ribbon / quiet_more / quiet_selection / quiet_section / quiet_proto .png.
/// Never writes the user's save (saves snapshotted and restored); reloads the fixture at the end.
/// </summary>
public static class QuietChecks
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "quiet_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" Q-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
    static SongManager SM => SongManager.I;
    static IslandHeader H => IslandHeader.I;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test song
    const int Inst = 1;   // pluck
    const int IdA = 9401, IdRepeat = 9402, IdChanged = 9403;
    static readonly int[] PathX = { 0, 1, 2, 3 }, PathZ = { 0, 1, 0, 1 }, OtherZ = { 1, 1, 2, 2 }, Beats = { 24, 24, 24, 24 };

    static MeasureState Chord(int col, string name, int root, int[] semis) => new MeasureState { chordKey = name, root = root, semis = semis, bars = 1, kind = 0, col = col, repeat = 1, energy = 2 };
    static CubeState Cube(int measure, int id, int[] xs, int[] zs) => new CubeState
    {
        instrument = Inst, measure = measure, xs = (int[])xs.Clone(), zs = (int[])zs.Clone(), rests = new bool[xs.Length], mods = new int[xs.Length], durs = (int[])Beats.Clone(),
        nudges = new int[xs.Length], volume = 1f, moon = -1, twinOf = -1, echoOf = -1, id = id, step = 1, gate = 1, mode = 0, hits = -1
    };

    /// <summary>Am7 (a path) · Dm7 (the same path) · G7 (another path) · Cmaj7 (empty): one section, C major, 120 bpm, looping.</summary>
    public static SongState TestSong()
    {
        var m = new List<MeasureState> { Chord(0, "Am7", 57, new[] { 0, 3, 7, 10 }), Chord(1, "Dm7", 62, new[] { 0, 3, 7, 10 }), Chord(2, "G7", 55, new[] { 0, 4, 7, 10 }), Chord(3, "Cmaj7", 60, new[] { 0, 4, 7, 11 }) };
        var cubes = new List<CubeState> { Cube(0, IdA, PathX, PathZ), Cube(1, IdRepeat, PathX, PathZ), Cube(2, IdChanged, PathX, OtherZ) };
        return new SongState
        {
            version = SongState.CurrentVersion, name = "quiet", bpm = 120f, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes.ToArray(),
            moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
        };
    }

    static void LoadState(SongState st)
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.ClearRegion();
        GlobalClock.Stop();
        GridSelection.Clear();
        if (UIManager.I != null) UIManager.I.ClearSelection();
        IslandHeader.Hide(); SectionHeader.Hide();
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static AudioCube ById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }
    static KeyBlock IslandAt(int i) => SM != null && i >= 0 && i < SM.Islands.Count ? SM.Islands[i] : null;
    static KeyBlock OfKind(int kind) { foreach (var k in SM.Islands) if (k != null && k.kind == kind) return k; return null; }
    static int IndexOf(KeyBlock kb) => SM.Islands.IndexOf(kb);
    static int Cubes() { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) n++; return n; }
    static string Join(IEnumerable<string> l) => string.Join(" ", l.ToArray());
    static bool SameSet(IEnumerable<string> a, params string[] b) { var x = a.OrderBy(s => s, StringComparer.Ordinal).ToList(); var y = b.OrderBy(s => s, StringComparer.Ordinal).ToList(); return x.SequenceEqual(y); }
    static bool SameOrder(IList<string> a, params string[] b) => a.SequenceEqual(b);

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
        Park();
        if (PathManager.I != null) PathManager.I.PutDown();
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        QuietGrids.Enabled = true;
    }

    static void Restore()
    {
        PathManager.SimOnly = false;
        QuietGrids.Enabled = true;
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        try { GridSelection.Clear(); IslandHeader.Hide(); SectionHeader.Hide(); } catch (Exception) { }
        PathManager.AutoHand = false;
        VoiceRules.Tap = null;
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
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    /// <summary>The pointer off the world (sim only: the real mouse stays out of it).</summary>
    static void Park() { PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f); if (PathManager.I != null) PathManager.I.SimPointer(PathManager.SimPos, false, false, false); }
    /// <summary>The pointer resting on <paramref name="t"/> (a ray through its top).</summary>
    static void Point(TileInteraction t)
    {
        Vector3 s = Camera.main.WorldToScreenPoint(t.Top);
        PathManager.SimOnly = true; PathManager.SimPos = s;
        PathManager.I.SimPointer(s, false, false, false);
    }
    /// <summary>The pointer resting on <paramref name="kb"/>'s front margin (the island itself, not a tile: what brings its ribbon up).</summary>
    static void PointEdge(KeyBlock kb)
    {
        Vector3 s = Camera.main.WorldToScreenPoint(new Vector3(kb.Center.x + 1.3f, kb.VisualCenter.y, kb.FrontEdge + 0.25f));
        PathManager.SimOnly = true; PathManager.SimPos = s;
        PathManager.I.SimPointer(s, false, false, false);
    }
    static void Frame(Vector3 centre, float dist)
    {
        if (OrbitCamera.I == null) return;
        OrbitCamera.I.FrameBounds(new Bounds(centre, new Vector3(dist, 2f, dist * 0.6f)), 0.05f, true);
    }
    static IEnumerator ShowHeader(KeyBlock kb, float dist = 14f)
    {
        IslandHeader.Hide();
        yield return Frames(2);
        Frame(kb.Center, dist);
        yield return Wait(0.25f);
        IslandHeader.Show(kb);
        yield return Wait(0.45f);
    }
    static IEnumerator OpenMore()
    {
        if (!IslandHeader.MoreOpen) H.More.onClick();
        yield return Frames(3);
    }

    /// <summary>Plays the song from <paramref name="a"/> to <paramref name="b"/> (beats) and collects every event the cubes hand the synth.</summary>
    static IEnumerator Record(float a, float b, List<VoiceRules.NoteEvent> log)
    {
        log.Clear();
        VoiceRules.Tap = e => log.Add(e);
        GlobalClock.SetRegion(a, b);
        GlobalClock.Seek(a); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait((float)((b - a) / GlobalClock.BeatsPerSecond) + 0.35f);
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        VoiceRules.Tap = null;
        yield return Frames(2);
    }

    /// <summary>The notes of ONE pass of the 16-beat test song in a recording: from its first note, 16 beats on (less a tenth of a second).</summary>
    static List<VoiceRules.NoteEvent> PassOf(List<VoiceRules.NoteEvent> log)
    {
        if (log.Count == 0) return new List<VoiceRules.NoteEvent>();
        double t0 = log.Min(e => e.onDsp), pass = 16.0 / GlobalClock.BeatsPerSecond - 0.1;
        return log.Where(e => e.onDsp < t0 + pass).OrderBy(e => e.onDsp).ThenBy(e => e.midi).ToList();
    }

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static Color Shown(TileInteraction t) { var m = new MaterialPropertyBlock(); t.GetComponent<Renderer>().GetPropertyBlock(m); return m.GetColor(BaseColorId); }
    static float Sat(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return s; }
    static float Y(TileInteraction t) => t.transform.localPosition.y;
    static float Flush(KeyBlock kb) { float lo = float.MaxValue; foreach (var t in kb.tiles) lo = Mathf.Min(lo, t.RestY); return lo; }
    static float Stair(KeyBlock kb) { float hi = float.MinValue; foreach (var t in kb.tiles) hi = Mathf.Max(hi, t.RestY); return hi - Flush(kb); }
    /// <summary>null when every tile of <paramref name="kb"/> lies at its rest height (<paramref name="flat"/>: at the grid's flush level instead;
    /// <paramref name="standing"/>: those keep their rest height either way).</summary>
    static string Heights(KeyBlock kb, bool flat, HashSet<TileInteraction> standing = null)
    {
        float lo = Flush(kb);
        foreach (var t in kb.tiles)
        {
            bool stands = !flat || (standing != null && standing.Contains(t));
            float want = stands ? t.RestY : lo;
            if (Mathf.Abs(Y(t) - want) > 0.012f) return "tile " + t.gridX + "," + t.gridZ + " at y " + Y(t).ToString("F3") + " want " + want.ToString("F3") + (stands ? " (its rest height)" : " (flush)");
        }
        return null;
    }
    static HashSet<TileInteraction> NodesOn(KeyBlock kb) { var s = new HashSet<TileInteraction>(); foreach (var c in SequenceMaster.Cubes) if (c != null) foreach (var t in c.nodes) if (t != null && t.island == kb) s.Add(t); return s; }
    static int Marks() { int n = 0; foreach (var kb in SM.Islands) if (QuietGrids.Mark(kb) != null) n++; return n; }

    struct Pose { public Vector3 top, col; }
    static List<Pose> Layout()
    {
        var l = new List<Pose>();
        foreach (var kb in SM.Islands)
        {
            l.Add(new Pose { top = kb.Center, col = new Vector3(kb.Width, kb.Depth, kb.px * 0.001f + kb.pz) });
            foreach (var t in kb.tiles) { var c = t.GetComponent<Collider>(); l.Add(new Pose { top = t.Top, col = c != null ? c.bounds.center : Vector3.zero }); }
        }
        return l;
    }

    static int ActivePainterTrash(Component control)
    {
        // repaint the control's pictures now and count the trash cans drawn
        int n0 = IslandHeader.TrashPaints;
        foreach (var p in control.GetComponentsInChildren<InkPainter>(false)) p.Repaint();
        Canvas.ForceUpdateCanvases();
        return IslandHeader.TrashPaints - n0;
    }

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("QuietChecks ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        Prepare();
        LoadState(TestSong());
        yield return Frames(4);
        Frame(IslandAt(1).Center + new Vector3(4f, 0f, 0f), 34f);
        string json0 = JsonUtility.ToJson(SongState.Capture());

        // ================================================================ f. quiet grids
        // the normal look first (the switch off): the layout and one pass of notes, to compare against
        QuietGrids.Enabled = false; QuietGrids.ScanNow();
        yield return Wait(0.7f);
        var layoutOff = Layout();
        var notesOff = new List<VoiceRules.NoteEvent>();
        // (the cubes of a freshly loaded song land for a moment and their first play may miss its first notes: let them settle, play a warm-up
        // pass, and take the reference pass again if it still came up short — the comparison below stays strict: 12 notes, each one equal)
        yield return Wait(1.5f);
        yield return Record(0f, 16f, notesOff);
        yield return Wait(0.4f);
        yield return Record(0f, 16f, notesOff);
        int refTries = 1;
        while (refTries < 3 && PassOf(notesOff).Count != 12) { refTries++; yield return Wait(0.5f); yield return Record(0f, 16f, notesOff); }
        string normalHeights = null;
        foreach (var kb in SM.Islands) { normalHeights = Heights(kb, false); if (normalHeights != null) break; }
        QuietGrids.Enabled = true; Park(); QuietGrids.ScanNow();
        yield return Wait(0.9f);

        KeyBlock g0 = IslandAt(0), g1 = IslandAt(1), g2 = IslandAt(2), g3 = IslandAt(3);
        Run(sb, "f. with the switch off every grid looks as it always did (tiles at their pitch heights, no grid resting)", () => normalHeights);
        Run(sb, "f. an EMPTY grid rests: its tiles lie flush (the staircase down to its lowest tile) and a step paler; its platform too", () =>
        {
            if (QuietGrids.RestOf(g3) != QuietGrids.Rest.Empty) return "RestOf = " + QuietGrids.RestOf(g3);
            if (!QuietGrids.Resting(g3)) return "not resting";
            if (Stair(g3) < 0.3f) return "the grid has no staircase to lay down (" + Stair(g3).ToString("F2") + ")";
            string h = Heights(g3, true); if (h != null) return h;
            var hi = g3.tiles.OrderByDescending(t => t.RestY).First();
            float s0 = Sat(hi.BaseColor), s1 = Sat(Shown(hi));
            var lo = g3.tiles.OrderBy(t => t.RestY).First();
            if (Sat(Shown(lo)) > Sat(lo.BaseColor) - 0.03f) return "the low tile is not paler: saturation " + Sat(lo.BaseColor).ToString("F2") + " → " + Sat(Shown(lo)).ToString("F2");
            if (g3.QuietLook < 0.99f) return "platform quiet look " + g3.QuietLook.ToString("F2");
            return null;
        });
        Run(sb, "f. a ray through a resting tile's top still lands on that tile (the collider keeps its place: clicks go where they always did)", () =>
        {
            var hi = g3.tiles.OrderByDescending(t => t.RestY).First();
            RaycastHit hit;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(Camera.main.WorldToScreenPoint(hi.Top)), out hit, 500f)) return "the ray hit nothing";
            var got = hit.collider.GetComponent<TileInteraction>();
            return got == hi ? null : "hit " + (got != null ? got.name : hit.collider.name) + " want " + hi.name + " (drop " + hi.QuietDrop.ToString("F2") + ")";
        });
        // the pointer wakes it
        Point(g3.GetTile(2, 2));
        yield return Wait(0.9f);
        Run(sb, "f. the pointer over the empty grid wakes it: the tiles rise back to their pitch heights", () =>
        {
            if (PathManager.I.HoverTile == null || PathManager.I.HoverTile.island != g3) return "the pointer is not on a tile of the grid (" + (PathManager.I.HoverTile != null ? PathManager.I.HoverTile.name : "none") + ")";
            if (QuietGrids.Resting(g3)) return "still resting";
            foreach (var t in g3.tiles) if (Y(t) < t.RestY - 0.012f) return "tile " + t.gridX + "," + t.gridZ + " at " + Y(t).ToString("F3") + " under its rest " + t.RestY.ToString("F3");
            return g3.QuietLook > 0.01f ? "platform quiet look " + g3.QuietLook.ToString("F2") : null;
        });
        Park();
        yield return Wait(1.6f);
        Run(sb, "f. the pointer gone, it rests again", () => QuietGrids.Resting(g3) ? Heights(g3, true) : "not resting (ribbon " + (IslandHeader.Current == g3) + ")");
        // a cube in the hand wakes every grid
        PathManager.I.PickUpCube(Inst);
        yield return Wait(0.9f);
        Run(sb, "f. a cube in the hand wakes every grid (pitch reads as height while placing notes)", () =>
        {
            foreach (var kb in SM.Islands) { if (QuietGrids.Resting(kb)) return kb.name + " still rests"; string h = Heights(kb, false); if (h != null) return kb.name + ": " + h; }
            return Marks() == 0 ? null : Marks() + " repeat marks still drawn";
        });
        PathManager.I.PutDown(); Park();
        yield return Wait(1.1f);
        Run(sb, "f. the hand empty again, the empty grid rests", () => QuietGrids.Resting(g3) ? Heights(g3, true) : "not resting");
        Run(sb, "f. a REPEATED grid (the same instruments, tiles, lengths as the grid before it in its lane) rests and wears ONE repeat mark on its front edge; its cubes' tiles keep their height", () =>
        {
            if (QuietGrids.RestOf(g1) != QuietGrids.Rest.Repeat) return "RestOf = " + QuietGrids.RestOf(g1) + " (pattern " + QuietGrids.PatternOf(g1) + " vs " + QuietGrids.PatternOf(g0) + ", before " + (QuietGrids.Before(g1) != null ? QuietGrids.Before(g1).name : "none") + ")";
            if (QuietGrids.Repeats(g1) != g0) return "repeats " + (QuietGrids.Repeats(g1) != null ? QuietGrids.Repeats(g1).name : "none");
            if (!QuietGrids.Resting(g1)) return "not resting";
            var mark = QuietGrids.Mark(g1);
            if (mark == null) return "no mark";
            int marksOnIt = 0; foreach (Transform c in g1.transform) if (c.name == "RepeatMark" && c.gameObject.activeSelf) marksOnIt++;
            if (marksOnIt != 1) return marksOnIt + " marks on the grid";
            if (mark.localPosition.z > g1.tiles.Min(t => t.transform.localPosition.z)) return "the mark is not on the front edge (z " + mark.localPosition.z.ToString("F2") + ")";
            var stand = NodesOn(g1);
            if (stand.Count != 4) return "the cube stands on " + stand.Count + " tiles";
            string h = Heights(g1, true, stand); if (h != null) return h;
            foreach (var t in stand) if (t.QuietDrop != 0f) return "a tile under the cube drops " + t.QuietDrop;
            return null;
        });
        Run(sb, "f. the first grid of the pattern and the CHANGED grid do not rest and wear no mark (one mark in the whole song)", () =>
        {
            if (QuietGrids.RestOf(g0) != QuietGrids.Rest.None || QuietGrids.RestOf(g2) != QuietGrids.Rest.None) return "rest " + QuietGrids.RestOf(g0) + " / " + QuietGrids.RestOf(g2);
            if (QuietGrids.Mark(g0) != null || QuietGrids.Mark(g2) != null) return "a mark on the first / the changed grid";
            string h = Heights(g0, false) ?? Heights(g2, false); if (h != null) return h;
            if (Sat(Shown(g2.tiles[0])) < Sat(g2.tiles[0].BaseColor) - 0.01f) return "the changed grid is paler";
            return Marks() == 1 ? null : Marks() + " marks";
        });
        Run(sb, "f. one note longer, or another instrument, and the grid is no repeat any more (the mark goes); put back, it is one again", () =>
        {
            var c = ById(IdRepeat);
            int keep = c.durs[1]; c.durs[1] = 12; QuietGrids.ScanNow();
            var r1 = QuietGrids.RestOf(g1); bool m1 = QuietGrids.Mark(g1) != null;
            c.durs[1] = keep; int ki = c.instrument; c.instrument = 3; QuietGrids.ScanNow();
            var r2 = QuietGrids.RestOf(g1); bool m2 = QuietGrids.Mark(g1) != null;
            c.instrument = ki; QuietGrids.ScanNow();
            var r3 = QuietGrids.RestOf(g1);
            if (r1 != QuietGrids.Rest.None || m1) return "a changed length: " + r1 + ", mark " + m1;
            if (r2 != QuietGrids.Rest.None || m2) return "another instrument: " + r2 + ", mark " + m2;
            return r3 == QuietGrids.Rest.Repeat ? null : "put back: " + r3;
        });
        yield return Wait(0.5f);
        // while its column plays a resting grid looks normal
        GlobalClock.Seek(SM.ColumnStart(1) + 0.05f); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(0.9f);
        bool lit1 = SM.LitColumn == 1, restPlaying = QuietGrids.Resting(g1), mark1 = QuietGrids.Mark(g1) != null, rest3 = QuietGrids.Resting(g3);
        string hPlay = Heights(g1, false);
        GlobalClock.Stop(); GlobalClock.Seek(0);
        yield return Wait(1.0f);
        Run(sb, "f. while its column plays the repeated grid looks normal (tiles up, no mark); the empty grid of another column keeps resting; stopped, it rests again", () =>
        {
            if (!lit1) return "column 1 was not the lit column";
            if (restPlaying || mark1) return "resting " + restPlaying + ", mark " + mark1 + " while its column played";
            if (hPlay != null) return "while playing: " + hPlay;
            if (!rest3) return "the empty grid of column 3 woke";
            return QuietGrids.Resting(g1) && QuietGrids.Mark(g1) != null ? null : "after the stop: resting " + QuietGrids.Resting(g1) + ", mark " + (QuietGrids.Mark(g1) != null);
        });
        // nothing but the look changed
        var layoutOn = Layout();
        Run(sb, "f. the layout is untouched: every island's place and size, every tile's Top and every tile's collider are where they are with the look off", () =>
        {
            if (layoutOn.Count != layoutOff.Count) return layoutOn.Count + " vs " + layoutOff.Count + " poses";
            for (int i = 0; i < layoutOn.Count; i++)
            {
                if ((layoutOn[i].top - layoutOff[i].top).sqrMagnitude > 1e-8f) return "pose " + i + ": top " + layoutOn[i].top.ToString("F4") + " vs " + layoutOff[i].top.ToString("F4");
                if ((layoutOn[i].col - layoutOff[i].col).sqrMagnitude > 1e-5f) return "pose " + i + ": collider " + layoutOn[i].col.ToString("F4") + " vs " + layoutOff[i].col.ToString("F4");
            }
            return QuietGrids.Resting(g3) && QuietGrids.Resting(g1) ? null : "the grids were not resting while compared";
        });
        Run(sb, "f. the saved song is untouched (SongState before = after the grids rested, woke, played)", () =>
        {
            string j = JsonUtility.ToJson(SongState.Capture());
            if (j == json0) return null;
            int at = 0; while (at < j.Length && at < json0.Length && j[at] == json0[at]) at++;
            return "differs at char " + at + ": …" + json0.Substring(Mathf.Max(0, at - 40), Mathf.Min(80, json0.Length - Mathf.Max(0, at - 40))) + "… vs …" + j.Substring(Mathf.Max(0, at - 40), Mathf.Min(80, j.Length - Mathf.Max(0, at - 40)));
        });
        var notesOn = new List<VoiceRules.NoteEvent>();
        yield return Record(0f, 16f, notesOn);
        Run(sb, "f. one pass of the song hands the synth the same notes (sound, pitch, velocity, onset, length) with the grids at rest as with the look off", () =>
        {
            if (notesOff.Count == 0 || notesOn.Count == 0) return "no events at all: the audio clock may be frozen (environmental)";
            // one pass = the 16 beats from the first note (the recording runs a moment past the loop's wrap: the next pass's first note is not counted)
            var a = PassOf(notesOff); var b = PassOf(notesOn);
            if (a.Count != 12) return a.Count + " notes in the pass with the look off (the song has 12; " + refTries + " tries)";
            if (a.Count != b.Count) return a.Count + " notes with the look off, " + b.Count + " with it on";
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].slot != b[i].slot || a[i].midi != b[i].midi || a[i].vel != b[i].vel) return "note " + i + ": " + a[i].slot + "/" + a[i].midi + "/" + a[i].vel + " vs " + b[i].slot + "/" + b[i].midi + "/" + b[i].vel;
                double da = a[i].onDsp - a[0].onDsp, db = b[i].onDsp - b[0].onDsp;
                if (Math.Abs(da - db) > 0.004) return "note " + i + " onset " + da.ToString("F4") + " vs " + db.ToString("F4") + " s after the first";
                if (Math.Abs((a[i].offDsp - a[i].onDsp) - (b[i].offDsp - b[i].onDsp)) > 0.004) return "note " + i + " length differs";
            }
            return null;
        });
        Info(sb, "one pass: 12 notes compared (recorded " + notesOff.Count + " with the look off — take " + refTries + " —, " + notesOn.Count + " with the grids at rest, the loop's wrap included); scans so far " + QuietGrids.Scans);
        if (captures)
        {
            Frame(IslandAt(1).Center + new Vector3(4f, 0f, 0f), 34f);
            yield return Wait(0.6f);
            yield return Shot("quiet_grids.png");
        }

        // ================================================================ a. the ribbon at rest
        int kAt = SM.AddKeyboardIsland(-1, false); yield return Frames(3);
        int sAt = SongOps.AddStairIsland(3, false); yield return Frames(3);
        int mAt = SM.AddMoon(0); yield return Frames(3);
        History.Push();
        Info(sb, "added a keyboard (" + kAt + "), a staircase (" + sAt + "), a Moon (" + mAt + "); islands " + SM.Islands.Count);
        g0 = IslandAt(0);
        yield return ShowHeader(g0);
        var chordRest = H.RestControls(); var chordBand = H.ItemsShown();
        Run(sb, "a. a chord grid's ribbon at rest is exactly FIVE controls — move, mute, repeat, copy, ⋯ (the + ghosts above / below beside it)", () =>
        {
            if (H.Shown < 0.99f || IslandHeader.Current != g0) return "the header is not up on the grid";
            if (!SameOrder(chordBand, "Grip", "Eye", "Repeat", "DupRight", "More")) return "the band: " + Join(chordBand);
            var others = chordRest.Where(n => !chordBand.Contains(n)).ToList();
            return SameSet(others, "GhostAbove", "GhostBelow") ? null : "also drawn at rest: " + Join(others);
        });
        Run(sb, "b. the + ghosts on the left / right side are gone (they said the same as copy); above / below stay", () =>
        {
            if (H.AddLeftButton.gameObject.activeInHierarchy || H.AddRightButton.gameObject.activeInHierarchy) return "a side + is drawn";
            return H.AddAbove.gameObject.activeInHierarchy && H.AddBelow.gameObject.activeInHierarchy ? null : "the above / below ghosts are not up";
        });
        // ×2: the repeat's extras only under the pointer
        int u0 = History.UndoCount;
        H.Repeat.onClick(); yield return Wait(0.7f);
        bool calm = !H.StyleShown && !H.VaryShown && !H.RepeatMinus.gameObject.activeInHierarchy; var bandX2 = H.ItemsShown(); string tag = H.RepeatSticker;
        H.HoldRepeatHot(1.2f); yield return Frames(4);
        bool hot = H.StyleShown && H.VaryShown && H.RepeatMinus.gameObject.activeInHierarchy;
        H.Style.onClick(); yield return Frames(2); bool rew = IslandAt(0).rewind;
        H.HoldRepeatHot(1.2f); H.Vary.onClick(); yield return Frames(2); int vary = IslandAt(0).vary;
        H.HoldRepeatHot(1.2f); yield return Frames(2);
        H.RepeatMinus.onClick(); yield return Frames(3); int repAfter = IslandAt(0).repeat;
        yield return Wait(1.6f);
        Run(sb, "a. at ×2 the ribbon is still five (the ×2 tag on repeat); its −, rewind and vary show only while the pointer is on repeat — and work (rewind on, vary, one pass fewer)", () =>
        {
            if (tag != "×2") return "the tag reads '" + tag + "'";
            if (!calm) return "at rest at ×2: style " + !calm;
            if (!SameOrder(bandX2, "Grip", "Eye", "Repeat", "DupRight", "More")) return "the band at ×2: " + Join(bandX2);
            if (!hot) return "with the pointer on repeat: style " + H.StyleShown + " vary " + H.VaryShown;
            if (!rew || vary != 1 || repAfter != 1) return "rewind " + rew + ", vary " + vary + ", repeat after − " + repAfter;
            return !H.StyleShown && !H.VaryShown && !H.RepeatMinus.gameObject.activeInHierarchy ? null : "the extras stayed up after the pointer left";
        });
        while (History.UndoCount > u0) History.Undo();
        yield return Frames(4);
        yield return ShowHeader(IslandAt(0));
        yield return OpenMore();
        var moreNames = H.MoreControls();
        Run(sb, "a. the ⋯ card holds everything that left the ribbon — octave ▼ ▲, extend, launch, lead, copy below, copy paths, fill the section — and the grid's own settings (chord, mood, drum fill, climb, energy, length 1–4, the trash can)", () =>
        {
            string[] want = { "RegDown", "RegUp", "Extend", "Launch", "LeadSpot", "DupBelow", "CopyPaths", "FillSection", "Chord", "Mood", "Fill", "Climb", "Energy", "Bars1", "Bars2", "Bars3", "Bars4", "Remove" };
            foreach (var w in want) if (!moreNames.Contains(w)) return "missing " + w + " (has " + Join(moreNames) + ")";
            foreach (var b in new Component[] { H.RegisterDown, H.RegisterUp, H.Extend, H.Launch, H.LeadSpot, H.DuplicateBelow, H.CopyPathsButton, H.FillSection }) if (!H.InMore(b) || H.OnRibbon(b)) return b.name + " is not in the card";
            return null;
        });
        Run(sb, "a. the card adds no words: the labels are the seven it always had (chord, mood, drum fill, climb, energy, length, delete)", () =>
        {
            var words = new List<string>();
            foreach (var t in H.transform.Find("Ribbon/MoreCard").GetComponentsInChildren<TMPro.TextMeshProUGUI>(false)) { int d; if (t.gameObject.activeInHierarchy && !int.TryParse(t.text, out d) && !t.text.StartsWith("×")) words.Add(t.text); }
            return SameSet(words, "chord", "mood", "drum fill", "climb", "energy", "length", "delete") ? null : "words: " + string.Join(" · ", words.ToArray());
        });
        Run(sb, "a. the card's cells sit on an even grid (columns " + IslandHeader.CellW + " px apart, rows aligned)", () =>
        {
            var more = (RectTransform)H.transform.Find("Ribbon/MoreCard");
            var xs = new List<float>(); var ys = new List<float>();
            foreach (var n in new[] { "Extend", "Launch", "LeadSpot", "DupBelow", "CopyPaths", "FillSection", "Chord", "Mood", "Fill", "Climb", "Energy", "Remove" })
            { var rt = (RectTransform)more.Find(n); xs.Add(rt.anchoredPosition.x); ys.Add(rt.anchoredPosition.y); }
            foreach (float x in xs) { float k = x / IslandHeader.CellW; if (Mathf.Abs(k - Mathf.Round(k)) > 0.01f) return "x " + x + " is off the columns"; }
            return ys.Distinct().Count() <= 4 ? null : ys.Distinct().Count() + " different rows for twelve cells";
        });
        // one functional assert each (the card stays open; undo puts the song back)
        string moved = null;
        Func<string, bool, string> note = (what, ok) => ok ? null : what;
        {
            int reg = IslandAt(0).register; H.RegisterDown.onClick(); yield return Frames(3);
            moved = moved ?? note("octave ▼: register " + IslandAt(0).register, IslandAt(0).register == reg - 1);
            H.RegisterUp.onClick(); yield return Frames(3);
            moved = moved ?? note("octave ▲: register " + IslandAt(0).register, IslandAt(0).register == reg);
            H.Extend.onClick(); yield return Frames(4);
            moved = moved ?? note("extend: carry " + IslandAt(0).carry + " tag '" + H.ExtendSticker + "'", IslandAt(0).carry == 1 && H.ExtendSticker == "×1");
            History.Undo(); yield return Frames(4);
            yield return ShowHeader(IslandAt(0)); yield return OpenMore();
            H.FillSection.onClick(); yield return Frames(4);
            moved = moved ?? note("fill the section: carry " + IslandAt(0).carry, IslandAt(0).carry == 3);
            History.Undo(); yield return Frames(4);
            yield return ShowHeader(IslandAt(0)); yield return OpenMore();
            H.Launch.onClick(); yield return Frames(3);
            moved = moved ?? note("launch: " + IslandAt(0).launch, IslandAt(0).launch);
            H.Launch.onClick(); yield return Frames(3);
            H.LeadSpot.onClick(); yield return Frames(3);
            moved = moved ?? note("lead: " + IslandAt(0).lead, IslandAt(0).lead);
            H.LeadSpot.onClick(); yield return Frames(3);
            int cp = IslandHeader.CopyPathsCount; H.CopyPathsButton.onClick(); yield return Frames(3);
            moved = moved ?? note("copy paths: copies " + (IslandHeader.CopyPathsCount - cp) + ", clipboard has paths " + Clipboard.HasPaths, IslandHeader.CopyPathsCount == cp + 1 && Clipboard.HasPaths);
            int n0 = SM.Islands.Count; H.DuplicateBelow.onClick(); yield return Frames(4);
            moved = moved ?? note("copy below: islands " + n0 + " → " + SM.Islands.Count, SM.Islands.Count == n0 + 1);
            History.Undo(); yield return Frames(4);
            // paste paths / paste here on the empty grid
            var c0 = ById(IdA); bool copied = c0 != null && Clipboard.Copy(c0);
            var empty = SM.Islands.First(k => k.kind == 0 && QuietGrids.PatternOf(k) == "" && k.assignedChord.StartsWith("C"));
            yield return ShowHeader(empty); yield return OpenMore();
            moved = moved ?? note("paste paths / paste here are not offered in the empty grid's card (paths " + H.PastePathsShown + ", pattern " + H.PasteShown + ", copied " + copied + ")", H.PastePathsShown && H.PasteShown && H.InMore(H.PastePathsButton) && H.InMore(H.PasteHereButton));
            int q0 = Cubes(); H.PastePathsButton.onClick(); yield return Frames(4);
            moved = moved ?? note("paste paths: cubes " + q0 + " → " + Cubes(), Cubes() == q0 + 1);
            History.Undo(); yield return Frames(4);
            yield return ShowHeader(SM.Islands.First(k => k.kind == 0 && QuietGrids.PatternOf(k) == "" && k.assignedChord.StartsWith("C"))); yield return OpenMore();
            q0 = Cubes(); H.PasteHereButton.onClick(); yield return Frames(4);
            moved = moved ?? note("paste here: cubes " + q0 + " → " + Cubes(), Cubes() == q0 + 1);
            History.Undo(); yield return Frames(4);
            Clipboard.Clear(); Clipboard.ClearPaths();
        }
        Run(sb, "a. every moved control works from the card: octave ▼ ▲, extend, fill the section, launch, lead, copy paths, copy below, paste paths, paste here", () => moved);
        string own = null;
        {
            yield return ShowHeader(IslandAt(0)); yield return OpenMore();
            int mood = IslandAt(0).mood; H.MoodButton.onClick(); yield return Frames(4);
            own = own ?? note("mood: " + mood + " → " + IslandAt(0).mood, IslandAt(0).mood != mood);
            bool fill = IslandAt(0).fill; H.FillButton.onClick(); yield return Frames(4);
            own = own ?? note("drum fill: " + IslandAt(0).fill, IslandAt(0).fill != fill);
            int en = IslandAt(0).energy; H.EnergyButton.onClick(); yield return Frames(4);
            own = own ?? note("energy: " + en + " → " + IslandAt(0).energy, IslandAt(0).energy != en);
            int cl = Climb.LevelOf(IslandAt(0)); H.ClimbButton.onClick(); yield return Frames(4);
            own = own ?? note("climb: " + cl + " → " + Climb.LevelOf(IslandAt(0)), Climb.LevelOf(IslandAt(0)) != cl);
            H.BarsButton(2).onClick(); yield return Frames(4);
            own = own ?? note("length: bars " + IslandAt(0).bars, IslandAt(0).bars == 2);
            int n0 = SM.Islands.Count; H.DeleteButton.onClick(); yield return Frames(4);
            own = own ?? note("trash: islands " + n0 + " → " + SM.Islands.Count, SM.Islands.Count == n0 - 1);
            while (History.UndoCount > u0) History.Undo();
            yield return Frames(4);
        }
        Run(sb, "a. the grid's own settings still work from the card: mood, drum fill, energy, climb, length, the trash can (undo brings it all back)", () => own ?? (SM.Islands.Count(k => k.kind == 0) == 4 ? null : "after undo: " + SM.Islands.Count(k => k.kind == 0) + " chord grids"));

        // a keyboard
        var keys = OfKind(2);
        yield return ShowHeader(keys);
        var keysBand = H.ItemsShown(); var keysRest = H.RestControls();
        yield return OpenMore();
        var keysMore = H.MoreControls();
        int kc = keys.KeyCount; H.KeysAbove.onClick(); yield return Frames(4);
        int kc2 = OfKind(2).KeyCount;
        History.Undo(); yield return Frames(4);
        Run(sb, "a. a keyboard's ribbon: move, mute, repeat, copy, ⋯ (five); its octaves ▼ ▲, extend, launch, copy below and + octave below / above ride in the card and work", () =>
        {
            if (!SameOrder(keysBand, "Grip", "Eye", "Repeat", "DupRight", "More")) return "the band: " + Join(keysBand);
            if (keysRest.Count != 5) return "drawn at rest: " + Join(keysRest);
            foreach (var w in new[] { "RegDown", "RegUp", "Extend", "Launch", "DupBelow", "KeysBelow", "KeysAbove", "Remove" }) if (!keysMore.Contains(w)) return "the card misses " + w + " (has " + Join(keysMore) + ")";
            return kc2 == kc + 12 ? null : "+ octave above: keys " + kc + " → " + kc2;
        });
        // a staircase
        var st = OfKind(3);
        string stairs = null; List<string> stBand = null, stRest = null, stMore = null;
        if (st == null) stairs = "no staircase (SongOps.AddStairIsland → " + sAt + ")";
        else
        {
            yield return ShowHeader(st);
            stBand = H.ItemsShown(); stRest = H.RestControls();
            int dir = st.stairDir; H.StairDir.onClick(); yield return Frames(4);
            stairs = stairs ?? note("direction: " + dir + " → " + OfKind(3).stairDir, OfKind(3).stairDir != dir);
            yield return ShowHeader(OfKind(3)); yield return OpenMore();
            stMore = H.MoreControls();
            int steps = OfKind(3).stairSteps; H.StepsUp.onClick(); yield return Frames(4);
            stairs = stairs ?? note("steps: " + steps + " → " + OfKind(3).stairSteps, OfKind(3).stairSteps == steps + 1);
            int rate = OfKind(3).stairRate; H.StairRate.onClick(); yield return Frames(4);
            stairs = stairs ?? note("speed: " + rate + " → " + OfKind(3).stairRate, OfKind(3).stairRate != rate);
            bool lead = OfKind(3).stairLead; H.StairLead.onClick(); yield return Frames(4);
            stairs = stairs ?? note("lead-in: " + OfKind(3).stairLead, OfKind(3).stairLead != lead);
            int rep = OfKind(3).repeat; H.Repeat.onClick(); yield return Frames(4);
            stairs = stairs ?? note("repeat (in the card): " + rep + " → " + OfKind(3).repeat + ", in the card " + H.InMore(H.Repeat), OfKind(3).repeat == rep + 1 && H.InMore(H.Repeat));
            int type = OfKind(3).stairType, pick = (type + 2) % 6;
            H.StairType.onClick(); yield return Frames(3);
            bool fan = H.FanOpen && !IslandHeader.MoreOpen;
            H.FanItem(pick).onClick(); yield return Frames(4);
            stairs = stairs ?? note("kind: the fan opened " + fan + ", type " + type + " → " + OfKind(3).stairType + " (picked " + pick + ")", fan && OfKind(3).stairType == pick);
            while (History.UndoCount > u0) History.Undo();
            yield return Frames(4);
        }
        Run(sb, "a. a staircase's ribbon: move, mute, DIRECTION (its main job), copy, ⋯ (five); kind, steps, speed, lead-in, octave and repeat ride in the card and work", () =>
        {
            if (stBand == null) return stairs;
            if (!SameOrder(stBand, "Grip", "Eye", "StairDir", "DupRight", "More")) return "the band: " + Join(stBand);
            if (stRest.Count != 5) return "drawn at rest: " + Join(stRest);
            foreach (var w in new[] { "StairType", "StepsDown", "StepsUp", "StairRate", "StairLead", "RegDown", "RegUp", "Repeat", "DupBelow", "Remove" }) if (!stMore.Contains(w)) return "the card misses " + w + " (has " + Join(stMore) + ")";
            return stairs;
        });
        // a Moon
        List<string> moonBand = null, moonMore = null;
        if (SM.Moons.Count > 0)
        {
            IslandHeader.Hide(); yield return Frames(2);
            Frame(SM.Moons[0].Center, 14f); yield return Wait(0.25f);
            IslandHeader.Show(SM.Moons[0]); yield return Wait(0.45f);
            moonBand = H.ItemsShown();
            yield return OpenMore();
            moonMore = H.MoreControls();
        }
        Run(sb, "a. a Moon's ribbon: move, dice (its main job), ⋯ — whose card is the trash can", () =>
        {
            if (moonBand == null) return "no Moon";
            if (!SameOrder(moonBand, "Grip", "Dice", "More")) return "the band: " + Join(moonBand);
            return SameSet(moonMore, "Remove") ? null : "the card: " + Join(moonMore);
        });
        Info(sb, "controls drawn at rest, BEFORE (measured on the same build before this package, the gallery song get proto): chord grid 16 (up to 21 at ×2 with the clipboard full), keyboard 16, staircase 13, Moon 3");
        Info(sb, "controls drawn at rest, AFTER: chord grid " + chordBand.Count + " on the ribbon (+ the 2 + ghosts = " + chordRest.Count + "), keyboard " + (keysRest != null ? keysRest.Count : -1) + ", staircase " + (stRest != null ? stRest.Count : -1) + ", Moon " + (moonBand != null ? moonBand.Count : -1));
        if (captures)
        {
            yield return ShowHeader(IslandAt(1), 16f);
            yield return Shot("quiet_ribbon.png");
            yield return OpenMore(); yield return Wait(0.3f);
            yield return Shot("quiet_more.png");
        }

        // ================================================================ b. a cube / a pattern in the hand: no ribbon, no + ghosts
        var grp = H.GetComponent<CanvasGroup>();
        yield return ShowHeader(IslandAt(0));
        bool up0 = H.Shown > 0.99f && grp.blocksRaycasts;
        IslandHeader.Hide(); yield return Wait(0.9f);
        PointEdge(IslandAt(0)); yield return Wait(0.7f);
        bool hoverUp = H.Shown > 0.99f && IslandHeader.Current == IslandAt(0);   // (an empty hand on the edge: the ribbon comes — the control case)
        Park(); yield return Wait(0.2f);
        IslandHeader.Show(IslandAt(0)); yield return Wait(0.5f);
        PathManager.I.PickUpCube(Inst); yield return Wait(0.5f);
        bool hidPinned = H.Shown < 0.01f && !grp.blocksRaycasts && IslandHeader.Current == null && H.RestControls().Count >= 0 && grp.alpha < 0.01f;
        PointEdge(IslandAt(0)); yield return Wait(0.7f);
        bool onEdge = PathManager.I.hoverIsland == IslandAt(0);
        bool hidHover = H.Shown < 0.01f && !grp.blocksRaycasts && onEdge;
        Park(); PathManager.I.PutDown(); yield return Wait(0.6f);
        bool back = H.Shown > 0.99f && grp.blocksRaycasts && H.AddAbove.gameObject.activeInHierarchy && H.AddBelow.gameObject.activeInHierarchy;
        bool copiedPattern = Clipboard.Copy(ById(IdA)); bool inHand = PathManager.I.PickUpPattern(); yield return Wait(0.5f);
        bool hidPattern = H.Shown < 0.01f && !grp.blocksRaycasts;
        PathManager.I.PutDown(); Clipboard.Clear(); yield return Wait(0.5f);
        IslandHeader.Hide(); yield return Frames(3);
        UIManager.I.SelectMeasure(0, false); yield return Wait(0.6f);
        bool selUp = H.Shown > 0.99f && IslandHeader.Current == IslandAt(0);
        PathManager.I.PickUpCube(Inst); yield return Wait(0.5f);
        bool selHid = H.Shown < 0.01f && !grp.blocksRaycasts;
        PathManager.I.PutDown(); yield return Wait(0.6f);
        bool selBack = H.Shown > 0.99f;
        UIManager.I.ClearSelection(); IslandHeader.Hide(); yield return Frames(3);
        Run(sb, "b. with a cube in the hand there is no ribbon and no + ghost — pinned, hovered or selected — and with a copied pattern in the hand neither; they return with an empty hand", () =>
        {
            if (!up0) return "the pinned ribbon was not up to begin with";
            if (!hoverUp) return "an empty hand on the island's edge did not bring the ribbon (the control case)";
            if (!hidPinned) return "a cube in the hand: the pinned ribbon stayed (shown " + H.Shown + ")";
            if (!onEdge) return "the pointer was not on the island's edge";
            if (!hidHover) return "a cube in the hand, the pointer on the grid's edge: the ribbon came up";
            if (!back) return "the hand empty: the ribbon / its + ghosts did not return";
            if (!copiedPattern || !inHand) return "could not take a pattern in the hand";
            if (!hidPattern) return "a pattern in the hand: the ribbon stayed";
            return selUp && selHid && selBack ? null : "the selected island's ribbon: up " + selUp + ", hidden with a cube " + selHid + ", back " + selBack;
        });

        // ================================================================ c. the selection bar and the section header
        GridSelection.Set(new List<KeyBlock> { IslandAt(0), IslandAt(1) });
        Frame(IslandAt(1).Center, 22f);
        yield return Wait(0.8f);
        var bar = HudSelection.I;
        var drawn = new List<string>();
        foreach (HudSelection.Act a in Enum.GetValues(typeof(HudSelection.Act))) if (bar.Button(a) != null && bar.Button(a).gameObject.activeInHierarchy) drawn.Add(a.ToString());
        bool barUp = bar.Shown;
        if (captures) yield return Shot("quiet_selection.png");
        bar.Button(HudSelection.Act.Repeat).onClick(); yield return Frames(4);
        int rep0 = IslandAt(0).repeat, rep1 = IslandAt(1).repeat;
        History.Undo(); yield return Frames(4);
        GridSelection.Set(new List<KeyBlock> { IslandAt(0), IslandAt(1) }); yield return Frames(3);
        int regA = IslandAt(0).register; bool octOk = HudSelection.Do(HudSelection.Act.OctaveUp); yield return Frames(4);
        int regB = IslandAt(0).register;
        History.Undo(); yield return Frames(4);
        GridSelection.Clear(); yield return Frames(3);
        Run(sb, "c. the selection bar draws FOUR — copy, paste, repeat, the trash can; repeat works from it, and the ones that left keep their action (octave up: Shift+↑)", () =>
        {
            if (!barUp) return "the bar was not up";
            if (!SameSet(drawn, "Copy", "Paste", "Repeat", "Delete")) return "drawn: " + Join(drawn);
            if (rep0 != 2 || rep1 != 2) return "repeat from the bar: " + rep0 + " / " + rep1;
            return octOk && regB == regA + 1 ? null : "octave up through Do: " + octOk + ", register " + regA + " → " + regB;
        });
        // the section header
        var sh = SectionHeader.I;
        IslandHeader.Hide(); yield return Frames(3);
        Frame(IslandAt(1).Center + new Vector3(4f, 0f, 0f), 34f); yield return Wait(0.3f);
        float cut = (IslandAt(1).EastEdge + IslandAt(2).WestEdge) * 0.5f;
        SectionHeader.Show(0, cut); yield return Wait(0.8f);
        var secBand = sh.ItemsShown();
        bool moreShut = !sh.MoreOpen && !sh.SplitButton.gameObject.activeInHierarchy;
        sh.NameButton.onClick(); yield return Frames(3);
        bool fanUp = sh.RoleFanOpen;
        sh.RoleItem(2).onClick(); yield return Frames(4);
        int role = SM.SectionRole(0);
        sh.MoreButton.onClick(); yield return Frames(3);
        bool moreUp = sh.MoreOpen && sh.SplitButton.gameObject.activeInHierarchy && sh.JoinButton.gameObject.activeInHierarchy && sh.DrumsButton.gameObject.activeInHierarchy && sh.LaunchButton.gameObject.activeInHierarchy;
        if (captures) { yield return Wait(0.3f); yield return Shot("quiet_section.png"); }
        int moons0 = SM.Moons.Count; sh.DrumsButton.onClick(); yield return Frames(4);
        int moons1 = SM.Moons.Count;
        sh.LaunchButton.onClick(); yield return Frames(4);
        bool launching = SectionHeader.Launching(0);
        int splitAt = sh.SplitColumn, secs0 = SM.SectionCount; sh.SplitButton.onClick(); yield return Frames(5);
        int secs1 = SM.SectionCount;
        SectionHeader.Show(0); yield return Wait(0.8f);
        if (!sh.MoreOpen) sh.MoreButton.onClick(); yield return Frames(3);
        sh.JoinButton.onClick(); yield return Frames(5);
        int secs2 = SM.SectionCount;
        SectionHeader.Hide(); yield return Frames(3);
        Run(sb, "c. the section header draws FOUR — the letter (a click opens the name fan), copy, the trash can, ⋯ — and split, join, drums and the build-up work from its ⋯ card", () =>
        {
            if (!SameOrder(secBand, "Letter", "Duplicate", "Delete", "More")) return "the band: " + Join(secBand);
            if (!moreShut) return "the ⋯ card was open at rest";
            if (!fanUp || role != 2) return "the letter: fan " + fanUp + ", role → " + role;
            if (!moreUp) return "the ⋯ card did not open with its four";
            if (moons1 != moons0 + 1) return "drums: Moons " + moons0 + " → " + moons1;
            if (!launching) return "build-up: the section does not launch";
            if (secs1 != secs0 + 1) return "split (at column " + splitAt + "): sections " + secs0 + " → " + secs1;
            return secs2 == secs0 ? null : "join: sections " + secs1 + " → " + secs2;
        });

        // ================================================================ d. the punch pads
        var tr = UnityEngine.Object.FindFirstObjectByType<HudTransport>();
        GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(0.6f);
        bool playing = GlobalClock.IsPlaying;
        bool padsUp = tr != null && (tr.Punch.gameObject.activeInHierarchy || tr.Stutter.gameObject.activeInHierarchy || tr.Funnel.gameObject.activeInHierarchy || tr.Half.gameObject.activeInHierarchy || tr.Drop.gameObject.activeInHierarchy);
        Performance.HoldStutter(true); yield return Wait(0.5f);
        bool stutter = Performance.StutterActive;
        Performance.HoldStutter(false); yield return Wait(0.3f);
        bool released = !Performance.StutterActive;
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Run(sb, "d. no punch pads are drawn while playing; the pads stay built for the keys (UIManager.UpdatePunch reads J K H B through them) and the stutter punch-in still engages and releases", () =>
        {
            if (tr == null) return "no transport";
            if (!playing) return "the song did not play";
            if (padsUp) return "the pads are on screen";
            if (tr.Stutter == null || tr.Funnel == null || tr.Half == null || tr.Drop == null) return "a pad is gone (the keys' update needs them)";
            return stutter && released ? null : "stutter: engaged " + stutter + ", released " + released;
        });
        Run(sb, "d. J K H B stay on the shortcut sheet", () =>
        {
            foreach (var col in HudShortcuts.Columns) foreach (var sec in col) foreach (var e in sec.rows) if (e.glyph == "punch" && string.Join(" ", e.keys).Contains("j")) return null;
            return "no punch row";
        });

        // ================================================================ e. no destructive control draws the cross
        yield return ShowHeader(IslandAt(0)); yield return OpenMore();
        int islandCan = ActivePainterTrash(H.DeleteButton);
        IslandHeader.Hide(); yield return Frames(3);
        SectionHeader.Show(0); yield return Wait(0.8f);
        int sectionCan = ActivePainterTrash(sh.DeleteButton);
        SectionHeader.Hide(); yield return Frames(3);
        GridSelection.Set(new List<KeyBlock> { IslandAt(0) }); yield return Wait(0.8f);
        int barCan = ActivePainterTrash(bar.Button(HudSelection.Act.Delete));
        GridSelection.Clear(); yield return Frames(3);
        Clipboard.Copy(ById(IdA)); yield return Wait(0.8f);
        var chip = HudClipboard.I; int chipCan = 0; bool chipUp = chip != null && chip.Shown;
        if (chip != null) { chip.ShowClear(); yield return Frames(2); chipCan = ActivePainterTrash(chip.ClearButton); chip.ShowClear(); chip.ClearButton.onClick(); yield return Frames(3); }
        bool cleared = !Clipboard.HasPattern;
        Run(sb, "e. delete is the trash can (the island ⋯ card's drawing) on the island card, the section header, the selection bar and the clipboard chip's clear — which still clears", () =>
        {
            if (islandCan < 1) return "the island card's delete drew " + islandCan + " cans";
            if (sectionCan < 1) return "the section header's delete drew " + sectionCan + " cans";
            if (barCan < 1) return "the selection bar's delete drew " + barCan + " cans";
            if (!chipUp) return "the clipboard chip was not up";
            if (chipCan < 1) return "the chip's clear drew " + chipCan + " cans";
            return cleared ? null : "the chip's clear did not clear";
        });
        Run(sb, "e. the shortcut sheet draws the trash can on every delete row and the cross on none", () =>
        {
            int deletes = 0;
            foreach (var col in HudShortcuts.Columns) foreach (var sec in col) foreach (var e in sec.rows)
            {
                if (e.glyph == "cross") return "'" + e.words + "' draws the cross";
                if (e.words.StartsWith("delete")) { deletes++; if (e.glyph != "trash") return "'" + e.words + "' draws " + e.glyph; }
            }
            return deletes >= 4 ? null : deletes + " delete rows";
        });
        HudShortcuts.Open(); yield return Wait(0.5f);
        int crosses = 0, cans = 0; string stray = null;
        foreach (var p in UnityEngine.Object.FindObjectsByType<InkPath>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (p.Glyph == "trash") cans++;
            if (p.Glyph != "cross") continue;
            crosses++;
            bool closes = false; for (var t = p.transform; t != null; t = t.parent) if (t.name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0) { closes = true; break; }
            if (!closes && stray == null) stray = "a cross on " + (p.transform.parent != null ? p.transform.parent.name : "?") + "/" + p.name;
        }
        HudShortcuts.Close(); yield return Wait(0.3f);
        Run(sb, "e. every tagged drawing in the scene (InkPath.Glyph): the cross is only ever on a control that closes (the shortcut sheet's, the cube card's)", () =>
        {
            if (stray != null) return stray;
            return crosses >= 1 ? null : "found no cross at all (the scan saw nothing; " + cans + " cans)";
        });
        Info(sb, "tagged drawings: " + crosses + " crosses (all on Close controls), " + cans + " trash cans");

        // ================================================================ g. + captures
        Run(sb, "g. no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3)));
        if (captures)
        {
            bool opened = false;
            try { Gallery.ClearTestSongs(); Gallery.Reload(); opened = Gallery.Open("get-proto", false); } catch (Exception e) { Info(sb, "get proto: " + e.Message); }
            if (opened)
            {
                yield return Wait(3f);
                Park(); SectionHeader.Hide(); IslandHeader.Hide();
                if (OrbitCamera.I != null) OrbitCamera.I.FrameColumn(2, 0.2f);
                yield return Wait(2f);
                int e2, r2, a2; QuietGrids.Count(out e2, out r2, out a2);
                Info(sb, "get proto: " + SM.Islands.Count(k => k.kind == 0) + " chord grids — " + e2 + " empty and " + r2 + " repeated ones at rest (the same instruments, tiles and lengths as the grid before), " + Marks() + " repeat marks");
                yield return Shot("quiet_proto.png");
                GlobalClock.Stop();
            }
            else Info(sb, "get proto did not open: no quiet_proto.png");
        }
        Application.logMessageReceived -= OnLog;
        try { GlobalClock.Stop(); SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); History.Reset(); History.Push(); } catch (Exception e) { Info(sb, "fixture reload: " + e.Message); }
        Restore();
        V3Fixes.RestoreSaves(saves);
        int pass = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS")), fail = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append("DONE ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }
}
