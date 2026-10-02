using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v7 §5.6 / §12.1 package H checks (Play mode, PathManager.AutoHand = false). <c>V7ChecksH.RunAll()</c> starts the timed checks; poll
/// <see cref="Done"/> or Captures/h7_report.txt. Sections: "copy" (octave copies: layer, pitch ±12 / +24 by VoiceRules.NodeMidi, no occupancy,
/// one History entry, the layer limit and a duplicate refused, the late echo's rest + shortened tail), "harmony" (Harmony.Harmonize's answer
/// above / below, tiles + bends, a clash dodged, the preview's ghosts), "flip" (the mirror, stickers / lengths kept, bends turned), "paste"
/// (echo up / down / the doubling / late, answer, step up / down), "tiles" (varied passes = Harmony.Vary's tiles, flow windows = the pedal chain,
/// cached), "motion" (sampled at beats while paused: the layer ride + phase-in, the rewind zip at its middle, the flow glide between grids, the
/// fling's apex and its landing at TurnEnd — §21: the long-grid walk's ordinary hops into each next measure, and a launch that moves no cube),
/// "runner" (a stairs island's runner = StairPath, refresh, the fall step by step; §21: it holds on its last step and climbs back up its own
/// stairs after the reset), "hold" (a finished grid's cube holds on its last tile mid-song; after the loop it moves home over more than 0.2 beat,
/// never more than 0.6 u in a frame, out of sync with another; a column-0 cube lands home on beat 0; a stop sends it home in real time),
/// "song" (§21: a whole song with a long grid, a launch, a runner, pastes and an octave copy — no cube ever leaves its grid). The fixture is
/// loaded (never the user's save); the saves are put back. Synth late / errors must not move (a mid-play capture and the test's own restarts
/// are counted apart). Captures h7_*.png.
/// </summary>
public static class V7ChecksH
{
    public static string Report = "";
    public static bool Done = true;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "h7_report.txt");

    /// <summary>v9: the stairs runner's pre-fall lift (AudioCube.StairHop: 4 × 0.3 × f(1 − f), at most 0.3 u).</summary>
    const float StairHopLift = 0.3f;
    static StringBuilder sb;
    static int pass, fail, pushes, lateExcused;
    static bool shots;
    static readonly Vector3 Sea = new Vector3(-50f, -50f, 0f);

    // ================================================================== helpers
    static string L(bool ok, string name, string detail = null) => (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + "\n";
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); Report = sb.ToString(); }
    static void Add(string lines)
    {
        if (string.IsNullOrEmpty(lines)) return;
        foreach (var l in lines.Split('\n'))
        {
            if (l.Length == 0) continue;
            if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++;
            sb.Append(l).Append('\n');
        }
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + "...running " + Progress + "\n"); } catch (Exception) { }
    }
    static string FirstFrame(Exception e) { var st = e.StackTrace ?? ""; int i = st.IndexOf('\n'); return i > 0 ? st.Substring(0, i).Trim() : st.Trim(); }
    static void OnHistory() { pushes++; }
    static string F(double v, string f = "F2") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static string Ints(IList<int> a) { if (a == null) return "null"; var s = new StringBuilder(); for (int i = 0; i < a.Count; i++) { if (i > 0) s.Append(','); s.Append(a[i]); } return s.ToString(); }
    static string Tiles(IList<TileInteraction> t) { var s = new StringBuilder(); for (int i = 0; i < t.Count; i++) { if (i > 0) s.Append(' '); s.Append(t[i] != null ? t[i].gridX + "/" + t[i].gridZ : "-"); } return s.ToString(); }
    static string Midis(IList<TileInteraction> t) { var s = new StringBuilder(); for (int i = 0; i < t.Count; i++) { if (i > 0) s.Append(','); s.Append(t[i] != null ? t[i].midi.ToString() : "-"); } return s.ToString(); }
    static SongManager SM => SongManager.I;

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray closed, the real mouse ignored, an empty hand).</summary>
    static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = Sea;
        var pm = PathManager.I;
        if (pm != null)
        {
            if (pm.IsDrawing) pm.CancelPath(false);
            if (pm.Drag != null && pm.Drag.Busy) pm.Drag.Cancel();
            pm.Deselect();
            pm.PutDown();
        }
        Clipboard.EchoLate = false;
        CubeOps.EndPreview();
        KeyShim.Clear();
    }

    static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        GlobalClock.SetSwing(0f);
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        AudioCube.SnapAllHome();
    }

    static void Frame(Vector3 focus, float dist, float yaw = 0f, float pitch = 52f)
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    /// <summary>A screen capture (overlay UI included); a capture mid-play stalls the main thread: late notes around it are counted apart.</summary>
    static IEnumerator Capture(string name)
    {
        if (!shots) yield break;
        int late = Synth.LateEvents;
        Directory.CreateDirectory(V2Checks.CapturePath);
        ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, name + ".png"), 1);
        yield return null; yield return null;
        if (GlobalClock.IsPlaying) { yield return Wait(0.3f); }
        lateExcused += Synth.LateEvents - late;
    }

    /// <summary>Paused at song beat <paramref name="beat"/> (the pose of that beat; a frame of play first so the cubes leave their stopped home),
    /// then <paramref name="settle"/> seconds for the springs.</summary>
    static IEnumerator At(double beat, float settle = 0.2f, int loopIndex = -1)
    {
        if (!GlobalClock.IsPlaying) { int late = Synth.LateEvents; GlobalClock.Play(); yield return null; lateExcused += Synth.LateEvents - late; }
        GlobalClock.Pause();
        GlobalClock.Seek(beat);
        if (loopIndex >= 0) GlobalClock.LoopIndex = loopIndex;
        yield return Wait(settle);
    }

    /// <summary>A finished cube built from a state on island <paramref name="kb"/> (lengths in ticks; mods null = notes).</summary>
    static AudioCube Make(KeyBlock kb, int instrument, int[] xs, int[] zs, int[] durs, int[] mods = null)
    {
        var m = mods ?? new int[xs.Length];
        var rests = new bool[xs.Length]; for (int i = 0; i < m.Length; i++) rests[i] = m[i] == 1;
        var s = new CubeState { instrument = instrument, measure = SM.Islands.IndexOf(kb), xs = xs, zs = zs, rests = rests, mods = m, durs = durs, volume = 1f, moon = -1, twinOf = -1, echoOf = -1 };
        var c = PathManager.I.RestoreCube(s);
        SequenceMaster.RecalculateTimeline();
        return c;
    }

    /// <summary>Removes every cube of island <paramref name="kb"/> (a clean grid for a check).</summary>
    static void Clear(KeyBlock kb)
    {
        var l = new List<AudioCube>();
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) l.Add(c);
        foreach (var c in l) { SequenceMaster.Unregister(c); UnityEngine.Object.DestroyImmediate(c.gameObject); }
        SequenceMaster.RecalculateTimeline();
    }

    /// <summary>The cube on island index <paramref name="isl"/> with this instrument and these grid positions (cubes are re-created by rebuilds).</summary>
    static AudioCube Find(int isl, int instrument, IList<int> xs, IList<int> zs, int layer = 0)
    {
        if (isl < 0 || isl >= SM.Islands.Count) return null;
        var kb = SM.Islands[isl];
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.Island != kb || c.instrument != instrument || c.nodes.Count != xs.Count || c.layer != layer) continue;
            bool ok = true;
            for (int i = 0; i < xs.Count && ok; i++) ok = c.nodes[i].gridX == xs[i] && c.nodes[i].gridZ == zs[i];
            if (ok) return c;
        }
        return null;
    }

    static List<int> XsOf(AudioCube c) { var l = new List<int>(); foreach (var t in c.nodes) l.Add(t != null ? t.gridX : -1); return l; }
    static List<int> ZsOf(AudioCube c) { var l = new List<int>(); foreach (var t in c.nodes) l.Add(t != null ? t.gridZ : -1); return l; }

    /// <summary>The cube's rest pose on tile <paramref name="t"/> without the stack (tile top + half its size + its layer).</summary>
    static Vector3 RestOn(AudioCube c, TileInteraction t) => c.TopOf(t) + Vector3.up * (ProjectConfig.CubeSize * c.SizeFactor * 0.5f);

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

    // ================================================================== entry points
    public static string RunAll(bool captures = true) => Start(null, captures);
    /// <summary>Some sections (a comma list of "copy", "harmony", "flip", "paste", "tiles", "motion", "runner", "hold").</summary>
    public static string RunOnly(string which, bool captures = false) => Start(which, captures);

    static string Start(string only, bool captures)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start"; shots = captures;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(only));
        return "started" + (only != null ? " " + only : "");
    }

    static readonly string[] Sections = { "copy", "harmony", "flip", "paste", "tiles", "motion", "runner", "hold", "song" };

    static IEnumerator Routine(string only)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0; lateExcused = 0;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float bpm0 = GlobalClock.BPM;
        bool auto0 = PathManager.AutoHand, loop0 = GlobalClock.LoopSong;
        History.OnChanged += OnHistory;
        foreach (var s in Sections)
        {
            if (only != null && Array.IndexOf(only.Split(','), s) < 0) continue;
            Prepare(); LoadFixture(); GlobalClock.LoopSong = true;
            yield return null; yield return null;
            Progress = s;
            IEnumerator body = null;
            switch (s)
            {
                case "copy": body = Copies(); break;
                case "harmony": body = Harmonies(); break;
                case "flip": body = Flips(); break;
                case "paste": body = Pastes(); break;
                case "tiles": body = TilesVaryFlow(); break;
                case "motion": body = Motions(); break;
                case "runner": body = Runners(); break;
                case "hold": body = Holds(); break;
                case "song": body = WholeSong(); break;
            }
            int lateS = Synth.LateEvents;
            if (body != null) yield return Guard(body);
            if (Synth.LateEvents != lateS) Info("section " + s + ": synth late +" + (Synth.LateEvents - lateS) + " (last: " + Synth.LastLate + ")");
            CubeOps.EndPreview();
            Clipboard.EchoLate = false;
            if (PathManager.I.IsDrawing) PathManager.I.CancelPath(false);
            PathManager.I.PutDown();
            CubeInspector.CloseImmediate();
            FocusLoop.Dismiss(); GlobalClock.Stop();
            if (GlobalClock.BPM != bpm0) GlobalClock.SetBPM(bpm0);
        }
        Progress = "finish";
        Prepare(); LoadFixture();
        Clipboard.Clear();
        GlobalClock.LoopSong = loop0;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        PathManager.I.PutDown();
        PathManager.AutoHand = auto0;
        History.OnChanged -= OnHistory;
        V3Fixes.RestoreSaves(saves);
        Add(L(Synth.LateEvents - lateExcused == late0 && Synth.Errors == err0, "RUN Synth late / errors unchanged (counted apart: " + lateExcused + " around captures / the test's own play starts)", Synth.Stats()));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true; Progress = "done";
    }

    static IEnumerator Guard(IEnumerator body)
    {
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) yield break; cur = body.Current; }
            catch (Exception e) { Add(L(false, Progress + " threw", e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e))); yield break; }
            yield return cur;
        }
    }

    // ================================================================== 1. octave copies / echoes
    static IEnumerator Copies()
    {
        var kb = SM.Islands[2];
        Clear(kb);
        var src = Make(kb, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 36, 12 });
        if (src == null) { Add(L(false, "O copy", "no source cube")); yield break; }
        yield return null;
        int cubes0 = SequenceMaster.Cubes.Count, p0 = pushes;
        var up = CubeOps.OctaveCopy(src, +1);
        bool made = up != null && SequenceMaster.Cubes.Count == cubes0 + 1 && pushes == p0 + 1;
        bool same = up != null && up.nodes.Count == src.nodes.Count;
        for (int i = 0; same && i < src.nodes.Count; i++) same = up.nodes[i] == src.nodes[i] && up.durs[i] == src.durs[i] && up.ModOf(i) == src.ModOf(i);
        var dP = new List<int>();
        if (up != null) for (int i = 0; i < src.nodes.Count; i++) dP.Add(VoiceRules.NodeMidi(up, i) - VoiceRules.NodeMidi(src, i));
        bool plus12 = dP.Count == src.nodes.Count && dP.TrueForAll(d => d == 12);
        Add(L(made && same && up.layer == 1 && up.echoOf == src.id && up.id != src.id && plus12,
              "O1 octave copy ↑: a new cube (one History entry), same tiles / lengths / stickers, layer 1, echoOf = the source, sounds +12 on every note (VoiceRules.NodeMidi)",
              "cubes " + cubes0 + " → " + SequenceMaster.Cubes.Count + ", pushes +" + (pushes - p0) + ", layer " + (up != null ? up.layer : -9) + ", echoOf " + (up != null ? up.echoOf : -9) + " (src " + src.id + "), pitch diffs " + Ints(dP)));
        var down = CubeOps.OctaveCopy(src, -1);
        var dD = new List<int>();
        if (down != null) for (int i = 0; i < src.nodes.Count; i++) dD.Add(VoiceRules.NodeMidi(down, i) - VoiceRules.NodeMidi(src, i));
        var up2 = up != null ? CubeOps.OctaveCopy(up, +1) : null;
        var d2 = new List<int>();
        if (up2 != null) for (int i = 0; i < src.nodes.Count; i++) d2.Add(VoiceRules.NodeMidi(up2, i) - VoiceRules.NodeMidi(src, i));
        Add(L(down != null && down.layer == -1 && dD.TrueForAll(d => d == -12) && up2 != null && up2.layer == 2 && d2.TrueForAll(d => d == 24),
              "O2 copy ↓ = layer −1, −12; a copy of the ↑ copy = layer 2, +24 from the source", "down diffs " + Ints(dD) + ", up-up layer " + (up2 != null ? up2.layer : -9) + " diffs " + Ints(d2)));
        int deny0 = CubeOps.DenyCount, p1 = pushes, n1 = SequenceMaster.Cubes.Count;
        var over = up2 != null ? CubeOps.OctaveCopy(up2, +1) : null;
        string why1 = CubeOps.LastDeny;
        var dup = CubeOps.OctaveCopy(src, +1);
        string why2 = CubeOps.LastDeny;
        var drum = CubeOps.OctaveCopy(Make(kb, 9, new[] { 5, 4 }, new[] { 0, 0 }, new[] { 24, 24 }), +1);
        Add(L(over == null && dup == null && drum == null && CubeOps.DenyCount == deny0 + 3 && pushes == p1 + 0 + (drum == null ? 0 : 1) && SequenceMaster.Cubes.Count == n1 + 1,
              "O3 soft refusals: past layer ±2, the same copy again, a drum cube (no History entry, no cube)", "\"" + why1 + "\", \"" + why2 + "\", drums \"" + CubeOps.LastDeny + "\", pushes +" + (pushes - p1)));
        // no occupancy: the copies stand in their layer, never in the grid's stacks
        yield return Wait(0.8f);
        bool skyExact = up != null && (up.TopOf(src.nodes[0]) - (src.nodes[0].Top + Magic.LayerOffset(1))).magnitude < 1e-3f;
        bool deepFront = down != null && down.TopOf(src.nodes[0]).z < kb.FrontEdge && down.TopOf(src.nodes[0]).y < src.nodes[0].Top.y - 0.2f;
        bool noOcc = skyExact && deepFront; string occWhy = skyExact && deepFront ? "" : "sky exact " + skyExact + ", deep in front / below " + deepFront;
        foreach (var c in new[] { up, down, up2 })
        {
            if (c == null) continue;
            foreach (var t in c.nodes) if (SequenceMaster.IndexOn(t, c) >= 0) { noOcc = false; occWhy = "layer " + c.layer + " cube in the stack of " + t.gridX + "/" + t.gridZ; }
        }
        var home = src.nodes[0];
        int stackHome = SequenceMaster.CountOn(home, null);
        Vector3 wantUp = RestOn(up, home), wantDn = RestOn(down, home);
        float eu = up != null ? (up.transform.position - wantUp).magnitude : 99f, ed = down != null ? (down.transform.position - wantDn).magnitude : 99f;
        Add(L(noOcc && stackHome == 1 && eu < 0.12f && ed < 0.12f,
              "O4 layer cubes never occupy tiles (the source's home tile holds only the source); at rest ↑ stands on its sky glass tile (tile + Magic.LayerOffset(+1)), ↓ on its deep band tile (Magic.LayerPoint(tile, −1): in front of and below the platform)",
              (occWhy.Length > 0 ? occWhy + "; " : "") + "home stack " + stackHome + ", ↑ off " + F(eu, "F3") + " (at " + V(up != null ? up.transform.position : Vector3.zero) + " want " + V(wantUp) + "), ↓ off " + F(ed, "F3")));
        Add(L(up != null && up.GlassShown && up.LayerPhase < 0.05f && !src.GlassShown,
              "O5 at rest (stopped) a layer cube is faint glass (the hologram shader, alpha ≈ " + F(AudioCube.GlassAlpha) + "); its source stays solid", "glass " + (up != null && up.GlassShown) + ", phase " + F(up != null ? up.LayerPhase : -1f)));
        // the late echo: a leading rest of an eighth on the first tile, the tail shortened by it (the 12-tick last note dropped, the 36 shortened)
        History.Undo(); yield return null;   // back before the ↑↑ copy (the drum cube was never pushed): the world is rebuilt
        kb = SM.Islands[2];
        src = Find(2, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 });
        var late = src != null ? CubeOps.OctaveCopy(src, -1, CubeOps.EchoLateTicks) : null;
        bool lateOk = late != null && late.layer == -1 && late.nodes.Count == 4 && late.ModOf(0) == 1 && late.durs[0] == 12 && late.nodes[0] == src.nodes[0]
                      && late.durs[1] == 24 && late.durs[2] == 24 && late.durs[3] == 36 - 0 && late.nodes[3] == src.nodes[2];
        int total = 0; if (late != null) foreach (int d in late.durs) total += d;
        Add(L(lateOk && total == 96,
              "O6 an echo \"late\": a rest of an eighth (12 ticks) on the first tile, then the notes; the tail loses the same 12 ticks (the last 12-tick note dropped) so one pass stays one pass (96 ticks)",
              late != null ? "nodes " + late.nodes.Count + " " + Tiles(late.nodes) + ", mods " + Ints(late.mods) + ", durs " + Ints(late.durs) + " (Σ " + total + ")" : "no echo (" + CubeOps.LastDeny + ")"));
        // undo removes a copy
        int n2 = SequenceMaster.Cubes.Count;
        History.Undo(); yield return null;
        Add(L(SequenceMaster.Cubes.Count == n2 - 1, "O7 undo removes the last copy", "cubes " + n2 + " → " + SequenceMaster.Cubes.Count));
        if (shots)
        {
            kb = SM.Islands[2];
            src = Find(2, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 });
            Frame(kb.VisualCenter + Vector3.up * 0.4f, 13f, 0f, 30f);
            yield return At(SM.ColumnStart(kb.column) + 0.5, 0.35f);
            yield return Capture("h7_layers");
            // the default camera, playing: the sky copy above, the deep copy in its band in front of the platform, both phasing in on their notes
            if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(2, true); }
            yield return Wait(1.2f);
            GlobalClock.Stop(); GlobalClock.Seek(SM.ColumnStart(kb.column) - 1.0);
            int lt = Synth.LateEvents; GlobalClock.Play();
            yield return Wait((float)(1.4 / GlobalClock.BeatsPerSecond));   // into the first note (beat 0.4 of its column)
            lateExcused += Synth.LateEvents - lt;
            yield return Capture("h7_deep_default");
            GlobalClock.Stop();
            if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        }
    }

    // ================================================================== 2. harmony
    static IEnumerator Harmonies()
    {
        var kb = SM.Islands[4];
        Clear(kb);
        var src = Make(kb, 0, new[] { 0, 1, 2, 3, 4 }, new[] { 0, 1, 2, 1, 0 }, new[] { 24, 24, 24, 12, 12 });
        if (src == null) { Add(L(false, "H harmony", "no source")); yield break; }
        yield return null;
        var key = MusicTheory.KeyOfSong();
        int root; IList<int> semis; Harmony.ChordOf(kb, out root, out semis);
        foreach (bool above in new[] { true, false })
        {
            var plan = CubeOps.PlanHarmony(src, above);
            int n = src.nodes.Count, tr = SongManager.Transpose;
            var srcP = new int[n];
            for (int i = 0; i < n; i++) srcP[i] = VoiceRules.NodeMidi(src, i) - tr;
            var others = new List<IList<int>>();
            for (int i = 0; i < n; i++) { var l = new List<int>(); foreach (int s in semis) l.Add(root + s); others.Add(l); }
            var want = Harmony.Harmonize(srcP, Harmony.WeightsOf(src.durs, n), root, semis, key.tonic, key.minor, others, above, CubeOps.HarmonyLo, CubeOps.HarmonyHi);
            bool pitchesOk = plan != null && Ints(plan.pitch) == Ints(want);
            bool tilesOk = plan != null;
            int perfect = 0;
            for (int i = 0; plan != null && i < n; i++)
            {
                int iv = want[i] - srcP[i];
                if (Mathf.Abs(iv) == 5 || Mathf.Abs(iv) == 7) perfect++;
                tilesOk &= plan.tiles[i] != null && plan.tiles[i].midi + plan.bend[i] == src.nodes[i].midi + iv && plan.tiles[i] != src.nodes[i];
            }
            int p0 = pushes, c0 = SequenceMaster.Cubes.Count;
            var h = CubeOps.Harmonize(src, above);
            bool placed = h != null && pushes == p0 + 1 && SequenceMaster.Cubes.Count == c0 + 1 && h.echoOf == src.id;
            bool same = placed;
            var sound = new List<int>();
            for (int i = 0; placed && i < n; i++)
            {
                same &= h.nodes[i] == plan.tiles[i] && h.BendAt(i) == plan.bend[i] && h.durs[i] == src.durs[i];
                sound.Add(VoiceRules.NodeMidi(h, i) - tr);
            }
            if (h != null) { SequenceMaster.Unregister(h); UnityEngine.Object.DestroyImmediate(h.gameObject); SequenceMaster.RecalculateTimeline(); }   // the next side plans without it
            Add(L(pitchesOk && tilesOk && placed && same && perfect * 2 >= n,
                  "H" + (above ? "1 harmony above" : "2 harmony below") + ": = Harmony.Harmonize over the sounding notes + the chord (4ths / 5ths first), each note on the tile of that pitch or the nearest + a bend (never the source's own tile); one History entry, echoOf",
                  "src " + Ints(srcP) + " → " + (plan != null ? Ints(plan.pitch) : "null") + " (want " + Ints(want) + "), perfect " + perfect + "/" + n + ", tiles " + (plan != null ? Tiles(plan.tiles) : "-") + " bends " + (plan != null ? Ints(plan.bend) : "-") + ", it sounds " + Ints(sound) + ", pushes +" + (pushes - p0)));
        }
        // a clash dodged: another voice sounding a semitone from the harmony's first choice under node j moves the harmony off it. The grid's own
        // tiles are its chord tones (already in the mix), so the other voice is a BENT one (a harmony cube's kind of note: off the chord)
        string found = null;
        Clear(kb);
        src = Make(kb, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 });
        yield return null;
        var before = CubeOps.PlanHarmony(src, true);
        for (int j = 0; before != null && j < src.nodes.Count && found == null; j++)
            for (int side = -1; side <= 1 && found == null; side += 2)
            {
                int iv = before.pitch[j] - before.src[j];
                int cp = before.pitch[j] + side;   // a minor 2nd from the harmony's first choice (sounding, no transpose)
                TileInteraction ct = null; int best = int.MaxValue;
                foreach (var t in kb.tiles) if (t != null && t != src.nodes[j]) { int d = Mathf.Abs(t.midi - cp); if (d < best) { best = d; ct = t; } }
                if (ct == null) continue;
                var xs = new List<int>(); var zs = new List<int>(); var ds = new List<int>(); var ms = new List<int>(); var bs = new List<int>();
                if (j > 0) { xs.Add(ct.gridX); zs.Add(ct.gridZ); ds.Add(24 * j); ms.Add(1); bs.Add(0); }
                xs.Add(ct.gridX); zs.Add(ct.gridZ); ds.Add(24); ms.Add(0); bs.Add(cp - ct.midi);
                if (j < 3) { xs.Add(ct.gridX); zs.Add(ct.gridZ); ds.Add(96 - 24 * (j + 1)); ms.Add(1); bs.Add(0); }
                var other = Make(kb, 3, xs.ToArray(), zs.ToArray(), ds.ToArray(), ms.ToArray());
                if (other == null) continue;
                other.SetPathAndMods(new List<TileInteraction>(other.nodes), new List<int>(other.mods), new List<int>(other.durs), bs);
                yield return null;
                int om = VoiceRules.NodeMidi(other, j > 0 ? 1 : 0) - SongManager.Transpose;
                var after = CubeOps.PlanHarmony(src, true);
                int iv2 = after != null ? after.pitch[j] - after.src[j] : 0;
                int clash = after != null ? ((after.pitch[j] - om) % 12 + 12) % 12 : -1;
                int was = ((before.pitch[j] - om) % 12 + 12) % 12;
                if (after != null && iv2 != iv)
                    found = "node " + j + ": alone +" + iv + " (" + before.pitch[j] + "), another voice sounding " + om + " (a bent note, interval " + was + ") → +" + iv2 + " (" + after.pitch[j] + ", interval " + clash + ")";
                SequenceMaster.Unregister(other); UnityEngine.Object.DestroyImmediate(other.gameObject); SequenceMaster.RecalculateTimeline();
                yield return null;
                if (found != null)
                    Add(L((was == 1 || was == 11) && clash != 1 && clash != 11,
                          "H3 a clash avoided: another cube sounding a semitone from the harmony's first choice under that note moves it to another interval (\"considering the notes that the person has down\")", found));
            }
        if (found == null) Add(L(false, "H3 a clash avoided", "no clash moved the harmony (first choices " + (before != null ? Ints(before.pitch) : "-") + ")"));
        // the preview: ghost beads on the harmony's tiles + both voices, no song change
        int pp = pushes, cc = SequenceMaster.Cubes.Count, pv0 = CubeOps.PreviewCount;
        var pitches = CubeOps.PreviewHarmony(src, true);
        var again = CubeOps.PreviewHarmony(src, true);
        var root2 = GameObject.Find("HarmonyPreview");
        int beads = 0; bool ghostCube = false;
        if (root2 != null) foreach (Transform ch in root2.transform) { if (ch.name == "GhostBead" || ch.name == "GhostRest") beads++; if (ch.name == "GhostCube") ghostCube = true; }
        Frame(kb.VisualCenter + Vector3.up * 0.3f, 11f, 0f, 55f);
        yield return Wait(0.3f);
        yield return Capture("h7_harmony_preview");
        bool previewing = CubeOps.Previewing;
        CubeOps.EndPreview();
        yield return null;
        Add(L(pitches != null && again != null && previewing && beads == src.nodes.Count && ghostCube && CubeOps.PreviewCount == pv0 + 1 && pushes == pp && SequenceMaster.Cubes.Count == cc && !CubeOps.Previewing,
              "H4 the preview: ghost beads on the harmony's tiles (one per node) + a hologram of the second cube where it would start, once per hover (a repeat call keeps it), no cube, no History; EndPreview clears it",
              "pitches " + Ints(pitches) + ", beads " + beads + ", ghost cube " + ghostCube + ", previews +" + (CubeOps.PreviewCount - pv0) + ", pushes +" + (pushes - pp)));
        // a bent note tilts the body toward its bend (at rest on a node bent +3: rolled ≈ 7.5° to the right; unbent: upright)
        var bentCube = Make(kb, 0, new[] { 4, 5 }, new[] { 0, 0 }, new[] { 24, 24 });
        bentCube.SetPathAndMods(new List<TileInteraction>(bentCube.nodes), new List<int>(bentCube.mods), new List<int>(bentCube.durs), new List<int> { 3, 0 });
        yield return Wait(0.5f);
        float roll = Vector3.SignedAngle(Vector3.up, bentCube.transform.up, Vector3.forward);
        float upright = Vector3.Angle(Vector3.up, src.transform.up);
        Add(L(roll < -5f && roll > -10f && upright < 1f, "H5 a harmony cube's bent node tilts the body a little toward its bend (+3 semitones: a roll to the right); an unbent cube stays upright",
              "roll " + F(roll, "F1") + "°, the plain cube " + F(upright, "F1") + "°"));
    }

    // ================================================================== 3. flip
    static IEnumerator Flips()
    {
        var kb = SM.Islands[5];
        Clear(kb);
        var c = Make(kb, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 12, 12, 48 }, new[] { 0, 3, 0, 1 });
        if (c == null) { Add(L(false, "F flip", "no cube")); yield break; }
        c.SetPathAndMods(new List<TileInteraction>(c.nodes), new List<int>(c.mods), new List<int>(c.durs), new List<int> { 0, 2, 0, -1 });   // bends to see them turn
        yield return null;
        int p0 = pushes, f0 = CubeOps.FlipCount;
        var r = CubeOps.Flip(c);
        bool ok = r == c && pushes == p0 + 1 && CubeOps.FlipCount == f0 + 1;
        var want = new[] { kb.cols - 1, kb.cols - 2, kb.cols - 3, kb.cols - 4 };
        var wantZ = new[] { kb.rows - 1, kb.rows - 2, kb.rows - 3, kb.rows - 2 };
        for (int i = 0; ok && i < 4; i++) ok = c.nodes[i].gridX == want[i] && c.nodes[i].gridZ == wantZ[i];
        bool kept = Ints(c.durs) == "24,12,12,48" && Ints(c.mods) == "0,3,0,1" && Ints(c.bend) == "0,-2,0,1";
        Add(L(ok && kept, "F1 flip: the path upside down (x → cols − 1 − x, z → rows − 1 − z), stickers and lengths stay on their notes, bends turn the other way; one History entry",
              "tiles " + Tiles(c.nodes) + ", durs " + Ints(c.durs) + ", mods " + Ints(c.mods) + ", bends " + Ints(c.bend) + ", pushes +" + (pushes - p0)));
        CubeOps.Flip(c);
        bool back = c.nodes[0].gridX == 0 && c.nodes[1].gridX == 1 && c.nodes[2].gridZ == 2 && Ints(c.bend) == "0,2,0,-1";
        Add(L(back, "F2 flipping twice gives the path back", Tiles(c.nodes)));
        if (shots) { Frame(kb.VisualCenter + Vector3.up * 0.3f, 11f, 0f, 55f); CubeOps.Flip(c); yield return Wait(0.15f); yield return Capture("h7_flip"); }
        // a keyboard: the keys mirror within the path's own range
        int ki = SM.AddKeyboardIsland(SM.Islands.IndexOf(kb), false);
        yield return Wait(0.3f);
        var kk = ki >= 0 && ki < SM.Islands.Count ? SM.Islands[ki] : null;
        if (kk == null || !kk.IsKeyboard) { Add(L(false, "F3 keyboard flip", "no keyboard island")); yield break; }
        var kc = Make(kk, 0, new[] { 3, 5, 10, 7 }, new[] { 0, 0, 0, 0 }, new[] { 24, 24, 24, 24 });
        yield return null;
        CubeOps.Flip(kc);
        Add(L(Ints(XsOf(kc)) == "10,8,3,6", "F3 a keyboard melody mirrors its keys inside its own range ((lowest + highest) − key: 3 5 10 7 → 10 8 3 6)", "keys " + Ints(XsOf(kc))));
    }

    // ================================================================== 4. paste modes
    static IEnumerator Pastes()
    {
        var sm = SM;
        var home = sm.Islands[1];
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == home && !c.IsDrums) { src = c; break; }
        if (src == null) { Add(L(false, "P paste", "no source")); yield break; }
        Clipboard.Copy(src);
        var next = Clipboard.NextGrid(home);
        if (next == null) { Add(L(false, "P paste", "no next grid")); yield break; }
        int ni = sm.Islands.IndexOf(next);
        var plain = Clipboard.AdaptedFor(next);
        // echo up onto the next grid: the plain adapted tiles, one layer up
        int p0 = pushes;
        var eu = Clipboard.PasteNext(Clipboard.PasteMode.EchoUp);
        bool euOk = eu != null && eu.layer == src.layer + 1 && eu.echoOf == src.id && eu.Island == next && pushes == p0 + 1 && Clipboard.LastMode == Clipboard.PasteMode.EchoUp;
        for (int i = 0; euOk && i < plain.xs.Length; i++) euOk = eu.nodes[i].gridX == plain.xs[i] && eu.nodes[i].gridZ == plain.zs[i];
        bool appears = eu != null && eu.BodyVisible && eu.PathVisible;   // §21: nothing flies across — visible on its grid at once
        euOk &= appears;
        Add(L(euOk, "P1 paste as echo ↑ onto the next grid: the adapted pattern (the plain paste's tiles) one octave layer up, echoOf the source; one History entry; §21: it APPEARS there at once (no flight)",
              eu != null ? "layer " + eu.layer + ", tiles " + Tiles(eu.nodes) + " (plain " + Ints(plain.xs) + " / " + Ints(plain.zs) + "), pushes +" + (pushes - p0) : "null"));
        // echo down on its own grid: a doubling (no adaptation)
        int p1 = pushes;
        var ed = Clipboard.PasteOn(home, Clipboard.PasteMode.EchoDown);
        bool edOk = ed != null && ed.layer == src.layer - 1 && ed.Island == home && pushes == p1 + 1 && ed.nodes.Count == src.nodes.Count;
        for (int i = 0; edOk && i < src.nodes.Count; i++) edOk = ed.nodes[i] == src.nodes[i];
        Add(L(edOk, "P2 echo ↓ on the source's own grid = a doubling one layer down on exactly its tiles", ed != null ? "layer " + ed.layer + ", tiles " + Tiles(ed.nodes) + " vs " + Tiles(src.nodes) : "null"));
        // late: an eighth late
        Clipboard.EchoLate = true;
        var el = Clipboard.PasteNext(Clipboard.PasteMode.EchoDown);
        Clipboard.EchoLate = false;
        int tot = 0; if (el != null) foreach (int d in el.durs) tot += d;
        float winBeats = src.windows.Count > 0 ? src.windows[0].length : 4f;
        Add(L(el != null && el.layer == src.layer - 1 && el.ModOf(0) == 1 && el.HasDurations && el.durs[0] == 12 && tot == Mathf.RoundToInt(winBeats * 24),
              "P3 echo \"late\": the echo starts with an eighth's rest and keeps one pass one pass long", el != null ? "mods " + Ints(el.mods) + ", durs " + Ints(el.durs) + " (Σ " + tot + ")" : "null"));
        // answer: the last sounding note resolves to the target chord's root / 3rd
        var an = Clipboard.PasteNext(Clipboard.PasteMode.Answer);
        int root; IList<int> semis; Harmony.ChordOf(next, out root, out semis);
        var ap = new int[plain.xs.Length];
        var mods = Clipboard.State.ModsOrDerived();
        for (int i = 0; i < ap.Length; i++) ap[i] = i < mods.Length && mods[i] == 1 ? -1 : next.GetTile(plain.xs[i], plain.zs[i]).midi;
        var want = Harmony.Answer(ap, Clipboard.Weights, root, semis, Harmony.Pool(next));
        bool anOk = an != null && an.layer == src.layer && an.nodes.Count == ap.Length;
        var got = new List<int>();
        for (int i = 0; an != null && i < an.nodes.Count; i++) { got.Add(ap[i] < 0 ? -1 : an.nodes[i].midi); if (anOk && ap[i] >= 0) anOk = an.nodes[i].midi == want[i]; }
        int last = -1; for (int i = ap.Length - 1; i >= 0; i--) if (ap[i] >= 0) { last = i; break; }
        int lastPc = last >= 0 && an != null ? Harmony.Pc(an.nodes[last].midi - root) : -1;
        Add(L(anOk && (lastPc == 0 || lastPc == 3 || lastPc == 4), "P4 paste as answer: the adapted pattern whose last note resolves to the chord's root / 3rd (Harmony.Answer)",
              "adapted " + Ints(ap) + " → " + Ints(got) + " (want " + Ints(want) + "), last note " + (lastPc == 0 ? "root" : lastPc == 3 || lastPc == 4 ? "3rd" : "pc " + lastPc)));
        // step up / down: the source's pitches one scale step up / down (Harmony.StepShift) on the next grid's nearest tiles
        var key = MusicTheory.KeyOfSong();
        var sp = new int[src.nodes.Count];
        for (int i = 0; i < sp.Length; i++) sp[i] = src.ModOf(i) == 1 ? -1 : src.nodes[i].midi;
        foreach (int dir in new[] { 1, -1 })
        {
            var line = Harmony.StepShift(sp, dir, key.tonic, key.minor);
            int[] wx, wz; Harmony.MelodyToGrid(line, Harmony.MidiTable(next), out wx, out wz);
            int pS = pushes;
            var st = Clipboard.PasteNext(dir > 0 ? Clipboard.PasteMode.StepUp : Clipboard.PasteMode.StepDown);
            bool ok = st != null && pushes == pS + 1 && st.Island == next && st.nodes.Count == wx.Length;
            for (int i = 0; ok && i < wx.Length; i++) ok = st.nodes[i].gridX == wx[i] && st.nodes[i].gridZ == wz[i];
            Add(L(ok, "P" + (dir > 0 ? "5 paste as step ↑" : "6 paste as step ↓") + ": the source's pitches one scale step " + (dir > 0 ? "up" : "down") + " (Harmony.StepShift) on the next grid's nearest tiles (MelodyToGrid); one History entry",
                  "src " + Ints(sp) + " → " + Ints(line) + ", tiles " + (st != null ? Tiles(st.nodes) + " = " + Midis(st.nodes) : "null") + " (want " + Ints(wx) + " / " + Ints(wz) + ")"));
        }
        // refusals: an echo needs a layer left (± MaxLayer); stairs take no paste
        Clipboard.Copy(ed);
        var ed2 = Clipboard.PasteOn(home, Clipboard.PasteMode.EchoDown);   // layer −2
        bool atLimit = ed2 != null && ed2.layer == -ProjectConfig.MaxLayer;
        if (ed2 != null) Clipboard.Copy(ed2);
        bool refused = !Clipboard.CanPasteOn(next, Clipboard.PasteMode.EchoDown) && Clipboard.CanPasteOn(next, Clipboard.PasteMode.EchoUp) && Clipboard.PasteNext(Clipboard.PasteMode.EchoDown) == null;
        Add(L(atLimit && refused, "P7 an echo needs a layer left: from layer −2 an echo ↓ is refused (↑ still goes)", "echo of an echo: layer " + (ed2 != null ? ed2.layer : -9) + ", refused " + refused));
        if (shots)
        {
            Clipboard.Copy(src);
            Frame((home.VisualCenter + next.VisualCenter) * 0.5f + Vector3.up * 1f, 17f, 0f, 38f);
            Clipboard.PasteNext(Clipboard.PasteMode.EchoUp);
            yield return Wait(0.3f);
            yield return Capture("h7_paste_echo");
        }
        yield return null;
        Info("paste target " + ni + " (" + next.assignedChord + ")");
    }

    // ================================================================== 5. varied passes and flow tiles
    static IEnumerator TilesVaryFlow()
    {
        var sm = SM;
        int hi = 1;
        var c0 = Find(hi, 2, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1, 2, 2, 1, 0 });
        if (c0 == null) { Add(L(false, "T tiles", "no cube on island 1")); yield break; }
        int inst = c0.instrument;
        var xs = XsOf(c0); var zs = ZsOf(c0);
        sm.SetRepeat(hi, 3);
        sm.SetVary(hi, 1);
        yield return Wait(0.2f);
        var c = Find(hi, inst, xs, zs);
        var kb = sm.Islands[hi];
        if (c == null) { Add(L(false, "T vary", "cube lost")); yield break; }
        int root; IList<int> semis; Harmony.ChordOf(kb, out root, out semis);
        var key = MusicTheory.KeyOfSong();
        int n = c.nodes.Count;
        var src = new int[n]; for (int i = 0; i < n; i++) src[i] = c.ModOf(i) == 1 ? -1 : c.nodes[i].midi;
        var why = new StringBuilder(); bool ok = true; int changed = 0, windows = 0;
        for (int w = 0; w < c.windows.Count; w++)
        {
            var win = c.windows[w];
            if (win.carried || win.island != kb) continue;
            windows++;
            var v = Harmony.Vary(src, Harmony.WeightsOf(null, n), win.pass, 3, c.seed, 1, root, semis, key.tonic, key.minor, Harmony.Pool(kb));
            for (int i = 0; i < n; i++)
            {
                var t = c.TileAt(w, i);
                var exp = win.pass == 0 || v[i] == src[i] ? c.nodes[i] : CubeOps.TileFor(kb, v[i], c.nodes[i], false);
                if (t != exp) { ok = false; why.Append("pass " + win.pass + " node " + i + " " + (t != null ? t.midi : -1) + " want " + (exp != null ? exp.midi : -1) + "; "); }
                if (win.pass > 0 && t != c.nodes[i]) changed++;
            }
            why.Append("pass " + win.pass + ": ");
            for (int i = 0; i < n; i++) why.Append(c.TileAt(w, i).midi).Append(i + 1 < n ? "," : "; ");
        }
        int b0 = AudioCube.VaryBuilds;
        for (int k = 0; k < 10; k++) c.TileAt(1, 1);
        yield return null;
        c.TileAt(1, 1);
        Add(L(ok && windows == 3 && changed > 0 && AudioCube.VaryBuilds == b0,
              "T1 vary: passes 2 and 3 play Harmony.Vary's notes (one or two light notes an octave / a neighbour away) on the nearest tiles, pass 1 as drawn; cached (no rebuild between reads)",
              "windows " + windows + ", varied notes " + changed + ", rebuilds +" + (AudioCube.VaryBuilds - b0) + " — " + why));
        // vary 2: the last pass also answers (its last note → root / 3rd)
        sm.SetVary(hi, 2);
        yield return Wait(0.2f);
        c = Find(hi, inst, xs, zs);
        int lastW = -1; for (int w = 0; c != null && w < c.windows.Count; w++) if (!c.windows[w].carried && c.windows[w].pass == 2) lastW = w;
        int lp = -1;
        if (lastW >= 0) { var t = c.TileAt(lastW, n - 1); lp = Harmony.Pc(t.midi - root); }
        Add(L(lastW >= 0 && (lp == 0 || lp == 3 || lp == 4), "T2 vary \"answer\": the last pass ends on the chord's root / 3rd", "last note pc " + lp));
        // flow: carried windows by pedal, hop 1 from home, hop 2 from hop 1
        sm.SetVary(hi, 0); sm.SetRepeat(hi, 1);
        sm.SetCarry(hi, 2, 1);
        yield return Wait(0.2f);
        c = Find(hi, inst, xs, zs);
        var targets = sm.CarryTargets(hi);
        if (c == null || targets.Count < 2) { Add(L(false, "T3 flow", "cube " + (c != null) + ", targets " + targets.Count)); yield break; }
        int w1 = -1, w2 = -1, flows = 0;
        for (int w = 0; w < c.windows.Count; w++) { var wi = c.windows[w]; if (!wi.carried) continue; if (wi.flow) flows++; if (wi.hop == 1 && w1 < 0) w1 = w; if (wi.hop == 2 && w2 < 0) w2 = w; }
        IList<TileInteraction> prev = c.nodes;
        var got = new StringBuilder(); bool fok = w1 >= 0 && w2 >= 0;
        var ws = Harmony.WeightsOf(null, n);
        for (int h = 0; fok && h < 2; h++)
        {
            var T = targets[h];
            var p = new int[n]; for (int i = 0; i < n; i++) p[i] = c.ModOf(i) == 1 ? -1 : prev[i].midi;
            int r2; IList<int> s2; Harmony.ChordOf(T, out r2, out s2);
            var ped = Harmony.AdaptPedal(p, ws, r2, s2, key.tonic, key.minor, Harmony.Pool(T));
            var list = new List<TileInteraction>();
            for (int i = 0; i < n; i++)
            {
                var near = T.GetTile(Mathf.Clamp(prev[i].gridX, 0, T.cols - 1), Mathf.Clamp(prev[i].gridZ, 0, T.rows - 1));
                var e = ped[i] < 0 ? near : (CubeOps.TileFor(T, ped[i], near, false) ?? near);
                list.Add(e);
                if (c.TileAt(h == 0 ? w1 : w2, i) != e) fok = false;
            }
            int kept = 0; for (int i = 0; i < n; i++) if (ped[i] == p[i]) kept++;
            got.Append("hop " + (h + 1) + ": " + Ints(p) + " → " + Midis(list) + " (" + kept + " held); ");
            prev = list;
        }
        Add(L(fok && flows >= 2, "T3 flow windows play the pattern adapted by PEDAL (Harmony.AdaptPedal, hop 2 from hop 1): what fits the next chord stays, only what has to move moves", "flow windows " + flows + " — " + got));
    }

    // ================================================================== 6. motions sampled at beats (paused: the pose of that beat)
    static IEnumerator Motions()
    {
        var sm = SM;
        // --- the layer ride + phase-in: a copy of a cube with a rest (notes at 0, 2, 3; a rest at 1)
        var kb = sm.Islands[2];
        Clear(kb);
        var src = Make(kb, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 }, new[] { 0, 1, 0, 0 });
        var up = src != null ? CubeOps.OctaveCopy(src, +1) : null;
        if (up == null) { Add(L(false, "M layer", "no copy")); yield break; }
        yield return Wait(0.7f);
        double S = sm.ColumnStart(kb.column);
        yield return At(S + 0.5);
        Vector3 want = RestOn(up, src.nodes[0]);
        float off = (up.transform.position - want).magnitude, phMid = up.LayerPhase;
        bool solidMid = !up.GlassShown;
        yield return At(S + 1.1);
        float phAfter = up.LayerPhase;
        yield return At(S + 1.5);
        float phRest = up.LayerPhase; bool glassRest = up.GlassShown;
        yield return At(S + 2 - AudioCube.PhaseInBeats * 0.5);
        float phBefore = up.LayerPhase;
        Add(L(off < 0.15f && phMid > 0.99f && solidMid && Mathf.Abs(phAfter - 0.5f) < 0.2f && phRest < 0.01f && glassRest && Mathf.Abs(phBefore - 0.5f) < 0.2f,
              "M1 the layer ride: mid-note the copy stands at its tile + LayerOffset, SOLID (phase 1); it fades back to glass after the note (0.1 beat after: ≈ 0.5), is glass in the rest (phase 0), and phases in just before its next note (half way: ≈ 0.5)",
              "offset " + F(off, "F3") + ", phase mid " + F(phMid) + " (solid " + solidMid + "), +0.1 after " + F(phAfter) + ", rest " + F(phRest) + " (glass " + glassRest + "), 0.06 before " + F(phBefore)));
        if (shots)
        {
            Frame(kb.VisualCenter + Vector3.up * 1.2f, 13f, 0f, 25f);
            yield return At(S + 0.5, 0.3f); yield return Capture("h7_layer_phase");
            yield return At(S + 1.5, 0.3f); yield return Capture("h7_layer_glass");
        }
        GlobalClock.Stop(); AudioCube.SnapAllHome();
        yield return null;

        // --- the rewind zip: island 1 (a 6-node path, one bar) × 3 with rewind
        int ri = 1;
        var rc = Find(ri, 2, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1, 2, 2, 1, 0 });
        if (rc != null)
        {
            var rxs = XsOf(rc); var rzs = ZsOf(rc);
            sm.SetRepeat(ri, 3); sm.SetRewind(ri, true);
            yield return Wait(0.2f);
            rc = Find(ri, 2, rxs, rzs);
            var rk = sm.Islands[ri];
            double pl = sm.PassLength(rk.column), p0s = sm.ColumnStart(rk.column), rb = SongManager.RewindBeatsOf(pl);
            int rp;
            float phase = sm.RewindPhase(rk, p0s + pl - rb * 0.5, out rp);
            yield return At(p0s + pl - rb * 0.75, 0.05f);
            float x25 = rc.transform.position.x; bool z25 = rc.Zipping;
            yield return At(p0s + pl - rb * 0.5, 0.05f);
            float x50 = rc.transform.position.x, u50 = rc.ZipU; bool z50 = rc.Zipping; Vector3 pos50 = rc.transform.position;
            if (shots) { Frame(rk.VisualCenter + Vector3.up * 0.5f, 12f, 0f, 45f); yield return Capture("h7_rewind_zip"); }
            yield return At(p0s + pl - rb * 0.02, 0.05f);
            Vector3 endPos = rc.transform.position;
            var first = rc.TileAt(1, rc.HomeNode);
            float dEnd = first != null ? Flat(endPos, first.Top) : 99f;
            float tileTop = rc.nodes[0].Top.y;
            Add(L(Mathf.Abs(phase - 0.5f) < 0.01f && z25 && z50 && x50 < x25 - 0.05f && Mathf.Abs(u50 - Mathf.Pow(0.5f, 2.2f)) < 0.02f && pos50.y > tileTop + 0.2f && dEnd < 0.35f,
                  "M2 the rewind zip: in the unwind (SongManager.RewindPhase) the body zips BACKWARD through its path (x falling from ¼ to ½ of the unwind, slow then fast: u = phase^2.2), lifted, and sits on its first tile at the pass boundary",
                  "phase " + F(phase) + ", zipping " + z25 + "/" + z50 + ", x " + F(x25) + " → " + F(x50) + ", u(½) " + F(u50, "F3") + ", y " + F(pos50.y) + " (tile " + F(tileTop) + "), end " + F(dEnd, "F3") + " u from the first tile"));
            GlobalClock.Stop();
            sm.SetRewind(ri, false); sm.SetRepeat(ri, 1);
            yield return Wait(0.2f);
        }
        else Add(L(false, "M2 rewind zip", "no cube on island 1"));

        // --- §21 the LONG GRID: island 1 extends over the next two grids; its cube walks left → right, stepping into each next measure with an
        // ordinary hop (no glide, no arc, no flight)
        rc = Find(ri, 2, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1, 2, 2, 1, 0 });
        if (rc != null)
        {
            var rxs = XsOf(rc); var rzs = ZsOf(rc);
            sm.SetCarry(ri, 2, 1);
            yield return Wait(0.2f);
            rc = Find(ri, 2, rxs, rzs);
            var rk = sm.Islands[ri];
            var tg = sm.CarryTargets(ri);
            var why = new StringBuilder(); bool walkOk = tg.Count >= 2 && rc != null;
            int crossings = 0; float worstLift = 0f;
            for (int m = 0; walkOk && m < 2; m++)
            {
                var fromIsl = m == 0 ? rk : tg[0]; var toIsl = tg[m];
                double e = sm.ColumnStart(fromIsl.column) + sm.PassLength(fromIsl.column);   // the measure boundary
                yield return At(e - 0.3, 0.03f);
                float xBefore = rc.transform.position.x;
                yield return At(e - 0.2, 0.03f);
                Vector3 mid = rc.transform.position;
                bool hop = rc.Hopping && rc.CrossingMeasure && !rc.Flying && !rc.Gliding && !rc.Flinging;
                // an ordinary hop: its lift over the straight line between the two tiles stays a hop's (cubeHopIntensity), never a flight's arc
                Vector3 A = rc.transform.position; float lift = 0f;
                var ta = rc.RestingTile;
                yield return At(e + 0.1, 0.25f);
                Vector3 after = rc.transform.position;
                bool landed = rc.RestingTile != null && rc.RestingTile.island == toIsl;
                float yLine = Mathf.Max(rk.tiles[0].Top.y, toIsl.tiles[0].Top.y);
                float topMax = float.MinValue; foreach (var t in fromIsl.tiles) topMax = Mathf.Max(topMax, t.Top.y); foreach (var t in toIsl.tiles) topMax = Mathf.Max(topMax, t.Top.y);
                lift = mid.y - (topMax + ProjectConfig.CubeSize * rc.SizeFactor * 0.5f);
                worstLift = Mathf.Max(worstLift, lift);
                bool rightward = xBefore < mid.x && mid.x < after.x;
                if (hop) crossings++;
                why.Append("measure " + (m + 1) + " → " + (m + 2) + ": hop " + hop + ", x " + F(xBefore) + " → " + F(mid.x) + " → " + F(after.x) + ", lift over the tiles " + F(lift) + ", landed on the next measure " + landed + "; ");
                if (!hop || !landed || !rightward || lift > ProjectConfig.cubeHopIntensity + 0.15f) walkOk = false;
                if (m == 0 && shots)
                {
                    yield return At(e - 0.2, 0.05f);
                    Frame((rk.VisualCenter + tg[0].VisualCenter) * 0.5f + Vector3.up * 0.5f, 15f, 0f, 42f);
                    yield return Wait(0.1f);
                    yield return Capture("h7_longgrid_walk");
                }
            }
            Add(L(walkOk && crossings == 2, "M3 §21 the long grid: the cube walks LEFT → RIGHT through the grids it extends over — into each next measure with an ORDINARY HOP (as low as any hop: no glide, no arc, no flight), landing on that measure's tile",
                  why.ToString()));
            GlobalClock.Stop();
            sm.SetCarry(ri, 0, 0);
            yield return Wait(0.2f);
        }

        // --- §21.4 the launch is a build-up only: around its turn's end the launching grid's cube stays on its grid (no fling)
        kb = sm.Islands[2];
        Clear(kb);
        Make(kb, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 });
        sm.SetLaunch(2, true);
        yield return Wait(0.2f);
        kb = sm.Islands[2];
        var fc = Find(2, 0, new List<int> { 0, 1, 2, 3 }, new List<int> { 0, 1, 2, 1 });
        if (fc == null) { Add(L(false, "M4 launch", "no cube")); yield break; }
        float turnEnd = sm.TurnEnd(kb);
        int fl0 = AudioCube.FlingCount, outside = 0, samples = 0;
        bool gst; var lastT = fc.EndTileOf(fc.windows.Count - 1, out gst);
        for (double b2 = turnEnd - 0.8; b2 <= turnEnd + 0.8; b2 += 0.1)
        {
            yield return At(b2, 0.03f);
            samples++;
            var bnd = kb.VisualBounds; bnd.Expand(new Vector3(0.6f, 100f, 0.6f));
            if (!bnd.Contains(fc.transform.position) || fc.Flinging || fc.transform.position.y > kb.tiles[0].Top.y + 2f) outside++;
        }
        yield return At(turnEnd + 0.3, 0.25f);
        bool held = fc.RestingTile == lastT && lastT.island == kb && fc.Holding;
        Add(L(outside == 0 && AudioCube.FlingCount == fl0 && held && kb.launch,
              "M4 §21.4 a launching grid's cubes do NOT move off it: around the turn's end (TurnEnd ± 0.8 beat) the cube stays on its own platform (no fling), then holds on its last tile",
              samples + " samples, outside / flung " + outside + ", flings +" + (AudioCube.FlingCount - fl0) + ", after: resting " + (fc.RestingTile != null ? sm.Islands.IndexOf(fc.RestingTile.island) + " " + fc.RestingTile.gridX + "/" + fc.RestingTile.gridZ : "-") + " (own grid " + sm.Islands.IndexOf(kb) + "), holding " + fc.Holding));
        if (shots) { Frame(kb.VisualCenter + Vector3.up * 0.5f, 14f, 0f, 45f); yield return At(turnEnd + 0.3, 0.2f); yield return Capture("h7_launch_stays"); }
        GlobalClock.Stop();
    }

    // ================================================================== 7. stairs runners
    static IEnumerator Runners()
    {
        var sm = SM;
        int si = sm.AddStairIsland(0, false, 0);
        yield return Wait(0.4f);
        if (si < 0 || si >= sm.Islands.Count || !sm.Islands[si].IsStairs) { Add(L(false, "R stairs", "AddStairIsland refused (" + si + ")")); yield break; }
        var st = sm.Islands[si];
        AudioCube run = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == st) { run = c; break; }
        int[] xs, mods, durs;
        sm.StairPath(st, out xs, out mods, out durs);
        bool pathOk = run != null && run.IsRunner && run.nodes.Count == xs.Length;
        for (int i = 0; pathOk && i < xs.Length; i++) pathOk = run.nodes[i].gridX == xs[i] && run.ModOf(i) == mods[i] && run.durs[i] == durs[i];
        Add(L(pathOk, "R1 a stairs island's cube is a RUNNER whose path is SongManager.StairPath (the lead-in rest, then every step at the rate)",
              run != null ? "path " + Ints(XsOf(run)) + " mods " + Ints(run.mods) + " durs " + Ints(run.durs) + " (want " + Ints(xs) + " / " + Ints(mods) + " / " + Ints(durs) + ")" : "no runner"));
        if (run == null) yield break;
        // refresh: a runner knocked off its run takes the run back
        var wrong = new List<TileInteraction> { st.GetTile(0, 0), st.GetTile(1, 0) };
        run.SetPathAndMods(wrong, new List<int> { 0, 0 }, new List<int> { 48, 48 });
        bool changed = run.RefreshRunner(), again = run.RefreshRunner();
        bool back = run.nodes.Count == xs.Length;
        for (int i = 0; back && i < xs.Length; i++) back = run.nodes[i].gridX == xs[i];
        Add(L(changed && !again && back, "R2 RefreshRunner re-derives a runner's path from StairPath when it differs (once)", "changed " + changed + ", second " + again + ", path " + Ints(XsOf(run))));
        // the fall: at each step's onset the runner stands on that step; mid-way it falls between two steps; after the run it lands on the next column's ground
        double S = sm.ColumnStart(st.column);
        var ev = new List<AudioCube.TimelineEvent>();
        run.TimelineEvents(0, ev);
        var ys = new StringBuilder(); bool stepsOk = true; int stepN = 0; float prevY = float.MaxValue; bool falling = st.stairDir <= 0;
        foreach (var e in ev)
        {
            if (e.rest) continue;
            yield return At(S + e.start + 0.03, 0.25f);
            var t = run.TileAt(0, e.node);
            float want = t.Top.y + ProjectConfig.CubeSize * run.SizeFactor * 0.5f;
            float y = run.transform.position.y;
            ys.Append(F(y)).Append(" (").Append(F(want)).Append(") ");
            if (Mathf.Abs(y - want) > 0.12f || Flat(run.transform.position, t.Top) > 0.1f) stepsOk = false;
            if (stepN > 0 && (falling ? y > prevY - 0.2f : y < prevY + 0.2f)) stepsOk = false;
            prevY = y; stepN++;
        }
        Add(L(stepsOk && stepN == st.stairSteps, "R3 the stair fall: on each note the runner stands on that step — " + (falling ? "lower" : "higher") + " by a step each time", "steps " + stepN + ": y " + ys));
        // mid-fall between the first two notes: below the upper step, above the lower one, moving toward it
        int n0 = -1, n1 = -1; float o0 = 0f, o1 = 0f;
        foreach (var e in ev) { if (e.rest) continue; if (n0 < 0) { n0 = e.node; o0 = e.start; } else { n1 = e.node; o1 = e.start; break; } }
        if (n1 >= 0)
        {
            float len = o1 - o0, hs = Mathf.Max(ProjectConfig.snapThreshold, 1f - ProjectConfig.HopMaxBeats / len);
            yield return At(S + o0 + len * (hs + (1f - hs) * 0.7f), 0.03f);
            float yA = run.TileAt(0, n0).Top.y, yB = run.TileAt(0, n1).Top.y, y = run.transform.position.y - ProjectConfig.CubeSize * run.SizeFactor * 0.5f;
            bool hop70 = run.Hopping;
            if (shots) { Frame(st.VisualCenter + Vector3.up * 0.3f, 9f, 0f, 20f); yield return Capture("h7_stair_fall"); }
            yield return At(S + o0 + len * (hs + (1f - hs) * 0.9f), 0.03f);
            float y9 = run.transform.position.y - ProjectConfig.CubeSize * run.SizeFactor * 0.5f;
            // v9: the steps sit at their pitch heights and the runner hops up off its step before it drops (AudioCube.StairHop's pre-fall lift,
            // StairHopLift): mid-fall it is above the lower step and at most that lift over the upper one, and still dropping onto the next
            bool mid = hop70 && y < Mathf.Max(yA, yB) + StairHopLift + 0.05f && y > Mathf.Min(yA, yB) - 0.05f && (falling ? y9 < y - 0.02f : y9 > y + 0.02f);
            Add(L(mid, "R4 mid-fall (70 % through the drop) the runner is between the two steps (or in its pre-fall hop over the upper one), still dropping onto the next at 90 %",
                  "floor y " + F(y) + " → " + F(y9) + " at 90 %, steps " + F(yA) + " and " + F(yB) + ", hopping " + hop70));
        }
        // §21: after its run the runner HOLDS on its last step (it no longer jumps onto the next grid); after the reset it climbs back up its own
        // stairs, step by step, to its first step
        int w0 = -1; for (int w = 0; w < run.windows.Count; w++) if (!run.windows[w].carried) w0 = w;
        double end = run.windows[w0].start + run.windows[w0].length;
        bool g0; var lastStep = run.EndTileOf(w0, out g0);
        yield return At(end + 0.3, 0.3f);
        bool holdsLast = run.RestingTile != null && run.RestingTile.island == st && run.RestingTile == lastStep && run.Holding;
        // its stairs play again right at the loop point (column 0): the climb runs in the last beats before it and lands on the first step on beat 0
        float total = GlobalClock.TotalBeats;
        yield return At(total - 0.3, 0.03f);
        Vector3 cm = run.transform.position; bool climbing = run.Tripping;
        var vb = st.VisualBounds; vb.Expand(new Vector3(0.6f, 100f, 0.6f));
        bool onStairs = vb.Contains(cm);
        yield return At(0.2, 0.3f, 1);
        bool home = run.RestingTile == run.HomeTile && run.RestingTile.island == st;
        Add(L(holdsLast && climbing && onStairs && home,
              "R5 §21 after its run the runner HOLDS on its last step (no jump onto the next grid); before its stairs play again it climbs back up its own stairs, step by step, onto its first step",
              "after the run on " + (lastStep != null ? "step " + lastStep.gridX : "-") + " holding " + holdsLast + "; mid-climb tripping " + climbing + " at " + V(cm) + " on the stairs " + onStairs + "; then home " + home));
        GlobalClock.Stop();
    }

    // ================================================================== 8. hold then return (real time)
    static IEnumerator Holds()
    {
        var sm = SM;
        // a cube of the fixture's column 3 whose last tile is not its home tile, and one of column 0 (it plays again right at the reset)
        AudioCube c3 = null, c0 = null;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.IsOnMoon || c.windows.Count == 0) continue;
            int w = c.windows.Count - 1;
            bool guest; var endT = c.EndTileOf(w, out guest);
            if (c3 == null && c.Island != null && c.Island.column == 3 && endT != c.HomeTile) c3 = c;
            if (c0 == null && c.Island != null && c.Island.column == 0 && endT != c.HomeTile && c.nodes.Count >= 2) c0 = c;
        }
        if (c3 == null || c0 == null) { Add(L(false, "L hold", "no suitable cubes (" + (c3 != null) + ", " + (c0 != null) + ")")); yield break; }
        var kb3 = c3.Island;
        double s3 = c3.windows[0].start, e3 = c3.windows[c3.windows.Count - 1].start + c3.windows[c3.windows.Count - 1].length;
        bool g; var last3 = c3.EndTileOf(c3.windows.Count - 1, out g);
        // mid-song, long after its turn: still on its last tile (no teleport home)
        yield return At(e3 + 3.0, 0.3f);
        bool held = c3.RestingTile == last3 && c3.Holding && Flat(c3.transform.position, last3.Top) < 0.05f;
        Add(L(held, "L1 mid-song, 3 beats after its grid ended, the cube is still on its LAST tile (not teleported home)",
              "resting " + (c3.RestingTile != null ? c3.RestingTile.gridX + "/" + c3.RestingTile.gridZ : "-") + ", last " + last3.gridX + "/" + last3.gridZ + ", home " + c3.HomeTile.gridX + "/" + c3.HomeTile.gridZ));
        if (shots) { Frame(kb3.VisualCenter + Vector3.up * 0.5f, 11f, 0f, 50f); yield return Capture("h7_hold"); }
        // across the loop: play from 1.6 beats before the loop point to 3 beats after, in real time
        GlobalClock.Stop();
        float total = GlobalClock.TotalBeats;
        GlobalClock.Seek(total - 1.6);
        GlobalClock.LoopIndex = 1;
        int late = Synth.LateEvents;
        GlobalClock.Play();
        yield return null;
        lateExcused += Synth.LateEvents - late;
        Vector3 prev3 = c3.transform.position, prev0 = c0.transform.position;
        float maxJump3 = 0f, maxJump0 = 0f, moveStart3 = 0f, moveEnd3 = 0f, moveStart0 = 0f;
        bool moved3 = false, moved0 = false; string firstMove3 = ""; float holdDrift = 0f;
        bool wrapped = false, zeroOnFirst = false; int frames = 0;
        var home0 = c0.FirstTileOf(0);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 6f)
        {
            yield return null;
            frames++;
            float beat = GlobalClock.SongBeat;
            if (beat < total * 0.5f) wrapped = true;
            float ub = wrapped ? beat : beat - total;   // beats since the reset (negative before it)
            if (wrapped && beat > 3.2f) break;
            Vector3 p3 = c3.transform.position, p0 = c0.transform.position;
            float j3 = (p3 - prev3).magnitude, j0 = (p0 - prev0).magnitude;
            maxJump3 = Mathf.Max(maxJump3, j3); maxJump0 = Mathf.Max(maxJump0, j0);
            // it holds while it stays on its last tile (that tile may move with its grid); it travels once it leaves it
            float off3 = Flat(p3, last3.Top);
            if (off3 > 0.05f && !moved3) { moved3 = true; moveStart3 = ub; firstMove3 = "left its last tile at beat " + F(ub) + " (tripping " + c3.Tripping + ", holding " + c3.Holding + ")"; }
            if (!moved3) holdDrift = Mathf.Max(holdDrift, off3);
            if (moved3 && j3 > 1e-3f) moveEnd3 = ub;
            if (j0 > 0.01f && !moved0) { moved0 = true; moveStart0 = ub; }
            if (wrapped && !zeroOnFirst && beat < 0.25f && home0 != null && Flat(p0, home0.Top) < 0.1f) zeroOnFirst = true;
            prev3 = p3; prev0 = p0;
        }
        bool home3 = c3.RestingTile == c3.HomeTile;
        float trip = moveEnd3 - moveStart3;
        Add(L(moved3 && home3 && moveStart3 >= -0.05f && trip > 0.2f && maxJump3 < 0.6f && holdDrift < 0.05f,
              "L2 it holds on its last tile up to the loop point; after it the cube travels home (a movement over > 0.2 beat that starts after the reset: its stagger " + F(c3.ResetStagger) + " beat), never jumping more than 0.6 u in a frame, and ends home",
              "held (drift " + F(holdDrift, "F3") + " u), " + firstMove3 + ", moving until " + F(moveEnd3) + " (" + F(trip) + " beats), max jump " + F(maxJump3, "F3") + " u, home " + home3 + ", " + frames + " frames"));
        Add(L(moved0 && zeroOnFirst && moveStart0 < 0f && maxJump0 < 0.6f,
              "L3 a column-0 cube (its grid plays again right at the reset) travels in the last beats before the loop and is on its first tile at beat 0",
              "moved from " + F(moveStart0) + " beats (before the reset), on its first tile at beat 0: " + zeroOnFirst + ", max jump " + F(maxJump0, "F3")));
        // the stagger: two cubes of one grid do not leave together
        AudioCube other = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c != c3 && c.Island == kb3 && Mathf.Abs(c.ResetStagger - c3.ResetStagger) > 0.05f) { other = c; break; }
        Add(L(other != null, "L4 slightly out of sync: two cubes of one grid have different reset delays (0 .. ResetStaggerBeats by id)",
              other != null ? F(c3.ResetStagger) + " vs " + F(other.ResetStagger) + " beats" : "all equal"));
        // a stop mid-song: home in real time, never a snap
        GlobalClock.Stop();
        GlobalClock.Seek(e3 + 2.0); GlobalClock.LoopIndex = 0;
        late = Synth.LateEvents;
        GlobalClock.Play();
        yield return Wait(0.3f);
        lateExcused += Synth.LateEvents - late;
        Vector3 before = c3.transform.position;
        int stop0 = AudioCube.StopTripCount;
        GlobalClock.Stop();
        yield return null;
        Vector3 first = c3.transform.position, prev = first;
        float maxJ = (first - before).magnitude, arrived = -1f; t0 = Time.realtimeSinceStartup;
        var homeT = c3.HomeTile;
        while (Time.realtimeSinceStartup - t0 < 1.2f)
        {
            yield return null;
            Vector3 p = c3.transform.position;
            maxJ = Mathf.Max(maxJ, (p - prev).magnitude);
            prev = p;
            if (arrived < 0f && Flat(p, homeT.Top) < 0.02f) arrived = Time.realtimeSinceStartup - t0;
        }
        bool noSnap = Flat(first, homeT.Top) > 0.3f;
        Add(L(AudioCube.StopTripCount > stop0 && noSnap && maxJ < 0.6f && arrived > 0.2f && arrived < 0.95f,
              "L5 a stop sends the holding cube home in real time (≈ " + F(AudioCube.StopTripSeconds) + " s + its stagger), not in a snap: never more than 0.6 u a frame",
              "a frame after the stop " + F(Flat(first, homeT.Top)) + " u from home, home after " + F(arrived) + " s, max jump " + F(maxJ, "F3") + " u, stop trips +" + (AudioCube.StopTripCount - stop0)));
    }
    // ================================================================== 9. §21 a whole song: no cube ever leaves its grid
    /// <summary>How far (world units, x / z) <paramref name="c"/>'s body is outside its grid — its island and the measures of its long grid (the
    /// islands its carried windows play on), their platforms plus <paramref name="margin"/> (a layer cube: its layer's points too) — and how high it
    /// is above that grid's highest (layer) tile top.</summary>
    static readonly Dictionary<AudioCube, List<KeyBlock>> gridsOf = new Dictionary<AudioCube, List<KeyBlock>>();
    static float OutsideGrid(AudioCube c, float margin, out float above)
    {
        above = 0f;
        var own = c.Island;
        if (own == null) return 0f;
        List<KeyBlock> grids;
        if (!gridsOf.TryGetValue(c, out grids))
        {
            grids = new List<KeyBlock> { own };
            foreach (var w in c.windows) if (w.carried && w.island != null && !grids.Contains(w.island)) grids.Add(w.island);
            gridsOf[c] = grids;
        }
        Bounds b = own.VisualBounds; float top = float.MinValue;
        for (int gi = 0; gi < grids.Count; gi++)
        {
            var g = grids[gi];
            b.Encapsulate(g.VisualBounds);
            for (int ti = 0; ti < g.tiles.Count; ti++) { var t = g.tiles[ti]; if (t == null) continue; var p = c.TopOf(t); b.Encapsulate(p); top = Mathf.Max(top, p.y); }
        }
        Vector3 q = c.transform.position;
        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - margin - q.x, q.x - b.max.x - margin));
        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - margin - q.z, q.z - b.max.z - margin));
        above = q.y - top;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static IEnumerator WholeSong()
    {
        var sm = SM;
        // everything that used to send cubes across grids: a carry ×3 (now a long grid), a launch, a stairs runner, pastes + an echo, an octave copy
        sm.SetCarry(0, 3, 1);
        sm.SetLaunch(2, true);
        int si = sm.AddStairIsland(4, false, 0);
        yield return Wait(0.4f);
        AudioCube src = null, oneSrc = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && c.Island == sm.Islands[3]) { src = c; break; }
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && c.Island == sm.Islands[1]) { oneSrc = c; break; }
        AudioCube pasted = null, echo = null, oc = null;
        if (src != null) { Clipboard.Copy(src); pasted = Clipboard.PasteNext(); echo = Clipboard.PasteNext(Clipboard.PasteMode.EchoUp); }
        if (oneSrc != null) oc = CubeOps.OctaveCopy(oneSrc, -1);
        bool appear = pasted != null && pasted.BodyVisible && echo != null && echo.BodyVisible;
        int cubes = 0, longGrid = 0, runners = 0;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) { cubes++; if (c.HasCarried) longGrid++; if (c.IsRunner) runners++; }
        Info("song: " + cubes + " cubes (" + longGrid + " walking a long grid, " + runners + " runner), launch on island 2 " + sm.Islands[2].launch + ", stairs island " + si + ", pastes " + (pasted != null) + "/" + (echo != null) + ", octave copy " + (oc != null));
        float bpm0 = GlobalClock.BPM;
        GlobalClock.SetBPM(240f);
        GlobalClock.Stop(); GlobalClock.Seek(0); AudioCube.SnapAllHome();
        yield return Wait(0.3f);
        int late0 = Synth.LateEvents;
        gridsOf.Clear();
        GlobalClock.Play();
        float t0 = Time.realtimeSinceStartup;
        int lateSeenG = Synth.LateEvents, lateAtStart = 0; var lateLog = new StringBuilder();
        int frames = 0, bad = 0, flights0 = AudioCube.FlightCount, flings0 = AudioCube.FlingCount;
        float worstOut = 0f, worstAbove = -9f; string worstWhy = "-";
        bool stopped = false; float stopT = 0f;
        while (Time.realtimeSinceStartup - t0 < 40f)
        {
            yield return null;
            frames++;
            if (Synth.LateEvents != lateSeenG)
            {
                int d = Synth.LateEvents - lateSeenG; lateSeenG = Synth.LateEvents;
                float since = Time.realtimeSinceStartup - t0;
                if (since < 0.35f) lateAtStart += d;   // the play-start artifact (counted apart, as in V6ChecksH)
                if (lateLog.Length < 600) lateLog.Append("+" + d + " at " + F(since) + " s (beat " + F(GlobalClock.SongBeatD) + ", frame " + F(Time.unscaledDeltaTime * 1000f, "F0") + " ms); ");
            }
            if (!stopped && GlobalClock.LoopIndex >= 2) { GlobalClock.Stop(); stopped = true; stopT = Time.realtimeSinceStartup; }
            if (stopped && Time.realtimeSinceStartup - stopT > 1.0f) break;
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || !c.isFinalized || c.nodes.Count == 0) continue;
                float above;
                float o = OutsideGrid(c, 0.6f, out above);
                if (o > worstOut || above > worstAbove)
                {
                    if (o > worstOut) worstOut = o;
                    if (above > worstAbove) worstAbove = above;
                    worstWhy = "cube " + c.id + " (island " + sm.Islands.IndexOf(c.Island) + ", layer " + c.layer + (c.HasCarried ? ", long grid" : "") + (c.IsRunner ? ", runner" : "") + ") at beat " + F(GlobalClock.SongBeatD) + " loop " + GlobalClock.LoopIndex + (stopped ? " (stopped)" : "") + ": out " + F(o, "F3") + ", above " + F(above);
                }
                if (o > 0f || above > 2.2f) bad++;
            }
        }
        GlobalClock.SetBPM(bpm0);
        lateExcused += lateAtStart;
        if (lateLog.Length > 0) Info("song: late notes " + lateLog);
        gridsOf.Clear();
        Add(L(appear && frames > 100 && bad == 0 && AudioCube.FlightCount == flights0 && AudioCube.FlingCount == flings0,
              "G1 §21 a whole song played twice through and stopped — a carry ×3 long grid, a launch, a stairs runner, two pastes and an octave copy, every cube checked every frame: no body ever leaves its grid's (long grid's) platform (+0.6 u) or rises more than 2.2 u over its tiles; no flights, no flings; pastes appear on their grid at once",
              frames + " frames, outside " + bad + ", worst: " + worstWhy + ", flights +" + (AudioCube.FlightCount - flights0) + ", flings +" + (AudioCube.FlingCount - flings0) + ", pastes visible at once " + appear + ", synth late +" + (Synth.LateEvents - late0)));
    }
}
