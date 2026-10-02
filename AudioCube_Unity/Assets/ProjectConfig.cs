using UnityEngine;

public static class ProjectConfig
{
    // island grid
    public const int numInversions = 6;        // columns per island (each column lifts the lowest chord tone an octave)
    public const float Spacing = 1.12f;        // tile pitch
    public const float TileSize = 1.0f;
    public const float TileThickness = 0.30f;
    public const float PitchRise = 0.045f;     // world units of tile lift per semitone (terrain look)
    public const float gridSpacing = 3.4f;     // gap between islands (legacy row layout)
    public const float PlatformPad = 0.85f;
    public const float PlatformThickness = 0.55f;

    // v2 world (SPEC §2.1, §2.2, §8.4)
    public const float IslandSnap = 0.5f;      // drag snap grid
    public const float IslandGap = 1.5f;       // minimum clearance between islands (push-out)
    public const float CableSag = -1.8f;       // y of the Route cable at a segment's midpoint
    public const float HubRadius = 0.7f;       // hub prism circumradius
    public const float HubHeight = 0.5f;       // hub prism height
    public const float DropOnWireDist = 1.5f;  // hub-to-cable distance that opens a Route gap while dragging
    public const float OctaveRise = 0.35f;     // cube float per octave (consumed by the audio package)
    public const float MoonRadius = 3.9f;      // Moon disc platform radius
    public const int MoonCols = 6, MoonRows = 4;

    // v3 islands (SPEC v3 §3): merge / split / the group look / the comet over a group
    public const float MergeReach = 1.4f;       // edge-to-edge distance that opens the merge zone while dragging
    public const float MergeOverlap = 0.4f;     // minimum front-back overlap (fraction of the shallower platform) for a merge
    public const float SplitGap = ColumnGap;    // the right part of a split group slides this far east (v4: the columns unglue, the gap opens)
    public const float SeamBridgeWidth = 0.6f;  // the box that fills the rounded-corner notch of a seam
    public const float RimWidth = 0.08f;        // member outline width (the chord divide)
    public const float RimInset = 0.12f;        // outline inset from the platform edge: at a seam the two lines run 2 x inset apart
    public const float SeamGlideHeight = 0.35f; // the comet glides over a seam this high above the platform
    public const float LapInset = 0.42f;        // group members: the comet laps along the platform margin, this far inside the edge

    // v4 columns + per-note length (SPEC v4 §2.2)
    public const float ColumnGap = 3.7f;        // x gap between two unglued columns (v3's 12 u island pitch)
    public const float LaneGap = 1.8f;          // minimum z clearance between two islands of one column
    public const int MaxLanes = 4;              // islands per column
    public const float RegisterRise = 1.2f;     // v4 static platform lift per register octave (v6: replaced by the tower below; kept for old callers)
    public const int TicksPerBeat = 24;         // note lengths in ticks: 6 = 16th, 12 = 8th, 24 = quarter, 48 = half, 96 = whole; x1.5 = dotted
    public const float HopMaxBeats = 0.5f;      // a hop between two notes never takes longer than this (a long note sits, then hops)
    public static readonly float[] SizeTable = { 0.62f, 0.78f, 1f, 1.24f, 1.5f };   // cube size for a 16th, 8th, quarter, half, whole note
    // v4 column look (SPEC v4 §3 K3, package K)
    public const float TrackMargin = 0.55f;     // the beat track runs this far outside the platform edge (its centre line)
    public const float TrackWidth = 0.16f;      // width of the beat track band
    public const float BandPadZ = 1f;           // a column band reaches this far past its top / bottom island
    public const float BandPadX = 0.6f;         // and this far past the platforms on each side (IslandWidth + 1.2 wide)
    public const float SatelliteScale = 0.6f;   // the comets of the other islands of the lit column
    public const float RegisterSpring = 0.35f;  // seconds of the springy rise / sink when an island is made higher / lower
    // v5 repeat = passes on a conveyor belt (SPEC v5 §2.5, package K)
    public const int MaxRepeat = 4;             // passes an island can play (the header's repeat button cycles 1..4)
    public const int MaxCarry = 3;              // v6: next chords an island's patterns can be carried onto (the header's carry button cycles 0..3)
    public const int KeyboardKeys = 25;         // v6: keys of a keyboard island (two octaves, chromatic, from the key's tonic); v7 §20.1: a new keyboard's
    public const int KeyboardMinKeys = 13;      // v7 §20.1: a keyboard extends / shrinks by octaves between one octave (13 keys) ...
    public const int KeyboardMaxKeys = 61;      // ... and five octaves (61 keys)
    public const float KeyRiseTotalMax = 0.6f;  // v8: 0.72 → 0.3 ("shouldnt be as tall") → 0.6 ("taller, almost how it was before") // v7 §20.1: the keys' gentle rise to the right never totals more than this (a long keyboard rises less per key)
    public const float BeltGap = 0.6f;          // gap between two slots of a belt: slot pitch = IslandWidth + BeltGap
    public const float JerkLead = 0.3f;         // beats of the belt's jerk before a pass boundary (accelerate, travel, overshoot) ...
    public const float JerkSettle = 0.1f;       // ... and of its settling clunk after the boundary
    public const float BeltGlideBeats = 1.5f;   // the glide back to slot 0 after an island's last pass
    public const float BeltGlideLift = 0.45f;   // the island hops this high while it glides back
    public const float BeltTopY = -0.74f;       // island-local y of the belt's top face (just under the beat track, the platform floats on it)
    // v6 the octave tower (SPEC v6 §3.3, package K): a raised island floats a hint higher at rest and rises on its turn, then sinks slowly
    public const float TowerRestLift = 0.3f;    // lift per octave at rest (the hint that it will rise)
    public const float TowerRise = 3.2f;        // lift per octave on its turn (a raised island); v7 §12.3: 60 % taller (was 2.0)
    public const float TowerDip = 0.8f;         // dip per octave on its turn (a lowered island; never into the sea)
    public const float TowerLead = 0.5f;        // beats before its column starts that the rise begins ...
    public const float TowerRiseEnd = 0.25f;    // ... reaching the top this many beats after the start (an ease-out with a small overshoot)
    public const float TowerSinkBeats = 3f;     // the slow sink back to rest after its last pass (ease-in-out)
    public const float TowerBob = 0.05f;        // the slight bob on every beat while it is up (per octave)

