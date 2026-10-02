using System;
using System.Collections.Generic;
using UnityEngine;

public enum ChordRole { Root, Third, Fifth, Seventh, Extension, Other }
public enum ChordQuality { Major7, Minor7, Dominant7, Sus4, Major9, Minor9, Dominant9 }

/// <summary>Chord role detection, colour language and an offline progression generator.</summary>
public static class MusicTheory
{
    public static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static ChordRole RoleOf(int semitoneFromRoot)
    {
        int s = ((semitoneFromRoot % 12) + 12) % 12;
        switch (s)
        {
            case 0: return ChordRole.Root;
            case 3: case 4: return ChordRole.Third;
            case 6: case 7: case 8: return ChordRole.Fifth;
            case 10: case 11: return ChordRole.Seventh;
            case 1: case 2: case 5: case 9: return ChordRole.Extension;
            default: return ChordRole.Other;
        }
    }

    public static bool IsMinor(IList<int> semis) { return Contains(semis, 3) && !Contains(semis, 4); }
    public static bool IsDominant(IList<int> semis) { return Contains(semis, 4) && Contains(semis, 10); }
    public static bool IsSus(IList<int> semis) { return Contains(semis, 5) && !Contains(semis, 3) && !Contains(semis, 4); }
    static bool Contains(IList<int> l, int v) { for (int i = 0; i < l.Count; i++) if (((l[i] % 12) + 12) % 12 == v) return true; return false; }

    public static ChordQuality QualityOf(IList<int> semis)
    {
        bool ninth = Contains(semis, 2);
        if (IsSus(semis)) return ChordQuality.Sus4;
        if (IsDominant(semis)) return ninth ? ChordQuality.Dominant9 : ChordQuality.Dominant7;
        if (IsMinor(semis)) return ninth ? ChordQuality.Minor9 : ChordQuality.Minor7;
        return ninth ? ChordQuality.Major9 : ChordQuality.Major7;
    }

    public static int[] Semitones(ChordQuality q)
    {
        switch (q)
        {
            case ChordQuality.Major7: return new[] { 0, 4, 7, 11 };
            case ChordQuality.Major9: return new[] { 0, 4, 7, 11, 14 };
            case ChordQuality.Minor7: return new[] { 0, 3, 7, 10 };
            case ChordQuality.Minor9: return new[] { 0, 3, 7, 10, 14 };
            case ChordQuality.Dominant7: return new[] { 0, 4, 7, 10 };
            case ChordQuality.Dominant9: return new[] { 0, 4, 7, 10, 14 };
            case ChordQuality.Sus4: return new[] { 0, 5, 7, 10 };
        }
        return new[] { 0, 4, 7 };
    }

    /// <summary>SPEC v5 §2.2: the chord's glyph is its vibe's weather glyph (sun, rain cloud, storm …; was a triangle / diamond / ring by quality).</summary>
    public static string QualityIcon(ChordQuality q) => Vibe.Icon(Vibe.Of(q));

    public static string ChordName(int rootMidi, IList<int> semis)
    {
        string n = NoteNames[((rootMidi % 12) + 12) % 12];
        switch (QualityOf(semis))
        {
            case ChordQuality.Major7: return n + "maj7";
            case ChordQuality.Major9: return n + "maj9";
            case ChordQuality.Minor7: return n + "m7";
            case ChordQuality.Minor9: return n + "m9";
            case ChordQuality.Dominant7: return n + "7";
            case ChordQuality.Dominant9: return n + "9";
            case ChordQuality.Sus4: return n + "sus4";
        }
        return n;
    }

    /// <summary>The reversible mood/climate view of a stored quality (SPEC §3.5): family 0 = as generated, 1 sun, 2 cloud, 3 storm.
    /// Row-count preserving: 7ths map to 7ths (Sus4 keeps its shape in a storm), 9ths to 9ths.</summary>
    public static ChordQuality MoodMap(ChordQuality stored, int family)
    {
        bool ninth = stored == ChordQuality.Major9 || stored == ChordQuality.Minor9 || stored == ChordQuality.Dominant9;
        switch (family)
        {
            case 1: return ninth ? ChordQuality.Major9 : ChordQuality.Major7;
            case 2: return ninth ? ChordQuality.Minor9 : ChordQuality.Minor7;
            case 3: return ninth ? ChordQuality.Dominant9 : (stored == ChordQuality.Sus4 ? ChordQuality.Sus4 : ChordQuality.Dominant7);
            default: return stored;
        }
    }

