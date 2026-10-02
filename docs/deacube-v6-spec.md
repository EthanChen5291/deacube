# DeaCube v6 — SPEC (as given to the v6 builders)

Project: AudioCube_Unity (Unity 6000.4 URP). v5 is built, uncommitted and green (V5Suites.RunAll: 887 checks, every suite since v3). v5 spec:
docs/deacube-v5-spec.md; v4 spec + UI language: docs/deacube-v4-spec.md, docs/deacube-v4-ui.md (Melatonin × Spider-Verse ink: cream paper, ink
outlines, drawn on twos, lowercase captions, ICONS FIRST, words minimal — captions / tutorial sentences / menu words / tempo digits are allowed).

## 0. The user's asks (verbatim, 2026-09-30, after playing v5)

1. "there should be UI that allows any given cube path to be pasted onto the next grid even if it's a different chord progression (adapted
   within the context of the genre basically)."
2. "certain grids should also be able to be transposed to the next chord progression, like if it's some pattern it should be able to be
   transcribed to the next chord in the chord progression -> when they do this is should look like magic, and on the UI there should be
   something (similar to the conveyor belt's vibe) that makes it look unique when played and recognizable"
3. "moreover if you raise a grid by an octave, the grid itself should rise (with waves coming out from underneath like it's a tower rising) on
   its turn and then slowly go down after its done"
4. "there should also be a keyboard style grid that just has one row (like a piano) for actual melody"
5. "along with the patterns, there should also be some ui showing which grid cards fit given the patterns that they have. so not just the vibe
   but also like a star as well as a number (like 1 2 3 4 in the chord progression, but take some creative liberty since 1234 can also work as
   1432 so they should know it can be swapped)"
6. "also, players should be able to click the grid without placing a cube every time - they have to actively select the cube to go into
   placing mode. and whenever theyre pressing the keys, it should be louder than whatever else is playing so they can actually hear it"
7. "also, the instruments should be expanded into groups, with more VSTs added - is this possible or will it hurt performance"
   (a "grid" = an island; "cube path" = a cube's pattern; "the keys" = tiles / piano keys the player clicks.)

Answer to 7 (the orchestrator tells the user; A measures the numbers): real VST / AU plugins cannot be hosted in the game (decided in v2: no
plugin host in the engine, players would need the plugins, licences). The game's "VST" is the SoundFont synth (MeltySynth + GeneralUser GS,
287 presets already loaded: pianos, e-pianos, organs, guitars, koto, shamisen, kalimba, synth leads and pads, brass, winds, 9 drum kits). More
instruments cost memory only when a new SoundFont is added (none is needed now); CPU cost scales with notes sounding at once, not with how many
instruments exist. So: groups of sounds, drawn from the bank we already ship.

## 1. Packages (built in parallel on one working tree and one Unity editor)

| pkg | what |
|---|---|
| K | islands & timeline: the keyboard island (build, layout, ops), carry (timeline, targets, windows), the octave tower (pose), job badges on platforms |
| A | audio: instrument groups (voices in the synth: slots, channels, both buses), louder presses, VoiceRules for voices / keyboards, the performance numbers |
| H | the hand: placing mode (pick up a cube to place; an empty hand plays / moves), keyboard drawing, copy / paste (adapted), carried cubes (adapted tiles + flights) |
| W | world magic: the tower (pillar + waves), the carry bridge (the recognisable look of a carried pattern, played and at rest), paste / carry spell effects |
| U1 | cards & islands UI: stars + job numbers + swap hints on cards, the keyboard card, the header's carry button (+ keyboard islands' header), the rail's numbers, the tutorial |
| U2 | hand & instruments UI: the instrument column as groups (voice flyout, the held chip), the cube / pattern cursor, the cube card's copy / paste / voice, the clipboard chip, hotkeys |

## 2. Shared contract (M0 — landed by the orchestrator; compiles in Unity; keep every public member; you may add members)

2.1 Save format (SongState v5). `SongState.CurrentVersion = 5`. `MeasureState.kind` 2 = keyboard island. `MeasureState.carry` (0..MaxCarry) +
`SongManager.MeasureData.carry` + `KeyBlock.carry` (all copies / round trips done). `CubeState.voice` + `AudioCube.voice` (round trip in
ToState / ApplySettings). `ProjectConfig.MaxCarry = 3`, `ProjectConfig.KeyboardKeys = 25`. Old files load unchanged (carry 0, voice 0, no kind 2).

