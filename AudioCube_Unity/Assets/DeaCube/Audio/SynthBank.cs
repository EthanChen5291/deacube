using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static description of one instrument slot: which SoundFont preset it uses and how notes are
/// shaped for it (register, level trim, effect sends, velocity range).
/// v6 (SPEC v6 §2.7 / §4.1): a slot is one VOICE of an instrument group (<see cref="group"/> = the Instruments role 0..9, <see cref="voice"/> its
/// index in the group, 0 = the group's v5 sound), with a lowercase <see cref="caption"/>, the SoundFont preset name it must resolve to
/// (<see cref="preset"/>) and a static loudness <see cref="trim"/> (linear, 1 = as recorded) matching it to its group's voice 0.
/// </summary>
[Serializable]
public struct InstrumentDef
{
    public string name;
    public int bank;               // SoundFont bank: 0 = GM melodic, 128 = GM percussion
    public int patch;              // GM program number inside the bank
    public int lowMidi, highMidi;  // playable register; ClampToRegister folds notes into it by octaves
    public int octaveShift;        // octave offset applied before the register fold
    public float gain;             // per-instrument level trim (1 = nominal); v6: the GROUP's level (every voice of a group has its voice 0's)
    public float reverb;           // reverb send 0..1 (CC91)
    public float chorus;           // chorus send 0..1 (CC93)
    public float releaseSec;       // typical tail after note-off; callers can use it to size gates
    public bool drums;             // percussion slot: lives on a percussion MIDI channel, keys select drum sounds
    public int velocityMin, velocityMax;  // velocity produced for volume 0 and volume 1
    // v6 (SPEC v6 §4.1)
    public int group;              // the instrument group (Instruments role 0..9) this slot is a voice of
    public int voice;              // its index inside the group (0 = the group's v5 sound)
    public string caption;         // lowercase caption of one or two words ("fm e.piano", "koto")
    public string preset;          // the SoundFont preset name bank:patch must resolve to (checked at load and by V6ChecksA)
    public float trim;             // loudness match to the group's voice 0 (linear amplitude on the slot's channel; voice 0 = 1)
}

/// <summary>
/// The game's instrument table for <see cref="SynthEngine"/>: every slot is bound to its own MIDI channel of the synthesizer
/// (v5: ten slots — melodic slots on channels 0..8, the drum slot on channel 9).
/// v6 (SPEC v6 §2.7 / §4.1, the user: "the instruments should be expanded into groups"): the ten roles became GROUPS of voices drawn from
/// the GeneralUser GS bank we already ship — 72 slots. Slots 0..9 are the v5 table bit for bit (each group's voice 0, same channel,
/// program and level); slots 10.. are the other voices, in group order, each on its own channel (channel = slot), every drum-kit voice on a
/// percussion channel of its own (MeltySynth's channel count and percussion channels are configured from <see cref="ChannelCount"/> /
/// <see cref="PercussionChannels"/>). Every voice keeps its group's level, octave shift and velocity range; its own preset, register window,
/// sends, release and a loudness trim measured offline (K-weighted RMS of C4-ish notes vs voice 0; sustaining groups also the held-note
/// level — scratchpad v6/a/lab). Also holds the drum-row mapping and the register / velocity helpers used when converting tiles to notes.
/// v7 (A, SPEC v7 §4.4 — the launch riser): one EFFECTS channel after the voices (<see cref="FxChannel"/>, played as slot <see cref="FxSlot"/>; not a
/// voice of any group, never in the instrument tables) carries the GeneralUser GS "Reverse Cymbal" (<see cref="RiserBank"/>:<see cref="RiserPatch"/>)
/// on the song bus: the swell that peaks on a launch's landing.
/// </summary>
public static class SynthBank
{
    /// <summary>v6: every voice of every group (v5: 10); v9 (G): + the seven FX voices (slots 72..78).</summary>
    public const int Slots = 79;
    /// <summary>v6: the instrument groups (= Instruments.Count); v9 (G): 11 — group 10 is FX.</summary>
    public const int Groups = 11;

    /// <summary>GM patches; names are verified against the loaded SoundFont by SynthEngine (and V6ChecksA).</summary>
    public static readonly InstrumentDef[] Defs = Build();