    /// <summary>The chord tones an island sounds: the stored tones viewed through its mood (or the song's climate when mood == 0);
    /// family 0 => the stored tones. Never changes the row count (falls back to the stored tones) and never mutates its input.</summary>
    public static int[] EffectiveSemis(int[] storedSemis, int mood, int climate)
    {
        if (storedSemis == null || storedSemis.Length == 0) return new[] { 0, 4, 7 };
        int family = mood != 0 ? mood : climate;
        if (family <= 0 || family > 3) return (int[])storedSemis.Clone();
        var mapped = Semitones(MoodMap(QualityOf(storedSemis), family));
        return mapped.Length == storedSemis.Length ? mapped : (int[])storedSemis.Clone();
    }

    /// <summary>Hue of a chord root arranged around the circle of fifths, so neighbouring chords share colour families.</summary>
    public static float HueOfRoot(int midi)
    {
        int pc = ((midi % 12) + 12) % 12;
        int fifthsIndex = (pc * 7) % 12;
        return fifthsIndex / 12f;
    }

    /// <summary>SPEC v5 §2.2: a chord's colour is its vibe's family colour (was a hue by root around the circle of fifths, which read as random).</summary>
    public static Color ChordColor(int rootMidi, IList<int> semis) => Vibe.ChordColor(rootMidi, semis);

    // ---------- offline generator (no API needed) ----------
    struct Step { public int degree; public ChordQuality q; public Step(int d, ChordQuality q) { degree = d; this.q = q; } }
    static readonly Step[][] Templates =
    {
        new[] { new Step(0, ChordQuality.Major9), new Step(9, ChordQuality.Minor7), new Step(5, ChordQuality.Major7), new Step(7, ChordQuality.Dominant7) },
        new[] { new Step(2, ChordQuality.Minor7), new Step(7, ChordQuality.Dominant7), new Step(0, ChordQuality.Major9), new Step(0, ChordQuality.Major7) },
        new[] { new Step(9, ChordQuality.Minor9), new Step(5, ChordQuality.Major7), new Step(0, ChordQuality.Major9), new Step(7, ChordQuality.Sus4) },
        new[] { new Step(0, ChordQuality.Minor9), new Step(8, ChordQuality.Major7), new Step(3, ChordQuality.Major9), new Step(10, ChordQuality.Dominant7) },
        new[] { new Step(0, ChordQuality.Major7), new Step(4, ChordQuality.Minor7), new Step(5, ChordQuality.Major9), new Step(2, ChordQuality.Minor7), new Step(7, ChordQuality.Dominant7), new Step(0, ChordQuality.Major9) },
        new[] { new Step(0, ChordQuality.Minor7), new Step(5, ChordQuality.Minor9), new Step(10, ChordQuality.Major7), new Step(7, ChordQuality.Dominant7) },
    };

    public static SongManager.SongData RandomSong(int seed, string name = "")
    {
        var rng = new System.Random(seed);
        var tpl = Templates[rng.Next(Templates.Length)];
        int key = rng.Next(12);
        var song = new SongManager.SongData
        {
            songName = string.IsNullOrEmpty(name) ? "offline " + NoteNames[key] : name,
            bpm = 84 + rng.Next(0, 44),
            timeSignature = "4/4",
            demoSeed = seed,
            measures = new SongManager.MeasureData[tpl.Length]
        };
        for (int i = 0; i < tpl.Length; i++)
        {
            int root = 55 + ((key + tpl[i].degree) % 12);
            var semis = Semitones(tpl[i].q);
            song.measures[i] = new SongManager.MeasureData
            {
                index = i + 1,
                chordKey = ChordName(root, semis),
                chordRootMIDI = root,
                semitones = semis,
                measureDuration = 4f,
                bars = 1
            };
        }
        return song;
    }

    public static int SeedFromPrompt(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return Environment.TickCount;
        int h = 17; foreach (char c in prompt) h = h * 31 + c; return h;
    }

