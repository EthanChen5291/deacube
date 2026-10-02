#!/usr/bin/env python3
"""Builds the DeaCube gallery songs from the sheets (python3 stdlib only).

usage:
  python3 build_gallery.py            build every sheets/*.py -> AudioCube_Unity/Assets/Resources/Gallery/<id>.json + index.json
  python3 build_gallery.py --verify   re-read the written JSON and check it against the sheets (tiles on the grid, every note sounding
                                      the written pitch after VoiceRules' fold, lengths, lanes, tile ownership) + print the music stats
  python3 build_gallery.py --only <id> [--stats]   one song

The sheets write music as the pitches that SOUND; this finds the tile of the island that plays each one (deacube.py mirrors KeyBlock's
pitch grid and VoiceRules' fold / bass anchor), keeps every tile of an island owned by one cube (the stack rule would lift a cube that
lands on another's tile an octave), gives repeated pitches the anti-diagonal twin so the cube still moves, lays the columns / lanes out
the way SongManager.NormalizeColumns does, and writes SongState v4 JSON (the format Unity's JsonUtility reads).
"""
import sys
sys.dont_write_bytecode = True   # no __pycache__ beside the sheets
import glob
import importlib.util
import json
import os
import re
import statistics
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import deacube as D  # noqa: E402
from sheet import DUR, DRUM_ROWS  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "AudioCube_Unity", "Assets", "Resources", "Gallery")
LANES = ["ground", "melody", "double", "extra"]
LANE_Z = {"ground": 0.0, "melody": 9.0, "double": 18.0, "extra": 27.0}     # lane centres: >= LaneGap between two 5-row islands
LANE_ROOT = {"ground": 50, "melody": 57, "double": 57, "extra": 55}         # preferred root (MIDI) when several fit
BAR = 96                                                                      # ticks per 4/4 bar
MODS = {"!": 3, "?": 2, "~": 7, "^": 8}
NUDGES = {"++": 2, "+": 1, "--": -2, "-": -1}   # v9 (N): scale steps of the song key the step plays above / below its tile (DeaCube/Nudge.cs)
TOKEN = re.compile(r"^(?P<p>r|[A-G](?:#|b)?-?\d|\d[',]*|[KSHOCRP]\d?)(?::(?P<d>t\d+|16\.|16|8\.|8|4\.|4|2\.|2|1\.|1))?(?P<m>(?:[!?~^]|x2|x3|\+\+|\+|--|-)*)$")


class BuildError(Exception):
    pass


# ------------------------------------------------------------------ parsing
def parse(text, where):
    """Tokens -> events [{p: str|None, t: ticks, mod: int}] plus bar-line checks (tick positions of each |)."""
    events, bars, t, last = [], [], 0, 24
    for tok in text.replace("|", " | ").split():
        if tok == "|":
            bars.append(t)
            continue
        m = TOKEN.match(tok)
        if not m:
            raise BuildError("%s: cannot read token %r" % (where, tok))
        d = m.group("d")
        if d:
            last = int(d[1:]) if d.startswith("t") else DUR[d]
        mod, nudge = 0, 0
        for mm in re.findall(r"x2|x3|\+\+|\+|--|-|[!?~^]", m.group("m") or ""):
            if mm in NUDGES:
                nudge = NUDGES[mm]
            else:
                mod = 4 if mm == "x2" else (5 if mm == "x3" else MODS[mm])
        p = m.group("p")
        if p == "r" and nudge:
            raise BuildError("%s: a rest cannot be nudged (%r)" % (where, tok))
        events.append({"p": None if p == "r" else p, "t": last, "mod": 1 if p == "r" else mod, "nudge": nudge})
        t += last
    for b in bars:
        if b % BAR:
            raise BuildError("%s: a bar line falls at tick %d (not on a bar)" % (where, b))
    return events


def degree_pitch(tok, root_pc, q, base):
    """A chord-degree token ('5', "1'", '3,') of the island's chord -> MIDI, the root placed in [base, base + 12)."""
    digits = tok.rstrip("',")
    octs = tok.count("'") - tok.count(",")
    semis = D.QUALITIES[q]
    k = int(digits)
    want = {1: 0, 8: 12, 3: [3, 4], 4: 5, 5: [6, 7, 8], 7: [10, 11], 9: 14, 2: 14}.get(k)
    if want is None:
        raise BuildError("degree %s is not a chord tone" % tok)
    if isinstance(want, list):
        hit = [s for s in semis if s in want]
        if not hit and k == 3 and 5 in semis:
            hit = [5]                     # a sus chord's "third" is its fourth
        if not hit:
            raise BuildError("degree %s: the chord %s%s has no such tone" % (tok, D.NAMES[root_pc], q))
        iv = hit[0]
    else:
        iv = want
        if iv % 12 not in [s % 12 for s in semis]:
            raise BuildError("degree %s: the chord %s%s has no such tone" % (tok, D.NAMES[root_pc], q))
    r = base + ((root_pc - base) % 12)
    return r + iv + 12 * octs


# ------------------------------------------------------------------ tile allocation
def lay_times(events, window):
    """Start tick of every event in one window (the path loops inside it); returns [(event index, start)] until the window end."""
    out, t, cyc = [], 0, sum(e["t"] for e in events)
    if cyc <= 0:
        return out
    i = 0
    while t < window:
        out.append((i, t))
        t += events[i]["t"]
        i = (i + 1) % len(events)
    return out


