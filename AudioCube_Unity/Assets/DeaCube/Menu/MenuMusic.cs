using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The menu's sound (SPEC v3 §5.1) on its own 90 BPM clock, in A minor: the title plays its own name while its letters rise
/// (D E A C, a soft claves tick for U, B E — Bells over a quiet Keys double), then a quiet four-chord Pad loop starts on the next
/// bar (Am9, Fmaj9, C(add9)/E, G6(add9), two bars each). Touched wall pins play C-major-pentatonic Bells notes by row, quantised
/// to the next 8th (at most two per 8th and eight per second); a click strums the sounding chord on Keys; button hovers and
/// letter hovers play one note. Everything runs through the Synth facade under private owner ids, so <see cref="Stop"/> drops
/// the queue and releases what sounds (the leave's hit uses its own owner and outlives it). Main thread only.
/// v4 (SPEC §7b): leaving plays <see cref="LeaveHit"/> on the impact frame instead of the v3 falling shimmer.
/// v5 (SPEC §4, the gallery): <see cref="Hold"/> pauses the pad loop and the wall's notes while a gallery song previews (queued notes
/// dropped, sounding ones released) and restarts the pad on a fresh bar when released; the leave into a gallery song hits that song's
/// chord (<see cref="LeaveHit(float, int[])"/>).
/// </summary>
public class MenuMusic
{
    public const int OwnerPad = 1900000021, OwnerNotes = 1900000022, OwnerFx = 1900000023;   // ids >= 16, far from the cubes' (instance id + 16) range
    public const float Bpm = 90f;
    public const int PadSlot = 2, BellSlot = 5, KeysSlot = 0;
    public const double Lead = 0.1;                                   // scheduling lead (never late for the audio thread)
    public static readonly int[] Penta = { 0, 2, 4, 7, 9 };           // C D E G A (= A minor pentatonic)
    public const int PentaRoot = 60;                                  // C4
    /// <summary>The title's notes, letter by letter: D E A C (U = tick) B E.</summary>
    public static readonly int[] LetterMidi = { 62, 64, 69, 72, -1, 71, 76 };
    public const int TickKey = 75;                                    // GM claves on the drum slot
    static readonly int[][] Chords =
    {
        new[] { 57, 60, 64, 67, 71 },   // Am9
        new[] { 53, 57, 60, 64, 67 },   // Fmaj9
        new[] { 52, 55, 60, 62, 67 },   // C(add9)/E
        new[] { 55, 59, 62, 64, 69 },   // G6(add9)
    };
    const int PadVelocity = 36;

    struct Pending { public float at; public int slot, midi, vel, owner; public float dur; }
    readonly List<Pending> pending = new List<Pending>();
    readonly Queue<float> recent = new Queue<float>();
    readonly List<int> slotKeys = new List<int>(4);
    double t0; int nextChord; bool running; double lastSlot = -1; float lastStrum = -9f;

    public bool Running => running;
    /// <summary>v5: the loop is paused for a gallery preview (<see cref="Hold"/>).</summary>
    public bool Held { get; private set; }
    /// <summary>v5: holds begun / released this session (tests).</summary>
    public int Holds { get; private set; }
    public int Releases { get; private set; }
    public int NotesScheduled { get; private set; }
    public int NotesDropped { get; private set; }
    public int ChordsScheduled { get; private set; }
    public int NameNotes { get; private set; }
    public int LetterNotes { get; private set; }
    public int MaxPerSecond { get; private set; }
    /// <summary>Leave hits scheduled (tests).</summary>
    public int LeaveHits { get; private set; }
    /// <summary>DSP time of beat 0 of the pad / quantise clock.</summary>
    public double ClockStart => t0;
    public static double EighthSeconds => 30.0 / Bpm;

    public static int PentaMidi(int k) { k = Mathf.Max(0, k); return PentaRoot + 12 * (k / 5) + Penta[k % 5]; }
    /// <summary>Index of the chord sounding now (0..3).</summary>
    public int ChordNow { get { double beats = (AudioSettings.dspTime - t0) * Bpm / 60.0; return beats < 0 ? 0 : ((int)Math.Floor(beats / 8.0)) % Chords.Length; } }

    public void Start()
    {
        if (running) return;
        running = true;
        Held = false;
        t0 = AudioSettings.dspTime + 0.35;
        nextChord = 0; lastSlot = -1; slotKeys.Clear(); recent.Clear(); pending.Clear();
        NotesScheduled = 0; NotesDropped = 0; ChordsScheduled = 0; MaxPerSecond = 0; NameNotes = 0;
    }

    /// <summary>Cancels every queued menu note and releases the sounding ones (the pad fades out on its own release).</summary>
    public void Stop()
    {
        pending.RemoveAll(p => p.owner != OwnerFx);
        Held = false;
        if (!running) return;
        running = false;
        Synth.CancelOwner(OwnerPad);
        Synth.CancelOwner(OwnerNotes);
    }

