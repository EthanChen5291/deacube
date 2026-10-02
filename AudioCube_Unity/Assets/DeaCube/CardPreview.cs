using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 (the user, 2026-10-01): "whenever you're hovering over possible cards, instead of the chord playing, play the last (live) cube grid pattern
/// (all the grids for that measure) in that chord, adapted. if no chord, then just have the fallback of what it is now".
/// Hovering a chord card in the deck (the song stopped) plays one pass of the REFERENCE COLUMN's patterns — every grid in it (chord grids,
/// keyboards, melody rolls; stairs runs and Moons stay out) — each cube's path adapted to the card's chord the way a paste adapts it
/// (Clipboard.Adapt's rules: a grid pattern keeps its shape by voice leading on the grid the card would become, a bass line keeps the root, a
/// melody moves by scale steps in its own range, drums go as they are), with the cubes' own sounds, lengths and rests, on the preview bus.
/// The reference column is the one a placed card would follow (IslandTray's focused island: the selected island, the camera's focus, else the
/// last); when it has no notes yet, the nearest column before it that has some. A card without a chord (keyboard / stairs / phrase cards) or a
/// song without notes: <see cref="Play"/> returns false and the caller keeps the chord's arpeggio.
/// </summary>
public static class CardPreview
{
    /// <summary>The longest preview (beats): one pass of the column, at most two bars.</summary>
    public const float MaxBeats = 8f;
    /// <summary>Previews played and fallbacks (no chord / no notes) — tests.</summary>
    public static int Plays { get; private set; }
    public static int Fallbacks { get; private set; }
    /// <summary>The column the last preview played (−1 none).</summary>
    public static int LastColumn { get; private set; } = -1;
    /// <summary>The notes the last preview scheduled: x = synth slot, y = midi, z = onset (ticks from its start) — tests.</summary>
    public static readonly List<Vector3Int> LastNotes = new List<Vector3Int>();
    /// <summary>The kind of the grid each of <see cref="LastNotes"/> came from (0 chord grid, 2 keyboard, 4 melody roll; −1 a drum note) — tests.</summary>
    public static readonly List<int> LastKinds = new List<int>();
    /// <summary>A preview is sounding (the song starting cuts it).</summary>
    public static bool Sounding => sounding;
    /// <summary>The last preview's span on the audio clock.</summary>
    public static double LastStartDsp { get; private set; }
    public static double LastEndDsp { get; private set; }
    static bool sounding;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Plays = 0; Fallbacks = 0; LastColumn = -1; LastNotes.Clear(); LastKinds.Clear(); LastStartDsp = 0.0; LastEndDsp = 0.0; sounding = false; }

    /// <summary>True for a chord card (a chord island to be).</summary>
    public static bool HasChord(SongManager.MeasureData md) => md != null && md.kind == 0 && md.semitones != null && md.semitones.Length > 0;

    /// <summary>The column a hover plays: <paramref name="fromIsland"/>'s column if it has pitched notes, else the nearest column before it that does
    /// (then after it); −1 when the song has none.</summary>
    public static int ReferenceColumn(int fromIsland)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || sm.Islands.Count == 0) return -1;
        int start = fromIsland >= 0 && fromIsland < sm.Islands.Count && sm.Islands[fromIsland] != null ? sm.Islands[fromIsland].column : sm.ColumnCount - 1;
        start = Mathf.Clamp(start, 0, Mathf.Max(0, sm.ColumnCount - 1));
        for (int col = start; col >= 0; col--) if (HasNotes(sm, col)) return col;
        for (int col = start + 1; col < sm.ColumnCount; col++) if (HasNotes(sm, col)) return col;
        return -1;
    }

    static bool HasNotes(SongManager sm, int col)
    {
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.column != col || kb.IsStairs || kb.IsMoon) continue;
            foreach (var c in Clipboard.PathsOf(kb)) if (!c.IsDrums) return true;
        }
        return false;
    }

    /// <summary>The grid a chord card would become, as tile pitches [x, z] (KeyBlock's chord-grid rule: column x = the x-th inversion of the chord's tones,
    /// row z = its z-th tone, + 12 × register).</summary>
    public static int[,] ChordTable(int root, IList<int> semis, int cols, int register)
    {
        cols = Mathf.Max(1, cols);
        int rows = Mathf.Max(1, semis.Count);
        var t = new int[cols, rows];
        var inv = new List<int>(semis);
        for (int x = 0; x < cols; x++)
        {
            if (x > 0) { int low = inv[0]; inv.RemoveAt(0); inv.Add(low + 12); inv.Sort(); }
            for (int z = 0; z < rows; z++) t[x, z] = root + inv[z] + 12 * register;
        }
        return t;
    }

    /// <summary>Plays card <paramref name="md"/>'s preview (see the class) from <paramref name="fromIsland"/>'s column; false = nothing to play (the
    /// caller's fallback: the chord's arpeggio).</summary>
    public static bool Play(SongManager.MeasureData md, int fromIsland)
    {
        LastNotes.Clear(); LastKinds.Clear();
        var sm = SongManager.I;
        if (!HasChord(md) || sm == null || !sm.HasSong || !Synth.Ready) { Fallbacks++; return false; }
        int col = ReferenceColumn(fromIsland);
        if (col < 0) { Fallbacks++; return false; }
        // the card's chord as its grid would hold it (KeyBlock: root into 36..84, the effective tones with the root, sorted)
        int root = md.chordRootMIDI;
        if (root < 36) root += 24;
        if (root > 84) root -= 12;
        var semis = new List<int>(MusicTheory.EffectiveSemis(md.semitones, md.mood, SongManager.Climate));
        if (!semis.Contains(0)) semis.Insert(0, 0);
        semis.Sort();
        int tpb = ProjectConfig.TicksPerBeat;
        int span = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(sm.ColumnLength(col), MaxBeats) * tpb));
        double spt = 1.0 / (tpb * GlobalClock.BeatsPerSecond);
        double t0 = Synth.DspNow + 0.06;
        var node = new List<int>(); var ticks = new List<int>(); var mods = new List<int>();
        var notes = new List<Vector3Int>(); var ends = new List<int>(); var kinds = new List<int>();
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.column != col || kb.IsStairs || kb.IsMoon) continue;
            foreach (var c in Clipboard.PathsOf(kb))
            {
                var pitch = AdaptedPitches(c, kb, root, semis);
                if (pitch == null) continue;
                int total; CubeOps.PlayOrder(c, node, ticks, mods, out total);
                if (total <= 0) continue;
                int slot = Instruments.SlotOf(c.instrument, c.voice);
                for (int at = 0; at < span; at += total)   // its period again and again until the column (≤ two bars) is full
                {
                    int t = at;
                    for (int i = 0; i < node.Count && t < span; i++)
                    {
                        int len = Mathf.Min(ticks[i], span - t);
                        int m = node[i] >= 0 && node[i] < pitch.Length ? pitch[node[i]] : -1;
                        if (mods[i] != 1 && m > 0 && len > 0) { notes.Add(new Vector3Int(slot, m, t)); ends.Add(t + len); kinds.Add(c.IsDrums ? -1 : kb.kind); }
                        t += ticks[i];
                    }
                }
            }
        }
        if (notes.Count == 0) { Fallbacks++; return false; }
        Synth.CancelOwner(Synth.PreviewOwner);   // a preview still sounding from the card before goes
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            int vel = 80;
            Synth.Audition(n.x, n.y, vel, t0 + n.z * spt, t0 + (ends[i] - 0.08 * (ends[i] - n.z)) * spt);
        }
        LastNotes.AddRange(notes); LastKinds.AddRange(kinds);
        LastColumn = col; LastStartDsp = t0; LastEndDsp = t0 + span * spt;
        sounding = true;
        Plays++;
        return true;
    }

    /// <summary>The song starting cuts a preview still sounding (the deck stays quiet while it plays). IslandTray calls it every frame.</summary>
    public static void Tick()
    {
        if (!sounding) return;
        if (Synth.DspNow > LastEndDsp) { sounding = false; return; }
        if (GlobalClock.IsPlaying) { Synth.CancelOwner(Synth.PreviewOwner); sounding = false; }
    }

    /// <summary>The sounding pitch of every node of <paramref name="c"/> (on <paramref name="kb"/>) over the card's chord; null when it cannot be adapted.</summary>
    static int[] AdaptedPitches(AudioCube c, KeyBlock kb, int root, List<int> semis)
    {
        int n = c.nodes.Count;
        var r = new int[n];
        if (c.IsDrums)
        {
            for (int i = 0; i < n; i++) r[i] = VoiceRules.NodeMidi(c, i);   // drums as they are
            return r;
        }
        var src = Clipboard.SourceOf(c.nodes, kb, c.instrument, c.HasDurations ? c.durs : null);
        if (!src.Valid) return null;
        var tile = new int[n];
        if (kb.IsKeyboard || kb.IsPhrase)
        {
            // a melody: by scale steps onto the card's chord, in its own grid's range (Harmony.AdaptMelody, as a paste onto a keyboard)
            var own = Harmony.MidiTable(kb);
            int lo = int.MaxValue, hi = int.MinValue;
            for (int x = 0; x < own.GetLength(0); x++) for (int z = 0; z < own.GetLength(1); z++) { lo = Mathf.Min(lo, own[x, z]); hi = Mathf.Max(hi, own[x, z]); }
            if (lo > hi) return null;
            var key = MusicTheory.KeyOfSong();
            var line = Harmony.AdaptMelody(src.midi, src.w, src.root, src.semis, root, semis, key.tonic, key.minor, lo, hi);
            for (int i = 0; i < n; i++) tile[i] = line != null && i < line.Length ? line[i] : src.midi[i];
        }
        else
        {
            // a grid pattern: its shape by voice leading onto the grid the card would become (Harmony.AdaptGrid; the bass rule for the bass group)
            var table = ChordTable(root, semis, kb.cols, kb.register);
            bool bassLike = c.instrument == Clipboard.BassGroup;
            int[] xs, zs;
            Harmony.AdaptGrid(src.midi, src.xs, src.zs, src.w, src.rows, table, bassLike, bassLike ? Harmony.Pc(root) : -1, out xs, out zs);
            if (xs == null || zs == null) return null;
            for (int i = 0; i < n; i++) tile[i] = table[Mathf.Clamp(xs[i], 0, table.GetLength(0) - 1), Mathf.Clamp(zs[i], 0, table.GetLength(1) - 1)];
        }
        for (int i = 0; i < n; i++) r[i] = VoiceRules.PreviewPitch(c, i, tile[i], kb);
        return r;
    }
}
