using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Package U2 (SPEC v6 §8, scratchpad/v6/brief_U2.md) Play-mode checks — the hand and the instruments UI, with PathManager.AutoHand OFF (the
/// real game): the instrument column as ten groups (a chip click picks the group's cube up: the chip lifts out "in the hand" — offset, hard
/// shadow, sway, ring — a second click puts it down, another chip swaps, mix mode unchanged), the voice fan (hover ≈ 0.25 s, one chip per
/// voice right of the chip, ≥ 28 px hits, captions = the voice names, hover auditions, click = pick up that voice, the chip wears its pips,
/// it folds away when the pointer leaves, 1..12 voices), the cursor (the pointing hand + "press to hear" over a free tile with an empty hand,
/// the words stop after three presses, a cube / a pattern card riding the cursor, bobbing and tilted, gone while drawing), the cube card
/// (≤ 20 face targets, copy → the clipboard with no History entry, the voice row in the sound popover — a voice re-sounds the cube with one
/// entry, a group pick keeps the popover on the new group's voices —, paste → next grid: the card closes, the adapted copy lands there with
/// one entry), the clipboard chip (bottom-right after a copy, click = the pattern in the hand, drag onto a grid = paste there, hidden with the
/// HUD, × clears), the hotkeys (1–9 / 0 pick up and put down, Esc puts the hand down and nothing else, Esc while inspecting closes the card
/// first, digits while inspecting recolour, Cmd+C the hovered cube, Cmd+V the hovered grid / the next grid), no per-frame GC; Synth late /
/// errors 0; the user's save untouched. Start with <see cref="RunAll"/>, poll <see cref="Done"/> or Captures/u2_6_report.txt.
/// Captures (Game view 1920 × 1080): u2_6_column_held, u2_6_voice_fan, u2_6_cursor_cube, u2_6_cursor_pattern, u2_6_cursors, u2_6_card,
/// u2_6_card_voices, u2_6_paste_flight, u2_6_paste_landed, u2_6_clipboard, u2_6_clipboard_held.
/// </summary>
public static class V6ChecksU2
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u2_6_report.txt");
    static StringBuilder sb; static int num, pass, fail, pushes;
    /// <summary>Synth late events counted apart: a structural rebuild while playing (K's AddMoon mid-song) re-decides the step under the
    /// playhead late by design (like a seek while playing) — not the UI's doing.</summary>
    static int lateApart;
    static bool captures;

    static void Line(bool ok, string what, string detail = null)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append("U2.6.").Append(num).Append(' ').Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" — ").Append(detail);
        sb.Append('\n');
        Flush(false);
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }
    static void Flush(bool final)
    {
        Report = "V6ChecksU2: " + pass + " PASS, " + fail + " FAIL" + (final ? "" : " (running)") + "\n" + sb;
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
    }
    static void OnHistory() { pushes++; }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float s) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Shot(string file)
    {
        if (!captures) yield break;
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        Info("capture " + file + " (" + Screen.width + "x" + Screen.height + ")");
        yield return null; yield return null;
        yield return Wait(0.25f);
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        var pm = PathManager.I;
        if (pm != null && pm.IsDrawing) pm.CancelPath();
        FocusLoop.Dismiss(); GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0); SequenceMaster.ResetAllCubes();
    }

    static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); WorldInput.Unlock(HudMenuStrip.LockOwner);
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        WorldInput.Unlock("prompt");
        if (IslandTray.IsOpen) IslandTray.Close();
        if (UIManager.I != null) { UIManager.I.SetHudVisible(true); UIManager.I.ClearSelection(); if (UIManager.I.Strip != null) UIManager.I.Strip.CloseNow(); }
        IslandHeader.Hide();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void Frame(Vector3 centre, float dist)
    {
        if (OrbitCamera.I == null) return;
        OrbitCamera.I.FrameBounds(new Bounds(centre, new Vector3(dist, 2f, dist * 0.6f)), 0.05f, true);
    }

    static Vector3 ScreenOf(Vector3 world) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero; }

    static TileInteraction FreeTile(KeyBlock kb, int skip)
    {
        int k = 0;
        foreach (var t in kb.tiles) { if (t == null || PathManager.TopCubeOn(t) != null) continue; if (k++ < skip) continue; return t; }
        return null;
    }

    static AudioCube PitchedCube(Func<AudioCube, bool> extra = null)
    {
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && c.Island != null && c.nodes.Count >= 2 && (extra == null || extra(c))) return c;
        return null;
    }

    static int CubesOn(KeyBlock kb) { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == kb) n++; return n; }

    static readonly Vector3[] corners = new Vector3[4];
    static Rect ScreenRect(RectTransform r)
    {
        r.GetWorldCorners(corners);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++) { Vector2 p = RectTransformUtility.WorldToScreenPoint(null, corners[i]); x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    /// <summary>The top UI raycast target at a screen point is <paramref name="target"/> or one of its children (what a real click reaches).</summary>
    static bool TopHit(Transform target, Vector2 screen, out string got)
    {
        var es = EventSystem.current;
        got = "no event system";
        if (es == null || target == null) return false;
        var ped = new PointerEventData(es) { position = screen };
        var list = new List<RaycastResult>();
        es.RaycastAll(ped, list);
        if (list.Count == 0) { got = "nothing"; return false; }
        var go = list[0].gameObject;
        got = go.name;
        return go.transform == target || go.transform.IsChildOf(target);
    }

    static void Key(KeyCode k, bool cmd = false)
    {
        if (cmd) KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
        KeyShim.Sim(k, true, true, false);
        UIManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static float RefScale => UIManager.I != null && UIManager.I.Canvas != null ? Mathf.Max(0.01f, UIManager.I.Canvas.scaleFactor) : 1f;

    // ================================================================== run
    public static string RunAll(bool withCaptures = true)
    {
        if (UIManager.I == null || SongManager.I == null || PathManager.I == null || HudInstruments.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; captures = withCaptures;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        UIManager.I.StartCoroutine(Routine());
        return "started";
    }

    static IEnumerator Routine()
    {
        sb = new StringBuilder(); num = pass = fail = 0; pushes = 0; lateApart = 0;
        SongIO.QuitAutosave = false;
        string userMd5 = Md5(SongIO.Path);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        bool suppressed = Onboarding.Suppressed, autoHand = PathManager.AutoHand;
        var brush0 = (int[])Instruments.BrushVoice.Clone();
        PathManager.AutoHand = false;
        History.OnChanged += OnHistory;
        IEnumerator[] parts = { Column(), Fan(), Cursor(), Card(), ClipboardChip(), Hotkeys(), Cost() };
        foreach (var part in parts)
        {
            Prepare();
            LoadFixture();
            PathManager.I.PutDown(); Clipboard.Clear();
            PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            yield return null; yield return null;
            // run the part; an exception fails it and the next part goes on
            while (true)
            {
                object cur;
                try { if (!part.MoveNext()) break; cur = part.Current; }
                catch (Exception e) { Line(false, "exception", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
                yield return cur;
            }
        }
        // ---- leave things as found
        Clipboard.Clear();
        if (PathManager.I != null) PathManager.I.PutDown();
        HudInstruments.TestVoices = 0;
        for (int i = 0; i < brush0.Length; i++) Instruments.BrushVoice[i] = brush0[i];
        if (HudInstruments.I != null) { HudInstruments.I.CloseFan(); HudInstruments.I.SetMix(false); }
        CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss(); GlobalClock.Stop();
        PathManager.SimOnly = false; KeyShim.Clear(); CursorKit.ResetAll();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        try { LoadFixture(); } catch (Exception) { }
        History.OnChanged -= OnHistory;
        Onboarding.Suppressed = suppressed; PathManager.AutoHand = autoHand;
        if (lateApart > 0) Info("Synth late events counted apart (the Moon added while playing rebuilds the song mid-play): " + lateApart);
        Line(Synth.LateEvents - lateApart == late0 && Synth.Errors == err0, "Synth late / errors unchanged (a rebuild while playing counted apart)",
             "late +" + (Synth.LateEvents - late0 - lateApart) + " (+" + lateApart + " apart), errors +" + (Synth.Errors - err0));
        Line(Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + userMd5);
        Flush(true);
        Done = true;
    }

    // ================================================================== 1. the column: ten groups, the chip in the hand
    static IEnumerator Column()
    {
        var pm = PathManager.I; var col = HudInstruments.I;
        int picks = 0, mutes = 0;
        foreach (var b in col.Root.GetComponentsInChildren<HudButton>(true)) { if (b.name == "Pick") picks++; else if (b.name == "Mute") mutes++; }
        Line(picks == Instruments.Count && mutes == Instruments.Count, "the column: a chip per group (HUD/Palette keeps a Pick + a Mute per group; v9: 11 with fx)", picks + " / " + mutes);
        int p0 = pushes;
        col.PickOf(3).onClick();
        yield return Wait(0.45f);
        Vector2 off = col.RowOffset(3);
        Line(pm.Hand == PathManager.HandKind.Cube && pm.selectedInstrument == 3 && col.Held == 3 && col.LiftOf(3) >= 1f && (off - HudInstruments.HeldLift).magnitude < 0.6f
             && col.ShadowShown(3) && col.ChipOf(3).Ring && pushes == p0,
             "a chip click picks up that group's cube: the chip lifts out of the column (right + up), wears the ring and a hard shadow; no History entry",
             "hand " + pm.Hand + " / " + pm.selectedInstrument + ", lift " + col.LiftOf(3).ToString("F2") + ", offset " + off + ", shadow " + col.ShadowShown(3) + ", pushes +" + (pushes - p0));
        float amin = 99f, amax = -99f; float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.3f) { float a = col.ChipAngle(3); amin = Mathf.Min(amin, a); amax = Mathf.Max(amax, a); yield return null; }
        Line(amax - amin >= 2f && amax - amin <= 7f, "the held chip sways in the hand (stepped, about ±3°)", amin.ToString("F1") + "° .. " + amax.ToString("F1") + "°");
        yield return Shot("u2_6_column_held.png");
        col.PickOf(3).onClick();
        yield return Wait(0.45f);
        Line(pm.Hand == PathManager.HandKind.Empty && col.Held == -1 && col.LiftOf(3) <= 0f && !col.ShadowShown(3) && col.RowOffset(3).magnitude < 0.5f && Mathf.Abs(col.ChipAngle(3)) < 0.01f,
             "a second click puts it down: the chip settles back into the column", "hand " + pm.Hand + ", offset " + col.RowOffset(3) + ", angle " + col.ChipAngle(3).ToString("F1"));
        col.PickOf(1).onClick(); yield return null;
        col.PickOf(5).onClick();
        yield return Wait(0.45f);
        Line(pm.CubeInHand && pm.selectedInstrument == 5 && col.Held == 5 && col.LiftOf(1) <= 0f && col.LiftOf(5) >= 1f, "another chip swaps the cube in the hand (the first settles back)",
             "held " + col.Held + ", lifts " + col.LiftOf(1).ToString("F2") + " / " + col.LiftOf(5).ToString("F2"));
        pm.PutDown(); yield return null;
        col.SetMix(true); yield return Wait(0.3f);
        bool m0 = Instruments.Muted[2]; p0 = pushes;
        col.PickOf(2).onClick(); yield return null;
        bool mixOk = Instruments.Muted[2] != m0 && pm.Hand == PathManager.HandKind.Empty && pushes == p0 + 1 && !col.FanOpen;
        col.PickOf(2).onClick(); yield return null;
        col.SetMix(false); yield return Wait(0.2f);
        Line(mixOk && Instruments.Muted[2] == m0, "mix mode unchanged: a chip click toggles the group's mute (one History entry), nothing is picked up");
    }

    // ================================================================== 2. the voice fan
    static IEnumerator Fan()
    {
        var pm = PathManager.I; var col = HudInstruments.I;
        int g = 1, n = Instruments.VoiceCount(g);
        Info("group " + g + " (" + Instruments.GroupWords[g] + ") has " + n + " voices: " + VoiceList(g));
        col.PickOf(g).OnPointerEnter(null);
        yield return Wait(0.1f);
        bool early = col.FanOpen;
        yield return Wait(0.3f);
        Line(!early && col.FanOpen && col.FanGroup == g && col.FanCount == n, "hovering a chip ≈ 0.25 s fans the group's voices out (not before)", "at 0.1 s " + early + ", at 0.4 s " + col.FanOpen + " with " + col.FanCount + " chips (VoiceCount " + n + ")");
        yield return Wait(0.35f);
        // geometry: in a row right of the chip, each ≥ 28 px, on screen; captions = the voice names
        Rect chip = ScreenRect(col.ChipOf(g).rectTransform);
        bool row = true, big = true, caps = true, shown = true; float lastX = chip.xMax; string bad = null;
        for (int v = 0; v < n; v++)
        {
            var b = col.FanChip(v);
            if (b == null || !b.gameObject.activeInHierarchy) { shown = false; bad = "chip " + v + " hidden"; break; }
            Rect r = ScreenRect((RectTransform)b.transform);
            if (r.center.x <= lastX || Mathf.Abs(r.center.y - chip.center.y) > 18f * RefScale) { row = false; if (bad == null) bad = "chip " + v + " at " + r.center; }
            if (r.width / RefScale < 27.5f || r.height / RefScale < 27.5f) { big = false; if (bad == null) bad = "chip " + v + " " + (r.width / RefScale).ToString("F0") + "x" + (r.height / RefScale).ToString("F0"); }
            if (r.xMax > Screen.width) { shown = false; if (bad == null) bad = "chip " + v + " off screen"; }
            lastX = r.center.x;
            var cap = b.GetComponent<InkCaptionHover>();
            if (cap == null || cap.words != Instruments.VoiceName(g, v)) { caps = false; if (bad == null) bad = "caption " + v + " = " + (cap != null ? cap.words : "none"); }
        }
        Line(row && big && caps && shown, "the fan: one chip per voice in a row right of the group chip, each ≥ 28 px, on screen, captioned with the voice's name", bad ?? (n + " chips"));
        string hit;
        var mid = col.FanChip(n / 2);
        bool reach = mid != null && TopHit(mid.transform, ScreenRect((RectTransform)mid.transform).center, out hit);
        Line(reach, "a real click reaches a voice chip (top raycast target)");
        int au0 = HudInstruments.AuditionCount;
        col.PickOf(g).OnPointerExit(null);
        col.FanChip(4).OnPointerEnter(null);
        InkCaption.Show((RectTransform)col.FanChip(4).transform, Instruments.VoiceName(g, 4));
        yield return Wait(0.5f);
        Line(HudInstruments.AuditionCount == au0 + 1 && col.FanOpen, "hovering a voice auditions it (Instruments.Audition(group, voice)); the fan stays while hovered", "auditions +" + (HudInstruments.AuditionCount - au0));
        yield return Shot("u2_6_voice_fan.png");
        col.FanChip(4).onClick();
        yield return Wait(0.45f);
        Line(pm.CubeInHand && pm.selectedInstrument == g && Instruments.BrushVoice[g] == 4 && col.MarkVoice(g) == 4 && col.Held == g,
             "clicking a voice picks up that voice (PickUpCube(group, voice)): BrushVoice = 4, the chip wears its pips", "hand " + pm.Hand + ", brush " + Instruments.BrushVoice[g] + ", mark " + col.MarkVoice(g));
        col.FanChip(4).OnPointerExit(null); InkCaption.Hide(null);
        yield return Wait(0.55f);
        Line(!col.FanOpen, "the fan folds away ≈ 0.3 s after the pointer left the chip and the fan");
        int p0 = pushes;
        col.PickOf(g).onClick(); yield return null;
        Line(pm.Hand == PathManager.HandKind.Empty && pushes == p0, "the held group's chip puts the voice down again (no History entry)");
        HudInstruments.TestVoices = 12; col.OpenFan(0);
        // (the fan deals its chips three per drawing on twos: wait for the deal itself, not a fixed time — a busy editor runs few frames)
        yield return WaitFor(() => { for (int v = 0; v < 12; v++) { var fb = col.FanChip(v); if (fb == null || !fb.gameObject.activeInHierarchy) return false; } return true; }, 2f);
        yield return Wait(0.1f);
        bool all = col.FanCount == 12; float right = 0f;
        for (int v = 0; v < 12 && all; v++) { var b = col.FanChip(v); all = b != null && b.gameObject.activeInHierarchy; if (all) right = Mathf.Max(right, ScreenRect((RectTransform)b.transform).xMax); }
        Line(all && right < Screen.width * 0.5f, "the fan lays out up to 12 voices (12 fit, left half of the screen)", "right edge " + right.ToString("F0") + " px");
        HudInstruments.TestVoices = 1; col.OpenFan(0); yield return null;
        Line(!col.FanOpen, "a group with a single voice fans nothing out");
        HudInstruments.TestVoices = 0; col.CloseFan();
        Instruments.BrushVoice[g] = 0;
        yield return null;
    }

    static string VoiceList(int g) { var s = new StringBuilder(); for (int v = 0; v < Instruments.VoiceCount(g); v++) { if (v > 0) s.Append(", "); s.Append(Instruments.VoiceName(g, v)); } return s.ToString(); }

    // ================================================================== 3. the cursor
    static IEnumerator Cursor()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.3f);
        var t = FreeTile(kb, 3);
        if (t == null) { Line(false, "cursor", "no free tile"); yield break; }
        Vector3 s = ScreenOf(t.Top);
        PathManager.SimPos = s; CursorKit.ResetAll(); CursorKit.ResetPressHint();
        yield return null; yield return null; yield return null;
        bool overUi = InputUtil.PointerOverUI;
        var k0 = CursorKit.Current; bool hint0 = CursorKit.PressHintShown;
        yield return Wait(0.8f);
        bool hint1 = CursorKit.PressHintShown;
        if (!overUi)
            Line(pm.HoverPressTile == t && k0 == CursorKit.Kind.Point && !hint0 && hint1, "empty hand over a free tile: the pointing hand; \"press to hear\" after the pointer rests 0.6 s",
                 "press tile " + (pm.HoverPressTile == t) + ", cursor " + k0 + ", words " + hint0 + " → " + hint1);
        else Info("the real mouse is over the UI: the pointing hand check is skipped (cursor " + k0 + ")");
        for (int i = 0; i < CursorKit.PressHintTimes; i++) { pm.SimPointer(s, true, true, false); pm.SimPointer(s, false, false, true); yield return null; yield return null; }
        yield return Wait(0.8f);
        Line(CursorKit.PressCount >= CursorKit.PressHintTimes && !CursorKit.PressHintShown && pm.Hand == PathManager.HandKind.Empty && pm.CubeInHand == false,
             "after three presses the words stop (the pointing hand stays); pressing placed nothing", "presses " + CursorKit.PressCount + ", words " + CursorKit.PressHintShown);
        // a cube in the hand rides the cursor
        HudInstruments.ToggleHand(5, -1);
        yield return Wait(0.35f);
        Vector2 rs = CursorKit.RiderScreen; Vector2 ptr = s;
        float dx = (rs.x - ptr.x) / RefScale, dy = (rs.y - ptr.y) / RefScale;
        Color rc = CursorKit.RiderColor, want = Comic.Opaque(Instruments.Colors[5]);
        Line(CursorKit.RiderKind == 1 && Mathf.Abs(rc.r - want.r) + Mathf.Abs(rc.g - want.g) + Mathf.Abs(rc.b - want.b) < 0.02f && dx > 12f && dx < 60f && dy < -12f && dy > -60f && CursorKit.Current != CursorKit.Kind.Point,
             "a cube in the hand rides the cursor: a small cube of the group's colour below-right of the pointer (not the pointing hand)", "rider " + CursorKit.RiderKind + " at (" + dx.ToString("F0") + ", " + dy.ToString("F0") + ") ref px, cursor " + CursorKit.Current);
        float bmin = 99f, bmax = -99f, tmin = 99f, tmax = -99f; float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.2f) { bmin = Mathf.Min(bmin, CursorKit.RiderBob); bmax = Mathf.Max(bmax, CursorKit.RiderBob); tmin = Mathf.Min(tmin, CursorKit.RiderTilt); tmax = Mathf.Max(tmax, CursorKit.RiderTilt); yield return null; }
        Line(bmax - bmin >= 2f && tmax <= -8f && tmin >= -20f, "it bobs and is tilted (on twos)", "bob " + bmin + ".." + bmax + " px, tilt " + tmin + ".." + tmax + "°");
        PathManager.SimPos = ScreenOf(FreeTile(kb, 5) != null ? FreeTile(kb, 5).Top : t.Top);
        yield return Wait(0.3f);
        yield return Shot("u2_6_cursor_cube.png");
        // drawing: the brush and the hologram take over
        var t2 = FreeTile(kb, 7) ?? t;
        Vector3 s2 = ScreenOf(t2.Top); PathManager.SimPos = s2;
        pm.SimPointer(s2, true, true, false); pm.SimPointer(s2, false, false, true);
        yield return null; yield return null;
        bool drawing = pm.IsDrawing, gone = CursorKit.RiderKind == 0, brush = CursorKit.Current == CursorKit.Kind.Draw;
        pm.CancelPath(); yield return null; yield return null;
        Line(drawing && gone && brush && pm.CubeInHand, "while a path is drawn the rider leaves (the brush cursor draws); the cube stays in the hand", "drawing " + drawing + ", rider gone " + gone + ", cursor " + (brush ? "Draw" : "other"));
        // a pattern in the hand: the card rides
        var src = PitchedCube(c => c.Island != kb);
        Clipboard.Copy(src);
        pm.PickUpPattern();
        var other = Clipboard.NextGrid(src.Island) ?? kb;
        var ot = FreeTile(other, 2) ?? FreeTile(other, 0);
        PathManager.SimPos = ot != null ? ScreenOf(ot.Top) : s;
        yield return Wait(0.4f);
        Line(CursorKit.RiderKind == 2 && pm.Hand == PathManager.HandKind.Pattern, "a pattern in the hand rides the cursor as a small card (its dots and line)", "rider " + CursorKit.RiderKind);
        yield return Shot("u2_6_cursor_pattern.png");
        pm.PutDown(); PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null; yield return null;
        Line(CursorKit.RiderKind == 0 && CursorKit.Current == CursorKit.Kind.Default, "an empty hand away from tiles: no rider, the system cursor");
        float tip = CursorKit.CoverageAt(CursorKit.Kind.Point, new Vector2(-0.2f, 0.72f)), empty = CursorKit.CoverageAt(CursorKit.Kind.Point, new Vector2(0.62f, 0.62f));
        bool prev = CursorKit.SavePreview(Path.Combine(CaptureDir, "u2_6_cursors.png"), Instruments.Colors[5]);
        Line(tip > 0.5f && empty < 0.35f && prev, "the pointing hand: the finger up to its tip, clear beside it (preview u2_6_cursors.png: move, grab, draw, point)", "finger " + tip.ToString("F2") + ", beside " + empty.ToString("F2"));
    }

    // ================================================================== 4. the cube card: copy, the voices, paste → next grid
    static IEnumerator Card()
    {
        var pm = PathManager.I;
        var c = PitchedCube(x => x.Island.cols >= 5 && x.nodes.Count >= 3 && InspectorCard.NextGridOf(x) != null && Instruments.VoiceCount(x.instrument) >= 3);
        if (c == null) { Line(false, "card", "no pitched cube with a next grid"); yield break; }
        Frame(c.Island.Center, 13f); yield return null;
        CubeInspector.Open(c);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open && InspectorCard.Shown >= 1f, 4f);
        yield return Wait(0.3f);
        int face = InspectorCard.FaceTargetCount();
        string h1, h2;
        bool r1 = TopHit(InspectorCard.CopyButton.transform, ScreenRect((RectTransform)InspectorCard.CopyButton.transform).center, out h1);
        bool r2 = TopHit(InspectorCard.PasteNextButton.transform, ScreenRect((RectTransform)InspectorCard.PasteNextButton.transform).center, out h2);
        Line(face <= 20 && r1 && r2 && InspectorCard.PasteNextButton.Interactable, "the card's face gains copy and paste → next grid (a real click reaches both) and stays ≤ 20 hit targets", face + " targets, copy → " + h1 + ", paste → " + h2);
        int p0 = pushes;
        InspectorCard.CopyButton.Click();
        yield return null;
        bool same = Clipboard.HasPattern && Clipboard.SourceCube == c && Clipboard.State.xs.Length == c.nodes.Count;
        for (int i = 0; same && i < c.nodes.Count; i++) same = Clipboard.State.xs[i] == c.nodes[i].gridX && Clipboard.State.zs[i] == c.nodes[i].gridZ;
        Line(same && pushes == p0, "copy puts the cube's pattern on the clipboard (its tiles, node for node); no History entry", "pattern " + (Clipboard.HasPattern ? Clipboard.State.xs.Length + " notes" : "none") + ", pushes +" + (pushes - p0));
        yield return Wait(0.7f);
        var chip = UIManager.I.ClipboardChip;
        Rect cr = chip != null ? ScreenRect(chip.Root) : new Rect();
        Line(chip != null && chip.Shown && cr.xMin > Screen.width * 0.8f && cr.yMax < Screen.height * 0.25f, "the clipboard chip appears bottom-right (flown in from the copy button)", "chip " + cr);
        yield return Shot("u2_6_card.png");
        // the voices of its group in the sound popover
        int g = c.instrument, n = Instruments.VoiceCount(g);
        InspectorCard.ChipButton.Click();
        yield return Wait(0.2f);
        Line(InspectorCard.PopoverOpen && InspectorCard.VoiceChipCount == n, "the chip's popover: every group and, under a wavy line, the group's voices as small cubes with their pips", InspectorCard.VoiceChipCount + " voice chips (" + n + " voices)");
        yield return Shot("u2_6_card_voices.png");
        p0 = pushes;
        InspectorCard.VoiceButton(2).Click();
        yield return null; yield return null;
        Line(c.voice == 2 && pushes - p0 == 1 && InspectorCard.ChipVoice == 2 && InspectorCard.PopoverOpen && InspectorCard.VoicePicks > 0,
             "a voice click re-sounds the cube (AudioCube.SetVoice: voice 2 → slot " + Instruments.SlotOf(g, 2) + ", voice 0 is slot " + Instruments.SlotOf(g, 0) + "), one History entry; the chip wears its pips, the popover stays",
             "voice " + c.voice + ", pushes +" + (pushes - p0));
        int k = -1; for (int i = 0; i < Instruments.Count && k < 0; i++) if (i != g && !Instruments.IsDrums(i) && Instruments.VoiceCount(i) >= 2) k = i;
        p0 = pushes;
        InspectorCard.InstrumentButton(k).Click();
        yield return null; yield return null;
        Line(c.instrument == k && c.voice == Instruments.BrushVoiceOf(k) && pushes - p0 == 1 && InspectorCard.PopoverOpen && InspectorCard.VoiceChipCount == Instruments.VoiceCount(k),
             "a group pick recolours the cube (one History entry), gives it the group's brush voice and keeps the popover up on the new group's voices",
             "instrument " + c.instrument + ", voice " + c.voice + ", voice chips " + InspectorCard.VoiceChipCount + ", pushes +" + (pushes - p0));
        InspectorCard.ChipButton.Click(); yield return null;
        // paste → next grid
        var target = InspectorCard.NextGridOf(c);
        int on0 = CubesOn(target), all0 = SequenceMaster.Cubes.Count; p0 = pushes;
        InspectorCard.PasteNextButton.Click();
        yield return WaitFor(() => !InspectorCard.PastePending, 4f);
        yield return Wait(0.25f);
        yield return Shot("u2_6_paste_flight.png");
        yield return Wait(0.9f);
        var made = InspectorCard.LastPasteNext;
        Line(made != null && made.Island == target && CubesOn(target) == on0 + 1 && SequenceMaster.Cubes.Count == all0 + 1 && pushes - p0 == 1 && !CubeInspector.IsOpen,
             "paste → next grid: the card closes, then the adapted copy lands on the next grid (Clipboard.NextGrid) — one History entry",
             "made " + (made != null) + " on " + (made != null && made.Island != null ? "column " + made.Island.column : "-") + " (want column " + target.column + "), cubes there " + on0 + " → " + CubesOn(target) + ", pushes +" + (pushes - p0));
        if (made != null)
        {
            var want = Clipboard.AdaptedFor(target);
            bool adapted = want != null && want.xs.Length == made.nodes.Count;
            for (int i = 0; adapted && i < made.nodes.Count; i++) adapted = want.xs[i] == made.nodes[i].gridX && want.zs[i] == made.nodes[i].gridZ;
            Info("pasted tiles = Clipboard.AdaptedFor(next grid): " + adapted + " (the adaptation itself is H's)");
        }
        yield return Shot("u2_6_paste_landed.png");
    }

    // ================================================================== 5. the clipboard chip
    static IEnumerator ClipboardChip()
    {
        var pm = PathManager.I; var sm = SongManager.I; var chip = UIManager.I.ClipboardChip;
        if (chip == null) { Line(false, "clipboard chip", "not built"); yield break; }
        yield return Wait(0.5f);   // (a chip left from the previous part pops out)
        Line(!chip.Root.gameObject.activeSelf, "an empty clipboard: no chip");
        var src = PitchedCube(x => x.nodes.Count >= 3);
        Frame(src.Island.Center, 18f); yield return null;
        Clipboard.Copy(src);
        yield return Wait(0.5f);
        Line(chip.Shown, "a copy shows the chip");
        yield return Shot("u2_6_clipboard.png");
        chip.SimClick();
        yield return Wait(0.35f);
        Line(pm.Hand == PathManager.HandKind.Pattern && chip.Lifted, "a click picks the pattern up (paste mode): the chip lifts like a held chip");
        var ot = FreeTile(Clipboard.NextGrid(src.Island) ?? src.Island, 1);
        if (ot != null) PathManager.SimPos = ScreenOf(ot.Top);
        yield return Wait(0.3f);
        yield return Shot("u2_6_clipboard_held.png");
        chip.SimClick(); yield return null;
        Line(pm.Hand == PathManager.HandKind.Empty, "a second click puts it down");
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        KeyBlock target = null;
        foreach (var kb in sm.Islands) if (kb != null && kb != src.Island && !kb.IsMoon && !kb.IsKeyboard && Clipboard.CanPasteOn(kb) && kb.column != src.Island.column) { target = kb; break; }
        if (target == null) { Line(false, "drop", "no target grid"); yield break; }
        Frame(target.Center, 18f); yield return Wait(0.2f);
        int on0 = CubesOn(target), d0 = HudClipboard.DropCount, p0 = pushes;
        chip.SimDrop(target);
        yield return Wait(0.9f);
        Line(HudClipboard.DropCount == d0 + 1 && HudClipboard.LastDropTarget == target && CubesOn(target) == on0 + 1 && pushes - p0 == 1 && pm.Hand == PathManager.HandKind.Empty,
             "dragging the chip onto a grid pastes there (one History entry); the hand goes back to empty", "cubes there " + on0 + " → " + CubesOn(target) + ", pushes +" + (pushes - p0) + ", hand " + pm.Hand);
        UIManager.I.SetHudVisible(false);
        yield return Wait(0.6f);
        bool hid = !chip.Shown && !chip.Root.gameObject.activeSelf;
        UIManager.I.SetHudVisible(true);
        yield return Wait(0.6f);
        Line(hid && chip.Shown, "the chip leaves with the HUD (presenting, menus, the prompt) and comes back with it");
        bool calm = !chip.ClearShown;
        chip.Card.OnPointerEnter(null);
        yield return null; yield return null;
        bool onHover = chip.ClearShown;
        chip.Card.OnPointerExit(null);
        chip.ShowClear();
        chip.ClearButton.onClick();
        yield return Wait(0.5f);
        Line(calm && onHover && !Clipboard.HasPattern && !chip.Root.gameObject.activeSelf, "the × shows only while the chip is hovered (one target at rest); it clears the clipboard and the chip leaves",
             "at rest " + !calm + ", hovered " + onHover);
    }

    // ================================================================== 6. hotkeys
    static IEnumerator Hotkeys()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        Key(KeyCode.Alpha3); yield return null;
        bool a = pm.CubeInHand && pm.selectedInstrument == 2;
        Key(KeyCode.Alpha3); yield return null;
        bool b = pm.Hand == PathManager.HandKind.Empty;
        Key(KeyCode.Alpha3); yield return null; Key(KeyCode.Alpha6); yield return null;
        bool c = pm.CubeInHand && pm.selectedInstrument == 5;
        Key(KeyCode.Alpha0); yield return null;
        bool d = pm.CubeInHand && pm.selectedInstrument == 9;
        Line(a && b && c && d, "1–9 / 0 pick up the groups' cubes; the same key again puts it down; another key swaps", a + " " + b + " " + c + " " + d);
        // Esc (a real frame: PathManager's LateUpdate puts the hand down; nothing else takes the press)
        bool stripOpen0 = UIManager.I.Strip != null && UIManager.I.Strip.IsOpen;
        KeyShim.Sim(KeyCode.Escape, true, true, false); yield return null; KeyShim.Clear(); yield return null;
        bool escOk = pm.Hand == PathManager.HandKind.Empty && (UIManager.I.Strip == null || UIManager.I.Strip.IsOpen == stripOpen0);
        Line(escOk, "Esc puts the hand down and does nothing else (the menu strip stays shut)", "hand " + pm.Hand + ", strip " + (UIManager.I.Strip != null && UIManager.I.Strip.IsOpen));
        // Esc while inspecting: the card closes first, the hand stays; digits recolour the inspected cube
        var ic = PitchedCube(x => x.nodes.Count >= 2);
        HudInstruments.ToggleHand(4, -1);
        Frame(ic.Island.Center, 13f);
        CubeInspector.Open(ic);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open, 3f);
        int inst0 = ic.instrument, want = inst0 == 6 ? 7 : 6;
        KeyShim.Sim(KeyCode.Alpha1 + want, true, true, false); yield return null; KeyShim.Clear(); yield return null;
        bool recol = ic.instrument == want && pm.CubeInHand && pm.selectedInstrument == 4;
        KeyShim.Sim(KeyCode.Escape, true, true, false); yield return null; KeyShim.Clear(); yield return null;
        bool closing = CubeInspector.State == CubeInspector.Phase.Closing || CubeInspector.State == CubeInspector.Phase.Closed;
        Line(recol && closing && pm.CubeInHand, "while inspecting: a digit recolours the inspected cube (the hand keeps its cube); Esc closes the card first, the hand stays",
             "instrument " + inst0 + " → " + ic.instrument + ", inspector " + CubeInspector.State + ", hand " + pm.Hand);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
        pm.PutDown(); Clipboard.Clear();
        // Cmd+C on the hovered cube
        var hc = PitchedCube(x => x.nodes.Count >= 3);
        Frame(hc.Island.Center, 14f); yield return Wait(0.2f);
        PathManager.SimPos = ScreenOf(hc.transform.position);
        yield return null; yield return null;
        var cand = pm.Candidate;
        Key(KeyCode.C, true); yield return null;
        Line(cand == hc && Clipboard.HasPattern && Clipboard.SourceCube == hc, "Cmd+C copies the hovered cube", "candidate " + (cand == hc) + ", clipboard " + (Clipboard.SourceCube == hc));
        // Cmd+V onto the hovered grid
        KeyBlock tgt = null; TileInteraction tt = null;
        foreach (var kb in sm.Islands) if (kb != null && kb != hc.Island && !kb.IsMoon && Clipboard.CanPasteOn(kb)) { tt = FreeTile(kb, 2); if (tt != null) { tgt = kb; break; } }
        Frame(tgt.Center, 16f); yield return Wait(0.2f);
        PathManager.SimPos = ScreenOf(tt.Top);
        yield return null; yield return null;
        int on0 = CubesOn(tgt), p0 = pushes;
        Key(KeyCode.V, true);
        yield return Wait(0.8f);
        Line(CubesOn(tgt) == on0 + 1 && pushes - p0 == 1, "Cmd+V pastes onto the hovered grid (one History entry)", "cubes there " + on0 + " → " + CubesOn(tgt) + ", pushes +" + (pushes - p0));
        // Cmd+V with nothing hovered: the next grid after the copied cube's home
        var next = Clipboard.NextGrid(Clipboard.SourceIsland);
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null; yield return null;
        int n0 = next != null ? CubesOn(next) : -1; p0 = pushes;
        Key(KeyCode.V, true);
        yield return Wait(0.8f);
        Line(next != null && CubesOn(next) == n0 + 1 && pushes - p0 == 1, "Cmd+V with nothing hovered pastes onto the next grid after the copied cube's home", "next " + (next != null ? "column " + next.column : "none") + ", cubes " + n0 + " → " + (next != null ? CubesOn(next) : -1));
        Clipboard.Clear(); pm.PutDown();
        // N (SPEC §11.4): a Moon whose section starts at the focused column (the selected island's)
        int col = -1, sel = -1;
        for (int i = 0; i < sm.Islands.Count && sel < 0; i++) if (sm.Islands[i] != null && !sm.Islands[i].IsMoon && sm.Islands[i].column >= 1) { sel = i; col = sm.Islands[i].column; }
        if (sel >= 0)
        {
            FocusLoop.Dismiss(); GlobalClock.Stop();   // (the inspector above began the focus loop: while it plays the LIT column wins)
            yield return null;
            UIManager.I.SelectMeasure(sel, false);
            int m0 = sm.Moons.Count; p0 = pushes;
            Key(KeyCode.N); yield return null; yield return null;
            int m1 = sm.Moons.Count - 1;
            Line(sm.Moons.Count == m0 + 1 && sm.MoonStartColumn(m1) == col && pushes - p0 == 1, "N adds a drum Moon whose part starts at the focused column (one History entry)",
                 "moons " + m0 + " → " + sm.Moons.Count + ", start column " + (m1 >= 0 ? sm.MoonStartColumn(m1) : -1) + " (focused " + col + "), pushes +" + (pushes - p0));
            UIManager.I.ClearSelection();
            // while playing, the lit column
            int litCol = sm.ColumnCount > 2 ? 2 : sm.ColumnCount - 1;
            GlobalClock.Seek(sm.ColumnStart(litCol) + 0.5f); GlobalClock.Play();
            yield return Wait(0.3f);
            int lit = sm.LitColumn; m0 = sm.Moons.Count;
            int lateN = Synth.LateEvents;
            Key(KeyCode.N); yield return Wait(0.4f);
            m1 = sm.Moons.Count - 1;
            GlobalClock.Stop();
            lateApart += Synth.LateEvents - lateN;
            Line(sm.Moons.Count == m0 + 1 && sm.MoonStartColumn(m1) == lit, "while playing, N starts the Moon at the lit column", "lit " + lit + ", start " + (m1 >= 0 ? sm.MoonStartColumn(m1) : -1));
        }
        else Line(false, "N: no island past column 0 in the fixture");
    }

    // ================================================================== 7. per-frame cost
    static IEnumerator Cost()
    {
        var pm = PathManager.I; var sm = SongManager.I; var col = HudInstruments.I; var chip = UIManager.I.ClipboardChip;
        var kb = sm.Islands[0];
        Frame(kb.Center, 16f); yield return Wait(0.2f);
        var t = FreeTile(kb, 1);
        Clipboard.Copy(PitchedCube());
        HudInstruments.ToggleHand(6, -1);
        if (t != null) PathManager.SimPos = ScreenOf(t.Top);
        yield return Wait(0.6f);
        for (int i = 0; i < 3; i++) { col.StepForTest(); chip.StepForTest(); CursorKit.TickWorld(false); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) { col.StepForTest(); chip.StepForTest(); CursorKit.TickWorld(false); }
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        Line(a1 - a0 == 0, "no per-frame GC: 120 frames of the column (a held chip swaying), the clipboard chip and the cursor rider allocate nothing", (a1 - a0) + " bytes");
        double ms = sw.Elapsed.TotalMilliseconds / 120.0;
        Line(ms < 0.3, "their per-frame work is small (< 0.3 ms)", ms.ToString("F4") + " ms / frame (editor)");
        pm.PutDown(); Clipboard.Clear();
    }
}
