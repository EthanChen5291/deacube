using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Play-mode checks of the presentation's lifecycle (SPEC v3 §4). v9 (W): the plunge (a separate stage of cubes ricocheting through tiles, its
/// strike sync, tile budget, contact / mirror / pop checks) is gone; the presentation shows the real world rising from the sea (Presenter,
/// PresentSea, PresentWater, PresentRig; the detailed checks are V9ChecksPresent). What stays here: enter (veil, HUD, locks, the orbit camera,
/// the clock after the pre-roll, Onboarding), the world shown (the main camera keeps its culling mask; the first columns up, later ones under),
/// pause / resume, exit (camera transform and settings, fog, HUD, locks, playback paused, nothing of the presentation left), and the end state of
/// a song that does not loop (replay + exit, replay restarts). Start with <see cref="Run"/> from execute_code, poll <see cref="Done"/> /
/// <see cref="Report"/>. Every check prints a numbered PASS / FAIL line. Never touches the user's save: songs come from MusicTheory.RandomSong(4242)
/// and the v1 fixture.
/// </summary>
public static class V3ChecksC
{
    public static string Report = "";
    public static bool Done = true;
    public static string Progress = "";
    static V3ChecksCRunner runner;
    static int checkNo;
    static readonly List<string> lines = new List<string>();