2.2 Harmony (DeaCube/Harmony.cs — REAL, unit-tested; orchestrator-owned: ask for changes via requests). Read its summary. Pure core:
`Job(tonic, minor, root, semis)` → 1 home (I / i) · 2 away (ii, IV / ii, iv) · 3 heart (iii, vi / III, VI) · 4 pull (V, vii / v, VII, any dominant
7 / 9; off-scale sus4) — off-scale minor → 2, off-scale major → 3; `JobWord` ("home" "away" "heart" "pull"), `JobFeeling`, `Swappable(a, b)` (same
job); `Fit(midi, weights, root, semis, tonic, minor)` 0..1 with `StarThreshold` 0.72 (chord tone 1, key tension 0.55, avoid note 0);
`WeightsOf(durs, n)`; `AdaptGrid(...)` (grid → grid: keep the shape, the voice-led inversion shift; bass-like → the root first);
`AdaptMelody(...)` (a sequence by scale steps, long notes snap off avoid notes, range fit); `NearestTile`, `MelodyToGrid`, `FitRange`.
Unity helpers: `Job(KeyBlock)`, `Job(MeasureData)`, `ChordOf(KeyBlock, out root, out semis)` (keyboards: SongManager.ChordOfColumn, else the
key's home chord), `MidiTable(KeyBlock)`, `NotesOf(AudioCube, …)`, `NotesOfIsland(KeyBlock, …)`, `Fit(midi, w, MeasureData)`. `KeyBlock.Job`.
Why jobs: "1234 can also work as 1432" — I IV vi V and I V vi IV are both hits; the four jobs in any order make a pop progression, and chords
with the same number swap freely. That is the creative liberty we take: the number is the chord's JOB, the order is yours.

2.3 The hand (PathManager, H implements): `enum HandKind { Empty, Cube, Pattern }`, `Hand`, `CubeInHand`, `static event OnHandChanged`,
`PickUpCube(instrument, voice = -1)`, `PickUpPattern()` (clipboard → hand), `PutDown()`, `static bool AutoHand` (TESTS ONLY: an empty-hand press on
a free tile picks up selectedInstrument and draws like v5 — the orchestrator's suite runner sets it for the pre-v6 suites).
Clipboard (DeaCube/Clipboard.cs, H owns after M0): `Copy(AudioCube)` (real), `Clear()`, `HasPattern`, `State`, `Midi`, `Weights`, `SourceKind`,
`SourceRows`, `SourceRoot`, `SourceSemis`, `Instrument`, `Voice`, `IsDrums`, `OnChanged`, `CanPasteOn(kb)`, stubs `AdaptedFor(kb)`,
`PasteOn(kb)` (one History entry), `PasteNext()`.

2.4 Carry (K: timeline; H: tiles + flights; W: look; U1: header button). `KeyBlock.carry` 0..3 = the island's patterns ALSO play on the next
`carry` chords of the progression. `SongManager.SetCarry(at, n)` (M0 working: rebuild keeping the lit column + one History entry + OnCarryChanged),
`SongManager.CarryTargets(at)` (M0 working: in each next column the island nearest in z of the same family — chord → chord islands, keyboard →
keyboards else chord islands; the chain stops at a column without one), `SongManager.ChordOfColumn(col)`, `event OnCarryChanged(KeyBlock)`.
Windows (K): a resident cube of a carrying island gets, after its own windows, one window per pass of each target column on the target island
(`Window.carried = true`, `Window.hop = 1..n`, order = that column, silent when the source or the target sleeps). `AudioCube.TilesOn(island)`
(M0 working for home + any window: H makes the carried tiles adapted) → W draws ghost stars there.

2.5 Keyboard island (K builds, H draws, A sounds, U1 adds it). kind 2, `KeyBlock.IsKeyboard`, one row (rows = 1, gridZ 0) of
`ProjectConfig.KeyboardKeys` = 25 chromatic keys (gridX = key index); `SongManager.AddKeyboardIsland(island, above)` (stub → K). Its chord for
highlighting / adapting = `SongManager.ChordOfColumn(column)` (Harmony.ChordOf).

2.6 Tower (K pose, W look). `KeyBlock.TowerLift` (world units shown this frame), `KeyBlock.TowerState` (0 rest, 1 rising, 2 up, 3 sinking),
`SongManager.TowerLiftAt(kb, beat)` (pure; stub = the v4 static lift).

2.7 Instrument groups (A implements). The ten roles stay (colours, icons, volume, mute per GROUP); each becomes a group of voices:
`Instruments.VoiceCount(g)`, `VoiceName(g, v)`, `SlotOf(g, v)` (synth slot), `BrushVoice[g]` (what a new cube gets), `Audition(g, v)`; voice 0 = the
v5 sound. `AudioCube.voice` / `CubeState.voice`.

2.8 Press (A implements the loudness, H calls it): `Synth.PressNote(slot, midi, vel, onDsp, offDsp)` — a pressed tile / key, louder than the
song (stub = the v5 audition).

2.9 Magic (DeaCube/Magic.cs, W owns after M0): `ArcPoint(a, b, height, t)`, `ArcHeight(a, b)` (real), `Trail(Transform, Color, seconds)`,
`Land(p, color)`, `PasteFlight(from, to, color, seconds)` (placeholders until W).

## 3. K — islands & timeline

3.1 Keyboard island (kind 2) — "a keyboard style grid that just has one row (like a piano) for actual melody".
- Build (KeyBlock.Build for kind 2): a piano. 25 chromatic keys (two octaves + 1) from the lowest key = the song key's tonic voiced into 53..64
  (MusicTheory.KeyOfSong; stored in MeasureData.chordRootMIDI so it round-trips; semitones = {0} placeholder like a Moon). White keys long and
  cream / ivory, black keys shorter, raised, set back, ink-dark — real piano geometry (a two-octave span from a white key has 15 white keys).
  Same WIDTH as a chord island (KeyBlock.IslandWidth, so columns stay aligned); depth what reads as a keyboard (≈ 3 u). Pitch still reads as
  height (a gentle rise to the right, by eye). tile.midi = key + 12 × register. The key's home key wears a tiny house; keys off the key's
  scale are a little dimmer (still playable). Chord tones of its column's chord (ChordOfColumn) carry a soft dot in that chord's vibe colour
  (the "safe notes"), refreshed when the column / chord / key changes. A wood-ink platform (not a vibe colour), the beat track as usual.
- Layout / ops: a keyboard is an island in a column lane like any other (MaxLanes, lane gaps, NormalizeColumns, RelayoutLive, drag, merge rules:
  never merges with chord islands), with register, repeat, sleep, carry, duplicate, delete. `AddKeyboardIsland(island, above)`: into that
  island's column (above / below, pushing lanes), or (island -1 / a full column) as a new column after the last; RiseIn animation; one History
  entry. A column holding only keyboards: its length is the keyboard's bars; its chord = the previous column's chord island (ChordOfColumn may
  return null — Harmony.ChordOf falls back).
- Keep everything that treats islands generically working (Route, Comet, hub gauge, ColumnBands, IslandDrag, IslandGhost, belts); islands that
  hold chords (card placement, chord wheel, mood) skip keyboards.

3.2 Carry timeline: implement the windows (2.4) in RecomputeMeasureStarts; refine CarryTargets / SetCarry if needed (keep the signatures);
CarryTargets and the windows must agree. A carried cube keeps ONE owner and plays its home windows, then its carried windows, in song order.
Riders are unchanged. Layout is unchanged by carry (no space is made; the look lives above the islands).

3.3 The octave tower — "if you raise a grid by an octave, the grid itself should rise (with waves coming out from underneath like it's a tower
rising) on its turn and then slowly go down after its done".
- Replace the v4 static register lift with a pure function of the song beat (TowerLiftAt): at rest a raised island floats only a HINT higher
  (≈ 0.3 u per octave) — on its turn (its column's span, all its passes) it RISES to ≈ 2 u per octave: starting ≈ 0.5 beat before its column
  starts, reaching the top by ≈ +0.25 beat (fast, an ease-out with a small overshoot — a tower pushing up out of the sea), holding while it plays
  (a slight bob on the beat), then after its last pass it SINKS SLOWLY back to rest (≈ 3 beats, ease-in-out). A lowered island (register < 0)
  mirrors it: it dips on its turn (≈ 0.8 u per octave, never into the sea) and floats back. Loops wrap (a first-column tower rises before the
  loop point); seeks, the focus loop, undo land right; stopped = rest (+ a preview: pressing ▲ / ▼ while stopped plays the rise and the slow sink
  once, so the player sees what happens on its turn). TowerState reports the phase. Compose with the belt ride; cubes, the comet, the cable, the
  header and the hub follow (they read the island transform / VisualOffset / LiftY today — keep LiftY meaning "the lift shown now").
- The pillar and the waves are W's (reading TowerLift / TowerState); give W what it needs (the platform underside y, the rise start beat).

3.4 Job badges: chord islands wear their job number (Harmony.Job) as a small badge beside the vibe glyph on the platform's front margin (the
number in the job's badge shape — shapes / colours agreed with U1, who draws the same badges on cards and rail: use U1's `JobBadge` helper if it
exists when you get there; a simple ink-outlined disc with the digit is the fallback). Refreshed on chord / key changes. Keyboards and Moons wear
none.

3.5 Tests: Tests/V6ChecksK.cs (RunAll → Captures/k6_report.txt; captures k6_*.png): keyboard build (25 keys, pitches from the key, black / white
count, width = island width), add in column / as new column (one History entry each), save / load round trip (kind, carry), carry windows (count
= own passes + Σ target passes; order; silent rules; targets = CarryTargets), tower lift samples (rest / rising / top / sinking / rest again /
lowered dip; pure: the same beat → the same lift; seek-safe), job badges shown with the right digits, older K suites green (update only
assertions whose meaning changed — e.g. the static register lift — and say so).

## 4. A — audio: instrument groups, louder presses

4.1 Instrument groups (2.7). Build the voice table from GeneralUser GS (names verified against the loaded SoundFont; the bank has 287 presets,
listed in the SoundFont preset list (GeneralUser GS, 287 presets)). Ten groups, voice 0 = today's sound; 5–9 voices each, chosen for this game's genres (J-pop / Vocaloid /
lo-fi / wa-rock), for example — keys: e.piano (v5), fm e.piano, chorus e.piano, clav, harpsichord, tonewheel organ, accordion; pluck: nylon (v5),
steel, clean guitar, muted guitar, koto, shamisen, harp, kalimba, pizzicato; pad: warm (v5), polysynth, halo, sweep, solar wind, space voice,
bowed glass; lead: square (v5), saw, chiff, calliope, flute, trumpet, alto sax, shakuhachi, ocarina; bass: finger (v5), pick, fretless, upright,
slap, synth bass, acid; bells: vibraphone (v5), celesta, glockenspiel, music box, marimba, crystal, steel drums, tubular bells; strings: ensemble
(v5), slow strings, tremolo, violin, cello, synth strings, full orchestra; choir: aahs (v5), oohs, synth voice, solo vox; piano: grand (v5), bright
grand, electric grand, honky-tonk, bell piano; drums: standard (v5), room, power, electronic, 808/909, dance, jazz, brush. Each voice: bank,
patch, register window, gain trim (loudness-matched to voice 0 by ear and RMS), sends. Lowercase captions of one or two words.
- Engine: every (group, voice) is a synth slot with its own MIDI channel on BOTH buses (song + preview); slots 0..9 keep their v5 channels; raise
  MeltySynth's channel count (vendored, MIT: edit Synthesizer.cs, note the change in its header) and let every drum-kit slot's channel be a
  percussion channel. Program changes at init on both buses. Group volume / mute / pan / sends apply to all of its slots
  (Instruments.SetVolume / SetMuted / PushToSynth). VoiceRules: the role rules (sustaining, mono, bass anchor, register law, costumes) follow
  the GROUP; the sound follows the slot (Instruments.SlotOf(cube.instrument, cube.voice)); previews, auditions, the menu's MenuPreview (T's file:
  request the orchestrator) and every other caller of SlotOf(instrument) keep working (voice 0).
