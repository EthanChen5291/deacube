using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v6 (SPEC v6 §2.9, package W): the world's magic — the look of a pattern travelling between grids (a paste, a carry). Package H moves carried
/// cubes along <see cref="ArcPoint"/> and asks for trails / landings here; the carry bridges and the octave towers live in W's own components
/// (<see cref="WorldMagic"/>, <see cref="CarryBridge"/>, <see cref="Tower"/>), which also hold the effect pools these calls use: a comet-like
/// sparkle trail (a flat tapered ribbon in the colour + ink-rimmed star sparkles twinkling out, on twos), a landing burst (a flat ink ring and a
/// ring of sparkles), and the paste flight (ghost beads arcing one after another and landing).
/// v7 (SPEC v7 §2.7–§2.9, §6): the octave layers' points (<see cref="LayerPoint"/>, <see cref="LayerOffset"/>), the flow glide
/// (<see cref="FlowPoint"/>), the fling's arc (<see cref="FlingPoint"/>), <see cref="Flip"/> (the mirror plane sweeping over the island as a path
/// flips). v7 §21 ("just keep cubes on each grid"): nothing flies between grids any more — <see cref="PasteFlight"/> no longer flies ghost beads
/// (it pops where they would have landed), <see cref="Fling"/> draws nothing, and a pasted / made cube APPEARS on its grid (<see cref="Appear"/>).
/// </summary>
public static class Magic
{
    /// <summary>A point on the magic arc from <paramref name="a"/> to <paramref name="b"/> at <paramref name="t"/> 0..1 (a parabola
    /// <paramref name="height"/> above the higher end at its middle). Carry bridges, carried cubes and paste flights share it.</summary>
    public static Vector3 ArcPoint(Vector3 a, Vector3 b, float height, float t)
    {
        t = Mathf.Clamp01(t);
        var p = Vector3.Lerp(a, b, t);
        p.y += 4f * height * t * (1f - t);
        return p;
    }

    /// <summary>The arc height used between two points (grows with their distance). v6 W: 1.4 + 0.3 × distance (M0 had 1.2 + 0.18 ×): between two
    /// neighbouring columns (≈ 12 u) the leap rises ≈ 5 u — it reads as an arc from the home camera's steep angle, not a flat line.</summary>
    public static float ArcHeight(Vector3 a, Vector3 b) => 1.4f + 0.3f * Vector3.Distance(a, b);

    /// <summary>v7 (SPEC v7 §2.9): where an octave layer rides relative to its grid — the SKY layer (layer &gt; 0): LayerRise up and LayerBack back per
    /// layer. For the DEEP layer (layer &lt; 0) this is only the typical offset of a front-row tile (the deep band is placed per tile: use
    /// <see cref="LayerPoint"/>).</summary>
    public static Vector3 LayerOffset(int layer)
    {
        if (layer > 0) return new Vector3(0f, layer * ProjectConfig.LayerRise, layer * ProjectConfig.LayerBack);
        if (layer < 0) return new Vector3(0f, -(DeepTop + 0.5f * DeepSlope + (-layer - 1) * DeepStepY), -(KeyBlock.EdgeInset + DeepGap + 0.6f + (-layer - 1) * DeepStepZ));
        return Vector3.zero;
    }

    /// <summary>The deep band: its gap in front of the platform's front edge, its row-0 drop under the platform top, the extra drop across the band,
    /// the most and the least it reaches forward, and the shift of each deeper layer (world units).</summary>
    public const float DeepGap = 0.28f, DeepTop = 0.4f, DeepSlope = 0.42f, DeepMaxDepth = 1.5f, DeepMinDepth = 0.7f, DeepStepY = 0.3f, DeepStepZ = 0.16f;

