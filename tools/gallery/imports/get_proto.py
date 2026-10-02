"""get proto — the user's own MIDI (get_proto-2.mid, at the repository root), made a gallery song by midi_song.py.

Measured (midi_song.py, the analysis in docs/deacube-gallery.md): 960 ticks a quarter, one tempo (370370 us = 162 bpm), no time signature (4/4),
11 tracks, 4802 notes, no controllers or pitch bends (velocities are the only dynamics). The music sits an EIGHTH LATE against the file's bar
lines: shifted 480 ticks earlier every onset lands on the 24-tick grid (the sax's triplet 8ths a MIDI tick off), chords change on beats 1 and 3,
the kick plays every beat, the snare 2 and 4. The harmony is one 4-bar circle of fifths in B minor (| Em9 A | Dmaj9 Gmaj7 | C#o F#m7 | Bm |, a
chord every 2 beats) under every section; the sections differ by their voices. Form (shifted bars): 0 intro (strings) | 1-8 A (e.piano, piano,
bass, flute, drums) | 9-16 A' (+ sax, square) | 17-24 B (sax riff, music box, half-note bass, busier kit; strings in 24) | 25-32 C (sax, flute,
music box) | 33-40 A | 41-48 D (no bass; the sax with a written-out echo, flute soft, square) | 49-56 B | 57-64 C | 65-72 C' | 73-80 E (the
"Scifi" fx carries the C tune) | 81-88 outro (e.piano, piano, flute, music box).
"""

# track index (midi.read order) -> name: 0 Saxophone, 1 Electric Piano, 2 Grand Piano, 3 Synth Bass (Classic), 4 Flute, 5 8-Bit Square,
# 6 Music Box, 7 Scifi, 8 Electric Drum Kit, 9 Strings, 10 8-Bit Triangle

# (first bar, bars a pass[, passes]): the intro bar, then a column per four-bar section; where a section is played twice EXACTLY by every voice
# (C at 25-32, E at 73-80: the sax's one note at bar 73 plays in the first pass only) the column rides a repeat belt (2 passes) instead of a twin
COLUMNS = [(0, 1)] + [(1 + 4 * k, 4) for k in range(6)] + [(25, 4, 2)] + [(33 + 4 * k, 4) for k in range(10)] + [(73, 4, 2)] + [(81, 4), (85, 4)]

#            intro  A    A    A'    A'    B    B    C x2  A    A    D    D    B    B    C    C    C'    C'    E x2  outro outro
LETTERS = ["I", "A", "A", "A'", "A'", "B", "B", "C", "A", "A", "D", "D", "B", "B", "C", "C", "C'", "C'", "E", "O", "O"]
# SongManager section roles: 1 intro, 2 verse, 3 chorus, 4 bridge, 5 drop, 6 outro
ROLES = [1, 2, 2, 2, 2, 4, 4, 3, 2, 2, 5, 5, 4, 4, 3, 3, 3, 3, 5, 6, 6]

