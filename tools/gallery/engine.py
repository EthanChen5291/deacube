"""DeaCube's note rules for KEYBOARD islands and MOONS, mirrored from the C# for the MIDI importer (python3 stdlib only).

The voice table is read from AudioCube_Unity/Assets/DeaCube/Audio/SynthBank.cs itself (the D(...) / V(...) rows), so a voice added there is
known here. Mirrored rules (keep in sync with the C#):
  VoiceRules.FoldMelody / MelodyWindow   a keyboard key's pitch (the group's octave shift, the melody window 48..96 shifted by the register)
  VoiceRules.Layered                     an octave layer cube sounds 12 x layer from its folded pitch
  VoiceRules.Resolve rule 7              velocity: SynthBank.Velocity(volume x energy x ghost, beat weight + accent) + humanise
  VoiceRules.DrumPiece(x, z, rows, kit)  a Moon's piece (v9: its own kit; column 5 = the row's alternate)
  KeyBlock keyboards                     tile.midi = lowest key + k + 12 x register
"""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SYNTHBANK = os.path.join(REPO, "AudioCube_Unity", "Assets", "DeaCube", "Audio", "SynthBank.cs")

TPB = 24                 # ProjectConfig.TicksPerBeat
BAR = 96                 # ticks in a 4/4 bar
MIN_TICKS, MAX_TICKS = 3, 192   # AudioCube.ClampTicks
MELODY_LOW, MELODY_HIGH = 48, 96   # VoiceRules.MelodyLow / MelodyHigh
GROUP_KEYS, GROUP_PAD, GROUP_LEAD, GROUP_BASS, GROUP_BELLS, GROUP_STRINGS, GROUP_CHOIR, GROUP_PIANO, GROUP_DRUMS = 0, 2, 3, 4, 5, 6, 7, 8, 9
SUSTAINING = {GROUP_PAD, GROUP_STRINGS, GROUP_CHOIR}
MOON_COLS, MOON_ROWS = 6, 4      # ProjectConfig.MoonCols / MoonRows
DRUM_KIT = [36, 38, 42, 46, 39, 37, 45, 48, 49, 51, 54, 56, 75, 80]   # SynthBank.DrumKit (the standard rows)
ENERGY_SCALE = [0.6, 0.8, 1.0, 1.15]


class Voice:
    """One synth slot: a voice of an instrument group (SynthBank.Defs)."""
    def __init__(self, group, voice, caption, preset, bank, patch, low, high, octave_shift, vmin, vmax, drums):
        self.group, self.voice, self.caption, self.preset = group, voice, caption, preset
        self.bank, self.patch, self.low, self.high = bank, patch, low, high
        self.octave_shift, self.vmin, self.vmax, self.drums = octave_shift, vmin, vmax, drums

    def __repr__(self):
        return "Voice(%d.%d %s)" % (self.group, self.voice, self.caption)


def load_voices(path=SYNTHBANK):
    """Every voice of SynthBank.Defs: {(group, caption): Voice}; voices are numbered per group in table order (voice 0 = the D row)."""
    src = open(path, encoding="utf-8").read()
    body = src[src.index("static InstrumentDef[] Build()"):src.index("static readonly int[] channelOf")]
    d_re = re.compile(r'D\("([^"]+)",\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(-?\d+),\s*[\d.]+f,\s*[\d.]+f,\s*[\d.]+f,\s*[\d.]+f,\s*(true|false),'
                      r'\s*(\d+),\s*(\d+),\s*(\d+),\s*"([^"]+)",\s*"([^"]+)"\)')
    v_re = re.compile(r'V\(l,\s*(\d+),\s*"([^"]+)",\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*[\d.]+f,\s*[\d.]+f,\s*[\d.]+f,\s*[\d.]+f,\s*"([^"]+)"\)')
    rows = [ln for ln in body.split("\n") if 'D("' in ln or "V(l," in ln]
    voices, base, count = {}, {}, {}
    for row in rows:
        m = d_re.search(row)
        if m:
            name, bank, patch, low, high, octs, drums, vmin, vmax, group, caption, preset = m.groups()
            g = int(group)
            v = Voice(g, 0, caption, preset, int(bank), int(patch), int(low), int(high), int(octs), int(vmin), int(vmax), drums == "true")
            base[g] = v
            count[g] = 1
            voices[(g, caption)] = v
            continue
        m = v_re.search(row)
        if m:
            group, caption, bank, patch, low, high, preset = m.groups()
            g = int(group)
            b = base[g]
            v = Voice(g, count[g], caption, preset, int(bank), int(patch), int(low), int(high), b.octave_shift, b.vmin, b.vmax, b.drums)
            count[g] += 1
            voices[(g, caption)] = v
    if not voices:
        raise RuntimeError("no voices read from " + path)
    return voices


