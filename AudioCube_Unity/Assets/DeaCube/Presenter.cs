using System;
using UnityEngine;

/// <summary>
/// The presentation (SPEC v3 §4 lifecycle). v9 (W; the user, 2026-10-01: "scrap the current falling play mode, just make it record of the cube
/// path as we have now, with camera slowly falling and islands appearing from the watter as the measures pass (with water animations)"): the v4–v8
/// plunge (a separate stage of cubes ricocheting down through tiles) is gone; the presentation shows the REAL world playing the song.
/// P or the HUD's present button enters: a focus loop is dismissed, the HUD fades, the world and the hotkeys lock ("present"), the orbit camera is
/// saved and suspended, an indigo veil covers the swap. Behind it every island, Moon and section floor goes under the sea (<see cref="PresentSea"/>),
/// the song seeks to 0, and after a short pre-roll the clock plays: the real AudioCubes walk their paths and sound as in normal playback (audio is
/// untouched). Each column's islands burst out of the sea just before it plays (a sixteenth apart, the last one beat ahead), its floor rises under
/// them, and they settle on the downbeat; the water answers on the beat (<see cref="PresentWater"/>: swell, foam, spray, sheets off the sides, a
/// crest wave, ripples). The camera (<see cref="PresentRig"/>) follows the columns from high above the first ones, falling slowly and steadily
/// toward the sea over the whole song. All three are pure functions of the presentation beat (the audio clock, smoothed per frame): Space pauses
/// and resumes (everything freezes with the song), the end of a song that does not loop shows replay / exit, a looping song dips the veil at the
/// loop and starts over, a replay puts everything under again. Esc / P exit: a 0.2 s veil, every island back up exactly where it was, the camera
/// (OrbitCamera.RestoreView and its settings), fog, HUD and locks restored, playback paused where it was before the presentation.
/// </summary>
public class Presenter : MonoBehaviour
{
    public static Presenter I;

    public enum Phase { Off, Covering, Presenting, ExitCovering, Revealing }
    public enum ClockMode { Preroll, Live, Ended }

    // ---------------------------------------------------------------- contract (M0 static API)
    /// <summary>True from Enter until the edit world is restored (the veil may still be fading out after that).</summary>
    public static bool Active => I != null && (I.phase == Phase.Covering || I.phase == Phase.Presenting || I.phase == Phase.ExitCovering);
    public static void Enter() { if (Active) return; Ensure(); I.BeginEnter(); }
    public static void Exit() { if (I != null) I.BeginExit(); }
    public static void Toggle() { if (Active) Exit(); else Enter(); }

    // ---------------------------------------------------------------- diagnostics
    public static Phase CurrentPhase => I != null ? I.phase : Phase.Off;
    public static ClockMode Clock => I != null ? I.clock : ClockMode.Preroll;
    /// <summary>The presentation beat: negative in the pre-roll, counting the song's loops (monotonic while it plays).</summary>
    public static double Beat => I != null ? I.B : 0.0;
    /// <summary>The beat inside the song that the islands, the water and the camera follow (the pre-roll negative; a looping song wraps).</summary>
    public static double SongBeat => I != null ? I.local : 0.0;
    public static bool Paused => I != null && I.paused;
    public static PresentSea Sea => I != null ? I.sea : null;
    public static PresentRig Rig => I != null ? I.rig : null;
    public static PresentWater Water => I != null ? I.water : null;
    /// <summary>The veil's opacity as last drawn (0 clear .. 1 covered; with a looping song's dip at the loop point).</summary>
    public static float Veil => I == null || I.overlay == null ? 0f : (I.phase == Phase.Presenting ? I.veilNow : I.overlay.Fade);
    public static long GcMaxBytesPerFrame, GcFramesAllocating, GcFramesMeasured, GcFramesExcused;
    public static float WorstFrameSeconds, FrameSecondsSum; public static int FrameCount;
    /// <summary>Camera smoothness: the largest frame-to-frame change of the camera's velocity (u/s²) and its top speed (u/s).</summary>
    public static float CamMaxAccel, CamMaxSpeed;
    Vector3 camPrevPos, camPrevVel; int camSamples;
    public static bool EndShown => I != null && I.overlay != null && I.overlay.EndShown;
    /// <summary>Tests: zero the frame / allocation / camera statistics.</summary>
    public static void ResetStats() { GcMaxBytesPerFrame = 0; GcFramesAllocating = 0; GcFramesMeasured = 0; GcFramesExcused = 0; WorstFrameSeconds = 0f; FrameSecondsSum = 0f; FrameCount = 0; CamMaxAccel = 0f; CamMaxSpeed = 0f; if (I != null) { I.camSamples = 0; if (I.water != null) I.water.ResetStats(); } }
    /// <summary>Replay from the end state (or restart while presenting): everything goes under again and the song starts over.</summary>
    public static void Replay() { if (I != null && I.phase == Phase.Presenting) I.BeginReplay(); }
    public static void SetPaused(bool p) { if (I != null && I.phase == Phase.Presenting && I.paused != p) I.TogglePause(); }

