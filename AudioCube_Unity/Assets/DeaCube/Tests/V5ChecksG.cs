using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// SPEC v5 §3 (package G) — the gallery songs. Per index entry: the song loads (Gallery.LoadState: version 4, a key), opens (Gallery.Open)
/// with every island / column / Moon / cube it was authored with (all cubes finalized, the song key kept), round-trips through
/// SongState.Capture, plays at least 8 bars from bar 0 with Synth late / errors 0 and no console error, frames its chorus (the index's
/// hook bar) for one capture (Captures/g5_&lt;id&gt;.png), and records WAVs for the analysis:
/// &lt;id&gt;.wav (12 s from bar 0), &lt;id&gt;_chorus.wav (12 s from that column) and &lt;id&gt;_chorus_novoice.wav (the same with the voice muted, for
/// the lead-over-support ratio). Report: Captures/g5_report.txt (PASS / FAIL / INFO lines; poll <see cref="Done"/>).
/// Restores at the end: the clock stopped, mutes and camera follow as they were, the backup slots Gallery.Open rotated (SongIO.Backup) put
/// back byte for byte, the gallery id cleared; the caller restores the autosave (PROTOCOL).
/// </summary>
public static class V5ChecksG
{
    public static bool Done = true;
    public static string Progress = "";
    public static string LastReport = "";
    static string ReportPath => Path.Combine(V2Checks.CapturePath, "g5_report.txt");

    /// <summary>The instrument that sings each song's tune (muted for the no-voice stem); Lead unless listed.</summary>
    static readonly Dictionary<string, int> VoiceInstrument = new Dictionary<string, int> { { "moon-tide", 0 } };

    /// <summary>Starts the run. <paramref name="ids"/>: comma-separated gallery ids ("" = every entry); <paramref name="wavDir"/>: where the WAVs go
    /// (null = Captures/g5_wav); <paramref name="stems"/>: also record the chorus and the no-voice chorus.</summary>
    public static string Run(bool captures = true, string ids = "", string wavDir = null, bool stems = true)
    {
        if (!Done) return "already running";
        if (SequenceMaster.I == null || SongManager.I == null) return "no SequenceMaster / SongManager";
        Done = false;
        SequenceMaster.I.StartCoroutine(Routine(captures, ids ?? "", string.IsNullOrEmpty(wavDir) ? Path.Combine(V2Checks.CapturePath, "g5_wav") : wavDir, stems));
        return "started";
    }