    /// <summary>A sensible follow-up chord for "add measure": a fifth up in the same colour family.</summary>
    public static SongManager.MeasureData NextChord(SongManager.MeasureData prev)
    {
        var q = QualityOf(prev.semitones);
        int root = prev.chordRootMIDI + 5; if (root > 67) root -= 12;
        ChordQuality nq = q == ChordQuality.Dominant7 ? ChordQuality.Major7 : (q == ChordQuality.Minor7 || q == ChordQuality.Minor9 ? ChordQuality.Dominant7 : ChordQuality.Minor7);
        var semis = Semitones(nq);
        return new SongManager.MeasureData { chordKey = ChordName(root, semis), chordRootMIDI = root, semitones = semis, measureDuration = 4f, bars = prev.bars <= 0 ? 1 : prev.bars };
    }

    // ---------- v3 island tray (SPEC v3 §3.1): the song's key, its seven diatonic sevenths, "next" suggestions, borrowed colours ----------
    /// <summary>A song key: tonic pitch class 0..11 and mode.</summary>
    public struct SongKey
    {
        public int tonic; public bool minor;
        public SongKey(int tonic, bool minor) { this.tonic = PitchClass(tonic); this.minor = minor; }
        public override string ToString() => NoteNames[tonic] + (minor ? " minor" : " major");
    }

    static readonly int[] MajorSteps = { 0, 2, 4, 5, 7, 9, 11 };
    static readonly int[] MinorSteps = { 0, 2, 3, 5, 7, 8, 10 };
    // major: I maj7, ii m7, iii m7, IV maj7, V 7, vi m7, vii -> Sus4; minor: i m7, ii -> Sus4, III maj7, iv m7, v m7, VI maj7, VII 7
    static readonly ChordQuality[] MajorSevenths = { ChordQuality.Major7, ChordQuality.Minor7, ChordQuality.Minor7, ChordQuality.Major7, ChordQuality.Dominant7, ChordQuality.Minor7, ChordQuality.Sus4 };
    static readonly ChordQuality[] MinorSevenths = { ChordQuality.Minor7, ChordQuality.Sus4, ChordQuality.Major7, ChordQuality.Minor7, ChordQuality.Minor7, ChordQuality.Major7, ChordQuality.Dominant7 };
    /// <summary>"Next" ranking by scale degree: I -> IV, vi, V, ii; ii -> V, IV; iii -> vi, IV; IV -> V, I, ii; V -> I, vi; vi -> IV, ii, V; vii -> I (minor mirrors it degree for degree).</summary>
    static readonly int[][] NextByDegree = { new[] { 3, 5, 4, 1 }, new[] { 4, 3 }, new[] { 5, 3 }, new[] { 4, 0, 1 }, new[] { 0, 5 }, new[] { 3, 1, 4 }, new[] { 0 } };
    /// <summary>After the ranked follow-ups (or for an off-key chord): I, IV, V, vi, ii, iii, vii.</summary>
    static readonly int[] FillOrder = { 0, 3, 4, 5, 1, 2, 6 };

    public static int PitchClass(int midi) => ((midi % 12) + 12) % 12;
    /// <summary>A root voiced into 55..66, as RandomSong voices its chords.</summary>
    public static int VoiceRoot(int pitchClass) => 55 + PitchClass(pitchClass - 55);

    public static SongKey KeyOf(int rootMidi, IList<int> semis) => new SongKey(PitchClass(rootMidi), semis != null && IsMinor(semis));
    /// <summary>The song's key: SPEC v5 §2.1 the key the song keeps (SongManager.SongKey: set when the song starts or loads, so moving islands never
    /// changes it); without one, v3's detection (tonic = the first island's root, minor when its stored chord is minor; C major without a song).</summary>
    public static SongKey KeyOfSong()
    {
        var sm = SongManager.I;
        if (sm != null && sm.HasSongKey) return sm.SongKey;
        if (sm == null || sm.Islands.Count == 0 || sm.Islands[0] == null) return new SongKey(0, false);
        var kb = sm.Islands[0];
        IList<int> semis = kb.storedSemis != null && kb.storedSemis.Length > 0 ? (IList<int>)kb.storedSemis : kb.semitoneList;
        return KeyOf(kb.chordRootMIDI, semis);
    }

