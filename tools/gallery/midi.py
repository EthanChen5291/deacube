"""A small Standard MIDI File reader (python3 stdlib only).

read(path) -> Song with .division (ticks per quarter), .tempos [(tick, us_per_quarter)], .timesigs [(tick, num, den)],
.tracks [Track(name, notes)], Note(pitch, start, end, vel, channel) in ticks. Running status, meta and sysex events
are handled; a note-on with velocity 0 is a note-off; an unmatched note-on is closed at the track's last tick.
Used by analyze.py to measure the Japanese corpus (read only: the files stay where they are).
"""
import struct


class Note:
    __slots__ = ("pitch", "start", "end", "vel", "channel")

    def __init__(self, pitch, start, end, vel, channel):
        self.pitch, self.start, self.end, self.vel, self.channel = pitch, start, end, vel, channel

    def __repr__(self):
        return "Note(%d, %d-%d, v%d)" % (self.pitch, self.start, self.end, self.vel)


class Track:
    def __init__(self):
        self.name = ""
        self.notes = []
        self.programs = []


class Song:
    def __init__(self):
        self.division = 480
        self.tempos = []
        self.timesigs = []
        self.keysigs = []
        self.tracks = []

    def bpm(self):
        """The first tempo in beats per minute (120 when the file declares none)."""
        return 60000000.0 / self.tempos[0][1] if self.tempos else 120.0


def _varlen(data, i):
    v = 0
    while True:
        b = data[i]
        i += 1
        v = (v << 7) | (b & 0x7F)
        if not b & 0x80:
            return v, i


def _track(data, song):
    tr = Track()
    i, tick, status = 0, 0, 0
    open_notes = {}
    while i < len(data):
        delta, i = _varlen(data, i)
        tick += delta
        b = data[i]
        if b & 0x80:
            status = b
            i += 1
        # else: running status, the data byte stays where it is
        if status == 0xFF:
            kind = data[i]
            i += 1
            ln, i = _varlen(data, i)
            body = data[i:i + ln]
            i += ln
            if kind == 0x03:
                tr.name = body.decode("latin-1", "replace").strip()
            elif kind == 0x51 and ln == 3:
                song.tempos.append((tick, (body[0] << 16) | (body[1] << 8) | body[2]))
            elif kind == 0x58 and ln >= 2:
                song.timesigs.append((tick, body[0], 2 ** body[1]))
            elif kind == 0x59 and ln == 2:
                song.keysigs.append((tick, struct.unpack("b", body[0:1])[0], body[1]))
            elif kind == 0x2F:
                break
            status = 0
            continue
        if status in (0xF0, 0xF7):
            ln, i = _varlen(data, i)
            i += ln
            status = 0
            continue
        hi, ch = status & 0xF0, status & 0x0F
        if hi in (0xC0, 0xD0):
            if hi == 0xC0:
                tr.programs.append((tick, ch, data[i]))
            i += 1
            continue
        a, c = data[i], data[i + 1]
        i += 2
        if hi == 0x90 and c > 0:
            key = (ch, a)
            if key in open_notes:   # a re-strike before the note-off: close the sounding one
                s, v = open_notes.pop(key)
                tr.notes.append(Note(a, s, tick, v, ch))
            open_notes[key] = (tick, c)
        elif hi == 0x80 or (hi == 0x90 and c == 0):
            key = (ch, a)
            if key in open_notes:
                s, v = open_notes.pop(key)
                tr.notes.append(Note(a, s, max(tick, s + 1), v, ch))
    for (ch, a), (s, v) in open_notes.items():
        tr.notes.append(Note(a, s, max(tick, s + 1), v, ch))
    tr.notes.sort(key=lambda n: (n.start, n.pitch))
    return tr


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] != b"MThd":
        raise ValueError("not a MIDI file: " + path)
    hl = struct.unpack(">I", data[4:8])[0]
    fmt, ntrks, div = struct.unpack(">HHH", data[8:14])
    song = Song()
    if div & 0x8000:
        raise ValueError("SMPTE time division is not supported")
    song.division = div
    i = 8 + hl
    while i + 8 <= len(data) and len(song.tracks) < ntrks:
        cid = data[i:i + 4]
        ln = struct.unpack(">I", data[i + 4:i + 8])[0]
        body = data[i + 8:i + 8 + ln]
        i += 8 + ln
        if cid == b"MTrk":
            song.tracks.append(_track(body, song))
    song.tempos.sort()
    song.timesigs.sort()
    return song
