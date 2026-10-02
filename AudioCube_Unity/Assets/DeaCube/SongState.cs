using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// One island (chord measure) or Moon (drum island) in the song. v2 adds a world position, the
/// island kind and the section controls (mood, energy, fill, sleep, repeat). Missing fields in an
/// old file keep their initialiser defaults, so v1 saves load unchanged.
/// </summary>
[Serializable]
public class MeasureState
{
    public string chordKey;
    public int root;
    public int[] semis;
    public int bars = 1;
    public int barOffset;         // v8: a chord grid's first measure inside its column (0 = the column start; a shorter grid inside a longer column)
    // v2 world
    public float px, pz;          // island root position on the sea (transform.position.x/z)
    public bool placed;           // false => legacy row layout on build
    public int kind;              // 0 chord island, 1 Moon (drum island), 2 keyboard (v6: one row of piano keys for melody)
    // v2 sections
    public int mood;              // 0 as generated, 1 sun, 2 cloud, 3 storm (per-island override of the climate)
    public int energy = 2;        // 0..3
    public bool fill;             // ratchets + crash in the last bar
    public bool sleep;            // silent island (breath)
    public int repeat = 1;        // 1..4
    // v3
    public int group;             // merge group id (0 none): members are consecutive in song order and touch edge to edge (SPEC v3 §3.3)
    public int fall;              // presentation fall pattern: 0 auto, 1 wave, 2 rows, 3 spiral, 4 rain, 5 bloom (SPEC v3 §4.3)
    // v4 (SPEC v4 §2.1)
    public int col = -1;          // column index (all islands of a column play together); -1 = an older file: one column per island in order
    public int reg;               // register shift of the whole island in octaves (-2..+2)
    // v6 (SPEC v6 §2.4)
    public int carry;             // the island's patterns also play on the next `carry` chords of the progression (0..3; SongManager.CarryTargets)
    // v7 (SPEC v7 §2.1)
    public int stairType;         // stairs (kind 3): 0 chord, 1 scale, 2 spark, 3 slide, 4 bright, 5 walk (Harmony.Stairs)
    public int stairDir = -1;     // stairs: -1 falls, +1 climbs
    public int stairSteps = 4;    // stairs: 3..8 steps
    public int stairRate = 12;    // stairs: ticks per step (6, 8, 12, 24)
    public bool stairLead = true; // stairs: the run ends at its column's end (a lead-in); false: it starts with the column
    public int carryStyle;        // 0 hop (v6 carry), 1 flow (the pattern flows through the next chords by pedal, SPEC v7 §2.7)
    public bool rewind;           // repeat style: time unwinds at every pass boundary (no belt)
    public int vary;              // 0 same, 1 vary, 2 answer: the passes after the first play small differences
    public bool launch;           // the cubes are flung to the next grid at the end of its turn
    public bool lead;             // v9 (L): the stage lights spotlight this grid while it sounds (keyboards, stairs and phrases are lead anyway)
    public int phraseOffset;      // phrase (kind 4): ticks from its column's start
    public int phraseBeats = 4;   // phrase: its length in beats (2 = ½ measure .. 32)
    public int phraseGrid = 12;   // phrase: ticks per time cell (12 eighths, 6 sixteenths)
    public int keyCount;          // v7 §20.1: a keyboard's key count (13..61; 0 = an older file: 25) from its lowest key (root)
    public int secRole;           // v7 §17.1: the name of the section this island's column starts (0 none, 1 intro, 2 verse, 3 chorus, 4 bridge, 5 drop, 6 outro)
    public int[] kit;             // v9 (G): a Moon's own drum kit (VoiceRules.DrumPiece: the GM key of each row, then each row's column-5 alternate); null / empty = the standard rows

