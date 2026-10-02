"""tumble — after "Rolling Girl" (wowaka, 2010): the frantic minor rock.

Measured on the source (python3 analyze.py "Rolling Girl" --beats): 195 bpm, D major but living on vi (B minor), 152 bars.
Intro (24 bars): a single-note piano counterline riff (7.5 strikes a bar, top 74) over vi | vi | IV | IV. Verse: the same two-chord vamp,
TWO BARS A CHORD (vi vi IV IV x4), piano silent or a line (2.6 strikes a bar), bass octave / root-fifth 8ths on 1 &1 2 3 &3 4 &4 (6 a bar).
Chorus: one chord a bar, vi V I6 IV | vi V IV IV (the I over its third: D/F#), piano block4 (1.8 notes a strike), bass root-fifth 8ths.
Voice: 3.4 notes a sung bar (verse 2.6 with 40% long notes, chorus 4.2), REPEATED NOTES 39% (chorus 43%), steps 39%, range 61-83,
verse median 70 -> chorus 74; phrases start on a beat (52%).
Borrowed: the vamp at two bars a chord, the chorus progression (one a bar, D/F# as a Dmaj7 island with the bass on its third), the
tempo, the grooves, the form, the arc, the roles (piano riff / block, bass, kit + the voice). Original: every melody, the riff, the lines.
Key written as B minor (the song's home is vi).
"""
from sheet import Song, Moon, C, I, col, up, held

HARM = [
    ("intro", "Bm7", 2, 1, 2, False), ("intro", "Gmaj7", 2, 1, 2, False),
    ("verse", "Bm7", 2, 1, 1, False), ("verse", "Gmaj7", 2, 1, 1, False), ("verse", "Bm7", 2, 1, 1, False), ("verse", "Gmaj7", 2, 1, 1, False),
    ("pre", "Bm7", 1, 1, 2, False), ("pre", "A7", 1, 1, 2, False), ("pre", "Gmaj7", 1, 1, 2, False), ("pre", "Asus4", 1, 1, 2, True),
    ("chorus", "Bm7", 1, 1, 3, False), ("chorus", "A7", 1, 1, 3, False), ("chorus", "Dmaj7", 1, 1, 3, False), ("chorus", "Gmaj7", 1, 1, 3, False),
    ("chorus", "Bm7", 1, 1, 3, False), ("chorus", "A7", 1, 1, 3, False), ("chorus", "Gmaj7", 1, 2, 3, False),
]

