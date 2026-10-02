# DeaCube v4 — columns, per-note length, draft hologram, rhythm timeline, hand-made UI

Single source of truth for every v4 engineer. Project: /Users/ethanchen/audio_to_3d_space/AudioCube_Unity (Unity 6000.4, URP).
Protocol (lock, compile check, tests, captures): scratchpad/v4/PROTOCOL.md. UI language: scratchpad/v4/UI.md (from the reference research).
v3 spec (world, islands, merge, inspector, presenter, menu): scratchpad/v3/SPEC.md. v2 spec (audio/rhythm model): docs/deacube-v2-spec.md.

## 0. The user's request (verbatim, 2026-09-29)
"when building the grids, i'd like you to be able to expand vertically too as well as horizontally -> so it's still going left to right but
you can add grids below and above the grid (not like actually below and above but below and above on the screen) where all the grids in a
column play at the same time from left to right. / you should be able to toggle these grids and edit them like components - like if you
hover over the ground beneath the grid the mouse should show like an move button where you can then drag the grid whereever you'd like
within its column (respecting stuff) / moreover you should be able to duplicate grids, edit grids, make them lower or higher, etc. /
moreover i want to introduce some concept of rhythm rather than constant speed cubes -> when you're placing down paths, ensure live
feedback and all the ui is very intuitive and encourage iteration of that cube path. so like live cube playing, and then selection of
speeds for the next note (like when you click it, a row pops up of like quarter note, eight note, half note, whole note, and if you click
on them you can also do dotted. but dont actually include the music theory, the point is no music theory, so add some ui that conveys the
duration, maybe with like fat (bloated) cube for whole note during that interval and smaller cube for fast notes etc. this is weird though
because the cube is constantly going to be transform depending on rhythm so include transition animations lively / also allow editing of
the rhythm by showing the timeline of the cubes patterns and size shifts and allowing those to be edited without deleting the entire cube"
Follow-ups: "when you're setting the cube path like actively, it should show a ghost cube (like a semitransparent hologram cube of that
color) tracing through the path -> and if the mouse is hovering over the next note, it should play that note too (not add it, but just
play it so the user can hear what its like) then upon click it soldifies and stuff" · "the UI right now in the editorial (the buttons and
stuff) are a bit too AI generated and read as a bit too artificial. I want you to analyze similar games like this and how their UI works
(like Melatonin for example)".
Later (same day): "the falling should be steeper, and at more varied distances like the cubes are genuinely falling and hitting notes along
the way, not bouncing. cubes and their paths dont have to be preserved, it should just seem like cubes are falling quickly and hitting, so
if you need to spawn multiple cubes for one cube's path that's totally fine (in the case one cube falls already and the path still has
notes left). moreover the start screen transition looks like AI -> looks cool but it breaking apart looks AI generated" · "when hovering
on cubes, the outline should be a rounded cube corner too not a solid hard edge cube" (done at M0: CubeOutline draws a rounded wire box).
Later still: "the triangle underneath the island shouldnt shoot a laser ray upon every measure, it should just fill gradually until it
reaches the end of measure" (→ K: no SkyBeam/starburst on arrival; the anchor hub fills with the chord colour over its column's span)
· "the cube colors should be less saturated and more pastel-ish" (→ U1: pastel Instruments.Colors, S ≈ 0.35-0.45, V ≈ 0.95-1).
And: "When the cubes move and stuff I think you should add a bit of smearing, like handrawn organic smearing that has no outline" (→ R:
CubeSmear + DeaCube_Smear.shader, world hops; P uses it for falling cubes; after "the smearing doesnt look organic at all": a BODY smear —
a flat unlit copy of the cube mesh stretched back along the real curved path with lumpy noise, tapering; no strands) · "for the falling it should be falling quicker and they
should bounce more convincingly like they're getting thrown at the tiles" (→ P) · "the cubes on the start screen should be like liquid
pixellated (same sprite but liquid pixellated like in goose/cookingsim)" (→ T: low-res buffer + posterized bands + cluster-dot dither +
nearest upscale for MenuStage's floating cubes; reference: ~/Documents/GitHub/cookingsim/src/core/postfx.js, ~/Documents/GitHub/goose).