CONFIG = dict(
    id="get-proto",
    title="get proto",
    after="your own midi · get_proto-2",
    blurb="your song, every voice in its lane",
    order=7,
    midi="get_proto-2.mid",
    shift=480,                       # the file's music sits an 8th late
    bpm=162,
    key_pc=11, minor=True,           # B minor
    vibe=3,                          # moonlit (Bm9)
    hook=25,                         # the first C section
    columns=COLUMNS,
    letters=LETTERS,
    roles=ROLES,
    lane_pitch=5.0,                  # (unused: lanes below)
    # lane z's: the bass lane holds 4-row chord grids (6.06 deep), the keyboards' lanes 5 apart behind it (a keyboard is 2.7 deep, LaneGap 1.8)
    lanes=[0.0, 8.0, 13.0, 18.0, 23.0, 28.0, 33.0],
    voices={
        # lane 0 low: the bass (synth bass; its lowest notes C#1 / D1 lie below the bass window, 28: they fold up an octave)
        "bass":   dict(track=3, group=4, caption="synth bass", lane=0,
                       # a chord grid per bar where the bar's notes fit it (the 8th-note sections): the bar's harmony as DeaCube chords (the bass plays
                       # the 5th then the root of its 2nd chord: E-A, D-G, C#-F#, B), each repeated 8th walking its pitch's tiles as a ladder
                       grid_chords=["A7", "Gmaj7", "F#m7", "Bm7"], grid_from=1),
        # lane 1 harmony: the piano's triads (register 0; -1 where the break's low E2 plays)
        "piano":  dict(track=2, group=8, caption="grand", lane=1),
        # lane 2 colour: the e.piano's octaves (the upper octave on a sky layer) and the strings' doubled triads (the lower on a deep layer)
        "epiano": dict(track=1, group=0, caption="e.piano", lane=2, octave_layer=1),
        "strings": dict(track=9, group=6, caption="tremolo", lane=2, octave_layer=-1),
        # lane 3 lead: the sax (GM soprano sax -> alto sax)
        "sax":    dict(track=0, group=3, caption="alto sax", lane=3),
        # lane 4 high: the flute's dyads
        "flute":  dict(track=4, group=3, caption="flute", lane=4,
                       bar_registers={41: [1], 45: [1], 65: [1], 69: [1]}),   # D and C': a register up (the same notes), a tower at the climaxes
        # lane 5 sparkle: the music box, the 8-bit square (its 32nd octave flicker = the lift sticker, register +1), the 8-bit triangle (ocarina)
        "mbox":   dict(track=6, group=5, caption="music box", lane=5),
        "square": dict(track=5, group=3, caption="square", lane=5, flicker=True, registers=[1, 0, 2]),
        "tri":    dict(track=10, group=3, caption="ocarina", lane=5),
        # lane 6 fx: the "Scifi" track (GM FX 4 Atmosphere: the new FX colour)
        "scifi":  dict(track=7, group=10, caption="atmosphere", lane=6),
    },
    drums=dict(
        track=8, caption="electronic",
        # Moon kit: rows kick 36, snare 38, pedal hat 44, electric snare 40; their column-5 alternates crash 49, clap 39, tambourine 54, claves 75
        kit=[36, 38, 44, 40, 49, 39, 54, 75],
        # one Moon per 8-bar phrase: (first bar, end bar); each starts a column
        moons=[(1 + 8 * k, 9 + 8 * k) for k in range(11)],
    ),
    mix={},
    tone=1.0, space=1.0,
    deck=[(52, [0, 3, 7, 10, 14], "Em9"), (55, [0, 4, 7, 11], "Gmaj7"), (54, [0, 3, 7, 10], "F#m7")],
)


