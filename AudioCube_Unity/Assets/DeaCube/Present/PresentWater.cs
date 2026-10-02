using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v9 (W; the user: "… islands appearing from the watter as the measures pass (with water animations)"; "take note from games like geometry dash
/// where it's not just the music but the effects too"): the present mode's WATER, in the toon / ink look and on the beat. For every riser of
/// <see cref="PresentSea"/> (an island, a Moon, a column's floor), as a PURE FUNCTION of the song beat around its breach beat (pause freezes it,
/// seek / replay / the loop land exactly):
/// - the swell: the water bulges up in a low dome where it is about to break the surface (the last ~0.3 s before);
/// - the breach: a foam ring hugging its footprint at the waterline, spray droplets thrown up and out from its edges that fall back to the sea;
/// - water sheeting off its sides as it climbs: a streaked sheet pouring from its top edge down to the sea whose top then falls away under
///   gravity (the island comes out dry from the top down), and drops dribbling off its edges;
/// - a wave pushing outward: a low crest ring (3D, flat colour with an ink rim) spreading a few units past the footprint and settling;
/// - ring ripples on the beat: one at the breach and one on each of the next two beats (ink rings in its colour).
/// The spread is in world units past the footprint (a long keyboard or a floor gets the same few units, not a giant hoop). Every piece is a
/// pooled renderer (droplet balls, rings, swell domes, crests, sheets) posed per frame through one
/// MaterialPropertyBlock: no allocation per frame. Lives under the presentation's root on the world layer (no colliders).
/// </summary>
public sealed class PresentWater
{
    // ---------------------------------------------------------------- tuning
    public static float Gravity = 17f;                  // u/s² (snappy toon droplets)
    public static float SwellSeconds = 0.32f;           // the patch before the breach
    public static float FoamSeconds = 0.9f, CrestSeconds = 1.1f, RippleSeconds = 0.95f;
    public static float SheetAfterArrive = 0.25f;       // the sheet is gone this long after the riser arrived (s)
    /// <summary>The sheet: it keeps pouring off the top edge this long (s), then its top falls away under SheetFall (u/s²); its opacity.</summary>
    public static float SheetHold = 0.1f, SheetFall = 13f, SheetAlpha = 0.42f;
    /// <summary>How far the crest wave, the ripples and the foam ring spread past the footprint (u; the crest a little more for a big one).</summary>
    public static float CrestSpread = 2.6f, RippleSpread = 2.4f, FoamSpread = 1.2f;
    public static int RipplesPerBreach = 3;             // the breach and the next beats
    public static readonly Color Foam = new Color(0.97f, 0.95f, 1f, 1f);
    public static readonly Color Spray = new Color(0.90f, 0.88f, 1f, 1f);

    const int DropCap = 360, RingCap = 56, DiscCap = 16, CrestCap = 18, SheetCap = 14;
    const float SeaY = PresentSea.SeaY;

    readonly Transform root;
    readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
    readonly Material dropMat, ringMat, discMat, crestMat, sheetMat;
    readonly Mesh ballMesh, ringMesh, crestMesh, sheetMesh;
    readonly Pool drops, rings, discs, crests, sheets;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity"), StId = Shader.PropertyToID("_MainTex_ST");
    static Texture2D streakTex;

    /// <summary>Pieces drawn this frame (diagnostics) and the risers whose effects are live.</summary>
    public int LiveRisers { get; private set; }
    public int Drops => drops.used;
    public int Rings => rings.used;
    public int Crests => crests.used;
    public int Sheets => sheets.used;
    public int Discs => discs.used;
    /// <summary>Most pieces of each kind at once since the last ResetStats (tests: the pools never ran out).</summary>
    public int MaxDrops, MaxRings, Starved;
    /// <summary>True when a pool grew this frame (a new piece was made: an allocation the steady state never sees).</summary>
    public bool Grew { get; private set; }

    sealed class Pool
    {
        public readonly List<Transform> t = new List<Transform>();
        public readonly List<MeshRenderer> r = new List<MeshRenderer>();
        public int used, shown;
        public readonly int cap;
        public Pool(int cap) { this.cap = cap; }
    }

    public PresentWater(Transform parent)
    {
        var go = new GameObject("PresentWater");
        go.transform.SetParent(parent, false);
        root = go.transform;
        ballMesh = Ball(2);
        ringMesh = MeshFactory.Ring(0.6f, 1f, 64);
        crestMesh = Crest(56);
        sheetMesh = Skirt();
        dropMat = Fx.InkMaterial(5); dropMat.renderQueue = 3010;
        ringMat = Fx.InkMaterial(1); ringMat.renderQueue = 3002;
        discMat = Fx.InkMaterial(5); discMat.renderQueue = 3001;
        crestMat = Fx.InkMaterial(5); crestMat.renderQueue = 3004;
        sheetMat = Fx.Alpha(StreakTexture(), Foam); sheetMat.renderQueue = 3006;
        drops = new Pool(DropCap); rings = new Pool(RingCap); discs = new Pool(DiscCap); crests = new Pool(CrestCap); sheets = new Pool(SheetCap);
        // warm the pools (a busy column's splash at once): no piece is made mid-song in the usual case
        Warm(drops, ballMesh, dropMat, 160); Warm(rings, ringMesh, ringMat, 28); Warm(discs, ballMesh, discMat, 8); Warm(crests, crestMesh, crestMat, 10); Warm(sheets, sheetMesh, sheetMat, 8);
    }

    void Warm(Pool pool, Mesh mesh, Material mat, int n)
    {
        for (int i = pool.t.Count; i < n && i < pool.cap; i++) Make(pool, mesh, mat);
    }

    void Make(Pool pool, Mesh mesh, Material mat)
    {
        var go = new GameObject(mesh != null ? mesh.name : "piece");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.SetActive(false);
        pool.t.Add(go.transform); pool.r.Add(mr);
    }

    public void ResetStats() { MaxDrops = 0; MaxRings = 0; Starved = 0; }

    /// <summary>Poses every effect for song beat <paramref name="b"/> (call once per frame while presenting); breaches before
    /// <paramref name="minBreach"/> are not shown (a looping song's later passes: the first column never went under again).</summary>
    public void Show(PresentSea sea, double b, double bps, double minBreach = double.NegativeInfinity)
    {
        drops.used = 0; rings.used = 0; discs.used = 0; crests.used = 0; sheets.used = 0;
        Grew = false;
        int live = 0;
        if (sea != null && bps > 0.0)
        {
            double pre = SwellSeconds * bps, post = Math.Max(1.7, (RipplesPerBreach - 1) / 1.0 + RippleSeconds * bps + 0.2) ;
            for (int i = 0; i < sea.Count; i++)
            {
                var r = sea[i];
                if (r.breach < minBreach) continue;
                double dt = b - r.breach;
                if (dt < -pre || dt > Math.Max(post, (r.Arrive - r.breach) + (SheetAfterArrive + 1.6) * bps)) continue;
                live++;
                Riser(sea, i, r, b, (float)(dt / bps), bps);
            }
        }
        LiveRisers = live;
        Finish(drops); Finish(rings); Finish(discs); Finish(crests); Finish(sheets);
        if (drops.used > MaxDrops) MaxDrops = drops.used;
        if (rings.used > MaxRings) MaxRings = rings.used;
    }

    /// <summary>Hides every piece (exit, a replay's veil).</summary>
    public void HideAll()
    {
        drops.used = 0; rings.used = 0; discs.used = 0; crests.used = 0; sheets.used = 0;
        Finish(drops); Finish(rings); Finish(discs); Finish(crests); Finish(sheets);
    }

    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        foreach (var m in new[] { dropMat, ringMat, discMat, crestMat, sheetMat }) if (m != null) UnityEngine.Object.Destroy(m);
        if (ballMesh != null) UnityEngine.Object.Destroy(ballMesh);
        if (crestMesh != null) UnityEngine.Object.Destroy(crestMesh);
        if (sheetMesh != null) UnityEngine.Object.Destroy(sheetMesh);
    }

    // ---------------------------------------------------------------- one riser's effects at t seconds after its breach
    void Riser(PresentSea sea, int i, PresentSea.Riser r, double b, float t, double bps)
    {
        float hx = r.size.x * 0.5f, hz = r.size.y * 0.5f;
        float big = Mathf.Clamp(Mathf.Sqrt(r.size.x * r.size.y) / 4.5f, 0.7f, 1.9f);   // how big a splash this footprint makes
        bool floor = r.kind == PresentSea.Kind.Floor;
        Color tint = floor ? Color.Lerp(Look.SeaLine, Foam, 0.5f) : Color.Lerp(r.color, Foam, 0.35f);
        Vector3 c = new Vector3(r.centre.x, SeaY, r.centre.z);
        uint seed = (uint)(i * 7919 + r.col * 104729 + 17);

        // the swell: the water bulges (a low dome) where it is about to break the surface
        if (t < 0.06f)
        {
            float u = Mathf.Clamp01(1f + t / SwellSeconds);
            float e = Ease.OutCubic(u), fade = t > 0f ? 1f - t / 0.06f : 1f;
            float hgt = 0.45f * Mathf.Sqrt(big) * e;
            Pose(discs, ballMesh, discMat, c, Quaternion.identity, new Vector3((hx + 0.3f) * 2f * Mathf.Lerp(0.55f, 1f, e), Mathf.Max(0.02f, hgt * 2f), (hz + 0.3f) * 2f * Mathf.Lerp(0.55f, 1f, e)), Color.Lerp(Look.SeaSwell, Foam, 0.3f), 0.9f * e * fade);
            if (t < 0f) return;
        }

        // the foam ring at the waterline, hugging the footprint
        if (t < FoamSeconds)
        {
            float u = t / FoamSeconds, e = Ease.OutCubic(u);
            Pose(rings, ringMesh, ringMat, c + Vector3.up * 0.03f, Quaternion.identity, new Vector3(hx + 0.45f + FoamSpread * e, 1f, hz + 0.45f + FoamSpread * e), Foam, 1f - u);
        }

        // ripples on the beat: the breach and the next beats, spreading in its colour
        for (int k = 0; k < RipplesPerBreach; k++)
        {
            float tk = t - (float)(k / bps);
            if (tk < 0f || tk > RippleSeconds) continue;
            float u = tk / RippleSeconds, e = Ease.OutCubic(u);
            float grow = 0.7f + (RippleSpread + 0.5f * k) * e;
            Pose(rings, ringMesh, ringMat, c + Vector3.up * (0.02f + 0.004f * k), Quaternion.identity, new Vector3(hx + grow, 1f, hz + grow), Color.Lerp(tint, Look.SeaLine, 0.25f * k), (1f - u) * (k == 0 ? 0.9f : 0.7f));
        }

        // the wave pushing outward: a low crest ring spreading over the sea, settling
        if (t < CrestSeconds)
        {
            float u = t / CrestSeconds, e = Ease.OutCubic(u);
            float grow = 0.5f + (CrestSpread + 0.3f * Mathf.Min(8f, Mathf.Max(hx, hz))) * e;
            float hgt = 0.3f * Mathf.Sqrt(big) * (1f - e) * Mathf.Clamp01(t / 0.08f);
            Pose(crests, crestMesh, crestMat, c, Quaternion.identity, new Vector3(hx + grow, Mathf.Max(0.01f, hgt), hz + grow), new Color(Look.SeaLine.r, Look.SeaLine.g, Look.SeaLine.b, 0.85f), (1f - u) * (1f - u));
        }

        // water sheeting off its sides while it climbs: it pours from its top edge, then the sheet's top falls away under gravity (the island
        // comes out dry from the top down); not for a floor (a long low slab: the spray and the rings carry it)
        double arrive = r.Arrive;
        float tArrive = (float)((arrive - r.breach) / bps);
        if (!floor && t < tArrive + SheetAfterArrive)
        {
            float topNow = r.top - r.hide * PresentSea.RiseDepth(r, b);
            float span = topNow - SeaY - 0.05f;
            float tf = Mathf.Max(0f, t - SheetHold);
            float lag = Mathf.Min(span, 0.5f * SheetFall * tf * tf);
            float hgt = span - lag;
            if (hgt > 0.05f)
            {
                float a = SheetAlpha * Mathf.Clamp01(t / 0.05f) * (1f - Mathf.SmoothStep(0.55f, 1f, span > 1e-3f ? lag / span : 1f));
                var sc = new Vector3(r.size.x + 0.06f, hgt, r.size.y + 0.06f);
                Pose(sheets, sheetMesh, sheetMat, new Vector3(r.centre.x, topNow - lag - 0.02f, r.centre.z), Quaternion.identity, sc, new Color(Foam.r, Foam.g, Foam.b, a), -1f, (r.size.x + r.size.y) * 0.18f, -t * 2.6f);
            }
        }

        // spray: thrown up and out from its edges at the breach
        int nSpray = Mathf.Clamp(Mathf.RoundToInt((r.size.x + r.size.y) * 1.1f), 10, floor ? 30 : 24);
        for (int j = 0; j < nSpray; j++)
        {
            float t0 = 0.11f * Hash(seed, (uint)j * 3u);
            float tau = t - t0;
            if (tau <= 0f) continue;
            Vector3 p0, nrm; Edge(hx, hz, Hash(seed, (uint)j * 3u + 1u), out p0, out nrm);
            float up = Mathf.Lerp(3.6f, 7.4f, Hash(seed, (uint)j * 3u + 2u)) * Mathf.Sqrt(big);
            float out_ = Mathf.Lerp(0.6f, 2.4f, Hash(seed ^ 0x5bd1e995u, (uint)j)) * big;
            Vector3 v = nrm * out_ + Vector3.up * up;
            float life = 2f * up / Gravity;
            if (tau >= life) continue;
            Vector3 p = c + p0 + v * tau + 0.5f * Gravity * tau * tau * Vector3.down;
            float size = Mathf.Lerp(0.13f, 0.3f, Hash(seed ^ 0x27d4eb2du, (uint)j)) * Mathf.Sqrt(big);
            float shrink = 1f - Mathf.Clamp01((tau / life - 0.75f) / 0.25f) * 0.6f;
            Pose(drops, ballMesh, dropMat, p, Quaternion.identity, Vector3.one * size * shrink, Spray, 1f);
        }

        // drops dribbling off its edges as it climbs (released from the rising top edge, falling back to the sea)
        if (!floor || r.size.x * r.size.y < 400f)
        {
            int nDrib = Mathf.Clamp(Mathf.RoundToInt((r.size.x + r.size.y) * 0.8f), 6, 18);
            for (int j = 0; j < nDrib; j++)
            {
                float tr = Mathf.Lerp(0.03f, Mathf.Max(0.1f, tArrive * 0.8f), Hash(seed ^ 0x9e3779b9u, (uint)j));
                float tau = t - tr;
                if (tau <= 0f) continue;
                double br = r.breach + tr * bps;
                float yTop = r.top - r.hide * PresentSea.RiseDepth(r, br);
                Vector3 p0, nrm; Edge(hx, hz, Hash(seed ^ 0x85ebca6bu, (uint)j), out p0, out nrm);
                Vector3 v = nrm * Mathf.Lerp(0.3f, 1.1f, Hash(seed ^ 0xc2b2ae35u, (uint)j)) + Vector3.up * Mathf.Lerp(0f, 1.2f, Hash(seed ^ 0x165667b1u, (uint)j));
                Vector3 p = new Vector3(c.x + p0.x, yTop - 0.05f, c.z + p0.z) + v * tau + 0.5f * Gravity * tau * tau * Vector3.down;
                if (p.y <= SeaY) continue;
                float size = Mathf.Lerp(0.09f, 0.18f, Hash(seed ^ 0xd3a2646cu, (uint)j));
                Pose(drops, ballMesh, dropMat, p, Quaternion.identity, Vector3.one * size, Spray, 1f);
            }
        }
    }

    /// <summary>A point on the footprint's edge (local to its centre, at the waterline) and its outward normal, for a parameter in [0, 1).</summary>
    static void Edge(float hx, float hz, float u, out Vector3 p, out Vector3 n)
    {
        float per = 4f * (hx + hz), d = u * per;
        if (d < 2f * hx) { p = new Vector3(-hx + d, 0f, -hz); n = new Vector3(0f, 0f, -1f); }
        else if ((d -= 2f * hx) < 2f * hz) { p = new Vector3(hx, 0f, -hz + d); n = new Vector3(1f, 0f, 0f); }
        else if ((d -= 2f * hz) < 2f * hx) { p = new Vector3(hx - d, 0f, hz); n = new Vector3(0f, 0f, 1f); }
        else { d -= 2f * hx; p = new Vector3(-hx, 0f, hz - d); n = new Vector3(-1f, 0f, 0f); }
        // corners throw diagonally a little
        n = (n + new Vector3(p.x / Mathf.Max(0.1f, hx), 0f, p.z / Mathf.Max(0.1f, hz)) * 0.25f).normalized;
    }

    /// <summary>A stable pseudo-random value in [0, 1) for (seed, i).</summary>
    static float Hash(uint seed, uint i)
    {
        unchecked
        {
            uint h = seed * 747796405u + i * 2891336453u + 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // ---------------------------------------------------------------- pooled pieces
    void Pose(Pool pool, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, Color col, float life, float tileU = 1f, float offV = 0f)
    {
        if (pool.used >= pool.cap) { Starved++; return; }
        if (pool.used >= pool.t.Count) { Make(pool, mesh, mat); Grew = true; }
        int k = pool.used++;
        var tr = pool.t[k]; var r = pool.r[k];
        tr.SetPositionAndRotation(pos, rot);
        tr.localScale = scale;
        mpb.Clear();
        mpb.SetColor(ColorId, col);
        if (life >= 0f) mpb.SetFloat(IntensityId, life);
        else mpb.SetVector(StId, new Vector4(tileU, 1f, 0f, offV));
        r.SetPropertyBlock(mpb);
        if (k >= pool.shown && !tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
    }

    static void Finish(Pool pool)
    {
        for (int k = pool.used; k < pool.shown && k < pool.t.Count; k++) if (pool.t[k].gameObject.activeSelf) pool.t[k].gameObject.SetActive(false);
        pool.shown = pool.used;
    }

    // ---------------------------------------------------------------- meshes and the streak texture (built once per presentation)
    /// <summary>A low-poly ball: an octahedron subdivided <paramref name="levels"/> times, normalised (a toon droplet).</summary>
    static Mesh Ball(int levels)
    {
        var v = new List<Vector3> { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left, Vector3.down };
        var tri = new List<int> { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 };
        for (int l = 0; l < levels; l++)
        {
            var next = new List<int>(tri.Count * 4);
            var mid = new Dictionary<long, int>();
            Func<int, int, int> M = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int idx;
                if (mid.TryGetValue(key, out idx)) return idx;
                idx = v.Count; v.Add(((v[a] + v[b]) * 0.5f).normalized); mid[key] = idx; return idx;
            };
            for (int i = 0; i < tri.Count; i += 3)
            {
                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                int ab = M(a, b), bc = M(b, c), ca = M(c, a);
                next.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
            }
            tri = next;
        }
        var m = new Mesh { name = "pw_drop" };
        var verts = new Vector3[v.Count]; for (int i = 0; i < v.Count; i++) verts[i] = v[i] * 0.5f;
        m.vertices = verts; m.normals = v.ToArray(); m.triangles = tri.ToArray();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>The wave's crest: a ring (radius 0.78 .. 1) whose cross-section rises to a sharp crest (height 1) at 0.9 — a hard crease on top.</summary>
    static Mesh Crest(int segs)
    {
        float r0 = 0.78f, rc = 0.9f, r1 = 1f;
        var verts = new Vector3[(segs + 1) * 4]; var norms = new Vector3[verts.Length]; var uvs = new Vector2[verts.Length];
        var tris = new int[segs * 12];
        float sIn = Mathf.Atan2(1f, rc - r0), sOut = Mathf.Atan2(1f, r1 - rc);
        for (int i = 0; i <= segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            Vector3 radial = new Vector3(cs, 0f, sn);
            Vector3 nIn = (-radial * Mathf.Sin(sIn) + Vector3.up * Mathf.Cos(sIn)).normalized;
            Vector3 nOut = (radial * Mathf.Sin(sOut) + Vector3.up * Mathf.Cos(sOut)).normalized;
            int k = i * 4;
            verts[k] = radial * r0; verts[k + 1] = radial * rc + Vector3.up; verts[k + 2] = radial * rc + Vector3.up; verts[k + 3] = radial * r1;
            norms[k] = nIn; norms[k + 1] = nIn; norms[k + 2] = nOut; norms[k + 3] = nOut;
            uvs[k] = new Vector2(i / (float)segs, 0f); uvs[k + 1] = new Vector2(i / (float)segs, 0.5f); uvs[k + 2] = uvs[k + 1]; uvs[k + 3] = new Vector2(i / (float)segs, 1f);
        }
        for (int i = 0; i < segs; i++)
        {
            int k = i * 4, nk = k + 4, t = i * 12;
            tris[t] = k; tris[t + 1] = nk + 1; tris[t + 2] = k + 1;
            tris[t + 3] = k; tris[t + 4] = nk; tris[t + 5] = nk + 1;
            tris[t + 6] = k + 2; tris[t + 7] = nk + 2; tris[t + 8] = k + 3;
            tris[t + 9] = k + 3; tris[t + 10] = nk + 2; tris[t + 11] = nk + 3;
        }
        var m = new Mesh { name = "pw_crest", vertices = verts, normals = norms, uv = uvs, triangles = tris };
        m.RecalculateBounds();
        return m;
    }

    /// <summary>The sheet: four walls around a unit square, from y = 0 (its top edge) down to y = −1; uv.x runs round it (0..4), uv.y top 0 .. bottom 1.</summary>
    static Mesh Skirt()
    {
        var verts = new Vector3[16]; var uvs = new Vector2[16]; var norms = new Vector3[16]; var tris = new int[24];
        Vector3[] c = { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        Vector3[] nrm = { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
        for (int s = 0; s < 4; s++)
        {
            Vector3 a = c[s], b = c[(s + 1) % 4];
            int k = s * 4;
            verts[k] = a; verts[k + 1] = b; verts[k + 2] = b + Vector3.down; verts[k + 3] = a + Vector3.down;
            uvs[k] = new Vector2(s, 0f); uvs[k + 1] = new Vector2(s + 1, 0f); uvs[k + 2] = new Vector2(s + 1, 1f); uvs[k + 3] = new Vector2(s, 1f);
            for (int q = 0; q < 4; q++) norms[k + q] = nrm[s];
            int t = s * 6;
            tris[t] = k; tris[t + 1] = k + 1; tris[t + 2] = k + 2; tris[t + 3] = k; tris[t + 4] = k + 2; tris[t + 5] = k + 3;
        }
        var m = new Mesh { name = "pw_sheet", vertices = verts, uv = uvs, normals = norms, triangles = tris };
        m.RecalculateBounds();
        return m;
    }

    /// <summary>Vertical water streaks (white, alpha in bands), tiling: the sheet's texture (built once).</summary>
    static Texture2D StreakTexture()
    {
        if (streakTex != null) return streakTex;
        const int W = 64, H = 128;
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float colA = 0.25f + 0.75f * Hash(91u, (uint)(x / 3));
            float phase = Hash(17u, (uint)(x / 3)) * H, len = 18f + 70f * Hash(23u, (uint)(x / 3));
            for (int y = 0; y < H; y++)
            {
                float k = Mathf.Repeat(y + phase, H);
                float band = k < len ? 1f : 0f;
                float edge = (x % 3 == 1) ? 1f : 0.55f;
                float a = Mathf.Clamp01(0.05f + band * colA * edge * 0.85f * (Hash(57u, (uint)(x / 3)) < 0.55f ? 1f : 0.25f));
                px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        streakTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "pw_streaks", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        streakTex.SetPixels32(px);
        streakTex.Apply(false, true);
        return streakTex;
    }
}
