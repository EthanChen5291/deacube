using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// v9 (W) Play-mode verification of the NEW present mode (the user, 2026-10-01: "scrap the current falling play mode, just make it record of the
/// cube path as we have now, with camera slowly falling and islands appearing from the watter as the measures pass (with water animations)"):
/// <c>V9ChecksPresent.RunAll()</c> starts a coroutine; poll <c>V9ChecksPresent.Done</c> or Captures/w9_report.txt (numbered PASS / FAIL lines).
/// Part A, a compact test song (8 one-bar columns at 120 bpm, two sections, chord grids, a second lane, a belt, stairs, a keyboard KeyStage drives,
/// one it does not, two Moons; loop off): enter (HUD hidden, locks on, every island, Moon and floor under the sea within a frame or two), the live
/// run (every column's scheduled islands still under 2 beats ahead and up by its first beat, the floor never above one of its islands, the water
/// at each breach, the camera falling steadily — lower at 75 % than at 25 % of the song, never jumping —, the playing column in view, KeyStage's
/// keyboard left to KeyStage while the song plays), pause (the beat, the camera, the islands and the water hold), a seek is exact (the same pose
/// reached twice), the end (every island and floor up, replay / exit shown), replay (everything under again, the song from the top), exit (camera
/// pose and settings, islands back exactly — positions, colliders —, floors, input, HUD, the playhead). Part B: the same song looping (the veil
/// dips at the loop point and the song starts over under it). Part C: the gallery song "get-proto" (98 keyboards) and part D "polka-dots": enter,
/// first breaches, a seek to the middle and the end, exit exact. Throughout: no console errors, no allocation per frame in the steady state.
/// Captures w9_*.png. Never writes the user's save (the autosave and backups are snapshotted and restored).
/// </summary>
public static class V9ChecksPresent
{
    public static string Report = "";
    public static bool Done = true;
    static int num, pass, fail;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "w9_report.txt");
    static StringBuilder sb;
    static bool shots;

    static void Line(bool ok, string name, string detail)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" W9-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(); }
    static void Flush() { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void Check(string name, Func<string> c)
    {
        try { string d = c(); Line(d == null || d.StartsWith("ok"), name, d ?? "ok"); }
        catch (Exception e) { Line(false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    static string SecondFrame(Exception e) { var s = (e.StackTrace ?? "").Split('\n'); return s.Length > 1 ? s[1].Trim() : ""; }
    static string F(double v) => v.ToString("F2");
    static SongManager SM => SongManager.I;
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test song
    static MeasureState Chord(int col, string name, int root, int[] semis, float pz = 0f, int repeat = 1) => new MeasureState { chordKey = name, root = root, semis = semis, bars = 1, kind = 0, col = col, repeat = repeat, energy = 2, pz = pz, placed = true };
    static MeasureState Keys(int col, float pz) => new MeasureState { chordKey = "Keys", root = 48, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, pz = pz, placed = true, keyCount = 25 };
    static MeasureState Stairs(int col, float pz) => new MeasureState { chordKey = "Stairs", root = 60, semis = new[] { 0 }, bars = 1, kind = 3, placed = true, col = col, energy = 2, repeat = 1, stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true, stairType = 0, pz = pz };
    static CubeState Cube(int id, int inst, int measure, int[] xs, int[] zs, int ticks)
    {
        int n = xs.Length;
        var durs = new int[n]; for (int i = 0; i < n; i++) durs[i] = ticks;
        return new CubeState { id = id, instrument = inst, measure = measure, xs = (int[])xs.Clone(), zs = (int[])zs.Clone(), rests = new bool[n], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f, hits = -1, seed = id, moon = -1, twinOf = -1, echoOf = -1 };
    }

    /// <summary>C Am | F | G + keys (a melody: KeyStage drives it) | C ×2 (a belt) || Dm + stairs | G7 | C + keys (no notes) | F; Moons at columns 0 and
    /// 4; sections at 0 and 4; 120 bpm.</summary>
    public static SongState TestSong(bool loop)
    {
        var m = new List<MeasureState>
        {
            Chord(0, "C", 60, new[] { 0, 4, 7 }), Chord(0, "Am", 57, new[] { 0, 3, 7 }, 10f),
            Chord(1, "F", 65, new[] { 0, 4, 7 }),
            Chord(2, "G", 67, new[] { 0, 4, 7 }), Keys(2, 5f),
            Chord(3, "C", 60, new[] { 0, 4, 7 }, 0f, 2),
            Chord(4, "Dm", 62, new[] { 0, 3, 7 }), Stairs(4, 5f),
            Chord(5, "G7", 67, new[] { 0, 4, 7, 10 }),
            Chord(6, "C", 60, new[] { 0, 4, 7 }), Keys(6, 5f),
            Chord(7, "F", 65, new[] { 0, 4, 7 }),
        };
        var cubes = new List<CubeState>
        {
            Cube(9101, 0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, 24),
            Cube(9102, 4, 2, new[] { 0, 2, 4, 2 }, new[] { 1, 0, 1, 2 }, 24),
            Cube(9103, 8, 4, new[] { 12, 14, 16, 19 }, new[] { 0, 0, 0, 0 }, 24),
            Cube(9104, 0, 5, new[] { 5, 4, 3, 2 }, new[] { 0, 0, 1, 1 }, 24),
            Cube(9105, 4, 6, new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, 32),
        };
        cubes.Add(new CubeState { id = 9106, instrument = 9, measure = 0, moon = 0, xs = new[] { 3 }, zs = new[] { 0 }, rests = new bool[1], step = 1, gate = 1, volume = 1f, hits = 4, twinOf = -1, echoOf = -1 });
        var moons = new[]
        {
            new MeasureState { chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1, col = 0, px = 0f, pz = -12f },
            new MeasureState { chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1, col = 4, px = 0f, pz = -12f },
        };
        return new SongState
        {
            version = SongState.CurrentVersion, name = "w9 present", bpm = 120f, beatsPerBar = 4, loop = loop, measures = m.ToArray(), cubes = cubes.ToArray(),
            moons = moons, keyTonic = 0, keyMinor = false, sections = new[] { 0, 4 }, tone = 1f, space = 1f, demoSeed = 9
        };
    }

    static IEnumerator Load(SongState st)
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        if (PathManager.I != null) PathManager.I.PutDown();
        SongState.Apply(st);
        History.Reset(); History.Push();
        yield return Frames(4);
        SectionPlinth.RefreshNow();
        KeyHands.RefreshNow();
        yield return Wait(0.8f);
    }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; pass = 0; fail = 0; errors.Clear(); shots = captures;
        SequenceMaster.I.StartCoroutine(AllRoutine());
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        Presenter.ScrubBeat = double.NaN;
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Until(Func<bool> c, float timeout) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < timeout) { bool ok = false; try { ok = c(); } catch (Exception) { } if (ok) yield break; yield return null; } }

    static IEnumerator Shot(string file)
    {
        if (!shots) yield break;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        int n0 = Presenter.CapturesDone;
        if (Presenter.Active) { Presenter.RequestCapture(p, 1600, 900); yield return Until(() => Presenter.CapturesDone > n0, 2f); }
        else { yield return null; Presenter.RenderToPng(Camera.main, p, 1600, 900); }
        Info("capture " + file + " at song beat " + F(Presenter.SongBeat));
    }
    static IEnumerator ScreenShot(string file)
    {
        if (!shots) yield break;
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return Frames(3);
        Info("screen capture " + file);
    }

    static float HudTarget()
    {
        var ui = UIManager.I; if (ui == null) return -1f;
        var f = typeof(UIManager).GetField("hudAlphaTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? (float)f.GetValue(ui) : -1f;
    }

    // ------------------------------------------------------------------ world snapshot (the exit's "exactly where it was")
    struct Snap { public KeyBlock kb; public Vector3 pos; public Quaternion rot; public int colliders, enabled; }
    static List<Snap> SnapIslands()
    {
        var l = new List<Snap>();
        foreach (var kb in SM.Islands) if (kb != null) l.Add(SnapOf(kb));
        foreach (var kb in SM.Moons) if (kb != null) l.Add(SnapOf(kb));
        return l;
    }
    static Snap SnapOf(KeyBlock kb)
    {
        var cs = kb.GetComponentsInChildren<Collider>(true); int en = 0; foreach (var c in cs) if (c.enabled) en++;
        return new Snap { kb = kb, pos = kb.transform.position, rot = kb.transform.rotation, colliders = cs.Length, enabled = en };
    }
    static List<float> SnapFloors()
    {
        var l = new List<float>();
        for (int s = 0; s < SectionPlinth.Count; s++) for (int m = 0; m < SectionPlinth.MeasuresShown(s) + SectionPlinth.GhostsShown(s); m++) { float a, b; bool g; int c; if (!SectionPlinth.MeasureSpan(s, m, out a, out b, out g, out c)) break; l.Add(SectionPlinth.TopOf(s, m)); }
        return l;
    }

    /// <summary>The top of riser <paramref name="i"/> now (world y): its rest top minus its sea drop.</summary>
    static float TopNow(PresentSea sea, int i)
    {
        var r = sea[i];
        if (r.kind == PresentSea.Kind.Floor) return r.top - SectionPlinth.SinkOf(r.col);
        return r.kb != null ? r.top - r.kb.SeaDrop : float.NaN;
    }

    // ================================================================== the run
    static IEnumerator AllRoutine()
    {
        sb = new StringBuilder();
        sb.Append("V9ChecksPresent ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var snap = V3Fixes.SnapshotSaves();
        string userMd5 = SaveHash();
        Prepare();
        bool loop0 = GlobalClock.LoopSong, ks0 = KeyStage.Enabled;
        KeyStage.Enabled = true;
        IEnumerator[] parts = { PartA(), PartB(), PartC("get-proto", "w9_proto"), PartC("polka-dots", "w9_polka"), PartFog() };
        foreach (var part in parts)
        {
            // the part and every step it waits on (a load, a capture, a wait) run here, so an exception anywhere is a FAIL line with its frame —
            // never a coroutine that dies silently and leaves the run hanging
            var stack = new Stack<IEnumerator>();
            stack.Push(part);
            while (stack.Count > 0)
            {
                var top = stack.Peek(); object cur = null; bool more;
                try { more = top.MoveNext(); if (more) cur = top.Current; }
                catch (Exception e) { Line(false, "exception", e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e) + " | " + SecondFrame(e)); break; }
                if (!more) { stack.Pop(); continue; }
                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return cur;
            }
            try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
            yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
            Presenter.ScrubBeat = double.NaN;
        }
        Application.logMessageReceived -= OnLog;
        Check("no console errors or exceptions during the whole run", () => errors.Count == 0 ? "ok" : errors.Count + ": " + string.Join(" | ", errors.ToArray()));
        GlobalClock.Stop();
        GlobalClock.LoopSong = loop0; KeyStage.Enabled = ks0;
        V3Fixes.RestoreSaves(snap);
        Check("the user's save is untouched", () => SaveHash() == userMd5 ? "ok: " + (userMd5 ?? "absent") : "md5 " + SaveHash() + " (was " + userMd5 + ")");
        Done = true;
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Flush();
    }

    static string SaveHash()
    {
        try
        {
            if (!File.Exists(SongIO.Path)) return null;
            using (var md5 = System.Security.Cryptography.MD5.Create()) { var h = md5.ComputeHash(File.ReadAllBytes(SongIO.Path)); return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant(); }
        }
        catch (Exception) { return "?"; }
    }

    // ------------------------------------------------------------------ part A: the test song, loop off
    static IEnumerator PartA()
    {
        Info("part A: the test song (loop off)");
        Prepare();
        yield return Load(TestSong(false));
        var sm = SM; var cam = Camera.main; var oc = OrbitCamera.I;
        if (oc != null) oc.FrameBounds(sm.ColumnBounds(0), 0.1f, true, 1f);
        GlobalClock.Seek(4.0);                                    // a playhead away from 0: the exit must put it back
        yield return Wait(1.5f);                                  // the belts and Moons glide to the playhead's pose first
        Info("song: " + sm.Islands.Count + " islands, " + sm.Moons.Count + " Moons, " + sm.ColumnCount + " columns, " + F(sm.TotalBeats) + " beats, " + SectionPlinth.Count + " plinths");
        var before = SnapIslands(); var floors0 = SnapFloors();
        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
        int mask0 = cam.cullingMask; var clear0 = cam.clearFlags; Color bg0 = cam.backgroundColor; float fov0 = cam.fieldOfView, near0 = cam.nearClipPlane, far0 = cam.farClipPlane;
        bool fog0 = RenderSettings.fog; float fs0 = RenderSettings.fogStartDistance, fe0 = RenderSettings.fogEndDistance; Color fc0 = RenderSettings.fogColor;
        float hud0 = HudTarget(); string locks0 = WorldInput.Describe(); double beat0 = GlobalClock.SongBeatD; Vector3 sea0 = Fx.SeaCentre;
        yield return Shot("w9_before.png");

        // ---- enter
        Presenter.Toggle();
        bool covering = Presenter.CurrentPhase == Presenter.Phase.Covering && Presenter.Active;
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Presenting, 3f);
        yield return Frames(2);
        var sea = Presenter.Sea;
        Check("enter: P / the HUD button (Toggle) covers with the veil, then presents; the HUD hides, the world and keys lock (\"present\"), the orbit camera is suspended", () =>
        {
            if (!covering) return "not covering right after Toggle (" + Presenter.CurrentPhase + ")";
            if (Presenter.CurrentPhase != Presenter.Phase.Presenting || sea == null) return "phase " + Presenter.CurrentPhase;
            if (HudTarget() != 0f) return "HUD alpha target " + HudTarget();
            if (!WorldInput.IsLockedBy("present") || !WorldInput.KeysLocked) return "locks " + WorldInput.Describe();
            if (oc != null && !oc.Suspended) return "orbit camera not suspended";
            return "ok: " + sea.Count + " risers (" + sea.StagedCount + " keyboards KeyStage drives), locks " + WorldInput.Describe();
        });
        Check("enter: within two frames every island, Moon and section floor is under the sea (its top below the water), colliders off", () =>
        {
            if (sea == null) return "no sea";
            int islands = 0, floorsN = 0;
            for (int i = 0; i < sea.Count; i++)
            {
                var r = sea[i];
                float top = TopNow(sea, i);
                if (r.kind != PresentSea.Kind.Floor && Presenter.SongBeat >= r.start) continue;     // (the first column may already be rising)
                if (!(top < PresentSea.SeaY - 0.5f)) return r.kind + " of column " + r.col + " top at " + F(top);
                if (r.kind == PresentSea.Kind.Floor) floorsN++;
                else
                {
                    islands++;
                    foreach (var c in r.kb.GetComponentsInChildren<Collider>(true)) if (c.enabled) return "a collider on under the sea: " + r.kb.name;
                }
            }
            for (int i = 0; i < sea.StagedCount; i++) { var s = sea.StagedAt(i); if (s.kb != null && s.kb.SeaDrop < KeyBlock.SeaHide - 0.05f) return "KeyStage's keyboard " + s.kb.name + " not held under (drop " + F(s.kb.SeaDrop) + ")"; }
            return "ok: " + islands + " islands / Moons and " + floorsN + " floors under, beat " + F(Presenter.SongBeat);
        });
        yield return Wait(0.45f);                                   // the veil clears
        yield return Shot("w9_start.png");

        // ---- the live run: sample every frame
        double total = sea.TotalBeats;
        int nc = sm.ColumnCount;
        var aheadWorst = new float[nc]; var upWorst = new float[nc]; var sawAhead = new bool[nc]; var sawUp = new bool[nc];
        for (int c = 0; c < nc; c++) { aheadWorst[c] = float.NegativeInfinity; upWorst[c] = 0f; }
        float y25 = float.NaN, y75 = float.NaN, maxRise = 0f, maxSpeed = 0f, maxStepDt = 0f, prevDt = 0f, prevDt2 = 0f; double maxSpeedAt = 0.0, maxSpeedDb = 0.0; int outOfView = 0, seaOff = 0, stallFrames = 0, fogOff = 0, viewSamples = 0, floorAbove = 0, stagedTouched = 0, stagedSamples = 0;
        string floorWhy = null, viewWhy = null;
        double lastLocal = double.NaN; Vector3 lastPos = cam.transform.position;
        string lastTgt = Tgt(), worstTgt = "";
        bool breachShot = false, midShot = false, paused = false, sawWater = false, gcReset = false;
        int maxDrops = 0, maxRings = 0, maxCrests = 0, maxSheets = 0;
        float t0 = Time.realtimeSinceStartup;
        double breachCol = sm.ColumnStart(4);                     // the second section: a floor, stairs and a Moon break the surface
        double firstStill = double.NaN; float stillT = 0f;
        while (Time.realtimeSinceStartup - t0 < (float)(total / GlobalClock.BeatsPerSecond) + 14f)
        {
            yield return null;
            if (!Presenter.Active) break;
            double b = Presenter.SongBeat;
            sea = Presenter.Sea; if (sea == null) break;
            var w = Presenter.Water;
            // the frozen-audio guard (environmental: dspTime not advancing)
            if (Presenter.Clock == Presenter.ClockMode.Live && !Presenter.Paused) { if (!double.IsNaN(lastLocal) && Math.Abs(b - lastLocal) < 1e-9) { stillT += Time.unscaledDeltaTime; if (stillT > 1.5f && double.IsNaN(firstStill)) firstStill = b; } else stillT = 0f; }
            // per column: 2+ beats ahead still under; up by its first beat
            for (int i = 0; i < sea.Count; i++)
            {
                var r = sea[i]; if (r.kind == PresentSea.Kind.Floor) continue;
                double rel = b - r.downbeat;
                float top = TopNow(sea, i);
                if (rel >= -2.3 && rel <= -2.0) { sawAhead[r.col] = true; aheadWorst[r.col] = Mathf.Max(aheadWorst[r.col], top - PresentSea.SeaY); }
                if (rel >= 0.0 && rel <= 0.6 && Presenter.Clock != Presenter.ClockMode.Preroll || (rel >= 0.0 && rel <= 0.6 && r.col == 0)) { sawUp[r.col] = true; upWorst[r.col] = Mathf.Max(upWorst[r.col], r.kb.SeaDrop); }
            }
            // the floor never above one of its islands (its top under the island's underside)
            for (int i = 0; i < sea.Count; i++)
            {
                var r = sea[i]; if (r.kind == PresentSea.Kind.Floor || r.kb == null) continue;
                float floorSink = SectionPlinth.SinkOf(r.col), islandSink = r.kb.SeaDrop;
                if (floorSink < islandSink - 0.3f) { floorAbove++; if (floorWhy == null) floorWhy = "column " + r.col + " floor sink " + F(floorSink) + " vs island " + F(islandSink) + " at " + F(b); }
            }
            // KeyStage's keyboards: left alone while the song plays (settled)
            if (Presenter.Clock == Presenter.ClockMode.Live && !Presenter.Paused && Time.realtimeSinceStartup - t0 > 3f)
                for (int i = 0; i < sea.StagedCount; i++) { var s = sea.StagedAt(i); if (s.kb == null) continue; stagedSamples++; if (s.kb.SeaDepth > 1e-4f) stagedTouched++; }
            // the camera: falling steadily, never jumping, the playing column in view
            Vector3 cp = cam.transform.position;
            if (Presenter.Clock == Presenter.ClockMode.Live && !Presenter.Paused && !double.IsNaN(lastLocal) && b > lastLocal && b - lastLocal < 0.5)
            {
                maxRise = Mathf.Max(maxRise, (cp.y - lastPos.y) / Mathf.Max(1e-3f, prevDt));   // upward speed (u/s)
                // this coroutine runs between Update and LateUpdate: the move it sees happened in the LAST frame (the Presenter's LateUpdate), over
                // that frame's dt (prevDt); an editor stall (that frame or the one before over 50 ms, or the beat leaping a quarter) is not counted
                float fdt = Mathf.Max(1e-3f, prevDt), sp = (cp - lastPos).magnitude / fdt;
                if (prevDt > 0.05f || prevDt2 > 0.05f || b - lastLocal > 0.25) stallFrames++;
                else if (sp > maxSpeed) { maxSpeed = sp; maxStepDt = fdt; maxSpeedAt = b; maxSpeedDb = b - lastLocal; worstTgt = "before " + lastTgt + " | after " + Tgt() + " | camera " + lastPos.ToString("F2") + " → " + cp.ToString("F2"); }
            }
            if (double.IsNaN(y25) && b >= total * 0.25) y25 = cp.y;
            if (double.IsNaN(y75) && b >= total * 0.75) y75 = cp.y;
            if (b >= 0.0 && b < total)
            {
                int col = sm.ActiveColumn((float)b);
                viewSamples++;
                string vw = ColumnInView(sm, col, b, cam);
                if (vw != null) { outOfView++; if (viewWhy == null) viewWhy = vw + " at beat " + F(b); }
            }
            // the fog follows the present camera's distance (Fx sets it each frame, a frame behind at most)
            if (Mathf.Abs(RenderSettings.fogStartDistance - Fx.FogStartFor(Presenter.Rig.Target.distance)) > 0.6f) fogOff++;
            // the sea stays under the camera
            { var sc = Fx.SeaCentre; var fo = Presenter.Rig.Target.focus; if (new Vector2(sc.x - fo.x, sc.z - fo.z).magnitude > 0.5f) seaOff++; }
            // water at the breaches
            if (w != null)
            {
                maxDrops = Math.Max(maxDrops, w.Drops); maxRings = Math.Max(maxRings, w.Rings); maxCrests = Math.Max(maxCrests, w.Crests); maxSheets = Math.Max(maxSheets, w.Sheets);
                if (w.Drops > 0 && w.Rings > 0 && w.Crests > 0) sawWater = true;
            }
            if (!gcReset && Presenter.Clock == Presenter.ClockMode.Live && b > 1.0) { Presenter.ResetStats(); gcReset = true; }
            if (!breachShot && b >= breachCol - 1.0 + 0.12 * GlobalClock.BeatsPerSecond) { breachShot = true; yield return Shot("w9_breach.png"); lastLocal = double.NaN; lastPos = cam.transform.position; continue; }
            if (!midShot && b >= total * 0.5) { midShot = true; yield return Shot("w9_mid.png"); lastLocal = double.NaN; lastPos = cam.transform.position; continue; }
            // pause at ~40 %: the beat, the camera, the islands and the water hold; resume moves on
            if (!paused && b >= total * 0.4)
            {
                paused = true;
                Presenter.SetPaused(true);
                yield return Frames(2);
                double pb = Presenter.Beat; Vector3 pp = cam.transform.position; Quaternion pr = cam.transform.rotation;
                var depths = new float[sea.Count]; for (int i = 0; i < sea.Count; i++) depths[i] = sea[i].kb != null ? sea[i].kb.SeaDepth : SectionPlinth.SinkOf(sea[i].col);
                int drops = w != null ? w.Drops : 0; Vector3 firstDrop = Vector3.zero;
                yield return Wait(0.6f);
                Check("pause: the beat, the camera, the islands, the floors and the water hold still; the clock stops", () =>
                {
                    if (GlobalClock.IsPlaying) return "the clock still plays";
                    if (Math.Abs(Presenter.Beat - pb) > 1e-9) return "beat " + F(pb) + " → " + F(Presenter.Beat);
                    if ((cam.transform.position - pp).magnitude > 1e-4f || Quaternion.Angle(cam.transform.rotation, pr) > 1e-3f) return "camera moved " + (cam.transform.position - pp).magnitude.ToString("E2");
                    for (int i = 0; i < sea.Count; i++) { float d = sea[i].kb != null ? sea[i].kb.SeaDepth : SectionPlinth.SinkOf(sea[i].col); if (Mathf.Abs(d - depths[i]) > 1e-5f) return "riser " + i + " moved " + depths[i] + " → " + d; }
                    if (w != null && w.Drops != drops) return "water pieces " + drops + " → " + w.Drops;
                    return "ok: held at beat " + F(Presenter.SongBeat);
                });
                // a seek is exact: scrub away and back, the same pose
                Presenter.ScrubBeat = pb;
                yield return Frames(2);
                Vector3 sp1 = Presenter.Rig.Target.position; var d1 = new float[sea.Count]; for (int i = 0; i < sea.Count; i++) d1[i] = sea[i].kb != null ? sea[i].kb.SeaDepth : SectionPlinth.SinkOf(sea[i].col);
                Presenter.ScrubBeat = pb + 7.0;
                yield return Frames(2);
                Presenter.ScrubBeat = pb;
                yield return Frames(2);
                Check("seek: the camera target and every depth at a beat are the same however it is reached (pure functions of the beat)", () =>
                {
                    if ((Presenter.Rig.Target.position - sp1).magnitude > 1e-4f) return "camera target differs by " + (Presenter.Rig.Target.position - sp1).magnitude.ToString("E2");
                    for (int i = 0; i < sea.Count; i++) { float d = sea[i].kb != null ? sea[i].kb.SeaDepth : SectionPlinth.SinkOf(sea[i].col); if (Mathf.Abs(d - d1[i]) > 1e-5f) return "riser " + i + ": " + d1[i] + " vs " + d; }
                    if ((cam.transform.position - Presenter.Rig.Target.position).magnitude > 1e-3f) return "the shown camera is not on its target after the seek";
                    return "ok";
                });
                Presenter.ScrubBeat = double.NaN;
                Presenter.SetPaused(false);
                yield return Wait(0.4f);
                Check("resume: the clock plays on from where it paused", () => GlobalClock.IsPlaying && Presenter.Beat > pb ? "ok: " + F(pb) + " → " + F(Presenter.Beat) : "playing " + GlobalClock.IsPlaying + ", beat " + F(Presenter.Beat));
                lastLocal = double.NaN; lastPos = cam.transform.position;
                continue;
            }
            lastLocal = b; lastPos = cp; prevDt2 = prevDt; prevDt = Time.unscaledDeltaTime; lastTgt = Tgt();
            if (Presenter.Clock == Presenter.ClockMode.Ended && Presenter.EndShown) break;
        }
        if (!double.IsNaN(firstStill)) Info("ENVIRONMENT: the audio clock stopped advancing at song beat " + F(firstStill) + " (dspTime frozen: the editor's audio)");
        Check("every column's islands are still under the sea 2 beats before it plays (their tops below the water)", () =>
        {
            for (int c = 0; c < nc; c++) { if (!sawAhead[c]) continue; if (aheadWorst[c] > -0.05f) return "column " + c + ": a top " + F(aheadWorst[c]) + " u above the sea"; }
            int seen = 0; foreach (var s in sawAhead) if (s) seen++;
            return seen >= nc - 1 ? "ok: " + seen + " columns sampled" : "only " + seen + " of " + nc + " columns sampled";
        });
        Check("every column's islands are up by its first beat (within the settle dip)", () =>
        {
            for (int c = 0; c < nc; c++) { if (!sawUp[c]) continue; if (upWorst[c] > 0.25f) return "column " + c + ": still " + F(upWorst[c]) + " u under"; }
            int seen = 0; foreach (var s in sawUp) if (s) seen++;
            return seen >= nc - 1 ? "ok: " + seen + " columns, worst drop " + F(Max(upWorst)) + " u" : "only " + seen + " of " + nc + " columns sampled";
        });
        Check("a column's floor rises under its islands, never through one", () => floorAbove == 0 ? "ok" : floorAbove + " frames: " + floorWhy);
        Check("the water answers each breach: spray, rings and the crest wave (pooled; it never ran out)", () =>
        {
            var w = Presenter.Water;
            if (!sawWater) return "no frame with spray + rings + a crest";
            if (w != null && w.Starved > 0) return "pools starved " + w.Starved + " times";
            return "ok: up to " + maxDrops + " drops, " + maxRings + " rings, " + maxCrests + " crests, " + maxSheets + " sheets at once";
        });
        Check("the camera falls steadily: lower at 75 % of the song than at 25 %, never rising faster than 4 u/s (a column handover breathes the fit a little), never jumping", () =>
        {
            if (double.IsNaN(y25) || double.IsNaN(y75)) return "not sampled";
            if (!(y75 < y25 - 3f)) return "y at 25 % " + F(y25) + ", at 75 % " + F(y75);
            if (maxRise > 4f) return "rose at " + F(maxRise) + " u/s";
            if (maxSpeed > 25f) return "a jump: " + F(maxSpeed) + " u/s over a " + (maxStepDt * 1000f).ToString("F0") + " ms frame at beat " + F(maxSpeedAt) + " (the beat moved " + maxSpeedDb.ToString("F3") + "): " + worstTgt;
            return "ok: y " + F(y25) + " → " + F(y75) + ", top speed " + F(maxSpeed) + " u/s (frame " + (maxStepDt * 1000f).ToString("F0") + " ms, beat " + F(maxSpeedAt) + "); " + stallFrames + " frames after an editor stall not counted";
        });
        Check("the sea is kept under the camera (centred on its focus every frame: a long song reaches past the sea's disc)", () => seaOff == 0 ? "ok" : seaOff + " frames off");
        Check("the fog follows the present camera's distance (start = max(30, distance − 10), end 95 later)", () => fogOff == 0 ? "ok: " + F(RenderSettings.fogStartDistance) + ".." + F(RenderSettings.fogEndDistance) : fogOff + " frames off");
        Check("the playing column stays in view (its pass platform's west / east edges and front / back lanes inside the frame)", () => viewSamples > 50 && outOfView == 0 ? "ok: " + viewSamples + " frames" : outOfView + " of " + viewSamples + " frames out: " + viewWhy);
        Check("KeyStage's keyboard is left to KeyStage while the song plays (no SeaDepth from the presentation once it settled)", () => sea == null || sea.StagedCount == 0 ? "no keyboard KeyStage drives in the song (KeyStage.Drives false)" : (stagedTouched == 0 && stagedSamples > 0 ? "ok: " + stagedSamples + " samples" : stagedTouched + " of " + stagedSamples + " samples held"));
        Check("no allocation per frame in the steady state (the presentation's LateUpdate)", () => Presenter.GcFramesMeasured > 30 && Presenter.GcFramesAllocating == 0 ? "ok: " + Presenter.GcFramesMeasured + " frames (" + Presenter.GcFramesExcused + " excused: an island's colliders flipping / a pool growing)" : Presenter.GcFramesAllocating + " of " + Presenter.GcFramesMeasured + " frames allocated (max " + Presenter.GcMaxBytesPerFrame + " B)");
        Info("frames " + Presenter.FrameCount + ", avg " + (Presenter.FrameCount > 0 ? (Presenter.FrameCount / Mathf.Max(1e-3f, Presenter.FrameSecondsSum)).ToString("F0") : "?") + " fps, worst " + (Presenter.WorstFrameSeconds * 1000f).ToString("F1") + " ms");

        // ---- the end (loop off)
        yield return Until(() => Presenter.EndShown, 8f);
        sea = Presenter.Sea;
        Check("the end (loop off): the clock stops, replay / exit show, every island, Moon and floor is up", () =>
        {
            if (Presenter.Clock != Presenter.ClockMode.Ended || !Presenter.EndShown) return "clock " + Presenter.Clock + ", end shown " + Presenter.EndShown;
            for (int i = 0; i < sea.Count; i++) { var r = sea[i]; float drop = r.kind == PresentSea.Kind.Floor ? SectionPlinth.SinkOf(r.col) : r.kb.SeaDrop; if (drop > 0.01f) return r.kind + " of column " + r.col + " still " + F(drop) + " under"; }
            return "ok";
        });
        yield return Shot("w9_end.png");
        yield return ScreenShot("w9_end_screen.png");

        // ---- replay: everything under again, the song from the top
        Presenter.Replay();
        yield return Until(() => Presenter.Clock == Presenter.ClockMode.Preroll && Presenter.Veil > 0.95f, 2f);
        yield return Frames(2);
        sea = Presenter.Sea;
        Check("replay: under the veil everything goes under the sea again and the song restarts from the pre-roll", () =>
        {
            if (Presenter.Clock != Presenter.ClockMode.Preroll || Presenter.SongBeat >= 0.0) return "clock " + Presenter.Clock + " at " + F(Presenter.SongBeat);
            for (int i = 0; i < sea.Count; i++) { var r = sea[i]; if (r.col == 0 && r.kind != PresentSea.Kind.Floor) continue; float top = TopNow(sea, i); if (top > PresentSea.SeaY - 0.5f) return r.kind + " of column " + r.col + " top " + F(top); }
            return "ok";
        });
        yield return Until(() => Presenter.Clock == Presenter.ClockMode.Live && Presenter.SongBeat > 0.5, 6f);
        Check("replay: the song plays again from the top", () => Presenter.Clock == Presenter.ClockMode.Live && GlobalClock.IsPlaying ? "ok: beat " + F(Presenter.SongBeat) : "clock " + Presenter.Clock);
        yield return Wait(1.2f);

        // ---- exit: everything back exactly (after the belts' and Moons' own glides to the restored playhead settle)
        Presenter.Exit();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
        yield return Wait(1.2f);
        Check("exit: the camera pose and its settings come back (OrbitCamera's saved view, mask, clear, background, FOV, clip planes), fog too", () =>
        {
            float dp = (cam.transform.position - p0).magnitude, dr = Quaternion.Angle(cam.transform.rotation, r0);
            if (dp > 1e-3f || dr > 0.06f) return "camera |dp| " + dp.ToString("E2") + ", angle " + dr.ToString("F3");
            if (cam.cullingMask != mask0 || cam.clearFlags != clear0 || cam.backgroundColor != bg0 || Mathf.Abs(cam.fieldOfView - fov0) > 1e-4f || Mathf.Abs(cam.nearClipPlane - near0) > 1e-5f || Mathf.Abs(cam.farClipPlane - far0) > 1e-3f) return "camera settings differ (fov " + cam.fieldOfView + " vs " + fov0 + ")";
            if (RenderSettings.fog != fog0 || Mathf.Abs(RenderSettings.fogStartDistance - fs0) > 1e-4f || Mathf.Abs(RenderSettings.fogEndDistance - fe0) > 1e-4f) return "fog differs";
            return "ok";
        });
        Check("exit: every island and Moon is back exactly where it was (position, rotation), nothing under the sea, its colliders on", () =>
        {
            foreach (var s in before)
            {
                if (s.kb == null) return "an island is gone";
                if ((s.kb.transform.position - s.pos).magnitude > 1e-3f) return s.kb.name + " at " + s.kb.transform.position.ToString("F3") + " (was " + s.pos.ToString("F3") + ")";
                if (Quaternion.Angle(s.kb.transform.rotation, s.rot) > 0.01f) return s.kb.name + " rotated";
                if (s.kb.SeaDrop > 1e-4f || s.kb.SeaDepth > 0f || s.kb.UnderSea) return s.kb.name + " still under (" + F(s.kb.SeaDrop) + ")";
                int en = 0; foreach (var c in s.kb.GetComponentsInChildren<Collider>(true)) if (c.enabled) en++;
                if (en != s.enabled) return s.kb.name + ": " + en + " of " + s.enabled + " colliders on";
            }
            return "ok: " + before.Count + " islands and Moons";
        });
        Check("exit: every section floor is back in its place (no sink left)", () =>
        {
            if (SectionPlinth.ColumnSinks != null) return "the sink hook is still set";
            var f1 = SnapFloors();
            if (f1.Count != floors0.Count) return "blocks " + floors0.Count + " → " + f1.Count;
            for (int i = 0; i < f1.Count; i++) if (Mathf.Abs(f1[i] - floors0[i]) > 1e-4f) return "block " + i + " at " + F(f1[i]) + " (was " + F(floors0[i]) + ")";
            return "ok: " + f1.Count + " blocks";
        });
        Check("exit: input, HUD and the orbit camera are given back; playback paused at the playhead it had before", () =>
        {
            if (WorldInput.IsLockedBy("present") || WorldInput.Describe() != locks0) return "locks " + WorldInput.Describe();
            if (!Mathf.Approximately(HudTarget(), hud0)) return "HUD " + HudTarget();
            if (oc != null && oc.Suspended) return "orbit camera still suspended";
            if (GlobalClock.IsPlaying) return "still playing";
            if (Math.Abs(GlobalClock.SongBeatD - beat0) > 1e-6) return "playhead " + F(GlobalClock.SongBeatD) + " (was " + F(beat0) + ")";
            if (GameObject.Find("Presentation") != null || GameObject.Find("PresentWater") != null) return "the water is still there";
            if ((Fx.SeaCentre - sea0).magnitude > 1e-3f) return "the sea plane is not back on the song (" + Fx.SeaCentre.ToString("F2") + " vs " + sea0.ToString("F2") + ")";
            return "ok";
        });
        yield return Wait(0.5f);
        yield return Shot("w9_after.png");
    }

    static float Max(float[] a) { float m = 0f; foreach (var v in a) m = Mathf.Max(m, v); return m; }
    static string Tgt() { var t = Presenter.Rig != null ? Presenter.Rig.Target : new PresentRig.Pose(); return "target " + t.position.ToString("F2") + " focus " + t.focus.ToString("F2") + " alt " + F(t.altitude) + " dist " + F(t.distance) + " pitch " + F(t.pitch) + " yaw " + F(t.yaw) + " fit " + F(t.fitX) + "/" + F(t.fitZ) + " span x " + F(t.spanW) + ".." + F(t.spanE) + " z " + F(t.spanF) + ".." + F(t.spanK) + " beat " + F(Presenter.SongBeat); }

    /// <summary>v9 (S17): the playing pass's whole platform in view: its west and east edges (the pass's slot on a belt) and its front and back lanes,
    /// each inside the frame with a 2 % margin. Null when they are; else which point is out and where.</summary>
    static string ColumnInView(SongManager sm, int col, double beat, Camera cam)
    {
        var b = sm.ColumnBounds(col);
        int pass = sm.PassAt(col, (float)beat);
        float slide = sm.BeltExtent(col) > 0f ? pass * (sm.ColumnWidth(col) + ProjectConfig.BeltGap) : 0f;
        float west = sm.ColumnWestEdge(col) + slide, east = west + sm.ColumnWidth(col), xc = (west + east) * 0.5f, y = b.center.y;
        Vector3[] pts = { new Vector3(west, y, b.center.z), new Vector3(east, y, b.center.z), new Vector3(xc, y, b.min.z), new Vector3(xc, y, b.max.z) };
        string[] names = { "west edge", "east edge", "front lane", "back lane" };
        for (int i = 0; i < 4; i++)
        {
            var vp = cam.WorldToViewportPoint(pts[i]);
            if (!(vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f)) return "column " + col + " pass " + pass + " " + names[i] + " at viewport " + vp.ToString("F2") + " (platform x " + F(west) + ".." + F(east) + ", z " + F(b.min.z) + ".." + F(b.max.z) + ", camera focus " + Presenter.Rig.Target.focus.ToString("F1") + ", distance " + F(Presenter.Rig.Target.distance) + ")";
        }
        return null;
    }

    // ------------------------------------------------------------------ part fog: the fog follows the camera (play, editing, the menu)
    static string FogNow() => F(RenderSettings.fogStartDistance) + ".." + F(RenderSettings.fogEndDistance) + (RenderSettings.fog ? "" : " (off)");
    static string FogMatches(float dist)
    {
        float want = Fx.FogStartFor(dist);
        if (!RenderSettings.fog) return "fog off";
        if (Mathf.Abs(RenderSettings.fogStartDistance - want) > 0.6f || Mathf.Abs(RenderSettings.fogEndDistance - (want + Fx.FogSpan)) > 0.6f) return "camera " + F(dist) + " u, fog " + FogNow() + " (want " + F(want) + ".." + F(want + Fx.FogSpan) + ")";
        return null;
    }

    static IEnumerator PartFog()
    {
        Info("part fog: the fog follows the camera's distance (S17 / G9)");
        Prepare();
        var oc = OrbitCamera.I;
        var st = Gallery.LoadState("get-proto");
        if (st == null || oc == null) { Info("SKIP: no get-proto / no orbit camera"); yield break; }
        yield return Load(st);
        var sm = SM;
        int sC = -1; for (int s = 0; s < SectionPlinth.Count; s++) if (SectionPlinth.LetterOf(s) == "C") { sC = s; break; }
        double startC = sC >= 0 ? sm.SectionStartBeat(sC) : sm.TotalBeats * 0.4;
        if (!oc.followPlayhead) oc.ToggleFollow();
        GlobalClock.Seek(startC); GlobalClock.Play();
        yield return Wait(3.5f);
        float dProto = oc.Dist;
        Check("fog: get proto's section C in play, the follow camera far out: the fog starts at its distance − 10 and ends 95 later (no pink haze)", () =>
        {
            string why = FogMatches(oc.Dist);
            if (why != null) return why;
            return oc.Dist > 45f ? "ok: section " + (sC >= 0 ? "C" : "?") + " at beat " + F(startC) + ", camera " + F(oc.Dist) + " u, fog " + FogNow() : "the camera is only " + F(oc.Dist) + " u out (fog " + FogNow() + ")";
        });
        yield return Shot("w9_fog_proto_C.png");
        GlobalClock.Pause();
        // the menu owns the fog while its stage shows (off), and gives it back
        MainMenu.Show();
        yield return Until(() => MainMenu.Stage != null && MainMenu.Stage.Active && !RenderSettings.fog, 6f);
        bool offInMenu = !RenderSettings.fog;
        MainMenu.Hide(); Prepare();
        yield return Wait(1f);
        Check("fog: the menu switches it off for its stage and the world gets it back, following the camera again", () =>
        {
            if (!offInMenu) return "the fog stayed on under the menu's stage";
            string why = FogMatches(oc.Dist);
            return why == null ? "ok: back at " + FogNow() + " for a camera " + F(oc.Dist) + " u out" : why;
        });
        // a normal song framed at the usual distance: exactly as before
        st = Gallery.LoadState("crush");
        if (st != null)
        {
            yield return Load(st);
            oc.FrameBounds(sm.ColumnBounds(0), 0.1f, true, 1f);   // a fresh view at the song's usual distance (get proto left it far out)
            yield return Frames(2);
            GlobalClock.Seek(sm.ColumnStart(Mathf.Min(2, sm.ColumnCount - 1))); GlobalClock.Play();
            yield return Wait(3f);
            Check("fog: a normal gallery song (crush) in play keeps (about) the default fog (" + F(Look.FogStart) + ".." + F(Look.FogEnd) + "; exactly it up to a " + F(Look.FogStart + Fx.FogLead) + " u camera)", () =>
            {
                string why = FogMatches(oc.Dist);
                if (why != null) return why;
                return Mathf.Abs(RenderSettings.fogStartDistance - Look.FogStart) < 5f ? "ok: camera " + F(oc.Dist) + " u, fog " + FogNow() : "camera " + F(oc.Dist) + " u: fog " + FogNow() + " (more than 5 u off the default)";
            });
            yield return Shot("w9_fog_crush.png");
            GlobalClock.Pause();
        }
        if (oc.followPlayhead) oc.ToggleFollow();
        GlobalClock.Stop();
        Info("get proto's follow camera was " + F(dProto) + " u out");
    }

    // ------------------------------------------------------------------ part B: the same song looping
    static IEnumerator PartB()
    {
        Info("part B: the test song looping");
        Prepare();
        yield return Load(TestSong(true));
        var sm = SM;
        var before = SnapIslands();
        Presenter.Enter();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Presenting, 3f);
        var sea = Presenter.Sea;
        if (sea == null) { Line(false, "loop: presenting", "no sea"); yield break; }
        double total = sea.TotalBeats;
        float t0 = Time.realtimeSinceStartup;
        float veilMax = 0f; bool sawWrap = false, wrapOk = false; string wrapWhy = "never reached the loop point";
        double lastB = Presenter.Beat;
        while (Time.realtimeSinceStartup - t0 < (float)(total / GlobalClock.BeatsPerSecond) + 10f)
        {
            yield return null;
            double B = Presenter.Beat;
            if (B > total - 1.0 && B < total + 1.5) veilMax = Mathf.Max(veilMax, Presenter.Veil);
            if (!sawWrap && B >= total + 0.15 && lastB < total + 0.15)
            {
                sawWrap = true;
                double b = Presenter.SongBeat;
                wrapOk = true; wrapWhy = null;
                for (int i = 0; i < sea.Count; i++)
                {
                    var r = sea[i];
                    if (r.col == 0) { if (r.kind != PresentSea.Kind.Floor && r.kb.SeaDrop > 0.25f) { wrapOk = false; wrapWhy = "column 0 not up after the loop (" + F(r.kb.SeaDrop) + ")"; break; } continue; }
                    if (r.downbeat - 2.5 > b && TopNow(sea, i) > PresentSea.SeaY - 0.3f) { wrapOk = false; wrapWhy = r.kind + " of column " + r.col + " not under again after the loop"; break; }
                }
                if (wrapOk && Presenter.Rig.Target.progress > 0.05f) { wrapOk = false; wrapWhy = "the camera did not start over (progress " + F(Presenter.Rig.Target.progress) + ")"; }
                if (wrapOk) wrapWhy = "ok: song beat " + F(b) + " after the loop";
                if (shots) yield return Shot("w9_loop.png");
            }
            lastB = B;
            if (sawWrap && Presenter.Beat > total + 3.0) break;
        }
        Check("loop: the veil dips over the loop point", () => veilMax > 0.6f ? "ok: veil up to " + F(veilMax) : "veil max " + F(veilMax));
        Check("loop: after the loop point the song starts over: later columns under again, the first column up, the camera back at the start", () => wrapOk ? wrapWhy : wrapWhy);
        Presenter.Exit();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
        yield return Wait(1.2f);
        Check("loop: exit puts every island back exactly", () =>
        {
            foreach (var s in before) { if (s.kb == null) return "an island is gone"; if (s.kb.SeaDrop > 1e-4f || (s.kb.transform.position - s.pos).magnitude > 0.02f) return s.kb.name + " at " + s.kb.transform.position.ToString("F3") + " (was " + s.pos.ToString("F3") + ")"; }
            return "ok";
        });
    }

    // ------------------------------------------------------------------ part C / D: gallery songs
    static IEnumerator PartC(string id, string tag)
    {
        Info("part " + tag + ": the gallery song \"" + id + "\"");
        Prepare();
        var st = Gallery.LoadState(id);
        if (st == null) { Info("SKIP: gallery song " + id + " not found"); yield break; }
        st.loop = false;
        yield return Load(st);
        var sm = SM; var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.ColumnBounds(0), 0.1f, true, 1f);
        yield return Frames(3);
        var before = SnapIslands();
        Vector3 p0 = cam.transform.position;
        Info(id + ": " + sm.Islands.Count + " islands, " + sm.Moons.Count + " Moons, " + sm.ColumnCount + " columns, " + F(sm.TotalBeats) + " beats at " + GlobalClock.BPM + " bpm");
        Presenter.Enter();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Presenting, 3f);
        yield return Frames(2);
        var sea = Presenter.Sea;
        if (sea == null) { Line(false, id + ": presenting", "no sea"); yield break; }
        Check(id + ": on enter every island, Moon and floor is under the sea (KeyStage's keyboards held at its own pose)", () =>
        {
            int n = 0;
            for (int i = 0; i < sea.Count; i++) { var r = sea[i]; if (r.kind != PresentSea.Kind.Floor && Presenter.SongBeat >= r.start) continue; if (TopNow(sea, i) > PresentSea.SeaY - 0.5f) return r.kind + " of column " + r.col + " top " + F(TopNow(sea, i)); n++; }
            int held = 0;
            for (int i = 0; i < sea.StagedCount; i++) { var s = sea.StagedAt(i); if (s.kb == null) continue; float want = sea.StagedDepthAt(s.kb, Presenter.SongBeat); if (Mathf.Abs(s.kb.SeaDepth - want) > 1e-4f) return "KeyStage keyboard " + s.kb.name + " at " + F(s.kb.SeaDepth) + ", KeyStage's pose " + F(want); held++; }
            return "ok: " + n + " under, " + held + " keyboards KeyStage drives held at its pose";
        });
        yield return Wait(0.5f);
        yield return Shot(tag + "_start.png");
        // the first breaches
        double firstDown = sm.ColumnCount > 1 ? sm.ColumnStart(1) : sm.ColumnStart(0);
        yield return Until(() => Presenter.SongBeat >= firstDown - 1.0 + 0.1 * GlobalClock.BeatsPerSecond, 20f);
        yield return Shot(tag + "_breach.png");
        yield return Until(() => Presenter.SongBeat >= firstDown + 0.3, 6f);
        Check(id + ": at its downbeat the new column's whole pass platform is already in view (edges and lanes)", () =>
        {
            double b = Presenter.SongBeat; int col = sm.ActiveColumn((float)Math.Max(0.0, b));
            string vw = ColumnInView(sm, col, b, cam);
            return vw == null ? "ok: column " + col + " (" + F(sm.ColumnWidth(col)) + " u wide, " + F(sm.ColumnBounds(col).size.z) + " u deep) at beat " + F(b) + ", camera " + F(Presenter.Rig.Target.distance) + " u out" : vw;
        });
        double b0 = Presenter.SongBeat;
        Check(id + ": columns rise as the song reaches them (the first columns up, the later ones under)", () =>
        {
            float under = 0f; int upCols = 0;
            for (int c = 0; c < sm.ColumnCount; c++)
            {
                double s = sm.ColumnStart(c);
                if (s <= b0 - 0.1) { if (sea.ColumnDepth(c, b0) > 0.02f) return "column " + c + " (starts " + F(s) + ") not up at " + F(b0); upCols++; }
                else if (s > b0 + 2.5) under = Mathf.Max(under, 1f - sea.ColumnMinDepth(c, b0));
            }
            return under < 1e-3f ? "ok: " + upCols + " columns up at beat " + F(b0) + ", every later one under" : "a later column partly up (" + F(under) + ")";
        });
        yield return Wait(1.5f);
        Check(id + ": the playing column is in view (its pass platform's edges and its front / back lanes)", () =>
        {
            double b = Presenter.SongBeat; int col = sm.ActiveColumn((float)Math.Max(0.0, b));
            string vw = ColumnInView(sm, col, b, cam);
            return vw == null ? "ok: column " + col + " (" + sm.ColumnSize(col) + " islands, " + F(sm.ColumnWidth(col)) + " u wide) at beat " + F(b) + ", camera " + F(Presenter.Rig.Target.distance) + " u out" : vw;
        });
        // seek to the middle and near the end (paused)
        Presenter.SetPaused(true);
        double total = sea.TotalBeats;
        Presenter.ScrubBeat = total * 0.5; yield return Frames(3);
        yield return Shot(tag + "_mid.png");
        Vector3 midPos = cam.transform.position; var midT = Presenter.Rig.Target;
        Presenter.ScrubBeat = total * 0.93; yield return Frames(3);
        yield return Shot(tag + "_late.png");
        Vector3 latePos = cam.transform.position;
        var lateT = Presenter.Rig.Target;
        Check(id + ": the camera is lower late in the song than in its middle (falling toward the sea)", () => (latePos.y < midPos.y - 1f ? "ok: y " : "y ") + F(midPos.y) + " → " + F(latePos.y) + " (mid: distance " + F(midT.distance) + ", fit across " + F(midT.fitX) + " / deep " + F(midT.fitZ) + ", span x " + F(midT.spanW) + ".." + F(midT.spanE) + " z " + F(midT.spanF) + ".." + F(midT.spanK) + "; late: distance " + F(lateT.distance) + ", fit " + F(lateT.fitX) + " / " + F(lateT.fitZ) + ", span x " + F(lateT.spanW) + ".." + F(lateT.spanE) + " z " + F(lateT.spanF) + ".." + F(lateT.spanK) + ")");
        Check(id + ": late in the song every earlier column is up", () =>
        {
            double b = Presenter.SongBeat;
            for (int i = 0; i < sea.Count; i++) { var r = sea[i]; if (r.downbeat > b - 0.5) continue; float drop = r.kind == PresentSea.Kind.Floor ? SectionPlinth.SinkOf(r.col) : r.kb.SeaDrop; if (drop > 0.2f) return r.kind + " of column " + r.col + " still " + F(drop) + " under at " + F(b); }
            return "ok";
        });
        Presenter.ScrubBeat = double.NaN;
        Presenter.Exit();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
        yield return Wait(1.2f);
        Check(id + ": exit puts every island and Moon back exactly (position, colliders) and the camera where it was", () =>
        {
            foreach (var s in before)
            {
                if (s.kb == null) return "an island is gone";
                if ((s.kb.transform.position - s.pos).magnitude > 1e-3f || s.kb.SeaDrop > 1e-4f) return s.kb.name + " at " + s.kb.transform.position.ToString("F3") + " (was " + s.pos.ToString("F3") + ")";
                int en = 0; foreach (var c in s.kb.GetComponentsInChildren<Collider>(true)) if (c.enabled) en++;
                if (en != s.enabled) return s.kb.name + ": " + en + " of " + s.enabled + " colliders";
            }
            if (SectionPlinth.ColumnSinks != null) return "floor sinks left";
            if ((cam.transform.position - p0).magnitude > 1e-3f) return "camera off by " + (cam.transform.position - p0).magnitude.ToString("E2");
            return "ok: " + before.Count + " islands and Moons";
        });
    }
}
