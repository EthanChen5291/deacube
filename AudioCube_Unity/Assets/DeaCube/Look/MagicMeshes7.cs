using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 package W (SPEC v7 §6): the procedural meshes of the v7 world magic. The TERRACE under a grid whose ground is not the sea's level
/// (<see cref="Terrace"/>: a stone mesa of rounded-rectangle drums, one per StairStepRise, each drum's top edge bevelled in ink so the strata
/// read as ink bands, the walls leaning out a little toward the foot; flat-faced for the Toon bands), its scalloped foam ring at the waterline
/// (<see cref="FoamRect"/>), a flat rounded-rectangle fill (<see cref="RoundRectFill"/>: the glass panes, the sepia flash, the selection
/// sheen) and the carpet roll of the flow ribbon (<see cref="Roll"/>).
/// </summary>
public static partial class MagicMeshes
{
    /// <summary>The outline of a rounded rectangle (half extents <paramref name="hx"/> x <paramref name="hz"/>, corner radius <paramref name="r"/>),
    /// counter-clockwise seen from above starting on the east side, <paramref name="perCorner"/> + 1 points per corner; <paramref name="normals"/>
    /// gets the outward normal of each point.</summary>
    public static void RoundRect(float hx, float hz, float r, int perCorner, List<Vector3> pts, List<Vector3> normals)
    {
        pts.Clear(); normals.Clear();
        r = Mathf.Clamp(r, 0.01f, Mathf.Min(hx, hz) - 0.01f);
        Vector2[] centres = { new Vector2(hx - r, hz - r), new Vector2(-hx + r, hz - r), new Vector2(-hx + r, -hz + r), new Vector2(hx - r, -hz + r) };
        for (int c = 0; c < 4; c++)
            for (int k = 0; k <= perCorner; k++)
            {
                float a = (c * 90f + 90f * k / perCorner) * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                pts.Add(new Vector3(centres[c].x, 0f, centres[c].y) + n * r);
                normals.Add(n);
            }
    }

