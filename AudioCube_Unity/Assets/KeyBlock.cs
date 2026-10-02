using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One chord "island" (kind 0) or drum "Moon" (kind 1): a floating platform carrying a terrain of pitch tiles.
/// Rows are chord tones (low to high), columns are inversions. The platform is tinted by the chord root, a glyph
/// on the front edge shows the chord quality, a hub prism (sides = bars + 2) at the front-centre is the island's
/// handle / comet dock / cable anchor, and a segmented beat ring under the platform shows where every cube's hits
/// land (SPEC §2.1, §2.5). Moons are round, carry a 6 x 4 kit grid, a disc handle and a per-bar ring pointer (§3.4).
/// v3 (SPEC v3 §3.3): a member of a merged group wears a glowing rim outline in its chord colour (two lines side by side at
/// a seam = the chord divide) and bridges the seam to its east neighbour; islands can slide into place (merge, split) and
/// rise in with a delay (the wand).
/// v4 (SPEC v4 §3 K3/K5): islands stand in columns (SongManager lays them out); only a column's anchor wears the hub (route anchor);
/// the beat ring became a rounded-rectangle beat TRACK hugging the platform (16th segments evenly by arc length from the front centre,
/// clockwise); an island's register lifts / sinks the platform RegisterRise per octave with a springy ease and re-pitches its tiles; a
/// sleeping (toggled off) island reads clearly off (desaturated platform, dimmed tiles, a closed-eye glyph); a refused op shakes it.
/// The hub is the column's progress GAUGE (the user, 2026-09-29: "it should just fill gradually until it reaches the end of measure"): a dim
/// ink shell whose top face fills with the chord colour (spreading from its centre, area = the level) from empty at the column's start to full
/// exactly at its end (a pure function of the song beat: seeks, loops and the focus loop follow), faint outlines mark the bars, a restart drains
/// it quickly; nothing shoots up from it.
/// v5 (SPEC v5 §2.1 / §2.5, package K): an island with repeat ≥ 2 plays that many PASSES of its column and stands at the head of a conveyor
/// belt (DeaCube/Belt.cs, a child that counters the ride): while playing it rides the belt (SongManager.RideSlot, a pure function of the song
/// beat, applied in ApplyPose so the tiles, cubes, comet and cable ride along; a jump — a seek, a stop, an undo — glides there instead); its track
/// playhead runs once per pass; the anchor's gauge fills over the whole column (a notch per pass). The song key's home island wears a small
/// house beside its vibe glyph.
/// v6 (SPEC v6 §3, package K): a KEYBOARD island (kind 2 — the user: "a keyboard style grid that just has one row (like a piano) for actual
/// melody") is a piano: 25 chromatic keys from the song key's tonic (its lowest key is stored as the root), real key geometry (white keys long
/// and ivory, black keys shorter, raised, set back, ink-dark: 15 white keys over two octaves from a white key, 14 from a black one), a gentle rise
/// to the right (pitch still reads as height), a tiny house on the key's tonic keys, off-scale keys a little dimmer and a soft dot in the
/// column's chord colour on its chord tones (the safe notes, <see cref="RefreshKeyDots"/>), on a wood-ink case (cheeks, a back rail) exactly
/// as wide as a chord island. The register lift became the octave TOWER: a raised island floats only a hint higher at rest and rises on its turn
/// (SongManager.TowerLiftAt, a pure function of the song beat; a jump — seek, stop, undo — glides there), a lowered one dips; ▲ / ▼ while stopped
/// plays the rise and the slow sink once. Chord islands wear their JOB badge (Harmony.Job; U1's JobBadge art) on the front-left corner; the job-1
/// badge is a house, so the v5 home sticker only remains on a home island doing another job.
/// v6 §11: a Moon's column is its START column; it rests in line with it (SongManager.LayoutMoons), pulses / laps its bead only while its SECTION
/// plays, and follows the lit column meanwhile (SongManager.MoonFollowX: a pure function of the beat applied like the belt ride; a jump glides).
/// </summary>
[DefaultExecutionOrder(-20)]   // v5: the island is posed (its belt ride) before the cubes, the comet and the cable read where it is this frame
public partial class KeyBlock : MonoBehaviour
{
    public int measureIndex;
    public string assignedChord;
    public int chordRootMIDI;
    public int chordHeight;
    public List<int> semitoneList = new List<int>();
    public int bars = 1;
    public int barOffset;   // measures from its column's start (a shorter grid inside a longer column); 0 = at the column start
    public float startBeatOffset;
    // v2 data (world position = SetPosition / basePos; the transform adds the drag lift and the rise animation)
    public bool placed;          // true once the user placed/moved this island (else the legacy row layout is used)
    public int kind;             // 0 chord island, 1 Moon, 2 keyboard (v6), 3 stairs (v7)
    public int mood;             // 0 as generated, 1 sun, 2 cloud, 3 storm
    public int energy = 2;       // 0..3
    public bool fill, sleep;
    public int repeat = 1;       // 1..4
    public int[] storedSemis;    // the chord as saved (semitoneList may be a mood-mapped view of it)
    // v3 data (SPEC v3 §3.3, §4.3)
    public int group;            // merge group id (0 none)
    public int fall;             // presentation fall pattern 0..5
    // v4 columns (SPEC v4 §2.3): written by SongManager's layout
    public int column;           // the column this island plays in (all islands of a column sound together)
    public int register;         // octave shift of the whole island (-2..+2): its tiles sound register x 12 higher and the platform sits higher
    public int carry;            // v6: its patterns also play on the next `carry` chords (0..3; SongManager.CarryTargets)
    // v7 (SPEC v7 §2.1): stairs (kind 3), the carry style, the repeat style, vary, launch
    public int stairType;        // 0 chord, 1 scale, 2 spark, 3 slide, 4 bright, 5 walk (Harmony.Stairs)
    public int stairDir = -1;    // -1 falls, +1 climbs
    public int stairSteps = 4;   // 3..8
    public int stairRate = 12;   // ticks per step (6, 8, 12, 24)
    public bool stairLead = true;// the run ends at its column's end (a lead-in into the next chord); false: it starts with the column
    public int carryStyle;       // 0 hop (v6 carry), 1 flow
    public bool rewind;          // repeat style: time unwinds at every pass boundary (no belt)
    public int vary;             // 0 same, 1 vary, 2 answer
    public bool launch;          // its cubes are flung to the next grid at the end of its turn
    public bool lead;            // v9 (L): the stage lights spotlight this grid while it sounds (SongManager.IsLead: + every keyboard, stairs, phrase)
    public int phraseOffset;     // phrase (kind 4): ticks from its column's start (half-measure steps)
    public int phraseBeats = 4;  // phrase: its length in beats (2 = ½ measure .. 32)
    public int phraseGrid = 12;  // phrase: ticks per time cell (12 eighths, 6 sixteenths)
    public int secRole;          // v7 §17.1: a section's name, read from the first island of its first column (0 none, 1 intro … 6 outro)
    public int keyCount = 25;    // v7 §20.1: a keyboard's keys from its lowest key (chordRootMIDI): 13 (an octave) .. 61 (five octaves)
    /// <summary>v9 (G): a Moon's own drum kit — kit[row] = the GM drum key of each row, kit[rows + row] = that row's column-5 alternate (0: none);
    /// null = the standard rows (VoiceRules.DrumPiece).</summary>
    public int[] kit;
    /// <summary>v7 §20.1: the keys of a keyboard island (keyCount clamped to KeyboardMinKeys..KeyboardMaxKeys; 25 when unset).</summary>
    public int KeyCount => Mathf.Clamp(keyCount <= 0 ? ProjectConfig.KeyboardKeys : keyCount, ProjectConfig.KeyboardMinKeys, ProjectConfig.KeyboardMaxKeys);
    public bool isAnchor = true; // the first island of its column (route hub, comet lap)
    public int Column => column;
    public bool IsAnchor => isAnchor;
    /// <summary>v5 (SPEC v5 §2.5): passes this island plays (its repeat, 1..MaxRepeat); it rests in its column's later passes.</summary>
    public int Passes => Mathf.Clamp(repeat, 1, ProjectConfig.MaxRepeat);
    /// <summary>v5: passes of this island's column (the max repeat of its islands; set with the timeline).</summary>
    public int ColumnPasses => colPasses;
    /// <summary>v5: one slot of a conveyor belt along x (IslandWidth + BeltGap): pass p plays p slots east of home. v7: a one-measure island's; a
    /// wider island's slot is its own width + BeltGap (<see cref="BeltPitch"/>).</summary>
    public static float SlotPitch => IslandWidth + ProjectConfig.BeltGap;
    /// <summary>v7: this island's belt slot (its width + BeltGap: a 2-measure island moves two measures per pass).</summary>
    public float BeltPitch => RowWidth + ProjectConfig.BeltGap;   // v9: a grid shorter than its column rides the column's slot

    /// <summary>v9 (per-grid measures: a 1-bar grid at barOffset 2 of a repeated 4-bar column): the width of the row it rides with — its column's
    /// (the pass length: every grid of a repeated column moves one column per pass, its offset kept inside the slot); its own when it is the
    /// column's length (every song before v9) or when the song is not laid out yet.</summary>
    public float RowWidth
    {
        get
        {
            var sm = SongManager.I;
            // only a repeated island rides a slot, and only when the column tables already describe it (during a rebuild they are still the
            // previous song's until RefreshColumns runs): else its own width
            if (sm == null || IsMoon || !HasBelt || column < 0 || column >= sm.ColumnCount) return Width;
            int at = sm.Islands.IndexOf(this), f = sm.ColumnFirst(column), n = sm.ColumnSize(column);
            if (at < 0 || f < 0 || at < f || at >= f + n || f + n > sm.Islands.Count) return Width;
            return Mathf.Max(Width, sm.ColumnWidth(column));
        }
    }
    /// <summary>v9: island-local x of its column's west edge (−EdgeInset for an island at the column's start).</summary>
    public float ColumnWestLocal { get { var sm = SongManager.I; return sm != null && column >= 0 && column < sm.ColumnCount ? sm.ColumnWestEdge(column) - px : -EdgeInset; } }
    /// <summary>v9: true when this island draws its row's belt — the island at the smallest barOffset among the column's belted islands in its lane
    /// (|Δpz| &lt; 0.5); a full-width island always draws its own.</summary>
    public bool BeltRowLeader
    {
        get
        {
            var sm = SongManager.I;
            if (sm == null || RowWidth <= Width + 1e-3f) return true;
            foreach (var o in sm.Islands)
            {
                if (o == null || o == this || o.column != column || !o.HasBelt || Mathf.Abs(o.pz - pz) >= 0.5f) continue;
                if (o.barOffset < barOffset || (o.barOffset == barOffset && sm.Islands.IndexOf(o) < sm.Islands.IndexOf(this))) return false;
            }
            return true;
        }
    }
    /// <summary>v7: true when this island rides a conveyor belt (repeat ≥ 2; never a Moon, a phrase or a REWIND island — time unwinds instead).</summary>
    public bool HasBelt => !IsMoon && !IsPhrase && !rewind && Passes >= 2;
    /// <summary>v5: where the island stands on its belt this frame, in slots (0 = home; follows SongManager.RideSlot).</summary>
    public float RideSlotShown => rideShown;
    /// <summary>v5: the ride's world offset (x = slots × BeltPitch, y = the hop of the glide back).</summary>
    public Vector3 BeltOffset => new Vector3(rideShown * BeltPitch, rideLift, 0f);
    /// <summary>v5: true when the ride moved the island this frame (the Route refits its cable).</summary>
    public bool Riding { get; private set; }
    /// <summary>v5: the conveyor belt (null without one).</summary>
    public Belt Belt => belt;
    /// <summary>v5: the belt's shown length in slots (1 = no belt).</summary>
    public float BeltSlotsShown => belt != null ? belt.SlotsShown : 1f;
    /// <summary>v5: the small house beside the vibe glyph on the song key's home island (null for Moons).</summary>
    public Transform HomeGlyph => homeGlyph;
    public bool HomeShown => homeGlyph != null && homeGlyph.gameObject.activeSelf;
    /// <summary>The platform's register lift shown this frame (world units). v6: the octave tower's lift (<see cref="TowerLift"/>).</summary>
    public float LiftY => regShown;
    /// <summary>True while the register lift moved this frame (v6: the tower rising / bobbing / sinking, a glide; the Route refits the hub cable).</summary>
    public bool Lifting { get; private set; }
    /// <summary>True while the sleep look is shown (desaturated platform, dimmed tiles, closed eye).</summary>
    public bool SleepShown => sleepShown;
    /// <summary>The closed-eye glyph of a sleeping island (null for Moons).</summary>
    public Transform SleepGlyph => eye;

    // legacy prefab fields (unused by the procedural build)
    public Transform tilesContainer;
    public GameObject noteTemplate;
    public Transform cubesContainer;

    public int cols, rows;
    public readonly List<TileInteraction> tiles = new List<TileInteraction>();
    TileInteraction[,] grid;
    public Color chordColor = Color.white;
    public ChordQuality quality;
    public bool IsMinor => MusicTheory.IsMinor(semitoneList);
    public bool IsMoon => kind == 1;
    /// <summary>v6 (SPEC v6 §2.5): a keyboard island — one row of piano keys for melody (kind 2).</summary>
    public bool IsKeyboard => kind == 2;
    /// <summary>v7 (SPEC v7 §2.4): a stairs island — one row of steps, a run that falls / climbs into the next chord (kind 3).</summary>
    public bool IsStairs => kind == 3;
    /// <summary>v7 (SPEC v7 §14): a melody PHRASE — a roll with time along x and the key's scale along z, any length (½ .. 8 measures) (kind 4).</summary>
    public bool IsPhrase => kind == 4;
    /// <summary>v7: a phrase's time span in world units: its measures × IslandWidth (inside a section time maps to x at exactly this scale, so its
    /// cells line up with the columns' measures).</summary>
    public float PhraseCellsWidth => Mathf.Max(0.125f, phraseBeats / (float)Mathf.Max(1, GlobalClock.BeatsPerBar)) * IslandWidth;
    /// <summary>v7: a phrase's platform width: its time span + the roll's margins (B: the tiny piano west of the first cell, a pad after the last).</summary>
    public float PhraseWidth { get { float w, e; PhraseMargins(out w, out e); return PhraseCellsWidth + w + e; } }
    /// <summary>v7: island-local x where a phrase's time starts (its first cell's west edge): −EdgeInset + the west margin.</summary>
    public float PhraseCellsX0 { get { float w, e; PhraseMargins(out w, out e); return -EdgeInset + w; } }
    /// <summary>v7: the phrase roll's margins beyond its time cells (package B's build decides them through <c>KindPhraseMargins</c>; 0 without).</summary>
    public static void PhraseMargins(out float west, out float east) { west = 0f; east = 0f; KindPhraseMargins(ref west, ref east); }
    static partial void KindPhraseMargins(ref float west, ref float east);
    /// <summary>v7 (B's builds): re-pitch / re-colour what depends on the column's chord, the next chord and the key — a stairs island's steps
    /// (RefreshStairs), a phrase's chord tints and safe-note dots (RefreshPhraseChords); SongManager calls it wherever a keyboard's RefreshKeyDots
    /// runs. Cheap and idempotent.</summary>
    public void RefreshKindChords() { if (IsStairs) RefreshStairs(); else if (IsPhrase) RefreshPhraseChords(); }
    /// <summary>v7: a phrase's depth (its scale rows).</summary>
    public static float PhraseDepth => (ProjectConfig.PhraseRows - 1) * ProjectConfig.PhraseRowPitch + ProjectConfig.TileSize * 0.5f + 2f * ProjectConfig.PlatformPad;
    /// <summary>v7 (SPEC v7 §2.5): the ground this island stands on, shown now (world units; K glides it to SongManager.GroundOf(column)).</summary>
    public float GroundY => groundY;
    internal float groundY;
    /// <summary>v7 (package W): a purely visual y offset (world units) the world magic adds for a moment (the fling's platform flex, the selection
    /// lift); W sets it back to exactly 0 afterwards. The tiles, the cubes, the hub and the cable follow it.</summary>
    public float FxLift => fxLift;
    public void SetFxLift(float y)
    {
        if (Mathf.Abs(y - fxLift) < 1e-5f) return;
        fxLift = y; fxLiftFrame = Time.frameCount;
        ApplyPose();
        RefreshCubeLines();
    }
    /// <summary>v7: true for a frame after the FxLift changed (the Route refits an anchor's cable).</summary>
    public bool FxLifting => Time.frameCount - fxLiftFrame <= 1;
    float fxLift; int fxLiftFrame = -9;
    /// <summary>v6 (SPEC v6 §2.2): the chord's job in the song key (1 home, 2 away, 3 heart, 4 pull; 0 for Moons and keyboards).</summary>
    public int Job => Harmony.Job(this);
    /// <summary>v6 tower (K, SPEC v6 §2.6 / §3.3): the register lift shown this frame (world units: the rest hint, the rise on its turn, the slow
    /// sink after; the same number as <see cref="LiftY"/>) and its phase: 0 rest, 1 rising (a lowered island: dipping), 2 up (down), 3 sinking
    /// (floating back). A glide after a jump (a seek, a stop, an undo) reports 1 / 3 by its direction.</summary>
    public float TowerLift => regShown;
    public int TowerState => towerState;
    /// <summary>v6: 0..1 progress through the current tower phase (the rise, the hold, the sink; 0 at rest).</summary>
    public float TowerPhaseT => towerPhaseT;
    /// <summary>v6: true while a stopped preview (▲ / ▼ pressed while stopped) plays the rise and the slow sink once.</summary>
    public bool TowerPreviewing => previewT >= 0f;
    /// <summary>v6: the lift at rest and on its turn for this island's register (world units; SongManager.TowerRestOf / TowerTopOf).</summary>
    public float TowerRestY => SongManager.TowerRestOf(register);
    public float TowerTopY => SongManager.TowerTopOf(register);
    /// <summary>v6: world y of the platform's underside this frame (the tower's pillar reaches up to it: package W).</summary>
    public float UndersideY => transform.position.y - 0.03f - ProjectConfig.PlatformThickness;   // v7: a down stair's real underside is B's BaseUndersideY
    /// <summary>One pass in beats: bars × BeatsPerBar (v7: a phrase's is its own length, phraseBeats).</summary>
    public float LengthBeats => IsPhrase ? Mathf.Max(1, phraseBeats) : Mathf.Max(1, bars) * Mathf.Max(1, GlobalClock.BeatsPerBar);
    public static readonly Color MoonColor = new Color(0.74f, 0.78f, 0.92f);

    // ---- position (SPEC §2.2): basePos is the logical sea-level position; the transform adds lift/tilt/rise/slide
    Vector3 basePos; bool hasBase;
    public float px => hasBase ? basePos.x : transform.position.x;
    public float pz => hasBase ? basePos.z : transform.position.z;
    /// <summary>Places the island root at (x, 0, z) (the world package's only way to move it); lift/tilt/rise/slide stay visual.</summary>
    public void SetPosition(float x, float z) { basePos = new Vector3(x, 0f, z); hasBase = true; ApplyPose(); }

    // ---- v2 world contract (SPEC §2.1, §8.4)
    Vector3 CenterOffset => IsMoon ? new Vector3((cols - 1) * ProjectConfig.Spacing * 0.5f, 0f, (rows - 1) * ProjectConfig.Spacing * 0.5f)
        : new Vector3(Width * 0.5f - EdgeInset, 0f, Depth * 0.5f - EdgeInset);   // v7: the platform's centre (tile 0,0 sits EdgeInset in from the front-left corner; a 2-measure island is centred on its 2 measures)
    /// <summary>Front-centre hub position: the grab handle, comet dock, halo anchor and cable attachment.</summary>
    public Vector3 HubPos => Center + new Vector3(0f, 0.15f + regShown + groundY + fxLift, -HubOffsetZ);   // v7: the ground and W's visual lift
    float HubOffsetZ => IsMoon ? ProjectConfig.MoonRadius + 0.7f : Depth * 0.5f + 0.9f;
    float HubH => IsMoon ? 0.28f : ProjectConfig.HubHeight;
    /// <summary>Hub polygon sides = bars + 2 (triangle for 1 bar … hexagon for 4); v4: the column's progress gauge (<see cref="Gauge"/>).</summary>
    public int HubSides => bars + 2;
    /// <summary>The hub prism (islands) or disc handle (Moons) with its collider; picked through GetComponentInParent&lt;KeyBlock&gt;().</summary>
    public Transform Hub { get; protected set; }
    /// <summary>Where the Route cable attaches.</summary>
    public Vector3 HubUnderside => HubPos + Vector3.down * (HubH * 0.5f);
    public Vector3 HubTop => HubPos + Vector3.up * (HubH * 0.5f);
    /// <summary>True while this island's column is the one the comet is on (v4: every island of the lit column is lit).</summary>
    public bool IsLit => GlobalClock.IsPlaying && !IsMoon && SongManager.I != null && SongManager.I.LitColumn == column;
    /// <summary>Beat ring radius (Moons) / the beat track's half diagonal (islands), world units, centred on the platform.</summary>
    public float RingRadius => ringR;
    /// <summary>World point on the beat track (islands: the rounded rectangle TrackMargin outside the platform; Moons: the ring) at fraction
    /// <paramref name="t01"/> of a clockwise lap starting at the front centre (hub side), <paramref name="y"/> above the island's base.</summary>
    public Vector3 RingPoint(float t01, float y)
    {
        Vector3 c = VisualCenter;
        if (!IsMoon)
        {
            Vector3 n;
            Vector3 p = MeshFactory.TrackPoint(TrackHX, TrackHZ, TrackR, t01, out n);
            return new Vector3(c.x + p.x, transform.position.y + y, c.z + TrackDz + p.z);   // v7 §21: a long grid's member: around its deeper platform
        }
        float a = -Mathf.PI * 0.5f - t01 * Mathf.PI * 2f;
        return new Vector3(c.x + Mathf.Cos(a) * ringR, transform.position.y + y, c.z + Mathf.Sin(a) * ringR);
    }
    // the beat track's centre line: TrackMargin outside the platform edge, corners concentric with the platform's 0.24 rounding
    float TrackHX => PlatformWidth * 0.5f + ProjectConfig.TrackMargin;   // v9: hugs a long keyboard's case
    float TrackHZ => PlatformDepth * 0.5f + ProjectConfig.TrackMargin;   // v7 §21: a long grid's member stands as deep as the long grid
    /// <summary>v7 §21: the beat track's (and the core glow's) z offset from CenterOffset: a long grid's deeper platform grows toward the back.</summary>
    float TrackDz => IsMoon ? 0f : (PlatformDepth - Depth) * 0.5f;
    static float TrackR => 0.24f + ProjectConfig.TrackMargin;
    const int TrackSub = 4;

