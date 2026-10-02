"""night dash — after "Yoru ni Kakeru" (YOASOBI / Ayase, 2019): the piano-driven rushing J-pop.

Measured on the source (python3 analyze.py "Yoru ni" --beats): 130 bpm, Eb major (written D#), 140 bars. Intro: a piano figure over
IV V | iii vi | IV .. | I (the royal road). Verse: vi IV | III7 vi | V IV | iii vi (two chords a bar), piano arpeggio 8ths (6.9 strikes a
bar, 1.2 notes a strike), bass octaves (55% octave moves) with a 16th push before beat 4. Pre: bVII (Db) I ii V. Chorus: the royal road
IV V iii vi with the bVII and a III7 turnaround, piano block8 (8.5 strikes, 1.8 notes a strike, top 77), bass root-fifth 8ths pushing
on the "a" of 3 (slots 1 &1 2 &2 3 a3 4 &4). Voice: 7.2 notes a sung bar (8ths 65%, 16ths 20%), steps 52% thirds 24%, range 55-77,
verse median 63 -> chorus 67; phrases start off the beat (55%) or on a beat (37%).
Borrowed: the progressions (one chord a bar, in the source's order), the tempo, the grooves (the four-on-the-floor dance-pop kit is the
recording's, not the file's), the form, the arc, the roles (piano riff / arpeggio / block, bass, synth strings, kit + the voice).
Original: every melody, the riff, the lines.
"""
from sheet import Song, Moon, C, I, col, up, held

HARM = [
    ("intro", "G#maj7", 1, 1, 2, False), ("intro", "A#7", 1, 1, 2, False), ("intro", "Gm7", 1, 1, 2, False), ("intro", "Cm7", 1, 1, 2, False),
    ("verse", "Cm7", 1, 1, 1, False), ("verse", "G#maj7", 1, 1, 1, False), ("verse", "G7", 1, 1, 1, False), ("verse", "Cm7", 1, 1, 1, False),
    ("verse", "A#7", 1, 1, 1, False), ("verse", "G#maj7", 1, 1, 1, False), ("verse", "Gm7", 1, 1, 1, False), ("verse", "Cm7", 1, 1, 1, False),
    ("pre", "C#maj7", 1, 1, 2, False), ("pre", "D#maj9", 1, 1, 2, False), ("pre", "Fm7", 1, 1, 2, False), ("pre", "A#sus4", 1, 1, 2, True),
    ("chorus", "G#maj7", 1, 1, 2, False), ("chorus", "A#7", 1, 1, 2, False), ("chorus", "Gm7", 1, 1, 2, False), ("chorus", "Cm7", 1, 1, 2, False),
    ("chorus", "C#maj7", 1, 1, 2, False), ("chorus", "A#7", 1, 1, 2, False), ("chorus", "D#maj9", 1, 1, 2, False), ("chorus", "G7", 1, 1, 2, False),
]