    public static MeasureState From(SongManager.MeasureData d) => new MeasureState
    {
        chordKey = d.chordKey, root = d.chordRootMIDI, semis = (int[])(d.semitones ?? new[] { 0, 4, 7 }).Clone(), bars = d.bars <= 0 ? 1 : d.bars, barOffset = Mathf.Max(0, d.barOffset),
        px = d.px, pz = d.pz, placed = d.placed, kind = d.kind, mood = d.mood, energy = Mathf.Clamp(d.energy, 0, 3), fill = d.fill, sleep = d.sleep, repeat = Mathf.Clamp(d.repeat, 1, 4),
        group = Mathf.Max(0, d.group), fall = Mathf.Clamp(d.fall, 0, 5), col = d.col, reg = Mathf.Clamp(d.reg, -2, 2),
        carry = Mathf.Clamp(d.carry, 0, ProjectConfig.MaxCarry),
        stairType = Mathf.Clamp(d.stairType, 0, 5), stairDir = d.stairDir > 0 ? 1 : -1, stairSteps = Mathf.Clamp(d.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps),
        stairRate = StairRate(d.stairRate), stairLead = d.stairLead, carryStyle = Mathf.Clamp(d.carryStyle, 0, 1), rewind = d.rewind, vary = Mathf.Clamp(d.vary, 0, 2), launch = d.launch, lead = d.lead,
        phraseOffset = Mathf.Max(0, d.phraseOffset), phraseBeats = Mathf.Clamp(d.phraseBeats <= 0 ? 4 : d.phraseBeats, 1, ProjectConfig.PhraseMaxBeats), phraseGrid = d.phraseGrid == 6 ? 6 : 12,
        secRole = Mathf.Clamp(d.secRole, 0, 6), keyCount = d.keyCount, kit = KitCopy(d.kit)
    };
    public static MeasureState From(KeyBlock kb) => new MeasureState
    {
        chordKey = kb.assignedChord, root = kb.chordRootMIDI,
        semis = kb.storedSemis != null && kb.storedSemis.Length > 0 ? (int[])kb.storedSemis.Clone() : kb.semitoneList.ToArray(),
        bars = kb.bars, barOffset = kb.barOffset, px = kb.px, pz = kb.pz, placed = kb.placed, kind = kb.kind,
        mood = kb.mood, energy = kb.energy, fill = kb.fill, sleep = kb.sleep, repeat = kb.repeat, group = kb.group, fall = kb.fall, col = kb.column, reg = kb.register,
        carry = kb.carry,
        stairType = kb.stairType, stairDir = kb.stairDir, stairSteps = kb.stairSteps, stairRate = kb.stairRate, stairLead = kb.stairLead,
        carryStyle = kb.carryStyle, rewind = kb.rewind, vary = kb.vary, launch = kb.launch, lead = kb.lead,
        phraseOffset = kb.phraseOffset, phraseBeats = kb.phraseBeats, phraseGrid = kb.phraseGrid, secRole = kb.secRole, keyCount = kb.keyCount, kit = KitCopy(kb.kit)
    };
    public static MeasureState Clone(MeasureState m) => new MeasureState
    {
        chordKey = m.chordKey, root = m.root, semis = (int[])m.semis.Clone(), bars = m.bars, barOffset = m.barOffset,
        px = m.px, pz = m.pz, placed = m.placed, kind = m.kind, mood = m.mood, energy = m.energy, fill = m.fill, sleep = m.sleep, repeat = m.repeat, group = m.group, fall = m.fall, col = m.col, reg = m.reg,
        carry = m.carry,
        stairType = m.stairType, stairDir = m.stairDir, stairSteps = m.stairSteps, stairRate = m.stairRate, stairLead = m.stairLead,
        carryStyle = m.carryStyle, rewind = m.rewind, vary = m.vary, launch = m.launch, lead = m.lead,
        phraseOffset = m.phraseOffset, phraseBeats = m.phraseBeats, phraseGrid = m.phraseGrid, secRole = m.secRole, keyCount = m.keyCount, kit = KitCopy(m.kit)
    };
    public SongManager.MeasureData ToData(int index) => new SongManager.MeasureData
    {
        index = index + 1, chordKey = chordKey, chordRootMIDI = root, semitones = (int[])semis.Clone(), measureDuration = bars * GlobalClock.BeatsPerBar, bars = bars, barOffset = barOffset,
        px = px, pz = pz, placed = placed, kind = kind, mood = mood, energy = energy, fill = fill, sleep = sleep, repeat = repeat, group = group, fall = fall, col = col, reg = reg,
        carry = carry,
        stairType = stairType, stairDir = stairDir, stairSteps = stairSteps, stairRate = stairRate, stairLead = stairLead,
        carryStyle = carryStyle, rewind = rewind, vary = vary, launch = launch, lead = lead,
        phraseOffset = phraseOffset, phraseBeats = phraseBeats, phraseGrid = phraseGrid, secRole = secRole, keyCount = keyCount, kit = KitCopy(kit)
    };

