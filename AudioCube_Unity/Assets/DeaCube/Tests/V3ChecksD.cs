using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package D Play-mode checks (SPEC v3 §5): the boot menu, the wall's pointer life, the menu audio, the menu flows (Hide with no
/// song, New → prompt → dice → world + tutorial, home → autosave + menu, Continue → newest save), every tutorial step and its
/// bubble geometry, skip / resume / suppression, and the captures. Start with <see cref="Run"/> right after entering Play mode
/// (the boot state is read first), poll <see cref="Done"/> / <see cref="Report"/>. Leaves the user's save untouched, restores the
/// tutorial PlayerPrefs and any previous autosave. Numbered PASS / FAIL / INFO lines.
/// </summary>
public static class V3ChecksD
{
    public static bool Done;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    static int num;

    public static string Run(bool captures = true)
    {
        if (!Application.isPlaying || MainMenu.I == null) return "FAIL needs Play mode (MainMenu missing)";
        Done = false; Report = ""; num = 0;
        MainMenu.I.StartCoroutine(Routine(captures));
        return "started";
    }

    /// <summary>Menu look iteration: shows the menu, puts the synthetic pointer at (fx, fy) of the screen, captures after a delay.</summary>
    public static string LookInfo = "";
    public static string Look(float fx, float fy, string file, float delay)
    {
        if (MainMenu.I == null) return "no menu";
        if (!MainMenu.IsShown) MainMenu.Show();
        MainMenu.I.StartCoroutine(LookRoutine(fx, fy, file, delay, false));
        return "queued";
    }
    /// <summary>Replays the boot intro and captures <paramref name="file"/> <paramref name="delay"/> s into it.</summary>
    public static string Reveal(string file, float delay)
    {
        if (MainMenu.I == null) return "no menu";
        if (!MainMenu.IsShown) MainMenu.Show();
        MainMenu.I.StartCoroutine(LookRoutine(0.9f, 0.1f, file, delay, true));
        return "queued";
    }
    static IEnumerator LookRoutine(float fx, float fy, string file, float delay, bool replay)
    {
        yield return null;   // in the player loop: Screen is the Game view
        MainMenu.SimulatePointer(new Vector2(Screen.width * fx, Screen.height * fy));
        if (replay) MainMenu.ReplayIntro();
        yield return new WaitForSecondsRealtime(delay);
        Shoot(file);
        var st = MainMenu.Stage;
        float md = 0f; int lc = 0;
        Rect wr = st != null ? st.WordScreenRect(out md, out lc) : new Rect();
        LookInfo = file + ": screen " + Screen.width + "x" + Screen.height + ", pins " + (st != null ? st.Count : 0) + ", word " + wr + " min depth " + md.ToString("F2") + " letters risen " + (st != null ? st.LettersRisen : 0) + ", reveal t " + (st != null ? st.RevealTime.ToString("F2") : "-") + ", state " + MainMenu.State;
    }

