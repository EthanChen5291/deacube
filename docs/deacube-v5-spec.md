# DeaCube v5 — SPEC (single source of truth for the v5 builders)

Project: AudioCube_Unity (Unity 6000.4 URP). v4 is built, uncommitted and green (V4Suites.RunAll ≈ 594
checks). v4 spec: docs/deacube-v4-spec.md, UI language: docs/deacube-v4-ui.md (Melatonin × Spider-Verse
ink: paper cream, ink outlines, things drawn on twos, lowercase captions, icons first, words minimal).

## 0. The user's asks (verbatim, 2026-09-30)

1. "I want you to generate a song as a demo. consult the claudesune miku claudes if you can, if not go through the .midi files provided that are
   japanese and learn from them and make a demo song level. add a new section called gallery with these song demos that basically imitate them
   using the instruments and chord progressions we have (so adapted)"
2. "also, you should be able to drag grids to the left and right too not just up and down so if i want to move a grid to an earlier section i
   can do that"  (a "grid" = an island)
3. "also when im selecting notes on the grid i genuinely have no idea what the next note sounds like -> and the shapes are very ambiguous and i
   have no idea whats going on."
4. "same for the random colored cards -> i like the idea of cards but i think rather than random we should assign some sort of meaning to them
   so that nonmusical users can remember certain cards or shapes and stuff in terms of their vibe"
5. "additionally, you can set certain grids to repeat -> and what this does it establishes almost like a track that the grid moves along (like
   when you're at walmart and you're placing things on the thing that moves the items closer to the cashier) except it's not constant speed and
   does it every repeat -> it should be a straight line so the grids on the next measure will have to make space. if you set all the grids on a
   certain measure to repeat then it just repeats twice."
6. "also the falling cubes should be quicker and the camera quicker too - it should seem like actual falling and the camera should move a little
   bit rather than stay constant"

