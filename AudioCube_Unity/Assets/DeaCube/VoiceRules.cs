using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single audio decision point (SPEC §4.3). <see cref="Resolve"/> turns one <see cref="AudioCube.Hit"/> into
/// note events (pure: the only randomness is Rhythm.Hash(pass, node, seed)); <see cref="Dispatch"/> is the only
/// caller of Synth.Note for cubes; <see cref="PreviewEvent"/> / <see cref="AuditionEvent"/> are the only preview path (v5: auditions are no longer
/// gated by <see cref="PreviewAllowed"/>: they play on the synth's preview bus and the song ducks under them).
/// Rules, in order: 1 drums, 2 pitch, 3 stack, 4 bass anchor, 5 register + low-end law, 6 costumes (pad / shadow /
/// echo / ratchet), 7 velocity, 8 timing + gate policy + window clamp, 9 energy 0 hush.
/// Invariant: every offDsp &lt;= windowEndDsp - 5 ms (sustaining slots: - one 16th).
/// v6 (A, SPEC v6 §4.1 / §4.3): a cube sounds through its VOICE's slot (Instruments.SlotOf(instrument, voice)) while the role rules —
/// sustaining, mono, the bass anchor, the register / low-end law, the pad costume, the gate policy — follow the slot's GROUP (SynthBank.GroupOf),
/// so every voice of a group behaves like its voice 0. Keyboard islands (kind 2) play melodies: a key sounds its own pitch (<see cref="FoldMelody"/>:
/// no octave fold inside MIDI 48..96, shifted by the register; no bass anchor; a drum voice plays the kit piece by key) and carries no
/// chord-tone assertion (<see cref="HarmonicCheck"/>). Carried windows (SPEC v6 §2.4) play the tiles AudioCube.TileAt returns and are stamped
/// with the WINDOW's island. <see cref="PressEvent"/> builds a pressed tile's note for Synth.PressNote (louder than the song).
/// v7 (A, SPEC v7 §4.1–§4.3 — "duplicate that cube down an octave or up an octave", "a support harmony of that cube (5ths or 4ths basically)",
/// "a stair-case grid that just is descending notes"): a note sounds tile.midi + transpose + 12 × octave + bend[node] THROUGH ITS FOLD and then
/// 12 × layer (<see cref="Layered"/>: the folds never undo an octave layer — an octave copy sounds exactly one / two octaves from its source; kept
/// inside MIDI 21..108 by octaves). A layer cube never takes the stack's octave lift (it lives in its layer, not in the stack); a BENT note (a harmony
/// voice) plays alone (no bass anchor, no pad tones or follow shadows piling chord tones around it). Stairs (kind 3) and phrases (kind 4) are
/// melodies like keyboards: <see cref="FoldRun"/> keeps a run's shape (one octave shift for the whole run). Every preview / audition / press has an
/// overload taking the cube and node (<see cref="NodeMidi"/>: its layer and bend), and <see cref="HarmonicCheck"/> exempts bent notes, stairs and
/// phrases from the chord-tone law and judges the low-end law before the layer. The launch's landing crash (<see cref="ScheduleLandingCrash"/>) never
/// doubles a fill crash; its riser lives in LaunchRiser.
/// </summary>
public static class VoiceRules
{
    /// <summary>One synth note. <c>owner</c>, <c>winStartDsp</c>/<c>winEndDsp</c> and <c>island</c> are stamped by Resolve/Dispatch for the
    /// harmonic-safety assertions (SPEC 2.9): a cube event's on lies inside its window, its off before windowEnd - 5 ms, and its
    /// pitch class is a chord tone of <c>island</c>. Previews and auditions carry no window (winEndDsp 0).</summary>
    public struct NoteEvent { public int slot, midi, vel; public double onDsp, offDsp; public int owner; public double winStartDsp, winEndDsp; public KeyBlock island;
        /// <summary>v7 (SPEC v7 §4.1 / §4.3): the octave layer of the cube (the note sounds 12 × layer from its folded pitch) and whether the node is BENT
        /// (a harmony voice's pitch, not the tile's) — stamped by Resolve (and the preview builders) for <see cref="HarmonicCheck"/>; <c>fit</c>: the note
        /// comes from a carried window (§21: every carried window is the LONG GRID's — a flow window, Harmony.AdaptPedal keeps a pedal that FITS the
        /// chord) or a varied pass (Harmony.Vary's neighbour fits it): it must fit the chord (a chord tone or a key tension), not be a chord tone.</summary>
        public int layer; public bool bent, fit; }

    /// <summary>Reserved Synth owner ids (§4): song events (crash), stutter, cancellable previews. Cubes use >= 16.</summary>
    public const int OwnerSong = 7, OwnerStutter = 8, OwnerPreview = 9;

    // v6: the ROLE of a slot is its group (for the v5 slots 0..8 group = slot); compare Grp(slot) with these
    const int SlotKeys = 0, SlotPluck = 1, SlotPad = 2, SlotLead = 3, SlotBass = 4, SlotBells = 5, SlotStrings = 6, SlotChoir = 7, SlotPiano = 8;
    static int Grp(int slot) => SynthBank.GroupOf(slot);
    /// <summary>v6: the melody window of a keyboard island's keys (register 0): a key inside it plays as it is (SPEC v6 §4.3).</summary>
    public const int MelodyLow = 48, MelodyHigh = 96;
    const double WindowEps = 0.005;          // every note-off lands at least this early before the window end
    static readonly float[] RatchetVel = { 1f, 0.82f, 0.68f };

    /// <summary>Pads, strings and choirs (v6: every voice of those groups).</summary>
    public static bool IsSustaining(int slot) { int g = Grp(slot); return g == SlotPad || g == SlotStrings || g == SlotChoir; }
    /// <summary>Leads and basses (v6: every voice of those groups).</summary>
    public static bool IsMono(int slot) { int g = Grp(slot); return g == SlotLead || g == SlotBass; }
    /// <summary>v6: the slot is a voice of the bass group (the low-end law's exception, the anchor rule).</summary>
    public static bool IsBass(int slot) => Grp(slot) == SlotBass;

    /// <summary>Register fold (SynthBank.ClampToRegister) followed by the low-end law: no slot but Bass below 48,
    /// no slot but Bells above 96 (v6: by group). Drum keys pass through unchanged.</summary>
    public static int Fold(int slot, int midi) => Fold(slot, midi, 0);

    /// <summary>
    /// v4 (K's island register, "make them lower or higher"): the same fold with the slot's register window and the low-end law shifted by
    /// <paramref name="reg"/> octaves (the island's register, -2..+2), so every register step is heard as exactly one octave (the plain
    /// fold would pull a shifted note back into the slot's window). The window stays inside MIDI 21..108; drums are unaffected; reg 0 is
    /// the v3 fold bit for bit.
    /// </summary>
    public static int Fold(int slot, int midi, int reg)
    {
        InstrumentDef d = SynthBank.Def(slot);
        int g = d.group;   // v6: the law follows the group; the window is the voice's own
        if (reg == 0 || d.drums)
        {
            midi = SynthBank.ClampToRegister(slot, midi);
            if (d.drums) return midi;
            if (g != SlotBass) while (midi < 48) midi += 12;
            if (g != SlotBells) while (midi > 96) midi -= 12;
            return Mathf.Clamp(midi, 0, 127);
        }
        reg = Mathf.Clamp(reg, -2, 2);
        midi += 12 * d.octaveShift;
        int low = Mathf.Min(d.lowMidi, d.highMidi) + 12 * reg, high = Mathf.Max(d.lowMidi, d.highMidi) + 12 * reg;
        while (low < 21) { low += 12; high += 12; }
        while (high > 108) { low -= 12; high -= 12; }
        while (midi < low) midi += 12;
        while (midi > high) midi -= 12;
        if (midi < low) midi = low;
        int floor = 48 + 12 * reg, ceil = 96 + 12 * reg;   // the low-end law moves with the register too
        if (g != SlotBass) while (midi < floor && midi + 12 <= 108) midi += 12;
        if (g != SlotBells) while (midi > ceil && midi - 12 >= 21) midi -= 12;
        return Mathf.Clamp(midi, 0, 127);
    }