- Performance (the user asked): measure and report Synth.Stats cpu / voices for (a) the v5 table and (b) the expanded table with 12 different
  voices playing at once in a dense song, plus the SoundFont load time and memory before / after. These numbers go into the answer to the user.

4.2 Louder presses — "whenever theyre pressing the keys, it should be louder than whatever else is playing so they can actually hear it".
PressNote (2.8): the song bus ducks deeper and longer under a press than under a hover audition (≈ −15 dB, held for the pressed note's length +
a short release; hover auditions ≈ −10 dB), the preview bus is boosted (≈ +4 dB) and a press uses a strong velocity (≥ 100), within the limiter.
Target (WAV analysis): a pressed note over a dense playing song is ≥ 6 dB louder (RMS, 50–300 ms after its onset) than the song alone in the
same window; no clipping, Synth late / errors 0. Hover auditions stay audible and a little louder than v5.

4.3 Keyboards in VoiceRules: tiles of keyboard islands play their key's pitch (no chord-tone harmonic-safety assertion for them); register folds
keep the melody's octave (the lead / keys windows are fine; do not fold a melody note into another octave when it is inside MIDI 48..96); carried
windows play the tiles AudioCube.TileAt returns (H) and the harmonic safety uses the WINDOW's island.

4.4 Tests: Tests/V6ChecksA.cs (+ your older suites V2Checks, WpAChecks green): every voice's preset resolves in the SoundFont (name + bank +
patch), channels unique, drum kits on percussion channels, a note on each voice renders non-silent on both buses, voice 0 unchanged (same slot /
channel / program as v5), group volume / mute reach all its slots, press ≥ 6 dB over the song (WAV), hover duck −10, the performance table
(report), Synth late / errors 0.