    // v7 (SPEC v7 §2.2): stairs, grounds, rewind, fling, octave layers, and the hold-then-return rule (§12)
    public const int StairMinSteps = 3;         // a stairs island's steps (the header's − n + stays inside)
    public const int StairMaxSteps = 8;
    public const float StairStepRise = 0.34f;   // world units per step; a stair of n steps moves the next columns' ground n x this
    public const float GroundMin = -1.6f;       // the ground never sinks below this (the sea is at -4) ...
    public const float GroundMax = 4.8f;        // ... nor climbs above this
    public const float GroundGlideSeconds = 0.6f;   // islands glide to a new ground (ease in-out)
    public const float RewindBeats = 0.5f;      // the unwind at the end of a pass (rewind style)
    public const float FlingBeats = 0.75f;      // a flung cube's flight to the next grid (lands on the next downbeat)
    public const float LayerRise = 2.3f;        // the sky layer: this far above the platform per layer ...
    public const float LayerBack = 0.55f;       // ... and this far back (+z)
    public const float LayerDrop = 1.6f;        // the deep layer: this far below the platform per layer ...
    public const float LayerFront = 0.9f;       // ... and this far toward the camera (-z), so it peeks out under the front edge
    public const int MaxLayer = 2;              // octave layers -2..+2
    public const float ReturnBeats = 0.75f;     // §12: a cube's trip home (from where its turn ended) before it plays again / after a reset
    public const float ResetStaggerBeats = 0.6f;    // §12: after a reset things go home slightly out of sync (0..this, by a hash)
    public const int PhraseRows = 15;           // §14: a melody phrase's rows (the key's scale over two octaves)
    public const float PhraseRowPitch = 0.46f;  // §14: z between two phrase rows
    public const int PhraseMaxBeats = 32;       // §14: the longest phrase (8 measures of 4/4)
    public const int SectionBars = 4;           // §13: measures in a section (new columns join a section until it has this many)
    public const float SectionGap = ColumnGap;  // §13: the gap between two sections (inside a section the columns touch)
    public const float TowerPreviewHold = 1f;   // beats a stopped preview (▲ / ▼) stays up before it sinks
    public const float TowerMaxSlope = 26f;     // lift change per beat per octave the pure lift can make: a faster change is a jump (seek, stop, undo) and glides (v7: the taller rise peaks at ≈ 16.5)
    // v6 the keyboard island (SPEC v6 §3.1, package K): a piano of KeyboardKeys chromatic keys on an IslandWidth x DepthOf(1) platform
    public const float KeyPadX = 0.5f;          // the piano's cheeks (left / right margins)
    public const float KeyPadFront = 0.2f;      // the lip in front of the keys
    public const float KeyPadBack = 0.62f;      // the back rail behind the keys
    public const float KeyGap = 0.045f;         // gap between two white keys
    public const float BlackKeyWidth = 0.6f;    // black key width (fraction of the white key pitch)
    public const float BlackKeyLength = 0.62f;  // black key length (fraction of the white key length; set back against the rail)
    public const float BlackKeyRaise = 0.15f;   // v8: 0.16 → 0.1 → 0.15   // a black key's top stands this far above its white neighbours'
    public const float KeyRise = 0.02f;         // world units a key rises per semitone (pitch reads as a gentle rise to the right)

    // cube mechanics
    public const float CubeSize = 0.62f;
    public const float CubeGap = 0.035f;
    public const float CubeDropHeight = 3.0f;
    public const float CubeHoverHeight = 0.0f;
    public const float snapThreshold = 0.55f;  // fraction of a step the cube rests before hopping
    public const float cubeHopIntensity = 0.42f;

    // audio
    public const int refMIDI = 60;             // samples are recorded at C4
    public const float refFreq = 261.63f;
    public const float MaxSystemVolume = 0.8f;
    public const float SynthMasterGain = 2.2f;  // linear gain in front of the SoundFont engine's limiter

    // legacy (kept for old grid manager)
    public const int Cols = 12;
    public const int Rows = 7;
    public static readonly int[,] baseSemitoneVariations = new int[7, 12]
    {
        {20, 24, 26, 27, 31, 32, 31, 27, 26, 24, 22, 19},
        {17, 20, 22, 24, 27, 29, 27, 24, 22, 20, 19, 15},
        {12, 15, 17, 19, 22, 24, 22, 19, 17, 15, 14, 10},
        {8, 12, 14, 15, 19, 20, 19, 15, 14, 12, 10, 7},
        {5, 8, 10, 12, 15, 17, 15, 12, 10, 8, 7, 3},
        {0, 3, 5, 7, 10, 12, 10, 7, 5, 3, 2, -2},
        {-2,  2, -3, -2, -1, 0, 2, -5, 0, -2, -3, -7}
    };
}