    /// <summary>v9 (G): a copy of a Moon kit (null for none / an empty one: JsonUtility reads a missing array as empty).</summary>
    static int[] KitCopy(int[] k) => k != null && k.Length > 0 ? (int[])k.Clone() : null;

    /// <summary>v7: a stairs rate in ticks per step, one of 6 (sixteenths), 8 (triplet eighths), 12 (eighths), 24 (quarters); anything else → 12.</summary>
    public static int StairRate(int ticks) => ticks == 6 || ticks == 8 || ticks == 12 || ticks == 24 ? ticks : 12;
}

/// <summary>
/// One block. v2 adds the necklace (hits/rot/mask), per-node stickers (mods), octave, twin/shadow/echo
/// modifiers, the Rider flag, a deterministic seed and a stable id. <c>rests</c> is kept in sync with
/// <c>mods</c> (rest = 1) so older builds still read the file.
/// </summary>
[Serializable]
public class CubeState
{
    public int instrument;
    public int measure;
    public int[] xs;
    public int[] zs;
    public bool[] rests;
    public int step = 1;
    public int gate = 1;
    public int mode = 0;
    public float volume = 1f;
    public bool muted;
    // v2
    public int moon = -1;         // >= 0 => the cube lives on SongState.moons[moon] and measure is ignored
    public int[] mods;            // per node: 0 none 1 rest 2 ghost 3 accent 4 ratchet2 5 ratchet3 6 coin 7 tie 8 lift; null => from rests
    public int hits = -1;         // -1 = every step, -2 = explicit mask, 1..n euclidean hits per bar
    public int rot;               // necklace rotation
    public int mask;              // explicit per-bar hit bits (bit j = step j), used when hits == -2
    public int octave;            // -1..+1
    public bool reverse;          // walk the path backwards
    public int phase;             // hit-index offset for twins
    public int follow;            // 0 off, 1 above, 2 below, 3 both
    public int echo;              // 0..2 repeats
    public bool shimmer;          // echo climbs a shadow row per repeat
    public bool rider;            // pitched only; replays on every Route island
    public int seed;              // coin stickers and dice walks; 0 => derived from (measure, xs[0], zs[0]) on load
    public int twinOf = -1;       // id of the source cube (visual braid), -1 none
    public int id;                // stable per-cube id (assigned when the cube is finalised)
    // v4 (SPEC v4 §2.1)
    public int[] durs;            // per node note length in ticks (24 = one beat); null = the legacy uniform-step engine (step + necklace)
    // v6 (SPEC v6 §2.7)
    public int voice;             // the sound inside the instrument group (0 = the group's first sound: every pre-v6 cube)
    // v7 (SPEC v7 §2.1)
    public int layer;             // octave layer -2..+2: sounds 12 x layer higher and rides above (sky) / below (deep) its grid
    public int[] bend;            // per node semitones added to the tile's pitch (a harmony cube); null = none
    public int echoOf = -1;       // the source cube's id for an octave copy / echo / harmony cube (-1 none)
    public int climb;             // CLIMB (DeaCube/Climb.cs): 0 off, 1..3 high points climb to the next chord tone
    public bool sphere;           // a SPHERE piece (DeaCube/SphereBody.cs): leaps to any tile, bounces and rolls; false = a cube
    // v9 (N, DeaCube/Nudge.cs)
    public int[] nudges;          // per node scale steps of the SONG KEY (−2..+2) the step plays above / below its tile ("higher" / "lower"); null / short = none

    public static CubeState Clone(CubeState c) => new CubeState
    {
        instrument = c.instrument, measure = c.measure, xs = (int[])c.xs.Clone(), zs = (int[])c.zs.Clone(), rests = (bool[])c.rests.Clone(),
        step = c.step, gate = c.gate, mode = c.mode, volume = c.volume, muted = c.muted,
        moon = c.moon, mods = c.mods == null ? null : (int[])c.mods.Clone(), hits = c.hits, rot = c.rot, mask = c.mask, octave = c.octave,
        reverse = c.reverse, phase = c.phase, follow = c.follow, echo = c.echo, shimmer = c.shimmer, rider = c.rider, seed = c.seed, twinOf = c.twinOf, id = c.id,
        durs = c.durs == null ? null : (int[])c.durs.Clone(), voice = c.voice,
        layer = c.layer, bend = c.bend == null ? null : (int[])c.bend.Clone(), echoOf = c.echoOf, climb = c.climb, sphere = c.sphere,
        nudges = c.nudges == null ? null : (int[])c.nudges.Clone()
    };