# ---------------------------------------------------------------- keyboards (VoiceRules.FoldMelody)
def melody_window(voice, reg):
    """VoiceRules.MelodyWindow: 48..96 (the bass group from its register's bottom) shifted by reg octaves, kept inside 21..108."""
    reg = max(-2, min(2, reg))
    low = (min(MELODY_LOW, min(voice.low, voice.high)) if voice.group == GROUP_BASS else MELODY_LOW) + 12 * reg
    high = MELODY_HIGH + 12 * reg
    while low < 21:
        low += 12
        high += 12
    while high > 108:
        low -= 12
        high -= 12
    return low, high


def fold_melody(voice, midi, reg):
    """VoiceRules.FoldMelody (a pitched voice)."""
    if voice.drums:
        return max(0, min(127, midi))
    midi += 12 * voice.octave_shift
    low, high = melody_window(voice, reg)
    while midi < low:
        midi += 12
    while midi > high:
        midi -= 12
    return max(0, min(127, midi))


def layered(folded, layer):
    """VoiceRules.Layered: the folded note moved by `layer` octaves, kept inside 21..108 by octaves."""
    if layer == 0:
        return folded
    m = folded + 12 * max(-2, min(2, layer))
    while m > 108:
        m -= 12
    while m < 21:
        m += 12
    return m


def key_sound(voice, tile, reg, layer=0, lift=False):
    """The pitch a key of tile pitch `tile` plays on a keyboard of register `reg` (no stack, no transpose, octave 0)."""
    return layered(fold_melody(voice, tile + (12 if lift else 0), reg), layer)


def key_for(voice, sound, reg, layer=0, lift=False):
    """The tile pitch whose key sounds `sound` (None when the register window cannot hold it as it is)."""
    base = sound - 12 * layer
    low, high = melody_window(voice, reg)
    if not (low <= base <= high):
        return None
    tile = base - 12 * voice.octave_shift - (12 if lift else 0)
    return tile if key_sound(voice, tile, reg, layer, lift) == sound else None


# ---------------------------------------------------------------- velocity (VoiceRules.Resolve rule 7, SynthBank.Velocity)
def synth_velocity(voice, volume01, accent01):
    v = max(0.0, min(1.0, volume01))
    a = max(0.0, min(1.0, accent01))
    vel = voice.vmin + (voice.vmax - voice.vmin) * (v ** 0.7)
    vel += a * (127.0 - vel)
    return max(1, min(127, int(round(vel))))


def beat_weight(local_tick):
    """VoiceRules: 0.30 on a bar's downbeat, 0.12 on another beat, 0 off the beat (the step's start inside its window)."""
    if local_tick % TPB:
        return 0.0
    return 0.30 if (local_tick // TPB) % 4 == 0 else 0.12


def pitched_velocity(voice, volume, mod, local_tick, energy=2):
    """The velocity a pitched note plays at, humanise left out (it adds -6..+6)."""
    ghost = 0.45 if mod == 2 else 1.0
    acc = beat_weight(local_tick) + (0.35 if mod == 3 else 0.0)
    return synth_velocity(voice, volume * ENERGY_SCALE[energy] * ghost, acc)


def drum_velocity(voice, volume, mod, local_tick, x, z, energy=2):
    """VoiceRules rule 1 on a Moon: row 0 (the kick row) is 100 (+10 accented); the rest by column loudness x the cube's volume."""
    if z == 0:
        return 100 + (10 if mod == 3 else 0)
    col01 = 0.55 + 0.09 * max(0, min(5, x))
    ghost = 0.45 if mod == 2 else 1.0
    acc = beat_weight(local_tick) + (0.35 if mod == 3 else 0.0)
    return synth_velocity(voice, col01 * volume * ENERGY_SCALE[energy] * ghost, acc)


# ---------------------------------------------------------------- Moons (VoiceRules.DrumPiece)
def drum_for_row(row, rows=MOON_ROWS):
    if rows > 0:
        row = ((row % rows) + rows) % rows
    return DRUM_KIT[row % len(DRUM_KIT)]


def drum_piece(x, z, rows=MOON_ROWS, kit=None):
    """VoiceRules.DrumPiece (v9: kit[row]; column 5 = kit[rows + row] when > 0; no kit = the standard rows + clap / open hat / ride)."""
    if not kit:
        piece = drum_for_row(z, rows)
        if x >= 5 and 1 <= z <= 3:
            piece = 39 if z == 1 else (46 if z == 2 else 51)
        return piece
    r = max(1, rows)
    zz = ((z % r) + r) % r
    piece = kit[zz] if zz < len(kit) and kit[zz] > 0 else drum_for_row(zz, r)
    if x >= 5 and r + zz < len(kit) and kit[r + zz] > 0:
        piece = kit[r + zz]
    return max(0, min(127, piece))


# ---------------------------------------------------------------- Rhythm.Hash (the humanise and the 0..8 ms lateness)
def rhythm_hash(a, b, c):
    h = 2166136261
    for x in (a, b, c):
        h = ((h ^ (x & 0xFFFFFFFF)) * 16777619) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 0x5bd1e995) & 0xFFFFFFFF
    h ^= h >> 15
    return h
