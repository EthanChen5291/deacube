using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v5 §6 package R checks (Play mode): hearing notes, tiles that make sense. <c>V5ChecksR.Run()</c> starts the timed checks; poll
/// <see cref="Done"/> or Captures/r5_report.txt. Sections: "tiles" (no chord glyphs, drum pictograms on Moons, the pitch staircase and the
/// brightness ramp), "idle" (hover audition when stopped: at once, the brush instrument, once per entry, the rate cap; nothing over the HUD, while
/// the world is locked or when only the camera moves), "playing" (a tile of an island that is not lit auditions on the next 16th on the preview
/// bus; the song ducks and recovers), "duck" (recorded steady tones: the song bus dips about 8 dB for about 0.4 s, the preview bus does not),
/// "run" (drafting: the run a click adds = LineTiles in order, as an arpeggio RunGap apart, the target longest; the hologram peeks; a click adds
/// exactly those tiles), "melody" (the melody line's points = the draft's nodes: count, order, x by the lengths, relative heights; the ghost run;
/// Backspace; finish), "shots" (r5_island_rest, r5_moon, r5_inspector_grid; r5_draft_melody comes from "melody"). The fixture is loaded (never
/// the user's save); the autosave and backups are put back as they were.
/// v6 (H): its checks draw by clicking tiles with an empty hand, as before v6: they run (and RunOlder runs the older suites) with
/// PathManager.AutoHand = true, restored after.
/// </summary>
public static class V5ChecksR
{
    static bool autoWas, olderAutoWas;
    public static string Report = "";
    public static bool Done;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "r5_report.txt");
    /// <summary>A synth owner for the test tones (far from the cubes' instance-id range).</summary>
    const int ToneOwner = 1900000051;

    static StringBuilder sb;
    static int pass, fail, pushes;
    static readonly List<VoiceRules.NoteEvent> tapped = new List<VoiceRules.NoteEvent>();

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
    }

    static string FirstFrame(Exception e) { var st = e.StackTrace ?? ""; int i = st.IndexOf('\n'); return i > 0 ? st.Substring(0, i).Trim() : st.Trim(); }
    static void OnHistory() { pushes++; }
    static void OnTap(VoiceRules.NoteEvent e) { if (tapped.Count < 4096) tapped.Add(e); }
    static string F(double v, string f = "F3") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static readonly Vector3 Sea = new Vector3(-50f, -50f, 0f);

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray closed, the real mouse ignored, the Keys brush).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); WorldInput.Unlock("r5"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        PathManager.SimOnly = true; PathManager.SimPos = Sea;
        if (PathManager.I != null)
        {
            if (PathManager.I.IsDrawing) PathManager.I.CancelPath(false);
            if (PathManager.I.Drag != null && PathManager.I.Drag.Busy) PathManager.I.Drag.Cancel();
            PathManager.I.Deselect();
            if (PathManager.I.selectedInstrument != 0) PathManager.I.SelectInstrument(0);
        }
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
        if (cmd) KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
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

    static IEnumerator Capture(string name)
    {
        Directory.CreateDirectory(V2Checks.CapturePath);
        ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, name + ".png"), 1);
        yield return null;
        yield return null;
    }

    /// <summary>A tile of <paramref name="kb"/> with no cube that picks itself (a free tile, rule 4) from the current view.</summary>
    static bool Free(TileInteraction t)
    {
        if (t == null || PathManager.TopCubeOn(t) != null) return false;
        var r = PathManager.I.PickAt(Scr(t.Top));
        return r.tile == t && r.rule == 4;
    }

    /// <summary>A free row of <paramref name="len"/> pickable tiles on <paramref name="kb"/> (the camera frames it steeply and stays there).</summary>
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

    static KeyBlock FreeIsland(int minRows, int exceptColumn = -1)
    {
        var sm = SongManager.I;
        KeyBlock best = null; int bestCubes = int.MaxValue;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.rows < minRows || kb.column == exceptColumn) continue;
            int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) n++;
            if (n < bestCubes) { bestCubes = n; best = kb; }
        }
        return best;
    }

    static int OwnerEvents(int owner) { int n = 0; foreach (var e in tapped) if (e.owner == owner) n++; return n; }

    // ================================================================== entry points
    public static string Run(bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; Progress = "start"; autoWas = PathManager.AutoHand; PathManager.AutoHand = true;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(null, captures));
        return "started";
    }

    /// <summary>One section ("tiles", "idle", "playing", "duck", "run", "melody", "shots") with the usual preamble / restore.</summary>
    public static string RunOnly(string which, bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; Progress = "start"; autoWas = PathManager.AutoHand; PathManager.AutoHand = true;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(which, captures));
        return "started " + which;
    }

    static readonly string[] Sections = { "tiles", "idle", "playing", "duck", "run", "melody", "belt", "shots" };

    static IEnumerator Routine(string only, bool captures)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0;
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        float duckDb0 = Synth.DuckDb;
        History.OnChanged += OnHistory;
        foreach (var s in Sections)
        {
            if (only != null && only != s) continue;
            if (s == "shots" && !captures) continue;
            Prepare(); LoadFixture(); yield return null; yield return null;
            Progress = s;
            IEnumerator body = null;
            switch (s)
            {
                case "tiles": Add(Tiles()); break;
                case "idle": body = Idle(); break;
                case "playing": body = Playing(); break;
                case "duck": body = Duck(); break;
                case "run": captureRun = captures; body = RunArp(); break;
                case "melody": body = Melody(captures); break;
                case "belt": body = BeltRide(); break;
                case "shots": body = Shots(); break;
            }
            int lateS = Synth.LateEvents;
            if (body != null) yield return Guard(body);
            if (Synth.LateEvents != lateS) Info("section " + s + ": synth late +" + (Synth.LateEvents - lateS) + " (last: " + Synth.LastLate + ")");
            VoiceRules.Tap = null;
            if (PathManager.I.IsDrawing) PathManager.I.CancelPath(false);
            FocusLoop.Dismiss(); GlobalClock.Stop();
        }
        Progress = "finish";
        VoiceRules.Tap = null;
        Synth.DuckDb = duckDb0;
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        History.OnChanged -= OnHistory;
        V3Fixes.RestoreSaves(saves);
        Add(L(Synth.LateEvents == late0 && Synth.Errors == err0, "RUN Synth late / errors unchanged", Synth.Stats()));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        PathManager.AutoHand = autoWas; Done = true; Progress = "done";
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

    // ================================================================== the older suites R owns, in one go
    public static bool OlderDone = true;
    public static string OlderReport = "";
    public static string OlderPath => Path.Combine(V2Checks.CapturePath, "r5_older_report.txt");

    /// <summary>Runs the older suites package R owns one after the other — V4ChecksR (timed), V2Checks.RunAll + RunWorld, WpAChecks.RunM1 +
    /// RunM2Static and its timed rider (v7 §21: home windows only) / Moon / stutter checks, V2Checks.RunIntegration — and writes Captures/r5_older_report.txt (per suite
    /// PASS / FAIL counts, every FAIL line, then the reports). Poll <see cref="OlderDone"/>.</summary>
    public static string RunOlder(bool captures = false)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!OlderDone) return "already running";
        OlderDone = false; OlderReport = ""; olderAutoWas = PathManager.AutoHand; PathManager.AutoHand = true;
        try { if (File.Exists(OlderPath)) File.Delete(OlderPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(OlderRoutine(captures));
        return "started";
    }

    static void Count(string text, string suite, ref int p, ref int f, StringBuilder fails)
    {
        foreach (var line in (text ?? "").Split('\n'))
        {
            var l = line.TrimStart();
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^(\d+\.\s*)?(PASS|FAIL)\b");
            if (!m.Success) continue;
            if (m.Groups[2].Value == "PASS") p++; else { f++; fails.Append("  [").Append(suite).Append("] ").Append(l.Length > 260 ? l.Substring(0, 260) : l).Append('\n'); }
        }
    }

    static IEnumerator OlderRoutine(bool captures)
    {
        var saves = V3Fixes.SnapshotSaves();
        var body = new StringBuilder(); var head = new StringBuilder(); var fails = new StringBuilder();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        string r;
        // V4ChecksR
        Prepare(); LoadFixture(); yield return null;
        V4ChecksR.Run(true, captures);
        yield return null; yield return null;
        float t0 = Time.realtimeSinceStartup;
        while (!V4ChecksR.Done && Time.realtimeSinceStartup - t0 < 600f) yield return null;
        int p = 0, f = 0; Count(V4ChecksR.Report, "V4ChecksR", ref p, ref f, fails);
        head.Append("V4ChecksR ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V4ChecksR\n").Append(V4ChecksR.Report).Append('\n');
        // V2Checks RunAll / RunWorld
        Prepare(); V3ChecksB.Prepare(); PathManager.SimOnly = false;
        yield return null; yield return null;
        try { r = V2Checks.RunAll(); } catch (Exception e) { r = "FAIL RunAll threw " + e.Message + "\n"; }
        p = 0; f = 0; Count(r, "V2Checks.RunAll", ref p, ref f, fails); head.Append("V2Checks.RunAll ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V2Checks.RunAll\n").Append(r).Append('\n');
        yield return null; yield return null;
        try { r = V2Checks.RunWorld(); } catch (Exception e) { r = "FAIL RunWorld threw " + e.Message + "\n"; }
        p = 0; f = 0; Count(r, "V2Checks.RunWorld", ref p, ref f, fails); head.Append("V2Checks.RunWorld ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V2Checks.RunWorld\n").Append(r).Append('\n');
        // WpAChecks static + timed
        FocusLoop.Dismiss(); GlobalClock.Stop(); yield return null;
        var w = new StringBuilder();
        try { w.Append(WpAChecks.RunM1()); } catch (Exception e) { w.Append("FAIL RunM1 threw " + e.Message + "\n"); }
        yield return null;
        try { w.Append(WpAChecks.RunM2Static()); } catch (Exception e) { w.Append("FAIL RunM2Static threw " + e.Message + "\n"); }
        yield return null;
        // v7 §21 (the user: "just keep cubes on each grid"): riders are retired — a saved rider plays on its own grid like any cube (its home windows
        // only, no rider windows on other islands), so the timed check covers its home window (v4: three windows, one per island it rode)
        w.Append("INFO ").Append(WpAChecks.RiderArm()).Append('\n');
        {
            var rc = SequenceMaster.Cubes.Count > 0 ? SequenceMaster.Cubes[0] : null;
            bool home = rc != null && rc.windows.Count > 0;
            if (rc != null) foreach (var win in rc.windows) if (win.island != rc.Island) home = false;
            w.Append(home ? "PASS" : "FAIL").Append(" §21 a rider keeps to its own grid (no rider windows on other islands): windows ").Append(rc != null ? rc.windows.Count : 0).Append('\n');
        }
        yield return Wait(4f * 60f / GlobalClock.BPM * 3f + 0.8f);
        w.Append(WpAChecks.RiderCheck(1)).Append('\n');
        w.Append("INFO ").Append(WpAChecks.RiderDisarm()).Append('\n');
        yield return null;
        w.Append("INFO ").Append(WpAChecks.MoonArm()).Append('\n');
        yield return Wait(4f * 60f / GlobalClock.BPM * 3f + 0.8f);
        w.Append(WpAChecks.MoonCheck(3)).Append('\n');
        w.Append("INFO ").Append(WpAChecks.MoonDisarm()).Append('\n');
        yield return null;
        w.Append("INFO ").Append(WpAChecks.PlayAll()).Append('\n');
        yield return Wait(1.2f);
        w.Append("INFO ").Append(WpAChecks.StutterEngage()).Append('\n');
        yield return Wait(0.6f);
        w.Append(WpAChecks.StutterCheckEngaged()).Append('\n');
        w.Append("INFO ").Append(WpAChecks.StutterRelease()).Append('\n');
        yield return Wait(0.6f);
        w.Append(WpAChecks.StutterCheckReleased()).Append('\n');
        GlobalClock.Stop();
        p = 0; f = 0; Count(w.ToString(), "WpAChecks", ref p, ref f, fails); head.Append("WpAChecks M1 + M2 ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== WpAChecks\n").Append(w).Append('\n');
        // V2Checks.RunIntegration (timed)
        Prepare(); V3ChecksB.Prepare(); PathManager.SimOnly = false;
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        yield return null;
        r = V2Checks.RunIntegration();
        yield return null; yield return null;
        t0 = Time.realtimeSinceStartup;
        while (!V2Checks.IntegrationDone && Time.realtimeSinceStartup - t0 < 300f) yield return null;
        p = 0; f = 0; Count(V2Checks.IntegrationReport, "V2Checks.RunIntegration", ref p, ref f, fails); head.Append("V2Checks.RunIntegration ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V2Checks.RunIntegration ").Append(r).Append('\n').Append(V2Checks.IntegrationReport).Append('\n');
        // restore
        FocusLoop.Dismiss(); GlobalClock.Stop();
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        V3Fixes.RestoreSaves(saves);
        head.Append("synth late +").Append(Synth.LateEvents - late0).Append(" errors +").Append(Synth.Errors - err0).Append(" | ").Append(Synth.Stats()).Append('\n');
        if (fails.Length > 0) head.Append("FAILS:\n").Append(fails);
        OlderReport = head.ToString() + "\n" + body.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(OlderPath, OlderReport); } catch (Exception) { }
        PathManager.AutoHand = olderAutoWas;
        OlderDone = true;
    }

    // ================================================================== tiles (synchronous)
    public static string Tiles()
    {
        var o = new StringBuilder();
        var sm = SongManager.I;
        // 1. chord tiles wear no glyph
        int chordTiles = 0, icons = 0, drawn = 0;
        foreach (var kb in sm.Islands)
        {
            if (kb == null) continue;
            foreach (var t in kb.tiles)
            {
                if (t == null) continue;
                chordTiles++;
                if (t.MarkerIcon != null) icons++;
                var m = t.transform.Find("Marker");
                if (m != null && m.gameObject.activeInHierarchy) { var r = m.GetComponent<Renderer>(); if (r != null && r.enabled) drawn++; }
            }
        }
        o.Append(L(chordTiles > 0 && icons == 0 && drawn == 0, "R5 chord tiles wear no glyph (the role shapes are gone): MarkerIcon null, no marker drawn",
                   chordTiles + " chord tiles, icons " + icons + ", markers drawn " + drawn));
        // 2. the pictograms: every name registered; the fourteen kit keys map to fourteen different pictures
        int reg = 0; foreach (var n in KitGlyphs.Names) if (IconFactory.Has(n) && IconFactory.GetTexture(n) != null) reg++;
        var seen = new HashSet<string>(); foreach (int k in SynthBank.DrumKit) seen.Add(TileInteraction.KitGlyphFor(k));
        o.Append(L(reg == KitGlyphs.Names.Length && seen.Count == SynthBank.DrumKit.Length && !seen.Contains(KitGlyphs.Drum),
                   "R5 drum pictograms: " + KitGlyphs.Names.Length + " registered glyphs; every kit key (" + SynthBank.DrumKit.Length + ") has its own picture",
                   "registered " + reg + ", distinct " + seen.Count + ": " + string.Join(" ", seen)));
        // 3. a Moon's tiles wear the pictogram of the piece each plays (row: kick, snare, closed hat, open hat; column 5: clap, open hat, ride)
        int mi = sm.AddMoon();
        var moon = mi >= 0 && mi < sm.Moons.Count ? sm.Moons[mi] : null;
        if (moon == null) o.Append(L(false, "R5 Moon pictograms", "AddMoon failed"));
        else
        {
            int ok = 0, total = 0; string bad = null;
            foreach (var t in moon.tiles)
            {
                if (t == null) continue;
                total++;
                string want = TileInteraction.KitGlyphFor(VoiceRules.DrumPiece(t.gridX, t.gridZ, moon.rows));
                var m = t.transform.Find("Marker");
                var r = m != null ? m.GetComponent<Renderer>() : null;
                bool shown = r != null && r.enabled && m.gameObject.activeInHierarchy && r.sharedMaterial != null && r.sharedMaterial.mainTexture == IconFactory.GetTexture(want);
                if (t.KitLook && t.MarkerIcon == want && shown) ok++; else if (bad == null) bad = t.gridX + "," + t.gridZ + " " + t.MarkerIcon + " want " + want + " shown " + shown;
            }
            string rowNames = TileInteraction.KitGlyph(0, 0, moon.rows) + " " + TileInteraction.KitGlyph(0, 1, moon.rows) + " " + TileInteraction.KitGlyph(0, 2, moon.rows) + " " + TileInteraction.KitGlyph(0, 3, moon.rows)
                              + " | col 5: " + TileInteraction.KitGlyph(5, 1, moon.rows) + " " + TileInteraction.KitGlyph(5, 2, moon.rows) + " " + TileInteraction.KitGlyph(5, 3, moon.rows);
            bool rows = TileInteraction.KitGlyph(0, 0, moon.rows) == KitGlyphs.Kick && TileInteraction.KitGlyph(0, 1, moon.rows) == KitGlyphs.Snare
                        && TileInteraction.KitGlyph(0, 2, moon.rows) == KitGlyphs.HatClosed && TileInteraction.KitGlyph(0, 3, moon.rows) == KitGlyphs.HatOpen
                        && TileInteraction.KitGlyph(5, 1, moon.rows) == KitGlyphs.Clap && TileInteraction.KitGlyph(5, 3, moon.rows) == KitGlyphs.Ride;
            o.Append(L(total > 0 && ok == total && rows, "R5 a Moon's tiles wear the pictogram of the piece each plays (the marker drawn with that texture)",
                       ok + " / " + total + "; " + rowNames + (bad != null ? "; first bad " + bad : "")));
        }
        // 4. the drum brush dresses a chord island in pictograms; back to none when disarmed
        var pm = PathManager.I;
        var isl = sm.Islands[0];
        int drums = 0; for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) { drums = i; break; }
        pm.SelectInstrument(drums);
        bool dressed = true; foreach (var t in isl.tiles) if (t != null && (!t.KitLook || t.MarkerIcon != TileInteraction.KitGlyph(t.gridX, t.gridZ, isl.rows))) dressed = false;
        pm.SelectInstrument(0);
        bool bare = true; foreach (var t in isl.tiles) if (t != null && (t.KitLook || t.MarkerIcon != null)) bare = false;
        o.Append(L(dressed && bare, "R5 arming the Drums brush dresses chord tiles in pictograms; disarming leaves them bare", "dressed " + dressed + ", bare after " + bare));
        // 5. pitch = height: a tile stands PitchLook.Rise per semitone above the island's lowest tile (a clearer staircase than v4's 0.045)
        var kb2 = sm.Islands[2];
        TileInteraction lo = null, hi = null;
        foreach (var t in kb2.tiles) { if (t == null) continue; if (lo == null || t.midi < lo.midi) lo = t; if (hi == null || t.midi > hi.midi) hi = t; }
        float worst = 0f;
        foreach (var t in kb2.tiles) { if (t == null) continue; worst = Mathf.Max(worst, Mathf.Abs((t.Top.y - lo.Top.y) - PitchLook.Rise * (t.midi - lo.midi))); }
        o.Append(L(PitchLook.Rise >= 0.07f && PitchLook.Rise <= 0.1f && worst < 1e-3f, "R5 pitch = height: every tile stands Rise x semitones above the island's lowest (Rise " + F(PitchLook.Rise) + ", v4 0.045)",
                   "island 2 (" + kb2.assignedChord + "): " + (hi.midi - lo.midi) + " semitones = " + F(hi.Top.y - lo.Top.y) + " u, worst error " + worst.ToString("E1")));
        // 6. pitch = brightness: same hue, deeper low, lighter high (the ramp, and the island's own lowest / highest tiles)
        float h0, s0, v0, h1, s1, v1, hl, sl, vl, hh, sh, vh;
        Color tint = KeyBlock.TileTintOf(kb2.chordColor);
        Color.RGBToHSV(PitchLook.TileColor(tint, 0f), out h0, out s0, out v0);
        Color.RGBToHSV(PitchLook.TileColor(tint, 1f), out h1, out s1, out v1);
        Color.RGBToHSV(lo.BaseColor, out hl, out sl, out vl);
        Color.RGBToHSV(hi.BaseColor, out hh, out sh, out vh);
        float dh = Mathf.Abs(Mathf.DeltaAngle(h0 * 360f, h1 * 360f));
        o.Append(L(dh < 2f && v1 - v0 >= 0.3f && s0 > s1 && vh - vl >= 0.3f, "R5 pitch = brightness: the same hue from the lowest to the highest tile, low deeper (value +" + F(v1 - v0, "F2") + " up the ramp), high paler",
                   "ramp V " + F(v0, "F2") + " -> " + F(v1, "F2") + ", S " + F(s0, "F2") + " -> " + F(s1, "F2") + ", hue shift " + F(dh, "F1") + " deg; island 2 lowest V " + F(vl, "F2") + ", highest V " + F(vh, "F2")));
        return o.ToString();
    }

    // ================================================================== idle hover (stopped)
    static IEnumerator Idle()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        GlobalClock.Stop();
        var kb = FreeIsland(4);
        int z, x0;
        if (!PickableRow(kb, 6, out z, out x0)) { Add(L(false, "R5 idle hover", "no free pickable row of 6")); yield break; }
        var tA = kb.GetTile(x0, z); var tB = kb.GetTile(x0 + 1, z);
        tapped.Clear(); VoiceRules.Tap = OnTap;
        yield return Wait(0.2f);
        int a0 = PathManager.AuditionCount, i0 = PathManager.IdleAuditionCount;
        PathManager.SimPos = Scr(tA.Top);
        yield return null;                                                    // the entry frame: the pointer has not rested yet
        bool waits = PathManager.AuditionCount == a0 && pm.HoverAuditionTile == tA;
        float tEnter = Time.unscaledTime;
        while (PathManager.AuditionCount == a0 && Time.unscaledTime - tEnter < 0.6f) yield return null;
        float waited = Time.unscaledTime - tEnter;
        int a1 = PathManager.AuditionCount;
        VoiceRules.NoteEvent ev = default(VoiceRules.NoteEvent); int n9 = 0;
        foreach (var e in tapped) if (e.owner == VoiceRules.OwnerPreview) { ev = e; n9++; }
        var want = VoiceRules.AuditionEvent(Instruments.SlotOf(0), tA, 0, 0.4, TileInteraction.HoverVolume);
        bool once = a1 == a0 + 1 && PathManager.IdleAuditionCount == i0 + 1 && pm.HoverAuditionTile == tA && n9 == 1;
        bool note = ev.onDsp == 0.0 && ev.slot == Instruments.SlotOf(0) && ev.midi == want.midi && ev.vel == want.vel && ev.offDsp - GlobalClock.DspNow > 0.2;
        bool dwelt = pm.LastIdleDwell >= PathManager.IdleDwellSeconds && waited < PathManager.IdleDwellSeconds + 0.25f;
        yield return Frames(6);
        bool stays = PathManager.AuditionCount == a1;
        Add(L(waits && once && note && dwelt && stays, "R5 idle (stopped): resting on a free tile (" + F(PathManager.IdleDwellSeconds, "F2") + " s) auditions its note with the brush instrument (preview owner, at once), once per entry",
              "entry frame silent " + waits + "; played " + F(waited, "F3") + " s after the entry (rested " + F(pm.LastIdleDwell, "F3") + " s); auditions +" + (a1 - a0) + " (idle +" + (PathManager.IdleAuditionCount - i0) + "), then +" + (PathManager.AuditionCount - a1) + " over 6 frames resting; event owner 9 x" + n9 + " slot " + ev.slot + " midi " + ev.midi + " (want " + want.midi + ") vel " + ev.vel + " on " + F(ev.onDsp) + (ev.onDsp == 0.0 ? " (at once)" : "")));
        // the next tile, rested on: +1
        yield return Wait(0.15f);
        int b0 = PathManager.AuditionCount;
        PathManager.SimPos = Scr(tB.Top);
        yield return Wait(PathManager.IdleDwellSeconds + 0.15f);
        Add(L(PathManager.AuditionCount == b0 + 1 && pm.LastAuditionTiles.Count == 1 && pm.LastAuditionTiles[0] == tB, "R5 idle: resting on the next tile plays it too (once)", "auditions +" + (PathManager.AuditionCount - b0)));
        // a fast sweep across the row (to the HUD, say) plays nothing; the tile it stops on plays once the pointer rests there
        yield return Wait(0.2f);
        int s0 = PathManager.AuditionCount; float t0 = Time.realtimeSinceStartup;
        Vector3 prevPx = PathManager.SimPos; float px = 0f;
        TileInteraction lastSwept = null;
        for (int k = 0; k < 6; k++)
        {
            lastSwept = kb.GetTile(x0 + (k % 2 == 0 ? 5 - k / 2 : k / 2), z);
            PathManager.SimPos = Scr(lastSwept.Top); px += (PathManager.SimPos - prevPx).magnitude; prevPx = PathManager.SimPos;
            yield return null;
        }
        float took = Time.realtimeSinceStartup - t0;
        int sweep = PathManager.AuditionCount - s0;
        yield return Wait(PathManager.IdleDwellSeconds + 0.15f);
        int rested = PathManager.AuditionCount - s0;
        bool restTile = pm.LastAuditionTiles.Count == 1 && pm.LastAuditionTiles[0] == lastSwept;
        Add(L(sweep == 0 && rested == 1 && restTile, "R5 idle: a fast sweep across the row plays nothing; resting on the tile it stops on plays that one, once, after the dwell",
              sweep + " auditions over a " + F(took, "F2") + " s sweep of 6 entries (" + F(px / Mathf.Max(1e-3f, took), "F0") + " px/s; rest below " + F(PathManager.RestSpeedPx * OrbitCamera.DpiScale, "F0") + "), " + rested + " after resting, on the tile it stopped on " + restTile));
        // the HUD: over UI nothing plays
        PathManager.SimPos = Sea; yield return Frames(2);
        yield return Wait(0.15f);
        int u0 = PathManager.AuditionCount;
        pm.SimPointer(Scr(tA.Top), false, false, false, false, false, true);
        pm.SimPointer(Scr(tA.Top), false, false, false, false, false, true);
        bool ui = PathManager.AuditionCount == u0;
        // a locked world (menu, presentation, inspector, the deck's ghost): nothing
        WorldInput.Lock("r5");
        PathManager.SimPos = Scr(tB.Top); yield return Frames(3);
        bool locked = PathManager.AuditionCount == u0;
        WorldInput.Unlock("r5");
        yield return Frames(3);
        bool unlocked = PathManager.AuditionCount == u0;   // the pointer did not move: the unlock alone plays nothing
        Add(L(ui && locked && unlocked, "R5 idle: nothing auditions over the HUD or while the world is locked (and unlocking under a still pointer plays nothing)",
              "over UI " + ui + ", locked " + locked + ", after unlock " + unlocked));
        // the camera gliding under a still pointer: the tile under it changes, nothing plays (only entries the pointer makes)
        yield return Wait(0.2f);
        PathManager.SimPos = Scr(tA.Top); yield return Frames(2);
        yield return Wait(0.4f);
        int g0 = PathManager.AuditionCount; var h0 = pm.HoverTile;
        var cam = Camera.main.transform;
        cam.position += (kb.GetTile(x0 + 1, z).Top - tA.Top);   // one tile along the row: the tile before it slides under the pointer
        yield return Frames(3);
        var h1 = pm.HoverTile;
        Add(L(h0 == tA && h1 != null && h1 != tA && PathManager.AuditionCount == g0, "R5 idle: a view gliding under a still pointer changes the hovered tile but plays nothing",
              "hover " + (h0 != null ? h0.name : "-") + " -> " + (h1 != null ? h1.name : "-") + ", auditions +" + (PathManager.AuditionCount - g0)));
        // a Moon: the kit piece of the tile, whatever the brush
        int mi = sm.AddMoon();
        var moon = mi >= 0 ? sm.Moons[mi] : null;
        yield return Wait(0.9f);   // it rises
        if (moon != null)
        {
            var mt = moon.GetTile(5, 3);
            Frame(mt.Top, 8f, 0f, 62f);
            PathManager.SimPos = Sea; yield return Frames(2);
            yield return Wait(0.15f);
            tapped.Clear();
            PathManager.SimPos = Scr(mt.Top); yield return Wait(PathManager.IdleDwellSeconds + 0.15f);
            VoiceRules.NoteEvent me = default(VoiceRules.NoteEvent); int mn = 0;
            foreach (var e in tapped) if (e.owner == VoiceRules.OwnerPreview) { me = e; mn++; }
            Add(L(mn == 1 && me.slot == SynthBank.DrumSlot && me.midi == VoiceRules.DrumPiece(5, 3, moon.rows) && pm.HoverTile == mt, "R5 idle on a Moon: the tile's kit piece plays (drums, whatever the brush)",
                  "events " + mn + ", slot " + me.slot + " key " + me.midi + " (ride " + VoiceRules.DrumPiece(5, 3, moon.rows) + ")"));
        }
        else Add(L(false, "R5 idle on a Moon", "AddMoon failed"));
        VoiceRules.Tap = null;
        PathManager.SimPos = Sea;
    }

    // ================================================================== playing: a tile of an island that is not lit
    static IEnumerator Playing()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        GlobalClock.Stop(); GlobalClock.Seek(0); GlobalClock.LoopSong = true;
        // an island of a column that plays later (not lit for the next seconds)
        KeyBlock kb = null;
        foreach (var k in sm.Islands) if (k != null && !k.IsMoon && k.column >= 3 && k.startBeatOffset >= 8f) { kb = k; break; }
        if (kb == null) kb = FreeIsland(3, 0);
        int z, x0;
        if (!PickableRow(kb, 2, out z, out x0)) { Add(L(false, "R5 playing", "no free pickable pair on island " + (kb != null ? kb.assignedChord : "-"))); yield break; }
        var t = kb.GetTile(x0, z);
        GlobalClock.Play();
        yield return Wait(0.5f);
        tapped.Clear(); VoiceRules.Tap = OnTap;
        Synth.ResetDuckMin();
        int trig0 = Synth.DuckTriggers;
        bool harmonic = VoiceRules.PreviewAllowed(t, Instruments.SlotOf(0));
        int ia0 = PathManager.IdleAuditionCount;
        PathManager.SimPos = Scr(t.Top);
        float tw0 = Time.realtimeSinceStartup;
        while (PathManager.IdleAuditionCount == ia0 && Time.realtimeSinceStartup - tw0 < 0.8f) yield return null;
        double callDsp = pm.LastAuditionDsp;   // the moment the (rested) hover played
        VoiceRules.NoteEvent ev = default(VoiceRules.NoteEvent); int n9 = 0;
        foreach (var e in tapped) if (e.owner == VoiceRules.OwnerPreview) { ev = e; n9++; }
        double beat = (ev.onDsp - GlobalClock.DspTimeOfBeat(0.0)) * GlobalClock.BeatsPerSecond;
        double frac = Math.Abs(beat * 4.0 - Math.Round(beat * 4.0));
        bool lit = sm.IsActiveAt(kb, (float)GlobalClock.SongBeatD);
        Add(L(!harmonic && !lit && n9 == 1 && ev.onDsp >= callDsp + 0.029 && ev.onDsp <= callDsp + 0.25 / GlobalClock.BeatsPerSecond + 0.04 && frac < 0.02,
              "R5 playing: a tile of an island that is NOT lit auditions anyway (v4 only flashed it), on the next 16th, on the preview bus",
              "PreviewAllowed " + harmonic + " (lit " + lit + "), events " + n9 + ", onset " + F((ev.onDsp - callDsp) * 1000.0, "F0") + " ms after the audition (the pointer rested " + F(pm.LastIdleDwell, "F3") + " s), beat " + F(beat, "F3")));
        // the song ducks under it (about -8 dB) and comes back
        float minG = 1f; int maxPv = 0; float tw = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tw < 0.6f) { minG = Mathf.Min(minG, Synth.DuckGain); maxPv = Mathf.Max(maxPv, Synth.PreviewVoices); yield return null; }
        float dmin = Synth.DuckMin;
        yield return Wait(0.8f);
        float back = Synth.DuckGain;
        float want = Mathf.Pow(10f, Synth.DuckDb / 20f);
        Add(L(Synth.DuckTriggers > trig0 && dmin <= want + 0.06f && dmin >= want - 0.03f && back >= 0.97f && maxPv >= 1,
              "R5 playing: the song bus ducks under the audition (about " + F(Synth.DuckDb, "F0") + " dB) and recovers; the audition sounds on the preview bus",
              "duck triggers +" + (Synth.DuckTriggers - trig0) + ", min gain " + F(dmin) + " (" + F(20f * Mathf.Log10(Mathf.Max(1e-4f, dmin)), "F1") + " dB; frames saw " + F(minG) + "), after 1.4 s " + F(back) + ", preview voices up to " + maxPv));
        VoiceRules.Tap = null;
        PathManager.SimPos = Sea;
        GlobalClock.Stop();
    }

    // ================================================================== duck: recorded steady tones
    struct Wav { public float[] mono; public int sr; }
    static Wav ReadWav(string path)
    {
        var w = new Wav();
        var b = File.ReadAllBytes(path);
        int sr = BitConverter.ToInt32(b, 24), ch = BitConverter.ToInt16(b, 22);
        int pos = 12, dataAt = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4); int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
            pos += 8 + len;
        }
        int frames = dataAt < 0 ? 0 : dataLen / (2 * ch);
        w.mono = new float[frames]; w.sr = sr;
        for (int i = 0; i < frames; i++)
        {
            float acc = 0f;
            for (int c = 0; c < ch; c++) acc += BitConverter.ToInt16(b, dataAt + (i * ch + c) * 2) / 32768f;
            w.mono[i] = acc / ch;
        }
        return w;
    }
    static float Rms(Wav w, double from, double to)
    {
        int a = Mathf.Clamp((int)(from * w.sr), 0, w.mono.Length), b = Mathf.Clamp((int)(to * w.sr), 0, w.mono.Length);
        if (b <= a) return 0f;
        double acc = 0; for (int i = a; i < b; i++) acc += w.mono[i] * w.mono[i];
        return (float)Math.Sqrt(acc / (b - a));
    }
    static double Onset(Wav w)
    {
        float peak = 0f; foreach (var s in w.mono) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < w.mono.Length; i++) if (Mathf.Abs(w.mono[i]) > peak * 0.1f) return i / (double)w.sr;
        return 0.0;
    }

    /// <summary>Records a 2.3 s Lead tone on the song bus (<paramref name="preview"/> false) or on the preview bus, with or without a
    /// Synth.Duck 1.0 s into it, and returns its RMS envelope in 50 ms windows from the tone's onset (<paramref name="env"/>).</summary>
    static IEnumerator Tone(bool preview, bool duck, string file, List<float> env)
    {
        env.Clear();
        Synth.AllNotesOff(true);
        yield return Wait(0.5f);
        Synth.StartRecording(2.7f);
        yield return null;
        double t0 = Synth.DspNow + 0.15;
        const int slot = 3, key = 64, vel = 72;
        if (preview) Synth.Audition(slot, key, vel, t0, t0 + 2.3);
        else Synth.Note(slot, key, vel, t0, t0 + 2.3, ToneOwner);
        if (duck) Synth.Duck(t0 + 1.0, 0.4f);
        float tw = Time.realtimeSinceStartup;
        while (!Synth.RecordingDone && Time.realtimeSinceStartup - tw < 5f) yield return null;
        string path = Path.Combine(V2Checks.CapturePath, file);
        if (!Synth.SaveRecording(path)) yield break;
        var w = ReadWav(path);
        double on = Onset(w);
        for (int k = 0; k < 46; k++) env.Add(Rms(w, on + k * 0.05, on + (k + 1) * 0.05));
    }

    /// <summary>Mean level difference (dB) of two envelopes over windows [from, to) seconds after the onset.</summary>
    static float Diff(List<float> a, List<float> b, double from, double to)
    {
        double acc = 0; int n = 0;
        for (int k = (int)Math.Round(from / 0.05); k < (int)Math.Round(to / 0.05) && k < a.Count && k < b.Count; k++)
        {
            if (a[k] <= 1e-6f || b[k] <= 1e-6f) continue;
            acc += 20.0 * Math.Log10(a[k] / b[k]); n++;
        }
        return n > 0 ? (float)(acc / n) : 99f;
    }

    /// <summary>The same tone recorded with and without a duck (paired, so the tone's own tremolo cancels out), on the song bus and on the
    /// preview bus; chorus and reverb off on the slot while recording (restored after).</summary>
    static IEnumerator Duck()
    {
        GlobalClock.Stop();
        Synth.SetSlotChorus(3, 0f); Synth.SetSlotReverb(3, 0f);
        var songD = new List<float>(); var songN = new List<float>(); var prevD = new List<float>(); var prevN = new List<float>();
        try
        {
            yield return Tone(false, true, "r5_duck_song.wav", songD);
            yield return Tone(false, false, "r5_duck_song_ref.wav", songN);
            yield return Tone(true, true, "r5_duck_preview.wav", prevD);
            yield return Tone(true, false, "r5_duck_preview_ref.wav", prevN);
        }
        finally { Instruments.PushToSynth(); }
        float want = Synth.DuckDb;
        // the duck: from 0.98 s to 1.4 s after the onset, then a ~70 ms release
        float sIn = Diff(songD, songN, 1.05, 1.35), sBefore = Diff(songD, songN, 0.2, 0.9), sAfter = Diff(songD, songN, 1.75, 2.2);
        float pIn = Diff(prevD, prevN, 1.05, 1.35), pBefore = Diff(prevD, prevN, 0.2, 0.9), pAfter = Diff(prevD, prevN, 1.75, 2.2);
        string env = ""; for (int k = 16; k < 32 && k < songD.Count && k < songN.Count; k++) env += F(20f * Mathf.Log10(Mathf.Max(1e-6f, songD[k]) / Mathf.Max(1e-6f, songN[k])), "F1") + " ";
        Add(L(songN.Count > 0 && sIn <= want + 1f && sIn >= want - 1f && Mathf.Abs(sBefore) <= 0.6f && Mathf.Abs(sAfter) <= 0.8f,
              "R5 duck (recorded, paired): a tone on the song bus is " + F(want, "F0") + " dB lower while ducked and the same before / after",
              "during " + F(sIn, "F2") + " dB, before " + F(sBefore, "F2") + ", after " + F(sAfter, "F2") + " (50 ms steps 0.8-1.6 s: " + env + "; Captures/r5_duck_song*.wav)"));
        Add(L(prevN.Count > 0 && Mathf.Abs(pIn) <= 0.6f && Mathf.Abs(pBefore) <= 0.6f && Mathf.Abs(pAfter) <= 0.6f,
              "R5 duck (recorded, paired): the same tone on the preview bus is never ducked",
              "during " + F(pIn, "F2") + " dB, before " + F(pBefore, "F2") + ", after " + F(pAfter, "F2") + " (Captures/r5_duck_preview*.wav)"));
    }

    // ================================================================== drafting: the run a click adds
    static bool captureRun;
    static IEnumerator RunArp()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        GlobalClock.Stop();
        var kb = FreeIsland(3);
        int z, x0;
        if (!PickableRow(kb, 6, out z, out x0)) { Add(L(false, "R5 run", "no free pickable row of 6")); yield break; }
        var tA = kb.GetTile(x0, z); var tD = kb.GetTile(x0 + 3, z); var tF = kb.GetTile(x0 + 5, z);
        pm.BrushTicks = 24;
        yield return Click(Scr(tA.Top));
        var d = pm.Draft;
        if (d == null) { Add(L(false, "R5 run", "no draft")); yield break; }
        try
        {
            GlobalClock.Pause();   // stopped timing first
            yield return Wait(0.25f);
            tapped.Clear(); VoiceRules.Tap = OnTap;
            int r0 = PathManager.RunAuditionCount;
            double call = GlobalClock.DspNow;
            PathManager.SimPos = Scr(tD.Top);
            yield return Frames(3);
            var line = new List<TileInteraction> { kb.GetTile(x0 + 1, z), kb.GetTile(x0 + 2, z), tD };
            bool tilesOk = pm.LastAuditionTiles.Count == 3 && pm.PreviewTiles.Count == 3 && pm.AuditionTile == tD;
            for (int i = 0; tilesOk && i < 3; i++) tilesOk = pm.LastAuditionTiles[i] == line[i] && pm.PreviewTiles[i] == line[i];
            var ev = new List<VoiceRules.NoteEvent>(); foreach (var e in tapped) if (e.owner == VoiceRules.OwnerPreview) ev.Add(e);
            int slot = Instruments.SlotOf(d.instrument);
            bool order = ev.Count == 3, gaps = ev.Count == 3, lastLonger = ev.Count == 3;
            for (int i = 0; order && i < 3; i++) order = ev[i].midi == VoiceRules.AuditionEvent(slot, line[i], d.octave, 0.1, 0.7f).midi;
            for (int i = 1; gaps && i < 3; i++) gaps = Math.Abs(ev[i].onDsp - ev[i - 1].onDsp - TileInteraction.RunGap) < 1e-4;
            if (ev.Count == 3) lastLonger = ev[2].offDsp - ev[2].onDsp > ev[0].offDsp - ev[0].onDsp + 0.05;
            bool soon = ev.Count > 0 && ev[0].onDsp > call && ev[0].onDsp < call + 0.2;
            string mids = ""; foreach (var e in ev) mids += e.midi + "@" + F((e.onDsp - call) * 1000.0, "F0") + "ms ";
            Add(L(tilesOk && order && gaps && lastLonger && soon && PathManager.RunAuditionCount == r0 + 1,
                  "R5 drafting (paused): hovering a tile 3 steps away auditions the RUN a click adds (LineTiles in order) as an arpeggio " + F(TileInteraction.RunGap * 1000f, "F0") + " ms apart, the target held longest",
                  "tiles " + tilesOk + ", notes " + ev.Count + ": " + mids + "(pitch order " + order + ", gaps " + gaps + ", last longer " + lastLonger + ")"));
            // the hologram peeks toward the target
            yield return Wait(0.2f);
            bool peek = d.PeekTile == tD && d.PeekAmount > 0.5f;
            Add(L(peek, "R5 drafting: the hologram peeks toward the hovered tile (a lean / hop in place)", "peek tile " + (d.PeekTile == tD) + ", amount " + F(d.PeekAmount, "F2")));
            if (captureRun)
            {
                var keep = Camera.main.transform.position; var keepR = Camera.main.transform.rotation;
                Frame(Vector3.Lerp(tA.Top, tD.Top, 0.35f) + Vector3.up * 0.3f, 5.2f, -32f, 30f);
                PathManager.SimPos = Scr(tD.Top); yield return Frames(3);
                yield return Wait(0.25f);
                yield return Capture("r5_peek");
                Camera.main.transform.position = keep; Camera.main.transform.rotation = keepR;
                PathManager.SimPos = Scr(tD.Top); yield return Frames(2);
            }
            // playing: the run starts on the next 16th
            GlobalClock.Play();
            yield return Wait(0.3f);
            PathManager.SimPos = Sea; yield return Frames(2);
            yield return Wait(0.15f);
            tapped.Clear();
            call = GlobalClock.DspNow;
            PathManager.SimPos = Scr(tF.Top);
            yield return Frames(3);
            ev.Clear(); foreach (var e in tapped) if (e.owner == VoiceRules.OwnerPreview) ev.Add(e);
            double beat = ev.Count > 0 ? (ev[0].onDsp - GlobalClock.DspTimeOfBeat(0.0)) * GlobalClock.BeatsPerSecond : -1.0;
            double frac = Math.Abs(beat * 4.0 - Math.Round(beat * 4.0));
            bool grid = ev.Count == 5 && ev[0].onDsp >= call + 0.029 && frac < 0.02;
            for (int i = 1; grid && i < ev.Count; i++) grid = Math.Abs(ev[i].onDsp - ev[i - 1].onDsp - TileInteraction.RunGap) < 1e-4;
            Add(L(grid && pm.LastAuditionTiles.Count == 5, "R5 drafting (playing): the run of 5 starts on the next 16th, " + F(TileInteraction.RunGap * 1000f, "F0") + " ms apart",
                  "notes " + ev.Count + ", first at beat " + F(beat, "F3") + ", " + F((ev.Count > 0 ? ev[0].onDsp - call : 0.0) * 1000.0, "F0") + " ms after the hover"));
            // what you hear = what a click adds
            var heard = new List<TileInteraction>(pm.LastAuditionTiles);
            int n0 = d.nodes.Count;
            yield return Click(Scr(tF.Top));
            bool same = d.nodes.Count == n0 + heard.Count;
            for (int i = 0; same && i < heard.Count; i++) same = d.nodes[n0 + i] == heard[i];
            Add(L(same, "R5 drafting: a click adds exactly the tiles the run auditioned, in that order", "nodes " + n0 + " -> " + d.nodes.Count + ", heard " + heard.Count));
            PathManager.SimPos = Sea; yield return Frames(3);
            Add(L(d.PeekTile == null, "R5 drafting: off the island the hologram stops peeking", "peek tile " + (d.PeekTile != null ? d.PeekTile.name : "none")));
        }
        finally
        {
            VoiceRules.Tap = null;
            PathManager.SimPos = Sea;
            if (pm.IsDrawing) pm.CancelPath(false);
        }
    }

    // ================================================================== the melody line
    static IEnumerator Melody(bool captures)
    {
        var pm = PathManager.I;
        GlobalClock.Stop();
        var kb = FreeIsland(4);
        int z, x0;
        if (!PickableRow(kb, 6, out z, out x0)) { Add(L(false, "R5 melody", "no free pickable row of 6")); yield break; }
        // a diagonal climb makes a contour with heights: (x0, z) then up the rows where they exist
        var tA = kb.GetTile(x0, z); var tB = kb.GetTile(x0 + 1, z); var tD = kb.GetTile(x0 + 3, z);
        pm.BrushTicks = 24;
        yield return Click(Scr(tA.Top));
        var d = pm.Draft;
        if (d == null) { Add(L(false, "R5 melody", "no draft")); yield break; }
        try
        {
            yield return Click(Scr(tB.Top));
            pm.SetDraftDuration(12);
            yield return Click(Scr(tD.Top));
            pm.SetDraftDuration(48);
            PathManager.SimPos = Sea;
            yield return Frames(3);
            var pts = MelodyLine.Points;
            float span = MelodyLineSpan(d, kb);
            bool count = MelodyLine.Shown && pts.Count == d.nodes.Count && d.nodes.Count == 4;
            bool xs = count, ys = count;
            float acc = 0f;
            for (int i = 0; xs && i < pts.Count; i++) { xs = Mathf.Abs(pts[i].x - acc / span) < 1e-3f; acc += d.DurBeats(i); }
            for (int i = 0; ys && i < pts.Count; i++)
                for (int j = i + 1; ys && j < pts.Count; j++)
                    ys = Math.Sign(Mathf.Round((pts[j].y - pts[i].y) * 1000f)) == Math.Sign(d.nodes[j].midi - d.nodes[i].midi);
            string sx = "", sy = ""; foreach (var p in pts) { sx += F(p.x, "F3") + " "; sy += F(p.y, "F2") + " "; }
            Add(L(count && xs && ys, "R5 melody line: one dot per drafted note, x = its start by the lengths (24 12 12 48 ticks), y = its pitch (same order as the notes' pitches)",
                  "shown " + MelodyLine.Shown + ", dots " + pts.Count + " / nodes " + d.nodes.Count + ", x " + sx + "(span " + F(span, "F1") + " beats), y " + sy));
            // the ghost run: hovering a tile two steps on
            var tF = kb.GetTile(x0 + 5, z);
            PathManager.SimPos = Scr(tF.Top);
            yield return Frames(3);
            var gp = MelodyLine.GhostPoints;
            bool ghost = gp.Count == pm.PreviewTiles.Count && gp.Count == 2 && gp[0].x > pts[pts.Count - 1].x && gp[1].x > gp[0].x;
            Add(L(ghost, "R5 melody line: the run a click would add is a ghost extension after the last dot (one per tile)", "ghost dots " + gp.Count + " / preview tiles " + pm.PreviewTiles.Count));
            Rect card = MelodyLine.ScreenRect, row = DurationPicker.RowScreenRect;
            bool apart = card.width > 50f && !card.Overlaps(row);
            int inside = 0; float h = ProjectConfig.TileSize * 0.5f;
            foreach (var t in kb.tiles) { if (t == null) continue; for (int k = 0; k < 4; k++) { Vector3 s = Scr(t.Top + new Vector3((k & 1) == 0 ? -h : h, 0f, (k & 2) == 0 ? -h : h)); if (s.z > 0f && card.Contains(new Vector2(s.x, s.y))) inside++; } }
            Add(L(apart && inside == 0, "R5 melody line: its card sits beside the length row, never over the row or the island's tiles", "card " + card + ", row " + row + ", tile corners inside " + inside));
            if (captures) { yield return Wait(0.35f); yield return Capture("r5_draft_melody"); }
            int rp0 = MelodyLine.RepaintCount;
            yield return Frames(10);
            int idleRepaints = MelodyLine.RepaintCount - rp0;
            // Backspace shrinks it, finishing hides it
            PathManager.SimPos = Sea;
            yield return Frames(2);
            Key(KeyCode.Backspace);
            yield return Frames(3);
            int dotsAfter = MelodyLine.Points.Count, nodesAfter = d.nodes.Count;
            Key(KeyCode.Return);
            yield return Frames(3);
            Add(L(idleRepaints == 0 && dotsAfter == 3 && nodesAfter == 3 && !MelodyLine.Shown, "R5 melody line: repainted only on change; Backspace takes its last dot back; finishing the draft hides it",
                  "repaints over 10 idle frames " + idleRepaints + ", after Backspace " + dotsAfter + " dots / " + nodesAfter + " nodes, shown after finish " + MelodyLine.Shown));
        }
        finally
        {
            PathManager.SimPos = Sea;
            if (pm.IsDrawing) pm.CancelPath(false);
        }
    }

    /// <summary>The melody line's time span (beats): the island's length, grown to whole bars to fit the draft and its ghost run.</summary>
    static float MelodyLineSpan(AudioCube d, KeyBlock kb)
    {
        float total = 0f; for (int i = 0; i < d.nodes.Count; i++) total += d.DurBeats(i);
        float ghost = PathManager.I.PreviewTiles.Count * PathManager.I.BrushTicks / (float)ProjectConfig.TicksPerBeat;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        return Mathf.Max(kb.LengthBeats, Mathf.Ceil((total + ghost) / bpb) * bpb);
    }

    // ================================================================== K's repeat belt: cubes ride the jerk with their island
    /// <summary>An island at ×2 (SongManager.SetRepeat) jerks one slot at the pass boundary; a 16th-note cube hopping up and down one grid
    /// column of it must stay in that column in island space through the jerk (v4 captured the hop's start in world space: a cube mid-hop
    /// lagged behind the island), and the island really travelled a slot.</summary>
    static IEnumerator BeltRide()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        GlobalClock.Stop();
        var kb0 = FreeIsland(4);
        int isl = kb0 != null ? sm.Islands.IndexOf(kb0) : -1;
        if (isl < 0) { Add(L(false, "R5 belt ride", "no free island")); yield break; }
        sm.SetRepeat(isl, 2);
        yield return null; yield return null;
        var kb = sm.Islands[isl];
        if (kb == null || kb.Passes < 2 || sm.ColumnPasses(kb.column) < 2) { Add(L(false, "R5 belt ride", "SetRepeat x2 did not take (passes " + (kb != null ? kb.Passes : -1) + ")")); yield break; }
        // muted: this check is about positions (a 16th note right at the play start is only 0.12 s ahead: one editor hitch would make it late)
        var st = new CubeState { instrument = 0, measure = isl, xs = new[] { 2, 2, 2, 2 }, zs = new[] { 0, 1, 2, 3 }, rests = new bool[4], mods = new int[4], step = (int)StepLen.Sixteenth,
                                 gate = 1, mode = (int)PathMode.PingPong, volume = 0.6f, muted = true, hits = -1, twinOf = -1 };
        var c = pm.RestoreCube(st);
        SequenceMaster.RecalculateTimeline();
        if (c == null) { Add(L(false, "R5 belt ride", "no cube")); yield break; }
        int col = kb.column;
        float s0 = sm.ColumnStart(col), pass = sm.PassLength(col);
        float colX = kb.transform.InverseTransformPoint(kb.GetTile(2, 0).Top).x;
        Frame(kb.Center + Vector3.right * (KeyBlock.SlotPitch * 0.5f), 22f, 0f, 55f);
        yield return Wait(0.4f);   // the rebuild and the new view settle before the transport starts (its first notes are scheduled 0.12 s ahead)
        GlobalClock.Seek(Mathf.Max(0f, s0 + pass - 1.5f));
        GlobalClock.Play();
        yield return Wait(0.3f);
        float x0 = kb.transform.position.x, travel = 0f, worstOff = 0f, worstRideOff = 0f, maxStep = 0f;
        int rideFrames = 0, rideHops = 0, frames = 0;
        float tw = Time.realtimeSinceStartup, px = x0;
        while (GlobalClock.SongBeatD < s0 + pass + 1.2 && Time.realtimeSinceStartup - tw < 8f)
        {
            yield return null;   // after this frame's Updates: the island and the cube are posed
            Vector3 local = kb.transform.InverseTransformPoint(c.transform.position);
            float off = Mathf.Abs(local.x - colX);
            worstOff = Mathf.Max(worstOff, off);
            float ix = kb.transform.position.x;
            maxStep = Mathf.Max(maxStep, Mathf.Abs(ix - px)); px = ix;
            if (kb.Riding) { rideFrames++; if (c.Hopping) rideHops++; worstRideOff = Mathf.Max(worstRideOff, off); }
            travel = Mathf.Max(travel, ix - x0);
            frames++;
        }
        GlobalClock.Stop();
        Add(L(rideFrames > 0 && rideHops > 0 && travel >= KeyBlock.SlotPitch * 0.9f && worstOff < 0.08f,
              "R5 belt (K's repeat x2): a cube hopping on the island rides the pass-boundary jerk with it (in island space it never leaves its grid column)",
              "island travelled " + F(travel, "F2") + " u (a slot " + F(KeyBlock.SlotPitch, "F2") + ", up to " + F(maxStep, "F2") + " u per frame); " + rideFrames + " riding frames, " + rideHops + " of them mid-hop; worst island-local drift " + F(worstRideOff, "F3") + " u while riding, " + F(worstOff, "F3") + " u overall (" + frames + " frames)"));
    }

    // ================================================================== captures
    static IEnumerator Shots()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        GlobalClock.Stop();
        // an island at rest: no glyphs, the staircase, the brightness ramp (the fixture's island 0 carries cubes)
        var kb = sm.Islands[0];
        Frame(kb.Center + new Vector3(0.6f, 0.6f, 0.4f), 10.5f, -28f, 38f);
        yield return Wait(0.5f);
        yield return Capture("r5_island_rest");
        var k2 = sm.Islands[2];
        Frame(k2.Center + new Vector3(0.4f, 0.5f, 0.3f), 10f, 24f, 34f);
        yield return Wait(0.3f);
        yield return Capture("r5_island_side");
        // the island header ribbon hangs from the front edge: the taller front tiles must not swallow it
        IslandHeader.Show(kb);
        Frame(kb.Center + new Vector3(0.3f, 0.4f, -0.6f), 12.5f, -8f, 42f);
        yield return Wait(0.6f);
        yield return Capture("r5_header");
        IslandHeader.Hide();
        // a Moon with its pictograms
        int mi = sm.AddMoon();
        yield return Wait(1f);
        if (mi >= 0) { var m = sm.Moons[mi]; Frame(m.Center, 9.5f, -10f, 56f); yield return Wait(0.4f); yield return Capture("r5_moon"); }
        // the inspector's grid (the six-node cube on island 1)
        AudioCube c = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.Island == sm.Islands[1]) { c = x; break; }
        if (c != null)
        {
            if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(1, true); }
            yield return Wait(0.6f);
            CubeInspector.Open(c);
            yield return Wait(1.2f);
            yield return Capture("r5_inspector_grid");
            CubeInspector.CloseImmediate();
            FocusLoop.Dismiss(); GlobalClock.Stop();
        }
        Add(L(true, "R5 captures: r5_island_rest, r5_island_side, r5_header, r5_moon, r5_inspector_grid (+ r5_draft_melody, r5_peek) — LOOK at them"));
    }
}
