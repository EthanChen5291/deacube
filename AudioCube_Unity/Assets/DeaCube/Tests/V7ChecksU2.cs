using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Package U2 (SPEC v7 §8, §15, §16.4, §17.3; scratchpad/v7/brief_U2.md) Play-mode checks, PathManager.AutoHand OFF (the real game):
/// the SIZE ROW (a cube in the hand → the row beside the held chip, the chosen size = PathManager.BrushTicks, a click sets the brush — the
/// chosen size again / the dot = dotted —, the row follows the brush keys, the pick-up pulse, ≥ 28 px targets a real click reaches, a press on
/// it never finishes a draft, it steps aside for the fan / mix / the inspector), the cursor (the riding cube IS the brush: its size and dot;
/// the size echo while drawing), the cube card's IDEAS row (octave copy ↑ / ↓, harmony ↑ / ↓ with the hover preview, flip — each one History
/// entry through CubeOps; the row steps aside for a selected note; the face stays ≤ 20 targets), the PASTE FAN on paste → next grid (six modes
/// + late; a pick pastes on the next grid in that mode) and on the clipboard chip (right-click / hold; a pick = the pattern in the hand in that
/// mode; a drag = a paste there), the SELECTION BAR (over the selected grids; copy, paste after, duplicate, delete, octave ↑ / ↓, repeat,
/// extend (§21: the long grid), the build-up (the launch, §21.4) through SongOps; the keys), the HOTKEYS (⇧↑ / ⇧↓, ⌥↑ / ⌥↓, U, ⌘↑ / ⌘↓,
/// ⌘⌥↑ / ⌘⌥↓, ⌘⇧V, ⇧→, `?`; the old bindings kept; §21: R, the rider, retired — no rider card in the more drawer, no rider row on the sheet),
/// the SHORTCUT SHEET (every shortcut, Esc / ? close, keys locked while up), no per-frame GC; Synth late / errors 0; the user's save untouched.
/// Start with <see cref="RunAll"/>, poll <see cref="Done"/> or Captures/u2_7_report.txt. Captures: u2_7_size_row, u2_7_rider_brush,
/// u2_7_echo_drawing, u2_7_card_ideas, u2_7_harmony_preview, u2_7_paste_fan, u2_7_paste_echo, u2_7_clipboard_fan, u2_7_cursor_mode,
/// u2_7_selection_bar, u2_7_shortcuts; §21: u2_7_card_more (the more drawer without the rider), u2_7_selection_bar_on (extend + build-up on).
/// </summary>
public static class V7ChecksU2
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u2_7_report.txt");
    static StringBuilder sb; static int num, pass, fail, pushes;
    static bool captures;
    /// <summary>Synth late events counted apart: a ScreenCapture taken while the song plays (the inspector's focus loop) stalls the main thread
    /// (PROTOCOL: "a ScreenCapture mid-play stalls the main thread: count those apart").</summary>
    static int lateApart;
    /// <summary>Only these parts (a comma list of names: size, rider, card, fan, clip, select, keys, sheet, cost; empty = all).</summary>
    public static string Only = "";

    static void Line(bool ok, string what, string detail = null)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append("U2.7.").Append(num).Append(' ').Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" — ").Append(detail);
        sb.Append('\n');
        Flush(false);
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }
    static void Flush(bool final)
    {
        Report = "V7ChecksU2: " + pass + " PASS, " + fail + " FAIL" + (final ? "" : " (running)") + "\n" + sb;
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
        int l0 = Synth.LateEvents; bool playing = GlobalClock.IsPlaying;
        ScreenCapture.CaptureScreenshot(p, 1);
        Info("capture " + file + " (" + Screen.width + "x" + Screen.height + ")");
        yield return null; yield return null;
        yield return Wait(0.3f);
        int d = Synth.LateEvents - l0;
        if (playing && d > 0) { lateApart += d; Info("late +" + d + " during that capture while playing (the capture stalls the main thread; counted apart)"); }
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
        GridSelection.Clear();
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
        HudShortcuts.Close();
        Clipboard.EchoLate = false;
        if (UIManager.I != null && UIManager.I.ClipboardChip != null && UIManager.I.ClipboardChip.Fan != null) UIManager.I.ClipboardChip.Fan.Close();
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

    static int CubeCount() { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) n++; return n; }
    static int CubesOn(KeyBlock kb) { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == kb) n++; return n; }
    static AudioCube CubeById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }

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

    /// <summary>Every button's hit ≥ 28 reference px on both sides, and a real click at its centre reaches it.</summary>
    static bool Reachable(IList<InkButton> bs, out string detail)
    {
        float s = RefScale; int ok = 0; string miss = null; float minPx = float.MaxValue;
        foreach (var b in bs)
        {
            if (b == null) { if (miss == null) miss = "null"; continue; }
            var r = ScreenRect((RectTransform)b.transform);
            minPx = Mathf.Min(minPx, Mathf.Min(r.width, r.height) / s);
            string got;
            if (TopHit(b.transform, r.center, out got)) ok++; else if (miss == null) miss = b.name + " → " + got;
        }
        detail = ok + " / " + bs.Count + " reached, smallest hit " + minPx.ToString("F0") + " ref px" + (miss != null ? ", first miss " + miss : "");
        return ok == bs.Count && minPx >= 28f - 0.5f;
    }

    static void Key(KeyCode k, bool cmd = false, bool shift = false, bool alt = false)
    {
        if (cmd) KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
        if (shift) KeyShim.Sim(KeyCode.LeftShift, false, true, false);
        if (alt) KeyShim.Sim(KeyCode.LeftAlt, false, true, false);
        KeyShim.Sim(k, true, true, false);
        UIManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static float RefScale => UIManager.I != null && UIManager.I.Canvas != null ? Mathf.Max(0.01f, UIManager.I.Canvas.scaleFactor) : 1f;

    /// <summary>A control's hover caption (InkCaption.Attach's words; "" none).</summary>
    static string CaptionOf(Component c) { var h = c != null ? c.GetComponent<InkCaptionHover>() : null; return h != null ? h.words : ""; }

    static IEnumerator OpenCard(AudioCube c)
    {
        Frame(c.Island.Center, 13f); yield return null;
        CubeInspector.Open(c);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open && InspectorCard.Shown >= 1f, 4f);
        yield return Wait(0.3f);
    }

    static IEnumerator CloseCard()
    {
        CubeInspector.Close();
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 4f);
        yield return null;
    }

    static bool Wanted(string part) { if (string.IsNullOrEmpty(Only)) return true; foreach (var p in Only.Split(',')) if (p.Trim() == part) return true; return false; }

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
        int brush0 = PathManager.I.BrushTicks;
        PathManager.AutoHand = false;
        History.OnChanged += OnHistory;
        var parts = new List<KeyValuePair<string, IEnumerator>>
        {
            new KeyValuePair<string, IEnumerator>("size", SizeRow()), new KeyValuePair<string, IEnumerator>("rider", Rider()),
            new KeyValuePair<string, IEnumerator>("card", Card()), new KeyValuePair<string, IEnumerator>("fan", CardFan()),
            new KeyValuePair<string, IEnumerator>("clip", ClipFan()), new KeyValuePair<string, IEnumerator>("select", Selection()),
            new KeyValuePair<string, IEnumerator>("keys", Hotkeys()), new KeyValuePair<string, IEnumerator>("sheet", Sheet()),
            new KeyValuePair<string, IEnumerator>("cost", Cost()),
        };
        foreach (var kv in parts)
        {
            if (!Wanted(kv.Key)) continue;
            var part = kv.Value;
            Prepare();
            LoadFixture();
            PathManager.I.PutDown(); Clipboard.Clear(); PathManager.I.SetBrush(24);
            PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            yield return null; yield return null;
            int partLate0 = Synth.LateEvents, partApart0 = lateApart;
            while (true)
            {
                object cur;
                try { if (!part.MoveNext()) break; cur = part.Current; }
                catch (Exception e) { Line(false, "exception in " + kv.Key, e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
                yield return cur;
            }
            KeyShim.Clear();
            int pl = Synth.LateEvents - partLate0, pa = lateApart - partApart0;
            if (pl > 0) Info("part " + kv.Key + ": Synth late +" + pl + " (" + pa + " of them during captures while playing)");
        }
        // ---- leave things as found
        Clipboard.Clear(); Clipboard.EchoLate = false; GridSelection.Clear(); HudShortcuts.Close();
        if (PathManager.I != null) { PathManager.I.PutDown(); PathManager.I.SetBrush(brush0); }
        if (HudInstruments.I != null) { HudInstruments.I.CloseFan(); HudInstruments.I.SetMix(false); }
        CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss(); GlobalClock.Stop();
        PathManager.SimOnly = false; KeyShim.Clear(); CursorKit.ResetAll();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        try { LoadFixture(); } catch (Exception) { }
        History.OnChanged -= OnHistory;
        Onboarding.Suppressed = suppressed; PathManager.AutoHand = autoHand;
        Line(Synth.LateEvents - lateApart == late0 && Synth.Errors == err0, "Synth late / errors unchanged (late notes during captures taken while playing counted apart)",
             "late +" + (Synth.LateEvents - late0 - lateApart) + " (+" + lateApart + " apart), errors +" + (Synth.Errors - err0));
        Line(Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + userMd5);
        Flush(true);
        Done = true;
    }

    // ================================================================== 1. the size row
    static IEnumerator SizeRow()
    {
        var pm = PathManager.I; var col = HudInstruments.I; var row = UIManager.I.SizeRow;
        if (row == null) { Line(false, "size row", "not built"); yield break; }
        yield return Wait(0.3f);
        Line(!row.Shown, "an empty hand: no size row");
        int pulse0 = HudSizeRow.PulseCount, p0 = pushes;
        col.PickOf(3).onClick();
        yield return Wait(0.5f);
        var chipR = ScreenRect(col.ChipOf(3).rectTransform); var rowR = ScreenRect(row.Row);
        float dy = Mathf.Abs(rowR.center.y - chipR.center.y) / RefScale, gap = (rowR.xMin - chipR.xMax) / RefScale;
        Line(row.Shown && gap > 2f && gap < 60f && dy < 30f, "a cube in the hand: the size row sticks out beside the held chip (right of it, level with it)",
             "gap " + gap.ToString("F0") + " ref px, dy " + dy.ToString("F0"));
        Line(HudSizeRow.PulseCount == pulse0 + 1 && pushes == p0, "picking the cube up pulses the chosen size once (\"the last used size is preselected and pulses once\"); no History entry",
             "pulses +" + (HudSizeRow.PulseCount - pulse0));
        int sel = row.SelectedSize; bool ringOk = true;
        for (int i = 0; i < 5; i++) ringOk &= row.CubeOf(i).Ring == (i == sel);
        Line(sel == pm.BrushSize && pm.BrushTicks == 24 && sel == 2 && ringOk, "the chosen size is the brush (a quarter: the middle cube wears the ink hexagon)", "size " + sel + ", brush " + pm.BrushTicks);
        float[] px = new float[5]; bool grows = true;
        for (int i = 0; i < 5; i++) { px[i] = row.SlotCubePx(i); if (i > 0 && px[i] <= px[i - 1]) grows = false; }
        Line(grows && px[4] / px[0] > 3f, "five cube sizes, tiny (a sixteenth) → fat (a whole note)", px[0].ToString("F0") + " … " + px[4].ToString("F0") + " px");
        var bs = new List<InkButton>(); for (int i = 0; i < 5; i++) bs.Add(row.Slot(i)); bs.Add(row.DotButton);
        string det;
        Line(Reachable(bs, out det), "its six targets (five sizes + the dot) are ≥ 28 px and a real click reaches each", det);
        yield return Shot("u2_7_size_row.png");
        // clicks set the brush (no History: nothing is placed yet)
        int ch0 = PathManager.BrushChanges; p0 = pushes;
        row.SimClick(4); yield return null;
        bool whole = pm.BrushTicks == 96 && row.SelectedSize == 4 && !row.SelectedDotted;
        row.SimClick(4); yield return null;
        bool dotted = pm.BrushTicks == 144 && row.SelectedDotted && row.CubeOf(4).Satellite && row.DotButton.Active;
        row.SimClick(5); yield return null;
        bool undot = pm.BrushTicks == 96 && !row.SelectedDotted;
        row.SimClick(0); yield return null;
        bool tiny = pm.BrushTicks == 6 && row.SelectedSize == 0;
        Line(whole && dotted && undot && tiny && pushes == p0 && PathManager.BrushChanges >= ch0 + 4,
             "a click sets the brush: the fat cube = a whole note, the same cube again = dotted (the satellite), the dot = undotted, the tiny one = a sixteenth; no History entry",
             whole + " " + dotted + " " + undot + " " + tiny + ", brush changes +" + (PathManager.BrushChanges - ch0) + ", pushes +" + (pushes - p0));
        // the brush keys / wheel (package D) move the row too
        pm.StepBrush(1); yield return null; yield return null;
        bool follows = row.SelectedSize == 1 && pm.BrushTicks == 12;
        pm.ToggleBrushDotted(); yield return null; yield return null;
        follows &= row.SelectedDotted && pm.BrushTicks == 18;
        Line(follows, "the row follows the brush when [ ] . / the wheel change it (PathManager.StepBrush / ToggleBrushDotted)", "brush " + pm.BrushTicks + ", row " + row.SelectedSize + (row.SelectedDotted ? " dotted" : ""));
        int au0 = HudSizeRow.AuditionCount;
        row.Slot(3).SimHover(true); yield return null; row.Slot(3).SimHover(false);
        Line(HudSizeRow.AuditionCount == au0 + 1, "hovering a size auditions that length", "auditions +" + (HudSizeRow.AuditionCount - au0));
        // a press on the row is the row's: PathManager.DraftUiHover while the pointer is on it
        PathManager.SimPos = ScreenRect((RectTransform)row.Slot(2).transform).center;
        yield return null; yield return null;
        bool over = PathManager.DraftUiHover && HudSizeRow.PointerOver;
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null; yield return null;
        Line(over && !PathManager.DraftUiHover, "the pointer on the row claims it for the draft (PathManager.DraftUiHover: a press there never finishes a path)", "on " + over + ", off " + !PathManager.DraftUiHover);
        // it steps aside for the voice fan and mix mode
        col.OpenFan(3); yield return Wait(0.35f);
        bool fanAway = !row.Shown;
        col.CloseFan(); yield return Wait(0.4f);
        bool back = row.Shown;
        col.SetMix(true); yield return Wait(0.35f);
        bool mixAway = !row.Shown;
        col.SetMix(false); yield return Wait(0.4f);
        Line(fanAway && back && mixAway && row.Shown, "it steps aside for the voice fan and mix mode and comes back", fanAway + " " + back + " " + mixAway);
        // another group: the pulse again; put down: gone
        pulse0 = HudSizeRow.PulseCount;
        col.PickOf(6).onClick();
        yield return Wait(0.12f);
        float ps = row.PulseScale;
        yield return Wait(0.5f);
        Line(HudSizeRow.PulseCount == pulse0 + 1 && ps > 1.02f && Mathf.Abs(row.PulseScale - 1f) < 1e-3f, "another cube in the hand pulses the chosen size again (it swells, then settles)", "scale mid-pulse " + ps.ToString("F2"));
        pm.PutDown(); yield return Wait(0.45f);
        Line(!row.Shown, "the hand put down: the row folds away");
    }

    // ================================================================== 2. the cursor: the riding cube is the brush, the echo while drawing
    static IEnumerator Rider()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.3f);
        var t = FreeTile(kb, 3);
        if (t == null) { Line(false, "rider", "no free tile"); yield break; }
        HudInstruments.ToggleHand(5, -1);
        PathManager.SimPos = ScreenOf(t.Top);
        pm.SetBrush(6); yield return Wait(0.2f);
        float small = CursorKit.RiderCubePx;
        pm.SetBrush(96); yield return Wait(0.2f);
        float big = CursorKit.RiderCubePx; bool bigDot = CursorKit.RiderDotted;
        yield return Shot("u2_7_rider_brush.png");
        pm.SetBrush(144); yield return Wait(0.2f);
        bool dot = CursorKit.RiderDotted;
        pm.SetBrush(24); yield return Wait(0.2f);
        float mid = CursorKit.RiderCubePx;
        Line(Mathf.Abs(small - DurationPicker.SizePx(0.25f)) < 0.5f && Mathf.Abs(big - DurationPicker.SizePx(4f)) < 0.5f && Mathf.Abs(mid - 30f) < 0.5f && !bigDot && dot && CursorKit.RiderKind == 1,
             "the cube riding the cursor IS the brush: a sixteenth tiny, a whole note fat, a quarter the v6 size; a dotted brush wears the satellite",
             small.ToString("F1") + " / " + mid.ToString("F1") + " / " + big.ToString("F1") + " px, dotted " + dot);
        // drawing: the rider leaves, the size echo rides next to the cursor (away from the draft's own length row)
        var t2 = FreeTile(kb, 0) ?? t;
        Vector3 s2 = ScreenOf(t2.Top);
        PathManager.SimPos = s2;
        pm.SimPointer(s2, true, true, false); pm.SimPointer(s2, false, false, true);
        yield return Wait(0.25f);
        bool drawing = pm.IsDrawing;
        var dr = DurationPicker.RowScreenRect;
        bool nearRow = DurationPicker.IsShown && dr.width > 1f && Mathf.Abs(s2.x - Mathf.Clamp(s2.x, dr.xMin, dr.xMax)) < 70f * Screen.height / 1080f && Mathf.Abs(s2.y - Mathf.Clamp(s2.y, dr.yMin, dr.yMax)) < 70f * Screen.height / 1080f;
        bool echo = CursorKit.EchoShown, echoSize = CursorKit.EchoSize == pm.BrushSize;
        pm.StepBrush(1); yield return Wait(0.15f);
        bool echoFollows = !CursorKit.EchoShown || CursorKit.EchoSize == pm.BrushSize;
        Vector2 es = CursorKit.EchoScreen;
        float edx = (es.x - s2.x) / RefScale, edy = (es.y - s2.y) / RefScale;
        yield return Shot("u2_7_echo_drawing.png");
        if (!nearRow)
            Line(drawing && CursorKit.RiderKind == 0 && echo && echoSize && echoFollows && edx > 10f && edx < 110f && edy < 0f && edy > -80f,
                 "while a path is drawn the rider leaves and a small echo of the size row rides beside the cursor (the chosen size, following [ ] .)",
                 "drawing " + drawing + ", echo " + echo + " size " + CursorKit.EchoSize + " (brush " + pm.BrushSize + "), at (" + edx.ToString("F0") + ", " + edy.ToString("F0") + ") ref px");
        else Line(drawing && !echo, "while a path is drawn next to the draft's own length row the echo stays away (the row is the echo)", "echo " + echo);
        pm.CancelPath(); yield return null; yield return null;
        Line(!CursorKit.EchoShown, "the echo leaves with the draft");
        pm.PutDown(); PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null;
    }

    // ================================================================== 3. the cube card: the ideas row
    static IEnumerator Card()
    {
        var pm = PathManager.I;
        var c = PitchedCube(x => x.Island.cols >= 4 && x.nodes.Count >= 3 && InspectorCard.NextGridOf(x) != null);
        if (c == null) { Line(false, "card", "no pitched cube"); yield break; }
        yield return OpenCard(c);
        int face = InspectorCard.FaceTargetCount();
        var ideas = new List<InkButton> { InspectorCard.OctaveCopyButton(1), InspectorCard.OctaveCopyButton(-1), InspectorCard.HarmonyButton(true), InspectorCard.HarmonyButton(false), InspectorCard.FlipButton };
        string det;
        bool reach = Reachable(ideas, out det);
        Line(InspectorCard.IdeasShown && face <= 20 && reach, "the card's ideas row: octave copy ↑ / ↓, harmony ↑ / ↓, flip — a real click reaches each; the face stays ≤ 20 targets",
             face + " face targets; " + det);
        // a selected note: the length row takes the slot
        var strip = InspectorCard.Strip;
        strip.SelectEvent(0, true); yield return null; yield return null;
        int face2 = InspectorCard.FaceTargetCount(); bool away = !InspectorCard.IdeasShown && DurationPicker.IsShown;
        strip.Deselect(); yield return null; yield return null;
        Line(away && face2 <= 22 && InspectorCard.IdeasShown, "a selected note's length row takes the ideas' slot (face ≤ 22 with v9's higher / lower); deselected, the ideas are back", "face " + face2);
        InspectorCard.OctaveCopyButton(1).SimHover(true); yield return Wait(0.2f);
        yield return Shot("u2_7_card_ideas.png");
        InspectorCard.OctaveCopyButton(1).SimHover(false);
        // octave copies: a new cube in the layer above / below, echoOf = the source (CubeOps, one History entry each)
        int n0 = CubeCount(), p0 = pushes, i0 = InspectorCard.IdeaCount;
        InspectorCard.OctaveCopyButton(1).Click();
        yield return Wait(0.2f);
        var up = InspectorCard.IdeaCount > i0 ? InspectorCard.LastIdea : null;
        Line(up != null && up != c && up.layer == c.layer + 1 && up.echoOf == c.id && CubeCount() == n0 + 1 && pushes - p0 == 1,
             "octave copy ↑: a new cube in the layer above (echoOf = the source), one History entry; the card stays",
             up != null ? "layer " + up.layer + ", echoOf " + up.echoOf + " (source " + c.id + "), cubes +" + (CubeCount() - n0) + ", pushes +" + (pushes - p0) : "refused (CubeOps.OctaveCopy returned null)");
        p0 = pushes; i0 = InspectorCard.IdeaCount;
        InspectorCard.OctaveCopyButton(-1).Click();
        yield return Wait(0.2f);
        var dn = InspectorCard.IdeaCount > i0 ? InspectorCard.LastIdea : null;
        Line(dn != null && dn.layer == c.layer - 1 && dn.echoOf == c.id && pushes - p0 == 1 && CubeInspector.IsOpen && InspectorCard.Cube == c,
             "octave copy ↓: the layer below, one History entry; the card stays on the source", dn != null ? "layer " + dn.layer : "refused");
        // harmony: hover previews (ghost beads + both voices), click makes the support voice
        int pv0 = InspectorCard.PreviewCount;
        InspectorCard.HarmonyButton(true).SimHover(true);
        yield return Wait(0.35f);
        bool previewing = InspectorCard.HarmonyPreviewing && InspectorCard.PreviewCount == pv0 + 1;
        yield return Shot("u2_7_harmony_preview.png");
        InspectorCard.HarmonyButton(true).SimHover(false);
        yield return null;
        Line(previewing && !InspectorCard.HarmonyPreviewing, "hovering harmony ↑ previews it (CubeOps.PreviewHarmony: ghost beads + both voices); leaving ends it (EndPreview)");
        p0 = pushes; i0 = InspectorCard.IdeaCount; n0 = CubeCount();
        InspectorCard.HarmonyButton(true).Click();
        yield return Wait(0.2f);
        var hm = InspectorCard.IdeaCount > i0 ? InspectorCard.LastIdea : null;
        Line(hm != null && hm != c && hm.echoOf == c.id && CubeCount() == n0 + 1 && pushes - p0 == 1, "harmony ↑: a support voice cube (echoOf = the source), one History entry",
             hm != null ? "cube " + hm.id + " on column " + (hm.Island != null ? hm.Island.column : -1) : "refused (CubeOps.Harmonize returned null)");
        // flip: the path upside down, once more = back
        var before = new List<Vector2Int>(); foreach (var nd in c.nodes) before.Add(new Vector2Int(nd.gridX, nd.gridZ));
        p0 = pushes;
        InspectorCard.FlipButton.Click();
        yield return Wait(0.15f);
        bool changed = false; for (int i = 0; i < c.nodes.Count && i < before.Count; i++) if (c.nodes[i].gridX != before[i].x || c.nodes[i].gridZ != before[i].y) changed = true;
        int p1 = pushes - p0;
        InspectorCard.FlipButton.Click();
        yield return Wait(0.15f);
        bool back = c.nodes.Count == before.Count; for (int i = 0; back && i < c.nodes.Count; i++) back = c.nodes[i].gridX == before[i].x && c.nodes[i].gridZ == before[i].y;
        Line(changed && back && p1 == 1 && pushes - p0 == 2, "flip: the path upside down (one History entry); flipped again it is back", "changed " + changed + ", back " + back + ", pushes " + p1 + " / " + (pushes - p0));
        // v7 §21 ("just keep cubes on each grid"): the more drawer has no rider card any more, and R (the v3 rider key) does nothing
        InspectorCard.MoreTab.Click();
        yield return WaitFor(() => InspectorCard.DrawerShown >= 1f, 1.5f); yield return Wait(0.3f);
        int namedRider = 0; foreach (var b in InspectorCard.Root.GetComponentsInChildren<InkButton>(true)) if (b.name == "Rider") namedRider++;
        var drawerCards = new List<InkButton> { InspectorCard.DrawerButton("twin"), InspectorCard.DrawerButton("shadow"), InspectorCard.DrawerButton("echo"), InspectorCard.DrawerButton("stamp"), InspectorCard.DrawerButton("up") };
        string ddet;
        bool drawerReach = Reachable(drawerCards, out ddet);
        yield return Shot("u2_7_card_more.png");
        bool rider0 = c.rider; int pr0 = pushes;
        Key(KeyCode.R); yield return null;
        Line(InspectorCard.DrawerButton("rider") == null && namedRider == 0 && drawerReach && c.rider == rider0 && pushes == pr0,
             "§21: the more drawer has no rider card (twin, shadow, echo / stamp, octave — reachable) and R does nothing (cubes never leave their grid)", ddet + ", R pushes +" + (pushes - pr0));
        InspectorCard.MoreTab.Click(); yield return Wait(0.3f);
        // the layer limit: a copy of a copy at the top layer cannot go higher
        yield return CloseCard();
        if (up != null)
        {
            var top = up;
            for (int k = 0; k < 3 && top != null && top.layer < ProjectConfig.MaxLayer; k++) top = CubeOps.OctaveCopy(top, 1);
            if (top != null && top.layer >= ProjectConfig.MaxLayer)
            {
                yield return OpenCard(top);
                Line(!InspectorCard.OctaveCopyButton(1).Interactable && InspectorCard.OctaveCopyButton(-1).Interactable, "at the top layer the octave copy ↑ is off (↓ stays)", "layer " + top.layer);
                yield return CloseCard();
            }
            else Line(false, "layer limit", "could not reach the top layer");
        }
        // drums: no octaves, harmonies or flips
        AudioCube drum = null;
        foreach (var x in SequenceMaster.Cubes) if (x != null && x.isFinalized && x.IsDrums && x.Island != null) { drum = x; break; }
        if (drum != null)
        {
            yield return OpenCard(drum);
            Line(!InspectorCard.OctaveCopyButton(1).Interactable && !InspectorCard.HarmonyButton(true).Interactable && !InspectorCard.FlipButton.Interactable, "a drum cube: the ideas are off");
            yield return CloseCard();
        }
        else Info("no drum cube on a grid in the fixture: the drums case is skipped");
    }

    // ================================================================== 4. the paste fan on paste → next grid
    static IEnumerator CardFan()
    {
        var pm = PathManager.I;
        var c = PitchedCube(x => x.Island.cols >= 4 && x.nodes.Count >= 3 && InspectorCard.NextGridOf(x) != null);
        if (c == null) { Line(false, "fan", "no pitched cube"); yield break; }
        yield return OpenCard(c);
        var fan = InspectorCard.Fan;
        InspectorCard.PasteNextButton.SimHover(true);
        yield return Wait(0.12f);
        bool early = fan.IsOpen;
        yield return Wait(0.35f);
        bool opened = fan.IsOpen;
        var chips = new List<InkButton>(); foreach (var m in PasteFan.Modes) chips.Add(fan.Chip(m)); chips.Add(fan.LateButton);
        string det = "not open";
        bool reach = opened && Reachable(chips, out det);
        Line(!early && opened && reach, "hovering paste → next grid fans the paste modes after a short rest: plain, echo ↑, echo ↓, step ↑, step ↓, answer + late (≥ 28 px, reachable)", det);
        yield return Shot("u2_7_paste_fan.png");
        bool late0 = Clipboard.EchoLate;
        fan.LateButton.Click(); yield return null;
        bool lateOn = Clipboard.EchoLate && fan.LateButton.Active;
        fan.LateButton.Click(); yield return null;
        Line(!late0 && lateOn && !Clipboard.EchoLate, "the late toggle sets Clipboard.EchoLate (echoes land an eighth late) and back");
        InspectorCard.PasteNextButton.SimHover(false);
        yield return Wait(0.5f);
        Line(!fan.IsOpen, "the fan folds once the pointer has left it and the button");
        // echo ↑ onto the next grid: the card closes, then the pattern lands in the layer above
        var target = InspectorCard.NextGridOf(c);
        int on0 = CubesOn(target), p0 = pushes;
        InspectorCard.PasteNextButton.SimHover(true); yield return Wait(0.4f);
        fan.Chip(Clipboard.PasteMode.EchoUp).Click();
        yield return WaitFor(() => !InspectorCard.PastePending && CubeInspector.State == CubeInspector.Phase.Closed, 4f);
        yield return Wait(0.3f);
        yield return Shot("u2_7_paste_echo.png");
        yield return Wait(0.5f);
        var made = InspectorCard.LastPasteNext;
        Line(InspectorCard.LastPasteMode == Clipboard.PasteMode.EchoUp && made != null && made.Island == target && made.layer == c.layer + 1 && pushes - p0 == 1 && !CubeInspector.IsOpen,
             "a fan pick (echo ↑): the card closes, the copy lands on the next grid in the layer above — one History entry",
             "mode " + InspectorCard.LastPasteMode + ", made " + (made != null ? "layer " + made.layer + " on column " + (made.Island != null ? made.Island.column : -1) : "none") + ", pushes +" + (pushes - p0));
        // step ↓ and answer through the fan (the mode reaches Clipboard.PasteNext)
        foreach (var m in new[] { Clipboard.PasteMode.StepDown, Clipboard.PasteMode.Answer })
        {
            yield return OpenCard(c);
            InspectorCard.PasteNextButton.onRightClick();   // (a right-click opens it too)
            yield return null;
            bool rc = fan.IsOpen;
            p0 = pushes;
            fan.Chip(m).Click();
            yield return WaitFor(() => !InspectorCard.PastePending && CubeInspector.State == CubeInspector.Phase.Closed, 4f);
            yield return Wait(0.8f);
            Line(rc && InspectorCard.LastPasteMode == m && InspectorCard.LastPasteNext != null && pushes - p0 == 1, "a right-click opens the fan too; " + PasteFan.Captions[Array.IndexOf(PasteFan.Modes, m)] + " pastes on the next grid in that mode (one History entry)",
                 "mode " + InspectorCard.LastPasteMode + ", made " + (InspectorCard.LastPasteNext != null) + ", pushes +" + (pushes - p0));
        }
        // a plain click stays the v6 paste
        yield return OpenCard(c);
        p0 = pushes;
        InspectorCard.PasteNextButton.Click();
        yield return WaitFor(() => !InspectorCard.PastePending && CubeInspector.State == CubeInspector.Phase.Closed, 4f);
        yield return Wait(0.8f);
        Line(InspectorCard.LastPasteMode == Clipboard.PasteMode.Plain && InspectorCard.LastPasteNext != null && InspectorCard.LastPasteNext.layer == c.layer && pushes - p0 == 1, "a plain click on paste → next grid stays the v6 paste (no fan needed)");
    }

    // ================================================================== 5. the clipboard chip's fan
    static IEnumerator ClipFan()
    {
        var pm = PathManager.I; var sm = SongManager.I; var chip = UIManager.I.ClipboardChip;
        var src = PitchedCube(x => x.nodes.Count >= 3 && InspectorCard.NextGridOf(x) != null);
        if (chip == null || src == null) { Line(false, "clipboard fan", "no chip / cube"); yield break; }
        Frame(src.Island.Center, 18f); yield return null;
        Clipboard.Copy(src);
        yield return Wait(0.6f);
        chip.Card.onRightClick();
        yield return Wait(0.3f);
        var fan = chip.Fan;
        bool open = chip.FanOpen;
        var fr = open ? ScreenRect(fan.Root) : new Rect(); var cr = ScreenRect(chip.Root);
        string det = "not open";
        var chips = new List<InkButton>(); foreach (var m in PasteFan.Modes) chips.Add(fan.Chip(m)); chips.Add(fan.LateButton);
        bool reach = open && Reachable(chips, out det);
        Line(open && fr.yMin > cr.yMin && fr.xMax <= Screen.width + 1f && reach, "a right-click on the clipboard chip opens the same fan above it (on screen, reachable)", det);
        yield return Shot("u2_7_clipboard_fan.png");
        // a pick = the pattern in the hand in that mode
        fan.Chip(Clipboard.PasteMode.EchoDown).Click();
        var ot = FreeTile(Clipboard.NextGrid(src.Island), 1);
        if (ot != null) PathManager.SimPos = ScreenOf(ot.Top);
        yield return Wait(0.35f);
        bool held = pm.Hand == PathManager.HandKind.Pattern && pm.PatternMode == Clipboard.PasteMode.EchoDown && !chip.FanOpen && CursorKit.RiderKind == 2 && CursorKit.RiderMode == (int)Clipboard.PasteMode.EchoDown;
        yield return Shot("u2_7_cursor_mode.png");
        Line(held, "a fan pick puts the pattern in the hand in that mode (echo ↓): the card riding the cursor wears the mode on its corner",
             "hand " + pm.Hand + " / " + pm.PatternMode + ", rider " + CursorKit.RiderKind + " mode " + CursorKit.RiderMode);
        chip.Card.onHold();
        yield return Wait(0.2f);
        bool holdOpen = chip.FanOpen;
        fan.Chip(Clipboard.PasteMode.EchoDown).Click();
        yield return null;
        Line(holdOpen && pm.Hand == PathManager.HandKind.Empty, "a press-and-hold opens the fan too; the same mode again puts the pattern down");
        // a short click stays the plain paste mode
        chip.SimClick(); yield return null;
        bool plain = pm.Hand == PathManager.HandKind.Pattern && pm.PatternMode == Clipboard.PasteMode.Plain;
        chip.SimClick(); yield return null;
        Line(plain && pm.Hand == PathManager.HandKind.Empty, "a short click on the chip is still the plain paste mode (as v6)");
        // a fan chip dragged onto a grid pastes there in that mode
        var target = Clipboard.NextGrid(src.Island);
        Frame(target.Center, 18f); yield return Wait(0.2f);
        int on0 = CubesOn(target), p0 = pushes, d0 = HudClipboard.ModeDropCount;
        var made = chip.SimModeDrop(target, Clipboard.PasteMode.EchoUp);
        yield return Wait(0.8f);
        Line(made != null && HudClipboard.ModeDropCount == d0 + 1 && HudClipboard.LastDropMode == Clipboard.PasteMode.EchoUp && made.layer == src.layer + 1 && CubesOn(target) == on0 + 1 && pushes - p0 == 1 && pm.Hand == PathManager.HandKind.Empty,
             "dragging echo ↑ from the fan onto a grid pastes there in the layer above (one History entry); the hand goes back to empty",
             made != null ? "layer " + made.layer + ", cubes there " + on0 + " → " + CubesOn(target) + ", pushes +" + (pushes - p0) : "refused");
        chip.Card.onRightClick(); yield return null;
        KeyShim.Sim(KeyCode.Escape, true, true, false); UIManager.I.RunHotkeysForTest(); KeyShim.Clear();
        yield return null;
        Line(!chip.FanOpen, "Esc folds the chip's fan first");
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
    }

    // ================================================================== 6. the selection bar
    static IEnumerator Selection()
    {
        var sm = SongManager.I; var bar = UIManager.I.SelectionBar;
        if (bar == null) { Line(false, "selection bar", "not built"); yield break; }
        Line(!bar.Shown, "no selection: no bar");
        var picks = new List<KeyBlock>();
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && (kb.column == 1 || kb.column == 2)) picks.Add(kb);
        if (picks.Count < 2) { Line(false, "selection", "the fixture has no columns 1–2"); yield break; }
        var b = new Bounds(picks[0].Center, Vector3.zero); foreach (var kb in picks) b.Encapsulate(kb.VisualBounds);
        Frame(b.center, 22f); yield return Wait(0.3f);
        GridSelection.Set(picks);
        yield return Wait(0.45f);
        var br = ScreenRect(bar.Root); var sr = bar.SelectionScreenRect;
        bool over = bar.PlacedBelow ? br.yMax < sr.yMin + 4f : br.yMin > sr.yMax - 4f;
        bool centred = br.xMax > sr.xMin && br.xMin < sr.xMax;
        var bs = new List<InkButton>(); foreach (HudSelection.Act a in Enum.GetValues(typeof(HudSelection.Act))) bs.Add(bar.Button(a));
        string det;
        bool reach = Reachable(bs, out det);
        Line(bar.Shown && over && centred && reach, "a selection: the bar floats over the selected grids (" + (bar.PlacedBelow ? "below" : "above") + " them, overlapping them in x); nine icon buttons, reachable", det + ", bar " + br + ", selection " + sr);
        yield return Shot("u2_7_selection_bar.png");
        // copy (no song change), then paste after the selection
        int p0 = pushes, a0 = HudSelection.ActionCount, cols0 = sm.ColumnCount, isl0 = sm.Islands.Count;
        bar.Button(HudSelection.Act.Copy).Click(); yield return null;
        bool copied = HudSelection.LastAction == HudSelection.Act.Copy && SongOps.HasCopiedGrids && bar.Button(HudSelection.Act.Paste).Interactable;
        Line(copied, "copy: the grids go to the grid clipboard (paste is on)", "copied " + SongOps.HasCopiedGrids + ", pushes +" + (pushes - p0));
        yield return Wait(0.2f);
        p0 = pushes;
        bar.Button(HudSelection.Act.Paste).Click(); yield return Wait(0.3f);
        Line(sm.Islands.Count == isl0 + picks.Count && pushes - p0 == 1, "paste: the copied grids land as new columns after the selection (one History entry)", "islands " + isl0 + " → " + sm.Islands.Count + ", columns " + cols0 + " → " + sm.ColumnCount + ", pushes +" + (pushes - p0));
        // octave ↑ / ↓ on the selection (the selection follows its rebuilt grids)
        GridSelection.Set(Reselect(1, 2));
        yield return null;
        int r0 = RegisterSum(); p0 = pushes;
        bar.Button(HudSelection.Act.OctaveUp).Click(); yield return Wait(0.2f);
        int r1 = RegisterSum();
        bar.Button(HudSelection.Act.OctaveDown).Click(); yield return Wait(0.2f);
        Line(r1 == r0 + GridSelection.Selected.Count && RegisterSum() == r0 && pushes - p0 == 2 && GridSelection.Any, "octave ↑ / ↓: every selected grid's register ± 1 (one History entry each); the selection follows its grids",
             "registers " + r0 + " → " + r1 + " → " + RegisterSum() + ", pushes +" + (pushes - p0));
        // repeat cycles, the build-up (launch), extend (§21: the long grid through the selection)
        p0 = pushes;
        bar.Button(HudSelection.Act.Repeat).Click(); yield return Wait(0.2f);
        int rep = HudSelection.RepeatOf();
        bar.Button(HudSelection.Act.Launch).Click(); yield return Wait(0.2f);
        bool launch = HudSelection.LaunchOf();
        bar.Button(HudSelection.Act.Extend).Click(); yield return Wait(0.2f);
        bool extend = HudSelection.ExtendOf();
        string capE = CaptionOf(bar.Button(HudSelection.Act.Extend)), capL = CaptionOf(bar.Button(HudSelection.Act.Launch));
        Line(rep == 2 && launch && extend && pushes - p0 == 3 && bar.Button(HudSelection.Act.Launch).Active && bar.Button(HudSelection.Act.Extend).Active && capE == "extend through" && capL == "build-up",
             "repeat (1 → 2), the build-up and extend through the selection (§21: \"extend through\" / \"build-up\" captions) — one History entry each; the buttons show their state",
             "repeat " + rep + ", build-up " + launch + ", extend " + extend + ", pushes +" + (pushes - p0) + ", captions \"" + capE + "\" / \"" + capL + "\"");
        yield return Shot("u2_7_selection_bar_on.png");
        // the keys act on the selection
        a0 = HudSelection.ActionCount;
        Key(KeyCode.C, true); var k1 = HudSelection.LastAction;
        Key(KeyCode.UpArrow, false, true); var k2 = HudSelection.LastAction;
        Key(KeyCode.DownArrow, false, true); var k3 = HudSelection.LastAction;
        yield return Wait(0.2f);
        Line(k1 == HudSelection.Act.Copy && k2 == HudSelection.Act.OctaveUp && k3 == HudSelection.Act.OctaveDown && HudSelection.ActionCount == a0 + 3,
             "with grids selected ⌘C copies them and ⇧↑ / ⇧↓ move them an octave", k1 + ", " + k2 + ", " + k3);
        // duplicate, then delete through the key: the selection empties, the bar leaves
        isl0 = sm.Islands.Count; p0 = pushes;
        int selN = GridSelection.Selected.Count;
        bar.Button(HudSelection.Act.Duplicate).Click(); yield return Wait(0.3f);
        bool dup = sm.Islands.Count == isl0 + selN && pushes - p0 == 1;
        isl0 = sm.Islands.Count; selN = GridSelection.Selected.Count; p0 = pushes;
        Key(KeyCode.Delete);
        yield return Wait(0.5f);
        Line(dup && sm.Islands.Count == isl0 - selN && pushes - p0 == 1 && !GridSelection.Any && !bar.Shown, "duplicate (one entry), then Delete removes the selected grids (one entry): the selection empties and the bar leaves",
             "dup " + dup + ", islands " + isl0 + " → " + sm.Islands.Count + ", pushes +" + (pushes - p0) + ", shown " + bar.Shown);
        // Esc with a selection: nothing else takes the press (the menu strip stays shut)
        GridSelection.Set(Reselect(0, 0));
        yield return Wait(0.3f);
        bool stripOpen0 = UIManager.I.Strip != null && UIManager.I.Strip.IsOpen;
        KeyShim.Sim(KeyCode.Escape, true, true, false); yield return null; KeyShim.Clear(); yield return null; yield return null;
        Line(!GridSelection.Any && UIManager.I.Strip != null && UIManager.I.Strip.IsOpen == stripOpen0, "Esc clears the selection and does nothing else (the strip stays shut)", "selected " + GridSelection.Selected.Count);
    }

    static List<KeyBlock> Reselect(int c0, int c1)
    {
        var l = new List<KeyBlock>();
        foreach (var kb in SongManager.I.Islands) if (kb != null && !kb.IsMoon && kb.column >= c0 && kb.column <= c1) l.Add(kb);
        return l;
    }
    static int RegisterSum() { int s = 0; foreach (var kb in GridSelection.Selected) if (kb != null) s += kb.register; return s; }

    // ================================================================== 7. hotkeys
    static IEnumerator Hotkeys()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var c = PitchedCube(x => x.nodes.Count >= 3 && InspectorCard.NextGridOf(x) != null);
        if (c == null) { Line(false, "hotkeys", "no cube"); yield break; }
        yield return OpenCard(c);
        int n0 = CubeCount(), p0 = pushes, oct0 = c.octave, k0 = UIManager.IdeaKeyCount;
        Key(KeyCode.UpArrow, false, true); yield return Wait(0.15f);
        var up = LastBorn(n0);
        Key(KeyCode.DownArrow, false, true); yield return Wait(0.15f);
        var dn = LastBorn(n0 + 1);
        Line(up != null && up.layer == c.layer + 1 && dn != null && dn.layer == c.layer - 1 && c.octave == oct0 && pushes - p0 == 2,
             "⇧↑ / ⇧↓ make octave copies of the inspected cube (the cube's own octave stays)", "layers " + (up != null ? up.layer : -9) + " / " + (dn != null ? dn.layer : -9) + ", octave " + oct0 + " → " + c.octave + ", pushes +" + (pushes - p0));
        n0 = CubeCount(); p0 = pushes;
        Key(KeyCode.UpArrow, false, false, true); yield return Wait(0.15f);
        var hm = LastBorn(n0);
        Line(hm != null && hm.echoOf == c.id && pushes - p0 == 1, "⌥↑ makes a harmony above it", hm != null ? "cube " + hm.id : "none");
        var x0 = c.nodes[0].gridX; var z0 = c.nodes[0].gridZ; p0 = pushes;
        Key(KeyCode.U); yield return Wait(0.1f);
        bool flipped = pushes - p0 == 1;
        Key(KeyCode.U); yield return Wait(0.1f);
        Line(flipped && c.nodes[0].gridX == x0 && c.nodes[0].gridZ == z0 && pushes - p0 == 2, "U flips it (twice = back)");
        p0 = pushes;
        Key(KeyCode.UpArrow); yield return null;
        bool octKept = c.octave == oct0 + 1 && pushes - p0 == 1;
        Key(KeyCode.DownArrow); yield return null;
        Line(octKept && c.octave == oct0, "plain ↑ / ↓ still set the inspected cube's own octave (v3 binding kept)", "octave " + c.octave);
        yield return CloseCard();
        // the hovered cube (no card): ⇧↑ — a cube of another grid (the copies above made float over c and would take the pick)
        var c2 = PitchedCube(x => x != c && x.Island != c.Island && x.layer == 0 && x.echoOf < 0) ?? c;
        Frame(c2.Island.Center, 14f); yield return Wait(0.2f);
        PathManager.SimPos = ScreenOf(c2.transform.position);
        yield return null; yield return null;
        n0 = CubeCount(); p0 = pushes;
        var hc = pm.Candidate;
        Key(KeyCode.UpArrow, false, true); yield return Wait(0.15f);
        var hu = LastBorn(n0);
        Line(hc != null && hu != null && hu.echoOf == hc.id && hu.layer == hc.layer + 1 && pushes - p0 == 1, "⇧↑ on a hovered cube (no card open) makes its octave copy too",
             "hovered " + (hc != null ? "cube " + hc.id : "none") + ", made " + (hu != null ? "layer " + hu.layer + " echoOf " + hu.echoOf : "none"));
        // paste modes: ⌘↑ echo ↑ on the hovered grid, ⌘⇧V answer, ⌘⌥↓ step ↓
        Clipboard.Copy(c);
        var tgt = Clipboard.NextGrid(c.Island);
        var tt = FreeTile(tgt, 1);
        Frame(tgt.Center, 16f); yield return Wait(0.2f);
        if (tt != null) PathManager.SimPos = ScreenOf(tt.Top);
        yield return null; yield return null;
        int on0 = CubesOn(tgt); p0 = pushes;
        Key(KeyCode.UpArrow, true); yield return Wait(0.8f);
        var e1 = UIManager.LastPasteKeyMode; var eu = LastBorn(CubeCount() - 1);
        Key(KeyCode.V, true, true); yield return Wait(0.8f);
        var e2 = UIManager.LastPasteKeyMode;
        Key(KeyCode.DownArrow, true, false, true); yield return Wait(0.8f);
        var e3 = UIManager.LastPasteKeyMode;
        Line(e1 == Clipboard.PasteMode.EchoUp && eu != null && eu.layer == c.layer + 1 && e2 == Clipboard.PasteMode.Answer && e3 == Clipboard.PasteMode.StepDown && CubesOn(tgt) == on0 + 3 && pushes - p0 == 3,
             "⌘↑ pastes an echo ↑ on the hovered grid, ⌘⇧V an answer, ⌘⌥↓ a step ↓ (one History entry each)",
             e1 + " (layer " + (eu != null ? eu.layer : -9) + "), " + e2 + ", " + e3 + ", cubes there " + on0 + " → " + CubesOn(tgt) + ", pushes +" + (pushes - p0));
        Clipboard.Clear();
        PathManager.SimPos = new Vector3(-50f, -50f, 0f); yield return null;
        // ⇧→: launch on the focused (selected) island
        int at = -1; for (int i = 0; i < sm.Islands.Count && at < 0; i++) if (sm.Islands[i] != null && !sm.Islands[i].IsMoon && sm.Islands[i].column == 1) at = i;
        UIManager.I.SelectMeasure(at, false);
        yield return null;
        bool l0 = sm.Islands[at].launch; p0 = pushes;
        Key(KeyCode.RightArrow, false, true); yield return Wait(0.2f);
        bool l1 = sm.Islands[at].launch;
        Key(KeyCode.RightArrow, false, true); yield return Wait(0.2f);
        Line(!l0 && l1 && !sm.Islands[at].launch && pushes - p0 == 2, "⇧→ toggles the build-up on the focused island (§21.4: a riser into the next section, no fling), again = off; one History entry each", "launch " + l0 + " → " + l1 + " → " + sm.Islands[at].launch);
        UIManager.I.ClearSelection();
    }

    /// <summary>The newest finalized cube when the song has more than <paramref name="before"/> of them (else null).</summary>
    static AudioCube LastBorn(int before)
    {
        if (CubeCount() <= before) return null;
        AudioCube best = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && (best == null || c.id > best.id)) best = c;
        return best;
    }

    // ================================================================== 8. the shortcut sheet
    static IEnumerator Sheet()
    {
        var sm = SongManager.I;
        int o0 = HudShortcuts.OpenCount;
        Key(KeyCode.Slash, false, true);
        yield return Wait(0.35f);
        var card = HudShortcuts.Card;
        Rect r = card != null ? ScreenRect(card) : new Rect();
        bool on = HudShortcuts.IsOpen && HudShortcuts.OpenCount == o0 + 1 && WorldInput.KeysLocked && r.xMin >= 0f && r.xMax <= Screen.width && r.yMin >= 0f && r.yMax <= Screen.height;
        int rows = HudShortcuts.RowCount;
        Line(on && rows >= 45, "? opens the shortcut sheet: a cream card on screen listing every shortcut (key caps + a picture + a word), the keys locked meanwhile",
             rows + " rows, card " + r);
        int riderRows = 0, buildUp = 0;
        foreach (var col in HudShortcuts.Columns) foreach (var sec in col) foreach (var e in sec.rows) { if (e.words == "rider" || e.glyph == "rider") riderRows++; if (e.words == "build-up" && string.Join(" ", e.keys) == "shift + right") buildUp++; }
        Line(riderRows == 0 && buildUp == 1, "§21: the sheet lists no rider; ⇧→ reads \"build-up\"", "rider rows " + riderRows + ", build-up rows " + buildUp);
        yield return Shot("u2_7_shortcuts.png");
        int m0 = sm.Moons.Count;
        Key(KeyCode.N); yield return null;
        Line(sm.Moons.Count == m0, "while it is up the game's keys sleep (N adds no Moon)");
        KeyShim.Sim(KeyCode.Escape, true, true, false); HudShortcuts.I.StepForTest(); KeyShim.Clear();
        yield return null;
        bool escClosed = !HudShortcuts.IsOpen && !WorldInput.KeysLocked;
        Key(KeyCode.Slash, false, true); yield return null; yield return null;
        bool again = HudShortcuts.IsOpen;
        KeyShim.Sim(KeyCode.LeftShift, false, true, false); KeyShim.Sim(KeyCode.Slash, true, true, false); HudShortcuts.I.StepForTest(); KeyShim.Clear();
        yield return null;
        Line(escClosed && again && !HudShortcuts.IsOpen, "Esc closes it; ? opens it again and ? closes it");
        UIManager.I.Strip.Open(); yield return Wait(0.3f);
        bool rowFound = Hints.Has("shortcuts");
        var hr = Hints.RectOf("shortcuts");
        var hb = hr != null ? hr.GetComponent<HudButton>() : null;
        if (hb != null) hb.onClick();
        yield return Wait(0.3f);
        Line(rowFound && hb != null && HudShortcuts.IsOpen && !UIManager.I.Strip.IsOpen, "the menu strip's \"shortcuts\" row opens it too");
        HudShortcuts.Close(); yield return null;
    }

    // ================================================================== 9. per-frame cost
    static IEnumerator Cost()
    {
        var pm = PathManager.I; var sm = SongManager.I; var row = UIManager.I.SizeRow; var bar = UIManager.I.SelectionBar;
        var kb = sm.Islands[0];
        Frame(kb.Center, 20f); yield return Wait(0.2f);
        var t = FreeTile(kb, 1);
        HudInstruments.ToggleHand(6, -1);
        if (t != null) PathManager.SimPos = ScreenOf(t.Top);
        GridSelection.Set(Reselect(1, 1));
        yield return Wait(0.6f);
        for (int i = 0; i < 3; i++) { row.StepForTest(); bar.StepForTest(); CursorKit.TickWorld(false); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) { row.StepForTest(); bar.StepForTest(); CursorKit.TickWorld(false); }
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        Line(a1 - a0 == 0, "no per-frame GC: 120 frames of the size row, the selection bar (re-projected over its grids) and the brush-sized rider", (a1 - a0) + " bytes");
        double ms = sw.Elapsed.TotalMilliseconds / 120.0;
        Line(ms < 0.3, "their per-frame work is small (< 0.3 ms)", ms.ToString("F4") + " ms / frame (editor)");
        pm.PutDown(); GridSelection.Clear();
    }
}
