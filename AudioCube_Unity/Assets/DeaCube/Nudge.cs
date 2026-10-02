using System;
using UnityEngine;

/// <summary>
/// v9 (N): the per-step NUDGE — "override certain cube tiles to lower them / raise them by a note that the chord doesn't cover just for that
/// tile". A node of a cube carries an int −2..+2 (<see cref="AudioCube.nudges"/>, CubeState.nudges; 0 = none): on that step the cube plays the
/// n-th SCALE note of the song key above (+) / below (−) its tile's note instead of the tile — scale steps, not semitones, so a nudged step
/// always lands in key (a chord grid's passing notes, a keyboard key's neighbour; on a stairs run the offset rides on the run's shift). The
/// offset is added to the note's BASE pitch (VoiceRules rule 2, like a bend) before the normal fold; a nudged note plays alone (no pad tones,
/// followers, shimmer climb or bass anchor around it: like a bent note, its pitch is not the tile's). The rule is one function,
/// <see cref="Semis"/> (Harmony.StepShift's degree walk for one note); tools/gallery/deacube.py mirrors it (scale_step).
/// Look: a small ▲ / ▼ ink glyph on the bead (▲▲ / ▼▼ for ±2), the bead a touch higher / lower, the cube's hop a touch higher / lower.
/// Words (the user's rule: standard icons + short lowercase words): "higher", "lower".
/// </summary>
public static class Nudge
{
    /// <summary>Scale steps a node can be nudged by (±).</summary>
    public const int Max = 2;
    static readonly int[] MajorSteps = { 0, 2, 4, 5, 7, 9, 11 };
    static readonly int[] MinorSteps = { 0, 2, 3, 5, 7, 8, 10 };

    public static int Clamp(int n) => Mathf.Clamp(n, -Max, Max);

    /// <summary>
    /// The semitones that take <paramref name="tileMidi"/> <paramref name="steps"/> scale steps up (&gt; 0) / down (&lt; 0) in the key
    /// (<paramref name="tonic"/> pitch class, <paramref name="minor"/>): Harmony.StepShift's walk for one note, allocation-free. A note off the
    /// scale walks from the scale note just below it (else just above) and keeps its offset, as StepShift does. 0 for 0 steps.
    /// </summary>
    public static int Semis(int tileMidi, int steps, int tonic, bool minor)
    {
        if (steps == 0) return 0;
        var st = minor ? MinorSteps : MajorSteps;
        int t = ((tonic % 12) + 12) % 12;
        int d = Degree(st, t, tileMidi), off = 0;
        if (d < 0) { d = Degree(st, t, tileMidi - 1); off = 1; if (d < 0) { d = Degree(st, t, tileMidi + 1); off = -1; } }
        if (d < 0) return 0;
        int baseM = tileMidi - off, rel = baseM - t;
        int oct = (int)Math.Floor(rel / 12.0);
        int idx = d + steps, octMove = (int)Math.Floor(idx / 7.0);
        idx -= octMove * 7;
        return t + 12 * (oct + octMove) + st[idx] + off - tileMidi;
    }

    /// <summary><see cref="Semis(int, int, int, bool)"/> in the song's key (MusicTheory.KeyOfSong: SongManager.SongKey, else the first island's).</summary>
    public static int Semis(int tileMidi, int steps)
    {
        if (steps == 0) return 0;
        var k = MusicTheory.KeyOfSong();
        return Semis(tileMidi, steps, k.tonic, k.minor);
    }

    /// <summary>The key a saved song plays in (MenuPreview, no SongManager): its keyTonic / keyMinor, else the first island's chord (how
    /// SongManager derives it on load; C major without one).</summary>
    public static MusicTheory.SongKey KeyOf(SongState st)
    {
        if (st == null) return new MusicTheory.SongKey(0, false);
        if (st.keyTonic >= 0) return new MusicTheory.SongKey(st.keyTonic, st.keyMinor);
        var m = st.measures != null && st.measures.Length > 0 ? st.measures[0] : null;
        if (m == null) return new MusicTheory.SongKey(0, false);
        return MusicTheory.KeyOf(m.root, m.semis);
    }

    static int Degree(int[] st, int tonic, int midi)
    {
        int rel = (((midi - tonic) % 12) + 12) % 12;
        for (int d = 0; d < 7; d++) if (st[d] == rel) return d;
        return -1;
    }

    /// <summary>True when any entry is non-zero (null / empty = none).</summary>
    public static bool Any(System.Collections.Generic.IList<int> l)
    {
        if (l == null) return false;
        for (int i = 0; i < l.Count; i++) if (l[i] != 0) return true;
        return false;
    }

    // ------------------------------------------------------------------ look
    /// <summary>The IconFactory glyph of a nudge (null for 0): "nudgeUp" ▲, "nudgeUp2" ▲▲, "nudgeDown" ▼, "nudgeDown2" ▼▼ (registered on first use).</summary>
    public static string Icon(int n)
    {
        if (n == 0) return null;
        EnsureIcons();
        return n > 0 ? (n >= 2 ? "nudgeUp2" : "nudgeUp") : (n <= -2 ? "nudgeDown2" : "nudgeDown");
    }

    /// <summary>The short word for a direction (the user's rule): "higher" / "lower".</summary>
    public static string Word(int dir) => dir > 0 ? "higher" : "lower";

    static bool icons;
    /// <summary>Registers the four glyphs with IconFactory (solid triangles; the doubles stacked).</summary>
    public static void EnsureIcons()
    {
        if (icons) return;
        icons = true;
        IconFactory.Register("nudgeUp", p => IconFactory.P.Tri(p, -0.78f, -0.56f, 0.78f, -0.56f, 0f, 0.72f) - 0.06f);
        IconFactory.Register("nudgeDown", p => IconFactory.P.Tri(p, -0.78f, 0.56f, 0f, -0.72f, 0.78f, 0.56f) - 0.06f);
        IconFactory.Register("nudgeUp2", p => IconFactory.P.U(IconFactory.P.Tri(p, -0.8f, -0.92f, 0.8f, -0.92f, 0f, -0.08f) - 0.05f,
                                                              IconFactory.P.Tri(p, -0.8f, 0.08f, 0.8f, 0.08f, 0f, 0.92f) - 0.05f));
        IconFactory.Register("nudgeDown2", p => IconFactory.P.U(IconFactory.P.Tri(p, -0.8f, 0.92f, 0f, 0.08f, 0.8f, 0.92f) - 0.05f,
                                                                IconFactory.P.Tri(p, -0.8f, -0.08f, 0f, -0.92f, 0.8f, -0.08f) - 0.05f));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { icons = false; }
}
