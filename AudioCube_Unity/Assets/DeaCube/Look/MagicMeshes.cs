using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v6 package W (SPEC v6 §6): the procedural meshes of the world magic (MeshFactory is K's: these live beside it). The octave tower: a stack of
/// hand-cut stone drums under a flared capital with one coloured band per octave (<see cref="Pillar"/>, flat-faced so the Toon shader draws
/// every facet as one flat value and the chamfered joints as dark grooves); the waves: a crest ring (<see cref="Crest"/>: an inner slope, a
/// curling crest leaning outward, a steep front) that spreads over the sea and sinks into it, a scalloped foam skirt round the pillar's foot
/// (<see cref="FoamSkirt"/>) and the splash crown (<see cref="Crown"/>: tongues of water leaning out). The carry sigil's band follows the
/// platform's rounded outline (<see cref="TrackBand"/>, on MeshFactory.TrackPoint). Meshes are built once per shape and cached where they can be.
/// </summary>
public static partial class MagicMeshes
{
    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    // ------------------------------------------------------------------ the octave tower
    /// <summary>
    /// The pillar under a raised island, top at y = 0 going down <paramref name="depth"/> world units: a capital (radius 1.28 r) and, for each of
    /// <paramref name="bands"/> octaves, a coloured band drum (submesh 1) between thin stone rings, then stone drums (submesh 0) of seeded heights
    /// (0.8..1.15 u), radii (± 4 %) and turns, each chamfered top and bottom so the joints read as grooves. <paramref name="sides"/>-gon drums.
    /// </summary>
    public static Mesh Pillar(float r, int bands, float depth, int sides, int seed)
    {
        string key = "pillar3_" + r.ToString("F2") + "_" + bands + "_" + depth.ToString("F1") + "_" + sides + "_" + seed;
        Mesh cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;
        var rnd = new System.Random(seed);
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
        var stone = new List<int>(); var band = new List<int>();
        float y = 0f;
        // the capital: a flared slab that holds the platform (with a top cap: it is what shows at the waterline at rest)
        Drum(v, n, uv, stone, y, y - 0.34f, r * 1.28f, r * 1.28f, 0.08f, sides, 0.3f, true);
        y -= 0.34f;
        Drum(v, n, uv, stone, y, y - 0.16f, r * 1.02f, r * 1.02f, 0.04f, sides, 0.1f, false);   // the neck
        y -= 0.16f;
        for (int b = 0; b < bands; b++)
        {
            Drum(v, n, uv, band, y, y - 0.26f, r * 1.1f, r * 1.1f, 0.05f, sides, 0.2f + b * 0.4f, false);
            y -= 0.26f;
            Drum(v, n, uv, stone, y, y - 0.3f, r * 0.98f, r * 0.98f, 0.06f, sides, 0.7f + b * 0.3f, false);
            y -= 0.3f;
        }
        // the second set: the bands that show while the tower is up (the capital's set is then hidden under the platform; from the steep home
        // camera the pillar shows from ≈ 2.5 u under the platform down to the water)
        float lowBands = -2.9f;
        while (y > -depth)
        {
            if (y <= lowBands && bands > 0)
            {
                for (int b = 0; b < bands; b++)
                {
                    float tb = Taper(r, y);
                    Drum(v, n, uv, band, y, y - 0.28f, tb * 1.07f, tb * 1.07f, 0.05f, sides, 0.5f + b * 0.3f, false);
                    y -= 0.28f;
                    Drum(v, n, uv, stone, y, y - 0.32f, tb * 0.99f, tb * 0.99f, 0.06f, sides, 0.9f + b * 0.2f, false);
                    y -= 0.32f;
                }
                lowBands = float.NegativeInfinity;
                continue;
            }
            float h = 0.8f + 0.35f * (float)rnd.NextDouble();
            float j = 0.97f + 0.07f * (float)rnd.NextDouble();
            float turn = (float)rnd.NextDouble();
            // a tower is wider at its foot: each drum a little wider than the one above it
            Drum(v, n, uv, stone, y, y - h, Taper(r, y) * j, Taper(r, y - h) * j, 0.09f, sides, turn, false);
            y -= h;
        }
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        m.subMeshCount = 2;
        m.SetTriangles(stone, 0); m.SetTriangles(band, 1);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>The pillar's radius <paramref name="y"/> below its top (≤ 0): it widens 6 % per world unit down.</summary>
    static float Taper(float r, float y) => r * (1f + 0.06f * Mathf.Max(0f, -y - 0.5f));

    /// <summary>One chamfered drum between y0 (top) and y1 (bottom): the top chamfer, the side, the bottom chamfer, each face flat; a top cap when
    /// <paramref name="cap"/>. <paramref name="turn"/> (0..1 of a facet) rotates the polygon so stacked drums never line up.</summary>
    static void Drum(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, float y0, float y1, float rTop, float rBot, float ch, int sides, float turn, bool cap)
    {
        ch = Mathf.Min(ch, (y0 - y1) * 0.3f);
        float[] ys = { y0, y0 - ch, y1 + ch, y1 };
        float[] rs = { rTop * 0.86f, rTop, rBot, rBot * 0.86f };   // the chamfers meet as a V groove: a dark line in the cel shading
        for (int i = 0; i < sides; i++)
        {
            float a0 = (i + turn) / sides * Mathf.PI * 2f, a1 = (i + 1 + turn) / sides * Mathf.PI * 2f;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            for (int s = 0; s < 3; s++)
            {
                Vector3 p00 = d0 * rs[s] + Vector3.up * ys[s], p01 = d1 * rs[s] + Vector3.up * ys[s];
                Vector3 p10 = d0 * rs[s + 1] + Vector3.up * ys[s + 1], p11 = d1 * rs[s + 1] + Vector3.up * ys[s + 1];
                Quad(v, n, uv, tris, p00, p01, p11, p10);
            }
        }
        if (cap)
        {
            int c0 = v.Count;
            v.Add(new Vector3(0f, y0, 0f)); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                float a = (i + turn) / sides * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * rs[0], y0, Mathf.Sin(a) * rs[0])); n.Add(Vector3.up); uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int i = 0; i < sides; i++) { tris.Add(c0); tris.Add(c0 + 1 + (i + 1) % sides); tris.Add(c0 + 1 + i); }
        }
    }

    /// <summary>A flat-shaded quad (a, b, c, d counter-clockwise seen from outside), its normal from the geometry.</summary>
    static void Quad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 nn = Vector3.Cross(b - a, d - a);
        if (nn.sqrMagnitude < 1e-10f) nn = Vector3.Cross(c - b, a - b);
        nn.Normalize();
        // outward: the face normal must point away from the axis
        Vector3 mid = (a + b + c + d) * 0.25f; mid.y = 0f;
        bool flip = Vector3.Dot(nn, mid) < 0f && Mathf.Abs(nn.y) < 0.95f;
        if (flip) nn = -nn;
        int i0 = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        for (int k = 0; k < 4; k++) n.Add(nn);
        uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
        // wind so the face is seen from the normal's side
        Vector3 fn = Vector3.Cross(b - a, c - a);   // (MeshFactory.AddTri's rule: a front face's cross product points along its normal)
        if (Vector3.Dot(fn, nn) >= 0f) { tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2); tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3); }
        else { tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1); tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2); }
    }

    // ------------------------------------------------------------------ waves
    /// <summary>
    /// A wave crest ring of radius 1 lying on y = 0 (scale it: x / z = radius, y = crest height): a CLOSED profile (the Toon ink outline is an
    /// inverted hull: an open sheet z-fights with it into a sawtooth) — from the inner foot (−0.075 of the radius) up a gentle inner slope to a
    /// crest leaning outward, down a steep front to the outer foot (+0.04), then under the water back to the inner foot. uv.x across the top of
    /// the profile (the foam ramp), uv.y round the ring. Smooth normals over the top, hard edges at the feet and underneath.
    /// </summary>
    public static Mesh Crest(int segs = 128)
    {
        string key = "crest3_" + segs;
        Mesh cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;
        // control points of the top of the profile (radial offset, height), inner foot -> crest -> outer foot, sampled finely along a
        // Catmull-Rom curve: the Toon shader thresholds the interpolated normal into flat bands, and a coarse row zigzags triangle by triangle
        Vector2[] ctl = { new Vector2(-0.075f, 0f), new Vector2(-0.05f, 0.3f), new Vector2(-0.024f, 0.74f), new Vector2(-0.004f, 1f), new Vector2(0.014f, 0.9f), new Vector2(0.028f, 0.45f), new Vector2(0.04f, 0f) };
        const int per = 4;
        var topL = new List<Vector2>();
        for (int k = 0; k < ctl.Length - 1; k++)
            for (int q = 0; q < per; q++)
            {
                float u = q / (float)per;
                Vector2 p0 = ctl[Mathf.Max(0, k - 1)], p1 = ctl[k], p2 = ctl[k + 1], p3 = ctl[Mathf.Min(ctl.Length - 1, k + 2)];
                topL.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u));
            }
        topL.Add(ctl[ctl.Length - 1]);
        var top = topL.ToArray();
        int np = top.Length;
        var tn = new Vector2[np];
        for (int j = 0; j < np; j++)
        {
            Vector2 a = top[Mathf.Max(0, j - 1)], b = top[Mathf.Min(np - 1, j + 1)];
            Vector2 t = (b - a).normalized;
            Vector2 nn = new Vector2(-t.y, t.x);   // left of the walk inner -> outer = up
            if (nn.y < 0f) nn = -nn;
            tn[j] = nn;
        }
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        // top sheet
        for (int i = 0; i <= segs; i++)
        {
            float ang = i / (float)segs * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            for (int j = 0; j < np; j++)
            {
                v.Add(d * (1f + top[j].x) + Vector3.up * top[j].y);
                n.Add((d * tn[j].x + Vector3.up * tn[j].y).normalized);
                uv.Add(new Vector2(j / (float)(np - 1), i / (float)segs));
            }
        }
        for (int i = 0; i < segs; i++)
            for (int j = 0; j < np - 1; j++)
            {
                int a = i * np + j, b = a + 1, c = (i + 1) * np + j, e = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(e);
                tris.Add(a); tris.Add(e); tris.Add(c);
            }
        // under the water: the outer wall down, the floor back in, the inner wall up (hard edges; the sea hides them, the outline needs them)
        Vector2[] under = { new Vector2(0.04f, 0f), new Vector2(0.04f, -0.25f), new Vector2(-0.075f, -0.25f), new Vector2(-0.075f, 0f) };
        Vector2[] underN = { new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };
        for (int f = 0; f < 3; f++)
        {
            int b0 = v.Count;
            for (int i = 0; i <= segs; i++)
            {
                float ang = i / (float)segs * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 nn = (d * underN[f].x + Vector3.up * underN[f].y).normalized;
                v.Add(d * (1f + under[f].x) + Vector3.up * under[f].y); n.Add(nn); uv.Add(new Vector2(1f, i / (float)segs));
                v.Add(d * (1f + under[f + 1].x) + Vector3.up * under[f + 1].y); n.Add(nn); uv.Add(new Vector2(1f, i / (float)segs));
            }
            for (int i = 0; i < segs; i++)
            {
                int a = b0 + i * 2, b = a + 1, c = a + 2, e = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(e);
                tris.Add(a); tris.Add(e); tris.Add(c);
            }
        }
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>
    /// The foam skirt round the pillar's foot: a flat ring from radius 0.78 out to a scalloped edge of <paramref name="lobes"/> round lobes
    /// (1.0 .. 1.2), lying on y = 0, facing up.
    /// </summary>
    public static Mesh FoamSkirt(int lobes = 14, int perLobe = 6)
    {
        string key = "foamskirt_" + lobes + "_" + perLobe;
        Mesh cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;
        int segs = lobes * perLobe;
        var v = new Vector3[(segs + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        for (int i = 0; i <= segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            float lobe = (i % perLobe) / (float)perLobe;
            float ro = 1f + 0.2f * Mathf.Sin(lobe * Mathf.PI);
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v[i * 2] = d * 0.78f; v[i * 2 + 1] = d * ro;
            n[i * 2] = Vector3.up; n[i * 2 + 1] = Vector3.up;
            uv[i * 2] = new Vector2(0.9f, i / (float)segs); uv[i * 2 + 1] = new Vector2(1f, i / (float)segs);
        }
        var tris = new List<int>(segs * 6);
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris.Add(a); tris.Add(d); tris.Add(b);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }
        var m = new Mesh { name = key, vertices = v, normals = n, uv = uv };
        m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>
    /// The splash crown: <paramref name="count"/> rounded tongues of water round a unit circle on y = 0 — closed spindles (thick at the foot, a
    /// round tip; 8 sides, smooth normals: the Toon bands read them as blobs of water, the ink outline rims them) of height 0.55..1 (seeded),
    /// leaning outward 14..30°. Scale y to throw it up and let it fall back.
    /// </summary>
    public static Mesh Crown(int count, int seed)
    {
        string key = "crown2_" + count + "_" + seed;
        Mesh cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;
        var rnd = new System.Random(seed);
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        const int sides = 8, rings = 6;
        for (int k = 0; k < count; k++)
        {
            float a = (k + 0.35f * (float)rnd.NextDouble()) / count * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float h = 0.55f + 0.45f * (float)rnd.NextDouble();
            float lean = (14f + 16f * (float)rnd.NextDouble()) * Mathf.Deg2Rad;
            float br = 0.13f + 0.07f * (float)rnd.NextDouble();
            Vector3 axis = (d * Mathf.Sin(lean) + Vector3.up * Mathf.Cos(lean)).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            Vector3 fwd = Vector3.Cross(side, axis).normalized;
            Vector3 foot = d * 1f - Vector3.up * 0.1f;
            int b0 = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                // thick low, a rounded tip: r(t) = br · √(1 − t²) · (1 − 0.35 t)
                float rr = br * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) * (1f - 0.35f * t);
                Vector3 c = foot + axis * (h * t);
                for (int i = 0; i < sides; i++)
                {
                    float q = i / (float)sides * Mathf.PI * 2f;
                    Vector3 radial = side * Mathf.Cos(q) + fwd * Mathf.Sin(q);
                    v.Add(c + radial * rr);
                    n.Add((radial + axis * (t * 1.2f)).normalized);
                    uv.Add(new Vector2(0.85f + 0.15f * t, 0f));
                }
            }
            int tip = v.Count;
            v.Add(foot + axis * (h * 1.02f)); n.Add(axis); uv.Add(new Vector2(1f, 1f));
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < sides; i++)
                {
                    int i1 = (i + 1) % sides;
                    int p00 = b0 + r * sides + i, p01 = b0 + r * sides + i1, p10 = b0 + (r + 1) * sides + i, p11 = b0 + (r + 1) * sides + i1;
                    tris.Add(p00); tris.Add(p10); tris.Add(p11);
                    tris.Add(p00); tris.Add(p11); tris.Add(p01);
                }
            // the last ring closes on the tip, the foot on its centre
            for (int i = 0; i < sides; i++) { int i1 = (i + 1) % sides; tris.Add(b0 + rings * sides + i); tris.Add(tip); tris.Add(b0 + rings * sides + i1); }
            int fc = v.Count;
            v.Add(foot); n.Add(-axis); uv.Add(new Vector2(0.85f, 0f));
            for (int i = 0; i < sides; i++) { int i1 = (i + 1) % sides; tris.Add(b0 + i); tris.Add(b0 + i1); tris.Add(fc); }
        }
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ the carry sigil
    /// <summary>
    /// A flat band following the rounded rectangle of half extents (<paramref name="hx"/>, <paramref name="hz"/>) and corner radius
    /// <paramref name="r"/> (MeshFactory.TrackPoint: from the front centre, clockwise seen from above), <paramref name="width"/> wide, facing up;
    /// uv.x = the arc-length fraction, uv.y 0 inner .. 1 outer. Not cached (every island owns its outline).
    /// </summary>
    public static Mesh TrackBand(float hx, float hz, float r, float width, int segs)
    {
        var v = new Vector3[(segs + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        float half = width * 0.5f;
        for (int i = 0; i <= segs; i++)
        {
            Vector3 nn;
            Vector3 p = MeshFactory.TrackPoint(hx, hz, r, i / (float)segs, out nn);
            v[i * 2] = p - nn * half; v[i * 2 + 1] = p + nn * half;
            n[i * 2] = Vector3.up; n[i * 2 + 1] = Vector3.up;
            uv[i * 2] = new Vector2(i / (float)segs, 0f); uv[i * 2 + 1] = new Vector2(i / (float)segs, 1f);
        }
        var tris = new List<int>(segs * 6);
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }
        var m = new Mesh { name = "trackband", vertices = v, normals = n, uv = uv };
        m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A unit quad in the XY plane centred on its pivot, double sided (camera-facing sprites).</summary>
    public static Mesh Sprite
    {
        get
        {
            Mesh cached;
            if (cache.TryGetValue("sprite", out cached) && cached != null) return cached;
            var m = new Mesh { name = "magic_sprite" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            cache["sprite"] = m;
            return m;
        }
    }

    /// <summary>A flat ring (radius <paramref name="inner"/>..1, XZ plane, facing up; uv.y 0 inner .. 1 outer: the InkFx ring shape).</summary>
    public static Mesh FlatRing(float inner = 0.6f, int segs = 64) { return MeshFactory.Ring(inner, 1f, segs); }

    /// <summary>Makes every triangle face its vertices' normals (the builders above write normals first and let this fix the winding).</summary>
    static void FixWinding(Mesh m)
    {
        var v = m.vertices; var n = m.normals; var t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Vector3 avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
            // Unity's front faces are clockwise seen from the front: their cross product points toward the viewer, along the normal
            if (Vector3.Dot(fn, avg) < 0f) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
        }
        m.triangles = t;
    }
}
