# DeaCube v7 — design spec (the build plan the v7 packages were built from)

Project: AudioCube_Unity (Unity 6000.4 URP). v6 is built, uncommitted and green (V6Suites.RunAll: every suite since v3 in one Play session,
1117 checks; the one late-accounting fail fixed and re-verified). v6 spec: docs/deacube-v6-spec.md (§11: drum Moons per song part);
v5: docs/deacube-v5-spec.md; UI language: docs/deacube-v4-ui.md (Melatonin × Spider-Verse ink: cream paper, ink outlines, drawn on twos,
lowercase captions, ICONS FIRST, words minimal — captions / tutorial sentences / menu words / digits are allowed).

## 0. The user's asks (verbatim, 2026-09-30, with a screen recording) and what the recording plays

The user sent a screen recording (an Instagram reel: a piano cover of the J-pop song "ループザルーム" / Loop the Room) and wrote:
1. "like in the screen recording, the part with 4 notes going downwards (like 5 seconds in) maybe allow some ui that allows the cubes to fall
   downwards like a stair for each note instead (so maybe some stair-case grid that just is descending notes? probably different tpyes too that
   of course fit within the genre)"
2. "and for higher notes (like chorus where it repeats the melody in a higher/lower octave) -> like 11 seconds in to 15 seconds in, maybe like
   some other cubes above if higher and below if lower octave phase in and plays it (make sure this looks good). add this as an option to copy
   and paste grids basically (like create an echo, higher or lower of a melodic voice"
3. "moreover for a given cube, there should also be controls to duplicate that cube down an octave or up an octave, simple UI. OR, considering
   the notes that the person has down, even like a support harmony of that cube (5ths or 4ths basically)"
4. "repeating same grid. like 37-41 notice how it's basically repeating the same notes with some differences, make it like the grid is
   repeating itself and time unwinding to the beginning everytime"
5. "same grid going on over and over again (like the grid extending and being longer and the cubes just going from left to right through the
   grid) like when the song is at 23.5 seconds, the harmony basically repeats and sounds like its flowing (analyze audio notes)"
6. "cube getting flungs as a transition animation (33- 35) where the cubes play and then from their last location, get flung upwards and to
   the right to the next grid"
7. "since there's the addition of heights, grids should abide by the height. for example if there was a staircase up then grids spawn in line
   with the top of the staircase, same for staircase down, etc"
8. "think of other ideas too - just want to introduce musical ideas and make it intuitive and magical for non-music theory producers with
   shortcuts and stuff"
