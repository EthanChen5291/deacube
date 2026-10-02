using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MeltySynth;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// One queued synthesizer event. <c>dsp</c> is an AudioSettings.dspTime; 0 or less means "as soon as
/// possible" (frame 0 of the next audio callback) and is never counted as late.
/// </summary>
public struct SynthEvent
{
    public double dsp;
    public byte kind;    // SynthEngine.Kind*
    public byte slot;    // instrument slot (SynthBank)
    public byte key;     // MIDI key; controller number for CC; patch for Program
    public byte vel;     // velocity; controller value for CC; bank-select value for Program; flag for control kinds
    public int owner;    // caller tag used by CancelOwner (0 = anonymous, never cancelled by owner)
    public float arg;    // v6: a control event's argument (KindPress: how long the press holds its duck and boost, seconds)
}

/// <summary>
/// Real-time SoundFont synthesizer (MeltySynth) hosted in an AudioSource filter callback.
/// The main thread only appends events stamped with DSP time to a pending list under a lock; the
/// audio thread swaps that list in, merges it into a time-sorted queue and applies the events
/// sample-accurately between synth.Render sub-blocks. Master gain, a one-pole low-pass and a soft
/// limiter follow, and the result is ADDED into the (silent) host buffer. Use the <see cref="Synth"/>
/// facade; never call the synthesizer from the main thread.
/// v5 (R, "I have no idea what the next note sounds like"): two buses. The notes of the preview owner
/// (<see cref="PreviewOwner"/> = VoiceRules.OwnerPreview: auditions, previews) play on a second synthesizer
/// sharing the SoundFont (the same slots, programs and controllers: every controller event goes to both);
/// everything else is the song bus. Every preview note-on DUCKS the song bus (<see cref="Synth.DuckDb"/>, about
/// -8 dB for about 0.4 s from 20 ms before the note, smooth attack and release, applied per sample on the audio
/// thread) so an audition is always heard over the music; the preview bus itself is never ducked. The preview
/// synthesizer renders only while it has voices or a reverb tail (4 s), so an idle preview bus costs nothing.
/// v6 (A, SPEC v6 §4.1 / §4.2 — "the instruments should be expanded into groups", "whenever theyre pressing the keys, it should be louder than
/// whatever else is playing"): both synthesizers run SynthBank.ChannelCount channels (one per voice slot, 72; MeltySynth's channel count and
/// percussion channels come from SynthBank, every drum kit on a percussion channel), each channel programmed at init on both buses with its
/// preset, sends and loudness trim (voice 0 of each group = the v5 slot bit for bit). CPU follows the notes sounding, not the number of
/// voices defined. Two kinds of preview note now: a hover AUDITION ducks the song bus <see cref="Synth.DuckDb"/> (-10 dB, was -8); a PRESS
/// (<see cref="Synth.PressNote"/>: a tile or a piano key the player clicked) ducks it deeper and longer (<see cref="Synth.PressDuckDb"/>, -15 dB,
/// held for the note's length + <see cref="Synth.PressRelease"/>) and boosts the preview bus meanwhile (<see cref="Synth.PressBoostDb"/>, +4 dB over a
/// silent song; over a playing song — its running level is tracked — enough to put the press <see cref="Synth.PressOverDb"/> above it), both per
/// sample with smooth attack / release, ahead of the master gain and the soft limiter. Immediate controller events (volume, pan, sends,
/// programs at dsp 0) apply when the audio thread drains them instead of going through the sorted queue (group-wide pushes send one per voice).
/// Test hooks: stems recording (the song bus before the duck, the preview bus after the boost, the gains), per-callback CPU / voice peaks, the
/// SoundFont's load time and memory.
/// v7 (A, SPEC v7 §4.4 — the launch riser): both synthesizers get one more channel, the EFFECTS channel (SynthBank.FxChannel, events use slot
/// SynthBank.FxSlot), programmed with GeneralUser GS's "Reverse Cymbal" at full channel volume with a reverb send; its notes (owners ≠ the preview
/// owner) play on the song bus (LaunchRiser: a swell that peaks on a launch's landing). At boot a worker thread renders the preset dry at two keys
/// from the loaded SoundFont (never touching the audio thread's synthesizers) and measures the swell's peak (the 10 ms RMS maximum) and how it
/// scales with the key: <see cref="Synth.RiserPeakSeconds"/> / <see cref="Synth.RiserKeyFor"/>.
/// </summary>
[DisallowMultipleComponent]
public class SynthEngine : MonoBehaviour
{
    public const byte KindNoteOn = 0, KindNoteOff = 1, KindAllOff = 2, KindCC = 3, KindProgram = 4;
    // Control kinds are applied when the audio thread drains the pending list, in enqueue order,
    // so they affect exactly the events enqueued before them.
    public const byte KindAllOffOwner = 5, KindCancelAfter = 6, KindAllNotesOff = 7, KindCancelOwnerAfter = 8;
    /// <summary>v5: ducks the song bus from dsp for vel / 100 seconds (<see cref="Synth.Duck"/>).</summary>
    public const byte KindDuck = 9;
    /// <summary>v5: the owner whose notes play on the preview bus (= VoiceRules.OwnerPreview).</summary>
    public const int PreviewOwner = 9;
    /// <summary>v6: a press (<see cref="Synth.PressNote"/>): ducks the song bus to the press level and boosts the preview bus from dsp - 20 ms for
    /// <c>arg</c> seconds; overlapping presses extend it.</summary>
    public const byte KindPress = 10;

    public const string SoundFontFile = "GeneralUser-GS.sf2";
    const float LimiterKnee = 0.7f;

    internal static SynthEngine instance;

    // --- synthesizer (touched only by the audio thread once ready) ---
    Synthesizer synth;
    SoundFont soundFont;
    volatile bool ready;
    volatile int callbacks; double lastBlockDsp;   // the audio thread has started rendering (boot: events queued before its first callback would all be late)
    int sampleRate = 48000;
    float[] left = new float[4096], right = new float[4096];

    // --- event queue ---
    readonly object gate = new object();
    List<SynthEvent> pending = new List<SynthEvent>(512);    // main thread appends under gate
    List<SynthEvent> incoming = new List<SynthEvent>(512);   // audio thread swaps in under gate, drains outside it
    readonly List<SynthEvent> queue = new List<SynthEvent>(2048);   // audio thread only, sorted by dsp
    readonly bool[] onSet = new bool[(SynthBank.Slots + SynthBank.FxChannels) * 128];   // scratch for CancelAfter (v7: + the effects channel)

    // --- v5 preview bus (audio thread once ready): a second synthesizer for the preview owner's notes ---
    Synthesizer pv;
    float[] pl = new float[4096], pr = new float[4096];
    bool pvLive; double pvQuietAfter;
    const double PreviewTail = 4.0;              // seconds the preview bus keeps rendering after its last voice (reverb tail)

    // --- v5 song duck: parameters written by the main thread, state owned by the audio thread ---
    volatile float duckLevel = 0.3162f;          // v6: -10 dB (v5: -8 dB)
    volatile float duckHold = 0.38f;             // seconds a trigger holds the duck after its note-on
    const double DuckLead = 0.02;                // the duck starts this long before the note
    const float DuckAttackTau = 0.008f, DuckReleaseTau = 0.07f;
    double duckStart = -1.0, duckEnd = -1.0;
    float duckG = 1f, duckAtk = 0.1f, duckRel = 0.01f, duckMinAcc = 1f;
    int duckMinSeen;
    volatile float duckNow = 1f, duckMin = 1f;
    volatile int duckTriggers, duckMinGen, previewVoices;

    // --- v6 presses: a deeper, longer duck and a preview-bus boost (parameters main thread, state audio thread) ---
    volatile float pressLevel = 0.1778f;         // -15 dB
    volatile float pressBoost = 1.585f;          // +4 dB: the boost over silence (and the least a press gets)
    volatile float pressOverDb = 9f;             // over a playing song the boost aims the press this far above the song's recent loud level
    volatile float pressBoostMax = 7.943f;       // +18 dB
    volatile float pressRelease = 0.2f;          // seconds the press holds after its note-off
    float pressBoostWin = 1.585f;                // the boost of the open press window (audio thread)
    float songMs, songPeakMs;                    // the song bus before the duck, x master gain: mean square (~0.1 s) and its peak hold (~1.5 s release)
    volatile float songLevel, lastPressBoost = 1f;
    // presses queued ahead wait here until their window opens: the boost is sized from the song's level at that moment (audio thread)
    struct PendingPress { public double start, end; public int slot, key, vel; }
    readonly PendingPress[] pendingPress = new PendingPress[32];
    int pendingPressCount;
    const float PressReleaseTau = 0.15f, BoostAttackTau = 0.005f, BoostReleaseTau = 0.08f;
    const double PressTail = 0.6;                // after a press window the duck releases with the press's slower time constant
    double pressStart = -1.0, pressEnd = -1.0;
    float boostG = 1f, boostAtk = 0.1f, boostRel = 0.01f, pressRel = 0.01f, boostMaxAcc = 1f;
    volatile float boostNow = 1f, boostMax = 1f;
    volatile int pressTriggers;

    // --- v6 performance meters (audio thread writes; ResetPerf starts a new window) ---
    float perfLoadAcc, perfLoadMax; int perfN, perfVoicesMax, perfSongMax, perfSeen;
    volatile int perfGen, perfVoicesOut, perfSongOut, perfBlocks;
    volatile float perfAvgOut, perfMaxOut;

    // --- v6 load statistics (Awake) ---
    float loadMs, initMs; long soundFontBytes, sampleBytes; int presetsMissing;

    // --- sounding notes, for owner cancellation (audio thread only); bus 1 = the preview synthesizer ---
    struct Sounding { public byte slot, key, bus; public int owner; public bool active; }
    readonly Sounding[] sounding = new Sounding[256];
    int soundingCursor;

    // --- master chain: targets written by the main thread, state owned by the audio thread ---
    volatile float masterGainTarget = 2.2f;
    volatile float cutoffTarget = 1f;
    float masterGain = 2.2f, cutoffSmooth = 1f, lpL, lpR;

