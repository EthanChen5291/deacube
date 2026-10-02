using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v7 §4.6 package A checks (Play mode): octave layers and harmony bends heard correctly in every pitch path, stairs and phrase tiles, the
/// harmonic-safety exemptions, the launch riser + landing crash, the performance numbers. <see cref="RunAll"/> starts them (a coroutine); poll
/// <see cref="Done"/> or Captures/a7_report.txt. Static: every pitched voice × every fixture tile × layers −2..+2 sounds exactly 12 × layer from the
/// same cube in layer 0 after the folds (costumes included; the MIDI 21..108 edge counted apart), the cases the v6 fold would have undone, bends
/// (Fold(tile + bend), only on the node's home tile, no stack lift, no costumes), NodeMidi = the preview / audition / press of a cube's node = what
/// Resolve plays (layer + bend), the v6 builders unchanged for layer 0 / no bend, layer cubes off the stacks (SequenceMaster), HarmonicCheck's
/// exemptions (bent notes, stairs, phrases; the low-end law before the layer), stairs / phrase tiles playing their pitch (a run keeps its shape).
/// Timed (Captures/a7_*.wav): a layer copy and a harmony cube in the playing song, the riser alone (its 10 ms RMS peak within ±30 ms of each
/// landing, a swell, silence before it), the crash alone (its onset on the landing), the full mix (evidence), once per launch per pass (a focus loop
/// played three times), only when audible (asleep, a muted group, launch off: none), never late (a seek into the swell: a shorter one or none), the
/// performance table (a dense song + 4 octave layers + 2 harmony cubes; the fixture + the same), Synth late / errors 0. SPEC v7 §21 ("just keep cubes
/// on each grid"): the long grid's carried notes (any saved carry style) are fit-stamped and fit their measure's chord; a launch is only its sound — no
/// cube is flung or leaves its platform while the risers and crashes play. The fixture and generated
/// songs only (never the user's save); the autosave and the backups are put back as they were.
/// </summary>
public static class V7ChecksA
{
    public static bool Done = true;
    public static string Report = "";
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "a7_report.txt");

    static StringBuilder sb;
    static int pass, fail;

    // ================================================================== helpers
    static string F(double v, string f = "F2") => v.ToString(f, System.Globalization.CultureInfo.InvariantCulture);
    static double Db(double v) => 20.0 * Math.Log10(Math.Max(1e-9, v));
    static void Line(bool ok, string name, string detail = null)
    {
        if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(name).Append(string.IsNullOrEmpty(detail) ? "" : ": " + detail).Append('\n');
        Report = sb.ToString();
    }
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); Report = sb.ToString(); }
    static void Guard(string name, Action a)
    {
        try { a(); }
        catch (Exception e) { Line(false, name + " threw", e.GetType().Name + ": " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var st = e.StackTrace ?? ""; int i = st.IndexOf('\n'); return i > 0 ? st.Substring(0, i).Trim() : st.Trim(); }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);
    static SongManager SM => SongManager.I;

    static void LoadState(SongState st)
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.ClearRegion();
        GlobalClock.Stop();
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }
    static SongState Fixture() => SongState.FromJson(File.ReadAllText(V2Checks.FixturePath));
    static void LoadFixture() => LoadState(Fixture());

    static void Restore()
    {
        try
        {
            CubeInspector.CloseImmediate();
            if (Presenter.Active) Presenter.Exit();
            FocusLoop.Dismiss();
            GlobalClock.ClearRegion();
            GlobalClock.Stop();
            if (PathManager.I != null) PathManager.I.PutDown();
            PathManager.AutoHand = false;
            PathManager.SimOnly = false;
            LaunchRiser.Enabled = true; LaunchRiser.SoundRiser = true;
            VoiceRules.Tap = null;
        }
        catch (Exception e) { Debug.LogWarning("V7ChecksA restore: " + e.Message); }
    }

    // ---- WAV reading (our own writer: 44-byte header, 16-bit stereo)
    static float[] ReadWav(string path, out int sr)
    {
        var b = File.ReadAllBytes(path);
        sr = BitConverter.ToInt32(b, 24);
        int dataLen = BitConverter.ToInt32(b, 40);
        int n = Math.Min(dataLen, b.Length - 44) / 2;
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = BitConverter.ToInt16(b, 44 + 2 * i) / 32768f;
        return f;
    }
    /// <summary>Per millisecond: the RMS (both channels) of the 10 ms window centred on it (the riser's peak measure, as SynthEngine renders it).</summary>
    static double[] Env10(float[] x, int sr)
    {
        int ms = Math.Max(1, sr / 1000), frames = x.Length / 2, nms = frames / ms;
        var e = new double[nms];
        for (int k = 0; k < nms; k++) { double s = 0; for (int i = k * ms; i < (k + 1) * ms; i++) s += x[2 * i] * (double)x[2 * i] + x[2 * i + 1] * (double)x[2 * i + 1]; e[k] = s; }
        var r = new double[nms];
        double acc = 0;
        for (int k = 0; k < nms; k++)
        {
            acc += e[k]; if (k >= 10) acc -= e[k - 10];
            int c = k - 5;   // window [k-9, k] centred at k - 4.5 ms
            if (c >= 0) r[c] = Math.Sqrt(Math.Max(0.0, acc) / (2.0 * 10 * ms));
        }
        return r;
    }
    /// <summary>The time (s) of the envelope's maximum inside [t0, t1] and its level.</summary>
    static double PeakIn(double[] env, double t0, double t1, out double level)
    {
        int a = Mathf.Clamp((int)(t0 * 1000.0), 0, env.Length - 1), b = Mathf.Clamp((int)(t1 * 1000.0), 0, env.Length - 1);
        int best = a; level = 0.0;
        for (int k = a; k <= b; k++) if (env[k] > level) { level = env[k]; best = k; }
        return best / 1000.0;
    }
    static double LevelAt(double[] env, double t) { int k = Mathf.Clamp((int)(t * 1000.0), 0, env.Length - 1); return env.Length > 0 ? env[k] : 0.0; }
    static double MaxIn(double[] env, double t0, double t1) { double l; PeakIn(env, t0, t1, out l); return l; }

    /// <summary>A pitched, non-drum cube on a chord island (the fixture's).</summary>
    static AudioCube PitchedCube(bool nonSustaining)
    {
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.nodes.Count == 0 || c.IsOnMoon || c.Island == null || c.Island.IsMoon || Instruments.IsDrums(c.instrument)) continue;
            if (VoiceRules.IsMelody(c.Island)) continue;
            if (nonSustaining && VoiceRules.IsSustaining(Instruments.SlotOf(c.instrument, c.voice))) continue;
            if (c.Island.energy == 0) continue;
            return c;
        }
        return null;
    }

    /// <summary>The DSP time the static checks resolve their notes at (far ahead: nothing is dispatched) — one time for every call, so two resolutions
    /// compare exactly (the audio clock ticks between calls).</summary>
    static double staticOn = -1.0;
    static double StaticOn { get { if (staticOn < Synth.DspNow + 10.0) staticOn = Synth.DspNow + 30.0; return staticOn; } }

    static int Resolve(AudioCube c, KeyBlock island, TileInteraction tile, int node, VoiceRules.NoteEvent[] ev, int stack = 0, int echo = 0, int ratchet = 1)
    {
        double on = StaticOn;   // far ahead; nothing is dispatched
        var hit = new AudioCube.Hit { fires = true, node = node, weight = 1, ratchet = ratchet, echo = echo };
        return VoiceRules.Resolve(c, new AudioCube.Window { start = 0f, length = 8f, island = island, order = 0 }, tile, hit, stack, 0, on, 0.25, on + 2.0, double.PositiveInfinity, ev);
    }

    /// <summary>A cube's state kept aside while a check changes it.</summary>
    struct Saved { public int instrument, voice, layer, octave, follow, echo; public bool shimmer; public List<int> bend; }
    static Saved Save(AudioCube c) => new Saved { instrument = c.instrument, voice = c.voice, layer = c.layer, octave = c.octave, follow = c.follow, echo = c.echo, shimmer = c.shimmer, bend = new List<int>(c.bend) };
    static void Put(AudioCube c, Saved s) { c.instrument = s.instrument; c.voice = s.voice; c.layer = s.layer; c.octave = s.octave; c.follow = s.follow; c.echo = s.echo; c.shimmer = s.shimmer; c.bend.Clear(); c.bend.AddRange(s.bend); }

    // ---- main-thread stalls: another package's file import / an MCP call can freeze the editor's main thread for longer than the 0.22 s
    // lookahead; the notes due meanwhile are late by design (the protocol counts those apart, like a ScreenCapture's stall), and so is the step
    // under the playhead after a seek while playing (≤ 30 ms by design). Anything else late is a real failure.
    static int stallLate, seekLate, realLate, stalls, lateSeen;
    static float longestStall, lastLong = -99f, lastSeek = -99f;
    static bool watching;
    static int watchGen;
    static readonly StringBuilder realLateLog = new StringBuilder();
    static IEnumerator StallWatch()
    {
        int gen = ++watchGen;
        watching = true; lateSeen = Synth.LateEvents;
        while (watching && gen == watchGen)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime, now = Time.realtimeSinceStartup;
            if (dt > 0.12f) { stalls++; longestStall = Mathf.Max(longestStall, dt); lastLong = now; }
            int late = Synth.LateEvents;
            if (late > lateSeen)
            {
                int n = late - lateSeen; lateSeen = late;
                // a stall's signature: notes late within a second of a long frame, or a burst (several cubes late on one step: the scheduler, not the audio path)
                if (now - lastLong < 1.0f || n >= 3) stallLate += n;
                else if (now - lastSeek < 0.5f) seekLate += n;
                else { realLate += n; if (realLateLog.Length < 600) realLateLog.Append(Synth.LastLate).Append(" (frame ").Append((1000f * dt).ToString("F0")).Append(" ms); "); }
            }
        }
    }
    /// <summary>A seek while playing (its catch-up note may be ≤ 30 ms late by design).</summary>
    static void SeekPlaying(double beat) { lastSeek = Time.realtimeSinceStartup; GlobalClock.Seek(beat); }

    // ================================================================== entry point
    /// <summary>Starts every A check (static + timed); poll <see cref="Done"/> / Captures/a7_report.txt.</summary>
    public static string RunAll(bool captures = false)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(null));
        return "started";
    }

    /// <summary>Runs only the named parts (comma list: static, stairs, song, riser, rules, perf) — for iterating on one part.</summary>
    public static string RunParts(string parts)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine(parts ?? ""));
        return "started";
    }

    static bool Want(string parts, string p) => parts == null || ("," + parts.Replace(" ", "") + ",").Contains("," + p + ",");

    static IEnumerator Routine(string parts)
    {
        sb = new StringBuilder(); pass = 0; fail = 0;
        sb.Append("V7ChecksA ").Append(DateTime.Now.ToString("HH:mm:ss")).Append(parts != null ? " (parts " + parts + ")" : "").Append('\n');
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors, lateLog0 = Synth.LateLogCount, revives0 = Synth.FilterRevives, restarts0 = Synth.HostRestarts;
        bool wasAutoHand = PathManager.AutoHand;
        PathManager.AutoHand = false;
        V6ChecksA.Prepare(); LoadFixture();
        stallLate = 0; seekLate = 0; realLate = 0; stalls = 0; longestStall = 0f; realLateLog.Length = 0;
        SequenceMaster.I.StartCoroutine(StallWatch());
        yield return Frames(3);

        if (Want(parts, "static"))
        {
            Progress = "static";
            Guard("layers", Layers);
            Guard("layer cases", LayerCases);
            Guard("bends", Bends);
            Guard("previews", Previews);
            Guard("stacks", Stacks);
            Guard("exemptions", Exemptions);
            Guard("riser static", RiserStatic);
        }
        if (Want(parts, "stairs")) { Progress = "stairs"; yield return StairsAndPhrase(); }
        if (Want(parts, "song")) { Progress = "song"; yield return LayerAndHarmonyInSong(); }
        if (Want(parts, "flow")) { Progress = "long grid"; yield return FlowAndVaryInSong(); }
        if (Want(parts, "riser")) { Progress = "riser"; yield return RiserWavs(); }
        if (Want(parts, "rules")) { Progress = "rules"; yield return RiserRules(); }
        if (Want(parts, "perf")) { Progress = "perf"; yield return PerformanceTable(); }

        yield return Frames(3);
        watching = false;
        int dl = Synth.LateEvents - late0, de = Synth.Errors - err0;
        int dr = Synth.FilterRevives - revives0, dh = Synth.HostRestarts - restarts0;
        Line(realLate == 0 && de == 0 && dr == 0 && dh == 0, "Synth late 0 / errors 0 over every A check (notes made late by a main-thread stall longer than the lookahead, or by a seek while playing, counted apart), the audio callback never lost",
             "late +" + dl + " (" + realLate + " real, " + stallLate + " in " + stalls + " main-thread stall(s) of up to " + F(longestStall, "F2") + " s, " + seekLate + " seek catch-up) errors +" + de
             + ", callback revived " + dr + ", host restarted " + dh
             + (realLate > 0 ? " — real: " + realLateLog : "") + (dl > 0 ? " | log: " + Synth.LateLog(lateLog0) : "") + " | " + Synth.Stats());

        Progress = "restore";
        Instruments.PushToSynth();
        V6ChecksA.Prepare(); LoadFixture();
        PathManager.AutoHand = wasAutoHand;
        Restore();
        V3Fixes.RestoreSaves(saves);
        sb.Insert(0, "V7ChecksA: " + pass + " pass, " + fail + " fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Progress = "done";
        Done = true;
    }

    // ================================================================== static: octave layers
    /// <summary>Every pitched voice (all 63) × every tile of the fixture's islands × layers ±1 / ±2: the same cube resolved in layer L plays exactly
    /// 12 × L from layer 0 — the fold ran on the base pitch — with the same slot, velocity and timing; then the costumes (the pad's tones, the follow
    /// shadows, shimmer echoes) for each group's voice 0: the whole voicing moves by the layer. Notes that would leave MIDI 21..108 are counted apart.</summary>
    static void Layers()
    {
        var c = PitchedCube(false);
        if (c == null) { Line(false, "octave layers", "no pitched cube in the fixture"); return; }
        var s0 = Save(c);
        var e0 = new VoiceRules.NoteEvent[32]; var e1 = new VoiceRules.NoteEvent[32];
        int checks = 0, exact = 0, edge = 0, edgeOk = 0, bad = 0, costumeChecks = 0, costumeBad = 0, costumeEvents = 0;
        var badSb = new StringBuilder();
        try
        {
            c.bend.Clear(); c.octave = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool costumes = pass == 1;
                c.follow = costumes ? 3 : 0; c.echo = costumes ? 2 : 0; c.shimmer = costumes;
                for (int g = 0; g < Instruments.Count; g++)
                {
                    if (Instruments.IsDrums(g)) continue;
                    int voices = costumes ? 1 : Instruments.VoiceCount(g);
                    for (int v = 0; v < voices; v++)
                    {
                        c.instrument = g; c.voice = v;
                        foreach (var kb in SM.Islands)
                        {
                            if (kb == null || kb.IsMoon) continue;
                            foreach (var t in kb.tiles)
                            {
                                if (t == null) continue;
                                c.layer = 0;
                                int n0 = Resolve(c, kb, t, 0, e0, 0, costumes ? 2 : 0);
                                for (int L = -2; L <= 2; L++)
                                {
                                    if (L == 0) continue;
                                    c.layer = L;
                                    int n1 = Resolve(c, kb, t, 0, e1, 0, costumes ? 2 : 0);
                                    bool inRange = true;
                                    for (int i = 0; i < n0; i++) { int w = e0[i].midi + 12 * L; if (w < 21 || w > 108) inRange = false; }
                                    bool same = n1 == n0;
                                    for (int i = 0; same && i < n0; i++)
                                        same = e1[i].slot == e0[i].slot && e1[i].vel == e0[i].vel && e1[i].onDsp == e0[i].onDsp && e1[i].offDsp == e0[i].offDsp && e1[i].layer == L
                                               && e1[i].midi == VoiceRules.Layered(e0[i].midi, L);
                                    if (costumes) { costumeChecks++; costumeEvents += n1; if (!same && inRange) { costumeBad++; if (badSb.Length < 700) badSb.Append("costumes g" + g + " '" + kb.assignedChord + "' tile " + t.midi + " L" + L + ": n " + n0 + "/" + n1 + "; "); } continue; }
                                    checks++;
                                    if (!inRange) { edge++; if (same) edgeOk++; continue; }
                                    bool plus = same;
                                    for (int i = 0; plus && i < n0; i++) plus = e1[i].midi == e0[i].midi + 12 * L;
                                    if (plus) exact++;
                                    else { bad++; if (badSb.Length < 700) badSb.Append(Instruments.GroupWords[g] + "/" + v + " '" + kb.assignedChord + "' tile " + t.midi + " L" + L + ": " + (n0 > 0 ? e0[0].midi : -1) + " → " + (n1 > 0 ? e1[0].midi : -1) + "; "); }
                                }
                            }
                        }
                    }
                }
            }
        }
        finally { Put(c, s0); }
        Line(bad == 0 && costumeBad == 0 && exact > 5000 && edgeOk == edge,
             "octave layers: every pitched voice × every fixture tile × layers ±1 / ±2 sounds exactly 12 × layer from layer 0 AFTER the folds (same slot, velocity, timing); the costumes (pad tones, follow shadows, shimmer echoes) move with it",
             exact + " / " + checks + " exact (+12 / −12 / +24 / −24), " + edge + " at the MIDI 21..108 edge (kept there by octaves: " + edgeOk + " ok), costumes " + (costumeChecks - costumeBad) + " / " + costumeChecks + " voicings (" + costumeEvents + " notes)" + (badSb.Length > 0 ? " — " + badSb : ""));
    }

    /// <summary>The cases the v6 pitch rule got wrong (M0 added 12 × layer BEFORE the fold, so the fold pulled a copy back onto its source): the v7 note is
    /// the source's folded note ± 12 × layer.</summary>
    static void LayerCases()
    {
        //                 group voice pitch layer
        int[,] cases = { { 3, 0, 84, 1 }, { 2, 0, 72, 1 }, { 0, 0, 50, -1 }, { 1, 0, 60, -2 }, { 5, 0, 90, 1 }, { 6, 0, 84, 1 }, { 7, 0, 76, 1 }, { 4, 0, 45, -1 }, { 8, 0, 90, 1 }, { 3, 2, 60, 2 } };
        var d = new StringBuilder(); int undone = 0, ok = 0, n = cases.GetLength(0);
        for (int i = 0; i < n; i++)
        {
            int slot = Instruments.SlotOf(cases[i, 0], cases[i, 1]), p = cases[i, 2], L = cases[i, 3];
            int src = VoiceRules.Fold(slot, p, 0), m0 = VoiceRules.Fold(slot, p + 12 * L, 0), v7 = VoiceRules.Layered(src, L);
            if (m0 == src) undone++;
            if (v7 == src + 12 * L) ok++;
            d.Append(Instruments.GroupWords[cases[i, 0]]).Append(cases[i, 1] > 0 ? "/" + cases[i, 1] : "").Append(" tile ").Append(p).Append(L > 0 ? " +" : " ").Append(L)
             .Append(": source ").Append(src).Append(", M0 fold ").Append(m0).Append(m0 == src ? " (undone)" : "").Append(", v7 ").Append(v7).Append(i < n - 1 ? " | " : "");
        }
        Line(ok == n && undone >= 6, "the folds no longer undo a layer: the copy sounds the source's folded note ± 12 × layer where M0's fold-after-the-layer pulled it back onto its source",
             ok + "/" + n + " right, " + undone + " of them undone by M0's rule — " + d);
    }

    // ================================================================== static: bends
    static readonly int[] BendPattern = { 2, 7, -5, 5, -7, 3, 4, -3 };

    /// <summary>A harmony cube's bends: node i plays Fold(tile + bend[i]) (bent, chord-tone law exempt), only on its HOME tile (another island's tile:
    /// no bend), never the stack's octave lift, no costumes around a bent note, no bass anchor on a bent node.</summary>
    static void Bends()
    {
        var c = PitchedCube(true);
        if (c == null) { Line(false, "bends", "no pitched cube"); return; }
        var s0 = Save(c);
        var ev = new VoiceRules.NoteEvent[32];
        var bad = new StringBuilder(); int nodes = 0, nonTones = 0, stackOk = 0, costumeOk = 0, homeOk = 0, anchorOk = 0; string eg = "";
        try
        {
            c.follow = 0; c.echo = 0; c.shimmer = false; c.layer = 0; c.octave = 0;
            var kb = c.Island; int slot = Instruments.SlotOf(c.instrument, c.voice);
            c.bend.Clear(); for (int i = 0; i < c.nodes.Count; i++) c.bend.Add(BendPattern[i % BendPattern.Length]);
            for (int i = 0; i < c.nodes.Count; i++)
            {
                var t = c.nodes[i];
                int n = Resolve(c, kb, t, i, ev);
                int want = VoiceRules.Fold(slot, t.midi + SongManager.Transpose + c.bend[i], kb.register);
                string why = n > 0 ? VoiceRules.HarmonicCheck(ev[0]) : "none";
                nodes++;
                if (n != 1 || ev[0].midi != want || !ev[0].bent || why != null) bad.Append("node " + i + ": n " + n + " midi " + (n > 0 ? ev[0].midi : -1) + " want " + want + " bent " + (n > 0 && ev[0].bent) + " check " + why + "; ");
                if (n > 0 && !IsChordTone(kb, ev[0].midi)) nonTones++;
                if (i == 0) eg = "tile " + t.midi + " bend +" + c.bend[i] + " → " + (n > 0 ? ev[0].midi : -1);
                // the stack: another cube under it (stack 1, this one on top) — a bent note keeps its pitch
                int n2 = Resolve(c, kb, t, i, ev, 1);
                if (n2 == 1 && ev[0].midi == want) stackOk++; else bad.Append("node " + i + " stacked: " + (n2 > 0 ? ev[0].midi : -1) + "; ");
            }
            // another island's tile (a carried / flow window): the bend belongs to the home tile — none there
            KeyBlock other = null;
            foreach (var k in SM.Islands) if (k != null && k != kb && !k.IsMoon && !VoiceRules.IsMelody(k)) { other = k; break; }
            if (other != null)
            {
                var t2 = other.GetTile(0, Mathf.Min(1, other.rows - 1));
                int n = Resolve(c, other, t2, 0, ev);
                int want = VoiceRules.Fold(slot, t2.midi + SongManager.Transpose, other.register);
                if (n >= 1 && ev[0].midi == want && !ev[0].bent && VoiceRules.BendOf(c, t2, 0) == 0) homeOk++; else bad.Append("other island: " + (n > 0 ? ev[0].midi : -1) + " want " + want + "; ");
            }
            // costumes: a pad with follow shadows and shimmer echoes — a bent note alone (+ its plain echoes), an unbent one with its chord tones
            c.instrument = 2; c.voice = 0; c.follow = 3; c.shimmer = true;
            int bentN = Resolve(c, kb, c.nodes[0], 0, ev, 0, 0);
            c.bend.Clear();
            int plainN = Resolve(c, kb, c.nodes[0], 0, ev, 0, 0);
            if (bentN == 1 && plainN >= 3) costumeOk++; else bad.Append("costumes: bent " + bentN + " notes, unbent " + plainN + "; ");
            // the bass anchor: a bass cube on a 7th / 9th tile on a downbeat plays the root; bent, it plays its bend
            c.instrument = 4; c.voice = 0; c.follow = 0; c.shimmer = false;
            TileInteraction seventh = null; int sn = -1;
            foreach (var k in SM.Islands)
            {
                if (k == null || k.IsMoon || VoiceRules.IsMelody(k)) continue;
                foreach (var t in k.tiles) if (t != null && (t.role == ChordRole.Seventh || t.role == ChordRole.Extension)) { seventh = t; break; }
                if (seventh != null) break;
            }
            if (seventh != null)
            {
                KeyBlock sk = seventh.island;
                for (int i = 0; i < c.nodes.Count; i++) if (c.nodes[i] == seventh) sn = i;
                int bslot = Instruments.SlotOf(4, 0);
                int anchored = VoiceRules.Fold(bslot, sk.chordRootMIDI + SongManager.Transpose + 12 * sk.register, sk.register);
                int plain = ResolveDown(c, sk, seventh, 0, ev) > 0 ? ev[0].midi : -1;
                // a bent node: make the 7th tile the cube's own node 0 temporarily
                var home0 = c.nodes[0]; c.nodes[0] = seventh; c.bend.Clear(); for (int i = 0; i < c.nodes.Count; i++) c.bend.Add(i == 0 ? 5 : 0);
                int bentM = ResolveDown(c, sk, seventh, 0, ev) > 0 ? ev[0].midi : -1;
                c.nodes[0] = home0;
                int wantBent = VoiceRules.Fold(bslot, seventh.midi + SongManager.Transpose + 5, sk.register);
                if (plain == anchored && bentM == wantBent) anchorOk++; else bad.Append("anchor: plain " + plain + " (root " + anchored + "), bent " + bentM + " (want " + wantBent + "); ");
            }
            else anchorOk++;   // no 7th / 9th tile in the fixture: nothing to anchor
        }
        finally { Put(c, s0); }
        Line(bad.Length == 0 && nodes > 0 && homeOk == 1 && costumeOk == 1 && anchorOk == 1 && stackOk == nodes,
             "bends: a harmony cube's node plays Fold(tile + bend) (bent: exempt from the chord-tone law), only on its home tile, never lifted by a stack, alone (no pad tones / shadows around it), never anchored",
             nodes + " bent nodes (" + nonTones + " of them not chord tones, all passing HarmonicCheck), e.g. " + eg + "; stack-proof " + stackOk + "/" + nodes + ", carried tile unbent " + homeOk + ", costumes " + costumeOk + ", anchor " + anchorOk + (bad.Length > 0 ? " — " + bad : ""));
    }

    /// <summary>Resolves node <paramref name="node"/> on a DOWNBEAT (local beat 0 of the window: the bass anchor's rule 4 applies there).</summary>
    static int ResolveDown(AudioCube c, KeyBlock island, TileInteraction tile, int node, VoiceRules.NoteEvent[] ev)
    {
        double on = StaticOn, bps = GlobalClock.BeatsPerSecond;
        float len = (float)(2.0 * bps);   // windowEnd = on + 2 s → local beat = len − 2 s × bps = 0
        var hit = new AudioCube.Hit { fires = true, node = node, weight = 1, ratchet = 1 };
        return VoiceRules.Resolve(c, new AudioCube.Window { start = 0f, length = len, island = island, order = 0 }, tile, hit, 0, 0, on, 0.25, on + 2.0, double.PositiveInfinity, ev);
    }

    static bool IsChordTone(KeyBlock kb, int midi)
    {
        int pc = (((midi - kb.chordRootMIDI - SongManager.Transpose) % 12) + 12) % 12;
        foreach (int s in kb.semitoneList) if (((s % 12) + 12) % 12 == pc) return true;
        return false;
    }

    // ================================================================== static: previews / auditions / presses
    /// <summary>For a cube in every layer, bent and not: NodeMidi = the preview = the audition = the press of each node = what Resolve plays; and the
    /// v6 builders (no layer, no bend) give exactly the v6 note for every voice on every fixture tile.</summary>
    static void Previews()
    {
        var c = PitchedCube(true);
        if (c == null) { Line(false, "previews", "no pitched cube"); return; }
        var s0 = Save(c);
        var ev = new VoiceRules.NoteEvent[32];
        var bad = new StringBuilder(); int checks = 0; string eg = "";
        try
        {
            c.follow = 0; c.echo = 0; c.shimmer = false; c.octave = 0;
            for (int L = -2; L <= 2; L++)
                for (int b = 0; b < 2; b++)
                {
                    c.layer = L;
                    c.bend.Clear(); if (b == 1) for (int i = 0; i < c.nodes.Count; i++) c.bend.Add(BendPattern[(i + 1) % BendPattern.Length]);
                    for (int i = 0; i < c.nodes.Count; i++)
                    {
                        int n = Resolve(c, c.Island, c.nodes[i], i, ev);
                        int want = n > 0 ? ev[0].midi : -1;
                        int node = VoiceRules.NodeMidi(c, i);
                        var pe = VoiceRules.PreviewEvent(c, i);
                        var ae = VoiceRules.AuditionEvent(c, i, 0.3, TileInteraction.HoverVolume);
                        var pr = VoiceRules.PressEvent(c, i, 0.3);
                        checks++;
                        if (node != want || pe.midi != want || ae.midi != want || pr.midi != want || ae.layer != L || ae.bent != (b == 1 && c.BendAt(i) != 0) || pr.vel < Synth.PressMinVelocity)
                            bad.Append("L" + L + (b == 1 ? " bent" : "") + " node " + i + ": resolve " + want + " node " + node + " preview " + pe.midi + " audition " + ae.midi + " press " + pr.midi + "; ");
                        if (L == 1 && b == 1 && i == 0) eg = "layer +1, bend +" + c.BendAt(0) + " on tile " + c.nodes[0].midi + " → " + want;
                    }
                }
        }
        finally { Put(c, s0); }
        // the v6 builders unchanged (layer 0, no bend): every voice × every fixture tile
        int v6 = 0; var bad6 = new StringBuilder();
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            if (SynthBank.Def(s).drums) continue;
            foreach (var kb in SM.Islands)
            {
                if (kb == null || kb.IsMoon) continue;
                foreach (var t in kb.tiles)
                {
                    if (t == null) continue;
                    for (int o = -1; o <= 1; o++)
                    {
                        int want = kb.IsKeyboard ? VoiceRules.FoldMelody(s, t.midi + SongManager.Transpose + 12 * o, kb.register) : VoiceRules.Fold(s, t.midi + SongManager.Transpose + 12 * o, kb.register);
                        int p = VoiceRules.PreviewEvent(s, t, o).midi, a = VoiceRules.AuditionEvent(s, t, o, 0.3, 0.7f).midi, q = VoiceRules.PressEvent(s, t, o, 0.3).midi;
                        v6++;
                        if (p != want || a != want || q != want) { if (bad6.Length < 400) bad6.Append("slot " + s + " tile " + t.midi + " oct " + o + ": " + p + "/" + a + "/" + q + " want " + want + "; "); }
                    }
                }
            }
        }
        Line(bad.Length == 0 && bad6.Length == 0 && checks > 0,
             "previews: NodeMidi = PreviewEvent = AuditionEvent = PressEvent of a cube's node = what Resolve plays, in every layer, bent or not (a harmony cube's bent note, an octave copy in its octave); the v6 builders are unchanged without layer / bend",
             checks + " node checks (e.g. " + eg + "), " + v6 + " v6 previews / auditions / presses unchanged" + (bad.Length > 0 ? " — " + bad : "") + (bad6.Length > 0 ? " — v6: " + bad6 : ""));
    }

    // ================================================================== static: layer cubes and the stacks
    static void Stacks()
    {
        var c = PitchedCube(false);
        if (c == null) { Line(false, "stacks", "no pitched cube"); return; }
        var tile = c.nodes[0];
        var s0 = Save(c);
        var ev = new VoiceRules.NoteEvent[32];
        int idx0 = SequenceMaster.IndexOn(tile, c), cnt0 = SequenceMaster.CountOn(tile);
        int occ = 0, idx1 = 0, cnt1 = 0, layeredMidi = 0, liftedMidi = 0, wantLayered = 0, wantLifted = 0;
        float h0 = SequenceMaster.StackHeight(tile, 4), h1 = 0f;
        try
        {
            c.follow = 0; c.echo = 0; c.shimmer = false; c.bend.Clear();
            c.layer = 1;
            occ = SequenceMaster.Occupy(tile, c);                 // refused: a layer cube never occupies (and leaves any stack it was on)
            idx1 = SequenceMaster.IndexOn(tile, c); cnt1 = SequenceMaster.CountOn(tile); h1 = SequenceMaster.StackHeight(tile, 4);
            int slot = Instruments.SlotOf(c.instrument, c.voice);
            int basePitch = tile.midi + SongManager.Transpose + 12 * c.octave;
            Resolve(c, c.Island, tile, 0, ev, 1);                  // another cube under it: a layer cube still takes no octave lift
            layeredMidi = ev[0].midi; wantLayered = VoiceRules.Layered(VoiceRules.Fold(slot, basePitch, c.Island.register), 1);
            c.layer = 0;
            Resolve(c, c.Island, tile, 0, ev, 1);                  // the same cube in layer 0, on top of that cube: the v2 stack lift
            liftedMidi = ev[0].midi; wantLifted = VoiceRules.Fold(slot, basePitch + 12, c.Island.register);
        }
        finally { Put(c, s0); if (idx0 >= 0) SequenceMaster.Occupy(tile, c); }
        bool ok = occ == -1 && idx1 == -1 && cnt1 == Mathf.Max(0, cnt0 - (idx0 >= 0 ? 1 : 0)) && layeredMidi == wantLayered && liftedMidi == wantLifted;
        Line(ok, "layer cubes never occupy a tile (SequenceMaster: Occupy refused, off the stack, not counted, no stack height) and their notes never take the stack's octave lift; a layer-0 cube on top still does",
             "Occupy → " + occ + ", index " + idx0 + " → " + idx1 + ", stack " + cnt0 + " → " + cnt1 + ", height(4) " + F(h0) + " → " + F(h1) + "; layer +1 over a cube " + layeredMidi + " (want " + wantLayered + "), layer 0 on top " + liftedMidi + " (want " + wantLifted + ")");
    }

    // ================================================================== static: harmonic-safety exemptions
    static void Exemptions()
    {
        KeyBlock isl = null;
        foreach (var kb in SM.Islands) if (kb != null && !kb.IsMoon && !VoiceRules.IsMelody(kb) && kb.semitoneList.Count >= 3) { isl = kb; break; }
        if (isl == null) { Line(false, "exemptions", "no chord island"); return; }
        double on = 100.0;
        Func<int, int, int, bool, VoiceRules.NoteEvent> E = (slot, midi, layer, bent) => new VoiceRules.NoteEvent { slot = slot, midi = midi, vel = 90, onDsp = on, offDsp = on + 0.2, owner = 99, winStartDsp = on - 0.1, winEndDsp = on + 1.0, island = isl, layer = layer, bent = bent };
        // a flow pedal / a varied note: it must FIT the chord (a chord tone or a key tension), an avoid note fails
        var key = MusicTheory.KeyOfSong();
        int tension = -1, avoidN = -1;
        for (int k = 0; k < 12; k++)
        {
            int m = 60 + k; float sc = Harmony.NoteScore(m - SongManager.Transpose, isl.chordRootMIDI, isl.semitoneList, key.tonic, key.minor);
            if (sc > 0.5f && sc < 0.99f && tension < 0) tension = m;
            if (sc < 0.01f && avoidN < 0) avoidN = m;
        }
        Func<int, VoiceRules.NoteEvent> Fit = midi => { var ev = E(Instruments.SlotOf(0, 0), midi, 0, false); ev.fit = true; return ev; };
        string fitT = tension > 0 ? VoiceRules.HarmonicCheck(Fit(tension)) : "none", fitA = avoidN > 0 ? VoiceRules.HarmonicCheck(Fit(avoidN)) : "none";
        string plainT = tension > 0 ? VoiceRules.HarmonicCheck(E(Instruments.SlotOf(0, 0), tension, 0, false)) : "none";
        int root = isl.chordRootMIDI + SongManager.Transpose + 12 * isl.register;
        int non = -1;
        for (int k = 1; k < 12 && non < 0; k++) { int m = root + 12 + k; if (!IsChordTone(isl, m)) non = m; }
        while (non < 60) non += 12;
        int keys = Instruments.SlotOf(0, 0), bass = Instruments.SlotOf(4, 0);
        int tone = root + 24; while (tone < 60) tone += 12;
        int low = tone; while (low >= 48) low -= 12;   // a chord tone under 48
        string a = VoiceRules.HarmonicCheck(E(keys, non, 0, false));          // a non-tone: fails (v6)
        string b = VoiceRules.HarmonicCheck(E(keys, non, 0, true));           // the same pitch bent: a harmony voice, passes
        string c = VoiceRules.HarmonicCheck(E(keys, low, -1, false));         // layer −1 of a legal note (base low + 12 ≥ 48): passes
        string d = VoiceRules.HarmonicCheck(E(keys, low, 0, false));          // layer 0 under 48: the low-end law (v6)
        string e = VoiceRules.HarmonicCheck(E(keys, low - 12, -1, false));    // a copy of a note that was already under 48: fails
        string f = VoiceRules.HarmonicCheck(E(bass, low - 12, 0, false));     // the bass may (v6)
        var late = E(keys, non, 0, true); late.offDsp = late.winEndDsp - 0.001;
        string g = VoiceRules.HarmonicCheck(late);                              // a bent note still obeys the timing rules
        bool ok = a != null && b == null && c == null && d != null && e != null && f == null && g != null && tension > 0 && fitT == null && plainT != null && avoidN > 0 && fitA != null;
        Line(ok, "harmonic check exemptions: a bent note skips the chord-tone law (Harmony chose it), a flow pedal / a varied note must only FIT the chord (a key tension passes, an avoid note fails), the low-end law judges the note before its octave layer, the timing rules still hold (stairs / phrases: see their check)",
             "non-tone " + non + ": " + (a ?? "ok") + " | bent " + (b ?? "ok") + " | layer −1 at " + low + ": " + (c ?? "ok") + " | layer 0 at " + low + ": " + (d ?? "ok") + " | layer −1 at " + (low - 12) + ": " + (e ?? "ok") + " | bass " + (f ?? "ok") + " | bent, late off: " + (g ?? "ok")
             + " | tension " + tension + " as a flow / varied note: " + (fitT ?? "ok") + " (plain: " + (plainT ?? "ok") + ") | avoid " + avoidN + " as a flow / varied note: " + (fitA ?? "ok"));
    }

    // ================================================================== static: the riser's channel and its measured swell
    static void RiserStatic()
    {
        bool chans = SynthBank.ChannelCount == SynthBank.Slots + 1 && Synth.ChannelCount == SynthBank.ChannelCount && Synth.PreviewChannelCount == SynthBank.ChannelCount;
        bool fx = SynthBank.ChannelOf(SynthBank.FxSlot) == SynthBank.FxChannel && SynthBank.FxChannel == SynthBank.Slots && !SynthBank.IsPercussionChannel(SynthBank.FxChannel) && !Synth.IsPercussionChannel(SynthBank.FxChannel);
        bool voicesUntouched = true;
        for (int s = 0; s < SynthBank.Slots; s++) if (SynthBank.ChannelOf(s) == SynthBank.FxChannel) voicesUntouched = false;
        string preset = Synth.RiserPresetName;
        float root = Synth.RiserPeakRoot, cents = Synth.RiserCentsPerKey;
        bool nums = Synth.RiserMeasured && root > 1.36f && root < 1.43f && cents > 48f && cents < 52f && Synth.RiserEndRoot > root;
        int bad = 0;
        for (int k = LaunchRiser.MinKey; k <= LaunchRiser.MaxKey; k++) if (Synth.RiserKeyFor(Synth.RiserPeakSeconds(k), 0, 127, false) != k || Synth.RiserKeyFor(Synth.RiserPeakSeconds(k) + 0.004, 0, 127, true) != k) bad++;
        float bpm0 = GlobalClock.BPM;
        double at120, at162, at60, at240;
        GlobalClock.BPM = 120f; at120 = LaunchRiser.PlannedSeconds();
        GlobalClock.BPM = 162f; at162 = LaunchRiser.PlannedSeconds();
        GlobalClock.BPM = 60f; at60 = LaunchRiser.PlannedSeconds();
        GlobalClock.BPM = 240f; at240 = LaunchRiser.PlannedSeconds();
        GlobalClock.BPM = bpm0;
        bool plan = Math.Abs(at120 - 2.0) < 1e-6 && Math.Abs(at162 - 4.0 * 60.0 / 162.0) < 1e-6 && Math.Abs(at60 - LaunchRiser.MaxSeconds) < 1e-6 && Math.Abs(at240 - LaunchRiser.LongSeconds) < 1e-6;
        Line(chans && fx && voicesUntouched && preset == SynthBank.RiserPreset && nums && bad == 0 && plan,
             "the riser: GeneralUser GS 'Reverse Cymbal' on the song bus's own effects channel (after the " + SynthBank.Slots + " voices, melodic), its swell's peak rendered at boot, the key ↔ swell length map round-trips, one bar of swell (1.0..2.8 s)",
             "channels " + Synth.ChannelCount + " / " + Synth.PreviewChannelCount + " (fx " + SynthBank.FxChannel + "), preset '" + preset + "' (" + SynthBank.RiserBank + ":" + SynthBank.RiserPatch + "), measured " + Synth.RiserMeasured
             + " in " + F(Synth.RiserMeasureMs, "F1") + " ms on a worker thread: peak " + F(root, "F3") + " s after the note-on at key " + SynthEngine.RiserRootKey + " (sound ends " + F(Synth.RiserEndRoot, "F3") + " s), "
             + F(cents, "F1") + " cents per key, peak level " + F(Synth.RiserPeakDb, "F1") + " dB before the master gain" + (Synth.RiserError != null ? " (error " + Synth.RiserError + ")" : "")
             + "; keys " + LaunchRiser.MinKey + " / 48 / 60 / 72 / " + LaunchRiser.MaxKey + " peak at " + F(Synth.RiserPeakSeconds(LaunchRiser.MinKey), "F3") + " / " + F(Synth.RiserPeakSeconds(48), "F3") + " / " + F(Synth.RiserPeakSeconds(60), "F3") + " / " + F(Synth.RiserPeakSeconds(72), "F3") + " / " + F(Synth.RiserPeakSeconds(LaunchRiser.MaxKey), "F3") + " s"
             + ", map misses " + bad + "; planned swell at 120 / 162 / 60 / 240 bpm: " + F(at120, "F2") + " / " + F(at162, "F2") + " / " + F(at60, "F2") + " / " + F(at240, "F2") + " s");
    }

    // ================================================================== stairs and phrase tiles
    static KeyBlock FirstKind(int kind)
    {
        foreach (var kb in SM.Islands) if (kb != null && kb.kind == kind && kb.tiles.Count > 0) return kb;
        return null;
    }
    static AudioCube CubeOn(KeyBlock kb)
    {
        foreach (var c in SequenceMaster.Cubes) if (c != null && !c.IsOnMoon && c.Island == kb && c.nodes.Count > 0) return c;
        return null;
    }

    /// <summary>Stairs (O's AddStairIsland, B's steps): every type × both directions — the steps' pitches are SongManager.StairPitches, the runner
    /// (the lead) plays each step's own pitch (FoldRun: a run keeps its octave; one shift for the whole run), strictly falling / rising as heard, all
    /// passing HarmonicCheck (stairs are exempt); a walk (the bass range) played by a lead moves up as a whole. A phrase (O's AddPhrase, B's roll):
    /// every row plays its pitch (the bass an octave down, as a whole), exempt from the chord-tone law.</summary>
    static IEnumerator StairsAndPhrase()
    {
        LoadFixture(); yield return Frames(3);
        int idx = -1; string err = null;
        try { idx = SM.AddStairIsland(0, false, -1); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
        yield return Frames(6);
        KeyBlock st = FirstKind(3);
        var ev = new VoiceRules.NoteEvent[32];
        if (st == null)
            Line(false, "stairs tiles play their pitch (FoldRun: a run keeps its shape) and pass the harmonic check", "no stairs island with tiles (AddStairIsland → " + idx + (err != null ? ", " + err : "") + "): O's op / B's build not landed yet");
        else
        {
            var bad = new StringBuilder(); var eg = new StringBuilder(); int runs = 0, steps = 0, exempt = 0;
            string[] words = { "chord", "scale", "spark", "slide", "bright", "walk" };
            for (int type = 0; type < 6; type++)
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    int at = SM.Islands.IndexOf(st);
                    try { SM.SetStair(at, type, dir, -1, -1, -1); } catch (Exception e) { bad.Append("SetStair threw " + e.Message + "; "); }
                    yield return Frames(3);
                    st = FirstKind(3);
                    if (st == null) { bad.Append("stairs gone after SetStair; "); break; }
                    var runner = CubeOn(st);
                    AudioCube c = runner != null ? runner : PitchedCube(true);
                    if (c == null) { bad.Append("no cube; "); break; }
                    var s0 = Save(c);
                    try
                    {
                        if (runner == null) { c.instrument = 3; c.voice = 0; }
                        c.layer = 0; c.bend.Clear(); c.octave = 0; c.follow = 0; c.echo = 0; c.shimmer = false;
                        int slot = Instruments.SlotOf(c.instrument, c.voice);
                        int[] want = SM.StairPitches(st);
                        var tiles = new List<TileInteraction>(); foreach (var t in st.tiles) if (t != null) tiles.Add(t);
                        tiles.Sort((x, y) => x.gridX.CompareTo(y.gridX));
                        var heard = new List<int>(); int shift0 = int.MinValue; bool uniform = true;
                        for (int i = 0; i < tiles.Count; i++)
                        {
                            var t = tiles[i];
                            if (i < want.Length && t.midi != want[i]) bad.Append(words[type] + (dir < 0 ? "↓" : "↑") + " step " + i + " tile " + t.midi + " != StairPitches " + want[i] + "; ");
                            int n = Resolve(c, st, t, 0, ev);
                            if (n < 1) { bad.Append("step " + i + " silent; "); continue; }
                            steps++;
                            if (VoiceRules.HarmonicCheck(ev[0]) == null) exempt++; else bad.Append("step " + i + " check: " + VoiceRules.HarmonicCheck(ev[0]) + "; ");
                            int shift = ev[0].midi - (t.midi + SongManager.Transpose);
                            if (shift0 == int.MinValue) shift0 = shift; else if (shift != shift0) uniform = false;
                            heard.Add(ev[0].midi);
                        }
                        bool mono = true;
                        for (int i = 1; i < heard.Count; i++) if (dir < 0 ? heard[i] >= heard[i - 1] : heard[i] <= heard[i - 1]) mono = false;
                        bool asIs = shift0 == 12 * SynthBank.Def(slot).octaveShift;   // inside the melody window: its own pitch (the bass an octave down)
                        bool walkUp = type == 5 && shift0 == 12 * SynthBank.Def(slot).octaveShift + 12 && !VoiceRules.IsBass(slot);
                        if (!mono || !uniform || !(asIs || walkUp)) bad.Append(words[type] + (dir < 0 ? "↓" : "↑") + ": heard " + string.Join(" ", heard) + " (mono " + mono + ", uniform " + uniform + ", shift " + shift0 + "); ");
                        runs++;
                        if (dir < 0 || type == 5) eg.Append(words[type]).Append(dir < 0 ? "↓ " : "↑ ").Append(string.Join(" ", want)).Append(" → ").Append(string.Join(" ", heard)).Append(" | ");
                    }
                    finally { Put(c, s0); }
                }
            Line(bad.Length == 0 && runs == 12 && steps >= 36,
                 "stairs tiles play their pitch: the steps are SongManager.StairPitches, the runner hears each step as it is (a run keeps its octave: one shift for the whole run, a walk under the lead's window moves up as a whole), strictly falling / rising, exempt from the chord-tone law",
                 runs + " runs (6 types × ↓ / ↑), " + steps + " steps, " + exempt + " pass HarmonicCheck — " + eg + (bad.Length > 0 ? " — " + bad : ""));
        }

        // ---- the phrase (kind 4)
        LoadFixture(); yield return Frames(3);
        int pi = -1; err = null;
        try { pi = SongOps.AddPhrase(0, -1); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
        yield return Frames(6);
        if (PathManager.I != null) PathManager.I.PutDown();
        KeyBlock ph = FirstKind(4);
        if (ph == null) Line(false, "phrase tiles play their pitch", "no phrase island with tiles (AddPhrase → " + pi + (err != null ? ", " + err : "") + "): O's op / B's build not landed yet");
        else
        {
            var c = PitchedCube(true);
            var bad = new StringBuilder(); int cells = 0, nonTones = 0; string eg = "";
            if (c == null) bad.Append("no cube; ");
            else
            {
                var s0 = Save(c);
                try
                {
                    c.layer = 0; c.bend.Clear(); c.octave = 0; c.follow = 0; c.echo = 0; c.shimmer = false;
                    foreach (int g in new[] { 3, 4, 8 })   // a lead, the bass (an octave down, as a whole), the piano
                    {
                        c.instrument = g; c.voice = 0;
                        int slot = Instruments.SlotOf(g, 0), oct = 12 * SynthBank.Def(slot).octaveShift;
                        var heard = new List<int>();
                        for (int z = 0; z < ph.rows; z++)
                        {
                            var t = ph.GetTile(0, z);
                            if (t == null) continue;
                            int n = Resolve(c, ph, t, 0, ev);
                            cells++;
                            if (n < 1 || ev[0].midi != t.midi + SongManager.Transpose + oct || VoiceRules.HarmonicCheck(ev[0]) != null)
                                bad.Append(Instruments.GroupWords[g] + " row " + z + ": tile " + t.midi + " heard " + (n > 0 ? ev[0].midi : -1) + " check " + (n > 0 ? VoiceRules.HarmonicCheck(ev[0]) : "none") + "; ");
                            if (n > 0) { heard.Add(ev[0].midi); if (!IsChordTone(ph, ev[0].midi)) nonTones++; }
                        }
                        if (g == 3) eg = "lead rows " + string.Join(" ", heard);
                    }
                }
                finally { Put(c, s0); }
            }
            Line(bad.Length == 0 && cells >= 30, "phrase tiles play their pitch (a lead / the piano as they are, the bass an octave down as a whole) and pass the harmonic check (a phrase is a melody: no chord-tone law)",
                 cells + " cells, " + nonTones + " off the chord — " + eg + (bad.Length > 0 ? " — " + bad : ""));
        }
        LoadFixture(); yield return Frames(3);
    }

    // ================================================================== timed: an octave copy and a harmony cube in the playing song
    /// <summary>The fixture + two octave copies (+1, −1) and a harmony cube (bends, one row up) of the pad cube that plays alone on island 1 (no
    /// stacking: every difference is the layer / the bend), all with the source's seed: played for two bars, every note of a copy is the source's note
    /// at the same time ± 12 (its pad tones too); every note of the harmony cube is one of its nodes' NodeMidi and passes HarmonicCheck; the copies
    /// never occupy a tile. Then H's CubeOps.OctaveCopy when it exists (INFO).</summary>
    static IEnumerator LayerAndHarmonyInSong()
    {
        var st = Fixture();
        int si = -1;
        for (int i = 0; i < st.cubes.Length; i++) if (st.cubes[i].measure == 1 && st.cubes[i].moon < 0) { si = i; break; }
        if (si < 0) { Line(false, "layers in the song", "no cube on island 1 in the fixture"); yield break; }
        var src = st.cubes[si]; src.id = 9100;
        if (src.seed == 0) src.seed = src.SeedOrDerived();
        var up = CubeState.Clone(src); up.id = 9101; up.layer = 1; up.echoOf = 9100;
        var dn = CubeState.Clone(src); dn.id = 9102; dn.layer = -1; dn.echoOf = 9100;
        var hc = CubeState.Clone(src); hc.id = 9103; hc.echoOf = 9100;
        int rows = st.measures[1].semis != null ? st.measures[1].semis.Length : 4;
        hc.bend = new int[hc.xs.Length];
        for (int j = 0; j < hc.xs.Length; j++) { hc.zs[j] = (hc.zs[j] + 1) % Mathf.Max(1, rows); hc.bend[j] = j % 2 == 0 ? 5 : 7; }
        var list = new List<CubeState>(st.cubes) { up, dn, hc };
        st.cubes = list.ToArray();
        LoadState(st);
        yield return Frames(4);
        AudioCube S = null, U = null, D = null, H = null;
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; if (c.id == 9100) S = c; else if (c.id == 9101) U = c; else if (c.id == 9102) D = c; else if (c.id == 9103) H = c; }
        if (S == null || U == null || D == null || H == null) { Line(false, "layers in the song", "cubes not found after the load (" + (S != null) + (U != null) + (D != null) + (H != null) + ")"); yield break; }
        var log = new List<VoiceRules.NoteEvent>();
        VoiceRules.Tap = e => log.Add(e);
        int late0 = realLate;
        GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait((float)(8.0 / GlobalClock.BeatsPerSecond) + 0.4f);
        GlobalClock.Stop();
        VoiceRules.Tap = null;
        yield return Frames(2);
        var byOwner = new Dictionary<int, List<VoiceRules.NoteEvent>>();
        foreach (var e in log) { List<VoiceRules.NoteEvent> l; if (!byOwner.TryGetValue(e.owner, out l)) { l = new List<VoiceRules.NoteEvent>(); byOwner[e.owner] = l; } l.Add(e); }
        Func<AudioCube, List<VoiceRules.NoteEvent>> Of = c => { List<VoiceRules.NoteEvent> l; return byOwner.TryGetValue(c.owner, out l) ? l : new List<VoiceRules.NoteEvent>(); };
        var sE = Of(S); var bad = new StringBuilder(); int pairs = 0, unmatched = 0;
        foreach (var pair in new[] { new KeyValuePair<AudioCube, int>(U, 1), new KeyValuePair<AudioCube, int>(D, -1) })
        {
            foreach (var e in Of(pair.Key))
            {
                bool found = false;
                foreach (var s in sE)
                    if (Math.Abs(s.onDsp - e.onDsp) < 1e-6 && s.slot == e.slot && e.midi == VoiceRules.Layered(s.midi, pair.Value)) { found = true; break; }
                if (found) pairs++; else { unmatched++; if (bad.Length < 400) bad.Append("layer " + pair.Value + " note " + e.midi + " @" + F(e.onDsp - GlobalClock.DspNow, "F3") + " has no source note " + (e.midi - 12 * pair.Value) + "; "); }
                string why = VoiceRules.HarmonicCheck(e);
                if (why != null) bad.Append("layer " + pair.Value + ": " + why + "; ");
            }
        }
        var expect = new HashSet<int>();
        for (int n = 0; n < H.nodes.Count; n++) expect.Add(VoiceRules.NodeMidi(H, n));
        int hNotes = 0, hBent = 0, hOff = 0;
        foreach (var e in Of(H))
        {
            hNotes++;
            if (e.bent) hBent++;
            if (!expect.Contains(e.midi)) bad.Append("harmony note " + e.midi + " not a node's NodeMidi; ");
            string why = VoiceRules.HarmonicCheck(e);
            if (why != null) bad.Append("harmony: " + why + "; ");
            if (!IsChordTone(H.Island, e.midi)) hOff++;
        }
        bool noOcc = SequenceMaster.IndexOn(U.nodes[0], U) == -1 && SequenceMaster.IndexOn(D.nodes[0], D) == -1;
        int dl = realLate - late0;
        Line(bad.Length == 0 && pairs > 0 && unmatched == 0 && sE.Count > 0 && hNotes > 0 && hBent == hNotes && noOcc && dl == 0,
             "in the playing song an octave copy sounds exactly ±12 from its source at the same moments (pad tones included), a harmony cube plays its nodes' bent pitches (exempt, chord-safe by Harmony), the copies occupy no tile, no late notes",
             "source " + sE.Count + " notes, copies " + pairs + " matched / " + unmatched + " unmatched, harmony " + hNotes + " notes (" + hBent + " bent, " + hOff + " off the chord), NodeMidi " + string.Join("/", expect) + ", copies on a stack: " + (!noOcc) + ", late +" + dl + (bad.Length > 0 ? " — " + bad : ""));

        // H's ops on a fresh fixture (the layer / bend rules are A's): an octave copy up and down must SOUND ±12 from its source, a harmony cube its bends
        LoadFixture(); yield return Frames(3);
        S = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && !c.IsOnMoon && c.Island == SM.Islands[1] && c.nodes.Count > 0) { S = c; break; }
        AudioCube up1 = null, dn1 = null, hh = null; string err = null;
        if (S != null)
        {
            try { up1 = CubeOps.OctaveCopy(S, 1); } catch (Exception e) { err = "OctaveCopy ↑ threw " + e.Message; }
            yield return Frames(3);
            try { dn1 = CubeOps.OctaveCopy(S, -1); } catch (Exception e) { err = "OctaveCopy ↓ threw " + e.Message; }
            yield return Frames(3);
            try { hh = CubeOps.Harmonize(S, true); } catch (Exception e) { err = "Harmonize threw " + e.Message; }
            yield return Frames(3);
        }
        if (S == null || up1 == null || dn1 == null || hh == null)
            Info("H's CubeOps on the fixture: source " + (S != null) + ", OctaveCopy ↑ " + (up1 != null) + " ↓ " + (dn1 != null) + ", Harmonize " + (hh != null) + (err != null ? " (" + err + ")" : "") + " — last deny: " + CubeOps.LastDeny);
        else
        {
            var diffs = new StringBuilder(); int okN = 0, nodesN = 0; var iv = new StringBuilder();
            for (int n = 0; n < S.nodes.Count && n < up1.nodes.Count && n < dn1.nodes.Count; n++)
            {
                nodesN++;
                int a = VoiceRules.NodeMidi(S, n), u = VoiceRules.NodeMidi(up1, n), dd = VoiceRules.NodeMidi(dn1, n);
                if (u == VoiceRules.Layered(a, 1) && dd == VoiceRules.Layered(a, -1)) okN++; else diffs.Append(a + "→" + u + "/" + dd + " ");
            }
            int hOk = 0, hBent2 = 0, hN = 0;
            int slot = Instruments.SlotOf(hh.instrument, hh.voice);
            for (int n = 0; n < hh.nodes.Count; n++)
            {
                if (hh.ModOf(n) == 1) continue;   // a rest
                hN++;
                var t = hh.nodes[n]; int b = hh.BendAt(n);
                if (b != 0) hBent2++;
                int want = VoiceRules.Layered(VoiceRules.FoldFor(slot, t.midi + SongManager.Transpose + 12 * hh.octave + b, t, b), hh.layer);
                if (VoiceRules.NodeMidi(hh, n) == want) hOk++;
                if (n < S.nodes.Count) iv.Append(VoiceRules.NodeMidi(hh, n) - VoiceRules.NodeMidi(S, n)).Append(' ');
            }
            Line(okN == nodesN && nodesN > 0 && up1.layer == S.layer + 1 && dn1.layer == S.layer - 1 && SequenceMaster.IndexOn(up1.nodes[0], up1) == -1 && SequenceMaster.IndexOn(dn1.nodes[0], dn1) == -1 && hOk == hN && hN > 0,
                 "H's CubeOps through A's pitch rule: OctaveCopy ↑ / ↓ sound exactly ±12 from the source on every node and occupy no tile; Harmonize's cube sounds each node's tile + bend through its fold",
                 "copies: layers " + up1.layer + " / " + dn1.layer + ", " + okN + "/" + nodesN + " nodes ±12" + (diffs.Length > 0 ? " — " + diffs : "") + "; harmony: " + hOk + "/" + hN + " nodes as bent (" + hBent2 + " bent), heard intervals over the source: " + iv);
        }
        LoadFixture(); yield return Frames(2);
    }

    // ================================================================== timed: the long grid and varied passes through the harmonic check
    /// <summary>Two songs from the fixture, each played through a focus loop with every note tapped: (1) island 0 repeating ×3 with vary 1 (its later
    /// passes play Harmony.Vary's notes); (2) THE LONG GRID (§21: "keep cubes on each grid … the grid extending and being longer"): island 1 (it has a
    /// cube; island 2 has none) extended over the next two grids of its section — saved with the v6 HOP style (carryStyle 0), which §21 ignores: every
    /// carried window is a long-grid (flow) window. Every note passes HarmonicCheck; every carried / varied note is stamped fit (it only has to fit
    /// its measure's chord); the rest are chord tones.</summary>
    static IEnumerator FlowAndVaryInSong()
    {
        var bad = new StringBuilder(); int notes = 0, fitN = 0, fitOff = 0, plain = 0, badN = 0, unstamped = 0, carriedW = 0, flowW = 0, variedW = 0, carriedNotes = 0;
        int late0 = realLate;
        for (int phase = 0; phase < 2; phase++)
        {
            var st = Fixture();
            if (phase == 0) { st.measures[0].repeat = 3; st.measures[0].vary = 1; }
            else { st.measures[1].carry = 2; st.measures[1].carryStyle = 0; }
            LoadState(st);
            yield return Frames(4);
            int src = phase == 0 ? 0 : 1;
            var kb = SM.Islands[src];
            int c0 = kb.column, c1 = Mathf.Min(SM.ColumnCount - 1, c0 + (phase == 0 ? 0 : 2));
            foreach (var c in SequenceMaster.Cubes)
                if (c != null) foreach (var w in c.windows) { if (w.carried) carriedW++; if (w.flow) flowW++; if (w.pass >= 1 && w.island != null && w.island.vary >= 1) variedW++; }
            var log = new List<VoiceRules.NoteEvent>();
            VoiceRules.Tap = e => log.Add(e);
            float a = SM.ColumnStart(c0), b = SM.ColumnStart(c1) + SM.ColumnLength(c1);
            GlobalClock.SetRegion(a, b);
            GlobalClock.Seek(a); GlobalClock.Play(); AudioCube.ScheduleAllNow();
            yield return Wait((float)((b - a) / GlobalClock.BeatsPerSecond) + 0.3f);
            GlobalClock.Stop(); GlobalClock.ClearRegion();
            VoiceRules.Tap = null;
            yield return Frames(2);
            // the carried windows, by owner and island (a carried note is stamped with the measure it plays on)
            var carriedOn = new HashSet<long>();
            foreach (var c in SequenceMaster.Cubes) if (c != null) foreach (var w in c.windows) if (w.carried && w.island != null) carriedOn.Add(((long)c.owner << 20) ^ w.island.GetInstanceID());
            foreach (var e in log)
            {
                if (SynthBank.Def(e.slot).drums) continue;
                notes++;
                bool carried = e.island != null && carriedOn.Contains(((long)e.owner << 20) ^ e.island.GetInstanceID());
                if (carried) { carriedNotes++; if (!e.fit) unstamped++; }
                string why = VoiceRules.HarmonicCheck(e);
                if (e.fit) { fitN++; if (e.island != null && !IsChordTone(e.island, e.midi)) fitOff++; } else plain++;
                if (why != null) { badN++; if (bad.Length < 400) bad.Append(why).Append("; "); }
            }
        }
        Line(badN == 0 && notes > 0 && unstamped == 0 && carriedNotes > 0 && variedW > 0 && realLate - late0 == 0,
             "the long grid (§21) and varied passes in the playing song pass the harmonic check: every carried note (whatever its saved style) and every varied note is stamped fit and fits its measure's chord; every other note is a chord tone",
             notes + " notes: " + carriedNotes + " on the long grid's next measures, " + fitN + " fit-stamped (" + fitOff + " key tensions, not chord tones), " + plain + " plain; windows: " + carriedW + " carried ("
             + flowW + " flagged flow — K's §21 rule makes it every one), " + variedW + " varied; late +" + (realLate - late0) + (unstamped > 0 ? " — " + unstamped + " carried notes not stamped fit" : "")
             + (badN > 0 ? " — " + badN + " failed: " + bad : ""));
        LoadFixture(); yield return Frames(2);
    }

    // ================================================================== timed: the riser in WAVs
    /// <summary>The fixture (121 bpm, six one-bar columns) with launches on islands 0 and 3: landings on beats 4 and 16.</summary>
    static SongState LaunchSong(params int[] launching)
    {
        var st = Fixture();
        foreach (int i in launching) if (i >= 0 && i < st.measures.Length) st.measures[i].launch = true;
        st.loop = true;
        return st;
    }

    static void MuteVoices(bool on, bool exceptDrumSlot)
    {
        for (int s = 0; s < SynthBank.Slots; s++) Synth.SetSlotMute(s, on && !(exceptDrumSlot && s == SynthBank.DrumSlot));
        if (!on) Instruments.PushToSynth();
    }

    /// <summary>Plays the loaded song from beat <paramref name="fromBeat"/> while recording <paramref name="seconds"/>; saves the WAV; returns its first
    /// frame's DSP time through <paramref name="rec0"/>.</summary>
    static IEnumerator PlayRecorded(string wav, float seconds, double fromBeat, double[] rec0)
    {
        GlobalClock.Stop(); GlobalClock.Seek(fromBeat);
        yield return Frames(2);
        Synth.StartRecording(seconds);
        yield return Frames(2);
        GlobalClock.Play(); AudioCube.ScheduleAllNow();
        float t0 = Time.realtimeSinceStartup;
        while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < seconds + 4f) yield return null;
        GlobalClock.Stop();
        Synth.SaveRecording(Cap(wav));
        rec0[0] = Synth.RecordingStartDsp;
        yield return Frames(2);
    }

    static List<LaunchRiser.Plan> SentPlans() { var l = new List<LaunchRiser.Plan>(); foreach (var p in LaunchRiser.Log) if (p.sent && !p.cancelled) l.Add(p); return l; }

    // ---- §21 ("just keep cubes on each grid"): a launch is only its sound — sampled every frame while the launch song plays: no cube is flung, and the
    // launching grids' cubes stay over their own platform (XZ inside VisualBounds + a margin)
    static bool noFlingWatch;
    static int nfFrames, nfFlingFrames, nfOffFrames; static float nfWorst; static string nfWhy;
    static IEnumerator NoFlingWatch(List<KeyBlock> launching)
    {
        noFlingWatch = true; nfFrames = 0; nfFlingFrames = 0; nfOffFrames = 0; nfWorst = 0f; nfWhy = null;
        while (noFlingWatch)
        {
            yield return null;
            if (!GlobalClock.IsPlaying) continue;
            nfFrames++;
            bool fl = false, off = false;
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || c.IsOnMoon) continue;
                if (c.Flinging) { fl = true; if (nfWhy == null) nfWhy = "cube " + c.id + " flinging at beat " + F(GlobalClock.SongBeat, "F2"); }
                var kb = c.Island;
                if (kb == null || !launching.Contains(kb)) continue;
                var b = kb.VisualBounds; Vector3 p = c.transform.position;
                float d = Mathf.Max(Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x), Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z));
                if (d > nfWorst) nfWorst = d;
                if (d > 0.75f) { off = true; if (nfWhy == null) nfWhy = "cube " + c.id + " " + F(d) + " u off '" + kb.assignedChord + "' at beat " + F(GlobalClock.SongBeat, "F2"); }
            }
            if (fl) nfFlingFrames++;
            if (off) nfOffFrames++;
        }
    }

    static IEnumerator RiserWavs()
    {
        LoadState(LaunchSong(0, 3));
        yield return Frames(4);
        var i0 = SM.Islands[0]; var i3 = SM.Islands[3];
        Info("launch song: the fixture (" + F(GlobalClock.BPM, "F0") + " bpm) with launches on '" + i0.assignedChord + "' (turn ends at beat " + F(SM.TurnEnd(i0), "F1") + ", lands on '" + (SM.LaunchTarget(i0) != null ? SM.LaunchTarget(i0).assignedChord : "none")
             + "') and '" + i3.assignedChord + "' (beat " + F(SM.TurnEnd(i3), "F1") + ", lands on '" + (SM.LaunchTarget(i3) != null ? SM.LaunchTarget(i3).assignedChord : "none") + "'); planned swell " + F(LaunchRiser.PlannedSeconds(), "F3") + " s = key "
             + Synth.RiserKeyFor(LaunchRiser.PlannedSeconds(), LaunchRiser.MinKey, LaunchRiser.MaxKey, false) + " (peak " + F(Synth.RiserPeakSeconds(Synth.RiserKeyFor(LaunchRiser.PlannedSeconds(), LaunchRiser.MinKey, LaunchRiser.MaxKey, false)), "F3") + " s)");
        var rec0 = new double[1];
        int late0 = realLate;
        SequenceMaster.I.StartCoroutine(NoFlingWatch(new List<KeyBlock> { i0, i3 }));   // §21: the launch moves no cube

        // ---- 1. the riser alone: every voice slot muted on the synth (the groups stay unmuted, so the islands are audible), the crash with them
        MuteVoices(true, false);
        yield return Wait(2.5f);   // the earlier checks' reverb tails die away: the recording starts in silence
        LaunchRiser.ResetLog();
        yield return PlayRecorded("a7_riser_alone.wav", 9.6f, 0.0, rec0);
        MuteVoices(false, false);
        var sent = SentPlans();
        int sr; var x = ReadWav(Cap("a7_riser_alone.wav"), out sr);
        var env = Env10(x, sr);
        var d = new StringBuilder(); int good = 0, inRec = 0; double worst = 0.0; var pk = new StringBuilder();
        for (int i = 0; i < sent.Count; i++)
        {
            var p = sent[i];
            double t = p.landingDsp - rec0[0], ton = p.onDsp - rec0[0];
            if (t < 0.2 || t > env.Length / 1000.0 - 0.35) continue;
            inRec++;
            double lvl, tp = PeakIn(env, Math.Max(0.0, t - 0.8 * p.peak), t + 0.3, out lvl);
            double err = tp - t, pDb = Db(lvl);
            double swell = Db(LevelAt(env, t - 0.5 * p.peak)), after = Db(MaxIn(env, t + 0.06, t + 0.35)), before = Db(MaxIn(env, Math.Max(0.0, ton - 0.4), Math.Max(0.0, ton - 0.03)));
            bool ok = Math.Abs(err) <= 0.030 && swell <= pDb - 5.0 && after <= pDb - 9.0 && before < -60.0;
            if (ok) good++;
            worst = Math.Max(worst, Math.Abs(err));
            d.Append("landing ").Append(F(t, "F3")).Append(" s (beat ").Append(F(p.landingBeat, "F1")).Append(", key ").Append(p.key).Append(", swell ").Append(F(p.peak, "F3")).Append(" s from ").Append(F(ton, "F3"))
             .Append(" s): WAV peak ").Append(F(tp, "F3")).Append(" s (").Append(F(1000.0 * err, "+0;-0")).Append(" ms) at ").Append(F(pDb, "F1")).Append(" dBFS, half-way ").Append(F(swell - pDb, "F1"))
             .Append(" dB, after ").Append(F(after - pDb, "F1")).Append(" dB, before the note-on ").Append(before < -150 ? "silent" : F(before, "F1") + " dBFS").Append(ok ? "" : " ✗").Append(" | ");
            pk.Append(F(t, "F4")).Append(i < sent.Count - 1 ? "," : "");
        }
        Line(inRec >= 2 && good == inRec && sent.Count == LaunchRiser.Sent,
             "the riser (Reverse Cymbal on the song bus) swells and PEAKS on each launch's landing beat — within ±30 ms in the recording — then stops dead; nothing else sounds before its note-on",
             inRec + " landings in a7_riser_alone.wav, " + good + " right, worst " + F(1000.0 * worst, "F1") + " ms — " + d);
        Info("a7_riser_alone.wav landings (s from its first frame, for scratchpad/v7/a/riser_check.py): " + pk);

        // ---- 2. the crash alone: the riser not sent, only the drum slot unmuted (the fixture has no drums: the landing crash is the only hit)
        MuteVoices(true, true);
        LaunchRiser.SoundRiser = false;
        yield return Frames(3);
        LaunchRiser.ResetLog();
        yield return PlayRecorded("a7_crash_alone.wav", 3.4f, 0.0, rec0);
        LaunchRiser.SoundRiser = true;
        MuteVoices(false, false);
        var plans = new List<LaunchRiser.Plan>(LaunchRiser.Log);
        x = ReadWav(Cap("a7_crash_alone.wav"), out sr);
        int ms = Math.Max(1, sr / 1000); int nms = x.Length / 2 / ms; var e1 = new double[nms];
        for (int k = 0; k < nms; k++) { double s = 0; for (int i = k * ms; i < (k + 1) * ms; i++) s += x[2 * i] * (double)x[2 * i] + x[2 * i + 1] * (double)x[2 * i + 1]; e1[k] = Math.Sqrt(s / (2.0 * ms)); }
        string crash = "no landing in the recording"; bool crashOk = false;
        foreach (var p in plans)
        {
            double t = p.landingDsp - rec0[0];
            if (t < 0.2 || t > nms / 1000.0 - 0.3) continue;
            int onset = -1;
            for (int k = Math.Max(0, (int)((t - 0.3) * 1000)); k < Math.Min(nms, (int)((t + 0.2) * 1000)); k++) if (Db(e1[k]) > -50.0) { onset = k; break; }
            double quiet = 0; for (int k = Math.Max(0, (int)((t - 1.0) * 1000)); k < Math.Max(0, (int)((t - 0.02) * 1000)); k++) quiet = Math.Max(quiet, e1[k]);
            double loud = 0; for (int k = Math.Max(0, (int)(t * 1000)); k < Math.Min(nms, (int)((t + 0.15) * 1000)); k++) loud = Math.Max(loud, e1[k]);
            double err = onset >= 0 ? onset / 1000.0 - t : 9.0;
            crashOk = !p.sent && p.crashSent && Math.Abs(err) <= 0.006 && Db(quiet) < -60.0 && Db(loud) > -40.0;
            crash = "landing at " + F(t, "F3") + " s: onset " + (onset >= 0 ? F(onset / 1000.0, "F3") + " s (" + F(1000.0 * err, "+0.0;-0.0") + " ms)" : "none") + ", " + F(Db(loud), "F1") + " dBFS, silent before (" + F(Db(quiet), "F0") + " dBFS), riser note sent " + p.sent + ", crash sent " + p.crashSent;
            break;
        }
        Line(crashOk && LaunchRiser.Crashes >= 1, "a soft crash lands on the landing beat (VoiceRules.ScheduleLandingCrash, velocity " + LaunchRiser.CrashVelocity + ")", crash + " (a7_crash_alone.wav)");

        // ---- 3. the full mix (evidence to listen to): the song, the riser into each landing and the crash on it
        LaunchRiser.ResetLog();
        yield return Frames(2);
        yield return PlayRecorded("a7_riser_mix.wav", 9.6f, 0.0, rec0);
        sent = SentPlans();
        x = ReadWav(Cap("a7_riser_mix.wav"), out sr);
        env = Env10(x, sr);
        var mix = new StringBuilder();
        foreach (var p in sent)
        {
            double t = p.landingDsp - rec0[0];
            if (t < 0.2 || t > env.Length / 1000.0 - 0.35) continue;
            mix.Append("landing ").Append(F(t, "F3")).Append(" s: the last 100 ms before it ").Append(F(Db(MaxIn(env, t - 0.1, t)), "F1")).Append(" dBFS vs a second before ").Append(F(Db(MaxIn(env, t - 1.1, t - 1.0)), "F1")).Append(" dBFS | ");
        }
        Info("a7_riser_mix.wav (the fixture with its launches, everything audible): " + sent.Count + " risers, " + LaunchRiser.Crashes + " crashes — " + mix);
        Line(realLate - late0 == 0, "no late note while the risers and crashes play", "late +" + (realLate - late0) + " (stall-late so far " + stallLate + ")");
        noFlingWatch = false;
        yield return null;
        Line(nfFrames > 300 && nfFlingFrames == 0 && nfOffFrames == 0,
             "§21 — the launch is only its sound: through the three launch recordings (risers + crashes heard) no cube is flung and the launching grids' cubes never leave their platform",
             nfFrames + " playing frames sampled: flinging in " + nfFlingFrames + ", off the platform (> 0.75 u) in " + nfOffFrames + ", the farthest " + F(nfWorst) + " u past an edge" + (nfWhy != null ? " — first: " + nfWhy : ""));
        LoadFixture(); yield return Frames(2);
    }

    // ================================================================== timed: once per launch per pass, only when audible, never late
    static IEnumerator RiserRules()
    {
        int late0 = realLate;
        // ---- once per launch per pass: a focus loop over columns 0-1 (island 0 launches onto island 1) played three times
        LoadState(LaunchSong(0));
        yield return Frames(4);
        float passBeats = SM.ColumnStart(2);
        GlobalClock.SetRegion(0.0, passBeats);
        LaunchRiser.ResetLog();
        GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        double passSec = passBeats / GlobalClock.BeatsPerSecond;
        yield return Wait((float)(3.0 * passSec + 0.35));
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        var sent = SentPlans();
        bool spaced = sent.Count >= 3, distinct = true;
        for (int i = 1; i < sent.Count; i++) { if (Math.Abs(sent[i].landingDsp - sent[i - 1].landingDsp - passSec) > 0.002) spaced = false; for (int j = 0; j < i; j++) if (Math.Abs(sent[i].landingDsp - sent[j].landingDsp) < 1e-3) distinct = false; }
        var keys = new StringBuilder(); foreach (var p in sent) keys.Append(p.key).Append(' ');
        Line(spaced && distinct && LaunchRiser.Crashes + LaunchRiser.CrashShared >= sent.Count && LaunchRiser.Skipped == 0,
             "once per launch per pass: a focus loop over the launching column and its target played three times gives one riser + one crash per pass, one pass apart",
             sent.Count + " risers (keys " + keys + "), " + LaunchRiser.Crashes + " crashes, landings " + F(passSec, "F3") + " s apart: " + spaced + ", distinct " + distinct + ", skipped " + LaunchRiser.Skipped);

        // ---- only when audible: asleep, the island's groups muted, launch off → no riser
        var audible = new StringBuilder(); bool none = true;
        for (int mode = 0; mode < 3; mode++)
        {
            LoadState(LaunchSong(0));
            yield return Frames(3);
            var kb = SM.Islands[0];
            var groups = new HashSet<int>();
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb) groups.Add(c.instrument);
            if (mode == 0) SM.SetSleep(0, true);
            else if (mode == 1) foreach (int g in groups) Instruments.SetMuted(g, true);
            else SM.SetLaunch(0, false);
            yield return Frames(3);
            kb = SM.Islands[0];
            bool aud = LaunchRiser.Audible(kb);
            LaunchRiser.ResetLog();
            GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
            yield return Wait((float)(SM.TurnEnd(kb) / GlobalClock.BeatsPerSecond) + 0.4f);
            GlobalClock.Stop();
            int plansN = LaunchRiser.Log.Count;
            if (mode == 1) foreach (int g in groups) Instruments.SetMuted(g, false);
            string[] names = { "asleep", "its groups muted", "launch off" };
            audible.Append(names[mode]).Append(": audible ").Append(aud).Append(", plans ").Append(plansN).Append(mode < 2 ? " | " : "");
            if (plansN != 0 || (mode < 2 && aud)) none = false;
        }
        Instruments.PushToSynth();
        Line(none, "only when audible: an asleep island, one whose cubes' groups are muted, or launch off gets no riser (and no crash)", audible.ToString());

        // ---- never late: a seek into the swell cancels it; a shorter swell (or none) fits what is left
        LoadState(LaunchSong(0));
        yield return Frames(3);
        MuteVoices(true, false);
        var kb0 = SM.Islands[0];
        float land = SM.TurnEnd(kb0);
        LaunchRiser.ResetLog();
        var rec0 = new double[1];
        // play from beat 0 and seek (0.75 s in, the swell sounding) to 0.9 s before the landing
        GlobalClock.Stop(); GlobalClock.Seek(0);
        yield return Frames(2);
        Synth.StartRecording(3.2f);
        yield return Frames(2);
        GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(0.75f);
        int beforeSeek = SentPlans().Count;
        double seekBeat = land - 0.9 * GlobalClock.BeatsPerSecond;
        SeekPlaying(seekBeat); AudioCube.ScheduleAllNow();
        float tw = Time.realtimeSinceStartup;
        while (!Synth.RecordingDone && Time.realtimeSinceStartup - tw < 6f) yield return null;
        GlobalClock.Stop();
        Synth.SaveRecording(Cap("a7_riser_seek.wav"));
        rec0[0] = Synth.RecordingStartDsp;
        MuteVoices(false, false);
        var log = new List<LaunchRiser.Plan>(LaunchRiser.Log);
        LaunchRiser.Plan first = log.Count > 0 ? log[0] : new LaunchRiser.Plan(), second = log.Count > 1 ? log[log.Count - 1] : new LaunchRiser.Plan();
        int sr; var x = ReadWav(Cap("a7_riser_seek.wav"), out sr);
        var env = Env10(x, sr);
        double t2 = second.landingDsp - rec0[0], lvl, tp = PeakIn(env, Math.Max(0.0, t2 - 0.9), t2 + 0.3, out lvl);
        bool shorter = log.Count >= 2 && first.cancelled && second.sent && !second.cancelled && second.key > first.key && second.peak <= 0.9 + 1e-6 && second.onDsp >= second.plannedAt + LaunchRiser.Margin - 1e-6;
        Line(shorter && Math.Abs(tp - t2) <= 0.030 && realLate - late0 == 0,
             "never late: a seek into a sounding swell cancels it and a shorter (faster) swell that fits the time left still peaks on the landing; no late note",
             "planned key " + first.key + " (" + F(first.peak, "F3") + " s, sent " + (beforeSeek > 0) + ", cancelled " + first.cancelled + ") → after the seek key " + second.key + " (" + F(second.peak, "F3") + " s, queued " + F(second.onDsp - second.plannedAt, "F3") + " s ahead); a7_riser_seek.wav peak "
             + F(tp, "F3") + " s vs landing " + F(t2, "F3") + " s (" + F(1000.0 * (tp - t2), "+0;-0") + " ms); plans " + log.Count + ", late +" + (realLate - late0) + " (a seek's catch-up " + seekLate + ")");
        LoadFixture(); yield return Frames(2);
    }

    // ================================================================== timed: the performance numbers
    /// <summary><paramref name="st"/> + four octave layers (+1, −1, +2, −2) of pitched cubes and two harmony cubes (a 5th / 4th above, a 4th / 5th below,
    /// as bends) — "a dense song with 4 octave layers + 2 harmony cubes".</summary>
    static SongState WithIdeas(SongState st)
    {
        var src = new List<int>();
        for (int i = 0; i < st.cubes.Length; i++) if (st.cubes[i].moon < 0 && st.cubes[i].instrument != 9 && st.cubes[i].xs != null && st.cubes[i].xs.Length > 0) src.Add(i);
        if (src.Count == 0) return st;
        var list = new List<CubeState>(st.cubes);
        int[] layers = { 1, -1, 2, -2 };
        for (int i = 0; i < 4; i++) { var c = CubeState.Clone(st.cubes[src[i % src.Count]]); c.layer = layers[i]; c.id = 9200 + i; c.echoOf = st.cubes[src[i % src.Count]].id; c.seed = 7001 + i; list.Add(c); }
        for (int i = 0; i < 2; i++)
        {
            var c = CubeState.Clone(st.cubes[src[(i + 1) % src.Count]]);
            int n = c.xs.Length; c.bend = new int[n];
            for (int j = 0; j < n; j++) c.bend[j] = i == 0 ? (j % 2 == 0 ? 7 : 5) : (j % 2 == 0 ? -5 : -7);
            c.id = 9210 + i; c.echoOf = st.cubes[src[(i + 1) % src.Count]].id; c.seed = 7101 + i;
            list.Add(c);
        }
        st.cubes = list.ToArray();
        return st;
    }

    struct Perf { public float avg, peak, frameMs, maxFrame; public int voices, songVoices, blocks, late, stallLate, cubes, tries; public double avgVoices; }

    static IEnumerator Measure(SongState st, Perf[] outp, int at)
    {
        V6ChecksA.Prepare(); GlobalClock.Stop();
        LoadState(st);
        yield return Frames(6);
        yield return Wait(0.8f);
        int late0 = realLate, stall0 = stallLate;
        GlobalClock.Stop(); GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(1.5f);
        Synth.ResetPerf();
        float t0 = Time.realtimeSinceStartup, maxDt = 0f; double vSum = 0, dtSum = 0; int n = 0;
        while (Time.realtimeSinceStartup - t0 < 6f) { yield return null; vSum += Synth.ActiveVoices; dtSum += Time.unscaledDeltaTime; maxDt = Mathf.Max(maxDt, Time.unscaledDeltaTime); n++; }
        yield return Frames(3);
        outp[at] = new Perf
        {
            avg = Synth.PerfAvgCpu, peak = Synth.PerfMaxCpu, voices = Synth.PerfMaxVoices, songVoices = Synth.PerfMaxSongVoices, blocks = Synth.PerfBlocks,
            avgVoices = vSum / Math.Max(1, n), frameMs = (float)(1000.0 * dtSum / Math.Max(1, n)), late = realLate - late0, stallLate = stallLate - stall0, maxFrame = maxDt,
            cubes = SequenceMaster.Cubes.Count, tries = outp[at].tries + 1
        };
        GlobalClock.Stop();
        yield return Frames(3);
    }

    static IEnumerator PerformanceTable()
    {
        var p = new Perf[4]; var q = new Perf[4];
        // a measurement a main-thread stall hit (late notes counted apart) is measured again, up to twice
        for (int i = 0; i < 4; i++) { do { yield return Measure(i < 2 ? (i == 0 ? V6ChecksA.PerfSong(true) : WithIdeas(V6ChecksA.PerfSong(true))) : (i == 2 ? Fixture() : WithIdeas(Fixture())), p, i); } while (p[i].stallLate > 0 && p[i].tries < 3); }
        for (int i = 0; i < 2; i++) { do { yield return Measure(i == 0 ? V6ChecksA.PerfSong(true) : WithIdeas(V6ChecksA.PerfSong(true)), q, i); } while (q[i].stallLate > 0 && q[i].tries < 3); }
        int len, num; AudioSettings.GetDSPBufferSize(out len, out num);
        Info("PERFORMANCE (SPEC v7 §4.5) — 6 s after a 1.5 s warm-up, DSP buffer " + len + " × " + num + " @ " + AudioSettings.outputSampleRate + " Hz, the Unity editor:");
        Info("  song                                         | cubes | audio cpu avg | audio cpu peak | synth voices peak (song bus; cap 64) | song voices avg | frame ms | late");
        string[] names = { "(a) dense: 12 cubes of 16ths (v6's)          ", "(b) (a) + 4 octave layers + 2 harmony cubes  ", "(c) the fixture (13 cubes, 121 bpm)          ", "(d) (c) + 4 octave layers + 2 harmony cubes  " };
        for (int i = 0; i < 4; i++)
        {
            var rs = i < 2 ? new[] { p[i], q[i] } : new[] { p[i] };
            foreach (var r in rs)
                Info("  " + names[i] + "| " + r.cubes + "    | " + F(100 * r.avg, "F1") + " %        | " + F(100 * r.peak, "F1") + " %         | " + r.voices + " (" + r.songVoices + ")                            | " + F(r.avgVoices, "F1") + "            | " + F(r.frameMs, "F1") + " (max " + F(1000 * r.maxFrame, "F0") + ") | " + r.late
                     + (r.tries > 1 ? " (measured " + r.tries + "×: a main-thread stall made notes late)" : "") + (r.stallLate > 0 ? " +" + r.stallLate + " in a stall" : ""));
        }
        bool ran = true; for (int i = 0; i < 4; i++) if (p[i].blocks < 50) ran = false;
        float bPeak = Mathf.Max(p[1].peak, q[1].peak), dPeak = p[3].peak;
        int lates = 0; for (int i = 0; i < 4; i++) lates += p[i].late; lates += q[0].late + q[1].late;
        Line(ran && bPeak < 0.5f && dPeak < 0.5f && lates == 0,
             "performance: a dense song with 4 octave layers + 2 harmony cubes plays with the audio thread well inside its budget and no late notes",
             "(a) " + F(100 * (p[0].avg + q[0].avg) / 2, "F1") + " % → (b) " + F(100 * (p[1].avg + q[1].avg) / 2, "F1") + " % avg (peak " + F(100 * bPeak, "F1") + " %); (c) " + F(100 * p[2].avg, "F1") + " % → (d) " + F(100 * p[3].avg, "F1") + " % avg (peak " + F(100 * dPeak, "F1") + " %) of one core; voices (a) "
             + p[0].voices + " (b) " + p[1].voices + " (c) " + p[2].voices + " (d) " + p[3].voices + "; frame (c) " + F(p[2].frameMs, "F1") + " → (d) " + F(p[3].frameMs, "F1") + " ms; late +" + lates + " (outside stalls)");
        LoadFixture(); yield return Frames(2);
    }
}