    /// <summary>v5 (the gallery): <paramref name="on"/> pauses the menu's own music for a song preview — the queued pad chords and wall notes
    /// are dropped and the sounding ones released (the pad fades on its release), pins, strums and hovers stay silent; releasing restarts the
    /// pad loop from its first chord on a fresh bar a moment later.</summary>
    public void Hold(bool on)
    {
        if (Held == on) return;
        Held = on;
        if (on)
        {
            Holds++;
            pending.RemoveAll(p => p.owner == OwnerNotes);
            Synth.CancelOwner(OwnerPad);
            Synth.CancelOwner(OwnerNotes);
            return;
        }
        Releases++;
        if (!running) return;
        t0 = AudioSettings.dspTime + 0.25;
        nextChord = 0; lastSlot = -1; slotKeys.Clear();
    }

    /// <summary>The title plays its name: letter k at <paramref name="startDsp"/> + k·<paramref name="step"/>; the pad then begins on
    /// the next bar (its clock restarts one step after the last letter).</summary>
    public void PlayName(double startDsp, double step)
    {
        if (!running) Start();
        if (!Synth.Ready || !Synth.Rendering) return;   // boot: the audio thread is not rendering yet (the name would arrive late)
        double now = AudioSettings.dspTime;
        if (startDsp < now + Lead) startDsp = now + Lead;
        // own owner (OwnerFx): a Stop()/cancel issued in the same frame must not swallow the name; it is short anyway
        for (int k = 0; k < LetterMidi.Length; k++)
        {
            double on = startDsp + k * step;
            if (LetterMidi[k] < 0) { Synth.Note(SynthBank.DrumSlot, TickKey, 40, on, on + 0.12, OwnerFx); NameNotes++; continue; }
            int m = SynthBank.ClampToRegister(BellSlot, LetterMidi[k]);
            Synth.Note(BellSlot, m, 54, on, on + 0.34, OwnerFx);
            Synth.Note(KeysSlot, SynthBank.ClampToRegister(KeysSlot, LetterMidi[k] - 12), 26, on + 0.004, on + 0.3, OwnerFx);
            NameNotes++;
        }
        t0 = startDsp + (LetterMidi.Length + 1) * step;
        nextChord = 0; lastSlot = -1;
    }

    public void Tick()
    {
        if (!running) return;
        if (Held) { TickPendingOnly(); return; }
        double now = AudioSettings.dspTime;
        // boot: until Unity's audio actually calls the synth, hold the pad's clock (a chord queued now would only arrive late)
        if (!Synth.Rendering) { if (nextChord == 0 && t0 < now + 0.35) t0 = now + 0.35; TickPendingOnly(); return; }
        double beat = 60.0 / Bpm, chordLen = 8.0 * beat;
        // pad: each chord is queued ~0.6 s ahead; it ends a hair before the next one starts (shared keys never cut a new note)
        while (Synth.Ready && t0 + nextChord * chordLen - now < 0.6)
        {
            double on = t0 + nextChord * chordLen, off = on + chordLen - 0.02;
            if (on < now + Lead) on = now + Lead;
            var ch = Chords[nextChord % Chords.Length];
            for (int i = 0; i < ch.Length; i++) Synth.Note(PadSlot, ch[i], PadVelocity - i * 2, on + i * 0.012, off, OwnerPad);
            nextChord++; ChordsScheduled++;
        }
        TickPendingOnly();
        float rt = Time.realtimeSinceStartup;
        while (recent.Count > 0 && rt - recent.Peek() > 1f) recent.Dequeue();
    }

    /// <summary>A wall pin was touched: pentatonic degree <paramref name="k"/> (0 = C4, pitch by wall row) on Bells at the next 8th of
    /// the menu clock. Returns false when the 8th already holds two notes (or this key), or eight notes sounded in the last second.</summary>
    public bool RequestNote(int k, float strength01)
    {
        if (!running || Held || !Synth.Ready) return false;
        float rt = Time.realtimeSinceStartup;
        while (recent.Count > 0 && rt - recent.Peek() > 1f) recent.Dequeue();
        if (recent.Count >= 8) { NotesDropped++; return false; }
        double now = AudioSettings.dspTime, e = EighthSeconds;
        double slot = Math.Ceiling((now + Lead - t0) / e);
        if (slot != lastSlot) { lastSlot = slot; slotKeys.Clear(); }
        int midi = SynthBank.ClampToRegister(BellSlot, PentaMidi(k));
        if (slotKeys.Count >= 2 || slotKeys.Contains(midi)) { NotesDropped++; return false; }
        double on = t0 + slot * e;
        int vel = Mathf.RoundToInt(Mathf.Lerp(30f, 48f, Mathf.Clamp01(strength01)));
        Synth.Note(BellSlot, midi, vel, on, on + 0.3, OwnerNotes);   // 0.3 s < one 8th: a same-key note in the next 8th is never cut
        slotKeys.Add(midi);
        recent.Enqueue(rt);
        NotesScheduled++;
        if (recent.Count > MaxPerSecond) MaxPerSecond = recent.Count;
        return true;
    }