    /// <summary>
    /// v6 (SPEC v6 §4.3, "a keyboard style grid ... for actual melody"): the fold for a KEYBOARD island's key. A melody keeps its octave: after the
    /// group's octave shift (the bass plays an octave down), a note inside the melody window — <see cref="MelodyLow"/>..<see cref="MelodyHigh"/>
    /// (the bass group from its register's bottom), shifted by <paramref name="reg"/> octaves and kept inside MIDI 21..108 — plays exactly as it
    /// is; only a note outside it folds by octaves into it. The voice's own register window is not applied. Drum keys pass through.
    /// </summary>
    public static int FoldMelody(int slot, int midi, int reg)
    {
        InstrumentDef d = SynthBank.Def(slot);
        if (d.drums) return Mathf.Clamp(midi, 0, 127);
        reg = Mathf.Clamp(reg, -2, 2);
        midi += 12 * d.octaveShift;
        int low, high; MelodyWindow(d, reg, out low, out high);
        while (midi < low) midi += 12;
        while (midi > high) midi -= 12;
        return Mathf.Clamp(midi, 0, 127);
    }

    /// <summary>The melody window of a slot at register <paramref name="reg"/>: MelodyLow..MelodyHigh (the bass group from its register's bottom),
    /// shifted by the register and kept inside MIDI 21..108.</summary>
    static void MelodyWindow(InstrumentDef d, int reg, out int low, out int high)
    {
        low = (d.group == SlotBass ? Mathf.Min(MelodyLow, Mathf.Min(d.lowMidi, d.highMidi)) : MelodyLow) + 12 * reg; high = MelodyHigh + 12 * reg;
        while (low < 21) { low += 12; high += 12; }
        while (high > 108) { low -= 12; high -= 12; }
    }

    /// <summary>v6: the fold a tile's note gets — <see cref="FoldMelody"/> on a keyboard island, else <see cref="Fold(int, int, int)"/> with the
    /// island's register. v7: <see cref="FoldRun"/> on a stairs island or a phrase.</summary>
    public static int FoldFor(int slot, int midi, TileInteraction tile) => FoldTile(slot, midi, tile, tile != null ? tile.island : null, 0);

    /// <summary>v7: <see cref="FoldFor(int, int, TileInteraction)"/> of a note carrying <paramref name="extra"/> semitones of its own (a bend, a lift) on top
    /// of the tile's pitch: on a run (stairs / a phrase) they ride on the run's octave shift instead of choosing it.</summary>
    public static int FoldFor(int slot, int midi, TileInteraction tile, int extra) => FoldTile(slot, midi, tile, tile != null ? tile.island : null, extra);

    /// <summary>v7: a MELODY island — a keyboard (kind 2), stairs (3) or a phrase (4): its tiles play their own pitch (the melody folds), with no
    /// chord-tone law, no bass anchor and no pad costume.</summary>
    public static bool IsMelody(KeyBlock kb) => kb != null && (kb.IsKeyboard || kb.IsStairs || kb.IsPhrase);

    /// <summary>v7: the fold of a note on <paramref name="tile"/>: a keyboard key keeps its octave (<see cref="FoldMelody"/>), a stairs / phrase run keeps its
    /// shape (<see cref="FoldRun"/>; <paramref name="extra"/> = the note's own offset — lift, bend, stack — that does not move the run), else the register
    /// fold with <paramref name="island"/>'s register (the window's island: a carried pattern answers to the island it plays on).</summary>
    static int FoldTile(int slot, int midi, TileInteraction tile, KeyBlock island, int extra)
    {
        KeyBlock t = tile != null ? tile.island : null;
        if (t != null)
        {
            if (t.IsKeyboard) return FoldMelody(slot, midi, t.register);
            if (t.IsStairs || t.IsPhrase) return FoldRun(slot, midi, tile, extra);
        }
        return Fold(slot, midi, RegOf(island));
    }

    /// <summary>
    /// v7 (SPEC v7 §4.2, "a run keeps its octave"): the fold of a note on a STAIRS island or a PHRASE — the keyboard's melody window (MelodyLow..
    /// MelodyHigh shifted by the island's register; the group's octave shift; the bass group from its register's bottom) but ONE octave shift for the
    /// whole run: the island's lowest and highest tile, moved by what moves every note alike (the transpose, the cube's octave), decide it — a run that
    /// pokes out of the window moves as a whole and keeps its shape (a walk in the bass range played by a lead climbs an octave, all of it); a run
    /// inside the window plays exactly as it is (FoldMelody's note). <paramref name="extra"/> = the note's own part of <paramref name="midi"/> (a lift, a
    /// bend, the stack): it rides on the run's shift. Kept inside MIDI 21..108 by octaves. Drums pass through.
    /// </summary>
    public static int FoldRun(int slot, int midi, TileInteraction tile, int extra = 0)
    {
        InstrumentDef d = SynthBank.Def(slot);
        if (d.drums) return Mathf.Clamp(midi, 0, 127);
        KeyBlock run = tile != null ? tile.island : null;
        int reg = run != null ? Mathf.Clamp(run.register, -2, 2) : 0;
        int lo, hi;
        if (tile == null || !RunRange(run, out lo, out hi)) return FoldMelody(slot, midi, reg);
        int common = midi - extra - tile.midi + 12 * d.octaveShift;   // what moves every note of the run alike
        int low, high; MelodyWindow(d, reg, out low, out high);
        lo += common; hi += common;
        int shift = 0;
        while (lo + shift < low) shift += 12;
        while (hi + shift > high && lo + shift - 12 >= low) shift -= 12;
        int m = midi + 12 * d.octaveShift + shift;
        while (m > 108) m -= 12;
        while (m < 21) m += 12;
        return m;
    }

    /// <summary>v7: the lowest and highest tile pitch of a run (a stairs island's steps; a phrase's rows, read along its first time cell).</summary>
    static bool RunRange(KeyBlock run, out int lo, out int hi)
    {
        lo = int.MaxValue; hi = int.MinValue;
        if (run == null) return false;
        if (run.IsPhrase && run.cols > 0)
            for (int z = 0; z < run.rows; z++) { var t = run.GetTile(0, z); if (t != null) { if (t.midi < lo) lo = t.midi; if (t.midi > hi) hi = t.midi; } }
        if (lo > hi)
            foreach (var t in run.tiles) if (t != null) { if (t.midi < lo) lo = t.midi; if (t.midi > hi) hi = t.midi; }
        return lo <= hi;
    }

    /// <summary>
    /// v7 (SPEC v7 §2.9 / §4.1, "duplicate that cube down an octave or up an octave"): a note after its fold moved by <paramref name="layer"/> octaves —
    /// an octave copy SOUNDS exactly 12 × layer from its source (the fold ran on the base pitch, so it cannot undo the layer) — kept inside the piano's
    /// MIDI 21..108 by whole octaves (the pitch class never changes: at the very edge a copy stays in its octave rather than become another note).
    /// Layer 0 = the note as it is.
    /// </summary>
    public static int Layered(int folded, int layer)
    {
        if (layer == 0) return folded;
        int m = folded + 12 * Mathf.Clamp(layer, -ProjectConfig.MaxLayer, ProjectConfig.MaxLayer);
        while (m > 108) m -= 12;
        while (m < 21) m += 12;
        return m;
    }

