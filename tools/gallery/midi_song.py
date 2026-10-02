#!/usr/bin/env python3
"""A MIDI file as a DeaCube gallery song (python3 stdlib only): the importer behind the gallery's "get proto" (the user's own MIDI).

usage:
  python3 midi_song.py <import id> [--table] [--notes]
      builds imports/<id>.py's song and prints the plan and the faithfulness numbers: the notes the song's JSON plays (decoded through
      engine.py's mirror of DeaCube's rules) against the MIDI's. build_gallery.py writes the JSON and the index entry (and --verify checks them).

The representation (docs/deacube-gallery.md, "get proto"):
  * one COLUMN per four-bar SECTION (an intro bar is a section of its own), the sections explicit, with their own letters and roles;
  * every pitched voice on KEYBOARDS (kind 2): one keyboard per voice per section, in a LANE of its own (a fixed z; lanes ordered low to high);
    a keyboard's keys span the voice's range (13 keys at least), its register chosen so every note plays as it is (VoiceRules.FoldMelody);
  * a voice's simultaneous notes go to several cubes: every KEY belongs to ONE cube of its keyboard (keys coloured so that no two notes of a cube
    overlap), so no cube lands on another's key and the stack rule (the top cube of a stack plays an octave up) never lifts a note;
  * octave doublings (the e.piano's octaves, the strings' doubled triads) play on OCTAVE LAYER cubes (layer +1 / -1, copies riding above / below
    the keyboard); a voice flickering between octaves (the square's 32nds) plays the lift sticker (+12) on its lower key;
  * every note is a node of its own length, the gaps rest nodes, gate long (a note sounds to the next node, -10 ms); velocities come from the
    cube's volume plus ghost / accent stickers, fitted to the MIDI's velocities (VoiceRules rule 7);
  * the drums on MOONS with their own kit (v9: MeasureState.kit), one Moon per phrase playing the whole phrase once (v9: a Moon's bars = its
    cubes' window), one cube per piece; a hit's loudness picks its Moon column (rule 1: 0.55 + 0.09 x), the kick row always plays 100.
"""
import importlib.util
import json
import os
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import midi as M        # noqa: E402
import engine as E      # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "AudioCube_Unity", "Assets", "Resources", "Gallery")
SPACING, TILE, PAD = 1.12, 1.0, 0.85
ISLAND_WIDTH = 5 * SPACING + TILE + 2 * PAD          # KeyBlock.IslandWidth (one measure)
EDGE_INSET = TILE * 0.5 + PAD
SECTION_GAP = 3.7                                    # ProjectConfig.SectionGap
SECTION_BARS = 4                                     # ProjectConfig.SectionBars
MOON_Z = -10.5
NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
E_SEMIS = {"maj7": [0, 4, 7, 11], "maj9": [0, 4, 7, 11, 14], "m7": [0, 3, 7, 10], "m9": [0, 3, 7, 10, 14], "7": [0, 4, 7, 10], "9": [0, 4, 7, 10, 14],
           "sus4": [0, 5, 7, 10]}   # MusicTheory.Semitones


