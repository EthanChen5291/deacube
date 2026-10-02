#!/usr/bin/env python3
"""A MIDI file as a DeaCube song built the way a player builds one (python3 stdlib only): "get proto", rounds 2 and 3 (CONFIG3: plan_song3 —
nudges, a stairs on every descending measure at its own measure, launches on the crash bars, rewind, lead keyboards, a pedal, fragments,
a device signature per section; see docs/deacube-gallery.md section 7).

usage:
  python3 midi_grid.py <import id> [--sections N] [--out FILE] [--table]
      plans imports/<id>.py's song, prints its plan and its numbers against the MIDI (harmony coverage per bar and voice, the groove, the
      melody's contour, the devices and where they sit). --sections N stops after N sections (a draft); --out writes the SongState JSON there.
      build_gallery.py writes the gallery's file and its index entry.

The representation (docs/deacube-gallery.md, "get proto"; the user: "the point of deacube is to abstract away the piano"):
  * HARMONY: one chord card per bar (a chord grid holds one chord; the cards of the 4-bar loop, plus colour cards where the song turns);
  * LANES = VOICES: per bar one 1-bar chord grid per voice role, stacked in the bar's column, low to high: bass (register -1), harmony (piano,
    e.piano, the strings' swell; register 0), lead (the sax), high (flute, music box, square, ocarina, the fx; register +1, raised on its turn);
    drums on Moons (one per 8-bar phrase, its own kit and 8-bar groove);
  * PATHS: a voice's notes are its cubes' paths over the chord's tiles. Cubes step to neighbouring tiles (repeated notes walk LADDERS over the
    tiles that sound the same pitch); voices that leap are SPHERES (they bounce anywhere). A note that is not a tile is re-voiced to the
    nearest tile (the game's own re-voicing);
  * DEVICES for the song's moments: one cube pumping 8ths with FOLLOW 3 = the piano's triads; octave LAYER copies = octave doublings; STAIRS
    islands = the runs falling into the next chord; LAUNCH = the swell into a section; repeat BELTS (+ vary) for a phrase's repeated half;
    EXTEND (long grids) where one cube walks a section's chords; the high lane raised (towers); CLIMB in the climax.
Every value written is one the game's own UI can produce (island header, inspector card, deck); nothing is hand-edited past it.
"""
import importlib.util
import json
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import engine as E          # noqa: E402
import deacube as D         # noqa: E402
import midi_song as R1      # noqa: E402  (the shared pieces of round 1: the MIDI reader, paths, velocities, Moons)

BAR, TPB = 96, 24
NUM_INV = 6
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "AudioCube_Unity", "Assets", "Resources", "Gallery")
SPACING, TILE, PAD = 1.12, 1.0, 0.85
ISLAND_WIDTH = (NUM_INV - 1) * SPACING + TILE + 2 * PAD
EDGE_INSET = TILE * 0.5 + PAD
SECTION_GAP = 3.7
BELT_GAP = 0.6
MOON_Z = -10.5
MINOR_STEPS = [0, 2, 3, 5, 7, 8, 10]
MAJOR_STEPS = [0, 2, 4, 5, 7, 9, 11]
PENTA = [0, 2, 4, 7, 9]
STAIR_RATES = (6, 8, 12, 24)


class BuildError(Exception):
    pass