    static int RegOf(KeyBlock island) => island != null ? island.register : 0;

    /// <summary>Drum piece for a tile: row → piece via SynthBank.DrumForRow; column 5 selects the alternate piece for rows 1-3 (clap / open hat / ride).</summary>
    public static int DrumPiece(int gridX, int gridZ, int rows)
    {
        int piece = SynthBank.DrumForRow(gridZ, rows);
        if (gridX >= 5 && gridZ >= 1 && gridZ <= 3) piece = gridZ == 1 ? 39 : (gridZ == 2 ? 46 : 51);
        return piece;
    }

    /// <summary>
    /// v9 (G, the gallery's "get proto": its electronic kit plays pieces the standard rows lack): the drum piece of a tile of a Moon with its own
    /// <paramref name="kit"/> — kit[row] on every column (a missing / 0 entry: the standard row), and on column 5 the row's alternate kit[rows + row]
    /// when the kit has one (&gt; 0; any row, the kick's too: its velocity rule stays the row's). A null / empty kit = <see cref="DrumPiece(int, int, int)"/>
    /// exactly.
    /// </summary>
    public static int DrumPiece(int gridX, int gridZ, int rows, int[] kit)
    {
        if (kit == null || kit.Length == 0) return DrumPiece(gridX, gridZ, rows);
        int r = Mathf.Max(1, rows), z = ((gridZ % r) + r) % r;
        int piece = z < kit.Length && kit[z] > 0 ? kit[z] : SynthBank.DrumForRow(z, r);
        if (gridX >= 5 && r + z < kit.Length && kit[r + z] > 0) piece = kit[r + z];
        return Mathf.Clamp(piece, 0, 127);
    }

    /// <summary>v6: the drum piece a tile plays — <see cref="DrumPiece"/> on a chord island or a Moon; on a keyboard (one row) the key picks the
    /// piece: key k plays SynthBank.DrumKit[k mod 14] (kick, snare, closed hat, open hat, clap, rim, toms, crash, ride, …).</summary>
    public static int DrumPieceOf(TileInteraction tile)
    {
        if (tile == null) return SynthBank.DrumKit[0];
        KeyBlock isl = tile.island;
        if (isl != null && isl.IsKeyboard) { int n = SynthBank.DrumKit.Length; return SynthBank.DrumKit[((tile.gridX % n) + n) % n]; }
        return DrumPiece(tile.gridX, tile.gridZ, isl != null ? isl.rows : 4, isl != null && isl.IsMoon ? isl.kit : null);   // v9 (G): a Moon's own kit
    }

    /// <summary>
    /// The nearest chord-tone row above/below <paramref name="tile"/> (same column) whose interval is at least 3 semitones,
    /// wrapping around the island's rows an octave up/down (<paramref name="octaveOffset"/> receives +-12 for a wrapped row).
    /// Null without an island grid or when no row qualifies. Followers never sit on 2nds (SPEC risk 13).
    /// </summary>
    public static TileInteraction ShadowTile(TileInteraction tile, bool up, out int octaveOffset)
    {
        octaveOffset = 0;
        if (tile == null || tile.island == null) return null;
        KeyBlock kb = tile.island;
        int rows = kb.rows;
        if (rows <= 1) return null;
        int dir = up ? 1 : -1;
        for (int s = 1; s < rows; s++)
        {
            int z = tile.gridZ + dir * s, off = 0;
            if (z >= rows) { z -= rows; off = 12; }
            else if (z < 0) { z += rows; off = -12; }
            var t = kb.GetTile(tile.gridX, z);
            if (t == null) continue;
            if (Mathf.Abs(t.midi + off - tile.midi) >= 3) { octaveOffset = off; return t; }
        }
        return null;
    }

    /// <summary>v7: a note's BASE pitch — the tile + an octave offset (a wrapped row) + the transpose + the cube's octave + the lift sticker +
    /// <paramref name="bend"/> — everything but the octave layer, which is added after the fold (<see cref="Layered"/>; SPEC v7 §2.9: the note sounds
    /// tile.midi + Transpose + 12 × (octave + layer) + bend[node]).</summary>
    static int BaseOf(TileInteraction t, int octaveOffset, AudioCube cube, AudioCube.Hit hit, int bend)
        => t.midi + octaveOffset + SongManager.Transpose + 12 * cube.octave + (hit.lift ? 12 : 0) + bend;

    /// <summary>The chord island whose section controls (energy, fill) apply to this hit: the window's island, or for a Moon cube the island lit at that beat.</summary>
    static KeyBlock SectionIsland(AudioCube cube, AudioCube.Window w, double localBeat, KeyBlock fallback)
    {
        if (!cube.IsOnMoon) return fallback;
        var sm = SongManager.I;
        if (sm == null || sm.Islands.Count == 0) return fallback;
        var anchor = sm.AnchorOf(sm.ActiveColumn(w.start + (float)localBeat));   // v4 R5: the anchor of the column playing at that beat
        if (anchor != null) return anchor;
        int i = sm.ActiveMeasureIndex(w.start + (float)localBeat);
        return i >= 0 && i < sm.Islands.Count ? sm.Islands[i] : fallback;
    }

    static int Push(NoteEvent[] ev, int count, int slot, int midi, int vel, double on, double off)
    {
        if (count >= ev.Length) return count;
        for (int i = 0; i < count; i++) if (ev[i].slot == slot && ev[i].midi == midi && Math.Abs(ev[i].onDsp - on) < 1e-6) return count;   // same key at the same time: once
        ev[count] = new NoteEvent { slot = slot, midi = Mathf.Clamp(midi, 0, 127), vel = Mathf.Clamp(vel, 1, 127), onDsp = on, offDsp = off };
        return count + 1;
    }

    /// <summary>Stamps the window and island every event of this Resolve was derived from (harmonic-safety log, SPEC 2.9); v7: the cube's layer and
    /// whether the node is bent.</summary>
    static int Stamp(NoteEvent[] ev, int count, AudioCube cube, KeyBlock island, double winStartDsp, double winEndDsp, bool bent, bool fit = false)
    {
        for (int i = 0; i < count && i < ev.Length; i++)
        {
            ev[i].owner = cube.owner; ev[i].island = island; ev[i].winStartDsp = winStartDsp; ev[i].winEndDsp = winEndDsp;
            ev[i].layer = cube.layer; ev[i].bent = bent; ev[i].fit = fit;
        }
        return count;
    }

    /// <summary>Per-slot gate policy (§4.1) on top of the cube's gate: Short 0.3 / Normal 0.92 step, Long or tie = until the next hit.
    /// Bass at least 120 ms; sustaining slots at least a beat and never Short; Pluck/Bells/Piano decay within two steps; Bells ignore Long.</summary>
    static double GateSeconds(int slot, Gate g, bool tie, double stepSec, double on, double nextHitDsp, double bps)
    {
        int grp = Grp(slot);   // v6: the gate policy follows the group
        if (IsSustaining(slot) && g == Gate.Short) g = Gate.Normal;
        if (grp == SlotBells && g == Gate.Long) g = Gate.Normal;
        double gate;
        if (tie || g == Gate.Long) gate = double.IsInfinity(nextHitDsp) ? double.PositiveInfinity : Math.Max(0.03, (nextHitDsp - 0.01) - on);
        else gate = (g == Gate.Short ? 0.3 : 0.92) * stepSec;
        if (grp == SlotBass) gate = Math.Max(gate, 0.12);
        if (IsSustaining(slot)) gate = Math.Max(gate, 1.0 / bps);
        if (grp == SlotPluck || grp == SlotBells || grp == SlotPiano) gate = Math.Min(gate, 2.0 * stepSec);
        return gate;
    }

