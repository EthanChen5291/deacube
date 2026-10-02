# DeaCube gallery builder

The demo songs of the title menu's gallery are written here as data and built into `AudioCube_Unity/Assets/Resources/Gallery/<id>.json`
+ `index.json` (SongState JSON, what Unity's JsonUtility reads). Python 3 standard library only. The design notes per song:
`docs/deacube-gallery.md`. Two kinds of songs:

* the six "levels" imitating songs of the Japanese MIDI corpus, written as song SHEETS (`sheets/<id>.py`, SongState v4: chord islands);
* v9: songs built from a MIDI file (`imports/<name>.py` + `midi_grid.py`): "get proto", the user's own `sample.mid`, built the way a
  player builds a song: a chord card per bar, a lane of chord grids per voice, paths over the chord's tiles, the devices for its moments
  (stairs on every falling measure, launches on the crash bars, belts / rewind and vary, long grids and a pedal, nudges, lead keyboards,
  follow triads, octave copies; SongState v6). Round 1 (`midi_song.py`, keyboards playing the
  MIDI note for note) was replaced; its MIDI reader, paths, velocity fit and Moons are shared.

## Rebuild

    cd tools/gallery
    python3 build_gallery.py                 # every sheet -> Resources/Gallery/<id>.json + index.json
    python3 build_gallery.py --verify        # re-read the JSON, check it against the sheets, print the music statistics
    python3 build_gallery.py --only crush --stats --table    # one song, its numbers and its columns (harmony | melody palette | voice)

Unity picks the files up on the next asset refresh; `Gallery.Reload()` re-reads the index in Play mode.

    python3 midi_grid.py get-proto            # the song's plan (columns, lanes, cards, devices) and its numbers against the MIDI
    python3 midi_grid.py get-proto --sections 2 --out draft.json   # a draft of the first sections (Play mode: V9ChecksProto.DraftShots)

For an import, `--verify` also runs its own checks: no keyboards, at most 4 lanes a column, no note landing on a tile another cube rests
on (the stack rule), every cube path walking to neighbouring tiles (spheres leap), the drums exact. The Play-mode check of what DeaCube really
plays is `Tests/V9ChecksProto.cs` (it reads the .mid itself: harmony coverage, the groove, the sax's contour, the devices).

`--verify` checks: every node on its island's grid (6 inversion columns x the chord's rows; Moons 6 x 4), every note sounding the
pitch the sheet wrote after VoiceRules' fold (the slot's register, the island's register, the bass anchor on downbeats), the lengths
(3..192 ticks, a path tiling its window), no two cubes of an island sharing a tile (the stack rule would lift one an octave), no cube
standing on one tile twice in a row (except a pumping bass), the index matching a fresh build.

## Files

| file | what |
|---|---|
| `sheets/<id>.py` | one song: the harmony (a column per chord), the voice, the lines, the kit, the mix; the source measurements in its docstring |
| `sheet.py` | the vocabulary the sheets are written in (Song / Column / Island / Cube, the note tokens, `up`, `held`) |
| `deacube.py` | DeaCube's music model mirrored from the C# (KeyBlock's pitch grid, VoiceRules.Fold, the bass anchor, shadows, pad tones, drum rows) |
| `build_gallery.py` | the builder: tile allocation, melody palettes, layout, JSON, `--verify`, statistics |
| `palettes.py` | composer's aid: which DeaCube chords a melody island could use over a harmony chord, and the exact pitches they play |
| `midi.py`, `analyze.py` | a stdlib MIDI reader and the measurements of a corpus song (tempo, key, form, chords by beat, melody / accompaniment / bass numbers) |
| `imports/<name>.py` | v9: one imported song: its MIDI file, the shift, the columns (a section each), letters and roles, each track's voice and lane, the Moons' kit |
| `midi_grid.py` | v9 rounds 2-3: a MIDI file as a DeaCube song: a card per bar, lanes per voice (front to back: the lead and every stairs, the bass lowered, the harmony, the high lane raised), cubes that walk and spheres that leap, per-step NUDGES for the notes a card has no tile for, follow triads, octave copies, lift flicker, a STAIRS on every descending measure (any voice; the engine's own run rule, at its measure), LAUNCHES on the MIDI's crash bars, belts / REWIND + vary, lead keyboards where the line is mostly off the cards, a PEDAL on a long grid, 1-2-bar fragments as their own grids, a device signature per section; the engine mirror (fold, shadows, FoldRun, AdaptPedal, Nudge.Semis, echo) and the numbers (coverage, groove, contour) |
| `midi_song.py` | v9 round 1 (replaced): keyboards playing the MIDI note for note; its MIDI reader, path, velocity and Moon helpers are midi_grid's |
| `engine.py` | v9: the keyboard / Moon rules mirrored from the C# (FoldMelody, Layered, the velocity curve, DrumPiece with a kit); the voice table read from SynthBank.cs |
| `wavstats.py` | levels of the Play-mode recordings (V5ChecksG): RMS, peak, clipping, the voice over its support |

## Writing a sheet

Notes are the pitches that SOUND (`F#4`, `Bb3`), chord degrees of the island (`1 3 5 7 9`, `8` = the octave, `'` / `,` octave marks,
placed from the cube's `base`), `r` rests, drums `K S H O C R P` (+ a loudness column 0-4); lengths `:16 :8 :8. :4 :2 :1` (24 ticks a
beat), stickers `! ? ~ ^ x2 x3` (accent, ghost, tie, lift, ratchets), a nudge `+ ++ - --` (v9: one / two scale steps of the song key
higher / lower than the tile — the written pitch still sounds; the builder picks the tile whose nudged note is that pitch); `|` checks a
bar line. A melody island's chord may be `"auto"`:
the builder picks the DeaCube chord whose tiles hold every note of that bar (preferring the harmony's own 9th), so each bar of a tune
keeps to one 7th / 9th / sus4 chord's tones (`palettes.py` shows the choices). The corpus lives in the motif-engine repository and is
read in place (`analyze.py` defaults to `~/Documents/GitHub/motif-engine/audios/vocaloid-r35`); nothing is copied from it.