    /// <summary>A measure for the chord <paramref name="rootMidi"/> + <paramref name="q"/> (unplaced, no group).</summary>
    public static SongManager.MeasureData Chord(int rootMidi, ChordQuality q, int bars = 1)
    {
        var semis = Semitones(q);
        bars = Mathf.Clamp(bars, 1, 4);
        return new SongManager.MeasureData { chordKey = ChordName(rootMidi, semis), chordRootMIDI = rootMidi, semitones = semis, measureDuration = 4f * bars, bars = bars, energy = 2, repeat = 1 };
    }

    /// <summary>The seventh chord on scale degree <paramref name="degree"/> (0..6) of the key.</summary>
    public static SongManager.MeasureData Diatonic(SongKey k, int degree, int bars = 1)
    {
        degree = ((degree % 7) + 7) % 7;
        int step = (k.minor ? MinorSteps : MajorSteps)[degree];
        return Chord(VoiceRoot(k.tonic + step), (k.minor ? MinorSevenths : MajorSevenths)[degree], bars);
    }

    /// <summary>The 7 diatonic sevenths of the key in degree order (I .. vii).</summary>
    public static SongManager.MeasureData[] DiatonicSet(SongKey k, int bars = 1)
    {
        var r = new SongManager.MeasureData[7];
        for (int d = 0; d < 7; d++) r[d] = Diatonic(k, d, bars);
        return r;
    }

    /// <summary>Scale degree (0..6) of <paramref name="rootMidi"/> in the key, -1 when the root is off the scale.</summary>
    public static int DegreeOf(SongKey k, int rootMidi)
    {
        int rel = PitchClass(rootMidi - k.tonic);
        var steps = k.minor ? MinorSteps : MajorSteps;
        for (int d = 0; d < 7; d++) if (steps[d] == rel) return d;
        return -1;
    }

    /// <summary>Ranked follow-ups after the chord rooted on <paramref name="lastRootMidi"/> (the SPEC v3 §3.1 table, then I, IV, V, vi, ii, iii, vii;
    /// never the same degree again; an off-key chord gets I, IV, V, vi first). At most 7.</summary>
    public static SongManager.MeasureData[] SuggestNext(SongKey k, int lastRootMidi, int count, int bars = 1)
    {
        int d = DegreeOf(k, lastRootMidi);
        var order = new List<int>(7);
        if (d >= 0) foreach (int x in NextByDegree[d]) if (!order.Contains(x)) order.Add(x);
        foreach (int x in FillOrder) if (x != d && !order.Contains(x)) order.Add(x);
        count = Mathf.Clamp(count, 0, order.Count);
        var r = new SongManager.MeasureData[count];
        for (int i = 0; i < count; i++) r[i] = Diatonic(k, order[i], bars);
        return r;
    }

    /// <summary>Three borrowed colours: major bVII7, iv m7, bVI maj7; minor V7, IV7, bII maj7.</summary>
    public static SongManager.MeasureData[] Borrowed(SongKey k, int bars = 1)
    {
        if (k.minor) return new[] { Chord(VoiceRoot(k.tonic + 7), ChordQuality.Dominant7, bars), Chord(VoiceRoot(k.tonic + 5), ChordQuality.Dominant7, bars), Chord(VoiceRoot(k.tonic + 1), ChordQuality.Major7, bars) };
        return new[] { Chord(VoiceRoot(k.tonic + 10), ChordQuality.Dominant7, bars), Chord(VoiceRoot(k.tonic + 5), ChordQuality.Minor7, bars), Chord(VoiceRoot(k.tonic + 8), ChordQuality.Major7, bars) };
    }

    /// <summary>Same root pitch class and the same quality.</summary>
    public static bool SameChord(SongManager.MeasureData a, SongManager.MeasureData b)
    {
        if (a == null || b == null) return false;
        return PitchClass(a.chordRootMIDI) == PitchClass(b.chordRootMIDI) && QualityOf(a.semitones ?? new[] { 0, 4, 7 }) == QualityOf(b.semitones ?? new[] { 0, 4, 7 });
    }
}
