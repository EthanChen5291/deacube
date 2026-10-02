using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// The user's 2026-10-01 header asks: standard symbols on the island header and its more card (captures), "paste on next grid" from the last grid
/// adds a grid (InspectorCard.PasteOnNewGrid), play starts from a picked grid (GlobalClock.Toggle), un-extend cuts the long grid into grids that keep
/// their notes (SongManager.SplitExtend) and the − buttons, plus an exploration of "a grid added beside a repeated / extended grid". Runs on
/// V7ChecksB's test song. Report: Captures/s17_report.txt; captures s17_header*.png.
/// </summary>
public static class S17Checks
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "s17_report.txt");
    static SongManager SM => SongManager.I;

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" S17-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string where = (stack ?? "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        string line = msg + " @ " + where;
        if (errors.Count < 20) errors.Add(line.Length > 320 ? line.Substring(0, 320) : line);
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(Guarded(captures));
        return "started";
    }

    static IEnumerator Guarded(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("S17Checks ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        var body = Routine(sb, captures);
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(sb, false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { Restore(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog;
        Line(sb, errors.Count == 0, "no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) PathManager.I.PutDown();
    }

    static void Restore()
    {
        PathManager.SimOnly = false;
        IslandHeader.Hide(); IslandTray.Close(); GlobalClock.Stop();
        Clipboard.ClearPaths(); Clipboard.Clear();
        if (UIManager.I != null) UIManager.I.ClearSelection();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
    }

    static KeyBlock ChordAt(int col) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == col);
    static int Idx(KeyBlock kb) => SM.Islands.IndexOf(kb);
    static List<AudioCube> CubesOn(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && c.isFinalized && !c.IsOnMoon && c.Island == kb && c.nodes.Count > 0).ToList();

    static CubeState Cube(KeyBlock kb, int instrument, int[] xs, int[] zs, int[] durs)
    {
        for (int i = 0; i < xs.Length; i++) { xs[i] = Mathf.Clamp(xs[i], 0, kb.cols - 1); zs[i] = Mathf.Clamp(zs[i], 0, kb.rows - 1); }
        return new CubeState { instrument = instrument, measure = Idx(kb), xs = xs, zs = zs, rests = new bool[xs.Length], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f };
    }

    static void Load()
    {
        V7ChecksB.LoadTest();
        var g0 = ChordAt(0);
        PathManager.I.RestoreCube(Cube(g0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 }));
        PathManager.I.RestoreCube(Cube(g0, Clipboard.BassGroup, new[] { 0, 0 }, new[] { 0, 2 }, new[] { 48, 48 }));
        SequenceMaster.RecalculateTimeline();
        History.Reset(); History.Push();
    }

    static IEnumerator Frame(KeyBlock a, KeyBlock b)
    {
        var view = a.WorldBounds; if (b != null) view.Encapsulate(b.WorldBounds);
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(view, 0.1f, true);
        yield return Wait(0.8f);
    }

    static IEnumerator Routine(StringBuilder sb, bool captures)
    {
        // ---- 0. the title intro rises with no little pops (the user, 2026-10-01: "when the title animation appears in the menu screen, there
        // shouldnt be like little pop emojis"): no comic starbursts, no confetti
        SongIO.QuitAutosave = false;
        MainMenu.Show(); yield return Frames(3);
        MainMenu.ReplayIntro(); yield return Frames(2);
        var stage = MenuStage.I;
        int b0 = stage != null ? stage.Bursts : 0, c0 = stage != null ? stage.ConfettiEmitted : 0;
        float tt = Time.realtimeSinceStartup; int shot = 0;
        while (Time.realtimeSinceStartup - tt < 3.4f)
        {
            yield return null;
            if (captures && shot < 3 && Time.realtimeSinceStartup - tt >= 0.9f + shot * 0.75f) { yield return Shot("s17_title_" + shot + ".png"); shot++; }
        }
        int risen = stage != null ? stage.LettersRisen : 0;
        Line(sb, stage != null && risen == 7 && stage.Bursts == b0 && stage.ConfettiEmitted == c0, "the menu title rises with no little pops (no comic starbursts, no confetti)",
             "letters risen " + risen + ", starbursts +" + (stage != null ? stage.Bursts - b0 : -1) + ", confetti +" + (stage != null ? stage.ConfettiEmitted - c0 : -1));
        Prepare();
        Load();
        yield return Frames(3);
        var h = IslandHeader.I;
        KeyBlock g1;

        // ---- 1. the header and its more card (captures)
        var g0 = ChordAt(0);
        SM.SetRepeat(Idx(g0), 2); yield return Frames(2);
        g0 = ChordAt(0);
        yield return Frame(g0, ChordAt(1));
        IslandHeader.Show(g0); yield return Wait(0.6f);
        if (captures) yield return Shot("s17_header.png");
        h.More.onClick(); yield return Wait(0.5f);
        if (captures) yield return Shot("s17_header_more.png");
        Line(sb, h.RepeatSticker == "×2", "the repeat button shows ×2 and its − (repeat one time less)", "sticker \"" + h.RepeatSticker + "\"");
        IslandHeader.CloseMore(); IslandHeader.Hide();
        SM.SetRepeat(Idx(ChordAt(0)), 1); yield return Frames(2);

        // ---- 1b. CLIMB, the music rule on the user's recording (D minor 7: A4 D5 C5 D5 ×3, C5 D5 F5 D5 — the second time F5 → A5)
        var rec = new[] { 69, 74, 72, 74, 69, 74, 72, 74, 69, 74, 72, 74, 72, 74, 77, 74 };
        var span = new HashSet<int>(); for (int m = 50; m <= 90; m++) span.Add(m);
        var dm7 = new[] { 0, 3, 7, 10 };
        var p1 = Climb.Plan(rec, 1, 62, dm7, span);
        string ch1 = string.Join(",", Enumerable.Range(0, rec.Length).Where(k => p1[k] >= 0).Select(k => k + ":" + rec[k] + "→" + p1[k]).ToArray());
        Line(sb, ch1 == "14:77→81", "climb ×1 on the recorded path over Dm7: only the climax F5 rises, to A5 (the next chord tone) — exactly the recording's second time", ch1);
        var p2 = Climb.Plan(rec, 2, 62, dm7, span);
        string ch2 = string.Join(",", Enumerable.Range(0, rec.Length).Where(k => p2[k] >= 0).Select(k => k + ":" + rec[k] + "→" + p2[k]).ToArray());
        Line(sb, ch2 == "11:74→77,14:77→81", "climb ×2: the climax and the next-highest peak (the last D5 before it) climb, each to the next chord tone (D5 → F5)", ch2);
        var cm = Climb.Plan(new[] { 60, 64, 67, 64 }, 1, 60, new[] { 0, 4, 7, 11 }, span);
        Line(sb, cm[2] == 71 && cm[0] < 0 && cm[1] < 0 && cm[3] < 0, "the same rule on another chord: C E G E over Cmaj7 → the G climbs to B", string.Join(",", cm.Select(v => v.ToString()).ToArray()));
        var top = Climb.Plan(new[] { 60, 64, 67 }, 1, 60, new[] { 0, 4, 7 }, new HashSet<int> { 60, 64, 67 });
        Line(sb, top.All(v => v < 0), "a high note already at the top of its grid stays (nowhere higher)", string.Join(",", top.Select(v => v.ToString()).ToArray()));

        // ---- 1c. CLIMB in the game: the grid's paths climb from the more card (one undo step); the cube plays and stands on the higher tile
        g0 = ChordAt(0);
        var gc = CubesOn(g0);
        IslandHeader.Show(g0); yield return Wait(0.4f);
        int cu0 = History.UndoCount;
        h.ClimbButton.onClick(); yield return Frames(3);
        var keys = gc.FirstOrDefault(c => c.instrument == 0);
        bool climbedOk = keys != null && keys.climb == 1 && Climb.LevelOf(g0) == 1 && History.UndoCount == cu0 + 1;
        string kd = "";
        if (keys != null)
        {
            int root; IList<int> semis; Harmony.ChordOf(g0, out root, out semis);
            var avail = new HashSet<int>(); foreach (var t in g0.tiles) if (t != null) avail.Add(t.midi);
            var src = keys.nodes.Select(t => t.midi).ToArray();
            var want = Climb.Plan(src, 1, root, semis, avail);
            int w0 = -1; for (int w = 0; w < keys.windows.Count; w++) if (keys.windows[w].island == g0 && !keys.windows[w].carried) { w0 = w; break; }
            for (int k = 0; k < src.Length; k++)
            {
                var heard = w0 >= 0 ? keys.TileAt(w0, k) : null;
                int got = heard != null ? heard.midi : -1, exp = want[k] >= 0 ? want[k] : src[k];
                kd += src[k] + (want[k] >= 0 ? "→" + got : "") + " ";
                if (got != exp) climbedOk = false;
            }
            if (want.All(v => v < 0)) { climbedOk = false; kd += "(nothing could climb)"; }
        }
        Line(sb, climbedOk, "the more card's climb: every path on the grid climbs ×1 (one undo step); the keys path plays its climax on the next chord tone up (TileAt)", kd.Trim());
        if (captures) { h.More.onClick(); yield return Wait(0.5f); yield return Shot("s17_climb_more.png"); IslandHeader.CloseMore(); }
        History.Undo(); yield return Frames(3);
        g0 = ChordAt(0);
        Line(sb, Climb.LevelOf(g0) == 0, "undo puts the climb back down", "level " + Climb.LevelOf(g0));
        IslandHeader.Hide(); yield return Frames(2);

        // ---- 1d. add to the left / right of a grid = a copy there (the side + buttons)
        g0 = ChordAt(0); g1 = ChordAt(1);
        IslandHeader.Show(g1); yield return Wait(0.5f);
        if (captures) yield return Shot("s17_side_plus.png");
        int colsL = SM.ColumnCount, g1col = g1.column, g1cubes = CubesOn(g1).Count;
        h.AddLeftButton.onClick(); yield return Frames(4);
        var leftCopy = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == g1col);
        Line(sb, SM.ColumnCount == colsL + 1 && leftCopy != null && CubesOn(leftCopy).Count == g1cubes && ChordAt(g1col + 1) != null,
             "add left: a copy of the grid in a new column before it (the original moves one column right)", "columns " + colsL + " → " + SM.ColumnCount + ", copy cubes " + (leftCopy != null ? CubesOn(leftCopy).Count : -1) + "/" + g1cubes);
        History.Undo(); yield return Frames(3);
        g1 = ChordAt(1); IslandHeader.Show(g1); yield return Wait(0.4f);
        h.AddRightButton.onClick(); yield return Frames(4);
        Line(sb, SM.ColumnCount == colsL + 1 && ChordAt(g1col + 1) != null && CubesOn(ChordAt(g1col + 1)).Count == g1cubes, "add right: a copy in a new column after it", "columns " + SM.ColumnCount);
        History.Undo(); yield return Frames(3);
        IslandHeader.Hide(); yield return Frames(2);

        // ---- 2. play from a picked grid; a pause / play with no new pick resumes
        GlobalClock.Stop();
        var g2 = ChordAt(2);
        int from0 = GlobalClock.PlayedFromGrid;
        double dsp0 = AudioSettings.dspTime; int cb0 = Synth.Callbacks;
        UIManager.I.SelectMeasure(Idx(g2), false);
        GlobalClock.Toggle();
        double at2 = GlobalClock.SongBeatD, s2 = SM.ColumnStart(g2.column);   // read in the same frame: where play starts
        yield return Wait(0.5f);
        double ran = AudioSettings.dspTime - dsp0, moved = GlobalClock.SongBeatD - at2; int blocks = Synth.Callbacks - cb0;
        Line(sb, ran > 0.3 && blocks > 10 && moved > 0.3, "the editor's audio runs: the audio clock advances, the synth renders blocks, the playhead moves (0 = Unity's audio is stalled)",
             "clock +" + ran.ToString("0.000") + " s, synth blocks +" + blocks + ", playhead +" + moved.ToString("0.00") + " beats over 0.5 s");
        bool fromPick = GlobalClock.IsPlaying && GlobalClock.PlayedFromGrid == from0 + 1 && Math.Abs(at2 - s2) < 0.01;
        Line(sb, fromPick, "a grid is picked, play starts at its column", "column " + g2.column + " starts at beat " + s2.ToString("0.00") + ", playhead " + at2.ToString("0.00"));
        GlobalClock.Toggle(); yield return Frames(2);
        double paused = GlobalClock.SongBeatD;
        GlobalClock.Toggle();
        double resumedAt = GlobalClock.SongBeatD;
        yield return Wait(0.3f);
        bool resumed = Math.Abs(resumedAt - paused) < 0.01 && GlobalClock.PlayedFromGrid == from0 + 1;
        Line(sb, resumed, "pause then play with no new pick resumes where it paused (no jump back)", "paused at " + paused.ToString("0.00") + ", play resumed at " + resumedAt.ToString("0.00"));
        g1 = ChordAt(1);
        UIManager.I.SelectMeasure(Idx(g1), false);
        GlobalClock.Toggle(); yield return Frames(2);
        GlobalClock.Toggle();
        double startedAt = GlobalClock.SongBeatD, s1 = SM.ColumnStart(g1.column);
        yield return Wait(0.3f);
        Line(sb, Math.Abs(startedAt - s1) < 0.01, "a grid picked while the song plays: pause, then play starts there", "column " + g1.column + " at " + s1.ToString("0.00") + ", play started at " + startedAt.ToString("0.00"));
        GlobalClock.Stop(); UIManager.I.ClearSelection(); yield return Frames(2);

        // ---- 3. un-extend cuts the long grid into grids that keep their notes
        g0 = ChordAt(0);
        int i0 = Idx(g0);
        SM.SetCarry(i0, 1, 1); yield return Frames(3);
        g0 = ChordAt(0); i0 = Idx(g0);
        var targets = SM.CarryTargets(i0);
        if (targets.Count == 0) { Line(sb, false, "the first grid extends over the next one", "no carry targets"); yield break; }
        var t1 = targets[0];
        var played = CubesOn(g0).Select(c => string.Join(",", c.TilesOn(t1).Select(t => t.midi.ToString()).ToArray())).OrderBy(s => s).ToList();
        int ownBefore = CubesOn(t1).Count, u0 = History.UndoCount;
        if (captures) { yield return Frame(g0, t1); yield return Shot("s17_extended.png"); }
        IslandHeader.Show(g0); yield return Wait(0.4f);
        SM.SplitExtend(i0, 0); yield return Frames(3);
        g0 = ChordAt(0); t1 = SM.Islands.FirstOrDefault(kb => kb != null && kb.column == targets[0].column && kb.kind == targets[0].kind);
        var kept = CubesOn(t1).Select(c => string.Join(",", c.nodes.Select(t => t.midi.ToString()).ToArray())).OrderBy(s => s).ToList();
        bool allKept = played.All(p => kept.Contains(p));
        Line(sb, g0.carry == 0 && CubesOn(t1).Count == ownBefore + played.Count && allKept && History.UndoCount == u0 + 1,
             "un-extend: the extend is off, the grid it let go keeps the notes it played as its own cubes, one undo step",
             "carry " + g0.carry + ", cubes on it " + ownBefore + " → " + CubesOn(t1).Count + ", played [" + string.Join(" | ", played.ToArray()) + "], kept [" + string.Join(" | ", kept.ToArray()) + "], undo +" + (History.UndoCount - u0));
        if (captures) { yield return Frame(g0, t1); yield return Shot("s17_split.png"); }
        IslandHeader.Hide();
        History.Undo(); yield return Frames(3);
        g0 = ChordAt(0);
        Line(sb, g0.carry == 1 && CubesOn(SM.CarryTargets(Idx(g0)).FirstOrDefault()).Count == ownBefore, "undo brings the long grid back (the kept cubes go)", "carry " + g0.carry);
        SM.SetCarry(Idx(g0), 0, 1); yield return Frames(2);

        // ---- 4. paste on next grid from the song's last column adds a grid there
        int last = SM.ColumnCount - 1;
        var lastGrid = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == last);
        if (lastGrid == null)
        {
            // the test song may end on a keyboard / stairs: add a chord grid at the end first
            int added = SM.AddMeasure(Idx(SM.Islands.First(kb => kb != null && kb.column == last)));
            yield return Frames(3);
            last = SM.ColumnCount - 1; lastGrid = added >= 0 ? SM.Islands[added] : null;
            History.Reset(); History.Push();
        }
        if (lastGrid == null) { Line(sb, false, "a chord grid in the last column", "none"); yield break; }
        PathManager.I.RestoreCube(Cube(lastGrid, 0, new[] { 0, 1, 2 }, new[] { 0, 1, 0 }, new[] { 24, 24, 24 }));
        SequenceMaster.RecalculateTimeline(); History.Push(); yield return Frames(2);
        var srcCube = CubesOn(lastGrid).First();
        int cols0 = SM.ColumnCount, u1 = History.UndoCount, n0 = InspectorCard.NewGridPastes;
        bool can = InspectorCard.CanPasteNext(srcCube);
        Clipboard.Copy(srcCube);
        InspectorCard.PasteNextAfterClose(Clipboard.PasteMode.Plain); yield return Frames(4);
        var newest = SM.Islands.FirstOrDefault(kb => kb != null && kb.column == cols0);
        Line(sb, can && SM.ColumnCount == cols0 + 1 && newest != null && CubesOn(newest).Count == 1 && InspectorCard.NewGridPastes == n0 + 1 && History.UndoCount == u1 + 1 && ChordAt(0) != null && CubesOn(ChordAt(0)).Count == 2,
             "paste on next grid from the last grid adds a new grid with the pasted pattern (no wrap to the first grid), one undo step",
             "columns " + cols0 + " → " + SM.ColumnCount + ", cubes on the new grid " + (newest != null ? CubesOn(newest).Count : -1) + ", first grid cubes " + CubesOn(ChordAt(0)).Count + ", undo +" + (History.UndoCount - u1));
        History.Undo(); yield return Frames(3);
        Line(sb, SM.ColumnCount == cols0, "one undo takes the new grid and its pattern back", "columns " + SM.ColumnCount);

        // ---- 5. exploration: a grid added beside a repeated / extended grid
        Load(); yield return Frames(3);
        g0 = ChordAt(0);
        SM.SetRepeat(Idx(g0), 2); yield return Frames(2);
        g0 = ChordAt(0);
        int addR = SM.AddIslandInColumn(Idx(g0), false); yield return Frames(3);
        var nr = addR >= 0 ? SM.Islands[addR] : null;
        Info(sb, "repeat ×2 grid + a grid added below: new repeat " + (nr != null ? nr.repeat : -1) + ", carry " + (nr != null ? nr.carry : -1) + ", column passes " + SM.ColumnPasses(ChordAt(0).column)
                 + ", new grid's long grid " + (nr != null && SM.LongGridOf(nr) != null ? SM.LongGridOf(nr).Count : 0));
        Load(); yield return Frames(3);
        g0 = ChordAt(0);
        SM.SetCarry(Idx(g0), 1, 1); yield return Frames(3);
        g0 = ChordAt(0);
        var tg = SM.CarryTargets(Idx(g0));
        int addE = SM.AddIslandInColumn(Idx(g0), false); yield return Frames(3);
        var ne = addE >= 0 ? SM.Islands[addE] : null;
        Info(sb, "extended grid + a grid added below it: new carry " + (ne != null ? ne.carry : -1) + ", repeat " + (ne != null ? ne.repeat : -1) + ", its long grid " + (ne != null && SM.LongGridOf(ne) != null ? SM.LongGridOf(ne).Count : 0)
                 + ", source long grid " + (SM.LongGridOf(ChordAt(0)) != null ? SM.LongGridOf(ChordAt(0)).Count : 0));
        if (tg.Count > 0)
        {
            var tgt = SM.Islands.FirstOrDefault(kb => kb != null && kb.column == tg[0].column && kb.kind == 0);
            int addT = tgt != null ? SM.AddIslandInColumn(Idx(tgt), false) : -1; yield return Frames(3);
            var nt = addT >= 0 ? SM.Islands[addT] : null;
            Info(sb, "a grid added below the covered grid: carry " + (nt != null ? nt.carry : -1) + ", its long grid " + (nt != null && SM.LongGridOf(nt) != null ? SM.LongGridOf(nt).Count : 0));
        }
        if (captures) { yield return Frame(ChordAt(0), ChordAt(1)); yield return Shot("s17_added_beside.png"); }

        // ---- 6. exploration (the user): "if you have a grid and it's set to extend … and you add a grid above it, it's also extended … i should
        // also be able to add a normal grid on the second measure of an extended grid" — the header's + above, on the extended grid and on its
        // second measure (the covered grid)
        Load(); yield return Frames(3);
        g0 = ChordAt(0);
        SM.SetCarry(Idx(g0), 1, 1); yield return Frames(3);
        g0 = ChordAt(0);
        Info(sb, "X0 extended: " + Describe());
        yield return Frame(g0, ChordAt(1));
        IslandHeader.Show(g0); yield return Wait(0.7f);
        if (captures) yield return Shot("s17_x_ext_header.png");
        int nBefore = SM.Islands.Count;
        h.AddAbove.onClick(); yield return Frames(4); yield return Wait(0.5f);
        Info(sb, "X1 after + above the extended grid (islands " + nBefore + " → " + SM.Islands.Count + "): " + Describe());
        IslandHeader.Hide(); yield return Frames(2);
        yield return Frame(ChordAt(0), ChordAt(1));
        if (captures) yield return Shot("s17_x_ext_added.png");
        History.Undo(); yield return Frames(3);
        var run = SM.LongGridOf(ChordAt(0));
        var cov = run != null && run.Count > 1 ? run[1] : null;
        if (cov != null)
        {
            IslandHeader.Show(cov); yield return Wait(0.7f);
            Info(sb, "X2 the header on the covered grid (the second measure) targets " + (IslandHeader.Current != null ? "column " + IslandHeader.Current.column + (IslandHeader.Current == cov ? " (the covered grid itself)" : " (another grid)") : "nothing"));
            if (captures) yield return Shot("s17_x_cov_header.png");
            nBefore = SM.Islands.Count;
            h.AddAbove.onClick(); yield return Frames(4); yield return Wait(0.5f);
            Info(sb, "X3 after + above the covered grid (islands " + nBefore + " → " + SM.Islands.Count + "): " + Describe());
            IslandHeader.Hide(); yield return Frames(2);
            yield return Frame(ChordAt(0), ChordAt(1));
            if (captures) yield return Shot("s17_x_cov_added.png");
        }
        else Info(sb, "X2 no covered grid found");

        // ---- 7. v8 (the user): a 2-measure grid ("extended … to 8 beats"): a grid added above it is a NORMAL one-measure grid, on the measure the
        // user picks (the 2nd too), movable between the measures; the column keeps its length; the others keep theirs
        Load(); yield return Frames(3);
        g0 = ChordAt(0);
        int im0 = Idx(g0);
        float beats0 = SM.TotalBeats;
        SM.SetBars(im0, 2); yield return Frames(3);
        g0 = ChordAt(0); im0 = Idx(g0);
        int bpbM = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float colStart = SM.ColumnStart(0), W1 = KeyBlock.IslandWidth;
        Line(sb, g0.bars == 2 && SM.ColumnBars(0) == 2 && Mathf.Abs(SM.TotalBeats - (beats0 + bpbM)) < 1e-3f, "a grid set to 2 measures: its column is 2 measures (the song one measure longer)", "bars " + g0.bars + ", column bars " + SM.ColumnBars(0) + ", total " + beats0 + " → " + SM.TotalBeats);
        IslandHeader.Show(g0); yield return Frame(g0, null); yield return Wait(0.6f);
        if (captures) yield return Shot("s17_m_two_slots.png");
        int u7 = History.UndoCount, n7 = SM.Islands.Count;
        h.AddIslandAt(true, 1); yield return Frames(4);
        var second = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == 0 && kb.barOffset == 1);
        bool okSecond = second != null && second.bars == 1 && SM.Islands.Count == n7 + 1 && History.UndoCount == u7 + 1 && SM.ColumnBars(0) == 2;
        string win2 = "";
        if (second != null)
        {
            okSecond &= Mathf.Abs(second.px - (SM.ColumnX(0) + W1)) < 0.01f && Mathf.Abs(second.startBeatOffset - (colStart + bpbM)) < 1e-3f;
            PathManager.I.RestoreCube(Cube(second, 0, new[] { 0, 1, 2 }, new[] { 0, 1, 0 }, new[] { 24, 24, 24 }));
            SequenceMaster.RecalculateTimeline(); yield return Frames(2);
            var c2 = CubesOn(second).FirstOrDefault();
            if (c2 != null && c2.windows.Count > 0) { var w0 = c2.windows[0]; win2 = "window " + w0.start.ToString("0.00") + "+" + w0.length.ToString("0.00"); okSecond &= Mathf.Abs(w0.start - (colStart + bpbM)) < 1e-3f && Mathf.Abs(w0.length - bpbM) < 1e-3f; }
            else okSecond = false;
        }
        Line(sb, okSecond, "+ above the 2-measure grid on its 2nd measure: a NORMAL one-measure grid there (x one measure in, plays measure 2 only), the column stays 2 measures, one undo step",
             second != null ? "bars " + second.bars + ", offset " + second.barOffset + ", x +" + (second.px - SM.ColumnX(0)).ToString("0.00") + " (measure " + W1.ToString("0.00") + "), start " + second.startBeatOffset.ToString("0.00") + ", " + win2 : "no grid on measure 2");
        // (the header moved on to the new grid, as for the user: click the 2-measure grid again, then + on its first measure)
        var twoBar = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == 0 && kb.bars == 2);
        if (twoBar != null) { IslandHeader.Show(twoBar); yield return Wait(0.4f); }
        h.AddIslandAt(true, 0); yield return Frames(4);
        var first1 = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == 0 && kb.barOffset == 0 && kb.bars == 1);
        second = SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == 0 && kb.barOffset == 1);
        bool lane = first1 != null && second != null && Mathf.Abs(first1.pz - second.pz) < 0.6f;
        Line(sb, first1 != null && lane, "a normal grid on measure 1 too: the two one-measure grids share one lane above the 2-measure grid (side by side)", first1 != null && second != null ? "z " + first1.pz.ToString("0.0") + " / " + second.pz.ToString("0.0") : "missing");
        IslandHeader.Hide(); yield return Frames(2);
        yield return Frame(ChordAt(0), null); yield return Wait(0.4f);
        if (captures) yield return Shot("s17_m_added.png");
        // move the measure-2 grid back to measure 1 of the column? (it is taken: it moves to its own lane) — move the measure-1 grid to measure 2 instead
        if (first1 != null)
        {
            int fi = Idx(first1);
            bool movedM = SM.SetBarOffset(fi, 1); yield return Frames(3);
            var nowAt = SM.Islands.Where(kb => kb != null && kb.kind == 0 && kb.column == 0 && kb.bars == 1).Select(kb => kb.barOffset).ToList();
            Line(sb, movedM && nowAt.Count(o => o == 1) == 2 && SM.ColumnBars(0) == 2, "a grid moves to another measure of its column (SetBarOffset: the drag's drop): both one-measure grids on measure 2 now (stacked)", "offsets " + string.Join(",", nowAt.Select(o => o.ToString()).ToArray()));
        }
        History.Undo(); yield return Frames(3); History.Undo(); yield return Frames(3); History.Undo(); yield return Frames(3);
        g0 = ChordAt(0);
        Line(sb, SM.Islands.Count(kb => kb != null && kb.column == 0 && kb.kind == 0) == 1 && g0.bars == 2, "undo takes the added / moved grids back (the 2-measure grid stays)", "column 0 chord grids " + SM.Islands.Count(kb => kb != null && kb.column == 0 && kb.kind == 0));
    }

    /// <summary>The grids of the first three columns: column, z, kind, bars, carry, repeat, long-grid size, width.</summary>
    static string Describe()
    {
        var parts = new List<string>();
        foreach (var kb in SM.Islands)
        {
            if (kb == null || kb.column > 2 || kb.IsMoon) continue;
            var lg = SM.LongGridOf(kb);
            parts.Add("c" + kb.column + " z" + kb.pz.ToString("0.0") + " k" + kb.kind + " bars" + kb.bars + " carry" + kb.carry + " rep" + kb.repeat + " lg" + (lg != null ? lg.Count : 0) + " w" + kb.Width.ToString("0.0"));
        }
        return string.Join(" | ", parts.ToArray());
    }
}