    /// <summary>Platform width / depth. v6: a keyboard is exactly a chord island's width (IslandWidth: columns stay aligned) and DepthOf(1) deep (its
    /// layout state has one row: semitones {0}), whatever its 25 keys.</summary>
    public float Width => IsPhrase ? PhraseWidth : IsMoon ? WidthOf(cols) : IsKeyboard ? KeyboardWidthOf(KeyCount, bars) : Mathf.Max(1, bars) * IslandWidth;   // v8: a grown piano is wider   // v7 (SPEC v7 §13.2): a column is its measures wide
    public float Depth => IsKeyboard ? KeyboardDepthOf(KeyCount) : IsStairs ? StairDepth : IsPhrase ? PhraseDepth : (rows - 1) * ProjectConfig.Spacing + ProjectConfig.TileSize + 2f * ProjectConfig.PlatformPad;
    /// <summary>v7 §20.1: a keyboard's depth: DepthOf(1), a little deeper above 37 keys (up to +0.7 at 61) so its longer, narrower keys stay readable.</summary>
    public static float KeyboardDepthOf(int count) => DepthOf(1) + (count > OrganSplit ? BaseKeyLen * UpperLenOf + TierGap : 0f);   // v8: an organ's upper manual behind
    /// <summary>Logical centre of the platform on the sea plane (ignores the drag lift/tilt and the rise/slide animations).</summary>
    public Vector3 Center => (hasBase ? basePos : transform.position) + CenterOffset;
    /// <summary>Where the platform's centre is this frame (follows lift/tilt/rise/slide).</summary>
    public Vector3 VisualCenter => transform.TransformPoint(CenterOffset);
    public Bounds WorldBounds => IsMoon
        ? new Bounds(Center + Vector3.up * 0.6f, new Vector3(ringR * 2f, 3f, ringR * 2f))
        : new Bounds(Center + Vector3.up * 0.6f, new Vector3(Width, 3f, Depth));
    /// <summary>v5: WorldBounds plus the island's conveyor belt ((Passes − 1) slots toward +x): the footprint the layout reserves (song and column
    /// bounds, free spots, Moons pushed clear).</summary>
    public Bounds FootprintBounds
    {
        get
        {
            var b = WorldBounds;
            if (!HasBelt) return b;   // v7: rewind islands and phrases ride no belt
            float ext = (Passes - 1) * BeltPitch;
            b.center += new Vector3(ext * 0.5f, 0f, 0f); b.size += new Vector3(ext, 0f, 0f);
            return b;
        }
    }
    /// <summary>v7: FootprintBounds (a Moon: WorldBounds) raised to the ground the island stands on now — what the camera frames (SPEC v7 §3.3).</summary>
    public Bounds GroundedBounds { get { var b = IsMoon ? WorldBounds : FootprintBounds; b.center += Vector3.up * groundY; return b; } }
    /// <summary>v5: WorldBounds where the platform is shown this frame (moved by the slide and the belt ride): UI anchored to an island follows it.</summary>
    public Bounds VisualBounds { get { var b = WorldBounds; b.center += VisualOffset; return b; } }
    /// <summary>v5: the platform's visual offset from its logical position: the merge / split slide, the belt ride and a drag's slot preview (the
    /// Route cable follows it).</summary>
    public Vector3 VisualOffset => slideOff + previewOff + (IsMoon ? new Vector3(moonRide, 0f, 0f) : BeltOffset);   // v6 §11: a Moon's follow offset
    /// <summary>v5: the eased offset of a free drag's slot preview (neighbours parting for a new column, a column making room for a join).</summary>
    public Vector3 PreviewOffset => previewOff;
    /// <summary>v5: true while the preview offset moved this frame (the Route refits the cable).</summary>
    public bool Shifting { get; private set; }
    /// <summary>v5: eases the island to <paramref name="offset"/> (x, z) away from its logical position — a visual preview only (IslandDrag: the
    /// columns part to show a new column's slot, a column's islands make room for a joining island); zero eases it back.</summary>
    public void SetPreviewOffset(Vector3 offset) { offset.y = 0f; previewTo = offset; }

    // ---- v3 platform geometry (SPEC v3 §3.3): every chord island is IslandWidth wide; members of a group touch edge to edge
    public static float WidthOf(int cols) => (cols - 1) * ProjectConfig.Spacing + ProjectConfig.TileSize + 2f * ProjectConfig.PlatformPad;
    public static float DepthOf(int rows) => (rows - 1) * ProjectConfig.Spacing + ProjectConfig.TileSize + 2f * ProjectConfig.PlatformPad;
    public static float IslandWidth => WidthOf(ProjectConfig.numInversions);
    /// <summary>From the root (tile 0,0 centre) to the platform's west / front edge.</summary>
    public static float EdgeInset => ProjectConfig.TileSize * 0.5f + ProjectConfig.PlatformPad;
    /// <summary>Platform centre offset from the root for a cols x rows grid.</summary>
    public static Vector3 CenterOffsetOf(int cols, int rows) => new Vector3((cols - 1) * ProjectConfig.Spacing * 0.5f, 0f, (rows - 1) * ProjectConfig.Spacing * 0.5f);
    public float WestEdge => px - EdgeInset;
    public float EastEdge => px - EdgeInset + Width;
    public float FrontEdge => pz - EdgeInset;
    public float BackEdge => pz - EdgeInset + Depth;
    /// <summary>True while the member look is on (a same-group neighbour in Route order).</summary>
    public bool IsGroupMember => groupLook;
    /// <summary>Platform base colour (the group seam bridge blends two of them).</summary>
    public Color PlatformColor { get; private set; }
    /// <summary>Visual slide offset (merge snap-in, split): the Route and the comet follow it.</summary>
    public Vector3 SlideOffset => slideOff;
    public bool Sliding => slideT < 1f;

    static Material tileMat;
    public static Material TileMaterial { get { if (tileMat == null) tileMat = Fx.Lit(Color.white, 0.5f, 0f); return tileMat; } }

    Renderer platform; MaterialPropertyBlock mpb; float pulse;
    Material platMat, hubMat, coreMat, ringMat, beadMat, hubFillMat, notchMat;   // per-island materials (Fx.* creates one per call): destroyed with the island
    Renderer glyph; Material glyphMat; Renderer coreGlow; MaterialPropertyBlock coreMpb;
    // ring
    MeshFilter ringFilter; Renderer ringRend; MaterialPropertyBlock ringMpb; Mesh ringMesh; float ringR = 5f;
    int segments; Color[] ringColors; Color[] segA, segB; int[] segCount; int playheadSeg = -1; bool preLightDirty = true; int lastCubeCount = -1;
    // hub
    Renderer hubRend; MaterialPropertyBlock hubMpb; Transform hubFill; Renderer hubFillRend; MaterialPropertyBlock fillMpb; float gauge;
    Transform bead; Renderer beadRend; MaterialPropertyBlock beadMpb;
    // animation state
    float preGlow; bool preGlowFresh; float hover, hoverTarget; float dragLift; Vector2 dragDir; float riseT = 9f, riseY; bool riseRippled = true;
    // v3 group look + slide
    bool groupLook; LineRenderer rim; Material rimMat; Vector3[] rimPts, rimTmp; float rimDraw = 1f, rimDrawRate = 2.5f;
    Transform bridge; Material bridgeMat; KeyBlock bridgeTo; float bridgeShow = 1f, bridgeDelay;
    Vector3 slideOff, slideFrom; float slideT = 9f, slideDur = 0.28f; bool slideBack;
    // v4: register lift, the refusal shake, the sleep look
    float regShown; float shakeT = 9f, shakeAmp;
    // v6: the octave tower (the shown lift follows SongManager.TowerLiftAt; a jump glides; the stopped preview), the job badge, the keyboard's parts
    float towerLastTarget, towerPhaseT, previewT = -1f; double towerLastBeat; bool towerInit, towerCatch; int towerState;
    Transform jobBadge; int jobShown;
    Material caseMat, keyDotMat, keyRootMat, keyHouseInk, keyHouseCream;
    readonly List<Transform> keyDots = new List<Transform>();
    int keyDotRoot = -1; int[] keyDotSemis;
    // v6 §11: a Moon following the song (x offset from its resting place; SongManager.MoonFollowX; a jump glides)
    float moonRide, moonRideLast; double moonRideBeat; bool moonRideInit, moonRideCatch;
    bool sleepShown; Transform eye; Material eyeMat; Color platBase;
    int segVerts = 4;
    // v5: the belt ride (the shown slot follows SongManager.RideSlot; a jump glides there), the belt, the column's passes, the home glyph
    float rideShown, rideLift, rideLastTarget; double rideLastBeat; bool rideInit, rideCatch; Belt belt; int colPasses = 1;
    float dragYawShown, dragSpeedShown, dragCruise;   // v8: a dragged keyboard's lean (degrees about Y; negative = CCW seen from above), its speed (0..1), its cruising speed (slots / beat)
    /// <summary>v8: a dragged keyboard's speed now as a fraction of its cruising speed (0 = still).</summary>
    public float DragSpeedShown => dragSpeedShown;

    /// <summary>
    /// v8: where a repeated KEYBOARD stands along its checkpoints at song beat <paramref name="beat"/>, in slots — SongManager.RideSlot's rules (the
    /// turn, then held at its last slot until the song resets, then the glide home, <see cref="SongManager.HoldPhase"/>) with Belt.DragRide inside the
    /// turn: a constant-speed drag instead of the belt's jerk at each pass. A pure function of the beat, the loop and the timeline.
    /// </summary>
    public float KeyboardRideSlot(double beat, out float lift)
    {
        lift = 0f;
        var sm = SongManager.I;
        if (sm == null || !HasBelt) return 0f;
        int c = column;
        float pl = sm.PassLength(c);
        if (c < 0 || pl <= 0f) return 0f;
        if (!GlobalClock.IsPlaying && beat <= 1e-6) return 0f;
        int passes = Mathf.Min(Passes, sm.ColumnPasses(c));
        if (passes < 2) return 0f;
        double s = sm.ColumnStart(c), end = s + passes * (double)pl;
        double ls = GlobalClock.LoopStartBeat, le = GlobalClock.LoopEndBeat;
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        bool selfLoop = looping && System.Math.Abs(s - ls) < 1e-3 && System.Math.Abs(end - le) < 1e-3;
        float glide = Mathf.Min(ProjectConfig.BeltGlideBeats, pl * (selfLoop ? 0.5f : 1f));
        dragCruise = (passes - 1) / Mathf.Max(1e-3f, (float)(selfLoop ? end - s - glide : end - s)) / (1f - Belt.DragEase);
        if (beat >= s && beat < end) return Belt.DragRide(beat - s, passes, pl, selfLoop, out lift);
        double u;
        int hp = SongManager.HoldPhase(s, end, beat, ProjectConfig.BeltGlideBeats, sm.ResetStaggerOf(this), 0.0, false, out u);
        float held = passes - 1;
        if (hp == 2) return held;
        if (hp == 3) { lift = ProjectConfig.BeltGlideLift * Mathf.Sin((float)u * Mathf.PI); return held * (1f - Ease.InOutCubic((float)u)); }
        return 0f;
    }
    /// <summary>v8: a keyboard's swing now (degrees about Y, negative = counter-clockwise seen from above; 0 at rest).</summary>
    public float DragYawShown => dragYawShown;
    Vector3 previewOff, previewTo;
    Transform homeGlyph; Material homeMat, homeBackMat; readonly List<GameObject> notches = new List<GameObject>();

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly Color DimBeat = new Color(0.30f, 0.30f, 0.37f, 1f), DimSeg = new Color(0.16f, 0.16f, 0.21f, 1f);

    void OnEnable() { GlobalClock.OnBeat += HandleBeat; AudioCube.OnAnyChanged += HandleCubeChanged; GlobalClock.OnLoop += HandleLoop; GlobalClock.OnStop += HandleStop; }
    void OnDisable() { GlobalClock.OnBeat -= HandleBeat; AudioCube.OnAnyChanged -= HandleCubeChanged; GlobalClock.OnLoop -= HandleLoop; GlobalClock.OnStop -= HandleStop; }

    // ---- v7 §12: a STOP resets the song — what held after its turn goes home by moving, in real time, slightly out of sync (never a snap)
    float resetT0 = -1f, resetDelay, resetSpb = 0.5f, resetRideFrom, resetLiftFrom, resetMoonFrom;
    /// <summary>v7 §12: true while this island's / Moon's real-time trip home after a stop runs (its belt, its tower, its follow).</summary>
    public bool ResetTripRunning => resetT0 >= 0f;
    void HandleStop()
    {
        var sm = SongManager.I;
        float spb = (float)(1.0 / System.Math.Max(0.5, GlobalClock.BeatsPerSecond));
        resetSpb = spb;
        resetDelay = (sm != null ? sm.ResetStaggerOf(this) : 0f) * spb;
        resetRideFrom = rideShown; resetLiftFrom = regShown; resetMoonFrom = moonRide;
        bool any = Mathf.Abs(rideShown) > 1e-3f || Mathf.Abs(moonRide) > 1e-3f || (!IsMoon && Mathf.Abs(regShown - SongManager.TowerRestOf(register)) > 1e-3f);
        resetT0 = any ? Time.realtimeSinceStartup : -1f;
    }
    /// <summary>v7 §12: 0..1 through the stop's trip home for a move of <paramref name="beats"/> (the song's tempo); −1 when none runs; ends the trip
    /// once the clock plays or seeks away from the stop.</summary>
    float ResetU(float beats)
    {
        if (resetT0 < 0f) return -1f;
        if (GlobalClock.IsPlaying || GlobalClock.SongBeatD > 1e-6 || dragLift > 0.001f) { resetT0 = -1f; return -1f; }
        float u = (Time.realtimeSinceStartup - resetT0 - resetDelay) / Mathf.Max(0.05f, beats * resetSpb);
        return Mathf.Clamp01(u);
    }
    void EndResetIfHome()
    {
        if (resetT0 < 0f) return;
        float longest = Mathf.Max(ProjectConfig.BeltGlideBeats, Mathf.Max(ProjectConfig.TowerSinkBeats, 2f));
        if (Time.realtimeSinceStartup - resetT0 - resetDelay > longest * resetSpb + 0.05f) resetT0 = -1f;
    }

    // ---- v7 (SPEC v7 §2.5, §3.3): the ground this island stands on glides to its target (GroundGlideSeconds, ease in-out); a load snaps
    float groundFrom, groundTo, groundT = 9f; bool groundInit;
    /// <summary>v7: true while the ground glides to a new target.</summary>
    public bool GroundGliding => groundT < 1f;
    /// <summary>v7: after a rebuild of the same song the island starts at the ground its old self showed and glides to its own target.</summary>
    public void CarryGround(float shown)
    {
        groundY = shown; groundFrom = shown; groundTo = shown; groundT = 9f; groundInit = true;
        ApplyPose();
    }
    bool UpdateGround(float dt)
    {
        var sm = SongManager.I;
        float target = sm != null && hasBase ? sm.GroundTargetOf(this) : 0f;
        float prev = groundY;
        if (!groundInit) { groundInit = true; groundY = groundFrom = groundTo = target; groundT = 9f; }
        else if (IsMoon && groundT >= 1f && Mathf.Abs(target - groundTo) < 0.3f) { groundY = groundTo = target; }   // a Moon's ground follows its follow smoothly
        else if (Mathf.Abs(target - groundTo) > 1e-4f) { groundFrom = groundY; groundTo = target; groundT = 0f; }   // a new target: glide there
        if (groundT < 1f)
        {
            groundT = Mathf.Min(1f, groundT + dt / Mathf.Max(0.05f, ProjectConfig.GroundGlideSeconds));
            groundY = Mathf.Lerp(groundFrom, groundTo, Ease.InOutCubic(groundT));
        }
        if (Mathf.Abs(groundY - prev) < 1e-5f) return false;
        ApplyPose();
        RefreshCubeLines();
        return true;
    }
    /// <summary>Only a cube with a window on this island can change its ring (a Rider has one on every island): a level drag elsewhere must not recompute and re-upload every ring each frame.</summary>
    void HandleCubeChanged(AudioCube c) { if (c == null || c.rider || c.Island == this || c.Moon == this || HasWindowOn(c)) preLightDirty = true; }
    /// <summary>v6: a carried cube (SongManager's carry windows) also lights this island's track.</summary>
    bool HasWindowOn(AudioCube c) { var w = c.windows; for (int i = 0; i < w.Count; i++) if (w[i].island == this) return true; return false; }
    void HandleLoop() { preLightDirty = true; }

    void HandleBeat(int beat, bool downbeat)
    {
        if (IsMoon) { if (GlobalClock.IsPlaying && (SongManager.I == null || SongManager.I.MoonPlaysAt(this, beat + 0.01))) Pulse(downbeat ? 0.55f : 0.2f); return; }   // v6 §11: its section only
        float local = beat - startBeatOffset;
        if (local >= -0.01f && local < LengthBeats * Passes) Pulse(downbeat ? 0.75f : 0.28f);   // v5: every pass it plays
    }

    public void BuildFromData(SongManager.MeasureData data, int index)
    {
        measureIndex = index;
        assignedChord = data.chordKey;
        kind = data.kind;
        bars = data.bars <= 0 ? 1 : Mathf.Clamp(data.bars, 1, 4);
        barOffset = Mathf.Max(0, data.barOffset);
        placed = data.placed; mood = Mathf.Clamp(data.mood, 0, 3); energy = Mathf.Clamp(data.energy, 0, 3);
        fill = data.fill; sleep = data.sleep; repeat = Mathf.Clamp(data.repeat, 1, 4);
        group = Mathf.Max(0, data.group); fall = Mathf.Clamp(data.fall, 0, 5);
        register = Mathf.Clamp(data.reg, -2, 2);   // SPEC v4 §2.1
        carry = Mathf.Clamp(data.carry, 0, ProjectConfig.MaxCarry);   // SPEC v6 §2.4 (v7 §21.3: carryStyle below reads 1 whenever it carries)
        stairType = Mathf.Clamp(data.stairType, 0, 5); stairDir = data.stairDir > 0 ? 1 : -1;   // SPEC v7 §2.1
        stairSteps = Mathf.Clamp(data.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps); stairRate = MeasureState.StairRate(data.stairRate);
        stairLead = data.stairLead; carryStyle = carry > 0 ? 1 : Mathf.Clamp(data.carryStyle, 0, 1); rewind = data.rewind; vary = Mathf.Clamp(data.vary, 0, 2); launch = data.launch; lead = data.lead;
        phraseOffset = Mathf.Max(0, data.phraseOffset); phraseBeats = Mathf.Clamp(data.phraseBeats <= 0 ? 4 : data.phraseBeats, 1, 32); phraseGrid = data.phraseGrid == 6 ? 6 : 12;
        secRole = Mathf.Clamp(data.secRole, 0, 6);   // v7 §17.1
        keyCount = data.keyCount <= 0 ? ProjectConfig.KeyboardKeys : Mathf.Clamp(data.keyCount, ProjectConfig.KeyboardMinKeys, ProjectConfig.KeyboardMaxKeys);   // v7 §20.1
        kit = data.kit != null && data.kit.Length > 0 ? (int[])data.kit.Clone() : null;   // v9 (G): a Moon's own kit (null = the standard rows)
        if (IsMoon)
        {
            // a Moon carries no chord: rows are drum pieces (§3.4); the stored chord is a placeholder that round-trips
            // v9 (G): a Moon's groove may run several bars (its cubes' window: SongManager.RecomputeMeasureStarts); 1 = the v6 Moon
            chordRootMIDI = 36; bars = Mathf.Clamp(data.bars <= 0 ? 1 : data.bars, 1, SongManager.MaxMoonBars); group = 0;
            storedSemis = new[] { 0 };
            semitoneList = new List<int> { 0 };
            chordHeight = 1;
            quality = ChordQuality.Major7;
            chordColor = MoonColor;
            if (string.IsNullOrEmpty(assignedChord)) assignedChord = "Moon";
            Build();
            return;
        }
        if (IsKeyboard)
        {
            // v6 (SPEC v6 §3.1): a keyboard carries no chord: its lowest key is stored as the root (it round-trips), semitones {0} like a Moon; its
            // chord for highlighting / adapting is its column's (SongManager.ChordOfColumn); no mood view
            chordRootMIDI = data.chordRootMIDI > 0 ? data.chordRootMIDI : SongManager.KeyboardLowestKey();
            // v7 §20.1: the whole range inside the piano (A0 21 .. C8 108; v6 kept the lowest key in 36..72 for its 25 keys: unchanged for those)
            while (chordRootMIDI < 21) chordRootMIDI += 12;
            while (chordRootMIDI + KeyCount - 1 > 108) chordRootMIDI -= 12;
            storedSemis = new[] { 0 };
            semitoneList = new List<int> { 0 };
            chordHeight = 1; mood = 0;
            quality = ChordQuality.Major7;
            chordColor = KeyboardGlow;
            if (string.IsNullOrEmpty(assignedChord) || assignedChord == "C") assignedChord = "Keys";
            Build();
            return;
        }
        chordRootMIDI = data.chordRootMIDI;
        if (chordRootMIDI < 36) chordRootMIDI += 24;
        if (chordRootMIDI > 84) chordRootMIDI -= 12;
        var stored = new List<int>(data.semitones != null && data.semitones.Length > 0 ? data.semitones : new[] { 0, 4, 7 });
        if (!stored.Contains(0)) stored.Insert(0, 0);
        stored.Sort();
        storedSemis = stored.ToArray();
        // The mood/climate view (§3.5) never rewrites storedSemis and never changes the row count.
        semitoneList = new List<int>(MusicTheory.EffectiveSemis(storedSemis, mood, SongManager.Climate));
        if (!semitoneList.Contains(0)) semitoneList.Insert(0, 0);
        semitoneList.Sort();
        chordHeight = semitoneList.Count;
        quality = MusicTheory.QualityOf(semitoneList);
        chordColor = MusicTheory.ChordColor(chordRootMIDI, semitoneList);
        if (string.IsNullOrEmpty(assignedChord)) assignedChord = MusicTheory.ChordName(chordRootMIDI, semitoneList);
        Build();
    }

    public SongManager.MeasureData ToData() => new SongManager.MeasureData
    {
        index = measureIndex + 1, chordKey = assignedChord, chordRootMIDI = chordRootMIDI,
        semitones = storedSemis != null && storedSemis.Length > 0 ? (int[])storedSemis.Clone() : semitoneList.ToArray(), measureDuration = LengthBeats, bars = bars, barOffset = barOffset,
        px = px, pz = pz, placed = placed, kind = kind, mood = mood, energy = energy, fill = fill, sleep = sleep, repeat = repeat, group = group, fall = fall, col = column, reg = register,
        carry = carry,
        stairType = stairType, stairDir = stairDir, stairSteps = stairSteps, stairRate = stairRate, stairLead = stairLead,
        carryStyle = carryStyle, rewind = rewind, vary = vary, launch = launch, lead = lead,
        phraseOffset = phraseOffset, phraseBeats = phraseBeats, phraseGrid = phraseGrid, secRole = secRole, keyCount = IsKeyboard ? KeyCount : 0,
        kit = kit != null && kit.Length > 0 ? (int[])kit.Clone() : null   // v9 (G)
    };

    /// <summary>Rows of the tile grid for a chord: the count of stored tones (0 inserted if missing). Used for layout before the island exists.</summary>
    public static int RowsOf(int[] semitones)
    {
        var stored = new List<int>(semitones != null && semitones.Length > 0 ? semitones : new[] { 0, 4, 7 });
        if (!stored.Contains(0)) stored.Add(0);
        return stored.Count;
    }

    /// <summary>Platform colour of a chord island (the tray cards and the ghost use it too). v5 (the vibes: an island reads as its card): the
    /// chord colour keeps its hue and a little more colour and is darkened in HSV to a value that sits under the pastel cubes and the pitch-lit
    /// tiles (≈ 0.64) instead of being blended toward the violet ink (which turned a butter-yellow sunny island khaki). Per hue band: yellows stay
    /// bright and lean yellow (golden, not olive or orange), oranges lean toward yellow (tangerine, apart from the stormy red), reds are a little lighter and softer
    /// (coral, not crimson), blue-violets a little lighter (they read dark, near the dusk sea). Moons and grey colours keep the v4 ink blend.</summary>
    public static Color PlatformColorOf(Color chordColor, bool moon = false)
    {
        Color ink = new Color(0.16f, 0.14f, 0.25f);
        if (moon) return Color.Lerp(ink, chordColor, 0.3f);
        float h, s, v;
        Color.RGBToHSV(chordColor, out h, out s, out v);
        if (s < 0.08f) return Color.Lerp(ink, chordColor, 0.52f);
        float deg = h * 360f;
        float yellow = HueBump(deg, 47f, 30f), orange = HueBump(deg, 22f, 14f), red = HueBump(deg, 0f, 22f), violet = HueBump(deg, 250f, 50f);
        float val = 0.64f + 0.24f * yellow + 0.1f * orange + 0.08f * red + 0.08f * violet;
        float sat = Mathf.Clamp(s * 1.12f + 0.06f - 0.08f * red, 0.4f, 0.86f);
        // the render grades warm: sunny is pulled toward golden yellow (47°), spicy toward tangerine (27°), whatever the root's tiny shade
        deg += Mathf.DeltaAngle(deg, 47f) * 0.6f * yellow + Mathf.DeltaAngle(deg, 27f) * 0.6f * orange;
        var c = Color.HSVToRGB(Mathf.Repeat(deg / 360f, 1f), sat, val);
        c.a = 1f;
        return c;
    }

    /// <summary>1 at hue <paramref name="centre"/> (degrees), falling linearly to 0 at ±<paramref name="width"/>.</summary>
    static float HueBump(float deg, float centre, float width) => Mathf.Clamp01(1f - Mathf.Abs(Mathf.DeltaAngle(deg, centre)) / width);

