using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v6 §5.6 package H checks (Play mode, PathManager.AutoHand = false: the real game). <c>V6ChecksH.Run()</c> starts the timed checks; poll
/// <see cref="Done"/> or Captures/h6_report.txt. Sections: "hand" (an empty-hand click presses a tile — a loud note, the key sinks — and creates
/// nothing; a press that becomes a drag hands the island to IslandDrag and the drag moves it; a picked-up cube hovers as a hologram, a click draws, the
/// cube stays in the hand after finishing, Esc / a right-click on the sea put it down, a picked voice reaches the cube), "keyboard" (on a keyboard
/// island a click adds exactly the clicked key, the last key again repeats it, a drag adds a glissando run, the melody line follows, the done tick
/// finishes; the inspector's grid shows keys and a repeated note's beads apart; the empty hand plays keys, Alt + drag a glissando), "paste" (the
/// adapted pitches equal Harmony's answer for grid → grid, the bass rule, keyboard → keyboard and grid → keyboard; PasteNext's target; one History
/// entry and undo; v7 §21: the new cube appears on its grid at once (no flight); a paste while playing sounds at once; the pattern hand's ghost = the paste; an
/// adapted stamp), "carry" (a carried window's tiles are the pattern adapted hop by hop, cached — v7 §21.3: the long grid's pedal rule; v7 §21:
/// the cube steps into each next measure of its long grid with an ordinary hop, never a flight; v7 §12.1: after its last carried window it HOLDS
/// on that grid (v6 flew home at once) and a stop sends it home in real time), "moon" (a drum cube stays on its Moon while the Moon glides
/// with the song, SPEC §11). The fixture is loaded (never the user's save); the saves are put back as they were. Synth late / errors must not
/// move; a late event within 0.35 s of the test's own Pause → Seek → Play or around a mid-play capture is reported and counted apart. Captures:
/// h6_hand_hologram, h6_keyboard_melody, h6_keyboard_grid, h6_key_press, h6_pattern_ghost, h6_carry_flight.
/// </summary>
public static class V6ChecksH
{
    public static string Report = "";
    public static bool Done = true;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "h6_report.txt");

    static StringBuilder sb;
    static int pass, fail, pushes, handEvents, lateExcused;
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
    static void OnHand() { handEvents++; }
    static string F(double v, string f = "F2") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static string Ints(IList<int> a) { if (a == null) return "null"; var s = new StringBuilder(); for (int i = 0; i < a.Count; i++) { if (i > 0) s.Append(','); s.Append(a[i]); } return s.ToString(); }
    static string Midis(IList<TileInteraction> t) { var s = new StringBuilder(); for (int i = 0; i < t.Count; i++) { if (i > 0) s.Append(','); s.Append(t[i] != null ? t[i].midi.ToString() : "-"); } return s.ToString(); }

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray closed, the real mouse ignored, the Keys brush, an
    /// empty hand, AutoHand off).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = Sea;
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
        AudioCube.SnapAllHome();   // v7: a stop / reset now travels home in real time; a fresh fixture starts home at once
    }

    static void Frame(Vector3 focus, float dist, float yaw = 0f, float pitch = 58f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    static void Key(KeyCode k)
    {
        KeyShim.Sim(k, true, true, false);
        PathManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static IEnumerator Click(Vector3 screen)
    {
        var pm = PathManager.I;
        pm.SimPointer(screen, true, true, false);
        pm.SimPointer(screen, false, false, true);
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

    /// <summary>The cube on island <paramref name="islandIdx"/> with this instrument and these grid positions (cubes are re-created by undo and
    /// structural edits); null when gone.</summary>
    static AudioCube FindCube(int islandIdx, int instrument, List<int> xs, List<int> zs)
    {
        var sm = SongManager.I;
        if (islandIdx < 0 || islandIdx >= sm.Islands.Count) return null;
        var kb = sm.Islands[islandIdx];
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.Island != kb || c.instrument != instrument || c.nodes.Count != xs.Count) continue;
            bool ok = true;
            for (int i = 0; i < xs.Count && ok; i++) ok = c.nodes[i].gridX == xs[i] && c.nodes[i].gridZ == zs[i];
            if (ok) return c;
        }
        return null;
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
            if (kb == null || kb.IsMoon || kb.IsKeyboard || kb.column == exceptColumn) continue;
            int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) n++;
            if (n < bestCubes) { bestCubes = n; best = kb; }
        }
        return best;
    }

    /// <summary>Frames a keyboard so every key picks itself; false when some key does not.</summary>
    static bool FrameKeys(KeyBlock kb)
    {
        float[] dists = { 11f, 13f, 15f, 17f };
        foreach (float d in dists)
        {
            Frame(kb.VisualCenter + Vector3.up * 0.3f, d, 0f, 56f);
            bool ok = true;
            foreach (var t in kb.tiles) { var r = PathManager.I.PickAt(Scr(t.Top)); if (r.tile != t) { ok = false; break; } }
            if (ok) return true;
        }
        return false;
    }

    static AudioCube CubeOn(KeyBlock kb, int minNodes, bool pitched)
    {
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && c.Island == kb && !c.IsOnMoon && !c.rider && c.nodes.Count >= minNodes && (!pitched || !c.IsDrums)) return c;
        return null;
    }

    /// <summary>A finished cube built from a state (instrument, grid positions, lengths) on island <paramref name="kb"/>.</summary>
    static AudioCube Make(KeyBlock kb, int instrument, int[] xs, int[] zs, int[] durs)
    {
        var s = new CubeState { instrument = instrument, measure = SongManager.I.Islands.IndexOf(kb), xs = xs, zs = zs, rests = new bool[xs.Length], mods = new int[xs.Length], durs = durs, volume = 1f, moon = -1 };
        var c = PathManager.I.RestoreCube(s);
        SequenceMaster.RecalculateTimeline();
        return c;
    }

    // ================================================================== entry points
    public static string Run(bool captures = true) => Start(null, captures);
    /// <summary>Some sections (a comma list of "hand", "keyboard", "paste", "carry", "moon").</summary>
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

    static readonly string[] Sections = { "hand", "keyboard", "paste", "carry", "moon" };

    static IEnumerator Routine(string only)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0; handEvents = 0; lateExcused = 0; lateAtPlay = 0;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float bpm0 = GlobalClock.BPM;
        bool auto0 = PathManager.AutoHand;
        History.OnChanged += OnHistory;
        PathManager.OnHandChanged += OnHand;
        foreach (var s in Sections)
        {
            if (only != null && Array.IndexOf(only.Split(','), s) < 0) continue;
            Prepare(); LoadFixture(); yield return null; yield return null;
            Progress = s;
            IEnumerator body = null;
            switch (s)
            {
                case "hand": body = Hand(); break;
                case "keyboard": body = Keyboard(); break;
                case "paste": body = Paste(); break;
                case "carry": body = Carry(); break;
                case "moon": body = MoonRide(); break;
            }
            int lateS = Synth.LateEvents;
            if (body != null) yield return Guard(body);
            if (Synth.LateEvents != lateS) Info("section " + s + ": synth late +" + (Synth.LateEvents - lateS) + " (last: " + Synth.LastLate + ")");
            if (PathManager.I.IsDrawing) PathManager.I.CancelPath(false);
            PathManager.I.PutDown();
            CubeInspector.CloseImmediate();
            FocusLoop.Dismiss(); GlobalClock.Stop();
            if (GlobalClock.BPM != bpm0) GlobalClock.SetBPM(bpm0);
        }
        Progress = "finish";
        Prepare(); LoadFixture();
        Clipboard.Clear();   // a pattern left on the clipboard would show U2's clipboard chip in the next suite
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        PathManager.I.PutDown();
        PathManager.AutoHand = auto0;
        History.OnChanged -= OnHistory;
        PathManager.OnHandChanged -= OnHand;
        V3Fixes.RestoreSaves(saves);
        Add(L(Synth.LateEvents - lateExcused == late0 && Synth.Errors == err0, "RUN Synth late / errors unchanged (counted apart: " + lateExcused + " around mid-play captures or test play restarts)", Synth.Stats()));
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

    // ================================================================== 1. the hand (placing mode)
    static IEnumerator Hand()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var kb = FewestCubes();
        int z, x0;
        if (!PickableRow(kb, 4, out z, out x0)) { Add(L(false, "H hand", "no free pickable row of 4")); yield break; }
        var a = kb.GetTile(x0, z); var b = kb.GetTile(x0 + 1, z); var c = kb.GetTile(x0 + 2, z); var d = kb.GetTile(x0 + 3, z);

        // an EMPTY hand: a click presses the tile (a loud note at once, the key sinks) and creates nothing
        int cubes0 = SequenceMaster.Cubes.Count, undo0 = History.UndoCount, p0 = pushes, press0 = PathManager.PressCount, sp0 = Synth.PressCount;
        pm.SimPointer(Scr(a.Top), true, true, false);
        float sinkNow = a.KeySink;
        pm.SimPointer(Scr(a.Top), false, false, true);
        yield return null;
        float sink1 = a.KeySink;
        var e = TileInteraction.LastPress;
        int slot = Instruments.SlotOf(pm.selectedInstrument, Instruments.BrushVoiceOf(pm.selectedInstrument));
        var want = VoiceRules.PressEvent(slot, a, 0, pm.PressSeconds);
        bool nothing = SequenceMaster.Cubes.Count == cubes0 && History.UndoCount == undo0 && pushes == p0 && !pm.IsDrawing && pm.Hand == PathManager.HandKind.Empty;
        bool pressed = PathManager.PressCount == press0 + 1 && Synth.PressCount == sp0 + 1 && pm.LastPressTile == a;
        bool loud = e.slot == slot && e.midi == want.midi && e.vel >= Synth.PressMinVelocity && e.onDsp == 0.0 && Synth.LastPressVelocity >= Synth.PressMinVelocity;
        bool look = sink1 > 0.02f || sinkNow > 0.0f;
        bool header = UIManager.I != null && UIManager.I.SelectedMeasure == sm.Islands.IndexOf(kb);
        Add(L(nothing && pressed && loud && look && header,
              "H1 EMPTY hand: a click on a tile PRESSES it (Synth.PressNote at once, a strong velocity, the tile's pitch; the key sinks) and creates nothing (no cube, no History); the island's header shows",
              "cubes " + cubes0 + "->" + SequenceMaster.Cubes.Count + ", undo " + undo0 + "->" + History.UndoCount + ", presses +" + (PathManager.PressCount - press0) + " (synth +" + (Synth.PressCount - sp0) + "), note slot " + e.slot + " midi " + e.midi + " (want " + want.midi + ") vel " + e.vel + " on " + F(e.onDsp) + ", sink " + F(sink1, "F3") + ", selected island " + (UIManager.I != null ? UIManager.I.SelectedMeasure : -9) + "/" + sm.Islands.IndexOf(kb)));

        // while the song plays the press still sounds at once (not on the next 16th) and loud
        GlobalClock.Play();
        yield return Wait(0.4f);
        int sp1 = Synth.PressCount;
        Synth.ResetDuckMin();
        yield return Click(Scr(b.Top));
        var e2 = TileInteraction.LastPress;
        yield return Wait(0.15f);
        float duckMin = Synth.DuckMin, boostMax = Synth.BoostMax;   // A's press sizing on the audio thread: the song ducks, the press is boosted
        Add(L(Synth.PressCount == sp1 + 1 && e2.onDsp == 0.0 && e2.vel >= Synth.PressMinVelocity && SequenceMaster.Cubes.Count == cubes0 && duckMin <= 0.22f && boostMax >= 1.5f,
              "H1 playing: an empty-hand press sounds at once over the song, loud (the song ducks about -15 dB, the press bus is boosted), still no cube",
              "presses +" + (Synth.PressCount - sp1) + ", on " + F(e2.onDsp) + ", vel " + e2.vel + ", song gain min " + F(duckMin, "F3") + " (" + F(20f * Mathf.Log10(Mathf.Max(1e-4f, duckMin)), "F1") + " dB), press boost max " + F(boostMax, "F2") + " (+" + F(20f * Mathf.Log10(Mathf.Max(1e-4f, boostMax)), "F1") + " dB)"));
        GlobalClock.Stop();
        yield return null;

        // a press that becomes a drag hands the island to IslandDrag; driven, the drag moves the island (one History entry)
        int h0 = PathManager.DragHandovers, pr1 = PathManager.PressCount;
        Vector3 sb0 = Scr(b.Top);
        pm.SimPointer(sb0, true, true, false);
        pm.SimPointer(sb0 + new Vector3(OrbitCamera.DragThresholdPx * 3f, 0f, 0f), false, true, false);
        bool armed = PathManager.DragHandovers == h0 + 1 && pm.Drag.State == IslandDrag.Phase.Armed && pm.Drag.Block == kb;
        string armedAt = pm.Drag.State + " on island " + (pm.Drag.Block == kb);
        int idx = sm.Islands.IndexOf(kb);
        float pz0 = kb.pz; int undo1 = History.UndoCount;
        var ray = Camera.main.ScreenPointToRay(sb0); float enter; var ground = new Plane(Vector3.up, Vector3.zero).Raycast(ray, out enter) ? ray.GetPoint(enter) : kb.Center;
        pm.Drag.Cancel();
        pm.SimPointer(sb0, false, false, true);
        pm.Drag.SimBegin(kb, false, ground);
        bool dragging = pm.Drag.Dragging;
        pm.Drag.SimMove(ground + new Vector3(0f, 0f, 2.5f));
        yield return null;
        pm.Drag.SimMove(ground + new Vector3(0f, 0f, 2.5f));
        pm.Drag.SimRelease();
        yield return Wait(0.5f);
        var moved = idx >= 0 && idx < sm.Islands.Count ? sm.Islands[idx] : null;
        float pz1 = moved != null ? moved.pz : float.NaN;
        bool moves = dragging && (History.UndoCount == undo1 + 1 || Mathf.Abs(pz1 - pz0) > 0.4f);
        Add(L(armed && moves && SequenceMaster.Cubes.Count == cubes0,
              "H2 EMPTY hand: a press on a tile that becomes a drag hands the island to IslandDrag (armed at the press point) and the drag moves the island",
              "handovers +" + (PathManager.DragHandovers - h0) + ", then " + armedAt + ", presses +" + (PathManager.PressCount - pr1) + ", drag began " + dragging + ", pz " + F(pz0) + " -> " + F(pz1) + ", undo " + undo1 + "->" + History.UndoCount));
        if (History.UndoCount > undo1) { History.Undo(); yield return Wait(0.3f); }
        kb = idx >= 0 && idx < sm.Islands.Count ? sm.Islands[idx] : kb;
        if (!PickableRow(kb, 4, out z, out x0)) { Add(L(false, "H hand", "no free pickable row of 4 after the drag")); yield break; }
        a = kb.GetTile(x0, z); b = kb.GetTile(x0 + 1, z); c = kb.GetTile(x0 + 2, z); d = kb.GetTile(x0 + 3, z);

        // pick up a cube: its hologram on the hovered free tile (the colour, the brush length's size); a click draws; the cube stays in the hand
        int he0 = handEvents;
        pm.PickUpCube(2);
        PathManager.SimPos = Scr(a.Top);
        yield return Frames(3);
        yield return Wait(0.2f);
        var gh = pm.HandGhost;
        float size = ProjectConfig.CubeSize * AudioCube.SizeOf(pm.BrushTicks / (float)ProjectConfig.TicksPerBeat);
        bool ghost = pm.Hand == PathManager.HandKind.Cube && pm.CubeInHand && handEvents == he0 + 1 && pm.HandGhostTile == a && gh != null && gh.gameObject.activeSelf
                     && gh.position.y > a.Top.y + size * 0.3f && Mathf.Abs(gh.position.x - a.Top.x) < 0.05f && Mathf.Abs(gh.localScale.x - size) < size * 0.2f;
        Add(L(ghost, "H3 a CUBE in the hand: the hovered free tile shows a hologram cube sitting on it (the brush length's size)",
              "hand " + pm.Hand + ", events +" + (handEvents - he0) + ", ghost tile ok " + (pm.HandGhostTile == a) + ", ghost y " + (gh != null ? F(gh.position.y) : "-") + " over tile top " + F(a.Top.y) + ", scale " + (gh != null ? F(gh.localScale.x) : "-") + " want " + F(size)));
        if (shots)
        {
            var keep = Camera.main.transform.position; var keepR = Camera.main.transform.rotation;
            Frame(a.Top + Vector3.up * 0.4f, 6.5f, -24f, 38f);
            PathManager.SimPos = Scr(a.Top); yield return Frames(4);
            yield return Wait(0.2f);
            yield return Capture("h6_hand_hologram");
            Camera.main.transform.position = keep; Camera.main.transform.rotation = keepR;
            PathManager.SimPos = Scr(a.Top); yield return Frames(2);
        }
        int cubes1 = SequenceMaster.Cubes.Count, p1 = pushes;
        yield return Click(Scr(a.Top));
        var draft = pm.Draft;
        bool drawing = pm.IsDrawing && draft != null && draft.instrument == 2 && draft.voice == Instruments.BrushVoiceOf(2) && pm.HandGhostTile == null;
        PathManager.SimPos = Scr(c.Top); yield return Frames(2);
        yield return Click(Scr(c.Top));
        int nodes = draft != null ? draft.nodes.Count : -1;
        Key(KeyCode.Return);
        yield return null;
        bool finished = !pm.IsDrawing && draft != null && draft.isFinalized && SequenceMaster.Cubes.Count == cubes1 + 1 && pushes >= p1 + 1 && pm.Hand == PathManager.HandKind.Cube;
        Add(L(drawing && nodes == 3 && finished, "H3 a click with the cube in hand draws exactly as v5 (the run to a farther tile); Enter finishes (one cube, History) and the cube STAYS in the hand",
              "draft " + (draft != null) + " instrument " + (draft != null ? draft.instrument : -1) + " voice " + (draft != null ? draft.voice : -1) + ", nodes " + nodes + ", cubes " + cubes1 + "->" + SequenceMaster.Cubes.Count + ", pushes +" + (pushes - p1) + ", hand " + pm.Hand));
        FocusLoop.Dismiss(); GlobalClock.Stop();   // the drawn cube stops hopping over the next tiles
        yield return Wait(0.2f);
        // a second path at once (the hand still holds the cube); Esc while drawing only cancels the draft
        PathManager.SimPos = Scr(d.Top); yield return Frames(2);
        yield return Click(Scr(d.Top));
        bool again = pm.IsDrawing;
        Key(KeyCode.Escape); yield return null; yield return null;
        bool cancelKeeps = !pm.IsDrawing && pm.Hand == PathManager.HandKind.Cube;
        // Esc with nothing else to close puts the hand down (LateUpdate)
        int he1 = handEvents;
        yield return Wait(0.35f);   // the dissolve's frames pass (Esc owners of the last frame)
        Key(KeyCode.Escape); yield return null; yield return null;
        bool escDown = pm.Hand == PathManager.HandKind.Empty && handEvents == he1 + 1;
        // a right-click on the empty sea puts it down too; the held chip again is U2's (PutDown)
        pm.PickUpCube(0);
        if (OrbitCamera.I != null) OrbitCamera.I.SimRightPress();   // a real right press resets the camera's last right-drag (an earlier suite's orbit)
        pm.SimPointer(Sea, false, false, false, true);
        bool rightDown = pm.Hand == PathManager.HandKind.Empty;
        Add(L(again && cancelKeeps && escDown && rightDown, "H4 the hand is a tool: a second path starts at once; Esc cancels a draft (the hand keeps the cube), Esc again puts it down, a right-click on the empty sea too",
              "again " + again + ", cancel keeps " + cancelKeeps + ", esc down " + escDown + " (events +" + (handEvents - he1) + "), right-click down " + rightDown));

        // a picked VOICE: PickUpCube(group, voice) sets the brush voice; the new cube sounds it
        FocusLoop.Dismiss(); GlobalClock.Stop();
        yield return Wait(0.2f);
        int g = 5, v = Mathf.Min(2, Instruments.VoiceCount(5) - 1);
        pm.PickUpCube(g, v);
        PathManager.SimPos = Scr(b.Top); yield return Frames(2);
        yield return Click(Scr(b.Top));
        var vd = pm.Draft;
        Key(KeyCode.Return); yield return null;
        bool voiced = Instruments.BrushVoiceOf(g) == v && vd != null && vd.voice == v && vd.ToState().voice == v;
        Add(L(voiced, "H5 PickUpCube(group, voice) makes that voice the brush: the drawn cube carries it (state round trip)",
              "brush voice " + Instruments.BrushVoiceOf(g) + " want " + v + " (of " + Instruments.VoiceCount(g) + "), cube voice " + (vd != null ? vd.voice : -1)));
        pm.PutDown();
        PathManager.SimPos = Sea; yield return Frames(2);
    }

    // ================================================================== 2. keyboard drawing
    static IEnumerator Keyboard()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        int p0 = pushes;
        int ki = sm.AddKeyboardIsland(1, true);
        yield return Wait(0.8f);
        KeyBlock kb = ki >= 0 && ki < sm.Islands.Count ? sm.Islands[ki] : null;
        if (kb == null || !kb.IsKeyboard) { Add(L(false, "K keyboard", "AddKeyboardIsland gave " + ki)); yield break; }
        Info("keyboard island " + ki + ": " + kb.cols + " keys, lowest " + kb.GetTile(0, 0).midi + ", column " + kb.column + ", pushes +" + (pushes - p0));
        if (!FrameKeys(kb)) { Add(L(false, "K keyboard", "the keys do not all pick themselves from any view")); yield break; }
        Func<int, TileInteraction> K = k => kb.GetTile(k, 0);
        pm.PickUpCube(3);
        pm.BrushTicks = 6;   // 16ths: seven notes stay inside the island's bar (no column fit after the finish)
        yield return Click(Scr(K(3).Top));
        var dr = pm.Draft;
        if (dr == null) { Add(L(false, "K keyboard", "no draft on key 3")); yield break; }
        // a click adds EXACTLY the clicked key (no run: melodies leap)
        PathManager.SimPos = Scr(K(10).Top); yield return Frames(2);
        bool single = pm.PreviewTiles.Count == 1 && pm.PreviewTiles[0] == K(10) && pm.LastAuditionTiles.Count == 1 && pm.LastAuditionTiles[0] == K(10);
        int ka0 = PathManager.KeyAdds;
        yield return Click(Scr(K(10).Top));
        bool leap = dr.nodes.Count == 2 && dr.nodes[1] == K(10) && PathManager.KeyAdds == ka0 + 1;
        Add(L(single && leap, "K1 keyboard: hovering a key previews / auditions that single key; a click adds exactly it (key 3 -> key 10: no in-between keys)",
              "preview " + pm.PreviewTiles.Count + " audition " + pm.LastAuditionTiles.Count + ", nodes " + Midis(dr.nodes)));
        // the last key again: a repeated note (the beads stand apart along the key)
        yield return Click(Scr(K(10).Top));
        bool repeat = dr.nodes.Count == 3 && dr.nodes[2] == K(10) && pm.IsDrawing;
        float apart = (dr.BeadPosition(1) - dr.BeadPosition(2)).magnitude;
        Add(L(repeat && apart >= AudioCube.RepeatStep * 0.9f, "K2 keyboard: a click on the draft's last key adds a REPEATED note (the draft goes on; its beads stand apart along the key)",
              "nodes " + Midis(dr.nodes) + ", drawing " + pm.IsDrawing + ", bead gap " + F(apart, "F3")));
        // a drag across keys adds each key it enters (a glissando run); its release does not finish the melody
        pm.SimPointer(Scr(K(12).Top), true, true, false);
        pm.SimPointer(Scr(K(15).Top), false, true, false);
        pm.SimPointer(Scr(K(15).Top), false, false, true);
        yield return null;
        var want = new[] { K(3), K(10), K(10), K(12), K(13), K(14), K(15) };
        bool gliss = dr.nodes.Count == want.Length && pm.IsDrawing;
        for (int i = 0; gliss && i < want.Length; i++) gliss = dr.nodes[i] == want[i];
        Add(L(gliss, "K3 keyboard: a drag from key 12 to key 15 adds 12 then the run 13 14 15 (a glissando); the release keeps drawing",
              "nodes " + Midis(dr.nodes) + ", drawing " + pm.IsDrawing));
        // the melody line draws the tune: one dot per note, higher notes higher, the repeat at the same height, time running right
        PathManager.SimPos = Sea; yield return Frames(3);
        var pts = MelodyLine.Points;
        bool shape = MelodyLine.Shown && pts.Count == dr.nodes.Count;
        for (int i = 0; shape && i < pts.Count; i++)
            for (int j = 0; shape && j < pts.Count; j++)
            {
                int mi = dr.nodes[i].midi, mj = dr.nodes[j].midi;
                if (mi < mj && !(pts[i].y < pts[j].y)) shape = false;
                if (mi == mj && Mathf.Abs(pts[i].y - pts[j].y) > 1e-4f) shape = false;
                if (i < j && !(pts[i].x < pts[j].x)) shape = false;
            }
        Add(L(shape, "K4 keyboard: the melody line has a dot per note (7, the repeat included), pitch order = height order, time running right",
              "shown " + MelodyLine.Shown + ", points " + pts.Count));
        if (shots)
        {
            var keep = Camera.main.transform.position; var keepR = Camera.main.transform.rotation;
            Frame(kb.VisualCenter + new Vector3(0f, 0.2f, 0.6f), 10.5f, -8f, 44f);
            PathManager.SimPos = Scr(K(17).Top); yield return Frames(4);
            yield return Wait(0.3f);
            yield return Capture("h6_keyboard_melody");
            Camera.main.transform.position = keep; Camera.main.transform.rotation = keepR;
            PathManager.SimPos = Sea; yield return Frames(2);
        }
        // the done tick finishes (one History entry); the cube stays in the hand
        int cubes0 = SequenceMaster.Cubes.Count, p1 = pushes;
        bool doneShown = DurationPicker.DoneShown;
        DurationPicker.SimClickDone();
        yield return null;
        bool done = !pm.IsDrawing && dr.isFinalized && SequenceMaster.Cubes.Count == cubes0 && pushes == p1 + 1 && pm.CubeInHand;
        Add(L(doneShown && done, "K5 keyboard: the length row's done tick finishes the melody (one History entry); the cube stays in the hand",
              "tick shown " + doneShown + ", finalized " + dr.isFinalized + ", pushes +" + (pushes - p1) + ", hand " + pm.Hand));
        yield return Wait(0.3f);
        float wantBody = ProjectConfig.CubeSize * dr.SizeTarget * AudioCube.KeyFoot;
        Add(L(Mathf.Abs(dr.Foot - AudioCube.KeyFoot) < 0.01f && dr.transform.localScale.x < ProjectConfig.CubeSize * dr.SizeTarget * 0.8f,
              "K5 a cube on the keys wears the smaller keyboard footprint (KeyFoot; its size still follows the note length)",
              "foot " + F(dr.Foot) + ", body " + F(dr.transform.localScale.x, "F3") + " u (about " + F(wantBody, "F3") + ", a grid would show " + F(ProjectConfig.CubeSize * dr.SizeTarget, "F3") + ")"));
        yield return Wait(0.3f);
        // the inspector's grid shows one row of keys; the repeated note's beads stand apart and each is its own bead (a click rests only it)
        var cube = pm.selectedCube != null ? pm.selectedCube : dr;
        foreach (var cc in SequenceMaster.Cubes) if (cc != null && cc.Island == kb && cc.nodes.Count == 7) cube = cc;
        pm.PutDown();
        CubeInspector.Open(cube);
        yield return Wait(0.8f);
        var grid = InspectorCard.Grid;
        int bk = -1, wk = -1;
        for (int k = 0; k < kb.cols; k++) { if (KeyBlock.IsBlackKeyMidi(kb.GetTile(k, 0).midi)) bk = k; else wk = k; }
        bool gridKeys = grid != null && grid.IsKeys && grid.Cols == 25 && grid.Rows == 1 && bk >= 0 && wk >= 0 && grid.KeyRect(bk).height < grid.KeyRect(wk).height;
        bool beadsApart = grid != null && grid.BeadPositions.Count == 7 && (grid.BeadPositions[1] - grid.BeadPositions[2]).magnitude >= PathGridView.KeyBeadStep * 0.9f;
        int mods1 = cube.ModOf(1);
        int gp = pushes;
        if (grid != null) { grid.SimDownNode(2); grid.SimUp(); }
        yield return null;
        bool restOne = cube.ModOf(2) == 1 && cube.ModOf(1) == mods1 && pushes == gp + 1;
        if (shots) { yield return Wait(0.3f); yield return Capture("h6_keyboard_grid"); }
        Add(L(gridKeys && beadsApart && restOne, "K6 the inspector's grid shows the keyboard as one row of keys (black keys shorter); the repeated note's beads stand apart and a click rests only that node",
              "keys " + gridKeys + " (" + (grid != null ? grid.Cols + "x" + grid.Rows : "-") + "), bead gap " + (grid != null && grid.BeadPositions.Count > 2 ? F((grid.BeadPositions[1] - grid.BeadPositions[2]).magnitude) : "-") + ", rest node2 " + cube.ModOf(2) + " node1 " + cube.ModOf(1)));
        CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop();
        yield return Wait(0.3f);
        // the empty hand plays keys (a keyboard key keeps its octave); Alt + drag plays a glissando; nothing is created
        FrameKeys(kb);
        int c0 = SequenceMaster.Cubes.Count, gl0 = PathManager.GlissCount, pr0 = PathManager.PressCount, h0 = PathManager.DragHandovers;
        if (shots)
        {
            // the press look (the key sinks, springs back, a ring spreads): a capture a frame after a press on a key near 7 that picks itself
            var keep = Camera.main.transform.position; var keepR = Camera.main.transform.rotation;
            Frame(K(7).Top + new Vector3(0f, 0.1f, 0.4f), 5.5f, -18f, 40f);
            yield return null;
            TileInteraction pk = null;
            for (int k = 7; k <= 12 && pk == null; k++) { var r = pm.PickAt(Scr(K(k).Top)); if (r.tile == K(k)) pk = K(k); }
            if (pk != null)
            {
                pm.SimPointer(Scr(pk.Top), true, true, false); pm.SimPointer(Scr(pk.Top), false, false, true);
                yield return null;
                Info("key " + pk.gridX + " pressed: " + (pm.LastPressTile == pk) + ", its sink a frame later: " + F(pk.KeySink, "F3") + " u (depth " + F(TileInteraction.KeyDepth) + ")");
                yield return Capture("h6_key_press");
            }
            Camera.main.transform.position = keep; Camera.main.transform.rotation = keepR;
            yield return Wait(0.6f);
            c0 = SequenceMaster.Cubes.Count; gl0 = PathManager.GlissCount; pr0 = PathManager.PressCount; h0 = PathManager.DragHandovers;
        }
        yield return Click(Scr(K(5).Top));
        var e = TileInteraction.LastPress;
        int slot = Instruments.SlotOf(pm.selectedInstrument, Instruments.BrushVoiceOf(pm.selectedInstrument));
        bool key = e.midi == VoiceRules.PressEvent(slot, K(5), 0, pm.PressSeconds).midi && pm.LastPressTile == K(5);
        pm.SimPointer(Scr(K(5).Top), true, true, false, false, true);
        for (int k = 6; k <= 9; k++) pm.SimPointer(Scr(K(k).Top), false, true, false, false, true);
        pm.SimPointer(Scr(K(9).Top), false, false, true, false, true);
        yield return null;
        bool glissE = PathManager.GlissCount == gl0 + 4 && PathManager.PressCount == pr0 + 6 && PathManager.DragHandovers == h0 && SequenceMaster.Cubes.Count == c0 && !pm.IsDrawing;
        Add(L(key && glissE, "K7 EMPTY hand on a keyboard: a click plays the key (its own octave, VoiceRules.PressEvent); Alt + drag plays each key entered (a glissando), no cube, no island drag",
              "key midi " + e.midi + " (key " + K(5).midi + "), gliss +" + (PathManager.GlissCount - gl0) + ", presses +" + (PathManager.PressCount - pr0) + ", handovers +" + (PathManager.DragHandovers - h0)));
    }

    // ================================================================== 3. copy / paste
    static IEnumerator Paste()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        var key = MusicTheory.KeyOfSong();
        // the source: a pitched fixture cube, not bass-like (so the plain voice-leading rule applies)
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && !c.rider && c.instrument != Clipboard.BassGroup && c.nodes.Count >= 3 && c.Island != null && !c.Island.IsKeyboard)
            { bool low = true; foreach (var t in c.nodes) if (t.gridZ != 0) low = false; if (!low) { src = c; break; } }
        if (src == null) { Add(L(false, "P paste", "no pitched fixture cube")); yield break; }
        var home = src.Island;
        Clipboard.Copy(src);
        // PasteNext's target = the next column's island nearest in z (chord islands)
        KeyBlock wantNext = null; float bd = float.MaxValue;
        int nc = (home.column + 1) % sm.ColumnCount;
        foreach (var kb in sm.ColumnIslands(nc)) { if (kb == null || kb.IsKeyboard || kb.IsMoon) continue; float d = Mathf.Abs(kb.Center.z - home.Center.z); if (d < bd) { bd = d; wantNext = kb; } }
        var next = Clipboard.NextGrid(home);
        // case 1: grid -> grid = Harmony.AdaptGrid
        var srcMidi = new List<int>(); var sx = new List<int>(); var szs = new List<int>();
        foreach (var t in src.nodes) { srcMidi.Add(t.midi); sx.Add(t.gridX); szs.Add(t.gridZ); }
        var w = Harmony.WeightsOf(src.HasDurations ? src.durs : null, src.nodes.Count);
        int[] ex, ez;
        Harmony.AdaptGrid(srcMidi, sx, szs, w, home.rows, Harmony.MidiTable(next), false, -1, out ex, out ez);
        var st = Clipboard.AdaptedFor(next);
        bool same = st != null && Ints(st.xs) == Ints(ex) && Ints(st.zs) == Ints(ez);
        int cubes0 = SequenceMaster.Cubes.Count, p0 = pushes;
        var pasted = Clipboard.PasteNext();
        bool hidden = pasted != null && !pasted.BodyVisible;
        bool placed = pasted != null && pasted.Island == next && SequenceMaster.Cubes.Count == cubes0 + 1 && pushes == p0 + 1;
        string pitches = "-";
        bool pitchOk = placed;
        if (pasted != null)
        {
            pitches = Midis(pasted.nodes);
            for (int i = 0; pitchOk && i < ex.Length; i++) pitchOk = pasted.nodes[i] == next.GetTile(ex[i], ez[i]);
        }
        bool kept = pasted != null && pasted.instrument == src.instrument && pasted.voice == src.voice && Ints(pasted.durs) == Ints(src.durs) && pasted.id != src.id && pasted.twinOf == -1 && pasted.octave == src.octave;
        Add(L(next == wantNext && same && placed && pitchOk && kept,
              "P1 grid -> grid: PasteNext lands on the next column's nearest chord island with Harmony.AdaptGrid's tiles (voice-led inversion); one History entry; instrument, voice, lengths kept, a new id",
              "next ok " + (next == wantNext) + ", xs " + (st != null ? Ints(st.xs) : "null") + " want " + Ints(ex) + ", zs " + (st != null ? Ints(st.zs) : "null") + " want " + Ints(ez) + ", pitches " + Midis(src.nodes) + " -> " + pitches + ", pushes +" + (pushes - p0)));
        yield return Wait(0.2f);
        Add(L(!hidden && pasted != null && pasted.BodyVisible && pasted.PathVisible, "P1 (v7 §21) the pasted cube APPEARS on its grid at once (a pop and a sparkle there; v6 hid it while ghost beads flew across)",
              "visible at once " + !hidden + ", visible after 0.2 s " + (pasted != null && pasted.BodyVisible)));
        int srcIdx = sm.Islands.IndexOf(src.Island), srcInst = src.instrument;
        History.Undo(); yield return Wait(0.3f);
        Add(L(SequenceMaster.Cubes.Count == cubes0, "P1 undo takes the paste back", "cubes " + SequenceMaster.Cubes.Count + " want " + cubes0));
        // a paste while the song plays sounds at once (the target's column loops: the new cube's notes reach the synth in the first pass)
        {
            var s0 = FindCube(srcIdx, srcInst, sx, szs);
            if (s0 != null)
            {
                Clipboard.Copy(s0);
                var tn = Clipboard.NextGrid(s0.Island);
                FocusLoop.Begin(tn.column);
                yield return Wait(0.3f);
                tappedOwner = -1; tappedCount = 0; VoiceRules.Tap = OnTapOwner;
                var live = Clipboard.PasteOn(tn);
                tappedOwner = live != null ? live.owner : -1;
                double loopSec = (GlobalClock.RegionEnd - GlobalClock.RegionStart) / GlobalClock.BeatsPerSecond;
                yield return Wait((float)loopSec + 0.4f);
                VoiceRules.Tap = null;
                Add(L(live != null && tappedCount > 0, "P1 a paste while the song plays joins it at once (its notes are dispatched within one pass of its column)",
                      "notes " + tappedCount + " in " + F(loopSec + 0.4, "F1") + " s"));
                FocusLoop.Dismiss(); GlobalClock.Stop();
                History.Undo(); yield return Wait(0.3f);
            }
        }

        // case 2: the bass rule — a bass cube's first note lands on the target's root
        home = sm.Islands[0];
        var tgt = Clipboard.NextGrid(home);
        var bass = Make(home, Clipboard.BassGroup, new[] { 2, 3, 4 }, new[] { 1, 0, 1 }, new[] { 24, 24, 48 });
        Clipboard.Copy(bass);
        var bst = Clipboard.AdaptedFor(tgt);
        var bexpX = new List<int>(); var bexpZ = new List<int>(); var bm = new List<int>();
        foreach (var t in bass.nodes) { bm.Add(t.midi); bexpX.Add(t.gridX); bexpZ.Add(t.gridZ); }
        int[] bx, bz;
        Harmony.AdaptGrid(bm, bexpX, bexpZ, Harmony.WeightsOf(bass.durs, 3), home.rows, Harmony.MidiTable(tgt), true, Harmony.Pc(tgt.chordRootMIDI), out bx, out bz);
        var first = bst != null ? tgt.GetTile(bst.xs[0], bst.zs[0]) : null;
        bool rootFirst = first != null && Harmony.Pc(first.midi) == Harmony.Pc(tgt.chordRootMIDI);
        Add(L(bst != null && Ints(bst.xs) == Ints(bx) && Ints(bst.zs) == Ints(bz) && rootFirst,
              "P2 the bass rule: a bass pattern pasted on the next chord starts on its root (Harmony.AdaptGrid bassLike)",
              "xs " + (bst != null ? Ints(bst.xs) : "null") + " want " + Ints(bx) + ", first note " + (first != null ? first.midi.ToString() : "-") + " root pc " + Harmony.Pc(tgt.chordRootMIDI)));
        bass.Delete(false); SequenceMaster.Unregister(bass); SequenceMaster.RecalculateTimeline();

        // two keyboards in two columns (different chords): keyboard -> keyboard and grid -> keyboard = Harmony.AdaptMelody (exact keys)
        int k1 = sm.AddKeyboardIsland(0, false);
        yield return Wait(0.5f);
        int colB = sm.Islands[k1].column + 1;
        int anchorB = sm.ColumnFirst(colB);
        int k2 = anchorB >= 0 ? sm.AddKeyboardIsland(anchorB, false) : -1;
        yield return Wait(0.5f);
        KeyBlock kb1 = null, kb2 = null;
        foreach (var kb in sm.Islands) if (kb != null && kb.IsKeyboard) { if (kb.column == colB - 1) kb1 = kb; else if (kb.column == colB) kb2 = kb; }
        if (kb1 == null || kb2 == null) { Add(L(false, "P keyboards", "added " + k1 + " / " + k2)); yield break; }
        int r1, r2; IList<int> s1, s2;
        Harmony.ChordOf(kb1, out r1, out s1); Harmony.ChordOf(kb2, out r2, out s2);
        Info("keyboards: col " + kb1.column + " chord " + r1 + " [" + Ints(s1) + "], col " + kb2.column + " chord " + r2 + " [" + Ints(s2) + "]");
        var mel = Make(kb1, 3, new[] { 0, 4, 7, 12, 11, 7 }, new[] { 0, 0, 0, 0, 0, 0 }, new[] { 24, 12, 12, 48, 24, 24 });
        Clipboard.Copy(mel);
        var mm = new List<int>(); foreach (var t in mel.nodes) mm.Add(t.midi);
        int lo = kb2.GetTile(0, 0).midi, hi = kb2.GetTile(kb2.cols - 1, 0).midi;
        var line = Harmony.AdaptMelody(mm, Harmony.WeightsOf(mel.durs, mm.Count), r1, s1, r2, s2, key.tonic, key.minor, lo, hi);
        var kst = Clipboard.AdaptedFor(kb2);
        bool kk = kst != null && kst.xs.Length == line.Length;
        for (int i = 0; kk && i < line.Length; i++) kk = kb2.GetTile(kst.xs[i], 0).midi == line[i];
        int pk = pushes;
        var kp = Clipboard.PasteOn(kb2);
        bool kPlaced = kp != null && kp.Island == kb2 && pushes == pk + 1;
        for (int i = 0; kPlaced && i < line.Length; i++) kPlaced = kp.nodes[i].midi == line[i];
        Add(L(kk && kPlaced, "P3 keyboard -> keyboard: the melody moves by scale steps onto the next column's chord (Harmony.AdaptMelody), landing on exactly those keys; one History entry",
              Midis(mel.nodes) + " -> " + (kp != null ? Midis(kp.nodes) : "null") + " want " + Ints(line)));
        // grid -> keyboard (the islands were rebuilt by the adds: the source cube is found again)
        int shift = 0; foreach (var kb in sm.Islands) if (kb != null && kb.IsKeyboard && sm.Islands.IndexOf(kb) <= srcIdx + shift) shift++;
        src = FindCube(srcIdx + shift, srcInst, sx, szs);
        if (src == null) { for (int i = 0; i < sm.Islands.Count && src == null; i++) src = FindCube(i, srcInst, sx, szs); }
        if (src == null) { Add(L(false, "P4 grid -> keyboard", "the source cube is gone")); yield break; }
        Clipboard.Copy(src);
        int gr; IList<int> gs; Harmony.ChordOf(src.Island, out gr, out gs);
        var gline = Harmony.AdaptMelody(srcMidi, w, gr, gs, r2, s2, key.tonic, key.minor, lo, hi);
        var gst = Clipboard.AdaptedFor(kb2);
        bool gk = gst != null && gst.xs.Length == gline.Length;
        for (int i = 0; gk && i < gline.Length; i++) gk = kb2.GetTile(gst.xs[i], 0).midi == gline[i];
        Add(L(gk, "P4 grid -> keyboard: the grid pattern becomes that melody on the keyboard's keys (Harmony.AdaptMelody on its pitches, in the keys' range)",
              Midis(src.nodes) + " -> " + (gst != null ? Ints(KeysMidi(kb2, gst.xs)) : "null") + " want " + Ints(gline)));

        // the pattern hand: hovering a grid shows the adapted ghost (= what a click pastes); a click pastes; the hand keeps the pattern
        Clipboard.Copy(src);
        var target = Clipboard.NextGrid(src.Island);
        bool up = pm.PickUpPattern();
        Frame(target.VisualCenter, 10f, 0f, 58f);
        TileInteraction over = null;
        foreach (var t in target.tiles) { var r = pm.PickAt(Scr(t.Top)); if (r.tile == t) { over = t; break; } }
        PathManager.SimPos = over != null ? Scr(over.Top) : Scr(target.VisualCenter);
        yield return Frames(3);
        var ast = Clipboard.AdaptedFor(target);
        bool ghostOk = up && pm.Hand == PathManager.HandKind.Pattern && pm.HoverPasteTarget == target && ast != null && pm.PatternGhostTiles.Count == ast.xs.Length;
        for (int i = 0; ghostOk && i < ast.xs.Length; i++) ghostOk = pm.PatternGhostTiles[i] == target.GetTile(ast.xs[i], ast.zs[i]);
        string ghostAt = "target ok " + (pm.HoverPasteTarget == target) + ", ghost " + pm.PatternGhostTiles.Count + " tiles = " + Midis(new List<TileInteraction>(pm.PatternGhostTiles));
        if (shots) { yield return Wait(0.2f); yield return Capture("h6_pattern_ghost"); }
        int pc0 = Clipboard.PasteCount, pp = pushes;
        yield return Click(PathManager.SimPos);
        bool clickPaste = Clipboard.PasteCount == pc0 + 1 && pushes == pp + 1 && Clipboard.LastPasted != null && Clipboard.LastPasted.Island == target && pm.Hand == PathManager.HandKind.Pattern;
        Key(KeyCode.Escape); yield return null; yield return null;
        Add(L(ghostOk && clickPaste && pm.Hand == PathManager.HandKind.Empty,
              "P5 a PATTERN in the hand: the hovered grid shows the adapted ghost beads (exactly Clipboard.AdaptedFor's tiles); a click pastes there (one History entry), the hand keeps the pattern; Esc puts it down",
              "picked " + up + ", " + ghostAt + ", pasted +" + (Clipboard.PasteCount - pc0) + ", hand after Esc " + pm.Hand));
        // the stamp pastes ADAPTED too
        pm.Select(src);
        var stamped = pm.StampTo(target);
        pm.Deselect();
        bool stampOk = stamped != null && ast != null;
        for (int i = 0; stampOk && i < ast.xs.Length; i++) stampOk = stamped.nodes[i] == target.GetTile(ast.xs[i], ast.zs[i]);
        Add(L(stampOk, "P6 the stamp (G) places the ADAPTED pattern too (the same tiles as a paste)", stamped != null ? Midis(stamped.nodes) : "null"));
        CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop();
    }

    static int[] KeysMidi(KeyBlock kb, int[] xs) { var r = new int[xs.Length]; for (int i = 0; i < xs.Length; i++) { var t = kb.GetTile(xs[i], 0); r[i] = t != null ? t.midi : -1; } return r; }
    static int tappedOwner = -1, tappedCount;
    static void OnTapOwner(VoiceRules.NoteEvent e) { if (e.owner == tappedOwner && tappedOwner >= 0) tappedCount++; }

    // ================================================================== 5. drum cubes ride their Moon (K, SPEC §11: a Moon follows the lit column while its section plays)
    static IEnumerator MoonRide()
    {
        var sm = SongManager.I;
        if (sm.Moons.Count == 0 && UIManager.I != null) UIManager.I.AddMoon();
        yield return Wait(0.4f);
        if (sm.Moons.Count == 0) { Add(L(false, "M moon", "no Moon could be added")); yield break; }
        var moon = sm.Moons[0];
        AudioCube dc = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Moon == moon && c.nodes.Count >= 2) { dc = c; break; }
        if (dc == null)
        {
            var s = new CubeState { instrument = 9, moon = 0, measure = -1, xs = new[] { 0, 2, 4, 2 }, zs = new[] { 0, 1, 0, 1 }, rests = new bool[4], mods = new int[4], durs = new[] { 12, 12, 12, 12 }, volume = 1f };
            dc = PathManager.I.RestoreCube(s);
            SequenceMaster.RecalculateTimeline();
        }
        if (dc == null) { Add(L(false, "M moon", "no drum cube on the Moon")); yield break; }
        Frame(moon.VisualCenter, 24f, 0f, 60f);
        GlobalClock.Stop(); GlobalClock.Seek(0); GlobalClock.Play();
        float t0 = Time.realtimeSinceStartup, worst = 0f, travel = 0f, x0 = moon.transform.position.x;
        int frames = 0, riding = 0, rideHops = 0;
        while (Time.realtimeSinceStartup - t0 < 6f)
        {
            frames++;
            if (moon.Riding) { riding++; if (dc.Hopping) rideHops++; }
            travel = Mathf.Max(travel, Mathf.Abs(moon.transform.position.x - x0));
            // Moon-local x/z distance from the cube to its path's hop lines (node i -> i + 1, and the loop's last -> first): riding along, a cube
            // stays on them (hops are straight in x/z); left behind by a gliding Moon it would drift as far as the Moon travels
            Vector3 lp = moon.transform.InverseTransformPoint(dc.transform.position);
            Vector2 q = new Vector2(lp.x, lp.z);
            float dmin = float.MaxValue;
            int n = dc.nodes.Count;
            for (int i = 0; i < n; i++)
            {
                var ta = dc.nodes[i]; var tb = dc.nodes[(i + 1) % n];
                if (ta == null || tb == null) continue;
                Vector3 a3 = moon.transform.InverseTransformPoint(ta.Top), b3 = moon.transform.InverseTransformPoint(tb.Top);
                Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z), ab = b - a;
                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                dmin = Mathf.Min(dmin, (q - (a + ab * u)).magnitude);
            }
            worst = Mathf.Max(worst, dmin);
            yield return null;
        }
        GlobalClock.Stop();
        if (riding == 0) Info("the Moon did not glide in this run (no Moon section ride): the drift below is at rest");
        Add(L(worst < 0.35f, "M a drum cube stays on its Moon while the Moon glides with the lit column (its hops, drops and rest are island-local)",
              "frames " + frames + ", Moon riding " + riding + " (" + rideHops + " mid-hop), Moon travel " + F(travel) + " u, worst Moon-local x/z distance to its hop lines " + F(worst, "F3") + " u"));
    }

    /// <summary>Carry diagnostics: a late synth event while the carried cubes play is logged with the beat, the seconds since the last
    /// play start and what the cubes were doing (flying or not).</summary>
    static int lateSeen, lateAtPlay; static float playT;
    /// <summary>A late event within 0.35 s of a test's Pause → Seek → Play restart is the known play-start artifact (a note on the restart beat is
    /// queued only the play latency ahead; an editor hitch then makes it late): it is reported and counted apart; any other late event counts.</summary>
    static void WatchLate(string where)
    {
        if (Synth.LateEvents == lateSeen) return;
        int d = Synth.LateEvents - lateSeen;
        int fly = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Flying) fly++;
        float since = Time.realtimeSinceStartup - playT;
        bool atPlay = since < 0.35f;
        if (atPlay) { lateExcused += d; lateAtPlay += d; }
        Info("late +" + d + " " + where + " at beat " + F(GlobalClock.SongBeatD, "F3") + ", " + F(since, "F2") + " s after the play start" + (atPlay ? " (the play-start artifact: counted apart)" : "") + ", flying cubes " + fly + ", frame " + F(Time.unscaledDeltaTime * 1000f, "F0") + " ms (" + Synth.LastLate + ")");
        lateSeen = Synth.LateEvents;
    }

    // ================================================================== 4. carried cubes
    static IEnumerator Carry()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        // a home island with a pitched cube and two columns after it
        KeyBlock home = null; AudioCube cube = null;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsKeyboard || kb.column > sm.ColumnCount - 3) continue;
            var c = CubeOn(kb, 3, true);
            if (c != null) { home = kb; cube = c; break; }
        }
        if (home == null) { Add(L(false, "C carry", "no island with a pitched cube and two columns after it")); yield break; }
        int hi = sm.Islands.IndexOf(home);
        int inst = cube.instrument, n = cube.nodes.Count; var hx = new List<int>(); var hz = new List<int>();
        foreach (var t in cube.nodes) { hx.Add(t.gridX); hz.Add(t.gridZ); }
        int p0 = pushes;
        sm.SetCarry(hi, 2);
        yield return Wait(0.4f);
        home = sm.Islands[hi];
        cube = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == home && c.instrument == inst && c.nodes.Count == n) { bool ok = true; for (int i = 0; i < n && ok; i++) ok = c.nodes[i].gridX == hx[i] && c.nodes[i].gridZ == hz[i]; if (ok) cube = c; }
        var targets = sm.CarryTargets(hi);
        if (cube == null || targets.Count < 2) { Add(L(false, "C carry", "cube " + (cube != null) + ", targets " + targets.Count)); yield break; }
        // the expected tiles: hop 1 from home, hop 2 from hop 1 — v7 §21.3 (every carry is a LONG GRID: carryStyle is ignored) by the flow rule,
        // Harmony.AdaptPedal over that measure's pitches (v6 asserted Harmony.AdaptGrid's voice-led inversion here: changed meaning, H's suite)
        var expect = new List<List<TileInteraction>>();
        IList<TileInteraction> prev = cube.nodes;
        var w = Harmony.WeightsOf(cube.HasDurations ? cube.durs : null, n);
        var key = MusicTheory.KeyOfSong();
        foreach (var T in targets)
        {
            var src = new int[n]; for (int i = 0; i < n; i++) src[i] = cube.ModOf(i) == 1 ? -1 : prev[i].midi;
            int root; IList<int> semis; Harmony.ChordOf(T, out root, out semis);
            var ped = Harmony.AdaptPedal(src, w, root, semis, key.tonic, key.minor, Harmony.Pool(T));
            var l = new List<TileInteraction>();
            for (int i = 0; i < n; i++)
            {
                var near = T.GetTile(Mathf.Clamp(prev[i].gridX, 0, T.cols - 1), Mathf.Clamp(prev[i].gridZ, 0, T.rows - 1));
                l.Add(ped[i] < 0 ? near : (CubeOps.TileFor(T, ped[i], near, false) ?? near));
            }
            expect.Add(l); prev = l;
        }
        int w1 = -1, w2 = -1, carried = 0;
        for (int i = 0; i < cube.windows.Count; i++) { var wi = cube.windows[i]; if (!wi.carried) continue; carried++; if (wi.hop == 1 && w1 < 0) w1 = i; if (wi.hop == 2 && w2 < 0) w2 = i; }
        bool tilesOk = w1 >= 0 && w2 >= 0 && cube.windows[w1].island == targets[0] && cube.windows[w2].island == targets[1];
        for (int i = 0; tilesOk && i < n; i++) tilesOk = cube.TileAt(w1, i) == expect[0][i] && cube.TileAt(w2, i) == expect[1][i];
        var on1 = cube.TilesOn(targets[0]);
        bool tilesOn = on1.Count == n; for (int i = 0; tilesOn && i < n; i++) tilesOn = on1[i] == expect[0][i];
        var got1 = new List<TileInteraction>(); var got2 = new List<TileInteraction>();
        for (int i = 0; i < n && w1 >= 0 && w2 >= 0; i++) { got1.Add(cube.TileAt(w1, i)); got2.Add(cube.TileAt(w2, i)); }
        Add(L(tilesOk && tilesOn && pushes == p0 + 1, "C1 a carried window plays the pattern ADAPTED hop by hop (hop 1 from home, hop 2 from hop 1; v7 §21.3: the long grid's flow rule, Harmony.AdaptPedal); TilesOn(target) returns them",
              "carried windows " + carried + ", home " + Midis(cube.nodes) + " -> hop1 " + Midis(got1) + " (want " + Midis(expect[0]) + ") -> hop2 " + Midis(got2) + " (want " + Midis(expect[1]) + "), pushes +" + (pushes - p0)));
        int builds0 = AudioCube.HopBuilds;
        for (int k = 0; k < 20; k++) cube.TileAt(w1, 0);
        yield return null;
        cube.TileAt(w1, 0);
        Add(L(AudioCube.HopBuilds == builds0, "C1 the adapted tiles are cached (no rebuild between changes)", "rebuilds +" + (AudioCube.HopBuilds - builds0)));

        // v7 §21 (the user: "the cubes jumping form grid to grid doesnt look good ... just keep cubes on each grid"): the carry is a LONG GRID and the
        // cube steps into the next measure with an ORDINARY HOP (v6 asserted a flight along the magic arc here: changed meaning, H's suite)
        GlobalClock.SetBPM(45f);
        float s1 = cube.windows[w1].start;
        int lastW = -1; float lastEnd = -1f;
        for (int i = 0; i < cube.windows.Count; i++) { var wi = cube.windows[i]; if (wi.carried && wi.start + wi.length > lastEnd) { lastEnd = wi.start + wi.length; lastW = i; } }
        Frame((home.VisualCenter + targets[0].VisualCenter) * 0.5f + Vector3.up * 0.5f, 17f, 0f, 50f);
        GlobalClock.Stop(); GlobalClock.Seek(Mathf.Max(0f, s1 - 1.2f)); GlobalClock.Play();
        lateSeen = Synth.LateEvents; playT = Time.realtimeSinceStartup;
        float t0 = Time.realtimeSinceStartup;
        bool sawCross = false, everFlew = false, rightward = true; float maxLift = -9f, prevX = float.NegativeInfinity; Vector3 posSeen = Vector3.zero;
        float topAll = float.MinValue;
        foreach (var t in home.tiles) topAll = Mathf.Max(topAll, t.Top.y);
        foreach (var t in targets[0].tiles) topAll = Mathf.Max(topAll, t.Top.y);
        while (Time.realtimeSinceStartup - t0 < 6f && GlobalClock.SongBeat < s1 + 0.05f)
        {
            WatchLate("(to hop 1)");
            if (cube.Flying || cube.Gliding || cube.Flinging) everFlew = true;
            if (cube.CrossingMeasure && cube.Hopping)
            {
                if (!sawCross) { sawCross = true; posSeen = cube.transform.position; if (shots) yield return Capture("h6_carry_flight"); }
                float x = cube.transform.position.x;
                if (x < prevX - 1e-3f) rightward = false;
                prevX = x;
                maxLift = Mathf.Max(maxLift, cube.transform.position.y - (topAll + ProjectConfig.CubeSize * cube.SizeFactor * 0.5f));
            }
            yield return null;
        }
        Add(L(sawCross && !everFlew && rightward && maxLift < ProjectConfig.cubeHopIntensity + 0.15f, "C2 (v7 §21) before hop 1 the cube steps into the next measure of its long grid with an ORDINARY HOP (left → right, as low as any hop), never a flight",
              "crossing seen " + sawCross + " at " + posSeen.ToString("F2") + ", flew " + everFlew + ", left → right " + rightward + ", lift over the tiles " + F(maxLift)));
        yield return Wait(0.5f);
        AudioCube.CubePose pose;
        bool landed = !cube.Flying && cube.TryGetPose(out pose) && pose.tile != null && pose.tile.island == targets[0];
        Add(L(landed, "C3 it lands on the target and plays its adapted notes there", "flying " + cube.Flying + ", pose island " + (cube.TryGetPose(out pose) && pose.tile != null ? sm.Islands.IndexOf(pose.tile.island).ToString() : "-") + " want " + sm.Islands.IndexOf(targets[0])));
        // the chain goes on: before hop 2 it steps from the first target to the second (§21: an ordinary hop)
        WatchLate("(landed)");
        float s2 = cube.windows[w2].start;
        GlobalClock.Pause(); GlobalClock.Seek(Mathf.Max(0f, s2 - 1.2f)); GlobalClock.Play();
        lateSeen = Synth.LateEvents; playT = Time.realtimeSinceStartup;
        t0 = Time.realtimeSinceStartup;
        bool hopStep = false, flew2 = false;
        while (Time.realtimeSinceStartup - t0 < 6f && GlobalClock.SongBeat < s2 + 0.3f && GlobalClock.SongBeat >= s2 - 2f)
        {
            WatchLate("(to hop 2)");
            if (cube.Flying || cube.Gliding) flew2 = true;
            if (cube.CrossingMeasure && cube.Hopping) hopStep = true;
            yield return null;
        }
        yield return Wait(0.3f);
        bool on2 = cube.TryGetPose(out pose) && pose.tile != null && pose.tile.island == targets[1];
        Add(L(hopStep && !flew2 && on2, "C3 the chain goes on: before hop 2 the cube steps (an ordinary hop, v7 §21) from the first target to the second and plays there", "step " + hopStep + ", flew " + flew2 + ", on hop 2's island " + on2));
        // v7 §12.1 (hold then return — the user: "whenever a grid ends, it shouldnt teleport back to its starting position, they should only do
        // that when the song resets"): after its last carried window the cube HOLDS on the grid it played last; v6 asserted "it flies home the same
        // way" right after it (changed meaning, H's own suite)
        GlobalClock.Pause(); GlobalClock.Seek(Mathf.Max(0f, lastEnd - 1.3f)); GlobalClock.Play();
        lateSeen = Synth.LateEvents; playT = Time.realtimeSinceStartup;
        t0 = Time.realtimeSinceStartup;
        bool homeFlight = false;
        while (Time.realtimeSinceStartup - t0 < 6f && GlobalClock.SongBeat < lastEnd + 1.2f && GlobalClock.SongBeat >= lastEnd - 2f)
        {
            WatchLate("(hold)");
            if (cube.Flying && cube.FlightTo == home) homeFlight = true;
            yield return null;
        }
        WatchLate("(hold, after)");
        yield return Wait(0.2f);
        var lastIsl = cube.windows[lastW].island;
        bool holds = !cube.Flying && cube.RestingTile != null && cube.RestingTile.island == lastIsl && cube.Holding;
        Add(L(!homeFlight && holds, "C4 (v7 §12.1) after its last carried window the cube HOLDS on the last grid it played (no flight home until the song resets)",
              "home flight " + homeFlight + ", resting on island " + (cube.RestingTile != null ? sm.Islands.IndexOf(cube.RestingTile.island).ToString() : "-") + " (last " + sm.Islands.IndexOf(lastIsl) + "), holding " + cube.Holding));
        GlobalClock.Stop();
        yield return Wait(0.9f);
        Add(L(!cube.Flying && cube.RestingTile != null && cube.RestingTile.island == home, "C4 stopped: the cube travels home (v7: in real time, no snap) and rests there", "resting island " + (cube.RestingTile != null ? sm.Islands.IndexOf(cube.RestingTile.island).ToString() : "-")));
    }
}
