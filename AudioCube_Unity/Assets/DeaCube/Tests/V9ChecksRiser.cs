using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v9 (R) Play-mode verification of the riser, the crash, the section changes, the echo afterimages and the stairs FX: <c>V9ChecksRiser.RunAll()</c>
/// starts a coroutine; poll <see cref="Done"/> or Captures/riser9_report.txt (PASS/FAIL lines, INFO lines, a SUMMARY). On the gallery song
/// "get-proto" (launches landing on bars 1, 25, 57): the beam exists only during a launch's swell and grows with it (captures at 25 / 50 / 90 %),
/// its rings accelerate, the camera breathes; the crash fires once on the landing beat (the shockwave, the lane bounce, the letter kick, the HUD
/// cursor kick; a capture); section downbeats pulse (counted per section; a plain one captured); the same in the present mode (scrubbed captures
/// of the swell and the crash, a live crash); nothing is left after a stop and a replay; zero allocations per frame in steady state. On the v1
/// fixture: a cube with echo 2 leaves afterimages that are gone after; a get-proto stairs runner lights steps, leaves a trail and (a lead-in) flashes
/// the next grid. 0 console errors; the user's save untouched.
/// </summary>
public static class V9ChecksRiser
{
    public static string Report = "";
    public static bool Done = true;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "riser9_report.txt");
    public const string SongId = "get-proto";
    static StringBuilder sb;
    static int num, pass, fail;
    static bool shots;
    static readonly List<string> errors = new List<string>();

    static SongManager SM => SongManager.I;
    static WorldMagic WM => WorldMagic.I;
    static string F(double v) => v.ToString("F2");

    static void Check(string name, Func<string> probe)
    {
        string r;
        try { r = probe(); } catch (Exception e) { r = "exception " + e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e); }
        bool ok = r != null && r.StartsWith("ok");
        Line(ok, name, r ?? "null");
    }
    static void Line(bool ok, string name, string detail)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" R9-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(); }
    static void Flush() { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static string FirstFrame(Exception e) { var s = (e.StackTrace ?? "").Split('\n'); return s.Length > 0 ? s[0].Trim() : ""; }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg + " @ " + (stack ?? "").Split('\n')[0]);
    }

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
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        Application.runInBackground = true;
        Presenter.ScrubBeat = double.NaN;
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Until(Func<bool> c, float timeout) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < timeout) { bool ok = false; try { ok = c(); } catch (Exception) { } if (ok) yield break; yield return null; } }

    static IEnumerator Shot(string file)
    {
        if (!shots) yield break;
        yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return Frames(3);
        Info("capture " + file + " at beat " + F(WorldMagic.FxBeat));
    }
    static IEnumerator PresentShot(string file)
    {
        if (!shots) yield break;
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        int n0 = Presenter.CapturesDone;
        Presenter.RequestCapture(p, 1600, 900);
        yield return Until(() => Presenter.CapturesDone > n0, 3f);
        Info("present capture " + file + " at beat " + F(Presenter.SongBeat));
    }
    /// <summary>Pauses the clock, captures, plays on.</summary>
    static IEnumerator PausedShot(string file)
    {
        GlobalClock.Pause();
        yield return Frames(2);
        yield return Shot(file);
        GlobalClock.Play();
    }

    static IEnumerator Load(SongState st)
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        GridSelection.Clear();
        if (PathManager.I != null) PathManager.I.PutDown();
        SongState.Apply(st);
        History.Reset(); History.Push();
        yield return Frames(4);
        SectionPlinth.RefreshNow();
        yield return Wait(0.8f);
    }
    static IEnumerator LoadFixture() { yield return Load(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); }

    static IEnumerator SeekPlay(double beat)
    {
        GlobalClock.Stop();
        yield return null;
        GlobalClock.Seek(Math.Max(0.0, beat));
        GlobalClock.Play();
        yield return null;
    }

    static void Frame(params KeyBlock[] kbs)
    {
        var oc = OrbitCamera.I; if (oc == null) return;
        var b = new Bounds(); bool first = true;
        foreach (var kb in kbs) { if (kb == null) continue; var w = kb.VisualBounds; if (first) { b = w; first = false; } else b.Encapsulate(w); }
        if (first) return;
        b.Encapsulate(b.center + Vector3.up * 9f);
        oc.FrameBounds(b, 0.1f, true, 0.9f);
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

    // ================================================================== the run
    static IEnumerator AllRoutine()
    {
        sb = new StringBuilder();
        sb.Append("V9ChecksRiser ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var snap = V3Fixes.SnapshotSaves();
        string userMd5 = SaveHash();
        Prepare();
        bool loop0 = GlobalClock.LoopSong;
        IEnumerator[] parts = { PartLaunch(), PartPresent(), PartEcho(), PartStairs(), PartPerf() };
        foreach (var part in parts)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(part);
            while (stack.Count > 0)
            {
                var top = stack.Peek(); object cur = null; bool more;
                try { more = top.MoveNext(); if (more) cur = top.Current; }
                catch (Exception e) { Line(false, "exception", e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); break; }
                if (!more) { stack.Pop(); continue; }
                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return cur;
            }
            try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
            yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
            Presenter.ScrubBeat = double.NaN;
            GlobalClock.Stop();
        }
        Application.logMessageReceived -= OnLog;
        Check("no console errors or exceptions during the whole run", () => errors.Count == 0 ? "ok" : errors.Count + ": " + string.Join(" | ", errors.ToArray()));
        GlobalClock.Stop();
        GlobalClock.LoopSong = loop0;
        V3Fixes.RestoreSaves(snap);
        Check("the user's save is untouched", () => SaveHash() == userMd5 ? "ok: " + (userMd5 ?? "absent") : "md5 " + SaveHash() + " (was " + userMd5 + ")");
        Done = true;
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Flush();
    }

    // ------------------------------------------------------------------ the launch picked on get-proto
    static KeyBlock launchKb, launchTarget; static LaunchFx launchFx; static double L, S, SB;

    static bool PickLaunch()
    {
        launchKb = null; launchFx = null; launchTarget = null;
        float bestE = -1f; double bestL = 0;
        foreach (var kb in SM.Islands)
        {
            if (kb == null || !kb.launch || kb.IsMoon) continue;
            var fx = WM.LaunchOf(kb); var t = SM.LaunchTarget(kb);
            if (fx == null || t == null) continue;
            double le = SM.TurnEnd(kb);
            if (le >= SM.TotalBeats - 1e-3) continue;            // the wrap landing: a plain one reads better
            float score = kb.energy * 1000f - (float)le;          // the most energetic, earliest
            if (score > bestE) { bestE = score; launchKb = kb; launchFx = fx; launchTarget = t; bestL = le; }
        }
        if (launchKb == null) return false;
        L = bestL; SB = LaunchFx.SwellBeats; S = L - SB;
        return true;
    }

    static IEnumerator PartLaunch()
    {
        Info("part A: the riser and the crash in play mode (" + SongId + ")");
        Prepare();
        var st = Gallery.LoadState(SongId);
        if (st == null) { Line(false, "gallery song " + SongId, "not found"); yield break; }
        st.loop = true;
        yield return Load(st);
        GlobalClock.LoopSong = true;
        if (!PickLaunch()) { Line(false, "a launching island with a target on " + SongId, "none"); yield break; }
        Info(SongId + ": " + SM.Islands.Count + " islands, " + SM.ColumnCount + " columns, " + SM.SectionCount + " sections, " + F(SM.TotalBeats) + " beats at " + GlobalClock.BPM + " bpm; launch '" + launchKb.assignedChord + "' (energy " + launchKb.energy + ", big " + F(launchFx.Big) + ") lands at beat " + F(L) + " on '" + launchTarget.assignedChord + "' (column " + launchTarget.column + "), swell " + F(SB) + " beats from " + F(S));
        Frame(launchKb, launchTarget);
        yield return Wait(0.4f);

        // ---- the beam is a pure function of the swell: seek (paused) to 25 / 50 / 90 %
        GlobalClock.Seek(S - 1.0); yield return Frames(3);
        float hBefore = launchFx.BeamHeight; int ringsBefore = launchFx.RingsShown; float brBefore = LaunchFx.BreathNow;
        float fovBase = Camera.main != null ? Camera.main.fieldOfView : 0f;
        GlobalClock.Seek(S + 0.25 * SB); yield return Frames(3);
        float h25 = launchFx.BeamHeight, v25 = launchFx.RingSpeed, br25 = LaunchFx.BreathNow, cam25 = OrbitCamera.I != null ? OrbitCamera.I.BreathNow : 0f;
        yield return Shot("riser9_swell25.png");
        GlobalClock.Seek(S + 0.5 * SB); yield return Frames(3);
        float h50 = launchFx.BeamHeight, v50 = launchFx.RingSpeed, glow50 = launchFx.WaterGlow; int rings50 = launchFx.RingsShown;
        yield return Shot("riser9_swell50.png");
        GlobalClock.Seek(S + 0.9 * SB); yield return Frames(3);
        float h90 = launchFx.BeamHeight, v90 = launchFx.RingSpeed, br90 = LaunchFx.BreathNow, cam90 = OrbitCamera.I != null ? OrbitCamera.I.BreathNow : 0f;
        float lift90 = launchKb.FxLift, lean90 = Quaternion.Angle(launchKb.transform.rotation, Quaternion.identity);
        float fov90 = Camera.main != null ? Camera.main.fieldOfView : 0f;
        Vector3 camPos90 = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        yield return Shot("riser9_swell90.png");
        GlobalClock.Seek(L + 2.5); yield return Frames(3);
        float hAfter = launchFx.BeamHeight;
        float liftAfter = launchKb.FxLift, leanAfter = Quaternion.Angle(launchKb.transform.rotation, Quaternion.identity);
        Check("the beam exists only during the swell and grows with it (seeks land exactly)", () =>
        {
            if (hBefore > 0.01f || ringsBefore > 0) return "a beam before the swell: height " + F(hBefore) + ", rings " + ringsBefore;
            if (hAfter > 0.01f) return "a beam after the landing: " + F(hAfter);
            if (!(h25 > 1f && h50 > h25 && h90 > h50)) return "heights 25/50/90 % " + F(h25) + " / " + F(h50) + " / " + F(h90);
            if (h90 < 12f * launchFx.Big) return "the beam at 90 % is only " + F(h90) + " u tall (big " + F(launchFx.Big) + ")";
            if (rings50 < 2) return "rings at 50 %: " + rings50;
            if (glow50 <= 0.05f) return "the water glow at 50 %: " + F(glow50);
            return "ok: heights " + F(h25) + " / " + F(h50) + " / " + F(h90) + " u, rings at 50 % " + rings50 + ", water glow " + F(glow50);
        });
        Check("the launching grid tenses through the swell (a slight lift and a lean back growing with it) and is upright and unlifted after the landing", () =>
        {
            if (!(lift90 > 0.03f && lift90 <= 0.2f)) return "lift at 90 % " + F(lift90);
            if (!(lean90 > 1f && lean90 <= 4.5f)) return "lean at 90 % " + F(lean90) + "°";
            if (Mathf.Abs(liftAfter) > 1e-4f || leanAfter > 0.01f) return "after the landing: lift " + F(liftAfter) + ", lean " + F(leanAfter) + "°";
            return "ok: at 90 % lift " + F(lift90) + " u, lean " + F(lean90) + "°; after: " + F(liftAfter) + " / " + F(leanAfter) + "°";
        });
                Check("the rings climb faster and faster toward the top", () => v25 > 0f && v50 > v25 && v90 > v50 ? "ok: cycles/beat " + F(v25) + " → " + F(v50) + " → " + F(v90) : "ring speeds " + F(v25) + " / " + F(v50) + " / " + F(v90));
        Check("the camera breathes through the swell (a lift and a wider field of view) and not outside it", () =>
        {
            if (brBefore > 1e-3f) return "breath before the swell " + F(brBefore);
            if (!(br25 >= 0f && br90 > br25 && br90 > 0.3f * launchFx.Big)) return "breath 25 / 90 % " + F(br25) + " / " + F(br90);
            if (cam90 < 0.3f * launchFx.Big) return "OrbitCamera.BreathNow at 90 % " + F(cam90);
            if (fovBase > 0f && !(fov90 > fovBase + 0.5f)) return "fov " + F(fovBase) + " → " + F(fov90);
            return "ok: breath " + F(br25) + " → " + F(br90) + ", fov " + F(fovBase) + " → " + F(fov90) + ", camera y " + F(camPos90.y);
        });

        // ---- the crash: play through the landing
        int cr0 = launchFx.Crashes, sw0 = launchFx.Shockwaves, kicks0 = HudColumnRail.Kicks, pulses0 = WM.Sections.Pulses, cs0 = launchFx.CrashSparkles, drops0 = launchFx.SprayDrops;
        int targetSection = SM.SectionOf(launchTarget.column);
        int secPulses0 = WM.Sections.PulsesOf(targetSection);
        float maxShock = 0f, maxLane = 0f, maxLetter = 0f, maxFlash = 0f, beamAtCrash = -1f, breathAtL = -1f, breathAfter = 9f, maxBeamInSwell = 0f;
        int allocFrames0 = -1, allocMeasured0 = -1, allocExcused0 = 0, wmFrames0 = 0, wmMeasured0 = 0; long allocMax0 = 0;
        bool crashShot = false, shockShot = false; double shotBeat = 0;
        int sparksBefore = launchFx.Sparks;
        yield return SeekPlay(S - 0.5);
        float t0 = Time.realtimeSinceStartup;
        double lastBeat = -1;
        while (Time.realtimeSinceStartup - t0 < (float)((SB + 3.5) / GlobalClock.BeatsPerSecond) + 6f)
        {
            yield return null;
            double b = GlobalClock.SongBeatD;
            if (b < lastBeat - 1.0) break;   // the song wrapped
            lastBeat = b;
            if (b >= S + 0.25 * SB && b < L - 0.05)
            {
                if (allocFrames0 < 0) { allocFrames0 = WM.FxAllocFrames; allocMeasured0 = WM.FxAllocMeasured; allocExcused0 = WM.FxAllocExcused; allocMax0 = WM.FxAllocMax; wmFrames0 = WM.AllocFrames; wmMeasured0 = WM.AllocMeasured; }
                maxBeamInSwell = Mathf.Max(maxBeamInSwell, launchFx.BeamHeight);
            }
            if (b >= L && b < L + 1.5)
            {
                maxShock = Mathf.Max(maxShock, launchFx.ShockRadius);
                maxLane = Mathf.Max(maxLane, launchFx.LaneLift);
                maxLetter = Mathf.Max(maxLetter, SectionPlinth.PulseOf(targetSection));
                maxFlash = Mathf.Max(maxFlash, launchFx.Flash);
                if (beamAtCrash < 0f && b >= L + 0.1) { beamAtCrash = launchFx.BeamHeight; breathAtL = LaunchFx.BreathNow; }
            }
            if (b >= L + 1.4 && b < L + 2.0) breathAfter = Mathf.Min(breathAfter, LaunchFx.BreathNow);
            if (!crashShot && b >= L + 0.25) { crashShot = true; shotBeat = b; yield return PausedShot("riser9_crash.png"); }
            if (!shockShot && b >= L + 0.6) { shockShot = true; yield return PausedShot("riser9_crash_wave.png"); }
            if (b >= L + 2.2) break;
        }
        int allocFrames = allocFrames0 >= 0 ? WM.FxAllocFrames - allocFrames0 : -1, allocMeasured = allocMeasured0 >= 0 ? WM.FxAllocMeasured - allocMeasured0 : -1;
        int wmFrames = WM.AllocFrames - wmFrames0, wmMeasured = WM.AllocMeasured - wmMeasured0;
        Check("the crash fires once on the landing beat: the flash, the shockwave over the sea, the lanes bouncing, the letter and the HUD cursor kicking, spray", () =>
        {
            int cr = launchFx.Crashes - cr0;
            if (cr != 1) return "crashes " + cr + " (heard " + launchFx.Heard + ", armed " + launchFx.Armed + ", beat now " + F(GlobalClock.SongBeatD) + ", swell " + F(launchFx.Swell) + ")";
            if (launchFx.Shockwaves - sw0 != 1) return "shockwaves " + (launchFx.Shockwaves - sw0);
            if (maxShock < 8f) return "shockwave radius reached " + F(maxShock);
            if (maxFlash <= 0.2f) return "flash " + F(maxFlash);
            if (maxLane < 0.05f) return "lane bounce " + F(maxLane);
            if (HudColumnRail.Kicks - kicks0 < 1) return "HUD cursor kicks " + (HudColumnRail.Kicks - kicks0);
            if (maxLetter <= 0.05f) return "letter pulse of section " + targetSection + " " + F(maxLetter);
            if (launchFx.SprayDrops - drops0 < 10) return "spray drops " + (launchFx.SprayDrops - drops0);
            return "ok: shockwave to " + F(maxShock) + " u, flash " + F(maxFlash) + ", lane bounce " + F(maxLane) + " u, letter pulse " + F(maxLetter) + ", HUD kicks " + (HudColumnRail.Kicks - kicks0) + ", sparkles " + (launchFx.CrashSparkles - cs0) + ", spray " + (launchFx.SprayDrops - drops0) + ", sparks through the swell " + (launchFx.Sparks - sparksBefore) + ", crash shot at " + F(shotBeat);
        });
        Check("the landing is also the section's downbeat: its pulse counted once; the beam is gone and the breath settles within " + F(LaunchFx.SettleBeats) + " beats", () =>
        {
            int sp = WM.Sections.PulsesOf(targetSection) - secPulses0;
            if (sp != 1) return "section " + targetSection + " pulses " + sp + " (all " + (WM.Sections.Pulses - pulses0) + ")";
            if (beamAtCrash > 0.01f) return "the beam still " + F(beamAtCrash) + " u at the landing";
            if (breathAfter > 0.02f) return "breath " + F(breathAfter) + " after the settle";
            return "ok: section " + targetSection + " pulsed once, beam at the landing " + F(beamAtCrash) + ", breath at L " + F(breathAtL) + " → " + F(breathAfter) + " after";
        });
        Check("zero allocations per frame in the v9 effects' steady state through the swell", () =>
        {
            if (allocMeasured < 0) return "no frames measured";
            if (WM.FxAllocLast < 0 && allocMeasured == 0) return "ok: allocation probe unavailable on this runtime";
            string wm = "; all of WorldMagic: " + wmFrames + " of " + wmMeasured + " frames allocated (max " + WM.AllocMax + " B)";
            if (allocFrames > 0) return allocFrames + " of " + allocMeasured + " frames allocated (max " + WM.FxAllocMax + " B; " + (WM.FxAllocExcused - allocExcused0) + " excused for pool growth)" + wm;
            return "ok: 0 of " + allocMeasured + " frames allocated (" + (WM.FxAllocExcused - allocExcused0) + " excused for pool growth), beam up to " + F(maxBeamInSwell) + " u" + wm;
        });

        // ---- a plain section change (no launch landing there)
        int plain = -1;
        for (int s = 1; s < SM.SectionCount; s++)
        {
            bool landing = false;
            foreach (var kb in SM.Islands) if (kb != null && kb.launch && !kb.IsMoon && Math.Abs(SM.TurnEnd(kb) - SM.SectionStartBeat(s)) < 0.5) landing = true;
            if (!landing) { plain = s; break; }
        }
        if (plain < 0) Line(false, "a plain section change", "every section start is a launch landing");
        else
        {
            double ps = SM.SectionStartBeat(plain);
            var anchor = SM.AnchorOf(SM.SectionFirst(plain));
            int p0 = WM.Sections.PulsesOf(plain), ll0 = WM.Sections.LaneLights;
            float maxRing = 0f, maxLetterP = 0f; int maxRings = 0; bool shot = false;
            Frame(anchor);
            yield return SeekPlay(ps - 1.0);
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f && GlobalClock.SongBeatD < ps + 2.2)
            {
                yield return null;
                double b = GlobalClock.SongBeatD;
                if (b >= ps && b < ps + 2.0)
                {
                    maxRings = Mathf.Max(maxRings, WM.Sections.ActiveRings);
                    maxLetterP = Mathf.Max(maxLetterP, SectionPlinth.PulseOf(plain));
                    foreach (var kb in SM.Islands) if (kb != null && kb.column == SM.SectionFirst(plain)) maxRing = Mathf.Max(maxRing, kb.FxLift);
                }
                if (!shot && b >= ps + 0.3) { shot = true; yield return PausedShot("riser9_section.png"); }
            }
            Check("a plain section change: the floor ripple, the lanes lit front to back, a bounce, the letter pulsing (energy " + (anchor != null ? anchor.energy : -1) + ")", () =>
            {
                int p = WM.Sections.PulsesOf(plain) - p0;
                if (p != 1) return "section " + plain + " pulses " + p;
                if (maxRings < 1) return "no ring";
                if (WM.Sections.LaneLights - ll0 < 1) return "lanes lit " + (WM.Sections.LaneLights - ll0);
                if (maxLetterP <= 0.02f) return "letter pulse " + F(maxLetterP);
                return "ok: section " + plain + " at beat " + F(ps) + ": rings " + maxRings + ", lanes lit " + (WM.Sections.LaneLights - ll0) + ", bounce " + F(maxRing) + " u, letter pulse " + F(maxLetterP);
            });
        }
        // ---- every section starting within the next 40 s pulses once
        {
            int all0 = WM.Sections.Pulses;
            var per0 = new int[SM.SectionCount]; for (int s = 0; s < per0.Length; s++) per0[s] = WM.Sections.PulsesOf(s);
            double from = SM.SectionStartBeat(1) - 0.5;
            float window = Mathf.Min(40f, (float)((SM.TotalBeats - from) / GlobalClock.BeatsPerSecond) + 1f);
            double to = from + window * GlobalClock.BeatsPerSecond;
            yield return SeekPlay(from);
            t0 = Time.realtimeSinceStartup;
            double lb = -1; bool wrapped = false;
            while (Time.realtimeSinceStartup - t0 < window + 6f)
            {
                yield return null;
                double b = GlobalClock.SongBeatD;
                if (b < lb - 1.0) { wrapped = true; break; }
                lb = b;
                if (b >= to) break;
            }
            yield return Frames(2);
            double reached = lb;
            Check("every section's first downbeat pulses exactly once (sections starting within " + window.ToString("F0") + " s from section 1)", () =>
            {
                var bad = new StringBuilder(); int total = 0, n = 0;
                for (int s = 1; s < per0.Length; s++)
                {
                    double ss = SM.SectionStartBeat(s);
                    if (ss < from + 0.5 || ss > reached - 0.5) continue;
                    n++;
                    int p = WM.Sections.PulsesOf(s) - per0[s]; total += p; if (p != 1) bad.Append(" s").Append(s).Append('=').Append(p);
                }
                if (n == 0) return "no section start in the window (reached beat " + F(reached) + ")";
                if (bad.Length > 0) return "sections pulsed" + bad + " (wrapped " + wrapped + ")";
                return "ok: " + total + " pulses over " + n + " sections (all " + (WM.Sections.Pulses - all0) + ", reached beat " + F(reached) + ")";
            });
        }
        // ---- nothing left after a stop, nor after a replay
        GlobalClock.Stop();
        yield return Wait(0.4f);
        Check("nothing is left after the song stops", LeftOver);
        yield return SeekPlay(0.0);
        yield return Wait(1.0f);
        GlobalClock.Stop();
        yield return Wait(0.4f);
        Check("nothing is left after a replay and stop", LeftOver);
    }

    static string LeftOver()
    {
        foreach (var f in WM.Launches)
        {
            if (f == null || f.Retiring) continue;
            if (f.BeamActive || f.RingsShown > 0 || f.ShockRadius > 0f || f.Flash > 0f || f.WaterGlow > 0f || f.LaneLift > 0f) return "launch " + f.Index + ": beam " + F(f.BeamHeight) + ", rings " + f.RingsShown + ", shock " + F(f.ShockRadius) + ", flash " + F(f.Flash);
        }
        if (WM.Sections.Active || WM.Sections.ActiveRings > 0) return "section rings " + WM.Sections.ActiveRings;
        if (WM.Echoes.ActiveImages > 0 || WM.Echoes.ActiveRecords > 0) return "echo images " + WM.Echoes.ActiveImages + ", records " + WM.Echoes.ActiveRecords;
        if (WM.Stairs.ActiveTrail > 0) return "stairs trail " + WM.Stairs.ActiveTrail;
        foreach (var kb in SM.Islands) if (kb != null && Mathf.Abs(kb.FxLift) > 1e-4f) return kb.name + " FxLift " + F(kb.FxLift);
        if (LaunchFx.BreathNow > 1e-4f) return "breath " + F(LaunchFx.BreathNow);
        if (OrbitCamera.I != null && OrbitCamera.I.BreathNow > 1e-4f) return "camera breath " + F(OrbitCamera.I.BreathNow);
        for (int s = 0; s < SectionPlinth.Count; s++) if (SectionPlinth.PulseOf(s) > 1e-3f) return "letter pulse of " + s + " " + F(SectionPlinth.PulseOf(s));
        return "ok";
    }

    // ------------------------------------------------------------------ part B: the present mode
    static IEnumerator PartPresent()
    {
        Info("part B: the riser and the crash in the present mode");
        Prepare();
        if (launchKb == null || SM == null || !SM.HasSong || Gallery.CurrentId != SongId) { var st = Gallery.LoadState(SongId); if (st == null) { Line(false, "present: " + SongId, "not found"); yield break; } st.loop = true; yield return Load(st); }
        if (!PickLaunch()) { Line(false, "present: a launch on " + SongId, "none"); yield break; }
        GlobalClock.LoopSong = true;
        Presenter.Enter();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Presenting, 4f);
        yield return Frames(2);
        if (Presenter.CurrentPhase != Presenter.Phase.Presenting) { Line(false, "present: entering", "phase " + Presenter.CurrentPhase); yield break; }
        Presenter.SetPaused(true);
        yield return Frames(2);
        var fx = WM.LaunchOf(launchKb);
        Presenter.ScrubBeat = S + 0.5 * SB; yield return Frames(4);
        float hs = fx != null ? fx.BeamHeight : -1f; int rs = fx != null ? fx.RingsShown : 0; float brs = Presenter.Rig != null ? Presenter.Rig.Target.breath : -1f; float fovS = Camera.main != null ? Camera.main.fieldOfView : 0f;
        yield return PresentShot("riser9_present_swell.png");
        Presenter.ScrubBeat = S + 0.92 * SB; yield return Frames(4);
        float h92 = fx != null ? fx.BeamHeight : -1f;
        yield return PresentShot("riser9_present_swell90.png");
        Presenter.ScrubBeat = L + 0.25; yield return Frames(4);
        float shock = fx != null ? fx.ShockRadius : -1f, flash = fx != null ? fx.Flash : -1f, hc = fx != null ? fx.BeamHeight : -1f;
        yield return PresentShot("riser9_present_crash.png");
        Presenter.ScrubBeat = L + 3.0; yield return Frames(4);
        float shockAfter = fx != null ? fx.ShockRadius : -1f;
        Check("present mode: a scrub into the swell shows the beam and its rings, the rig breathes (a wider view); the landing shows the shockwave; all gone after", () =>
        {
            if (fx == null) return "no LaunchFx for the launch";
            if (!(hs > 1f && h92 > hs)) return "beam at 50 / 92 % " + F(hs) + " / " + F(h92);
            if (rs < 2) return "rings " + rs;
            if (brs <= 0.02f) return "rig breath " + F(brs);
            if (Mathf.Abs(fovS - PresentRig.Fov) < 0.3f) return "fov " + F(fovS) + " (base " + F(PresentRig.Fov) + ")";
            if (shock < 2f) return "shockwave at L + 0.25: " + F(shock);
            if (hc > 0.01f) return "beam at the landing " + F(hc);
            if (shockAfter > 0f) return "shockwave after the settle " + F(shockAfter);
            return "ok: beam " + F(hs) + " / " + F(h92) + " u, rings " + rs + ", rig breath " + F(brs) + ", fov " + F(fovS) + ", shockwave " + F(shock) + " u, flash " + F(flash);
        });
        // live: the crash fires in the present mode too
        Presenter.ScrubBeat = double.NaN;
        int cr0 = fx != null ? fx.Crashes : 0;
        GlobalClock.Seek(S - 1.0);
        yield return Frames(2);
        Presenter.SetPaused(false);
        float t0 = Time.realtimeSinceStartup;
        double maxB = -9;
        while (Time.realtimeSinceStartup - t0 < (float)((SB + 3.0) / GlobalClock.BeatsPerSecond) + 6f)
        {
            yield return null;
            double b = Presenter.SongBeat; maxB = Math.Max(maxB, b);
            if (b >= L + 1.0 || !Presenter.Active) break;
        }
        Check("present mode, live: the crash fires once on the landing", () => fx != null && fx.Crashes - cr0 == 1 ? "ok: crashes +1 (beat reached " + F(maxB) + ")" : "crashes +" + (fx != null ? fx.Crashes - cr0 : -1) + ", beat reached " + F(maxB) + ", phase " + Presenter.CurrentPhase);
        Presenter.Exit();
        yield return Until(() => Presenter.CurrentPhase == Presenter.Phase.Off, 4f);
    }

    // ------------------------------------------------------------------ part C: the echoes (the v1 fixture)
    static IEnumerator PartEcho()
    {
        Info("part C: echo afterimages (the v1 fixture)");
        Prepare();
        yield return LoadFixture();
        AudioCube cube = null;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && !c.IsOnMoon && !c.IsDrums && c.layer == 0 && c.Island != null && !c.Island.IsStairs && c.nodes.Count >= 2 && (cube == null || c.nodes.Count > cube.nodes.Count)) cube = c;
        if (cube == null) { Line(false, "a melody cube on the fixture", "none"); yield break; }
        var kb = cube.Island;
        cube.SetEcho(2);
        yield return Frames(2);
        Frame(kb);
        int fires0 = WM.Echoes.Fires, images0 = WM.Echoes.Images; WM.Echoes.MaxActive = 0;
        double cs = SM.ColumnStart(kb.column), ce = cs + SM.PassLength(kb.column);
        bool shot = false; int maxActive = 0;
        yield return SeekPlay(cs - 0.5);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < (float)((ce - cs + 2.0) / GlobalClock.BeatsPerSecond) + 5f && GlobalClock.SongBeatD < ce + 1.0)
        {
            yield return null;
            maxActive = Mathf.Max(maxActive, WM.Echoes.ActiveImages);
            if (!shot && WM.Echoes.ActiveImages >= 1 && GlobalClock.SongBeatD > cs + 0.3) { shot = true; yield return PausedShot("riser9_echo.png"); }
        }
        GlobalClock.Stop();
        yield return Wait(0.4f);
        Check("an echoing note (echo 2) leaves afterimages on its tile at each repeat, none left after the stop", () =>
        {
            int fires = WM.Echoes.Fires - fires0, images = WM.Echoes.Images - images0;
            if (fires < 1) return "echo notes recorded " + fires + " (cube '" + cube.name + "' echo " + cube.echo + ")";
            if (maxActive < 1) return "no afterimage shown (" + images + " scheduled)";
            if (WM.Echoes.ActiveImages > 0 || WM.Echoes.ActiveRecords > 0) return "left: images " + WM.Echoes.ActiveImages + ", records " + WM.Echoes.ActiveRecords;
            return "ok: " + fires + " echo notes, " + images + " afterimages scheduled, up to " + maxActive + " shown at once, captured " + shot;
        });
        cube.SetEcho(0);
    }

    // ------------------------------------------------------------------ part E: the cost (get-proto's busiest stretch, effects on vs off)
    struct PerfSample { public int frames, regFrames, unregFrames; public double seconds, lateMs, gfxReg, gfxUnreg, wmMs, launchMs, sectionMs, echoMs, stairsMs, worst; }

    static IEnumerator Measure(double from, float seconds, bool on, System.Action<PerfSample> done)
    {
        WorldMagic.V9Fx = on;
        var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        Unity.Profiling.ProfilerRecorder late = default, reg = default, unreg = default;
        foreach (var h in handles)
        {
            var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
            var opt = Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.WrapAroundWhenCapacityReached;
            if (d.Name == "PreLateUpdate.ScriptRunBehaviourLateUpdate" && d.UnitType == Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds) { late = new Unity.Profiling.ProfilerRecorder(h, 1, opt); late.Start(); }
            else if (d.Name == "GfxResource.Register") { reg = new Unity.Profiling.ProfilerRecorder(h, 1, opt); reg.Start(); }
            else if (d.Name == "GfxResource.Unregister") { unreg = new Unity.Profiling.ProfilerRecorder(h, 1, opt); unreg.Start(); }
        }
        yield return SeekPlay(from);
        yield return Wait(0.6f);
        var ps = new PerfSample();
        float t0 = Time.realtimeSinceStartup;
        double wm = 0, l = 0, se = 0, e = 0, st = 0;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            ps.frames++; ps.seconds += dt; ps.worst = Math.Max(ps.worst, dt);
            if (late.Valid) ps.lateMs += late.LastValue / 1e6;
            if (reg.Valid) { ps.gfxReg += reg.LastValue; if (reg.LastValue != 0) ps.regFrames++; }
            if (unreg.Valid) { ps.gfxUnreg += unreg.LastValue; if (unreg.LastValue != 0) ps.unregFrames++; }
            wm += WM.LastMs; l += WM.MsLaunch; se += WM.MsSections; e += WM.MsEchoes; st += WM.MsStairs;
        }
        GlobalClock.Stop();
        int n = Math.Max(1, ps.frames);
        ps.lateMs /= n; ps.gfxReg /= n; ps.gfxUnreg /= n; ps.wmMs = wm / n; ps.launchMs = l / n; ps.sectionMs = se / n; ps.echoMs = e / n; ps.stairsMs = st / n;
        if (late.Valid) late.Dispose(); if (reg.Valid) reg.Dispose(); if (unreg.Valid) unreg.Dispose();
        WorldMagic.V9Fx = true;
        done(ps);
    }

    static string Fmt(PerfSample p) => p.frames + " frames, " + (p.frames / Math.Max(0.01, p.seconds)).ToString("F0") + " fps (mean " + (1000.0 * p.seconds / Math.Max(1, p.frames)).ToString("F1") + " ms, worst " + (p.worst * 1000f).ToString("F0") + " ms), ScriptRunBehaviourLateUpdate " + p.lateMs.ToString("F2") + " ms, GfxResource register / unregister fired in " + p.regFrames + " / " + p.unregFrames + " frames (raw " + p.gfxReg.ToString("E1") + " / " + p.gfxUnreg.ToString("E1") + "), WorldMagic " + p.wmMs.ToString("F2") + " ms (launches " + p.launchMs.ToString("F3") + ", sections " + p.sectionMs.ToString("F3") + ", echoes " + p.echoMs.ToString("F3") + ", stairs " + p.stairsMs.ToString("F3") + ")";

    static IEnumerator PartPerf()
    {
        Info("part E: the cost on " + SongId + "'s busiest stretch (the D launch into bar 57: swell, crash, stairs, echoes), effects on vs off");
        Prepare();
        var st = Gallery.LoadState(SongId);
        if (st == null) { Line(false, "perf: " + SongId, "not found"); yield break; }
        st.loop = true;
        yield return Load(st);
        // the stretch: the latest energetic launch's swell start − 2 beats
        double from = 216.0; KeyBlock lk = null;
        foreach (var kb in SM.Islands) if (kb != null && kb.launch && !kb.IsMoon && SM.TurnEnd(kb) < SM.TotalBeats - 1 && (lk == null || kb.energy > lk.energy || (kb.energy == lk.energy && SM.TurnEnd(kb) > SM.TurnEnd(lk)))) lk = kb;
        if (lk != null) from = SM.TurnEnd(lk) - LaunchFx.SwellBeats - 2.0;
        if (lk != null) Frame(lk, SM.LaunchTarget(lk));
        int launches = 0, stairsN = 0; foreach (var kb in SM.Islands) if (kb != null) { if (kb.launch) launches++; if (kb.IsStairs) stairsN++; }
        PerfSample on = default, off = default, noLift = default;
        yield return Measure(from, 7f, true, p => on = p);
        yield return Wait(0.3f);
        yield return Measure(from, 7f, false, p => off = p);
        yield return Wait(0.3f);
        float il = LaunchFx.IslandLift, lb = LaunchFx.LaneBounce, sb2 = SectionFx.LaneBounce;
        LaunchFx.IslandLift = 0f; LaunchFx.LaneBounce = 0f; SectionFx.LaneBounce = 0f;
        yield return Measure(from, 7f, true, p => noLift = p);
        LaunchFx.IslandLift = il; LaunchFx.LaneBounce = lb; SectionFx.LaneBounce = sb2;
        Info("perf ON  (" + launches + " launches, " + stairsN + " stairs, from beat " + F(from) + "): " + Fmt(on));
        Info("perf OFF (WorldMagic.V9Fx = false): " + Fmt(off));
        Info("perf ON without the island lifts (no SetFxLift re-poses / cube-line re-bakes): " + Fmt(noLift));
        Check("the v9 effects cost little: their WorldMagic laps ≤ 1.5 ms a frame and ScriptRunBehaviourLateUpdate at most 2.5 ms more than with them off", () =>
        {
            double mine = on.launchMs + on.sectionMs + on.echoMs + on.stairsMs;
            double extra = on.lateMs - off.lateMs;
            if (on.frames < 20 || off.frames < 20) return "too few frames (" + on.frames + " / " + off.frames + ")";
            if (mine > 1.5) return "the effects' laps " + mine.ToString("F2") + " ms a frame";
            if (extra > 2.5) return "LateUpdate " + on.lateMs.ToString("F2") + " ms on vs " + off.lateMs.ToString("F2") + " ms off (+" + extra.ToString("F2") + ")";
            return "ok: effects " + mine.ToString("F2") + " ms a frame; LateUpdate " + on.lateMs.ToString("F2") + " ms on vs " + off.lateMs.ToString("F2") + " off (" + (extra >= 0 ? "+" : "") + extra.ToString("F2") + "); gfx register fired in " + on.regFrames + " vs " + off.regFrames + " frames (" + noLift.regFrames + " without lifts); " + (on.frames / Math.Max(0.01, on.seconds)).ToString("F0") + " vs " + (off.frames / Math.Max(0.01, off.seconds)).ToString("F0") + " fps";
        });
    }

    // ------------------------------------------------------------------ part D: the stairs (get-proto's runners)
    static IEnumerator PartStairs()
    {
        Info("part D: stairs FX (" + SongId + "'s runners)");
        Prepare();
        var st = Gallery.LoadState(SongId);
        if (st == null) { Line(false, "stairs: " + SongId, "not found"); yield break; }
        st.loop = true;
        yield return Load(st);
        KeyBlock stairs = null; AudioCube runner = null;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.IsRunner || c.Island == null || c.nodes.Count < 3) continue;
            if (stairs == null || (c.Island.stairLead && !stairs.stairLead) || (c.Island.stairLead == stairs.stairLead && c.nodes.Count > runner.nodes.Count)) { stairs = c.Island; runner = c; }
        }
        if (stairs == null) { Line(false, "a stairs island with a runner on " + SongId, "none"); yield break; }
        var lead = stairs.LeadInTarget();
        Info("stairs '" + stairs.name + "' in column " + stairs.column + ": " + stairs.cols + " steps, dir " + stairs.stairDir + ", lead " + stairs.stairLead + ", runner with " + runner.nodes.Count + " nodes, lead-in target " + (lead != null ? lead.assignedChord : "none"));
        Frame(stairs);
        int f0 = WM.Stairs.StepFlashes, m0 = WM.Stairs.TrailMarks, lf0 = WM.Stairs.LeadFlashes, p0 = WM.Stairs.DustPuffs; WM.Stairs.MaxTrail = 0;
        double cs = SM.ColumnStart(stairs.column), ce = cs + SM.PassLength(stairs.column);
        bool shot = false;
        yield return SeekPlay(cs - 0.5);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < (float)((ce - cs + 2.0) / GlobalClock.BeatsPerSecond) + 5f && GlobalClock.SongBeatD < ce + 0.75)
        {
            yield return null;
            if (!shot && WM.Stairs.ActiveTrail >= 2) { shot = true; yield return PausedShot("riser9_stairs.png"); }
        }
        GlobalClock.Stop();
        yield return Wait(0.4f);
        Check("a runner lights each step and puffs, a trail of step outlines fades behind it" + (stairs.stairLead && lead != null ? ", the lead-in's last step flashes the grid it falls into" : "") + "; none left after", () =>
        {
            int f = WM.Stairs.StepFlashes - f0, m = WM.Stairs.TrailMarks - m0, l = WM.Stairs.LeadFlashes - lf0;
            if (f < 2) return "steps lit " + f;
            if (m < 2 || WM.Stairs.MaxTrail < 2) return "trail marks " + m + ", most shown " + WM.Stairs.MaxTrail;
            if (stairs.stairLead && lead != null && l < 1) return "lead-in flashes " + l;
            if (WM.Stairs.ActiveTrail > 0) return "trail left " + WM.Stairs.ActiveTrail;
            return "ok: steps lit " + f + ", puffs " + (WM.Stairs.DustPuffs - p0) + ", trail marks " + m + " (up to " + WM.Stairs.MaxTrail + " shown), lead-in flashes " + l + ", captured " + shot;
        });
    }
}