    static int pass, fail;
    static StringBuilder sb;
    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" | ").Append(detail);
        sb.Append('\n');
    }
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); }
    static void Flush() { try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, sb.ToString()); } catch (Exception) { } }

    static int consoleErrors;
    static readonly List<string> consoleLines = new List<string>();
    static void OnLog(string msg, string stack, LogType t)
    {
        if (t != LogType.Error && t != LogType.Exception && t != LogType.Assert) return;
        consoleErrors++;
        if (consoleLines.Count < 8) consoleLines.Add(t + ": " + (msg.Length > 160 ? msg.Substring(0, 160) : msg));
    }

    static readonly int[] tapCount = new int[SynthBank.Slots];
    static readonly long[] tapVel = new long[SynthBank.Slots];
    static readonly int[] tapLo = new int[SynthBank.Slots], tapHi = new int[SynthBank.Slots];
    static void OnTap(VoiceRules.NoteEvent e)
    {
        int s = Mathf.Clamp(e.slot, 0, SynthBank.Slots - 1);
        tapCount[s]++; tapVel[s] += e.vel;
        if (tapCount[s] == 1) { tapLo[s] = e.midi; tapHi[s] = e.midi; } else { tapLo[s] = Mathf.Min(tapLo[s], e.midi); tapHi[s] = Mathf.Max(tapHi[s], e.midi); }
    }
    static void ClearTaps() { for (int i = 0; i < SynthBank.Slots; i++) { tapCount[i] = 0; tapVel[i] = 0; tapLo[i] = tapHi[i] = 0; } }
    static string TapSummary()
    {
        var s = new StringBuilder();
        for (int i = 0; i < SynthBank.Slots; i++)
        {
            if (tapCount[i] == 0) continue;
            if (s.Length > 0) s.Append(", ");
            s.Append(SynthBank.Defs[i].name).Append(' ').Append(tapCount[i]).Append(" notes vel ").Append((tapVel[i] / (double)tapCount[i]).ToString("F0"));
            if (!SynthBank.Defs[i].drums) s.Append(" midi ").Append(tapLo[i]).Append('-').Append(tapHi[i]);
        }
        return s.ToString();
    }

    /// <summary>A cube's identity for the round trip: its island, instrument and path (tiles, lengths, stickers).</summary>
    static string Sig(CubeState c)
    {
        var s = new StringBuilder();
        s.Append(c.moon >= 0 ? "moon" + c.moon : "m" + c.measure).Append(':').Append(c.instrument).Append(':');
        for (int i = 0; i < c.xs.Length; i++)
        {
            s.Append(c.xs[i]).Append(',').Append(c.zs[i]).Append(',');
            s.Append(c.durs != null && i < c.durs.Length ? c.durs[i] : -1).Append(',');
            var m = c.ModsOrDerived(); s.Append(i < m.Length ? m[i] : 0).Append(';');
        }
        s.Append("o").Append(c.octave).Append("f").Append(c.follow).Append("g").Append(c.gate);
        return s.ToString();
    }

    static IEnumerator Routine(bool captures, string ids, string wavDir, bool stems)
    {
        sb = new StringBuilder(); pass = 0; fail = 0; consoleErrors = 0; consoleLines.Clear();
        sb.Append("V5ChecksG (gallery songs) ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        // protocol preamble: no autosave from a test song, the menu / prompt / tutorial out of the way
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); Onboarding.Suppressed = true;
        // the backup slots Gallery.Open may rotate (a song with cubes is backed up before the next one replaces it): kept aside, put back at the end
        var backups = new Dictionary<string, byte[]>();
        var backupTimes = new Dictionary<string, DateTime>();
        for (int i = 1; i <= SongIO.BackupSlots; i++)
        {
            string p = SongIO.BackupPath(i);
            if (File.Exists(p)) { backups[p] = File.ReadAllBytes(p); backupTimes[p] = File.GetLastWriteTimeUtc(p); } else backups[p] = null;
        }
        string mainBefore = File.Exists(SongIO.Path) ? Convert.ToBase64String(File.ReadAllBytes(SongIO.Path)) : null;
        bool followBefore = OrbitCamera.I != null && OrbitCamera.I.followPlayhead;
        var mutedBefore = (bool[])Instruments.Muted.Clone();
        Application.logMessageReceived += OnLog;
        var prevTap = VoiceRules.Tap;
        VoiceRules.Tap = OnTap;
        try { Directory.CreateDirectory(wavDir); } catch (Exception e) { Info("wav dir: " + e.Message); }

        Gallery.Reload();
        var entries = Gallery.Entries.ToList();
        var want = ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
        if (want.Count == 0)
        {
            Check("the index lists seven songs (v9: + get proto, the user's own MIDI)", entries.Count == 7, entries.Count + " entries: " + string.Join(" ", entries.Select(e => e.id)));
            Check("gallery ids are unique", entries.Select(e => e.id).Distinct().Count() == entries.Count);
            foreach (var e in entries)
            {
                bool fields = !string.IsNullOrEmpty(e.title) && e.title == e.title.ToLowerInvariant() && e.title.Split(' ').Length <= 2
                    && !string.IsNullOrEmpty(e.after) && e.after.Contains(" · ") && e.vibe >= 0 && e.vibe < Vibe.Count && e.bpm > 0f
                    && e.bars >= 16 && (e.bars <= 32 || e.id == "get-proto") && e.file == "Gallery/" + e.id;   // v9: get proto is the user's whole song (89 bars)
                Check("index entry " + e.id + " is well formed (lowercase 1-2 word title, after, vibe, bpm, 16-32 bars — get proto its whole 89 —, file)", fields,
                      "'" + e.title + "' after '" + e.after + "' vibe " + Vibe.Word((VibeKind)Mathf.Clamp(e.vibe, 0, 6)) + " bpm " + e.bpm + " bars " + e.bars);
            }
        }
        foreach (var e in entries)
        {
            if (want.Count > 0 && !want.Contains(e.id)) continue;
            Progress = e.id;
            yield return SongRoutine(e, captures, wavDir, stems);
            Flush();
        }

        // restore
        GlobalClock.Stop();
        VoiceRules.Tap = prevTap;
        Application.logMessageReceived -= OnLog;
        for (int i = 0; i < Instruments.Count; i++) if (Instruments.Muted[i] != mutedBefore[i]) Instruments.SetMuted(i, mutedBefore[i]);
        if (OrbitCamera.I != null) OrbitCamera.I.followPlayhead = followBefore;
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        Gallery.ClearCurrent();
        int restored = 0;
        foreach (var kv in backups)
        {
            try
            {
                if (kv.Value == null) { if (File.Exists(kv.Key)) { File.Delete(kv.Key); restored++; } }
                else if (!File.Exists(kv.Key) || !File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value))
                {
                    File.WriteAllBytes(kv.Key, kv.Value); File.SetLastWriteTimeUtc(kv.Key, backupTimes[kv.Key]); restored++;
                }
            }
            catch (Exception ex) { Info("backup restore " + kv.Key + ": " + ex.Message); }
        }
        Info("backup slots put back: " + restored);
        string mainAfter = File.Exists(SongIO.Path) ? Convert.ToBase64String(File.ReadAllBytes(SongIO.Path)) : null;
        Check("the user's main save is untouched", mainBefore == mainAfter);
        Check("no console error during the run", consoleErrors == 0, consoleErrors + (consoleLines.Count > 0 ? ": " + string.Join(" / ", consoleLines) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        LastReport = sb.ToString();
        Flush();
        Progress = "done";
        Done = true;
    }

    static IEnumerator SongRoutine(GalleryEntry e, bool captures, string wavDir, bool stems)
    {
        string id = e.id;
        var st = Gallery.LoadState(id);
        // v6: SongState.CurrentVersion is 5 (carry, keyboards, voices — all optional fields); the gallery files are v4 files, which load unchanged
        Check(id + ": loads (version 4, a key)", st != null && st.version >= 4 && st.version <= SongState.CurrentVersion && st.keyTonic >= 0 && st.keyTonic < 12,
              st == null ? "null" : "v" + st.version + " key " + st.keyTonic + (st.keyMinor ? "m" : ""));
        if (st == null) yield break;
        int nIslands = st.measures.Length, nMoons = st.moons != null ? st.moons.Length : 0, nCubes = st.cubes != null ? st.cubes.Length : 0;
        int nCols = st.measures.Select(m => m.col).Distinct().Count();
        var sigs = st.cubes.Select(Sig).OrderBy(x => x).ToList();

        int errs0 = consoleErrors;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool opened = Gallery.Open(id, false);
        sw.Stop();
        yield return null; yield return null;
        var sm = SongManager.I;
        int finalized = SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.nodes.Count > 0);
        Check(id + ": opens", opened && Gallery.CurrentId == id, "Gallery.Open " + sw.ElapsedMilliseconds + " ms");
        Check(id + ": every island / column / Moon as authored", sm.Islands.Count == nIslands && sm.ColumnCount == nCols && sm.Moons.Count == nMoons,
              "islands " + sm.Islands.Count + "/" + nIslands + ", columns " + sm.ColumnCount + "/" + nCols + ", moons " + sm.Moons.Count + "/" + nMoons);
        Check(id + ": every cube restored and finalized", finalized == nCubes, finalized + "/" + nCubes);
        Check(id + ": the song key is kept", sm.HasSongKey && sm.SongKey.tonic == st.keyTonic && sm.SongKey.minor == st.keyMinor, sm.SongKey.ToString());
        Check(id + ": one History entry after opening", History.UndoCount == 1, "undo stack " + History.UndoCount);
        var cap = SongState.Capture();
        var capSigs = cap.cubes.Select(Sig).OrderBy(x => x).ToList();
        int same = 0; for (int i = 0; i < Math.Min(sigs.Count, capSigs.Count); i++) if (sigs[i] == capSigs[i]) same++;
        Check(id + ": round trip (Capture keeps every cube: island, instrument, tiles, lengths, stickers)", capSigs.Count == sigs.Count && same == sigs.Count,
              same + "/" + sigs.Count + " identical" + (cap.measures.Length == nIslands ? "" : ", islands " + cap.measures.Length));
        Check(id + ": bpm and total length", Mathf.Abs(GlobalClock.BPM - st.bpm) < 0.01f && Mathf.RoundToInt(GlobalClock.TotalBeats / 4f) == e.bars,
              "bpm " + GlobalClock.BPM + ", " + (GlobalClock.TotalBeats / 4f) + " bars (index " + e.bars + ")");

        // play >= 8 bars from bar 0 with a 12 s recording
        float bpm = GlobalClock.BPM;
        float eightBars = 8f * 4f * 60f / bpm;
        ClearTaps();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        if (OrbitCamera.I != null) { OrbitCamera.I.followPlayhead = true; OrbitCamera.I.FrameColumn(0, 0.2f); }
        GlobalClock.Seek(0);
        Synth.StartRecording(12f);
        GlobalClock.Play();
        AudioCube.ScheduleAllNow();   // v9 (G): as Gallery.Open plays a song — bar 0 queued before a big song's slow first frames (get proto: 122 islands)
        float t0 = Time.realtimeSinceStartup, frames = 0f, worst = 0f;
        while (Time.realtimeSinceStartup - t0 < Mathf.Max(12.3f, eightBars + 0.3f))
        {
            yield return null;
            frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
        }
        float secs = Time.realtimeSinceStartup - t0;
        float barsPlayed = (float)(GlobalClock.SongBeatD / 4.0);
        bool recOk = Synth.RecordingDone && Synth.SaveRecording(Path.Combine(wavDir, id + ".wav"));
        Check(id + ": plays >= 8 bars with Synth late / errors 0", barsPlayed >= 8f - 0.01f && Synth.LateEvents == late0 && Synth.Errors == err0,
              "played " + barsPlayed.ToString("F1") + " bars in " + secs.ToString("F1") + " s, late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0));
        Check(id + ": recorded " + id + ".wav", recOk, Path.Combine(wavDir, id + ".wav"));
        Info(id + ": frames " + (frames / secs).ToString("F0") + " fps mean, worst frame " + (worst * 1000f).ToString("F0") + " ms | notes " + TapSummary());

        // the chorus: the index's hook bar (the chorus's first bar, passes counted)
        float hookBeat = Mathf.Clamp(e.hook, 0, Mathf.Max(0, e.bars - 1)) * 4f;
        int chorus = sm.ActiveColumn(hookBeat + 0.01f), most = sm.ColumnSize(chorus);
        GlobalClock.Stop();
        yield return null;
        if (stems)
        {
            ClearTaps();
            late0 = Synth.LateEvents; err0 = Synth.Errors;
            GlobalClock.Seek(hookBeat);
            if (OrbitCamera.I != null) OrbitCamera.I.FrameColumn(chorus, 0.2f);
            Synth.StartRecording(12f);
            GlobalClock.Play();
            t0 = Time.realtimeSinceStartup;
            bool shot = false;
            while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < 16f)
            {
                if (captures && !shot && Time.realtimeSinceStartup - t0 > 2.5f) { V2Checks.Capture("g5_" + id + ".png"); shot = true; }
                yield return null;
            }
            bool ok1 = Synth.RecordingDone && Synth.SaveRecording(Path.Combine(wavDir, id + "_chorus.wav"));
            Info(id + ": chorus (hook bar " + e.hook + ", column " + chorus + ", " + most + " islands) notes " + TapSummary());
            GlobalClock.Stop();
            yield return null;
            int voice = VoiceInstrument.ContainsKey(id) ? VoiceInstrument[id] : 3;
            bool wasMuted = Instruments.Muted[voice];
            Instruments.SetMuted(voice, true);
            GlobalClock.Seek(hookBeat);
            Synth.StartRecording(12f);
            GlobalClock.Play();
            t0 = Time.realtimeSinceStartup;
            while (!Synth.RecordingDone && Time.realtimeSinceStartup - t0 < 16f) yield return null;
            bool ok2 = Synth.RecordingDone && Synth.SaveRecording(Path.Combine(wavDir, id + "_chorus_novoice.wav"));
            Instruments.SetMuted(voice, wasMuted);
            GlobalClock.Stop();
            Check(id + ": chorus stems recorded (with / without the voice) with late / errors 0", ok1 && ok2 && Synth.LateEvents == late0 && Synth.Errors == err0,
                  "late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0));
        }
        else if (captures)
        {
            GlobalClock.Seek(hookBeat);
            if (OrbitCamera.I != null) OrbitCamera.I.FrameColumn(chorus, 0.2f);
            GlobalClock.Play();
            yield return new WaitForSecondsRealtime(2.5f);
            V2Checks.Capture("g5_" + id + ".png");
            yield return new WaitForSecondsRealtime(0.6f);
            GlobalClock.Stop();
        }
        Check(id + ": no console error while it opened and played", consoleErrors == errs0, (consoleErrors - errs0) + " errors");
        yield return null;
    }
}
