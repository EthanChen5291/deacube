using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime meshes: rounded boxes (soft toy-like blocks), rings, discs and quads. v6: offset rounded boxes (piano keys).</summary>
public static class MeshFactory
{
    static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    public static Mesh RoundedBox(Vector3 size, float radius, int seg = 5)
    {
        string key = $"rb_{size.x:F3}_{size.y:F3}_{size.z:F3}_{radius:F3}_{seg}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;

        Vector3 h = size * 0.5f;
        float r = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.98f);
        Vector3 inner = new Vector3(Mathf.Max(0f, h.x - r), Mathf.Max(0f, h.y - r), Mathf.Max(0f, h.z - r));

        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        foreach (var n in dirs)
        {
            Vector3 t1 = (Mathf.Abs(n.y) > 0.5f) ? Vector3.right : Vector3.up;
            Vector3 t2 = Vector3.Cross(n, t1).normalized;
            t1 = Vector3.Cross(t2, n).normalized;
            float hn = Mathf.Abs(Vector3.Dot(h, n)) ;
            float h1 = Mathf.Abs(Vector3.Dot(h, t1));
            float h2 = Mathf.Abs(Vector3.Dot(h, t2));
            int baseIndex = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg * 2f - 1f;
                for (int j = 0; j <= seg; j++)
                {
                    float v = j / (float)seg * 2f - 1f;
                    Vector3 p = n * hn + t1 * (u * h1) + t2 * (v * h2);
                    Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - q;
                    Vector3 nn = d.sqrMagnitude < 1e-8f ? n : d.normalized;
                    verts.Add(q + nn * r);
                    norms.Add(nn);
                    uvs.Add(new Vector2((u + 1f) * 0.5f, (v + 1f) * 0.5f));
                }
            }
            // winding
            bool flip;
            {
                int a = baseIndex, b = baseIndex + 1, c = baseIndex + seg + 1;
                Vector3 fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                flip = Vector3.Dot(fn, n) < 0f;
            }
            for (int i = 0; i < seg; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int a = baseIndex + i * (seg + 1) + j;
                    int b = a + 1;
                    int c = a + seg + 1;
                    int d = c + 1;
                    if (!flip) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                    else { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                }
            }
        }
        var m = new Mesh { name = key };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        cache[key] = m;
        return m;
    }

    /// <summary>v6 (package K, the keyboard island): a rounded box whose centre sits at <paramref name="centre"/> in its mesh space — a piano key
    /// whose top face stays at its tile height (TileInteraction.Top) while its body reaches down to the case. Cached like RoundedBox.</summary>
    public static Mesh RoundedBoxAt(Vector3 size, Vector3 centre, float radius, int seg = 3)
    {
        if (centre.sqrMagnitude < 1e-10f) return RoundedBox(size, radius, seg);
        string key = $"rbat_{size.x:F3}_{size.y:F3}_{size.z:F3}_{centre.x:F3}_{centre.y:F3}_{centre.z:F3}_{radius:F3}_{seg}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var src = RoundedBox(size, radius, seg);
        var v = src.vertices;
        for (int i = 0; i < v.Length; i++) v[i] += centre;
        var m = new Mesh { name = key };
        m.vertices = v; m.normals = src.normals; m.uv = src.uv; m.triangles = src.triangles;
        m.RecalculateBounds(); m.RecalculateTangents();
        cache[key] = m;
        return m;
    }

    /// <summary>Flat ring in the XZ plane. uv.x = angle fraction, uv.y = 0 at inner edge, 1 at outer edge.</summary>
    public static Mesh Ring(float inner, float outer, int segs = 72)
    {
        string key = $"ring_{inner:F3}_{outer:F3}_{segs}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var verts = new Vector3[(segs + 1) * 2]; var uvs = new Vector2[verts.Length]; var norms = new Vector3[verts.Length];
        var tris = new int[segs * 6];
        for (int i = 0; i <= segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * inner, 0, s * inner);
            verts[i * 2 + 1] = new Vector3(c * outer, 0, s * outer);
            uvs[i * 2] = new Vector2(i / (float)segs, 0f);
            uvs[i * 2 + 1] = new Vector2(i / (float)segs, 1f);
            norms[i * 2] = Vector3.up; norms[i * 2 + 1] = Vector3.up;
        }
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            tris[i * 6] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
            tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
        }
        var m = new Mesh { name = key, vertices = verts, uv = uvs, normals = norms, triangles = tris };
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>Unit quad lying flat (XZ plane), normal up, centered.</summary>
    public static Mesh FlatQuad()
    {
        if (cache.TryGetValue("flatquad", out var c) && c != null) return c;
        var m = new Mesh { name = "flatquad" };
        m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        cache["flatquad"] = m;
        return m;
    }

    /// <summary>Unit quad standing up (XY plane), pivot at the bottom centre, double sided.</summary>
    public static Mesh VerticalQuad()
    {
        if (cache.TryGetValue("vquad", out var c) && c != null) return c;
        var m = new Mesh { name = "vquad" };
        m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        cache["vquad"] = m;
        return m;
    }

    // ------------------------------------------------------------------ v2 world meshes (SPEC §2.1, §2.5)
    static void AddTri(List<int> tris, List<Vector3> verts, int a, int b, int c, Vector3 wantNormal)
    {
        Vector3 fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (Vector3.Dot(fn, wantNormal) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else { tris.Add(a); tris.Add(c); tris.Add(b); }
    }

    /// <summary>Flat-shaded regular prism (the island hub): <paramref name="sides"/> sides of circumradius <paramref name="radius"/>,
    /// centred on x/z, y from 0 to <paramref name="height"/>; one vertex points to the front (-z). Cached.</summary>
    public static Mesh Prism(int sides, float radius, float height)
    {
        sides = Mathf.Clamp(sides, 3, 64);
        string key = $"prism_{sides}_{radius:F3}_{height:F3}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = -Mathf.PI * 0.5f + i / (float)sides * Mathf.PI * 2f;
            ring[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }
        // caps (centre + rim each)
        for (int cap = 0; cap < 2; cap++)
        {
            float y = cap == 0 ? height : 0f; Vector3 n = cap == 0 ? Vector3.up : Vector3.down;
            int c0 = verts.Count;
            verts.Add(new Vector3(0f, y, 0f)); norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++) { verts.Add(ring[i] + Vector3.up * y); norms.Add(n); uvs.Add(new Vector2(0.5f + 0.5f * ring[i].x / radius, 0.5f + 0.5f * ring[i].z / radius)); }
            for (int i = 0; i < sides; i++) AddTri(tris, verts, c0, c0 + 1 + i, c0 + 1 + (i + 1) % sides, n);
        }
        // sides (4 unique verts each, flat normal)
        for (int i = 0; i < sides; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % sides];
            Vector3 n = Vector3.Cross(Vector3.up, b - a).normalized;
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(b + Vector3.up * height); verts.Add(a + Vector3.up * height);
            for (int j = 0; j < 4; j++) norms.Add(n);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            AddTri(tris, verts, i0, i0 + 1, i0 + 2, n);
            AddTri(tris, verts, i0, i0 + 2, i0 + 3, n);
        }
        var m = new Mesh { name = key };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>Flat disc in the XZ plane (Moon platform / handle), normal up, centre vertex + <paramref name="segs"/> rim vertices. Cached.</summary>
    public static Mesh Disc(float radius, int segs)
    {
        segs = Mathf.Clamp(segs, 3, 256);
        string key = $"disc_{radius:F3}_{segs}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var verts = new Vector3[segs + 1]; var uvs = new Vector2[segs + 1]; var norms = new Vector3[segs + 1];
        verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f); norms[0] = Vector3.up;
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f; float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[1 + i] = new Vector3(c * radius, 0f, s * radius);
            uvs[1 + i] = new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s);
            norms[1 + i] = Vector3.up;
        }
        var tris = new int[segs * 3];
        for (int i = 0; i < segs; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % segs; tris[i * 3 + 2] = 1 + i; }
        var m = new Mesh { name = key, vertices = verts, uv = uvs, normals = norms, triangles = tris };
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>
    /// The segmented beat ring: <paramref name="segments"/> flat arcs (4 verts + 2 tris each, gap = <paramref name="gap01"/> of a
    /// segment's angle), segment 0 starting at the front (-z) and running clockwise seen from above. uv.x 0..1 along each
    /// segment, uv.y 0 inner / 1 outer; mesh.colors initialised white (one colour per segment, 4 entries each) and
    /// MarkDynamic for per-16th colour writes. Never cached: every ring owns its colours.
    /// </summary>
    public static Mesh SegmentRing(float inner, float outer, int segments, float gap01)
    {
        segments = Mathf.Max(1, segments);
        var verts = new Vector3[segments * 4]; var uvs = new Vector2[segments * 4]; var norms = new Vector3[segments * 4]; var colors = new Color[segments * 4];
        var tris = new int[segments * 6];
        float segAngle = Mathf.PI * 2f / segments;
        float gap = Mathf.Clamp01(gap01) * segAngle * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = -Mathf.PI * 0.5f - (i * segAngle + gap);
            float a1 = -Mathf.PI * 0.5f - ((i + 1) * segAngle - gap);
            int v = i * 4;
            verts[v] = new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner);
            verts[v + 1] = new Vector3(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer);
            verts[v + 2] = new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner);
            verts[v + 3] = new Vector3(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer);
            uvs[v] = new Vector2(0f, 0f); uvs[v + 1] = new Vector2(0f, 1f); uvs[v + 2] = new Vector2(1f, 0f); uvs[v + 3] = new Vector2(1f, 1f);
            for (int j = 0; j < 4; j++) { norms[v + j] = Vector3.up; colors[v + j] = Color.white; }
            int t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }
        var m = new Mesh { name = $"segring_{segments}", vertices = verts, uv = uvs, normals = norms, colors = colors, triangles = tris };
        m.MarkDynamic();
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ v4 (SPEC v4 §3 K3): the rounded beat track, the column bands
    /// <summary>Length of the outline of a rounded rectangle with half extents (<paramref name="hx"/>, <paramref name="hz"/>) and corner radius <paramref name="r"/>.</summary>
    public static float TrackLength(float hx, float hz, float r)
    {
        r = Mathf.Clamp(r, 0f, Mathf.Min(hx, hz));
        return 4f * (hx - r) + 4f * (hz - r) + 2f * Mathf.PI * r;
    }

    /// <summary>
    /// The point at arc-length fraction <paramref name="t01"/> of a rounded rectangle's outline (XZ plane, centred on the origin), starting at the
    /// front centre (-z) and running clockwise seen from above (front → west → back → east → front), like the v2 beat ring; <paramref name="normal"/>
    /// is the outward normal there.
    /// </summary>
    public static Vector3 TrackPoint(float hx, float hz, float r, float t01, out Vector3 normal)
    {
        r = Mathf.Clamp(r, 0f, Mathf.Min(hx, hz));
        float ax = hx - r, az = hz - r, q = Mathf.PI * 0.5f * r;
        float s = Mathf.Repeat(t01, 1f) * (4f * ax + 4f * az + 4f * q);
        float inv = 1f / Mathf.Max(1e-5f, r), a;
        if (s < ax) { normal = Vector3.back; return new Vector3(-s, 0f, -hz); }                                            // front, centre → west
        s -= ax;
        if (s < q) { a = -Mathf.PI * 0.5f - s * inv; return Corner(-ax, -az, r, a, out normal); }                           // front-west corner
        s -= q;
        if (s < 2f * az) { normal = Vector3.left; return new Vector3(-hx, 0f, -az + s); }                                   // west, front → back
        s -= 2f * az;
        if (s < q) { a = Mathf.PI - s * inv; return Corner(-ax, az, r, a, out normal); }                                    // back-west corner
        s -= q;
        if (s < 2f * ax) { normal = Vector3.forward; return new Vector3(-ax + s, 0f, hz); }                                 // back, west → east
        s -= 2f * ax;
        if (s < q) { a = Mathf.PI * 0.5f - s * inv; return Corner(ax, az, r, a, out normal); }                              // back-east corner
        s -= q;
        if (s < 2f * az) { normal = Vector3.right; return new Vector3(hx, 0f, az - s); }                                    // east, back → front
        s -= 2f * az;
        if (s < q) { a = -s * inv; return Corner(ax, -az, r, a, out normal); }                                              // front-east corner
        s -= q;
        normal = Vector3.back;
        return new Vector3(Mathf.Max(0f, ax - s), 0f, -hz);                                                                 // front, east → centre
    }

    static Vector3 Corner(float cx, float cz, float r, float a, out Vector3 normal)
    {
        float c = Mathf.Cos(a), sn = Mathf.Sin(a);
        normal = new Vector3(c, 0f, sn);
        return new Vector3(cx + c * r, 0f, cz + sn * r);
    }

    /// <summary>
    /// The v4 segmented beat track: <paramref name="segments"/> pieces of a rounded-rectangle band (centre line = the outline of half extents
    /// (<paramref name="hx"/>, <paramref name="hz"/>), corner radius <paramref name="r"/>; <paramref name="width"/> wide), evenly spaced by arc length from
    /// the front centre, clockwise seen from above; gap = <paramref name="gap01"/> of a segment's length. Each segment is a strip of <paramref name="sub"/>
    /// quads (so it bends round a corner): <see cref="TrackVertsPerSegment"/> vertices in order (inner, outer) per step. mesh.colors white,
    /// MarkDynamic for per-16th colour writes. Never cached: every track owns its colours.
    /// </summary>
    public static Mesh SegmentTrack(float hx, float hz, float r, float width, int segments, float gap01, int sub = 4)
    {
        segments = Mathf.Max(1, segments); sub = Mathf.Max(1, sub);
        int per = (sub + 1) * 2;
        var verts = new Vector3[segments * per]; var uvs = new Vector2[verts.Length]; var norms = new Vector3[verts.Length]; var colors = new Color[verts.Length];
        var tris = new int[segments * sub * 6];
        float half = width * 0.5f, g = Mathf.Clamp01(gap01) * 0.5f;
        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            float s0 = (i + g) / segments, s1 = (i + 1 - g) / segments;
            int v0 = i * per;
            for (int j = 0; j <= sub; j++)
            {
                Vector3 n;
                Vector3 p = TrackPoint(hx, hz, r, Mathf.Lerp(s0, s1, j / (float)sub), out n);
                verts[v0 + j * 2] = p - n * half; verts[v0 + j * 2 + 1] = p + n * half;
                uvs[v0 + j * 2] = new Vector2(j / (float)sub, 0f); uvs[v0 + j * 2 + 1] = new Vector2(j / (float)sub, 1f);
                norms[v0 + j * 2] = Vector3.up; norms[v0 + j * 2 + 1] = Vector3.up;
                colors[v0 + j * 2] = Color.white; colors[v0 + j * 2 + 1] = Color.white;
            }
            for (int j = 0; j < sub; j++)
            {
                int a = v0 + j * 2, b = a + 1, c = a + 2, d = a + 3;
                // clockwise travel with the outer edge on the left seen from above: (a, c, b) faces up (checked by the cross product)
                Vector3 fn = Vector3.Cross(verts[c] - verts[a], verts[b] - verts[a]);
                if (fn.y >= 0f) { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
                else { tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = b; tris[t++] = d; tris[t++] = c; }
            }
        }
        var m = new Mesh { name = $"segtrack_{segments}", vertices = verts, uv = uvs, normals = norms, colors = colors, triangles = tris };
        m.MarkDynamic();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A flat polygon band (XZ plane, centred, facing up) between circumradius <paramref name="inner"/> and <paramref name="outer"/>, one vertex
    /// pointing to the front (-z) like <see cref="Prism"/> (the hub gauge's bar outlines). Cached.</summary>
    public static Mesh PolyRing(int sides, float outer, float inner)
    {
        sides = Mathf.Clamp(sides, 3, 64); inner = Mathf.Clamp(inner, 0f, outer);
        string key = $"polyring_{sides}_{outer:F3}_{inner:F3}";
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = -Mathf.PI * 0.5f + i / (float)sides * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(d * inner); verts.Add(d * outer);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2, b = a + 1, c = ((i + 1) % sides) * 2, d = c + 1;
            AddTri(tris, verts, a, c, b, Vector3.up);
            AddTri(tris, verts, b, c, d, Vector3.up);
        }
        var norms = new List<Vector3>(); foreach (var v in verts) norms.Add(Vector3.up);
        var m = new Mesh { name = key };
        m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        cache[key] = m;
        return m;
    }

    /// <summary>Vertices per segment of <see cref="SegmentTrack"/> (the colour layout: pairs 0..sub, the first half = the segment's start colour).</summary>
    public static int TrackVertsPerSegment(int sub = 4) => (Mathf.Max(1, sub) + 1) * 2;

    /// <summary>
    /// A soft rounded plate lying flat (XZ plane, centred): a <paramref name="w"/> x <paramref name="d"/> rounded rectangle (corner radius
    /// <paramref name="r"/>) at full vertex alpha, fading to alpha 0 over <paramref name="feather"/> outside it (the column bands on the sea).
    /// Vertex colours white; not cached (the caller owns and destroys it).
    /// </summary>
    public static Mesh SoftPlate(float w, float d, float r, float feather, int cornerSteps = 6)
    {
        float hx = Mathf.Max(0.05f, w * 0.5f), hz = Mathf.Max(0.05f, d * 0.5f);
        r = Mathf.Clamp(r, 0.01f, Mathf.Min(hx, hz));
        int ring = 4 * (cornerSteps + 1);
        var verts = new Vector3[1 + ring * 2]; var uvs = new Vector2[verts.Length]; var norms = new Vector3[verts.Length]; var cols = new Color[verts.Length];
        verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f); norms[0] = Vector3.up; cols[0] = Color.white;
        float[] cx = { hx - r, -(hx - r), -(hx - r), hx - r }, cz = { hz - r, hz - r, -(hz - r), -(hz - r) };
        int k = 1;
        for (int c = 0; c < 4; c++)
            for (int j = 0; j <= cornerSteps; j++)
            {
                float a = (c * 90f + j * 90f / cornerSteps) * Mathf.Deg2Rad;   // counter-clockwise seen from above, starting at the back-east corner
                Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = new Vector3(cx[c], 0f, cz[c]) + n * r;
                verts[k] = p; verts[k + ring] = p + n * feather;
                cols[k] = Color.white; cols[k + ring] = new Color(1f, 1f, 1f, 0f);
                uvs[k] = new Vector2(0.5f + p.x / (2f * hx + 2f * feather), 0.5f + p.z / (2f * hz + 2f * feather));
                uvs[k + ring] = new Vector2(0.5f + verts[k + ring].x / (2f * hx + 2f * feather), 0.5f + verts[k + ring].z / (2f * hz + 2f * feather));
                norms[k] = Vector3.up; norms[k + ring] = Vector3.up;
                k++;
            }
        var tris = new List<int>(ring * 9);
        var vl = new List<Vector3>(verts);
        for (int i = 0; i < ring; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % ring;
            AddTri(tris, vl, 0, a, b, Vector3.up);
            AddTri(tris, vl, a, a + ring, b, Vector3.up);
            AddTri(tris, vl, b, a + ring, b + ring, Vector3.up);
        }
        var m = new Mesh { name = "softplate", vertices = verts, uv = uvs, normals = norms, colors = cols };
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }
}