    /// <summary>Sticker list for this cube: saved mods, or derived from the legacy rests.</summary>
    public int[] ModsOrDerived()
    {
        int n = xs != null ? xs.Length : 0;
        var m = new int[n];
        bool useMods = mods != null && mods.Length == n;
        for (int i = 0; i < n; i++) m[i] = useMods ? mods[i] : (rests != null && i < rests.Length && rests[i] ? 1 : 0);
        return m;
    }

    /// <summary>Deterministic seed for a cube that was saved without one.</summary>
    public int SeedOrDerived()
    {
        if (seed != 0) return seed;
        int x0 = xs != null && xs.Length > 0 ? xs[0] : 0, z0 = zs != null && zs.Length > 0 ? zs[0] : 0;
        unchecked
        {
            int h = 17; h = h * 31 + measure; h = h * 31 + x0; h = h * 31 + z0; h = h * 31 + (xs != null ? xs.Length : 0);
            return h == 0 ? 1 : h;
        }
    }
}

/// <summary>Complete serializable snapshot of the song: used for undo/redo, save/load and structural edits.</summary>
[Serializable]
public class SongState
{
    public const int CurrentVersion = 6;   // v4 (SPEC v4 §2.1): columns (col, reg) and per-note lengths (durs); v5 (SPEC v5 §2.1): the song key;
                                           // v6 (SPEC v6 §2.4-§2.7): carry, keyboard islands (kind 2), per-cube voices

    public string name;
    public float bpm = 100f;
    public int beatsPerBar = 4;
    public float swing;
    public bool loop = true;
    public int transpose;
    public MeasureState[] measures;
    public CubeState[] cubes;
    public float[] instVolume;
    public bool[] instMuted;
    // v2
    public int version;           // 0 = legacy (v1), 2 = this schema
    public MeasureState[] moons;  // 0..2 drum Moons (kind = 1)
    public int climate;           // 0 as generated, 1 sun, 2 cloud, 3 storm
    public float tone = 1f;       // master cutoff 0.15..1
    public float space = 1f;      // reverb/chorus send scale 0..1.6
    public int demoSeed;          // seed of the generated song (Moon groove dice, dice walks)
    // v3
    public MeasureState[] deck;   // the island tray's deck: the generated progression not placed yet (SPEC v3 §3.1), saved and undoable
    // v5 (SPEC v5 §2.1)
    public int[] sections;        // v7 (SPEC v7 §13): the first column of every section, ascending (null = older file: a section per 4 measures)
    public string[] sectionLetters; // v9 (G): an own letter per section ("A", "A'", "B" …; null / empty or a count other than the sections' = the content rule)
    public int keyTonic = -1;     // the song key's tonic pitch class 0..11 (-1 = unknown: derived from the first island on load, then kept)
    public bool keyMinor;

    public bool IsLegacy => version < 2;   // a v1 file (no world positions, no necklace fields)