    public static string CapturePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));

    /// <summary>which: "all" (lifecycle on the demo + the fixture's end state), "lifecycle", "fixture".</summary>
    public static string Run(string which = "all")
    {
        if (!Application.isPlaying) return "play mode only";
        if (!Done) return "already running: " + Progress;
        if (runner == null) { var go = new GameObject("V3ChecksC") { hideFlags = HideFlags.DontSave }; runner = go.AddComponent<V3ChecksCRunner>(); }
        Done = false; Report = ""; lines.Clear(); checkNo = 0; Progress = "starting";
        runner.StartCoroutine(Main(which));
        return "started " + which;
    }

    static void Check(bool ok, string what) { checkNo++; lines.Add(checkNo + ". " + (ok ? "PASS " : "FAIL ") + what); }
    static void Note(string what) { lines.Add("   " + what); }

    static void Prep()
    {
        try { MainMenu.Hide(); } catch (Exception e) { Note("MainMenu.Hide threw " + e.Message); }
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static IEnumerator Main(string which)
    {
        int errors = 0; var errs = new List<string>();
        Application.LogCallback hook = (msg, stack, type) => { if (type == LogType.Exception || type == LogType.Error) { errors++; if (errs.Count < 6) errs.Add(type + ": " + msg); } };
        Application.logMessageReceived += hook;
        string md5a = SaveHash();
        bool loop0 = GlobalClock.LoopSong;
        IEnumerator body = null;
        if (which == "all" || which == "lifecycle")
        {
            Progress = "loading demo";
            Prep();
            SongManager.I.StartNewSong(MusicTheory.RandomSong(4242));
            Prep();
            GlobalClock.LoopSong = true;
            if (GlobalClock.IsPlaying) GlobalClock.Pause();
            yield return new WaitForSecondsRealtime(2.5f);
            Note("demo: " + SongManager.I.Islands.Count + " islands, " + SongManager.I.Moons.Count + " moons, " + SequenceMaster.Cubes.Count + " cubes, bpm " + GlobalClock.BPM + ", total beats " + GlobalClock.TotalBeats);
            body = Lifecycle(); while (body.MoveNext()) yield return body.Current;
        }
        if (which == "all" || which == "fixture")
        {
            Progress = "loading fixture";
            Prep();
            SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
            Prep();
            GlobalClock.LoopSong = false;                        // the fixture exercises the end state (loop off)
            if (GlobalClock.IsPlaying) GlobalClock.Pause();
            if (OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(0, true);
            yield return new WaitForSecondsRealtime(2f);
            Note("fixture: " + SongManager.I.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes, bpm " + GlobalClock.BPM + ", total beats " + GlobalClock.TotalBeats);
            body = EndRun(); while (body.MoveNext()) yield return body.Current;
            GlobalClock.LoopSong = loop0;
        }
        Application.logMessageReceived -= hook;
        Check(errors == 0, "no errors / exceptions logged during the whole run (" + errors + ")" + (errs.Count > 0 ? ": " + string.Join(" | ", errs) : ""));
        string md5b = SaveHash();
        Check(md5a == md5b, "the user's save is untouched (" + (md5b ?? "absent") + ")");
        int pass = 0, fail = 0; foreach (var l in lines) { if (l.Contains(". PASS ")) pass++; else if (l.Contains(". FAIL ")) fail++; }
        Report = "V3ChecksC " + which + ": " + pass + " PASS, " + fail + " FAIL\n" + string.Join("\n", lines);
        Progress = "done";
        Done = true;
        try { Directory.CreateDirectory(CapturePath); File.WriteAllText(Path.Combine(CapturePath, "v3c_report.txt"), Report + "\n"); } catch (Exception) { }
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

    // ================================================================ lifecycle
    static float HudTarget()
    {
        var ui = UIManager.I; if (ui == null) return -1f;
        var f = typeof(UIManager).GetField("hudAlphaTarget", BindingFlags.NonPublic | BindingFlags.Instance);
        return f != null ? (float)f.GetValue(ui) : -1f;
    }

    /// <summary>Objects on the old presentation layer, not counting CubeSmear's pooled (hidden) ribbons (package R keeps them for reuse and they
    /// keep the layer they last drew on).</summary>
    static int Layer8Objects()
    {
        int n = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.gameObject.layer == DeaLayers.Present && !(t.parent != null && t.parent.GetComponent<CubeSmear>() != null)) n++;
        return n;
    }

    static IEnumerator Lifecycle()
    {
        Progress = "lifecycle";
        var cam = Camera.main;
        var oc = OrbitCamera.I;
        var events = new List<string>();
        Action<string> onEv = e => events.Add(e);
        Onboarding.OnEvent += onEv;
        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
        int mask0 = cam.cullingMask; var clear0 = cam.clearFlags; Color bg0 = cam.backgroundColor; float fov0 = cam.fieldOfView;
        bool fog0 = RenderSettings.fog; var fogMode0 = RenderSettings.fogMode; float fs0 = RenderSettings.fogStartDistance, fe0 = RenderSettings.fogEndDistance;
        float hud0 = HudTarget(); int l80 = Layer8Objects(); string locks0 = WorldInput.Describe();
        Note("before: " + locks0 + " hud " + hud0 + " layer8 " + l80 + " cam " + p0.ToString("F3"));

        Presenter.Toggle();                                   // what the HUD's present button calls (P calls Enter)
        float t0 = Time.realtimeSinceStartup;
        bool overlayUp = GameObject.Find("PresentOverlay") != null;
        while (!(Presenter.CurrentPhase == Presenter.Phase.Presenting && Presenter.Clock == Presenter.ClockMode.Live) && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        bool live = Presenter.CurrentPhase == Presenter.Phase.Presenting && Presenter.Clock == Presenter.ClockMode.Live;
        Check(live && Presenter.Active, "Toggle enters: presenting and the clock plays after the pre-roll (" + (Time.realtimeSinceStartup - t0).ToString("F2") + " s)");
        Check(overlayUp, "overlay canvas (veil, progress line, exit, end icons) created on enter");
        Check(cam.cullingMask == mask0, "v9: the main camera keeps rendering the world (culling mask " + cam.cullingMask + ", was " + mask0 + ")");
        Check(WorldInput.IsLockedBy("present") && WorldInput.KeysLocked, "world + keys locked by \"present\" (" + WorldInput.Describe() + ")");
        Check(oc == null || oc.Suspended, "OrbitCamera suspended");
        Check(HudTarget() == 0f, "HUD hidden (alpha target " + HudTarget() + ")");
        var sea = Presenter.Sea;
        Check(GlobalClock.IsPlaying && sea != null && sea.Count > 0 && Presenter.Rig != null && Presenter.Water != null, "v9: the presentation is built (" + (sea != null ? sea.Count : 0) + " risers on the schedule, the water and the camera rig), the song playing");
        Check(events.Contains(Onboarding.Ev.PresentEntered), "Onboarding: present.entered");
        yield return new WaitForSecondsRealtime(2f);
        // the world shows: the columns that started are up, the ones far ahead are still under the sea
        double b = Presenter.SongBeat; int up = 0, under = 0; string why = null;
        var sm = SongManager.I;
        if (sea != null)
            for (int c = 0; c < sm.ColumnCount; c++)
            {
                double s = sm.ColumnStart(c);
                if (s <= b) { if (sea.ColumnDepth(c, b) < 0.02f) up++; else if (why == null) why = "column " + c + " not up"; }
                else if (s > b + 3.0) { if (sea.ColumnMinDepth(c, b) > 0.98f) under++; else if (why == null) why = "column " + c + " up early"; }
            }
        Check(why == null && up > 0, "v9: the real world shows: " + up + " columns that started are up, " + under + " later ones still under the sea at beat " + b.ToString("F2") + (why != null ? " (" + why + ")" : ""));
        // pause / resume through the presenter's own key path
        Presenter.SetPaused(true);
        double bp = Presenter.Beat;
        Vector3 cp = cam.transform.position;
        yield return new WaitForSecondsRealtime(0.4f);
        Check(!GlobalClock.IsPlaying && Math.Abs(Presenter.Beat - bp) < 1e-6 && (cam.transform.position - cp).magnitude < 1e-4f, "Space pauses: the clock, the islands and the camera freeze");
        Presenter.SetPaused(false);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(GlobalClock.IsPlaying && Presenter.Beat > bp, "Space resumes (beat " + bp.ToString("F2") + " -> " + Presenter.Beat.ToString("F2") + ", never backwards)");

        Presenter.Exit();
        t0 = Time.realtimeSinceStartup;
        while (Presenter.CurrentPhase != Presenter.Phase.Off && Time.realtimeSinceStartup - t0 < 4f) yield return null;
        float exitSec = Time.realtimeSinceStartup - t0;
        yield return null; yield return null;
        float dp = (cam.transform.position - p0).magnitude, dr = Quaternion.Angle(cam.transform.rotation, r0);
        Check(Presenter.CurrentPhase == Presenter.Phase.Off && !Presenter.Active, "Exit completes (" + exitSec.ToString("F2") + " s incl. the veil)");
        Check(dp < 1e-3f && dr < 0.06f, "camera transform restored (|dp| " + dp.ToString("E2") + ", angle " + dr.ToString("F4") + " deg)");
        Check(cam.cullingMask == mask0 && cam.clearFlags == clear0 && cam.backgroundColor == bg0 && Mathf.Abs(cam.fieldOfView - fov0) < 1e-4f, "culling mask, clear flags, background, FOV restored");
        Check(RenderSettings.fog == fog0 && RenderSettings.fogMode == fogMode0 && Mathf.Abs(RenderSettings.fogStartDistance - fs0) < 1e-4f && Mathf.Abs(RenderSettings.fogEndDistance - fe0) < 1e-4f, "fog restored (on " + RenderSettings.fog + ", " + RenderSettings.fogStartDistance + ".." + RenderSettings.fogEndDistance + ")");
        Check(Mathf.Approximately(HudTarget(), hud0), "HUD alpha target restored (" + HudTarget() + ")");
        Check(!WorldInput.IsLockedBy("present") && WorldInput.Describe() == locks0, "locks released (" + WorldInput.Describe() + ")");
        Check(oc == null || !oc.Suspended, "OrbitCamera.Suspended false");
        Check(!GlobalClock.IsPlaying, "playback paused");
        int l8 = Layer8Objects();
        bool islandsUp = true; foreach (var kb in sm.Islands) if (kb != null && (kb.SeaDrop > 1e-4f || kb.SeaDepth > 0f)) islandsUp = false;
        foreach (var kb in sm.Moons) if (kb != null && (kb.SeaDrop > 1e-4f || kb.SeaDepth > 0f)) islandsUp = false;
        Check(l8 == l80 && GameObject.Find("Presentation") == null && SectionPlinth.ColumnSinks == null && islandsUp, "nothing of the presentation is left (no water, no floor sinks, every island up; layer-8 objects " + l8 + ")");
        Check(GameObject.Find("PresentOverlay") == null, "overlay canvas destroyed after the veil");
        Check(events.Contains(Onboarding.Ev.PresentExited), "Onboarding: present.exited");
        Onboarding.OnEvent -= onEv;
    }

    // ================================================================ the end state (loop off)
    static void Shoot(string file)
    {
        Directory.CreateDirectory(CapturePath);
        string p = Path.Combine(CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        if (Presenter.Active) Presenter.RequestCapture(p, 1600, 900);
        else Presenter.RenderToPng(Camera.main, p, 1600, 900);
        Note("capture " + file + " at song beat " + Presenter.SongBeat.ToString("F2"));
    }

    static IEnumerator EndRun()
    {
        Progress = "fixture end";
        var sm = SongManager.I;
        double total = sm.TotalBeats;
        Presenter.Enter();
        float t0 = Time.realtimeSinceStartup;
        while (Presenter.CurrentPhase != Presenter.Phase.Presenting && Time.realtimeSinceStartup - t0 < 3f) yield return null;
        bool sawEnd = false;
        t0 = Time.realtimeSinceStartup;
        float limit = (float)(total / GlobalClock.BeatsPerSecond) + 14f;
        while (Time.realtimeSinceStartup - t0 < limit)
        {
            yield return null;
            if (Presenter.EndShown) { sawEnd = true; break; }
        }
        Check(sawEnd, "fixture (loop off): the song ends, every island up, replay + exit icons appear");
        if (sawEnd)
        {
            Shoot("c_end.png");
            yield return null; yield return null;
            Presenter.Replay();
            float r0 = Time.realtimeSinceStartup;
            while (!(Presenter.Clock == Presenter.ClockMode.Live && Presenter.SongBeat > 0.5) && Time.realtimeSinceStartup - r0 < 8f) yield return null;
            Check(Presenter.Clock == Presenter.ClockMode.Live && GlobalClock.IsPlaying, "fixture: replay restarts the song from the top (" + (Time.realtimeSinceStartup - r0).ToString("F2") + " s)");
        }
        Presenter.Exit();
        t0 = Time.realtimeSinceStartup;
        while (Presenter.CurrentPhase != Presenter.Phase.Off && Time.realtimeSinceStartup - t0 < 4f) yield return null;
        Check(Presenter.CurrentPhase == Presenter.Phase.Off, "fixture: exit completes");
        yield return new WaitForSecondsRealtime(0.6f);
        Shoot("c_exit.png");
        yield return new WaitForSecondsRealtime(0.3f);
    }
}

/// <summary>Coroutine host of V3ChecksC.</summary>
public class V3ChecksCRunner : MonoBehaviour { }