def load_config(name):
    path = os.path.join(HERE, "imports", name.replace("-", "_") + ".py")
    spec = importlib.util.spec_from_file_location("import2_" + name.replace("-", "_"), path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return getattr(mod, "CONFIG3", None) or mod.CONFIG2


def pc(m):
    return m % 12


# ================================================================== chord grids (KeyBlock.Build) and what a tile sounds (VoiceRules)
class Grid:
    """A chord grid: root (clamped like KeyBlock), quality, register; t[x][z] = the tile's MIDI (register included)."""
    def __init__(self, name, root, reg):
        self.name = name
        self.pc, self.q = D.parse_chord(name)
        self.semis = D.QUALITIES[self.q]
        self.root = D.clamp_root(root)
        self.reg = reg
        self.t = D.grid(self.root, self.q, reg)
        self.rows = len(self.semis)

    def role(self, x, z):
        return D.role_of(self.t[x][z] - 12 * self.reg - self.root)

    def tiles(self):
        return [(x, z) for x in range(NUM_INV) for z in range(self.rows)]


def clamp_register(voice, midi):
    """SynthBank.ClampToRegister."""
    if voice.drums:
        return max(0, min(127, midi))
    midi += 12 * voice.octave_shift
    lo, hi = min(voice.low, voice.high), max(voice.low, voice.high)
    while midi < lo:
        midi += 12
    while midi > hi:
        midi -= 12
    if midi < lo:
        midi = lo
    return max(0, min(127, midi))


def fold(voice, midi, reg):
    """VoiceRules.Fold(slot, midi, reg): the voice's register window and the low-end law, both shifted by the island's register."""
    g = voice.group
    if reg == 0 or voice.drums:
        midi = clamp_register(voice, midi)
        if voice.drums:
            return midi
        if g != E.GROUP_BASS:
            while midi < 48:
                midi += 12
        if g != E.GROUP_BELLS:
            while midi > 96:
                midi -= 12
        return max(0, min(127, midi))
    reg = max(-2, min(2, reg))
    midi += 12 * voice.octave_shift
    lo = min(voice.low, voice.high) + 12 * reg
    hi = max(voice.low, voice.high) + 12 * reg
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
    if g != E.GROUP_BASS:
        while midi < floor and midi + 12 <= 108:
            midi += 12
    if g != E.GROUP_BELLS:
        while midi > ceil and midi - 12 >= 21:
            midi -= 12
    return max(0, min(127, midi))


KEY = [11, True]   # the song key (tonic pitch class, minor) the nudges walk in: set by Plan from the config


def nudge_semis(tile_midi, steps, tonic=None, minor=None):
    """Nudge.Semis (v9 N): the semitones that take tile_midi `steps` scale steps of the song key up (> 0) / down (< 0); an off-scale note walks
    from the scale note just below it (else above) and keeps its offset. 0 for 0 steps."""
    if not steps:
        return 0
    tonic = KEY[0] if tonic is None else tonic
    minor = KEY[1] if minor is None else minor
    st = MINOR_STEPS if minor else MAJOR_STEPS
    t = tonic % 12

    def degree(m):
        rel = (m - t) % 12
        return st.index(rel) if rel in st else -1
    d, off = degree(tile_midi), 0
    if d < 0:
        d, off = degree(tile_midi - 1), 1
        if d < 0:
            d, off = degree(tile_midi + 1), -1
    if d < 0:
        return 0
    base = tile_midi - off
    octv = (base - t) // 12
    idx = d + steps
    om = idx // 7
    idx -= om * 7
    return t + 12 * (octv + om) + st[idx] + off - tile_midi


def sound(voice, grid, x, z, octave=0, lift=False, downbeat=False, layer=0, nudge=0):
    """The pitch a cube on tile (x, z) plays (VoiceRules.Resolve rules 2, 4, 5: no stack, no transpose, no bend; v9: the step's nudge, which
    plays alone: no bass anchor)."""
    midi = grid.t[x][z] + 12 * octave + (12 if lift else 0) + nudge_semis(grid.t[x][z], nudge)
    if not nudge and voice.group == E.GROUP_BASS and downbeat and grid.role(x, z) in ("seventh", "extension"):
        midi = grid.root + 12 * octave + 12 * grid.reg
    return E.layered(fold(voice, midi, grid.reg), layer)


SHAPES = ("ladder", "zigzag", "diagonal", "spiral", "mirror", "pump")


def shape_cost(shape, i, pt, t):
    """A small tie-break (<= 0.12, below every pitch cost) that steers a path's SHAPE among tiles sounding the same: zigzag (the inversion
    column swings left / right), diagonal (steps across both axes), spiral (right, up, left, down, two steps a side), pump (stays and bounces),
    ladder (none). Mirror is a second pass (line_cube)."""
    if not shape or shape in ("ladder", "mirror"):
        return 0.0
    dx, dz = t[0] - pt[0], t[1] - pt[1]
    if shape == "pump":
        return 0.0 if (dx, dz) == (0, 0) else 0.1
    if shape == "zigzag":
        want = 1 if i % 2 else -1
        return 0.0 if dx == want else (0.05 if dx == 0 else 0.1)
    if shape == "diagonal":
        return 0.0 if dx != 0 and dz != 0 else (0.06 if (dx, dz) == (0, 0) else 0.1)
    if shape == "spiral":
        want = [(1, 0), (0, 1), (-1, 0), (0, -1)][(i // 2) % 4]
        sx, sz = (dx > 0) - (dx < 0), (dz > 0) - (dz < 0)
        return 0.0 if (sx, sz) == want else (0.06 if (dx, dz) == (0, 0) else 0.1)
    return 0.0


def shadow(grid, x, z, up):
    """VoiceRules.ShadowTile: (row, octave offset) of the nearest row above / below in the same column at least 3 semitones away."""
    rows = grid.rows
    d = 1 if up else -1
    for s in range(1, rows):
        zz, off = z + d * s, 0
        if zz >= rows:
            zz -= rows
            off = 12
        elif zz < 0:
            zz += rows
            off = -12
        if abs(grid.t[x][zz] + off - grid.t[x][z]) >= 3:
            return zz, off
    return None


def follow_sounds(voice, grid, x, z, follow, octave=0, layer=0):
    """The follow shadows' pitches (VoiceRules.ShadowNote: the shadow tile + its octave offset, folded, then the layer)."""
    out = []
    for up, on in ((True, follow in (1, 3)), (False, follow in (2, 3))):
        if not on:
            continue
        s = shadow(grid, x, z, up)
        if s is None:
            continue
        zz, off = s
        out.append(E.layered(fold(voice, grid.t[x][zz] + off + 12 * octave, grid.reg), layer))
    return out


def best_root(name, reg, voice, targets):
    """The root (MIDI, pitch class of the chord) whose grid lets `voice` sound the most `targets` exactly (ties: nearest 57..66, the game's
    VoiceRoot)."""
    p, q = D.parse_chord(name)
    best = None
    for root in range(36, 85):
        if root % 12 != p:
            continue
        g = Grid(name, root, reg)
        have = set(sound(voice, g, x, z) for x, z in g.tiles())
        score = sum(1 for t in targets if t in have)
        cost = (-score, abs(root - 61))
        if best is None or cost < best[0]:
            best = (cost, root)
    return best[1]


# ================================================================== paths
def cheb(a, b):
    return max(abs(a[0] - b[0]), abs(a[1] - b[1]))


def plan_line(voice, grid, notes, sphere, start=None, layer=0, lift_of=None):
    """The tiles of a one-line voice over a bar: notes = [Note] (local ticks, no overlaps). For each note the candidate tiles sounding its pitch
    (else the nearest pitch: re-voicing); a dynamic programme picks one per note — a CUBE steps to a neighbouring tile (Chebyshev 1, the UI's
    rule) or stays (a bounce), so repeated notes walk ladders over the tiles of their pitch; a SPHERE may land anywhere. Returns [(x, z)]."""
    if not notes:
        return []
    cands = []
    for i, n in enumerate(notes):
        lift = bool(lift_of and lift_of(i))
        down = n.t % BAR == 0
        opts = []
        for (x, z) in grid.tiles():
            s = sound(voice, grid, x, z, 0, lift, down, layer)
            opts.append((abs(s - n.p), (x, z)))
        m = min(o[0] for o in opts)
        cands.append([t for d, t in opts if d == m])
    # DP
    INF = 10 ** 9
    prev_cost = {}
    for t in cands[0]:
        prev_cost[t] = (0.0 if start is None else (0.0 if cheb(start, t) <= 1 or sphere else 8.0), None)
    hist = [prev_cost]
    for i in range(1, len(notes)):
        cur = {}
        same = notes[i].p == notes[i - 1].p
        for t in cands[i]:
            best = (INF, None)
            for pt, (pc_, _) in hist[-1].items():
                d = cheb(pt, t)
                if sphere:
                    step = 0.05 * d + (0.15 if d == 0 and same else 0.0)
                else:
                    step = 0.0 if d == 1 else (0.25 if d == 0 else 8.0 + d)
                c = pc_ + step
                if c < best[0]:
                    best = (c, pt)
            cur[t] = best
        hist.append(cur)
    t = min(hist[-1], key=lambda k: (hist[-1][k][0], k))
    out = [t]
    for i in range(len(notes) - 1, 0, -1):
        t = hist[i][t][1]
        out.append(t)
    return out[::-1]


def nodes_for(seq, t1=BAR):
    """[(note, tile, mod)] -> path nodes over [0, t1) (round 1's path_nodes: a note keeps its length, gaps are rests on the tile just played)."""
    nodes, cuts = R1.path_nodes(seq, 0, t1)
    return nodes, cuts


class Cube:
    def __init__(self, voice, nodes, label, sphere=False, follow=0, layer=0, gate=2, octave=0, echo=0, climb=0, src=None, volume=None, mods=None,
                 nudges=None):
        self.voice, self.nodes, self.label = voice, nodes, label
        self.sphere, self.follow, self.layer, self.gate, self.octave, self.echo, self.climb = sphere, follow, layer, gate, octave, echo, climb
        self.src = src                 # the source Cube of an octave copy (echoOf)
        self.volume = volume
        self.verr = 0.0
        self.nudges = nudges           # v9 (N): per node -2..+2 scale steps (None = none)

    def nudge(self, i):
        return self.nudges[i] if self.nudges and i < len(self.nudges) else 0


class Isl:
    """One island of the plan."""
    def __init__(self, **kw):
        self.kind = 0
        self.cubes = []
        self.repeat, self.vary, self.launch, self.carry, self.energy = 1, 0, False, 0, 2
        self.bar, self.bars = 0, 1
        self.stair = None
        self.__dict__.update(kw)


def fit(voice, cube, notes_at, energy=2):
    """The cube's volume and stickers (ghost / accent) from the MIDI velocities of the notes its sounding nodes play (VoiceRules rule 7).
    notes_at: {node index: Note}."""
    items = []
    t = 0
    idx = []
    for i, nd in enumerate(cube.nodes):
        if nd[2] != 1 and i in notes_at:
            items.append((notes_at[i].v, t, None if nd[2] in (0, 2, 3) else nd[2]))
            idx.append(i)
        t += nd[1]
    if not items:
        cube.volume = cube.volume if cube.volume is not None else 0.6
        return
    vol, mods, err = R1.fit_velocities(voice, items)
    cube.volume = vol
    cube.verr = err
    for i, m in zip(idx, mods):
        if cube.nodes[i][2] in (0, 2, 3):
            cube.nodes[i][2] = m


# ================================================================== stairs (SongManager.StairPitches / StairPath, Harmony.Stairs, VoiceRules.FoldRun)
def stair_pool(stype, root, semis, tonic, minor):
    pcs = set()
    if stype in (1, 5):
        pcs = set((tonic + s) % 12 for s in (MINOR_STEPS if minor else MAJOR_STEPS))
    elif stype == 2:
        if 4 in semis and 10 in semis:
            third = root + 4
            pcs = set((third + 3 * k) % 12 for k in range(4))
        else:
            pcs = set((root + s) % 12 for s in semis)
    elif stype == 3:
        pcs = set(range(12))
    elif stype == 4:
        maj = tonic + 3 if minor else tonic
        pcs = set((maj + s) % 12 for s in PENTA)
    else:
        pcs = set((root + s) % 12 for s in semis)
    return pcs or {root % 12}


def stairs(stype, sdir, steps, root, semis, nroot, nsemis, tonic, minor, top, lo, hi):
    """Harmony.Stairs (pure): the run's pitches before the register."""
    steps = max(1, min(16, steps))
    sdir = -1 if sdir <= 0 else 1
    pool = stair_pool(stype, root, semis, tonic, minor)
    ns = nsemis or [0, 4, 7]
    land = set([nroot % 12]) if stype == 5 else set((nroot + s) % 12 for s in ns)
    strong = set([nroot % 12]) | set((nroot + s) % 12 for s in ns if s % 12 in (3, 4))
    best, bcost = None, None
    for pas in range(2):
        if best is not None:
            break
        inside = pas == 0
        for L in range(lo - 14, hi + 15):
            if L % 12 not in land:
                continue
            run = [0] * steps
            p, k = L, 0
            for _ in range(16 * 12):
                if k >= steps:
                    break
                p -= sdir
                if p % 12 in pool:
                    run[steps - 1 - k] = p
                    k += 1
            if k < steps:
                continue
            if inside and any(m < lo or m > hi for m in run):
                continue
            cost = abs(run[0] - top) * 100 + (0 if L % 12 in strong else 10) + (L - (lo - 14)) // 12
            if not inside:
                cost += sum(max(0, lo - m) + max(0, m - hi) for m in run) * 1000
            if bcost is None or cost < bcost:
                best, bcost = run, cost
    if best is None:
        best = [max(lo, min(hi, top + sdir * i)) for i in range(steps)]
    return best


def stair_pitches(st, chord, nchord, top, tonic, minor):
    """SongManager.StairPitches: st = dict(type, dir, steps, reg); chord / nchord = Grid of this column's / the next column's first chord island;
    top = the highest note >= 60 of the column's other islands' cubes (None: none)."""
    root, semis = chord.root, chord.semis
    nroot, nsemis = nchord.root, nchord.semis
    walk = st["type"] == 5
    if top is None:
        top = root + 7
        while top < 67:
            top += 12
        while top > 79:
            top -= 12
    if st["dir"] > 0:
        top -= 7
    lo, hi = 55, 88
    if walk:
        lo, hi = 36, 57
        top = root
        while top > 52:
            top -= 12
        while top < 40:
            top += 12
        if st["dir"] > 0:
            top -= 5
    r = stairs(st["type"], st["dir"], max(3, min(8, st["steps"])), root, semis, nroot, nsemis, tonic, minor, top, lo, hi)
    return [m + 12 * st["reg"] for m in r]


def fold_run(voice, run_tiles, octave=0):
    """VoiceRules.FoldRun for every step of a run (one octave shift for the whole run into the voice's melody window at the island's register)."""
    reg = run_tiles["reg"]
    tiles = run_tiles["pitches"]
    lo, hi = min(tiles), max(tiles)
    common = 12 * octave + 12 * voice.octave_shift
    low, high = E.melody_window(voice, reg)
    lo += common
    hi += common
    shift = 0
    while lo + shift < low:
        shift += 12
    while hi + shift > high and lo + shift - 12 >= low:
        shift -= 12
    out = []
    for t in tiles:
        m = t + 12 * octave + 12 * voice.octave_shift + shift
        while m > 108:
            m -= 12
        while m < 21:
            m += 12
        out.append(m)
    return out


def stair_path(st, bars):
    """SongManager.StairPath: (xs, mods, durs) of the runner over one pass of the stairs island."""
    n = max(3, min(8, st["steps"]))
    rate = st["rate"] if st["rate"] in STAIR_RATES else 12
    pas = max(rate, bars * BAR)
    fitn = max(1, min(n, pas // rate))
    X, M, Dd = [], [], []
    if st["lead"]:
        first, rest = n - fitn, pas - fitn * rate
        while rest > 0:
            chunk = min(rest, 192)
            if chunk < 3:
                if Dd:
                    Dd[-1] += chunk
                break
            X.append(first)
            M.append(1)
            Dd.append(chunk)
            rest -= chunk
        for i in range(first, n):
            X.append(i)
            M.append(0)
            Dd.append(rate)
    else:
        for i in range(fitn):
            X.append(i)
            M.append(0)
            Dd.append(rate)
        left = pas - fitn * rate
        while left > 0 and Dd:
            room = 192 - Dd[-1]
            if room >= left:
                Dd[-1] += left
                left = 0
            else:
                Dd[-1] = 192
                left -= room
                if left > 0:
                    X.append(fitn - 1)
                    M.append(7)
                    Dd.append(min(192, max(3, left)))
                    left -= Dd[-1]
    return X, M, Dd


# ================================================================== the plan
class Plan:
    def __init__(self, cfg):
        self.cfg = cfg
        self.voices = E.load_voices()
        self.song, self.tracks, self.offgrid = R1.read_notes(R1_cfg(cfg))
        self.islands = []        # Isl (chord grids, stairs) in column order
        self.moons = []          # round 1 Moon islands
        self.columns = []        # dict(bar0, bars, passes, section)
        self.sections = []       # first column of each section
        self.letters, self.roles = [], []
        self.notes_dropped = []  # (voice, Note, why)
        self.devices = []        # (kind, where, what)

    def voice(self, vname):
        vc = self.cfg["voices"][vname]
        return self.voices[(vc["group"], vc["caption"])]

    def notes(self, vname, t0, t1):
        """The voice's MIDI notes starting in [t0, t1) (song ticks), local to t0 (without the config's drops)."""
        tr = self.tracks[self.cfg["voices"][vname]["track"]]
        out = []
        for n in tr:
            if t0 <= n.t < t1:
                if any(d["voice"] == vname and d["bars"][0] * BAR <= n.t < d["bars"][1] * BAR for d in self.cfg.get("drops", [])):
                    continue
                out.append(R1.Note(n.t - t0, n.d, n.p, n.v, n.track, n.nid))
        return out

    def double_of(self, vname, bar):
        for d in self.cfg.get("doubles", []):
            if d["voice"] == vname and d["bars"][0] <= bar < d["bars"][1]:
                return d
        return None

    def climb_of(self, vname, bar):
        for c in self.cfg.get("climbs", []):
            if c["voice"] == vname and c["bars"][0] <= bar < c["bars"][1]:
                return c["n"]
        return 0

    def chord_of(self, bar):
        cc = self.cfg["colour_cards"]
        if bar in cc:
            return cc[bar]
        loop = self.cfg["loop_cards"]
        return loop[(bar - self.cfg["loop_from"]) % len(loop)]


def R1_cfg(cfg):
    return dict(midi=cfg["midi"], shift=cfg.get("shift", 0))


def split_chords(notes):
    """Notes -> [(onset, [Note...])] grouped by onset (a chord's notes start together)."""
    groups = {}
    for n in notes:
        groups.setdefault(n.t, []).append(n)
    return sorted(groups.items())


def occupied(cubes, tile, t):
    """Does any (non-layer) cube of `cubes` rest on `tile` at local tick t? (SequenceMaster.Occupy: a cube occupies its tile for its whole node;
    a cube leaving a tile on the very tick another lands there still counts — the order of their updates is not ours)"""
    for cb in cubes:
        if cb.layer != 0:
            continue
        s = 0
        for nd in cb.nodes:
            if s <= t <= s + nd[1] and tuple(nd[0]) == tuple(tile):
                return True
            s += nd[1]
    return False


def stack_hits(cubes):
    """[(cube index, node index)]: sounding nodes landing on a tile another non-layer cube of the island rests on (the stack rule's +12)."""
    out = []
    for i, cb in enumerate(cubes):
        if cb.layer != 0:
            continue
        others = [c for j, c in enumerate(cubes) if j != i]
        s = 0
        for k, nd in enumerate(cb.nodes):
            if nd[2] != 1 and occupied(others, nd[0], s):
                out.append((i, k))
            s += nd[1]
    return out


def plan_lane_bar(P, lane, grid, bar_t0, voices_here, stairs_cover, local_len=BAR, others0=None):
    """The cubes of one lane in one bar: voices_here = [vname]; stairs_cover = {vname: (t0, t1)} local ticks a stairs island plays instead.
    Returns [Cube] (others0: cubes already on the grid, kept first)."""
    cubes = list(others0 or [])
    for vname in voices_here:
        vc = P.cfg["voices"][vname]
        voice = P.voice(vname)
        notes = P.notes(vname, bar_t0, bar_t0 + local_len)
        cov = stairs_cover.get(vname)
        if cov:
            keep = [n for n in notes if not (cov[0] <= n.t < cov[1])]
            for n in notes:
                if cov[0] <= n.t < cov[1]:
                    P.notes_dropped.append((vname, n, "stairs"))
            notes = keep
        if not notes:
            continue
        kind = vc["kind"]
        dbl = P.double_of(vname, bar_t0 // BAR)
        if dbl:
            continue           # played as an octave copy of another voice's cube (added after that voice is planned)
        if kind == "flicker":
            cubes.extend(plan_flicker(P, vname, voice, grid, notes, cubes, vc))
        elif kind == "triads":
            cubes.extend(plan_triads(P, vname, voice, grid, notes, cubes, vc))
        elif kind == "octaves":
            cubes.extend(plan_octaves(P, vname, voice, grid, notes, cubes, vc))
        elif kind == "pad":
            cubes.extend(plan_pad(P, vname, voice, grid, notes, cubes, vc))
        elif kind == "dyads":
            cubes.extend(plan_dyads(P, vname, voice, grid, notes, cubes, vc))
        else:
            cubes.extend(plan_lines(P, vname, voice, grid, notes, cubes, vc))
    for _ in range(3):
        hits = stack_hits(cubes)
        if not hits:
            break
        i, k = hits[0]
        cb = cubes[i]
        if cb.follow:          # a triads cube keeps its tiles; the cube it lands on moves away from it
            s = sum(nd[1] for nd in cb.nodes[:k])
            rest = [j for j, c in enumerate(cubes) if j != i and c.layer == 0 and c.follow == 0 and occupied([c], cb.nodes[k][0], s)]
            if not rest:
                break
            i = rest[0]
            cb = cubes[i]
        line = [nd[3] for nd in cb.nodes if nd[2] != 1 and nd[3] is not None]
        others = [c for k, c in enumerate(cubes) if k != i and c.src is not cb]
        lift_of = (lambda k, ln=line, lifts=[nd[2] == 8 for nd in cb.nodes if nd[2] != 1 and nd[3] is not None]: lifts[k]) if any(nd[2] == 8 for nd in cb.nodes) else None
        new = line_cube(P, cb.label, cb.voice, grid, line, others, cb.sphere, cb.label, cb.gate, lift_of, cb.layer, octaves=(cb.octave,))
        if not new.sphere and not walkable(new):
            new = line_cube(P, cb.label, cb.voice, grid, line, others, True, cb.label, cb.gate, lift_of, cb.layer, octaves=(cb.octave,))
        new.follow = cb.follow
        cubes[i] = new
        for k, c in enumerate(cubes):
            if c.src is cb:
                cp = Cube(c.voice, [list(nd) for nd in new.nodes], c.label, sphere=c.sphere, layer=c.layer, src=new, octave=c.octave, follow=c.follow,
                          nudges=list(new.nudges) if new.nudges else None)
                cp.volume = c.volume
                cubes[k] = cp
    if stack_hits(cubes):
        P.devices.append(("stack?", "lane " + lane, "%d notes still land on another cube's tile" % len(stack_hits(cubes))))
    # octave copies in another voice's colour (the music box doubling the sax / the e.piano)
    bar = bar_t0 // BAR
    for d in P.cfg.get("doubles", []):
        if d["of"] in voices_here and d["bars"][0] <= bar < d["bars"][1]:
            src = next((c for c in cubes if c.label == d["of"] and c.layer == 0), None)
            if src is None:
                continue
            dv = P.voice(d["voice"])
            mine = P.notes(d["voice"], bar_t0, bar_t0 + local_len)
            # the copy's layer: the octaves between the source's notes and the doubling voice's (measured on the MIDI)
            t, offs = 0, []
            for nd in src.nodes:
                if nd[2] != 1 and nd[3] is not None:
                    m = [n for n in mine if n.t == t]
                    if m:
                        offs.append(round((max(n.p for n in m) - sound(src.voice, grid, nd[0][0], nd[0][1], src.octave)) / 12.0))
                t += nd[1]
            lay = max(set(offs), key=offs.count) if offs else 1
            cp = Cube(dv, [list(nd) for nd in src.nodes], d["voice"] + " (copy of " + d["of"] + ")", sphere=src.sphere, layer=lay, src=src,
                      octave=src.octave, nudges=list(src.nudges) if src.nudges else None)
            fit(dv, cp, {i: m for i, nd in enumerate(cp.nodes) if nd[3] is not None for m in [next((n for n in mine if n.t == sum(x[1] for x in cp.nodes[:i])), nd[3])]})
            cubes.append(cp)
    # climb (the climax): the voice's cubes raise their highest peaks
    for cb in cubes:
        cl = P.climb_of(cb.label.split(" ")[0], bar)
        if cl and cb.layer == 0:
            cb.climb = cl
    return cubes


def mono_lines(notes):
    """Split notes into lines without overlaps (a mono voice's lines): the top line first."""
    lines = []
    for n in sorted(notes, key=lambda n: (n.t, -n.p)):
        for ln in lines:
            if ln[-1].t + ln[-1].d <= n.t:
                ln.append(n)
                break
        else:
            lines.append([n])
    return lines


def landings(cubes):
    """(tile, tick) of every sounding node of the non-layer cubes."""
    out = []
    for cb in cubes:
        if cb.layer != 0:
            continue
        s = 0
        for nd in cb.nodes:
            if nd[2] != 1:
                out.append((tuple(nd[0]), s))
            s += nd[1]
    return out


def avoid_tiles(grid, cubes, notes, end=BAR):
    """Per note the tiles it must not stand on: the ones other cubes rest on at its onset, and the ones another cube lands on while this cube
    stays (until its next note) — either way the stack rule would lift a note an octave."""
    lands = landings(cubes)
    out = []
    for i, n in enumerate(notes):
        b = notes[i + 1].t if i + 1 < len(notes) else end
        bad = set(t for t in grid.tiles() if occupied(cubes, t, n.t))
        bad |= set(tl for tl, s in lands if n.t <= s <= b)
        out.append(bad)
    return out


def line_cube(P, vname, voice, grid, line, others, sphere, label, gate=2, lift_of=None, layer=0, above=None, octaves=(0, -1, 1)):
    """One cube (or sphere) playing a line of notes over the bar on `grid`, avoiding the tiles `others` occupy; the cube's octave (the inspector's
    octave buttons, -1..+1) that plays the most notes exactly (then the nearest); above = {onset: pitch} a dyad's upper line must exceed."""
    av = avoid_tiles(grid, others, line)
    shape = getattr(P, "cur_shape", None)
    nudge = bool(getattr(P, "nudge_ok", None) and P.nudge_ok(vname))
    best = None
    for octv in octaves:
        tiles, nds = plan_line_avoid(voice, grid, line, sphere, av, lift_of, octv, layer, above, shape=shape, nudge=nudge)
        if shape == "mirror" and len(line) >= 4:
            # the second half mirrors the first: its notes prefer the inversion column across the grid's centre from their twin's
            h = len(line) // 2
            want = {h + j: NUM_INV - 1 - tiles[j][0] for j in range(len(line) - h)}
            un = lambda i, tl, w=want: 0.03 * abs(tl[0] - w[i]) if i in w else 0.0
            tiles, nds = plan_line_avoid(voice, grid, line, sphere, av, lift_of, octv, layer, above, shape=None, unary=un, nudge=nudge)
        got = [sound(voice, grid, x, z, octv, bool(lift_of and lift_of(i)), n.t % BAR == 0, layer, nds[i]) for i, ((x, z), n) in enumerate(zip(tiles, line))]
        exact = sum(1 for g, n in zip(got, line) if g == n.p)
        dist = sum(abs(g - n.p) for g, n in zip(got, line))
        cost = (-exact, sum(1 for d in nds if d), dist, abs(octv))
        if best is None or cost < best[0]:
            best = (cost, octv, tiles, nds)
    _, octv, tiles, nds = best
    seq = []
    by_nid = {}
    for i, (n, t) in enumerate(zip(line, tiles)):
        mod = 8 if (lift_of and lift_of(i)) else 0
        seq.append((n, t, mod))
        by_nid[n.nid] = nds[i]
    nodes, cuts = nodes_for(seq)
    cb = Cube(voice, nodes, label, sphere=sphere, gate=gate, octave=octv, layer=layer)
    if any(nds):
        cb.nudges = [by_nid.get(nd[3].nid, 0) if nd[3] is not None and nd[2] != 1 else 0 for nd in nodes]
    fit(voice, cb, {i: nd[3] for i, nd in enumerate(nodes) if nd[3] is not None})
    return cb


def plan_line_avoid(voice, grid, notes, sphere, avoid, lift_of=None, octave=0, layer=0, above=None, shape=None, unary=None, nudge=False):
    """plan_line with per-note forbidden tiles (they fall back to the nearest other tile sounding the pitch, else the nearest pitch); above:
    {onset: pitch} the note must sound higher than (a dyad's upper line); shape / unary: tie-breaks among tiles that sound the same; nudge: a
    note no tile sounds may take a tile nudged -2..+2 scale steps that does (v9 N; the smallest nudge). Returns (tiles, nudges)."""
    if not notes:
        return [], []
    cands, nmap = [], []
    for i, n in enumerate(notes):
        lift = bool(lift_of and lift_of(i))
        down = n.t % BAR == 0
        opts = []
        floor = above.get(n.t) if above else None
        for (x, z) in grid.tiles():
            if (x, z) in avoid[i]:
                continue
            s = sound(voice, grid, x, z, octave, lift, down, layer)
            if floor is not None and s <= floor:
                continue
            opts.append((abs(s - n.p), (x, z)))
        if not opts:
            opts = [(abs(sound(voice, grid, x, z, octave, lift, down, layer) - n.p), (x, z)) for (x, z) in grid.tiles()]
        m = min(o[0] for o in opts)
        nd = {}
        if m > 0 and nudge:
            # no tile sounds it: the tiles a nudge of 1 (else 2) scale steps takes to it exactly
            for group in ((1, -1), (2, -2)):
                for k in group:
                    for (x, z) in grid.tiles():
                        if (x, z) in avoid[i] or (x, z) in nd:
                            continue
                        s = sound(voice, grid, x, z, octave, lift, down, layer, k)
                        if s == n.p and not (floor is not None and s <= floor):
                            nd[(x, z)] = k
                if nd:
                    break
            if nd:
                cands.append(sorted(nd))
                nmap.append(nd)
                continue
        cands.append([t for d, t in opts if d == m])
        nmap.append({})
    INF = 10 ** 9
    u0 = (lambda i, t: unary(i, t)) if unary else (lambda i, t: 0.0)
    hist = [{t: (u0(0, t), None) for t in cands[0]}]
    for i in range(1, len(notes)):
        cur = {}
        same = notes[i].p == notes[i - 1].p
        for t in cands[i]:
            best = (INF, None)
            for pt, (pcost, _) in hist[-1].items():
                d = cheb(pt, t)
                if sphere:
                    step = 0.05 * d + (0.15 if d == 0 and same else 0.0)
                else:
                    step = 0.0 if d == 1 else (0.25 if d == 0 else 8.0 + d)
                step += shape_cost(shape, i, pt, t) + u0(i, t)
                if pcost + step < best[0]:
                    best = (pcost + step, pt)
            cur[t] = best
        hist.append(cur)
    t = min(hist[-1], key=lambda k: (hist[-1][k][0], k))
    out = [t]
    for i in range(len(notes) - 1, 0, -1):
        t = hist[i][t][1]
        out.append(t)
    out = out[::-1]
    return out, [nmap[i].get(tl, 0) for i, tl in enumerate(out)]


def walkable(cb):
    """Every step of the cube's path a neighbouring tile (or the same): a cube can draw it (else it takes a sphere, which leaps)."""
    prev = None
    for nd in cb.nodes:
        if prev is not None and cheb(prev, nd[0]) > 1:
            return False
        prev = nd[0]
    return True


def plan_lines(P, vname, voice, grid, notes, others, vc):
    """A melodic voice: each of its lines (top line first) one cube / sphere (sphere: "auto" = a cube when it can walk its path)."""
    out = []
    for k, line in enumerate(mono_lines(notes)[:vc.get("max_lines", 8)]):
        sph = vc.get("sphere", False)
        label = "%s%s" % (vname, "" if k == 0 else k + 1)
        if sph == "auto":
            cb = line_cube(P, vname, voice, grid, line, others + out, False, label)
            if not walkable(cb):
                cb = line_cube(P, vname, voice, grid, line, others + out, True, label)
        else:
            cb = line_cube(P, vname, voice, grid, line, others + out, bool(sph), label)
        out.append(cb)
    return out


def plan_flicker(P, vname, voice, grid, notes, others, vc):
    """An octave flicker (the 8-bit square's 32nds: a note and its octave, alternating): one cube on the lower note's tile, the LIFT sticker
    (+12) on every upper note (the cube bounces, every other bounce an octave up)."""
    line = mono_lines(notes)[0]
    base, lifts = [], []
    for i, n in enumerate(line):
        lo = min(n.p, line[i - 1].p if i else n.p, line[i + 1].p if i + 1 < len(line) else n.p)
        up = n.p - lo >= 12
        base.append(R1.Note(n.t, n.d, n.p - 12 if up else n.p, n.v, n.track, n.nid))
        lifts.append(up)
    lift_of = lambda k: lifts[k]
    cb = line_cube(P, vname, voice, grid, line, others, False, vname, lift_of=lift_of)
    if not walkable(cb):
        cb = line_cube(P, vname, voice, grid, line, others, True, vname, lift_of=lift_of)
    return [cb]


def plan_dyads(P, vname, voice, grid, notes, others, vc):
    """Dyads (the flute's thirds, the music box): the lower line and the upper line, two spheres; a single note goes to the lower line."""
    groups = split_chords(notes)
    lower, upper = [], []
    for t, ns in groups:
        ns = sorted(ns, key=lambda n: n.p)
        lower.append(ns[0])
        if len(ns) > 1:
            upper.append(ns[-1])
    out = []
    if lower:
        out.append(line_cube(P, vname, voice, grid, lower, others, True, vname))
    if upper:
        lo_cb = out[0]
        floor, t = {}, 0
        for i, nd in enumerate(lo_cb.nodes):
            if nd[2] != 1:
                floor[t] = sound(voice, grid, nd[0][0], nd[0][1], lo_cb.octave, nd[2] == 8, t % BAR == 0, 0, lo_cb.nudge(i))
            t += nd[1]
        out.append(line_cube(P, vname, voice, grid, upper, others + out, True, vname + " (upper)", above=floor))
    return out


def plan_triads(P, vname, voice, grid, notes, others, vc):
    """The piano's 8th-note triads: ONE cube with follow 3 (its tile + the shadow rows above and below = a triad of the grid's chord), pumping on
    a tile; per chord the tile whose three notes best match the MIDI's (pitch classes, then register), a neighbour of the last (a cube)."""
    groups = split_chords(notes)
    # per chord the cost of each tile (its three notes against the MIDI's), then a walk: a cube moves only to a neighbouring tile (or stays)
    costs = []
    for t, ns in groups:
        want = sorted(n.p for n in ns)
        row = {}
        for (x, z) in grid.tiles():
            if occupied(others, (x, z), t):
                continue
            main = sound(voice, grid, x, z)
            got = sorted([main] + follow_sounds(voice, grid, x, z, 3))
            pcs_hit = sum(1 for w in want if w % 12 in set(g % 12 for g in got))
            exact = sum(1 for w in want if w in got)
            reg = abs(sum(got) / float(len(got)) - sum(want) / float(len(want)))
            row[(x, z)] = (-exact * 2 - pcs_hit) * 10 + reg * 0.5
        costs.append(row)
    INF = 10 ** 9
    hist = [{tl: (c, None) for tl, c in costs[0].items()}]
    for i in range(1, len(groups)):
        cur = {}
        for tl, c in costs[i].items():
            best = (INF, None)
            for pt, (pc_, _) in hist[-1].items():
                d = cheb(pt, tl)
                if d > 1:
                    continue
                v = pc_ + c + (0.0 if d == 0 else 0.3)
                if v < best[0]:
                    best = (v, pt)
            if best[1] is not None:
                cur[tl] = best
        if not cur:      # no walk: let it leap (a sphere)
            cur = {tl: (min(v[0] for v in hist[-1].values()) + c, min(hist[-1], key=lambda k: hist[-1][k][0])) for tl, c in costs[i].items()}
        hist.append(cur)
    tl = min(hist[-1], key=lambda k: (hist[-1][k][0], k))
    tiles = [tl]
    for i in range(len(groups) - 1, 0, -1):
        tl = hist[i][tl][1]
        tiles.append(tl)
    tiles = tiles[::-1]
    seq = []
    for (t, ns), tl in zip(groups, tiles):
        top = max(ns, key=lambda n: n.p)
        seq.append((R1.Note(t, min(n.d for n in ns), top.p, max(n.v for n in ns), top.track, top.nid), tl, 0))
    nodes, cuts = nodes_for(seq)
    cb = Cube(voice, nodes, vname, sphere=False, follow=3)
    if not walkable(cb):
        cb.sphere = True
    fit(voice, cb, {i: nd[3] for i, nd in enumerate(nodes) if nd[3] is not None})
    return [cb]


def plan_pad(P, vname, voice, grid, notes, others, vc):
    """A swell (the strings): one cube with follow 3 per chord (as the piano) and an octave layer copy below (the MIDI doubles at the octave)."""
    groups = split_chords(notes)
    seq = []
    prev = None
    for t, ns in groups:
        upper = sorted(n.p for n in ns)[len(ns) // 2:]
        best = None
        for (x, z) in grid.tiles():
            main = sound(voice, grid, x, z)
            got = sorted([main] + follow_sounds(voice, grid, x, z, 3))
            pcs = set(n.p % 12 for n in ns)
            hit = sum(1 for g in got if g % 12 in pcs)
            reg = abs(sum(got) / 3.0 - sum(upper) / float(len(upper)))
            move = 0.0 if prev is None else (0.0 if cheb(prev, (x, z)) <= 1 else 5.0)
            cost = -hit * 10 + reg * 0.5 + move
            if best is None or cost < best[0]:
                best = (cost, (x, z))
        prev = best[1]
        top = max(ns, key=lambda n: n.p)
        seq.append((R1.Note(t, max(n.d for n in ns), top.p, max(n.v for n in ns), top.track, top.nid), prev, 0))
    nodes, cuts = nodes_for(seq)
    cb = Cube(voice, nodes, vname, follow=3)
    fit(voice, cb, {i: nd[3] for i, nd in enumerate(nodes) if nd[3] is not None})
    low = Cube(voice, [list(nd) for nd in nodes], vname + " (octave below)", follow=3, layer=-1, src=cb)
    low.volume = cb.volume
    return [cb, low]


def plan_octaves(P, vname, voice, grid, notes, others, vc):
    """Octave pairs (the e.piano): the lower notes one sphere, the upper octave its layer +1 copy."""
    groups = split_chords(notes)
    lower = []
    for t, ns in groups:
        ns = sorted(ns, key=lambda n: n.p)
        lower.append(ns[0])
    upper = []
    for t, ns in groups:
        upper.append(sorted(ns, key=lambda n: n.p)[-1])
    def score(cb, line, lay):
        t, ex = 0, 0
        for i, nd in enumerate(cb.nodes):
            if nd[2] != 1 and nd[3] is not None:
                s = sound(voice, grid, nd[0][0], nd[0][1], cb.octave, False, t % BAR == 0, 0, cb.nudge(i))
                ex += (s == nd[3].p) + (E.layered(s, lay) == nd[3].p + 12 * lay)
            t += nd[1]
        return ex
    a = line_cube(P, vname, voice, grid, lower, others, vc.get("sphere", True), vname)
    b = line_cube(P, vname, voice, grid, upper, others, vc.get("sphere", True), vname)
    if score(b, upper, -1) > score(a, lower, 1):
        cp = Cube(voice, [list(nd) for nd in b.nodes], vname + " (octave below)", sphere=b.sphere, layer=-1, src=b, octave=b.octave,
                  nudges=list(b.nudges) if b.nudges else None)
        cp.volume = b.volume
        return [b, cp]
    cp = Cube(voice, [list(nd) for nd in a.nodes], vname + " (octave above)", sphere=a.sphere, layer=1, src=a, octave=a.octave,
              nudges=list(a.nudges) if a.nudges else None)
    cp.volume = a.volume
    return [a, cp]


LANE_ORDER = ["bass", "harmony", "lead", "high", "fall"]


def plan_song(cfg, max_sections=None):
    P = Plan(cfg)
    lanes = cfg["lanes"]
    by_lane = {}
    for vname, vc in cfg["voices"].items():
        by_lane.setdefault(vc["lane"], []).append(vname)
    secs = cfg["sections"][:max_sections] if max_sections else cfg["sections"]
    col = 0
    for si, sec in enumerate(secs):
        P.sections.append(col)
        P.letters.append(sec["letter"])
        P.roles.append(sec["role"])
        if sec.get("columns", "belt") == "bars":
            specs = [(sec["bar0"] + k, 1, 1) for k in range(sec["bars"])]
        else:
            specs = [(sec["bar0"], sec["bars"], sec.get("passes", 1))]
        for (bar0, nb, passes) in specs:
            P.columns.append(dict(bar0=bar0, bars=nb, passes=passes, section=si))
            t0 = bar0 * BAR
            col_isl = []
            stairs_here = [s for s in cfg.get("stairs", []) if bar0 <= s["bar"] < bar0 + nb]
            # a stairs island holds its lane for its own bars from the column's start (SongManager.SharesMeasures): no grid of that lane there
            held = set()
            for s in stairs_here:
                for k in range(s["island_bars"]):
                    held.add((s["lane"], k))
            for lane in LANE_ORDER:
                if lane not in lanes:
                    continue
                for b in range(nb):
                    bar = bar0 + b
                    if (lane, b) in held:
                        run = [s for s in stairs_here if s["lane"] == lane]
                        for v in by_lane.get(lane, []):
                            for n in P.notes(v, bar * BAR, (bar + 1) * BAR):
                                if not any(s["voice"] == v and s["bar"] == bar and s["cover"][0] * TPB <= n.t < s["cover"][1] * TPB for s in run):
                                    P.notes_dropped.append((v, n, "the stairs hold the lane in bar %d" % bar))
                        continue
                    cover = {}
                    for s in stairs_here:
                        if s["bar"] == bar:
                            cover[s["voice"]] = (int(s["cover"][0] * TPB), int(s["cover"][1] * TPB))
                    here = [v for v in by_lane.get(lane, []) if P.notes(v, bar * BAR, (bar + 1) * BAR)]
                    carried = [v for v in here if si in P.cfg.get("long", {}).get(v, []) and bar0 != sec["bar0"]]
                    here = [v for v in here if v not in carried]
                    if not here and not carried:
                        continue
                    name = P.chord_of(bar)
                    reg = lanes[lane]["reg"]
                    targets = [n.p for v in here + carried for n in P.notes(v, bar * BAR, (bar + 1) * BAR)]
                    voice0 = P.voice((here + carried)[0])
                    grid = Grid(name, best_root(name, reg, voice0, targets), reg)
                    cubes = plan_lane_bar(P, lane, grid, bar * BAR, here, cover) if here else []
                    if not cubes and not carried:
                        continue
                    isl = Isl(kind=0, col=col, bar=b, bars=1, lane=lane, z=lanes[lane]["z"], reg=reg, grid=grid, chord=name, cubes=cubes,
                              repeat=passes, vary=sec.get("vary", {}).get(lane, 0), energy=2)
                    longs = [v for v in here if si in P.cfg.get("long", {}).get(v, []) and bar0 == sec["bar0"]]
                    if longs and sec.get("columns") == "bars":
                        isl.carry = min(3, sec["bars"] - 1)
                        P.devices.append(("extend", "bar %d" % bar, "%s: one cube walks the section's %d chords (a long grid)" % (",".join(longs), isl.carry + 1)))
                    rep1 = sec.get("repeat1", {})
                    if lane in rep1 and b in rep1[lane]:
                        isl.repeat = 1
                    col_isl.append(isl)
            # stairs islands (a run falling into the next chord), after the column's grids (their notes set the run's top)
            for s in stairs_here:
                isl = Isl(kind=3, col=col, bar=0, bars=s["island_bars"], lane=s["lane"], z=lanes[s["lane"]]["z"], reg=s["reg"],
                          repeat=passes, stair=dict(type=s["type"], dir=s.get("dir", -1), steps=s["steps"], rate=s["rate"], lead=s.get("lead", True),
                                                   reg=s["reg"]), voice=P.voice(s["voice"]), vname=s["voice"], src_bar=s["bar"], cover=s["cover"])
                col_isl.append(isl)
            # the launch: on the harmony island at the column's start (the swell over the column's last bar, the crash on the next downbeat)
            if any(bar0 <= lb < bar0 + nb * passes for lb in cfg.get("launches", [])):
                lh = [i for i in col_isl if i.kind == 0 and i.lane == "harmony" and i.bar == 0 and i.repeat == passes]
                if lh:
                    lh[0].launch = True
                    P.devices.append(("launch", "bar %d" % (bar0 + nb * passes - 1), "riser over the bar, crash on bar %d" % (bar0 + nb * passes)))
            P.islands.extend(col_isl)
            col += 1
    # stairs pitches: the column's first chord island, the next column's, the column's top note
    for isl in P.islands:
        if isl.kind != 3:
            continue
        mates = [i for i in P.islands if i.col == isl.col and i.kind == 0]
        chord = mates[0].grid if mates else None
        nxt = [i for i in P.islands if i.col == (isl.col + 1 if isl.col + 1 < col else 0) and i.kind == 0]
        nchord = nxt[0].grid if nxt else chord
        top = None
        for m in mates:
            for cb in m.cubes:
                for nd in cb.nodes:
                    if nd[2] != 1:
                        tm = m.grid.t[nd[0][0]][nd[0][1]] + 12 * cb.octave
                        if tm >= 60 and (top is None or tm > top):
                            top = tm
        mid = sorted((n for n in P.notes(isl.vname, isl.src_bar * BAR, (isl.src_bar + 1) * BAR) if isl.cover[0] * TPB <= n.t < isl.cover[1] * TPB),
                     key=lambda n: (n.t, -n.p))
        tops = []
        for n in mid:
            if not tops or tops[-1].t != n.t:
                tops.append(n)
        regs = [-2, -1, 0, 1] if isl.stair["reg"] == "auto" else [isl.stair["reg"]]
        best = None
        for rg in regs:
            st = dict(isl.stair, reg=rg)
            raw = stair_pitches(st, chord, nchord, top, cfg["key_pc"], cfg["minor"])
            run = fold_run(isl.voice, dict(reg=rg, pitches=raw))
            k = min(len(run), len(tops))
            err = sum(abs(a - b.p) for a, b in zip(run[-k:], tops[-k:])) / float(max(1, k)) if k else 0.0
            if best is None or (err, abs(rg)) < best[0]:
                best = ((err, abs(rg)), rg, raw, run)
        isl.stair["reg"] = best[1]
        isl.reg = best[1]
        isl.run_tiles = dict(reg=best[1], pitches=best[2])
        isl.run = best[3]
        xs, mods, durs = stair_path(isl.stair, isl.bars)
        nodes = [[(x, 0), d, m, None] for x, m, d in zip(xs, mods, durs)]
        cb = Cube(isl.voice, nodes, isl.vname + " (stairs runner)", sphere=False, gate=2)
        # the runner's volume from the MIDI run's velocities
        mid = [n for n in P.notes(isl.vname, isl.src_bar * BAR, (isl.src_bar + 1) * BAR) if isl.cover[0] * TPB <= n.t < isl.cover[1] * TPB]
        if mid:
            tv = sum(n.v for n in mid) / float(len(mid))
            cb.volume = min(range(1, 101), key=lambda vi: abs(E.pitched_velocity(isl.voice, vi / 100.0, 0, 12) - tv)) / 100.0
        else:
            cb.volume = 0.6
        isl.cubes = [cb]
        # a voice doubling this one (the music box on the sax in C) rides the stairs as an octave copy of the runner
        for d in cfg.get("doubles", []):
            if d["of"] == isl.vname and d["bars"][0] <= isl.src_bar < d["bars"][1]:
                dv = P.voice(d["voice"])
                mine = top_line(Plan.notes(P, d["voice"], isl.src_bar * BAR, (isl.src_bar + 1) * BAR))
                offs = [round((m.p - n.p) / 12.0) for n in isl.mid for m in mine if m.t == n.t]
                lay = max(set(offs), key=offs.count) if offs else 1
                cp = Cube(dv, [list(nd) for nd in nodes], d["voice"] + " (copy of the stairs)", layer=lay, src=cb, echo=cb.echo)
                tv2 = sum(m.v for m in mine) / float(max(1, len(mine))) if mine else tv
                cp.volume = min(range(1, 101), key=lambda vi: abs(E.pitched_velocity(dv, vi / 100.0, 0, 12) - tv2)) / 100.0
                isl.cubes.append(cp)
                c0 = P.columns[isl.col]
                for p in range(min(isl.repeat, c0["passes"])):
                    P.stairs_placed.append((d["voice"], c0["bar0"] + p * c0["bars"] + isl.bar, isl.lane))
        P.devices.append(("stairs", "bar %d" % isl.src_bar, "%s %s x%d: %s (MIDI %s)" % (
            isl.vname, ["chord", "scale", "spark", "slide", "bright", "walk"][isl.stair["type"]], isl.stair["steps"],
            " ".join(R1.name_of(p) for p in isl.run), " ".join(R1.name_of(n.p) for n in sorted(mid, key=lambda n: n.t)))))
    # Moons: one per 8-bar phrase starting a column
    dc = cfg["drums"]
    dvoice = P.voices[(9, dc["caption"])]
    dnotes = P.tracks[dc["track"]]
    starts = {c["bar0"]: i for i, c in enumerate(P.columns)}
    last_bar = P.columns[-1]["bar0"] + P.columns[-1]["bars"] * P.columns[-1]["passes"]
    for mi, (b0, b1) in enumerate(dc["moons"]):
        if b0 >= last_bar:
            break
        if b0 not in starts:
            raise BuildError("moon %d: bar %d does not start a column" % (mi, b0))
        t0, t1 = b0 * BAR, min(b1, last_bar) * BAR
        mine = [R1.Note(n.t - t0, n.d, n.p, n.v, n.track, n.nid) for n in dnotes if t0 <= n.t < t1]
        mo = R1.plan_moon(R1_cfg(cfg), dc, dvoice, mine, mi, starts[b0], 0, t1 - t0, [])
        P.moons.append(mo)
    return P


# ================================================================== round 3: more creative, more effects (docs/deacube-gallery.md, "get proto")
LANE_ORDER3 = ["bass", "harmony", "lead", "high"]     # the order islands are written in a column: the anchor (section controls) is the bass
STAIR_NAMES = ["chord", "scale", "spark", "slide", "bright", "walk"]


def echo_filter(notes, gap=12, max_rep=4):
    """A written-out echo (the sax in D: every note repeats 2 16ths later, softer, four times): a note repeating an earlier, louder note of its
    pitch k x gap ticks later is that note's echo (the cube's echo sticker plays it). Returns (line, echoes)."""
    keep, echo, seen = [], [], {}
    for n in sorted(notes, key=lambda n: (n.t, -n.v)):
        src = any(m.v > n.v for k in range(1, max_rep + 1) for m in seen.get((n.p, n.t - k * gap), []))
        (echo if src else keep).append(n)
        seen.setdefault((n.p, n.t), []).append(n)
    return keep, echo


def top_line(notes):
    """The top note of every onset, repeated pitches (and their octave flicker) collapsed to the first."""
    tops = []
    for n in sorted(notes, key=lambda n: (n.t, -n.p)):
        if not tops or tops[-1].t != n.t:
            tops.append(n)
    out = []
    for n in tops:
        if out and out[-1].p % 12 == n.p % 12:
            if n.p > out[-1].p:          # an octave flicker: the line keeps its upper note
                out[-1] = R1.Note(out[-1].t, out[-1].d, n.p, out[-1].v, n.track, out[-1].nid)
            continue
        out.append(n)
    return out


def descending_run(notes, min_notes=4):
    """A DESCENDING MEASURE: the longest run of the bar's top line stepping down by 1..4 semitones note to note (>= 3 steps down = 4 notes) that the
    bar does not climb back from (every later note at most the run's second-lowest note), else None."""
    line = top_line(notes)
    best, cur = [], line[:1]
    for a, b in zip(line, line[1:]):
        if 1 <= a.p - b.p <= 4:
            cur.append(b)
        else:
            cur = [b]
        if len(cur) >= len(best):
            best = list(cur)
    if len(best) < min_notes:
        return None
    after = [n for n in line if n.t > best[-1].t]
    if any(n.p > best[-2].p for n in after):
        return None
    return best


def sec_columns(sec):
    c = sec.get("columns", "belt")
    if c == "bars":
        return [(sec["bar0"] + k, 1, 1) for k in range(sec["bars"])]
    return [(sec["bar0"], sec["bars"], sec.get("passes", 1))]


class Plan3(Plan):
    def __init__(self, cfg):
        Plan.__init__(self, cfg)
        KEY[0], KEY[1] = cfg["key_pc"], cfg["minor"]
        self.sec_span = []
        for si, sec in enumerate(cfg["sections"]):
            span = sum(nb * ps for _, nb, ps in sec_columns(sec))
            self.sec_span.append((sec["bar0"], sec["bar0"] + span))
        self.cur_shape = None
        self.stairs_missed = []      # (voice, bar, why)
        self.stairs_placed = []      # (voice, bar, lane)  every MIDI bar a stairs plays
        self.nudged = {}             # voice -> nudged steps
        self.desc = {}

    def sec_at(self, bar):
        for si, (a, b) in enumerate(self.sec_span):
            if a <= bar < b:
                return si
        return len(self.sec_span) - 1

    def nudge_ok(self, vname):
        return bool(self.cfg.get("nudges")) and vname in self.cfg.get("nudge_voices", [])

    def lane_of(self, vname, bar):
        sec = self.cfg["sections"][self.sec_at(bar)]
        return sec.get("lanes", {}).get(vname, self.cfg["voices"][vname]["lane"])

    def lane_reg(self, lane, bar):
        sec = self.cfg["sections"][self.sec_at(bar)]
        return sec.get("lane_reg", {}).get(lane, self.cfg["lanes"][lane]["reg"])

    def notes(self, vname, t0, t1, raw=False):
        base = Plan.notes(self, vname, t0, t1)
        if raw or not base:
            return base
        bar = t0 // BAR
        sec = self.cfg["sections"][self.sec_at(bar)]
        if vname in sec.get("echo", {}):
            # the echoes of the bar before ring over the bar line: filter with them in view, keep this bar's line
            wide = Plan.notes(self, vname, t0 - BAR, t1)
            line, _ = echo_filter(wide)
            keep = set(n.nid for n in line)
            base = [n for n in base if n.nid in keep]
        r = sec.get("raise", {}).get(self.lane_of(vname, bar), 0)
        if r:
            base = [R1.Note(n.t, n.d, n.p + 12 * r, n.v, n.track, n.nid) for n in base]
        return base


def plan_pedal(P, vname, voice, grid, notes, others, vc):
    """A PEDAL (the sax's F#-E rocking over every chord of the loop): one cube pumping ONE tile, the notes off that tile nudged a scale step or
    two (v9 N). The tile: the one that plays every note with the fewest nudges."""
    line = mono_lines(notes)[0]
    best = None
    for (x, z) in grid.tiles():
        if any(occupied(others, (x, z), n.t) for n in line):
            continue
        nds, miss = [], 0
        for n in line:
            k = next((k for k in (0, 1, -1, 2, -2) if sound(voice, grid, x, z, 0, False, n.t % BAR == 0, 0, k) == n.p), None)
            if k is None:
                miss += 1
                k = 0
            nds.append(k)
        cost = (miss, sum(1 for k in nds if k), abs(sum(nds)))
        if best is None or cost < best[0]:
            best = (cost, (x, z), nds)
    _, tl, nds = best
    seq = [(n, tl, 0) for n in line]
    nodes, cuts = nodes_for(seq)
    by = {n.nid: k for n, k in zip(line, nds)}
    cb = Cube(voice, nodes, vname, sphere=False, gate=2)
    cb.nudges = [by.get(nd[3].nid, 0) if nd[3] is not None and nd[2] != 1 else 0 for nd in nodes]
    fit(voice, cb, {i: nd[3] for i, nd in enumerate(nodes) if nd[3] is not None})
    return [cb]


def plan_keys(P, vname, lane, notes, reg_pref=(0, -1, 1)):
    """A melody KEYBOARD for one bar (the lead where grids cannot carry it): round 1's keyboard planner on this bar's notes."""
    vc = dict(P.cfg["voices"][vname])
    vc["lane"] = lane
    vc["registers"] = list(reg_pref)
    devs = []
    isl = R1.plan_keyboard(dict(columns=[(0, 1, 1)]), vname, vc, P.voice(vname), notes, 0, 0, BAR, devs)
    return isl, devs


def stair_fit(P, voice, st, chord, nchord, top, mid):
    """The stairs' type, register, steps and rate that sound nearest the MIDI run `mid` (the top line). Returns (stair dict, run, error)."""
    n = max(3, min(8, len(mid)))
    iois = [b.t - a.t for a, b in zip(mid, mid[1:])] or [12]
    ioi = sorted(iois)[len(iois) // 2]
    rate = min(STAIR_RATES, key=lambda r: (abs(r - ioi), r))
    end = mid[-1].t + min(mid[-1].d, rate)
    lead = abs(BAR - end) <= abs(mid[0].t - 0) or mid[0].t >= BAR // 2
    best = None
    for stype in st.get("types", (1, 2, 0, 3, 4)):
        for rg in st.get("regs", (-2, -1, 0, 1, 2)):
            sd = dict(type=stype, dir=-1, steps=n, rate=rate, lead=lead, reg=rg)
            raw = stair_pitches(sd, chord, nchord, top, P.cfg["key_pc"], P.cfg["minor"])
            run = fold_run(voice, dict(reg=rg, pitches=raw))
            k = min(len(run), len(mid))
            err = sum(abs(a - b.p) for a, b in zip(run[-k:], mid[-k:])) / float(max(1, k))
            cost = (round(err, 3), abs(rg), stype != st.get("prefer", 1))
            if best is None or cost < best[0]:
                best = (cost, sd, raw, run, err)
    return best[1], best[2], best[3], best[4]


def plan_song3(cfg, max_sections=None):
    P = Plan3(cfg)
    lanes = cfg["lanes"]
    voices = list(cfg["voices"])
    secs = cfg["sections"][:max_sections] if max_sections else cfg["sections"]
    last_bar = max(b for a, b in P.sec_span[:len(secs)])
    # descending measures per voice (the bar's top line, echoes collapsed)
    for v in cfg.get("stairs_voices", voices):
        P.desc[v] = {}
        for bar in range(last_bar):
            run = descending_run(P.notes(v, bar * BAR, (bar + 1) * BAR))
            if run:
                P.desc[v][bar] = run
    long_of = cfg.get("long", {})
    col = 0
    for si, sec in enumerate(secs):
        P.sections.append(col)
        P.letters.append(sec["letter"])
        P.roles.append(sec["role"])
        specs = sec_columns(sec)
        for ci_sec, (bar0, nb, passes) in enumerate(specs):
            P.columns.append(dict(bar0=bar0, bars=nb, passes=passes, section=si))
            P.cur_shape = sec.get("shape")
            rewind = sec.get("style") == "rewind" and passes > 1

            def bar_of(p, k):
                return bar0 + p * nb + k
            lane_v = {L: [v for v in voices if P.lane_of(v, bar0) == L] for L in lanes}

            def vnotes(v, bar):
                if P.double_of(v, bar):
                    return []
                return P.notes(v, bar * BAR, (bar + 1) * BAR)
            # ---- 1. stairs: every descending measure of pass 0 (later passes ride the column's repeat)
            held, stairs = {}, []
            order = sorted(P.desc, key=lambda v: (P.lane_of(v, bar0) != "lead", voices.index(v)))
            pedal_bars = set()
            for v, lst in long_of.items():
                if si in lst and cfg["voices"][v].get("pedal"):
                    pedal_bars |= set(bar0 + k for k in range(nb))
            for k in range(nb):
                for v in order:
                    b = bar_of(0, k)
                    if b not in P.desc[v]:
                        continue
                    if b in pedal_bars and v in long_of and si in long_of[v]:
                        P.stairs_missed.append((v, b, "the pedal's long grid holds the lead lane"))
                        continue
                    own = P.lane_of(v, b)
                    prefs = ["lead"] if own == "lead" else ["lead", own]
                    lane = None
                    for L in prefs:
                        if L not in lanes or (L, k) in held:
                            continue
                        if any(u != v and vnotes(u, bar_of(p, k)) for u in lane_v.get(L, []) for p in range(passes)):
                            continue
                        if L == "lead" and any(si in long_of.get(u, []) for u in lane_v.get(L, [])):
                            continue
                        lane = L
                        break
                    if lane is None:
                        P.stairs_missed.append((v, b, "no free lane in its bar"))
                        continue
                    rep = 1
                    for p in range(1, passes):
                        if bar_of(p, k) in P.desc[v]:
                            rep += 1
                        else:
                            break
                    st = dict(v=v, lane=lane, k=k, rep=rep, run=P.desc[v][b])
                    held[(lane, k)] = st
                    stairs.append(st)
                    for p in range(rep):
                        P.stairs_placed.append((v, bar_of(p, k), lane))
            for v in P.desc:
                for p in range(1, passes):
                    for k in range(nb):
                        b = bar_of(p, k)
                        if b in P.desc[v] and not any(s["v"] == v and s["k"] == k and s["rep"] > p for s in stairs):
                            P.stairs_missed.append((v, b, "a later pass of a repeated column whose first pass does not fall"))
            col_isl = []
            # ---- 2. grids / keyboards per lane and measure
            for L in LANE_ORDER3:
                if L not in lanes:
                    continue
                reg = P.lane_reg(L, bar0)
                for k in range(nb):
                    b = bar_of(0, k)
                    if (L, k) in held:
                        st = held[(L, k)]
                        rn = set(n.nid for n in st["run"])
                        for p in range(passes):
                            for n in vnotes(st["v"], bar_of(p, k)):
                                if p < st["rep"] and (n.t >= st["run"][0].t and n.nid in rn or st["run"][0].t <= n.t):
                                    continue
                                P.notes_dropped.append((st["v"], n, "the stairs hold its lane in bar %d" % bar_of(p, k)))
                        continue
                    cover = {}
                    for st in stairs:
                        if st["k"] == k and st["lane"] != L and P.lane_of(st["v"], b) == L:
                            cover[st["v"]] = (st["run"][0].t, BAR)

                    def lane_set(p):
                        out = set()
                        for v in lane_v[L]:
                            cv = cover.get(v)
                            for n in vnotes(v, bar_of(p, k)):
                                if cv and cv[0] <= n.t < cv[1] and p < 1 + max([s["rep"] - 1 for s in stairs if s["v"] == v and s["k"] == k] or [0]):
                                    continue
                                out.add((n.t, n.p))
                        return out
                    here = [v for v in lane_v[L] if vnotes(v, b)]
                    carried = [v for v in lane_v[L] if si in long_of.get(v, []) and ci_sec > 0]
                    here = [v for v in here if v not in carried]
                    if not here and not carried:
                        for p in range(1, passes):
                            for v in lane_v[L]:
                                for n in vnotes(v, bar_of(p, k)):
                                    P.notes_dropped.append((v, n, "only in a later pass of bar %d's column" % b))
                        continue
                    # the repeat: the later passes play this island again while they sound alike (else it rests there: a break)
                    s0, rep = lane_set(0), 1
                    for p in range(1, passes):
                        sp = lane_set(p)
                        if sp and s0:
                            j = len(s0 & sp) / float(len(s0 | sp))
                            jo = len(set(t for t, _ in s0) & set(t for t, _ in sp)) / float(len(set(t for t, _ in s0) | set(t for t, _ in sp)))
                            if j >= 0.15 or jo >= 0.3:
                                rep += 1
                                continue
                        break
                    for p in range(rep, passes):
                        for v in lane_v[L]:
                            for n in vnotes(v, bar_of(p, k)):
                                P.notes_dropped.append((v, n, "bar %d: its pass rests (the MIDI differs from the first pass)" % bar_of(p, k)))
                    name = P.chord_of(b)
                    energy = sec.get("energy", 2)
                    if isinstance(energy, (list, tuple)):
                        energy = energy[min(k, len(energy) - 1)]
                    if L == "lead" and sec.get("lead") == "keys" and here:
                        notes = [n for v in here for n in vnotes(v, b) if not (cover.get(v) and cover[v][0] <= n.t)]
                        kb, devs = plan_keys(P, here[0], L, notes)
                        isl = Isl(kind=2, col=col, bar=k, bars=1, lane=L, z=lanes[L]["z"], reg=kb.reg, keys=kb, chord=name, cubes=[],
                                  repeat=rep, vary=0, energy=energy, rewind=rewind, vname=here[0])
                        col_isl.append(isl)
                        continue
                    targets = [n.p for v in here + carried for n in P.notes(v, b * BAR, (b + 1) * BAR)]
                    voice0 = P.voice((here + carried)[0])
                    grid = Grid(name, best_root(name, reg, voice0, targets), reg)
                    pedal = [v for v in here if si in long_of.get(v, []) and cfg["voices"][v].get("pedal")]
                    rest = [v for v in here if v not in pedal]
                    cubes = []
                    for v in pedal:
                        cubes.extend(plan_pedal(P, v, P.voice(v), grid, vnotes(v, b), cubes, cfg["voices"][v]))
                    if rest:
                        cubes = plan_lane_bar(P, L, grid, b * BAR, rest, cover, others0=cubes)
                    if not cubes and not carried:
                        continue
                    isl = Isl(kind=0, col=col, bar=k, bars=1, lane=L, z=lanes[L]["z"], reg=reg, grid=grid, chord=name, cubes=cubes,
                              repeat=rep, vary=sec.get("vary", {}).get(L, 0), energy=energy, rewind=rewind)
                    longs = [v for v in here if si in long_of.get(v, []) and ci_sec == 0]
                    if longs and len(specs) > 1:
                        isl.carry = min(3, len(specs) - 1)
                        P.devices.append(("extend", "bar %d" % b, "%s: one cube walks the section's %d chords (a long grid)" % (",".join(longs), isl.carry + 1)))
                    for v, n in sec.get("echo", {}).items():
                        for cb in cubes:
                            if cb.label.split(" ")[0] == v and cb.layer == 0:
                                cb.echo = n
                    col_isl.append(isl)
            # ---- 3. the stairs islands
            for st in stairs:
                isl = Isl(kind=3, col=col, bar=st["k"], bars=1, lane=st["lane"], z=lanes[st["lane"]]["z"], reg=0, repeat=st["rep"],
                          stair=None, voice=P.voice(st["v"]), vname=st["v"], src_bar=bar_of(0, st["k"]), mid=st["run"], rewind=rewind,
                          energy=sec.get("energy", 2) if not isinstance(sec.get("energy", 2), (list, tuple)) else sec["energy"][min(st["k"], len(sec["energy"]) - 1)],
                          echo=sec.get("echo", {}).get(st["v"], 0))
                col_isl.append(isl)
            P.islands.extend(col_isl)
            col += 1
    ncol = col
    # ---- stairs pitches (Harmony.ChordOf / ChordAfterIsland with the barOffset; the top note of the islands sharing its measure)
    for isl in P.islands:
        if isl.kind != 3:
            continue
        c = P.columns[isl.col]
        mates = [i for i in P.islands if i.col == isl.col and i.kind == 0 and i.bar == isl.bar]
        chord = mates[0].grid if mates else Grid(P.chord_of(isl.src_bar), 60, 0)     # SongManager.ChordAt: the first in column order
        if isl.bar + 1 < c["bars"]:
            nm = [i for i in P.islands if i.col == isl.col and i.kind == 0 and i.bar == isl.bar + 1]
        else:
            nc = isl.col + 1 if isl.col + 1 < ncol else 0
            nm = [i for i in P.islands if i.col == nc and i.kind == 0 and i.bar == 0]
        nchord = nm[0].grid if nm else Grid(P.chord_of(isl.src_bar + 1), 60, 0)
        top = None
        for m in mates:
            for cb in m.cubes:
                for nd in cb.nodes:
                    if nd[2] != 1:
                        tm = m.grid.t[nd[0][0]][nd[0][1]] + 12 * cb.octave
                        if tm >= 60 and (top is None or tm > top):
                            top = tm
        sd, raw, run, err = stair_fit(P, isl.voice, cfg.get("stairs_style", {}).get(isl.vname, {}), chord, nchord, top, isl.mid)
        isl.stair = sd
        isl.reg = sd["reg"]
        isl.run_tiles = dict(reg=sd["reg"], pitches=raw)
        isl.run = run
        xs, mods, durs = stair_path(sd, isl.bars)
        nodes = [[(x, 0), d, m, None] for x, m, d in zip(xs, mods, durs)]
        cb = Cube(isl.voice, nodes, isl.vname + " (stairs runner)", sphere=False, gate=2, echo=isl.echo)
        tv = sum(n.v for n in isl.mid) / float(len(isl.mid))
        cb.volume = min(range(1, 101), key=lambda vi: abs(E.pitched_velocity(isl.voice, vi / 100.0, 0, 12) - tv)) / 100.0
        isl.cubes = [cb]
        # a voice doubling this one (the music box on the sax in C) rides the stairs as an octave copy of the runner
        for d in cfg.get("doubles", []):
            if d["of"] == isl.vname and d["bars"][0] <= isl.src_bar < d["bars"][1]:
                dv = P.voice(d["voice"])
                mine = top_line(Plan.notes(P, d["voice"], isl.src_bar * BAR, (isl.src_bar + 1) * BAR))
                offs = [round((m.p - n.p) / 12.0) for n in isl.mid for m in mine if m.t == n.t]
                lay = max(set(offs), key=offs.count) if offs else 1
                cp = Cube(dv, [list(nd) for nd in nodes], d["voice"] + " (copy of the stairs)", layer=lay, src=cb, echo=cb.echo)
                tv2 = sum(m.v for m in mine) / float(max(1, len(mine))) if mine else tv
                cp.volume = min(range(1, 101), key=lambda vi: abs(E.pitched_velocity(dv, vi / 100.0, 0, 12) - tv2)) / 100.0
                isl.cubes.append(cp)
                c0 = P.columns[isl.col]
                for p in range(min(isl.repeat, c0["passes"])):
                    P.stairs_placed.append((d["voice"], c0["bar0"] + p * c0["bars"] + isl.bar, isl.lane))
        P.devices.append(("stairs", "bar %d" % isl.src_bar, "%s %s x%d @%d/8 %s: %s (MIDI %s)%s" % (
            isl.vname, STAIR_NAMES[sd["type"]], sd["steps"], sd["rate"], "lead-in" if sd["lead"] else "from the start",
            " ".join(R1.name_of(p) for p in run), " ".join(R1.name_of(n.p) for n in isl.mid), " x%d" % isl.repeat if isl.repeat > 1 else "")))
    # ---- climb
    for isl in P.islands:
        for cb in isl.cubes:
            cl = P.climb_of(cb.label.split(" ")[0], P.columns[isl.col]["bar0"] + isl.bar)
            if cl and cb.layer == 0 and isl.kind == 0:
                cb.climb = cl
    # ---- launches at the crash bars (an island whose turn ends on that downbeat); the strings' swells the big ones (energy 3)
    starts, total = col_timeline(P)
    pref = {"harmony": 0, "high": 1, "lead": 2, "bass": 3}
    for X in cfg.get("crashes", []):
        cands = []
        for isl in P.islands:
            if isl.kind == 3:
                continue
            c = P.columns[isl.col]
            end = starts[isl.col] + c["bars"] * BAR * min(isl.repeat, c["passes"])
            if end == X * BAR and isl.col + 1 < ncol:
                cands.append(isl)
        if not cands:
            P.devices.append(("launch?", "bar %d" % X, "no island's turn ends there"))
            continue
        big = X in cfg.get("big_crashes", [])
        isl = min(cands, key=lambda i: (pref.get(i.lane, 9), -i.bar, -len(i.cubes)))
        isl.launch = True
        isl.energy = 3 if big else min(isl.energy, 2)      # R's riser scales with the launching island's energy: only the swells are big
        P.devices.append(("launch", "bar %d" % X, "%s %s bar %d: riser into the crash on bar %d%s" % (
            isl.lane, ["grid", "", "keyboard"][isl.kind] if isl.kind != 3 else "stairs", starts[isl.col] // BAR + isl.bar, X, " (BIG: energy 3)" if big else "")))
    # ---- energy: the anchor (the column's first island, which the Moon reads) stays 2; the others' volumes keep the MIDI's loudness
    first = {}
    for isl in P.islands:
        first.setdefault(isl.col, isl)
    for isl in P.islands:
        if first[isl.col] is isl:
            # the anchor stays 2 (the Moon reads it), but a big launch's island keeps its 3 (the intro bar: no Moon plays there)
            if not (isl.launch and isl.energy == 3 and P.columns[isl.col]["bar0"] not in [b0 for b0, _ in cfg["drums"]["moons"]]):
                isl.energy = 2
            continue
        if isl.energy != 2:
            k = E.ENERGY_SCALE[2] / E.ENERGY_SCALE[isl.energy]
            for cb in isl.cubes:
                if cb.volume is not None:
                    cb.volume = min(1.0, cb.volume * k)
            if isl.kind == 2:
                for cb in isl.keys.cubes:
                    cb["volume"] = min(1.0, cb["volume"] * k)
    # ---- nudged steps per voice
    vn_of = {(vc["group"], vc["caption"]): vn for vn, vc in cfg["voices"].items()}
    for isl in P.islands:
        for cb in isl.cubes:
            if cb.nudges and cb.layer == 0:
                vn = vn_of.get((cb.voice.group, cb.voice.caption), cb.label)
                P.nudged[vn] = P.nudged.get(vn, 0) + sum(1 for x in cb.nudges if x)
    # ---- Moons
    dc = cfg["drums"]
    dvoice = P.voices[(9, dc["caption"])]
    dnotes = P.tracks[dc["track"]]
    starts_of = {c["bar0"]: i for i, c in enumerate(P.columns)}
    for mi, (b0, b1) in enumerate(dc["moons"]):
        if b0 >= last_bar:
            break
        if b0 not in starts_of:
            raise BuildError("moon %d: bar %d does not start a column" % (mi, b0))
        t0, t1 = b0 * BAR, min(b1, last_bar) * BAR
        mine = [R1.Note(n.t - t0, n.d, n.p, n.v, n.track, n.nid) for n in dnotes if t0 <= n.t < t1]
        P.moons.append(R1.plan_moon(R1_cfg(cfg), dc, dvoice, mine, mi, starts_of[b0], 0, t1 - t0, []))
    return P


# ================================================================== the SongState JSON
def blank_measure():
    return {"chordKey": "", "root": 60, "semis": [0], "bars": 1, "barOffset": 0, "px": 0.0, "pz": 0.0, "placed": True, "kind": 0, "mood": 0,
            "energy": 2, "fill": False, "sleep": False, "repeat": 1, "group": 0, "fall": 0, "col": 0, "reg": 0, "carry": 0, "stairType": 0,
            "stairDir": -1, "stairSteps": 4, "stairRate": 12, "stairLead": True, "carryStyle": 0, "rewind": False, "vary": 0, "launch": False,
            "lead": False, "phraseOffset": 0, "phraseBeats": 4, "phraseGrid": 12, "keyCount": 0, "secRole": 0, "kit": []}


def column_xs(P):
    xs, x = [], EDGE_INSET
    secs = set(P.sections)
    for ci, c in enumerate(P.columns):
        xs.append(x)
        w = c["bars"] * ISLAND_WIDTH
        x += w + (c["passes"] - 1) * (w + BELT_GAP)
        if ci + 1 in secs:
            x += SECTION_GAP
    return xs


def cube_json(cb, measure, moon, cid, src_id, voice):
    nodes = cb.nodes
    mods = [nd[2] for nd in nodes]
    return {
        "instrument": voice.group, "measure": measure, "xs": [nd[0][0] for nd in nodes], "zs": [nd[0][1] for nd in nodes],
        "rests": [m == 1 for m in mods], "step": 1, "gate": cb.gate, "mode": 0, "volume": round(cb.volume if cb.volume is not None else 0.6, 3),
        "muted": False, "moon": moon, "mods": mods, "hits": -1, "rot": 0, "mask": 0, "octave": cb.octave, "reverse": False, "phase": 0,
        "follow": cb.follow, "echo": cb.echo, "shimmer": False, "rider": False, "seed": 0, "twinOf": -1, "id": cid,
        "durs": [nd[1] for nd in nodes], "voice": voice.voice, "layer": cb.layer, "echoOf": src_id, "climb": cb.climb, "sphere": bool(cb.sphere),
        "nudges": [cb.nudge(i) for i in range(len(nodes))] if cb.nudges and any(cb.nudges) else [],
    }


def to_state(P):
    cfg = P.cfg
    xs = column_xs(P)
    measures, cubes = [], []
    first_of_col = {}
    sec_first = set(P.sections)
    for isl in P.islands:
        mi = len(measures)
        anchor = isl.col not in first_of_col
        if anchor:
            first_of_col[isl.col] = mi
        m = blank_measure()
        m.update({"col": isl.col, "pz": float(isl.z), "repeat": isl.repeat, "vary": isl.vary, "energy": isl.energy, "launch": bool(isl.launch),
                  "carry": isl.carry, "carryStyle": 1 if isl.carry > 0 else 0, "reg": isl.reg, "rewind": bool(getattr(isl, "rewind", False))})
        off = isl.bar if cfg.get("round", 2) >= 3 else 0
        if isl.kind == 0:
            g = isl.grid
            m.update({"chordKey": D.chord_name(g.root, g.q), "root": g.root, "semis": list(g.semis), "bars": 1, "barOffset": isl.bar,
                      "px": xs[isl.col] + isl.bar * ISLAND_WIDTH})
        elif isl.kind == 2:
            kb = isl.keys
            m.update({"chordKey": "Keys", "root": kb.root, "semis": [0], "bars": 1, "barOffset": off, "kind": 2, "keyCount": kb.keys,
                      "reg": kb.reg, "px": xs[isl.col] + off * ISLAND_WIDTH})
        else:
            st = isl.stair
            tonic = 48 + cfg["key_pc"]
            while tonic + 12 <= 66:
                tonic += 12
            m.update({"chordKey": "Stairs", "root": tonic, "semis": [0], "bars": isl.bars, "barOffset": off, "kind": 3,
                      "px": xs[isl.col] + off * ISLAND_WIDTH,
                      "stairType": st["type"], "stairDir": st["dir"], "stairSteps": st["steps"], "stairRate": st["rate"], "stairLead": st["lead"]})
        # v9 (L) the stage lights: the chord grids of the main-melody lane are LEAD (DeaCube spotlights them while they sound; keyboards and
        # stairs are lead anyway). cfg "lead_lane" names the lane (default "lead" when the song has one; None = no lead marks).
        lead_lane = cfg.get("lead_lane", "lead" if "lead" in cfg.get("lanes", {}) else None)
        m["lead"] = bool(isl.kind == 0 and lead_lane is not None and getattr(isl, "lane", None) == lead_lane)
        if anchor and isl.col in sec_first:
            m["secRole"] = P.roles[P.sections.index(isl.col)]
        measures.append(m)
        ids = {}
        if isl.kind == 2:
            base = {}
            for cb in isl.keys.cubes:
                cid = len(cubes) + 1
                if cb["layer"] == 0:
                    base[cb["line"]] = cid
                cj = R1.cube_json(cfg, isl.keys, cb, mi, -1, cid, base.get(cb["line"], -1) if cb["layer"] else -1)
                cj["nudges"] = []
                cubes.append(cj)
        for cb in isl.cubes:
            cid = len(cubes) + 1
            ids[id(cb)] = cid
            voice = cb.voice
            cubes.append(cube_json(cb, mi, -1, cid, ids.get(id(cb.src), -1) if cb.src is not None else -1, voice))
    moons = []
    for mo in P.moons:
        m = blank_measure()
        m.update({"chordKey": "Moon", "root": 36, "semis": [0], "bars": mo.bars, "kind": 1, "col": mo.col, "kit": mo.kit,
                  "px": xs[mo.col] + 2 * ISLAND_WIDTH - EDGE_INSET - (E.MOON_COLS - 1) * SPACING * 0.5,
                  "pz": MOON_Z - (E.MOON_ROWS - 1) * SPACING * 0.5})
        moons.append(m)
        for cb in mo.cubes:
            nodes = cb["nodes"]
            mods = [nd[2] for nd in nodes]
            cubes.append({
                "instrument": mo.voice.group, "measure": 0, "xs": [nd[0][0] for nd in nodes], "zs": [nd[0][1] for nd in nodes],
                "rests": [x == 1 for x in mods], "step": 1, "gate": 1, "mode": 0, "volume": round(cb["volume"], 3), "muted": False,
                "moon": len(moons) - 1, "mods": mods, "hits": -1, "rot": 0, "mask": 0, "octave": 0, "reverse": False, "phase": 0, "follow": 0,
                "echo": 0, "shimmer": False, "rider": False, "seed": 0, "twinOf": -1, "id": len(cubes) + 1, "durs": [nd[1] for nd in nodes],
                "voice": mo.voice.voice, "layer": 0, "echoOf": -1, "climb": 0, "sphere": False})
    for i, c in enumerate(cubes):
        h = 2166136261
        for ch in (cfg["id"] + ":" + str(i)):
            h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
        c["seed"] = (h & 0x7FFFFFFF) or 1
    ngroups = 11
    deck = []
    for name in cfg.get("deck", []):
        p, q = D.parse_chord(name)
        root = p + 48
        while root < 55:
            root += 12
        d = blank_measure()
        d.update({"chordKey": D.chord_name(root, q), "root": root, "semis": list(D.QUALITIES[q]), "placed": False, "col": -1})
        deck.append(d)
    return {
        "name": cfg["title"], "bpm": float(cfg["bpm"]), "beatsPerBar": 4, "swing": 0.0, "loop": False, "transpose": 0,
        "measures": measures, "cubes": cubes, "instVolume": [1.0] * ngroups, "instMuted": [False] * ngroups, "version": 6, "moons": moons,
        "climate": 0, "tone": 1.0, "space": 1.0, "demoSeed": 0, "deck": deck, "sections": list(P.sections), "sectionLetters": list(P.letters),
        "keyTonic": cfg["key_pc"], "keyMinor": bool(cfg["minor"]),
    }


# ================================================================== long grids (SongManager.CarryTargets, AudioCube.RebuildHops, Harmony.AdaptPedal)
TENSION = 0.55


def note_score(p, root, semis, tonic, minor):
    """Harmony.NoteScore: a chord tone 1, a half step above one 0 (the avoid note), in the key 0.55, else 0."""
    rel = (p - root) % 12
    s = set(x % 12 for x in semis)
    if rel in s:
        return 1.0
    if (rel - 1) % 12 in s:
        return 0.0
    return TENSION if (p - tonic) % 12 in (MINOR_STEPS if minor else MAJOR_STEPS) else 0.0


def adapt_pedal(src, root, semis, tonic, minor, pool):
    out = []
    ps = set(pool)
    for m in src:
        if m < 0:
            out.append(-1)
            continue
        if m in ps and note_score(m, root, semis, tonic, minor) >= TENSION:
            out.append(m)
            continue
        found = None
        for d in range(1, 13):
            if (m - d) in ps and note_score(m - d, root, semis, tonic, minor) >= TENSION:
                found = m - d
                break
            if (m + d) in ps and note_score(m + d, root, semis, tonic, minor) >= TENSION:
                found = m + d
                break
        if found is None and pool:
            found = min(pool, key=lambda q: (abs(q - m), q))
        out.append(m if found is None else found)
    return out


def carried_tiles(cube, home, targets, tonic, minor):
    """The tiles a long grid's cube plays on each target (hop k from hop k-1): [[(x, z) or None per node] per target]."""
    prev = [home.t[nd[0][0]][nd[0][1]] if nd[2] != 1 else -1 for nd in cube.nodes]
    prev_tiles = [tuple(nd[0]) for nd in cube.nodes]
    out = []
    for tg in targets:
        g = tg.grid
        pool = sorted(set(g.t[x][z] for x, z in g.tiles()))
        new = adapt_pedal(prev, g.root, g.semis, tonic, minor, pool)
        tiles = []
        last = None
        for i, m in enumerate(new):
            if m < 0:
                tiles.append(last if last is not None else prev_tiles[i])
                continue
            near = last if last is not None else prev_tiles[i]
            cands = [(x, z) for x, z in g.tiles() if g.t[x][z] == m]
            tl = min(cands, key=lambda c: (cheb(c, near), c)) if cands else near
            tiles.append(tl)
            last = tl
        out.append(tiles)
        prev, prev_tiles = new, tiles
    return out


# ================================================================== what the JSON plays (engine mirror) and the numbers
def col_timeline(P):
    starts, t = [], 0
    for c in P.columns:
        starts.append(t)
        t += c["bars"] * c["passes"] * BAR
    return starts, t


def decode(P):
    """Every note the plan plays: [(song tick, ticks, pitch, velocity, vname, kind)] (kind: main / follow / layer / stairs / drum).
    Vary is not modelled (a varied pass plays as the first)."""
    starts, total = col_timeline(P)
    vname_of = {}
    for vn, vc in P.cfg["voices"].items():
        vname_of[(vc["group"], vc["caption"])] = vn
    out = []
    tonic, minor = P.cfg["key_pc"], P.cfg["minor"]
    for isl in P.islands:
        if isl.kind != 0 or not isl.carry:
            continue
        targets = []
        for k in range(1, isl.carry + 1):
            tg = [i for i in P.islands if i.col == isl.col + k and i.kind == 0 and abs(i.z - isl.z) < 0.5]
            if not tg:
                break
            targets.append(tg[0])
        for cb in isl.cubes:
            vn = vname_of.get((cb.voice.group, cb.voice.caption), cb.voice.caption)
            for tg, tiles in zip(targets, carried_tiles(cb, isl.grid, targets, tonic, minor)):
                w0 = starts[tg.col] + tg.bar * BAR
                t = 0
                for i, (nd, tl) in enumerate(zip(cb.nodes, tiles)):
                    if nd[2] != 1:
                        nu = cb.nudge(i)
                        main = sound(cb.voice, tg.grid, tl[0], tl[1], cb.octave, nd[2] == 8, t % BAR == 0, cb.layer, nu)
                        pitches = [main] + ([] if nu else follow_sounds(cb.voice, tg.grid, tl[0], tl[1], cb.follow, cb.octave, cb.layer))
                        vel = E.pitched_velocity(cb.voice, cb.volume, nd[2] if nd[2] in (2, 3) else 0, t)
                        for k2, pp in enumerate(pitches):
                            out.append((w0 + t, nd[1], pp, vel if k2 == 0 else int(round(vel * 0.6)), vn, "carried" if k2 == 0 else "follow"))
                    t += nd[1]
    for isl in P.islands:
        c = P.columns[isl.col]
        cpass = c["bars"] * BAR
        en = getattr(isl, "energy", 2)
        for p in range(min(isl.repeat, c["passes"])):
            w0 = starts[isl.col] + p * cpass + isl.bar * BAR
            wl = min(cpass, isl.bars * BAR)
            if isl.kind == 2:
                kb = isl.keys
                vn = isl.vname
                for cb in kb.cubes:
                    t = 0
                    for nd in cb["nodes"]:
                        if t >= wl:
                            break
                        if nd[2] != 1:
                            pp = E.key_sound(kb.voice, kb.root + nd[0] + 12 * kb.reg, kb.reg, cb["layer"], nd[2] == 8)
                            vel = E.pitched_velocity(kb.voice, cb["volume"], nd[2] if nd[2] in (2, 3) else 0, t, en)
                            out.append((w0 + t, nd[1], pp, vel, vn, "keys" if not cb["layer"] else "layer"))
                        t += nd[1]
                continue
            for cb in isl.cubes:
                vn = vname_of.get((cb.voice.group, cb.voice.caption), cb.voice.caption)
                t = 0
                for i, nd in enumerate(cb.nodes):
                    if t >= wl:
                        break
                    if nd[2] != 1:
                        if isl.kind == 3:
                            pitches = [E.layered(isl.run[nd[0][0]], cb.layer) if cb.layer else isl.run[nd[0][0]]]
                            kind = "stairs"
                        else:
                            x, z = nd[0]
                            nu = cb.nudge(i)
                            main = sound(cb.voice, isl.grid, x, z, cb.octave, nd[2] == 8, t % BAR == 0, cb.layer, nu)
                            pitches = [main] + ([] if nu else follow_sounds(cb.voice, isl.grid, x, z, cb.follow, cb.octave, cb.layer))
                            kind = "layer" if cb.layer else "main"
                        vel = E.pitched_velocity(cb.voice, cb.volume, nd[2] if nd[2] in (2, 3) else 0, t, en)
                        for k, pp in enumerate(pitches):
                            out.append((w0 + t, nd[1], pp, vel if k == 0 else int(round(vel * 0.6)), vn, kind if k == 0 else "follow"))
                        ev = float(vel)
                        for e in range(1, getattr(cb, "echo", 0) + 1):
                            te = t + e * nd[1]
                            if te >= wl:
                                break
                            ev *= 0.65
                            out.append((w0 + te, nd[1], pitches[0], int(round(ev)), vn, "echo"))
                    t += nd[1]
    # Moons
    moon_starts = sorted(set(mo.col for mo in P.moons))
    for mo in P.moons:
        s0 = starts[mo.col]
        later = [starts[c] for c in moon_starts if c > mo.col]
        s1 = min(later) if later else total
        wl = mo.bars * BAR
        w = s0
        while w < s1:
            ln = min(wl, s1 - w)
            for cb in mo.cubes:
                t = 0
                for nd in cb["nodes"]:
                    if t >= ln:
                        break
                    if nd[2] != 1:
                        piece = E.drum_piece(nd[0][0], nd[0][1], E.MOON_ROWS, mo.kit)
                        vel = E.drum_velocity(mo.voice, cb["volume"], nd[2], t, nd[0][0], nd[0][1])
                        out.append((w + t, 3, piece, vel, "drums", "drum"))
                    t += nd[1]
            w += wl
    out.sort()
    return out


def chord_tones(name):
    p, q = D.parse_chord(name)
    return set((p + s) % 12 for s in D.QUALITIES[q])


def numbers(P):
    """The round-2 numbers: harmony coverage per bar and voice, the groove (bass, drums), pitch matches per voice, the melody's contour."""
    cfg = P.cfg
    starts, total = col_timeline(P)
    last_bar = total // BAR
    played = decode(P)
    lines = []
    # harmony coverage: the MIDI's note-time on the bar's chord tones, per voice
    cov = {}
    for vn, vc in cfg["voices"].items():
        hit = tot = 0
        for n in P.tracks[vc["track"]]:
            if n.t >= last_bar * BAR:
                continue
            T = chord_tones(P.chord_of(n.t // BAR))
            tot += n.d
            if n.p % 12 in T:
                hit += n.d
        if tot:
            cov[vn] = 100.0 * hit / tot
    lines.append("harmony coverage (MIDI note-time on the bar's card): " + ", ".join("%s %.0f%%" % kv for kv in sorted(cov.items())))
    # pitch + onset matches per voice (the MIDI's notes in the planned bars)
    pool = {}
    for e in played:
        pool.setdefault((e[0], e[4]), []).append(e)
    rows = []
    for vn, vc in list(cfg["voices"].items()) + [("drums", dict(track=cfg["drums"]["track"]))]:
        notes = [n for n in P.tracks[vc["track"]] if n.t < last_bar * BAR]
        if not notes:
            continue
        on = ex = 0
        for n in notes:
            got = pool.get((n.t, vn), [])
            if got:
                on += 1
                if any(g[2] == n.p for g in got):
                    ex += 1
        rows.append((vn, len(notes), on, ex))
    lines.append("  %-8s %6s %10s %10s" % ("voice", "notes", "onset", "onset+pitch"))
    for vn, n, on, ex in rows:
        lines.append("  %-8s %6d %9.0f%% %9.0f%%" % (vn, n, 100.0 * on / n, 100.0 * ex / n))
    extra = {}
    midi_on = set()
    for vn, vc in cfg["voices"].items():
        for n in P.tracks[vc["track"]]:
            midi_on.add((n.t, vn))
    for n in P.tracks[cfg["drums"]["track"]]:
        midi_on.add((n.t, "drums"))
    for e in played:
        if (e[0], e[4]) not in midi_on and e[5] != "follow":
            extra[e[4]] = extra.get(e[4], 0) + 1
    if extra:
        lines.append("  onsets the MIDI has not (main notes): " + ", ".join("%s %d" % kv for kv in sorted(extra.items())))
    # contour of the melodic voices: the top line's steps up / down / same, MIDI vs played (onsets the plan plays)
    for vn in cfg.get("contour_voices", []):
        vc = cfg["voices"][vn]
        top = {}
        for n in P.tracks[vc["track"]]:
            if n.t < last_bar * BAR and (n.t not in top or n.p > top[n.t]):
                top[n.t] = n.p
        ptop = {}
        for e in played:
            if e[4] == vn and e[5] in ("main", "stairs") and (e[0] not in ptop or e[2] > ptop[e[0]]):
                ptop[e[0]] = e[2]
        ts = [t for t in sorted(top) if t in ptop]
        agree = sum(1 for a, b in zip(ts, ts[1:]) if (top[b] > top[a]) - (top[b] < top[a]) == (ptop[b] > ptop[a]) - (ptop[b] < ptop[a]))
        lines.append("  contour %-6s %d of %d steps keep their direction (%.0f%%)" % (vn, agree, max(0, len(ts) - 1), 100.0 * agree / max(1, len(ts) - 1)))
    return "\n".join(lines)


def describe(P):
    lines = []
    starts, total = col_timeline(P)
    lines.append("%s: %d sections, %d columns, %d islands (%d chord grids, %d stairs), %d moons, %d cubes, %d bars" % (
        P.cfg["id"], len(P.sections), len(P.columns), len(P.islands), sum(1 for i in P.islands if i.kind == 0),
        sum(1 for i in P.islands if i.kind == 3), len(P.moons), sum(len(i.cubes) for i in P.islands) + sum(len(m.cubes) for m in P.moons), total // BAR))
    for ci, c in enumerate(P.columns):
        isl = [i for i in P.islands if i.col == ci]
        parts = []
        for lane in LANE_ORDER + ["lead"] * 0:
            li = sorted((i for i in isl if i.lane == lane), key=lambda i: i.bar)
            if not li:
                continue
            desc = []
            for i in li:
                if i.kind == 3:
                    desc.append("@%d STAIRS(%s %s)%s" % (i.bar, i.vname, STAIR_NAMES[i.stair["type"]], " r%d" % i.repeat if i.repeat != c["passes"] else ""))
                elif i.kind == 2:
                    desc.append("@%d KEYS[%s]%s%s" % (i.bar, i.vname, " r%d" % i.repeat if i.repeat != c["passes"] else "", " LAUNCH" if i.launch else ""))
                else:
                    cv = ",".join(sorted(set(cb.label.split(" ")[0] for cb in i.cubes)))
                    desc.append("@%d %s[%s]%s%s%s" % (i.bar, i.chord, cv, " r%d" % i.repeat if i.repeat != c["passes"] else "", " LAUNCH" if i.launch else "",
                                                      " carry%d" % i.carry if i.carry else ""))
            parts.append("%s(reg%+d): %s" % (lane, li[0].reg, " ".join(desc)))
        rw = any(getattr(i, "rewind", False) for i in isl)
        lines.append("  col %d bar %d%s%s %s | %s" % (ci, c["bar0"], " x%d" % c["passes"] if c["passes"] > 1 else "", " REWIND" if rw else "", P.letters[c["section"]], " | ".join(parts)))
    for d in P.devices:
        lines.append("  device %-7s %-8s %s" % d)
    return "\n".join(lines)


def build(name):
    """(cfg, plan, SongState JSON, index entry) of an import's round-2 song."""
    cfg = load_config(name)
    P = plan_song3(cfg) if cfg.get("round", 2) >= 3 else plan_song(cfg)
    state = to_state(P)
    starts, total = col_timeline(P)
    entry = {"id": cfg["id"], "title": cfg["title"], "after": cfg["after"], "vibe": cfg["vibe"], "bpm": float(cfg["bpm"]),
             "bars": total // BAR, "file": "Gallery/" + cfg["id"], "blurb": cfg["blurb"], "hook": cfg["hook"]}
    return cfg, P, state, entry


def checks(P, state):
    """The round-2 song's own checks (build_gallery --verify): no keyboards; at most 4 lanes a column; every chord grid's chord one of DeaCube's;
    no note landing on another cube's tile (the stack rule); the groove exact (the drums' onsets and pieces, the bass's onsets); a cube's path
    walks to neighbouring tiles (spheres leap)."""
    bad = []
    if P.cfg.get("round", 2) < 3 and any(m["kind"] == 2 for m in state["measures"]):
        bad.append("a keyboard")
    for isl in P.islands:
        if isl.kind == 2 and (isl.lane != "lead" or P.cfg["sections"][P.columns[isl.col]["section"]].get("lead") != "keys"):
            bad.append("col %d: a keyboard outside a lead-keyboard section" % isl.col)
    for ci in range(len(P.columns)):
        lanes = set(round(i.z, 1) for i in P.islands if i.col == ci)
        if len(lanes) > 4:
            bad.append("column %d has %d lanes" % (ci, len(lanes)))
    for isl in P.islands:
        if isl.kind == 0:
            if stack_hits(isl.cubes):
                bad.append("col %d bar %d %s: a note lands on another cube's tile" % (isl.col, isl.bar, isl.lane))
            for cb in isl.cubes:
                if not cb.sphere and cb.layer == 0 and not walkable(cb):
                    bad.append("col %d bar %d %s: cube %s leaps" % (isl.col, isl.bar, isl.lane, cb.label))
    played = decode(P)
    starts, total = col_timeline(P)
    got = set((e[0], e[2]) for e in played if e[4] == "drums")
    dn = [n for n in P.tracks[P.cfg["drums"]["track"]] if n.t < total]
    if sum(1 for n in dn if (n.t, n.p) in got) != len(dn):
        bad.append("drums: %d of %d hits exact" % (sum(1 for n in dn if (n.t, n.p) in got), len(dn)))
    return bad


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        sys.exit(2)
    cfg = load_config(args[0])
    n = None
    if "--sections" in args:
        n = int(args[args.index("--sections") + 1])
    P = plan_song3(cfg, n) if cfg.get("round", 2) >= 3 else plan_song(cfg, n)
    print(describe(P))
    print(numbers(P))
    if "--out" in args:
        st = to_state(P)
        path = args[args.index("--out") + 1]
        with open(path, "w") as f:
            json.dump(st, f, separators=(",", ":"))
        print("wrote", path, len(st["measures"]), "islands", len(st["cubes"]), "cubes")


if __name__ == "__main__":
    main()