    /// <summary>Click: the sounding pad chord strummed upward on Keys (45 ms apart), starting now.</summary>
    public void Strum(float strength01 = 1f)
    {
        float rt = Time.realtimeSinceStartup;
        if (!running || Held || rt - lastStrum < 0.4f) return;
        lastStrum = rt;
        var ch = Chords[ChordNow];
        int vel = Mathf.RoundToInt(Mathf.Lerp(38f, 56f, Mathf.Clamp01(strength01)));
        for (int i = 0; i < ch.Length; i++)
            pending.Add(new Pending { at = rt + i * 0.045f, slot = KeysSlot, midi = SynthBank.ClampToRegister(KeysSlot, ch[i] + 12), vel = vel - i * 2, dur = 0.34f, owner = OwnerNotes });
    }

    /// <summary>Button hover: one soft Bells note, higher for the words further down (v5: five words).</summary>
    public void Hover(int index)
    {
        if (!running || Held) return;
        int[] deg = { 5, 6, 7, 8, 9 };
        int k = deg[Mathf.Clamp(index, 0, deg.Length - 1)];
        pending.Add(new Pending { at = Time.realtimeSinceStartup, slot = BellSlot, midi = SynthBank.ClampToRegister(BellSlot, PentaMidi(k)), vel = 40, dur = 0.3f, owner = OwnerNotes });
    }

    /// <summary>A title letter was touched: its note (the U ticks).</summary>
    public void PlayLetter(int k, float strength01)
    {
        if (!running || Held || k < 0 || k >= LetterMidi.Length) return;
        int vel = Mathf.RoundToInt(Mathf.Lerp(36f, 52f, Mathf.Clamp01(strength01)));
        LetterNotes++;
        if (LetterMidi[k] < 0) pending.Add(new Pending { at = Time.realtimeSinceStartup, slot = SynthBank.DrumSlot, midi = TickKey, vel = vel - 8, dur = 0.12f, owner = OwnerNotes });
        else pending.Add(new Pending { at = Time.realtimeSinceStartup, slot = BellSlot, midi = SynthBank.ClampToRegister(BellSlot, LetterMidi[k]), vel = vel, dur = 0.3f, owner = OwnerNotes });
    }

    /// <summary>Leaving the menu: a quick descending pentatonic shimmer while the wall falls (runs even after <see cref="Stop"/>).</summary>
    public void FallShimmer()
    {
        float rt = Time.realtimeSinceStartup;
        int[] ks = { 12, 10, 8, 6, 4, 2 };
        for (int i = 0; i < ks.Length; i++)
            pending.Add(new Pending { at = rt + 0.02f + i * 0.06f, slot = BellSlot, midi = SynthBank.ClampToRegister(BellSlot, PentaMidi(ks[i])), vel = 42 - i * 3, dur = 0.25f, owner = OwnerFx });
    }

    /// <summary>The leave's impact (SPEC v4 §7b), <paramref name="delay"/> seconds from now: the sounding chord hit at once on Keys an
    /// octave up with a low root under it and one Bells note on top. Own owner (runs after <see cref="Stop"/>).</summary>
    public void LeaveHit(float delay) { LeaveHit(delay, null); }

    /// <summary>v5: the impact hit on <paramref name="chord"/> (MIDI, root first; the gallery song's opening chord) instead of the menu's
    /// sounding one (null or empty = the menu's).</summary>
    public void LeaveHit(float delay, int[] chord)
    {
        float rt = Time.realtimeSinceStartup + Mathf.Max(0f, delay);
        var ch = chord != null && chord.Length > 0 ? chord : Chords[ChordNow];
        for (int i = 0; i < ch.Length; i++)
            pending.Add(new Pending { at = rt, slot = KeysSlot, midi = SynthBank.ClampToRegister(KeysSlot, ch[i] + 12), vel = 56 - i * 2, dur = 0.42f, owner = OwnerFx });
        pending.Add(new Pending { at = rt, slot = KeysSlot, midi = SynthBank.ClampToRegister(KeysSlot, ch[0] - 12), vel = 52, dur = 0.5f, owner = OwnerFx });
        pending.Add(new Pending { at = rt + 0.03f, slot = BellSlot, midi = SynthBank.ClampToRegister(BellSlot, ch[ch.Length - 1] + 12), vel = 48, dur = 0.4f, owner = OwnerFx });
        LeaveHits++;
    }

    /// <summary>Sends due immediate notes ("as soon as possible": never counted late). Works while stopped (the fall shimmer).</summary>
    public void TickPendingOnly()
    {
        if (pending.Count == 0) return;
        float rt = Time.realtimeSinceStartup;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (p.at > rt) continue;
            pending.RemoveAt(i);
            if (!Synth.Ready) continue;
            Synth.NoteOn(p.slot, p.midi, p.vel, 0.0, p.owner);
            Synth.NoteOff(p.slot, p.midi, AudioSettings.dspTime + Lead + p.dur, p.owner);
        }
    }
}
