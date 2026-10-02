using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package T Play-mode checks (SPEC v4 §7b): the title menu's lowercase words (focus, underline, keys, the wall hop) and the comic
/// panel leave — from Continue, Learn and New → dice end to end (≤ 1.2 s, the drawings in order, a held frame, the impact sticker,
/// 3-4 panels moving on twos and leaving the screen, no particles and no pin leaving its slot, the world and HUD afterwards exactly
/// as v3 left them), Quit (the editor only shakes), LeaveToWorld's callback (once; never after Hide mid-leave) — then, with
/// captures, the leave frozen at seven drawings (Captures/t_leave_*.png) and the menu at rest (t_menu.png).
/// Start with <see cref="Run"/> right after entering Play mode (after SongIO.QuitAutosave = false); poll <see cref="Done"/> or
/// Captures/t_report.txt. Restores the autosave / backups, the tutorial prefs and Onboarding.Suppressed; never writes the user's save.
/// </summary>
public static class V4ChecksT
{
    public static bool Done;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "t_report.txt");
    static StringBuilder sb;
    static int num;

    public static string Run(bool captures = true)
    {
        if (!Application.isPlaying || MainMenu.I == null) return "FAIL needs Play mode (MainMenu missing)";
        Done = false; Report = ""; num = 0;
        try { Directory.CreateDirectory(CaptureDir); if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        MainMenu.I.StartCoroutine(Routine(captures));
        return "started";
    }

    /// <summary>Captures only: the menu at rest and the leave frozen at a few drawings (quick look iteration). <paramref name="which"/>:
    /// 0 continue (the fixture as the newest save), 1 learn (a fresh song: the island rising), 2 new song → dice (from the prompt).</summary>
    public static string Frames(string prefix = "t_leave_", int which = 0)
    {
        if (!Application.isPlaying || MainMenu.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; num = 0;
        MainMenu.I.StartCoroutine(FramesOnly(prefix, which));
        return "started";
    }

    static void Line(bool ok, string what) { num++; sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what).Append('\n'); }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }

    static void Shoot(string file)
    {
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
    }

    static float HudAlpha()
    {
        if (UIManager.I == null || UIManager.I.Canvas == null) return -1f;
        var t = UIManager.I.Canvas.transform.Find("HUD");
        var g = t != null ? t.GetComponent<CanvasGroup>() : null;
        return g != null ? g.alpha : -1f;
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static IEnumerator WaitFor(Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
    }

    static IEnumerator ShowMenu()
    {
        if (!MainMenu.IsShown) MainMenu.Show();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return new WaitForSecondsRealtime(0.35f);
    }

    static void WriteFixtureAutosave()
    {
        File.WriteAllText(SongIO.AutosavePath, File.ReadAllText(V2Checks.FixturePath));
        File.SetLastWriteTimeUtc(SongIO.AutosavePath, DateTime.UtcNow);
    }

    // ------------------------------------------------------------------ one leave, sampled every frame
    class LeaveLog
    {
        public float start = -1f, hidden = -1f, maxDt, captureAt = -1f, swapAt = -1f, hudAt = -1f;
        public int captureDrawing = -1;
        public int frames, confetti0, confettiMax, particlesAfterSwap, maxPanels, minPanelsAfter = 99, drawings, panelFrames, posesAtPanels = -1, posesEnd;
        public bool squashed, heldSeen, impactSeen, stickerAtImpact, stickerAfter, worldCamBeforeSwap, stageCamAfterSwap, capturedOk, captureFailed;
        public float maxDrift, hudBeforeHudDrawing = -1f;
        public string word = "", order = "";
        public Vector2 origin;
        public bool allGoneBeforeHud = true, offScreenAtHud = true, fontOk;
        public int panelCount;
        public readonly List<float> hudSamples = new List<float>();
    }

    /// <summary>Samples a leave already started (phase Leaving) until the menu is hidden: every frame's drawing, panels, sticker,
    /// stage, particles, cameras and HUD.</summary>
    static IEnumerator Sample(LeaveLog L, MenuButton item, float timeout)
    {
        var lv = MainMenu.Transition; var st = MainMenu.Stage; var wc = MainMenu.WorldCamera;
        L.start = L.start < 0f ? Time.realtimeSinceStartup : L.start;
        L.confetti0 = st != null ? st.ConfettiEmitted : 0; L.confettiMax = L.confetti0;
        int lastD = -1; float t0 = Time.realtimeSinceStartup; bool swapped = false;
        while (MainMenu.State == MainMenu.Phase.Leaving && Time.realtimeSinceStartup - t0 < timeout)
        {
            L.frames++;
            L.maxDt = Mathf.Max(L.maxDt, Time.unscaledDeltaTime);
            int d = lv.DrawingNow;
            if (d != lastD) { L.drawings++; L.order += (L.order.Length > 0 ? " " : "") + d; lastD = d; }
            if (item != null && item.Squashed && d <= MenuLeave.HoldAt) L.squashed = true;
            if (lv.Captured && L.captureAt < 0f) { L.captureAt = lv.T; L.capturedOk = true; L.captureDrawing = d; }
            if (lv.CaptureFailed) L.captureFailed = true;
            if (lv.HeldShown && d <= MenuLeave.HoldAt) L.heldSeen = true;
            if (d == MenuLeave.ImpactAt) { if (lv.ImpactShown) L.impactSeen = true; if (lv.StickerShown) L.stickerAtImpact = true; }
            if (d > MenuLeave.ImpactAt + 4 && lv.StickerShown) L.stickerAfter = true;
            L.maxPanels = Mathf.Max(L.maxPanels, lv.PanelsVisible);
            if (d >= MenuLeave.PanelsAt && d < MenuLeave.HudAt) { L.panelFrames++; if (L.posesAtPanels < 0) L.posesAtPanels = lv.PoseUpdates - 1; }
            if (d >= MenuLeave.HudAt) { L.minPanelsAfter = Mathf.Min(L.minPanelsAfter, lv.PanelsVisible); if (lv.PanelsVisible > 0) L.allGoneBeforeHud = false; }
            L.posesEnd = lv.PoseUpdates;
            if (st != null) { L.confettiMax = Mathf.Max(L.confettiMax, st.ConfettiEmitted); if (st.Active) L.maxDrift = Mathf.Max(L.maxDrift, st.MaxPinDrift); }
            bool worldOn = wc != null && wc.enabled;
            if (!swapped && worldOn) { swapped = true; L.swapAt = lv.T; }
            if (swapped)
            {
                if (st != null) L.particlesAfterSwap = Mathf.Max(L.particlesAfterSwap, st.ParticlesAlive);
                if (st != null && st.Cam != null && st.Cam.enabled) L.stageCamAfterSwap = true;
            }
            if (d < MenuLeave.HudAt) L.hudBeforeHudDrawing = Mathf.Max(L.hudBeforeHudDrawing, HudAlpha());
            else L.hudSamples.Add(HudAlpha());
            if (d == MenuLeave.HudAt && L.hudAt < 0f) L.hudAt = lv.T;
            if (d == MenuLeave.HudAt)
                for (int i = 0; i < lv.PanelCount; i++)
                {
                    Rect r = lv.PanelScreenRect(i);
                    if (lv.PanelsVisible > 0 && r.xMax > 0f && r.xMin < Screen.width && r.yMax > 0f && r.yMin < Screen.height) L.offScreenAtHud = false;
                }
            L.word = lv.Word; L.origin = lv.Origin; L.panelCount = lv.PanelCount;
            L.fontOk = lv.WordFont != null && lv.WordFont == Comic.DigitFont;
            yield return null;
        }
        L.hidden = Time.realtimeSinceStartup;
    }

    static string Describe(LeaveLog L)
    {
        return "drawings seen [" + L.order + "], " + L.frames + " frames, worst frame " + (L.maxDt * 1000f).ToString("F0") + " ms, capture at t " + L.captureAt.ToString("F3")
            + " s, world camera at t " + L.swapAt.ToString("F3") + " s, panels " + L.panelCount + " (max visible " + L.maxPanels + "), pose updates " + (L.posesEnd - Mathf.Max(0, L.posesAtPanels))
            + " over " + L.panelFrames + " panel frames, sticker \"" + L.word + "\"";
    }

    static void CheckLeave(string name, LeaveLog L, bool squashExpected)
    {
        float dur = L.hidden - L.start;
        Line(dur <= 1.2f && dur >= 0.95f, name + ": the leave ran end to end in " + dur.ToString("F3") + " s (≤ 1.2 s; " + Describe(L) + ")");
        if (squashExpected) Line(L.squashed, name + ": the clicked word squashed before the held frame");
        Line(L.capturedOk && !L.captureFailed && L.heldSeen && L.captureDrawing >= 0 && L.captureDrawing <= MenuLeave.HoldAt, name + ": the frame was captured before any drawing of the leave and held (captured at t " + L.captureAt.ToString("F3") + " s, drawing " + L.captureDrawing + ", held frame shown " + L.heldSeen + ")");
        Line(L.swapAt >= 0f && L.swapAt <= MenuLeave.ImpactAt * MenuLeave.Drawing + 0.02f && !L.stageCamAfterSwap, name + ": behind the held frame the world camera took over at t " + L.swapAt.ToString("F3") + " s (stage camera off from then on)");
        Line(L.impactSeen && L.stickerAtImpact && !L.stickerAfter && L.fontOk, name + ": impact drawing shown with the lettered sticker \"" + L.word + "\" (Bangers " + L.fontOk + "), gone after 5 drawings");
        Line(L.panelCount >= 3 && L.panelCount <= 4 && L.maxPanels == L.panelCount, name + ": the frame was cut into " + L.panelCount + " panels");
        int poses = L.posesEnd - Mathf.Max(0, L.posesAtPanels);
        Line(!Look.OnTwos || (poses <= (MenuLeave.HudAt - MenuLeave.PanelsAt) + 3 && L.panelFrames > poses), name + ": panels move on twos (" + poses + " pose updates over " + L.panelFrames + " frames of the panel phase)");
        Line(L.allGoneBeforeHud && L.offScreenAtHud, name + ": every panel had left the screen by the HUD drawing");
        Line(L.confettiMax == L.confetti0 && L.particlesAfterSwap == 0 && L.maxDrift < 0.001f, name + ": no particles (confetti emitted +" + (L.confettiMax - L.confetti0) + ", stage particles after the swap " + L.particlesAfterSwap + "), no pin left its slot (max drift " + L.maxDrift.ToString("F4") + " u)");
        if (L.hudBeforeHudDrawing >= 0f)
        {
            string hs = ""; for (int i = 0; i < Mathf.Min(8, L.hudSamples.Count); i++) hs += L.hudSamples[i].ToString("F2") + " ";
            Line(L.hudBeforeHudDrawing < 0.05f, name + ": the HUD stayed hidden until drawing " + MenuLeave.HudAt + " (max alpha before " + L.hudBeforeHudDrawing.ToString("F2") + "), then came in: " + hs);
        }
    }

    static void CheckWorldAfter(string name, bool fogWorld)
    {
        var wc = MainMenu.WorldCamera; var st = MainMenu.Stage; var lv = MainMenu.Transition;
        bool ok = MainMenu.State == MainMenu.Phase.Hidden && !MainMenu.IsShown && wc != null && wc.enabled && st != null && !st.Active && !st.Cam.enabled
            && !WorldInput.IsLockedBy("menu") && (OrbitCamera.I == null || !OrbitCamera.I.Suspended) && !MainMenu.MenuCanvas.gameObject.activeSelf
            && lv != null && !lv.Active && !lv.Canvas.gameObject.activeSelf && lv.Frame == null && !MainMenu.Music.Running && !MainMenu.LastLeaveStacked
            && RenderSettings.fog == fogWorld;
        Line(ok, name + ": after the leave the world is as v3 left it (hidden, world camera on, stage off, input free (" + WorldInput.Describe() + "), orbit camera live, menu + leave canvases off, frame released, menu music stopped, fog " + RenderSettings.fog + " = world's " + fogWorld + ")");
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder();
        string userSave = Md5(SongIO.Path);
        var saves = V3Fixes.SnapshotSaves();
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        bool suppressed = Onboarding.Suppressed;
        Onboarding.Suppressed = true;
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float worstLeaveFrame = 0f;

        // the world's fog (the menu turns it off while its stage is on screen)
        MainMenu.Hide(); WorldInput.Unlock("prompt");
        yield return null; yield return null;
        bool fogWorld = RenderSettings.fog;

        // ---- 1. the words
        yield return ShowMenu();
        var canvas = MainMenu.MenuCanvas;
        var btns = canvas.GetComponentsInChildren<MenuButton>(true);
        int texts = 0, lower = 0, pills = 0;
        foreach (var t in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) texts++;
        foreach (var b in btns) { if (b.IsText && b.label != null && b.label.text == b.label.text.ToLowerInvariant() && b.label.font == Comic.Font) lower++; if (b.fill is ComicShape) pills++; }
        // v5 (package T): a fifth word, "gallery", between new song and learn (always built, shown while the gallery has songs; its shelf has
        // its own canvas, so the menu canvas still holds the words only)
        int nWords = MainMenu.Words.Length;
        Line(nWords == 5 && btns.Length == nWords && lower == nWords && pills == 0 && texts == nWords, "menu: five lowercase Fredoka words (v5: + gallery), no pills (" + btns.Length + " items, " + lower + " lowercase text items, " + pills + " pills, " + texts + " texts): "
             + (MainMenu.ContinueButton != null ? MainMenu.ContinueButton.label.text : "?") + " / " + MainMenu.NewButton.label.text + " / " + MainMenu.GalleryButton.label.text + " / " + MainMenu.LearnButton.label.text + " / " + MainMenu.QuitButton.label.text);
        yield return new WaitForSecondsRealtime(0.4f);
        var f0 = MainMenu.FocusedButton;
        int underlined = 0; foreach (var b in btns) if (b.UnderlineDrawn > 0f) underlined++;
        Line(f0 != null && f0.Focused && Mathf.Approximately(f0.UnderlineDrawn, 1f) && underlined == 1, "focus: one word focused (" + (f0 != null ? f0.label.text : "none") + ") wearing the whole wavy underline; " + underlined + " underlined");
        // the words sit under the title's D, below the word
        float md; int lc; Rect word = MainMenu.Stage.WordScreenRect(out md, out lc);
        Rect first = ScreenRect(btns[0].gameObject.activeSelf ? btns[0].label.rectTransform : btns[1].label.rectTransform);
        Line(Mathf.Abs(first.xMin - word.xMin) < Screen.width * 0.03f && first.yMax < word.yMin, "layout: the words are left-aligned under the D (word x " + word.xMin.ToString("F0") + " px, first word x " + first.xMin.ToString("F0") + " px, below the title " + (first.yMax < word.yMin) + ")");
        // keys: ↓ moves the focus (KeyShim, the menu's key path), MoveFocus back
        int fi0 = f0 != null ? f0.index : -1;
        KeyShim.Sim(KeyCode.DownArrow, true, true, false); yield return null; KeyShim.Clear(); yield return null;
        var f1 = MainMenu.FocusedButton;
        bool f1Only = f1 != null && f1.Focused && (f0 == null || !f0.Focused);
        MainMenu.MoveFocus(-1); yield return null;
        var f2 = MainMenu.FocusedButton;
        Line(f1Only && f1.index != fi0 && f2 != null && f2.index == fi0, "keys: ↓ moved the focus " + fi0 + " → " + (f1 != null ? f1.index : -1) + ", ↑ back → " + (f2 != null ? f2.index : -1));
        // the wall behind the focused word hops on the beat
        int hops0 = MainMenu.Stage.Hops;
        yield return new WaitForSecondsRealtime(1.6f);
        Line(MainMenu.Stage.Hops - hops0 >= 2, "wall: the cubes behind the focused word hopped " + (MainMenu.Stage.Hops - hops0) + " times in 1.6 s (90 BPM)");
        // the glass cubes, liquid pixellated: drawn into a 1/4-screen point-filtered buffer every frame, never by the stage camera,
        // shown nearest-upscaled on the overlay between the stage and the menu canvas
        var stg = MainMenu.Stage;
        int glassRenderers = 0, glassOff = 0, pixelMats = 0;
        foreach (Transform ch in stg.transform)
        {
            if (!ch.name.StartsWith("GlassCube")) continue;
            var mr = ch.GetComponent<MeshRenderer>(); if (mr == null) continue;
            glassRenderers++; if (!mr.enabled) glassOff++;
            if (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Pixel") && mr.sharedMaterial.GetFloat("_Pixel") > 0.5f) pixelMats++;
        }
        int draws0 = stg.GlassDraws; int frames0 = Time.frameCount;
        yield return new WaitForSecondsRealtime(0.3f);
        int drawn = stg.GlassDraws - draws0, framesN = Time.frameCount - frames0;
        var gt = stg.GlassTexture;
        bool sizeOk = gt != null && Mathf.Abs(gt.width - Screen.width / (float)MenuStage.GlassPixelScale) <= 1f && Mathf.Abs(gt.height - Screen.height / (float)MenuStage.GlassPixelScale) <= 1f && gt.filterMode == FilterMode.Point;
        var gc = stg.GlassCanvas;
        Line(glassRenderers == 3 && glassOff == 3 && pixelMats == 3 && sizeOk && drawn >= framesN - 1 && gc != null && gc.gameObject.activeInHierarchy && gc.sortingOrder > 10 && gc.sortingOrder < MainMenu.SortOrder,
             "glass: the three cubes are drawn only into the pixel buffer (" + glassOff + "/" + glassRenderers + " renderers off, pixel mode " + pixelMats + "), " + (gt != null ? gt.width + "x" + gt.height : "none") + " point-filtered (1/" + MenuStage.GlassPixelScale + " of " + Screen.width + "x" + Screen.height + "), redrawn " + drawn + " times in " + framesN + " frames, overlay order " + (gc != null ? gc.sortingOrder : -1) + " (stage < it < menu " + MainMenu.SortOrder + ")");
        if (captures)
        {
            MainMenu.SimulatePointer(new Vector2(Screen.width * 0.86f, Screen.height * 0.12f)); yield return new WaitForSecondsRealtime(0.6f); Shoot("t_menu.png"); yield return new WaitForSecondsRealtime(0.5f);
            Shoot("t_glass_a.png"); yield return new WaitForSecondsRealtime(0.7f); Shoot("t_glass_b.png"); yield return new WaitForSecondsRealtime(0.5f);
            MainMenu.ReleasePointer();
        }

        // ---- 2. Continue (the fixture as the newest save)
        WriteFixtureAutosave();
        int hits0 = MainMenu.Music.LeaveHits;
        var LC = new LeaveLog();
        Synth.StartRecording(1.6f);   // the impact chord should land on the impact drawing (~0.17 s)
        LC.start = Time.realtimeSinceStartup;
        MainMenu.ContinueButton.Invoke();
        yield return Sample(LC, MainMenu.ContinueButton, 4f);
        yield return WaitFor(() => Synth.RecordingDone, 2f);
        if (Synth.RecordingDone) { Synth.SaveRecording(Path.Combine(CaptureDir, "t_leave_audio.wav")); Info("recorded the Continue leave: Captures/t_leave_audio.wav (starts at the click)"); }
        worstLeaveFrame = Mathf.Max(worstLeaveFrame, LC.maxDt);
        CheckLeave("continue", LC, true);
        Line(Vector2.Distance(LC.origin, MainMenu.ContinueButton.ScreenCenter) < 60f, "continue: the impact point is the clicked word (" + LC.origin + " vs its centre " + MainMenu.ContinueButton.ScreenCenter + ")");
        Line(MainMenu.Music.LeaveHits == hits0 + 1, "continue: the impact chord was scheduled once (" + (MainMenu.Music.LeaveHits - hits0) + ")");
        int risingNow = 0; foreach (var kb in SongManager.I.Islands) if (kb != null && kb.Rising) risingNow++;
        Line(MainMenu.IslandsRisen == 0 && risingNow == 0, "continue: a song with cubes stays put (islands risen " + MainMenu.IslandsRisen + ", rising now " + risingNow + ")");
        yield return new WaitForSecondsRealtime(0.8f);
        var sm = SongManager.I;
        Line(sm.Islands.Count == 6 && SequenceMaster.Cubes.Count == 13 && Mathf.RoundToInt(GlobalClock.BPM) == 121, "continue: the newest save is loaded (" + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes, " + GlobalClock.BPM + " bpm)");
        CheckWorldAfter("continue", fogWorld);
        float hudC = HudAlpha();
        if (hudC >= 0f) Line(hudC > 0.9f, "continue: HUD visible 0.8 s later (alpha " + hudC.ToString("F2") + ")");

        // ---- 3. Learn (an offline dice song: its island rises behind the panels)
        yield return ShowMenu();
        var LL = new LeaveLog();
        MainMenu.LearnButton.Invoke();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Leaving || MainMenu.State == MainMenu.Phase.Hidden, 6f);
        LL.start = Time.realtimeSinceStartup - (MainMenu.Transition.Active ? MainMenu.Transition.T : 0f);
        KeyBlock island0 = sm.Islands.Count > 0 ? sm.Islands[0] : null;
        float minY = float.MaxValue;
        var watchRise = MainMenu.I.StartCoroutine(WatchY(island0, v => minY = Mathf.Min(minY, v)));
        yield return Sample(LL, MainMenu.LearnButton, 4f);
        worstLeaveFrame = Mathf.Max(worstLeaveFrame, LL.maxDt);
        CheckLeave("learn", LL, true);
        bool risingAtEnd = island0 != null && island0.Rising;
        yield return new WaitForSecondsRealtime(0.8f);
        MainMenu.I.StopCoroutine(watchRise);
        float restY = island0 != null ? island0.transform.position.y : 0f;
        Line(MainMenu.IslandsRisen >= 1 && minY < restY - 1f && risingAtEnd && island0 != null && !island0.Rising,
             "learn: the fresh island rose from the sea behind the panels (" + MainMenu.IslandsRisen + " risen, lowest y " + minY.ToString("F2") + " vs rest " + restY.ToString("F2") + ", still rising at the end " + risingAtEnd + ", landed after)");
        CheckWorldAfter("learn", fogWorld);

        // ---- 4. New → prompt → dice → leave from the dice
        yield return ShowMenu();
        MainMenu.NewButton.Invoke();
        yield return WaitFor(() => InterfaceController.I != null && InterfaceController.I.Visible, 3f);
        yield return new WaitForSecondsRealtime(0.4f);
        var ic = InterfaceController.I;
        Vector2 dicePt = ic != null && ic.DiceButton != null ? UIKit.ScreenOf(ic.DiceButton) : Vector2.zero;
        var LN = new LeaveLog();
        if (ic != null && ic.DiceButton != null) ic.DiceButton.onClick?.Invoke();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Leaving || MainMenu.State == MainMenu.Phase.Hidden, 6f);
        LN.start = Time.realtimeSinceStartup - (MainMenu.Transition.Active ? MainMenu.Transition.T : 0f);
        yield return Sample(LN, null, 4f);
        worstLeaveFrame = Mathf.Max(worstLeaveFrame, LN.maxDt);
        CheckLeave("new song (dice)", LN, false);
        Line(Vector2.Distance(LN.origin, dicePt) < 4f && LN.word == "POP!", "new song: the leave starts at the dice (" + LN.origin + " vs " + dicePt + ") with \"" + LN.word + "\"");
        yield return new WaitForSecondsRealtime(0.8f);
        CheckWorldAfter("new song", fogWorld);
        Line(ic == null || !ic.Visible, "new song: the prompt is closed");

        // ---- 5. Quit (the editor only shakes)
        yield return ShowMenu();
        MainMenu.QuitButton.Invoke();
        yield return new WaitForSecondsRealtime(0.5f);
        Line(MainMenu.State == MainMenu.Phase.Shown && !MainMenu.Transition.Active, "quit (editor): no leave, the word shakes (state " + MainMenu.State + ")");

        // ---- 6a. a hitch as the leave starts (a fresh song build does this right before the dice's leave): the clock may not skip
        // the capture, the held frame or the impact, and must never stall
        int hcalls = 0;
        MainMenu.LeaveToWorld(new Vector2(Screen.width * 0.6f, Screen.height * 0.45f), () => hcalls++);
        System.Threading.Thread.Sleep(320);
        var LH = new LeaveLog(); LH.start = Time.realtimeSinceStartup - 0.32f;
        yield return Sample(LH, null, 4f);
        yield return null;
        float hdur = LH.hidden - LH.start;
        Line(hcalls == 1 && MainMenu.State == MainMenu.Phase.Hidden && LH.capturedOk && LH.captureDrawing <= MenuLeave.HoldAt && LH.heldSeen && LH.impactSeen && hdur < 1.6f,
             "hitch: after a 0.32 s stall as the leave starts, the frame is still captured before any drawing of the leave (drawing " + LH.captureDrawing + "), held (" + LH.heldSeen + "), the impact drawn (" + LH.impactSeen + "), and the leave ends " + hdur.ToString("F3") + " s after the click, its action run " + hcalls + " time(s)");
        yield return ShowMenu();

        // ---- 6. LeaveToWorld: Hide mid-leave never runs the callback; a full leave runs it once
        int calls = 0;
        MainMenu.LeaveToWorld(new Vector2(Screen.width * 0.7f, Screen.height * 0.4f), () => calls++);
        yield return WaitFor(() => MainMenu.Transition.DrawingNow >= 5 || MainMenu.State != MainMenu.Phase.Leaving, 2f);
        MainMenu.Hide();
        yield return null;
        var lv = MainMenu.Transition;
        Line(calls == 0 && !MainMenu.IsShown && MainMenu.WorldCamera.enabled && !MainMenu.Stage.Cam.enabled && !WorldInput.IsLockedBy("menu") && !lv.Active && !lv.Canvas.gameObject.activeSelf && lv.Frame == null,
             "Hide() mid-leave: the world back at once, the leave gone and its frame released, the pending action not run (calls " + calls + ")");
        yield return ShowMenu();
        MainMenu.LeaveToWorld(() => calls++);
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 3f);
        yield return null;
        Line(calls == 1 && Vector2.Distance(lv.Origin, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)) < 2f, "LeaveToWorld(after): runs its action once at the end (calls " + calls + "), from the screen centre");
        Info("worst frame during the leaves " + (worstLeaveFrame * 1000f).ToString("F0") + " ms; screen " + Screen.width + "x" + Screen.height + "; capture flipped " + lv.Flipped);

        // ---- 7. captures: the Continue leave frozen at single drawings
        if (captures)
        {
            yield return ShowMenu();
            WriteFixtureAutosave();
            yield return CaptureSequence("t_leave_", 0);
        }

        // ---- 8. audio and the user's save
        Line(Synth.LateEvents == late0 && Synth.Errors == err0, "synth: late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " over the run");
        Line(Md5(SongIO.Path) == userSave, "the user's save is untouched (md5 " + userSave + ")");

        // ---- restore
        MenuLeave.FreezeAt = null; MenuLeave.FreezeWorld = false;
        GlobalClock.Stop();
        if (Onboarding.Active) Onboarding.Skip();
        V3Fixes.RestoreSaves(saves);
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        MainMenu.ReleasePointer();
        Finish();
    }

    static IEnumerator WatchY(KeyBlock kb, Action<float> onY)
    {
        while (kb != null) { onY(kb.transform.position.y); yield return null; }
    }

    static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, c[0]), b = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>The seven drawings worth looking at: squash, hold, impact, crack, panels mid-flight, late, HUD.</summary>
    static readonly float[] FrameTimes = { 0.04f, 0.125f, 0.2f, 0.29f, 0.46f, 0.63f, 0.9f };

    /// <summary>The leave from <paramref name="which"/> (0 continue, 1 learn, 2 new song → dice) frozen at <see cref="FrameTimes"/>; the world's
    /// scaled time stops with it (Time.timeScale 0 while frozen) so the island rising behind the panels is caught in step.</summary>
    static IEnumerator CaptureSequence(string prefix, int which)
    {
        MenuLeave.FreezeAt = FrameTimes[0];
        if (which == 1) MainMenu.LearnButton.Invoke();
        else if (which == 2)
        {
            MainMenu.NewButton.Invoke();
            yield return WaitFor(() => InterfaceController.I != null && InterfaceController.I.Visible, 3f);
            yield return new WaitForSecondsRealtime(0.5f);
            MenuLeave.FreezeAt = FrameTimes[0];   // renew the lease: the prompt took a moment
            if (InterfaceController.I != null && InterfaceController.I.DiceButton != null) InterfaceController.I.DiceButton.onClick?.Invoke();
        }
        else MainMenu.ContinueButton.Invoke();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Leaving, 6f);
        MenuLeave.FreezeWorld = true;   // the world's time stops with the pinned leave (MainMenu applies and restores it; leased)
        for (int i = 0; i < FrameTimes.Length; i++)
        {
            MenuLeave.FreezeAt = FrameTimes[i];
            var lv = MainMenu.Transition;
            yield return WaitFor(() => lv.T >= FrameTimes[i] - 1e-4f, 2f);
            yield return null; yield return null; yield return null;
            string file = prefix + i + "_d" + lv.DrawingNow + ".png";
            Shoot(file);
            Info("capture " + file + " at leave t " + lv.T.ToString("F3") + " (drawing " + lv.DrawingNow + ", held " + lv.HeldShown + ", panels " + lv.PanelsVisible + ", sticker " + lv.StickerShown + ")");
            yield return new WaitForSecondsRealtime(0.45f);
        }
        MenuLeave.FreezeWorld = false;
        MenuLeave.FreezeAt = null;
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 3f);
    }

    static IEnumerator FramesOnly(string prefix, int which)
    {
        sb = new StringBuilder();
        var saves = V3Fixes.SnapshotSaves();
        string userSave = Md5(SongIO.Path);
        bool suppressed = Onboarding.Suppressed; Onboarding.Suppressed = true;
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        yield return ShowMenu();
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.86f, Screen.height * 0.12f));
        yield return new WaitForSecondsRealtime(0.5f);
        Shoot(prefix + "menu.png");
        yield return new WaitForSecondsRealtime(0.5f);
        MainMenu.ReleasePointer();
        WriteFixtureAutosave();
        yield return CaptureSequence(prefix, which);
        GlobalClock.Stop();
        if (Onboarding.Active) Onboarding.Skip();
        V3Fixes.RestoreSaves(saves);
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        Line(Md5(SongIO.Path) == userSave, "the user's save is untouched");
        Finish();
    }

    static void Finish()
    {
        int pass = 0, fail = 0;
        foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Insert(0, "V4ChecksT: " + pass + " PASS, " + fail + " FAIL\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true;
    }

    static void RestorePref(string key, int v) { if (v < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, v); }
}
