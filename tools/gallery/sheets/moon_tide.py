"""moon tide — after "Looking for the Moon" (koyori, 2016), arranged in the user's calm setup: the calm ballad.

Measured on the source (python3 analyze.py "Looking for" --beats): 132 bpm, G major, 139 bars. Verse: the vi-IV / V-vi vamp (Em C | D Em,
two chords a bar), bass root-fifth 8ths; chorus: IV^7 Vsus iii7 I | bVI bVIIsus bIII Isus (C D Bm G | Eb F Bb G: the parallel minor's
bVI bVII bIII borrowed, one chord a bar), piano block4 (5.2 strikes, 2.6 notes a strike); voice 4.8 notes a sung bar (8ths 53%,
quarters 23%), steps 58%, range 62-75, verse median 65 -> chorus 72.
The calm setup (motif-engine memory calm-references.md, the user's liked calm songs, measured there): no drums, no bass instrument (the
pad holds the low end), a flowing single-note piano arpeggio (~14 strikes a bar across 55-86), a soft pad restruck twice a bar at
0.23-0.30 x the lead, strings as a counterline under the tune, a seventh on nearly every chord, 68-96 bpm.
Borrowed: the progressions (one chord a bar), the tempo at HALF TIME (66 bpm: the source's 132 with the calm setup's 16ths would rush),
the form (intro -> verse -> chorus), the roles of the calm setup. Original: every melody, the lines, the arrangement.
Lanes: the ground island carries the piano arpeggio and the pad (the chord); the melody island the voice; in the chorus a third island,
a register higher, the strings' descant (a slow line of chord tones above the tune).
"""
from sheet import Song, C, I, col, up, held

HARM = [
    ("intro", "Em9", 1, 1, 1, False), ("intro", "Cmaj9", 1, 1, 1, False),
    ("verse", "Em9", 1, 1, 1, False), ("verse", "Cmaj7", 1, 1, 1, False), ("verse", "Dsus4", 1, 1, 1, False), ("verse", "Em7", 1, 1, 1, False),
    ("verse", "Cmaj7", 1, 1, 1, False), ("verse", "Dsus4", 1, 1, 1, False), ("verse", "Bm7", 1, 1, 1, False), ("verse", "Em9", 1, 1, 1, False),
    ("chorus", "Cmaj7", 1, 1, 2, False), ("chorus", "Dsus4", 1, 1, 2, False), ("chorus", "Bm7", 1, 1, 2, False), ("chorus", "Gmaj9", 1, 1, 2, False),
    ("chorus", "D#maj7", 1, 1, 2, False), ("chorus", "Fsus4", 1, 1, 2, False), ("chorus", "A#maj7", 1, 1, 2, False), ("chorus", "Gsus4", 1, 2, 2, False),
]

# the voice (Keys): slow 8ths and quarters, long ends; the chorus lifts about four semitones (its islands a register higher)
VOICE = [
    None, None,
    "r:4 B3:8 D4:8 E4:4 F#4:8 G4:8",
    "G4:4. E4:8 D4:4 E4:4",
    "D4:8 G4:8 A4:4 G4:4 r:8 D4:8",
    "E4:2 r:4 B3:8 D4:8",
    "G4:4. E4:8 D4:4 E4:4",                               # the 2-bar cell (IV - Vsus) returns verbatim
    "D4:8 G4:8 A4:4 G4:4 r:8 D4:8",
    "D4:8 F#4:8 A4:4 B4:4 A4:4",
    "G4:2 F#4:4 r:8 B4:8",
    "r:8 E4:8 G4:8 B4:8 D5:4 B4:8 C5:8",                 # chorus
    "D5:4. C5:8 A4:4 G4:4",
    "F#4:4 A4:8 B4:8 D5:4 B4:8 A4:8",
    "B4:2 A4:4 r:8 G4:8",
    "G4:4 A#4:8 D5:8 D5:4 A#4:4",                        # the borrowed bVI: the tune takes its Bb
    "C5:4. A#4:8 F4:4 A#4:4",
    "D5:4 C5:8 A#4:8 A4:4 F4:4",
    "G4:2. r:4",                                         # sung once; the belt's second pass floats without it
]
# the chorus descant (strings): chord tones above the tune, never on its pitch at the same time
DESCANT = ["E5:2 G5:2", "A5:2 G5:2", "F#5:1", "B5:2 A5:2", "G5:1", "F5:2 A#5:2", "F5:2 A5:2", "F5:2 D5:2"]


def arp(chord):
    """The flowing piano: 16ths up and down over an octave and a half, a rest at the top of the second half (14 strikes a bar)."""
    return "1:16 5 8 3' 5' 3' 8 5 1 5 8 3' 5':8 r:16 8:16"


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        ground = I(chord, C("piano", arp(chord), base="C3", volume=0.5), C("pad", "3:2 3:2", base="C3", volume=0.36))
        lanes = {"ground": ground}
        if VOICE[i]:
            lanes["melody"] = I("auto", C("keys", VOICE[i], tag="voice", gate=2), reg=1 if sec == "chorus" else 0, repeat=1)
            if sec == "chorus":
                # the strings' descant: a slow line of half and whole notes above the tune, on an island a register higher
                lanes["double"] = I("auto", C("strings", DESCANT[i - 10], volume=0.42, tag="descant"), reg=1, repeat=1)
        out.append(col(sec, bars, en, fill, rep, **lanes))
    return out


SONG = Song(
    id="moon-tide", title="moon tide", after="looking for the moon · koyori", bpm=66, key="G", minor=False,
    columns=columns(), moons=[],
    vol={"keys": 1.0, "piano": 0.7, "pad": 0.6, "strings": 0.55},
    tone=0.85, space=1.3, deck=["Am9", "Em7", "Cmaj9"], blurb="a slow night on the water", home="sus4",
)
SONG.order = 5