## 5. H — the hand: placing mode, keyboard drawing, copy / paste, carried cubes

5.1 Placing mode — "players should be able to click the grid without placing a cube every time - they have to actively select the cube to go into
placing mode" (2.3).
- EMPTY hand: hovering a tile auditions it as in v5 (idle dwell); a CLICK (press + release in place) on a tile PRESSES it: its note plays through
  Synth.PressNote (louder than the song), the tile / key sinks and springs back, the island's header shows (IslandHeader hover); nothing is
  created. A press on a tile that becomes a DRAG moves the island (like its ground today: IslandDrag). A click on a cube opens the inspector (as
  today). On a keyboard: clicking keys plays them like a piano (each press loud); dragging across keys with Alt plays a glissando.
- CUBE in hand (PickUpCube from the HUD chip / a hotkey): the hovered free tile shows a hologram cube sitting on it (the instrument's colour, the
  size of the brush length); a click starts a path exactly as v5 (then click / drag to extend, finish as today); after finishing the cube STAYS in
  the hand (a tool). Esc (when not drawing), a right-click on empty sea, or clicking the held HUD chip again PUTS IT DOWN. Picking up another chip
  swaps it. Moons: any held cube places drums there (as today).
- PATTERN in hand (PickUpPattern: the clipboard chip / Cmd+V with nothing hovered): hovering a grid shows the adapted pattern there as ghost
  beads (Clipboard.AdaptedFor) — the exact notes a click would paste; a click pastes (Clipboard.PasteOn); Esc / right-click puts it down.
