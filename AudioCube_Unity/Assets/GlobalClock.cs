using System;
using UnityEngine;

/// <summary>
/// The single musical clock. Beat position is derived from the audio DSP clock so cubes,
/// metronome and visuals stay locked together. All state is static for easy access.
/// </summary>
public class GlobalClock : MonoBehaviour
{
    public static GlobalClock I;

    public static bool IsPlaying = false;
    public static float BPM = 100f;
    public static int BeatsPerBar = 4;
    public static float Swing = 0f;          // 0 = straight, 1 = full triplet swing
    public static bool LoopSong = true;
    public static bool Metronome = false;
    /// <summary>Number of loop wraps since the last Stop / Seek(0); part of the per-window pass id (SPEC §2.10).</summary>
    public static int LoopIndex;

    public static double SongBeatD = 0.0;
    public static float SongBeat => (float)SongBeatD;
    public static float TotalBeats = 16f;

    // legacy names kept for older scripts
    public static float CurrentBeat = 0f;
    public static float MasterBeatLength = 4f;

    public static event Action<int, bool> OnBeat;      // (beat index, is downbeat)
    public static event Action OnLoop, OnPlay, OnPause, OnStop, OnTempoChanged, OnMeterChanged, OnSeek;

    static double startDsp;
    static int lastBeatInt = -1;

    [SerializeField] float debugBeat;
    [SerializeField] float debugTotal;
    [SerializeField] float debugBpm;

    AudioSource[] metro = new AudioSource[2];
    int metroIdx;
    double nextMetroBeat;

    public static double DspNow => AudioSettings.dspTime;
    public static double BeatsPerSecond => BPM / 60.0;
    public static double DspTimeOfBeat(double beat) => startDsp + beat / BeatsPerSecond;
    /// <summary>The song beat right now from the DSP clock (SongBeatD is refreshed once per frame and lags it by up to a frame plus any main-thread stall).</summary>
    public static double BeatNow => IsPlaying ? (DspNow - startDsp) * BeatsPerSecond : SongBeatD;
    // the clock parameters that held before the last SetBPM / SetSwing: OnTempoChanged handlers retime DSP times scheduled under them
    static double prevStartDsp; static double prevBps = 100.0 / 60.0;
    /// <summary>Maps a DSP time scheduled before the last tempo / swing change to the same song beat under the current tempo (identity when only the swing changed).</summary>
    public static double RetimeDsp(double dsp) => DspTimeOfBeat((dsp - prevStartDsp) * prevBps);
    public static float BeatPhase => SongBeatD < 0.0 ? 0f : (float)(SongBeatD - Math.Floor(SongBeatD));
    public static int BeatIndex => (int)Math.Max(0.0, Math.Floor(SongBeatD));
    /// <summary>Play anchors the song slightly ahead of the DSP clock so the very first notes can be scheduled sample-accurately instead of firing late.</summary>
    public const double PlayLatency = 0.12;
    public static int BeatInBar => ((BeatIndex % Math.Max(1, BeatsPerBar)) + BeatsPerBar) % Math.Max(1, BeatsPerBar);
    /// <summary>The next 16th-note grid line (song beats) while playing; the current position when stopped.</summary>
    public static double Next16thBeat() => IsPlaying ? Math.Ceiling(SongBeatD * 4.0) / 4.0 : SongBeatD;
    public static double DspOfNext16th() => DspTimeOfBeat(Next16thBeat());

