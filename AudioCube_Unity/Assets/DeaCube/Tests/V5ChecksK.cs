using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package K (v5: free island drag, the repeat conveyor belt, the song key) Play-mode verification, SPEC v5 §2.1 / §2.5 / §2.6 / §5:
/// <c>V5ChecksK.RunAll()</c> starts a coroutine; poll <c>V5ChecksK.Done</c> or Captures/k5_report.txt (numbered PASS/FAIL lines, INFO lines
/// with numbers). Covers SongManager.MoveIslandToColumn (join, new column in front, anchor promotion, an emptied column closing, bars equalised,
/// cubes / register kept, a full column refused, a merge group moving as a block, the lit column keeping its local beat while playing), the free
/// drag (JOIN / NEW column / own column / MERGE slots with their previews, one History entry per drop, Esc restoring exactly), repeat = passes
/// (ColumnLength, windows per pass, the next column making space, the belt, the ride sampled mid-pass on the beat, stopped = home, the jerk, the
/// glide back, all islands ×2 = the column's notes twice (logged), undo), the key never changing and the home glyph. Captures k5_*.png.
/// Never writes the user's save; restores the instrument mutes and the fixture at the end.
/// </summary>
public static class V5ChecksK
{
    public static string Report = "";
    public static bool Done;
    static int num;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "k5_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" K5-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, sb.ToString() + (Done ? "" : "...running\n")); } catch (Exception) { } }

    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); Line(sb, d == null, name, d ?? "ok"); }
        catch (Exception e) { Line(sb, false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }

    static string FirstFrame(Exception e)
    {
        var s = e.StackTrace ?? "";
        int nl = s.IndexOf('\n');
        return (nl > 0 ? s.Substring(0, nl) : s).Trim();
    }

    static SongManager SM => SongManager.I;
    static float W => KeyBlock.IslandWidth;
    static float Pitch => KeyBlock.SlotPitch;

    public static string RunAll()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(AllRoutine());
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
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

    static void Frame(Bounds b, float tight) { if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(b, 0.1f, true, tight); }

    static CubeState CubeOn(int island, int instrument, int[] xs, int[] zs, int step, int hits = -1)
    {
        return new CubeState { instrument = instrument, measure = island, moon = -1, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = 1, mode = 0, volume = 1f, hits = hits, twinOf = -1 };
    }

    static string Chords() { var s = new StringBuilder(); foreach (var kb in SM.Islands) s.Append(kb.assignedChord).Append('@').Append(kb.column).Append(' '); return s.ToString().Trim(); }
    static int IndexOfChordReg(string chord, int column) { for (int i = 0; i < SM.Islands.Count; i++) if (SM.Islands[i].assignedChord == chord && (column < 0 || SM.Islands[i].column == column)) return i; return -1; }

    static float MinClearance(int col)
    {
        var l = SM.ColumnIslands(col).OrderBy(k => k.FrontEdge).ToList();
        float m = float.MaxValue;
        for (int k = 0; k + 1 < l.Count; k++) m = Mathf.Min(m, l[k + 1].FrontEdge - l[k].BackEdge);
        return m;
    }

    /// <summary>The column invariants of v4 plus v5's belts: column-major and contiguous, anchors = first of each column (hub on anchors only), a
    /// shared x per column, LaneGap clearance, the x pitch = the column's width + its longest belt + (v7: touching — one section or glued — ? 0 :
    /// the short section's ghost measures + SectionGap), shared starts.</summary>
    static string Invariants()
    {
        var sm = SM;
        int prev = -1;
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb.column < prev || kb.column > prev + 1) return "not column-major / contiguous at " + i;
            bool first = kb.column != prev;
            if (kb.IsAnchor != first) return "anchor flag at " + i;
            if (kb.Hub != null && kb.Hub.gameObject.activeSelf != first) return "hub shown on a non-anchor at " + i;
            prev = kb.column;
        }
        if (prev + 1 != sm.ColumnCount) return "ColumnCount " + sm.ColumnCount + " vs " + (prev + 1);
        for (int c = 0; c < sm.ColumnCount; c++)
        {
            var l = sm.ColumnIslands(c);
            foreach (var kb in l) if (Mathf.Abs(kb.px - l[0].px) > 1e-3f) return "column " + c + " x differs";
            if (l.Count > ProjectConfig.MaxLanes) return "column " + c + " holds " + l.Count;
            if (l.Count > 1 && MinClearance(c) < ProjectConfig.LaneGap - 0.01f) return "column " + c + " clearance " + MinClearance(c).ToString("F2");
            if (c + 1 < sm.ColumnCount)
            {
                float pitch = sm.ColumnX(c + 1) - sm.ColumnX(c), want = sm.ColumnWidth(c) + sm.BeltExtent(c) + (sm.Touching(c) ? 0f : ProjectConfig.SectionGap + Mathf.Max(0, ProjectConfig.SectionBars - sm.SectionBars(sm.SectionOf(c))) * W);   // v7 §13.2
                if (Mathf.Abs(pitch - want) > 0.01f) return "pitch " + c + "->" + (c + 1) + " = " + pitch.ToString("F2") + " want " + want.ToString("F2");
            }
            foreach (var kb in l) if (Mathf.Abs(sm.MeasureStarts[sm.Islands.IndexOf(kb)] - sm.ColumnStart(c)) > 1e-4f) return "start of an island in column " + c;
            int passes = 1; foreach (var kb in l) passes = Mathf.Max(passes, kb.Passes);
            if (sm.ColumnPasses(c) != passes || Mathf.Abs(sm.ColumnLength(c) - sm.PassLength(c) * passes) > 1e-4f) return "passes of column " + c;
        }
        return null;
    }

    /// <summary>Resident cubes of island <paramref name="kb"/> (not riders, not Moon cubes).</summary>
    static List<AudioCube> CubesOf(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && !c.rider && !c.IsOnMoon && c.Island == kb).ToList();
    static string CubePrint(KeyBlock kb)
    {
        var sb = new StringBuilder();
        foreach (var c in CubesOf(kb).OrderBy(c => c.id)) { sb.Append(c.instrument).Append(':'); foreach (var n in c.nodes) sb.Append(n.gridX).Append(n.gridZ).Append(','); sb.Append(' '); }
        return sb.ToString();
    }

    /// <summary>The test song: five columns (1, 2, 1, 1, 1 islands) at 120 BPM, one resident cube (8ths) on every island.</summary>
    static void BuildSong()
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        if (FocusLoop.Active) FocusLoop.Dismiss();
        var song = MusicTheory.RandomSong(5505, "k5 song");
        SM.StartFreshSong(song);
        var cards = new List<SongManager.MeasureData>();
        for (int i = 1; cards.Count < 4; i++) cards.Add(song.measures[i % song.measures.Length]);
        SM.PlaceAll(cards);
        SM.AddIslandInColumn(SM.ColumnFirst(1), true);
        Performance.SetNominalBpm(120f);
        GlobalClock.LoopSong = true;
        var pm = PathManager.I;
        int[] inst = { 0, 1, 5, 8, 3, 6, 0 };
        for (int i = 0; i < SM.Islands.Count; i++)
        {
            int rr = Mathf.Max(1, SM.Islands[i].rows);
            pm.RestoreCube(CubeOn(i, inst[i % inst.Length], new[] { i % 3, (i % 3) + 1, (i % 3) + 2 }, new[] { 0, Mathf.Min(1, rr - 1), Mathf.Min(2, rr - 1) }, (int)StepLen.Eighth, 5));
        }
        SM.RecomputeMeasureStarts();
        History.Reset(); History.Push();
    }

    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        Prepare();
        yield return null;
        var mutedBefore = (bool[])Instruments.Muted.Clone();
        string userMd5 = Md5(SongIO.Path);
        var drag = PathManager.I != null ? PathManager.I.Drag : null;

        // ================================================================ A. MoveIslandToColumn (the API behind the free drag)
        BuildSong();
        yield return null;
        var key0 = SM.SongKey;
        Info(sb, "song: " + Chords() + " | key tonic " + key0.tonic + (key0.minor ? "m" : "") + " | slot pitch " + Pitch.ToString("F2"));
        Run(sb, "the test song: 5 columns (1, 2, 1, 1, 1), invariants", () =>
        {
            if (SM.ColumnCount != 5 || SM.ColumnSize(1) != 2) return "columns " + SM.ColumnCount + " size1 " + SM.ColumnSize(1);
            return Invariants();
        });
        Run(sb, "MoveIslandToColumn JOIN: column 3's island joins column 1 in its anchor's lane -> after the anchor (anchor kept), the column's others make room, its OWN length kept (1 measure on the column's first measure: v8, the user — grids are not forced to the column's length), register + cubes kept, column 3 disappears (the later column slides into its x), one History entry, the key unchanged", () =>
        {
            SM.SetBars(SM.ColumnFirst(1), 2);
            int i3 = SM.ColumnFirst(3);
            SM.SetRegister(i3, 1);
            History.Reset(); History.Push();
            var k3 = SM.Islands[i3]; string chord = k3.assignedChord; string cubes = CubePrint(k3); int nCubes = CubesOf(k3).Count;
            string anchor1 = SM.AnchorOf(1).assignedChord, chord4 = SM.AnchorOf(4).assignedChord;
            float x3 = SM.ColumnX(3), z = SM.AnchorOf(1).pz; int h = History.UndoCount;
            int r = SM.MoveIslandToColumn(i3, 1, false, z);
            if (r < 0) return "refused";
            var kb = SM.Islands[r];
            if (kb.assignedChord != chord || kb.column != 1 || kb.IsAnchor) return "moved island: " + kb.assignedChord + " column " + kb.column + " anchor " + kb.IsAnchor;
            if (SM.AnchorOf(1).assignedChord != anchor1) return "anchor changed: " + SM.AnchorOf(1).assignedChord;
            // v7 §13.2: the later column takes the closed column's place in the layout — right after column 2 (touching inside its section, else
            // after the gap and a short section's ghost measures; the old column 3 was section 0's last, the later one leads section 1)
            float want3 = SM.ColumnX(2) + SM.ColumnWidth(2) + SM.BeltExtent(2) + (SM.Touching(2) ? 0f : ProjectConfig.SectionGap + Mathf.Max(0, ProjectConfig.SectionBars - SM.SectionBars(SM.SectionOf(2))) * W);
            if (SM.ColumnCount != 4 || SM.AnchorOf(3).assignedChord != chord4 || Mathf.Abs(SM.ColumnX(3) - want3) > 0.01f) return "column 3 not closed: " + Chords() + " x " + SM.ColumnX(3).ToString("F2") + " want " + want3.ToString("F2") + " (was " + x3.ToString("F2") + ")";
            if (kb.bars != 1 || kb.barOffset != 0 || SM.ColumnBars(1) != 2 || SM.ColumnSize(1) != 3) return "bars " + kb.bars + " offset " + kb.barOffset + " column bars " + SM.ColumnBars(1) + " size " + SM.ColumnSize(1);
            if (kb.register != 1 || CubesOf(kb).Count != nCubes || CubePrint(kb) != cubes) return "register " + kb.register + " cubes " + CubePrint(kb) + " vs " + cubes;
            if (Mathf.Abs(kb.pz - Mathf.Round(z * 2f) * 0.5f) > 0.01f) return "joined z " + kb.pz + " want " + z;
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (SM.SongKey.tonic != key0.tonic || SM.SongKey.minor != key0.minor) return "the key changed";
            return Invariants();
        });
        if (drag != null) { var bb = SM.ColumnBounds(1); bb.Encapsulate(SM.ColumnBounds(2)); Frame(bb, 1.1f); }
        yield return Wait(0.45f);
        yield return Shot("k5_drop_join.png");
        Run(sb, "undo of the join restores the 5 columns (chords, columns, registers)", () =>
        {
            History.Undo();
            if (SM.ColumnCount != 5 || SM.ColumnSize(1) != 2) return "after undo: " + Chords();
            if (SM.AnchorOf(3).register != 1) return "register " + SM.AnchorOf(3).register;
            return Invariants();
        });
        Run(sb, "MoveIslandToColumn NEW column in front: column 1's upper island -> a new column 0 (its anchor), the old first column keeps its x, column 1 keeps its anchor, one History entry", () =>
        {
            var up = SM.ColumnIslands(1)[1]; string chord = up.assignedChord; int i = SM.Islands.IndexOf(up);
            float x0 = SM.ColumnX(0); string c0 = SM.AnchorOf(0).assignedChord, a1 = SM.AnchorOf(1).assignedChord; int h = History.UndoCount;
            int r = SM.MoveIslandToColumn(i, 0, true, up.pz);
            if (r < 0) return "refused";
            if (SM.ColumnCount != 6 || SM.AnchorOf(0).assignedChord != chord || SM.ColumnSize(0) != 1 || r != 0) return "front: " + Chords();
            if (SM.AnchorOf(1).assignedChord != c0 || Mathf.Abs(SM.ColumnX(1) - x0) > 0.01f) return "the old first column moved: x " + SM.ColumnX(1).ToString("F2") + " want " + x0.ToString("F2");
            if (SM.AnchorOf(2).assignedChord != a1 || SM.ColumnSize(2) != 1) return "column 2: " + Chords();
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo();
            if (SM.ColumnCount != 5 || SM.ColumnSize(1) != 2) return "undo: " + Chords();
            return null;
        });
        Run(sb, "MoveIslandToColumn promotes the next island to anchor: column 1's ANCHOR -> a new column at the end; column 1's other island is its anchor (hub on)", () =>
        {
            var a = SM.AnchorOf(1); var other = SM.ColumnIslands(1)[1]; string oc = other.assignedChord + other.pz.ToString("F1"); int i = SM.Islands.IndexOf(a);
            int h = History.UndoCount;
            int r = SM.MoveIslandToColumn(i, SM.ColumnCount, true, a.pz);
            if (r < 0) return "refused";
            var na = SM.AnchorOf(1);
            if (na.assignedChord + na.pz.ToString("F1") != oc || !na.IsAnchor || na.Hub == null || !na.Hub.gameObject.activeSelf) return "promoted: " + na.assignedChord;
            if (SM.Islands[r].column != SM.ColumnCount - 1 || !SM.Islands[r].IsAnchor) return "not at the end: " + Chords();
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo();
            return SM.ColumnCount == 5 && SM.ColumnSize(1) == 2 ? null : "undo: " + Chords();
        });
        Run(sb, "MoveIslandToColumn refuses a full column (MaxLanes): -1, nothing moves, no History", () =>
        {
            while (SM.ColumnSize(1) < ProjectConfig.MaxLanes) SM.AddIslandInColumn(SM.ColumnFirst(1), true);
            string before = Chords(); int h = History.UndoCount;
            int r = SM.MoveIslandToColumn(SM.ColumnFirst(0), 1, false, SM.AnchorOf(1).pz);
            if (r != -1 || Chords() != before || History.UndoCount != h) return "r " + r + " history +" + (History.UndoCount - h);
            while (SM.ColumnSize(1) > 2) History.Undo();
            return SM.ColumnSize(1) == 2 ? null : "undo " + SM.ColumnSize(1);
        });
        Run(sb, "a merge group moves as a block: columns 3 + 4 merged, moved to a new column 0 -> two glued columns at the front, one group, one History entry", () =>
        {
            if (!SM.MergeIslands(SM.AnchorOf(4), SM.AnchorOf(3), true)) return "merge refused";
            var g = SM.GroupOf(SM.AnchorOf(3)); if (g.Count != 2) return "group " + g.Count;
            string c0 = g[0].assignedChord, c1 = g[1].assignedChord; int h = History.UndoCount;
            int r = SM.MoveIslandToColumn(SM.Islands.IndexOf(g[1]), 0, true, g[0].pz);
            if (r < 0) return "refused";
            if (SM.AnchorOf(0).assignedChord != c0 || SM.AnchorOf(1).assignedChord != c1 || !SM.Glued(0) || SM.AnchorOf(0).group == 0 || SM.AnchorOf(0).group != SM.AnchorOf(1).group) return "block: " + Chords() + " | " + SM.GroupReport();
            if (SM.ColumnCount != 5) return "columns " + SM.ColumnCount;
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo(); History.Undo();
            return SM.ColumnCount == 5 && !SM.Glued(3) ? null : "undo: " + SM.GroupReport();
        });
        // while playing: the lit column keeps its local beat
        {
            Instruments.SetMuted(9, true);
            int late0 = Synth.LateEvents, err0 = Synth.Errors, ll0 = Synth.LateLogCount;   // (the play start inside a note is a catch-up too)
            GlobalClock.Seek(SM.ColumnStart(2) + 1.25f);
            GlobalClock.Play();
            yield return Wait(0.6f);
            string litChord = SM.AnchorOf(SM.LitColumn).assignedChord;
            double local0 = GlobalClock.BeatNow - SM.ColumnStart(SM.LitColumn);
            int r = SM.MoveIslandToColumn(SM.ColumnFirst(0), SM.ColumnCount, true, SM.AnchorOf(0).pz);   // column 0 leaves: every start shifts
            double local1 = GlobalClock.BeatNow - SM.ColumnStart(SM.LitColumn);
            string litChord1 = SM.AnchorOf(SM.LitColumn).assignedChord;
            yield return Wait(0.8f);
            GlobalClock.Stop();
            int late = Synth.LateEvents - late0, err = Synth.Errors - err0;
            // v6 (integration): a rebuild while playing may fire the note under the playhead a few ms late (the v5 seek catch-up class, ≤ 30 ms, a
            // note or two in a long session): those are counted apart; anything later, or more of them, fails
            string lateLog = Synth.LateLog(ll0);
            int catchUp = 0; bool worse = false;
            foreach (var ln in lateLog.Split(';'))   // entries: "dsp … owner … kind … slot … key …: 16.8 ms late; "
            {
                int k = ln.IndexOf(" ms late"); if (k < 0) continue;
                int b = ln.LastIndexOf(' ', k - 1); float ms;
                if (b >= 0 && float.TryParse(ln.Substring(b + 1, k - b - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ms) && ms <= 30f) catchUp++; else worse = true;
            }
            bool lateOk = late == 0 || (!worse && catchUp == late && late <= 2);
            Line(sb, r >= 0 && litChord1 == litChord && Math.Abs(local1 - local0) < 0.15 && lateOk && err == 0,
                "while playing a move keeps the lit column's local beat (column 0 moved to the end: every start shifts), Synth late / errors 0",
                "lit " + litChord + " local " + local0.ToString("F3") + " -> " + litChord1 + " " + local1.ToString("F3") + ", late +" + late + (catchUp > 0 ? " (catch-up ≤ 30 ms: " + lateLog.Trim() + ")" : "") + " errors +" + err);
            History.Undo();
        }

        // ================================================================ B. the free drag (SimBegin / SimMove / SimRelease)
        if (drag == null) Line(sb, false, "free drag", "no IslandDrag");
        else
        {
            BuildSong();
            yield return null;
            var bAll = SM.SongBounds; Frame(bAll, 0.95f);
            yield return Wait(0.3f);
            // B1 JOIN: column 3's island over column 1, in its anchor's lane
            var k3 = SM.AnchorOf(3); string c3 = k3.assignedChord;
            var a1 = SM.AnchorOf(1);
            drag.SimBegin(k3, false, k3.Center);
            bool free = drag.DragKind == IslandDrag.Kind.InColumn && drag.Slot == IslandDrag.DropSlot.Own;
            drag.SimMove(new Vector3(a1.Center.x + 0.3f, 0f, a1.Center.z + 0.6f));
            yield return Wait(0.35f);
            drag.SimMove(new Vector3(a1.Center.x + 0.3f, 0f, a1.Center.z + 0.6f));
            var slot = drag.Slot; int sc = drag.SlotColumn; int pv = drag.PreviewCount; int rc = ColumnBands.RangeColumn;
            float room = 0f; foreach (var o in SM.ColumnIslands(1)) room = Mathf.Max(room, Mathf.Abs(o.PreviewOffset.z));
            bool followed = Mathf.Abs(k3.Center.x - (a1.Center.x + 0.3f)) < 0.6f && k3.Lifted;
            bool shadow = ColumnBands.ShadowShown;
            yield return Shot("k5_drag_join.png");
            int h = History.UndoCount;
            drag.SimRelease();
            yield return null;
            int ki = IndexOfChordReg(c3, 1);
            Line(sb, free && slot == IslandDrag.DropSlot.Join && sc == 1 && rc == 1 && pv >= 1 && room > 0.5f && followed && shadow && ki >= 0 && SM.ColumnCount == 4 && History.UndoCount == h + 1 && Invariants() == null,
                "free drag JOIN: the island follows the pointer in x and z (lifted, a shadow on the sea), over column 1 the slot is JOIN 1 (its band lit, its islands make room), the release joins it (column 3 closes), one History entry",
                "slot " + slot + " " + sc + ", band " + rc + ", previews " + pv + " (max room " + room.ToString("F2") + " u), followed " + followed + ", shadow " + shadow + ", joined at " + ki + ", columns " + SM.ColumnCount + ", history +" + (History.UndoCount - h) + " " + (Invariants() ?? ""));
            yield return Wait(0.4f);
            History.Undo();
            yield return null;
            // B2 NEW column: column 3's island into the gap between columns 0 and 1
            k3 = SM.AnchorOf(3);
            float e = KeyBlock.EdgeInset;
            float gapX = (SM.ColumnX(0) - e + W + SM.ColumnX(1) - e) * 0.5f;
            drag.SimBegin(k3, false, k3.Center);
            drag.SimMove(new Vector3(gapX, 0f, SM.AnchorOf(0).Center.z));
            yield return Wait(0.4f);
            drag.SimMove(new Vector3(gapX, 0f, SM.AnchorOf(0).Center.z));
            slot = drag.Slot; sc = drag.SlotColumn; pv = drag.PreviewCount;
            bool slit = ColumnBands.SlitShown; float slitX = ColumnBands.SlitX;
            float partL = SM.AnchorOf(0).PreviewOffset.x, partR = SM.AnchorOf(1).PreviewOffset.x;
            var bb2 = SM.ColumnBounds(0); bb2.Encapsulate(SM.ColumnBounds(1)); bb2.Encapsulate(k3.WorldBounds); Frame(bb2, 1.05f);
            yield return Wait(0.15f);
            yield return Shot("k5_drag_slit.png");
            h = History.UndoCount;
            drag.SimRelease();
            yield return null;
            Line(sb, slot == IslandDrag.DropSlot.NewColumn && sc == 1 && slit && Mathf.Abs(slitX - gapX) < 0.3f && pv >= 2 && partL < -1f && partR > 1f
                && SM.AnchorOf(1).assignedChord == c3 && SM.ColumnSize(1) == 1 && SM.ColumnCount == 5 && History.UndoCount == h + 1 && Invariants() == null,
                "free drag NEW column: in the gap between columns 0 and 1 the slot is NEW 1 (the insertion slit at the gap, the two columns part), the release makes it column 1 (column 3 closes), one History entry",
                "slot " + slot + " " + sc + ", slit " + slit + " at " + slitX.ToString("F2") + " (gap " + gapX.ToString("F2") + "), previews " + pv + " (parted " + partL.ToString("F2") + " / " + partR.ToString("F2") + "), column 1 = " + SM.AnchorOf(1).assignedChord + ", history +" + (History.UndoCount - h) + " " + (Invariants() ?? ""));
            yield return Wait(0.4f);
            History.Undo();
            yield return null;
            // B3 own column: the upper island of column 1 moved up in its column
            var up = SM.ColumnIslands(1)[1]; float upz = up.pz, upx = up.px;
            drag.SimBegin(up, false, up.Center);
            drag.SimMove(up.Center + new Vector3(1.2f, 0f, 2f));
            var slot3 = drag.Slot;
            h = History.UndoCount;
            drag.SimRelease();
            yield return null;
            Line(sb, slot3 == IslandDrag.DropSlot.Own && Mathf.Abs(up.px - upx) < 1e-3f && Mathf.Abs(up.pz - (upz + 2f)) < 0.26f && History.UndoCount == h + 1 && Invariants() == null,
                "free drag over its own column = the in-column move: z moves (snapped), x back in the column, one History entry",
                "slot " + slot3 + ", z " + upz.ToString("F2") + " -> " + up.pz.ToString("F2") + ", x kept " + (Mathf.Abs(up.px - upx) < 1e-3f) + ", history +" + (History.UndoCount - h));
            History.Undo();
            yield return null;
            // B4 cancel restores exactly
            var before = SM.Islands.Select(k => new Vector2(k.px, k.pz)).ToList(); h = History.UndoCount;
            k3 = SM.AnchorOf(3); a1 = SM.AnchorOf(1);
            drag.SimBegin(k3, false, k3.Center);
            drag.SimMove(new Vector3(a1.Center.x, 0f, a1.Center.z));
            yield return Wait(0.25f);
            drag.Cancel();
            yield return Wait(0.5f);
            bool back = true; float worst = 0f;
            for (int i = 0; i < SM.Islands.Count; i++) { float d = (new Vector2(SM.Islands[i].px, SM.Islands[i].pz) - before[i]).magnitude; worst = Mathf.Max(worst, d); if (d > 1e-4f) back = false; }
            float off = 0f; foreach (var k in SM.Islands) off = Mathf.Max(off, k.PreviewOffset.magnitude);
            Line(sb, back && History.UndoCount == h && drag.PreviewCount == 0 && off < 0.01f && ColumnBands.RangeColumn == -1 && !ColumnBands.SlitShown && !ColumnBands.ShadowShown && !SM.IslandMoveActive,
                "Esc / right-click during a free drag: every island exactly where it was, previews eased back, no History",
                "worst " + worst.ToString("E1") + ", history +" + (History.UndoCount - h) + ", preview offsets " + off.ToString("F3") + ", band " + ColumnBands.RangeColumn + ", slit " + ColumnBands.SlitShown);
            // B5 merge: column 4's island pushed against column 3's east end in its lane
            var k4 = SM.AnchorOf(4); k3 = SM.AnchorOf(3);
            drag.SimBegin(k4, false, k4.Center);
            drag.SimMove(new Vector3(k3.EastEdge + 0.4f + W * 0.5f, 0f, k3.Center.z));
            var slot5 = drag.Slot; var mt = drag.MergeTarget; bool me = drag.MergeEast; string mtName = mt != null ? mt.assignedChord : "none";
            h = History.UndoCount;
            drag.SimRelease();
            yield return null;
            Line(sb, slot5 == IslandDrag.DropSlot.Merge && mt == k3 && me && SM.Glued(3) && History.UndoCount == h + 1,
                "free drag MERGE: pushed against another island's outer end in its lane the magnet offers the merge; the release glues them, one History entry",
                "slot " + slot5 + ", target " + mtName + " east " + me + ", glued " + SM.Glued(3) + ", history +" + (History.UndoCount - h));
            History.Undo();
            yield return null;
            // B6 a drop on a full column is refused: everything back, no History
            while (SM.ColumnSize(1) < ProjectConfig.MaxLanes) SM.AddIslandInColumn(SM.ColumnFirst(1), true);
            before = SM.Islands.Select(k => new Vector2(k.px, k.pz)).ToList(); h = History.UndoCount;
            k3 = SM.AnchorOf(3); a1 = SM.AnchorOf(1);
            drag.SimBegin(k3, false, k3.Center);
            drag.SimMove(new Vector3(a1.Center.x, 0f, a1.Center.z));
            var slot6 = drag.Slot;
            drag.SimRelease();
            yield return Wait(0.4f);
            back = true; for (int i = 0; i < SM.Islands.Count; i++) if ((new Vector2(SM.Islands[i].px, SM.Islands[i].pz) - before[i]).magnitude > 1e-4f) back = false;
            Line(sb, slot6 == IslandDrag.DropSlot.Join && back && History.UndoCount == h, "a drop that joins a full column is refused: the island glides back, no History", "slot " + slot6 + ", back " + back + ", history +" + (History.UndoCount - h));
            while (SM.ColumnSize(1) > 2) History.Undo();
        }

        // ================================================================ C. repeat = passes on a conveyor belt
        BuildSong();
        yield return null;
        int col = 2; var isl = SM.AnchorOf(col); int islI = SM.Islands.IndexOf(isl);
        float pl0 = SM.PassLength(col), len0 = SM.ColumnLength(col), total0 = SM.TotalBeats, xNext0 = SM.ColumnX(col + 1);
        Run(sb, "SetRepeat ×2 on a one-island column: ColumnPasses 2, PassLength kept, ColumnLength doubles, TotalBeats + one pass, the island's cube gets 2 windows (start, start + PassLength), the next column moves right by one slot, the belt (2 slots) and a wider band, one History entry", () =>
        {
            int h = History.UndoCount;
            SM.SetRepeat(islI, 2);
            isl = SM.Islands[islI];
            if (SM.ColumnPasses(col) != 2 || Mathf.Abs(SM.PassLength(col) - pl0) > 1e-4f || Mathf.Abs(SM.ColumnLength(col) - 2f * len0) > 1e-4f) return "passes " + SM.ColumnPasses(col) + " pass " + SM.PassLength(col) + " length " + SM.ColumnLength(col);
            if (Mathf.Abs(SM.TotalBeats - (total0 + pl0)) > 1e-4f) return "TotalBeats " + SM.TotalBeats + " want " + (total0 + pl0);
            var cubes = CubesOf(isl); if (cubes.Count == 0) return "no cube";
            foreach (var c in cubes)
            {
                if (c.windows.Count != 2) return "windows " + c.windows.Count;
                if (Mathf.Abs(c.windows[0].start - SM.ColumnStart(col)) > 1e-4f || Mathf.Abs(c.windows[1].start - (SM.ColumnStart(col) + pl0)) > 1e-4f || Mathf.Abs(c.windows[1].length - pl0) > 1e-4f) return "window starts " + c.windows[0].start + " / " + c.windows[1].start;
            }
            foreach (var c in SequenceMaster.Cubes) if (c != null && !c.rider && !c.IsOnMoon && c.Island != isl && c.windows.Count != 1) return "another cube has " + c.windows.Count + " windows";
            if (Mathf.Abs(SM.ColumnX(col + 1) - (xNext0 + Pitch)) > 0.01f) return "next column x " + SM.ColumnX(col + 1).ToString("F2") + " want " + (xNext0 + Pitch).ToString("F2");
            if (isl.Belt == null || isl.Belt.Slots != 2) return "belt " + (isl.Belt != null ? isl.Belt.Slots.ToString() : "none");
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            ColumnBands.Refresh();
            float bw = ColumnBands.BandBounds(col).size.x, want = W + Pitch + (SM.Touching(col - 1) ? 0f : ProjectConfig.BandPadX) + (SM.Touching(col) ? 0f : ProjectConfig.BandPadX);   // v7: no pad where a section's columns touch
            if (Mathf.Abs(bw - want) > 0.05f) return "band width " + bw.ToString("F2") + " want " + want.ToString("F2");
            return Invariants();
        });
        Run(sb, "a stacked column with one island ×2: that island 2 windows, its column-mate 1 (it rests in pass 2: IsActiveAt false there), the column plays 2 passes", () =>
        {
            var l = SM.ColumnIslands(1); var a = l[0]; var b = l[1];
            SM.SetRepeat(SM.Islands.IndexOf(b), 2);
            l = SM.ColumnIslands(1); a = l[0]; b = l[1];
            if (SM.ColumnPasses(1) != 2) return "passes " + SM.ColumnPasses(1);
            if (CubesOf(b).Any(c => c.windows.Count != 2) || CubesOf(a).Any(c => c.windows.Count != 1)) return "windows a " + string.Join(",", CubesOf(a).Select(c => c.windows.Count)) + " b " + string.Join(",", CubesOf(b).Select(c => c.windows.Count));
            float mid2 = SM.PassStart(1, 1) + SM.PassLength(1) * 0.5f;
            if (SM.IsActiveAt(a, mid2) || !SM.IsActiveAt(b, mid2) || !SM.IsActiveAt(a, SM.ColumnStart(1) + 0.5f)) return "IsActiveAt";
            History.Undo();
            return SM.ColumnPasses(1) == 1 ? null : "undo passes " + SM.ColumnPasses(1);
        });
        Run(sb, "×3: three passes, a 3-slot belt, the next column two slots right", () =>
        {
            SM.SetRepeat(islI, 3);
            isl = SM.Islands[islI];
            if (SM.ColumnPasses(col) != 3 || isl.Belt == null || isl.Belt.Slots != 3) return "passes " + SM.ColumnPasses(col);
            if (Mathf.Abs(SM.ColumnX(col + 1) - (xNext0 + 2f * Pitch)) > 0.01f) return "next x " + SM.ColumnX(col + 1).ToString("F2");
            return Invariants();
        });
        {
            var bb = SM.ColumnBounds(col); bb.Encapsulate(SM.ColumnBounds(col + 1)); bb.Encapsulate(SM.ColumnBounds(col - 1));
            Frame(bb, 1.0f);
            yield return Wait(0.6f);
            yield return Shot("k5_belt_rest.png");
        }
        // the ride: mid-pass samples while playing
        {
            isl = SM.Islands[islI];
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            var slots = new StringBuilder(); string rideWhy = null;
            for (int p = 0; p < 3; p++)
            {
                GlobalClock.Seek(SM.PassStart(col, p) + SM.PassLength(col) * 0.5f - 0.6f);
                if (!GlobalClock.IsPlaying) GlobalClock.Play();
                yield return Wait(0.5f);   // 0.6 beat before mid-pass at 120 BPM = 0.3 s; plus the catch-up glide after the seek
                float lift; float pure = SM.RideSlot(isl, GlobalClock.SongBeatD, out lift);
                float shown = isl.RideSlotShown, offX = isl.transform.position.x - (isl.px + isl.SlideOffset.x + isl.PreviewOffset.x);
                var cube = CubesOf(isl).FirstOrDefault();
                float cx = cube != null ? cube.transform.position.x : float.NaN, ix = isl.transform.position.x;
                bool cubeRides = cube != null && cx > ix - 0.8f && cx < ix + 5f * ProjectConfig.Spacing + 0.8f;
                bool cometOn = Comet.I != null && (new Vector2(Comet.I.Position.x - isl.VisualCenter.x, Comet.I.Position.z - isl.VisualCenter.z)).magnitude < new Vector2(isl.Width, isl.Depth).magnitude * 0.5f + 1.2f;
                slots.Append("pass ").Append(p).Append(": beat ").Append(GlobalClock.SongBeatD.ToString("F2")).Append(" slot ").Append(shown.ToString("F3")).Append(" (pure ").Append(pure.ToString("F3")).Append(") x+").Append(offX.ToString("F2")).Append(" cube rides ").Append(cubeRides).Append(" comet on it ").Append(cometOn).Append("; ");
                if (Mathf.Abs(shown - p) > 0.02f || Mathf.Abs(pure - p) > 1e-4f || Mathf.Abs(offX - p * Pitch) > 0.05f) rideWhy = "pass " + p + ": slot " + shown.ToString("F3") + " offset " + offX.ToString("F2");
                if (!cubeRides) rideWhy = "pass " + p + ": the cube does not ride (cube x " + cx.ToString("F2") + ", island x " + ix.ToString("F2") + ")";
                if (!cometOn) rideWhy = "pass " + p + ": the comet is not on the island";
            }
            Info(sb, "ride: " + slots);
            // the belt runs only while it carries the island: a steady mid-pass, then across the pass boundary
            GlobalClock.Seek(SM.PassStart(col, 1) - 2.2f);
            yield return Wait(0.35f);   // -> 1.5 beats before the boundary (the jerk starts JerkLead before it)
            float tr0 = isl.Belt != null ? isl.Belt.Travel : 0f, dr0 = isl.Belt != null ? isl.Belt.DrumAngle : 0f;
            yield return Wait(0.25f);   // -> 1.0 beat before
            float tr1 = isl.Belt != null ? isl.Belt.Travel : 0f, dr1 = isl.Belt != null ? isl.Belt.DrumAngle : 0f;
            yield return Wait(0.9f);    // -> 0.8 beat after: the jerk and its clunk are over
            float tr2 = isl.Belt != null ? isl.Belt.Travel : 0f, dr2 = isl.Belt != null ? isl.Belt.DrumAngle : 0f;
            bool steady = Mathf.Abs(tr1 - tr0) < 1e-4f && Mathf.Abs(dr1 - dr0) < 1e-3f, ran = Mathf.Abs(tr2 - tr1 - Pitch) < 0.05f && Mathf.Abs(dr2 - dr1) > 10f;
            GlobalClock.Stop();
            yield return Wait(0.3f + (ProjectConfig.ResetStaggerBeats + ProjectConfig.BeltGlideBeats) / (float)GlobalClock.BeatsPerSecond);   // v7 §12.2: a stop glides it home in real time
            float home = isl.RideSlotShown, homeX = isl.transform.position.x - isl.px;
            int late = Synth.LateEvents - late0, err = Synth.Errors - err0;
            Line(sb, rideWhy == null && steady && ran && Mathf.Abs(home) < 1e-3f && Mathf.Abs(homeX) < 0.01f && late == 0 && err == 0,
                "the ride (×3, playing): mid-pass p the island stands at slot p (x offset p × SlotPitch; the cube and the comet ride with it), the belt's surface and drums move only across a pass boundary (one slot), stopped = home (slot 0), Synth late / errors 0",
                (rideWhy ?? "slots 0 / 1 / 2 on the beat") + ", belt travel steady " + (tr1 - tr0).ToString("F3") + " then +" + (tr2 - tr1).ToString("F2") + " (drums " + (dr2 - dr1).ToString("F0") + "°), stopped slot " + home.ToString("F3") + " x+" + homeX.ToString("F3") + ", late +" + late + " errors +" + err);
        }
        // grabbing an island while it rides: it stays under the pointer (no jump), Esc puts it home, the ride resumes
        if (drag != null)
        {
            isl = SM.Islands[islI];
            float home0 = isl.px;
            GlobalClock.Seek(SM.PassStart(col, 2) + 1.0f);
            GlobalClock.Play();
            yield return Wait(0.5f);
            float slotBefore = isl.RideSlotShown, xBefore = isl.transform.position.x;
            drag.SimBegin(isl, false, isl.VisualCenter);
            float xAfter = isl.transform.position.x, logical = isl.px - home0, rideAfter = isl.RideSlotShown;
            drag.SimMove(isl.VisualCenter);   // the pointer has not moved
            float xMoved = isl.transform.position.x;
            drag.Cancel();
            float back = isl.px - home0;
            yield return Wait(0.6f);
            float resumed = isl.RideSlotShown, expect = SM.RideSlot(isl, GlobalClock.SongBeatD, out float _unused);
            GlobalClock.Stop();
            Line(sb, Mathf.Abs(slotBefore - 2f) < 0.02f && Mathf.Abs(xAfter - xBefore) < 0.05f && Mathf.Abs(xMoved - xBefore) < 0.3f && Mathf.Abs(logical - 2f * Pitch) < 0.01f && rideAfter == 0f && Mathf.Abs(back) < 1e-4f && Mathf.Abs(resumed - expect) < 0.02f,
                "grabbing an island riding its belt (slot 2): it stays where it is shown (its logical root moves there, the ride drops to 0), Esc puts it home exactly and the ride resumes",
                "slot " + slotBefore.ToString("F2") + ", x " + xBefore.ToString("F2") + " -> " + xAfter.ToString("F2") + " (drag frame " + xMoved.ToString("F2") + "), logical +" + logical.ToString("F2") + ", ride " + rideAfter.ToString("F2") + ", cancel -> +" + back.ToString("F4") + ", resumed " + resumed.ToString("F2") + " (pure " + expect.ToString("F2") + ")");
        }
        // the jerk (paused mid-jerk) and the glide back after the last pass
        {
            isl = SM.Islands[islI];
            float bnd = SM.PassStart(col, 1);
            GlobalClock.Seek(bnd - ProjectConfig.JerkLead * 0.45f);   // stopped + seeked = paused on that beat
            yield return Wait(0.5f);
            float mid = isl.RideSlotShown;
            yield return Shot("k5_belt_jerk.png");
            GlobalClock.Seek(bnd + ProjectConfig.JerkSettle * 0.1f);
            yield return Wait(0.5f);
            float clunk = isl.RideSlotShown;
            float endB = SM.ColumnStart(col) + SM.ColumnLength(col);
            GlobalClock.Seek(endB + ProjectConfig.BeltGlideBeats * 0.5f);
            yield return Wait(0.5f);
            float held = isl.RideSlotShown;   // v7 §12.2 (changed meaning: v5 glided back right after its last pass): it stays at its last slot
            GlobalClock.LoopIndex = 1;        // the next loop pass: the song has reset, the belt glides home (with its stagger)
            float stag = SM.ResetStaggerOf(isl);
            GlobalClock.Seek(Math.Max(0.01, stag + ProjectConfig.BeltGlideBeats * 0.5f));
            yield return Wait(0.5f);
            float glide = isl.RideSlotShown, lift = isl.BeltOffset.y;
            GlobalClock.Seek(stag + ProjectConfig.BeltGlideBeats + 0.3f);
            yield return Wait(0.5f);
            float after = isl.RideSlotShown;
            GlobalClock.Stop();
            Line(sb, mid > 0.15f && mid < 0.99f && clunk > 1f && clunk < 1.06f && Mathf.Abs(held - 2f) < 0.02f && glide > 0.2f && glide < 1.8f && lift > 0.1f && Mathf.Abs(after) < 1e-3f,
                "the jerk: mid-way just before the boundary, a hair past the slot just after it (the clunk); v7: after its last pass it STAYS at its last slot until the song resets, then glides back with a hop (staggered), home after BeltGlideBeats",
                "mid-jerk " + mid.ToString("F3") + ", clunk " + clunk.ToString("F3") + ", held " + held.ToString("F3") + ", after the reset glide " + glide.ToString("F3") + " (hop " + lift.ToString("F2") + " u, stagger " + stag.ToString("F2") + "), after " + after.ToString("F3"));
        }
        Run(sb, "undo of the repeats restores the timeline, the layout and removes the belt", () =>
        {
            History.Undo(); History.Undo();
            isl = SM.Islands[islI];
            if (SM.ColumnPasses(col) != 1 || Mathf.Abs(SM.TotalBeats - total0) > 1e-4f || Mathf.Abs(SM.ColumnX(col + 1) - xNext0) > 0.01f) return "passes " + SM.ColumnPasses(col) + " total " + SM.TotalBeats + " x " + SM.ColumnX(col + 1).ToString("F2");
            if (isl.Belt != null) return "belt left";
            foreach (var c in SequenceMaster.Cubes) if (c != null && !c.rider && !c.IsOnMoon && c.windows.Count != 1) return "windows " + c.windows.Count;
            return Invariants();
        });
        // all islands of a column ×2: the column simply plays twice (every note of pass 1 again in pass 2, logged)
        {
            GlobalClock.Stop();
            var idx1 = new List<int>(); for (int k = SM.ColumnFirst(1); k < SM.ColumnFirst(1) + SM.ColumnSize(1); k++) idx1.Add(k);
            foreach (int k in idx1) SM.SetRepeat(k, 2);   // each SetRepeat rebuilds the islands: by index, not by reference
            var l = SM.ColumnIslands(1);
            bool twice = SM.ColumnPasses(1) == 2;
            var ev = new List<AudioCube.TimelineEvent>(); var ev2 = new List<AudioCube.TimelineEvent>();
            foreach (var kb in l) foreach (var c in CubesOf(kb))
            {
                if (c.windows.Count != 2) twice = false;
                else { c.TimelineEvents(0, ev); c.TimelineEvents(1, ev2); if (ev.Count == 0 || ev.Count != ev2.Count) twice = false; for (int i = 0; i < Mathf.Min(ev.Count, ev2.Count); i++) if (ev[i].node != ev2[i].node || Mathf.Abs(ev[i].start - ev2[i].start) > 1e-4f) twice = false; }
            }
            yield return Wait(1.0f);   // everything before rings out
            long log0 = Performance.LoggedTotal;
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            float s0 = SM.ColumnStart(1), pl = SM.PassLength(1);
            float pre = s0 >= 1f ? 1f : 0f;   // start a beat early: a downbeat exactly at the play-start can be missed (both passes then start mid-song)
            GlobalClock.Seek(s0 - pre);
            GlobalClock.Play();
            double dsp0 = GlobalClock.DspTimeOfBeat(0);
            double bps = GlobalClock.BeatsPerSecond;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < (2f * pl + pre) / (float)bps + 0.3f) yield return null;
            GlobalClock.Stop();
            var set = new HashSet<KeyBlock>(l);
            int n1 = 0, n2 = 0; var p1 = new List<string>(); var p2 = new List<string>();
            for (int i = 0; i < Performance.LoggedCount; i++)
            {
                if (Performance.LoggedTotal - Performance.LoggedCount + i < log0) continue;
                var e = Performance.Logged(i);
                if (e.owner < 16 || ReferenceEquals(e.island, null) || !set.Contains(e.island)) continue;
                double beat = (e.onDsp - dsp0) * bps;
                // the pitch and the 16th it lands on (humanised timing moves a note by a few ms, never off its 16th)
                if (beat >= s0 - 0.05 && beat < s0 + pl - 0.05) { n1++; p1.Add(e.midi + "@" + (Math.Round((beat - s0) * 4.0) / 4.0).ToString("F2")); }
                else if (beat >= s0 + pl - 0.05 && beat < s0 + 2f * pl - 0.05) { n2++; p2.Add(e.midi + "@" + (Math.Round((beat - s0 - pl) * 4.0) / 4.0).ToString("F2")); }
            }
            p1.Sort(); p2.Sort();
            bool same = n1 > 0 && n1 == n2 && string.Join(" ", p1) == string.Join(" ", p2);
            int late = Synth.LateEvents - late0, err = Synth.Errors - err0;
            Line(sb, twice && same && late == 0 && err == 0,
                "all islands of a column ×2 = the column plays twice: every cube 2 windows with the same notes, the logged notes of pass 2 = pass 1 (pitch and beat), Synth late / errors 0",
                "windows ok " + twice + ", logged pass 1: " + n1 + ", pass 2: " + n2 + ", identical " + same + ", late +" + late + " errors +" + err);
            Info(sb, "pass 1 notes: " + string.Join(" ", p1) + " | pass 2 notes: " + string.Join(" ", p2));
            foreach (int k in idx1) History.Undo();
        }
        // SetRepeat while playing keeps the lit column's pass and beat
        {
            int i2 = SM.ColumnFirst(2);
            GlobalClock.Seek(SM.ColumnStart(2) + 1.0f);
            GlobalClock.Play();
            yield return Wait(0.4f);
            double local0 = GlobalClock.BeatNow - SM.ColumnStart(SM.LitColumn); string lit0 = SM.AnchorOf(SM.LitColumn).assignedChord;
            SM.SetRepeat(SM.ColumnFirst(1), 2);   // an earlier column gets longer: every later start shifts by one pass
            double local1 = GlobalClock.BeatNow - SM.ColumnStart(SM.LitColumn); string lit1 = SM.AnchorOf(SM.LitColumn).assignedChord;
            yield return Wait(0.3f);
            GlobalClock.Stop();
            Line(sb, lit1 == lit0 && Math.Abs(local1 - local0) < 0.15, "SetRepeat while playing (an earlier column ×2) keeps the lit column's local beat", "lit " + lit0 + " " + local0.ToString("F3") + " -> " + lit1 + " " + local1.ToString("F3"));
            History.Undo();
        }

        // ================================================================ D. the song key and the home glyph
        // v6 (package K): the job-1 badge (U1's JobBadge: a house with a 1) marks a home island doing the home job; the v5 sticker (HomeShown)
        // remains only on a home island doing another job (a tonic dominant). "Home is marked" = the sticker OR the job-1 badge, never both.
        Func<KeyBlock, bool> homeMark = k => k.HomeShown || k.JobShown == 1;
        Run(sb, "the key never changed through the moves, merges, repeats and undos; a home mark (v6: the job-1 badge, or the house sticker on a home island doing another job) sits exactly on the islands rooted on the key's tonic; a sleeping home island hides it", () =>
        {
            if (SM.SongKey.tonic != key0.tonic || SM.SongKey.minor != key0.minor) return "key " + SM.SongKey.tonic + " vs " + key0.tonic;
            int homes = 0;
            foreach (var kb in SM.Islands)
            {
                bool want = MusicTheory.PitchClass(kb.chordRootMIDI) == SM.SongKey.tonic && !kb.sleep;
                if (homeMark(kb) != want || (kb.HomeShown && kb.JobShown == 1)) return "island " + kb.assignedChord + " home " + kb.HomeShown + " job " + kb.JobShown + " want " + want;
                if (want) homes++;
            }
            var h0 = SM.Islands.FirstOrDefault(homeMark);
            if (h0 == null) return "no home island in the song";
            int hi = SM.Islands.IndexOf(h0);
            SM.SetSleep(hi, true);
            bool hid = !homeMark(SM.Islands[hi]);
            SM.SetSleep(hi, false);
            if (!hid || !homeMark(SM.Islands[hi])) return "sleep: hidden " + hid;
            History.Undo(); History.Undo();
            return null;
        });
        {
            var h0 = SM.Islands.FirstOrDefault(homeMark);
            if (h0 != null) { Frame(new Bounds(h0.Center, new Vector3(h0.Width * 0.8f, 1f, h0.Depth * 0.8f)), 1.2f); yield return Wait(0.35f); yield return Shot("k5_home.png"); }
        }

        // ================================================================ E. the user's save
        Line(sb, Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + Md5(SongIO.Path));

        // restore
        for (int i = 0; i < Instruments.Count && i < mutedBefore.Length; i++) Instruments.SetMuted(i, mutedBefore[i]);
        GlobalClock.ClearRegion();
        LoadFixture();
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" passed, ").Append(fail).Append(" failed\n");
        Report = sb.ToString();
        Done = true;
        Flush(sb);
    }

    /// <summary>
    /// The vibe look check (the orchestrator, 2026-09-30: an island must read as its card): one island of each of the 7 vibes side by side in a
    /// row (same root), the deck holding the same 7 chords and the tray open, captured as k5_vibes.png. Stopped; returns the platform colours.
    /// </summary>
    public static string VibeRow()
    {
        if (SongManager.I == null) return "needs Play mode";
        Prepare();
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        var qs = new[] { ChordQuality.Major7, ChordQuality.Major9, ChordQuality.Minor7, ChordQuality.Minor9, ChordQuality.Dominant7, ChordQuality.Dominant9, ChordQuality.Sus4 };
        var ms = new SongManager.MeasureData[qs.Length];
        for (int i = 0; i < qs.Length; i++) { var s = MusicTheory.Semitones(qs[i]); ms[i] = new SongManager.MeasureData { chordKey = MusicTheory.ChordName(60, s), chordRootMIDI = 60, semitones = s, bars = 1, energy = 2, repeat = 1 }; }
        var song = new SongManager.SongData { songName = "k5 vibes", bpm = 110, timeSignature = "4/4", measures = ms };
        SongManager.I.StartFreshSong(song);
        SongManager.I.PlaceAll(ms.Skip(1).ToList());
        IslandTray.SetDeck(ms);
        IslandTray.Open();
        Frame(SongManager.I.SongBounds, 0.9f);
        var sb = new StringBuilder();
        foreach (var kb in SongManager.I.Islands) { var c = KeyBlock.PlatformColorOf(kb.chordColor); sb.Append(Vibe.Word(Vibe.Of(kb.quality))).Append(' ').Append(c.r.ToString("F2")).Append(',').Append(c.g.ToString("F2")).Append(',').Append(c.b.ToString("F2")).Append("; "); }
        return sb.ToString();
    }

    /// <summary>K's battery in one Play session: V5ChecksK.RunAll, the vibe captures, then V4ChecksK.RunAll (and V3ChecksB.RunAll when
    /// <paramref name="withB"/>); progress in Captures/k5_battery.txt ("DONE" at the end).</summary>
    public static string RunBattery(bool withB)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(BatteryRoutine(withB));
        return "started";
    }

    static IEnumerator BatteryRoutine(bool withB)
    {
        string path = Path.Combine(V2Checks.CapturePath, "k5_battery.txt");
        var log = new StringBuilder();
        Action<string> note = t => { log.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(t).Append('\n'); try { File.WriteAllText(path, log.ToString()); } catch (Exception) { } };
        note("V5ChecksK " + RunAll());
        float t0 = Time.realtimeSinceStartup;
        while (!Done && Time.realtimeSinceStartup - t0 < 400f) yield return null;
        note("V5ChecksK " + (Done ? Report.Split('\n').FirstOrDefault(l => l.StartsWith("SUMMARY")) : "TIMEOUT"));
        yield return VibeRoutine();
        note("vibes captured");
        note("V4ChecksK " + V4ChecksK.RunAll());
        t0 = Time.realtimeSinceStartup;
        while (!V4ChecksK.Done && Time.realtimeSinceStartup - t0 < 600f) yield return null;
        note("V4ChecksK " + (V4ChecksK.Done ? V4ChecksK.Report.Split('\n').FirstOrDefault(l => l.StartsWith("SUMMARY")) : "TIMEOUT"));
        if (withB)
        {
            V3ChecksB.Prepare();
            note("V3ChecksB " + V3ChecksB.RunAll());
            t0 = Time.realtimeSinceStartup;
            while (!V3ChecksB.Done && Time.realtimeSinceStartup - t0 < 600f) yield return null;
            if (V3ChecksB.Done)
            {
                try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, "k5_v3b_report.txt"), V3ChecksB.Report); } catch (Exception) { }
                int bp = 0, bf = 0; foreach (var l in V3ChecksB.Report.Split('\n')) { if (l.StartsWith("PASS")) bp++; else if (l.StartsWith("FAIL")) bf++; }
                note("V3ChecksB " + bp + " passed, " + bf + " failed (Captures/k5_v3b_report.txt)");
            }
            else note("V3ChecksB TIMEOUT");
        }
        GlobalClock.Stop();
        note("DONE");
    }

    /// <summary>The suites next to K's area that drag / merge / play islands (V3Fixes, V3Integration, V4Integration), one after another with the
    /// V4Suites reset between them; results in Captures/k5_neighbours.txt ("DONE" at the end, PASS / FAIL counts from each report).</summary>
    public static string RunNeighbours()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(NeighboursRoutine());
        return "started";
    }

    static IEnumerator NeighboursRoutine()
    {
        string path = Path.Combine(V2Checks.CapturePath, "k5_neighbours.txt");
        var log = new StringBuilder();
        Action<string> note = t => { log.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(t).Append('\n'); try { File.WriteAllText(path, log.ToString()); } catch (Exception) { } };
        var names = new[] { "V3Fixes", "V3Integration", "V4Integration", "v3 bundle (D, V2 RunAll/RunWorld/RunIntegration, B, A)" };
        var reports = new[] { "v3fixes_report.txt", "v3_integration_report.txt", "v4_integration_report.txt", "a_suites_report.txt" };
        for (int k = 0; k < names.Length; k++)
        {
            try { CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit(); FocusLoop.Dismiss(); GlobalClock.Stop(); if (MainMenu.IsShown) MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); if (IslandTray.IsOpen) IslandTray.Close(); SongIO.QuitAutosave = false; }
            catch (Exception e) { note("reset: " + e.Message); }
            yield return null; yield return null;
            string r;
            try { r = k == 0 ? V3Fixes.Run(false) : (k == 1 ? V3Integration.Run(false) : (k == 2 ? V4Integration.Run(false) : V3ChecksA.RunSuites())); } catch (Exception e) { r = "threw " + e.Message; }
            note(names[k] + " " + r);
            yield return null; yield return null;
            float t0 = Time.realtimeSinceStartup;
            Func<bool> done = () => k == 0 ? V3Fixes.Done : (k == 1 ? V3Integration.Done : (k == 2 ? V4Integration.Done : V3ChecksA.SuitesDone));
            while (!done() && Time.realtimeSinceStartup - t0 < 420f) yield return null;
            yield return Wait(0.5f);
            string text = ""; try { string rp = Path.Combine(V2Checks.CapturePath, reports[k]); text = File.Exists(rp) ? File.ReadAllText(rp) : ""; } catch (Exception) { }
            int p = 0, f = 0; var fails = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line.TrimStart(), @"^(\d+\.\s*)?(PASS|FAIL)\b");
                if (!m.Success) continue;
                if (m.Groups[2].Value == "PASS") p++; else { f++; fails.Append("   ").Append(line.Length > 220 ? line.Substring(0, 220) : line).Append('\n'); }
            }
            note(names[k] + (done() ? "" : " TIMEOUT") + ": " + p + " pass, " + f + " fail (" + (Time.realtimeSinceStartup - t0).ToString("F0") + " s)" + (fails.Length > 0 ? "\n" + fails : ""));
        }
        GlobalClock.Stop();
        note("DONE");
    }

    /// <summary>The vibe captures in one go (Captures/k5_vibes_a.png: sunny … moonlit, k5_vibes_b.png: moonlit … floaty, k5_vibes_home.png: the home
    /// island close up with its house sticker), the tray closed; writes Captures/k5_vibes_done.txt with the platform colours when finished.</summary>
    public static string VibeCaptures()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(VibeRoutine());
        return "started";
    }

    static IEnumerator VibeRoutine()
    {
        string done = Path.Combine(V2Checks.CapturePath, "k5_vibes_done.txt");
        if (File.Exists(done)) File.Delete(done);
        string colours = VibeRow();
        IslandTray.Close();
        yield return Wait(0.5f);
        var b = SM.ColumnBounds(0); for (int c = 1; c <= 3; c++) b.Encapsulate(SM.ColumnBounds(c));
        Frame(b, 1.1f);
        yield return Wait(0.35f);
        yield return Shot("k5_vibes_a.png");
        b = SM.ColumnBounds(3); for (int c = 4; c <= 6 && c < SM.ColumnCount; c++) b.Encapsulate(SM.ColumnBounds(c));
        Frame(b, 1.1f);
        yield return Wait(0.35f);
        yield return Shot("k5_vibes_b.png");
        var h0 = SM.Islands.FirstOrDefault(k => k.HomeShown || k.JobShown == 1);
        if (h0 != null) { Frame(new Bounds(h0.Center, new Vector3(h0.Width * 0.8f, 1f, h0.Depth * 0.8f)), 1.15f); yield return Wait(0.35f); yield return Shot("k5_vibes_home.png"); }
        // the deck's vibe cards next to the islands they make (the tray open: its cards are the 7 vibes too)
        IslandTray.Open();
        b = SM.ColumnBounds(0); for (int c = 1; c <= 4 && c < SM.ColumnCount; c++) b.Encapsulate(SM.ColumnBounds(c));
        Frame(b, 1.0f);
        yield return Wait(0.6f);
        yield return Shot("k5_vibes_cards.png");
        IslandTray.Close();
        File.WriteAllText(done, colours + "\nhome islands: " + SM.Islands.Count(k => k.HomeShown || k.JobShown == 1));
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) { var h = md5.ComputeHash(File.ReadAllBytes(path)); var s = new StringBuilder(); foreach (var x in h) s.Append(x.ToString("x2")); return s.ToString(); }
    }
}