    public static SongState Capture()
    {
        var sm = SongManager.I;
        var st = new SongState
        {
            version = CurrentVersion,
            name = sm != null && sm.currentSongData != null ? sm.currentSongData.songName : "",
            bpm = Performance.NominalBpm,   // the un-halved tempo while the half-time punch-in is held
            beatsPerBar = GlobalClock.BeatsPerBar,
            swing = GlobalClock.Swing,
            loop = GlobalClock.LoopSong,
            transpose = SongManager.Transpose,
            measures = sm != null ? sm.Islands.Where(k => k != null).Select(MeasureState.From).ToArray() : new MeasureState[0],
            moons = sm != null ? sm.Moons.Where(k => k != null).Select(MeasureState.From).ToArray() : new MeasureState[0],
            cubes = SequenceMaster.Cubes.Where(c => c != null && c.isFinalized && c.nodes.Count > 0).Select(c => c.ToState()).ToArray(),
            instVolume = (float[])Instruments.Volume.Clone(),
            instMuted = (bool[])Instruments.Muted.Clone(),
            climate = SongManager.Climate,
            tone = SongManager.Tone,
            space = SongManager.Space,
            demoSeed = sm != null ? sm.demoSeed : 0,
            deck = IslandTray.DeckStates(),
            keyTonic = sm != null && sm.HasSongKey ? sm.SongKey.tonic : -1,
            keyMinor = sm != null && sm.HasSongKey && sm.SongKey.minor,
            sections = sm != null && sm.ColumnCount > 0 ? sm.SectionStarts().ToArray() : null,   // v7 (SPEC v7 §13): K restores them on a rebuild
            sectionLetters = sm != null && sm.ColumnCount > 0 ? sm.ExplicitLetters() : null   // v9 (G): the song's own section letters (null = the content rule)
        };
        return st;
    }

    public static void Apply(SongState st)
    {
        if (st == null || st.measures == null || st.measures.Length == 0 || SongManager.I == null) return;
        SongManager.I.RebuildFromState(st);
    }

    public SongManager.SongData ToSongData()
    {
        var d = new SongManager.SongData { songName = name, bpm = Mathf.RoundToInt(bpm), timeSignature = beatsPerBar + "/4", measures = new SongManager.MeasureData[measures.Length] };
        for (int i = 0; i < measures.Length; i++) d.measures[i] = measures[i].ToData(i);
        return d;
    }

    public string ToJson() => JsonUtility.ToJson(this);
    public static SongState FromJson(string json) => JsonUtility.FromJson<SongState>(json);
}

/// <summary>Snapshot-based undo/redo.</summary>
public static class History
{
    static readonly List<string> undo = new List<string>();
    static readonly List<string> redo = new List<string>();
    public static event Action OnChanged;
    public static bool CanUndo => undo.Count > 1;
    public static bool CanRedo => redo.Count > 0;
    /// <summary>Snapshots on the undo stack (the current state included) and on the redo stack: tests count gestures with them.</summary>
    public static int UndoCount => undo.Count;
    public static int RedoCount => redo.Count;
    static bool applying;

    public static void Reset() { undo.Clear(); redo.Clear(); OnChanged?.Invoke(); }

    public static void Push()
    {
        if (applying || SongManager.I == null || !SongManager.I.HasSong) return;
        string j = SongState.Capture().ToJson();
        if (undo.Count > 0 && undo[undo.Count - 1] == j) return;
        undo.Add(j);
        if (undo.Count > 150) undo.RemoveAt(0);
        redo.Clear();
        OnChanged?.Invoke();
    }

    /// <summary>Folds the newest snapshot into the gesture before it, so one undo takes both back (e.g. a grid added and pasted on in one click).</summary>
    public static void FoldLast()
    {
        if (undo.Count < 3) return;
        undo.RemoveAt(undo.Count - 2);
        OnChanged?.Invoke();
    }

    public static void Undo()
    {
        if (!CanUndo) return;
        redo.Add(undo[undo.Count - 1]);
        undo.RemoveAt(undo.Count - 1);
        Restore(undo[undo.Count - 1]);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.35f, 0.8f);
    }

    public static void Redo()
    {
        if (!CanRedo) return;
        string j = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(j);
        Restore(j);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.35f, 1.2f);
    }

    static void Restore(string json)
    {
        applying = true;
        try { SongState.Apply(SongState.FromJson(json)); }
        finally { applying = false; }
        OnChanged?.Invoke();
    }
}