    // --- counters: audio thread writes, main thread reads ---
    volatile int lateEvents, errors, activeVoices, queuedCount, callbackFrames;
    volatile int lateOwner, lateInfo; volatile float lateBy;   // v5: the last late event (diagnostics: owner, kind / slot / key, how late)
    // v5 diagnostics: the last LateLogSize late events (block DSP time, how late, owner, kind / slot / key), written by the audio thread only
    const int LateLogSize = 32;
    readonly double[] lateLogDsp = new double[LateLogSize]; readonly float[] lateLogBy = new float[LateLogSize];
    readonly int[] lateLogOwner = new int[LateLogSize], lateLogInfo = new int[LateLogSize]; volatile int lateLogCount;
    /// <summary>The recorded late events newer than index <paramref name="from"/> (a count from <see cref="LateLogCount"/>), one per line.</summary>
    internal string LateLog(int from)
    {
        var sb = new System.Text.StringBuilder();
        int n = lateLogCount;
        for (int i = Mathf.Max(from, n - LateLogSize); i < n; i++)
        {
            int li = i % LateLogSize, info = lateLogInfo[li];
            sb.AppendFormat("dsp {0:F3} owner {1} kind {2} slot {3} key {4}: {5:F1} ms late; ", lateLogDsp[li], lateLogOwner[li], (info >> 16) & 255, (info >> 8) & 255, info & 255, lateLogBy[li] * 1000f);
        }
        return sb.ToString();
    }
    internal int LateLogCount => lateLogCount;
    volatile float cpuLoad;
    volatile string lastError;
    bool errorLogged;

    // --- recording tee (debug): captures this engine's post-limiter output ---
    float[] rec; int recPos;
    volatile int recGen;                 // bumped by StartRecording so a stale callback cannot write back its position
    volatile bool recording, recordingDone;
    float[] recSong, recPv, recGain;     // v6 stems (tests): the song bus before the duck, the preview bus after the boost (both x master gain), the gains
    volatile bool recStems;
    double recStartDsp = -1.0;           // v6: DSP time of the recording's first frame (written by the audio thread; read after RecordingDone)