- `AutoHand` (2.3) for the older suites; your own V5ChecksR / V4ChecksR must pass with AutoHand = true (run them that way) and your v6 checks with
  false.

5.2 Keyboard drawing (on kind-2 islands; K builds the keys): a click adds EXACTLY the clicked key (no run of in-between keys — melodies leap);
clicking the draft's last key again adds a REPEATED note (the cube re-hops on the same key; beads stack so every node stays clickable); a drag
across keys adds each key the pointer enters (a glissando run); finishing: Enter, a right-click, a click off the island, or the draft row's done;
the brush length applies per note as today; hover / run auditions play single keys. MelodyLine draws the tune (it is the whole point of a
keyboard); PathGridView (the inspector's grid) shows one row of keys for keyboard cubes; bead / rest / sticker clicks work with repeated keys.

5.3 Copy / paste — "any given cube path to be pasted onto the next grid even if it's a different chord (adapted within the context of the
genre)". Clipboard.AdaptedFor / PasteOn / PasteNext (2.3), with Harmony: grid → grid AdaptGrid (bassLike when the group is bass or the pattern
sits on the lowest row: first note on the root); keyboard → keyboard AdaptMelody (source chord = Clipboard.SourceRoot / Semis, target chord =
Harmony.ChordOf(target), range = the target keyboard's keys); grid → keyboard AdaptMelody on the grid's pitches; keyboard → grid AdaptMelody then
MelodyToGrid; drums → Moons as-is (x / z clamped). The pasted cube keeps instrument, voice, durations, stickers and settings (new id, seed, no
twin link); the "next grid" = the next column's island nearest in z of the same family (like CarryTargets' first hop); PasteOn → one History
entry, the magic flight (Magic.PasteFlight: ghost beads arc from the source cube's beads to the target tiles and land, the new cube materialises
as they land), and it plays at once when the song is playing. The old stamp (StampTo / StampToAll) now pastes ADAPTED too.

5.4 Carried cubes (2.4) — the play-time half of "transcribed to the next chord … recognizable when played":
- AudioCube.TileAt(w, node) for carried windows returns the ADAPTED tiles (chained: hop k adapts from hop k − 1 so the line moves smoothly;
  cached per window, recomputed when the path, the windows or a chord change); TilesOn(island) returns them.
- The cube itself TRAVELS: when its next window is on another island it FLIES there along Magic.ArcPoint (height Magic.ArcHeight) with a
  Magic.Trail, timed to land on the target's first note (a flight of ≈ 0.5 beat ending at the window start; never late for the audio: audio is
  scheduled from the windows as today, only the body flies), lands with Magic.Land; after its last carried window it flies home the same way
  (before its home window begins again / at the loop). A pure function of the song beat like every cube pose (seeks / loops land right).
  Riders keep their v5 motion.
- Tests keep late / errors 0: flights are visual only.

5.5 Louder presses: every press / key click goes through Synth.PressNote (A) with the pressed tile's pitch (VoiceRules.Fold, register);
TileInteraction gains the press look (sink + spring, a ring).

