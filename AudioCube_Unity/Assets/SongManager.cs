using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the song: builds one island per measure (anywhere on the sea: positions live in the model, a fresh song lays
/// them on an arc, legacy saves keep the row), the Moons (drum islands, a separate list), the Route cable and the Comet,
/// keeps the timeline (measure start beats + every cube's windows) and applies structural edits through SongState.
/// v3 (SPEC v3 §3): a fresh song starts with island 0 + the demo Moon and hands the rest of the progression to the island
/// tray (the deck); islands are placed from the tray (PlaceIsland / PlaceIslandMerged / PlaceAll), merged into groups
/// (MergeIslands: consecutive in song order, laid edge to edge, NormalizeGroups after every structural edit) and split again.
/// v4 (SPEC v4 §3, package K): islands stand in COLUMNS. The song plays column by column, left to right; every island of a column
/// sounds at the same time (one start, one length). Islands order = column-major (a column's anchor first, then the others in
/// creation order); a column's islands share x (ColumnGap between columns, 0 between glued ones), z is free per island and resolved
/// so no two islands of a column come closer than LaneGap. NormalizeColumns runs on load, undo/redo and every structural edit
/// (legacy files become a clean row of one-island columns). The island "component" ops (add above/below, duplicate, register,
/// in-column move, column reorder, tray slots) live here; OnColumnsChanged fires after every layout change.
/// v5 (SPEC v5 §2.5 / §2.6, package K): repeat = PASSES. A column plays ColumnPasses (the max repeat of its islands) passes of PassLength
/// beats; island i plays passes 0 .. repeat − 1 (one cube window per pass) and rests in the others. An island with repeat ≥ 2 stands at the
/// head of a conveyor belt laid toward +x (DeaCube/Belt.cs): the column's width in the layout includes its longest belt, and while playing
/// the belt jerks the island one slot forward per pass (RideSlot: a pure function of the song beat). Islands drag anywhere: into another
/// column or a new one (MoveIslandToColumn); the song keeps its key through every op.
/// v6 (SPEC v6 §3, package K): KEYBOARD islands (kind 2: a piano of 25 keys for melody; AddKeyboardIsland) stand in columns like any island and
/// survive every op (layout, drag, duplicate, save / load); they never merge with chord islands and their chord is their column's
/// (ChordOfColumn, looking back when a column holds only keyboards). CARRY: an island's patterns also play on the next `carry` chords — its
/// resident cubes get, after their own windows, one window per pass of each carry target's column on the target (CarryTargets; carried, hop,
/// silent when either sleeps). The octave TOWER: TowerLiftAt, a pure function of the song beat (a raised island floats a hint higher at rest,
/// rises on its turn, holds with a bob, sinks slowly after its last pass; a lowered one dips; loops wrap), which KeyBlock shows (a jump glides).
/// v6 §11 (the user: "the percussion circle should stay in line with the current part of the song"): every Moon has a START COLUMN and plays only
/// its SECTION (from there to the next later Moon start or the song's end; Moons starting together layer; before the first start = no drums); it
/// rests in line with its start column in the drum lane in front of the islands (LayoutMoons) and, while its section plays, follows the lit
/// column (MoonFollowX, a pure function of the beat), gliding home after it. Old songs (every Moon at column 0) keep the v5 timeline exactly.
/// </summary>
public class SongManager : MonoBehaviour
{
    public static SongManager I;
    public static int Transpose = 0;
    // v2 song-wide controls (persisted in SongState)
    public static int Climate = 0;          // 0 as generated, 1 sun, 2 cloud, 3 storm
    public static float Tone = 1f;          // master low-pass 0.15..1
    public static float Space = 1f;         // reverb/chorus send scale 0..1.6
    public int demoSeed;
    public static event Action OnSongControlsChanged;
    /// <summary>Climate (§3.5): every island without a mood override is viewed through this family; rebuilds through Capture/RebuildFromState and pushes History.</summary>
    public static void SetClimate(int c)
    {
        c = Mathf.Clamp(c, 0, 3); if (c == Climate) return;
        Climate = c;
        if (I != null && I.HasSong) { var st = SongState.Capture(); st.climate = c; I.RebuildFromState(st); History.Push(); }
        Fx.SetClimateTint(c);
        OnSongControlsChanged?.Invoke();
    }
    public static void SetTone(float t) { t = Mathf.Clamp(t, 0.15f, 1f); Tone = t; Synth.SetMasterCutoff01(t); OnSongControlsChanged?.Invoke(); }
    public static void SetSpace(float s) { s = Mathf.Clamp(s, 0f, 1.6f); Space = s; Instruments.PushToSynth(); OnSongControlsChanged?.Invoke(); }
    public static event Action OnTransposeChanged;
    /// <summary>Global transpose: every cube releases its notes and re-schedules under the new pitch (AudioCube subscribes); a queued preview is dropped.</summary>
    public static void SetTranspose(int semis) { semis = Mathf.Clamp(semis, -12, 12); if (semis == Transpose) return; Transpose = semis; Synth.CancelOwner(VoiceRules.OwnerPreview); OnTransposeChanged?.Invoke(); }

    [Serializable]
    public class MeasureData
    {
        public int index;
        public string chordKey;
        public int chordRootMIDI;
        public int[] semitones;
        public float measureDuration;
        public int bars;
        public int barOffset;           // v8: a chord grid's first measure inside its column (0 = the column start)
        // v2 (carried through SongState; defaults match a fresh island)
        public float px, pz;
        public bool placed;
        public int kind;
        public int mood;
        public int energy = 2;
        public bool fill, sleep;
        public int repeat = 1;
        public int group;               // v3 merge group (0 none), SPEC v3 §3.3
        public int fall;                // v3 presentation fall pattern 0..5, SPEC v3 §4.3
        public int col = -1;            // v4 column (SPEC v4 §2.1), -1 = one column per island in order
        public int reg;                 // v4 register shift in octaves (-2..+2)
        public int carry;               // v6 carry: the island's patterns also play on the next n chords (0..3), SPEC v6 §2.4
        // v7 (SPEC v7 §2.1): stairs (kind 3), carry style, repeat style, vary, launch
        public int stairType;
        public int stairDir = -1;
        public int stairSteps = 4;
        public int stairRate = 12;
        public bool stairLead = true;
        public int carryStyle;
        public bool rewind;
        public int vary;
        public bool launch;
        public bool lead;               // v9 (L): the stage lights spotlight this grid while it sounds (IsLead)
        public int phraseOffset;
        public int phraseBeats = 4;
        public int phraseGrid = 12;
        public int secRole;             // v7 §17.1: the section's name when this island leads a section's first column (0 none, 1 intro, 2 verse, 3 chorus, 4 bridge, 5 drop, 6 outro)
        public int keyCount;            // v7 §20.1: a keyboard's keys from its lowest key (13..61; 0 = an older file: 25)
        public int[] kit;               // v9 (G): a Moon's own drum kit — the GM drum key of each row, then of each row's column-5 alternate (VoiceRules.DrumPiece); null / empty = the standard rows
    }

    [Serializable]
    public class SongData
    {
        public string songName;
        public int bpm;
        public string timeSignature;
        public MeasureData[] measures;
        public int demoSeed;            // seed of a generated song (MusicTheory.RandomSong); 0 = derive from the name
        public MeasureData[] moons;     // v2: Moons (kind 1); null from the generators => the demo Moon is added
    }

    [Header("Data")]
    public SongData currentSongData;

    [Header("References")]
    public GameObject keyBlockPrefab;   // legacy, unused
    public Transform mainCamera;        // legacy, unused

    [Header("Settings")]
    public float cameraPanSpeed = 5f;
    public int beatsPerMeasure = 4;
    public int beatValue = 4;

    public readonly List<KeyBlock> Islands = new List<KeyBlock>();
    public readonly List<KeyBlock> Moons = new List<KeyBlock>();   // drum islands; never part of the timeline
    /// <summary>v4: island i's column start (every island of a column shares it; one entry per island, Islands order).</summary>
    public readonly List<float> MeasureStarts = new List<float>();
    public float TotalBeats = 4f;
    /// <summary>Play order (island index per occurrence): the island order (v5: a repeat is passes INSIDE its column, not extra occurrences).</summary>
    public readonly List<int> PlayOrder = new List<int>();
    /// <summary>The island under the playhead: v4 the ANCHOR of the column under the playhead.</summary>
    public int LitIsland => ActiveMeasureIndex(GlobalClock.SongBeat);
    public event Action OnSongRebuilt;
    public bool HasSong => Islands.Count > 0;
    /// <summary>SPEC v5 §2.1: the key the song keeps (set when a song starts or loads; moving or editing islands never changes it; saved).</summary>
    public MusicTheory.SongKey SongKey { get; private set; }
    public bool HasSongKey { get; private set; }
    /// <summary>The cable through every column's anchor hub (one per song; rebuilt after every build).</summary>
    public Route Route { get; private set; }
    public const int MaxMoons = 12;   // v6 §11.4: a Moon per song part (was 2); v9 (G): 12 (the gallery's "get proto" has a Moon per 8-bar phrase: 11)
    /// <summary>v9 (G): the longest Moon groove in bars (a Moon's bars: its cubes' window; 1 = one bar, the v6 Moon).</summary>
    public const int MaxMoonBars = 8;

    Transform islandsRoot;
    readonly int[] moonRolls = new int[MaxMoons];
    int lastCrashPass = -1;

    void Awake() { I = this; }
    void Start() { IslandTray.Ensure(); }   // the tray listens for its key (I) from the start; the HUD exists by now (DeaCubeBoot)
    void OnEnable() { GlobalClock.OnMeterChanged += RecomputeMeasureStarts; GlobalClock.OnStop += ResetCrash; GlobalClock.OnSeek += ResetCrash; GlobalClock.OnPause += ResetCrash; GlobalClock.OnTempoChanged += ResetCrash; }
    void OnDisable() { GlobalClock.OnMeterChanged -= RecomputeMeasureStarts; GlobalClock.OnStop -= ResetCrash; GlobalClock.OnSeek -= ResetCrash; GlobalClock.OnPause -= ResetCrash; GlobalClock.OnTempoChanged -= ResetCrash; }
    /// <summary>Stop / pause / seek / tempo: the queued fill crash (owner 7) and any 16th-quantised preview (owner 9) are dropped along with the cubes' notes; the crash is re-armed for the pass.</summary>
    void ResetCrash() { lastCrashPass = -1; VoiceRules.ResetCrash(); Synth.CancelOwner(VoiceRules.OwnerSong); Synth.CancelOwner(VoiceRules.OwnerPreview); }

    // ------------------------------------------------------------------ building
    /// <summary>v2 song start (kept for tests and the full-layout demo): every measure in a row of one-island columns (v4), the demo Moon +
    /// groove, a Keys cube stamped to every island and a Bass Rider, playing on load.</summary>
    public void StartNewSong(SongData newData) { BeginSong(newData, null); }

    /// <summary>
    /// v3 song start (SPEC v3 §3.2): builds ONLY measure 0 centred on the origin plus the demo Moon with its default groove (no
    /// Keys/Bass demo cubes), stopped, the camera framing island 0 and the Moon, History reset; the rest of the progression becomes
    /// the island tray's deck (IslandTray.SetDeck(measures[1..])). The generators (online and offline) and the menu call this.
    /// </summary>
    public void StartFreshSong(SongData generated)
    {
        if (generated == null || generated.measures == null || generated.measures.Length == 0) return;
        var src = generated.measures;
        var deck = new MeasureData[src.Length - 1];
        for (int i = 1; i < src.Length; i++) { var d = CopyData(src[i]); d.placed = false; d.group = 0; d.kind = 0; d.col = -1; d.reg = 0; deck[i - 1] = d; }
        var first = CopyData(src[0]);
        if (first.bars <= 0) first.bars = 1;
        int rows = KeyBlock.RowsOf(first.semitones);
        first.px = -(ProjectConfig.numInversions - 1) * ProjectConfig.Spacing * 0.5f;
        first.pz = -(rows - 1) * ProjectConfig.Spacing * 0.5f;
        first.placed = true; first.group = 0; first.kind = 0; first.col = 0; first.reg = 0;
        var song = new SongData { songName = generated.songName, bpm = generated.bpm, timeSignature = generated.timeSignature, demoSeed = generated.demoSeed, measures = new[] { first }, moons = null };
        BeginSong(song, deck);
        Onboarding.Notify(Onboarding.Ev.SongStarted);
    }

    /// <summary>A copy of a measure (a tray card's data is never modified by a placement).</summary>
    public static MeasureData CopyMeasure(MeasureData m) { return CopyData(m); }

    static MeasureData CopyData(MeasureData m)
    {
        if (m == null) return new MeasureData { chordKey = "Cmaj7", chordRootMIDI = 60, semitones = new[] { 0, 4, 7, 11 }, bars = 1 };
        return new MeasureData
        {
            index = m.index, chordKey = m.chordKey, chordRootMIDI = m.chordRootMIDI, semitones = m.semitones != null ? (int[])m.semitones.Clone() : new[] { 0, 4, 7 },
            measureDuration = m.measureDuration, bars = m.bars <= 0 ? 1 : m.bars, px = m.px, pz = m.pz, placed = m.placed, kind = m.kind, mood = m.mood,
            energy = m.energy, fill = m.fill, sleep = m.sleep, repeat = m.repeat <= 0 ? 1 : m.repeat, group = m.group, fall = m.fall, col = m.col, reg = m.reg,
            carry = m.carry,
            stairType = m.stairType, stairDir = m.stairDir, stairSteps = m.stairSteps, stairRate = m.stairRate, stairLead = m.stairLead,
            carryStyle = m.carryStyle, rewind = m.rewind, vary = m.vary, launch = m.launch, lead = m.lead,
            phraseOffset = m.phraseOffset, phraseBeats = m.phraseBeats, phraseGrid = m.phraseGrid, secRole = m.secRole, keyCount = m.keyCount,
            kit = m.kit != null && m.kit.Length > 0 ? (int[])m.kit.Clone() : null
        };
    }

    /// <summary>Shared song start: <paramref name="freshDeck"/> == null ⇒ v2 (a row of columns, demo cubes on every island, playing);
    /// otherwise v3 (the one-island song given, the Moon beside it, stopped, the deck handed to the tray).</summary>
    void BeginSong(SongData newData, MeasureData[] freshDeck)
    {
        if (newData == null || newData.measures == null || newData.measures.Length == 0) return;
        bool fresh = freshDeck != null;
        if (PathManager.I != null) PathManager.I.ClearAllPaths(false);
        currentSongData = newData;
        var m0 = newData.measures[0];
        SongKey = MusicTheory.KeyOf(m0.chordRootMIDI, m0.semitones != null && m0.semitones.Length > 0 ? m0.semitones : new[] { 0, 4, 7 }); HasSongKey = true;   // SPEC v5 §2.1
        foreach (var m in currentSongData.measures) { if (m.bars <= 0) m.bars = 1; m.group = 0; }
        demoSeed = newData.demoSeed != 0 ? newData.demoSeed : MusicTheory.SeedFromPrompt(newData.songName);
        Climate = 0; Tone = 1f; Space = 1f; Synth.SetMasterCutoff01(1f);
        Instruments.Restore(null, null);               // a fresh song starts with every role at full level, unmuted
        for (int i = 0; i < moonRolls.Length; i++) moonRolls[i] = 0;
        ParseMeter(newData.timeSignature);
        GlobalClock.Stop();
        Performance.SetNominalBpm(newData.bpm > 0 ? newData.bpm : 100f);
        GlobalClock.SetSwing(0f);
        Transpose = 0;
        NormalizeSongData(currentSongData);            // v4: one-island columns in a clean row (a generated song carries no columns)
        bool demo = currentSongData.moons == null;     // the generators ship no Moons: add the demo Moon + groove (§3.4)
        sectionStarts.Clear();                         // v7: a new song's sections are automatic until its first snapshot writes them
        sectionLetters.Clear();                        // v9 (G): and its letters follow the content rule
        BuildIslands();
        if (demo) AddDemoMoon(fresh);
        RecomputeMeasureStarts();
        EnsureRouteAndComet();
        if (demo) AddDemoCubes(!fresh);
        RecomputeMeasureStarts();
        LayoutMoons(false);   // v6 §11.3: the Moons in line with their start columns
        RefreshGroupLook();
        Route.Rebuild();
        IslandTray.SetDeck(freshDeck);                 // before the first snapshot: the deck is part of the song state
        History.Reset();
        History.Push();
        OnSongRebuilt?.Invoke();
        RaiseColumnsChanged();
        RaiseSectionsChanged();
        Fx.I.SetSongBounds(SongBounds);
        if (OrbitCamera.I != null)
        {
            if (fresh) OrbitCamera.I.FrameStart(StartBounds());
            else { OrbitCamera.I.ResetView(); OrbitCamera.I.FocusMeasure(0, true); }
        }
        Fx.StartSweep();
        if (!fresh) GlobalClock.Play();                // Appendix C: v2 plays on load; a v3 fresh song waits (SPEC v3 §3.2)
        Debug.Log("World built for: " + newData.songName + " (" + newData.measures.Length + " measures, " + Moons.Count + " moons" + (fresh ? ", deck " + freshDeck.Length : "") + ")");
    }

    /// <summary>v4: a song's measures as a normalised row of columns (NormalizeColumns through a throw-away snapshot).</summary>
    static void NormalizeSongData(SongData d)
    {
        if (d == null || d.measures == null || d.measures.Length == 0) return;
        var st = new SongState { measures = d.measures.Select(MeasureState.From).ToArray(), cubes = new CubeState[0] };
        NormalizeColumns(st);
        d.measures = st.measures.Select((m, i) => m.ToData(i)).ToArray();
    }

    /// <summary>Island 0 and the Moon(s): what the camera frames when a fresh song starts.</summary>
    Bounds StartBounds()
    {
        var b = Islands.Count > 0 && Islands[0] != null ? Islands[0].WorldBounds : new Bounds(Vector3.zero, new Vector3(9f, 3f, 7f));
        foreach (var m in Moons) if (m != null) b.Encapsulate(m.WorldBounds);
        return b;
    }

    void ParseMeter(string ts)
    {
        int n = 4;
        if (!string.IsNullOrEmpty(ts) && ts.Contains("/"))
        {
            var parts = ts.Split('/');
            int.TryParse(parts[0].Trim(), out n);
            int.TryParse(parts[1].Trim(), out beatValue);
        }
        beatsPerMeasure = Mathf.Clamp(n, 2, 8);
        GlobalClock.SetBeatsPerBar(beatsPerMeasure);
    }

    void ClearIslands()
    {
        Islands.Clear();
        Moons.Clear();
        if (islandsRoot != null) Destroy(islandsRoot.gameObject);
        islandsRoot = null;
    }

    /// <summary>Builds the islands of currentSongData (already normalised: column-major, contiguous columns, positions laid out): each takes its
    /// column, the first of each column is its anchor (the only one wearing a hub).</summary>
    void BuildIslands()
    {
        ClearIslands();
        islandsRoot = new GameObject("Islands").transform;
        float x = 0f;
        int prevCol = int.MinValue;
        for (int i = 0; i < currentSongData.measures.Length; i++)
        {
            var md = currentSongData.measures[i];
            if (md.kind < 2 || md.kind > 4) md.kind = 0;   // v6 / v7: keyboards, stairs and phrases keep their kind; a stray Moon in the measure list is a chord island
            var go = new GameObject("Measure_" + i);
            go.transform.SetParent(islandsRoot, false);
            var kb = go.AddComponent<KeyBlock>();
            int col = md.col >= 0 ? md.col : i;
            kb.column = col; kb.isAnchor = col != prevCol;
            kb.BuildFromData(md, i);
            kb.SetAnchor(col != prevCol);
            prevCol = col;
            float offX = (kb.cols - 1) * ProjectConfig.Spacing * 0.5f;
            float offZ = (kb.rows - 1) * ProjectConfig.Spacing * 0.5f;
            if (md.placed) kb.SetPosition(md.px, md.pz);
            else kb.SetPosition(x + kb.Width * 0.5f - offX, -offZ);   // legacy row layout: an old save looks as it did
            kb.placed = true;   // materialised: the next Capture writes px/pz with placed = true (SPEC §2.2)
            Islands.Add(kb);
            x += kb.Width + ProjectConfig.gridSpacing;
        }
        if (currentSongData.moons != null)
            for (int j = 0; j < currentSongData.moons.Length && Moons.Count < MaxMoons; j++)
                if (currentSongData.moons[j] != null) BuildMoonBlock(currentSongData.moons[j], j);
        RefreshColumns();
        RefreshHomes();
        RefreshKeyboards();   // v6: the keyboards' chord-tone dots, once the columns are known
    }

    /// <summary>SPEC v5 §2.1: the house on the song key's home island(s) (Vibe.IsHome), after every build (the key is set before it).</summary>
    public void RefreshHomes() { foreach (var kb in Islands) if (kb != null) { kb.RefreshHome(); kb.RefreshJobBadge(); } }   // v6: + the job badges

    KeyBlock BuildMoonBlock(MeasureData md, int j)
    {
        md.kind = 1; md.bars = Mathf.Clamp(md.bars, 1, MaxMoonBars); md.group = 0;   // v9 (G): a Moon's groove may run several bars (1 = the v6 Moon)
        var go = new GameObject("Moon_" + j);
        go.transform.SetParent(islandsRoot, false);
        var kb = go.AddComponent<KeyBlock>();
        kb.column = Mathf.Max(0, md.col); kb.isAnchor = false;   // v6 §11.1: a Moon's column = its START column (older files: -1 = 0)
        kb.BuildFromData(md, j);
        float offX = (kb.cols - 1) * ProjectConfig.Spacing * 0.5f;
        float offZ = (kb.rows - 1) * ProjectConfig.Spacing * 0.5f;
        if (md.placed) kb.SetPosition(md.px, md.pz);
        else { var c = MoonSpawnCentre(); kb.SetPosition(c.x - offX, c.z - offZ); }
        kb.placed = true;
        Moons.Add(kb);
        return kb;
    }

    static Vector3 MoonSize => new Vector3(ProjectConfig.MoonRadius * 2.2f, 3f, ProjectConfig.MoonRadius * 2.2f);

    /// <summary>Where a new Moon appears (§2.2): in front of the song (SongCenter − (extents.z + 9) on z), nearest free spot.</summary>
    Vector3 MoonSpawnCentre()
    {
        var b = SongBounds;
        Vector3 near = new Vector3(b.center.x, 0f, b.center.z - (b.extents.z + 9f));
        return FindFreeSpot(near, Vector3.back, MoonSize);
    }

    void EnsureRouteAndComet()
    {
        if (Route == null) Route = new GameObject("Route").AddComponent<Route>();
        Comet.Ensure();
    }

    // ------------------------------------------------------------------ demo song (Appendix C, §3.4)
    void AddDemoMoon(bool fresh)
    {
        // v2: in front of the row of columns; v3 fresh song: beside island 0 on its front-left, so the song grows to the right
        Vector3 c = fresh ? FindFreeSpot(SongCenter + new Vector3(-11f, 0f, -2.5f), new Vector3(-1f, 0f, -0.35f), MoonSize) : FindFreeSpot(SongCenter, Vector3.back, MoonSize);
        var md = new MeasureData
        {
            chordKey = "Moon", chordRootMIDI = 36, semitones = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1,
            px = c.x - (ProjectConfig.MoonCols - 1) * ProjectConfig.Spacing * 0.5f, pz = c.z - (ProjectConfig.MoonRows - 1) * ProjectConfig.Spacing * 0.5f
        };
        currentSongData.moons = new[] { md };
        BuildMoonBlock(md, 0);
    }

    static CubeState Cube(int instrument, int measure, int moon, int[] xs, int[] zs, int step, int hits, int rot, bool rider = false)
    {
        return new CubeState { instrument = instrument, measure = measure, moon = moon, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = 1, mode = 0, volume = 1f, hits = hits, rot = rot, rider = rider, twinOf = -1 };
    }

    /// <summary>The demo groove on the Moon; with <paramref name="islandCubes"/> (v2) also a Keys cube stamped to every island and a Bass Rider on island 0.</summary>
    void AddDemoCubes(bool islandCubes)
    {
        var pm = PathManager.I; if (pm == null) return;
        int drums = Instruments.Count - 1; for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) { drums = i; break; }
        if (Moons.Count > 0)
        {
            // four on the floor: kick E(4,4) Quarter, snare E(2,4) rot 1 Quarter, closed hat E(8,8) Eighth at column 2
            pm.RestoreCube(Cube(drums, 0, 0, new[] { 3 }, new[] { 0 }, (int)StepLen.Quarter, 4, 0));
            pm.RestoreCube(Cube(drums, 0, 0, new[] { 3 }, new[] { 1 }, (int)StepLen.Quarter, 2, 1));
            pm.RestoreCube(Cube(drums, 0, 0, new[] { 2 }, new[] { 2 }, (int)StepLen.Eighth, 8, 0));
        }
        if (!islandCubes) return;
        // one Keys cube stamped to every island (stairs up, Eighth, E(5,8)) and one Bass Rider on island 0 (root-fifth, Quarter)
        for (int i = 0; i < Islands.Count; i++)
        {
            var kb = Islands[i]; if (kb == null) continue;
            int r = Mathf.Max(1, kb.rows);
            pm.RestoreCube(Cube(0, i, -1, new[] { 0, 1, 2, 3 }, new[] { 0, Mathf.Min(1, r - 1), Mathf.Min(2, r - 1), Mathf.Min(3, r - 1) }, (int)StepLen.Eighth, 5, 0));
        }
        if (Islands.Count > 0 && Islands[0] != null)
            pm.RestoreCube(Cube(4, 0, -1, new[] { 0, 0 }, new[] { 0, Mathf.Min(2, Mathf.Max(1, Islands[0].rows) - 1) }, (int)StepLen.Quarter, -1, 0, true));
    }

    // ------------------------------------------------------------------ timeline + windows (SPEC §2.10, v4 §3 K2)
    /// <summary>
    /// v4: the timeline runs column by column. Per column: start (the sum of the previous columns' lengths) and length, shared by its islands;
    /// every island of a column gets startBeatOffset = MeasureStarts[i] = its column's start; TotalBeats = Σ column lengths. v5 (SPEC v5 §2.5):
    /// a column plays ColumnPasses(c) = the max repeat of its islands passes of PassLength(c) = bars·BeatsPerBar beats, so ColumnLength =
    /// PassLength × passes. Windows: a resident gets one window per pass its island plays (passes 0 .. repeat − 1: start = column start + p ×
    /// PassLength, length = PassLength, order = the column; silent while the island sleeps; no window in the passes it rests), a Rider one window
    /// per pass of every column on the island of that column nearest in z to its home island, Moon cubes one window per bar of the song (never
    /// silent). A song without repeats gets exactly the v4 (and, of one-island columns, the v3) timeline. The only writer of AudioCube.windows.
    /// v6 carry (SPEC v6 §2.4): a resident cube of an island with carry ≥ 1 gets, after its own windows, one window per pass of each of its island's
    /// CarryTargets' columns on that target (carried = true, hop = 1.. its place in the chain, order = that column, silent when the home island or
    /// the target sleeps): one owner, its home windows, then its carried ones, in song order. Riders and Moon cubes are unchanged.
    /// v7 §21 (the user: "just keep cubes on each grid. only explore grids moving from left to right with the long grid"): every carried window is a
    /// FLOW window (carryStyle is ignored) and the targets are the grids of the source's LANE in its SECTION (<see cref="CarryTargets(int)"/>: the
    /// long grid); a Rider is an ordinary cube of its home island (no windows on other islands; the `rider` flag stays in the file, ignored).
    /// </summary>
    public void RecomputeMeasureStarts()
    {
        RefreshColumns();
        MeasureStarts.Clear();
        colStart.Clear(); colLen.Clear(); colPass.Clear(); colPasses.Clear();
        float t = 0f;
        int bpb0 = Mathf.Max(1, GlobalClock.BeatsPerBar);
        for (int c = 0; c < colFirst.Count; c++)
        {
            // v7: a phrase never sets its column's length or passes (it lives in a melody track; its own time is phraseBeats)
            KeyBlock a = null;
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c] && a == null; k++) if (Islands[k] != null && !Islands[k].IsPhrase) a = Islands[k];
            // v8: a column is as long as its longest grid's END (a shorter chord grid may start on a later measure: barOffset)
            float pass = 0f;
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) { var o = Islands[k]; if (o != null && !o.IsPhrase) pass = Mathf.Max(pass, OffsetBeats(o, bpb0) + o.LengthBeats); }
            if (a == null || pass <= 0f) pass = a != null ? a.LengthBeats : (Islands[colFirst[c]] != null ? Mathf.Max(1, Islands[colFirst[c]].bars) * bpb0 : bpb0);
            int passes = 1;
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) if (Islands[k] != null && !Islands[k].IsPhrase) passes = Mathf.Max(passes, Islands[k].Passes);
            colStart.Add(t); colPass.Add(pass); colPasses.Add(passes); colLen.Add(pass * passes);
            t += pass * passes;
        }
        foreach (var kb in Islands)
        {
            if (kb == null) { MeasureStarts.Add(0f); continue; }
            int c = Mathf.Clamp(kb.column, 0, Mathf.Max(0, colStart.Count - 1));
            float s = colStart.Count > 0 ? colStart[c] : 0f;
            float so = s + OffsetBeats(kb, bpb0);   // v8: a grid starting on a later measure of its column
            MeasureStarts.Add(so);
            kb.startBeatOffset = kb.IsPhrase ? s + kb.phraseOffset / (float)ProjectConfig.TicksPerBeat : so;   // v7: a phrase starts at its offset
            kb.SetColumnPasses(colPasses.Count > 0 ? colPasses[c] : 1);
        }
        TotalBeats = t > 0f ? t : 4f;
        GlobalClock.TotalBeats = TotalBeats;
        PlayOrder.Clear();
        for (int i = 0; i < Islands.Count; i++) PlayOrder.Add(i);
        foreach (var m in Moons) if (m != null) m.startBeatOffset = 0f;
        RefreshGrounds();   // v7 (SPEC v7 §2.5): the columns' ground targets (OnGroundsChanged when they changed)

        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        int barsTotal = Mathf.Max(1, Mathf.RoundToInt(TotalBeats / bpb));
        var wl = new List<AudioCube.Window>(8);
        // v6: each carrying island's targets, once per recompute (CarryTargets' rule; the windows and the world magic's bridges agree); v7 §21: the long
        // grids are re-derived here too (the joined look, LongGridOf, OnLongGridsChanged)
        while (carryBuf.Count < Islands.Count) carryBuf.Add(new List<KeyBlock>(ProjectConfig.MaxCarry));
        for (int i = 0; i < Islands.Count; i++) { carryBuf[i].Clear(); if (Islands[i] != null && Islands[i].carry > 0) CarryTargets(i, carryBuf[i]); }
        RefreshLongGrids();
        foreach (var cube in SequenceMaster.Cubes)
        {
            if (cube == null) continue;
            wl.Clear();
            if (cube.moon >= 0 || cube.Moon != null)
            {
                var moonKb = cube.Moon != null ? cube.Moon : (cube.moon < Moons.Count ? Moons[cube.moon] : null);
                if (moonKb == null) continue;
                // v6 §11.2: one window per bar of its Moon's SECTION (a Moon at column 0 alone = every bar of the song: the v5 windows exactly)
                float ms0, ms1;
                int b0 = 0, b1 = barsTotal;
                if (MoonSection(Moons.IndexOf(moonKb), out ms0, out ms1)) { b0 = Mathf.Clamp(Mathf.RoundToInt(ms0 / bpb), 0, barsTotal); b1 = Mathf.Clamp(Mathf.RoundToInt(ms1 / bpb), b0, barsTotal); }
                // v9 (G): a Moon whose groove runs several bars (its bars, 1..MaxMoonBars) gets one window per groove (the last one cut at its section's
                // end); bars 1 = one window per bar, the v6 windows exactly
                int mb = Mathf.Clamp(moonKb.bars, 1, MaxMoonBars);
                for (int b = b0; b < b1; b += mb)
                {
                    float ws = b * bpb; int wc = ActiveColumn(ws + 1e-3f);
                    wl.Add(new AudioCube.Window { start = ws, length = Mathf.Min(mb, b1 - b) * bpb, island = moonKb, order = b, silent = false, pass = PassAt(wc, ws + 1e-3f) });   // v7: the pass of the column under the bar
                }
                if (wl.Count == 0) continue;
            }
            else
            {
                // v7 §21.1: a Rider (v4) no longer plays on other islands — it gets the windows of any cube of its home island
                int m = cube.assignedGridIndex;
                if (m < 0 || m >= Islands.Count || m >= MeasureStarts.Count || Islands[m] == null) continue;
                var kb = Islands[m];
                int c = Mathf.Clamp(kb.column, 0, Mathf.Max(0, colLen.Count - 1));
                if (kb.IsPhrase)
                {
                    // v7 (SPEC v7 §14.2): a phrase plays ONE window per song pass over its own span (never per column pass: a column repeat does not
                    // stretch it); its repeat r = r windows back to back
                    float ps = kb.startBeatOffset, pl = Mathf.Max(1, kb.phraseBeats);
                    for (int p = 0; p < kb.Passes; p++)
                    {
                        float ws = ps + p * pl;
                        if (ws >= TotalBeats - 1e-4f) break;
                        wl.Add(new AudioCube.Window { start = ws, length = pl, island = kb, order = kb.column, silent = kb.sleep, pass = p });
                    }
                    if (wl.Count == 0) wl.Add(new AudioCube.Window { start = Mathf.Min(ps, Mathf.Max(0f, TotalBeats - pl)), length = pl, island = kb, order = kb.column, silent = true, pass = 0 });
                }
                else
                {
                    float pl = colPass.Count > 0 ? colPass[c] : kb.LengthBeats;
                    int reps = colPasses.Count > 0 ? Mathf.Min(kb.Passes, colPasses[c]) : 1;
                    float own = Mathf.Min(pl, kb.LengthBeats);   // v8: a grid plays its own measures (a shorter grid in a longer column rests in the rest)
                    for (int p = 0; p < reps; p++)   // v5: one window per pass the island plays (it rests in the column's other passes)
                        wl.Add(new AudioCube.Window { start = MeasureStarts[m] + p * pl, length = own, island = kb, order = kb.column, silent = kb.sleep, pass = p });
                    // v6 carry: then every pass of each target's column on the target (the pattern played on the next chords); v7 §21: always FLOW (the
                    // long grid: re-voiced by pedal, an ordinary step into the next measure), whatever the island's carryStyle
                    var targets = m < carryBuf.Count ? carryBuf[m] : null;
                    const bool flow = true;
                    if (targets != null)
                        for (int k = 0; k < targets.Count; k++)
                        {
                            var tgt = targets[k]; int tc = tgt != null ? tgt.column : -1;
                            if (tc < 0 || tc >= colStart.Count) continue;
                            for (int p = 0; p < colPasses[tc]; p++)
                                wl.Add(new AudioCube.Window { start = colStart[tc] + p * colPass[tc] + OffsetBeats(tgt, bpb), length = Mathf.Min(colPass[tc], tgt.LengthBeats), island = tgt, order = tc, silent = kb.sleep || tgt.sleep, carried = true, hop = k + 1, pass = p, flow = flow });
                        }
                }
            }
            cube.startBeatOffset = wl[0].start; cube.measureLength = wl[0].length;
            cube.SetWindows(wl);
        }
        if (GlobalClock.SongBeatD >= TotalBeats) GlobalClock.Seek(0);
        foreach (var kb in Islands) if (kb != null) kb.UpdatePreLight();
        foreach (var kb in Moons) if (kb != null) kb.UpdatePreLight();
    }

    // ------------------------------------------------------------------ v4 columns (SPEC v4 §2.3)
    /// <summary>Fires after every layout change (a rebuild, a live relayout, a committed in-column move, a register change).</summary>
    public event Action OnColumnsChanged;
    void RaiseColumnsChanged() { ColumnBands.Refresh(); RefreshKeyboards(); OnColumnsChanged?.Invoke(); }
    /// <summary>v6: every keyboard's chord-tone dots follow its column's chord (a rebuild, a move, a relayout may change it).</summary>
    void RefreshKeyboards() { foreach (var kb in Islands) if (kb != null) { if (kb.IsKeyboard) kb.RefreshKeyDots(); else if (kb.IsStairs || kb.IsPhrase) kb.RefreshKindChords(); } }   // v7: + the stairs' steps, the phrases' chord tints (B)
    readonly List<List<KeyBlock>> carryBuf = new List<List<KeyBlock>>();
    readonly List<int> colFirst = new List<int>(), colCount = new List<int>(), colPasses = new List<int>();
    readonly List<float> colStart = new List<float>(), colLen = new List<float>(), colPass = new List<float>();

    /// <summary>Re-reads the column ranges from the islands (kb.column; Islands are column-major).</summary>
    void RefreshColumns()
    {
        colFirst.Clear(); colCount.Clear();
        int prev = int.MinValue;
        for (int i = 0; i < Islands.Count; i++)
        {
            var kb = Islands[i]; if (kb == null) continue;
            if (colFirst.Count == 0 || kb.column != prev) { colFirst.Add(i); colCount.Add(1); prev = kb.column; }
            else colCount[colCount.Count - 1]++;
        }
    }

    public int ColumnCount => colFirst.Count;
    public int ColumnOf(int island) => island >= 0 && island < Islands.Count && Islands[island] != null ? Islands[island].column : -1;
    /// <summary>The islands of column <paramref name="col"/>, anchor first (a new list; see the non-allocating overload).</summary>
    public List<KeyBlock> ColumnIslands(int col) { var l = new List<KeyBlock>(); ColumnIslands(col, l); return l; }
    /// <summary>The islands of column <paramref name="col"/>, anchor first, into <paramref name="into"/> (cleared first; no allocation).</summary>
    public void ColumnIslands(int col, List<KeyBlock> into)
    {
        into.Clear();
        if (col < 0 || col >= colFirst.Count) return;
        for (int k = colFirst[col]; k < colFirst[col] + colCount[col]; k++) into.Add(Islands[k]);
    }
    /// <summary>Island index of column <paramref name="col"/>'s anchor (-1 none).</summary>
    public int ColumnFirst(int col) => col >= 0 && col < colFirst.Count ? colFirst[col] : -1;
    /// <summary>How many islands column <paramref name="col"/> holds (≤ MaxLanes).</summary>
    public int ColumnSize(int col) => col >= 0 && col < colCount.Count ? colCount[col] : 0;
    public KeyBlock AnchorOf(int col) => col >= 0 && col < colFirst.Count ? Islands[colFirst[col]] : null;
    public float ColumnStart(int col) => col >= 0 && col < colStart.Count ? colStart[col] : 0f;
    /// <summary>The whole span of column <paramref name="col"/> in beats (v5: PassLength × ColumnPasses — every pass of its belt).</summary>
    public float ColumnLength(int col) => col >= 0 && col < colLen.Count ? colLen[col] : 0f;
    /// <summary>SPEC v5 §2.5: passes of column <paramref name="col"/> = the max repeat of its islands (1 without repeats).</summary>
    public int ColumnPasses(int col) => col >= 0 && col < colPasses.Count ? colPasses[col] : 1;
    /// <summary>SPEC v5 §2.5: one pass of column <paramref name="col"/> in beats = bars × BeatsPerBar (the column's length without repeats).</summary>
    public float PassLength(int col) => col >= 0 && col < colPass.Count ? colPass[col] : 0f;
    /// <summary>The pass of column <paramref name="col"/> playing at <paramref name="beat"/> (0 .. ColumnPasses − 1, clamped; 0 outside the column).</summary>
    public int PassAt(int col, float beat)
    {
        if (col < 0 || col >= colPass.Count || colPass[col] <= 0f) return 0;
        float local = beat - colStart[col];
        if (local < 0f || local >= colLen[col]) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(local / colPass[col] + 1e-5f), 0, colPasses[col] - 1);
    }
    /// <summary>v5: the x extent of column <paramref name="col"/>'s longest belt ((passes − 1) × SlotPitch; 0 without repeats).</summary>
    public float BeltExtent(int col)
    {
        int p = 1;   // v7: rewind islands and phrases ride no belt; a slot is the column's width + BeltGap
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++) { var kb = Islands[k]; if (kb != null && kb.HasBelt) p = Mathf.Max(p, kb.Passes); }
        return (p - 1) * (ColumnWidth(col) + ProjectConfig.BeltGap);
    }
    /// <summary>The start beat of pass <paramref name="pass"/> of column <paramref name="col"/>.</summary>
    public float PassStart(int col, int pass) => ColumnStart(col) + Mathf.Clamp(pass, 0, Mathf.Max(0, ColumnPasses(col) - 1)) * PassLength(col);

    /// <summary>
    /// SPEC v5 §2.5 the ride: where island <paramref name="kb"/> stands on its conveyor belt at song beat <paramref name="beat"/>, in slots (0 = home,
    /// pass p plays at slot p; Belt.Ride: the jerk at each pass boundary). v7 §12.2 (the user: "the conveyor belt should only go back after the song
    /// resets, not directly afterward"): after its last pass it STAYS at its last slot until the song resets (the loop point), then glides back
    /// (BeltGlideBeats, with a hop — <paramref name="lift"/>) slightly out of sync with the other belts (ResetStaggerOf); a column that plays again
    /// right at the reset (it starts the loop) glides home in the last beats before the loop point, and one whose turn fills the loop glides home in
    /// its last pass (v5). A stop: KeyBlock animates the trip home in real time. 0 when stopped at beat 0, for rewind islands and phrases (no belt).
    /// A pure function of the beat, the loop state and the timeline (<see cref="HoldPhase"/>).
    /// </summary>
    public float RideSlot(KeyBlock kb, double beat, out float lift)
    {
        lift = 0f;
        if (kb == null || !kb.HasBelt) return 0f;
        int c = kb.column;
        if (c < 0 || c >= colStart.Count || colPass[c] <= 0f) return 0f;
        if (!GlobalClock.IsPlaying && beat <= 1e-6) return 0f;
        int passes = Mathf.Min(kb.Passes, colPasses[c]);
        if (passes < 2) return 0f;
        double s = colStart[c], end = s + passes * (double)colPass[c];
        double ls = GlobalClock.LoopStartBeat, le = GlobalClock.LoopEndBeat;
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        bool selfLoop = looping && Math.Abs(s - ls) < 1e-3 && Math.Abs(end - le) < 1e-3;
        if (beat >= s && beat < end) return Belt.Ride(beat - s, passes, colPass[c], selfLoop, out lift);
        double u;
        int hp = HoldPhase(s, end, beat, ProjectConfig.BeltGlideBeats, ResetStaggerOf(kb), 0.0, false, out u);
        float held = passes - 1;
        if (hp == 2) return held;
        if (hp == 3) { lift = ProjectConfig.BeltGlideLift * Mathf.Sin((float)u * Mathf.PI); return held * (1f - Ease.InOutCubic((float)u)); }
        return 0f;
    }

    // ---- v7 §12 hold-then-return (K): belts, towers and Moons stay where their turn ended until the song resets, then go home by moving
    /// <summary>
    /// v7 §12 (the user: "it shouldnt teleport back to its starting position, they should only do that when the song resets (or if the grid is
    /// playing again), and no teleportation, just movement"): where something whose TURN is [<paramref name="s"/>, <paramref name="e"/>) stands at
    /// song beat <paramref name="beat"/> — 1 inside its turn (the caller poses it), 2 HELD where its turn ended, 3 RETURNING home (<paramref name="u"/>
    /// 0..1 through the trip of <paramref name="dur"/> beats, starting <paramref name="stagger"/> beats after the reset and arriving by
    /// s − <paramref name="lead"/>), 0 home. "The song resets" = the loop point (the song loop or the focus loop wraps; a stop is animated by
    /// KeyBlock in real time). A turn before the focus loop resets at the first wrap. A turn that plays again right at the reset (it starts the loop)
    /// returns in the last dur beats before the loop point, landing as it restarts — or, with <paramref name="stayIfReplays"/> (a tower), just stays.
    /// Pure: seeks and loops land right.
    /// </summary>
    public static int HoldPhase(double s, double e, double beat, double dur, double stagger, double lead, bool stayIfReplays, out double u)
    {
        u = 0.0;
        if (beat >= s && beat < e) return 1;
        const double eps = 1e-3;
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        double ls = GlobalClock.LoopStartBeat, le = GlobalClock.LoopEndBeat;
        int li = GlobalClock.LoopIndex;
        bool inLoop = s >= ls - eps && e <= le + eps && e > s;
        bool starts = looping && inLoop && Math.Abs(s - ls) < eps;
        dur = Math.Max(0.05, dur);
        if (beat >= e)
        {
            if (!looping) return 2;
            if (e <= ls + eps && li >= 1 && beat >= ls - eps)
            {
                if (li > 1) return 0;                                           // a turn before the focus loop went home at the first wrap
                return Trip(beat, ls + stagger, dur, double.MaxValue, ls, out u);
            }
            if (starts && beat < le + eps)
            {
                if (stayIfReplays) return 2;
                double t0 = Math.Max(e, le - dur);                              // it plays again right at the reset: home by the loop point
                if (beat < t0) return 2;
                u = Math.Min(1.0, Math.Max(0.0, (beat - t0) / Math.Max(1e-3, le - t0)));
                return u >= 1.0 ? 0 : 3;
            }
            return 2;
        }
        // before its turn in this loop pass: it held at the reset (it played in the pass before) and goes home now
        if (looping && li >= 1 && inLoop && !starts && beat >= ls - eps) return Trip(beat, ls + stagger, dur, s - lead, ls, out u);
        return 0;
    }

    /// <summary>The trip home from t0 over dur beats, compressed to arrive by <paramref name="arriveBy"/> (never before <paramref name="floor"/>).</summary>
    static int Trip(double beat, double t0, double dur, double arriveBy, double floor, out double u)
    {
        u = 0.0;
        double t1 = t0 + dur;
        if (t1 > arriveBy - 0.05) { t1 = arriveBy - 0.05; t0 = Math.Max(floor, Math.Min(t0, t1 - Math.Min(dur, 0.25))); }
        if (t1 <= t0 + 1e-4) return beat < t0 ? 2 : 0;
        if (beat < t0) return 2;
        if (beat >= t1) { u = 1.0; return 0; }
        u = (beat - t0) / (t1 - t0);
        return 3;
    }

    /// <summary>v7 §12: "not in sync, but slightly out of sync" — how many beats after a reset island / Moon <paramref name="kb"/> starts home
    /// (0..ResetStaggerBeats by a hash of its index; stable while the song's structure is).</summary>
    public float ResetStaggerOf(KeyBlock kb) => kb == null ? 0f : ResetStagger(kb.IsMoon ? 7001 + kb.measureIndex : kb.measureIndex);
    /// <summary>v7 §12: the stagger (0..ResetStaggerBeats beats) for hash key <paramref name="key"/> (H uses a cube's id).</summary>
    public static float ResetStagger(int key)
    {
        unchecked
        {
            uint h = (uint)(key + 1) * 2654435761u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return ProjectConfig.ResetStaggerBeats * (h & 0xFFFF) / 65535f;
        }
    }

    /// <summary>v7: island <paramref name="kb"/>'s turn in song beats: its column's start to the end of its own last pass (a phrase: its start to the
    /// end of its last pass). False without a timeline.</summary>
    public bool TurnSpan(KeyBlock kb, out double start, out double end)
    {
        start = end = 0.0;
        if (kb == null || kb.IsMoon) return false;
        if (kb.IsPhrase) { start = kb.startBeatOffset; end = start + Mathf.Max(1, kb.phraseBeats) * (double)kb.Passes; return true; }
        int c = kb.column;
        if (c < 0 || c >= colStart.Count || colPass[c] <= 0f) return false;
        start = colStart[c]; end = start + colPass[c] * (double)Mathf.Clamp(kb.Passes, 1, colPasses[c]);
        return true;
    }

    /// <summary>
    /// SPEC v5 §2.6 free island drag, the commit: moves island <paramref name="island"/> into column <paramref name="targetCol"/> at z
    /// <paramref name="pz"/> (its root) — it JOINS that column after its islands (the anchor stays the anchor; the column's others make room;
    /// bars equalised to the column's max: nothing is cut) — or, <paramref name="asNewColumn"/>, into a NEW column inserted at position
    /// <paramref name="targetCol"/> of the current columns (0 = the front, ColumnCount = the end; never inside a glued run: it moves past it).
    /// Its own column as the target = the in-column move (live, no rebuild). A column left empty disappears (the later columns slide left; the
    /// song's start stays put, a new first column appears on its left); a stacked column it leaves promotes its next island to anchor. The island
    /// keeps its chord, register, repeat, sleep and cubes (remapped); a merge-group member moves with its whole group as a block (the west-most
    /// member goes to <paramref name="targetCol"/>, the others to the following columns). The song key never changes. Seek-keeping rebuild (the
    /// lit column keeps its local beat), one History entry. Returns the island's new index, -1 when refused (a full column shakes).
    /// </summary>
    public int MoveIslandToColumn(int island, int targetCol, bool asNewColumn, float pz) => MoveIslandToColumn(island, targetCol, asNewColumn, pz, 0);

    /// <summary>v8: <see cref="MoveIslandToColumn(int, int, bool, float)"/> — a chord grid joining a column starts on its measure
    /// <paramref name="measure"/> (0-based; the column grows to fit, up to 4 measures); a new column starts it at 0.</summary>
    public int MoveIslandToColumn(int island, int targetCol, bool asNewColumn, float pz, int measure)
    {
        if (!ValidIsland(island)) return -1;
        var kb = Islands[island];
        var block = GroupOf(kb);
        var movers = new List<int>(block.Count);
        foreach (var m in block) { int k = Islands.IndexOf(m); if (k >= 0) movers.Add(k); }
        if (movers.Count == 0) return -1;
        int nc = ColumnCount, count = movers.Count, west = Islands[movers[0]].column;
        var moverSet = new HashSet<int>(movers);
        pz = Mathf.Round(pz / ProjectConfig.IslandSnap) * ProjectConfig.IslandSnap;
        if (!asNewColumn)
        {
            if (targetCol < 0 || targetCol >= nc) return -1;
            if (targetCol == west)
            {
                // its own column: the in-column move (live: the column's others are pushed / swap, no rebuild)
                MoveIslandInColumn(island, pz, true);
                History.Push();
                return island;
            }
            for (int k = 0; k < count; k++)
            {
                int c = targetCol + k;
                if (c >= nc) break;   // past the last column: new columns at the end
                int stay = 0; for (int i = ColumnFirst(c); i >= 0 && i < ColumnFirst(c) + ColumnSize(c); i++) if (!moverSet.Contains(i) && !Islands[i].IsPhrase) stay++;   // v7: phrases are no lanes
                if (stay >= ProjectConfig.MaxLanes && !Islands[movers[k]].IsPhrase) { Refuse(c); return -1; }
            }
        }
        else
        {
            targetCol = Mathf.Clamp(targetCol, 0, nc);
            // never inside a glued run (of islands that stay): move past it
            while (targetCol > 0 && targetCol < nc && GluedByOthers(targetCol - 1, moverSet)) targetCol++;
        }
        var st = SongState.Capture();
        var oldSecs = SectionsOf(st);
        int n = st.measures.Length;
        float x0 = n > 0 ? st.measures[0].px : 0f;
        var key = new double[n];
        for (int i = 0; i < n; i++) key[i] = st.measures[i].col;
        for (int k = 0; k < count; k++)
        {
            int m = movers[k];
            key[m] = asNewColumn ? targetCol - 0.5 + k * 0.01 : targetCol + k;   // a join past the last column = new columns at the end
            st.measures[m].pz = pz;
            st.measures[m].barOffset = !asNewColumn && count == 1 && st.measures[m].kind == 0 ? Mathf.Max(0, measure) : 0;   // v8: the measure it lands on
        }
        // column-major order: by column key, the joining islands after the column's own ones (its anchor stays), then creation order
        var order = Enumerable.Range(0, n).OrderBy(i => key[i]).ThenBy(i => moverSet.Contains(i) ? 1 : 0).ThenBy(i => i).ToArray();
        var map = new int[n]; var ms = new MeasureState[n];
        int col = -1; double prevKey = double.NaN;
        for (int r = 0; r < n; r++)
        {
            int i = order[r];
            map[i] = r; ms[r] = st.measures[i];
            if (double.IsNaN(prevKey) || Math.Abs(key[i] - prevKey) > 1e-6) { col++; prevKey = key[i]; }
            ms[r].col = col;
        }
        st.measures = ms;
        if (st.cubes != null) foreach (var c in st.cubes) if (c != null && c.moon < 0 && c.measure >= 0 && c.measure < n) c.measure = map[c.measure];
        // v7: the columns that keep an island stay in their section; a column made only of movers (a new column) joins by the landing rule;
        // an emptied column leaves its section
        var oldCols = new int[n];
        for (int i = 0; i < n; i++) oldCols[i] = Islands[i] != null ? Islands[i].column : 0;
        st.sections = RemapSections(oldSecs, ColumnOrigins(st, map, oldCols, moverSet), SnapshotColumnBars(st.measures)).ToArray();
        FixSections(st);
        // x: the song's start stays where it was; the old first column keeps its place when a new column is inserted in front of it
        int pin = map[island];
        ms[0].px = x0;
        NormalizeColumns(st, pin);
        int refOld = -1;
        for (int i = 0; i < n && refOld < 0; i++) if (!moverSet.Contains(i) && Islands[i] != null && Islands[i].column == 0) refOld = i;
        if (refOld >= 0)
        {
            float dx = x0 - st.measures[map[refOld]].px;
            if (Mathf.Abs(dx) > 1e-4f) foreach (var m in st.measures) m.px += dx;
        }
        var result = RebuildKeepingLit(st, map, pin, moverSet);
        History.Push();
        int at = island >= 0 && island < result.Length ? result[island] : -1;
        if (at >= 0 && at < Islands.Count && Islands[at] != null) Fx.SeaRipple(Islands[at].Center, Islands[at].chordColor, 3f);
        AudioPool.UI(ProceduralAudio.Thud(), 0.28f, asNewColumn ? 1.2f : 1.0f);
        return at;
    }

    /// <summary>True when columns <paramref name="col"/> and col + 1 are glued through islands that are not in <paramref name="movers"/> (a moving
    /// group's own glue does not pin the slot it leaves).</summary>
    bool GluedByOthers(int col, ICollection<int> movers)
    {
        if (col < 0 || col + 1 >= colFirst.Count) return false;
        for (int i = colFirst[col]; i < colFirst[col] + colCount[col]; i++)
        {
            if (movers.Contains(i)) continue;
            var a = Islands[i]; if (a == null || a.group == 0) continue;
            for (int j = colFirst[col + 1]; j < colFirst[col + 1] + colCount[col + 1]; j++) if (!movers.Contains(j) && Islands[j] != null && Islands[j].group == a.group) return true;
        }
        return false;
    }
    /// <summary>The column playing at <paramref name="beat"/> (the last column starting at or before it; 0 before the first).</summary>
    public int ActiveColumn(float beat)
    {
        int c = 0, n = Mathf.Min(colStart.Count, colFirst.Count);
        for (int k = 1; k < n; k++) { if (beat >= colStart[k]) c = k; else break; }
        return c;
    }
    public int LitColumn => ActiveColumn(GlobalClock.SongBeat);
    /// <summary>True when <paramref name="kb"/> plays at <paramref name="beat"/>: its column spans it (every island of a column plays together) and, v5,
    /// the beat falls in one of the island's own passes (an island with fewer passes than its column rests in the later ones).</summary>
    public bool IsActiveAt(KeyBlock kb, float beat) => kb != null && !kb.IsMoon && beat >= kb.startBeatOffset && beat < kb.startBeatOffset + kb.LengthBeats * kb.Passes;
    /// <summary>x of column <paramref name="col"/>'s islands (their roots).</summary>
    public float ColumnX(int col) { var a = AnchorOf(col); return a != null ? a.px - OffsetBars(a) * KeyBlock.IslandWidth : 0f; }

    /// <summary>v8 (the user: "grids added above it should not be forced to be extended … i should also be able to add a normal grid on the second
    /// measure of an extended grid"): a chord grid's first measure inside its column (barOffset; other kinds start at the column start), in measures
    /// and in beats.</summary>
    /// <summary>v9: the kinds that may start on a later measure of their column (chord grids, keyboards, stairs; Moons and phrases never).</summary>
    public static bool Offsettable(int kind) => kind == 0 || kind == 2 || kind == 3;
    public static int OffsetBars(KeyBlock kb) => kb != null && Offsettable(kb.kind) ? Mathf.Clamp(kb.barOffset, 0, 3) : 0;
    static float OffsetBeats(KeyBlock kb, int bpb) => OffsetBars(kb) * (float)Mathf.Max(1, bpb);
    static int OffsetBars(MeasureState m) => m != null && Offsettable(m.kind) ? Mathf.Clamp(m.barOffset, 0, 3) : 0;

    /// <summary>v8: moves chord grid <paramref name="island"/> to start on measure <paramref name="measure"/> of its column (0-based; the column grows
    /// to fit, up to 4 measures) at z <paramref name="pz"/> (NaN keeps it). Snapshot rebuild, one History entry. False when nothing changed or it is
    /// not a chord grid.</summary>
    public bool SetBarOffset(int island, int measure, float pz = float.NaN)
    {
        if (!ValidIsland(island)) return false;
        var st = SongState.Capture();
        var m = st.measures[island];
        if (m.kind != 0) return false;
        int off = Mathf.Clamp(measure, 0, 4 - Mathf.Clamp(m.bars, 1, 4));
        bool zMove = !float.IsNaN(pz) && Mathf.Abs(pz - m.pz) > 1e-3f;
        if (off == m.barOffset && !zMove) return false;
        m.barOffset = off;
        if (zMove) m.pz = Mathf.Round(pz / ProjectConfig.IslandSnap) * ProjectConfig.IslandSnap;
        rebuildPin = island;
        RebuildFromState(st);
        History.Push();
        return true;
    }
    /// <summary>True when columns <paramref name="col"/> and col + 1 are glued (an island of each shares a merge group: no gap between them).</summary>
    public bool Glued(int col)
    {
        if (col < 0 || col + 1 >= colFirst.Count) return false;
        for (int i = colFirst[col]; i < colFirst[col] + colCount[col]; i++)
        {
            var a = Islands[i]; if (a == null || a.group == 0) continue;
            for (int j = colFirst[col + 1]; j < colFirst[col + 1] + colCount[col + 1]; j++) if (Islands[j] != null && Islands[j].group == a.group) return true;
        }
        return false;
    }
    /// <summary>True when the anchors of columns <paramref name="col"/> and col + 1 share a group: the Route has no cable there (the comet glides low).</summary>
    public bool AnchorSeam(int col) => col >= 0 && col + 1 < colFirst.Count && SameGroup(Islands[colFirst[col]], Islands[colFirst[col + 1]]);
    /// <summary>World bounds of column <paramref name="col"/>'s islands and their belts (the camera frames the lit column).</summary>
    public Bounds ColumnBounds(int col)
    {
        if (col < 0 || col >= colFirst.Count) return SongBounds;
        var b = Islands[colFirst[col]].GroundedBounds;   // v7: at the height it stands on (the camera frames heights)
        for (int k = colFirst[col] + 1; k < colFirst[col] + colCount[col]; k++) if (Islands[k] != null) b.Encapsulate(Islands[k].GroundedBounds);
        return b;
    }
    /// <summary>The last column of the glued run that starts at (or runs through) <paramref name="col"/>: a new column never lands inside a run.</summary>
    public int GlueRunEnd(int col) { int c = Mathf.Clamp(col, 0, Mathf.Max(0, colFirst.Count - 1)); while (Glued(c)) c++; return c; }

    /// <summary>v4: the island index of the ANCHOR of the column playing at <paramref name="beat"/>.</summary>
    public int ActiveMeasureIndex(float beat)
    {
        if (colFirst.Count == 0) return 0;
        return colFirst[Mathf.Min(ActiveColumn(beat), colFirst.Count - 1)];
    }

    /// <summary>Bounds of every island (with its belt) and Moon.</summary>
    public Bounds SongBounds
    {
        get
        {
            if (Islands.Count == 0) return new Bounds(new Vector3(3f, 0f, 2f), new Vector3(10f, 3f, 8f));
            bool any = false; var b = new Bounds();
            foreach (var kb in Islands) { if (kb == null) continue; if (!any) { b = kb.FootprintBounds; any = true; } else b.Encapsulate(kb.FootprintBounds); }
            foreach (var kb in Moons) { if (kb == null) continue; if (!any) { b = kb.WorldBounds; any = true; } else b.Encapsulate(kb.WorldBounds); }
            return any ? b : new Bounds(new Vector3(3f, 0f, 2f), new Vector3(10f, 3f, 8f));
        }
    }
    public Vector3 SongCenter { get { var c = SongBounds.center; c.y = 0f; return c; } }
    /// <summary>v7: SongBounds at the heights the islands and Moons stand on (the camera's "frame all" includes the grounds; the layout's free-spot
    /// tests keep SongBounds at sea level).</summary>
    public Bounds FrameSongBounds
    {
        get
        {
            var b = SongBounds;
            foreach (var kb in Islands) if (kb != null) b.Encapsulate(kb.GroundedBounds);
            foreach (var kb in Moons) if (kb != null) b.Encapsulate(kb.GroundedBounds);
            return b;
        }
    }
    /// <summary>Re-fits the ambient effects to the world (after moves and adds).</summary>
    public void RefreshBounds() { if (HasSong) Fx.I.SetSongBounds(SongBounds); }

    // ------------------------------------------------------------------ fill crash (SPEC §3.6): once per pass on the lit column's last bar
    void Update()
    {
        if (!GlobalClock.IsPlaying || !HasSong || MeasureStarts.Count != Islands.Count || colFirst.Count == 0) return;
        int col = Mathf.Clamp(LitColumn, 0, colFirst.Count - 1);
        bool fill = false;
        for (int k = colFirst[col]; k < colFirst[col] + colCount[col]; k++) if (Islands[k] != null && Islands[k].fill) { fill = true; break; }
        if (!fill) return;
        int lit = colFirst[col];
        var kb = Islands[lit];
        if (kb == null) return;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        double local = GlobalClock.SongBeatD - colStart[col];
        if (local < colLen[col] - bpb) return;                            // v5: the last bar of the column's LAST pass (the crash leads into the next column)
        int pass = GlobalClock.LoopIndex * 64 + lit;
        if (pass == lastCrashPass) return;
        if (!GlobalClock.LoopSong && col == colFirst.Count - 1) return;   // the song ends on that downbeat: no crash into the silence
        if (Moons.Count > 0 && !AnyMoonPlaysAt(colStart[col] + colLen[col] - 0.05f) && !AnyMoonPlaysAt(colStart[col] + colLen[col] + 0.05f)) return;   // v6 §11: no crash in a part without percussion
        double dsp = GlobalClock.DspTimeOfBeat(colStart[col] + colLen[col]);
        if (dsp - GlobalClock.DspNow > 0.22) return;
        if (dsp < GlobalClock.DspNow) return;                             // the downbeat already passed inside this frame
        VoiceRules.ScheduleFillCrash(dsp);                                // one crash per downbeat time (rebuilds and seeks re-arm the pass)
        lastCrashPass = pass;
    }

    // ------------------------------------------------------------------ structural edits (all go through SongState)
    int rebuildPin = -1; int[] lastNormalizeMap;
    readonly Dictionary<int, Vector3> shownAt = new Dictionary<int, Vector3>();
    /// <summary>The next seek-keeping rebuild slides <paramref name="kb"/> in from <paramref name="root"/> (where a drag left it on screen) rather than
    /// from its logical position (IslandDrag puts the logical positions back before the op).</summary>
    public void ShownAt(KeyBlock kb, Vector3 root) { int i = Islands.IndexOf(kb); if (i >= 0) shownAt[i] = new Vector3(root.x, 0f, root.z); }
    /// <summary>Forgets the ShownAt positions (an op that was refused: nothing was rebuilt).</summary>
    public void ClearShownAt() { shownAt.Clear(); }

    /// <summary>
    /// v5 free drag preview (SPEC v5 §2.6): the root z every island of column <paramref name="col"/> would take if island <paramref name="island"/>
    /// joined it at root z <paramref name="pz"/> — the commit's resolution (the joining island keeps its z; the column's others make room, LaneGap
    /// clear) — into <paramref name="into"/> (cleared first). Nothing moves.
    /// </summary>
    public void JoinPreviewZ(int island, int col, float pz, Dictionary<KeyBlock, float> into)
    {
        into.Clear();
        if (!ValidIsland(island) || col < 0 || col >= colFirst.Count) return;
        var who = new List<KeyBlock>(ProjectConfig.MaxLanes + 1); var sub = new List<MeasureState>(ProjectConfig.MaxLanes + 1);
        for (int k = colFirst[col]; k < colFirst[col] + colCount[col]; k++)
        {
            if (k == island || Islands[k] == null) continue;
            var m = MeasureState.From(Islands[k]); m.col = 0; m.group = 0;
            sub.Add(m); who.Add(Islands[k]);
        }
        var j = MeasureState.From(Islands[island]); j.col = 0; j.group = 0; j.pz = pz;
        sub.Add(j);
        var arr = sub.ToArray();
        ResolveZ(arr, arr.Length - 1, null);
        for (int i = 0; i < who.Count; i++) into[who[i]] = arr[i].pz;
    }

    public void RebuildFromState(SongState st)
    {
        if (st == null || st.measures == null || st.measures.Length == 0) return;
        shownAt.Clear();   // (RebuildKeepingLit read them already; any other rebuild must not slide from a stale drop)
        if (islandMove != null) islandMove = null;          // a rebuild ends a live in-column move (undo mid-drag, a load)
        var secBefore = new List<int>(SectionStarts());      // v7: OnSectionsChanged when the rebuild changed them
        bool sameSong = currentSongData != null && st.name == currentSongData.songName && st.demoSeed == demoSeed && Islands.Count > 0;   // v7: an undo / an edit, not a load
        // SPEC v4 §3 K1: every structural edit, undo/redo and load normalises the columns (legacy files become a row of one-island columns)
        lastNormalizeMap = NormalizeColumns(st, rebuildPin);
        rebuildPin = -1;
        RemapMoonColumns(st);   // v6 §11: a structural edit keeps every Moon at the same part of the song
        // SPEC v5 §2.1: the song keeps its key (a file without one derives it from its first island once; it is saved from then on)
        if (st.keyTonic >= 0 && st.keyTonic < 12) SongKey = new MusicTheory.SongKey(st.keyTonic, st.keyMinor);
        else { var k0 = st.measures[0]; SongKey = MusicTheory.KeyOf(k0.root, k0.semis != null && k0.semis.Length > 0 ? k0.semis : new[] { 0, 4, 7 }); }
        HasSongKey = true;
        bool wasPlaying = GlobalClock.IsPlaying;
        if (PathManager.I != null) PathManager.I.ClearAllPaths(false);
        currentSongData = st.ToSongData();
        currentSongData.demoSeed = st.demoSeed;
        currentSongData.moons = st.moons == null ? null : st.moons.Where(m => m != null).Select((m, i) => { var d = m.ToData(i); d.kind = 1; d.bars = Mathf.Clamp(d.bars, 1, MaxMoonBars); return d; }).ToArray();   // v9 (G): a Moon keeps its groove's bars
        beatsPerMeasure = st.beatsPerBar;
        GlobalClock.SetBeatsPerBar(st.beatsPerBar);
        Performance.SetNominalBpm(st.bpm);             // while the half-time punch-in is held the clock gets the halved value; its release restores st.bpm
        GlobalClock.SetSwing(st.swing);
        GlobalClock.LoopSong = st.loop;
        Transpose = Mathf.Clamp(st.transpose, -12, 12);
        Climate = Mathf.Clamp(st.climate, 0, 3); Tone = st.tone <= 0f ? 1f : Mathf.Clamp(st.tone, 0.15f, 1f); Space = Mathf.Clamp(st.space, 0f, 1.6f); demoSeed = st.demoSeed;
        Synth.SetMasterCutoff01(Tone);
        Fx.SetClimateTint(Climate);
        lastCrashPass = -1;
        // v6: the rebuilt islands start at the tower lift the old ones showed (an undo / a redo of the same islands glides like a seek; a
        // structural edit maps them exactly in RebuildKeepingLit)
        int oldN = Islands.Count; var oldLift = new float[oldN]; var oldCol = new int[oldN];
        for (int i = 0; i < oldN; i++) { oldLift[i] = Islands[i] != null ? Islands[i].LiftY : 0f; oldCol[i] = Islands[i] != null ? Islands[i].column : -1; }
        // v7: the grounds shown per column (and the Moons'), carried when the rebuilt song is the same song (an undo, an edit)
        float[] oldMoonGround; var oldGroundByCol = GroundsShown(out oldMoonGround);
        BuildIslands();
        RestoreSections(st);   // v7 (SPEC v7 §13.1): the sections as the snapshot says (validated; none = automatic)
        if (Islands.Count == oldN) for (int i = 0; i < oldN; i++) if (Islands[i] != null && Islands[i].column == oldCol[i]) Islands[i].CarryLift(oldLift[i]);
        CarryGrounds(sameSong, oldGroundByCol, oldMoonGround);   // v7: an undo / an edit glides to the new heights; a load (another song) snaps
        RecomputeMeasureStarts();
        Instruments.Restore(st.instVolume, st.instMuted);
        if (st.cubes != null && PathManager.I != null) foreach (var cs in st.cubes) PathManager.I.RestoreCube(cs);
        RecomputeMeasureStarts();
        EnsureRouteAndComet();
        RefreshGroupLook();
        Route.Rebuild();
        LayoutMoons(true);   // v6 §11.3 (v4: ClearMoons pushed them clear of the islands)
        IslandTray.SetDeck(DeckOf(st));
        OnSongRebuilt?.Invoke();
        RaiseColumnsChanged();
        if (!SameList(secBefore, SectionStarts())) RaiseSectionsChanged();
        Fx.I.SetSongBounds(SongBounds);
        // re-seek from the DSP clock as late as possible: the beat read at the top of a 10-40 ms rebuild would rewind the song by that much,
        // and the re-created cubes would re-fire the step the destroyed ones already played; ResumeAll keeps them from re-issuing it
        double beat = wasPlaying ? GlobalClock.BeatNow : GlobalClock.SongBeatD;
        bool rewound = beat >= TotalBeats;
        if (rewound) beat = 0;
        GlobalClock.Seek(beat);
        if (wasPlaying) { AudioCube.ResumeAll(GlobalClock.DspNow + (rewound ? 0.0 : 0.03)); if (!deferSchedule) AudioCube.ScheduleAllNow(); }   // v5: queued now, not in the heavy first frame
        if (wasPlaying && !GlobalClock.IsPlaying) GlobalClock.Play();
    }

    static MeasureData[] DeckOf(SongState st)
    {
        if (st == null || st.deck == null) return null;
        var list = new List<MeasureData>(st.deck.Length);
        for (int i = 0; i < st.deck.Length; i++) if (st.deck[i] != null && st.deck[i].semis != null) list.Add(st.deck[i].ToData(i));
        return list.ToArray();
    }

    /// <summary>
    /// RebuildFromState keeping the lit column's local beat while playing (SPEC §2.3 rule, v2 MoveMeasure): <paramref name="map"/>[old index] =
    /// index in <paramref name="st"/>.measures (-1 = removed; null = identity), composed with the normalisation's own reorder. When the lit column
    /// survives (through any of its islands) the song re-seeks to its new start + the same local beat and the re-created cubes skip the steps the
    /// destroyed ones already played. <paramref name="pin"/> (an index in st.measures) keeps its z while the columns are resolved. Islands whose
    /// place changed slide there (0.32 s) instead of jumping. v5: the lit column keeps its pass and its beat inside the pass (clamped to the passes
    /// it has now); an island of the lit column that did not move (<paramref name="movers"/>) is preferred as the reference; the rebuilt islands
    /// start where the old ones were shown on their belts (the ride and the belt's length glide to the new timeline).
    /// </summary>
    bool deferSchedule;   // v5: RebuildKeepingLit queues the re-created cubes itself, after its re-seek
    internal int[] RebuildKeepingLit(SongState st, int[] map, int pin = -1, ICollection<int> movers = null)
    {
        bool wasPlaying = GlobalClock.IsPlaying;
        int nOld = Islands.Count;
        int litCol = colFirst.Count > 0 ? Mathf.Clamp(ActiveColumn(GlobalClock.SongBeat), 0, colFirst.Count - 1) : -1;
        double beat0 = GlobalClock.SongBeatD;
        double local = litCol >= 0 ? beat0 - ColumnStart(litCol) : 0.0;
        float oldPass = litCol >= 0 ? PassLength(litCol) : 0f;
        var oldPos = new Vector3[nOld];
        var oldRide = new float[nOld]; var oldBelt = new float[nOld]; var oldLift = new float[nOld]; var oldGround = new float[nOld];
        for (int i = 0; i < nOld; i++)
        {
            Vector3 sp; oldPos[i] = shownAt.TryGetValue(i, out sp) ? sp : (Islands[i] != null ? new Vector3(Islands[i].px, 0f, Islands[i].pz) : Vector3.zero);
            oldRide[i] = Islands[i] != null ? Islands[i].RideSlotShown : 0f;
            oldBelt[i] = Islands[i] != null ? Islands[i].BeltSlotsShown : 1f;
            oldLift[i] = Islands[i] != null ? Islands[i].LiftY : 0f;
            oldGround[i] = Islands[i] != null ? Islands[i].GroundY : 0f;
        }
        shownAt.Clear();
        var litIslands = new List<int>();
        if (litCol >= 0) for (int k = colFirst[litCol]; k < colFirst[litCol] + colCount[litCol]; k++) litIslands.Add(k);
        rebuildPin = pin;
        PrepareMoonRemap(map, movers);   // v6 §11: the Moons' start columns follow their columns through inserts / removals / reorders
        deferSchedule = true;   // v5: the cubes are queued once, after the re-seek below (queuing them twice would double notes)
        try { RebuildFromState(st); } finally { deferSchedule = false; moonRemap = null; }
        var nm = lastNormalizeMap;
        Func<int, int> final = old =>
        {
            int m = map == null ? old : (old >= 0 && old < map.Length ? map[old] : -1);
            if (m < 0) return -1;
            return nm != null ? (m < nm.Length ? nm[m] : -1) : m;
        };
        var result = new int[nOld];
        for (int i = 0; i < nOld; i++) result[i] = final(i);
        // islands that moved glide to their new place (a column inserted / removed / reordered, a merge's glue, a belt that grew)
        for (int i = 0; i < nOld; i++)
        {
            int f = result[i];
            if (f < 0 || f >= Islands.Count || Islands[f] == null) continue;
            Vector3 d = oldPos[i] - new Vector3(Islands[f].px, 0f, Islands[f].pz);
            if (d.sqrMagnitude > 1e-4f && d.sqrMagnitude < 1600f) Islands[f].SlideFrom(d, 0.32f, false);
            Islands[f].CarryRide(oldRide[i], oldBelt[i]);
            Islands[f].CarryLift(oldLift[i]);   // v6: the tower glides from where it was shown
            Islands[f].CarryGround(oldGround[i]);   // v7: the ground glides from where it was shown (a stair added / removed, a column moved)
        }
        if (!wasPlaying || litCol < 0) { AudioCube.ScheduleAllNow(); return result; }
        int keep = -1;
        foreach (int k in litIslands) { if (movers != null && movers.Contains(k)) continue; int f = result[k]; if (f >= 0 && f < MeasureStarts.Count) { keep = f; break; } }
        if (keep < 0) foreach (int k in litIslands) { int f = result[k]; if (f >= 0 && f < MeasureStarts.Count) { keep = f; break; } }
        if (keep < 0) { AudioCube.ScheduleAllNow(); return result; }
        double target = MeasureStarts[keep] + Math.Max(0.0, KeepLocal(local, oldPass, Islands[keep].column));
        double ahead = Math.Max(0.0, GlobalClock.BeatNow - beat0) / GlobalClock.BeatsPerSecond;   // the destroyed cubes already played the steps in this span
        if (Math.Abs(target - GlobalClock.SongBeatD) > 1e-4) GlobalClock.Seek(target);
        AudioCube.ResumeAll(GlobalClock.DspNow + ahead + 0.03);
        AudioCube.ScheduleAllNow();
        return result;
    }

    /// <summary>The local beat in column <paramref name="col"/> that continues a column played at <paramref name="local"/> with passes of
    /// <paramref name="oldPass"/> beats: the same pass (clamped to the passes the column has now) and the same beat inside the pass.</summary>
    double KeepLocal(double local, float oldPass, int col)
    {
        float pl = PassLength(col); int passes = ColumnPasses(col);
        if (pl <= 0f || oldPass <= 0f || local < 0.0) return local;
        int p = (int)Math.Floor(local / oldPass + 1e-9);
        double inPass = local - p * (double)oldPass;
        if (inPass >= pl) inPass %= pl;
        return Mathf.Clamp(p, 0, Mathf.Max(0, passes - 1)) * (double)pl + inPass;
    }

    /// <summary>A new island after <paramref name="after"/>'s column (the v2 "+"): the next chord, a new column right after the glued run
    /// <paramref name="after"/> belongs to, in its lane (its z). Seek-keeping rebuild, History.Push, rises from the sea. Returns its index.</summary>
    public int AddMeasure(int after)
    {
        if (!HasSong) return -1;
        after = Mathf.Clamp(after, 0, Islands.Count - 1);
        var st = SongState.Capture();
        var from = st.measures[after].ToData(after);
        if (from.kind >= 2) { var ch = ChordOfColumn(Islands[after].column); from = ch != null ? ch.ToData() : MusicTheory.Diatonic(MusicTheory.KeyOfSong(), 0); }   // v6 / v7: a keyboard, stairs or phrase holds no chord
        var nd = MusicTheory.NextChord(from);
        var ms = MeasureState.From(nd); ms.group = 0; ms.kind = 0; ms.reg = 0;
        ms.pz = st.measures[after].pz; ms.placed = true;
        int at = PlaceStateAsColumn(st, ms, GlueRunEnd(ColumnOf(after)) + 1, null);
        History.Push();
        if (at >= 0 && at < Islands.Count) { Islands[at].RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        return at;
    }

    /// <summary>v2 duplicate = v4 DuplicateIsland(at, 0): a copy in a new column right after the glued run of <paramref name="at"/>'s column.</summary>
    public int DuplicateMeasure(int at) { return DuplicateIsland(at, 0); }

    /// <summary>v4 (column-aware): removes island <paramref name="at"/> and its cubes (its body sinks); an emptied column disappears and the
    /// later columns slide left; the next island of its column becomes the anchor. Seek-keeping rebuild, History.Push. The last island stays.</summary>
    public void RemoveMeasure(int at)
    {
        var st = SongState.Capture();
        if (st.measures.Length <= 1) return;
        EnsureSections(st);
        int n = st.measures.Length;
        at = Mathf.Clamp(at, 0, n - 1);
        int col = st.measures[at].col;
        bool alone = true; for (int i = 0; i < n; i++) if (i != at && st.measures[i].col == col) { alone = false; break; }
        if (at < Islands.Count) SinkHusk(Islands[at]);
        var list = st.measures.ToList();
        list.RemoveAt(at);
        if (alone) foreach (var m in list) if (m.col > col) m.col--;
        var cubes = new List<CubeState>();
        foreach (var c in st.cubes) { if (c.moon < 0) { if (c.measure == at) continue; if (c.measure > at) c.measure--; } cubes.Add(c); }
        st.measures = list.ToArray(); st.cubes = cubes.ToArray();
        if (alone) ShiftSections(st, col, -1);   // v7: an emptied column leaves its section (an emptied section disappears)
        var map = new int[n];
        for (int i = 0; i < n; i++) map[i] = i < at ? i : (i == at ? -1 : i - 1);
        RebuildKeepingLit(st, map);
        History.Push();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
    }

    /// <summary>The removed island's body sinks into the sea over 0.4 s while the song is rebuilt without it.</summary>
    void SinkHusk(KeyBlock kb)
    {
        if (kb == null) return;
        kb.transform.SetParent(null, true);
        kb.enabled = false;
        foreach (var c in kb.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var t in kb.GetComponentsInChildren<TileInteraction>()) t.enabled = false;
        kb.gameObject.AddComponent<SinkingHusk>();
        Fx.SeaRipple(kb.Center, kb.chordColor, 3f);
    }

    // ------------------------------------------------------------------ v3 contract (SPEC v3 §3.2, §4.3)
    /// <summary>Island fall pattern for the presentation: 0 auto, 1 wave, 2 rows, 3 spiral, 4 rain, 5 bloom. Visual only (no rebuild); pushes History.</summary>
    public void SetFall(int at, int fall)
    {
        if (at < 0 || at >= Islands.Count || Islands[at] == null) return;
        fall = Mathf.Clamp(fall, 0, 5);
        if (Islands[at].fall == fall) return;
        Islands[at].fall = fall;
        if (currentSongData != null && currentSongData.measures != null && at < currentSongData.measures.Length && currentSongData.measures[at] != null) currentSongData.measures[at].fall = fall;
        History.Push();
    }

    /// <summary>v4: bars apply to the whole column (all islands of a column share one length).</summary>
    public void SetBars(int at, int bars)
    {
        var st = SongState.Capture();
        if (at < 0 || at >= st.measures.Length) return;
        bars = Mathf.Clamp(bars, 1, 4);
        int col = st.measures[at].col;
        bool changed = false;
        var me = st.measures[at];
        if (me.kind == 0)
        {
            // v8: a chord grid's OWN length (its column follows the longest grid; the column's other grids keep theirs); never past measure 4
            bars = Mathf.Min(bars, 4 - Mathf.Clamp(me.barOffset, 0, 3));
            if (me.bars != bars) { me.bars = bars; changed = true; }
        }
        else foreach (var m in st.measures) if (m.col == col && m.bars != bars) { m.bars = bars; changed = true; }
        if (!changed) return;
        SongOps.RefreshRunners(st);   // v7: a stairs runner stays exactly one pass long (O)
        RebuildFromState(st);
        History.Push();
    }

    public void SetChord(int at, int rootMidi, int[] semis)
    {
        var st = SongState.Capture();
        if (at < 0 || at >= st.measures.Length || st.measures[at].kind >= 2) return;   // v6 / v7: keyboards, stairs and phrases hold no chord (the wheel skips them)
        st.measures[at].root = rootMidi;
        st.measures[at].semis = (int[])semis.Clone();
        st.measures[at].chordKey = MusicTheory.ChordName(rootMidi, semis);
        st.measures[at].mood = 0;   // a wheel edit clears the island's mood view (§3.5)
        rebuildPin = at;            // a chord with more or fewer tones changes the island's depth: its neighbours make room, it stays
        RebuildFromState(st);
        History.Push();
        if (at < Islands.Count) Fx.IslandCelebrate(Islands[at]);
    }

    // ------------------------------------------------------------------ v2 world edits (SPEC §2.2, §2.3, §3.6)
    /// <summary>
    /// Song-order surgery, v4: moves island <paramref name="from"/>'s COLUMN to column position <paramref name="to"/> (an index in the column list
    /// after removal; a song of one-island columns = the v2 island semantics), remaps every cube's measure, rebuilds, re-seeks so the lit column
    /// keeps its local beat while playing, and pushes History (MoveColumn).
    /// </summary>
    public void MoveMeasure(int from, int to)
    {
        if (!HasSong || from < 0 || from >= Islands.Count) return;
        MoveColumn(ColumnOf(from), Mathf.Clamp(to, 0, Mathf.Max(0, ColumnCount - 1)));
    }

    /// <summary>v3: moves the columns of islands [<paramref name="from"/>, from + <paramref name="count"/>) (a dragged group) as one block to column
    /// position <paramref name="to"/> of the column list after removal (MoveColumn semantics; one History entry); the block keeps its glue.</summary>
    public void MoveBlock(int from, int count, int to)
    {
        if (!HasSong || count < 1 || from < 0 || from + count > Islands.Count) return;
        int c0 = ColumnOf(from), c1 = ColumnOf(from + count - 1);
        if (c0 < 0 || c1 < c0) return;
        if (MoveColumns(c0, c1 - c0 + 1, to, false)) History.Push();
    }

    /// <summary>Places island <paramref name="index"/> at (px, 0, pz). Positions are not time: nothing is rebuilt and the
    /// timeline is untouched; the island's cubes follow (they rest on tile.Top) and the cable refits. The caller pushes History on release.
    /// v4: a raw setter (drags, previews, tests); the next structural edit lays the columns out again.</summary>
    public void MoveIsland(int index, float px, float pz)
    {
        if (index < 0 || index >= Islands.Count || Islands[index] == null) return;
        MoveIsland(Islands[index], px, pz);
    }

    /// <summary>Same for any island or Moon.</summary>
    public void MoveIsland(KeyBlock kb, float px, float pz)
    {
        if (kb == null) return;
        kb.SetPosition(px, pz);
        kb.placed = true;
        foreach (var c in SequenceMaster.Cubes) if (c != null && (c.Island == kb || c.Moon == kb)) c.RefreshLine();
        if (!kb.IsMoon && Route != null) { int i = Islands.IndexOf(kb); if (i >= 0) Route.Refit(i); }
    }

    /// <summary>Adds a Moon (round drum island) rising from the sea; returns its index or -1 (past MaxMoons it bounces). v6 §11.4: it starts at
    /// column 0 (older callers and tests); see <see cref="AddMoon(int)"/>.</summary>
    public int AddMoon() { return AddMoon(0); }

    /// <summary>v6 §11.4: adds a Moon whose section starts at column <paramref name="startColumn"/> (clamped; the tray's Moon card and the N key pass
    /// the focused column): it rises in line with that column in the drum lane (a Moon already there stacks it in front); the sections and the
    /// windows follow. One rebuild, one History entry. Returns its index or -1 (no song / MaxMoons: the last Moon bounces).</summary>
    public int AddMoon(int startColumn)
    {
        if (!HasSong) return -1;
        if (Moons.Count >= MaxMoons)
        {
            var last = Moons[Moons.Count - 1];
            if (last != null) { last.Pulse(1f); Fx.SeaRipple(last.Center, last.chordColor, 2f); }
            AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 0.7f);
            return -1;
        }
        var st = SongState.Capture();
        var moons = st.moons != null ? st.moons.ToList() : new List<MeasureState>();
        int col = Mathf.Clamp(startColumn, 0, Mathf.Max(0, ColumnCount - 1));
        moons.Add(new MeasureState
        {
            chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1, col = col,
            px = ColumnCenterX(col) - KeyBlock.CenterOffsetOf(ProjectConfig.MoonCols, ProjectConfig.MoonRows).x, pz = SongBounds.max.z   // LayoutMoons pulls it into the drum lane in front of the islands
        });
        st.moons = moons.ToArray();
        RebuildFromState(st);
        History.Push();
        int idx = Moons.Count - 1;
        if (idx >= 0 && Moons[idx] != null) { Moons[idx].RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        return idx;
    }

    /// <summary>Removes Moon <paramref name="i"/> and its cubes (sinks first), then rebuilds.</summary>
    public void RemoveMoon(int i)
    {
        var st = SongState.Capture();
        if (st.moons == null || i < 0 || i >= st.moons.Length) return;
        if (i < Moons.Count) SinkHusk(Moons[i]);
        var moons = st.moons.ToList(); moons.RemoveAt(i);
        var cubes = new List<CubeState>();
        foreach (var c in st.cubes) { if (c.moon == i) continue; if (c.moon > i) c.moon--; cubes.Add(c); }
        st.moons = moons.ToArray(); st.cubes = cubes.ToArray();
        for (int k = i; k < MaxMoons - 1; k++) moonRolls[k] = moonRolls[k + 1];
        RebuildFromState(st);
        History.Push();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
    }

    /// <summary>
    /// Groove dice (§3.4): the next Rhythm.Templates groove (walked from Hash(demoSeed, moon)) is written onto the Moon's
    /// cubes row by row (kick, snare, hat, perc): hits/rot/step through the AudioCube setters, a muted row for hits 0, a new
    /// one-tile cube for a row that has none. The caller pushes History.
    /// </summary>
    public void RollMoonGroove(int moonIndex)
    {
        if (moonIndex < 0 || moonIndex >= Moons.Count || Moons[moonIndex] == null || PathManager.I == null) return;
        var moon = Moons[moonIndex];
        var tpls = Rhythm.Templates; int n = tpls.Length;
        int roll = moonRolls[moonIndex]++;
        var tpl = tpls[((int)(Rhythm.Hash(demoSeed, moonIndex, 7) % (uint)n) + 1 + roll) % n];
        float scale = GlobalClock.BeatsPerBar / 4f;
        int drums = Instruments.Count - 1; for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) { drums = i; break; }
        for (int row = 0; row < 4 && row < moon.rows; row++)
        {
            AudioCube cube = null;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Moon == moon && c.nodes.Count > 0 && c.nodes[0] != null && c.nodes[0].gridZ == row) { cube = c; break; }
            int hits = tpl.hits[row];
            if (hits <= 0) { if (cube != null) cube.SetMuted(true); continue; }
            hits = Mathf.Max(1, Mathf.RoundToInt(hits * scale));
            if (cube == null)
            {
                cube = PathManager.I.RestoreCube(Cube(drums, 0, moonIndex, new[] { row == 2 ? 2 : 3 }, new[] { row }, tpl.step[row], hits, tpl.rot[row]));
                continue;
            }
            cube.SetMuted(false);
            cube.SetStep((StepLen)Mathf.Clamp(tpl.step[row], 0, 4));
            cube.SetHits(hits);
            cube.SetRot(tpl.rot[row]);
        }
        RecomputeMeasureStarts();
        moon.Pulse(0.9f);
        Fx.SeaRipple(moon.Center, moon.chordColor, 2.5f);
        AudioPool.UI(ProceduralAudio.Chime(), 0.3f, 0.9f);
    }

    /// <summary>A free island centre near <paramref name="near"/>, preferring direction <paramref name="dir"/>: the footprint
    /// is the largest island's WorldBounds size.</summary>
    public Vector3 FindFreeSpot(Vector3 near, Vector3 dir)
    {
        Vector3 size = new Vector3(9f, 3f, 7f);
        foreach (var kb in Islands) if (kb != null) size = Vector3.Max(size, kb.WorldBounds.size);
        return FindFreeSpot(near, dir, size);
    }

    public Vector3 FindFreeSpot(Vector3 near, Vector3 dir, Vector3 size) { return FindFreeSpot(near, dir, size, (KeyBlock)null); }

    /// <summary>Spiral search on the 0.5 u grid (max radius 40 u) for a spot whose <paramref name="size"/> footprint clears every
    /// island's and Moon's WorldBounds expanded by 1.5 u on each side (ignoring <paramref name="exclude"/>); the nearest ring wins,
    /// then the point closest to <paramref name="dir"/>.</summary>
    public Vector3 FindFreeSpot(Vector3 near, Vector3 dir, Vector3 size, KeyBlock exclude)
    {
        return FindFreeSpot(near, dir, size, exclude != null ? new[] { exclude } : null, null);
    }

    /// <summary>Same, ignoring every island in <paramref name="exclude"/> and also clearing the <paramref name="extra"/> footprints (islands about to be built).</summary>
    public Vector3 FindFreeSpot(Vector3 near, Vector3 dir, Vector3 size, ICollection<KeyBlock> exclude, List<Bounds> extra)
    {
        near.y = 0f; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
        dir.Normalize();
        const float step = 0.5f; const int maxR = 80;
        float ox = Mathf.Round(near.x * 2f) * 0.5f, oz = Mathf.Round(near.z * 2f) * 0.5f;
        for (int r = 0; r <= maxR; r++)
        {
            Vector3 best = near; float bestScore = float.MaxValue; bool found = false;
            int count = r == 0 ? 1 : 8 * r;
            for (int q = 0; q < count; q++)
            {
                int ix, iz;
                if (r == 0) { ix = 0; iz = 0; }
                else
                {
                    int side = q / (2 * r), off = q % (2 * r) - r;     // walk the square ring's four sides
                    if (side == 0) { ix = off; iz = r; } else if (side == 1) { ix = r; iz = -off; } else if (side == 2) { ix = -off; iz = -r; } else { ix = -r; iz = off; }
                }
                var p = new Vector3(ox + ix * step, 0f, oz + iz * step);
                if (!IsFreeSpot(p, size, exclude, extra)) continue;
                float score = r == 0 ? 0f : -Vector3.Dot((p - near).normalized, dir);
                if (score < bestScore) { bestScore = score; best = p; found = true; }
            }
            if (found) return best;
        }
        return near + dir * (maxR * step);
    }

    /// <summary>True when a <paramref name="size"/> footprint centred on <paramref name="center"/> clears every island and Moon by 1.5 u
    /// (islands in <paramref name="exclude"/> ignored; <paramref name="extra"/> footprints count as islands).</summary>
    public bool IsFreeSpot(Vector3 center, Vector3 size, ICollection<KeyBlock> exclude = null, List<Bounds> extra = null)
    {
        var b = new Bounds(center + Vector3.up * 0.6f, size);
        foreach (var kb in Islands) if (kb != null && (exclude == null || !exclude.Contains(kb))) { var o = kb.FootprintBounds; o.Expand(ProjectConfig.IslandGap * 2f); if (o.Intersects(b)) return false; }   // v5: a belt is part of its island's footprint
        foreach (var kb in Moons) if (kb != null && (exclude == null || !exclude.Contains(kb))) { var o = kb.WorldBounds; o.Expand(ProjectConfig.IslandGap * 2f); if (o.Intersects(b)) return false; }
        if (extra != null) foreach (var e in extra) { var o = e; o.Expand(ProjectConfig.IslandGap * 2f); if (o.Intersects(b)) return false; }
        return true;
    }

    /// <summary>Per-island mood override (0 as generated, 1 sun, 2 cloud, 3 storm): a reversible view, rebuilt through the snapshot.</summary>
    public void SetMood(int at, int mood)
    {
        var st = SongState.Capture();
        if (at < 0 || at >= st.measures.Length || st.measures[at].kind >= 2) return;   // v6 / v7: no mood view on a keyboard, stairs or phrase
        mood = Mathf.Clamp(mood, 0, 3);
        if (st.measures[at].mood == mood) return;
        st.measures[at].mood = mood;
        RebuildFromState(st);
        History.Push();
    }

    /// <summary>
    /// SPEC v5 §2.5 repeat = PASSES (1..4, the island header's button cycles it): the island plays n passes of its column; the column plays the
    /// max repeat of its islands (ColumnLength grows, the song gets longer), the island's conveyor belt grows / shrinks with an animation and the
    /// later columns slide to make space (or back). Seek-keeping rebuild (the lit column keeps its pass and beat), one History entry, a soft tick.
    /// </summary>
    public void SetRepeat(int at, int n)
    {
        if (!ValidIsland(at)) return;
        var st = SongState.Capture();
        n = Mathf.Clamp(n, 1, ProjectConfig.MaxRepeat);
        if (st.measures[at].repeat == n) return;
        bool longer = n > st.measures[at].repeat;
        st.measures[at].repeat = n;
        RebuildKeepingLit(st, null, at);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, longer ? 1.3f : 0.85f);
    }

    // ------------------------------------------------------------------ v6 (SPEC v6 §2.4 carry, §2.5 keyboard, §2.6 tower; package K)
    /// <summary>v6: fires after an island's carry changed (the world magic redraws its bridges and casts the spell).</summary>
    public event Action<KeyBlock> OnCarryChanged;

    /// <summary>v6 carry: island <paramref name="at"/>'s patterns also play on the next <paramref name="n"/> chords (0..MaxCarry). One rebuild keeping
    /// the lit column (the windows follow: RecomputeMeasureStarts), one History entry (like SetRepeat), then OnCarryChanged with the rebuilt island.
    /// v7 §21.3 carry = EXTEND: the grid extends over the next n grids of its lane inside its section (clamped: CarryTargets) and they join into one
    /// LONG GRID (the covered grids' z aligns to the source's: the layout); the style is always 1 (flow).</summary>
    public void SetCarry(int at, int n)
    {
        if (!ValidIsland(at)) return;
        var st = SongState.Capture();
        n = Mathf.Clamp(n, 0, ProjectConfig.MaxCarry);
        if (st.measures[at].carry == n && (n == 0 || st.measures[at].carryStyle == 1)) return;
        st.measures[at].carry = n;
        if (n > 0) st.measures[at].carryStyle = 1;   // v7 §21.3
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnCarryChanged?.Invoke(Islands[f]);
    }

    /// <summary>Un-extend (the user: "if unextend, it just cuts it up into two grids"): island <paramref name="at"/> extends over only
    /// <paramref name="n"/> grids from now on, and every grid it lets go KEEPS what the long grid played there — each of the source's paths as the
    /// flow rule placed it on that grid (AudioCube.TilesOn) becomes that grid's own cube. One rebuild, one History entry, OnCarryChanged.
    /// Raising the carry is plain <see cref="SetCarry(int, int)"/>.</summary>
    public void SplitExtend(int at, int n)
    {
        if (!ValidIsland(at)) return;
        var src = Islands[at];
        var targets = CarryTargets(at);
        n = Mathf.Clamp(n, 0, ProjectConfig.MaxCarry);
        if (n >= targets.Count) { SetCarry(at, n); return; }
        var st = SongState.Capture();
        var baked = new List<CubeState>();
        for (int k = n; k < targets.Count; k++)
        {
            var t = targets[k];
            int ti = Islands.IndexOf(t);
            if (ti < 0) continue;
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || !c.isFinalized || c.IsOnMoon || c.Island != src || c.nodes.Count == 0) continue;
                var tiles = c.TilesOn(t);
                if (tiles.Count != c.nodes.Count) continue;   // it never played there (a drum path, a rider)
                var s = CubeState.Clone(c.ToState());
                s.measure = ti; s.moon = -1; s.id = 0; s.seed = 0; s.twinOf = -1; s.echoOf = -1; s.bend = null; s.rider = false;
                s.xs = new int[tiles.Count]; s.zs = new int[tiles.Count];
                for (int i = 0; i < tiles.Count; i++) { s.xs[i] = tiles[i].gridX; s.zs[i] = tiles[i].gridZ; }
                baked.Add(s);
            }
        }
        st.measures[at].carry = n;
        if (baked.Count > 0) { var all = new List<CubeState>(st.cubes); all.AddRange(baked); st.cubes = all.ToArray(); }
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        SplitBaked += baked.Count;
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnCarryChanged?.Invoke(Islands[f]);
    }

    /// <summary>Paths kept by grids an un-extend let go (tests).</summary>
    public int SplitBaked { get; private set; }

    /// <summary>v6 carry: the islands island <paramref name="at"/>'s patterns are carried onto, in order. v7 §21.2 (the long grid): the next
    /// <c>carry</c> grids of its LANE — each the <see cref="LaneNext"/> of the one before (the next column, the same SECTION, the same family, the
    /// largest front-back overlap, mutual) — clamped at the section's end and at a column with no grid in that lane (it never wraps, never crosses a
    /// section boundary). Empty when carry is 0 and for stairs / phrases / Moons. The carry windows and the long grids use exactly these.</summary>
    public List<KeyBlock> CarryTargets(int at)
    {
        var r = new List<KeyBlock>();
        CarryTargets(at, r);
        return r;
    }

    /// <summary>v6: <see cref="CarryTargets(int)"/> into <paramref name="into"/> (cleared first; no allocation).</summary>
    public void CarryTargets(int at, List<KeyBlock> into)
    {
        into.Clear();
        if (!ValidIsland(at)) return;
        var home = Islands[at];
        if (LaneFamily(home) < 0) return;   // v7: stairs never carry; a phrase spans its own time (no per-column carry); a Moon has its section
        var t = home;
        for (int k = 1; k <= Mathf.Min(home.carry, ProjectConfig.MaxCarry); k++)
        {
            t = LaneNext(t);
            if (t == null) break;
            into.Add(t);
        }
    }

    /// <summary>v6: the island of column <paramref name="col"/> a pattern of <paramref name="home"/> is carried onto (see CarryTargets), null none.
    /// v7 §21: the grid of home's lane in that column — on the chain of <see cref="LaneNext"/> from home (the same section), null past its end.</summary>
    public KeyBlock CarryTargetIn(int col, KeyBlock home)
    {
        if (home == null || col <= home.column || col >= colFirst.Count) return null;
        var t = home;
        for (int c = home.column + 1; c <= col && t != null; c++) t = LaneNext(t);
        return t;
    }

    // ------------------------------------------------------------------ v7 §21 THE LONG GRID (package K): lanes, runs, the joined look
    /// <summary>v7 §21.2: fires after the long grids changed (a carry / extend, a rebuild, a layout that moved a grid into or out of a lane): the
    /// joined platforms are re-shaped already; H / W / U1 re-read <see cref="LongGridOf"/>.</summary>
    public event Action OnLongGridsChanged;

    /// <summary>
    /// v7 §21.2: the next grid of <paramref name="kb"/>'s LANE: in the next column — only inside kb's SECTION (never across a boundary) — the island of
    /// the same family (chord islands ↔ chord islands, keyboards ↔ keyboards; never a Moon, stairs or a phrase) with the largest front-back overlap,
    /// at least MergeOverlap of the shallower platform, when kb is that island's own best match in kb's column (mutual: a grid is in one lane).
    /// Null none. Pure (no caching): the layout's snapshot rule (<see cref="SnapshotRuns"/>) is the same.
    /// </summary>
    public KeyBlock LaneNext(KeyBlock kb)
    {
        int f = LaneFamily(kb);
        if (f < 0) return null;
        int c = kb.column;
        if (c < 0 || c + 1 >= colFirst.Count || SectionBoundaryAfter(c)) return null;
        var e = BestLaneMate(kb, c + 1, f);
        return e != null && BestLaneMate(e, c, f) == kb ? e : null;
    }

    /// <summary>v7 §21.2: the grid before <paramref name="kb"/> in its lane (the mirror of <see cref="LaneNext"/>), null none.</summary>
    public KeyBlock LanePrev(KeyBlock kb)
    {
        int f = LaneFamily(kb);
        if (f < 0) return null;
        int c = kb.column;
        if (c <= 0 || c >= colFirst.Count || SectionBoundaryAfter(c - 1)) return null;
        var w = BestLaneMate(kb, c - 1, f);
        return w != null && BestLaneMate(w, c, f) == kb ? w : null;
    }

    KeyBlock BestLaneMate(KeyBlock kb, int col, int fam)
    {
        if (col < 0 || col >= colFirst.Count) return null;
        KeyBlock best = null; float bo = 0f;
        for (int k = colFirst[col]; k < colFirst[col] + colCount[col]; k++)
        {
            var o = k >= 0 && k < Islands.Count ? Islands[k] : null;
            if (o == null || o == kb || LaneFamily(o) != fam) continue;
            float ov;
            if (!LaneMates(kb.FrontEdge, kb.Depth, o.FrontEdge, o.Depth, out ov)) continue;
            if (best == null || ov > bo + 1e-4f) { best = o; bo = ov; }
        }
        return best;
    }

    /// <summary>v7 §21: true when some carry's chain (CarryTargets now) differs from the one the windows were made with (carryBuf).</summary>
    bool LongGridsMoved()
    {
        var tmp = new List<KeyBlock>(ProjectConfig.MaxCarry);
        for (int i = 0; i < Islands.Count; i++)
        {
            var kb = Islands[i];
            int had = i < carryBuf.Count ? carryBuf[i].Count : 0;
            if (kb == null || kb.carry <= 0) { if (had > 0) return true; continue; }
            CarryTargets(i, tmp);
            if (tmp.Count != had) return true;
            for (int k = 0; k < had; k++) if (tmp[k] != carryBuf[i][k]) return true;
        }
        return false;
    }

    /// <summary>v7 §21: 0 a chord island, 1 a keyboard; -1 never in a lane's long grid (a Moon, stairs, a phrase).</summary>
    static int LaneFamily(KeyBlock kb) => kb == null || kb.IsMoon || kb.IsStairs || kb.IsPhrase ? -1 : (kb.IsKeyboard ? 1 : 0);
    static int LaneFamily(MeasureState m) => m == null ? -1 : m.kind == 0 ? 0 : m.kind == 2 ? 1 : -1;
    /// <summary>v7 §21: two platforms (front edge, depth) share a lane when they overlap front to back by at least MergeOverlap of the shallower.</summary>
    static bool LaneMates(float a0, float ad, float b0, float bd, out float ov)
    {
        ov = Mathf.Min(a0 + ad, b0 + bd) - Mathf.Max(a0, b0);
        return ad > 0f && bd > 0f && ov >= ProjectConfig.MergeOverlap * Mathf.Min(ad, bd) - 1e-4f;
    }

    /// <summary>
    /// v7 §21.2: the LONG GRID <paramref name="kb"/> belongs to — its islands in column order, the source (the west-most: the grid that extends)
    /// first, then the grids it covers (a chain of extends, A → B and B → C, is one long grid A B C). Null when kb is in none (no carry reaches it /
    /// it carries nothing that lands). The list is SongManager's own: read it, never change it. Re-derived on every timeline change
    /// (<see cref="OnLongGridsChanged"/>).
    /// </summary>
    public List<KeyBlock> LongGridOf(KeyBlock kb)
    {
        if (kb == null) return null;
        int i = Islands.IndexOf(kb);
        var r = i >= 0 && i < lgRunOf.Count ? lgRunOf[i] : null;
        return r != null && r.Contains(kb) ? r : null;
    }

    /// <summary>v7 §21.2: every long grid now (source first in each; SongManager's own lists).</summary>
    public IReadOnlyList<List<KeyBlock>> LongGrids => lgRuns;

    readonly List<List<KeyBlock>> lgRuns = new List<List<KeyBlock>>();
    readonly List<List<KeyBlock>> lgRunOf = new List<List<KeyBlock>>();
    string lgSig = "";

    /// <summary>
    /// v7 §21.2: re-derives the long grids from the carries (carryBuf: CarryTargets of every island) — union of each source with its targets —,
    /// tells every island its joined neighbours and the run's platform depth (KeyBlock.SetLongGrid: the joined look), and raises OnLongGridsChanged
    /// when the runs (their islands, in order) changed. True when they changed.
    /// </summary>
    bool RefreshLongGrids()
    {
        int n = Islands.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        Func<int, int> find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        bool any = false;
        for (int i = 0; i < n && i < carryBuf.Count; i++)
            foreach (var t in carryBuf[i])
            {
                int j = t != null ? Islands.IndexOf(t) : -1;
                if (j < 0) continue;
                int a = find(i), b = find(j);
                if (a != b) { parent[b] = a; any = true; }
            }
        lgRuns.Clear();
        lgRunOf.Clear();
        for (int i = 0; i < n; i++) lgRunOf.Add(null);
        if (any)
        {
            var size = new int[n];
            for (int i = 0; i < n; i++) size[find(i)]++;
            var byRoot = new Dictionary<int, List<KeyBlock>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                if (size[r] < 2) continue;
                List<KeyBlock> l;
                if (!byRoot.TryGetValue(r, out l)) { l = new List<KeyBlock>(4); byRoot[r] = l; lgRuns.Add(l); }
                l.Add(Islands[i]);   // Islands are column-major: each run comes out in column order
                lgRunOf[i] = l;
            }
        }
        // the joined look: each member's west / east neighbour in its run and the run's platform depth (its deepest member's: one continuous platform)
        for (int i = 0; i < n; i++)
        {
            var kb = Islands[i]; if (kb == null) continue;
            var run = lgRunOf[i];
            if (run == null) { kb.SetLongGrid(null, null, 0f); continue; }
            int k = run.IndexOf(kb);
            float d = 0f; foreach (var m in run) if (m != null) d = Mathf.Max(d, m.Depth);
            kb.SetLongGrid(k > 0 ? run[k - 1] : null, k + 1 < run.Count ? run[k + 1] : null, d);
        }
        var sb = new System.Text.StringBuilder();
        foreach (var run in lgRuns) { foreach (var kb in run) sb.Append(kb != null ? kb.GetInstanceID() : 0).Append(','); sb.Append(';'); }
        string sig = sb.ToString();
        if (sig == lgSig) return false;
        lgSig = sig;
        OnLongGridsChanged?.Invoke();
        return true;
    }

    /// <summary>v6: the chord island column <paramref name="col"/> plays over — its first chord island in the column's order; a column holding only
    /// keyboards plays over the chord before it (the previous columns, nearest first); null when no chord island comes before (Harmony.ChordOf then
    /// falls back to the key's home chord). Keyboards light its chord tones and adapt patterns to it.</summary>
    /// <summary>v9: the chord grid of column <paramref name="col"/> sounding on measure <paramref name="measure"/> (its own measures: barOffset ..
    /// barOffset + bars), the FIRST in column order when several (the anchor first, as ChordOfColumn: a whole-column keyboard keeps playing over its
    /// column's first chord, v6's rule); null when none (a stairs / keyboard at a later measure borrows it). <paramref name="pz"/> is unused for now.</summary>
    public KeyBlock ChordAt(int col, int measure, float pz)
    {
        if (col < 0 || col >= colFirst.Count) return null;
        for (int k = colFirst[col]; k < colFirst[col] + colCount[col] && k < Islands.Count; k++)
        {
            var kb = Islands[k];
            if (kb == null || kb.kind != 0) continue;
            int o0 = OffsetBars(kb), o1 = o0 + Mathf.Max(1, kb.bars);
            if (measure >= o0 && measure < o1) return kb;
        }
        return null;
    }
    /// <summary>v9: the chord a stairs run (or any island) falls INTO: the column's chord grid sounding on the measure after its end, else the next
    /// column's grid at its first measure (what the lead-in arrow points at); null when the song has no chord grid there.</summary>
    public KeyBlock ChordAfterIsland(KeyBlock kb)
    {
        if (kb == null || kb.column < 0 || kb.column >= ColumnCount) return null;
        int end = OffsetBars(kb) + Mathf.Max(1, kb.bars);
        if (end < ColumnBarsOf(kb.column)) { var here = ChordAt(kb.column, end, kb.pz); if (here != null) return here; }
        int next = kb.column + 1 < ColumnCount ? kb.column + 1 : 0;
        return ChordAt(next, 0, kb.pz) ?? ChordOfColumn(next);
    }
    public KeyBlock ChordOfColumn(int col)
    {
        if (col >= colFirst.Count) col = colFirst.Count - 1;
        for (int c = col; c >= 0; c--)
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++)
            {
                var kb = k >= 0 && k < Islands.Count ? Islands[k] : null;
                if (kb != null && !kb.IsMoon && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase) return kb;   // v7: stairs / phrases borrow the chord too
            }
        return null;
    }

    /// <summary>v6: the lowest key of a new keyboard: the song key's tonic voiced into 53..64 (MusicTheory.KeyOfSong).</summary>
    public static int KeyboardLowestKey()
    {
        var k = MusicTheory.KeyOfSong();
        return 53 + ((k.tonic - 53) % 12 + 12) % 12;
    }

    /// <summary>v6: a keyboard island's data (kind 2): the lowest key = the song key's tonic voiced into 53..64 (stored as the root: it round-trips),
    /// semitones {0} (a placeholder like a Moon's), <paramref name="bars"/> bars, unplaced. The tray's keyboard card drags it like a card
    /// (IslandGhost.Create / Place keep the kind).</summary>
    public static MeasureData KeyboardMeasure(int bars = 1)
    {
        bars = Mathf.Clamp(bars, 1, 4);
        return new MeasureData { chordKey = "Keys", chordRootMIDI = KeyboardLowestKey(), semitones = new[] { 0 }, bars = bars, measureDuration = bars * GlobalClock.BeatsPerBar, kind = 2, energy = 2, repeat = 1 };
    }

    /// <summary>
    /// v6 keyboard (SPEC v6 §3.1): adds a keyboard island (kind 2, no cubes) to the column of island <paramref name="island"/>, LaneGap above
    /// (<paramref name="above"/>, +z) or below it (the column's others make room), with the column's bars; with <paramref name="island"/> = -1 or a
    /// full column, as a new column after the last (in the last column's lane). Seek-keeping rebuild, one History entry, it rises from the sea.
    /// Returns its index (-1 without a song).
    /// </summary>
    public int AddKeyboardIsland(int island, bool above)
    {
        if (!HasSong) return -1;
        var st = SongState.Capture();
        var ms = MeasureState.From(KeyboardMeasure(1));
        ms.placed = true;
        int at;
        if (ValidIsland(island) && LaneCount(Islands[island].column) < ProjectConfig.MaxLanes)
        {
            var src = st.measures[island];
            ms.px = src.px; ms.col = src.col; ms.bars = src.bars;
            ms.pz = LanePz(src, ms, above);
            at = PlaceStateInColumn(st, ms, Islands[island].column, null);
        }
        else
        {
            int nc = ColumnCount;
            var last = nc > 0 ? AnchorOf(nc - 1) : null;
            ms.pz = last != null ? last.pz : 0f;
            ms.bars = ValidIsland(island) ? Islands[island].bars : (last != null ? last.bars : 1);
            at = PlaceStateAsColumn(st, ms, nc, null);
        }
        if (at < 0) return -1;
        History.Push();
        if (at < Islands.Count && Islands[at] != null) { Islands[at].RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        return at;
    }

    // ---- v7 §20.1 the extensible keyboard (the user: "for the piano keyboard, it should also be extensible if needed (like if a melody goes
    //      beyond an octave)"): its range is keyCount keys (13..61) from its lowest key (the stored root)
    /// <summary>v7 §20.1: a keyboard's key range changed (ExtendKeysLive / ShrinkKeysLive / SetKeyRange): the cat (C), the header (U1) follow.</summary>
    public event Action<KeyBlock> OnKeyRangeChanged;

    /// <summary>v7 §20.1: keyboard <paramref name="kb"/> grows an octave LIVE — <paramref name="dir"/> +1 above, −1 below (every key's index +12, the
    /// lowest key −12) — with no rebuild and no History (D: a draft keeps its key objects; the stroke pushes one entry): KeyBlock.ChangeKeyRange.
    /// False (the island shakes a little) at 61 keys or past the piano.</summary>
    public bool ExtendKeysLive(KeyBlock kb, int dir)
    {
        if (kb == null || !kb.IsKeyboard || Islands.IndexOf(kb) < 0) return false;
        float d0 = kb.Depth, w0 = kb.Width;
        if (!kb.ExtendKeys(dir)) { kb.Shake(0.06f); return false; }
        SyncKeyData(kb);
        if (Mathf.Abs(kb.Depth - d0) > 1e-4f || Mathf.Abs(kb.Width - w0) > 1e-4f) RelayoutLive(Islands.IndexOf(kb), 0.25f);   // v8: wider at 3 octaves, an organ's tier past it: the layout makes room
        OnKeyRangeChanged?.Invoke(kb);
        return true;
    }

    /// <summary>v7 §20.1: keyboard <paramref name="kb"/> gives back an octave LIVE (<paramref name="dir"/> +1 the top one, −1 the bottom one: every key's
    /// index −12, the lowest key +12) — no rebuild, no History. False at 13 keys or when any cube (a draft included) plays a key of that octave.</summary>
    public bool ShrinkKeysLive(KeyBlock kb, int dir)
    {
        if (kb == null || !kb.IsKeyboard || Islands.IndexOf(kb) < 0) return false;
        float d0 = kb.Depth, w0 = kb.Width;
        if (!kb.ShrinkKeys(dir)) return false;
        SyncKeyData(kb);
        if (Mathf.Abs(kb.Depth - d0) > 1e-4f || Mathf.Abs(kb.Width - w0) > 1e-4f) RelayoutLive(Islands.IndexOf(kb), 0.25f);
        OnKeyRangeChanged?.Invoke(kb);
        return true;
    }

    /// <summary>v7: the song data of a keyboard after a live range change (its lowest key and its keys).</summary>
    void SyncKeyData(KeyBlock kb)
    {
        int i = Islands.IndexOf(kb);
        if (i < 0 || currentSongData == null || currentSongData.measures == null || i >= currentSongData.measures.Length || currentSongData.measures[i] == null) return;
        currentSongData.measures[i].chordRootMIDI = kb.chordRootMIDI;
        currentSongData.measures[i].keyCount = kb.KeyCount;
    }

    /// <summary>
    /// v7 §20.1: keyboard island <paramref name="at"/>'s range = <paramref name="count"/> keys (13..61) from <paramref name="lowKey"/> (kept inside the
    /// piano, 21..108). Every cube on it keeps the PITCH of its notes (its key indices remapped; a note the new range does not hold moves by octaves
    /// into it — the closest octave of the same pitch class). One rebuild keeping the lit column, one History entry, OnKeyRangeChanged.
    /// </summary>
    public void SetKeyRange(int at, int lowKey, int count)
    {
        if (!ValidIsland(at) || !Islands[at].IsKeyboard) return;
        count = Mathf.Clamp(count, ProjectConfig.KeyboardMinKeys, ProjectConfig.KeyboardMaxKeys);
        while (lowKey < 21) lowKey += 12;
        while (lowKey + count - 1 > 108) lowKey -= 12;
        var st = SongState.Capture();
        var m = st.measures[at];
        int oldLow = m.root, oldCount = m.keyCount <= 0 ? ProjectConfig.KeyboardKeys : m.keyCount;
        if (oldLow == lowKey && oldCount == count) return;
        m.root = lowKey; m.keyCount = count;
        if (st.cubes != null)
            foreach (var c in st.cubes)
            {
                if (c == null || c.moon >= 0 || c.measure != at || c.xs == null) continue;
                for (int i = 0; i < c.xs.Length; i++)
                {
                    int x = oldLow + c.xs[i] - lowKey;   // the same pitch from the new lowest key
                    while (x < 0) x += 12;
                    while (x > count - 1) x -= 12;
                    c.xs[i] = Mathf.Clamp(x, 0, count - 1);
                }
            }
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnKeyRangeChanged?.Invoke(Islands[f]);
    }

    // ---- the octave tower (SPEC v6 §3.3): pure functions of the song beat
    /// <summary>v6 tower: the lift at rest for register <paramref name="reg"/> — only a hint (TowerRestLift per octave; mirrored below 0).</summary>
    public static float TowerRestOf(int reg) => reg * ProjectConfig.TowerRestLift;
    /// <summary>v6 tower: the lift on the island's turn (TowerRise per octave up; a lowered island dips TowerDip per octave).</summary>
    public static float TowerTopOf(int reg) => reg >= 0 ? reg * ProjectConfig.TowerRise : reg * ProjectConfig.TowerDip;   // v7 §12.3: TowerRise 3.2 (60 % taller)
    /// <summary>v6 tower: the bob on the beat as a fraction of the turn's travel (TowerBob world units per octave).</summary>
    public static float TowerBobOf(int reg) => reg == 0 ? 0f : ProjectConfig.TowerBob * Mathf.Abs(reg) / Mathf.Max(0.05f, Mathf.Abs(TowerTopOf(reg) - TowerRestOf(reg)));

    /// <summary>
    /// v6 tower: the phase of a turn at local beat <paramref name="u"/> (0 = its column's start; up until <paramref name="upEnd"/> = the end of its last
    /// pass), as the fraction of the travel from rest to the top (0 rest … 1 up; the rise's small overshoot and the bob go past 1): before
    /// −TowerLead rest; rising until +TowerRiseEnd (an ease-out with a small overshoot: a tower pushing up out of the sea); up with a slight bob on
    /// every beat (<paramref name="bob"/>, a quick push then a settle); after upEnd sinking slowly (TowerSinkBeats, ease-in-out); then rest.
    /// <paramref name="state"/> 0 rest, 1 rising, 2 up, 3 sinking; <paramref name="phaseT"/> 0..1 through that phase. Pure.
    /// </summary>
    public static float TowerPhase(double u, double upEnd, float bob, out int state, out float phaseT)
    {
        state = 0; phaseT = 0f;
        double lead = ProjectConfig.TowerLead, rise = ProjectConfig.TowerRiseEnd, sink = ProjectConfig.TowerSinkBeats;
        if (u < -lead) return 0f;
        if (u < rise) { float t = (float)((u + lead) / (lead + rise)); state = 1; phaseT = t; return Ease.OutBack(t, 1.25f); }
        if (u < upEnd)
        {
            state = 2; phaseT = (float)((u - rise) / System.Math.Max(1e-6, upEnd - rise));
            float f = (float)(u - System.Math.Floor(u));
            float b = f < 0.08f ? Mathf.SmoothStep(0f, 1f, f / 0.08f) : 1f - Mathf.SmoothStep(0f, 1f, (f - 0.08f) / 0.92f);
            return 1f + bob * b;
        }
        if (u < upEnd + sink) { float t = (float)((u - upEnd) / sink); state = 3; phaseT = t; return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * t); }
        return 0f;
    }

    /// <summary>v6 tower (K implements): the island's register lift at song beat <paramref name="beat"/> in world units — a small rest hint, the
    /// rise on its turn, the slow sink after (SPEC v6 §2.6). Pure: seeks and loops land right.</summary>
    public float TowerLiftAt(KeyBlock kb, double beat) { int st; float t; return TowerLiftAt(kb, beat, out st, out t); }

    /// <summary>
    /// v6 tower (SPEC v6 §3.3): <see cref="TowerLiftAt(KeyBlock, double)"/> with the phase (<see cref="TowerPhase"/>). The island's turn = its column's
    /// start to the end of its own last pass (a phrase: its own span). Stopped (beat 0, not playing) = the rest hint. v7 §12.3 (the user: "the tower
    /// things that rise should rise like 60% taller and also only go back after the song resets (not in sync, but slightly out of sync)"): TowerRise
    /// is 3.2 per octave; after its turn a raised island STAYS UP (still, no bob) until the song resets, then sinks slowly (TowerSinkBeats) starting
    /// ResetStaggerOf beats after the loop point; a lowered island stays dipped likewise; a tower whose turn starts the loop stays up through the
    /// loop point (it plays again right away); a stop: KeyBlock sinks it in real time. <paramref name="state"/> 0 rest, 1 rising, 2 up (holding:
    /// phaseT 1), 3 sinking. A function of the beat, the loop state and the timeline only (<see cref="HoldPhase"/>).
    /// </summary>
    public float TowerLiftAt(KeyBlock kb, double beat, out int state, out float phaseT)
    {
        state = 0; phaseT = 0f;
        if (kb == null || kb.IsMoon || kb.register == 0) return 0f;
        float rest = TowerRestOf(kb.register), top = TowerTopOf(kb.register);
        if (!GlobalClock.IsPlaying && beat <= 1e-6) return rest;
        double s, e;
        if (!TurnSpan(kb, out s, out e)) return rest;
        double upEnd = e - s, lead = ProjectConfig.TowerLead;
        float bob = TowerBobOf(kb.register), f;
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        bool startsLoop = looping && Math.Abs(s - GlobalClock.LoopStartBeat) < 1e-3 && e <= GlobalClock.LoopEndBeat + 1e-3;
        if (beat >= s - lead && beat < e)
        {
            f = TowerPhase(beat - s, upEnd, bob, out state, out phaseT);
            // it held up through the loop point (its turn starts the loop): it stays up into its turn instead of rising again
            if (startsLoop && GlobalClock.LoopIndex > 0 && beat - s < ProjectConfig.TowerRiseEnd) { f = 1f; state = 2; phaseT = 0f; }
        }
        else
        {
            double u;
            int hp = HoldPhase(s, e, beat, ProjectConfig.TowerSinkBeats, ResetStaggerOf(kb), lead, true, out u);
            if (hp == 2) { f = 1f; state = 2; phaseT = 1f; }
            else if (hp == 3) { float t = (float)u; f = 0.5f + 0.5f * Mathf.Cos(Mathf.PI * t); state = 3; phaseT = t; }
            else f = 0f;
        }
        return rest + (top - rest) * f;
    }

    /// <summary>v6 tower (for the world magic, package W): the beats of island <paramref name="kb"/>'s turn in the song's timeline — the rise begins
    /// (<paramref name="riseStart"/> = its turn's start − TowerLead), it is at the top (<paramref name="upStart"/>), its last pass ends (<paramref
    /// name="sinkStart"/>: v7 §12.3 it then HOLDS until the song resets), and — v7 — the slow sink after the reset runs from the loop end + its stagger
    /// (<paramref name="restAt"/> = that + TowerSinkBeats; on the song's own timeline, i.e. the next loop pass's first beats + the loop's length).
    /// False for Moons, register 0 or no timeline. KeyBlock.TowerState / TowerPhaseT give the live phase (loops, seeks and glides included).</summary>
    public bool TowerTurn(KeyBlock kb, out double riseStart, out double upStart, out double sinkStart, out double restAt)
    {
        riseStart = upStart = sinkStart = restAt = 0.0;
        if (kb == null || kb.IsMoon || kb.register == 0) return false;
        double s, e;
        if (!TurnSpan(kb, out s, out e)) return false;
        riseStart = s - ProjectConfig.TowerLead; upStart = s + ProjectConfig.TowerRiseEnd; sinkStart = e;
        restAt = GlobalClock.LoopEndBeat + ResetStaggerOf(kb) + ProjectConfig.TowerSinkBeats;
        return true;
    }

    // ================================================================== v6 §11: Moons follow the song (package K)
    /// <summary>v6 §11.1: Moon <paramref name="moon"/>'s START column (its KeyBlock.column; -1 in older files = 0), clamped into the song; -1 for no Moon.</summary>
    public int MoonStartColumn(int moon)
    {
        if (moon < 0 || moon >= Moons.Count || Moons[moon] == null) return -1;
        return Mathf.Clamp(Moons[moon].column, 0, Mathf.Max(0, colFirst.Count - 1));
    }

    /// <summary>v6 §11.1: the column (exclusive) where Moon <paramref name="moon"/>'s section ends: the next LATER start column of any Moon, else the
    /// song's end (ColumnCount). -1 for no Moon.</summary>
    public int MoonEndColumn(int moon)
    {
        int s = MoonStartColumn(moon);
        if (s < 0) return -1;
        int e = Mathf.Max(s + 1, colFirst.Count);
        for (int k = 0; k < Moons.Count; k++) { int c = MoonStartColumn(k); if (c > s && c < e) e = c; }
        return e;
    }

    /// <summary>v6 §11.2: Moon <paramref name="moon"/>'s SECTION in song beats: its start column's start → the next later Moon start (or the song's
    /// end), every pass of its columns included. False for no Moon / no timeline.</summary>
    public bool MoonSection(int moon, out float startBeat, out float endBeat)
    {
        startBeat = 0f; endBeat = TotalBeats;
        int s = MoonStartColumn(moon);
        if (s < 0 || colStart.Count == 0) return false;
        int e = MoonEndColumn(moon);
        startBeat = colStart[Mathf.Min(s, colStart.Count - 1)];
        endBeat = e < colStart.Count ? colStart[e] : TotalBeats;
        return true;
    }

    /// <summary>v6 §11.2: the Moons whose section plays at <paramref name="beat"/> (Moons sharing a start column: all of them) into
    /// <paramref name="into"/> (cleared first).</summary>
    public void MoonsAt(float beat, List<KeyBlock> into)
    {
        into.Clear();
        for (int j = 0; j < Moons.Count; j++) { float a, b; if (MoonSection(j, out a, out b) && beat >= a && beat < b) into.Add(Moons[j]); }
    }

    /// <summary>v6 §11: true when <paramref name="moon"/>'s section plays at <paramref name="beat"/> (its bead laps, it pulses, it follows the song).</summary>
    public bool MoonPlaysAt(KeyBlock moon, double beat)
    {
        int j = moon != null ? Moons.IndexOf(moon) : -1;
        float a, b;
        return j >= 0 && MoonSection(j, out a, out b) && beat >= a && beat < b;
    }

    /// <summary>v6 §11: true when some Moon with cubes plays at <paramref name="beat"/> (the fill crash only sounds where there is percussion).</summary>
    bool AnyMoonPlaysAt(float beat)
    {
        if (beat >= TotalBeats) beat -= TotalBeats;
        for (int j = 0; j < Moons.Count; j++)
        {
            float a, b;
            if (!MoonSection(j, out a, out b) || beat < a || beat >= b) continue;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Moon == Moons[j]) return true;
        }
        return false;
    }

    /// <summary>v6 §11.3: the column whose platforms' centre x is nearest to world x <paramref name="x"/> (a dragged Moon snaps its start there).</summary>
    public int ColumnNearX(float x)
    {
        int best = -1; float bestD = float.MaxValue;
        for (int c = 0; c < colFirst.Count; c++) { float d = Mathf.Abs(ColumnCenterX(c) - x); if (d < bestD) { bestD = d; best = c; } }   // v7: column centres
        return best;
    }

    /// <summary>v6 §11.3: Moon <paramref name="moon"/>'s section starts at column <paramref name="col"/> (clamped): it rests in line with that column
    /// (its z as it is when that clears the islands), the sections and the Moon cubes' windows follow at once (live: no rebuild), one History entry.
    /// The Moon drag commits through it.</summary>
    public void SetMoonColumn(int moon, int col)
    {
        if (!HasSong || moon < 0 || moon >= Moons.Count || Moons[moon] == null || ColumnCount == 0) return;
        col = Mathf.Clamp(col, 0, ColumnCount - 1);
        var kb = Moons[moon];
        bool changed = kb.column != col;
        kb.column = col;
        if (currentSongData != null && currentSongData.moons != null && moon < currentSongData.moons.Length && currentSongData.moons[moon] != null) currentSongData.moons[moon].col = col;
        LayoutMoons(true);
        if (changed) RecomputeMeasureStarts();
        RaiseColumnsChanged();
        RefreshBounds();
        History.Push();
        if (changed) { kb.Pulse(0.9f); Fx.SeaRipple(kb.Center, kb.chordColor, 2.4f); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.2f); }
    }

    /// <summary>v6 §11.3: a Moon's ring half-width (its WorldBounds' radius).</summary>
    static float MoonReach => ProjectConfig.MoonRadius * 1.1f;

    /// <summary>
    /// v6 §11.3: every Moon rests IN LINE with its start column (its platform centred over the column's platforms: the same root x) in the drum lane
    /// in front of the islands: its z as the player left it when it clears every island of its section's columns by IslandGap, else the nearest z
    /// that does (toward the front); Moons in the way of each other (sharing a start column, or columns closer than a Moon) stack front to back,
    /// the one nearest the islands keeping its place (older first on a tie). Islands never move. Moved Moons glide there when
    /// <paramref name="animate"/>. The v4 ClearMoons push-out became this layout.
    /// </summary>
    public void LayoutMoons(bool animate)
    {
        int n = Moons.Count;
        if (n == 0 || colFirst.Count == 0) return;
        float r = MoonReach, gap = ProjectConfig.IslandGap, pitch = 2f * r + gap;
        Vector3 off = KeyBlock.CenterOffsetOf(ProjectConfig.MoonCols, ProjectConfig.MoonRows);
        float cx = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, 1).x;
        var want = new float[n]; var xs = new float[n];
        for (int j = 0; j < n; j++)
        {
            var m = Moons[j]; if (m == null) continue;
            int c0 = MoonStartColumn(j), c1 = Mathf.Max(c0 + 1, MoonEndColumn(j));
            float front = float.MaxValue;
            for (int c = c0; c < c1 && c < colFirst.Count; c++)
                for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) if (Islands[k] != null) front = Mathf.Min(front, Islands[k].FrontEdge);
            xs[j] = ColumnCenterX(c0);   // v7: the column's centre (its platforms may be several measures wide)
            // v7: inside a section the columns touch, so a neighbour column's islands lie within the Moon's reach too: clear every island it spans
            float reachW = xs[j] - r - gap, reachE = xs[j] + r + gap;
            foreach (var kb in Islands) { if (kb == null) continue; var fb = kb.FootprintBounds; if (fb.max.x > reachW && fb.min.x < reachE) front = Mathf.Min(front, kb.FrontEdge); }
            if (front == float.MaxValue) front = 0f;
            want[j] = Mathf.Min(m.Center.z, front - gap - r);
        }
        var order = Enumerable.Range(0, n).Where(j => Moons[j] != null).OrderByDescending(j => want[j]).ThenBy(j => j).ToList();
        var placed = new List<Vector2>(n);
        float snap = ProjectConfig.IslandSnap;
        Func<float, float> snapDown = zc => Mathf.Floor((zc - off.z) / snap + 1e-4f) * snap + off.z;   // the root on the 0.5 grid, never nearer the islands
        foreach (int j in order)
        {
            float z = snapDown(want[j]);
            for (int guard = 0; guard < 2 * n + 2; guard++)
            {
                bool moved = false;
                foreach (var p in placed) if (Mathf.Abs(p.x - xs[j]) < pitch && Mathf.Abs(p.y - z) < pitch - 1e-3f) { z = snapDown(p.y - pitch); moved = true; }
                if (!moved) break;
            }
            placed.Add(new Vector2(xs[j], z));
            var m = Moons[j];
            float px = xs[j] - off.x, pz = z - off.z;
            Vector3 d = new Vector3(m.px - px, 0f, m.pz - pz);
            if (d.sqrMagnitude < 1e-8f) continue;
            MoveIsland(m, px, pz);
            if (animate && d.sqrMagnitude < 2500f) m.SlideFrom(d, 0.35f, false);
        }
    }

    // ---- the start columns through structural edits: RebuildKeepingLit remembers each Moon's start column by its islands (the ones the edit
    //      does not move first, then the islands after it: a removed column hands its Moon to the column that slides into its place)
    int[][] moonRemap; int[] moonRemapMap;
    void PrepareMoonRemap(int[] map, ICollection<int> movers)
    {
        moonRemap = null; moonRemapMap = map;
        if (Moons.Count == 0 || colFirst.Count == 0) return;
        moonRemap = new int[Moons.Count][];
        for (int j = 0; j < Moons.Count; j++)
        {
            int c = MoonStartColumn(j);
            if (c < 0) continue;
            var list = new List<int>();
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) if (movers == null || !movers.Contains(k)) list.Add(k);
            for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) if (movers != null && movers.Contains(k)) list.Add(k);
            for (int k = colFirst[c] + colCount[c]; k < Islands.Count; k++) list.Add(k);
            moonRemap[j] = list.ToArray();
        }
    }

    /// <summary>Applies PrepareMoonRemap to the snapshot being rebuilt (after its normalisation: the columns are final).</summary>
    void RemapMoonColumns(SongState st)
    {
        var rm = moonRemap; moonRemap = null;
        if (rm == null || st.moons == null || st.measures == null || st.measures.Length == 0) return;
        var nm = lastNormalizeMap; var map = moonRemapMap;
        int lastCol = st.measures[st.measures.Length - 1].col;
        for (int j = 0; j < st.moons.Length && j < rm.Length; j++)
        {
            if (st.moons[j] == null || rm[j] == null) continue;
            int col = -1;
            foreach (int old in rm[j])
            {
                int m = map == null ? old : (old >= 0 && old < map.Length ? map[old] : -1);
                if (m < 0) continue;
                int f = nm != null ? (m < nm.Length ? nm[m] : -1) : m;
                if (f < 0 || f >= st.measures.Length) continue;
                col = st.measures[f].col; break;
            }
            st.moons[j].col = col >= 0 ? col : lastCol;
        }
    }

    /// <summary>v6 §11.3: how long a following Moon's step takes, in beats: ≈ 0.75 to a neighbour column, up to 2 for a long way home.</summary>
    public static float MoonStepBeats(float dx) => Mathf.Clamp(0.75f * dx / (KeyBlock.IslandWidth + ProjectConfig.ColumnGap), 0.5f, 2f);

    /// <summary>
    /// v6 §11.3: the x offset of <paramref name="moon"/> from its resting place (in line with its start column) at song beat <paramref name="beat"/>.
    /// While its section plays it stays in line with the lit column: the step to the next column glides (MoonStepBeats, eased) and ARRIVES as that
    /// column starts; at the loop point it steps to the loop's first column when that is in its section. v7 §12.4: after its section it HOLDS in line
    /// with its section's last column until the song resets, then glides back to its start column (slightly out of sync: ResetStaggerOf); a section
    /// that plays again right at the reset (it starts the loop) glides home in the last beats before the loop point. 0 when stopped (a stop: KeyBlock
    /// glides it home in real time) or dragged. Pure: seeks, loops and the focus loop land right (<see cref="HoldPhase"/>).
    /// </summary>
    public float MoonFollowX(KeyBlock moon, double beat)
    {
        if (moon == null || !moon.IsMoon || colFirst.Count == 0 || colStart.Count == 0) return 0f;
        if (!GlobalClock.IsPlaying && beat <= 1e-6) return 0f;
        int j = Moons.IndexOf(moon);
        int c0 = MoonStartColumn(j), c1 = MoonEndColumn(j);
        if (c0 < 0 || c1 <= c0) return 0f;
        c1 = Mathf.Min(c1, colStart.Count);
        float home = ColumnCenterX(c0);
        double s = colStart[c0], e = c1 < colStart.Count ? colStart[c1] : TotalBeats;
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        double ls = GlobalClock.LoopStartBeat, le = GlobalClock.LoopEndBeat;
        int loopFirst = looping ? ActiveColumn((float)ls + 1e-3f) : -1;
        bool loopInSection = loopFirst >= c0 && loopFirst < c1;
        if (beat >= s && beat < e)
        {
            int c = Mathf.Clamp(ActiveColumn((float)beat), c0, c1 - 1);
            float x = ColumnCenterX(c);
            double cEnd = colStart[c] + colLen[c];
            int next = c + 1 < c1 ? c + 1 : -1;
            if (looping && System.Math.Abs(cEnd - le) < 1e-3) next = loopInSection ? loopFirst : -1;   // the loop point: its next column is the loop's first
            if (next >= 0 && next != c)
            {
                float nx = ColumnCenterX(next);
                double dur = MoonStepBeats(Mathf.Abs(nx - x));
                if (beat > cEnd - dur) x = Mathf.Lerp(x, nx, Mathf.SmoothStep(0f, 1f, (float)((beat - (cEnd - dur)) / dur)));
            }
            return x - home;
        }
        // v7 §12.4: after its section it holds at its section's last column until the song resets, then glides home
        float last = ColumnCenterX(c1 - 1);
        double u;
        int hp = HoldPhase(s, e, beat, MoonStepBeats(Mathf.Abs(last - home)), ResetStaggerOf(moon), 0.0, false, out u);
        if (hp == 2) return last - home;
        if (hp == 3) return Mathf.Lerp(last, home, Mathf.SmoothStep(0f, 1f, (float)u)) - home;
        return 0f;
    }

    /// <summary>Energy 0..3: velocity/density scale (read by VoiceRules); no rebuild.</summary>
    public void SetEnergy(int at, int e)
    {
        if (at < 0 || at >= Islands.Count || Islands[at] == null) return;
        e = Mathf.Clamp(e, 0, 3);
        if (Islands[at].energy == e) return;
        Islands[at].energy = e;
        RecomputeMeasureStarts();
        History.Push();
    }

    /// <summary>Fill: ratchets + crash in the island's last bar (Decide/VoiceRules + the crash from Update); no rebuild.</summary>
    public void SetFill(int at, bool f)
    {
        if (at < 0 || at >= Islands.Count || Islands[at] == null || Islands[at].fill == f) return;
        Islands[at].fill = f;
        RecomputeMeasureStarts();
        History.Push();
    }

    /// <summary>Sleep (the island header's eye, SPEC v4 §3 K5 toggle): the island's windows become silent (its cubes stop sounding), length kept;
    /// no rebuild. The island reads clearly off (KeyBlock's sleep look: desaturated platform, dimmed tiles, a closed eye). Pushes History.</summary>
    public void SetSleep(int at, bool s)
    {
        if (at < 0 || at >= Islands.Count || Islands[at] == null || Islands[at].sleep == s) return;
        Islands[at].sleep = s;
        Islands[at].ApplySleepLook(s);
        RecomputeMeasureStarts();
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, s ? 0.75f : 1.25f);
        RaiseColumnsChanged();
    }

    public void SetBeatsPerBar(int n)
    {
        GlobalClock.SetBeatsPerBar(n);
        beatsPerMeasure = GlobalClock.BeatsPerBar;
        RecomputeMeasureStarts();
        History.Push();
    }

    // ================================================================== v3 islands (SPEC v3 §3): tray placement, groups, merge, split
    // ------------------------------------------------------------------ groups
    /// <summary>
    /// The v3 group invariant on a snapshot in song order, in place (kept for tools and tests; v4 normalisation validates groups by columns,
    /// see NormalizeColumns): a group is a run of consecutive measures sharing a non-zero id; a run of one is dissolved (group 0); an id already
    /// used by an earlier run is renumbered (max + 1); every run is laid out left to right from its first member (member j+1's px = member j's
    /// px + IslandWidth, same pz: platforms touch, front rows aligned). Idempotent. Returns true when anything changed.
    /// </summary>
    public static bool NormalizeGroups(MeasureState[] ms)
    {
        if (ms == null) return false;
        bool changed = false;
        int n = ms.Length, maxId = 0;
        foreach (var m in ms) if (m != null && m.group > maxId) maxId = m.group;
        var used = new HashSet<int>();
        float w = KeyBlock.IslandWidth;
        int i = 0;
        while (i < n)
        {
            var a = ms[i];
            if (a == null || a.group <= 0 || a.kind != 0)
            {
                if (a != null && a.group != 0) { a.group = 0; changed = true; }
                i++; continue;
            }
            int g = a.group, j = i;
            while (j + 1 < n && ms[j + 1] != null && ms[j + 1].kind == 0 && ms[j + 1].group == g) j++;
            if (j == i) { a.group = 0; changed = true; i++; continue; }
            if (used.Contains(g)) { g = ++maxId; for (int k = i; k <= j; k++) ms[k].group = g; changed = true; }
            used.Add(g);
            for (int k = i + 1; k <= j; k++)
            {
                float px = ms[k - 1].px + w, pz = a.pz;
                if (!ms[k].placed || Mathf.Abs(ms[k].px - px) > 1e-4f || Mathf.Abs(ms[k].pz - pz) > 1e-4f) { ms[k].px = px; ms[k].pz = pz; ms[k].placed = true; changed = true; }
            }
            i = j + 1;
        }
        return changed;
    }

    /// <summary>v4: the column invariant on the live islands (after a tear-out, a split): groups re-validated by columns, x laid out, z resolved,
    /// the islands slide to their places (RelayoutLive). True when anything changed.</summary>
    public bool NormalizeLiveGroups() { return RelayoutLive(-1, 0.3f); }

    static bool SameGroup(KeyBlock a, KeyBlock b) => a != null && b != null && !a.IsMoon && !b.IsMoon && a.group != 0 && a.group == b.group;
    /// <summary>True when islands <paramref name="i"/> and i + 1 (Islands order) belong to one group. v4: with one-island columns this is the
    /// v3 seam; in general see <see cref="AnchorSeam"/> (the Route) and <see cref="GroupNeighbour"/> (any lane).</summary>
    public bool IsSeam(int i) => i >= 0 && i + 1 < Islands.Count && SameGroup(Islands[i], Islands[i + 1]);

    /// <summary>v4: the member of <paramref name="kb"/>'s group in the next (<paramref name="dir"/> = +1) or previous (-1) column; null when none.</summary>
    public KeyBlock GroupNeighbour(KeyBlock kb, int dir)
    {
        if (kb == null || kb.IsMoon || kb.group == 0) return null;
        int c = kb.column + (dir >= 0 ? 1 : -1);
        if (c < 0 || c >= colFirst.Count) return null;
        for (int k = colFirst[c]; k < colFirst[c] + colCount[c]; k++) if (SameGroup(Islands[k], kb)) return Islands[k];
        return null;
    }

    /// <summary>The list indices of the west-most and east-most members of island <paramref name="i"/>'s group (v4: members sit in consecutive
    /// columns, not necessarily at consecutive indices); a non-member is its own range.</summary>
    public void GroupRange(int i, out int first, out int last)
    {
        first = last = i;
        if (i < 0 || i >= Islands.Count || Islands[i] == null || Islands[i].group == 0 || Islands[i].IsMoon) return;
        var g = GroupOf(Islands[i]);
        first = Islands.IndexOf(g[0]); last = Islands.IndexOf(g[g.Count - 1]);
    }

    /// <summary>The members of <paramref name="kb"/>'s group, west to east (just kb when it is alone or a Moon).</summary>
    public List<KeyBlock> GroupOf(KeyBlock kb)
    {
        var r = new List<KeyBlock>();
        if (kb == null) return r;
        if (kb.IsMoon || kb.group == 0 || Islands.IndexOf(kb) < 0) { r.Add(kb); return r; }
        foreach (var o in Islands) if (SameGroup(o, kb) || o == kb) r.Add(o);
        r.Sort((a, b) => a.column.CompareTo(b.column));
        return r;
    }

    /// <summary>World bounds of <paramref name="kb"/>'s group (the camera frames groups as one).</summary>
    public Bounds GroupBounds(KeyBlock kb)
    {
        var list = GroupOf(kb);
        if (list.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = list[0].GroundedBounds;   // v7: at the heights they stand on
        for (int k = 1; k < list.Count; k++) if (list[k] != null) b.Encapsulate(list[k].GroundedBounds);
        return b;
    }

    public int NextGroupId() { int m = 0; foreach (var kb in Islands) if (kb != null && kb.group > m) m = kb.group; return m + 1; }
    static int NextGroupId(MeasureState[] ms) { int m = 0; foreach (var s in ms) if (s != null && s.group > m) m = s.group; return m + 1; }

    /// <summary>Rebuilds the member look from the live ids: the rim outline on every member, a seam bridge toward each member's east neighbour
    /// (the member of its group in the next column); the Route drops its cable at anchor seams.</summary>
    public void RefreshGroupLook()
    {
        foreach (var kb in Islands)
        {
            if (kb == null) continue;
            var right = GroupNeighbour(kb, +1);
            bool member = right != null || GroupNeighbour(kb, -1) != null;
            kb.SetGroupLook(member, right);
        }
        foreach (var m in Moons) if (m != null) m.SetGroupLook(false, null);
        if (Route != null) Route.RefreshSeams();
    }

    // ------------------------------------------------------------------ merge zone
    /// <summary>The moving platform block for the merge probe: west / east platform edges, the common front edge, the back edges of its
    /// west-end and east-end members, and its member count.</summary>
    // ------------------------------------------------------------------ v7 (SPEC v7 §2.4–§2.8, §12; package K)
    /// <summary>v7: the grounds' targets changed (a stair added / removed / changed, columns moved, a load): terraces redraw, islands glide.</summary>
    public event Action OnGroundsChanged;
    /// <summary>v7: an island's repeat style (rewind) or vary changed.</summary>
    public event Action<KeyBlock> OnRepeatStyleChanged;
    /// <summary>v7: an island's launch changed.</summary>
    public event Action<KeyBlock> OnLaunchChanged;
    /// <summary>v7: a stairs island's run changed (type, direction, steps, rate, lead-in).</summary>
    public event Action<KeyBlock> OnStairChanged;

    /// <summary>v7 stairs (SPEC v7 §2.4; O implements it in SongOps — this forwards): adds a stairs island (kind 3) to island <paramref name="island"/>'s column (above / below it;
    /// else as a new column after the last) with ONE runner cube of <paramref name="instrument"/> (−1: the lead group) so it sounds at once; one
    /// History entry; it rises from the sea. Returns its index (−1 without a song). M0: a stub.</summary>
    public int AddStairIsland(int island, bool above, int instrument = -1) => SongOps.AddStairIsland(island, above, instrument);

    /// <summary>v7 stairs (O implements it in SongOps — this forwards): island <paramref name="at"/>'s run — <paramref name="type"/> 0..5, <paramref name="dir"/> −1 / +1 (0 keeps),
    /// <paramref name="steps"/> StairMinSteps..StairMaxSteps, <paramref name="rate"/> ticks per step (6, 8, 12, 24), <paramref name="lead"/> 1 a lead-in /
    /// 0 from the column's start (−1 keeps any of type / steps / rate / lead). One History entry; new tiles, every runner's path re-derived
    /// (StairPath), the grounds re-computed. M0: a stub.</summary>
    public void SetStair(int at, int type, int dir, int steps, int rate, int lead) => SongOps.SetStair(at, type, dir, steps, rate, lead);


    // ---- v7 sections (SPEC v7 §13; K owns after M0). M0: working queries over `sectionStarts` (K keeps it from SongState.sections), else one
    // section per SectionBars measures.
    /// <summary>v7: the sections changed (split, join, a column added / removed / moved into another section, a load).</summary>
    public event Action OnSectionsChanged;
    /// <summary>v7: the first column of every section, ascending (K fills it from SongState.sections on a build; empty = automatic).</summary>
    internal readonly List<int> sectionStarts = new List<int>();
    /// <summary>v9 (G): the song's own section letters (SongState.sectionLetters, one per section; empty = the content rule). Honoured while they
    /// match the sections one to one (<see cref="ExplicitLetter"/>); an edit that changes the section count lets the content rule decide again.</summary>
    internal readonly List<string> sectionLetters = new List<string>();
    /// <summary>v9 (G): section <paramref name="s"/>'s own letter (SongState.sectionLetters) while the song carries one per section; null = the
    /// content rule (SectionPlinth.Letters).</summary>
    public string ExplicitLetter(int s)
    {
        int n = SectionCount;
        if (sectionLetters.Count == 0 || sectionLetters.Count != n || s < 0 || s >= n) return null;
        var l = sectionLetters[s];
        return string.IsNullOrEmpty(l) ? null : l;
    }
    /// <summary>v9 (G): the section letters a snapshot saves (null when the song has none, or they no longer match its sections).</summary>
    public string[] ExplicitLetters() => sectionLetters.Count > 0 && sectionLetters.Count == SectionCount ? sectionLetters.ToArray() : null;
    readonly List<int> secScratch = new List<int>();

    /// <summary>v7: the first column of every section right now (explicit starts, else one section per SectionBars measures). Column 0 always starts one.</summary>
    public List<int> SectionStarts()
    {
        secScratch.Clear();
        int nc = ColumnCount;
        if (nc == 0) return secScratch;
        secScratch.Add(0);
        if (sectionStarts.Count > 0)
        {
            foreach (int c in sectionStarts) if (c > 0 && c < nc && c > secScratch[secScratch.Count - 1]) secScratch.Add(c);
            return secScratch;
        }
        int bars = 0;
        for (int c = 0; c < nc; c++)
        {
            if (bars >= ProjectConfig.SectionBars && c > 0) { secScratch.Add(c); bars = 0; }
            bars += ColumnBars(c);
        }
        return secScratch;
    }

    /// <summary>v7: the measures of column <paramref name="col"/> (its pass length in bars; at least 1).</summary>
    public int ColumnBars(int col) => Mathf.Max(1, Mathf.RoundToInt(PassLength(col) / Mathf.Max(1, GlobalClock.BeatsPerBar)));
    /// <summary>v7: sections in the song.</summary>
    public int SectionCount => SectionStarts().Count;
    /// <summary>v7: the section column <paramref name="col"/> belongs to (-1 outside the song).</summary>
    public int SectionOf(int col)
    {
        if (col < 0 || col >= ColumnCount) return -1;
        var st = SectionStarts();
        for (int s = st.Count - 1; s >= 0; s--) if (col >= st[s]) return s;
        return 0;
    }
    /// <summary>v7: the first column of section <paramref name="s"/>.</summary>
    public int SectionFirst(int s) { var st = SectionStarts(); return s >= 0 && s < st.Count ? st[s] : -1; }
    /// <summary>v7: the last column of section <paramref name="s"/>.</summary>
    public int SectionLast(int s) { var st = SectionStarts(); return s >= 0 && s < st.Count ? (s + 1 < st.Count ? st[s + 1] - 1 : ColumnCount - 1) : -1; }
    /// <summary>v7: the measures of section <paramref name="s"/>.</summary>
    public int SectionBars(int s) { int a = SectionFirst(s), b = SectionLast(s), n = 0; for (int c = a; c >= 0 && c <= b; c++) n += ColumnBars(c); return n; }
    /// <summary>v7: the song beat section <paramref name="s"/> starts.</summary>
    public float SectionStartBeat(int s) => ColumnStart(Mathf.Max(0, SectionFirst(s)));
    /// <summary>v7: the song beat section <paramref name="s"/> ends.</summary>
    public float SectionEndBeat(int s) { int b = SectionLast(s); return b < 0 ? 0f : ColumnStart(b) + ColumnLength(b); }
    /// <summary>v7: true when a section boundary lies between column <paramref name="col"/> and col + 1 (a gap there; inside a section the columns touch).</summary>
    public bool SectionBoundaryAfter(int col) { var st = SectionStarts(); return st.Contains(col + 1); }
    /// <summary>v7 §17.1: section <paramref name="s"/>'s name — the secRole of the first island of its first column (0 none, 1 intro, 2 verse, 3 chorus,
    /// 4 bridge, 5 drop, 6 outro); it travels with that column through every op.</summary>
    public int SectionRole(int s) { var a = AnchorOf(SectionFirst(s)); return a != null ? a.secRole : 0; }
    /// <summary>v7 (K): raise OnSectionsChanged (after K / O changed the sections).</summary>
    internal void RaiseSectionsChanged() => OnSectionsChanged?.Invoke();

    // ---- v7 sections on snapshots (SPEC v7 §13.1; K). A snapshot's st.sections = the first column of every section (Capture writes the live
    //      ones); every op that inserts / removes / reorders columns keeps them consistent with one of these helpers (O calls them too).
    /// <summary>v7: the measures of every column of a column-major snapshot (the longest non-phrase island; a column of phrases only: its phrase's).</summary>
    internal static int[] SnapshotColumnBars(MeasureState[] ms)
    {
        int n = ms != null ? ms.Length : 0, nc = n > 0 ? ms[n - 1].col + 1 : 0;
        var bars = new int[Mathf.Max(0, nc)];
        var any = new bool[bars.Length];
        for (int i = 0; i < n; i++)
        {
            var m = ms[i]; int c = m.col; if (c < 0 || c >= nc) continue;
            if (m.kind == 4) { if (!any[c]) bars[c] = Mathf.Max(bars[c], Mathf.Clamp(m.bars, 1, 4)); continue; }
            if (!any[c]) { bars[c] = 0; any[c] = true; }
            bars[c] = Mathf.Max(bars[c], Mathf.Clamp(m.bars, 1, 4));
        }
        for (int c = 0; c < bars.Length; c++) bars[c] = Mathf.Max(1, bars[c]);
        return bars;
    }

    /// <summary>v7: automatic sections: a new section every SectionBars measures (an older song without sections).</summary>
    internal static List<int> AutoSections(IList<int> bars)
    {
        var r = new List<int>();
        int nc = bars != null ? bars.Count : 0;
        if (nc == 0) return r;
        r.Add(0);
        int b = 0;
        for (int c = 0; c < nc; c++)
        {
            if (b >= ProjectConfig.SectionBars && c > 0) { r.Add(c); b = 0; }
            b += Mathf.Max(1, Mathf.Abs(bars[c]));
        }
        return r;
    }

    /// <summary>v7: <paramref name="starts"/> validated for <paramref name="nc"/> columns (0 first, ascending, unique, &lt; nc); null / empty = automatic
    /// from <paramref name="bars"/>.</summary>
    internal static List<int> ValidSections(IList<int> starts, int nc, IList<int> bars)
    {
        if (starts == null || starts.Count == 0) return bars != null ? AutoSections(bars) : new List<int> { 0 };
        var r = new List<int>();
        if (nc <= 0) return r;
        r.Add(0);
        var sorted = new List<int>(starts); sorted.Sort();
        foreach (int s in sorted) if (s > 0 && s < nc && s > r[r.Count - 1]) r.Add(s);
        return r;
    }

    static List<int> ValidSections(IList<int> starts, int nc, SnapshotGrid gr) => ValidSections(starts, nc, gr != null ? (IList<int>)gr.bars : null);

    /// <summary>v7: a snapshot's sections — st.sections validated, or the automatic ones (null in an older file).</summary>
    internal static List<int> SectionsOf(SongState st)
    {
        var bars = SnapshotColumnBars(st != null ? st.measures : null);
        return ValidSections(st != null ? st.sections : null, bars.Length, bars);
    }

    /// <summary>v7: the section index of column <paramref name="col"/> in <paramref name="starts"/>.</summary>
    static int SectionIndexIn(IList<int> starts, int col)
    {
        for (int s = starts.Count - 1; s >= 0; s--) if (col >= starts[s]) return s;
        return 0;
    }

    /// <summary>
    /// v7: the sections after an op re-laid a snapshot's columns. <paramref name="oldStarts"/> = the sections before the op; <paramref name="origin"/>
    /// [new column] = the old column it continues, −1 for a column that is NEW or MOVED there; <paramref name="bars"/>[new column] = its measures.
    /// Continuing columns keep their section (an emptied section disappears). The others join, left to right: a column strictly inside a section
    /// joins it; at a boundary / the front / the end it joins the section before while that has fewer than SectionBars measures, else the one after
    /// while that has fewer, else it starts a new section (SPEC v7 §13.1). <paramref name="mode"/> 1 = always join the section before (a column glued
    /// east of its neighbour), 2 = the one after (glued west).
    /// </summary>
    internal static List<int> RemapSections(IList<int> oldStarts, int[] origin, int[] bars, int mode = 0)
    {
        int nc = origin != null ? origin.Length : 0;
        var r = new List<int>();
        if (nc == 0) return r;
        var olds = oldStarts != null && oldStarts.Count > 0 ? oldStarts : new List<int> { 0 };
        var sid = new int[nc];
        int maxSid = -1, nextId = olds.Count;
        for (int c = 0; c < nc; c++)
        {
            sid[c] = origin[c] >= 0 ? SectionIndexIn(olds, origin[c]) : -1;
            if (sid[c] >= 0) { if (sid[c] < maxSid) sid[c] = -1; else maxSid = sid[c]; }   // a continuing column out of order counts as moved
        }
        var secBars = new Dictionary<int, int>();
        for (int c = 0; c < nc; c++) if (sid[c] >= 0) { int v; secBars.TryGetValue(sid[c], out v); secBars[sid[c]] = v + Mathf.Max(1, bars != null && c < bars.Length ? bars[c] : 1); }
        for (int c = 0; c < nc; c++)
        {
            if (sid[c] >= 0) continue;
            int prev = c > 0 ? sid[c - 1] : -1, next = -1;
            for (int k = c + 1; k < nc; k++) if (sid[k] >= 0 && origin[k] >= 0) { next = sid[k]; break; }
            int pb = 0, nb = 0;
            if (prev >= 0) secBars.TryGetValue(prev, out pb);
            if (next >= 0) secBars.TryGetValue(next, out nb);
            int id;
            if (prev >= 0 && prev == next) id = prev;
            else if (mode == 1 && prev >= 0) id = prev;
            else if (mode == 2 && next >= 0) id = next;
            else if (prev >= 0 && pb < ProjectConfig.SectionBars) id = prev;
            else if (next >= 0 && nb < ProjectConfig.SectionBars) id = next;
            else id = nextId++;
            sid[c] = id;
            int v; secBars.TryGetValue(id, out v); secBars[id] = v + Mathf.Max(1, bars != null && c < bars.Length ? bars[c] : 1);
        }
        r.Add(0);
        for (int c = 1; c < nc; c++) if (sid[c] != sid[c - 1]) r.Add(c);
        return r;
    }

    /// <summary>v7 (for O's column plans): shifts <paramref name="st"/>.sections after <paramref name="delta"/> &gt; 0 columns were inserted at
    /// <paramref name="fromCol"/> (they trail the section before them; at the front they form a new first section) or −delta columns
    /// [fromCol, fromCol − delta) were removed (an emptied section disappears). st.measures already has the new columns. Null sections stay
    /// automatic.</summary>
    internal static void ShiftSections(SongState st, int fromCol, int delta)
    {
        if (st == null || st.sections == null || delta == 0) return;
        var bars = SnapshotColumnBars(st.measures);
        var r = new List<int>();
        foreach (int s in st.sections)
        {
            if (delta > 0) r.Add(s < fromCol ? s : s + delta);
            else { int k = -delta; r.Add(s < fromCol ? s : (s < fromCol + k ? fromCol : s - k)); }
        }
        if (delta > 0 && fromCol == 0) r.Add(0);
        st.sections = ValidSections(r, bars.Length, bars).ToArray();
    }

    /// <summary>v7 (for O's and K's column plans): columns [<paramref name="at"/>, at + <paramref name="count"/>) are NEW in st.measures while
    /// st.sections still describes the old columns: they join by the landing rule (<see cref="RemapSections"/>; <paramref name="mode"/> 1 / 2 force
    /// the section before / after).</summary>
    internal static void JoinNewColumns(SongState st, int at, int count, int mode = 0)
    {
        if (st == null || st.sections == null || count <= 0) return;
        var bars = SnapshotColumnBars(st.measures);
        int nc = bars.Length;
        var origin = new int[nc];
        for (int c = 0; c < nc; c++) origin[c] = c < at ? c : (c < at + count ? -1 : c - count);
        st.sections = RemapSections(st.sections, origin, bars, mode).ToArray();
    }

    /// <summary>v7: validates st.sections and joins the sections a merge group spans (SPEC v7 §13.2: a merge across a boundary joins the sections).</summary>
    internal static void FixSections(SongState st)
    {
        if (st == null || st.sections == null || st.measures == null) return;
        var bars = SnapshotColumnBars(st.measures);
        var r = ValidSections(st.sections, bars.Length, bars);
        for (int c = 0; c + 1 < bars.Length; c++) if (GluedAt(st.measures, c)) r.Remove(c + 1);
        st.sections = r.ToArray();
    }

    /// <summary>v7: the old column of every new column when <paramref name="st"/>'s measures were re-laid (<paramref name="map"/>[old island] = new
    /// island; <paramref name="oldCols"/>[old island] = its old column; <paramref name="movers"/> = old islands that moved: a column made only of movers
    /// or new islands has no origin).</summary>
    static int[] ColumnOrigins(SongState st, int[] map, int[] oldCols, ICollection<int> movers)
    {
        var bars = SnapshotColumnBars(st.measures);
        var origin = new int[bars.Length];
        for (int c = 0; c < origin.Length; c++) origin[c] = -1;
        for (int i = 0; i < oldCols.Length; i++)
        {
            if (movers != null && movers.Contains(i)) continue;
            int f = map == null ? i : (i < map.Length ? map[i] : -1);
            if (f < 0 || f >= st.measures.Length) continue;
            int c = st.measures[f].col;
            if (c >= 0 && c < origin.Length && origin[c] < 0) origin[c] = oldCols[i];
        }
        return origin;
    }

    /// <summary>v7: the live section starts restored from a snapshot (validated; null / empty = automatic).</summary>
    void RestoreSections(SongState st)
    {
        sectionStarts.Clear();
        sectionLetters.Clear();   // v9 (G): the song's own letters come with its sections
        if (st != null && st.sectionLetters != null) foreach (var l in st.sectionLetters) sectionLetters.Add(l ?? "");
        if (st == null || st.sections == null || st.sections.Length == 0) return;
        var bars = new int[ColumnCount];
        for (int c = 0; c < bars.Length; c++) bars[c] = ColumnBarsOf(c);
        sectionStarts.AddRange(ValidSections(st.sections, ColumnCount, bars));
    }

    /// <summary>v7: column <paramref name="col"/>'s measures before the timeline exists (its islands' bars; phrases excluded).</summary>
    int ColumnBarsOf(int col)
    {
        int b = 0;
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++) { var kb = Islands[k]; if (kb != null && !kb.IsPhrase) b = Mathf.Max(b, OffsetBars(kb) + kb.bars); }
        return Mathf.Max(1, b);
    }

    static bool SameList(IList<int> a, IList<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>v7: the lanes of column <paramref name="col"/> (its islands but phrases: phrases live in melody tracks, never count toward MaxLanes).</summary>
    public int LaneCount(int col)
    {
        int n = 0;
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++) if (Islands[k] != null && !Islands[k].IsPhrase) n++;
        return n;
    }

    /// <summary>v7 (K): raise OnGroundsChanged.</summary>
    internal void RaiseGroundsChanged() => OnGroundsChanged?.Invoke();
    /// <summary>v7 (O): raise OnStairChanged.</summary>
    internal void RaiseStairChanged(KeyBlock kb) => OnStairChanged?.Invoke(kb);
    /// <summary>v7 (O's multi-grid ops): raise OnCarryChanged / OnLaunchChanged for a rebuilt island (W casts its spells).</summary>
    internal void RaiseCarryChanged(KeyBlock kb) => OnCarryChanged?.Invoke(kb);
    internal void RaiseLaunchChanged(KeyBlock kb) => OnLaunchChanged?.Invoke(kb);

    /// <summary>v7 phrase (SPEC v7 §14.2; K): the song beats a phrase island plays: its column's start + its offset, for its length.</summary>
    public void PhraseSpan(KeyBlock kb, out float startBeat, out float endBeat)
    {
        startBeat = 0f; endBeat = 0f;
        if (kb == null) return;
        startBeat = ColumnStart(kb.column) + kb.phraseOffset / (float)ProjectConfig.TicksPerBeat;
        endBeat = startBeat + Mathf.Max(1, kb.phraseBeats);
    }

    /// <summary>v7 stairs (SPEC v7 §2.4): the run's pitches as they sound (register included): Harmony.Stairs over the column's chord (Harmony.ChordOf)
    /// into the next chord (Harmony.ChordAfter) in the song key. The first step sits near the column's melody — the top note of its other islands'
    /// cubes (else the chord's 5th in 67..79); a climb starts a fifth under it; a walk lives in the bass (36..57).</summary>
    public int[] StairPitches(KeyBlock kb)
    {
        if (kb == null) return new int[0];
        int n = Mathf.Clamp(kb.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        int root; IList<int> semis;
        Harmony.ChordOf(kb, out root, out semis);
        int nroot; IList<int> nsemis;
        Harmony.ChordAfter(kb, out nroot, out nsemis);
        var k = MusicTheory.KeyOfSong();
        bool walk = kb.stairType == 5;
        int top = -1;
        var ms = new List<int>(); var ws = new List<float>();
        int s0 = OffsetBars(kb), s1 = s0 + Mathf.Max(1, kb.bars);   // v9: only the islands sounding in the run's own measures set its top
        foreach (var isl in Islands)
            if (isl != null && isl != kb && isl.column == kb.column && !isl.IsMoon && !isl.IsStairs
                && (!Offsettable(isl.kind) || (OffsetBars(isl) < s1 && s0 < OffsetBars(isl) + Mathf.Max(1, isl.bars)))) Harmony.NotesOfIsland(isl, ms, ws);
        foreach (int m in ms) if (m >= 60 && m > top) top = m;
        if (top < 0) { top = root + 7; while (top < 67) top += 12; while (top > 79) top -= 12; }
        if (kb.stairDir > 0) top -= 7;
        int lo = 55, hi = 88;
        if (walk) { lo = 36; hi = 57; top = root; while (top > 52) top -= 12; while (top < 40) top += 12; if (kb.stairDir > 0) top -= 5; }
        var r = Harmony.Stairs(kb.stairType, kb.stairDir, n, root, semis, nroot, nsemis, k.tonic, k.minor, top, lo, hi);
        for (int i = 0; i < r.Length; i++) r[i] += 12 * kb.register;
        return r;
    }

    /// <summary>v7 stairs (SPEC v7 §2.4): a runner's path, exactly one pass long. Lead-in: a rest on the first step that plays for (pass − steps ×
    /// rate) ticks (split into rests of at most 192 ticks; steps that do not fit are dropped from the front), then every step at the rate. From the
    /// start: every step at the rate, the last one holding to the pass end.</summary>
    public void StairPath(KeyBlock kb, out int[] xs, out int[] mods, out int[] durs)
    {
        var X = new List<int>(); var M = new List<int>(); var D = new List<int>();
        if (kb != null)
        {
            int n = Mathf.Clamp(kb.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
            int rate = MeasureState.StairRate(kb.stairRate);
            int pass = Mathf.Max(rate, Mathf.RoundToInt(Mathf.Max(1, kb.bars) * GlobalClock.BeatsPerBar * ProjectConfig.TicksPerBeat));
            int fit = Mathf.Clamp(pass / rate, 1, n);
            if (kb.stairLead)
            {
                int first = n - fit, rest = pass - fit * rate;
                while (rest > 0) { int chunk = Mathf.Min(rest, 192); if (chunk < 3) { if (D.Count > 0) D[D.Count - 1] += chunk; break; } X.Add(first); M.Add(1); D.Add(chunk); rest -= chunk; }
                for (int i = first; i < n; i++) { X.Add(i); M.Add(0); D.Add(rate); }
            }
            else
            {
                for (int i = 0; i < fit; i++) { X.Add(i); M.Add(0); D.Add(rate); }
                int left = pass - fit * rate;
                while (left > 0 && D.Count > 0)
                {
                    int room = 192 - D[D.Count - 1];
                    if (room >= left) { D[D.Count - 1] += left; left = 0; }
                    else { D[D.Count - 1] = 192; left -= room; if (left > 0) { X.Add(fit - 1); M.Add(7); D.Add(Mathf.Min(192, Mathf.Max(3, left))); left -= D[D.Count - 1]; } }
                }
            }
        }
        xs = X.ToArray(); mods = M.ToArray(); durs = D.ToArray();
    }

    /// <summary>v7 ground (SPEC v7 §2.5) — "grids should abide by the height": the target ground of column <paramref name="col"/> in world units.
    /// g(0) = 0; g(c + 1) = clamp(g(c) + StairRiseOf(c), GroundMin, GroundMax): the columns after a stair stand where it ends. Cached per timeline
    /// (RefreshGrounds); col = ColumnCount = the ground after the last column.</summary>
    public float GroundOf(int col)
    {
        if (col >= 0 && col < colGround.Count) return colGround[col];
        float g = 0f;
        for (int c = 0; c < col && c < ColumnCount; c++) g = Mathf.Clamp(g + StairRiseOf(c), ProjectConfig.GroundMin, ProjectConfig.GroundMax);
        return g;
    }

    /// <summary>v7: how far column <paramref name="col"/>'s first stairs island (lane order) moves the ground: dir × steps × StairStepRise; 0 without one.</summary>
    public float StairRiseOf(int col)
    {
        int a = ColumnFirst(col), n = ColumnSize(col);
        for (int k = a; k >= 0 && k < a + n && k < Islands.Count; k++)
        {
            var kb = Islands[k];
            if (kb != null && kb.IsStairs) return (kb.stairDir > 0 ? 1f : -1f) * Mathf.Clamp(kb.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps) * ProjectConfig.StairStepRise;
        }
        return 0f;
    }

    // ---- v7 grounds live (SPEC v7 §2.5 / §3.3): the targets per column, what every island / Moon stands on, the carry through rebuilds
    readonly List<float> colGround = new List<float>();   // GroundOf per column + the ground after the last

    /// <summary>v7: recomputes the columns' ground targets (with the timeline); OnGroundsChanged when any of them changed.</summary>
    void RefreshGrounds()
    {
        int nc = colFirst.Count;
        var g = new List<float>(nc + 1);
        float y = 0f;
        for (int c = 0; c < nc; c++) { g.Add(y); y = Mathf.Clamp(y + StairRiseOf(c), ProjectConfig.GroundMin, ProjectConfig.GroundMax); }
        g.Add(y);
        bool changed = g.Count != colGround.Count;
        for (int i = 0; !changed && i < g.Count; i++) if (Mathf.Abs(g[i] - colGround[i]) > 1e-4f) changed = true;
        colGround.Clear(); colGround.AddRange(g);
        if (changed) OnGroundsChanged?.Invoke();
    }

    /// <summary>v7: the ground <paramref name="kb"/> should stand on now: an island (a phrase: its start column) its column's; a Moon the ground under
    /// its platform's centre along the columns (it rides the ground of the column it is in line with, gliding with its follow).</summary>
    public float GroundTargetOf(KeyBlock kb)
    {
        if (kb == null) return 0f;
        if (kb.IsMoon) return GroundAtX(kb.Center.x + kb.MoonRideX);
        return GroundOf(kb.column);
    }

    /// <summary>v7: the ground at world x along the columns: a column's ground over its centre, interpolated linearly between two column centres,
    /// held past the first / last one.</summary>
    public float GroundAtX(float x)
    {
        int nc = colFirst.Count;
        if (nc == 0) return 0f;
        float x0 = ColumnCenterX(0);
        if (x <= x0) return GroundOf(0);
        for (int c = 0; c + 1 < nc; c++)
        {
            float x1 = ColumnCenterX(c + 1);
            if (x < x1) return Mathf.Lerp(GroundOf(c), GroundOf(c + 1), Mathf.Clamp01((x - x0) / Mathf.Max(1e-3f, x1 - x0)));
            x0 = x1;
        }
        return GroundOf(nc - 1);
    }

    /// <summary>v7: the grounds shown now, per column (its first island's) then per Moon — carried into a rebuild of the same song.</summary>
    float[] GroundsShown(out float[] moons)
    {
        int nc = colFirst.Count;
        var r = new float[nc];
        for (int c = 0; c < nc; c++) { var a = AnchorOf(c); r[c] = a != null ? a.GroundY : 0f; }
        moons = new float[Moons.Count];
        for (int j = 0; j < Moons.Count; j++) moons[j] = Moons[j] != null ? Moons[j].GroundY : 0f;
        return r;
    }

    /// <summary>v7: the rebuilt islands start at the ground their column showed (and the Moons theirs), then glide to their targets (an undo, a stair
    /// added / changed / removed, a column moved); another song (a load) snaps.</summary>
    void CarryGrounds(bool sameSong, float[] shown, float[] moons)
    {
        if (!sameSong || shown == null) return;
        foreach (var kb in Islands) if (kb != null && kb.column >= 0 && kb.column < shown.Length) kb.CarryGround(shown[kb.column]);
        if (moons != null) for (int j = 0; j < Moons.Count && j < moons.Length; j++) if (Moons[j] != null) Moons[j].CarryGround(moons[j]);
    }

    /// <summary>v7 (SPEC v7 §13.2): column <paramref name="col"/>'s width — its measures × IslandWidth (a 2-bar column is two measures wide).</summary>
    public float ColumnWidth(int col)
    {
        float w = ColumnBarsOf(col) * KeyBlock.IslandWidth;
        // v8: a grown piano is wider than its measures (KeyBlock.KeyboardWidthOf): the column makes room for it
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++) { var kb = Islands[k]; if (kb != null && kb.IsKeyboard) w = Mathf.Max(w, kb.Width); }
        return w;
    }
    /// <summary>v7: world x of the centre of column <paramref name="col"/>'s platforms (Moons stand in line with it).</summary>
    public float ColumnCenterX(int col) => ColumnX(col) - KeyBlock.EdgeInset + ColumnWidth(col) * 0.5f;
    /// <summary>v7: world x of column <paramref name="col"/>'s west edge / its east end (platforms + its longest belt).</summary>
    public float ColumnWestEdge(int col) => ColumnX(col) - KeyBlock.EdgeInset;
    public float ColumnEastEdge(int col) => ColumnWestEdge(col) + ColumnWidth(col) + BeltExtent(col);
    /// <summary>v7 (SPEC v7 §13.2 — "so song time maps to x"): world x of song beat <paramref name="beat"/> along the layout, on the platforms' west-edge
    /// line: the column the beat falls in, its pass (a belt: one slot per pass; rewind / no belt: the passes overlay the platform), the fraction of
    /// the pass × the column's width. Inside a section it is linear in time; over a section gap it jumps. Past the end it runs on.</summary>
    public float XEdgeOfBeat(double beat)
    {
        int nc = colFirst.Count;
        if (nc == 0 || colStart.Count < nc) return 0f;
        int c = Mathf.Clamp(ActiveColumn((float)beat), 0, nc - 1);
        double local = Math.Max(0.0, beat - colStart[c]), pl = Math.Max(1e-3, colPass[c]);
        int p = Mathf.Clamp((int)Math.Floor(local / pl + 1e-6), 0, colPasses[c] - 1);
        double inPass = local - p * pl;
        float w = ColumnWidth(c);
        return ColumnWestEdge(c) + (BeltExtent(c) > 0f ? p * (w + ProjectConfig.BeltGap) : 0f) + (float)(inPass / pl) * w;
    }
    /// <summary>
    /// v7 §19.1 (the user: "add a cap on beats per path … when making notes at first, it can auto-expand if needed or auto-unexpand"): column
    /// <paramref name="col"/> becomes <paramref name="bars"/> measures long (1..4) LIVE — no rebuild, no History: every island, tile and cube stays alive
    /// (a draft path in progress keeps its nodes); the islands re-shape (KeyBlock.ResizeMeasures), the timeline and every window follow, the columns
    /// re-lay (widths ∝ bars, the sections' gaps and ghost measures) and glide there; OnColumnsChanged + OnSectionsChanged (the section's measures
    /// changed; its starts never move: automatic sections are frozen first). While playing the lit column keeps its local beat. True when it changed.
    /// The gesture's owner pushes History when it ends (a draft's finish does).
    /// </summary>
    public bool ResizeColumnLive(int col, int bars)
    {
        if (!HasSong || col < 0 || col >= ColumnCount) return false;
        bars = Mathf.Clamp(bars, 1, 4);
        if (ColumnBarsOf(col) == bars) return false;
        if (sectionStarts.Count == 0) sectionStarts.AddRange(SectionStarts());   // the sections stay where they are (automatic ones would shift)
        bool wasPlaying = GlobalClock.IsPlaying;
        int litCol = colFirst.Count > 0 ? Mathf.Clamp(ActiveColumn(GlobalClock.SongBeat), 0, colFirst.Count - 1) : -1;
        double local = litCol >= 0 ? GlobalClock.SongBeatD - ColumnStart(litCol) : 0.0;
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++)
        {
            var kb = Islands[k]; if (kb == null) continue;
            kb.ResizeMeasures(bars);
            if (currentSongData != null && currentSongData.measures != null && k < currentSongData.measures.Length && currentSongData.measures[k] != null)
            { currentSongData.measures[k].bars = bars; currentSongData.measures[k].measureDuration = bars * GlobalClock.BeatsPerBar; }
        }
        // v7: a stairs runner stays exactly one pass long (the run re-derived on the same tiles: AudioCube.RefreshRunner)
        foreach (var cube in SequenceMaster.Cubes) if (cube != null && !cube.IsOnMoon && cube.Island != null && cube.Island.IsStairs && cube.Island.column == col) cube.RefreshRunner();
        RecomputeMeasureStarts();
        RelayoutLive(-1, 0.25f);
        if (wasPlaying && litCol >= 0 && litCol < ColumnCount)
        {
            double target = ColumnStart(litCol) + Math.Min(local, Math.Max(0.0, ColumnLength(litCol) - 1e-3));
            if (Math.Abs(target - GlobalClock.SongBeatD) > 1e-4) GlobalClock.Seek(target);
        }
        RaiseSectionsChanged();
        Fx.I.SetSongBounds(SongBounds);
        return true;
    }

    /// <summary>v7 §19.1: the measures column <paramref name="col"/>'s section would have if the column were <paramref name="bars"/> measures long (D stops
    /// the auto-expand at SectionBars); 0 outside the song.</summary>
    public int SectionBarsIfResized(int col, int bars)
    {
        int s = SectionOf(col);
        if (s < 0) return 0;
        return SectionBars(s) - ColumnBarsOf(col) + Mathf.Clamp(bars, 1, 4);
    }

    /// <summary>v7: true when columns <paramref name="col"/> and col + 1 touch (the same section, or a merge group): no gap between them.</summary>
    public bool Touching(int col) => col >= 0 && col + 1 < ColumnCount && (Glued(col) || !SectionBoundaryAfter(col));

    /// <summary>v7 repeat style (SPEC v7 §2.6): rewind on / off for island <paramref name="at"/> (no belt: time unwinds at every pass boundary).
    /// Seek-keeping rebuild, one History entry, OnRepeatStyleChanged.</summary>
    public void SetRewind(int at, bool on)
    {
        if (!ValidIsland(at)) return;
        var st = SongState.Capture();
        if (st.measures[at].rewind == on) return;
        st.measures[at].rewind = on;
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnRepeatStyleChanged?.Invoke(Islands[f]);
    }

    /// <summary>v7 vary (SPEC v7 §2.6): 0 same, 1 vary, 2 answer — the passes after the first play small differences. One History entry.</summary>
    public void SetVary(int at, int v)
    {
        if (!ValidIsland(at)) return;
        var st = SongState.Capture();
        v = Mathf.Clamp(v, 0, 2);
        if (st.measures[at].vary == v) return;
        st.measures[at].vary = v;
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnRepeatStyleChanged?.Invoke(Islands[f]);
    }

    /// <summary>v7 rewind (SPEC v7 §2.6 — "make it like the grid is repeating itself and time unwinding to the beginning everytime"): −1 when no
    /// unwind runs at song beat <paramref name="beat"/>, else 0..1 through the unwind at the end of pass <paramref name="pass"/> of a rewind island:
    /// the last RewindBeats (at most half a pass) of every pass but its last. A phrase's passes are its own. Pure.</summary>
    public float RewindPhase(KeyBlock kb, double beat, out int pass)
    {
        pass = -1;
        if (kb == null || kb.IsMoon || !kb.rewind) return -1f;
        double s, e;
        if (!TurnSpan(kb, out s, out e)) return -1f;
        double pl = kb.IsPhrase ? Mathf.Max(1, kb.phraseBeats) : (kb.column >= 0 && kb.column < colPass.Count ? colPass[kb.column] : kb.LengthBeats);
        if (pl <= 0.0) return -1f;
        int passes = Mathf.RoundToInt((float)((e - s) / pl));
        if (passes < 2 || beat < s || beat >= e) return -1f;
        int p = (int)Math.Floor((beat - s) / pl + 1e-9);
        if (p >= passes - 1) return -1f;   // the last pass plays on into what follows (no unwind)
        double rb = RewindBeatsOf(pl), passEnd = s + (p + 1) * pl;
        if (beat < passEnd - rb) return -1f;
        pass = p;
        return Mathf.Clamp01((float)((beat - (passEnd - rb)) / rb));
    }

    /// <summary>v7: the unwind's length in a pass of <paramref name="passBeats"/> beats (RewindBeats, at most half the pass).</summary>
    public static double RewindBeatsOf(double passBeats) => Math.Min(ProjectConfig.RewindBeats, passBeats * 0.5);

    /// <summary>v7 flow (SPEC v7 §2.7): <see cref="SetCarry(int, int)"/> with a style. v7 §21.3: the style is IGNORED — every carry is a long grid
    /// (stored as 1, flow); the signature stays for its callers. One rebuild keeping the lit column, one History entry, OnCarryChanged.</summary>
    public void SetCarry(int at, int n, int style) => SetCarry(at, n);

    /// <summary>v7 launch (SPEC v7 §2.8): island <paramref name="at"/>'s cubes are flung to the next grid at the end of its turn. One History entry,
    /// OnLaunchChanged.</summary>
    public void SetLaunch(int at, bool on)
    {
        if (!ValidIsland(at)) return;
        var st = SongState.Capture();
        if (st.measures[at].launch == on) return;
        st.measures[at].launch = on;
        var map = RebuildKeepingLit(st, null, at);
        History.Push();
        int f = map != null && at < map.Length ? map[at] : at;
        if (f >= 0 && f < Islands.Count && Islands[f] != null) OnLaunchChanged?.Invoke(Islands[f]);
    }

    /// <summary>v9 (L) the stage lights: island <paramref name="at"/> is a LEAD grid (<paramref name="on"/>) — while the song plays the stage dims and a
    /// spotlight comes down on it in every measure it sounds (StageLights). Only the mark changes (nothing is rebuilt: the timeline is the same); one
    /// History entry, OnLeadChanged. Keyboards, stairs and phrases are lead anyway (<see cref="IsLead"/>); a Moon never.</summary>
    public void SetLead(int at, bool on)
    {
        if (!ValidIsland(at)) return;
        var kb = Islands[at];
        if (kb.lead == on) return;
        kb.lead = on;
        History.Push();
        OnLeadChanged?.Invoke(kb);
    }

    /// <summary>v9 (L): every grid of <paramref name="grids"/> marked lead / not (the selection bar's spotlight): one History entry, OnLeadChanged per
    /// grid that changed. False when none changed.</summary>
    public bool SetLeadGrids(IList<KeyBlock> grids, bool on)
    {
        if (grids == null) return false;
        var changed = new List<KeyBlock>();
        foreach (var kb in grids) if (kb != null && !kb.IsMoon && Islands.Contains(kb) && kb.lead != on) { kb.lead = on; changed.Add(kb); }
        if (changed.Count == 0) return false;
        History.Push();
        foreach (var kb in changed) OnLeadChanged?.Invoke(kb);
        return true;
    }

    /// <summary>v9 (L): the stage lights spotlight <paramref name="kb"/> while it sounds — a grid marked lead, and every keyboard, stairs and phrase
    /// (VoiceRules.IsMelody); a Moon never.</summary>
    public static bool IsLead(KeyBlock kb) => kb != null && !kb.IsMoon && (kb.lead || VoiceRules.IsMelody(kb));

    /// <summary>v9 (L): an island's lead mark changed (SetLead / SetLeadGrids).</summary>
    public event Action<KeyBlock> OnLeadChanged;

    /// <summary>v7 launch: where island <paramref name="kb"/>'s cubes are flung — in the column that plays right after its turn (the next column; a
    /// phrase: the column its end falls in; the focus loop's last column: the loop's first) the island nearest in z, chord islands first, then
    /// keyboards, then phrases, then stairs; after the last column the first column's when the song loops, else null. Stairs never launch.</summary>
    public KeyBlock LaunchTarget(KeyBlock kb)
    {
        if (kb == null || kb.IsMoon || kb.IsStairs || ColumnCount == 0) return null;
        int nc = ColumnCount;
        int c = kb.IsPhrase ? ActiveColumn(TurnEnd(kb) + 1e-3f) : kb.column + 1;
        if (kb.IsPhrase && TurnEnd(kb) >= TotalBeats - 1e-3f) c = nc;
        if (GlobalClock.HasRegion)
        {
            int rLast = Mathf.Clamp(ActiveColumn((float)GlobalClock.RegionEnd - 1e-3f), 0, nc - 1);
            if (c - 1 == rLast || (kb.IsPhrase && TurnEnd(kb) >= GlobalClock.RegionEnd - 1e-3f)) c = Mathf.Clamp(ActiveColumn((float)GlobalClock.RegionStart + 1e-3f), 0, nc - 1);
        }
        if (c >= nc) { if (!GlobalClock.LoopSong) return null; c = 0; }
        KeyBlock best = null; float bestD = float.MaxValue; int bestRank = int.MaxValue;
        foreach (var o in Islands)
        {
            if (o == null || o == kb || o.column != c || o.IsMoon) continue;
            int rank = o.IsStairs ? 3 : (o.IsPhrase ? 2 : (o.IsKeyboard ? 1 : 0));
            float d = Mathf.Abs(o.pz - kb.pz);
            if (rank < bestRank || (rank == bestRank && d < bestD)) { bestRank = rank; bestD = d; best = o; }
        }
        return best;
    }

    /// <summary>v7 launch: the song beat island <paramref name="kb"/>'s last pass ends (its column's start + its passes × the pass length; a phrase:
    /// the end of its own last pass) — where a flung cube lands.</summary>
    public float TurnEnd(KeyBlock kb)
    {
        double s, e;
        if (kb == null || !TurnSpan(kb, out s, out e)) return 0f;
        return (float)e;
    }

    public struct MergeProbe { public float west, east, front, backWest, backEast; public int count; public bool keyboard; public float width; }   // v6: keyboard = the block is keyboards (merges only with keyboards); v7: width = the block's platforms (0: count × IslandWidth)
    /// <summary>A merge candidate: the target's end member (east end for <c>east</c>, west end otherwise), the edge gap, and where the moving
    /// block's first (west-most) member's root goes (rootX, rootZ).</summary>
    public struct MergeSlot { public KeyBlock target; public bool east; public float gap; public float rootX, rootZ; }

    /// <summary>The probe of a block whose first member's root is at (<paramref name="rootX"/>, <paramref name="rootZ"/>).</summary>
    public static MergeProbe ProbeAt(float rootX, float rootZ, int count, float depthWest, float depthEast)
    {
        float west = rootX - KeyBlock.EdgeInset, front = rootZ - KeyBlock.EdgeInset;
        return new MergeProbe { west = west, east = west + Mathf.Max(1, count) * KeyBlock.IslandWidth, front = front, backWest = front + depthWest, backEast = front + depthEast, count = Mathf.Max(1, count) };
    }

    /// <summary>v7: <see cref="ProbeAt(float, float, int, float, float)"/> for a block <paramref name="width"/> wide (its platforms: multi-measure islands).</summary>
    public static MergeProbe ProbeAt(float rootX, float rootZ, int count, float depthWest, float depthEast, float width)
    {
        var p = ProbeAt(rootX, rootZ, count, depthWest, depthEast);
        if (width > 0f) { p.east = p.west + width; p.width = width; }
        return p;
    }

    /// <summary>v7: a measure's kind as the layout keeps it (0 chord, 2 keyboard, 3 stairs, 4 phrase; anything else — a stray Moon — a chord island).</summary>
    static int KindOf(int kind) => kind >= 2 && kind <= 4 ? kind : 0;

    public bool FindMerge(MergeProbe p, ICollection<KeyBlock> exclude, out MergeSlot slot) { return FindMerge(p, exclude, -1, -1, out slot); }

    /// <summary>
    /// SPEC v4 §3 K4 merge zone: the nearest island whose east edge is within MergeReach of the probe's west edge (or whose west edge is near its
    /// east edge) with at least MergeOverlap front-back overlap (the same lane) and a free side: no member of its group already sits there, and a
    /// new column may go there (the target's column is not glued on that side). Islands in <paramref name="exclude"/> are never targets. For an
    /// in-column drag pass the moving block's column range [<paramref name="movingWest"/>, <paramref name="movingEast"/>]: only islands of the
    /// directly adjacent columns qualify (the block's own neighbour columns may already be glued through another lane).
    /// </summary>
    public bool FindMerge(MergeProbe p, ICollection<KeyBlock> exclude, int movingWest, int movingEast, out MergeSlot slot)
    {
        return FindMerge(p, exclude, movingWest, movingEast, float.MaxValue, out slot);
    }

    /// <summary>v5 free drag: any island (not in <paramref name="exclude"/>) whose outer end is within MergeReach of the probe's edge in the same lane,
    /// the probe overlapping its platform by at most <paramref name="maxOverlap"/> (deeper = the island is being dropped over a column or a gap, not
    /// pushed against an edge).</summary>
    public bool FindMerge(MergeProbe p, ICollection<KeyBlock> exclude, float maxOverlap, out MergeSlot slot)
    {
        return FindMerge(p, exclude, -1, -1, maxOverlap, out slot);
    }

    bool FindMerge(MergeProbe p, ICollection<KeyBlock> exclude, int movingWest, int movingEast, float maxOverlap, out MergeSlot slot)
    {
        slot = new MergeSlot { gap = float.MaxValue };
        bool found = false;
        float w = KeyBlock.IslandWidth;
        bool inColumn = movingWest >= 0 && movingEast >= movingWest;
        foreach (var t in Islands)
        {
            if (t == null || t.IsMoon || t.IsKeyboard != p.keyboard || t.IsStairs || t.IsPhrase || (exclude != null && exclude.Contains(t))) continue;   // v6: one family per group; v7: stairs / phrases never merge
            int c = t.column;
            // v7 (SPEC v7 §13.2): inside a section the columns already touch — a merge only glues across a GAP (a section boundary, the song's ends);
            // pushed against a touching neighbour the block joins / inserts a column there instead
            bool gapEast = c + 1 >= colFirst.Count || SectionBoundaryAfter(c), gapWest = c == 0 || SectionBoundaryAfter(c - 1);
            // east of t: the probe's west edge at t's east edge (v5: at the end of t's column's belt, where a glued neighbour waits)
            if (gapEast && GroupNeighbour(t, +1) == null && (inColumn ? c == movingWest - 1 : !Glued(c)))
            {
                float belt = BeltExtent(c);
                float signedE = p.west - (t.EastEdge + belt), gapE = Mathf.Abs(signedE);
                if (gapE <= ProjectConfig.MergeReach && signedE >= -maxOverlap && gapE < slot.gap && ZOverlap(p.front, p.backWest, t.FrontEdge, t.BackEdge))
                { slot = new MergeSlot { target = t, east = true, gap = gapE, rootX = t.px + t.Width + belt, rootZ = t.pz }; found = true; }
            }
            // west of t: the probe's east edge at t's west edge
            if (gapWest && GroupNeighbour(t, -1) == null && (inColumn ? c == movingEast + 1 : !Glued(c - 1)))
            {
                float signedW = t.WestEdge - p.east, gapW = Mathf.Abs(signedW);
                if (gapW <= ProjectConfig.MergeReach && signedW >= -maxOverlap && gapW < slot.gap && ZOverlap(p.front, p.backEast, t.FrontEdge, t.BackEdge))
                { slot = new MergeSlot { target = t, east = false, gap = gapW, rootX = t.px - (p.width > 0f ? p.width : w * p.count), rootZ = t.pz }; found = true; }
            }
        }
        return found;
    }

    static bool ZOverlap(float a0, float a1, float b0, float b1)
    {
        float ov = Mathf.Min(a1, b1) - Mathf.Max(a0, b0);
        float minDepth = Mathf.Min(a1 - a0, b1 - b0);
        return minDepth > 0f && ov >= ProjectConfig.MergeOverlap * minDepth;
    }

    // ------------------------------------------------------------------ merge / split
    /// <summary>
    /// SPEC v4 §3 K4 merge: <paramref name="moving"/>'s group (or moving alone) and its column(s) move in the column order to right after (east) /
    /// right before (west) <paramref name="target"/>'s group; the columns glue (no gap); every member shares one group id (the target's, else the
    /// mover's, else max + 1); the moving members snap to the target's z (the target never moves; the other islands of the moving columns make room).
    /// Seek-keeping rebuild, History.Push, the merge animation. False when either is a Moon, they share a column or a group, or the target's side
    /// is glued to another column already (a column cannot squeeze in there).
    /// </summary>
    public bool MergeIslands(KeyBlock moving, KeyBlock target, bool eastSide)
    {
        if (!HasSong || moving == null || target == null || moving.IsMoon || target.IsMoon) return false;
        int mi = Islands.IndexOf(moving), ti = Islands.IndexOf(target);
        if (mi < 0 || ti < 0 || moving.column == target.column || SameGroup(moving, target)) return false;
        if (moving.IsKeyboard != target.IsKeyboard) return false;   // v6: a keyboard never merges with a chord island
        if (moving.IsStairs || target.IsStairs || moving.IsPhrase || target.IsPhrase) return false;   // v7: stairs and phrases never merge
        var gm = GroupOf(moving); var gt = GroupOf(target);
        int m0 = gm[0].column, m1 = gm[gm.Count - 1].column, t0 = gt[0].column, t1 = gt[gt.Count - 1].column;
        if ((target.column >= m0 && target.column <= m1) || (moving.column >= t0 && moving.column <= t1)) return false;
        int nc = ColumnCount;
        if (eastSide && t1 + 1 < nc && (t1 + 1 < m0 || t1 + 1 > m1) && Glued(t1)) return false;
        if (!eastSide && t0 - 1 >= 0 && (t0 - 1 < m0 || t0 - 1 > m1) && Glued(t0 - 1)) return false;
        var st = SongState.Capture();
        var oldSecs = SectionsOf(st);
        int id = target.group != 0 ? target.group : (moving.group != 0 ? moving.group : NextGroupId(st.measures));
        float pz = st.measures[ti].pz;
        foreach (var kb in gt) { int k = Islands.IndexOf(kb); if (k >= 0) st.measures[k].group = id; }
        foreach (var kb in gm) { int k = Islands.IndexOf(kb); if (k >= 0) { st.measures[k].group = id; st.measures[k].pz = pz; } }
        var cols = new List<int>(nc);
        for (int c = 0; c < nc; c++) if (c < m0 || c > m1) cols.Add(c);
        int at = eastSide ? cols.IndexOf(t1) + 1 : cols.IndexOf(t0);
        for (int k = 0; k <= m1 - m0; k++) cols.Insert(at + k, m0 + k);
        var map = ReorderColumns(st, cols);
        // v7 (SPEC v7 §13.2): "a merge across a boundary joins the sections" — two neighbours glued in place: their sections become one
        // (FixSections removes the boundary inside the group); a block moved from elsewhere lands in the target's section
        bool inPlace = true; for (int k = 0; k < cols.Count; k++) if (cols[k] != k) { inPlace = false; break; }
        var origin = new int[cols.Count];
        for (int k = 0; k < cols.Count; k++) origin[k] = !inPlace && cols[k] >= m0 && cols[k] <= m1 ? -1 : cols[k];
        st.sections = RemapSections(oldSecs, origin, SnapshotColumnBars(st.measures), eastSide ? 1 : 2).ToArray();
        FixSections(st);
        int seamA = Islands.IndexOf(eastSide ? gt[gt.Count - 1] : gm[gm.Count - 1]), seamB = Islands.IndexOf(eastSide ? gm[0] : gt[0]);
        var final = RebuildKeepingLit(st, map, map[mi]);
        History.Push();
        KeyBlock a = seamA >= 0 && final[seamA] >= 0 ? Islands[final[seamA]] : null, b = seamB >= 0 && final[seamB] >= 0 ? Islands[final[seamB]] : null;
        PlayMerge(a, b);
        Onboarding.Notify(Onboarding.Ev.IslandsMerged);
        return true;
    }

    /// <summary>
    /// The merge animation (SPEC v3 §3.3) on the new seam between <paramref name="a"/> (west) and <paramref name="b"/> (east): a white flash sweeps
    /// along the seam back to front (0.35 s) with sparkles, every member pulses and draws its outline in over 0.4 s, the seam bridge grows in (the
    /// moved columns glide in from where they were); sound: a sparkle + (only while stopped: harmonic safety) the two roots as a quick "zip", a
    /// UI tick while playing.
    /// </summary>
    void PlayMerge(KeyBlock a, KeyBlock b)
    {
        if (a == null || b == null) return;
        foreach (var kb in GroupOf(a)) { if (kb == null) continue; kb.Pulse(1f); kb.DrawRimIn(0.4f); }
        a.ShowBridgeAfter(0.2f);
        a.FlashSeam(b, 0.16f);
        // integration: comic energy dots burst from the new seam (the big-moment language of the look, SPEC v3 §7.1)
        Fx.KirbyDots((a.Center + b.Center) * 0.5f + Vector3.up * 0.6f, Color.Lerp(a.chordColor, b.chordColor, 0.5f), 1.4f);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.42f, 1.12f);
        if (!GlobalClock.IsPlaying && Synth.Ready) StartCoroutine(Zip(a.chordRootMIDI, b.chordRootMIDI));
        else AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.6f);
    }

    IEnumerator Zip(int rootA, int rootB)
    {
        yield return new WaitForSecondsRealtime(0.12f);
        if (GlobalClock.IsPlaying) yield break;
        Synth.Preview(0, SynthBank.ClampToRegister(0, rootA + 12), 66, 0.3f);
        yield return new WaitForSecondsRealtime(0.08f);
        if (GlobalClock.IsPlaying) yield break;
        Synth.Preview(0, SynthBank.ClampToRegister(0, rootB + 12), 74, 0.45f);
    }

    /// <summary>
    /// SPEC v4 §3 K4 split (the seam's scissors): cuts island <paramref name="left"/>'s group between it and its east neighbour (the member in the
    /// next column); when no other lane glues the two columns they unglue and the right part slides east by the column gap (the whole song to the
    /// right follows) with a spark line and a reversed zip. Live (order, windows and audio are untouched), then History.Push. A part of one island
    /// leaves the group. False when there is no seam there.
    /// </summary>
    public bool SplitGroup(int left)
    {
        if (left < 0 || left >= Islands.Count) return false;
        var a = Islands[left]; var b = GroupNeighbour(a, +1);
        if (a == null || b == null) return false;
        var members = GroupOf(a);
        int newId = NextGroupId(), g = a.group;
        int westCount = 0; foreach (var m in members) if (m.column <= a.column) westCount++;
        int eastCount = members.Count - westCount;
        Vector3 seamFront = new Vector3(a.EastEdge, 0.12f, a.FrontEdge);
        float seamLen = Mathf.Min(a.Depth, b.Depth);
        Color c = Color.Lerp(a.chordColor, b.chordColor, 0.5f);
        foreach (var m in members) m.group = m.column <= a.column ? (westCount > 1 ? g : 0) : (eastCount > 1 ? newId : 0);
        RelayoutLive(-1, 0.3f);
        for (int s = 0; s < 5; s++) Fx.Burst(seamFront + new Vector3(0f, 0f, seamLen * (s + 0.5f) / 5f), Color.Lerp(c, Color.white, 0.5f), 6, 1.8f);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.35f, 1.3f);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.3f, 0.72f);
        History.Push();
        Onboarding.Notify(Onboarding.Ev.IslandSplit);
        return true;
    }

    // ------------------------------------------------------------------ tray placement (SPEC v3 §3.1, v4 §3 K5)
    /// <summary>Sets <paramref name="ms"/>'s root so its platform is centred on <paramref name="centre"/> (root snapped to the 0.5 u island grid).</summary>
    public static void SetCentre(MeasureState ms, Vector3 centre)
    {
        int rows = KeyBlock.RowsOf(ms.semis);
        var off = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, rows);
        float snap = ProjectConfig.IslandSnap;
        ms.px = Mathf.Round((centre.x - off.x) / snap) * snap;
        ms.pz = Mathf.Round((centre.z - off.z) / snap) * snap;
        ms.placed = true;
    }

    /// <summary>
    /// v3 tray API, v4 meaning: a new island from <paramref name="md"/> as a new COLUMN before the column of island <paramref name="insertAt"/>
    /// (Islands.Count = at the end of the song), its z from <paramref name="centre"/>; seek-keeping rebuild, History.Push, the island rises from
    /// the sea. Returns its index (-1 on failure).
    /// </summary>
    public int PlaceIsland(MeasureData md, Vector3 centre, int insertAt)
    {
        if (!HasSong || md == null) return -1;
        var st = SongState.Capture();
        var ms = MeasureState.From(md); ms.group = 0; ms.kind = KindOf(md.kind);   // v6 / v7: keyboards, stairs, phrases keep their kind
        SetCentre(ms, centre);
        int col = insertAt >= Islands.Count ? ColumnCount : Mathf.Max(0, ColumnOf(Mathf.Max(0, insertAt)));
        int at = PlaceStateAsColumn(st, ms, col, null);
        History.Push();
        if (at >= 0 && at < Islands.Count && Islands[at] != null) Islands[at].RiseIn();
        return at;
    }

    /// <summary>v3 tray (a card click), v4: a new column right after the glued run of <paramref name="focused"/>'s column, the new island in the
    /// focused island's lane (its z). Returns its index.</summary>
    public int PlaceIslandAfter(MeasureData md, int focused)
    {
        if (!HasSong || md == null) return -1;
        focused = Mathf.Clamp(focused, 0, Islands.Count - 1);
        var st = SongState.Capture();
        var ms = MeasureState.From(md); ms.group = 0; ms.kind = KindOf(md.kind);   // v6 / v7: keyboards, stairs, phrases keep their kind
        ms.pz = st.measures[focused].pz; ms.placed = true;
        int at = PlaceStateAsColumn(st, ms, GlueRunEnd(ColumnOf(focused)) + 1, null);
        History.Push();
        if (at >= 0 && at < Islands.Count && Islands[at] != null) Islands[at].RiseIn();
        return at;
    }

    /// <summary>v3 tray ghost released in a merge zone, v4: a new column right after (east) / before (west) <paramref name="target"/>'s column, glued,
    /// the new island in the target's lane joining its group, in one rebuild + one History.Push, then the merge animation. Returns its index
    /// (-1 when the target's side is taken: a group member or a glued column there).</summary>
    public int PlaceIslandMerged(MeasureData md, KeyBlock target, bool eastSide)
    {
        if (!HasSong || md == null || target == null || target.IsMoon) return -1;
        int ti = Islands.IndexOf(target);
        if (ti < 0 || GroupNeighbour(target, eastSide ? 1 : -1) != null) return -1;
        if ((md.kind == 2) != target.IsKeyboard) return -1;   // v6: one family per group
        if (md.kind == 3 || md.kind == 4 || target.IsStairs || target.IsPhrase) return -1;   // v7: stairs and phrases never merge
        int tc = target.column;
        if (eastSide ? Glued(tc) : Glued(tc - 1)) return -1;
        var st = SongState.Capture();
        int id = st.measures[ti].group != 0 ? st.measures[ti].group : NextGroupId(st.measures);
        st.measures[ti].group = id;
        var ms = MeasureState.From(md); ms.kind = md.kind == 2 ? 2 : 0; ms.group = id; ms.placed = true;
        ms.pz = st.measures[ti].pz;
        int at = PlaceStateAsColumn(st, ms, eastSide ? tc + 1 : tc, null, false, eastSide ? 1 : 2);   // v7: glued into the target's section
        History.Push();
        if (at >= 0 && at < Islands.Count && Islands[at] != null)
        {
            var nk = Islands[at]; var tk = GroupNeighbour(nk, eastSide ? -1 : 1);
            nk.SlideFrom(new Vector3(eastSide ? 0.8f : -0.8f, 0f, 0f), 0.28f, true);
            if (tk != null) PlayMerge(eastSide ? tk : nk, eastSide ? nk : tk);
        }
        Onboarding.Notify(Onboarding.Ev.IslandsMerged);
        return at;
    }

    /// <summary>
    /// The tray's wand, v4: every card of <paramref name="cards"/> becomes an island in a new column at the END of the song, in the last column's
    /// lane, in one rebuild + one History.Push; they rise in one after another. Returns how many were placed.
    /// </summary>
    public int PlaceAll(IList<MeasureData> cards)
    {
        if (!HasSong || cards == null || cards.Count == 0) return 0;
        var st = SongState.Capture();
        EnsureSections(st);
        int n = st.measures.Length, nc = ColumnCount;
        float pz = nc > 0 ? st.measures[colFirst[nc - 1]].pz : 0f;
        var list = st.measures.ToList();
        int placed = 0;
        foreach (var md in cards)
        {
            if (md == null) continue;
            var ms = MeasureState.From(md); ms.group = 0; ms.kind = KindOf(md.kind); ms.col = nc + placed; ms.pz = pz; ms.placed = true;
            list.Add(ms);
            placed++;
        }
        if (placed == 0) return 0;
        st.measures = list.ToArray();
        JoinNewColumns(st, nc, placed);   // v7: the new columns fill the last section to SectionBars measures, then start new ones
        RebuildKeepingLit(st, null);
        History.Push();
        for (int k = 0; k < placed; k++) { int f = NewIndex(n + k); if (f >= 0 && f < Islands.Count && Islands[f] != null) Islands[f].RiseIn(0.12f * k); }
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.45f);
        return placed;
    }

    /// <summary>A diagnostic line of the live groups (tests): "i:group@px,pz" per island plus the invariant verdict.</summary>
    public string GroupReport()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Islands.Count; i++) { var kb = Islands[i]; if (kb == null) continue; sb.Append(i).Append(':').Append(kb.group).Append('@').Append(kb.px.ToString("F2")).Append(',').Append(kb.pz.ToString("F2")).Append(' '); }
        return sb.ToString();
    }

    // ================================================================== v4 island components (SPEC v4 §3 K5): the ops behind the island header
    /// <summary>
    /// + above / + below: a new island in <paramref name="island"/>'s column with the same chord (root, semis) and bars, no cubes, LaneGap beyond its
    /// back edge (<paramref name="above"/>, +z) or front edge (-z); the column's other islands are pushed away; it rises from the sea. Seek-keeping
    /// rebuild + History.Push. Refused (the column shakes, a thud, -1) when the column is full (MaxLanes) or the index is not an island.
    /// </summary>
    public int AddIslandInColumn(int island, bool above) => AddIslandInColumn(island, above, 0);

    /// <summary>v8: <see cref="AddIslandInColumn(int, bool)"/> on measure <paramref name="measure"/> of island <paramref name="island"/> (0 = its first
    /// measure): a new chord grid is always a NORMAL one-measure grid there (never the reference grid's length: the user, "grids added above it
    /// should not be forced to be extended").</summary>
    public int AddIslandInColumn(int island, bool above, int measure)
    {
        if (!ValidIsland(island)) return -1;
        var kb = Islands[island];
        if (LaneCount(kb.column) >= ProjectConfig.MaxLanes) { Refuse(kb.column); return -1; }
        var st = SongState.Capture();
        var src = st.measures[island];
        var ms = new MeasureState { chordKey = src.chordKey, root = src.root, semis = (int[])src.semis.Clone(), bars = src.bars, placed = true, kind = src.kind == 2 ? 2 : 0, energy = 2, repeat = 1, px = src.px, col = src.col };   // v6: + beside a keyboard = another keyboard
        if (ms.kind == 0) { ms.bars = 1; ms.barOffset = Mathf.Clamp(OffsetBars(src) + Mathf.Clamp(measure, 0, Mathf.Max(0, src.bars - 1)), 0, 3); }   // v8: a normal grid on that measure
        if (src.kind == 3 || src.kind == 4)
        {
            // v7: + beside a stairs / a phrase = a chord island of the column's chord (they hold none)
            var ch = ChordOfColumn(kb.column);
            var d = ch != null ? ch.ToData() : MusicTheory.Diatonic(MusicTheory.KeyOfSong(), 0);
            ms.chordKey = d.chordKey; ms.root = d.chordRootMIDI; ms.semis = (int[])(d.semitones ?? new[] { 0, 4, 7 }).Clone();
        }
        ms.pz = LanePz(src, ms, above);
        int at = PlaceStateInColumn(st, ms, kb.column, null);
        if (at < 0) return -1;
        History.Push();
        if (at < Islands.Count && Islands[at] != null) { Islands[at].RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        return at;
    }

    /// <summary>
    /// Duplicate: a copy of <paramref name="island"/> (chord, register, section controls and its cubes with new ids, same lengths / stickers):
    /// <paramref name="where"/> 0 = right (a new column inserted right after the glued run of its column, the copy in its lane: the song gets
    /// longer), 1 = below, 2 = above (the same column, LaneGap beyond its front / back edge, neighbours pushed; refused with a shake when full).
    /// Seek-keeping rebuild + History.Push; the copy rises from the sea. Returns its index (-1 refused).
    /// </summary>
    public int DuplicateIsland(int island, int where)
    {
        if (!ValidIsland(island)) return -1;
        var kb = Islands[island];
        where = Mathf.Clamp(where, 0, 3);
        if (where != 0 && where != 3 && !kb.IsPhrase && LaneCount(kb.column) >= ProjectConfig.MaxLanes) { Refuse(kb.column); return -1; }
        var st = SongState.Capture();
        var src = st.measures[island];
        var clone = MeasureState.Clone(src); clone.group = 0;
        if (where == 0 || where == 3) clone.barOffset = 0;   // v8: a copy in a new column starts at its start
        var copies = new List<CubeState>();
        foreach (var c in st.cubes) if (c.moon < 0 && c.measure == island) { var d = CubeState.Clone(c); d.id = 0; copies.Add(d); }
        int at;
        if (where == 0) at = PlaceStateAsColumn(st, clone, GlueRunEnd(kb.column) + 1, copies);
        else if (where == 3)
        {
            // 3 = LEFT (the user: "add to left and right just basically translates to copy"): a new column right BEFORE the glued run of its
            // column, the copy in its lane — the copy plays first
            int c0 = kb.column;
            while (c0 > 0 && Glued(c0 - 1)) c0--;
            at = PlaceStateAsColumn(st, clone, c0, copies);
        }
        else { clone.pz = LanePz(src, clone, where == 2); at = PlaceStateInColumn(st, clone, kb.column, copies); }
        if (at < 0) return -1;
        History.Push();
        if (at < Islands.Count && Islands[at] != null) { Islands[at].RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        return at;
    }

    /// <summary>
    /// Make lower / higher: island <paramref name="island"/>'s register (octaves, -2..+2). Live (no rebuild, the song keeps playing): its tiles
    /// sound reg x 12 semitones higher and the cubes ride along; a quick arpeggio of its chord in the new register (the preview voice, only while
    /// stopped: harmonic safety; a tick while playing). Pushes History. v6: the platform is an octave TOWER (TowerLiftAt): at rest only a hint
    /// higher / lower, it rises (dips) on its turn; while stopped the change plays that rise and the slow sink once as a preview.
    /// </summary>
    public void SetRegister(int island, int reg)
    {
        if (!ValidIsland(island)) return;
        reg = Mathf.Clamp(reg, -2, 2);
        var kb = Islands[island];
        if (kb.register == reg) { kb.Shake(0.08f); AudioPool.UI(ProceduralAudio.Thud(), 0.2f, 1.3f); return; }
        bool up = reg > kb.register;
        kb.SetRegister(reg, true);
        if (currentSongData != null && currentSongData.measures != null && island < currentSongData.measures.Length && currentSongData.measures[island] != null) currentSongData.measures[island].reg = reg;
        Fx.SeaRipple(kb.Center, kb.chordColor, 2.4f);
        if (!GlobalClock.IsPlaying && Synth.Ready) StartCoroutine(RegisterArp(kb, up));
        else AudioPool.UI(ProceduralAudio.Tick(), 0.3f, up ? 1.5f : 0.8f);
        History.Push();
        RaiseColumnsChanged();
    }

    /// <summary>The island's chord as a quick arpeggio in its new register (up for "higher", down for "lower"), Keys preview voice.</summary>
    IEnumerator RegisterArp(KeyBlock kb, bool up)
    {
        if (kb == null) yield break;
        var tones = new List<int>();
        var t0 = kb.GetTile(0, 0);
        int baseMidi = t0 != null ? t0.midi : kb.chordRootMIDI + 12 * kb.register;
        IList<int> semis = kb.semitoneList;
        if (kb.IsKeyboard)
        {
            // v6: a keyboard arpeggiates its column's chord from its first key of that root, in its register
            int root; IList<int> cs;
            if (Harmony.ChordOf(kb, out root, out cs) && cs != null && cs.Count > 0) { baseMidi += ((root - kb.chordRootMIDI) % 12 + 12) % 12; semis = cs; }
        }
        foreach (int s in semis) tones.Add(baseMidi + s);
        if (!up) tones.Reverse();
        for (int i = 0; i < tones.Count && i < 5; i++)
        {
            if (GlobalClock.IsPlaying || kb == null) yield break;
            Synth.Preview(0, VoiceRules.Fold(0, tones[i] + Transpose), 70 + 6 * i, 0.3f);
            yield return new WaitForSecondsRealtime(0.06f);
        }
    }

    /// <summary>
    /// The in-column move (IslandDrag's platform drag and the header grip): island <paramref name="island"/> (and its group, each member in its own
    /// column) goes to z = <paramref name="pz"/> (clamped to the allowed range: one lane pair beyond the column); the column's other islands are pushed
    /// away from it, and one whose centre it passes swaps to its other side (a preview computed from where they stood when the move began, so they
    /// come back when it moves on). <paramref name="commit"/>: snap 0.5 u, finish the move, Moons pushed clear, OnColumnsChanged. Never pushes
    /// History (the gesture owner does, once, after the commit). Live: no rebuild, the timeline is untouched.
    /// </summary>
    public void MoveIslandInColumn(int island, float pz, bool commit)
    {
        if (!ValidIsland(island)) return;
        if (islandMove == null || islandMove.island != island || islandMove.origZ.Length != Islands.Count) BeginIslandMove(island);
        pz = Mathf.Clamp(pz, islandMove.lo, islandMove.hi);
        if (commit) pz = Mathf.Clamp(Mathf.Round(pz / ProjectConfig.IslandSnap) * ProjectConfig.IslandSnap, islandMove.lo, islandMove.hi);
        islandMove.lastZ = pz;
        var ms = LiveStates();
        for (int i = 0; i < ms.Length; i++) ms[i].pz = islandMove.origZ[i];
        ms[island].pz = pz;
        var dep = ResolveZ(ms, island, islandMove.origZ, new List<int>(SectionStarts()));   // v7 §21: a long grid moves as one
        LayoutPhraseZ(ms, GlobalClock.BeatsPerBar, dep);   // v7: the melody tracks stay behind the lanes
        // the moved island (and its group, v7 §21: its long grid) follows at once; a neighbour that has to jump (a swap to the other side) slides there
        int g = Islands[island].group;
        var lg = LongGridOf(Islands[island]);
        for (int i = 0; i < Islands.Count && i < ms.Length; i++)
        {
            var kb = Islands[i]; if (kb == null) continue;
            float dz = kb.pz - ms[i].pz;
            if (Mathf.Abs(dz) < 1e-4f) continue;
            MoveIsland(kb, kb.px, ms[i].pz);
            bool mover = i == island || (g != 0 && kb.group == g) || (lg != null && lg.Contains(kb));
            if (!mover && Mathf.Abs(dz) > 0.75f) kb.SlideFrom(new Vector3(0f, 0f, dz), 0.14f, false);
        }
        if (!commit) return;
        islandMove = null;
        AfterLiveLayout();
    }

    /// <summary>Starts an in-column move session for <paramref name="island"/> (IslandDrag calls it when a drag begins): every island's z is
    /// remembered (the push / swap preview starts from there) and the allowed range is fixed.</summary>
    public void BeginIslandMove(int island)
    {
        if (!ValidIsland(island)) return;
        var kb = Islands[island];
        var mv = new IslandMove { island = island, origZ = new float[Islands.Count] };
        for (int i = 0; i < Islands.Count; i++) mv.origZ[i] = Islands[i] != null ? Islands[i].pz : 0f;
        float minFront = kb.FrontEdge, maxBack = kb.BackEdge, d = kb.Depth, g = ProjectConfig.LaneGap;
        int c = kb.column;
        for (int k = ColumnFirst(c); k >= 0 && k < ColumnFirst(c) + ColumnSize(c); k++) { var o = Islands[k]; if (o == null) continue; minFront = Mathf.Min(minFront, o.FrontEdge); maxBack = Mathf.Max(maxBack, o.BackEdge); }
        mv.lo = minFront - 2f * (d + g) + KeyBlock.EdgeInset;
        mv.hi = maxBack + d + 2f * g + KeyBlock.EdgeInset;
        mv.lastZ = kb.pz;
        islandMove = mv;
    }

    /// <summary>Esc / right-click during an in-column move: every island goes back to where the move began (no History).</summary>
    public void CancelIslandMove()
    {
        if (islandMove == null) return;
        var mv = islandMove; islandMove = null;
        if (mv.origZ.Length != Islands.Count) return;
        for (int i = 0; i < Islands.Count; i++) { var kb = Islands[i]; if (kb != null && Mathf.Abs(kb.pz - mv.origZ[i]) > 1e-4f) MoveIsland(kb, kb.px, mv.origZ[i]); }
    }

    /// <summary>True while an in-column move session is live; its island, the allowed z range of its root and the z it was last given.</summary>
    public bool IslandMoveActive => islandMove != null;
    public int IslandMoveIsland => islandMove != null ? islandMove.island : -1;
    public float IslandMoveLo => islandMove != null ? islandMove.lo : 0f;
    public float IslandMoveHi => islandMove != null ? islandMove.hi : 0f;

    class IslandMove { public int island; public float[] origZ; public float lo, hi, lastZ; }
    IslandMove islandMove;

    /// <summary>
    /// Column reorder (an anchor's hub dropped on the route gap): column <paramref name="from"/> moves to position <paramref name="to"/> of the column
    /// list after removal; its islands leave their groups (tear-out: a column cannot stay glued to where it was). Cubes remapped, seek-keeping rebuild
    /// (the lit column keeps its local beat), History.Push.
    /// </summary>
    public void MoveColumn(int from, int to)
    {
        if (MoveColumns(from, 1, to, true)) History.Push();
    }

    bool MoveColumns(int c0, int count, int to, bool tearOut)
    {
        int nc = ColumnCount;
        if (nc < 2 || c0 < 0 || count < 1 || c0 + count > nc) return false;
        to = Mathf.Clamp(to, 0, nc - count);
        if (to == c0) return false;
        var st = SongState.Capture();
        var oldSecs = SectionsOf(st);
        if (tearOut) for (int i = 0; i < st.measures.Length; i++) if (st.measures[i].col >= c0 && st.measures[i].col < c0 + count) st.measures[i].group = 0;
        var cols = new List<int>(nc);
        for (int c = 0; c < nc; c++) if (c < c0 || c >= c0 + count) cols.Add(c);
        for (int k = 0; k < count; k++) cols.Insert(to + k, c0 + k);
        var map = ReorderColumns(st, cols);
        // v7: the moved columns leave their section and join the one they land in (the landing rule); the others keep theirs
        var origin = new int[cols.Count];
        for (int k = 0; k < cols.Count; k++) origin[k] = cols[k] >= c0 && cols[k] < c0 + count ? -1 : cols[k];
        st.sections = RemapSections(oldSecs, origin, SnapshotColumnBars(st.measures)).ToArray();
        FixSections(st);
        RebuildKeepingLit(st, map);
        return true;
    }

    /// <summary>Tray STACK: a new island from <paramref name="md"/> in column <paramref name="col"/> at z = <paramref name="pz"/> (its root), the column's
    /// bars, the column's other islands pushed away. Seek-keeping rebuild, History.Push, rises. -1 (with a shake) when the column is full.</summary>
    public int PlaceIslandInColumn(MeasureData md, int col, float pz)
    {
        if (!HasSong || md == null || col < 0 || col >= ColumnCount) return -1;
        if (LaneCount(col) >= ProjectConfig.MaxLanes && md.kind != 4) { Refuse(col); return -1; }
        var st = SongState.Capture();
        var ms = MeasureState.From(md); ms.group = 0; ms.kind = KindOf(md.kind); ms.pz = pz; ms.placed = true;   // v6 / v7: a keyboard / stairs card keeps its kind
        int at = PlaceStateInColumn(st, ms, col, null);
        if (at < 0) return -1;
        History.Push();
        if (at < Islands.Count && Islands[at] != null) Islands[at].RiseIn();
        return at;
    }

    /// <summary>Tray INSERT: a new island from <paramref name="md"/> as a new column at position <paramref name="insertCol"/> (0 = the front of the song,
    /// ColumnCount = the end; never inside a glued run: it moves past it), in <paramref name="md"/>'s lane when md.placed (its pz) else the neighbouring
    /// anchor's. Seek-keeping rebuild, History.Push, rises. Returns its index.</summary>
    public int PlaceIslandAsColumn(MeasureData md, int insertCol)
    {
        if (!HasSong || md == null) return -1;
        var st = SongState.Capture();
        var ms = MeasureState.From(md); ms.group = 0; ms.kind = KindOf(md.kind);   // v6 / v7: keyboards, stairs, phrases keep their kind
        if (!md.placed)
        {
            var nb = AnchorOf(Mathf.Clamp(insertCol - 1, 0, ColumnCount - 1));
            ms.pz = nb != null ? nb.pz : 0f;
        }
        ms.placed = true;
        int at = PlaceStateAsColumn(st, ms, insertCol, null);
        History.Push();
        if (at >= 0 && at < Islands.Count && Islands[at] != null) Islands[at].RiseIn();
        return at;
    }

    // ------------------------------------------------------------------ v4 helpers
    internal bool ValidIsland(int i) => HasSong && i >= 0 && i < Islands.Count && Islands[i] != null && !Islands[i].IsMoon;

    /// <summary>A refused op: the column's islands shake, a soft thud.</summary>
    internal void Refuse(int col)
    {
        for (int k = ColumnFirst(col); k >= 0 && k < ColumnFirst(col) + ColumnSize(col) && k < Islands.Count; k++) if (Islands[k] != null) Islands[k].Shake();
        AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.2f);
    }

    /// <summary>The root z of <paramref name="add"/> placed LaneGap beyond <paramref name="reference"/>'s back edge (<paramref name="above"/>) or front edge.</summary>
    internal static float LanePz(MeasureState reference, MeasureState add, bool above)
    {
        float front = reference.pz - KeyBlock.EdgeInset, back = front + DepthOfState(reference);
        float d = DepthOfState(add);
        return (above ? back + ProjectConfig.LaneGap : front - ProjectConfig.LaneGap - d) + KeyBlock.EdgeInset;
    }

    /// <summary>Inserts <paramref name="ms"/> into <paramref name="st"/> as the last island of column <paramref name="col"/> (creation order), with the
    /// column's bars; <paramref name="extraCubes"/> land on it; rebuilds keeping the lit column with it pinned (it keeps its z, the others make room).
    /// Returns its final index (-1 when the column is full).</summary>
    internal int PlaceStateInColumn(SongState st, MeasureState ms, int col, List<CubeState> extraCubes)
    {
        if (col < 0 || col >= ColumnCount || (ms.kind != 4 && LaneCount(col) >= ProjectConfig.MaxLanes)) return -1;   // v7: a phrase lives in a melody track (never a lane)
        ms.col = col; if (ms.kind < 2 || ms.kind > 4) ms.kind = 0; ms.placed = true;   // v7: keyboards, stairs and phrases keep their kind
        if (ms.kind != 0) ms.bars = st.measures[colFirst[col]].bars;   // keyboards, stairs and phrases take the column's length
        else ms.bars = Mathf.Clamp(ms.bars, 1, 4);   // v8: a chord grid keeps its OWN length (a normal grid inside a longer column)
        int at = colFirst[col] + colCount[col];
        var map = InsertState(st, ms, at, extraCubes);
        RebuildKeepingLit(st, map, at);
        return NewIndex(at);
    }

    /// <summary>Inserts <paramref name="ms"/> into <paramref name="st"/> as a new column at <paramref name="insertCol"/> (later columns shift right; with
    /// <paramref name="skipGlued"/> a position inside a glued run moves past the run); <paramref name="extraCubes"/> land on it; rebuilds keeping the lit
    /// column with it pinned. Returns its final index.</summary>
    internal int PlaceStateAsColumn(SongState st, MeasureState ms, int insertCol, List<CubeState> extraCubes, bool skipGlued = true, int sectionMode = 0)
    {
        int nc = ColumnCount;
        insertCol = Mathf.Clamp(insertCol, 0, nc);
        if (skipGlued) while (insertCol > 0 && insertCol < nc && Glued(insertCol - 1)) insertCol++;
        EnsureSections(st);   // v7: st.sections describes the columns before the insertion
        foreach (var m in st.measures) if (m.col >= insertCol) m.col++;
        ms.col = insertCol; if (ms.kind < 2 || ms.kind > 4) ms.kind = 0; ms.placed = true;   // v7: keyboards, stairs and phrases keep their kind
        int at = insertCol < nc ? colFirst[insertCol] : st.measures.Length;
        var map = InsertState(st, ms, at, extraCubes);
        JoinNewColumns(st, insertCol, 1, sectionMode);   // v7 (SPEC v7 §13.1): the new column joins the section it lands in (or starts one)
        if (insertCol == 0 && st.measures.Length > 1)
        {
            // a new first column: the existing columns stay where they are, the new one appears on their left (X[0] is the first island's px)
            bool glued = false;
            if (ms.group > 0) foreach (var m in st.measures) if (m.col == 1 && m.group == ms.group) { glued = true; break; }
            bool touch = glued || st.sections == null || System.Array.IndexOf(st.sections, 1) < 0;
            int bars = Mathf.Clamp(ms.bars, 1, 4);
            float w = bars * KeyBlock.IslandWidth, belt;
            if (ms.kind == 2) w = Mathf.Max(w, KeyBlock.KeyboardWidthOf(ms.keyCount, bars));   // v8: a grown piano is wider than its measures
            belt = ms.rewind || ms.kind == 4 ? 0f : (Mathf.Clamp(ms.repeat, 1, ProjectConfig.MaxRepeat) - 1) * (w + ProjectConfig.BeltGap);
            float gap = touch ? 0f : ProjectConfig.SectionGap + Mathf.Max(0, ProjectConfig.SectionBars - bars) * KeyBlock.IslandWidth;
            ms.px = st.measures[1].px - w - belt - gap;
        }
        RebuildKeepingLit(st, map, at);
        return NewIndex(at);
    }

    /// <summary>v7: makes a snapshot's sections explicit (the automatic ones when it has none) before an op edits its columns.</summary>
    internal static void EnsureSections(SongState st) { if (st != null && st.sections == null) st.sections = SectionsOf(st).ToArray(); }

    /// <summary>The final island index of snapshot index <paramref name="stIndex"/> of the last rebuild (through the normalisation's reorder).</summary>
    internal int NewIndex(int stIndex) => lastNormalizeMap == null ? stIndex : (stIndex >= 0 && stIndex < lastNormalizeMap.Length ? lastNormalizeMap[stIndex] : -1);

    /// <summary>Inserts <paramref name="ms"/> at index <paramref name="at"/> of <paramref name="st"/>.measures (island cubes at/after it shift by one;
    /// <paramref name="extraCubes"/> land on it). Returns map[old index] = new index.</summary>
    internal static int[] InsertState(SongState st, MeasureState ms, int at, List<CubeState> extraCubes)
    {
        int nOld = st.measures.Length;
        var list = st.measures.ToList();
        at = Mathf.Clamp(at, 0, list.Count);
        list.Insert(at, ms);
        if (st.cubes == null) st.cubes = new CubeState[0];
        foreach (var c in st.cubes) if (c.moon < 0 && c.measure >= at) c.measure++;
        if (extraCubes != null && extraCubes.Count > 0)
        {
            foreach (var c in extraCubes) c.measure = at;
            st.cubes = st.cubes.Concat(extraCubes).ToArray();
        }
        st.measures = list.ToArray();
        var map = new int[nOld];
        for (int i = 0; i < nOld; i++) map[i] = i < at ? i : i + 1;
        return map;
    }

    /// <summary>Reorders <paramref name="st"/>'s columns: <paramref name="colOrder"/>[new column] = old column; the measures are re-laid column-major,
    /// cols renumbered, island cubes remapped. Returns map[old island index] = new index.</summary>
    static int[] ReorderColumns(SongState st, List<int> colOrder)
    {
        int n = st.measures.Length;
        float x0 = n > 0 ? st.measures[0].px : 0f;   // the song's start stays where it was (X[0] is the first island's px)
        var order = new List<int>(n);
        var newCol = new Dictionary<int, int>();
        for (int k = 0; k < colOrder.Count; k++)
        {
            newCol[colOrder[k]] = k;
            for (int i = 0; i < n; i++) if (st.measures[i].col == colOrder[k]) order.Add(i);
        }
        for (int i = 0; i < n; i++) if (!order.Contains(i)) order.Add(i);   // a stray column (never in a normalised snapshot)
        var map = new int[n]; var ms = new MeasureState[n];
        for (int k = 0; k < n; k++) { map[order[k]] = k; ms[k] = st.measures[order[k]]; int nc; if (newCol.TryGetValue(ms[k].col, out nc)) ms[k].col = nc; }
        st.measures = ms;
        if (n > 0) ms[0].px = x0;
        if (st.cubes != null) foreach (var c in st.cubes) if (c.moon < 0 && c.measure >= 0 && c.measure < n) c.measure = map[c.measure];
        return map;
    }

    /// <summary>The live islands as snapshot states (positions, groups, columns, depths): the input of a live relayout.</summary>
    MeasureState[] LiveStates()
    {
        var ms = new MeasureState[Islands.Count];
        for (int i = 0; i < ms.Length; i++) ms[i] = Islands[i] != null ? MeasureState.From(Islands[i]) : new MeasureState { semis = new[] { 0, 4, 7 }, col = i > 0 && ms[i - 1] != null ? ms[i - 1].col : 0 };
        return ms;
    }

    /// <summary>Moves the live islands to <paramref name="ms"/>'s positions (and groups when <paramref name="groups"/>); islands that moved glide there
    /// over <paramref name="slideSeconds"/> (0 = jump: drag previews). True when anything moved.</summary>
    bool ApplyLive(MeasureState[] ms, float slideSeconds, bool groups)
    {
        bool moved = false;
        for (int i = 0; i < Islands.Count && i < ms.Length; i++)
        {
            var kb = Islands[i]; if (kb == null) continue;
            if (groups && kb.group != ms[i].group) { kb.group = ms[i].group; moved = true; }
            float dx = kb.px - ms[i].px, dz = kb.pz - ms[i].pz;
            if (Mathf.Abs(dx) < 1e-4f && Mathf.Abs(dz) < 1e-4f) continue;
            MoveIsland(kb, ms[i].px, ms[i].pz);
            if (slideSeconds > 0f) kb.SlideFrom(new Vector3(dx, 0f, dz), slideSeconds, false);
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// v4 live relayout (split, tear-out, a column drag's release): the live islands' groups are validated by columns, x laid out (glued columns
    /// touch, others ColumnGap apart), z resolved (<paramref name="pinned"/> keeps its z); islands glide to their places over
    /// <paramref name="slideSeconds"/>; the look, the Route, the Moons, the bounds and OnColumnsChanged follow. No rebuild, no History.
    /// </summary>
    public bool RelayoutLive(int pinned = -1, float slideSeconds = 0.3f)
    {
        if (!HasSong) return false;
        var ms = LiveStates();
        LayoutColumns(ms, pinned, null, new List<int>(SectionStarts()), GlobalClock.BeatsPerBar);
        bool moved = ApplyLive(ms, slideSeconds, true);
        AfterLiveLayout();
        return moved;
    }

    void AfterLiveLayout()
    {
        if (LongGridsMoved()) RecomputeMeasureStarts();   // v7 §21: a grid moved into / out of a lane: the carried windows and the joined look follow
        RefreshGroupLook();
        if (Route != null) Route.Rebuild();
        LayoutMoons(true);   // v6 §11.3
        RefreshBounds();
        RaiseColumnsChanged();
    }

    /// <summary>SPEC v4 §3 K1: a Moon that overlaps an island after a layout slides to the nearest free spot (no History: the edit that caused it pushes).
    /// v6 §11.3: superseded by <see cref="LayoutMoons"/> (every Moon rests in its drum lane); kept for reference, unused.</summary>
    void ClearMoons(bool animate)
    {
        foreach (var moon in Moons)
        {
            if (moon == null) continue;
            var mb = moon.WorldBounds; mb.Expand(ProjectConfig.IslandGap * 2f);
            KeyBlock hit = null; float best = float.MaxValue;
            foreach (var kb in Islands) { if (kb == null || !mb.Intersects(kb.FootprintBounds)) continue; float d = (kb.Center - moon.Center).sqrMagnitude; if (d < best) { best = d; hit = kb; } }
            if (hit == null) continue;
            Vector3 c = moon.Center; c.y = 0f;
            Vector3 dir = c - hit.Center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.back;
            Vector3 spot = FindFreeSpot(c, dir, MoonSize, moon);
            Vector3 dd = spot - c; dd.y = 0f;
            MoveIsland(moon, moon.px + dd.x, moon.pz + dd.z);
            if (animate) moon.SlideFrom(-dd, 0.35f, false);
        }
    }

    // ================================================================== v4 normalisation (SPEC v4 §3 K1, K4): static, on snapshots
    /// <summary>
    /// SPEC v4 §3 K1 normalisation of a snapshot, in place: legacy / v3 files (col -1 everywhere) become one column per island in order with every
    /// pz = the first island's (a clean row; merged groups stay merged); an island without a column gets its own right after the previous island's;
    /// the measures are put in column-major order (island cubes remapped), columns renumbered 0..n-1, bars equalised per column (the max), groups
    /// re-validated (K4: one member per column, consecutive columns, z-aligned), x laid out (X[0] = the first island's px as built, then IslandWidth +
    /// ColumnGap, 0 gap between glued columns) and z resolved (<paramref name="pinned"/>, an index in st.measures before the reorder, keeps its z; -1 =
    /// every column's anchor). Idempotent. Returns map[old index] = new index, or null when the order did not change.
    /// </summary>
    public static int[] NormalizeColumns(SongState st, int pinned = -1)
    {
        if (st == null || st.measures == null || st.measures.Length == 0) return null;
        var ms = st.measures;
        int n = ms.Length;
        for (int i = 0; i < n; i++) if (ms[i] == null) ms[i] = new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7 }, bars = 1 };
        AssignColumns(ms);
        // column-major order, stable within a column (anchor first, then creation order)
        var order = Enumerable.Range(0, n).OrderBy(i => ms[i].col).ThenBy(i => i).ToArray();
        int[] map = null;
        // v7: a renumbering of the columns (a gap closed) moves the section starts with them (a start on a vanished column = the next column)
        var colValues = ms.Select(m => m.col).Distinct().OrderBy(c => c).ToList();
        bool renumber = false; for (int k = 0; k < colValues.Count; k++) if (colValues[k] != k) { renumber = true; break; }
        if (renumber && st.sections != null)
        {
            var remapped = new List<int>();
            foreach (int s0 in st.sections) { int k = colValues.FindIndex(c => c >= s0); if (k >= 0) remapped.Add(k); }
            st.sections = remapped.ToArray();
        }
        bool identity = true; for (int k = 0; k < n; k++) if (order[k] != k) { identity = false; break; }
        if (!identity)
        {
            map = new int[n];
            var sorted = new MeasureState[n];
            for (int k = 0; k < n; k++) { map[order[k]] = k; sorted[k] = ms[order[k]]; }
            st.measures = ms = sorted;
            if (st.cubes != null) foreach (var c in st.cubes) if (c != null && c.moon < 0 && c.measure >= 0 && c.measure < n) c.measure = map[c.measure];
            if (pinned >= 0 && pinned < n) pinned = map[pinned];
        }
        // contiguous columns 0..n-1
        int col = -1, prev = int.MinValue;
        foreach (var m in ms) { if (m.col != prev) { prev = m.col; col++; } m.col = col; }
        // v8 (the user: "grids added above it should not be forced to be extended"): every grid keeps its OWN length and a chord grid may start on a
        // later measure of its column (barOffset); the column is as long as its longest grid's end (≤ 4 measures). A phrase still takes its column's
        // span; keyboards, stairs and Moons start at the column start.
        for (int i = 0; i < n;)
        {
            int j = i, span = 0;
            while (j < n && ms[j].col == ms[i].col)
            {
                var m = ms[j];
                if (m.kind != 4)
                {
                    m.bars = Mathf.Clamp(m.bars, 1, 4);
                    m.barOffset = Offsettable(m.kind) ? Mathf.Clamp(m.barOffset, 0, 4 - m.bars) : 0;   // v9: keyboards and stairs too
                    span = Mathf.Max(span, m.barOffset + m.bars);
                }
                j++;
            }
            if (span == 0) span = Mathf.Clamp(ms[i].bars, 1, 4);
            for (int k = i; k < j; k++) if (ms[k].kind == 4) ms[k].bars = span;
            i = j;
        }
        LayoutColumns(ms, pinned, null, SectionsOf(st), st.beatsPerBar > 0 ? st.beatsPerBar : GlobalClock.BeatsPerBar);
        foreach (var m in ms) { m.placed = true; if (m.kind < 2 || m.kind > 4) m.kind = 0; }   // v6 / v7: keyboards, stairs and phrases keep their kind; a stray Moon in the list becomes a chord island
        return map;
    }

    /// <summary>Legacy conversion and new islands (see NormalizeColumns).</summary>
    static void AssignColumns(MeasureState[] ms)
    {
        int n = ms.Length;
        bool legacy = true; foreach (var m in ms) if (m.col >= 0) { legacy = false; break; }
        if (legacy)
        {
            var first = ms[0];
            if (!first.placed) { first.px = KeyBlock.EdgeInset; first.pz = -(KeyBlock.RowsOf(first.semis) - 1) * ProjectConfig.Spacing * 0.5f; }   // the v1 row's first island
            for (int i = 0; i < n; i++) { ms[i].col = i; ms[i].pz = first.pz; ms[i].placed = true; if (i == 0) ms[i].px = first.px; }
            return;
        }
        for (int i = 0; i < n; i++)
        {
            if (ms[i].col >= 0) continue;
            int c = i > 0 ? ms[i - 1].col + 1 : 0;
            for (int j = 0; j < n; j++) if (j != i && ms[j].col >= c) ms[j].col++;
            ms[i].col = c;
            if (!ms[i].placed) { ms[i].pz = i > 0 ? ms[i - 1].pz : (n > 1 ? ms[1].pz : 0f); ms[i].placed = true; }
        }
    }

    /// <summary>Groups (K4), x and z of a column-major snapshot with contiguous columns (the live relayout uses it on LiveStates). v7: the sections
    /// <paramref name="secs"/> (the first column of each) decide where columns touch; <paramref name="bpb"/> maps a phrase's time to x; phrases take
    /// melody tracks behind their columns' lanes (LayoutPhraseZ).</summary>
    static void LayoutColumns(MeasureState[] ms, int pinned, float[] origZ, IList<int> secs, int bpb)
    {
        ValidateGroups(ms, secs);
        LayoutX(ms, secs, bpb);
        var dep = ResolveZ(ms, pinned, origZ, secs);   // v7 §21: a long grid's grids align in z (one platform, its deepest member's depth)
        LayoutPhraseZ(ms, bpb, dep);
    }

    /// <summary>
    /// K4: a group's members sit in consecutive columns, one per column, z-aligned. A second member in one column leaves the group; a gap in the
    /// columns splits the group into runs; a run of one dissolves; a later run of an id gets a new id; each run's members take the pz of its
    /// west-most member. v6: a keyboard and a chord island never share a run (the family changes = a new run).
    /// </summary>
    static void ValidateGroups(MeasureState[] ms, IList<int> secs = null)
    {
        int n = ms.Length, maxId = 0;
        foreach (var m in ms) if (m.kind == 3 || m.kind == 4) m.group = 0;   // v7: stairs and phrases never merge (SPEC v7 §2.4)
        foreach (var m in ms) if (m.group > maxId) maxId = m.group;
        var ids = new List<int>();
        foreach (var m in ms) if (m.group > 0 && !ids.Contains(m.group)) ids.Add(m.group);
        var run = new List<int>();
        foreach (int g in ids)
        {
            var runs = new List<List<int>>();
            List<int> cur = null;
            for (int i = 0; i < n; i++)
            {
                if (ms[i].group != g) continue;
                if (cur != null && ms[cur[cur.Count - 1]].col == ms[i].col) { ms[i].group = 0; continue; }
                bool boundary = secs != null && secs.Contains(ms[i].col);   // v7: a merge group never crosses a section boundary (the merge op joins the sections first)
                if (cur != null && ms[cur[cur.Count - 1]].col + 1 == ms[i].col && (ms[cur[0]].kind == 2) == (ms[i].kind == 2) && !boundary) cur.Add(i);   // v6: one family per run
                else { cur = new List<int> { i }; runs.Add(cur); }
            }
            bool first = true;
            foreach (var r in runs)
            {
                if (r.Count < 2) { foreach (int i in r) ms[i].group = 0; continue; }
                int id = first ? g : ++maxId;
                first = false;
                float pz = ms[r[0]].pz;
                foreach (int i in r) { ms[i].group = id; ms[i].pz = pz; }
            }
        }
    }

    static bool GluedAt(MeasureState[] ms, int col)
    {
        foreach (var a in ms)
        {
            if (a.col != col || a.group == 0) continue;
            foreach (var b in ms) if (b.col == col + 1 && b.group == a.group) return true;
        }
        return false;
    }

    /// <summary>
    /// x (v7, SPEC v7 §13.2): X[0] = the first island's px as built; a column is its measures wide (bars × IslandWidth) plus its longest belt
    /// ((passes − 1) × (its width + BeltGap) of its islands that ride one: rewind islands and phrases have none, v5 / v7); X[c + 1] = X[c] + that,
    /// + nothing when the two columns TOUCH (the same section, or a merge group) else the ghost measures of a short section (B draws them:
    /// SectionBars − its measures, IslandWidth each) + SectionGap. Every island of a column at X[c]; a phrase at the x of its start in time
    /// (<see cref="XEdgeOfTime"/>: inside a section time maps to x linearly), its first time cell there (PhraseMargins).
    /// </summary>
    static void LayoutX(MeasureState[] ms, IList<int> secs, int bpb)
    {
        int n = ms.Length;
        if (n == 0) return;
        var g = SnapshotGrid.Of(ms, secs, bpb);
        for (int i = 0; i < n; i++)
        {
            var m = ms[i];
            if (m.kind != 4) { m.px = g.x[m.col] + OffsetBars(m) * KeyBlock.IslandWidth; continue; }   // v8: a chord grid at its first measure
            float w, e; KeyBlock.PhraseMargins(out w, out e);
            m.px = g.XEdgeOfTime(m.col, m.phraseOffset / (float)ProjectConfig.TicksPerBeat) + KeyBlock.EdgeInset - w;
        }
    }

    /// <summary>
    /// v7: a snapshot's column grid for the layout — per column its measures, width, belt, passes, start beat, x; the section starts; the x of a song
    /// time. Built from a column-major MeasureState[] with contiguous columns (phrases never set a column's length, passes or belt).
    /// </summary>
    internal sealed class SnapshotGrid
    {
        public int nc; public int bpb;
        public int[] bars, passes; public float[] width, belt, x, start, len; public bool[] touch; public List<int> secs;
        public static SnapshotGrid Of(MeasureState[] ms, IList<int> secsIn, int bpb)
        {
            var gr = new SnapshotGrid();
            int n = ms.Length;
            gr.bpb = Mathf.Max(1, bpb);
            gr.nc = n > 0 ? ms[n - 1].col + 1 : 0;
            int nc = gr.nc;
            gr.bars = new int[nc]; gr.passes = new int[nc]; gr.width = new float[nc]; gr.belt = new float[nc]; gr.x = new float[nc + 1];
            gr.start = new float[nc + 1]; gr.len = new float[nc]; gr.touch = new bool[nc];
            for (int c = 0; c < nc; c++) gr.passes[c] = 1;
            var beltPasses = new int[nc];
            var keysW = new float[nc];   // v8: the widest keyboard of each column (a grown piano is wider than its measures)
            for (int i = 0; i < n; i++)
            {
                var m = ms[i]; int c = m.col;
                if (c < 0 || c >= nc) continue;
                if (m.kind == 4) { if (gr.bars[c] == 0) gr.bars[c] = -Mathf.Clamp(m.bars, 1, 4); continue; }   // a column of phrases only: its phrase's bars (negative = provisional)
                if (gr.bars[c] <= 0) gr.bars[c] = 0;
                gr.bars[c] = Mathf.Max(gr.bars[c], Mathf.Min(4, OffsetBars(m) + Mathf.Clamp(m.bars, 1, 4)));   // v8: the longest grid's end
                if (m.kind == 2) keysW[c] = Mathf.Max(keysW[c], KeyBlock.KeyboardWidthOf(m.keyCount, Mathf.Clamp(m.bars, 1, 4)));
                int rp = Mathf.Clamp(m.repeat, 1, ProjectConfig.MaxRepeat);
                gr.passes[c] = Mathf.Max(gr.passes[c], rp);
                if (!m.rewind) beltPasses[c] = Mathf.Max(beltPasses[c], rp);   // v7: a rewind island rides no belt (time unwinds instead)
            }
            gr.secs = ValidSections(secsIn, nc, gr);
            float w1 = KeyBlock.IslandWidth, t = 0f;
            for (int c = 0; c < nc; c++)
            {
                gr.bars[c] = Mathf.Max(1, Mathf.Abs(gr.bars[c]));
                gr.width[c] = Mathf.Max(gr.bars[c] * w1, keysW[c]);   // v8: room for a grown piano
                gr.belt[c] = Mathf.Max(0, beltPasses[c] - 1) * (gr.width[c] + ProjectConfig.BeltGap);
                gr.len[c] = gr.bars[c] * gr.bpb * gr.passes[c];
                gr.start[c] = t; t += gr.len[c];
            }
            gr.start[nc] = t;
            // X[0] = the first island's px as built (the song's start stays put)
            float x0 = n > 0 ? ms[0].px : 0f;
            for (int i = 0; i < n && ms[i].col == 0; i++) if (ms[i].kind != 4) { x0 = ms[i].px - OffsetBars(ms[i]) * KeyBlock.IslandWidth; break; }
            gr.x[0] = x0;
            for (int c = 0; c < nc; c++)
            {
                bool glued = GluedAt(ms, c);
                bool boundary = c + 1 < nc && gr.secs.Contains(c + 1);
                gr.touch[c] = c + 1 < nc && (glued || !boundary);
                float step = gr.width[c] + gr.belt[c];
                if (!gr.touch[c] && c + 1 < nc)
                {
                    // the end of a section: its ghost measures (a short section still reads as 4 measures, B) + the gap
                    int s0 = 0; foreach (int s in gr.secs) if (s <= c) s0 = s;
                    int sb = 0; for (int k = s0; k <= c; k++) sb += gr.bars[k];
                    step += Mathf.Max(0, ProjectConfig.SectionBars - sb) * w1 + ProjectConfig.SectionGap;
                }
                gr.x[c + 1] = gr.x[c] + step;
            }
            return gr;
        }

        /// <summary>World x of the time <paramref name="beats"/> after column <paramref name="col"/>'s start, measured on the platforms' west edges:
        /// the column the time falls in, its pass (a belt: one slot per pass; rewind / no belt: the passes overlay the platform), the fraction of the
        /// pass × the column's width. Past the song's end it runs on at the last column's scale.</summary>
        public float XEdgeOfTime(int col, float beats)
        {
            if (nc == 0) return 0f;
            int c = Mathf.Clamp(col, 0, nc - 1);
            float l = Mathf.Max(0f, beats);
            while (c < nc - 1 && l >= len[c] - 1e-4f) { l -= len[c]; c++; }
            float pass = bars[c] * bpb;
            int p = Mathf.Clamp(Mathf.FloorToInt(l / pass + 1e-5f), 0, passes[c] - 1);
            float inPass = l - p * pass;
            float e = KeyBlock.EdgeInset;
            return x[c] - e + (belt[c] > 0f ? p * (width[c] + ProjectConfig.BeltGap) : 0f) + inPass / pass * width[c];
        }

        /// <summary>The song beat of time <paramref name="beats"/> after column <paramref name="col"/>'s start.</summary>
        public float Beat(int col, float beats) => (col >= 0 && col < nc ? start[col] : 0f) + beats;
    }

    /// <summary>
    /// v7 (SPEC v7 §14.2): phrases live in MELODY TRACKS behind their columns: a phrase's front edge = LaneGap behind the back-most lane (a
    /// non-phrase island) of every column its time overlaps; phrases that overlap in time take tracks further back (in the order they start:
    /// the first keeps track 0). z only (LayoutX placed their x); every phrase's pz is decided here (a drag in z does not stick).
    /// </summary>
    static void LayoutPhraseZ(MeasureState[] ms, int bpb, float[] dep = null)
    {
        int n = ms.Length;
        List<int> ph = null;
        for (int i = 0; i < n; i++) if (ms[i].kind == 4) { if (ph == null) ph = new List<int>(); ph.Add(i); }
        if (ph == null) return;
        var gr = SnapshotGrid.Of(ms, null, bpb);
        int nc = gr.nc;
        var colBack = new float[nc];
        for (int c = 0; c < nc; c++) colBack[c] = float.MinValue;
        for (int i = 0; i < n; i++) { var m = ms[i]; if (m.kind == 4 || m.col < 0 || m.col >= nc) continue; colBack[m.col] = Mathf.Max(colBack[m.col], FrontOf(m, m.pz) + (dep != null && i < dep.Length ? dep[i] : DepthOfState(m))); }   // v7 §21: a long grid's platform depth
        var s0 = new float[n]; var s1 = new float[n];
        foreach (int i in ph) { s0[i] = gr.Beat(ms[i].col, ms[i].phraseOffset / (float)ProjectConfig.TicksPerBeat); s1[i] = s0[i] + Mathf.Max(1, ms[i].phraseBeats) * Mathf.Clamp(ms[i].repeat, 1, ProjectConfig.MaxRepeat); }
        ph.Sort((a, b) => { int k = s0[a].CompareTo(s0[b]); return k != 0 ? k : a.CompareTo(b); });
        var placed = new List<int>(ph.Count); var back = new float[n];
        float gap = ProjectConfig.LaneGap, d = KeyBlock.PhraseDepth;
        foreach (int i in ph)
        {
            float baseZ = float.MinValue;
            for (int c = 0; c < nc; c++) if (gr.start[c] < s1[i] - 1e-4f && gr.start[c + 1] > s0[i] + 1e-4f && colBack[c] > float.MinValue) baseZ = Mathf.Max(baseZ, colBack[c]);
            if (baseZ == float.MinValue) baseZ = ms[i].col >= 0 && ms[i].col < nc && colBack[ms[i].col] > float.MinValue ? colBack[ms[i].col] : ms[i].pz - KeyBlock.EdgeInset - gap;
            float front = baseZ + gap;
            foreach (int q in placed) if (s0[q] < s1[i] - 1e-4f && s0[i] < s1[q] - 1e-4f) front = Mathf.Max(front, back[q] + gap);
            ms[i].pz = front + KeyBlock.EdgeInset;
            back[i] = front + d;
            placed.Add(i);
        }
    }

    static float DepthOfState(MeasureState m) => m.kind == 4 ? KeyBlock.PhraseDepth : m.kind == 2 ? KeyBlock.KeyboardDepthOf(m.keyCount <= 0 ? ProjectConfig.KeyboardKeys : m.keyCount) : m.kind == 3 ? KeyBlock.StairDepthOf(m.semis) : KeyBlock.DepthOf(KeyBlock.RowsOf(m.semis));   // v9: a stairs island is as deep as a chord grid (its staircase)   // v7: a phrase's depth is its roll's; a long keyboard is a little deeper
    static float FrontOf(MeasureState m, float z) => z - KeyBlock.EdgeInset;
    static float CentreOf(MeasureState m, float z) => z + DepthOfState(m) * 0.5f - KeyBlock.EdgeInset;

    /// <summary>
    /// z resolution (K1): in every column no two islands come closer than LaneGap (front to back). The pinned island's group (the island being edited:
    /// <paramref name="pinned"/>) keeps its z; in a column without it the anchor stays. The others keep their z (<paramref name="origZ"/> when given:
    /// the move session's start) unless they must make room: sorted by centre, they are pushed away from the fixed ones (so an island whose centre the
    /// edited one passed ends up on its other side: the swap). A pushed group member moves its whole group (each member in its own column), which
    /// then stays fixed while the other columns settle.
    /// </summary>
    /// <summary>v8: two grids of a column share a measure (chord grids by their own measures; v9: stairs by their own bars from the column start —
    /// a one-bar run beside one-bar grids only pushes the first measure's; any other kind spans the whole column).</summary>
    static bool SharesMeasures(MeasureState a, MeasureState b)
    {
        int a0 = OffsetBars(a), a1 = Offsettable(a.kind) ? a0 + Mathf.Max(1, a.bars) : 99;
        int b0 = OffsetBars(b), b1 = Offsettable(b.kind) ? b0 + Mathf.Max(1, b.bars) : 99;
        return a0 < b1 && b0 < a1;
    }

    static float[] ResolveZ(MeasureState[] ms, int pinned, float[] origZ, IList<int> secs = null)
    {
        int n = ms.Length;
        if (n == 0) return new float[0];
        var z = new float[n];
        for (int i = 0; i < n; i++) z[i] = origZ != null && i < origZ.Length && i != pinned ? origZ[i] : ms[i].pz;
        // v7 §21.2: the long grids (from where the grids stood: a move session's start) — each one unit with its merge groups: its grids take the
        // west-most member's z (the pinned unit: the pinned island's), are pushed together, and stand as deep as their deepest member
        var zd = new float[n];
        for (int i = 0; i < n; i++) zd[i] = origZ != null && i < origZ.Length ? origZ[i] : ms[i].pz;
        float[] dep;
        var unit = SnapshotUnits(ms, zd, secs, out dep);
        for (int i = 0; i < n; i++)
        {
            if (unit[i] <= 0) continue;
            int w = -1; for (int k = 0; k < n; k++) if (unit[k] == unit[i]) { w = k; break; }   // column-major: the first is the west-most
            if (w >= 0 && w != i) z[i] = z[w];
        }
        var fixedUnits = new HashSet<int>();
        if (pinned >= 0 && pinned < n)
        {
            fixedUnits.Add(unit[pinned]);
            for (int i = 0; i < n; i++) if (unit[i] == unit[pinned]) z[i] = ms[pinned].pz;
        }
        var cols = new List<int[]>();   // [first, count] per column
        for (int i = 0; i < n;) { int j = i; while (j < n && ms[j].col == ms[i].col) j++; cols.Add(new[] { i, j - i }); i = j; }
        var idx = new List<int>(8);
        var moved = new Dictionary<int, float>();
        float gap = ProjectConfig.LaneGap, eps = 1e-4f;
        for (int iter = 0; iter < 8; iter++)
        {
            moved.Clear();
            foreach (var cr in cols)
            {
                int a = cr[0], cnt = cr[1];
                if (cnt < 2) continue;
                idx.Clear();
                for (int k = a; k < a + cnt; k++) if (ms[k].kind != 4) idx.Add(k);   // v7: phrases live in melody tracks (LayoutPhraseZ), not in the lanes
                if (idx.Count < 2) continue;
                idx.Sort((p, q) => { int c = (z[p] + dep[p] * 0.5f).CompareTo(z[q] + dep[q] * 0.5f); return c != 0 ? c : p.CompareTo(q); });   // (centres: the EdgeInset cancels)
                bool anyPin = false; foreach (int k in idx) if (fixedUnits.Contains(unit[k])) { anyPin = true; break; }
                int a0 = idx[0]; foreach (int k in idx) if (k < a0) a0 = k;
                Func<int, bool> isPin = k => anyPin ? fixedUnits.Contains(unit[k]) : k == a0;
                int lo = -1, hi = -1;
                for (int p = 0; p < idx.Count; p++) if (isPin(idx[p])) { if (lo < 0) lo = p; hi = p; }
                // v8: only grids that share a measure push each other (two short grids on different measures of a long column may share a lane)
                for (int p = lo + 1; p < idx.Count; p++)
                {
                    int k = idx[p]; if (isPin(k)) continue;
                    float minFront = float.NegativeInfinity;
                    for (int qq = p - 1; qq >= lo && qq >= 0; qq--) { int pr = idx[qq]; if (SharesMeasures(ms[pr], ms[k])) minFront = Mathf.Max(minFront, FrontOf(ms[pr], z[pr]) + dep[pr] + gap); }
                    if (minFront > float.NegativeInfinity && FrontOf(ms[k], z[k]) < minFront - eps) { z[k] = minFront + KeyBlock.EdgeInset; if (unit[k] > 0) moved[unit[k]] = z[k]; }
                }
                for (int p = hi - 1; p >= 0; p--)
                {
                    int k = idx[p]; if (isPin(k)) continue;
                    float maxBack = float.PositiveInfinity;
                    for (int qq = p + 1; qq <= hi && qq < idx.Count; qq++) { int nx = idx[qq]; if (SharesMeasures(ms[nx], ms[k])) maxBack = Mathf.Min(maxBack, FrontOf(ms[nx], z[nx]) - gap); }
                    float back = FrontOf(ms[k], z[k]) + dep[k];
                    if (maxBack < float.PositiveInfinity && back > maxBack + eps) { z[k] = maxBack - dep[k] + KeyBlock.EdgeInset; if (unit[k] > 0) moved[unit[k]] = z[k]; }
                }
            }
            if (moved.Count == 0) break;
            foreach (var kv in moved)
            {
                fixedUnits.Add(kv.Key);
                for (int i = 0; i < n; i++) if (unit[i] == kv.Key) z[i] = kv.Value;
            }
        }
        for (int i = 0; i < n; i++) ms[i].pz = z[i];
        return dep;
    }

    /// <summary>
    /// v7 §21.2: the layout's units of a column-major snapshot — merge groups and LONG GRIDS (each carrying island with the grids its carry reaches
    /// along its lane: the snapshot twin of <see cref="LaneNext"/> / <see cref="CarryTargets(int)"/>, lanes judged at <paramref name="z"/>, sections
    /// <paramref name="secs"/> (null: one section)) joined: per island a unit id &gt; 0 shared by its unit's members, or −(i + 1) alone; and
    /// <paramref name="dep"/>: its platform depth (a long grid's member: the long grid's deepest).
    /// </summary>
    static int[] SnapshotUnits(MeasureState[] ms, float[] z, IList<int> secs, out float[] dep)
    {
        int n = ms.Length;
        dep = new float[n];
        var parent = new int[n];
        for (int i = 0; i < n; i++) { parent[i] = i; dep[i] = DepthOfState(ms[i]); }
        Func<int, int> find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
        Action<int, int> join = (a, b) => { int ra = find(a), rb = find(b); if (ra != rb) parent[Mathf.Max(ra, rb)] = Mathf.Min(ra, rb); };
        // merge groups
        for (int i = 0; i < n; i++) if (ms[i].group > 0) for (int k = i + 1; k < n; k++) if (ms[k].group == ms[i].group) { join(i, k); break; }
        // long grids: the lane links (next column, same section, same family, the largest overlap, mutual best), then each carry's chain
        var next = SnapshotLaneNext(ms, z, secs, dep);
        var runMember = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int k = i, steps = Mathf.Clamp(ms[i].carry, 0, ProjectConfig.MaxCarry);
            if (LaneFamily(ms[i]) < 0) continue;
            for (int s = 0; s < steps; s++) { int j = next[k]; if (j < 0) break; join(i, j); runMember[i] = runMember[j] = true; k = j; }
        }
        // the long grids' depth: their deepest member's (a group without a long grid keeps its own depths)
        var deep = new Dictionary<int, float>();
        for (int i = 0; i < n; i++) if (runMember[i]) { int r = find(i); float d; deep[r] = deep.TryGetValue(r, out d) ? Mathf.Max(d, dep[i]) : dep[i]; }
        var size = new int[n];
        for (int i = 0; i < n; i++) size[find(i)]++;
        var unit = new int[n];
        for (int i = 0; i < n; i++)
        {
            int r = find(i);
            unit[i] = size[r] > 1 ? r + 1 : -(i + 1);
            float d; if (runMember[i] && deep.TryGetValue(r, out d)) dep[i] = d;
        }
        return unit;
    }

    /// <summary>v7 §21.2: <see cref="LaneNext"/> on a snapshot (island indices; -1 none): front edges from <paramref name="z"/>, depths
    /// <paramref name="dep"/> (each island's own), a section boundary before every column in <paramref name="secs"/>.</summary>
    static int[] SnapshotLaneNext(MeasureState[] ms, float[] z, IList<int> secs, float[] dep)
    {
        int n = ms.Length;
        var east = new int[n]; var west = new int[n]; var next = new int[n];
        for (int i = 0; i < n; i++) { east[i] = west[i] = next[i] = -1; }
        for (int i = 0; i < n; i++)
        {
            int f = LaneFamily(ms[i]);
            if (f < 0) continue;
            float be = 0f, bw = 0f;
            for (int k = 0; k < n; k++)
            {
                if (k == i || LaneFamily(ms[k]) != f) continue;
                int dc = ms[k].col - ms[i].col;
                if (dc != 1 && dc != -1) continue;
                int later = dc == 1 ? ms[k].col : ms[i].col;
                if (secs != null && secs.Contains(later)) continue;   // never across a section boundary
                float ov;
                if (!LaneMates(z[i] - KeyBlock.EdgeInset, dep[i], z[k] - KeyBlock.EdgeInset, dep[k], out ov)) continue;
                if (dc == 1) { if (east[i] < 0 || ov > be + 1e-4f) { east[i] = k; be = ov; } }
                else { if (west[i] < 0 || ov > bw + 1e-4f) { west[i] = k; bw = ov; } }
            }
        }
        for (int i = 0; i < n; i++) if (east[i] >= 0 && west[east[i]] == i) next[i] = east[i];
        return next;
    }
}

/// <summary>A removed island's body: sinks 7 u over 0.4 s, then is destroyed.</summary>
public class SinkingHusk : MonoBehaviour
{
    float t; Vector3 start;
    void Start() { start = transform.position; }
    void Update()
    {
        t += Time.deltaTime / 0.4f;
        if (t >= 1f) { Destroy(gameObject); return; }
        transform.position = start + Vector3.down * (7f * Ease.InQuad(t));
    }
}
