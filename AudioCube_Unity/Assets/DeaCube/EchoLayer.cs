using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §6.3): one octave LAYER of one grid — "for higher notes … maybe like some other cubes above if higher and below if
/// lower octave phase in and plays it (make sure this looks good)". While octave copies / echoes live in layer <see cref="Layer"/> of a grid
/// (AudioCube.layer ≠ 0; H stands them on Magic.LayerPoint) a faint GLASS COPY of the grid shows where they play: every tile as a glass tile (a
/// faint fill, a bright rim tinted by the grid's colour, a highlight streak; the cube stands on it). The SKY layer (layer &gt; 0) floats over the
/// grid (Magic.LayerOffset) on a barely-there pane with a bright rounded rim, fine THREADS of light running down from the glass tiles the layer's
/// cubes use (and the four corners) to the tiles they mirror. The DEEP layer (layer &lt; 0) is a REFLECTION BAND just in front of and below the
/// platform's front edge (Magic.LayerPoint: the grid's rows compressed into a glass ramp stepping down toward the camera — under the grid it
/// could not be seen), under a shimmering WATER-GLASS pane (a cyan caustic net drifting on twos) with bubbles rising to it, threads rising from
/// its glass tiles to the tiles they mirror. When a layer cube's note begins (AudioCube.OnLanded) its glass tile flashes, star sparkles (sky) /
/// bubbles (deep) leave it, a small ring spreads and a bead of light runs along the thread: the note "phases in" up / down there. The copy
/// materialises tile by tile from the centre when its first cube arrives and dissolves when the last one leaves. Built, re-bound and stepped by
/// <see cref="WorldMagic"/> (keyed by island index and layer).
/// </summary>
public class EchoLayer : MonoBehaviour
{
    /// <summary>Seconds of the glass copy materialising / dissolving.</summary>
    public const float AppearSeconds = 0.5f;
    /// <summary>How far above the deep band its water-glass pane lies (above the cubes standing on it).</summary>
    public const float PaneLift = 0.8f;
    const int ThreadSegs = 6;

    public KeyBlock Island { get; private set; }
    public int Index { get; private set; }
    public int Layer { get; private set; }
    public bool Retiring { get; private set; }
    public bool Deep => Layer < 0;
    /// <summary>Glass tiles in the copy (= the grid's tiles).</summary>
    public int TileCount => tileCount;
    /// <summary>Threads of light (the layer cubes' tiles; + the four corners in the sky).</summary>
    public int ThreadCount => threadCount;
    /// <summary>Layer cubes that lived here at the last bind.</summary>
    public int CubeCount { get; private set; }
    /// <summary>0 = gone .. 1 = fully materialised.</summary>
    public float Appear => appear;
    /// <summary>Counters since Play (tests): phase-in sparkles (notes that began here), bubbles released, thread pulses.</summary>
    public int PhaseIns, BubblesOut, ThreadPulses;
    /// <summary>World position of the glass copy's centre (the mean of its glass tiles) this frame, and its y.</summary>
    public Vector3 PlaneCentre { get; private set; }
    public float PlaneY { get; private set; }
    /// <summary>The deep band this frame: the world z of its back (at the platform's front edge − DeepGap) and front edges (tests: it never reaches
    /// the next lane in front) and its depth.</summary>
    public float BandBackZ { get; private set; }
    public float BandFrontZ { get; private set; }
    public float BandDepth { get; private set; }

    Transform root, tilesT, paneT, rimT, threadsT, waterT, causticT;
    MeshRenderer tilesRend, threadsRend;
    Mesh tilesMesh, threadsMesh, rimMesh, waterMesh;
    Material tileMat, paneMat, rimMat, threadMat, waterMat, causticMat;
    // glass tiles (root-local: the island's local space; the sky root sits at the island + the layer offset)
    readonly List<TileInteraction> tileRefs = new List<TileInteraction>();
    Vector3[] tc; Vector2[] th; float[] tdist, tflash; int tileCount;
    Vector3[] tv; Color[] tcol; bool tilesDirty = true;
    Vector3 tMean;
    // threads
    readonly List<TileInteraction> threadTiles = new List<TileInteraction>();
    Vector3[] thA, thB; float[] thPulse, thTravel; int threadCount;
    Vector3[] hv; Color[] hcol; bool threadsDirty = true;
    float appear, twos, bubbleClock, shimmer, hxPane, hzPane, builtDepth = -1f;
    Color tint, threadCol;
    Vector3 paneLocal;
    int builtSig = int.MinValue;

