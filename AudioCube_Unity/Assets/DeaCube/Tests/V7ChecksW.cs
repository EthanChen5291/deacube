using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package W (v7 world magic, SPEC v7 §6 + §12.3 / §13.3 / §13.5 / §16.2) Play-mode verification: <c>V7ChecksW.RunAll()</c> starts a coroutine;
/// poll <c>V7ChecksW.Done</c> or Captures/w7_report.txt (numbered PASS/FAIL lines, INFO lines with numbers). On the v1 fixture, through the real
/// v7 ops where they exist (SongManager.AddStairIsland / SetStair / SetRepeat / SetRewind / SetVary / SetCarry(at, n, 1) / SetLaunch,
/// CubeOps.OctaveCopy, Magic.Flip, GridSelection) and SongState edits where one is still a stub: the terraces (only where the ground is not 0,
/// top = the platform underside ± 0.1, following a glide, down → a low shelf, up → a mesa), the stairs FX (steps lit, dust, speed lines on the
/// fall), the echo layers (planes only where layer cubes live, a glass tile per grid tile, threads, phase-ins, link threads that pulse), the
/// rewind (the clock, the reversed beads, the boundary flash, varied-note sparkles), the flow ribbon (segments = flow targets + 1, the unroll
/// spell, the sweep, the roll-back, no v6 bridge), the fling (the flex's dip and kick, the landing, the target's tile wave), the flip mirror,
/// [§21 replaced the last two: the LONG GRID's light while its cubes walk (no bridge, no carpet), the LAUNCH build-up (the rim brightening through
/// A's riser, sparkles on the target's first measure at the crash, nothing moves), NOTHING FLIES over a whole song (no bead flight / trail /
/// bridge; no cube's body leaves its long grid's footprint) and Magic.Appear,]
/// the selection (outline, lift, shimmer, drop), the section glow, the taller tower (top = underside while up, quiet after its turn) and the
/// per-effect cost (≤ 1 ms each); no console errors. Captures w7_*.png of every look from the home camera angle. Runs with
/// PathManager.AutoHand = false; never writes the user's save.
/// </summary>
public static class V7ChecksW
{
    public static string Report = "";
    public static bool Done;
    static int num;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "w7_report.txt");
    static readonly List<string> errors = new List<string>();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" W7-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }

    static SongManager SM => SongManager.I;
    static WorldMagic WM => WorldMagic.I;

    public static string RunAll()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(Guard(AllRoutine("all")));
        return "started";
    }

    /// <summary>One part only (look iteration): "stairs", "layers", "rewind", "longgrid", "launch", "noflight", "appear", "flip", "select", "tower",
    /// "cost".</summary>
    public static string RunPart(string which)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(Guard(AllRoutine(which ?? "all")));
        return "started";
    }

    static IEnumerator Guard(IEnumerator body)
    {
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Report += "FAIL W7-EX " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0] + "\n"; break; }
            yield return cur;
        }
        Cleanup();
        Done = true;
        try { File.WriteAllText(ReportPath, Report); } catch (Exception) { }
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg + " @ " + (stack ?? "").Split('\n')[0]);
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        Tower.ForceState = -1;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        Application.runInBackground = true;
    }

    static void Cleanup()
    {
        Application.logMessageReceived -= OnLog;
        Tower.ForceState = -1;
        try { GridSelection.Clear(); } catch (Exception) { }
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        GridSelection.Clear();
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

    static void Home(Bounds b) { var oc = OrbitCamera.I; if (oc != null) oc.FrameStart(b); }
    static Bounds BoundsOf(IList<KeyBlock> kbs)
    {
        var b = new Bounds();
        bool first = true;
        foreach (var kb in kbs) { if (kb == null) continue; var w = kb.VisualBounds; if (first) { b = w; first = false; } else b.Encapsulate(w); }
        return b;
    }
    static Bounds BoundsOf(params KeyBlock[] kbs) => BoundsOf((IList<KeyBlock>)kbs);

    static IEnumerator SeekPlay(double beat)
    {
        GlobalClock.Stop();
        yield return null;
        GlobalClock.Seek(Math.Max(0.0, beat));
        GlobalClock.Play();
        yield return null;
    }

    static IEnumerator WaitBeat(double beat, float timeout)
    {
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < beat && Time.realtimeSinceStartup - t0 < timeout) yield return null;
    }

    /// <summary>The first non-Moon island of column <paramref name="col"/> in lane order that is a chord island (else any).</summary>
    static KeyBlock ChordIslandOf(int col)
    {
        KeyBlock any = null;
        foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && kb.column == col) { if (kb.kind == 0) return kb; if (any == null) any = kb; }
        return any;
    }
    static List<KeyBlock> ColumnsFrom(int col)
    {
        var l = new List<KeyBlock>();
        foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && kb.column >= col) l.Add(kb);
        return l;
    }
    static AudioCube MelodyCube(KeyBlock kb, int minNodes)
    {
        AudioCube best = null;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.Island == kb && !c.IsOnMoon && !c.IsDrums && c.layer == 0 && c.nodes.Count >= minNodes && (best == null || c.nodes.Count > best.nodes.Count)) best = c;
        return best;
    }
    static KeyBlock IslandWithMelody(int minNodes, int avoidCol = -1)
    {
        foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && kb.kind == 0 && kb.column != avoidCol && MelodyCube(kb, minNodes) != null) return kb;
        return null;
    }

    // ================================================================== RunAll
    static IEnumerator AllRoutine(string which)
    {
        var sb = new StringBuilder();
        errors.Clear();
        Application.logMessageReceived += OnLog;
        Prepare();
        LoadFixture();
        yield return Wait(0.6f);
        bool all = which == "all";
        Info(sb, "fixture: " + SM.Islands.Count + " islands, " + SM.ColumnCount + " columns, " + SM.SectionCount + " sections, " + SequenceMaster.Cubes.Count + " cubes, bpm " + GlobalClock.BPM.ToString("F0"));
        if (all) Line(sb, WM != null && WM.Links != null && WM.Mirror != null && WM.Selection != null && WM.Glow != null && WM.Stairs != null, "WorldMagic boots with its v7 parts",
                      WM != null ? "terraces " + WM.TerraceCount + ", layers " + WM.LayerCount + ", long grids " + WM.LongGlowCount : "missing");
        if (all || which == "stairs") { yield return StairsChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "layers") { yield return LayerChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "rewind") { yield return RewindChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "flow" || which == "longgrid") { yield return LongGridChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "fling" || which == "launch") { yield return LaunchChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "noflight") { yield return NoFlightChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "appear") { yield return AppearChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "flip") { yield return FlipChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "select") { yield return SelectChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "tower") { yield return TowerChecks(sb); LoadFixture(); yield return Wait(0.4f); }
        if (all || which == "cost") { yield return CostChecks(sb); }
        GlobalClock.Stop();
        Line(sb, errors.Count == 0, "no console errors", errors.Count == 0 ? "0" : errors.Count + ": " + errors[0]);
        int pass = 0, fail = 0;
        foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
    }

    // ------------------------------------------------------------------ stairs: the steps' light, dust, speed lines + the terraces
    /// <summary>Adds a stairs island (a spark fall of 4 unless <paramref name="dir"/> = +1) to the column of <paramref name="anchor"/>; its index.</summary>
    static int AddStairs(KeyBlock anchor, int dir)
    {
        int at = SM.Islands.IndexOf(anchor);
        int idx = SM.AddStairIsland(at, false);
        if (idx < 0 || idx >= SM.Islands.Count || SM.Islands[idx] == null || !SM.Islands[idx].IsStairs) return -1;
        SM.SetStair(idx, 2, dir, 4, 12, 1);
        for (int i = 0; i < SM.Islands.Count; i++) if (SM.Islands[i] != null && SM.Islands[i].IsStairs) return i;
        return -1;
    }

    static IEnumerator StairsChecks(StringBuilder sb)
    {
        var c1 = ChordIslandOf(1);
        int st = AddStairs(c1, -1);
        yield return Wait(1.1f);   // the grounds glide (GroundGlideSeconds) and the terraces rise out of the sea
        if (st < 0) { Line(sb, false, "a stairs island (SongManager.AddStairIsland)", "refused / stub — O's op not landed"); yield break; }
        var stairs = SM.Islands[st];
        int col = stairs.column;
        float g2 = SM.GroundOf(col + 1);
        Info(sb, "stairs island " + st + " in column " + col + ": steps " + stairs.stairSteps + " dir " + stairs.stairDir + ", tiles " + stairs.tiles.Count + ", runners " + CountRunners(stairs) + "; GroundOf(" + (col + 1) + ") " + g2.ToString("F2"));
        // terraces: exactly the columns whose ground is not 0, each top on the underside of what stands there (the plinth block, else the grid)
        int want, got, stray, plinth; float maxErr, minGround;
        TerraceCensus(out want, out got, out stray, out plinth, out maxErr, out minGround);
        Line(sb, want > 0 && got == want && stray == 0 && maxErr <= 0.1f, "terraces only where the ground is not 0, top = the underside above ± 0.1",
             "columns with ground ≠ 0: " + want + ", terraces standing " + got + " (" + plinth + " under their section plinth's block), stray " + stray + ", max |top − underside| " + maxErr.ToString("F3") + ", lowest GroundY shown " + minGround.ToString("F2"));
        var after = ColumnsFrom(col);
        Home(BoundsOf(after.Count > 0 ? after : new List<KeyBlock> { stairs }));
        yield return Wait(0.5f);
        yield return Shot("w7_terrace_down.png");

        // the runner falls step by step: steps light, dust, speed lines on the fall
        int f0 = WM.Stairs.StepFlashes, d0 = WM.Stairs.DustPuffs, l0 = WM.Stairs.FallLines;
        double cs = SM.ColumnStart(col), ce = cs + SM.PassLength(col);
        Home(BoundsOf(stairs, ChordIslandOf(col + 1)));
        yield return Wait(0.3f);
        yield return SeekPlay(cs + Mathf.Max(0f, SM.PassLength(col) - 2.3f));
        bool shot = false; float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < ce + 0.3 && Time.realtimeSinceStartup - t0 < 8f)
        {
            if (!shot && WM.Stairs.StepFlashes - f0 >= 2 && WM.LivePuffs > 3) { shot = true; GlobalClock.Pause(); yield return Wait(0.05f); yield return Shot("w7_stairs_fall.png"); GlobalClock.Play(); }
            yield return null;
        }
        GlobalClock.Stop();
        int fl = WM.Stairs.StepFlashes - f0, du = WM.Stairs.DustPuffs - d0, li = WM.Stairs.FallLines - l0;
        Line(sb, fl >= Mathf.Min(3, stairs.stairSteps) && du > 0 && li >= 1, "a runner's fall: each step lights, dust flies, speed lines on the fall",
             "steps lit " + fl + " (steps " + stairs.stairSteps + "), dust puffs " + du + ", speed-line bursts " + li + ", captured " + shot);

        // an up stair: the grids after it climb onto mesas
        SM.SetStair(st, -1, 1, -1, -1, -1);
        yield return Wait(0.3f);
        var mid = WM.TerraceOf(ChordIslandOf(col + 1));
        float midTop = mid != null ? mid.TopY : float.NaN, midUnder = mid != null ? mid.UndersideY : float.NaN;
        Home(BoundsOf(ColumnsFrom(col)));
        yield return Shot("w7_terrace_glide.png");
        yield return Wait(1.0f);
        float maxGround;
        TerraceCensus(out want, out got, out stray, out plinth, out maxErr, out maxGround, true);
        int lowSeen = CountLowered();
        Line(sb, want > 0 && got == want && stray == 0 && maxErr <= 0.1f && lowSeen == 0 && SM.GroundOf(col + 1) > 1f, "an up stair: the grids after it stand on mesas",
             "GroundOf(" + (col + 1) + ") " + SM.GroundOf(col + 1).ToString("F2") + ", highest GroundY " + maxGround.ToString("F2") + ", terraces " + got + "/" + want + ", max |top − underside| " + maxErr.ToString("F3") +
             (mid != null ? ", mid-glide top " + midTop.ToString("F2") + " vs underside " + midUnder.ToString("F2") : ""));
        Home(BoundsOf(ColumnsFrom(col)));
        yield return Wait(0.4f);
        yield return Shot("w7_terrace_up.png");
        // back to the sea: the stairs removed → every terrace sinks away
        SM.SetStair(st, -1, -1, -1, -1, -1);
        yield return Wait(1.2f);
        Info(sb, "down again: terraces " + WM.TerraceCount + ", lowered " + CountLowered());
    }

    static int CountRunners(KeyBlock kb) { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) n++; return n; }

    /// <summary>Terraces vs grounds: columns that need one, terraces standing (presence 1), strays (a terrace where the ground is 0), terraces under a
    /// plinth block, the worst |top − the underside above it| and the lowest (or, <paramref name="highest"/>, the highest) GroundY shown.</summary>
    static void TerraceCensus(out int want, out int got, out int stray, out int plinth, out float maxErr, out float ground, bool highest = false)
    {
        want = got = stray = plinth = 0; maxErr = 0f; ground = 0f;
        for (int c = 0; c < SM.ColumnCount; c++)
        {
            bool needs = Mathf.Abs(SM.GroundOf(c)) > 0.01f;
            var tr = WM.TerraceOfColumn(c);
            var a = SM.AnchorOf(c);
            if (a != null) ground = highest ? Mathf.Max(ground, a.GroundY) : Mathf.Min(ground, a.GroundY);
            if (!needs) { if (tr != null && tr.Presence > 0.5f) stray++; continue; }
            want++;
            if (tr == null || tr.Presence < 0.99f) continue;
            got++;
            if (tr.UnderPlinth) plinth++;
            maxErr = Mathf.Max(maxErr, Mathf.Abs(tr.TopY - tr.UndersideY));
            // the underside it reports must be the real one: the plinth block's (its top − Thick) or the grid's own
            if (!tr.UnderPlinth && a != null) maxErr = Mathf.Max(maxErr, Mathf.Abs(tr.UndersideY - Terrace.GroundUnderside(a)));
        }
    }
    static int CountLowered() { int n = 0; foreach (var t in WM.Terraces) if (t != null && !t.Retiring && t.Lowered) n++; return n; }

    // ------------------------------------------------------------------ echo layers + links
    /// <summary>An octave copy of <paramref name="c"/> (H's CubeOps.OctaveCopy, else a SongState edit): the copy.</summary>
    static AudioCube Copy(AudioCube c, int dir, StringBuilder sb)
    {
        var made = CubeOps.OctaveCopy(c, dir);
        if (made != null) return made;
        // fallback while H's op is a stub: the same cube with layer ± 1 and echoOf through the song state
        var st = SongState.Capture();
        var list = new List<CubeState>(st.cubes);
        CubeState src = null;
        foreach (var cs in st.cubes) if (cs.id == c.id) { src = cs; break; }
        if (src == null) return null;
        var copy = CubeState.Clone(src);
        copy.layer = Mathf.Clamp(src.layer + dir, -ProjectConfig.MaxLayer, ProjectConfig.MaxLayer); copy.echoOf = src.id; copy.id = 0; copy.seed = 0;
        list.Add(copy);
        st.cubes = list.ToArray();
        SongState.Apply(st);
        Info(sb, "CubeOps.OctaveCopy returned null: the copy (layer " + copy.layer + ") was made through the song state");
        foreach (var o in SequenceMaster.Cubes) if (o != null && o.echoOf == c.id && o.layer == copy.layer) return o;
        return null;
    }

    static IEnumerator LayerChecks(StringBuilder sb)
    {
        var kb = IslandWithMelody(3);
        var src = kb != null ? MelodyCube(kb, 3) : null;
        if (src == null) { Line(sb, false, "echo layers", "no melody cube in the fixture"); yield break; }
        int srcId = src.id, kbIdx = SM.Islands.IndexOf(kb);
        var up = Copy(src, 1, sb);
        yield return Wait(0.2f);
        kb = SM.Islands[kbIdx];
        src = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == srcId) src = c;
        var down = src != null ? Copy(src, -1, sb) : null;
        yield return Wait(0.8f);
        kb = SM.Islands[kbIdx];
        var sky = WM.LayerOf(kb, 1); var deep = WM.LayerOf(kb, -1);
        int others = 0; foreach (var l in WM.Layers) if (l != null && !l.Retiring && l.Island != kb) others++;
        int distinct = 0; var seen = new HashSet<TileInteraction>();
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb && c.layer == 1) foreach (var t in c.nodes) if (t != null && seen.Add(t)) distinct++;
        Line(sb, sky != null && deep != null && others == 0 && sky.TileCount == kb.tiles.Count && deep.TileCount == kb.tiles.Count && sky.ThreadCount == distinct + 4,
             "layer planes only where layer cubes live: a sky and a deep glass copy of the grid",
             "island " + kbIdx + ": sky " + (sky != null ? "tiles " + sky.TileCount + " threads " + sky.ThreadCount + " (path tiles " + distinct + " + 4 corners) at Δy " + (sky.PlaneY - kb.transform.position.y).ToString("F2") : "none") +
             ", deep " + (deep != null ? "tiles " + deep.TileCount + " at Δy " + (deep.PlaneY - kb.transform.position.y).ToString("F2") : "none") + ", grid tiles " + kb.tiles.Count + ", layers elsewhere " + others);
        Line(sb, WM.Links.Count == 2, "link threads: each copy to its source", "links " + WM.Links.Count + " (copies up " + (up != null) + ", down " + (down != null) + ")");
        Home(BoundsOf(kb));
        yield return Wait(0.5f);
        yield return Shot("w7_layers_rest.png");
        // the deep band: in front of the platform's front edge, seen from the home camera (a ray to each glass tile hits no island first)
        if (deep != null)
        {
            int seen2, total; float nearest; string who; DeepVisibility(kb, deep, out seen2, out total, out nearest, out who);
            Line(sb, total > 0 && seen2 >= total * 0.75f && deep.BandBackZ < kb.FrontEdge && deep.PlaneY < kb.transform.position.y - 0.3f,
                 "the deep layer is a reflection band in front of and below the grid, in view of the home camera",
                 "glass tiles in view " + seen2 + " / " + total + (who.Length > 0 ? " (hidden behind: " + who + ")" : "") + ", band z " + deep.BandFrontZ.ToString("F2") + " .. " + deep.BandBackZ.ToString("F2") + " (platform front " + kb.FrontEdge.ToString("F2") + "), depth " + deep.BandDepth.ToString("F2") +
                 ", its y " + (deep.PlaneY - kb.transform.position.y).ToString("F2") + " under the grid's");
            var dc = down != null ? down : null;
            if (dc != null && dc.nodes.Count > 0 && dc.nodes[0] != null)
            {
                Vector3 lp = Magic.LayerPoint(dc.nodes[0], -1);
                Info(sb, "deep copy cube at " + dc.transform.position.ToString("F2") + " vs Magic.LayerPoint(node 0) " + lp.ToString("F2") + " (H rides it there once it uses LayerPoint)");
            }
        }
        // phase in: play the island's column; notes of the layer cubes flash their glass tiles and pulse the threads
        int ph0 = (sky != null ? sky.PhaseIns : 0) + (deep != null ? deep.PhaseIns : 0), tp0 = sky != null ? sky.ThreadPulses : 0, lp0 = WM.Links.Pulses;
        double cs = SM.ColumnStart(kb.column), ce = cs + SM.ColumnLength(kb.column);
        yield return SeekPlay(cs - 0.5);
        bool shot = false; float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < ce && Time.realtimeSinceStartup - t0 < 10f)
        {
            if (!shot && sky != null && sky.PhaseIns > ph0 + 1 && GlobalClock.SongBeatD > cs + 1.0) { shot = true; GlobalClock.Pause(); yield return Wait(0.06f); yield return Shot("w7_layers_phase.png"); GlobalClock.Play(); }
            yield return null;
        }
        GlobalClock.Stop();
        int ph = (sky != null ? sky.PhaseIns : 0) + (deep != null ? deep.PhaseIns : 0) - ph0;
        Line(sb, ph >= 2 && (sky == null || sky.ThreadPulses > tp0) && WM.Links.Pulses > lp0, "a layer cube's notes phase in: glass flash, sparkles / bubbles, a bead up the thread, the link pulses",
             "phase-ins " + ph + ", thread pulses " + (sky != null ? sky.ThreadPulses - tp0 : 0) + ", link pulses " + (WM.Links.Pulses - lp0) + ", bubbles out " + (deep != null ? deep.BubblesOut : 0) + ", captured " + shot);
        // the deep copy playing, from the default camera (its notes flash on the band, bubbles rise to the water-glass)
        Home(BoundsOf(kb));
        yield return Wait(0.3f);
        int dp0 = deep != null ? deep.PhaseIns : 0;
        yield return SeekPlay(cs - 0.5);
        bool deepShot = false; t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < ce && Time.realtimeSinceStartup - t0 < 10f)
        {
            if (!deepShot && deep != null && deep.PhaseIns > dp0 + 1 && GlobalClock.SongBeatD > cs + 1.5) { deepShot = true; GlobalClock.Pause(); yield return Wait(0.06f); yield return Shot("w7_deep_phase.png"); GlobalClock.Play(); }
            yield return null;
        }
        GlobalClock.Stop();
        yield return Wait(0.3f);
        yield return Shot("w7_deep_rest.png");
        Info(sb, "deep phase-ins while playing " + (deep != null ? deep.PhaseIns - dp0 : 0) + ", captured " + deepShot);
        // a lane in front (a keyboard added below the grid): the deep band shortens and never reaches it
        int kIdx = SM.AddKeyboardIsland(SM.Islands.IndexOf(kb), false);
        yield return Wait(0.8f);
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == srcId && c.Island != null) { kb = c.Island; break; }
        KeyBlock keys = null; foreach (var o in SM.Islands) if (o != null && o.IsKeyboard && o.column == kb.column) keys = o;
        deep = WM.LayerOf(kb, -1);
        if (keys != null && deep != null && keys.BackEdge <= kb.FrontEdge + 0.01f)
        {
            int seen3, total3; float n3; DeepVisibility(kb, deep, out seen3, out total3, out n3);
            Line(sb, deep.BandFrontZ > keys.BackEdge + 0.05f && seen3 >= total3 * 0.6f, "a lane in front: the deep band fits the gap and never reaches it",
                 "band z " + deep.BandFrontZ.ToString("F2") + " .. " + deep.BandBackZ.ToString("F2") + ", the keyboard's back edge " + keys.BackEdge.ToString("F2") + ", depth " + deep.BandDepth.ToString("F2") + ", glass tiles in view " + seen3 + " / " + total3);
            var bb = BoundsOf(kb, keys);
            Home(bb);
            yield return Wait(0.5f);
            yield return Shot("w7_deep_lanes.png");
        }
        else Info(sb, "multi-lane check skipped: keyboard " + (keys != null) + " (AddKeyboardIsland " + kIdx + "), deep " + (deep != null));
        // the copies go: the planes dissolve
        var st = SongState.Capture();
        var keep = new List<CubeState>();
        foreach (var cs2 in st.cubes) if (cs2.layer == 0) keep.Add(cs2);
        st.cubes = keep.ToArray();
        SongState.Apply(st);
        yield return Wait(0.9f);
        Line(sb, WM.LayerCount == 0 && WM.Layers.Count == 0 && WM.Links.Count == 0, "the last layer cube gone: the glass copies dissolve", "layers " + WM.Layers.Count + ", links " + WM.Links.Count);
    }

    /// <summary>How many of <paramref name="layer"/>'s glass tiles the home camera sees (a ray to the tile hits no collider — island, tile, cube —
    /// on the way), and the nearest blocker distance.</summary>
    static void DeepVisibility(KeyBlock kb, EchoLayer layer, out int seen, out int total, out float nearest) { string who; DeepVisibility(kb, layer, out seen, out total, out nearest, out who); }

    static void DeepVisibility(KeyBlock kb, EchoLayer layer, out int seen, out int total, out float nearest, out string blockers)
    {
        seen = 0; total = 0; nearest = float.MaxValue;
        var names = new Dictionary<string, int>();
        var cam = Camera.main;
        blockers = "";
        if (cam == null) return;
        foreach (var t in kb.tiles)
        {
            if (t == null) continue;
            total++;
            Vector3 p = Magic.LayerPoint(t, layer.Layer) + Vector3.up * 0.05f;
            Vector3 o = cam.transform.position, d = p - o;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(o, d.normalized, d.magnitude - 0.08f))
            {
                if (hit.collider.GetComponentInParent<AudioCube>() != null) continue;   // a cube standing on the band is not a blocker
                blocked = true; nearest = Mathf.Min(nearest, hit.distance);
                string n = hit.collider.name; int c; names.TryGetValue(n, out c); names[n] = c + 1;
                break;
            }
            if (blocked) continue;
            var vp = cam.WorldToViewportPoint(p);
            if (vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f) seen++;
        }
        foreach (var kv in names) blockers += kv.Key + " ×" + kv.Value + " ";
    }

    // ------------------------------------------------------------------ rewind + vary
    static IEnumerator RewindChecks(StringBuilder sb)
    {
        var kb = IslandWithMelody(3);
        if (kb == null) { Line(sb, false, "rewind", "no melody island"); yield break; }
        int at = SM.Islands.IndexOf(kb);
        SM.SetRepeat(at, 3);
        SM.SetRewind(at, true);
        SM.SetVary(at, 1);
        yield return Wait(0.6f);
        kb = SM.Islands[at];
        var rw = WM.RewindOf(kb);
        Line(sb, rw != null && kb.rewind && kb.Passes == 3 && kb.Belt == null, "a rewind island gets its time-unwinding look (and no belt)", rw != null ? "passes " + kb.Passes + ", belt " + (kb.Belt != null) + ", vary " + kb.vary : "none");
        if (rw == null) yield break;
        Home(BoundsOf(kb));
        yield return Wait(0.4f);
        int col = kb.column;
        double pl = SM.PassLength(col), cs = SM.ColumnStart(col);
        double boundary = cs + pl;
        int u0 = rw.Unwinds, fl0 = rw.Flashes, b0 = rw.BeadsLit, v0 = WM.VariedSparkles;
        float probeP; int probePass;
        probeP = RewindFx.PhaseOf(kb, boundary - ProjectConfig.RewindBeats * 0.5, out probePass);
        yield return SeekPlay(boundary - 1.5);
        bool midShot = false, flashShot = false; float clockMax = 0f; int litMax = 0; float gaugeAtStart = -1f, gaugeMin = 9f;
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < cs + pl * 3 - 0.2 && Time.realtimeSinceStartup - t0 < 16f)
        {
            clockMax = Mathf.Max(clockMax, rw.ClockShow); litMax = Mathf.Max(litMax, rw.LitNow);
            if (rw.Phase >= 0f && rw.Phase < 0.2f && gaugeAtStart < 0f) gaugeAtStart = kb.Gauge;
            if (rw.Phase >= 0.6f && gaugeAtStart >= 0f) gaugeMin = Mathf.Min(gaugeMin, kb.Gauge);
            if (!midShot && rw.Phase >= 0.45f && rw.Phase <= 0.8f) { midShot = true; GlobalClock.Pause(); yield return Wait(0.05f); yield return Shot("w7_rewind_mid.png"); GlobalClock.Play(); }
            if (!flashShot && rw.Flashes > fl0 && rw.FlashAlpha > 0.2f) { flashShot = true; yield return Shot("w7_rewind_flash.png"); }
            yield return null;
        }
        GlobalClock.Stop();
        Line(sb, rw.Unwinds - u0 >= 2 && rw.Flashes - fl0 >= 2 && clockMax > 0.9f && rw.BeadsLit - b0 > 0 && probeP >= 0f && probePass == 0,
             "time unwinds at each boundary: the clock whirls back, beads light last → first, a sepia flash",
             "unwinds " + (rw.Unwinds - u0) + ", flashes " + (rw.Flashes - fl0) + ", clock show max " + clockMax.ToString("F2") + ", beads lit " + (rw.BeadsLit - b0) + " (max at once " + litMax + "), streaks " + rw.Streaks +
             ", phase mid-unwind " + probeP.ToString("F2") + " (pass " + probePass + "), captures mid " + midShot + " flash " + flashShot);
        Info(sb, "K's gauge while time unwinds: " + gaugeAtStart.ToString("F2") + " at the start → " + (gaugeMin < 9f ? gaugeMin.ToString("F2") : "-") + " late in the unwind (drains back)");
        int vs = WM.VariedSparkles - v0;
        bool variedTiles = false;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.Island == kb)
                for (int w = 0; w < c.windows.Count && !variedTiles; w++)
                    if (c.windows[w].pass >= 1) for (int n = 0; n < c.nodes.Count; n++) if (c.TileAt(w, n) != c.nodes[n]) { variedTiles = true; break; }
        Line(sb, !variedTiles || vs > 0, "varied notes sparkle on their tile", "varied sparkles " + vs + (variedTiles ? "" : " (H's varied tiles not in: TileAt plays the first pass's tiles — nothing to sparkle)"));
    }

    // ------------------------------------------------------------------ the long grid (§21: carry = one long grid the cubes walk through)
    static IEnumerator LongGridChecks(StringBuilder sb)
    {
        var kb = ChordIslandOf(0);
        int at = SM.Islands.IndexOf(kb);
        SM.SetCarry(at, 2, 1);
        yield return Wait(0.6f);
        kb = SM.Islands[at];
        var g = WM.LongGlowOf(kb);
        var targets = SM.CarryTargets(at);
        bool order = g != null && g.TargetCount == targets.Count;
        for (int k = 0; order && k < targets.Count; k++) if (g.Target(k) != targets[k]) order = false;
        Line(sb, g != null && order && g.Segments == targets.Count + 1 && targets.Count == 2 && WM.BridgeOf(kb) == null && WM.BridgeCount == 0 && g.Show < 0.01f,
             "§21 extend ×2: one long grid (the source + 2 measures), no carry bridge, dark at rest",
             g != null ? "segments " + g.Segments + ", targets " + targets.Count + ", bridges " + WM.BridgeCount + ", light at rest " + g.Show.ToString("F2") : "none");
        if (g == null) yield break;
        var all = new List<KeyBlock> { kb }; all.AddRange(targets);
        Home(BoundsOf(all));
        yield return Wait(0.4f);
        // the walk: the rim lights while the long grid plays, a glow rides under each walking cube; nothing over a platform
        var t1 = targets[0];
        double cs = SM.ColumnStart(t1.column), pl = SM.PassLength(t1.column);
        int walks0 = g.Walks;
        yield return SeekPlay(SM.ColumnStart(kb.column) - 0.5);
        float maxShow = 0f, maxLag = 0f; int maxGlows = 0, over = 0; bool shot = false;
        double end = SM.ColumnStart(targets[targets.Count - 1].column) + SM.ColumnLength(targets[targets.Count - 1].column);
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < end + 1.5 && Time.realtimeSinceStartup - t0 < 14f)
        {
            maxShow = Mathf.Max(maxShow, g.Show); maxGlows = Mathf.Max(maxGlows, g.Glows); over = Mathf.Max(over, g.OverPlatform);
            if (g.Glows > 0) maxLag = Mathf.Max(maxLag, g.GlowLag);
            if (!shot && GlobalClock.SongBeatD > cs + pl * 0.45) { shot = true; GlobalClock.Pause(); yield return Wait(0.05f); yield return Shot("w7_longgrid.png"); GlobalClock.Play(); }
            yield return null;
        }
        float showAfter = g.Show;
        GlobalClock.Stop();
        Line(sb, maxShow > 0.95f && g.Walks - walks0 >= 1 && maxGlows >= 1 && maxLag < 0.05f && over == 0 && showAfter < 0.2f,
             "while the long grid plays its rim lights and a glow rides under each walking cube (never over a platform), dark again after",
             "light max " + maxShow.ToString("F2") + ", walks " + (g.Walks - walks0) + ", glows max " + maxGlows + ", glow-to-cube distance max " + maxLag.ToString("F3") + ", rim vertices over a platform " + over + ", light 1.5 beats after the run " + showAfter.ToString("F2") + ", captured " + shot);
        // selecting ONE member lifts the whole long grid as one: every joint stays joined through the lift and the landing (K's joined look is physical)
        var run = SM.LongGridOf(SM.Islands[at]);
        if (run != null && run.Count >= 2)
        {
            int open = 0, frames = 0, joints = 0; float spread = 0f, peak = 0f, before = 0f;
            foreach (var m in run) if (m != null && m.JoinedEast) joints++;
            KeyBlock pick = run[run.Count >= 3 ? 1 : 0];
            GridSelection.Set(new List<KeyBlock> { pick });
            float tl = Time.realtimeSinceStartup;
            bool cleared = false;
            while (Time.realtimeSinceStartup - tl < 1.4f)
            {
                if (!cleared && Time.realtimeSinceStartup - tl > 0.7f) { cleared = true; GridSelection.Clear(); }
                frames++;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < run.Count; k++)
                {
                    var m = run[k]; if (m == null) continue;
                    if (k + 1 < run.Count && !m.JoinedEast) open++;
                    if (k > 0 && !m.JoinedWest) open++;
                    lo = Mathf.Min(lo, m.FxLift); hi = Mathf.Max(hi, m.FxLift);
                }
                spread = Mathf.Max(spread, hi - lo); peak = Mathf.Max(peak, hi);
                yield return null;
            }
            Line(sb, joints == run.Count - 1 && open == 0 && spread < 1e-4f && peak > SelectionFx.Lift * 0.9f,
                 "§21 selecting one member lifts the whole long grid as one: every joint stays joined through the lift and the landing",
                 "members " + run.Count + ", joints joined at rest " + joints + ", open joint-frames " + open + " over " + frames + " frames, lift peak " + peak.ToString("F3") + ", members' lift spread " + spread.ToString("F4"));
        }
        else Info(sb, "lift-as-one check skipped: no K long grid (LongGridOf " + (run != null ? run.Count.ToString() : "null") + ")");
        // extend off: the light goes
        SM.SetCarry(at, 0, 1);
        yield return Wait(0.9f);
        Line(sb, WM.LongGlowOf(SM.Islands[at]) == null && WM.LongGlows.Count == 0, "extend off: the long grid's light goes", "long grids " + WM.LongGlows.Count);
    }

    // ------------------------------------------------------------------ the launch (§21.4: a build-up, no fling)
    static IEnumerator LaunchChecks(StringBuilder sb)
    {
        var kb = IslandWithMelody(2, SM.ColumnCount - 1);
        if (kb == null) kb = ChordIslandOf(0);
        int at = SM.Islands.IndexOf(kb);
        SM.SetLaunch(at, true);
        yield return Wait(0.3f);
        kb = SM.Islands[at];
        var fx = WM.LaunchOf(kb);
        var target = SM.LaunchTarget(kb);
        Line(sb, fx != null && target != null, "a launching island gets its build-up look", fx != null ? "target " + (target != null ? SM.Islands.IndexOf(target).ToString() : "none") + ", turn end " + SM.TurnEnd(kb).ToString("F2") + ", swell " + LaunchFx.SwellBeats.ToString("F2") + " beats (A's riser)" : "none");
        if (fx == null || target == null) yield break;
        Home(BoundsOf(kb, target));
        yield return Wait(0.5f);
        double L = SM.TurnEnd(kb), sbBeats = LaunchFx.SwellBeats, S = L - sbBeats;
        int sw0 = fx.Swells, cr0 = fx.Crashes, cs0 = fx.CrashSparkles;
        float lightMid = -1f, lightEnd = 0f, lightBefore = 0f; float yMin = float.MaxValue, yMax = float.MinValue; float liftMax = 0f, liftBefore = 0f, liftAfter = 0f, leanMax = 0f;
        Vector3 cube0 = Vector3.zero; AudioCube watch = MelodyCube(kb, 2); float cubeDrift = 0f;
        yield return SeekPlay(S - 1.0);
        bool shot = false;
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < L + 1.0 && Time.realtimeSinceStartup - t0 < 10f)
        {
            double b = GlobalClock.SongBeatD;
            if (b < S - 0.1) lightBefore = Mathf.Max(lightBefore, fx.RimLight);
            if (lightMid < 0f && fx.Swell >= 0.45f && fx.Swell <= 0.6f) lightMid = fx.RimLight;
            if (fx.Swell >= 0.9f) lightEnd = Mathf.Max(lightEnd, fx.RimLight);
            yMin = Mathf.Min(yMin, kb.transform.position.y); yMax = Mathf.Max(yMax, kb.transform.position.y);
            // v9 (R): the grid TENSES through the swell — a slight lift (≤ 0.2 u) and a lean back — only during the swell, 0 before it and after the landing
            if (b < S - 0.1) liftBefore = Mathf.Max(liftBefore, Mathf.Abs(kb.FxLift));
            else if (b >= L + 0.35) liftAfter = Mathf.Max(liftAfter, Mathf.Abs(kb.FxLift) + Quaternion.Angle(kb.transform.rotation, Quaternion.identity));
            else { liftMax = Mathf.Max(liftMax, Mathf.Abs(kb.FxLift)); leanMax = Mathf.Max(leanMax, Quaternion.Angle(kb.transform.rotation, Quaternion.identity)); }
            // the cubes do not move off the grid (they play their last notes in place)
            if (watch != null) { var vb = kb.VisualBounds; var p = watch.transform.position; cubeDrift = Mathf.Max(cubeDrift, Mathf.Max(0f, Mathf.Max(vb.min.x - p.x, p.x - vb.max.x))); }
            if (!shot && fx.Swell >= 0.7f) { shot = true; GlobalClock.Pause(); yield return Wait(0.05f); yield return Shot("w7_launch_buildup.png"); GlobalClock.Play(); }
            yield return null;
        }
        GlobalClock.Stop();
        yield return Wait(0.3f);
        Line(sb, fx.Swells - sw0 >= 1 && lightBefore < 0.01f && lightMid > 0.1f && lightMid < 0.5f && lightEnd > 0.75f && liftBefore < 1e-4f && liftMax > 0.02f && liftMax <= 0.2f && leanMax > 0.5f && liftAfter < 1e-3f && cubeDrift < 0.3f,
             "§21.4 through A's riser the launching grid's rim brightens like the swell; the grid tenses (a lift ≤ 0.2 u and a lean back, only during the swell, upright again at the landing) and its cubes stay on it",
             "swells " + (fx.Swells - sw0) + ", light before " + lightBefore.ToString("F2") + " / mid-swell " + lightMid.ToString("F2") + " / at the top " + lightEnd.ToString("F2") + ", platform y range " + (yMax - yMin).ToString("F3") + ", FxLift before / in the swell / after " + liftBefore.ToString("F3") + " / " + liftMax.ToString("F3") + " / " + liftAfter.ToString("F3") + ", lean max " + leanMax.ToString("F1") + "°, a cube's x past its grid " + cubeDrift.ToString("F2") + ", captured " + shot);
        Line(sb, fx.Crashes - cr0 == 1 && fx.CrashSparkles - cs0 >= 6 && fx.LastTarget == target, "at the crash a few sparkles burst on the target's first measure",
             "crash bursts " + (fx.Crashes - cr0) + ", sparkles " + (fx.CrashSparkles - cs0) + ", on island " + SM.Islands.IndexOf(fx.LastTarget) + "; Magic.Fling calls " + WM.FlingCalls + " (retired: draws nothing)");
    }

    // ------------------------------------------------------------------ §21: nothing flies over a whole song (carry ×3, a launch, pastes)
    static IEnumerator NoFlightChecks(StringBuilder sb)
    {
        var k0 = ChordIslandOf(0); SM.SetCarry(SM.Islands.IndexOf(k0), 3, 1);
        yield return null;
        var k3 = ChordIslandOf(3); if (k3 != null) SM.SetLaunch(SM.Islands.IndexOf(k3), true);
        yield return Wait(0.4f);
        int beads0 = WM.BeadsLaunched, trails0 = WM.TrailsStarted, flings0 = WM.FlingCalls, appear0 = WM.Appears, pops0 = WM.PastePops;
        // pastes: a melody cube copied onto the next grids (plain, echo up)
        var src = MelodyCube(IslandWithMelody(3), 3);
        int pasted = 0;
        if (src != null && Clipboard.Copy(src))
        {
            var t1 = ChordIslandOf(4); var t2 = ChordIslandOf(5);
            if (t1 != null && Clipboard.PasteOn(t1) != null) pasted++;
            yield return Wait(0.6f);
            if (t2 != null && Clipboard.PasteOn(t2, Clipboard.PasteMode.EchoUp) != null) pasted++;
            yield return Wait(0.8f);
        }
        // a whole pass of the song: every cube's body stays on its own grid (its long grid's footprint)
        float worst = 0f; string who = "";
        yield return SeekPlay(0.0);
        double end = SM.TotalBeats;
        float t0 = Time.realtimeSinceStartup;
        var foot = new Dictionary<KeyBlock, Bounds>();
        while (GlobalClock.SongBeatD < end - 0.1 && GlobalClock.SongBeatD >= 0.0 && Time.realtimeSinceStartup - t0 < 20f)
        {
            foot.Clear();
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || c.IsOnMoon || c.Island == null || c.nodes.Count == 0) continue;
                Bounds fb;
                if (!foot.TryGetValue(c.Island, out fb))
                {
                    // its grid: K's long grid when it is in one (the whole run), else the island and the grids it covers
                    fb = c.Island.VisualBounds;
                    var run = SM.LongGridOf(c.Island);
                    if (run != null) foreach (var t in run) { if (t != null) fb.Encapsulate(t.VisualBounds); }
                    else { int idx = SM.Islands.IndexOf(c.Island); if (idx >= 0) foreach (var t in SM.CarryTargets(idx)) if (t != null) fb.Encapsulate(t.VisualBounds); }
                    foot[c.Island] = fb;
                }
                Vector3 p = c.transform.position;
                float padZ = c.layer < 0 ? 2.4f : 1.2f;   // the deep layer's reflection band lies in front of its grid
                float dx = Mathf.Max(0f, Mathf.Max(fb.min.x - 1.2f - p.x, p.x - fb.max.x - 1.2f));
                float dz = Mathf.Max(0f, Mathf.Max(fb.min.z - padZ - p.z, p.z - fb.max.z - 1.2f));
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > worst) { worst = d; who = "cube " + c.id + " of island " + SM.Islands.IndexOf(c.Island) + " at beat " + GlobalClock.SongBeatD.ToString("F2"); }
            }
            yield return null;
        }
        GlobalClock.Stop();
        Line(sb, WM.BeadsLaunched == beads0 && WM.TrailsStarted == trails0 && WM.BridgeCount == 0 && WM.Bridges.Count == 0,
             "§21 nothing flies: no arc, bead flight, trail or bridge over a whole song with extend ×3, a launch and pastes",
             "bead flights +" + (WM.BeadsLaunched - beads0) + ", trails +" + (WM.TrailsStarted - trails0) + ", bridges " + WM.BridgeCount + ", pastes " + pasted + " (appear pops +" + (WM.Appears - appear0) + ", paste pops +" + (WM.PastePops - pops0) + "), Magic.Fling calls +" + (WM.FlingCalls - flings0));
        Line(sb, worst < 0.05f, "§21 no cube's body leaves its grid (its long grid) over the whole song", "worst distance outside its footprint " + worst.ToString("F2") + (worst > 0f ? " (" + who + ")" : ""));
    }

    // ------------------------------------------------------------------ Magic.Appear (§21.1: a pasted / made cube appears on its grid)
    static IEnumerator AppearChecks(StringBuilder sb)
    {
        var kb = IslandWithMelody(3);
        var c = kb != null ? MelodyCube(kb, 3) : null;
        if (c == null) { Line(sb, false, "Magic.Appear", "no melody cube"); yield break; }
        Home(BoundsOf(kb));
        yield return Wait(0.4f);
        int a0 = WM.Appears, g0 = WM.AppearGlints, s0 = WM.LiveSparkles;
        Magic.Appear(c);
        yield return Wait(0.12f);
        int sparks = WM.LiveSparkles - s0;
        yield return Shot("w7_appear.png");
        yield return Wait(0.6f);
        Line(sb, WM.Appears - a0 == 1 && WM.AppearGlints - g0 == c.nodes.Count && sparks >= 5, "Magic.Appear: a pop and sparkles at the cube on its grid, its path glints",
             "appears " + (WM.Appears - a0) + ", sparkles live " + sparks + ", path tiles glinted " + (WM.AppearGlints - g0) + " / " + c.nodes.Count);
    }

    // ------------------------------------------------------------------ the flip mirror
    static IEnumerator FlipChecks(StringBuilder sb)
    {
        var kb = IslandWithMelody(3);
        var c = kb != null ? MelodyCube(kb, 3) : null;
        if (c == null) { Line(sb, false, "flip", "no melody cube"); yield break; }
        Home(BoundsOf(kb));
        yield return Wait(0.4f);
        int s0 = WM.Mirror.Sweeps, tf0 = WM.Mirror.TileFlashes, fc0 = WM.FlipCalls;
        var before = new List<TileInteraction>(c.nodes);
        // H's CubeOps.Flip calls Magic.Flip itself before it changes the path; while it is a stub (the path unchanged) the test flips by hand
        CubeOps.Flip(c);
        bool real = false;
        for (int i = 0; i < c.nodes.Count && i < before.Count; i++) if (c.nodes[i] != before[i]) { real = true; break; }
        if (!real) { Magic.Flip(c); MirrorByHand(kb, c); }
        yield return Wait(0.26f);
        float prog = WM.Mirror.Progress;
        yield return Shot("w7_flip.png");
        yield return Wait(0.6f);
        var distinct = new HashSet<TileInteraction>();
        foreach (var t in c.nodes) if (t != null) distinct.Add(t);
        Line(sb, WM.Mirror.Sweeps - s0 == 1 && WM.FlipCalls - fc0 == 1 && prog > 0.2f && prog < 0.95f && WM.Mirror.TileFlashes - tf0 == distinct.Count,
             "a flip: a mirror plane sweeps the island, the path's tiles flash as it passes",
             "sweeps " + (WM.Mirror.Sweeps - s0) + ", Magic.Flip calls " + (WM.FlipCalls - fc0) + (real ? " (from H's CubeOps.Flip)" : " (CubeOps.Flip a stub: flipped by the test)") + ", progress at capture " + prog.ToString("F2") + ", tiles flashed " + (WM.Mirror.TileFlashes - tf0) + " / " + distinct.Count);
        // detection without the call: the path turned into its mirror (and nobody said Magic.Flip) still sweeps
        yield return Wait(1.1f);
        int s1 = WM.Mirror.Sweeps, det0 = WM.Mirror.Detected;
        if (kb.kind == 0) MirrorByHand(kb, c);
        yield return Wait(0.7f);
        Line(sb, kb.kind != 0 || (WM.Mirror.Detected - det0 == 1 && WM.Mirror.Sweeps - s1 == 1), "a mirrored path without the call is recognised", "sweeps +" + (WM.Mirror.Sweeps - s1) + ", detected +" + (WM.Mirror.Detected - det0));
    }

    /// <summary>Turns <paramref name="c"/>'s path upside down in place on chord grid <paramref name="kb"/> (x → cols − 1 − x, z → rows − 1 − z) and
    /// finalises it (AudioCube.OnAnyChanged fires).</summary>
    static void MirrorByHand(KeyBlock kb, AudioCube c)
    {
        for (int i = 0; i < c.nodes.Count; i++)
        {
            var t = c.nodes[i];
            if (t == null) continue;
            var m = kb.GetTile(kb.cols - 1 - t.gridX, kb.rows - 1 - t.gridZ);
            if (m != null) c.nodes[i] = m;
        }
        c.Finalize();
    }

    // ------------------------------------------------------------------ the selection
    static IEnumerator SelectChecks(StringBuilder sb)
    {
        int s = 0;
        var list = new List<KeyBlock>();
        int a = SM.SectionFirst(s), b = SM.SectionLast(s);
        foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && kb.column >= a && kb.column <= b) list.Add(kb);
        Home(BoundsOf(list));
        yield return Wait(0.4f);
        GridSelection.SelectSection(s);
        yield return Wait(0.55f);
        float maxLiftErr = 0f;
        foreach (var kb in list) maxLiftErr = Mathf.Max(maxLiftErr, Mathf.Abs(kb.FxLift - SelectionFx.Lift));
        int shown = WM.Selection.Count;
        yield return Wait(1.3f);
        yield return Shot("w7_selection.png");
        Line(sb, shown == list.Count && maxLiftErr < 0.02f && WM.Selection.Shimmers >= 1, "selected grids: an ink outline, a soft lift, a shimmer",
             "section 0: " + list.Count + " grids, wearing the look " + shown + ", max |FxLift − " + SelectionFx.Lift.ToString("F2") + "| " + maxLiftErr.ToString("F3") + ", shimmers " + WM.Selection.Shimmers);
        GridSelection.Clear();
        yield return Wait(0.5f);
        float maxLift = 0f; foreach (var kb in list) if (kb != null) maxLift = Mathf.Max(maxLift, Mathf.Abs(kb.FxLift));
        Line(sb, WM.Selection.Count == 0 && maxLift < 1e-4f, "cleared: the grids drop back, the outline fades", "wearing " + WM.Selection.Count + ", max FxLift " + maxLift.ToString("F4"));
        // the section glow while playing
        yield return SeekPlay(1.0);
        yield return Wait(0.6f);
        var g = WM.Glow;
        Home(BoundsOf(list));
        yield return Wait(0.3f);
        bool ok = g.Active;
        yield return Shot("w7_section_glow.png");
        GlobalClock.Stop();
        Line(sb, ok, "the section playhead wears a glow", "active " + ok + ", from B's plinth " + g.FromPlinth + ", line " + g.From.ToString("F1") + " → " + g.To.ToString("F1") + ", sparkles " + g.Sparkles);
    }

    // ------------------------------------------------------------------ the tower (taller, stays up until the reset)
    static IEnumerator TowerChecks(StringBuilder sb)
    {
        var kb = ChordIslandOf(1);
        int at = SM.Islands.IndexOf(kb);
        SM.SetRegister(at, 1);
        yield return Wait(4.5f);   // K's stopped preview
        kb = SM.Islands[at];
        var tw = WM.TowerOf(kb);
        if (tw == null) { Line(sb, false, "the taller tower", "no tower"); yield break; }
        Home(BoundsOf(kb));
        yield return Wait(0.3f);
        int col = kb.column;
        double cs = SM.ColumnStart(col), ce = cs + SM.ColumnLength(col);
        float maxErr = 0f, maxLift = 0f; int upFrames = 0, upAfter = 0; int q0 = tw.QuietDownbeats;
        bool shot = false;
        yield return SeekPlay(cs - 1.0);
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < ce + 3.0 && Time.realtimeSinceStartup - t0 < 12f)
        {
            if (tw.State == 2 && tw.Attach >= 0.999f) { upFrames++; maxErr = Mathf.Max(maxErr, Mathf.Abs(tw.PillarTop - tw.UndersideY)); if (GlobalClock.SongBeatD > ce + 0.5) upAfter++; }
            maxLift = Mathf.Max(maxLift, kb.TowerLift);
            if (!shot && tw.State == 2 && GlobalClock.SongBeatD > cs + 1.0) { shot = true; yield return Shot("w7_tower_tall.png"); }
            yield return null;
        }
        GlobalClock.Stop();
        Line(sb, upFrames > 0 && maxErr <= 0.1f && maxLift > ProjectConfig.TowerRise * 0.9f, "the taller tower: the pillar holds the platform all the way up",
             "top lift " + maxLift.ToString("F2") + " (TowerRise " + ProjectConfig.TowerRise.ToString("F1") + "), max |top − underside| " + maxErr.ToString("F3") + " over " + upFrames + " frames, pillar depth " + Tower.PillarDepth);
        Line(sb, upAfter == 0 || tw.QuietDownbeats > q0, "after its turn it holds quietly (no ring every bar)",
             "frames up after the turn " + upAfter + " (" + (upAfter > 0 ? "K's stay-up rule" : "K sinks right after the turn") + "), quiet downbeats " + (tw.QuietDownbeats - q0));
        // a seek straight into the hold (K's k7_hold scenario): the pillar holds the platform there too
        yield return Wait(2.5f);
        yield return SeekPlay(ce + 1.5);
        yield return Wait(0.7f);
        kb = SM.Islands[at]; tw = WM.TowerOf(kb);
        bool held = tw != null && kb.TowerState == 2 && tw.Attach >= 0.99f && Mathf.Abs(tw.PillarTop - tw.UndersideY) < 0.1f && tw.PillarTransform.gameObject.activeInHierarchy;
        GlobalClock.Pause();
        yield return Shot("w7_tower_hold.png");
        GlobalClock.Stop();
        Line(sb, held, "a seek into the hold: the pillar holds the platform", tw != null ? "state " + kb.TowerState + ", attach " + tw.Attach.ToString("F2") + ", top " + tw.PillarTop.ToString("F2") + " / underside " + tw.UndersideY.ToString("F2") + ", breaks out of the plinth at " + tw.BaseY.ToString("F2") : "no tower");
        yield return Wait(3f);
    }

    // ------------------------------------------------------------------ cost
    static IEnumerator CostChecks(StringBuilder sb)
    {
        // everything at once: a down stair, a layer copy, rewind + vary, a flow ×2, a launch, a selection — playing
        var c1 = ChordIslandOf(1);
        AddStairs(c1, -1);
        yield return Wait(0.3f);
        var kb = IslandWithMelody(3);
        var src = kb != null ? MelodyCube(kb, 3) : null;
        if (src != null) Copy(src, 1, sb);
        yield return Wait(0.3f);
        var k0 = ChordIslandOf(0); int a0 = SM.Islands.IndexOf(k0);
        SM.SetCarry(a0, 2, 1);
        var k2 = ChordIslandOf(2); int a2 = SM.Islands.IndexOf(k2);
        if (a2 >= 0) { SM.SetRepeat(a2, 2); SM.SetRewind(a2, true); }
        var k3 = ChordIslandOf(3); int a3 = SM.Islands.IndexOf(k3);
        if (a3 >= 0) SM.SetLaunch(a3, true);
        GridSelection.SelectSection(Mathf.Max(0, SM.SectionCount - 1));
        yield return Wait(1.0f);
        yield return SeekPlay(0.0);
        yield return Wait(0.5f);
        double t = 0, l = 0, r = 0, f = 0, fl = 0, s = 0, m = 0, se = 0, g = 0, all = 0; int n = 0;   // f: long grids, fl: launches
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 4f)
        {
            t += WM.MsTerraces; l += WM.MsLayers; r += WM.MsRewind; f += WM.MsLongGrid; fl += WM.MsLaunch; s += WM.MsStairs; m += WM.MsMirror; se += WM.MsSelection; g += WM.MsGlow; all += WM.LastMs; n++;
            yield return null;
        }
        GlobalClock.Stop();
        n = Math.Max(1, n);
        double worst = Math.Max(Math.Max(Math.Max(t, l), Math.Max(r, f)), Math.Max(Math.Max(fl, s), Math.Max(Math.Max(m, se), g))) / n;
        Line(sb, worst <= 1.0, "frame cost ≤ +1 ms per effect",
             "ms/frame: terraces " + (t / n).ToString("F3") + " (" + WM.TerraceCount + "), layers+links " + (l / n).ToString("F3") + " (" + WM.LayerCount + "/" + WM.Links.Count + "), rewind " + (r / n).ToString("F3") + " (" + WM.RewindCount + "), long grid " + (f / n).ToString("F3") + " (" + WM.LongGlowCount + "), launch " + (fl / n).ToString("F3") + " (" + WM.LaunchCount + "), stairs " + (s / n).ToString("F3") +
             ", mirror " + (m / n).ToString("F3") + ", selection " + (se / n).ToString("F3") + " (" + WM.Selection.Count + "), glow " + (g / n).ToString("F3") + "; WorldMagic total " + (all / n).ToString("F3") + " over " + n + " frames");
        GridSelection.Clear();
    }
}
