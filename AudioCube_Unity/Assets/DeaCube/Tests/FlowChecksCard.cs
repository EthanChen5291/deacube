using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Flow E1 (2026-11-13, SPEC flow §3 / BRIEF_E1): the CUBE CARD — fewer things on it, and the edits a composer needs. Play-mode verification:
/// <c>FlowChecksCard.RunAll(captures)</c> starts a coroutine; poll <c>FlowChecksCard.Done</c> or Captures/flow_card_report.txt (numbered PASS /
/// FAIL lines, INFO lines). A test song of four one-bar chord grids in one section and one lane (Am7 Dm7 G7 Cmaj7, C major, 120 bpm) with a
/// pluck cube of three beats on grid 0 (span 0) and a six-beat SPAN cube on grids 1 → 2 (span 1). Everything goes through the real handlers
/// (InkButton.Click, PathGridView / RhythmStrip Sim*, KeyShim through PathManager's and CubeInspector's key steps). Covers:
/// a. the card at rest shows exactly the instrument chip, the volume line, the flat grid, the rhythm strip, the size row (5 sizes + rest),
///    the trash can, the close ✕ and ONE "more"; a real click reaches each; the size row at rest is the brush; no necklace, no pace row;
/// b. every control that moved is an icon of the ⋯ drawer (three groups set apart by space), a real click reaches it and it still does its job;
/// c. INSERT after the selected note (index, length = the brush, the later notes pushed back, the selection moves on, one undo each), an
///    insert that outgrows the bar raises the span, a click with nothing selected extends the end, SPLIT (scissors, key S; refused below the
///    shortest size), key R, Backspace = the selected note only (nothing selected: nothing), Delete / the trash = the cube, undo restores;
/// d. a span cube: the strip spans the chain's beats with a hairline at the seam, the flat grid follows the selection / a click on a strip
///    part, an insert that overflows the chain raises the span (the focus loop follows); a stretch does too, and with PathManager.Flow off the
///    old capacity chip stays;
/// e. no destructive control draws the cross (InkPath.Glyph of the draw routine: trash on the cube's and the note's delete; cross only on close);
/// f. no console errors. Captures flow_card_rest / _more / _selected / _span .png.
/// Never writes the user's save (saves snapshotted and restored); reloads the fixture at the end.
/// </summary>
public static class FlowChecksCard
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "flow_card_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" FC-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test song
    const int Pluck = 1;
    const int IdA = 9501, IdS = 9502, IdL = 9503, IdZ = 9504, IdF = 9505;
    /// <summary>The face at rest before E1 (measured on the same cube with InspectorCard.FaceTargetCount before the rebuild): chip, copy, paste →
    /// next, volume, grid, strip, pencil, reverse, three modes, octave copy ↑ ↓, harmony ↑ ↓, flip, climb, close, delete, the more tab.</summary>
    public const int FaceBefore = 20;
    static readonly string[] RestFace = { "Chip", "More", "Volume", "PathGrid", "RhythmStrip", "Len0", "Len1", "Len2", "Len3", "Len4", "Rest", "Close", "Delete" };
    static readonly string[] ChordNames = { "Am7", "Dm7", "G7", "Cmaj7" };
    static readonly int[] ChordRoots = { 57, 62, 55, 60 };
    static readonly int[][] ChordSemis = { new[] { 0, 3, 7, 10 }, new[] { 0, 3, 7, 10 }, new[] { 0, 4, 7, 10 }, new[] { 0, 4, 7, 11 } };

    static MeasureState Chord(int col) => new MeasureState { chordKey = ChordNames[col], root = ChordRoots[col], semis = (int[])ChordSemis[col].Clone(), bars = 1, kind = 0, col = col, repeat = 1, energy = 2 };
    static CubeState Cube(int id, int measure, int span, int[] xs, int[] zs, int[] durs) => new CubeState
    {
        instrument = Pluck, measure = measure, xs = xs, zs = zs, rests = new bool[xs.Length], mods = new int[xs.Length], durs = durs,
        volume = 1f, moon = -1, twinOf = -1, echoOf = -1, id = id, step = 1, gate = 1, mode = 0, hits = -1, span = span
    };

    /// <summary>Four one-bar chords (one section, one lane); cube A: 1 + 1 + ½ + ½ beats on grid 0; cube S: six quarters from grid 1 on (span 1:
    /// its notes 0..3 on grid 1, 4 and 5 on grid 2).</summary>
    public static SongState TestSong() => new SongState
    {
        version = SongState.CurrentVersion, name = "flow card", bpm = 120f, beatsPerBar = 4, loop = true,
        measures = new[] { Chord(0), Chord(1), Chord(2), Chord(3) },
        cubes = new[]
        {
            Cube(IdA, 0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 1, 2 }, new[] { 24, 24, 12, 12 }),
            Cube(IdS, 1, 1, new[] { 0, 1, 2, 3, 2, 3 }, new[] { 0, 0, 0, 0, 1, 1 }, new[] { 24, 24, 24, 24, 24, 24 }),
            Cube(IdL, 3, 0, new[] { 0, 1, 2 }, new[] { 0, 0, 0 }, null),                                   // a legacy cube (no per-note lengths)
            Cube(IdZ, 3, 0, new[] { 0, 1, 2, 3 }, new[] { 2, 2, 2, 2 }, new[] { 24, 24, 24, 24 })           // a full bar on the section's last grid
        },
        moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
    };

    /// <summary>Three one-bar chords (a section of three measures: its last grid may still grow) with a full-bar cube on the last grid.</summary>
    public static SongState TestSong3() => new SongState
    {
        version = SongState.CurrentVersion, name = "flow card 3", bpm = 120f, beatsPerBar = 4, loop = true,
        measures = new[] { Chord(0), Chord(1), Chord(2) },
        cubes = new[] { Cube(IdF, 2, 0, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 1, 1 }, new[] { 24, 24, 24, 24 }) },
        moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
    };

    static void LoadState(SongState st)
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.ClearRegion();
        GlobalClock.Stop();
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
        AudioCube.SnapAllHome();
        Clipboard.Clear();
        if (PathManager.I != null) PathManager.I.SetBrush(24);
    }

    static AudioCube ById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id && c.isFinalized) return c; return null; }
    static KeyBlock Grid(int i) => SM != null && i >= 0 && i < SM.Islands.Count ? SM.Islands[i] : null;
    static string Durs(AudioCube c) => c == null ? "null" : string.Join(",", c.durs.Select(d => d.ToString()).ToArray());
    static int Total(AudioCube c) { int t = 0; foreach (int d in c.durs) t += d; return t; }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null || PathManager.I == null) return "FAIL: needs Play mode";
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
        PathManager.AutoHand = false; PathManager.Flow = true;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void Restore()
    {
        PathManager.SimOnly = false; PathManager.Flow = true;
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) { PathManager.I.PutDown(); PathManager.I.SetBrush(24); } } catch (Exception) { }
        PathManager.AutoHand = false;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
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
    static IEnumerator WaitFor(Func<bool> cond, float timeout)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; yield return null; }
    }
    static IEnumerator OpenCard(AudioCube c)
    {
        if (c != null && OrbitCamera.I != null)
        {
            var b = c.Island.WorldBounds; b.Expand(new Vector3(6f, 0f, 3f));
            OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.9f);
        }
        yield return Frames(2);
        CubeInspector.Open(c);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open && InspectorCard.Shown >= 1f, 5f);
        yield return Wait(0.3f);
    }
    /// <summary>After an undo / redo the islands are rebuilt: the inspector re-targets the cube with the same id and the card binds it again.</summary>
    static IEnumerator Settle(int id)
    {
        yield return WaitFor(() => { var c = ById(id); return c != null && CubeInspector.Current == c && InspectorCard.Cube == c && InspectorCard.Strip.Cube == c && InspectorCard.Grid.Cube == c; }, 3f);
        yield return Frames(3);
    }

    /// <summary>A key through the card's own step (S, R, Esc) / through PathManager's hotkeys (Backspace, Delete), as a real press arrives.</summary>
    static void CardKey(KeyCode k) { KeyShim.Sim(k, true, true, false); CubeInspector.RunInputForTest(); KeyShim.Clear(); }
    static void WorldKey(KeyCode k) { KeyShim.Sim(k, true, true, false); PathManager.I.RunHotkeysForTest(); KeyShim.Clear(); }

    static readonly Vector3[] corners = new Vector3[4];
    static Vector2 CentreOf(RectTransform r)
    {
        r.GetWorldCorners(corners);
        return RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
    }
    /// <summary>The top UI raycast target at a screen point is <paramref name="target"/> or one of its children (what a real click reaches).</summary>
    static bool TopHit(Transform target, Vector2 screen, out string got)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        got = "no event system";
        if (es == null || target == null) return false;
        var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };
        var list = new List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(ped, list);
        if (list.Count == 0) { got = "nothing"; return false; }
        var go = list[0].gameObject;
        got = go.name;
        return go.transform == target || go.transform.IsChildOf(target);
    }
    static string Reach(Component c)
    {
        if (c == null) return "missing";
        string got;
        return TopHit(c.transform, CentreOf((RectTransform)c.transform), out got) ? null : c.name + " → " + got;
    }
    /// <summary>The standard symbol a control draws (the Glyph of its InkPaths: "trash", "cross", "more", "scissors"; "" none, joined when several).</summary>
    static string Glyphs(Component c)
    {
        if (c == null) return "missing";
        var l = new List<string>();
        foreach (var p in c.GetComponentsInChildren<InkPath>(true)) if (!string.IsNullOrEmpty(p.Glyph) && !l.Contains(p.Glyph)) l.Add(p.Glyph);
        return string.Join("+", l.ToArray());
    }

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("FlowChecksCard ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        Prepare();
        var pm = PathManager.I;

        // ================================================================ a. the card at rest
        LoadState(TestSong());
        yield return Frames(4);
        var a = ById(IdA);
        yield return OpenCard(a);
        var strip = InspectorCard.Strip; var grid = InspectorCard.Grid;
        Run(sb, "a. the card at rest shows exactly: instrument chip, more ⋯, volume, flat grid, rhythm strip, the size row (5 sizes + rest), close ✕, trash — 8 controls (" + RestFace.Length + " hit targets; " + FaceBefore + " before)", () =>
        {
            if (a == null || InspectorCard.Cube != a) return "the card is not on cube A";
            var face = InspectorCard.FaceTargets();
            string got = string.Join(" ", face.ToArray());
            if (face.Count != RestFace.Length) return face.Count + " targets: " + got;
            foreach (var want in RestFace) if (!face.Contains(want)) return "no " + want + " in: " + got;
            if (InspectorCard.FaceTargetCount() != RestFace.Length) return "FaceTargetCount " + InspectorCard.FaceTargetCount();
            if (InspectorCard.NudgeShown || InspectorCard.SplitShown) return "higher / lower / scissors show with no note selected";
            if (strip.TearShown || strip.InsertEdgeShown) return "the strip shows a tear-off / insert edge with no note selected";
            if (InspectorCard.DrawerOpen) return "the drawer is out";
            return null;
        });
        Info(sb, "face at rest: " + string.Join(" ", InspectorCard.FaceTargets().ToArray()) + " — before E1: " + FaceBefore + " targets (chip, copy, paste → next, volume, grid, strip, pencil, reverse, 3 modes, octave copy ↑ ↓, harmony ↑ ↓, flip, climb, close, delete, more tab)");
        Run(sb, "a. a real click reaches every face control (the top raycast target at its centre), a grid cell and a strip block", () =>
        {
            var targets = new Component[] { InspectorCard.ChipButton, InspectorCard.MoreTab, Hints.RectOf("inspector.volume"), InspectorCard.CloseButton, InspectorCard.DeleteButton,
                DurationPicker.Slot(0), DurationPicker.Slot(1), DurationPicker.Slot(2), DurationPicker.Slot(3), DurationPicker.Slot(4) };
            foreach (var t in targets) { string r = Reach(t); if (r != null) return r; }
            string g1, s1;
            if (!TopHit(grid.transform, RectTransformUtility.WorldToScreenPoint(null, grid.transform.TransformPoint(grid.CellPos(4, 0))), out g1)) return "grid cell → " + g1;
            if (!TopHit(strip.transform, RectTransformUtility.WorldToScreenPoint(null, strip.transform.TransformPoint(strip.BlockCentre(0))), out s1)) return "strip block → " + s1;
            return null;
        });
        Run(sb, "a. the size row at rest is the size of the NEXT note: it shows the brush, a pick sets it (no History entry, no note changes)", () =>
        {
            if (!DurationPicker.IsShown || DurationPicker.Mode != DurationPicker.Where.Inspector || DurationPicker.Target != a) return "the size row is not on the card (shown " + DurationPicker.IsShown + ", mode " + DurationPicker.Mode + ")";
            if (DurationPicker.TargetNode != -1) return "it targets node " + DurationPicker.TargetNode + " with nothing selected";
            if (DurationPicker.SelectedTicks != pm.BrushTicks || pm.BrushTicks != 24) return "it shows " + DurationPicker.SelectedTicks + " ticks, the brush is " + pm.BrushTicks;
            int u0 = History.UndoCount; string d0 = Durs(a);
            DurationPicker.SimClick(1);
            if (pm.BrushTicks != 12) return "a click on the eighth left the brush at " + pm.BrushTicks;
            if (History.UndoCount != u0 || Durs(a) != d0) return "the pick changed the song: pushes " + (History.UndoCount - u0) + ", durs " + Durs(a);
            DurationPicker.SimClick(2);
            return pm.BrushTicks == 24 ? null : "back to the quarter: " + pm.BrushTicks;
        });
        Run(sb, "a. the legacy necklace and the pace buttons are gone from the UI", () =>
        {
            if (InspectorCard.Dial != null) return "InspectorCard.Dial is still there";
            int dials = InspectorCard.Root.GetComponentsInChildren<NecklaceDial>(true).Length;
            if (dials != 0) return dials + " NecklaceDial under the card";
            for (int k = 0; k < 5; k++) if (InspectorCard.DrawerButton("pace" + k) != null) return "pace" + k + " still exists";
            foreach (var b in InspectorCard.Root.GetComponentsInChildren<InkButton>(true)) if (b.name.StartsWith("Pace") || b.name == "Necklace") return b.name + " under the card";
            return null;
        });
        if (captures) yield return Shot("flow_card_rest.png");

        // ================================================================ e. the glyphs (at rest and with a note selected)
        strip.SelectNode(1);
        yield return Frames(3);
        Run(sb, "e. no destructive control draws the cross: the cube's delete and the note's delete are trash cans; ✕ only closes; more = ⋯, split = scissors", () =>
        {
            string del = Glyphs(InspectorCard.DeleteButton), close = Glyphs(InspectorCard.CloseButton), more = Glyphs(InspectorCard.MoreTab), split = Glyphs(InspectorCard.SplitButton);
            if (del != "trash") return "the cube's delete draws '" + del + "'";
            if (strip.TearGlyph != "trash") return "the note's tear-off draws '" + strip.TearGlyph + "'";
            if (close != "cross") return "close draws '" + close + "'";
            if (more != "more") return "more draws '" + more + "'";
            if (split != "scissors") return "split draws '" + split + "'";
            // nothing else on the card or in the drawer wears the cross
            foreach (var p in InspectorCard.Root.GetComponentsInChildren<InkPath>(true))
                if (p.Glyph == "cross" && !p.transform.IsChildOf(InspectorCard.CloseButton.transform)) return "a cross on " + p.transform.parent.name + "/" + p.name;
            return null;
        });

        // ================================================================ c. a selected note: the tools beside the size row
        Run(sb, "c. a selected note: higher / lower and ONE scissors show left of the size row, its block wears the trash can and a bold right edge (the insert point), the cube's trash steps aside, the size row shows its size", () =>
        {
            if (strip.SelectedNote != 1) return "selected " + strip.SelectedNote;
            if (!InspectorCard.NudgeShown) return "higher / lower hidden";
            if (!InspectorCard.SplitShown) return "the scissors are hidden";
            int scissors = 0; foreach (var p in InspectorCard.Root.GetComponentsInChildren<InkPath>(false)) if (p.Glyph == "scissors") scissors++;
            if (scissors != 1) return scissors + " scissors on the card";
            if (!strip.TearShown) return "no trash can on the block";
            if (!strip.InsertEdgeShown || Mathf.Abs(strip.InsertEdgeX - strip.BlockEdge(strip.SelectedEvent).x) > 0.5f) return "the insert edge is not on the selected block's right edge (" + strip.InsertEdgeShown + ", x " + strip.InsertEdgeX + " vs " + strip.BlockEdge(strip.SelectedEvent).x + ")";
            if (InspectorCard.DeleteButton.gameObject.activeInHierarchy) return "the cube's trash shows beside the note's";
            if (DurationPicker.TargetNode != 1 || DurationPicker.SelectedTicks != a.durs[1]) return "the size row: node " + DurationPicker.TargetNode + ", " + DurationPicker.SelectedTicks + " ticks";
            var face = InspectorCard.FaceTargets();
            foreach (var want in new[] { "Higher", "Lower", "Split", "Tear" }) if (!face.Contains(want)) return "no " + want + " on the face: " + string.Join(" ", face.ToArray());
            string r = Reach(InspectorCard.SplitButton) ?? Reach(InspectorCard.NudgeButton(1)) ?? Reach(InspectorCard.NudgeButton(-1));
            return r;
        });
        if (captures) yield return Shot("flow_card_selected.png");

        // ---------------------------------------------------------------- INSERT
        int rows = a.Island.rows, cols = a.Island.cols;
        strip.Deselect(); yield return Frames(2);
        DurationPicker.SimClick(1);   // the brush: an eighth
        yield return Frames(2);
        strip.SelectNode(1); yield return Frames(2);
        {
            var tiles0 = new List<TileInteraction>(a.nodes);
            int u0 = History.UndoCount, ins0 = grid.InsertCount;
            float start2 = a.NodeStartBeat(2);
            grid.SimDownCell(cols - 2, rows - 1); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. INSERT: with note 1 selected a click on a free tile adds a note of the brush size (an eighth) right after it — index 2, the later notes pushed back, the selection on the new note, one History entry", () =>
            {
                if (pm.BrushTicks != 12) return "the brush is " + pm.BrushTicks;
                if (a.nodes.Count != 5) return "nodes " + a.nodes.Count;
                if (a.nodes[2] != a.Island.GetTile(cols - 2, rows - 1)) return "node 2 is on (" + a.nodes[2].gridX + "," + a.nodes[2].gridZ + ")";
                if (Durs(a) != "24,24,12,12,12") return "durs " + Durs(a);
                if (a.nodes[0] != tiles0[0] || a.nodes[1] != tiles0[1] || a.nodes[3] != tiles0[2] || a.nodes[4] != tiles0[3]) return "the other notes moved tiles";
                if (Mathf.Abs(a.NodeStartBeat(3) - (start2 + 0.5f)) > 1e-3f) return "the old note 2 starts at " + a.NodeStartBeat(3) + " (was " + start2 + ")";
                if (strip.SelectedNote != 2 || grid.HighlightNode != 2) return "selection: strip " + strip.SelectedNote + ", grid " + grid.HighlightNode;
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                if (grid.InsertCount != ins0 + 1) return "InsertCount +" + (grid.InsertCount - ins0);
                return null;
            });
            u0 = History.UndoCount;
            grid.SimDownCell(cols - 1, rows - 1); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. clicks keep inserting in order: the next click adds its note after the one just added (the selection moved on), one History entry", () =>
            {
                if (a.nodes.Count != 6) return "nodes " + a.nodes.Count + " durs " + Durs(a);
                if (a.nodes[3] != a.Island.GetTile(cols - 1, rows - 1)) return "node 3 is on (" + a.nodes[3].gridX + "," + a.nodes[3].gridZ + ")";
                if (Durs(a) != "24,24,12,12,12,12") return "durs " + Durs(a);
                if (strip.SelectedNote != 3) return "selected " + strip.SelectedNote;
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                if (a.span != 0) return "span " + a.span + " (the bar is exactly full: 4 beats)";
                return null;
            });
            // the bar is full now (4 beats). A click on the selected note's own bead = that note again — and it overflows: Flow, the span grows
            int rs0 = RhythmStrip.RoomSpans, ref0 = grid.CapacityRefusals; u0 = History.UndoCount;
            grid.SimDownNode(3); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. a click on a tile that already has a note inserts that note again; this one outgrows the bar and follows Flow — the cube's span grows onto the lane's next grid (no refusal, no chip), still one History entry", () =>
            {
                if (a.nodes.Count != 7) return "nodes " + a.nodes.Count;
                if (a.PathTiles()[4].gridX != cols - 1 || a.PathTiles()[4].gridZ != rows - 1) return "node 4 is not node 3's tile again";
                if (strip.SelectedNote != 4) return "selected " + strip.SelectedNote;
                if (a.span != 1 || RhythmStrip.RoomSpans != rs0 + 1) return "span " + a.span + ", RoomSpans +" + (RhythmStrip.RoomSpans - rs0);
                if (grid.CapacityRefusals != ref0 || grid.CapacityChipShown) return "refused / chip (" + (grid.CapacityRefusals - ref0) + ", " + grid.CapacityChipShown + ")";
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                if (Mathf.Abs(PathManager.CapacityOf(a.Island, a.span) - 8f) > 1e-3f) return "capacity " + PathManager.CapacityOf(a.Island, a.span);
                return null;
            });
            // one undo each
            string undo = null;
            int[] wantNodes = { 6, 5, 4 }; int[] wantSpan = { 0, 0, 0 };
            for (int k = 0; k < 3 && undo == null; k++)
            {
                History.Undo();
                yield return Settle(IdA);
                var c2 = ById(IdA);
                if (c2 == null) undo = "cube lost after undo " + (k + 1);
                else if (c2.nodes.Count != wantNodes[k] || c2.span != wantSpan[k]) undo = "after undo " + (k + 1) + ": " + c2.nodes.Count + " nodes, span " + c2.span + " (want " + wantNodes[k] + ", " + wantSpan[k] + ")";
            }
            a = ById(IdA);
            Run(sb, "c. one undo takes back each insert (7 → 6 → 5 → 4 notes; the span the last one raised goes with it)", () => undo ?? (Durs(a) == "24,24,12,12" ? null : "durs " + Durs(a)));
        }
        strip = InspectorCard.Strip; grid = InspectorCard.Grid;

        // ---------------------------------------------------------------- nothing selected: a click extends the end
        {
            pm.SetBrush(12); yield return Frames(2);
            int u0 = History.UndoCount;
            grid.SimDownCell(cols - 1, 0); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. with nothing selected a click on a free tile extends the END (at the brush size), nothing gets selected, one History entry", () =>
            {
                if (strip.SelectedNote >= 0) return "a note is selected: " + strip.SelectedNote;
                if (a.nodes.Count != 5 || a.nodes[4] != a.Island.GetTile(cols - 1, 0)) return "nodes " + a.nodes.Count;
                if (Durs(a) != "24,24,12,12,12") return "durs " + Durs(a);
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            History.Undo(); yield return Settle(IdA);
            a = ById(IdA); strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            // the size row's hollow cube with nothing selected: a rest of the brush size at the end
            u0 = History.UndoCount; var lastTile = a.nodes[a.nodes.Count - 1];
            string reachRest = null; { string got; var rb = DurationPicker.Slot(0).transform.parent.Find("Rest"); if (rb == null || !TopHit(rb, CentreOf((RectTransform)rb), out got)) reachRest = "the rest cube is not reachable"; }
            DurationPicker.SimClickRest();
            yield return Frames(3);
            Run(sb, "c. with nothing selected the size row's hollow cube adds a REST of the brush size at the end (the cube waits on its last tile), one History entry", () =>
            {
                if (reachRest != null) return reachRest;
                if (a.nodes.Count != 5 || a.ModOf(4) != 1) return "nodes " + a.nodes.Count + ", last mod " + a.ModOf(a.nodes.Count - 1);
                if (Durs(a) != "24,24,12,12,12" || a.nodes[4] != lastTile) return "durs " + Durs(a);
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            History.Undo(); yield return Settle(IdA);
            a = ById(IdA); strip = InspectorCard.Strip; grid = InspectorCard.Grid;
        }

        // ---------------------------------------------------------------- SPLIT, key R
        {
            strip.SelectNode(0); yield return Frames(3);
            int u0 = History.UndoCount; var t0 = a.nodes[0]; int tot0 = Total(a);
            CardKey(KeyCode.S);
            yield return Frames(3);
            Run(sb, "c. SPLIT (key S): the selected quarter becomes two eighths on the same tile, the later notes stay where they were, the first half stays selected, one History entry", () =>
            {
                if (a.nodes.Count != 5) return "nodes " + a.nodes.Count;
                if (Durs(a) != "12,12,24,12,12") return "durs " + Durs(a);
                if (a.nodes[0] != t0 || a.nodes[1] != t0) return "the halves are not on the note's tile";
                if (Total(a) != tot0) return "the pattern's length changed: " + Total(a) + " (was " + tot0 + ")";
                if (strip.SelectedNote != 0) return "selected " + strip.SelectedNote;
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            u0 = History.UndoCount; int sp0 = strip.SplitCount;
            InspectorCard.SplitButton.Click();   // the scissors: an eighth → two sixteenths
            yield return Frames(3);
            string afterScissors = Durs(a); int uScissors = History.UndoCount - u0;
            InspectorCard.SplitButton.Click();   // a sixteenth: nothing shorter — refused with a tick
            CardKey(KeyCode.S);
            yield return Frames(3);
            Run(sb, "c. SPLIT (the scissors): an eighth → two sixteenths (one History entry); below the shortest size it is refused (nothing changes, no History entry)", () =>
            {
                if (afterScissors != "6,6,12,24,12,12" || uScissors != 1) return "after the scissors: durs " + afterScissors + ", History entries " + uScissors;
                if (Durs(a) != "6,6,12,24,12,12") return "a sixteenth was split: " + Durs(a);
                if (History.UndoCount != u0 + 1) return "a refused split pushed History: " + (History.UndoCount - u0);
                if (strip.SplitCount != sp0 + 1) return "SplitCount +" + (strip.SplitCount - sp0);
                return null;
            });
            u0 = History.UndoCount;
            strip.SelectNode(3); yield return Frames(2);
            CardKey(KeyCode.R); yield return Frames(2);
            int modOn = a.ModOf(3), uOn = History.UndoCount - u0;
            CardKey(KeyCode.R); yield return Frames(2);
            Run(sb, "c. key R: rest on / off for the selected note, one History entry each (right-click on its block stays)", () =>
            {
                if (modOn != 1 || uOn != 1) return "R: mod " + modOn + ", History entries " + uOn;
                if (a.ModOf(3) != 0 || History.UndoCount != u0 + 2) return "R again: mod " + a.ModOf(3) + ", History entries " + (History.UndoCount - u0);
                int u1 = History.UndoCount; Vector2 at = strip.BlockCentre(strip.SelectedEvent);
                strip.SimDown(at, 1); strip.SimUp(at, 1);
                if (a.ModOf(3) != 1 || History.UndoCount != u1 + 1) return "right-click: mod " + a.ModOf(3);
                strip.SimDown(at, 1); strip.SimUp(at, 1);
                return a.ModOf(3) == 0 ? null : "right-click back: mod " + a.ModOf(3);
            });
            strip.Deselect(); yield return Frames(2);
            int uNo = History.UndoCount; string dNo = Durs(a);
            CardKey(KeyCode.S); CardKey(KeyCode.R); yield return Frames(2);
            Run(sb, "c. S and R with nothing selected do nothing", () => Durs(a) == dNo && History.UndoCount == uNo && a.ModOf(0) == 0 ? null : "durs " + Durs(a) + ", History entries " + (History.UndoCount - uNo));
        }

        // ---------------------------------------------------------------- Backspace / Delete / the trash
        {
            strip.SelectNode(2); yield return Frames(2);
            int n0 = a.nodes.Count, u0 = History.UndoCount, cubes0 = SequenceMaster.Cubes.Count;
            WorldKey(KeyCode.Backspace);
            yield return Frames(3);
            Run(sb, "c. Backspace with a note selected deletes that note only (the cube and the card stay), one History entry", () =>
            {
                if (a == null || a.nodes.Count != n0 - 1) return "nodes " + n0 + " → " + (a != null ? a.nodes.Count : -1);
                if (!CubeInspector.IsOpen || CubeInspector.Current != a || SequenceMaster.Cubes.Count != cubes0) return "the cube / the card went";
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            strip.Deselect(); yield return Frames(2);
            n0 = a.nodes.Count; u0 = History.UndoCount; int bt0 = CubeInspector.BackspaceTicks;
            WorldKey(KeyCode.Backspace);
            yield return Frames(3);
            Run(sb, "c. Backspace with NO note selected does nothing (a soft tick): the cube stays, no History entry", () =>
            {
                if (ById(IdA) != a || a.nodes.Count != n0) return "the cube changed: " + (ById(IdA) == null ? "deleted" : a.nodes.Count + " nodes");
                if (!CubeInspector.IsOpen || CubeInspector.Current != a) return "the card closed";
                if (History.UndoCount != u0) return "History entries " + (History.UndoCount - u0);
                if (CubeInspector.BackspaceTicks != bt0 + 1) return "no soft tick (" + (CubeInspector.BackspaceTicks - bt0) + ")";
                return null;
            });
            u0 = History.UndoCount; cubes0 = SequenceMaster.Cubes.Count;
            WorldKey(KeyCode.Delete);
            yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
            yield return Frames(3);
            bool gone = ById(IdA) == null && SequenceMaster.Cubes.Count == cubes0 - 1; int uDel = History.UndoCount - u0;
            History.Undo();
            yield return Frames(5);
            a = ById(IdA);
            Run(sb, "c. the Delete key deletes the cube (one History entry, no dialog); undo brings it back with its notes", () =>
            {
                if (!gone || uDel != 1) return "deleted " + gone + ", History entries " + uDel;
                if (a == null || a.nodes.Count != n0) return "undo: " + (a == null ? "no cube" : a.nodes.Count + " nodes (want " + n0 + ")");
                return null;
            });
            yield return OpenCard(a);
            strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            u0 = History.UndoCount;
            string reach = Reach(InspectorCard.DeleteButton);
            InspectorCard.DeleteButton.Click();
            yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
            yield return Frames(3);
            gone = ById(IdA) == null; uDel = History.UndoCount - u0;
            History.Undo();
            yield return Frames(5);
            a = ById(IdA);
            Run(sb, "c. the trash can deletes the cube (one History entry); undo brings it back", () => reach ?? (gone && uDel == 1 && a != null && a.nodes.Count == n0 ? null : "deleted " + gone + ", History entries " + uDel + ", back " + (a != null)));
        }

        // ---------------------------------------------------------------- a legacy cube bakes on its first insert; a full section refuses
        LoadState(TestSong());
        yield return Frames(4);
        {
            var l = ById(IdL);
            yield return OpenCard(l);
            strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            pm.SetBrush(12); yield return Frames(2);
            bool legacy0 = l != null && !l.HasDurations; int bake0 = RhythmStrip.BakeCalls, u0 = History.UndoCount;
            strip.SelectNode(0); yield return Frames(3);
            grid.SimDownCell(l.Island.cols - 1, 0); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. a legacy cube (uniform steps): its first insert bakes it to per-note lengths, then adds the brush-size note after the selected one (one History entry)", () =>
            {
                if (!legacy0) return "cube L is not a legacy cube";
                if (!l.HasDurations || RhythmStrip.BakeCalls != bake0 + 1) return "baked " + l.HasDurations + ", BakeCalls +" + (RhythmStrip.BakeCalls - bake0);
                if (l.nodes.Count != 4 || l.durs.Count != 4 || l.durs[1] != 12 || l.nodes[1] != l.Island.GetTile(l.Island.cols - 1, 0)) return "nodes " + l.nodes.Count + ", durs " + Durs(l);
                if (strip.SelectedNote != 1) return "selected " + strip.SelectedNote;
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            CubeInspector.CloseImmediate(); yield return Frames(3);
            var z = ById(IdZ);
            yield return OpenCard(z);
            strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            pm.SetBrush(24); yield return Frames(2);
            strip.SelectNode(3); yield return Frames(3);
            u0 = History.UndoCount; int gr0 = grid.CapacityRefusals;
            grid.SimDownCell(z.Island.cols - 1, z.Island.rows - 1); grid.SimUp();
            yield return Frames(3);
            Run(sb, "c. where Flow has no room left (a full bar on the last grid of a full four-measure section) the insert is refused with a thud — no chip, no words, no History entry", () =>
            {
                if (z.nodes.Count != 4 || z.span != 0) return "nodes " + z.nodes.Count + ", span " + z.span;
                if (grid.CapacityRefusals != gr0 + 1) return "refusals +" + (grid.CapacityRefusals - gr0);
                if (grid.CapacityChipShown) return "the capacity chip shows in Flow";
                if (History.UndoCount != u0) return "History entries " + (History.UndoCount - u0);
                if (SM.ColumnBars(3) != 1) return "the grid grew to " + SM.ColumnBars(3) + " measures (the section is full)";
                return null;
            });
            CubeInspector.CloseImmediate(); yield return Frames(3);
        }
        // ---------------------------------------------------------------- R2: the section's last grid grows a measure
        LoadState(TestSong3());
        yield return Frames(4);
        {
            var f = ById(IdF);
            yield return OpenCard(f);
            strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            pm.SetBrush(24); yield return Frames(2);
            strip.SelectNode(3); yield return Frames(3);
            int u0 = History.UndoCount, rg0 = RhythmStrip.RoomGrows, bars0 = SM.ColumnBars(2);
            grid.SimDownCell(f.Island.cols - 1, f.Island.rows - 1); grid.SimUp();
            yield return Frames(4);
            Run(sb, "c. at a section's last grid (the section still has room) an insert that outgrows the bar grows that grid a measure, as while drawing (Flow R2); one History entry", () =>
            {
                if (bars0 != 1) return "the grid started at " + bars0 + " measures";
                if (f.nodes.Count != 5 || f.span != 0) return "nodes " + f.nodes.Count + ", span " + f.span;
                if (SM.ColumnBars(2) != 2 || RhythmStrip.RoomGrows != rg0 + 1) return "measures " + SM.ColumnBars(2) + ", RoomGrows +" + (RhythmStrip.RoomGrows - rg0);
                if (Mathf.Abs(strip.TotalBeats - 8f) > 1e-3f) return "the strip shows " + strip.TotalBeats + " beats";
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            History.Undo(); yield return Settle(IdF);
            f = ById(IdF);
            Run(sb, "c. one undo takes the insert back together with the measure it grew", () => f != null && f.nodes.Count == 4 && SM.ColumnBars(2) == 1 ? null : (f == null ? "cube lost" : f.nodes.Count + " notes, " + SM.ColumnBars(2) + " measures"));
            CubeInspector.CloseImmediate(); yield return Frames(3);
        }

        // ================================================================ b. the ⋯ drawer
        LoadState(TestSong());
        yield return Frames(4);
        a = ById(IdA);
        yield return OpenCard(a);
        strip = InspectorCard.Strip; grid = InspectorCard.Grid;
        string moreReach = Reach(InspectorCard.MoreTab);
        InspectorCard.MoreTab.Click();
        yield return WaitFor(() => InspectorCard.DrawerShown >= 1f, 2f);
        yield return Wait(0.35f);
        Run(sb, "b. ONE more (⋯) pulls the drawer out: 22 icons in three groups set apart by space, not lines — path (5) · copies (4 + 4) · devices (4 + 5) — evenly spaced, a real click reaches each", () =>
        {
            if (moreReach != null) return moreReach;
            if (!InspectorCard.DrawerOpen || !InspectorCard.IdeasShown) return "the drawer is not out";
            var icons = InspectorCard.DrawerIcons();
            if (icons.Count != 22) return icons.Count + " icons: " + string.Join(" ", icons.Select(b => b.name).ToArray());
            // rows by y, columns by x (drawer-local)
            var ys = icons.Select(b => Mathf.Round(((RectTransform)b.transform).anchoredPosition.y)).Distinct().OrderByDescending(y => y).ToList();
            if (ys.Count != 5) return ys.Count + " rows";
            int[] perRow = ys.Select(y => icons.Count(b => Mathf.Abs(((RectTransform)b.transform).anchoredPosition.y - y) < 1f)).ToArray();
            if (string.Join(",", perRow.Select(v => v.ToString()).ToArray()) != "5,4,4,4,5") return "icons per row " + string.Join(",", perRow.Select(v => v.ToString()).ToArray());
            float g01 = ys[0] - ys[1], g12 = ys[1] - ys[2], g23 = ys[2] - ys[3], g34 = ys[3] - ys[4];
            if (Mathf.Abs(g12 - g34) > 0.5f || Mathf.Abs(g01 - g23) > 0.5f || g01 < g12 + 12f) return "row gaps " + g01 + " " + g12 + " " + g23 + " " + g34 + " (a group gap must be clearly wider)";
            var xs = icons.Select(b => Mathf.Round(((RectTransform)b.transform).anchoredPosition.x)).Distinct().OrderBy(x => x).ToList();
            if (xs.Count != 5) return xs.Count + " columns";
            for (int i = 2; i < xs.Count; i++) if (Mathf.Abs((xs[i] - xs[i - 1]) - (xs[1] - xs[0])) > 0.5f) return "uneven columns";
            foreach (var b in icons) { if (!b.gameObject.activeInHierarchy) return b.name + " is hidden"; string r = Reach(b); if (r != null) return r; }
            return null;
        });
        if (captures) yield return Shot("flow_card_more.png");
        // ---- group 1: path
        {
            string res = null;
            int u0 = History.UndoCount;
            InspectorCard.ModeButton(1).Click(); if (a.mode != PathMode.PingPong || History.UndoCount != u0 + 1) res = "back and forth: mode " + a.mode + ", History " + (History.UndoCount - u0);
            u0 = History.UndoCount;
            InspectorCard.ModeButton(2).Click(); if (res == null && (a.mode != PathMode.Once || History.UndoCount != u0 + 1)) res = "once: mode " + a.mode;
            u0 = History.UndoCount;
            InspectorCard.ModeButton(0).Click(); if (res == null && (a.mode != PathMode.Loop || History.UndoCount != u0 + 1)) res = "loop: mode " + a.mode;
            var first = a.nodes[0]; var last = a.nodes[a.nodes.Count - 1]; u0 = History.UndoCount;
            InspectorCard.ReverseButton.Click();
            if (res == null && (a.nodes[0] != last || a.nodes[a.nodes.Count - 1] != first || History.UndoCount != u0 + 1)) res = "reverse: first tile (" + a.nodes[0].gridX + "," + a.nodes[0].gridZ + "), History " + (History.UndoCount - u0);
            InspectorCard.ReverseButton.Click();
            u0 = History.UndoCount;
            InspectorCard.PencilButton.Click();
            bool armed = grid.PencilArmed;
            grid.SimDownCell(cols - 1, rows - 1); grid.SimUp();
            if (res == null && (!armed || grid.PencilArmed || a.nodes.Count != 1 || a.nodes[0] != a.Island.GetTile(cols - 1, rows - 1) || History.UndoCount != u0 + 1))
                res = "redraw: armed " + armed + ", nodes " + a.nodes.Count + ", History " + (History.UndoCount - u0);
            Run(sb, "b. path group: the three modes (loop · back and forth · once), reverse ⇄ and redraw (pencil: the next press restarts the path) each do their job, one History entry each", () => res);
            History.Undo(); yield return Settle(IdA);
            a = ById(IdA); strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            if (!InspectorCard.DrawerOpen) { InspectorCard.MoreTab.Click(); yield return WaitFor(() => InspectorCard.DrawerShown >= 1f, 2f); }
        }
        // ---- group 3: devices
        {
            string res = null;
            int u0 = History.UndoCount, f0 = a.follow;
            InspectorCard.DrawerButton("shadow").Click(); if (a.follow != (f0 + 1) % 4 || History.UndoCount != u0 + 1) res = "shadow: follow " + a.follow;
            u0 = History.UndoCount; int e0 = a.echo;
            InspectorCard.DrawerButton("echo").Click(); if (res == null && (a.echo != (e0 + 1) % 3 || History.UndoCount != u0 + 1)) res = "echo: " + a.echo;
            u0 = History.UndoCount; bool sh0 = a.shimmer;
            InspectorCard.DrawerButton("echo").SimHold(); if (res == null && (a.shimmer == sh0 || History.UndoCount != u0 + 1)) res = "echo hold (shimmer): " + a.shimmer;
            u0 = History.UndoCount;
            InspectorCard.DrawerButton("up").Click(); if (res == null && (a.octave != 1 || History.UndoCount != u0 + 1)) res = "octave ↑: " + a.octave;
            u0 = History.UndoCount;
            InspectorCard.DrawerButton("down").Click(); if (res == null && (a.octave != 0 || History.UndoCount != u0 + 1)) res = "octave ↓: " + a.octave;
            for (int k = 0; k < 3 && res == null; k++)
            {
                int want = (k + 2) % 3;   // long, short, normal (it starts at normal)
                u0 = History.UndoCount;
                InspectorCard.DrawerButton("gate" + want).Click();
                if ((int)a.gate != want || History.UndoCount != u0 + 1) res = "gate " + want + ": " + a.gate + ", History " + (History.UndoCount - u0);
            }
            u0 = History.UndoCount; int cubes0 = SequenceMaster.Cubes.Count;
            InspectorCard.DrawerButton("twin").Click();
            if (res == null && (SequenceMaster.Cubes.Count != cubes0 + 1 || !SequenceMaster.Cubes.Any(c => c != null && c.twinOf == a.id) || History.UndoCount != u0 + 1)) res = "twin ×2: cubes +" + (SequenceMaster.Cubes.Count - cubes0);
            InspectorCard.DrawerButton("twin").SimHold();
            bool variants = InspectorCard.PopoverOpen;
            CardKey(KeyCode.Escape);
            if (res == null && (!variants || InspectorCard.PopoverOpen || !InspectorCard.DrawerOpen)) res = "twin hold: variants " + variants + ", Esc left the popover " + InspectorCard.PopoverOpen + " / the drawer " + InspectorCard.DrawerOpen;
            u0 = History.UndoCount; cubes0 = SequenceMaster.Cubes.Count;
            InspectorCard.DrawerButton("stamp").Click();
            if (res == null && (SequenceMaster.Cubes.Count != cubes0 + 1 || History.UndoCount != u0 + 1)) res = "stamp: cubes +" + (SequenceMaster.Cubes.Count - cubes0) + ", History " + (History.UndoCount - u0);
            Run(sb, "b. devices group: twin ×2 (hold: its variants), shadow, echo (hold: shimmer), stamp, octave ↑ ↓, gate short · normal · long each do their job, one History entry each", () => res);
        }
        // ---- group 2: copies
        {
            string res = null;
            int c0 = InspectorCard.CopyCount, u0 = History.UndoCount;
            InspectorCard.CopyButton.Click();
            if (InspectorCard.CopyCount != c0 + 1 || !Clipboard.HasPattern || History.UndoCount != u0) res = "copy: count +" + (InspectorCard.CopyCount - c0) + ", clipboard " + Clipboard.HasPattern;
            int i0 = InspectorCard.IdeaCount; u0 = History.UndoCount;
            InspectorCard.FlipButton.Click();
            if (res == null && (InspectorCard.IdeaCount != i0 + 1 || History.UndoCount != u0 + 1)) res = "flip: ideas +" + (InspectorCard.IdeaCount - i0) + ", History " + (History.UndoCount - u0) + (CubeOps.FlipRefusal(a) != null ? " (refused: " + CubeOps.FlipRefusal(a) + ")" : "");
            InspectorCard.FlipButton.Click();
            u0 = History.UndoCount;
            bool canClimb = Climb.CanClimb(a);
            InspectorCard.ClimbButton.Click();
            if (res == null && canClimb && (a.climb != 1 || History.UndoCount != u0 + 1)) res = "climb: level " + a.climb + ", History " + (History.UndoCount - u0);
            if (res == null && !canClimb) res = "climb cannot be tested on this cube (Climb.CanClimb false)";
            for (int dir = 1; dir >= -1 && res == null; dir -= 2)
            {
                u0 = History.UndoCount; i0 = InspectorCard.IdeaCount; int cubes0 = SequenceMaster.Cubes.Count;
                InspectorCard.OctaveCopyButton(dir).Click();
                var made = InspectorCard.LastIdea;
                if (SequenceMaster.Cubes.Count != cubes0 + 1 || made == null || made.echoOf != a.id || made.layer != a.layer + dir || History.UndoCount != u0 + 1 || InspectorCard.IdeaCount != i0 + 1)
                    res = "octave copy " + (dir > 0 ? "↑" : "↓") + ": cubes +" + (SequenceMaster.Cubes.Count - cubes0) + ", History " + (History.UndoCount - u0) + ", deny " + CubeOps.LastDeny;
            }
            for (int k = 0; k < 2 && res == null; k++)
            {
                bool above = k == 0;
                u0 = History.UndoCount; i0 = InspectorCard.IdeaCount; int cubes0 = SequenceMaster.Cubes.Count;
                InspectorCard.HarmonyButton(above).Click();
                if (SequenceMaster.Cubes.Count != cubes0 + 1 || History.UndoCount != u0 + 1 || InspectorCard.IdeaCount != i0 + 1)
                    res = "harmony " + (above ? "above" : "below") + ": cubes +" + (SequenceMaster.Cubes.Count - cubes0) + ", History " + (History.UndoCount - u0) + ", refusal " + CubeOps.HarmonyRefusal(a);
            }
            Run(sb, "b. copies group: copy, flip, climb, octave copy ↑ ↓, harmony ↑ ↓ each do their job (one History entry each; copy none)", () => res);
            // paste → next grid closes the card, then the pattern appears on the next grid
            u0 = History.UndoCount; int p0 = InspectorCard.PasteNextCount;
            bool can = InspectorCard.PasteNextButton.Interactable;
            InspectorCard.PasteNextButton.Click();
            yield return WaitFor(() => InspectorCard.PasteNextCount > p0 && !InspectorCard.PastePending, 4f);
            yield return Frames(3);
            Run(sb, "b. copies group: paste → next grid closes the card and the pattern appears on the next grid (one History entry)", () =>
            {
                var made = InspectorCard.LastPasteNext;
                if (!can) return "the button is disabled";
                if (InspectorCard.PasteNextCount != p0 + 1 || made == null) return "pastes +" + (InspectorCard.PasteNextCount - p0) + ", made " + (made != null);
                if (made.Island != Grid(1)) return "it landed on " + (made.Island != null ? made.Island.assignedChord : "nothing");
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
        }

        // ================================================================ d. a span cube
        LoadState(TestSong());
        yield return Frames(4);
        var s = ById(IdS);
        yield return OpenCard(s);
        strip = InspectorCard.Strip; grid = InspectorCard.Grid;
        yield return Frames(3);
        Run(sb, "d. a span-1 cube: the strip spans the whole chain (8 beats = the chain's capacity) in two parts with a hairline at the chord change; the focus loop covers both columns", () =>
        {
            if (s == null || s.span != 1 || !s.HasThrough) return "cube S: span " + (s != null ? s.span : -1);
            if (strip.Parts != 2) return "parts " + strip.Parts;
            float cap = PathManager.CapacityOf(s.Island, s.span);
            if (Mathf.Abs(strip.TotalBeats - cap) > 1e-3f || Mathf.Abs(cap - 8f) > 1e-3f) return "strip " + strip.TotalBeats + " beats, the chain " + cap;
            if (Mathf.Abs(strip.VisibleBeats - 8f) > 1e-3f) return "it shows " + strip.VisibleBeats + " beats";
            if (strip.Hairlines.Count != 1 || Mathf.Abs(strip.Hairlines[0] - 4f) > 1e-3f) return "hairlines: " + string.Join(",", strip.Hairlines.Select(h => h.ToString("F2")).ToArray());
            if (strip.PartGrid(0) != Grid(1) || strip.PartGrid(1) != Grid(2)) return "the parts are not grids 1 and 2";
            // one block per note, on the pattern's own time: notes 4 and 5 start on the second part
            for (int i = 0; i < 6; i++)
            {
                if (i >= strip.Events.Count || strip.Events[i].node != i || Mathf.Abs(strip.Events[i].start - i) > 0.02f) return "event " + i + ": node " + (i < strip.Events.Count ? strip.Events[i].node : -1) + " at " + (i < strip.Events.Count ? strip.Events[i].start : -1f);
                if (Mathf.Abs(strip.BlockCentre(i).x - (strip.XOfBeat(i) + strip.XOfBeat(i + 1)) * 0.5f) > 2f) return "block " + i + " is not at beat " + i;
            }
            if (!FocusLoop.Active || FocusLoop.Column != 1 || FocusLoop.LastColumn != 2) return "the focus loop: " + FocusLoop.Active + " columns " + FocusLoop.Column + ".." + FocusLoop.LastColumn;
            return null;
        });
        {
            strip.SelectNode(1); yield return Frames(3);
            var g1 = grid.Island; Color col1 = grid.GetComponent<InkShape>().color; bool v1 = grid.BeadVisible(0) && grid.BeadVisible(3) && !grid.BeadVisible(4) && !grid.BeadVisible(5) && grid.HighlightNode == 1;
            strip.SelectNode(4); yield return Frames(3);
            var g2 = grid.Island; Color col2 = grid.GetComponent<InkShape>().color; bool v2 = !grid.BeadVisible(0) && !grid.BeadVisible(3) && grid.BeadVisible(4) && grid.BeadVisible(5) && grid.HighlightNode == 4;
            Run(sb, "d. the flat grid shows the chain grid that holds the selected note — its notes only, in that grid's colour — and switches with the selection", () =>
            {
                if (g1 != Grid(1)) return "note 1 selected: the grid shows " + (g1 != null ? g1.assignedChord : "none");
                if (g2 != Grid(2)) return "note 4 selected: the grid shows " + (g2 != null ? g2.assignedChord : "none");
                if (!v1 || !v2) return "beads: on grid 1 " + v1 + ", on grid 2 " + v2;
                if (s.GridOfNode(4) != Grid(2) || s.GridOfNode(1) != Grid(1)) return "the engine places the notes elsewhere";
                Color want1 = Comic.Opaque(Palette.Mul(Color.Lerp(new Color(0.2f, 0.17f, 0.32f), Grid(1).chordColor, 0.55f), 0.7f)), want2 = Comic.Opaque(Palette.Mul(Color.Lerp(new Color(0.2f, 0.17f, 0.32f), Grid(2).chordColor, 0.55f), 0.7f));
                if (((Vector4)(col1 - want1)).magnitude > 0.02f || ((Vector4)(col2 - want2)).magnitude > 0.02f) return "the plate is not in the shown grid's colour";
                if (((Vector4)(col1 - col2)).magnitude < 0.02f) return "both grids have the same colour here (the check cannot tell)";
                return null;
            });
            if (captures) yield return Shot("flow_card_span.png");
            // no stepper: a click on the strip's part for another grid (its empty time: the ruler band over that part) switches the grid
            strip.Deselect(); yield return Frames(2);
            Vector2 over0 = new Vector2(strip.XOfBeat(1.5f), RhythmStrip.Height * 0.5f - 6f), over1 = new Vector2(strip.XOfBeat(6.5f), RhythmStrip.Height * 0.5f - 6f);
            strip.SimDown(over0); strip.SimUp(over0); yield return Frames(3);
            var p0 = grid.Island; int vp0 = strip.ViewPart;
            strip.SimDown(over1); strip.SimUp(over1); yield return Frames(3);
            var p1 = grid.Island; int vp1 = strip.ViewPart;
            Run(sb, "d. no stepper control: a click on the strip's part for another grid switches the flat grid (and selects nothing)", () =>
            {
                if (strip.SelectedNote >= 0) return "a note got selected";
                if (p0 != Grid(1) || vp0 != 0) return "a click over part 0: the grid shows " + (p0 != null ? p0.assignedChord : "none") + " (part " + vp0 + ")";
                if (p1 != Grid(2) || vp1 != 1) return "a click over part 1: the grid shows " + (p1 != null ? p1.assignedChord : "none") + " (part " + vp1 + ")";
                foreach (var st in InspectorCard.Root.GetComponentsInChildren<InkButton>(false)) if (st.name.Contains("Step") || st.name.Contains("Prev") || st.name.Contains("Next") && st.name != "PasteNext") return "a stepper control: " + st.name;
                return null;
            });
        }
        // ---------------------------------------------------------------- an insert that overflows the chain raises the span
        {
            pm.SetBrush(24); yield return Frames(2);
            strip.SelectNode(5); yield return Frames(3);
            int rr = grid.Island.rows, cc = grid.Island.cols;
            int u0 = History.UndoCount, rs0 = RhythmStrip.RoomSpans;
            grid.SimDownCell(cc - 1, rr - 1); grid.SimUp(); yield return Frames(3);   // 7 beats
            grid.SimDownCell(cc - 2, rr - 1); grid.SimUp(); yield return Frames(3);   // 8 beats: the chain is full
            int spanFull = s.span, uFull = History.UndoCount - u0;
            grid.SimDownCell(cc - 3, rr - 1); grid.SimUp(); yield return Frames(4);   // 9 beats: the melody walks on onto grid 3
            Run(sb, "d. an insert that overflows the chain raises the span (1 → 2): the new note starts on the next grid of the lane, the strip gains a part, the flat grid and the focus loop follow; one History entry per insert, no refusal", () =>
            {
                if (spanFull != 1 || uFull != 2) return "filling the chain: span " + spanFull + ", History entries " + uFull;
                if (s.nodes.Count != 9 || Total(s) != 9 * 24) return "nodes " + s.nodes.Count + ", " + Total(s) + " ticks";
                if (s.span != 2 || RhythmStrip.RoomSpans != rs0 + 1) return "span " + s.span + ", RoomSpans +" + (RhythmStrip.RoomSpans - rs0);
                if (History.UndoCount != u0 + 3) return "History entries " + (History.UndoCount - u0);
                if (grid.CapacityChipShown) return "the capacity chip shows";
                if (s.GridOfNode(8) != Grid(3)) return "note 8 is realised on " + (s.GridOfNode(8) != null ? s.GridOfNode(8).assignedChord : "none");
                if (strip.Parts != 3 || Mathf.Abs(strip.TotalBeats - 12f) > 1e-3f || strip.Hairlines.Count != 2) return "strip: parts " + strip.Parts + ", " + strip.TotalBeats + " beats, hairlines " + strip.Hairlines.Count;
                if (strip.SelectedNote != 8 || grid.Island != Grid(3) || !grid.BeadVisible(8)) return "the selection / the flat grid did not follow: selected " + strip.SelectedNote + ", grid " + (grid.Island != null ? grid.Island.assignedChord : "none");
                if (FocusLoop.Column != 1 || FocusLoop.LastColumn != 3) return "the focus loop covers columns " + FocusLoop.Column + ".." + FocusLoop.LastColumn;
                return null;
            });
            History.Undo(); yield return Settle(IdS);
            s = ById(IdS); strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            Run(sb, "d. one undo takes the overflowing insert back together with the span it raised (9 → 8 notes, span 2 → 1)", () => s != null && s.nodes.Count == 8 && s.span == 1 ? null : (s == null ? "cube lost" : s.nodes.Count + " notes, span " + s.span));
        }
        // ---------------------------------------------------------------- a stretch makes room too; Flow off keeps the chip
        LoadState(TestSong());
        yield return Frames(4);
        a = ById(IdA);
        yield return OpenCard(a);
        strip = InspectorCard.Strip; grid = InspectorCard.Grid;
        {
            int u0 = History.UndoCount, cr0 = RhythmStrip.CapacityRefusals;
            bool st = strip.SimStretch(0, 96);   // the first quarter → a whole note: 3 + 3 = 6 beats in a 4-beat bar
            yield return Frames(3);
            Run(sb, "d. Flow: a stretch (and so a longer size) that outgrows the bar raises the span instead of stopping at the capacity chip; one History entry", () =>
            {
                if (!st || a.durs[0] != 96) return "the stretch: " + st + ", note 0 = " + a.durs[0] + " ticks";
                if (a.span != 1) return "span " + a.span;
                if (RhythmStrip.CapacityRefusals != cr0 || grid.CapacityChipShown) return "refused / chip";
                if (History.UndoCount != u0 + 1) return "History entries " + (History.UndoCount - u0);
                return null;
            });
            History.Undo(); yield return Settle(IdA);
            a = ById(IdA); strip = InspectorCard.Strip; grid = InspectorCard.Grid;
            PathManager.Flow = false;
            u0 = History.UndoCount; cr0 = RhythmStrip.CapacityRefusals;
            strip.SimStretch(0, 96);
            yield return Frames(3);
            int gr0 = grid.CapacityRefusals; int n0 = a.nodes.Count;
            pm.SetBrush(48);
            grid.SimDownCell(a.Island.cols - 1, a.Island.rows - 1); grid.SimUp();   // 4 beats used + a half note: refused
            yield return Frames(2);
            Run(sb, "d. PathManager.Flow off: the old rule stays — the stretch stops at the longest size that fits (48 ticks: the bar is full), a click past the capacity is refused, the capacity chip shows, the span never grows", () =>
            {
                if (a.span != 0) return "span " + a.span;
                if (a.durs[0] != 48) return "note 0 = " + a.durs[0] + " ticks (want 48: 2 + 1 + ½ + ½ beats)";
                if (RhythmStrip.CapacityRefusals != cr0 + 1) return "strip refusals +" + (RhythmStrip.CapacityRefusals - cr0);
                if (a.nodes.Count != n0 || grid.CapacityRefusals != gr0 + 1) return "the click: nodes " + a.nodes.Count + ", refusals +" + (grid.CapacityRefusals - gr0);
                if (!grid.CapacityChipShown) return "no capacity chip";
                return null;
            });
            PathManager.Flow = true; pm.SetBrush(24);
        }

        // ================================================================ f. done
        CubeInspector.CloseImmediate();
        yield return Frames(3);
        Run(sb, "f. no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3).ToArray()));
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