    /// <summary>
    /// A terrace mesa, its top face at y = 0 going down <paramref name="depth"/>: drums of <paramref name="band"/> height (the stair step's rise:
    /// the strata line up with the ground levels), each one a rounded-rectangle ring (half extents <paramref name="hx"/> x <paramref name="hz"/>,
    /// corner radius <paramref name="r"/>) leaning out 4 % toward the foot, seeded ±3 cm per drum (hand-cut stone); the top 5 cm of every drum
    /// but the first is bevelled in and belongs to submesh 1 (the ink band), the rest to submesh 0 (stone). A flat top cap.
    /// </summary>
    public static Mesh Terrace(float hx, float hz, float r, float depth, float band, int seed)
    {
        var rnd = new System.Random(seed);
        var outline = new List<Vector3>(); var nrm = new List<Vector3>();
        RoundRect(hx, hz, r, 4, outline, nrm);
        int np = outline.Count;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
        var stone = new List<int>(); var ink = new List<int>();
        // the top cap (a fan: what shows between a lifted platform and its terrace)
        int c0 = v.Count;
        v.Add(Vector3.zero); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < np; i++) { v.Add(outline[i] * 0.985f); n.Add(Vector3.up); uv.Add(new Vector2(0.5f, 0.5f)); }
        for (int i = 0; i < np; i++) { stone.Add(c0); stone.Add(c0 + 1 + (i + 1) % np); stone.Add(c0 + 1 + i); }
        float y = 0f; int drum = 0;
        band = Mathf.Max(0.1f, band);
        while (y > -depth)
        {
            float yTop = y, yBot = Mathf.Max(-depth, y - band);
            float j = drum == 0 ? 0f : ((float)rnd.NextDouble() - 0.4f) * 0.06f;
            float grow0 = 0.04f * -yTop + j, grow1 = 0.04f * -yBot + j;
            float bevel = drum == 0 ? 0f : Mathf.Min(0.055f, (yTop - yBot) * 0.3f);
            for (int i = 0; i < np; i++)
            {
                int i1 = (i + 1) % np;
                Vector3 p0 = outline[i], p1 = outline[i1], d0 = nrm[i], d1 = nrm[i1];
                if (bevel > 0f)
                {
                    // the ink band: a bevel from 4 cm inside the wall at the drum's top out to the wall 5.5 cm lower (a dark groove line)
                    Vector3 a = p0 + d0 * (grow0 - 0.04f) + Vector3.up * yTop, b = p1 + d1 * (grow0 - 0.04f) + Vector3.up * yTop;
                    Vector3 c = p1 + d1 * grow0 + Vector3.up * (yTop - bevel), e = p0 + d0 * grow0 + Vector3.up * (yTop - bevel);
                    Quad(v, n, uv, ink, a, b, c, e);
                }
                {
                    Vector3 a = p0 + d0 * grow0 + Vector3.up * (yTop - bevel), b = p1 + d1 * grow0 + Vector3.up * (yTop - bevel);
                    Vector3 c = p1 + d1 * grow1 + Vector3.up * yBot, e = p0 + d0 * grow1 + Vector3.up * yBot;
                    Quad(v, n, uv, stone, a, b, c, e);
                }
            }
            y = yBot; drum++;
        }
        var m = new Mesh { name = "terrace" };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        m.subMeshCount = 2;
        m.SetTriangles(stone, 0); m.SetTriangles(ink, 1);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A foam ring round a rounded rectangle lying on y = 0, facing up: from <paramref name="inner"/> inside the outline out to a scalloped
    /// edge <paramref name="outer"/> .. 1.6 x <paramref name="outer"/> outside it (one lobe per ≈ 0.7 u of the outline).</summary>
    public static Mesh FoamRect(float hx, float hz, float r, float inner, float outer)
    {
        var outline = new List<Vector3>(); var nrm = new List<Vector3>();
        RoundRect(hx, hz, r, 6, outline, nrm);
        // resample the outline evenly by arc length (the lobes then have one size)
        float per = 0f;
        for (int i = 0; i < outline.Count; i++) per += (outline[(i + 1) % outline.Count] - outline[i]).magnitude;
        int lobes = Mathf.Max(8, Mathf.RoundToInt(per / 0.7f));
        const int perLobe = 6;
        int segs = lobes * perLobe;
        var v = new Vector3[(segs + 1) * 2]; var n = new Vector3[v.Length]; var uv = new Vector2[v.Length];
        int k = 0; float acc = 0f, next = 0f, step = per / segs;
        for (int s = 0; s <= segs; s++)
        {
            float want = s * step;
            while (k < outline.Count - 1 && acc + (outline[k + 1] - outline[k]).magnitude < want) { acc += (outline[k + 1] - outline[k]).magnitude; k++; }
            Vector3 a = outline[k % outline.Count], b = outline[(k + 1) % outline.Count];
            float seg = Mathf.Max(1e-5f, (b - a).magnitude);
            float t = Mathf.Clamp01((want - acc) / seg);
            Vector3 p = Vector3.Lerp(a, b, t);
            Vector3 d = Vector3.Lerp(nrm[k % nrm.Count], nrm[(k + 1) % nrm.Count], t).normalized;
            float lobe = (s % perLobe) / (float)perLobe;
            float ro = outer * (1f + 0.6f * Mathf.Sin(lobe * Mathf.PI));
            v[s * 2] = p - d * inner; v[s * 2 + 1] = p + d * ro;
            n[s * 2] = Vector3.up; n[s * 2 + 1] = Vector3.up;
            uv[s * 2] = new Vector2(0.9f, s / (float)segs); uv[s * 2 + 1] = new Vector2(1f, s / (float)segs);
        }
        next = 0f;
        var tris = new List<int>(segs * 6);
        for (int s = 0; s < segs; s++)
        {
            int a = s * 2, b = a + 1, c = a + 2, d = a + 3;
            tris.Add(a); tris.Add(d); tris.Add(b);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }
        var m = new Mesh { name = "foamrect", vertices = v, normals = n, uv = uv };
        m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A flat rounded rectangle on y = 0 facing up (a fan), uv 0..1 over its bounds. Cached by size.</summary>
    public static Mesh RoundRectFill(float hx, float hz, float r)
    {
        string key = "rrfill_" + hx.ToString("F2") + "_" + hz.ToString("F2") + "_" + r.ToString("F2");
        Mesh cached;
        if (cache.TryGetValue(key, out cached) && cached != null) return cached;
        var outline = new List<Vector3>(); var nrm = new List<Vector3>();
        RoundRect(hx, hz, r, 6, outline, nrm);
        int np = outline.Count;
        var v = new Vector3[np + 1]; var n = new Vector3[np + 1]; var uv = new Vector2[np + 1];
        v[0] = Vector3.zero; n[0] = Vector3.up; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < np; i++)
        {
            v[i + 1] = outline[i]; n[i + 1] = Vector3.up;
            uv[i + 1] = new Vector2(outline[i].x / (2f * hx) + 0.5f, outline[i].z / (2f * hz) + 0.5f);
        }
        var t = new int[np * 3];
        for (int i = 0; i < np; i++) { t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % np; t[i * 3 + 2] = 1 + i; }
        var m = new Mesh { name = key, vertices = v, normals = n, uv = uv, triangles = t };
        FixWinding(m);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>A unit cylinder along x (length 1, radius 1, 16 sides, capped; uv.y round it): the flow ribbon's carpet roll.</summary>
    public static Mesh Roll()
    {
        Mesh cached;
        if (cache.TryGetValue("roll", out cached) && cached != null) return cached;
        const int sides = 16;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            var d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
            v.Add(new Vector3(-0.5f, 0f, 0f) + d); n.Add(d); uv.Add(new Vector2(0f, i / (float)sides));
            v.Add(new Vector3(0.5f, 0f, 0f) + d); n.Add(d); uv.Add(new Vector2(1f, i / (float)sides));
        }
        for (int i = 0; i < sides; i++) { int a = i * 2; tris.Add(a); tris.Add(a + 1); tris.Add(a + 3); tris.Add(a); tris.Add(a + 3); tris.Add(a + 2); }
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -0.5f : 0.5f;
            var nn = side == 0 ? Vector3.left : Vector3.right;
            int c = v.Count;
            v.Add(new Vector3(x, 0f, 0f)); n.Add(nn); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++) { float a = i / (float)sides * Mathf.PI * 2f; v.Add(new Vector3(x, Mathf.Cos(a), Mathf.Sin(a))); n.Add(nn); uv.Add(new Vector2(0.5f, 0.5f)); }
            for (int i = 0; i < sides; i++) { tris.Add(c); tris.Add(c + 1 + i); tris.Add(c + 1 + (i + 1) % sides); }
        }
        var m = new Mesh { name = "magic_roll" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        FixWinding(m);
        m.RecalculateBounds();
        cache["roll"] = m;
        return m;
    }
}
