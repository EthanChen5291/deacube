"""Measure one song of the Japanese MIDI corpus (read only) for the gallery sheets.

usage: python3 analyze.py "<file name or part of it>" [--bars A-B] [--corpus DIR]
Prints: tempo, key, the form (the r38 window labels from _analysis-r38.json when present), the chords by half-bar
(labelled here from NOTE + BASS), and per section label the melody's rhythm density / intervals / range / phrase starts,
the accompaniment's and the bass's groove (onsets per bar, notes per strike, 16th-slot histogram, root / fifth motion).
Numbers only: no melody is written out (the gallery's tunes are original).
"""
import sys
sys.dont_write_bytecode = True   # no __pycache__ beside the sheets
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import midi  # noqa: E402

CORPUS = os.path.expanduser("~/Documents/GitHub/motif-engine/audios/vocaloid-r35")
NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
# chord templates: name -> pitch classes above the root (the first is the label suffix)
TEMPLATES = [
    ("", (0, 4, 7)), ("m", (0, 3, 7)), ("7", (0, 4, 7, 10)), ("^7", (0, 4, 7, 11)), ("m7", (0, 3, 7, 10)),
    ("sus4", (0, 5, 7)), ("sus2", (0, 2, 7)), ("o", (0, 3, 6)), ("m7b5", (0, 3, 6, 10)), ("6", (0, 4, 7, 9)),
    ("m6", (0, 3, 7, 9)), ("+", (0, 4, 8)), ("5", (0, 7)),
]
MAJ = [0, 2, 4, 5, 7, 9, 11]
MIN = [0, 2, 3, 5, 7, 8, 10]
ROMAN = ["I", "bII", "II", "bIII", "III", "IV", "#IV", "V", "bVI", "VI", "bVII", "VII"]


def ks_key(notes):
    """Krumhansl-Schmuckler over duration-weighted pitch classes: (tonic pc, minor)."""
    maj = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
    mnr = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
    h = [0.0] * 12
    for n in notes:
        h[n.pitch % 12] += n.end - n.start
    best = None
    for t in range(12):
        for prof, mn in ((maj, False), (mnr, True)):
            x = [prof[(i - t) % 12] for i in range(12)]
            mx, mh = sum(x) / 12.0, sum(h) / 12.0
            num = sum((a - mx) * (b - mh) for a, b in zip(x, h))
            den = (sum((a - mx) ** 2 for a in x) * sum((b - mh) ** 2 for b in h)) ** 0.5
            r = num / den if den else 0
            if best is None or r > best[0]:
                best = (r, t, mn)
    return best[1], best[2], best[0]


def find(name, corpus):
    files = sorted(f for f in os.listdir(corpus) if f.endswith(".mid"))
    for f in files:
        if f.lower().startswith(name.lower()):
            return os.path.join(corpus, f)
    for f in files:
        if name.lower() in f.lower():
            return os.path.join(corpus, f)
    raise SystemExit("no corpus file matches " + name)


def roles(song, path, corpus):
    """(voice, acc list, bass) tracks and the grid shift in 16ths from the r35 read-out (by name otherwise)."""
    shift = 0
    try:
        for e in json.load(open(os.path.join(corpus, "_analysis.json"))):
            if e.get("file") == os.path.basename(path) and e.get("tracks"):
                t = e["tracks"]
                by = {tr.name: tr for tr in song.tracks}
                shift = e.get("gridShift16ths") or 0
                return by.get(t["voice"]), [by[a] for a in t["acc"] if a in by], by.get(t["bass"]), shift
    except (OSError, ValueError):
        pass
    tracks = [t for t in song.tracks if t.notes]
    voice = next((t for t in tracks if t.name.upper() in ("VOCAL", "MIKU", "SING", "RIN")), None)
    bass = next((t for t in tracks if t.name.upper() == "BASS"), None)
    acc = [t for t in tracks if t is not voice and t is not bass]
    return voice, acc, bass, shift


def label_window(notes, a, b):
    """Best chord label of the notes sounding in [a, b): (root pc, suffix) or None."""
    h = [0.0] * 12
    low, low_t = None, None
    for n in notes:
        s, e = max(n.start, a), min(n.end, b)
        if e <= s:
            continue
        h[n.pitch % 12] += e - s
        if low is None or n.pitch < low or (n.pitch == low and n.start < low_t):
            low, low_t = n.pitch, n.start
    tot = sum(h)
    if tot <= 0:
        return None
    best = None
    for r in range(12):
        for suf, pcs in TEMPLATES:
            inside = sum(h[(r + p) % 12] for p in pcs)
            outside = tot - inside
            score = inside - 1.3 * outside - 0.08 * tot * (len(pcs) - 3)
            if low is not None and low % 12 == r:
                score += 0.25 * tot
            if h[r] <= 0:
                score -= 0.4 * tot
            if best is None or score > best[0]:
                best = (score, r, suf)
    return best[1], best[2]


