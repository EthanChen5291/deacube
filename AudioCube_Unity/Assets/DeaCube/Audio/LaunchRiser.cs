using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 (A, SPEC v7 §2.8 / §4.4 — "cube getting flungs as a transition animation (33- 35) where the cubes play and then from their last location, get
/// flung upwards and to the right to the next grid"): the LAUNCH's sound, the production trick every pop transition uses. A RISER — GeneralUser GS's
/// "Reverse Cymbal" on the song bus's effects channel (SynthBank.FxChannel) — swells through the launching island's last bar and PEAKS exactly on the
/// landing beat (SongManager.TurnEnd: where the flung cubes land on the next grid), and a soft crash (VoiceRules.ScheduleLandingCrash, never doubling
/// a fill crash) hits on that beat. Once per launch per pass of the song (islands landing on the same beat share one riser); only when the island is
/// audible (awake, a cube of it sounds: not muted, its group not muted) and the music goes on after the landing (a next grid, or the song / the focus
/// loop wraps — never a riser into the song's final silence). SPEC v7 §21 ("just keep cubes on each grid"): the launch is ONLY this sound — no cube
/// is flung, so nothing here reads flights, fling targets or carried windows (audibility looks at the island's own cubes). Never late: the swell's
/// peak time comes from rendering the preset (Synth.RiserPeakSeconds), the note-on is queued ahead like the cubes' notes (only while it is still at
/// least a few ms away), and when a seek / a play start / a rebuild leaves less time than the planned swell a shorter (faster) swell is chosen — or,
/// under <see cref="MinSeconds"/>, none (the crash still lands). A stop, pause, seek, tempo change or focus-loop change cancels every queued or
/// sounding riser; a changed plan (launch off, the island asleep, the landing moved by an edit) cancels its riser. Ticked by SequenceMaster.
/// </summary>
public static class LaunchRiser
{
    /// <summary>Master switch (tests / A-B).</summary>
    public static bool Enabled = true;
    /// <summary>Tests: false plans and crashes as usual but sends no riser note (the crash heard alone).</summary>
    public static bool SoundRiser = true;
    /// <summary>The swell's planned length: one bar of the song (the classic riser), at least <see cref="LongSeconds"/>, at most <see cref="MaxSeconds"/>.</summary>
    public const float Bars = 1f;
    public const double LongSeconds = 1.0, MaxSeconds = 2.8;
    /// <summary>The shortest swell worth playing (a late start after a seek): under it only the crash lands.</summary>
    public const double MinSeconds = 0.5;
    /// <summary>The riser's key range (the key sets the swell's speed: 36 ≈ 2.8 s, 60 ≈ 1.4 s, 96 ≈ 0.5 s with GeneralUser GS).</summary>
    public const int MinKey = 36, MaxKey = 96;
    /// <summary>The riser's velocity and the landing crash's (softer than the fill crash's 96).</summary>
    public static int Velocity = 112, CrashVelocity = 84;
    /// <summary>How far ahead the riser note-on and the crash are queued (the cubes' lookahead), and the least time a note-on may be queued before it
    /// sounds (never late).</summary>
    public const double Lookahead = 0.22, Margin = 0.06;
    /// <summary>The riser's note-off after the landing (the sample stops dead a few ms after its peak; this only closes the note).</summary>
    public const double Tail = 0.06;
    /// <summary>The synth owners of the risers (a small pool: consecutive launches never cancel each other).</summary>
    public const int OwnerBase = 1900000071, OwnerPool = 4;

    /// <summary>One planned landing: its DSP time and song beat, the swell (key, peak, note-on), the island that launches, the state.</summary>
    public struct Plan
    {
        public double landingDsp, onDsp, peak; public float landingBeat; public int key, owner; public string island;
        public bool sent, crashed, crashSent, skipped, cancelled; public double plannedAt;
    }
    static readonly List<Plan> plans = new List<Plan>();
    /// <summary>Tests: every plan made since <see cref="ResetLog"/> (sent / skipped / cancelled marked), newest last.</summary>
    public static readonly List<Plan> Log = new List<Plan>();
    /// <summary>Tests: risers queued, swells skipped (no time left), plans cancelled, landing crashes queued (and ones a fill crash already covered).</summary>
    public static int Sent, Skipped, Cancelled, Crashes, CrashShared, Late;
    public static void ResetLog() { Log.Clear(); Sent = 0; Skipped = 0; Cancelled = 0; Crashes = 0; CrashShared = 0; Late = 0; }
    static int nextOwner;
    static bool hooked;
    static readonly List<double> landings = new List<double>();
    static readonly List<float> landingBeats = new List<float>();
    static readonly List<string> landingIslands = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Unhook();
        plans.Clear(); Log.Clear(); Sent = 0; Skipped = 0; Cancelled = 0; Crashes = 0; CrashShared = 0; Late = 0; nextOwner = 0; Enabled = true; SoundRiser = true;
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        GlobalClock.OnStop += CancelAll; GlobalClock.OnPause += CancelAll; GlobalClock.OnSeek += CancelAll;
        GlobalClock.OnTempoChanged += CancelAll; GlobalClock.OnRegionChanged += CancelAll;
    }
    static void Unhook()
    {
        GlobalClock.OnStop -= CancelAll; GlobalClock.OnPause -= CancelAll; GlobalClock.OnSeek -= CancelAll;
        GlobalClock.OnTempoChanged -= CancelAll; GlobalClock.OnRegionChanged -= CancelAll;
        hooked = false;
    }

    /// <summary>Drops every plan and silences every queued / sounding riser (a stop, pause, seek, tempo or focus-loop change: the landings moved).</summary>
    public static void CancelAll()
    {
        double now = Synth.DspNow;
        for (int i = 0; i < plans.Count; i++)
        {
            var p = plans[i];
            if (p.sent && !p.cancelled && p.landingDsp > now) { Synth.CancelOwner(p.owner); Cancelled++; MarkLog(p, true); }   // landed ones already played
        }
        plans.Clear();
    }

    /// <summary>The planned landings right now (tests): DSP time, song beat and state of each.</summary>
    public static List<Plan> Plans => new List<Plan>(plans);

    /// <summary>The swell's planned length at the current tempo: one bar, clamped to <see cref="LongSeconds"/>..<see cref="MaxSeconds"/>.</summary>
    public static double PlannedSeconds()
    {
        double bar = Bars * Mathf.Max(1, GlobalClock.BeatsPerBar) / GlobalClock.BeatsPerSecond;
        return Math.Max(LongSeconds, Math.Min(MaxSeconds, bar));
    }

    /// <summary>True when <paramref name="kb"/>'s launch is heard: awake, and one of its cubes sounds (not muted, its group not muted, some volume).</summary>
    public static bool Audible(KeyBlock kb)
    {
        if (kb == null || kb.sleep || kb.IsMoon) return false;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.IsOnMoon || c.muted || c.nodes.Count == 0 || c.volume <= 0.01f || c.Island != kb) continue;
            int g = Mathf.Clamp(c.instrument, 0, Instruments.Count - 1);
            if (Instruments.Muted[g] || Instruments.Volume[g] <= 0.01f) continue;
            return true;
        }
        return false;
    }

    /// <summary>Every frame (SequenceMaster): plan the landings ahead, queue the risers and crashes that are due, cancel plans that no longer hold.</summary>
    public static void Tick()
    {
        Hook();
        if (!GlobalClock.IsPlaying) { if (plans.Count > 0) CancelAll(); return; }
        var sm = SongManager.I;
        if (!Enabled || sm == null || !sm.HasSong || !Synth.Ready) { if (plans.Count > 0) CancelAll(); return; }
        double now = Synth.DspNow;
        double horizon = MaxSeconds + Lookahead + 0.3;
        CollectLandings(sm, now, horizon);

        // plans whose landing no longer holds (launch off, asleep, the landing moved): cancel their riser; landed ones retire
        for (int i = plans.Count - 1; i >= 0; i--)
        {
            var p = plans[i];
            if (p.landingDsp < now - 0.5) { plans.RemoveAt(i); continue; }
            if (p.landingDsp <= now) continue;
            if (IndexOfLanding(p.landingDsp) >= 0) continue;
            if (p.sent) { Synth.CancelOwner(p.owner); Cancelled++; }
            MarkLog(p, true);
            plans.RemoveAt(i);
        }
        // new landings: plan the swell (the planned length when it fits, else the longest that still fits, else none)
        for (int j = 0; j < landings.Count; j++)
        {
            double L = landings[j];
            if (PlanOf(L) >= 0 || L <= now + Margin) continue;
            var p = new Plan { landingDsp = L, landingBeat = landingBeats[j], island = landingIslands[j], plannedAt = now, owner = OwnerBase + (nextOwner++ % OwnerPool) };
            Fit(ref p, now, PlannedSeconds(), false);
            plans.Add(p); Log.Add(p);
            if (p.skipped) Skipped++;
        }
        // due: queue the riser (while its note-on is still ahead) and the crash
        for (int i = 0; i < plans.Count; i++)
        {
            var p = plans[i];
            bool changed = false;
            if (!p.sent && !p.skipped && p.onDsp - now <= Lookahead)
            {
                if (p.onDsp < now + Margin)
                {
                    // a stalled frame ate the margin: a shorter swell from here, or none — never a late note
                    Fit(ref p, now, p.landingDsp - now - Margin, true);
                    if (p.skipped) { Skipped++; Late++; }
                }
                if (!p.skipped && !SoundRiser) p.skipped = true;   // tests: the crash alone
                else if (!p.skipped)
                {
                    Synth.RiserNote(p.key, Velocity, p.onDsp, p.landingDsp + Tail, p.owner);
                    p.sent = true; Sent++;
                }
                changed = true;
            }
            if (!p.crashed && p.landingDsp - now <= Lookahead)
            {
                if (p.landingDsp >= now + Margin)
                {
                    p.crashSent = VoiceRules.ScheduleLandingCrash(p.landingDsp, CrashVelocity);
                    if (p.crashSent) Crashes++; else CrashShared++;
                }
                p.crashed = true; changed = true;
            }
            if (changed) { plans[i] = p; MarkLog(p, false); }
        }
    }

    /// <summary>Picks the swell for plan <paramref name="p"/>: <paramref name="seconds"/> long (the nearest key; <paramref name="notLonger"/>: the longest
    /// that is not longer), shortened to what is left before the landing (minus the margin and the lookahead's slack), skipped under MinSeconds.</summary>
    static void Fit(ref Plan p, double now, double seconds, bool notLonger)
    {
        double left = p.landingDsp - now - Margin;
        int key = Synth.RiserKeyFor(seconds, MinKey, MaxKey, notLonger);
        double peak = Synth.RiserPeakSeconds(key);
        if (peak > left)
        {
            key = Synth.RiserKeyFor(left, MinKey, MaxKey, true);
            peak = Synth.RiserPeakSeconds(key);
        }
        p.skipped = left < MinSeconds || peak > left || peak < MinSeconds * 0.95;
        p.key = key; p.peak = peak; p.onDsp = p.landingDsp - peak;
    }

    static int PlanOf(double landingDsp) { for (int i = 0; i < plans.Count; i++) if (Math.Abs(plans[i].landingDsp - landingDsp) < 1e-3) return i; return -1; }
    static int IndexOfLanding(double dsp) { for (int i = 0; i < landings.Count; i++) if (Math.Abs(landings[i] - dsp) < 1e-3) return i; return -1; }

    static void MarkLog(Plan p, bool cancelled)
    {
        p.cancelled = cancelled;
        for (int i = Log.Count - 1; i >= 0; i--) if (Math.Abs(Log[i].landingDsp - p.landingDsp) < 1e-3 && Log[i].owner == p.owner) { Log[i] = p; return; }
    }

    /// <summary>The landings within <paramref name="horizon"/> seconds: every launching, audible island's TurnEnd in this pass of the loop and, when the
    /// song (or the focus loop) wraps, in the next one — one entry per landing time.</summary>
    static void CollectLandings(SongManager sm, double now, double horizon)
    {
        landings.Clear(); landingBeats.Clear(); landingIslands.Clear();
        double bps = GlobalClock.BeatsPerSecond;
        if (bps <= 0.0) return;
        double b0 = GlobalClock.BeatNow;
        double loopStart = GlobalClock.LoopStartBeat, loopEnd = GlobalClock.LoopEndBeat;
        bool wraps = GlobalClock.HasRegion || GlobalClock.LoopSong;
        double wrapDsp = GlobalClock.DspTimeOfBeat(loopEnd);
        foreach (var kb in sm.Islands)
        {
            if (kb == null || !kb.launch || kb.IsMoon || kb.IsStairs) continue;
            float L = sm.TurnEnd(kb);
            if (L <= loopStart + 1e-4 || L > loopEnd + 1e-4) continue;   // outside the loop: never reached
            if (L >= loopEnd - 1e-4 && !wraps) continue;                   // §21: the landing is the song's end and nothing follows it: no build-up into silence
            if (!Audible(kb)) continue;
            if (L > b0 + 1e-6) Add(GlobalClock.DspTimeOfBeat(L), L, kb, now, horizon);
            if (wraps) Add(wrapDsp + (L - loopStart) / bps, L, kb, now, horizon);
        }
    }

    static void Add(double dsp, float beat, KeyBlock kb, double now, double horizon)
    {
        if (dsp <= now || dsp - now > horizon || IndexOfLanding(dsp) >= 0) return;
        landings.Add(dsp); landingBeats.Add(beat); landingIslands.Add(kb.assignedChord + " #" + kb.measureIndex);
    }
}
