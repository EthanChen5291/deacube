using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Package U1 (v7) Play-mode checks (SPEC v7 §7, §13.4, §13.5, §14.6, §16.4, §17.1, §17.2, §19; scratchpad/v7/brief_U1.md), with
/// PathManager.AutoHand = false (the real game): the deck's STAIRS and MELODY cards (a click and a drag each: the island kind, its column, the
/// stairs' runner and its group, the hand picking up the phrase's cube, one History entry per gesture); every grid's header (the v7 item set;
/// the repeat style + vary stickers from ×2, rewind on / off, vary 0 → 1 → 2 → 0, flow 0 → 3 in flow style and the wand back to hop, "fill the
/// section" = flow to the section's last column, launch on / off, the refusals); the STAIRS header (its item set, the type fan: six types, the
/// gold star = the best StairFit, a hovered type plays that run (= StairPitches with that type, strictly falling), a click picks it; direction,
/// steps − n + within 3..8, the rate's cycle, lead-in); the PHRASE header (½ 1 2 4 + auto, double, cells); the SECTION HEADER on a hovered
/// plinth (the letter = the plinth's, split here, join, duplicate, delete, drums at the section's first column, launch into the next section,
/// the name picker — §17.1); the RAIL (a bracket + letter per section, measure numbers = the measures, dotted slots for a short section's missing
/// measures, the gap between sections only, stairs beads, the heights landscape = GroundOf, flow waves, launch marks, phrase bars, the section
/// names); the MEASURE TAG ("½ measure" … "4 measures · 1 section", growing with the phrase); the SECTIONS support (a section reaching 4
/// measures sparkles on the rail and the world) and the v7 tips (each once: + §17.1 names, §19.1 capacity, §20.1 keys); §20.1 a keyboard's range
/// (+ an octave below / above through SongManager.SetKeyRange, the fit rule); Synth late / errors 0; the user's save untouched.
/// Captures (1920×1080): u1_7_deck_cards, u1_7_header_grid, u1_7_header_keyboard, u1_7_header_stairs, u1_7_header_stairs_fan, u1_7_header_phrase,
/// u1_7_section_header, u1_7_section_names, u1_7_rail, u1_7_measure_tag, u1_7_tip_sections, u1_7_tip_stairs.
/// Start with <see cref="RunAll"/>, poll <see cref="Done"/> or Captures/u1_7_report.txt.
/// </summary>
public static class V7ChecksU1
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u1_7_report.txt");
    static StringBuilder sb; static int num, pass, fail;
    static int pushes;
    static readonly List<string> events = new List<string>();

    static void Line(bool ok, string what, string detail = null)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" — ").Append(detail);
        sb.Append('\n');
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }
    static void Flush(bool final)
    {
        Report = "V7ChecksU1: " + pass + " PASS, " + fail + " FAIL" + (final ? "" : " (running)") + "\n" + sb;
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float s) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static bool captures;
    static IEnumerator Shot(string file)
    {
        if (!captures) yield break;
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
        yield return Wait(0.3f);
    }

    static void OnHistory() { pushes++; }
    static void OnEvent(string e) { events.Add(e); }

    static void LoadFixture()
    {
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    static void Frame(Vector3 centre, float dist)
    {
        if (OrbitCamera.I == null) return;
        OrbitCamera.I.FrameBounds(new Bounds(centre, new Vector3(dist, 2f, dist * 0.6f)), 0.05f, true);
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static Vector3 ScreenOf(Vector3 world) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero; }
    static PointerEventData Ptr() => new PointerEventData(EventSystem.current);
    static string Join(List<string> l) => string.Join(" ", l.ToArray());

    static KeyBlock FirstOf(Func<KeyBlock, bool> f) { var sm = SongManager.I; foreach (var kb in sm.Islands) if (kb != null && f(kb)) return kb; return null; }
    static int IndexOf(KeyBlock kb) => kb != null && SongManager.I != null ? SongManager.I.Islands.IndexOf(kb) : -1;
    static AudioCube RunnerOf(KeyBlock kb) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && kb != null && c.Island == kb) return c; return null; }

    // ================================================================== run
    public static string RunAll(bool withCaptures = true)
    {
        if (UIManager.I == null || SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; captures = withCaptures;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        UIManager.I.StartCoroutine(Routine());
        return "started";
    }

    static IEnumerator Routine()
    {
        sb = new StringBuilder(); num = pass = fail = 0;
        SongIO.QuitAutosave = false;
        string userMd5 = Md5(SongIO.Path);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        var tipPrefs = new int[Onboarding.AllTips.Length];
        for (int k = 0; k < tipPrefs.Length; k++) tipPrefs[k] = PlayerPrefs.GetInt(Onboarding.PrefTip + Onboarding.AllTips[k], -1);
        bool suppressed = Onboarding.Suppressed, autoHand = PathManager.AutoHand, loop = GlobalClock.LoopSong;
        PathManager.AutoHand = false;
        History.OnChanged += OnHistory; Onboarding.OnEvent += OnEvent;
        V4ChecksU1.Prepare();
        Clipboard.Clear(); GridSelection.Clear();
        try { LoadFixture(); } catch (Exception e) { Line(false, "fixture", e.Message); }
        yield return null; yield return null;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return Wait(0.6f);
        var sm = SongManager.I;
        Info("screen " + Screen.width + "x" + Screen.height + ", islands " + sm.Islands.Count + ", columns " + sm.ColumnCount + ", sections " + sm.SectionCount + ", key " + MusicTheory.KeyOfSong() + ", AutoHand " + PathManager.AutoHand);

        IEnumerator[] parts = { Cards(), GridHeader(), StairsHeader(), PhraseHeader(), Sections(), Rail(), Tag(), Tips() };
        foreach (var part in parts)
        {
            bool threw = false; string why = null;
            while (true)
            {
                object cur;
                try { if (!part.MoveNext()) break; cur = part.Current; }
                catch (Exception e) { threw = true; why = e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]; break; }
                yield return cur;
            }
            if (threw) Line(false, "a check section threw", why);
            Flush(false);
            Restore();
            yield return null;
        }

        Line(Synth.LateEvents == late0 && Synth.Errors == err0, "Synth: no late events or errors over the whole run", "late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " | " + Synth.Stats());
        History.OnChanged -= OnHistory; Onboarding.OnEvent -= OnEvent;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        Onboarding.ClearTips();
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        for (int k = 0; k < tipPrefs.Length; k++) RestorePref(Onboarding.PrefTip + Onboarding.AllTips[k], tipPrefs[k]);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        GlobalClock.LoopSong = loop;
        Clipboard.Clear(); GridSelection.Clear();
        try { LoadFixture(); } catch (Exception) { }
        PathManager.AutoHand = autoHand;
        PathManager.SimOnly = false;
        Line(Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + userMd5);
        Flush(true);
        Done = true;
    }

    static void RestorePref(string key, int v) { if (v < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, v); }

    static void Restore()
    {
        CubeInspector.CloseImmediate();
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        var pm = PathManager.I;
        if (pm != null && pm.Drag != null && pm.Drag.State != IslandDrag.Phase.Idle) pm.Drag.Cancel();
        if (pm != null && pm.IsDrawing) pm.CancelPath();
        if (pm != null) pm.PutDown();
        IslandHeader.CloseMore(); IslandHeader.Hide(); SectionHeader.Hide(); MeasureTag.Pin(null);
        var ui = UIManager.I;
        if (ui != null) { ui.ClearSelection(); if (ui.Strip != null) ui.Strip.CloseNow(); }
        if (IslandTray.IsOpen) IslandTray.Close();
        CursorKit.ResetAll();
        GridSelection.Clear();
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
    }

    // ------------------------------------------------------------------ 1. the STAIRS and MELODY cards (SPEC v7 §7.1, §14.6)
    static IEnumerator Cards()
    {
        var sm = SongManager.I; var tray = IslandTray.Ensure();
        bool cards = tray != null && tray.Tool(5) != null && tray.Tool(6) != null && tray.StairsCard == tray.Tool(5) && tray.MelodyCard == tray.Tool(6) && tray.Tool(4) != null && tray.Tool(4) != tray.Tool(5);
        Line(cards && Hints.Has("tray.stairs") && Hints.Has("tray.melody"), "deck: the STAIRS card (Tool 5) and the MELODY card (Tool 6) join the tools row (hints tray.stairs / tray.melody)");
        UIManager.I.SelectMeasure(1, false);
        IslandTray.Open();
        yield return Wait(0.7f);
        var s5 = tray.Tool(5).transform as RectTransform; var s6 = tray.Tool(6).transform as RectTransform;
        Rect r5 = default(Rect), r6 = default(Rect);
        // v8: the melody card is retired (hidden: "way too complex" — the keyboard is the melody tool); the stairs card stays on screen
        bool on = s5 != null && s6 != null && s5.gameObject.activeInHierarchy && !s6.gameObject.activeInHierarchy;
        if (on) { r5 = ScreenRect(s5); }
        Line(on && r5.width >= 28f && r5.xMax < Screen.width, "the stairs card is on screen in the open deck (≥ 28 px); v8: the melody card is hidden",
             "stairs " + r5 + ", melody shown " + (s6 != null && s6.gameObject.activeInHierarchy));
        yield return Shot("u1_7_deck_cards.png");

        // click: stairs into the focused island's column with a runner of the lead group; one History entry
        int n0 = sm.Islands.Count, p0 = pushes, col = sm.Islands[1].column;
        tray.SimTool(5);
        yield return Frames(3);
        var st = FirstOf(k => k.IsStairs);
        var run = RunnerOf(st);
        Line(sm.Islands.Count == n0 + 1 && st != null && st.column == col && run != null && run.instrument == 3 && pushes - p0 == 1,
             "stairs card click: a stairs island (kind 3) in the focused island's column with one runner cube of the lead group, one History entry",
             "islands " + n0 + " → " + sm.Islands.Count + ", column " + (st != null ? st.column : -1) + " (focused " + col + "), runner " + (run != null ? "group " + run.instrument : "none") + ", pushes " + (pushes - p0) + ", type " + (st != null ? Harmony.StairTypeWord(st.stairType) : "-"));
        Line(st != null && st.stairType == SongOps.BestStairType(st.column) && st.stairType == IslandHeader.BestStairType(st), "…its type is the one the header's star marks (Harmony.StairFit over its chord)", st != null ? Harmony.StairTypeWord(st.stairType) : "");
        // with a cube in the hand: the runner is of that group
        LoadFixture(); yield return Frames(2);
        PathManager.I.PickUpCube(0);
        UIManager.I.SelectMeasure(2, false);
        tray.SimTool(5);
        yield return Frames(3);
        st = FirstOf(k => k.IsStairs); run = RunnerOf(st);
        Line(st != null && st.column == sm.Islands[IndexOf(st)].column && run != null && run.instrument == 0, "…with a cube in the hand the runner is of the hand's group", run != null ? "group " + run.instrument : "none");
        PathManager.I.PutDown();
        // drag through the ghost: a STACK slot in a column, the end of the song → a new column
        LoadFixture(); yield return Frames(2);
        int c3 = 3; var a3 = sm.AnchorOf(c3);
        var g = tray.SimStairsDragStart();
        n0 = sm.Islands.Count; p0 = pushes;
        int nc0 = sm.ColumnCount;
        if (g != null && a3 != null)
        {
            tray.SimDragAt(new Vector3(sm.ColumnCenterX(c3), 0f, a3.FrontEdge - a3.Depth * 0.5f - 0.6f));
            yield return Frames(3);
            var snap = g.Snapshot();
            int at = tray.SimDragRelease();
            yield return Frames(3);
            var kb = at >= 0 && at < sm.Islands.Count ? sm.Islands[at] : null;
            bool ok = kb != null && kb.IsStairs && pushes - p0 == 1 && (snap.mode == IslandGhost.Mode.Stack ? kb.column == snap.column && sm.ColumnCount == nc0 : sm.ColumnCount == nc0 + 1);
            Line(ok, "stairs card drag through the ghost: the slot decides (a STACK slot → that column; an INSERT slot → a new column), one History entry",
                 "slot " + snap.mode + " col " + snap.column + " insertAt " + snap.insertAt + " → island " + at + " column " + (kb != null ? kb.column : -1) + ", columns " + nc0 + " → " + sm.ColumnCount + ", pushes " + (pushes - p0));
        }
        else Line(false, "stairs card drag: no ghost");
        LoadFixture(); yield return Frames(2);
        g = tray.SimStairsDragStart(); p0 = pushes; nc0 = sm.ColumnCount;
        if (g != null)
        {
            var last = sm.AnchorOf(sm.ColumnCount - 1);
            tray.SimDragAt(new Vector3(last.EastEdge + KeyBlock.IslandWidth * 1.2f, 0f, last.Center.z));
            yield return Frames(3);
            var snap = g.Snapshot();
            int at = tray.SimDragRelease();
            yield return Frames(3);
            var kb = at >= 0 && at < sm.Islands.Count ? sm.Islands[at] : null;
            Line(kb != null && kb.IsStairs && sm.ColumnCount == nc0 + 1 && kb.column == sm.ColumnCount - 1 && pushes - p0 == 1 && RunnerOf(kb) != null,
                 "…dropped past the song's end: a new last column (with its runner), one History entry", "slot " + snap.mode + ", column " + (kb != null ? kb.column : -1) + " of " + sm.ColumnCount + ", pushes " + (pushes - p0));
        }
        // melody: a click → a one-measure phrase at the focused column, the hand ready to draw; one History entry
        LoadFixture(); yield return Frames(2);
        UIManager.I.SelectMeasure(2, false);
        n0 = sm.Islands.Count; p0 = pushes;
        tray.SimTool(6);
        yield return Frames(3);
        var ph = FirstOf(k => k.IsPhrase);
        Line(ph != null && ph.column == 2 && ph.phraseBeats == GlobalClock.BeatsPerBar && PathManager.I.CubeInHand && pushes - p0 == 1 && tray.PhrasesAdded >= 1,
             "melody card click: a one-measure phrase (kind 4) at the focused column, a cube in the hand ready to draw, one History entry",
             "phrase " + (ph != null ? "col " + ph.column + ", " + ph.phraseBeats + " beats" : "none") + ", hand " + PathManager.I.CubeInHand + ", pushes " + (pushes - p0));
        PathManager.I.PutDown();
        // melody drag → the column it is dropped in line with
        LoadFixture(); yield return Frames(2);
        p0 = pushes;
        var a4 = sm.AnchorOf(4);
        int pi = tray.SimMelodyDrop(new Vector3(sm.ColumnCenterX(4), 0f, a4 != null ? a4.Center.z : 0f));
        yield return Frames(3);
        ph = pi >= 0 && pi < sm.Islands.Count ? sm.Islands[pi] : null;
        Line(ph != null && ph.IsPhrase && ph.column == 4 && pushes - p0 == 1, "melody card drag: the phrase starts at the column the card is dropped in line with, one History entry",
             "phrase " + pi + " column " + (ph != null ? ph.column : -1) + ", pushes " + (pushes - p0));
        PathManager.I.PutDown();
        IslandTray.Close();
        LoadFixture(); yield return Frames(2);
    }

    static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, c[0]), b = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    static readonly string[] ChordItems = { "Grip", "Eye", "RegDown", "RegUp", "Repeat", "Extend", "Launch", "DupRight", "DupBelow", "More" };   // §21: one EXTEND control (was Carry + Flow)
    static string CaptionOf(Component c) { var k = c != null ? c.GetComponent<InkCaptionHover>() : null; return k != null ? k.words : ""; }
    static readonly string[] StairItems = { "Grip", "Eye", "StairType", "StairDir", "StepsDown", "StepsUp", "StairRate", "StairLead", "RegDown", "RegUp", "Repeat", "More" };
    static readonly string[] PhraseItems = { "Grip", "Eye", "Len½", "Len1", "Len2", "Len4", "LenAuto", "PhraseDouble", "PhraseCells", "RegDown", "RegUp", "Repeat", "More" };

    static bool Same(List<string> got, string[] want)
    {
        if (got.Count != want.Length) return false;
        for (int i = 0; i < want.Length; i++) if (got[i] != want[i]) return false;
        return true;
    }

    static IEnumerator ShowHeader(KeyBlock kb, float dist = 14f)
    {
        IslandHeader.Hide();
        yield return Frames(2);
        Frame(kb.Center, dist);
        yield return Wait(0.25f);
        IslandHeader.Show(kb);
        yield return Wait(0.45f);
    }

    // ------------------------------------------------------------------ 2. every grid's header (SPEC v7 §7.3, §13.5)
    static IEnumerator GridHeader()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        LoadFixture(); yield return Frames(2);
        yield return ShowHeader(sm.Islands[0]);
        var items = h.ItemsShown();
        Line(Same(items, ChordItems) && h.KindShown == 0, "a chord island's band: grip, eye, ▼ ▲, repeat, EXTEND → (§21: one control for carry and flow), LAUNCH, duplicates, more (left to right)", Join(items));
        Line(!h.StyleShown && !h.VaryShown && h.FillShown, "at ×1 no repeat-style / vary stickers; the fill-the-section tab stands over extend (columns follow in its section)",
             "style " + h.StyleShown + ", vary " + h.VaryShown + ", fill " + h.FillShown);
        int p0 = pushes;
        h.Repeat.onClick(); yield return Frames(3);
        Line(sm.Islands[0].repeat == 2 && h.StyleShown && h.VaryShown && pushes - p0 == 1, "from ×2 the repeat STYLE (⟲) and VARY (dice) stickers stand up over the repeat button", "repeat " + sm.Islands[0].repeat);
        p0 = pushes; h.Style.onClick(); yield return Frames(3);
        bool rw1 = sm.Islands[0].rewind; int d1 = pushes - p0;
        p0 = pushes; h.Style.onClick(); yield return Frames(3);
        Line(rw1 && !sm.Islands[0].rewind && d1 == 1 && pushes - p0 == 1, "style: time unwinds (rewind) on / off, one History entry each (SongManager.SetRewind)", "on " + rw1 + " → " + sm.Islands[0].rewind);
        var vs = new List<string>(); bool vok = true;
        foreach (int want in new[] { 1, 2, 0 }) { p0 = pushes; h.Vary.onClick(); yield return Frames(3); vs.Add(sm.Islands[0].vary + "/" + (pushes - p0)); vok &= sm.Islands[0].vary == want && pushes - p0 == 1; }
        Line(vok, "vary: same → vary → answer → same, one History entry each (SetVary)", Join(vs));
        // §21 EXTEND → (was: flow 0 → 3 in flow style + the carry wand in hop style): one control, ×1 → ×2 → ×3 → off, a LONG GRID (SetCarry(at, n, 1))
        var fs = new List<string>(); bool fok = true;
        string cap0 = CaptionOf(h.Extend);
        foreach (int want in new[] { 1, 2, 3, 0 })
        {
            p0 = pushes; h.Extend.onClick(); yield return Frames(3);
            var k0 = sm.Islands[0];
            fs.Add(k0.carry + "s" + k0.carryStyle + "'" + h.ExtendSticker + "'");
            fok &= k0.carry == want && (want == 0 || k0.carryStyle == 1) && pushes - p0 == 1 && h.ExtendSticker == (want > 0 ? "×" + want : "");
        }
        Line(fok && cap0 == IslandHeader.ExtendCaptionOff && h.Flow == h.Extend && h.Carry == h.Extend, "§21 EXTEND → (one control for carry and flow): ×1 → ×2 → ×3 → off — one long grid over the next grids of its section (SetCarry(at, n, 1)), its ×n sticker, one History entry each",
             Join(fs) + ", caption \"" + cap0 + "\"");
        sm.SetCarry(0, 2, 0); yield return Frames(3);
        Line(h.ExtendSticker == "×2", "§21: a carry saved in v6's hop style shows on the same control (the style is ignored: every carry is a long grid)", "sticker '" + h.ExtendSticker + "'");
        // the capture: extend ×2 on the header and its long-grid bar on the rail
        yield return ShowHeader(sm.Islands[0]);
        yield return Wait(0.3f);
        yield return Shot("u1_7_extend.png");
        sm.SetCarry(0, 0, 0); yield return Frames(3);
        // fill the section: flow to the section's last column in one click
        int s0 = sm.SectionOf(0), last = sm.SectionLast(s0), want3 = Mathf.Min(ProjectConfig.MaxCarry, last - 0);
        p0 = pushes; h.FillSection.onClick(); yield return Frames(3);
        var tg = sm.CarryTargets(0);
        Line(sm.Islands[0].carry == want3 && sm.Islands[0].carryStyle == 1 && pushes - p0 == 1 && tg.Count == want3 && tg.Count > 0 && tg[tg.Count - 1].column == last && !h.FillShown,
             "FILL THE SECTION: one long grid to the section's end in one click (carry = its last column − this one), then the tab steps down",
             "section " + s0 + " columns 0.." + last + ", carry " + sm.Islands[0].carry + " style " + sm.Islands[0].carryStyle + ", targets " + tg.Count + ", tab " + h.FillShown);
        // launch on / off (§21: a build-up into the next section — the riser and its crash; nothing is flung)
        string capOff = CaptionOf(h.Launch);
        p0 = pushes; h.Launch.onClick(); yield return Frames(3);
        bool l1 = sm.Islands[0].launch; int dl = pushes - p0;
        yield return Frames(2);
        string capOn = CaptionOf(h.Launch);
        // the look: ×2 + rewind + vary + flow ×2 + launch
        h.Style.onClick(); yield return Frames(2); h.Vary.onClick(); yield return Frames(2);
        sm.SetCarry(0, 2, 1); yield return Wait(0.5f);
        yield return Shot("u1_7_header_grid.png");
        p0 = pushes; h.Launch.onClick(); yield return Frames(3);
        Line(l1 && dl == 1 && !sm.Islands[0].launch && pushes - p0 == 1 && capOff == IslandHeader.LaunchCaptionOff && capOn == IslandHeader.LaunchCaptionOn,
             "launch (↗): on / off, one History entry each (SetLaunch); §21: its captions say a build-up / a riser into the next section (no fling)", "on " + l1 + ", \"" + capOff + "\" / \"" + capOn + "\"");
        // the section's last column: no fill tab; the song's last column without the loop: launch refuses
        yield return ShowHeader(sm.Islands[last]);
        Line(!h.FillShown && h.FlowShown, "the section's last column has no fill tab (nothing after it in its section); its extend control stays", "column " + last);
        int er0 = h.CarryRefusals, ep0 = pushes;
        h.Extend.onClick(); yield return Frames(3);
        Line(h.CarryRefusals == er0 + 1 && pushes == ep0 && sm.Islands[last].carry == 0, "§21: extend refuses on a section's last column (a long grid never crosses its section)", "refusals +" + (h.CarryRefusals - er0));
        GlobalClock.LoopSong = false;
        yield return ShowHeader(sm.Islands[sm.Islands.Count - 1]);
        int r0 = h.V7Refusals; p0 = pushes;
        h.Launch.onClick(); yield return Frames(3);
        Line(h.V7Refusals == r0 + 1 && pushes == p0 && !sm.Islands[sm.Islands.Count - 1].launch, "launch refuses at the song's last column when the song does not loop (nowhere to land)");
        GlobalClock.LoopSong = true;
        // a keyboard: the same grid items (carry, flow, launch)
        int ki = sm.AddKeyboardIsland(0, false); yield return Frames(3);
        if (ki >= 0)
        {
            yield return ShowHeader(sm.Islands[ki]);
            items = h.ItemsShown();
            Line(Same(items, ChordItems) && h.KindShown == 2, "a keyboard's band: the same grid items (flow and launch too)", Join(items));
            // §20.1 the keyboard's range: + an octave below / above (one History entry each, the notes keep their pitch: SongManager.SetKeyRange)
            var kk = sm.Islands[ki];
            int low0 = kk.chordRootMIDI, n0 = kk.KeyCount;
            bool ends = h.KeysBelow != null && h.KeysBelow.gameObject.activeInHierarchy && h.KeysAbove != null && h.KeysAbove.gameObject.activeInHierarchy && !h.KeysFitShown;
            int pk = pushes; h.KeysAbove.onClick(); yield return Frames(4);
            kk = sm.Islands[ki]; int low1 = kk.chordRootMIDI, n1 = kk.KeyCount; int dk1 = pushes - pk;
            pk = pushes; h.KeysBelow.onClick(); yield return Frames(4);
            kk = sm.Islands[ki]; int low2 = kk.chordRootMIDI, n2 = kk.KeyCount; int dk2 = pushes - pk;
            yield return ShowHeader(kk); yield return Wait(0.3f);
            yield return Shot("u1_7_header_keyboard.png");
            Line(ends && low1 == low0 && n1 == n0 + 12 && dk1 == 1 && low2 == low0 - 12 && n2 == n0 + 24 && dk2 == 1,
                 "a keyboard's ends: + an octave above / below (SongManager.SetKeyRange), one History entry each; no fit tab without notes",
                 "keys " + low0 + "+" + n0 + " → " + low1 + "+" + n1 + " → " + low2 + "+" + n2 + ", ends " + ends);
            int fl, fn; bool f1 = IslandHeader.FitKeys(53, 49, 60, 70, out fl, out fn); bool fitA = f1 && fl == 53 && fn == 25;
            bool f2 = IslandHeader.FitKeys(41, 49, 60, 64, out fl, out fn); bool fitB = f2 && fl == 53 && fn == 13;
            bool f3 = IslandHeader.FitKeys(41, 49, 99, 0, out fl, out fn);
            Line(fitA && fitB && !f3, "fit to the melody: whole octaves from the lowest key that hold the notes, at least one (53+49 with notes 60..70 → 53+25; 41+49 with 60..64 → 53+13; no notes → nothing)");
        }
        IslandHeader.Hide();
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 3. the STAIRS header (SPEC v7 §7.2)
    static IEnumerator StairsHeader()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        LoadFixture(); yield return Frames(2);
        int si = SongOps.AddStairIsland(1, false); yield return Frames(3);
        if (si < 0) { Line(false, "stairs header: no stairs island (SongOps.AddStairIsland)"); yield break; }
        var kb = sm.Islands[si];
        yield return ShowHeader(kb, 12f);
        var items = h.ItemsShown();
        Line(Same(items, StairItems) && h.KindShown == 3, "the stairs band: grip, eye, TYPE, DIRECTION, STEPS − +, RATE, LEAD-IN, ▼ ▲, repeat, more (no carry / flow / launch)", Join(items));
        Line(h.StepsShown == kb.stairSteps.ToString(), "the steps digit shows the island's steps", "'" + h.StepsShown + "' = " + kb.stairSteps);
        // the type fan: six types, the gold star on the best fit, a hovered type plays its run
        h.StairType.onClick(); yield return Frames(3);
        int best = IslandHeader.BestStairType(kb);
        bool six = true; for (int t = 0; t < 6; t++) six &= h.FanItem(t) != null && h.FanItem(t).gameObject.activeInHierarchy;
        Line(h.FanOpen && six && h.FanStar == best && best == SongOps.BestStairType(kb.column), "the TYPE fan opens: six types (chord, scale, spark, slide, bright, walk), the gold star on the best fit (Harmony.StairFit)",
             "star on " + Harmony.StairTypeWord(h.FanStar) + " (best " + Harmony.StairTypeWord(best) + ")");
        int hover = best == 1 ? 3 : 1;
        int a0 = h.RunAuditions;
        h.FanItem(hover).OnPointerEnter(Ptr());
        yield return Frames(3);
        var want = IslandHeader.RunOf(sm.Islands[si], hover);
        var got = h.LastRun;
        bool same = got != null && want.Length == got.Length && want.Length > 0; if (same) for (int k = 0; k < want.Length; k++) same &= want[k] == got[k];
        bool falling = same; if (same) for (int k = 1; k < got.Length; k++) falling &= got[k] < got[k - 1];
        var runner = RunnerOf(sm.Islands[si]);
        Line(h.RunAuditions == a0 + 1 && same && falling && runner != null && h.LastRunSlot == Instruments.SlotOf(runner.instrument, runner.voice),
             "hovering a type plays that run on the preview bus in the runner's sound: the stairs' pitches with that type, strictly falling",
             Harmony.StairTypeWord(hover) + " " + (got != null ? string.Join(",", Array.ConvertAll(got, x => x.ToString())) : "-"));
        yield return Wait(0.35f);
        yield return Shot("u1_7_header_stairs_fan.png");
        h.FanItem(hover).OnPointerExit(Ptr());
        int p0 = pushes;
        h.FanItem(hover).onClick(); yield return Frames(4);
        Line(sm.Islands[si].stairType == hover && !h.FanOpen && pushes - p0 == 1, "a click on a type picks it (SongOps.SetStair), the fan closes, one History entry", Harmony.StairTypeWord(sm.Islands[si].stairType));
        // direction
        p0 = pushes; h.StairDir.onClick(); yield return Frames(4);
        int col = sm.Islands[si].column;
        Line(sm.Islands[si].stairDir > 0 && pushes - p0 == 1 && sm.GroundOf(col + 1) > 0.1f, "direction ↓ → ↑: the stairs climb, the next columns' ground rises", "dir " + sm.Islands[si].stairDir + ", ground after " + sm.GroundOf(col + 1).ToString("F2"));
        // steps − n +
        var ss = new List<string>(); bool sok = true;
        p0 = pushes; h.StepsUp.onClick(); yield return Frames(4); ss.Add(h.StepsShown); sok &= sm.Islands[si].stairSteps == 5 && pushes - p0 == 1 && h.StepsShown == "5";
        foreach (int w in new[] { 4, 3 }) { p0 = pushes; h.StepsDown.onClick(); yield return Frames(4); ss.Add(h.StepsShown); sok &= sm.Islands[si].stairSteps == w && pushes - p0 == 1; }
        int r0 = h.V7Refusals; p0 = pushes; h.StepsDown.onClick(); yield return Frames(3);
        Line(sok && h.V7Refusals == r0 + 1 && pushes == p0 && sm.Islands[si].stairSteps == 3, "steps − n +: 4 → 5 → 4 → 3, below 3 refuses (a thud, no History)", Join(ss));
        // rate: the sizes cycle
        var rs = new List<string>(); bool rok = true;
        foreach (int w in new[] { 8, 6, 24, 12 }) { p0 = pushes; h.StairRate.onClick(); yield return Frames(4); int r = MeasureState.StairRate(sm.Islands[si].stairRate); rs.Add(r.ToString()); rok &= r == w && pushes - p0 == 1; }
        Line(rok, "rate (a cube's size): even → triplets → quick → slow → even (12 → 8 → 6 → 24 → 12 ticks a step)", Join(rs));
        // lead-in
        bool lead0 = sm.Islands[si].stairLead;
        p0 = pushes; h.StairLead.onClick(); yield return Frames(4);
        bool lead1 = sm.Islands[si].stairLead; int dl = pushes - p0;
        yield return ShowHeader(sm.Islands[si], 12f);
        yield return Shot("u1_7_header_stairs.png");
        p0 = pushes; h.StairLead.onClick(); yield return Frames(4);
        Line(lead0 && !lead1 && dl == 1 && sm.Islands[si].stairLead && pushes - p0 == 1, "lead-in: the run ends at the chord change ⟷ starts with the chord, one History entry each");
        // the more card: duplicate + delete
        h.More.onClick(); yield return Frames(3);
        Line(h.MoreDuplicateRight.gameObject.activeInHierarchy && h.MoreDuplicateBelow.gameObject.activeInHierarchy && h.DeleteButton.gameObject.activeInHierarchy && !h.ChordButton.gameObject.activeInHierarchy,
             "the stairs' more card: duplicate → / ↓ and delete (no chord wheel)");
        IslandHeader.CloseMore(); IslandHeader.Hide();
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 4. the PHRASE header (SPEC v7 §14.6, §17.2)
    static IEnumerator PhraseHeader()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        LoadFixture(); yield return Frames(2);
        int pi = SongOps.AddPhrase(2); yield return Frames(3);
        PathManager.I.PutDown();
        var ph = FirstOf(k => k.IsPhrase);
        if (ph == null) { Line(false, "phrase header: no phrase (SongOps.AddPhrase)", "returned " + pi); yield break; }
        yield return ShowHeader(ph, 14f);
        var items = h.ItemsShown();
        Line(Same(items, PhraseItems) && h.KindShown == 4, "the phrase band: grip, eye, LENGTH ½ 1 2 4 + auto, DOUBLE, CELLS, ▼ ▲, repeat, more", Join(items));
        var ls = new List<string>(); bool lok = true;
        int bpb = GlobalClock.BeatsPerBar;
        foreach (var kv in new[] { new[] { 2, 2 * bpb }, new[] { 0, bpb / 2 }, new[] { 1, bpb } })
        {
            int p0 = pushes; h.PhraseLength(kv[0]).onClick(); yield return Frames(4);
            ph = FirstOf(k => k.IsPhrase);
            ls.Add((ph != null ? ph.phraseBeats : -1) + "/" + (pushes - p0)); lok &= ph != null && ph.phraseBeats == kv[1] && pushes - p0 == 1;
        }
        Line(lok, "length ½ 1 2 4: SongOps.SetPhraseLength, one History entry each (2 measures → ½ → 1)", Join(ls));
        int pd = pushes; h.PhraseDouble.onClick(); yield return Frames(4);
        ph = FirstOf(k => k.IsPhrase);
        Line(ph != null && ph.phraseBeats == 2 * bpb && pushes - pd == 1, "DOUBLE: twice as long (SongOps.DoublePhrase), one History entry", ph != null ? ph.phraseBeats + " beats" : "");
        int pc = pushes; h.PhraseCells.onClick(); yield return Frames(4);
        ph = FirstOf(k => k.IsPhrase);
        int g1 = ph != null ? ph.phraseGrid : -1;
        yield return ShowHeader(ph, 14f);
        yield return Shot("u1_7_header_phrase.png");
        h.PhraseCells.onClick(); yield return Frames(4);
        ph = FirstOf(k => k.IsPhrase);
        Line(g1 == 6 && ph != null && ph.phraseGrid == 12 && pushes - pc == 2, "cells: wide (eighths) ⟷ fine (sixteenths) — SongOps.SetPhraseGrid, one History entry each", g1 + " → " + (ph != null ? ph.phraseGrid : -1));
        int pa = pushes; h.PhraseLength(4).onClick(); yield return Frames(4);
        ph = FirstOf(k => k.IsPhrase);
        Line(ph != null && ph.phraseBeats == IslandHeader.AutoBeats(ph) && ph.phraseBeats == bpb, "auto: the length that holds its notes (none drawn: 1 measure)", ph != null ? ph.phraseBeats + " beats" : "");
        IslandHeader.Hide();
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 5. the SECTION HEADER (SPEC v7 §16.4, §17.1)
    static IEnumerator Sections()
    {
        var sm = SongManager.I; var sh = SectionHeader.I;
        LoadFixture(); yield return Frames(3);
        if (sh == null) { Line(false, "no SectionHeader"); yield break; }
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.05f, true);
        yield return Wait(0.4f);
        int n0 = sm.SectionCount;
        float bx = 0f, w0, e0, w1, e1;
        if (SectionHeader.ColumnSpan(1, out w0, out e0) && SectionHeader.ColumnSpan(2, out w1, out e1)) bx = (e0 + w1) * 0.5f;
        SectionHeader.Show(0, bx); yield return Wait(0.45f);
        Line(SectionHeader.Current == 0 && sh.SplitColumn == 2 && sh.Letter == SectionHeader.LetterOf(0) && sh.Letter.Length == 1, "the section header on section 0: its letter (the plinth's), split here = the boundary nearest the pointer",
             "letter " + sh.Letter + " (plinth " + SectionPlinth.LetterOf(0) + "), split at column " + sh.SplitColumn + ", sections " + n0);
        yield return Shot("u1_7_section_header.png");
        int p0 = pushes;
        sh.SplitButton.onClick(); yield return Frames(4);
        Line(sm.SectionCount == n0 + 1 && sm.SectionFirst(1) == 2 && pushes - p0 == 1, "split here: a new section starts at that column (SongOps.SplitSectionAt), one History entry", "sections " + sm.SectionCount + ", section 1 starts at " + sm.SectionFirst(1));
        SectionHeader.Show(0); yield return Wait(0.3f);
        p0 = pushes; sh.JoinButton.onClick(); yield return Frames(4);
        Line(sm.SectionCount == n0 && pushes - p0 == 1, "join with the next (SongOps.JoinSectionAt the next section's first column), one History entry", "sections " + sm.SectionCount);
        int nc = sm.ColumnCount, bars1 = sm.SectionBars(1);
        SectionHeader.Show(1); yield return Wait(0.3f);
        p0 = pushes; sh.DuplicateButton.onClick(); yield return Frames(4);
        Line(sm.SectionCount == n0 + 1 && sm.ColumnCount == nc + bars1 && pushes - p0 == 1, "duplicate the section: its columns again right after it, a new section, one History entry", "sections " + sm.SectionCount + ", columns " + nc + " → " + sm.ColumnCount);
        SectionHeader.Show(2); yield return Wait(0.3f);
        p0 = pushes; sh.DeleteButton.onClick(); yield return Frames(4);
        Line(sm.SectionCount == n0 && sm.ColumnCount == nc && pushes - p0 == 1, "delete the section, one History entry", "sections " + sm.SectionCount + ", columns " + sm.ColumnCount);
        int m0 = sm.Moons.Count;
        SectionHeader.Show(1); yield return Wait(0.3f);
        p0 = pushes; sh.DrumsButton.onClick(); yield return Frames(4);
        Line(sm.Moons.Count == m0 + 1 && sm.MoonStartColumn(sm.Moons.Count - 1) == sm.SectionFirst(1) && pushes - p0 == 1, "drums: a drum Moon whose part starts at the section's first column, one History entry",
             "moons " + m0 + " → " + sm.Moons.Count + ", start " + (sm.Moons.Count > 0 ? sm.MoonStartColumn(sm.Moons.Count - 1) : -1) + " (section 1 starts at " + sm.SectionFirst(1) + ")");
        SectionHeader.Show(0); yield return Wait(0.3f);
        p0 = pushes; sh.LaunchButton.onClick(); yield return Frames(4);
        bool allOn = SectionHeader.Launching(0); var lg = SectionHeader.LaunchGrids(0); int dl = pushes - p0;
        p0 = pushes; sh.LaunchButton.onClick(); yield return Frames(4);
        Line(allOn && lg.Count > 0 && lg[0].column == sm.SectionLast(0) && dl == 1 && !SectionHeader.Launching(0) && pushes - p0 == 1,
             "launch into the next section: the grids of its last column launch (SongOps.SetLaunchGrids), again: off — one History entry each",
             lg.Count + " grid(s) in column " + sm.SectionLast(0) + ", on " + allOn + " (pushes " + dl + "), off " + !SectionHeader.Launching(0) + " (pushes " + (pushes - p0) + "), header on " + SectionHeader.Current);
        // §17.1 the name picker
        sh.NameButton.onClick(); yield return Frames(3);
        bool seven = true; for (int r = 0; r <= 6; r++) seven &= sh.RoleItem(r) != null && sh.RoleItem(r).gameObject.activeInHierarchy;
        bool fanWas = sh.RoleFanOpen;
        yield return Wait(0.3f);
        yield return Shot("u1_7_section_names.png");
        p0 = pushes; sh.RoleItem(3).onClick(); yield return Frames(4);
        Line(sh.RoleFanOpen == false && seven && sm.SectionRole(0) == 3 && pushes - p0 == 1, "the name picker: none + intro verse chorus bridge drop outro; a click names the section (SongOps.SetSectionRole), one History entry",
             "role " + sm.SectionRole(0) + " (" + SectionRoles.Word(sm.SectionRole(0)) + "), fan was open " + fanWas + ", seven " + seven + ", pushes " + (pushes - p0) + ", header on " + SectionHeader.Current);
        yield return Wait(0.3f);
        Line(sh.RoleShown == 3, "the name button shows the section's name", SectionRoles.Word(sh.RoleShown));
        // the hover path: the pointer over the plinth's front strip (no island there) shows it
        SectionHeader.Hide(); yield return Wait(0.5f);
        Bounds b1;
        if (SectionHeader.SectionBounds(1, out b1))
        {
            Vector3 sp = ScreenOf(new Vector3(b1.center.x, b1.max.y, b1.min.z + 0.3f));
            PathManager.SimPos = sp;
            yield return Wait(0.6f);
            Line(SectionHeader.Current == 1 && PathManager.I.HoverIsEmpty, "hovering a plinth's front strip (nothing of the world there) shows its section's header", "current " + SectionHeader.Current + " at " + sp.ToString("F0"));
            // a tile of an island standing on that plinth is the island's: no section header there
            SectionHeader.Hide(); PathManager.SimPos = new Vector3(-50f, -50f, 0f); yield return Wait(0.6f);
            var isl = sm.AnchorOf(sm.SectionFirst(1));
            TileInteraction tl = null;
            if (isl != null && isl.tiles != null) foreach (var t in isl.tiles) if (t != null && PathManager.TopCubeOn(t) == null) { tl = t; break; }   // a free tile (a cube on it would take the hover)
            if (tl != null) { PathManager.SimPos = ScreenOf(tl.Top); yield return Wait(0.6f); }
            Line(tl != null && PathManager.I.HoverTile == tl && SectionHeader.Current < 0, "…but over an island's tile it stays away (the tile is the island's)", "hover tile " + (PathManager.I.HoverTile != null ? PathManager.I.HoverTile.name : "none") + ", current " + SectionHeader.Current);
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        }
        else Line(false, "no bounds for section 1");
        SectionHeader.Hide();
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 6. the RAIL (SPEC v7 §7.4, §13.4, §17.1)
    static IEnumerator Rail()
    {
        var sm = SongManager.I; var rail = UIManager.I.Rail;
        LoadFixture(); yield return Frames(3);
        rail.CountNow();
        int ns = sm.SectionCount; bool letters = rail.SectionCountShown == ns; int bars = 0, ghosts = 0;
        for (int s = 0; s < ns; s++) { letters &= rail.SectionLetterShown(s) == SectionHeader.LetterOf(s) && rail.SectionFirstShown(s) == sm.SectionFirst(s); bars += sm.SectionBars(s); ghosts += Mathf.Max(0, ProjectConfig.SectionBars - sm.SectionBars(s)); }
        Line(letters && rail.MeasureNumbers == bars && rail.GhostMeasureCount == ghosts, "the rail groups its beads by section: a bracket + letter each, the measure numbers = the measures, dotted slots for a short section's missing measures",
             ns + " sections, numbers " + rail.MeasureNumbers + " = " + bars + ", ghost slots " + rail.GhostMeasureCount + " = " + ghosts);
        int b0 = sm.SectionLast(0);
        Line(rail.GapAfter(b0) > 8f && rail.GapAfter(0) < 1f && rail.GapAfter(1) < 1f, "inside a section the beads touch, a gap between sections", "gap after col 0 " + rail.GapAfter(0).ToString("F1") + ", after the section " + rail.GapAfter(b0).ToString("F1"));
        // stairs: a staircase bead, the ground after it lower — the rail's landscape
        int si = SongOps.AddStairIsland(1, false); yield return Frames(3);
        rail.CountNow(); yield return Wait(0.8f);
        int col = si >= 0 ? sm.Islands[si].column : 1;
        KeyBlock after = sm.AnchorOf(col + 1);
        int ai = IndexOf(after);
        float gw = sm.GroundOf(col + 1) * HudColumnRail.HeightPx;
        Line(si >= 0 && rail.BeadStairs(si) && gw < -1f && Mathf.Abs(rail.ColumnGround(col + 1) - gw) < 0.01f && Mathf.Abs(rail.ColumnGroundShown(col + 1) - gw) < 0.2f && ai >= 0 && rail.BeadY(ai) < rail.BeadY(IndexOf(sm.AnchorOf(0))) - 1f,
             "a stairs bead is a staircase; the columns after a falling stair sit lower on the rail (GroundOf × " + HudColumnRail.HeightPx + " px)",
             "ground after " + sm.GroundOf(col + 1).ToString("F2") + " u → " + rail.ColumnGroundShown(col + 1).ToString("F1") + " px; bead y " + rail.BeadY(ai).ToString("F1") + " vs " + rail.BeadY(IndexOf(sm.AnchorOf(0))).ToString("F1"));
        // a long grid (§21), launch mark, a phrase bar, a name
        sm.SetCarry(0, 2, 1); yield return Frames(2);
        int li = IndexOf(sm.AnchorOf(4));
        if (li >= 0) sm.SetLaunch(li, true);
        yield return Frames(2);
        int pi = SongOps.AddPhrase(2); PathManager.I.PutDown(); yield return Frames(2);
        var ph = FirstOf(k => k.IsPhrase);
        if (ph != null) SongOps.SetPhraseLength(ph, 2 * GlobalClock.BeatsPerBar);
        yield return Frames(2);
        SongOps.SetSectionRole(0, 3); yield return Frames(2);
        rail.CountNow();
        ph = FirstOf(k => k.IsPhrase); int phi = IndexOf(ph);
        li = IndexOf(sm.AnchorOf(sm.Islands.Count > 0 ? 4 : 0));
        var tgs = sm.CarryTargets(0); int lgTo = tgs.Count > 0 ? sm.Islands.IndexOf(tgs[tgs.Count - 1]) : -1;
        bool flowOk = rail.LongGridCount == 1 && rail.LongGridFrom(0) == 0 && rail.LongGridTo(0) == lgTo && rail.CarryArcCount == 0 && rail.FlowWaveCount == 0
                      && rail.LongGridX1(0) - rail.LongGridX0(0) > rail.ColumnWidth(0) * 2.5f;
        bool launchOk = rail.LaunchMarkCount >= 1;
        float colW = rail.ColumnWidth(ph != null ? ph.column : 0);
        bool phOk = phi >= 0 && rail.BeadPhrase(phi) && rail.BeadWidth(phi) > colW * 1.4f && rail.BeadLane(phi) >= 1;
        Line(flowOk, "§21: a long grid is ONE bar around its beads (an extended grid over the next two: no wave, no hops)",
             "long grids " + rail.LongGridCount + " (" + rail.LongGridFrom(0) + " → " + rail.LongGridTo(0) + " = " + lgTo + ", " + (rail.LongGridX1(0) - rail.LongGridX0(0)).ToString("F0") + " px), waves " + rail.FlowWaveCount + ", arcs " + rail.CarryArcCount);
        Line(launchOk, "launch: a small ↗ after the launching island's bead", "marks " + rail.LaunchMarkCount);
        Line(phOk, "a 2-measure phrase is one bar over the two measures it spans, above its columns' stacks", phi >= 0 ? "width " + rail.BeadWidth(phi).ToString("F0") + " (a column " + colW.ToString("F0") + "), lane " + rail.BeadLane(phi) : "no phrase");
        Line(rail.SectionRoleShown(0) == 3, "the bracket shows the section's name picture (§17.1)", SectionRoles.Word(rail.SectionRoleShown(0)));
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.05f, true);
        yield return Wait(0.5f);
        yield return Shot("u1_7_rail.png");
        // §21 (package K's runs): a chain of extends is ONE long grid — the run's last grid extends one more: still one bar, one grid longer
        int chainEnd = rail.LongGridTo(0), size0 = rail.LongGridSize(0);
        var ce = chainEnd >= 0 && chainEnd < sm.Islands.Count ? sm.Islands[chainEnd] : null;
        if (ce != null && ce.column < sm.SectionLast(sm.SectionOf(ce.column)) && sm.LaneNext(ce) != null)
        {
            sm.SetCarry(chainEnd, 1, 1); yield return Frames(2);
            rail.CountNow();
            var run = sm.LongGridOf(sm.Islands[0]);
            Line(rail.LongGridCount == 1 && rail.LongGridSize(0) == size0 + 1 && run != null && run.Count == size0 + 1 && rail.LongGridTo(0) == sm.Islands.IndexOf(run[run.Count - 1]),
                 "§21: a chain of extends (A → B → C … and its last grid on one more) is still ONE long-grid bar, one grid longer (SongManager.LongGridOf)",
                 "bars " + rail.LongGridCount + ", grids " + size0 + " → " + rail.LongGridSize(0) + " (K's run " + (run != null ? run.Count : 0) + ")");
        }
        else Info("§21 chain: the fixture's long grid already ends its section (" + chainEnd + ") — no chain to try");
        // a section reaching its 4 measures sparkles (the rail's bracket + the world) and says so once
        LoadFixture(); yield return Frames(3);
        rail.CountNow();
        int sp0 = rail.SparkleCount, sf0 = Onboarding.SectionsFilled, lastS = sm.SectionCount - 1, have = sm.SectionBars(lastS);
        UIManager.I.SelectMeasure(sm.Islands.Count - 1, false);
        var tray = IslandTray.Ensure();
        for (int k = have; k < ProjectConfig.SectionBars; k++) { IslandTray.Open(); yield return Wait(0.3f); tray.RebuildNow(); tray.SimClick(0); yield return Frames(3); UIManager.I.SelectMeasure(sm.Islands.Count - 1, false); }
        IslandTray.Close();
        yield return Frames(3);
        Line(sm.SectionBars(lastS) == ProjectConfig.SectionBars && Onboarding.SectionsFilled == sf0 + 1 && rail.SparkleCount == sp0 + 1 && Onboarding.LastFilledSection == lastS && events.Contains(Onboarding.Ev.SectionFilled),
             "a section that reaches 4 measures sparkles (rail + world) and reports section.filled", "section " + lastS + " " + have + " → " + sm.SectionBars(lastS) + " measures, sparkles " + (rail.SparkleCount - sp0));
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 7. the MEASURE TAG (SPEC v7 §13.4)
    static IEnumerator Tag()
    {
        var sm = SongManager.I;
        int bpb = GlobalClock.BeatsPerBar;
        bool words = MeasureTag.WordsFor(bpb / 2f) == "½ measure" && MeasureTag.WordsFor(bpb) == "1 measure" && MeasureTag.WordsFor(bpb * 1.5f) == "1½ measures"
                     && MeasureTag.WordsFor(2 * bpb) == "2 measures" && MeasureTag.WordsFor(4 * bpb) == "4 measures · 1 section" && MeasureTag.WordsFor(8 * bpb) == "8 measures · 2 sections";
        Line(words, "the length in words: ½ measure, 1 measure, 1½ measures, 2 measures … 4 measures · 1 section, 8 measures · 2 sections", MeasureTag.WordsFor(4 * bpb));
        LoadFixture(); yield return Frames(2);
        SongOps.AddPhrase(1); PathManager.I.PutDown(); yield return Frames(3);
        var ph = FirstOf(k => k.IsPhrase);
        if (ph == null) { Line(false, "measure tag: no phrase"); yield break; }
        Frame(ph.Center, 16f); yield return Wait(0.3f);
        MeasureTag.Pin(ph); yield return Wait(0.35f);
        string w1 = MeasureTag.Words; bool s1 = MeasureTag.Shown;
        int g0 = MeasureTag.Grows;
        SongOps.SetPhraseLength(ph, 2 * bpb); yield return Frames(3);
        ph = FirstOf(k => k.IsPhrase); MeasureTag.Pin(ph); yield return Wait(0.3f);
        string w2 = MeasureTag.Words;
        SongOps.SetPhraseLength(ph, 4 * bpb); yield return Frames(3);
        ph = FirstOf(k => k.IsPhrase); MeasureTag.Pin(ph); yield return Wait(0.4f);
        string w4 = MeasureTag.Words;
        Frame(ph.Center, 22f); yield return Wait(0.3f);
        yield return Shot("u1_7_measure_tag.png");
        Line(s1 && w1 == "1 measure" && w2 == "2 measures" && w4 == "4 measures · 1 section" && MeasureTag.Grows >= g0 + 2, "the tag on a phrase reads its length as measures and pops as it grows (1 → 2 → 4 measures = a section)",
             w1 + " → " + w2 + " → " + w4 + ", grows +" + (MeasureTag.Grows - g0));
        MeasureTag.Pin(null);
        // a cube in the hand over the phrase shows it too (the drawing case)
        PathManager.I.PickUpCube(3);
        var cell = ph.tiles != null && ph.tiles.Count > 0 ? ph.tiles[ph.tiles.Count / 2] : null;
        string pick = "";
        if (cell != null)
        {
            PathManager.SimPos = ScreenOf(cell.Top); yield return Wait(0.5f);
            var pr = PathManager.I.PickAt(PathManager.SimPos);
            pick = "pick island " + (pr.island != null ? pr.island.name : "none") + " tile " + (pr.tile != null ? pr.tile.name : "none") + " at " + PathManager.SimPos.ToString("F0") + ", locks " + WorldInput.Describe() + ", simOnly " + PathManager.SimOnly;
        }
        Line(cell != null && MeasureTag.Phrase == ph && MeasureTag.Shown, "with a cube in the hand over a phrase (drawing) the tag stands on it",
             "hover tile " + (PathManager.I.HoverTile != null ? PathManager.I.HoverTile.name : "none") + "; " + pick);
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        PathManager.I.PutDown();
        LoadFixture(); yield return Frames(2);
    }

    // ------------------------------------------------------------------ 8. the v7 tips (each once) — SPEC v7 §7.5, §13.4, §17.1, §19.1
    static IEnumerator Tips()
    {
        var sm = SongManager.I;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        PlayerPrefs.SetInt(Onboarding.PrefDone, 1);
        foreach (var id in Onboarding.AllTips) PlayerPrefs.SetInt(Onboarding.PrefTip + id, 1);
        foreach (var id in Onboarding.V7Tips) PlayerPrefs.DeleteKey(Onboarding.PrefTip + id);
        LoadFixture(); yield return Frames(2);
        Onboarding.Suppressed = false;
        Onboarding.ClearTips();
        yield return null;
        var got = new List<string>();
        // sections — a gesture that fills a section (the deck places measures after the last one)
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.05f, true);
        yield return TipOnce(Onboarding.TipSections, Onboarding.SectionsText, () =>
        {
            var tray = IslandTray.Ensure(); int lastS = sm.SectionCount - 1;
            for (int k = sm.SectionBars(lastS); k < ProjectConfig.SectionBars; k++) { UIManager.I.SelectMeasure(sm.Islands.Count - 1, false); tray.RebuildNow(); tray.SimClick(0); }
        }, "u1_7_tip_sections.png", got, null, () => Onboarding.Notify(Onboarding.Ev.SectionFilled));
        LoadFixture(); yield return Frames(2);
        // stairs (the card) — then the heights it caused (queued behind it)
        UIManager.I.SelectMeasure(1, false);
        yield return TipOnce(Onboarding.TipStairs, Onboarding.StairsText, () => IslandTray.Ensure().SimTool(5), "u1_7_tip_stairs.png", got, null, () => Onboarding.Notify(Onboarding.Ev.StairsAdded));
        LoadFixture(); yield return Frames(2);   // a flat song again: the next stairs lifts / lowers the ground for the first time
        yield return TipOnce(Onboarding.TipHeights, Onboarding.HeightsText, () => SongOps.AddStairIsland(1, false), null, got, null, () => SongOps.AddStairIsland(3, false));
        LoadFixture(); yield return Frames(2);
        yield return TipOnce(Onboarding.TipMelody, Onboarding.MelodyText, () => IslandTray.Ensure().SimTool(6), null, got, () => PathManager.I.PutDown(), () => Onboarding.Notify(Onboarding.Ev.PhraseAdded));
        PathManager.I.PutDown();
        LoadFixture(); yield return Frames(2);
        yield return TipOnce(Onboarding.TipEchoes, Onboarding.EchoesText, () => Onboarding.Notify(Onboarding.Ev.OctaveCopied), null, got);
        yield return TipOnce(Onboarding.TipHarmony, Onboarding.HarmonyText, () => Onboarding.Notify(Onboarding.Ev.HarmonyAdded), null, got);
        // the header's rewind / vary, flow, launch
        yield return ShowHeader(sm.Islands[0]);
        IslandHeader.I.Repeat.onClick(); yield return Frames(3);
        yield return TipOnce(Onboarding.TipRewind, Onboarding.RewindText, () => IslandHeader.I.Style.onClick(), null, got);
        yield return ShowHeader(sm.Islands[0]);
        yield return TipOnce(Onboarding.TipFlow, Onboarding.FlowText, () => IslandHeader.I.FillSection.onClick(), null, got);   // §21: the fill-the-section tab
        yield return ShowHeader(sm.Islands[0]);
        yield return TipOnce(Onboarding.TipLaunch, Onboarding.LaunchText, () => IslandHeader.I.Launch.onClick(), null, got);
        IslandHeader.Hide();
        LoadFixture(); yield return Frames(2);
        yield return TipOnce(Onboarding.TipFlip, Onboarding.FlipText, () => Onboarding.Notify(Onboarding.Ev.CubeFlipped), null, got);
        yield return TipOnce(Onboarding.TipSelect, Onboarding.SelectText, () => GridSelection.Set(new[] { sm.Islands[0], sm.Islands[1] }), null, got, () => GridSelection.Clear());
        GridSelection.Clear();
        yield return TipOnce(Onboarding.TipShortcuts, Onboarding.ShortcutsText, () => Onboarding.Notify(Onboarding.Ev.CubeInspected), null, got);
        yield return TipOnce(Onboarding.TipNames, Onboarding.NamesText, () => { SectionHeader.Show(0); }, null, got, () => SectionHeader.Hide());
        SectionHeader.Hide();
        yield return TipOnce(Onboarding.TipCapacity, Onboarding.CapacityText, () => Onboarding.Notify(Onboarding.Ev.CapacityReached), null, got);
        yield return TipOnce(Onboarding.TipKeys, Onboarding.KeysText, () => Onboarding.Notify(Onboarding.Ev.KeysExtended), null, got);
        Onboarding.Suppressed = true;
        Onboarding.ClearTips();
        LoadFixture(); yield return null;
        Line(got.Count == Onboarding.V7Tips.Length && !got.Exists(s => s.Contains("✗")), "the " + Onboarding.V7Tips.Length + " v7 tips (sections, stairs, heights, melody, echoes, harmony, rewind, flow, launch, flip, select, shortcuts, names, capacity, keys) each show their words once, then never again",
             string.Join("; ", got.ToArray()));
    }

    /// <summary>A tip: <paramref name="trigger"/> shows it (its words, the bubble visible); skipped; <paramref name="again"/> (default: the trigger)
    /// never shows it a second time.</summary>
    static IEnumerator TipOnce(string id, string text, Action trigger, string shot, List<string> got, Action after = null, Action again = null)
    {
        Onboarding.ClearTips();
        yield return WaitFor(() => Onboarding.ActiveTip == null, 3f);
        int ev0 = events.Count;
        trigger();
        yield return WaitFor(() => Onboarding.ActiveTip == id, 2f);
        string at = Onboarding.ActiveTip;
        yield return Wait(0.4f);
        var bub = Onboarding.Bubble;
        bool shown = Onboarding.ActiveTip == id && bub != null && bub.Visible && bub.Text == text.ToLowerInvariant();
        string saw = (bub != null ? bub.Text.Replace('\n', ' ') : "") + " | active then " + at + ", now " + Onboarding.ActiveTip + ", visible " + (bub != null && bub.Visible) + ", pending " + Onboarding.PendingTips
                     + ", events " + string.Join(",", events.GetRange(ev0, events.Count - ev0).ToArray());
        if (shot != null) yield return Shot(shot);
        if (after != null) after();
        Onboarding.ClearTips();   // the tip AND any tip queued behind it (a queued one would show the next frame)
        yield return Frames(2);
        (again ?? trigger)();
        yield return Wait(0.5f);
        bool once = Onboarding.ActiveTip != id && PlayerPrefs.GetInt(Onboarding.PrefTip + id, 0) == 1;
        if (after != null) after();
        Onboarding.ClearTips();
        got.Add(id + (shown && once ? "" : " ✗ (shown " + shown + ", once " + once + ", active " + Onboarding.ActiveTip + ", \"" + saw + "\")"));
    }
}