    // ---------------------------------------------------------------- tuning
    const float CoverSeconds = 0.25f, RevealSeconds = 0.4f, ExitCoverSeconds = 0.2f, ExitRevealSeconds = 0.25f;
    /// <summary>The pre-roll: at least this many beats and seconds (and always long enough for the first column to rise from fully under).</summary>
    public static float PrerollBeats = 2.5f, PrerollMinSeconds = 1.6f;
    /// <summary>A looping song: the veil dips over the loop point (covering this long before it, clearing over this long after it).</summary>
    public static float WrapCoverSeconds = 0.22f, WrapRevealSeconds = 0.45f;
    /// <summary>A song that ends: the presentation beat runs on this long (the end card shows once it is past the song's end).</summary>
    public static float EndTailBeats = 4f;
    /// <summary>Tests: while set (and paused), the presentation beat is this value instead of the audio clock (seek anywhere). NaN = live.</summary>
    public static double ScrubBeat = double.NaN;

    Phase phase = Phase.Off;
    ClockMode clock = ClockMode.Preroll;
    float phaseT, fadeBase; int enterFrame = -10;
    bool paused, playCalled, replaying; float replayT;
    double B, local, preroll = 2.5;
    double dspOff; bool dspOffInit; double lastReal;
    bool camSnap;
    float veilNow;
    Vector3 seaSaved; bool seaMoved;   // Fx's sea plane, kept under the camera while presenting (a long song reaches past it)
    float liveSince = -99f;   // when the clock last started playing (KeyStage eases its keyboards there for KeyStage.EaseS)

    PresentSea sea; PresentRig rig; PresentWater water; PresentOverlay overlay; GameObject root;
    Camera cam;

    // saved state
    int savedMask; CameraClearFlags savedClear; Color savedBg; float savedFov, savedNear, savedFar;
    bool savedFog; FogMode savedFogMode; Color savedFogColor; float savedFogStart, savedFogEnd, savedFogDensity;
    double savedSongBeat;
    public static Vector3 SavedCamPos { get; private set; }
    public static Quaternion SavedCamRot { get; private set; }

    static Func<long> allocProbe; static bool probeTried;

    // ---------------------------------------------------------------- fixed-size captures (tests): the main camera into a render texture
    static string capturePath; static int captureW, captureH;
    public static int CapturesDone { get; private set; }
    /// <summary>Tests: at the end of this frame's LateUpdate (after the presentation posed everything) render the main camera into a
    /// <paramref name="w"/> x <paramref name="h"/> texture and write it as a PNG (independent of the Game view's size).</summary>
    public static void RequestCapture(string path, int w, int h) { capturePath = path; captureW = w; captureH = h; }

