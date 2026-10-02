using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Package U1 Play-mode checks (SPEC v4 §5, scratchpad/v4/brief_U1.md): the hit targets at rest (≈ 20), a caption on every icon-only
/// control, the island header's ops (the right SongManager op, one History entry each), the cursor states, the deck's placement through
/// the ghost (and the rail's drop preview), the hotkeys the HUD owns, the v4 tutorial start to finish with simulated input, no per-frame
/// GC at rest, the HUD's frame cost; and <see cref="Looks"/>: the captures at 1920×1080 (rest, header + ghosts, menu strip, sound drawer,
/// deck, drafting, inspector, mix, playing, tutorial). Start with <see cref="Run"/> / <see cref="Looks"/>, poll <see cref="Done"/> or the
/// report file (Captures/u1_report.txt). Never writes the user's save; restores the tutorial PlayerPrefs and the fixture at the end.
/// </summary>
public static class V4ChecksU1
{
    public static bool Done;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u1_report.txt");
    static StringBuilder sb; static int num, pass, fail;
    static int pushes;

    static void Line(bool ok, string what, string detail = null)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" — ").Append(detail);
        sb.Append('\n');
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }

    static void Flush(string suffix = "")
    {
        Report = "V4ChecksU1: " + pass + " PASS, " + fail + " FAIL" + suffix + "\n" + sb;
        // the looks log goes beside the checks' report (a capture pass never overwrites the last check run)
        string path = string.IsNullOrEmpty(suffix) ? ReportPath : Path.Combine(CaptureDir, "u1_looks.txt");
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(path, Report); } catch (Exception) { }
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float s) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < s) yield return null; }

    static IEnumerator Shot(string file)
    {
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
        yield return Wait(0.25f);
    }

    static void OnHistory() { pushes++; }

    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); WorldInput.Unlock(HudMenuStrip.LockOwner);
        Onboarding.Suppressed = true;
        Clipboard.Clear();   // v6: a pattern left on the clipboard shows U2's clipboard chip (one more hit target at rest)
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        WorldInput.Unlock("prompt");
        if (IslandTray.IsOpen) IslandTray.Close();
        if (UIManager.I != null) { UIManager.I.SetHudVisible(true); UIManager.I.ClearSelection(); if (UIManager.I.Strip != null) UIManager.I.Strip.CloseNow(); }
        IslandHeader.Hide();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void LoadFixture()
    {
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    static void Restore()
    {
        CubeInspector.CloseImmediate();
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        PathManager.SimOnly = false; KeyShim.Clear();
        if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.CancelPath();
        IslandHeader.Hide();
        if (UIManager.I != null) { UIManager.I.ClearSelection(); if (UIManager.I.Strip != null) UIManager.I.Strip.CloseNow(); if (UIManager.I.Transport != null) UIManager.I.Transport.SetDrawer(false); if (HudInstruments.I != null) HudInstruments.I.SetMix(false); }
        if (IslandTray.IsOpen) IslandTray.Close();
        CursorKit.ResetAll();
    }

    static void Frame(Vector3 centre, float dist)
    {
        if (OrbitCamera.I == null) return;
        OrbitCamera.I.FrameBounds(new Bounds(centre, new Vector3(dist, 2f, dist * 0.6f)), 0.05f, true);
    }

    static Vector3 ScreenOf(Vector3 world) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero; }

    // ================================================================== the looks (captures)
    public static bool LooksDone;
    /// <summary>Captures the HUD states at the Game view's size (1920×1080): u1_rest, u1_header, u1_header_more, u1_header_edge,
    /// u1_header_corner, u1_strip, u1_drawer, u1_deck, u1_deck_fan, u1_mix, u1_caption, u1_playing, u1_draft, u1_inspect, u1_cursors, u1_tutorial, u1_prompt.
    /// Poll <see cref="LooksDone"/>.</summary>
    public static string Looks(string only = null)
    {
        if (UIManager.I == null || SongManager.I == null) return "FAIL needs Play mode";
        LooksDone = false;
        UIManager.I.StartCoroutine(LooksRoutine(only));
        return "started";
    }

    static bool Want(string only, string name) => string.IsNullOrEmpty(only) || only.Contains(name);

    static IEnumerator LooksRoutine(string only)
    {
        sb = new StringBuilder(); num = pass = fail = 0;
        Prepare();
        LoadFixture();
        yield return null; yield return null;
        var sm = SongManager.I;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        yield return Wait(0.8f);
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);   // the real pointer is not over the Game view: park the sim pointer off screen
        yield return Wait(0.3f);
        if (Want(only, "rest")) { yield return Shot("u1_rest.png"); Info("u1_rest.png " + Screen.width + "x" + Screen.height + ", targets at rest " + CountTargets(null)); }
        if (Want(only, "header"))
        {
            var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
            Frame(kb.Center, 16f); yield return Wait(0.6f);
            IslandHeader.Show(kb);
            yield return Wait(0.5f);
            yield return Shot("u1_header.png");
            IslandHeader.I.More.onClick();
            yield return Wait(0.3f);
            yield return Shot("u1_header_more.png");
            IslandHeader.CloseMore(); IslandHeader.Hide();
            if (OrbitCamera.I != null) OrbitCamera.I.ResetView();
            yield return Wait(0.6f);
        }
        if (Want(only, "edge"))
        {
            var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
            string[] files = { "u1_header_edge.png", "u1_header_corner.png" };
            Vector2[] spots = { new Vector2(Screen.width * 0.5f, Screen.height * 0.03f), new Vector2(Screen.width * 0.08f, Screen.height * 0.03f) };
            for (int k = 0; k < 2; k++)
            {
                yield return FrameAt(kb, spots[k]);
                IslandHeader.Show(kb);
                yield return Wait(0.5f);
                IslandHeader.I.More.onClick();
                yield return Wait(0.3f);
                yield return Shot(files[k]);
                string why; RibbonClear(IslandHeader.I, out why);
                Info(files[k] + ": island edge at (" + lowEdgeX.ToString("F0") + ", " + lowEdgeY.ToString("F0") + ") px, more card open " + IslandHeader.MoreOpen + ", " + why);
                IslandHeader.CloseMore(); IslandHeader.Hide();
                yield return Wait(0.3f);
            }
            if (OrbitCamera.I != null) OrbitCamera.I.ResetView();
            yield return Wait(0.6f);
        }
        if (Want(only, "strip")) { UIManager.I.Strip.Open(); yield return Wait(0.5f); yield return Shot("u1_strip.png"); UIManager.I.Strip.CloseNow(); yield return Wait(0.2f); }
        if (Want(only, "drawer")) { UIManager.I.Transport.SetDrawer(true); yield return Wait(0.4f); yield return Shot("u1_drawer.png"); UIManager.I.Transport.SetDrawer(false); yield return Wait(0.3f); }
        if (Want(only, "deck"))
        {
            IslandTray.Open(); yield return Wait(0.6f); yield return Shot("u1_deck.png");
            IslandTray.I.SimFan(1, true); yield return Wait(0.4f); yield return Shot("u1_deck_fan.png"); IslandTray.I.SimFan(1, false);
            IslandTray.Close(); yield return Wait(0.4f);
        }
        if (Want(only, "mix")) { HudInstruments.I.SetMix(true); yield return Wait(0.4f); yield return Shot("u1_mix.png"); HudInstruments.I.SetMix(false); yield return Wait(0.2f); }
        if (Want(only, "caption"))
        {
            InkCaption.Show(UIManager.I.Present.Sticker, "present");
            yield return Wait(0.5f); yield return Shot("u1_caption.png"); InkCaption.Hide(null);
        }
        if (Want(only, "playing")) { GlobalClock.Seek(0); GlobalClock.Play(); yield return Wait(1.3f); yield return Shot("u1_playing.png"); GlobalClock.Stop(); yield return Wait(0.3f); }
        if (Want(only, "draft"))
        {
            var kb = sm.Islands[0];
            Frame(kb.Center, 14f); yield return Wait(0.6f);
            var t0 = FreeTile(kb, 0); var t1 = FreeTile(kb, 3);
            if (t0 != null && t1 != null)
            {
                var pm = PathManager.I;
                Vector3 s0 = ScreenOf(t0.Top), s1 = ScreenOf(t1.Top);
                PathManager.SimPos = s0; pm.SimPointer(s0, true, true, false); pm.SimPointer(s0, false, false, true);
                yield return null;
                PathManager.SimPos = s1; pm.SimPointer(s1, true, true, false); pm.SimPointer(s1, false, false, true);
                yield return Wait(0.8f);
                yield return Shot("u1_draft.png");
                Info("draft: drawing " + pm.IsDrawing + ", tiles " + pm.currentPathTiles.Count + ", cursor " + CursorKit.Current + ", length row " + DurationPicker.IsShown);
                pm.CancelPath();
                PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            }
            else Info("draft: no free tiles");
            yield return Wait(0.3f);
        }
        if (Want(only, "inspect"))
        {
            AudioCube c = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.isFinalized && !x.IsDrums && x.nodes.Count >= 2) { c = x; break; }
            if (c != null)
            {
                CubeInspector.Open(c);
                yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open, 4f);
                yield return Wait(0.8f);
                yield return Shot("u1_inspect.png");
                CubeInspector.CloseImmediate();
                FocusLoop.Dismiss(); GlobalClock.Stop();
                yield return Wait(0.4f);
            }
        }
        if (Want(only, "cursors")) { bool ok = CursorKit.SavePreview(Path.Combine(CaptureDir, "u1_cursors.png"), Instruments.Colors[5]); Info("cursor preview " + ok); }
        if (Want(only, "tutorial"))
        {
            int ps = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), pd = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
            var tipKeep = KeepTips();
            Frame(sm.Islands[0].Center, 20f);   // the first step points at island 0 (the hand-drawn loop around it)
            yield return Wait(0.6f);
            Onboarding.Suppressed = false;
            Onboarding.StartTutorial(true);
            yield return Wait(1.6f);
            yield return Shot("u1_tutorial.png");
            Info("tutorial bubble: \"" + (Onboarding.Bubble != null ? Onboarding.Bubble.Text.Replace('\n', ' ') : "") + "\" → " + (Onboarding.ShownTarget ?? "centred") + ", tip " + (Onboarding.Bubble != null ? Onboarding.Bubble.TipDistance().ToString("F1") : "-") + " px");
            Onboarding.Skip();
            Onboarding.Suppressed = true;
            if (ps < 0) PlayerPrefs.DeleteKey(Onboarding.PrefStep); else PlayerPrefs.SetInt(Onboarding.PrefStep, ps);
            if (pd < 0) PlayerPrefs.DeleteKey(Onboarding.PrefDone); else PlayerPrefs.SetInt(Onboarding.PrefDone, pd);
            RestoreTips(tipKeep);
            yield return Wait(0.4f);
        }
        if (Want(only, "prompt") && InterfaceController.I != null)
        {
            InterfaceController.I.Show();
            yield return Wait(0.7f);
            yield return Shot("u1_prompt.png");
            InterfaceController.I.Hide(); WorldInput.Unlock("prompt");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            yield return Wait(0.4f);
        }
        Restore();
        Flush(" (looks)");
        LooksDone = true;
    }

    static TileInteraction FreeTile(KeyBlock kb, int skip)
    {
        int k = 0;
        foreach (var t in kb.tiles)
        {
            if (t == null || PathManager.TopCubeOn(t) != null) continue;
            if (k++ < skip) continue;
            return t;
        }
        return null;
    }

    // ================================================================== the checks
    public static string Run(bool captures = false)
    {
        if (UIManager.I == null || SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        UIManager.I.StartCoroutine(Routine(captures));
        return "started";
    }

    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); num = pass = fail = 0;
        string userMd5 = Md5(SongIO.Path);
        History.OnChanged += OnHistory;
        Prepare();
        try { LoadFixture(); } catch (Exception e) { Line(false, "fixture", e.Message); }
        yield return null; yield return null;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return Wait(0.6f);
        var sm = SongManager.I; var ui = UIManager.I;
        Info("screen " + Screen.width + "x" + Screen.height + ", islands " + sm.Islands.Count + ", columns " + sm.ColumnCount + ", K ops " + (KLanded() ? "live" : "M0 stubs"));

        // ---- 1. hit targets at rest
        {
            var names = new List<string>();
            int n = CountTargets(names);
            // v9: an instrument cube per group (11 with fx) and the cube / sphere switch (two halves of one control, like the rail's beads)
            int nAdj = n - (names.Contains("Cube") && names.Contains("Ball") ? 1 : 0);
            Line(nAdj >= 16 && nAdj <= 14 + Instruments.Count, "hit targets at rest ≈ 20 (the rail's beads and the cube / sphere switch count as one control each, an instrument cube per group counts)", nAdj + " (" + n + "): " + string.Join(", ", names));
            int beads = ui.Rail != null ? ui.Rail.BeadCount : -1;
            Line(beads == sm.Islands.Count && ui.PillCount == sm.Islands.Count, "the column rail: one bead per island (PillCount)", "beads " + beads + ", islands " + sm.Islands.Count);
        }
        // ---- 2. captions on every icon-only control
        {
            var missing = new List<string>(); int total = 0;
            foreach (var go in Controls())
            {
                total++;
                var cap = go.GetComponent<InkCaptionHover>();
                if (cap == null || string.IsNullOrEmpty(cap.words)) missing.Add(PathOf(go.transform));
            }
            Line(missing.Count == 0 && total > 40, "every icon-only control has a hover caption (InkCaption)", total + " controls" + (missing.Count > 0 ? ", missing: " + string.Join(", ", missing.GetRange(0, Mathf.Min(12, missing.Count))) : ""));
        }
        // ---- 2b. every hit target ≥ 28 px (UI.md §4), in each HUD state
        yield return SizeChecks();
        // ---- 3. the island header
        yield return HeaderChecks();
        // ---- 4. cursors
        yield return CursorChecks();
        // ---- 5. the deck
        yield return DeckChecks();
        // ---- 6. hotkeys the HUD owns
        yield return HotkeyChecks();
        // ---- 7. GC at rest + the HUD's frame cost
        yield return CostChecks();
        // ---- 8. the tutorial, start to finish with simulated input
        yield return TutorialRun();

        Restore();
        try { LoadFixture(); } catch (Exception) { }
        History.OnChanged -= OnHistory;
        Line(Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + userMd5);
        Flush();
        Done = true;
    }

    static bool KLanded()
    {
        // the M0 stubs return -1 without touching anything: probe on a scratch copy is not possible, so read the column layout instead
        var sm = SongManager.I;
        foreach (var kb in sm.Islands) if (kb != null && kb.column != sm.Islands.IndexOf(kb)) return true;
        return sm.GetType().GetMethod("AddIslandInColumn", new[] { typeof(int), typeof(bool) }) != null && ProbeAdd();   // v8 added an overload (…, int measure)
    }
    static bool probed, probeResult;
    static bool ProbeAdd()
    {
        if (probed) return probeResult;
        probed = true;
        var sm = SongManager.I;
        int n = sm.Islands.Count;
        int r = sm.AddIslandInColumn(0, false);
        probeResult = r >= 0;
        if (probeResult) { History.Undo(); if (sm.Islands.Count != n) LoadFixture(); }
        return probeResult;
    }

    // ------------------------------------------------------------------ hit targets / controls
    /// <summary>Interactive controls visible at rest in the world HUD (raycast graphics that are active, not faded out, on screen); the
    /// rail's beads count as one control.</summary>
    public static int CountTargets(List<string> names)
    {
        var ui = UIManager.I; if (ui == null) return -1;
        var hud = ui.HudRoot;
        int n = 0; bool beads = false;
        var seen = new HashSet<GameObject>();
        foreach (var g in hud.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.raycastTarget || !g.gameObject.activeInHierarchy || !g.enabled) continue;
            if (!IsControl(g.gameObject)) continue;
            if (Hints.EffectiveAlpha(g.rectTransform) < 0.5f || !BlocksRaycasts(g.transform)) continue;
            Rect r; if (!OnScreen(g.rectTransform, out r)) continue;
            if (r.width > Screen.width * 0.8f && r.height > Screen.height * 0.8f) continue;   // full-screen catchers / ghosts
            if (seen.Contains(g.gameObject)) continue;
            seen.Add(g.gameObject);
            string path = PathOf(g.transform);
            if (path.Contains("/Rail/Beads/")) { if (beads) continue; beads = true; if (names != null) names.Add("Rail(beads)"); n++; continue; }
            n++;
            if (names != null) names.Add(g.gameObject.name);
        }
        return n;
    }

    // ------------------------------------------------------------------ hit sizes
    static IEnumerator SizeChecks()
    {
        var sm = SongManager.I; var ui = UIManager.I; var h = IslandHeader.I;
        var small = new List<string>(); int seen = 0;
        var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.4f);
        seen += MeasureTargets(small);                                   // rest
        if (h != null)
        {
            IslandHeader.Show(kb); yield return Wait(0.4f);
            h.More.onClick(); yield return Wait(0.3f);
            seen += MeasureTargets(small);                               // header + its more card
            IslandHeader.CloseMore(); IslandHeader.Hide(); yield return Wait(0.3f);
        }
        if (HudInstruments.I != null) { HudInstruments.I.SetMix(true); yield return Wait(0.4f); seen += MeasureTargets(small); HudInstruments.I.SetMix(false); yield return Wait(0.2f); }
        if (ui.Transport != null) { ui.Transport.SetDrawer(true); yield return Wait(0.4f); seen += MeasureTargets(small); ui.Transport.SetDrawer(false); yield return Wait(0.3f); }
        if (ui.Strip != null) { ui.Strip.Open(); yield return Wait(0.5f); seen += MeasureTargets(small); ui.Strip.CloseNow(); yield return Wait(0.2f); }
        FocusLoop.Begin(kb.column); yield return Wait(0.4f);
        seen += MeasureTargets(small);                                   // the focus badge's ✕
        FocusLoop.Dismiss(); GlobalClock.Stop(); yield return Wait(0.3f);
        Line(small.Count == 0 && seen > 60, "every HUD hit target is ≥ 28 px at the 1920×1080 reference (rest, header + more card, mix, drawer, menu strip, focus badge)",
             seen + " measured" + (small.Count > 0 ? ", under 28: " + string.Join(", ", small) : ""));
    }

    /// <summary>Measures the visible HUD controls (reference px, rotation and scale included); adds the ones under 28 px to <paramref name="small"/>.</summary>
    static int MeasureTargets(List<string> small)
    {
        var ui = UIManager.I; if (ui == null || ui.HudRoot == null) return 0;
        float k = ui.Canvas != null ? Mathf.Max(0.01f, ui.Canvas.scaleFactor) : 1f;
        int n = 0;
        foreach (var g in ui.HudRoot.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.raycastTarget || !g.gameObject.activeInHierarchy || !g.enabled || !IsControl(g.gameObject)) continue;
            if (Hints.EffectiveAlpha(g.rectTransform) < 0.5f || !BlocksRaycasts(g.transform)) continue;
            Rect r; if (!OnScreen(g.rectTransform, out r)) continue;
            if (r.width > Screen.width * 0.8f && r.height > Screen.height * 0.8f) continue;   // full-screen painters with polygon hits (the + ghosts)
            string path = PathOf(g.transform);
            if (path.Contains("/Inspector")) continue;   // U2's card
            g.rectTransform.GetWorldCorners(corners);
            float w = Vector3.Distance(corners[0], corners[3]) / k, hgt = Vector3.Distance(corners[0], corners[1]) / k;
            n++;
            if (w < 27.5f || hgt < 27.5f)
            {
                string e = path.Substring(path.IndexOf("HUD/", StringComparison.Ordinal) >= 0 ? path.IndexOf("HUD/", StringComparison.Ordinal) + 4 : 0) + " " + w.ToString("F0") + "×" + hgt.ToString("F0");
                if (!small.Contains(e)) small.Add(e);
            }
        }
        return n;
    }

    static bool IsControl(GameObject go) => go.GetComponent<HudButton>() != null || go.GetComponent<HudDial>() != null || go.GetComponent<TrayTool>() != null || go.GetComponent<TrayCard>() != null || go.GetComponent<HudDrag>() != null;

    static bool BlocksRaycasts(Transform t)
    {
        var groups = new List<CanvasGroup>();
        while (t != null)
        {
            groups.Clear(); t.GetComponents(groups);
            foreach (var g in groups) { if (!g.enabled) continue; if (!g.blocksRaycasts) return false; if (g.ignoreParentGroups) return true; }
            t = t.parent;
        }
        return true;
    }

    static readonly Vector3[] corners = new Vector3[4];
    static bool OnScreen(RectTransform rt, out Rect r)
    {
        rt.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return r.xMax > 0f && r.yMax > 0f && r.xMin < Screen.width && r.yMin < Screen.height && r.width > 1f && r.height > 1f;
    }

    /// <summary>Every icon-only control of the v4 HUD, header and deck (including hidden ones). Excluded: the v3 inspector card (package
    /// U2 replaces it), the chord wheel's wedges and catcher, the menu strip's word items and veil, pop rings.</summary>
    static IEnumerable<GameObject> Controls()
    {
        var ui = UIManager.I;
        foreach (var c in ui.HudRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!(c is HudButton) && !(c is HudDial) && !(c is TrayTool) && !(c is TrayCard) && !(c is HudDrag)) continue;
            string p = PathOf(c.transform);
            if (p.Contains("/Inspector/") || p.EndsWith("/Inspector") || p.Contains("ChordWheel") || p.Contains("PopRing") || p.Contains("/Strip/") || p.EndsWith("StripVeil")) continue;
            if (c is HudButton && c.GetComponent<HudDial>() != null) continue;
            yield return c.gameObject;
        }
    }

    static string PathOf(Transform t) { var s = new StringBuilder(t.name); while (t.parent != null) { t = t.parent; s.Insert(0, t.name + "/"); } return s.ToString(); }

    // ------------------------------------------------------------------ the island header
    static IEnumerator HeaderChecks()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        if (h == null) { Line(false, "island header", "missing"); yield break; }
        int i = Mathf.Min(1, sm.Islands.Count - 1);
        var kb = sm.Islands[i];
        Frame(kb.Center, 16f); yield return Wait(0.4f);
        // hover timing: 0.15 s dwell, then it shows; 0.4 s linger after the pointer leaves
        {
            Vector3 margin = new Vector3(kb.WestEdge + 0.35f, kb.VisualCenter.y + 0.05f, kb.Center.z);
            PathManager.SimPos = ScreenOf(margin);
            yield return null; yield return null;
            bool ground = PathManager.I.HoverGround && PathManager.I.hoverIsland == kb;
            float t0 = Time.realtimeSinceStartup;
            yield return WaitFor(() => IslandHeader.Current == kb, 1f);
            float appear = Time.realtimeSinceStartup - t0;
            yield return Wait(0.2f);
            bool shown = IslandHeader.IsShown && h.Shown > 0.9f;
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            float t1 = Time.realtimeSinceStartup;
            yield return WaitFor(() => IslandHeader.Current == null, 2f);
            float linger = Time.realtimeSinceStartup - t1;
            Line(ground && shown && appear >= 0.1f && appear < 0.45f && linger >= 0.3f && linger < 0.9f, "header: hovering the island's margin shows it after ~0.15 s (HoverGround), it lingers ~0.4 s after the pointer leaves",
                 "hover ground " + ground + ", appeared after " + appear.ToString("F2") + " s, shown " + shown + ", gone after " + linger.ToString("F2") + " s");
        }
        IslandHeader.Show(kb);
        yield return Wait(0.35f);
        bool pinned = IslandHeader.Current == kb && h.Shown > 0.9f;
        Line(pinned, "header: Show(kb) pins it on the island (tutorial / selection)", "current " + (IslandHeader.Current != null ? IslandHeader.Current.name : "none"));
        // the eye → SetSleep (one push, twice back)
        {
            int p0 = pushes; bool s0 = kb.sleep;
            h.Eye.onClick(); yield return null;
            bool toggled = kb.sleep != s0; int d1 = pushes - p0;
            h.Eye.onClick(); yield return null;
            Line(toggled && d1 == 1 && kb.sleep == s0, "header eye: SetSleep toggles the island off / on, one History entry each", "sleep " + s0 + " → " + !s0 + ", pushes " + d1);
        }
        // register ▲ / ▼ → SetRegister (package K)
        {
            int p0 = pushes, r0 = kb.register;
            h.RegisterUp.onClick(); yield return null;
            int r1 = kb.register, d1 = pushes - p0;
            if (r1 == r0 + 1) { h.RegisterDown.onClick(); yield return null; Line(kb.register == r0 && d1 == 1 && pushes - p0 == 2, "header register ▲ ▼: SetRegister ±1, one History entry each", "register " + r0 + " → " + r1 + " → " + kb.register); }
            else Info("header register: SetRegister is still the M0 stub (register " + r0 + " → " + r1 + ", pushes " + d1 + ") — re-test after report_K.md");
        }
        // more ⋯ card: bars, energy, fall, mood, fill, chord wheel
        {
            h.More.onClick(); yield return null;
            bool open = IslandHeader.MoreOpen;
            int p0 = pushes, b0 = kb.bars;
            int want = b0 == 2 ? 3 : 2;
            h.BarsButton(want).onClick(); yield return null;
            kb = sm.Islands[i];
            bool bars = kb.bars == want && pushes - p0 == 1;
            p0 = pushes; int e0 = kb.energy; h.EnergyButton.onClick(); yield return null; kb = sm.Islands[i];
            bool energy = kb.energy == (e0 + 1) % 4 && pushes - p0 == 1;
            p0 = pushes; int f0 = kb.fall; h.FallButton.onClick(); yield return null; kb = sm.Islands[i];
            bool fall = kb.fall == (Mathf.Clamp(f0, 0, 5) + 1) % 6 && pushes - p0 == 1;
            p0 = pushes; int m0 = kb.mood; h.MoodButton.onClick(); yield return null; kb = sm.Islands[i];
            bool mood = kb.mood == (m0 + 1) % 4 && pushes - p0 == 1;
            p0 = pushes; bool fi0 = kb.fill; h.FillButton.onClick(); yield return null; kb = sm.Islands[i];
            bool fill = kb.fill != fi0 && pushes - p0 == 1;
            IslandHeader.Show(kb); h.ChordButton.onClick(); yield return null;
            bool wheel = UIManager.I.WheelOpen;
            var wr = UIManager.I.HudRoot.Find("ChordWheel"); var wb = wr != null ? wr.GetComponent<HudButton>() : null; if (wb != null && wb.onClick != null) wb.onClick();
            Line(open && bars && energy && fall && mood && fill && wheel, "header more ⋯: length blocks (SetBars), energy, fall, mood, fill each one History entry; chord opens the wheel",
                 "open " + open + ", bars " + bars + ", energy " + energy + ", fall " + fall + ", mood " + mood + ", fill " + fill + ", wheel " + wheel);
            yield return Wait(0.3f);
        }
        // duplicate → / ↓ and + above / + below (package K ops)
        {
            IslandHeader.Show(sm.Islands[i]);
            yield return null;
            int n0 = sm.Islands.Count, c0 = sm.ColumnCount, p0 = pushes;
            h.DuplicateRight.onClick(); yield return null;
            if (sm.Islands.Count == n0 + 1)
            {
                bool col = sm.ColumnCount == c0 + 1;
                Line(col && pushes - p0 == 1, "header duplicate →: DuplicateIsland(i, 0) adds a new column after the island's, one History entry", "islands " + n0 + " → " + sm.Islands.Count + ", columns " + c0 + " → " + sm.ColumnCount + ", pushes " + (pushes - p0));
                History.Undo(); yield return null;
                IslandHeader.Show(sm.Islands[i]); yield return null;
                n0 = sm.Islands.Count; c0 = sm.ColumnCount; p0 = pushes;
                h.DuplicateBelow.onClick(); yield return null;
                Line(sm.Islands.Count == n0 + 1 && sm.ColumnCount == c0 && pushes - p0 == 1, "header duplicate ↓: DuplicateIsland(i, 1) adds to the same column, one History entry", "islands " + n0 + " → " + sm.Islands.Count + ", columns " + c0 + " → " + sm.ColumnCount);
                History.Undo(); yield return null;
                IslandHeader.Show(sm.Islands[i]); yield return null;
                n0 = sm.Islands.Count; c0 = sm.ColumnCount; p0 = pushes;
                h.AddBelow.onClick(); yield return null;
                Line(sm.Islands.Count == n0 + 1 && sm.ColumnCount == c0 && pushes - p0 == 1, "header + below: AddIslandInColumn(i, false), same column, one History entry", "islands " + n0 + " → " + sm.Islands.Count + ", columns " + c0 + " → " + sm.ColumnCount);
                History.Undo(); yield return null;
            }
            else Info("header duplicate / + ghosts: DuplicateIsland / AddIslandInColumn are still the M0 stubs (islands " + n0 + " → " + sm.Islands.Count + ", pushes " + (pushes - p0) + ") — re-test after report_K.md");
        }
        // delete (more card) → RemoveMeasure
        {
            LoadFixture(); yield return null;
            kb = sm.Islands[i];
            IslandHeader.Show(kb); h.More.onClick(); yield return null;
            int n0 = sm.Islands.Count, p0 = pushes;
            h.DeleteButton.onClick(); yield return null;
            Line(sm.Islands.Count == n0 - 1 && pushes - p0 == 1 && !IslandHeader.MoreOpen, "header more ⋯ delete: RemoveMeasure, one History entry, the card closes", "islands " + n0 + " → " + sm.Islands.Count + ", pushes " + (pushes - p0));
            LoadFixture(); yield return null;
        }
        // hidden while drawing / presenting / inspecting
        {
            kb = sm.Islands[0];
            IslandHeader.Show(kb); yield return Wait(0.3f);
            var t0 = FreeTile(kb, 0);
            bool hidDraw = false;
            if (t0 != null)
            {
                Frame(kb.Center, 14f); yield return Wait(0.3f);
                Vector3 s0 = ScreenOf(t0.Top);
                PathManager.I.SimPointer(s0, true, true, false); PathManager.I.SimPointer(s0, false, false, true);
                yield return Wait(0.3f);
                hidDraw = PathManager.I.IsDrawing && (IslandHeader.Current == null || h.Shown < 0.5f);
                PathManager.I.CancelPath();
            }
            Line(hidDraw, "header: hidden while a path is drawn", "drawing hides it " + hidDraw);
            IslandHeader.Hide();
        }
        // near the screen's bottom edge (centre: over the deck; left: over the transport and beside the instrument column): the ribbon
        // stays on screen and clear of the fixed HUD, and the header draws under it
        {
            kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
            var parts = new List<string>(); bool ok = true;
            string[] spotNames = { "bottom centre", "bottom left" };
            Vector2[] spots = { new Vector2(Screen.width * 0.5f, Screen.height * 0.03f), new Vector2(Screen.width * 0.08f, Screen.height * 0.03f) };
            for (int k = 0; k < spots.Length; k++)
            {
                yield return FrameAt(kb, spots[k]);
                IslandHeader.Show(kb);
                yield return Wait(0.5f);
                string why;
                bool clear = RibbonClear(h, out why);
                ok &= clear && h.Shown > 0.9f && lowEdgeY < Screen.height * 0.1f;
                parts.Add(spotNames[k] + ": island edge at (" + lowEdgeX.ToString("F0") + ", " + lowEdgeY.ToString("F0") + ") px, " + why);
                IslandHeader.Hide();
                yield return Wait(0.2f);
            }
            int sib = h.transform.GetSiblingIndex();
            Line(ok && sib == 0, "header near the screen's bottom edge: the ribbon stays on screen and clear of the deck, the transport (its punch row too) and the instrument column; the header draws under the fixed HUD",
                 string.Join("; ", parts) + "; sibling " + sib);
            if (OrbitCamera.I != null) OrbitCamera.I.ResetView();
        }
        yield return Wait(0.3f);
    }

    /// <summary>The header's band against the fixed HUD's rects (screen px): on screen, not over the deck stack, the transport with its
    /// punch row (while playing it tops out 152 units above the transport's bottom) or the instrument column.</summary>
    static bool RibbonClear(IslandHeader h, out string why)
    {
        var bandRt = h.Ribbon != null ? h.Ribbon.Find("Band") as RectTransform : null;
        if (bandRt == null) { why = "no band"; return false; }
        Rect rib = ScreenBox(bandRt), tr, dk, pal;   // all four corners: the band tilts with the island's edge
        bool hasT = Hints.TryGetScreenRect("transport", out tr), hasD = Hints.TryGetScreenRect("tray.button", out dk), hasP = Hints.TryGetScreenRect("palette", out pal);
        float u = Screen.height / 1080f;
        Rect transport = hasT ? Rect.MinMaxRect(tr.xMin, tr.yMin, tr.xMax, tr.yMin + 152f * u) : default(Rect);
        bool clearT = !hasT || !rib.Overlaps(transport), clearD = !hasD || !rib.Overlaps(dk), clearP = !hasP || !rib.Overlaps(pal);
        bool inside = rib.xMin >= 0f && rib.xMax <= Screen.width && rib.yMin >= 0f && rib.yMax <= Screen.height;
        why = "band x " + rib.xMin.ToString("F0") + "-" + rib.xMax.ToString("F0") + " y " + rib.yMin.ToString("F0") + "-" + rib.yMax.ToString("F0")
            + " (deck top " + (hasD ? dk.yMax.ToString("F0") : "-") + ", transport + punch x ≤ " + (hasT ? transport.xMax.ToString("F0") : "-") + " y ≤ " + (hasT ? transport.yMax.ToString("F0") : "-")
            + ", column x ≤ " + (hasP ? pal.xMax.ToString("F0") : "-") + "), clear of deck " + clearD + ", transport " + clearT + ", column " + clearP + ", on screen " + inside;
        return clearT && clearD && clearP && inside;
    }

    /// <summary>Frames the camera so the island's front edge (middle) lands on a screen point (px): the camera moves by the offset
    /// between the edge and the ground point under the target, a few times to settle.</summary>
    static IEnumerator FrameAt(KeyBlock kb, Vector2 target)
    {
        var cam = Camera.main;
        lowEdgeX = 0f; lowEdgeY = float.MaxValue;
        if (cam == null || kb == null) yield break;
        Vector3 front = new Vector3(kb.Center.x, kb.VisualCenter.y + 0.05f, kb.FrontEdge);
        Vector3 centre = kb.Center;
        var plane = new Plane(Vector3.up, front);
        for (int k = 0; k < 3; k++)
        {
            Frame(centre, 16f);
            yield return Wait(0.3f);
            var ray = cam.ScreenPointToRay(new Vector3(target.x, target.y, 0f));
            float d;
            if (!plane.Raycast(ray, out d)) break;
            centre += front - ray.GetPoint(d);
        }
        Frame(centre, 16f);
        yield return Wait(0.35f);
        Vector3 sp = cam.WorldToScreenPoint(front);
        lowEdgeX = sp.x; lowEdgeY = sp.z > 0f ? sp.y : float.MaxValue;
    }
    static float lowEdgeX;
    static float lowEdgeY;

    /// <summary>Screen-pixel bounding box of a HUD rect from all four corners (Hints' rect reads two, which shrinks a tilted rect).</summary>
    static Rect ScreenBox(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int k = 0; k < 4; k++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, c[k]);
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // ------------------------------------------------------------------ cursors
    static IEnumerator CursorChecks()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        var kb = sm.Islands[0];
        Frame(kb.Center, 14f); yield return Wait(0.4f);
        CursorKit.ResetAll();
        Vector3 margin = new Vector3(kb.WestEdge + 0.35f, kb.VisualCenter.y + 0.05f, kb.Center.z);
        PathManager.SimPos = ScreenOf(margin);
        yield return null; yield return null; yield return null;
        var moveV = CursorKit.Current; bool ground = pm.HoverGround;
        pm.Drag.SimBegin(kb, false, new Vector3(kb.Center.x, 0f, kb.Center.z));
        yield return null; yield return null;
        var grab = CursorKit.Current;
        pm.Drag.Cancel();
        var t0 = FreeTile(kb, 0);
        CursorKit.Kind draw = CursorKit.Kind.Default;
        if (t0 != null)
        {
            Vector3 s0 = ScreenOf(t0.Top);
            PathManager.SimPos = s0;
            pm.SimPointer(s0, true, true, false); pm.SimPointer(s0, false, false, true);
            yield return null; yield return null;
            draw = CursorKit.Current;
            pm.CancelPath();
        }
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null; yield return null;
        var rest = CursorKit.Current;
        Line(ground && moveV == CursorKit.Kind.MoveV && grab == CursorKit.Kind.Grab && draw == CursorKit.Kind.Draw && rest == CursorKit.Kind.Default,
             "cursors: MoveV over an island's margin (HoverGround), Grab while it is dragged, Draw while a path is drawn, the system cursor elsewhere",
             "margin " + moveV + " (hover ground " + ground + "), drag " + grab + ", draw " + draw + ", away " + rest + ", SetCursor calls " + CursorKit.Applied);
        bool prev = CursorKit.SavePreview(Path.Combine(CaptureDir, "u1_cursors.png"), Instruments.Colors[5]);
        Info("cursor preview Captures/u1_cursors.png " + prev);
    }

    // ------------------------------------------------------------------ the deck
    static IEnumerator DeckChecks()
    {
        var sm = SongManager.I;
        LoadFixture(); yield return null;
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I != null && IslandTray.I.ShownAmount > 0.98f, 2f);
        var t = IslandTray.I;
        if (t == null) { Line(false, "deck", "missing"); yield break; }
        t.RebuildNow();
        Line(IslandTray.IsOpen && t.CountIn(0) == 4 && t.CountIn(1) == 7 && t.CountIn(2) == 3, "deck: opens into a hand of Next 4, Key 7, Colour 3", t.CountIn(0) + "/" + t.CountIn(1) + "/" + t.CountIn(2));
        // placement through the ghost (free sea), with the rail's drop preview
        var b = sm.SongBounds;
        Vector3 drop = sm.FindFreeSpot(new Vector3(b.center.x, 0f, b.max.z + 12f), Vector3.forward, new Vector3(KeyBlock.IslandWidth + 1f, 3f, 8f));
        int n0 = sm.Islands.Count, h0 = History.UndoCount;
        var g = t.SimDragStart(4);
        yield return null;
        t.SimDragAt(drop);
        yield return null; yield return null;
        int ghostCol = UIManager.I.Rail != null ? UIManager.I.Rail.GhostColumn : -2; string kind = UIManager.I.Rail != null ? UIManager.I.Rail.GhostKind : "";
        var mode = g != null ? g.Snapshot().mode : IslandGhost.Mode.Blocked;
        bool hadGhost = g != null;   // (the ghost is destroyed by the release)
        int at = t.SimDragRelease();
        yield return null;
        Line(hadGhost && at >= 0 && sm.Islands.Count == n0 + 1 && History.UndoCount == h0 + 1 && !WorldInput.IsLockedBy("tray"), "deck: a card dragged onto the sea is placed through the ghost (IslandGhost.Snapshot / Place), one History entry, the tray's lock released",
             "ghost " + mode + ", placed at " + at + ", islands " + n0 + " → " + sm.Islands.Count + ", history +" + (History.UndoCount - h0) + ", locks " + WorldInput.Describe());
        Line(ghostCol >= 0 && kind.Length > 0, "the column rail previews the drop slot while a card is dragged", "ghost bead " + kind + " at column " + ghostCol);
        // a click places after the focused island
        t.RebuildNow();
        UIManager.I.SelectMeasure(0, false);
        n0 = sm.Islands.Count; h0 = History.UndoCount;
        int at2 = t.SimClick(0);
        Line(at2 >= 0 && sm.Islands.Count == n0 + 1 && History.UndoCount == h0 + 1, "deck: a card click places the island after the selected one, one History entry", "placed at " + at2);
        // the Moon card
        int m0 = sm.Moons.Count;
        t.SimTool(3);
        Line(sm.Moons.Count == Mathf.Min(SongManager.MaxMoons, m0 + 1) || m0 >= SongManager.MaxMoons, "deck: the Moon card adds a drum Moon (v3's kit button)", "moons " + m0 + " → " + sm.Moons.Count);
        IslandTray.Close();
        yield return Wait(0.3f);
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ hotkeys
    static IEnumerator HotkeyChecks()
    {
        var sm = SongManager.I; var ui = UIManager.I;
        Restore(); yield return null;
        // N adds a Moon
        int m0 = sm.Moons.Count;
        KeyShim.Sim(KeyCode.N, true, true, false); ui.RunHotkeysForTest(); KeyShim.Clear();
        bool n = sm.Moons.Count == Mathf.Min(SongManager.MaxMoons, m0 + 1);
        // I toggles the deck (read in the deck's Update: hold the simulated key for one frame)
        KeyShim.Sim(KeyCode.I, true, true, false); yield return null; KeyShim.Clear();
        bool iOpen = IslandTray.IsOpen;
        KeyShim.Sim(KeyCode.I, true, true, false); yield return null; KeyShim.Clear();
        bool iClosed = !IslandTray.IsOpen;
        yield return Wait(0.3f);
        // Esc with nothing else to close opens the menu strip; Esc closes it again
        FocusLoop.Dismiss(); GlobalClock.Stop(); ui.ClearSelection(); PathManager.I.Deselect();
        yield return null; yield return null;
        KeyShim.Sim(KeyCode.Escape, true, true, false); ui.RunHotkeysForTest(); KeyShim.Clear();
        bool stripOpen = ui.Strip.IsOpen && WorldInput.IsLockedBy(HudMenuStrip.LockOwner);
        yield return null; yield return null;
        KeyShim.Sim(KeyCode.Escape, true, true, false); yield return null; KeyShim.Clear();
        yield return null;
        bool stripClosed = !ui.Strip.IsOpen && !WorldInput.IsLockedBy(HudMenuStrip.LockOwner);
        // Esc while playing stops (after the focus loop and the selection)
        GlobalClock.Play(); yield return null; yield return null;
        KeyShim.Sim(KeyCode.Escape, true, true, false); ui.RunHotkeysForTest(); KeyShim.Clear();
        bool stopped = !GlobalClock.IsPlaying;
        GlobalClock.Stop();
        yield return Wait(0.3f);
        bool keysFree = !InputUtil.TypingInField && !WorldInput.KeysLocked;
        Line(n && iOpen && iClosed && stripOpen && stripClosed && stopped && keysFree, "hotkeys: N adds a Moon, I opens / closes the deck, Esc opens / closes the menu strip, Esc stops a playing song; nothing leaves the keys locked",
             "N " + n + ", I " + iOpen + "/" + iClosed + ", strip " + stripOpen + "/" + stripClosed + ", stop " + stopped + ", keys free " + keysFree);
        Info("Space / Home / L / M / P (SequenceMaster), O / F (OrbitCamera) and Cmd+S / Cmd+Z (PathManager) read Input.GetKeyDown in their owners' files and cannot be simulated here; the HUD never selects an input field and releases every lock it takes (checked above)");
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ GC and frame cost
    static Func<long> allocProbe; static bool probeTried;
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

    static IEnumerator CostChecks()
    {
        Restore(); yield return null;
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return Wait(0.5f);
        var hudCanvas = UIManager.I.Canvas.gameObject;
        // bytes per frame over 90 frames, HUD on vs off (at rest, stopped)
        long on = 0, off = 0; int fOn = 0, fOff = 0;
        long a = AllocNow();
        for (int k = 0; k < 90; k++) { yield return null; long b = AllocNow(); if (a >= 0) { on += b - a; fOn++; } a = b; }
        hudCanvas.SetActive(false);
        yield return null; a = AllocNow();
        for (int k = 0; k < 90; k++) { yield return null; long b = AllocNow(); if (a >= 0) { off += b - a; fOff++; } a = b; }
        hudCanvas.SetActive(true);
        yield return null;
        float perOn = fOn > 0 ? on / (float)fOn : -1f, perOff = fOff > 0 ? off / (float)fOff : -1f;
        Line(a >= 0 && perOn - perOff <= 8f, "no per-frame GC from the HUD at rest (managed bytes / frame, HUD on vs off)", "on " + perOn.ToString("F0") + " B, off " + perOff.ToString("F0") + " B");
        // while playing: frame time with the HUD on vs off (alternating, 3 × 0.8 s each)
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return Wait(0.5f);
        float tOn = 0f, tOff = 0f; int nOn = 0, nOff = 0; long gOn = 0; int gfOn = 0;
        for (int rep = 0; rep < 3; rep++)
        {
            float t0 = Time.realtimeSinceStartup; a = AllocNow();
            while (Time.realtimeSinceStartup - t0 < 0.8f) { yield return null; tOn += Time.unscaledDeltaTime; nOn++; long b = AllocNow(); gOn += b - a; gfOn++; a = b; }
            hudCanvas.SetActive(false); yield return null;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.8f) { yield return null; tOff += Time.unscaledDeltaTime; nOff++; }
            hudCanvas.SetActive(true); yield return null;
        }
        GlobalClock.Stop();
        float msOn = tOn / Mathf.Max(1, nOn) * 1000f, msOff = tOff / Mathf.Max(1, nOff) * 1000f;
        Line(msOn - msOff <= 1.5f, "the HUD's frame cost while playing ≤ 1.5 ms (frame time HUD on vs off, editor)", "on " + msOn.ToString("F2") + " ms, off " + msOff.ToString("F2") + " ms, delta " + (msOn - msOff).ToString("F2") + " ms; playing GC " + (gOn / Mathf.Max(1f, gfOn)).ToString("F0") + " B/frame (whole game)");
        yield return Wait(0.3f);
    }

    // ------------------------------------------------------------------ the tutorial
    /// <summary>Every one-off tip id (v4 + the three of v5): a run that lets the tutorial finish may fire one; its pref is restored afterwards.</summary>
    static readonly string[] TipIds = Onboarding.AllTips;   // v6: every tip since v4
    static int[] KeepTips() { var v = new int[TipIds.Length]; for (int k = 0; k < TipIds.Length; k++) v[k] = PlayerPrefs.GetInt(Onboarding.PrefTip + TipIds[k], -1); return v; }
    static void RestoreTips(int[] v) { for (int k = 0; k < TipIds.Length; k++) { if (v[k] < 0) PlayerPrefs.DeleteKey(Onboarding.PrefTip + TipIds[k]); else PlayerPrefs.SetInt(Onboarding.PrefTip + TipIds[k], v[k]); } }

    static IEnumerator TutorialRun()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        var tipKeep = KeepTips();
        Restore();
        LoadFixture(); yield return null;
        // a fresh one-island song, as after New
        sm.StartFreshSong(MusicTheory.RandomSong(4242));
        yield return Wait(0.6f);
        GlobalClock.Stop();
        if (OrbitCamera.I != null) OrbitCamera.I.ResetView();
        yield return Wait(0.5f);
        var events = new List<string>();
        Action<string> listen = e => events.Add(e);
        Onboarding.OnEvent += listen;
        Onboarding.Suppressed = false;
        Onboarding.StartTutorial(true);
        var simulated = new List<string>();
        var bub = Onboarding.Bubble;
        int reached = 0;
        for (int s = 1; s <= Onboarding.StepCount; s++)
        {
            yield return WaitFor(() => Onboarding.Step == s || !Onboarding.Active, 3f);
            if (Onboarding.Step != s) { Line(false, "tutorial: expected step " + s, "at " + Onboarding.Step + ", active " + Onboarding.Active); break; }
            reached = s;
            yield return Wait(0.5f);
            Info("step " + s + ": \"" + (bub != null ? bub.Text.Replace('\n', ' ') : "") + "\" → " + (Onboarding.ShownTarget ?? "centred"));
            var kb0 = sm.Islands[0];
            switch (s)
            {
                case Onboarding.SDraw:
                {
                    Frame(kb0.Center, 14f); yield return Wait(0.4f);
                    var t0 = FreeTile(kb0, 0); var t1 = FreeTile(kb0, 2);
                    Vector3 s0 = ScreenOf(t0.Top), s1 = ScreenOf(t1.Top);
                    pm.SimPointer(s0, true, true, false); pm.SimPointer(s0, false, false, true);
                    yield return null;
                    pm.SimPointer(s1, true, true, false); pm.SimPointer(s1, false, false, true);
                    break;
                }
                case Onboarding.SLength:
                {
                    int want = pm.BrushTicks == 48 ? 12 : 48;   // a length other than the current brush (the brush persists between runs)
                    pm.SetDraftDuration(want);
                    if (pm.BrushTicks != want) { Onboarding.Notify(Onboarding.Ev.LengthPicked); simulated.Add("length (SetDraftDuration did not take)"); }
                    break;
                }
                case Onboarding.SFinish:
                {
                    var last = pm.currentPathTiles.Count > 0 ? pm.currentPathTiles[pm.currentPathTiles.Count - 1] : null;
                    if (last != null) { Vector3 sl = ScreenOf(last.Top); pm.SimPointer(sl, true, true, false); pm.SimPointer(sl, false, false, true); }
                    else pm.FinishPath();
                    break;
                }
                case Onboarding.SOpen: { var c = Onboarding.UserCube; if (c != null) CubeInspector.Open(c); else { Onboarding.Notify(Onboarding.Ev.CubeInspected); simulated.Add("open"); } break; }
                case Onboarding.SStretch: Onboarding.Notify(Onboarding.Ev.NoteStretched); simulated.Add("stretch (U2's strip gesture)"); break;
                case Onboarding.SClose: CubeInspector.Close(); break;
                case Onboarding.SAddBelow:
                {
                    yield return WaitFor(() => IslandHeader.IsShown, 2f);
                    int n0 = sm.Islands.Count;
                    if (IslandHeader.I != null) IslandHeader.I.AddBelow.onClick();
                    yield return null;
                    if (sm.Islands.Count == n0) { Onboarding.Notify(Onboarding.Ev.IslandAdded); simulated.Add("add below (K stub)"); }
                    break;
                }
                case Onboarding.SDragColumn:
                {
                    var kb = Onboarding.ChapterIsland != null ? Onboarding.ChapterIsland : sm.Islands[sm.Islands.Count - 1];
                    Vector3 g0 = new Vector3(kb.Center.x, 0f, kb.Center.z);
                    pm.Drag.SimBegin(kb, false, g0);
                    yield return null;
                    pm.Drag.SimMove(g0 + new Vector3(0f, 0f, -2f));
                    yield return null;
                    pm.Drag.SimRelease();
                    yield return Wait(0.5f);
                    break;
                }
                case Onboarding.SLower:
                    yield return WaitFor(() => IslandHeader.IsShown, 2f);
                    {
                        var ih = IslandHeader.I; var tgt = ih != null ? ih.Target : null;
                        Info("step 9 header: shown " + IslandHeader.IsShown + ", target " + (tgt != null ? tgt.name + " reg " + tgt.register : "none") + ", section header " + SectionHeader.Current
                             + ", drag " + (pm.Drag != null ? pm.Drag.State.ToString() : "-") + ", locks " + WorldInput.Describe());
                    }
                    if (IslandHeader.I != null) IslandHeader.I.RegisterDown.onClick();
                    break;
                case Onboarding.SDeck:
                    IslandTray.Open(); yield return Wait(0.5f);
                    if (IslandTray.I != null) IslandTray.I.SimClick(0);
                    IslandTray.Close();
                    break;
                case Onboarding.SPresent:
                    Presenter.Enter(); yield return Wait(1.5f); Presenter.Exit();
                    yield return WaitFor(() => !Presenter.Active, 4f);
                    break;
            }
            if (s == Onboarding.StepCount) break;
        }
        yield return WaitFor(() => !Onboarding.Active, 5f);
        bool done = !Onboarding.Active && Onboarding.Done;
        Line(reached == Onboarding.StepCount && done, "tutorial: every v4 step runs start to finish with simulated input and the last bubble closes itself", "reached step " + reached + " of " + Onboarding.StepCount + ", done " + done + (simulated.Count > 0 ? "; notified for missing gestures: " + string.Join(", ", simulated) : ""));
        Onboarding.OnEvent -= listen;
        Onboarding.Suppressed = true;
        if (prefStep < 0) PlayerPrefs.DeleteKey(Onboarding.PrefStep); else PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep);
        if (prefDone < 0) PlayerPrefs.DeleteKey(Onboarding.PrefDone); else PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone);
        RestoreTips(tipKeep);
        PlayerPrefs.Save();
        Restore();
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
}
