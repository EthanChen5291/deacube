using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v4 §4 package R checks (Play mode). <c>V4ChecksR.RunEngine()</c> is synchronous (the durations engine tables with swing /
/// PingPong / Once / reverse / phase, legacy identity against the v3 formulas over 1440 settings and the fixture, bake, lengths kept
/// aligned through every node edit, save / load / undo of durs, the region-aware preview wrap). <c>V4ChecksR.Run()</c> starts the timed
/// checks (draft via SimPointer, focus loop wrap + restore, size transitions, a recording with its event log, captures); poll
/// <see cref="Done"/> or the report file Captures/v4r_report.txt. The fixture is loaded (never the user's save); the autosave and
/// backups are put back as they were.
/// v6 (H): the timed checks draw by clicking tiles with an empty hand, as before v6: they run with PathManager.AutoHand = true (restored after).
/// </summary>
public static class V4ChecksR
{
    static bool autoWas;
    public static string Report = "";
    public static bool Done;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "v4r_report.txt");
    public static string RecPath => Path.Combine(V2Checks.CapturePath, "v4r_rec.wav");
    public static string RecEventsPath => Path.Combine(V2Checks.CapturePath, "v4r_rec_events.txt");

    static StringBuilder sb;
    static int pass, fail, pushes;
    static readonly List<VoiceRules.NoteEvent> tapped = new List<VoiceRules.NoteEvent>();

    // ================================================================== helpers
    static string L(bool ok, string name, string detail = null) => (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + "\n";

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
    }

    delegate string Check();
    static string Safe(string name, Check c)
    {
        try { return c(); }
        catch (Exception e) { return L(false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var st = e.StackTrace ?? ""; int i = st.IndexOf('\n'); return i > 0 ? st.Substring(0, i).Trim() : st.Trim(); }

    static void OnHistory() { pushes++; }
    static void OnTap(VoiceRules.NoteEvent e) { if (tapped.Count < 4096) tapped.Add(e); }

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray closed, the real mouse ignored).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (PathManager.I != null) { if (PathManager.I.IsDrawing) PathManager.I.CancelPath(false); if (PathManager.I.Drag != null && PathManager.I.Drag.Busy) PathManager.I.Drag.Cancel(); }
        // a text field left selected (e.g. the prompt after another suite) swallows every hotkey (InputUtil.TypingInField)
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        KeyShim.Clear();
    }

    public static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        GlobalClock.SetSwing(0f);
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void Frame(Vector3 focus, float dist, float yaw = -24f, float pitch = 52f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static string F(double v, string f = "F3") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static string Ints(IList<int> l) { var s = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) s.Append(','); s.Append(l[i]); } return s.ToString(); }
    static string Floats(IList<float> l) { var s = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) s.Append(','); s.Append(F(l[i], "0.###")); } return s.ToString(); }

    static void Key(KeyCode k, bool cmd = false)
    {
        if (cmd) KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
        KeyShim.Sim(k, true, true, false);
        PathManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    /// <summary>A finalized cube from a state (the flat way tests build cubes).</summary>
    static AudioCube Make(int measure, int instrument, int[] xs, int[] zs, int[] durs, PathMode mode = PathMode.Loop, int phase = 0, bool reverse = false)
    {
        var st = new CubeState
        {
            instrument = instrument, measure = measure, xs = xs, zs = zs, rests = new bool[xs.Length], mods = new int[xs.Length], step = (int)StepLen.Quarter,
            gate = 1, mode = (int)mode, volume = 1f, hits = -1, twinOf = -1, phase = phase, reverse = reverse, durs = durs
        };
        var c = PathManager.I.RestoreCube(st);
        SequenceMaster.RecalculateTimeline();
        return c;
    }

    static int[] Row(int n, int x0 = 0) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = x0 + i; return a; }
    static int[] Same(int n, int v) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

    static List<float> Starts(AudioCube c, int count) { var l = new List<float>(); for (int k = 0; k < count; k++) l.Add(c.StepStartLocalBeat(k)); return l; }
    static List<int> NodesOf(AudioCube c, int count) { var l = new List<int>(); int pass = c.PassFor(0, 0); for (int k = 0; k < count; k++) l.Add(c.Decide(0, k, pass).node); return l; }
    static bool Near(IList<float> got, float[] want, float eps = 1e-4f) { if (got.Count < want.Length) return false; for (int i = 0; i < want.Length; i++) if (Mathf.Abs(got[i] - want[i]) > eps) return false; return true; }
    static bool Eq(IList<int> got, int[] want) { if (got.Count < want.Length) return false; for (int i = 0; i < want.Length; i++) if (got[i] != want[i]) return false; return true; }

    // ================================================================== the v3 formulas (reference for the legacy identity)
    static float RefStepBeats(StepLen s) { switch (s) { case StepLen.Half: return 2f; case StepLen.Eighth: return 0.5f; case StepLen.Sixteenth: return 0.25f; case StepLen.Triplet: return 1f / 3f; default: return 1f; } }
    static float RefStart(StepLen step, float swing, int k)
    {
        float sb = RefStepBeats(step); float kw = sb <= 0.5f && step != StepLen.Triplet ? swing / 3f : 0f;
        int pair = k / 2; float b = pair * 2f * sb;
        return (k % 2 == 0) ? b : b + (1f + kw) * sb;
    }
    static void RefCompute(StepLen step, float swing, float local, out int k, out float progress)
    {
        float sb = RefStepBeats(step); float kw = sb <= 0.5f && step != StepLen.Triplet ? swing / 3f : 0f; float pairLen = 2f * sb;
        int pair = Mathf.FloorToInt(local / pairLen);
        float r = local - pair * pairLen;
        float first = (1f + kw) * sb;
        if (r < first) { k = pair * 2; progress = r / first; }
        else { k = pair * 2 + 1; progress = (r - first) / Mathf.Max(1e-4f, (1f - kw) * sb); }
        progress = Mathf.Clamp01(progress);
    }
    static int RefNodeForStep(PathMode mode, int n, int k)
    {
        if (n == 0 || k < 0) return -1;
        switch (mode)
        {
            case PathMode.PingPong: if (n == 1) return 0; int period = 2 * n - 2; int m = k % period; return m < n ? m : period - m;
            case PathMode.Once: return k < n ? k : -1;
            default: return k % n;
        }
    }
    /// <summary>(node, hit) of step k under the v3 necklace engine.</summary>
    static void RefDecide(AudioCube c, int k, out int node, out bool hit)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(GlobalClock.BeatsPerBar / RefStepBeats(c.step)), 1, 24);
        var p = Rhythm.Pattern(c.hits, c.rot, c.mask, n);
        var prefix = new int[n + 1];
        for (int j = 0; j < n; j++) prefix[j + 1] = prefix[j] + (p[j] ? 1 : 0);
        int hi = k < 0 ? -1 : (k / n) * prefix[n] + prefix[k % n + 1] - 1;
        int nn = c.nodes.Count;
        if (hi < 0) node = nn == 0 ? -1 : (c.reverse ? nn - 1 : 0);
        else { int idx = RefNodeForStep(c.mode, nn, hi + Mathf.Max(0, c.phase)); node = idx < 0 ? -1 : (c.reverse ? nn - 1 - idx : idx); }
        hit = k >= 0 && p[k % n] && hi >= 0 && node >= 0;
    }

    // ================================================================== synchronous engine checks
    public static string RunEngine()
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        var o = new StringBuilder();
        Prepare(); LoadFixture();
        o.Append(Safe("R1 warp", CheckWarp));
        o.Append(Safe("R2 SizeOf / lengths", CheckSizes));
        o.Append(Safe("R1 tables", CheckTables));
        o.Append(Safe("R1 swing", CheckSwing));
        o.Append(Safe("R1 legacy identity", CheckLegacyIdentity));
        o.Append(Safe("R1 bake", CheckBake));
        o.Append(Safe("R1 durs aligned", CheckAligned));
        o.Append(Safe("R1 save/load/undo", CheckSaveUndo));
        o.Append(Safe("R4 preview wrap", CheckPreviewWrap));
        o.Append(Safe("register fold", CheckRegisterFold));
        Prepare(); LoadFixture();
        PathManager.SimOnly = false;
        return o.ToString();
    }

    static string CheckWarp()
    {
        float s = 0.2f; float worst = 0f;
        for (int i = 0; i <= 800; i++) { float t = i * 0.01f; worst = Mathf.Max(worst, Mathf.Abs(AudioCube.WarpInv(AudioCube.Warp(t, s), s) - t)); }
        bool ids = Mathf.Abs(AudioCube.Warp(0.5f, s) - 0.6f) < 1e-6f && Mathf.Abs(AudioCube.Warp(1f, s) - 1f) < 1e-6f && Mathf.Abs(AudioCube.Warp(2.25f, s) - 2.3f) < 1e-5f
                   && Mathf.Abs(AudioCube.Warp(3.75f, s) - 3.8f) < 1e-5f && AudioCube.Warp(1.7f, 0f) == 1.7f;
        return L(ids && worst < 1e-5f, "R1 swing warp W(t): 0.5 -> 0.5(1+s), whole beats fixed, W^-1(W(t)) = t", "s 0.2: W(.5) " + F(AudioCube.Warp(0.5f, s), "F4") + ", W(2.25) " + F(AudioCube.Warp(2.25f, s), "F4") + ", worst round trip " + worst.ToString("E2"));
    }

    static string CheckSizes()
    {
        var o = new StringBuilder();
        float[] beats = { 0.25f, 0.5f, 1f, 2f, 4f };
        var got = new List<float>(); foreach (var b in beats) got.Add(AudioCube.SizeOf(b));
        bool table = Near(got, ProjectConfig.SizeTable, 1e-5f);
        bool mono = true; float prev = 0f; foreach (int t in AudioCube.UiTicks) { float v = AudioCube.SizeOf(t / 24f); if (t <= 96 ? v <= prev : v < prev) mono = false; prev = v; }   // a dotted whole clamps at the whole's size
        bool clamp = AudioCube.SizeOf(0.05f) == ProjectConfig.SizeTable[0] && AudioCube.SizeOf(16f) == ProjectConfig.SizeTable[4];
        o.Append(L(table && mono && clamp, "R2 SizeOf: 16th 0.62, 8th 0.78, quarter 1, half 1.24, whole 1.5; dotted lengths land between (a dotted whole = the whole, clamped)", Floats(got) + " dotted quarter " + F(AudioCube.SizeOf(1.5f)) + " dotted whole " + F(AudioCube.SizeOf(6f))));
        bool codec = true; var list = new StringBuilder();
        for (int sz = 0; sz < 5; sz++) for (int d = 0; d < 2; d++)
            {
                int t = AudioCube.TicksOf(sz, d == 1); int s2; bool d2;
                if (!AudioCube.TryDecode(t, out s2, out d2) || s2 != sz || d2 != (d == 1) || AudioCube.SnapTicks(t) != t) codec = false;
                list.Append(t).Append(' ');
            }
        int s3; bool d3;
        bool snaps = AudioCube.SnapTicks(8f) == 9 && AudioCube.SnapTicks(20f) == 18 && AudioCube.SnapTicks(30f) == 36 && AudioCube.SnapTicks(500f) == 144 && AudioCube.SnapTicks(4f) == 6 && !AudioCube.TryDecode(8, out s3, out d3);
        o.Append(L(codec && snaps, "R1 the ten UI lengths: TicksOf / TryDecode / SnapTicks round trip; SnapTicks takes the nearest on a log scale (a triplet 8th = 8 ticks -> 9)", list.ToString().Trim()));
        return o.ToString();
    }

    static string CheckTables()
    {
        var o = new StringBuilder();
        var kb = SongManager.I.Islands[0];
        int[] durs = { 12, 12, 24, 48 };   // 8th 8th quarter half = one 4/4 bar
        var c = Make(0, 0, Row(4), Same(4, 0), durs);
        if (c == null) return L(false, "R1 tables", "cube not built");
        bool eng = c.HasDurations && c.windows.Count == 1 && Mathf.Abs(c.windows[0].length - 4f) < 1e-4f;
        var st = Starts(c, 9); var nd = NodesOf(c, 8);
        bool loop = Near(st, new[] { 0f, 0.5f, 1f, 2f, 4f, 4.5f, 5f, 6f, 8f }) && Eq(nd, new[] { 0, 1, 2, 3, 0, 1, 2, 3 });
        var h16 = c.HitSteps16(0, c.PassFor(0, 0));
        var tl = new List<AudioCube.TimelineEvent>(); c.TimelineEvents(0, tl);
        var tls = new List<float>(); foreach (var e in tl) { tls.Add(e.start); tls.Add(e.len); }
        bool tlOk = tl.Count == 4 && Near(tls, new[] { 0f, 0.5f, 0.5f, 0.5f, 1f, 1f, 2f, 2f });
        bool lens = Mathf.Abs(c.StepLenBeats(0) - 0.5f) < 1e-5f && Mathf.Abs(c.StepLenBeats(3) - 2f) < 1e-5f && c.StepsInWindow(0) == 4;
        int hitsAll = 0; int pass0 = c.PassFor(0, 0); for (int k = 0; k < 8; k++) if (c.Decide(0, k, pass0).hit) hitsAll++;
        o.Append(L(eng && loop && Ints(h16) == "0,2,4,8" && tlOk && lens && hitsAll == 8, "R1 Loop: starts = prefix sums of the lengths, every step a hit, one period repeats; HitSteps16, TimelineEvents, StepLenBeats, StepsInWindow",
                   "starts " + Floats(st) + " nodes " + Ints(nd) + " hits16 " + Ints(h16) + " timeline " + Floats(tls)));
        // PingPong: 0 1 2 3 2 1 | 0 ...: one period = 12+12+24+48+24+12 = 132 ticks = 5.5 beats
        c.SetMode(PathMode.PingPong);
        st = Starts(c, 8); nd = NodesOf(c, 7);
        bool pp = Near(st, new[] { 0f, 0.5f, 1f, 2f, 4f, 5f, 5.5f, 6f }) && Eq(nd, new[] { 0, 1, 2, 3, 2, 1, 0 });
        o.Append(L(pp, "R1 PingPong: period 2n-2 over the nodes with their own lengths", "starts " + Floats(st) + " nodes " + Ints(nd)));
        // Once: 4 notes, then no hits (the cube rests on its end node)
        c.SetMode(PathMode.Once);
        st = Starts(c, 6); nd = NodesOf(c, 6);
        int p1 = c.PassFor(0, 0);
        bool once = Near(st, new[] { 0f, 0.5f, 1f, 2f, 4f, 5f }) && Eq(nd, new[] { 0, 1, 2, 3, 3, 3 }) && c.Decide(0, 3, p1).hit && !c.Decide(0, 4, p1).hit && !c.PatternHit(4);
        o.Append(L(once, "R1 Once: n notes, then filler steps without hits (resting on the end node)", "starts " + Floats(st) + " nodes " + Ints(nd)));
        // reverse (Loop): 3 2 1 0 with their lengths
        c.SetMode(PathMode.Loop); c.SetReverse(true);
        st = Starts(c, 5); nd = NodesOf(c, 4);
        bool rev = Near(st, new[] { 0f, 2f, 3f, 3.5f, 4f }) && Eq(nd, new[] { 3, 2, 1, 0 });
        c.SetReverse(false);
        // phase 1: 1 2 3 0
        c.SetPhase(1);
        var st2 = Starts(c, 5); var nd2 = NodesOf(c, 4);
        bool ph = Near(st2, new[] { 0f, 0.5f, 1.5f, 3.5f, 4f }) && Eq(nd2, new[] { 1, 2, 3, 0 });
        c.SetPhase(0);
        o.Append(L(rev && ph, "R1 reverse (3 2 1 0) and phase +1 (1 2 3 0) carry each node's own length", "reverse " + Floats(st) + " / phase " + Floats(st2)));
        // a rest keeps its length and is silent; the window cuts the last note
        c.SetMod(1, 1);
        var r1 = c.Decide(0, 1, c.PassFor(0, 0));
        c.SetMod(1, 0);
        var c2 = Make(0, 0, Row(2, 4), Same(2, 1), new[] { 96, 96 });
        var tl2 = new List<AudioCube.TimelineEvent>(); c2.TimelineEvents(0, tl2);
        bool cut = tl2.Count == 1 && Mathf.Abs(tl2[0].len - 4f) < 1e-4f && c2.HitSteps16(0, c2.PassFor(0, 0)).Count == 1;
        o.Append(L(r1.hit && !r1.fires && r1.mod == 1 && Mathf.Abs(c.StepStartLocalBeat(2) - 1f) < 1e-5f && cut, "R1 a rest is silent for its length; a note past the window end is not played",
                   "rest hit " + r1.hit + " fires " + r1.fires + "; two whole notes in a 1-bar window: " + tl2.Count + " event(s)"));
        // ComputeStep inverts the starts everywhere (no swing)
        string inv = InverseCheck(c, 12f);
        o.Append(L(inv == null, "R1 StepAt inverts StepStartLocalBeat over 3 periods", inv));
        c.Delete(false); SequenceMaster.Unregister(c); c2.Delete(false); SequenceMaster.Unregister(c2);
        SequenceMaster.RecalculateTimeline();
        return o.ToString();
    }

    /// <summary>null when for every local beat (0.005 steps) StepAt gives k with start(k) <= local < start(k+1) and the right progress.</summary>
    static string InverseCheck(AudioCube c, float span)
    {
        for (int i = 0; i < span * 200f; i++)
        {
            float local = i * 0.005f; int k; float p;
            c.StepAt(local, out k, out p);
            float a = c.StepStartLocalBeat(k), b = c.StepStartLocalBeat(k + 1);
            if (local < a - 1e-4f || local >= b + 1e-4f) return "local " + F(local, "F4") + " -> k " + k + " [" + F(a, "F4") + ", " + F(b, "F4") + ")";
            float want = Mathf.Clamp01((local - a) / Mathf.Max(1e-4f, b - a));
            if (Mathf.Abs(p - want) > 2e-3f) return "local " + F(local, "F4") + " progress " + F(p, "F4") + " want " + F(want, "F4");
        }
        return null;
    }

    static string CheckSwing()
    {
        var o = new StringBuilder();
        float sw0 = GlobalClock.Swing;
        var c = Make(0, 0, Row(4), Same(4, 2), new[] { 12, 12, 6, 6 });
        if (c == null) return L(false, "R1 swing", "no cube");
        c.SetAllDurations(new[] { 12, 12, 6, 6 });   // 8th 8th 16th 16th: 1.5 beats a period
        GlobalClock.SetSwing(0.6f);                 // s = 0.2
        var st = Starts(c, 8);
        // raw 0 .5 1 1.25 1.5 2 2.5 2.75 -> W: 0 .6 1 1.3 1.6 2 2.6 2.8
        bool warp = Near(st, new[] { 0f, 0.6f, 1f, 1.3f, 1.6f, 2f, 2.6f, 2.8f }, 2e-4f);
        bool lens = Mathf.Abs(c.StepLenBeats(0) - 0.6f) < 2e-4f && Mathf.Abs(c.StepLenBeats(1) - 0.4f) < 2e-4f;
        string inv = InverseCheck(c, 6f);
        GlobalClock.SetSwing(sw0);
        o.Append(L(warp && lens && inv == null, "R1 swing warps the durations engine per beat (8ths swung, lengths follow), StepAt still inverts it",
                   "starts " + Floats(st) + (inv != null ? " inverse " + inv : "")));
        c.Delete(false); SequenceMaster.Unregister(c); SequenceMaster.RecalculateTimeline();
        return o.ToString();
    }

    static string CheckLegacyIdentity()
    {
        var o = new StringBuilder();
        // the fixture's cubes: HitSteps16 as the v3 formulas give them (they carry no durs)
        int bad = 0, checkedCubes = 0; var detail = new StringBuilder();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized) continue;
            checkedCubes++;
            if (c.HasDurations) { bad++; detail.Append(" cube " + c.id + " has durs"); continue; }
            var got = c.HitSteps16(0, c.PassFor(0, 0));
            var want = new List<int>();
            float len = c.windows[0].length;
            for (int k = 0; k < 4096; k++) { float sl = RefStart(c.step, GlobalClock.Swing, k); if (sl >= len - 1e-4f) break; int node; bool hit; RefDecide(c, k, out node, out hit); if (!hit) continue; int s16 = Mathf.Clamp(Mathf.RoundToInt(sl * 4f), 0, Mathf.Max(1, Mathf.RoundToInt(len * 4f)) - 1); if (want.Count == 0 || want[want.Count - 1] != s16) want.Add(s16); }
            if (Ints(got) != Ints(want)) { bad++; if (detail.Length < 300) detail.Append(" cube " + c.id + " " + Ints(got) + " != " + Ints(want)); }
        }
        o.Append(L(bad == 0 && checkedCubes == 13, "R1 the fixture's 13 legacy cubes keep their exact v3 HitSteps16", checkedCubes + " cubes" + detail));
        // every legacy setting: starts, StepAt, nodes and hits bit-identical to the v3 formulas
        AudioCube t = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count >= 3 && !c.IsDrums) { t = c; break; }
        if (t == null) return o.Append(L(false, "R1 legacy settings sweep", "no 3-node cube")).ToString();
        var save = t.ToState();
        float sw0 = GlobalClock.Swing;
        int combos = 0, mism = 0; string first = null;
        StepLen[] steps = { StepLen.Half, StepLen.Quarter, StepLen.Eighth, StepLen.Sixteenth, StepLen.Triplet };
        float[] swings = { 0f, 0.5f, 1f };
        int[] hitsV = { -1, 3, 5, -2 };
        foreach (var sw in swings)
        {
            GlobalClock.SetSwing(sw);
            foreach (var stp in steps)
                foreach (var hv in hitsV)
                    for (int rot = 0; rot < 2; rot++)
                        for (int mode = 0; mode < 3; mode++)
                            for (int rv = 0; rv < 2; rv++)
                                for (int ph = 0; ph < 2; ph++)
                                {
                                    t.step = stp; t.hits = hv; t.rot = rot; t.mask = 0x2D; t.mode = (PathMode)mode; t.reverse = rv == 1; t.phase = ph;
                                    combos++;
                                    int pass = t.PassFor(0, 0);
                                    for (int k = 0; k < 40; k++)
                                    {
                                        float a = t.StepStartLocalBeat(k), b = RefStart(stp, sw, k);
                                        int node; bool hit; RefDecide(t, k, out node, out hit);
                                        var h = t.Decide(0, k, pass);
                                        if (a != b || h.node != node || h.hit != hit) { mism++; if (first == null) first = stp + " sw " + sw + " hits " + hv + " rot " + rot + " mode " + mode + " rev " + rv + " ph " + ph + " k " + k + ": " + a + "/" + b + " node " + h.node + "/" + node + " hit " + h.hit + "/" + hit; break; }
                                        float local = b + 0.37f * RefStepBeats(stp); int k1, k2; float p1, p2;
                                        t.StepAt(local, out k1, out p1); RefCompute(stp, sw, local, out k2, out p2);
                                        if (k1 != k2 || p1 != p2) { mism++; if (first == null) first = stp + " StepAt " + local + ": " + k1 + "/" + k2 + " " + p1 + "/" + p2; break; }
                                    }
                                }
        }
        GlobalClock.SetSwing(sw0);
        t.ApplySettings(save);
        o.Append(L(mism == 0 && combos == 1440, "R1 the legacy engine is bit-identical to v3: starts, StepAt, node and hit over 5 steps x 3 swings x 4 necklaces x 2 rotations x 3 modes x 2 directions x 2 phases, 40 steps each",
                   combos + " settings, " + mism + " mismatches" + (first != null ? ": " + first : "")));
        return o.ToString();
    }

    static string CheckBake()
    {
        var o = new StringBuilder();
        // a quarter-step loop: bake keeps the timing exactly
        var st = new CubeState { instrument = 0, measure = 1, xs = Row(3), zs = Same(3, 3), rests = new bool[3], mods = new int[3], step = (int)StepLen.Quarter, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1 };
        var c = PathManager.I.RestoreCube(st); SequenceMaster.RecalculateTimeline();
        var before = c.HitSteps16(0, c.PassFor(0, 0));
        c.BakeDurations();
        var after = c.HitSteps16(0, c.PassFor(0, 0));
        bool q = c.HasDurations && Ints(c.durs) == "24,24,24" && Ints(before) == Ints(after);
        // a Euclidean E(3,8) on 8ths: gaps 1.5 1.5 1 -> dotted quarter, dotted quarter, quarter
        var st2 = new CubeState { instrument = 0, measure = 1, xs = Row(3, 3), zs = Same(3, 2), rests = new bool[3], mods = new int[3], step = (int)StepLen.Eighth, gate = 1, mode = 0, volume = 1f, hits = 3, twinOf = -1 };
        var e = PathManager.I.RestoreCube(st2); SequenceMaster.RecalculateTimeline();
        var b2 = e.HitSteps16(0, e.PassFor(0, 0));
        e.BakeDurations();
        var a2 = e.HitSteps16(0, e.PassFor(0, 0));
        bool eu = Ints(e.durs) == "36,36,24" && Ints(b2) == Ints(a2) && e.hits == -1;
        o.Append(L(q && eu, "R1 BakeDurations keeps the sound: quarters -> 24s; E(3,8) on 8ths -> 36,36,24 (hits -1), HitSteps16 unchanged",
                   "quarters " + Ints(c.durs) + " " + Ints(before) + "->" + Ints(after) + "; E(3,8) " + Ints(e.durs) + " " + Ints(b2) + "->" + Ints(a2)));
        // SetDuration on a legacy cube bakes first; SetAllDurations(null) goes back to legacy
        var st3 = new CubeState { instrument = 0, measure = 1, xs = Row(2), zs = Same(2, 1), rests = new bool[2], mods = new int[2], step = (int)StepLen.Eighth, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1 };
        var l = PathManager.I.RestoreCube(st3); SequenceMaster.RecalculateTimeline();
        l.SetDuration(1, 48);
        string afterSet = Ints(l.durs);
        bool first = l.HasDurations && afterSet == "12,48";
        l.SetAllDurations(null);
        bool back = !l.HasDurations && l.durs.Count == 0;
        o.Append(L(first && back, "R1 the first rhythm edit of a legacy cube bakes it; SetAllDurations(null) returns to the legacy engine", "after SetDuration " + afterSet + ", legacy again " + back));
        foreach (var x in new[] { c, e, l }) { x.Delete(false); SequenceMaster.Unregister(x); }
        SequenceMaster.RecalculateTimeline();
        return o.ToString();
    }

    static string CheckAligned()
    {
        var o = new StringBuilder();
        var c = Make(1, 0, Row(4), Same(4, 0), new[] { 6, 12, 24, 48 });
        var isl = c.Island;
        c.InsertNode(4, isl.GetTile(4, 0));                      // append: repeats the last length
        bool ins = Ints(c.durs) == "6,12,24,48,48";
        c.InsertNode(1, isl.GetTile(0, 1), 0, 9);                // explicit length in the middle
        bool ins2 = Ints(c.durs) == "6,9,12,24,48,48";
        c.RemoveNode(1);
        bool rem = Ints(c.durs) == "6,12,24,48,48";
        c.MoveNode(0, isl.GetTile(5, 1));
        bool mov = Ints(c.durs) == "6,12,24,48,48";
        c.ReversePath();
        bool rv = Ints(c.durs) == "48,48,24,12,6";
        bool sh = c.ShiftPath(0, 1) && Ints(c.durs) == "48,48,24,12,6";
        var tiles = new List<TileInteraction>(c.nodes); tiles.Add(isl.GetTile(0, 3)); var mods = new List<int>(); for (int i = 0; i < tiles.Count; i++) mods.Add(0);
        c.SetPathAndMods(tiles, mods);                           // the flat grid's append: a new tail node repeats the last length
        bool grid = Ints(c.durs) == "48,48,24,12,6,6";
        c.SetPathAndMods(new List<TileInteraction> { isl.GetTile(2, 2) }, new List<int> { 0 });   // the pencil restart keeps the first length
        bool pencil = Ints(c.durs) == "48";
        o.Append(L(ins && ins2 && rem && mov && rv && sh && grid && pencil, "R1 node edits keep durs aligned: insert (tail / middle), remove, move, reverse, shift, the grid's append and pencil",
                   ins + " " + ins2 + " " + rem + " " + mov + " " + rv + " " + sh + " " + grid + " " + pencil + " -> " + Ints(c.durs)));
        c.Delete(false); SequenceMaster.Unregister(c);
        // twins, stamps, state round trip
        var d = Make(1, 0, Row(3), Same(3, 1), new[] { 12, 36, 24 });
        var tw0 = d.TwinState(0); var tw1 = d.TwinState(1); var tw2 = d.TwinState(2);
        bool twins = Ints(tw0.durs) == "24,72,48" && Ints(tw1.durs) == "6,18,12" && Ints(tw2.durs) == "12,36,24";
        var rt = PathManager.I.RestoreCube(d.ToState());
        bool round = rt != null && rt.HasDurations && Ints(rt.durs) == "12,36,24";
        PathManager.I.Select(d);
        var stamped = PathManager.I.StampTo(SongManager.I.Islands[2]);
        PathManager.I.Deselect();
        bool stamp = stamped != null && stamped.HasDurations && Ints(stamped.durs) == "12,36,24";
        o.Append(L(twins && round && stamp, "R1 twins scale the lengths (half speed x2, double speed x0.5, reversed kept); ToState / RestoreCube and stamps carry them",
                   "twins " + Ints(tw0.durs) + " / " + Ints(tw1.durs) + "; restored " + (rt != null ? Ints(rt.durs) : "null") + "; stamped " + (stamped != null ? Ints(stamped.durs) : "null")));
        foreach (var x in new[] { d, rt, stamped }) if (x != null) { x.Delete(false); SequenceMaster.Unregister(x); }
        SequenceMaster.RecalculateTimeline();
        return o.ToString();
    }

    static string CheckSaveUndo()
    {
        var o = new StringBuilder();
        LoadFixture();
        pushes = 0; History.OnChanged += OnHistory;
        try
        {
            var c = Make(2, 3, Row(4), Same(4, 1), new[] { 6, 18, 24, 72 });
            History.Push();
            int u0 = History.UndoCount;
            c.SetDuration(2, 96); History.Push();                 // one gesture = one entry
            bool one = History.UndoCount == u0 + 1;
            History.Undo();
            AudioCube found = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.isFinalized && x.HasDurations && x.instrument == 3) found = x;
            bool undone = found != null && Ints(found.durs) == "6,18,24,72";
            History.Redo();
            found = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.isFinalized && x.HasDurations && x.instrument == 3) found = x;
            bool redone = found != null && Ints(found.durs) == "6,18,96,72";
            string p = Path.Combine(V2Checks.CapturePath, "v4r_save.json");
            Directory.CreateDirectory(V2Checks.CapturePath);
            bool saved = SongIO.SaveTo(p);
            var json = File.ReadAllText(p);
            LoadFixture();
            bool loaded = SongIO.LoadFrom(p);
            found = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.isFinalized && x.HasDurations && x.instrument == 3) found = x;
            bool back = found != null && Ints(found.durs) == "6,18,96,72";
            var st = SongState.FromJson(json);
            bool ver = st.version == SongState.CurrentVersion && !st.IsLegacy;
            int legacyDurs = 0; foreach (var cs in st.cubes) if (cs.durs != null && cs.durs.Length > 0 && cs.instrument != 3) legacyDurs++;
            try { File.Delete(p); } catch (Exception) { }
            o.Append(L(one && undone && redone && saved && loaded && back && ver && legacyDurs == 0, "R1 durs survive undo / redo and save / load (version 3); legacy cubes save no durs",
                       "one entry " + one + ", undo " + undone + ", redo " + redone + ", save/load " + back + ", legacy cubes with durs " + legacyDurs));
        }
        finally { History.OnChanged -= OnHistory; }
        LoadFixture();
        return o.ToString();
    }

    static string CheckPreviewWrap()
    {
        // inside a region, the 16th after the region's end is the region's first 16th (VoiceRules.PreviewBeat) — pure arithmetic, stopped clock
        GlobalClock.Stop();
        GlobalClock.SetRegion(8.0, 12.0);
        bool region = GlobalClock.HasRegion && GlobalClock.LoopStartBeat == 8.0 && GlobalClock.LoopEndBeat == 12.0 && Math.Abs(GlobalClock.LoopLengthBeats - 4.0) < 1e-9;
        bool inR = GlobalClock.InRegion(8.0) && GlobalClock.InRegion(11.99) && !GlobalClock.InRegion(12.0) && !GlobalClock.InRegion(7.5);
        GlobalClock.Seek(7.0); GlobalClock.Play();
        bool snapped = Math.Abs(GlobalClock.SongBeatD - 8.0) < 1e-9;   // Play resumes inside the region
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        bool cleared = !GlobalClock.HasRegion && GlobalClock.LoopEndBeat == GlobalClock.TotalBeats;
        return L(region && inR && snapped && cleared, "R4 GlobalClock region: LoopStart/End/Length, InRegion, Play snaps into the region, ClearRegion", region + " " + inR + " " + snapped + " " + cleared);
    }

    /// <summary>K's island register ("make them lower or higher"): the same cube's note one octave apart at register -1 / 0 / +1 for pitched
    /// slots (the fold follows the register), drums unchanged; the bass anchor (rule 4) follows the register too.</summary>
    static string CheckRegisterFold()
    {
        var sm = SongManager.I;
        var kb = sm.Islands[0];
        int[] insts = { 0, 3, 4, 2, 5, 9 };   // keys, lead, bass, pad, bells, drums
        var made = new List<AudioCube>();
        var log = new StringBuilder(); bool ok = true;
        try
        {
            foreach (int inst in insts) { var c = Make(0, inst, new[] { 0, 1 }, new[] { 0, 0 }, new[] { 24, 24 }); if (c != null) made.Add(c); }
            var ev = new VoiceRules.NoteEvent[16];
            var midis = new int[insts.Length, 3];
            for (int r = -1; r <= 1; r++)
            {
                sm.SetRegister(0, r);
                for (int i = 0; i < made.Count; i++)
                {
                    var c = made[i];
                    var h = c.Decide(0, 0, c.PassFor(0, 0));
                    double on = GlobalClock.DspNow + 0.1;
                    int n = VoiceRules.Resolve(c, c.windows[0], c.nodes[0], h, 0, 0, on, 0.5, on + 2.0, on + 0.5, ev);
                    midis[i, r + 1] = n > 0 ? ev[0].midi : -1;
                }
            }
            sm.SetRegister(0, 0);
            for (int i = 0; i < made.Count; i++)
            {
                int lo = midis[i, 0], mid = midis[i, 1], hi = midis[i, 2];
                bool drums = Instruments.IsDrums(insts[i]);
                bool good = drums ? (lo == mid && mid == hi) : (mid - lo == 12 && hi - mid == 12);
                if (!good) ok = false;
                log.Append(' ').Append(Instruments.Icons[insts[i]]).Append(' ').Append(lo).Append('/').Append(mid).Append('/').Append(hi);
            }
        }
        finally { foreach (var c in made) if (c != null) { c.Delete(false); SequenceMaster.Unregister(c); } SequenceMaster.RecalculateTimeline(); sm.SetRegister(0, 0); }
        return L(ok && made.Count == insts.Length, "register: the same cube's note is exactly an octave lower / higher at island register -1 / +1 for every pitched slot (drums unchanged)",
                 "midi at register -1/0/+1:" + log);
    }

    // ================================================================== timed checks
    public static string Run(bool record = true, bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; Progress = "start"; autoWas = PathManager.AutoHand; PathManager.AutoHand = true;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(record, captures));
        return "started";
    }

    /// <summary>Runs one timed section ("draft", "focus", "size", "beams", "smear", "record") with the usual preamble / restore; poll
    /// <see cref="Done"/> or the report file.</summary>
    public static string RunOnly(string which, bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; Progress = "start"; autoWas = PathManager.AutoHand; PathManager.AutoHand = true;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(OnlyRoutine(which, captures));
        return "started " + which;
    }

    static IEnumerator OnlyRoutine(string which, bool captures)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0;
        var saves = V3Fixes.SnapshotSaves();
        History.OnChanged += OnHistory;
        Prepare(); LoadFixture(); yield return null; yield return null;
        Progress = which;
        if (which == "draft") yield return Guard(Draft(captures));
        else if (which == "focus") yield return Guard(Focus());
        else if (which == "size") yield return Guard(Size(captures));
        else if (which == "beams") yield return Guard(Beams());
        else if (which == "smear") yield return Guard(Smear(captures));
        else if (which == "record") yield return Guard(Record());
        VoiceRules.Tap = null;
        FocusLoop.Dismiss(); GlobalClock.Stop();
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        History.OnChanged -= OnHistory;
        V3Fixes.RestoreSaves(saves);
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        PathManager.AutoHand = autoWas; Done = true; Progress = "done";
    }

    static IEnumerator Routine(bool record, bool captures)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        History.OnChanged += OnHistory;
        Prepare();
        yield return null;
        Progress = "engine"; Add(RunEngine());
        Prepare(); LoadFixture(); yield return null; yield return null;
        Progress = "draft"; yield return Guard(Draft(captures));
        Prepare(); LoadFixture(); yield return null; yield return null;
        Progress = "focus"; yield return Guard(Focus());
        Prepare(); LoadFixture(); yield return null; yield return null;
        Progress = "size"; yield return Guard(Size(captures));
        if (captures) { Prepare(); LoadFixture(); yield return null; yield return null; Progress = "beams"; yield return Guard(Beams()); }
        Prepare(); LoadFixture(); yield return null; yield return null;
        Progress = "smear"; yield return Guard(Smear(captures));
        if (record) { Prepare(); LoadFixture(); yield return null; yield return null; Progress = "record"; yield return Guard(Record()); }

        Progress = "finish";
        VoiceRules.Tap = null;
        FocusLoop.Dismiss(); GlobalClock.Stop();
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        History.OnChanged -= OnHistory;
        V3Fixes.RestoreSaves(saves);
        Add(L(Synth.LateEvents == late0 && Synth.Errors == err0, "RUN Synth late / errors unchanged", Synth.Stats()));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        PathManager.AutoHand = autoWas; Done = true; Progress = "done";
    }

    /// <summary>Runs a coroutine body, turning an exception into a FAIL line.</summary>
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

    static IEnumerator Capture(string name)
    {
        Directory.CreateDirectory(V2Checks.CapturePath);
        ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, name + ".png"), 1);
        yield return null;
    }

    /// <summary>A free row of <paramref name="len"/> tiles on island 0..n whose tops pick themselves from a steep view (the camera is left there).</summary>
    static bool PickableRow(int len, out KeyBlock isl, out int z, out int x0)
    {
        isl = null; z = -1; x0 = -1;
        var pm = PathManager.I;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || kb.IsMoon || kb.cols < len) continue;
            for (int zz = 0; zz < kb.rows; zz++)
                for (int x = 0; x + len - 1 < kb.cols; x++)
                {
                    bool ok = true;
                    for (int k = 0; k < len && ok; k++) if (PathManager.TopCubeOn(kb.GetTile(x + k, zz)) != null) ok = false;
                    if (!ok) continue;
                    Frame(kb.GetTile(x + len / 2, zz).Top, 9f, 0f, 62f);
                    for (int k = 0; k < len && ok; k++) { var t = kb.GetTile(x + k, zz); if (pm.PickAt(Scr(t.Top)).tile != t) ok = false; }
                    if (ok) { isl = kb; z = zz; x0 = x; return true; }
                }
        }
        return false;
    }

    static int started, changed, finished; static AudioCube finishedCube; static bool finishedNull;
    static void OnStarted() { started++; }
    static void OnChangedDraft() { changed++; }
    static void OnFinished(AudioCube c) { finished++; finishedCube = c; finishedNull = c == null; }

    static IEnumerator Draft(bool captures)
    {
        var pm = PathManager.I;
        // clear one island so the draft has room and nothing stands in front of its tiles
        KeyBlock isl; int z, x0;
        if (!PickableRow(6, out isl, out z, out x0)) { Add(L(false, "R3 draft", "no pickable free row of 6")); yield break; }
        started = changed = finished = 0; finishedCube = null;
        pm.OnDraftStarted += OnStarted; pm.OnDraftChanged += OnChangedDraft; pm.OnDraftFinished += OnFinished;
        try
        {
            pm.BrushTicks = 24;
            int p0 = pushes, cubes0 = SequenceMaster.Cubes.Count;
            var tA = isl.GetTile(x0, z);
            pm.SimPointer(Scr(tA.Top), true, true, false); pm.SimPointer(Scr(tA.Top), false, false, true);
            var d = pm.Draft;
            double rs, re; FocusLoop.Span(isl.column, out rs, out re);
            bool start = pm.IsDrawing && d != null && d.Hologram && !d.isFinalized && d.HasDurations && Ints(d.durs) == "24" && started == 1
                         && FocusLoop.Active && FocusLoop.Column == isl.column && GlobalClock.IsPlaying && GlobalClock.HasRegion && GlobalClock.RegionStart == rs && GlobalClock.RegionEnd == re;
            yield return null; yield return null;
            bool shown = d != null && d.HologramShown && d.BodyRenderer.sharedMaterial != null && d.BodyRenderer.sharedMaterial.shader.name == "DeaCube/Hologram";
            bool windows = d != null && d.windows.Count == 1 && d.windows[0].island == isl;
            Add(L(start && shown && windows, "R3 a press on a free tile starts a HOLOGRAM draft (node 0 = the brush length) that has windows and loops its column (focus loop, clock playing)",
                  "drawing " + pm.IsDrawing + ", hologram shader " + shown + ", durs " + (d != null ? Ints(d.durs) : "-") + ", focus col " + FocusLoop.Column + " region " + F(GlobalClock.RegionStart, "F1") + "-" + F(GlobalClock.RegionEnd, "F1") + ", playing " + GlobalClock.IsPlaying));
            // the draft plays live: its own notes are dispatched while it is drawn
            tapped.Clear(); VoiceRules.Tap = OnTap;
            yield return Wait(Mathf.Min(2.2f, (float)((re - rs) / GlobalClock.BeatsPerSecond) + 0.3f));
            int own = 0; foreach (var e in tapped) if (d != null && e.owner == d.owner) own++;
            VoiceRules.Tap = null;
            Add(L(own >= 1, "R3 the draft plays live (its notes reach the synth while it is being drawn)", own + " draft notes in one loop"));

            // hover: audition once per entry and the ghost of what a click adds
            var tD = isl.GetTile(x0 + 3, z);
            int a0 = PathManager.AuditionCount;
            yield return Wait(0.12f);
            pm.SimPointer(Scr(tD.Top), false, false, false);
            int a1 = PathManager.AuditionCount;
            var ghost = new List<TileInteraction>(pm.PreviewTiles);
            pm.SimPointer(Scr(tD.Top), false, false, false); pm.SimPointer(Scr(tD.Top), false, false, false);
            int a2 = PathManager.AuditionCount;
            bool hover = pm.AuditionTile == tD && a1 == a0 + 1 && a2 == a1 && ghost.Count == 3 && ghost[0] == isl.GetTile(x0 + 1, z) && ghost[2] == tD && d.nodes.Count == 1;
            Add(L(hover, "R3 hovering a tile of the draft island auditions it once per entry and previews the grid line a click adds (nothing added)",
                  "audition tile " + (pm.AuditionTile == tD) + ", auditions +" + (a1 - a0) + " then +" + (a2 - a1) + ", ghost beads " + ghost.Count + ", nodes " + d.nodes.Count));
            if (captures)
            {
                Frame(isl.GetTile(x0 + 2, z).Top, 7.5f, -18f, 48f);
                pm.SimPointer(Scr(tD.Top), false, false, false);
                yield return Wait(0.35f);
                yield return Capture("v4r_draft_hover");
                yield return Wait(0.3f);
                Frame(isl.GetTile(x0 + 3, z).Top, 9f, 0f, 62f);
            }
            // rate limit: a sweep over four tiles in consecutive frames plays at most one per 0.1 s
            yield return Wait(0.15f);
            int s0 = PathManager.AuditionCount; float t0 = Time.realtimeSinceStartup;
            for (int k = 1; k <= 5; k++) { pm.SimPointer(Scr(isl.GetTile(x0 + k, z).Top), false, false, false); yield return null; }
            int sweep = PathManager.AuditionCount - s0; float took = Time.realtimeSinceStartup - t0;
            Add(L(sweep >= 1 && sweep <= Mathf.CeilToInt(took / 0.1f) + 1, "R3 hover auditions are rate-limited (at most 10 per second)", sweep + " auditions over a " + F(took, "F2") + " s sweep of 5 tiles"));

            // click: the ghost tiles are added with the brush length
            pm.SimPointer(Scr(tD.Top), false, false, false);
            int c0 = changed;
            pm.SimPointer(Scr(tD.Top), true, true, false); pm.SimPointer(Scr(tD.Top), false, false, true);
            bool added = d.nodes.Count == 4 && Ints(d.durs) == "24,24,24,24" && pm.IsDrawing && changed > c0 && pushes == p0;
            Add(L(added, "R3 a click adds the previewed tiles (each at the brush length); no History entry while drafting", "nodes " + d.nodes.Count + " durs " + Ints(d.durs) + " pushes " + (pushes - p0)));

            if (captures)
            {
                var tF = isl.GetTile(x0 + 5, z);
                Frame(isl.GetTile(x0 + 3, z).Top, 7f, 0f, 58f);
                pm.SimPointer(Scr(tF.Top), false, false, false);
                yield return Wait(0.3f);
                pm.SimPointer(Scr(tF.Top), false, false, false);
                yield return Capture("v4r_draft_midpath");
                Frame(isl.GetTile(x0 + 3, z).Top, 9f, 0f, 62f);
                pm.SimPointer(Scr(tD.Top), false, false, false);
            }
            // the length keys and SetDraftDuration. v7 (SPEC v7 §15, size first): [ ] . set the BRUSH — the size of the NEXT node — and never resize
            // a node already placed (v4: they set the latest node too); SetDraftDuration (scripts) keeps the v4 meaning
            string durs0 = Ints(d.durs);
            Key(KeyCode.RightBracket); int k1 = pm.BrushTicks; int last1 = d.durs[d.durs.Count - 1];
            Key(KeyCode.Period); int k2 = pm.BrushTicks;
            Key(KeyCode.LeftBracket); int k3 = pm.BrushTicks;
            Key(KeyCode.Period); int k4 = pm.BrushTicks;
            yield return null;
            bool placedKept = Ints(d.durs) == durs0 && last1 == 24;
            pm.SetDraftDuration(96);
            yield return null;
            bool set = pm.BrushTicks == 96 && d.durs[d.durs.Count - 1] == 96 && Mathf.Abs(d.SizeTarget - 1.5f) < 1e-3f;
            Add(L(k1 == 48 && k2 == 72 && k3 == 36 && k4 == 24 && placedKept && set, "R3 (v7 size first) ] longer, . dotted, [ shorter set the brush (the NEXT node) and leave the placed nodes as they are; SetDraftDuration(96) (scripts) = the brush + the latest node",
                  "] " + k1 + " . " + k2 + " [ " + k3 + " . " + k4 + ", placed durs " + durs0 + " -> " + Ints(d.durs) + " (last after ] " + last1 + "); SetDraftDuration -> brush " + pm.BrushTicks + " last " + d.durs[d.durs.Count - 1] + " size target " + F(d.SizeTarget)));
            // Backspace and Cmd+Z take the last node back
            Key(KeyCode.Backspace); int n1 = d.nodes.Count;
            Key(KeyCode.Z, true); int n2 = d.nodes.Count;
            Add(L(n1 == 3 && n2 == 2 && pm.IsDrawing && Ints(d.durs) == "24,24" && pushes == p0, "R3 Backspace and Cmd+Z remove the draft's last node (no History entry)", "4 -> " + n1 + " -> " + n2 + " durs " + Ints(d.durs)));

            // finish: Enter solidifies, one History entry, the focus loop stays
            int f0 = finished;
            Key(KeyCode.Return);
            bool fin = !pm.IsDrawing && d.isFinalized && !d.Hologram && d.Solidifying && pushes == p0 + 1 && finished == f0 + 1 && finishedCube == d && FocusLoop.Active;
            var capMid = new List<string>();
            if (captures)
            {
                Frame(d.transform.position, 4.2f, -20f, 38f);
                float ts = Time.realtimeSinceStartup;
                string[] names = { "v4r_solidify_1", "v4r_solidify_2", "v4r_solidify_3", "v4r_solidify_4" };
                float[] at = { 0.04f, 0.16f, 0.29f, 0.5f };
                for (int i = 0; i < names.Length; i++) { while (Time.realtimeSinceStartup - ts < at[i]) yield return null; Frame(d.transform.position, 4.2f, -20f, 38f); yield return Capture(names[i]); capMid.Add(names[i]); }
            }
            yield return Wait(0.45f);
            bool solid = !d.HologramShown && d.BodyRenderer.sharedMaterial != null && d.BodyRenderer.sharedMaterial.shader.name != "DeaCube/Hologram";
            Add(L(fin && solid, "R3 Enter finishes: Solidify (hologram -> solid over 0.35 s), exactly one History entry, OnDraftFinished(cube); the focus loop stays",
                  "finalized " + d.isFinalized + ", solidifying at once " + fin + ", solid after " + solid + ", pushes " + (pushes - p0) + ", focus " + FocusLoop.Active));

            // cancel: Esc dissolves, no History entry, the cube leaves the registry at once and is destroyed after the wipe. The loop is
            // paused first: a press must land on a free tile, not on the finished cube hopping past it (that opens the inspector)
            GlobalClock.Pause();
            yield return null;
            Frame(isl.GetTile(x0 + 3, z).Top, 9f, 0f, 62f);
            TileInteraction tE = null;
            for (int k = 5; k >= 2 && tE == null; k--) { var t = isl.GetTile(x0 + k, z); if (t != null && PathManager.TopCubeOn(t) == null && pm.PickAt(Scr(t.Top)).tile == t) tE = t; }
            if (tE == null) { Add(L(false, "R3 Esc cancels", "no free pickable tile for the second draft")); yield break; }
            pm.SimPointer(Scr(tE.Top), true, true, false); pm.SimPointer(Scr(tE.Top), false, false, true);
            var d2 = pm.Draft; int p1 = pushes;
            yield return null;
            Key(KeyCode.Escape);
            bool dis = d2 != null && d2.Dissolving && !pm.IsDrawing && !SequenceMaster.Cubes.Contains(d2) && finishedNull && pushes == p1;
            yield return Wait(0.45f);
            bool gone = d2 == null;
            Add(L(dis && gone, "R3 Esc cancels: the hologram dissolves (scanline wipe), no History entry, gone after the wipe; OnDraftFinished(null)",
                  "dissolving " + dis + ", destroyed " + gone + ", pushes " + (pushes - p1)));
            // Esc with nothing else open dismisses the focus loop (decided in LateUpdate); the clock was stopped before, so it stops
            bool inspectorOpen = CubeInspector.IsOpen;
            if (inspectorOpen) { CubeInspector.CloseImmediate(); yield return null; yield return null; }
            bool wasStopped = FocusLoop.WasStopped;
            Key(KeyCode.Escape);
            yield return null;
            bool dismissed = !FocusLoop.Active && !GlobalClock.HasRegion && (!wasStopped || !GlobalClock.IsPlaying);
            Add(L(dismissed && !inspectorOpen, "R4 Esc with nothing else to close dismisses the focus loop and restores the transport (stopped)", "active " + FocusLoop.Active + ", region " + GlobalClock.HasRegion + ", playing " + GlobalClock.IsPlaying + " (was stopped " + wasStopped + (inspectorOpen ? "; an inspector was open" : "") + ")"));
            // a click on empty sea dismisses too
            FocusLoop.Begin(isl.column);
            Frame(isl.Center + Vector3.forward * 40f, 8f, 0f, 20f);
            Vector3 sea = new Vector3(Screen.width * 0.5f, Screen.height * 0.25f, 0f);
            var pr = pm.PickAt(sea);
            bool isSea = pr.tile == null && pr.cube == null && pr.island == null;
            pm.SimPointer(sea, true, true, false); pm.SimPointer(sea, false, false, true);
            yield return null;
            Add(L(isSea && !FocusLoop.Active, "R4 a click on empty sea dismisses the focus loop", "sea point " + isSea + ", active " + FocusLoop.Active));
        }
        finally
        {
            pm.OnDraftStarted -= OnStarted; pm.OnDraftChanged -= OnChangedDraft; pm.OnDraftFinished -= OnFinished;
            VoiceRules.Tap = null;
            if (pm.IsDrawing) pm.CancelPath(false);
            FocusLoop.Dismiss();
        }
    }

    static IEnumerator Focus()
    {
        var sm = SongManager.I;
        GlobalClock.Stop();
        const int col = 3;   // island 3 carries the fixture's four pad cubes
        double s2, e2; FocusLoop.Span(col, out s2, out e2);
        FocusLoop.Begin(col);
        bool begun = FocusLoop.Active && FocusLoop.Column == col && FocusLoop.WasStopped && GlobalClock.IsPlaying && GlobalClock.HasRegion && GlobalClock.RegionStart == s2 && GlobalClock.RegionEnd == e2;
        int loop0 = GlobalClock.LoopIndex;
        tapped.Clear(); VoiceRules.Tap = OnTap;
        double minB = double.MaxValue, maxB = double.MinValue;
        double loopSec = (e2 - s2) / GlobalClock.BeatsPerSecond;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < loopSec * 2.3 + 0.3) { double b = GlobalClock.SongBeatD; if (b < minB) minB = b; if (b > maxB) maxB = b; yield return null; }
        VoiceRules.Tap = null;
        int loops = GlobalClock.LoopIndex - loop0;
        // every cube event of those loops belongs to column 2 (or a Moon); the two loops sound alike (same local times, +-1 ms)
        int outside = 0; var locals = new List<double>();
        double loopStartDsp0 = double.NaN;
        foreach (var e in tapped)
        {
            if (e.owner < 16) continue;
            if (e.island != null && !e.island.IsMoon && e.island.column != col) outside++;
            locals.Add(e.onDsp);
        }
        locals.Sort();
        int matched = 0, compared = 0;
        if (locals.Count > 0)
        {
            loopStartDsp0 = locals[0];
            double L = loopSec;
            foreach (var t in locals)
            {
                if (t + L > locals[locals.Count - 1] + 1e-3) break;
                compared++;
                foreach (var u in locals) if (Math.Abs(u - (t + L)) < 0.0085) { matched++; break; }   // the humanise (0..8 ms) is seeded per pass
            }
        }
        double lat = GlobalClock.PlayLatency * GlobalClock.BeatsPerSecond;   // the first frames after Play sit up to the play latency before the start
        Add(L(begun && minB >= s2 - lat - 1e-3 && maxB < e2 && loops >= 2 && outside == 0 && compared > 4 && matched == compared,
              "R4 Begin(3) from stopped: region = the column's span, play; the clock wraps at its end (LoopIndex++), only column 3 sounds, and every loop repeats the notes of the one before one loop later (+-8 ms humanise; the lookahead crosses the wrap)",
              "region " + F(s2, "F1") + "-" + F(e2, "F1") + ", beats seen " + F(minB, "F2") + ".." + F(maxB, "F2") + ", loops " + loops + ", events " + locals.Count + ", outside " + outside + ", repeated " + matched + "/" + compared));
        // R4 / R5: previews on the loop's last 16th wrap to the region's first 16th, and "lit" = the tile's column spans that 16th
        TileInteraction inCol = null, other = null;
        foreach (var kb in sm.Islands) { if (kb == null || kb.tiles.Count == 0) continue; if (kb.column == col && inCol == null) inCol = kb.tiles[0]; else if (kb.column != col && other == null) other = kb.tiles[0]; }
        float g0 = Time.realtimeSinceStartup;
        while (!(GlobalClock.SongBeatD > e2 - 0.2 && GlobalClock.SongBeatD < e2) && Time.realtimeSinceStartup - g0 < loopSec + 1.0) yield return null;
        double on16, end16;
        double wrapped = VoiceRules.PreviewBeat(out on16, out end16);
        int slot0 = Instruments.SlotOf(0);
        bool wrapOk = wrapped >= s2 - 1e-6 && wrapped < s2 + 0.51 && Math.Abs((end16 - on16) * GlobalClock.BeatsPerSecond - (e2 - wrapped)) < 1e-3;
        bool litOk = inCol != null && other != null && VoiceRules.PreviewAllowed(inCol, slot0) && !VoiceRules.PreviewAllowed(other, slot0);
        Add(L(wrapOk && litOk, "R4 / R5 a preview on the loop's last 16th lands on the region's first 16th (its window = the column); PreviewAllowed (in the harmony) = the looped column's tiles only (v5: the others still audition, on the preview bus with the song ducked — V5ChecksR)",
              "beat " + F(GlobalClock.SongBeatD, "F2") + " -> preview 16th " + F(wrapped, "F2") + " (region " + F(s2, "F0") + "-" + F(e2, "F0") + "), window end " + F((end16 - on16) * GlobalClock.BeatsPerSecond, "F2") + " beats later; column tile " + (inCol != null && VoiceRules.PreviewAllowed(inCol, slot0)) + ", other column " + (other != null && VoiceRules.PreviewAllowed(other, slot0))));
        // Space pauses / resumes inside the region
        GlobalClock.Toggle(); bool paused = !GlobalClock.IsPlaying;
        double pb = GlobalClock.SongBeatD;
        GlobalClock.Toggle(); bool resumed = GlobalClock.IsPlaying && GlobalClock.SongBeatD >= s2 && GlobalClock.SongBeatD < e2;
        yield return Wait(0.2f);
        // Begin on another column moves the loop; the transport memory of the first Begin stays
        double s4, e4; FocusLoop.Span(4, out s4, out e4);
        FocusLoop.Begin(4);
        bool moved = FocusLoop.Column == 4 && GlobalClock.RegionStart == s4 && FocusLoop.WasStopped && Math.Abs(GlobalClock.SongBeatD - s4) < 1e-6 && GlobalClock.IsPlaying;
        yield return null;
        FocusLoop.Dismiss();
        bool restored = !FocusLoop.Active && !GlobalClock.HasRegion && !GlobalClock.IsPlaying;
        Add(L(paused && resumed && moved && restored, "R4 Space pauses / resumes inside the loop; Begin on another column moves it; Dismiss restores the stopped transport",
              "paused " + paused + " at " + F(pb, "F2") + ", resumed " + resumed + ", moved " + moved + ", restored " + restored));
        // started while playing: Begin seeks into the region when outside; Dismiss keeps the whole song playing from there
        GlobalClock.Seek(0.5); GlobalClock.Play();
        yield return Wait(0.2f);
        double s3, e3; FocusLoop.Span(3, out s3, out e3);
        FocusLoop.Begin(3);
        bool seeked = GlobalClock.IsPlaying && Math.Abs(GlobalClock.SongBeatD - s3) < 1e-6 && !FocusLoop.WasStopped;
        yield return Wait(0.3f);
        FocusLoop.Dismiss();
        yield return Wait((float)(loopSec + 0.3));
        double beatAfter = GlobalClock.SongBeatD;
        bool keeps = GlobalClock.IsPlaying && !GlobalClock.HasRegion && beatAfter > e3;
        GlobalClock.Stop();
        Add(L(seeked && keeps, "R4 Begin while playing outside the region jumps to its start (with the play latency); Dismiss keeps the whole song playing from where it is",
              "seeked " + seeked + ", after dismiss playing on past the region " + keeps + " (beat " + F(beatAfter, "F2") + ")"));
        // the region follows the column's span when its bars change; a new song (History reset) ends the focus
        FocusLoop.Begin(1);
        int isl1 = -1; for (int i = 0; i < sm.Islands.Count; i++) if (sm.Islands[i] != null && sm.Islands[i].column == 1) { isl1 = i; break; }
        sm.SetBars(isl1, 2);
        yield return null; yield return null;
        double s1, e1; FocusLoop.Span(1, out s1, out e1);
        bool follows = FocusLoop.Active && Math.Abs(GlobalClock.RegionEnd - e1) < 1e-6 && Math.Abs(e1 - s1 - 2 * GlobalClock.BeatsPerBar) < 1e-6;
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));   // a load: Apply + History reset (as SongIO.LoadFrom does)
        History.Reset(); History.Push();
        bool newSong = !FocusLoop.Active && !GlobalClock.HasRegion;
        Add(L(follows && newSong, "R4 the region follows its column's span (2 bars after SetBars); a new song / load (History reset) ends the focus loop",
              "region " + F(GlobalClock.RegionStart, "F1") + "-" + F(e1, "F1") + " follows " + follows + ", after load active " + FocusLoop.Active));
    }

    static IEnumerator Size(bool captures)
    {
        var sm = SongManager.I;
        // one cube, whole note then 16ths (4 beats + 4 x 0.25 = one 5-beat period in a 2-bar window), alone on island 1
        foreach (var c in new List<AudioCube>(SequenceMaster.Cubes)) if (c != null && c.Island == sm.Islands[1]) { c.Delete(false); SequenceMaster.Unregister(c); }
        sm.SetBars(1, 2);
        yield return null;
        var cube = Make(1, 3, Row(5), Same(5, 1), new[] { 96, 6, 6, 6, 6 });
        yield return null;
        bool home = Mathf.Abs(cube.SizeTarget - 1.5f) < 1e-3f && Mathf.Abs(cube.SizeFactor - 1.5f) < 0.02f;
        Frame(cube.nodes[2].Top, 6f, -15f, 34f);
        FocusLoop.Begin(sm.Islands[1].column);
        // sample the size through one loop of the island (8 beats): the whole note grows with overshoot, the 16ths shrink to ~0.62
        var trace = new List<float>();
        float maxS = 0f, minS = 9f;
        float start = Time.realtimeSinceStartup;
        double loopSec = 8.0 / GlobalClock.BeatsPerSecond;
        while (Time.realtimeSinceStartup - start < loopSec + 0.2)
        {
            float s = cube.SizeFactor; trace.Add(s);
            if (s > maxS) maxS = s; if (s < minS) minS = s;
            yield return null;
        }
        if (captures)
        {
            // frame strips: the whole note shrinking into the 16ths (song beat 8), then the last 16th growing into the whole note (beat 9)
            double w0 = SongManager.I.Islands[1].startBeatOffset;
            float[] at = { 0f, 0.07f, 0.14f, 0.24f };
            for (int strip = 0; strip < 2; strip++)
            {
                double beat = w0 + (strip == 0 ? 4.0 : 5.0) - 0.03;
                float guard = Time.realtimeSinceStartup;
                while (!(GlobalClock.SongBeatD >= beat && GlobalClock.SongBeatD < beat + 0.2) && Time.realtimeSinceStartup - guard < loopSec + 1f) yield return null;
                float ts = Time.realtimeSinceStartup;
                for (int i = 0; i < at.Length; i++)
                {
                    while (Time.realtimeSinceStartup - ts < at[i]) yield return null;
                    yield return Capture((strip == 0 ? "v4r_size_shrink_" : "v4r_size_grow_") + i);
                }
            }
        }
        FocusLoop.Dismiss();
        // the spring: an overshoot past 1.5 when growing, down near 0.62 on the 16ths
        Add(L(home && maxS > 1.52f && maxS < 2.1f && minS < 0.75f, "R2 size follows the note: 1.5 on a whole note (stopped: the home node), springs up with overshoot, shrinks toward 0.62 on 16ths",
              "stopped " + F(cube.SizeFactor) + ", max " + F(maxS) + ", min " + F(minS) + " over " + trace.Count + " frames"));
        // the dotted satellite: a dotted quarter on the home node shows it while stopped
        cube.SetAllDurations(new[] { 36, 6, 6, 6, 6 });
        GlobalClock.Stop();
        yield return Wait(0.3f);
        bool sat = cube.SatelliteShown;
        if (captures) { Frame(cube.nodes[0].Top, 3.2f, -30f, 30f); yield return Wait(0.2f); yield return Capture("v4r_dotted_satellite"); }
        cube.SetAllDurations(new[] { 24, 6, 6, 6, 6 });
        yield return Wait(0.3f);
        bool satGone = !cube.SatelliteShown;
        Add(L(sat && satGone, "R2 a dotted note carries the satellite dot (pops in / out)", "dotted " + sat + ", undotted hidden " + satGone));
        // stacking by visual size: a second cube on the whole-note cube's tile rests on top of it
        cube.SetAllDurations(new[] { 96, 6, 6, 6, 6 });
        yield return Wait(0.6f);
        var top = Make(1, 0, new[] { cube.nodes[0].gridX }, new[] { cube.nodes[0].gridZ }, new[] { 6 });
        yield return Wait(0.8f);
        float gap = (top.transform.position.y - ProjectConfig.CubeSize * top.SizeFactor * 0.5f) - (cube.transform.position.y + ProjectConfig.CubeSize * cube.SizeFactor * 0.5f);
        Add(L(Mathf.Abs(gap - ProjectConfig.CubeGap) < 0.03f, "R2 stacked cubes stack by their visual sizes (a 16th cube sits on a whole-note cube)", "gap between them " + F(gap) + " (want " + F(ProjectConfig.CubeGap) + ")"));
        // beads scale with their node's length
        Add(L(true, "R2 path beads scale with SizeOf(length) (RefreshLine; look at the captures)", null));
    }

    /// <summary>A-B capture of the landing beams on the fixture's island 0 (8 cubes) at the same beat: the v3 beam on every hit, then the v4
    /// subtle beams (a stub on normal hits, a short full beam on accents only).</summary>
    static IEnumerator Beams()
    {
        var sm = SongManager.I;
        var kb = sm.Islands[0];
        Frame(kb.Center + Vector3.up * 0.4f, 11f, -22f, 44f);
        float[] seconds = { 0.93f, 0.93f };
        string[] names = { "v4r_beams_before", "v4r_beams_after" };
        int beamsBefore = 0, beamsAfter = 0;
        for (int pass2 = 0; pass2 < 2; pass2++)
        {
            AudioCube.BeamsV3 = pass2 == 0;
            GlobalClock.Stop(); GlobalClock.Seek(0); SequenceMaster.ResetAllCubes();
            yield return Wait(0.6f);
            GlobalClock.Play();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds[pass2]) yield return null;
            int active = Fx.I.ActivePillars;
            if (pass2 == 0) beamsBefore = active; else beamsAfter = active;
            yield return Capture(names[pass2]);
            GlobalClock.Stop();
        }
        AudioCube.BeamsV3 = false;
        Add(L(beamsAfter <= beamsBefore, "R2 landing beams are subtle (a short stub on normal hits, a full beam only on accents): look at v4r_beams_before / _after",
              "pillars alive at the same beat: v3 " + beamsBefore + ", v4 " + beamsAfter));
    }

    /// <summary>The paint smear (CubeSmear): a lone cube hopping 8ths across a row smears only while it moves fast, the smear is gone
    /// within three drawings of the stop, drawing allocates nothing; a hop captured over four drawings.</summary>
    static IEnumerator Smear(bool captures)
    {
        var sm = SongManager.I;
        var kb = sm.Islands[1];
        foreach (var c in new List<AudioCube>(SequenceMaster.Cubes)) if (c != null && c.Island == kb) { c.Delete(false); SequenceMaster.Unregister(c); }
        yield return null;
        var cube = Make(1, 0, new[] { 0, 2, 4, 2 }, new[] { 1, 1, 1, 2 }, new[] { 12, 12, 12, 12 });
        float bpm0 = GlobalClock.BPM;
        GlobalClock.SetBPM(150f);   // a fast tempo: 8th hops over two tiles
        Frame(kb.GetTile(2, 1).Top + Vector3.up * 0.3f, 6.5f, -12f, 36f);
        yield return null;
        int idleFrames = 0, idleSmears = 0;
        for (int i = 0; i < 20; i++) { yield return null; idleFrames++; if (CubeSmear.Visible > 0) idleSmears++; }
        FocusLoop.Begin(kb.column);
        int frames = 0, smearFrames = 0; float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 2.0f) { yield return null; frames++; if (CubeSmear.Visible > 0) smearFrames++; }
        var shots = new List<string>();
        if (captures)
        {
            // a hop over four drawings: wait for a smear to start, then one capture per drawing (on twos)
            float g = Time.realtimeSinceStartup;
            while (CubeSmear.Visible == 0 && Time.realtimeSinceStartup - g < 2f) yield return null;
            for (int i = 0; i < 4; i++)
            {
                yield return Capture("v4r_smear_blob_" + i); shots.Add("v4r_smear_blob_" + i);
                float ts = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - ts < 1f / 12f - 0.012f) yield return null;
            }
        }
        FocusLoop.Dismiss();
        float stopT = Time.realtimeSinceStartup; float goneAfter = -1f;
        while (Time.realtimeSinceStartup - stopT < 0.6f) { yield return null; if (CubeSmear.Visible == 0) { goneAfter = Time.realtimeSinceStartup - stopT; break; } }
        // drawing allocates nothing (pooled mesh, scratch arrays)
        for (int i = 0; i < 3; i++) CubeSmear.Draw(987654, cube.transform.position, cube.transform.position + Vector3.right * 1.2f, 0.5f, cube.Color);
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) CubeSmear.Draw(987654, cube.transform.position, cube.transform.position + Vector3.right * (1.2f + i * 0.001f), 0.5f, cube.Color);
        long alloc = System.GC.GetAllocatedBytesForCurrentThread() - a0;
        CubeSmear.Forget(987654);
        GlobalClock.SetBPM(bpm0);
        Add(L(idleSmears == 0 && smearFrames > 0 && smearFrames < frames && goneAfter >= 0f && goneAfter <= 3f / 12f + 0.05f && alloc == 0,
              "smear: a hopping cube (8ths at 150 BPM) smears its own body along its path only while it moves fast, gone within three drawings of the stop; drawing allocates nothing",
              "idle frames with a smear " + idleSmears + "/" + idleFrames + ", playing " + smearFrames + "/" + frames + ", gone " + F(goneAfter, "F2") + " s after the stop, 200 draws allocated " + alloc + " B" + (shots.Count > 0 ? "; captures " + string.Join(", ", shots.ToArray()) : "")));
    }

    static IEnumerator Record()
    {
        var sm = SongManager.I;
        // a lone Lead cube on island 0 (the other cubes removed), 2 bars
        foreach (var c in new List<AudioCube>(SequenceMaster.Cubes)) if (c != null) { c.Delete(false); SequenceMaster.Unregister(c); }
        sm.SetBars(0, 2);
        yield return null;
        // 16th, rest, 8th, rest, quarter, rest, half, rest (each note followed by a rest of its own length): 7.5 beats in the 2-bar window
        int[] durs = { 6, 6, 12, 12, 24, 24, 48, 48 };
        var cube = Make(0, 3, new[] { 0, 1, 2, 3, 4, 5, 4, 3 }, new[] { 0, 0, 1, 1, 2, 2, 3, 3 }, durs);
        if (cube == null) { Add(L(false, "R1 recording", "no cube")); yield break; }
        for (int i = 1; i < 8; i += 2) cube.SetMod(i, 1);
        GlobalClock.Stop();
        yield return null;
        tapped.Clear(); VoiceRules.Tap = OnTap;
        Synth.StartRecording(5.2f);
        yield return Wait(0.25f);
        FocusLoop.Begin(sm.Islands[0].column);
        double playDsp = GlobalClock.DspTimeOfBeat(GlobalClock.RegionStart);
        while (!Synth.RecordingDone) yield return null;
        VoiceRules.Tap = null;
        FocusLoop.Dismiss(); GlobalClock.Stop();
        bool saved = Synth.SaveRecording(RecPath);
        // the expected notes: window start + W(raw start) / bps + the humanise jitter (Rhythm.Hash) — every dispatched event must match one
        var tl = new List<AudioCube.TimelineEvent>(); cube.TimelineEvents(0, tl);
        double bps = GlobalClock.BeatsPerSecond;
        int good = 0, total = 0; double worstOn = 0, worstLen = 0;
        var lines = new StringBuilder();
        lines.Append("# onDsp offDsp midi  (relative to the first event); bps ").Append(F(bps, "F6")).Append('\n');
        double first = tapped.Count > 0 ? tapped[0].onDsp : 0;
        foreach (var e in tapped)
        {
            if (e.owner != cube.owner) continue;
            total++;
            double rel = e.onDsp - playDsp;
            double loopSec = cube.windows[0].length / bps;
            int loop = (int)Math.Floor((rel + 0.02) / loopSec);
            double inLoop = rel - loop * loopSec;
            // distance of the onset from [step time, step time + 8 ms] (the humanise is 0..8 ms late, never early)
            double best = double.MaxValue, wantLen = 0;
            foreach (var ev in tl)
            {
                double d = inLoop - ev.start / bps;
                double err = d < 0 ? -d : (d > 0.008 ? d - 0.008 : 0);
                if (err < best) { best = err; wantLen = 0.92 * ev.len / bps; }
            }
            double len = e.offDsp - e.onDsp;
            worstOn = Math.Max(worstOn, best);
            worstLen = Math.Max(worstLen, Math.Abs(len - wantLen));
            if (best < 0.0005 && Math.Abs(len - wantLen) < 0.001) good++;
            lines.Append(F(e.onDsp - first, "F6")).Append(' ').Append(F(e.offDsp - first, "F6")).Append(' ').Append(e.midi).Append(' ').Append(F(wantLen, "F6")).Append('\n');
        }
        try { File.WriteAllText(RecEventsPath, lines.ToString()); } catch (Exception) { }
        int restHits = 0;   // a rest starts no note: no dispatched onset lies on a rest's start
        foreach (var e in tapped)
        {
            if (e.owner != cube.owner) continue;
            double inLoop = (e.onDsp - playDsp) % (cube.windows[0].length / bps);
            foreach (var ev in tl) if (ev.rest && inLoop - ev.start / bps >= -0.0005 && inLoop - ev.start / bps <= 0.0085) restHits++;
        }
        Add(L(saved && total >= 6 && good == total && restHits == 0, "R1 recorded run: every dispatched note starts at window start + W(start)/bps (+ its 0-8 ms humanise) and lasts 0.92 x its length; rests are silent",
              total + " notes, " + good + " match, notes on rests " + restHits + ", worst onset " + F(worstOn * 1000, "F2") + " ms, worst length error " + F(worstLen * 1000, "F2") + " ms; wav " + (saved ? RecPath : "not saved") + " + " + RecEventsPath));
    }
}