    // ------------------------------------------------------------------ binding
    /// <summary>(Re-)binds the layer to <paramref name="kb"/> (island index <paramref name="index"/>), layer <paramref name="layer"/>, with
    /// <paramref name="cubes"/> layer cubes and their path tiles <paramref name="pathTiles"/> (distinct); rebuilds the copy when the grid changed.</summary>
    public void Bind(KeyBlock kb, int index, int layer, int cubes, List<TileInteraction> pathTiles)
    {
        Island = kb; Index = index; Layer = layer; CubeCount = cubes;
        if (kb == null) return;
        if (root == null) Build();
        tint = Deep ? Color.Lerp(new Color(0.5f, 0.86f, 1f), kb.chordColor, 0.2f) : Color.Lerp(kb.chordColor, new Color(0.9f, 0.96f, 1f), 0.62f);
        int sig = kb.GetInstanceID() * 31 + kb.tiles.Count * 7 + layer;
        float depth = Deep ? Magic.DeepDepth(kb) : 0f;
        if (sig != builtSig || Mathf.Abs(depth - builtDepth) > 0.01f) { builtSig = sig; builtDepth = depth; BuildTiles(kb); threadTiles.Clear(); thA = null; }
        // threads: the layer cubes' tiles (in a stable order) (+ the corners in the sky)
        bool same = pathTiles.Count == threadTiles.Count;
        if (same) for (int i = 0; i < pathTiles.Count; i++) if (pathTiles[i] != threadTiles[i]) { same = false; break; }
        if (!same || thA == null) { threadTiles.Clear(); threadTiles.AddRange(pathTiles); BuildThreads(kb); }
    }

    void Build()
    {
        root = new GameObject("Layer").transform;
        root.SetParent(transform, false);
        threadCol = Deep ? new Color(0.55f, 0.9f, 1f) : new Color(1f, 0.95f, 0.8f);

        tileMat = Fx.Alpha(MagicTextures.GlassTile, Color.white);
        tileMat.renderQueue = 3012;
        tilesT = Part("GlassTiles", tileMat, out tilesRend);
        tilesMesh = new Mesh { name = "glassTiles" }; tilesMesh.MarkDynamic();
        tilesT.GetComponent<MeshFilter>().sharedMesh = tilesMesh;

        threadMat = Fx.Additive(MagicTextures.Soft, Color.white, 0.9f);
        threadsT = Part("Threads", threadMat, out threadsRend);
        threadsMesh = new Mesh { name = "layerThreads" }; threadsMesh.MarkDynamic();
        threadsT.GetComponent<MeshFilter>().sharedMesh = threadsMesh;

        if (!Deep)
        {
            paneMat = Fx.Alpha(IconFactory.GetTexture("white"), new Color(1f, 1f, 1f, 0.05f));
            paneMat.renderQueue = 3010;
            MeshRenderer pr; paneT = Part("Pane", paneMat, out pr);
            rimMat = Fx.Alpha(IconFactory.GetTexture("white"), new Color(1f, 1f, 1f, 0.5f));
            rimMat.renderQueue = 3011;
            MeshRenderer rr; rimT = Part("Rim", rimMat, out rr);
        }
        else
        {
            waterMat = Fx.Alpha(MagicTextures.Pane, Palette.A(new Color(0.45f, 0.72f, 0.95f), 0.5f));
            waterMat.renderQueue = 3013;
            MeshRenderer wr; waterT = Part("WaterGlass", waterMat, out wr);
            causticMat = Fx.Alpha(MagicTextures.Caustics, Palette.A(new Color(0.8f, 0.97f, 1f), 0.55f));
            causticMat.renderQueue = 3014;
            MeshRenderer cr; causticT = Part("Caustics", causticMat, out cr);
            waterMesh = new Mesh { name = "deepWater" };
            waterT.GetComponent<MeshFilter>().sharedMesh = waterMesh;
            causticT.GetComponent<MeshFilter>().sharedMesh = waterMesh;
        }
    }