    static InstrumentDef[] Build()
    {
        var l = new List<InstrumentDef>
        {
            //  name       bank patch low high oct  gain  rev   cho   rel   drums  vel          caption       preset
            D("Keys",       0,   4,  41,  88,  0, 1.00f, 0.35f, 0.30f, 0.30f, false, 40, 110, 0, "e.piano",    "Tine Electric Piano"),  // Electric Piano 1
            D("Pluck",      0,  24,  40,  84,  0, 1.00f, 0.40f, 0.10f, 0.25f, false, 40, 110, 1, "nylon",      "Nylon Guitar"),         // Nylon Guitar
            D("Pad",        0,  89,  48,  79,  0, 0.90f, 0.60f, 0.30f, 0.60f, false, 40, 105, 2, "warm",       "Warm Pad"),             // Warm Pad
            D("Lead",       0,  80,  55,  91,  0, 0.70f, 0.30f, 0.15f, 0.15f, false, 45, 110, 3, "square",     "Square Lead"),          // Square Lead
            D("Bass",       0,  33,  28,  55, -1, 1.00f, 0.15f, 0.00f, 0.15f, false, 45, 112, 4, "finger",     "Finger Bass"),          // Fingered Bass
            D("Bells",      0,  11,  60,  96,  0, 0.90f, 0.60f, 0.15f, 0.50f, false, 40, 110, 5, "vibraphone", "Vibraphone"),           // Vibraphone
            D("Strings",    0,  48,  43,  88,  0, 0.90f, 0.60f, 0.20f, 0.50f, false, 40, 105, 6, "ensemble",   "Fast Strings"),         // String Ensemble 1
            D("Choir",      0,  52,  48,  84,  0, 0.90f, 0.60f, 0.20f, 0.50f, false, 40, 105, 7, "aahs",       "Concert Choir"),        // Choir Aahs
            D("Piano",      0,   0,  36,  96,  0, 1.00f, 0.35f, 0.00f, 0.30f, false, 40, 112, 8, "grand",      "Grand Piano"),          // Acoustic Grand Piano
            D("Drums",    128,   0,  27,  87,  0, 1.00f, 0.15f, 0.00f, 0.20f, true,  50, 115, 9, "standard",   "Standard 1"),           // Standard Kit
        };
        // v6 voices 1.. of each group: (group, caption, bank, patch, register low / high, loudness trim, reverb, chorus, release, SoundFont preset).
        // Chosen for the game's genres (J-pop / Vocaloid / lo-fi / wa-rock); the trims come from the offline lab (voice 0 = 1).
        V(l, 0, "fm e.piano",       0,   5,  41,  88, 0.88f, 0.35f, 0.30f, 0.30f, "FM Electric Piano");
        V(l, 0, "chorus e.piano",   8,   4,  41,  88, 1.12f, 0.35f, 0.10f, 0.30f, "Chorused Tine EP");
        V(l, 0, "clav",             0,   7,  41,  88, 1.22f, 0.25f, 0.10f, 0.20f, "Clavinet");
        V(l, 0, "harpsichord",      0,   6,  41,  88, 1.11f, 0.35f, 0.10f, 0.30f, "Harpsichord");
        V(l, 0, "organ",            0,  16,  41,  88, 0.72f, 0.30f, 0.15f, 0.20f, "Tonewheel Organ");
        V(l, 0, "accordion",        0,  21,  41,  88, 0.91f, 0.30f, 0.10f, 0.30f, "Accordion");
        V(l, 1, "steel",            0,  25,  40,  84, 1.91f, 0.40f, 0.10f, 0.25f, "Steel Guitar");
        V(l, 1, "clean guitar",     0,  27,  40,  84, 1.14f, 0.40f, 0.10f, 0.25f, "Clean Guitar");
        V(l, 1, "muted guitar",     0,  28,  40,  84, 2.11f, 0.25f, 0.10f, 0.15f, "Muted Guitar");
        V(l, 1, "koto",             0, 107,  40,  84, 1.05f, 0.45f, 0.10f, 0.40f, "Koto");
        V(l, 1, "shamisen",         0, 106,  40,  84, 1.80f, 0.45f, 0.10f, 0.30f, "Shamisen");
        V(l, 1, "harp",             0,  46,  40,  84, 0.58f, 0.50f, 0.10f, 0.60f, "Orchestral Harp");
        V(l, 1, "kalimba",          0, 108,  55,  88, 1.04f, 0.45f, 0.10f, 0.50f, "Kalimba");
        V(l, 1, "pizzicato",        0,  45,  40,  84, 1.43f, 0.50f, 0.10f, 0.25f, "Pizzicato Strings");
        V(l, 2, "polysynth",        0,  90,  48,  79, 1.38f, 0.60f, 0.30f, 0.60f, "Polysynth");
        V(l, 2, "halo",             0,  94,  48,  79, 1.17f, 0.60f, 0.30f, 0.60f, "Halo Pad");
        V(l, 2, "sweep",            0,  95,  48,  79, 0.91f, 0.60f, 0.30f, 0.60f, "Sweep Pad");
        V(l, 2, "solar wind",      11,  89,  48,  79, 2.32f, 0.60f, 0.30f, 0.60f, "Solar Wind");
        V(l, 2, "fantasia",         0,  88,  48,  79, 0.82f, 0.60f, 0.30f, 0.60f, "Fantasia");
        V(l, 2, "bowed glass",      0,  92,  48,  79, 1.57f, 0.60f, 0.30f, 0.60f, "Bowed Glass");
        V(l, 3, "saw",              0,  81,  55,  91, 0.95f, 0.30f, 0.15f, 0.15f, "Saw Lead");
        V(l, 3, "chiff",            0,  83,  55,  91, 0.96f, 0.30f, 0.15f, 0.15f, "Chiffer Lead");
        V(l, 3, "calliope",         0,  82,  55,  91, 0.44f, 0.30f, 0.15f, 0.15f, "Synth Calliope");
        V(l, 3, "flute",            0,  73,  60,  96, 0.75f, 0.40f, 0.10f, 0.15f, "Flute");
        V(l, 3, "trumpet",          0,  56,  55,  91, 0.89f, 0.35f, 0.05f, 0.15f, "Trumpet");
        V(l, 3, "alto sax",         0,  65,  55,  91, 0.92f, 0.35f, 0.05f, 0.15f, "Alto Sax");
        V(l, 3, "shakuhachi",       0,  77,  55,  91, 0.92f, 0.45f, 0.05f, 0.25f, "Shakuhachi");
        V(l, 3, "ocarina",          0,  79,  60,  96, 0.41f, 0.40f, 0.10f, 0.15f, "Ocarina");
        V(l, 4, "pick",             0,  34,  28,  55, 1.95f, 0.15f, 0.00f, 0.15f, "Pick Bass");
        V(l, 4, "fretless",         0,  35,  28,  55, 0.79f, 0.15f, 0.00f, 0.15f, "Fretless Bass");
        V(l, 4, "upright",          0,  32,  28,  55, 1.48f, 0.15f, 0.00f, 0.15f, "Acoustic Bass");
        V(l, 4, "slap",             0,  36,  28,  55, 2.07f, 0.10f, 0.00f, 0.15f, "Slap Bass 1");
        V(l, 4, "synth bass",       0,  38,  28,  55, 0.83f, 0.15f, 0.00f, 0.15f, "Synth Bass 1");
        V(l, 4, "saw bass",        12,  38,  28,  55, 1.27f, 0.15f, 0.00f, 0.15f, "Mean Saw Bass");
        V(l, 5, "celesta",          0,   8,  60,  96, 1.02f, 0.60f, 0.15f, 0.50f, "Celeste");
        V(l, 5, "glockenspiel",     0,   9,  72, 108, 1.78f, 0.60f, 0.15f, 0.60f, "Glockenspiel");
        V(l, 5, "music box",        0,  10,  60, 100, 2.16f, 0.55f, 0.15f, 0.60f, "Music Box");
        V(l, 5, "marimba",          0,  12,  60,  96, 1.22f, 0.45f, 0.05f, 0.30f, "Marimba");
        V(l, 5, "crystal",          0,  98,  60,  96, 1.14f, 0.60f, 0.15f, 0.50f, "Crystal");
        V(l, 5, "steel drums",      0, 114,  55,  88, 1.32f, 0.45f, 0.10f, 0.40f, "Steel Drums");
        V(l, 5, "tubular bells",    0,  14,  60,  84, 1.23f, 0.65f, 0.15f, 1.20f, "Tubular Bells");
        V(l, 6, "slow strings",     0,  49,  43,  88, 1.01f, 0.60f, 0.20f, 0.50f, "Slow Strings");
        V(l, 6, "tremolo",          0,  44,  43,  88, 1.27f, 0.60f, 0.20f, 0.50f, "Tremolo Strings");
        V(l, 6, "violin",           0,  40,  55,  96, 0.53f, 0.60f, 0.20f, 0.50f, "Violin");
        V(l, 6, "cello",            0,  42,  43,  76, 0.61f, 0.60f, 0.20f, 0.50f, "Cello");
        V(l, 6, "synth strings",    0,  50,  43,  88, 0.96f, 0.60f, 0.20f, 0.50f, "Synth Strings 1");
        V(l, 6, "orchestra",       12,  48,  43,  88, 0.76f, 0.60f, 0.20f, 0.50f, "Full Orchestra");
        V(l, 7, "oohs",             0,  53,  48,  84, 1.12f, 0.60f, 0.20f, 0.50f, "Voice Oohs");
        V(l, 7, "synth voice",      0,  54,  48,  84, 1.94f, 0.60f, 0.20f, 0.50f, "Synth Voice");
        V(l, 7, "solo vox",         0,  85,  48,  84, 0.64f, 0.60f, 0.20f, 0.50f, "Solo Vox");
        V(l, 7, "space voice",      0,  91,  48,  84, 2.15f, 0.60f, 0.20f, 0.50f, "Space Voice");
        V(l, 8, "bright grand",     0,   1,  36,  96, 1.04f, 0.35f, 0.00f, 0.30f, "Bright Grand Piano");
        V(l, 8, "electric grand",   0,   2,  36,  96, 1.01f, 0.35f, 0.20f, 0.30f, "Electric Grand Piano");
        V(l, 8, "honky-tonk",       0,   3,  36,  96, 1.29f, 0.35f, 0.00f, 0.30f, "Honky-Tonk Piano");
        V(l, 8, "bell piano",      12,   0,  36,  96, 1.00f, 0.45f, 0.10f, 0.60f, "Bell Piano");
        V(l, 9, "room",           128,   8,  27,  87, 1.08f, 0.25f, 0.00f, 0.20f, "Room");
        V(l, 9, "power",          128,  16,  27,  87, 0.76f, 0.15f, 0.00f, 0.20f, "Power");
        V(l, 9, "electronic",     128,  24,  27,  87, 0.97f, 0.15f, 0.00f, 0.20f, "Electronic");
        V(l, 9, "808/909",        128,  25,  27,  87, 1.19f, 0.15f, 0.00f, 0.20f, "808/909");
        V(l, 9, "dance",          128,  26,  27,  87, 1.06f, 0.15f, 0.00f, 0.20f, "Dance");
        V(l, 9, "jazz",           128,  32,  27,  87, 1.22f, 0.25f, 0.00f, 0.20f, "Jazz");
        V(l, 9, "brush",          128,  40,  27,  87, 1.46f, 0.25f, 0.00f, 0.20f, "Brush");
        // v9 (G, the user: "if we're missing an instrument (and any similar instrument) then add it as a color"): group 10, FX — the GeneralUser GS
        // effects presets (GM programs 97-104 but Crystal, already a bell). Voice 0 = Atmosphere (GM "FX 4", the "Scifi" track of the gallery's
        // "get proto"). Appended after every v6 slot: slots 0..71 keep their index and channel; each FX voice gets its own channel (= slot). The
        // group sounds like a pad (level, register, sends) but is neither sustaining nor mono; the voices' trims are not measured yet (1).
        l.Add(D("FX",          0,  99,  48,  96,  0, 0.90f, 0.60f, 0.30f, 0.60f, false, 40, 105, 10, "atmosphere", "Atmosphere"));
        V(l, 10, "ice rain",        0,  96,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Ice Rain");
        V(l, 10, "soundtrack",      0,  97,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Soundtrack");
        V(l, 10, "brightness",      0, 100,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Brightness");
        V(l, 10, "goblin",          0, 101,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Goblin");
        V(l, 10, "echo drops",      0, 102,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Echo Drops");
        V(l, 10, "star theme",      0, 103,  48,  96, 1.00f, 0.60f, 0.30f, 0.60f, "Star Theme");
        return l.ToArray();
    }

    static readonly int[] channelOf = new int[Slots];
    static readonly int drumSlot;
    static readonly int[][] groupSlots = new int[Groups][];
    static readonly bool[] percussionChannel;
    static readonly int[] percussionChannels;

    static SynthBank()
    {
        if (Defs.Length != Slots) Debug.LogError("SynthBank: " + Defs.Length + " defs for " + Slots + " slots");
        // v5: melodic slots took the channels in order but skipped 9 (MIDI's percussion channel), the drum slot took 9 — for the v5 table that
        // is channel = slot. v6 keeps channel = slot for every voice (unique by construction); each drum-kit voice's channel is a percussion one.
        drumSlot = -1;
        var perc = new List<int>();
        percussionChannel = new bool[Slots];
        var lists = new List<int>[Groups];
        for (int g = 0; g < Groups; g++) lists[g] = new List<int>();
        for (int i = 0; i < Slots && i < Defs.Length; i++)
        {
            channelOf[i] = i;
            if (Defs[i].drums) { perc.Add(i); percussionChannel[i] = true; if (drumSlot < 0) drumSlot = i; }
            int g = Mathf.Clamp(Defs[i].group, 0, Groups - 1);
            lists[g].Add(i);
        }
        for (int g = 0; g < Groups; g++)
        {
            lists[g].Sort((a, b) => Defs[a].voice.CompareTo(Defs[b].voice));
            groupSlots[g] = lists[g].ToArray();
        }
        percussionChannels = perc.ToArray();
    }

    /// <summary>The percussion slot index (-1 if the table has none): the v5 kit (voice 0 of the drums group).</summary>
    public static int DrumSlot => drumSlot;

    /// <summary>MIDI channel that plays the given slot (v6: channel = slot, 0..<see cref="ChannelCount"/> - 1; v7: <see cref="FxSlot"/> → <see cref="FxChannel"/>).</summary>
    public static int ChannelOf(int slot) => slot == FxSlot ? FxChannel : channelOf[Mathf.Clamp(slot, 0, Slots - 1)];

    /// <summary>v6: MIDI channels the synthesizers need (one per slot); v7: + the effects channel.</summary>
    public static int ChannelCount => Slots + FxChannels;

    // ---- v7: the effects channel (the launch riser)
    /// <summary>v7: effects channels after the voice slots (1: the riser).</summary>
    public const int FxChannels = 1;
    /// <summary>v7: the slot id a SynthEvent uses for the effects channel (= <see cref="Slots"/>: never a voice; SynthEngine maps it to <see cref="FxChannel"/>).</summary>
    public const int FxSlot = Slots;
    /// <summary>v7: the effects channel (right after the voices' channels; melodic).</summary>
    public const int FxChannel = Slots;
    /// <summary>v7: the riser's preset — GeneralUser GS bank 0 patch 119 "Reverse Cymbal" (a reversed cymbal: it swells and stops dead at its peak).</summary>
    public const int RiserBank = 0, RiserPatch = 119;
    public const string RiserPreset = "Reverse Cymbal";
    /// <summary>v6: the percussion channels (every drum-kit voice's), for MeltySynth's SynthesizerSettings.PercussionChannels (a copy).</summary>
    public static int[] PercussionChannels => (int[])percussionChannels.Clone();
    /// <summary>v6: true when <paramref name="channel"/> is a drum-kit voice's channel (bank select adds 128 there).</summary>
    public static bool IsPercussionChannel(int channel) => channel >= 0 && channel < Slots && percussionChannel[channel];

    /// <summary>Definition of a slot (index is clamped).</summary>
    public static InstrumentDef Def(int slot) => Defs[Mathf.Clamp(slot, 0, Slots - 1)];

    // ---- v6 groups
    /// <summary>The instrument group (Instruments role) slot <paramref name="slot"/> is a voice of.</summary>
    public static int GroupOf(int slot) => Def(slot).group;
    /// <summary>The voice index of slot <paramref name="slot"/> inside its group (0 = the group's v5 sound).</summary>
    public static int VoiceOf(int slot) => Def(slot).voice;
    /// <summary>Voices in group <paramref name="group"/> (at least 1).</summary>
    public static int VoiceCount(int group) => groupSlots[Mathf.Clamp(group, 0, Groups - 1)].Length;
    /// <summary>The slot of voice <paramref name="voice"/> of group <paramref name="group"/>; a voice out of range plays the group's voice 0.</summary>
    public static int SlotOf(int group, int voice)
    {
        var s = groupSlots[Mathf.Clamp(group, 0, Groups - 1)];
        return voice >= 0 && voice < s.Length ? s[voice] : s[0];
    }
    /// <summary>Every slot of group <paramref name="group"/>, voice order (a copy).</summary>
    public static int[] SlotsOf(int group) => (int[])groupSlots[Mathf.Clamp(group, 0, Groups - 1)].Clone();

    /// <summary>GM drum keys, in the order rows map to them:
    /// kick, snare, closed hat, open hat, clap, rim, low tom, high tom, crash, ride, tambourine, cowbell, claves, mute triangle.</summary>
    public static readonly int[] DrumKit = { 36, 38, 42, 46, 39, 37, 45, 48, 49, 51, 54, 56, 75, 80 };

    /// <summary>Drum key for a tile row: row 0 = kick, 1 = snare, 2 = closed hat, 3 = open hat, then clap, rim, ... cyclic.</summary>
    public static int DrumForRow(int row, int rows)
    {
        int n = DrumKit.Length;
        if (rows > 0) row = ((row % rows) + rows) % rows;
        return DrumKit[((row % n) + n) % n];
    }

    /// <summary>Applies the slot's octave shift and folds the note by octaves into [lowMidi, highMidi].
    /// Drum slots return the key unchanged (drum keys are sounds, not pitches).</summary>
    public static int ClampToRegister(int slot, int midi)
    {
        InstrumentDef d = Def(slot);
        if (d.drums) return Mathf.Clamp(midi, 0, 127);
        midi += 12 * d.octaveShift;
        int low = Mathf.Min(d.lowMidi, d.highMidi), high = Mathf.Max(d.lowMidi, d.highMidi);
        while (midi < low) midi += 12;
        while (midi > high) midi -= 12;
        if (midi < low) midi = low;      // register narrower than an octave: clamp
        return Mathf.Clamp(midi, 0, 127);
    }

    /// <summary>Musical velocity curve: volume 0..1 maps onto [velocityMin, velocityMax] with a gentle
    /// compressive curve (volume^0.7), and accent 0..1 pushes the result toward 127. Always 1..127.</summary>
    public static int Velocity(int slot, float volume01, float accent01)
    {
        InstrumentDef d = Def(slot);
        float v = Mathf.Clamp01(volume01), a = Mathf.Clamp01(accent01);
        float vel = d.velocityMin + (d.velocityMax - d.velocityMin) * Mathf.Pow(v, 0.7f);
        vel += a * (127f - vel);
        return Mathf.Clamp(Mathf.RoundToInt(vel), 1, 127);
    }

    /// <summary>v6: how loud a PRESS of each group sounds, by pitch — RMS dB, 50-300 ms after the onset (the window SPEC v6 §4.2 measures), of a
    /// 0.5 s note at velocity 121 (the press velocity: presets switch sample layers with velocity, so the level is measured there, not scaled
    /// from 100) on the group's voice 0 with its CC7 level and sends, MeltySynth's master volume 0.5, before SynthEngine's master gain; every
    /// key 21..108 (sample zones make neighbouring keys differ by several dB; the drums: the standard kit's pieces, -99 = no sound). Measured
    /// offline (scratchpad v6/a/lab/Cal.cs; it matched the game's recorded presses within 0.8 dB); every voice is loudness-matched to its
    /// group's voice 0. SynthEngine sizes a press's boost with it.</summary>
    static readonly float[,] PressLevels =
    {
        {   // keys, keys 21-108
            -29.8f, -29.8f, -30.2f, -31.0f, -31.5f, -31.4f, -31.9f, -32.0f, -32.7f, -33.2f, -33.4f, -34.4f, -34.0f, -35.0f, -35.7f, -36.5f, -36.9f, -37.4f, -37.4f, -38.0f, -39.3f, -38.8f,
            -39.1f, -40.4f, -40.0f, -35.4f, -35.9f, -36.8f, -36.7f, -37.7f, -37.8f, -40.2f, -35.4f, -35.5f, -35.4f, -37.2f, -36.7f, -36.1f, -36.2f, -36.7f, -29.5f, -30.2f, -29.6f, -32.3f,
            -33.6f, -34.3f, -34.6f, -36.3f, -36.5f, -36.3f, -36.2f, -35.6f, -37.2f, -39.3f, -35.2f, -38.1f, -38.3f, -39.1f, -32.7f, -33.3f, -36.4f, -37.2f, -39.3f, -38.6f, -40.3f, -43.2f,
            -41.3f, -43.8f, -45.1f, -45.1f, -41.3f, -41.1f, -44.7f, -43.6f, -47.1f, -46.4f, -52.0f, -51.8f, -53.9f, -52.4f, -54.4f, -56.8f, -56.1f, -57.7f, -59.7f, -59.5f, -60.7f, -61.5f
        },
        {   // pluck, keys 21-108
            -28.0f, -27.8f, -27.9f, -28.4f, -28.6f, -28.1f, -28.6f, -28.4f, -28.9f, -29.0f, -28.6f, -29.3f, -28.4f, -29.0f, -29.2f, -29.3f, -29.6f, -29.4f, -29.2f, -29.5f, -29.9f, -29.3f,
            -30.0f, -30.0f, -29.0f, -28.9f, -29.2f, -29.5f, -28.7f, -29.6f, -28.9f, -29.5f, -29.3f, -28.6f, -29.0f, -29.4f, -29.2f, -29.7f, -29.9f, -29.6f, -28.8f, -29.5f, -29.0f, -30.2f,
            -28.2f, -28.7f, -28.0f, -33.3f, -34.5f, -34.1f, -34.1f, -33.1f, -34.8f, -36.2f, -32.9f, -34.6f, -35.2f, -35.1f, -35.1f, -35.0f, -37.0f, -37.3f, -37.4f, -36.5f, -38.7f, -39.9f,
            -38.1f, -40.1f, -40.0f, -38.2f, -38.0f, -38.3f, -39.6f, -40.0f, -39.3f, -40.7f, -41.5f, -40.7f, -41.8f, -39.1f, -40.2f, -42.2f, -40.6f, -40.8f, -42.2f, -40.9f, -41.2f, -41.0f
        },
        {   // pad, keys 21-108
            -43.4f, -42.7f, -42.5f, -42.5f, -42.3f, -41.6f, -41.1f, -40.3f, -40.3f, -40.5f, -40.0f, -39.7f, -39.3f, -38.9f, -39.3f, -39.4f, -39.6f, -39.7f, -43.2f, -43.3f, -43.2f, -39.9f,
            -40.3f, -40.7f, -42.6f, -43.1f, -43.0f, -39.6f, -39.1f, -38.7f, -40.5f, -41.7f, -41.8f, -38.9f, -40.7f, -42.7f, -42.3f, -42.4f, -42.6f, -42.0f, -41.4f, -37.3f, -37.0f, -38.5f,
            -39.8f, -47.5f, -46.9f, -46.5f, -41.5f, -42.1f, -41.9f, -47.3f, -47.1f, -47.8f, -44.6f, -46.2f, -47.3f, -47.7f, -48.1f, -46.3f, -41.0f, -39.9f, -41.6f, -44.0f, -44.6f, -44.9f,
            -44.7f, -44.4f, -45.4f, -46.4f, -46.7f, -46.5f, -50.6f, -48.8f, -50.1f, -46.3f, -47.2f, -47.2f, -47.0f, -48.1f, -49.5f, -50.9f, -51.5f, -51.9f, -54.3f, -54.9f, -53.9f, -55.6f
        },
        {   // lead, keys 21-108
            -27.3f, -27.4f, -27.4f, -27.5f, -27.6f, -27.7f, -27.8f, -27.8f, -27.9f, -28.0f, -28.1f, -28.2f, -28.4f, -28.5f, -28.6f, -28.8f, -29.0f, -29.2f, -29.4f, -29.7f, -30.0f, -30.3f,
            -30.6f, -30.9f, -31.3f, -31.6f, -32.0f, -32.4f, -32.7f, -32.8f, -32.9f, -32.9f, -32.9f, -32.8f, -32.6f, -32.7f, -32.8f, -32.9f, -33.0f, -33.2f, -34.0f, -35.0f, -35.6f, -35.9f,
            -36.0f, -36.0f, -35.9f, -35.6f, -34.9f, -34.1f, -32.9f, -31.2f, -30.3f, -29.9f, -30.0f, -30.4f, -30.1f, -29.4f, -29.1f, -29.9f, -31.5f, -32.4f, -32.0f, -31.8f, -32.2f, -32.2f,
            -32.5f, -31.6f, -32.0f, -32.6f, -31.7f, -32.2f, -32.1f, -32.7f, -33.5f, -32.3f, -33.4f, -32.8f, -33.6f, -32.7f, -32.6f, -32.7f, -33.3f, -33.6f, -33.9f, -33.4f, -33.9f, -34.7f
        },
        {   // bass, keys 21-108
            -24.8f, -25.3f, -25.5f, -25.9f, -26.3f, -25.9f, -26.3f, -26.3f, -26.5f, -26.7f, -24.4f, -24.9f, -24.4f, -24.8f, -24.8f, -26.4f, -26.5f, -26.4f, -26.5f, -26.5f, -26.5f, -26.4f,
            -26.3f, -26.7f, -26.4f, -26.4f, -26.4f, -26.7f, -26.4f, -26.6f, -26.4f, -27.1f, -27.6f, -27.5f, -27.7f, -28.2f, -28.2f, -28.4f, -28.4f, -28.8f, -29.0f, -29.3f, -28.7f, -30.2f,
            -31.4f, -31.3f, -31.9f, -32.9f, -33.1f, -33.5f, -34.5f, -34.4f, -35.8f, -36.8f, -35.5f, -37.8f, -37.8f, -39.1f, -40.1f, -40.6f, -42.8f, -43.5f, -45.8f, -46.4f, -47.4f, -49.2f,
            -49.5f, -51.1f, -52.2f, -53.7f, -55.5f, -56.2f, -33.4f, -33.0f, -35.1f, -35.3f, -36.6f, -38.2f, -38.5f, -38.4f, -40.7f, -41.1f, -43.1f, -43.9f, -44.8f, -46.1f, -47.2f, -47.3f
        },
        {   // bells, keys 21-108
            -29.3f, -30.5f, -29.6f, -29.4f, -28.5f, -28.2f, -27.4f, -27.4f, -29.2f, -29.3f, -29.8f, -30.6f, -28.2f, -27.5f, -27.3f, -28.9f, -27.5f, -27.4f, -26.6f, -28.3f, -30.4f, -29.5f,
            -29.5f, -31.4f, -29.6f, -30.4f, -30.8f, -30.1f, -28.6f, -28.8f, -27.9f, -28.3f, -29.3f, -28.1f, -28.5f, -29.3f, -27.4f, -26.6f, -26.1f, -28.3f, -25.8f, -25.7f, -24.7f, -27.1f,
            -30.8f, -29.2f, -30.1f, -33.1f, -30.9f, -31.4f, -32.6f, -30.4f, -32.9f, -34.9f, -30.3f, -33.7f, -34.3f, -35.7f, -36.7f, -35.9f, -39.7f, -39.5f, -41.8f, -42.2f, -44.5f, -47.9f,
            -44.8f, -47.8f, -47.4f, -46.3f, -47.5f, -49.4f, -50.6f, -52.4f, -51.7f, -52.9f, -54.6f, -55.0f, -57.8f, -57.1f, -59.9f, -59.0f, -60.5f, -61.5f, -63.1f, -65.1f, -64.1f, -65.8f
        },
        {   // strings, keys 21-108
            -33.9f, -33.9f, -33.3f, -32.9f, -32.7f, -31.7f, -31.6f, -31.2f, -31.9f, -32.3f, -32.1f, -31.7f, -30.7f, -30.5f, -31.0f, -31.0f, -29.9f, -29.7f, -29.9f, -30.2f, -30.2f, -30.5f,
            -30.2f, -30.6f, -30.0f, -31.3f, -30.5f, -31.1f, -31.3f, -31.3f, -31.3f, -27.8f, -29.5f, -28.6f, -32.4f, -32.7f, -30.9f, -35.0f, -35.2f, -34.4f, -31.2f, -32.1f, -31.1f, -31.1f,
            -30.9f, -31.9f, -30.6f, -31.2f, -30.7f, -30.3f, -30.6f, -29.8f, -32.3f, -33.1f, -31.6f, -31.9f, -32.2f, -32.6f, -31.5f, -31.3f, -32.1f, -29.6f, -30.7f, -30.4f, -29.9f, -31.0f,
            -29.9f, -31.0f, -32.4f, -32.4f, -33.1f, -32.8f, -33.2f, -32.2f, -33.2f, -32.5f, -32.0f, -32.2f, -31.9f, -31.8f, -32.7f, -32.7f, -32.9f, -32.6f, -32.9f, -32.8f, -33.0f, -32.8f
        },
        {   // choir, keys 21-108
            -35.5f, -35.3f, -34.7f, -34.4f, -34.3f, -34.4f, -33.7f, -33.4f, -34.0f, -34.3f, -34.6f, -35.1f, -35.2f, -34.6f, -34.6f, -34.2f, -34.1f, -34.3f, -34.2f, -34.4f, -34.4f, -34.2f,
            -34.0f, -34.0f, -33.9f, -34.1f, -34.1f, -36.1f, -36.0f, -35.9f, -35.0f, -34.5f, -34.5f, -35.4f, -34.5f, -34.6f, -34.4f, -33.6f, -33.5f, -33.3f, -32.4f, -32.4f, -30.3f, -32.1f,
            -32.0f, -31.4f, -31.1f, -31.8f, -31.8f, -31.6f, -31.7f, -31.3f, -30.7f, -30.2f, -32.5f, -32.7f, -31.7f, -31.3f, -30.9f, -30.8f, -31.8f, -31.5f, -31.7f, -30.8f, -29.4f, -29.8f,
            -30.0f, -28.8f, -29.6f, -31.5f, -30.2f, -30.0f, -31.6f, -29.8f, -31.2f, -30.3f, -31.3f, -31.0f, -31.9f, -30.9f, -31.7f, -31.2f, -30.5f, -31.1f, -31.8f, -31.4f, -30.1f, -31.7f
        },
        {   // piano, keys 21-108
            -33.0f, -32.8f, -33.1f, -33.2f, -33.2f, -33.0f, -33.1f, -33.9f, -34.1f, -33.8f, -33.6f, -33.8f, -33.3f, -33.5f, -33.2f, -33.5f, -33.7f, -33.3f, -33.9f, -33.7f, -32.0f, -31.8f,
            -32.1f, -31.9f, -31.8f, -32.1f, -30.7f, -30.7f, -30.5f, -30.4f, -28.0f, -29.3f, -30.6f, -29.9f, -29.8f, -32.2f, -32.0f, -32.3f, -31.9f, -32.0f, -30.7f, -30.9f, -30.2f, -31.8f,
            -31.2f, -30.0f, -29.9f, -30.9f, -30.6f, -31.8f, -31.7f, -31.2f, -32.6f, -33.9f, -29.8f, -32.1f, -29.7f, -30.4f, -30.6f, -30.3f, -31.8f, -35.9f, -34.7f, -33.7f, -38.1f, -36.5f,
            -36.3f, -35.5f, -35.2f, -34.2f, -35.1f, -34.1f, -40.0f, -43.2f, -40.9f, -41.9f, -42.0f, -43.3f, -44.6f, -43.7f, -43.7f, -43.6f, -44.7f, -45.5f, -46.6f, -48.7f, -47.7f, -49.7f
        },
        {   // drums, keys 21-108
            -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -38.0f, -41.6f, -37.5f, -37.2f, -51.4f, -70.5f, -64.7f, -43.3f, -35.0f, -33.9f, -48.4f, -40.8f, -40.9f, -37.5f, -31.7f, -53.7f,
            -32.4f, -57.5f, -30.9f, -40.0f, -30.2f, -29.0f, -30.2f, -31.8f, -48.2f, -33.5f, -41.2f, -40.4f, -37.7f, -45.1f, -31.0f, -37.3f, -48.3f, -54.4f, -51.8f, -57.4f, -37.4f, -35.0f,
            -41.0f, -34.5f, -51.6f, -47.4f, -48.4f, -55.3f, -40.0f, -31.4f, -62.8f, -41.6f, -55.5f, -69.1f, -63.5f, -32.6f, -34.9f, -88.2f, -40.7f, -54.0f, -33.2f, -46.3f, -68.0f, -47.4f,
            -28.6f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f, -99.0f
        },
        {   // fx (v9 G): not measured yet — the pad row (Atmosphere is a pad with a soft pluck), keys 21-108
            -43.4f, -42.7f, -42.5f, -42.5f, -42.3f, -41.6f, -41.1f, -40.3f, -40.3f, -40.5f, -40.0f, -39.7f, -39.3f, -38.9f, -39.3f, -39.4f, -39.6f, -39.7f, -43.2f, -43.3f, -43.2f, -39.9f,
            -40.3f, -40.7f, -42.6f, -43.1f, -43.0f, -39.6f, -39.1f, -38.7f, -40.5f, -41.7f, -41.8f, -38.9f, -40.7f, -42.7f, -42.3f, -42.4f, -42.6f, -42.0f, -41.4f, -37.3f, -37.0f, -38.5f,
            -39.8f, -47.5f, -46.9f, -46.5f, -41.5f, -42.1f, -41.9f, -47.3f, -47.1f, -47.8f, -44.6f, -46.2f, -47.3f, -47.7f, -48.1f, -46.3f, -41.0f, -39.9f, -41.6f, -44.0f, -44.6f, -44.9f,
            -44.7f, -44.4f, -45.4f, -46.4f, -46.7f, -46.5f, -50.6f, -48.8f, -50.1f, -46.3f, -47.2f, -47.2f, -47.0f, -48.1f, -49.5f, -50.9f, -51.5f, -51.9f, -54.3f, -54.9f, -53.9f, -55.6f
        },
    };

    /// <summary>v6: the level of a press of <paramref name="slot"/> on <paramref name="key"/> at velocity 121 (see <see cref="PressLevels"/>; keys
    /// outside 21..108 clamp; a silent kit key takes the nearest measured one).</summary>
    public static float PressLevelDb(int slot, int key)
    {
        InstrumentDef d = Def(slot);
        int g = Mathf.Clamp(d.group, 0, Groups - 1);
        int k = Mathf.Clamp(key, 21, 108) - 21;
        float v = PressLevels[g, k];
        for (int r = 1; v < -80f && r < 88; r++)   // a key without a sound (outside the kit): the nearest measured one
        {
            if (k - r >= 0 && PressLevels[g, k - r] > -80f) { v = PressLevels[g, k - r]; break; }
            if (k + r < 88 && PressLevels[g, k + r] > -80f) { v = PressLevels[g, k + r]; break; }
        }
        return v;
    }

    static InstrumentDef D(string name, int bank, int patch, int low, int high, int oct, float gain, float reverb, float chorus, float release, bool drums, int vmin, int vmax,
                           int group, string caption, string preset)
    {
        return new InstrumentDef
        {
            name = name, bank = bank, patch = patch, lowMidi = low, highMidi = high, octaveShift = oct,
            gain = gain, reverb = reverb, chorus = chorus, releaseSec = release, drums = drums,
            velocityMin = vmin, velocityMax = vmax,
            group = group, voice = 0, caption = caption, preset = preset, trim = 1f
        };
    }

    /// <summary>v6: appends voice n (the next index) of <paramref name="group"/>: the group's level, octave shift, velocity range and drum flag
    /// (from its voice 0, slot = group), its own preset, register window, trim, sends and release.</summary>
    static void V(List<InstrumentDef> l, int group, string caption, int bank, int patch, int low, int high, float trim, float reverb, float chorus, float release, string preset)
    {
        InstrumentDef g = l[group];
        for (int i = 0; i < l.Count; i++) if (l[i].group == group && l[i].voice == 0) { g = l[i]; break; }   // v9: group 10's voice 0 is not at index 10 (groups 0..9: l[group] as before)
        int n = 0;
        for (int i = 0; i < l.Count; i++) if (l[i].group == group) n++;
        l.Add(new InstrumentDef
        {
            name = g.name + "/" + caption, bank = bank, patch = patch, lowMidi = low, highMidi = high, octaveShift = g.octaveShift,
            gain = g.gain, reverb = reverb, chorus = chorus, releaseSec = release, drums = g.drums,
            velocityMin = g.velocityMin, velocityMax = g.velocityMax,
            group = group, voice = n, caption = caption, preset = preset, trim = trim
        });
    }
}
