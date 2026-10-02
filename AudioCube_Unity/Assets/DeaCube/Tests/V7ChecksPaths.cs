using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// v7 (the user, 2026-10-01): GRID PATHS — "copy a certain grid's paths to a different chord progression … add the grid level edition and also make
/// it a button" (Clipboard.CopyPaths / PastePathsOn, the island header's copy-paths / paste-paths stickers, ⌘C / ⌘V over a grid's empty tiles) —
/// and the deck's PATTERN PREVIEW: "whenever you're hovering over possible cards, instead of the chord playing, play the last (live) cube grid
/// pattern (all the grids for that measure) in that chord, adapted; if no chord, the fallback" (CardPreview). Runs on V7ChecksB's test song (C
/// minor: Fm7 Bb7 Ebmaj7 Abmaj7 …, stairs on columns 1 and 3, a keyboard on 5, a melody roll on 0) with four cubes on the first chord grid.
/// Report: Captures/gp7_report.txt; capture gp7_header.png.
/// </summary>
public static class V7ChecksPaths
{
    public static string Report = "";
    public static bool Done = true;
    static int num, pushes;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "gp7_report.txt");
    static SongManager SM => SongManager.I;

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" GP-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }
    static void OnHistory() { pushes++; }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(Guarded(captures));
        return "started";
    }

    static IEnumerator Guarded(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("V7ChecksPaths ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog; History.OnChanged += OnHistory;
        var saves = V3Fixes.SnapshotSaves();
        var body = Routine(sb, captures);
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(sb, false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { Restore(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog; History.OnChanged -= OnHistory;
        Line(sb, errors.Count == 0, "no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);   // the real mouse is ignored
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) PathManager.I.PutDown();
    }

    static void Restore()
    {
        PathManager.SimOnly = false;
        IslandHeader.Hide(); IslandTray.Close(); GlobalClock.Stop();
        Clipboard.ClearPaths(); Clipboard.Clear();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
    }

    static KeyBlock ChordAt(int col) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == col);
    static KeyBlock KindAt(int kind) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == kind);

    static CubeState Cube(KeyBlock kb, int instrument, int[] xs, int[] zs, int[] durs, int layer = 0)
    {
        for (int i = 0; i < xs.Length; i++) { xs[i] = Mathf.Clamp(xs[i], 0, kb.cols - 1); zs[i] = Mathf.Clamp(zs[i], 0, kb.rows - 1); }
        return new CubeState { instrument = instrument, measure = SM.Islands.IndexOf(kb), xs = xs, zs = zs, rests = new bool[xs.Length], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f, layer = layer };
    }

    /// <summary>Loads the test song and puts four cubes on its first chord grid (Fm7): a keys arpeggio, a bass line, a pad, an octave-layer copy.</summary>
    static void LoadWithCubes()
    {
        V7ChecksB.LoadTest();
        var g0 = ChordAt(0);
        var pm = PathManager.I;
        pm.RestoreCube(Cube(g0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, new[] { 24, 24, 24, 24 }));
        pm.RestoreCube(Cube(g0, Clipboard.BassGroup, new[] { 0, 0 }, new[] { 0, 2 }, new[] { 48, 48 }));
        pm.RestoreCube(Cube(g0, 2, new[] { 1, 2 }, new[] { 3, 2 }, new[] { 48, 48 }));
        pm.RestoreCube(Cube(g0, 3, new[] { 0, 1 }, new[] { 1, 2 }, new[] { 48, 48 }, 1));
        SequenceMaster.RecalculateTimeline();
        History.Reset(); History.Push();
    }

    static string Sig(AudioCube c) => c.instrument + "/" + c.voice + "/L" + c.layer + "/" + string.Join(",", c.durs.Select(d => d.ToString()).ToArray());
    static int Pc(int m) => ((m % 12) + 12) % 12;

    static IEnumerator Routine(StringBuilder sb, bool captures)
    {
        Prepare();
        LoadWithCubes();
        yield return Frames(3);
        var g0 = ChordAt(0); var g1 = ChordAt(1);
        var src = Clipboard.PathsOf(g0);

        // ---- 1. copy
        Line(sb, Clipboard.PathCount(g0) == 4 && src.Count == 4, "a grid's paths are every finished cube on it (keys, bass, pad, an octave-layer copy)", "count " + Clipboard.PathCount(g0) + ", list " + src.Count);
        bool copied = Clipboard.CopyPaths(g0);
        Line(sb, copied && Clipboard.HasPaths && Clipboard.PathsCount == 4 && Clipboard.PathsLast && Clipboard.PathsSource == g0, "copy paths holds all four and ⌘V will paste them (the last copy)",
             "copied " + copied + ", count " + Clipboard.PathsCount + ", last " + Clipboard.PathsLast);
        var stairs = KindAt(3); var keys = KindAt(2); var phrase = KindAt(4);
        int fSelf = Clipboard.PathsFitOn(g0), fG1 = Clipboard.PathsFitOn(g1), fSt = Clipboard.PathsFitOn(stairs), fPh = Clipboard.PathsFitOn(phrase), fKeys = Clipboard.PathsFitOn(keys);
        Line(sb, fSelf == 0 && fG1 == 4 && fSt == 0 && fPh == 0 && fKeys == 4, "where they go: never back on their own grid; all four on another chord grid or a keyboard; none on stairs or a melody roll",
             "own " + fSelf + ", Bb7 " + fG1 + ", stairs " + fSt + ", melody roll " + fPh + ", keyboard " + fKeys);

        // ---- 2. paste onto Bb7: one History entry, the paste adaptation of each, chord tones, the bass on the root
        var want = Clipboard.PathsAdaptedFor(g1);
        int p0 = pushes, u0 = History.UndoCount;
        var made = Clipboard.PastePathsOn(g1);
        yield return Frames(2);
        var onG1 = Clipboard.PathsOf(g1);
        bool same = made.Count == 4 && onG1.Count == 4;
        for (int i = 0; same && i < 4; i++) if (Sig(made[i]) != Sig(src[i])) same = false;
        Line(sb, same && History.UndoCount == u0 + 1, "paste paths onto Bb7: four new cubes with the same sounds, lengths and layers in the same order — ONE History entry",
             "made " + made.Count + ", on Bb7 " + onG1.Count + ", undo +" + (History.UndoCount - u0) + ", pushes +" + (pushes - p0) + (made.Count > 0 ? ", first " + Sig(made[0]) + " vs " + Sig(src[0]) : ""));
        bool adapted = want.Count == 4 && made.Count == 4, tones = made.Count == 4; string why = "";
        var bb = new HashSet<int> { 10, 2, 5, 8 };   // Bb7: Bb D F Ab
        for (int i = 0; i < made.Count && i < want.Count; i++)
        {
            var c = made[i];
            for (int k = 0; k < c.nodes.Count; k++)
            {
                if (c.nodes[k].gridX != want[i].xs[k] || c.nodes[k].gridZ != want[i].zs[k]) { adapted = false; why = "cube " + i + " node " + k; }
                if (!bb.Contains(Pc(c.nodes[k].midi))) { tones = false; why += " pc " + Pc(c.nodes[k].midi); }
            }
        }
        var bass = made.FirstOrDefault(c => c.instrument == Clipboard.BassGroup);
        bool rootFirst = bass != null && Pc(bass.nodes[0].midi) == 10;
        Line(sb, adapted && tones && rootFirst, "each pasted path is the paste adaptation onto Bb7 (Clipboard.Adapt): every note a Bb7 chord tone, the bass line starting on Bb",
             "adapted " + adapted + ", chord tones " + tones + ", bass first pc " + (bass != null ? Pc(bass.nodes[0].midi) : -1) + (why.Length > 0 ? " (" + why + ")" : ""));
        History.Undo(); yield return Frames(3);
        int afterUndo = Clipboard.PathCount(ChordAt(1));
        History.Redo(); yield return Frames(3);
        int afterRedo = Clipboard.PathCount(ChordAt(1));
        Line(sb, afterUndo == 0 && afterRedo == 4, "undo takes all four away at once; redo brings them back", "after undo " + afterUndo + ", after redo " + afterRedo);

        // ---- 3. ⌘C over an empty tile copies the grid's paths; ⌘V over another grid pastes them; a cube copied later wins
        Clipboard.ClearPaths();
        g0 = ChordAt(0); var g2 = ChordAt(2); var g3 = ChordAt(3);
        var view = g0.WorldBounds; view.Encapsulate(g2.WorldBounds);
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(view, 0.1f, true);
        yield return Wait(1.0f);
        TileInteraction empty = null;
        var used = new HashSet<TileInteraction>();
        foreach (var c in Clipboard.PathsOf(g0)) foreach (var n in c.nodes) used.Add(n);
        foreach (var t in g0.tiles) if (t != null && !used.Contains(t)) { empty = t; break; }
        var cam = Camera.main;
        int ck0 = UIManager.PathsCopyKeyCount;
        if (empty != null && cam != null) { PathManager.SimPos = cam.WorldToScreenPoint(empty.Top); yield return Frames(4); }
        var hov = UIManager.HoveredGrid();
        bool keyCopy = empty != null && UIManager.I != null && UIManager.I.CopyKey();
        Line(sb, keyCopy && UIManager.PathsCopyKeyCount == ck0 + 1 && Clipboard.PathsSource == g0 && Clipboard.PathsCount == 4, "⌘C over an empty tile of a grid (no cube under the pointer) copies all its paths",
             "tile " + (empty != null ? empty.gridX + "," + empty.gridZ : "none") + ", hovered " + (hov != null ? hov.name : "none") + ", copied " + keyCopy + ", source " + (Clipboard.PathsSource == g0) + ", count " + Clipboard.PathsCount);
        if (cam != null && g2.tiles.Count > 0) { PathManager.SimPos = cam.WorldToScreenPoint(g2.tiles[0].Top); yield return Frames(4); }
        int before2 = Clipboard.PathCount(g2);
        var keyPaste = UIManager.I != null ? UIManager.I.PasteKey() : null;
        yield return Frames(2);
        Line(sb, keyPaste != null && Clipboard.PathCount(ChordAt(2)) == before2 + 4, "⌘V over another grid pastes all of them there (Ebmaj7)", "on Ebmaj7 " + before2 + " → " + Clipboard.PathCount(ChordAt(2)));
        var one = Clipboard.PathsOf(ChordAt(0)).First();
        Clipboard.Copy(one);
        bool lastIsCube = !Clipboard.PathsLast && Clipboard.HasPaths;
        g3 = ChordAt(3);
        var view3 = g3.WorldBounds; if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(view3, 0.1f, true);
        yield return Wait(0.8f);
        if (cam != null && g3.tiles.Count > 0) { PathManager.SimPos = cam.WorldToScreenPoint(g3.tiles[0].Top); yield return Frames(4); }
        int before3 = Clipboard.PathCount(g3);
        UIManager.I.PasteKey();
        yield return Frames(2);
        Line(sb, lastIsCube && Clipboard.PathCount(ChordAt(3)) == before3 + 1, "a cube copied after the paths: ⌘V pastes that one cube (the last copy wins)", "paths last " + Clipboard.PathsLast + ", on Abmaj7 " + before3 + " → " + Clipboard.PathCount(ChordAt(3)));
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);

        // ---- 4. the header's stickers
        var h = IslandHeader.I;
        if (h != null)
        {
            Clipboard.ClearPaths(); Clipboard.Clear();
            g0 = ChordAt(0);
            IslandHeader.Show(g0); yield return Wait(0.5f);
            bool copyOn = h.CopyPathsShown, pasteOffOnSource = !h.PastePathsShown;
            int cc0 = IslandHeader.CopyPathsCount;
            if (h.CopyPathsButton != null) h.CopyPathsButton.onClick();
            yield return Frames(3);
            bool copiedByBtn = IslandHeader.CopyPathsCount == cc0 + 1 && Clipboard.PathsSource == ChordAt(0) && Clipboard.PathsCount == 4;
            bool pasteStillOff = !h.PastePathsShown;   // the source grid takes none
            Line(sb, copyOn && pasteOffOnSource && copiedByBtn && pasteStillOff, "the header's copy-paths sticker shows on a grid with paths and copies them (the source grid shows no paste)",
                 "copy shown " + copyOn + ", copied " + copiedByBtn + ", paste on source " + !pasteStillOff);
            var g4 = ChordAt(4);
            IslandHeader.Show(g4); yield return Wait(0.5f);
            bool emptyNoCopy = !h.CopyPathsShown, pasteOn = h.PastePathsShown;
            var g1b = ChordAt(1);
            IslandHeader.Show(g1b); yield return Wait(0.5f);
            if (captures) yield return Shot("gp7_header.png");
            bool bothOnG1 = h.CopyPathsShown && h.PastePathsShown;
            IslandHeader.Show(g4); yield return Wait(0.4f);
            int u1 = History.UndoCount, before4 = Clipboard.PathCount(g4);
            if (h.PastePathsButton != null) h.PastePathsButton.onClick();
            yield return Frames(3);
            Line(sb, emptyNoCopy && pasteOn && bothOnG1 && Clipboard.PathCount(ChordAt(4)) == before4 + 4 && History.UndoCount == u1 + 1,
                 "on another grid the paste-paths sticker shows (an empty grid shows no copy) and pastes all four with one History entry",
                 "empty grid: copy " + !emptyNoCopy + ", paste " + pasteOn + "; a grid with paths shows both " + bothOnG1 + "; pasted " + before4 + " → " + Clipboard.PathCount(ChordAt(4)) + ", undo +" + (History.UndoCount - u1));
            IslandHeader.Hide();
        }
        else Line(sb, false, "the island header", "no IslandHeader");

        // ---- 5. the deck's pattern preview (CardPreview)
        Prepare();
        LoadWithCubes();
        yield return Frames(3);
        g0 = ChordAt(0);
        if (UIManager.I != null) UIManager.I.SelectMeasure(SM.Islands.IndexOf(g0), false);
        var dm7 = new SongManager.MeasureData { chordKey = "Dm7", chordRootMIDI = 62, semitones = new[] { 0, 3, 7, 10 }, bars = 1, kind = 0, measureDuration = 2f };
        var gm = new SongManager.MeasureData { chordKey = "Gm", chordRootMIDI = 55, semitones = new[] { 0, 3, 7 }, bars = 1, kind = 0, measureDuration = 2f };
        IslandTray.SetDeck(new[] { dm7, gm });
        IslandTray.Open(); yield return Wait(0.5f);
        var tray = IslandTray.I;
        int ci = -1;
        if (tray != null) { tray.RebuildNow(); yield return Frames(2); for (int i = 0; i < tray.CardCount; i++) { var d = tray.CardData(i); if (d != null && d.kind == 0 && d.chordKey == "Dm7") { ci = i; break; } } }
        GlobalClock.Stop();
        int pa0 = tray != null ? tray.PatternAuditionCount : 0, au0 = tray != null ? tray.AuditionCount : 0;
        if (tray != null && ci >= 0) tray.SimHover(ci);
        yield return Frames(2);
        int tr = SongManager.Transpose;
        var dPcs = new HashSet<int> { Pc(62 + tr), Pc(65 + tr), Pc(69 + tr), Pc(72 + tr) };
        bool allTones = CardPreview.LastNotes.Count > 0; int gridNotes = 0;
        for (int i = 0; i < CardPreview.LastNotes.Count; i++) if (CardPreview.LastKinds[i] == 0) { gridNotes++; if (!dPcs.Contains(Pc(CardPreview.LastNotes[i].y))) allTones = false; }
        Line(sb, ci >= 0 && tray.PatternAuditionCount == pa0 + 1 && tray.AuditionCount == au0 + 1 && CardPreview.LastColumn == g0.column && gridNotes >= 8 && allTones && CardPreview.Sounding,
             "hovering a chord card (stopped) plays the focused column's patterns — all its grids — adapted to the card's chord (Dm7: every grid note a Dm7 tone), not the chord's arpeggio",
             "card " + ci + ", pattern auditions +" + (tray != null ? tray.PatternAuditionCount - pa0 : -1) + ", column " + CardPreview.LastColumn + ", notes " + CardPreview.LastNotes.Count + " (grid " + gridNotes + "), chord tones " + allTones);
        GlobalClock.Play(); yield return Frames(3);
        bool cut = !CardPreview.Sounding;
        int q0 = tray != null ? tray.QuietHoverCount : 0, pa1 = tray != null ? tray.PatternAuditionCount : 0;
        if (tray != null && ci >= 0) tray.SimHover(ci);
        yield return Frames(2);
        bool quiet = tray != null && tray.QuietHoverCount == q0 + 1 && tray.PatternAuditionCount == pa1;
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Line(sb, cut && quiet, "the song starting cuts the preview; while it plays a hover stays quiet (a tick, as before)", "cut " + cut + ", quiet " + quiet);
        // the reference walks back to the nearest column with notes; no chord / no notes = the arpeggio fallback
        var g4c = ChordAt(4);
        if (UIManager.I != null && g4c != null) UIManager.I.SelectMeasure(SM.Islands.IndexOf(g4c), false);
        if (tray != null && ci >= 0) tray.SimHover(ci);
        yield return Frames(2);
        bool walkedBack = CardPreview.LastColumn == g0.column;
        int fb0 = CardPreview.Fallbacks;
        bool noChord = !CardPreview.Play(new SongManager.MeasureData { chordKey = "Keys", chordRootMIDI = 60, semitones = new[] { 0 }, bars = 1, kind = 2 }, 0);
        V7ChecksB.LoadTest(); yield return Frames(3);   // the song without notes
        IslandTray.SetDeck(new[] { dm7, gm });
        if (tray != null) { tray.RebuildNow(); yield return Frames(2); }
        int pa2 = tray != null ? tray.PatternAuditionCount : 0, au2 = tray != null ? tray.AuditionCount : 0;
        int ci2 = -1; if (tray != null) for (int i = 0; i < tray.CardCount; i++) { var d = tray.CardData(i); if (d != null && d.kind == 0) { ci2 = i; break; } }
        if (tray != null && ci2 >= 0) tray.SimHover(ci2);
        yield return Frames(2);
        bool arpeggio = tray != null && tray.AuditionCount == au2 + 1 && tray.PatternAuditionCount == pa2;
        Line(sb, walkedBack && noChord && arpeggio && CardPreview.Fallbacks >= fb0 + 2, "an empty focused column plays the nearest column before it with notes; a card without a chord or a song without notes keeps the chord's arpeggio",
             "walked back " + walkedBack + " (column " + CardPreview.LastColumn + "), keyboard card falls back " + noChord + ", empty song arpeggio " + arpeggio + ", fallbacks +" + (CardPreview.Fallbacks - fb0));
        IslandTray.Close();
        yield return Frames(2);
    }
}