5.6 Tests: Tests/V6ChecksH.cs (RunAll → Captures/h6_report.txt, captures h6_*.png): empty-hand click creates no cube (cube count, History
unchanged) and presses (PressNote count, a loud event), drag moves the island, pick up → click draws, the cube stays in the hand after finishing,
Esc puts it down; keyboard: clicks add single keys, repeat node, drag glissando, MelodyLine points; copy / paste: pasted pitches = Harmony's
answer for 4 cases (grid → grid, bass rule, keyboard → keyboard, grid → keyboard), PasteNext target, one History entry, undo; carried windows'
tiles adapted (pitch = expected) and a carried cube mid-flight between islands (position between the two, above both), back home after;
Synth late / errors 0.

## 6. W — world magic (look)

6.1 The tower (reads KeyBlock.TowerLift / TowerState / register): as a raised island rises on its turn, a PILLAR grows out of the sea (y = −4)
up to the platform's underside — stacked ink-outlined stone drums (one band per octave), a foam skirt at the waterline; WAVES come out from
underneath: 2–3 expanding rings on the sea at the start of the rise + a splash of droplets, a ring on every downbeat while it is up, one slow wide
ring as it sinks; the pillar shrinks back as it sinks (at rest a raised island shows only a short stub under it, the hint that it will rise). A
lowered island: a whirl of inward ripples as it dips. Everything in the v4 look (flat colours, ink outlines, drawn on twos where it helps).