    /// <summary>The one place that clamps a note-off inside its window: off &lt;= windowEnd - 5 ms, sustaining slots release a 16th early, never a zero-length note.</summary>
    static double ClampOff(int slot, double on, double off, double windowEndDsp, double bps)
    {
        double cap = windowEndDsp - WindowEps;
        if (IsSustaining(slot)) cap = Math.Min(cap, windowEndDsp - 0.25 / bps);
        off = Math.Min(off, cap);
        off = Math.Max(off, on + 0.03);
        off = Math.Min(off, windowEndDsp - WindowEps);
        if (off <= on) off = on + 0.02;
        return off;
    }

    static int PadTones(NoteEvent[] ev, int count, AudioCube cube, AudioCube.Hit hit, TileInteraction tile, KeyBlock island, int slot, int vel, double on, double off)
    {
        int rows = island.rows;
        if (rows < 2) return count;
        int v = Mathf.Clamp(Mathf.RoundToInt(vel * 0.65f), 1, 127);
        int z = tile.gridZ, off12 = 0;
        for (int n = 0; n < 2 && n < rows - 1; n++)
        {
            z--;
            if (z < 0) { z += rows; off12 -= 12; }               // below the bottom row: the top row an octave down
            var t = island.GetTile(tile.gridX, z);
            if (t == null) break;
            count = Push(ev, count, slot, Layered(Fold(slot, BaseOf(t, off12, cube, hit, 0), RegOf(island)), cube.layer), v, on, off);   // (never on a melody island)
        }
        return count;
    }

    static int ShadowNote(NoteEvent[] ev, int count, AudioCube cube, AudioCube.Hit hit, TileInteraction tile, bool up, int slot, int vel, double on, double off, int stackLift)
    {
        int o;
        var st = ShadowTile(tile, up, out o);
        if (st == null) return count;
        // v7: the shadow's own fold (a phrase's rows fold as the run), then the cube's layer
        return Push(ev, count, slot, Layered(FoldTile(slot, BaseOf(st, o, cube, hit, 0) + stackLift, st, tile.island, o + (hit.lift ? 12 : 0) + stackLift), cube.layer), vel, on, off);
    }

    /// <summary>
    /// Resolves one hit into events written to <paramref name="outEvents"/>; returns the count (0 = silent).
    /// <paramref name="onDsp"/> is the step's start, <paramref name="windowEndDsp"/> the end of the window that
    /// contains it, <paramref name="nextHitDsp"/> the cube's next firing step in the same window (+inf if none),
    /// <paramref name="stack"/> the other cubes on the tile.
    /// </summary>
    public static int Resolve(AudioCube cube, AudioCube.Window w, TileInteraction tile, AudioCube.Hit hit, int stack, int pass,
                              double onDsp, double stepSec, double windowEndDsp, double nextHitDsp, NoteEvent[] outEvents)
    {
        if (cube == null || tile == null || outEvents == null || outEvents.Length == 0 || !hit.fires) return 0;
        int slot = Instruments.SlotOf(cube.instrument, cube.voice);   // v6: the cube's voice sounds; the rules below follow its group
        int grp = Grp(slot);
        KeyBlock island = w.island != null ? w.island : tile.island;   // v6: a carried window is stamped with (and hushed by) the window's island
        bool keyboard = tile.island != null && tile.island.IsKeyboard;   // v6: a melody key
        bool melody = IsMelody(tile.island);                             // v7: a keyboard, stairs or a phrase (a melody: its own pitch, no anchor / pad)
        double bps = GlobalClock.BeatsPerSecond;
        double localBeat = w.length - (windowEndDsp - onDsp) * bps;
        double windowStartDsp = windowEndDsp - w.length / bps;
        KeyBlock section = SectionIsland(cube, w, localBeat, island);
        int energy = section != null ? section.energy : 2;
        if (energy == 0 && IsSustaining(slot)) return 0;                                   // rule 9: hush silences the pads

        uint h = Rhythm.Hash(pass, hit.node, cube.seed);
        float energyScale = Rhythm.EnergyScale(energy);
        float spot = Performance.SpotlightCube != null && Performance.SpotlightCube != cube ? 0.25f : 1f;
        float ghost = hit.weight == 0 ? 0.45f : 1f;
        double rounded = Math.Round(localBeat);
        int beatsPerBar = Mathf.Max(1, GlobalClock.BeatsPerBar);
        bool onBeat = Math.Abs(localBeat - rounded) < 1e-3;
        bool downbeat = onBeat && (((int)rounded % beatsPerBar) + beatsPerBar) % beatsPerBar == 0;
        float beatWeight = onBeat ? (downbeat ? 0.30f : 0.12f) : 0f;
        float accent01 = beatWeight + (hit.weight == 2 ? 0.35f : 0f);
        int humanise = (int)((h >> 8) % 13u) - 6;
        int r = Mathf.Clamp(hit.ratchet, 1, 3);
        double subSec = stepSec / r;
        double on = onDsp + (h & 0xFFu) * (0.008 / 255.0);                                 // 0..8 ms late, never early
        int count = 0;

        if (SynthBank.Def(slot).drums)
        {
            // rule 1: drums are unpitched; row → piece, column → loudness; the kick is fixed and never humanised (nor jittered)
            int piece = DrumPieceOf(tile);   // v6: a keyboard's key picks the piece
            float col01 = keyboard ? 0.82f : 0.55f + 0.09f * Mathf.Clamp(tile.gridX, 0, 5);
            bool kick = keyboard ? piece == 36 : tile.gridZ == 0;
            int vel0 = kick ? 100 + (hit.weight == 2 ? 10 : 0) : SynthBank.Velocity(slot, col01 * cube.volume * energyScale * ghost * spot, accent01) + humanise;
            double onK = kick ? onDsp : on;
            for (int j = 0; j < r; j++)
            {
                double oj = onK + j * subSec;
                if (oj >= windowEndDsp - 0.02) break;
                count = Push(outEvents, count, slot, piece, Mathf.RoundToInt(vel0 * RatchetVel[j]), oj, ClampOff(slot, oj, oj + 0.1, windowEndDsp, bps));
            }
            return Stamp(outEvents, count, cube, island, windowStartDsp, windowEndDsp, false);
        }

        // rule 2: pitch (octave and lift keep the pitch class). v7: the BASE pitch carries the node's bend (a harmony voice's 4th / 5th that is not a
        // tile); the octave LAYER is added after the fold (rule 5), so an octave copy sounds exactly 12 × layer from its source
        int bend = BendOf(cube, tile, hit.node);   // v7 (H's request): a bend belongs to the node's HOME tile (0 on a carried / flow / varied tile)
        bend += Nudge.Semis(tile.midi, hit.nudge);   // v9 (N): the step's nudge — scale steps of the song key above / below the tile it plays (in key on any tile)
        bool bent = bend != 0;                      // a nudged note is "bent" too: its pitch is not the tile's (no pad tones, shadows, anchor, stack lift around it)
        int layer = cube.layer;
        int midi = BaseOf(tile, 0, cube, hit, bend);
        float velScale = 1f;
        // rule 3: the top cube of a stack plays an octave up and a little louder (v7: never a layer cube — it lives in its layer, not in the stack —
        // nor a bent note: its pitch is not the tile's, there is no unison to lift away from)
        int idx = SequenceMaster.IndexOn(tile, cube);
        int stackLift = 0;
        if (stack >= 1 && (idx < 0 || idx >= stack) && layer == 0 && !bent) { stackLift = 12; velScale *= 1f + 0.25f * Mathf.Log(stack + 1f, 2f); }
        midi += stackLift;
        // rule 4: the bass anchors integer downbeats on 7th/9th tiles to the chord root (root/3rd/5th play as drawn); v6: not on a keyboard's melody;
        // v7: nor on stairs / a phrase (melodies), nor on a bent note (Harmony chose that pitch)
        if (grp == SlotBass && !melody && !bent && downbeat && island != null && (tile.role == ChordRole.Seventh || tile.role == ChordRole.Extension))
            midi = island.chordRootMIDI + SongManager.Transpose + 12 * cube.octave + 12 * island.register;   // v4: in the island's register
        // rule 5: register fold + low-end law (v4: both shifted by the island's register, so "higher / lower" is heard); v6: a keyboard's key
        // keeps the melody's octave (FoldMelody); v7: a stairs / phrase run keeps its shape (FoldRun) — then the octave layer (the fold never undoes it)
        midi = Layered(FoldTile(slot, midi, tile, island, (hit.lift ? 12 : 0) + bend + stackLift), layer);
        // rule 7: velocity
        float volume01 = cube.volume * energyScale * ghost * spot;
        int vel = Mathf.Clamp(Mathf.RoundToInt(SynthBank.Velocity(slot, volume01, accent01) * velScale) + humanise, 1, 127);
        // rule 8: gate (slot policy) - the clamp happens per event below
        double gateSec = GateSeconds(slot, cube.gate, hit.tie, stepSec, on, nextHitDsp, bps);

        // the hit (ratchet: r sub-hits at step/r, velocities 1 / 0.82 / 0.68) with its costumes (rule 6)
        for (int j = 0; j < r; j++)
        {
            double oj = on + j * subSec;
            if (oj >= windowEndDsp - 0.02) break;
            double gj = j < r - 1 ? Math.Min(gateSec, subSec - 0.005) : gateSec;
            double offj = ClampOff(slot, oj, double.IsInfinity(gj) ? double.PositiveInfinity : oj + gj, windowEndDsp, bps);
            int vj = Mathf.Clamp(Mathf.RoundToInt(vel * RatchetVel[j]), 1, 127);
            count = Push(outEvents, count, slot, midi, vj, oj, offj);
            // v7: a bent note plays alone — the costumes that pile chord tones around it (the pad's tones, the follow shadows) would rub against it
            if (grp == SlotPad && island != null && !melody && !bent) count = PadTones(outEvents, count, cube, hit, tile, island, slot, vj, oj, offj);
            if (cube.follow != 0 && !bent)
            {
                int vs = Mathf.Clamp(Mathf.RoundToInt(vj * 0.6f), 1, 127);
                if (cube.follow == 1 || cube.follow == 3) count = ShadowNote(outEvents, count, cube, hit, tile, true, slot, vs, oj, offj, stackLift);
                if (cube.follow == 2 || cube.follow == 3) count = ShadowNote(outEvents, count, cube, hit, tile, false, slot, vs, oj, offj, stackLift);
            }
        }
        // echo: repeats on the step grid at -35 % each, dropped past the window end; shimmer climbs one shadow row per repeat
        int echoes = Mathf.Clamp(hit.echo, 0, 2);
        TileInteraction et = tile; int eoff = 0; float ev = vel;
        double echoGate = Math.Min(double.IsInfinity(gateSec) ? stepSec : gateSec, 0.92 * stepSec);
        for (int i = 1; i <= echoes; i++)
        {
            double oe = on + i * stepSec;
            if (oe >= windowEndDsp - 0.02) break;
            ev *= 0.65f;
            int me = midi;
            if (cube.shimmer && !bent)   // v7: a bent note's echoes repeat it (no shimmer climb through the chord's rows)
            {
                int o2;
                var st = ShadowTile(et, true, out o2);
                if (st != null) { et = st; eoff += o2; }
                me = Layered(FoldTile(slot, BaseOf(et, eoff, cube, hit, 0) + stackLift, et, island, eoff + (hit.lift ? 12 : 0) + stackLift), layer);
            }
            count = Push(outEvents, count, slot, me, Mathf.RoundToInt(ev), oe, ClampOff(slot, oe, oe + echoGate, windowEndDsp, bps));
        }
        // v7: a flow pedal / a varied note only has to fit the chord; §21: EVERY carried window is a long-grid (flow) window, whatever its style flag
        bool fit = w.carried || w.flow || (w.pass >= 1 && island != null && island.vary >= 1);
        return Stamp(outEvents, count, cube, island, windowStartDsp, windowEndDsp, bent, fit);
    }