    /// <summary>
    /// v7 (SPEC v7 §6.3 — "for higher notes … some other cubes above if higher and below if lower octave phase in and plays it (make sure this looks
    /// good)"): the world point a layer cube of layer <paramref name="layer"/> stands on for <paramref name="tile"/> (the glass tile's top). The SKY:
    /// tile.Top + <see cref="LayerOffset(int)"/>, a glass copy floating over the grid. The DEEP: under the grid nothing can be seen from the home
    /// camera (the platform and the section plinth cover it), so the deep copy is a REFLECTION BAND just in front of and below the platform's front
    /// edge — the grid's rows compressed into a glass ramp that steps down toward the camera (row 0, the front row, nearest the edge), never deeper
    /// than the free gap in front (the next lane in its column; the front lane reaches DeepMaxDepth, over the plinth's ruler), always above the
    /// section's floor; each deeper layer a little lower and further out. Follows the island's live pose (belt ride, tower, ground, a flex).
    /// H rides layer cubes on it; W's glass layers draw on it.
    /// </summary>
    public static Vector3 LayerPoint(TileInteraction tile, int layer)
    {
        if (tile == null) return Vector3.zero;
        var kb = tile.island;
        if (layer >= 0 || kb == null) return tile.Top + LayerOffset(layer);
        return kb.transform.TransformPoint(DeepLocal(kb, kb.transform.InverseTransformPoint(tile.Top).x, tile.gridZ, -layer));
    }

    /// <summary>The deep band point in <paramref name="kb"/>'s local space for local x <paramref name="x"/>, row <paramref name="row"/>, deep layer
    /// <paramref name="k"/> (1, 2).</summary>
    public static Vector3 DeepLocal(KeyBlock kb, float x, int row, int k)
    {
        int rows = Mathf.Max(1, kb.IsKeyboard || kb.IsStairs ? 1 : kb.rows);
        float depth = DeepDepth(kb);
        float u = (Mathf.Clamp(row, 0, rows - 1) + 0.5f) / rows;
        float y = -0.03f - DeepTop - u * DeepSlope - (k - 1) * DeepStepY;
        float z = -KeyBlock.EdgeInset - DeepGap - u * depth - (k - 1) * DeepStepZ;
        return new Vector3(x, y, z);
    }

    /// <summary>How far forward <paramref name="kb"/>'s deep band may reach: the free gap in front of its platform (to the back edge of the next lane
    /// in front in its column) less DeepGap and a clearance, within DeepMinDepth..DeepMaxDepth; the front lane reaches DeepMaxDepth (over the plinth's
    /// ruler, above the section's floor). Cached per frame.</summary>
    public static float DeepDepth(KeyBlock kb)
    {
        if (kb == null) return DeepMaxDepth;
        if (Time.frameCount != cacheFrame) { cacheFrame = Time.frameCount; depthCache.Clear(); }
        float d;
        if (depthCache.TryGetValue(kb, out d)) return d;
        float gap = float.PositiveInfinity;   // the front lane: over the plinth's ruler (the band stays above the section's floor)
        var sm = SongManager.I;
        if (sm != null)
        {
            float front = kb.FrontEdge, best = float.NegativeInfinity;
            foreach (var o in sm.Islands)
                if (o != null && o != kb && !o.IsMoon && o.column == kb.column && o.BackEdge <= front + 0.01f) best = Mathf.Max(best, o.BackEdge);
            if (!float.IsNegativeInfinity(best)) gap = front - best;
        }
        d = float.IsPositiveInfinity(gap) ? DeepMaxDepth : Mathf.Clamp(gap - DeepGap - 0.25f, DeepMinDepth, DeepMaxDepth);
        depthCache[kb] = d;
        return d;
    }

    static int cacheFrame = -1;
    static readonly Dictionary<KeyBlock, float> depthCache = new Dictionary<KeyBlock, float>();

    /// <summary>v7 (SPEC v7 §2.7): the flow glide from <paramref name="a"/> to <paramref name="b"/> at <paramref name="t"/> 0..1 — straight along the
    /// grid, a small lift over the gap (no arc: "the cubes just going from left to right through the grid").</summary>
    public static Vector3 FlowPoint(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        return Vector3.Lerp(a, b, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.35f);
    }

    /// <summary>v7 (SPEC v7 §2.8): the fling from <paramref name="a"/> to <paramref name="b"/> at <paramref name="t"/> 0..1 — a ballistic arc up and to
    /// the right, its apex 2.4 + 0.25 × distance above the higher end.</summary>
    public static Vector3 FlingPoint(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        float apex = Mathf.Max(a.y, b.y) + 2.4f + 0.25f * Vector3.Distance(a, b);
        float y0 = a.y, y1 = b.y;
        // a parabola through (0, y0), (1, y1) whose top is apex: y = y0 + (y1 - y0) t + k t (1 - t), k chosen so max = apex
        float k = 4f * (apex - (y0 + y1) * 0.5f);
        float y = y0 + (y1 - y0) * t + k * t * (1f - t);
        var p = Vector3.Lerp(a, b, t);
        p.y = Mathf.Max(y, Mathf.Min(y0, y1));
        return p;
    }