def allocate(island, cubes, where, key=None):
    """Chooses a tile for every event of every cube of one island. cubes: [(Cube, events with 'pitch' ints)]. Tiles are owned by one cube;
    consecutive events land on different tiles; rests stay near the previous note. Returns [(xs, zs)] per cube. v9 (N): a nudged event
    (e["nudge"] scale steps of the song <key>) takes the tile whose NUDGED note sounds the written pitch."""
    g, root, reg = island["grid"], island["root"], island["reg"]
    cols, rows = len(g), len(g[0])
    owner = {}
    result = []
    # demand of the later cubes: tile -> count of later cubes that could need it
    cands_of = []
    for ci, (cube, evs) in enumerate(cubes):
        slot = D.INSTRUMENT[cube.inst]
        per = []
        for e in evs:
            if e["pitch"] is None:
                per.append(None)
                continue
            downs = e["downbeat"]
            nd = e.get("nudge", 0)
            c = [(x, z) for x in range(cols) for z in range(rows)
                 if D.sounding(slot, g, x, z, root, reg, cube.octave, e["mod"] == 8, downs, nd, key) == e["pitch"]
                 and (not downs or D.sounding(slot, g, x, z, root, reg, cube.octave, e["mod"] == 8, False, nd, key) == e["pitch"] or slot != 4)]
            if not c:
                have = sorted(set(D.sounding(slot, g, x, z, root, reg, cube.octave, False, False, nd, key) for x in range(cols) for z in range(rows)))
                raise BuildError("%s: %s cannot play %s on %s (root %s reg %d%s); it plays %s" % (
                    where, cube.inst, D.name_of(e["pitch"]), island["chord"], D.name_of(root), reg, (" nudge %+d" % nd) if nd else "", " ".join(D.name_of(h) for h in have)))
            per.append(c)
        cands_of.append(per)
    for ci, (cube, evs) in enumerate(cubes):
        demand = {}
        for cj in range(ci + 1, len(cubes)):
            for c in cands_of[cj]:
                if c:
                    for tile in c:
                        demand[tile] = demand.get(tile, 0) + 1.0 / len(c)
        pref_low = cube.inst == "bass"
        mine = set()
        path = [None] * len(evs)
        prev = None
        for i, e in enumerate(evs):
            c = cands_of[ci][i]
            if c is None:
                continue
            best, bs = None, None
            for tile in c:
                if owner.get(tile, ci) != ci:
                    continue
                s = 0.0
                if tile == prev:
                    s += 100.0
                if prev is not None:
                    s += 0.35 * (abs(tile[0] - prev[0]) + abs(tile[1] - prev[1]))
                if tile in mine:
                    s -= 0.6
                s += 3.0 * demand.get(tile, 0.0)
                s += (0.5 * tile[1]) if pref_low else (0.8 if tile[1] == 0 else 0.0)
                if bs is None or s < bs:
                    best, bs = tile, s
            if best is None:
                raise BuildError("%s: %s: no free tile for %s (event %d) on %s; tiles owned by other cubes: %s" % (
                    where, cube.inst, D.name_of(e["pitch"]), i, island["chord"], sorted(t for t in owner if owner[t] != ci)))
            if best == prev and cube.inst != "bass":
                raise BuildError("%s: %s: %s repeats with no free twin tile on %s (the cube could not move)" % (
                    where, cube.inst, D.name_of(e["pitch"]), island["chord"]))
            path[i] = best
            mine.add(best)
            owner[best] = ci
            prev = best
        # rests: stay beside the note before (or after) on a free tile of this cube's own, different from both neighbours
        n = len(evs)
        for i, e in enumerate(evs):
            if path[i] is not None:
                continue
            before = next((path[(i - k) % n] for k in range(1, n) if path[(i - k) % n] is not None), None)
            after = next((path[(i + k) % n] for k in range(1, n) if path[(i + k) % n] is not None), None)
            ref = before or after or (0, rows - 1)
            best, bs = None, None
            for x in range(cols):
                for z in range(rows):
                    tile = (x, z)
                    if owner.get(tile, ci) != ci or tile == path[(i - 1) % n] or tile == path[(i + 1) % n]:
                        continue
                    s = abs(x - ref[0]) + abs(z - ref[1]) + (0 if tile in mine else 1.5) + 3.0 * demand.get(tile, 0.0)
                    if bs is None or s < bs:
                        best, bs = tile, s
            if best is None:
                raise BuildError("%s: %s: no free tile for a rest" % (where, cube.inst))
            path[i] = best
            mine.add(best)
            owner[best] = ci
        result.append(([p[0] for p in path], [p[1] for p in path]))
    return result


