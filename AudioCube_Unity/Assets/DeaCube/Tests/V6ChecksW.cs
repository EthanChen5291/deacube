using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package W (v6 world magic: the octave tower, the carry bridge, the paste / carry spells) Play-mode verification, SPEC v6 §6.4:
/// <c>V6ChecksW.RunAll()</c> starts a coroutine; poll <c>V6ChecksW.Done</c> or Captures/w6_report.txt (numbered PASS/FAIL lines, INFO lines with
/// numbers). On the v1 fixture: WorldMagic boots and follows the song; a raised island's tower (the rest stub at the waterline, K's preview rise,
/// its turn while playing: the pillar's top on the platform's underside ± 0.1 while it holds it, the rise's crest rings, a ring per downbeat
/// while up, the slow sink ring, the phases seen), a lowered island's whirl (inward ripples while it dips), a tower retiring at register 0; the
/// carry bridge (the spell mid-cast, arcs = CarryTargets, target sigils, ghost stars = the distinct tiles the source's cubes play there, the
/// pulse mid-flight between two islands and above both, the sigil flare as the column starts, ignitions, a retract, the bridge gone at carry 0);
/// Magic.PasteFlight (beads in flight, all landed), Magic.Trail / Land; WorldMagic's frame cost per carried island; no console errors.
/// Captures w6_*.png from the home camera angle. Runs with PathManager.AutoHand = false; never writes the user's save.
/// v7 (SPEC v7 §12.3, "the tower things that rise … only go back after the song resets"): a raised island now STAYS UP after its turn until the
/// song resets or stops, so the turn check plays through the turn, sees the tower still holding up two beats after it, then stops the clock and
/// watches the staggered real-time sink (state 3 → 0) and its slow ring — instead of expecting the sink right after the turn.
/// v7 §21 ("just keep cubes on each grid"): the carry bridge (arcs, sigils, ghost stars, the spell, the pulse) and the paste flight are retired —
/// a carry is a long grid, a paste pops in place; the carry / paste checks now assert that nothing is built and nothing flies.
/// </summary>
public static class V6ChecksW
{
    public static string Report = "";
    public static bool Done;
    static int num;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "w6_report.txt");
    static readonly List<string> errors = new List<string>();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" W6-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
        SequenceMaster.I.StartCoroutine(Guard(AllRoutine()));
        return "started";
    }

    /// <summary>Captures only (look iteration): "tower", "lowered", "carry", "paste" or "all".</summary>
    public static string RunLooks(string which)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(Guard(LooksRoutine(which ?? "all")));
        return "started";
    }

    static IEnumerator Guard(IEnumerator body)
    {
        // a coroutine that stops on an exception would leave Done false forever: step it by hand and report the throw
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Report += "FAIL W6-EX " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0] + "\n"; break; }
            yield return cur;
        }
        Cleanup();
        Done = true;
        try { File.WriteAllText(ReportPath, Report); } catch (Exception) { }
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
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

    /// <summary>The home camera angle (yaw / pitch) framing <paramref name="b"/> (instant).</summary>
    static void Home(Bounds b)
    {
        var oc = OrbitCamera.I;
        if (oc == null) return;
        oc.FrameStart(b);
    }
    static Bounds BoundsOf(params int[] islands)
    {
        var b = SM.Islands[islands[0]].WorldBounds;
        foreach (int i in islands) b.Encapsulate(SM.Islands[i].WorldBounds);
        return b;
    }

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

    // ================================================================== RunAll
    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        errors.Clear();
        Application.logMessageReceived += OnLog;
        Prepare();
        LoadFixture();
        yield return Wait(0.5f);
        Info(sb, "fixture: " + SM.Islands.Count + " islands, " + SM.ColumnCount + " columns, " + SequenceMaster.Cubes.Count + " cubes, bpm " + GlobalClock.BPM.ToString("F0"));
        Line(sb, WM != null && WM.gameObject.activeInHierarchy, "WorldMagic boots", WM != null ? "towers " + WM.TowerCount + ", bridges " + WM.BridgeCount : "missing");

        yield return TowerChecks(sb);
        yield return LoweredChecks(sb);
        yield return CarryChecks(sb);
        yield return PasteChecks(sb);
        yield return CostChecks(sb);

        GlobalClock.Stop();
        Line(sb, errors.Count == 0, "no console errors", errors.Count == 0 ? "0" : errors.Count + ": " + errors[0]);
        int pass = 0, fail = 0;
        foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
    }

    // ------------------------------------------------------------------ the tower
    static IEnumerator TowerChecks(StringBuilder sb)
    {
        const int isl = 2;
        var kb = SM.Islands[isl];
        SM.SetRegister(isl, 1);
        yield return null; yield return null;
        kb = SM.Islands[isl];
        var tw = WM.TowerOf(kb);
        Line(sb, tw != null, "a raised island gets a tower", tw != null ? "register " + kb.register + ", radius " + tw.Radius.ToString("F2") : "none");
        if (tw == null) yield break;
        // K's preview (▲ while stopped: one rise and slow sink) — wait for it to settle back to rest
        int maxState = 0; float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 7f)
        {
            maxState = Mathf.Max(maxState, tw.State);
            if (Time.realtimeSinceStartup - t0 > 0.6f && tw.State == 0 && tw.Attach <= 0.001f) break;
            yield return null;
        }
        Info(sb, "preview while stopped: highest TowerState " + maxState + ", rises drawn since the tower appeared " + tw.Rises + ", splashes " + tw.Splashes + " (K's preview " + (maxState > 0 ? "seen" : "not seen") + ")");
        Home(BoundsOf(isl));
        yield return Wait(0.6f);
        float stub = Tower.SeaY + Tower.StubHeight;
        Line(sb, tw.State == 0 && Mathf.Abs(tw.PillarTop - stub) < 0.06f && tw.PillarTransform.gameObject.activeSelf, "rest: only the stub shows",
             "pillar top " + tw.PillarTop.ToString("F2") + " (stub " + stub.ToString("F2") + "), underside " + tw.UndersideY.ToString("F2") + ", lift " + kb.TowerLift.ToString("F2"));
        yield return Shot("w6_tower_rest.png");

        // its turn while playing
        int col = kb.column;
        double cs = SM.ColumnStart(col), ce = cs + SM.ColumnLength(col);
        int rises0 = tw.Rises, down0 = tw.DownbeatRings, sink0 = tw.SinkRings;
        var seen = new List<int>();
        float maxErrUp = 0f, maxErrHeld = 0f; int upFrames = 0, heldFrames = 0;
        bool shotRise = false, shotUp = false, shotSink = false;
        yield return SeekPlay(cs - 1.5);
        t0 = Time.realtimeSinceStartup;
        float sinkSeen = -1f;
        int upAfterTurn = 0;
        // v7: play through the turn and two beats past it (the tower holds up), then stop: the sink runs in real time
        while (GlobalClock.SongBeatD < ce + 2.0 && Time.realtimeSinceStartup - t0 < 12f)
        {
            int st = tw.State;
            if (seen.Count == 0 || seen[seen.Count - 1] != st) seen.Add(st);
            if (st == 2) { upFrames++; maxErrUp = Mathf.Max(maxErrUp, Mathf.Abs(tw.PillarTop - tw.UndersideY)); if (GlobalClock.SongBeatD > ce + 0.5) upAfterTurn++; }
            if ((st == 1 || st == 3) && tw.Attach >= 0.999f) { heldFrames++; maxErrHeld = Mathf.Max(maxErrHeld, Mathf.Abs(tw.PillarTop - tw.UndersideY)); }
            if (!shotRise && st == 1 && tw.Attach > 0.5f) { shotRise = true; yield return Shot("w6_tower_rising.png"); }
            else if (!shotUp && st == 2 && GlobalClock.SongBeatD > cs + 1.2) { shotUp = true; yield return Shot("w6_tower_up.png"); }
            yield return null;
        }
        GlobalClock.Stop();
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f)
        {
            int st = tw.State;
            if (seen.Count == 0 || seen[seen.Count - 1] != st) seen.Add(st);
            if ((st == 1 || st == 3) && tw.Attach >= 0.999f) { heldFrames++; maxErrHeld = Mathf.Max(maxErrHeld, Mathf.Abs(tw.PillarTop - tw.UndersideY)); }
            if (st == 3 && sinkSeen < 0f) sinkSeen = Time.realtimeSinceStartup;
            if (!shotSink && st == 3 && sinkSeen >= 0f && Time.realtimeSinceStartup > sinkSeen + 0.35f) { shotSink = true; yield return Shot("w6_tower_sinking.png"); }
            if (sinkSeen >= 0f && st == 0 && tw.Attach <= 0.001f) break;
            yield return null;
        }
        string phases = string.Join(">", seen.ConvertAll(x => x.ToString()).ToArray());
        bool kLanded = seen.Contains(1) || seen.Contains(2);
        Line(sb, kLanded && seen.Contains(2) && seen.Contains(3) && seen[seen.Count - 1] == 0, "the turn: rising > up (held past the turn) > sinking after the stop > rest",
             "phases " + phases + ", frames up after the turn " + upAfterTurn + (kLanded ? "" : " (K's pose not landed: TowerState never left 0)"));
        Line(sb, upFrames > 0 && maxErrUp <= 0.1f, "pillar top = underside ± 0.1 while up", "max |top − underside| " + maxErrUp.ToString("F3") + " over " + upFrames + " frames; while rising / sinking held " + maxErrHeld.ToString("F3") + " over " + heldFrames);
        Line(sb, tw.Rises - rises0 >= 1 && tw.LastRiseRings == Tower.RiseRingCount, "waves at the rise", "rises " + (tw.Rises - rises0) + ", crest rings of the rise " + tw.LastRiseRings + " (want " + Tower.RiseRingCount + "), splashes " + tw.Splashes);
        Line(sb, tw.DownbeatRings - down0 >= 1, "a ring per downbeat while up", (tw.DownbeatRings - down0) + " downbeat rings (column " + SM.ColumnLength(col).ToString("F0") + " beats)");
        Line(sb, tw.SinkRings - sink0 >= 1, "one slow wide ring as it sinks (after the stop)", (tw.SinkRings - sink0) + " sink rings");
        Info(sb, "captures: rising " + shotRise + ", up " + shotUp + ", sinking " + shotSink);

        // the look without K (or in addition): forced phases for the captures
        if (!kLanded)
        {
            Tower.ForceState = 0; yield return Wait(0.2f);
            Tower.ForceState = 1; yield return Wait(0.12f);
            yield return Shot("w6_tower_forced_rise.png");
            Tower.ForceState = 2; yield return Wait(0.6f);
            yield return Shot("w6_tower_forced_up.png");
            Tower.ForceState = -1; yield return Wait(0.6f);
        }

        // retire
        SM.SetRegister(isl, 0);
        yield return Wait(0.9f);
        Line(sb, WM.TowerOf(SM.Islands[isl]) == null && WM.TowerCount == 0, "register 0: the tower sinks away", "towers " + WM.TowerCount);
    }

    static IEnumerator LoweredChecks(StringBuilder sb)
    {
        const int isl = 4;
        SM.SetRegister(isl, -1);
        yield return null; yield return null;
        var kb = SM.Islands[isl];
        var tw = WM.TowerOf(kb);
        Line(sb, tw != null && !tw.PillarTransform.gameObject.activeSelf, "a lowered island gets a whirl (no pillar)", tw != null ? "whirl " + tw.Whirl.ToString("F2") : "none");
        if (tw == null) yield break;
        // K's stopped preview (one dip and the float back) first
        float p0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - p0 < 7f) { if (Time.realtimeSinceStartup - p0 > 0.6f && tw.State == 0) break; yield return null; }
        Info(sb, "lowered preview while stopped: dips drawn " + tw.Dips + ", inward ripples " + tw.InwardRings);
        yield return Wait(0.3f);
        int col = kb.column;
        double cs = SM.ColumnStart(col), ce = cs + SM.ColumnLength(col);
        int in0 = tw.InwardRings, dips0 = tw.Dips; float maxWhirl = 0f; bool shot = false;
        Home(BoundsOf(isl));
        yield return SeekPlay(cs - 1.0);
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < ce + 1.0 && Time.realtimeSinceStartup - t0 < 10f)
        {
            maxWhirl = Mathf.Max(maxWhirl, tw.Whirl);
            if (!shot && GlobalClock.SongBeatD > cs + 0.6 && (tw.State == 1 || tw.State == 2)) { shot = true; yield return Shot("w6_tower_lowered.png"); }
            yield return null;
        }
        GlobalClock.Stop();
        Line(sb, tw.Dips - dips0 >= 1 && tw.InwardRings - in0 >= 3 && maxWhirl > 0.6f, "the dip: a whirl of inward ripples", "dips " + (tw.Dips - dips0) + ", inward ripples " + (tw.InwardRings - in0) + ", whirl max " + maxWhirl.ToString("F2"));
        if (!shot) { Tower.ForceState = 2; yield return Wait(0.8f); yield return Shot("w6_tower_lowered.png"); Tower.ForceState = -1; }
        SM.SetRegister(isl, 0);
        yield return Wait(0.8f);
    }

    // ------------------------------------------------------------------ the carry (v7 §21: a long grid — no bridge, arc, sigil or ghost star)
    static IEnumerator CarryChecks(StringBuilder sb)
    {
        const int src = 0;
        Home(BoundsOf(0, 1, 2));
        yield return Wait(0.4f);
        int beads0 = WM.BeadsLaunched, trails0 = WM.TrailsStarted;
        SM.SetCarry(src, 1);
        yield return Wait(0.8f);
        var kb = SM.Islands[src];
        Line(sb, WM.BridgeOf(kb) == null && WM.BridgeCount == 0, "§21 carry 1: no carry bridge (the carry is K's long grid)", "bridges " + WM.BridgeCount + ", targets " + SM.CarryTargets(src).Count);
        SM.SetCarry(src, 3);
        yield return Wait(0.8f);
        kb = SM.Islands[src];
        var targets = SM.CarryTargets(src);
        Home(BoundsOf(0, 1, 2, 3));
        yield return Wait(0.5f);
        yield return Shot("w6_carry_rest.png");
        // playing into the first target's column: nothing flies there (no pulse, no bead, no trail)
        double cs = targets.Count > 0 ? SM.ColumnStart(targets[0].column) : 4.0;
        yield return SeekPlay(Math.Max(0.0, cs - 1.2));
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < cs + 2.0 && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        yield return Shot("w6_carry_landed.png");
        GlobalClock.Stop();
        Line(sb, WM.BridgeCount == 0 && WM.BeadsLaunched == beads0 && WM.TrailsStarted == trails0, "§21 carry 3 playing: nothing flies (no arc, bead or trail)",
             "targets " + targets.Count + ", bridges " + WM.BridgeCount + ", bead flights +" + (WM.BeadsLaunched - beads0) + ", trails +" + (WM.TrailsStarted - trails0));
        SM.SetCarry(src, 0);
        yield return Wait(0.6f);
        Line(sb, WM.Bridges.Count == 0, "§21 carry 0: still nothing", "bridges " + WM.Bridges.Count);
    }

    // ------------------------------------------------------------------ paste (v7 §21: no flight — pops in place), trail, land
    static IEnumerator PasteChecks(StringBuilder sb)
    {
        var from = new List<Vector3>(); var to = new List<Vector3>();
        AudioCube cube = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == SM.Islands[0] && c.nodes.Count >= 3) { cube = c; break; }
        var target = SM.Islands[1];
        if (cube != null)
            for (int i = 0; i < cube.nodes.Count; i++)
            {
                from.Add(cube.nodes[i].Top + Vector3.up * 0.3f);
                var t = target.GetTile(Mathf.Min(i, target.cols - 1), Mathf.Min(i % 3, target.rows - 1));
                to.Add(t.Top + Vector3.up * 0.3f);
            }
        Home(BoundsOf(0, 1));
        yield return Wait(0.6f);
        int beads0 = WM.BeadsLaunched, pops0 = WM.PastePops;
        Magic.PasteFlight(from, to, cube != null ? cube.Color : Color.white, 0.9f);
        yield return Wait(0.38f);
        int flying = WM.ActiveBeads;
        yield return Shot("w6_paste_flight.png");
        yield return Wait(1.0f);
        Line(sb, from.Count > 0 && flying == 0 && WM.BeadsLaunched == beads0 && WM.PastePops - pops0 == from.Count, "§21 paste: nothing flies — each target pops in place on time",
             "targets " + from.Count + ", beads in flight at 0.38 s " + flying + ", bead flights +" + (WM.BeadsLaunched - beads0) + ", pops " + (WM.PastePops - pops0) + ", the last at " + Magic.PasteLandTime(from.Count - 1, from.Count, 0.9f).ToString("F2") + " s");

        // a trail behind a moving transform, then a landing
        var go = new GameObject("w6_trail_probe");
        Vector3 a = SM.Islands[0].VisualCenter + Vector3.up * 1.2f, b = SM.Islands[1].VisualCenter + Vector3.up * 1.2f;
        int trails0 = WM.TrailsStarted;
        Magic.Trail(go.transform, Instruments.Colors[3], 0.7f);
        int sparkMax = 0, activeMax = 0;
        float t0 = Time.realtimeSinceStartup;
        bool shot = false;
        while (Time.realtimeSinceStartup - t0 < 0.7f)
        {
            float u = (Time.realtimeSinceStartup - t0) / 0.7f;
            go.transform.position = Magic.ArcPoint(a, b, Magic.ArcHeight(a, b), u);
            sparkMax = Mathf.Max(sparkMax, WM.LiveSparkles); activeMax = Mathf.Max(activeMax, WM.ActiveTrails);
            if (!shot && u > 0.55f) { shot = true; yield return Shot("w6_trail.png"); }
            yield return null;
        }
        Magic.Land(b, Instruments.Colors[3]);
        yield return null;
        UnityEngine.Object.Destroy(go);
        yield return Wait(0.5f);
        Line(sb, WM.TrailsStarted > trails0 && activeMax >= 1 && sparkMax >= 3, "Magic.Trail follows its cube with sparkles", "trails " + (WM.TrailsStarted - trails0) + ", active max " + activeMax + ", live sparkles max " + sparkMax + ", active after the target died " + WM.ActiveTrails);
    }

    // ------------------------------------------------------------------ cost
    static IEnumerator CostChecks(StringBuilder sb)
    {
        GlobalClock.Stop();
        yield return SeekPlay(0.0);
        yield return Wait(0.5f);
        double base0 = 0.0; int n0 = 0;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.5f) { base0 += WM.LastMs; n0++; yield return null; }
        base0 /= Math.Max(1, n0);
        // three carrying islands (v7 §21: three long grids — WorldMagic lights their walks, no bridges)
        SM.SetCarry(0, 2); SM.SetCarry(3, 1); SM.SetCarry(1, 1);
        yield return Wait(2.2f);
        int carried = WM.LongGlowCount, runs = SM.LongGrids.Count;   // K joins chains into one run and never crosses a section: count its runs
        double with = 0.0; int n1 = 0;
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 3f) { with += WM.LastMs; n1++; yield return null; }
        with /= Math.Max(1, n1);
        double per = carried > 0 ? (with - base0) / carried : 99.0;
        Line(sb, carried >= 1 && carried == runs && per <= 1.0 && WM.BridgeCount == 0, "frame cost ≤ +1 ms per long grid (§21: three carries → K's long grids, no bridge)", "WorldMagic " + base0.ToString("F3") + " ms/frame without carries, " + with.ToString("F3") + " with " + carried + " lit long grids (K's runs " + runs + ": a chain is one run, a section boundary stops one) (+" + per.ToString("F3") + " ms each), bridges " + WM.BridgeCount + ", playing, " + n1 + " frames");
        GlobalClock.Stop();
        SM.SetCarry(0, 0); SM.SetCarry(3, 0); SM.SetCarry(1, 0);
        yield return Wait(2.5f);
    }

    // ================================================================== looks only (iteration)
    static IEnumerator LooksRoutine(string which)
    {
        var sb = new StringBuilder();
        errors.Clear();
        Application.logMessageReceived += OnLog;
        Prepare();
        LoadFixture();
        yield return Wait(0.5f);
        bool all = which == "all";
        if (all || which == "tower")
        {
            Home(BoundsOf(2));
            yield return Wait(0.3f);
            SM.SetRegister(2, 1);   // K's stopped preview: one rise and the slow sink
            yield return null;
            var tw = WM.TowerOf(SM.Islands[2]);
            float t0 = Time.realtimeSinceStartup; bool r = false, u = false, k = false; float sinkAt = -1f;
            while (tw != null && Time.realtimeSinceStartup - t0 < 6f)
            {
                if (!r && tw.State == 1 && tw.Attach > 0.6f) { r = true; yield return Shot("w6_look_tower_rise.png"); }
                else if (!u && tw.State == 2) { u = true; yield return Wait(0.25f); yield return Shot("w6_look_tower_up.png"); }
                else if (tw.State == 3 && sinkAt < 0f) sinkAt = Time.realtimeSinceStartup;
                else if (!k && sinkAt > 0f && Time.realtimeSinceStartup - sinkAt > 0.6f) { k = true; yield return Shot("w6_look_tower_sink.png"); }
                if (k && tw.State == 0) break;
                yield return null;
            }
            yield return Wait(1.2f);
            yield return Shot("w6_look_tower_rest.png");
            Info(sb, tw != null ? "tower preview shots: rise " + r + " up " + u + " sink " + k + "; rest top " + tw.PillarTop.ToString("F2") + " underside " + tw.UndersideY.ToString("F2") : "no tower");
            SM.SetRegister(2, 0);
            yield return Wait(0.8f);
        }
        bool films = which == "films";
        if (films || which == "towerfilm")
        {
            // a filmstrip of the tower's turn while playing: the clock pauses at each beat (K's pose is a pure function of the beat), a capture
            const int isl = 2;
            SM.SetRegister(isl, 1);
            yield return Wait(4f);   // K's stopped preview first
            var kb = SM.Islands[isl];
            double rs, us, ss, ra;
            if (SM.TowerTurn(kb, out rs, out us, out ss, out ra))
            {
                Home(BoundsOf(isl));
                yield return Wait(0.3f);
                double[] at = { rs + 0.12, rs + 0.3, us - 0.1, us + 1.2, ss + 0.8, ss + 2.2, ra + 0.6 };
                string[] names = { "a_surge", "b_rise", "c_top", "d_up", "e_sink", "f_sink2", "g_rest" };
                yield return SeekPlay(rs - 1.0);
                for (int i = 0; i < at.Length; i++)
                {
                    float t0 = Time.realtimeSinceStartup;
                    while (GlobalClock.SongBeatD < at[i] && Time.realtimeSinceStartup - t0 < 6f) yield return null;
                    GlobalClock.Pause();
                    var tw = WM.TowerOf(kb);
                    Info(sb, names[i] + " beat " + GlobalClock.SongBeatD.ToString("F2") + " state " + kb.TowerState + " lift " + kb.TowerLift.ToString("F2") + (tw != null ? " top " + tw.PillarTop.ToString("F2") + " under " + tw.UndersideY.ToString("F2") + " crests " + WM.ActiveCrests + " drops " + WM.LiveDrops : ""));
                    yield return Shot("w6_film_tower_" + names[i] + ".png");
                    GlobalClock.Play();
                    yield return null;
                }
                GlobalClock.Stop();
            }
            SM.SetRegister(isl, 0);
            yield return Wait(0.8f);
        }
        if (films || which == "carryfilm")
        {
            Home(BoundsOf(0, 1, 2));
            yield return Wait(0.6f);
            SM.SetCarry(0, 1);
            float t0 = Time.realtimeSinceStartup;
            float[] at = { 0.2f, 0.55f, 0.95f, 1.3f, 1.75f, 2.3f };
            for (int i = 0; i < at.Length; i++)
            {
                while (Time.realtimeSinceStartup - t0 < at[i]) yield return null;
                yield return Shot("w6_film_cast_" + i + ".png");
            }
            SM.SetCarry(0, 2);
            yield return Wait(2.2f);
            // playing into the first target's column: the pulse, the flare, the ignition
            var tg = SM.CarryTargets(0);
            double cs = SM.ColumnStart(tg[0].column);
            yield return SeekPlay(cs - 1.0);
            double[] beats = { cs - 0.35, cs - 0.15, cs + 0.1, cs + 0.6, cs + 1.6 };
            for (int i = 0; i < beats.Length; i++)
            {
                float w0 = Time.realtimeSinceStartup;
                while (GlobalClock.SongBeatD < beats[i] && Time.realtimeSinceStartup - w0 < 6f) yield return null;
                GlobalClock.Pause();
                yield return Shot("w6_film_play_" + i + ".png");
                GlobalClock.Play();
                yield return null;
            }
            GlobalClock.Stop();
            SM.SetCarry(0, 0);
            yield return Wait(2f);
        }
        if (all || which == "lowered")
        {
            SM.SetRegister(4, -1);
            yield return Wait(0.2f);
            Home(BoundsOf(4));
            Tower.ForceState = 0; yield return Wait(0.3f);
            Tower.ForceState = 1; yield return Wait(0.9f);
            yield return Shot("w6_look_lowered.png");
            Tower.ForceState = -1;
            SM.SetRegister(4, 0);
            yield return Wait(0.6f);
        }
        if (all || which == "carry")
        {
            Home(BoundsOf(0, 1, 2));
            yield return Wait(0.5f);
            SM.SetCarry(0, 1);
            yield return Wait(0.62f);
            yield return Shot("w6_look_cast.png");
            yield return Wait(1.6f);
            SM.SetCarry(0, 2);
            yield return Wait(2f);
            Home(BoundsOf(0, 1, 2));
            yield return Wait(0.7f);
            yield return Shot("w6_look_carry.png");
            var br = WM.BridgeOf(SM.Islands[0]);
            Info(sb, br != null ? "bridge: hops " + br.HopCount + " arcs " + br.DrawnArcs + " stars " + br.GhostStars() + " runes " + br.SourceRunes : "no bridge");
            SM.SetCarry(0, 1);
            yield return Wait(0.2f);
            yield return Shot("w6_look_retract.png");
            yield return Wait(1f);
            SM.SetCarry(0, 0);
            yield return Wait(2f);
        }
        if (all || which == "paste")
        {
            Home(BoundsOf(0, 1));
            yield return Wait(0.6f);
            var from = new List<Vector3>(); var to = new List<Vector3>();
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == SM.Islands[0] && c.nodes.Count >= 3)
                {
                    for (int i = 0; i < c.nodes.Count; i++) { from.Add(c.nodes[i].Top + Vector3.up * 0.3f); to.Add(SM.Islands[1].GetTile(i % 6, i % 3).Top + Vector3.up * 0.3f); }
                    Magic.PasteFlight(from, to, c.Color, 0.9f);
                    break;
                }
            yield return Wait(0.4f);
            yield return Shot("w6_look_paste.png");
            yield return Wait(1f);
        }
        GlobalClock.Stop();
        Info(sb, "errors " + errors.Count + (errors.Count > 0 ? ": " + errors[0] : ""));
        Report = sb.ToString();
    }
}