# ================================================================== round 2 (midi_grid.py): the song as a DeaCube player builds it
# The user (2026-10-01, on round 1's keyboards): "you literally just pasted in the MIDI onto pianos ... the point of deacube is to abstract away
# the piano". Round 2: a chord card per bar, one lane per voice on chord grids, the devices for the song's moments (docs/deacube-gallery.md).
CONFIG2 = dict(
    id="get-proto", title="get proto", after="your own midi · get_proto-2", blurb="your song, every voice in its lane", order=7,
    midi="get_proto-2.mid", shift=480, bpm=162, key_pc=11, minor=True, vibe=3, hook=25,
    # harmony: the loop's cards (bar 1 = A9), chosen for the bass's root-fifth (E-A, D-G, C#-F#, B) and the most note-time on their tiles;
    # colour cards where the song turns: the intro's swell (A major), C''s turnaround into E (C#7, F#7)
    loop_cards=["A9", "Gmaj9", "F#m7", "Bm9"], loop_from=1,
    colour_cards={0: "Amaj7", 71: "C#7", 72: "F#7"},
    deck=["Bm9", "A9", "Gmaj9", "F#m7", "Amaj7", "C#7", "F#7"],
    # sections: an intro bar, then per 8-bar phrase one 4-bar column on a x2 belt (vary where the halves differ), C' as one-bar columns
    sections=[
        dict(letter="I", role=1, bar0=0, bars=1),
        dict(letter="A", role=2, bar0=1, bars=4, passes=2, vary=dict(harmony=1)),
        dict(letter="A'", role=2, bar0=9, bars=4, passes=2, vary=dict(lead=1, high=1)),
        dict(letter="B", role=4, bar0=17, bars=4, passes=2, vary=dict(lead=1), repeat1=dict(bass=[3], harmony=[3], lead=[3], high=[3])),
        dict(letter="C", role=3, bar0=25, bars=4, passes=2),
        dict(letter="A", role=2, bar0=33, bars=4, passes=2, vary=dict(harmony=1)),
        dict(letter="D", role=5, bar0=41, bars=4, passes=2, vary=dict(lead=1)),
        dict(letter="B", role=4, bar0=49, bars=4, passes=2, vary=dict(lead=1), repeat1=dict(bass=[3], harmony=[3], lead=[3], high=[3])),
        dict(letter="C", role=3, bar0=57, bars=4, passes=2, vary=dict(lead=1)),
        dict(letter="C'", role=3, bar0=65, bars=4, columns="bars"),
        dict(letter="C'", role=3, bar0=69, bars=4, columns="bars"),
        dict(letter="E", role=5, bar0=73, bars=4, passes=2),
        dict(letter="O", role=6, bar0=81, bars=4, passes=2, vary=dict(harmony=2, high=2)),
    ],
    # lanes front to back: z (a 5-row grid is 7.2 deep, lanes 1.8 apart), register (bass lowered, the high lane raised: a tower on its turn)
    lanes=dict(bass=dict(z=0.0, reg=-1), harmony=dict(z=9.0, reg=0), lead=dict(z=18.0, reg=0), high=dict(z=27.0, reg=1), fall=dict(z=36.0, reg=0)),
    voices=dict(
        bass=dict(track=3, group=4, caption="synth bass", lane="bass", kind="line", sphere=False),
        piano=dict(track=2, group=8, caption="grand", lane="harmony", kind="triads"),
        epiano=dict(track=1, group=0, caption="e.piano", lane="harmony", kind="octaves", sphere=True),
        strings=dict(track=9, group=6, caption="tremolo", lane="harmony", kind="pad"),
        sax=dict(track=0, group=3, caption="alto sax", lane="lead", kind="line", sphere=True),
        flute=dict(track=4, group=3, caption="flute", lane="high", kind="dyads"),
        mbox=dict(track=6, group=5, caption="music box", lane="high", kind="dyads"),
        square=dict(track=5, group=3, caption="square", lane="high", kind="flicker"),
        tri=dict(track=10, group=3, caption="ocarina", lane="high", kind="line", sphere=True),
        scifi=dict(track=7, group=10, caption="atmosphere", lane="high", kind="line", sphere=True),
    ),
    # stairs: the runs falling into the next chord. bar = the bar the run plays in, cover = the beats of that bar it replaces
    stairs=[
        # the flute's diminished fall at the end of a phrase's half (G6 E6 C#6 A#5 over F#5..): spark stairs over the dominant A9 = C# E G A#,
        # in the lane behind the high lane, as long as the column (its steps drawn in the last measure)
        dict(bar=4, voice="flute", lane="fall", type=2, steps=4, rate=12, lead=True, island_bars=4, reg=0, cover=(2.0, 4.0)),
        dict(bar=36, voice="flute", lane="fall", type=2, steps=4, rate=12, lead=True, island_bars=4, reg=0, cover=(2.0, 4.0)),
        dict(bar=44, voice="flute", lane="fall", type=2, steps=4, rate=12, lead=True, island_bars=4, reg=0, cover=(2.0, 4.0)),
        dict(bar=76, voice="flute", lane="fall", type=2, steps=4, rate=12, lead=True, island_bars=4, reg=0, cover=(2.0, 4.0)),
        dict(bar=84, voice="flute", lane="fall", type=2, steps=4, rate=12, lead=True, island_bars=4, reg=0, cover=(2.0, 4.0)),
        # the sax's scale runs falling into the next chord: stairs in the lead lane (one measure; at a column's start in A' and D, every run of
        # C' where each bar is a column); the register that brings the run nearest the MIDI's (the game picks the run from the column's top note)
        dict(bar=9, voice="sax", lane="lead", type=1, steps=5, rate=12, lead=True, island_bars=1, reg="auto", cover=(1.5, 4.0)),
        dict(bar=41, voice="sax", lane="lead", type=1, steps=5, rate=12, lead=True, island_bars=1, reg="auto", cover=(1.5, 4.0)),
        dict(bar=65, voice="sax", lane="lead", type=1, steps=5, rate=12, lead=True, island_bars=1, reg="auto", cover=(1.5, 4.0)),
        dict(bar=66, voice="sax", lane="lead", type=1, steps=5, rate=12, lead=True, island_bars=1, reg="auto", cover=(1.5, 4.0)),
        dict(bar=67, voice="sax", lane="lead", type=1, steps=4, rate=12, lead=True, island_bars=1, reg="auto", cover=(2.0, 4.0)),
        dict(bar=69, voice="sax", lane="lead", type=1, steps=4, rate=12, lead=True, island_bars=1, reg="auto", cover=(2.0, 4.0)),
        dict(bar=72, voice="sax", lane="lead", type=1, steps=5, rate=12, lead=True, island_bars=1, reg="auto", cover=(1.5, 4.0)),
    ],
    # the music box doubling another voice an octave (or two) up: that voice's cube copied to an octave layer, in the music box's colour
    doubles=[dict(voice="mbox", of="sax", bars=(25, 33)), dict(voice="mbox", of="sax", bars=(57, 65)), dict(voice="mbox", of="epiano", bars=(81, 89))],
    # long grids: in C' (one-bar columns) the piano is one cube on the section's first grid, extended over the next three (sections 9, 10)
    long=dict(piano=[9, 10]),
    # the climax: the sax's cubes in D climb (their highest peak up to the next chord tone)
    climbs=[dict(voice="sax", bars=(41, 49), n=1)],
    # notes left out: the sax's one note in E (bar 73, the landing of C''s last run): E's four lanes are bass, harmony, high and the fall
    drops=[dict(voice="sax", bars=(73, 81), why="E's lanes are full (bass, harmony, high, the flute's fall)")],
    launches=[0, 24, 56],
    contour_voices=["sax", "flute"],
    drums=dict(track=8, caption="electronic", kit=[36, 38, 44, 40, 49, 39, 54, 75], moons=[(1 + 8 * k, 9 + 8 * k) for k in range(11)]),
)