    // --- per-slot main-thread state ---
    readonly float[] slotGain = new float[SynthBank.Slots];
    readonly bool[] slotMuted = new bool[SynthBank.Slots];
    readonly string[] presetNames = new string[SynthBank.Slots];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    // ------------------------------------------------------------------ lifecycle
    void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject);
        sampleRate = AudioSettings.outputSampleRate;
        for (int i = 0; i < SynthBank.Slots; i++) { slotGain[i] = 1f; presetNames[i] = SynthBank.Defs[i].name + ": (not loaded)"; }

        string path = Path.Combine(Application.streamingAssetsPath, SoundFontFile);
        try
        {
            if (!File.Exists(path))
                Debug.LogWarning("SynthEngine: SoundFont not found at " + path + "; synth disabled, WAV playback stays in use.");
            else
            {
                long mem0 = GC.GetTotalMemory(false);
                var sw = Stopwatch.StartNew();
                soundFont = new SoundFont(path);
                loadMs = (float)sw.Elapsed.TotalMilliseconds;
                soundFontBytes = Math.Max(0L, GC.GetTotalMemory(false) - mem0);
                sampleBytes = (long)soundFont.WaveData.Length * 2L;
                var sw2 = Stopwatch.StartNew();
                synth = new Synthesizer(soundFont, Settings(64));
                pv = new Synthesizer(soundFont, Settings(32));   // v5: the preview bus
                presetsMissing = 0;
                for (int s = 0; s < SynthBank.Slots; s++) { ApplySlotDefaults(synth, s); ApplySlotDefaults(pv, s); }   // direct calls: audio thread not running yet
                ApplyFxDefaults(synth); ApplyFxDefaults(pv);   // v7: the effects channel (the launch riser)
                initMs = (float)sw2.Elapsed.TotalMilliseconds;
                MeasureRiserAsync();   // v7: the riser's swell peak, rendered on a worker thread
                duckAtk = 1f - Mathf.Exp(-1f / (sampleRate * DuckAttackTau));
                duckRel = 1f - Mathf.Exp(-1f / (sampleRate * DuckReleaseTau));
                pressRel = 1f - Mathf.Exp(-1f / (sampleRate * PressReleaseTau));
                boostAtk = 1f - Mathf.Exp(-1f / (sampleRate * BoostAttackTau));
                boostRel = 1f - Mathf.Exp(-1f / (sampleRate * BoostReleaseTau));
                var v5 = new string[Mathf.Min(SynthBank.Groups, SynthBank.Slots)];
                Array.Copy(presetNames, v5, v5.Length);
                Debug.Log(string.Format("SynthEngine: '{0}' loaded in {1:0} ms ({2:0.0} MB of samples), {3} presets, {4} Hz. Slots: {5} | v6: +{6} voices, {7} channels, synths ready in {8:0.0} ms",
                    soundFont.Info.BankName, loadMs, sampleBytes / 1048576.0, soundFont.Presets.Count, sampleRate, string.Join(" | ", v5),
                    SynthBank.Slots - v5.Length, SynthBank.ChannelCount, initMs));
                if (presetsMissing > 0) Debug.LogWarning("SynthEngine: " + presetsMissing + " voice preset(s) do not resolve as expected: " + MissingPresets());
                ready = true;
            }
        }
        catch (Exception e)
        {
            synth = null; ready = false;
            Debug.LogWarning("SynthEngine: SoundFont failed to load (" + e.Message + "); synth disabled, WAV playback stays in use.");
        }

        // Host: a looping silent stereo clip gives perfectly regular callbacks; we add our output to it.
        var src = GetComponent<AudioSource>();
        if (src == null) src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; src.loop = true; src.spatialBlend = 0f; src.volume = 1f; src.dopplerLevel = 0f; src.priority = 0;
        var clip = AudioClip.Create("SynthHost", sampleRate, 2, sampleRate, false);
        clip.SetData(new float[sampleRate * 2], 0);
        src.clip = clip;
        src.Play();
        host = src;
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;
    }

    // v6 (integration): the host clip must keep playing — Unity stops every AudioSource when the audio configuration changes (an output device
    // plugged in or out, a sample-rate switch), and nothing restarted it: the synth then went silent for good (the v6 battery caught it: a
    // stopped host, 90 queued events, voices frozen at 64). A watchdog restarts it; the events queued while it was stopped are dropped first
    // (they would all fire at once, late).
    AudioSource host;
    float hostCheckT;
    internal int HostRestarts { get; private set; }

    void OnAudioConfigChanged(bool deviceWasChanged)
    {
        if (host != null && !host.isPlaying) RestartHost();
        if (ready && AudioSettings.outputSampleRate != sampleRate)
            Debug.LogWarning("SynthEngine: the output sample rate changed (" + sampleRate + " → " + AudioSettings.outputSampleRate + " Hz); notes play off pitch until the game restarts.");
    }

    void RestartHost()
    {
        if (host == null || !host.isActiveAndEnabled) return;
        Enqueue(KindAllNotesOff, 0, 0, 1, 0.0, 0);   // the stale queue and the frozen voices go (immediate): nothing fires late in a burst
        host.Play();
        HostRestarts++;
        // v7 (integration): when Unity's audio stays broken (seen after an audio-system reset with the editor in the background) the host stops
        // again at once; the watchdog then retried every 0.25 s and logged each one (500+ warnings in a few minutes). Restarts that do not stick
        // back off to every 4 s, and only the first few and then every 50th are logged.
        quickRestarts = Time.unscaledTime - lastRestartAt < 1.5f ? quickRestarts + 1 : 0;
        lastRestartAt = Time.unscaledTime;
        if (HostRestarts <= 5 || HostRestarts % 50 == 0)
            Debug.LogWarning("SynthEngine: the host AudioSource had stopped (audio configuration change?); restarted (" + HostRestarts + ")" + (HostRestarts == 5 ? " — further restarts are logged every 50th." : "."));
    }
    int quickRestarts; float lastRestartAt = -10f;

    void Update()
    {
        if (!errorLogged && lastError != null) { errorLogged = true; Debug.LogError("SynthEngine audio callback threw: " + lastError); }
        if (host != null && !host.isPlaying && Time.unscaledTime >= hostCheckT) { hostCheckT = Time.unscaledTime + (quickRestarts >= 8 ? 4f : 0.25f); RestartHost(); }
        WatchCallbacks();
    }

    // v7 (A): the FILTER watchdog — seen in the v7 battery (the editor under heavy memory pressure): the host kept playing and the DSP clock ran on,
    // but Unity no longer called OnAudioFilterRead — not ours, not a second engine's (a sampled stack showed FMOD mixing without the script filters):
    // the synth silent for good while events piled up unplayed. A callback silent for more than a second while the host plays: (1) drop what piled
    // up (it would all fire late) and re-insert the filter (the component disabled and enabled, the host restarted); (2) still silent a second later:
    // reset Unity's audio system with its own configuration (AudioSettings.Reset re-creates every DSP; OnAudioConfigurationChanged restarts the
    // host); (3) still silent: one warning, then quiet until the callback runs again. Each step logs once.
    int seenCallbacks = -1, reviveStep; float seenAt, callbackCheckT;
    internal int FilterRevives { get; private set; }
    internal int AudioResets { get; private set; }
    internal int LastReviveStale { get; private set; }
    void WatchCallbacks()
    {
        float now = Time.unscaledTime;
        if (!ready || host == null || !host.isPlaying || AudioListener.pause) { seenAt = now; return; }
        int c = callbacks;
        if (c != seenCallbacks) { seenCallbacks = c; seenAt = now; reviveStep = 0; return; }   // the callback runs (a main-thread stall is no silence)
        if (c == 0 || now - seenAt < 1.0f || now < callbackCheckT || reviveStep >= 3) return;
        reviveStep++;
        int stale; lock (gate) { stale = pending.Count; pending.Clear(); }
        LastReviveStale = stale;
        Enqueue(KindAllNotesOff, 0, 0, 1, 0.0, 0);   // the audio thread's own queue and the frozen voices go too, when it runs again
        if (reviveStep == 1)
        {
            FilterRevives++;
            Debug.LogWarning("SynthEngine: the audio callback stopped at dsp " + lastBlockDsp.ToString("F2") + " (now " + AudioSettings.dspTime.ToString("F2") + ", the host playing, "
                             + stale + " events waiting): dropped them and re-inserted the filter.");
            enabled = false; enabled = true;
            host.Stop(); host.Play();
        }
        else if (reviveStep == 2)
        {
            AudioResets++;
            Debug.LogWarning("SynthEngine: the audio callback is still silent: resetting Unity's audio system (its own configuration).");
            AudioSettings.Reset(AudioSettings.GetConfiguration());
            if (host != null && !host.isPlaying) RestartHost();
        }
        else Debug.LogWarning("SynthEngine: the audio callback could not be revived (Unity calls no OnAudioFilterRead): the synth is silent until it runs again.");
        callbackCheckT = now + 1.0f;
    }

    void OnDestroy() { AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged; ready = false; if (instance == this) instance = null; }

    /// <summary>v6: settings of both synthesizers — one channel per voice slot, every drum-kit voice's channel a percussion channel.</summary>
    SynthesizerSettings Settings(int polyphony) => new SynthesizerSettings(sampleRate)
    {
        EnableReverbAndChorus = true, MaximumPolyphony = polyphony, ChannelCount = Mathf.Max(16, SynthBank.ChannelCount), PercussionChannels = SynthBank.PercussionChannels
    };

    /// <summary>Program, sends, volume and (v6) the loudness trim for a slot of <paramref name="sy"/>; called directly at init, later through events.</summary>
    void ApplySlotDefaults(Synthesizer sy, int slot)
    {
        InstrumentDef d = SynthBank.Defs[slot];
        int ch = SynthBank.ChannelOf(slot);
        sy.ProcessMidiMessage(ch, 0xB0, 0, BankSelectValue(ch, d.bank));
        sy.ProcessMidiMessage(ch, 0xC0, d.patch, 0);
        sy.ProcessMidiMessage(ch, 0xB0, 7, Cc7For(slot));
        sy.ProcessMidiMessage(ch, 0xB0, 10, 64);
        sy.ProcessMidiMessage(ch, 0xB0, 91, To7Bit(d.reverb));
        sy.ProcessMidiMessage(ch, 0xB0, 93, To7Bit(d.chorus));
        sy.SetChannelGain(ch, d.trim > 0f ? d.trim : 1f);   // v6: voice 0 = 1 (the v5 level exactly)
        string found = PresetName(d.bank, d.patch);
        if (sy == synth && !string.IsNullOrEmpty(d.preset) && found != d.preset) presetsMissing++;
        presetNames[slot] = d.name + ": " + found + " (" + d.bank + ":" + d.patch + ")";
    }

    // ---- v7: the effects channel and the riser's measured swell
    /// <summary>v7: the riser's channel volume (CC7, 0..127) and reverb send (0..1): its swell's peak sits about at a dense song's loudness (measured:
    /// ≈ −19.5 dBFS as a 10 ms RMS at velocity 112, a dense gallery song ≈ −23..−21 dBFS as a 400 ms RMS).</summary>
    public const int RiserCc7 = 127;
    public const float RiserReverb = 0.35f;
    /// <summary>v7: the key the riser's swell is measured at (the preset's root: the sample plays at its recorded speed).</summary>
    public const int RiserRootKey = 60;

    /// <summary>v7: programs the effects channel of <paramref name="sy"/>: the riser preset, full channel volume, centre, its reverb send, no chorus.</summary>
    void ApplyFxDefaults(Synthesizer sy)
    {
        int ch = SynthBank.FxChannel;
        if (ch >= sy.ChannelCount) return;
        sy.ProcessMidiMessage(ch, 0xB0, 0, SynthBank.RiserBank);
        sy.ProcessMidiMessage(ch, 0xC0, SynthBank.RiserPatch, 0);
        sy.ProcessMidiMessage(ch, 0xB0, 7, RiserCc7);
        sy.ProcessMidiMessage(ch, 0xB0, 10, 64);
        sy.ProcessMidiMessage(ch, 0xB0, 91, To7Bit(RiserReverb));
        sy.ProcessMidiMessage(ch, 0xB0, 93, 0);
    }

    // measured on a worker thread at boot (the lab's render of GeneralUser GS until then: 1.400 s at key 60, 50 cents per key)
    volatile float riserPeakRoot = 1.400f, riserEndRoot = 1.405f, riserCentsPerKey = 50f, riserMeasureMs, riserPeakDb;
    volatile bool riserMeasured;
    volatile string riserError;

    /// <summary>v7: renders the riser preset dry at <see cref="RiserRootKey"/> and an octave of keys above it on a throwaway synthesizer from the loaded
    /// SoundFont (a worker thread: the SoundFont is read-only, the audio thread's synthesizers are never touched) and keeps the swell's peak time at the
    /// root key and its scaling per key (the preset's scale tuning: GeneralUser GS plays it at 50 cents per key).</summary>
    void MeasureRiserAsync()
    {
        var sf = soundFont; int sr = sampleRate;
        if (sf == null) return;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                double endA, dbA, endB, dbB;
                double a = RenderRiserPeak(sf, sr, RiserRootKey, out endA, out dbA);
                double b = RenderRiserPeak(sf, sr, RiserRootKey + 12, out endB, out dbB);
                if (a > 0.05 && b > 0.05 && a > b)
                {
                    riserCentsPerKey = (float)(1200.0 * Math.Log(a / b, 2.0) / 12.0);
                    riserPeakRoot = (float)a; riserEndRoot = (float)endA; riserPeakDb = (float)dbA;
                    riserMeasured = true;
                }
                else riserError = "no swell found (" + a.ToString("F3") + " s / " + b.ToString("F3") + " s)";
                riserMeasureMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception e) { riserError = e.GetType().Name + ": " + e.Message; }
        });
    }

    /// <summary>v7: renders the riser at <paramref name="key"/> (velocity 112, the effects channel's volume, dry) and returns the time of its 10 ms RMS
    /// maximum (1 ms hop) after the note-on; <paramref name="end"/> = where the sound stops (the last millisecond above −60 dB of that peak) and
    /// <paramref name="peakDb"/> the peak's level (before the master gain).</summary>
    static double RenderRiserPeak(SoundFont sf, int sr, int key, out double end, out double peakDb)
    {
        end = -1.0; peakDb = -200.0;
        var sy = new Synthesizer(sf, new SynthesizerSettings(sr) { EnableReverbAndChorus = false, MaximumPolyphony = 8 });
        sy.ProcessMidiMessage(0, 0xB0, 0, SynthBank.RiserBank); sy.ProcessMidiMessage(0, 0xC0, SynthBank.RiserPatch, 0);
        sy.ProcessMidiMessage(0, 0xB0, 7, RiserCc7);
        int n = (int)(sr * 3.2);
        var L = new float[n]; var R = new float[n];
        sy.NoteOn(0, key, 112);
        sy.Render(new Span<float>(L, 0, n), new Span<float>(R, 0, n));
        int ms = Math.Max(1, sr / 1000), win = 10 * ms;
        int nms = n / ms;
        var e = new double[nms];   // energy per millisecond
        for (int k = 0; k < nms; k++) { double s = 0; for (int i = k * ms; i < (k + 1) * ms; i++) s += L[i] * L[i] + R[i] * R[i]; e[k] = s; }
        double best = -1.0; int bestAt = -1; double acc = 0;
        for (int k = 0; k < nms; k++)
        {
            acc += e[k]; if (k >= 10) acc -= e[k - 10];
            if (k >= 9 && acc > best) { best = acc; bestAt = k - 9; }
        }
        if (bestAt < 0 || best <= 1e-12) return -1.0;
        double rms = Math.Sqrt(best / (2.0 * win));
        peakDb = 20.0 * Math.Log10(rms);
        for (int k = nms - 1; k >= 0; k--) if (Math.Sqrt(e[k] / (2.0 * ms)) > rms * 0.001) { end = (k + 1) / 1000.0 * (1000.0 * ms / sr); break; }
        return (bestAt * ms + win * 0.5) / sr;
    }

    internal bool RiserMeasured => riserMeasured;
    internal float RiserPeakRoot => riserPeakRoot;
    internal float RiserEndRoot => riserEndRoot;
    internal float RiserCentsPerKey => riserCentsPerKey;
    internal float RiserMeasureMs => riserMeasureMs;
    internal float RiserPeakDb => riserPeakDb;
    internal string RiserError => riserError;

    string MissingPresets()
    {
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s < SynthBank.Slots; s++)
        {
            var d = SynthBank.Defs[s];
            string found = PresetName(d.bank, d.patch);
            if (!string.IsNullOrEmpty(d.preset) && found != d.preset) sb.Append(d.name).Append(" wants '").Append(d.preset).Append("' got '").Append(found).Append("'; ");
        }
        return sb.ToString();
    }

    /// <summary>v6: the preset bank:patch resolves to in the loaded SoundFont ("(missing …)" / "(no soundfont)").</summary>
    internal string PresetOf(int bank, int patch) => PresetName(bank, patch);
    /// <summary>v6: true when the synthesizers treat <paramref name="channel"/> as a percussion channel (tests).</summary>
    internal bool ChannelIsPercussion(int channel) => synth != null && synth.IsPercussionChannel(channel) && pv != null && pv.IsPercussionChannel(channel);
    /// <summary>v6: the channels of each synthesizer (tests).</summary>
    internal int ChannelCount => synth != null ? synth.ChannelCount : 0;
    internal int PreviewChannelCount => pv != null ? pv.ChannelCount : 0;
    internal float ChannelTrim(int channel) => synth != null ? synth.GetChannelGain(channel) : 1f;

    string PresetName(int bank, int patch)
    {
        if (soundFont == null) return "(no soundfont)";
        var presets = soundFont.Presets;
        for (int i = 0; i < presets.Count; i++)
            if (presets[i].BankNumber == bank && presets[i].PatchNumber == patch) return presets[i].Name;
        return "(missing " + bank + ":" + patch + ")";
    }

    // MeltySynth adds 128 to bank-select on a percussion channel, so the drum bank is selected with 0 there (v6: every drum-kit voice's channel).
    static int BankSelectValue(int channel, int bank) => SynthBank.IsPercussionChannel(channel) ? Mathf.Clamp(bank - 128, 0, 127) : Mathf.Clamp(bank, 0, 127);
    static int To7Bit(float v01) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v01) * 127f), 0, 127);
    // Channel volume is squared inside the synth, so sqrt here makes CC7 linear in loudness; gain 1 gives CC7 = 100.
    int Cc7For(int slot) => Mathf.Clamp(Mathf.RoundToInt(127f * Mathf.Sqrt(0.62f * SynthBank.Defs[slot].gain * slotGain[slot])), 0, 127);

    // ------------------------------------------------------------------ main-thread API (used by Synth)
    internal void Enqueue(byte kind, int slot, int key, int vel, double dsp, int owner, float arg = 0f)
    {
        var ev = new SynthEvent
        {
            dsp = dsp, kind = kind, slot = (byte)Mathf.Clamp(slot, 0, SynthBank.FxSlot),   // v7: FxSlot = the effects channel
            key = (byte)Mathf.Clamp(key, 0, 127), vel = (byte)Mathf.Clamp(vel, 0, 127), owner = owner, arg = arg
        };
        lock (gate) pending.Add(ev);
    }

    internal void SetSlotGain(int slot, float gain01)
    {
        slot = Mathf.Clamp(slot, 0, SynthBank.Slots - 1);
        slotGain[slot] = Mathf.Clamp01(gain01);
        if (!slotMuted[slot]) Enqueue(KindCC, slot, 7, Cc7For(slot), 0.0, 0);
    }

    internal void SetSlotMute(int slot, bool mute)
    {
        slot = Mathf.Clamp(slot, 0, SynthBank.Slots - 1);
        slotMuted[slot] = mute;
        Enqueue(KindCC, slot, 7, mute ? 0 : Cc7For(slot), 0.0, 0);
        if (mute) Enqueue(KindAllOff, slot, 0, 0, 0.0, 0);
    }

    internal void SetSlotProgram(int slot, int bank, int patch)
    {
        slot = Mathf.Clamp(slot, 0, SynthBank.Slots - 1);
        patch = Mathf.Clamp(patch, 0, 127);
        Enqueue(KindProgram, slot, patch, BankSelectValue(SynthBank.ChannelOf(slot), bank), 0.0, 0);
        presetNames[slot] = SynthBank.Defs[slot].name + ": " + PresetName(bank, patch) + " (" + bank + ":" + patch + ")";
    }

    internal void StartRecording(float seconds) => StartRecording(seconds, false);

    /// <summary>v6: <paramref name="stems"/> also records the song bus before the duck, the preview bus after the boost (both x the master
    /// gain, before the limiter) and the duck / boost gains (left = the song bus gain, right = the preview bus gain / 8): <see cref="SaveStems"/>.</summary>
    internal void StartRecording(float seconds, bool stems)
    {
        recording = false;
        recGen++;
        int frames = Mathf.Clamp((int)(seconds * sampleRate), sampleRate / 10, sampleRate * 120);
        rec = new float[frames * 2];
        if (stems) { recSong = new float[frames * 2]; recPv = new float[frames * 2]; recGain = new float[frames * 2]; }
        else { recSong = null; recPv = null; recGain = null; }
        recStems = stems;
        recPos = 0;
        recStartDsp = -1.0;
        recordingDone = false;
        recording = true;   // volatile write last: the audio thread sees a fully prepared buffer
    }

    /// <summary>v6: writes the stems of the last <see cref="StartRecording(float, bool)"/> with stems (false without them).</summary>
    internal bool SaveStems(string songPath, string previewPath, string gainPath)
    {
        if (recSong == null || recPv == null || recGain == null) return false;
        int count = Mathf.Clamp(recPos, 0, recSong.Length) & ~1;
        return WriteWav(songPath, recSong, count) & WriteWav(previewPath, recPv, count) & (string.IsNullOrEmpty(gainPath) || WriteWav(gainPath, recGain, count));
    }

    internal bool SaveRecording(string path)
    {
        recording = false;
        var data = rec;
        if (data == null) return false;
        int count = Mathf.Clamp(recPos, 0, data.Length) & ~1;
        return WriteWav(path, data, count);
    }

    /// <summary>Writes <paramref name="count"/> interleaved stereo samples as a 16-bit WAV.</summary>
    bool WriteWav(string path, float[] data, int count)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var w = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write)))
            {
                int bytes = count * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)2); w.Write(sampleRate); w.Write(sampleRate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(bytes);
                for (int i = 0; i < count; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(data[i] * 32767f), -32768, 32767));
            }
            return true;
        }
        catch (Exception e) { Debug.LogWarning("SynthEngine: could not write " + path + ": " + e.Message); return false; }
    }

    internal string[] PresetNames() { return (string[])presetNames.Clone(); }
    /// <summary>v6 (tests): the DSP time of the last recording's first frame (-1 before it started).</summary>
    internal double RecordingStartDsp => recStartDsp;
    /// <summary>v6 (tests): a slot's level / mute as the main thread last set them (Instruments' group controls must reach every voice).</summary>
    internal float SlotGain(int slot) => slotGain[Mathf.Clamp(slot, 0, SynthBank.Slots - 1)];
    internal bool SlotMuted(int slot) => slotMuted[Mathf.Clamp(slot, 0, SynthBank.Slots - 1)];

    /// <summary>
    /// v6 (the user asked "will it hurt performance"): builds throwaway synthesizer pairs ON THE MAIN THREAD from the loaded SoundFont — never
    /// the audio thread's — like the engine does, once with the v5 table (16 channels, 10 slots programmed) and once with v6's (SynthBank's
    /// channels, every slot programmed), and reports the construction + programming time (ms, best of 5). The SoundFont itself is shared
    /// (loaded once: <see cref="LoadMs"/>, <see cref="SampleBytes"/>); memory per channel is measured offline (Mono's GC counters are too coarse).
    /// </summary>
    internal string MeasureConfigs()
    {
        if (soundFont == null) return "no soundfont";
        var sb = new System.Text.StringBuilder();
        for (int pass = 0; pass < 2; pass++)
        {
            bool v6 = pass == 1;
            int slots = v6 ? SynthBank.Slots : SynthBank.Groups;
            double best = double.MaxValue; int chans = 0;
            for (int rep = 0; rep < 5; rep++)
            {
            var sw = Stopwatch.StartNew();
            Synthesizer a, b;
            if (v6) { a = new Synthesizer(soundFont, Settings(64)); b = new Synthesizer(soundFont, Settings(32)); }
            else
            {
                a = new Synthesizer(soundFont, new SynthesizerSettings(sampleRate) { EnableReverbAndChorus = true, MaximumPolyphony = 64 });
                b = new Synthesizer(soundFont, new SynthesizerSettings(sampleRate) { EnableReverbAndChorus = true, MaximumPolyphony = 32 });
            }
            for (int s = 0; s < slots; s++)
            {
                InstrumentDef d = SynthBank.Defs[s];
                int ch = SynthBank.ChannelOf(s);
                foreach (var sy in new[] { a, b })
                {
                    sy.ProcessMidiMessage(ch, 0xB0, 0, BankSelectValue(ch, d.bank)); sy.ProcessMidiMessage(ch, 0xC0, d.patch, 0);
                    sy.ProcessMidiMessage(ch, 0xB0, 7, Cc7For(s)); sy.ProcessMidiMessage(ch, 0xB0, 10, 64);
                    sy.ProcessMidiMessage(ch, 0xB0, 91, To7Bit(d.reverb)); sy.ProcessMidiMessage(ch, 0xB0, 93, To7Bit(d.chorus));
                    if (v6) sy.SetChannelGain(ch, d.trim);
                }
            }
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            chans = a.ChannelCount;
            GC.KeepAlive(a); GC.KeepAlive(b);
            }
            sb.AppendFormat("{0}: {1} channels, {2} slots programmed on both synthesizers: {3:0.00} ms", v6 ? "v6" : "v5", chans, slots, best);
            if (pass == 0) sb.Append(" | ");
        }
        return sb.ToString();
    }

    internal string Stats()
    {
        return string.Format("ready={0} sr={1} frames={2} voices={3} queued={4} late={5} errors={6} cpu={7:0.0}% gain={8:0.00} cutoff={9:0.00} pv={10} duck={11:0.00} boost={12:0.00} presses={13}{14}",
            ready, sampleRate, callbackFrames, activeVoices, queuedCount, lateEvents, errors, cpuLoad * 100f, masterGainTarget, cutoffTarget, previewVoices, duckNow, boostNow, pressTriggers,
            FilterRevives > 0 || HostRestarts > 0 ? " revives=" + FilterRevives + " restarts=" + HostRestarts : "");
    }
    /// <summary>v7 (tests): audio callbacks so far and the DSP time of the last one.</summary>
    internal int Callbacks => callbacks;
    internal double LastBlockDsp => lastBlockDsp;

    // ---- v5 duck (main thread)
    internal float DuckLevel { get { return duckLevel; } set { duckLevel = Mathf.Clamp01(value); } }
    internal float DuckHold { get { return duckHold; } set { duckHold = Mathf.Clamp(value, 0.02f, 1.27f); } }
    internal float DuckNow => duckNow;
    internal float DuckMin => duckMin;
    internal void ResetDuckMin() { duckMin = 1f; boostMax = 1f; duckMinGen++; }
    internal int DuckTriggers => duckTriggers;
    internal int PreviewVoices => previewVoices;

    // ---- v6 presses (main thread)
    internal float PressLevel { get { return pressLevel; } set { pressLevel = Mathf.Clamp01(value); } }
    internal float PressBoost { get { return pressBoost; } set { pressBoost = Mathf.Clamp(value, 1f, 4f); } }
    internal float PressRelease { get { return pressRelease; } set { pressRelease = Mathf.Clamp(value, 0f, 2f); } }
    internal float PressOverDb { get { return pressOverDb; } set { pressOverDb = Mathf.Clamp(value, 0f, 20f); } }
    internal float PressBoostMax { get { return pressBoostMax; } set { pressBoostMax = Mathf.Clamp(value, 1f, 8f); } }
    internal float LastPressBoost => lastPressBoost;
    internal float SongLevel => songLevel;
    internal float BoostNow => boostNow;
    internal float BoostMax => boostMax;
    internal int PressTriggers => pressTriggers;

    // ---- v6 performance meters and load statistics (main thread)
    internal void ResetPerf() { perfGen++; }
    internal float PerfAvgCpu => perfAvgOut;
    internal float PerfMaxCpu => perfMaxOut;
    internal int PerfMaxVoices => perfVoicesOut;
    internal int PerfMaxSongVoices => perfSongOut;
    internal int PerfBlocks => perfBlocks;
    internal float LoadMs => loadMs;
    internal float InitMs => initMs;
    internal long SoundFontBytes => soundFontBytes;
    internal long SampleBytes => sampleBytes;
    internal int PresetsMissing => presetsMissing;

    internal string LastLate => lateEvents == 0 ? "none" : string.Format("owner {0} kind {1} slot {2} key {3}, {4:0.0} ms late",
        lateOwner, (lateInfo >> 16) & 255, (lateInfo >> 8) & 255, lateInfo & 255, lateBy * 1000f);
    internal bool Ready => ready;
    /// <summary>True once the audio thread renders buffers (its last one within half a second): at boot the synth is ready a few seconds
    /// before Unity's audio starts calling it, and a note queued then arrives late.</summary>
    internal bool Rendering => ready && callbacks > 0 && AudioSettings.dspTime - lastBlockDsp < 0.5;
    internal int SampleRate => sampleRate;
    internal int LateEvents => lateEvents;
    internal int Errors => errors;
    internal int ActiveVoices => activeVoices;
    internal int Queued => queuedCount;
    internal float CpuLoad => cpuLoad;
    internal bool RecordingDone => recordingDone;
    internal float MasterGain { get { return masterGainTarget; } set { masterGainTarget = Mathf.Clamp(value, 0f, 8f); } }
    internal float MasterCutoff01 { get { return cutoffTarget; } set { cutoffTarget = Mathf.Clamp01(value); } }

    // ------------------------------------------------------------------ audio thread
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!ready || channels < 1) return;
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            int frames = data.Length / channels;
            if (frames > left.Length) { Array.Resize(ref left, frames); Array.Resize(ref right, frames); Array.Resize(ref pl, frames); Array.Resize(ref pr, frames); }
            double blockDsp = AudioSettings.dspTime;   // DSP time of frame 0 of this buffer (verified sample-exact)
            lastBlockDsp = blockDsp; callbacks++;
            double sr = sampleRate;

            // 1. Pull what the main thread queued since the last callback (tiny critical section).
            lock (gate) { var t = pending; pending = incoming; incoming = t; }
            Drain(blockDsp);
            if (pendingPressCount > 0) OpenPresses(blockDsp + frames / (double)sampleRate);   // v6

            // 2. Apply due events at their frame, rendering the gaps between them.
            int cursor = 0, head = 0;
            while (head < queue.Count)
            {
                SynthEvent ev = queue[head];
                int idx = 0;
                if (ev.dsp > 0.0)
                {
                    double f = (ev.dsp - blockDsp) * sr + 1e-4;   // epsilon absorbs double rounding at block edges
                    if (f >= frames) break;                        // later block: everything after it is later too
                    if (f < 0.0)
                    {
                        lateEvents++; lateOwner = ev.owner; lateInfo = (ev.kind << 16) | (ev.slot << 8) | ev.key; lateBy = (float)(-f / sr);
                        int li = lateLogCount % LateLogSize; lateLogDsp[li] = blockDsp; lateLogBy[li] = lateBy; lateLogOwner[li] = ev.owner; lateLogInfo[li] = lateInfo; lateLogCount++;
                    }
                    else idx = (int)f;
                }
                if (idx > cursor) { RenderRange(cursor, idx); cursor = idx; }
                Apply(ref ev);
                head++;
            }
            if (head > 0) queue.RemoveRange(0, head);
            RenderRange(cursor, frames);
            queuedCount = queue.Count;
            activeVoices = synth.ActiveVoiceCount;
            double blockEnd = blockDsp + frames / sr;
            if (pvLive)
            {
                // the preview bus renders while it has voices and for a reverb tail after the last one
                int pvv = pv.ActiveVoiceCount;
                previewVoices = pvv;
                if (pvv > 0) pvQuietAfter = blockEnd + PreviewTail;
                else if (blockEnd > pvQuietAfter) pvLive = false;
            }
            else previewVoices = 0;

            // 3. v5: the song bus ducked under the previews (per sample, from duckStart to duckEnd with a smooth attack / release),
            // plus the preview bus; then master gain (linear ramp per block), one-pole low-pass, soft limiter; add into the host buffer.
            float gTarget = masterGainTarget, g = masterGain, gStep = (gTarget - g) / frames;
            cutoffSmooth += (cutoffTarget - cutoffSmooth) * 0.15f;
            float a = 1f;
            if (cutoffSmooth < 0.999f)
            {
                float fc = 60f * Mathf.Pow(20000f / 60f, cutoffSmooth);
                a = 1f - Mathf.Exp(-2f * Mathf.PI * fc / sampleRate);
            }
            bool tee = recording, wasTee = tee; float[] r = rec; int rp = recPos, gen = recGen;
            bool stems = tee && recStems; float[] rs = recSong, rv = recPv, rg = recGain;
            if (tee && rp == 0 && gen == recGen) recStartDsp = blockDsp;   // v6: the first recorded frame is this block's first
            if (stems && (rs == null || rv == null || rg == null || rs.Length != r.Length || rv.Length != r.Length || rg.Length != r.Length)) stems = false;
            // v6: the envelopes run while a hover duck or a press window touches this block, or while either gain is still settling
            bool ducking = duckG < 0.99999f || boostG > 1.00001f || (duckEnd > blockDsp && duckStart < blockEnd) || (pressEnd > blockDsp && pressStart < blockEnd);
            float dLevel = duckLevel, pLevel = pressLevel, pBoost = pressBoostWin, dg = duckG, bg = boostG, blockMin = 1f, blockMax = 1f, songSq = 0f;
            double tS = blockDsp, dtS = 1.0 / sr;
            for (int i = 0; i < frames; i++)
            {
                float gd = 1f, gb = 1f;
                if (ducking)
                {
                    float target = tS >= duckStart && tS < duckEnd ? dLevel : 1f, bt = 1f;
                    if (tS >= pressStart && tS < pressEnd) { if (pLevel < target) target = pLevel; bt = pBoost; }   // v6: a press ducks deeper and boosts the preview bus
                    dg += (target - dg) * (target < dg ? duckAtk : (tS < pressEnd + PressTail ? pressRel : duckRel));
                    bg += (bt - bg) * (bt > bg ? boostAtk : boostRel);
                    gd = dg; gb = bg; tS += dtS;
                    if (gd < blockMin) blockMin = gd;
                    if (gb > blockMax) blockMax = gb;
                }
                float gs = g;
                songSq += (left[i] * left[i] + right[i] * right[i]) * (gs * gs);   // v6: the song's own loudness (before the duck) for the press boost
                float l = (left[i] * gd + pl[i] * gb) * g, rr = (right[i] * gd + pr[i] * gb) * g;
                g += gStep;
                lpL += a * (l - lpL); lpR += a * (rr - lpR);
                l = Limit(lpL); rr = Limit(lpR);
                if (tee)
                {
                    if (r != null && rp + 1 < r.Length)
                    {
                        if (stems)
                        {
                            rs[rp] = left[i] * gs; rs[rp + 1] = right[i] * gs;
                            rv[rp] = pl[i] * gb * gs; rv[rp + 1] = pr[i] * gb * gs;
                            rg[rp] = gd; rg[rp + 1] = 0.125f * gb;   // the boost / 8: up to +18 dB fits a 16-bit WAV
                        }
                        r[rp++] = l; r[rp++] = rr;
                    }
                    else { tee = false; recording = false; recordingDone = true; }
                }
                int o = i * channels;
                if (channels >= 2) { data[o] += l; data[o + 1] += rr; }
                else data[o] += 0.5f * (l + rr);
            }
            if (wasTee && gen == recGen) recPos = rp;
            songMs += (songSq / (2f * frames) - songMs) * (1f - Mathf.Exp(-(float)(frames / sr) / 0.1f));
            songPeakMs = Mathf.Max(songMs, songPeakMs * Mathf.Exp(-(float)(frames / sr) / 1.5f));   // a swelling song raises it at once, a quiet bar lowers it slowly
            songLevel = Mathf.Sqrt(songPeakMs);
            masterGain = gTarget;
            callbackFrames = frames;
            duckG = ducking && dg < 0.99999f ? dg : 1f;
            boostG = ducking && bg > 1.00001f ? bg : 1f;
            duckNow = duckG; boostNow = boostG;
            if (duckMinGen != duckMinSeen) { duckMinSeen = duckMinGen; duckMinAcc = 1f; duckMin = 1f; boostMaxAcc = 1f; boostMax = 1f; }
            if (blockMin < duckMinAcc) { duckMinAcc = blockMin; duckMin = blockMin; }
            if (blockMax > boostMaxAcc) { boostMaxAcc = blockMax; boostMax = blockMax; }

            float load = (float)((Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency / (frames / sr));
            cpuLoad += (load - cpuLoad) * 0.1f;
            // v6 performance meters: mean / peak load per callback and peak voices since the last ResetPerf
            if (perfGen != perfSeen) { perfSeen = perfGen; perfLoadAcc = 0f; perfLoadMax = 0f; perfN = 0; perfVoicesMax = 0; perfSongMax = 0; }
            perfLoadAcc += load; perfN++;
            if (load > perfLoadMax) perfLoadMax = load;
            int vAll = activeVoices + previewVoices;
            if (vAll > perfVoicesMax) perfVoicesMax = vAll;
            if (activeVoices > perfSongMax) perfSongMax = activeVoices;
            perfAvgOut = perfLoadAcc / perfN; perfMaxOut = perfLoadMax; perfVoicesOut = perfVoicesMax; perfSongOut = perfSongMax; perfBlocks = perfN;
        }
        catch (Exception e)
        {
            errors++;
            if (lastError == null) lastError = e.GetType().Name + ": " + e.Message;
        }
    }

    void RenderRange(int from, int to)
    {
        if (to <= from) return;
        int n = to - from;
        synth.Render(new Span<float>(left, from, n), new Span<float>(right, from, n));
        if (pvLive) pv.Render(new Span<float>(pl, from, n), new Span<float>(pr, from, n));
        else { Array.Clear(pl, from, n); Array.Clear(pr, from, n); }
    }

    /// <summary>Soft limiter: transparent up to the knee, then an exponential approach to 1.0 (never reached).</summary>
    static float Limit(float x)
    {
        float ax = x < 0f ? -x : x;
        if (ax <= LimiterKnee) return x;
        float y = LimiterKnee + (1f - LimiterKnee) * (1f - (float)Math.Exp(-(ax - LimiterKnee) / (1f - LimiterKnee)));
        return x < 0f ? -y : y;
    }

    void Apply(ref SynthEvent ev)
    {
        int ch = SynthBank.ChannelOf(ev.slot);
        bool bus = ev.owner == PreviewOwner;   // v5: the preview owner's notes live on the preview bus; controllers go to both
        switch (ev.kind)
        {
            case KindNoteOn:
                if (bus) { pv.NoteOn(ch, ev.key, ev.vel); pvLive = true; }
                else synth.NoteOn(ch, ev.key, ev.vel);
                Track(ev.slot, ev.key, ev.owner);
                break;
            case KindNoteOff: if (bus) pv.NoteOff(ch, ev.key); else synth.NoteOff(ch, ev.key); Untrack(ev.slot, ev.key, bus); break;
            case KindAllOff: synth.NoteOffAll(ch, ev.vel != 0); pv.NoteOffAll(ch, ev.vel != 0); UntrackSlot(ev.slot); break;
            case KindCC: synth.ProcessMidiMessage(ch, 0xB0, ev.key, ev.vel); pv.ProcessMidiMessage(ch, 0xB0, ev.key, ev.vel); break;
            case KindProgram:
                synth.ProcessMidiMessage(ch, 0xB0, 0, ev.vel); synth.ProcessMidiMessage(ch, 0xC0, ev.key, 0);
                pv.ProcessMidiMessage(ch, 0xB0, 0, ev.vel); pv.ProcessMidiMessage(ch, 0xC0, ev.key, 0);
                break;
        }
    }

    /// <summary>v5: ducks the song bus from <paramref name="at"/> - DuckLead for <paramref name="hold"/> s after it; overlapping or pending
    /// triggers extend the current duck, a trigger after a finished one starts a new one.</summary>
    void TriggerDuck(double at, float hold, double now)
    {
        double s0 = at - DuckLead, e0 = at + hold;
        if (duckStart < 0.0 || duckEnd <= now) { duckStart = s0; duckEnd = e0; }
        else { if (s0 < duckStart) duckStart = s0; if (e0 > duckEnd) duckEnd = e0; }
        duckTriggers++;
    }

    /// <summary>v6: a press holds the deep duck and the preview-bus boost from <paramref name="at"/> - DuckLead for <paramref name="hold"/> s after it.
    /// It waits in the pending list until the block its window opens in (<see cref="OpenPresses"/>).</summary>
    void QueuePress(double at, float hold, int slot, int key, int vel)
    {
        if (pendingPressCount >= pendingPress.Length) { for (int i = 1; i < pendingPressCount; i++) pendingPress[i - 1] = pendingPress[i]; pendingPressCount--; }
        pendingPress[pendingPressCount++] = new PendingPress { start = at - DuckLead, end = at + Math.Max(0.05f, hold), slot = slot, key = key, vel = vel };
        pressTriggers++;
    }

    /// <summary>v6: opens the pending presses whose window starts before <paramref name="blockEnd"/>: a window that overlaps the open one extends
    /// it (the higher boost wins), else it starts a new one. The boost: pressBoost over silence; over a playing song enough to put the press
    /// pressOverDb above the song's recent loud level (the peak-held running level, before the duck) — the press's own level estimated from its
    /// group's press level at that key (SynthBank.PressLevelDb), its velocity, its slot's CC7 level and the master gain — at least pressBoost,
    /// at most pressBoostMax.</summary>
    void OpenPresses(double blockEnd)
    {
        int w = 0;
        for (int i = 0; i < pendingPressCount; i++)
        {
            PendingPress p = pendingPress[i];
            if (p.start >= blockEnd) { pendingPress[w++] = p; continue; }
            float lvl = Mathf.Sqrt(Mathf.Max(0f, songPeakMs)), b = pressBoost;
            if (lvl > 1e-5f)
            {
                float noteDb = SynthBank.PressLevelDb(p.slot, p.key) + 40f * Mathf.Log10(Mathf.Max(1, p.vel) / 121f)
                             + 20f * Mathf.Log10(Mathf.Max(1e-3f, slotGain[Mathf.Clamp(p.slot, 0, SynthBank.Slots - 1)] * masterGain));
                b = Mathf.Pow(10f, (pressOverDb + 20f * Mathf.Log10(lvl) - noteDb) / 20f);
            }
            b = Mathf.Clamp(b, pressBoost, Mathf.Max(pressBoost, pressBoostMax));
            if (pressStart < 0.0 || pressEnd <= p.start) { pressStart = p.start; pressEnd = p.end; pressBoostWin = b; }
            else { if (p.start < pressStart) pressStart = p.start; if (p.end > pressEnd) pressEnd = p.end; if (b > pressBoostWin) pressBoostWin = b; }
            lastPressBoost = b;
        }
        pendingPressCount = w;
    }

    /// <summary>Merges the swapped-in list into the sorted queue; control events act on what was queued before them.</summary>
    void Drain(double now)
    {
        for (int i = 0; i < incoming.Count; i++)
        {
            SynthEvent ev = incoming[i];
            switch (ev.kind)
            {
                case KindAllOffOwner:
                    if (ev.owner != 0) { DropWhere(ev.owner, 0.0, false, now); SilenceOwner(ev.owner); }
                    break;
                case KindCancelOwnerAfter:
                    if (ev.owner != 0) DropWhere(ev.owner, ev.dsp, false, now);   // that owner's queued events from ev.dsp on; its sounding notes keep playing (the caller re-issues their note-offs)
                    break;
                case KindCancelAfter:
                    DropWhere(0, ev.dsp, false, now);
                    if (ev.vel != 0) { synth.NoteOffAll(false); pv.NoteOffAll(false); ClearSounding(); }
                    break;
                case KindAllNotesOff:
                    DropWhere(0, 0.0, true, now);
                    synth.NoteOffAll(ev.vel != 0); pv.NoteOffAll(ev.vel != 0); ClearSounding();
                    break;
                case KindDuck:
                    TriggerDuck(ev.dsp > 0.0 ? ev.dsp : now, ev.vel / 100f, now);
                    break;
                case KindPress:
                    QueuePress(ev.dsp > 0.0 ? ev.dsp : now, ev.arg, ev.slot, ev.key, ev.vel);
                    break;
                default:
                    // v6: an immediate controller / program change applies now: the sorted queue would put it first in this block anyway (dsp 0 sorts
                    // before every timed event, controllers before note-ons), and a group-wide push sends one per voice
                    if ((ev.kind == KindCC || ev.kind == KindProgram) && ev.dsp <= 0.0) { Apply(ref ev); break; }
                    if (ev.kind == KindNoteOn && ev.owner == PreviewOwner) TriggerDuck(ev.dsp > 0.0 ? ev.dsp : now, duckHold, now);   // v5: every preview ducks the song
                    InsertSorted(ev);
                    break;
            }
        }
        incoming.Clear();
    }

    /// <summary>Insertion sort by time; at equal times note-ons go last so offs/CCs/programs precede them.</summary>
    void InsertSorted(SynthEvent ev)
    {
        double d = ev.dsp > 0.0 ? ev.dsp : 0.0;
        int rank = ev.kind == KindNoteOn ? 1 : 0;
        int i = queue.Count;
        while (i > 0)
        {
            SynthEvent q = queue[i - 1];
            double qd = q.dsp > 0.0 ? q.dsp : 0.0;
            if (qd < d || (qd == d && (q.kind == KindNoteOn ? 1 : 0) <= rank)) break;
            i--;
        }
        queue.Insert(i, ev);
    }

    /// <summary>
    /// Removes queued events in place. owner != 0: that owner's events (all of them, or only those at/after <paramref name="after"/> when it is > 0). notesOnly: every note on/off.
    /// Otherwise: events at/after <paramref name="after"/>, except note-offs whose note will still be sounding
    /// (already on, or turned on by a surviving note-on) so that nothing is left hanging.
    /// </summary>
    void DropWhere(int owner, double after, bool notesOnly, double now)
    {
        if (owner == 0 && !notesOnly)
        {
            Array.Clear(onSet, 0, onSet.Length);
            for (int i = 0; i < sounding.Length; i++) if (sounding[i].active) onSet[sounding[i].slot * 128 + sounding[i].key] = true;
            for (int i = 0; i < queue.Count; i++)
            {
                SynthEvent q = queue[i];
                if (q.kind == KindNoteOn && (q.dsp > 0.0 ? q.dsp : now) < after) onSet[q.slot * 128 + q.key] = true;
            }
        }
        int w = 0;
        for (int i = 0; i < queue.Count; i++)
        {
            SynthEvent q = queue[i];
            bool drop;
            if (owner != 0) drop = q.owner == owner && (after <= 0.0 || (q.dsp > 0.0 ? q.dsp : now) >= after);
            else if (notesOnly) drop = q.kind == KindNoteOn || q.kind == KindNoteOff || q.kind == KindAllOff;
            else
            {
                drop = (q.dsp > 0.0 ? q.dsp : now) >= after;
                if (drop && q.kind == KindNoteOff && onSet[q.slot * 128 + q.key]) drop = false;
            }
            if (!drop) queue[w++] = q;
        }
        if (w < queue.Count) queue.RemoveRange(w, queue.Count - w);
    }

    void Track(byte slot, byte key, int owner)
    {
        byte bus = owner == PreviewOwner ? (byte)1 : (byte)0;
        int free = -1;
        for (int i = 0; i < sounding.Length; i++)
        {
            if (sounding[i].active) { if (sounding[i].slot == slot && sounding[i].key == key && sounding[i].bus == bus) { sounding[i].owner = owner; return; } }
            else if (free < 0) free = i;
        }
        if (free < 0) free = (soundingCursor++) & 255;   // table full: recycle an old entry
        sounding[free].slot = slot; sounding[free].key = key; sounding[free].bus = bus; sounding[free].owner = owner; sounding[free].active = true;
    }

    void Untrack(byte slot, byte key, bool previewBus)
    {
        byte bus = previewBus ? (byte)1 : (byte)0;
        for (int i = 0; i < sounding.Length; i++) if (sounding[i].active && sounding[i].slot == slot && sounding[i].key == key && sounding[i].bus == bus) sounding[i].active = false;
    }

    void UntrackSlot(byte slot)
    {
        for (int i = 0; i < sounding.Length; i++) if (sounding[i].active && sounding[i].slot == slot) sounding[i].active = false;
    }

    void ClearSounding() { for (int i = 0; i < sounding.Length; i++) sounding[i].active = false; }

    void SilenceOwner(int owner)
    {
        for (int i = 0; i < sounding.Length; i++)
        {
            if (!sounding[i].active || sounding[i].owner != owner) continue;
            (sounding[i].bus != 0 ? pv : synth).NoteOff(SynthBank.ChannelOf(sounding[i].slot), sounding[i].key);
            sounding[i].active = false;
        }
    }
}

