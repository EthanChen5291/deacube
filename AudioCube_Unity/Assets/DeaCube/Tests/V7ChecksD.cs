using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v7 package D checks (Play mode, PathManager.AutoHand = false: the real game). <c>V7ChecksD.RunAll()</c> starts the timed checks; poll
/// <see cref="Done"/> or Captures/d7_report.txt. Sections: "size" (§15 size first — the hologram on the hovered tile shows the brush and a click places
/// exactly that length; while drawing the hologram shows the NEXT node, ] [ . and the draft's length row change only the brush — never a placed node —
/// with a tiny audition; the wheel over a grid with a cube in the hand steps it (trackpad deltas add up; OwnsWheel) and over the sea stays the
/// camera's; the inspector and the empty hand keep [ ]; keys show the key footprint), "roll" (PhraseRoll on data: encode / decode with rests, a
/// leading gap, long rests split, a note replacing what it covers, the auto-grow thresholds ½ → 1 → 2 → 3, a resize), "phrase" (drawing on a phrase
/// island: the hologram bar spans the brush's cells; a press + drag places notes in time order with rests; reaching into the last half measure grows
/// the phrase 1 → 2 measures in the same stroke — ONE History entry —; the phrase loops while drawn; a note over others replaces what it covers; a
/// right-click erases a note; the melody card shows the notes; undo), "runner" (stairs: the hologram + the run's ghost rings; a click with a cube in
/// the hand places the group's runner = StairPath (one History entry), again replaces it, another group adds one, the brush's rate re-derives the run;
/// the empty hand presses a step), "marquee" (§16.1: Shift + drag selects exactly the grids whose platforms the screen rect touches — checked
/// against an independent projection —, live, the camera state is Selecting meanwhile; Shift + click toggles; Esc and a click on the sea clear; a
/// Moon only by its centre; a section plinth), "sleep" (idle tiles switch their Update off and wake on hover / press), "cap" (§19: QUIET drawing —
/// a playing song pauses when a path starts, nothing loops by itself, Space / the draft row's hear-it button start and stop the loop over the draft's
/// grid; CAPACITY — a note past the grid auto-expands it live while its section has room (the draft keeps its nodes), Backspace gives it back, past
/// the section's limit the note is refused ("capacity reached", the EXPAND chip), EXPAND grows it and drawing continues, a cancelled draft gives its
/// auto measures back, the fill caption), "keys" (§20.1: a melody reaching a keyboard's highest / lowest key extends it an octave that way, live — the
/// draft keeps its notes and their pitches —; finishing gives back an added octave that holds no note; one History entry), "grid" (the inspector's
/// path grid keeps the cap: an append past a full grid is refused with the capacity chip, EXPAND lets it in; a phrase cube's grid shrinks its cells to
/// fit and keeps its derived path), "stay" (§21: a stamp no longer flies across — the stamped cube appears on its grid at once; keys clicked into a
/// keyboard draft raise PathManager.OnDraftKey for the cat). Synth late / errors must not
/// move (a late note right after a test's own play start / rebuild while playing, or around a mid-play capture, is counted apart). Captures:
/// d7_size_hologram, d7_size_draft, d7_phrase_hologram, d7_phrase_stroke, d7_runner, d7_marquee.
/// </summary>
public static class V7ChecksD
{
    public static string Report = "";
    public static bool Done = true;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "d7_report.txt");

    static StringBuilder sb;
    static int pass, fail, pushes, brushEvents, lateExcused;
    static bool shots;
    static readonly Vector3 Sea = new Vector3(-50f, -50f, 0f);

    // ================================================================== helpers
    static string L(bool ok, string name, string detail = null) => (ok ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + "\n";
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); Report = sb.ToString(); }
    static void Add(string lines)
    {
        if (string.IsNullOrEmpty(lines)) return;
        foreach (var l in lines.Split('\n'))
        {
            if (l.Length == 0) continue;
            if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++;
            sb.Append(l).Append('\n');
        }
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + "...running " + Progress + "\n"); } catch (Exception) { }
    }
    static string FirstFrame(Exception e) { var st = e.StackTrace ?? ""; int i = st.IndexOf('\n'); return i > 0 ? st.Substring(0, i).Trim() : st.Trim(); }
    static void OnHistory() { pushes++; }
    static void OnBrush() { brushEvents++; }
    static string F(double v, string f = "F2") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static string Ints(IList<int> a) { if (a == null) return "null"; var s = new StringBuilder(); for (int i = 0; i < a.Count; i++) { if (i > 0) s.Append(','); s.Append(a[i]); } return s.ToString(); }
    static string Notes(IList<PhraseNote> n) { if (n == null) return "null"; var s = new StringBuilder(); for (int i = 0; i < n.Count; i++) { if (i > 0) s.Append(' '); s.Append(n[i].ToString()); } return s.ToString(); }
    static string Durs(AudioCube c) => c == null ? "-" : Ints(c.durs);
    static string Mods(AudioCube c) => c == null ? "-" : Ints(c.mods);
    static string Xs(AudioCube c) { if (c == null) return "-"; var l = new List<int>(); foreach (var t in c.nodes) l.Add(t != null ? t.gridX : -1); return Ints(l); }
    static string Zs(AudioCube c) { if (c == null) return "-"; var l = new List<int>(); foreach (var t in c.nodes) l.Add(t != null ? t.gridZ : -1); return Ints(l); }

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray closed, the real mouse ignored, an empty hand, AutoHand off).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = Sea; PathManager.SimShift = false;
        var pm = PathManager.I;
        if (pm != null)
        {
            if (pm.IsDrawing) pm.CancelPath(false);
            if (pm.Drag != null && pm.Drag.Busy) pm.Drag.Cancel();
            pm.Deselect();
            pm.PutDown();
            if (pm.selectedInstrument != 0) pm.SelectInstrument(0);
        }
        for (int i = 0; i < Instruments.Count; i++) Instruments.BrushVoice[i] = 0;
        GridSelection.Clear();
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        KeyShim.Clear();
    }

    public static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        GlobalClock.SetSwing(0f);
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void Frame(Vector3 focus, float dist, float yaw = 0f, float pitch = 58f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    static void Key(KeyCode k, bool cmd = false)
    {
        if (cmd) { KeyShim.Sim(KeyCode.LeftCommand, false, true, false); }
        KeyShim.Sim(k, true, true, false);
        PathManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static IEnumerator Click(Vector3 screen, bool shift = false)
    {
        var pm = PathManager.I;
        pm.SimPointer(screen, true, true, false, false, false, false, shift);
        pm.SimPointer(screen, false, false, true, false, false, false, shift);
        yield return null;
    }

    /// <summary>A screen capture (overlay UI included). A capture mid-play stalls the main thread: late notes around it are counted apart.</summary>
    static IEnumerator Capture(string name)
    {
        if (!shots) yield break;
        int late = Synth.LateEvents;
        Directory.CreateDirectory(V2Checks.CapturePath);
        ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, name + ".png"), 1);
        yield return null; yield return null;
        if (GlobalClock.IsPlaying) { yield return Wait(0.3f); lateExcused += Synth.LateEvents - late; }
    }

    static bool Free(TileInteraction t)
    {
        if (t == null || PathManager.TopCubeOn(t) != null) return false;
        var r = PathManager.I.PickAt(Scr(t.Top));
        return r.tile == t && r.rule == 4;
    }

    /// <summary>A free row of <paramref name="len"/> pickable tiles on <paramref name="kb"/> (the camera frames it and stays there).</summary>
    static bool PickableRow(KeyBlock kb, int len, out int z, out int x0)
    {
        z = -1; x0 = -1;
        if (kb == null || kb.IsMoon || kb.cols < len) return false;
        for (int zz = 0; zz < kb.rows; zz++)
            for (int x = 0; x + len - 1 < kb.cols; x++)
            {
                Frame(kb.GetTile(x + len / 2, zz).Top, 9f, 0f, 62f);
                bool ok = true;
                for (int k = 0; k < len && ok; k++) if (!Free(kb.GetTile(x + k, zz))) ok = false;
                if (ok) { z = zz; x0 = x; return true; }
            }
        return false;
    }

    static KeyBlock FewestCubes(int exceptColumn = -1)
    {
        KeyBlock best = null; int bestCubes = int.MaxValue;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || kb.IsMoon || kb.IsKeyboard || kb.IsStairs || kb.IsPhrase || kb.column == exceptColumn) continue;
            int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) n++;
            if (n < bestCubes) { bestCubes = n; best = kb; }
        }
        return best;
    }

    /// <summary>Frames a set of tiles so each picks itself (tries a few distances); false when some tile does not.</summary>
    static bool FrameTiles(KeyBlock kb, IList<TileInteraction> tiles, float pitch = 60f)
    {
        float[] dists = { 9f, 11f, 13f, 15f, 18f, 22f, 26f };
        float[] pitches = { pitch, 70f, 78f, 84f };
        Vector3 c = Vector3.zero; int n = 0;
        foreach (var t in tiles) if (t != null) { c += t.Top; n++; }
        if (n == 0) return false;
        c /= n;
        foreach (float p in pitches)
            foreach (float d in dists)
            {
                Frame(c, d, 0f, p);
                bool ok = true;
                foreach (var t in tiles) { if (t == null) { ok = false; break; } var r = PathManager.I.PickAt(Scr(t.Top)); if (r.tile != t) { ok = false; break; } }
                if (ok) return true;
            }
        return false;
    }

    static int AnchorIndex(int col) { var sm = SongManager.I; var a = sm.AnchorOf(col); return a != null ? sm.Islands.IndexOf(a) : -1; }

    /// <summary>A one-measure phrase in column <paramref name="col"/>: O's SongOps.AddPhrase, else (before it lands) the same model through
    /// SongManager's internal placement helper. Returns its index (-1 refused); <paramref name="viaOps"/> tells which.</summary>
    static int MakePhrase(int col, out bool viaOps)
    {
        var sm = SongManager.I;
        viaOps = false;
        int idx = -1;
        try { idx = SongOps.AddPhrase(col, 0); } catch (Exception e) { Info("SongOps.AddPhrase threw " + e.GetType().Name + " " + e.Message); }
        if (idx >= 0 && idx < sm.Islands.Count && sm.Islands[idx] != null && sm.Islands[idx].IsPhrase) { viaOps = true; return idx; }
        var st = SongState.Capture();
        int ai = AnchorIndex(col);
        if (ai < 0) return -1;
        var src = st.measures[ai];
        var k = MusicTheory.KeyOfSong();
        int root = 60 + ((k.tonic % 12) + 12) % 12; if (root > 66) root -= 12;
        var ms = new MeasureState { chordKey = "Melody", root = root, semis = new[] { 0 }, bars = src.bars, px = src.px, placed = true, kind = 4, col = col, phraseBeats = 4, phraseGrid = 12, repeat = 1, energy = src.energy };
        ms.pz = SongManager.LanePz(src, ms, true);
        idx = sm.PlaceStateInColumn(st, ms, col, null);
        History.Push();
        return idx;
    }

    /// <summary>A stairs island (4 falling steps at 12 ticks) in island <paramref name="island"/>'s column: O's SongOps.AddStairIsland (with its runner),
    /// else the model through the internal placement helper (no runner). Returns its index.</summary>
    static int MakeStairs(int island, out bool viaOps)
    {
        var sm = SongManager.I;
        viaOps = false;
        int idx = -1;
        try { idx = SongOps.AddStairIsland(island, false, 0); } catch (Exception e) { Info("SongOps.AddStairIsland threw " + e.GetType().Name + " " + e.Message); }
        if (idx >= 0 && idx < sm.Islands.Count && sm.Islands[idx] != null && sm.Islands[idx].IsStairs) { viaOps = true; return idx; }
        var st = SongState.Capture();
        var src = st.measures[island];
        var ms = new MeasureState { chordKey = "Stairs", root = src.root, semis = new[] { 0 }, bars = src.bars, px = src.px, placed = true, kind = 3, col = src.col, stairType = 0, stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true, repeat = 1, energy = src.energy };
        ms.pz = SongManager.LanePz(src, ms, false);
        idx = sm.PlaceStateInColumn(st, ms, sm.Islands[island].column, null);
        History.Push();
        return idx;
    }

    static KeyBlock PhraseAt(int col)
    {
        foreach (var kb in SongManager.I.Islands) if (kb != null && kb.IsPhrase && kb.column == col) return kb;
        return null;
    }

    // ================================================================== entry points
    public static string RunAll(bool captures = true) => Start(null, captures);
    /// <summary>Some sections (a comma list of "size", "roll", "phrase", "runner", "marquee", "sleep").</summary>
    public static string RunOnly(string which, bool captures = false) => Start(which, captures);

    static string Start(string only, bool captures)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start"; shots = captures;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(only));
        return "started" + (only != null ? " " + only : "");
    }

    static readonly string[] Sections = { "size", "cap", "keys", "grid", "stay", "roll", "phrase", "runner", "marquee", "sleep" };

    static IEnumerator Routine(string only)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0; brushEvents = 0; lateExcused = 0;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float bpm0 = GlobalClock.BPM;
        bool auto0 = PathManager.AutoHand;
        int brush0 = PathManager.I.BrushTicks;
        History.OnChanged += OnHistory;
        PathManager.OnBrushChanged += OnBrush;
        foreach (var s in Sections)
        {
            if (only != null && Array.IndexOf(only.Split(','), s) < 0) continue;
            Prepare(); LoadFixture(); yield return null; yield return null;
            Progress = s;
            IEnumerator body = null;
            switch (s)
            {
                case "size": body = Size(); break;
                case "cap": body = Cap(); break;
                case "keys": body = KeysExtend(); break;
                case "grid": body = InspectorGrid(); break;
                case "stay": body = Stay(); break;
                case "roll": body = Roll(); break;
                case "phrase": body = Phrase(); break;
                case "runner": body = Runner(); break;
                case "marquee": body = Marquee(); break;
                case "sleep": body = Sleep(); break;
            }
            int lateS = Synth.LateEvents;
            if (body != null) yield return Guard(body);
            if (Synth.LateEvents != lateS) Info("section " + s + ": synth late +" + (Synth.LateEvents - lateS) + " (last: " + Synth.LastLate + ")");
            var pm = PathManager.I;
            PathManager.SimShift = false;
            if (pm.IsDrawing) pm.CancelPath(false);
            pm.PutDown();
            GridSelection.Clear();
            CubeInspector.CloseImmediate();
            FocusLoop.Dismiss(); GlobalClock.Stop();
            if (GlobalClock.BPM != bpm0) GlobalClock.SetBPM(bpm0);
        }
        Progress = "finish";
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false; PathManager.SimShift = false;
        PathManager.I.PutDown();
        PathManager.I.SetBrush(brush0);
        PathManager.AutoHand = auto0;
        History.OnChanged -= OnHistory;
        PathManager.OnBrushChanged -= OnBrush;
        V3Fixes.RestoreSaves(saves);
        Add(L(Synth.LateEvents - lateExcused == late0 && Synth.Errors == err0, "RUN Synth late / errors unchanged (counted apart: " + lateExcused + " right after the test's own play starts / rebuilds while playing or around mid-play captures)", Synth.Stats()));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true; Progress = "done";
    }

    static IEnumerator Guard(IEnumerator body)
    {
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) yield break; cur = body.Current; }
            catch (Exception e) { Add(L(false, Progress + " threw", e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e))); yield break; }
            yield return cur;
        }
    }

    // ================================================================== 1. size first (§15)
    static int lateT;
    /// <summary>v7 (integration): a checkpoint — reports when the synth's late count moved since the last one (and the last late event).</summary>
    static void T(string tag) { if (Synth.LateEvents != lateT) Info("late at " + tag + ": +" + (Synth.LateEvents - lateT) + " (" + Synth.LastLate + ", playing " + GlobalClock.IsPlaying + ")"); lateT = Synth.LateEvents; }

    static IEnumerator Size()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        lateT = Synth.LateEvents;
        var kb = FewestCubes();
        int z, x0;
        if (!PickableRow(kb, 4, out z, out x0)) { Add(L(false, "S size first", "no free pickable row of 4")); yield break; }
        var a = kb.GetTile(x0, z); var b = kb.GetTile(x0 + 1, z); var c = kb.GetTile(x0 + 2, z); var d = kb.GetTile(x0 + 3, z);
        pm.PickUpCube(0);
        int ev0 = brushEvents;
        pm.SetBrush(48);
        PathManager.SimPos = Scr(a.Top);
        yield return Frames(4);
        float want48 = ProjectConfig.CubeSize * AudioCube.SizeOf(2f) * AudioCube.FootOf(a);
        bool holo = pm.HandGhostTile == a && pm.HandGhostTicks == 48 && Mathf.Abs(pm.HandGhostSize - want48) < 1e-3f && pm.HandGhost != null && pm.HandGhost.gameObject.activeSelf;
        pm.SetBrush(24); yield return Frames(2);
        bool holo24 = pm.HandGhostTicks == 24 && Mathf.Abs(pm.HandGhostSize - ProjectConfig.CubeSize * AudioCube.SizeOf(1f) * AudioCube.FootOf(a)) < 1e-3f;
        float shownScale = pm.HandGhost != null ? pm.HandGhost.localScale.x : 0f;
        if (shots) { Frame(b.Top, 6.5f, -16f, 52f); PathManager.SimPos = Scr(a.Top); yield return Wait(0.3f); yield return Capture("d7_size_hologram"); PickableRow(kb, 4, out z, out x0); PathManager.SimPos = Scr(a.Top); yield return Frames(3); }
        T("before play");
        int p0 = pushes;
        int lp = Synth.LateEvents;
        GlobalClock.Play();   // §19.2: a song playing when a path starts pauses; nothing loops by itself
        yield return Wait(0.3f);
        lateExcused += Synth.LateEvents - lp;   // v7 (integration): the test's own play start (as the RUN line says) — counted apart
        T("play window");
        yield return Click(Scr(a.Top));
        T("first click");
        var draft = pm.Draft;
        bool first = pm.IsDrawing && draft != null && Durs(draft) == "24" && pushes == p0;
        bool quiet = !GlobalClock.IsPlaying && !FocusLoop.Active;
        Add(L(holo && holo24 && first && brushEvents > ev0, "S1 SIZE FIRST: the hologram on the hovered tile shows the brush chosen before the click (48 ticks: size SizeOf(2 beats); 24: SizeOf(1)); the click places node 0 at exactly that length",
              "ghost ticks 48 size " + F(want48, "F3") + " (scale shown " + F(shownScale, "F3") + "), then 24 " + holo24 + ", draft durs " + Durs(draft) + ", brush events +" + (brushEvents - ev0)));
        Add(L(quiet, "Q1 QUIET drawing: the song that was playing PAUSED when the path started and nothing loops by itself (v4 looped the column)", "playing " + GlobalClock.IsPlaying + ", focus loop " + FocusLoop.Active));
        if (draft == null) yield break;
        // Space / the draft row's hear-it button: the loop over the draft's grid in context, on and off
        int t0 = PathManager.DrawLoopToggles;
        int lq = Synth.LateEvents;
        Key(KeyCode.Space);
        yield return Frames(2);
        bool spaceOn = FocusLoop.Active && FocusLoop.Column == kb.column && GlobalClock.IsPlaying && pm.DrawLoopOn && PathManager.OwnsSpace;
        // v7 (integration): the loop starts mid-song — the step under the playhead fires at once (≤ 30 ms late by design, as after a seek) — counted apart
        yield return Wait(0.3f); lateExcused += Synth.LateEvents - lq;
        yield return Wait(0.1f);
        int own = 0; // the draft sounds in its loop
        Key(KeyCode.Space);
        yield return Frames(2);
        bool spaceOff = !FocusLoop.Active && !GlobalClock.IsPlaying && !pm.DrawLoopOn;
        yield return Frames(2);
        bool btnShown = DurationPicker.LoopShown;
        int lb = Synth.LateEvents;
        DurationPicker.SimClickLoop(); yield return Frames(2);
        bool btnOn = FocusLoop.Active && GlobalClock.IsPlaying;
        DurationPicker.SimClickLoop(); yield return Frames(2);
        bool btnOff = !FocusLoop.Active && !GlobalClock.IsPlaying;
        yield return Frames(2); lateExcused += Synth.LateEvents - lb;   // the same: the button's loop start fires its first step at once
        Add(L(spaceOn && spaceOff && btnShown && btnOn && btnOff && PathManager.DrawLoopToggles == t0 + 4,
              "Q2 Space while drawing — and the draft row's hear-it button — start the loop over the draft's grid in context and stop it again (the transport back to paused)",
              "space on " + spaceOn + " (col " + FocusLoop.Column + "/" + kb.column + "), off " + spaceOff + ", button shown " + btnShown + " on " + btnOn + " off " + btnOff + ", toggles +" + (PathManager.DrawLoopToggles - t0) + (own > 0 ? "" : "")));

        T("space / hear-it loops");
        // while drawing: the hologram shows the NEXT node on the tile a click goes to; ] [ . set only the brush (auditioned)
        PathManager.SimPos = Scr(b.Top);
        yield return Frames(3);
        bool nextHolo = pm.HandGhostTile == b && pm.HandGhostTicks == 24;
        int au0 = PathManager.BrushAuditions;
        Key(KeyCode.RightBracket);
        int k1 = pm.BrushTicks; string placed1 = Durs(draft);
        yield return Frames(2);
        bool holo96 = pm.HandGhostTicks == 48 && Mathf.Abs(pm.HandGhostSize - ProjectConfig.CubeSize * AudioCube.SizeOf(2f)) < 1e-3f;
        yield return Click(Scr(b.Top));
        string after = Durs(draft);
        Key(KeyCode.Period); int k2 = pm.BrushTicks;
        Key(KeyCode.LeftBracket); int k3 = pm.BrushTicks;
        Key(KeyCode.LeftBracket); int k4 = pm.BrushTicks;
        Key(KeyCode.Period); int k5 = pm.BrushTicks;
        bool kept = Durs(draft) == "24,48";
        int auditions = PathManager.BrushAuditions - au0;
        Add(L(nextHolo && k1 == 48 && placed1 == "24" && holo96 && after == "24,48" && k2 == 72 && k3 == 36 && k4 == 18 && k5 == 12 && kept && auditions >= 5,
              "S2 while drawing the hologram shows the NEXT node on the tile a click goes to; ] [ . change only the brush, each with a tiny audition — the placed nodes keep their lengths (v4 resized the latest one); the next click places the new size",
              "ghost on next tile " + nextHolo + ", ] -> " + k1 + " (placed " + placed1 + ", ghost " + pm.HandGhostTicks + "), click -> " + after + ", . " + k2 + " [ " + k3 + " [ " + k4 + " . " + k5 + ", placed now " + Durs(draft) + ", auditions +" + auditions));

        T("brush keys");
        // the wheel over a grid with a cube in the hand = the brush (one size per notch; small deltas add up); over the sea: the camera's
        PathManager.SimPos = Scr(c.Top);
        yield return Frames(2);
        bool owns = PathManager.OwnsWheel;
        int w0 = PathManager.WheelSteps;
        pm.SimWheel(1f); yield return Frames(2);
        int up = pm.BrushTicks;
        yield return Wait(0.12f);
        pm.SimWheel(-1f); yield return Frames(2);
        int down = pm.BrushTicks;
        yield return Wait(0.12f);
        pm.SimWheel(0.35f); yield return Frames(2);
        pm.SimWheel(0.35f); yield return Frames(2);
        int part = pm.BrushTicks;
        pm.SimWheel(0.35f); yield return Frames(2);
        int whole = pm.BrushTicks;
        PathManager.SimPos = Sea; yield return Frames(2);
        bool seaOwns = PathManager.OwnsWheel;
        int beforeSea = pm.BrushTicks;
        pm.SimWheel(1f); yield return Frames(2);
        int seaBrush = pm.BrushTicks;
        Add(L(owns && up == 24 && down == 12 && part == 12 && whole == 24 && !seaOwns && seaBrush == beforeSea && PathManager.WheelSteps == w0 + 3,
              "S3 the WHEEL over a grid with a cube in the hand steps the brush (one size per notch; a trackpad's small deltas add up) and OwnsWheel keeps the camera from zooming; over the sea the wheel stays the camera's",
              "owns " + owns + ", 12 +1 -> " + up + " -1 -> " + down + ", 0.35 x2 -> " + part + " x3 -> " + whole + ", over the sea owns " + seaOwns + " brush " + beforeSea + " -> " + seaBrush + ", steps +" + (PathManager.WheelSteps - w0)));

        T("wheel");
        // the draft's length row is the brush too: a pick sets the NEXT note, the next click places it
        pm.SetBrush(24);
        yield return Frames(2);
        bool rowShown = DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Draft;
        DurationPicker.SimClick(1); yield return null;
        int rowBrush = pm.BrushTicks; string rowPlaced = Durs(draft);
        PathManager.SimPos = Scr(c.Top); yield return Frames(2);
        yield return Click(Scr(c.Top));
        string rowAfter = Durs(draft);
        if (shots) { PathManager.SimPos = Scr(d.Top); yield return Wait(0.35f); yield return Capture("d7_size_draft"); }
        Add(L(rowShown && rowBrush == 12 && rowPlaced == "24,48" && rowAfter == "24,48,12" && DurationPicker.SelectedTicks == 12,
              "S4 the draft's length row (DurationPicker) sets the brush — the next note — and shows it; the placed notes stay; the next click places that size",
              "row shown " + rowShown + ", pick -> brush " + rowBrush + " (placed " + rowPlaced + "), click -> " + rowAfter + ", row shows " + DurationPicker.SelectedTicks));
        int f0 = pushes;
        Key(KeyCode.Return);
        yield return Frames(2);
        bool finished = !pm.IsDrawing && pushes == f0 + 1 && pm.Hand == PathManager.HandKind.Cube && draft.isFinalized && Durs(draft) == "24,48,12" && !GlobalClock.IsPlaying;
        Add(L(finished, "S4 finishing keeps the sizes chosen before each click (one History entry; the cube stays in the hand); finishing plays nothing (§19.2)", "durs " + Durs(draft) + ", pushes +" + (pushes - f0) + ", hand " + pm.Hand + ", playing " + GlobalClock.IsPlaying));

        // the inspector keeps [ ] (its necklace / note length); the empty hand leaves the brush alone
        int bi = pm.BrushTicks;
        int lo0 = Synth.LateEvents;
        CubeInspector.Open(draft);   // starts the column's focus loop (FocusLoop.Begin): the test's own play start
        yield return Wait(0.6f); lateExcused += Synth.LateEvents - lo0;   // v7 (integration): its first step lands in the card's heavy first frames — counted apart
        bool insp = CubeInspector.IsOpen;
        Key(KeyCode.LeftBracket); Key(KeyCode.Period);
        int inInsp = pm.BrushTicks;
        CubeInspector.CloseImmediate();
        yield return Frames(3);
        pm.PutDown();
        Key(KeyCode.RightBracket); Key(KeyCode.Period);
        int emptyHand = pm.BrushTicks;
        Add(L(insp && inInsp == bi && emptyHand == bi, "S5 [ ] . are the brush's only with a cube in the hand or a draft: the open inspector keeps them (its necklace / note), the empty hand leaves the brush",
              "inspector open " + insp + ", brush " + bi + " -> " + inInsp + " (inspector) -> " + emptyHand + " (empty hand)"));

        T("inspector open / close");
        // on a keyboard the hologram is the brush at the key footprint; SetBrush clamps; events
        int lk = Synth.LateEvents;
        int ki = sm.AddKeyboardIsland(sm.Islands.IndexOf(kb), false);   // a rebuild while the inspector's focus loop still plays
        yield return Wait(0.6f); lateExcused += Synth.LateEvents - lk;   // v7 (integration): the step under the playhead after the rebuild fires at once — counted apart
        T("keyboard added");
        KeyBlock keys = ki >= 0 && ki < sm.Islands.Count ? sm.Islands[ki] : null;
        bool keyOk = false; string keyDetail = "no keyboard (AddKeyboardIsland " + ki + ")";
        if (keys != null && keys.IsKeyboard)
        {
            var t = keys.GetTile(5, 0);
            if (FrameTiles(keys, new List<TileInteraction> { t }, 56f))
            {
                pm.PickUpCube(1); pm.SetBrush(24);
                PathManager.SimPos = Scr(t.Top); yield return Frames(3);
                float wantK = ProjectConfig.CubeSize * AudioCube.SizeOf(1f) * AudioCube.KeyFoot;
                keyOk = pm.HandGhostTile == t && Mathf.Abs(pm.HandGhostSize - wantK) < 1e-3f;
                keyDetail = "ghost on key " + (pm.HandGhostTile == t) + ", size " + F(pm.HandGhostSize, "F3") + " / want " + F(wantK, "F3");
            }
            else keyDetail = "key 5 not pickable";
        }
        int ev1 = brushEvents;
        pm.SetBrush(500); int hi = pm.BrushTicks; pm.SetBrush(1); int lo = pm.BrushTicks; pm.SetBrush(lo);
        Add(L(keyOk && hi == 192 && lo == 3 && brushEvents == ev1 + 2, "S6 on a keyboard the hologram is the brush at the key footprint (AudioCube.KeyFoot); SetBrush clamps 3..192 and raises OnBrushChanged once per change",
              keyDetail + ", clamp 500 -> " + hi + ", 1 -> " + lo + ", events +" + (brushEvents - ev1)));
        PathManager.SimPos = Sea;
        T("length row / keyboard brush");
    }

    // ================================================================== 1b. capacity (§19.1)
    static IEnumerator Cap()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        // a grid in a section with room (the fixture: sections of 4 one-bar columns, the last one shorter) and one in a full section
        KeyBlock roomy = null, full = null;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.IsKeyboard || kb.IsStairs || kb.IsPhrase) continue;
            int s = sm.SectionOf(kb.column);
            if (roomy == null && sm.SectionBars(s) <= 2) roomy = kb;
            if (full == null && sm.SectionBars(s) >= ProjectConfig.SectionBars) full = kb;
        }
        if (roomy == null || full == null) { Add(L(false, "C capacity", "no grid in a short section / a full section (sections: " + sm.SectionCount + ")")); yield break; }
        int z, x0;
        if (!PickableRow(roomy, 4, out z, out x0)) { Add(L(false, "C capacity", "no free pickable row of 4 on the roomy grid")); yield break; }
        var t = new TileInteraction[4]; for (int k = 0; k < 4; k++) t[k] = roomy.GetTile(x0 + k, z);
        int col = roomy.column, bars0 = sm.ColumnBars(col), sec = sm.SectionOf(col), secBars0 = sm.SectionBars(sec);
        pm.PickUpCube(0); pm.SetBrush(96);
        int ex0 = PathManager.AutoExpands, un0 = PathManager.AutoUnexpands, rf0 = PathManager.CapacityRefusals, p0 = pushes;
        yield return Click(Scr(t[0].Top));
        var d = pm.Draft;
        string fill1 = DurationPicker.FillText;
        yield return Click(Scr(t[1].Top));
        yield return Frames(2);
        bool grew = d != null && pm.Draft == d && Durs(d) == "96,96" && sm.ColumnBars(col) == bars0 + 1 && PathManager.AutoExpands == ex0 + 1 && pushes == p0 && t[0] != null && t[1] != null;
        string fill2 = DurationPicker.FillText;
        Add(L(grew && fill1 == "4 / 4 beats" && fill2 == "8 / 8 beats",
              "C1 a note past the grid's capacity AUTO-EXPANDS it one measure live (K's ResizeColumnLive: the same draft, tiles alive, no History) while its section has room; the draft row says how full it is",
              "bars " + bars0 + " -> " + sm.ColumnBars(col) + " (section " + secBars0 + " -> " + sm.SectionBars(sec) + "), durs " + Durs(d) + ", same draft " + (pm.Draft == d) + ", fill '" + fill1 + "' -> '" + fill2 + "', pushes +" + (pushes - p0)));
        Key(KeyCode.Backspace);
        yield return Frames(2);
        bool back = d != null && Durs(d) == "96" && sm.ColumnBars(col) == bars0 && PathManager.AutoUnexpands == un0 + 1;
        Add(L(back, "C2 Backspace AUTO-UNEXPANDS: the grid gives back the measure (never below its length when the draft began)", "durs " + Durs(d) + ", bars " + sm.ColumnBars(col) + ", unexpands +" + (PathManager.AutoUnexpands - un0)));
        // a dropped draft gives the measures auto-expand added back
        yield return Click(Scr(t[1].Top));
        int grown = sm.ColumnBars(col);
        Key(KeyCode.Escape);
        yield return Frames(2);
        Add(L(grown == bars0 + 1 && sm.ColumnBars(col) == bars0 && !pm.IsDrawing, "C3 Esc drops the draft and gives back the measure auto-expand added for it", "bars " + bars0 + " -> " + grown + " -> " + sm.ColumnBars(col)));
        // fill the section to its limit, then one more is refused
        yield return Click(Scr(t[0].Top));
        d = pm.Draft;
        yield return Click(Scr(t[1].Top));
        yield return Click(Scr(t[2].Top));
        int barsAt = sm.ColumnBars(col), secAt = sm.SectionBars(sec);
        int nodesBefore = d != null ? d.nodes.Count : -1;
        yield return Click(Scr(t[3].Top));
        yield return Frames(2);
        bool refused = d != null && d.nodes.Count == nodesBefore && pm.CapacityReached && PathManager.CapacityRefusals == rf0 + 1 && sm.ColumnBars(col) == barsAt;
        bool chip = DurationPicker.ExpandShown && DurationPicker.FillText == "capacity reached";
        if (shots) { PathManager.SimPos = Scr(t[3].Top); yield return Wait(0.3f); yield return Capture("d7_capacity"); }
        Add(L(refused && chip && secAt == ProjectConfig.SectionBars && nodesBefore == 3,
              "C4 CAPACITY REACHED: past the section's SectionBars measures the note is refused (a thud, the grid shakes) — the draft row says \"capacity reached\" and shows the EXPAND chip",
              "section at " + secAt + " measures (column " + barsAt + " bars), nodes " + nodesBefore + " -> " + (d != null ? d.nodes.Count : -1) + ", reached " + pm.CapacityReached + ", refusals +" + (PathManager.CapacityRefusals - rf0) + ", fill '" + DurationPicker.FillText + "', chip " + DurationPicker.ExpandShown));
        int e0 = PathManager.Expands; p0 = pushes;
        DurationPicker.SimClickExpand();
        yield return Frames(2);
        bool expanded = PathManager.Expands == e0 + 1 && sm.ColumnBars(col) == barsAt + 1 && pm.Draft == d && !pm.CapacityReached && pushes == p0 + 1;
        yield return Click(Scr(t[3].Top));
        bool cont = d != null && d.nodes.Count == nodesBefore + 1;
        Add(L(expanded && cont, "C5 EXPAND (+ one measure, the section grows past 4; one History entry): the draft stays and the refused note now fits",
              "bars " + barsAt + " -> " + sm.ColumnBars(col) + ", same draft " + (pm.Draft == d) + ", pushes +" + (pushes - p0) + ", nodes " + (d != null ? d.nodes.Count : -1)));
        Key(KeyCode.Return);
        yield return Frames(2);
        // a full section: no auto-expand at all
        if (!PickableRow(full, 2, out z, out x0)) { Add(L(false, "C6 full section", "no free pickable row of 2")); yield break; }
        var f0 = full.GetTile(x0, z); var f1 = full.GetTile(x0 + 1, z);
        int fb0 = sm.ColumnBars(full.column); rf0 = PathManager.CapacityRefusals; ex0 = PathManager.AutoExpands;
        yield return Click(Scr(f0.Top));
        yield return Click(Scr(f1.Top));
        var fd = pm.Draft;
        bool noGrow = fd != null && Durs(fd) == "96" && sm.ColumnBars(full.column) == fb0 && PathManager.AutoExpands == ex0 && PathManager.CapacityRefusals == rf0 + 1;
        Add(L(noGrow, "C6 a grid whose section already has SectionBars measures never auto-expands: the note past it is refused at once", "durs " + Durs(fd) + ", bars " + fb0 + " -> " + sm.ColumnBars(full.column) + ", refusals +" + (PathManager.CapacityRefusals - rf0)));
        pm.CancelPath();
        yield return Frames(2);
        PathManager.SimPos = Sea;
    }

    // ================================================================== 1c. the keyboard extends under a melody (§20.1)
    static IEnumerator KeysExtend()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        if (!PathManager.KeysLiveAvailable) { Add(L(false, "K1 a keyboard extends under a melody", "K's SongManager.ExtendKeysLive / ShrinkKeysLive are not in the tree yet")); yield break; }
        int ki = sm.AddKeyboardIsland(AnchorIndex(4), false);
        yield return Wait(0.6f);
        KeyBlock keys = ki >= 0 && ki < sm.Islands.Count ? sm.Islands[ki] : null;
        if (keys == null || !keys.IsKeyboard) { Add(L(false, "K1 keyboard", "AddKeyboardIsland " + ki)); yield break; }
        int n0 = keys.cols;
        pm.PickUpCube(1); pm.SetBrush(12);
        GlobalClock.Stop();
        var mid = keys.GetTile(12, 0); var top = keys.GetTile(n0 - 1, 0);
        if (!FrameTiles(keys, new List<TileInteraction> { mid, top, keys.GetTile(0, 0) }, 56f)) { Add(L(false, "K1 keys pickable", "not from the test camera")); yield break; }
        int p0 = pushes, x0 = PathManager.KeyExtends, s0 = PathManager.KeyShrinks;
        yield return Click(Scr(mid.Top));
        var d = pm.Draft;
        yield return Click(Scr(top.Top));
        bool up = keys.cols == n0 + 12 && PathManager.KeyExtends == x0 + 1 && pm.Draft == d && d != null && d.nodes.Count == 2 && pm.DraftKeyOctavesAbove == 1;
        yield return Wait(0.7f);   // the new octave slides in
        var hi = keys.GetTile(n0 + 3, 0); Vector3 hp;
        if (hi != null && FrameKey(keys, hi, out hp)) yield return Click(hp); else Info("K1: key " + (n0 + 3) + " not pickable");
        var low = keys.GetTile(0, 0); Vector3 lp;
        if (FrameKey(keys, low, out lp)) yield return Click(lp); else Info("K1: key 0 not pickable");
        yield return Wait(0.7f);
        bool down = keys.cols == n0 + 24 && PathManager.KeyExtends == x0 + 2 && d != null && d.nodes.Count == 4 && pm.DraftKeyOctavesBelow == 1;
        var before = new List<int>(); if (d != null) foreach (var t in d.nodes) before.Add(t != null ? t.midi : -1);
        Add(L(up && down && pushes == p0, "K1 a melody reaching the keyboard's highest key extends it an octave above (live: the same draft), the lowest key an octave below",
              "keys " + n0 + " -> " + (n0 + 12) + " -> " + keys.cols + ", extends +" + (PathManager.KeyExtends - x0) + ", draft notes " + (d != null ? d.nodes.Count : -1) + ", pushes +" + (pushes - p0)));
        Key(KeyCode.Return);
        yield return Frames(2);
        var after = new List<int>(); if (d != null) foreach (var t in d.nodes) after.Add(t != null ? t.midi : -1);
        bool gave = keys.cols == n0 + 12 && PathManager.KeyShrinks == s0 + 1 && Ints(before) == Ints(after) && pushes == p0 + 1 && d != null && d.isFinalized;
        Add(L(gave, "K2 finishing gives back the added octave that holds no note (below: the lowest note sits on the old lowest key) and keeps the one above (a note there); every note keeps its pitch; ONE History entry",
              "keys " + keys.cols + " (want " + (n0 + 12) + "), shrinks +" + (PathManager.KeyShrinks - s0) + ", pitches " + Ints(before) + " -> " + Ints(after) + ", pushes +" + (pushes - p0)));
        PathManager.SimPos = Sea;
    }

    // ================================================================== 1d. the inspector's path grid (§19.1 "finished paths keep the cap"; phrases)
    static IEnumerator InspectorGrid()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        // a full grid (a section of SectionBars one-bar columns): a cube of four quarters fills its bar
        KeyBlock full = null;
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase && sm.SectionBars(sm.SectionOf(kb.column)) >= ProjectConfig.SectionBars && sm.ColumnBars(kb.column) == 1) { full = kb; break; }
        if (full == null) { Add(L(false, "G inspector grid", "no one-bar grid in a full section")); yield break; }
        var st = new CubeState { instrument = 0, measure = sm.Islands.IndexOf(full), xs = new[] { 0, 1, 2, 3 }, zs = new[] { 0, 0, 0, 0 }, rests = new bool[4], mods = new int[4], durs = new[] { 24, 24, 24, 24 }, volume = 1f, moon = -1 };
        var c = pm.RestoreCube(st);
        SequenceMaster.RecalculateTimeline();
        History.Push();
        CubeInspector.Open(c);
        yield return Wait(0.7f);
        var g = InspectorCard.Grid;
        if (g == null || g.Cube != c) { Add(L(false, "G1 inspector grid", "the card's grid is not bound to the cube")); CubeInspector.CloseImmediate(); yield break; }
        int p0 = pushes, r0 = g.CapacityRefusals, b0 = sm.ColumnBars(full.column);
        g.SimDownCell(0, Mathf.Min(1, full.rows - 1)); g.SimUp(); yield return null;
        bool refused = c.nodes.Count == 4 && g.CapacityRefusals == r0 + 1 && g.CapacityChipShown && pushes == p0;
        g.SimClickExpand(); yield return Frames(2);
        bool expanded = sm.ColumnBars(full.column) == b0 + 1 && pushes == p0 + 1 && !g.CapacityChipShown && g.Cube == c;
        g.SimDownCell(0, Mathf.Min(1, full.rows - 1)); g.SimUp(); yield return null;
        Add(L(refused && expanded && c.nodes.Count == 5, "G1 the inspector's grid keeps the cap: an append past a full grid is refused (a thud, the grid shakes, the \"capacity reached\" chip); its EXPAND adds a measure (one History entry) and the append goes in",
              "refused " + refused + " (nodes 4, refusals +" + (g.CapacityRefusals - r0) + "), expanded " + expanded + " (bars " + b0 + " -> " + sm.ColumnBars(full.column) + "), nodes now " + c.nodes.Count));
        CubeInspector.CloseImmediate();
        yield return Frames(2);
        // a phrase cube in the inspector: the 8 x 15 roll fits (smaller cells) and its path (notes in time order) is not edited as a grid path
        bool viaOps;
        int idx = MakePhrase(1, out viaOps);
        yield return Wait(0.4f);
        var ph = PhraseAt(1);
        if (ph == null || ph.tiles.Count == 0) { Add(L(false, "G2 phrase in the inspector", "no phrase with cells")); yield break; }
        int[] xs, zs, mods, durs;
        PhraseRoll.Encode(new List<PhraseNote> { new PhraseNote(0, 4, 24), new PhraseNote(48, 7, 24) }, PhraseRoll.TicksOf(ph), ph.phraseGrid, ph.cols, out xs, out zs, out mods, out durs);
        var ps = new CubeState { instrument = 0, measure = sm.Islands.IndexOf(ph), xs = xs, zs = zs, mods = mods, durs = durs, rests = new bool[xs.Length], volume = 1f, moon = -1 };
        for (int i = 0; i < xs.Length; i++) ps.rests[i] = mods[i] == 1;
        var pc = pm.RestoreCube(ps);
        SequenceMaster.RecalculateTimeline();
        History.Push();
        CubeInspector.Open(pc);
        yield return Wait(0.7f);
        g = InspectorCard.Grid;
        string path0 = Xs(pc) + "|" + Durs(pc);
        int pp = pushes;
        if (g != null) { g.SimDownCell(6, 10); g.SimUp(); yield return null; }
        bool fits = g != null && g.Cube == pc && g.CellScale < 0.99f && g.PathLocked;
        Add(L(fits && Xs(pc) + "|" + Durs(pc) == path0 && pushes == pp, "G2 a phrase cube's inspector grid: the roll's cells shrink to fit the card and a click on an empty cell does not append to the derived path (notes are drawn on the phrase)",
              "cell scale " + (g != null ? F(g.CellScale, "F2") : "-") + ", locked " + (g != null && g.PathLocked) + ", path " + path0 + " -> " + Xs(pc) + "|" + Durs(pc)));
        CubeInspector.CloseImmediate();
        yield return Frames(2);
    }

    /// <summary>Frames a keyboard key and finds a screen point that picks it (its top, else nearer its front end, where no black key covers a white one).</summary>
    static bool FrameKey(KeyBlock kb, TileInteraction t, out Vector3 screen)
    {
        screen = Vector3.zero;
        if (t == null) return false;
        float[] dists = { 9f, 11f, 13f, 16f, 20f };
        float[] pitches = { 56f, 66f, 76f };
        float[] fronts = { 0f, 0.2f, 0.35f, 0.5f, 0.65f };
        foreach (float p in pitches)
            foreach (float d in dists)
            {
                Frame(t.Top, d, 0f, p);
                foreach (float f in fronts)
                {
                    Vector3 sp = Scr(t.Top + new Vector3(0f, 0f, -f));
                    if (PathManager.I.PickAt(sp).tile == t) { screen = sp; return true; }
                }
            }
        return false;
    }

    // ================================================================== 1e. §21 cubes stay on their grid (D's part) + the draft keys the cat plays
    static IEnumerator Stay()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && c.Island != null && !c.Island.IsKeyboard) { src = c; break; }
        KeyBlock target = null;
        if (src != null) foreach (var kb in sm.Islands) if (kb != null && kb != src.Island && !kb.IsMoon && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase) { target = kb; break; }
        if (src == null || target == null) { Add(L(false, "T1 §21 stamp", "no source cube / target grid")); yield break; }
        pm.Select(src, true);
        int a0 = PathManager.Appears;
        var made = pm.StampTo(target);
        History.Push();
        pm.Deselect();
        yield return null;
        bool on = made != null && made.Island == target && made.BodyVisible && PathManager.Appears == a0 + 1;
        var b = target.VisualBounds; Vector3 bp = made != null ? made.transform.position : Vector3.zero;
        bool inside = made != null && bp.x >= b.min.x - 0.1f && bp.x <= b.max.x + 0.1f && bp.z >= b.min.z - 0.1f && bp.z <= b.max.z + 0.1f;
        Add(L(on && inside, "T1 §21 a stamp no longer flies a cube across grids: the stamped cube APPEARS on its target grid at once (on its platform, visible)",
              "island ok " + (made != null && made.Island == target) + ", visible " + (made != null && made.BodyVisible) + ", appears +" + (PathManager.Appears - a0) + ", body inside the target's footprint " + inside));
        // keys clicked into a keyboard draft raise OnDraftKey (the keyboard's cat plays the notes being placed)
        int ki = sm.AddKeyboardIsland(AnchorIndex(4), false);
        yield return Wait(0.6f);
        KeyBlock keys = ki >= 0 && ki < sm.Islands.Count ? sm.Islands[ki] : null;
        if (keys == null || !keys.IsKeyboard) { Add(L(false, "T2 draft keys event", "no keyboard")); yield break; }
        Vector3 k5, k9;
        var t5 = keys.GetTile(5, 0); var t9 = keys.GetTile(9, 0);
        if (!FrameKey(keys, t5, out k5) || !FrameKey(keys, t9, out k9)) { Add(L(false, "T2 draft keys event", "keys not pickable")); yield break; }
        FrameKey(keys, t5, out k5);
        pm.PickUpCube(1); pm.SetBrush(12);
        var got = new List<TileInteraction>();
        Action<TileInteraction> h = x => got.Add(x);
        PathManager.OnDraftKey += h;
        int e0 = PathManager.DraftKeyEvents, pr0 = PathManager.PressCount;
        yield return Click(k5);
        FrameKey(keys, t9, out k9);
        yield return Click(k9);
        PathManager.OnDraftKey -= h;
        bool keyed = got.Count == 2 && got[0] == t5 && got[1] == t9 && PathManager.DraftKeyEvents == e0 + 2 && PathManager.PressCount == pr0 && pm.Draft != null && pm.Draft.nodes.Count == 2;
        Add(L(keyed, "T2 keys clicked into a keyboard DRAFT raise PathManager.OnDraftKey (the cat plays the notes being placed; empty-hand presses stay OnTilePressed)",
              "events " + got.Count + " (" + (got.Count > 0 && got[0] == t5) + ", " + (got.Count > 1 && got[1] == t9) + "), presses +" + (PathManager.PressCount - pr0) + ", draft notes " + (pm.Draft != null ? pm.Draft.nodes.Count : -1)));
        pm.CancelPath();
        yield return Frames(2);
        PathManager.SimPos = Sea;
    }

    // ================================================================== 2. PhraseRoll on data (§14.4)
    static IEnumerator Roll()
    {
        int[] xs, zs, mods, durs;
        var n1 = new List<PhraseNote> { new PhraseNote(0, 2, 24), new PhraseNote(48, 5, 12) };
        PhraseRoll.Encode(n1, 96, 12, 8, out xs, out zs, out mods, out durs);
        var back = PhraseRoll.Decode(zs, mods, durs);
        bool enc = Ints(xs) == "0,2,4,5" && Ints(zs) == "2,2,5,5" && Ints(mods) == "0,1,0,1" && Ints(durs) == "24,24,12,36" && Notes(back) == Notes(n1);
        Add(L(enc, "R1 a phrase path = its notes in time order, a rest for each gap on the cell it starts (the previous row) and a rest to the phrase's end; decode gives the notes back",
              "xs " + Ints(xs) + " zs " + Ints(zs) + " mods " + Ints(mods) + " durs " + Ints(durs) + ", decoded " + Notes(back)));
        PhraseRoll.Encode(new List<PhraseNote> { new PhraseNote(24, 3, 12) }, 96, 12, 8, out xs, out zs, out mods, out durs);
        string lead = Ints(xs) + "|" + Ints(zs) + "|" + Ints(mods) + "|" + Ints(durs);
        PhraseRoll.Encode(new List<PhraseNote> { new PhraseNote(0, 1, 12) }, 384, 12, 32, out xs, out zs, out mods, out durs);
        string longRest = Ints(durs) + "|" + Ints(xs);
        PhraseRoll.Encode(new List<PhraseNote>(), 96, 12, 8, out xs, out zs, out mods, out durs);
        int empty = xs.Length;
        Add(L(lead == "0,2,3|3,3,3|1,0,1|24,12,60" && longRest == "12,192,180|0,1,17" && empty == 0,
              "R2 a leading gap rests on the first note's row; a long rest is split into nodes of at most 192 ticks (on the cells they start); no notes = no path",
              "leading " + lead + ", long " + longRest + ", empty " + empty));
        var r = new List<PhraseNote> { new PhraseNote(0, 2, 48) };
        bool c1 = PhraseRoll.Place(r, new PhraseNote(24, 4, 24)); string s1 = Notes(r);
        bool c2 = PhraseRoll.Place(r, new PhraseNote(24, 4, 24));
        var r2 = new List<PhraseNote> { new PhraseNote(0, 2, 24), new PhraseNote(24, 3, 24), new PhraseNote(48, 4, 24) };
        PhraseRoll.Place(r2, new PhraseNote(12, 6, 48)); string s2 = Notes(r2);
        PhraseRoll.Place(r2, new PhraseNote(0, 1, 96)); string s3 = Notes(r2);
        Add(L(c1 && s1 == "0:2x24 24:4x24" && !c2 && s2 == "0:2x12 12:6x48" && s3 == "0:1x96",
              "R3 a note placed over others replaces what it covers (the one it starts under is trimmed, the ones it covers go); the same note again changes nothing",
              "[0:2x48] + 24:4x24 -> " + s1 + " (again: changed " + c2 + "); [0,24,48] + 12:6x48 -> " + s2 + "; + 0:1x96 -> " + s3));
        int bpb = 4, mx = ProjectConfig.PhraseMaxBeats;
        int g1 = PhraseRoll.GrowBeats(4, 48, bpb, mx), g2 = PhraseRoll.GrowBeats(4, 49, bpb, mx), g3 = PhraseRoll.GrowBeats(2, 24, bpb, mx), g4 = PhraseRoll.GrowBeats(2, 25, bpb, mx);
        int g5 = PhraseRoll.GrowBeats(8, 144, bpb, mx), g6 = PhraseRoll.GrowBeats(8, 150, bpb, mx), g7 = PhraseRoll.GrowBeats(4, 200, bpb, mx), g8 = PhraseRoll.GrowBeats(32, 900, bpb, mx), g9 = PhraseRoll.GrowBeats(12, 250, bpb, mx);
        Add(L(g1 == 4 && g2 == 8 && g3 == 2 && g4 == 4 && g5 == 8 && g6 == 12 && g7 == 12 && g8 == 32 && g9 == 16,
              "R4 auto-grow: a note reaching into the LAST HALF MEASURE grows the phrase by a measure (½ → 1 → 2 → 3 …; a long note: until it fits), never past PhraseMaxBeats",
              "1 bar end 48 -> " + g1 + ", 49 -> " + g2 + "; ½ bar end 24 -> " + g3 + ", 25 -> " + g4 + "; 2 bars end 144 -> " + g5 + ", 150 -> " + g6 + "; 1 bar end 200 -> " + g7 + "; 8 bars -> " + g8 + "; 3 bars end 250 -> " + g9));
        var cs = new CubeState { instrument = 0, measure = 0, rests = new bool[0], xs = new int[0], zs = new int[0] };
        PhraseRoll.Encode(new List<PhraseNote> { new PhraseNote(0, 2, 24), new PhraseNote(72, 4, 48), new PhraseNote(144, 5, 24) }, 192, 12, 16, out xs, out zs, out mods, out durs);
        cs.xs = xs; cs.zs = zs; cs.mods = mods; cs.durs = durs; cs.rests = new bool[xs.Length];
        bool rs = PhraseRoll.Resize(cs, 96, 12, 8);
        string rsN = Notes(PhraseRoll.NotesOf(cs)); string rsD = Ints(cs.durs);
        Add(L(rs && rsN == "0:2x24 72:4x24" && rsD == "24,48,24" && cs.rests.Length == cs.xs.Length,
              "R5 Resize (O's length changes): notes past the new end go, a note crossing it is trimmed, the path re-padded to the end",
              "2 bars -> 1: notes " + rsN + ", durs " + rsD));
        yield break;
    }

    // ================================================================== 3. drawing on a phrase (§14.4)
    static IEnumerator Phrase()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        bool viaOps;
        int col = 1;
        int idx = MakePhrase(col, out viaOps);
        yield return Wait(0.5f);
        var kb = PhraseAt(col);
        Info("phrase fixture: " + (viaOps ? "SongOps.AddPhrase" : "the internal placement helper (O's AddPhrase not in yet)") + ", index " + idx + ", cells " + (kb != null ? kb.cols + "x" + kb.rows + " tiles " + kb.tiles.Count : "-"));
        if (kb == null || kb.tiles.Count == 0 || kb.tiles.Count != kb.cols * kb.rows || kb.cols < 8)
        {
            Add(L(false, "P phrase island with cells", kb == null ? "no phrase island" : "cells " + kb.cols + "x" + kb.rows + ", tiles " + kb.tiles.Count + " (B's phrase build)"));
            yield break;
        }
        int grid = kb.phraseGrid;
        int uPhrase = History.UndoCount;   // the phrase exists, no cube yet
        pm.PutDown();
        pm.PickUpCube(0);
        pm.SetBrush(24);
        GlobalClock.Stop();
        var cA = kb.GetTile(1, 4);
        if (!FrameTiles(kb, new List<TileInteraction> { kb.GetTile(0, 4), kb.GetTile(kb.cols - 1, 8), kb.GetTile(1, 4), kb.GetTile(4, 8) }, 62f))
        { Add(L(false, "P phrase cells pickable", "cells not pickable from the test camera")); yield break; }
        PathManager.SimPos = Scr(cA.Top);
        yield return Frames(4);
        float cw = Mathf.Abs(kb.transform.InverseTransformPoint(kb.GetTile(2, 4).Top).x - kb.transform.InverseTransformPoint(cA.Top).x);
        bool bar = pm.HandGhostTile == cA && pm.HandGhostTicks == 24 && Mathf.Abs(pm.HandGhostSize - 2f * cw) < 1e-3f;
        pm.SetBrush(48); yield return Frames(2);
        bool bar4 = Mathf.Abs(pm.HandGhostSize - 4f * cw) < 1e-3f;
        pm.SetBrush(24); yield return Frames(2);
        if (shots) { yield return Wait(0.3f); yield return Capture("d7_phrase_hologram"); }
        Add(L(bar && bar4, "P1 on a phrase the hologram is the note a click places: a bar from the hovered cell over the brush's cells (a quarter = 2 eighth cells, a half = 4)",
              "ghost on the cell " + (pm.HandGhostTile == cA) + ", cell " + F(cw, "F3") + " u, quarter bar " + F(2f * cw, "F3") + ", half " + (bar4 ? "ok" : "off")));

        // one stroke: press at cell 0 row 4, drag through its own note (nothing), on to cell 2 row 6, cell 4 row 8 (reaches into the last half
        // measure: the phrase grows to 2 measures mid-stroke), then cell 6 row 5 of the grown roll; release = ONE History entry
        int p0 = pushes, u0 = History.UndoCount, grows0 = PathManager.PhraseGrows, beats0 = kb.phraseBeats;
        int lateG = Synth.LateEvents;
        GlobalClock.Play(); yield return Wait(0.25f);
        pm.SimPointer(Scr(kb.GetTile(0, 4).Top), true, true, false);
        bool looping = !FocusLoop.Active && !GlobalClock.IsPlaying;   // §19.2: quiet — the playing song paused, no loop by itself
        pm.SimPointer(Scr(kb.GetTile(1, 4).Top), false, true, false);
        pm.SimPointer(Scr(kb.GetTile(2, 6).Top), false, true, false);
        Vector3 growPx = Scr(kb.GetTile(4, 8).Top);
        pm.SimPointer(growPx, false, true, false);
        var kb2 = PhraseAt(col);
        bool grewMid = kb2 != null && kb2.phraseBeats == 8 && PathManager.PhraseGrows == grows0 + 1 && pm.PhraseStroking;
        PathManager.SimPos = growPx;
        yield return Wait(0.7f);   // the grown roll re-lays (its new measure animates in); the stroke stays held
        kb2 = PhraseAt(col);
        if (kb2 != null && kb2.GetTile(6, 5) != null)
        {
            Physics.SyncTransforms();
            if (!FrameTiles(kb2, new List<TileInteraction> { kb2.GetTile(6, 5), kb2.GetTile(10, 2), kb2.GetTile(1, 7) }, 62f)) Info("the grown phrase's cells are not all pickable from the test camera");
            pm.SimPointer(Scr(kb2.GetTile(6, 5).Top), false, true, false);
            pm.SimPointer(Scr(kb2.GetTile(6, 5).Top), false, false, true);
        }
        else pm.SimPointer(Sea, false, false, true);
        yield return Frames(2);
        if (GlobalClock.IsPlaying) lateExcused += Synth.LateEvents - lateG;   // the stroke's rebuild while the loop plays (seek-keeping) — counted apart
        kb2 = PhraseAt(col);
        var cube = PathManager.PhraseCubeOf(kb2, 0);
        string want = "0:4x24 24:6x24 48:8x24 72:5x24";
        bool stroke = cube != null && Notes(PhraseRoll.NotesOf(cube)) == want && Xs(cube) == "0,2,4,6,8" && Zs(cube) == "4,6,8,5,5" && Mods(cube) == "0,0,0,0,1" && Durs(cube) == "24,24,24,24,96";
        Add(L(looping && grewMid && stroke && pushes == p0 + 1 && History.UndoCount == u0 + 1 && kb2 != null && kb2.phraseBeats == 8 && beats0 == 4 && !pm.PhraseStroking,
              "P2 a STROKE (press + drag) places a note at each cell reached after the last note's end (the brush's length, the pointer's row); the path = the notes in time order + a rest to the end; reaching into the last half measure GREW the phrase 1 → 2 measures mid-stroke (GrowPhrase); quiet (the song paused, no loop); ONE History entry",
              "looping " + looping + ", grew mid-stroke " + grewMid + " (" + beats0 + " -> " + (kb2 != null ? kb2.phraseBeats : -1) + " beats), notes " + (cube != null ? Notes(PhraseRoll.NotesOf(cube)) : "-") + ", xs " + Xs(cube) + " zs " + Zs(cube) + " mods " + Mods(cube) + " durs " + Durs(cube) + ", pushes +" + (pushes - p0) + ", undo +" + (History.UndoCount - u0)));
        if (cube == null || kb2 == null) yield break;

        // the draft row floats over the phrase being drawn (fill "4 / 8 beats"); Space = hear it (the phrase's loop) on / off
        yield return Frames(2);
        bool row = DurationPicker.Mode == DurationPicker.Where.Phrase && DurationPicker.PhraseIsland == kb2 && DurationPicker.FillText == "4 / 8 beats" && DurationPicker.LoopShown;
        Key(KeyCode.Space); yield return Frames(2);
        bool hear = FocusLoop.Active && FocusLoop.Phrase >= 0 && sm.Islands[FocusLoop.Phrase] == kb2 && GlobalClock.IsPlaying;
        Key(KeyCode.Space); yield return Frames(2);
        bool hearOff = !FocusLoop.Active && !GlobalClock.IsPlaying;
        Add(L(row && hear && hearOff, "P2b the draft row floats over the phrase being drawn (sizes, hear it, fill \"4 / 8 beats\"); Space plays the phrase's loop in context and stops it",
              "row mode " + DurationPicker.Mode + ", fill '" + DurationPicker.FillText + "', hear " + hear + ", off " + hearOff));
        // a click after a gap: a rest between; the next click (a half note) replaces what it covers
        GlobalClock.Stop();
        p0 = pushes;
        yield return Click(Scr(kb2.GetTile(10, 2).Top));
        kb2 = PhraseAt(col); cube = PathManager.PhraseCubeOf(kb2, 0);
        bool rest = cube != null && Mods(cube) == "0,0,0,0,1,0,1" && Durs(cube) == "24,24,24,24,24,24,48" && kb2.phraseBeats == 8 && pushes == p0 + 1;
        pm.SetBrush(48);
        yield return Click(Scr(kb2.GetTile(1, 7).Top));
        kb2 = PhraseAt(col); cube = PathManager.PhraseCubeOf(kb2, 0);
        string rep = cube != null ? Notes(PhraseRoll.NotesOf(cube)) : "-";
        Add(L(rest && rep == "0:4x12 12:7x48 72:5x24 120:2x24" && pushes == p0 + 2,
              "P3 a click after a gap leaves a rest between (no growth: it ends at the last half measure's start); a half note placed over others replaces what it covers (the note it starts under trimmed, the covered ones gone); one History entry each",
              "after the gap mods " + Mods(cube) + " durs " + Durs(cube) + ", after the half note " + rep + ", pushes +" + (pushes - p0)));

        // the melody card draws the phrase's notes; a right-click erases one; undo brings it back
        PathManager.SimPos = Scr(kb2.GetTile(3, 3).Top);
        yield return Frames(4);
        bool card = MelodyLine.PhraseMode && MelodyLine.PhraseIsland == kb2 && MelodyLine.Points.Count == 4 && MelodyLine.GhostPoints.Count == 1;
        string pts = ""; foreach (var p in MelodyLine.Points) pts += F(p.x, "F3") + "/" + F(p.y, "F2") + " ";
        if (shots) { yield return Wait(0.35f); yield return Capture("d7_phrase_stroke"); }
        p0 = pushes;
        pm.SimPointer(Scr(kb2.GetTile(6, 5).Top), false, false, false, true);
        yield return Frames(2);
        kb2 = PhraseAt(col); cube = PathManager.PhraseCubeOf(kb2, 0);
        string erased = cube != null ? Notes(PhraseRoll.NotesOf(cube)) : "-";
        int erasePushes = pushes - p0;   // (History.Undo below raises OnChanged too)
        History.Undo();
        yield return Frames(3);
        kb2 = PhraseAt(col); cube = PathManager.PhraseCubeOf(kb2, 0);
        string undone = cube != null ? Notes(PhraseRoll.NotesOf(cube)) : "-";
        Add(L(card && erased == "0:4x12 12:7x48 120:2x24" && erasePushes == 1 && undone == "0:4x12 12:7x48 72:5x24 120:2x24",
              "P4 the melody card draws the phrase's notes (x = start over the phrase, y = the row's pitch) + the hovered note as a ghost; a right-click (a cube in the hand) erases the note under it (one History entry); undo brings it back",
              "card " + MelodyLine.PhraseMode + " dots " + MelodyLine.Points.Count + " (" + pts + ") ghost " + MelodyLine.GhostPoints.Count + ", erased -> " + erased + " (pushes +" + erasePushes + "), undo -> " + undone));
        // §19.1 at the section's end: the phrase (from column 1 of a 4-measure section) cannot auto-grow to 4 measures — a note past its end is refused,
        // EXPAND adds a measure (a rebuild; the drawing goes on) and the note fits
        {
            kb2 = PhraseAt(col);
            int rf0 = PathManager.CapacityRefusals;
            pm.SetBrush(48);
            var last = kb2.GetTile(kb2.cols - 2, 3);
            FrameTiles(kb2, new List<TileInteraction> { last }, 62f);
            yield return Click(Scr(last.Top));
            kb2 = PhraseAt(col);
            bool refused = kb2.phraseBeats == 8 && pm.CapacityReached && PathManager.CapacityRefusals == rf0 + 1 && DurationPicker.ExpandShown;
            int e0 = PathManager.Expands; p0 = pushes;
            DurationPicker.SimClickExpand();
            yield return Wait(0.7f);
            kb2 = PhraseAt(col);
            bool expanded = kb2 != null && kb2.phraseBeats == 12 && PathManager.Expands == e0 + 1 && pushes == p0 + 1 && pm.DrawPhrase == kb2;
            var cell = kb2 != null ? kb2.GetTile(14, 3) : null;
            if (cell != null) { FrameTiles(kb2, new List<TileInteraction> { cell }, 62f); yield return Click(Scr(cell.Top)); }
            kb2 = PhraseAt(col); cube = PathManager.PhraseCubeOf(kb2, 0);
            bool fits = cube != null && Notes(PhraseRoll.NotesOf(cube)).Contains("168:3x48");
            Add(L(refused && expanded && fits, "P6 CAPACITY on a phrase: auto-grow stops at its section's end — a note past the phrase is refused (\"capacity reached\", EXPAND); EXPAND adds a measure (2 → 3, one History entry) and the note fits",
                  "refused " + refused + " (beats " + (kb2 != null ? kb2.phraseBeats : -1) + "), expanded " + expanded + ", notes " + (cube != null ? Notes(PhraseRoll.NotesOf(cube)) : "-")));
            for (int i = 0; i < 2; i++) History.Undo();
            yield return Frames(3);
        }
        // undoing the whole first stroke gives the 1-measure phrase back with no cube
        for (int i = 0; i < 12 && History.UndoCount > uPhrase; i++) History.Undo();
        yield return Frames(3);
        var kb3 = PhraseAt(col);
        Add(L(kb3 != null && kb3.phraseBeats == 4 && PathManager.PhraseCubeOf(kb3, 0) == null, "P5 undoing the first stroke restores the phrase before it (1 measure, no cube): the growth was part of that stroke's entry",
              "phrase beats " + (kb3 != null ? kb3.phraseBeats : -1) + ", cube " + (kb3 != null && PathManager.PhraseCubeOf(kb3, 0) != null)));
        PathManager.SimPos = Sea;
    }

    // ================================================================== 4. stairs runners (§5.3)
    static IEnumerator Runner()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        bool viaOps;
        int si = MakeStairs(AnchorIndex(2), out viaOps);
        yield return Wait(0.5f);
        KeyBlock st = null;
        foreach (var kb in sm.Islands) if (kb != null && kb.IsStairs) { st = kb; break; }
        Info("stairs fixture: " + (viaOps ? "SongOps.AddStairIsland" : "the internal placement helper (O's AddStairIsland not in yet)") + ", index " + si + ", steps " + (st != null ? st.tiles.Count : -1));
        if (st == null || st.tiles.Count < 3) { Add(L(false, "N stairs island with steps", st == null ? "no stairs island" : "steps " + st.tiles.Count + " (B's stairs build)")); yield break; }
        int n = st.tiles.Count;
        var steps = new List<TileInteraction>(); for (int i = 0; i < n; i++) steps.Add(st.GetTile(i, 0));
        GlobalClock.Stop();
        pm.PickUpCube(2); pm.SetBrush(12);   // a cube in the hand: steps win over a runner's body (the empty hand opens a runner by its body)
        if (!FrameTiles(st, steps, 50f)) { Add(L(false, "N steps pickable", "not from the test camera")); yield break; }
        PathManager.SimPos = Scr(steps[1].Top);
        yield return Frames(3);
        int pass = Mathf.RoundToInt(Mathf.Max(1, st.bars) * GlobalClock.BeatsPerBar * ProjectConfig.TicksPerBeat), fit = Mathf.Clamp(pass / 12, 1, n);
        bool ghost = pm.HandGhostTile == steps[1] && pm.HandGhostTicks == 12 && pm.StairGhostCount == fit;
        if (shots) { yield return Wait(0.3f); yield return Capture("d7_runner"); }
        int p0 = pushes, r0 = RunnerCount(st, 2);
        if (!viaOps) r0 = RunnerCount(st, 2);
        yield return Click(Scr(steps[1].Top));
        st = StairsOf(sm); var run = RunnerOf(st, 2);
        int[] xs, mods, durs; sm.StairPath(st, out xs, out mods, out durs);
        bool placed = run != null && Xs(run) == Ints(xs) && Mods(run) == Ints(mods) && Durs(run) == Ints(durs) && RunnerCount(st, 2) == 1 && pushes == p0 + 1;
        Add(L(ghost && placed, "N1 stairs: the hologram on the hovered step + ghost rings on the steps the run plays; a click with a cube in the hand places the group's RUNNER = SongManager.StairPath (one History entry)",
              "ghost " + ghost + " (rings " + pm.StairGhostCount + " / " + fit + "), runner xs " + Xs(run) + " mods " + Mods(run) + " durs " + Durs(run) + " vs StairPath " + Ints(xs) + " / " + Ints(mods) + " / " + Ints(durs) + ", runners of group 2 " + r0 + " -> " + RunnerCount(st, 2) + ", pushes +" + (pushes - p0)));
        // again = replaced (one per group per stairs) — with another sound of the group, so the replacement shows (an identical runner is no change:
        // History records nothing); another group adds its own
        p0 = pushes;
        int v = Instruments.VoiceCount(2) > 1 ? 1 : 0;
        pm.PickUpCube(2, v);
        PathManager.SimPos = Scr(steps[n - 1].Top); yield return Frames(2);
        yield return Click(Scr(steps[n - 1].Top));
        st = StairsOf(sm);
        int again = RunnerCount(st, 2); var r2 = RunnerOf(st, 2);
        int pushAgain = pushes - p0;
        pm.PickUpCube(3);
        yield return Click(Scr(steps[0].Top));
        st = StairsOf(sm);
        Add(L(again == 1 && r2 != null && r2.voice == v && pushAgain == (v != 0 ? 1 : 0) && RunnerCount(st, 3) == 1 && RunnerCount(st, 2) == 1 && pushes == p0 + pushAgain + 1,
              "N2 the same group again REPLACES its runner (one per group per stairs: now with the hand's sound); another group adds its own (one History entry each)",
              "group 2 runners after again " + again + " (voice " + (r2 != null ? r2.voice : -1) + " / " + v + ", pushes +" + pushAgain + "), group 3 " + RunnerCount(st, 3) + ", pushes +" + (pushes - p0)));
        // size first on stairs: the brush's nearest rate re-derives the run (SongOps.SetStair)
        p0 = pushes;
        pm.SetBrush(6);
        PathManager.SimPos = Scr(steps[1].Top); yield return Frames(2);
        int ghostRate = pm.HandGhostTicks;
        yield return Click(Scr(steps[1].Top));
        yield return Wait(0.3f);
        st = StairsOf(sm);
        run = RunnerOf(st, 3);
        sm.StairPath(st, out xs, out mods, out durs);
        bool rated = st.stairRate == 6 && run != null && Durs(run) == Ints(durs) && pushes == p0 + 1;
        Add(L(ghostRate == 6 && rated, "N3 size first on stairs: the hologram shows the run's rate nearest the brush (a 16th: 6 ticks); the click sets that rate (SongOps.SetStair) and every runner follows — one History entry",
              "ghost ticks " + ghostRate + ", stairs rate " + st.stairRate + ", runner durs " + Durs(run) + " vs StairPath " + Ints(durs) + ", pushes +" + (pushes - p0)));
        // the empty hand presses a step (no cube, no History); the steps were rebuilt by the rate change
        pm.PutDown();
        st = StairsOf(sm);
        var step2 = st != null ? st.GetTile(2, 0) : null;
        if (step2 == null) { Add(L(false, "N4 empty-hand press", "no step 2 after the rebuild")); yield break; }
        FrameTiles(st, new List<TileInteraction> { step2 }, 50f);
        int pr0 = PathManager.PressCount, cubes0 = SequenceMaster.Cubes.Count; p0 = pushes;
        yield return Click(Scr(step2.Top));
        Add(L(PathManager.PressCount == pr0 + 1 && SequenceMaster.Cubes.Count == cubes0 && pushes == p0 && pm.LastPressTile == StairsOf(sm).GetTile(2, 0),
              "N4 the empty hand PRESSES a step (its note, loud) and places nothing", "presses +" + (PathManager.PressCount - pr0) + ", cubes " + cubes0 + " -> " + SequenceMaster.Cubes.Count + ", pushes +" + (pushes - p0)));
        PathManager.SimPos = Sea;
    }

    static KeyBlock StairsOf(SongManager sm) { foreach (var kb in sm.Islands) if (kb != null && kb.IsStairs) return kb; return null; }
    static int RunnerCount(KeyBlock st, int inst) { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == st && c.instrument == inst && c.layer == 0) n++; return n; }
    static AudioCube RunnerOf(KeyBlock st, int inst) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == st && c.instrument == inst && c.layer == 0) return c; return null; }

    // ================================================================== 5. the Shift marquee (§16.1)
    static IEnumerator Marquee()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var A = sm.AnchorOf(0); var B = sm.AnchorOf(1);
        if (A == null || B == null) { Add(L(false, "M marquee", "no columns 0 / 1")); yield break; }
        // a view from above: platforms project to near-rectangles
        Vector3 mid = (A.VisualCenter + B.VisualCenter) * 0.5f;
        Frame(mid, 26f, 0f, 78f);
        yield return Frames(2);
        Vector3 a = Scr(A.VisualCenter) + new Vector3(-18f, -45f, 0f), b = Scr(B.VisualCenter) + new Vector3(18f, 45f, 0f);
        a.z = 0f; b.z = 0f;
        GridSelection.Clear();
        int mc0 = PathManager.MarqueeCount;
        pm.SimPointer(a, true, true, false, false, false, false, true);
        bool armed = pm.ShiftGesture && pm.currentState == PathManager.EditorState.Selecting;
        pm.SimPointer(Vector3.Lerp(a, b, 0.5f), false, true, false, false, false, false, true);
        bool drawing = pm.Marqueeing && pm.currentState == PathManager.EditorState.Selecting;
        pm.SimPointer(b, false, true, false, false, false, false, true);
        var live = new List<KeyBlock>(GridSelection.Selected);
        if (shots) { yield return Wait(0.25f); yield return Capture("d7_marquee"); }
        pm.SimPointer(b, false, false, true, false, false, false, true);
        yield return null;
        Rect rect = pm.MarqueeRect;
        var got = new List<KeyBlock>(GridSelection.Selected);
        var expect = ExpectedIn(rect);
        bool same = SameSet(got, expect) && SameSet(live, expect) && got.Contains(A) && got.Contains(B);
        Add(L(armed && drawing && same && PathManager.MarqueeCount == mc0 + 1 && pm.currentState == PathManager.EditorState.Idle && !pm.Marqueeing,
              "M1 Shift + drag draws the marquee (the camera state is Selecting all along: no pan) and selects — live and on release — exactly the grids whose platforms the screen rect touches (checked against an independent projection)",
              "armed " + armed + ", drawing " + drawing + ", rect " + rect + ", selected " + Names(got) + " (live " + Names(live) + "), expected " + Names(expect) + ", state after " + pm.currentState));
        // Shift + click toggles a grid; Esc clears; a plain click on the sea clears
        var C = sm.AnchorOf(Mathf.Min(3, sm.ColumnCount - 1));
        Frame(C.VisualCenter, 16f, 0f, 70f);
        yield return Frames(2);
        yield return Click(Scr(C.VisualCenter + Vector3.up * 0.05f), true);
        bool addedC = GridSelection.Contains(C);
        yield return Click(Scr(C.VisualCenter + Vector3.up * 0.05f), true);
        bool removedC = !GridSelection.Contains(C) && GridSelection.Contains(A);
        Key(KeyCode.Escape);
        yield return Frames(2);
        bool escCleared = !GridSelection.Any;
        GridSelection.Set(new List<KeyBlock> { A, B });
        yield return Click(Sea);
        yield return Frames(1);
        bool seaCleared = !GridSelection.Any;
        GridSelection.Set(new List<KeyBlock> { A });
        yield return Click(Sea, true);
        bool shiftSea = !GridSelection.Any;
        Add(L(addedC && removedC && escCleared && seaCleared && shiftSea, "M2 Shift + click toggles the grid under the pointer; Esc clears the selection; a click (or a Shift + click) on the empty sea clears it",
              "toggle in " + addedC + ", out " + removedC + ", Esc " + escCleared + ", sea " + seaCleared + ", shift-sea " + shiftSea));
        // a Moon only when the rectangle covers its centre
        int mi = sm.AddMoon(0);
        yield return Wait(0.6f);
        KeyBlock moon = mi >= 0 && mi < sm.Moons.Count ? sm.Moons[mi] : (sm.Moons.Count > 0 ? sm.Moons[sm.Moons.Count - 1] : null);
        if (moon != null)
        {
            Frame(moon.VisualCenter, 24f, 0f, 78f);
            yield return Frames(2);
            Vector3 mc = Scr(moon.VisualCenter); Vector3 edge = Scr(moon.VisualCenter + new Vector3(ProjectConfig.MoonRadius * 0.8f, 0f, 0f));
            float rpx = Mathf.Abs(edge.x - mc.x);
            var covering = pm.GridsInRect(new Rect(mc.x - 20f, mc.y - 20f, 40f, 40f));
            var rim = pm.GridsInRect(new Rect(mc.x + rpx * 0.5f, mc.y - 10f, rpx, 20f));
            Add(L(covering.Contains(moon) && !rim.Contains(moon), "M3 a Moon joins a marquee only when the rectangle covers its centre", "centre rect: " + covering.Contains(moon) + ", rim rect: " + rim.Contains(moon)));
        }
        else Info("M3 skipped: no Moon (AddMoon " + mi + ")");
        // a Shift + click on a section plinth selects the section (B's SectionPlinth); AddMoon rebuilt the islands
        A = sm.AnchorOf(0);
        var plinthType = Type.GetType("SectionPlinth");
        if (plinthType != null)
        {
            Frame(A.VisualCenter, 16f, 0f, 40f);
            yield return Frames(3);
            Vector3 hit = Vector3.zero; bool found = false;
            var cam = Camera.main;
            for (int y = 0; y < Screen.height && !found; y += 12)
            {
                Vector3 sp = new Vector3(Scr(A.VisualCenter).x, y, 0f);
                var r = pm.PickAt(sp);
                if (r.tile == null && r.island == null && r.cube == null && PathManager.PlinthUnder(cam.ScreenPointToRay(sp)) == 0) { hit = sp; found = true; }
            }
            if (found)
            {
                GridSelection.Clear();
                yield return Click(hit, true);
                var sec = new List<KeyBlock>(); for (int c = sm.SectionFirst(0); c <= sm.SectionLast(0); c++) foreach (var kb in sm.Islands) if (kb != null && kb.column == c && !kb.IsMoon) sec.Add(kb);
                Add(L(SameSet(new List<KeyBlock>(GridSelection.Selected), sec), "M4 Shift + click on a section plinth selects every grid of that section", "selected " + Names(new List<KeyBlock>(GridSelection.Selected)) + ", section 0 " + Names(sec)));
            }
            else Add(L(false, "M4 Shift + click on a section plinth", "no screen point over plinth 0 alone"));
        }
        else Info("M4 skipped: SectionPlinth (B) not in the tree yet");
        GridSelection.Clear();
    }

    /// <summary>The independent check: a grid is in when its platform's top face (or the face raised to its highest tile) projects to a quad whose
    /// screen AABB meets the rect AND some point of the quad lies in the rect or some rect corner lies in the quad (sampled edges).</summary>
    static List<KeyBlock> ExpectedIn(Rect r)
    {
        var sm = SongManager.I; var list = new List<KeyBlock>();
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || !kb.enabled) continue;
            var tr = kb.transform;
            Vector3 c = tr.InverseTransformPoint(kb.VisualCenter);
            float hw = kb.Width * 0.5f, hd = kb.Depth * 0.5f, high = -0.03f;
            foreach (var t in kb.tiles) if (t != null) high = Mathf.Max(high, tr.InverseTransformPoint(t.Top).y);
            if (FaceIn(tr, c, hw, hd, -0.03f, r) || FaceIn(tr, c, hw, hd, high, r)) list.Add(kb);
        }
        return list;
    }

    static bool FaceIn(Transform tr, Vector3 c, float hw, float hd, float y, Rect r)
    {
        // sample the face densely (21 x 21 points): a sample inside the rect = touching
        for (int i = 0; i <= 20; i++)
            for (int j = 0; j <= 20; j++)
            {
                Vector3 lp = new Vector3(c.x - hw + 2f * hw * i / 20f, y, c.z - hd + 2f * hd * j / 20f);
                Vector3 sp = Camera.main.WorldToScreenPoint(tr.TransformPoint(lp));
                if (sp.z > 0f && r.Contains(new Vector2(sp.x, sp.y))) return true;
            }
        return false;
    }

    static bool SameSet(List<KeyBlock> a, List<KeyBlock> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var k in a) if (!b.Contains(k)) return false;
        return true;
    }

    static string Names(List<KeyBlock> l) { var s = new StringBuilder("["); var sm = SongManager.I; for (int i = 0; i < l.Count; i++) { if (i > 0) s.Append(','); s.Append(sm.Islands.IndexOf(l[i])); } return s.Append(']').ToString(); }

    // ================================================================== 6. idle tiles sleep (B's perf ask)
    static IEnumerator Sleep()
    {
        var sm = SongManager.I;
        yield return Wait(1.2f);
        int total = 0, awake = 0;
        foreach (var kb in sm.Islands) if (kb != null) foreach (var t in kb.tiles) if (t != null) { total++; if (t.Animating) awake++; }
        var tile = sm.Islands[0].GetTile(1, 1);
        tile.SetHover(true);
        bool woke = tile.Animating;
        yield return Wait(0.8f);
        bool slept = !tile.Animating;
        float lift = tile.transform.localPosition.y;
        tile.SetHover(false);
        tile.PressLook(Color.white);
        bool woke2 = tile.Animating;
        yield return Wait(0.9f);
        bool slept2 = !tile.Animating && Mathf.Abs(tile.KeySink) < 1e-4f;
        Add(L(total > 0 && awake == 0 && woke && slept && woke2 && slept2, "Z idle tiles switch their Update off (a long phrase's cells cost nothing) and hover / press wake them until they settle",
              "awake at rest " + awake + " / " + total + ", hover woke " + woke + " then slept " + slept + " (lifted y " + F(lift, "F3") + "), press woke " + woke2 + " then slept " + slept2 + ", sleeps so far " + TileInteraction.Sleeps));
    }
}
