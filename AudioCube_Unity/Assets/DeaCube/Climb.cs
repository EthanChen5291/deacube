using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CLIMB (the user, 2026-10-01, with a screen recording: the same 16 presses twice over D minor 7 — A4 D5 C5 D5 ×3, C5 D5 F5 D5 — and the second
/// time the phrase's top note rises, F5 → A5: "certain notes are raised"; "you should be able to accent many chords and get a reliable result
/// depending on both the path and the chords"; plays every time; not called "accent", which is the louder-note sticker).
/// The device is CLIMAX HEIGHTENING: the path's high points climb to the NEXT NOTE OF THE GRID'S CHORD above them, everything else stays, so the
/// pattern is still recognisable but reaches higher. A high point = a local peak of the looping path (higher than the note before, not lower
/// than the note after); they climb in order of height, the later one first among equals (a climax sits near the end of a phrase). Level n
/// (1..3, the ×n on the controls) = how many peaks climb. The target is the nearest chord tone above on the grid (within an octave), else a
/// step above (within a fourth) when the grid has no chord tone there; a peak at the top of the grid stays.
/// Played through AudioCube.TileAt (the cube hops to the higher tile, the note sounds there), saved per cube (CubeState.climb).
/// </summary>
public static class Climb
{
    public const int MaxLevel = 3;

    /// <summary>For pitches <paramref name="src"/> (−1 = a rest) at climb <paramref name="level"/>: the new pitch of each climbing note, −1 for
    /// the notes that stay. <paramref name="avail"/> = the pitches the grid offers.</summary>
    public static int[] Plan(IList<int> src, int level, int rootMidi, IList<int> semis, ICollection<int> avail)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = -1;
        if (level <= 0 || n == 0 || avail == null) return r;
        var pcs = new HashSet<int>();
        if (semis != null) foreach (int s in semis) pcs.Add(Pc(rootMidi + s));
        if (pcs.Count == 0) pcs.Add(Pc(rootMidi));
        var notes = new List<int>(n);
        for (int i = 0; i < n; i++) if (src[i] >= 0) notes.Add(i);
        if (notes.Count == 0) return r;
        var peaks = Peaks(src, notes);
        int take = Math.Min(Mathf.Clamp(level, 0, MaxLevel), peaks.Count);
        for (int k = 0; k < take; k++)
        {
            int i = peaks[k];
            int up = NextUp(src[i], pcs, avail);
            if (up > src[i]) r[i] = up;
        }
        return r;
    }

    /// <summary>The local peaks of the looping path (indices into <paramref name="src"/>), highest first, the later first among equals; a flat
    /// path's peak is its last note.</summary>
    public static List<int> Peaks(IList<int> src, List<int> notes)
    {
        var peaks = new List<int>();
        int m = notes.Count;
        if (m == 1) { peaks.Add(notes[0]); return peaks; }
        for (int k = 0; k < m; k++)
        {
            int i = notes[k], prev = notes[(k - 1 + m) % m], next = notes[(k + 1) % m];
            if (src[i] > src[prev] && src[i] >= src[next]) peaks.Add(i);
        }
        if (peaks.Count == 0) peaks.Add(notes[m - 1]);
        peaks.Sort((a, b) => src[a] != src[b] ? src[b].CompareTo(src[a]) : b.CompareTo(a));
        return peaks;
    }

    /// <summary>The next chord tone above <paramref name="m"/> among <paramref name="avail"/> (within an octave), else the next pitch above within
    /// a fourth; −1 when there is none.</summary>
    public static int NextUp(int m, HashSet<int> pcs, ICollection<int> avail)
    {
        int best = -1;
        foreach (int a in avail) if (a > m && a - m <= 12 && pcs.Contains(Pc(a)) && (best < 0 || a < best)) best = a;
        if (best >= 0) return best;
        foreach (int a in avail) if (a > m && a - m <= 5 && (best < 0 || a < best)) best = a;
        return best;
    }

    static int Pc(int m) => ((m % 12) + 12) % 12;

    /// <summary>Climb is offered for pitched paths on chord grids, keyboards and melody rolls (not drums, Moons or stairs runners).</summary>
    public static bool CanClimb(AudioCube c)
    {
        if (c == null || !c.isFinalized || c.IsOnMoon || c.IsDrums || c.nodes.Count == 0) return false;
        var kb = c.Island;
        return kb != null && !kb.IsMoon && !kb.IsStairs;
    }

    /// <summary>Sets one path's climb (0 off, 1..3) — one History entry. False when it cannot climb or nothing changed.</summary>
    public static bool Set(AudioCube c, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        if (!CanClimb(c) || c.climb == level) return false;
        c.climb = level;
        Changes++;
        History.Push();
        return true;
    }

    /// <summary>The paths on <paramref name="kb"/> that can climb.</summary>
    public static List<AudioCube> PathsOn(KeyBlock kb)
    {
        var r = new List<AudioCube>();
        if (kb == null) return r;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb && CanClimb(c)) r.Add(c);
        return r;
    }

    /// <summary>A grid's climb as its controls show it: the highest of its paths' levels (0 when none climbs or it has no paths).</summary>
    public static int LevelOf(KeyBlock kb)
    {
        int l = 0;
        foreach (var c in PathsOn(kb)) l = Math.Max(l, c.climb);
        return l;
    }

    /// <summary>Sets every path on <paramref name="kb"/> to climb <paramref name="level"/> — one History entry. False when it has none or nothing changed.</summary>
    public static bool SetGrid(KeyBlock kb, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        bool any = false;
        foreach (var c in PathsOn(kb)) if (c.climb != level) { c.climb = level; any = true; }
        if (!any) return false;
        Changes++;
        History.Push();
        return true;
    }

    /// <summary>The next level a climb control cycles to: off → ×1 → ×2 → ×3 → off.</summary>
    public static int Next(int level) => level >= MaxLevel ? 0 : Mathf.Max(0, level) + 1;

    /// <summary>The control's caption.</summary>
    public static string Caption(int level) => level <= 0 ? "climb · the high note reaches higher" : (level == 1 ? "climb · the top note rises" : "climb · the top " + level + " notes rise");

    /// <summary>Climb changes made (tests).</summary>
    public static int Changes { get; private set; }
}
