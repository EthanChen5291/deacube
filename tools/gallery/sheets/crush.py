"""crush — after "Melt" (ryo / supercell, 2007): the bright in-love pop-rock.

Measured on the source (python3 analyze.py Melt --beats): 170 bpm, D major, 181 bars. Intro: a piano block riff (3.5 strikes a bar,
1.7 notes a strike, top 74-81) over I-IV, then the borrowed bVI-bVII (Bb, C); bass pumping 8ths. Verse: the descending "canon" bass
(I - I/7 - vi - V - IV - iii - IV - V, two beats a chord), bass octave / root-fifth 8ths (6.6 strikes a bar), piano a single-note line
(2.2 strikes a bar). Pre: IV V .. bVI. Chorus: vi V vi IV V iii IV (vi-led, one chord a bar), piano block chords (2.7 notes a strike)
with the tune on top, bass quarters (5.9 a bar). Voice: 3.5 notes a sung bar (quarters 51%, 8ths 26%), steps 56%, repeats 22%,
range 57-81, chorus median 74 vs verse 64 (+10).
Borrowed: the progressions (one chord a bar), the tempo, the form (intro riff -> verse -> pre -> chorus), the energy arc (verse thin,
chorus thick), the roles (piano / bass / strings / kit + the voice), the grooves. Original: every melody, the riff, the lines.
"""
from sheet import Song, Moon, C, I, col, up

# the harmony, one column per chord: (section, chord, bars, repeat, energy, fill)
HARM = [
    ("intro", "Dmaj9", 1, 1, 2, False), ("intro", "Gmaj7", 1, 1, 2, False), ("intro", "A#maj7", 1, 1, 2, False), ("intro", "C7", 1, 1, 2, False),
    ("verse", "Dmaj7", 1, 2, 1, False), ("verse", "Bm7", 1, 1, 1, False), ("verse", "A7", 1, 1, 1, False), ("verse", "Gmaj7", 1, 1, 1, False),
    ("verse", "F#m7", 1, 1, 1, False), ("verse", "Gmaj7", 1, 1, 1, False), ("verse", "A7", 1, 1, 1, False),
    ("pre", "Gmaj7", 1, 1, 2, False), ("pre", "A7", 1, 1, 2, False), ("pre", "A#maj7", 1, 1, 2, False), ("pre", "Asus4", 1, 1, 2, True),
    ("chorus", "Bm7", 1, 1, 2, False), ("chorus", "A7", 1, 1, 2, False), ("chorus", "Bm7", 1, 1, 2, False), ("chorus", "Gmaj7", 1, 1, 2, False),
    ("chorus", "A7", 1, 1, 2, False), ("chorus", "F#m7", 1, 1, 2, False), ("chorus", "Gmaj7", 1, 1, 2, False), ("chorus", "Asus4", 1, 1, 2, False),
]

# the voice (Lead), one entry per column (None = it rests). Each bar keeps to one DeaCube chord's tones (the melody island under it).
# Verse ~A3-A4 around F#4; chorus A4-G5 around D5 (its islands stand a register higher).
VOICE = [
    None, None, None, None,
    "r:8 F#4:8 F#4:8 E4:8 D4:8 E4:8 F#4:4",               # the verse cell (x2 belt: it returns verbatim)
    "B3:8 C#4:8 D4:8 D4:8 C#4:8 B3:8 A3:4",               # the answer, falling
    "C#4:2 r:8 A3:8 B3:8 C#4:8",                          # a held end, a pickup into the next phrase
    "D4:4 F#4:8 A4:8 G4:8 F#4:8 D4:4",                    # the pickup lands; the cell, turned upward
    "E4:8 E4:8 F#4:8 E4:8 C#4:4 r:4",
    "r:8 B3:8 D4:8 E4:8 F#4:8 E4:8 D4:4",
    "E4:4 C#4:8 D4:8 E4:4 r:4",
    "r:4 G4:8 A4:8 B4:4 A4:8 G4:8",                       # pre: climbing, repeated notes, longer values
    "A4:8 A4:8 G4:8 A4:8 B4:4 C#5:4",
    "D5:4 D5:8 A4:8 F4:4 A4:4",                           # over the borrowed Bb
    "E5:4 D5:8 E5:8 D5:4 r:8 C#5:8",                      # ... and the pickup into the chorus
    "D5:8 D5:8 C#5:8 D5:8 F#5:4! E5:8 D5:8",              # the chorus hook (2 bars) ...
    "C#5:4 A4:8 B4:8 C#5:4 r:8 C#5:8",                    # breath, pickup
    "D5:8 D5:8 C#5:8 D5:8 F#5:4! E5:8 D5:8",              # ... returning at pitch
    "G5:4! F#5:8 E5:8 D5:4 r:8 D5:8",
    "E5:8 E5:8 C#5:8 D5:8 E5:4 r:8 E5:8",
    "F#5:8 F#5:8 E5:8 C#5:8 E5:4 r:8 F#5:8",
    "G5:4 F#5:8 E5:8 F#5:4 D5:4",
    "E5:2. r:4",
]