# Round 3 (the user on round 2: "it's better but it should still be more creative ... more various with the grids ... a way to override certain
# cube tiles to lower them/raise them by a note ... the transitions should be cooler ... a visual for a riser ... piano could be used for the
# main melody if its too complicated to use grids ... the parts where the part is descending for a measure should be steps"):
#   every descending measure a STAIRS island (any voice; found by midi_grid.descending_run), the lead lane in FRONT, NUDGES for the notes a
#   card has no tile for, a device signature per section, LAUNCHES at the 14 crash bars, REWIND on the hook, 1-2-bar fragments as own grids.
CONFIG3 = dict(CONFIG2)
CONFIG3.update(
    round=3,
    title="get proto",
    nudges=True,
    nudge_voices=["sax", "flute", "mbox", "square", "tri", "scifi", "epiano"],
    # front to back: the lead (and its stairs, and any voice's fall while the lead rests), the bass (lowered), the harmony, the high lane (raised)
    lanes=dict(lead=dict(z=0.0, reg=0), bass=dict(z=9.0, reg=-1), harmony=dict(z=18.0, reg=0), high=dict(z=27.0, reg=1)),
    voices=dict(
        bass=dict(track=3, group=4, caption="synth bass", lane="bass", kind="line", sphere=False),
        piano=dict(track=2, group=8, caption="grand", lane="harmony", kind="triads"),
        epiano=dict(track=1, group=0, caption="e.piano", lane="harmony", kind="octaves", sphere=True),
        strings=dict(track=9, group=6, caption="tremolo", lane="harmony", kind="pad"),
        sax=dict(track=0, group=3, caption="alto sax", lane="lead", kind="line", sphere="auto", pedal=True),
        flute=dict(track=4, group=3, caption="flute", lane="high", kind="dyads"),
        mbox=dict(track=6, group=5, caption="music box", lane="high", kind="dyads"),
        square=dict(track=5, group=3, caption="square", lane="high", kind="flicker"),
        # the ocarina plays two 1-bar fragments (bars 4 and 36): its own short grid in the free lead lane (it pops up for its bar)
        tri=dict(track=10, group=3, caption="ocarina", lane="lead", kind="line", sphere=True),
        scifi=dict(track=7, group=10, caption="atmosphere", lane="high", kind="line", sphere=True),
    ),
    # sections (a section is 4 measures; x2 = its belt / rewind replays it): each its own device signature (docs: the round-3 recipe)
    sections=[
        dict(letter="I", role=1, bar0=0, bars=1, energy=3),                                                    # the swell: a big launch
        dict(letter="A", role=2, bar0=1, bars=4, passes=2, shape="zigzag", energy=1),                          # ladders on a belt
        dict(letter="A'", role=2, bar0=9, bars=4, passes=2, style="rewind", vary=dict(lead=1), shape="diagonal", energy=2),
        dict(letter="B", role=4, bar0=17, bars=4, passes=2, lead="keys", shape="spiral", energy=1),            # the lead keyboard
        dict(letter="C", role=3, bar0=25, bars=4, passes=2, shape="mirror", energy=3),                         # belt + echo layers
        dict(letter="A", role=2, bar0=33, bars=4, passes=2, vary=dict(harmony=1), shape="pump", energy=1),
        dict(letter="D", role=5, bar0=41, bars=4, passes=2, style="rewind", echo=dict(sax=2), shape="ladder", energy=3,
             lanes=dict(square="bass"), lane_reg=dict(bass=1)),                                                # no bass in D: the square's tower
        dict(letter="B", role=4, bar0=49, bars=4, columns="bars", shape="pump", energy=2),                     # the sax's pedal: a long grid
        dict(letter="B", role=4, bar0=53, bars=4, passes=1, lead="keys", shape="zigzag", energy=2),
        dict(letter="C", role=3, bar0=57, bars=4, passes=2, vary=dict(lead=1), shape="mirror", energy=3),
        dict(letter="C'", role=3, bar0=65, bars=4, columns="bars", shape="ladder", energy=2),                  # stairs on every falling bar
        dict(letter="C'", role=3, bar0=69, bars=4, columns="bars", shape="ladder", energy=2),                  # the turnaround cards
        dict(letter="E", role=5, bar0=73, bars=4, passes=2, vary=dict(high=1), shape="spiral", energy=3,
             lanes=dict(scifi="lead"), raise_=None),
        dict(letter="O", role=6, bar0=81, bars=4, passes=2, vary=dict(harmony=2, high=2), shape="ladder", energy=[3, 2, 1, 0]),
    ],
    # long grids: C''s piano (one cube walks the section's chords), the sax's PEDAL in B (one cube rocking F#-E on one tile over every chord)
    long=dict(piano=[10, 11], sax=[7]),
    stairs_voices=["sax", "flute", "mbox", "square", "scifi", "bass", "piano", "epiano", "tri"],
    stairs_style=dict(flute=dict(prefer=2), mbox=dict(prefer=2), square=dict(prefer=1), sax=dict(prefer=1), scifi=dict(prefer=1)),
    doubles=[dict(voice="mbox", of="sax", bars=(25, 33)), dict(voice="mbox", of="sax", bars=(57, 65)), dict(voice="mbox", of="epiano", bars=(81, 89))],
    climbs=[dict(voice="sax", bars=(41, 49), n=1)],
    drops=[],
    # the MIDI's crash cymbals (the phrase starts): a launch lands on each; the strings' swells (into bars 1, 25, 57) are the big ones
    crashes=[1, 5, 9, 17, 25, 33, 37, 41, 49, 57, 65, 69, 73, 81],
    big_crashes=[1, 25, 57],
)
CONFIG3["sections"][12]["raise"] = dict(harmony=1, lead=1)     # E: the higher chorus (its harmony and lead lanes an octave up, raised grids)
CONFIG3["sections"][12]["lane_reg"] = dict(harmony=1, lead=1)
del CONFIG3["sections"][12]["raise_"]