    public static GlobalClock Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<GlobalClock>();
            if (I == null) I = new GameObject("GlobalClock").AddComponent<GlobalClock>();
        }
        return I;
    }

    void Awake()
    {
        I = this;
        for (int i = 0; i < metro.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0.5f; s.dopplerLevel = 0f;
            metro[i] = s;
        }
    }

    void Update()
    {
        debugBeat = SongBeat; debugTotal = TotalBeats; debugBpm = BPM;
        if (!IsPlaying) return;

        SongBeatD = (DspNow - startDsp) * BeatsPerSecond;
        if (HasRegion && SongBeatD >= RegionEnd)
        {
            // SPEC v4 R4: the focus loop wraps at RegionEnd back to RegionStart (always, whatever LoopSong says)
            double len = RegionEnd - RegionStart;
            double wraps = Math.Max(1.0, Math.Floor((SongBeatD - RegionStart) / len));
            startDsp += wraps * len / BeatsPerSecond;
            SongBeatD -= wraps * len;
            nextMetroBeat -= wraps * len;
            lastBeatInt = -1;
            LoopIndex++;
            OnLoop?.Invoke();
        }
        else if (SongBeatD >= TotalBeats)
        {
            if (LoopSong && TotalBeats > 0f)
            {
                double wraps = Math.Floor(SongBeatD / TotalBeats);
                startDsp += wraps * TotalBeats / BeatsPerSecond;
                SongBeatD -= wraps * TotalBeats;
                nextMetroBeat -= wraps * TotalBeats;
                lastBeatInt = -1;
                LoopIndex++;
                OnLoop?.Invoke();
            }
            else { Stop(); return; }
        }
        int b = (int)Math.Floor(SongBeatD);
        if (b != lastBeatInt)
        {
            lastBeatInt = b;
            OnBeat?.Invoke(b, BeatsPerBar > 0 && b % BeatsPerBar == 0);
        }
        ScheduleMetronome();
        CurrentBeat = SongBeat;
    }

    public static void Play()
    {
        Ensure();
        if (IsPlaying) return;
        if (SongBeatD >= TotalBeats) SongBeatD = 0.0;
        if (HasRegion && (SongBeatD < RegionStart - 1e-9 || SongBeatD >= RegionEnd)) { SongBeatD = RegionStart; CurrentBeat = (float)SongBeatD; }   // SPEC v4 R4: play resumes inside the focus loop
        startDsp = DspNow + PlayLatency - SongBeatD / BeatsPerSecond;
        IsPlaying = true;
        lastBeatInt = -1;
        I.nextMetroBeat = Math.Ceiling(SongBeatD - 1e-6);
        NoteStart();
        OnPlay?.Invoke();
    }

    public static void Pause()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        if (I != null) I.StopMetro();
        OnPause?.Invoke();
    }

    public static void Stop()
    {
        IsPlaying = false;
        SongBeatD = 0.0; CurrentBeat = 0f;
        LoopIndex = 0;
        lastBeatInt = -1;
        if (I != null) I.StopMetro();
        OnStop?.Invoke();
    }

    // ---- v4 loop region (SPEC v4 §2.4 / §4 R4: the focus loop)
    /// <summary>True while a loop region is set (FocusLoop): the clock wraps at <see cref="RegionEnd"/> back to <see cref="RegionStart"/>
    /// (LoopIndex++ and OnLoop, as at the song end), Play resumes inside it, and the cubes' lookahead, the Moon windows and the previews wrap
    /// with it. Seek is not clamped: a playhead before the region plays into it, one past its end wraps at once.</summary>
    public static bool HasRegion { get; private set; }
    public static double RegionStart { get; private set; }
    public static double RegionEnd { get; private set; }
    /// <summary>Raised when the region is set, moved or cleared (the cubes re-decide their queued notes).</summary>
    public static event Action OnRegionChanged;
    /// <summary>Where the clock wraps from and to: the region while one is set, else the whole song.</summary>
    public static double LoopStartBeat => HasRegion ? RegionStart : 0.0;
    public static double LoopEndBeat => HasRegion ? RegionEnd : TotalBeats;
    /// <summary>Beats of one pass of the loop: the region's length while one is set, else the song's.</summary>
    public static double LoopLengthBeats => LoopEndBeat - LoopStartBeat;
    /// <summary>True when <paramref name="beat"/> lies inside the region (always true without one).</summary>
    public static bool InRegion(double beat) => !HasRegion || (beat >= RegionStart - 1e-9 && beat < RegionEnd);

    /// <summary>Sets (or moves) the loop region to [start, end) song beats; an empty span clears it. No event when nothing changes.</summary>
    public static void SetRegion(double start, double end)
    {
        start = Math.Max(0.0, start);
        if (end <= start + 1e-6) { ClearRegion(); return; }
        if (HasRegion && Math.Abs(start - RegionStart) < 1e-9 && Math.Abs(end - RegionEnd) < 1e-9) return;
        HasRegion = true; RegionStart = start; RegionEnd = end;
        OnRegionChanged?.Invoke();
    }

    public static void ClearRegion() { if (!HasRegion) return; HasRegion = false; OnRegionChanged?.Invoke(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegionStatics() { HasRegion = false; RegionStart = 0.0; RegionEnd = 0.0; pickVersion = 0; pickAtStart = 0; selAtStart = -1; PlayedFromGrid = 0; }

    /// <summary>Play / pause (the play button, Space). A grid picked since play last started (UIManager.SelectedMeasure — a click on it, even
    /// while the song plays, or the same grid clicked again) is where play starts: the clock jumps to its column's start; with no new pick play
    /// resumes where it paused.</summary>
    public static void Toggle()
    {
        if (IsPlaying) { Pause(); return; }
        int sel = PickedGrid();
        if (sel >= 0 && (pickVersion != pickAtStart || sel != selAtStart))
        {
            var kb = SongManager.I.Islands[sel];
            if (kb != null && kb.column >= 0) { Seek(SongManager.I.ColumnStart(kb.column)); PlayedFromGrid++; }
        }
        Play();
    }

    /// <summary>UIManager.SelectMeasure calls this on every grid pick (a click, the tray, the rail, a new grid): the next Toggle that starts play
    /// starts at that grid.</summary>
    public static void NotePick() { pickVersion++; }
    static int pickVersion, pickAtStart, selAtStart = -1;
    static void NoteStart() { pickAtStart = pickVersion; selAtStart = PickedGrid(); }
    /// <summary>Plays started from a picked grid (tests).</summary>
    public static int PlayedFromGrid { get; private set; }
    static int PickedGrid()
    {
        var ui = UIManager.I; var sm = SongManager.I;
        if (ui == null || sm == null || !sm.HasSong) return -1;
        int i = ui.SelectedMeasure;
        return i >= 0 && i < sm.Islands.Count ? i : -1;
    }

    /// <summary>Phase-preserving tempo change: the new tempo continues from the beat the DSP clock is at right now (not the frame-start
    /// SongBeatD, which would rewind the song by the time elapsed inside the frame on every dial event and re-fire the steps in that gap).</summary>
    public static void SetBPM(float bpm)
    {
        bpm = Mathf.Clamp(bpm, 40f, 240f);
        if (Mathf.Approximately(bpm, BPM)) return;
        prevStartDsp = startDsp; prevBps = BeatsPerSecond;
        if (IsPlaying)
        {
            double beatNow = (DspNow - startDsp) * BeatsPerSecond;
            SongBeatD = beatNow; CurrentBeat = (float)beatNow;
            startDsp = DspNow - beatNow / (bpm / 60.0);
        }
        BPM = bpm;
        OnTempoChanged?.Invoke();
    }

    public static void SetBeatsPerBar(int n)
    {
        n = Mathf.Clamp(n, 2, 8);
        if (n == BeatsPerBar) return;
        BeatsPerBar = n;
        OnMeterChanged?.Invoke();
    }

    public static void SetSwing(float s)
    {
        s = Mathf.Clamp01(s);
        if (Mathf.Approximately(s, Swing)) return;
        prevStartDsp = startDsp; prevBps = BeatsPerSecond;
        Swing = s;
        OnTempoChanged?.Invoke();
    }

    public static void SetMetronome(bool on)
    {
        Metronome = on;
        if (I != null) { I.nextMetroBeat = Math.Ceiling(SongBeatD - 1e-6); if (!on) I.StopMetro(); }
    }

    public static void Seek(double beat)
    {
        beat = Math.Max(0.0, Math.Min(beat, Math.Max(0.0, TotalBeats - 1e-4)));
        SongBeatD = beat; CurrentBeat = (float)beat;
        if (beat <= 1e-9) LoopIndex = 0;
        if (IsPlaying) startDsp = DspNow - beat / BeatsPerSecond;
        lastBeatInt = -1;
        if (I != null) { I.nextMetroBeat = Math.Ceiling(beat - 1e-6); I.StopMetro(); }
        OnSeek?.Invoke();
    }

    void ScheduleMetronome()
    {
        if (!Metronome) return;
        const double lookahead = 0.16;
        int guard = 0;
        while (guard++ < 8)
        {
            double t = DspTimeOfBeat(nextMetroBeat);
            if (t > DspNow + lookahead) break;
            bool allowed = LoopSong || nextMetroBeat < TotalBeats - 1e-6;
            if (allowed && t >= DspNow - 0.03)
            {
                int bi = (int)Math.Round(nextMetroBeat);
                bool down = BeatsPerBar > 0 && ((bi % BeatsPerBar) + BeatsPerBar) % BeatsPerBar == 0;
                var s = metro[metroIdx++ % metro.Length];
                s.Stop();
                s.clip = ProceduralAudio.Click(down);
                s.volume = down ? 0.55f : 0.36f;
                s.PlayScheduled(Math.Max(t, DspNow));
            }
            nextMetroBeat += 1.0;
        }
    }

    void StopMetro() { foreach (var s in metro) if (s != null) s.Stop(); }

    public static void ResetClock() { Stop(); MasterBeatLength = 4f; }
}
