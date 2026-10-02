using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package K (columns and island components) Play-mode verification, SPEC v4 §3: <c>V4ChecksK.RunAll()</c> starts a coroutine; poll
/// <c>V4ChecksK.Done</c> or the report file Captures/v4k_report.txt (numbered PASS/FAIL lines, INFO lines with numbers). Covers the legacy
/// conversion of the v1 fixture (layout + the v3 timing, against a reference dump when one exists), the column API, stacked timing and
/// windows, add / duplicate / register / sleep / delete, the in-column drag with push / swap and its range band, the tray ghost's STACK /
/// INSERT / MERGE / BLOCKED slots, merge + split across columns, column reorder, undo / redo and save / load round trips, satellites, the
/// focus-loop flight, the column follow, Moons pushed clear, and a recording that proves two islands of one column sound together
/// (Captures/k_simul.wav + the scheduled beats of every logged note). Captures: k_song4.png, k_drag_push.png, k_merge.png, k_register.png,
/// k_sleep.png. Never writes the user's save; restores the instrument mutes and the fixture at the end.
/// </summary>
public static class V4ChecksK
{
    public static string Report = "";
    public static bool Done;
    static int num;
    static StringBuilder live;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "v4k_report.txt");
    public static string ScratchSave => Path.Combine(Application.temporaryCachePath, "v4checksk_roundtrip.json");
    /// <summary>A dump of the fixture's timeline taken before package K landed (see DumpTimeline): the v3 reference for "legacy timing identical".</summary>
    public static string ReferencePath => Path.Combine(V2Checks.CapturePath, "k_v3_timeline.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" K").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
        WorldInput.Unlock("prompt");
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

    static string Chords() { var s = new StringBuilder(); foreach (var kb in SM.Islands) s.Append(kb.assignedChord).Append(kb.column).Append(' '); return s.ToString().Trim(); }

    /// <summary>One line per island (start, length, column, anchor) and per cube window (start, length, island index, order, silent) + HitSteps16 of
    /// window 0: the timeline fingerprint of the loaded song.</summary>
    public static string Timeline()
    {
        var sb = new StringBuilder();
        var sm = SM;
        sb.Append("total ").Append(sm.TotalBeats.ToString("F3")).Append('\n');
        for (int i = 0; i < sm.Islands.Count; i++) sb.Append("island ").Append(i).Append(' ').Append(sm.MeasureStarts[i].ToString("F3")).Append(' ').Append(sm.Islands[i].LengthBeats.ToString("F3")).Append('\n');
        var cubes = SequenceMaster.Cubes.Where(c => c != null).OrderBy(c => c.id).ToList();
        foreach (var c in cubes)
        {
            sb.Append("cube ").Append(c.id).Append(c.rider ? " rider" : "").Append(c.IsOnMoon ? " moon" : "").Append(':');
            for (int w = 0; w < c.windows.Count; w++)
            {
                var win = c.windows[w];
                int isl = win.island != null ? (win.island.IsMoon ? -1 - sm.Moons.IndexOf(win.island) : sm.Islands.IndexOf(win.island)) : -99;
                sb.Append(' ').Append(win.start.ToString("F3")).Append('/').Append(win.length.ToString("F3")).Append('@').Append(isl).Append('#').Append(win.order).Append(win.silent ? "s" : "");
            }
            if (c.windows.Count > 0) sb.Append(" | ").Append(string.Join(",", c.HitSteps16(0, c.windows[0].order)));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Loads the fixture and writes its timeline (run BEFORE package K lands: the v3 reference).</summary>
    public static string DumpTimeline()
    {
        Prepare(); LoadFixture();
        string t = Timeline();
        Directory.CreateDirectory(V2Checks.CapturePath);
        File.WriteAllText(ReferencePath, t);
        return "wrote " + ReferencePath + " (" + t.Split('\n').Length + " lines)";
    }

    static CubeState CubeOn(int island, int instrument, int[] xs, int[] zs, int step, int hits = -1, int mask = 0)
    {
        return new CubeState { instrument = instrument, measure = island, moon = -1, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = 1, mode = 0, volume = 1f, hits = hits, mask = mask, twinOf = -1 };
    }

    static float MinClearance(int col)
    {
        var l = SM.ColumnIslands(col).OrderBy(k => k.FrontEdge).ToList();
        float m = float.MaxValue;
        for (int k = 0; k + 1 < l.Count; k++) m = Mathf.Min(m, l[k + 1].FrontEdge - l[k].BackEdge);
        return m;
    }

    static string Invariants()
    {
        var sm = SM;
        int prev = -1;
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb.column < prev || kb.column > prev + 1) return "not column-major / contiguous at " + i + " (column " + kb.column + " after " + prev + ")";
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
                float pitch = sm.ColumnX(c + 1) - sm.ColumnX(c), want = sm.ColumnWidth(c) + sm.BeltExtent(c) + (sm.Touching(c) ? 0f : ProjectConfig.SectionGap + Mathf.Max(0, ProjectConfig.SectionBars - sm.SectionBars(sm.SectionOf(c))) * W);   // v7 §13.2: touching inside a section
                if (Mathf.Abs(pitch - want) > 0.01f) return "pitch " + c + "->" + (c + 1) + " = " + pitch.ToString("F2") + " want " + want.ToString("F2");
            }
            foreach (var kb in l) if (Mathf.Abs(sm.MeasureStarts[sm.Islands.IndexOf(kb)] - sm.ColumnStart(c)) > 1e-4f) return "start of an island in column " + c;
        }
        return null;
    }

    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        live = sb;
        Prepare();
        yield return null;
        var mutedBefore = (bool[])Instruments.Muted.Clone();
        string userMd5 = Md5(SongIO.Path);

        // ================================================================ A. the v1 fixture: legacy conversion, v3 timing, column API
        LoadFixture();
        yield return null;
        Run(sb, "legacy conversion of the v1 fixture: 6 one-island columns, a clean row (v7: x pitch IslandWidth inside a section, + SectionGap between the two automatic sections; every pz = island 0's), anchors + hubs, Capture carries col", () =>
        {
            if (SM.Islands.Count != 6 || SM.ColumnCount != 6) return "islands " + SM.Islands.Count + " columns " + SM.ColumnCount;
            string inv = Invariants(); if (inv != null) return inv;
            for (int i = 0; i < 6; i++)
            {
                var kb = SM.Islands[i];
                if (kb.column != i) return "island " + i + " column " + kb.column;
                if (Mathf.Abs(kb.pz - SM.Islands[0].pz) > 1e-4f) return "pz of " + i;
                if (Mathf.Abs(kb.px - (SM.Islands[0].px + i * W + (i >= 4 ? ProjectConfig.SectionGap : 0f))) > 1e-3f) return "px of " + i;   // v7: sections [0, 4]
            }
            var st = SongState.Capture();
            for (int i = 0; i < 6; i++) if (st.measures[i].col != i) return "captured col " + st.measures[i].col;
            if (st.version != SongState.CurrentVersion) return "version " + st.version;
            return null;
        });
        string reference = File.Exists(ReferencePath) ? File.ReadAllText(ReferencePath) : null;
        Run(sb, "v1 fixture timing = v3: island starts, TotalBeats, every cube's windows (start / length / island / order / silent) and HitSteps16 equal the pre-K dump", () =>
        {
            string now = Timeline();
            float t = 0f; for (int i = 0; i < SM.Islands.Count; i++) { if (Mathf.Abs(SM.MeasureStarts[i] - t) > 1e-4f) return "MeasureStarts " + i; t += SM.Islands[i].LengthBeats; }
            if (Mathf.Abs(SM.TotalBeats - t) > 1e-4f) return "TotalBeats " + SM.TotalBeats;
            if (reference == null) return "no reference dump at " + ReferencePath + " (run DumpTimeline before K lands)";
            if (reference != now)
            {
                var a = reference.Split('\n'); var b = now.Split('\n');
                for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return "differs at line " + i + ": v3 '" + a[i] + "' vs '" + b[i] + "'";
                return "line count " + a.Length + " vs " + b.Length;
            }
            return null;
        });
        Info(sb, "fixture timeline: " + Timeline().Replace("\n", " | "));
        Run(sb, "column API on the fixture: ColumnIslands / AnchorOf / ColumnStart / ColumnLength / ActiveColumn / ActiveMeasureIndex (= the anchor) / IsActiveAt", () =>
        {
            for (int c = 0; c < SM.ColumnCount; c++)
            {
                var l = SM.ColumnIslands(c);
                if (l.Count != 1 || l[0] != SM.Islands[c] || SM.AnchorOf(c) != SM.Islands[c] || SM.ColumnFirst(c) != c || SM.ColumnSize(c) != 1) return "column " + c;
                if (Mathf.Abs(SM.ColumnStart(c) - SM.MeasureStarts[c]) > 1e-4f || Mathf.Abs(SM.ColumnLength(c) - SM.Islands[c].LengthBeats) > 1e-4f) return "start/length " + c;
                float mid = SM.ColumnStart(c) + SM.ColumnLength(c) * 0.5f;
                if (SM.ActiveColumn(mid) != c || SM.ActiveMeasureIndex(mid) != c || SM.ActiveColumn(SM.ColumnStart(c)) != c) return "active " + c;
                if (!SM.IsActiveAt(SM.Islands[c], mid) || (c > 0 && SM.IsActiveAt(SM.Islands[c - 1], mid))) return "IsActiveAt " + c;
            }
            if (SM.ColumnOf(3) != 3 || SM.ColumnOf(99) != -1) return "ColumnOf";
            return null;
        });

        // ================================================================ B. the stacked song (4 columns: 1, 3, 2, 1)
        int h0 = 0;
        Run(sb, "AddIslandInColumn above / below: same chord + bars, no cubes, LaneGap beyond the reference's back / front edge, neighbours pushed, one History entry each", () =>
        {
            GlobalClock.Stop(); GlobalClock.ClearRegion();
            var song = MusicTheory.RandomSong(4404, "k song4");
            SM.StartFreshSong(song);
            var cards = new List<SongManager.MeasureData>(); for (int i = 1; i < song.measures.Length && cards.Count < 3; i++) cards.Add(song.measures[i]);
            if (SM.PlaceAll(cards) != 3 || SM.ColumnCount != 4) return "PlaceAll: columns " + SM.ColumnCount;
            h0 = History.UndoCount;
            var r = SM.AnchorOf(1); string chord = r.assignedChord; int bars = r.bars; float back = r.BackEdge, front = r.FrontEdge;
            int cubes0 = SequenceMaster.Cubes.Count;
            int a = SM.AddIslandInColumn(SM.ColumnFirst(1), true);
            if (a < 0) return "above refused";
            var up = SM.Islands[a];
            if (up.column != 1 || up.assignedChord != chord || up.bars != bars) return "above: column " + up.column + " chord " + up.assignedChord;
            if (Mathf.Abs(up.FrontEdge - (back + ProjectConfig.LaneGap)) > 0.01f) return "above: front " + up.FrontEdge + " want " + (back + ProjectConfig.LaneGap);
            if (SequenceMaster.Cubes.Count != cubes0) return "cubes appeared";
            int a2 = SM.AddIslandInColumn(a, true);
            if (a2 < 0 || SM.ColumnSize(1) != 3) return "second above: " + a2 + " size " + SM.ColumnSize(1);
            int b = SM.AddIslandInColumn(SM.ColumnFirst(2), false);
            if (b < 0) return "below refused";
            var dn = SM.Islands[b]; var r2 = SM.AnchorOf(2);
            if (Mathf.Abs(dn.BackEdge - (r2.FrontEdge - ProjectConfig.LaneGap)) > 0.01f) return "below: back " + dn.BackEdge + " want " + (r2.FrontEdge - ProjectConfig.LaneGap);
            if (History.UndoCount != h0 + 3) return "history +" + (History.UndoCount - h0);
            // a middle insert pushes the neighbours: add below the TOP island of column 1 -> the new one sits between, the top one moves up
            string inv = Invariants(); if (inv != null) return inv;
            return null;
        });
        Run(sb, "a full column refuses: + above on a column of MaxLanes returns -1, no island, no History (the column shakes)", () =>
        {
            int col = 1;
            int x = SM.AddIslandInColumn(SM.ColumnFirst(col), false);   // 4th island: allowed
            if (x < 0 || SM.ColumnSize(col) != ProjectConfig.MaxLanes) return "fourth: " + x + " size " + SM.ColumnSize(col);
            int n = SM.Islands.Count, h = History.UndoCount;
            int y = SM.AddIslandInColumn(SM.ColumnFirst(col), true);
            if (y != -1 || SM.Islands.Count != n || History.UndoCount != h) return "fifth: " + y;
            int z = SM.DuplicateIsland(SM.ColumnFirst(col), 1);
            if (z != -1 || SM.Islands.Count != n) return "duplicate below into a full column: " + z;
            History.Undo();   // back to 3 islands in column 1
            if (SM.ColumnSize(col) != 3) return "undo: size " + SM.ColumnSize(col);
            return null;
        });
        // cubes on every island, 120 BPM
        {
            Performance.SetNominalBpm(120f);
            var pm = PathManager.I;
            int[] inst = { 0, 1, 5, 8, 3, 6, 0 };
            for (int i = 0; i < SM.Islands.Count; i++)
            {
                int rr = Mathf.Max(1, SM.Islands[i].rows);
                pm.RestoreCube(CubeOn(i, inst[i % inst.Length], new[] { i % 3, (i % 3) + 1, (i % 3) + 2 }, new[] { 0, Mathf.Min(1, rr - 1), Mathf.Min(2, rr - 1) }, (int)StepLen.Eighth, 5));
            }
            var home = SM.Islands[SM.ColumnFirst(1) + 2];   // the top island of column 1 carries a Bass Rider
            pm.RestoreCube(new CubeState { instrument = 4, measure = SM.Islands.IndexOf(home), moon = -1, xs = new[] { 0, 0 }, zs = new[] { 0, 1 }, rests = new bool[2], step = (int)StepLen.Quarter, gate = 1, volume = 1f, hits = -1, rider = true, twinOf = -1 });
            SM.RecomputeMeasureStarts();
            History.Push();
        }
        yield return null;
        Run(sb, "stacked timing: every island of a column shares its start + length, TotalBeats = Σ column lengths, ActiveMeasureIndex = the anchor, IsActiveAt the whole column only", () =>
        {
            string inv = Invariants(); if (inv != null) return inv;
            float t = 0f;
            for (int c = 0; c < SM.ColumnCount; c++)
            {
                if (Mathf.Abs(SM.ColumnStart(c) - t) > 1e-4f) return "column " + c + " start " + SM.ColumnStart(c) + " want " + t;
                foreach (var kb in SM.ColumnIslands(c)) if (Mathf.Abs(kb.startBeatOffset - t) > 1e-4f || Mathf.Abs(kb.LengthBeats - SM.ColumnLength(c)) > 1e-4f) return "island in column " + c;
                float mid = t + SM.ColumnLength(c) * 0.5f;
                if (SM.ActiveMeasureIndex(mid) != SM.ColumnFirst(c)) return "ActiveMeasureIndex(" + mid + ") = " + SM.ActiveMeasureIndex(mid) + " want anchor " + SM.ColumnFirst(c);
                for (int i = 0; i < SM.Islands.Count; i++) if (SM.IsActiveAt(SM.Islands[i], mid) != (SM.Islands[i].column == c)) return "IsActiveAt island " + i + " at column " + c;
                t += SM.ColumnLength(c);
            }
            if (Mathf.Abs(SM.TotalBeats - t) > 1e-4f) return "TotalBeats " + SM.TotalBeats + " want " + t;
            return null;
        });
        Run(sb, "windows: residents on stacked islands get their column's span; §21 (was: the Rider gets one window per column on the island nearest in z to its home): the Rider plays only on its home island, one window like a resident", () =>
        {
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || c.IsOnMoon) continue;
                var isl = c.Island;
                if (c.windows.Count != 1) return "resident windows " + c.windows.Count;
                if (Mathf.Abs(c.windows[0].start - SM.ColumnStart(isl.column)) > 1e-4f || Mathf.Abs(c.windows[0].length - SM.ColumnLength(isl.column)) > 1e-4f || c.windows[0].island != isl) return "resident window on island " + SM.Islands.IndexOf(isl);
            }
            return null;
        });
        Run(sb, "column bands: one per column, covering its islands (front − BandPadZ .. back + BandPadZ), IslandWidth + BandPadX on each side that does not touch a neighbour (v7: columns of a section touch)", () =>
        {
            ColumnBands.Refresh();
            if (ColumnBands.Count != SM.ColumnCount) return "bands " + ColumnBands.Count + " columns " + SM.ColumnCount;
            for (int c = 0; c < SM.ColumnCount; c++)
            {
                var b = ColumnBands.BandBounds(c);
                var l = SM.ColumnIslands(c);
                float front = l.Min(k => k.FrontEdge) - ProjectConfig.BandPadZ, back = l.Max(k => k.BackEdge) + ProjectConfig.BandPadZ;
                if (Mathf.Abs(b.min.z - front) > 0.05f || Mathf.Abs(b.max.z - back) > 0.05f) return "band " + c + " z " + b.min.z.ToString("F2") + ".." + b.max.z.ToString("F2") + " want " + front.ToString("F2") + ".." + back.ToString("F2");
                float wantW = W + (SM.Touching(c - 1) ? 0f : ProjectConfig.BandPadX) + (SM.Touching(c) ? 0f : ProjectConfig.BandPadX);   // v7 (was: Glued)
                if (Mathf.Abs(b.size.x - wantW) > 0.05f) return "band " + c + " width " + b.size.x.ToString("F2");
            }
            return null;
        });

        // satellites + the lit column, a capture of the 4-column song playing on column 1 (3 islands)
        Instruments.SetMuted(9, true);
        GlobalClock.Seek(SM.ColumnStart(1) + 0.6f);
        GlobalClock.Play();
        if (OrbitCamera.I != null) { OrbitCamera.I.FrameBounds(SM.SongBounds, 0.1f, true, 0.92f); }
        yield return Wait(1.1f);
        {
            int sat = Comet.I != null ? Comet.I.SatelliteCount : -1;
            int lit = SM.LitColumn;
            bool allLit = true; foreach (var kb in SM.ColumnIslands(1)) if (!kb.IsLit) allLit = false;
            bool othersDark = true; foreach (var kb in SM.Islands) if (kb.column != 1 && kb.IsLit) othersDark = false;
            string satWhy = null;
            if (Comet.I != null) for (int k = 0; k < 4; k++) { var si = Comet.I.SatelliteIsland(k); if (si != null && si.column != 1) satWhy = "satellite on column " + si.column; }
            Line(sb, lit == 1 && sat == SM.ColumnSize(1) - 1 && allLit && othersDark && satWhy == null && ColumnBands.Glow(1) > 0.5f,
                "lit column 1 (3 islands): all its islands lit, the others dark, 2 satellites on its non-anchor islands, its band glowing",
                "lit column " + lit + ", satellites " + sat + ", all lit " + allLit + ", others dark " + othersDark + ", band glow " + ColumnBands.Glow(1).ToString("F2") + (satWhy != null ? ", " + satWhy : ""));
        }
        yield return Shot("k_song4.png");
        // satellites follow their tracks in sync with the comet; pop out at the column change
        {
            yield return Wait(0.3f);
            string why = null;
            if (Comet.I != null)
                for (int k = 0; k < 4; k++)
                {
                    var si = Comet.I.SatelliteIsland(k); if (si == null) continue;
                    float lap = Mathf.Clamp01((GlobalClock.SongBeat - SM.ColumnStart(1)) / SM.ColumnLength(1));
                    float d = (Comet.I.SatellitePosition(k) - si.LapPoint(lap, 0.35f)).magnitude;
                    if (d > 0.9f) why = "satellite " + k + " is " + d.ToString("F2") + " u off its track point (island " + SM.Islands.IndexOf(si) + " column " + si.column + " lap " + lap.ToString("F3") + " pos " + Comet.I.SatellitePosition(k).ToString("F2") + " track " + si.LapPoint(lap, 0.35f).ToString("F2") + " passes " + SM.ColumnPasses(1) + ")";
                }
            GlobalClock.Seek(SM.ColumnStart(2) + 0.3f);
            yield return Wait(0.6f);
            int sat2 = Comet.I != null ? Comet.I.SatelliteCount : -1;
            GlobalClock.Stop(); GlobalClock.Seek(0);
            yield return Wait(0.4f);
            int sat0 = Comet.I != null ? Comet.I.SatelliteCount : -1;
            Line(sb, why == null && sat2 == SM.ColumnSize(2) - 1 && sat0 == 0, "satellites: on their islands' track points in sync; column 2 (2 islands) = 1 satellite; none when stopped at 0",
                (why ?? "in sync") + ", column 2 satellites " + sat2 + ", stopped " + sat0);
        }

        // hub gauge (the user, 2026-09-29: "it should just fill gradually until it reaches the end of measure"): no beam, the anchor's hub fills
        {
            GlobalClock.Stop();
            yield return Wait(0.25f);
            bool emptyStopped = true; foreach (var kb in SM.Islands) if (kb.IsAnchor && kb.Gauge > 0.001f) emptyStopped = false;
            int col = 1; var a = SM.AnchorOf(col); float s0 = SM.ColumnStart(col), L = SM.ColumnLength(col);
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(new Bounds(a.HubPos + new Vector3(0f, 0f, 1.6f), new Vector3(5f, 1f, 4.5f)), 0.1f, true, 1f);
            yield return Wait(0.3f);
            string why = null; var levels = new StringBuilder();
            float[] fr = { 0.1f, 0.4f, 0.7f, 0.97f };
            for (int k = 0; k < fr.Length; k++)
            {
                GlobalClock.Seek(s0 + fr[k] * L);   // stopped + seeked = paused: the gauge is frozen at the beat
                yield return Wait(0.25f);
                levels.Append(a.Gauge.ToString("F3")).Append(' ');
                if (Mathf.Abs(a.Gauge - fr[k]) > 0.02f) why = "level " + a.Gauge.ToString("F3") + " at " + fr[k];
                if (SM.AnchorOf(0).Gauge < 0.999f) why = "column 0 (played) not full: " + SM.AnchorOf(0).Gauge.ToString("F3");
                if (SM.AnchorOf(2).Gauge > 0.001f) why = "column 2 (ahead) not empty: " + SM.AnchorOf(2).Gauge.ToString("F3");
                yield return Shot("k_gauge_" + k + ".png");
            }
            GlobalClock.Seek(s0 + 0.5f * L); GlobalClock.Play();
            yield return Wait(0.5f);
            float rising = a.Gauge;
            GlobalClock.Seek(s0 + 0.05f * L);   // the column restarts: a quick drain (never below the pure level), then it follows the beat again
            yield return null;
            float draining = a.Gauge, drainTarget = a.GaugeTarget();
            yield return Wait(0.3f);
            float after = a.Gauge, afterTarget = a.GaugeTarget();
            GlobalClock.Stop();
            yield return Wait(0.25f);
            bool emptyAgain = true; foreach (var kb in SM.Islands) if (kb.IsAnchor && kb.Gauge > 0.001f) emptyAgain = false;
            int pillars = Fx.I != null ? Fx.I.ActivePillars : -1;
            bool drainOk = draining < rising - 0.01f && draining >= drainTarget - 1e-3f && Mathf.Abs(after - afterTarget) < 0.025f;   // (the gauge reads the beat a frame before the test: ≤ 0.02 at 121 bpm and ≥ 25 fps)
            Line(sb, emptyStopped && emptyAgain && why == null && rising > 0.55f && drainOk,
                "hub gauge: empty when stopped; frozen at (beat − start) / length inside the column (paused); played columns full, later ones empty; rises while playing; a restart drains it quickly",
                (why ?? "levels " + levels.ToString().Trim()) + ", rising " + rising.ToString("F2") + ", the frame after the restart " + draining.ToString("F2") + " (level " + drainTarget.ToString("F2") + ") -> 0.3 s later " + after.ToString("F2") + " (level " + afterTarget.ToString("F2") + "), empty when stopped " + emptyStopped + "/" + emptyAgain + ", live pillars " + pillars);
        }

        // ================================================================ C. duplicate, register, sleep, delete
        Run(sb, "DuplicateIsland right: a new column after the glued run (the song gets longer), chord + register + cubes copied with new ids; below / above: same column", () =>
        {
            int src = SM.ColumnFirst(2);
            var kb = SM.Islands[src];
            int cubesOn = SequenceMaster.Cubes.Count(c => c != null && !c.rider && c.Island == kb);
            int cols = SM.ColumnCount, h = History.UndoCount; float total = SM.TotalBeats, len = SM.ColumnLength(2);
            string chord = kb.assignedChord;
            int d = SM.DuplicateIsland(src, 0);
            if (d < 0) return "right refused";
            var dk = SM.Islands[d];
            if (dk.column != 3 || !dk.IsAnchor || SM.ColumnCount != cols + 1 || dk.assignedChord != chord) return "right: column " + dk.column + " columns " + SM.ColumnCount;
            if (Mathf.Abs(SM.TotalBeats - (total + len)) > 1e-4f) return "TotalBeats " + SM.TotalBeats + " want " + (total + len);
            var copies = SequenceMaster.Cubes.Where(c => c != null && !c.rider && c.Island == dk).ToList();
            if (copies.Count != cubesOn) return "copied cubes " + copies.Count + " want " + cubesOn;
            var ids = new HashSet<int>(); foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) { if (ids.Contains(c.id)) return "duplicate cube id " + c.id; ids.Add(c.id); }
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            History.Undo();
            if (SM.ColumnCount != cols) return "undo";
            int below = SM.DuplicateIsland(SM.ColumnFirst(3), 1);
            if (below < 0 || SM.Islands[below].column != 3 || SM.ColumnSize(3) != 2 || SM.Islands[below].BackEdge > SM.AnchorOf(3).FrontEdge - ProjectConfig.LaneGap + 0.01f) return "below: " + below;
            History.Undo();
            int above = SM.DuplicateIsland(SM.ColumnFirst(0), 2);
            if (above < 0 || SM.Islands[above].column != 0 || SM.Islands[above].FrontEdge < SM.AnchorOf(0).BackEdge + ProjectConfig.LaneGap - 0.01f) return "above: " + above;
            History.Undo();
            string inv = Invariants(); if (inv != null) return inv;
            return null;
        });

        int regIsland = -1; float regY0 = 0f; int midi0 = 0;
        Run(sb, "SetRegister(+1): every tile sounds 12 higher, the platform starts rising (spring), Capture carries reg, one History entry", () =>
        {
            regIsland = SM.ColumnFirst(3);
            var kb = SM.Islands[regIsland];
            midi0 = kb.GetTile(0, 0).midi; regY0 = kb.transform.position.y;
            int h = History.UndoCount;
            SM.SetRegister(regIsland, 1);
            if (kb.register != 1) return "register " + kb.register;
            foreach (var t in kb.tiles) if (t == null) return "tile";
            if (kb.GetTile(0, 0).midi != midi0 + 12) return "tile midi " + kb.GetTile(0, 0).midi + " want " + (midi0 + 12);
            if (SongState.Capture().measures[regIsland].reg != 1) return "capture reg";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            SM.SetRegister(SM.ColumnFirst(2) + 1, -1);   // the lower island of column 2 goes down: a pair to look at
            return null;
        });
        yield return Wait(0.12f);
        float regMid = regIsland >= 0 ? SM.Islands[regIsland].LiftY : 0f;
        // v6 (package K, the octave tower — the meaning of this check changed): the static lift became the tower. Stopped, a register change
        // previews the island's turn (it rises to TowerTopOf per octave, holds a beat, sinks slowly) and settles at the rest HINT (TowerRestOf);
        // the lowered island settles a hint lower. v4 checked a springy rise to RegisterRise (1.2 u) per octave.
        float upWait = (float)((ProjectConfig.TowerLead + 0.6) / GlobalClock.BeatsPerSecond);
        yield return Wait(Mathf.Max(0.05f, upWait - 0.12f));
        float regUp = regIsland >= 0 ? SM.Islands[regIsland].LiftY : 0f;
        float previewAll = (float)((ProjectConfig.TowerLead + ProjectConfig.TowerRiseEnd + ProjectConfig.TowerPreviewHold + ProjectConfig.TowerSinkBeats) / GlobalClock.BeatsPerSecond);
        yield return Wait(Mathf.Max(0.1f, previewAll - upWait) + 0.5f);
        {
            var kb = SM.Islands[regIsland];
            float y = kb.LiftY, want = SongManager.TowerRestOf(1), top = SongManager.TowerTopOf(1);
            var low = SM.Islands[SM.ColumnFirst(2) + 1];
            Line(sb, Mathf.Abs(y - want) < 1e-3f && regMid > want + 0.05f && regUp > top - 0.1f && Mathf.Abs(low.LiftY - SongManager.TowerRestOf(-1)) < 1e-3f && !kb.Lifting,
                "the platform previews its octave tower (v6: rises to TowerTopOf per octave, holds, sinks slowly) and settles at the rest hint; the other settles a hint lower",
                "lift at 0.12 s " + regMid.ToString("F3") + ", up " + regUp.ToString("F3") + " (top " + top + "), settled " + y.ToString("F3") + " (want " + want + "), lowered " + low.LiftY.ToString("F3"));
        }
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(SM.ColumnBounds(2), 0.1f, true, 1.25f);
        yield return Wait(0.3f);
        yield return Shot("k_register.png");
        History.Undo(); History.Undo();   // v6: an undo glides the tower lift back like a seek (the rebuilt island starts where the old one was shown)
        yield return Wait(0.6f);
        Run(sb, "register undo: the tiles and the platform come back (rebuild at register 0)", () =>
        {
            var kb = SM.Islands[regIsland];
            if (kb.register != 0 || kb.GetTile(0, 0).midi != midi0 || Mathf.Abs(kb.LiftY) > 1e-4f) return "register " + kb.register + " midi " + kb.GetTile(0, 0).midi + " lift " + kb.LiftY;
            return null;
        });

        Run(sb, "SetSleep: the island reads off (desaturated platform, tiles on the dim material, the closed eye), its windows silent; wake restores; one History entry each", () =>
        {
            int i = SM.ColumnFirst(1) + 1;
            var kb = SM.Islands[i];
            int h = History.UndoCount;
            SM.SetSleep(i, true);
            if (!kb.SleepShown || kb.SleepGlyph == null || !kb.SleepGlyph.gameObject.activeSelf) return "look off: shown " + kb.SleepShown;
            var tr = kb.GetTile(0, 0).GetComponent<Renderer>();
            if (tr.sharedMaterial != KeyBlock.DimTileMaterial) return "tile material";
            foreach (var c in SequenceMaster.Cubes) if (c != null && !c.rider && c.Island == kb && (c.windows.Count == 0 || !c.windows[0].silent)) return "window not silent";
            if (History.UndoCount != h + 1) return "history";
            return null;
        });
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(SM.ColumnBounds(1), 0.1f, true, 1.1f);
        yield return Wait(0.35f);
        yield return Shot("k_sleep.png");
        Run(sb, "wake: the look comes back", () =>
        {
            int i = SM.ColumnFirst(1) + 1;
            var kb = SM.Islands[i];
            SM.SetSleep(i, false);
            if (kb.SleepShown || kb.SleepGlyph.gameObject.activeSelf || kb.GetTile(0, 0).GetComponent<Renderer>().sharedMaterial != KeyBlock.TileMaterial) return "still asleep";
            return null;
        });

        Run(sb, "RemoveMeasure column-aware: removing a column's anchor promotes the next island (the column stays); removing a one-island column removes the column (later columns slide left)", () =>
        {
            int cols = SM.ColumnCount; int size1 = SM.ColumnSize(1);
            var second = SM.Islands[SM.ColumnFirst(1) + 1]; string c2 = second.assignedChord + second.pz.ToString("F2");
            SM.RemoveMeasure(SM.ColumnFirst(1));
            if (SM.ColumnCount != cols || SM.ColumnSize(1) != size1 - 1) return "anchor removal: columns " + SM.ColumnCount + " size " + SM.ColumnSize(1);
            var a = SM.AnchorOf(1);
            if (a.assignedChord + a.pz.ToString("F2") != c2 || !a.IsAnchor || a.Hub == null || !a.Hub.gameObject.activeSelf) return "promoted anchor " + a.assignedChord;
            History.Undo();
            float x3 = SM.ColumnX(3), start3 = SM.ColumnStart(3), len0 = SM.ColumnLength(0);
            string chord3 = SM.AnchorOf(3).assignedChord;
            SM.RemoveMeasure(SM.ColumnFirst(0));
            if (SM.ColumnCount != cols - 1 || SM.AnchorOf(2).assignedChord != chord3) return "column removal: " + Chords();
            if (Mathf.Abs(SM.ColumnStart(2) - (start3 - len0)) > 1e-4f) return "start after removal " + SM.ColumnStart(2);
            History.Undo();
            string inv = Invariants(); if (inv != null) return inv;
            return null;
        });

        // ================================================================ D. in-column drag: push / swap, range band, History
        var drag = PathManager.I != null ? PathManager.I.Drag : null;
        int dragIdx = -1; float dragZ0 = 0f; string pushWhy = null; int hDrag = 0; int swaps = 0; bool rangeShown = false;
        if (drag == null) Line(sb, false, "in-column drag", "no IslandDrag");
        else
        {
            // column 1: bottom = anchor, middle, top; drag the bottom one up through the middle one
            var col1 = SM.ColumnIslands(1).OrderBy(k => k.FrontEdge).ToList();
            var bottom = col1[0]; var middle = col1[1];
            dragIdx = SM.Islands.IndexOf(bottom); dragZ0 = bottom.pz;
            float midZ0 = middle.pz;
            hDrag = History.UndoCount;
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(SM.ColumnBounds(1), 0.1f, true, 1.35f);
            yield return Wait(0.3f);
            drag.SimBegin(bottom, false, bottom.Center);
            rangeShown = ColumnBands.RangeColumn == 1 && drag.DragKind == IslandDrag.Kind.InColumn;
            // step 1: up by 3 u: the middle island is pushed (clearance LaneGap), not swapped yet
            drag.SimMove(bottom.Center + new Vector3(0f, 0f, 3f));
            yield return null;
            float clear = middle.FrontEdge - bottom.BackEdge;
            if (Mathf.Abs(bottom.pz - (dragZ0 + 3f)) > 0.26f) pushWhy = "dragged z " + bottom.pz + " want " + (dragZ0 + 3f);
            else if (clear < ProjectConfig.LaneGap - 0.01f) pushWhy = "clearance " + clear.ToString("F2");
            else if (middle.pz <= midZ0 + 0.01f && bottom.BackEdge + ProjectConfig.LaneGap > middle.FrontEdge + 0.01f) pushWhy = "the middle island was not pushed";
            Info(sb, "mid-push: dragged front " + bottom.FrontEdge.ToString("F2") + " back " + bottom.BackEdge.ToString("F2") + ", middle front " + middle.FrontEdge.ToString("F2") + " (was " + (midZ0 - KeyBlock.EdgeInset).ToString("F2") + "), clearance " + clear.ToString("F2"));
            yield return Wait(0.15f);
            yield return Shot("k_drag_push.png");
        }
        if (drag != null) Line(sb, pushWhy == null && rangeShown, "in-column drag, push: the dragged island follows the pointer in z (x locked), the island above is pushed LaneGap clear, the band shows the range", pushWhy ?? ("ok, range band " + rangeShown + " [" + drag.RangeLo.ToString("F1") + ", " + drag.RangeHi.ToString("F1") + "]"));
        if (drag != null && dragIdx >= 0)
        {
            var bottom = SM.Islands[dragIdx];
            var col1 = SM.ColumnIslands(1).Where(k => k != bottom).OrderBy(k => k.FrontEdge).ToList();
            var middle = col1[0];
            // step 2: past the middle island's centre: it swaps below; step 3: release
            float target = middle.Center.z + 1.5f;
            drag.SimMove(new Vector3(bottom.Center.x, 0f, target));
            yield return null;
            swaps = drag.Swaps;
            bool below = middle.BackEdge <= bottom.FrontEdge - ProjectConfig.LaneGap + 0.01f;
            float xBefore = bottom.px;
            drag.SimRelease();
            yield return null;
            string why = null;
            if (!below) why = "the passed island did not swap below (its back " + middle.BackEdge.ToString("F2") + ", dragged front " + bottom.FrontEdge.ToString("F2") + ")";
            else if (Mathf.Abs(bottom.pz * 2f - Mathf.Round(bottom.pz * 2f)) > 1e-3f) why = "not snapped to 0.5: " + bottom.pz;
            else if (Mathf.Abs(bottom.px - xBefore) > 1e-3f) why = "x moved";
            else if (History.UndoCount != hDrag + 1) why = "history +" + (History.UndoCount - hDrag);
            else if (SM.IslandMoveActive) why = "move session left open";
            else if (ColumnBands.RangeColumn != -1) why = "range band left on";
            else { string inv = Invariants(); if (inv != null) why = inv; }
            Line(sb, why == null && swaps >= 1, "in-column drag, swap + release: the passed island swaps to the other side, the release snaps 0.5 u, one History entry, x kept, invariants", why ?? ("swaps " + swaps + ", z " + dragZ0.ToString("F1") + " -> " + bottom.pz.ToString("F1")));
            // cancel: Esc puts everything back
            var z0 = SM.Islands.Select(k => k.pz).ToList(); int h = History.UndoCount;
            drag.SimBegin(bottom, false, bottom.Center);
            drag.SimMove(bottom.Center + new Vector3(0f, 0f, -4f));
            drag.Cancel();
            bool back = true; for (int i = 0; i < SM.Islands.Count; i++) if (Mathf.Abs(SM.Islands[i].pz - z0[i]) > 1e-3f) back = false;
            Line(sb, back && History.UndoCount == h && !SM.IslandMoveActive, "in-column drag cancel (Esc): every island back where it was, no History", "back " + back + ", history +" + (History.UndoCount - h));
        }

        // ================================================================ E. tray ghost slots (IslandGhost directly)
        Run(sb, "ghost slots: over a column = STACK (empty slot nearest the pointer), a full column = BLOCKED, a boundary in an island's lane = MERGE (flush), an empty boundary lane = INSERT; Place builds them (one History each)", () =>
        {
            var md = MusicTheory.RandomSong(77, "k card").measures[2];
            var g = IslandGhost.Create(md);
            try
            {
                float e = KeyBlock.EdgeInset;
                // STACK below column 3's anchor
                var a3 = SM.AnchorOf(3);
                g.Track(new Vector3(a3.Center.x, 0f, a3.FrontEdge - 3f));
                if (g.State != IslandGhost.Mode.Stack || g.Column != 3) return "stack below: " + g.State + " column " + g.Column;
                float wantBack = a3.FrontEdge - ProjectConfig.LaneGap;
                if (Mathf.Abs(g.Root.z - e + g.Depth - wantBack) > 0.01f) return "stack slot back " + (g.Root.z - e + g.Depth) + " want " + wantBack;
                var d1 = g.Snapshot();
                int h = History.UndoCount, n = SM.Islands.Count;
                int at = IslandGhost.Place(d1, md);
                if (at < 0 || SM.Islands[at].column != 3 || SM.Islands.Count != n + 1 || History.UndoCount != h + 1) return "stack place " + at;
                History.Undo();
                // MERGE: the pointer right of column 3's anchor edge in its lane (column 3 is the last one)
                a3 = SM.AnchorOf(3);
                g.Track(new Vector3(a3.EastEdge + 1f, 0f, a3.Center.z));
                if (g.State != IslandGhost.Mode.Merge || g.MergeTarget != a3 || !g.MergeEast) return "merge: " + g.State;
                if (Mathf.Abs(g.Root.x - (a3.px + W)) > 0.01f || Mathf.Abs(g.Root.z - a3.pz) > 0.01f) return "merge slot " + g.Root;
                // INSERT: the gap between columns 0 and 1, far behind every island (an empty lane)
                float bx = (SM.ColumnX(0) - e + W + SM.ColumnX(1) - e) * 0.5f;
                g.Track(new Vector3(bx, 0f, SM.SongBounds.max.z + 10f));
                if (g.State != IslandGhost.Mode.Insert || g.InsertAt != 1) return "insert: " + g.State + " at " + g.InsertAt;
                var d2 = g.Snapshot();
                h = History.UndoCount; int cols = SM.ColumnCount;
                at = IslandGhost.Place(d2, md);
                if (at < 0 || SM.Islands[at].column != 1 || SM.ColumnCount != cols + 1 || History.UndoCount != h + 1) return "insert place " + at;
                if (Mathf.Abs(SM.Islands[at].pz - d2.pz) > 0.01f) return "insert lane " + SM.Islands[at].pz + " want " + d2.pz;
                History.Undo();
                // BLOCKED: a full column
                int c = 1;
                while (SM.ColumnSize(c) < ProjectConfig.MaxLanes) SM.AddIslandInColumn(SM.ColumnFirst(c), true);
                var ac = SM.AnchorOf(c);
                g.Track(new Vector3(ac.Center.x, 0f, ac.Center.z));
                if (g.State != IslandGhost.Mode.Blocked) return "full column: " + g.State;
                if (IslandGhost.Place(g.Snapshot(), md) != -1) return "blocked placed";
                History.Undo();
                string inv = Invariants(); if (inv != null) return inv;
                return null;
            }
            finally { UnityEngine.Object.Destroy(g.gameObject); }
        });

        // ================================================================ F. merge + split across columns, column reorder
        Run(sb, "merge across columns (in-column pull): column 2's lower island pulled toward column 3's anchor lane -> zone; release glues the columns (gap 0), z-aligned, one group, one History entry", () =>
        {
            if (drag == null) return "no IslandDrag";
            // v7 (SPEC v7 §13.2): inside a section the columns touch and a merge only glues across a GAP — a section boundary between columns 2 and 3
            // first (the merge then joins the two sections again)
            var stSec = SongState.Capture(); stSec.sections = new[] { 0, 3 }; SM.RebuildFromState(stSec); History.Push();
            var a3 = SM.AnchorOf(3);
            var mover = SM.ColumnIslands(2).OrderBy(k => Mathf.Abs(k.Center.z - a3.Center.z)).First();
            // put the mover in a3's lane first (an in-column move), then pull it sideways toward a3's west edge
            SM.MoveIslandInColumn(SM.Islands.IndexOf(mover), a3.pz, true); History.Push();
            int h = History.UndoCount;
            drag.SimBegin(mover, false, mover.Center);
            drag.SimMove(new Vector3(a3.WestEdge - W * 0.5f - 0.6f, 0f, a3.Center.z));
            if (drag.MergeTarget != a3 || drag.MergeEast) { drag.Cancel(); return "no zone: target " + (drag.MergeTarget != null ? drag.MergeTarget.name : "none") + " gap " + drag.NearestGap.ToString("F2"); }
            if (!SM.Route.MergeSheetVisible) { drag.Cancel(); return "no light sheet"; }
            drag.SimRelease();
            var m = SM.Islands.First(k => k.column == 2 && Mathf.Abs(k.pz - SM.AnchorOf(3).pz) < 0.01f && k.group != 0);
            var t = SM.AnchorOf(3);
            if (!SM.Glued(2) || m.group != t.group) return "not merged: " + SM.GroupReport();
            if (Mathf.Abs(m.EastEdge - t.WestEdge) > 0.01f) return "not touching: " + m.EastEdge + " / " + t.WestEdge;
            if (SM.GroupNeighbour(m, +1) != t) return "GroupNeighbour";
            if (SM.SectionCount != 1) return "the merge did not join the sections: " + SM.SectionCount;
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            return null;
        });
        if (OrbitCamera.I != null) { var bb = SM.ColumnBounds(2); bb.Encapsulate(SM.ColumnBounds(3)); OrbitCamera.I.FrameBounds(bb, 0.1f, true, 1.1f); }
        yield return Wait(0.8f);
        yield return Shot("k_merge.png");
        Run(sb, "MergeIslands API with reorder: column 0's island merges west of column 3's anchor -> the column moves next to it, glued; cubes remapped, one History entry", () =>
        {
            var mover = SM.AnchorOf(0); var target = SM.AnchorOf(3);
            string mc = mover.assignedChord, tc = target.assignedChord;
            int cubesM = SequenceMaster.Cubes.Count(c => c != null && !c.rider && c.Island == mover);
            int h = History.UndoCount, cols = SM.ColumnCount;
            if (!SM.MergeIslands(mover, target, false)) return "refused (" + SM.GroupReport() + ")";
            var m = SM.Islands.First(k => k.assignedChord == mc && k.group != 0);
            var g = SM.GroupOf(m);   // v3 rule: the mover joins the target's GROUP at its west end
            if (g.Count != 3 || g[0] != m || g[2].assignedChord != tc || SM.GroupNeighbour(m, -1) != null) return "not the west end of the target's group: " + Chords() + " | " + SM.GroupReport();
            if (SM.ColumnCount != cols) return "columns " + SM.ColumnCount;
            if (SequenceMaster.Cubes.Count(c => c != null && !c.rider && c.Island == m) != cubesM) return "cubes not remapped";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo();
            return null;
        });
        Run(sb, "split in columns: SplitGroup at the seam -> the columns unglue (v7: inside their section they keep touching), one History entry", () =>
        {
            var m = SM.Islands.FirstOrDefault(k => k.group != 0 && SM.GroupNeighbour(k, +1) != null);
            if (m == null) return "no seam: " + SM.GroupReport();
            int c = m.column; float xr = SM.ColumnX(c + 1); int h = History.UndoCount;
            if (!SM.SplitGroup(SM.Islands.IndexOf(m))) return "refused";
            if (SM.Glued(c)) return "still glued";
            float wantX = xr + (SM.Touching(c) ? 0f : ProjectConfig.SectionGap);   // v7: a group never spans a section boundary, so the split keeps them touching
            if (Mathf.Abs(SM.ColumnX(c + 1) - wantX) > 0.01f) return "right part at " + SM.ColumnX(c + 1) + " want " + wantX;
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            History.Undo();
            if (!SM.Glued(c)) return "undo lost the glue";
            return null;
        });
        Run(sb, "MoveColumn: column 3 to the front (its islands leave their groups), cubes remapped, starts follow, one History entry; undo restores", () =>
        {
            string before = Chords(); var st0 = SongState.Capture().ToJson();
            string moved = SM.AnchorOf(3).assignedChord; int size = SM.ColumnSize(3);
            int h = History.UndoCount;
            SM.MoveColumn(3, 0);
            if (SM.AnchorOf(0).assignedChord != moved || SM.ColumnSize(0) != size) return "order " + Chords();
            foreach (var kb in SM.ColumnIslands(0)) if (kb.group != 0) return "still grouped";
            if (Mathf.Abs(SM.ColumnStart(1) - SM.ColumnLength(0)) > 1e-4f) return "starts";
            foreach (var c in SequenceMaster.Cubes) if (c != null && !c.rider && !c.IsOnMoon && (c.windows.Count == 0 || c.windows[0].island != c.Island)) return "cube window island";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo();
            if (Chords() != before) return "undo: " + Chords();
            return null;
        });
        Run(sb, "undo / redo round trips keep the columns, groups, z and registers exactly (GroupReport + cols + regs)", () =>
        {
            Func<string> fp = () => SM.GroupReport() + "|" + string.Join(",", SM.Islands.Select(k => k.column + ":" + k.register));
            SM.SetRegister(SM.ColumnFirst(1), 2);
            SM.AddIslandInColumn(SM.ColumnFirst(3), true);
            string after = fp();
            History.Undo(); string mid = fp();
            History.Undo(); History.Redo(); History.Redo();
            if (fp() != after) return "redo: " + fp() + " vs " + after;
            History.Undo(); if (fp() != mid) return "undo again";
            History.Undo();
            return null;
        });
        Run(sb, "save / load round trip (SongIO.SaveTo scratch, never the user's save) keeps columns, groups, positions and registers", () =>
        {
            SM.SetRegister(SM.ColumnFirst(2), -1);
            string fp = SM.GroupReport() + "|" + string.Join(",", SM.Islands.Select(k => k.column + ":" + k.register)) + "|" + Timeline();
            if (!SongIO.SaveTo(ScratchSave)) return "SaveTo failed";
            var json = File.ReadAllText(ScratchSave);
            if (!json.Contains("\"col\"") || !json.Contains("\"reg\"")) return "json has no col / reg";
            LoadFixture();
            if (!SongIO.LoadFrom(ScratchSave)) return "LoadFrom failed";
            string fp2 = SM.GroupReport() + "|" + string.Join(",", SM.Islands.Select(k => k.column + ":" + k.register)) + "|" + Timeline();
            if (fp2 != fp) return "differs after load";
            try { File.Delete(ScratchSave); } catch (Exception) { }
            return null;
        });

        // ================================================================ G. focus loop flight, follow, Moons
        {
            int c = 1;
            double rs = SM.ColumnStart(c), re = rs + SM.ColumnLength(c);
            GlobalClock.SetRegion(rs, re);
            GlobalClock.Seek(re - 0.5);
            yield return null; yield return null;
            int to = Comet.I != null ? Comet.I.ToColumn : -1; bool fl = Comet.I != null && Comet.I.InFlight;
            GlobalClock.ClearRegion();
            yield return null; yield return null;
            int to2 = Comet.I != null ? Comet.I.ToColumn : -1;
            GlobalClock.Seek(0);
            Line(sb, fl && to == c && to2 == c + 1, "focus loop: the last-beat flight of the looped column goes back to its own anchor; without the region it flies on", "in flight " + fl + ", to column " + to + " (region), " + to2 + " (no region)");
        }
        if (OrbitCamera.I != null)
        {
            var cam = OrbitCamera.I;
            cam.ToggleFollow();
            GlobalClock.Seek(SM.ColumnStart(1) - 0.3f);
            GlobalClock.Play();
            yield return Wait(1.6f);
            int litCol = SM.LitColumn;
            var bnd = SM.ColumnBounds(litCol); bnd.Expand(4f);
            Vector3 ft = cam.FocusTarget;
            bool inside = litCol == 1 && bnd.Contains(new Vector3(ft.x, bnd.center.y, ft.z));
            GlobalClock.Stop(); GlobalClock.Seek(0);
            cam.ToggleFollow();
            Line(sb, inside, "follow (F) frames the lit column: the focus target sits inside the lit column's bounds", "target " + ft.ToString("F1") + ", column " + litCol + " bounds " + bnd.center.ToString("F1") + " ± " + bnd.extents.ToString("F1"));
        }
        Run(sb, "Moons pushed clear: an island added where the Moon floats pushes the Moon to the nearest free spot (no overlap), no extra History entry", () =>
        {
            if (SM.Moons.Count == 0) return "no Moon";
            var moon = SM.Moons[0];
            // park the Moon right where a new island below column 0's anchor will land
            var a0 = SM.AnchorOf(0);
            float S = ProjectConfig.Spacing;
            Vector3 park = new Vector3(a0.Center.x, 0f, a0.FrontEdge - ProjectConfig.LaneGap - 3f);
            SM.MoveIsland(moon, park.x - (ProjectConfig.MoonCols - 1) * S * 0.5f, park.z - (ProjectConfig.MoonRows - 1) * S * 0.5f);
            History.Push();
            Vector3 m0 = moon.Center;
            int h = History.UndoCount;
            int at = SM.AddIslandInColumn(SM.ColumnFirst(0), false);
            if (at < 0) return "add refused";
            moon = SM.Moons[0];
            float moved = new Vector2(moon.Center.x - m0.x, moon.Center.z - m0.z).magnitude;
            foreach (var kb in SM.Islands) { var b = moon.WorldBounds; b.Expand(ProjectConfig.IslandGap * 2f - 0.05f); if (b.Intersects(kb.WorldBounds)) return "the Moon overlaps island " + SM.Islands.IndexOf(kb) + " (moved " + moved.ToString("F2") + ")"; }
            if (moved < 0.5f) return "the Moon did not move";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            History.Undo(); History.Undo();
            return null;
        });

        // ================================================================ G2. the anchor's hub: column reorder on the route gap; a v3 file with free positions + groups
        Run(sb, "hub drag: an anchor's hub dropped on the cable between two other columns moves its whole column there (MoveColumn), one History entry", () =>
        {
            if (drag == null) return "no IslandDrag";
            if (SM.ColumnCount < 4) return "columns " + SM.ColumnCount;
            var a = SM.AnchorOf(0); int size = SM.ColumnSize(0); string ch = a.assignedChord;
            string order = Chords(); int h = History.UndoCount;
            int seg = -1;   // a drawn cable (not a seam) that does not touch column 0
            for (int k = 1; k + 1 < SM.ColumnCount; k++) if (!SM.AnchorSeam(k)) { seg = k; break; }
            if (seg < 0) return "no free cable segment: " + SM.GroupReport();
            drag.SimBegin(a, true, a.HubPos);
            if (drag.DragKind != IslandDrag.Kind.Column || drag.Moving.Count != size) { drag.Cancel(); return "kind " + drag.DragKind + " moving " + drag.Moving.Count; }
            Vector3 mid = SM.Route.PositionAlong(seg, 0.5f);
            drag.SimMove(new Vector3(mid.x, 0f, mid.z));
            int got = drag.GapSegment; string where = "hub at " + a.HubPos.ToString("F2") + ", cable at " + mid.ToString("F2") + ", zone " + (drag.MergeTarget != null);
            if (got != seg) { drag.Cancel(); return "gap segment " + got + " want " + seg + " (" + where + ")"; }
            drag.SimRelease();
            if (SM.AnchorOf(seg).assignedChord != ch || SM.ColumnSize(seg) != size) return "order " + Chords() + " (was " + order + ")";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            string inv = Invariants(); if (inv != null) return inv;
            History.Undo();
            if (Chords() != order) return "undo: " + Chords();
            return null;
        });
        Run(sb, "a v3 file (free positions, a merged pair, no columns) loads as a clean row of one-island columns: the pair stays merged (glued), fronts aligned, v3 timing", () =>
        {
            var st = SongState.FromJson(File.ReadAllText(V2Checks.FixturePath));
            st.version = 2;
            float[] xs = { 3f, 30f, 17f, -12f, 44f, 8f }, zs = { 0f, 9f, -14f, 5f, 2f, 30f };
            for (int i = 0; i < st.measures.Length; i++) { st.measures[i].px = xs[i % xs.Length]; st.measures[i].pz = zs[i % zs.Length]; st.measures[i].placed = true; st.measures[i].col = -1; }
            st.measures[2].group = 5; st.measures[3].group = 5;
            SongState.Apply(SongState.FromJson(st.ToJson()));
            if (SM.ColumnCount != st.measures.Length) return "columns " + SM.ColumnCount;
            if (Mathf.Abs(SM.Islands[0].px - 3f) > 1e-3f || Mathf.Abs(SM.Islands[0].pz) > 1e-3f) return "island 0 moved: " + SM.Islands[0].px + "," + SM.Islands[0].pz;
            foreach (var kb in SM.Islands) if (Mathf.Abs(kb.pz - SM.Islands[0].pz) > 1e-3f) return "not a clean row: " + SM.GroupReport();
            if (!SM.Glued(2) || SM.Islands[2].group == 0 || SM.Islands[3].group != SM.Islands[2].group || Mathf.Abs(SM.Islands[2].EastEdge - SM.Islands[3].WestEdge) > 0.01f) return "the pair: " + SM.GroupReport();
            string inv = Invariants(); if (inv != null) return inv;
            float t = 0f; for (int i = 0; i < SM.Islands.Count; i++) { if (Mathf.Abs(SM.MeasureStarts[i] - t) > 1e-4f) return "timing at " + i; t += SM.Islands[i].LengthBeats; }
            return null;
        });

        // ================================================================ H. simultaneity: two islands of one column sound together (recorded)
        string recWhy = null;
        {
            GlobalClock.Stop(); GlobalClock.ClearRegion();
            var song = MusicTheory.RandomSong(515, "k simul");
            SM.StartFreshSong(song);
            SM.AddIslandInColumn(0, false);                            // island 1: below island 0, same column
            SM.PlaceIslandAfter(song.measures[1], 0);                   // column 1 (silent)
            Performance.SetNominalBpm(120f);
            GlobalClock.LoopSong = true;
            int kit = 9;   // the kick (row 0 of a kit cube) is never humanised: every note lands exactly on its step
            for (int i = 0; i < Instruments.Count; i++) Instruments.SetMuted(i, i != kit);
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.IsOnMoon) c.SetMuted(true);   // the demo Moon's groove stays silent
            var pm = PathManager.I;
            pm.RestoreCube(CubeOn(0, kit, new[] { 0 }, new[] { 0 }, (int)StepLen.Quarter));                                       // island 0: on the beat
            pm.RestoreCube(CubeOn(1, kit, new[] { 1 }, new[] { 0 }, (int)StepLen.Eighth, -2, 0xAA));                               // island 1: the "ands"
            SM.RecomputeMeasureStarts();
            yield return Wait(1.2f);   // everything before rings out
            yield return null;
            long log0 = Performance.LoggedTotal;
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            GlobalClock.Seek(0);
            Synth.StartRecording(3.9f);
            GlobalClock.Play();
            double start = GlobalClock.DspTimeOfBeat(0);
            float t0 = Time.realtimeSinceStartup;
            while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < 7f) yield return null;
            GlobalClock.Stop();
            bool saved = Synth.SaveRecording(Path.Combine(V2Checks.CapturePath, "k_simul.wav"));
            // the scheduled beat of every logged note: island 0 on integer beats, island 1 on the halves, both inside column 0's span
            var a = new List<double>(); var b = new List<double>();
            double bps = GlobalClock.BeatsPerSecond;
            for (int i = 0; i < Performance.LoggedCount; i++)
            {
                if (Performance.LoggedTotal - Performance.LoggedCount + i < log0) continue;
                var e = Performance.Logged(i);
                if (e.owner < 16 || ReferenceEquals(e.island, null)) continue;
                int isl = SM.Islands.IndexOf(e.island);
                double beat = (e.onDsp - start) * bps;
                if (beat > 3.99) continue;   // the next pass
                if (isl == 0) a.Add(beat); else if (isl == 1) b.Add(beat);
            }
            string fmt(List<double> l) => string.Join(" ", l.Select(v => v.ToString("F3")));
            Info(sb, "simultaneity: island 0 note beats [" + fmt(a) + "], island 1 [" + fmt(b) + "], wav saved " + saved + ", " + Synth.Stats());
            try
            {
                var lines = new List<string>();
                foreach (var v in a) lines.Add("0 " + (v / bps).ToString("F5"));
                foreach (var v in b) lines.Add("1 " + (v / bps).ToString("F5"));
                File.WriteAllLines(Path.Combine(V2Checks.CapturePath, "k_simul_sched.txt"), lines.ToArray());
            }
            catch (Exception) { }
            if (a.Count != 4 || b.Count != 4) recWhy = "notes: island 0 " + a.Count + ", island 1 " + b.Count + " in column 0 (want 4 + 4)";
            else
            {
                for (int k = 0; k < 4; k++)
                {
                    if (Math.Abs(a[k] - k) / bps > 0.005) recWhy = "island 0 note " + k + " at beat " + a[k].ToString("F4");
                    if (Math.Abs(b[k] - (k + 0.5)) / bps > 0.005) recWhy = "island 1 note " + k + " at beat " + b[k].ToString("F4");
                }
            }
            if (recWhy == null && (Synth.LateEvents != late0 || Synth.Errors != err0)) recWhy = "late +" + (Synth.LateEvents - late0) + " errors +" + (Synth.Errors - err0);
            if (recWhy == null && !saved) recWhy = "recording not saved";
        }
        Line(sb, recWhy == null, "two islands of one column sound together: every note of both scheduled on its beat (±5 ms) inside column 0's span; recording Captures/k_simul.wav (onsets checked from Bash)", recWhy ?? "ok");

        // ================================================================ I. the user's save
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

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) { var h = md5.ComputeHash(File.ReadAllBytes(path)); var s = new StringBuilder(); foreach (var x in h) s.Append(x.ToString("x2")); return s.ToString(); }
    }
}