# the intro riff (piano, the chord tone below each note added by `follow`), on the lane the voice takes over
RIFF = ["D5:4 F#5:8 A5:8 r:8 F#5:8 E5:4", "D5:4 F#5:8 B5:8 r:8 F#5:8 D5:4", "D5:4 F5:8 A5:8 r:8 F5:8 D5:4", "E5:4 G5:8 A#5:8 r:8 G5:8 E5:4"]
# the verse / pre piano: one note a strike, three strikes a bar, under the voice (the Asus4 fill bar breaks: no piano)
PIANO = {
    4: "D3:4. F#3:4. A3:4", 5: "D3:4. F#3:4. B3:4", 6: "C#3:4. E3:4. A3:4", 7: "D3:4. B3:4. G3:4",
    8: "C#3:4. A3:4. F#3:4", 9: "D3:4. G3:4. B3:4", 10: "C#3:4. E3:4. G3:4",
    11: "B3:8 D4:8 r:8 B3:8 G3:4 D4:4", 12: "C#4:8 E4:8 r:8 C#4:8 A3:4 E4:4", 13: "D4:8 F4:8 r:8 D4:8 A#3:4 F4:4",
}
# chorus strings: whisper-quiet half notes under the tune
STRINGS = {15: "D4:2 F#4:2", 16: "C#4:2 E4:2", 17: "D4:2 F#4:2", 18: "D4:2 B3:2", 19: "C#4:2 E4:2", 20: "C#4:2 A3:2", 21: "B3:2 D4:2", 22: "D4:1"}

BASS = {
    "intro": "1:8 8 1 8 5 8 1 8",            # root / octave / fifth 8ths
    "verse": "1:8 8 1 8 5 8 1 8",
    "pre": "1:4 1:8 1:8 5:4 8:8 5:8",
    "chorus": "1:4. 1:8 5:4 8:8 5:8",          # quarters with a push (the source's chorus bass)
}
FILL_BASS = "1:4 r:4 5:4 r:4"                # the fill bar: its echoes fill the rests
# placed variation: the last bar of every 4-bar phrase turns up the chord into the next one
TURN = {"intro": "1:8 8 1 8 5 8 3 5", "verse": "1:8 8 1 8 5 8 3 5", "pre": "1:4 1:8 1:8 5:8 8:8 5:8 3:8", "chorus": "1:4. 1:8 5:8 8:8 5:8 3:8"}


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        first = next(k for k, h in enumerate(HARM) if h[0] == sec)
        bass = FILL_BASS if fill else (TURN[sec] if (i - first) % 4 == 3 else BASS[sec])
        cubes = [C("bass", bass, base="A1", volume=0.9)]
        if i in PIANO:
            cubes.append(C("piano", PIANO[i], volume=0.6))
        if i in STRINGS:
            cubes.append(C("strings", STRINGS[i], volume=0.45))
        ground = I(chord, *cubes)
        melody = None
        if sec == "intro":
            melody = I("auto", C("piano", RIFF[i], follow=2, volume=0.85, tag="riff"))
        elif VOICE[i]:
            vc = [C("lead", VOICE[i], tag="voice")]
            if sec == "chorus":
                vc.append(C("piano", up(VOICE[i], 12), octave=1, follow=2, volume=0.62, tag="double"))
            melody = I("auto", *vc, reg=1 if sec == "chorus" else 0, repeat=rep)
        out.append(col(sec, bars, en, fill, rep, ground=ground, melody=melody))
    return out


DRUMS = [
    C("drums", "K4:4. K2:8 K4:2"),                        # kick 1, &2, 3
    C("drums", "r:4 S4:4 r:4 S3:4"),                      # snare 2, 4
    C("drums", "H3:8 H1:8 H3:8 H1:8 H3:8 H1:8 H4:8 H1:8"),  # hats: accent on the beats
]

SONG = Song(
    id="crush", title="crush", after="melt · ryo", bpm=170, key="D", minor=False,
    columns=columns(), moons=[Moon(DRUMS, at_col=4)],
    vol={"lead": 1.0, "piano": 0.55, "bass": 0.5, "strings": 0.45, "drums": 0.55},
    tone=1.0, space=0.9, deck=["Em7", "Bm9", "C7"], blurb="in love, running late", home="maj7",
)
SONG.order = 1
