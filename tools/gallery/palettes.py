"""Composer's aid: for a key and a harmony chord, the DeaCube chords a melody island could use and the exact pitches their tiles
play for an instrument (lead by default) in a range. usage: python3 palettes.py D major A7 [--lo 57 --hi 81 --reg 0 --inst lead]
Marks each pitch against the harmony: UPPER = chord tone, lower = tension (9, 11, 13 ...), * = a semitone against a chord tone."""
import sys
sys.dont_write_bytecode = True   # no __pycache__ beside the sheets
import sys
import deacube as D

args = sys.argv[1:]
key, mode, harm = args[0], args[1], args[2]
opt = {k: v for k, v in zip(args[3::2], args[4::2])}
lo, hi, reg, inst = int(opt.get("--lo", 57)), int(opt.get("--hi", 81)), int(opt.get("--reg", 0)), opt.get("--inst", "lead")
kpc = D.pc_of(key)[0]
scale = set((kpc + s) % 12 for s in ([0, 2, 3, 5, 7, 8, 10] if mode.startswith("min") else [0, 2, 4, 5, 7, 9, 11]))
hpc, hq = D.parse_chord(harm)
htones = set((hpc + s) % 12 for s in D.QUALITIES[hq])
ok = scale | htones
slot = D.INSTRUMENT[inst]
rows = []
for pc in range(12):
    for q in D.QUALITIES:
        tones = set((pc + s) % 12 for s in D.QUALITIES[q])
        if not tones <= ok:
            continue
        best = None
        for root in range(36, 85):
            if root % 12 != pc:
                continue
            g = D.grid(root, q, reg)
            ps = sorted(set(D.sounding(slot, g, x, z, root, reg) for x in range(6) for z in range(len(g[0]))))
            inr = [p for p in ps if lo <= p <= hi]
            if best is None or len(inr) > len(best[1]):
                best = (root, inr)
        def mark(p):
            n = D.name_of(p)
            if p % 12 in htones:
                return n.upper()
            clash = any((p - t) % 12 in (1, 11) for t in htones)
            return n.lower() + ("*" if clash else "")
        rank = 0 if (pc == hpc and q == {"m7": "m9", "maj7": "maj9", "7": "9"}.get(hq)) else (1 if (pc == hpc and q == hq) else 2)
        rows.append((rank, len(tones - htones), D.NAMES[pc] + q, best[0], " ".join(mark(p) for p in best[1])))
rows.sort()
for r in rows:
    print("%-7s root %-3s  %s" % (r[2], D.name_of(r[3]), r[4]))