    /// <summary>v7 (SPEC v7 §6.7): cube <paramref name="c"/>'s path is about to flip upside down (CubeOps.Flip calls this right BEFORE it changes
    /// the path): a mirror plane sweeps across its island and the path's new tiles flash as it passes them.</summary>
    public static void Flip(AudioCube c)
    {
        var wm = WorldMagic.Ensure();
        if (wm != null && c != null) wm.FlipSweep(c);
    }

    /// <summary>v7 §6.6, RETIRED by §21 (nothing flings: the launch is only A's riser and crash, W's <see cref="LaunchFx"/> lights it): kept so old
    /// callers compile; it only counts the call (WorldMagic.FlingCalls).</summary>
    public static void Fling(AudioCube c, KeyBlock from, KeyBlock to, float seconds)
    {
        var wm = WorldMagic.Ensure();
        if (wm != null && c != null) wm.FlingStart(c, from, to, seconds);
    }

    /// <summary>v7 §21.1 ("the pasted cube APPEARS on its target grid (a pop + sparkle there)"): cube <paramref name="c"/> appears on its grid now —
    /// a pop (a ring and a star burst in its colour at its tile) and its path's tiles glint one after another. For pastes, octave copies, harmony
    /// cubes, stairs runners: anything that materialises in place.</summary>
    public static void Appear(AudioCube c)
    {
        var wm = WorldMagic.Ensure();
        if (wm != null && c != null) wm.AppearPop(c);
    }

    /// <summary>A sparkle trail behind <paramref name="t"/> for <paramref name="seconds"/> (a carried cube in flight): a tapered ribbon in
    /// <paramref name="c"/> and star sparkles shed along the way. Calling it again for the same transform extends the trail.</summary>
    public static void Trail(Transform t, Color c, float seconds)
    {
        var wm = WorldMagic.Ensure();
        if (wm != null) wm.StartTrail(t, c, seconds);
    }

    /// <summary>The landing sparkle where a flying pattern note arrives (a flat ink ring and a ring of star sparkles).</summary>
    public static void Land(Vector3 p, Color c)
    {
        var wm = WorldMagic.Ensure();
        if (wm != null) wm.LandBurst(p, c);
        else Fx.Burst(p, c, 8, 1.6f);
    }

    /// <summary>A paste. v6: ghost beads flew from <paramref name="from"/> to <paramref name="to"/> along arcs. v7 §21 ("the cubes jumping form grid
    /// to grid doesnt look good"): nothing flies — each target point POPS in place at the moment its bead would have landed (a small ring and a
    /// sparkle burst in <paramref name="c"/>), the last one <paramref name="seconds"/> after the call (<see cref="PasteLandTime"/> unchanged), so a
    /// caller timing the cube's materialisation on it still lines up.</summary>
    public static void PasteFlight(IList<Vector3> from, IList<Vector3> to, Color c, float seconds)
    {
        if (to == null || to.Count == 0) return;
        var wm = WorldMagic.Ensure();
        if (wm == null) { foreach (var p in to) Fx.Burst(p, c, 8, 1.6f); return; }
        int n = to.Count;
        for (int i = 0; i < n; i++) wm.PopAt(to[i], c, PasteLandTime(i, n, seconds));
    }

    /// <summary>Seconds after <see cref="PasteFlight"/> at which bead <paramref name="i"/> of <paramref name="n"/> lands.</summary>
    public static float PasteLandTime(int i, int n, float seconds)
    {
        float stagger, dur;
        PasteTiming(n, seconds, out stagger, out dur);
        return stagger * Mathf.Clamp(i, 0, Mathf.Max(0, n - 1)) + dur;
    }

    static void PasteTiming(int n, float seconds, out float stagger, out float dur)
    {
        seconds = Mathf.Max(0.2f, seconds);
        stagger = n > 1 ? Mathf.Min(0.07f, seconds * 0.45f / (n - 1)) : 0f;
        dur = Mathf.Max(0.18f, seconds - stagger * Mathf.Max(0, n - 1));
    }
}
