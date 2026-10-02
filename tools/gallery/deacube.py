"""DeaCube's music model, mirrored in python for the gallery builder (read from the C# sources; keep in sync with them).

Sources: AudioCube_Unity/Assets/KeyBlock.cs (Build: tile pitch grid, getInversion, root clamp), DeaCube/VoiceRules.cs (Fold, rule 4 bass
anchor, ShadowTile, PadTones), DeaCube/Audio/SynthBank.cs (slot ranges, octave shifts, drum rows), DeaCube/MusicTheory.cs (qualities,
RoleOf), ProjectConfig.cs (grid, layout constants), SongManager.NormalizeColumns (lanes: LaneGap), Palette.cs (Instruments).
"""

NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
FLAT = {"Db": 1, "Eb": 3, "Gb": 6, "Ab": 8, "Bb": 10, "Cb": 11, "Fb": 4}

# MusicTheory.Semitones(ChordQuality) — the seven qualities DeaCube knows
QUALITIES = {
    "maj7": [0, 4, 7, 11], "maj9": [0, 4, 7, 11, 14], "m7": [0, 3, 7, 10], "m9": [0, 3, 7, 10, 14],
    "7": [0, 4, 7, 10], "9": [0, 4, 7, 10, 14], "sus4": [0, 5, 7, 10],
}
# VibeKind of a quality (Vibe.Of)
VIBE = {"maj7": 0, "maj9": 1, "m7": 2, "m9": 3, "7": 4, "9": 5, "sus4": 6}
VIBE_WORD = ["sunny", "dreamy", "rainy", "moonlit", "stormy", "spicy", "floaty"]

# ProjectConfig
NUM_INV = 6
SPACING = 1.12
TILE = 1.0
PAD = 0.85
LANE_GAP = 1.8
COLUMN_GAP = 3.7
MOON_COLS, MOON_ROWS = 6, 4
TPB = 24                     # ticks per beat
UI_TICKS = [6, 9, 12, 18, 24, 36, 48, 72, 96, 144]

EDGE_INSET = TILE * 0.5 + PAD


def depth_of(rows):
    return (rows - 1) * SPACING + TILE + 2 * PAD


ISLAND_WIDTH = (NUM_INV - 1) * SPACING + TILE + 2 * PAD

# SynthBank.Defs: name, low, high, octaveShift, drums
SLOTS = [
    ("keys", 41, 88, 0, False), ("pluck", 40, 84, 0, False), ("pad", 48, 79, 0, False), ("lead", 55, 91, 0, False),
    ("bass", 28, 55, -1, False), ("bells", 60, 96, 0, False), ("strings", 43, 88, 0, False), ("choir", 48, 84, 0, False),
    ("piano", 36, 96, 0, False), ("drums", 27, 87, 0, True),
]
INSTRUMENT = {s[0]: i for i, s in enumerate(SLOTS)}   # Instruments.SynthSlot is the identity
SUSTAINING = {2, 6, 7}
DRUM_KIT = [36, 38, 42, 46, 39, 37, 45, 48, 49, 51, 54, 56, 75, 80]


def pc_of(name):
    name = name.strip()
    if len(name) >= 2 and name[:2] in FLAT:
        return FLAT[name[:2]], name[2:]
    if len(name) >= 2 and name[1] == "#":
        return NAMES.index(name[:2]), name[2:]
    return NAMES.index(name[0]), name[1:]


def parse_chord(spec):
    """'Dmaj7' / 'Bm9' / 'A7' / 'Gsus4' / 'F#m7' -> (root pc, quality)."""
    pc, rest = pc_of(spec)
    q = rest if rest else "maj7"
    if q not in QUALITIES:
        raise ValueError("unknown chord quality in %r (DeaCube knows %s)" % (spec, ", ".join(QUALITIES)))
    return pc, q


def midi_of(name):
    """'F#4' -> 66 (C4 = 60)."""
    pc, rest = pc_of(name)
    return pc + 12 * (int(rest) + 1)