    /// <summary>Tile tint of a chord island (brightness then varies with pitch: PitchLook.TileColor keeps this hue). v5: a pastel of the chord's
    /// own hue (saturation ≈ half the vibe's, value 0.96) instead of a blend toward the lavender tile base, so the tiles carry the vibe too.
    /// Moons and grey colours keep the v4 blend.</summary>
    public static Color TileTintOf(Color chordColor, bool moon = false)
    {
        if (moon) return Color.Lerp(Palette.TileBase, chordColor, 0.2f);
        float h, s, v;
        Color.RGBToHSV(chordColor, out h, out s, out v);
        if (s < 0.08f) return Color.Lerp(Palette.TileBase, chordColor, 0.36f);
        var c = Color.HSVToRGB(h, Mathf.Clamp(s * 0.5f + 0.04f, 0.18f, 0.36f), 0.96f);
        c.a = 1f;
        return c;
    }

    void Build()
    {
        hideDirty = true;   // v9 perf: HideDepth re-measures once the new tiles stand
        Clear();
        cols = IsMoon ? ProjectConfig.MoonCols : (IsKeyboard ? KeyCount : ProjectConfig.numInversions);   // v7 §20.1: a keyboard's keys (13..61)
        rows = IsMoon ? ProjectConfig.MoonRows : (IsKeyboard ? 1 : chordHeight);
        if (IsStairs || IsPhrase) KindGridSize();   // v7 (B, KeyBlock.Kinds.cs): stairs = one row of steps, a phrase = time cells x scale rows
        grid = new TileInteraction[cols, rows];
        mpb = new MaterialPropertyBlock();
        ringMpb = new MaterialPropertyBlock();
        hubMpb = new MaterialPropertyBlock();
        fillMpb = new MaterialPropertyBlock();
        coreMpb = new MaterialPropertyBlock();
        beadMpb = new MaterialPropertyBlock();

        var tilesRoot = new GameObject("Tiles").transform;
        tilesRoot.SetParent(transform, false);
        tilesContainer = tilesRoot;

        // pitch table
        int[,] midi = new int[cols, rows];
        int lowest = int.MaxValue, highest = int.MinValue;
        if (IsMoon)
        {
            for (int x = 0; x < cols; x++) for (int z = 0; z < rows; z++) midi[x, z] = kit != null && kit.Length > 0 ? VoiceRules.DrumPiece(x, z, rows, kit) : SynthBank.DrumForRow(z, rows);   // v9 (G): a Moon's own kit
            lowest = 0; highest = 1;
        }
        else if (IsKeyboard)
        {
            // v6: one row of chromatic keys from the lowest key; tile.midi = key + 12 x register
            for (int x = 0; x < cols; x++) midi[x, 0] = chordRootMIDI + x + 12 * register;
            lowest = midi[0, 0]; highest = midi[cols - 1, 0];
        }
        else if (IsStairs || IsPhrase) KindPitches(midi, ref lowest, ref highest);   // v7 (B): the run's pitches / the rows' scale pitches
        else
        {
            var inversion = new List<int>(semitoneList);
            for (int x = 0; x < cols; x++)
            {
                if (x > 0) inversion = getInversion(inversion);
                for (int z = 0; z < rows; z++)
                {
                    midi[x, z] = chordRootMIDI + inversion[z] + 12 * register;   // v4: the island's register shifts every tile by octaves
                    lowest = Mathf.Min(lowest, midi[x, z]); highest = Mathf.Max(highest, midi[x, z]);
                }
            }
        }

        // platform (+ collider: picked as the island when no tile/cube is in front of it)
        float w = PlatformWidth, d = Depth, thick = ProjectConfig.PlatformThickness;   // v9: a long keyboard's hugs its keys
        Vector3 off = CenterOffset;
        var plat = new GameObject("Platform");
        plat.transform.SetParent(transform, false);
        Color platColor = IsKeyboard ? KeyboardWood : PlatformColorOf(chordColor, IsMoon);   // v6: a keyboard's case is wood-ink, not a vibe
        PlatformColor = platColor;
        if (IsMoon)
        {
            float r = ProjectConfig.MoonRadius;
            plat.transform.localPosition = new Vector3(off.x, -thick - 0.03f, off.z);
            plat.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Prism(48, r, thick);
            var pc = plat.AddComponent<BoxCollider>(); pc.center = new Vector3(0f, thick * 0.5f, 0f); pc.size = new Vector3(r * 2f, thick, r * 2f);
        }
        else
        {
            plat.transform.localPosition = new Vector3(off.x, -thick * 0.5f - 0.03f, off.z);
            plat.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(w, thick, d), 0.24f, 4);
            var pc = plat.AddComponent<BoxCollider>(); pc.size = new Vector3(w, thick, d);
        }
        platform = plat.AddComponent<MeshRenderer>();
        platMat = Fx.Lit(platColor, 0.32f, 0f);
        platform.sharedMaterial = platMat;
        platBase = platColor;
        platform.shadowCastingMode = ShadowCastingMode.On;
        float platTopY = -0.03f;