    public static string FrameInfo = "";
    /// <summary>Average / worst frame time over <paramref name="seconds"/> of the shown menu (poll FrameInfo).</summary>
    public static string MeasureMenuFrames(float seconds)
    {
        if (MainMenu.I == null) return "no menu";
        FrameInfo = "";
        MainMenu.I.StartCoroutine(FrameRoutine(seconds));
        return "measuring";
    }
    static IEnumerator FrameRoutine(float seconds)
    {
        yield return null;
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.3f, Screen.height * 0.3f));
        float t0 = Time.realtimeSinceStartup, worst = 0f; int frames = 0;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            yield return null;
            frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            float u = (Time.realtimeSinceStartup - t0) / seconds;
            MainMenu.SimulatePointer(new Vector2(Screen.width * (0.2f + 0.6f * u), Screen.height * (0.25f + 0.1f * Mathf.Sin(u * 12f))));
        }
        float avg = (Time.realtimeSinceStartup - t0) / Mathf.Max(1, frames);
        FrameInfo = "menu " + Screen.width + "x" + Screen.height + ": " + frames + " frames in " + seconds + " s, avg " + (avg * 1000f).ToString("F1") + " ms (" + (1f / avg).ToString("F0") + " fps), worst " + (worst * 1000f).ToString("F1") + " ms, pins " + (MainMenu.Stage != null ? MainMenu.Stage.Count : 0) + ", property-block pins now " + (MainMenu.Stage != null ? MainMenu.Stage.PropertyBlockPins : 0);
    }

    static void Line(StringBuilder sb, bool ok, string what) { num++; sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what).Append('\n'); }
    static void Info(StringBuilder sb, string what) { sb.Append("INFO ").Append(what).Append('\n'); }

    static void Shoot(string file)
    {
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 2);
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

    static IEnumerator Routine(bool captures)
    {
        var sb = new StringBuilder();
        // ---- keep what we touch
        string userSave = Md5(SongIO.Path);
        string autoBackup = null;
        bool hadAuto = File.Exists(SongIO.AutosavePath);
        if (hadAuto) { autoBackup = Path.Combine(Application.temporaryCachePath, "deacube_autosave_backup.json"); File.Copy(SongIO.AutosavePath, autoBackup, true); }
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        bool suppressed = Onboarding.Suppressed;
        string[] tips = Onboarding.AllTips;   // v5 / v6: the newer tips too
        var tipPrefs = new int[tips.Length];
        for (int i = 0; i < tips.Length; i++) tipPrefs[i] = PlayerPrefs.GetInt(Onboarding.PrefTip + tips[i], -1);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        var events = new List<string>();
        Action<string> listen = e => events.Add(e);
        Onboarding.OnEvent += listen;
        Onboarding.Suppressed = true;   // no bubbles until the tutorial part
        var stage = MainMenu.Stage;

        // ---- 1. boot state
        bool bootShown = MainMenu.IsShown;
        if (!bootShown) { Info(sb, "menu was not up at the start (hidden by an earlier test): showing it"); MainMenu.Show(); yield return null; }
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown && MainMenu.Stage != null && MainMenu.Stage.RevealDone, 7f);
        yield return new WaitForSecondsRealtime(0.4f);
        stage = MainMenu.Stage;
        var wc = MainMenu.WorldCamera;
        Line(sb, MainMenu.IsShown && MainMenu.State == MainMenu.Phase.Shown, "boot: menu shown (state " + MainMenu.State + ", shown at boot " + bootShown + ")");
        Line(sb, WorldInput.IsLockedBy("menu") && WorldInput.KeysLocked, "boot: WorldInput locked by the menu, keys too (" + WorldInput.Describe() + ")");
        Line(sb, wc != null && !wc.enabled, "boot: world camera off (" + (wc != null ? wc.name : "null") + ")");
        Line(sb, stage != null && stage.Cam != null && stage.Cam.enabled && stage.Cam.cullingMask == (1 << DeaLayers.Menu), "boot: menu camera on, culling = Menu layer " + DeaLayers.Menu + " only (mask " + (stage != null && stage.Cam != null ? stage.Cam.cullingMask : 0) + ")");
        Line(sb, HudAlpha() >= 0f && HudAlpha() < 0.05f, "boot: HUD hidden (alpha " + HudAlpha().ToString("F2") + ")");
        Line(sb, InterfaceController.I == null || !InterfaceController.I.Visible, "boot: no vibe prompt auto-shown");
        Line(sb, MainMenu.MenuCanvas != null && UIManager.I != null && MainMenu.MenuCanvas.sortingOrder > UIManager.I.Canvas.sortingOrder && InterfaceController.I != null && InterfaceController.I.Canvas.sortingOrder > MainMenu.MenuCanvas.sortingOrder,
            "canvas order: HUD " + (UIManager.I != null ? UIManager.I.Canvas.sortingOrder : -1) + " < menu " + MainMenu.MenuCanvas.sortingOrder + " < prompt " + (InterfaceController.I != null && InterfaceController.I.Canvas != null ? InterfaceController.I.Canvas.sortingOrder : -1) + " < bubbles " + TutorialBubble.SortOrder);
        var camData = stage != null ? UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(stage.Cam) : null;
        Line(sb, camData != null && camData.renderPostProcessing, "menu camera renders post-processing (bloom)");
        Info(sb, "screen " + Screen.width + "x" + Screen.height + ", wall pins " + (stage != null ? stage.Count : 0) + ", continue shown " + (MainMenu.ContinueButton != null && MainMenu.ContinueButton.gameObject.activeSelf) + " (any save " + SongIO.AnyExists + ")");
        // the title lives in the wall: no text wordmark, the word's pins stand out, centred and big enough to read
        // (v5: the menu's texts are its words — four in v4, five with package T's "gallery" (MainMenu.Words, hidden ones included); the
        // gallery shelf lives on its own canvas — none outside the words, none spelling the title)
        var menuTexts = MainMenu.MenuCanvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        int menuWords = MainMenu.Words.Length, stray = 0; string strayNames = "";
        foreach (var tx in menuTexts)
            if ((tx.text ?? "").ToLowerInvariant().Contains("deacube") || tx.GetComponentInParent<MenuButton>(true) == null) { stray++; strayNames += tx.name + " "; }
        bool noText = menuTexts.Length == menuWords && menuWords >= 4 && stray == 0;
        float minDepth; int letterPins;
        Rect word = stage.WordScreenRect(out minDepth, out letterPins);
        Line(sb, noText && letterPins == MenuStage.LetterPinCount && letterPins > 100, "title: DEACUBE spelled by " + letterPins + " wall pins, no text wordmark (menu texts " + menuTexts.Length + " = the " + menuWords + " menu words" + (stray > 0 ? "; stray texts: " + strayNames : "") + ")");
        Line(sb, minDepth >= 1.0f, "title: every letter pin stands out of the wall (min " + minDepth.ToString("F2") + " u, plain wall base <= 0.65 u)");
        float cx = word.center.x / Screen.width;
        Line(sb, Mathf.Abs(cx - 0.5f) < 0.03f && word.height / Screen.height > 0.12f && word.width / Screen.width < 0.92f && word.center.y / Screen.height > 0.5f,
            "title: centred (x " + cx.ToString("F3") + "), " + (100f * word.height / Screen.height).ToString("F0") + "% of the screen height, " + (100f * word.width / Screen.width).ToString("F0") + "% of the width, in the upper half (y " + (word.center.y / Screen.height).ToString("F2") + ")");
        Line(sb, MainMenu.Music.NameNotes >= 7, "reveal: the title played its name (" + MainMenu.Music.NameNotes + " notes: D E A C tick B E)");
        if (captures) { MainMenu.SimulatePointer(new Vector2(Screen.width * 0.9f, Screen.height * 0.1f)); yield return new WaitForSecondsRealtime(1.0f); Shoot("d_menu.png"); yield return new WaitForSecondsRealtime(0.4f); }
        if (captures)
        {
            MainMenu.ReplayIntro();
            yield return new WaitForSecondsRealtime(2.05f);
            int risenMid = stage.LettersRisen;
            Shoot("d_menu_reveal.png");
            Info(sb, "d_menu_reveal.png at reveal t " + stage.RevealTime.ToString("F2") + " s, letters risen " + risenMid);
            yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown && stage.RevealDone, 6f);
            yield return new WaitForSecondsRealtime(0.3f);
        }

        // ---- 2. the wall under a synthetic pointer
        Vector2 P = new Vector2(Screen.width * 0.2f, Screen.height * 0.2f);
        int touches0 = stage.Touches, notes0 = MainMenu.Music.NotesScheduled;
        MainMenu.SimulatePointer(P);
        yield return new WaitForSecondsRealtime(0.7f);
        float nearMax, farMax; Vector2 arg;
        float rpx = Mathf.Max(40f, Screen.height * 0.09f);
        stage.Probe(P, rpx, out nearMax, out farMax, out arg);
        Line(sb, nearMax > 0.5f && farMax < 0.2f, "pointer: pins near the pointer extrude " + nearMax.ToString("F2") + " u, far ones " + farMax.ToString("F2") + " u");
        Line(sb, (arg - P).magnitude < rpx * 1.4f, "pointer: the most extruded pin is " + (arg - P).magnitude.ToString("F0") + " px from the pointer");
        // sweep for the wake (and the hover capture)
        Vector2 A = new Vector2(Screen.width * 0.08f, Screen.height * 0.16f), B = new Vector2(Screen.width * 0.34f, Screen.height * 0.26f);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.75f) { float u = (Time.realtimeSinceStartup - t0) / 0.75f; MainMenu.SimulatePointer(Vector2.Lerp(A, B, Ease.InOutCubic(u))); yield return null; }
        MainMenu.SimulatePointer(B);
        float wakeMid; float dummy; Vector2 arg2;
        stage.Probe(Vector2.Lerp(A, B, 0.55f), rpx, out wakeMid, out dummy, out arg2);
        Line(sb, wakeMid > 0.15f, "wake: pins the pointer passed are still out " + wakeMid.ToString("F2") + " u right after it moved on");
        if (captures) { Shoot("d_menu_hover.png"); }
        yield return new WaitForSecondsRealtime(0.3f);
        Line(sb, stage.Touches > touches0 && MainMenu.Music.NotesScheduled > notes0, "touch: " + (stage.Touches - touches0) + " pins flashed, " + (MainMenu.Music.NotesScheduled - notes0) + " notes queued (dropped " + MainMenu.Music.NotesDropped + ", max " + MainMenu.Music.MaxPerSecond + "/s)");
        Line(sb, MainMenu.Music.MaxPerSecond <= 8, "touch notes never exceed 8 per second (max " + MainMenu.Music.MaxPerSecond + ")");
        // decay after the pointer leaves
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.95f, Screen.height * 0.95f));
        yield return new WaitForSecondsRealtime(1.2f);
        float after, far2; Vector2 arg3;
        stage.Probe(P, rpx, out after, out far2, out arg3);
        Line(sb, after < 0.1f, "spring: after the pointer left, the pins at the old spot settled back (" + after.ToString("F2") + " u)");
        // the letters ring under the pointer but always come back to their pose
        int ln0 = MainMenu.Music.LetterNotes;
        float wx0 = word.xMin + word.width * 0.05f, wx1 = word.xMax - word.width * 0.05f, wy = word.center.y;
        float t2 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t2 < 1.2f) { float u = (Time.realtimeSinceStartup - t2) / 1.2f; MainMenu.SimulatePointer(new Vector2(Mathf.Lerp(wx0, wx1, u), wy)); yield return null; }
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.95f, Screen.height * 0.05f));
        yield return new WaitForSecondsRealtime(1.4f);
        float md2; int lp2; stage.WordScreenRect(out md2, out lp2);
        Line(sb, MainMenu.Music.LetterNotes - ln0 >= 5 && md2 >= 1.0f, "letters: the pointer crossing the word played " + (MainMenu.Music.LetterNotes - ln0) + " letter notes; afterwards every letter is back out (min " + md2.ToString("F2") + " u)");

        // ---- 3. click ripple + strum
        int hits0 = stage.RippleHits;
        MainMenu.SimulateClick(new Vector2(Screen.width * 0.7f, Screen.height * 0.3f));
        yield return new WaitForSecondsRealtime(0.8f);
        Line(sb, stage.RippleHits - hits0 > 150, "click: the ripple reached " + (stage.RippleHits - hits0) + " pins in 0.8 s");
        // ---- 4. menu audio
        Synth.StartRecording(6f);
        yield return new WaitForSecondsRealtime(1.0f);
        MainMenu.SimulatePointer(A);
        float t1 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t1 < 1.4f) { float u = (Time.realtimeSinceStartup - t1) / 1.4f; MainMenu.SimulatePointer(Vector2.Lerp(A, new Vector2(Screen.width * 0.9f, Screen.height * 0.2f), u)); yield return null; }
        yield return new WaitForSecondsRealtime(4f);
        if (Synth.RecordingDone) Synth.SaveRecording(Path.Combine(CaptureDir, "d_menu_audio.wav"));
        Line(sb, MainMenu.Music.ChordsScheduled >= 2 && Synth.LateEvents == late0 && Synth.Errors == err0, "menu audio: " + MainMenu.Music.ChordsScheduled + " pad chords, " + MainMenu.Music.NotesScheduled + " notes, late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " | " + Synth.Stats());

        // ---- 5. Hide() restores the world (no song at boot)
        bool hadSong = SongManager.I != null && SongManager.I.HasSong;
        MainMenu.Hide();
        yield return null;
        Line(sb, !MainMenu.IsShown && !WorldInput.IsLockedBy("menu") && wc != null && wc.enabled && !stage.Cam.enabled, "Hide(): world back at once (camera on, menu camera off, unlocked) with song " + hadSong);
        yield return new WaitForSecondsRealtime(0.6f);
        Line(sb, HudAlpha() > 0.9f, "Hide(): HUD visible again (alpha " + HudAlpha().ToString("F2") + ")");
        yield return new WaitForSecondsRealtime(0.3f);
        Line(sb, !MainMenu.Music.Running, "Hide(): menu music stopped (voices now " + Synth.ActiveVoices + ", releasing)");

        // ---- 6. New → prompt → dice → world + tutorial at step 1
        MainMenu.Show();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 3f);
        PlayerPrefs.SetInt(Onboarding.PrefDone, 0); PlayerPrefs.SetInt(Onboarding.PrefStep, 1);
        Onboarding.Suppressed = false;
        MainMenu.NewButton.Invoke();
        yield return new WaitForSecondsRealtime(0.5f);
        var ic = InterfaceController.I;
        Line(sb, ic != null && ic.Visible && ic.OpenedFromMenu && WorldInput.IsLockedBy("prompt") && MainMenu.IsShown, "New: vibe prompt over the menu, prompt lock on");
        if (captures) { MainMenu.SimulatePointer(new Vector2(Screen.width * 0.2f, Screen.height * 0.7f)); yield return new WaitForSecondsRealtime(0.3f); Shoot("d_prompt.png"); yield return new WaitForSecondsRealtime(0.3f); }
        events.Clear();
        int succ0 = ic != null ? ic.Successes : 0;
        if (ic != null && ic.DiceButton != null) ic.DiceButton.onClick();
        yield return WaitFor(() => ic != null && ic.Successes > succ0, 5f);
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 3f);
        yield return new WaitForSecondsRealtime(0.8f);
        Line(sb, SongManager.I != null && SongManager.I.HasSong && !MainMenu.IsShown && wc.enabled && !stage.Cam.enabled && !WorldInput.WorldLocked, "dice: song built (" + (SongManager.I != null ? SongManager.I.Islands.Count : 0) + " islands), menu gone, world camera on, input free (" + WorldInput.Describe() + ")");
        Line(sb, HudAlpha() > 0.9f && !ic.Visible, "dice: HUD visible, prompt hidden");
        Line(sb, Onboarding.Active && Onboarding.Step == 1, "dice: tutorial started at step " + Onboarding.Step);
        Info(sb, "song.started seen " + events.Contains(Onboarding.Ev.SongStarted) + " (StartFreshSong via the generator: package B), leave used the camera stack " + MainMenu.LastLeaveStacked);

        // ---- 7. the tutorial, step by step (v4, UI.md §5: draw → length → finish → open → stretch → close → + below → drag in the
        //      column → lower → deck → present → done), advanced with the gestures a player makes (simulated pointer / the header's
        //      buttons); a gesture another package has not landed yet is notified instead (listed as INFO)
        var bub = Onboarding.Bubble;
        GlobalClock.Stop();
        var smT = SongManager.I; var pmT = PathManager.I;
        for (int s = 1; s <= Onboarding.StepCount; s++)
        {
            yield return WaitFor(() => Onboarding.Step == s || !Onboarding.Active, 3f);
            if (Onboarding.Step != s) { Line(sb, false, "tutorial: expected step " + s + ", at " + Onboarding.Step); break; }
            yield return new WaitForSecondsRealtime(s == 1 ? 0.8f : 0.75f);
            string tgt = Onboarding.TargetOf(s), shownTgt = Onboarding.ShownTarget;
            bool vis = bub != null && bub.Visible && bub.Alpha > 0.9f;
            if (tgt == null) Line(sb, vis && bub.Centered, "step " + s + ": centred bubble \"" + Flat(bub.Text) + "\"");
            else if (shownTgt == tgt)
            {
                if (bub != null) bub.Layout();
                float dist = bub != null ? bub.TipDistance() : 999f;
                Line(sb, vis && !bub.Centered && dist <= 24f, "step " + s + ": bubble points at " + tgt + " (side " + bub.Side + ", tail tip " + dist.ToString("F1") + " px from the target" + (bub.OffScreen ? ", edge arrow" : "") + ")" + (vis ? "" : " [visible " + (bub != null && bub.Visible) + ", alpha " + (bub != null ? bub.Alpha.ToString("F2") : "-") + ", header " + IslandHeader.IsShown + ", target shown " + Hints.IsVisible(tgt) + ", mouse " + Input.mousePosition + "]") + " \"" + Flat(bub.Text) + "\"");
            }
            else Info(sb, "step " + s + ": target " + tgt + " not available → " + (shownTgt != null ? "hint at " + shownTgt : "centred fallback with next") + " \"" + Flat(bub != null ? bub.Text : "") + "\"");
            Line(sb, bub == null || bub.Text == bub.Text.ToLowerInvariant(), "step " + s + ": lowercase words");
            if (captures && s == 1) { Shoot("d_tutorial_world.png"); yield return new WaitForSecondsRealtime(0.3f); }
            if (captures && s == Onboarding.SAddBelow) { Shoot("d_tutorial_header.png"); yield return new WaitForSecondsRealtime(0.3f); }
            if (captures && s == Onboarding.SDeck) { Shoot("d_tutorial_deck.png"); yield return new WaitForSecondsRealtime(0.3f); }
            if (s == 3) Line(sb, PlayerPrefs.GetInt(Onboarding.PrefStep, -1) == 3, "persistence: deacube.tutorial.step = " + PlayerPrefs.GetInt(Onboarding.PrefStep, -1));
            // advance the way a player would
            var kb0 = smT.Islands[0];
            switch (s)
            {
                case Onboarding.SDraw:
                {
                    TileInteraction a = null, b = null; int k = 0;
                    foreach (var t in kb0.tiles) { if (t == null || PathManager.TopCubeOn(t) != null) continue; if (k == 0) a = t; else if (k == 2) { b = t; break; } k++; }
                    var cam = Camera.main;
                    if (a != null && b != null && cam != null)
                    {
                        Vector3 sa = cam.WorldToScreenPoint(a.Top), sbp = cam.WorldToScreenPoint(b.Top);
                        pmT.SimPointer(sa, true, true, false); pmT.SimPointer(sa, false, false, true);
                        yield return null;
                        pmT.SimPointer(sbp, true, true, false); pmT.SimPointer(sbp, false, false, true);
                    }
                    else { Info(sb, "step 1: no free tiles in view: path notified"); Onboarding.Notify(Onboarding.Ev.PathFinished); }
                    break;
                }
                case Onboarding.SLength:
                {
                    int want = pmT.BrushTicks == 48 ? 12 : 48;   // a length other than the current brush (it persists between runs)
                    pmT.SetDraftDuration(want);
                    if (pmT.BrushTicks != want) { Info(sb, "step 2: SetDraftDuration did not take: length notified"); Onboarding.Notify(Onboarding.Ev.LengthPicked); }
                    break;
                }
                case Onboarding.SFinish:
                {
                    var last = pmT.currentPathTiles.Count > 0 ? pmT.currentPathTiles[pmT.currentPathTiles.Count - 1] : null;
                    var cam = Camera.main;
                    if (last != null && cam != null) { Vector3 sl = cam.WorldToScreenPoint(last.Top); pmT.SimPointer(sl, true, true, false); pmT.SimPointer(sl, false, false, true); }
                    else pmT.FinishPath();
                    break;
                }
                case Onboarding.SOpen: { var c = Onboarding.UserCube; if (c != null) CubeInspector.Open(c); else Onboarding.Notify(Onboarding.Ev.CubeInspected); } break;
                case Onboarding.SStretch: Onboarding.Notify(Onboarding.Ev.NoteStretched); break;
                case Onboarding.SClose: CubeInspector.Close(); break;
                case Onboarding.SAddBelow:
                {
                    yield return WaitFor(() => IslandHeader.IsShown, 2f);
                    int n0 = smT.Islands.Count;
                    if (IslandHeader.I != null) IslandHeader.I.AddBelow.onClick();
                    yield return null;
                    if (smT.Islands.Count == n0) { Info(sb, "step 7: AddIslandInColumn is a stub: island.added notified"); Onboarding.Notify(Onboarding.Ev.IslandAdded); }
                    break;
                }
                case Onboarding.SDragColumn:
                {
                    var kb = Onboarding.ChapterIsland != null ? Onboarding.ChapterIsland : smT.Islands[smT.Islands.Count - 1];
                    Vector3 g0 = new Vector3(kb.Center.x, 0f, kb.Center.z);
                    pmT.Drag.SimBegin(kb, false, g0);
                    yield return null;
                    pmT.Drag.SimMove(g0 + new Vector3(0f, 0f, -2f));
                    yield return null;
                    pmT.Drag.SimRelease();
                    yield return new WaitForSecondsRealtime(0.5f);
                    break;
                }
                case Onboarding.SLower:
                    yield return WaitFor(() => IslandHeader.IsShown, 2f);
                    if (IslandHeader.I != null) IslandHeader.I.RegisterDown.onClick();
                    break;
                case Onboarding.SDeck:
                    IslandTray.Toggle();
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (IslandTray.I != null) IslandTray.I.SimClick(0);
                    if (SongManager.I != null && Onboarding.Step == Onboarding.SDeck) Onboarding.Notify(Onboarding.Ev.IslandPlaced);
                    IslandTray.Close();
                    break;
                case Onboarding.SPresent: Onboarding.Notify(Onboarding.Ev.PresentEntered); break;
                default: break;
            }
            GlobalClock.Stop();
        }
        yield return WaitFor(() => !Onboarding.Active, 4.5f);
        Line(sb, !Onboarding.Active && Onboarding.Done && PlayerPrefs.GetInt(Onboarding.PrefDone, 0) == 1, "tutorial: the last bubble closed itself, done = 1");

        // ---- 8. skip, resume, suppression
        Onboarding.StartTutorial(true);
        yield return new WaitForSecondsRealtime(0.7f);
        bool restarted = Onboarding.Step == 1 && !Onboarding.Done;
        if (bub != null && bub.SkipButton != null) bub.SkipButton.onClick();
        yield return null;
        Line(sb, restarted && !Onboarding.Active && Onboarding.Done, "skip: Learn-style restart at step 1, ✕ ends it (done = 1)");
        PlayerPrefs.SetInt(Onboarding.PrefDone, 0); PlayerPrefs.SetInt(Onboarding.PrefStep, 4);
        Onboarding.StartTutorial(false);
        yield return null;
        Line(sb, Onboarding.Step == 4, "resume: a saved step 4 resumes at step " + Onboarding.Step);
        events.Clear();
        Onboarding.Suppressed = true;
        Onboarding.Notify(Onboarding.Ev.PathFinished);
        yield return new WaitForSecondsRealtime(0.4f);
        Line(sb, Onboarding.Step == 4 && !Onboarding.Active && events.Contains(Onboarding.Ev.PathFinished) && (bub == null || !bub.Visible), "Suppressed: no bubble, progress frozen, events still forwarded");
        Onboarding.Suppressed = false;
        Onboarding.Skip();

        // ---- 8b. one-off contextual tips after the tutorial (v5: the three new tips are marked seen and any tip on screen is closed first, so
        //      only the hover tip can answer; their prefs are restored at the end)
        PlayerPrefs.SetInt(Onboarding.PrefDone, 1);
        foreach (var t5 in new[] { Onboarding.TipVibes, Onboarding.TipMove, Onboarding.TipRepeat, Onboarding.TipHear, Onboarding.TipDraftRun }) PlayerPrefs.SetInt(Onboarding.PrefTip + t5, 1);
        foreach (var t6 in Onboarding.V6Tips) PlayerPrefs.SetInt(Onboarding.PrefTip + t6, 1);   // v6 too
        foreach (var t7 in Onboarding.V7Tips) PlayerPrefs.SetInt(Onboarding.PrefTip + t7, 1);   // v7 too (the shortcuts tip answers an inspected cube)
        if (Onboarding.ActiveTip != null && bub != null && bub.SkipButton != null) { bub.SkipButton.onClick(); yield return null; }
        PlayerPrefs.DeleteKey(Onboarding.PrefTip + "hover");
        Onboarding.Notify(Onboarding.Ev.CubeHovered);
        yield return new WaitForSecondsRealtime(0.5f);
        bool tipShown = Onboarding.ActiveTip == "hover" && bub != null && bub.Visible;
        string tipText = bub != null ? Flat(bub.Text) : "";
        yield return WaitFor(() => Onboarding.ActiveTip == null, 4f);
        Onboarding.Notify(Onboarding.Ev.CubeHovered);
        yield return null;
        Line(sb, tipShown && Onboarding.ActiveTip == null && PlayerPrefs.GetInt(Onboarding.PrefTip + "hover", 0) == 1, "tips: the first cube hover shows \"" + tipText + "\" once, then never again");

        // ---- 9. home button from the world → autosave + menu
        GlobalClock.Stop();
        HudButton home = null;
        foreach (var b in UIManager.I.Canvas.GetComponentsInChildren<HudButton>(true)) if (b.name == "Home") { home = b; break; }
        int autos0 = MainMenu.Autosaves;
        DateTime before = DateTime.UtcNow.AddSeconds(-1);
        if (home != null) home.onClick();
        yield return null;
        bool autoOk = File.Exists(SongIO.AutosavePath) && File.GetLastWriteTimeUtc(SongIO.AutosavePath) >= before && MainMenu.Autosaves == autos0 + 1;
        int autoIslands = -1;
        try { var st = SongState.FromJson(File.ReadAllText(SongIO.AutosavePath)); autoIslands = st.measures.Length; } catch (Exception) { }
        Line(sb, home != null && MainMenu.IsShown && autoOk && autoIslands == SongManager.I.Islands.Count, "home: menu shown, autosave written to " + Path.GetFileName(SongIO.AutosavePath) + " (" + autoIslands + " islands)");
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 3f);
        Line(sb, MainMenu.State == MainMenu.Phase.Shown && !wc.enabled && stage.Cam.enabled && !stage.Overlay, "home: the world faded to dark, the quick reveal played, then the menu camera alone renders");

        // ---- 10. Continue loads the newest save (a scratch autosave built from the fixture)
        File.WriteAllText(SongIO.AutosavePath, File.ReadAllText(V2Checks.FixturePath));
        File.SetLastWriteTimeUtc(SongIO.AutosavePath, DateTime.UtcNow);
        bool newest = SongIO.NewestExisting == SongIO.AutosavePath;
        MainMenu.ContinueButton.Invoke();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 3f);
        yield return new WaitForSecondsRealtime(0.3f);
        var sm = SongManager.I;
        Line(sb, newest && !MainMenu.IsShown && sm.Islands.Count == 6 && SequenceMaster.Cubes.Count == 13 && Mathf.RoundToInt(GlobalClock.BPM) == 121, "Continue: newest (autosave) loaded: " + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes, " + GlobalClock.BPM + " bpm; world back");

        // ---- 11. audio and the user's save
        Line(sb, Synth.LateEvents == late0 && Synth.Errors == err0, "synth: late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " over the whole run");
        Line(sb, Md5(SongIO.Path) == userSave, "the user's save is untouched (md5 " + userSave + ")");

        // ---- restore
        GlobalClock.Stop();
        Onboarding.OnEvent -= listen;
        if (hadAuto && autoBackup != null) File.Copy(autoBackup, SongIO.AutosavePath, true); else if (File.Exists(SongIO.AutosavePath)) File.Delete(SongIO.AutosavePath);
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        for (int i = 0; i < tips.Length; i++) RestorePref(Onboarding.PrefTip + tips[i], tipPrefs[i]);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        MainMenu.ReleasePointer();
        int pass = 0, fail = 0;
        foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Insert(0, "V3ChecksD: " + pass + " PASS, " + fail + " FAIL\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(Path.Combine(CaptureDir, "d_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }

    static void RestorePref(string key, int v) { if (v < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, v); }
    static string Flat(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('\n', ' ');
}
