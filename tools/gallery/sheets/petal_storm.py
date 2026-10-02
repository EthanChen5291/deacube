"""petal storm — after "Senbonzakura" (Kurousa-P, 2011): the wa-rock festival.

Measured on the source (python3 analyze.py Senbonzakura --beats; the file sits a 16th early, shifted back): 154 bpm, D minor, 154 bars.
Intro: a block8 riff over bVI bVII | I(sus, thirdless: D G A C E) bIII | bVI bVII | i IV (the "wa" colour is the missing third);
verse: bVI bIII (bVI bVII) i | bVI bIII IV(sus2) V(sus4), block4 piano (4.2 strikes, 1.5 notes a strike), bass riff / quarters
(5.2 a bar, repeats 50%); chorus: i bVI bVII bIII twice as fast (two chords a bar) with the turnaround bVI - bII (Eb) - V, piano
block8 (6.5 strikes, 1.85 notes a strike, top 76) doubling the tune a fourth / octave up, bass syncopated root-fifth 8ths
(8 a bar, 16th slots 1 &1 e2 &2 3 &3 e4 &4). Voice: 5.7 notes a sung bar, 8ths 56%, steps 54% thirds 22%, range 55-76,
verse median 62 -> chorus 69; verse phrases start off the beat (81%), chorus phrases on the downbeat (60%).
Borrowed: those progressions (one chord a bar: the chorus loop at half its harmonic speed), the tempo, the pushes, the form, the arc,
the roles (a koto-like pluck riff, piano, bass, strings, kit + the voice). Original: every melody, the riff, the lines.
"""
from sheet import Song, Moon, C, I, col, up, held

HARM = [
    ("intro", "A#maj7", 1, 1, 2, False), ("intro", "C9", 1, 1, 2, False), ("intro", "Dsus4", 1, 1, 2, False), ("intro", "Fmaj7", 1, 1, 2, False),
    ("verse", "A#maj7", 1, 1, 1, False), ("verse", "Fmaj7", 1, 1, 1, False), ("verse", "C7", 1, 1, 1, False), ("verse", "Dm7", 1, 1, 1, False),
    ("verse", "A#maj7", 1, 1, 1, False), ("verse", "Fmaj7", 1, 1, 1, False), ("verse", "Gm7", 1, 1, 1, False), ("verse", "Asus4", 1, 1, 1, False),
    ("pre", "Dm7", 1, 1, 2, False), ("pre", "A#maj7", 1, 1, 2, False), ("pre", "Fmaj7", 1, 1, 2, False), ("pre", "C7", 1, 1, 2, True),
    ("chorus", "Dm9", 1, 1, 3, False), ("chorus", "A#maj7", 1, 1, 3, False), ("chorus", "C7", 1, 1, 3, False), ("chorus", "Fmaj7", 1, 1, 3, False),
    ("chorus", "Dm9", 1, 1, 3, False), ("chorus", "A#maj7", 1, 1, 3, False), ("chorus", "D#maj7", 1, 1, 3, False), ("chorus", "A7", 1, 1, 3, False),
]

