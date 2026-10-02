using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure rhythm helpers (SPEC §3.2, §3.4): Euclidean necklaces, deterministic hashing, the Moon groove
/// templates and the energy rules. Nothing here touches Unity state, so audio (scheduled 0.22 s early),
/// the Land visuals and the ring pre-light all agree by construction.
/// </summary>
public static class Rhythm
{
    /// <summary>
    /// Bjorklund / Toussaint E(k, n): k hits spread as evenly as possible over n steps. Index 0 is always a hit
    /// when k >= 1. Identities: E(3,8) = x..x..x., E(5,8) = x.xx.xx., k >= n => all hits, k <= 0 => none.
    /// </summary>
    public static bool[] Bjorklund(int k, int n)
    {
        n = Math.Max(0, n);
        var result = new bool[n];
        if (n == 0 || k <= 0) return result;
        if (k >= n) { for (int i = 0; i < n; i++) result[i] = true; return result; }

        // The standard pairing recursion: a = sequences starting with a hit, b = the remainder.
        var a = new List<List<bool>>(k);
        var b = new List<List<bool>>(n - k);
        for (int i = 0; i < k; i++) a.Add(new List<bool> { true });
        for (int i = 0; i < n - k; i++) b.Add(new List<bool> { false });
        while (b.Count > 1)
        {
            int m = Math.Min(a.Count, b.Count);
            var next = new List<List<bool>>(m);
            for (int i = 0; i < m; i++) { var s = new List<bool>(a[i]); s.AddRange(b[i]); next.Add(s); }
            var rem = new List<List<bool>>();
            if (a.Count > m) for (int i = m; i < a.Count; i++) rem.Add(a[i]);
            else for (int i = m; i < b.Count; i++) rem.Add(b[i]);
            a = next; b = rem;
        }
        int w = 0;
        foreach (var s in a) foreach (bool v in s) result[w++] = v;
        foreach (var s in b) foreach (bool v in s) result[w++] = v;
        return result;
    }

    /// <summary>
    /// The per-bar hit pattern of a cube (§3.2): hits == -1 => every step, hits == -2 => bit j of mask,
    /// otherwise E(clamp(hits, 1, n), n) rotated so that pattern[j] = E[(j - rot) mod n].
    /// </summary>
    public static bool[] Pattern(int hits, int rot, int mask, int n)
    {
        n = Math.Max(1, n);
        var p = new bool[n];
        if (hits == -1) { for (int j = 0; j < n; j++) p[j] = true; return p; }
        if (hits == -2) { for (int j = 0; j < n; j++) p[j] = j < 31 && ((mask >> j) & 1) != 0; return p; }
        var e = Bjorklund(Mathf.Clamp(hits, 1, n), n);
        for (int j = 0; j < n; j++) p[j] = e[(((j - rot) % n) + n) % n];
        return p;
    }

    /// <summary>FNV-1a over three ints with a final avalanche; deterministic across runs and platforms
    /// (byte-identical to the former AudioCube.Hash).</summary>
    public static uint Hash(int a, int b, int c)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)a) * 16777619u;
        h = (h ^ (uint)b) * 16777619u;
        h = (h ^ (uint)c) * 16777619u;
        h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
        return h;
    }

    /// <summary>Hash mapped to [0, 1).</summary>
    public static float Hash01(int a, int b, int c) => (Hash(a, b, c) >> 8) / 16777216f;

    /// <summary>One Moon groove: per row (kick, snare, hat, perc) the necklace hits, rotation and step (StepLen as int:
    /// Quarter = 1, Eighth = 2, Sixteenth = 3, Triplet = 4). hits == 0 => that row's cube is muted.</summary>
    public struct Template { public string name; public int[] hits, rot, step; }

    const int Q = 1, E = 2, S = 3, Tr = 4;

    /// <summary>The six grooves of §3.4 (4/4; scale hits by BeatsPerBar / 4 for other meters).</summary>
    public static readonly Template[] Templates =
    {
        new Template { name = "straight",       hits = new[] { 4, 2, 8, 0 }, rot = new[] { 0, 1, 0, 0 }, step = new[] { Q, Q, E, Q } },
        new Template { name = "shuffle",        hits = new[] { 4, 2, 6, 3 }, rot = new[] { 0, 1, 0, 1 }, step = new[] { Q, Q, Tr, Tr } },
        new Template { name = "halftime",       hits = new[] { 2, 1, 8, 2 }, rot = new[] { 0, 2, 0, 3 }, step = new[] { Q, Q, E, E } },
        new Template { name = "fourOnTheFloor", hits = new[] { 4, 2, 8, 4 }, rot = new[] { 0, 1, 1, 2 }, step = new[] { Q, Q, E, E } },
        new Template { name = "breakbeat",      hits = new[] { 5, 3, 7, 5 }, rot = new[] { 0, 4, 0, 3 }, step = new[] { S, S, E, S } },
        new Template { name = "waltz",          hits = new[] { 3, 2, 6, 3 }, rot = new[] { 0, 1, 0, 1 }, step = new[] { Q, Q, E, E } },
    };

    static readonly float[] energyScale = { 0.6f, 0.8f, 1.0f, 1.15f };

    /// <summary>Velocity scale of an island's energy 0..3: {0.6, 0.8, 1.0, 1.15}.</summary>
    public static float EnergyScale(int energy) => energyScale[Mathf.Clamp(energy, 0, 3)];

    /// <summary>
    /// Moon density coupling (§3.4) for a Moon cube on <paramref name="row"/> (0 kick, 1 snare, 2 hat, 3 perc) while
    /// the lit island has <paramref name="energy"/>: returns -1 silent, 0 ghost, 1 normal, 2 accent.
    /// 0 hush: only the kick sounds, the snare at ghost, hats/perc silent. 1: hats/perc at ghost weight.
    /// 2: as drawn. 3: hats/perc accented on off-beats (the caller adds the ratchet x2).
    /// </summary>
    public static int MoonWeight(int energy, int row, bool offBeat)
    {
        energy = Mathf.Clamp(energy, 0, 3);
        switch (energy)
        {
            case 0: return row == 0 ? 1 : (row == 1 ? 0 : -1);
            case 1: return row >= 2 ? 0 : 1;
            case 3: return row >= 2 && offBeat ? 2 : 1;
            default: return 1;
        }
    }
}