# ------------------------------------------------------------------ building one song
def load_sheet(path):
    spec = importlib.util.spec_from_file_location("sheet_" + os.path.basename(path)[:-3].replace("-", "_"), path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.SONG


SCALE = {False: [0, 2, 4, 5, 7, 9, 11], True: [0, 2, 3, 5, 7, 8, 10]}
EXT = {"m7": "m9", "maj7": "maj9", "7": "9"}


def pick_palette(isl, harm, key_pc, minor, lane, where, window, allow):
    """The chord of a melody island (chord "auto"): every chord whose tiles can play the island's cubes, its tones inside the key (plus the
    harmony chord's own tones and the sheet's `allow`ed pitch classes); preferred: the harmony chord's 9th, the harmony chord, the same root,
    then the fewest tones outside the harmony."""
    scale = set((key_pc + s) % 12 for s in SCALE[minor])
    hpc, hq = D.parse_chord(harm) if harm else (None, None)
    hset = set((hpc + s) % 12 for s in D.QUALITIES[hq]) if harm else set()
    ok = scale | hset | set(a % 12 for a in allow)
    cands = []
    for pc in range(12):
        for q in D.QUALITIES:
            tones = set((pc + s) % 12 for s in D.QUALITIES[q])
            if not tones <= ok:
                continue
            if harm and pc == hpc and q == EXT.get(hq):
                rank = 0
            elif harm and pc == hpc and q == hq:
                rank = 1
            elif harm and pc == hpc:
                rank = 2
            else:
                rank = 3 + len(tones - hset)
            cands.append((rank, pc, q))
    cands.sort()
    for rank, pc, q in cands:
        trial = Island_copy(isl, D.NAMES[pc] + q)
        try:
            build_island_trial(trial, lane, window, where)
            return D.NAMES[pc] + q
        except BuildError:
            continue
    raise BuildError("%s: no DeaCube chord holds these notes inside the key" % where)


def Island_copy(isl, chord):
    import copy
    c = copy.copy(isl)
    c.chord = chord
    return c


SONG_KEY = [None]   # v9 (N): (tonic pc, minor) of the song being built — the nudges' scale (build_song sets it)


def build_island_trial(isl, lane, window, where):
    """Parses and allocates one island (raises BuildError when its cubes do not fit)."""
    key = SONG_KEY[0]
    pc, q = D.parse_chord(isl.chord)
    parsed = parse_island_cubes(isl, pc, q, window, where)
    for root in island_root(pc, q, isl.reg, lane, parsed, isl.root):
        try:
            allocate({"grid": D.grid(root, q, isl.reg), "root": root, "reg": isl.reg, "chord": isl.chord}, parsed, where, key)
            return root
        except BuildError:
            continue
    raise BuildError(where + ": does not fit")


def parse_island_cubes(isl, pc, q, window, where):
    parsed = []
    for cube in isl.cubes:
        if cube.inst not in D.INSTRUMENT or cube.inst == "drums":
            raise BuildError("%s: an island cube cannot be %r" % (where, cube.inst))
        evs = parse(cube.notes, where + " " + cube.inst)
        base = D.midi_of(cube.base) if cube.base else {"bass": 38, "pad": 55, "strings": 55, "choir": 60}.get(cube.inst, 60)
        for e in evs:
            if e["p"] is None:
                e["pitch"] = None
            elif e["p"][0].isdigit():
                e["pitch"] = degree_pitch(e["p"], pc, q, base)
            else:
                e["pitch"] = D.midi_of(e["p"])
        cyc = sum(e["t"] for e in evs)
        if cyc > window:
            raise BuildError("%s: %s runs %d ticks, longer than its %d-tick window (the end would be cut)" % (where, cube.inst, cyc, window))
        if window % cyc:
            raise BuildError("%s: %s runs %d ticks: it does not tile its %d-tick window" % (where, cube.inst, cyc, window))
        starts = {}
        for (ei, st) in lay_times(evs, window):
            starts.setdefault(ei, []).append(st)
        for ei, e in enumerate(evs):
            sts = starts.get(ei, [])
            e["starts"] = sts
            e["downbeat"] = any(st % BAR == 0 for st in sts)
        parsed.append((cube, evs))
    return parsed


def island_root(pc, q, reg, lane, cubes_events, explicit):
    """Candidate roots for an island in preference order (explicit first, else nearest the lane's preferred root)."""
    if explicit is not None:
        r = D.midi_of(explicit) if isinstance(explicit, str) else explicit
        return [D.clamp_root(r)]
    roots = [r for r in range(36, 85) if r % 12 == pc]
    pref = LANE_ROOT.get(lane, 55)
    return sorted(roots, key=lambda r: (abs(r - pref), r))


def build_song(song):
    cols_out, cubes_out, notes_out = [], [], []
    measures = []
    col_passes = []
    key_pc = D.pc_of(song.key)[0]
    key = (key_pc, bool(song.minor))   # v9 (N): the nudges' scale
    SONG_KEY[0] = key
    for ci, column in enumerate(song.columns):
        present = [ln for ln in LANES if column.lanes.get(ln) is not None]
        extra = [ln for ln in column.lanes if ln not in LANES and column.lanes[ln] is not None]
        if extra:
            raise BuildError("column %d: unknown lanes %s" % (ci, extra))
        if not present:
            raise BuildError("column %d has no island" % ci)
        if len(present) > 4:
            raise BuildError("column %d: more than four islands (MaxLanes)" % ci)
        passes = 1
        for li, lane in enumerate(present):
            isl = column.lanes[lane]
            if isl.chord == "auto":
                harm = column.lanes[present[0]].chord if present[0] != lane else None
                isl.chord = pick_palette(isl, harm, key_pc, song.minor, lane, "col %d (%s) %s" % (ci, column.sec, lane), column.bars * BAR,
                                         getattr(song, "allow", ()))
            pc, q = D.parse_chord(isl.chord)
            where = "col %d (%s) %s %s" % (ci, column.sec, lane, isl.chord)
            window = column.bars * BAR
            parsed = parse_island_cubes(isl, pc, q, window, where)
            reg = isl.reg
            first_err = None
            chosen = None
            for root in island_root(pc, q, reg, lane, parsed, isl.root):
                g = D.grid(root, q, reg)
                info = {"grid": g, "root": root, "reg": reg, "chord": isl.chord}
                try:
                    tiles = allocate(info, parsed, where, key)
                    chosen = (root, g, tiles)
                    break
                except BuildError as e:
                    first_err = first_err or e
            if chosen is None:
                raise first_err
            root, g, tiles = chosen
            rows = len(g[0])
            m_index = len(measures)
            repeat = isl.repeat if isl.repeat is not None else column.repeat
            passes = max(passes, repeat)
            measures.append({
                "chordKey": D.chord_name(root, q), "root": root, "semis": D.QUALITIES[q], "bars": column.bars,
                "px": ci * (D.ISLAND_WIDTH + D.COLUMN_GAP) - (D.NUM_INV - 1) * D.SPACING * 0.5,
                "pz": LANE_Z[lane] - (rows - 1) * D.SPACING * 0.5,
                "placed": True, "kind": 0, "mood": 0,
                "energy": isl.energy if isl.energy is not None else column.energy,
                "fill": isl.fill if isl.fill is not None else (column.fill and li == 0),
                "sleep": isl.sleep, "repeat": repeat, "group": 0, "fall": 0, "col": ci, "reg": reg,
                "lead": lane == "melody",   # v9 (L) the stage lights: the melody lane's grids are lead (DeaCube spotlights them while they sound)
                "_lane": lane, "_sec": column.sec, "_q": q,
            })
            for (cube, evs), (xs, zs) in zip(parsed, tiles):
                cubes_out.append({
                    "instrument": D.INSTRUMENT[cube.inst], "measure": m_index, "xs": xs, "zs": zs,
                    "rests": [e["mod"] == 1 for e in evs], "step": 1, "gate": cube.gate, "mode": cube.mode, "volume": cube.volume,
                    "muted": False, "moon": -1, "mods": [e["mod"] for e in evs], "hits": -1, "rot": 0, "mask": 0, "octave": cube.octave,
                    "reverse": False, "phase": 0, "follow": cube.follow, "echo": cube.echo, "shimmer": cube.shimmer, "rider": False,
                    "seed": 0, "twinOf": -1, "id": 0, "durs": [e["t"] for e in evs],
                    "_want": [e["pitch"] for e in evs], "_lane": lane, "_tag": cube.tag or cube.inst,
                })
                if any(e.get("nudge", 0) for e in evs):
                    cubes_out[-1]["nudges"] = [e.get("nudge", 0) for e in evs]   # v9 (N): scale steps of the song key (DeaCube/Nudge.cs)
        col_passes.append(passes)
    # Moons: one window per bar, their cubes run one bar
    moons = []
    for mi, moon in enumerate(song.moons):
        at = min(max(0, moon.at_col), len(song.columns) - 1)
        cx = at * (D.ISLAND_WIDTH + D.COLUMN_GAP)
        cz = moon.side * 10.5
        moons.append({"chordKey": "Moon", "root": 36, "semis": [0], "bars": 1,
                      "px": cx - (D.MOON_COLS - 1) * D.SPACING * 0.5, "pz": cz - (D.MOON_ROWS - 1) * D.SPACING * 0.5,
                      "placed": True, "kind": 1, "mood": 0, "energy": 2, "fill": False, "sleep": False, "repeat": 1, "group": 0,
                      "fall": 0, "col": -1, "reg": 0})
        for cube in moon.cubes:
            where = "moon %d %s" % (mi, cube.notes[:30])
            evs = parse(cube.notes, where)
            cyc = sum(e["t"] for e in evs)
            if BAR % cyc:
                raise BuildError("%s: a Moon cube runs %d ticks; it must tile the %d-tick bar" % (where, cyc, BAR))
            hits = []
            for i, e in enumerate(evs):
                if e["p"] is None:
                    hits.append(None)
                    continue
                letter = e["p"][0]
                if letter not in DRUM_ROWS:
                    raise BuildError("%s: %r is not a drum" % (where, e["p"]))
                z, fx = DRUM_ROWS[letter]
                hits.append((fx if fx is not None else (int(e["p"][1]) if len(e["p"]) > 1 else 3), z))
            n = len(hits)
            for i in range(n):
                if hits[i] is None:
                    # a rest: the cube waits on a tile of its own row, away from the hits before and after it
                    prev = next((hits[(i - k) % n] for k in range(1, n) if hits[(i - k) % n] is not None), (0, 3))
                    nxt = next((hits[(i + k) % n] for k in range(1, n) if hits[(i + k) % n] is not None), (0, 3))
                    before = hits[(i - 1) % n]
                    hits[i] = next((x, prev[1]) for x in [0, 5, 1, 4, 2, 3] if (x, prev[1]) not in (prev, nxt, before))
            for i in range(n):
                if hits[i] == hits[(i - 1) % n] and i > 0:
                    raise BuildError("%s: two hits in a row on one tile (%s at event %d): write the neighbouring column" % (where, evs[i]["p"], i))
            xs = [h[0] for h in hits]
            zs = [h[1] for h in hits]
            cubes_out.append({
                "instrument": 9, "measure": 0, "xs": xs, "zs": zs, "rests": [e["mod"] == 1 for e in evs], "step": 1, "gate": 1,
                "mode": 0, "volume": cube.volume, "muted": False, "moon": mi, "mods": [e["mod"] for e in evs], "hits": -1, "rot": 0,
                "mask": 0, "octave": 0, "reverse": False, "phase": 0, "follow": 0, "echo": 0, "shimmer": False, "rider": False,
                "seed": 0, "twinOf": -1, "id": 0, "durs": [e["t"] for e in evs],
                "_want": [(None if e["p"] is None else D.drum_piece(x, z)) for e, x, z in zip(evs, xs, zs)], "_lane": "moon", "_tag": "drums",
            })
    for i, c in enumerate(cubes_out):
        c["id"] = i + 1
        h = 2166136261
        for ch in (song.id + ":" + str(i)):
            h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
        c["seed"] = (h & 0x7FFFFFFF) or 1
    vol = [1.0] * 10
    for k, v in song.vol.items():
        vol[D.INSTRUMENT[k]] = float(v)
    deck = []
    for spec in song.deck:
        pc, q = D.parse_chord(spec)
        r = 55 + ((pc - 55) % 12)
        deck.append({"chordKey": D.chord_name(r, q), "root": r, "semis": D.QUALITIES[q], "bars": 1, "px": 0.0, "pz": 0.0, "placed": False,
                     "kind": 0, "mood": 0, "energy": 2, "fill": False, "sleep": False, "repeat": 1, "group": 0, "fall": 0, "col": -1, "reg": 0})
    state = {
        "name": song.title, "bpm": float(song.bpm), "beatsPerBar": 4, "swing": float(song.swing), "loop": True, "transpose": 0,
        "measures": [{k: v for k, v in m.items() if not k.startswith("_")} for m in measures],
        "cubes": [{k: v for k, v in c.items() if not k.startswith("_")} for c in cubes_out],
        "instVolume": vol, "instMuted": [False] * 10, "version": 4, "moons": moons, "climate": 0,
        "tone": float(song.tone), "space": float(song.space), "demoSeed": 0, "deck": deck,
        "keyTonic": key_pc, "keyMinor": bool(song.minor),
    }
    home = song.home
    if home is None:
        home = next((m["_q"] for m in measures if m["root"] % 12 == key_pc and m["_lane"] == "ground"), "m7" if song.minor else "maj7")
    bars_total = sum(c.bars * p for c, p in zip(song.columns, col_passes))
    # hook (package T): the bar the menu's preview starts on = the first chorus bar, passes counted as the timeline does
    hook, at = 0, 0
    for c, p in zip(song.columns, col_passes):
        if c.sec == getattr(song, "hook_section", "chorus"):
            hook = at
            break
        at += c.bars * p
    if any(ord(ch) > 0x2FF for ch in song.after + song.title):
        raise BuildError("title / after must stay in Latin script (the UI font draws no CJK)")
    entry = {"id": song.id, "title": song.title, "after": song.after, "vibe": D.vibe_of(home), "bpm": float(song.bpm),
             "bars": bars_total, "file": "Gallery/" + song.id, "blurb": song.blurb, "hook": hook}
    return state, entry, measures, cubes_out, col_passes


# ------------------------------------------------------------------ verification (reads the JSON back)
def verify(song, state, measures, cubes_meta, col_passes):
    """Decodes every cube of the written JSON through the grid + fold and compares with the sheet's pitches. Returns (errors, events)."""
    errs = []
    ms, cubes = state["measures"], state["cubes"]
    key = (D.pc_of(song.key)[0], bool(song.minor))   # v9 (N): the nudges' scale
    owner = {}
    events = []    # (song beat, lane, tag, slot, pitch, beats, section, column)
    col_start = []
    t = 0.0
    col_len = {}
    for m in ms:
        col_len[m["col"]] = m["bars"] * 4
    for c in range(len(col_len)):
        col_start.append(t)
        t += col_len[c] * col_passes[c]
    total = t
    for ci, (c, meta) in enumerate(zip(cubes, cubes_meta)):
        slot = c["instrument"]
        if c["moon"] >= 0:
            if len(c["xs"]) != len(c["durs"]):
                errs.append("moon cube %d: xs/durs length" % ci)
            for i, (x, z) in enumerate(zip(c["xs"], c["zs"])):
                if not (0 <= x < D.MOON_COLS and 0 <= z < D.MOON_ROWS):
                    errs.append("moon cube %d node %d off the kit grid" % (ci, i))
            bars = int(round(total / 4))
            evs = [{"t": d, "mod": mo} for d, mo in zip(c["durs"], c["mods"])]
            for b in range(bars):
                for (ei, st) in lay_times(evs, BAR):
                    if c["mods"][ei] == 1:
                        continue
                    events.append((b * 4 + st / 24.0, "moon", "drums", 9, D.drum_piece(c["xs"][ei], c["zs"][ei]), evs[ei]["t"] / 24.0, "", -1))
            continue
        m = ms[c["measure"]]
        q = next(k for k, v in D.QUALITIES.items() if v == m["semis"])
        g = D.grid(D.clamp_root(m["root"]), q, m["reg"])
        rows = len(g[0])
        n = len(c["xs"])
        if not (n == len(c["zs"]) == len(c["durs"]) == len(c["mods"])):
            errs.append("cube %d: per-node arrays differ in length" % ci)
            continue
        for i in range(n):
            x, z = c["xs"][i], c["zs"][i]
            if not (0 <= x < D.NUM_INV and 0 <= z < rows):
                errs.append("cube %d node %d (%d,%d) off the %dx%d grid" % (ci, i, x, z, D.NUM_INV, rows))
                continue
            key = (c["measure"], x, z)
            if owner.setdefault(key, ci) != ci:
                errs.append("cube %d shares tile %s of island %d with cube %d" % (ci, (x, z), c["measure"], owner[key]))
            if i > 0 and (x, z) == (c["xs"][i - 1], c["zs"][i - 1]) and slot != 4:
                errs.append("cube %d (%s) nodes %d-%d on one tile" % (ci, meta["_tag"], i - 1, i))
        for d in c["durs"]:
            if not (3 <= d <= 192):
                errs.append("cube %d: length %d outside 3..192 ticks" % (ci, d))
        window = m["bars"] * BAR
        cyc = sum(c["durs"])
        if cyc > window or window % cyc:
            errs.append("cube %d: %d ticks do not tile the %d-tick window" % (ci, cyc, window))
        evs = [{"t": d, "mod": mo} for d, mo in zip(c["durs"], c["mods"])]
        passes = m["repeat"]
        for p in range(max(1, passes)):
            base = col_start[m["col"]] + p * m["bars"] * 4
            for (ei, st) in lay_times(evs, window):
                if c["mods"][ei] == 1:
                    continue
                x, z = c["xs"][ei], c["zs"][ei]
                nudges = c.get("nudges") or []
                nd = nudges[ei] if len(nudges) == n else 0   # v9 (N): a short / missing list = none (CubeState.nudges)
                pitch = D.sounding(slot, g, x, z, D.clamp_root(m["root"]), m["reg"], c["octave"], c["mods"][ei] == 8, st % BAR == 0, nd, key)
                want = meta["_want"][ei]
                if want is not None and pitch != want:
                    errs.append("cube %d (%s, col %d) node %d sounds %s, the sheet wrote %s" % (ci, meta["_tag"], m["col"], ei, D.name_of(pitch), D.name_of(want)))
                dur = min(evs[ei]["t"], window - st) / 24.0
                events.append((base + st / 24.0, meta["_lane"], meta["_tag"], slot, pitch, dur, measures[c["measure"]]["_sec"], m["col"]))
                if c["follow"] in (1, 3) or c["follow"] in (2, 3):
                    for up in ([True] if c["follow"] == 1 else ([False] if c["follow"] == 2 else [True, False])):
                        sh = D.shadow_tile(g, D.clamp_root(m["root"]), x, z, up)
                        if sh:
                            sp = D.fold(slot, g[x][sh[0]] + sh[1] + 12 * c["octave"], m["reg"])
                            events.append((base + st / 24.0, meta["_lane"], meta["_tag"] + "+follow", slot, sp, dur, measures[c["measure"]]["_sec"], m["col"]))
                if slot == 2:
                    for (zz, off) in D.pad_tones(g, x, z):
                        sp = D.fold(slot, g[x][zz] + off + 12 * c["octave"], m["reg"])
                        events.append((base + st / 24.0, meta["_lane"], meta["_tag"] + "+pad", slot, sp, dur, measures[c["measure"]]["_sec"], m["col"]))
    events.sort()
    # every chord change audible: the ground island of each column strikes on the first beat of each of its passes
    ground_starts = set(round(b * 96) for (b, lane, tag, slot, p, d, sec, c) in events if lane == "ground")
    for m in ms:
        if m.get("kind", 0) != 0:
            continue
        first_of_col = next(x for x in ms if x["col"] == m["col"])
        if m is not first_of_col:
            continue
        for pss in range(max(1, m["repeat"])):
            t0 = round((col_start[m["col"]] + pss * m["bars"] * 4) * 96)
            if t0 not in ground_starts:
                errs.append("column %d pass %d: no ground onset on its first beat (the chord change would be silent)" % (m["col"], pss))
    return errs, events, total


def pct(a, b):
    return "%d%%" % round(100.0 * a / b) if b else "-"


def melody_stats(evs, bpm):
    """The r35 laws measured on one lane's events [(beat, pitch, dur)]."""
    if len(evs) < 2:
        return {}
    evs = sorted(evs)
    bars = {}
    for b, p, d in evs:
        bars.setdefault(int(b // 4), []).append(p)
    ioi = [evs[i + 1][0] - evs[i][0] for i in range(len(evs) - 1) if evs[i + 1][0] - evs[i][0] < 4]
    cls = {"16th": 0, "8th": 0, "quarter": 0, "dotted/other": 0, "longer": 0}
    for d in ioi:
        if d <= 0.3:
            cls["16th"] += 1
        elif d <= 0.55:
            cls["8th"] += 1
        elif 0.95 <= d <= 1.05:
            cls["quarter"] += 1
        elif d < 0.95:
            cls["dotted/other"] += 1
        else:
            cls["longer"] += 1
    iv = [evs[i + 1][1] - evs[i][1] for i in range(len(evs) - 1) if evs[i + 1][0] - (evs[i][0] + evs[i][2]) < 0.5]
    ivc = {"repeat": 0, "step": 0, "third": 0, "4th-5th": 0, "6th+": 0}
    for d in iv:
        a = abs(d)
        ivc["repeat" if a == 0 else "step" if a <= 2 else "third" if a <= 4 else "4th-5th" if a <= 7 else "6th+"] += 1
    starts = [evs[0]] + [evs[i + 1] for i in range(len(evs) - 1) if evs[i + 1][0] - (evs[i][0] + evs[i][2]) >= 0.49]
    sl = {"downbeat": 0, "beat": 0, "off8": 0, "off16": 0}
    for b, p, d in starts:
        f = round((b % 4) * 4) % 16
        sl["downbeat" if f == 0 else "beat" if f % 4 == 0 else "off8" if f % 2 == 0 else "off16"] += 1
    ps = sorted(p for _, p, _ in evs)
    # 2-bar cells returning exactly (onsets relative to the cell + pitches)
    cells = {}
    for b, p, d in evs:
        k = int(b // 8)
        cells.setdefault(k, []).append((round((b - 8 * k) * 4), p))
    seen, rep = [], 0
    for k in sorted(cells):
        cell = tuple(cells[k])
        if cell in seen:
            rep += 1
        seen.append(cell)
    cells1, seen1, rep1 = {}, [], 0
    for b, p, d in evs:
        k = int(b // 4)
        cells1.setdefault(k, []).append((round((b - 4 * k) * 4), p))
    for k in sorted(cells1):
        cell = tuple(cells1[k])
        if cell in seen1:
            rep1 += 1
        seen1.append(cell)
    slots = {}
    for b, p, d in starts:
        f = round((b % 4) * 4) % 16
        nm = {0: "1", 2: "&1", 4: "2", 6: "&2", 8: "3", 10: "&3", 12: "4", 14: "&4"}.get(f, "16th")
        slots[nm] = slots.get(nm, 0) + 1
    sung = len(bars)
    return {
        "cells1_exact_repeat": pct(rep1, len(seen1)), "start_slots": {k: pct(v, len(starts)) for k, v in sorted(slots.items())},
        "notes": len(evs), "per_bar": len(evs) / float(sung), "syl_s": len(evs) / (sung * 4 * 60.0 / bpm),
        "ioi": {k: pct(v, len(ioi)) for k, v in cls.items()}, "iv": {k: pct(v, len(iv)) for k, v in ivc.items()},
        "mean_iv": sum(abs(x) for x in iv) / float(max(1, len(iv))), "lo": ps[0], "hi": ps[-1], "median": statistics.median(ps),
        "p10": ps[len(ps) // 10], "p90": ps[9 * len(ps) // 10], "phrases": len(starts), "starts": {k: pct(v, len(starts)) for k, v in sl.items()},
        "cells2": len(seen), "cells2_exact_repeat": pct(rep, len(seen)),
    }


def report(song, state, entry, events, total):
    """Human-readable stats for the doc: per section the voice's laws, texture counts, doubling, unisons, chord-change onsets."""
    lines = []
    bpm = song.bpm
    lines.append("%s — %s  (%s)  bpm %g  key %s %s  %d bars (%.1f s)  islands %d  cubes %d  moons %d  home vibe %s" % (
        song.id, song.title, song.after, bpm, song.key, "minor" if song.minor else "major", entry["bars"], total * 60.0 / bpm,
        len(state["measures"]), len(state["cubes"]), len(state["moons"]), D.VIBE_WORD[entry["vibe"]]))
    voice = [(b, p, d, s) for (b, lane, tag, slot, p, d, s, c) in events if tag == "voice"]
    secs = []
    for (b, lane, tag, slot, p, d, s, c) in events:
        if s and s not in secs:
            secs.append(s)
    allv = melody_stats([(b, p, d) for b, p, d, s in voice], bpm)
    if allv:
        lines.append("  voice (all): %d notes, %.1f per sung bar, %.2f notes/s | IOI %s | intervals %s mean %.1f | range %d-%d median %g "
                     "p10-p90 %d-%d | %d phrases, starts %s | 1-bar cells returning %s, 2-bar cells %d returning %s" % (
                         allv["notes"], allv["per_bar"], allv["syl_s"], allv["ioi"], allv["iv"], allv["mean_iv"], allv["lo"], allv["hi"],
                         allv["median"], allv["p10"], allv["p90"], allv["phrases"], allv["start_slots"], allv["cells1_exact_repeat"], allv["cells2"],
                         allv["cells2_exact_repeat"]))
    for s in secs:
        vs = [(b, p, d) for b, p, d, ss in voice if ss == s]
        bars = sorted(set(c for (b, lane, tag, slot, p, d, ss, c) in events if ss == s))
        nb = sum(1 for _ in bars)
        span = [(b, lane, tag, slot, p, d) for (b, lane, tag, slot, p, d, ss, c) in events if ss == s]
        dur_beats = 0.0
        if span:
            first = min(e[0] for e in span)
            last = max(e[0] + e[5] for e in span)
            dur_beats = max(4.0, round((last - first) / 4.0) * 4.0)
        per_bar = lambda f: len([e for e in span if f(e)]) / max(1.0, dur_beats / 4.0)
        med = statistics.median([p for _, p, _ in vs]) if vs else None
        onsets_acc = per_bar(lambda e: e[2] not in ("voice", "drums", "bass") and "+" not in e[2])
        notes_acc = per_bar(lambda e: e[2] not in ("voice", "drums", "bass"))
        lines.append("  %-9s cols %d | voice %s notes median %s | bass %.1f/bar | other pitched: %.1f onsets, %.1f notes a bar" % (
            s, nb, len(vs), med, per_bar(lambda e: e[2] == "bass"), onsets_acc, notes_acc))
    # doubling: voice onsets with a pitched onset exactly +12 at the same time
    by_t = {}
    for (b, lane, tag, slot, p, d, s, c) in events:
        by_t.setdefault(round(b * 96), []).append((tag, p, s))
    dbl = {}
    for (b, p, d, s) in voice:
        hit = any(t2 != "voice" and p2 == p + 12 for (t2, p2, s2) in by_t.get(round(b * 96), []))
        a, n = dbl.get(s, (0, 0))
        dbl[s] = (a + (1 if hit else 0), n + 1)
    lines.append("  octave-up doubling of the voice by section: " + ", ".join("%s %s" % (s, pct(a, n)) for s, (a, n) in dbl.items()))
    # unison: two different pitched cubes striking the same pitch together, runs
    uni = []
    for k, lst in sorted(by_t.items()):
        seen = {}
        for (tag, p, s) in lst:
            if tag == "drums" or "+" in tag:
                continue
            if p in seen and seen[p] != tag:
                uni.append("%s/%s %s at beat %.2f" % (seen[p], tag, D.name_of(p), k / 96.0))
            seen[p] = tag
    lines.append("  unison strikes between different cubes: %d %s" % (len(uni), "; ".join(uni[:6])))
    drums = [e for e in events if e[2] == "drums"]
    lines.append("  drums: %.1f hits a bar" % (len(drums) / max(1.0, total / 4.0)))
    return "\n".join(lines), allv


def write_json(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, separators=(",", ":"), ensure_ascii=False)
    os.replace(tmp, path)


def build_import(MS, name, only, do_verify, args):
    """v9 (G): a MIDI import (midi_song.build): writes / verifies its JSON, prints its faithfulness, checks the target (>= 98 % of the MIDI's notes
    with the exact pitch and onset on the right voice; nothing the MIDI does not play beyond the listed folds). Returns (index entry, ok) or None."""
    cfg, plan, state, entry = MS.build(name)
    if only and cfg["id"] != only:
        return None
    out = os.path.join(OUT, cfg["id"] + ".json")
    good = True
    if do_verify:
        with open(out, encoding="utf-8") as f:
            disk = json.load(f)
        if disk != json.loads(json.dumps(state)):
            print("VERIFY %s: the JSON on disk differs from a fresh build (run the builder)" % cfg["id"])
            good = False
    else:
        os.makedirs(OUT, exist_ok=True)
        write_json(out, state)
    rows, tot, extra, _ = MS.faithfulness(plan, state)
    pct_exact = 100.0 * tot["hit"] / max(1, tot["n"])
    folds = sum(1 for d in plan["deviations"] if d[0] == "fold")
    if pct_exact < 98.0 or extra > folds or any(d[0] in ("missing", "kit") for d in plan["deviations"]):
        print("  FAIL %s: %.2f%% exact, %d notes not in the MIDI (%d folds)" % (cfg["id"], pct_exact, extra, folds))
        good = False
    print("%s %s: %d islands, %d cubes, %d moons, %d bars, %.2f%% of the MIDI's notes exact" % (
        "verified" if do_verify else "built", cfg["id"], len(state["measures"]), len(state["cubes"]), len(state["moons"]), entry["bars"], pct_exact))
    if "--stats" in args or do_verify:
        print(MS.report(plan, state))
    if "--table" in args:
        print(MS.table(plan, state))
    return entry, good


def has_round2(path):
    with open(path, encoding="utf-8") as f:
        return "CONFIG2" in f.read()


def build_import2(MG, name, only, do_verify, args):
    """v9 round 2 (G): an import built the way a player builds a song (midi_grid.build): writes / verifies its JSON, prints its plan and numbers,
    and its own checks (no keyboards, at most 4 lanes a column, no stack lifts, cubes that walk, the drums exact)."""
    cfg, plan, state, entry = MG.build(name)
    if only and cfg["id"] != only:
        return None
    out = os.path.join(OUT, cfg["id"] + ".json")
    good = True
    if do_verify:
        with open(out, encoding="utf-8") as f:
            disk = json.load(f)
        if disk != json.loads(json.dumps(state)):
            print("VERIFY %s: the JSON on disk differs from a fresh build (run the builder)" % cfg["id"])
            good = False
    else:
        os.makedirs(OUT, exist_ok=True)
        write_json(out, state)
    bad = MG.checks(plan, state)
    for b in bad:
        print("  FAIL %s: %s" % (cfg["id"], b))
    good = good and not bad
    print("%s %s: %d islands (%d stairs), %d cubes, %d moons, %d bars" % (
        "verified" if do_verify else "built", cfg["id"], len(state["measures"]), sum(1 for m in state["measures"] if m["kind"] == 3),
        len(state["cubes"]), len(state["moons"]), entry["bars"]))
    if "--stats" in args or do_verify:
        print(MG.numbers(plan))
    if "--table" in args:
        print(MG.describe(plan))
    return entry, good


def main():
    args = sys.argv[1:]
    only = args[args.index("--only") + 1] if "--only" in args else None
    do_verify = "--verify" in args
    stats = "--stats" in args or do_verify
    sheets = sorted(glob.glob(os.path.join(HERE, "sheets", "*.py")))
    entries, ok = [], True
    order = []
    for p in sheets:
        if os.path.basename(p).startswith("_"):
            continue
        song = load_sheet(p)
        order.append((getattr(song, "order", 99), song, p))
    # v9 (G): songs imported from MIDI files (imports/<name>.py, built by midi_song.py: the gallery's "get proto")
    import midi_song as MS
    import midi_grid as MG
    for p in sorted(glob.glob(os.path.join(HERE, "imports", "*.py"))):
        if os.path.basename(p).startswith("_"):
            continue
        name = os.path.basename(p)[:-3]
        order.append((MS.load_config(name).get("order", 99), name, p))
    order.sort(key=lambda t: t[0])
    for _, song, p in order:
        if isinstance(song, str):
            # round 2 (the import's CONFIG2): the song built from chord cards, lanes and devices (midi_grid.py); else round 1's keyboards
            r = build_import2(MG, song, only, do_verify, args) if hasattr(MS.load_config, "__call__") and has_round2(p) else build_import(MS, song, only, do_verify, args)
            if r is None:
                continue
            entry, good = r
            ok = ok and good
            entries.append(entry)
            continue
        if only and song.id != only:
            continue
        try:
            state, entry, measures, cubes_meta, col_passes = build_song(song)
        except BuildError as e:
            print("BUILD ERROR %s: %s" % (song.id, e))
            ok = False
            continue
        out = os.path.join(OUT, song.id + ".json")
        if do_verify:
            with open(out, encoding="utf-8") as f:
                disk = json.load(f)
            if disk != json.loads(json.dumps(state)):
                print("VERIFY %s: the JSON on disk differs from a fresh build (run the builder)" % song.id)
                ok = False
            state = disk
        else:
            os.makedirs(OUT, exist_ok=True)
            write_json(out, state)
        errs, events, total = verify(song, state, measures, cubes_meta, col_passes)
        for e in errs[:40]:
            print("  FAIL", e)
        if errs:
            ok = False
        print("%s %s: %d islands, %d cubes, %d moons, %d bars, %d errors" % ("verified" if do_verify else "built", song.id, len(state["measures"]),
                                                                          len(state["cubes"]), len(state["moons"]), entry["bars"], len(errs)))
        if stats:
            txt, _ = report(song, state, entry, events, total)
            print(txt)
        if "--table" in args:
            for ci in sorted(set(m["col"] for m in measures)):
                isl = [m for m in measures if m["col"] == ci]
                row = " | ".join("%s %s%s" % (m["_lane"][:3], m["chordKey"], (" reg%+d" % m["reg"]) if m["reg"] else "") for m in isl)
                voice = [c for c in cubes_meta if measures[c["measure"]]["col"] == ci and c["_tag"] == "voice"] if False else []
                vn = []
                for c, cm in zip(state["cubes"], cubes_meta):
                    if cm["_tag"] == "voice" and c["moon"] < 0 and measures[c["measure"]]["col"] == ci:
                        vn = [D.name_of(p) if p else "r" for p in cm["_want"]]
                print("  %2d %-7s x%d  %-58s %s" % (ci, isl[0]["_sec"], isl[0]["repeat"], row, " ".join(vn)))
        entries.append(entry)
    if not only:
        idx = {"songs": entries}
        ipath = os.path.join(OUT, "index.json")
        if do_verify:
            with open(ipath, encoding="utf-8") as f:
                if json.load(f) != json.loads(json.dumps(idx)):
                    print("VERIFY index.json differs from a fresh build")
                    ok = False
        else:
            write_json(ipath, idx)
    print("OK" if ok else "FAILED")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