## 1. Principles
- No music theory on screen: note length = cube SIZE (tiny = fast … fat/bloated = long); "dotted" = a little satellite dot. No note symbols,
  no numbers except the tempo digits. Hear it: every length choice auditions.
- Live and iterative: the draft cube plays while you draw (focus loop), hovering auditions, clicks solidify, everything undoable.
- A song with one island per column behaves EXACTLY like v3 (timing, audio, merge, tests). Old saves load as one-island columns.
- Every user gesture that changes the song ends in exactly one History.Push() (by the gesture owner; SongManager structural ops push
  themselves as today).
- Words on screen minimal (icons first); tutorial sentences, menu words and tempo digits allowed.

## 2. Shared contract (M0 lands these as compilable stubs; owners implement; never rename/remove them)
### 2.1 Data (SongState.cs; saved JSON; missing fields keep initialiser defaults)
- MeasureState / SongManager.MeasureData: `int col = -1` (column index; -1 = legacy → one column per island in order), `int reg` (register
  shift in octaves, -2..+2). Clone / From / ToData carry both.
- CubeState: `int[] durs` (ticks per node, 24 ticks = 1 beat; null = legacy uniform-step engine). Clone carries it.
- SongState.CurrentVersion = 3. `IsLegacy` keeps meaning "v1" (version < 2).
### 2.2 Constants (ProjectConfig)
ColumnGap 3.7 (x gap between unglued columns: v3's 12 u pitch), LaneGap 1.8 (min z clearance between islands of a column), MaxLanes 4,
RegisterRise 0.7 (platform y per register octave), TicksPerBeat 24, HopMaxBeats 0.5 (a hop never takes longer), SizeTable {0.62, 0.78, 1.0,
1.24, 1.5} for {16th, 8th, quarter, half, whole}.
### 2.3 Columns (SongManager / KeyBlock; owner K)
- KeyBlock: `int column` (set by SongManager), `int register`, `bool isAnchor` (first island of its column), `Column`, `IsAnchor`.
- SongManager: `int ColumnCount`, `int ColumnOf(int island)`, `List<KeyBlock> ColumnIslands(int col)` (anchor first), `KeyBlock AnchorOf(int col)`,
  `float ColumnStart(int col)`, `float ColumnLength(int col)`, `int ActiveColumn(float beat)`, `int LitColumn` (while playing), `bool
  IsActiveAt(KeyBlock kb, float beat)` (its column spans the beat), `int AddIslandInColumn(int island, bool above)`, `int DuplicateIsland(int
  island, int where)` (0 right = new column after, 1 below, 2 above), `void MoveIslandInColumn(int island, float pz, bool commit)`,
  `void SetRegister(int island, int reg)`, `void MoveColumn(int from, int to)`, `int PlaceIslandInColumn(MeasureData md, int col, float pz)`,
  `int PlaceIslandAsColumn(MeasureData md, int insertCol)`, event `OnColumnsChanged`.
- Unchanged meaning, column-aware: `ActiveMeasureIndex(beat)` / `LitIsland` return the ANCHOR index of the active column; `MeasureStarts[i]`
  = start of island i's column; `KeyBlock.IsLit` = its column is lit.
### 2.4 Rhythm (AudioCube / PathManager / GlobalClock; owner R)
- AudioCube: `List<int> durs`, `bool HasDurations`, `float DurBeats(int node)`, `void SetDuration(int node, int ticks)`, `void SetAllDurations(IList<int>)`,
  `void BakeDurations()`, `float StepLenBeats(int k)`, `float SizeFactor` (current animated size, 1 = today's cube), `static float SizeOf(float beats)`,
  `static readonly int[] BaseTicks = {6,12,24,48,96}`, `static int TicksOf(int size, bool dotted)`, `static bool TryDecode(int ticks, out int size,
  out bool dotted)`, `bool Hologram`, `void SetHologram(bool on)`, `void Solidify()`, `struct TimelineEvent { int k, node; float start, len;
  bool rest; int mod; }`, `int TimelineEvents(int window, List<TimelineEvent> into)` (both engines; M0 implements it on the existing step math).
- PathManager: `AudioCube Draft`, `int BrushTicks`, `void SetDraftDuration(int ticks)` (latest node + brush), `TileInteraction AuditionTile`,
  `void RemoveLastNode()`, `bool HoverGround` (pointer over an island's platform margin, not a tile/cube/hub), events `OnDraftStarted`,
  `OnDraftChanged`, `OnDraftFinished(AudioCube)`.
- GlobalClock: `bool HasRegion`, `double RegionStart, RegionEnd`, `void SetRegion(double start, double end)`, `void ClearRegion()`,
  `double LoopStartBeat`, `double LoopEndBeat`, event `OnRegionChanged`.
- FocusLoop (static): `bool Active`, `int Column`, `void Begin(int column)`, `void Dismiss()`, event `OnChanged`.
### 2.5 UI components (stubs; owners U1/U2)
- `IslandHeader` (U1): `static KeyBlock Current`, `static bool IsShown`, `static void Show(KeyBlock kb)`, `static void Hide()`.
- `CursorKit` (U1): `enum Kind { Default, MoveV, Grab, Draw }`, `static void Want(Kind k, object owner)`, `static void Release(object owner)`.
- `DurationPicker` (U2): `static bool IsShown`, `static void Show(AudioCube cube, int node)`, `static void Hide()`, `static int SelectedTicks`.
- `RhythmStrip` (U2): MonoBehaviour, `static RhythmStrip Create(Transform parent)`, `void Bind(AudioCube c)`, `int SelectedNote`, event `OnNoteSelected`.
- `InspectorCard` (U2): the new inspector card (replaces UIManager's cube panel at integration).
- `ColumnBands` (K): world visuals, `static void Refresh()`.
- Hint ids (Hints.Register by the owner): draft.lengths, island.header, island.move, island.toggle, island.dup, island.octave, island.addAbove,
  island.addBelow, inspector.rhythm, focusLoop. Existing ids stay registered (Onboarding uses them) unless U1 retires them with the tutorial.

## 3. Package K — columns and island components (world side)
K1 Layout. Islands order = column-major; within a column the anchor first, then the others in creation order. Column c's islands share
  px = X[c]: X[0] = the first island's px as built; X[c+1] = X[c] + IslandWidth + (Glued(c,c+1) ? 0 : ColumnGap); Glued = an island of c and
  an island of c+1 share a merge group. z is free per island (pz), resolved so no two islands of a column overlap (clearance LaneGap):
  the island being edited keeps its z, the others are pushed away from it (order kept unless an island's centre passes another's: swap).
  All islands of a column share `bars` (SetBars applies to the column; bars = max on normalisation). MaxLanes 4 per column.
  Legacy / v3 files (col == -1): col = index, every pz = the first island's pz (a clean row; merged groups stay merged), then normal layout.
  Moons that overlap an island after any layout slide to the nearest free spot (SlideFrom, no History).
  Normalisation (load, undo/redo, every structural edit): contiguous columns 0..n-1, column-major order with cube `measure` indices remapped,
  bars equalised, z resolved, groups re-validated (K4).
K2 Timing. RecomputeMeasureStarts: per column start/length (length = bars·BeatsPerBar); every island of the column gets startBeatOffset =
  column start; MeasureStarts[i] = its column start; TotalBeats = Σ column lengths. Resident windows = the island's column span (silent =
  that island's sleep). Rider windows: per column, on the island of that column nearest in z to the rider's home island. Moons unchanged.
  Two islands in one column sound together: verify by recording (onsets of both within ±5 ms of their scheduled beats).
K3 World look. Beat ring → a rounded-rectangle beat TRACK hugging the platform (margin 0.55, 16th segments evenly by arc length, starting
  front-centre, clockwise; pre-light and playhead as today); KeyBlock.RingPoint/LapPoint follow the track (group members: the v3 inside-margin
  lap). Column bands: a soft rounded band on the sea under each column (from the top island's back edge +1 to the bottom island's front
  edge -1, IslandWidth + 1.2 wide), lit column glows faintly in its anchor's chord colour; while an island is dragged its band shows the
  allowed range. Hubs only on anchors (hub = route anchor; its click selects/frames only — no bar cycling). Route cable between consecutive
  anchors (v3 logic on anchors). Comet laps the anchor; every other island of the lit column gets a satellite comet (0.6 scale, same style,
  lapping its own track in sync, popping in/out at column change). Lit column: all its islands lit.
  Focus loop (R4): while GlobalClock.HasRegion, the comet's last-beat flight of the region's last column goes back to the region's first
  column (it never flies on to the next column); camera follow stays on the looped column.
K4 Merge in columns. A merge joins islands of ADJACENT columns in the same lane (front-back overlap ≥ MergeOverlap): the columns glue
  (gap 0) and the right island snaps to the left's z; group members are consecutive columns, one per column, z-aligned. Dragging a group
  member vertically moves the whole group (each member in its own column). Split (scissors) unglues: the right part slides east by the gap.
  Tray ghost merge zones work the same way.
K5 Island components (world ops K owns; the header UI is U1's IslandHeader calling these):
  - add above / below: AddIslandInColumn → same chord (root, semis) and bars as the reference island, no cubes, placed LaneGap beyond the
    reference's back (above, +z) or front (below, -z) edge, neighbours pushed; rises from the sea. Refused (shake) when the column is full.
  - duplicate: DuplicateIsland(i, 0 right | 1 below | 2 above): chord + register + cubes (new ids, same durs/mods) copied; right = a new
    column inserted right after i's column with the copy at i's z (the song gets longer); below/above = same column.
  - register: SetRegister(i, reg ∈ -2..2): tiles sound reg·12 semitones higher; the platform rises/sinks reg·RegisterRise with a springy
    0.35 s ease-out-back and a quick chord arpeggio in the new register (preview voice); cubes ride along.
  - toggle: SetSleep (exists). An off island reads clearly off: desaturated platform, dimmed tiles, its cubes asleep, a small closed-eye glyph.
  - delete: RemoveMeasure (column-aware: an emptied column disappears, later columns slide left).
  - move: IslandDrag within the column: press on the platform margin (PathManager.HoverGround) or the header's grip; x locked, z follows the
    pointer with the grab offset, neighbours slide (push / swap preview), group moves together, Moons pushed on release; release → snap
    0.5 u, MoveIslandInColumn(commit) + History.Push. Anchors' HUB drag keeps v3 column reorder (drop on the route gap → MoveColumn) and
    tear-out of a group member. Esc / right-click cancels.
  - tray (U1 owns IslandTray.cs; K owns IslandGhost incl. Snapshot/Place and SongManager.PlaceIslandAfter/PlaceAll semantics): card click →
    a new column after the focused island's column (v3 rule); ghost drag snaps to column slots: STACK (empty slot above/
    below an island of a column: PlaceIslandInColumn), INSERT (between columns / either end: PlaceIslandAsColumn), MERGE (flush against an
    island's east/west edge in its lane), BLOCKED (column full / overlapping); the wand places the remaining cards as new columns at the end.
K6 Camera: follow/framing frames the lit COLUMN (bounds of its islands); FrameIsland unchanged; overview unchanged (plus the F9 fix).
K7 Also owned: the trackpad/orbit fixes already landed by the fix batch stay intact.
K tests: Assets/DeaCube/Tests/V4ChecksK.cs (layout invariants, legacy conversion of the v1 fixture = v3 timing, stacked timing + recorded
  simultaneity, add/duplicate/register/toggle/delete, drag-in-column with push/swap, tray STACK/INSERT/MERGE, merge + split in columns,
  undo/redo round trips, save/load round trip with col/reg, user save md5). Update V3ChecksB where v4 intentionally changes behaviour.

## 4. Package R — per-note length, draft hologram, focus loop
R1 Duration engine (AudioCube). Durations mode when durs.Count == nodes.Count > 0; otherwise the legacy uniform-step + necklace engine,
  unchanged. In durations mode every step is a hit; event k plays node NodeForHit(k + phase) (Loop / PingPong / Once / reverse as today) for
  durs[node]/24 beats. Precompute one period (Loop n, PingPong max(1, 2n-2), Once n): raw starts + cycle length; Once ends after n events
  (the cube rests on EndNode). Swing = a per-beat time warp W(t): frac < 0.5 → frac·(1+s), else 0.5(1+s) + (frac-0.5)(1-s), s = Swing/3.
  StepStartLocalBeat(k) = W(raw(k)); ComputeStep inverts W and binary-searches; StepLenBeats(k) = start(k+1) - start(k); PatternHit,
  HitIndex, NextPatternHitStep, StepsInWindow, Decide, TryGetPose, HitSteps16, TrySchedule (stepSec = StepLenBeats/bps), ratchet/echo spacing
  and the hop all use these. The hop starts at max(snapThreshold·len, len - HopMaxBeats). Mods keep their meaning (rest = silent for its
  length, tie, ratchet splits the note, ghost/accent, coin, lift). Legacy cubes: bit-identical timing to v3 (HitSteps16 of every fixture cube
  before == after). BakeDurations converts a legacy cube (per node: the gap from its first hit to the next hit in window 0, snapped to the
  nearest of the 10 UI lengths; hits = -1) — used by the first rhythm edit of a legacy cube. Node edits (Insert/Remove/Move/SetPathAndMods/
  Reverse/Shift, twins, stamps, riders, ToState/ApplySettings/RestoreCube) keep durs aligned.
R2 Size. SizeOf(beats) = SizeTable interpolated on log2(beats) (clamped): 16th 0.62, 8th 0.78, quarter 1.0, half 1.24, whole 1.5 (dotted lengths
  land between). The cube's scale follows the note it is on (stopped: its home node's note) through a springy transition (ω≈18, ζ≈0.35):
  growing = inflate with overshoot + a soft puff ring; shrinking = a quick squash-pop + 3-5 ink specks; long notes breathe (+4 % over the note).
  Dotted notes carry a small ink-outlined satellite dot orbiting the cube (pops in/out). Path beads scale with their node's length. Stacked
  cubes stack by their visual sizes. The presenter's cubes use SizeOf too (package P).
R3 Draft (PathManager). Press a free tile → the draft cube appears as a HOLOGRAM (semi-transparent in the instrument colour, glitchy scanlines,
  chromatic split — Spider-Verse multiverse glitch; new shader Resources/Shaders/DeaCube_Hologram.shader) and node 0 gets length BrushTicks.
  The draft plays live: it has windows like a finished cube and the focus loop (R4) loops its column; it traces its path in rhythm.
  Hover audition: while drafting, hovering any tile of the draft island other than the last node plays that tile's note once (preview voice,
  instrument of the draft, ≤ 10/s, once per entry) and shows a hologram preview of what a click would add (dashed segment + ghost beads
  along the grid line ExtendTo would fill). Click → those tiles are added and SOLIDIFY (beads pop 0 → 1.2 → 1, tile flash, the note plays).
  Lengths: DurationPicker (U2) calls SetDraftDuration(ticks) → the latest node's length and the brush; keys [ shorter, ] longer, . dotted.
  Backspace or Cmd+Z removes the last node (last one → cancel the draft). Finish (click the end tile again, Enter, right-click, end of a drag
  stroke, a press on another island / empty sea / the HUD) → Solidify(): 0.35 s glitch → chromatic split converges → white flash → solid
  toon cube + ink burst + pop sound; History.Push (one). Cancel (Esc) → the hologram dissolves (scanline wipe). The draft never enters
  SongState.Capture until finished (as today). Include the fix-batch behaviour (seam presses map to tiles, HUD press finishes first).
R4 Focus loop (GlobalClock region + FocusLoop). Begin(column): region = the column's span; if the clock was stopped, remember it, seek to the
  region start and play; if playing outside the region, seek to its start. The clock wraps at RegionEnd to RegionStart (LoopIndex++); cube
  lookahead scheduling, Moon windows and VoiceRules' wrap logic respect the region (the comet's side is K's, see K3). Engaged by starting a draft and by opening the inspector (U2 calls FocusLoop.Begin); it STAYS after finishing/closing until
  dismissed: Esc with nothing else to close, a click on empty sea, the HUD focus badge ✕ (U1), entering Present or the menu, a new song.
  Space pauses/resumes inside the region. Beginning on another column moves it. Dismiss: ClearRegion; if the clock was stopped when the
  focus began, stop (notes ring out); else keep playing the whole song from where it is.
R5 VoiceRules call sites that compare the active island (e.g. "the tile's island is active at beat b", chord at a beat) use
  SongManager.IsActiveAt / ActiveColumn (column-aware).
R tests: Assets/DeaCube/Tests/V4ChecksR.cs (engine timing tables incl. swing/PingPong/Once/reverse/phase; legacy identity on the fixture;
  recorded onsets ±5 ms and note lengths ≈0.92·length; size transitions; draft via SimPointer: hologram, hover audition count, add, keys,
  Backspace, finish → solid + one History entry, cancel; focus loop wrap + restore; save/load/undo of durs; bake). V2Checks stay green.

## 5. Package U1 — hand-made HUD, island header, cursor, tutorial
Follow scratchpad/v4/UI.md (written from the reference research). Scope: restyle/rebuild the world HUD (UIManager: top clusters, overview
strip → columns with stacked chips, palette, transport, tempo, focus-loop badge), the kit (UIKit, Comic, IconFactory glyph style), the
prompt (InterfaceController), IslandHeader (component toolbar: grip, on/off, register ▼▲, duplicate → / ↓, + above /
+ below bubbles, more ⋯ for chord/length/energy/fall/delete; appears on island hover after 0.15 s, lingers 0.4 s), CursorKit (hardware
cursors drawn procedurally: MoveV over island margins, Grab while dragging), Onboarding + TutorialBubble rewritten for v4 (draw with the
hologram, pick a length, open the cube → rhythm strip, add an island above/below, drag it in its column, present). Remove the old cube
panel from UIManager once U2's InspectorCard is live (coordinate through the orchestrator).
U1 tests: Assets/DeaCube/Tests/V4ChecksU1.cs + keep V3ChecksD green (update the tutorial assertions you change).

## 6. Package U2 — inspector card, rhythm strip, length picker
Follow UI.md. InspectorCard: a new, calmer card (cube colour/instrument, volume, the flat path grid, the rhythm strip, path tools; the
advanced modifiers — twin, shadow, echo, rider, stamp, gate, octave, necklace for legacy cubes — behind one "more" drawer). RhythmStrip:
one block per event of the cube's window (TimelineEvents): x = start, width = length, a cube icon sized by SizeOf, rests hollow, bar/beat
ticks, the loop repeats of a short path drawn faded, a playhead, the playing block bounces; click a block = select that note (its bead
highlights in the grid and the world) and show the length row under the strip; drag a block's right edge = stretch/shrink through the
10 lengths (auditions); rest toggle; delete note (keeps ≥ 1). Legacy cubes: first edit calls BakeDurations. DurationPicker: the row of 5
cube sizes (click the selected one again = dotted; the dot shows), anchored above the draft island's far edge (never over its tiles),
hover auditions that length on the latest note, click → PathManager.SetDraftDuration; the same row is used under the strip.
CubeInspector: Open → FocusLoop.Begin(the cube's column). Hints: inspector.rhythm, draft.lengths (+ existing inspector.* ids).
U2 tests: Assets/DeaCube/Tests/V4ChecksU2.cs + keep V3ChecksA green (update the assertions tied to the old card).

## 7. Package P — presentation: cubes ricochet down through solid angled tiles, the camera dives with them
User feedback (in order): (a) "the falling should be steeper, and at more varied distances like the cubes are genuinely falling and hitting
notes along the way, not bouncing. cubes and their paths dont have to be preserved ... if you need to spawn multiple cubes for one cube's path
that's totally fine" (b) "when they're falling, the tiles the cubes bounce off of should be solid and not move -> the camera should just be
moving down much faster basically and the cubes should react and actually bounce off the tiles (tiles angled so that they follow the cubes)".
Reconciled: the "marble falls through angled platforms" music video.
P1 Tiles: one SOLID, STATIC tile per note at its contact point (v3 colour / glyph / pitch brightness), angled so the incoming velocity
  reflects into the outgoing one (normal = normalize(v̂_out − v̂_in)); it never moves after appearing (flash + tiny press / ripple on contact
  only); it leaves the frame upward as the camera descends and is recycled off-screen.
P2 Camera: descends continuously and FAST at the streams' mean fall speed (a pure function of the beat), framing the falling cubes with a
  little look-ahead below; the backdrop scrolls with it.
P3 Motion: "time is depth" with a much larger K (steep and fast, tuned by eye ~1.5-2.5 u/beat); gravity low enough that no flight ever
  launches upward (e.g. g ≤ K·bps²/2 for gaps up to 4 beats): every flight is a downward ballistic arc and every hit a ricochet off the
  angled tile, never an up-and-down hop. Lateral steps vary widely (seeded ±0.6-2.4 u, steering back toward the lane centre); grid paths
  are not preserved. The cube reacts on each hit (squash, a spin kick in the deflection direction); size = SizeOf(the note's length).
P4 Respawn only for gaps: when a stream's next note is more than ~2 beats away (rests, a sleeping island, a Moon lull) or its cube would
  leave the frame, it falls out of view and a new cube drops in from above the frame for the next note.
P5 Keep: a pure function of the beat (seek / loop / replay), pooled views, no per-frame allocation, TileBudget, overlay/HUD, the Presenter
  lifecycle, stacked islands of a column pouring together (lanes by island), legacy and durations cubes, contact sync median ≤ 8 ms.
P tests: V3ChecksC updated to the new model (list every replaced assertion), Assets/DeaCube/Tests/V4ChecksP.cs (no upward flight, tiles
  static after appearing, camera speed, respawn on gaps, contact sync, 2- and 3-lane columns, a durations song 16th..whole, no GC).

## 7b. Package T — the title screen's exit transition
The user: "the start screen transition looks like AI -> looks cool but it breaking apart looks AI generated" (MenuStage.BeginFall: every pin
of the cube wall breaks off and falls, with comic bursts per letter). Replace the leave with a hand-made comic transition, same energy:
anticipation (the clicked item squashes; the frame holds one frame; a white impact frame with a lettered onomatopoeia sticker at the click
point), then the menu frame is cut into 3-4 slanted comic panels with thick ink gutters radiating from the click point that slide / swing
off-screen in different directions ON TWOS with hard offset shadows, revealing the world already rendered behind (the island rising); the
HUD pops in stepped at the end; ≤ 1.2 s. No particles, no shatter, no dissolve, no cubes flying apart: rigid ink-bordered paper shapes only.
Keep MainMenu's public API, phases and timings contract (V3ChecksD must stay green), the intro reveal, the autosave and the music. Should:
the lowercase text menu with a wavy underline instead of the four pills (ui_research.md §4.7), using the Ink kit.
T tests: Assets/DeaCube/Tests/V4ChecksT.cs (the leave runs end to end from each button, lasts ≤ 1.2 s, never spawns particles / falling
  pins, HUD + world state identical to v3 after it); captures of 4-6 frames of the transition that you LOOK at.

## 8. Ownership (edit only your files; request others through your report)
- K: SongManager.cs, KeyBlock.cs, SongState.cs (after M0), IslandDrag.cs, DeaCube/IslandGhost.cs (incl. Drop/Snapshot/Place), DeaCube/Route.cs,
  DeaCube/Comet.cs, OrbitCamera.cs, SongGenerator.cs, DeaCube/MusicTheory.cs, DeaCube/MeshFactory.cs, ProjectConfig.cs, DeaCube/ColumnBands.cs,
  Tests/V3ChecksB.cs, Tests/V4ChecksK.cs.
- R: AudioCube.cs, PathManager.cs, GlobalClock.cs, DeaCube/VoiceRules.cs, TileInteraction.cs, DeaCube/Rhythm.cs, SequenceMaster.cs,
  DeaCube/CubeSmear.cs, Resources/Shaders/DeaCube_Smear.shader,
  DeaCube/CubeOutline.cs, DeaCube/FocusLoop.cs, Resources/Shaders/DeaCube_Hologram.shader, Tests/V2Checks.cs, Tests/WpAChecks.cs, Tests/V4ChecksR.cs.
- U1: UIManager.cs, DeaCube/UIKit.cs, DeaCube/Comic/*, DeaCube/IconFactory.cs, InterfaceController.cs,
  DeaCube/Menu/TutorialBubble.cs, DeaCube/Onboarding.cs, DeaCube/IslandHeader.cs, DeaCube/CursorKit.cs, DeaCube/Palette.cs, DeaCube/Look/BounceGlyphs.cs,
  DeaCube/IslandTray.cs (the deck: visuals + input; placement via IslandGhost.Snapshot/Place), DeaCube/Ink/* (the kit),
  Tests/V3ChecksD.cs, Tests/V4ChecksU1.cs.
- U2: DeaCube/CubeInspector.cs, DeaCube/PathGridView.cs, DeaCube/InspectorCard.cs, DeaCube/RhythmStrip.cs, DeaCube/DurationPicker.cs,
  Tests/V3ChecksA.cs, Tests/V4ChecksU2.cs.
- P: DeaCube/Presenter.cs, DeaCube/Present/*, Resources/Shaders/DeaCube_Present*.shader, Tests/V3ChecksC.cs, Tests/V4ChecksP.cs.
- T: DeaCube/MainMenu.cs, DeaCube/Menu/MenuStage.cs, DeaCube/Menu/MenuMusic.cs, DeaCube/Menu/MenuSprites.cs, DeaCube/Menu/MenuButton.cs,
  DeaCube/Menu/Resources/*, Tests/V4ChecksT.cs.
- Frozen (orchestrator only): DeaCubeBoot.cs, WorldInput.cs, Hints.cs,
  Fx.cs, Look/* (except BounceGlyphs), Performance.cs, Audio/*, ProceduralAudio.cs, Ease.cs, existing shaders, Tests/V3ChecksE.cs,
  Tests/V3Integration.cs, Tests/V3Fixes.cs.

## 9. Acceptance (integration, orchestrator)
All suites green (V2/V3/V4 + V3Fixes + V3Integration + a new V4Integration: menu → new song → draw a hologram path with mixed lengths →
add an island below → duplicate right → register down → drag within column → inspect → edit rhythm in the strip → present → menu →
continue); console clean; Synth late/errors 0; user save md5 ef5c7a1e0808fe6fda3efb7b81befa3f unchanged; captures reviewed by eye.
