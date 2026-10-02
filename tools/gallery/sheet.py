"""The vocabulary the song sheets are written in (sheets/<id>.py import it).

A song is a list of COLUMNS (left to right = time). A column holds up to four stacked islands, one per LANE (front to back:
ground, melody, double, extra); all islands of a column sound together for `bars` bars (x `repeat` passes once the belt lands).
Each island is ONE chord (its tiles are that chord's tones in six inversions) carrying cubes; each cube is a path of notes.

Notes are written as the pitch that SOUNDS; the builder finds the tile that plays it. Tokens (space separated, "|" = a bar line check):
  F#4  Bb3  C5          a pitch (C4 = 60)
  1 3 5 7 9 4 8         a chord degree of the island's chord (4 = sus4's fourth, 8 = root an octave up), placed from the cube's
                        `base` pitch: the root sits in [base, base + 12); ' raises an octave, , lowers one (e.g. 5, = the fifth below)
  r                     a rest (the cube waits silently on a tile)
  K S H O C R P         drums on a Moon: kick, snare, closed hat, open hat (rows 0-3), clap / ride / open-hat-alt (column 5 of rows 1-3),
                        followed by an optional column digit 0-4 (loudness 0.55 + 0.09 x; the kick is always velocity 100)
then  :16 :16. :8 :8. :4 :4. :2 :2. :1 :1.   the length (default: the previous one), or :t<ticks>
then  stickers: ! accent   ? ghost   ~ tie (hold to the next note)   ^ lift (+12)   x2 x3 ratchet
then  a nudge (v9, DeaCube/Nudge.cs): + ++ (one / two scale steps of the song key HIGHER than the tile)   - -- (LOWER): the step plays a note
                        the chord has no tile for; the pitch written is still the one that SOUNDS (the builder finds the tile whose nudged
                        note is that pitch), so `F4:8+` on a Cmaj7 island sits on the E tile and sounds F
"""

DUR = {"16": 6, "16.": 9, "8": 12, "8.": 18, "4": 24, "4.": 36, "2": 48, "2.": 72, "1": 96, "1.": 144}
# drum letter -> (row z, forced column x or None): kick, snare, closed hat, open hat (row 3), clap (row 1 x5), ride (row 3 x5),
# open hat alt (row 2 x5)
DRUM_ROWS = {"K": (0, None), "S": (1, None), "H": (2, None), "O": (3, None), "C": (1, 5), "R": (3, 5), "P": (2, 5)}


class Cube:
    def __init__(self, inst, notes, octave=0, gate=1, follow=0, echo=0, volume=1.0, base=None, mode=0, shimmer=False, tag=None):
        self.inst, self.notes = inst, notes
        self.octave, self.gate, self.follow, self.echo, self.volume = octave, gate, follow, echo, volume
        self.base, self.mode, self.shimmer, self.tag = base, mode, shimmer, tag


class Island:
    def __init__(self, chord, cubes=(), reg=0, root=None, energy=None, fill=None, repeat=None, sleep=False):
        # chord "auto": the builder picks the chord whose tiles hold the cubes' notes (a melody lane: SPEC v5 §3)
        self.chord, self.cubes, self.reg, self.root = chord, list(cubes), reg, root
        self.energy, self.fill, self.repeat, self.sleep = energy, fill, repeat, sleep


class Column:
    def __init__(self, sec, bars=1, energy=2, fill=False, repeat=1, **lanes):
        self.sec, self.bars, self.energy, self.fill, self.repeat = sec, bars, energy, fill, repeat
        self.lanes = lanes          # lane name -> Island


class Moon:
    def __init__(self, cubes, at_col=0, side=-1):
        self.cubes, self.at_col, self.side = list(cubes), at_col, side


class Song:
    def __init__(self, id, title, after, bpm, key, minor, columns, moons=(), vol=None, swing=0.0, tone=1.0, space=1.0,
                 deck=(), blurb="", home=None, source=None, sections_doc=None):
        self.id, self.title, self.after, self.bpm, self.key, self.minor = id, title, after, bpm, key, minor
        self.columns, self.moons, self.vol = list(columns), list(moons), dict(vol or {})
        self.swing, self.tone, self.space, self.deck, self.blurb, self.home = swing, tone, space, list(deck), blurb, home
        self.source, self.sections_doc = source or {}, sections_doc or {}


# ---- shorthand used by the sheets
def C(inst, notes, **kw):
    return Cube(inst, notes, **kw)


def I(chord, *cubes, **kw):
    return Island(chord, cubes, **kw)


def col(sec, bars=1, energy=2, fill=False, repeat=1, **lanes):
    return Column(sec, bars, energy, fill, repeat, **lanes)


def up(line, semis):
    """The same notes transposed by <semis> (pitch tokens only; degrees, rests, drums and stickers unchanged): an octave-up double."""
    import re
    import deacube as D
    out = []
    for tok in line.split():
        m = re.match(r"^([A-G](?:#|b)?-?\d)(.*)$", tok)
        if m:
            out.append(D.name_of(D.midi_of(m.group(1)) + semis) + m.group(2))
        else:
            out.append(tok)
    return " ".join(out)


def held(line):
    """Consecutive repeats of one pitch merged into one longer note (a double that holds where the voice re-strikes)."""
    import re
    toks, out = line.split(), []
    for tok in toks:
        m = re.match(r"^([A-G](?:#|b)?-?\d):?(\d+\.?|t\d+)?(.*)$", tok)
        if m and out:
            pm = re.match(r"^([A-G](?:#|b)?-?\d):(\d+\.?|t\d+)(.*)$", out[-1])
            if pm and pm.group(1) == m.group(1) and m.group(2):
                ticks = lambda d: int(d[1:]) if d.startswith("t") else DUR[d]
                out[-1] = "%s:t%d%s" % (pm.group(1), ticks(pm.group(2)) + ticks(m.group(2)), pm.group(3))
                continue
        out.append(tok)
    return " ".join(out)


def split_bars(line):
    """A continuous line written with | bar lines -> list of per-bar strings."""
    return [b.strip() for b in line.split("|")]