    static readonly Dictionary<long, int> lastMonoKey = new Dictionary<long, int>();

    /// <summary>Tests (SPEC v4 R tests): when set, every event Dispatch sends to the synth is also handed to this tap (owner stamped).</summary>
    public static Action<NoteEvent> Tap;

    /// <summary>Sends events to the synth under <paramref name="owner"/>: the only Synth.Note call site for cubes.
    /// Mono slots (Lead, Bass) release the owner's previous key first; every cube event is logged for the stutter buffer.
    /// Cube notes are dropped while a stutter is active (the stutter's own re-issues and song events pass).</summary>
    public static void Dispatch(NoteEvent[] ev, int n, int owner)
    {
        if (ev == null || n <= 0 || !Synth.Ready) return;
        if (Performance.StutterActive && owner != OwnerStutter && owner != OwnerSong) return;
        for (int i = 0; i < n && i < ev.Length; i++)
        {
            NoteEvent e = ev[i];
            if (IsMono(e.slot))
            {
                long key = ((long)owner << 8) | (long)(e.slot & 255);   // v6: 72 slots (was & 15)
                int prev;
                if (lastMonoKey.TryGetValue(key, out prev) && prev != e.midi) Synth.NoteOff(e.slot, prev, e.onDsp, owner);
                lastMonoKey[key] = e.midi;
            }
            Synth.Note(e.slot, e.midi, e.vel, e.onDsp, e.offDsp, owner);
            e.owner = owner;
            if (owner >= 16) Performance.Log(e);   // cube events only: previews / auditions (owner 9) are not part of the stutter buffer
            if (Tap != null) Tap(e);
        }
    }

    /// <summary>The note a preview of <paramref name="tile"/> would play on <paramref name="slot"/> (§4.6): same register
    /// fold as the cubes, drum piece for the row, no costume. Timing: immediate when stopped (onDsp 0), else the next 16th.</summary>
    public static NoteEvent PreviewEvent(int slot, TileInteraction tile, int octave) => PreviewEvent(slot, tile, octave, 0.35);

    /// <summary><see cref="PreviewEvent(int, TileInteraction, int)"/> sounding <paramref name="pitchedSeconds"/> (SPEC v4: the draft's hover
    /// audition and the note-length audition); drums keep their 0.1 s hit.</summary>
    public static NoteEvent PreviewEvent(int slot, TileInteraction tile, int octave, double pitchedSeconds) => PreviewEvent(slot, tile, octave, pitchedSeconds, 0, 0);