/// <summary>
/// Save slots: the user's main save (<see cref="Path"/>), the autosave (menu / quit) and three rotating backups (F15: a song with cubes
/// is copied there before New / Learn / the HUD sparkle replace it; the oldest is overwritten, never the main save). S10: every write
/// goes to a temp file first and then replaces the slot, so a crash mid-write never leaves a torn file.
/// "My songs" (SongLibrary): the player's songs live in persistentDataPath/songs/ (one file per song + library.json); <see cref="Save"/> /
/// <see cref="Load"/> go there. <see cref="Path"/> is the old single save, kept as it was (the library imports it once as "my song").
/// </summary>
public static class SongIO
{
    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "deacube_song.json");
    /// <summary>v3: the slot the menu writes when leaving the world (never the user's main save).</summary>
    public static string AutosavePath => System.IO.Path.Combine(Application.persistentDataPath, "deacube_autosave.json");
    /// <summary>F15: rotating backup slots 1..BackupSlots.</summary>
    public const int BackupSlots = 3;
    public static string BackupPath(int slot) => System.IO.Path.Combine(Application.persistentDataPath, "deacube_backup_" + slot + ".json");
    /// <summary>Backups written this session (tests).</summary>
    public static int BackupsWritten { get; private set; }
    public static bool Exists => File.Exists(Path);
    public static bool AnyExists => File.Exists(Path) || File.Exists(AutosavePath);

    /// <summary>The newer of the main save and the autosave (null when neither exists): what the menu's Continue opens.</summary>
    public static string NewestExisting
    {
        get
        {
            bool a = File.Exists(Path), b = File.Exists(AutosavePath);
            if (!a && !b) return null;
            if (a && !b) return Path;
            if (b && !a) return AutosavePath;
            return File.GetLastWriteTimeUtc(AutosavePath) > File.GetLastWriteTimeUtc(Path) ? AutosavePath : Path;
        }
    }

    /// <summary>SPEC v4: leaving Play mode autosaves (MainMenu.OnApplicationQuit); test harnesses switch this off so a test song never lands in the autosave.
    /// "My songs": it also guards the player's library (SongLibrary) — while it is off nothing is written there (leaving for the menu and quitting
    /// save the world's song into its library file only while it is on) and the library is not used at all (tests see the single-save behaviour).</summary>
    public static bool QuitAutosave = true;
    /// <summary>⌘S / the strip's "save": the world's song into its library song ("my songs": a new entry "song N" when it is not one yet); with
    /// the library off (test harnesses), the old single save.</summary>
    public static bool Save() => SongLibrary.Enabled ? SongLibrary.SaveCurrent() : SaveTo(Path);
    /// <summary>The current library song (else the old single save); with the library off, the old single save.</summary>
    public static bool Load() => SongLibrary.Enabled ? SongLibrary.LoadCurrent() : LoadFrom(Path);

    public static bool SaveTo(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || SongManager.I == null || !SongManager.I.HasSong) return false;
            WriteAtomic(path, SongState.Capture().ToJson());
            return true;
        }
        catch (Exception e) { Debug.LogWarning("Save failed: " + e.Message); return false; }
    }

    /// <summary>S10: writes <paramref name="text"/> to a temp file beside <paramref name="path"/>, then swaps it in (File.Replace, or a
    /// move when the slot is new): the slot holds either the old song or the new one, never half of one.</summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (!File.Exists(path)) { File.Move(tmp, path); return; }
        try { File.Replace(tmp, path, null); }
        catch (Exception)
        {
            // a file system without Replace: the old slot goes only once the new text is complete on disk
            File.Delete(path);
            File.Move(tmp, path);
        }
    }

    /// <summary>F15: copies the current song to the oldest (or first free) backup slot when it has cubes of the player's own (islands'
    /// cubes: a fresh song's demo Moon groove alone is not kept). Never touches the main save. Returns the slot written, else null.</summary>
    public static string Backup()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return null;
        bool cubes = false;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0 && !c.IsOnMoon) { cubes = true; break; }
        if (!cubes) return null;
        string slot = null; DateTime oldest = DateTime.MaxValue;
        for (int i = 1; i <= BackupSlots; i++)
        {
            string p = BackupPath(i);
            if (!File.Exists(p)) { slot = p; break; }
            var t = File.GetLastWriteTimeUtc(p);
            if (t < oldest) { oldest = t; slot = p; }
        }
        if (slot == null || slot == Path || slot == AutosavePath) return null;
        if (!SaveTo(slot)) return null;
        BackupsWritten++;
        return slot;
    }

    public static bool LoadFrom(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            var st = SongState.FromJson(File.ReadAllText(path));
            if (st == null || st.measures == null || st.measures.Length == 0) return false;
            SongState.Apply(st);
            History.Reset();
            History.Push();
            return true;
        }
        catch (Exception e) { Debug.LogWarning("Load failed: " + e.Message); return false; }
    }
}