# the voice: verse around D4 (phrases start off the beat), chorus around C5 on 8ths (phrases on the downbeat; the 2-bar cell over
# i - bVI returns verbatim); the melody islands of the chorus stand a register higher
VOICE = [
    None, None, None, None,
    "r:8 C4:8 D4:8 F4:8 F4:4 D4:8 C4:8",                  # verse cell over bVI ...
    "C4:8 F4:8 F4:8 E4:8 F4:4 r:8 G4:8",                  # ... and bIII
    "G4:8 E4:8 D4:8 C4:8 D4:4 r:4",
    "r:8 A3:8 D4:8 E4:8 F4:8 E4:8 D4:4",
    "r:8 C4:8 D4:8 F4:8 F4:4 D4:8 C4:8",                  # the 2-bar cell returns verbatim
    "C4:8 F4:8 F4:8 E4:8 F4:4 r:8 G4:8",
    "A4:8 G4:8 F4:8 D4:8 F4:8 G4:8 A4:4",
    "A4:4 G4:8 E4:8 D4:4 r:4",
    "r:8 D4:8 D4:8 G4:8 A4:8 G4:8 A4:4",                  # pre: climbing on repeated notes
    "r:8 A4:8 A4:8 C5:8 D5:8 C5:8 A4:4",
    "C5:8 C5:8 A4:8 G4:8 A4:4 C5:4",
    "D5:4. C5:8 A4:4 r:8 C5:8",                           # pickup into the chorus
    "A4:8 A4:8 C5:8 D5:8 E5:8 D5:8 C5:8 A4:8",            # chorus: straight 8ths ...
    "D5:8 C5:8 A4:8 C5:8 D5:4 A4:4",
    "E5:8! E5:8 D5:8 C5:8 G4:8 C5:8 E5:4",
    "F5:4 E5:8 C5:8 A4:4 r:8 A4:8",
    "A4:8 A4:8 C5:8 D5:8 E5:8 D5:8 C5:8 A4:8",            # ... the hook returns (i - bVI again)
    "D5:8 C5:8 A4:8 C5:8 D5:4 A4:4",
    "G5:8! G5:8 F5:8 D#5:8 D5:4 A#4:4",                   # the Neapolitan bII
    "C#5:4 E5:8 C#5:8 A4:2",
]
# the intro riff: a koto-like pluck in pentatonic 16th-8th figures, a chord tone under each note (follow)
RIFF = [
    "D5:16 F5:16 A5:8 F5:8 D5:8 r:8 C5:8 D5:8 F5:8",
    "E5:16 G5:16 A#5:8 G5:8 E5:8 r:8 D5:8 E5:8 G5:8",
    "D5:16 G5:16 A5:8 G5:8 D5:8 r:8 C5:8 D5:8 G5:8",
    "C5:16 F5:16 A5:8 F5:8 C5:8 r:8 E5:8 F5:8 G5:8",
]
# chorus strings: half notes under the tune (never on its pitch)
STRINGS = ["F4:2 D4:2", "F4:2 D4:2", "E4:2 C4:2", "C4:2 A3:2", "F4:2 D4:2", "F4:2 D4:2", "G4:2 D#4:2", "E4:2 C#4:2"]
BASS = {
    "intro": "1:8 1 5 1 8 1 5 1",
    "verse": "1:4 1:8 1:8 1:4 5:4",                        # quarters with a push, the note repeated (the source's verse riff)
    "pre": "1:8 1 5 1 8 1 5 1",
    "chorus": "1:8 1:8. 5:16 8:8 1:8 1:8. 5:16 8:8",       # the source's syncopated chorus bass: 1 &1 e2 &2 3 &3 e4 &4
}
FILL_BASS = "1:4 r:4 5:4 r:4"
TURN = {"intro": "1:8 1 5 1 8 5 3 5", "verse": "1:4 1:8 1:8 3:4 5:4", "pre": "1:8 1 5 1 8 5 3 5",
        "chorus": "1:8 1:8. 5:16 8:8 1:8 5:8. 3:16 5:8"}      # placed variation at every 4th bar


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        first = next(k for k, h in enumerate(HARM) if h[0] == sec)
        bass = FILL_BASS if fill else (TURN[sec] if (i - first) % 4 == 3 else BASS[sec])
        cubes = [C("bass", bass, base="A1", volume=0.9)]
        if sec == "verse":
            cubes.append(C("piano", "3:4 5:4 3:4 5:8 3:8", base="A2", follow=1, volume=0.55))     # block4 under the voice
        elif sec == "pre" and not fill:
            cubes.append(C("piano", "3:8 5 3 5 3 5 3 5", base="A2", follow=1, volume=0.55))      # block8, building
        elif sec == "chorus":
            cubes.append(C("strings", STRINGS[i - 16], volume=0.45))
        ground = I(chord, *cubes)
        melody = None
        if sec == "intro":
            melody = I("auto", C("pluck", RIFF[i], follow=2, volume=0.8, tag="riff"))
        elif VOICE[i]:
            vc = [C("lead", VOICE[i], tag="voice")]
            if sec == "chorus":
                vc.append(C("piano", held(up(VOICE[i], 12)), octave=1, follow=2, volume=0.6, tag="double"))
            melody = I("auto", *vc, reg=1 if sec == "chorus" else 0, repeat=rep)
        out.append(col(sec, bars, en, fill, rep, ground=ground, melody=melody))
    return out


DRUMS = [
    C("drums", "K4:4. K2:8 K4:8 K2:8 r:4"),                # kick 1, &2, 3, &3 (don-don)
    C("drums", "r:4 S4:4 r:4 S4:8 S2:8"),                  # snare 2, 4 and a softer &4
    C("drums", "H3:8 H1:8 H3:8 H1:8 H3:8 H1:8 H3:8 O2:8"), # hats, an open hat into the bar
]

SONG = Song(
    id="petal-storm", title="petal storm", after="senbonzakura · kurousa-p", bpm=154, key="D", minor=True,
    columns=columns(), moons=[Moon(DRUMS, at_col=4)],
    vol={"lead": 1.0, "pluck": 0.65, "piano": 0.55, "bass": 0.5, "strings": 0.45, "drums": 0.55},
    tone=1.0, space=0.95, deck=["Gm9", "A7", "C9"], blurb="a festival in falling petals", home="m9",
)
SONG.order = 2