    /// <summary>v7 (SPEC v7 §4.1): <see cref="PreviewEvent(int, TileInteraction, int, double)"/> of a note in octave <paramref name="layer"/> carrying
    /// <paramref name="offset"/> semitones on its tile (a bend, a lift): the pitch a cube in that layer / with that bend plays (<see cref="PitchedMidi"/>).</summary>
    public static NoteEvent PreviewEvent(int slot, TileInteraction tile, int octave, double pitchedSeconds, int layer, int offset)
    {
        var e = new NoteEvent { slot = slot, layer = layer, bent = offset != 0 };
        if (tile == null) return e;
        InstrumentDef d = SynthBank.Def(slot);
        double seconds;
        if (d.drums)
        {
            e.midi = DrumPieceOf(tile);
            e.vel = e.midi == 36 && (tile.gridZ == 0 || IsKeyboardTile(tile)) ? 100 : SynthBank.Velocity(slot, IsKeyboardTile(tile) ? 0.82f : 0.55f + 0.09f * Mathf.Clamp(tile.gridX, 0, 5), 0f);
            seconds = 0.1;
        }
        else
        {
            e.midi = PitchedMidi(slot, tile, octave, layer, offset);
            e.vel = SynthBank.Velocity(slot, 0.85f, 0f);
            seconds = Math.Max(0.05, pitchedSeconds);
        }
        if (GlobalClock.IsPlaying)
        {
            double t, end;
            PreviewBeat(out t, out end);
            e.onDsp = t;
            e.offDsp = d.drums ? t + seconds : Math.Max(t + 0.03, Math.Min(t + seconds, end - WindowEps));   // a pitched preview never rings past its island
        }
        else { e.onDsp = 0.0; e.offDsp = GlobalClock.DspNow + seconds; }
        return e;
    }

    /// <summary>
    /// v5 (R): an AUDITION of <paramref name="tile"/> on <paramref name="slot"/> — the preview's pitch (the same register fold as the cubes, the drum
    /// piece for a drum slot) at <paramref name="volume01"/> (0..1 of the slot's velocity range; a click previews at 0.85, a hover at 0.7), sounding
    /// <paramref name="pitchedSeconds"/> (drums 0.1 s). Timing: <paramref name="atDsp"/> when &gt; 0, else at once when stopped (onDsp 0) and on the next
    /// 16th while playing (<see cref="PreviewBeat"/>). Neither gated by <see cref="PreviewAllowed"/> nor clamped to the lit column's window: an audition
    /// is meant to be heard — it plays on the synth's preview bus (owner 9) and the song ducks under it.
    /// </summary>
    public static NoteEvent AuditionEvent(int slot, TileInteraction tile, int octave, double pitchedSeconds, float volume01, double atDsp = 0.0)
        => AuditionEvent(slot, tile, octave, pitchedSeconds, volume01, atDsp, 0, 0);

    /// <summary>v7 (SPEC v7 §4.1): <see cref="AuditionEvent(int, TileInteraction, int, double, float, double)"/> of a note in octave <paramref name="layer"/>
    /// carrying <paramref name="offset"/> semitones on its tile (a bend, a lift) — a harmony cube's bent note, an octave copy in its octave.</summary>
    public static NoteEvent AuditionEvent(int slot, TileInteraction tile, int octave, double pitchedSeconds, float volume01, double atDsp, int layer, int offset)
    {
        var e = new NoteEvent { slot = slot, layer = layer, bent = offset != 0 };
        if (tile == null) return e;
        InstrumentDef d = SynthBank.Def(slot);
        float v = Mathf.Clamp01(volume01);
        double seconds;
        if (d.drums)
        {
            e.midi = DrumPieceOf(tile);
            float scale = v / 0.85f;   // the kit's own loudness by column (the cubes' rule 1), scaled like the pitched level
            bool kb = IsKeyboardTile(tile);
            e.vel = (kb ? e.midi == 36 : tile.gridZ == 0) ? Mathf.Clamp(Mathf.RoundToInt(100f * scale), 1, 127)
                  : SynthBank.Velocity(slot, Mathf.Clamp01((kb ? 0.82f : 0.55f + 0.09f * Mathf.Clamp(tile.gridX, 0, 5)) * scale), 0f);
            seconds = 0.1;
        }
        else
        {
            e.midi = PitchedMidi(slot, tile, octave, layer, offset);   // v6: a keyboard key keeps its octave; v7: a run its shape, a layer its octave
            e.vel = SynthBank.Velocity(slot, v, 0f);
            seconds = Math.Max(0.05, pitchedSeconds);
        }
        double t;
        if (atDsp > 0.0) t = atDsp;
        else if (GlobalClock.IsPlaying) { double end; PreviewBeat(out t, out end); }
        else t = 0.0;
        e.onDsp = t;
        e.offDsp = (t > 0.0 ? t : GlobalClock.DspNow) + seconds;
        return e;
    }

    static bool IsKeyboardTile(TileInteraction t) => t != null && t.island != null && t.island.IsKeyboard;

    /// <summary>
    /// v6 (SPEC v6 §2.8 / §4.2): the note a PRESSED tile / piano key plays — the audition's pitch (the voice's slot, <see cref="FoldFor"/>: a
    /// keyboard key keeps its octave; drums: the kit piece) at the press velocity (<see cref="PressVelocity"/>, at least Synth.PressMinVelocity),
    /// sounding <paramref name="seconds"/> (drums 0.12 s), at <paramref name="atDsp"/> when &gt; 0 else at once (onDsp 0: a press answers the hand
    /// immediately, playing or not). Send it with <see cref="Press(NoteEvent)"/> (= Synth.PressNote): louder than the song.
    /// </summary>
    public static NoteEvent PressEvent(int slot, TileInteraction tile, int octave, double seconds, double atDsp = 0.0) => PressEvent(slot, tile, octave, seconds, atDsp, 0, 0);

    /// <summary>v7 (SPEC v7 §4.1): <see cref="PressEvent(int, TileInteraction, int, double, double)"/> of a note in octave <paramref name="layer"/> carrying
    /// <paramref name="offset"/> semitones on its tile (a bend, a lift).</summary>
    public static NoteEvent PressEvent(int slot, TileInteraction tile, int octave, double seconds, double atDsp, int layer, int offset)
    {
        var e = new NoteEvent { slot = slot, layer = layer, bent = offset != 0 };
        if (tile == null) return e;
        InstrumentDef d = SynthBank.Def(slot);
        double len;
        if (d.drums) { e.midi = DrumPieceOf(tile); len = 0.12; }
        else { e.midi = PitchedMidi(slot, tile, octave, layer, offset); len = Math.Max(0.08, seconds); }
        e.vel = PressVelocity(slot);
        e.onDsp = atDsp > 0.0 ? atDsp : 0.0;
        e.offDsp = (atDsp > 0.0 ? atDsp : GlobalClock.DspNow) + len;
        return e;
    }

    // ---- v7 (SPEC v7 §4.1): previews / auditions / presses of a CUBE's node — its voice, octave, the node's bend (+ lift) and the cube's layer
    /// <summary>v7: the pitch of <paramref name="tile"/> for a preview / audition / press on <paramref name="slot"/>: the tile + the transpose + 12 × the
    /// octave (−1..+1) + <paramref name="offset"/> (a bend, a lift), through the tile's fold, then 12 × <paramref name="layer"/>. Offset 0, layer 0 =
    /// the v6 preview pitch (drums: see the builders — the kit piece).</summary>
    public static int PitchedMidi(int slot, TileInteraction tile, int octave, int layer, int offset)
    {
        if (tile == null) return -1;
        return Layered(FoldTile(slot, tile.midi + SongManager.Transpose + 12 * Mathf.Clamp(octave, -1, 1) + offset, tile, tile.island, offset), layer);
    }

    /// <summary>
    /// v7 (SPEC v7 §4.1): the pitch node <paramref name="node"/> of <paramref name="cube"/> SOUNDS — as Resolve plays it without a stack, the bass
    /// anchor or a costume: its tile (<paramref name="tile"/> when given — e.g. a carried window's — else the node's home tile) + the transpose + the
    /// cube's octave + the node's lift sticker + its BEND, through the tile's fold, then 12 × its LAYER (an octave copy one / two octaves from its
    /// source, a harmony cube's 4th / 5th). Drums: the kit piece. −1 without a tile.
    /// </summary>
    public static int NodeMidi(AudioCube cube, int node, TileInteraction tile = null)
    {
        if (cube == null) return -1;
        if (tile == null) tile = NodeTile(cube, node);
        if (tile == null) return -1;
        int slot = Instruments.SlotOf(cube.instrument, cube.voice);
        if (SynthBank.Def(slot).drums) return DrumPieceOf(tile);
        int own = NodeOffset(cube, node, tile);
        return Layered(FoldTile(slot, tile.midi + SongManager.Transpose + 12 * cube.octave + own, tile, tile.island, own), cube.layer);
    }