def roman(root, suf, tonic, minor):
    """Roman numeral of a chord relative to the tonic: minor qualities lowercase (b / # prefix kept)."""
    rn = ROMAN[(root - tonic) % 12]
    is_minor = suf.startswith("m") or suf == "o"
    if is_minor:
        rn = rn[0] + rn[1:].lower() if rn[0] in "b#" else rn.lower()
    tail = {"m": "", "m7": "7", "m6": "6", "m7b5": "\u00f8", "o": "\u00b0"}.get(suf, suf)
    return rn + tail


def pct(x, n):
    return "%d%%" % round(100.0 * x / n) if n else "-"


def main():
    args = sys.argv[1:]
    corpus = CORPUS
    rng = None
    name = None
    beats_view = False
    i = 0
    while i < len(args):
        if args[i] == "--corpus":
            corpus = args[i + 1]; i += 2
        elif args[i] == "--beats":
            beats_view = True; i += 1
        elif args[i] == "--bars":
            a, b = args[i + 1].split("-"); rng = (int(a), int(b)); i += 2
        else:
            name = args[i]; i += 1
    path = find(name, corpus)
    song = midi.read(path)
    voice, acc, bass, shift = roles(song, path, corpus)
    q = song.division
    bar = 4 * q
    shift_t = shift * q // 4
    def sh(ns):
        return [midi.Note(n.pitch, n.start + shift_t, n.end + shift_t, n.vel, n.channel) for n in ns]
    vn = sh(voice.notes) if voice else []
    an = sh([n for t in acc for n in t.notes])
    bn = sh(bass.notes) if bass else []
    everything = vn + an + bn
    total_bars = max(n.end for n in everything) // bar + 1
    tonic, minor, r = ks_key(everything)
    meta = None
    try:
        for e in json.load(open(os.path.join(corpus, "_analysis-r38.json"))):
            if e.get("file") == os.path.basename(path):
                meta = e
    except (OSError, ValueError):
        pass
    if meta and meta.get("key"):
        kname, kmode = meta["key"].split()
        tonic, minor = NAMES.index(kname), kmode == "minor"
    lab = (meta or {}).get("label") or {}
    print("== %s  (%s · %s · %s)" % (os.path.basename(path), lab.get("song", "?"), lab.get("producer", "?"), lab.get("lane", "?")))
    print("tempo %.1f bpm | key %s %s (KS r %.2f) | %d bars | grid shift %d/16 | tracks voice=%s acc=%s bass=%s" % (
        song.bpm(), NAMES[tonic], "minor" if minor else "major", r, total_bars, shift,
        voice.name if voice else None, [t.name for t in acc], bass.name if bass else None))
    # form: r38 window labels per bar
    lab_of_bar = {}
    if meta and meta.get("sections"):
        print("form (r38 windows):")
        for s in meta["sections"]:
            print("  bar %3d +%2d  %-9s acc %-11s bass %-10s loop %s" % (s["startBar"], s["bars"], s["label"], s.get("accCls"), s.get("bassCls"), s.get("loop")))
            for b in range(s["startBar"], s["startBar"] + s["bars"]):
                lab_of_bar[b] = s["label"]
    # chords by half-bar
    harm = an + bn
    chords = []
    for b in range(total_bars):
        row = []
        for h in (0, 1):
            a0 = b * bar + h * bar // 2
            l = label_window(harm, a0, a0 + bar // 2)
            row.append(roman(l[0], l[1], tonic, minor) if l else "-")
        chords.append(row)
    lo, hi = rng if rng else (0, total_bars)
    print("chords by half-bar (bars %d-%d):" % (lo, hi - 1))
    line = []
    for b in range(lo, min(hi, total_bars)):
        c = chords[b]
        line.append("%3d %-6s %-8s|%-8s" % (b, lab_of_bar.get(b, "")[:6], c[0], c[1]))
        if len(line) == 4:
            print("  " + "   ".join(line)); line = []
    if line:
        print("  " + "   ".join(line))
    if beats_view:
        print("per beat (bass at the beat / chord of the beat, NOTE + BASS):")
        for b in range(lo, min(hi, total_bars)):
            cells = []
            for k in range(4):
                t0 = b * bar + k * q
                sounding = [n for n in bn if n.start <= t0 + q // 8 and n.end > t0 + q // 8]
                bp = NAMES[min(sounding, key=lambda n: n.pitch).pitch % 12] if sounding else "."
                l = label_window(harm, t0, t0 + q)
                cells.append("%-2s %-7s" % (bp, roman(l[0], l[1], tonic, minor) if l else "-"))
            print("  %3d %-6s %s" % (b, lab_of_bar.get(b, "")[:6], " | ".join(cells)))
    # per section label stats
    labels = sorted(set(lab_of_bar.values())) or ["all"]
    for L in labels + ["all"]:
        bars_in = [b for b in range(total_bars) if (L == "all" or lab_of_bar.get(b) == L)]
        if not bars_in:
            continue
        bs = set(bars_in)
        v = [n for n in vn if n.start // bar in bs]
        print("-- %s: %d bars" % (L, len(bars_in)))
        if v:
            sung = len(set(n.start // bar for n in v))
            ioi = [(v[k + 1].start - v[k].start) / float(q) for k in range(len(v) - 1) if v[k + 1].start - v[k].start < 2 * bar]
            cls = {"16th": 0, "8th": 0, "dotted8/triplet": 0, "quarter": 0, "longer": 0}
            for d in ioi:
                if d <= 0.3: cls["16th"] += 1
                elif d <= 0.55: cls["8th"] += 1
                elif d < 0.95: cls["dotted8/triplet"] += 1
                elif d <= 1.05: cls["quarter"] += 1
                else: cls["longer"] += 1
            iv = [v[k + 1].pitch - v[k].pitch for k in range(len(v) - 1) if v[k + 1].start - v[k].end < q // 2]
            ivc = {"repeat": 0, "step": 0, "third": 0, "4th-5th": 0, "6th+": 0}
            for d in iv:
                a = abs(d)
                if a == 0: ivc["repeat"] += 1
                elif a <= 2: ivc["step"] += 1
                elif a <= 4: ivc["third"] += 1
                elif a <= 7: ivc["4th-5th"] += 1
                else: ivc["6th+"] += 1
            ps = sorted(n.pitch for n in v)
            # phrases: a rest >= an 8th
            starts = [v[0]] + [v[k + 1] for k in range(len(v) - 1) if v[k + 1].start - v[k].end >= q // 2]
            slot = {}
            for n in starts:
                s16 = (n.start % bar) * 4 // q
                key = "downbeat" if s16 == 0 else ("beat" if s16 % 4 == 0 else ("off8" if s16 % 2 == 0 else "off16"))
                slot[key] = slot.get(key, 0) + 1
            durs = [(n.end - n.start) / float(q) for n in v]
            print("   melody: %d notes, %.1f per sung bar, IOI %s" % (len(v), len(v) / float(max(1, sung)), " ".join("%s %s" % (k, pct(c, len(ioi))) for k, c in cls.items())))
            print("           intervals %s | mean |iv| %.1f" % (" ".join("%s %s" % (k, pct(c, len(iv))) for k, c in ivc.items()), sum(abs(d) for d in iv) / float(max(1, len(iv)))))
            print("           range %d-%d median %d p10 %d p90 %d | note length median %.2f beat | phrases %d (%.1f notes) starts %s" % (
                ps[0], ps[-1], statistics.median(ps), ps[len(ps) // 10], ps[9 * len(ps) // 10], statistics.median(durs), len(starts), len(v) / float(len(starts)),
                " ".join("%s %s" % (k, pct(c, len(starts))) for k, c in sorted(slot.items()))))
        for nm, ns in (("acc", an), ("bass", bn)):
            x = [n for n in ns if n.start // bar in bs]
            if not x:
                print("   %s: silent" % nm); continue
            strikes = {}
            for n in x:
                strikes.setdefault(n.start, []).append(n)
            hist = [0] * 16
            for s in strikes:
                hist[(s % bar) * 4 // q % 16] += 1
            peak = max(hist) or 1
            ps = sorted(n.pitch for n in x)
            extra = ""
            if nm == "bass":
                seq = [min(strikes[s], key=lambda n: n.pitch) for s in sorted(strikes)]
                moves = [seq[k + 1].pitch - seq[k].pitch for k in range(len(seq) - 1)]
                rep = sum(1 for m in moves if m == 0); fif = sum(1 for m in moves if abs(m) in (5, 7)); octv = sum(1 for m in moves if abs(m) == 12)
                roots = 0
                for n in seq:
                    b = n.start // bar; h = 1 if (n.start % bar) >= bar // 2 else 0
                    l = label_window(harm, b * bar + h * bar // 2, b * bar + h * bar // 2 + bar // 2)
                    if l and n.pitch % 12 == l[0]:
                        roots += 1
                extra = " | repeat %s fifth-move %s octave %s on-root %s" % (pct(rep, len(moves)), pct(fif, len(moves)), pct(octv, len(moves)), pct(roots, len(seq)))
            lens = [(n.end - n.start) / float(q) for n in x]
            print("   %s: %.1f strikes/bar, %.2f notes/strike, pitch %d-%d median %d, note len med %.2f%s" % (
                nm, len(strikes) / float(len(bars_in)), len(x) / float(len(strikes)), ps[0], ps[-1], statistics.median(ps), statistics.median(lens), extra))
            print("        16th slots: " + " ".join(("%d" % round(9.0 * h / peak)) for h in hist))


if __name__ == "__main__":
    main()