    /// <summary>Renders <paramref name="c"/> into a w x h texture and writes it to <paramref name="path"/> (PNG).</summary>
    public static bool RenderToPng(Camera c, string path, int w, int h)
    {
        if (c == null) return false;
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = c.targetTexture;
        var prevActive = RenderTexture.active;
        c.targetTexture = rt;
        c.Render();
        c.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);
        return true;
    }

    static void Ensure()
    {
        if (I != null) return;
        I = FindAnyObjectByType<Presenter>();
        if (I == null) I = new GameObject("Presenter").AddComponent<Presenter>();
    }

    void Awake() { if (I == null) I = this; Application.quitting -= OnQuit; Application.quitting += OnQuit; }
    /// <summary>Leaving Play mode / quitting: the world is being torn down, so OnDestroy must not touch it (only the static floor hook is cleared).</summary>
    static bool quitting;
    static void OnQuit() { quitting = true; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { quitting = false; ScrubBeat = double.NaN; }
    void OnDestroy()
    {
        if (I != this) return;
        if (phase != Phase.Off) { if (quitting) SectionPlinth.ColumnSinks = null; else Restore(); }
        I = null;
    }

    // ================================================================ enter
    void BeginEnter()
    {
        if (phase != Phase.Off && phase != Phase.Revealing) return;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || MainMenu.IsShown) return;
        cam = Camera.main;
        if (cam == null) return;
        if (phase == Phase.Revealing && overlay != null) { overlay.Destroy(); overlay = null; }   // a re-entry during the last exit's reveal
        if (CubeInspector.IsOpen) CubeInspector.CloseImmediate();      // package A: drops the proxy, dim and card without a return flight
        if (IslandTray.IsOpen) IslandTray.Close();
        FocusLoop.Release();                                            // SPEC v4 R4: presenting ends a focus loop; the presentation owns the clock
        if (GlobalClock.HasRegion) GlobalClock.ClearRegion();
        replaying = false; replayT = 0f;                                // S7: a replay cut short by the last exit does not leak into this entry

        WorldInput.Lock("present", true);
        if (UIManager.I != null) UIManager.I.SetHudVisible(false);
        var oc = OrbitCamera.I;
        if (oc != null) { oc.SaveView(); oc.Suspended = true; }
        savedSongBeat = GlobalClock.SongBeatD;
        if (GlobalClock.IsPlaying) GlobalClock.Pause();

        savedMask = cam.cullingMask; savedClear = cam.clearFlags; savedBg = cam.backgroundColor; savedFov = cam.fieldOfView; savedNear = cam.nearClipPlane; savedFar = cam.farClipPlane;
        SavedCamPos = cam.transform.position; SavedCamRot = cam.transform.rotation;
        savedFog = RenderSettings.fog; savedFogMode = RenderSettings.fogMode; savedFogColor = RenderSettings.fogColor;
        savedFogStart = RenderSettings.fogStartDistance; savedFogEnd = RenderSettings.fogEndDistance; savedFogDensity = RenderSettings.fogDensity;

        if (overlay != null) overlay.Destroy();
        overlay = BuildOverlay(sm);
        overlay.SetFade(0f);
        enterFrame = Time.frameCount;
        phase = Phase.Covering; phaseT = 0f;
        Onboarding.Notify(Onboarding.Ev.PresentEntered);
    }

    /// <summary>The overlay's progress line: one dot per column in its anchor's chord colour.</summary>
    PresentOverlay BuildOverlay(SongManager sm)
    {
        int n = sm.ColumnCount;
        var cols = new Color[n]; var at = new float[n];
        float total = Mathf.Max(1f, sm.TotalBeats);
        for (int i = 0; i < n; i++) { var a = sm.AnchorOf(i); cols[i] = a != null ? a.chordColor : Color.white; at[i] = sm.ColumnStart(i) / total; }
        return new PresentOverlay(n, cols, at, Exit, Replay);
    }

    /// <summary>Behind the veil: everything under the sea, the water and the camera rig ready, the pre-roll starts.</summary>
    void Swap()
    {
        var sm = SongManager.I;
        root = new GameObject("Presentation");
        sea = new PresentSea(); rig = new PresentRig();
        if (sm == null || !sea.Build(sm) || !rig.Build(sm)) { Debug.LogWarning("Presenter: no song to present"); phase = Phase.ExitCovering; phaseT = ExitCoverSeconds; return; }
        water = new PresentWater(root.transform);
        seaSaved = Fx.SeaCentre; seaMoved = true;
        cam.nearClipPlane = Mathf.Min(savedNear, 0.2f); cam.farClipPlane = Mathf.Max(savedFar, 400f);
        StartPreroll(false);
        ResetStats();
    }

    /// <summary>A clean pre-roll: the song at 0, everything under the sea, the camera high above the first columns; <paramref name="fromReplay"/> =
    /// the replay's own restart (it keeps its reveal fade running).</summary>
    void StartPreroll(bool fromReplay)
    {
        if (!fromReplay) { replaying = false; replayT = 0f; }   // S7
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
        GlobalClock.Seek(0);
        double bps = Math.Max(0.1, GlobalClock.BeatsPerSecond);
        preroll = Math.Max(Math.Max(PrerollBeats, PrerollMinSeconds * bps), (sea != null ? -sea.FirstStart : 0.0) + 0.5);
        B = -preroll; local = B;
        clock = ClockMode.Preroll; playCalled = false; paused = false;
        dspOffInit = false;
        camSnap = true;
        if (sea != null) sea.Apply(local, false);
        if (water != null) water.HideAll();
        if (rig != null) rig.Snap();
    }

    // ================================================================ exit
    void BeginExit()
    {
        if (phase != Phase.Presenting && phase != Phase.Covering) return;
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
        paused = true;
        phase = Phase.ExitCovering; phaseT = overlay != null ? overlay.Fade * ExitCoverSeconds : 0f;
    }

    /// <summary>Gives the edit world back exactly as it was: islands and floors up in place, the water gone, camera, fog, HUD, locks and the
    /// playhead restored, playback paused.</summary>
    void Restore()
    {
        if (sea != null) sea.SurfaceAll();
        else SectionPlinth.ColumnSinks = null;
        if (water != null) water.Dispose();
        if (seaMoved) { Fx.CenterSea(seaSaved); seaMoved = false; }
        sea = null; rig = null; water = null;
        if (root != null) Destroy(root);
        root = null;
        if (cam != null)
        {
            cam.cullingMask = savedMask; cam.clearFlags = savedClear; cam.backgroundColor = savedBg;
            cam.fieldOfView = savedFov; cam.nearClipPlane = savedNear; cam.farClipPlane = savedFar;
        }
        RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogStartDistance = savedFogStart; RenderSettings.fogEndDistance = savedFogEnd; RenderSettings.fogDensity = savedFogDensity;
        var oc = OrbitCamera.I;
        if (oc != null) { oc.Suspended = false; oc.RestoreView(true); }
        else if (cam != null) cam.transform.SetPositionAndRotation(SavedCamPos, SavedCamRot);
        if (UIManager.I != null) UIManager.I.SetHudVisible(true);
        WorldInput.Unlock("present");
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
        if (Math.Abs(GlobalClock.SongBeatD - savedSongBeat) > 1e-6) GlobalClock.Seek(savedSongBeat);
        Onboarding.Notify(Onboarding.Ev.PresentExited);
    }

    // ================================================================ per frame
    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        switch (phase)
        {
            case Phase.Covering:
                phaseT += dt;
                fadeBase = phaseT / CoverSeconds;
                if (overlay != null) overlay.SetFade(fadeBase);
                if (phaseT >= CoverSeconds) { Swap(); if (phase == Phase.Covering) { phase = Phase.Presenting; phaseT = 0f; } }
                break;
            case Phase.Presenting:
                phaseT += dt;
                if (replaying)
                {
                    replayT += dt;
                    if (replayT < CoverSeconds) fadeBase = replayT / CoverSeconds;
                    else if (replayT - dt < CoverSeconds) { StartPreroll(true); fadeBase = 1f; }
                    else fadeBase = 1f - (replayT - CoverSeconds) / RevealSeconds;
                    if (replayT >= CoverSeconds + RevealSeconds) replaying = false;
                }
                else fadeBase = 1f - phaseT / RevealSeconds;
                if (overlay != null) overlay.SetFade(fadeBase);
                HandleKeys();
                break;
            case Phase.ExitCovering:
                phaseT += dt;
                if (overlay != null) overlay.SetFade(Mathf.Max(overlay.Fade, phaseT / ExitCoverSeconds));
                if (phaseT >= ExitCoverSeconds) { Restore(); phase = Phase.Revealing; phaseT = 0f; }
                break;
            case Phase.Revealing:
                phaseT += dt;
                if (overlay != null) overlay.SetFade(1f - phaseT / ExitRevealSeconds);
                if (phaseT >= ExitRevealSeconds) { if (overlay != null) overlay.Destroy(); overlay = null; phase = Phase.Off; }
                break;
        }
    }

    void HandleKeys()
    {
        if (Time.frameCount <= enterFrame + 1) return;          // the P that entered must not exit in the same frame
        if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetKeyDown(KeyCode.P) && !InputUtil.Cmd)) { BeginExit(); return; }
        if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
    }

    void TogglePause()
    {
        if (clock == ClockMode.Ended) { BeginReplay(); return; }
        paused = !paused;
        if (clock == ClockMode.Live) { if (paused) GlobalClock.Pause(); else { GlobalClock.Play(); liveSince = Time.unscaledTime; } }
    }

    void BeginReplay()
    {
        if (replaying) return;
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
        replaying = true; replayT = 0f;
    }

    void LateUpdate()
    {
        if (sea == null || rig == null || water == null || cam == null) return;
        if (phase != Phase.Presenting && phase != Phase.ExitCovering) return;
        if (!sea.StillValid(SongManager.I)) { if (phase == Phase.Presenting) BeginExit(); return; }
        long a0 = AllocNow();
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        double now = Time.realtimeSinceStartupAsDouble;
        double est = EstimateDsp(now, AudioSettings.dspTime, dt);
        double prevLocal = local;
        AdvanceClock(dt, est);
        bool loops = GlobalClock.LoopSong;
        local = sea.Local(B, loops);
        bool jumped = Math.Abs(local - prevLocal) > 1.0;               // a loop, a replay, a seek: the camera snaps (under the veil)

        bool ksLive = clock == ClockMode.Live && GlobalClock.IsPlaying && !paused && double.IsNaN(ScrubBeat) && Time.unscaledTime - liveSince >= KeyStage.EaseS + 0.1f;
        int crossings = sea.Apply(local, ksLive);
        double bps = Math.Max(0.1, GlobalClock.BeatsPerSecond);
        water.Show(sea, local, bps, loops && B >= sea.TotalBeats ? 0.0 : double.NegativeInfinity);   // a loop's later passes: the first column stayed up
        rig.Display(cam, local, dt, camSnap || jumped, paused);
        camSnap = false;
        Fx.CenterSea(rig.Target.focus);                                 // the sea under the camera wherever the song goes

        // the veil: the phase's fade, and a dip over a looping song's loop point
        if (overlay != null && phase == Phase.Presenting)
        {
            float wrap = loops ? WrapVeil(B, bps) : 0f;
            veilNow = Mathf.Max(Mathf.Clamp01(fadeBase), wrap);
            overlay.SetFade(veilNow);
            double total = sea.TotalBeats;
            double prog = local < 0.0 ? 0.0 : Math.Min(1.0, local / total);
            bool ended = clock == ClockMode.Ended && B >= total + 0.5;
            var sm = SongManager.I;
            int col = sm != null && local >= 0.0 ? sm.ActiveColumn((float)Math.Min(local, total - 1e-3)) : 0;
            overlay.Tick(dt, (float)prog, col, paused, ended, !replaying);
        }

        if (clock == ClockMode.Live && !paused && dt > 1e-4f && dt < 0.05f && !jumped)
        {
            Vector3 v = (cam.transform.position - camPrevPos) / dt;
            if (camSamples >= 2) CamMaxAccel = Mathf.Max(CamMaxAccel, (v - camPrevVel).magnitude / dt);
            CamMaxSpeed = Mathf.Max(CamMaxSpeed, v.magnitude);
            camPrevVel = v; camSamples++;
        }
        else camSamples = 0;
        camPrevPos = cam.transform.position;
        if (capturePath != null) { string p = capturePath; capturePath = null; RenderToPng(cam, p, captureW, captureH); CapturesDone++; a0 = -1; }

        // stats (an island flipping its colliders on / off, or a water pool growing, allocates inside KeyBlock / Unity: excused)
        if (clock == ClockMode.Live && !paused)
        {
            FrameCount++; FrameSecondsSum += Time.unscaledDeltaTime; WorstFrameSeconds = Mathf.Max(WorstFrameSeconds, Time.unscaledDeltaTime);
            long used = AllocNow() - a0;
            if (a0 >= 0)
            {
                if (crossings > 0 || water.Grew) GcFramesExcused++;
                else { GcFramesMeasured++; if (used > 0) { GcFramesAllocating++; GcMaxBytesPerFrame = Math.Max(GcMaxBytesPerFrame, used); } }
            }
        }
    }

    /// <summary>The veil over a looping song's loop point (a pure function of the presentation beat): up over WrapCoverSeconds before it, down over
    /// WrapRevealSeconds after it (not at the first start).</summary>
    double WrapVeilBeat(double bps, out double cover, out double reveal) { cover = WrapCoverSeconds * bps; reveal = WrapRevealSeconds * bps; return sea.TotalBeats; }
    float WrapVeil(double b, double bps)
    {
        double cover, reveal, total = WrapVeilBeat(bps, out cover, out reveal);
        if (b < total - cover) return 0f;
        double k = Math.Round(b / total);
        if (k < 1.0) return 0f;
        double d = b - k * total;
        if (d < 0.0) return d > -cover ? (float)(1.0 + d / cover) : 0f;
        return d < reveal ? (float)(1.0 - d / reveal) : 0f;
    }

    /// <summary>The DSP clock at this instant: AudioSettings.dspTime advances in audio-buffer steps, so the offset to the real-time
    /// clock is tracked by a slowly decaying peak follower (the peak = right after a buffer update); jumps re-seed it.</summary>
    double EstimateDsp(double now, double raw, float dt)
    {
        double off = raw - now;
        if (!dspOffInit || Math.Abs(off - dspOff) > 0.1) { dspOff = off; dspOffInit = true; }
        else { dspOff -= 0.002 * dt; if (off > dspOff) dspOff = off; }
        lastReal = now;
        return now + dspOff;
    }

    void AdvanceClock(float dt, double estDsp)
    {
        double bps = GlobalClock.BeatsPerSecond;
        double total = Math.Max(1.0, sea.TotalBeats);
        if (!double.IsNaN(ScrubBeat) && paused) { B = ScrubBeat; return; }
        if (replaying && clock != ClockMode.Preroll) return;   // the replay's veil is closing: hold until it restarts the pre-roll
        switch (clock)
        {
            case ClockMode.Preroll:
                if (!paused && !replaying) B += dt * bps;
                if (!replaying && !paused && B >= -GlobalClock.PlayLatency * bps)
                {
                    B = -GlobalClock.PlayLatency * bps;
                    GlobalClock.Play(); liveSince = Time.unscaledTime;
                    playCalled = true; clock = ClockMode.Live;   // the DSP estimate stays warm: no re-seed jump at the hand-over
                }
                break;
            case ClockMode.Live:
                if (GlobalClock.IsPlaying)
                {
                    double live = GlobalClock.LoopIndex * total + (estDsp - GlobalClock.DspTimeOfBeat(0.0)) * bps;
                    if (live < B - 2.0) B = live;
                    else if (live > B)
                    {
                        // a small lead (the audio clock's buffer step, e.g. at the pre-roll hand-over) is caught up at no more than 1.5x speed so
                        // nothing jumps; a big one (an editor stall) is taken at once to stay in sync
                        double lead = live - B;
                        B = lead > 0.25 ? live : Math.Min(live, B + dt * bps * 1.5 + 1e-6);
                    }
                }
                else if (!paused && !GlobalClock.LoopSong) { clock = ClockMode.Ended; B = Math.Max(B, total); }   // the song ran out (loop off)
                break;
            case ClockMode.Ended:
                if (B < total + EndTailBeats) B = Math.Min(total + EndTailBeats, B + dt * bps);
                break;
        }
    }

    static long AllocNow()
    {
        if (!probeTried)
        {
            probeTried = true;
            try
            {
                var mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mi != null) allocProbe = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi);
            }
            catch (Exception) { allocProbe = null; }
        }
        return allocProbe != null ? allocProbe() : -1;
    }
}
