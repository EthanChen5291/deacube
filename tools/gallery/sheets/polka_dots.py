"""polka dots — after "Ievan Polkka" (traditional, the Otomania Miku arrangement, 2007): the playful dance.

Measured on the source (python3 analyze.py Ievan --beats): 119 bpm, F# minor, 72 bars. Opening: the tonic vamp (i) with the voice on
one chanted pitch (repeats 100%), the bass OOM on the beats (root / fifth, 5.2 strikes a bar) and the accompaniment PAH on the off-beat
8ths (16th slots strongest on &1 &2 &3 &4, 7.6 strikes a bar). Verse: i | v i | i | v i | i V7 | ... (the minor v and the harmonic-minor
V7 around the tonic), the voice a fast scat: 10.4 notes a sung bar, 16ths 51%, REPEATS 48%, range 61-73 around G#4. Last section: i and
bIII (A major) alternating. Interlude: block8 accompaniment (10.8 strikes a bar).
Borrowed: the progressions (i / v / V7 one chord a bar, the i - bIII alternation for the chorus, iv - V7 cadences), the tempo, the
oom-pah (bass on the beats, chords on the "and"), the form, the roles (bass, keys, bells, kit + the voice). Original: every melody
(the traditional tune is not used), the bell riff, the lines.
"""
from sheet import Song, Moon, C, I, col, up, held

HARM = [
    ("intro", "F#m9", 2, 2, 2, False),
    ("verse", "F#m7", 1, 1, 2, False), ("verse", "C#m7", 1, 1, 2, False), ("verse", "F#m7", 1, 1, 2, False), ("verse", "C#7", 1, 1, 2, False),
    ("verse", "F#m7", 1, 1, 2, False), ("verse", "C#m7", 1, 1, 2, False), ("verse", "Bm7", 1, 1, 2, False), ("verse", "C#7", 1, 1, 2, True),
    ("chorus", "F#m7", 1, 1, 3, False), ("chorus", "Amaj7", 1, 1, 3, False), ("chorus", "F#m7", 1, 1, 3, False), ("chorus", "Amaj7", 1, 1, 3, False),
    ("chorus", "Bm7", 1, 1, 3, False), ("chorus", "C#7", 1, 1, 3, False), ("chorus", "F#m7", 1, 1, 3, False), ("chorus", "C#7", 1, 1, 3, False),
    ("tag", "F#m9", 2, 2, 2, False),
]

# the voice: a fast scat (16ths, repeated notes) in the verse, bouncing 8ths in the chorus (a register higher, the 2-bar cell over
# i - bIII returning), and a chanted tag on one pitch
VOICE = [
    None,
    "F#4:16 F#4:16 F#4:8 E4:8 F#4:8 G#4:8 A4:8 G#4:8 F#4:8",
    "G#4:16 G#4:16 G#4:8 F#4:8 E4:8 F#4:8 G#4:8 B4:8 G#4:8",
    "F#4:16 F#4:16 F#4:8 E4:8 F#4:8 G#4:8 A4:8 G#4:8 F#4:8",  # the bar returns
    "G#4:8 F4:8 G#4:8 B4:8 C#5:4 r:4",                      # over V7: the leading tone E# (written F)
    "C#5:16 C#5:16 C#5:8 B4:8 A4:8 B4:8 C#5:8 E5:8 C#5:8",
    "G#4:16 G#4:16 G#4:8 F#4:8 E4:8 F#4:8 G#4:8 B4:8 G#4:8",
    "A4:16 A4:16 A4:8 B4:8 C#5:8 D5:8 C#5:8 B4:8 A4:8",
    "G#4:8 F4:8 G#4:8 B4:8 C#5:4 r:8 C#5:8",
    "C#5:8 C#5:8 B4:8 C#5:8 E5:8! E5:8 C#5:4",             # chorus: bouncing 8ths ...
    "B4:8 B4:8 A4:8 B4:8 C#5:8 C#5:8 A4:4",
    "C#5:8 C#5:8 B4:8 C#5:8 E5:8! E5:8 C#5:4",             # ... the 2-bar cell returns
    "B4:8 B4:8 A4:8 B4:8 C#5:8 C#5:8 A4:4",
    "D5:8 D5:8 C#5:8 B4:8 A4:8 B4:8 C#5:4",
    "C#5:8 C#5:8 B4:8 G#4:8 B4:8 C#5:8 G#4:4",
    "A4:8 A4:8 G#4:8 A4:8 C#5:4 A4:4",
    "G#4:4 B4:4 C#5:4 r:4",
    "C#5:4 C#5:4 C#5:4 C#5:4 | C#5:8 B4:8 A4:8 G#4:8 A4:2",  # the chant (the belt sings it twice)
]
# the intro: a bell riff over the vamp (two bars, the belt plays it twice)
RIFF = "F#4:8 A4:8 C#5:8 A4:8 E5:8 C#5:8 A4:8 C#5:8 | G#4:8 A4:8 C#5:8 A4:8 G#4:8 E4:8 F#4:4"
OOM = "1:4 5:8 5:8 1:4 5:8 5:8"              # the bass on the beats: root, fifth (and a pickup on the "and")
PAH = "r:8 3:8 r:8 5:8 r:8 3:8 r:8 5:8"        # the chord on the off-beat 8ths
FILL_OOM = "1:4 r:4 5:4 r:4"
TURN_OOM = "1:4 5:8 5:8 3:4 5:8 3:8"           # every 4th bar the oom walks the third


def columns():
    out = []
    for i, (sec, chord, bars, rep, en, fill) in enumerate(HARM):
        first = next(k for k, h in enumerate(HARM) if h[0] == sec)
        oom = FILL_OOM if fill else (TURN_OOM if sec in ("verse", "chorus") and (i - first) % 4 == 3 else OOM)
        cubes = [C("bass", oom, base="G#1", volume=0.9),
                 C("keys", PAH, base="C3", follow=1, gate=0, volume=0.6)]
        ground = I(chord, *cubes)
        lanes = {"ground": ground}
        if sec == "intro":
            lanes["melody"] = I("auto", C("bells", RIFF, volume=0.7, tag="riff"))
        elif VOICE[i]:
            vc = [C("lead", VOICE[i], tag="voice")]
            if sec == "chorus":
                vc.append(C("bells", held(up(VOICE[i], 12)), octave=1, volume=0.5, tag="double"))
            lanes["melody"] = I("auto", *vc, reg=1 if sec == "chorus" else 0)
        out.append(col(sec, bars, en, fill, rep, **lanes))
    return out


DRUMS = [
    C("drums", "K4:4 r:4 K4:4 r:4"),                        # boom on 1 and 3
    C("drums", "r:4 S4:4 r:4 S4:4"),                        # chick on 2 and 4
    C("drums", "r:8 H3:8 r:8 H2:8 r:8 H3:8 r:8 H4:8"),      # hats with the pah
]

SONG = Song(
    id="polka-dots", title="polka dots", after="ievan polkka · otomania", bpm=119, key="F#", minor=True,
    columns=columns(), moons=[Moon(DRUMS, at_col=1)],
    vol={"lead": 1.0, "keys": 0.55, "bass": 0.5, "bells": 0.5, "drums": 0.55},
    tone=1.0, space=0.85, deck=["Bm9", "Dmaj7", "Amaj9"], blurb="spin, spin, spin", home="m9",
)
SONG.order = 6