    Transform Part(string name, Material mat, out MeshRenderer mr)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>();
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go.transform;
    }

    /// <summary>The glass tiles: one quad per tile of the grid — at its top (the sky) or on the deep band (Magic.DeepLocal) — sized from its
    /// renderer (the deep band's rows squeezed into the band's depth).</summary>
    void BuildTiles(KeyBlock kb)
    {
        tileRefs.Clear();
        foreach (var t in kb.tiles) if (t != null) tileRefs.Add(t);
        tileCount = tileRefs.Count;
        tc = new Vector3[tileCount]; th = new Vector2[tileCount]; tdist = new float[tileCount]; tflash = new float[tileCount];
        Vector3 mid = kb.transform.InverseTransformPoint(kb.VisualCenter);
        int rows = Mathf.Max(1, kb.IsKeyboard || kb.IsStairs ? 1 : kb.rows);
        float depth = Magic.DeepDepth(kb);
        float maxD = 0.01f;
        tMean = Vector3.zero;
        for (int i = 0; i < tileCount; i++)
        {
            var t = tileRefs[i];
            Vector3 local = kb.transform.InverseTransformPoint(t.Top);
            var r = t.GetComponent<Renderer>();
            Vector3 size = r != null ? r.bounds.size : new Vector3(ProjectConfig.TileSize, 0.3f, ProjectConfig.TileSize);
            if (Deep)
            {
                tc[i] = Magic.DeepLocal(kb, local.x, t.gridZ, -Layer);
                th[i] = new Vector2(Mathf.Max(0.12f, size.x * 0.46f), Mathf.Max(0.05f, Mathf.Min(size.z, depth / rows) * 0.42f));
            }
            else
            {
                tc[i] = local;
                th[i] = new Vector2(Mathf.Max(0.12f, size.x * 0.47f), Mathf.Max(0.12f, size.z * 0.47f));
            }
            tMean += tc[i];
            tdist[i] = new Vector2(local.x - mid.x, local.z - mid.z).magnitude;
            maxD = Mathf.Max(maxD, tdist[i]);
        }
        if (tileCount > 0) tMean /= tileCount;
        for (int i = 0; i < tileCount; i++) tdist[i] /= maxD;
        tv = new Vector3[tileCount * 4]; tcol = new Color[tileCount * 4];
        var uv = new Vector2[tileCount * 4]; var tris = new int[tileCount * 6];
        for (int i = 0; i < tileCount; i++)
        {
            uv[i * 4] = new Vector2(0f, 0f); uv[i * 4 + 1] = new Vector2(1f, 0f); uv[i * 4 + 2] = new Vector2(1f, 1f); uv[i * 4 + 3] = new Vector2(0f, 1f);
            tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1; tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
        }
        tilesMesh.Clear();
        tilesMesh.vertices = tv; tilesMesh.uv = uv; tilesMesh.colors = tcol; tilesMesh.triangles = tris;
        tilesDirty = true;
        hxPane = kb.Width * 0.5f; hzPane = kb.Depth * 0.5f;
        if (!Deep)
        {
            // the sky pane: the platform's outline under the glass tiles, and its bright rim
            float yMin = float.MaxValue;
            for (int i = 0; i < tileCount; i++) yMin = Mathf.Min(yMin, tc[i].y);
            if (tileCount == 0) yMin = 0f;
            paneLocal = new Vector3(mid.x, yMin - 0.04f, mid.z);
            paneT.GetComponent<MeshFilter>().sharedMesh = MagicMeshes.RoundRectFill(hxPane, hzPane, 0.3f);
            paneT.localPosition = paneLocal;
            if (rimMesh != null) Destroy(rimMesh);
            rimMesh = MagicMeshes.TrackBand(hxPane, hzPane, 0.3f, 0.045f, 120);
            rimT.GetComponent<MeshFilter>().sharedMesh = rimMesh;
            rimT.localPosition = paneLocal + Vector3.up * 0.005f;
        }
        else
        {
            // the water-glass pane over the band (parallel to its slope, above the cubes standing on it)
            int k = -Layer;
            float x0 = -KeyBlock.EdgeInset + 0.12f, x1 = -KeyBlock.EdgeInset + kb.Width - 0.12f;
            Vector3 back = Magic.DeepLocal(kb, 0f, 0, k), front = Magic.DeepLocal(kb, 0f, rows - 1, k);
            float ub = 0.5f / rows, uf = (rows - 0.5f) / rows;
            // extend the band's slope to its two edges (u = 0 at the back, 1 at the front)
            float yb = back.y + (front.y - back.y) * (0f - ub) / Mathf.Max(1e-4f, uf - ub), yf = back.y + (front.y - back.y) * (1f - ub) / Mathf.Max(1e-4f, uf - ub);
            float zb = -KeyBlock.EdgeInset - Magic.DeepGap + 0.06f - (k - 1) * Magic.DeepStepZ, zf = zb - 0.12f - depth;
            if (rows == 1) { yb = back.y + 0.12f; yf = back.y - 0.12f; }
            waterMesh.Clear();
            waterMesh.vertices = new[] { new Vector3(x0, yf + PaneLift, zf), new Vector3(x1, yf + PaneLift, zf), new Vector3(x1, yb + PaneLift, zb), new Vector3(x0, yb + PaneLift, zb) };
            waterMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            waterMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            waterMesh.RecalculateBounds();
            causticMat.mainTextureScale = new Vector2(kb.Width / 2.6f, Mathf.Max(0.4f, depth / 2.6f));
            paneLocal = new Vector3((x0 + x1) * 0.5f, (yb + yf) * 0.5f, (zb + zf) * 0.5f);
        }
    }

    /// <summary>The threads: from each glass tile the layer's cubes use (and, in the sky, the four corners) to the real tile it mirrors (root-local).</summary>
    void BuildThreads(KeyBlock kb)
    {
        var glass = new List<Vector3>(); var real = new List<Vector3>();
        Vector3 skyOff = Deep ? Vector3.zero : Quaternion.Inverse(kb.transform.rotation) * Magic.LayerOffset(Layer);
        foreach (var t in threadTiles)
        {
            if (t == null) continue;
            int i = tileRefs.IndexOf(t);
            Vector3 local = kb.transform.InverseTransformPoint(t.Top);
            if (Deep) { glass.Add(i >= 0 ? tc[i] : Magic.DeepLocal(kb, local.x, t.gridZ, -Layer)); real.Add(local); }
            else { glass.Add(local); real.Add(local - skyOff); }
        }
        if (!Deep)
        {
            var c = new[] { new Vector3(-hxPane + 0.3f, 0f, -hzPane + 0.3f), new Vector3(hxPane - 0.3f, 0f, -hzPane + 0.3f), new Vector3(hxPane - 0.3f, 0f, hzPane - 0.3f), new Vector3(-hxPane + 0.3f, 0f, hzPane - 0.3f) };
            foreach (var p in c) { glass.Add(paneLocal + p); real.Add(paneLocal + p - skyOff); }
        }
        threadCount = glass.Count;
        thA = new Vector3[threadCount]; thB = new Vector3[threadCount]; thPulse = new float[threadCount]; thTravel = new float[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            thA[i] = glass[i] + Vector3.up * (Deep ? 0.04f : -0.02f);   // the glass end
            thB[i] = real[i] + Vector3.up * (Deep ? 0.02f : 0.03f);     // the real tile
            thTravel[i] = 2f;
        }
        int per = (ThreadSegs + 1) * 2 * 2;   // two crossed strips of ThreadSegs quads
        hv = new Vector3[threadCount * per]; hcol = new Color[threadCount * per];
        var uv = new Vector2[hv.Length]; var tris = new List<int>(threadCount * ThreadSegs * 12);
        for (int i = 0; i < threadCount; i++)
        {
            Vector3 dir = (thB[i] - thA[i]).normalized;
            Vector3 s1 = Vector3.Cross(dir, Vector3.right); if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.forward; s1.Normalize();
            Vector3 s2 = Vector3.Cross(dir, s1).normalized;
            float w = i < threadTiles.Count ? 0.045f : 0.03f;
            for (int strip = 0; strip < 2; strip++)
            {
                Vector3 side = strip == 0 ? s1 : s2;
                int b0 = i * per + strip * (ThreadSegs + 1) * 2;
                for (int k = 0; k <= ThreadSegs; k++)
                {
                    float u = k / (float)ThreadSegs;
                    Vector3 p = Vector3.Lerp(thA[i], thB[i], u);
                    hv[b0 + k * 2] = p - side * w; hv[b0 + k * 2 + 1] = p + side * w;
                    uv[b0 + k * 2] = new Vector2(u, 0f); uv[b0 + k * 2 + 1] = new Vector2(u, 1f);
                }
                for (int k = 0; k < ThreadSegs; k++)
                {
                    int a = b0 + k * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
        }
        threadsMesh.Clear();
        threadsMesh.vertices = hv; threadsMesh.uv = uv; threadsMesh.colors = hcol; threadsMesh.SetTriangles(tris, 0);
        threadsMesh.RecalculateBounds();
        threadsDirty = true;
    }

    /// <summary>The last layer cube left (or the grid is gone): the copy dissolves, then removes itself.</summary>
    public void Retire() { Retiring = true; }

    // ------------------------------------------------------------------ notes
    /// <summary>A layer cube's note began on <paramref name="tile"/> (its tile on the grid; its glass tile flashes): sparkles or bubbles, a small
    /// ring and a bead of light along the thread.</summary>
    public void OnNote(TileInteraction tile, Color c)
    {
        var wm = WorldMagic.I;
        int ti = tileRefs.IndexOf(tile);
        PhaseIns++;
        if (ti >= 0) { tflash[ti] = 1f; tilesDirty = true; }
        int hi = threadTiles.IndexOf(tile);
        if (hi >= 0 && thPulse != null && hi < thPulse.Length) { thPulse[hi] = 1f; thTravel[hi] = 0f; threadsDirty = true; ThreadPulses++; }
        if (wm == null || root == null || ti < 0) return;
        Vector3 p = root.TransformPoint(tc[ti]) + Vector3.up * 0.04f;
        Color pale = Color.Lerp(c, Color.white, 0.6f);
        if (!Deep)
        {
            for (int i = 0; i < 7; i++)
            {
                float a = (i / 7f + Random.value * 0.1f) * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                wm.Sparkle(p + d * 0.3f, d * Random.Range(0.6f, 1.2f) + Vector3.up * Random.Range(0.8f, 1.6f), i % 2 == 0 ? pale : Color.white, Random.Range(0.14f, 0.24f), Random.Range(0.35f, 0.55f));
            }
        }
        else
        {
            for (int i = 0; i < 4; i++) { wm.Bubble(p + new Vector3(Random.Range(-0.3f, 0.3f), 0.1f, Random.Range(-0.12f, 0.12f)), Vector3.up * Random.Range(0.55f, 0.9f), Random.Range(0.12f, 0.2f), Random.Range(0.7f, 0.95f)); BubblesOut++; }
            for (int i = 0; i < 3; i++) wm.Sparkle(p + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(0.3f, 0.7f), i == 0 ? Color.white : pale, 0.15f, 0.4f);
        }
        Fx.Ripple(p, Color.Lerp(c, tint, 0.4f), Deep ? 0.5f : 0.7f, 0.32f);
    }

    // ------------------------------------------------------------------ frame
    public void Step(float dt, bool world)
    {
        if (Island == null && !Retiring) Retire();
        twos += dt;
        bool drawTwos = twos >= 1f / Look.TwosFps || !Look.OnTwos;
        if (drawTwos) twos = 0f;
        float prevAppear = appear;
        appear = Mathf.MoveTowards(appear, Retiring ? 0f : 1f, dt / AppearSeconds);
        if (Retiring && appear <= 0f) { Destroy(gameObject); return; }
        bool on = world && Island != null && root != null;
        if (root != null && root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
        if (!on) return;
        // the copy rides with the grid (the tower lift, the ground, the belt, a flex — all of it): the sky over it, the deep band in front of it
        root.position = Island.transform.position + (Deep ? Vector3.zero : Magic.LayerOffset(Layer));
        root.rotation = Island.transform.rotation;
        PlaneCentre = root.TransformPoint(tMean);
        PlaneY = PlaneCentre.y;
        if (Deep)
        {
            BandDepth = Magic.DeepDepth(Island);
            BandBackZ = root.TransformPoint(new Vector3(0f, 0f, -KeyBlock.EdgeInset - Magic.DeepGap)).z;
            BandFrontZ = BandBackZ - BandDepth - (-Layer - 1) * Magic.DeepStepZ;
        }
        if (Mathf.Abs(appear - prevAppear) > 1e-5f) { tilesDirty = true; threadsDirty = true; }
        bool flashing = false;
        for (int i = 0; i < tileCount; i++) if (tflash[i] > 0f) { tflash[i] = Mathf.Max(0f, tflash[i] - dt / 0.4f); flashing = true; }
        if (flashing) tilesDirty = true;
        if (thPulse != null)
            for (int i = 0; i < threadCount; i++)
            {
                if (thPulse[i] > 0f) { thPulse[i] = Mathf.Max(0f, thPulse[i] - dt / 0.5f); threadsDirty = true; }
                if (thTravel[i] < 1.5f) { thTravel[i] += dt / 0.22f; threadsDirty = true; }
            }
        // a slow shimmer runs across the glass (on twos)
        if (drawTwos) { shimmer = Mathf.Repeat(Look.Stepped(Time.time) * 0.35f + Index * 0.37f, 1.6f); tilesDirty = true; }
        if (tilesDirty) WriteTiles();
        if (threadsDirty) WriteThreads();
        float a = Ease.OutCubic(appear);
        if (!Deep)
        {
            paneT.localScale = new Vector3(a, 1f, a);
            rimT.localScale = new Vector3(a, 1f, a);
            paneMat.SetColor("_Color", Palette.A(tint, 0.05f * a));
            rimMat.SetColor("_Color", Palette.A(Color.Lerp(tint, Color.white, 0.4f), 0.34f * a));
        }
        else
        {
            if (drawTwos)
            {
                float t = Look.Stepped(Time.time);
                causticMat.mainTextureOffset = new Vector2(t * 0.045f, t * 0.03f);
                causticMat.SetColor("_Color", Palette.A(new Color(0.82f, 0.97f, 1f), (0.42f + 0.12f * Mathf.Sin(t * 1.7f)) * a));
                waterMat.SetColor("_Color", Palette.A(Color.Lerp(new Color(0.45f, 0.72f, 0.95f), tint, 0.3f), 0.45f * a));
            }
            // bubbles rise from the band to the water-glass all the time (more while its cubes play: OnNote)
            bubbleClock -= dt;
            if (bubbleClock <= 0f && tileCount > 0 && appear > 0.9f)
            {
                bubbleClock = Random.Range(0.22f, 0.5f);
                var wm = WorldMagic.I;
                int i = Random.Range(0, tileCount);
                if (wm != null) { wm.Bubble(root.TransformPoint(tc[i]) + new Vector3(Random.Range(-0.35f, 0.35f), 0.05f, Random.Range(-0.1f, 0.1f)), Vector3.up * Random.Range(0.4f, 0.6f), Random.Range(0.09f, 0.16f), Random.Range(1.1f, 1.5f)); BubblesOut++; }
            }
        }
    }

    void WriteTiles()
    {
        tilesDirty = false;
        if (tv == null) return;
        float a01 = appear;
        for (int i = 0; i < tileCount; i++)
        {
            // materialise from the centre out
            float k = Mathf.Clamp01((a01 * 1.5f - tdist[i] * 0.5f) * 2f);
            float s = Retiring ? Ease.InOutCubic(k) : Ease.OutBack(k, 2.2f);
            Vector3 c = tc[i]; Vector2 h = th[i] * s;
            tv[i * 4] = c + new Vector3(-h.x, 0f, -h.y); tv[i * 4 + 1] = c + new Vector3(h.x, 0f, -h.y);
            tv[i * 4 + 2] = c + new Vector3(h.x, 0f, h.y); tv[i * 4 + 3] = c + new Vector3(-h.x, 0f, h.y);
            float sh = Mathf.Clamp01(1f - Mathf.Abs(shimmer - tdist[i] - 0.2f) / 0.25f) * 0.25f;
            float f = tflash[i];
            Color col = Color.Lerp(tint, Color.white, Mathf.Clamp01(sh * 1.5f + f));
            col.a = Mathf.Clamp01((Deep ? 0.85f : 0.42f) + sh + f) * k;
            tcol[i * 4] = tcol[i * 4 + 1] = tcol[i * 4 + 2] = tcol[i * 4 + 3] = col;
        }
        tilesMesh.vertices = tv; tilesMesh.colors = tcol;
        tilesMesh.RecalculateBounds();
    }

    void WriteThreads()
    {
        threadsDirty = false;
        if (hv == null) return;
        int per = (ThreadSegs + 1) * 2 * 2;
        float a = Ease.OutCubic(appear);
        for (int i = 0; i < threadCount; i++)
        {
            bool corner = i >= threadTiles.Count;
            float baseA = (corner ? 0.2f : 0.38f) * a, pulse = thPulse[i];
            for (int strip = 0; strip < 2; strip++)
            {
                int b0 = i * per + strip * (ThreadSegs + 1) * 2;
                for (int k = 0; k <= ThreadSegs; k++)
                {
                    float u = k / (float)ThreadSegs;               // 0 at the glass, 1 at the real tile
                    float fade = Mathf.Lerp(1f, 0.25f, u);
                    // the bead travels from the real tile (u = 1) to the glass (u = 0): the note is sent up / down
                    float bead = thTravel[i] <= 1.2f ? Mathf.Clamp01(1f - Mathf.Abs((1f - thTravel[i]) - u) / 0.3f) : 0f;
                    float v = baseA * fade + pulse * 0.35f + bead * 1.1f;
                    Color c = Color.Lerp(threadCol, Color.white, Mathf.Clamp01(bead + pulse * 0.5f)) * v;
                    c.a = 1f;
                    hcol[b0 + k * 2] = c; hcol[b0 + k * 2 + 1] = c;
                }
            }
        }
        threadsMesh.colors = hcol;
    }

    void OnDestroy()
    {
        if (tileMat != null) Destroy(tileMat);
        if (paneMat != null) Destroy(paneMat);
        if (rimMat != null) Destroy(rimMat);
        if (threadMat != null) Destroy(threadMat);
        if (waterMat != null) Destroy(waterMat);
        if (causticMat != null) Destroy(causticMat);
        if (tilesMesh != null) Destroy(tilesMesh);
        if (threadsMesh != null) Destroy(threadsMesh);
        if (rimMesh != null) Destroy(rimMesh);
        if (waterMesh != null) Destroy(waterMesh);
    }
}

/// <summary>
/// v7 package W (SPEC v7 §6.3): the fine LINK THREAD between every echo / octave copy / harmony cube and its source (AudioCube.echoOf): a thin
/// pale thread sagging a little between the two cubes (a curve lifted over them), mixing their colours. It PULSES when both sound — when one
/// lands and the other landed within ≈ half a second (an echo "late" answers its source an eighth after it) the thread brightens and a bead of
/// light runs along it from the one that played first to the one that answered. One LineRenderer per link, pooled.
/// </summary>
public class EchoLinks : MonoBehaviour
{
    const int Points = 18;
    /// <summary>Seconds within which two landings count as "both sound".</summary>
    public const float BothWindow = 0.6f;

    class Link { public AudioCube src, echo; public LineRenderer lr; public Transform bead; public MeshRenderer beadRend; public float pulse, travel = 9f; public bool fromSrc = true; public bool used; }
    readonly List<Link> links = new List<Link>();
    readonly Dictionary<int, AudioCube> byId = new Dictionary<int, AudioCube>();
    readonly Dictionary<AudioCube, float> lastLand = new Dictionary<AudioCube, float>();
    readonly Vector3[] pts = new Vector3[Points];
    Material lineMat, beadMat; MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>Links drawn now.</summary>
    public int Count { get { int n = 0; foreach (var l in links) if (l.used && l.lr != null && l.lr.enabled) n++; return n; } }
    /// <summary>Pulses since Play (tests).</summary>
    public int Pulses;
    /// <summary>True when <paramref name="echo"/>'s link to its source is drawn.</summary>
    public bool Linked(AudioCube echo) { foreach (var l in links) if (l.used && l.echo == echo && l.lr != null && l.lr.enabled) return true; return false; }

    /// <summary>A cube's note landed: remember it, and pulse its link(s) when the other end sounded within <see cref="BothWindow"/>.</summary>
    public void OnLanded(AudioCube c)
    {
        if (c == null) return;
        float now = Time.time;
        lastLand[c] = now;
        foreach (var l in links)
        {
            if (!l.used) continue;
            AudioCube other = l.echo == c ? l.src : (l.src == c ? l.echo : null);
            if (other == null) continue;
            float t;
            if (!lastLand.TryGetValue(other, out t) || now - t > BothWindow) continue;
            l.pulse = 1f; l.travel = 0f; l.fromSrc = other == l.src; Pulses++;
        }
    }

    public void Step(float dt, bool world)
    {
        if (lineMat == null)
        {
            lineMat = Fx.Alpha(MagicTextures.Soft, Color.white);
            beadMat = Fx.Alpha(MagicTextures.Star, Color.white);
            mpb = new MaterialPropertyBlock();
        }
        byId.Clear();
        var cubes = SequenceMaster.Cubes;
        for (int i = 0; i < cubes.Count; i++) { var c = cubes[i]; if (c != null && c.id != 0 && !byId.ContainsKey(c.id)) byId[c.id] = c; }
        foreach (var l in links) l.used = false;
        if (world)
            for (int i = 0; i < cubes.Count; i++)
            {
                var e = cubes[i];
                if (e == null || e.echoOf < 0 || e.echoOf == e.id) continue;
                AudioCube s;
                if (!byId.TryGetValue(e.echoOf, out s) || s == null) continue;
                if (!e.BodyVisible || !s.BodyVisible || !e.gameObject.activeInHierarchy || !s.gameObject.activeInHierarchy) continue;
                Link l = null;
                foreach (var o in links) if (!o.used && o.echo == e && o.src == s) { l = o; break; }
                if (l == null) foreach (var o in links) if (!o.used && o.echo == null) { l = o; break; }
                if (l == null) foreach (var o in links) if (!o.used) { l = o; break; }
                if (l == null) { l = new Link(); links.Add(l); }
                if (l.echo != e || l.src != s) { l.echo = e; l.src = s; l.pulse = 0f; l.travel = 9f; }
                l.used = true;
                Pose(l, dt);
            }
        foreach (var l in links)
            if (!l.used)
            {
                if (l.lr != null && l.lr.enabled) l.lr.enabled = false;
                if (l.bead != null && l.bead.gameObject.activeSelf) l.bead.gameObject.SetActive(false);
                l.echo = null; l.src = null;
            }
    }

    void Pose(Link l, float dt)
    {
        if (l.lr == null)
        {
            var go = new GameObject("EchoLink");
            go.transform.SetParent(transform, false);
            l.lr = go.AddComponent<LineRenderer>();
            l.lr.useWorldSpace = true; l.lr.positionCount = Points; l.lr.alignment = LineAlignment.View; l.lr.textureMode = LineTextureMode.Stretch;
            l.lr.numCapVertices = 2; l.lr.sharedMaterial = lineMat;
            l.lr.shadowCastingMode = ShadowCastingMode.Off; l.lr.receiveShadows = false; l.lr.lightProbeUsage = LightProbeUsage.Off;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.2f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0.2f, 1f) });
            l.lr.colorGradient = g;
            var b = new GameObject("LinkBead");
            b.transform.SetParent(transform, false);
            b.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
            l.beadRend = b.AddComponent<MeshRenderer>();
            l.beadRend.sharedMaterial = beadMat;
            l.beadRend.shadowCastingMode = ShadowCastingMode.Off; l.beadRend.receiveShadows = false; l.beadRend.lightProbeUsage = LightProbeUsage.Off;
            l.bead = b.transform;
            b.SetActive(false);
        }
        if (!l.lr.enabled) l.lr.enabled = true;
        l.pulse = Mathf.Max(0f, l.pulse - dt / 0.5f);
        Vector3 a = l.src.transform.position + Vector3.up * 0.2f, b2 = l.echo.transform.position + Vector3.up * 0.2f;
        float dist = Vector3.Distance(a, b2);
        Vector3 ctl = (a + b2) * 0.5f + Vector3.up * (0.3f + 0.12f * dist);
        for (int i = 0; i < Points; i++)
        {
            float t = i / (Points - 1f);
            pts[i] = (1f - t) * (1f - t) * a + 2f * (1f - t) * t * ctl + t * t * b2;
        }
        l.lr.SetPositions(pts);
        Color mix = Color.Lerp(Color.Lerp(l.src.Color, l.echo.Color, 0.5f), Color.white, 0.55f);
        l.lr.widthMultiplier = 0.06f + 0.05f * l.pulse;
        mpb.Clear(); mpb.SetColor(ColorId, Palette.A(Color.Lerp(mix, Color.white, l.pulse * 0.6f), 0.55f + 0.45f * l.pulse));
        l.lr.SetPropertyBlock(mpb);
        // the bead of a pulse runs from the one that played first to the one that answered (≈ 0.25 s)
        if (l.travel < 1f)
        {
            l.travel += dt / 0.25f;
            float t = Mathf.Clamp01(l.travel); if (!l.fromSrc) t = 1f - t;
            Vector3 p = (1f - t) * (1f - t) * a + 2f * (1f - t) * t * ctl + t * t * b2;
            if (!l.bead.gameObject.activeSelf) l.bead.gameObject.SetActive(true);
            l.bead.position = p;
            var cam = Camera.main;
            if (cam != null) l.bead.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, Look.Stepped(Time.time) * 160f);
            float s = 0.34f * Mathf.Sin(Mathf.Clamp01(l.travel) * Mathf.PI) + 0.12f;
            l.bead.localScale = new Vector3(s, s, 1f);
            mpb.Clear(); mpb.SetColor(ColorId, Palette.A(Color.Lerp(mix, Color.white, 0.4f), 1f)); l.beadRend.SetPropertyBlock(mpb);
        }
        else if (l.bead.gameObject.activeSelf) l.bead.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (lineMat != null) Destroy(lineMat);
        if (beadMat != null) Destroy(beadMat);
    }
}