        // tiles (v6: a keyboard builds its keys instead)
        Color tileTint = TileTintOf(chordColor, IsMoon);
        if (!IsMoon && !IsKeyboard && !IsStairs && !IsPhrase && bars >= 2) BuildHeldMeasures(tileTint, midi, lowest, highest);   // v7 §13.2
        if (IsKeyboard) BuildKeys(tilesRoot, midi);
        else if (IsStairs || IsPhrase) KindTiles(tilesRoot, midi, lowest, highest, tileTint);   // v7 (B): steps / cells
        for (int x = 0; x < cols && !IsKeyboard && !IsStairs && !IsPhrase; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                float y = IsMoon ? 0.03f * x : PitchLook.Rise * (midi[x, z] - lowest);   // SPEC v5 §2.4: pitch = height (R tunes it)
                var go = new GameObject($"Tile_{measureIndex}_{x}_{z}");
                go.transform.SetParent(tilesRoot, false);
                go.transform.localPosition = new Vector3(x * ProjectConfig.Spacing, y + ProjectConfig.TileThickness * 0.5f, z * ProjectConfig.Spacing);
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(ProjectConfig.TileSize, ProjectConfig.TileThickness, ProjectConfig.TileSize), 0.1f, 3);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = TileMaterial;
                // v5: the pitch staircase (PitchLook.Rise) floats high tiles well above their platform and their hard shadows on it read as holes /
                // missing tiles (dark maroon on a golden island); the toon shader has no per-material shadow-receive switch, so chord tiles cast no
                // shadows (the steps read by their banded side faces and ink outlines; the platform still takes the cubes' and the hub's shadows)
                mr.shadowCastingMode = IsMoon ? ShadowCastingMode.On : ShadowCastingMode.Off;
                var col = go.AddComponent<BoxCollider>();
                col.size = new Vector3(ProjectConfig.TileSize, ProjectConfig.TileThickness, ProjectConfig.TileSize);
                go.tag = "Tile";
                var tile = go.AddComponent<TileInteraction>();
                float t = IsMoon ? x / (float)(cols - 1) : (highest > lowest ? (midi[x, z] - lowest) / (float)(highest - lowest) : 0.5f);
                Color c = PitchLook.TileColor(tileTint, t);   // SPEC v5 §2.4: pitch = brightness (R tunes it)
                tile.Setup(this, x, z, midi[x, z], c);
                if (IsMoon) tile.SetKitLook(true);
                tiles.Add(tile);
                grid[x, z] = tile;
            }
        }

        // segmented beat ring under the platform (§2.5): one flat arc per 16th, colours in mesh.colors; v4: islands get the rounded beat
        // TRACK hugging the platform (TrackMargin outside its edge) instead of the circle, Moons keep the ring
        ringR = IsMoon ? ProjectConfig.MoonRadius * 1.1f : Mathf.Sqrt(TrackHX * TrackHX + TrackHZ * TrackHZ);
        var ring = new GameObject(IsMoon ? "BeatRing" : "BeatTrack");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(off.x, platTopY - thick - 0.1f, off.z);
        ring.transform.localScale = IsMoon ? new Vector3(ringR, 1f, ringR) : Vector3.one;
        ringFilter = ring.AddComponent<MeshFilter>();
        ringRend = ring.AddComponent<MeshRenderer>();
        ringMat = Fx.AdditiveVertex(IconFactory.GetTexture("white"), 0.9f);
        ringRend.sharedMaterial = ringMat;
        ringRend.shadowCastingMode = ShadowCastingMode.Off; ringRend.receiveShadows = false;
        ringMesh = null; segments = 0;
        EnsureRing();

        // soft core glow beneath the island
        var core = new GameObject("CoreGlow");
        core.transform.SetParent(transform, false);
        core.transform.localPosition = new Vector3(off.x, platTopY - thick - 0.32f, off.z);
        float gw = IsMoon ? ProjectConfig.MoonRadius * 2f : w, gd = IsMoon ? ProjectConfig.MoonRadius * 2f : d;
        core.transform.localScale = new Vector3(gw * 1.75f, 1f, gd * 1.75f);
        core.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        coreGlow = core.AddComponent<MeshRenderer>();
        coreMat = Fx.Additive(IconFactory.GetTexture("glowSoft"), chordColor, 0.25f);
        coreGlow.sharedMaterial = coreMat;
        coreGlow.shadowCastingMode = ShadowCastingMode.Off; coreGlow.receiveShadows = false;

        // chord quality glyph on the platform's front-left margin (chord islands only)
        if (!IsMoon && !IsKeyboard && !IsStairs && !IsPhrase)
        {
            string glyphIcon = MusicTheory.QualityIcon(quality);
            var g = new GameObject("QualityGlyph");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(-ProjectConfig.TileSize * 0.5f - ProjectConfig.PlatformPad * 0.5f, 0.015f, off.z);
            g.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            glyph = g.AddComponent<MeshRenderer>();
            glyphMat = Fx.Alpha(IconFactory.GetTexture(glyphIcon), Palette.A(Color.Lerp(chordColor, Color.white, 0.35f), 0.9f));
            glyph.sharedMaterial = glyphMat;
            glyph.shadowCastingMode = ShadowCastingMode.Off; glyph.receiveShadows = false;

            // v6 (SPEC v6 §3.4): the chord's job badge on the platform's front-left corner (U1's JobBadge art: the number on its family shape, family
            // colour, ink outline; the glyph fills 80 % of the quad); RefreshJobBadge picks the job (none for job 0) and hides it while asleep
            var jb = new GameObject("JobBadge");
            jb.transform.SetParent(transform, false);
            jb.transform.localPosition = new Vector3(-EdgeInset + ProjectConfig.PlatformPad * 0.5f + 0.05f, 0.017f, -EdgeInset + ProjectConfig.PlatformPad * 0.5f + 0.05f);
            jb.transform.localScale = new Vector3(JobBadgeSize, 1f, JobBadgeSize);
            jb.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var jr = jb.AddComponent<MeshRenderer>();
            jr.shadowCastingMode = ShadowCastingMode.Off; jr.receiveShadows = false;
            jobBadge = jb.transform; jobShown = 0;
            jb.SetActive(false);
            RefreshJobBadge();

            // v5 (SPEC v5 §2.1): the song key's home chord wears a small house sticker, cream on an ink backing so it reads on every vibe colour
            // (RefreshHome shows it). v6: the job-1 badge IS the house, so the sticker only remains on a home island doing another job (a tonic
            // dominant), beside the badge on the front margin
            var hg = new GameObject("HomeGlyph");
            hg.transform.SetParent(transform, false);
            hg.transform.localPosition = new Vector3(-EdgeInset + ProjectConfig.PlatformPad * 1.5f + 0.05f, 0.016f, -EdgeInset + ProjectConfig.PlatformPad * 0.5f + 0.05f);
            hg.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            hg.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var hr = hg.AddComponent<MeshRenderer>();
            homeMat = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(new Color(1f, 0.95f, 0.84f), 1f));
            hr.sharedMaterial = homeMat;
            hr.shadowCastingMode = ShadowCastingMode.Off; hr.receiveShadows = false;
            var hb = new GameObject("Backing");
            hb.transform.SetParent(hg.transform, false);
            hb.transform.localPosition = new Vector3(0f, -0.004f, -0.02f);
            hb.transform.localScale = new Vector3(1.28f, 1f, 1.28f);
            hb.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var hbr = hb.AddComponent<MeshRenderer>();
            homeBackMat = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(Look.InkColor, 0.9f));
            homeMat.renderQueue = homeBackMat.renderQueue + 1;   // the house always over its backing
            hbr.sharedMaterial = homeBackMat;
            hbr.shadowCastingMode = ShadowCastingMode.Off; hbr.receiveShadows = false;
            homeGlyph = hg.transform;
            hg.SetActive(false);
        }
        if (!IsMoon)
        {
            // v4: the closed eye of a sleeping island on the platform's front-right margin (hidden while awake); v6: a keyboard's on its back rail
            var e = new GameObject("SleepEye");
            if (IsKeyboard && keyRail != null)
            {
                e.transform.SetParent(keyRail, false);
                e.transform.localPosition = new Vector3(keyRailLen * 0.5f - 0.55f, keyRailH * 0.5f + 0.014f, 0f);
                e.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            }
            else
            {
                e.transform.SetParent(transform, false);
                e.transform.localPosition = new Vector3(-EdgeInset + w - ProjectConfig.PlatformPad * 0.5f - 0.05f, 0.016f, -EdgeInset + ProjectConfig.PlatformPad * 0.5f + 0.05f);
                e.transform.localScale = new Vector3(0.84f, 1f, 0.84f);
            }
            e.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var er = e.AddComponent<MeshRenderer>();
            eyeMat = Fx.Alpha(ClosedEyeTexture, Palette.A(Look.InkColor, 0.92f));
            er.sharedMaterial = eyeMat;
            er.shadowCastingMode = ShadowCastingMode.Off; er.receiveShadows = false;
            eye = e.transform;
            e.SetActive(false);
        }

        // hub: prism with sides = bars + 2 (islands) or a small disc handle (Moons); the comet dock and cable anchor
        var hub = new GameObject("Hub");
        hub.transform.SetParent(transform, false);
        hub.transform.localPosition = new Vector3(off.x, 0.15f - HubH * 0.5f, off.z - HubOffsetZ);
        int sides = IsMoon ? 24 : HubSides;
        float hubR = IsMoon ? 0.45f : ProjectConfig.HubRadius;
        hub.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Prism(sides, hubR, HubH);
        hubRend = hub.AddComponent<MeshRenderer>();
        hubMat = Fx.Lit(IsMoon ? Color.Lerp(chordColor, Color.black, 0.25f) : HubShellColor, 0.6f, 0.15f);
        hubRend.sharedMaterial = hubMat;
        hubRend.shadowCastingMode = ShadowCastingMode.On;
        var hc = hub.AddComponent<BoxCollider>();
        hc.center = new Vector3(0f, HubH * 0.5f, 0f); hc.size = new Vector3(hubR * 2f, HubH, hubR * 2f);
        Hub = hub.transform;
        hubFill = null; hubFillRend = null; gauge = 0f;
        if (!IsMoon)
        {
            // v4: the progress gauge, read from above (the camera looks down on it): the chord colour spreads over the dim ink shell's top from
            // its centre (a liquid rising in a funnel: the lit area grows with the level) until it covers the whole face at the column's end;
            // faint ink outlines on the face mark the bars of a multi-bar island
            var f = new GameObject("HubFill");
            f.transform.SetParent(hub.transform, false);
            f.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Prism(sides, hubR, HubH + 0.025f);
            hubFillRend = f.AddComponent<MeshRenderer>();
            hubFillMat = Fx.Lit(FillColor, 0.6f, 0.1f, false);
            hubFillRend.sharedMaterial = hubFillMat;
            hubFillRend.shadowCastingMode = ShadowCastingMode.Off;
            hubFill = f.transform;
            hubFill.localScale = new Vector3(0.001f, 1f, 0.001f);
            f.SetActive(false);
            BuildNotches();
        }
        else
        {
            // Moon ring pointer: a small bright bead lapping the ring once per bar (§2.4 rule 7)
            var b = new GameObject("Bead");
            b.transform.SetParent(transform, false);
            b.transform.localScale = new Vector3(0.55f, 1f, 0.55f);
            b.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            beadRend = b.AddComponent<MeshRenderer>();
            beadMat = Fx.Additive(IconFactory.GetTexture("glow"), Color.white, 1.4f);
            beadRend.sharedMaterial = beadMat;
            beadRend.shadowCastingMode = ShadowCastingMode.Off; beadRend.receiveShadows = false;
            bead = b.transform;
            b.SetActive(false);
        }
        preLightDirty = true;
        // v6: the tower's shown lift starts at the rest hint; the first frame takes the pure lift of the beat (CarryLift keeps a rebuilt island's)
        regShown = SongManager.TowerRestOf(register); towerInit = false; towerCatch = false; previewT = -1f; towerState = 0; towerPhaseT = 0f; Lifting = false;
        sleepShown = false;
        // v5: the conveyor belt of a repeated island (its length = its passes; a rebuild may animate it from the old length: CarryRide)
        belt = null; rideInit = false; rideCatch = false; rideShown = 0f; rideLift = 0f;
        if (HasBelt) { belt = Belt.Create(this); belt.SetSlots(Passes, false, Passes); }   // v7: no belt under a rewind island or a phrase
        if (!IsMoon) { SetAnchor(isAnchor); if (sleep) ApplySleepLook(true); }
        if (IsKeyboard) RefreshKeyDots();
        KindStructure();   // v7 (B, KeyBlock.Kinds.cs): beat pips + measure marks on every grid, the stairs / phrase marks
        ApplyPose();
    }

    // ---- v7 build hooks (SPEC v7 §18: package B implements them in KeyBlock.Kinds.cs; unimplemented partial methods compile away)
    /// <summary>v7: sets <see cref="cols"/> / <see cref="rows"/> for a stairs island (steps x 1) or a phrase (time cells x scale rows).</summary>
    partial void KindGridSize();
    /// <summary>v7: fills the pitch table of a stairs island (SongManager.StairPitches) or a phrase (its rows' scale pitches, register included).</summary>
    partial void KindPitches(int[,] midi, ref int lowest, ref int highest);
    /// <summary>v7: builds a stairs island's steps / a phrase's cells as TileInteraction tiles (grid[x, z], tiles).</summary>
    partial void KindTiles(Transform tilesRoot, int[,] midi, int lowest, int highest, Color tileTint);
    /// <summary>v7: the structure marks every island wears (beat pips on its front margin, measure lines) and the stairs / phrase extras.</summary>
    partial void KindStructure();

    /// <summary>The gauge's faint outlines on the hub's face: one per bar boundary of a multi-bar column (v4), or v5 one per PASS boundary when the
    /// column repeats (the fill covers every pass). Rebuilt when the column's passes change.</summary>
    void BuildNotches()
    {
        foreach (var o in notches) if (o != null) Destroy(o);
        notches.Clear();
        if (IsMoon || Hub == null) return;
        int div = colPasses > 1 ? colPasses : bars;
        if (div <= 1) return;
        int sides = HubSides; float hubR = ProjectConfig.HubRadius;
        if (notchMat == null) notchMat = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Color.Lerp(Look.InkColor, chordColor, 0.25f), colPasses > 1 ? 0.7f : 0.55f));
        for (int k = 1; k < div; k++)
        {
            var nch = new GameObject((colPasses > 1 ? "PassNotch" : "BarNotch") + k);
            nch.transform.SetParent(Hub, false);
            nch.transform.localPosition = new Vector3(0f, HubH + 0.035f, 0f);
            float sc = Mathf.Sqrt(k / (float)div);   // the area of the face up to bar / pass k (the fill's area grows with the level)
            nch.transform.localScale = new Vector3(sc, 1f, sc);
            nch.AddComponent<MeshFilter>().sharedMesh = MeshFactory.PolyRing(sides, hubR, hubR - (colPasses > 1 ? 0.06f : 0.045f) / Mathf.Max(0.2f, sc));
            var nr = nch.AddComponent<MeshRenderer>();
            nr.sharedMaterial = notchMat;
            nr.shadowCastingMode = ShadowCastingMode.Off; nr.receiveShadows = false;
            notches.Add(nch);
        }
    }

    /// <summary>v5: the passes of this island's column (SongManager, with the timeline): the gauge's notches follow.</summary>
    public void SetColumnPasses(int passes)
    {
        passes = Mathf.Clamp(passes, 1, ProjectConfig.MaxRepeat);
        if (passes == colPasses) return;
        colPasses = passes;
        if (notchMat != null) { Kill(notchMat); notchMat = null; }
        BuildNotches();
    }

    /// <summary>v5 (SPEC v5 §2.1): shows the house on the song key's home island (Vibe.IsHome of its root); hidden while it sleeps.
    /// SongManager calls it after every build; the key never changes by editing islands.</summary>
    public void RefreshHome()
    {
        if (homeGlyph == null) return;
        bool on = !IsMoon && !IsKeyboard && !sleepShown && Vibe.IsHome(chordRootMIDI) && Job != 1;   // v6: the job-1 badge is the house
        if (homeGlyph.gameObject.activeSelf != on) homeGlyph.gameObject.SetActive(on);
    }

    /// <summary>v5: the steepest the ride moves in slots per song beat (the jerk: ≈ 10.5 at JerkLead 0.3; the glide back ≤ 9): a faster change is a
    /// jump (a seek, a stop, an undo) and glides.</summary>
    public const float RideMaxSlope = 12f;

    /// <summary>v5: the island leaves its belt at once (a drag grabbed it where it was shown: IslandDrag moves its logical root there); the belt's
    /// surface does not run.</summary>
    public void ResetRide()
    {
        if (IsMoon) { moonRide = 0f; moonRideLast = 0f; moonRideCatch = false; moonRideInit = true; moonRideBeat = GlobalClock.SongBeatD; ApplyPose(); RefreshCubeLines(); return; }   // v6 §11
        rideShown = 0f; rideLift = 0f; rideCatch = false; rideInit = true; rideLastTarget = 0f; rideLastBeat = GlobalClock.SongBeatD;
        if (belt != null) belt.Ride(0f, false);
        ApplyPose();
        RefreshCubeLines();
    }

    /// <summary>v5: after a seek-keeping rebuild the new island starts where the old one was shown on its belt (<paramref name="slot"/>) and its belt
    /// at the old length (<paramref name="beltSlots"/>): the ride glides to the new timeline, the belt grows / shrinks to the new repeat.</summary>
    public void CarryRide(float slot, float beltSlots)
    {
        if (IsMoon) return;
        rideInit = true; rideShown = HasBelt ? Mathf.Max(0f, slot) : 0f; rideCatch = Mathf.Abs(rideShown) > 1e-4f; rideLastTarget = rideShown; rideLastBeat = GlobalClock.SongBeatD;
        int want = HasBelt ? Passes : 1;   // v7: a rewind island's belt shrinks away
        if (Mathf.Abs(beltSlots - want) > 0.01f && (beltSlots > 1.01f || want > 1))
        {
            if (belt == null) belt = Belt.Create(this);
            belt.SetSlots(want, true, beltSlots);
        }
        ApplyPose();
    }

    void Clear()
    {
        DestroyMaterials();
        tiles.Clear();
        Hub = null; hubRend = null; hubFill = null; hubFillRend = null; ringFilter = null; ringRend = null; ringMesh = null; segments = 0; bead = null; beadRend = null;
        platform = null; coreGlow = null; glyph = null;
        rim = null; bridge = null; bridgeTo = null; groupLook = false; rimPts = null; rimTmp = null; eye = null; sleepShown = false;
        belt = null; homeGlyph = null; notches.Clear();
        jobBadge = null; jobShown = 0; keyDots.Clear(); keyRail = null; keyDotRoot = -1; keyDotSemis = null; keyCx = null;
        lgDivider = null; lgJoinW = lgJoinE = lgShaped = false; lgShapedFor = null;   // v7 §21: the next Build re-joins (LateUpdate)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    void OnDestroy() { DestroyMaterials(); }

    /// <summary>The materials and the ring mesh Build created for this island (Unity never frees them with the GameObject; a rebuild makes new ones).</summary>
    void DestroyMaterials()
    {
        Kill(platMat); Kill(hubMat); Kill(coreMat); Kill(ringMat); Kill(beadMat); Kill(glyphMat); Kill(ringMesh); Kill(rimMat); Kill(bridgeMat); Kill(eyeMat); Kill(hubFillMat); Kill(notchMat); Kill(homeMat); Kill(homeBackMat);
        platMat = hubMat = coreMat = ringMat = beadMat = glyphMat = rimMat = bridgeMat = eyeMat = hubFillMat = notchMat = homeMat = homeBackMat = null; ringMesh = null;
        Kill(caseMat); Kill(keyDotMat); Kill(keyRootMat); Kill(keyHouseInk); Kill(keyHouseCream);
        caseMat = keyDotMat = keyRootMat = keyHouseInk = keyHouseCream = null;
        Kill(heldMat); Kill(dividerMat); heldMat = dividerMat = null; heldRoot = null;   // v7
        Kill(lgDivMat); lgDivMat = null;   // v7 §21
    }
    static void Kill(Object o) { if (o != null) Destroy(o); }

    public TileInteraction GetTile(int x, int z)
    {
        if (grid == null || x < 0 || z < 0 || x >= cols || z >= rows) return null;
        return grid[x, z];
    }

    public void Pulse(float amount) { pulse = Mathf.Max(pulse, amount); }

    // ------------------------------------------------------------------ segmented ring (SPEC §2.5)
    void EnsureRing()
    {
        if (ringFilter == null) return;
        int want = Mathf.Max(1, (IsPhrase ? Mathf.Max(1, phraseBeats) : (IsMoon ? 1 : Mathf.Max(1, bars)) * Mathf.Max(1, GlobalClock.BeatsPerBar)) * 4);   // v7: a phrase's track = its own beats
        if (ringMesh != null && segments == want) return;
        segments = want;
        if (ringMesh != null) Destroy(ringMesh);
        if (IsMoon) { ringMesh = MeshFactory.SegmentRing(0.972f, 1f, segments, 0.18f); segVerts = 4; }
        else { ringMesh = MeshFactory.SegmentTrack(TrackHX, TrackHZ, TrackR, ProjectConfig.TrackWidth, segments, 0.18f, TrackSub); segVerts = MeshFactory.TrackVertsPerSegment(TrackSub); }
        ringBuiltHZ = TrackHZ;
        ringFilter.sharedMesh = ringMesh;
        ringColors = ringMesh.colors;
        RefreshRingMask(false);
        segA = new Color[segments]; segB = new Color[segments]; segCount = new int[segments];
        for (int s = 0; s < segments; s++) { segA[s] = Dim(s); segB[s] = segA[s]; }
        playheadSeg = -1;
        WriteAllSegments();
        preLightDirty = true;
    }

    static Color Dim(int s) => s % 4 == 0 ? DimBeat : DimSeg;

    void SetSegColor(int s, Color a, Color b)
    {
        int v = s * segVerts, half = segVerts / 2;
        // start half = a, end half = b (the v2 ring: 2 + 2 vertices; the v4 track: (sub + 1) pairs, the middle pair takes a)
        for (int k = 0; k < segVerts; k++) ringColors[v + k] = ringMask != null && v + k < ringMask.Length && ringMask[v + k] ? Color.clear : (k <= half ? a : b);   // v7 §21: a joined end's corner arcs stay dark
    }

    bool[] ringMask; float ringBuiltHZ;
    /// <summary>v7 §21.2 (tests): beat-track vertices hidden at joined ends now (0 when not joined).</summary>
    public int TrackMaskedVerts { get { if (ringMask == null) return 0; int n = 0; foreach (bool b in ringMask) if (b) n++; return n; } }
    /// <summary>v7 §21.2: the beat track's vertices hidden at a joined end — its corner arcs, which would curl out in front of / behind the joint
    /// (the straight lines run on into the neighbour's; its side line lies under the neighbour's platform). <paramref name="write"/>: re-colour now.</summary>
    void RefreshRingMask(bool write)
    {
        bool any = !IsMoon && ringMesh != null && (lgJoinW || lgJoinE);
        if (!any) { if (ringMask == null) return; ringMask = null; }
        else
        {
            var vs = ringMesh.vertices;
            if (ringMask == null || ringMask.Length != vs.Length) ringMask = new bool[vs.Length];
            float ax = TrackHX - TrackR, az = TrackHZ - TrackR;
            for (int i = 0; i < vs.Length; i++)
            {
                var p = vs[i];
                bool corner = Mathf.Abs(p.x) > ax + 0.01f && Mathf.Abs(p.z) > az + 0.01f;
                ringMask[i] = corner && ((p.x > 0f && lgJoinE) || (p.x < 0f && lgJoinW));
            }
        }
        if (write) WriteAllSegments();
    }

    /// <summary>v7 §21.2: the beat track and the core glow around the platform as shown (a long grid's member: its long grid's depth).</summary>
    void FitTrackToPlatform()
    {
        if (IsMoon) return;
        Vector3 off = CenterOffset; float dz = TrackDz;
        ringR = Mathf.Sqrt(TrackHX * TrackHX + TrackHZ * TrackHZ);
        if (ringFilter != null)
        {
            var rt = ringFilter.transform;
            rt.localPosition = new Vector3(off.x, rt.localPosition.y, off.z + dz);
            if (ringMesh != null && Mathf.Abs(ringBuiltHZ - TrackHZ) > 1e-4f) { segments = 0; EnsureRing(); }
        }
        if (coreGlow != null) { var ct = coreGlow.transform; ct.localPosition = new Vector3(off.x, ct.localPosition.y, off.z + dz); ct.localScale = new Vector3(PlatformWidth * 1.75f, 1f, PlatformDepth * 1.75f); }
    }

    void WriteAllSegments()
    {
        if (ringMesh == null) return;
        for (int s = 0; s < segments; s++)
        {
            if (s == playheadSeg) SetSegColor(s, Color.white, Color.white);
            else SetSegColor(s, segA[s], segB[s]);
        }
        ringMesh.colors = ringColors;
    }

    /// <summary>Base colour of every 16th segment (two per segment when two cubes share it: start and end halves). Called by UpdatePreLight.</summary>
    public void SetRingSegments(Color[] perSegment)
    {
        EnsureRing();
        if (ringMesh == null) return;
        for (int s = 0; s < segments; s++)
        {
            Color c = perSegment != null && s < perSegment.Length ? perSegment[s] : Dim(s);
            segA[s] = c; segB[s] = c;
        }
        WriteAllSegments();
    }

    /// <summary>
    /// Recomputes the ring colours from the cubes that hit this island (AudioCube.HitSteps16 through their windows, the
    /// same resolver the audio uses): first cube colours the segment, a second one colours its far half, dim grey otherwise,
    /// beat boundaries slightly brighter. One mesh.colors write.
    /// </summary>
    public void UpdatePreLight()
    {
        preLightDirty = false;
        EnsureRing();
        if (ringMesh == null) return;
        int n = segments;
        for (int s = 0; s < n; s++) { segA[s] = Dim(s); segB[s] = segA[s]; segCount[s] = 0; }
        int loop = GlobalClock.LoopIndex;
        var cubes = SequenceMaster.Cubes;
        for (int ci = 0; ci < cubes.Count; ci++)
        {
            var c = cubes[ci];
            if (c == null || (!c.isFinalized && !c.Hologram) || c.nodes.Count == 0) continue;   // v4: a live draft (hologram) pre-lights its hits too
            for (int w = 0; w < c.windows.Count; w++)
            {
                if (c.windows[w].island != this) continue;
                var steps = c.HitSteps16(w, loop * 64 + c.windows[w].order);
                Color col = c.Color;
                for (int k = 0; k < steps.Count; k++)
                {
                    int s = steps[k];
                    if (s < 0 || s >= n) continue;
                    int cnt = segCount[s];
                    if (cnt == 0) { segA[s] = col; segB[s] = col; }
                    else if (cnt == 1) segB[s] = col;
                    segCount[s] = cnt + 1;
                }
                break;   // one window per island suffices (a Moon cube's bars are identical for the ring)
            }
        }
        WriteAllSegments();
    }

    /// <summary>White playhead segment (-1 none); one colour write per 16th.</summary>
    public void SetPlayheadSegment(int s16)
    {
        if (ringMesh == null) return;
        if (s16 >= segments) s16 = segments - 1;
        if (s16 < -1) s16 = -1;
        if (s16 == playheadSeg) return;
        int prev = playheadSeg;
        playheadSeg = s16;
        if (prev >= 0 && prev < segments) SetSegColor(prev, segA[prev], segB[prev]);
        if (playheadSeg >= 0) SetSegColor(playheadSeg, Color.white, Color.white);
        ringMesh.colors = ringColors;
    }

    // ------------------------------------------------------------------ count-in, hover, drag look, rise, slide
    /// <summary>Count-in pre-glow (0..1) while the comet flies here; fades by itself when no longer refreshed.</summary>
    public void SetPreGlow(float t01) { preGlow = Mathf.Clamp01(t01); preGlowFresh = true; }
    public void SetHover(bool on) { hoverTarget = on ? 1f : 0f; }
    /// <summary>v5: true while the drag look lifts the island (it is being dragged).</summary>
    public bool Lifted => dragLift > 0.001f;
    /// <summary>Drag look: lifts 0.5 u x t01 and tilts 4° x |dir| toward dir (x, z). Visual only: px/pz keep the logical position.</summary>
    public void SetDragLift(float t01, Vector2 dir) { dragLift = Mathf.Clamp01(t01); dragDir = dir; ApplyPose(); }
    /// <summary>Rises from the sea over 0.7 s (new islands and Moons).</summary>
    public void RiseIn() { RiseIn(0f); }
    /// <summary>Rises from the sea over 0.7 s after <paramref name="delay"/> seconds (hidden under the sea until then; the wand staggers them).</summary>
    public void RiseIn(float delay)
    {
        delay = Mathf.Max(0f, delay);
        ClearSea();   // v8: from under the sea too (HideUnderSea / SinkOut)
        riseT = -delay / 0.7f; riseY = -HideDepth; riseRippled = delay <= 0f;
        ApplyPose();
        if (riseRippled) Fx.SeaRipple(Center, chordColor, IsMoon ? 3f : 3.5f);
    }
    public bool Rising => riseT < 1f;

    // ---- v8: in and out of the sea (any island kind) — the present mode's islands, a keyboard's part (KeyStage: "it will appear (pop in from
    //      the ocean) and then disappear after its part … take note from games like geometry dash where it's not just the music but the effects too")
    /// <summary>v8: how deep an island hides under the sea (world units under its place: the RiseIn pose), a pop's overshoot (OutBack strength).</summary>
    public const float SeaHide = 6f, PopOvershoot = 1.7f;
    /// <summary>v8: how deep THIS island goes to be all the way under the sea: at least SeaHide, and enough that its tallest tile (+ a cube on it)
    /// on its ground ends HideMargin under the sea's surface (a tall grid or a raised ground would poke out at SeaHide).</summary>
    public float HideDepth
    {
        get
        {
            // v9 perf (S17: the every-2-s re-measure over 190 islands bunched into one frame): re-measured when the tiles change (a build, a key
            // range, a re-pitch: InvalidateHideDepth), with a slow safety pass every HideRemeasureS staggered per island (never all in one frame)
            if (Time.frameCount != hideFrame)
            {
                hideFrame = Time.frameCount;
                if (hideAt < -90f) hideAt = Time.unscaledTime - (GetInstanceID() & 1023) / 1024f * HideRemeasureS;
                if (hideDirty || tiles.Count != hideTiles || Time.unscaledTime - hideAt > HideRemeasureS) { hideDirty = false; hideTiles = tiles.Count; hideAt = Time.unscaledTime; topAbove = TopAboveBase(); HideMeasures++; }
            }
            return Mathf.Max(SeaHide, basePos.y + groundY + topAbove - SeaSurfaceY + HideMargin);
        }
    }
    /// <summary>v8: the sea's surface (world y) and how far under it a hidden island's top ends.</summary>
    public const float SeaSurfaceY = -4f, HideMargin = 0.6f;
    int hideFrame = -1, hideTiles = -1; float hideAt = -99f, topAbove = 1.5f; bool hideDirty = true;
    /// <summary>v9 perf: the safety re-measure period of <see cref="HideDepth"/> (s; staggered per island).</summary>
    public const float HideRemeasureS = 10f;
    /// <summary>v9 perf: HideDepth re-measures since Play (tests).</summary>
    public static int HideMeasures;
    /// <summary>v9 perf: the tiles changed height (a re-pitch, a relayout): HideDepth re-measures on its next read.</summary>
    public void InvalidateHideDepth() { hideDirty = true; }
    static readonly System.Diagnostics.Stopwatch seaWatch = new System.Diagnostics.Stopwatch();
    /// <summary>v9 perf: Stopwatch ticks every island spent in UpdateSea + UpdateWater since Play (tests sample its change per frame).</summary>
    public static long SeaTicks => seaWatch.ElapsedTicks;
    /// <summary>v8: the tallest tile top over the island's base (island-local y, + a cube's height on it; a keyboard: its rail too).</summary>
    float TopAboveBase()
    {
        float top = ProjectConfig.TileThickness;
        foreach (var t in tiles) if (t != null) top = Mathf.Max(top, transform.InverseTransformPoint(t.Top).y);
        if (keyRail != null) top = Mathf.Max(top, keyRail.localPosition.y + keyRailH * 0.5f);
        return top + 0.8f;
    }
    float seaDepthIn, partDepth;
    /// <summary>v8: a POSE input, 0 = in its place .. 1 = all the way under the sea (SeaHide under it), set every frame by whoever drives it from the
    /// song beat (the present mode) — pause, seek and replay stay exact. Composes with RiseIn, the timed SinkOut / PopIn, KeyStage's
    /// <see cref="PartDepth"/>, the ride and the drag swing. Over 0.5 the island takes no clicks.</summary>
    public float SeaDepth { get => seaDepthIn; set { float v = Mathf.Clamp01(value); if (Mathf.Abs(v - seaDepthIn) < 1e-5f) return; seaDepthIn = v; SeaPosed(); } }
    /// <summary>v8: KeyStage's pose input for a keyboard's part (its pop in / sink out as a function of the song beat; slightly below 0 = the pop's
    /// overshoot above its place). The deeper of this and <see cref="SeaDepth"/> wins.</summary>
    public float PartDepth { get => partDepth; set { float v = Mathf.Clamp(value, -0.2f, 1f); if (Mathf.Abs(v - partDepth) < 1e-5f) return; partDepth = v; SeaPosed(); } }
    /// <summary>v8: going under (and coming up) the island lists: one end down ListDeg, the front edge RollDeg, a drift of DriftDeg round; off with
    /// <see cref="SeaTilt"/> = false (straight down).</summary>
    public const float ListDeg = 7f, RollDeg = 4f, DriftDeg = 5f;
    /// <summary>v8: true (default): tilts while it goes under / comes up.</summary>
    public bool SeaTilt = true;
    /// <summary>v8: true (default): the water shows it (ring waves off its edges as it crosses the floor, foam and a swirl where it disappears,
    /// bubbles after, a splash as it breaks the surface) for KeyStage's parts and SinkOut / PopIn; <see cref="SeaDepth"/> (the present mode) shows
    /// them only when this is on AND <see cref="SeaDepthWater"/> is.</summary>
    public bool SeaWater = true;
    public static bool SeaDepthWater = false;
    /// <summary>v8: the height of the surface it sinks through (its section's floor; world y).</summary>
    public float SurfaceY => basePos.y + groundY - SectionPlinth.TopDrop + 0.02f - SectionPlinth.SinkOf(column);
    /// <summary>v8: the depth both pose inputs give now (0..1; below 0 = a pop's overshoot).</summary>
    public float PoseDepth => seaDepthIn > 0f ? Mathf.Max(seaDepthIn, partDepth) : partDepth;
    void SeaPosed() { if (seaMode != 2) SetPickable(PoseDepth < 0.5f); ApplyPose(); RefreshCubeLines(); }
    float seaY, seaT = 9f, seaDur = 0.5f, seaFrom, seaTo; int seaMode;   // seaMode: 0 none / up, 1 sinking, 2 under, 3 popping
    bool pickOff; Collider[] pickCache; int pickChildren = -1, pickTiles = -1;
    /// <summary>v8: true when the island is all the way under the sea (HideUnderSea, or a SinkOut that finished); its colliders are off.</summary>
    public bool UnderSea => seaMode == 2;
    /// <summary>v8: true while a SinkOut runs; true while a PopIn runs.</summary>
    public bool Sinking => seaMode == 1;
    public bool Popping => seaMode == 3;
    /// <summary>v8: the island's drop under its place now (world units; HideUnderSea / SinkOut / PopIn and the pose inputs).</summary>
    public float SeaDrop => -seaY + HideDepth * PoseDepth;

    /// <summary>v8: under the sea at once (no splash), colliders off — e.g. the start of the present mode. PopIn / RiseIn bring it back.</summary>
    public void HideUnderSea()
    {
        riseT = 9f; riseY = 0f;
        seaMode = 2; seaT = 9f; seaY = -HideDepth;
        SetPickable(false);
        ApplyPose();
    }

    /// <summary>v8: sinks under the sea over <paramref name="seconds"/>: a little bob up, then down, accelerating, with a splash as it goes under;
    /// its colliders go off at the end.</summary>
    public void SinkOut(float seconds)
    {
        if (seaMode == 2 || seaMode == 1) return;
        riseT = 9f; riseY = 0f;
        seaMode = 1; seaT = 0f; seaDur = Mathf.Max(0.05f, seconds); seaFrom = seaY; seaTo = -HideDepth;
        ApplyPose();
    }

    /// <summary>v8: pops up from under the sea over <paramref name="seconds"/>: a splash, up past its place (overshoot) and settling; colliders on.</summary>
    public void PopIn(float seconds)
    {
        if (seaMode == 0 && seaY == 0f) return;
        if (seaMode == 3) return;
        riseT = 9f; riseY = 0f;
        seaMode = 3; seaT = 0f; seaDur = Mathf.Max(0.05f, seconds); seaFrom = Mathf.Min(seaY, -0.01f); seaTo = 0f;
        SetPickable(true);   // the splash: UpdateWater, as it breaks the surface
        ApplyPose();
    }

    /// <summary>v8: back in its place at once (no animation, no splash): leaving the present mode, a reset. Clears HideUnderSea / SinkOut / PopIn,
    /// a RiseIn in progress and the <see cref="SeaDepth"/> input (KeyStage's <see cref="PartDepth"/> stays: it follows the song).</summary>
    public void SurfaceNow() { riseT = 9f; riseY = 0f; seaDepthIn = 0f; ClearSea(); ApplyPose(); RefreshCubeLines(); }

    /// <summary>v8: back to its place at once, nothing under the sea (RiseIn starts from here).</summary>
    void ClearSea() { seaMode = 0; seaT = 9f; seaY = 0f; SetPickable(PoseDepth < 0.5f); }

    /// <summary>v8: an island under the sea takes no clicks (its platform's and tiles' colliders off).</summary>
    void SetPickable(bool on)
    {
        if (pickOff == !on) return;
        pickOff = !on;
        // v8: the colliders cached (no allocation per crossing; re-read when the island's children changed: keys added, cubes landed)
        if (pickCache == null || pickChildren != transform.childCount || pickTiles != tiles.Count) { pickCache = GetComponentsInChildren<Collider>(true); pickChildren = transform.childCount; pickTiles = tiles.Count; }
        foreach (var c in pickCache) if (c != null) c.enabled = on;
    }

    float waterD, waterT, bubbleT, bubbleEvery; int waterI;
    /// <summary>v8: the depth the water shows (0..1): the timed sink / pop, KeyStage's part, and the present mode's SeaDepth when SeaDepthWater.</summary>
    float WaterDepth => seaY == 0f && (!SeaDepthWater || seaDepthIn <= 0f) ? Mathf.Clamp01(partDepth) : Mathf.Clamp01(-seaY / HideDepth + Mathf.Max(0f, SeaDepthWater ? PoseDepth : partDepth));   // v9 perf: no HideDepth while in its place
    /// <summary>v8: water ring splashes / sinks so far (tests).</summary>
    public static int WaterWaves, WaterSplashes, WaterFoams;

    void UpdateWater(float dt)
    {
        float d = WaterDepth, dd = d - waterD, prev = waterD;
        waterD = d;
        if (!SeaWater || !hasBase) return;
        float y = SurfaceY, e = EdgeInset, w = Width, dp = Depth;
        Vector3 o = basePos + slideOff;
        Color foam = Color.Lerp(Look.SeaLine, Color.white, 0.6f);
        // ring waves off its edges while it crosses the floor (either way)
        if (d > 0.03f && d < 0.95f && Mathf.Abs(dd) > 1e-4f)
        {
            waterT += dt;
            while (waterT >= 0.09f)
            {
                waterT -= 0.09f;
                int i = waterI++ % 6;
                float fx = i % 3 == 0 ? 0.15f : (i % 3 == 1 ? 0.5f : 0.85f), fz = i < 3 ? -0.15f : 1.15f;
                Fx.Ripple(new Vector3(o.x - e + w * fx, y, o.z - e + dp * fz), foam, 1.1f + 0.6f * (1f - Mathf.Abs(0.5f - d) * 2f), 0.9f);
                WaterWaves++;
            }
        }
        else waterT = 0f;
        Vector3 c = new Vector3(o.x - e + w * 0.5f, y, o.z - e + dp * 0.5f);
        // all the way under: foam and a swirl where it disappeared, then bubbles
        if (prev < 0.95f && d >= 0.95f && dd > 0f)
        {
            Fx.Burst(c, foam, 16, 2.2f); Fx.Ripple(c, foam, 2.6f, 1.1f); Fx.Ripple(c, Color.Lerp(foam, chordColor, 0.3f), 1.6f, 0.7f);
            bubbleT = 0.8f; bubbleEvery = 0f; WaterFoams++;
        }
        // breaking the surface on the way up: a splash
        if (prev > 0.3f && d <= 0.3f && dd < 0f)
        {
            Fx.SeaRipple(c, chordColor, IsMoon ? 3f : 3.5f); Fx.Ripple(c, foam, 3f, 0.9f);
            Fx.Burst(c, foam, 14, 3.2f); WaterSplashes++;
        }
        if (bubbleT > 0f)
        {
            bubbleT -= dt; bubbleEvery -= dt;
            if (bubbleEvery <= 0f)
            {
                bubbleEvery = 0.08f;
                Vector3 b = new Vector3(o.x - e + w * UnityEngine.Random.Range(0.2f, 0.8f), y, o.z - e + dp * UnityEngine.Random.Range(0.25f, 0.75f));
                Fx.Burst(b, Color.white, 3, 1.1f);
            }
        }
    }

    void UpdateSea(float dt)
    {
        if (seaMode != 1 && seaMode != 3) return;
        seaT = Mathf.Min(1f, seaT + dt / seaDur);
        if (seaMode == 1)
        {
            // a little bob up (0 .. 0.25), then down (InCubic)
            float u = seaT;
            seaY = u < 0.25f ? seaFrom + 0.22f * Mathf.Sin(u / 0.25f * Mathf.PI * 0.5f) : Mathf.Lerp(seaFrom + 0.22f, seaTo, Ease.InCubic((u - 0.25f) / 0.75f));
            if (seaT >= 1f) { seaY = seaTo; seaMode = 2; SetPickable(false); }   // the foam: UpdateWater
        }
        else
        {
            seaY = Mathf.LerpUnclamped(seaFrom, seaTo, Ease.OutBack(seaT, PopOvershoot));
            if (seaT >= 1f) { seaY = 0f; seaMode = 0; SetPickable(PoseDepth < 0.5f); }
        }
        ApplyPose();
    }

    /// <summary>Visual slide (merge snap-in, split): the island shows <paramref name="from"/> away from its logical position and eases home over
    /// <paramref name="seconds"/> (OutBack when <paramref name="overshoot"/>, else OutCubic). Cube paths and the cable follow.</summary>
    public void SlideFrom(Vector3 from, float seconds, bool overshoot = true)
    {
        from.y = 0f;
        slideFrom = from; slideDur = Mathf.Max(0.05f, seconds); slideT = 0f; slideBack = overshoot; slideOff = from;
        ApplyPose();
        RefreshCubeLines();
    }

    void RefreshCubeLines()
    {
        lineWatch.Start(); LineRefreshes++;
        var cubes = SequenceMaster.Cubes;
        for (int i = 0; i < cubes.Count; i++) { var c = cubes[i]; if (c != null && (c.Island == this || c.Moon == this)) c.RefreshLine(); }
        lineWatch.Stop();
    }
    static readonly System.Diagnostics.Stopwatch lineWatch = new System.Diagnostics.Stopwatch();
    /// <summary>v9 perf: Stopwatch ticks spent re-placing cube lines after a pose change, and how many times (tests).</summary>
    public static long LineTicks => lineWatch.ElapsedTicks;
    public static int LineRefreshes;

    void ApplyPose()
    {
        if (!hasBase) return;
        Quaternion rot = Quaternion.identity;
        float tilt = Mathf.Min(1f, dragDir.magnitude);
        if (dragLift > 0.001f && tilt > 0.001f)
        {
            Vector3 dir = new Vector3(dragDir.x, 0f, dragDir.y).normalized;
            rot = Quaternion.AngleAxis(4f * tilt * dragLift, Vector3.Cross(Vector3.up, dir));
        }
        Vector3 off = CenterOffset;
        // v8: a keyboard dragged to its next checkpoint swings a little counter-clockwise about its grab point on the front edge
        if (Mathf.Abs(dragYawShown) > 1e-3f && dragLift <= 0.001f)
        {
            rot = Quaternion.AngleAxis(dragYawShown, Vector3.up);
            off = new Vector3(-EdgeInset + Width * Belt.GrabX, 0f, -EdgeInset);
        }
        // v8 (the user: "when the piano sinks it should like rotate a tiny bit too … so it doesnt just go straight down, with wave animations"): going
        // under the sea it lists like a boat — one end dips first, the front edge rolls a little, a slow drift round — growing over the first part
        // of the dive and held as it goes under; coming up it is the same curve backwards (it levels out as it breaks the surface)
        float hd = HideDepth;
        float sd = SeaTilt ? Mathf.Clamp01(-seaY / hd + Mathf.Max(0f, PoseDepth)) : 0f;
        if (sd > 1e-3f)
        {
            float a = Mathf.SmoothStep(0f, 1f, sd / 0.45f), side = (measureIndex * 7 + 3) % 2 == 0 ? 1f : -1f;
            rot = Quaternion.AngleAxis(side * ListDeg * a, Vector3.forward) * Quaternion.AngleAxis(RollDeg * a, Vector3.right) * Quaternion.AngleAxis(-side * DriftDeg * sd, Vector3.up) * rot;
            off = CenterOffset;
        }
        transform.rotation = rot;
        Vector3 shake = shakeT < 1f ? new Vector3(Mathf.Sin(shakeT * 38f) * shakeAmp * (1f - shakeT), 0f, 0f) : Vector3.zero;
        Vector3 ride = IsMoon ? new Vector3(moonRide, 0f, 0f) : BeltOffset;   // v5: the island rides its belt (the belt, a child, stays put); v6 §11: a Moon follows the song
        Vector3 rest = basePos + slideOff + previewOff + shake + Vector3.up * (0.5f * dragLift + riseY + seaY - hd * PoseDepth + regShown + groundY + fxLift);   // v7: + the ground, W's lift; v8: + under the sea
        transform.position = rest + ride + (off - rot * off);
        if (belt != null) { belt.transform.localPosition = Quaternion.Inverse(rot) * -ride; belt.SetHome(basePos + slideOff + previewOff, basePos.y + groundY - SectionPlinth.TopDrop + 0.03f); }   // (Belt follows the present mode's floor sink live)   // v8: the circles lie on the section's floor, never shaken or lifted
    }

    /// <summary>
    /// v5 (SPEC v5 §2.5): the ride this frame. The target is SongManager.RideSlot (a pure function of the song beat: pass p at slot p, the jerk at
    /// each pass boundary, the glide back after the last pass; 0 when stopped, while dragged or presenting). A jump of the target that the beat's
    /// own progress cannot explain (a seek, a stop, an undo, a timeline change: more than RideMaxSlope slots per beat) glides there instead of
    /// snapping. The belt's surface follows the island while it carries it (not during a glide back or a catch-up).
    /// </summary>
    void UpdateRide(float dt)
    {
        if (IsMoon) { UpdateMoonRide(dt); return; }
        var sm = SongManager.I;
        if (!HasBelt && rideShown == 0f && rideLift == 0f && !rideCatch && resetT0 < 0f) { Riding = false; return; }
        float lift = 0f, target = 0f;
        double beat = GlobalClock.SongBeatD;
        if (sm != null && HasBelt && dragLift <= 0.001f) target = IsKeyboard ? KeyboardRideSlot(beat, out lift) : sm.RideSlot(this, beat, out lift);   // v8: a keyboard is dragged at a constant speed
        float prev = rideShown, prevLift = rideLift;
        bool catching = rideCatch;
        float ru = HasBelt || rideShown > 0f ? ResetU(ProjectConfig.BeltGlideBeats) : -1f;
        if (ru >= 0f && resetRideFrom > 1e-3f)
        {
            // v7 §12.2: a stop — the belt carries the island home in real time, slightly out of sync with the others
            rideShown = resetRideFrom * (1f - Ease.InOutCubic(ru)); rideLift = ProjectConfig.BeltGlideLift * Mathf.Sin(ru * Mathf.PI);
            dragYawShown = 0f;   // v8: no swing on the way home
            rideCatch = false; rideLastTarget = 0f; rideLastBeat = beat;
            if (belt != null) belt.Ride(rideShown, false);
            Riding = Mathf.Abs(rideShown - prev) > 1e-5f || Mathf.Abs(rideLift - prevLift) > 1e-5f;
            if (Riding) { ApplyPose(); RefreshCubeLines(); }
            return;
        }
        if (!rideInit) { rideInit = true; rideShown = target; rideLift = lift; }
        else
        {
            double db = beat - rideLastBeat;
            bool jumped = db < -1e-6 || db > 0.5;   // the beat itself jumped (a seek, a wrap): any change of the target is a jump
            if (!rideCatch && Mathf.Abs(target - rideLastTarget) > (jumped ? 0f : RideMaxSlope * (float)db) + 0.02f) rideCatch = true;
            if (rideCatch)
            {
                float k = 1f - Mathf.Exp(-dt * 14f);
                rideShown = Mathf.Lerp(rideShown, target, k); rideLift = Mathf.Lerp(rideLift, lift, k);
                if (Mathf.Abs(target - rideShown) < 0.01f) { rideShown = target; rideLift = lift; rideCatch = false; }
            }
            else { rideShown = target; rideLift = lift; }
        }
        float prevTarget = rideLastTarget;
        rideLastTarget = target; rideLastBeat = beat;
        if (belt != null) belt.Ride(rideShown, !catching && !rideCatch && rideLift <= 1e-4f);
        bool yawMoved = false;
        if (belt != null && belt.Checkpoints)
        {
            // v8: dragged across the floor at a constant speed — it leans counter-clockwise (pulled from its grab point) with a slow sway while it
            // moves, a wake of ripples round it; both follow the pure ride (its speed a hair back in time), never the glide home
            float l2, speed01 = 0f;
            if (sm != null && dragLift <= 0.001f && lift <= 1e-4f && dragCruise > 1e-4f)
            {
                float back = KeyboardRideSlot(beat - 0.05, out l2);
                speed01 = Mathf.Clamp01((target - back) / 0.05f / dragCruise);
            }
            float want = -(Belt.SwayBase + Belt.SwayAmp * Mathf.Sin((float)beat * Mathf.PI * 2f / Belt.SwayBeats)) * speed01;
            float y0 = dragYawShown;
            dragYawShown = Mathf.Lerp(dragYawShown, want, 1f - Mathf.Exp(-dt * 6f));
            if (Mathf.Abs(dragYawShown) < 1e-3f && Mathf.Abs(want) < 1e-3f) dragYawShown = 0f;
            yawMoved = Mathf.Abs(dragYawShown - y0) > 1e-5f;
            dragSpeedShown = speed01;
            if (GlobalClock.IsPlaying) belt.Wake(speed01, dt);
        }
        Riding = Mathf.Abs(rideShown - prev) > 1e-5f || Mathf.Abs(rideLift - prevLift) > 1e-5f || yawMoved;
        if (Riding) { ApplyPose(); RefreshCubeLines(); }
    }

    /// <summary>v6 §11.3: the x offset a Moon shows from its resting place this frame (it follows the lit column while its section plays).</summary>
    public float MoonRideX => moonRide;

    /// <summary>v6 §11.3: a Moon's follow this frame: the target is SongManager.MoonFollowX (a pure function of the song beat; 0 when stopped or
    /// dragged); a change the beat's progress cannot explain (a seek, a stop, a wrap, a relayout) glides there. Its tiles, cubes and bead ride along.</summary>
    void UpdateMoonRide(float dt)
    {
        var sm = SongManager.I;
        double beat = GlobalClock.SongBeatD;
        float target = sm != null && dragLift <= 0.001f ? sm.MoonFollowX(this, beat) : 0f;
        float prev = moonRide;
        float mu = Mathf.Abs(resetMoonFrom) > 1e-3f ? ResetU(SongManager.MoonStepBeats(Mathf.Abs(resetMoonFrom))) : -1f;
        if (mu >= 0f)
        {
            // v7 §12.4: a stop — the Moon glides back to its start column in real time
            moonRide = resetMoonFrom * (1f - Mathf.SmoothStep(0f, 1f, mu)); moonRideCatch = false; moonRideLast = 0f; moonRideBeat = beat;
            if (mu >= 1f) resetT0 = -1f;
            Riding = Mathf.Abs(moonRide - prev) > 1e-5f;
            if (Riding) { ApplyPose(); RefreshCubeLines(); }
            return;
        }
        if (!moonRideInit) { moonRideInit = true; moonRide = target; moonRideCatch = false; }
        else
        {
            double db = beat - moonRideBeat;
            bool jumped = db < -1e-6 || db > 0.5;
            float allow = jumped ? 0f : 160f * (float)System.Math.Max(0.0, db);   // the steepest step: 2 beats for a long way home
            if (!moonRideCatch && Mathf.Abs(target - moonRideLast) > allow + 0.05f) moonRideCatch = true;
            if (moonRideCatch)
            {
                moonRide = Mathf.Lerp(moonRide, target, 1f - Mathf.Exp(-dt * 10f));
                if (Mathf.Abs(target - moonRide) < 0.02f) { moonRide = target; moonRideCatch = false; }
            }
            else moonRide = target;
        }
        moonRideLast = target; moonRideBeat = beat;
        Riding = Mathf.Abs(moonRide - prev) > 1e-5f;
        if (Riding) { ApplyPose(); RefreshCubeLines(); }
    }

    // ------------------------------------------------------------------ v3 group look (SPEC v3 §3.3)
    /// <summary>
    /// Member look on/off: a glowing outline around this platform's top rim in its chord colour (a slow flow; where two members
    /// touch, their two lines run side by side = the chord divide) and, when <paramref name="right"/> is its east neighbour in the
    /// same group, a seam bridge filling the rounded-corner notch between the two platforms. Singletons keep the v2 look.
    /// </summary>
    public void SetGroupLook(bool member, KeyBlock right)
    {
        if (IsMoon) { member = false; right = null; }
        groupLook = member;
        // a member's beat ring hugs its own platform (an ellipse just outside the edges) instead of the circle that would reach under its neighbours
        // (v4: the beat track already hugs the platform; only the v2 ring of a Moon is scaled, and Moons never join a group)
        if (ringFilter != null && IsMoon) ringFilter.transform.localScale = new Vector3(ringR, 1f, ringR);
        if (member) { if (rim == null) BuildRim(); }
        else if (rim != null) { Destroy(rim.gameObject); rim = null; Kill(rimMat); rimMat = null; }
        if (right != null && Mathf.Abs(right.WestEdge - EastEdge) > 0.05f) right = null;   // v5: a belt between the two: the neighbour waits at its end (no bridge)
        if (right == null || right == this)
        {
            if (bridge != null) Destroy(bridge.gameObject);
            bridge = null; Kill(bridgeMat); bridgeMat = null; bridgeTo = null;
        }
        else FitBridge(right);
    }

    /// <summary>The outline draws itself in over <paramref name="seconds"/> (merge).</summary>
    public void DrawRimIn(float seconds)
    {
        if (rim == null) return;
        rimDraw = 0f; rimDrawRate = 1f / Mathf.Max(0.05f, seconds);
        ApplyRim();
    }

    /// <summary>The seam bridge grows in after <paramref name="delay"/> seconds (so it appears when the sliding member lands).</summary>
    public void ShowBridgeAfter(float delay)
    {
        if (bridge == null) return;
        bridgeShow = 0f; bridgeDelay = Mathf.Max(0f, delay);
        bridge.localScale = new Vector3(0.001f, 1f, 1f);
    }

    /// <summary>Merge: a white flash sweeps this island's east seam back to front (0.35 s) with sparkles along it.</summary>
    public void FlashSeam(KeyBlock right, float delay)
    {
        var go = new GameObject("SeamSweep");
        go.transform.SetParent(transform, false);
        go.AddComponent<SeamSweep>().Init(this, right, delay);
    }

    void BuildRim()
    {
        var go = new GameObject("MemberRim");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // TransformZ alignment with +Z up: the ribbon lies flat on the platform
        rim = go.AddComponent<LineRenderer>();
        rim.useWorldSpace = false; rim.loop = false; rim.alignment = LineAlignment.TransformZ;
        rim.widthMultiplier = ProjectConfig.RimWidth; rim.textureMode = LineTextureMode.Tile;
        rim.numCornerVertices = 2; rim.numCapVertices = 0;
        rim.shadowCastingMode = ShadowCastingMode.Off; rim.receiveShadows = false; rim.lightProbeUsage = LightProbeUsage.Off;
        // a crisp, flat colour line (hard edges: the chord divide reads as ink) with short gaps drifting slowly around the rim (the flow)
        rimMat = Fx.Alpha(RimTexture, Palette.A(Color.Lerp(chordColor, Color.white, 0.45f), 1f));
        rimMat.mainTextureScale = new Vector2(0.35f, 1f);
        rim.sharedMaterial = rimMat;
        // rounded rectangle inset from the platform edge: from the front centre, clockwise seen from above (the comet's lap direction)
        float ins = ProjectConfig.RimInset, r = 0.2f, y = -0.012f;
        float px0 = -EdgeInset + (Width - PlatformWidth) * 0.5f;   // v9: around a long keyboard's hugging platform
        float x0 = px0 + ins, x1 = px0 + PlatformWidth - ins, z0 = -EdgeInset + ins, z1 = -EdgeInset + Depth - ins;
        const int arc = 5;
        var pts = new List<Vector3>(4 * (arc + 1) + 2);
        pts.Add(new Vector3((x0 + x1) * 0.5f, y, z0));
        AddArc(pts, x0 + r, z0 + r, r, -90f, -180f, arc, y);
        AddArc(pts, x0 + r, z1 - r, r, 180f, 90f, arc, y);
        AddArc(pts, x1 - r, z1 - r, r, 90f, 0f, arc, y);
        AddArc(pts, x1 - r, z0 + r, r, 0f, -90f, arc, y);
        pts.Add(new Vector3((x0 + x1) * 0.5f, y, z0));
        var toRim = Quaternion.Euler(90f, 0f, 0f);   // island-local -> rim-local (the rim transform is rotated -90° about x)
        rimPts = new Vector3[pts.Count]; rimTmp = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++) rimPts[i] = toRim * pts[i];
        rimDraw = 1f;
        ApplyRim();
    }

    static Texture2D rimTex;
    /// <summary>The rim's dash texture (repeat-wrapped: a solid run with a short gap), tiled once per ~2.9 u along the outline.</summary>
    static Texture2D RimTexture
    {
        get
        {
            if (rimTex != null) return rimTex;
            const int n = 64;
            rimTex = new Texture2D(n, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "deacube_rim" };
            var px = new Color32[n * 2];
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                float a = Mathf.Clamp01((0.86f - u) * n) * Mathf.Clamp01(u * n + 0.5f);
                byte b = (byte)Mathf.RoundToInt(a * 255f);
                px[x] = new Color32(255, 255, 255, b); px[n + x] = new Color32(255, 255, 255, b);
            }
            rimTex.SetPixels32(px); rimTex.Apply(false, false);
            return rimTex;
        }
    }

    static void AddArc(List<Vector3> pts, float cx, float cz, float r, float a0, float a1, int steps, float y)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
            pts.Add(new Vector3(cx + Mathf.Cos(a) * r, y, cz + Mathf.Sin(a) * r));
        }
    }

    void ApplyRim()
    {
        if (rim == null || rimPts == null) return;
        int n = rimPts.Length;
        if (rimDraw >= 1f) { rim.positionCount = n; rim.SetPositions(rimPts); return; }
        float f = Mathf.Clamp01(rimDraw) * (n - 1);
        int k = Mathf.FloorToInt(f);
        for (int i = 0; i <= k && i < n; i++) rimTmp[i] = rimPts[i];
        int count = k + 1;
        if (k + 1 < n) { rimTmp[k + 1] = Vector3.Lerp(rimPts[k], rimPts[k + 1], f - k); count = k + 2; }
        rim.positionCount = Mathf.Max(2, count);
        if (count < 2) rimTmp[1] = rimTmp[0];
        rim.SetPositions(rimTmp);
    }

    void FitBridge(KeyBlock right)
    {
        bridgeTo = right;
        float thick = ProjectConfig.PlatformThickness;
        float len = Mathf.Min(Depth, right.Depth);
        if (bridge == null)
        {
            var go = new GameObject("SeamBridge");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.On;
            bridge = go.transform;
            bridgeShow = 1f; bridgeDelay = 0f;
        }
        Color c = Color.Lerp(Color.Lerp(PlatformColor, right.PlatformColor, 0.5f), new Color(0.05f, 0.04f, 0.09f), 0.55f);   // a dark groove between the two rim lines
        if (bridgeMat == null) { bridgeMat = Fx.Lit(c, 0.32f, 0f); bridge.GetComponent<MeshRenderer>().sharedMaterial = bridgeMat; }
        else bridgeMat.SetColor("_BaseColor", c);
        bridge.GetComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(ProjectConfig.SeamBridgeWidth, thick - 0.01f, Mathf.Max(0.1f, len - 0.02f)), 0.04f, 2);
        bridge.localPosition = new Vector3(-EdgeInset + Width, -thick * 0.5f - 0.035f, -EdgeInset + len * 0.5f);
        bridge.localScale = new Vector3(Mathf.Max(0.001f, bridgeShow), 1f, 1f);
    }

    /// <summary>Group members: the comet laps along the platform margin (a rectangle LapInset inside the edge, radial projection of the
    /// ring angle) so it never crosses a neighbour's tiles; everyone else laps the beat ring (v2).</summary>
    public Vector3 LapPoint(float t01, float y)
    {
        if (!groupLook || IsMoon) return RingPoint(t01, y);
        float a = -Mathf.PI * 0.5f - t01 * Mathf.PI * 2f;
        float dx = Mathf.Cos(a), dz = Mathf.Sin(a);
        float hx = Width * 0.5f - ProjectConfig.LapInset, hz = Depth * 0.5f - ProjectConfig.LapInset;
        float s = Mathf.Min(hx / Mathf.Max(1e-4f, Mathf.Abs(dx)), hz / Mathf.Max(1e-4f, Mathf.Abs(dz)));
        Vector3 c = VisualCenter;
        return new Vector3(c.x + dx * s, transform.position.y + y, c.z + dz * s);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.4f);
        hover = Mathf.Lerp(hover, hoverTarget, 1f - Mathf.Exp(-dt * 12f));
        if (preGlowFresh) preGlowFresh = false; else preGlow = Mathf.MoveTowards(preGlow, 0f, dt * 3f);
        seaWatch.Start();
        UpdateSea(dt);   // v8
        UpdateWater(dt);
        seaWatch.Stop();
        if (riseT < 1f)
        {
            riseT = Mathf.Min(1f, riseT + dt / 0.7f);
            if (!riseRippled && riseT >= 0f) { riseRippled = true; Fx.SeaRipple(Center, chordColor, IsMoon ? 3f : 3.5f); }
            riseY = -HideDepth * (1f - Ease.OutCubic(Mathf.Clamp01(riseT)));   // v8: deep enough for its tallest tile
            ApplyPose();
            if (riseT >= 1f) { riseY = 0f; ApplyPose(); Fx.IslandCelebrate(this); }
        }
        if (slideT < 1f)
        {
            slideT = Mathf.Min(1f, slideT + dt / slideDur);
            float e = slideBack ? Ease.OutBack(slideT) : Ease.OutCubic(slideT);
            slideOff = slideT >= 1f ? Vector3.zero : slideFrom * (1f - e);
            ApplyPose();
            RefreshCubeLines();
        }
        UpdateTower(dt);
        if (UpdateGround(dt)) Lifting = true;   // v7: the ground glide moves the platform like the tower (the Route refits, the cubes follow)
        if (keySlideT < 1f)
        {
            // v7 §20.1: the keys slide into their new range (0.35 s, ease out); the cubes on them follow (they stand on the keys' tops)
            keySlideT = Mathf.Min(1f, keySlideT + dt / 0.35f);
            ApplyKeySlide(Ease.OutCubic(keySlideT));
            RefreshCubeLines();
        }
        if (shakeT < 1f) { shakeT = Mathf.Min(1f, shakeT + dt / 0.32f); ApplyPose(); }
        UpdateRide(dt);
        EndResetIfHome();
        Shifting = false;
        if ((previewOff - previewTo).sqrMagnitude > 1e-8f)
        {
            previewOff = Vector3.Lerp(previewOff, previewTo, 1f - Mathf.Exp(-dt * 16f));
            if ((previewOff - previewTo).sqrMagnitude < 1e-4f) previewOff = previewTo;
            Shifting = true;
            ApplyPose();
            RefreshCubeLines();
        }
        if (!IsMoon && sleep != sleepShown) ApplySleepLook(sleep);
        if (rim != null && rimDraw < 1f) { rimDraw = Mathf.Min(1f, rimDraw + dt * rimDrawRate); ApplyRim(); }
        if (rimMat != null) rimMat.mainTextureOffset = new Vector2(-Time.time * 0.09f, 0f);   // the slow flow around the rim
        if (bridge != null && bridgeShow < 1f)
        {
            if (bridgeDelay > 0f) bridgeDelay -= dt;
            else { bridgeShow = Mathf.Min(1f, bridgeShow + dt / 0.2f); bridge.localScale = new Vector3(Mathf.Max(0.001f, Ease.OutBack(bridgeShow)), 1f, 1f); }
        }

        // where inside the measure (or bar, for Moons) are we
        bool lit = false; int seg = -1; int bar = -1; float barT = 0f;
        if (GlobalClock.IsPlaying)
        {
            int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
            if (IsMoon)
            {
                float sb = GlobalClock.SongBeat;
                float bl = sb - Mathf.Floor(sb / bpb) * bpb;
                lit = SongManager.I == null || SongManager.I.MoonPlaysAt(this, sb);   // v6 §11: its bead laps while its section plays
                if (lit) { seg = Mathf.FloorToInt(bl * 4f); barT = bl / bpb; }
            }
            else
            {
                // v5: the track's playhead runs once per pass the island plays (the track shows one pass); dark in the passes it rests
                float local = GlobalClock.SongBeat - startBeatOffset, pl = LengthBeats;
                if (local >= 0f && local < pl * Passes)
                {
                    float inPass = local - Mathf.Floor(local / pl) * pl;
                    lit = true; seg = Mathf.FloorToInt(inPass * 4f); bar = Mathf.FloorToInt(inPass / bpb);
                }
            }
        }

        if (platform != null)
        {
            float e = (0.06f + pulse * pulse * 0.9f + hover * 0.12f + preGlow * 0.18f) * (sleepShown ? 0.2f : 1f);
            mpb.SetColor(EmissionId, chordColor * e);
            platform.SetPropertyBlock(mpb);
        }
        if (ringRend != null)
        {
            EnsureRing();
            // a deleted cube leaves the registry at the end of its frame without an event: re-light when the count changes
            if (SequenceMaster.Cubes.Count != lastCubeCount) { lastCubeCount = SequenceMaster.Cubes.Count; preLightDirty = true; }
            if (preLightDirty) UpdatePreLight();
            SetPlayheadSegment(seg);
            ringMpb.SetFloat(IntensityId, ((lit ? 1.0f : 0.62f) + pulse * 0.5f + hover * 0.25f) * (sleepShown ? 0.35f : 1f));
            ringRend.SetPropertyBlock(ringMpb);
        }
        if (coreGlow != null)
        {
            coreMpb.SetFloat(IntensityId, (0.2f + (lit ? 0.18f : 0f) + pulse * pulse * 0.9f + preGlow * 0.7f + hover * 0.3f) * (sleepShown ? 0.3f : 1f));
            coreGlow.SetPropertyBlock(coreMpb);
        }
        if (hubRend != null)
        {
            // v4: the island hub is a dim ink shell (the gauge's glass): only hover and the comet's count-in touch it; the fill carries the colour
            float he = IsMoon ? 0.12f + (lit ? 0.3f : 0f) + preGlow * 1.2f + hover * 0.5f + pulse * 0.4f : 0.03f + preGlow * 0.35f + hover * 0.35f;
            hubMpb.SetColor(EmissionId, chordColor * he);
            hubRend.SetPropertyBlock(hubMpb);
        }
        if (hubFill != null) UpdateGauge(dt);
        if (bead != null)
        {
            bool show = lit;
            if (bead.gameObject.activeSelf != show) bead.gameObject.SetActive(show);
            if (show)
            {
                bead.position = RingPoint(barT, 0.02f - ProjectConfig.PlatformThickness - 0.03f);
                float bi = 1.2f + pulse * 0.8f;
                beadMpb.SetFloat(IntensityId, bi);
                beadRend.SetPropertyBlock(beadMpb);
            }
        }
    }

    // ------------------------------------------------------------------ v4 components (SPEC v4 §3 K3, K5)
    /// <summary>Anchor or not (SongManager's layout): only a column's anchor wears the hub (the route anchor, the comet dock, the column's handle).</summary>
    public void SetAnchor(bool anchor)
    {
        isAnchor = anchor;
        if (!IsMoon && Hub != null && Hub.gameObject.activeSelf != anchor) Hub.gameObject.SetActive(anchor);
    }

    /// <summary>
    /// Register (octaves, -2..+2) without a rebuild: every tile is re-pitched reg x 12 (roles and colours keep: the chord is the same); cubes ride
    /// along. v6: the platform follows the octave tower (<see cref="UpdateTower"/>: the new register's pure lift, a glide from where it is); with
    /// <paramref name="animate"/> while stopped it plays the rise and the slow sink once (the preview of its turn); without, the lift is taken at once.
    /// </summary>
    public void SetRegister(int reg, bool animate)
    {
        reg = Mathf.Clamp(reg, -2, 2);
        int delta = reg - register;
        register = reg;
        if (delta != 0 && !IsMoon)
            foreach (var t in tiles) if (t != null) { t.midi += 12 * delta; t.myFrequency = ProjectConfig.refFreq * t.GetPitch(); }
        if (!animate) { towerInit = false; previewT = -1f; }
        else previewT = !IsMoon && reg != 0 && !GlobalClock.IsPlaying && GlobalClock.SongBeatD <= 1e-6 ? 0f : -1f;
        ApplyPose();
        RefreshCubeLines();
        preLightDirty = true;
    }

    /// <summary>A refused op (a full column, an impossible slot): the island shakes sideways for 0.32 s.</summary>
    public void Shake(float amount = 0.18f) { shakeAmp = Mathf.Max(0.02f, amount); shakeT = 0f; ApplyPose(); }

    /// <summary>The sleep look on / off: a desaturated, darker platform, dimmed tiles (a grey base map under the tiles' colours), a dim hub
    /// and track, and the closed-eye glyph. KeyBlock follows <see cref="sleep"/> by itself every frame.</summary>
    public void ApplySleepLook(bool on)
    {
        sleepShown = on;
        if (IsMoon) return;
        if (platMat != null)
        {
            float l = platBase.r * 0.3f + platBase.g * 0.59f + platBase.b * 0.11f;
            Color grey = new Color(l, l, l * 1.06f, 1f);
            platMat.SetColor("_BaseColor", on ? Color.Lerp(grey, platBase, 0.22f) * 0.72f : platBase);
        }
        if (hubMat != null) hubMat.SetColor("_BaseColor", on ? Color.Lerp(HubShellColor, new Color(0.3f, 0.29f, 0.34f), 0.7f) : HubShellColor);
        if (hubFillMat != null) { Color fc = FillColor; float fl = fc.r * 0.3f + fc.g * 0.59f + fc.b * 0.11f; hubFillMat.SetColor("_BaseColor", on ? Color.Lerp(new Color(fl, fl, fl * 1.05f), fc, 0.2f) * 0.8f : fc); }
        var tm = on ? DimTileMaterial : TileMaterial;
        foreach (var t in tiles) { if (t == null) continue; var r = t.GetComponent<Renderer>(); if (r != null && r.sharedMaterial != tm) r.sharedMaterial = tm; }
        if (glyph != null) glyph.enabled = !on;
        if (eye != null) eye.gameObject.SetActive(on);
        RefreshHome();
        RefreshJobBadge();
        preLightDirty = true;
    }

    static Material dimTileMat;
    /// <summary>The tiles' material while their island sleeps: the same toon material with a grey base map (the per-tile colours still apply, dimmed).</summary>
    public static Material DimTileMaterial
    {
        get
        {
            if (dimTileMat != null) return dimTileMat;
            dimTileMat = Fx.Lit(Color.white, 0.5f, 0f);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "deacube_dim", filterMode = FilterMode.Point };
            var c = new Color32(118, 116, 128, 255);
            tex.SetPixels32(new[] { c, c, c, c }); tex.Apply(false, true);
            dimTileMat.SetTexture("_BaseMap", tex);
            dimTileMat.mainTexture = tex;
            return dimTileMat;
        }
    }

    static Texture2D eyeTex;
    /// <summary>A closed eye (a lid curve with three lashes), drawn once: white with alpha, tinted by its material.</summary>
    static Texture2D ClosedEyeTexture
    {
        get
        {
            if (eyeTex != null) return eyeTex;
            const int n = 64;
            eyeTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "deacube_eye_closed" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    // the lid: a downward arc y = 0.12 - 0.42 (1 - u^2) for |u| < 0.78, stroke 0.09
                    float d = 9f;
                    if (Mathf.Abs(u) < 0.8f) d = Mathf.Abs(v - (0.14f - 0.42f * (1f - u * u)));
                    // three lashes hanging from the lid
                    for (int k = -1; k <= 1; k++)
                    {
                        float lu = k * 0.42f, lv = 0.14f - 0.42f * (1f - lu * lu);
                        Vector2 a = new Vector2(lu, lv), b = new Vector2(lu * 1.35f, lv - 0.3f), p = new Vector2(u, v);
                        Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, (a + ab * t - p).magnitude * 1.1f);
                    }
                    float alpha = Mathf.Clamp01((0.075f - d) * n * 0.6f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            eyeTex.SetPixels32(px); eyeTex.Apply(false, false);
            return eyeTex;
        }
    }

    // ------------------------------------------------------------------ v4 hub gauge (the user, 2026-09-29)
    /// <summary>The gauge's shell: the chord colour sunk into ink (reads as an empty glass).</summary>
    Color HubShellColor => Color.Lerp(Color.Lerp(chordColor, Look.InkColor, 0.6f), chordColor, 0.1f);
    Color FillColor => Color.Lerp(chordColor, Color.white, 0.1f);
    /// <summary>Seconds of the drain when the column restarts (the level only ever falls this way; it rises with the beat).</summary>
    public const float GaugeDrain = 0.12f;
    /// <summary>The level the hub gauge shows this frame (0..1).</summary>
    public float Gauge => gauge;

    /// <summary>The gauge level at the current song beat: 0 before the column's start (and when stopped), (beat − start) / length inside the
    /// column, 1 after its end (until the song wraps back). Paused = frozen (the beat is). v5: the length is the whole column (every pass).</summary>
    public float GaugeTarget()
    {
        if (IsMoon) return 0f;
        double beat = GlobalClock.SongBeatD;
        if (!GlobalClock.IsPlaying && beat <= 1e-6) return 0f;
        double s = startBeatOffset, len = LengthBeats * (double)colPasses;
        if (beat < s) return 0f;
        if (beat >= s + len) return 1f;
        if (rewind && Passes >= 2)
        {
            // v7 (SPEC v7 §6.4, W): a rewind island's gauge fills through each pass and DRAINS BACK while time unwinds at its end (every pass but the last)
            double pl = LengthBeats, local = beat - s;
            int p = (int)System.Math.Floor(local / pl + 1e-9);
            int passes = Mathf.Min(Passes, colPasses);
            if (p >= passes) return 1f;
            int rp; float ph = SongManager.I != null ? SongManager.I.RewindPhase(this, beat, out rp) : -1f;
            double rb = SongManager.RewindBeatsOf(pl);
            if (ph >= 0f) return (float)((1.0 - ph) * (pl - rb) / pl);
            return (float)((local - p * pl) / pl);
        }
        return (float)((beat - s) / len);
    }

    void UpdateGauge(float dt)
    {
        float target = GaugeTarget();
        gauge = target < gauge - 1e-4f ? Mathf.Max(target, gauge - dt / GaugeDrain) : target;
        bool on = gauge > 0.004f;
        if (hubFill.gameObject.activeSelf != on) hubFill.gameObject.SetActive(on);
        if (!on) return;
        float sc = Mathf.Sqrt(gauge);   // the lit area of the face = the level
        hubFill.localScale = new Vector3(sc, 1f, sc);
        fillMpb.SetColor(EmissionId, FillColor * ((0.3f + pulse * 0.45f + hover * 0.2f) * (sleepShown ? 0.25f : 1f)));
        hubFillRend.SetPropertyBlock(fillMpb);
    }

    // ------------------------------------------------------------------ v6 the keyboard island (SPEC v6 §3.1)
    /// <summary>v6: a keyboard's chordColor (its core glow, hub gauge, track pulses, ripples: warm lamp light) and its wood-ink case.</summary>
    public static readonly Color KeyboardGlow = new Color(0.98f, 0.78f, 0.50f);
    public static readonly Color KeyboardWood = new Color(0.34f, 0.22f, 0.21f);
    static readonly Color KeyCaseColor = new Color(0.25f, 0.155f, 0.165f);
    // key faces: in the song key / off it (a little dimmer, still playable)
    static readonly Color Ivory = new Color(1f, 0.95f, 0.86f), IvoryOff = new Color(0.80f, 0.77f, 0.76f);
    static readonly Color Ebony = new Color(0.27f, 0.22f, 0.32f), EbonyOff = new Color(0.15f, 0.12f, 0.19f);
    /// <summary>v6: the chord-tone dot on a keyboard's keys (a disc, tinted with the column chord's vibe colour).</summary>
    public const string KeyDotGlyph = "k6.keyDot";
    /// <summary>v8: the chord root's mark on a keyboard (a dot in a ring) and how much of the vibe colour washes its key face (ivory / ebony).</summary>
    public const string KeyRootGlyph = "k8.keyRoot";
    /// <summary>v8: how deep a held key sinks.</summary>
    public const float KeyPressDepth = 0.1f;
    public const float RootWash = 0.26f, RootWashBlack = 0.34f;
    /// <summary>v8: a tension's dot (a melody note beyond the chord tones) is this much of a chord tone's.</summary>
    public const float TensionDotScale = 0.68f;
    Transform keyRail; float keyRailLen, keyRailH;
    float[] keyCx, keyCz, keyW, keyL;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterKeyGlyphs()
    {
        IconFactory.Register(KeyDotGlyph, p => IconFactory.P.Circle(p, 0f, 0f, 0.66f));
        IconFactory.Register(KeyRootGlyph, p => Mathf.Min(IconFactory.P.Circle(p, 0f, 0f, 0.42f), IconFactory.P.Ring(p, 0f, 0f, 0.74f, 0.09f)));
    }

    /// <summary>v6: true when <paramref name="midi"/> is a black key (C#, D#, F#, G#, A#).</summary>
    public static bool IsBlackKeyMidi(int midi) { int pc = ((midi % 12) + 12) % 12; return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10; }
    /// <summary>v6: true when <paramref name="t"/> is a black key of a keyboard island (H: its hover / press look may differ).</summary>
    public static bool IsBlackKey(TileInteraction t) => t != null && t.island != null && t.island.IsKeyboard && IsBlackKeyMidi(t.island.chordRootMIDI + t.gridX);
    /// <summary>v6: the white keys of a keyboard whose lowest key is <paramref name="lowest"/> (15 from a white key, 14 from a black one).</summary>
    public static int WhiteKeysFrom(int lowest) { return WhiteKeysFrom(lowest, ProjectConfig.KeyboardKeys); }
    /// <summary>v7 §20.1: the white keys among <paramref name="count"/> keys from <paramref name="lowest"/>.</summary>
    public static int WhiteKeysFrom(int lowest, int count) { int n = 0; for (int k = 0; k < count; k++) if (!IsBlackKeyMidi(lowest + k)) n++; return n; }
    /// <summary>v7 §20.1: a key's gentle rise to the right per semitone for a keyboard of <paramref name="count"/> keys (KeyRise; a long keyboard
    /// rises less per key so its whole rise stays ≤ KeyRiseTotalMax).</summary>
    public static float KeyRiseOf(int count) => Mathf.Min(ProjectConfig.KeyRise, ProjectConfig.KeyRiseTotalMax / Mathf.Max(1, count - 1));

    /// <summary>
    /// v6: the island-local footprint of every key of a keyboard whose lowest key is <paramref name="lowest"/> (the root is where a chord island's
    /// tile 0,0 sits: the case spans −EdgeInset .. IslandWidth − EdgeInset in x and −EdgeInset .. DepthOf(1) − EdgeInset in z): centre x / z, width,
    /// length. White keys share the span between the cheeks (one key pitch each, KeyGap apart) and run from the front lip to the back rail; a black
    /// key sits on the boundary between its white neighbours (the classic offsets: C# and F# lean left, D# and A# right), BlackKeyWidth of a pitch
    /// wide and BlackKeyLength as long, set back against the rail. A black key at either end still fits inside the span.
    /// </summary>
    public static void KeyLayout(int lowest, float[] cx, float[] cz, float[] w, float[] len) { KeyLayout(lowest, cx, cz, w, len, IslandWidth); }

    /// <summary>v7: <see cref="KeyLayout(int, float[], float[], float[], float[])"/> over a case <paramref name="caseWidth"/> wide (a keyboard of several
    /// measures spreads its keys over them).</summary>
    public static void KeyLayout(int lowest, float[] cx, float[] cz, float[] w, float[] len, float caseWidth) { KeyLayout(lowest, cx, cz, w, len, caseWidth, ProjectConfig.KeyboardKeys); }

    /// <summary>v7 §20.1: <see cref="KeyLayout(int, float[], float[], float[], float[], float)"/> for <paramref name="count"/> keys (the arrays hold at least
    /// that many): the more keys, the narrower they are.</summary>
    public static void KeyLayout(int lowest, float[] cx, float[] cz, float[] w, float[] len, float caseWidth, int count)
    {
        // v8 (the user: "adding an octave should stretch the piano horizontally not condense the keys -> but second time its expanded it should be
        // like an organ where the keys go on a different layer"): the LOWER manual (up to OrganSplit keys) fills the case's width — the case itself
        // widens with the keys (KeyboardWidthOf), so a three-octave piano keeps a two-octave piano's key size; the keys past OrganSplit go on an
        // UPPER manual behind it, raised a tier (KeyTopYOf), the same key size, centred
        int n = Mathf.Max(1, count), nl = LowerManualKeys(n);
        float left = -EdgeInset + ProjectConfig.KeyPadX, right = -EdgeInset + caseWidth - ProjectConfig.KeyPadX;
        float zFront = -EdgeInset + ProjectConfig.KeyPadFront, zBack = -EdgeInset + KeyboardDepthOf(n) - ProjectConfig.KeyPadBack;
        float lowerBack = n > OrganSplit ? zFront + BaseKeyLen : zBack;
        float pitch = Manual(lowest, 0, nl, cx, cz, w, len, left, right, 0f, zFront, lowerBack);
        if (n > OrganSplit) Manual(lowest, nl, n, cx, cz, w, len, left, right, pitch, lowerBack + TierGap, zBack);
    }

    /// <summary>v8: lays keys [<paramref name="a"/>, <paramref name="b"/>) out as one manual between <paramref name="left"/> and <paramref name="right"/>
    /// (filling it when <paramref name="pitch"/> ≤ 0, else centred at that pitch) from <paramref name="zF"/> to <paramref name="zB"/>; returns its pitch.</summary>
    static float Manual(int lowest, int a, int b, float[] cx, float[] cz, float[] w, float[] len, float left, float right, float pitch, float zF, float zB)
    {
        int white = 0;
        float lo = 0f, hi = 0f, hw = ProjectConfig.BlackKeyWidth * 0.5f;
        for (int k = a; k < b; k++)
        {
            if (!IsBlackKeyMidi(lowest + k)) { white++; continue; }
            float c = white + BlackOffset(lowest + k);   // slot units: white key j spans [j, j + 1]
            lo = Mathf.Min(lo, c - hw); hi = Mathf.Max(hi, c + hw);
        }
        hi = Mathf.Max(hi, white);
        if (pitch <= 0f) pitch = (right - left) / Mathf.Max(1f, hi - lo);
        else left = (left + right) * 0.5f - (hi - lo) * pitch * 0.5f;
        float wl = zB - zF, bl = wl * ProjectConfig.BlackKeyLength;
        white = 0;
        for (int k = a; k < b; k++)
        {
            if (IsBlackKeyMidi(lowest + k))
            {
                cx[k] = left + (white + BlackOffset(lowest + k) - lo) * pitch;
                w[k] = ProjectConfig.BlackKeyWidth * pitch; len[k] = bl; cz[k] = zB - bl * 0.5f;
            }
            else
            {
                cx[k] = left + (white + 0.5f - lo) * pitch;
                w[k] = pitch - ProjectConfig.KeyGap; len[k] = wl; cz[k] = (zF + zB) * 0.5f;
                white++;
            }
        }
        return pitch;
    }

    // ---- v8: the growing piano and the organ's second manual
    /// <summary>v8: keys on the lower manual at most; a keyboard of more keys puts the rest on a raised upper manual behind it (an organ).</summary>
    public const int OrganSplit = 37;
    /// <summary>v8: the upper manual's raise above the lower manual's highest key, the gap between the two manuals, its keys' length (× a lower key's).</summary>
    public const float TierRaise = 0.3f, TierGap = 0.14f, UpperLenOf = 0.78f;
    /// <summary>v8: how far a <paramref name="count"/>-key organ's upper manual stands above the lower one's base (over its highest key, a tier up).</summary>
    public static float UpperBase(int count) { int nl = LowerManualKeys(count); return KeyRiseOf(nl) * (nl - 1) + TierRaise; }
    public static int LowerManualKeys(int count) => Mathf.Min(Mathf.Max(1, count), OrganSplit);
    public static bool HasUpperManual(int count) => count > OrganSplit;
    /// <summary>v8: true when key <paramref name="k"/> of a <paramref name="count"/>-key keyboard is on the upper manual.</summary>
    public static bool OnUpperManual(int k, int count) => count > OrganSplit && k >= OrganSplit;
    static float BaseKeyLen => DepthOf(1) - ProjectConfig.KeyPadFront - ProjectConfig.KeyPadBack;
    /// <summary>v8: a two-octave piano's slot pitch over one measure — the key size a growing piano keeps.</summary>
    static float BasePitch => (IslandWidth - 2f * ProjectConfig.KeyPadX) / WhiteKeysFrom(60, ProjectConfig.KeyboardKeys);
    /// <summary>v8 (a gallery song's 4-bar keyboards drew "giant ivory slabs"): the width the keys and their case take — a one-measure keyboard's
    /// (its keys keep their normal size however many measures it lasts: the keys don't encode time), centred in the island's <see cref="Width"/>
    /// (the platform, the beat track, the belt and the layout still span the whole column).</summary>
    public float KeysCaseWidth => IsKeyboard ? Mathf.Min(Width, KeyboardWidthOf(KeyCount, 1)) : Width;
    /// <summary>v8: island-local x of the key case's west edge (centred).</summary>
    public float KeysCaseX0 => -EdgeInset + (Width - KeysCaseWidth) * 0.5f;
    /// <summary>v9 (a gallery song's 4-bar keyboards read as "rows of heavy empty benches"): the platform's width — a keyboard's hugs its keys (the
    /// case's width, centred; the section floor shows the column), every other island's is its <see cref="Width"/>. The layout still reserves Width.</summary>
    public float PlatformWidth => IsKeyboard ? KeysCaseWidth : Width;
    /// <summary>v8: the keys laid out over the centred case (KeyLayout over KeysCaseWidth, shifted to KeysCaseX0).</summary>
    void LayoutMyKeys(int low, int n)
    {
        KeyLayout(low, keyCx, keyCz, keyW, keyL, KeysCaseWidth, n);
        float dx = KeysCaseX0 + EdgeInset;
        if (Mathf.Abs(dx) > 1e-5f) for (int k = 0; k < n; k++) keyCx[k] += dx;
    }

    /// <summary>v8: a keyboard's width: its measures, or wider when its lower manual holds more white keys than fit at a two-octave piano's key
    /// size ("stretch the piano horizontally not condense the keys").</summary>
    public static float KeyboardWidthOf(int keyCount, int bars)
    {
        int n = keyCount <= 0 ? ProjectConfig.KeyboardKeys : keyCount;
        return Mathf.Max(Mathf.Max(1, bars) * IslandWidth, WhiteKeysFrom(60, LowerManualKeys(n)) * BasePitch + 2f * ProjectConfig.KeyPadX);
    }
    /// <summary>v8: the top of key <paramref name="k"/> of a <paramref name="count"/>-key keyboard (its manual's gentle rise; the upper manual a tier up).</summary>
    public static float KeyTopYOf(int lowest, int k, int count)
    {
        bool up = OnUpperManual(k, count);
        int a = up ? OrganSplit : 0, m = up ? count - OrganSplit : LowerManualKeys(count);
        return KeyTopY(lowest + a, k - a, KeyRiseOf(m)) + (up ? UpperBase(count) : 0f);
    }
    /// <summary>v8: the height a white key reaches down to (the case floor; the upper manual's riser).</summary>
    public static float KeyFloorOf(int k, int count) => OnUpperManual(k, count) ? UpperBase(count) - 0.02f : 0f;

    static float BlackOffset(int midi)
    {
        switch (((midi % 12) + 12) % 12) { case 1: return -0.1f; case 3: return 0.1f; case 6: return -0.12f; case 10: return 0.12f; default: return 0f; }
    }

    /// <summary>v6: the top of key <paramref name="k"/> above the island root: a gentle rise per semitone (KeyRise); a black key stands
    /// BlackKeyRaise above its higher white neighbour.</summary>
    public static float KeyTopY(int lowest, int k) { return KeyTopY(lowest, k, ProjectConfig.KeyRise); }
    /// <summary>v7 §20.1: <see cref="KeyTopY(int, int)"/> with a rise of <paramref name="rise"/> per semitone (KeyRiseOf the keyboard's count).</summary>
    public static float KeyTopY(int lowest, int k, float rise)
    {
        float t = ProjectConfig.TileThickness;
        return IsBlackKeyMidi(lowest + k) ? rise * (k + 1) + t + ProjectConfig.BlackKeyRaise : rise * k + t;
    }

    /// <summary>
    /// v7 (SPEC v7 §13.2 — "a 2 measure melody shouldnt be two separate grids, they should be together"): a chord island of several measures is that
    /// many measures wide: its tile grid in the first measure, every later measure a quieter HELD-CHORD surface — one soft bar per chord-tone row
    /// across the measure (the row's pitch height, halved; the tiles' tint, dimmer: the chord rings on) — and a thin ink measure divider on the platform
    /// at every measure boundary. Visual only (the platform's collider is the island's handle).
    /// </summary>
    void BuildHeldMeasures(Color tileTint, int[,] midi, int lowest, int highest)
    {
        var root = new GameObject("HeldMeasures").transform;
        root.SetParent(transform, false);
        heldMat = Fx.Lit(Color.Lerp(tileTint, PlatformColorOf(chordColor), 0.42f), 0.45f, 0f);
        dividerMat = Fx.Lit(Color.Lerp(Look.InkColor, PlatformColorOf(chordColor), 0.2f), 0.2f, 0f);
        float e = EdgeInset, w1 = IslandWidth, pad = ProjectConfig.PlatformPad, t = ProjectConfig.TileThickness * 0.5f;
        float barLen = w1 - 2f * pad + ProjectConfig.TileSize * 0.3f, depth = ProjectConfig.TileSize * 0.56f;
        var barMesh = MeshFactory.RoundedBox(new Vector3(barLen, t, depth), 0.1f, 3);
        var divMesh = MeshFactory.RoundedBox(new Vector3(0.1f, 0.05f, Mathf.Max(0.2f, Depth - 0.34f)), 0.025f, 1);
        for (int m = 1; m < bars; m++)
        {
            float x0 = -e + m * w1;
            var dv = new GameObject("MeasureDivider_" + m);
            dv.transform.SetParent(root, false);
            dv.transform.localPosition = new Vector3(x0, -0.005f, -e + Depth * 0.5f);
            dv.AddComponent<MeshFilter>().sharedMesh = divMesh;
            var dr = dv.AddComponent<MeshRenderer>(); dr.sharedMaterial = dividerMat; dr.shadowCastingMode = ShadowCastingMode.Off;
            for (int z = 0; z < rows; z++)
            {
                float y = (PitchLook.Rise * (midi[0, z] - lowest)) * 0.5f + t * 0.5f;
                var b = new GameObject("Held_" + m + "_" + z);
                b.transform.SetParent(root, false);
                b.transform.localPosition = new Vector3(x0 + w1 * 0.5f, y, z * ProjectConfig.Spacing);
                b.AddComponent<MeshFilter>().sharedMesh = barMesh;
                var br = b.AddComponent<MeshRenderer>(); br.sharedMaterial = heldMat; br.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
        heldRoot = root;
    }
    Material heldMat, dividerMat; Transform heldRoot;
    /// <summary>v7 (tests): the held-chord surface of a multi-measure chord island (null otherwise).</summary>
    public Transform HeldMeasures => heldRoot;

    /// <summary>
    /// v7 §19.1 (the user: "when making notes at first, it can auto-expand if needed or auto-unexpand"; K): the island becomes
    /// <paramref name="newBars"/> measures long WITHOUT a rebuild — its tiles (and a draft path on them) and its cubes stay alive; the platform and its
    /// collider, the held-chord surface, the beat track, the core glow, the hub (bars + 2 sides, the gauge's notches), the sleep eye, the member rim,
    /// the belt and a keyboard's keys and case follow the new width (B's structure marks through <c>KindResize</c>). SongManager.ResizeColumnLive
    /// calls it for every island of the column, then re-times and re-lays the song. A phrase only mirrors its column's bars (its length is its own).
    /// </summary>
    public void ResizeMeasures(int newBars)
    {
        newBars = Mathf.Clamp(newBars, 1, 4);
        if (IsMoon || newBars == bars) return;
        bars = newBars;
        if (IsPhrase) return;
        ReshapePlatform();
        if (heldRoot != null) { Destroy(heldRoot.gameObject); heldRoot = null; }
        Kill(heldMat); Kill(dividerMat); heldMat = dividerMat = null;
        if (!IsKeyboard && !IsStairs && bars >= 2 && grid != null && cols > 0)
        {
            var midi = new int[cols, rows]; int lo = int.MaxValue, hi = int.MinValue;
            for (int x = 0; x < cols; x++) for (int z = 0; z < rows; z++) { var t = grid[x, z]; int m = t != null ? t.midi : chordRootMIDI; midi[x, z] = m; lo = Mathf.Min(lo, m); hi = Mathf.Max(hi, m); }
            BuildHeldMeasures(TileTintOf(chordColor, false), midi, lo, hi);
        }
        if (IsKeyboard) RelayoutKeys();
        if (Hub != null)
        {
            var hm = Hub.GetComponent<MeshFilter>(); if (hm != null) hm.sharedMesh = MeshFactory.Prism(HubSides, ProjectConfig.HubRadius, HubH);
            if (hubFill != null) { var fm = hubFill.GetComponent<MeshFilter>(); if (fm != null) fm.sharedMesh = MeshFactory.Prism(HubSides, ProjectConfig.HubRadius, HubH + 0.025f); }
            if (notchMat != null) { Kill(notchMat); notchMat = null; }
            BuildNotches();
        }
        KindResize();
        preLightDirty = true;
        ApplyPose();
        RefreshCubeLines();
    }
    /// <summary>v7 (B): re-lays B's structure marks (the beat pips per measure) after a live resize (ResizeMeasures).</summary>
    partial void KindResize();

    /// <summary>v7: the platform re-shaped in place for the current Width / Depth (a live resize, a keyboard grown past 37 keys): the platform mesh and
    /// collider, the beat track (its segments follow the bars, its outline the size), the core glow, the hub's place, the eye, the member rim, the belt.</summary>
    void ReshapePlatform()
    {
        float w = PlatformWidth, d = Depth, thick = ProjectConfig.PlatformThickness;
        Vector3 off = CenterOffset;
        if (platform != null)
        {
            var pt = platform.transform;
            pt.localPosition = new Vector3(off.x, pt.localPosition.y, off.z);   // (y: B lowers a falling stair's base)
            pt.GetComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(w, thick, d), 0.24f, 4);
            var pc = pt.GetComponent<BoxCollider>(); if (pc != null) pc.size = new Vector3(w, thick, d);
            if (InLongGrid || lgShaped) UpdateLongGridLook(true);   // v7 §21.2: a long grid's member keeps its joined shape
        }
        ringR = Mathf.Sqrt(TrackHX * TrackHX + TrackHZ * TrackHZ);
        if (ringFilter != null) { ringFilter.transform.localPosition = new Vector3(off.x, ringFilter.transform.localPosition.y, off.z + TrackDz); segments = 0; EnsureRing(); }
        if (coreGlow != null) { var ct = coreGlow.transform; ct.localPosition = new Vector3(off.x, ct.localPosition.y, off.z + TrackDz); ct.localScale = new Vector3(w * 1.75f, 1f, PlatformDepth * 1.75f); }
        if (Hub != null) Hub.localPosition = new Vector3(off.x, 0.15f - HubH * 0.5f, off.z - HubOffsetZ);
        if (eye != null && !IsKeyboard) eye.localPosition = new Vector3(-EdgeInset + w - ProjectConfig.PlatformPad * 0.5f - 0.05f, eye.localPosition.y, eye.localPosition.z);
        if (rim != null) { Destroy(rim.gameObject); rim = null; Kill(rimMat); rimMat = null; BuildRim(); }
        if (belt != null) belt.SetSlots(HasBelt ? Passes : 1, false, HasBelt ? Passes : 1);
    }

    // ------------------------------------------------------------------ v7 §21.2 THE LONG GRID: the joined look (package K)
    // SongManager tells every island its neighbours in its long grid (SetLongGrid: a carry run, source first). While a neighbour's platform meets
    // this one exactly (same height, same front edge, its edge on this edge: at rest, at a belt's held slot), the two platforms JOIN: the shared ends
    // are open and square (no end face, so no rounding, no notch and no ink outline between them: the outline is an inverted hull of the faces that
    // exist) and a thin ink divider marks the chord change on the joint (the held measures' divider). Every member stands as deep as the long grid's
    // deepest (front edges aligned by the layout), so the long grid reads as ONE platform going on to the right. A neighbour moving away (a drag, a
    // belt's ride, a tower, a glide) rounds the ends again at once. Each member keeps its tiles, colours, hub, pips and header.
    KeyBlock lgWest, lgEast;
    float lgDepth;
    bool lgJoinW, lgJoinE, lgShaped;
    Renderer lgShapedFor;
    Transform lgDivider; Material lgDivMat;
    static readonly Dictionary<string, Mesh> slabCache = new Dictionary<string, Mesh>();

    /// <summary>v7 §21.2: true while this island is part of a long grid (SongManager.LongGridOf).</summary>
    public bool InLongGrid => lgWest != null || lgEast != null;
    /// <summary>v7 §21.2: the grid before / after this one in its long grid (null at its ends or outside one).</summary>
    public KeyBlock LongGridWest => lgWest;
    public KeyBlock LongGridEast => lgEast;
    /// <summary>v7 §21.2: the platform is joined to its west / east neighbour right now (open square end, the divider on the west joint).</summary>
    public bool JoinedWest => lgJoinW;
    public bool JoinedEast => lgJoinE;
    /// <summary>v7 §21.2: the depth the platform is shown with — its own Depth, or in a long grid the long grid's deepest member's (the front edge
    /// stays: it grows toward the back). <see cref="PlatformBackEdge"/> = FrontEdge + this.</summary>
    public float PlatformDepth => InLongGrid ? Mathf.Max(Depth, lgDepth) : Depth;
    public float PlatformBackEdge => FrontEdge + PlatformDepth;
    /// <summary>v7 §21.2 (tests): the joint's ink divider on the west end while joined there, else null.</summary>
    public Transform LongGridDivider => lgDivider != null && lgDivider.gameObject.activeSelf ? lgDivider : null;

    /// <summary>v7 §21.2 (SongManager): this island's neighbours in its long grid and the long grid's platform depth (nulls: in none).</summary>
    internal void SetLongGrid(KeyBlock west, KeyBlock east, float depth)
    {
        if (IsMoon || IsStairs || IsPhrase) { west = east = null; }
        bool changed = west != lgWest || east != lgEast || Mathf.Abs(depth - lgDepth) > 1e-4f;
        lgWest = west; lgEast = east; lgDepth = west != null || east != null ? depth : 0f;
        if (changed) UpdateLongGridLook(true);
    }

    void LateUpdate()
    {
        if (lgWest == null && lgEast == null && !lgShaped) return;
        UpdateLongGridLook(false);
    }

    /// <summary>v7 §21.2: re-joins / re-rounds the platform's ends by where the neighbours stand this frame (after every island moved).</summary>
    void UpdateLongGridLook(bool force)
    {
        if (platform == null) return;
        bool w = lgWest != null && JoinFlush(lgWest, this);
        bool e = lgEast != null && JoinFlush(this, lgEast);
        if (!force && w == lgJoinW && e == lgJoinE && lgShapedFor == platform) return;
        lgJoinW = w; lgJoinE = e;
        ShapePlatform();
        PlaceLongGridDivider();
        ApplyLongGridCheeks();
        FitTrackToPlatform();
        RefreshRingMask(true);
    }

    /// <summary>v7 §21.2: <paramref name="a"/>'s east end meets <paramref name="b"/>'s west end right now (within 0.04 u in x and z, 0.03 u in
    /// height, neither tilted by a drag).</summary>
    static bool JoinFlush(KeyBlock a, KeyBlock b)
    {
        if (a == null || b == null || a.platform == null || b.platform == null) return false;
        Transform ta = a.transform, tb = b.transform;
        if (Quaternion.Angle(ta.rotation, Quaternion.identity) > 0.05f || Quaternion.Angle(tb.rotation, Quaternion.identity) > 0.05f) return false;
        Vector3 pa = ta.position, pb = tb.position;
        float gap = (pb.x - EdgeInset) - (pa.x - EdgeInset + a.Width);
        return Mathf.Abs(gap) < 0.04f && Mathf.Abs(pa.y - pb.y) < 0.03f && Mathf.Abs(pa.z - pb.z) < 0.04f;
    }

    /// <summary>v7 §21.2: the platform's mesh and collider for its shape now: the plain rounded box, or a long grid member's (its long grid's depth,
    /// open square ends where it is joined).</summary>
    void ShapePlatform()
    {
        lgShapedFor = platform;
        if (platform == null || IsMoon) return;
        float w = PlatformWidth, d = PlatformDepth, thick = ProjectConfig.PlatformThickness, dz = (d - Depth) * 0.5f;
        bool plain = (!lgJoinW && !lgJoinE && dz < 1e-4f) || w < Width - 1e-3f;   // v9: a hugging (narrower) platform is never joined
        var mf = platform.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = plain ? MeshFactory.RoundedBox(new Vector3(w, thick, d), 0.24f, 4) : JoinedSlab(new Vector3(w, thick, d), dz, 0.24f, 4, !lgJoinW, !lgJoinE);
        var pc = platform.GetComponent<BoxCollider>();
        if (pc != null) { pc.size = new Vector3(w, thick, d); pc.center = new Vector3(0f, 0f, dz); }
        lgShaped = !plain;
    }

    /// <summary>v7 §21.2: the joint's thin ink divider (the held measures' measure divider: 0.1 u wide, across the platform) on the west end while
    /// joined there, in the ink between the two chords' colours.</summary>
    void PlaceLongGridDivider()
    {
        if (!lgJoinW || lgWest == null)
        {
            if (lgDivider != null) lgDivider.gameObject.SetActive(false);
            return;
        }
        float d = PlatformDepth;
        if (lgDivider == null)
        {
            var go = new GameObject("LongGridDivider");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            lgDivider = go.transform;
        }
        var ink = Color.Lerp(Look.InkColor, Color.Lerp(PlatformColor, lgWest.PlatformColor, 0.5f), 0.2f);
        if (lgDivMat == null) lgDivMat = Fx.Lit(ink, 0.2f, 0f); else lgDivMat.SetColor("_BaseColor", ink);
        lgDivider.GetComponent<MeshRenderer>().sharedMaterial = lgDivMat;
        lgDivider.GetComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(0.1f, 0.05f, Mathf.Max(0.2f, d - 0.34f)), 0.025f, 1);
        lgDivider.localPosition = new Vector3(-EdgeInset, -0.005f, -EdgeInset + d * 0.5f);
        lgDivider.gameObject.SetActive(true);
    }

    /// <summary>v7 §21.2: a joined keyboard hides the cheek on its joined end (the keys run on into the next keyboard).</summary>
    void ApplyLongGridCheeks()
    {
        if (!IsKeyboard) return;
        bool full = KeysCaseWidth >= Width - 1e-3f;   // v8: a centred case (a long keyboard) keeps its cheeks: they are not at the joint
        var l = transform.Find("CheekL"); if (l != null) l.gameObject.SetActive(!(lgJoinW && full));
        var r = transform.Find("CheekR"); if (r != null) r.gameObject.SetActive(!(lgJoinE && full));
    }

    /// <summary>
    /// v7 §21.2: MeshFactory.RoundedBox's rounded box (<paramref name="size"/>, corner <paramref name="radius"/>, <paramref name="seg"/> quads per face
    /// side), shifted <paramref name="dz"/> toward +z, whose west (−x) / east (+x) end is either rounded as usual or OPEN: no end face, the top /
    /// bottom / front / back faces running square to the end — two open ends meeting read as one platform. Cached.
    /// </summary>
    static Mesh JoinedSlab(Vector3 size, float dz, float radius, int seg, bool roundW, bool roundE)
    {
        string key = $"lg_{size.x:F3}_{size.y:F3}_{size.z:F3}_{dz:F3}_{radius:F3}_{seg}_{(roundW ? 1 : 0)}{(roundE ? 1 : 0)}";
        Mesh cached;
        if (slabCache.TryGetValue(key, out cached) && cached != null) return cached;
        Vector3 h = size * 0.5f;
        float r = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.98f);
        Vector3 lo = new Vector3(roundW ? -h.x + r : -h.x, -h.y + r, -h.z + r), hi = new Vector3(roundE ? h.x - r : h.x, h.y - r, h.z - r);
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        var shift = new Vector3(0f, 0f, dz);
        foreach (var n in dirs)
        {
            if ((n.x < -0.5f && !roundW) || (n.x > 0.5f && !roundE)) continue;   // an open end: no end face
            Vector3 t1 = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 t2 = Vector3.Cross(n, t1).normalized;
            t1 = Vector3.Cross(t2, n).normalized;
            float hn = Mathf.Abs(Vector3.Dot(h, n)), h1 = Mathf.Abs(Vector3.Dot(h, t1)), h2 = Mathf.Abs(Vector3.Dot(h, t2));
            int b0 = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg * 2f - 1f;
                for (int j = 0; j <= seg; j++)
                {
                    float v = j / (float)seg * 2f - 1f;
                    Vector3 p = n * hn + t1 * (u * h1) + t2 * (v * h2);
                    Vector3 q = new Vector3(Mathf.Clamp(p.x, lo.x, hi.x), Mathf.Clamp(p.y, lo.y, hi.y), Mathf.Clamp(p.z, lo.z, hi.z));
                    Vector3 dd = p - q;
                    Vector3 nn = dd.sqrMagnitude < 1e-8f ? n : dd.normalized;
                    verts.Add(q + nn * r + shift);
                    norms.Add(nn);
                    uvs.Add(new Vector2((u + 1f) * 0.5f, (v + 1f) * 0.5f));
                }
            }
            Vector3 fn = Vector3.Cross(verts[b0 + 1] - verts[b0], verts[b0 + seg + 1] - verts[b0]);
            bool flip = Vector3.Dot(fn, n) < 0f;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = b0 + i * (seg + 1) + j, b = a + 1, c = a + seg + 1, d = c + 1;
                    if (!flip) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                    else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                }
        }
        var m = new Mesh { name = key };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        slabCache[key] = m;
        return m;
    }

    /// <summary>v7: a keyboard's keys re-laid over its (new) width — the same key objects (tiles) move and re-size, so a path on them stays; the
    /// case (cheeks, rail) is rebuilt; the sleep eye moves to the new rail.</summary>
    void RelayoutKeys()
    {
        int n = cols;
        if (grid == null || n <= 0 || keyCx == null || keyCx.Length != n) return;
        LayoutMyKeys(chordRootMIDI, n);
        for (int k = 0; k < n; k++) PoseKey(grid[k, 0], k);
        RebuildCase();
    }

    /// <summary>v7 §20.1: key object <paramref name="tile"/> posed as key <paramref name="k"/> of the current range (its mesh, collider, rest position,
    /// grid index, name, the marks' size) from the laid-out keyCx / keyCz / keyW / keyL — the same object, so a path on it keeps its note.</summary>
    void PoseKey(TileInteraction tile, int k)
    {
        if (tile == null) return;
        int low = chordRootMIDI;
        float t = ProjectConfig.TileThickness;
        bool black = IsBlackKeyMidi(low + k);
        float top = KeyTopYOf(low, k, cols);
        Vector3 size, centre; KeyBody(k, black, top, out size, out centre);
        var mf = tile.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = MeshFactory.RoundedBoxAt(size, centre, black ? 0.06f : 0.07f, 3);
        var bc = tile.GetComponent<BoxCollider>(); if (bc != null) { bc.size = size; bc.center = centre; }
        tile.gameObject.name = "Key_" + measureIndex + "_" + k;
        var house = tile.transform.Find("KeyHouse"); if (house != null) { float s = Mathf.Min(0.28f, keyW[k] * 0.74f); house.localScale = new Vector3(s, 1f, s); }
        var dot = tile.transform.Find("KeyDot"); if (dot != null) { float s = Mathf.Min(0.2f, keyW[k] * 0.56f); dot.localScale = new Vector3(s, 1f, s); }
        tile.gridX = k;   // v7 §20.1: re-indexed in place (an octave added below = every key +12): a path / a draft on it keeps its note
        tile.RestAt(new Vector3(keyCx[k], top - t * 0.5f, keyCz[k]));   // D's rest pose: Top, hover and press follow
    }

    // ------------------------------------------------------------------ v7 §20.1 the extensible keyboard (the user: "for the piano keyboard, it
    // should also be extensible if needed (like if a melody goes beyond an octave)")
    float keySlideT = 9f, keySlideA = 1f, keySlideB = 0f;
    /// <summary>v7 §20.1: true while the keys slide into a new range (the keys' bed eases from the old layout to the new one).</summary>
    public bool KeysSliding => keySlideT < 1f;
    /// <summary>v7 §20.1: the island-local centre of key <paramref name="k"/>'s top at rest (the cat's paws, tests); Vector3.zero off the range.</summary>
    public Vector3 KeyTopLocal(int k)
    {
        if (!IsKeyboard || keyCx == null || k < 0 || k >= keyCx.Length) return Vector3.zero;
        return new Vector3(keyCx[k], KeyTopYOf(chordRootMIDI, k, cols), keyCz[k]);
    }
    /// <summary>v7 §20.1: the width of key <paramref name="k"/> (0 off the range).</summary>
    public float KeyWidthOf(int k) => IsKeyboard && keyW != null && k >= 0 && k < keyW.Length ? keyW[k] : 0f;

    /// <summary>v7 §20.1 (K): an octave more LIVE — <paramref name="dir"/> +1 above, −1 below (see <see cref="ChangeKeyRange"/>). False at
    /// KeyboardMaxKeys or past the piano.</summary>
    public bool ExtendKeys(int dir) => ChangeKeyRange(dir < 0 ? -12 : 0, KeyCount + 12);
    /// <summary>v7 §20.1 (K): an octave less LIVE — <paramref name="dir"/> +1 the top octave, −1 the bottom one. False at KeyboardMinKeys or when a
    /// cube (a draft too) plays a key of that octave.</summary>
    public bool ShrinkKeys(int dir) => ChangeKeyRange(dir < 0 ? 12 : 0, KeyCount - 12);

    /// <summary>v7 §20.1: true when a cube's path (a draft's too: its nodes) uses a key of this keyboard outside new index range [lo, hi].</summary>
    bool KeysInUseOutside(int lo, int hi)
    {
        var cubes = SequenceMaster.Cubes;
        for (int i = 0; i < cubes.Count; i++)
        {
            var c = cubes[i]; if (c == null) continue;
            foreach (var n in c.nodes) if (n != null && n.island == this && (n.gridX < lo || n.gridX > hi)) return true;
        }
        return false;
    }

    /// <summary>
    /// v7 §20.1 (K): the keyboard's range LIVE — no rebuild, no History (like ResizeColumnLive): its lowest key moves by <paramref name="lowDelta"/>
    /// semitones and it holds <paramref name="newCount"/> keys (KeyboardMinKeys..KeyboardMaxKeys, inside the piano 21..108). The key objects that
    /// stay keep their pitch and are RE-INDEXED (lowDelta −12 = every key +12), so the cubes' paths and a live draft on them keep their notes (their
    /// saved xs follow gridX); dropped keys must hold no note (else false); new keys are built; everything re-lays over the same width (narrower
    /// keys as it grows) and slides there (the new octave slides in from its edge); the case and the chord-tone dots follow.
    /// </summary>
    public bool ChangeKeyRange(int lowDelta, int newCount)
    {
        if (!IsKeyboard || grid == null || keyCx == null || tilesContainer == null) return false;
        if (newCount < ProjectConfig.KeyboardMinKeys || newCount > ProjectConfig.KeyboardMaxKeys) return false;
        int oldLow = chordRootMIDI, oldN = cols, newLow = oldLow + lowDelta;
        if (newLow < 21 || newLow + newCount - 1 > 108) return false;
        if (lowDelta == 0 && newCount == oldN) return false;
        // old key k sounds oldLow + k → new index k − lowDelta; the keys that fall outside must be free
        if (KeysInUseOutside(lowDelta, lowDelta + newCount - 1)) return false;
        var oldGrid = grid; var oldCx = (float[])keyCx.Clone();
        var newGrid = new TileInteraction[newCount, 1];
        int firstKept = -1, lastKept = -1;
        for (int k = 0; k < oldN; k++)
        {
            var tile = oldGrid[k, 0];
            int nk = k - lowDelta;
            if (nk >= 0 && nk < newCount) { newGrid[nk, 0] = tile; if (firstKept < 0) firstKept = k; lastKept = k; }
            else if (tile != null) { tiles.Remove(tile); Destroy(tile.gameObject); }
        }
        float oldDepth = Depth, oldWidth = Width;
        chordRootMIDI = newLow; keyCount = newCount; cols = newCount; grid = newGrid;
        if (Mathf.Abs(Depth - oldDepth) > 1e-4f || Mathf.Abs(Width - oldWidth) > 1e-4f) ReshapePlatform();   // v8: a third octave widens it, a fourth adds the organ's manual
        keyCx = new float[newCount]; keyCz = new float[newCount]; keyW = new float[newCount]; keyL = new float[newCount];
        LayoutMyKeys(newLow, newCount);
        for (int k = 0; k < newCount; k++)
        {
            if (grid[k, 0] == null) grid[k, 0] = MakeKey(tilesContainer, k, newLow + k + 12 * register);
            else PoseKey(grid[k, 0], k);
        }
        tiles.Clear(); keyDots.Clear();
        for (int k = 0; k < newCount; k++) { var t = grid[k, 0]; tiles.Add(t); keyDots.Add(t != null ? t.transform.Find("KeyDot") : null); }
        RebuildCase();
        keyDotRoot = -1; RefreshKeyDots();
        // the slide: the keys' bed starts where the kept keys were shown (an affine x map new → old) and eases to the new layout
        if (firstKept >= 0 && lastKept > firstKept)
        {
            float n1 = keyCx[firstKept - lowDelta], n2 = keyCx[lastKept - lowDelta], o1 = oldCx[firstKept], o2 = oldCx[lastKept];
            keySlideA = Mathf.Abs(n2 - n1) > 1e-4f ? (o2 - o1) / (n2 - n1) : 1f; keySlideB = o1 - keySlideA * n1;
            keySlideT = 0f; ApplyKeySlide(0f);
        }
        preLightDirty = true;
        RefreshCubeLines();
        return true;
    }

    void ApplyKeySlide(float e)
    {
        if (tilesContainer == null) return;
        float a = Mathf.Lerp(keySlideA, 1f, e), b = Mathf.Lerp(keySlideB, 0f, e);
        tilesContainer.localScale = new Vector3(a, 1f, 1f);
        tilesContainer.localPosition = new Vector3(b, 0f, 0f);
    }

    /// <summary>v7: the keyboard's case (cheeks, rail) rebuilt for the current width / keys; the sleep eye moves to the new rail.</summary>
    void RebuildCase()
    {
        Transform oldRail = keyRail;
        if (eye != null && oldRail != null && eye.parent == oldRail) eye.SetParent(transform, true);
        foreach (var nm in new[] { "CheekL", "CheekR", "KeyRail", "OrganStep" }) { var tr = transform.Find(nm); if (tr != null) Destroy(tr.gameObject); }
        Kill(caseMat); caseMat = null;
        BuildCase();
        ApplyLongGridCheeks();   // v7 §21: a joined keyboard shows no cheek where it runs on into its neighbour
        if (eye != null && keyRail != null)
        {
            eye.SetParent(keyRail, false);
            eye.localPosition = new Vector3(keyRailLen * 0.5f - 0.55f, keyRailH * 0.5f + 0.014f, 0f);
            eye.localRotation = Quaternion.identity;
        }
    }

    /// <summary>v6: the keys of a keyboard island (tiles: gridX = key index, gridZ 0, tile.midi = key + 12 x register), the key marks (the tonic's
    /// tiny house, the chord-tone dot: children of the key, so they sink with a press) and the case.</summary>
    void BuildKeys(Transform tilesRoot, int[,] midi)
    {
        int n = cols, low = chordRootMIDI;
        keyCx = new float[n]; keyCz = new float[n]; keyW = new float[n]; keyL = new float[n];
        LayoutMyKeys(low, n);   // v8: a one-measure keyboard's keys, centred in the column (v7 spread them over every measure)
        keyHouseInk = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(Look.InkColor, 0.86f));
        keyHouseCream = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(new Color(1f, 0.95f, 0.84f), 0.95f));
        keyDotMat = Fx.Alpha(IconFactory.GetTexture(KeyDotGlyph), Color.white);
        keyDots.Clear();
        for (int k = 0; k < n; k++)
        {
            var tile = MakeKey(tilesRoot, k, midi[k, 0]);
            tiles.Add(tile);
            grid[k, 0] = tile;
            keyDots.Add(tile.transform.Find("KeyDot"));
        }
        BuildCase();
    }

    /// <summary>One key of the current range (index <paramref name="k"/> from chordRootMIDI, sounding <paramref name="midi"/>): its body (a white key
    /// reaches down to the case: no gap under the high keys), collider ("Tile"), TileInteraction (gridX = k), face colour (in the song key or a little
    /// dimmer), the tonic's tiny house and the chord-tone dot (children: they sink with a press). keyCx / keyCz / keyW / keyL are laid out.</summary>
    TileInteraction MakeKey(Transform tilesRoot, int k, int midi)
    {
        int low = chordRootMIDI;
        var key = MusicTheory.KeyOfSong();
        float t = ProjectConfig.TileThickness;
        bool black = IsBlackKeyMidi(low + k);
        float top = KeyTopYOf(low, k, cols);
        var go = new GameObject("Key_" + measureIndex + "_" + k);
        go.transform.SetParent(tilesRoot, false);
        go.transform.localPosition = new Vector3(keyCx[k], top - t * 0.5f, keyCz[k]);   // TileInteraction.Top = the key's top face
        Vector3 size, centre; KeyBody(k, black, top, out size, out centre);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBoxAt(size, centre, black ? 0.06f : 0.07f, 3);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sleepShown ? DimTileMaterial : TileMaterial;
        mr.shadowCastingMode = black ? ShadowCastingMode.On : ShadowCastingMode.Off;   // the raised black keys shade the white ones
        var col = go.AddComponent<BoxCollider>();
        col.size = size; col.center = centre;
        go.tag = "Tile";
        var tile = go.AddComponent<TileInteraction>();
        int pc = ((low + k) % 12 + 12) % 12;
        bool inScale = Harmony.InScale(key.tonic, key.minor, pc);
        tile.Setup(this, k, 0, midi, black ? (inScale ? Ebony : EbonyOff) : (inScale ? Ivory : IvoryOff));
        tile.pressDepth = KeyPressDepth;   // v8: a thin key travels less (it never sinks into its bed)
        float fz = -keyL[k] * 0.5f;   // the key's front end (key-local)
        if (pc == key.tonic) Mark(go.transform, "KeyHouse", new Vector3(0f, t * 0.5f + 0.013f, fz + (black ? 0.44f : 0.52f)), Mathf.Min(0.28f, keyW[k] * 0.74f), black ? keyHouseCream : keyHouseInk);
        var dot = Mark(go.transform, "KeyDot", new Vector3(0f, t * 0.5f + 0.012f, fz + (black ? 0.17f : 0.23f)), Mathf.Min(0.2f, keyW[k] * 0.56f), keyDotMat);
        dot.gameObject.SetActive(false);
        return tile;
    }

    /// <summary>v8 (the user, after thinner keys: "i want the piano keys to be taller (like almost how it was before)"): key <paramref name="k"/>'s body
    /// in key-local terms (the key's transform sits at its top − half a TileThickness, so its top face is at +TileThickness / 2): a white key reaches
    /// down to its manual's floor (the case; the upper manual's riser), a black key is TileThickness deep below its raised top.</summary>
    void KeyBody(int k, bool black, float top, out Vector3 size, out Vector3 centre)
    {
        float t = ProjectConfig.TileThickness, h = black ? t : Mathf.Max(t, top - KeyFloorOf(k, cols)), topL = t * 0.5f;
        size = new Vector3(keyW[k], h, keyL[k]);
        centre = new Vector3(0f, topL - h * 0.5f, 0f);
    }

    Transform Mark(Transform parent, string name, Vector3 local, float size, Material m)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = local;
        g.transform.localScale = new Vector3(size, 1f, size);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return g.transform;
    }

    /// <summary>v6: the keyboard's case on its wood-ink platform: a cheek at each end of the keys and a back rail (a piano's key slip) that rises
    /// with them. Visual only (the platform's collider stays the island's handle).</summary>
    void BuildCase()
    {
        caseMat = Fx.Lit(KeyCaseColor, 0.3f, 0f);
        float e = EdgeInset, w = Width, d = Depth, t = ProjectConfig.TileThickness;
        int nl = LowerManualKeys(cols);
        bool organ = HasUpperManual(cols);
        float rise = KeyRiseOf(nl) * (nl - 1);   // the lower manual's rise (v8: an organ's upper manual rises over its own keys, a tier up)
        float topMax = 0f; for (int k = 0; k < cols; k++) topMax = Mathf.Max(topMax, KeyTopYOf(chordRootMIDI, k, cols));
        float cx0 = KeysCaseX0, cw = KeysCaseWidth;   // v8: the case hugs the keys, centred in the column
        float xl = cx0 + ProjectConfig.KeyPadX, xr = cx0 + cw - ProjectConfig.KeyPadX;
        float zFront = -e + ProjectConfig.KeyPadFront, zBack = -e + d - ProjectConfig.KeyPadBack, floor = -0.03f;
        float lowerBack = organ ? zFront + BaseKeyLen : zBack;
        // the cheeks: from the front lip to the rail, a little above the keys beside them (an organ's: above its upper manual too)
        for (int side = 0; side < 2; side++)
        {
            float x0 = side == 0 ? cx0 + 0.07f : xr + 0.05f, x1 = side == 0 ? xl - 0.05f : cx0 + cw - 0.07f;
            float top = organ ? topMax + 0.09f : t + 0.09f + (side == 0 ? 0f : rise);
            var c = Box(side == 0 ? "CheekL" : "CheekR", new Vector3(x1 - x0, top - floor, zBack + 0.02f - zFront + 0.02f), 0.06f);
            c.localPosition = new Vector3((x0 + x1) * 0.5f, (top + floor) * 0.5f, (zFront - 0.02f + zBack + 0.02f) * 0.5f);
        }
        float slope = rise / Mathf.Max(0.1f, xr - xl);
        // v8: an organ — the riser under its upper manual (the upper keys reach down onto it), the full width between the cheeks
        if (organ)
        {
            float rz0 = lowerBack + 0.02f, rz1 = zBack + 0.02f, rTop = UpperBase(cols) - 0.02f;
            var riser = Box("OrganStep", new Vector3(xr - xl + 0.1f, rTop - floor, rz1 - rz0), 0.05f);
            riser.localPosition = new Vector3((xl + xr) * 0.5f, (rTop + floor) * 0.5f, (rz0 + rz1) * 0.5f);
        }
        // the back rail: one box tilted along the keys' rise (its body reaches into the platform at both ends)
        float x0r = cx0 + 0.07f, x1r = cx0 + cw - 0.07f, z0 = zBack + 0.03f, z1 = -e + d - 0.08f;
        float lift = organ ? topMax - t - rise * 0.5f : 0f;   // an organ's rail stands over its upper manual
        keyRailLen = x1r - x0r; keyRailH = 0.34f + rise + 0.3f + 0.1f + lift;
        float xc = (x0r + x1r) * 0.5f;
        float topC = t + 0.3f + lift + slope * (xc - xl);
        var rail = Box("KeyRail", new Vector3(keyRailLen, keyRailH, z1 - z0), 0.07f);
        rail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan(slope) * Mathf.Rad2Deg);
        rail.localPosition = new Vector3(xc, topC - keyRailH * 0.5f, (z0 + z1) * 0.5f);
        keyRail = rail;
    }

    Transform Box(string name, Vector3 size, float r)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(size, r, 3);
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = caseMat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        return g.transform;
    }

    /// <summary>
    /// v6 (SPEC v6 §3.1): the safe notes — a soft dot in the column chord's vibe colour (its platform colour) on every key that is a chord tone of
    /// its column's chord (Harmony.ChordOf: SongManager.ChordOfColumn — a keyboard-only column plays over the chord before it — else the key's home
    /// chord). SongManager calls it whenever the columns change (a rebuild: a chord or the key changed; a move; a live relayout); cheap when the
    /// chord is the same.
    /// </summary>
    public void RefreshKeyDots()
    {
        if (!IsKeyboard || keyDots.Count == 0) return;
        int root; IList<int> semis;
        if (!Harmony.ChordOf(this, out root, out semis) || semis == null || semis.Count == 0) return;
        var sm = SongManager.I;
        var ch = sm != null ? (sm.ChordAt(column, SongManager.OffsetBars(this), pz) ?? sm.ChordOfColumn(column)) : null;   // v9: the grid Harmony.ChordOf borrows (an offset keyboard: its measure's)
        Color vibe = ch != null ? ch.chordColor : MusicTheory.ChordColor(root, semis);
        Color col = Color.Lerp(vibe, PlatformColorOf(vibe), 0.4f);   // the vibe colour, a little deeper: it reads on ivory and on the ink keys
        if (keyDotMat != null) keyDotMat.SetColor(ColorId, Palette.A(col, 0.96f));
        if (keyRootMat != null) keyRootMat.SetColor(ColorId, Palette.A(col, 0.96f));
        if (keyRootMat == null) keyRootMat = Fx.Alpha(IconFactory.GetTexture(KeyRootGlyph), Palette.A(col, 0.96f));
        // v8: the dots mark every note a MELODY can land on (Harmony.MelodyPool: the chord tones plus the genre's tensions — "melody notes can be
        // more varied and experimental than regular grid notes"): a chord tone's dot full size, a tension's smaller; the chord's ROOT keys ("the bass
        // note for the chord … should be highlighted on the piano a bit") a bigger dot in a ring and a soft wash of the vibe colour on the key face.
        // Every pass re-poses them (a relayout resets the marks' size).
        var songKey = MusicTheory.KeyOfSong();
        var pool = Harmony.MelodyPool(root, semis, songKey.tonic, songKey.minor, keyTension);
        keyDotRoot = root; keyDotSemis = new int[semis.Count]; for (int i = 0; i < semis.Count; i++) keyDotSemis[i] = semis[i];
        for (int k = 0; k < keyDots.Count; k++)
        {
            int pc = ((chordRootMIDI + k) % 12 + 12) % 12;
            bool isRoot = ((pc - root) % 12 + 12) % 12 == 0, on = pool[pc], tense = keyTension[pc];
            var d = keyDots[k];
            if (d != null)
            {
                if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
                if (keyW != null && k < keyW.Length)
                {
                    float s = Mathf.Min(0.2f, keyW[k] * 0.56f);
                    if (isRoot) s = Mathf.Min(keyW[k] * 0.86f, s * 1.6f);
                    else if (tense) s *= TensionDotScale;
                    d.localScale = new Vector3(s, 1f, s);
                }
                var r = d.GetComponent<MeshRenderer>(); if (r != null) r.sharedMaterial = isRoot ? keyRootMat : keyDotMat;
            }
            if (k < tiles.Count && tiles[k] != null) tiles[k].SetRestTint(vibe, isRoot ? (IsBlackKeyMidi(chordRootMIDI + k) ? RootWashBlack : RootWash) : 0f);
        }
    }
    readonly bool[] keyTension = new bool[12];

    /// <summary>v8 (tests): whether key <paramref name="k"/> wears a TENSION's dot (a melody note that is not a chord tone: the smaller dot).</summary>
    public bool KeyTensionShown(int k) => KeyDotShown(k) && keyTension[((chordRootMIDI + k) % 12 + 12) % 12];

    /// <summary>v8 (tests): whether key <paramref name="k"/> wears the chord root's mark (the ringed dot and the wash).</summary>
    public bool KeyRootShown(int k) => k >= 0 && k < keyDots.Count && keyDots[k] != null && keyDots[k].gameObject.activeSelf && keyRootMat != null
        && keyDots[k].GetComponent<MeshRenderer>() != null && keyDots[k].GetComponent<MeshRenderer>().sharedMaterial == keyRootMat;

    /// <summary>v6 (tests): how many keys wear the chord-tone dot now; whether key <paramref name="k"/> does.</summary>
    public int KeyDotsShown { get { int n = 0; foreach (var d in keyDots) if (d != null && d.gameObject.activeSelf) n++; return n; } }
    public bool KeyDotShown(int k) => k >= 0 && k < keyDots.Count && keyDots[k] != null && keyDots[k].gameObject.activeSelf;
    /// <summary>v6: the keyboard's back rail (null for other islands).</summary>
    public Transform KeyRail => keyRail;

    /// <summary>v6: the key of this keyboard under island-local point <paramref name="local"/> (x / z; a black key wins where it covers a white one),
    /// null off the keys or on another island. H: the platform-hit fallback (PathManager.SeamTile maps a grid pitch, which a keyboard does not have).</summary>
    public TileInteraction KeyAtLocal(Vector3 local)
    {
        if (!IsKeyboard || keyCx == null || grid == null) return null;
        TileInteraction best = null;
        for (int pass = 0; pass < 2 && best == null; pass++)
            for (int k = 0; k < keyCx.Length; k++)
            {
                if (IsBlackKeyMidi(chordRootMIDI + k) != (pass == 0)) continue;
                float hx = keyW[k] * 0.5f + (pass == 0 ? 0f : ProjectConfig.KeyGap * 0.5f), hz = keyL[k] * 0.5f;
                if (Mathf.Abs(local.x - keyCx[k]) <= hx && Mathf.Abs(local.z - keyCz[k]) <= hz) { best = grid[k, 0]; break; }
            }
        return best;
    }

    /// <summary>v6: the key under world point <paramref name="world"/> (see <see cref="KeyAtLocal"/>).</summary>
    public TileInteraction KeyAt(Vector3 world) => KeyAtLocal(transform.InverseTransformPoint(world));

    // ------------------------------------------------------------------ v6 job badge (SPEC v6 §3.4)
    /// <summary>v6: the badge quad's size (U1's WorldTexture draws the badge in its inner 80 %: a ≈ 0.74 u badge, the size the v5 house read at).</summary>
    public const float JobBadgeSize = 0.92f;
    static readonly Material[] jobMats = new Material[5];

    /// <summary>v6: the job digit the platform badge shows now (0 = none: keyboards, Moons, asleep).</summary>
    public int JobShown => jobBadge != null && jobBadge.gameObject.activeSelf ? jobShown : 0;
    /// <summary>v6: the platform's job badge (null on keyboards and Moons).</summary>
    public Transform JobBadgeGlyph => jobBadge;

    /// <summary>v6 (SPEC v6 §3.4): shows the chord's job (Harmony.Job in the song key: 1 home, 2 away, 3 heart, 4 pull) as U1's badge — the number
    /// on its family shape — on the platform's front-left corner; none for job 0; hidden while the island sleeps. After every build (the chord and
    /// the key only change through a rebuild) and on the sleep look.</summary>
    public void RefreshJobBadge()
    {
        if (jobBadge == null) return;
        int job = Job;
        bool on = JobBadge.Valid(job) && !sleepShown;
        if (on && job != jobShown)
        {
            var m = JobMaterial(job);
            if (m == null) on = false;
            else { jobBadge.GetComponent<MeshRenderer>().sharedMaterial = m; jobShown = job; }
        }
        if (jobBadge.gameObject.activeSelf != on) jobBadge.gameObject.SetActive(on);
    }

    static Material JobMaterial(int job)
    {
        if (!JobBadge.Valid(job)) return null;
        if (jobMats[job] == null)
        {
            var tex = JobBadge.WorldTexture(job);
            if (tex == null) return null;
            jobMats[job] = Fx.Alpha(tex, Color.white);
        }
        return jobMats[job];
    }

    // ------------------------------------------------------------------ v6 the octave tower (SPEC v6 §3.3)
    /// <summary>
    /// v6: the tower this frame (the user: "if you raise a grid by an octave, the grid itself should rise … on its turn and then slowly go down after
    /// its done"). The target is SongManager.TowerLiftAt — a pure function of the song beat: the rest hint, the rise on the island's turn, the bob
    /// on the beat, the slow sink — while playing or paused; stopped, the rest hint, or after ▲ / ▼ the preview (the same rise and sink once, at the
    /// song's tempo); while dragged, the rest hint. A change of the target that the beat's own progress cannot explain (a seek, a stop, a loop wrap,
    /// an undo, a register change: more than TowerMaxSlope per beat per octave) glides there. Cubes, the comet, the cable, the header and the hub
    /// follow the transform / LiftY.
    /// </summary>
    void UpdateTower(float dt)
    {
        Lifting = false;
        if (IsMoon) return;
        double beat = GlobalClock.SongBeatD;
        if (register == 0 && previewT < 0f && !towerCatch && Mathf.Abs(regShown) < 1e-6f)
        {
            towerState = 0; towerPhaseT = 0f; towerLastTarget = 0f; towerLastBeat = beat; towerInit = true;
            return;
        }
        var sm = SongManager.I;
        bool running = GlobalClock.IsPlaying || beat > 1e-6;   // paused mid-song: the lift of the paused beat
        float rest = SongManager.TowerRestOf(register), top = SongManager.TowerTopOf(register);
        float target; int state = 0; float phase = 0f; double progress = 0.0; bool jumped = false;
        if (sm == null || dragLift > 0.001f) { target = rest; previewT = -1f; }
        else if (running)
        {
            previewT = -1f;
            target = sm.TowerLiftAt(this, beat, out state, out phase);
            progress = beat - towerLastBeat;
            jumped = progress < -1e-6 || progress > 0.5;
        }
        else if (previewT < 0f && ResetU(ProjectConfig.TowerSinkBeats) >= 0f && Mathf.Abs(resetLiftFrom - rest) > 1e-3f)
        {
            // v7 §12.3: a stop — the tower sinks slowly in real time, each with its own small delay ("not in sync, but slightly out of sync")
            float ru = ResetU(ProjectConfig.TowerSinkBeats);
            target = resetLiftFrom + (rest - resetLiftFrom) * (0.5f - 0.5f * Mathf.Cos(Mathf.PI * ru));
            state = ru <= 0f ? 2 : (ru < 1f ? 3 : 0); phase = ru;
            regShown = target; towerCatch = false; towerLastTarget = target; towerLastBeat = beat; towerState = state; towerPhaseT = phase;
            float d = Mathf.Abs(regShown - towerPrevShown); towerPrevShown = regShown;
            Lifting = d > 1e-5f;
            if (Lifting) { ApplyPose(); RefreshCubeLines(); }
            return;
        }
        else if (previewT >= 0f)
        {
            previewT += dt;
            double bps = System.Math.Max(0.5, GlobalClock.BeatsPerSecond);
            double u = -ProjectConfig.TowerLead + previewT * bps, upEnd = ProjectConfig.TowerRiseEnd + ProjectConfig.TowerPreviewHold;
            float f = SongManager.TowerPhase(u, upEnd, SongManager.TowerBobOf(register), out state, out phase);
            target = rest + (top - rest) * f;
            progress = dt * bps;
            if (u >= upEnd + ProjectConfig.TowerSinkBeats) previewT = -1f;
        }
        else target = rest;
        float prev = regShown;
        if (!towerInit) { regShown = target; towerInit = true; towerCatch = false; }
        else
        {
            float allow = jumped ? 0f : ProjectConfig.TowerMaxSlope * Mathf.Max(1, Mathf.Abs(register)) * (float)System.Math.Max(0.0, progress);
            if (!towerCatch && Mathf.Abs(target - towerLastTarget) > allow + 0.02f) towerCatch = true;
            if (towerCatch)
            {
                regShown = Mathf.Lerp(regShown, target, 1f - Mathf.Exp(-dt * 10f));
                if (Mathf.Abs(target - regShown) < 0.01f) { regShown = target; towerCatch = false; }
                else state = register == 0 ? 3 : ((target > regShown) == (register > 0) ? 1 : 3);   // a glide reports its direction
            }
            else regShown = target;
        }
        towerLastTarget = target; towerLastBeat = beat;
        towerState = state; towerPhaseT = phase;
        towerPrevShown = regShown;
        Lifting = Mathf.Abs(regShown - prev) > 1e-5f;
        if (Lifting) { ApplyPose(); RefreshCubeLines(); }
    }
    float towerPrevShown;

    /// <summary>v6: a rebuilt island starts at the tower lift its old self showed (SongManager after an undo / a structural edit): the lift then
    /// glides to its own target, like after a seek.</summary>
    public void CarryLift(float shown)
    {
        if (IsMoon) return;
        regShown = shown; towerInit = true; towerCatch = false; towerLastTarget = shown; towerLastBeat = GlobalClock.SongBeatD;
        ApplyPose();
    }

    // ---- legacy API
    public void initializeInversionGrid() { Build(); }
    public List<int> getInversion(List<int> semitones)
    {
        var result = new List<int>(semitones);
        int low = result[0];
        result.RemoveAt(0);
        result.Add(low + 12);
        result.Sort();
        return result;
    }
}