6.2 The carry bridge — "on the UI there should be something (similar to the conveyor belt's vibe) that makes it look unique when played and
recognizable" and "when they do this is should look like magic":
- At rest (carry ≥ 1): a SIGIL — a ring of small glowing runes — around the source island's hub / platform edge in its chord colour; from it a
  dotted, softly shimmering ARC of light (Magic.ArcPoint between platform centres, a little above the islands) to each target (SongManager.
  CarryTargets), each target wearing a smaller matching sigil and tiny GHOST STARS on the tiles the carried pattern plays there
  (AudioCube.TilesOn(target) of the source's cubes). Chains read as one spell (source → 1 → 2 → 3).
- Playing: at each chord change onto a target a pulse of light runs along its arc with the flying cube(s) (H flies them; you draw a bright comet
  trail = Magic.Trail), the target's sigil flares, its ghost stars ignite into the pattern's colour as the notes land (Magic.Land), then fade.
- The gesture (SongManager.OnCarryChanged): raising carry casts a SPELL — the sigil draws itself rune by rune, the arc shoots out to the new
  target like a thrown streamer (≈ 0.6 s), ghost copies of the pattern's beads fly along it and land on the target tiles in sparkles, the target
  flashes its chord colour; lowering it: the last arc retracts and fizzles into sparks.
- Components: new DeaCube/CarryBridge.cs (+ Tower.cs) managed by one WorldMagic MonoBehaviour you create at runtime (RuntimeInitializeOnLoadMethod)
  that follows SongManager (OnSongRebuilt, OnColumnsChanged, OnCarryChanged) — K's files are not yours: no KeyBlock edits.

6.3 Paste flight (Magic.PasteFlight) and the shared magic (Magic.Trail / Land): sparkle trails (additive quads drawn on twos, star shapes), a
landing burst; the paste: beads arc one after another (a quick stagger) and land. Fx.cs is yours for helpers (keep its public members).

6.4 Tests: Tests/V6ChecksW.cs (captures w6_*.png of each look: tower rising / up / sinking, pillar height vs lift, waves spawned; a carry chain at
rest, mid-flight, the spell mid-cast, a retract; a paste flight) + numbers (pillar top = platform underside ± 0.1, ring count per rise, arcs =
targets, ghost stars = TilesOn counts), no errors, frame time sane (≤ +1 ms per carried island).

## 7. U1 — cards & islands UI

7.1 Cards (IslandTray): every chord card shows — besides its vibe — its JOB NUMBER (Harmony.Job) as a badge (the number on a family shape: 1 a
little house, 2 a flag, 3 a heart, 4 a curling arrow home — ink on cream, family-coloured; a public `JobBadge` helper (new DeaCube/JobBadge.cs)
that K and the rail reuse), and a STAR when it fits the reference pattern (Harmony.Fit ≥ StarThreshold; the best card's star twinkles).
Reference: the pattern in the hand (Clipboard) → else the focused island's notes (IslandHeader.Current / the selected cube's island / the lit
island while playing / the last island; Harmony.NotesOfIsland) → else (no notes) the stars mark the two best next chords after the focused chord
(MusicTheory.SuggestNext). Hover caption: "<number> · <job word>" + "swaps with any <number>" + the vibe word; while a card is hovered, the
islands in the world with the same job show a small ⇄ swap chip (screen-space over them) and their rail beads pulse. Stars / badges refresh when
the reference changes.
7.2 The tray tools gain a KEYBOARD card (a little piano): click adds a keyboard island to the focused island's column (SongManager.
AddKeyboardIsland), drag places it like a card (as the Moon card does).
7.3 The island header: a CARRY button (a wand throwing an arc; a ×1 / ×2 / ×3 sticker, plain at 0) cycling 0 → 1 → 2 → 3 → 0 through
SongManager.SetCarry, caption "carry to next chord"; keyboard islands get grip, eye, register, repeat, carry, duplicate, more (delete) — no chord
wheel / weather; when the clipboard holds a pattern, a "paste here" item (Clipboard.PasteOn(this island)).
7.4 The column rail (HudColumnRail): each bead shows its column's job number (the badge, tiny), a small arc between beads where a carry passes,
keyboard columns a piano-key bead.
7.5 Tutorial / hints (Onboarding, Hints, TutorialBubble): the drawing step now says to pick a cube first ("pick a cube, then click tiles");
one light tip each for: clicking a tile just plays it, copy / paste, carry, the keyboard island, numbers & stars ("same number, same job: swap
them"), the tower ("raise a grid: it rises on its turn"). v4 tutorial voice, short.
7.6 Tests: Tests/V6ChecksU1.cs + your older suites (V5ChecksU, V4ChecksU1, V3ChecksD) green.

## 8. U2 — hand & instruments UI

8.1 The instrument column (HudInstruments) = the ten GROUPS: a chip click PICKS UP that cube (PathManager.PickUpCube; the chip lifts out of the
column "in the hand": raised, a hard shadow, a wobble) and a second click puts it down; hovering a chip for ≈ 0.25 s fans its VOICES out to the right
(one small chip per voice, the group colour with a variant mark, hover = audition + caption = the voice name, click = pick up that voice:
Instruments.BrushVoice); the held voice shows on the chip (a small dot pattern / sticker). Mix mode unchanged (group volume / mute).
8.2 The cursor (CursorKit): a cube in the hand = a small cube of that colour riding the cursor (tilted, bobbing); a pattern in the hand = a tiny
pattern card (dots + line in the instrument colour); the empty hand = today's cursors (a pointing hand over tiles says "press to hear").
8.3 The cube card (InspectorCard / CubeInspector): COPY (Clipboard.Copy), PASTE → NEXT GRID (Clipboard.PasteNext — one click, the magic flight)
and the VOICE picker (the group's voices as small chips; changing it re-sounds the cube, one History entry) — icons first, captions on hover.
8.4 The clipboard chip (new DeaCube/Ink/Hud/HudClipboard.cs): appears after a copy (bottom-right, above the transport): the pattern drawn as dots
+ a line in its colour (a mini melody line), its instrument glyph; click = pick up the pattern (paste mode), drag onto a grid = paste there, × =
clear. Hidden while presenting / in menus.
8.5 Hotkeys (UIManager): Cmd+C copies the selected / hovered cube, Cmd+V pastes onto the hovered grid (else the next grid after the copied
cube's home), 1–9 / 0 pick up the group cubes (a second press puts down), Esc puts the hand down (after the existing Esc owners).
8.6 Tests: Tests/V6ChecksU2.cs + V4ChecksU2 green.

## 9. Ownership (edit only your files; read anything)

| pkg | files |
|---|---|
| K | SongManager.cs, KeyBlock.cs, ProjectConfig.cs, OrbitCamera.cs, DeaCube/IslandDrag.cs, IslandGhost.cs, ColumnBands.cs, Route.cs, Comet.cs, MeshFactory.cs, Belt.cs, Tests/V6ChecksK.cs, Tests/V5ChecksK.cs, Tests/V4ChecksK.cs, Tests/V3ChecksB.cs |
| A | DeaCube/Audio/** (SynthEngine, SynthBank, MeltySynth), DeaCube/VoiceRules.cs, DeaCube/Palette.cs, GlobalClock.cs, SequenceMaster.cs, Tests/V6ChecksA.cs, Tests/V2Checks.cs, Tests/WpAChecks.cs |
| H | PathManager.cs, TileInteraction.cs, AudioCube.cs, DeaCube/Clipboard.cs, MelodyLine.cs, PathGridView.cs, DurationPicker.cs, FocusLoop.cs, CubeOutline.cs, Tests/V6ChecksH.cs, Tests/V5ChecksR.cs, Tests/V4ChecksR.cs |
| W | new DeaCube/Tower.cs, new DeaCube/CarryBridge.cs, new DeaCube/WorldMagic.cs, DeaCube/Magic.cs, DeaCube/Fx.cs, DeaCube/Look/**, Tests/V6ChecksW.cs |
| U1 | DeaCube/IslandTray.cs, IslandHeader.cs, Ink/Hud/HudColumnRail.cs, Vibe.cs, VibeGlyphs.cs, Onboarding.cs, Hints.cs, Menu/TutorialBubble.cs, new DeaCube/JobBadge.cs, Tests/V6ChecksU1.cs, Tests/V5ChecksU.cs, Tests/V4ChecksU1.cs, Tests/V3ChecksD.cs |
| U2 | Ink/Hud/HudInstruments.cs, HudKit.cs, HudTransport.cs, HudMenuStrip.cs, HudPresent.cs, new Ink/Hud/HudClipboard.cs, DeaCube/CursorKit.cs, InspectorCard.cs, CubeInspector.cs, UIManager.cs, InterfaceController.cs, Tests/V6ChecksU2.cs, Tests/V4ChecksU2.cs |
| orchestrator | everything else (Harmony.cs, SongState.cs, the menu / gallery / presentation files, V6Integration / V6Suites, older suites not listed, README, docs) |

Changes needed in another package's files were routed through the orchestrator.

## 10. Acceptance (orchestrator, at the end)

Every older suite green (with AutoHand for the pre-v6 suites; updated assertions explained), every V6Checks* green, V6Integration: a fresh song →
the empty hand clicks tiles (no cube, a loud press over the playing song) → pick up a cube, draw, it stays in the hand, put it down → copy → paste
onto the next grid (adapted pitches, the flight) → carry a grid ×2 (the spell, windows, flights at the chord changes, adapted pitches) → a
keyboard island: a melody by clicks (single keys, a repeat), the stars on the cards for that melody, the job numbers → raise a grid (the tower on
its turn, the slow sink) → pick a voice from a group (the sound changes) → present → save / load round trip (kind 2, carry, voice) → Synth late /
errors 0; the user's save md5 ef5c7a1e0808fe6fda3efb7b81befa3f unchanged.

## 11. Moons follow the song (added mid-build — the user: "also, the percussion circle should stay in line with the current part of the song -
that way you should be able to create different percussions for different parts of the song (or even no percussion) by having them at
different points and following until the next")

11.1 Model: every Moon (the drum "percussion circle") has a START COLUMN (MeasureState.col / KeyBlock.column for Moons; -1 in older files = 0).
A Moon plays from its start column's start until the next LATER start column of any Moon (or the song end): its SECTION. Moons sharing a start
column play together (layers). Columns before the first Moon's start have no percussion, and a Moon with no cubes makes its section silent ("no
percussion" there). Old songs: every Moon starts at column 0 → their sound is unchanged.
11.2 Timeline (K): a Moon cube gets one window per bar of its Moon's SECTION (not of the whole song); passes / repeats inside the section count
(a section covers whole columns, all their passes); focus loop / seek / loop as today. API: `SongManager.MoonStartColumn(moon)`,
`SongManager.MoonSection(moon, out startBeat, out endBeat)`, `SongManager.SetMoonColumn(moon, col)` (one History entry),
`SongManager.MoonsAt(beat, list)` (the Moons playing at a beat).
11.3 Place & look (K): stopped, a Moon sits IN LINE with its start column (x centred on that column; in a drum lane in front of the islands —
never overlapping them; its z as the player left it when that fits). Dragging a Moon left / right snaps its start to the column it is in line
with (the column band lights; one History entry per drop). While playing, the Moon whose section is playing FOLLOWS the song: it glides to stay in
line with the lit column (≈ 0.75 beat, arriving as each column starts; its cubes ride along like on a belt), and when its section ends it glides
back to its start column — a pure function of the song beat (seeks / loops / the focus loop land right). A faint dotted track on the sea marks
each Moon's section (its start column → the next Moon's start) so the player sees where each percussion part begins and ends.
11.4 Adding Moons: MaxMoons raised (≈ 8). `SongManager.AddMoon()` keeps its old behaviour (start column 0) for older callers / tests; new
`AddMoon(int startColumn)`; the tray's Moon card (U1) and the N key (U2, UIManager) add at the FOCUSED column (the lit column while playing,
else the focused / selected island's column, else 0); dragging the Moon card onto a column places it there.
11.5 UI (U1): the rail shows the drum sections (a small drum mark on the bead where a Moon starts, a thin line under the beads it covers, a gap
where there is no percussion). The Moon header (grip / dice / more) is unchanged.
11.6 Tests (K, V6ChecksK): two Moons at columns 0 and 2 → windows per bar only inside each section; one Moon at column 1 only → no drum events in
column 0; the follow pose (x in line with the lit column mid-section, back at its start after its section); a drag snaps the start column (one
History entry); an old song (every Moon at 0) → exactly the v5 windows (one per bar of the song).
