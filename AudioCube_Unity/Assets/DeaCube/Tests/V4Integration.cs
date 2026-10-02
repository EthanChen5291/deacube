using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v4 end-to-end run (integration, SPEC v4 §9): the menu → New (dice) → the comic exit → draw a hologram path with mixed note lengths (hover
/// audition, the focus loop plays it) → finish (solid, one History entry) → add an island below, duplicate right, lower an octave, drag within
/// the column → inspect → stretch a note on the rhythm strip → close, dismiss the loop → a recording → present (thrown cubes) → exit → Home
/// (autosave) → Continue (columns, registers and lengths survive). Asserts counts, timing and Synth late/errors at each stage, counts console
/// errors, writes captures i4_*.png. Poll Done / Report. Restores the saves and the tutorial prefs; never writes the user's save.
/// </summary>
public static class V4Integration
{
    public static bool Done;
    public static string Report = "";
    static StringBuilder sb;
    static int errors;
    static readonly List<string> errLines = new List<string>();
    static int pass, fail;
    static Dictionary<string, V3Fixes.FileSnap> saves;
    static readonly StringBuilder lateLog = new StringBuilder();
    static int lateMark;
    static int lateGrow;   // late events in the long-melody step: its island grows while the loop plays (a full rebuild; at most one ≤ 30 ms slip tolerated)
    static void Mark(string section) { int l = Synth.LateEvents; if (l != lateMark) lateLog.Append(section).Append(" +").Append(l - lateMark).Append(' '); if (section == "long-melody") lateGrow = l - lateMark; lateMark = l; }

    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);

    public static string Run(bool captures = true)
    {
        Done = false; Report = "";
        if (SequenceMaster.I == null) return "no SequenceMaster";
        SequenceMaster.I.StartCoroutine(Routine(captures));
        return "started";
    }

    static void OnLog(string msg, string stack, LogType t)
    {
        if (t != LogType.Error && t != LogType.Exception && t != LogType.Assert) return;
        errors++;
        if (errLines.Count < 16) errLines.Add(t + ": " + msg + (string.IsNullOrEmpty(stack) ? "" : " @ " + stack.Split('\n')[0]));
    }

    static void Check(bool ok, string what, string detail = "")
    {
        if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" | ").Append(detail);
        sb.Append('\n');
        Report = sb.ToString();
    }

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }

    static IEnumerator Until(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
    }

    static IEnumerator Shot(string file, bool captures)
    {
        if (!captures) yield break;
        ScreenCapture.CaptureScreenshot(Cap(file), 1);
        yield return null; yield return null;
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static bool OnScreen(Vector3 s) => s.z > 0f && s.x > 4f && s.y > 4f && s.x < Screen.width - 4f && s.y < Screen.height - 4f;

    static Vector3 ScreenOf(TileInteraction t) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(t.Top) : Vector3.zero; }

    static IEnumerator Click(TileInteraction t)
    {
        Vector3 s = ScreenOf(t);
        PathManager.I.SimPointer(s, true, true, false);
        yield return null;
        PathManager.I.SimPointer(s, false, false, true);
        yield return null;
    }

    static IEnumerator Hover(TileInteraction t, float seconds)
    {
        Vector3 s = ScreenOf(t);
        float e = 0f;
        while (e < seconds) { PathManager.I.SimPointer(s, false, false, false); e += Time.unscaledDeltaTime; yield return null; }
    }

    static string Durs(AudioCube c) { if (c == null) return "null"; var s = new StringBuilder(); foreach (int d in c.durs) s.Append(d).Append(' '); return s.ToString().Trim(); }

    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); errors = 0; errLines.Clear(); pass = 0; fail = 0;
        Application.logMessageReceived += OnLog;
        SongIO.QuitAutosave = false;
        saves = V3Fixes.SnapshotSaves();
        string userMd5 = Md5(SongIO.Path);
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, 0), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, 0);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        lateLog.Length = 0; lateMark = late0; lateGrow = 0;
        var sm = SongManager.I; var pm = PathManager.I;
        Onboarding.Suppressed = true;   // the tutorial has its own suite (V3ChecksD); its bubbles would sit on the tiles clicked here

        // ---- 1. menu → New → dice → the comic exit (no breaking apart) → the world
        if (!MainMenu.IsShown) MainMenu.Show();
        yield return Until(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return Wait(0.4f);
        yield return Shot("i4_01_menu.png", captures);
        Check(MainMenu.IsShown && MainMenu.NewButton != null, "the title menu is up (lowercase words)");
        MainMenu.NewButton.Invoke();
        yield return Until(() => InterfaceController.I != null && InterfaceController.I.Visible, 3f);
        yield return Wait(0.3f);
        if (InterfaceController.I != null && InterfaceController.I.DiceButton != null) InterfaceController.I.DiceButton.onClick?.Invoke();
        float tDice = Time.unscaledTime, tSong = -1f;
        yield return Until(() => sm.HasSong && MainMenu.State == MainMenu.Phase.Leaving, 8f);
        tSong = Time.unscaledTime;
        float drift = 0f;
        while (MainMenu.IsShown && Time.unscaledTime - tSong < 4f) { if (MainMenu.Stage != null) drift = Mathf.Max(drift, MainMenu.Stage.MaxPinDrift); yield return null; }
        float leave = Time.unscaledTime - tSong;
        Check(!MainMenu.IsShown && leave <= 1.5f, "the dice leave reaches the world in <= 1.5 s", "leave " + leave.ToString("F2") + " s (dice → song " + (tSong - tDice).ToString("F2") + " s)");
        Check(drift < 1e-3f, "the exit never breaks the title apart: no wall pin leaves its slot", "max pin drift " + drift.ToString("F4"));
        yield return Wait(0.8f);
        Check(sm.HasSong && sm.Islands.Count >= 1 && !WorldInput.WorldLocked, "a fresh song in the world, input free", sm.Islands.Count + " islands, " + WorldInput.Describe());

        // ---- 2. draft a path: hologram, focus loop, hover audition, lengths, solidify
        pm.SelectInstrument(3);
        yield return null;
        var isl = sm.Islands[0];
        var tiles = new[] { isl.GetTile(0, 0), isl.GetTile(1, 0), isl.GetTile(2, 1), isl.GetTile(3, 1) };
        bool visible = Camera.main != null; foreach (var t in tiles) if (t == null || !OnScreen(ScreenOf(t))) visible = false;
        if (!visible) { OrbitCamera.I.FrameIsland(isl, false); yield return Wait(0.9f); visible = true; foreach (var t in tiles) if (t == null || !OnScreen(ScreenOf(t))) visible = false; }
        if (!visible) { Check(false, "island 0's tiles on screen for the clicks — aborting the rest"); Finish(userMd5, prefStep, prefDone); yield break; }
        int cubes0 = SequenceMaster.Cubes.Count, hist0 = History.UndoCount;
        yield return Click(tiles[0]);
        yield return Wait(0.25f);
        var draft = pm.Draft;
        Check(draft != null && draft.Hologram && pm.IsDrawing, "the first click starts a HOLOGRAM draft", "draft " + (draft != null) + ", hologram " + (draft != null && draft.Hologram));
        Check(FocusLoop.Active && FocusLoop.Column == isl.column && GlobalClock.IsPlaying && GlobalClock.HasRegion, "the focus loop plays the draft's column",
              "active " + FocusLoop.Active + ", col " + FocusLoop.Column + ", playing " + GlobalClock.IsPlaying + ", region " + GlobalClock.RegionStart + ".." + GlobalClock.RegionEnd);
        pm.SetDraftDuration(48);   // a half note: a fat cube (half + three 8ths = 3.5 beats, inside the 1-bar island, so every note sounds)
        int aud0 = PathManager.AuditionCount;
        yield return Hover(tiles[1], 0.3f);
        Check(PathManager.AuditionCount > aud0 && draft != null && draft.nodes.Count == 1, "hovering the next tile auditions it without adding it",
              "auditions +" + (PathManager.AuditionCount - aud0) + ", nodes " + (draft != null ? draft.nodes.Count : -1));
        yield return Shot("i4_02_draft_hover.png", captures);
        yield return Click(tiles[1]);
        pm.SetDraftDuration(12);   // fast 8ths from here (the brush)
        yield return Click(tiles[2]);
        yield return Click(tiles[3]);
        yield return Wait(0.2f);
        yield return Shot("i4_03_draft_lengths.png", captures);
        Check(draft != null && draft.nodes.Count == 4 && Durs(draft) == "48 12 12 12", "each click adds one node; the length row sets the latest note and the brush", "durs " + Durs(draft));
        yield return Click(tiles[3]);   // the end tile again: finish
        yield return Wait(0.7f);
        AudioCube mine = SequenceMaster.Cubes.Count > cubes0 ? SequenceMaster.Cubes[SequenceMaster.Cubes.Count - 1] : null;
        Check(mine != null && SequenceMaster.Cubes.Count == cubes0 + 1 && !pm.IsDrawing && mine.isFinalized && !mine.Hologram && mine.HasDurations,
              "finishing solidifies ONE cube with per-note lengths", (SequenceMaster.Cubes.Count - cubes0) + " new, hologram " + (mine != null && mine.Hologram) + ", durs " + Durs(mine));
        Check(History.UndoCount == hist0 + 1, "the whole draft is one History entry", "undo stack " + hist0 + " → " + History.UndoCount);
        int mineIdKept = mine != null ? mine.id : -1;
        float szMax = 0f, szMin = 9f;
        for (float t = 0f; t < 2.6f; t += Time.unscaledDeltaTime) { if (mine != null) { szMax = Mathf.Max(szMax, mine.SizeFactor); szMin = Mathf.Min(szMin, mine.SizeFactor); } yield return null; }
        Check(szMax >= 1.15f && szMin <= 0.9f, "the cube swells on its long note and shrinks on its fast notes while it plays", "size " + szMin.ToString("F2") + " .. " + szMax.ToString("F2"));

        Mark("draft");
        // ---- 2b. a melody longer than its island grows the island's column, so every note sounds (integration fix)
        var t2 = new[] { isl.GetTile(0, 2), isl.GetTile(1, 2), isl.GetTile(2, 2) };
        bool ok2 = true; foreach (var t in t2) if (t == null || !OnScreen(ScreenOf(t))) ok2 = false;
        if (ok2)
        {
            yield return Click(t2[0]); pm.SetDraftDuration(96);
            yield return Click(t2[1]); yield return Click(t2[2]);
            yield return Click(t2[2]);   // finish: three whole notes = 12 beats on a 1-bar island
            yield return Wait(0.8f);
            var a0 = sm.AnchorOf(0);
            Check(a0 != null && a0.bars == 3 && !pm.IsDrawing, "a melody longer than its island grows the island's column to fit it", "bars " + (a0 != null ? a0.bars : -1));
            isl = a0 != null ? a0 : isl;
        }
        else Check(false, "island 0's third row on screen for the long melody");

        Mark("long-melody");
        // ---- 3. columns: add below, duplicate right, lower an octave, drag within the column
        int cols0 = sm.ColumnCount, n0 = sm.Islands.Count;
        int below = sm.AddIslandInColumn(0, false);
        yield return Wait(0.8f);
        var col0 = sm.ColumnIslands(0);
        Check(below >= 0 && col0.Count == 2 && sm.ColumnCount == cols0, "+ below adds an island to the same column", "index " + below + ", column 0 islands " + col0.Count);
        // v8 (S17, the user: an added grid is a normal grid, never forced to the column's length): both start with the column; the new one is one measure
        Check(col0.Count == 2 && Mathf.Approximately(col0[0].startBeatOffset, col0[1].startBeatOffset) && col0[1].bars == 1 && Mathf.Approximately(col0[1].LengthBeats, sm.beatsPerMeasure),
              "both islands of the column start together (they play together); the added one is a normal one-measure grid",
              "starts " + col0[0].startBeatOffset + " / " + col0[1].startBeatOffset + ", lengths " + col0[0].LengthBeats + " / " + col0[1].LengthBeats + ", new bars " + col0[1].bars);
        int dup = sm.DuplicateIsland(0, 0);
        yield return Wait(0.8f);
        Check(dup >= 0 && sm.ColumnCount == cols0 + 1, "duplicate → makes a new column after it", "columns " + cols0 + " → " + sm.ColumnCount);
        KeyBlock low = null; foreach (var kb in sm.ColumnIslands(0)) if (!kb.IsAnchor) low = kb;
        int lowIdx = low != null ? sm.Islands.IndexOf(low) : -1;
        var anchor = sm.AnchorOf(0);
        int midiA = anchor != null && anchor.GetTile(0, 0) != null ? anchor.GetTile(0, 0).midi : 0;
        if (lowIdx >= 0) sm.SetRegister(lowIdx, -1);
        yield return Wait(0.6f);
        low = lowIdx >= 0 && lowIdx < sm.Islands.Count ? sm.Islands[lowIdx] : null;
        int midiL = low != null && low.GetTile(0, 0) != null ? low.GetTile(0, 0).midi : 0;
        Check(low != null && low.register == -1 && midiA - midiL == 12 && low.LiftY < -0.5f * ProjectConfig.RegisterRise, "▼ lowers the island an octave: it sounds 12 lower and really sinks",
              "register " + (low != null ? low.register : 99) + ", midi " + midiA + " vs " + midiL + ", lift " + (low != null ? low.LiftY : 0f).ToString("F2"));
        if (low != null)
        {
            float pz0 = low.pz;
            sm.MoveIslandInColumn(lowIdx, pz0 - 3f, true);
            yield return Wait(0.5f);
            low = sm.Islands[Mathf.Clamp(lowIdx, 0, sm.Islands.Count - 1)];
            var c0 = sm.ColumnIslands(low.column);
            float gap = 99f;
            for (int a = 0; a < c0.Count; a++) for (int b = 0; b < c0.Count; b++) if (a != b) { float g = Mathf.Max(c0[b].FrontEdge - c0[a].BackEdge, c0[a].FrontEdge - c0[b].BackEdge); gap = Mathf.Min(gap, g); }
            Check(Mathf.Abs(low.pz - pz0) >= 2.5f && gap >= ProjectConfig.LaneGap - 0.05f && Mathf.Abs(low.px - sm.AnchorOf(low.column).px) < 1e-3f,
                  "dragging within the column moves it on z only and keeps clear of its neighbours", "pz " + pz0.ToString("F1") + " → " + low.pz.ToString("F1") + ", gap " + gap.ToString("F2"));
        }
        yield return Shot("i4_04_columns.png", captures);

        Mark("columns");
        // ---- 4. inspect → the rhythm strip → stretch a note (structural ops rebuild every cube: find ours again by its id)
        int mineId = mineIdKept;
        mine = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.id == mineId) { mine = c; break; }
        Check(mine != null, "the drawn cube survives the column edits (found again by its id)", "id " + mineId);
        CubeInspector.Open(mine);
        yield return Wait(1.0f);
        var strip = RhythmStrip.Active;
        Check(CubeInspector.IsOpen && InspectorCard.Shown > 0.9f && strip != null && strip.Cube == mine, "clicking the cube opens the v4 inspector card with its rhythm strip",
              "card " + InspectorCard.Shown.ToString("F2") + ", strip " + (strip != null));
        var evs = new List<AudioCube.TimelineEvent>();
        int nev = mine != null ? mine.TimelineEvents(0, evs) : 0;
        Check(strip != null && strip.BlockCount > 0 && strip.BlockCount >= Mathf.Min(nev, 4), "the strip draws the cube's notes as blocks", "blocks " + (strip != null ? strip.BlockCount : 0) + ", events " + nev);
        Check(FocusLoop.Active && FocusLoop.Column == (mine != null && mine.Island != null ? mine.Island.column : -2), "inspecting loops the cube's column");
        int h1 = History.UndoCount;
        RhythmStrip.ApplyLength(mine, 1, 48);
        yield return Wait(0.4f);
        yield return Shot("i4_05_inspector.png", captures);
        Check(mine != null && mine.durs.Count == 4 && mine.durs[1] == 48 && History.UndoCount == h1 + 1, "stretching a note edits its length without deleting the cube (one History entry)", "durs " + Durs(mine));
        CubeInspector.CloseImmediate();
        yield return null;
        FocusLoop.Dismiss();
        yield return Wait(0.3f);
        Check(!FocusLoop.Active && !GlobalClock.HasRegion, "dismissing the focus loop clears the region", "playing " + GlobalClock.IsPlaying);

        Mark("inspect");
        // ---- 5. a recording of the whole song: no late notes
        GlobalClock.Stop(); GlobalClock.Seek(0); GlobalClock.Play();
        Synth.StartRecording(5f);
        yield return Until(() => Synth.RecordingDone, 8f);
        try { Synth.SaveRecording(Cap("i4_song.wav")); } catch (Exception) { }
        Mark("record");
        Check(Synth.LateEvents - Mathf.Min(1, lateGrow) == late0 && Synth.Errors == err0, "synth: no late notes and no errors so far (the island-growing rebuild while looping may slip one event ≤ 30 ms)",
              Synth.Stats() + " | late by section: " + (lateLog.Length > 0 ? lateLog.ToString() : "none"));
        GlobalClock.Stop();

        // ---- 6. present (v9): the real world, its islands rising from the sea as their columns come, the camera falling slowly
        Presenter.Enter();
        yield return Until(() => Presenter.Active, 3f);
        yield return Wait(5f);
        var sea = Presenter.Sea;
        yield return Shot("i4_06_present.png", captures);
        double pb = Presenter.SongBeat; int upCols = 0, early = 0;
        if (sea != null) for (int c = 0; c < SongManager.I.ColumnCount; c++) { double s = SongManager.I.ColumnStart(c); if (s <= pb && sea.ColumnDepth(c, pb) < 0.02f) upCols++; else if (s > pb + 3.0 && sea.ColumnMinDepth(c, pb) < 0.98f) early++; }
        Check(Presenter.Active && sea != null && upCols > 0 && early == 0, "present: the islands rise from the sea as their columns come", sea != null ? ("beat " + pb.ToString("F1") + ": " + upCols + " columns up, " + early + " later ones up early") : "no sea");
        Presenter.Exit();
        yield return Until(() => !Presenter.Active, 4f);
        yield return Wait(0.5f);
        Check(!Presenter.Active && !WorldInput.WorldLocked, "present exits back to the world");

        // ---- 7. Home (autosave) → Continue: columns, registers and lengths survive
        var before = SongState.Capture();
        MainMenu.Show();
        yield return Until(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return Wait(0.3f);
        MainMenu.ContinueButton.Invoke();
        yield return Until(() => !MainMenu.IsShown, 6f);
        yield return Wait(0.8f);
        var after = SongState.Capture();
        int regB = 0, regA = 0, colsB = 0, colsA = 0; string dB = "", dA = "";
        foreach (var m in before.measures) { regB += Mathf.Abs(m.reg); colsB = Mathf.Max(colsB, m.col + 1); }
        foreach (var m in after.measures) { regA += Mathf.Abs(m.reg); colsA = Mathf.Max(colsA, m.col + 1); }
        foreach (var c in before.cubes) if (c.durs != null) dB += string.Join(",", Array.ConvertAll(c.durs, x => x.ToString())) + ";";
        foreach (var c in after.cubes) if (c.durs != null) dA += string.Join(",", Array.ConvertAll(c.durs, x => x.ToString())) + ";";
        Check(!MainMenu.IsShown && after.measures.Length == before.measures.Length && colsA == colsB && regA == regB && dA == dB && after.version == SongState.CurrentVersion,
              "Home then Continue restores the song: columns, registers and note lengths", "islands " + before.measures.Length + "/" + after.measures.Length + ", columns " + colsB + "/" + colsA + ", reg " + regB + "/" + regA + ", durs '" + dB + "' / '" + dA + "'");
        yield return Shot("i4_07_continue.png", captures);
        Check(Synth.LateEvents - Mathf.Min(1, lateGrow) == late0 && Synth.Errors == err0, "synth: no late notes and no errors over the whole run (same tolerance)", Synth.Stats());
        Finish(userMd5, prefStep, prefDone);
    }

    static void Finish(string userMd5, int prefStep, int prefDone)
    {
        Application.logMessageReceived -= OnLog;
        try { CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit(); FocusLoop.Dismiss(); GlobalClock.Stop(); } catch (Exception) { }
        Onboarding.Suppressed = false;
        V3Fixes.RestoreSaves(saves);
        PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep); PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone); PlayerPrefs.Save();
        Check(Md5(SongIO.Path) == userMd5, "the user's save is untouched", userMd5);
        Check(errors == 0, "no console errors or exceptions during the run", errors + (errLines.Count > 0 ? ": " + string.Join(" || ", errLines) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v4_integration_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }
}