    /// <summary>v7 (the deck's pattern preview, CardPreview): the pitch node <paramref name="node"/> of <paramref name="cube"/> would sound at on a tile of
    /// pitch <paramref name="tileMidi"/> of a grid like <paramref name="like"/> — <see cref="NodeMidi"/>'s rules without a real tile: the transpose,
    /// the cube's octave and lift sticker, a keyboard / phrase melody keeping its octave, a chord grid folded by its register, the octave layer.</summary>
    public static int PreviewPitch(AudioCube cube, int node, int tileMidi, KeyBlock like)
    {
        if (cube == null) return -1;
        int slot = Instruments.SlotOf(cube.instrument, cube.voice);
        int m = tileMidi + SongManager.Transpose + 12 * cube.octave + (cube.ModOf(node) == 8 ? 12 : 0) + Nudge.Semis(tileMidi, cube.NudgeAt(node));   // v9 (N): the nudge too
        int folded = like != null && (like.IsKeyboard || like.IsPhrase) ? FoldMelody(slot, m, like.register) : Fold(slot, m, RegOf(like));
        return Layered(folded, cube.layer);
    }

    static TileInteraction NodeTile(AudioCube cube, int node) => cube != null && node >= 0 && node < cube.nodes.Count ? cube.nodes[node] : null;
    /// <summary>v7: the semitones node <paramref name="node"/> adds to <paramref name="tile"/>: its bend (on its home tile) + the lift sticker's octave;
    /// v9 (N): + its nudge (scale steps of the song key on that tile).</summary>
    static int NodeOffset(AudioCube cube, int node, TileInteraction tile) => BendOf(cube, tile, node) + (cube.ModOf(node) == 8 ? 12 : 0) + NudgeOf(cube, tile, node);

    /// <summary>v9 (N): the semitones node <paramref name="node"/>'s nudge adds on <paramref name="tile"/> (0 without one): Nudge.Semis in the song's key.</summary>
    public static int NudgeOf(AudioCube cube, TileInteraction tile, int node) => cube != null && tile != null ? Nudge.Semis(tile.midi, cube.NudgeAt(node)) : 0;

    /// <summary>v7 (H's AudioCube.BendOn): the bend node <paramref name="node"/> of <paramref name="cube"/> adds on <paramref name="tile"/> — its bend when
    /// <paramref name="tile"/> is the node's HOME tile, else 0 (a carried / flow / varied window plays another tile: the bend would detune it).</summary>
    public static int BendOf(AudioCube cube, TileInteraction tile, int node) => cube != null ? cube.BendOn(tile, node) : 0;

    /// <summary>v7: <see cref="PreviewEvent(int, TileInteraction, int, double)"/> of a cube's node — its voice's slot, octave, bend and layer (a harmony
    /// cube's bent note and an octave copy preview as they will sound). <paramref name="tile"/>: the tile to hear (null = the node's home tile).</summary>
    public static NoteEvent PreviewEvent(AudioCube cube, int node, TileInteraction tile = null, double pitchedSeconds = 0.35)
    {
        if (cube == null) return new NoteEvent();
        if (tile == null) tile = NodeTile(cube, node);
        return PreviewEvent(Instruments.SlotOf(cube.instrument, cube.voice), tile, cube.octave, pitchedSeconds, cube.layer, NodeOffset(cube, node, tile));
    }

    /// <summary>v7: <see cref="AuditionEvent(int, TileInteraction, int, double, float, double)"/> of a cube's node (its voice, octave, bend and layer).
    /// Send it with <see cref="Audition(NoteEvent)"/>.</summary>
    public static NoteEvent AuditionEvent(AudioCube cube, int node, double pitchedSeconds, float volume01, double atDsp = 0.0, TileInteraction tile = null)
    {
        if (cube == null) return new NoteEvent();
        if (tile == null) tile = NodeTile(cube, node);
        return AuditionEvent(Instruments.SlotOf(cube.instrument, cube.voice), tile, cube.octave, pitchedSeconds, volume01, atDsp, cube.layer, NodeOffset(cube, node, tile));
    }

    /// <summary>v7: <see cref="PressEvent(int, TileInteraction, int, double, double)"/> of a cube's node (its voice, octave, bend and layer). Send it with
    /// <see cref="Press(NoteEvent)"/>.</summary>
    public static NoteEvent PressEvent(AudioCube cube, int node, double seconds, double atDsp = 0.0, TileInteraction tile = null)
    {
        if (cube == null) return new NoteEvent();
        if (tile == null) tile = NodeTile(cube, node);
        return PressEvent(Instruments.SlotOf(cube.instrument, cube.voice), tile, cube.octave, seconds, atDsp, cube.layer, NodeOffset(cube, node, tile));
    }

    static readonly NoteEvent[] oneEvent = new NoteEvent[1];
    /// <summary>v7: sends an audition / preview built above on the synth's preview bus (owner 9: heard over the song, which ducks under it) — what
    /// TileInteraction.Sound does. False when empty or without the synth.</summary>
    public static bool Audition(NoteEvent e)
    {
        if (!Synth.Ready || e.vel <= 0) return false;
        oneEvent[0] = e;
        Dispatch(oneEvent, 1, OwnerPreview);
        return true;
    }

    /// <summary>v6: the velocity of a press on <paramref name="slot"/>: two thirds of the way from the slot's loudest musical velocity to 127, at least
    /// Synth.PressMinVelocity (≈ 119-122).</summary>
    public static int PressVelocity(int slot)
    {
        InstrumentDef d = SynthBank.Def(slot);
        return Mathf.Clamp(Mathf.RoundToInt(d.velocityMax + (127f - d.velocityMax) * 0.66f), Synth.PressMinVelocity, 127);
    }

    /// <summary>v6: plays a press built by <see cref="PressEvent"/> (Synth.PressNote: the preview bus, the deep duck and the boost). False when empty.</summary>
    public static bool Press(NoteEvent e)
    {
        if (!Synth.Ready || e.vel <= 0) return false;
        Synth.PressNote(e.slot, e.midi, e.vel, e.onDsp, e.offDsp);
        return true;
    }