/// <summary>
/// Static facade over the single <see cref="SynthEngine"/>. Every call is safe when the engine is
/// missing or not ready (it becomes a no-op), so callers can fall back to clip playback by checking
/// <see cref="Ready"/>. Times are AudioSettings.dspTime; 0 means "now". Keys are raw MIDI: callers
/// fold pitches with SynthBank.ClampToRegister and must use the same key for on and off.
/// </summary>
public static class Synth
{
    static SynthEngine E => SynthEngine.instance;

    /// <summary>Creates the engine (GameObject "SynthEngine") if needed and returns it. Loads the SoundFont synchronously (~70 ms).</summary>
    public static SynthEngine Ensure()
    {
        if (SynthEngine.instance == null)
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<SynthEngine>();
            if (existing != null) SynthEngine.instance = existing;
            else new GameObject("SynthEngine").AddComponent<SynthEngine>();
        }
        return SynthEngine.instance;
    }

    public static bool Ready => E != null && E.Ready;
    /// <summary>True once the audio thread is actually rendering (see SynthEngine.Rendering): the title music waits for it at boot.</summary>
    public static bool Rendering => E != null && E.Rendering;
    /// <summary>v6: times the synth's host AudioSource was found stopped and restarted (audio configuration changes).</summary>
    public static int HostRestarts => E != null ? E.HostRestarts : 0;
    /// <summary>v7: times the audio callback was found silent while the host played and the filter was re-inserted / Unity's audio reset
    /// (SynthEngine.WatchCallbacks).</summary>
    public static int FilterRevives => E != null ? E.FilterRevives : 0;
    public static int AudioResets => E != null ? E.AudioResets : 0;
    /// <summary>v7 (tests): audio callbacks so far and the DSP time of the last one.</summary>
    public static int Callbacks => E != null ? E.Callbacks : 0;
    public static double LastBlockDsp => E != null ? E.LastBlockDsp : 0.0;
    public static int SampleRate => E != null ? E.SampleRate : AudioSettings.outputSampleRate;
    public static double DspNow => AudioSettings.dspTime;
    public static int LateEvents => E != null ? E.LateEvents : 0;
    /// <summary>v5 diagnostics: the last late event (owner, kind, slot, key and how late), "none" without one.</summary>
    public static string LastLate => E != null ? E.LastLate : "none";
    /// <summary>v5 diagnostics: the late events recorded after <paramref name="from"/> (see <see cref="LateLogCount"/>).</summary>
    public static string LateLog(int from) => E != null ? E.LateLog(from) : "";
    public static int LateLogCount => E != null ? E.LateLogCount : 0;
    public static int Errors => E != null ? E.Errors : 0;
    public static int ActiveVoices => E != null ? E.ActiveVoices : 0;
    public static int Queued => E != null ? E.Queued : 0;
    public static float CpuLoad => E != null ? E.CpuLoad : 0f;

    // --- notes ---
    public static void NoteOn(int slot, int midi, int velocity, double dspTime, int owner)
    { if (E != null) E.Enqueue(SynthEngine.KindNoteOn, slot, midi, Mathf.Clamp(velocity, 1, 127), dspTime, owner); }

    public static void NoteOff(int slot, int midi, double dspTime, int owner)
    { if (E != null) E.Enqueue(SynthEngine.KindNoteOff, slot, midi, 0, dspTime, owner); }

    public static void Note(int slot, int midi, int velocity, double onDsp, double offDsp, int owner)
    { NoteOn(slot, midi, velocity, onDsp, owner); NoteOff(slot, midi, offDsp, owner); }

    /// <summary>Immediate, anonymous note that ends after <paramref name="seconds"/> (previews, UI).</summary>
    public static void Preview(int slot, int midi, int velocity, float seconds)
    { NoteOn(slot, midi, velocity, 0.0, 0); NoteOff(slot, midi, DspNow + Mathf.Max(0.02f, seconds), 0); }

    /// <summary>Stops every sounding note (immediate = no release) and drops all queued note on/offs; CC/program events survive.</summary>
    public static void AllNotesOff(bool immediate)
    { if (E != null) E.Enqueue(SynthEngine.KindAllNotesOff, 0, 0, immediate ? 1 : 0, 0.0, 0); }

    /// <summary>Drops every queued event of this owner and releases its sounding notes. Owner 0 is ignored.</summary>
    public static void CancelOwner(int owner)
    { if (E != null && owner != 0) E.Enqueue(SynthEngine.KindAllOffOwner, 0, 0, 0, 0.0, owner); }

    /// <summary>Drops this owner's queued events at/after <paramref name="dspTime"/> (note-ons and note-offs alike) and leaves its sounding
    /// notes playing: the caller re-issues their note-offs. For tempo changes, where a cube retimes them to the new window end.</summary>
    public static void CancelOwnerAfter(int owner, double dspTime)
    { if (E != null && owner != 0) E.Enqueue(SynthEngine.KindCancelOwnerAfter, 0, 0, 0, dspTime, owner); }

    /// <summary>Drops queued events at/after <paramref name="dspTime"/> (note-offs of notes that will still sound are kept);
    /// with <paramref name="alsoSilence"/> every sounding note is released too. For pause/seek/tempo changes.</summary>
    public static void CancelPendingAfter(double dspTime, bool alsoSilence = false)
    { if (E != null) E.Enqueue(SynthEngine.KindCancelAfter, 0, 0, alsoSilence ? 1 : 0, dspTime, 0); }

    // --- v5 (R): the preview bus and the song duck ---
    /// <summary>The owner whose notes play on the preview bus and duck the song (= VoiceRules.OwnerPreview).</summary>
    public const int PreviewOwner = SynthEngine.PreviewOwner;

    /// <summary>An audition note on the preview bus (owner <see cref="PreviewOwner"/>): heard over the song, which ducks under it.
    /// <paramref name="onDsp"/> 0 = at once.</summary>
    public static void Audition(int slot, int midi, int velocity, double onDsp, double offDsp) { Note(slot, midi, velocity, onDsp, offDsp, PreviewOwner); }

    /// <summary>v6 (SPEC v6 §2.8 / §4.2, A): a note the player PRESSED (a click on a tile or a piano key) — louder than anything the song plays: on
    /// the preview bus (owner <see cref="PreviewOwner"/>) at a strong velocity (at least <see cref="PressMinVelocity"/>), the song bus ducked to
    /// <see cref="PressDuckDb"/> and the preview bus boosted by <see cref="PressBoostDb"/> from 20 ms before the note until
    /// <see cref="PressRelease"/> after its note-off (both smooth, ahead of the soft limiter). <paramref name="onDsp"/> 0 = at once;
    /// <paramref name="offDsp"/> is absolute. Callers build the note with VoiceRules.PressEvent (pitch, fold, velocity).</summary>
    public static void PressNote(int slot, int midi, int velocity, double onDsp, double offDsp)
    {
        if (E == null) return;
        int vel = Mathf.Clamp(Mathf.Max(velocity, PressMinVelocity), 1, 127);
        double on = onDsp > 0.0 ? onDsp : DspNow;
        float hold = (float)Math.Max(0.05, offDsp - on) + E.PressRelease;
        E.Enqueue(SynthEngine.KindPress, slot, midi, vel, onDsp, 0, hold);
        Note(slot, midi, vel, onDsp, offDsp, PreviewOwner);
        PressCount++;
        LastPressSlot = slot; LastPressMidi = midi; LastPressVelocity = vel; LastPressOnDsp = on; LastPressOffDsp = offDsp;
        OnPress?.Invoke(slot, midi, vel, on, offDsp);
    }

    /// <summary>v6: the weakest velocity a press plays at (SPEC v6 §4.2: "a strong velocity (≥ 100)").</summary>
    public const int PressMinVelocity = 100;
    /// <summary>v6 (tests): presses sent so far and the last one (slot, key, velocity, onset / note-off DSP time).</summary>
    public static int PressCount;
    public static int LastPressSlot = -1, LastPressMidi = -1, LastPressVelocity;
    public static double LastPressOnDsp, LastPressOffDsp;
    /// <summary>v6 (tests): raised on the main thread for every press (slot, key, velocity, onset DSP time, note-off DSP time).</summary>
    public static event Action<int, int, int, double, double> OnPress;
    /// <summary>v6: how far the song bus ducks under a press (dB, default -15).</summary>
    public static float PressDuckDb
    {
        get { return E != null ? 20f * Mathf.Log10(Mathf.Max(1e-4f, E.PressLevel)) : -15f; }
        set { if (E != null) E.PressLevel = Mathf.Pow(10f, Mathf.Clamp(value, -60f, 0f) / 20f); }
    }
    /// <summary>v6: how much the preview bus is boosted while a press holds over a silent song (dB, default +4, the least a press gets). Over a
    /// playing song the boost is sized, when the press's window opens, to put the press <see cref="PressOverDb"/> above the song's recent loud level
    /// (the press's level estimated from its group's measured press level at that key, its velocity, the group's volume and the master gain), up
    /// to <see cref="PressBoostMaxDb"/>: a press stays clearly over whatever else is playing, a dense song or a single part (SPEC v6 §4.2: ≥ 6 dB).</summary>
    public static float PressBoostDb
    {
        get { return E != null ? 20f * Mathf.Log10(Mathf.Max(1e-4f, E.PressBoost)) : 4f; }
        set { if (E != null) E.PressBoost = Mathf.Pow(10f, Mathf.Clamp(value, 0f, 12f) / 20f); }
    }
    /// <summary>v6: how far above the playing song's recent loud level a press is aimed (dB, default +9; SPEC v6 §4.2 asks ≥ 6: the margin covers
    /// the song swelling under the press and the level estimate's ±1 dB).</summary>
    public static float PressOverDb { get { return E != null ? E.PressOverDb : 9f; } set { if (E != null) E.PressOverDb = value; } }
    /// <summary>v6: the press boost's ceiling (dB, default +18: enough to lift a press of a group turned down in the mix; the soft limiter keeps it
    /// under full scale).</summary>
    public static float PressBoostMaxDb
    {
        get { return E != null ? 20f * Mathf.Log10(Mathf.Max(1e-4f, E.PressBoostMax)) : 18f; }
        set { if (E != null) E.PressBoostMax = Mathf.Pow(10f, Mathf.Clamp(value, 0f, 18f) / 20f); }
    }
    /// <summary>v6: the boost the last press window got (dB) and the song bus's recent loud level (dBFS RMS before the duck: a 0.1 s mean held
    /// with a 1.5 s release) that sizes it.</summary>
    public static float LastPressBoostDb => E != null ? 20f * Mathf.Log10(Mathf.Max(1e-4f, E.LastPressBoost)) : 0f;
    public static float SongLevelDb => E != null ? 20f * Mathf.Log10(Mathf.Max(1e-6f, E.SongLevel)) : -120f;
    /// <summary>v6: how long a press holds its duck and boost after the note-off (seconds, default 0.2).</summary>
    public static float PressRelease { get { return E != null ? E.PressRelease : 0.2f; } set { if (E != null) E.PressRelease = value; } }
    /// <summary>v6: the preview bus gain right now (1 = not boosted) and the highest since <see cref="ResetDuckMin"/>.</summary>
    public static float BoostGain => E != null ? E.BoostNow : 1f;
    public static float BoostMax => E != null ? E.BoostMax : 1f;
    /// <summary>v6: press windows the audio thread has opened so far.</summary>
    public static int PressTriggers => E != null ? E.PressTriggers : 0;

    /// <summary>Ducks the song bus from <paramref name="atDsp"/> (0 = now) for <paramref name="seconds"/> (at most 1.27 s) without a note.</summary>
    public static void Duck(double atDsp, float seconds)
    { if (E != null) E.Enqueue(SynthEngine.KindDuck, 0, 0, Mathf.Clamp(Mathf.RoundToInt(seconds * 100f), 2, 127), atDsp, 0); }

    /// <summary>How far the song bus ducks under a preview note (a hover audition; dB, default -10 — v5: -8; 0 = no duck).</summary>
    public static float DuckDb
    {
        get { return E != null ? 20f * Mathf.Log10(Mathf.Max(1e-4f, E.DuckLevel)) : -10f; }
        set { if (E != null) E.DuckLevel = Mathf.Pow(10f, Mathf.Clamp(value, -60f, 0f) / 20f); }
    }
    /// <summary>How long one preview note holds the duck after its onset (seconds, default 0.38; the duck starts 20 ms before the note).</summary>
    public static float DuckSeconds { get { return E != null ? E.DuckHold : 0.38f; } set { if (E != null) E.DuckHold = value; } }
    /// <summary>The song bus gain right now (1 = not ducked); written by the audio thread once per buffer.</summary>
    public static float DuckGain => E != null ? E.DuckNow : 1f;
    /// <summary>The lowest song bus gain since <see cref="ResetDuckMin"/> (tests).</summary>
    public static float DuckMin => E != null ? E.DuckMin : 1f;
    public static void ResetDuckMin() { if (E != null) E.ResetDuckMin(); }
    /// <summary>Duck triggers applied by the audio thread so far (preview note-ons and Duck calls).</summary>
    public static int DuckTriggers => E != null ? E.DuckTriggers : 0;
    /// <summary>Voices sounding on the preview bus.</summary>
    public static int PreviewVoices => E != null ? E.PreviewVoices : 0;

    // --- per-slot controls ---
    public static void SetSlotGain(int slot, float gain01) { if (E != null) E.SetSlotGain(slot, gain01); }
    public static void SetSlotMute(int slot, bool mute) { if (E != null) E.SetSlotMute(slot, mute); }
    public static void SetSlotReverb(int slot, float amount01) { if (E != null) E.Enqueue(SynthEngine.KindCC, slot, 91, Mathf.RoundToInt(Mathf.Clamp01(amount01) * 127f), 0.0, 0); }
    public static void SetSlotChorus(int slot, float amount01) { if (E != null) E.Enqueue(SynthEngine.KindCC, slot, 93, Mathf.RoundToInt(Mathf.Clamp01(amount01) * 127f), 0.0, 0); }
    public static void SetSlotPan(int slot, float pan) { if (E != null) E.Enqueue(SynthEngine.KindCC, slot, 10, Mathf.Clamp(Mathf.RoundToInt(64f + Mathf.Clamp(pan, -1f, 1f) * 63f), 0, 127), 0.0, 0); }
    public static void SetSlotProgram(int slot, int bank, int patch) { if (E != null) E.SetSlotProgram(slot, bank, patch); }

    // --- master ---
    public static void SetMasterGain(float linear) { if (E != null) E.MasterGain = linear; }
    public static void SetMasterCutoff01(float cutoff01) { if (E != null) E.MasterCutoff01 = cutoff01; }

    // --- debug recording tee ---
    public static void StartRecording(float seconds) { if (E != null) E.StartRecording(seconds); }
    /// <summary>v6 (tests): records the mix and, with <paramref name="stems"/>, the song bus before the duck, the preview bus after the boost and the
    /// gains (<see cref="SaveStems"/>).</summary>
    public static void StartRecording(float seconds, bool stems) { if (E != null) E.StartRecording(seconds, stems); }
    public static bool RecordingDone => E != null && E.RecordingDone;
    public static bool SaveRecording(string path) { return E != null && E.SaveRecording(path); }
    /// <summary>v6 (tests): writes the stems of the last stems recording (call after <see cref="SaveRecording"/>); <paramref name="gainPath"/> may be null.</summary>
    public static bool SaveStems(string songPath, string previewPath, string gainPath) { return E != null && E.SaveStems(songPath, previewPath, gainPath); }

    // --- diagnostics ---
    public static string[] PresetNames() { return E != null ? E.PresetNames() : new string[0]; }
    public static string Stats() { return E != null ? E.Stats() : "ready=False (no engine)"; }

    // --- v6 diagnostics: voices in the synth, performance ---
    /// <summary>The preset name bank:patch resolves to in the loaded SoundFont.</summary>
    public static string PresetOf(int bank, int patch) => E != null ? E.PresetOf(bank, patch) : "(no engine)";
    /// <summary>True when both synthesizers treat <paramref name="channel"/> as a percussion channel.</summary>
    public static bool IsPercussionChannel(int channel) => E != null && E.ChannelIsPercussion(channel);
    /// <summary>Channels of the song / preview synthesizer (SynthBank.ChannelCount once ready).</summary>
    public static int ChannelCount => E != null ? E.ChannelCount : 0;
    public static int PreviewChannelCount => E != null ? E.PreviewChannelCount : 0;
    /// <summary>The loudness trim programmed on <paramref name="channel"/> of the song synthesizer.</summary>
    public static float ChannelTrim(int channel) => E != null ? E.ChannelTrim(channel) : 1f;
    /// <summary>Voice presets that did not resolve to their expected SoundFont preset at load (0 = all good).</summary>
    public static int PresetsMissing => E != null ? E.PresetsMissing : -1;
    /// <summary>Starts a new performance window: <see cref="PerfAvgCpu"/> / <see cref="PerfMaxCpu"/> (the audio callback's time / the buffer's
    /// duration, 0..1 of one core) and <see cref="PerfMaxVoices"/> (both buses) / <see cref="PerfMaxSongVoices"/> count from here.</summary>
    public static void ResetPerf() { if (E != null) E.ResetPerf(); }
    public static float PerfAvgCpu => E != null ? E.PerfAvgCpu : 0f;
    public static float PerfMaxCpu => E != null ? E.PerfMaxCpu : 0f;
    public static int PerfMaxVoices => E != null ? E.PerfMaxVoices : 0;
    public static int PerfMaxSongVoices => E != null ? E.PerfMaxSongVoices : 0;
    public static int PerfBlocks => E != null ? E.PerfBlocks : 0;
    /// <summary>v6 (tests): the DSP time of the last recording's first frame (-1 before it started; read once <see cref="RecordingDone"/>).</summary>
    public static double RecordingStartDsp => E != null ? E.RecordingStartDsp : -1.0;
    /// <summary>v6 (tests): the level / mute the main thread last gave a slot.</summary>
    public static float SlotGain(int slot) => E != null ? E.SlotGain(slot) : 1f;
    public static bool SlotMuted(int slot) => E != null && E.SlotMuted(slot);
    /// <summary>v6 (tests, main thread): the cost of the synthesizer set-up, v5 table vs v6 table (see SynthEngine.MeasureConfigs).</summary>
    public static string MeasureConfigs() => E != null ? E.MeasureConfigs() : "no engine";
    /// <summary>The SoundFont's parse time (ms), the synthesizers' construction + per-slot setup (ms), the managed memory the SoundFont took
    /// (bytes, GC delta) and its sample data (bytes).</summary>
    public static float LoadMs => E != null ? E.LoadMs : 0f;
    public static float InitMs => E != null ? E.InitMs : 0f;
    public static long SoundFontBytes => E != null ? E.SoundFontBytes : 0L;
    public static long SampleBytes => E != null ? E.SampleBytes : 0L;

    // --- v7 (SPEC v7 §4.4): the launch riser on the song bus's effects channel ---
    /// <summary>v7: a riser note (GeneralUser GS "Reverse Cymbal" on the effects channel, SynthBank.FxChannel) on the song bus under <paramref name="owner"/>
    /// (never the preview owner). <paramref name="key"/> sets the swell's speed (<see cref="RiserPeakSeconds"/>).</summary>
    public static void RiserNote(int key, int velocity, double onDsp, double offDsp, int owner)
    {
        if (owner == PreviewOwner) owner = 0;
        Note(SynthBank.FxSlot, Mathf.Clamp(key, 0, 127), Mathf.Clamp(velocity, 1, 127), onDsp, offDsp, owner);
    }
    /// <summary>v7: seconds from a riser note-on at <paramref name="key"/> to its swell's PEAK (the 10 ms RMS maximum; the sound stops dead a few ms
    /// later), from the render at boot (<see cref="RiserMeasured"/>; before it the lab's numbers for GeneralUser GS).</summary>
    public static double RiserPeakSeconds(int key)
    {
        float root = E != null ? E.RiserPeakRoot : 1.400f, cents = E != null ? E.RiserCentsPerKey : 50f;
        return root * Math.Pow(2.0, -(key - SynthEngine.RiserRootKey) * cents / 1200.0);
    }
    /// <summary>v7: the riser key (inside <paramref name="minKey"/>..<paramref name="maxKey"/>) whose swell peaks nearest <paramref name="seconds"/> after its
    /// note-on; <paramref name="notLonger"/>: the nearest one that is not longer (a swell that must fit in the time left).</summary>
    public static int RiserKeyFor(double seconds, int minKey, int maxKey, bool notLonger)
    {
        float root = E != null ? E.RiserPeakRoot : 1.400f, cents = E != null ? E.RiserCentsPerKey : 50f;
        if (seconds <= 0.0 || cents <= 0f) return maxKey;
        double k = SynthEngine.RiserRootKey - 1200.0 / cents * Math.Log(seconds / root, 2.0);
        int key = notLonger ? (int)Math.Ceiling(k - 1e-6) : (int)Math.Round(k);
        return Mathf.Clamp(key, minKey, maxKey);
    }
    /// <summary>v7: true once the boot render measured the riser (else the lab's numbers are in use); its cost (ms, worker thread), the peak time and
    /// the end of the sound at the root key (s), the scaling (cents per key), the peak's level (dB before the master gain), an error if any.</summary>
    public static bool RiserMeasured => E != null && E.RiserMeasured;
    public static float RiserMeasureMs => E != null ? E.RiserMeasureMs : 0f;
    public static float RiserPeakRoot => E != null ? E.RiserPeakRoot : 1.400f;
    public static float RiserEndRoot => E != null ? E.RiserEndRoot : 1.405f;
    public static float RiserCentsPerKey => E != null ? E.RiserCentsPerKey : 50f;
    public static float RiserPeakDb => E != null ? E.RiserPeakDb : 0f;
    public static string RiserError => E != null ? E.RiserError : "no engine";
    /// <summary>v7: the preset the effects channel resolves to in the loaded SoundFont (want SynthBank.RiserPreset).</summary>
    public static string RiserPresetName => PresetOf(SynthBank.RiserBank, SynthBank.RiserPatch);
}