def name_of(m):
    return NAMES[m % 12] + str(m // 12 - 1)


class BuildError(Exception):
    pass


class Note:
    """One MIDI note in DeaCube ticks (24 a beat) after the shift: t start, d length, p pitch, v velocity."""
    __slots__ = ("t", "d", "p", "v", "track", "nid")

    def __init__(self, t, d, p, v, track, nid):
        self.t, self.d, self.p, self.v, self.track, self.nid = t, d, p, v, track, nid

    @property
    def end(self):
        return self.t + self.d

    def __repr__(self):
        return "N(%d+%d %s v%d)" % (self.t, self.d, name_of(self.p), self.v)


def load_config(name):
    path = os.path.join(HERE, "imports", name.replace("-", "_") + ".py")
    spec = importlib.util.spec_from_file_location("import_" + name.replace("-", "_"), path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.CONFIG


def read_notes(cfg):
    """The MIDI's notes per track, shifted by cfg['shift'] MIDI ticks and quantised to DeaCube ticks (40 MIDI ticks a tick at 960 per quarter;
    every onset of the file lands on that grid, the sax's triplet 8ths a MIDI tick off it). Returns (song, tracks, notes off the grid)."""
    song = M.read(os.path.join(REPO, cfg["midi"]))
    per = song.division / float(E.TPB)
    shift = cfg.get("shift", 0)
    tracks, nid, offgrid = [], 0, 0
    for ti, tr in enumerate(song.tracks):
        out = []
        for n in tr.notes:
            a, b = (n.start - shift) / per, (n.end - shift) / per
            t, e = int(round(a)), int(round(b))
            if abs(a - t) > 0.05 or abs(b - e) > 0.1:
                offgrid += 1
            out.append(Note(t, max(1, e - t), n.pitch, n.vel, ti, nid))
            nid += 1
        out.sort(key=lambda x: (x.t, x.p))
        tracks.append(out)
    return song, tracks, offgrid


# ================================================================== keyboards
def overlaps(a, b):
    return a.t < b.end and b.t < a.end


def colour_keys(items):
    """items: [(note, key)]. Colours the keys so that no two notes of one colour overlap (DSatur); {key: colour}, colour 0 = the lowest line."""
    keys = sorted(set(k for _, k in items))
    by = {k: [n for n, kk in items if kk == k] for k in keys}
    adj = {k: set() for k in keys}
    for i, a in enumerate(keys):
        for b in keys[i + 1:]:
            if any(overlaps(x, y) for x in by[a] for y in by[b]):
                adj[a].add(b)
                adj[b].add(a)
    colour = {}
    while len(colour) < len(keys):
        pick = max((k for k in keys if k not in colour),
                   key=lambda k: (len(set(colour[n] for n in adj[k] if n in colour)), len(adj[k]), -k))
        used = set(colour[n] for n in adj[pick] if n in colour)
        c = 0
        while c in used:
            c += 1
        colour[pick] = c
    n = max(colour.values()) + 1 if colour else 0
    mean = {c: sum(k for k in keys if colour[k] == c) / float(sum(1 for k in keys if colour[k] == c)) for c in range(n)}
    order = sorted(range(n), key=lambda c: mean[c])
    remap = {c: i for i, c in enumerate(order)}
    return {k: remap[c] for k, c in colour.items()}


def split_octaves(notes, layer):
    """Octave doublings: a note sounding with another exactly 12 below it (layer +1: it is the upper copy) or above (layer -1: the lower copy),
    same start and length, rides an OCTAVE LAYER cube. Returns (base notes, [(layer note, its partner)])."""
    base, lay = [], []
    by_time = {}
    for n in notes:
        by_time.setdefault((n.t, n.d), []).append(n)
    for key in sorted(by_time):
        grp = by_time[key]
        ps = {n.p: n for n in grp}
        taken = set()
        for n in sorted(grp, key=lambda x: x.p):
            partner = ps.get(n.p - 12) if layer > 0 else ps.get(n.p + 12)
            if partner is not None and partner.nid not in taken and n.nid not in taken:
                lay.append((n, partner))
                taken.add(n.nid)
        base.extend(n for n in grp if n.nid not in taken)
    base.sort(key=lambda x: (x.t, x.p))
    return base, lay


def flicker_lifts(notes):
    """The square's octave flicker: a note 3 ticks (a 32nd) from a note exactly 12 below it is that lower key with the lift sticker."""
    pos = {}
    for n in notes:
        pos.setdefault(n.t, []).append(n)
    return {n.nid: any(m.p == n.p - 12 for dt in (-3, 3) for m in pos.get(n.t + dt, [])) for n in notes}


def choose_register(voice, wants, prefer):
    """The first register of `prefer` under which every (sound, layer, lift) of `wants` has a key that plays it as it is; else the one that holds
    the most. Returns (reg, the wants it cannot hold)."""
    best, best_n = prefer[0], -1
    for reg in prefer:
        ok = sum(1 for s, layer, lift in wants if E.key_for(voice, s, reg, layer, lift) is not None)
        if ok == len(wants):
            return reg, []
        if ok > best_n:
            best, best_n = reg, ok
    return best, [w for w in wants if E.key_for(voice, w[0], best, w[1], w[2]) is None]


def tile_for(voice, s, reg, layer, lift):
    """A note's key (tile pitch); one the register cannot hold as it is takes the key whose fold sounds nearest. (tile, folded?)"""
    k = E.key_for(voice, s, reg, layer, lift)
    if k is not None:
        return k, False
    base = s - 12 * layer - (12 if lift else 0) - 12 * voice.octave_shift
    # the fold DeaCube itself would make: the same pitch class an octave (or two) away, the nearest one
    def unfolded(t):   # the key plays as it is (no octave fold of its own): it lies in the keyboard's usual range
        return E.key_sound(voice, t, reg, layer, lift) == t + 12 * voice.octave_shift + (12 if lift else 0) + 12 * layer
    best = min(range(base - 24, base + 25),
               key=lambda t: ((E.key_sound(voice, t, reg, layer, lift) - s) % 12 != 0, abs(E.key_sound(voice, t, reg, layer, lift) - s),
                              not unfolded(t), abs(t - base)))
    return best, True


def fit_velocities(voice, items, sticker_cost=1.5):
    """items: [(target velocity, local tick, fixed mod or None)]. The cube volume (0.01..1) and per-note sticker (none / ghost / accent; a fixed
    mod stays) that bring the played velocities nearest the MIDI's (VoiceRules rule 7, humanise left out). Returns (volume, mods, mean error)."""
    best = None
    for vi in range(1, 101):
        vol = vi / 100.0
        cost, err, mods = 0.0, 0.0, []
        for tv, lt, fixed in items:
            if fixed is not None:
                e = abs(E.pitched_velocity(voice, vol, fixed if fixed in (2, 3) else 0, lt) - tv)
                cost += e
                err += e
                mods.append(fixed)
                continue
            c = min((abs(E.pitched_velocity(voice, vol, m, lt) - tv) + (sticker_cost if m else 0.0), m) for m in (0, 2, 3))
            cost += c[0]
            err += abs(E.pitched_velocity(voice, vol, c[1], lt) - tv)
            mods.append(c[1])
        if best is None or cost < best[0] - 1e-9:
            best = (cost, vol, mods, err / max(1, len(items)))
    return best[1], best[2], best[3]


def tile_window(nodes, gap, key):
    """Rest nodes filling `gap` ticks (each 3..192) on `key`."""
    while gap > 0:
        take = min(gap, E.MAX_TICKS)
        if 0 < gap - take < E.MIN_TICKS:
            take = gap - E.MIN_TICKS
        nodes.append([key, take, 1, None])
        gap -= take


def stretch_for_normal_gate(nodes):
    """Bells play gate long as normal (VoiceRules.GateSeconds): a note sounds 0.92 x its node. A note of 13+ ticks followed by a rest takes
    round(len / 0.92) ticks from it (the rest keeps 3 at least, or vanishes when exactly used up), so it sounds its own length."""
    out = []
    i = 0
    while i < len(nodes):
        nd = nodes[i]
        if nd[3] is not None and nd[1] >= 13 and i + 1 < len(nodes) and nodes[i + 1][3] is None:
            want = int(round(nd[1] / 0.92))
            extra = want - nd[1]
            rest = nodes[i + 1][1]
            if extra > 0 and (rest - extra >= E.MIN_TICKS or rest == extra):
                nd = [nd[0], want, nd[2], nd[3]]
                out.append(nd)
                if rest > extra:
                    out.append([nodes[i + 1][0], rest - extra, nodes[i + 1][2], None])
                i += 2
                continue
        out.append(nd)
        i += 1
    return out


def path_nodes(seq, t0, t1):
    """A cube's path over its window [t0, t1): seq = [(note, key, mod)] in time order without overlaps (local times). A note keeps its length (cut
    at the next note or the window end); a gap becomes rest nodes on the key the cube just played (before its first note: that note's key); a gap
    shorter than a node (< 3 ticks) is held by the note before. Returns ([key, dur, mod, note or None], [(note, length change)])."""
    nodes, cuts = [], []
    cur = t0
    for i, (n, key, mod) in enumerate(seq):
        gap = n.t - cur
        if gap > 0:
            if gap < E.MIN_TICKS and nodes:
                nodes[-1][1] += gap
                prev = nodes[-1][3]
                if prev is not None:
                    cuts.append((prev, gap))
            else:
                tile_window(nodes, gap, nodes[-1][0] if nodes else key)
        nxt = seq[i + 1][0].t if i + 1 < len(seq) else t1
        dur = min(n.d, nxt - n.t, t1 - n.t)
        if dur < E.MIN_TICKS:
            raise BuildError("a note of %d ticks at %d cannot be a node" % (dur, n.t))
        if dur != n.d:
            cuts.append((n, dur - n.d))
        if dur > E.MAX_TICKS:
            raise BuildError("a note longer than two bars at %d" % n.t)
        nodes.append([key, dur, mod, n])
        cur = n.t + dur
    tile_window(nodes, t1 - cur, nodes[-1][0] if nodes else 0)
    if sum(x[1] for x in nodes) != t1 - t0:
        raise BuildError("a path does not tile its window: %d vs %d" % (sum(x[1] for x in nodes), t1 - t0))
    return nodes, cuts


class Island:
    """One keyboard (or Moon) of the plan, with its cubes."""
    def __init__(self, **kw):
        self.__dict__.update(kw)


def plan_keyboard(cfg, vname, vc, voice, notes, col, t0, t1, deviations):
    """The keyboard of voice `vname` in the column [t0, t1): its register, keys and cubes (see the module docstring)."""
    layer = vc.get("octave_layer", 0)
    base, lay = split_octaves(notes, layer) if layer else (list(notes), [])
    lifts = flicker_lifts(base) if vc.get("flicker") else {}
    wants = [(n.p, 0, lifts.get(n.nid, False)) for n in base] + [(n.p, layer, False) for n, _ in lay]
    bar0 = column_spec(cfg)[col][0]
    prefer = vc.get("bar_registers", {}).get(bar0) or vc.get("registers", [0, -1, 1, -2, 2])
    reg, cannot = choose_register(voice, wants, prefer)
    tiles = {}
    for n in base:
        tl, folded = tile_for(voice, n.p, reg, 0, lifts.get(n.nid, False))
        tiles[n.nid] = tl
        if folded:
            deviations.append(("fold", vname, n, "%s plays %s (outside the register window)" % (name_of(n.p), name_of(E.key_sound(voice, tl, reg, 0, lifts.get(n.nid, False))))))
    for n, _ in lay:
        tl, folded = tile_for(voice, n.p, reg, layer, False)
        tiles[n.nid] = tl
        if folded:
            deviations.append(("fold", vname, n, "layer note %s folds" % name_of(n.p)))
    lo, hi = min(tiles.values()), max(tiles.values())
    span = vc.get("_span", {}).get(reg)
    if span:
        lo, hi = min(lo, span[0]), max(hi, span[1])
    count = max(13, hi - lo + 1)
    if count > 61:
        raise BuildError("%s col %d: %d keys" % (vname, col, count))
    lo -= (count - (hi - lo + 1)) // 2
    root = lo - 12 * reg
    while root < 21:
        root += 12
        lo += 12
    while root + count - 1 > 108:
        root -= 12
        lo -= 12
    # base cubes: every key one cube's; layer cubes follow their partners' cubes
    colour = colour_keys([(n, tiles[n.nid]) for n in base]) if base else {}
    ncubes = max(colour.values()) + 1 if colour else 0
    lines = [[] for _ in range(ncubes)]
    for n in base:
        lines[colour[tiles[n.nid]]].append(n)
    lay_lines = {}
    for n, partner in lay:
        c = colour.get(tiles[partner.nid], 0)
        lay_lines.setdefault(c, []).append(n)
    cubes = []
    for ci, line in enumerate(lines):
        cubes.append(dict(notes=sorted(line, key=lambda x: x.t), layer=0, line=ci, lifts=lifts))
    for ci in sorted(lay_lines):
        ln = sorted(lay_lines[ci], key=lambda x: x.t)
        for a, b in zip(ln, ln[1:]):
            if overlaps(a, b):
                raise BuildError("%s col %d: layer line %d overlaps itself" % (vname, col, ci))
        cubes.append(dict(notes=ln, layer=layer, line=ci, lifts={}))
    out = []
    for cb in cubes:
        seq = [(n, tiles[n.nid] - lo, 8 if cb["lifts"].get(n.nid) else None) for n in cb["notes"]]
        nodes, cuts = path_nodes([(n, k, m) for n, k, m in seq], t0, t1)
        if voice.group == E.GROUP_BELLS:
            nodes = stretch_for_normal_gate(nodes)
        for n, dd in cuts:
            if n is not None:
                deviations.append(("length", vname, n, "%+d ticks" % dd))
        items = [(nd[3].v, sum(x[1] for x in nodes[:i]), nd[2]) for i, nd in enumerate(nodes) if nd[3] is not None]
        vol, mods, verr = fit_velocities(voice, items, vc.get("sticker_cost", 1.5))
        mi = iter(mods)
        for nd in nodes:
            if nd[3] is not None:
                m = next(mi)
                nd[2] = m if m is not None else 0
        out.append(dict(layer=cb["layer"], line=cb["line"], nodes=nodes, volume=vol, verr=verr))
    return Island(kind=2, voice=voice, vname=vname, vc=vc, col=col, reg=reg, root=root, keys=count, lo=lo, t0=t0, t1=t1, cubes=out,
                  lane=vc["lane"], cannot=cannot, tile_lo=min(tiles.values()), tile_hi=max(tiles.values()))


# ================================================================== chord grids (an option: a voice whose every bar fits one DeaCube chord)
def grid_for_bar(D, voice_slot, notes, root_pc, q):
    """The root (MIDI, KeyBlock's clamp) of a one-bar chord grid of quality q on which every note of the bar has a tile that sounds it (the
    bass anchor on the downbeat included: VoiceRules rule 4); {pitch: [(x, z) ...]} per note kind; None when no root holds them all."""
    for root in sorted((r for r in range(36, 85) if r % 12 == root_pc), key=lambda r: abs(r - 45)):
        g = D.grid(root, q, 0)
        cand = {}
        ok = True
        for n in notes:
            down = n.t == 0
            key = (n.p, down)
            if key in cand:
                continue
            tiles = [(x, z) for x in range(len(g)) for z in range(len(g[0])) if D.sounding(voice_slot, g, x, z, root, 0, 0, False, down) == n.p]
            if not tiles:
                ok = False
                break
            cand[key] = tiles
        if ok:
            return root, g, cand
    return None


def ladder(tiles, prev, k):
    """k tiles walking the candidates of one pitch as a ladder: from the end nearest the previous tile, back and forth (one tile: it stays)."""
    t = sorted(tiles)
    if len(t) == 1:
        return [t[0]] * k
    if prev is not None and abs(t[-1][0] - prev[0]) + abs(t[-1][1] - prev[1]) < abs(t[0][0] - prev[0]) + abs(t[0][1] - prev[1]):
        t = t[::-1]
    walk = list(range(len(t))) + list(range(len(t) - 2, 0, -1))
    out = [t[walk[i % len(walk)]] for i in range(k)]
    if prev is not None and out[0] == prev and len(t) > 1:
        out = [t[walk[(i + 1) % len(walk)]] for i in range(k)]
    return out


def plan_grids(cfg, vname, vc, voice, notes, col, bar0, nb, deviations):
    """One chord grid per bar of the column (barOffset = the bar), the bar's chord from vc['grid_chords'] (cycling from vc['grid_from']): every
    repeated note walks its pitch's tiles as a ladder (the shape the brief asks for: the same pitch lives on several tiles). None when a bar's notes
    do not fit its grid (the column then takes a keyboard)."""
    import deacube as D
    slot = D.INSTRUMENT["bass"] if voice.group == E.GROUP_BASS else None
    if slot is None:
        return None
    out = []
    for b in range(nb):
        bt0 = b * E.BAR
        mine = [Note(n.t - bt0, n.d, n.p, n.v, n.track, n.nid) for n in notes if bt0 <= n.t < bt0 + E.BAR]
        if not mine:
            continue
        name = vc["grid_chords"][(bar0 + b - vc.get("grid_from", 0)) % len(vc["grid_chords"])]
        pc, q = D.parse_chord(name)
        found = grid_for_bar(D, slot, mine, pc, q)
        if found is None:
            return None
        root, g, cand = found
        seq, prev, i = [], None, 0
        mine.sort(key=lambda x: x.t)
        while i < len(mine):
            j = i
            while j < len(mine) and mine[j].p == mine[i].p:
                j += 1
            run = mine[i:j]
            # the downbeat note may need an anchor-safe tile: pick per note from its own candidates
            tiles = ladder(cand[(run[0].p, False)] if (run[0].p, False) in cand else cand[(run[0].p, True)], prev, len(run))
            for k, n in enumerate(run):
                tl = tiles[k]
                if n.t == 0 and tl not in cand[(n.p, True)]:
                    tl = cand[(n.p, True)][0]
                seq.append((n, tl, None))
                prev = tl
            i = j
        nodes, cuts = path_nodes(seq, 0, E.BAR)
        for n, dd in cuts:
            if n is not None:
                deviations.append(("length", vname, n, "%+d ticks" % dd))
        items = [(nd[3].v, sum(x[1] for x in nodes[:k]), None) for k, nd in enumerate(nodes) if nd[3] is not None]
        vol, mods, verr = fit_velocities(voice, items, vc.get("sticker_cost", 1.5))
        mi = iter(mods)
        for nd in nodes:
            if nd[3] is not None:
                nd[2] = next(mi)
        out.append(Island(kind=0, voice=voice, vname=vname, vc=vc, col=col, bar=b, chord=name, root=root, q=q, reg=0, t0=bt0, t1=bt0 + E.BAR,
                          cubes=[dict(layer=0, line=0, nodes=nodes, volume=vol, verr=verr)], lane=vc["lane"], keys=0, lo=0))
    return out


# ================================================================== Moons
def plan_moon(cfg, dc, voice, notes, mi, col, t0, t1, deviations):
    """A Moon playing [t0, t1) once (its bars = the window): one cube per piece; a hit's Moon column follows its loudness (the kick row: a walk)."""
    kit = dc["kit"]
    rows = E.MOON_ROWS
    place = {}
    for z in range(rows):
        place[kit[z]] = (z, False)
        if rows + z < len(kit) and kit[rows + z] > 0:
            place[kit[rows + z]] = (z, True)
    by_piece = {}
    for n in notes:
        if n.p not in place:
            deviations.append(("kit", "drums", n, "piece %d is not in the kit" % n.p))
            continue
        by_piece.setdefault(n.p, []).append(n)
    cubes = []
    for piece in sorted(by_piece, key=lambda p: (place[p][0], place[p][1])):
        hits = sorted(by_piece[piece], key=lambda x: x.t)
        z, alt = place[piece]
        # loudness: the cube's volume and each hit's column (x 0..4; an alternate piece is column 5) + ghost / accent
        best = None
        for vi in range(1, 101):
            vol = vi / 100.0
            cost, plan = 0.0, []
            prev = None
            for n in hits:
                lt = n.t - t0
                xs = [5] if alt else range(5)
                cands = []
                for x in xs:
                    for m in ((0, 3) if z == 0 else (0, 2, 3)):
                        pv = E.drum_velocity(voice, vol, m, lt, x, z)
                        c = abs(pv - n.v) + (1.5 if m else 0.0) + (2.0 if (x, z) == prev and not alt else 0.0)
                        cands.append((c, x, m, pv))
                c = min(cands)
                if z == 0 and not alt:
                    c = (c[0], None, c[2], c[3])   # the kick's column is free (its velocity is the row's): a walk, below
                cost += c[0]
                plan.append(c)
                prev = (c[1], z)
            if best is None or cost < best[0] - 1e-9:
                best = (cost, vol, plan)
        vol, plan = best[1], best[2]
        walk = [0, 1, 2, 3, 4, 3, 2, 1]
        seq = []
        for i, (n, c) in enumerate(zip(hits, plan)):
            x = c[1] if c[1] is not None else walk[i % len(walk)]
            seq.append((n, x, c[2]))
        nodes = []
        cur = t0
        for i, (n, x, m) in enumerate(seq):
            if n.t > cur:
                tile_window(nodes, n.t - cur, (x, z))
            nxt = seq[i + 1][0].t if i + 1 < len(seq) else t1
            dur = nxt - n.t
            first = min(dur, E.MAX_TICKS)
            if 0 < dur - first < E.MIN_TICKS:
                first = dur - E.MIN_TICKS
            nodes.append([(x, z), first, m, n])
            tile_window(nodes, dur - first, (x, z))
            cur = nxt
        if sum(nd[1] for nd in nodes) != t1 - t0:
            raise BuildError("moon %d piece %d: path %d vs window %d" % (mi, piece, sum(nd[1] for nd in nodes), t1 - t0))
        verr = sum(abs(c[3] - n.v) for n, c in zip(hits, plan)) / float(len(hits))
        cubes.append(dict(piece=piece, nodes=nodes, volume=vol, verr=verr, layer=0, line=0))
    return Island(kind=1, voice=voice, vname="drums", col=col, t0=t0, t1=t1, bars=(t1 - t0) // E.BAR, cubes=cubes, kit=list(kit), moon=mi)


# ================================================================== the song
def column_spec(cfg):
    """[(first bar, bars a pass, passes)]: a column's third entry is its passes (a repeat belt: the column plays its pass that often)."""
    return [(c[0], c[1], c[2] if len(c) > 2 else 1) for c in cfg["columns"]]


def split_passes(notes, length, passes, where):
    """A repeated column's notes, pass by pass (local times): the island plays pass 0's notes `repeat` times — the passes from 0 on that hold
    the same notes; the rest must be silent (an island rests in its column's later passes, SongManager's windows). Returns (pass-0 notes, repeat,
    {later note id: its pass-0 twin's id})."""
    per = [[] for _ in range(passes)]
    for n in notes:
        per[min(passes - 1, n.t // length)].append(n)
    key = lambda n: (n.t % length, n.d, n.p, n.v)
    first = sorted(per[0], key=key)
    rep, twins = 1, {}
    for k in range(1, passes):
        if not per[k]:
            break
        mine = sorted(per[k], key=key)
        if [key(x) for x in mine] != [key(x) for x in first]:
            raise BuildError("%s: pass %d differs from pass 0 (a repeat would change the notes)" % (where, k))
        for a, b in zip(mine, first):
            twins[a.nid] = b.nid
        rep += 1
    for k in range(rep, passes):
        if per[k]:
            raise BuildError("%s: pass %d plays after a silent pass" % (where, k))
    return [Note(n.t, n.d, n.p, n.v, n.track, n.nid) for n in per[0]], rep, twins


def plan_song(cfg):
    voices = E.load_voices()
    song, tracks, offgrid = read_notes(cfg)
    deviations = []
    cols = column_spec(cfg)
    keyboards, moons, twins = [], [], {}
    for vname, vc in cfg["voices"].items():
        voice = voices[(vc["group"], vc["caption"])]
        notes = tracks[vc["track"]]
        per_col = []
        for ci, (bar0, nb, passes) in enumerate(cols):
            t0, t1 = bar0 * E.BAR, (bar0 + nb * passes) * E.BAR
            mine = [Note(n.t - t0, n.d, n.p, n.v, n.track, n.nid) for n in notes if t0 <= n.t < t1]
            if not mine:
                per_col.append(None)
                continue
            first, rep, tw = split_passes(mine, nb * E.BAR, passes, "%s col %d" % (vname, ci)) if passes > 1 else (mine, 1, {})
            twins.update(tw)
            per_col.append((first, rep))
        # pass 1: the voice's keys per register over the whole song (every keyboard of the lane then has the same keys: one instrument per lane)
        vc["_span"] = {}
        for ci, (bar0, nb, passes) in enumerate(cols):
            if per_col[ci] is None:
                continue
            probe = plan_keyboard(cfg, vname, dict(vc, _span={}), voice, per_col[ci][0], ci, 0, nb * E.BAR, [])
            a, b = vc["_span"].get(probe.reg, (999, -999))
            vc["_span"][probe.reg] = (min(a, probe.tile_lo), max(b, probe.tile_hi))
        for ci, (bar0, nb, passes) in enumerate(cols):
            if per_col[ci] is None:
                continue
            local, rep = per_col[ci]
            t0 = bar0 * E.BAR
            if vc.get("grid_chords"):
                grids = plan_grids(cfg, vname, vc, voice, local, ci, bar0, nb, deviations)
                if grids:
                    for gi in grids:
                        gi.song_t0 = t0
                        gi.repeat = rep
                    keyboards.extend(grids)
                    continue
            isl = plan_keyboard(cfg, vname, vc, voice, local, ci, 0, nb * E.BAR, deviations)
            isl.song_t0 = t0
            isl.repeat = rep
            keyboards.append(isl)
    dc = cfg["drums"]
    dvoice = voices[(9, dc["caption"])]
    dnotes = tracks[dc["track"]]
    starts = [c[0] for c in cols]
    for mi, (b0, b1) in enumerate(dc["moons"]):
        if b0 not in starts:
            raise BuildError("moon %d: bar %d does not start a column" % (mi, b0))
        c0 = starts.index(b0)
        t0, t1 = b0 * E.BAR, b1 * E.BAR
        mine = [Note(n.t - t0, n.d, n.p, n.v, n.track, n.nid) for n in dnotes if t0 <= n.t < t1]
        isl = plan_moon(cfg, dc, dvoice, mine, mi, c0, 0, t1 - t0, deviations)
        isl.song_t0 = t0
        moons.append(isl)
    # every MIDI note placed (a repeated column's later passes through their pass-0 twins)
    covered = set()
    for isl in keyboards + moons:
        for cb in isl.cubes:
            for nd in cb["nodes"]:
                if nd[3] is not None:
                    covered.add(nd[3].nid)
    for a, b in twins.items():
        if b in covered:
            covered.add(a)
    missing = [n for tr in tracks for n in tr if n.nid not in covered]
    for n in missing:
        deviations.append(("missing", "track %d" % n.track, n, "not placed"))
    return dict(cfg=cfg, voices=voices, song=song, tracks=tracks, offgrid=offgrid, keyboards=keyboards, moons=moons, deviations=deviations)


def lane_z(cfg, lane):
    """A lane's z (the root z of its islands): cfg['lanes'][lane] when given (a grid lane is deeper than a keyboard), else lane x lane_pitch."""
    if cfg.get("lanes"):
        return float(cfg["lanes"][lane])
    return cfg["lane_pitch"] * lane


def to_state(plan):
    """SongState JSON (the current schema: version 6, keyboards kind 2 with keyCount, Moons with bars and kit, sections and their letters)."""
    cfg = plan["cfg"]
    cols = column_spec(cfg)
    measures, cubes_out = [], []
    # columns' x: a column is its measures wide (+ its belt: a slot per later pass); every column its own section (SectionGap + the ghost
    # measures of a short one between them). SongManager.LayoutX lays them out again on load (from the first island's x).
    xs, x = [], EDGE_INSET
    for ci, (bar0, nb, passes) in enumerate(cols):
        xs.append(x)
        x += nb * ISLAND_WIDTH + (passes - 1) * (nb * ISLAND_WIDTH + 0.6) + SECTION_GAP + max(0, SECTION_BARS - nb) * ISLAND_WIDTH
    order = sorted(plan["keyboards"], key=lambda k: (k.col, k.lane, getattr(k, "bar", 0)))
    first_of_col = {}
    for isl in order:
        mi = len(measures)
        anchor = isl.col not in first_of_col
        if anchor:
            first_of_col[isl.col] = mi
        nb = cols[isl.col][1]
        grid = isl.kind == 0
        measures.append({
            "chordKey": isl.chord if grid else "Keys", "root": isl.root, "semis": E_SEMIS[isl.q] if grid else [0], "bars": 1 if grid else nb,
            "barOffset": isl.bar if grid else 0,
            "px": xs[isl.col] + (isl.bar * ISLAND_WIDTH if grid else 0.0), "pz": lane_z(cfg, isl.lane), "placed": True, "kind": 0 if grid else 2,
            "mood": 0, "energy": 2, "fill": False, "sleep": False,
            "repeat": getattr(isl, "repeat", 1), "group": 0, "fall": 0, "col": isl.col, "reg": isl.reg, "carry": 0,
            "stairType": 0, "stairDir": -1, "stairSteps": 4, "stairRate": 12, "stairLead": True, "carryStyle": 0, "rewind": False, "vary": 0,
            "launch": False, "phraseOffset": 0, "phraseBeats": 4, "phraseGrid": 12, "keyCount": 0 if grid else isl.keys,
            "secRole": cfg["roles"][isl.col] if anchor else 0, "kit": [],
        })
        base_ids = {}
        for cb in isl.cubes:
            cid = len(cubes_out) + 1
            if cb["layer"] == 0:
                base_ids[cb["line"]] = cid
            cubes_out.append(cube_json(cfg, isl, cb, mi, -1, cid, base_ids.get(cb["line"], -1) if cb["layer"] else -1))
    moons = []
    for isl in plan["moons"]:
        moons.append({
            "chordKey": "Moon", "root": 36, "semis": [0], "bars": isl.bars, "barOffset": 0,
            "px": xs[isl.col] + 2 * ISLAND_WIDTH - EDGE_INSET - (E.MOON_COLS - 1) * SPACING * 0.5, "pz": MOON_Z - (E.MOON_ROWS - 1) * SPACING * 0.5,
            "placed": True, "kind": 1, "mood": 0, "energy": 2, "fill": False, "sleep": False, "repeat": 1, "group": 0, "fall": 0,
            "col": isl.col, "reg": 0, "carry": 0, "stairType": 0, "stairDir": -1, "stairSteps": 4, "stairRate": 12, "stairLead": True,
            "carryStyle": 0, "rewind": False, "vary": 0, "launch": False, "phraseOffset": 0, "phraseBeats": 4, "phraseGrid": 12, "keyCount": 0,
            "secRole": 0, "kit": isl.kit,
        })
        for cb in isl.cubes:
            cubes_out.append(cube_json(cfg, isl, cb, 0, isl.moon, len(cubes_out) + 1, -1))
    for i, c in enumerate(cubes_out):
        h = 2166136261
        for ch in (cfg["id"] + ":" + str(i)):
            h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
        c["seed"] = (h & 0x7FFFFFFF) or 1
    ngroups = cfg.get("groups", 11)
    vol = [1.0] * ngroups
    for g, v in cfg.get("mix", {}).items():
        vol[g] = float(v)
    state = {
        "name": cfg["title"], "bpm": float(cfg["bpm"]), "beatsPerBar": 4, "swing": 0.0, "loop": bool(cfg.get("loop", False)), "transpose": 0,
        "measures": measures, "cubes": cubes_out, "instVolume": vol, "instMuted": [False] * ngroups, "version": 6, "moons": moons,
        "climate": 0, "tone": float(cfg.get("tone", 1.0)), "space": float(cfg.get("space", 1.0)), "demoSeed": 0, "deck": deck(cfg),
        "sections": list(range(len(cols))), "sectionLetters": list(cfg["letters"]), "keyTonic": cfg["key_pc"], "keyMinor": bool(cfg["minor"]),
    }
    return state


def deck(cfg):
    out = []
    for root, semis, name in cfg.get("deck", []):
        out.append({"chordKey": name, "root": root, "semis": semis, "bars": 1, "barOffset": 0, "px": 0.0, "pz": 0.0, "placed": False, "kind": 0,
                    "mood": 0, "energy": 2, "fill": False, "sleep": False, "repeat": 1, "group": 0, "fall": 0, "col": -1, "reg": 0, "carry": 0,
                    "stairType": 0, "stairDir": -1, "stairSteps": 4, "stairRate": 12, "stairLead": True, "carryStyle": 0, "rewind": False,
                    "vary": 0, "launch": False, "phraseOffset": 0, "phraseBeats": 4, "phraseGrid": 12, "keyCount": 0, "secRole": 0, "kit": []})
    return out


def cube_json(cfg, isl, cb, measure, moon, cid, echo_of):
    nodes = cb["nodes"]
    if isl.kind in (0, 1):
        xs = [nd[0][0] for nd in nodes]
        zs = [nd[0][1] for nd in nodes]
    else:
        xs = [nd[0] for nd in nodes]
        zs = [0] * len(nodes)
    mods = [nd[2] for nd in nodes]
    return {
        "instrument": isl.voice.group, "measure": measure, "xs": xs, "zs": zs, "rests": [m == 1 for m in mods], "step": 1,
        # gate: Moons 1 (drums are one-shots); pitched 2, long (a note sounds to the next node, -10 ms)
        "gate": 1 if isl.kind == 1 else 2, "mode": 0, "volume": round(cb["volume"], 3), "muted": False, "moon": moon, "mods": mods,
        "hits": -1, "rot": 0, "mask": 0, "octave": 0, "reverse": False, "phase": 0, "follow": 0, "echo": 0, "shimmer": False, "rider": False,
        "seed": 0, "twinOf": -1, "id": cid, "durs": [nd[1] for nd in nodes], "voice": isl.voice.voice, "layer": cb["layer"],
        "echoOf": echo_of, "climb": 0, "sphere": False,
    }


# ================================================================== decoding the JSON (what DeaCube plays) and the faithfulness numbers
def decode(state, voices):
    """Every note the song's JSON plays, through engine.py's mirror: [(song tick, ticks sounding, pitch, velocity, (group, voice), cube index)].
    Keyboards: a window per column (its bars), the path from its start, FoldMelody + layer + lift; Moons: a window per `bars` bars of the Moon's
    section (its start column to the next later start), the kit's pieces; gate long = to the next node (-10 ms, here exact)."""
    by_slot = {(v.group, v.voice): v for v in voices.values()}
    ms = state["measures"]
    col_bars, col_passes = {}, {}
    for m in ms:
        col_bars[m["col"]] = max(col_bars.get(m["col"], 1), m.get("barOffset", 0) + m["bars"] if m["kind"] == 0 else m["bars"])
        col_passes[m["col"]] = max(col_passes.get(m["col"], 1), m["repeat"])
    ncol = max(col_bars) + 1
    col_start = [0] * (ncol + 1)
    for c in range(ncol):
        col_start[c + 1] = col_start[c] + col_bars[c] * E.BAR * col_passes[c]
    total = col_start[ncol]
    moon_starts = sorted(set(max(0, m["col"]) for m in state["moons"]))
    out = []
    for ci, c in enumerate(state["cubes"]):
        voice = by_slot[(c["instrument"], c["voice"])]
        n = len(c["xs"])
        if c["moon"] >= 0:
            mo = state["moons"][c["moon"]]
            s0 = col_start[max(0, mo["col"])]
            later = [s for s in moon_starts if s > max(0, mo["col"])]
            s1 = col_start[min(later)] if later else total
            wl = mo["bars"] * E.BAR
            w = s0
            while w < s1:
                ln = min(wl, s1 - w)
                t = 0
                for i in range(n):
                    if t >= ln:
                        break
                    if c["mods"][i] != 1:
                        piece = E.drum_piece(c["xs"][i], c["zs"][i], E.MOON_ROWS, mo["kit"])
                        vel = E.drum_velocity(voice, c["volume"], c["mods"][i], t, c["xs"][i], c["zs"][i])
                        out.append((w + t, 3, piece, vel, (voice.group, voice.voice), ci))
                    t += c["durs"][i]
                w += wl
            continue
        m = ms[c["measure"]]
        for pas in range(m["repeat"]):
            out.extend(decode_window(c, ci, m, voice, col_start[m["col"]] + pas * col_bars[m["col"]] * E.BAR + (m.get("barOffset", 0) * E.BAR if m["kind"] == 0 else 0)))
    out.sort()
    return out


def decode_window(c, ci, m, voice, t0):
    """The notes one window of a pitched cube plays (its island's pass starting at song tick t0)."""
    out = []
    n = len(c["xs"])
    if True:
        ln = m["bars"] * E.BAR
        g = None
        if m["kind"] == 0:
            import deacube as D
            q = next(k for k, v in D.QUALITIES.items() if v == m["semis"])
            groot = D.clamp_root(m["root"])
            g = D.grid(groot, q, m["reg"])
        t = 0
        for i in range(n):
            if t >= ln:
                break
            mod = c["mods"][i]
            if mod != 1:
                if g is not None:
                    import deacube as D
                    p = D.sounding(D.INSTRUMENT["bass"] if voice.group == E.GROUP_BASS else voice.group, g, c["xs"][i], c["zs"][i], groot, m["reg"],
                                   c["octave"], mod == 8, t % E.BAR == 0)
                else:
                    tile = m["root"] + c["xs"][i] + 12 * m["reg"]
                    p = E.key_sound(voice, tile, m["reg"], c["layer"], mod == 8)
                # gate long: sounds to the next node's start (a rest included), never past the window; bells: normal (0.92 x the node)
                dur = c["durs"][i] * (0.92 if voice.group == E.GROUP_BELLS else 1.0)
                if voice.group in E.SUSTAINING:
                    dur = min(dur, ln - t - 6)   # VoiceRules.ClampOff: a sustaining voice releases a 16th before its window ends
                vel = E.pitched_velocity(voice, c["volume"], mod if mod in (2, 3) else 0, t)
                out.append((t0 + t, dur, p, vel, (voice.group, voice.voice), ci))
            t += c["durs"][i]
    out.sort()
    return out


def faithfulness(plan, state):
    """The MIDI against the decoded JSON, per voice: notes with the exact pitch and onset (on the right voice), lengths within a tick, velocity
    error; and the notes DeaCube plays that the MIDI has not."""
    cfg = plan["cfg"]
    voices = plan["voices"]
    played = decode(state, voices)
    want = {}
    for vname, vc in cfg["voices"].items():
        v = voices[(vc["group"], vc["caption"])]
        for n in plan["tracks"][vc["track"]]:
            want.setdefault(vname, []).append((n, (v.group, v.voice)))
    dc = cfg["drums"]
    dv = voices[(9, dc["caption"])]
    want["drums"] = [(n, (dv.group, dv.voice)) for n in plan["tracks"][dc["track"]]]
    pool = {}
    for e in played:
        pool.setdefault((e[0], e[2], e[4]), []).append(e)
    rows = []
    tot = dict(n=0, hit=0, dur=0, verr=0.0)
    for vname, items in want.items():
        n = hit = dur_ok = 0
        verr = 0.0
        for note, slot in items:
            n += 1
            cand = pool.get((note.t, note.p, slot))
            if not cand:
                continue
            e = cand.pop(0)
            hit += 1
            if vname == "drums" or abs(e[1] - note.d) <= 1:
                dur_ok += 1
            verr += abs(e[3] - note.v)
        rows.append((vname, n, hit, dur_ok, verr / max(1, hit)))
        tot["n"] += n
        tot["hit"] += hit
        tot["dur"] += dur_ok
        tot["verr"] += verr
    extra = sum(len(v) for v in pool.values())
    return rows, tot, extra, played


def report(plan, state):
    cfg = plan["cfg"]
    rows, tot, extra, played = faithfulness(plan, state)
    nk = sum(1 for k in plan["keyboards"] if k.kind == 2)
    lines = ["%s: %d columns, %d islands (%d keyboards, %d chord grids), %d moons, %d cubes, %d path nodes; MIDI notes off the tick grid: %d" % (
        cfg["id"], len(cfg["columns"]), len(plan["keyboards"]), nk, len(plan["keyboards"]) - nk, len(plan["moons"]), len(state["cubes"]),
        sum(len(c["xs"]) for c in state["cubes"]), plan["offgrid"])]
    lines.append("  %-8s %6s %8s %7s %8s %6s" % ("voice", "notes", "exact", "%", "length", "|dv|"))
    for vname, n, hit, dur_ok, verr in rows:
        lines.append("  %-8s %6d %8d %6.1f%% %8d %6.1f" % (vname, n, hit, 100.0 * hit / max(1, n), dur_ok, verr))
    lines.append("  %-8s %6d %8d %6.2f%% %8d %6.1f   notes played that the MIDI has not: %d" % (
        "ALL", tot["n"], tot["hit"], 100.0 * tot["hit"] / max(1, tot["n"]), tot["dur"], tot["verr"] / max(1, tot["hit"]), extra))
    kinds = {}
    for d in plan["deviations"]:
        kinds.setdefault((d[0], d[1]), []).append(d)
    for (k, v), ds in sorted(kinds.items()):
        lines.append("  deviation %-7s %-8s x%-4d e.g. %s %s" % (k, v, len(ds), ds[0][2], ds[0][3]))
    return "\n".join(lines)


def table(plan, state):
    cfg = plan["cfg"]
    lines = []
    for ci, (bar0, nb, passes) in enumerate(column_spec(cfg)):
        ks = [k for k in plan["keyboards"] if k.col == ci]
        parts = []
        grids = [k for k in ks if k.kind == 0]
        if grids:
            parts.append("L%d %s grids %s" % (grids[0].lane, grids[0].vname, " ".join("b%d:%s" % (k.bar, k.chord) for k in sorted(grids, key=lambda x: x.bar))))
        for k in sorted((k for k in ks if k.kind == 2), key=lambda x: x.lane):
            ncube = len(k.cubes)
            parts.append("L%d %s %d keys %s..%s reg%+d x%d" % (k.lane, k.vname, k.keys, name_of(k.lo), name_of(k.lo + k.keys - 1), k.reg, ncube))
        moons = [m for m in plan["moons"] if m.col == ci]
        if moons:
            parts.append("MOON %d bars, %d cubes" % (moons[0].bars, len(moons[0].cubes)))
        lines.append("  col %2d bar %2d%s %s %-6s %s" % (ci, bar0, " x%d" % passes if passes > 1 else "   ", cfg["letters"][ci], ["", "intro", "verse", "chorus", "bridge", "drop", "outro"][cfg["roles"][ci]], " | ".join(parts)))
    return "\n".join(lines)


def build(name):
    cfg = load_config(name)
    plan = plan_song(cfg)
    state = to_state(plan)
    entry = {"id": cfg["id"], "title": cfg["title"], "after": cfg["after"], "vibe": cfg["vibe"], "bpm": float(cfg["bpm"]),
             "bars": sum(nb * ps for _, nb, ps in column_spec(cfg)), "file": "Gallery/" + cfg["id"], "blurb": cfg["blurb"], "hook": cfg["hook"]}
    return cfg, plan, state, entry


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        sys.exit(2)
    cfg, plan, state, entry = build(args[0])
    print(report(plan, state))
    if "--table" in args:
        print(table(plan, state))
    if "--notes" in args:
        for e in decode(state, plan["voices"])[:200]:
            print(e)


if __name__ == "__main__":
    main()