    /// <summary>
    /// v6: the harmonic-safety test of one logged cube event (SPEC 2.9; v6 §4.3): null when it holds, else why. Timing: on inside
    /// [winStart, winEnd), off &lt;= winEnd - 5 ms, off &gt; on, key / velocity in range. Pitch (pitched voices only): a chord island's event is a
    /// chord tone of the event's island (the WINDOW's: a carried pattern answers to the chord it plays on) and obeys the low-end law (no group but
    /// the bass below 48, shifted down with a lowered island); a keyboard island's key is a melody (no chord-tone law). Events without a window
    /// (previews, auditions, presses) pass.
    /// v7 (SPEC v7 §4.3): stairs and phrases are melodies like keyboards (Harmony chose a run's pitches to fit the key), a BENT note is a harmony
    /// voice (Harmony.Harmonize chose its 4th / 5th / 3rd / 6th against the chord and the other notes): both skip the chord-tone law; a flow pedal or
    /// a varied note must FIT the chord (Harmony.NoteScore ≥ TensionScore: a chord tone or a key tension); the low-end law judges a note before its
    /// octave layer (an octave copy below sounds an octave under a legal note).
    /// </summary>
    public static string HarmonicCheck(NoteEvent e)
    {
        if (e.winEndDsp <= 0.0) return null;
        if (e.onDsp < e.winStartDsp - 1e-6) return "on before the window start by " + (e.winStartDsp - e.onDsp).ToString("F4") + " s";
        if (e.onDsp >= e.winEndDsp) return "on after the window end";
        if (e.offDsp > e.winEndDsp - WindowEps + 1e-9) return "off past windowEnd - 5 ms by " + (e.offDsp - (e.winEndDsp - WindowEps)).ToString("F4") + " s";
        if (e.offDsp <= e.onDsp) return "off <= on";
        if (e.midi < 0 || e.midi > 127 || e.vel < 1 || e.vel > 127) return "midi/vel out of range";
        if (SynthBank.Def(e.slot).drums) return null;
        if (ReferenceEquals(e.island, null)) return "no island";
        if (e.island.IsKeyboard || e.island.IsStairs || e.island.IsPhrase) return null;   // v6 keyboards, v7 stairs and phrases: melodies (the kind field, not IsMelody's Unity null test: a logged event may outlive its island — a phrase deleted since)
        if (e.fit && !e.bent)
        {
            // v7: a flow pedal / a varied note — Harmony kept or chose it because it FITS the chord (a chord tone or a key tension, never an avoid note)
            var key = MusicTheory.KeyOfSong();
            if (Harmony.NoteScore(e.midi - SongManager.Transpose, e.island.chordRootMIDI, e.island.semitoneList, key.tonic, key.minor) < Harmony.TensionScore)
                return "midi " + e.midi + " does not fit " + e.island.assignedChord + " in " + key;
        }
        else if (!e.bent)
        {
            int pc = (((e.midi - e.island.chordRootMIDI - SongManager.Transpose) % 12) + 12) % 12;
            bool tone = false;
            foreach (int sPc in e.island.semitoneList) if (((sPc % 12) + 12) % 12 == pc) { tone = true; break; }
            if (!tone) return "midi " + e.midi + " is not a chord tone of " + e.island.assignedChord;
        }
        int floor = 48 + 12 * Mathf.Min(0, e.island.register);
        int baseMidi = e.midi - 12 * e.layer;   // v7: before the octave layer
        if (baseMidi < floor && !IsBass(e.slot)) return "low-end law: midi " + e.midi + (e.layer != 0 ? " (layer " + e.layer + ")" : "") + " on slot " + e.slot + " (" + SynthBank.Def(e.slot).name + ")";
        return null;
    }

    /// <summary>The 16th a preview / audition plays on while the clock runs (SPEC 2.9 rule 5): the next 16th at least <see cref="PreviewLead"/> (50 ms) ahead. Returns its
    /// song beat (wrapped into the song) and gives its DSP time and the DSP time of the end of the island window that 16th falls in.</summary>
    /// <summary>The least time ahead of the audio clock a preview is scheduled (seconds): more than the audio thread's lead over DspNow.</summary>
    public const double PreviewLead = 0.05;
    public static double PreviewBeat(out double onDsp, out double windowEndDsp)
    {
        double b = GlobalClock.Next16thBeat();
        double t = GlobalClock.DspTimeOfBeat(b);
        // v7 (integration): 50 ms, not 30 — the audio thread can render a buffer (1024 frames ≈ 21 ms) ahead of DspNow, so a preview posted only
        // 30 ms ahead could be picked up a buffer late; 50 ms clears two buffers
        if (t < GlobalClock.DspNow + PreviewLead) { b += 0.25; t += 0.25 / GlobalClock.BeatsPerSecond; }
        onDsp = t;
        double total = GlobalClock.TotalBeats, wrapped = b;
        if (GlobalClock.HasRegion && wrapped >= GlobalClock.RegionEnd)
        {
            // SPEC v4 R4: inside the focus loop the 16th after the region's end is the region's first 16th
            double lo = GlobalClock.RegionStart, len = GlobalClock.RegionEnd - lo;
            if (len > 1e-6) wrapped = lo + (wrapped - lo) % len;
        }
        else if (total > 0.0 && wrapped >= total) wrapped -= total * Math.Floor(wrapped / total);
        windowEndDsp = t + 1.0;
        var sm = SongManager.I;
        if (sm != null && sm.HasSong && sm.MeasureStarts.Count == sm.Islands.Count)
        {
            // R5: the window is the COLUMN playing on that 16th (all its islands share start and length)
            int col = sm.ActiveColumn((float)wrapped);
            float len = sm.ColumnLength(col);
            if (len > 0f) windowEndDsp = t + (sm.ColumnStart(col) + len - wrapped) / GlobalClock.BeatsPerSecond;
        }
        return wrapped;
    }

    /// <summary>Harmonic safety for previews (SPEC 2.9 rule 5): always when stopped, drums and Moons always, otherwise only when the tile's island
    /// is the one lit on the 16th the preview would play on (not the one lit now: in a window's last 16th that is the next island's downbeat).
    /// v4 R5: "lit" = its column spans that 16th (SongManager.IsActiveAt), so every island of the playing column may preview.
    /// v5 (R): no longer gates the tiles' auditions and previews (they always sound, on the preview bus, with the song ducked); it stays the
    /// predicate "this preview is in the harmony right now" for callers that want it.</summary>
    public static bool PreviewAllowed(TileInteraction tile, int slot)
    {
        if (!GlobalClock.IsPlaying) return true;
        if (SynthBank.Def(slot).drums) return true;
        if (tile == null || tile.island == null) return false;
        if (tile.island.kind == 1) return true;
        if (SongManager.I == null) return false;
        double on, end;
        double b = PreviewBeat(out on, out end);
        return SongManager.I.IsActiveAt(tile.island, (float)b);
    }

    /// <summary>The fill crash (GM 49) at <paramref name="dsp"/>, owned by the song (owner 7).</summary>
    public static void DispatchCrash(double dsp) => DispatchCrash(dsp, 96);

    /// <summary>v7: the crash at <paramref name="velocity"/> (the launch's landing crash is a softer one than the fill's 96).</summary>
    public static void DispatchCrash(double dsp, int velocity)
    {
        if (!Synth.Ready || SynthBank.DrumSlot < 0) return;
        Synth.Note(SynthBank.DrumSlot, 49, Mathf.Clamp(velocity, 1, 127), dsp, dsp + 0.1, OwnerSong);
    }

    /// <summary>v7 (SPEC v7 §4.4): the soft crash on a launch's landing at <paramref name="dsp"/> — once per downbeat time, shared with the fill crash
    /// (a fill crash on the same downbeat counts: the two never double). True when this call scheduled it.</summary>
    public static bool ScheduleLandingCrash(double dsp, int velocity)
    {
        if (Math.Abs(dsp - lastCrashDsp) < 1e-3) return false;
        lastCrashDsp = dsp;
        DispatchCrash(dsp, velocity);
        return true;
    }

    static double lastCrashDsp = -1.0;

    /// <summary>Forgets the last crash time (stop / seek / tempo change: SongManager re-arms the pass).</summary>
    public static void ResetCrash() { lastCrashDsp = -1.0; }

    /// <summary>Schedules the fill crash on the next downbeat once (repeated calls for the same downbeat are ignored); for SongManager.</summary>
    public static void ScheduleFillCrash(double nextDownbeatDsp)
    {
        if (Math.Abs(nextDownbeatDsp - lastCrashDsp) < 1e-3) return;
        lastCrashDsp = nextDownbeatDsp;
        DispatchCrash(nextDownbeatDsp);
    }
}
