# DeaCube gallery — six demo songs after the Japanese corpus, and the user's own song

The user's ask (2026-09-30): "generate a song as a demo … consult the claudesune miku claudes if you can, if not go through the .midi
files provided that are japanese and learn from them and make a demo song level. add a new section called gallery with these song demos
that basically imitate them using the instruments and chord progressions we have (so adapted)".

Six songs ("levels") ship in `AudioCube_Unity/Assets/Resources/Gallery/` (`index.json` + one SongState v4 JSON each), built by
`tools/gallery/` from sheets (python data, see its README). Each opens from the title menu's gallery (package T) as a complete, playing
song. This page says, per song, what was taken from which source, what was measured, what is original, and how DeaCube plays it.
A seventh, **get proto** (v9, section 7), is the user's own MIDI built the way a player builds a song (a chord card per bar, a lane
per voice, nudges, a staircase on every falling measure, a launch on every crash, its own devices per section) by `tools/gallery/midi_grid.py`.

## Where the knowledge came from

"Claudesune Miku" is the user's own Claude skill (`~/Documents/GitHub/motif-engine`, read only; nothing copied into this repo). Consulted:
`SKILL.md` (the Vocaloid vocal-line table, craft rules: support under the lead, 4-5 parts, colour chords everywhere, sections own their
harmony, the chorus is a register and a texture not a speed, uniform anything reads as an exercise), `research/vocaloid-r35.md` (the twelve
laws of a sung Vocaloid line, measured on the corpus), `research/vocaloid-r38.md` (the arrangement: verse = a line, chorus = a block with
the tune on top, a bass always on 8ths, the chorus owns its progression, intros are riffs), the motif-engine memory (`calm-references.md`:
the user's liked calm setup; `ethan-motif-engine.md`: his taste — placed variation, sparsity as an ensemble shape, "energetic doesn't mean
fast notes"), the per-song read-out `audios/vocaloid-r35/_analysis-r38.json`, and the MIDI files themselves, measured again here with
`tools/gallery/analyze.py` (a stdlib MIDI reader; chords by beat from the NOTE + BASS tracks; the voice, accompaniment and bass per section).
The melodies of the corpus were measured, never copied: every tune in the gallery is original, written to the corpus's laws.

## How a song is built in DeaCube's vocabulary

- **One column per chord.** A DeaCube island holds one chord (its tiles are that chord's tones in six inversions), and a column plays all
  its islands together for 1-4 bars, so the harmony moves one chord a column. The sources change chords once a bar (Melt's chorus,
  Rolling Girl's chorus, Looking for the Moon), twice a bar (Yoru ni Kakeru, Senbonzakura's chorus) or every two bars (Rolling Girl's
  verse). The gallery keeps each source's progression in order at one chord a bar (two where the source holds a chord), so a two-chords-a-bar
  source moves at half its harmonic speed. Every chord is one of DeaCube's seven: triads take the family seventh (I / IV maj7, ii iii vi m7,
  V 7, borrowed bVI bIII maj7, bVII 7), "sus" stays sus4, and a 9th where the colour helps (the song's home chord, the melody palettes).
- **Lanes.** The ground island (front) carries the harmony: the bass and the accompaniment on its own tiles. The melody island (behind it)
  carries the voice. In five of the six choruses the voice's island stands a register higher (the platform rises: the chorus is literally
  lifted; night dash's chorus, only +5 semitones over its verse like its source, stays level), and the piano doubles the tune an octave up
  on the same island with a chord tone under each note (`follow`) — the corpus's chorus texture
  ("chords on 8ths with the tune on top", r38). The calm song has no double: a third lane carries the strings' descant instead.
- **Melody palettes.** A melody island's chord is chosen so its tiles hold that bar's notes (islands make no sound by themselves): the
  harmony's own 9th where it fits (Bm7 -> Bm9), else the DeaCube chord inside the key that holds them (a Dmaj9 palette over Bm7 gives
  D E F# over the chord). So every bar of a tune keeps to one 7th / 9th / sus4 chord's tones; steps live on the 9th chords' run
  (7 - 8 - 9 - 10), which is why the tunes sit where they do.
- **Note lengths** are cube sizes (16ths tiny ... half notes fat); **rests** are breaths (the cube waits on a tile); **repeated pitches**
  use the anti-diagonal twin tile so the cube still moves; no two cubes of an island share a tile (the stack rule would lift one an octave).
- **Repeat belts** where the source holds a chord and the music returns: crush's first verse chord (the verse cell sung twice), tumble's
  last chorus chord (sung once, then the band alone), moon tide's floating last chord, polka dots' intro vamp and chanted tag.
- **Moons** carry the kit: one bar of kick / snare / hats cubes playing all song long, shaped by each column's **energy** (1 in verses: hats
  ghosted; 2 as written; 3 in the busiest choruses: the off-beat hats doubled) and a **fill** on the last pre-chorus bar (ratchets + a
  crash on the chorus downbeat; that bar's bass is written as quarters with rests so its echoes fill the gaps). The calm song has no Moon.
- **Mix.** `instVolume` keeps support under the voice; strings whisper; `tone` / `space` per song (the calm song warmer and wetter).
- **Deck.** Each song leaves three chord cards in the tray (its other colours) to invite extending it.
- **Hook.** The index's `hook` bar (package T's menu preview) is each song's first chorus bar, belt passes counted.

## The corpus laws against the six songs

Voice statistics of the built songs (`build_gallery.py --verify`, the notes as they sound) next to the corpus medians (r35, 36 songs):
| | notes / sung bar | notes / s | 8ths / quarters / 16ths (IOI) | repeat / step / third / 4th+ | mean interval | range, median | verse -> chorus median | chorus doubled 8ve up |
|---|---|---|---|---|---|---|---|---|
| corpus (r35 medians) | 5.5 | 3.9 | 55 / 21 / 5 % | 25 / 48 / 13 / 11 % | 2.0 | 58-80, 68 | +3.1 (acc-selected) to +5.2 | 9% verse, 92% chorus |
| crush | 5.3 | 3.79 | 73% / 19% / 0% | 10% / 69% / 16% / 4% | 2.0 | 57-79, 69 | 64 -> 75 (+11) | 100% |
| petal-storm | 5.9 | 3.79 | 79% / 14% / 0% | 15% / 46% / 28% / 11% | 2.3 | 57-79, 69 | 64 -> 72 (+8) | 92% |
| tumble | 4.9 | 4.02 | 66% / 25% / 0% | 37% / 31% / 26% / 6% | 1.7 | 62-78, 71 | 69 -> 74 (+5) | 93% |
| night-dash | 6.5 | 3.52 | 81% / 12% / 3% | 12% / 56% / 30% / 2% | 2.1 | 60-77, 67 | 65 -> 70 (+5) | 90% |
| moon-tide | 4.2 | 1.17 | 48% / 36% / 0% | 7% / 54% / 31% / 8% | 2.4 | 59-74, 67 | 65 -> 70 (+5) | 18% |
| polka-dots | 6.7 | 3.32 | 74% / 14% / 9% | 25% / 53% / 19% / 2% | 1.7 | 64-76, 71 | 68 -> 71 (+3) | 78% |

Read with care: the corpus row is a median over 36 whole songs; each gallery song follows ITS source (Melt's chorus sits +10 over its
verse, so crush's does; Rolling Girl repeats notes 39% of the time, so tumble does 37%). Thirds run higher than the corpus's 13% because a
bar's tune keeps to one 7th / 9th chord's tones (steps exist on the 9th's 7-8-9-10 run; elsewhere the next tone is a third away); steps
and repeats still make 61-79% of every song's intervals. Phrase starts are pickups and off-beats (the "&" of 4 and of 1 most often), as the
corpus's; the 2-bar hook returns at pitch wherever the source's harmony returns (petal storm, tumble, polka dots: 20% of their 2-bar cells;
crush, night dash and moon tide return 1-bar cells over repeated chords). Doubling = the chorus tune an octave up on the piano (bells in polka dots),
sounded on the same beat; moon tide follows the calm setup instead (a strings descant, no double).

## The songs

### 1. crush — after "Melt" (ryo / supercell, 2007) — the bright in-love pop-rock (sunny)

- **Measured** (`analyze.py Melt --beats`): 170 bpm, D major, 181 bars. Intro: a piano block riff (3.5 strikes a bar, 1.7 notes a strike,
  top 74-81) over I - IV, then the borrowed bVI - bVII (Bb, C); bass pumping 8ths. Verse: the descending "canon" bass under I - I/7 - vi -
  V - IV - iii - IV - V (two beats a chord), bass octave / root-fifth 8ths (6.6 strikes a bar), the piano a single-note line (2.2 strikes a
  bar). Pre: IV V .. bVI. Chorus: vi V vi IV V iii IV (one chord a bar), piano block chords (2.7 notes a strike) with the tune on top, bass
  quarters (5.9 a bar). Voice: 3.5 notes a sung bar (quarters 51%, 8ths 26%), steps 56%, repeats 22%, range 57-81, verse median 64 ->
  chorus 74 (+10).
- **Borrowed**: intro Dmaj9 Gmaj7 A#maj7 C7 (I IV bVI bVII); verse Dmaj7 (x2 belt) Bm7 A7 Gmaj7 F#m7 Gmaj7 A7 (the canon's I vi V IV iii
  IV V at a chord a bar; its descending bass walks inside the bars); pre Gmaj7 A7 A#maj7 Asus4 (the bVI before the chorus); chorus Bm7 A7
  Bm7 Gmaj7 A7 F#m7 Gmaj7 Asus4 (vi-led, as the source); 170 bpm; the roles and grooves (verse piano line on 1, the and of 2, 4; the
  chorus bass in quarters with a push; strings in the chorus); the arc (verse energy 1, chorus 2, a fill into the chorus).
- **Original**: the riff (one syncopated bar sequenced through I IV bVI bVII, a chord tone under each note), the voice (a verse cell
  F#-F#-E-D-E-F# sung twice by the belt, then answered; a pre climbing on repeated notes over the borrowed Bb; a chorus hook
  D D C# D F# E D that returns at pitch two bars later, peaking on G5 over IV), every line.
- **In DeaCube**: 23 columns (24 bars), 46 islands, 75 cubes, one Moon (kick 1 &2 3, snare 2 4, hats 8ths with accents). The chorus's
  melody islands stand a register higher and carry the piano double (the tune an octave up with a chord tone under it: 100% of the chorus
  notes). Home chord Dmaj7 = sunny. Deck: Em7, Bm9, C7.

### 2. petal storm — after "Senbonzakura" (Kurousa-P, 2011) — the wa-rock festival (moonlit)

- **Measured** (the file sits a 16th early; shifted back): 154 bpm, D minor, 154 bars. Intro: a block8 riff over bVI bVII | I bIII |
  bVI bVII | i IV where the tonic is THIRDLESS (D G A C E: the "wa" colour). Verse: bVI bIII (bVI bVII) i | bVI bIII IV V(sus4), block4 piano
  (4.2 strikes, 1.5 notes a strike), bass riff / quarters (5.2 a bar, the note repeated 50%). Chorus: i bVI bVII bIII twice a bar with the
  turnaround bVI - bII (Eb) - V, piano block8 (6.5 strikes, 1.85 notes a strike) in parallel fourths / the tune an octave up, bass
  syncopated root-fifth 8ths on 1 &1 e2 &2 3 &3 e4 &4 (8 a bar). Voice: 5.7 notes a sung bar, 8ths 56%, steps 54%, thirds 22%, range
  55-76, verse median 62 -> chorus 69; verse phrases off the beat (81%), chorus phrases on the downbeat (60%).
- **Borrowed**: intro A#maj7 C9 Dsus4 Fmaj7 (the sus tonic kept thirdless); verse A#maj7 Fmaj7 C7 Dm7 A#maj7 Fmaj7 Gm7 Asus4; pre Dm7
  A#maj7 Fmaj7 C7; chorus Dm9 A#maj7 C7 Fmaj7 Dm9 A#maj7 D#maj7 A7 (i bVI bVII bIII at half its speed, then the Neapolitan bII - V);
  154 bpm; the syncopated chorus bass, the verse's repeated-note bass, a "don-don" kick (1 &2 3 &3) with an open hat into each bar; the
  chorus at energy 3 (the off-beat hats doubled into 16ths).
- **Original**: the riff (a koto-like pluck in 16th-8th pentatonic figures with a chord tone under each note), the voice (a verse built
  from a 2-bar cell that returns verbatim; a chorus of straight 8ths whose 2-bar hook returns over the same i - bVI; the tune takes the
  Neapolitan Eb over bII), every line.
- **In DeaCube**: 24 columns, 48 islands, 78 cubes, one Moon. Home Dm9 = moonlit. Deck: Gm9, A7, C9.

### 3. tumble — after "Rolling Girl" (wowaka, 2010) — the frantic minor rock (rainy)

- **Measured**: 195 bpm, D major living on vi (B minor), 152 bars. Intro (24 bars): a single-note piano counterline riff (7.5 strikes a bar)
  over vi | vi | IV | IV. Verse: the same vamp at TWO BARS A CHORD, the piano silent or a line (2.6 strikes a bar), bass octave / root-fifth
  8ths on 1 &1 2 3 &3 4 &4. Chorus: one chord a bar, vi V I6 IV | vi V IV IV (the I over its third). Voice: 3.4 notes a sung bar (verse 2.6
  with 40% long notes, chorus 4.2), REPEATED NOTES 39% (chorus 43%), steps 39%, range 61-83, verse median 70 -> chorus 74.
- **Borrowed**: intro Bm7 Gmaj7 (two bars each); verse Bm7 Gmaj7 Bm7 Gmaj7 (two bars each); pre Bm7 A7 Gmaj7 Asus4; chorus Bm7 A7 Dmaj7
  Gmaj7 Bm7 A7 Gmaj7 (x2 belt) — the I6 is a Dmaj7 island whose bass plays its third (F#); 195 bpm; the bass slots; the chorus at energy 3.
- **Original**: the riff (rolling 8ths over two bars a chord), the voice (a verse of repeated notes and long ends whose 2-bar phrase
  returns verbatim; a chorus of repeated-note pairs D D F# F# E E C# whose hook returns over the same chords; the last chord sung once,
  the belt's second pass left to the band), every line.
- **In DeaCube**: 17 columns (24 bars with the belt), 34 islands, 58 cubes, one Moon (kick 1 3 &3, snare 2 4, hats 8ths). Home Bm7 =
  rainy (the song's key written as B minor: its home is vi). Deck: Em7, F#m7, Dmaj9.

### 4. night dash — after "Yoru ni Kakeru" (YOASOBI / Ayase, 2019) — the piano-driven rushing J-pop (dreamy)

- **Measured**: 130 bpm, Eb major, 140 bars. Intro: a piano figure over IV V | iii vi | IV .. | I. Verse: vi IV | III7 vi | V IV | iii vi
  (two chords a bar), piano arpeggio 8ths (6.9 strikes a bar), bass octaves (55% octave moves) with a 16th push before beat 4. Pre: bVII
  (Db) I ii V. Chorus: the royal road IV V iii vi with the bVII and a III7 turnaround, piano block8 (8.5 strikes, 1.8 notes a strike), bass
  root-fifth 8ths pushing on the "a" of 3 (1 &1 2 &2 3 a3 4 &4). Voice: 7.2 notes a sung bar (8ths 65%, 16ths 20%), steps 52%, thirds 24%,
  range 55-77, verse median 63 -> chorus 67.
- **Borrowed**: intro G#maj7 A#7 Gm7 Cm7; verse Cm7 G#maj7 G7 Cm7 A#7 G#maj7 Gm7 Cm7 (the source's order at a chord a bar); pre C#maj7
  D#maj9 Fm7 A#sus4; chorus G#maj7 A#7 Gm7 Cm7 C#maj7 A#7 D#maj9 G7; 130 bpm; the bass pushes; a four-on-the-floor kit with claps and
  open hats on the "and" (the recording's dance-pop kit: the MIDI carries none).
- **Original**: the riff (one 8th / 16th-pair figure sequenced through the royal road), the verse's 8th-note arpeggios, the voice (dense
  conversational 8ths with 16th turns in the verse; a chorus running up and down the 9th chords' runs, the bar over V returning, the peak
  on the tonic's Eb5), every line.
- **In DeaCube**: 24 columns, 48 islands, 78 cubes, one Moon. Home Ebmaj9 = dreamy. Deck: Fm9, Cm9, C#maj7.

### 5. moon tide — after "Looking for the Moon" (koyori, 2016), in the user's calm setup — the calm ballad (floaty)

- **Measured**: 132 bpm, G major, 139 bars. Verse: the vi-IV / V-vi vamp (Em C | D Em, two chords a bar); chorus: IV^7 Vsus iii7 I |
  bVI bVIIsus bIII Isus (C D Bm G | Eb F Bb G: the parallel minor's bVI bVII bIII, one chord a bar), piano block4 (2.6 notes a strike);
  voice 4.8 notes a sung bar, steps 58%, range 62-75, verse median 65 -> chorus 72.
- **The calm setup** (motif-engine memory `calm-references.md`: the user's liked calm songs, measured there): no drums, no bass instrument
  (the pad holds the low end), a flowing single-note piano arpeggio (~14 strikes a bar), a soft pad restruck twice a bar at 0.23-0.30 x the
  lead, strings across the tune, a seventh on nearly every chord, 68-96 bpm.
- **Borrowed**: intro Em9 Cmaj9; verse Em9 Cmaj7 Dsus4 Em7 Cmaj7 Dsus4 Bm7 Em9; chorus Cmaj7 Dsus4 Bm7 Gmaj9 D#maj7 Fsus4 A#maj7 Gsus4
  (x2 belt, floating) — the borrowed Eb F Bb kept; the tempo at HALF TIME (66 bpm: the calm setup's 16ths at 132 would rush); the calm
  setup's roles: the piano's 14-strike arpeggio and a pad restruck twice a bar on the ground islands, the voice on Keys, and in the chorus a
  third island a register higher with the strings' descant (a slow line of chord tones above the tune).
- **Original**: the voice (slow 8ths and quarters with long ends; the verse's IV - Vsus cell returning verbatim; the chorus takes the
  borrowed Bb), the arrangement.
- **In DeaCube**: 18 columns (19 bars), 42 islands, 60 cubes, no Moon. Home Gsus4 (the floating end) = floaty. Deck: Am9, Em7, Cmaj9.

### 6. polka dots — after "Ievan Polkka" (traditional; the Otomania Miku arrangement, 2007) — the playful dance (moonlit)

- **Measured**: 119 bpm, F# minor, 72 bars. Opening: the tonic vamp, the voice chanting one pitch (repeats 100%), the bass OOM on the beats
  (root / fifth, 5.2 strikes a bar), the accompaniment PAH on the off-beat 8ths (strongest on &1 &2 &3 &4). Verse: i | v i | i | v i | i V7
  ..., the voice a fast scat (10.4 notes a sung bar, 16ths 51%, REPEATS 48%, range 61-73 around G#4). Last section: i and bIII (A)
  alternating.
- **Borrowed**: intro F#m9 (2 bars, x2 belt); verse F#m7 C#m7 F#m7 C#7 F#m7 C#m7 Bm7 C#7 (i v i V7 ... iv V7); chorus F#m7 Amaj7 F#m7
  Amaj7 Bm7 C#7 F#m7 C#7 (the i - bIII alternation); tag F#m9 (x2 belt); 119 bpm; the oom-pah (bass root / fifth on the beats, Keys chords
  on the "and"), kick 1 3, snare 2 4, hats with the pah; the chorus at energy 3.
- **Original**: every melody (the traditional tune is not used): the bell riff over the vamp, a scat verse of 16th pickups and repeated
  notes, a bouncing chorus whose 2-bar cell returns over the same i - bIII, a chanted tag on one pitch sung twice by the belt.
- **In DeaCube**: 18 columns (24 bars with the belts), 36 islands, 65 cubes, one Moon. Home F#m9 = moonlit. Deck: Bm9, Dmaj7, Amaj9.

## 7. get proto — the user's own MIDI, built as a DeaCube song (v9, 2026-10-02)

The user's ask: "make get_proto-2.mid a song in the gallery. it should be almost fully faithful to the song given the instruments that we
have, and if we're missing an instrument (and any similar instrument) then add it as a color ... make it visual, and exercise good
music-making practices ... a section is 4 measures, a voice is distinct, different voices in a song (bass/harmony/lead/high, low) ... make it
flashy in terms of the ui itself using what we have. like alter grid patterns".

Three rounds:
- **Round 1** put every voice on keyboards to play the MIDI note for note. The verdict: "you literally just pasted in the MIDI onto pianos
  ... the point of deacube is too abstract away the piano".
- **Round 2** built it the way a player would: a chord card per bar, one lane per voice on chord grids, paths over the chord's tiles, stairs,
  launches, belts. The verdict: "it's better but it should still be more creative. like more various with the grids, and also maybe a way to
  override certain cube tiles to lower them/raise them by a note that the chord doesnt cover just for that tile. also the transitions should
  be cooler ... a visual for a riser, and piano could be used for the main melody if its too complicated to use grids, and the parts where the
  part is descending for a measure should be steps and stuff. so just more effects". The user also asked that "the main part where the 2-measure
  intervals of the melody where it got rewinded" repeat, and that "a melody [that] appears for 1-2 measures only" be its own grid.
- **Round 3** (this one) keeps round 2's cards and lanes and adds the rest.

`tools/gallery/midi_grid.py` builds it from the MIDI (`plan_song3`; the choices are `CONFIG3` in `tools/gallery/imports/get_proto.py`) and
`build_gallery.py` writes it with the others. The card reads "after your own midi · sample"; the hook is bar 25, the first C.

### Measured (the MIDI)

- 960 ticks a quarter, one tempo (370370 µs = 162 bpm), 4/4, 11 tracks, 5,875 notes, no controllers or pitch bends.
- The music sits an eighth note late against the file's bar lines; shifted 480 ticks earlier, every onset lands on DeaCube's 24-tick grid.
- One harmony under the whole song: a 4-bar circle of fifths in B minor (| Em9 A | Dmaj9 Gmaj7 | C#ø F#m7 | Bm |); the bass plays the roots
  in 8ths (in B, half notes).
- Form (bars from 0): 0 intro · 1–8 A · 9–16 A' (the hook: a 2-bar call falling from D5, its answer a fourth lower from G4, then both again)
  · 17–24 B (the sax rocking F#–E over every chord; a break with a string swell in bar 24) · 25–32 C · 33–40 A · 41–48 D (the hook again
  with a written-out echo: every note repeats 2 16ths later, ×0.75, four times; no bass) · 49–56 B · 57–64 C · 65–72 C' (ending C# → F#)
  · 73–80 E (the "Scifi" FX carries the C tune) · 81–88 outro.
- **Descending measures** (`midi_grid.descending_run`, the test reads the same rule from the .mid): the bar's top line, repeated pitches and
  octave flickers collapsed, has a run of 4+ notes stepping down 1–4 semitones that the bar does not climb back from. 38 bars: sax 17 (9 11
  13 15 20 22 41 43 45 47 52 65 66 67 68 69 72), flute 13 (the diminished fall G6 E6 C#6 A#5 at 4 8 28 32 36 40 44 48 60 68 76 80 84),
  square 6 (10 12 14 42 44 46), music box 2 (20, 52). Bass, piano, e.piano and ocarina have none by this rule.
- **Crashes**: a crash cymbal on the downbeat of bars 1 5 9 17 25 33 37 41 49 57 65 69 73 81; the strings swell into 1, 25 and 57.
- **Sax on the cards** per phrase (note-time): A' 78 %, B 67 %, C 82 %, D 72 % (the echo copies; the line alone is A''s), B 72 %, C 82 %,
  C' 70 %. A real pedal: the sax's F#–E in B, the same two notes over all four chords.

### The harmony: a card per bar

| loop bar | MIDI | card | the MIDI's note-time on its tiles |
|---|---|---|---|
| 1 | Em9 · A | A9 | 72 % |
| 2 | Dmaj9 · Gmaj7 | Gmaj9 | 82 % |
| 3 | C#ø · F#m7 | F#m7 | 60 % |
| 4 | Bm | Bm9 | 77 % |

The cards were chosen for the bass's root–fifth (a card must hold both as a root or a fifth). Colour cards: Amaj7 for the intro's swell, C#7
and F#7 for C''s last two bars (the turnaround into E). Over the whole song: bass 99 % of its note-time on the card, the fx 82 %, sax 74 %,
piano 71 %, music box 71 %, e.piano 66 %, square 65 %, flute 60 %. A note a card has no tile for is NUDGED (below) when a scale step or two
from a tile reaches it, else it plays the nearest tile.

### Sections: a signature each

Fourteen 4-measure sections (the intro is one bar), with their letters and roles. "×2" = the column replays its 4 measures (a belt, or a
rewind). No two neighbours use the same devices (the test lists each section's devices and compares them).

| section | bars | columns | its devices | path shape |
|---|---|---|---|---|
| I | 0 | 1 bar | the strings' Amaj7 swell (follow ×3 + octave copy), the BIG launch into A (energy 3) | — |
| A | 1–8 | ×2 belt | ladders on the belt; the flute's fall on stairs (bar 4); the ocarina's 1-bar FRAGMENT (bar 4) launching into bar 5's crash | zigzag |
| A' | 9–16 | ×2 REWIND | the hook: the sax's two runs as STAIRS (bars 9, 11) with grids between, time unwinding at bar 13; the square's octave-flicker steps raised (towers); vary on the lead | diagonal |
| B | 17–24 | ×2 belt | the sax on lead KEYBOARDS (bars 17–19, popping up and sinking); half-note bass; the sax's run and the music box's diminished fall as stairs in bar 20; bar 24 rests (the break) and the BIG launch into C rises over it | spiral |
| C | 25–32 | ×2 belt | the music box as the sax's octave copy (echo layer); the flute's spark fall (28); mirrored paths | mirror |
| A | 33–40 | ×2 belt | A again, vary on the harmony, the ocarina's fragment launching into bar 37 | pump |
| D | 41–48 | ×2 REWIND | the climax: the sax's runs as stairs, its lines on ECHO 2 (the written-out echo), CLIMB; no bass, so the square's steps rise on towers in the bass lane; spheres leap | ladder |
| B | 49–52 | 1-bar columns | the sax's PEDAL: one cube rocking on one tile (E, every F# nudged ▲) carried as a LONG GRID over all four chords; the music box's diminished fall (52) | pump |
| B | 53–56 | one column | the sax on keyboards again; the strings' swell (bar 56) as its own short grid with the BIG launch into C | zigzag |
| C | 57–64 | ×2 belt | C with vary on the lead; the flute's spark fall (60) | mirror |
| C' | 65–68 | 1-bar columns | a stairs on every falling bar of the sax (65 66 67 68) and the flute (68); the piano one cube on a long grid | ladder |
| C' | 69–72 | 1-bar columns | the turnaround cards C#7 → F#7, the sax's runs as stairs (69, 72), the launch into E | ladder |
| E | 73–80 | ×2 belt | the higher chorus: the harmony and lead lanes raised an octave (register +1, the notes an octave up); the fx (Atmosphere) carries the C tune with its spark run (74); vary | spiral |
| O | 81–88 | ×2 belt | vary 2 (answer home) on the harmony and high lanes; energy falling 3 → 0 across the measures; the flute's last fall as stairs (84) | ladder |

Path shapes are tie-breaks among tiles that sound the same note (`midi_grid.shape_cost`): a shape never changes a pitch.

### Lanes = voices

Front to back (S17's order: the stairs where the eye is), at most 4 lanes in a column:

| lane | z | register | voices |
|---|---|---|---|
| lead | 0 | 0 (E +1) | the sax (grids, keyboards in B, the pedal), every stairs of the sax, the ocarina's fragments, the fx in E, the flute's fall in O (the lead rests there) |
| bass | 9 | −1 (lowered) | the synth bass (ladders; in D, where there is no bass, the square's steps at +1) |
| harmony | 18 | 0 (E +1) | the piano (one cube pumping 8ths with follow ×3 = its triads), the e.piano (a sphere + its octave copy), the strings' swells |
| high | 27 | +1 (raised: a tower) | the flute's dyads (two spheres), the music box, the square's octave flicker (lift stickers), their stairs where the lead lane is busy |
| drums | Moons | — | a Moon per 8-bar phrase, its own 8-bar groove and the electronic kit |

### Devices = the moments

- **Stairs** (25 islands, one per descending measure of the first pass; a ×2 column's stairs plays both passes): 36 of the 38 descending
  measures play on a staircase — sax 15/17, flute 13/13, square 6/6, music box 2/2. Each stairs sits on its own measure (barOffset) and
  falls into the chord of the measure after it; its type is the one whose run is nearest the MIDI's: scale (the hook's D5 C#5 B4 A4 G4 exactly,
  bar 9), spark (the diminished fall, on A9-type cards: C# A F# E / D C# B A), bright, chord, slide. The two left out: bar 22 (the second pass of
  bar 18, which does not fall) and 52 (the pedal's long grid holds the lead lane).
- **Launch** (14): a riser and crash into every crash bar, the three strings swells (into 1, 25, 57) at energy 3, the others at 2 or less. The
  mid-belt crashes (5, 37) come from the ocarina's fragment, whose turn ends after the first pass.
- **Rewind** (A', D): the hook's 4-measure column replays with time unwinding where the call comes back (bars 13 and 45). The engine
  replays whole columns, so a 2-bar rewind would have turned the loop into A9 Gmaj9 A9 Gmaj9; the 4-bar column keeps the harmony.
- **Nudge** (258 steps): flute 156, music box 54, e.piano 18, sax 17, square 8, fx 5. A nudged step plays a scale step or two above / below
  its tile (▲ / ▼ on the bead).
- **Lead keyboards** (6): only in B, where the sax's line is the least on the cards (67 % / 72 %) and rocks between neighbours; each
  keyboard holds one bar and pops up for it.
- **Echo** (D): the sax's cubes and stairs on echo 2 (the engine repeats each note at −35 %: the MIDI's written-out echo, two of its four
  repeats).
- **Fragments** (the ocarina at 4 and 36, the strings at 0 and 56): their own short grids on their measure, popping up from the sea for it.
- **Long grids** (3): the sax's pedal (B, 49–52) and the piano in both C' sections.
- **Octave copies**: the e.piano, the strings, the music box doubling the sax (C) and the e.piano (O). **Climb**: the sax in D.
  **Towers**: the high lane, D's square, E's raised lanes.

The user's earlier list of transitions, where each now plays: the diminished fall — spark stairs at 28 32 43 44 47 48 60 66 76 80; the
octave-displaced melody — the square's lift flicker (A', D) and the music box's octave copies; a pedal ostinato — the sax's pedal from 49;
the turnaround launching into a higher chorus — C#7 → F#7, the launch on 73, E raised; the same loop with small differences — vary (from
9 33 57 73 81) and rewind (9, 41).

### The recipe (the game's own actions)

1. A new song, moonlit, B minor, tempo 162. The deck: Bm9, A9, Gmaj9, F#m7, Amaj7, C#7, F#7.
2. The intro: an Amaj7 grid in the harmony lane; the tremolo cube (follow ×3, octave copy below); launch (↗), energy 3.
3. Each section: the four loop cards on the four measures of a column; lanes with the dotted add (the lead in front, the bass lowered ▼,
   the high row raised ▲).
4. Draw the voices size first: the bass's ladders (half notes in B); the piano pumping one tile, follow ×3; the e.piano a sphere + octave
   copy (⇧↑); the flute's two lines, the sax's lines; the square on one tile with lift on every other note; the music box as the sax's copy,
   recoloured. Where a note has no tile, select it in the inspector and press higher ▲ / lower ▼.
5. Every falling measure: a stairs card on that measure of the lead lane (or the high lane while the lead plays), the voice dropped on it;
   its type and ▼ / ▲ register in the header.
6. Repeat ×2 on the 8-bar phrases; the rewind sticker (⏪) on every island of A' and D; vary where the halves differ; the islands that
   rest in a second pass stay ×1 (a break).
7. B: a keyboard on bars 17–19 (and 53–55) of the lead lane, the sax drawn on its keys. B's second time: four one-bar columns; the sax's cube
   on one tile with its F#s nudged ▲, extended (→ ×3). C': extend the piano. D: echo ×2 on the sax, climb ×1. E: raise the harmony and lead
   rows (▲).
8. Launch (↗) on the island whose turn ends at each crash; energy 3 on the three swells.
9. A Moon card at each phrase's column (its 8-bar groove and kit are v9 data with no UI of their own yet).
10. The section plinths: letters and roles.

### What is not the MIDI

- Exact pitches at their onsets (the test): bass 95.6 %, e.piano 86 %, music box 81 %, flute 76 %, sax 56 %, piano 47 %, square 23 %,
  the fx 0 % (E is raised an octave on purpose). Onsets: bass 98 %, drums 100 %, piano 98 %, music box 95 %, flute 88 %, sax 87 %, square 34 %.
- A stairs holds its lane for its measure: the notes of that voice before the run drop (the flute's dyads in its fall bars: 120 notes),
  and a stairs plays one note per step (the square's 32nd-note flicker becomes one note per step: its onsets fall to 34 %).
- The runs are the game's (same direction and length, a step or a few off the MIDI's); spark needs a dominant card, so over Bm9 the flute's
  diminished fall is a scale run.
- A replayed pass repeats the first (with vary where set): where the second pass differs a lot the island rests there (the B breaks, D's last
  bar).
- The pedal replays bar 49's rocking over 50–52 (the MIDI's small ornaments there are not played); E is an octave higher than the MIDI.
- Velocities follow the MIDI through the cube's volume and stickers; an island's energy is compensated in its cubes' volume (only O's last
  measures and the big launches are louder / softer on purpose).

### Engine changes this song relies on (v9, additive)

- **The fx colour** (group 10, Atmosphere first), **Moon kits and grooves** (`MeasureState.kit`, `bars`), **section letters**
  (`SongState.sectionLetters`): round 1–2, unchanged.
- **Per-step nudge** (N, DeaCube/Nudge.cs): `CubeState.nudges`, −2..+2 scale steps of the song key per node; the builder mirrors Nudge.Semis.
- **Offsets for stairs and keyboards** (S17: SongManager.Offsettable 0/2/3) and **the stairs' chord** at its own measure (Harmony.ChordOf /
  ChordAfterIsland: the column's first grid sounding at that measure, and the one after it).
- **KeyStage fragments** (-60): any island shorter than its column pops up for its measures.
- **The riser, crash and section pulses** (R: LaunchFx, SectionFx), **staircases** (-60), echo visuals (R).
- **The menu preview** (MenuPreview): keyboards and stairs on their measure (barOffset), nudges; it leaves stairs runs out.

### How it was checked

`python3 tools/gallery/build_gallery.py --verify` rebuilds the song and checks it: keyboards only in the lead lane of B, at most 4 lanes a
column, no note landing on another cube's tile, every cube path walks to neighbouring tiles, the drums exact.

`V9ChecksProto` (Play mode, `Assets/DeaCube/Tests/V9ChecksProto.cs`, report `Captures/proto9_report.txt`) reads the .mid itself:
last run 2026-10-02 01:40: **26 PASS, 0 FAIL**.
- **The world**: 190 islands (159 chord grids, 25 stairs, 6 lead keyboards), 311 cubes, 11 Moons, 23 columns, 14 sections with their
  letters and roles, 9 columns replaying ×2 (2 of them rewind). Every grid at its lane's z and register, at most 4 lanes a column.
- **Stairs**: the test finds the descending measures in the .mid itself and the bars every stairs plays: sax 15/17, flute 13/13, square 6/6,
  music box 2/2 (the two sax bars are the builder's stated exceptions).
- **Launches**: the crash bars read from the drum track (1 5 9 17 25 33 37 41 49 57 65 69 73 81) are exactly where the 14 launches land;
  energy 3 on the swells into 1, 25 and 57.
- **Signatures**: no two consecutive sections alike (the report lists each section's devices).
- **Keyboards**: all 6 in the lead lane, playing the sax, up during their bar and under the sea elsewhere in their column.
- **Rewind, echo, fragments, pedal, long grids**: A' and D unwind at their pass boundary; D's sax on echo 2; the ocarina's 2 fragments pop
  up for their bar; the pedal; 3 long grids.
- **Nudges**: 258 nudged steps; 419 of 434 nudged notes (96.5 %) sound the MIDI's pitch at its onset (92 more on varied passes, climbing
  cubes and carried windows, which the game re-voices).
- **Against the MIDI** (every cube's every step through VoiceRules.Resolve): bass on its cards 99 %, bass onsets 98.2 %, bass pitches
  95.6 %, every drum hit, the sax's contour 85 % (round 2: 72 %).
- **Playing**: bar 0 to the end, 100 % of the 5,516 scheduled notes sent within a tick, none late; 51 fps over the whole song.
- **Frame and open time**, same session: get proto 53 fps (18.8 ms), crush 116 fps. It opens in 0.85 s warm, 3.0 s cold. (The frame rate
  depends on the editor's state: in other sessions it measured 41 and 27 fps, with the round-2 file equally slower there:
  V9ChecksProto.PerfProbe compares files, renderers by name and the profiler's markers in one session.)
- **The card preview**: the hook plays as the engine does (569 of 569 notes; the preview leaves the stairs runs out); the six v5 previews
  unchanged; the shelf shows 7 cards.

Levels (`python3 tools/gallery/wavstats.py --solos AudioCube_Unity/Captures/proto9_wav`; dB = the voice's loud end against the band in C):

| file | RMS dBFS | peak | clipped | vs the band |
|---|---|---|---|---|
| band, C | -18.6 | -3.9 | 0 | |
| bass | -23.5 | -11.9 | 0 | -4.7 |
| drums | -23.5 | -6.2 | 0 | -4.8 |
| strings (the intro's swell) | -22.3 | -6.1 | 0 | -0.9 |
| ocarina | -27.1 | -9.1 | 0 | -5.1 |
| e.piano | -27.4 | -14.5 | 0 | -6.8 |
| piano | -27.7 | -11.3 | 0 | -7.6 |
| sax (A', its stairs) | -31.4 | -13.8 | 0 | -10.6 |
| music box | -33.4 | -13.9 | 0 | -13.2 |
| flute | -35.2 | -18.4 | 0 | -14.7 |
| fx | -35.1 | -17.2 | 0 | -15.4 |
| square | -38.7 | -22.7 | 0 | -16.7 |

Nothing clips; the soft voices are soft in the MIDI too.

Captures (`Captures/proto9_*.png`):
- `overview`, `song_1` to `song_3`: the whole song (the thirds from above, the haze lifted).
- `sec00_I` … `sec13_O`: every section from the follow camera, a bar and a half in.
- `fragment` (the ocarina's bar in A), `Aprime_stairs` (the hook's staircases in front), `rewind` (A' unwinding), `B_keys` (the lead
  keyboard up for its bar), `launch_riser` / `launch_crash` (the beam into C, the crash), `D_climax`, `B_pedal`, `Cprime`, `turnaround`
  (C#7 → F#7 into E), `E_raised`.
- `hud`, `popover`, `card`.

## Verification

- `python3 tools/gallery/build_gallery.py --verify`: every node on its grid, every note sounding the written pitch after VoiceRules' fold
  and the bass anchor, every path tiling its window, tiles owned by one cube, the JSON equal to a fresh build. Result: 0 errors on all six.
- `V5ChecksG.Run()` (Play mode, `Assets/DeaCube/Tests/V5ChecksG.cs`, report `Captures/g5_report.txt`): the index (six entries, well
  formed), per song: loads (v4, a key), opens with every island / column / Moon / cube (all finalized), keeps its key, one History entry,
  round-trips through `SongState.Capture` (every cube identical), plays >= 8 bars from bar 0 with Synth late / errors 0 and no console
  error, one capture framing its chorus (`Captures/g5_<id>.png`), WAVs (`<id>.wav` 12 s from bar 0, `<id>_chorus.wav`,
  `<id>_chorus_novoice.wav`), the user's main save untouched, the backup slots put back.
Results of the last run (2026-09-30 04:24, `Captures/g5_report.txt`): **82 PASS, 0 FAIL**. Per song Gallery.Open took 64-360 ms (the first
open 360 ms, the rest 64-97 ms); Play mode held 116-120 fps mean with 34-48 islands and 58-78 cubes (worst frame 24-37 ms); every cube
restored (58-78 per song) and identical after `Capture`; late / errors 0 across 8.1-9.9 bars from bar 0 and both chorus stems; no console
error; the user's save md5 `ef5c7a1e0808fe6fda3efb7b81befa3f` unchanged. Captures `Captures/g5_<id>.png` (each song's chorus).
With get proto added (v9, 2026-10-01 23:14), the run covers seven songs: **88 PASS, 0 FAIL**. get proto opens, every island, Moon and cube
is as authored, it round-trips, and it plays 8 bars from bar 0 with no late notes. V5ChecksG now queues bar 0 the way Gallery.Open does
(AudioCube.ScheduleAllNow after Play): a song this size has slow first frames.

Recordings (`python3 tools/gallery/wavstats.py <dir>`; onsets: `scratchpad/v2/analyse_wav.py` against each song's 16th grid):

| song | RMS (12 s from bar 0) | peak | clipped | onsets vs the 16th grid (mean / max) | chorus RMS | voice vs the rest of the band |
|---|---|---|---|---|---|---|
| crush | -23.2 dBFS | -8.6 | 0 | 2.1 / 7.4 ms | -21.6 | -2.7 dB |
| petal storm | -23.0 | -8.7 | 0 | 1.6 / 8.2 ms | -21.0 | -3.3 dB |
| tumble | -23.4 | -9.0 | 0 | 3.4 / 7.4 ms | -21.9 | -4.0 dB |
| night dash | -23.6 | -7.2 | 0 | 2.2 / 21.0 ms | -22.3 | -2.9 dB |
| moon tide | -27.6 | -14.0 | 0 | (legato, no transients) | -25.8 | -3.1 dB |
| polka dots | -24.2 | -9.3 | 0 | 2.4 / 5.9 ms | -22.4 | -1.6 dB |

"Voice vs the rest": the chorus recorded with and without the voice, voice power = full - rest; the voice alone is 1.6-4 dB under the
WHOLE band (3-5 other parts), so it is the loudest single part (a first mix had it 7-10 dB under: the voice went to full level and the
bass, kit, piano and strings came down 3-5 dB). The calm song sits 4 dB under the others on purpose. Textures (builder, notes a bar,
voice excluded): verse -> chorus crush 3 -> 12.4, petal storm 10 -> 13.5, tumble 2 -> 12, night dash 8 -> 13.5, moon tide 20 -> 21.6,
polka dots 8 -> 13; every column's ground island strikes on its first beat (each chord change is heard); unison strikes between two cubes:
0-1 per song (moon tide 8: the flowing arpeggio meets the voice on a chord tone now and then).

## Known limits

- One chord a column: two-chords-a-bar sources (Yoru ni Kakeru, Senbonzakura's chorus) move at half their harmonic speed; slash chords
  exist only where the bass can play a chord tone (tumble's D/F#).
- A tune keeps to one 7th / 9th / sus4 chord's tones per bar, so thirds run higher (16-31%) than the corpus's 13%.
- In the six v5 songs the Moon's kit is one bar all song long; sections differ by energy (hats ghosted / as written / doubled) and the fill,
  not by pattern. Since v9 a Moon can hold a groove of up to 8 bars with its own kit (get proto uses both).
- Levels are measured, not heard: the user's ear decides the mix (instVolume per song in each sheet).
- get proto is the heaviest song: 190 islands and 311 cubes (14,400 active renderers against crush's 2,700). It opens in about 0.85 s warm
  and plays at about half of crush's frame rate (53 against 116 fps in the last run; 41 in a slower session, where the round-2 file was as
  slow). Where it is not the MIDI is listed in section 7.

## Appendix — the columns as they stand in the world

Per section, each column's islands front to back: the ground island's chord (the harmony), the melody island's chord (the palette its
tiles offer the tune; `^` = a register higher), `x2` = a repeat belt. Chord names as DeaCube writes them (A# = Bb).

**crush**
- intro: Dmaj9 / Dmaj9 · Gmaj7 / Gmaj9 · A#maj7 / A#maj7 · C7 / C9
- verse: Dmaj7 / Dmaj9 x2 · Bm7 / Bm9 · A7 / A9 · Gmaj7 / Gmaj9 · F#m7 / F#m7 · Gmaj7 / Em9 · A7 / Dmaj9
- pre: Gmaj7 / Gmaj9 · A7 / A9 · A#maj7 / A#maj7 · Asus4 / Dmaj9
- chorus: Bm7 / Dmaj9^ · A7 / A9^ · Bm7 / Dmaj9^ · Gmaj7 / Em9^ · A7 / Dmaj9^ · F#m7 / F#m7^ · Gmaj7 / Em9^ · Asus4 / Asus4^

**petal storm**
- intro: A#maj7 / A#maj9 · C9 / C9 · Dsus4 / Dsus4 · Fmaj7 / Fmaj9
- verse: A#maj7 / A#maj9 · Fmaj7 / Fmaj9 · C7 / C9 · Dm7 / Dm9 · A#maj7 / A#maj9 · Fmaj7 / Fmaj9 · Gm7 / Gm9 · Asus4 / Asus4
- pre: Dm7 / Dsus4 · A#maj7 / A#maj9 · Fmaj7 / Fmaj9 · C7 / Dsus4
- chorus: Dm9 / Dm9^ · A#maj7 / A#maj9^ · C7 / C9^ · Fmaj7 / Fmaj9^ · Dm9 / Dm9^ · A#maj7 / A#maj9^ · D#maj7 / D#maj9^ · A7 / A7^

**tumble**
- intro: Bm7 / Bm9 · Gmaj7 / Gmaj9
- verse: Bm7 / Bm9 · Gmaj7 / Gmaj9 · Bm7 / Bm9 · Gmaj7 / Gmaj9
- pre: Bm7 / Bm9 · A7 / A9 · Gmaj7 / Gmaj9 · Asus4 / Dmaj9
- chorus: Bm7 / Dmaj9^ · A7 / A9^ · Dmaj7 / Dmaj9^ · Gmaj7 / Bm9^ · Bm7 / Dmaj9^ · A7 / A9^ · Gmaj7 / Em9^ x2

**night dash**
- intro: G#maj7 / G#maj9 · A#7 / A#9 · Gm7 / Gm7 · Cm7 / Cm9
- verse: Cm7 / Cm9 · G#maj7 / G#maj9 · G7 / G7 · Cm7 / Cm9 · A#7 / A#9 · G#maj7 / G#maj9 · Gm7 / Gm7 · Cm7 / Cm9
- pre: C#maj7 / C#maj9 · D#maj9 / D#maj9 · Fm7 / Fm9 · A#sus4 / A#sus4
- chorus: G#maj7 / G#maj9 · A#7 / A#9 · Gm7 / Gm7 · Cm7 / Cm9 · C#maj7 / A#m9 · A#7 / A#9 · D#maj9 / D#maj9 · G7 / G7

**moon tide**
- intro: Em9 · Cmaj9
- verse: Em9 / Em9 · Cmaj7 / Cmaj9 · Dsus4 / Dsus4 · Em7 / Em9 · Cmaj7 / Cmaj9 · Dsus4 / Dsus4 · Bm7 / Bm7 · Em9 / Em9
- chorus: Cmaj7 / Cmaj9^ / Cmaj9^ · Dsus4 / Dsus4^ / Dsus4^ · Bm7 / Bm7^ / Bm7^ · Gmaj9 / Gmaj9^ / Gmaj9^ · D#maj7 / D#maj7^ / D#maj7^ · Fsus4 / Fsus4^ / Fsus4^ · A#maj7 / A#maj9^ / A#maj9^ · Gsus4 / Gsus4^ / Gsus4^ x2

**polka dots**
- intro: F#m9 / F#m9 x2
- verse: F#m7 / F#m9 · C#m7 / E9 · F#m7 / F#m9 · C#7 / C#7 · F#m7 / Amaj9 · C#m7 / E9 · Bm7 / Bm9 · C#7 / C#7
- chorus: F#m7 / F#sus4^ · Amaj7 / Amaj9^ · F#m7 / F#sus4^ · Amaj7 / Amaj9^ · Bm7 / Bm9^ · C#7 / C#7^ · F#m7 / F#m9^ · C#7 / C#7^
- tag: F#m9 / Amaj9 x2

## Rebuilding

See `tools/gallery/README.md`: `python3 build_gallery.py` writes the songs, `--verify` checks them, `--stats --table` prints the numbers.

### Lead marks (v9, the stage lights)

Both builders write `"lead"` on every measure. The six sheet songs mark the grids of their `melody` lane (`build_gallery.py`); an import
marks the chord grids of its main-melody lane (`midi_grid.to_state`: cfg `lead_lane`, default `"lead"` when the song has one — "get proto": the
sax's lane in front, 24 grids). Keyboards and stairs need no mark (DeaCube treats them as lead). While the song plays, DeaCube dims the stage and
spotlights a lead grid in every measure it sounds (`StageLights.cs`). Adding the field changed nothing else in the JSON (checked: every file is
equal to the previous build with the `lead` keys removed).
