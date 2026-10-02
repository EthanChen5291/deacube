"""Levels of the gallery test recordings (python3 stdlib): per song the peak / clipping / RMS of the 12 s from bar 0 and of the chorus,
and the voice over its support: the chorus recorded with and without the voice (V5ChecksG writes <id>.wav, <id>_chorus.wav,
<id>_chorus_novoice.wav) -> voice RMS = sqrt(full^2 - rest^2) (uncorrelated parts), ratio voice / rest in dB.
usage: python3 wavstats.py <wav dir> [id ...]
       python3 wavstats.py --solos <dir>     v9: get proto's band and every voice alone (Captures/proto9_wav)
"""
import sys
sys.dont_write_bytecode = True   # no __pycache__ beside the sheets
import math
import os
import struct
import sys
import wave


def read(path):
    w = wave.open(path, "rb")
    n, ch, sw = w.getnframes(), w.getnchannels(), w.getsampwidth()
    raw = w.readframes(n)
    sr = w.getframerate()
    w.close()
    assert sw == 2
    s = struct.unpack("<%dh" % (n * ch), raw)
    return s, ch, sr


def rms_db(s):
    if not s:
        return -999.0
    return 20 * math.log10(max(1e-9, math.sqrt(sum(v * v for v in s) / float(len(s)))) / 32768.0)


def stats(path):
    s, ch, sr = read(path)
    peak = max(abs(v) for v in s)
    clipped = sum(1 for v in s if v >= 32767 or v <= -32768)
    # quarter-second RMS windows: loudness range (p10 / p90)
    win = sr // 4 * ch
    wins = sorted(rms_db(s[i:i + win]) for i in range(0, len(s) - win, win))
    return {"rms": rms_db(s), "peak": 20 * math.log10(max(1, peak) / 32768.0), "clipped": clipped,
            "p10": wins[len(wins) // 10] if wins else 0, "p90": wins[9 * len(wins) // 10] if wins else 0, "raw": s}


def solos(d):
    """v9 (G, get proto): the band's mix and each voice alone (V9ChecksProto writes Captures/proto9_wav/<voice>.wav, mix_C.wav): per file the
    RMS, peak and clipped samples, and each voice's RMS against the band's (a voice far under the band is buried; a peak at 0 dBFS clips)."""
    files = sorted(f for f in os.listdir(d) if f.endswith(".wav"))
    mix = stats(os.path.join(d, "mix_C.wav")) if "mix_C.wav" in files else None
    print("%-10s %8s %8s %6s %14s %10s" % ("file", "rms", "peak", "clip", "p10/p90 (dB)", "vs band"))
    for f in files:
        st = stats(os.path.join(d, f))
        rel = (" %+9.1f" % (st["p90"] - mix["p90"])) if mix and f != "mix_C.wav" else ""
        print("%-10s %8.1f %8.1f %6d %6.1f/%6.1f%s" % (f[:-4], st["rms"], st["peak"], st["clipped"], st["p10"], st["p90"], rel))


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--solos":
        solos(sys.argv[2])
        return
    d = sys.argv[1]
    ids = sys.argv[2:] or sorted(set(f.split("_chorus")[0].replace(".wav", "") for f in os.listdir(d) if f.endswith(".wav")))
    print("%-12s %8s %8s %6s %14s | %8s %8s %8s %10s" % ("song", "rms", "peak", "clip", "p10/p90 (dB)", "chorus", "no-voice", "voice", "voice/rest"))
    for i in ids:
        a = os.path.join(d, i + ".wav")
        if not os.path.exists(a):
            continue
        sa = stats(a)
        line = "%-12s %8.1f %8.1f %6d %6.1f/%6.1f |" % (i, sa["rms"], sa["peak"], sa["clipped"], sa["p10"], sa["p90"])
        c, nv = os.path.join(d, i + "_chorus.wav"), os.path.join(d, i + "_chorus_novoice.wav")
        if os.path.exists(c) and os.path.exists(nv):
            sc, sn = stats(c), stats(nv)
            full = 10 ** (sc["rms"] / 10.0)
            rest = 10 ** (sn["rms"] / 10.0)
            voice = max(1e-12, full - rest)
            vdb = 10 * math.log10(voice)
            line += " %8.1f %8.1f %8.1f %+9.1f" % (sc["rms"], sn["rms"], vdb, vdb - sn["rms"])
        print(line)


if __name__ == "__main__":
    main()
