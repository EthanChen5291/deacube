using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// T: the song's timeline (HudColumnRail) after the owner's two verdicts — "this timeline thing is just not readable at all", then, on a
/// richer redesign, "way too complicate again … BE SIMPLE AND MINIMALISTIC". The rail is one row of pills, in every song size: a pill per
/// section (its letter's colour, its letter), as wide as the time it plays; in a small song a pill shows its measures as flat chord-coloured
/// cells; the pill holding the song position is at full strength and a progress bar under it fills with the song; a click goes there. Nothing else.
/// <c>RailChecks.RunAll(captures)</c> starts a coroutine; poll <c>RailChecks.Done</c> or Captures/rail_report.txt (numbered PASS / FAIL lines,
/// INFO lines, a SUMMARY). The user's saves are snapshotted and restored.
/// a. a fresh 4-chord song: one pill, its four measures as cells in the chords' colours; nothing else on the rail; one hit target;
/// b. the gallery song "get proto": a pill per section, lettered and coloured as its plinth, widths ∝ the measures played (within the floor),
///    all inside the rail's width, solid (no cells), no two shapes overlapping; a 40-section song scrolls;
/// c. where you are: the pill holding the song position is the current one while playing (three seeks), paused and stopped; the progress bar's end is in it;
/// d. a click on a pill / a cell goes there (grid picked, camera framed, song position moved; Play starts there; a playing song plays on);
///    a right-click only moves the song position;
/// e. while playing across a section boundary the progress bar's end never goes back and lands at the next pill's start; the pills do not move;
/// f. the controls that stay: the loop curl toggles the song loop; a focus loop wears its badge on the row and its ✕ ends it;
/// g. the rail repaints only on change, its Update allocates nothing, no console errors.
/// Captures (with <c>captures</c>): rail_4chords.png, rail_proto_start.png, rail_proto_mid.png.
/// </summary>
public static class RailChecks
{
    public const string ProtoId = "get-proto";
    public static string Report = "";
    public static bool Done = true;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "rail_report.txt");
    static int num;
    static readonly List<string> errors = new List<string>();
    static StringBuilder sb;
    static SongManager SM => SongManager.I;
    static HudColumnRail Rail => UIManager.I != null ? UIManager.I.Rail : null;

    /// <summary>The new rule, read off the rail's objects (not its own counters): a pill per section and a letter per pill shown, and no other
    /// object under the rail than its line, pills (a letter each), progress bar (a fill), loop curl and focus badge. Null when it holds, else what is wrong (the
    /// older suites call this where they used to count beads, badges, drum lines …).</summary>
    public static string PillsOnly(HudColumnRail rail)
    {
        if (rail == null || rail.Root == null) return "no rail";
        var sm = SongManager.I;
        int sections = sm != null && sm.HasSong ? sm.SectionCount : 0;
        if (rail.CountNow() != sections) return "pills " + rail.PillsShown + " ≠ sections " + sections;
        int visible = 0; for (int s = 0; s < rail.PillsShown; s++) if (rail.PillVisible(s)) visible++;
        var allowed = new HashSet<string> { "Line", "Beads", "LoopCurl", "Playhead", "FocusLoop" };
        foreach (Transform t in rail.Root) if (!allowed.Contains(t.name)) return "an extra object on the rail: " + t.name;
        int pillObjects = 0;
        var beads = rail.Root.Find("Beads"); var bar = rail.Root.Find("Playhead");
        if (beads == null || bar == null) return "the rail's parts are missing";
        foreach (Transform t in beads)
        {
            if (!t.gameObject.activeSelf) continue;
            if (!t.name.StartsWith("Pill")) return "not a pill among the pills: " + t.name;
            if (t.childCount != 1 || t.GetChild(0).name != "Letter") return "a pill carries more than its letter: " + t.name + " (" + t.childCount + " children)";
            pillObjects++;
        }
        if (pillObjects != visible) return "pill objects " + pillObjects + " ≠ pills shown " + visible;
        if (bar.childCount != 1 || bar.GetChild(0).name != "Fill") return "the progress bar carries more than its fill";
        return null;
    }

    static void Line(bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" T-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(); }
    static void Flush() { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string where = (stack ?? "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        string line = msg + " @ " + where;
        if (errors.Count < 20) errors.Add(line.Length > 320 ? line.Substring(0, 320) : line);
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }
    static string F(float v) => v.ToString("F1");
    static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        sb = new StringBuilder();
        SequenceMaster.I.StartCoroutine(Guarded(captures));
        return "started";
    }

    static IEnumerator Guarded(bool captures)
    {
        sb.Append("RailChecks ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        bool loop0 = GlobalClock.LoopSong;
        var body = Routine(captures);
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { FocusLoop.Release(); GlobalClock.ClearRegion(); GlobalClock.Stop(); GlobalClock.LoopSong = loop0; } catch (Exception) { }
        try { if (MainMenu.IsShown) MainMenu.Hide(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog;
        Line(errors.Count == 0, "g. no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush();
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) PathManager.I.PutDown();
        FocusLoop.Dismiss(); GlobalClock.ClearRegion(); GlobalClock.Stop();
        if (UIManager.I != null) UIManager.I.ClearSelection();
        Application.runInBackground = true;
    }

    static IEnumerator Routine(bool captures)
    {
        Prepare();
        yield return null;
        if (Rail == null) { Line(false, "the HUD has a column rail", "UIManager.Rail is null"); yield break; }
        yield return Small(captures);
        yield return Proto(captures);
        yield return Where(captures);
        yield return Clicks();
        yield return Boundary();
        yield return Controls();
        yield return Quiet();
        yield return Scrolling();
        // leave a small song behind
        Prepare();
        SM.StartFreshSong(MusicTheory.RandomSong(707));
        yield return Wait(0.6f);
        IslandTray.Ensure().SimTool(2);
        yield return Wait(1.0f);
    }

    // ------------------------------------------------------------------ a. a small song
    static IEnumerator Small(bool captures)
    {
        SM.StartFreshSong(MusicTheory.RandomSong(707));
        yield return Wait(0.6f);
        IslandTray.Ensure().SimTool(2);   // the wand: the whole deck (four chords)
        yield return Wait(1.6f);
        var rail = Rail; var sm = SM;
        rail.CountNow();
        yield return Frames(3);
        float bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        int ns = sm.SectionCount, measures = Mathf.RoundToInt(GlobalClock.TotalBeats / bpb);
        string only = PillsOnly(rail);
        bool one = ns == 1 && rail.PillsShown == 1 && rail.PillLetter(0) == SectionHeader.LetterOf(0) && rail.PillLetter(0).Length > 0 && Same(rail.PillColour(0), SectionPlinth.LetterTint(rail.PillLetter(0)))
                   && rail.PillMeasures(0) == measures && Mathf.Abs(rail.PillWidth(0) - measures * HudColumnRail.MeasureMax) < 0.01f && Mathf.Abs(rail.PillX(0) + rail.PillWidth(0) * 0.5f) < 0.01f;
        Line(only == null && one, "a. a 4-chord song: ONE pill — its section's letter and colour, " + HudColumnRail.MeasureMax + " px a measure, centred — and nothing else on the rail",
             only ?? "sections " + ns + ", pills " + rail.PillsShown + " \"" + rail.PillLetter(0) + "\", " + measures + " measures, width " + F(rail.PillWidth(0)) + " at x " + F(rail.PillX(0)));
        // its measures as cells: each in its column's chord colour, in order, end to end inside the pill
        bool cells = rail.CellsShown && rail.CellCount(0) == measures && rail.CellWidth(0) >= HudColumnRail.CellMin; var cols = new List<string>();
        for (int k = 0; k < rail.CellCount(0); k++)
        {
            int c = rail.CellColumn(0, k); var a = sm.AnchorOf(c);
            cells &= c == sm.ActiveColumn(k * bpb + 0.01f) && a != null && Same(rail.CellColour(0, k), a.chordColor)
                     && rail.CellX(0, k) >= rail.PillX(0) && rail.CellX(0, k) + rail.CellWidth(0) <= rail.PillX(0) + rail.PillWidth(0) + 0.01f;
            cols.Add(c + (a != null ? ":" + a.assignedChord : ""));
        }
        Line(cells, "a. the pill shows its measures as plain cells (a measure has " + F(rail.CellWidth(0)) + " px ≥ " + HudColumnRail.CellMin + "): one per measure, in its chord's colour, in song order",
             rail.CellCount(0) + " cells: " + string.Join(" ", cols.ToArray()));
        var names = new List<string>();
        int targets = V4ChecksU1.CountTargets(names);
        Line(names.Count(s => s == "Rail(beads)") == 1 && !names.Any(s => s.StartsWith("Pill")) && names.Contains("LoopCurl"), "a. the hit targets at rest count the rail's pills as one control, and its loop curl (V4ChecksU1.CountTargets)", targets + ": " + string.Join(", ", names.ToArray()));
        Info("a. the rail draws " + rail.GlyphCount + " shapes for this song (1 pill, " + rail.CellCount(0) + " cells, the progress bar, the loop curl)");
        // a click on a cell goes to that measure's column
        int cell = Mathf.Min(2, rail.CellCount(0) - 1), col = rail.CellColumn(0, cell);
        UIManager.I.ClearSelection(); GlobalClock.Stop(); GlobalClock.Seek(0);
        yield return Frames(2);
        rail.Click(0, cell);
        yield return Frames(3);
        int island = UIManager.I.SelectedMeasure;
        bool cellOk = island >= 0 && sm.Islands[island].column == col && Mathf.Abs(GlobalClock.SongBeat - sm.ColumnStart(col)) < 0.01f && !GlobalClock.IsPlaying
                      && rail.PlayheadX >= rail.CellX(0, cell) - 0.5f && rail.PlayheadX <= rail.CellX(0, cell) + rail.CellWidth(0);
        int played0 = GlobalClock.PlayedFromGrid;
        GlobalClock.Toggle();
        yield return Wait(0.25f);
        bool playOk = GlobalClock.IsPlaying && GlobalClock.SongBeat >= sm.ColumnStart(col) - 0.01f && GlobalClock.SongBeat < sm.ColumnStart(col) + 3f;
        if (captures) yield return Shot("rail_4chords.png");
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
        Line(cellOk && playOk, "a. a click on a cell goes to its measure: its column's grid picked, the progress bar's end on that cell, and Play starts there",
             "cell " + cell + " → column " + col + ": picked island " + island + ", beat " + sm.ColumnStart(col).ToString("F1") + ", needle on the cell " + cellOk + ", then playing from it " + playOk + " (plays from a picked grid +" + (GlobalClock.PlayedFromGrid - played0) + ")");
    }

    // ------------------------------------------------------------------ b. a long song
    static IEnumerator OpenProto()
    {
        Prepare();
        Gallery.ClearTestSongs();
        Gallery.Reload();
        bool ok = Gallery.Open(ProtoId, false);
        yield return Wait(1.2f);   // (the plinths letter their sections; the rail follows on twos)
        if (!ok) Line(false, "Gallery.Open(get-proto)", "refused");
        GlobalClock.Stop(); GlobalClock.Seek(0); SequenceMaster.ResetAllCubes();
        UIManager.I.ClearSelection();
        Rail.CountNow();
        yield return Frames(4);
    }

    static IEnumerator Proto(bool captures)
    {
        yield return OpenProto();
        var rail = Rail; var sm = SM;
        int ns = sm.SectionCount;
        float bpb = Mathf.Max(1, GlobalClock.BeatsPerBar), half = HudColumnRail.MaxWidth * 0.5f;
        bool letters = rail.PillsShown == ns; var shown = new List<string>();
        for (int s = 0; s < ns; s++)
        {
            shown.Add(rail.PillLetter(s));
            letters &= rail.PillLetter(s) == SectionHeader.LetterOf(s) && rail.PillLetter(s).Length > 0 && rail.PillVisible(s) && rail.PillFirst(s) == sm.SectionFirst(s) && Same(rail.PillColour(s), SectionPlinth.LetterTint(rail.PillLetter(s)));
        }
        string only = PillsOnly(rail);
        Line(letters && only == null && !rail.CellsShown, "b. get proto: one pill per section in its plinth's letter and colour — solid pills (no room for cells) and nothing else on the rail",
             only ?? ns + " sections, pills " + rail.PillsShown + ": " + string.Join(" ", shown.ToArray()) + "; islands " + sm.Islands.Count + ", Moons " + sm.Moons.Count);
        // widths ∝ the measures each section plays, within the floor
        float ppmMin = float.MaxValue, ppmMax = 0f; int floored = 0, measures = 0; bool widthOk = true, playedOk = true;
        for (int s = 0; s < ns; s++)
        {
            int played = Mathf.RoundToInt((sm.SectionEndBeat(s) - sm.SectionStartBeat(s)) / bpb);
            playedOk &= rail.PillMeasures(s) == played; measures += played;
            float w = rail.PillWidth(s);
            if (w <= HudColumnRail.PillMinW + 0.01f) { floored++; widthOk &= Mathf.Abs(w - HudColumnRail.PillMinW) < 0.01f; continue; }
            float ppm = w / played; ppmMin = Mathf.Min(ppmMin, ppm); ppmMax = Mathf.Max(ppmMax, ppm);
        }
        for (int s = 0; s < ns; s++) if (rail.PillWidth(s) <= HudColumnRail.PillMinW + 0.01f) widthOk &= rail.PillMeasures(s) * ppmMin <= HudColumnRail.PillMinW + 0.01f;   // (floored = narrower than the floor at the others' scale)
        Line(playedOk && widthOk && ppmMax - ppmMin < 0.01f, "b. a pill's width follows the measures its section plays (measures × passes), with a floor so its letter fits",
             measures + " measures, " + F(ppmMin) + " px a measure (spread " + (ppmMax - ppmMin).ToString("F3") + "), " + floored + " pills at the floor of " + HudColumnRail.PillMinW + " px");
        // inside the rail's width, in song order, a gap apart; each letter on its pill; no two shapes overlap
        bool inside = true, apart = true, lettersOn = true; float left = float.MaxValue, right = float.MinValue; var hits = new List<string>();
        var rects = new List<Rect>(); var names = new List<string>();
        for (int s = 0; s < ns; s++)
        {
            float x0 = rail.PillX(s), x1 = x0 + rail.PillWidth(s);
            left = Mathf.Min(left, x0); right = Mathf.Max(right, x1);
            inside &= x0 >= -half - 0.01f && x1 <= half + 0.01f;
            if (s > 0) apart &= Mathf.Abs(x0 - (rail.PillX(s - 1) + rail.PillWidth(s - 1)) - HudColumnRail.PillGap) < 0.01f;
            var pill = rail.PillOf(s); var lt = rail.LetterOf(s);
            lettersOn &= pill != null && lt != null && lt.parent == pill && lt.gameObject.activeInHierarchy && Mathf.Abs(lt.anchoredPosition.x) < 0.01f && Mathf.Abs(lt.anchoredPosition.y) < 2f;
            if (pill != null) { rects.Add(new Rect(pill.anchoredPosition.x - pill.sizeDelta.x * 0.5f, pill.anchoredPosition.y - pill.sizeDelta.y * 0.5f, pill.sizeDelta.x, pill.sizeDelta.y)); names.Add("pill " + s); }
        }
        for (int i = 0; i < rects.Count; i++) for (int j = i + 1; j < rects.Count; j++) if (rects[i].Overlaps(rects[j]) && hits.Count < 6) hits.Add(names[i] + " × " + names[j]);
        Line(inside && apart && lettersOn && hits.Count == 0 && rail.ScrollMax <= 0f, "b. every pill inside the rail's width, in song order, " + HudColumnRail.PillGap + " px apart, its letter centred on it; no two pills overlap",
             hits.Count > 0 ? string.Join("; ", hits.ToArray()) : "pills " + F(left) + " … " + F(right) + " (±" + half + "), all fit: no scroll");
        Info("b. the rail draws " + rail.GlyphCount + " shapes for get proto (" + ns + " pills, the progress bar, the loop curl); the old rail drew a bead for each of its " + sm.Islands.Count + " islands and "
             + sm.Moons.Count + " Moons, their badges, belts, arrows and bars, " + sm.Moons.Count + " drum lines, " + ns + " brackets and " + Enumerable.Range(0, ns).Sum(k => sm.SectionBars(k)) + " measure numbers");
        if (captures) yield return Shot("rail_proto_start.png");
    }

    // ------------------------------------------------------------------ c. where you are
    /// <summary>The progress bar sits just under pill <paramref name="s"/>, as wide as it, and its fill ends inside the pill's span.</summary>
    static bool BarIn(HudColumnRail rail, int s)
    {
        var bar = rail.Bar; var pill = rail.PillOf(s);
        if (bar == null || pill == null || !bar.gameObject.activeSelf || rail.BarFill == null) return false;
        float x0 = rail.PillX(s) + HudColumnRail.CellInset, w = rail.PillWidth(s) - 2f * HudColumnRail.CellInset;
        return Mathf.Abs(bar.anchoredPosition.x - x0) < 0.01f && Mathf.Abs(bar.sizeDelta.x - w) < 0.01f && bar.anchoredPosition.y + bar.sizeDelta.y * 0.5f <= pill.anchoredPosition.y - pill.sizeDelta.y * 0.5f
               && rail.PlayheadX >= x0 - 0.01f && rail.PlayheadX <= x0 + w + 0.01f && rail.BarFill.sizeDelta.x <= w;
    }

    static IEnumerator Where(bool captures)
    {
        var rail = Rail; var sm = SM;
        int ns = sm.SectionCount;
        bool atStart = rail.CurrentSection == 0 && BarIn(rail, 0);
        int[] into = { 2, Mathf.Min(7, ns - 1), Mathf.Min(11, ns - 1) };
        var got = new List<string>(); bool ok = true;
        foreach (int s in into)
        {
            GlobalClock.Seek(sm.SectionStartBeat(s) + 1.5f); SequenceMaster.ResetAllCubes();
            if (!GlobalClock.IsPlaying) GlobalClock.Play();
            yield return Wait(0.5f);
            ok &= GlobalClock.IsPlaying && rail.CurrentSection == s && BarIn(rail, s);
            got.Add(s + " → current " + rail.CurrentSection + ", bar end x " + F(rail.PlayheadX) + " in " + F(rail.PillX(s)) + " … " + F(rail.PillX(s) + rail.PillWidth(s)));
        }
        Line(atStart && ok, "c. where you are: the pill holding the song position is the current one (the only one at full strength) and the progress bar's end is inside it — at the start, then playing in three sections",
             "start: current " + (atStart ? "0" : "?") + "; " + string.Join("; ", got.ToArray()));
        int mid = Mathf.Min(9, ns - 1);
        GlobalClock.Seek(sm.SectionStartBeat(mid) + (sm.SectionEndBeat(mid) - sm.SectionStartBeat(mid)) * 0.4f); SequenceMaster.ResetAllCubes();
        yield return Wait(0.6f);
        if (captures) yield return Shot("rail_proto_mid.png");
        GlobalClock.Pause();
        yield return Wait(0.3f);
        int pausedSec = sm.SectionOf(sm.ActiveColumn(GlobalClock.SongBeat));
        bool pauseOk = rail.CurrentSection == pausedSec && BarIn(rail, pausedSec);
        // picking a grid elsewhere does not move the song position: the rail stays where the song is
        int other = sm.Islands.IndexOf(sm.AnchorOf(sm.SectionFirst(1)));
        UIManager.I.SelectMeasure(other, false);
        yield return Wait(0.3f);
        bool pickOk = rail.CurrentSection == pausedSec;
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
        yield return Wait(0.3f);
        Line(pauseOk && pickOk && rail.CurrentSection == 0 && BarIn(rail, 0), "c. paused: the current pill and the progress bar's end stay where the song is (a grid picked elsewhere does not move them); stopped: back on the first pill",
             "paused in " + pausedSec + " " + pauseOk + ", after a pick elsewhere " + pickOk + ", stopped: current " + rail.CurrentSection);
    }

    // ------------------------------------------------------------------ d. clicks
    static IEnumerator Clicks()
    {
        var rail = Rail; var sm = SM; var oc = OrbitCamera.I;
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
        yield return Frames(3);
        int s = Mathf.Min(6, sm.SectionCount - 1);
        Vector3 before = oc != null ? oc.FocusTarget : Vector3.zero;
        var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        rail.PillButton(s).OnPointerClick(ev);   // (the control's own click path)
        yield return Wait(0.8f);
        Bounds b; bool hasB = SectionHeader.SectionBounds(s, out b);
        int first = sm.Islands.IndexOf(sm.AnchorOf(sm.SectionFirst(s)));
        Vector3 after = oc != null ? oc.FocusTarget : Vector3.zero;
        bool framed = oc != null && hasB && after.x >= b.min.x - 0.5f && after.x <= b.max.x + 0.5f && (after - before).magnitude > 1f;
        bool there = Mathf.Abs(GlobalClock.SongBeat - sm.SectionStartBeat(s)) < 0.01f && rail.CurrentSection == s && BarIn(rail, s) && !GlobalClock.IsPlaying;
        Line(UIManager.I.SelectedMeasure == first && framed && there, "d. a click on a pill goes there: its section's first grid picked, the section framed, the song position (the progress bar's end, the current pill) on it",
             "section " + s + ": picked island " + UIManager.I.SelectedMeasure + " = " + first + ", camera x " + F(before.x) + " → " + F(after.x) + " in [" + F(b.min.x) + ", " + F(b.max.x) + "], beat " + GlobalClock.SongBeat.ToString("F1") + " = " + sm.SectionStartBeat(s).ToString("F1") + ", current " + rail.CurrentSection);
        GlobalClock.Toggle();   // the play button
        yield return Wait(0.25f);
        float beat = GlobalClock.SongBeat, want = sm.SectionStartBeat(s);
        bool playOk = GlobalClock.IsPlaying && beat >= want - 0.01f && beat < want + 3f;
        // a click while it plays: it plays on from there
        int s2 = Mathf.Min(3, sm.SectionCount - 1);
        rail.PillButton(s2).OnPointerClick(ev);
        yield return Wait(0.3f);
        float beat2 = GlobalClock.SongBeat, want2 = sm.SectionStartBeat(s2);
        bool onOk = GlobalClock.IsPlaying && beat2 >= want2 - 0.01f && beat2 < want2 + 3f && rail.CurrentSection == s2;
        GlobalClock.Pause();
        Line(playOk && onOk, "d. Play then starts there; a click on another pill while it plays: the song plays on from that one",
             "playing from " + beat.ToString("F2") + " (section " + s + " starts at " + want.ToString("F1") + "); then section " + s2 + ": beat " + beat2.ToString("F2") + " from " + want2.ToString("F1") + ", still playing " + onOk);
        int r = Mathf.Min(10, sm.SectionCount - 1);
        var rev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right };
        yield return Wait(0.9f);   // (the camera was following the song: let it come to rest)
        int sel = UIManager.I.SelectedMeasure; Vector3 cam0 = oc != null ? oc.FocusTarget : Vector3.zero;
        rail.PillButton(r).OnPointerClick(rev);
        yield return Frames(3);
        Line(Mathf.Abs(GlobalClock.SongBeat - sm.SectionStartBeat(r)) < 0.01f && UIManager.I.SelectedMeasure == sel && rail.CurrentSection == r && (oc == null || (oc.FocusTarget - cam0).magnitude < 0.05f),
             "d. a right-click on a pill only moves the song position there (nothing picked, the camera stays)",
             "beat " + GlobalClock.SongBeat.ToString("F1") + " = " + sm.SectionStartBeat(r).ToString("F1") + ", selection " + UIManager.I.SelectedMeasure + " = " + sel + ", current " + rail.CurrentSection);
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
    }

    // ------------------------------------------------------------------ e. the progress bar's end
    static IEnumerator Boundary()
    {
        var rail = Rail; var sm = SM;
        int s = Mathf.Min(3, sm.SectionCount - 2);
        float boundary = sm.SectionEndBeat(s);
        GlobalClock.Seek(boundary - 2.5f); SequenceMaster.ResetAllCubes();
        GlobalClock.Play();
        yield return Frames(2);
        float lastX = rail.PlayheadX, x0 = lastX, worstBack = 0f, firstAfter = float.NaN, lastBefore = lastX;
        int frames = 0, before = rail.CurrentSection; bool sawBefore = false, sawAfter = false, pillsStill = true;
        float px0 = rail.PillX(s), px1 = rail.PillX(s + 1);
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeat < boundary + 2.5f && Time.realtimeSinceStartup - t0 < 6f)
        {
            yield return null;
            float x = rail.PlayheadX, beat = GlobalClock.SongBeat;
            worstBack = Mathf.Max(worstBack, lastX - x);
            if (rail.CurrentSection == s) { sawBefore = true; lastBefore = x; } else if (rail.CurrentSection == s + 1) { if (!sawAfter) firstAfter = x; sawAfter = true; }   // (by the rail's own frame: it may read the clock a frame before this coroutine)
            pillsStill &= Mathf.Abs(rail.PillX(s) - px0) < 0.001f && Mathf.Abs(rail.PillX(s + 1) - px1) < 0.001f;
            lastX = x; frames++;
        }
        bool moved = GlobalClock.SongBeat >= boundary + 2.4f;
        float x1 = rail.PlayheadX;
        GlobalClock.Pause();
        if (!moved) Info("e. the song did not advance past the boundary in 6 s (beat " + GlobalClock.SongBeat.ToString("F2") + " of " + (boundary + 2.5f).ToString("F2") + "): the audio clock stands still?");
        float endOfPill = rail.PillX(s) + rail.PillWidth(s), startOfNext = rail.PillX(s + 1);
        Line(moved && sawBefore && sawAfter && worstBack <= 0.001f && lastBefore <= endOfPill + 0.01f && firstAfter >= startOfNext - 0.01f && firstAfter < startOfNext + 12f && before == s && rail.CurrentSection == s + 1 && pillsStill,
             "e. playing across a section boundary: the progress bar's end never goes back — it leaves the pill at its end and is at the next pill's start; the current pill changes, no pill moves",
             frames + " frames over section " + s + " → " + (s + 1) + ": x " + F(x0) + " → " + F(x1) + ", last before the boundary " + F(lastBefore) + " (the pill ends at " + F(endOfPill) + "), first after " + F(firstAfter) + " (the next starts at " + F(startOfNext)
             + "), worst step back " + worstBack.ToString("F3") + " px, current " + before + " → " + rail.CurrentSection);
        GlobalClock.Stop(); GlobalClock.Seek(0);
    }

    // ------------------------------------------------------------------ f. the controls that stay
    static IEnumerator Controls()
    {
        var rail = Rail; var sm = SM;
        var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        bool loop0 = GlobalClock.LoopSong; int h0 = History.UndoCount;
        rail.LoopButton.OnPointerClick(ev);
        yield return Frames(2);
        bool flipped = GlobalClock.LoopSong != loop0 && History.UndoCount == h0 + 1;
        rail.LoopButton.OnPointerClick(ev);
        yield return Frames(2);
        var curl = rail.LoopButton.transform as RectTransform; int last = rail.PillsShown - 1;
        Line(flipped && GlobalClock.LoopSong == loop0 && curl.anchoredPosition.x > rail.PillX(last) + rail.PillWidth(last), "f. the loop curl after the last pill is the song-loop toggle (one History entry a click)",
             "loop " + loop0 + " → " + !loop0 + " → " + GlobalClock.LoopSong + ", curl at x " + F(curl.anchoredPosition.x) + " past " + F(rail.PillX(last) + rail.PillWidth(last)));
        // a focus loop (a column looped while a cube is edited): its ink loop sits around that column's place on the row; its ✕ ends it
        int col = sm.SectionFirst(Mathf.Min(4, sm.SectionCount - 1));
        FocusLoop.Begin(col);
        yield return Frames(4);
        var badge = rail.FocusBadge;
        float cx = rail.ColumnX(col) + rail.ColumnWidth(col) * 0.5f;
        bool shown = FocusLoop.Active && badge != null && badge.gameObject.activeSelf && Mathf.Abs(badge.anchoredPosition.x - cx) < 0.5f && badge.sizeDelta.x >= rail.ColumnWidth(col) && rail.FocusButton != null && rail.FocusButton.gameObject.activeInHierarchy;
        rail.FocusButton.OnPointerClick(ev);
        yield return Frames(4);
        Line(shown && !FocusLoop.Active && !badge.gameObject.activeSelf, "f. a focus loop wears its ink loop around its column's place on the row; the ✕ on it ends the loop",
             "column " + col + ": badge at x " + F(badge.anchoredPosition.x) + " = " + F(cx) + ", shown " + shown + ", after the ✕: loop " + FocusLoop.Active);
        GlobalClock.ClearRegion(); GlobalClock.Stop(); GlobalClock.Seek(0);
    }

    // ------------------------------------------------------------------ g. quiet
    static long UpdateBytes(HudColumnRail rail, int calls)
    {
        var m = typeof(HudColumnRail).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        if (m == null) return -1;
        var act = (Action)Delegate.CreateDelegate(typeof(Action), rail, m);
        for (int i = 0; i < 8; i++) act();
        long best = long.MaxValue;
        for (int trial = 0; trial < 3; trial++)
        {
            long a = GC.GetTotalMemory(false);
            for (int i = 0; i < calls; i++) act();
            long d = GC.GetTotalMemory(false) - a;
            if (d >= 0) best = Math.Min(best, d);
        }
        return best == long.MaxValue ? 0 : best;
    }

    static IEnumerator Quiet()
    {
        var rail = Rail; var sm = SM;
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
        yield return Wait(0.6f);
        int p0 = rail.PaintCount;
        yield return Frames(40);
        int rest = rail.PaintCount - p0;
        long bytesRest = UpdateBytes(rail, 2000);
        // playing inside one section (the progress bar's end moves in its own canvas)
        int s = 1;
        GlobalClock.Seek(sm.SectionStartBeat(s) + 1f); SequenceMaster.ResetAllCubes(); GlobalClock.Play();
        yield return Wait(0.6f);
        float xa = rail.PlayheadX; p0 = rail.PaintCount;
        yield return Frames(40);
        int playing = rail.PaintCount - p0; bool same = rail.CurrentSection == s && GlobalClock.IsPlaying;
        float xb = rail.PlayheadX;
        long bytesPlay = UpdateBytes(rail, 2000);
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Line(rest == 0 && playing == 0 && same && xb > xa, "g. the rail repaints only on change: not at rest, not while a section plays (the progress bar's end moves without it)",
             "paints over 40 frames: at rest " + rest + ", playing " + playing + " (bar end x " + F(xa) + " → " + F(xb) + ")");
        Line(bytesRest >= 0 && bytesRest < 4096 && bytesPlay < 4096, "g. the rail's Update allocates nothing (2000 calls each, GC.GetTotalMemory)", "at rest " + bytesRest + " B, playing " + bytesPlay + " B");
    }

    // ------------------------------------------------------------------ a song too long even for the floors: the row scrolls
    static MeasureState Chord(int col, int k) => new MeasureState { chordKey = k % 2 == 0 ? "Am7" : "Dm7", root = k % 2 == 0 ? 57 : 62, semis = new[] { 0, 3, 7, 10 }, bars = 4, kind = 0, col = col, repeat = 1, energy = 2 };

    static IEnumerator Scrolling()
    {
        Prepare();
        const int n = 40;
        var m = new List<MeasureState>(); for (int c = 0; c < n; c++) m.Add(Chord(c, c));
        var st = new SongState
        {
            version = SongState.CurrentVersion, name = "rail scroll", bpm = 120f, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = new CubeState[0],
            moons = new MeasureState[0], keyTonic = 9, keyMinor = true, tone = 1f, space = 1f
        };
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0); UIManager.I.ClearSelection();
        yield return Wait(1.0f);
        var rail = Rail; var sm = SM;
        rail.CountNow();
        yield return Frames(3);
        float half = HudColumnRail.MaxWidth * 0.5f;
        int ns = sm.SectionCount, vis0 = 0; for (int s = 0; s < ns; s++) if (rail.PillVisible(s)) vis0++;
        bool startOk = rail.PillsShown == ns && rail.ScrollMax > 1f && rail.ScrollX < 0.5f && rail.PillVisible(0) && !rail.PillVisible(ns - 1) && vis0 < ns && PillsOnly(rail) == null;
        // go far along: the row scrolls the current pill into view
        int target = ns - 6;
        rail.RightClick(0);   // (nothing to do with the scroll: the first pill, already in view)
        GlobalClock.Seek(sm.SectionStartBeat(target) + 1f);
        yield return Wait(1.2f);
        bool inView = rail.CurrentSection == target && rail.PillVisible(target) && rail.PillX(target) >= -half && rail.PillX(target) + rail.PillWidth(target) <= half && BarIn(rail, target);
        bool windowOk = true; int vis = 0;
        for (int s = 0; s < ns; s++)
        {
            float x0 = rail.PillX(s), x1 = x0 + rail.PillWidth(s);
            bool overlapsWindow = x1 > -half + 4f && x0 < half - 4f;
            if (rail.PillVisible(s)) { vis++; windowOk &= overlapsWindow && rail.PillWidth(s) >= HudColumnRail.PillMinW - 0.01f; }
            else windowOk &= !overlapsWindow && (rail.PillOf(s) == null || !rail.PillOf(s).gameObject.activeSelf);
        }
        string only = PillsOnly(rail);
        Line(startOk && inView && windowOk && rail.ScrollX > 1f && only == null,
             "b. a song whose pills do not fit even at the floor (" + ns + " sections): the row scrolls — the current pill in view, pills outside the window hidden, none thinner than the floor",
             only ?? "at the start " + vis0 + " of " + ns + " pills shown (scroll 0 of " + F(rail.ScrollMax) + "); the song at section " + target + ": scroll " + F(rail.ScrollX) + ", its pill at " + F(rail.PillX(target)) + ", " + vis + " shown");
        GlobalClock.Stop(); GlobalClock.Seek(0);
    }
}
