using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The performance layer (SPEC §3.7): hold punch-ins (stutter, funnel, half-time, drop), the spotlight, the
/// dispatched-note ring buffer and the Tone/Space dials. Press and release are quantised to the next 16th while
/// playing (applied from <see cref="Tick"/>, which SequenceMaster.Update calls every frame) and immediate when stopped.
/// Stutter: the events of the last 16th before the engage tick are re-issued on every 16th while held (owner 8);
/// cubes' normal notes are dropped by VoiceRules.Dispatch while <see cref="StutterActive"/>.
/// </summary>
public static class Performance
{
    public static bool StutterActive;
    public static AudioCube SpotlightCube;
    public static bool DropActive => dropHeld;
    public static bool HalfActive => halfHeld;
    /// <summary>The song's tempo as set: while the half-time punch-in is held GlobalClock.BPM is the halved value, so snapshots and saves read this.</summary>
    public static float NominalBpm => halfHeld ? storedBpm : GlobalClock.BPM;
    /// <summary>Sets the nominal tempo (loads, undo, new songs): while half-time is held the clock gets the halved value and the release restores <paramref name="bpm"/>.</summary>
    public static void SetNominalBpm(float bpm)
    {
        bpm = Mathf.Clamp(bpm, 40f, 240f);
        if (halfHeld) { storedBpm = bpm; GlobalClock.SetBPM(bpm * 0.5f); }
        else GlobalClock.SetBPM(bpm);
    }
    public static bool FunnelActive => funnelHeld;
    /// <summary>True from the engage tick to the release tick (the HUD may light the button at the press).</summary>
    public static bool StutterHeld => stutterHeld;
    /// <summary>Punch-ins waiting for their 16th.</summary>
    public static int PendingCount => pending.Count;

    // ---- log (the last 1024 dispatched cube events: the stutter buffer and tests)
    const int LogSize = 1024;
    static readonly VoiceRules.NoteEvent[] log = new VoiceRules.NoteEvent[LogSize];
    static int logHead, logCount; static long logTotal;

    /// <summary>Called by VoiceRules.Dispatch for every cube event that reached the synth.</summary>
    public static void Log(VoiceRules.NoteEvent e)
    {
        log[logHead] = e;
        logHead = (logHead + 1) % LogSize;
        if (logCount < LogSize) logCount++;
        logTotal++;
    }
    public static int LoggedCount => logCount;
    /// <summary>Every cube event ever logged (monotonic; tests).</summary>
    public static long LoggedTotal => logTotal;
    /// <summary>Logged event i (0 = oldest still in the buffer).</summary>
    public static VoiceRules.NoteEvent Logged(int i)
    {
        if (logCount == 0) return new VoiceRules.NoteEvent();
        i = Mathf.Clamp(i, 0, logCount - 1);
        return log[((logHead - logCount + i) % LogSize + LogSize) % LogSize];
    }

    // ---- quantised holds
    const int KindStutter = 0, KindFunnel = 1, KindHalf = 2, KindDrop = 3, KindSpot = 4;
    const double StutterLead = 0.15;      // the stutter engages/releases this early so its first/last ticks are queued ahead of time
    struct Pending { public int kind; public bool on; public double dsp; public AudioCube cube; }
    static readonly List<Pending> pending = new List<Pending>();
    static readonly bool[] held = new bool[5];

    public static void HoldStutter(bool on) { Request(KindStutter, on, null); }
    public static void HoldFunnel(bool on) { Request(KindFunnel, on, null); }
    public static void HoldHalf(bool on) { Request(KindHalf, on, null); }
    public static void HoldDrop(bool on) { Request(KindDrop, on, null); }
    public static void HoldSpotlight(AudioCube c, bool on) { Request(KindSpot, on, c); }