"Claudesune Miku" = the user's own Claude skill in ~/Documents/GitHub/motif-engine (SKILL.md: a Strudel game/Vocaloid music engine whose rules
were learned from the user's keep/kill verdicts). No Claudesune Miku session is running, so we consult its knowledge instead: SKILL.md, its
memory (the Claude project memory of the motif-engine repo), its corpus read-outs on the dev branch
(`git -C ~/Documents/GitHub/motif-engine show dev:research/vocaloid-r35.md`, `dev:research/vocaloid-r38.md`) and the per-song analysis
~/Documents/GitHub/motif-engine/audios/vocaloid-r35/_analysis-r38.json (37 Japanese songs: bpm, key, verse / chorus loops, sections, chorus
cell, doubling ...), plus the MIDI files themselves in that folder. READ ONLY: never write into the motif-engine repo, never copy its files
into this repo (the user's rule: no file copies between his repos).

## 1. Packages (parallel, one working tree, one Unity editor — protocol: scratchpad/v5/PROTOCOL.md)

| pkg | what |
|---|---|
| G | gallery songs: 6 demo "levels" imitating songs of the Japanese corpus in DeaCube's vocabulary; the index; a reproducible builder |
| T | the title menu's new "gallery" section: browse, listen, open a demo (comic exit into the world, playing) |
| K | columns: free island drag (left / right into other columns or a new column, as well as up / down); the repeat conveyor belt; the song key |
| R | hearing notes: hover audition everywhere + "hear the run a click adds"; the tiles lose the ambiguous role shapes; pitch reads as height and brightness; the draft's melody line; drum pictograms |
| U | vibe cards (the deck) and the HUD: vibe glyphs, the island header's repeat control, the rail, the cursor, the tutorial |
| P | presentation: quicker falls, a living camera |

## 2. Shared contract (M0 — landed by the orchestrator before the builders start; keep every public member)

2.1 Song key (SongState v4). `SongState.CurrentVersion = 4`; new fields `keyTonic` (-1 = unknown: derive from the first island on load, then keep)
and `keyMinor`. `SongManager.SongKey` (MusicTheory.SongKey) is set by BeginSong (from the generated song's first chord) and RebuildFromState
(from the file, else derived from measures[0] after normalisation), and saved by SongState.Capture. `MusicTheory.KeyOfSong()` returns it (so
moving islands never changes the key). The key's tonic chord is the song's "home".

2.2 Vibe language (DeaCube/Vibe.cs; U may refine colours / glyph art / words, never the API or the mapping):
- `enum VibeKind { Sunny, Dreamy, Rainy, Moonlit, Stormy, Spicy, Floaty }` = the chord quality read as weather, extending the game's existing
  mood / climate families (sun = major, cloud = minor, storm = dominant): Major7 → Sunny (bright, happy), Major9 → Dreamy (soft glow),
  Minor7 → Rainy (sad, tender), Minor9 → Moonlit (wistful, pretty), Dominant7 → Stormy (tense: wants to go home), Dominant9 → Spicy (funky
  tension), Sus4 → Floaty (hanging, unresolved).
- `Vibe.Of(ChordQuality)`, `Vibe.Of(IList<int> semis)`, `Vibe.ColorOf(VibeKind)` (the family colour), `Vibe.ChordColor(root, semis)` (family
  colour with a tiny shade by root), `Vibe.Icon(VibeKind)` (IconFactory glyph name; falls back to an existing glyph until U registers the
  art), `Vibe.Word(VibeKind)` (lowercase caption: "sunny" …), `Vibe.IsHome(rootMidi)` (root pitch class = the song key's tonic),
  `Vibe.HomeIcon` ("home").
- `MusicTheory.ChordColor` now returns `Vibe.ChordColor` (islands, tiles, cards, rail beads, header tails, presentation tiles all follow);
  `MusicTheory.QualityIcon` returns the vibe glyph (island platform glyph, card emblem, chord wheel).

2.3 Gallery (DeaCube/Gallery.cs; T may extend): `Resources/Gallery/index.json` = `{"songs":[GalleryEntry...]}`, GalleryEntry { id, title,
after, vibe (VibeKind int), bpm, bars, file (Resources path without extension, e.g. "Gallery/sugar-rush"), blurb }. `Gallery.Entries`,
`Gallery.Find(id)`, `Gallery.LoadState(id)` (a fresh SongState parsed from the TextAsset, null on failure), `Gallery.Open(id, play)` (backs up
a song with cubes (SongIO.Backup), applies the state, History reset + push, playing from bar 0 when play; `Gallery.CurrentId`,
`Gallery.OnOpened`), `Gallery.Reload()`. A gallery song is a normal SongState JSON (version 4, keyTonic set, placed islands or not — the
normalisation lays columns out).

2.4 Pitch look (DeaCube/PitchLook.cs, owned by R): `PitchLook.Rise` (tile lift per semitone, world units; was ProjectConfig.PitchRise 0.045)
and `PitchLook.TileColor(Color tint, float t01)` (face colour by pitch rank 0 = lowest … 1 = highest). KeyBlock.Build reads both (M0 edit).

2.5 Repeat = passes on a conveyor belt (K implements; everyone else just reads the timeline):
- `MeasureState.repeat` / `KeyBlock.repeat` 1..4 = how many PASSES the island plays. Column c: `SongManager.ColumnPasses(c)` = the max repeat
  of its islands; `SongManager.PassLength(c)` = bars × beatsPerBar; `ColumnLength(c)` = PassLength × ColumnPasses (the whole belt time; the
  song timeline, the comet, the hub gauge, the focus loop and the presentation all use it). Island i plays passes 0 .. repeat_i − 1, one
  AudioCube window per pass (start = ColumnStart + p × PassLength, length = PassLength); an island with fewer passes than its column rests for
  the remaining passes (no window). All islands of a column at repeat 2 = the column simply plays twice.
- Look: an island with repeat ≥ 2 stands at the head of a straight conveyor belt laid toward +x (the time direction) with one slot per pass
  (slot pitch = island width + a small gap); the column's width in the layout includes its longest belt, so the next column moves right to
  make space (and back when the repeat is lowered). While playing, at each pass boundary the belt jerks the island forward one slot — not a
  constant speed: an ease with a start and a settling clunk, the belt's rollers turning only while it moves — so pass p plays at slot p; after
  its last pass it glides back to slot 0 (while the next column plays, or at the loop). Stopped / editing: the island rests at slot 0. The
  position is a pure function of the song beat (seeks, loops and the focus loop follow). Tiles and cubes ride with the island.
- Control: the island header gets a repeat button (U): click cycles ×1 → ×2 → ×3 → ×4 → ×1 through `SongManager.SetRepeat` (exists; pushes
  History). The belt grows / shrinks with an animation; the neighbours slide.

2.6 Free island drag (K): grabbing an island's ground (the move cursor, PathManager.HoverGround) or its header grip moves it in x and z (lifted,
following the pointer). The drop decides: centre over a column's x span → it JOINS that column at that z (the column's others pushed away as
today; bars equalised to the column's max, as NormalizeColumns does); in the gap between two columns, before the first or after the last →
it becomes a NEW column there (the neighbours part to show the slot while hovering); the v3 merge magnet (its edge near another island's outer
end in the same lane) still merges. Its own column: today's in-column move. `SongManager.MoveIslandToColumn(int island, int targetCol, bool
asNewColumn, float pz)` returns the island's new index (-1 refused). One History entry per drop; Esc / right-click cancels; while playing the
lit column keeps its local beat (RebuildKeepingLit). ColumnBands shows the target (the column lit, or an insertion slit between columns). The
hub drag (whole column) stays as in v4.

## 3. G — gallery songs (content)

Goal: SIX demo songs ("levels") that imitate songs of the Japanese MIDI corpus using DeaCube's instruments and chord vocabulary, each opening
from the gallery as a complete, playing song that shows off the world (stacked columns, registers, note lengths, repeat belts, Moons).
- Pick six DIFFERENT style families from the corpus (for example: a bright in-love pop-rock (Melt-like), a wa-rock festival (Senbonzakura-like),
  a frantic minor rock (Rolling Girl / Ghost Rule-like), a piano-driven rushing J-pop (Yoru ni Kakeru-like), a calm ballad (the user's liked
  calm setup — see memory calm-references.md: no drums, no bass, flowing piano arpeggio, soft pad, strings across the tune, sevenths
  everywhere), and one playful dance / polka (Ievan Polkka / Binomi-like)). Measure each chosen source (its _analysis-r38.json entry AND the
  MIDI: tempo, key, the verse / chorus progression, the groove of its accompaniment and bass, its melody's rhythm density and contour).
- Borrow: the chord progression (each chord mapped to root + one of maj7 / maj9 / m7 / m9 / 7 / 9 / sus4; triads take the family seventh,
  "sus" → sus4, "ø" → m7, "°" → 7 on the next root down a major third or the closest dominant), the tempo (keep the original bpm when DeaCube
  plays it cleanly, else half-time feel), the section form (intro → verse → pre → chorus, 16–32 bars), the energy arc (verse thin, chorus thick),
  the instrumentation roles, the groove. Melodies are ORIGINAL — written to the corpus's laws (research/vocaloid-r35.md "twelve laws": 8ths,
  steps and repeats, short phrases with 8th breaths, pickups off the downbeat, 2-bar cells that return verbatim, range ~58–80, the chorus lifted
  3–5 semitones with the accompaniment doubling it an octave up, verse thin / chorus thick) — never a transcription of the original's tune (the
  gallery ships inside the repo). Name each song originally (lowercase, 1–2 words); `after` = "<original title> · <producer>".
- Craft rules from Claudesune Miku's SKILL.md that apply: support under the lead (ratios), sparsity 4–5 parts, colour chords everywhere,
  sections own their harmony, the chorus is a register and a texture not a speed, uniform anything reads as an exercise (vary velocity via
  accent / ghost stickers, vary cells).
- DeaCube vocabulary (read the code, do not guess): SongState.cs (format), KeyBlock.Build (tile pitch = root + inversion_x[row] + 12 × register;
  6 inversion columns × chord-tone rows), AudioCube durations engine (durs per node, 24 ticks = 1 beat; the path loops inside each window),
  VoiceRules (Fold into each slot's range, mods: 1 rest, 2 ghost, 3 accent, 7 tie, 8 lift = +12; follow = shadow chord tones; echo; rider =
  replays the path on every column), Instruments (0 keys, 1 pluck, 2 pad, 3 lead, 4 bass, 5 bells, 6 strings, 7 choir, 8 piano, 9 drums),
  Moons (drum islands, kit rows via SynthBank.DrumForRow), island energy / fill, climate, swing, tone / space. A melody lane may sit on its own
  stacked island whose chord is chosen to contain the melody's pitches (islands make no sound by themselves), e.g. a 9th chord for stepwise
  lines; repeated pitches can use the anti-diagonal twin tile (same pitch) so the cube still moves.
- Deliverables: tools/gallery/ (python3 stdlib: the song sheets as data + build_gallery.py that writes
  AudioCube_Unity/Assets/Resources/Gallery/<id>.json and index.json, and a --verify pass: every node on its island grid, pitch ranges, durations);
  docs/deacube-gallery.md (per song: what it borrows from which source, measured numbers, what is original);
  Tests/V5ChecksG.cs (each entry loads, opens, plays ≥ 8 bars with Synth late / errors 0, all cubes finalized, no console errors; one capture
  per song; a 12 s WAV per song for the orchestrator's analysis).

## 4. T — the gallery in the title menu

- A new lowercase word "gallery" in the Melatonin text menu (between "new song" and "learn"; keyboard ↑ ↓ Enter as today).
- Choosing it swaps the word list for the gallery shelf, same ink / paper language: one card per Gallery entry (the song's vibe colour and vibe
  glyph, its lowercase title, tempo digits allowed, "after …" in small ink on hover), ← back (Esc too). Hover (or focus) a card: its hook plays
  as a preview through the synth (the first bars of its lead + bass + drums, derived from its SongState; the menu music ducks / stops while it
  plays); the wall behind reacts (its colour, a bounce on the preview's beat).
- Click / Enter: Gallery.Open(id) (backs up a song with cubes first; never touches the user's main save), then the v4 comic exit into the world,
  the song playing from bar 0, the camera framing the song; the autosave then holds it (Continue resumes it).
- Tests/V5ChecksT.cs (menu word present, shelf opens with N cards, back / Esc, preview sounds (notes scheduled) and stops on leave, open loads
  the right song and it plays, the user's save md5 unchanged, the backup made when the replaced song had cubes, captures).

## 5. K — columns: free drag, repeat belt, key

- 2.6 free drag (IslandDrag, SongManager.MoveIslandToColumn, ColumnBands feedback). Moving an island out of a stacked column promotes the next
  island to anchor; a column left empty disappears (the later columns slide left); joining a column with a different length equalises bars to
  the max (nothing is cut). Keep: in-column move, hub (whole column) drag, merge / split, push-out, Esc cancel, the tests of v4 (update only
  assertions whose meaning changed, and say so).
- 2.5 repeat: timeline (RecomputeMeasureStarts: passes, windows per pass, ColumnPasses / PassLength), layout (column widths include belts;
  NormalizeColumns and the live relayout), the belt (a new DeaCube/Belt.cs or inside KeyBlock: ink-style rubber belt with rollers and slot
  marks, rollers turning only while it moves), the ride (jerk per pass: ease-out with a small settle, ≈0.35 beat, rollers spin with it; glide back
  after the last pass), the Route cable / comet / hub gauge following the moving anchor, FitColumnToCube untouched (it grows bars, not passes).
- 2.1 key: SongManager.SongKey (M0 sets it; keep it right through every op: moving islands never changes it). Island platform: the quality glyph
  shows the vibe glyph (automatic via QualityIcon) and the home island (Vibe.IsHome) wears a small home glyph beside it.
- Tests/V5ChecksK.cs.

## 6. R — hearing notes, tiles that make sense

- Hover audition EVERYWHERE: hovering a free tile of an island (idle, not only while drafting) plays its note with the brush instrument, once per
  entry, at most ~8 per second, soft but clearly audible; stopped → at once; playing → on the next 16th, and ALWAYS audible (v4's harmonic
  safety silenced tiles of an island that was not lit — that is why the user "has no idea what the next note sounds like"): while the song plays
  the audition ducks the song's own voices briefly (≈ −8 dB for ≈ 0.4 s) so the note is heard.
- Drafting: hovering a tile that a click would add as a RUN (the grid line of tiles) auditions the whole run as a quick arpeggio in order (what
  you hear = what a click adds); the hologram peeks toward it.
- Tiles lose the chord-role shapes (dot / triangle / square / star / ring mean nothing to a non-musician). Pitch reads as HEIGHT (a clearer
  staircase: PitchLook.Rise) and BRIGHTNESS (low = deeper, high = lighter: PitchLook.TileColor); nothing else is printed on a chord tile.
  Moon (drum) tiles get recognisable pictograms (kick drum, snare, closed hat, open hat, clap / tom …) instead of dot / square / sparkle.
  PathGridView (inspector grid) follows (it reads TileInteraction.MarkerIcon / BaseColor).
- The draft's MELODY LINE: a small ink contour above the island while drafting (x = time by the notes' lengths, y = pitch): the drafted notes as
  dots joined by a line, the hovered candidate as a dashed ghost extension (up = higher) — the shape of the tune at a glance.
- Tests/V5ChecksR.cs (audition counts idle / drafting / playing on a non-lit island, duck applied and released, run arpeggio = the line's tiles in
  order, no role markers on chord tiles, kit pictograms, melody line points = nodes, Synth late / errors 0).

## 7. U — vibe cards and the HUD

- Cards (IslandTray): every card reads as its vibe — the vibe colour, the vibe glyph large, the island art small, a home sticker on the key's
  home chord, hover caption = the vibe word ("sunny", "rainy" …) — so a player remembers "the storm card makes it tense, the sun card is home".
  The Key / Colour stacks and the tools stay. Register the vibe glyph art (sun, dreamy sun, rain cloud, moon, storm cloud with a bolt, flame,
  feather) in your own file through IconFactory.Register and make Vibe.Icon return them.
- The island header: a repeat button (a belt / loop glyph with a ×n sticker; ×1 plain) cycling 1 → 2 → 3 → 4 → 1 (SetRepeat), a hover caption
  ("repeat"); the grip / move cursor now shows it moves anywhere (4-way); the rail's beads widen with passes and show the belt.
- Tutorial / hints: one light step or tip about the vibe cards ("each card has a weather: sun feels like home, storms want to go home"), one
  about dragging an island anywhere and one about repeat, in the v4 tutorial voice (sentences allowed there).
- Tests/V5ChecksU.cs.

## 8. P — quicker falls, a living camera

- Quicker: DepthPerBeat up (≈ 3.5 → 6, tuned by eye), throws / drops / gravity scaled so strikes stay on the beat and read as FALLING
  (accelerating) rather than floating.
- The camera stops being a constant-speed elevator: it chases the falling action with a spring (lags on a burst of strikes, catches up), leans a
  little toward the busiest lanes, rolls a few degrees on big strikes, gets a short kick on accented strikes near the frame centre, and its FOV
  breathes a little with speed. Deterministic (a function of the song beat + seeds) and smooth across loops and seeks.
- Tests/V5ChecksP.cs (strike timing still on the beat, descent speed, camera offset / roll non-constant but bounded, no late notes).

## 9. Ownership (edit only your files; read anything)

| pkg | files |
|---|---|
| G | tools/gallery/** (repo root), Assets/Resources/Gallery/**, docs/deacube-gallery.md, Tests/V5ChecksG.cs |
| T | DeaCube/MainMenu.cs, DeaCube/Menu/** (new MenuGallery.cs …), DeaCube/Gallery.cs (after M0), Menu shaders, Tests/V5ChecksT.cs |
| K | SongManager.cs, KeyBlock.cs, ProjectConfig.cs, OrbitCamera.cs, DeaCube/IslandDrag.cs, IslandGhost.cs, ColumnBands.cs, Route.cs, Comet.cs, MeshFactory.cs, new DeaCube/Belt.cs, Tests/V5ChecksK.cs, Tests/V4ChecksK.cs, Tests/V3ChecksB.cs |
| R | PathManager.cs, TileInteraction.cs, AudioCube.cs, GlobalClock.cs, SequenceMaster.cs, DeaCube/VoiceRules.cs, FocusLoop.cs, PitchLook.cs, DurationPicker.cs, PathGridView.cs, DeaCube/Audio/**, new DeaCube/MelodyLine.cs, Tests/V5ChecksR.cs, Tests/V4ChecksR.cs, Tests/V2Checks.cs, Tests/WpAChecks.cs |
| U | DeaCube/IslandTray.cs, IslandHeader.cs, CursorKit.cs, Onboarding.cs, Menu/TutorialBubble.cs, Hints.cs, Palette.cs, Vibe.cs (after M0), new DeaCube/VibeGlyphs.cs, Ink/Hud/**, UIManager.cs, InterfaceController.cs, Tests/V5ChecksU.cs, Tests/V4ChecksU1.cs, Tests/V3ChecksD.cs |
| P | DeaCube/Present/**, DeaCube/Presenter.cs, DeaCube_PresentBackdrop.shader, Tests/V5ChecksP.cs, Tests/V4ChecksP.cs, Tests/V3ChecksC.cs |
| orchestrator | everything else (M0, the integration suites V5Integration / V5Suites, older suites not listed above, README, docs/deacube-v5-spec.md) |

Note: Menu/TutorialBubble.cs is U's (not T's). A needed change in a file you do not own → scratchpad/v5/requests_<you>.md (the orchestrator
routes it).

## 10. Acceptance (orchestrator, at the end)

Every v4 suite stays green (updated assertions explained), every V5Checks* green, V5Integration: menu → gallery → open a demo (plays, columns,
belts ride on the beat) → drag an island to an earlier column and into a new column (one History entry each, playing on) → set repeat on one
island (the next column makes space; the belt jerks per pass; all islands at ×2 = the column plays twice) → hover tiles idle / drafting / playing
(auditions audible, duck) → the deck's vibe cards → present (quicker, camera alive) → Home / Continue restores it; Synth late / errors 0; the
user's save md5 ef5c7a1e0808fe6fda3efb7b81befa3f unchanged.