(a "grid" = an island; "cube" = a cube and its path; "the notes that the person has down" = the other cubes' notes.)

What the recording plays (the orchestrator transcribed the reel's audio with basic-pitch; ≈ 162 bpm, one chord every 2 beats). The whole cover is
ONE 8-chord loop played ten times — Fm7 → B♭7 → E♭maj7 → A♭maj7 → Dm7♭5 → G7 → Cm7 → C7(♭9) (the circle of fifths in E♭ / C minor) —
and every idea the user points at is a way of varying a loop:
- ~5 s (ask 1): over C7(♭9) the right hand falls E5 → C♯5 → B♭4 → G4 in eighths (minor-third steps: the diminished arpeggio of the ♭9 chord)
  and lands on the next chord (Fm). A LEAD-IN RUN that steps down into the next chord; it returns in most loops (10.9 s, 52.6 s).
- 11–15 s (ask 2): the third loop replays the melody of the first two displaced by octaves (up to C6 / B♭5, down to F4 / E♭4); the chorus
  (35–47 s) is the verse melody an octave higher; single notes are answered an eighth later one or two octaves lower (C5 → C4 at 2.4 s,
  D6 → D4 at 36.2 s and 42.1 s): an OCTAVE ECHO.
- 23.5–33 s (ask 5): an ostinato. The right hand repeats one eighth-note cell (a low chord tone + G4 | F4 | G4 | F4) over EVERY chord of the
  loop. The top G4 never moves (a pedal: G fits all eight chords as a chord tone or a colour); only the low note follows the chords. ONE
  PATTERN FLOWING THROUGH CHANGING HARMONY, moving only what has to move.
- 33–35.4 s (ask 6): the turnaround G7 → A → D♭7 → C7 slows down, and at 35.4 s the chorus bursts in an octave higher
  (E♭5 → A♭5 → C6). A LAUNCH into a higher section.
- 35–47 s (ask 4): loops 7 and 8 are the same melody with small differences (a note dropped, an octave echo added, a pickup added, one note
  an octave lower). THE SAME GRID REPLAYED WITH VARIATIONS.
The recording is a reference for devices only. Never copy its melody into the game or its gallery.

## 1. Packages (parallel, one working tree, one Unity editor — protocol: build notes: PROTOCOL.md)

| pkg | what |
|---|---|
| K | islands & timeline: the STAIRS island (build, pitches, runner paths, layout, ops), GROUND levels (heights after stairs: islands, Moons, camera), REWIND repeat style (no belt) + the pass index, FLOW windows, LAUNCH plumbing, save / load |
| A | audio: layers and bends in every pitch path (song, previews, presses), stairs tiles, the launch riser + landing crash, harmonic-safety rules, performance |
| H | cubes: octave copies / echoes (layers), harmony cubes (bends), flip, paste modes (echo ↑ / ↓, answer), stairs runners; the cube motions: the layer ride and phase-in, the stair fall, the rewind zip, the flow glide, the fling; varied passes and flow tiles (TileAt) |
| W | world magic: the stairs look, terraces (the heights), echo layers (sky / deep) + links, time unwinding (rewind), the flow ribbon (the grid extending), the fling (launch + landing), the flip mirror |
| U1 | islands UI: the stairs card and the stairs header (type / direction / steps / rate / lead-in), repeat style + vary on the header, the flow and launch buttons, the rail (stairs, heights, flow, launch), tutorial tips |
| U2 | cube UI and shortcuts: the cube card's octave ↑ / ↓ copies, harmony ↑ / ↓ (with a preview), flip, the paste fan (plain / echo ↑ / echo ↓ / answer, + "late"), the clipboard chip's fan, hotkeys, the shortcut sheet (?) |

## 2. Shared contract (M0 — landed by the orchestrator; compiles in Unity; keep every public member; you may add members)

2.1 Save format (SongState v6). `SongState.CurrentVersion = 6`. Old files load unchanged (every new field at its default).
- `MeasureState` / `SongManager.MeasureData` / `KeyBlock` (every copy / round trip done: From, Clone, ToData, CopyData, BuildFromData, KeyBlock.ToData):
  `kind` 3 = STAIRS; `stairType` (0..5, see 2.4), `stairDir` (−1 down / +1 up; 0 reads as −1), `stairSteps` (3..8, default 4), `stairRate`
  (ticks per step: 6 sixteenth, 8 triplet eighth, 12 eighth, 24 quarter; default 12), `stairLead` (true, default: the run ENDS at its column's
  end — a lead-in into the next chord; false: it starts at the column's start); `carryStyle` (0 hop = v6 carry, 1 flow); `rewind` (bool: the
  repeat style — false the v5 belt, true time unwinding); `vary` (0 same, 1 vary, 2 answer); `launch` (bool).
- `CubeState` / `AudioCube` (ToState / ApplySettings / PathManager.Aligned done): `layer` (−2..+2, default 0: the octave layer), `bend` (int[] per
  node, semitones added to the tile's pitch; null = none; kept aligned with the nodes like durs), `echoOf` (the source cube's id for an octave
  copy / echo / harmony cube; −1 none).
- `AudioCube.Window`: `pass` (the pass of its column this window plays: 0.. for home windows; for carried windows the target column's pass),
  `flow` (a carried window of a flow-style island).
- `KeyBlock.IsStairs` (kind 3), `KeyBlock.GroundY` (the ground shown now; M0: 0), `AudioCube.IsRunner` (a cube on a stairs island),
  `AudioCube.BendAt(node)` (0 when none), `AudioCube.IsLayered` (layer ≠ 0).

2.2 Constants (ProjectConfig): `StairMinSteps` 3, `StairMaxSteps` 8, `StairStepRise` 0.34 (world units per step), `GroundMin` −1.6, `GroundMax`
4.8, `GroundGlideSeconds` 0.6, `RewindBeats` 0.5, `FlingBeats` 0.75, `LayerRise` 2.3, `LayerDrop` 1.6, `LayerBack` 0.55, `LayerFront` 0.9,
`MaxLayer` 2.

2.3 Harmony (DeaCube/Harmony.cs — REAL, unit-tested; orchestrator-owned: ask for changes via requests). New pure functions:
- `Stairs(type, dir, steps, rootMidi, semis, nextRoot, nextSemis, tonic, minor, topHint, lo, hi)` → int[steps], strictly falling (dir −1) or
  rising (+1). Each type draws from its POOL of pitches: 0 chord = the chord's tones · 1 scale = the key's scale · 2 spark = the diminished
  seventh on the chord's 3rd when the chord is a dominant (the ♭9 fall of the recording), else the chord's tones · 3 slide = chromatic ·
  4 bright = the key's major pentatonic (a minor key: its relative major's) · 5 walk = the key's scale (a bass walk; the caller passes a bass
  range). The run LEADS INTO the next chord: it approaches a chord tone L of the next chord from above (down) / below (up), its last step is the
  pool pitch next to L, and L is chosen so the first step sits nearest `topHint`, everything inside [lo, hi]. `StairTypeWord(t)` ("chord",
  "scale", "spark", "slide", "bright", "walk"), `StairFit(type, semis)` 0..1 (spark 1 on dominants, 0.3 elsewhere; chord 0.9; scale 0.8;
  slide 0.6; bright 0.75; walk 0.8: the stairs header's star goes to the best).
- `Harmonize(src, w, rootMidi, semis, tonic, minor, others, above, lo, hi)` → int[] (−1 stays −1 = a rest): a SUPPORT VOICE — per note the
  perfect interval first (above: +7 then +5; below: −5 then −7), then the sixths / thirds (±8, ±9, ±3, ±4); scored by NoteScore (chord tone 1,
  key tension 0.55, avoid 0), minus a clash against `others[i]` (the other pitches sounding at that note: a minor second / major seventh /
  minor ninth to any of them), minus leaps from the previous harmony note, plus a bonus for keeping the previous note's interval (parallel
  motion reads as one voice). "5ths or 4ths basically": perfect intervals win whenever they fit.
- `AdaptPedal(src, w, tgtRoot, tgtSemis, tonic, minor, pool)` → int[]: a note that is in `pool` and fits the target chord (NoteScore ≥
  TensionScore) STAYS; any other note moves to the nearest pool pitch that fits (ties: the smaller move, then down). pool null = any pitch.
  The flow: "the harmony basically repeats and sounds like its flowing" — a pedal holds, only what has to move moves.
- `Vary(src, w, pass, passes, seed, amount, rootMidi, semis, tonic, minor, pool)` → int[]: pass 0 or amount 0 → src; amount ≥ 1 → deterministic by
  (seed, pass): one or two light notes change (never the first; prefer short / off-beat / repeated notes): an octave flip (±12, in pool) or a
  neighbour (the next pool pitch up / down that fits the chord); amount 2 on the LAST pass → the final note also resolves (Answer).
- `Answer(src, w, rootMidi, semis, pool)` → int[]: the last sounding note moves to the nearest root / 3rd of the chord in pool (a question gets
  its answer).
- Unity helper: `ChordAfter(KeyBlock kb, out root, out semis)` (the next column's chord — at the last column the first column's — else the
  key's home chord), `Pool(KeyBlock kb)` (the island's playable pitches, sorted, register included).

2.4 Stairs island (K builds; H runners; A sound; W look; U1 card + header). `KeyBlock.IsStairs` (kind 3): ONE row of `stairSteps` step tiles
(gridX = step index, gridZ 0) spanning KeyBlock.IslandWidth (columns stay aligned); step i's top sits at −i × StairStepRise (down) or
(i + 1) × StairStepRise (up) relative to the island's ground; tile.midi = StairPitches(kb)[i]. Stairs never carry / flow / launch / merge.
- `SongManager.AddStairIsland(int island, bool above, int instrument = -1)` — like AddKeyboardIsland (into that island's column, else a new column
  after the last) + ONE RUNNER cube of `instrument` (−1: the lead group) so it sounds at once; one History entry; RiseIn. Stub in M0 → K.
- `SongManager.SetStair(int at, int type, int dir, int steps, int rate, bool lead)` (−1 / 0 keep a value; one History entry; rebuild: new tiles,
  every runner's path re-derived, grounds re-computed). Stub → K.
- `SongManager.StairPitches(KeyBlock kb)` → Harmony.Stairs over the column's chord (ChordOfColumn / Harmony.ChordOf) into Harmony.ChordAfter,
  topHint = the last note sounding before the run on the column's other islands (else the chord's 5th in 67..79), range 55..88 (walk 36..57),
  + 12 × register. M0: working.
- `SongManager.StairPath(KeyBlock kb, out int[] xs, out int[] mods, out int[] durs)` — the runner's path, exactly one pass long: lead → a rest
  on step 0 for (pass ticks − steps × rate) then steps 0..n−1 at `rate` (the rest dropped when it would be ≤ 0; steps that do not fit
  dropped from the front); no lead → steps 0..n−1 at `rate`, the last step holding to the pass end. M0: working.
- A cube on a stairs island is a RUNNER (`AudioCube.IsRunner`): its path is always StairPath (drawing on stairs places a runner: H).

2.5 Ground — "grids should abide by the height" (K). `SongManager.GroundOf(int col)` = the column's target ground height (world units):
g(0) = 0; g(c + 1) = clamp(g(c) + rise(c), GroundMin, GroundMax), rise(c) = the first stairs island of column c (lane order): dir × steps ×
StairStepRise (a down stair of 4 lands the next column 1.36 lower; an up stair of 4 lifts it 1.36 — "grids spawn in line with the top of
the staircase"). `KeyBlock.GroundY` = the ground shown now (glides GroundGlideSeconds when the target changes), `SongManager.OnGroundsChanged`
(event). Every island's y = its GroundY + the v6 tower lift + belt / slide as today; Moons ride the ground of the column they are in line with;
the camera frames heights. M0: GroundOf working, GroundY = 0 (K applies it).

2.6 Repeat style & vary (K timeline; H motion + tiles; W look; U1 header).
- `KeyBlock.rewind`: no belt (the island stays home; its column needs no belt width) — at every pass boundary TIME UNWINDS.
  `SongManager.SetRewind(int at, bool on)` (one History entry; `OnRepeatStyleChanged(KeyBlock)`). `SongManager.RewindPhase(KeyBlock kb, double
  beat, out int pass)` → −1 when no unwind is running, else 0..1 through the unwind at the end of pass `pass` (every pass but the last): the
  last RewindBeats of that pass. Pure. Stub → K.
- `KeyBlock.vary`, `SongManager.SetVary(int at, int v)` (0..2, one History entry). A window with pass ≥ 1 on an island with vary ≥ 1 plays the
  VARIED tiles: `AudioCube.TileAt` (H) = Harmony.Vary over the island's Pool → the nearest tiles (cached per pass).
- `AudioCube.Window.pass` (K fills it for every window).

2.7 Flow — "the grid extending and being longer and the cubes just going from left to right through the grid" (K windows; H tiles + glide;
W ribbon; U1 buttons). `KeyBlock.carryStyle` 1 = flow: the island's pattern continues over the next `carry` chords like a v6 carry, but ADAPTED
BY PEDAL (Harmony.AdaptPedal, chained hop k from hop k − 1) and the cube GLIDES left → right between islands (Magic.FlowPoint) instead of arcing.
`SongManager.SetCarry(int at, int n, int style)` (style −1 keeps; one History entry; OnCarryChanged). Flow windows = carried windows with
`flow = true` (K).

2.8 Launch — "the cubes play and then from their last location, get flung upwards and to the right to the next grid" (K plumbing; H flight;
W look; A riser; U1 toggle). `KeyBlock.launch`; `SongManager.SetLaunch(int at, bool on)` (one History entry; `OnLaunchChanged(KeyBlock)`);
`SongManager.LaunchTarget(KeyBlock kb)` (the next column's island nearest in z, chord islands first; at the last column the first column's when
the song loops, else null); `SongManager.TurnEnd(KeyBlock kb)` (the song beat its last pass ends = the landing beat). M0: working (K refines).

2.9 Echo layers, harmony, flip, paste modes (H implements; U2 buttons; W look; A sound).
- New `DeaCube/CubeOps.cs` (H owns): `OctaveCopy(AudioCube c, int dir, int delayTicks = 0)` → the new cube (layer = c.layer + dir, clamped to
  ±MaxLayer; echoOf = c.id), `Harmonize(AudioCube c, bool above)` → the new cube (bends), `PreviewHarmony(AudioCube c, bool above)` → the
  pitches (and ghosts + an audition while previewing), `Flip(AudioCube c)` → c. Each but the preview = one History entry. Stubs → H.
- `Clipboard.PasteMode { Plain, EchoUp, EchoDown, Answer }`, `Clipboard.PasteOn(KeyBlock kb, PasteMode mode)`, `Clipboard.PasteNext(PasteMode
  mode)`, `Clipboard.CanPasteOn(KeyBlock kb, PasteMode mode)` (echo modes may target the source's own island: a doubling), `Clipboard.EchoLate`
  (bool: echoes land an eighth late — "late"). The v6 overloads = Plain. Stubs → H.
- `Magic.LayerOffset(int layer)` (up + back for layer > 0, down + toward the camera for layer < 0: layer × LayerRise / LayerBack, LayerDrop /
  LayerFront — real), `Magic.FlowPoint(a, b, t)` (a straight glide at platform height with a small lift over the gap — real),
  `Magic.FlingPoint(a, b, t)` (a ballistic arc up and to the right, apex 2.4 + 0.25 × distance above the higher end — real).
- Pitch rule (A): every pitched note of a cube sounds tile.midi + Transpose + 12 × (octave + layer) + bend[node] (VoiceRules.PitchOf in M0;
  A: every preview / audition / press, the folds — an octave copy must SOUND an octave away).

## 3. K — islands & timeline

3.1 Stairs island (kind 3) — "a stair-case grid that just is descending notes". Build (KeyBlock.Build for kind 3): the steps are the tiles
(`stairSteps` blocks of width IslandWidth / steps in a row, each one a solid block from its top down to the stair's base: a staircase you can
read from the default camera), ink-stone colours (not a vibe colour; W adds the look), one row deep (≈ DepthOf(1)), step colliders so the
hand picks steps. tile.midi from StairPitches (refresh when the column's chord / the next chord / the key / the type / direction / steps / the
register change — like the keyboard's chord-tone dots). Layout: a lane of its column like a keyboard (MaxLanes, lane gaps, NormalizeColumns,
relayout, drag, never merges); ops: register, repeat, sleep, duplicate, delete (not carry / flow / launch / vary). AddStairIsland + SetStair
(2.4); a column of only stairs is a column like a keyboard-only column (its chord = the previous column's chord island).
3.2 Runners: AddStairIsland creates the runner cube (SongState: a CubeState on the new measure with StairPath); SetStair re-derives every
runner's path (xs, mods, durs) in the same rebuild; a runner's repeat / sleep follow the island.
3.3 Ground (2.5): GroundOf; KeyBlock.GroundY glides (ease in-out, GroundGlideSeconds) whenever the target changes (a stair added / removed /
changed, a column moved, undo, a load: a load snaps); islands, keyboards, stairs, Moons (the ground of the column they are in line with,
gliding with their follow), belts, the Route cable, the comet, the header, the hub gauge all follow (they read transforms / LiftY). The camera
(OrbitCamera) frames the lit column including its height; "frame all" includes heights.
3.4 Rewind style (2.6): rewind islands have no belt and no ride (the column's layout width has no belt); windows unchanged; RewindPhase.
3.5 Windows: `pass` on every window; flow windows (carried + flow); SetCarry(at, n, style).
3.6 Launch (2.8): SetLaunch, LaunchTarget, TurnEnd (refine the M0 versions).
3.7 Save / load of every new field; the fixture and every gallery song load exactly as in v6 (ground 0, no stairs).
3.8 Tests: Tests/V7ChecksK.cs (RunAll → Captures/k7_report.txt; captures k7_*.png): stairs build (steps, pitches = StairPitches, monotone,
heights), add in column / as a new column (+ runner, one History entry), SetStair (tiles + runner path + grounds), grounds (a down stair of 4
lowers the next columns by 1.36, clamps, glides, a load snaps), Moons + camera follow heights, rewind (no belt, RewindPhase samples, pure),
pass indices, flow windows, launch target / turn end, save / load round trip of every field, older K suites green (update only assertions whose
meaning changed and say so).

## 4. A — audio

4.1 Pitch: layer and bend in VoiceRules.PitchOf (M0) AND every preview / audition / press / hover path (a harmony cube's node previews with its
bend; an octave copy previews in its octave). The folds must not undo a layer: an octave copy must SOUND an octave (two octaves) away from its
source (fold the base pitch, then add the layer, clamp 21..108).
4.2 Stairs tiles play their pitch (FoldMelody like keyboards: a run keeps its octave).
4.3 Harmonic safety (HarmonicCheck and the tests' chord-tone rules): bent notes and stairs tiles are exempt (Harmony chose them to fit the key).
4.4 The launch — a production trick every pop transition uses: a RISER (the GeneralUser GS "Reverse Cymbal" preset on a spare slot / channel of
the song bus) scheduled so its swell PEAKS on the landing beat (SongManager.TurnEnd of the launching island) + a soft crash on the landing
(VoiceRules.DispatchCrash), once per launch, only when the island is audible (not asleep, not muted), never late; measure the preset's peak
time (render it) and schedule from it.
4.5 Performance: a dense song with 4 octave layers and 2 harmony cubes playing: audio CPU (Synth.Stats), voices, late 0 — report the numbers.
4.6 Tests: Tests/V7ChecksA.cs (+ V6ChecksA, V2Checks, WpAChecks green): layer pitches (+12 / −12 / +24 exactly, after the fold), bends,
previews with bends, stairs pitches, harmonic check exemptions, the riser (peak within ±30 ms of the landing in a WAV), the crash, perf, Synth
late / errors 0.

## 5. H — cubes

5.1 CubeOps (2.9):
- OctaveCopy — "duplicate that cube down an octave or up an octave": a new cube, same island, path, durs, mods, instrument, voice and settings,
  layer = source layer + dir (clamped; at the limit: nothing, a soft deny), echoOf = the source's id, a new id / seed; delayTicks > 0 (an echo
  "late") = a leading rest of delayTicks on the first tile and the last note shortened by it (dropped when shorter). It materialises with the
  phase-in (W's shimmer) and plays at once when the song is playing. Layer cubes never occupy tiles (SequenceMaster.Occupy: they live in their
  layer; no stacking with the grid's cubes).
- Harmonize — "a support harmony of that cube (5ths or 4ths basically)", "considering the notes that the person has down": src = the cube's
  sounding pitches (rests −1), others[i] = every other pitched note sounding on the island at node i's time (the other residents' notes + the
  island's chord tones), w = Harmony.WeightsOf, range 36..96 → Harmony.Harmonize → per note the tile of that exact pitch when the island has
  it, else the nearest tile + bend = pitch − tile.midi; echoOf = the source's id; one History entry. Keyboards: the key of that pitch (no bend).
- Flip — the path upside down (a melodic mirror — "an instant answer"): chord grid x → cols − 1 − x, z → rows − 1 − z; keyboard key → (lowest
  + highest of the path) − key; in place, one History entry.
- PreviewHarmony: ghost beads of the harmony on the tiles + an audition of the source and the harmony together (preview bus).
5.2 Paste modes (2.9): EchoUp / EchoDown = the v6 adapted pattern with layer = the source's layer ± 1 (the source's own island: a doubling —
no adaptation); Answer = the adapted pattern with Harmony.Answer applied (then to tiles); Clipboard.EchoLate adds the eighth delay; the v6
paste flight for each (echoes fly up / down into their layer).
5.3 Stairs: with a cube in the hand a click on any step places a RUNNER of that group (the whole run, SongManager.StairPath; one per group per
stairs: the same group again replaces it); the empty hand presses a step (loud, like a key). Runners refresh when K re-derives the path.
5.4 Motions — pure functions of the song beat (seeks / loops land right; audio timing is never touched):
- Layer ride: a layer cube rides at its tile's pose + Magic.LayerOffset(layer); faint glass at rest (alpha ≈ 0.3), it PHASES IN (solid, glowing)
  ≈ 0.12 beat before each of its notes and back out after the note ("some other cubes above ... below ... phase in and plays it").
- Harmony cubes hop on their own tiles like any cube (a duet in parallel); a bent node tilts the body a little toward the bend.
- Stair fall — "the cubes fall downwards like a stair for each note": a runner on a down stair DROPS from step to step (a short fall under
  gravity, a squash on landing, a small bounce) on each note; on an up stair it hops up each step; after the last step it keeps going onto the
  next column's ground on the downbeat, then fades home like a fling.
- Rewind — "the grid is repeating itself and time unwinding to the beginning everytime": during RewindPhase the body zips BACKWARD through
  its path (last node → first, slow then fast) and sits on its first tile at the pass boundary.
- Flow glide: between two flow windows on different islands the body glides along Magic.FlowPoint (constant speed, no arc), arriving on the
  next window's first note; home after the last flow window the same way.
- Fling: on a launching island, in its last pass, after the cube's last note (onset + min(its length, 0.5 beat)) the body is flung along
  Magic.FlingPoint (up and to the right, one or two spins, a squash on launch) to LaunchTarget's centre (carried / flow cubes: their next
  window's first tile), landing at TurnEnd (the flight compresses when the last note ends later); on landing Magic.Land; then the body
  dissolves and re-forms at home (≈ 0.3 s). Stair runners fling from their last step.
5.5 TileAt: varied passes (2.6) and flow windows (2.7), cached like the v6 hop tiles.
5.6 Tests: Tests/V7ChecksH.cs (+ V6ChecksH, V5ChecksR, V4ChecksR green): octave copies (layer, pitch via A's rule, no occupancy, History),
echo late (the rest + shortened last note), harmonize (pitches = Harmony's answer for 3 cases incl. a clash avoided; tiles / bends), flip, paste
modes (echo up / down / answer / late), stairs runners (placement, path = StairPath, replace), the motions sampled at beats (layer offset,
phase-in alpha, stair fall heights per step, the rewind zip at its middle, the flow glide between islands, the fling's apex and landing at
TurnEnd), varied / flow tiles, Synth late / errors 0.

## 6. W — world magic (look)

6.1 Stairs look: the steps as ink-outlined stone blocks (a staircase you read at a glance, family of the tower's stone), each step lights and
puffs dust when a runner lands on it, the run's direction glyph (↓ / ↑) on the top step's riser; the runner's fall wears short speed lines.
6.2 Terraces — the heights made visible: an island whose ground ≠ 0 stands on a TERRACE (a stone column / mesa from the sea (y −4) up under its
platform, ink bands every StairStepRise level, foam at the waterline); a lowered ground: a low rock shelf; it grows / shrinks as GroundY glides.
Stairs and the terraces after them read as one landscape: the song climbs and falls.
6.3 Echo layers — "make sure this looks good": the SKY layer = a faint glass copy of the island's tile grid floating at LayerOffset(+1)
(only while layer cubes live there), thin threads of light down to the tiles it mirrors; the DEEP layer = the same below the island, seen
through a shimmering water-glass pane, a few rising bubbles; a phase-in sparkle as a layer cube's note begins; a fine link thread between an
echo / harmony cube and its source (echoOf) that pulses when both sound.
6.4 Rewind — time unwinding: a clock spiral above the hub spinning backward, the path's beads light in REVERSE order (last → first) with
streaks, the island's fill gauge drains back, a brief sepia / negative flash across the platform at the boundary; recognisable, ≤ RewindBeats.
Varied notes (vary ≥ 1) wear a small sparkle on their tile when they play ("the differences" pop).
6.5 The flow ribbon — "the grid extending and being longer": a ribbon of translucent tiles in the source's colour grows from the source's
right edge to each flow target (bridging the gaps at platform height, bending in z when the target sits in another lane), the targets wear the
source's colour rim so the whole run reads as ONE long grid, and while it plays a light sweeps left → right along it at the song's pace.
The spell when flow is set: the ribbon UNROLLS like a carpet (≈ 0.6 s); unset: it rolls back.
6.6 The fling: the launching island's platform flexes (a dip, then a kick) as its cubes leave; speed lines + a spin trail; the landing burst +
a ring on the target; the target's tiles flash in a quick left → right wave (charged).
6.7 Flip: a mirror plane sweeps across the island as the path flips.
6.8 Components: new DeaCube/Stairs look files as you see fit (e.g. Terraces.cs, EchoLayers.cs, RewindFx.cs, FlowRibbon.cs, FlingFx.cs) run by
WorldMagic, following SongManager events (OnSongRebuilt, OnColumnsChanged, OnCarryChanged, OnGroundsChanged, OnRepeatStyleChanged,
OnLaunchChanged). K's files are not yours.
6.9 Tests: Tests/V7ChecksW.cs (+ V6ChecksW green): captures w7_*.png of every look (stairs + a fall, terraces up / down, sky + deep layers at rest
and phased in, a rewind mid-unwind, a flow ribbon at rest / mid-sweep / mid-spell, a fling mid-flight and landing, the flip) + numbers
(terrace top = platform underside ± 0.1, layer planes only where layer cubes live, ribbon segments = flow targets), frame time ≤ +1 ms per
effect, no errors.

## 7. U1 — islands UI

7.1 The tray's STAIRS card (a little staircase): click adds a stairs island to the focused island's column (AddStairIsland with the hand's
group, else the lead group); drag places it like the keyboard card.
7.2 The stairs header: grip, eye, TYPE (a fan of six glyphs — chord / scale / spark / slide / bright / walk; hover plays that run, click picks;
a star on the type that fits the chord best, Harmony.StairFit), DIRECTION ↓ / ↑, STEPS − n +, RATE (the note-length language: sizes, not note
symbols), LEAD-IN (the run ends at the chord change / starts with it), register, repeat, duplicate, more.
7.3 Every grid's header: the repeat button + (repeat ≥ 2) the STYLE toggle (belt ⟷ rewind ⟲) and the VARY dice (same / vary / answer);
a FLOW button (≈ waves, cycles 0..3 in flow style) beside the carry wand (setting one sets the style); a LAUNCH toggle (a spring arrow ↗).
Captions: "rewind", "vary each repeat", "flow into next chords", "fling into the next grid".
7.4 The rail: stairs beads (a tiny step glyph, ↓ / ↑), the ground heights (the beads sit higher / lower with GroundOf: the rail becomes a gentle
landscape), flow (a wavy line under the beads it covers), launch (a small ↗ after the bead).
7.5 Tutorial / tips (one light tip each, v4 voice): stairs, heights, echoes, harmony, rewind / vary, flow, launch, flip, the shortcut sheet.
7.6 Tests: Tests/V7ChecksU1.cs + V6ChecksU1, V5ChecksU, V4ChecksU1, V3ChecksD green.

## 8. U2 — cube UI & shortcuts

8.1 The cube card (InspectorCard / CubeInspector): OCTAVE COPY ↑ / ↓ (a cube with a ghost above / below), HARMONY ↑ / ↓ (a cube with a smaller
partner; hover = CubeOps.PreviewHarmony: ghost beads + both voices), FLIP (↕), and the paste fan on "paste → next grid": plain / echo ↑ /
echo ↓ / answer, with a "late" toggle for echoes — icons first, captions on hover.
8.2 The clipboard chip: click = plain paste mode (as v6); a press-and-hold / right-click opens the same fan.
8.3 Hotkeys (UIManager; check every existing binding first and keep them — pick free keys, list them in your report): Shift+↑ / Shift+↓ octave
copy of the selected cube; harmony above / below; flip; paste as echo ↑ / echo ↓ / answer; launch on the focused island; `?` opens the
SHORTCUT SHEET (new Ink/Hud/HudShortcuts.cs): a cream card listing every shortcut of the game (key caps + glyphs + one-word captions), Esc /
? closes it.
8.4 Tests: Tests/V7ChecksU2.cs + V6ChecksU2, V4ChecksU2 green.

## 9. Ownership (edit only your files; read anything)

| pkg | files |
|---|---|
| K | SongManager.cs, KeyBlock.cs, ProjectConfig.cs, OrbitCamera.cs, DeaCube/IslandDrag.cs, IslandGhost.cs, ColumnBands.cs, Route.cs, Comet.cs, MeshFactory.cs, Belt.cs, Tests/V7ChecksK.cs, Tests/V6ChecksK.cs, Tests/V5ChecksK.cs, Tests/V4ChecksK.cs, Tests/V3ChecksB.cs |
| A | DeaCube/Audio/**, DeaCube/VoiceRules.cs, DeaCube/Palette.cs, GlobalClock.cs, SequenceMaster.cs, Tests/V7ChecksA.cs, Tests/V6ChecksA.cs, Tests/V2Checks.cs, Tests/WpAChecks.cs |
| H | PathManager.cs, TileInteraction.cs, AudioCube.cs, DeaCube/Clipboard.cs, DeaCube/CubeOps.cs, MelodyLine.cs, PathGridView.cs, DurationPicker.cs, FocusLoop.cs, CubeOutline.cs, Tests/V7ChecksH.cs, Tests/V6ChecksH.cs, Tests/V5ChecksR.cs, Tests/V4ChecksR.cs |
| W | DeaCube/Magic.cs, DeaCube/Fx.cs, DeaCube/WorldMagic.cs, DeaCube/Tower.cs, DeaCube/CarryBridge.cs, DeaCube/Look/**, new v7 look files, Tests/V7ChecksW.cs, Tests/V6ChecksW.cs |
| U1 | DeaCube/IslandTray.cs, IslandHeader.cs, Ink/Hud/HudColumnRail.cs, Vibe.cs, VibeGlyphs.cs, JobBadge.cs, Onboarding.cs, Hints.cs, Menu/TutorialBubble.cs, Tests/V7ChecksU1.cs, Tests/V6ChecksU1.cs, Tests/V5ChecksU.cs, Tests/V4ChecksU1.cs, Tests/V3ChecksD.cs |
| U2 | Ink/Hud/HudInstruments.cs, HudKit.cs, HudTransport.cs, HudMenuStrip.cs, HudPresent.cs, HudClipboard.cs, new Ink/Hud/HudShortcuts.cs, DeaCube/CursorKit.cs, InspectorCard.cs, CubeInspector.cs, UIManager.cs, InterfaceController.cs, Tests/V7ChecksU2.cs, Tests/V6ChecksU2.cs, Tests/V4ChecksU2.cs |
| orchestrator | everything else (Harmony.cs, SongState.cs, the menu / gallery / presentation files, V7Integration / V7Suites, older suites not listed, README, docs) |

A needed change in a file you do not own → a request note to the integrator.

## 10. Acceptance (orchestrator, at the end)

Every older suite green (pre-v6 suites with AutoHand), every V7Checks* green, V7Integration: a fresh song → add a stairs island (a spark fall
into the next chord: pitches, the runner falls step by step, the next column's grids sit lower by the stair's rise) → an up stair (the grids
after it climb) → octave copies ↑ and ↓ of a melody cube (layers, pitches ±12, phase-in at its notes) → a harmony cube (4ths / 5ths where they
fit, a clash avoided, bends) → flip → copy + paste as echo ↑ onto the next grid (and "late") + an answer paste → repeat ×3 with rewind + vary
(the unwind at each boundary, varied notes on passes 2–3) → flow ×2 (pedal tiles, the glide, the ribbon) → launch (the fling lands on the
next downbeat, the riser peaks there) → the shortcut sheet → present → save / load round trip (every v7 field) → Synth late / errors 0; the
user's save md5 ef5c7a1e0808fe6fda3efb7b81befa3f unchanged.

---------------------------------------------------------------------------------------------------------------------------------------------
# PART II — added mid-build by the user (2026-09-30, in this order). §12–§17 OVERRIDE anything above that disagrees; §18 replaces §1 / §9 / §10.

## 12. Hold the end, return by moving (verbatim)
- "also, the conveyor belt should only go back after the song resets, not directly afterward. also the tower things that rise should rise like
  60% taller and also only go back after the song resets (not in sync, but slightly out of sync)"
- "also, whenever a grid ends, it shouldnt teleport back to its starting position, they should only do that when the song resets (or if the grid
  is playing again), and no teleportation, just movement."
"The song resets" = the loop point (the song loop or the focus loop wraps) or a stop. Everything below is a pure function of the song beat while
playing (seeks and loops land right); on a stop it is animated in real time (never a snap).
12.1 Cubes (H). After a cube's window ends it HOLDS where it ended (its last tile — or where a carried / flow chain / fling left it) — never the
v6 snap home (today AudioCube.SettleOn / ResetToStart set x / z at once). It goes back to its home tile ONLY (a) when its grid plays again —
the trip is a MOVEMENT (a hop-glide over the grid, ≤ ReturnBeats, timed to land exactly on the next window's first note; consecutive windows
(repeat passes, the belt) = a hop across the pass boundary like any next note; rewind islands = the v7 unwinding zip), or (b) when the song
resets — right after the reset every holding cube travels home (≈ 1 beat), each slightly out of sync (a stagger of 0..ResetStaggerBeats by a
hash of its id); a cube whose next window starts right at the reset (column 0) travels in the last ReturnBeats before it and lands on beat 0.
Stop: the same trip in real time (≈ 0.5 s, staggered), not a snap. Riders / carried / flow / fling cubes follow the same rule for their final
position.
12.2 Belts (K). After an island's last pass it STAYS at its last slot until the song resets, then glides back to slot 0 (slightly out of sync
with the other belts: the same stagger rule). Stop: glides back in real time.
12.3 Towers (K; W's pillar follows). ProjectConfig.TowerRise × 1.6 (2.0 → 3.2 world units per octave); after its turn a raised island STAYS UP
until the song resets, then sinks slowly (TowerSinkBeats), each tower with its own small delay (0..ResetStaggerBeats by a hash of the island):
"not in sync, but slightly out of sync". A lowered island likewise stays dipped until the reset. Stop: the towers sink (real time, staggered).
12.4 Moons (K). A Moon whose section ended HOLDS in line with the last column of its section until the song resets, then glides back to its
start column (staggered).
12.5 Tests: assertions of V5ChecksK / V6ChecksK / V6ChecksW / V6ChecksH / V5Integration / V6Integration that encode "goes back right after its
turn" change meaning — their owners update them and say so (the orchestrator owns V5Integration / V6Integration).

## 13. Sections — beats, measures, sections made visible (verbatim)
- "also i want to make really intuitive the idea of sections. like 4 beats in a measure, 4 measures in a "section" of a musical idea, etc. so
  bear this in mind when handlgin the ui during when the user is making their melody/harmony or making their section and make it really
  visual, with support if needed"
- "also, like if it's the same pattern repeated in different chords in one section it should be a 4-grid section where the 4-grids are
  together rather than separate"
13.1 Model (K). A SECTION = a run of consecutive columns (normally 4 measures). `SongState.sections` (int[]: the first column of every section,
ascending; column 0 always starts one). Older songs (null): one section per 4 measures (by bars), computed on load and saved from then on.
New columns (AddIslandAfter, a card dropped at the end, the melody growing past the end, …) join the section they land in while it has fewer
than 4 measures, else start a new one; a column inserted inside a section joins it. API (K, SongManager): `SectionCount`, `SectionOf(int col)`,
`SectionFirst(int s)`, `SectionLast(int s)`, `SectionBars(int s)` (measures), `SectionStartBeat(int s)`, `SectionEndBeat(int s)`,
`event OnSectionsChanged`. Ops (O, SongOps): split / join / duplicate / delete / move a section (§16).
13.2 Layout (K). Inside a section the columns TOUCH (edge to edge, no gap — the v3 glue, now by section); between sections a gap
(SectionGap = the old ColumnGap). A column's width follows its length: `bars × KeyBlock.IslandWidth` (a 2-bar chord island is two measures
wide — K builds the longer platform: the tile grid in its first measure, the second measure a quieter "held chord" surface with the chord's
colour; a keyboard spreads its keys; Moons unchanged). So song time maps to x (linearly inside a section).
13.3 The look (B builds; W adds light): every section stands on a SECTION PLINTH — one low ink-stone slab under all its columns and lanes, its
LETTER on the front-left corner (A, B, C … by first appearance; a duplicated section keeps its source's letter), the MEASURE NUMBERS 1 2 3 4
along its front edge (one per measure), four BEAT PIPS per measure (the playing beat's pip lights, the downbeat's is bigger), and while it plays
a soft playhead line sweeps left → right across the section. Every grid shows its own four beat pips on its front margin (the measure it is).
A section that has fewer than 4 measures shows faint "ghost measure" outlines where the rest would go (support: "a section is 4 measures").
13.4 Support (U1): a light first-time tip "4 beats = 1 measure · 4 measures = 1 section" when the first section fills (a small sparkle when a
section reaches 4 measures); while drawing a melody the length reads as measures ("1 measure", "2 measures", "½"); the rail groups its beads by
section (a bracket with the letter), with measure numbers. Sections are also the natural unit of the v7 features: a Moon (drums) per section,
a launch at a section's end into the next section, flow "through the section", heights per section.
13.5 "The same pattern repeated in different chords in one section = a 4-grid section, together": the grids of a section touch (13.2); a
pattern carried / flowed through the section's chords reads as ONE block (W's flow ribbon is the continuous colour band across the touching
grids); the grid header gets "fill the section" (flow to the section's last measure in one click, U1).

## 14. Melody phrases — any length, one grid, auto-growing (verbatim)
- "moreover, with melodic ideas you should also be able to control how long they last (like for example a 2 measure melody shouldnt be two
  separate grids, they should be together). same if it was 4 measures, or 0.5 measures, etc. I want these patterns to be components
  represented by grids of their corresponding sizes and visualized."
- "choosing the ui for this and creation for this should be seamless and intuitive, and bear in mind people wont know how long their melody is
  at first so let it autoexpand as they reach the limit."
14.1 Model. kind 4 = a PHRASE (a melody roll): TIME runs along x, PITCH along z. `KeyBlock.IsPhrase`. It starts at its column (`col`) +
`phraseOffset` (ticks from that column's start; 0 default; snapped to half measures), lasts `phraseBeats` (2 = ½ measure, 4, 8, 12, 16 … up to
32), its cells are `phraseGrid` ticks wide (12 = eighths default, 6 = sixteenths); its rows = the song key's scale over two octaves (15 rows,
voiced around 60..84; + 12 × register). A phrase SPANS every column its time overlaps (a 2-measure melody over two 1-measure chords = ONE grid).
14.2 Layout (K). Phrases live in MELODY TRACKS behind their columns' lanes (track 0 right behind the back-most lane; phrases that overlap in time
take tracks further back), x from x(start) to x(end) through the columns' layout (inside a section time → x is linear; over a section gap a
thin bridge). Ground: a phrase follows the ground of its start column. Timeline: one window per song pass over its span (never per column
pass); column repeats do not stretch it.
14.3 Build (B). A long slab, one tile per cell (TileInteraction: gridX = time cell, gridZ = row; tile.midi = that row's pitch), bold MEASURE
lines, thin BEAT lines, a tiny vertical piano on the left end (rows read as pitch: higher = further back and a touch higher), under each column
segment a tint of that chord's vibe colour and a SAFE-NOTE dot on the rows that are its chord tones (refresh on chord / key changes).
14.4 Drawing (D): with a cube in the hand the hovered cell shows the hologram cube stretched over the brush length in cells (§15); a click
places that note (the cube's path = its notes in time order, gaps become rests, a note placed over another replaces what it covers); the empty
hand presses cells (loud). AUTO-GROW: a note that reaches into the last half measure grows the phrase by one measure (SongOps.GrowPhrase:
½ → 1 → 2 → 3 → 4 … measures; the roll extends to the right with a short animation and a soft tick); growing past the song's end appends a
measure (a new column: MusicTheory.SuggestNext's chord) to the last section. The whole stroke is one History entry.
14.5 The cube on a phrase walks LEFT → RIGHT through time (each note's cell, hop to the next; rests wait) — the time grid made visible.
14.6 Ops (O): `SongOps.AddPhrase(int col, int instrument = -1)` (a 1-measure phrase in that column's melody track with an empty runner-less
state; the hand picks up `instrument` (−1: the lead group) ready to draw), `SongOps.SetPhraseLength(KeyBlock kb, int beats)`,
`SongOps.GrowPhrase(KeyBlock kb, int toBeats)`, `SongOps.MovePhrase(KeyBlock kb, int col, int offsetTicks)`. U1: the MELODY card (a little
roll) in the tray; the phrase header (grip, eye, length ½ 1 2 4 + "auto", cells 8ths / 16ths, register, repeat, duplicate, more).
Keyboards (v6) stay as they are (one row per column).

## 15. Size first, then place (verbatim)
- "also for rhythm control, when they're selecting the path of the cube, make them sleect the size of the cube THEN place it down."
15.1 (D + U2) The brush (PathManager.BrushTicks) is the size of the NEXT note and is chosen BEFORE placing: the hologram at the cursor always
shows it (grids, keyboards, stairs runners, phrases — on a phrase it stretches over its cells); the SIZE ROW (U2: five cube sizes + the dot,
beside the held chip and echoed next to the cursor while drawing), the mouse WHEEL over a grid while a cube is in the hand, and [ ] / . change it
with a tiny audition of the length; a click places a node of exactly that size. [ ] no longer resize the node just placed (select its bead to
change a placed note: the inspector's length row, as today). The first placement of a new cube also waits on the size the row shows (the last
used size is preselected and pulses once so the player notices it can be chosen).

## 16. Select many grids (verbatim)
- "you should also be able to hold shift and drag over a section to then select all the grids in that section and like paste or delete or
  other options" — "or transpose up an octave or down an octave etc"
16.1 Input (D): Shift + left-drag over the world draws a marquee (screen-space rectangle, ink-dashed) selecting every grid (chord islands,
keyboards, stairs, phrases; not Moons unless the rectangle covers their centre) whose platform it touches; Shift + click a grid toggles it;
Shift + click a section plinth selects the whole section; Esc / a click on empty sea clears. Shift+drag never orbits / pans the camera.
16.2 Model (O): `GridSelection` (new DeaCube/GridSelection.cs): `Selected` (islands in song order), `Set(list)`, `Toggle(kb)`, `SelectSection(s)`,
`Clear()`, `event OnChanged`; selected grids wear an ink outline + a soft lift (W / B may add a shimmer).
16.3 Ops (O, SongOps — each one History entry, seek-keeping, the selection follows its grids): `CopyGrids(list)` (grids + their cubes to the
grid clipboard), `PasteGrids(int atColumn)` (as new columns there, joined into the section they land in), `DeleteGrids(list)`, `TransposeGrids(list,
int octaves)` (register ± 1, clamped −2..+2; the towers rise on their turns), `DuplicateGrids(list)` (right after the last selected column),
`SetRepeatGrids`, `SetFlowGrids` (flow through the selection), `SetLaunchGrids`. Sections: `SplitSectionAt(int col)`, `JoinSectionAt(int col)`,
`DuplicateSection(int s)`, `DeleteSection(int s)`, `MoveSection(int s, int to)`.
16.4 UI (U2): the SELECTION BAR — a floating ink card over the selection (icons first, captions on hover): copy, paste (after the selection),
delete, octave ↑ / ↓, duplicate, repeat, flow, launch; Cmd+C / Cmd+V / Delete / Shift+↑ / Shift+↓ act on the selection when there is one.
U1: the SECTION HEADER on a hovered plinth: split here, join with the next, duplicate the section, delete, drums (a Moon for this section),
launch into the next section.

## 17. Research (verbatim: "there's more stuff too, do some research and see if there's any other stuff") — adopted for v7
The research pass (GarageBand / Logic arrangement markers, Ableton Learning Music, FL Studio, Hookpad, Orb Composer, Hyperscore, Cococo, BandLab
SongStarter, Bitwig, Band-in-a-Box …) found, among 20 ideas, three that fit this build cheaply; the rest (linked sections, "unfold a loop into a
song", seam cards, three-ghosts alternatives, push / anticipation, catch-what-you-played, pass lamps, lift / key change, pump) are for later.
17.1 SECTION NAMES (sections are objects — GarageBand letters, Logic arrangement markers, "songs are built in multiples of four, eight or 16
bars"): a section can be named INTRO, VERSE, CHORUS, BRIDGE, DROP or OUTRO (an icon each; none by default — the letter only).
Model (K): `MeasureState.secRole` / `MeasureData.secRole` / `KeyBlock.secRole` (0 none, 1 intro, 2 verse, 3 chorus, 4 bridge, 5 drop, 6 outro);
a section's name = the secRole of the first island of its FIRST column (it travels with that column through every op). O:
`SongOps.SetSectionRole(int s, int role)` (one History entry), DuplicateSection keeps it. B: the plinth shows the role's icon + its lowercase
word beside the letter. U1: the section header's name picker (6 icons + none, hover = the word), the rail brackets show the icon.
17.2 PHRASE LENGTHS IN MUSICAL UNITS (FL "auto pattern length", Ableton "duplicate loop"): GrowPhrase grows to the next of 1, 2, 4, 8 measures
(½ below one measure; never 3, 5, 6, 7 by auto-grow — the header may still set them), and a DOUBLE button on the phrase header: the phrase
becomes twice as long with its notes repeated in the new half (`SongOps.DoublePhrase(KeyBlock kb)`, one History entry). Owners: O, U1.
17.3 STEP — a sequence (Hyperscore, Orb Composer's question / answer): paste modes `Clipboard.PasteMode.StepUp` / `StepDown` — the pattern
one scale step higher / lower on the next grid (Harmony.StepShift, REAL in M0; then to tiles like AdaptMelody's results: MelodyToGrid on chord
grids, exact keys / cells on keyboards / phrases). Owners: H (the modes), U2 (two more entries in the paste fan: step ↑ / step ↓).

## 18. Packages, ownership, acceptance (REPLACES §1, §9, §10)

| pkg | what |
|---|---|
| K | layout & timeline & motion: sections (model, API, the touching layout, gaps between), widths by length, grounds (GroundY, Moons, camera), phrase spans + melody tracks, windows (pass, flow, phrase), RewindPhase, launch timing, the hold-then-return rules for belts / towers (+60 %) / Moons (§12.2–12.4), save / load |
| O | song ops (new DeaCube/SongOps.cs, DeaCube/GridSelection.cs): stairs (AddStairIsland, SetStair, runner paths), phrases (AddPhrase, SetPhraseLength, GrowPhrase + appending measures, MovePhrase), sections (split / join / duplicate / delete / move), grid selection + multi-grid ops (copy, paste, delete, transpose, duplicate, repeat, flow, launch) |
| B | island builds & structure visuals (KeyBlock.Kinds.cs — a partial of KeyBlock — + MeshFactory.cs + new DeaCube/SectionPlinth.cs): the stairs build, the phrase roll build, beat pips + measure numbers on grids, section plinths (letter, measure numbers, beat pips, ghost measures, the playhead line) |
| A | audio: layers / bends in every pitch path, stairs + phrase tiles, the launch riser + crash, harmonic-safety rules, performance |
| H | cubes (AudioCube.cs, CubeOps.cs, Clipboard.cs, CubeOutline.cs): octave copies / echoes, harmony, flip, paste modes, runner refresh, the motions (layer ride + phase-in, stair fall, rewind zip, flow glide, fling) and §12.1 hold-then-return, TileAt (vary / flow) |
| D | drawing & input (PathManager.cs, TileInteraction.cs, MelodyLine.cs, PathGridView.cs, DurationPicker.cs, FocusLoop.cs): size first (§15), phrase drawing + auto-grow (§14.4), runner placement on stairs, the Shift marquee (§16.1) |
| W | world magic: stairs FX, terraces, echo layers, rewind (time unwinding), the flow ribbon, the fling, the flip mirror, selection shimmer, section playhead glow |
| U1 | islands & sections UI: the stairs + melody cards, the stairs / phrase / grid headers (repeat style, vary, flow, "fill the section", launch), the section header, the rail (sections, measures, stairs, heights, flow, launch), tutorial + support (§13.4) |
| U2 | cube UI, selection bar & shortcuts: the cube card (octave copies, harmony, flip, paste fan), the size row (§15), the clipboard chip fan, the selection bar (§16.4), hotkeys, the shortcut sheet |

Ownership (edit only your files; read anything; requests → build notes: requests_<you>.md):
| K | SongManager.cs, KeyBlock.cs (not KeyBlock.Kinds.cs), ProjectConfig.cs, OrbitCamera.cs, DeaCube/IslandDrag.cs, IslandGhost.cs, ColumnBands.cs, Route.cs, Comet.cs, Belt.cs, Tests/V7ChecksK.cs, Tests/V6ChecksK.cs, Tests/V5ChecksK.cs, Tests/V4ChecksK.cs, Tests/V3ChecksB.cs |
| O | DeaCube/SongOps.cs, DeaCube/GridSelection.cs, Tests/V7ChecksO.cs |
| B | KeyBlock.Kinds.cs, MeshFactory.cs, DeaCube/SectionPlinth.cs (+ new structure-look files), Tests/V7ChecksB.cs |
| A | DeaCube/Audio/**, DeaCube/VoiceRules.cs, DeaCube/Palette.cs, GlobalClock.cs, SequenceMaster.cs, Tests/V7ChecksA.cs, Tests/V6ChecksA.cs, Tests/V2Checks.cs, Tests/WpAChecks.cs |
| H | AudioCube.cs, DeaCube/CubeOps.cs, DeaCube/Clipboard.cs, CubeOutline.cs, Tests/V7ChecksH.cs, Tests/V6ChecksH.cs |
| D | PathManager.cs, TileInteraction.cs, MelodyLine.cs, PathGridView.cs, DurationPicker.cs, FocusLoop.cs, Tests/V7ChecksD.cs, Tests/V5ChecksR.cs, Tests/V4ChecksR.cs |
| W | DeaCube/Magic.cs, DeaCube/Fx.cs, DeaCube/WorldMagic.cs, DeaCube/Tower.cs, DeaCube/CarryBridge.cs, DeaCube/Look/**, new v7 FX files, Tests/V7ChecksW.cs, Tests/V6ChecksW.cs |
| U1 | DeaCube/IslandTray.cs, IslandHeader.cs, new DeaCube/SectionHeader.cs, Ink/Hud/HudColumnRail.cs, Vibe.cs, VibeGlyphs.cs, JobBadge.cs, Onboarding.cs, Hints.cs, Menu/TutorialBubble.cs, Tests/V7ChecksU1.cs, Tests/V6ChecksU1.cs, Tests/V5ChecksU.cs, Tests/V4ChecksU1.cs, Tests/V3ChecksD.cs |
| U2 | Ink/Hud/HudInstruments.cs, HudKit.cs, HudTransport.cs, HudMenuStrip.cs, HudPresent.cs, HudClipboard.cs, new Ink/Hud/HudShortcuts.cs, new Ink/Hud/HudSelection.cs, new Ink/Hud/HudSizeRow.cs, DeaCube/CursorKit.cs, InspectorCard.cs, CubeInspector.cs, UIManager.cs, InterfaceController.cs, Tests/V7ChecksU2.cs, Tests/V6ChecksU2.cs, Tests/V4ChecksU2.cs |
| orchestrator | everything else (Harmony.cs, SongState.cs, the menu / gallery / presentation files, V7Integration / V7Suites, older suites not listed, README, docs) |
Where PART I names a different owner for a v7 item, this table wins (e.g. §3.1's stairs BUILD is B's, AddStairIsland / SetStair are O's —
SongManager keeps thin forwarding members; the pass / flow windows and everything in SongManager.cs stay K's).

Acceptance (orchestrator): every older suite green (pre-v6 suites with AutoHand), every V7Checks* green, V7Integration: a fresh song → its
sections (4 measures each, touching grids, gaps between, letters, measure numbers, beat pips) → a melody phrase drawn size-first that
auto-grows from 1 to 2 measures (one grid spanning two chords) → a stairs spark fall into the next chord (the runner falls step by step; the
next section's grids sit lower) → octave copies ↑ / ↓ + a harmony cube + flip → paste as echo ↑ + answer → repeat ×3 rewind + vary → flow
"fill the section" → launch into the next section (fling lands on the downbeat, riser) → shift-marquee a section → octave ↑, copy, paste after,
delete → hold-then-return (a finished grid's cubes hold until the loop, then walk home; towers +60 % stay up then sink staggered; belts return
at the loop) → present → save / load (every v7 field + sections) → Synth late / errors 0; the user's save md5 ef5c7a1e0808fe6fda3efb7b81befa3f
unchanged.

## 19. Capacity and quiet drawing (added mid-build; verbatim)
- "i think to help encourage the section/measure limitations btw like add a cap on beats per path. like if max is hit then it says capacity
  reached in which they'll have to expand the grid to continue -> however when making notes at first, it can auto-expand if needed or
  auto-unexpand."
- "also whenever the user is actively creating a cube path, it shouldnt be looping over and over unless they press a key or press ui or
  something (so that they can first express the melody in their heads with it constantly looping other notes."
19.1 CAPACITY (D; K / O ops; U1 tips). A path's capacity = its grid's length in beats (a chord grid / keyboard: its column's bars × beats per
bar; a phrase: phraseBeats; stairs runners are derived). While a path is being DRAWN the grid shows how full it is — the grid's beat pips (B)
fill as the draft's notes add up (the brush's next note shows as a ghost pip span), a small "n / m beats" caption on the draft row.
AUTO-EXPAND while drawing: a note that would pass the capacity grows the grid by one measure (chord grid / keyboard: K's live column resize;
phrase: SongOps.GrowPhrase) as long as the grid's SECTION stays within SectionBars (4) measures (a phrase: up to its section's end);
AUTO-UNEXPAND: removing the draft's last notes (Backspace / RemoveLastNode) shrinks what auto-expand added back to the smallest length that
fits (never below the length the grid had when the draft began). CAPACITY REACHED: past that limit the note is refused — a soft thud, the grid
shakes, the caption "capacity reached" with an EXPAND chip (+ one measure: the section grows past 4) right on the draft row; after an expand
drawing continues. Finished paths keep the cap (a later edit that would pass it asks for the expand the same way). The draft never loses its
nodes through a resize (no rebuild under a live draft: K's live resize keeps islands and tiles).
19.2 QUIET DRAWING (D). Drawing never starts a loop by itself (retire v4 R4's "its column loops while it is drawn" and the phrase draft's loop):
when a path starts while the song plays, the song PAUSES (so only the notes being placed sound — each placed / hovered note auditions as
today); a LOOP button on the draft row (a small ▶⟲ "hear it") and the Space key while drawing start / stop FocusLoop over the draft's grid
(or phrase) in context. Finishing the path does not auto-play either.
19.3 Owners: D (PathManager, DurationPicker's draft row, FocusLoop: 19.1 UX + 19.2), K (`SongManager.ResizeColumnLive(int col, int bars)` —
timeline + layout + the platform, NO rebuild, no History, tiles and cubes stay; `SongManager.SectionBarsIfResized(col, bars)` or a similar
query for the section limit), O (`SongOps.GrowPhrase` / a no-History `SongOps.ShrinkPhraseForDraft(kb, beats)`; `SongOps.ExpandGrid(KeyBlock kb)`
= the explicit +1 measure beyond the limit, one History entry), B (the pips can show a draft's fill: `KeyBlock.SetDraftFill(float beats, float
nextBeats)` or equivalent), U1 (a tip the first time capacity is reached: "a grid holds its measures — expand it to keep going"). Tests in
V7ChecksD / V7ChecksK / V7ChecksO.

## 20. The piano keyboard extends, and a CAT plays it (added mid-build; verbatim)
- "also for the piano keyboard, it should also be extensible if needed (like if a melody goes beyond an octave) and should be played by a cat
  similar to the cat in the new screenshot with their arms. for more references look online at at offscrambledeggs for their reels"
Reference: a screen recording of the reel (an Instagram reel by OFFscrambledEGG, @offscript_ on TikTok — "all my fellas"
cat-piano animations). Frames: (frames kept with the build notes). The character: a flat CHARCOAL-BLACK cat (#2e2d33, no shading,
a rough hand-drawn ink edge), a WIDE head like a mushroom cap / a wide hat with two small pointed ear tips at its outer corners, HUGE pale cream-
yellow eyes (#f2e9a6) with dark pupils that look DOWN at the keys it plays (half-lidded when calm, wide when it reaches), a thin white mouth line,
a small narrow torso, and two RUBBERY NOODLE ARMS (thick, tapered, rounded paws) that stretch far across the keyboard — often one arm reaching
right across while the other rests or presses; the whole body leans and squashes toward the key it plays; sometimes it turns its head away (we
see the back of its head) between phrases. The piano: a long keyboard in perspective, white and black keys with sketchy ink lines. White page,
black ink, drawn on twos. Charm over realism.
20.1 EXTENSIBLE KEYBOARD (K model + build; D drawing; U1 header). A keyboard island's range is `keyCount` keys from its lowest key
(chordRootMIDI): 13 (one octave) .. 61 (five octaves); new keyboards keep 25. `MeasureState/MeasureData/KeyBlock.keyCount` (0 = 25 in older
files). K: BuildKeys lays any count over the island's width (bars × IslandWidth; narrower keys as it grows; at > 37 keys the island's depth grows a
little so keys stay readable), `SongManager.SetKeyRange(int at, int lowKey, int count)` (one History entry; cubes' key indices remapped so every
note keeps its PITCH), `SongManager.ExtendKeysLive(KeyBlock kb, int dir)` (+1 octave above / −1 below; NO rebuild, no History: key objects kept
(re-indexed), new keys slide in from that edge, cubes' paths keep their notes — like ResizeColumnLive), `ShrinkKeysLive(kb, dir)`.
D: while drawing on a keyboard a click on the lowest / highest key (or a drag past the edge) EXTENDS the keyboard by an octave that way (auto,
live) so a melody can go on beyond its range; finishing the path gives back the octaves it added that hold no notes (auto-unexpand); the
stroke pushes ONE History entry. U1: the keyboard header gets "+ octave below" / "+ octave above" (and "fit to the melody") items; a light tip.
20.2 THE CAT (new package C, new files DeaCube/KeyCat.cs (+ its own look files) and Tests/V7ChecksC.cs; reads islands and cubes, edits none of
them). Every keyboard island has a cat sitting BEHIND its keyboard (centred, facing the camera, scaled to the island: the head ≈ 0.5 × the island
width), in the reference's style as a 2D cut-out (camera-facing quads / flat meshes with procedural textures and ink edges, drawn on twos).
When the keyboard's cubes play, the cat PLAYS THEM: a paw arrives on exactly the key that sounds at its onset (a pure function of the song beat, like
the cubes' hops — seeks and loops land right), presses it (a squash + the key's press look), lifts; the arm is a stretchy noodle from its shoulder
to the key (bezier tube, tapered, round paw) so far keys mean a long reach; the nearer arm takes the note (left for low, right for high; a second
cube = the other arm); the eyes follow the playing key, the head and body lean toward it with squash and stretch; between phrases it sways,
blinks, sometimes turns its head away; when the empty hand presses a key the cat plays that key too. Stopped: it waits (paws on the keys' edge,
slow breathing, blinks). It never blocks picking keys (no colliders; the hand's raycasts ignore it). Several keyboards = several cats (cheap: one
mesh set each, ≤ 0.3 ms per cat).
20.3 THE CAT, ROUND 2 (the user's correction, verbatim: "the cat should look more like the cat in the video -> shorter, stubbier arms, and it
should look much more like the cat. added two more videos for reference (bear in mind the're slightly different povs and there's also a girl which
should be excluded - it's just the black cat)"). REPLACES the character notes above and 20.2's noodle arms: PURE flat black (#0b0b0d, unlit),
thin white contour lines only where black overlaps black (the chin over the body, a mitten over the head); the head is most of the figure, a
wide FLAT-TOPPED BELL whose top corners are the ears (rounded points), its sides sloping in to a narrower chin; HUGE pale lemon (#fbf8b4) oval
eyes set low and close together, big black pupils pushed toward the key it plays; no mouth; a blink is a thin white arc; SHORT, THICK,
constant-width MITTEN arms with rounded ends (reach at most 0.9 × the head width): far keys are reached by the body SCOOTING along behind the
keyboard and leaning, the paw still landing on exactly the sounding key at its onset; idle, a paw comes up to rub an eye now and then, the head
tilts, it blinks and breathes.
20.4 THE CAT, ROUND 3 (the user, verbatim, 2026-09-30 ~23:35): "the cat's arms shouldnt be as long and bendy, it should be almost one limb (rather
than the bending thing going on right now). also the piano shouldnt be backwards (the cat shouldnt be playing the piano backwards). since the notes
are closer the arm doesnt have to be bent over. it's just that whenever you're actually designing the notes on the piano, you move into first person
perspective from the cat and when you add a note the cat's first person paw hits that note. also ensure that when the cat is playing the notes, it's
actually animated to hit the correct notes on time with smear frames as needed and personality (as with handrawn cat animations). moreover it
shouldn't be the black color, it should be more similar to the color scheme/style of the website and fit in more"
REPLACES 20.2's placement and 20.3's colour / arms (the character's SHAPE language from 20.3 stays: the flat-topped bell head with ear corners, the
huge low eyes with big pupils, no mouth, the mitten paws):
- THE RIGHT SIDE OF THE PIANO: the cat sits on the PLAYER's side of the keyboard (the key fronts' side = the camera's side), FACING the keys, so it
  plays the piano the right way round (low keys on ITS left). From the default camera we see it from behind / three-quarters behind (the back of the
  bell head, the ear corners, the arms reaching forward); now and then it glances back over its shoulder at the camera (the face, the big eyes) —
  personality. It sits LOW (its body below the key level, head and shoulders above it, like a pianist at a bench) and is small enough not to hide
  the keyboard (head ≈ 0.3 × a one-measure island); while the pointer works on its keyboard it fades (the x-ray fade) so keys stay readable.
- ARMS: SHORT, almost ONE STRAIGHT LIMB each (a tapered sausage from the shoulder to the mitten paw; at most a tiny soft curve — no elbow, no bending
  over the keyboard). The keys are right in front of it: the paw reaches forward / sideways; for far keys the body scoots along the bench.
- FIRST PERSON WHILE DESIGNING: whenever the user is designing notes on a keyboard (a keyboard draft in progress — from its first key until it is
  finished or dropped), the camera glides (≈ 0.5 s, eased) into the CAT'S FIRST-PERSON VIEW: from the cat's eyes at the player's side, looking at the
  keys (the whole keyboard in view, a comfortable downward angle); the cat's own PAWS are in the foreground (first-person "hands", in its style,
  entering from the bottom of the screen); every note the user adds, the first-person paw STRIKES that key (anticipation → smear → impact with
  squash and the key sinking → a little recoil / follow-through), on the click. Finishing / dropping the draft glides back to the camera the user had.
  Keys stay clickable (the paws have no colliders; they never cover the key being aimed at for long). The draft's UI (size row, ✓, hear-it) must stay
  usable in the first-person view (D pins it if needed).
- ANIMATION (hand-drawn cat animation): every played note is hit ON ITS ONSET, on the right key, with real animation principles — anticipation
  (a small lift / wind-up before the strike), SMEAR FRAMES on fast moves (stretched / multiple-paw smears drawn for a frame or two), squash on
  impact, overshoot and settle, follow-through (ears, head bob), timing on twos with accents on ones for hits; personality between notes: head bobs
  to the beat, ear flicks, blinks, a tail swish, a satisfied squint after a run, glances back at the camera, a stretch when the song stops.
- COLOUR / STYLE: NOT black. The cat belongs to DeaCube's look: Comic.Cream / InkUI.Paper body (or a soft lilac from the sea's palette, Palette.Bg
  family), Comic.Ink outlines (the same ink weight / wobble as the islands and cards), cel / halftone shading like the toon tiles, the eyes kept
  big (cream-lemon with ink pupils or the game's accent), small pastel accents (blush, the paw pads) from the palette. It should look like it was
  drawn by the same hand as the rest of the game.

## 21. Cubes stay on their grid; THE LONG GRID (added 2026-09-30 ~21:55; verbatim; REPLACES the cross-grid motions of §2.6–§2.8, §5, §6.5–§6.6, v6 carry hops, v4 riders)
"the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid.
only explore grids moving from left to right with the long grid that i mentioned earlier"
(The long grid = ask 5: "same grid going on over and over again (like the grid extending and being longer and the cubes just going from left
to right through the grid) like when the song is at 23.5 seconds, the harmony basically repeats and sounds like its flowing".)

21.1 THE RULE. While the song plays, loops, stops or seeks, a cube NEVER leaves its own grid. A long grid (21.2) counts as ONE grid. Scrapped:
  - v4 RIDERS' flights: a rider no longer plays on other islands (no rider windows; `rider` stays in the file format, ignored; the cube card's
    rider toggle and its key are retired). A saved rider plays on its own grid like any cube.
  - v6 CARRY's hop style (golden arcs, flights, sigils, ghost stars) and v7 FLOW's glide between separate grids → both become the long grid.
  - v7 LAUNCH's FLING (cubes flung onto the next grid): retired. The launch keeps only its SOUND (21.4).
  - v7 stairs RUNNER EXIT onto the next grid: the runner stays on its stairs.
  - guest holds / cross-grid trips (nothing is ever on another grid).
  - PASTE no longer flies a cube across: the pasted cube APPEARS on its target grid (a pop + sparkle there).
  Kept: every motion INSIDE a grid (paths, hops, rewind zip, hold-then-return within the grid), islands moving WITH their cubes (belts, towers,
  drags, Moons), octave layers (they belong to their grid).
21.2 THE LONG GRID. A grid EXTENDS to the right over the next 1–3 grids of its lane inside its SECTION (or to the section's end = "fill the
  section"); it never crosses a section boundary (clamped).
  - LOOK: the extended grid and the grids it covers JOIN into ONE long grid — one continuous platform (the inner rounded corners / outlines /
    gaps go away; a thin ink divider marks each chord change: the v3 merge-group seam look is the model), each measure keeping its own chord
    (tile pitches, chord colours, its own cubes, hub, pips, header). It must read as THE SAME GRID GOING ON TO THE RIGHT, the chord changing
    measure by measure.
  - MOTION: the extended grid's cubes walk LEFT → RIGHT through the long grid: their pattern plays on every measure, re-voiced for that measure's
    chord by the flow rule (Harmony.AdaptPedal: the pedal stays, only what must move follows); crossing into the next measure is an ORDINARY STEP
    onto the next measure's tile — the same hop as inside a grid (no arc, no glide, no flash, no trail). The covered grids' own cubes stay on
    their own measure.
  - HOLD THEN RETURN (§12.1, within the long grid): after the last measure the walking cube holds where it ended (on the long grid), and goes back
    to its own measure ALONG the long grid (a hop-glide over the platform, no arc) right before its grid plays again, or after the song resets
    (staggered).
  - the column rail shows a long grid as ONE bar across its beads; the world may add a soft glow travelling under the walking cube and light
    the long grid's rim in the source's colour while it walks (W, optional, subtle).
21.3 CARRY = EXTEND. One header control "extend →" (replaces the carry wand + flow ≈): ×1 → ×2 → ×3 → off, plus "fill the section".
  carryStyle is ignored (always the long-grid behaviour = v7's flow windows); songs saved with carry (any style) load as long grids.
  SongManager.SetCarry(at, n, style) keeps its signature (style ignored → 1). SongOps.SetFlowGrids(list, n) = extend the first selected grid
  through the selection (unchanged meaning).
21.4 LAUNCH = a build-up, no fling. The ↗ toggle (grid header, section header "launch into the next section", ⇧→, the selection bar) keeps:
  a riser swells into the next section's first downbeat and a crash lands on it (LaunchRiser, unchanged). The launching grid's cubes do NOT
  move. Look (W, optional): the launching grid's rim brightens through the swell; a sparkle burst on the target's first measure at the crash.
21.5 TESTS: every assertion about flights, flings, carry arcs, rider windows, runner exits, guest holds or paste flights changes meaning — the
  OWNER updates it (reason "§21") and adds checks for 21.1–21.4: no cube's body ever leaves its grid's (long grid's) footprint while playing a
  whole song with carry / flow / launch / stairs / riders / pastes; the long grid joins (one platform, dividers); the walk is left → right.

## §22 Section letters in colour (the user, 2026-10-01: "the ui of the section letters shouldn't be black and white, should be more resonant colors — moreover the letter should be a bit smaller, with a circle around it")

- Every section letter has its own colour, and sections that share a letter share it: A marigold, B coral rose, C teal, D violet, E leaf, F tangerine, G azure, H magenta, then the cycle repeats. `SectionPlinth.LetterTint(letter)` gives the colour and `LetterDeep(letter)` its deep shade. No letter is drawn in black and white any more.
- On the plinth the letter is smaller (cap height 0.86, down from 1.35) and is centred on its glyphs. It sits on a circle 1.36 across: a disc of the letter's colour with a 0.12 rim in the deep shade. The circle stays inside the front margin. The letter's face is cream warmed by its colour, and its outline is the deep shade.
- On the column rail and in the section header, the letter's disc is in the letter's colour, pale until the section has its four measures. Its rim, shadow and letter are in the deep shade. The rail's sparkle on reaching four measures uses the letter's colour.

### §20.4.1 Cat round 3.1: short first-person paws

The first round-3 first-person view framed the keyboard in the upper middle of the screen, so the paws were long poles reaching up from the bottom edge. The user had said "the arms shouldn't be as long", so the view was changed:
- The camera looks 58° down, with the white keys' front edge 88% down the screen. All keys of a two-octave keyboard stay in view, and the draft row stays above them.
- The paws are short, foreshortened mittens rising from the bottom edge. At rest they show about 11% of the screen height. A white-key strike shows at most about 23% and a black-key strike about 28%.
- The view frames at most one measure. A wider keyboard slides sideways to follow the key being placed, and resting the pointer at the screen edge pans the view along.
- Playing the song, or hearing the draft, returns to the player's own view.

## §23 Grid paths (the user, 2026-10-01: "add the grid level edition and also make it a button")

- `Clipboard.CopyPaths(grid)` copies every path of a grid: its finished cubes, octave layers and harmonies included (a stairs grid has none: its cubes are its runners).
- `Clipboard.PastePathsOn(grid)` places each path that can go there, re-voiced for that grid's chord by the single paste's rules (`Clipboard.Adapt`), alongside the grid's own cubes. It is one History entry, and the cubes appear on the grid.
- The paths never go back onto the grid they came from. A melody roll takes only melody rolls' paths; stairs take none.
- The island header has two stickers on the ribbon's top edge. Copy-paths shows on a grid with paths. Paste-paths, with a ×n count, shows where the copied paths can go.
- ⌘C over a grid's empty tiles (no cube under the pointer) copies its paths. ⌘V over another grid pastes them; the last copy, a cube or a grid, decides what ⌘V pastes.

## §24 The deck's pattern preview (the user, 2026-10-01: "whenever you're hovering over possible cards … play the last (live) cube grid pattern (all the grids for that measure) in that chord, adapted; if no chord, then just have the fallback")

- With the song stopped, hovering a chord card plays one pass of the reference column (at most two bars) on the preview bus. Every grid in it plays: chord grids, keyboards and melody rolls (not stairs or Moons). Each cube's path is re-voiced for the card's chord the way a paste would be, in the cube's own sound, lengths and rests.
- The reference column is the column a placed card would follow: the selected island, else the camera's focus, else the last. If that column has no notes, the nearest column before it with notes is used.
- Fallbacks: a card without a chord (keyboard, stairs or melody-roll cards) or a song without notes plays the chord's arpeggio as before.
- While the song plays, a hover stays a quiet tick. The song starting cuts a preview that is still sounding.
- Code: `CardPreview.Play` / `ReferenceColumn` / `ChordTable`, and `VoiceRules.PreviewPitch`.

## §25 Sphere pieces (the user, 2026-10-01: "when choosing, you can choose between sphere and cube. spheres can bounce and don't have to travel to direct neighbors. that's it. it should also roll as its bouncing")

- A shape switch sits above the instrument column: a cube and a ball on one cream sticker. The chosen one is solid with the ink ring; the other is an open outline. While the ball is chosen, the chips and the piece on the cursor are balls. Cube is the default.
- A sphere is a cube in every other way: the same notes, lengths, stickers, edits, clipboard and grid paths, and the shape is saved (`CubeState.sphere`).
- Drawing: a click on a far tile adds just that tile, a leap. A cube still fills the straight or diagonal line of tiles. The hover preview draws the leap as a dashed arc, and there is no neighbour ring. The inspector's path grid leaps the same way.
- Motion: a hop is a bounce. It moves at a constant ground speed on a parabola; its height (`AudioCube.BounceHeight`) and air time grow with the leap, up to about 2.5× a cube's hop and twice its air time. The landing squash is 1.35× a cube's, with a small stretch in the air.
- Roll: two crossed ink seams turn by distance ÷ radius about the axis across the motion, for any movement (bounces, trips home, rewinds). The body itself stays upright, so the squash stays vertical. The seams set a sphere piece apart from the keyboard's hand spheres.
- Code: `DeaCube/SphereBody.cs` (a partial of AudioCube), `PathManager.BrushSphere` / `SetBrushShape`, `HudInstruments.ShapeButton`, `CubeGlyph.Round`. Tests: `Tests/V8ChecksSphere.cs` (sp8_report.txt).