def name_of(m):
    return NAMES[m % 12] + str(m // 12 - 1)


def chord_name(root, q):
    """MusicTheory.ChordName."""
    return NAMES[root % 12] + q


def clamp_root(root):
    """KeyBlock.BuildFromData: a root below 36 is raised two octaves, above 84 lowered one."""
    if root < 36:
        root += 24
    if root > 84:
        root -= 12
    return root


def inversions(semis):
    """KeyBlock.Build: column x = the chord with its lowest tone raised an octave x times (getInversion)."""
    inv = sorted(semis)
    out = [list(inv)]
    for _ in range(1, NUM_INV):
        low = inv[0]
        inv = sorted(inv[1:] + [low + 12])
        out.append(list(inv))
    return out


def grid(root, q, reg):
    """midi[x][z] of an island (root already clamped)."""
    semis = QUALITIES[q]
    return [[root + v + 12 * reg for v in col] for col in inversions(semis)]


def role_of(semi):
    """MusicTheory.RoleOf: root, third, fifth, seventh, extension (1 2 5 9), other."""
    s = semi % 12
    if s == 0:
        return "root"
    if s in (3, 4):
        return "third"
    if s in (6, 7, 8):
        return "fifth"
    if s in (10, 11):
        return "seventh"
    if s in (1, 2, 5, 9):
        return "extension"
    return "other"


def clamp_to_register(slot, midi):
    name, low, high, octs, drums = SLOTS[slot]
    if drums:
        return max(0, min(127, midi))
    midi += 12 * octs
    lo, hi = min(low, high), max(low, high)
    while midi < lo:
        midi += 12
    while midi > hi:
        midi -= 12
    if midi < lo:
        midi = lo
    return max(0, min(127, midi))


def fold(slot, midi, reg):
    """VoiceRules.Fold(slot, midi, reg)."""
    name, low, high, octs, drums = SLOTS[slot]
    if reg == 0 or drums:
        midi = clamp_to_register(slot, midi)
        if drums:
            return midi
        if slot != 4:
            while midi < 48:
                midi += 12
        if slot != 5:
            while midi > 96:
                midi -= 12
        return max(0, min(127, midi))
    reg = max(-2, min(2, reg))
    midi += 12 * octs
    lo = min(low, high) + 12 * reg
    hi = max(low, high) + 12 * reg
    while lo < 21:
        lo += 12
        hi += 12
    while hi > 108:
        lo -= 12
        hi -= 12
    while midi < lo:
        midi += 12
    while midi > hi:
        midi -= 12
    if midi < lo:
        midi = lo
    floor, ceil = 48 + 12 * reg, 96 + 12 * reg
    if slot != 4:
        while midi < floor and midi + 12 <= 108:
            midi += 12
    if slot != 5:
        while midi > ceil and midi - 12 >= 21:
            midi -= 12
    return max(0, min(127, midi))


MAJOR_STEPS = [0, 2, 4, 5, 7, 9, 11]
MINOR_STEPS = [0, 2, 3, 5, 7, 8, 10]


def scale_step(midi, steps, tonic, minor):
    """DeaCube/Nudge.cs Nudge.Semis: the semitones that take <midi> <steps> scale steps up (> 0) / down (< 0) in the key (tonic pitch class,
    minor): Harmony.StepShift's walk for one note. A note off the scale walks from the scale note just below it (else just above) and keeps
    its offset. 0 for 0 steps."""
    if steps == 0:
        return 0
    st = MINOR_STEPS if minor else MAJOR_STEPS
    t = tonic % 12

    def degree(m):
        rel = (m - t) % 12
        return st.index(rel) if rel in st else -1

    d, off = degree(midi), 0
    if d < 0:
        d, off = degree(midi - 1), 1
        if d < 0:
            d, off = degree(midi + 1), -1
    if d < 0:
        return 0
    base = midi - off
    oct_ = (base - t) // 12
    idx = d + steps
    oct_move = idx // 7
    idx -= oct_move * 7
    return t + 12 * (oct_ + oct_move) + st[idx] + off - midi


def sounding(slot, g, x, z, root, reg, octave=0, lift=False, downbeat=False, nudge=0, key=None):
    """The pitch a cube on tile (x, z) plays (VoiceRules.Resolve rules 2, 4, 5; no stack: gallery islands never share a tile).
    v9 (N): <nudge> scale steps of the song <key> (tonic pc, minor) above / below the tile (Nudge.Semis, on the base pitch like a bend);
    a nudged note skips the bass anchor (it is "bent": its pitch is not the tile's)."""
    tile = g[x][z]
    semis = scale_step(tile, nudge, key[0], key[1]) if nudge and key is not None else 0
    midi = tile + semis + 12 * octave + (12 if lift else 0)
    if slot == 4 and downbeat and semis == 0 and role_of(tile - root) in ("seventh", "extension"):
        midi = root + 12 * octave + 12 * reg
    return fold(slot, midi, reg)


def shadow_tile(g, root, x, z, up):
    """VoiceRules.ShadowTile: the nearest row above / below in the same column at least 3 semitones away (wrapping by octaves)."""
    rows = len(g[0])
    if rows <= 1:
        return None
    d = 1 if up else -1
    for s in range(1, rows):
        zz, off = z + d * s, 0
        if zz >= rows:
            zz -= rows
            off = 12
        elif zz < 0:
            zz += rows
            off = -12
        if abs(g[x][zz] + off - g[x][z]) >= 3:
            return zz, off
    return None


def pad_tones(g, x, z):
    """VoiceRules.PadTones: up to two rows below (wrapping an octave down): (z, octave offset) pairs."""
    rows = len(g[0])
    out = []
    zz, off = z, 0
    for _ in range(min(2, rows - 1)):
        zz -= 1
        if zz < 0:
            zz += rows
            off -= 12
        out.append((zz, off))
    return out


def drum_piece(x, z, rows=MOON_ROWS):
    """VoiceRules.DrumPiece: row -> kit piece; column 5 selects clap / open hat / ride on rows 1-3."""
    piece = DRUM_KIT[z % rows % len(DRUM_KIT)]
    if x >= 5 and 1 <= z <= 3:
        piece = 39 if z == 1 else (46 if z == 2 else 51)
    return piece


def vibe_of(q):
    return VIBE[q]
