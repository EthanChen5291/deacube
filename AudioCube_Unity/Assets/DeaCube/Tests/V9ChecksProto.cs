using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// v9 (builder G): the gallery's "get proto" — the user's own MIDI (sample.mid, the repository root) built the way a DeaCube player builds a
/// song (tools/gallery/midi_grid.py, round 3; the user: "the point of deacube is to abstract away the piano" and "more creative ... more effects"):
/// a chord card per bar, one lane per voice (front to back: the lead and its stairs, the bass lowered, the harmony, the high lane raised), paths
/// over the chord's tiles with NUDGES for the notes a card has no tile for, a STAIRS island on every descending measure of any voice, a LAUNCH on
/// every crash of the MIDI (the strings' swells the big ones), REWIND on the hook, lead KEYBOARDS in B, the sax's PEDAL on a long grid, 1-2-bar
/// fragments as their own grids, and a device signature of its own per section. Checks the song file and the world (14 sections with their
/// letters and roles, at most 4 lanes a column, every grid at its lane's z and register, 11 Moons with the electronic kit, the fx colour); the
/// devices where the MIDI has the moment (read here, independently of the builder: the descending measures and the crash bars come from the
/// MIDI file itself); what DeaCube plays against the MIDI (harmony coverage per voice, the groove exact — bass onsets and pitches, the drums — the
/// sax's contour, the nudged steps on pitch); that the synth is sent what the cubes schedule while the song plays to its end; the menu preview;
/// the load and frame time against crush; the levels; captures (one per section from the follow camera). Report: Captures/proto9_report.txt.
/// Run(parts): "index open faith live perf look preview shelf sound load" (any subset, space separated) or "all".
/// </summary>
public static class V9ChecksProto
{
    public const string Id = "get-proto";
    public static string Report = "";
    public static bool Done = true;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "proto9_report.txt");
    static int num;
    static readonly List<string> errors = new List<string>();
    static StringBuilder sb;
    static SongManager SM => SongManager.I;

    // the MIDI's tracks (midi.read order) -> the DeaCube voice each plays on (tools/gallery/imports/get_proto.py)
    static readonly string[] TrackName = { "sax", "epiano", "piano", "bass", "flute", "square", "mbox", "scifi", "drums", "strings", "tri" };
    static readonly int[] TrackGroup = { 3, 0, 8, 4, 3, 3, 5, 10, 9, 6, 3 };
    static readonly string[] TrackVoice = { "alto sax", "e.piano", "grand", "synth bass", "flute", "square", "music box", "atmosphere", "electronic", "tremolo", "ocarina" };
    // round 3: the intro bar, then 4-measure sections: x2 belts / rewinds for the 8-bar phrases, the pedal's one-bar columns (B), B's second half
    // alone, C' as two sections of one-bar columns
    static readonly string[] Letters = { "I", "A", "A'", "B", "C", "A", "D", "B", "B", "C", "C'", "C'", "E", "O" };
    static readonly int[] Roles = { 1, 2, 2, 4, 3, 2, 5, 4, 4, 3, 3, 3, 5, 6 };
    const int Sections = 14, Columns = 23, Repeated = 9, Rewound = 2, Keyboards = 6;
    // the lanes front to back (z, the chord grids' registers): the lead (and every stairs), the bass (lowered; in D the square's raised grids),
    // the harmony (raised in E: the higher chorus), the high lane (raised: a tower on its turn)
    static readonly float[] LaneZ = { 0f, 9f, 18f, 27f };
    static readonly int[][] LaneRegs = { new[] { 0, 1 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1 } };
    static readonly string[] LaneName = { "lead", "bass", "harmony", "high" };
    static readonly string[][] LaneVoices = {
        new[] { "alto sax", "music box", "ocarina", "atmosphere", "flute" }, new[] { "synth bass", "square" }, new[] { "grand", "e.piano", "tremolo", "music box" },
        new[] { "flute", "music box", "square", "atmosphere" } };
    // the descending measures the builder could not give a stairs (bar, voice, why): checked as the only ones missing
    static readonly string[] StairExceptions = { "22 sax: the second pass of bar 18, which does not fall", "52 sax: the pedal's long grid holds the lead lane" };
    static readonly int[] Kit = { 36, 38, 44, 40, 49, 39, 54, 75 };
    const int Shift = 480;   // MIDI ticks: the file's music sits an 8th late

    static void Line(bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" P9-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }

    public static string RunAll(bool captures = true) => Run("all", captures);

    public static string Run(string parts, bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        sb = new StringBuilder();
        SequenceMaster.I.StartCoroutine(Guarded(" " + (parts ?? "all") + " ", captures));
        return "started";
    }

    static bool Has(string parts, string p) => parts.Contains(" all ") || parts.Contains(" " + p + " ");

    static IEnumerator Guarded(string parts, bool captures)
    {
        sb.Append("V9ChecksProto ").Append(DateTime.Now.ToString("HH:mm:ss")).Append(" parts:").Append(parts).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        var body = Routine(parts, captures);
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { VoiceRules.Tap = null; FocusLoop.Release(); GlobalClock.ClearRegion(); GlobalClock.Stop(); KeyStage.Enabled = true; foreach (var c in SequenceMaster.Cubes) if (c != null) c.muted = false; } catch (Exception) { }
        try { RenderSettings.fogStartDistance = global::Look.FogStart; RenderSettings.fogEndDistance = global::Look.FogEnd; } catch (Exception) { }   // the map shots lift the haze
        try { if (MainMenu.IsShown) MainMenu.Hide(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog;
        Line(errors.Count == 0, "no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
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
        Application.runInBackground = true;
    }

    static IEnumerator Routine(string parts, bool captures)
    {
        Prepare();
        yield return null;
        Gallery.ClearTestSongs();
        Gallery.Reload();
        if (Has(parts, "index")) CheckIndex();
        if (Has(parts, "open") || Has(parts, "faith") || Has(parts, "live") || Has(parts, "look") || Has(parts, "perf") || Has(parts, "sound") || Has(parts, "load"))
            yield return OpenAndCheck(Has(parts, "open"));
        if (Has(parts, "load")) yield return LoadProbe();
        if (Has(parts, "faith")) Faithfulness();
        if (Has(parts, "look")) yield return Look(captures);
        if (Has(parts, "live")) yield return Live();
        if (Has(parts, "perf")) yield return Perf();
        if (Has(parts, "sound")) yield return Sound();
        if (Has(parts, "preview")) Preview();
        if (Has(parts, "shelf")) yield return Shelf(captures);
    }

    // ================================================================== round 2 drafts: a SongState JSON from a path, its captures and its notes
    /// <summary>Loads the song in <paramref name="jsonPath"/> (a draft of round 2), takes its captures (Captures/<paramref name="prefix"/>_*.png: the
    /// overview, a close-up of <paramref name="col"/> while it plays, the moments at <paramref name="beats"/> (song beats, comma list), the follow
    /// camera's view) and records what the synth was sent from beat <paramref name="from"/> to <paramref name="to"/> (VoiceRules.Tap:
    /// Captures/<paramref name="prefix"/>_notes.txt: song beat, slot, key, velocity, length in beats, owner). Report: Captures/<paramref name="prefix"/>_report.txt.</summary>
    public static string DraftShots(string jsonPath, string prefix, int col, string beats, float from, float to)
    {
        if (SequenceMaster.I == null) return "no SequenceMaster";
        SequenceMaster.I.StartCoroutine(DraftRoutine(jsonPath, prefix, col, beats ?? "", from, to));
        return "draft started";
    }

    static IEnumerator DraftRoutine(string jsonPath, string prefix, int col, string beats, float from, float to)
    {
        var log = new StringBuilder();
        string rep = Path.Combine(V2Checks.CapturePath, prefix + "_report.txt");
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(rep, "...running\n"); } catch (Exception) { }
        var saves = V3Fixes.SnapshotSaves();
        Prepare();
        yield return null;
        try
        {
            var st = SongState.FromJson(File.ReadAllText(jsonPath));
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
        }
        catch (Exception e) { log.Append("FAIL load: ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n'); }
        yield return null; yield return null; yield return null;
        log.Append("loaded: islands ").Append(SM.Islands.Count).Append(", columns ").Append(SM.ColumnCount).Append(", moons ").Append(SM.Moons.Count)
           .Append(", cubes ").Append(SequenceMaster.Cubes.Count).Append(", ").Append(GlobalClock.TotalBeats / 4f).Append(" bars\n");
        foreach (var kb in SM.Islands)
            if (kb != null && kb.IsStairs)
                log.Append("stairs col ").Append(kb.column).Append(": ").Append(string.Join(" ", SM.StairPitches(kb).Select(m => m.ToString()).ToArray())).Append('\n');
        var cam = OrbitCamera.I;
        // 1. the overview (stopped)
        if (cam != null) { cam.followPlayhead = false; cam.FrameAll(); }
        yield return Wait(1.6f);
        yield return Shot(prefix + "_overview.png");
        if (cam != null) cam.FrameAll();
        yield return Wait(0.5f);
        // 2. the notes while it plays (and a close-up of the column, then the moments)
        var tapped = new List<VoiceRules.NoteEvent>();
        VoiceRules.Tap = e => { if (e.owner >= 16) tapped.Add(e); };
        var b = SM.ColumnBounds(Mathf.Clamp(col, 0, Mathf.Max(0, SM.ColumnCount - 1)));
        if (cam != null) cam.FrameBounds(b, 0.2f, false, 0.8f, false);
        GlobalClock.Seek(from);
        GlobalClock.Play(); AudioCube.ScheduleAllNow();
        double d0 = GlobalClock.DspTimeOfBeat(0);
        var marks = new List<float>();
        foreach (var s in beats.Split(',')) { float f; if (float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) marks.Add(f); }
        int shot = 0;
        while (GlobalClock.IsPlaying && GlobalClock.SongBeatD < to)
        {
            if (shot < marks.Count && GlobalClock.SongBeatD >= marks[shot]) { yield return Shot(prefix + "_at" + marks[shot].ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ".png"); shot++; continue; }
            yield return null;
        }
        GlobalClock.Stop();
        VoiceRules.Tap = null;
        double bps = GlobalClock.BeatsPerSecond;
        var notes = new StringBuilder();
        foreach (var e in tapped.OrderBy(e => e.onDsp))
            notes.Append(((e.onDsp - d0) * bps).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(e.slot).Append(' ').Append(e.midi).Append(' ')
                 .Append(e.vel).Append(' ').Append(((e.offDsp - e.onDsp) * bps).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(e.owner).Append('\n');
        try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, prefix + "_notes.txt"), notes.ToString()); } catch (Exception) { }
        log.Append("tapped ").Append(tapped.Count).Append(" notes from beat ").Append(from).Append(" to ").Append(to).Append('\n');
        // 3. the follow camera's view (what a player sees) a beat into the column
        yield return null;
        if (cam != null) { cam.followPlayhead = true; cam.FrameColumn(Mathf.Clamp(col, 0, Mathf.Max(0, SM.ColumnCount - 1)), 0.2f); }
        GlobalClock.Seek(Mathf.Max(0f, SM.ColumnStart(col) + 2f)); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(2.4f);
        yield return Shot(prefix + "_follow.png");
        GlobalClock.Stop();
        FocusLoop.Release(); GlobalClock.ClearRegion();
        V3Fixes.RestoreSaves(saves);
        log.Append("console errors ").Append(errors.Count).Append('\n');
        try { File.WriteAllText(rep, log.ToString() + "done\n"); } catch (Exception) { }
    }

    // ================================================================== a frame-time probe: the song's variants in one session
    /// <summary>Frame time playing from <paramref name="beat"/> for 6 s (follow camera) for each JSON in <paramref name="jsons"/> (a '|' list; "crush" =
    /// that gallery song), each with KeyStage on and off. Report: Captures/g9_perf_report.txt.</summary>
    public static string PerfProbe(string jsons, float beat)
    {
        if (SequenceMaster.I == null) return "no SequenceMaster";
        SequenceMaster.I.StartCoroutine(PerfProbeRoutine(jsons, beat));
        return "probe started";
    }

    static string Prefix(string n)
    {
        int i = 0; while (i < n.Length && (char.IsLetter(n[i]) || (i > 0 && n[i] == '_'))) i++;
        return i > 0 ? n.Substring(0, i) : n;
    }

    /// <summary>For the song loaded now: active renderers and UI graphics by GameObject name prefix; the top profiler markers (ms a frame, mean over
    /// 3 s of play from <paramref name="beat"/>); for get proto the KeyStage runs it drives and their gaps.</summary>
    static IEnumerator Breakdown(StringBuilder log, string label, float beat)
    {
        var byR = new Dictionary<string, int>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            string k = Prefix(r.gameObject.name); int c; byR.TryGetValue(k, out c); byR[k] = c + 1;
        }
        var byG = new Dictionary<string, int>();
        foreach (var g in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
        {
            if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) continue;
            string k = Prefix(g.gameObject.name); int c; byG.TryGetValue(k, out c); byG[k] = c + 1;
        }
        int canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(c => c.isActiveAndEnabled);
        log.Append("== ").Append(label).Append(": active renderers by name prefix (top 25 of ").Append(byR.Count).Append("): ")
           .Append(string.Join(", ", byR.OrderByDescending(kv => kv.Value).Take(25).Select(kv => kv.Key + " " + kv.Value).ToArray())).Append('\n');
        log.Append("== ").Append(label).Append(": UI graphics by prefix (").Append(canvases).Append(" canvases; top 15): ")
           .Append(string.Join(", ", byG.OrderByDescending(kv => kv.Value).Take(15).Select(kv => kv.Key + " " + kv.Value).ToArray())).Append('\n');
        // profiler markers over 3 s of play
        var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
        Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
        var recs = new List<Unity.Profiling.ProfilerRecorder>(); var names = new List<string>();
        foreach (var h in handles)
        {
            var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
            if (d.UnitType != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            var r = new Unity.Profiling.ProfilerRecorder(h, 1, Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
            r.Start(); recs.Add(r); names.Add(d.Category.Name + "/" + d.Name);
        }
        GlobalClock.Seek(beat); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(0.5f);
        var sum = new double[recs.Count]; int frames = 0;
        float s0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - s0 < 3f)
        {
            yield return null; frames++;
            for (int i = 0; i < recs.Count; i++) if (recs[i].Valid) sum[i] += recs[i].LastValue;
        }
        GlobalClock.Stop();
        foreach (var r in recs) r.Dispose();
        var order = Enumerable.Range(0, recs.Count).OrderByDescending(i => sum[i]).Take(40).ToList();
        log.Append("== ").Append(label).Append(": profiler markers, ms a frame over ").Append(frames).Append(" frames (top 40): ")
           .Append(string.Join(", ", order.Select(i => names[i] + " " + (sum[i] / Math.Max(1, frames) / 1e6).ToString("F2")).ToArray())).Append('\n');
        if (label.StartsWith("get-proto.json"))
        {
            var seen = new HashSet<KeyBlock>();
            var rows = new List<string>();
            foreach (var kb in SM.Islands)
            {
                if (kb == null || seen.Contains(kb) || !KeyStage.Drives(kb)) continue;
                var run = KeyStage.RunMembers(kb);
                foreach (var m in run) seen.Add(m);
                var parts = KeyStage.PartsOf(kb);
                var gaps = new List<string>();
                for (int i = 1; i < parts.Count; i++) gaps.Add(((parts[i].x - parts[i - 1].y) / 4f).ToString("F1"));
                string voices = string.Join("+", SequenceMaster.Cubes.Where(c => c != null && run.Contains(c.Island) && c.layer == 0).Select(c => Instruments.VoiceName(c.instrument, c.voice)).Distinct().ToArray());
                rows.Add("z" + kb.pz.ToString("F0") + " cols " + run.Min(m => m.column) + "-" + run.Max(m => m.column) + " (" + run.Count + " islands, " + (voices.Length > 0 ? voices : "-") + "): " + parts.Count + " parts, gaps "
                         + (gaps.Count > 0 ? string.Join(" ", gaps.ToArray()) : "-") + " bars");
            }
            log.Append("== KeyStage runs driven (").Append(rows.Count).Append("):\n  ").Append(string.Join("\n  ", rows.ToArray())).Append('\n');
        }
    }

    static IEnumerator PerfProbeRoutine(string jsons, float beat)
    {
        string rep = Path.Combine(V2Checks.CapturePath, "g9_perf_report.txt");
        var log = new StringBuilder();
        try { File.WriteAllText(rep, "...running\n"); } catch (Exception) { }
        var saves = V3Fixes.SnapshotSaves();
        Prepare();
        yield return null;
        foreach (var j in jsons.Split('|'))
        {
            if (j == "crush") Gallery.Open("crush", false);
            else { var st = SongState.FromJson(File.ReadAllText(j)); GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push(); }
            yield return null; yield return null; yield return null;
            int driven = SM.Islands.Count(k => k != null && KeyStage.Drives(k));
            foreach (bool stage in new[] { true, false })
            {
                KeyStage.Enabled = stage;
                float b = j == "crush" ? Gallery.Find("crush").hook * 4f : beat;
                if (OrbitCamera.I != null) { OrbitCamera.I.followPlayhead = true; OrbitCamera.I.FrameColumn(SM.ActiveColumn(b + 0.01f), 0.2f); }
                GlobalClock.Seek(b); GlobalClock.Play(); AudioCube.ScheduleAllNow();
                yield return Wait(1f);
                var frames = new List<float>();
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 6f) { yield return null; frames.Add(Time.unscaledDeltaTime); }
                GlobalClock.Stop();
                frames.Sort();
                int tf = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                int ps = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length;
                int rends = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.enabled && r.gameObject.activeInHierarchy);
                if (stage) { yield return Breakdown(log, Path.GetFileName(j), b); }
                log.Append(Path.GetFileName(j)).Append(" stage ").Append(stage).Append(": ").Append((frames.Count / 6f).ToString("F0")).Append(" fps, mean ")
                   .Append((frames.Average() * 1000f).ToString("F1")).Append(" ms, p95 ").Append((frames[(int)(frames.Count * 0.95f)] * 1000f).ToString("F1"))
                   .Append(" ms; islands ").Append(SM.Islands.Count).Append(", driven ").Append(driven).Append(", transforms ").Append(tf).Append(", particle systems ").Append(ps)
                   .Append(", active renderers ").Append(rends).Append('\n');
                try { File.WriteAllText(rep, log.ToString() + "...running\n"); } catch (Exception) { }
                yield return null;
            }
        }
        KeyStage.Enabled = true;
        V3Fixes.RestoreSaves(saves);
        try { File.WriteAllText(rep, log.ToString() + "done\n"); } catch (Exception) { }
    }

    // ================================================================== sessions: the suites this change touches, in one Play session (G9)
    /// <summary>Runs V7Suites over <paramref name="suites"/> (a comma list of suite-name prefixes; "" = none), then V2Checks.RunAll (report
    /// Captures/proto9_v2_report.txt) and SongsChecks.RunAll(true) (Captures/songs_report.txt), and writes Captures/g9_chain_report.txt last.</summary>
    public static string RunChain(string suites) => RunChain(suites, false);
    /// <summary><see cref="RunChain(string)"/>; <paramref name="integration"/> also runs V2Checks.RunIntegration after the suites (report
    /// Captures/proto9_v2int_report.txt).</summary>
    public static string RunChain(string suites, bool integration)
    {
        if (SequenceMaster.I == null) return "no SequenceMaster";
        try { File.Delete(Path.Combine(V2Checks.CapturePath, "g9_chain_report.txt")); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Chain(suites ?? "", integration));
        return "chain started";
    }

    static IEnumerator Chain(string suites, bool integration)
    {
        var log = new StringBuilder();
        log.Append("G9 chain ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        if (suites.Length > 0)
        {
            log.Append("V7Suites ").Append(V7Suites.RunAll(false, suites)).Append('\n');
            yield return null; yield return null;
            while (!V7Suites.Done) yield return null;
        }
        if (integration)
        {
            string r0 = V2Checks.RunIntegration();
            float w0 = Time.realtimeSinceStartup;
            while (r0 == "started" && !V2Checks.IntegrationDone && Time.realtimeSinceStartup - w0 < 600f) yield return null;
            string ir = r0 == "started" ? V2Checks.IntegrationReport : r0;
            int ip = 0, ifl = 0;
            foreach (var l in ir.Split('\n')) { var t = l.TrimStart(); if (t.StartsWith("PASS")) ip++; else if (t.StartsWith("FAIL")) ifl++; }
            try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, "proto9_v2int_report.txt"), ir + "\nSUMMARY " + ip + " pass, " + ifl + " fail\n"); } catch (Exception) { }
            log.Append("V2Checks.RunIntegration ").Append(ip).Append(" pass, ").Append(ifl).Append(" fail").Append(V2Checks.IntegrationDone ? "" : " (not done)").Append('\n');
            yield return null;
        }
        string v2;
        try { v2 = V2Checks.RunAll(); } catch (Exception e) { v2 = "FAIL V2Checks threw " + e.Message; }
        int p = 0, f = 0;
        foreach (var l in v2.Split('\n')) { var t = l.TrimStart(); if (t.StartsWith("PASS")) p++; else if (t.StartsWith("FAIL")) f++; }
        try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, "proto9_v2_report.txt"), v2 + "\nSUMMARY " + p + " pass, " + f + " fail\n"); } catch (Exception) { }
        log.Append("V2Checks ").Append(p).Append(" pass, ").Append(f).Append(" fail\n");
        yield return null;
        log.Append("SongsChecks ").Append(SongsChecks.RunAll(true)).Append('\n');
        yield return null; yield return null;
        while (!SongsChecks.Done) yield return null;
        log.Append("done ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, "g9_chain_report.txt"), log.ToString()); } catch (Exception) { }
    }

    // ================================================================== the index
    static void CheckIndex()
    {
        var es = Gallery.Entries;
        var e = Gallery.Find(Id);
        Line(es.Count == 7 && e != null && es[es.Count - 1].id == Id, "the gallery index lists 7 songs, get proto the last",
             es.Count + " entries: " + string.Join(", ", es.Select(x => x.id).ToArray()));
        Line(e != null && e.title == "get proto" && e.after.Contains("midi") && Mathf.Abs(e.bpm - 162f) < 0.01f && e.bars == 89 && e.hook == 25 && e.vibe == 3 && !string.IsNullOrEmpty(e.blurb),
             "its entry: title, the \"after\" line crediting the user's own MIDI, 162 bpm, 89 bars, the hook at bar 25 (the first C section), moonlit, a blurb",
             e == null ? "missing" : "\"" + e.title + "\" / \"" + e.after + "\" / " + e.bpm + " bpm / " + e.bars + " bars / hook " + e.hook + " / vibe " + e.vibe + " / \"" + e.blurb + "\"");
        var st = Gallery.LoadState(Id);
        int kb = st != null ? st.measures.Count(m => m.kind == 2) : 0, grids = st != null ? st.measures.Count(m => m.kind == 0) : 0;
        int stairs = st != null ? st.measures.Count(m => m.kind == 3) : 0;
        bool kits = st != null && st.moons.All(m => m.kit != null && m.kit.SequenceEqual(Kit) && m.bars == 8);
        var cards = new[] { "A9", "Gmaj9", "F#m7", "Bm9", "Amaj7", "C#7", "F#7" };
        bool gridChords = st != null && st.measures.Where(m => m.kind == 0).All(m => m.bars == 1 && m.barOffset >= 0 && m.barOffset <= 3 && cards.Contains(m.chordKey));
        int nudged = st != null ? st.cubes.Count(c => c.nudges != null && c.nudges.Any(x => x != 0)) : 0;
        Line(st != null && kb == Keyboards && grids + stairs + kb == st.measures.Length && grids > 0 && stairs >= 20 && gridChords && st.measures.Length <= 195 && st.moons.Length == 11 && kits
             && nudged > 0 && st.sections != null && st.sections.Length == Sections && st.sectionLetters != null && st.sectionLetters.SequenceEqual(Letters) && !st.loop && st.keyTonic == 11 && st.keyMinor,
             "the song file: one-bar chord grids on the loop's cards (A9 | Gmaj9 | F#m7 | Bm9, colour cards Amaj7 C#7 F#7), 20+ stairs, " + Keyboards + " lead keyboards, nudged cubes, at most 195 islands, 11 Moons (8-bar grooves, the electronic kit), 14 sections with their letters, no loop, B minor",
             st == null ? "unreadable" : st.measures.Length + " islands (" + grids + " chord grids, " + stairs + " stairs, " + kb + " keyboards, cards ok " + gridChords + "), " + nudged + " nudged cubes, " + st.moons.Length + " moons (kits " + kits + "), "
             + st.cubes.Length + " cubes, sections " + (st.sections != null ? st.sections.Length : -1) + ", letters " + (st.sectionLetters != null ? string.Join(" ", st.sectionLetters) : "none") + ", loop " + st.loop);
    }

    // ================================================================== open: load times, structure, sections, lanes, Moons, the fx colour
    static float coldMs = -1f, warmMs = -1f;
    static IEnumerator OpenAndCheck(bool check)
    {
        if (SM.HasSong && Gallery.CurrentId == Id && coldMs >= 0f) yield break;
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        bool ok = Gallery.Open(Id, false);
        float first = (float)t0.Elapsed.TotalMilliseconds;
        yield return null; yield return null;
        // warm: another song, then this one again
        Gallery.Open("crush", false);
        yield return null;
        t0 = System.Diagnostics.Stopwatch.StartNew();
        ok &= Gallery.Open(Id, false);
        float second = (float)t0.Elapsed.TotalMilliseconds;
        coldMs = first; warmMs = second;
        yield return null; yield return null; yield return null;
        if (!check) yield break;
        Line(ok && Gallery.CurrentId == Id, "Gallery.Open(get-proto) opens it (first open " + first.ToString("F0") + " ms, again after crush " + second.ToString("F0") + " ms)",
             "islands " + SM.Islands.Count + ", moons " + SM.Moons.Count + ", columns " + SM.ColumnCount);
        int fin = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.nodes.Count > 0);
        var file = Gallery.LoadState(Id);
        int keyboards = SM.Islands.Count(k => k != null && k.IsKeyboard), stairs = SM.Islands.Count(k => k != null && k.IsStairs);
        Line(file != null && SM.Islands.Count == file.measures.Length && SM.Moons.Count == 11 && SM.ColumnCount == Columns && fin == file.cubes.Length && keyboards == Keyboards
             && Mathf.RoundToInt(GlobalClock.TotalBeats / 4f) == 89 && Mathf.Abs(GlobalClock.BPM - 162f) < 0.01f,
             "the world as authored: chord grids, stairs and " + Keyboards + " lead keyboards in " + Columns + " columns, 11 Moons, every cube, 89 bars at 162 bpm",
             SM.Islands.Count + " islands (" + stairs + " stairs, " + keyboards + " keyboards), " + SM.ColumnCount + " columns, " + SM.Moons.Count + " moons, " + fin + " cubes, "
             + (GlobalClock.TotalBeats / 4f) + " bars, " + GlobalClock.BPM + " bpm");
        // sections: their own letters on the plinths, the roles; every section 4 measures (a pass) but the intro bar; the phrases on x2 belts
        yield return null; yield return null;
        int ns = SM.SectionCount;
        var own = Enumerable.Range(0, ns).Select(s => SM.ExplicitLetter(s) ?? "-").ToArray();
        var shown = Enumerable.Range(0, ns).Select(s => SectionPlinth.LetterOf(s)).ToArray();
        var roles = Enumerable.Range(0, ns).Select(s => SM.SectionRole(s)).ToArray();
        var secBars = new int[ns];
        for (int s = 0; s < ns; s++)
        {
            int c0 = SM.SectionFirst(s), c1 = s + 1 < ns ? SM.SectionFirst(s + 1) : SM.ColumnCount;
            for (int c = c0; c < c1; c++) secBars[s] += SM.ColumnBars(c);
        }
        bool fours = secBars[0] == 1 && secBars.Skip(1).All(b => b == 4);
        int belted = Enumerable.Range(0, SM.ColumnCount).Count(c => SM.ColumnPasses(c) == 2);
        int rewound = Enumerable.Range(0, SM.ColumnCount).Count(c => SM.ColumnPasses(c) == 2 && SM.Islands.Where(k => k != null && k.column == c).All(k => k.rewind));
        Line(ns == Sections && fours && own.SequenceEqual(Letters) && shown.SequenceEqual(Letters) && roles.SequenceEqual(Roles) && belted == Repeated && rewound == Rewound,
             "14 sections — the intro bar, then 4 measures each — with the form's letters on the plinths (I A A' B C A D B B C C' C' E O) and their roles; the 8-bar phrases replay x2 (belts; the hook's two REWIND: time unwinds)",
             ns + " sections, bars " + string.Join(" ", secBars.Select(b => b.ToString()).ToArray()) + ", letters " + string.Join(" ", shown) + ", roles "
             + string.Join("", roles.Select(r => r.ToString()).ToArray()) + ", " + belted + " columns replay (" + rewound + " rewind)");
        // lanes: every chord grid at its lane's z and register; stairs in the lead lane or the fall lane; at most 4 lanes a column; the lanes' voices
        int badLane = 0, maxLanes = 0; var laneBad = new List<string>();
        for (int c = 0; c < SM.ColumnCount; c++)
        {
            var zs = new HashSet<int>();
            foreach (var kb in SM.Islands) if (kb != null && kb.column == c) zs.Add(Mathf.RoundToInt(kb.pz * 10f));
            maxLanes = Math.Max(maxLanes, zs.Count);
        }
        foreach (var kb in SM.Islands)
        {
            if (kb == null) continue;
            int lane = -1;
            for (int l = 0; l < LaneZ.Length; l++) if (Mathf.Abs(kb.pz - LaneZ[l]) < 0.5f) lane = l;
            if (lane < 0) { badLane++; laneBad.Add("z " + kb.pz.ToString("F1")); continue; }
            if (kb.kind == 0 && Array.IndexOf(LaneRegs[lane], kb.register) < 0) { badLane++; laneBad.Add(LaneName[lane] + " register " + kb.register); }
            if (kb.IsKeyboard && lane != 0) { badLane++; laneBad.Add("a keyboard in " + LaneName[lane]); }
            foreach (var cb in SequenceMaster.Cubes)
                if (cb != null && cb.Island == kb && Array.IndexOf(LaneVoices[lane], Instruments.VoiceName(cb.instrument, cb.voice)) < 0)
                { badLane++; laneBad.Add(Instruments.VoiceName(cb.instrument, cb.voice) + " in " + LaneName[lane]); }
        }
        Line(badLane == 0 && maxLanes <= 4, "lanes = voices, front to back: the lead (its keyboards and the stairs), the bass (lowered), the harmony, the high lane (raised); every grid at its lane's z and register; at most 4 lanes a column",
             "misplaced " + badLane + (laneBad.Count > 0 ? " (" + string.Join(", ", laneBad.Take(5).ToArray()) + ")" : "") + ", most lanes in a column " + maxLanes);
        // Moons: 11, an 8-bar groove each, the electronic kit's pieces on their tiles
        bool moonsOk = SM.Moons.All(m => m != null && m.bars == 8 && m.kit != null && m.kit.SequenceEqual(Kit));
        int s0 = SM.Moons.Count > 0 ? SM.MoonStartColumn(0) : -1;
        bool kitPieces = SM.Moons.Count > 0 && VoiceRules.DrumPieceOf(SM.Moons[0].GetTile(0, 2)) == 44 && VoiceRules.DrumPieceOf(SM.Moons[0].GetTile(5, 0)) == 49
                         && VoiceRules.DrumPieceOf(SM.Moons[0].GetTile(5, 3)) == 75 && VoiceRules.DrumPieceOf(SM.Moons[0].GetTile(1, 3)) == 40;
        var mc = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.Moon == SM.Moons[0]);
        bool win8 = mc != null && mc.windows.Count == 1 && Mathf.Abs(mc.windows[0].length - 32f) < 1e-3f && Mathf.Abs(mc.windows[0].start - 4f) < 1e-3f;
        Line(moonsOk && kitPieces && s0 == 1 && win8, "11 Moons, one per 8-bar phrase: each plays its phrase once (one 32-beat window) on the electronic kit (pedal hat, crash, claves, e-snare on their tiles)",
             "bars/kit ok " + moonsOk + ", pieces ok " + kitPieces + ", first Moon at column " + s0 + ", its cube's windows " + (mc != null ? mc.windows.Count + " x " + (mc.windows.Count > 0 ? mc.windows[0].length : 0) : "-"));
        // the fx colour: group 10, Atmosphere first, a pastel of its own
        var fx = SequenceMaster.Cubes.Where(c => c != null && c.instrument == 10).ToList();
        int fxSlot = Instruments.SlotOf(10, 0);
        Line(Instruments.Count == 11 && SynthBank.Groups == 11 && fx.Count > 0 && SynthBank.Def(fxSlot).preset == "Atmosphere" && SynthBank.Def(fxSlot).group == 10 && Instruments.GroupWords[10] == "fx"
             && Instruments.Icons[10] == "fx" && Synth.PresetOf(0, 99) == "Atmosphere",
             "the new fx colour (group 10): its cubes play GeneralUser GS's Atmosphere (the MIDI's \"Scifi\", GM FX 4), " + Instruments.VoiceCount(10) + " fx voices, lime, sparkles",
             fx.Count + " fx cubes, slot " + fxSlot + " = \"" + SynthBank.Def(fxSlot).preset + "\" (font: \"" + Synth.PresetOf(0, 99) + "\"), colour " + ColorUtility.ToHtmlStringRGB(Instruments.Colors[10]));
        Devices();
    }

    // ================================================================== round 3: the devices at the MIDI's own moments
    static readonly string[] StairNames = { "chord", "scale", "spark", "slide", "bright", "walk" };
    static List<Want> wantCache;
    static List<Want> WantAll() { if (wantCache == null) { string i; wantCache = Expected(out i); } return wantCache; }
    static int BarOf(double beat) => (int)Math.Floor(beat / 4.0 + 1e-6);
    /// <summary>The song bars island <paramref name="kb"/> plays in (every pass it plays: its column's start + the pass + its own measures).</summary>
    static List<int> BarsOf(KeyBlock kb)
    {
        var r = new List<int>();
        int passes = Mathf.Min(kb.Passes, SM.ColumnPasses(kb.column));
        for (int p = 0; p < passes; p++)
            for (int m = 0; m < Mathf.Max(1, kb.bars); m++)
                r.Add(BarOf(SM.ColumnStart(kb.column) + p * SM.PassLength(kb.column)) + SongManager.OffsetBars(kb) + m);
        return r;
    }
    static int TrackOfCube(AudioCube c)
    {
        string v = Instruments.VoiceName(c.instrument, c.voice);
        for (int k = 0; k < TrackVoice.Length; k++) if (TrackVoice[k] == v && TrackGroup[k] == c.instrument) return k;
        return -1;
    }

    /// <summary>The DESCENDING MEASURES of a track, read from the MIDI file (the rule of tools/gallery/midi_grid.descending_run): the bar's top line
    /// (repeated pitches and octave flickers collapsed) has a run of 4+ notes stepping down by 1..4 semitones that the bar does not climb back from;
    /// in <paramref name="echoBar"/> bars the written-out echoes (a note repeating a louder one of its pitch 1-4 x 2 16ths later) are left out first.</summary>
    static List<int> DescendingBars(List<Want> want, int track, Func<int, bool> echoBar)
    {
        var res = new List<int>();
        var mine = want.Where(w => w.track == track).OrderBy(w => w.t).ThenByDescending(w => w.vel).ToList();
        for (int bar = 0; bar < 89; bar++)
        {
            var ns = mine.Where(w => w.t >= bar * 96 && w.t < bar * 96 + 96).ToList();
            if (ns.Count == 0) continue;
            if (echoBar(bar))
            {
                var seen = new Dictionary<long, List<int>>();
                var kept = new List<Want>();
                foreach (var n in mine.Where(w => w.t >= bar * 96 - 96 && w.t < bar * 96 + 96))
                {
                    bool echo = false;
                    for (int k = 1; k <= 4 && !echo; k++) { List<int> vs; if (seen.TryGetValue(((long)n.pitch << 32) | (uint)(n.t - 12 * k + 100000), out vs) && vs.Any(v => v > n.vel)) echo = true; }
                    long key = ((long)n.pitch << 32) | (uint)(n.t + 100000);
                    List<int> l; if (!seen.TryGetValue(key, out l)) seen[key] = l = new List<int>(); l.Add(n.vel);
                    if (!echo && n.t >= bar * 96) kept.Add(n);
                }
                ns = kept;
            }
            var tops = new List<Want>();
            foreach (var n in ns.OrderBy(w => w.t).ThenByDescending(w => w.pitch)) if (tops.Count == 0 || tops[tops.Count - 1].t != n.t) tops.Add(n);
            var line = new List<Want>();
            foreach (var n in tops)
            {
                if (line.Count > 0 && line[line.Count - 1].pitch % 12 == n.pitch % 12)
                { if (n.pitch > line[line.Count - 1].pitch) { var u = line[line.Count - 1]; u.pitch = n.pitch; line[line.Count - 1] = u; } continue; }
                line.Add(n);
            }
            if (line.Count < 4) continue;
            var best = new List<Want>(); var cur = new List<Want> { line[0] };
            for (int i = 1; i < line.Count; i++)
            {
                int d = line[i - 1].pitch - line[i].pitch;
                if (d >= 1 && d <= 4) cur.Add(line[i]); else cur = new List<Want> { line[i] };
                if (cur.Count >= best.Count) best = new List<Want>(cur);
            }
            if (best.Count < 4) continue;
            int lastT = best[best.Count - 1].t, cap = best[best.Count - 2].pitch;
            if (line.Any(n => n.t > lastT && n.pitch > cap)) continue;
            res.Add(bar);
        }
        return res;
    }

    /// <summary>A section's device signature: the devices its islands and cubes use (what a player chose there).</summary>
    static string[] Signatures()
    {
        int ns = SM.SectionCount;
        var res = new string[ns];
        var colour = new[] { "Amaj7", "C#7", "F#7" };
        for (int s = 0; s < ns; s++)
        {
            int c0 = SM.SectionFirst(s), c1 = s + 1 < ns ? SM.SectionFirst(s + 1) : SM.ColumnCount;
            var t = new SortedSet<string>();
            foreach (var kb in SM.Islands)
            {
                if (kb == null || kb.column < c0 || kb.column >= c1) continue;
                int passes = SM.ColumnPasses(kb.column);
                if (passes > 1) t.Add(kb.rewind ? "rewind" : "belt");
                if (kb.vary > 0 && passes > 1) t.Add("vary" + kb.vary);
                if (kb.IsKeyboard) t.Add("keys");
                if (kb.IsStairs) t.Add("stairs:" + StairNames[Mathf.Clamp(kb.stairType, 0, 5)]);
                if (kb.carry > 0) t.Add("long grid");
                if (kb.launch) t.Add(kb.energy == 3 ? "big launch" : "launch");
                int lane = -1; for (int l = 0; l < LaneZ.Length; l++) if (Mathf.Abs(kb.pz - LaneZ[l]) < 0.5f) lane = l;
                if (kb.kind == 0 && kb.register > 0 && lane >= 0 && lane != 3) t.Add("raised " + LaneName[lane]);
                if (kb.kind == 0 && KeyStage.IsFragment(kb) && KeyStage.Drives(kb)) t.Add("fragment");
                if (kb.kind == 0 && colour.Contains(kb.assignedChord)) t.Add("colour card");
                foreach (var c in SequenceMaster.Cubes)
                {
                    if (c == null || c.Island != kb || !c.isFinalized) continue;
                    if (c.echo > 0) t.Add("echo");
                    if (c.layer != 0) t.Add("octave copy");
                    if (c.HasNudges) t.Add("nudge");
                    if (c.climb > 0) t.Add("climb");
                    if (c.sphere) t.Add("sphere");
                    if (c.mods.Contains(8)) t.Add("lift");
                    if (c.follow == 3) t.Add("triads");
                    if (kb.carry > 0 && c.HasNudges && c.nodes.All(n => n == c.nodes[0])) t.Add("pedal");
                }
            }
            res[s] = string.Join(", ", t.ToArray());
        }
        return res;
    }

    /// <summary>Round 3's devices, at the moments the MIDI file has them (read here from the file): a stairs on every descending measure of every
    /// voice; a launch on every crash (the strings' swells the big ones); a signature of its own per section; the lead keyboards popping in and out;
    /// rewind on the hook, the written-out echo, the fragments, the pedal; the user's transitions each present.</summary>
    static void Devices()
    {
        var want = WantAll();
        var cubes = SequenceMaster.Cubes.Where(c => c != null && c.isFinalized && !c.IsOnMoon).ToList();
        // 1. stairs on every descending measure (per voice: the MIDI's descending measures against the bars a stairs of that voice plays)
        var echoCols = new HashSet<long>();
        foreach (var c in cubes) if (c.echo > 0 && c.Island != null) echoCols.Add(((long)TrackOfCube(c) << 32) | (uint)c.Island.column);
        var stairBars = new Dictionary<int, HashSet<int>>();
        int runners = 0, spark = 0;
        foreach (var kb in SM.Islands)
        {
            if (kb == null || !kb.IsStairs) continue;
            if (kb.stairType == 2) spark++;
            foreach (var c in cubes)
            {
                if (c.Island != kb) continue;
                runners++;
                int tr = TrackOfCube(c);
                HashSet<int> set; if (!stairBars.TryGetValue(tr, out set)) stairBars[tr] = set = new HashSet<int>();
                foreach (int b in BarsOf(kb)) set.Add(b);
            }
        }
        var rows = new List<string>(); int found = 0, covered = 0; var missing = new List<string>();
        for (int k = 0; k < TrackName.Length; k++)
        {
            if (TrackGroup[k] == 9) continue;
            int kk = k;
            var desc = DescendingBars(want, k, bar => echoCols.Contains(((long)kk << 32) | (uint)SM.ActiveColumn(bar * 4f + 0.01f)));
            HashSet<int> have; stairBars.TryGetValue(k, out have);
            int cov = desc.Count(b => have != null && have.Contains(b));
            found += desc.Count; covered += cov;
            foreach (int b in desc) if (have == null || !have.Contains(b)) missing.Add(b + " " + TrackName[k]);
            if (desc.Count > 0 || (have != null && have.Count > 0))
                rows.Add(TrackName[k] + " " + cov + "/" + desc.Count + " (stairs bars " + (have != null ? have.Count : 0) + ")");
        }
        var excused = StairExceptions.Select(e => e.Substring(0, e.IndexOf(':'))).ToList();
        bool stairsOk = missing.All(m => excused.Contains(m)) && covered + missing.Count == found && runners > 0 && spark > 0;
        Line(stairsOk, "a STAIRS on every descending measure of any voice (read from the MIDI: a run of 3+ steps down the bar does not climb back from): per voice the measures found and the ones a stairs plays",
             string.Join("; ", rows.ToArray()) + "; " + covered + " of " + found + " (" + runners + " runners, " + spark + " spark stairs); missing " + (missing.Count == 0 ? "none" : string.Join(", ", missing.ToArray()))
             + " (the builder's exceptions: " + string.Join("; ", StairExceptions) + ")");
        // 2. launches on the MIDI's crash bars; the strings' swells into them the big ones (energy 3)
        int dr = Array.IndexOf(TrackName, "drums"), strg = Array.IndexOf(TrackName, "strings");
        var crash = want.Where(w => w.track == dr && (w.pitch == 49 || w.pitch == 57) && w.t % 96 == 0).Select(w => w.t / 96).Distinct().OrderBy(b => b).ToList();
        var swell = want.Where(w => w.track == strg).Select(w => w.t / 96 + 1).Distinct().OrderBy(b => b).ToList();
        var launch = SM.Islands.Where(k => k != null && k.launch).ToList();
        var lbars = launch.Select(k => Mathf.RoundToInt(SM.TurnEnd(k) / 4f)).OrderBy(b => b).ToList();
        var big = launch.Where(k => k.energy == 3).Select(k => Mathf.RoundToInt(SM.TurnEnd(k) / 4f)).OrderBy(b => b).ToList();
        bool targets = launch.All(k => SM.LaunchTarget(k) != null);
        Line(crash.Count == 14 && lbars.SequenceEqual(crash) && big.SequenceEqual(swell) && targets,
             "a LAUNCH (riser, crash) on every crash of the MIDI (" + crash.Count + " bars, read from the drum track), the strings' swells into bars " + string.Join(" ", swell.Select(b => b.ToString()).ToArray()) + " the big ones (energy 3)",
             "crashes " + string.Join(" ", crash.Select(b => b.ToString()).ToArray()) + "; launches land on " + string.Join(" ", lbars.Select(b => b.ToString()).ToArray()) + "; big " + string.Join(" ", big.Select(b => b.ToString()).ToArray())
             + "; every launch has a target " + targets);
        // 3. a signature per section; consecutive sections differ
        var sig = Signatures();
        int same = 0; for (int s = 1; s < sig.Length; s++) if (sig[s] == sig[s - 1]) same++;
        for (int s = 0; s < sig.Length; s++) Info("section " + s + " " + (s < Letters.Length ? Letters[s] : "?") + ": " + sig[s]);
        Line(same == 0 && sig.Length == Sections, "every section has a device signature of its own: no two consecutive sections use the same devices (the list per section above)", same + " consecutive pairs alike of " + (sig.Length - 1));
        // 4. the lead keyboards (the sax in B): in the lead lane, playing the sax; the stage pops each in for its bar and sinks it after
        var keys = SM.Islands.Where(k => k != null && k.IsKeyboard).ToList();
        int keysOk = 0; var keyInfo = new List<string>();
        foreach (var kb in keys)
        {
            bool lead = Mathf.Abs(kb.pz - LaneZ[0]) < 0.5f, sax = cubes.Any(c => c.Island == kb && TrackOfCube(c) == 0);
            var parts = KeyStage.PartsOf(kb);
            float upIn = parts.Count > 0 ? KeyStage.DepthAt(kb, (parts[0].x + parts[0].y) * 0.5) : 1f;
            float maxOut = 0f;
            double c0 = SM.ColumnStart(kb.column), c1 = c0 + SM.ColumnLength(kb.column);
            for (double b = c0; b < c1; b += 0.5) maxOut = Mathf.Max(maxOut, KeyStage.DepthAt(kb, b));
            bool ok = lead && sax && KeyStage.Drives(kb) && parts.Count > 0 && upIn < 0.1f && maxOut > 0.9f;
            if (ok) keysOk++;
            keyInfo.Add("bar " + (BarOf(c0) + SongManager.OffsetBars(kb)) + (ok ? "" : " (lead " + lead + ", sax " + sax + ", drives " + KeyStage.Drives(kb) + ", parts " + parts.Count + ", up " + upIn.ToString("F2") + ", out " + maxOut.ToString("F2") + ")"));
        }
        Line(keys.Count == Keyboards && keysOk == keys.Count, "the sax's lead KEYBOARDS in B (its line there mostly not on the cards): in the lead lane, popping up from the sea for their bars and sinking after",
             keysOk + "/" + keys.Count + " keyboards: " + string.Join(", ", keyInfo.ToArray()));
        // 5. rewind (the hook), the written-out echo (D), the fragments (the ocarina), the pedal (the sax in B), the long grids
        var rwCols = Enumerable.Range(0, SM.ColumnCount).Where(c => SM.ColumnPasses(c) == 2 && SM.Islands.Where(k => k != null && k.column == c).All(k => k.rewind)).ToList();
        bool unwinds = rwCols.Count == Rewound && rwCols.All(c =>
        {
            var kb = SM.Islands.First(k => k != null && k.column == c);
            int p; double end1 = SM.ColumnStart(c) + SM.PassLength(c);
            return SM.RewindPhase(kb, end1 - 0.2, out p) >= 0f && SM.RewindPhase(kb, SM.ColumnStart(c) + 1.0, out p) < 0f;
        });
        var rwBars = rwCols.Select(c => BarOf(SM.ColumnStart(c))).ToList();
        var echoes = cubes.Where(c => c.echo == 2 && TrackOfCube(c) == 0).ToList();
        bool echoInD = echoes.Count > 0 && echoes.All(c => c.Island != null && rwBars.Contains(41) && SM.ActiveColumn(41 * 4f + 0.01f) == c.Island.column);
        var frags = SM.Islands.Where(k => k != null && k.kind == 0 && cubes.Any(c => c.Island == k && Instruments.VoiceName(c.instrument, c.voice) == "ocarina")).ToList();
        bool fragOk = frags.Count == 2 && frags.All(k => KeyStage.IsFragment(k) && KeyStage.Drives(k) && KeyStage.DepthAt(k, SM.ColumnStart(k.column) + SongManager.OffsetBars(k) * 4 + 2) < 0.1f
                                                        && KeyStage.DepthAt(k, SM.ColumnStart(k.column) + 2) > 0.9f);
        var longs = SM.Islands.Where(k => k != null && k.carry == 3).ToList();
        var pedal = cubes.Where(c => c.Island != null && c.Island.carry == 3 && TrackOfCube(c) == 0 && c.HasNudges && c.nodes.All(n => n == c.nodes[0])).ToList();
        Line(unwinds && echoInD && fragOk && longs.Count == 3 && pedal.Count == 1,
             "REWIND on the hook (A' and D: time unwinds where the call comes back), the sax's written-out echo in D on echo 2, the ocarina's 1-bar fragments as their own grids (popping up for their bar), the sax's PEDAL (one cube rocking on one tile, nudged, over a long grid) and the piano's long grids in C'",
             "rewind columns at bars " + string.Join(" ", rwBars.Select(b => b.ToString()).ToArray()) + " (unwinds " + unwinds + "), " + echoes.Count + " sax cubes on echo 2 in D, " + frags.Count + " fragments (ok " + fragOk + "), "
             + longs.Count + " long grids, " + pedal.Count + " pedal");
        // 6. the user's transitions (the first message's video list), each present: where
        var sparkBars = SM.Islands.Where(k => k != null && k.IsStairs && k.stairType == 2).SelectMany(k => BarsOf(k)).Distinct().OrderBy(b => b).ToList();
        var liftBars = cubes.Where(c => c.mods.Contains(8) && c.Island != null).SelectMany(c => BarsOf(c.Island)).Distinct().OrderBy(b => b).ToList();
        var copyBars = cubes.Where(c => c.layer != 0 && c.Island != null && TrackOfCube(c) == Array.IndexOf(TrackName, "mbox")).SelectMany(c => BarsOf(c.Island)).Distinct().OrderBy(b => b).ToList();
        var pedalBars = pedal.Select(c => BarOf(SM.ColumnStart(c.Island.column))).ToList();
        int eCol = SM.ActiveColumn(73 * 4f + 0.01f);
        bool turn = SM.Islands.Any(k => k != null && k.kind == 0 && k.assignedChord == "C#7") && SM.Islands.Any(k => k != null && k.kind == 0 && k.assignedChord == "F#7")
                    && lbars.Contains(73) && SM.Islands.Any(k => k != null && k.column == eCol && k.kind == 0 && k.register == 1 && Mathf.Abs(k.pz - LaneZ[2]) < 0.5f);
        var varyBars = SM.Islands.Where(k => k != null && k.vary > 0 && SM.ColumnPasses(k.column) > 1).Select(k => BarOf(SM.ColumnStart(k.column))).Distinct().OrderBy(b => b).ToList();
        Line(sparkBars.Count > 0 && liftBars.Count > 0 && copyBars.Count > 0 && pedalBars.Count > 0 && turn && varyBars.Count > 0 && rwBars.Count > 0,
             "the user's transitions, each present: the diminished (spark) fall, the octave-displaced melody (lift flicker, octave copies), a pedal ostinato, the turnaround (C#7 -> F#7) launching into a higher chorus (E's grids raised), the same loop with small differences (vary, rewind)",
             "spark stairs on bars " + string.Join(" ", sparkBars.Select(b => b.ToString()).ToArray()) + "; lift flicker on " + string.Join(" ", liftBars.Select(b => b.ToString()).ToArray())
             + "; the music box's octave copies on " + copyBars.Count + " bars; pedal from bar " + string.Join(" ", pedalBars.Select(b => b.ToString()).ToArray()) + "; turnaround into a raised E " + turn
             + "; vary from bars " + string.Join(" ", varyBars.Select(b => b.ToString()).ToArray()) + "; rewind from bars " + string.Join(" ", rwBars.Select(b => b.ToString()).ToArray()));
    }

    // ================================================================== the MIDI file itself (an SMF reader, independent of the importer)
    struct MNote { public int track, t, d, pitch, vel; }

    static List<MNote> ReadMidi(string path, out int division)
    {
        var data = File.ReadAllBytes(path);
        division = (data[12] << 8) | data[13];
        int ntrk = (data[10] << 8) | data[11];
        int i = 14, tr = 0;
        var outNotes = new List<MNote>();
        while (i + 8 <= data.Length && tr < ntrk)
        {
            int len = (data[i + 4] << 24) | (data[i + 5] << 16) | (data[i + 6] << 8) | data[i + 7];
            bool mtrk = data[i] == 'M' && data[i + 1] == 'T' && data[i + 2] == 'r' && data[i + 3] == 'k';
            int j = i + 8, end = i + 8 + len;
            i = end;
            if (!mtrk) continue;
            int tick = 0, status = 0;
            var open = new Dictionary<int, int[]>();
            while (j < end)
            {
                int delta = 0; byte b;
                do { b = data[j++]; delta = (delta << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                tick += delta;
                if ((data[j] & 0x80) != 0) status = data[j++];
                if (status == 0xFF)
                {
                    int kind = data[j++]; int ln = 0;
                    do { b = data[j++]; ln = (ln << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                    j += ln; status = 0;
                    if (kind == 0x2F) break;
                    continue;
                }
                if (status == 0xF0 || status == 0xF7) { int ln = 0; do { b = data[j++]; ln = (ln << 7) | (b & 0x7F); } while ((b & 0x80) != 0); j += ln; status = 0; continue; }
                int hi = status & 0xF0, ch = status & 0x0F;
                if (hi == 0xC0 || hi == 0xD0) { j++; continue; }
                int a = data[j], c = data[j + 1]; j += 2;
                int key = ch * 128 + a;
                if (hi == 0x90 && c > 0)
                {
                    int[] o;
                    if (open.TryGetValue(key, out o)) outNotes.Add(new MNote { track = tr, t = o[0], d = tick - o[0], pitch = a, vel = o[1] });
                    open[key] = new[] { tick, c };
                }
                else if (hi == 0x80 || (hi == 0x90 && c == 0))
                {
                    int[] o;
                    if (open.TryGetValue(key, out o)) { outNotes.Add(new MNote { track = tr, t = o[0], d = Math.Max(1, tick - o[0]), pitch = a, vel = o[1] }); open.Remove(key); }
                }
            }
            foreach (var kv in open) outNotes.Add(new MNote { track = tr, t = kv.Value[0], d = Math.Max(1, tick - kv.Value[0]), pitch = kv.Key % 128, vel = kv.Value[1] });
            tr++;
        }
        return outNotes;
    }

    /// <summary>The MIDI's notes as DeaCube should play them: shifted an 8th earlier, in ticks (24 a beat), with the slot of their track's voice.</summary>
    struct Want { public int track, slot, t, end, pitch, vel; }
    static List<Want> Expected(out string info)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../sample.mid"));
        int div;
        var notes = ReadMidi(path, out div);
        double per = div / 24.0;
        var slots = new int[TrackName.Length];
        for (int k = 0; k < TrackName.Length; k++) slots[k] = SlotOfVoice(TrackGroup[k], TrackVoice[k]);
        var w = new List<Want>();
        foreach (var n in notes)
        {
            if (n.track < 0 || n.track >= slots.Length) continue;
            int t = (int)Math.Round((n.t - Shift) / per), e = (int)Math.Round((n.t + n.d - Shift) / per);
            w.Add(new Want { track = n.track, slot = slots[n.track], t = t, end = e, pitch = n.pitch, vel = n.vel });
        }
        info = notes.Count + " notes in " + Path.GetFileName(path) + " (" + div + " ticks a quarter)";
        return w;
    }

    static int SlotOfVoice(int group, string caption)
    {
        for (int v = 0; v < Instruments.VoiceCount(group); v++) if (Instruments.VoiceName(group, v) == caption) return Instruments.SlotOf(group, v);
        return -1;
    }

    // ================================================================== what the cubes schedule: every step of every window through VoiceRules.Resolve
    struct Ev { public int slot, midi, vel; public double on, off; public int cube; public bool nudged, stairs, varied; }

    static List<Ev> Scheduled()
    {
        var list = new List<Ev>();
        var events = new VoiceRules.NoteEvent[32];
        double bps = GlobalClock.BeatsPerSecond;
        int ci = 0;
        foreach (var cube in SequenceMaster.Cubes)
        {
            ci++;
            if (cube == null || !cube.isFinalized || cube.nodes.Count == 0) continue;
            for (int w = 0; w < cube.windows.Count; w++)
            {
                var win = cube.windows[w];
                if (win.silent) continue;
                int pass = GlobalClock.LoopIndex * 64 + win.order;
                for (int k = 0; k < 4096; k++)
                {
                    float sl = cube.StepStartLocalBeat(k);
                    if (sl >= win.length - 1e-4f) break;
                    var hit = cube.Decide(w, k, pass);
                    if (!hit.fires) continue;
                    var tile = cube.TileAt(w, hit.node);
                    if (tile == null) continue;
                    double on = (win.start + sl) / bps;
                    double stepSec = cube.StepLenBeats(k) / bps;
                    double wEnd = on + (win.length - sl) / bps;
                    double next = double.PositiveInfinity;
                    for (int j = k + 1; j < k + 256; j++)
                    {
                        float sj = cube.StepStartLocalBeat(j);
                        if (sj >= win.length - 1e-4f) break;
                        if (cube.Decide(w, j, pass).hit) { next = on + (sj - sl) / bps; break; }
                    }
                    int n = VoiceRules.Resolve(cube, win, tile, hit, 0, pass, on, stepSec, wEnd, next, events);
                    bool nud = cube.NudgeAt(hit.node) != 0, st = win.island != null && win.island.IsStairs;
                    bool vari = (win.pass >= 1 && win.island != null && win.island.vary >= 1) || cube.climb > 0 || win.carried;
                    for (int e = 0; e < n; e++) list.Add(new Ev { slot = events[e].slot, midi = events[e].midi, vel = events[e].vel, on = events[e].onDsp * bps * 24.0, off = events[e].offDsp * bps * 24.0, cube = ci, nudged = nud, stairs = st, varied = vari });
                }
            }
        }
        return list;
    }

    /// <summary>The MIDI against played events (ticks): per voice the notes with the exact pitch and onset on the right voice (an event's onset may be
    /// up to 8 ms late: Resolve's humanised lateness), the note's end within a tick (gate long ends 10 ms early; drums are one-shots), |Δvelocity|.</summary>
    static string Compare(List<Want> want, List<Ev> evs, string label, out double pct, out int extra, bool record)
    {
        var pool = new Dictionary<long, List<int>>();
        for (int i = 0; i < evs.Count; i++)
        {
            int t = (int)Math.Round(evs[i].on - 0.26);
            long key = ((long)evs[i].slot << 40) | ((long)evs[i].midi << 32) | (uint)(t + 1000000);
            List<int> l; if (!pool.TryGetValue(key, out l)) pool[key] = l = new List<int>(); l.Add(i);
        }
        var used = new bool[evs.Count];
        var rows = new StringBuilder();
        int all = 0, allHit = 0, allEnd = 0; double allDv = 0;
        for (int tr = 0; tr < TrackName.Length; tr++)
        {
            int n = 0, hit = 0, endOk = 0; double dv = 0; double worstEnd = 0;
            foreach (var w in want)
            {
                if (w.track != tr) continue;
                n++;
                long key = ((long)w.slot << 40) | ((long)w.pitch << 32) | (uint)(w.t + 1000000);
                List<int> l;
                if (!pool.TryGetValue(key, out l)) continue;
                int pick = -1;
                foreach (int i in l) if (!used[i]) { pick = i; break; }
                if (pick < 0) continue;
                used[pick] = true; hit++;
                double endErr = evs[pick].off - w.end;
                bool drum = TrackGroup[tr] == 9;
                if (drum || Math.Abs(endErr) <= 1.0) endOk++;
                if (!drum) worstEnd = Math.Max(worstEnd, Math.Abs(endErr));
                dv += Math.Abs(evs[pick].vel - w.vel);
            }
            all += n; allHit += hit; allEnd += endOk; allDv += dv;
            rows.Append("  ").Append(TrackName[tr].PadRight(8)).Append(" notes ").Append(n.ToString().PadLeft(4)).Append("  exact ").Append(hit.ToString().PadLeft(4))
                .Append(" (").Append((100.0 * hit / Math.Max(1, n)).ToString("F1")).Append("%)  end within a tick ").Append(endOk.ToString().PadLeft(4))
                .Append(TrackGroup[tr] == 9 ? " (one-shots)" : " (worst " + worstEnd.ToString("F1") + ")").Append("  |dv| ").Append((dv / Math.Max(1, hit)).ToString("F1")).Append('\n');
        }
        extra = used.Count(u => !u);
        pct = 100.0 * allHit / Math.Max(1, all);
        string head = label + ": " + allHit + "/" + all + " MIDI notes exact (" + pct.ToString("F2") + "%), ends within a tick " + allEnd + ", mean |dv| " + (allDv / Math.Max(1, allHit)).ToString("F1")
                      + ", events the MIDI has not " + extra;
        if (record)
        {
            var ex = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < evs.Count && shown < 12; i++) if (!used[i]) { ex.Append(" [slot ").Append(evs[i].slot).Append(" midi ").Append(evs[i].midi).Append(" @").Append(evs[i].on.ToString("F1")).Append(']'); shown++; }
            if (shown > 0) head += " — e.g." + ex;
        }
        return head + "\n" + rows;
    }

    static List<Ev> lastScheduled;
    /// <summary>Round 2's numbers against the MIDI, from every cube's every step through VoiceRules.Resolve: the harmony (the MIDI's note-time
    /// on the bar's card, per voice), the groove (the bass's onsets and pitches, the drums' onsets and pieces: exact), the sax's contour (its top
    /// line's steps up / down / the same), and per voice the MIDI's notes the song plays at their onset (and with their pitch).</summary>
    static void Faithfulness()
    {
        string info;
        var want = Expected(out info);
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        var evs = Scheduled();
        lastScheduled = evs;
        Info(info + "; " + evs.Count + " events resolved in " + t0.ElapsedMilliseconds + " ms");
        // the chord of every bar (the chord grids' windows)
        var chordOf = new Dictionary<int, HashSet<int>>();
        foreach (var kb in SM.Islands)
        {
            if (kb == null || kb.kind != 0) continue;
            int passes = Mathf.Min(kb.Passes, SM.ColumnPasses(kb.column));
            for (int p = 0; p < passes; p++)
            {
                int bar = Mathf.RoundToInt((SM.ColumnStart(kb.column) + p * SM.PassLength(kb.column)) / 4f) + kb.barOffset;
                if (chordOf.ContainsKey(bar)) continue;
                var set = new HashSet<int>();
                foreach (int s in kb.semitoneList) set.Add(((kb.chordRootMIDI + s) % 12 + 12) % 12);
                chordOf[bar] = set;
            }
        }
        var cov = new List<string>();
        double bassCov = 0;
        for (int k = 0; k < TrackName.Length; k++)
        {
            if (TrackName[k] == "drums") continue;
            double hit = 0, tot = 0;
            foreach (var w in want)
            {
                if (w.track != k) continue;
                HashSet<int> ch;
                if (!chordOf.TryGetValue(w.t / 96, out ch)) continue;
                tot += w.end - w.t;
                if (ch.Contains(w.pitch % 12)) hit += w.end - w.t;
            }
            if (tot > 0) { cov.Add(TrackName[k] + " " + (100.0 * hit / tot).ToString("F0") + "%"); if (TrackName[k] == "bass") bassCov = 100.0 * hit / tot; }
        }
        Info("harmony coverage (the MIDI's note-time on the bar's card): " + string.Join(", ", cov.ToArray()));
        // per voice: the MIDI's notes with a note of that voice at their onset (and with their pitch)
        var at = new Dictionary<long, List<Ev>>();
        foreach (var e in evs) { long key = ((long)e.slot << 32) | (long)Math.Round(e.on - 0.26); List<Ev> l; if (!at.TryGetValue(key, out l)) at[key] = l = new List<Ev>(); l.Add(e); }
        var rows = new List<string>();
        double bassOn = 0, bassPitch = 0, drumsExact = 0;
        for (int k = 0; k < TrackName.Length; k++)
        {
            int n = 0, on = 0, ex = 0;
            foreach (var w in want)
            {
                if (w.track != k) continue;
                n++;
                List<Ev> l;
                bool found = false, exact = false;
                for (int d = -1; d <= 1 && !exact; d++)
                    if (at.TryGetValue(((long)w.slot << 32) | (long)(w.t + d), out l)) { found = true; if (l.Any(e => e.midi == w.pitch)) exact = true; }
                if (found) on++;
                if (exact) ex++;
            }
            if (n == 0) continue;
            rows.Add(string.Format("  {0,-8} notes {1,5}  at their onset {2,5:F1}%  with their pitch {3,5:F1}%", TrackName[k], n, 100.0 * on / n, 100.0 * ex / n));
            if (TrackName[k] == "bass") { bassOn = 100.0 * on / n; bassPitch = 100.0 * ex / n; }
            if (TrackName[k] == "drums") drumsExact = 100.0 * ex / n;
        }
        foreach (var r in rows) Info(r);
        // the sax's contour: its top line's steps (up / down / the same) against the song's at the same onsets
        int sax = Array.IndexOf(TrackName, "sax");
        var top = new SortedDictionary<int, int>();
        foreach (var w in want) if (w.track == sax) { int pv; if (!top.TryGetValue(w.t, out pv) || w.pitch > pv) top[w.t] = w.pitch; }
        int saxSlot = SlotOfVoice(TrackGroup[sax], TrackVoice[sax]);
        var ptop = new Dictionary<int, int>();
        foreach (var e in evs) if (e.slot == saxSlot) { int t = (int)Math.Round(e.on - 0.26); for (int d = -1; d <= 1; d++) { int pv; if (top.ContainsKey(t + d) && (!ptop.TryGetValue(t + d, out pv) || e.midi > pv)) ptop[t + d] = e.midi; } }
        var ts = top.Keys.Where(x => ptop.ContainsKey(x)).ToList();
        int agree = 0;
        for (int i = 1; i < ts.Count; i++) if (Math.Sign(top[ts[i]] - top[ts[i - 1]]) == Math.Sign(ptop[ts[i]] - ptop[ts[i - 1]])) agree++;
        double contour = ts.Count > 1 ? 100.0 * agree / (ts.Count - 1) : 0;
        Info("the sax's contour: " + agree + " of " + Math.Max(0, ts.Count - 1) + " steps keep their direction (" + contour.ToString("F0") + "%)");
        // the NUDGES (v9 N): the steps a card has no tile for, raised / lowered a scale step or two — scheduled, and on the MIDI's pitch
        int nudgedEv = 0, nudgedOn = 0; var nudgedBy = new Dictionary<string, int>();
        var wantAt = new HashSet<long>();
        foreach (var w in want) for (int d = -1; d <= 1; d++) wantAt.Add(((long)w.slot << 40) | ((long)w.pitch << 32) | (uint)(w.t + d + 1000000));
        int nudgedVaried = 0;
        foreach (var e in evs)
        {
            if (!e.nudged) continue;
            if (e.varied) { nudgedVaried++; continue; }   // a varied pass, a climbing cube, a long grid's carried window: the game re-voices those
            nudgedEv++;
            if (wantAt.Contains(((long)e.slot << 40) | ((long)e.midi << 32) | (uint)((int)Math.Round(e.on - 0.26) + 1000000))) nudgedOn++;
        }
        int nudgedSteps = 0;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || !c.HasNudges || c.layer != 0) continue;
            int k = TrackOfCube(c); string nm = k >= 0 ? TrackName[k] : "?";
            int n = c.nudges.Count(x => x != 0); nudgedSteps += n;
            int have; nudgedBy.TryGetValue(nm, out have); nudgedBy[nm] = have + n;
        }
        Line(nudgedSteps > 0 && nudgedEv > 0 && nudgedOn >= 0.9 * nudgedEv,
             "NUDGES: the steps a card has no tile for play a scale step or two above / below their tile — the scheduled nudged notes sound the MIDI's pitch at its onset",
             nudgedSteps + " nudged steps (" + string.Join(", ", nudgedBy.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value).ToArray()) + "); " + nudgedOn + " of " + nudgedEv + " nudged notes scheduled on the MIDI's pitch ("
             + (100.0 * nudgedOn / Math.Max(1, nudgedEv)).ToString("F1") + "%; " + nudgedVaried + " more on varied passes, climbing cubes and carried windows, re-voiced by the game)");
        Line(bassCov >= 95 && bassOn >= 95 && bassPitch >= 90 && drumsExact >= 99.9 && contour >= 70,
             "the song as the MIDI: the bass on its cards (>= 95 % of its note-time), the groove exact (bass onsets >= 95 %, bass pitches >= 90 %, every drum hit), the sax's contour kept (>= 70 % of its steps)",
             "bass coverage " + bassCov.ToString("F0") + "%, bass onsets " + bassOn.ToString("F1") + "%, bass pitches " + bassPitch.ToString("F1") + "%, drums " + drumsExact.ToString("F1") + "%, sax contour " + contour.ToString("F0") + "%");
    }

    // ================================================================== the song played from bar 0 to its end: what the synth is sent
    /// <summary>The song from bar 0 to its end, no loop and no focus region: the synth is sent what the cubes schedule (VoiceRules.Tap against
    /// <see cref="Scheduled"/>: slot, key, onset within a tick), with no late event and no error, and it ends.</summary>
    static IEnumerator Live()
    {
        if (lastScheduled == null) lastScheduled = Scheduled();
        var tapped = new List<VoiceRules.NoteEvent>();
        VoiceRules.Tap = e => { if (e.owner >= 16) tapped.Add(e); };
        FocusLoop.Release(); GlobalClock.ClearRegion(); GlobalClock.Stop();   // the whole song: no focus loop / region (it would wrap the clock inside it)
        yield return null;
        bool noRegion = !GlobalClock.HasRegion && !FocusLoop.Active && !GlobalClock.LoopSong;
        if (OrbitCamera.I != null) { OrbitCamera.I.followPlayhead = true; OrbitCamera.I.FrameColumn(0, 0.2f); }
        GlobalClock.Seek(0);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        GlobalClock.Play();
        AudioCube.ScheduleAllNow();
        double d0 = GlobalClock.DspTimeOfBeat(0);
        float t0 = Time.realtimeSinceStartup;
        var frames = new List<float>();
        double total = GlobalClock.TotalBeats;
        while (GlobalClock.IsPlaying && Time.realtimeSinceStartup - t0 < (float)(total / GlobalClock.BeatsPerSecond) + 6f)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime);
        }
        float secs = Time.realtimeSinceStartup - t0;
        VoiceRules.Tap = null;
        bool ended = !GlobalClock.IsPlaying;
        GlobalClock.Stop();
        double bps = GlobalClock.BeatsPerSecond;
        var got = new Dictionary<string, int>();
        foreach (var e in tapped) { string k = e.slot + ":" + e.midi + "@" + (int)Math.Round((e.onDsp - d0) * bps * 24.0); int c; got.TryGetValue(k, out c); got[k] = c + 1; }
        int match = 0;
        foreach (var e in lastScheduled)
        {
            int tk = (int)Math.Round(e.on - 0.26);
            for (int d = -1; d <= 1; d++)
            {
                string k = e.slot + ":" + e.midi + "@" + (tk + d); int c;
                if (got.TryGetValue(k, out c) && c > 0) { got[k] = c - 1; match++; break; }
            }
        }
        double pct = lastScheduled.Count > 0 ? 100.0 * match / lastScheduled.Count : 0;
        frames.Sort();
        float mean = frames.Count > 0 ? frames.Average() : 0f, p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95f)] : 0f, worst = frames.Count > 0 ? frames[frames.Count - 1] : 0f;
        liveFrames = "get proto while playing (whole song, follow camera): " + frames.Count + " frames in " + secs.ToString("F1") + " s, " + (frames.Count / Mathf.Max(0.01f, secs)).ToString("F0")
                     + " fps, mean " + (mean * 1000f).ToString("F1") + " ms, p95 " + (p95 * 1000f).ToString("F1") + " ms, worst " + (worst * 1000f).ToString("F0") + " ms";
        Info(liveFrames);
        int late = Synth.LateEvents - late0, errs = Synth.Errors - err0;
        Line(noRegion && ended && pct >= 98.0 && late == 0 && errs == 0, "played from bar 0 to its end (no loop, no focus region): the synth is sent what the cubes schedule (>= 98 % of the scheduled notes, within a tick), nothing late",
             pct.ToString("F2") + "% of " + lastScheduled.Count + " scheduled notes sent (" + tapped.Count + " sent), synth late +" + late + ", errors +" + errs + ", ended " + ended + " after " + secs.ToString("F1") + " s, no region " + noRegion);
    }

    static string liveFrames = "";

    // ================================================================== performance: load and frame time against crush
    static IEnumerator Perf()
    {
        // get proto from its hook for 10 s, then crush from its hook for 10 s (the same session, the same camera rules)
        var res = new List<string>();
        foreach (var id in new[] { Id, "crush" })
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            bool opened = Gallery.Open(id, false);
            float openMs = (float)t0.Elapsed.TotalMilliseconds;
            yield return null; yield return null;
            Info(id + ": Gallery.Open returned " + opened + ", current " + Gallery.CurrentId + ", " + SM.Islands.Count + " islands right after");
            var e = Gallery.Find(id);
            float hook = e != null ? e.hook * 4f : 0f;
            if (OrbitCamera.I != null) { OrbitCamera.I.followPlayhead = true; OrbitCamera.I.FrameColumn(SM.ActiveColumn(hook + 0.01f), 0.2f); }
            GlobalClock.Seek(hook);
            GlobalClock.Play();
            AudioCube.ScheduleAllNow();
            yield return Wait(1f);
            var frames = new List<float>();
            float s0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - s0 < 10f) { yield return null; frames.Add(Time.unscaledDeltaTime); }
            GlobalClock.Stop();
            frames.Sort();
            float mean = frames.Average(), p95 = frames[(int)(frames.Count * 0.95f)], worst = frames[frames.Count - 1];
            int transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            res.Add(id + ": open " + openMs.ToString("F0") + " ms; playing 10 s from the hook: " + (frames.Count / 10f).ToString("F0") + " fps, mean " + (mean * 1000f).ToString("F1")
                    + " ms, p95 " + (p95 * 1000f).ToString("F1") + " ms, worst " + (worst * 1000f).ToString("F0") + " ms; " + SM.Islands.Count + " islands, " + SM.Moons.Count + " moons, "
                    + SequenceMaster.Cubes.Count + " cubes, " + transforms + " transforms in the scene");
            yield return null;
        }
        foreach (var r in res) Info(r);
        Info("load: get proto first open " + coldMs.ToString("F0") + " ms, again after crush " + warmMs.ToString("F0") + " ms");
        Line(warmMs >= 0f && warmMs < 1500f, "get proto opens (warm) in well under a second and a half", warmMs.ToString("F0") + " ms (first " + coldMs.ToString("F0") + " ms)");
        Gallery.Open(Id, false);
        yield return null;
    }

    // ================================================================== captures
    /// <summary>Captures (proto9_*.png): the whole song from above (and in thirds with the haze lifted), one per section from the follow camera, the
    /// moments (the ocarina's fragment, A''s stairs, the rewind, B's keyboards, the launch into C — riser and crash —, D's climax, the pedal, C''s
    /// stairs, the turnaround into E, E raised), the HUD and the inspector's instrument popover.</summary>
    static IEnumerator Look(bool captures)
    {
        if (!captures) yield break;
        var cam = OrbitCamera.I;
        GlobalClock.Stop();
        yield return null;
        if (cam != null) { cam.followPlayhead = false; cam.FrameAll(); }
        yield return Wait(1.6f);
        yield return Shot("proto9_overview.png");
        if (cam != null) cam.FrameAll();   // leave the overview
        yield return Wait(0.6f);
        float fs0 = RenderSettings.fogStartDistance, fe0 = RenderSettings.fogEndDistance;
        int third = 0;
        foreach (var span in new[] { new[] { 0, 5 }, new[] { 6, 12 }, new[] { 13, Columns - 1 } })
        {
            RenderSettings.fogStartDistance = 600f; RenderSettings.fogEndDistance = 1500f;
            var b = SM.ColumnBounds(span[0]);
            for (int c = span[0] + 1; c <= span[1]; c++) b.Encapsulate(SM.ColumnBounds(c));
            foreach (var m in SM.Moons) if (m != null && m.column >= span[0] && m.column <= span[1]) b.Encapsulate(m.WorldBounds);
            if (cam != null) cam.FrameBounds(b, 0.3f, false, 1f, false);
            yield return Wait(1.4f);
            RenderSettings.fogStartDistance = 600f; RenderSettings.fogEndDistance = 1500f;
            yield return Shot("proto9_song_" + (++third) + ".png");
        }
        RenderSettings.fogStartDistance = fs0; RenderSettings.fogEndDistance = fe0;
        // one capture per section from the follow camera (what a player sees), a bar and a half into it
        for (int sct = 0; sct < SM.SectionCount; sct++)
        {
            int c0 = SM.SectionFirst(sct);
            double st0 = SM.ColumnStart(c0);
            if (cam != null) { cam.followPlayhead = true; cam.FrameColumn(c0, 0.2f); }
            GlobalClock.Seek(st0 + 0.5);
            GlobalClock.Play(); AudioCube.ScheduleAllNow();
            float w1 = Time.realtimeSinceStartup;
            double at = st0 + (sct == 0 ? 2.5 : 6.0);
            while (GlobalClock.IsPlaying && GlobalClock.SongBeatD < at && Time.realtimeSinceStartup - w1 < 12f) yield return null;
            yield return Shot("proto9_sec" + sct.ToString("00") + "_" + Letters[Mathf.Min(sct, Letters.Length - 1)].Replace("'", "p") + ".png");
            GlobalClock.Stop();
            yield return null;
        }
        // the moments, framed on their columns while they play: (first column, last column, from beat, capture beat, file)
        var moments = new[] {
            new { c0 = 1, c1 = 1, from = 13.0, at = 18.6, file = "proto9_fragment.png" },
            new { c0 = 2, c1 = 2, from = 36.0, at = 38.6, file = "proto9_Aprime_stairs.png" },
            new { c0 = 2, c1 = 2, from = 48.0, at = 51.7, file = "proto9_rewind.png" },
            new { c0 = 3, c1 = 3, from = 66.0, at = 70.0, file = "proto9_B_keys.png" },
            new { c0 = 3, c1 = 3, from = 96.0, at = 99.6, file = "proto9_launch_riser.png" },
            new { c0 = 4, c1 = 4, from = 98.5, at = 100.3, file = "proto9_launch_crash.png" },
            new { c0 = 6, c1 = 6, from = 162.0, at = 166.4, file = "proto9_D_climax.png" },
            new { c0 = 7, c1 = 10, from = 195.0, at = 201.0, file = "proto9_B_pedal.png" },
            new { c0 = 13, c1 = 16, from = 259.0, at = 263.4, file = "proto9_Cprime.png" },
            new { c0 = 17, c1 = 20, from = 286.0, at = 291.6, file = "proto9_turnaround.png" },
            new { c0 = 21, c1 = 21, from = 291.0, at = 294.0, file = "proto9_E_raised.png" } };
        foreach (var m in moments)
        {
            int c = Mathf.Clamp(m.c0, 0, SM.ColumnCount - 1);
            if (cam != null)
            {
                cam.followPlayhead = false;
                var b = SM.ColumnBounds(c); for (int k = c + 1; k <= m.c1 && k < SM.ColumnCount; k++) b.Encapsulate(SM.ColumnBounds(k));
                cam.FrameBounds(b, 0.2f, false, 0.85f, false);
            }
            GlobalClock.Seek(m.from);
            GlobalClock.Play(); AudioCube.ScheduleAllNow();
            float w0 = Time.realtimeSinceStartup;
            while (GlobalClock.IsPlaying && GlobalClock.SongBeatD < m.at && Time.realtimeSinceStartup - w0 < 12f) yield return null;
            yield return Shot(m.file);
            GlobalClock.Stop();
            yield return null;
        }
        // the HUD (11 instrument cubes, the column rail with the 13 sections) and the inspector's instrument popover (11 chips)
        if (cam != null) { cam.followPlayhead = false; cam.FrameAll(); }
        yield return Wait(1.0f);
        yield return Shot("proto9_hud.png");
        if (cam != null) cam.FrameAll();
        yield return Wait(0.4f);
        var fxCube = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.instrument == 10 && c.isFinalized);
        if (fxCube != null)
        {
            if (cam != null) cam.FrameColumn(fxCube.Island != null ? fxCube.Island.column : 21, 0.2f);
            CubeInspector.Open(fxCube);
            float w0 = Time.realtimeSinceStartup;
            while (!(CubeInspector.IsOpen && InspectorCard.Shown >= 0.99f) && Time.realtimeSinceStartup - w0 < 4f) yield return null;
            var mi = typeof(InspectorCard).GetMethod("ToggleInstrumentPopover", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null && InspectorCard.I != null) mi.Invoke(InspectorCard.I, null);
            yield return Wait(0.6f);
            Line(InspectorCard.PopoverOpen && InspectorCard.InstrumentButton(10) != null && InspectorCard.InstrumentButton(10).gameObject.activeInHierarchy,
                 "the inspector's instrument popover shows the 11th (fx) chip", "popover open " + InspectorCard.PopoverOpen);
            yield return Shot("proto9_popover.png");
            CubeInspector.CloseImmediate();
            FocusLoop.Release(); GlobalClock.ClearRegion(); GlobalClock.Stop();   // inspecting a cube loops its column (FocusLoop): the parts after play the whole song
            yield return null;
        }
    }

    // ================================================================== the menu preview
    static void Preview()
    {
        // the six v5 songs preview exactly as before (MenuPreview vs the pre-v9 copy, V9PreviewRef), over their hook bars
        int same = 0; var diffs = new List<string>();
        foreach (var e in Gallery.Entries)
        {
            if (e.id == Id) continue;
            var st = Gallery.LoadState(e.id);
            var a = MenuPreview.Build(st, e.hook);
            var b = V9PreviewRef.Build(st, e.hook);
            bool eq = a != null && b != null && a.notes.Count == b.notes.Count && a.startBar == b.startBar && a.bars == b.bars;
            if (eq) for (int i = 0; i < a.notes.Count; i++)
                {
                    var x = a.notes[i]; var y = b.notes[i];
                    if (x.slot != y.slot || x.midi != y.midi || x.vel != y.vel || Math.Abs(x.on - y.on) > 1e-9 || Math.Abs(x.off - y.off) > 1e-9) { eq = false; break; }
                }
            if (eq) same++; else diffs.Add(e.id + " (" + (a != null ? a.notes.Count : -1) + " vs " + (b != null ? b.notes.Count : -1) + " notes)");
        }
        Line(same == 6, "the six v5 gallery songs preview exactly as before (every note: slot, key, velocity, on, off, over their hook bars)", same + "/6 identical" + (diffs.Count > 0 ? "; differ: " + string.Join(", ", diffs.ToArray()) : ""));
        // get proto's hook: the preview's notes against what the engine schedules in those bars
        var ge = Gallery.Find(Id);
        var gst = Gallery.LoadState(Id);
        var tr = MenuPreview.Build(gst, ge != null ? ge.hook : 0);
        if (tr == null || lastScheduled == null) { Line(false, "get proto's preview against the engine", tr == null ? "no preview" : "run the faith part first"); return; }
        double s0 = tr.startBar * 4.0 * 24.0, s1 = s0 + tr.lengthBeats * 24.0;
        int stairsLeft = lastScheduled.Count(x => x.stairs && x.on - 0.26 >= s0 - 0.5 && x.on - 0.26 < s1 - 0.5);
        var eng = lastScheduled.Where(x => !x.stairs && x.on - 0.26 >= s0 - 0.5 && x.on - 0.26 < s1 - 0.5).Select(x => x.slot + ":" + x.midi + "@" + (int)Math.Round(x.on - 0.26)).ToList();
        var pv = tr.notes.Select(n => n.slot + ":" + n.midi + "@" + (int)Math.Round(n.on * 24.0 + s0)).ToList();
        var engSet = new Dictionary<string, int>(); foreach (var k in eng) { int c; engSet.TryGetValue(k, out c); engSet[k] = c + 1; }
        int match = 0; var extraPv = new List<string>(); foreach (var k in pv) { int c; if (engSet.TryGetValue(k, out c) && c > 0) { engSet[k] = c - 1; match++; } else if (extraPv.Count < 8) extraPv.Add(SynthBank.Def(int.Parse(k.Split(':')[0])).preset + " " + k); }
        if (extraPv.Count > 0)
        {
            var parts0 = extraPv[0].Split(' '); var key0 = parts0[parts0.Length - 1]; int slot0 = int.Parse(key0.Split(':')[0]); int at0 = int.Parse(key0.Split('@')[1]);
            Info("preview-only notes; the engine's notes of that slot around: " + string.Join(" ", lastScheduled.Where(x => x.slot == slot0 && Math.Abs(x.on - 0.26 - at0) < 30).Select(x => x.midi + "@" + (int)Math.Round(x.on - 0.26) + (x.stairs ? "s" : "")).ToArray())
                 + "; the preview's: " + string.Join(" ", tr.notes.Where(n => n.slot == slot0 && Math.Abs(n.on * 24.0 + s0 - at0) < 30).Select(n => n.midi + "@" + (int)Math.Round(n.on * 24.0 + s0)).ToArray()));
        }
        Line(tr.notes.Count > 0 && match == pv.Count && match == eng.Count, "get proto's card preview plays its hook as the engine does (bars " + tr.startBar + ".." + (tr.startBar + tr.bars - 1) + ": every note's voice, key and onset; the preview plays no stairs runs)",
             match + " of " + pv.Count + " preview notes match the engine's " + eng.Count + " (" + tr.Seconds.ToString("F1") + " s; " + stairsLeft + " stairs notes left out)" + (extraPv.Count > 0 ? "; preview only, e.g. " + string.Join(" ", extraPv.ToArray()) : ""));
    }

    // ================================================================== the shelf: 7 cards, get proto's card
    static IEnumerator Shelf(bool captures)
    {
        GlobalClock.Stop();
        MainMenu.Show();
        float t0 = Time.realtimeSinceStartup;
        while (MainMenu.State != MainMenu.Phase.Shown && Time.realtimeSinceStartup - t0 < 12f) yield return null;
        yield return Wait(0.4f);
        var gw = MainMenu.GalleryButton;
        if (gw != null) { MainMenu.MoveFocus(-9); yield return null; while (MainMenu.FocusedButton != gw && Time.realtimeSinceStartup - t0 < 16f) { MainMenu.MoveFocus(1); yield return null; } }
        KeyShim.Sim(KeyCode.Return, true, true, false); yield return null; KeyShim.Clear(); yield return null;
        var shelf = MainMenu.Shelf;
        t0 = Time.realtimeSinceStartup;
        while ((shelf == null || shelf.Shown < 1f) && Time.realtimeSinceStartup - t0 < 4f) { shelf = MainMenu.Shelf; yield return null; }
        yield return Wait(0.8f);
        int n = shelf != null ? shelf.Cards.Count : 0;
        Line(MainMenu.ShelfOpen && n == 7, "the gallery shelf shows 7 cards", n + " cards");
        if (shelf != null && n == 7)
        {
            // focus get proto's card (MenuGallery.FocusCard: the arrows' focus; it is the last card)
            int gi = -1;
            for (int k = 0; k < shelf.Cards.Count; k++) if (shelf.Cards[k] != null && shelf.Cards[k].Entry != null && shelf.Cards[k].Entry.id == Id) gi = k;
            shelf.FocusCard(gi);
            yield return Wait(1.4f);
            Line(shelf.FocusedCard != null && shelf.FocusedCard.Entry != null && shelf.FocusedCard.Entry.id == Id && shelf.PreviewId == Id,
                 "get proto's card can be focused and its hook previews", "focused " + (shelf.FocusedCard != null && shelf.FocusedCard.Entry != null ? shelf.FocusedCard.Entry.id : "-") + ", preview " + shelf.PreviewId);
            if (captures) yield return Shot("proto9_card.png");
        }
        MainMenu.Hide(); WorldInput.Unlock("menu");
        yield return Wait(0.5f);
    }

    // ================================================================== where the open's time goes (the warm open, part by part)
    static IEnumerator LoadProbe()
    {
        var BF = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var sw = new System.Diagnostics.Stopwatch();
        Func<int> transforms = () => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Gallery.Open("crush", false); yield return null;
        sw.Restart(); Gallery.Open(Id, false); sw.Stop(); double open = sw.Elapsed.TotalMilliseconds;
        yield return null;
        var st = Gallery.LoadState(Id);
        sw.Restart(); SM.RebuildFromState(Gallery.LoadState(Id)); sw.Stop(); double rebuild = sw.Elapsed.TotalMilliseconds;
        yield return null;
        var mBuild = typeof(SongManager).GetMethod("BuildIslands", BF);
        sw.Restart(); mBuild.Invoke(SM, null); sw.Stop(); double islands = sw.Elapsed.TotalMilliseconds;
        SM.RebuildFromState(Gallery.LoadState(Id));
        yield return null;
        // the cubes alone: restore every cube (its body, its path beads), timing the beads by size
        PathManager.I.ClearAllPaths(false);
        yield return null;
        double cubesMs = 0; double[] byNodes = new double[4]; int[] cnt = new int[4];
        foreach (var cs in st.cubes)
        {
            sw.Restart(); PathManager.I.RestoreCube(cs); sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds; cubesMs += ms;
            int n = cs.xs != null ? cs.xs.Length : 0, k = n < 16 ? 0 : (n < 32 ? 1 : (n < 56 ? 2 : 3));
            byNodes[k] += ms; cnt[k]++;
        }
        sw.Restart(); SM.RecomputeMeasureStarts(); sw.Stop(); double recompute = sw.Elapsed.TotalMilliseconds;
        int nodes = SequenceMaster.Cubes.Where(c => c != null).Sum(c => c.nodes.Count), maxNodes = SequenceMaster.Cubes.Where(c => c != null).Max(c => c.nodes.Count);
        int tf = transforms();
        SM.RebuildFromState(Gallery.LoadState(Id));
        History.Reset(); History.Push();
        sw.Restart(); var snap = SongState.Capture().ToJson(); sw.Stop(); double capture = sw.Elapsed.TotalMilliseconds;
        Info("load breakdown (warm): Gallery.Open " + open.ToString("F0") + " ms = RebuildFromState " + rebuild.ToString("F0") + " ms (of it BuildIslands " + islands.ToString("F0")
             + " ms; the " + st.cubes.Length + " cubes' RestoreCube " + cubesMs.ToString("F0") + " ms; RecomputeMeasureStarts " + recompute.ToString("F0") + " ms) + History / capture (one Capture "
             + capture.ToString("F0") + " ms, " + (snap.Length / 1024) + " KB)");
        Info("cube restore by path length: <16 nodes " + cnt[0] + " cubes " + byNodes[0].ToString("F0") + " ms, 16-31 " + cnt[1] + " / " + byNodes[1].ToString("F0") + " ms, 32-55 " + cnt[2] + " / "
             + byNodes[2].ToString("F0") + " ms, 56+ " + cnt[3] + " / " + byNodes[3].ToString("F0") + " ms; " + nodes + " path nodes (the busiest path " + maxNodes + "), " + tf + " transforms");
    }

    // ================================================================== the sound: the mix and every voice alone (WAVs for tools/gallery/wavstats.py)
    // (voice, from bar, seconds): each voice soloed where it plays (every other cube muted), and the whole band there
    static readonly string[] SoloVoice = { "bass", "piano", "sax", "flute", "mbox", "drums", "epiano", "square", "strings", "scifi", "tri" };
    static readonly int[] SoloBar = { 9, 25, 9, 25, 17, 25, 1, 9, 0, 73, 4 };
    static readonly float[] SoloSecs = { 6f, 6f, 6f, 6f, 6f, 6f, 6f, 6f, 1.6f, 6f, 1.6f };

    static IEnumerator Sound()
    {
        string dir = Path.Combine(V2Checks.CapturePath, "proto9_wav");
        try { Directory.CreateDirectory(dir); } catch (Exception) { }
        var slotsOf = new Dictionary<string, int>();
        for (int k = 0; k < TrackName.Length; k++) slotsOf[TrackName[k]] = SlotOfVoice(TrackGroup[k], TrackVoice[k]);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        var done = new List<string>();
        for (int i = -1; i < SoloVoice.Length; i++)
        {
            // i = -1: the band in the hook (bar 25, C: bass, piano, sax, flute, music box, drums); then each voice alone
            string name = i < 0 ? "mix_C" : SoloVoice[i];
            int bar = i < 0 ? 25 : SoloBar[i];
            float secs = i < 0 ? 8f : SoloSecs[i];
            int slot = i < 0 ? -1 : slotsOf[SoloVoice[i]];
            foreach (var c in SequenceMaster.Cubes) if (c != null) c.muted = slot >= 0 && Instruments.SlotOf(c.instrument, c.voice) != slot;
            GlobalClock.Stop();
            yield return null;
            GlobalClock.Seek(bar * 4.0);
            Synth.StartRecording(secs);
            GlobalClock.Play(); AudioCube.ScheduleAllNow();
            float t0 = Time.realtimeSinceStartup;
            while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < secs + 3f) yield return null;
            bool ok = Synth.RecordingDone && Synth.SaveRecording(Path.Combine(dir, name + ".wav"));
            GlobalClock.Stop();
            if (ok) done.Add(name);
            yield return null;
        }
        foreach (var c in SequenceMaster.Cubes) if (c != null) c.muted = false;
        Line(done.Count == SoloVoice.Length + 1 && Synth.LateEvents == late0 && Synth.Errors == err0,
             "recorded the band in C and every voice alone (Captures/proto9_wav/*.wav: levels by tools/gallery/wavstats.py)",
             done.Count + " WAVs, synth late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0));
    }
}