# the voice: dense 8ths with 16th pairs (the source sings 7 notes a bar); verse around D#4, chorus around G#4 (+4, as the source)
VOICE = [
    None, None, None, None,
    "r:8 G4:8 G4:8 D#4:8 D4:8 D#4:16 D4:16 C4:8 D4:8",     # verse: conversational 8ths, 16th turns
    "D#4:8 D#4:8 C4:8 D#4:8 G4:8 G#4:8 G4:4",
    "F4:8 F4:8 D4:8 F4:8 G4:8 F4:8 D4:4",
    "D#4:4 r:8 C4:8 D4:8 D#4:8 G4:8 A#4:8",
    "A#4:8 A#4:8 G#4:8 F4:8 D4:16 F4:16 G#4:8 F4:4",
    "D#4:8 D#4:8 C4:8 D#4:8 G4:8 G#4:8 A#4:4",
    "A#4:8 G4:8 F4:8 D4:8 F4:8 G4:8 A#4:4",
    "G4:8 D#4:8 D4:8 C4:8 D4:4 r:4",
    "r:8 F4:8 G#4:8 G#4:8 C5:8 G#4:8 F4:4",               # pre: over the borrowed bVII (Db)
    "G4:8 F4:8 D#4:8 F4:8 G4:8 A#4:8 G4:4",
    "G#4:8 G#4:8 G4:8 F4:8 G4:8 G#4:8 C5:4",
    "A#4:4. G#4:8 F4:4 r:8 A#4:8",                        # pickup into the chorus
    "G#4:8 G#4:8 A#4:8 C5:8 A#4:8 G#4:8 G4:8 G#4:8",        # chorus: rushing 8ths up and down the 9th chords' runs
    "A#4:8 A#4:8 C5:8 D5:8 C5:8 A#4:8 G#4:4",
    "G4:8 G4:8 F4:8 G4:8 A#4:8 G4:8 F4:4",
    "D#5:8 D5:8 C5:8 A#4:8 C5:4 r:8 G4:8",
    "F4:8 F4:8 G#4:8 C5:8 C#5:8 C5:8 G#4:4",
    "A#4:8 A#4:8 C5:8 D5:8 C5:8 A#4:8 G#4:4",              # the bar over V returns
    "D#5:4 D5:8 D#5:8 F5:8! D#5:8 D5:4",                   # the peak, on the tonic
    "D5:4 B4:4 G4:4 r:4",
]
# the intro riff: one piano figure (8ths with a 16th pair) sequenced through the royal road
RIFF = [
    "D#5:8 C5:8 G#4:8 C5:16 D#5:16 G5:8 D#5:8 C5:8 D#5:8",
    "F5:8 D5:8 A#4:8 D5:16 F5:16 G#5:8 F5:8 D5:8 F5:8",
    "D5:8 A#4:8 G4:8 A#4:16 D5:16 F5:8 D5:8 A#4:8 D5:8",
    "D#5:8 C5:8 G4:8 C5:16 D#5:16 G5:8 D#5:8 D5:8 C5:8",
]
# the verse piano: an 8th-note arpeggio under the voice
ARP = {
    4: "C3:8 G3:8 A#3:8 G3:8 D#3:8 G3:8 A#3:8 G3:8", 5: "C3:8 D#3:8 G3:8 D#3:8 C3:8 D#3:8 G3:8 D#3:8",
    6: "D3:8 F3:8 B3:8 F3:8 D3:8 F3:8 B3:8 F3:8", 7: "C3:8 G3:8 A#3:8 G3:8 D#3:8 G3:8 A#3:8 D#3:8",
    8: "D3:8 F3:8 A#3:8 F3:8 D3:8 F3:8 G#3:8 F3:8", 9: "C3:8 D#3:8 G3:8 D#3:8 C3:8 D#3:8 G3:8 C3:8",
    10: "D3:8 F3:8 A#3:8 F3:8 D3:8 F3:8 A#3:8 F3:8", 11: "C3:8 D#3:8 G3:8 A#3:8 G3:8 D#3:8 G3:8 D#3:8",
}
CHORUS_STRINGS = ["C4:2 D#4:2", "D4:2 F4:2", "D4:2 A#3:2", "D#4:2 C4:2", "C#4:2 F4:2", "D4:2 F4:2", "D#4:2 G4:2", "D4:2 B3:2"]
BASS = {
    "intro": "1:8 1 1 1 1:8. 1:16 1:8 1",               # the source's chorus push on the "a" of 3
    "verse": "1:8 8:8 1:8 8:8 1:8. 8:16 1:8 8:8",       # octaves with the 16th push before beat 4
    "pre": "1:8 8:8 1:8 8:8 1:8. 8:16 1:8 8:8",
    "chorus": "1:8 1 5 1 1:8. 5:16 8:8 5",              # root-fifth 8ths, the push on the "a" of 3
}
FILL_BASS = "1:4 r:4 5:4 r:4"
TURN = {"intro": "1:8 1 1 1 1:8. 5:16 3:8 5", "verse": "1:8 8:8 1:8 8:8 1:8. 8:16 5:8 3:8", "pre": "1:8 8:8 1:8 8:8 1:8. 8:16 5:8 3:8",
        "chorus": "1:8 1 5 1 1:8. 5:16 3:8 5"}             # placed variation at every 4th bar


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        first = next(k for k, h in enumerate(HARM) if h[0] == sec)
        bass = FILL_BASS if fill else (TURN[sec] if (i - first) % 4 == 3 else BASS[sec])
        cubes = [C("bass", bass, base="G#1", volume=0.9)]
        if i in ARP:
            cubes.append(C("piano", ARP[i], volume=0.55))
        elif sec == "pre" and not fill:
            cubes.append(C("piano", "3:8 5 3 5 3 5 3 5", base="A2", follow=1, volume=0.5))
        elif sec == "chorus":
            cubes.append(C("strings", CHORUS_STRINGS[i - 16], volume=0.45))
        ground = I(chord, *cubes)
        melody = None
        if sec == "intro":
            melody = I("auto", C("piano", RIFF[i], volume=0.8, tag="riff"))
        elif VOICE[i]:
            vc = [C("lead", VOICE[i], tag="voice")]
            if sec == "chorus":
                vc.append(C("piano", held(up(VOICE[i], 12)), octave=1, follow=2, volume=0.6, tag="double"))
            melody = I("auto", *vc)
        out.append(col(sec, bars, en, fill, rep, ground=ground, melody=melody))
    return out


DRUMS = [
    C("drums", "K4:4 K2:4 K4:4 K2:4"),                     # four on the floor
    C("drums", "r:4 C:4 r:4 C:4"),                         # claps on 2 and 4
    C("drums", "H1:8 O2:8 H1:8 O3:8 H1:8 O2:8 H1:8 O3:8"), # closed hats on the beat, open hats on the "and"
]

SONG = Song(
    id="night-dash", title="night dash", after="yoru ni kakeru · yoasobi", bpm=130, key="D#", minor=False,
    columns=columns(), moons=[Moon(DRUMS, at_col=4)],
    vol={"lead": 1.0, "piano": 0.6, "bass": 0.5, "strings": 0.45, "drums": 0.5},
    tone=1.0, space=0.9, deck=["Fm9", "Cm9", "C#maj7"], blurb="neon streets after midnight", home="maj9",
)
SONG.order = 4
