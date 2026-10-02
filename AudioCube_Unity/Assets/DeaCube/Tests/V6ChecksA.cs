using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v6 §4.4 package A checks (Play mode): instrument groups of voices and louder presses. <see cref="RunAll"/> starts them (a coroutine);
/// poll <see cref="Done"/> or Captures/a6_report.txt. Static: the voice table (10 groups of 5-9 voices, captions), every voice's preset resolves in
/// the loaded SoundFont (name + bank + patch), channels unique on both buses, drum kits on percussion channels, voice 0 = the v5 slot bit for bit
/// (slot, channel, program, levels, register, trim 1, the whole v5 fold), the loudness trims programmed, group volume / mute / mix reach every
/// voice slot, the role rules follow the group, the keyboard fold keeps a melody's octave, a cube sounds through its voice, a carried window is
/// stamped with the window's island, a keyboard island's keys play their own pitch (when K's keyboard exists), the press note, the harmonic check.
/// Timed, recorded with stems (Captures/a6_*.wav): every voice sounds on both buses; group volume / mute are heard on every voice; a press over a
/// dense gallery song is at least 6 dB over the song alone (RMS 50-300 ms after its onset) with the song ducked -15 dB and the preview bus +4 dB,
/// never clipping; a hover audition ducks the song -10 dB; the performance table (the v5 sounds vs 12 different voices in a dense song: CPU, voices,
/// frame time; the SoundFont's load time and memory; the synthesizer set-up v5 vs v6); Synth late / errors 0. <see cref="RunOlder"/> runs A's older
/// suites (V2Checks RunAll / RunWorld / RunIntegration, WpAChecks M1 + M2 + timed) with PathManager.AutoHand = true (Captures/a6_older_report.txt).
/// The fixture and a gallery song are loaded (never the user's save); the autosave and the backups are put back as they were.
/// </summary>
public static class V6ChecksA
{
    public static bool Done = true;
    public static string Report = "";
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "a6_report.txt");
    /// <summary>The synth owner of the test tones (far from the cubes' instance-id range).</summary>
    const int ToneOwner = 1900000061;
    /// <summary>The gallery song the press is measured over (a dense one: lead, bass, strings, piano, pluck and the drum Moon, 154 bpm).</summary>
    public const string PressSong = "petal-storm";

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
    static IEnumerator UntilDsp(double dsp, float timeout = 30f)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Synth.DspNow < dsp && Time.realtimeSinceStartup - t0 < timeout) yield return null;
    }
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);

    // ---- v7 (A): main-thread stalls — another package's file import or an MCP call can freeze the editor's main thread longer than the 0.22 s
    // lookahead (the v7 build has eight engineers editing during Play sessions); the notes due meanwhile are late by design and counted apart, like
    // the protocol's ScreenCapture stall: late notes within a second of a frame > 0.12 s, or a burst of ≥ 3 at once. Anything else late fails.
    static int stallLate, realLate, stalls, lateSeen, watchGen;
    static float longestStall, lastLong = -99f;
    static bool watching;
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
                if (now - lastLong < 1.0f || n >= 3) stallLate += n;
                else { realLate += n; if (realLateLog.Length < 600) realLateLog.Append(Synth.LastLate).Append(" (frame ").Append((1000f * dt).ToString("F0")).Append(" ms); "); }
            }
        }
    }

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray / presentation closed, the hand empty).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        if (PathManager.I != null) PathManager.I.PutDown();
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);   // the real mouse neither hovers nor clicks (no stray auditions)
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }

    static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void Restore()
    {
        try
        {
            CubeInspector.CloseImmediate();
            if (Presenter.Active) Presenter.Exit();
            FocusLoop.Dismiss();
            GlobalClock.Stop();
            if (PathManager.I != null) PathManager.I.PutDown();
            PathManager.AutoHand = false;
            PathManager.SimOnly = false;
        }
        catch (Exception e) { Debug.LogWarning("V6ChecksA restore: " + e.Message); }
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
    /// <summary>RMS of an interleaved stereo buffer between <paramref name="t0"/> and <paramref name="t1"/> seconds after its first frame.</summary>
    static double Rms(float[] x, int sr, double t0, double t1)
    {
        int frames = x.Length / 2;
        int a = Mathf.Clamp((int)Math.Round(t0 * sr), 0, frames), b = Mathf.Clamp((int)Math.Round(t1 * sr), 0, frames);
        double s = 0; int n = 0;
        for (int i = a; i < b; i++) { s += x[2 * i] * (double)x[2 * i] + x[2 * i + 1] * (double)x[2 * i + 1]; n += 2; }
        return n > 0 ? Math.Sqrt(s / n) : 0.0;
    }
    /// <summary>Min / max of one channel (0 left, 1 right) between t0 and t1 seconds.</summary>
    static void MinMax(float[] x, int sr, int ch, double t0, double t1, out float mn, out float mx)
    {
        int frames = x.Length / 2;
        int a = Mathf.Clamp((int)Math.Round(t0 * sr), 0, frames), b = Mathf.Clamp((int)Math.Round(t1 * sr), 0, frames);
        mn = float.MaxValue; mx = float.MinValue;
        for (int i = a; i < b; i++) { float v = x[2 * i + ch]; if (v < mn) mn = v; if (v > mx) mx = v; }
        if (a >= b) { mn = mx = 0f; }
    }
    static float Peak(float[] x) { float p = 0f; for (int i = 0; i < x.Length; i++) { float a = Math.Abs(x[i]); if (a > p) p = a; } return p; }

    // ================================================================== the v5 table (voice 0 must match it bit for bit)
    //                                     bank patch low high oct vmin vmax channel
    static readonly int[,] V5 =
    {
        { 0,   4, 41, 88,  0, 40, 110, 0 }, { 0,  24, 40, 84,  0, 40, 110, 1 }, { 0,  89, 48, 79,  0, 40, 105, 2 }, { 0,  80, 55, 91,  0, 45, 110, 3 },
        { 0,  33, 28, 55, -1, 45, 112, 4 }, { 0,  11, 60, 96,  0, 40, 110, 5 }, { 0,  48, 43, 88,  0, 40, 105, 6 }, { 0,  52, 48, 84,  0, 40, 105, 7 },
        { 0,   0, 36, 96,  0, 40, 112, 8 }, { 128, 0, 27, 87,  0, 50, 115, 9 },
    };
    //                                      gain   rev    cho    rel
    static readonly float[,] V5f =
    {
        { 1.00f, 0.35f, 0.30f, 0.30f }, { 1.00f, 0.40f, 0.10f, 0.25f }, { 0.90f, 0.60f, 0.30f, 0.60f }, { 0.70f, 0.30f, 0.15f, 0.15f },
        { 1.00f, 0.15f, 0.00f, 0.15f }, { 0.90f, 0.60f, 0.15f, 0.50f }, { 0.90f, 0.60f, 0.20f, 0.50f }, { 0.90f, 0.60f, 0.20f, 0.50f },
        { 1.00f, 0.35f, 0.00f, 0.30f }, { 1.00f, 0.15f, 0.00f, 0.20f },
    };
    static readonly string[] V5Preset = { "Tine Electric Piano", "Nylon Guitar", "Warm Pad", "Square Lead", "Finger Bass", "Vibraphone", "Fast Strings", "Concert Choir", "Grand Piano", "Standard 1" };

    /// <summary>v5's VoiceRules.Fold, verbatim (slots 0..9: 4 = bass, 5 = bells) — the v6 fold must give the same note for every voice 0.</summary>
    static int V5Fold(int slot, int midi, int reg)
    {
        int low0 = V5[slot, 2], high0 = V5[slot, 3], oct = V5[slot, 4]; bool drums = V5[slot, 0] == 128;
        if (reg == 0 || drums)
        {
            if (drums) return Mathf.Clamp(midi, 0, 127);
            midi += 12 * oct;
            int lo = Mathf.Min(low0, high0), hi = Mathf.Max(low0, high0);
            while (midi < lo) midi += 12;
            while (midi > hi) midi -= 12;
            if (midi < lo) midi = lo;
            midi = Mathf.Clamp(midi, 0, 127);
            if (slot != 4) while (midi < 48) midi += 12;
            if (slot != 5) while (midi > 96) midi -= 12;
            return Mathf.Clamp(midi, 0, 127);
        }
        reg = Mathf.Clamp(reg, -2, 2);
        midi += 12 * oct;
        int low = Mathf.Min(low0, high0) + 12 * reg, high = Mathf.Max(low0, high0) + 12 * reg;
        while (low < 21) { low += 12; high += 12; }
        while (high > 108) { low -= 12; high -= 12; }
        while (midi < low) midi += 12;
        while (midi > high) midi -= 12;
        if (midi < low) midi = low;
        int floor = 48 + 12 * reg, ceil = 96 + 12 * reg;
        if (slot != 4) while (midi < floor && midi + 12 <= 108) midi += 12;
        if (slot != 5) while (midi > ceil && midi - 12 >= 21) midi -= 12;
        return Mathf.Clamp(midi, 0, 127);
    }

    // ================================================================== entry points
    /// <summary>Starts every A check (static + timed); poll <see cref="Done"/> / Captures/a6_report.txt.</summary>
    public static string RunAll(bool captures = false)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; Progress = "start";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(Routine());
        return "started";
    }

    static IEnumerator Routine()
    {
        sb = new StringBuilder(); pass = 0; fail = 0;
        sb.Append("V6ChecksA ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        var saves = V3Fixes.SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors, lateLog0 = Synth.LateLogCount;
        bool wasAutoHand = PathManager.AutoHand;
        PathManager.AutoHand = false;
        Prepare(); LoadFixture();
        stallLate = 0; realLate = 0; stalls = 0; longestStall = 0f; realLateLog.Length = 0;
        SequenceMaster.I.StartCoroutine(StallWatch());
        yield return Frames(3);

        Progress = "static";
        Guard("table", Table);
        Guard("presets", Presets);
        Guard("channels", Channels);
        Guard("voice 0", VoiceZero);
        Guard("group controls", GroupControls);
        Guard("role rules", RoleRules);
        Guard("keyboard fold", KeyboardFold);
        Guard("resolve", ResolveVoice);
        Guard("harmonic check", HarmonicUnit);
        Guard("press event", PressUnit);
        yield return Frames(6);
        // a press over silence gets the base boost: wait until the song bus's held level (1.5 s release) has fallen away (the title music)
        { float tq = Time.realtimeSinceStartup; while (Synth.SongLevelDb > -70f && Time.realtimeSinceStartup - tq < 8f) yield return null; }
        Guard("press envelope", PressEnvelope);
        yield return Keyboard();

        Progress = "voices";
        yield return AllVoicesSound();
        Progress = "volume";
        yield return GroupVolumeHeard();
        Progress = "press";
        yield return PressOverSong();
        Progress = "perf";
        yield return PerformanceTable();

        // ---- the synth counters over the whole run
        yield return Frames(3);
        watching = false;
        int dl = Synth.LateEvents - late0, de = Synth.Errors - err0;
        Line(realLate == 0 && de == 0, "Synth late 0 / errors 0 over every A check (v7: notes made late by a main-thread stall longer than the lookahead counted apart)",
             "late +" + dl + " (" + realLate + " real, " + stallLate + " in " + stalls + " main-thread stall(s) of up to " + F(longestStall, "F2") + " s) errors +" + de
             + (realLate > 0 ? " — real: " + realLateLog : "") + (dl > 0 ? " | log: " + Synth.LateLog(lateLog0) : "") + " | " + Synth.Stats());

        // ---- restore
        Progress = "restore";
        Prepare(); LoadFixture();
        Instruments.PushToSynth();
        PathManager.AutoHand = wasAutoHand;
        Restore();
        V3Fixes.RestoreSaves(saves);
        sb.Insert(0, "V6ChecksA: " + pass + " pass, " + fail + " fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Progress = "done";
        Done = true;
    }

    // ================================================================== static checks
    static void Table()
    {
        int groups = SynthBank.Groups, total = 0; var bad = new StringBuilder(); var counts = new StringBuilder();
        for (int g = 0; g < groups; g++)
        {
            int n = Instruments.VoiceCount(g); total += n;
            counts.Append(Instruments.GroupWords[g]).Append(' ').Append(n).Append(g < groups - 1 ? ", " : "");
            if (n < 5 || n > 9) bad.Append("group " + g + " has " + n + " voices; ");
            var seen = new HashSet<string>();
            for (int v = 0; v < n; v++)
            {
                int s = Instruments.SlotOf(g, v);
                var d = SynthBank.Def(s);
                if (d.group != g || d.voice != v) bad.Append("slot " + s + " says group " + d.group + " voice " + d.voice + " for " + g + "/" + v + "; ");
                string cap = Instruments.VoiceName(g, v);
                if (string.IsNullOrEmpty(cap) || cap != cap.ToLowerInvariant() || cap.Trim().Split(' ').Length > 2) bad.Append("caption '" + cap + "'; ");
                if (!seen.Add(cap)) bad.Append("caption '" + cap + "' twice in group " + g + "; ");
                if (d.drums != (g == 9)) bad.Append("slot " + s + " drums flag; ");
            }
            if (Instruments.SlotOf(g, n) != Instruments.SlotOf(g, 0) || Instruments.SlotOf(g, -1) != Instruments.SlotOf(g, 0)) bad.Append("group " + g + ": a voice out of range is not voice 0; ");
        }
        Line(total == SynthBank.Slots && SynthBank.Defs.Length == SynthBank.Slots && groups == Instruments.Count && bad.Length == 0,
             "the voice table: " + groups + " groups (v9: + fx) of 5-9 voices, one slot each, lowercase captions of one or two words, voice 0 = the group's slot",
             total + " voices (" + counts + ")" + (bad.Length > 0 ? " — " + bad : ""));
        var cap5 = new StringBuilder();
        for (int g = 0; g < groups; g++) { cap5.Append(Instruments.GroupWords[g]).Append(": "); for (int v = 0; v < Instruments.VoiceCount(g); v++) cap5.Append(v > 0 ? ", " : "").Append(Instruments.VoiceName(g, v)); cap5.Append(g < groups - 1 ? " | " : ""); }
        Info("voices — " + cap5);
    }

    static void Presets()
    {
        var bad = new StringBuilder(); var names = Synth.PresetNames(); int ok = 0;
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            var d = SynthBank.Def(s);
            string got = Synth.PresetOf(d.bank, d.patch);
            bool okName = got == d.preset && s < names.Length && names[s].Contains(d.preset) && names[s].Contains("(" + d.bank + ":" + d.patch + ")");
            if (okName) ok++; else bad.Append(d.name + " " + d.bank + ":" + d.patch + " wants '" + d.preset + "' got '" + got + "'; ");
        }
        Line(bad.Length == 0 && Synth.PresetsMissing == 0, "every voice's preset resolves in the loaded SoundFont (name + bank + patch), on the synth's slot",
             ok + "/" + SynthBank.Slots + " resolve, missing at load " + Synth.PresetsMissing + (bad.Length > 0 ? " — " + bad : ""));
    }

    static void Channels()
    {
        var used = new HashSet<int>(); var bad = new StringBuilder(); int kits = 0;
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            int ch = SynthBank.ChannelOf(s);
            if (!used.Add(ch)) bad.Append("channel " + ch + " twice; ");
            if (ch < 0 || ch >= Synth.ChannelCount || ch >= Synth.PreviewChannelCount) bad.Append("channel " + ch + " outside the synths' " + Synth.ChannelCount + "/" + Synth.PreviewChannelCount + "; ");
            bool perc = Synth.IsPercussionChannel(ch);
            if (SynthBank.Def(s).drums) { kits++; if (!perc) bad.Append(SynthBank.Def(s).name + " (ch " + ch + ") not percussion; "); }
            else if (perc) bad.Append(SynthBank.Def(s).name + " (ch " + ch + ") is percussion; ");
            float trim = Synth.ChannelTrim(ch), want = SynthBank.Def(s).trim;
            if (Mathf.Abs(trim - want) > 1e-6f) bad.Append(SynthBank.Def(s).name + " trim " + F(trim) + " != " + F(want) + "; ");
        }
        Line(bad.Length == 0 && used.Count == SynthBank.Slots && Synth.ChannelCount == SynthBank.ChannelCount && Synth.PreviewChannelCount == SynthBank.ChannelCount,
             "channels: one per voice on both buses (song + preview), drum kits on percussion channels, every voice's loudness trim programmed",
             used.Count + " unique channels, synths " + Synth.ChannelCount + " / " + Synth.PreviewChannelCount + " channels, " + kits + " kits on percussion channels" + (bad.Length > 0 ? " — " + bad : ""));
        var trims = new StringBuilder();
        for (int s = SynthBank.Groups; s < SynthBank.Slots; s++) trims.Append(SynthBank.Def(s).caption).Append(' ').Append(F(Db(SynthBank.Def(s).trim), "+0.0;-0.0")).Append(s < SynthBank.Slots - 1 ? ", " : "");
        Info("loudness trims vs voice 0 (dB): " + trims);
    }

    static void VoiceZero()
    {
        var bad = new StringBuilder(); int folds = 0, foldBad = 0; string firstFold = null;
        for (int g = 0; g < 10; g++)
        {
            int s = Instruments.SlotOf(g, 0);
            var d = SynthBank.Def(s);
            if (s != g || Instruments.SlotOf(g) != g || Instruments.SynthSlot[g] != g) bad.Append("group " + g + " voice 0 on slot " + s + "; ");
            if (d.bank != V5[g, 0] || d.patch != V5[g, 1] || d.lowMidi != V5[g, 2] || d.highMidi != V5[g, 3] || d.octaveShift != V5[g, 4] || d.velocityMin != V5[g, 5] || d.velocityMax != V5[g, 6])
                bad.Append("slot " + g + " program / register / velocity differ; ");
            if (SynthBank.ChannelOf(g) != V5[g, 7]) bad.Append("slot " + g + " channel " + SynthBank.ChannelOf(g) + "; ");
            if (d.gain != V5f[g, 0] || d.reverb != V5f[g, 1] || d.chorus != V5f[g, 2] || d.releaseSec != V5f[g, 3]) bad.Append("slot " + g + " levels / sends differ; ");
            if (d.trim != 1f || Synth.ChannelTrim(SynthBank.ChannelOf(g)) != 1f) bad.Append("slot " + g + " trim " + d.trim + "; ");
            if (d.preset != V5Preset[g] || Synth.PresetOf(d.bank, d.patch) != V5Preset[g]) bad.Append("slot " + g + " preset; ");
            for (int reg = -2; reg <= 2; reg++)
                for (int m = 0; m < 128; m++)
                {
                    folds++;
                    int a = VoiceRules.Fold(s, m, reg), b = V5Fold(g, m, reg);
                    if (a != b) { foldBad++; if (firstFold == null) firstFold = "slot " + g + " midi " + m + " reg " + reg + ": " + a + " vs v5 " + b; }
                }
            if (SynthBank.Velocity(s, 0.8f, 0.3f) != Mathf.Clamp(Mathf.RoundToInt(V5[g, 5] + (V5[g, 6] - V5[g, 5]) * Mathf.Pow(0.8f, 0.7f) + 0.3f * (127f - (V5[g, 5] + (V5[g, 6] - V5[g, 5]) * Mathf.Pow(0.8f, 0.7f)))), 1, 127))
                bad.Append("slot " + g + " velocity curve; ");
        }
        if (SynthBank.DrumSlot != 9) bad.Append("DrumSlot " + SynthBank.DrumSlot + "; ");
        Line(bad.Length == 0 && foldBad == 0, "voice 0 of every group = the v5 slot bit for bit (slot, channel, program, register, levels, sends, velocity, trim 1, the whole fold)",
             folds + " folds compared with v5's, " + foldBad + " differ" + (firstFold != null ? " (first: " + firstFold + ")" : "") + (bad.Length > 0 ? " — " + bad : ""));
    }

    static void GroupControls()
    {
        var vol0 = (float[])Instruments.Volume.Clone(); var mute0 = (bool[])Instruments.Muted.Clone();
        var bad = new StringBuilder();
        try
        {
            Instruments.SetVolume(3, 0.37f);
            foreach (int s in Instruments.SlotsOf(3)) if (Mathf.Abs(Synth.SlotGain(s) - 0.37f) > 1e-5f) bad.Append("lead slot " + s + " gain " + F(Synth.SlotGain(s)) + "; ");
            foreach (int s in Instruments.SlotsOf(2)) if (Mathf.Abs(Synth.SlotGain(s) - Instruments.Volume[2]) > 1e-5f) bad.Append("pad slot " + s + " gain moved; ");
            Instruments.SetMuted(6, true);
            foreach (int s in Instruments.SlotsOf(6)) if (!Synth.SlotMuted(s)) bad.Append("strings slot " + s + " not muted; ");
            foreach (int s in Instruments.SlotsOf(7)) if (Synth.SlotMuted(s) != Instruments.Muted[7]) bad.Append("choir slot " + s + " mute moved; ");
            Instruments.SetMuted(6, mute0[6]);
            foreach (int s in Instruments.SlotsOf(6)) if (Synth.SlotMuted(s) != mute0[6]) bad.Append("strings slot " + s + " not restored; ");
            // a whole mix (the gallery preview's path) onto every voice slot
            var vols = new float[Instruments.Count]; var mutes = new bool[Instruments.Count];   // v9: every group (+ fx)
            for (int g = 0; g < Instruments.Count; g++) { vols[g] = 0.1f + 0.08f * g; mutes[g] = g == 4; }
            Instruments.PushMix(vols, mutes, 1f);
            for (int s = 0; s < SynthBank.Slots; s++)
            {
                int g = SynthBank.GroupOf(s);
                if (Mathf.Abs(Synth.SlotGain(s) - vols[g]) > 1e-5f || Synth.SlotMuted(s) != mutes[g]) { bad.Append("PushMix missed slot " + s + "; "); break; }
            }
        }
        finally { Instruments.Restore(vol0, mute0); }
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            int g = SynthBank.GroupOf(s);
            if (Mathf.Abs(Synth.SlotGain(s) - Instruments.Volume[g]) > 1e-5f || Synth.SlotMuted(s) != Instruments.Muted[g]) { bad.Append("restore missed slot " + s + "; "); break; }
        }
        Line(bad.Length == 0, "group volume / mute / a whole mix reach every voice slot of the group (and no other group's)",
             "lead " + Instruments.SlotsOf(3).Length + " slots at 0.37, strings " + Instruments.SlotsOf(6).Length + " muted and back, PushMix onto " + SynthBank.Slots + " slots, restored" + (bad.Length > 0 ? " — " + bad : ""));
    }

    static void RoleRules()
    {
        var bad = new StringBuilder();
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            int g = SynthBank.GroupOf(s);
            bool sus = g == 2 || g == 6 || g == 7, mono = g == 3 || g == 4;
            if (VoiceRules.IsSustaining(s) != sus || VoiceRules.IsMono(s) != mono || VoiceRules.IsBass(s) != (g == 4)) bad.Append("slot " + s + " rules; ");
            if (SynthBank.Def(s).drums) continue;
            for (int m = 20; m <= 110; m++)
            {
                int f = VoiceRules.Fold(s, m, 0);
                if (g != 4 && f < 48) { bad.Append("slot " + s + " folds " + m + " to " + f + " (< 48); "); break; }
                if (g != 5 && f > 96) { bad.Append("slot " + s + " folds " + m + " to " + f + " (> 96); "); break; }
                if (f % 12 != ((m % 12) + 12) % 12 && (f - m) % 12 != 0) { bad.Append("slot " + s + " changes the pitch class of " + m + "; "); break; }
            }
        }
        int bassLow = VoiceRules.Fold(Instruments.SlotOf(4, 3), 40, 0), glock = VoiceRules.Fold(Instruments.SlotOf(5, 2), 100, 0);
        if (bassLow >= 48) bad.Append("upright bass cannot go below 48 (" + bassLow + "); ");
        if (glock <= 96) bad.Append("glockenspiel cannot go above 96 (" + glock + "); ");
        Line(bad.Length == 0, "the role rules follow the group for every voice (sustaining, mono, bass, the low-end law); each voice folds into its own register",
             "bass voice 40 → " + bassLow + ", glockenspiel 100 → " + glock + (bad.Length > 0 ? " — " + bad : ""));
    }

    static void KeyboardFold()
    {
        var bad = new StringBuilder(); int n = 0;
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            var d = SynthBank.Def(s);
            if (d.drums) { if (VoiceRules.FoldMelody(s, 60, 0) != 60) bad.Append("drums fold; "); continue; }
            bool bass = SynthBank.GroupOf(s) == 4;
            for (int reg = -1; reg <= 1; reg++)
                for (int key = 53 + 12 * reg; key <= 88 + 12 * reg; key++)
                {
                    n++;
                    int want = bass ? key - 12 : key, got = VoiceRules.FoldMelody(s, key, reg);
                    if (got != want) { bad.Append(d.name + " key " + key + " reg " + reg + " → " + got + " (want " + want + "); "); break; }
                }
            int low = VoiceRules.FoldMelody(s, 30, 0);
            if (!bass && (low < 48 || (low - 30) % 12 != 0)) bad.Append(d.name + " 30 → " + low + "; ");
        }
        Line(bad.Length == 0, "keyboard fold: a melody key inside 48..96 (shifted by the register) plays its own pitch on every voice (the bass an octave down), notes outside fold in by octaves",
             n + " keys checked (the 25 keys from the lowest possible tonic, registers -1..+1)" + (bad.Length > 0 ? " — " + bad : ""));
    }

    static AudioCube PitchedCube(bool nonSustaining)
    {
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.nodes.Count == 0 || c.IsOnMoon || c.Island == null || Instruments.IsDrums(c.instrument)) continue;
            if (nonSustaining && VoiceRules.IsSustaining(Instruments.SlotOf(c.instrument))) continue;
            if (c.Island.energy == 0) continue;
            return c;
        }
        return null;
    }

    static int ResolveOne(AudioCube c, AudioCube.Window w, TileInteraction tile, VoiceRules.NoteEvent[] ev, out double on)
    {
        on = Synth.DspNow + 30.0;   // far ahead; nothing is dispatched
        var hit = new AudioCube.Hit { fires = true, node = 0, weight = 1, ratchet = 1 };
        return VoiceRules.Resolve(c, w, tile, hit, 0, 0, on, 0.25, on + 2.0, double.PositiveInfinity, ev);
    }

    static void ResolveVoice()
    {
        var sm = SongManager.I;
        var c = PitchedCube(true);
        if (c == null) { Line(false, "a cube sounds through its voice", "no pitched cube in the fixture"); return; }
        int voice0 = c.voice, g = c.instrument;
        var ev = new VoiceRules.NoteEvent[32];
        var bad = new StringBuilder(); string detail = "";
        try
        {
            var tile = c.nodes[0];
            for (int v = 0; v < Instruments.VoiceCount(g); v++)
            {
                c.voice = v;
                double on;
                int n = ResolveOne(c, new AudioCube.Window { start = 0f, length = 8f, island = c.Island, order = 0 }, tile, ev, out on);
                int slot = Instruments.SlotOf(g, v);
                int want = VoiceRules.Fold(slot, tile.midi + SongManager.Transpose + 12 * c.octave, c.Island.register);
                if (n < 1 || ev[0].slot != slot || ev[0].midi != want) bad.Append("voice " + v + ": n " + n + " slot " + (n > 0 ? ev[0].slot : -1) + " midi " + (n > 0 ? ev[0].midi : -1) + " (want " + slot + " / " + want + "); ");
                string why = n > 0 ? VoiceRules.HarmonicCheck(ev[0]) : "none";
                if (why != null) bad.Append("voice " + v + " harmonic: " + why + "; ");
            }
            detail = Instruments.GroupWords[g] + " cube, " + Instruments.VoiceCount(g) + " voices resolved";
            // a carried window on another island: stamped with the window's island, the target tile's pitch. v7 §21 (A): every carried window is the
            // LONG GRID's (a flow window — the walking cube's next measure); the meaning is unchanged: it answers to the measure it plays on
            KeyBlock other = null;
            foreach (var kb in sm.Islands) if (kb != null && kb != c.Island && !kb.IsMoon && !kb.IsKeyboard && kb.energy > 0) { other = kb; break; }
            if (other != null)
            {
                c.voice = 1;
                var t2 = other.GetTile(Mathf.Min(1, other.cols - 1), Mathf.Min(1, other.rows - 1));
                double on;
                int n = ResolveOne(c, new AudioCube.Window { start = 0f, length = 8f, island = other, order = 1, carried = true, hop = 1, flow = true }, t2, ev, out on);
                int slot = Instruments.SlotOf(g, 1);
                int want = VoiceRules.Fold(slot, t2.midi + SongManager.Transpose + 12 * c.octave, other.register);
                string why = n > 0 ? VoiceRules.HarmonicCheck(ev[0]) : "none";
                if (n < 1 || !ReferenceEquals(ev[0].island, other) || ev[0].midi != want || why != null)
                    bad.Append("carried: n " + n + " island " + (n > 0 && ev[0].island != null ? ev[0].island.name : "-") + " midi " + (n > 0 ? ev[0].midi : -1) + " want " + want + " harmonic " + why + "; ");
                else detail += "; carried window on '" + other.assignedChord + "': stamped with it, pitch " + ev[0].midi + " = its tile, chord-safe";
            }
            else bad.Append("no second island for the carried window; ");
        }
        finally { c.voice = voice0; }
        Line(bad.Length == 0, "a cube sounds through its voice's slot (every voice of its group; pitch = the voice's fold), and a carried window answers to the window's island", detail + (bad.Length > 0 ? " — " + bad : ""));
    }

    static void HarmonicUnit()
    {
        var sm = SongManager.I;
        KeyBlock isl = null;
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && !kb.IsKeyboard && kb.semitoneList.Count >= 3) { isl = kb; break; }
        if (isl == null) { Line(false, "harmonic check", "no chord island"); return; }
        double on = 100.0;
        int root = isl.chordRootMIDI + SongManager.Transpose + 12 * isl.register;
        Func<int, int, VoiceRules.NoteEvent> E = (slot, midi) => new VoiceRules.NoteEvent { slot = slot, midi = midi, vel = 90, onDsp = on, offDsp = on + 0.2, owner = 99, winStartDsp = on - 0.1, winEndDsp = on + 1.0, island = isl };
        int chordTone = root + isl.semitoneList[1] + 12;
        int nonTone = root + 1 + 12;
        bool isTone = false; foreach (int sPc in isl.semitoneList) if (((sPc % 12) + 12) % 12 == 1) isTone = true;
        string a = VoiceRules.HarmonicCheck(E(Instruments.SlotOf(0, 3), chordTone));
        string b = isTone ? "skip" : VoiceRules.HarmonicCheck(E(Instruments.SlotOf(0, 3), nonTone));
        var late = E(Instruments.SlotOf(1, 2), chordTone); late.offDsp = late.winEndDsp - 0.001;
        string c = VoiceRules.HarmonicCheck(late);
        string d = VoiceRules.HarmonicCheck(E(Instruments.SlotOf(4, 5), root - 24 + 12 * ((48 - (root - 24)) > 20 ? 0 : 0)));   // a bass voice below 48
        string e = VoiceRules.HarmonicCheck(E(Instruments.SlotOf(0, 2), (root % 12) + 36));                                  // a keys voice below 48
        string f = VoiceRules.HarmonicCheck(E(Instruments.SlotOf(9, 4), 38));                                                 // drums: any key
        bool ok = a == null && (isTone || b != null) && c != null && d == null && e != null && f == null;
        Line(ok, "harmonic check (the v2 guarantee with v6's groups): a chord tone passes, a non-tone fails, off past windowEnd − 5 ms fails, a bass voice may go below 48, a keys voice may not, drums pass",
             "tone " + (a ?? "ok") + " | non-tone " + (b ?? "ok") + " | late off " + (c ?? "ok") + " | bass low " + (d ?? "ok") + " | keys low " + (e ?? "ok") + " | drums " + (f ?? "ok"));
    }

    static void PressUnit()
    {
        var sm = SongManager.I;
        var isl = sm.Islands[0];
        var tile = isl.GetTile(2, Mathf.Min(1, isl.rows - 1));
        var bad = new StringBuilder();
        int slot = Instruments.SlotOf(8, 0);
        var e = VoiceRules.PressEvent(slot, tile, 0, 0.5);
        int want = VoiceRules.Fold(slot, tile.midi + SongManager.Transpose, isl.register);
        if (e.slot != slot || e.midi != want || e.vel < Synth.PressMinVelocity || e.vel != VoiceRules.PressVelocity(slot) || e.onDsp != 0.0 || Math.Abs(e.offDsp - (Synth.DspNow + 0.5)) > 0.05)
            bad.Append("piano: slot " + e.slot + " midi " + e.midi + " (want " + want + ") vel " + e.vel + " on " + F(e.onDsp) + "; ");
        var d = VoiceRules.PressEvent(Instruments.SlotOf(9, 4), tile, 0, 0.5, Synth.DspNow + 1.0);
        if (d.midi != VoiceRules.DrumPieceOf(tile) || d.vel < Synth.PressMinVelocity || Math.Abs(d.offDsp - d.onDsp - 0.12) > 1e-6) bad.Append("drums: midi " + d.midi + " vel " + d.vel + "; ");
        var vels = new StringBuilder();
        for (int g = 0; g < 10; g++) vels.Append(Instruments.GroupWords[g]).Append(' ').Append(VoiceRules.PressVelocity(Instruments.SlotOf(g, 0))).Append(g < 9 ? ", " : "");
        for (int s = 0; s < SynthBank.Slots; s++) if (VoiceRules.PressVelocity(s) < 100 || VoiceRules.PressVelocity(s) > 127) bad.Append("slot " + s + " press velocity " + VoiceRules.PressVelocity(s) + "; ");
        Line(bad.Length == 0, "the press note: the voice's slot, the audition's pitch (keyboard-aware fold / the kit piece), a strong velocity (≥ 100), at once or at a given time",
             "press velocities " + vels + (bad.Length > 0 ? " — " + bad : ""));
    }

    static int pressCount0, pressTrig0;
    /// <summary>Sends one press (stopped): PressNote counts it, the audio thread opens a press window (duck -15 dB, boost +4 dB).</summary>
    static void PressEnvelope()
    {
        var isl = SongManager.I.Islands[0];
        var tile = isl.GetTile(1, 0);
        Synth.ResetDuckMin();
        pressCount0 = Synth.PressCount; pressTrig0 = Synth.PressTriggers;
        int got = 0, gotVel = 0; Action<int, int, int, double, double> tap = (s, m, v, on, off) => { got++; gotVel = v; };
        Synth.OnPress += tap;
        try { VoiceRules.Press(VoiceRules.PressEvent(Instruments.SlotOf(0, 0), tile, 0, 0.3)); }
        finally { Synth.OnPress -= tap; }
        Line(Synth.PressCount == pressCount0 + 1 && got == 1 && gotVel >= Synth.PressMinVelocity && Synth.LastPressSlot == Instruments.SlotOf(0, 0),
             "Synth.PressNote counts the press and raises OnPress (slot, key, velocity, times)", "count +" + (Synth.PressCount - pressCount0) + ", OnPress " + got + " vel " + gotVel + ", last slot " + Synth.LastPressSlot + " key " + Synth.LastPressMidi);
    }

    // ================================================================== keyboard islands (when K's AddKeyboardIsland exists)
    static IEnumerator Keyboard()
    {
        // the press window opened by PressEnvelope has run by now: its duck and boost reached their levels
        yield return Wait(0.6f);
        float dmin = Synth.DuckMin, bmax = Synth.BoostMax;
        Line(Synth.PressTriggers > pressTrig0 && Math.Abs(Db(dmin) - Synth.PressDuckDb) < 1.0 && Math.Abs(Db(bmax) - Synth.PressBoostDb) < 0.5,
             "a press opens its window on the audio thread: the song bus ducks to the press level, the preview bus is boosted",
             "windows +" + (Synth.PressTriggers - pressTrig0) + ", duck min " + F(Db(dmin), "F1") + " dB (want " + F(Synth.PressDuckDb, "F0") + "), boost max " + F(Db(bmax), "+0.0") + " dB (want +" + F(Synth.PressBoostDb, "F0") + ")");

        var sm = SongManager.I;
        int idx = -1; string err = null;
        try { idx = sm.AddKeyboardIsland(0, false); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
        yield return Frames(4);
        KeyBlock kbd = null;
        foreach (var kb in sm.Islands) if (kb != null && kb.IsKeyboard) { kbd = kb; break; }
        if (kbd == null || kbd.tiles.Count == 0)
        {
            Info("keyboard island not available yet (AddKeyboardIsland → " + idx + (err != null ? ", " + err : "") + "): the keyboard fold is checked statically above");
            LoadFixture(); yield return Frames(3);
            yield break;
        }
        var c = PitchedCube(true);
        var bad = new StringBuilder(); int keys = 0, notFolded = 0, offChord = 0;
        var ev = new VoiceRules.NoteEvent[32];
        if (c == null) bad.Append("no pitched cube; ");
        else
        {
            int voice0 = c.voice, oct0 = c.octave;
            try
            {
                c.voice = Mathf.Min(2, Instruments.VoiceCount(c.instrument) - 1); c.octave = 0;
                int slot = Instruments.SlotOf(c.instrument, c.voice);
                bool bass = VoiceRules.IsBass(slot);
                foreach (var t in kbd.tiles)
                {
                    if (t == null) continue;
                    double on;
                    int n = ResolveOne(c, new AudioCube.Window { start = 0f, length = 8f, island = kbd, order = 0 }, t, ev, out on);
                    keys++;
                    int pitch = t.midi + SongManager.Transpose;
                    int want = VoiceRules.FoldMelody(slot, pitch, kbd.register);
                    bool inside = pitch + (bass ? -12 : 0) >= VoiceRules.MelodyLow + 12 * kbd.register - (bass ? 20 : 0) && pitch <= VoiceRules.MelodyHigh + 12 * kbd.register;
                    if (n < 1 || ev[0].midi != want) { bad.Append("key " + t.gridX + " midi " + (n > 0 ? ev[0].midi : -1) + " want " + want + "; "); continue; }
                    if (inside && ev[0].midi == pitch + (bass ? -12 : 0)) notFolded++;
                    string why = VoiceRules.HarmonicCheck(ev[0]);
                    if (why != null) bad.Append("key " + t.gridX + " harmonic: " + why + "; ");
                    bool tone = false; int pc = (((pitch - kbd.chordRootMIDI) % 12) + 12) % 12;
                    foreach (int sPc in kbd.semitoneList) if (((sPc % 12) + 12) % 12 == pc) tone = true;
                    if (!tone) offChord++;
                }
            }
            finally { c.voice = voice0; c.octave = oct0; }
        }
        Line(bad.Length == 0 && keys >= 20 && notFolded == keys, "keyboard island: every key plays its own pitch through the cube's voice (no octave fold inside the melody window) and needs no chord tone",
             keys + " keys, " + notFolded + " unfolded, " + offChord + " off the island's chord still pass the harmonic check" + (bad.Length > 0 ? " — " + bad : ""));
        LoadFixture();
        yield return Frames(3);
    }

    // ================================================================== timed: every voice sounds on both buses
    static IEnumerator AllVoicesSound()
    {
        Prepare(); GlobalClock.Stop();
        yield return Frames(2);
        for (int s = 0; s < SynthBank.Slots; s++) { Synth.SetSlotReverb(s, 0f); Synth.SetSlotChorus(s, 0f); }   // dry: a note's window holds only that note
        yield return Wait(1.0f);
        int n = SynthBank.Slots;
        var ons = new double[n];
        Synth.StartRecording(n * 0.34f + 24f * 0.5f + 1.5f, true);
        yield return Frames(3);
        for (int k = 0; k < n; k++)
        {
            Synth.AllNotesOff(true);                     // the previous voice stops dead: the window before each onset is silent
            // pads / strings / choirs carry their own reverb send in the SoundFont (CC91 0 does not remove it): their tails need longer to fade,
            // and a slow attack needs a quiet window before it
            bool slow = VoiceRules.IsSustaining(k) || (k > 0 && VoiceRules.IsSustaining(k - 1));
            double on = Synth.DspNow + (slow ? 0.60 : 0.10);
            var d = SynthBank.Def(k);
            int key = d.drums ? 38 : SynthBank.ClampToRegister(k, 60);
            Synth.Note(k, key, 100, on, on + 0.13, ToneOwner);   // song bus
            Synth.Audition(k, key, 100, on, on + 0.13);          // preview bus
            ons[k] = on;
            yield return UntilDsp(on + 0.16);
        }
        Synth.AllNotesOff(true);
        yield return Wait(0.3f);   // SaveRecording stops the tee: what was recorded so far is written
        bool saved = Synth.SaveRecording(Cap("a6_voices_mix.wav")) && Synth.SaveStems(Cap("a6_voices_song.wav"), Cap("a6_voices_preview.wav"), null);
        double rec0 = Synth.RecordingStartDsp;
        Instruments.PushToSynth();   // the sends come back
        if (!saved || rec0 <= 0.0) { Line(false, "every voice sounds on both buses", "recording not saved (start " + F(rec0, "F3") + ")"); yield break; }
        int sr;
        var song = ReadWav(Cap("a6_voices_song.wav"), out sr);
        var prev = ReadWav(Cap("a6_voices_preview.wav"), out sr);
        var bad = new StringBuilder(); var lv = new StringBuilder(); double minS = 0, minP = 0, maxDiff = 0; bool first = true;
        var level = new double[n];
        for (int k = 0; k < n; k++)
        {
            double t = ons[k] - rec0;
            double s1 = Rms(song, sr, t + 0.01, t + 0.13), s0 = Rms(song, sr, t - 0.06, t - 0.01);
            level[k] = Db(s1);
            double p1 = Rms(prev, sr, t + 0.01, t + 0.13), p0 = Rms(prev, sr, t - 0.06, t - 0.01);
            bool okS = s1 > 1e-3 && s1 > 4.0 * s0, okP = p1 > 1e-3 && p1 > 4.0 * p0;
            if (!okS || !okP) bad.Append(SynthBank.Def(k).name + " song " + F(Db(s1), "F1") + " dB (before " + F(Db(s0), "F1") + "), preview " + F(Db(p1), "F1") + " dB (before " + F(Db(p0), "F1") + "); ");
            if (first || Db(s1) < minS) minS = Db(s1);
            if (first || Db(p1) < minP) minP = Db(p1);
            first = false;
            maxDiff = Math.Max(maxDiff, Math.Abs(Db(s1) - Db(p1)));
            lv.Append(SynthBank.Def(k).caption).Append(' ').Append(F(Db(s1), "F0")).Append(k < n - 1 ? ", " : "");
        }
        Line(bad.Length == 0, "every voice renders a note on BOTH buses (song + preview; a C4-ish note, the snare on kits), each rising well above the silence before it",
             n + " voices; quietest " + F(minS, "F1") + " dBFS (song) / " + F(minP, "F1") + " dBFS (preview), buses within " + F(maxDiff, "F1") + " dB of each other — Captures/a6_voices_{mix,song,preview}.wav" + (bad.Length > 0 ? " — " + bad : ""));
        Info("each voice's note on the song bus (dBFS RMS, 10-130 ms, dry): " + lv);
        // the loudness match in the game's own synth, for the groups whose notes speak at once (keys, pluck, bass, bells, piano; drums: the snare
        // only): each voice's note against its group's voice 0 over 120 ms. Pads, strings, leads and choirs have slow attacks — their trims were
        // fitted offline on whole notes and held notes, K-weighted (scratchpad v6/a/lab)
        var match = new StringBuilder();
        for (int g = 0; g < SynthBank.Groups; g++)
        {
            if (g == 2 || g == 3 || g == 6 || g == 7) continue;
            int s0 = Instruments.SlotOf(g, 0); double worstD = 0; string worstV = "";
            for (int v = 1; v < Instruments.VoiceCount(g); v++) { int s = Instruments.SlotOf(g, v); double d = level[s] - level[s0]; if (Math.Abs(d) > Math.Abs(worstD)) { worstD = d; worstV = Instruments.VoiceName(g, v); } }
            match.Append(Instruments.GroupWords[g]).Append(' ').Append(F(worstD, "+0.0;-0.0")).Append(" (").Append(worstV).Append(g < SynthBank.Groups - 1 ? "), " : ")");
        }
        Info("loudness vs voice 0 in the game (plain RMS of the first 120 ms, largest difference per group): " + match);
    }

    // ================================================================== timed: group volume / mute heard on the voices
    static IEnumerator GroupVolumeHeard()
    {
        Prepare(); GlobalClock.Stop();
        var vol0 = (float[])Instruments.Volume.Clone(); var mute0 = (bool[])Instruments.Muted.Clone();
        for (int s = 0; s < SynthBank.Slots; s++) { Synth.SetSlotReverb(s, 0f); Synth.SetSlotChorus(s, 0f); }
        int koto = Instruments.SlotOf(1, 4), box = Instruments.SlotOf(5, 3);
        int kKey = SynthBank.ClampToRegister(koto, 60), bKey = SynthBank.ClampToRegister(box, 72);
        var ons = new double[6];
        Synth.StartRecording(4.5f, true);
        yield return Frames(3);
        // A: full level, B: the groups at 0.25, C: the groups muted
        for (int phase = 0; phase < 3; phase++)
        {
            if (phase == 1) { Instruments.SetVolume(1, 0.25f); Instruments.SetVolume(5, 0.25f); }
            if (phase == 2) { Instruments.SetMuted(1, true); Instruments.SetMuted(5, true); }
            yield return Frames(2);
            Synth.AllNotesOff(true);
            double on = Synth.DspNow + 0.10;
            Synth.Note(koto, kKey, 100, on, on + 0.25, ToneOwner);
            Synth.Note(box, bKey, 100, on + 0.35, on + 0.60, ToneOwner);
            ons[2 * phase] = on; ons[2 * phase + 1] = on + 0.35;
            yield return UntilDsp(on + 0.75);
        }
        Instruments.Restore(vol0, mute0);
        yield return Wait(0.2f);
        bool saved = Synth.SaveRecording(Cap("a6_group_mix.wav")) && Synth.SaveStems(Cap("a6_group_song.wav"), Cap("a6_group_preview.wav"), null);
        double rec0 = Synth.RecordingStartDsp;
        Instruments.PushToSynth();
        if (!saved) { Line(false, "group volume / mute are heard on every voice", "recording not saved"); yield break; }
        int sr; var song = ReadWav(Cap("a6_group_song.wav"), out sr);
        var lv = new double[6];
        for (int i = 0; i < 6; i++) { double t = ons[i] - rec0; lv[i] = Db(Rms(song, sr, t + 0.02, t + 0.22)); }
        double kd = lv[2] - lv[0], bd = lv[3] - lv[1];
        bool ok = Math.Abs(kd + 12.04) < 1.5 && Math.Abs(bd + 12.04) < 1.5 && lv[4] < lv[0] - 50 && lv[5] < lv[1] - 50;
        Line(ok, "group volume and mute are heard on a voice that is not voice 0 (koto in pluck, music box in bells): 0.25 = −12 dB, muted = silent",
             "koto " + F(lv[0], "F1") + " → " + F(lv[2], "F1") + " (" + F(kd, "+0.0;-0.0") + " dB) → muted " + F(lv[4], "F1") + "; music box " + F(lv[1], "F1") + " → " + F(lv[3], "F1") + " (" + F(bd, "+0.0;-0.0") + " dB) → muted " + F(lv[5], "F1") + " — Captures/a6_group_song.wav");
    }

    // ================================================================== timed: a press over a dense song; the hover duck
    static IEnumerator PressOverSong()
    {
        Prepare();
        var sm = SongManager.I;
        var st = Gallery.LoadState(PressSong);
        string songName = PressSong;
        if (st == null) { st = SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)); songName = "the fixture (gallery song missing)"; }
        GlobalClock.Stop();
        SongState.Apply(st); History.Reset(); History.Push();
        yield return Frames(6);
        yield return Wait(1.0f);                      // the new world's heavy first frames happen before Play
        GlobalClock.Stop(); GlobalClock.Seek(0);
        GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(2.0f);
        int late0 = Synth.LateEvents, real0 = realLate;
        Synth.ResetDuckMin();
        Synth.StartRecording(10.5f, true);
        yield return Frames(3);
        double b = Synth.DspNow + 0.8;
        int[] groups = { 8, 0, 1, 3 };
        var ons = new double[groups.Length];
        var isl = sm.Islands[0];
        var tile = isl.GetTile(2, Mathf.Clamp(isl.rows / 2, 0, isl.rows - 1));
        for (int i = 0; i < groups.Length; i++)
        {
            ons[i] = b + 1.6 * i;
            var e = VoiceRules.PressEvent(Instruments.SlotOf(groups[i], 0), tile, 0, 0.5, ons[i]);
            VoiceRules.Press(e);
        }
        double hoverOn = b + 1.6 * groups.Length + 0.6;
        var h = VoiceRules.AuditionEvent(Instruments.SlotOf(0, 0), tile, 0, 0.4, TileInteraction.HoverVolume, hoverOn);
        VoiceRules.Dispatch(new[] { h }, 1, VoiceRules.OwnerPreview);
        float t0 = Time.realtimeSinceStartup;
        while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        bool saved = Synth.SaveRecording(Cap("a6_press_mix.wav")) && Synth.SaveStems(Cap("a6_press_song.wav"), Cap("a6_press_note.wav"), Cap("a6_press_gains.wav"));
        double rec0 = Synth.RecordingStartDsp;
        int lateDuring = Synth.LateEvents - late0, realDuring = realLate - real0;
        GlobalClock.Stop();
        if (!saved) { Line(false, "a press is louder than the song", "recording not saved"); yield break; }
        int sr;
        var mix = ReadWav(Cap("a6_press_mix.wav"), out sr);
        var song = ReadWav(Cap("a6_press_song.wav"), out sr);
        var note = ReadWav(Cap("a6_press_note.wav"), out sr);
        var gains = ReadWav(Cap("a6_press_gains.wav"), out sr);
        var rows = new StringBuilder(); double worst = 99; bool duckOk = true, boostOk = true;
        for (int i = 0; i < groups.Length; i++)
        {
            double t = ons[i] - rec0;
            double p = Rms(note, sr, t + 0.05, t + 0.30), s = Rms(song, sr, t + 0.05, t + 0.30), m = Rms(mix, sr, t + 0.05, t + 0.30);
            double ratio = Db(p) - Db(s);
            worst = Math.Min(worst, ratio);
            float dmn, dmx, bmn, bmx;
            MinMax(gains, sr, 0, t, t + 0.55, out dmn, out dmx);
            MinMax(gains, sr, 1, t, t + 0.55, out bmn, out bmx);
            double duckDb = Db(dmn), boostDb = Db(8.0 * bmx);   // the gains track holds the boost / 8
            if (Math.Abs(duckDb - Synth.PressDuckDb) > 1.0) duckOk = false;
            double before = Db(Rms(song, sr, t - 0.3, t));   // the song's level going into the press (what the boost is sized from)
            if (boostDb < Synth.PressBoostDb - 0.5 || boostDb > Synth.PressBoostMaxDb + 0.5) boostOk = false;
            rows.Append(Instruments.GroupWords[groups[i]]).Append(": press ").Append(F(Db(p), "F1")).Append(" dBFS, song alone ").Append(F(Db(s), "F1"))
                .Append(" → ").Append(F(ratio, "+0.0;-0.0")).Append(" dB; the mix ").Append(F(Db(m) - Db(s), "+0.0;-0.0")).Append(" dB over the song alone; duck ")
                .Append(F(duckDb, "F1")).Append(" dB, boost ").Append(F(boostDb, "+0.0")).Append(" dB (the song going in ").Append(F(before, "F1")).Append(" dBFS) | ");
        }
        var onsets = new StringBuilder();
        for (int i = 0; i < groups.Length; i++) onsets.Append(F(ons[i] - rec0, "F4")).Append(' ');
        Info("press onsets (s after the recording's first frame): " + onsets + "| hover " + F(hoverOn - rec0, "F4") + " | " + Synth.Stats());
        float peak = Peak(mix);
        int over = 0; for (int i = 0; i < mix.Length; i++) if (Math.Abs(mix[i]) > 0.7f) over++;
        Line(worst >= 6.0, "a pressed note over a dense playing song is ≥ 6 dB louder than the song alone (RMS 50-300 ms after the onset; stems)",
             songName + " — " + rows + "worst " + F(worst, "+0.0") + " dB — Captures/a6_press_{mix,song,note,gains}.wav");
        Line(duckOk && boostOk, "under every press the song ducks to the press level (−15 dB) and the preview bus is boosted (+4 dB over silence; over the song sized to put the press about +9 dB above it; ≤ +18 dB)",
             "see the duck / boost per press above; base " + F(Synth.PressBoostDb, "+0") + " dB, aim " + F(Synth.PressOverDb, "+0") + " dB over the song, ceiling " + F(Synth.PressBoostMaxDb, "+0") + " dB");
        Line(peak < 0.999f, "no clipping: the mix stays under full scale through the presses (soft limiter)", "peak " + F(peak, "F3") + " (" + F(Db(peak), "F1") + " dBFS), " + over + " samples above the limiter knee 0.7 of " + mix.Length);
        // the hover audition: -10 dB duck, a little louder over the song than v5 (-8 dB)
        {
            double t = hoverOn - rec0;
            float dmn, dmx;
            MinMax(gains, sr, 0, t, t + 0.4, out dmn, out dmx);
            double hv = Rms(note, sr, t + 0.05, t + 0.30), sg = Rms(song, sr, t + 0.05, t + 0.30);
            double overSongV6 = Db(hv) - (Db(sg) + Db(dmn)), overSongV5 = Db(hv) - (Db(sg) - 7.96);
            Line(Math.Abs(Db(dmn) - (-10.0)) < 0.6 && Math.Abs(Synth.DuckDb - (-10f)) < 0.01f, "a hover audition ducks the song −10 dB (v5: −8): a little louder over the music than v5",
                 "duck " + F(Db(dmn), "F1") + " dB; the hover over the ducked song " + F(overSongV6, "+0.0;-0.0") + " dB (v5's duck would give " + F(overSongV5, "+0.0;-0.0") + " dB)");
        }
        Line(realDuring == 0, "Synth late 0 while pressing over the playing song (v7: main-thread stalls counted apart)", "late +" + lateDuring + " (" + realDuring + " outside main-thread stalls)");
    }

    // ================================================================== timed: the performance table
    /// <summary>A dense one-island song: 12 cubes playing 16ths at once for the whole loop (2 bars, 120 bpm). <paramref name="variety"/>: 12
    /// different voices (none of them a voice 0) — else the v5 sounds (voice 0 of each group; keys and pluck twice).</summary>
    public static SongState PerfSong(bool variety)
    {
        var st = SongState.FromJson(File.ReadAllText(V2Checks.FixturePath));
        var m0 = MeasureState.Clone(st.measures[0]);
        m0.bars = 2; m0.col = 0; m0.repeat = 1; m0.sleep = false; m0.energy = 2; m0.carry = 0;
        st.measures = new[] { m0 };
        st.moons = new MeasureState[0];
        st.deck = new MeasureState[0];
        st.bpm = 120f; st.swing = 0f; st.loop = true; st.transpose = 0;
        st.version = SongState.CurrentVersion;
        st.instVolume = null; st.instMuted = null;
        int rows = m0.semis != null ? m0.semis.Length : 3;
        int[,] v5 = { { 0, 0 }, { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }, { 6, 0 }, { 7, 0 }, { 8, 0 }, { 9, 0 }, { 0, 0 }, { 1, 0 } };
        int[,] v6 = { { 0, 1 }, { 0, 5 }, { 1, 4 }, { 1, 7 }, { 2, 2 }, { 3, 1 }, { 3, 6 }, { 4, 5 }, { 5, 3 }, { 6, 6 }, { 7, 1 }, { 9, 4 } };
        var pick = variety ? v6 : v5;
        var cubes = new List<CubeState>();
        for (int i = 0; i < 12; i++)
        {
            int nodes = 32;   // 32 sixteenths = 2 bars
            var xs = new int[nodes]; var zs = new int[nodes]; var durs = new int[nodes]; var rests = new bool[nodes];
            for (int j = 0; j < nodes; j++) { int p = (j + i) % 10; xs[j] = p < 6 ? p : 10 - p; zs[j] = (i + j / 3) % Mathf.Max(1, rows); durs[j] = 6; }
            cubes.Add(new CubeState { instrument = pick[i, 0], voice = pick[i, 1], measure = 0, xs = xs, zs = zs, rests = rests, durs = durs, volume = 0.85f, gate = 1, step = 1, id = 7000 + i, seed = 101 + i });
        }
        st.cubes = cubes.ToArray();
        return st;
    }

    struct Perf { public float avg, peak, ema, frameMs; public int voices, songVoices, blocks; public double avgVoices; }

    static IEnumerator Measure(bool variety, Perf[] outp, int at)
    {
        Prepare(); GlobalClock.Stop();
        SongState.Apply(PerfSong(variety)); History.Reset(); History.Push();
        yield return Frames(6);
        yield return Wait(1.0f);
        GlobalClock.Stop(); GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(1.5f);
        Synth.ResetPerf();
        float t0 = Time.realtimeSinceStartup; double emaSum = 0, vSum = 0, dtSum = 0; int n = 0;
        while (Time.realtimeSinceStartup - t0 < 6f) { yield return null; emaSum += Synth.CpuLoad; vSum += Synth.ActiveVoices; dtSum += Time.unscaledDeltaTime; n++; }
        outp[at] = new Perf
        {
            avg = Synth.PerfAvgCpu, peak = Synth.PerfMaxCpu, ema = (float)(emaSum / Math.Max(1, n)), voices = Synth.PerfMaxVoices, songVoices = Synth.PerfMaxSongVoices,
            blocks = Synth.PerfBlocks, avgVoices = vSum / Math.Max(1, n), frameMs = (float)(1000.0 * dtSum / Math.Max(1, n))
        };
        GlobalClock.Stop();
        yield return Frames(3);
    }

    static IEnumerator PerformanceTable()
    {
        var p = new Perf[2]; var q = new Perf[2];
        int late0 = Synth.LateEvents, real0 = realLate;
        yield return Measure(false, p, 0);
        yield return Measure(true, p, 1);
        yield return Measure(false, q, 0);   // twice each, interleaved: the difference between the tables vs the run-to-run spread
        yield return Measure(true, q, 1);
        string cfg = Synth.MeasureConfigs();
        Info("PERFORMANCE — the same dense song (one island, 12 cubes playing 16ths at once, 120 bpm, 6 s after a 1.5 s warm-up), DSP buffer " + AudioSettingsBuffer() + ":");
        Info("  table            | audio cpu avg | audio cpu peak | synth voices peak (song + preview) | song voices avg | frame ms avg");
        string[] names = { "(a) v5 sounds    ", "(b) 12 v6 voices " };
        for (int i = 0; i < 2; i++)
            foreach (var r in new[] { p[i], q[i] })
                Info("  " + names[i] + "| " + F(100 * r.avg, "F1") + " %        | " + F(100 * r.peak, "F1") + " %         | " + r.voices + " (song bus " + r.songVoices + ", its cap 64)          | " + F(r.avgVoices, "F1") + "            | " + F(r.frameMs, "F1"));
        Info("  SoundFont (GeneralUser GS, the same file for both tables): parsed in " + F(Synth.LoadMs, "F0") + " ms at boot, " + F(Synth.SampleBytes / 1048576.0, "F1") + " MB of sample data held in memory; the two synthesizers (" + Synth.ChannelCount + " channels each) set up in " + F(Synth.InitMs, "F1") + " ms at boot");
        Info("  synthesizer set-up, v5 table vs v6 table (throwaway pair built on the main thread): " + cfg);
        bool ran = p[0].blocks > 50 && p[1].blocks > 50 && q[0].blocks > 50 && q[1].blocks > 50;
        float aAvg = (p[0].avg + q[0].avg) / 2f, bAvg = (p[1].avg + q[1].avg) / 2f, bPeak = Mathf.Max(p[1].peak, q[1].peak);
        Line(ran && bPeak < 0.5f && realLate - real0 == 0, "performance: the expanded table plays the dense 12-voice song with the audio thread well inside its budget and no late notes",
             "(a) v5 sounds " + F(100 * aAvg, "F1") + " % avg, (b) 12 v6 voices " + F(100 * bAvg, "F1") + " % avg / " + F(100 * bPeak, "F1") + " % peak of one core, late +" + (Synth.LateEvents - late0) + " (" + (realLate - real0) + " outside main-thread stalls)");
    }

    static string AudioSettingsBuffer()
    {
        int len, num; AudioSettings.GetDSPBufferSize(out len, out num);
        return len + " × " + num + " @ " + AudioSettings.outputSampleRate + " Hz";
    }

    // ================================================================== A's older suites, AutoHand on
    public static bool OlderDone = true;
    public static string OlderReport = "";
    public static string OlderPath => Path.Combine(V2Checks.CapturePath, "a6_older_report.txt");

    /// <summary>Runs the older suites A owns — V2Checks.RunAll + RunWorld, WpAChecks.RunM1 + RunM2Static and its timed rider / Moon / stutter
    /// checks, V2Checks.RunIntegration — with PathManager.AutoHand = true (they draw by clicking tiles) and writes Captures/a6_older_report.txt.</summary>
    public static string RunOlder()
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!OlderDone) return "already running";
        OlderDone = false; OlderReport = "";
        try { if (File.Exists(OlderPath)) File.Delete(OlderPath); } catch (Exception) { }
        SequenceMaster.I.StartCoroutine(OlderRoutine());
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

    static IEnumerator OlderRoutine()
    {
        var saves = V3Fixes.SnapshotSaves();
        var body = new StringBuilder(); var head = new StringBuilder(); var fails = new StringBuilder();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        bool hand0 = PathManager.AutoHand;
        PathManager.AutoHand = true;
        string r; int p, f;
        // V2Checks RunAll / RunWorld
        Prepare(); V3ChecksB.Prepare(); LoadFixture(); PathManager.SimOnly = false;
        yield return Frames(2);
        try { r = V2Checks.RunAll(); } catch (Exception e) { r = "FAIL RunAll threw " + e.Message + "\n"; }
        p = 0; f = 0; Count(r, "V2Checks.RunAll", ref p, ref f, fails); head.Append("V2Checks.RunAll ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V2Checks.RunAll\n").Append(r).Append('\n');
        yield return Frames(2);
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
        w.Append("INFO ").Append(WpAChecks.RiderArm()).Append('\n');
        yield return Wait(4f * 60f / GlobalClock.BPM * 3f + 0.8f);
        w.Append(WpAChecks.RiderCheck(3)).Append('\n');
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
        yield return null;
        r = V2Checks.RunIntegration();
        yield return Frames(2);
        float t0 = Time.realtimeSinceStartup;
        while (!V2Checks.IntegrationDone && Time.realtimeSinceStartup - t0 < 300f) yield return null;
        p = 0; f = 0; Count(V2Checks.IntegrationReport, "V2Checks.RunIntegration", ref p, ref f, fails); head.Append("V2Checks.RunIntegration ").Append(p).Append(" pass ").Append(f).Append(" fail\n");
        body.Append("==== V2Checks.RunIntegration ").Append(r).Append('\n').Append(V2Checks.IntegrationReport).Append('\n');
        // restore
        FocusLoop.Dismiss(); GlobalClock.Stop();
        Prepare(); LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        PathManager.AutoHand = hand0;
        V3Fixes.RestoreSaves(saves);
        head.Append("synth late +").Append(Synth.LateEvents - late0).Append(" errors +").Append(Synth.Errors - err0).Append(" | ").Append(Synth.Stats()).Append('\n');
        if (fails.Length > 0) head.Append("FAILS:\n").Append(fails);
        OlderReport = head.ToString() + "\n" + body.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(OlderPath, OlderReport); } catch (Exception) { }
        OlderDone = true;
    }
}