/// <summary>Merge flash (SPEC v3 §3.3): a white glow sweeps along the seam from the back to the front over 0.35 s, sparkles on the way; self-destroys.</summary>
public class SeamSweep : MonoBehaviour
{
    KeyBlock owner; float len, x, t, delay; Transform q; Material mat; Color col; bool sparked;
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    public void Init(KeyBlock a, KeyBlock right, float wait)
    {
        owner = a; delay = Mathf.Max(0f, wait);
        len = Mathf.Min(a.Depth, right != null ? right.Depth : a.Depth);
        x = -KeyBlock.EdgeInset + a.Width;
        col = Color.Lerp(a.chordColor, right != null ? right.chordColor : a.chordColor, 0.5f);
        var go = new GameObject("Flash");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var mr = go.AddComponent<MeshRenderer>();
        mat = Fx.Additive(IconFactory.GetTexture("glow"), Color.white, 0f);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        q = go.transform;
        q.localScale = new Vector3(1.1f, 1f, 2.2f);
        q.localPosition = new Vector3(x, 0.03f, -KeyBlock.EdgeInset + len);
        go.SetActive(false);
    }

    void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }
        if (delay > 0f) { delay -= Time.deltaTime; return; }
        if (!q.gameObject.activeSelf) q.gameObject.SetActive(true);
        t += Time.deltaTime / 0.35f;
        float u = Mathf.Clamp01(t);
        float zBack = -KeyBlock.EdgeInset + len, zFront = -KeyBlock.EdgeInset;
        q.localPosition = new Vector3(x, 0.03f, Mathf.Lerp(zBack, zFront, Ease.OutQuad(u)));
        mat.SetFloat(IntensityId, 3.2f * Mathf.Sin(u * Mathf.PI) + 0.2f);
        if (!sparked && u > 0.15f)
        {
            sparked = true;
            for (int i = 0; i < 4; i++)
                Fx.Burst(owner.transform.TransformPoint(new Vector3(x, 0.12f, zFront + len * (i + 0.5f) / 4f)), Color.Lerp(col, Color.white, 0.5f), 7, 1.7f);
        }
        if (t >= 1f) Destroy(gameObject);
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }
}