    static void Request(int kind, bool on, AudioCube cube)
    {
        // a press and its release inside one 16th cancel out; a re-press cancels a pending release
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].kind != kind) continue;
            bool was = pending[i].on;
            pending.RemoveAt(i);
            if (was != on) return;
        }
        if (held[kind] == on && kind != KindSpot) return;
        if (!GlobalClock.IsPlaying) { Apply(kind, on, cube, GlobalClock.DspNow); return; }
        double lead = kind == KindStutter ? StutterLead : 0.0;
        pending.Add(new Pending { kind = kind, on = on, dsp = Next16thDsp(lead + 0.02), cube = cube });
    }

    /// <summary>The DSP time of the next 16th at least <paramref name="minAhead"/> seconds from now, from the DSP clock itself
    /// (GlobalClock.SongBeatD is only refreshed once per frame, so after a main-thread stall it can lag the DSP clock).</summary>
    static double Next16thDsp(double minAhead)
    {
        double now = GlobalClock.DspNow, bps = GlobalClock.BeatsPerSecond;
        double beatNow = (now - GlobalClock.DspTimeOfBeat(0.0)) * bps;
        double t = GlobalClock.DspTimeOfBeat(Math.Ceiling(beatNow * 4.0 - 1e-6) / 4.0);
        double one16 = 0.25 / bps;
        int guard = 0;
        while (t < now + minAhead && guard++ < 64) t += one16;
        return t;
    }

    static void Apply(int kind, bool on, AudioCube cube, double dsp)
    {
        held[kind] = on;
        switch (kind)
        {
            case KindStutter: if (on) EngageStutter(dsp); else ReleaseStutter(dsp); break;
            case KindFunnel: ApplyFunnel(on); break;
            case KindHalf:
                if (on == halfHeld) break;
                halfHeld = on;
                if (on) { storedBpm = GlobalClock.BPM; GlobalClock.SetBPM(storedBpm * 0.5f); }
                else GlobalClock.SetBPM(storedBpm);
                break;
            case KindDrop: ApplyDrop(on); break;
            case KindSpot:
                if (on) SpotlightCube = cube;
                else if (SpotlightCube == cube || cube == null) SpotlightCube = null;
                break;
        }
    }

    /// <summary>Per-frame: applies due punch-ins, issues stutter ticks, ramps the funnel (called from SequenceMaster.Update).</summary>
    public static void Tick()
    {
        double now = GlobalClock.DspNow;
        bool playing = GlobalClock.IsPlaying;
        for (int i = 0; i < pending.Count; i++)
        {
            var p = pending[i];
            double lead = p.kind == KindStutter ? StutterLead : 0.0;
            if (!playing || now >= p.dsp - lead) { pending.RemoveAt(i); i--; Apply(p.kind, p.on, p.cube, playing ? p.dsp : now); }
        }
        if (!playing && (stutterHeld || StutterActive)) { ReleaseStutter(now); Synth.CancelOwner(VoiceRules.OwnerStutter); held[KindStutter] = false; stutterCancelAt = -1.0; }
        TickStutter(now);
        TickFunnel();
    }

    // ---- stutter: re-issue the last 16th's events every 16th (owner 8) while held
    static bool stutterHeld; static double stutterTickDsp, stutterCancelAt = -1.0;
    static readonly List<VoiceRules.NoteEvent> captured = new List<VoiceRules.NoteEvent>();
    static readonly VoiceRules.NoteEvent[] tickBuf = new VoiceRules.NoteEvent[64];
    /// <summary>Events captured by the last engage (tests).</summary>
    public static int StutterCapturedCount => captured.Count;
    /// <summary>DSP times of the last engage / release ticks (tests); -1 before the first.</summary>
    public static double StutterEngagedAtDsp { get; private set; } = -1.0;
    public static double StutterReleasedAtDsp { get; private set; } = -1.0;
    /// <summary>LoggedTotal at the last engage: no cube event may be logged between the engage and the release (tests).</summary>
    public static long StutterLogTotalAtEngage { get; private set; }

    /// <summary>Captures the logged events with onDsp in [from, to); with <paramref name="openAt"/> only pitched events whose window is still open then (drums always).</summary>
    static void CaptureSlice(double from, double to, double openAt = double.NegativeInfinity)
    {
        for (int i = 0; i < logCount; i++)
        {
            var e = Logged(i);
            if (e.onDsp < from || e.onDsp >= to) continue;
            if (e.winEndDsp > 0.0 && !SynthBank.Def(e.slot).drums && e.winEndDsp <= openAt) continue;
            captured.Add(e);
        }
    }

    static void EngageStutter(double t)
    {
        double one16 = 0.25 / GlobalClock.BeatsPerSecond;
        captured.Clear();
        CaptureSlice(t - one16, t);
        if (captured.Count == 0)
        {
            // nothing in the last 16th: the most recent 16th that had events (the buffer holds about two bars)
            double latest = double.NegativeInfinity;
            for (int i = 0; i < logCount; i++) { var e = Logged(i); if (e.onDsp < t && e.onDsp > latest) latest = e.onDsp; }
            if (!double.IsInfinity(latest)) CaptureSlice(latest - 0.5 * one16, latest + 0.5 * one16, t);
        }
        stutterHeld = true; StutterActive = true; stutterTickDsp = t; stutterCancelAt = -1.0; StutterEngagedAtDsp = t; StutterLogTotalAtEngage = logTotal;
        Synth.CancelPendingAfter(t);            // the cubes' notes from the engage tick on are the stutter's now
    }

    static void ReleaseStutter(double t)
    {
        stutterHeld = false; StutterActive = false;
        stutterCancelAt = t; StutterReleasedAtDsp = t;
        AudioCube.ResumeAll(t);                 // cubes schedule again from the release tick
    }

    static void TickStutter(double now)
    {
        if (stutterCancelAt > 0.0 && now >= stutterCancelAt) { Synth.CancelOwner(VoiceRules.OwnerStutter); stutterCancelAt = -1.0; }
        if (!stutterHeld || !GlobalClock.IsPlaying) return;
        int guard = 0;
        while (stutterTickDsp < now + StutterLead && guard++ < 32)
        {
            double one16 = 0.25 / GlobalClock.BeatsPerSecond;
            if (stutterTickDsp >= now - 0.02 && captured.Count > 0)
            {
                int n = 0;
                for (int i = 0; i < captured.Count && n < tickBuf.Length; i++)
                {
                    var e = captured[i];
                    // a pitched event belongs to its window (SPEC 2.9): once the tick has passed that window's end it stays silent; drums keep ticking
                    if (e.winEndDsp > 0.0 && !SynthBank.Def(e.slot).drums && stutterTickDsp >= e.winEndDsp - 0.005) continue;
                    tickBuf[n++] = new VoiceRules.NoteEvent { slot = e.slot, midi = e.midi, vel = Mathf.Clamp(Mathf.RoundToInt(e.vel * 0.85f), 1, 127), onDsp = stutterTickDsp, offDsp = stutterTickDsp + 0.6 * one16 };
                }
                if (n > 0) VoiceRules.Dispatch(tickBuf, n, VoiceRules.OwnerStutter);
            }
            stutterTickDsp += one16;
        }
    }

    // ---- funnel: master cutoff 1 -> 0.15 over one bar while held; on release held until the next downbeat, then back over 60 ms
    static bool funnelHeld; static float funnelT; static double funnelHoldUntilDsp = -1.0;

    static void ApplyFunnel(bool on)
    {
        if (on == funnelHeld) return;
        funnelHeld = on;
        if (on) { funnelHoldUntilDsp = -1.0; return; }
        if (GlobalClock.IsPlaying)
        {
            int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
            double nextDown = (Math.Floor(GlobalClock.SongBeatD / bpb) + 1) * bpb;
            funnelHoldUntilDsp = GlobalClock.DspTimeOfBeat(nextDown);
        }
        else funnelHoldUntilDsp = -1.0;
    }

    static void TickFunnel()
    {
        if (!funnelHeld && funnelT <= 0f) return;
        float dt = Time.unscaledDeltaTime;
        if (funnelHeld)
        {
            float barSec = (float)(Mathf.Max(1, GlobalClock.BeatsPerBar) / GlobalClock.BeatsPerSecond);
            funnelT = Mathf.Min(1f, funnelT + dt / Mathf.Max(0.05f, barSec));
        }
        else if (funnelHoldUntilDsp > 0.0 && GlobalClock.DspNow < funnelHoldUntilDsp && GlobalClock.IsPlaying) { /* hold the value until the downbeat */ }
        else funnelT = Mathf.Max(0f, funnelT - dt / 0.06f);
        Synth.SetMasterCutoff01(Mathf.Lerp(SongManager.Tone, 0.15f, funnelT));
    }

    // ---- half-time: exact BPM restore
    static bool halfHeld; static float storedBpm;

    // ---- drop: only Bass and Drums audible (temporary slot gains; Instruments.SetVolume defers while held)
    static bool dropHeld;

    static void ApplyDrop(bool on)
    {
        if (on == dropHeld) return;
        dropHeld = on;
        if (!Synth.Ready) return;
        if (on)
        {
            for (int slot = 0; slot < SynthBank.Slots; slot++)
            {
                if (SynthBank.GroupOf(slot) == 4 || SynthBank.Defs[slot].drums) continue;   // v6: every bass voice stays
                Synth.SetSlotGain(slot, 0f);
            }
        }
        else Instruments.PushToSynth();
    }

    // ---- song dials
    public static void SetTone(float t01) { SongManager.SetTone(t01); }
    public static void SetSpace(float s) { SongManager.SetSpace(s); }
}
