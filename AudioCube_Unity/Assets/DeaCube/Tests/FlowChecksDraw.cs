using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Flow (F2, SPEC flow §2) — NOTE ENTRY THAT NEVER HITS A WALL, Play-mode verification through the real input path (PathManager.SimPointer for
/// presses / drags / hovers, KeyShim + RunHotkeysForTest for keys, the draft row's own buttons): <c>FlowChecksDraw.RunAll(captures)</c> starts a
/// coroutine; poll <c>FlowChecksDraw.Done</c> or Captures/flow_draw_report.txt (PASS / FAIL lines by section letter, INFO lines);
/// <c>RunOnly("a,e")</c> runs some sections. Test songs are built from SongState (120 bpm, 4/4): four one-bar chord grids in one section, the same
/// with a second lane in column 0, a one-chord song, five columns in two sections, a keyboard between two chords.
/// a. ten quarter notes clicked on grid 0 walk on through grids 1 and 2 (span 2; nodes 0–3 / 4–7 / 8–9 by GridOfNode) with no refusal, no bar
///    added, no History entry until the pen goes up — then ONE, and one undo takes the whole run back; the beat pips fill across the chain.
/// b. the mirrored click: with the next start on grid 1 a click (and the hover ghost) on grid 0's tile (x, z) lands on grid 1's (x, z); the
///    first note on a new grid is a leap; a direct click on the chain's grid draws no arc; the game's camera eases to frame the chain when the
///    melody steps onto a grid that is off screen.
/// c. a lane with no grid in the next column: R1 makes one (the lane's z, that column's chord), Backspace gives it back; one undo for the run;
///    a keyboard lane gets a keyboard with the same keys (the same key there).
/// d. R2 a one-chord song grows a measure instead (and gives it back); R3 at a section's end a new draft starts on the next section's first
///    column, at the song's end a column is appended (Backspace steps back onto the first cube and takes the column away); one undo for both.
/// e. rest: note, R, note → mods 0 1 0 with the brush lengths; the draft row's hollow cube; Backspace; a pickup rest (R before the first tile);
///    a draft that never got a note leaves no cube and no History entry.
/// f. the same tile twice = two notes on grids AND keyboards; right-click / Enter / ✓ / Esc / a click on the sea each finish and KEEP the notes;
///    Esc twice puts the hand down; a click on a grid outside the chain finishes and starts the next path there.
/// g. a 3 px slip during a click adds one note; a drag paints notes (only onto another tile, after 8 px); the release does not finish.
/// h. transport: from stopped nothing starts and Space toggles the loop over the draft's columns (the range grows with the span), the pen up
///    puts the clock back at its place; drawing while playing never pauses, loops the draft's columns and plays on after the pen up.
/// i. resume: a "+" badge lies by the last bead of a finished path of the held group; a click there picks the path up again (the same cube),
///    Backspace takes its last old note, the pen up is one History entry (none when nothing changed).
/// j. no words: no fill tab, no "capacity reached", no expand chip, no measures tag; one size row (HudSizeRow hidden while drafting).
/// k. PathManager.Flow = false: the v7 rules still hold (refusal + chip, the last tile finishes, Esc drops the draft, a playing song pauses).
/// l. no console errors; Synth late / errors (late notes around the suite's own transport jumps and captures counted apart).
/// Captures: flow_draw_chain.png, flow_draw_mirror.png, flow_draw_rest.png, flow_draw_resume.png. Never writes the user's saves (snapshotted and
/// restored); reloads the fixture at the end.
/// </summary>
public static class FlowChecksDraw
{
    public static string Report = "";
    public static bool Done = true;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "flow_draw_report.txt");

    static StringBuilder sb;
    static readonly List<string> errors = new List<string>();
    static int pushes, lateExcused;
    static bool shots;
    static readonly Vector3 Sea = new Vector3(-50f, -50f, 0f);
    delegate string Check();

    // ================================================================== helpers
    static void Line(bool ok, string name, string detail)
    {
        sb.Append(ok ? "PASS " : "FAIL ").Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(); }
    static void Flush() { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running " + Progress + "\n")); } catch (Exception) { } }
    static void Run(string name, Check c)
    {
        try { string d = c(); Line(d == null, name, d ?? "ok"); }
        catch (Exception e) { Line(false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    static string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    static SongManager SM => SongManager.I;
    static PathManager PM => PathManager.I;
    static void OnHistory() { pushes++; }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static KeyBlock Grid(int i) => SM != null && i >= 0 && i < SM.Islands.Count ? SM.Islands[i] : null;
    static int IndexOf(KeyBlock kb) => kb == null ? -1 : SM.Islands.IndexOf(kb);
    static TileInteraction T(int g, int x, int z) { var kb = Grid(g); return kb != null ? kb.GetTile(x, z) : null; }
    static int UserCubes() { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) n++; return n; }
    static AudioCube ById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }
    static AudioCube Newest() { AudioCube b = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && (b == null || c.id > b.id)) b = c; return b; }

    /// <summary>A cube's notes as "grid(x,z)" per node (r = a rest, xN = a length other than a beat): where each one sounds (RealTile / GridOfNode).</summary>
    static string Seq(AudioCube c)
    {
        if (c == null) return "-";
        var s = new StringBuilder();
        for (int i = 0; i < c.nodes.Count; i++)
        {
            var rt = c.RealTile(i);
            if (i > 0) s.Append(' ');
            s.Append(IndexOf(c.GridOfNode(i))).Append('(').Append(rt != null ? rt.gridX : -1).Append(',').Append(rt != null ? rt.gridZ : -1).Append(')');
            if (c.ModOf(i) == 1) s.Append('r');
            if (i < c.durs.Count && c.durs[i] != 24) s.Append('x').Append(c.durs[i]);
        }
        return s.ToString();
    }

    // ------------------------------------------------------------------ the test songs
    const int Pluck = 1, Keys = 0;
    const int IdOld = 9501;
    static readonly string[] ChordNames = { "Am7", "Dm7", "G7", "Cmaj7", "Fmaj7" };
    static readonly int[] ChordRoots = { 57, 62, 55, 60, 53 };
    static readonly int[][] ChordSemis = { new[] { 0, 3, 7, 10 }, new[] { 0, 3, 7, 10 }, new[] { 0, 4, 7, 10 }, new[] { 0, 4, 7, 11 }, new[] { 0, 4, 7, 11 } };

    static MeasureState Chord(int col, int chord = -1) { int k = chord < 0 ? col : chord; return new MeasureState { chordKey = ChordNames[k], root = ChordRoots[k], semis = (int[])ChordSemis[k].Clone(), bars = 1, kind = 0, col = col, repeat = 1, energy = 2 }; }
    static MeasureState Piano(int col) => new MeasureState { chordKey = "Keys", root = 60, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, keyCount = 25 };
    static SongState Song(string name, MeasureState[] ms, int[] sections = null, params CubeState[] cubes) => new SongState
    {
        version = SongState.CurrentVersion, name = name, bpm = 120f, beatsPerBar = 4, loop = true, measures = ms, cubes = cubes, sections = sections,
        moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
    };
    static MeasureState[] FourChords() => new[] { Chord(0), Chord(1), Chord(2), Chord(3) };
    static CubeState OldCube() => new CubeState
    {
        instrument = Pluck, measure = 0, xs = new[] { 0, 1, 2, 3 }, zs = new[] { 3, 3, 3, 3 }, rests = new bool[4], mods = new int[4], durs = new[] { 24, 24, 24, 24 },
        volume = 1f, moon = -1, twinOf = -1, echoOf = -1, id = IdOld, step = 1, gate = 1, mode = 0, hits = -1
    };

    static void LoadState(SongState st)
    {
        var pm = PM;
        if (pm.IsDrawing) pm.CancelPath(false);
        pm.PutDown();
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.ClearRegion();
        GlobalClock.Stop();
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
        AudioCube.SnapAllHome();
        PathManager.SimPos = Sea;
        pm.SetBrush(24);
    }

    // ------------------------------------------------------------------ the camera and the pointer
    static void Frame(Vector3 focus, float dist, float yaw = 0f, float pitch = 60f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    /// <summary>Frames grids <paramref name="a"/> .. <paramref name="b"/> from above so that every tile of them picks itself (tries a few poses).</summary>
    static bool FrameGrids(int a, int b)
    {
        var tiles = new List<TileInteraction>();
        Vector3 c = Vector3.zero; int n = 0;
        for (int g = a; g <= b; g++) { var kb = Grid(g); if (kb == null) continue; foreach (var t in kb.tiles) if (t != null) { tiles.Add(t); c += t.Top; n++; } }
        if (n == 0) return false;
        c /= n;
        float[] dists = { 14f, 18f, 22f, 27f, 33f, 40f };
        float[] pitches = { 62f, 72f, 80f, 86f };
        foreach (float p in pitches)
            foreach (float d in dists)
            {
                Frame(c, d, 0f, p);
                Physics.SyncTransforms();
                bool ok = true;
                foreach (var t in tiles)
                {
                    Vector3 s = Scr(t.Top);
                    if (s.z <= 0f || s.x < 8f || s.y < 8f || s.x > Screen.width - 8f || s.y > Screen.height - 8f) { ok = false; break; }
                    var r = PM.PickAt(s);
                    if (r.tile != t && r.cube == null) { ok = false; break; }   // (a tile that carries a resting cube picks the cube when nothing is drawn)
                }
                if (ok) return true;
            }
        return false;
    }

    static void Key(KeyCode k, bool cmd = false)
    {
        if (cmd) KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
        KeyShim.Sim(k, true, true, false);
        PM.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static IEnumerator Click(TileInteraction t)
    {
        if (t == null) { Info("Click: no tile"); yield break; }
        Vector3 s = Scr(t.Top);
        PathManager.SimPos = s;
        PM.SimPointer(s, true, true, false);
        PM.SimPointer(s, false, false, true);
        yield return null;
    }
    /// <summary>A right-click (its release) at a screen point. A real right press resets the camera's last right-drag first (an earlier suite's
    /// orbit would make this release read as the end of a drag: V6ChecksH's remedy).</summary>
    static void RightClick(Vector3 s)
    {
        if (OrbitCamera.I != null) OrbitCamera.I.SimRightPress();
        PM.SimPointer(s, false, false, false, true);
    }
    static IEnumerator ClickAt(Vector3 s)
    {
        PathManager.SimPos = s;
        PM.SimPointer(s, true, true, false);
        PM.SimPointer(s, false, false, true);
        yield return null;
    }
    static IEnumerator Clicks(int g, params int[] xz)
    {
        for (int i = 0; i + 1 < xz.Length; i += 2) yield return Click(T(g, xz[i], xz[i + 1]));
    }
    static IEnumerator Hover(TileInteraction t)
    {
        Vector3 s = Scr(t.Top);
        PathManager.SimPos = s + new Vector3(3f, 0f, 0f);
        yield return null;
        PathManager.SimPos = s;
        PM.SimPointer(s, false, false, false);
        yield return null; yield return null;
    }

    /// <summary>A screen capture (overlay UI included) from the game's own camera framing <paramref name="a"/> .. <paramref name="b"/>; the test
    /// camera comes back afterwards. Late notes around a capture while playing are counted apart.</summary>
    static IEnumerator Capture(string file, int a, int b, float tight = 1f)
    {
        if (!shots) yield break;
        int late = Synth.LateEvents;
        var cam = Camera.main;
        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
        Vector3 sim = PathManager.SimPos;
        if (OrbitCamera.I != null)
        {
            OrbitCamera.I.Suspended = false;
            Bounds bb = Grid(a).VisualBounds; for (int g = a + 1; g <= b; g++) if (Grid(g) != null) bb.Encapsulate(Grid(g).VisualBounds);
            OrbitCamera.I.FrameBounds(bb, 0.1f, true, tight);
            yield return Wait(0.5f);
        }
        Directory.CreateDirectory(V2Checks.CapturePath);
        string path = System.IO.Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path, 1);
        yield return null; yield return null;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        cam.transform.position = p0; cam.transform.rotation = r0;
        PathManager.SimPos = sim;
        yield return null;
        if (GlobalClock.IsPlaying) { yield return Wait(0.3f); }
        lateExcused += Synth.LateEvents - late;
    }

    /// <summary>The words on screen now that the draft must not show: any active text holding "capacity", " beats" or "measure".</summary>
    static string DraftWords()
    {
        var found = new List<string>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            if (t == null || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
            var cg = t.GetComponentInParent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.05f) continue;
            string s = t.text.ToLowerInvariant();
            if (s.Contains("capacity") || s.Contains(" beats") || s.Contains("measure")) found.Add(t.text);
        }
        return string.Join(" | ", found.ToArray());
    }

    // ================================================================== entry points
    public static string RunAll(bool captures = true) => Start(null, captures);
    /// <summary>Some sections: a comma list of the letters a .. k.</summary>
    public static string RunOnly(string which, bool captures = false) => Start(which, captures);

    static string Start(string only, bool captures)
    {
        if (SequenceMaster.I == null || SongManager.I == null || PathManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start"; shots = captures; errors.Clear(); pushes = 0; lateExcused = 0;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(only));
        return "started" + (only != null ? " " + only : "");
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        PathManager.AutoHand = false; PathManager.Flow = true;
        PathManager.SimOnly = true; PathManager.SimPos = Sea; PathManager.SimShift = false;
        var pm = PM;
        if (pm.IsDrawing) pm.CancelPath(false);
        if (pm.Drag != null && pm.Drag.Busy) pm.Drag.Cancel();
        pm.Deselect();
        pm.PutDown();
        pm.SetBrushShape(false);   // (an earlier suite may leave the ball shape chosen: these checks draw cubes — a far click fills the line)
        for (int i = 0; i < Instruments.Count; i++) Instruments.BrushVoice[i] = 0;
        GridSelection.Clear();
        KeyShim.Clear();
    }

    static readonly string[] Sections = { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" };

    static IEnumerator Routine(string only)
    {
        sb = new StringBuilder();
        sb.Append("FlowChecksDraw ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float bpm0 = GlobalClock.BPM;
        bool auto0 = PathManager.AutoHand, flow0 = PathManager.Flow;
        int brush0 = PM.BrushTicks;
        History.OnChanged += OnHistory;
        foreach (var s in Sections)
        {
            if (only != null && Array.IndexOf(only.Split(','), s) < 0) continue;
            Progress = s;
            Prepare();
            IEnumerator body = null;
            switch (s)
            {
                case "a": body = Chain(); break;
                case "b": body = Mirror(); break;
                case "c": body = Lane(); break;
                case "d": body = Overflow(); break;
                case "e": body = Rests(); break;
                case "f": body = Finishes(); break;
                case "g": body = Drags(); break;
                case "h": body = Transport(); break;
                case "i": body = ResumeChecks(); break;
                case "j": body = Words(); break;
                case "k": body = Legacy(); break;
            }
            int lateS = Synth.LateEvents;
            if (body != null) yield return Guard(body);
            if (Synth.LateEvents != lateS) Info("section " + s + ": synth late +" + (Synth.LateEvents - lateS) + " (last: " + Synth.LastLate + ")");
            PathManager.Flow = true;
            var pm = PM;
            if (pm.IsDrawing) pm.CancelPath(false);
            pm.PutDown();
            CubeInspector.CloseImmediate();
            FocusLoop.Dismiss(); GlobalClock.Stop(); GlobalClock.ClearRegion();
            if (GlobalClock.BPM != bpm0) GlobalClock.SetBPM(bpm0);
        }
        Progress = "finish";
        if (only == null || only.Contains("l"))
        {
            Run("l. no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3).ToArray()));
            Run("l. Synth late / errors unchanged (late notes right after the suite's own transport jumps, rebuilds while playing and captures counted apart: " + lateExcused + ")", () =>
            {
                int late = Synth.LateEvents - late0 - lateExcused, err = Synth.Errors - err0;
                return late == 0 && err == 0 ? null : "late +" + late + " (+" + lateExcused + " apart), errors +" + err + " — last late: " + Synth.LastLate;
            });
        }
        Info("synth: " + Synth.Stats());
        Application.logMessageReceived -= OnLog;
        History.OnChanged -= OnHistory;
        Prepare();
        try { GlobalClock.Stop(); SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); History.Reset(); History.Push(); } catch (Exception e) { Info("fixture reload: " + e.Message); }
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false; PathManager.SimShift = false;
        PM.PutDown();
        PM.SetBrush(brush0);
        PathManager.AutoHand = auto0; PathManager.Flow = flow0;
        V3Fixes.RestoreSaves(saves);
        int pass = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS")), fail = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append("DONE ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true; Progress = "done";
        Flush();
    }

    static IEnumerator Guard(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            object cur;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                cur = stack.Peek().Current;
            }
            catch (Exception e) { Line(false, Progress + ". threw", e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); yield break; }
            var inner = cur as IEnumerator;
            if (inner != null) { stack.Push(inner); continue; }
            yield return cur;
        }
    }

    // ================================================================== a. the ten-note run
    static IEnumerator Chain()
    {
        var pm = PM;
        LoadState(Song("flow draw a", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 2)) { Line(false, "a. the tiles are pickable", "no camera pose picks every tile of grids 0–2"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int u0 = History.UndoCount, p0 = pushes, rf0 = PathManager.CapacityRefusals, sp0 = PathManager.FlowSpans, cubes0 = UserCubes();
        float total0 = SM.TotalBeats;
        // every click lands on GRID 0's tiles: the notes past its measure are mirrored onto grids 1 and 2
        yield return Clicks(0, 0, 0, 1, 0, 2, 0, 3, 0);
        var d4 = pm.Draft;
        string path4 = Seq(d4);
        yield return Clicks(0, 3, 1, 2, 1, 1, 1, 0, 1, 0, 2, 1, 2);
        yield return Frames(2);
        var d = pm.Draft;
        Run("a. ten quarter notes clicked on grid 0: no refusal — ONE draft cube ends with span 2, nodes 0–3 on grid 0, 4–7 on grid 1, 8–9 on grid 2 (GridOfNode), each on its own (x, z)", () =>
        {
            if (d == null || !pm.IsDrawing) return "not drawing";
            if (d != d4) return "the draft changed cubes";
            if (d.nodes.Count != 10 || d.span != 2) return "nodes " + d.nodes.Count + " span " + d.span + ": " + Seq(d);
            string want = "0(0,0) 0(1,0) 0(2,0) 0(3,0) 1(3,1) 1(2,1) 1(1,1) 1(0,1) 2(0,2) 2(1,2)";
            if (Seq(d) != want) return "path " + Seq(d) + ", want " + want;
            if (path4 != "0(0,0) 0(1,0) 0(2,0) 0(3,0)") return "after four: " + path4;
            if (PathManager.CapacityRefusals != rf0 || pm.CapacityReached) return "refusals +" + (PathManager.CapacityRefusals - rf0) + " reached " + pm.CapacityReached;
            if (PathManager.FlowSpans != sp0 + 2) return "span steps +" + (PathManager.FlowSpans - sp0);
            if (pm.DraftChain.Count != 3 || pm.DraftChain[0] != Grid(0) || pm.DraftChain[2] != Grid(2) || pm.NextNoteGrid != Grid(2)) return "chain " + pm.DraftChain.Count + " next " + IndexOf(pm.NextNoteGrid);
            return null;
        });
        Run("a. the chord grids keep their measures and the section its length; nothing is in History while the pen is down", () =>
        {
            for (int i = 0; i < 4; i++) if (Grid(i).bars != 1 || SM.ColumnBars(i) != 1) return "grid " + i + " has " + Grid(i).bars + " bars";
            if (SM.Islands.Count != 4 || SM.ColumnCount != 4 || SM.SectionCount != 1 || SM.SectionBars(0) != 4) return "islands " + SM.Islands.Count + " columns " + SM.ColumnCount + " sections " + SM.SectionCount + " bars " + SM.SectionBars(0);
            if (Mathf.Abs(SM.TotalBeats - total0) > 1e-3f) return "total " + total0 + " -> " + SM.TotalBeats;
            if (pushes != p0 || History.UndoCount != u0) return "pushes +" + (pushes - p0) + " undo " + u0 + " -> " + History.UndoCount;
            return null;
        });
        Run("a. the beat pips carry the fill across the chain: grid 0 and grid 1 full (4 beats), grid 2 two beats and the next note's beat, grid 3 none", () =>
        {
            float f0 = Grid(0).DraftFillBeats, f1 = Grid(1).DraftFillBeats, f2 = Grid(2).DraftFillBeats, f3 = Grid(3).DraftFillBeats;
            var m2 = Grid(2).KindMarks;
            if (Mathf.Abs(f0 - 4f) > 1e-3f || Mathf.Abs(f1 - 4f) > 1e-3f || Mathf.Abs(f2 - 2f) > 1e-3f || f3 >= 0f) return "fill " + F(f0) + " " + F(f1) + " " + F(f2) + " " + F(f3);
            if (m2 != null && (m2.FillStateOf(1) != 1 || m2.FillStateOf(2) != 2 || m2.FillStateOf(3) != 0)) return "grid 2 pips " + m2.FillStateOf(0) + m2.FillStateOf(1) + m2.FillStateOf(2) + m2.FillStateOf(3);
            for (int g = 0; g < 3; g++) { var m = Grid(g).KindMarks; if (m != null) for (int i = 0; i < m.PipCount; i++) if (m.FillStateOf(i) == 3) return "grid " + g + " pip " + i + " reads past the end"; }
            return null;
        });
        yield return Hover(T(0, 2, 2));
        yield return Wait(0.25f);
        yield return Capture("flow_draw_chain.png", 0, 3);
        Key(KeyCode.Return);
        yield return Frames(3);
        var c = Newest();
        int id = c != null ? c.id : 0;
        Run("a. Enter: the pen goes up — one finished cube (span 2, ten notes), ONE History entry for the whole run, the fill gone", () =>
        {
            if (pm.IsDrawing || pm.InRun) return "still drawing";
            if (c == null || c != d || !c.isFinalized || c.span != 2 || c.nodes.Count != 10) return "cube " + (c == null ? "none" : "span " + c.span + " nodes " + c.nodes.Count);
            if (UserCubes() != cubes0 + 1) return "cubes " + cubes0 + " -> " + UserCubes();
            if (History.UndoCount != u0 + 1) return "undo " + u0 + " -> " + History.UndoCount;
            for (int g = 0; g < 4; g++) if (Grid(g).DraftFillBeats >= 0f) return "grid " + g + " still shows a fill";
            if (pm.Hand != PathManager.HandKind.Cube) return "the hand is " + pm.Hand;
            return null;
        });
        History.Undo();
        yield return Frames(3);
        Run("a. one undo takes the whole run back (no cube, the four grids as they were)", () =>
        {
            if (ById(id) != null || UserCubes() != cubes0) return "cubes " + UserCubes() + " (want " + cubes0 + ")";
            if (History.UndoCount != u0 || SM.Islands.Count != 4) return "undo " + History.UndoCount + " islands " + SM.Islands.Count;
            return null;
        });
        History.Redo();
        yield return Frames(3);
        Run("a. redo brings it back whole (span 2, ten notes on three grids)", () =>
        {
            var r = ById(id);
            if (r == null || r.span != 2 || r.nodes.Count != 10) return r == null ? "no cube" : "span " + r.span + " nodes " + r.nodes.Count;
            return Seq(r) == "0(0,0) 0(1,0) 0(2,0) 0(3,0) 1(3,1) 1(2,1) 1(1,1) 1(0,1) 2(0,2) 2(1,2)" ? null : Seq(r);
        });
        PathManager.SimPos = Sea;
    }

    // ================================================================== b. the mirrored click
    static IEnumerator Mirror()
    {
        var pm = PM;
        LoadState(Song("flow draw b", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "b. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        yield return Clicks(0, 0, 0, 1, 0, 2, 0, 3, 0);
        var d = pm.Draft;
        // the next start (beat 4) is on grid 1: hovering grid 0's far tile (0, 3) shows the ghost on grid 1's (0, 3) — one tile, no line across the seam
        yield return Hover(T(0, 0, 3));
        yield return Frames(2);
        var ghost = pm.HandGhostTile;
        var prev = new List<TileInteraction>(pm.PreviewTiles);
        var aud = pm.HoverAuditionTile;
        Run("b. with the next start on grid 1, hovering grid 0's tile (0, 3) shows the ghost — and auditions — on grid 1's (0, 3): one tile (the first note on a new grid is a leap)", () =>
        {
            if (pm.NextNoteGrid != Grid(1)) return "next grid " + IndexOf(pm.NextNoteGrid);
            if (ghost != T(1, 0, 3)) return "hologram on " + (ghost == null ? "nothing" : IndexOf(ghost.island) + "(" + ghost.gridX + "," + ghost.gridZ + ")");
            if (prev.Count != 1 || prev[0] != T(1, 0, 3)) return "preview " + prev.Count + " tiles";
            if (aud != T(1, 0, 3)) return "audition tile " + (aud == null ? "none" : IndexOf(aud.island) + "(" + aud.gridX + "," + aud.gridZ + ")");
            return null;
        });
        int mir0 = PathManager.FlowMirrors;
        // (for the capture the game's own camera frames the two grids first: the arc lives half a second)
        var cam = Camera.main; Vector3 cp0 = cam.transform.position; Quaternion cr0 = cam.transform.rotation;
        bool gameCam = false;
        if (shots && OrbitCamera.I != null)
        {
            OrbitCamera.I.Suspended = false;
            Bounds bb = Grid(0).VisualBounds; bb.Encapsulate(Grid(1).VisualBounds);
            OrbitCamera.I.FrameBounds(bb, 0.1f, true, 1f);
            yield return Wait(0.5f);
            OrbitCamera.I.Suspended = true;
            Physics.SyncTransforms();
            gameCam = pm.PickAt(Scr(T(0, 0, 3).Top)).tile == T(0, 0, 3);
            if (!gameCam) { cam.transform.position = cp0; cam.transform.rotation = cr0; }
        }
        Vector3 s = Scr(T(0, 0, 3).Top);
        PathManager.SimPos = s;
        pm.SimPointer(s, true, true, false);
        pm.SimPointer(s, false, false, true);
        bool arc = pm.MirrorArcShown;
        yield return Wait(shots ? 0.22f : 0.05f);   // (the arc is whole after its first third)
        if (shots)
        {
            string path = System.IO.Path.Combine(V2Checks.CapturePath, "flow_draw_mirror.png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path, 1);
            yield return null; yield return null;
            if (!gameCam) Info("b. the mirror capture was taken from the test camera (the game's framing did not pick the tile)");
            cam.transform.position = cp0; cam.transform.rotation = cr0;
        }
        Run("b. the click on grid 0's (0, 3) adds ONE node realised on grid 1's (0, 3); both tiles flash and a dashed arc runs between them", () =>
        {
            if (d == null || pm.Draft != d || d.nodes.Count != 5) return "nodes " + (d == null ? -1 : d.nodes.Count);
            if (d.GridOfNode(4) != Grid(1) || d.RealTile(4) != T(1, 0, 3)) return "node 4 on " + Seq(d);
            if (d.span != 1) return "span " + d.span;
            if (PathManager.FlowMirrors != mir0 + 1 || !arc) return "mirrors +" + (PathManager.FlowMirrors - mir0) + " arc " + arc;
            return null;
        });
        mir0 = PathManager.FlowMirrors;
        yield return Click(T(1, 1, 3));
        yield return Click(T(0, 1, 2));
        Run("b. a click straight on grid 1's tile adds it there with no arc; the next click on grid 0's (1, 2) is mirrored again (the next start is still on grid 1)", () =>
        {
            if (d.nodes.Count != 7) return "nodes " + d.nodes.Count + ": " + Seq(d);
            if (d.RealTile(5) != T(1, 1, 3) || d.RealTile(6) != T(1, 1, 2)) return Seq(d);
            if (PathManager.FlowMirrors != mir0 + 1) return "mirrors +" + (PathManager.FlowMirrors - mir0);
            return null;
        });
        // a far tile on the grid the last note stands on fills the line, as on one grid
        yield return Click(T(1, 4, 2));
        Run("b. on the grid the melody stands on a far click still fills the line (grid 1: (1, 2) → (4, 2) adds three notes there, the last one stepping onto grid 2 at beat 8)", () =>
        {
            string want = "0(0,0) 0(1,0) 0(2,0) 0(3,0) 1(0,3) 1(1,3) 1(1,2) 1(2,2) 2(3,2) 2(4,2)";
            return Seq(d) == want ? null : Seq(d) + ", want " + want;
        });
        Key(KeyCode.Return);
        yield return Frames(2);
        PathManager.SimPos = Sea;
        // ---- the game's own camera: framed on grid 0 alone, the note that steps onto grid 1 (off screen) eases the camera to frame the chain
        if (OrbitCamera.I != null)
        {
            LoadState(Song("flow draw b2", FourChords()));
            yield return Frames(3);
            OrbitCamera.I.Suspended = false;
            var b0 = Grid(0).VisualBounds;
            OrbitCamera.I.FrameBounds(b0, 0.1f, true, 0.45f);
            yield return Wait(0.6f);
            Physics.SyncTransforms();
            bool pickable = true;
            for (int x = 0; x < 4 && pickable; x++) if (pm.PickAt(Scr(T(0, x, 1).Top)).tile != T(0, x, 1)) pickable = false;
            Func<KeyBlock, bool> inView = kb =>
            {
                var bb = kb.VisualBounds;
                for (int i = 0; i < 4; i++) { var sp = Scr(new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, bb.center.y, i < 2 ? bb.min.z : bb.max.z)); if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) return false; }
                return true;
            };
            bool before = inView(Grid(1));
            if (!pickable || before) Info("b. camera check skipped: grid 0's tiles pickable " + pickable + ", grid 1 in view already " + before);
            else
            {
                pm.PickUpCube(Pluck); pm.SetBrush(24);
                int fr0 = PathManager.FlowFrames;
                yield return Clicks(0, 0, 1, 1, 1, 2, 1, 3, 1);
                int frFull = PathManager.FlowFrames;
                yield return Click(T(0, 3, 2));
                yield return Wait(1.3f);
                var dd = pm.Draft;
                Run("b. the camera keeps the melody in view: framed on grid 0 alone, the first four notes move nothing; the note that steps onto grid 1 (off screen) eases the camera to frame the chain", () =>
                {
                    if (dd == null || dd.nodes.Count != 5 || dd.GridOfNode(4) != Grid(1)) return "the fifth note: " + Seq(dd);
                    if (frFull != fr0 || PathManager.FlowFrames != fr0 + 1) return "framings +" + (frFull - fr0) + " then +" + (PathManager.FlowFrames - frFull);
                    return inView(Grid(1)) && inView(Grid(0)) ? null : "after the glide: grid 0 in view " + inView(Grid(0)) + ", grid 1 in view " + inView(Grid(1));
                });
                Key(KeyCode.Return);
                yield return Frames(2);
            }
            OrbitCamera.I.Suspended = true;
            PathManager.SimPos = Sea;
        }
    }

    // ================================================================== c. a lane with no grid in the next column
    static IEnumerator Lane()
    {
        var pm = PM;
        LoadState(Song("flow draw c", FourChords()));
        yield return Frames(3);
        int at = SM.AddIslandInColumn(0, false);
        yield return Wait(0.5f);
        History.Reset(); History.Push();
        var home = Grid(at);
        if (home == null || home.column != 0 || at == 0) { Line(false, "c. a second lane in column 0", "AddIslandInColumn " + at); yield break; }
        float pz = home.pz;
        Run("c. the test song: a second chord grid in column 0 (its own lane: no grid of that lane in column 1)", () =>
            SM.LaneNext(home) == null && SM.LaneGridIn(1, home.FrontEdge, home.Depth, false) == null && SM.Islands.Count == 5 ? null : "LaneNext " + IndexOf(SM.LaneNext(home)) + " islands " + SM.Islands.Count);
        if (!FrameGrids(at, at)) { Line(false, "c. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int u0 = History.UndoCount, g0 = PathManager.FlowGrids, gb0 = PathManager.FlowGridsBack, cubes0 = UserCubes();
        yield return Clicks(at, 0, 0, 1, 0, 2, 0, 3, 0);
        yield return Click(T(at, 3, 1));
        yield return Wait(0.4f);
        home = null;
        KeyBlock made = null, h2 = null;
        foreach (var kb in SM.Islands) { if (kb.column == 1 && Mathf.Abs(kb.pz - pz) < 0.05f) made = kb; if (kb.column == 0 && Mathf.Abs(kb.pz - pz) < 0.05f) h2 = kb; }
        var d = pm.Draft;
        Run("c. the fifth note: R1 makes an empty grid in the next column — the lane's z, that column's chord, one measure — and the SAME melody walks onto it (the draft survives the rebuild)", () =>
        {
            if (!pm.IsDrawing || d == null) return "the draft is gone";
            if (SM.Islands.Count != 6 || made == null) return "islands " + SM.Islands.Count + ", made " + (made != null);
            if (PathManager.FlowGrids != g0 + 1) return "grids made +" + (PathManager.FlowGrids - g0);
            var chord = SM.ChordOfColumn(1);
            if (made.IsKeyboard || made.bars != 1 || made.chordRootMIDI != ChordRoots[1] || made.assignedChord != chord.assignedChord) return "made: " + made.assignedChord + " " + made.bars + " bars root " + made.chordRootMIDI;
            if (SM.LaneNext(h2) != made) return "the made grid is not the lane's next";
            if (d.span != 1 || d.nodes.Count != 5 || d.Island != h2 || d.GridOfNode(4) != made || d.RealTile(4) != made.GetTile(3, 1)) return "span " + d.span + " " + Seq(d);
            int own = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == made) own++;
            return own == 0 ? null : own + " cubes live on the made grid";
        });
        if (shots) { FrameGrids(0, SM.Islands.Count - 1); yield return Capture("flow_draw_lane.png", 0, SM.Islands.Count - 1); if (h2 != null) FrameGrids(IndexOf(h2), IndexOf(h2)); }
        Key(KeyCode.Backspace);
        yield return Wait(0.4f);
        d = pm.Draft;
        Run("c. Backspace takes the note back: the span is 0 again and the grid the run made — still empty — is given back", () =>
        {
            if (!pm.IsDrawing || d == null) return "the draft is gone";
            if (d.span != 0 || d.nodes.Count != 4) return "span " + d.span + " nodes " + d.nodes.Count;
            if (SM.Islands.Count != 5 || PathManager.FlowGridsBack != gb0 + 1) return "islands " + SM.Islands.Count + " given back +" + (PathManager.FlowGridsBack - gb0);
            foreach (var kb in SM.Islands) if (kb.column == 1 && Mathf.Abs(kb.pz - pz) < 0.05f) return "the made grid is still there";
            return null;
        });
        int hi = -1; for (int i = 0; i < SM.Islands.Count; i++) if (SM.Islands[i].column == 0 && Mathf.Abs(SM.Islands[i].pz - pz) < 0.05f) hi = i;
        if (hi < 0 || !FrameGrids(hi, hi)) { Line(false, "c. the lane after the give-back", "home " + hi); yield break; }
        yield return Click(T(hi, 3, 1));
        yield return Wait(0.4f);
        Key(KeyCode.Return);
        yield return Frames(3);
        var fin = Newest();
        Run("c. the note again (the grid is made again), Enter: ONE History entry for the run — the cube and the grid", () =>
        {
            if (pm.IsDrawing) return "still drawing";
            if (fin == null || fin.span != 1 || fin.nodes.Count != 5 || SM.Islands.Count != 6) return "cube " + (fin == null ? "none" : "span " + fin.span + " nodes " + fin.nodes.Count) + " islands " + SM.Islands.Count;
            return History.UndoCount == u0 + 1 ? null : "undo " + u0 + " -> " + History.UndoCount;
        });
        History.Undo();
        yield return Wait(0.3f);
        Run("c. one undo removes the run and the grid it made", () =>
            SM.Islands.Count == 5 && UserCubes() == cubes0 && History.UndoCount == u0 ? null : "islands " + SM.Islands.Count + " cubes " + UserCubes() + " undo " + History.UndoCount);
        PathManager.SimPos = Sea;
        // ---- a keyboard lane: the melody walks on onto a keyboard made beside the next chord, with the same keys (the same key = the same pitch)
        LoadState(Song("flow draw c2", FourChords()));
        yield return Frames(3);
        int ki = SM.AddKeyboardIsland(0, false);
        yield return Wait(0.5f);
        History.Reset(); History.Push();
        var keys = Grid(ki);
        if (keys == null || !keys.IsKeyboard || keys.column != 0) { Line(false, "c. a keyboard beside the first chord", "AddKeyboardIsland " + ki); yield break; }
        float kz = keys.pz; int keyCount = keys.KeyCount, lowest = keys.chordRootMIDI;
        var k5 = keys.GetTile(5, 0); var k7 = keys.GetTile(7, 0); var k12 = keys.GetTile(12, 0);
        bool pick = false;
        foreach (float dist in new[] { 12f, 15f, 19f, 24f }) { Frame((k5.Top + k12.Top) * 0.5f, dist, 0f, 64f); Physics.SyncTransforms(); if (pm.PickAt(Scr(k5.Top)).tile == k5 && pm.PickAt(Scr(k7.Top)).tile == k7 && pm.PickAt(Scr(k12.Top)).tile == k12) { pick = true; break; } }
        if (!pick) { Line(false, "c. the keys are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        u0 = History.UndoCount; g0 = PathManager.FlowGrids;
        int midi12 = k12.midi, n0 = SM.Islands.Count;
        yield return Click(k5); yield return Click(k7); yield return Click(k7); yield return Click(k5);
        yield return Click(k12);
        yield return Wait(0.5f);
        d = pm.Draft;
        KeyBlock madeKeys = null;
        foreach (var kb in SM.Islands) if (kb.column == 1 && kb.IsKeyboard) madeKeys = kb;
        Run("c. a keyboard lane: the fifth key makes a KEYBOARD beside the next chord — the same keys — and the note lands on the same key there (its pitch kept)", () =>
        {
            if (!pm.IsDrawing || d == null) return "the draft is gone";
            if (SM.Islands.Count != n0 + 1 || madeKeys == null || PathManager.FlowGrids != g0 + 1) return "islands " + n0 + " -> " + SM.Islands.Count + " keyboard made " + (madeKeys != null);
            if (madeKeys.KeyCount != keyCount || madeKeys.chordRootMIDI != lowest || Mathf.Abs(madeKeys.pz - kz) > 0.05f) return "made keyboard: " + madeKeys.KeyCount + " keys from " + madeKeys.chordRootMIDI + " (want " + keyCount + " from " + lowest + ")";
            if (d.span != 1 || d.nodes.Count != 5 || d.GridOfNode(4) != madeKeys) return "span " + d.span + " " + Seq(d);
            var rt = d.RealTile(4);
            return rt != null && rt.island == madeKeys && rt.midi == midi12 && rt.gridX == 12 ? null : "node 4 on key " + (rt != null ? rt.gridX + " (midi " + rt.midi + ")" : "none") + ", want key 12 (midi " + midi12 + ")";
        });
        Key(KeyCode.Return);
        yield return Frames(3);
        History.Undo();
        yield return Wait(0.3f);
        Run("c. Enter, one undo: the keyboard melody and the keyboard it made are gone together", () => SM.Islands.Count == n0 && History.UndoCount == u0 && UserCubes() == 0 ? null : "islands " + SM.Islands.Count + " undo " + History.UndoCount + " cubes " + UserCubes());
        PathManager.SimPos = Sea;
    }

    // ================================================================== d. R2 and R3
    static IEnumerator Overflow()
    {
        var pm = PM;
        // ---- R2: a one-chord song grows a measure
        LoadState(Song("flow draw d1", new[] { Chord(0) }));
        yield return Frames(4);
        if (!FrameGrids(0, 0)) { Line(false, "d. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int u0 = History.UndoCount, ex0 = PathManager.AutoExpands, un0 = PathManager.AutoUnexpands, sp0 = PathManager.FlowSpans, ho0 = PathManager.FlowHandovers, rf0 = PathManager.CapacityRefusals;
        yield return Clicks(0, 0, 0, 1, 0, 2, 0, 3, 0);
        var d = pm.Draft;
        yield return Click(T(0, 3, 1));
        yield return Wait(0.5f);
        string words = DraftWords();
        Run("d. R2: in a one-chord song the fifth beat GROWS the grid a measure (live: the same draft, no History), no span, no refusal — and no measures tag", () =>
        {
            if (pm.Draft != d || d == null || d.nodes.Count != 5 || d.span != 0) return "draft " + (d == null ? "none" : "nodes " + d.nodes.Count + " span " + d.span);
            if (SM.ColumnBars(0) != 2 || Grid(0).bars != 2) return "bars " + SM.ColumnBars(0);
            if (PathManager.AutoExpands != ex0 + 1 || PathManager.FlowSpans != sp0 || PathManager.FlowHandovers != ho0 || PathManager.CapacityRefusals != rf0) return "grows +" + (PathManager.AutoExpands - ex0) + " spans +" + (PathManager.FlowSpans - sp0);
            if (History.UndoCount != u0) return "undo " + u0 + " -> " + History.UndoCount;
            if (MeasureTag.Shown || words.Length > 0) return "words on screen: " + (MeasureTag.Shown ? MeasureTag.Words + " " : "") + words;
            return null;
        });
        Key(KeyCode.Backspace);
        yield return Frames(3);
        Run("d. Backspace gives the measure back", () => SM.ColumnBars(0) == 1 && d.nodes.Count == 4 && PathManager.AutoUnexpands == un0 + 1 ? null : "bars " + SM.ColumnBars(0) + " nodes " + d.nodes.Count + " unexpands +" + (PathManager.AutoUnexpands - un0));
        // sixteen quarters fill the section's four measures; the seventeenth is R3 (the song's end: a column is appended)
        if (!FrameGrids(0, 0)) { Line(false, "d. the tiles are pickable", "no camera pose"); yield break; }
        for (int i = 4; i < 16; i++) { yield return Click(T(0, i % 2 == 0 ? 2 : 3, 1)); if (i % 4 == 3) FrameGrids(0, 0); }
        yield return Frames(2);
        Run("d. R2 goes on to the section's limit: sixteen beats on one grid of four measures (one cube, span 0)", () =>
            pm.Draft == d && d.nodes.Count == 16 && SM.ColumnBars(0) == 4 && SM.ColumnCount == 1 && d.span == 0 ? null : "nodes " + (d != null ? d.nodes.Count : -1) + " bars " + SM.ColumnBars(0) + " columns " + SM.ColumnCount);
        FrameGrids(0, 0);
        yield return Click(T(0, 4, 2));
        yield return Wait(0.6f);
        var d2 = pm.Draft;
        Run("d. R3 at the song's end: the seventeenth beat finishes the cube and a NEW draft starts by itself on a column appended with the last column's chord — the note at the same (x, z)", () =>
        {
            if (!pm.IsDrawing || d2 == null || d2 == d) return "no new draft";
            if (SM.ColumnCount != 2 || SM.Islands.Count != 2) return "columns " + SM.ColumnCount + " islands " + SM.Islands.Count;
            var nk = Grid(1);
            if (nk.column != 1 || nk.chordRootMIDI != ChordRoots[0] || nk.bars != 1 || nk.IsKeyboard) return "new column: " + nk.assignedChord + " " + nk.bars + " bars";
            if (d2.Island != nk || d2.nodes.Count != 1 || d2.RealTile(0) != nk.GetTile(4, 2) || d2.span != 0) return "new draft " + Seq(d2);
            var first = Newest();
            if (first == null || first.nodes.Count != 16 || !first.isFinalized) return "the first cube: " + (first == null ? "none" : first.nodes.Count + " nodes");
            if (PathManager.FlowHandovers != ho0 + 1 || !pm.InRun) return "hand-overs +" + (PathManager.FlowHandovers - ho0);
            return null;
        });
        int firstId = Newest() != null ? Newest().id : 0;
        Key(KeyCode.Backspace);
        yield return Wait(0.5f);
        Run("d. Backspace on the new draft's only note: the pen steps back onto the first cube (the same cube, sixteen notes, drawing) and the appended column is gone", () =>
        {
            var b = pm.Draft;
            if (!pm.IsDrawing || b == null || b.id != firstId || b.nodes.Count != 16) return "draft " + (b == null ? "none" : "id " + b.id + " nodes " + b.nodes.Count);
            return SM.ColumnCount == 1 && SM.Islands.Count == 1 && pm.InRun ? null : "columns " + SM.ColumnCount;
        });
        FrameGrids(0, 0);
        yield return Click(T(0, 4, 2));
        yield return Wait(0.6f);
        if (SM.Islands.Count == 2 && FrameGrids(1, 1)) yield return Click(T(1, 5, 2));
        Key(KeyCode.Return);
        yield return Frames(3);
        Run("d. again, one more note on the new column, Enter: both cubes and the column are ONE History entry; one undo takes them all back", () =>
        {
            if (pm.IsDrawing || pm.InRun) return "still drawing";
            if (SM.ColumnCount != 2 || UserCubes() != 2) return "columns " + SM.ColumnCount + " cubes " + UserCubes();
            var b = Newest();
            if (b == null || b.nodes.Count != 2 || b.Island != Grid(1)) return "second cube " + Seq(b);
            if (History.UndoCount != u0 + 1) return "undo " + u0 + " -> " + History.UndoCount;
            History.Undo();
            return SM.ColumnCount == 1 && UserCubes() == 0 && SM.ColumnBars(0) == 1 ? null : "after undo: columns " + SM.ColumnCount + " cubes " + UserCubes() + " bars " + SM.ColumnBars(0);
        });
        yield return Frames(3);
        // ---- R3 at a section's end with a next section: the new draft starts on its first column (nothing is made)
        LoadState(Song("flow draw d2", new[] { Chord(0), Chord(1), Chord(2), Chord(3), Chord(4) }, new[] { 0, 4 }));
        yield return Frames(4);
        Run("d. the test song: five columns, two sections (4 + 1)", () => SM.SectionCount == 2 && SM.SectionOf(3) == 0 && SM.SectionOf(4) == 1 && SM.LaneNext(Grid(3)) == null ? null : "sections " + SM.SectionCount + " LaneNext(3) " + IndexOf(SM.LaneNext(Grid(3))));
        if (!FrameGrids(3, 4)) { Line(false, "d. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        u0 = History.UndoCount; ho0 = PathManager.FlowHandovers; ex0 = PathManager.AutoExpands;
        yield return Clicks(3, 0, 0, 1, 0, 2, 0, 3, 0);
        d = pm.Draft;
        yield return Click(T(3, 2, 2));
        yield return Wait(0.4f);
        d2 = pm.Draft;
        Run("d. R3 at a section's end (the section is full): the fifth beat finishes the cube and a new draft starts on the NEXT SECTION's first column, the note mirrored to (2, 2) there — no grid made, no measure added", () =>
        {
            if (!pm.IsDrawing || d2 == null || d2 == d) return "no new draft";
            if (d2.Island != Grid(4) || d2.nodes.Count != 1 || d2.RealTile(0) != T(4, 2, 2)) return "new draft " + Seq(d2);
            if (SM.Islands.Count != 5 || SM.ColumnCount != 5 || PathManager.AutoExpands != ex0) return "islands " + SM.Islands.Count + " grows +" + (PathManager.AutoExpands - ex0);
            if (d == null || !d.isFinalized || d.nodes.Count != 4 || d.span != 0) return "first cube " + Seq(d);
            return PathManager.FlowHandovers == ho0 + 1 ? null : "hand-overs +" + (PathManager.FlowHandovers - ho0);
        });
        Key(KeyCode.Return);
        yield return Frames(3);
        Run("d. Enter: the two cubes are one undo entry", () => History.UndoCount == u0 + 1 && UserCubes() == 2 ? null : "undo " + u0 + " -> " + History.UndoCount + " cubes " + UserCubes());
        PathManager.SimPos = Sea;
    }

    // ================================================================== e. rests
    static IEnumerator Rests()
    {
        var pm = PM;
        LoadState(Song("flow draw e", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "e. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int u0 = History.UndoCount, r0 = PathManager.FlowRests, cubes0 = UserCubes();
        yield return Click(T(0, 1, 1));
        pm.SetBrush(12);
        Key(KeyCode.R);
        yield return null;
        pm.SetBrush(48);
        yield return Click(T(0, 2, 2));
        var d = pm.Draft;
        Run("e. note, R, note: three events with mods 0 1 0 and the brush lengths (a beat, an eighth, a half); the rest stands on the tile of the note before it", () =>
        {
            if (d == null || d.nodes.Count != 3) return "nodes " + (d == null ? -1 : d.nodes.Count);
            if (d.ModOf(0) != 0 || d.ModOf(1) != 1 || d.ModOf(2) != 0) return "mods " + d.ModOf(0) + d.ModOf(1) + d.ModOf(2);
            if (d.durs.Count != 3 || d.durs[0] != 24 || d.durs[1] != 12 || d.durs[2] != 48) return "durs " + string.Join(",", d.durs.Select(x => x.ToString()).ToArray());
            if (d.RealTile(1) != T(0, 1, 1) || d.RealTile(2) != T(0, 2, 2)) return Seq(d);
            return PathManager.FlowRests == r0 + 1 ? null : "rests +" + (PathManager.FlowRests - r0);
        });
        yield return Frames(3);
        int rc0 = DurationPicker.RestCount;
        bool shown = DurationPicker.RestShown && DurationPicker.DoneShown && DurationPicker.LoopShown;
        pm.SetBrush(24);
        DurationPicker.SimClickRest();
        yield return Frames(2);
        Run("e. the draft row carries the rest cube beside the done tick; a click on it adds a rest of the brush size", () =>
        {
            if (!shown) return "rest " + DurationPicker.RestShown + " done " + DurationPicker.DoneShown + " hear-it " + DurationPicker.LoopShown;
            if (DurationPicker.RestCount != rc0 + 1 || d.nodes.Count != 4 || d.ModOf(3) != 1 || d.durs[3] != 24) return "rest clicks +" + (DurationPicker.RestCount - rc0) + " nodes " + d.nodes.Count;
            return d.RealTile(3) == T(0, 2, 2) ? null : Seq(d);
        });
        yield return Click(T(0, 3, 2));
        yield return Hover(T(0, 4, 2));
        yield return Wait(0.2f);
        yield return Capture("flow_draw_rest.png", 0, 1, 0.8f);
        Key(KeyCode.Backspace); Key(KeyCode.Backspace);
        yield return null;
        Run("e. Backspace takes a rest back like any note", () => d.nodes.Count == 3 && d.ModOf(2) == 0 && d.ModOf(1) == 1 ? null : "nodes " + d.nodes.Count + " " + Seq(d));
        // a rest that starts on the next grid stands on the same (x, z) there
        pm.SetBrush(24);
        Key(KeyCode.R); Key(KeyCode.R);
        yield return null;
        Run("e. rests fill the measure and walk on with the melody: the rest that starts at beat 4 stands on grid 1 (the span grows for it)", () =>
        {
            if (d.nodes.Count != 5 || d.span != 1) return "nodes " + d.nodes.Count + " span " + d.span + " " + Seq(d);
            return d.GridOfNode(3) == Grid(0) && d.GridOfNode(4) == Grid(1) && d.RealTile(4) == T(1, 2, 2) && d.ModOf(4) == 1 ? null : Seq(d);
        });
        Key(KeyCode.Return);
        yield return Frames(3);
        var fin = Newest();
        Run("e. Enter keeps the rests in the finished cube (one History entry)", () =>
            fin != null && fin == d && fin.isFinalized && fin.ModOf(1) == 1 && fin.rests.Count > 1 && fin.rests[1] && History.UndoCount == u0 + 1 ? null : "cube " + Seq(fin) + " undo " + History.UndoCount);
        // ---- a pickup rest: R before the first tile is remembered and opens the path
        LoadState(Song("flow draw e2", FourChords()));
        yield return Frames(4);
        FrameGrids(0, 1);
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        PathManager.SimPos = Sea;
        yield return Frames(2);
        u0 = History.UndoCount; cubes0 = UserCubes();
        Key(KeyCode.R);
        int pend = pm.PendingRests;
        yield return Click(T(0, 2, 1));
        d = pm.Draft;
        Run("e. a pickup rest: R with the cube in the hand and no tile under the pointer is remembered; the first click places the rest, then the note, on that tile", () =>
        {
            if (pend != 1 || pm.PendingRests != 0) return "remembered " + pend + " -> " + pm.PendingRests;
            if (d == null || d.nodes.Count != 2 || d.ModOf(0) != 1 || d.ModOf(1) != 0) return "draft " + Seq(d);
            return d.RealTile(0) == T(0, 2, 1) && d.RealTile(1) == T(0, 2, 1) && d.durs[0] == 24 && d.durs[1] == 24 ? null : Seq(d);
        });
        Key(KeyCode.Return);
        yield return Frames(3);
        Run("e. it finishes as a cube that starts with the rest", () => { var c = Newest(); return c != null && c.nodes.Count == 2 && c.ModOf(0) == 1 && History.UndoCount == u0 + 1 ? null : Seq(c); });
        // ---- R over a free tile starts the path with a rest standing there; a draft that never gets a note leaves nothing
        u0 = History.UndoCount; cubes0 = UserCubes();
        int p0 = pushes;
        yield return Hover(T(0, 4, 3));
        Key(KeyCode.R);
        yield return Frames(2);
        d = pm.Draft;
        bool started = pm.IsDrawing && d != null && d.nodes.Count == 1 && d.ModOf(0) == 1 && d.RealTile(0) == T(0, 4, 3);
        Key(KeyCode.R);
        Key(KeyCode.Escape);
        yield return Wait(0.5f);
        Run("e. R over a free tile opens a path with a rest there; ended without a note (Esc) it leaves NO cube and NO History entry", () =>
        {
            if (!started) return "the rest did not start a draft";
            if (pm.IsDrawing || pm.InRun) return "still drawing";
            if (UserCubes() != cubes0) return "cubes " + cubes0 + " -> " + UserCubes();
            int alive = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && !c.isFinalized) alive++;
            if (alive != 0) return alive + " unfinished cubes registered";
            return History.UndoCount == u0 && pushes == p0 ? null : "undo " + u0 + " -> " + History.UndoCount + " pushes +" + (pushes - p0);
        });
        PathManager.SimPos = Sea;
    }

    // ================================================================== f. every exit keeps the notes
    static IEnumerator Finishes()
    {
        var pm = PM;
        LoadState(Song("flow draw f", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 2)) { Line(false, "f. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int cubes0 = UserCubes(), u0 = History.UndoCount;
        yield return Click(T(0, 1, 0));
        yield return Click(T(0, 1, 0));
        var d = pm.Draft;
        Run("f. the same tile again = the note again on a grid (two nodes on one tile, still drawing)", () =>
            pm.IsDrawing && d != null && d.nodes.Count == 2 && d.RealTile(0) == T(0, 1, 0) && d.RealTile(1) == T(0, 1, 0) ? null : "drawing " + pm.IsDrawing + " " + Seq(d));
        // right-click
        Vector3 s = Scr(T(0, 3, 3).Top);
        RightClick(s);
        yield return Frames(2);
        Run("f. a right-click finishes and keeps the notes (a finished cube of two, one History entry, the cube stays in the hand)", () =>
            !pm.IsDrawing && d.isFinalized && d.nodes.Count == 2 && UserCubes() == cubes0 + 1 && History.UndoCount == u0 + 1 && pm.Hand == PathManager.HandKind.Cube ? null : "drawing " + pm.IsDrawing + " cubes " + UserCubes() + " undo " + History.UndoCount);
        // Enter
        yield return Clicks(0, 0, 1, 1, 1);
        d = pm.Draft;
        Key(KeyCode.Return);
        yield return Frames(2);
        Run("f. Enter finishes and keeps the notes", () => !pm.IsDrawing && d != null && d.isFinalized && d.nodes.Count == 2 && UserCubes() == cubes0 + 2 ? null : "cubes " + UserCubes());
        // the done tick
        yield return Clicks(0, 0, 2, 1, 2);
        d = pm.Draft;
        yield return Frames(3);
        int dc = DurationPicker.DoneCount;
        DurationPicker.SimClickDone();
        yield return Frames(2);
        Run("f. the ✓ on the draft row finishes and keeps the notes", () => !pm.IsDrawing && DurationPicker.DoneCount == dc + 1 && d != null && d.isFinalized && d.nodes.Count == 2 && UserCubes() == cubes0 + 3 ? null : "done clicks +" + (DurationPicker.DoneCount - dc) + " cubes " + UserCubes());
        // Esc
        yield return Clicks(0, 0, 3, 1, 3, 2, 3);
        d = pm.Draft;
        u0 = History.UndoCount;
        Key(KeyCode.Escape);
        yield return Frames(2);
        bool kept = !pm.IsDrawing && d != null && d.isFinalized && d.nodes.Count == 3 && UserCubes() == cubes0 + 4 && History.UndoCount == u0 + 1;
        bool hand1 = pm.Hand == PathManager.HandKind.Cube;
        Key(KeyCode.Escape);
        yield return Frames(2);
        Run("f. Esc no longer destroys: the first Esc finishes and KEEPS the three notes (the cube still in the hand), a second Esc puts the hand down", () =>
            kept && hand1 && pm.Hand == PathManager.HandKind.Empty && UserCubes() == cubes0 + 4 ? null : "kept " + kept + " hand after one " + hand1 + ", after two " + pm.Hand + ", cubes " + UserCubes());
        // a click on the sea
        pm.PickUpCube(Pluck);
        yield return Clicks(0, 4, 0, 5, 0);
        d = pm.Draft;
        yield return ClickAt(Sea);
        yield return Frames(2);
        Run("f. a click on the sea finishes and keeps the notes", () => !pm.IsDrawing && d != null && d.isFinalized && d.nodes.Count == 2 && UserCubes() == cubes0 + 5 ? null : "drawing " + pm.IsDrawing + " cubes " + UserCubes());
        // a click on a grid outside the chain: pen up, and the press starts the next path there
        yield return Clicks(0, 4, 1, 5, 1);
        d = pm.Draft;
        yield return Click(T(2, 1, 1));
        yield return Frames(2);
        var d2 = pm.Draft;
        Run("f. a click on a grid outside the chain finishes (the notes stay) and is NOT eaten: a new path starts on that grid", () =>
        {
            if (d == null || !d.isFinalized || d.nodes.Count != 2 || d.span != 0) return "the first cube: " + Seq(d);
            if (!pm.IsDrawing || d2 == null || d2 == d || d2.Island != Grid(2) || d2.nodes.Count != 1 || d2.RealTile(0) != T(2, 1, 1)) return "the new draft: " + Seq(d2);
            return UserCubes() == cubes0 + 6 ? null : "cubes " + UserCubes();
        });
        Key(KeyCode.Return);
        yield return Frames(2);
        // a HUD press and the menu
        yield return Clicks(1, 0, 0, 1, 0);
        d = pm.Draft;
        pm.SimPointer(new Vector3(40f, 40f, 0f), true, true, false, false, false, true);
        pm.SimPointer(new Vector3(40f, 40f, 0f), false, false, true, false, false, true);
        yield return Frames(2);
        Run("f. a press on the HUD finishes and keeps the notes", () => !pm.IsDrawing && d != null && d.isFinalized && d.nodes.Count == 2 ? null : "drawing " + pm.IsDrawing);
        // ---- keyboards: the same key again = the note again; right-click finishes
        LoadState(Song("flow draw f2", new[] { Chord(0), Piano(1), Chord(2, 1) }));
        yield return Frames(4);
        var keys = Grid(1);
        if (keys == null || !keys.IsKeyboard) { Line(false, "f. the keyboard song", "island 1 is not a keyboard"); yield break; }
        var k7 = keys.GetTile(7, 0); var k9 = keys.GetTile(9, 0);
        Vector3 kc = (k7.Top + k9.Top) * 0.5f;
        bool pick = false;
        foreach (float dist in new[] { 10f, 13f, 17f, 22f }) { Frame(kc, dist, 0f, 62f); Physics.SyncTransforms(); if (pm.PickAt(Scr(k7.Top)).tile == k7 && pm.PickAt(Scr(k9.Top)).tile == k9) { pick = true; break; } }
        if (!pick) { Line(false, "f. the keys are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        cubes0 = UserCubes();
        yield return Click(k7); yield return Click(k7); yield return Click(k9);
        d = pm.Draft;
        bool three = pm.IsDrawing && d != null && d.nodes.Count == 3 && d.nodes[0] == k7 && d.nodes[1] == k7 && d.nodes[2] == k9;
        RightClick(Scr(k9.Top));
        yield return Frames(2);
        Run("f. on a keyboard too: the same key again = the note again; a right-click finishes and keeps them", () =>
            three && !pm.IsDrawing && d.isFinalized && d.nodes.Count == 3 && UserCubes() == cubes0 + 1 ? null : "three " + three + " drawing " + pm.IsDrawing + " cubes " + UserCubes());
        PathManager.SimPos = Sea;
    }

    // ================================================================== g. slips and drags
    static IEnumerator Drags()
    {
        var pm = PM;
        LoadState(Song("flow draw g", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 0)) { Line(false, "g. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(12);
        var a = T(0, 0, 1); var b = T(0, 1, 1); var c = T(0, 2, 1);
        Vector3 sa = Scr(a.Top), sbb = Scr(b.Top), sc = Scr(c.Top);
        float tilePx = (sbb - sa).magnitude;
        // a click whose pointer slips 3 px before the release
        pm.SimPointer(sa, true, true, false);
        pm.SimPointer(sa + new Vector3(3f, 1f, 0f), false, true, false);
        pm.SimPointer(sa + new Vector3(3f, 1f, 0f), false, false, true);
        yield return null;
        var d = pm.Draft;
        int n1 = d != null ? d.nodes.Count : -1;
        // a second click that slips 3 px onto nothing new, then a long slide INSIDE the same tile
        pm.SimPointer(sbb, true, true, false);
        pm.SimPointer(sbb + new Vector3(-3f, 2f, 0f), false, true, false);
        pm.SimPointer(sbb + new Vector3(Mathf.Min(12f, tilePx * 0.3f), 0f, 0f), false, true, false);
        pm.SimPointer(sbb, false, false, true);
        yield return null;
        int n2 = d != null ? d.nodes.Count : -1;
        Run("g. a 3 px slip during a click adds ONE note; sliding inside the same tile adds nothing (tiles are " + F(tilePx) + " px apart)", () =>
            n1 == 1 && n2 == 2 && pm.IsDrawing ? null : "nodes " + n1 + " then " + n2 + " drawing " + pm.IsDrawing);
        // a slip of a few px that crosses a tile's edge: not 8 px of travel, nothing painted
        Vector3 edge = (sbb + sc) * 0.5f;
        pm.SimPointer(edge - new Vector3(3f, 0f, 0f), true, true, false);
        var under0 = pm.PickAt(edge - new Vector3(3f, 0f, 0f)).tile; var under1 = pm.PickAt(edge + new Vector3(3f, 0f, 0f)).tile;
        pm.SimPointer(edge + new Vector3(3f, 0f, 0f), false, true, false);
        pm.SimPointer(edge + new Vector3(3f, 0f, 0f), false, false, true);
        yield return null;
        int n3 = d != null ? d.nodes.Count : -1;
        Run("g. a 6 px slip across a tile's edge is still one click (the drag threshold is 8 px AND another tile)", () =>
            n3 == 3 && under0 != under1 ? null : "nodes " + n3 + (under0 == under1 ? " (the slip did not cross an edge)" : ""));
        // a real drag paints: press on (0, 2), drag over (1, 2), (2, 2), (3, 2)
        Vector3 p0 = Scr(T(0, 0, 2).Top), p1 = Scr(T(0, 1, 2).Top), p2 = Scr(T(0, 2, 2).Top), p3 = Scr(T(0, 3, 2).Top);
        pm.SimPointer(p0, true, true, false);
        pm.SimPointer(Vector3.Lerp(p0, p1, 0.5f), false, true, false);
        pm.SimPointer(p1, false, true, false);
        pm.SimPointer(p2, false, true, false);
        pm.SimPointer(p3, false, true, false);
        int n4 = d != null ? d.nodes.Count : -1;
        pm.SimPointer(p3, false, false, true);
        yield return Frames(2);
        Run("g. a real drag PAINTS notes (press + three tiles entered = four more) and the release does not finish", () =>
        {
            if (d == null || pm.Draft != d || !pm.IsDrawing) return "the draft ended (drawing " + pm.IsDrawing + ")";
            if (n4 != n3 + 4 || d.nodes.Count != n4) return "nodes " + n3 + " -> " + n4 + " -> " + d.nodes.Count;
            return d.RealTile(n4 - 1) == T(0, 3, 2) && d.RealTile(n4 - 4) == T(0, 0, 2) ? null : Seq(d);
        });
        Key(KeyCode.Return);
        yield return Frames(2);
        // keyboards: the drag is a glissando and stays open
        PathManager.SimPos = Sea;
    }

    // ================================================================== h. transport
    static IEnumerator Transport()
    {
        var pm = PM;
        LoadState(Song("flow draw h", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "h. the tiles are pickable", "no camera pose"); yield break; }
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        // ---- from a stopped transport (parked at beat 6)
        GlobalClock.Seek(6.0);
        yield return Frames(2);
        yield return Clicks(0, 0, 0, 1, 0);
        bool quiet = !GlobalClock.IsPlaying && !FocusLoop.Active && Math.Abs(GlobalClock.SongBeatD - 6.0) < 1e-6;
        Key(KeyCode.Space);
        yield return Wait(0.4f);
        bool on = GlobalClock.IsPlaying && FocusLoop.Active && FocusLoop.Column == 0 && FocusLoop.LastColumn == 0 && pm.DrawLoopOn && GlobalClock.SongBeat < 4f;
        string onD = "playing " + GlobalClock.IsPlaying + " loop " + FocusLoop.Active + " [" + FocusLoop.Column + ".." + FocusLoop.LastColumn + "] beat " + F(GlobalClock.SongBeat);
        yield return Clicks(0, 2, 0, 3, 0, 3, 1);
        yield return Wait(0.2f);
        bool grew = FocusLoop.Active && FocusLoop.Column == 0 && FocusLoop.LastColumn == 1 && GlobalClock.HasRegion && Math.Abs(GlobalClock.RegionEnd - 8.0) < 1e-6 && GlobalClock.IsPlaying;
        string grewD = "[" + FocusLoop.Column + ".." + FocusLoop.LastColumn + "] region end " + F(GlobalClock.RegionEnd);
        Key(KeyCode.Space);
        yield return Frames(3);
        bool off = !GlobalClock.IsPlaying && !FocusLoop.Active && !pm.DrawLoopOn && pm.IsDrawing;
        Key(KeyCode.Space);
        yield return Wait(0.3f);
        bool again = GlobalClock.IsPlaying && FocusLoop.Active && FocusLoop.LastColumn == 1;
        Key(KeyCode.Return);
        yield return Frames(3);
        Run("h. from a stopped transport drawing stays silent (nothing starts, the playhead stays at beat 6); Space starts the loop over the draft's column, the range grows with the span (columns 0..1), Space again pauses it", () =>
        {
            if (!quiet) return "drawing from stopped moved the transport";
            if (!on) return "Space on: " + onD;
            if (!grew) return "the range did not grow: " + grewD;
            if (!off) return "Space off did not pause";
            return again ? null : "Space on again";
        });
        Run("h. the pen up puts a stopped transport back: not playing, no loop, the playhead at its place (beat 6) — never a rewind to the start", () =>
            !GlobalClock.IsPlaying && !FocusLoop.Active && !GlobalClock.HasRegion && Math.Abs(GlobalClock.SongBeatD - 6.0) < 1e-3 ? null : "playing " + GlobalClock.IsPlaying + " loop " + FocusLoop.Active + " beat " + F(GlobalClock.SongBeatD));
        // ---- while the song plays
        LoadState(Song("flow draw h2", FourChords()));
        yield return Frames(4);
        FrameGrids(0, 1);
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int late = Synth.LateEvents;
        GlobalClock.Seek(9.0); GlobalClock.Play();
        yield return Wait(0.5f);
        int pauses = 0; Action onPause = () => pauses++;
        GlobalClock.OnPause += onPause;
        yield return Click(T(0, 0, 0));
        yield return Wait(0.3f);
        bool kept = GlobalClock.IsPlaying && FocusLoop.Active && FocusLoop.Column == 0 && FocusLoop.LastColumn == 0 && GlobalClock.SongBeat < 4f;
        string keptD = "playing " + GlobalClock.IsPlaying + " loop " + FocusLoop.Active + " [" + FocusLoop.Column + ".." + FocusLoop.LastColumn + "] beat " + F(GlobalClock.SongBeat);
        yield return Clicks(0, 1, 0, 2, 0, 3, 0, 3, 1);
        float tEnd = Time.realtimeSinceStartup + 5.2f, maxBeat = 0f, prevBeat = GlobalClock.SongBeat; int wraps = 0; bool stopped = false;
        while (Time.realtimeSinceStartup < tEnd)
        {
            float b = GlobalClock.SongBeat;
            if (!GlobalClock.IsPlaying) stopped = true;
            if (b < prevBeat - 1f) wraps++;
            maxBeat = Mathf.Max(maxBeat, b); prevBeat = b;
            yield return null;
        }
        bool looped = !stopped && wraps >= 1 && maxBeat < 8.05f && FocusLoop.LastColumn == 1;
        string loopD = "wraps " + wraps + " max beat " + F(maxBeat) + " stopped " + stopped + " [" + FocusLoop.Column + ".." + FocusLoop.LastColumn + "]";
        Key(KeyCode.Return);
        yield return Frames(2);
        float b0 = GlobalClock.SongBeat;
        bool freed = GlobalClock.IsPlaying && !FocusLoop.Active && !GlobalClock.HasRegion;
        // it plays on out of the old region (8 beats at 120 bpm: at most 4 s)
        float tOut = Time.realtimeSinceStartup + 4.6f; bool passed = false;
        while (Time.realtimeSinceStartup < tOut && !passed) { if (GlobalClock.SongBeat >= 8.2f) passed = true; if (!GlobalClock.IsPlaying) break; yield return null; }
        GlobalClock.OnPause -= onPause;
        lateExcused += Synth.LateEvents - late;   // (the loop's jump into the draft's columns re-starts the clock: counted apart)
        Run("h. drawing while the song plays does NOT pause it: the clock keeps running and loops the draft's column (it jumps in from beat 9)", () => kept ? null : keptD);
        Run("h. the loop grows with the span and keeps looping the draft's columns (five beats on grids 0..1: beats 0–8, at least one wrap in 5 s)", () => looped ? null : loopD);
        Run("h. after the pen up the song plays on from where the loop is (no region, still playing, past beat 8 within the next measures)", () =>
            freed && passed && GlobalClock.IsPlaying ? null : "freed " + freed + " (beat " + F(b0) + ") passed beat 8: " + passed + " playing " + GlobalClock.IsPlaying);
        GlobalClock.Stop();
        PathManager.SimPos = Sea;
    }

    // ================================================================== i. resume
    static IEnumerator ResumeChecks()
    {
        var pm = PM;
        LoadState(Song("flow draw i", FourChords(), null, OldCube()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "i. the tiles are pickable", "no camera pose"); yield break; }
        var old = ById(IdOld);
        if (old == null) { Line(false, "i. the test song's finished path", "missing"); yield break; }
        pm.PickUpCube(Keys);
        yield return Frames(16);
        int marksOther = pm.ResumeMarkCount;
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        yield return Frames(16);
        int marks = pm.ResumeMarkCount; var mt = pm.ResumeMarkTile(0);
        Run("i. with a cube of its group in the hand a \"+\" badge lies by the LAST bead of the finished path (none for another group)", () =>
            marks == 1 && mt == T(0, 3, 3) && marksOther == 0 ? null : "badges " + marks + " on " + (mt == null ? "nothing" : "(" + mt.gridX + "," + mt.gridZ + ")") + ", with another group " + marksOther);
        yield return Capture("flow_draw_resume.png", 0, 1, 0.9f);
        int u0 = History.UndoCount, cubes0 = UserCubes(), rs0 = PathManager.FlowResumes, all0 = SequenceMaster.Cubes.Count;
        yield return Click(T(0, 3, 3));
        yield return Frames(2);
        var d = pm.Draft;
        Run("i. a click on that last bead picks the path up again: the SAME cube is the draft (four notes, a hologram), no new cube, no card", () =>
        {
            if (!pm.IsDrawing || d == null) return "not drawing";
            if (d != old || d.id != IdOld || d.nodes.Count != 4 || !d.Hologram || d.isFinalized) return "draft id " + d.id + " nodes " + d.nodes.Count + " hologram " + d.Hologram;
            if (SequenceMaster.Cubes.Count != all0 || CubeInspector.IsOpen) return "cubes " + all0 + " -> " + SequenceMaster.Cubes.Count + " card " + CubeInspector.IsOpen;
            return PathManager.FlowResumes == rs0 + 1 && pm.ResumeMarkCount == 0 ? null : "resumes +" + (PathManager.FlowResumes - rs0) + " badges " + pm.ResumeMarkCount;
        });
        Key(KeyCode.Backspace);
        yield return null;
        bool back = d.nodes.Count == 3 && d.RealTile(2) == T(0, 2, 3);
        yield return Click(T(0, 3, 2));
        Key(KeyCode.Return);
        yield return Frames(3);
        Run("i. Backspace takes the last OLD note; a new note in its place; Enter: the same cube finished with its new end, ONE History entry; undo gives the old path back", () =>
        {
            if (!back) return "Backspace did not take the old note";
            var c = ById(IdOld);
            if (pm.IsDrawing || c == null || !c.isFinalized || c.nodes.Count != 4 || UserCubes() != cubes0) return "cube " + Seq(c) + " cubes " + UserCubes();
            if (Seq(c) != "0(0,3) 0(1,3) 0(2,3) 0(3,2)") return Seq(c);
            if (History.UndoCount != u0 + 1) return "undo " + u0 + " -> " + History.UndoCount;
            History.Undo();
            var o = ById(IdOld);
            return o != null && o.nodes.Count == 4 && Seq(o) == "0(0,3) 0(1,3) 0(2,3) 0(3,3)" ? null : "after undo: " + Seq(o);
        });
        yield return Frames(3);
        // picked up and put down unchanged: nothing in History
        u0 = History.UndoCount;
        FrameGrids(0, 1);
        yield return Click(T(0, 3, 3));
        bool up = pm.IsDrawing && pm.Draft != null && pm.Draft.id == IdOld;
        Key(KeyCode.Escape);
        yield return Frames(2);
        Run("i. picked up and ended unchanged: the path is as it was and History has no new entry", () =>
        {
            var c = ById(IdOld);
            return up && !pm.IsDrawing && c != null && c.isFinalized && c.nodes.Count == 4 && History.UndoCount == u0 ? null : "picked up " + up + " cube " + Seq(c) + " undo " + u0 + " -> " + History.UndoCount;
        });
        // the walk-on of a resumed path: four more beats do not fit grid 0 — the old cube's span grows
        yield return Click(T(0, 3, 3));
        yield return Click(T(0, 4, 3));
        Key(KeyCode.Return);
        yield return Frames(2);
        Run("i. a picked-up path walks on like a new one (its fifth beat lands on grid 1, span 1)", () => { var c = ById(IdOld); return c != null && c.span == 1 && c.nodes.Count == 5 && c.GridOfNode(4) == Grid(1) ? null : Seq(c); });
        PathManager.SimPos = Sea;
    }

    // ================================================================== j. no words, one size row
    static IEnumerator Words()
    {
        var pm = PM;
        LoadState(Song("flow draw j", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "j. the tiles are pickable", "no camera pose"); yield break; }
        if (UIManager.I != null) UIManager.I.SetHudVisible(true);
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        PathManager.SimPos = Sea;
        yield return Wait(0.9f);
        bool rowBefore = HudSizeRow.I != null && HudSizeRow.I.Shown;
        yield return Clicks(0, 0, 0, 1, 0, 2, 0, 3, 0);
        yield return Wait(0.7f);
        bool rowDuring = HudSizeRow.I != null && HudSizeRow.I.Row != null && HudSizeRow.I.Row.gameObject.activeSelf;
        string w4 = DraftWords(); string fill4 = DurationPicker.FillText; bool chip4 = DurationPicker.ExpandShown;
        yield return Click(T(0, 3, 1));
        yield return Wait(0.5f);
        string w5 = DraftWords(); string fill5 = DurationPicker.FillText; bool chip5 = DurationPicker.ExpandShown;
        Run("j. no words while drawing: a full grid and the note past it show no fill tab, no \"capacity reached\", no expand chip — the draft row is up with its sizes, hear-it, rest and done", () =>
        {
            if (!DurationPicker.IsShown || DurationPicker.Mode != DurationPicker.Where.Draft) return "the draft row is not up";
            if (w4.Length > 0 || w5.Length > 0) return "words: " + w4 + " / " + w5;
            if (fill4.Length > 0 || fill5.Length > 0 || chip4 || chip5 || pm.CapacityReached) return "fill '" + fill4 + "' '" + fill5 + "' chip " + chip4 + " " + chip5 + " reached " + pm.CapacityReached;
            return DurationPicker.RestShown && DurationPicker.DoneShown && DurationPicker.LoopShown ? null : "row parts: rest " + DurationPicker.RestShown + " done " + DurationPicker.DoneShown + " hear-it " + DurationPicker.LoopShown;
        });
        Key(KeyCode.Return);
        yield return Wait(0.9f);
        bool rowAfter = HudSizeRow.I != null && HudSizeRow.I.Shown;
        Run("j. ONE size row while drafting: the left HUD size row is up with a cube in the hand, hidden while a path is drawn, back after the pen up", () =>
        {
            if (HudSizeRow.I == null) return "no HudSizeRow in this scene";
            return rowBefore && !rowDuring && rowAfter ? null : "before " + rowBefore + " during " + rowDuring + " after " + rowAfter;
        });
        PathManager.SimPos = Sea;
    }

    // ================================================================== k. Flow off: the v7 rules
    static IEnumerator Legacy()
    {
        var pm = PM;
        LoadState(Song("flow draw k", FourChords()));
        yield return Frames(4);
        if (!FrameGrids(0, 1)) { Line(false, "k. the tiles are pickable", "no camera pose"); yield break; }
        PathManager.Flow = false;
        pm.PickUpCube(Pluck); pm.SetBrush(24);
        int cubes0 = UserCubes(), rf0 = PathManager.CapacityRefusals, sp0 = PathManager.FlowSpans, u0 = History.UndoCount;
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return Wait(0.3f);
        yield return Click(T(0, 0, 0));
        bool paused = !GlobalClock.IsPlaying && !FocusLoop.Active;
        yield return Clicks(0, 1, 0, 2, 0, 3, 0);
        var d = pm.Draft;
        yield return Click(T(0, 3, 1));
        yield return Frames(3);
        Run("k. Flow off: a playing song pauses when a path starts; in a full section the fifth beat is REFUSED (four nodes, capacity reached, the row says so and shows the expand chip, no span)", () =>
        {
            if (!paused) return "the song did not pause";
            if (d == null || d.nodes.Count != 4 || d.span != 0) return "nodes " + (d == null ? -1 : d.nodes.Count);
            if (!pm.CapacityReached || PathManager.CapacityRefusals != rf0 + 1 || PathManager.FlowSpans != sp0) return "reached " + pm.CapacityReached + " refusals +" + (PathManager.CapacityRefusals - rf0);
            return DurationPicker.ExpandShown && DurationPicker.FillText == "capacity reached" && !DurationPicker.RestShown ? null : "fill '" + DurationPicker.FillText + "' chip " + DurationPicker.ExpandShown + " rest cube " + DurationPicker.RestShown;
        });
        Key(KeyCode.R);
        yield return null;
        bool noRest = d.nodes.Count == 4;
        yield return Click(T(0, 3, 0));
        yield return Frames(2);
        Run("k. Flow off: R adds nothing; a click on the last tile FINISHES the path (one cube, one History entry)", () =>
            noRest && !pm.IsDrawing && d.isFinalized && d.nodes.Count == 4 && UserCubes() == cubes0 + 1 && History.UndoCount == u0 + 1 ? null : "rest ignored " + noRest + " drawing " + pm.IsDrawing + " cubes " + UserCubes());
        yield return Clicks(1, 0, 0, 1, 0);
        bool drawing = pm.IsDrawing;
        Key(KeyCode.Escape);
        yield return Wait(0.5f);
        Run("k. Flow off: Esc drops the draft (no cube, no History entry)", () => drawing && !pm.IsDrawing && UserCubes() == cubes0 + 1 && History.UndoCount == u0 + 1 ? null : "drawing " + drawing + " -> " + pm.IsDrawing + " cubes " + UserCubes());
        // a drag stroke finishes on release (v3 §2.6)
        Vector3 p0 = Scr(T(1, 0, 2).Top), p1 = Scr(T(1, 1, 2).Top), p2 = Scr(T(1, 2, 2).Top);
        pm.SimPointer(p0, true, true, false);
        pm.SimPointer(p1, false, true, false);
        pm.SimPointer(p2, false, true, false);
        pm.SimPointer(p2, false, false, true);
        yield return Frames(2);
        Run("k. Flow off: a drag stroke finishes on release", () => !pm.IsDrawing && UserCubes() == cubes0 + 2 ? null : "drawing " + pm.IsDrawing + " cubes " + UserCubes());
        PathManager.Flow = true;
        PathManager.SimPos = Sea;
    }
}