VOICE = [
    None, None,
    "r:4 F#4:8 F#4:8 A4:4 A4:4 | B4:2 r:8 A4:8 F#4:8 A4:8",     # verse: repeated notes, long ends
    "B4:4. A4:8 F#4:4 D4:4 | F#4:2. r:4",
    "r:4 F#4:8 F#4:8 A4:4 A4:4 | B4:2 r:8 A4:8 F#4:8 A4:8",     # the 2-bar phrase returns verbatim
    "B4:4. A4:8 B4:4 D5:4 | A4:2. r:8 F#4:8",
    "F#4:8 F#4:8 F#4:8 A4:8 B4:8 B4:8 A4:4",                   # pre: the notes pile up
    "A4:8 A4:8 A4:8 B4:8 C#5:8 C#5:8 B4:4",
    "B4:8 B4:8 A4:8 B4:8 D5:4 B4:4",
    "E5:4 D5:4 E5:4 r:8 C#5:8",
    "D5:8 D5:8 F#5:8! F#5:8 E5:8 E5:8 C#5:4",                  # chorus: pairs of repeated 8ths ...
    "C#5:8 C#5:8 E5:8 E5:8 B4:8 B4:8 A4:4",
    "A4:8 A4:8 D5:8 D5:8 C#5:8 C#5:8 E5:4",
    "D5:4 B4:4 A4:4 r:8 C#5:8",
    "D5:8 D5:8 F#5:8! F#5:8 E5:8 E5:8 C#5:4",                  # ... the hook returns
    "C#5:8 C#5:8 E5:8 E5:8 B4:8 B4:8 A4:4",
    "B4:4 D5:4 F#5:4. E5:8",                                    # sung once; the belt's second pass is the band alone
]
# the intro riff: a rolling single-note piano counterline (two bars a chord)
RIFF = [
    "B4:8 F#5:8 D5:8 F#5:8 B4:8 F#5:8 C#5:8 D5:8 | B4:8 F#5:8 D5:8 F#5:8 A5:8 F#5:8 C#5:8 D5:8",
    "G4:8 D5:8 B4:8 D5:8 G4:8 D5:8 A4:8 B4:8 | G4:8 D5:8 B4:8 D5:8 F#5:8 D5:8 A4:8 B4:8",
]
HOLD_DOUBLE = {12}            # a column whose repeated pairs leave no twin tiles for the double: it holds instead of re-striking
CHORUS_STRINGS = ["D4:2 F#4:2", "C#4:2 E4:2", "D4:2 F#4:2", "B3:2 D4:2", "D4:2 F#4:2", "C#4:2 E4:2", "B3:2 D4:2"]
VERSE_PIANO = ["B3:2 A3:2 | F#3:2 A3:2", "G3:2 F#3:2 | D3:2 F#3:2"]
BASS = {
    "intro": "1:8 5:8 1:4 1:8 5:8 8:8 5:8",
    "verse": "1:8 8:8 1:4 1:8 8:8 1:8 8:8",        # octave 8ths on 1 &1 2 3 &3 4 &4 (the source's verse bass)
    "pre": "1:8 5:8 1:4 1:8 5:8 8:8 5:8",
    "chorus": "1:8 5:8 1:4 1:8 5:8 8:8 5:8",       # root-fifth on the same slots
}
FILL_BASS = "1:4 r:4 5:4 r:4"
TURN = "1:8 5:8 1:4 1:8 3:8 5:8 8:8"            # the chorus's 4th bar walks up the chord
SLASH = {12: "3:8 5:8 3:4 3:8 5:8 8:8 5:8"}       # D/F#: the bass on the third (rule 4 anchors only 7ths / 9ths)


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        bass = SLASH.get(i, FILL_BASS if fill else (TURN if sec == "chorus" and (i - 10) % 4 == 3 else BASS[sec]))
        cubes = [C("bass", bass, base="A1", volume=0.9)]
        if sec == "verse":
            cubes.append(C("piano", VERSE_PIANO[(i - 2) % 2], volume=0.5))
        elif sec == "chorus":
            cubes.append(C("strings", CHORUS_STRINGS[i - 10], volume=0.45))
        elif sec == "pre" and not fill:
            cubes.append(C("piano", "5,:8 1 3 1 5, 1 3 1", base="D3", volume=0.55))
        ground = I(chord, *cubes)
        melody = None
        if sec == "intro":
            melody = I("auto", C("piano", RIFF[i], volume=0.8, tag="riff"))
        elif VOICE[i]:
            vc = [C("lead", VOICE[i], tag="voice")]
            if sec == "chorus":
                dbl = up(VOICE[i], 12)
                vc.append(C("piano", held(dbl) if i in HOLD_DOUBLE else dbl, octave=1, follow=2, volume=0.6, tag="double"))
            melody = I("auto", *vc, reg=1 if sec == "chorus" else 0, repeat=1)
        out.append(col(sec, bars, en, fill, rep, ground=ground, melody=melody))
    return out


DRUMS = [
    C("drums", "K4:4 r:4 K4:8 K2:8 r:4"),                  # kick 1, 3, &3
    C("drums", "r:4 S4:4 r:4 S4:4"),                       # snare 2, 4
    C("drums", "H3:8 H1:8 H3:8 H1:8 H3:8 H1:8 H3:8 H1:8"),
]

SONG = Song(
    id="tumble", title="tumble", after="rolling girl · wowaka", bpm=195, key="B", minor=True,
    columns=columns(), moons=[Moon(DRUMS, at_col=2)],
    vol={"lead": 1.0, "piano": 0.5, "bass": 0.5, "strings": 0.4, "drums": 0.45},
    tone=1.0, space=0.8, deck=["Em7", "F#m7", "Dmaj9"], blurb="tumbling, getting back up", home="m7",
)
SONG.order = 3
