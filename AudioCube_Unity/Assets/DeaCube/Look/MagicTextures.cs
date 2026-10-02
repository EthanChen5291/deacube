using UnityEngine;

/// <summary>
/// v6 package W (SPEC v6 §6): the procedural textures of the world magic, built once on first use (no assets), in the v3 print look — flat
/// fills, dark violet ink rims, alpha = the shape: the rune atlas of the carry sigils (bright strokes, no rim: they sit on a dark ink band),
/// the sigil band itself (two bright border lines round a dark ink fill), the sparkle sheet (four-point sparkle, five-point star, diamond,
/// dot), the ghost star, the pearl strip of the dotted carry arcs, the bead of a paste flight, the water droplet, the whirl spiral of a
/// lowered island and the foam ramp of the tower's waves.
/// </summary>
public static partial class MagicTextures
{
    static Texture2D runes, band, sparkles, star, pearls, bead, droplet, spiral, foam, soft;

    /// <summary>256 x 192 atlas of 4 x 3 cells (white strokes, alpha = the stroke; tinted by the vertex colour): cells 0..7 and 9..11 are runes,
    /// cell 8 (<see cref="PipTile"/>) a filled pip.</summary>
    public static Texture2D Runes { get { if (runes == null) runes = Atlas(256, 192, RuneCols, RuneRows, RuneShape, 0.06f, "deacube_magic_runes"); return runes; } }
    public const int RuneCols = 4, RuneRows = 3, PipTile = 8;
    /// <summary>Rune cells for a sigil to pick from (the pip excluded: 0..7).</summary>
    public const int RuneTiles = 8;
    /// <summary>The sigil band across v: a bright line at each edge (white), a dark ink fill between them (InkColor, alpha 0.7).</summary>
    public static Texture2D Band { get { if (band == null) band = BuildBand(); return band; } }
    /// <summary>128 x 128 sheet of 2 x 2 sparkles (four-point sparkle, five-point star, diamond, dot): white fill inside an ink rim.</summary>
    public static Texture2D Sparkles { get { if (sparkles == null) sparkles = Atlas(128, 128, 2, 2, SparkleShape, 0.2f, "deacube_magic_sparkles"); return sparkles; } }
    /// <summary>One rounded five-point star (white fill, ink rim): the ghost stars on carried tiles and the pulse head.</summary>
    public static Texture2D Star { get { if (star == null) star = Atlas(128, 128, 1, 1, (k, p) => RoundStar(p, 0.92f, 0.44f, 0.06f), 0.13f, "deacube_magic_star"); return star; } }
    /// <summary>A 2:1 tile with one pearl (white, ink rim, a highlight) for the dotted carry arc (LineTextureMode.Tile).</summary>
    public static Texture2D Pearls { get { if (pearls == null) pearls = BuildPearls(); return pearls; } }
    /// <summary>A bead (white disc, ink rim, a highlight at the upper left): a paste flight's ghost notes.</summary>
    public static Texture2D Bead { get { if (bead == null) bead = BuildBall(64, 0.78f, 0.17f, "deacube_magic_bead"); return bead; } }
    /// <summary>A water droplet (a pale disc with an ink rim and a highlight; the particle renderer stretches it along its velocity).</summary>
    public static Texture2D Droplet { get { if (droplet == null) droplet = BuildBall(64, 0.8f, 0.22f, "deacube_magic_drop"); return droplet; } }
    /// <summary>A three-arm whirl (white lines, alpha fading at the rim and the eye): the sea under a lowered island.</summary>
    public static Texture2D Spiral { get { if (spiral == null) spiral = BuildSpiral(256); return spiral; } }
    /// <summary>The wave crest's colour across its profile (u 0 inner foot .. 1 outer foot): swell violet → pale wave line → cream foam crest.</summary>
    public static Texture2D Foam { get { if (foam == null) foam = BuildFoam(); return foam; } }
    /// <summary>A soft band across v (alpha = sin(v π)^1.5): the additive glow under the sigils and the arcs.</summary>
    public static Texture2D Soft { get { if (soft == null) soft = BuildSoft(); return soft; } }

    /// <summary>The cream of the foam and the pale glyphs (the UI's paper, a touch cooler).</summary>
    public static readonly Color FoamCream = new Color(1f, 0.96f, 0.9f);

    // ------------------------------------------------------------------ builders
    delegate float Shape(int k, Vector2 p);

    /// <summary>A sheet of <paramref name="nx"/> x <paramref name="ny"/> tiles; tile k (row-major from the bottom-left, the particle sheet order)
    /// holds <paramref name="shape"/>(k, p) with p in [-1, 1]². <paramref name="rim"/> > 0: an ink rim that wide inside the edge; 0: plain white.</summary>
    static Texture2D Atlas(int w, int h, int nx, int ny, Shape shape, float rim, string name)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        int tw = w / nx, th = h / ny;
        Color ink = Look.InkColor;
        float aa = 2.2f / Mathf.Min(tw, th) * 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int tx = x / tw, ty = y / th;
                var p = new Vector2(((x % tw) + 0.5f) / tw * 2f - 1f, ((y % th) + 0.5f) / th * 2f - 1f);
                float d = shape(tx + ty * nx, p);
                float a = Mathf.Clamp01(0.5f - d / aa);
                Color c = Color.white;
                if (rim > 0f) c = Color.Lerp(Color.white, ink, Mathf.Clamp01(0.5f + (d + rim) / aa));
                px[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }

    // eight runes drawn with pen strokes (half-width 0.11 in the tile's [-1, 1] square): not letters of any script, just marks that read as runes
    static float RuneShape(int k, Vector2 p)
    {
        const float w = 0.16f;
        switch (k)
        {
            case 0: return IconFactory.P.U(IconFactory.P.Seg(p, -0.18f, -0.72f, -0.18f, 0.72f, w), IconFactory.P.Seg(p, -0.18f, 0.66f, 0.46f, 0.3f, w), IconFactory.P.Seg(p, -0.18f, 0.18f, 0.46f, -0.18f, w));
            case 1: return IconFactory.P.U(IconFactory.P.Seg(p, 0f, 0.74f, 0.6f, 0f, w), IconFactory.P.Seg(p, 0.6f, 0f, 0f, -0.74f, w), IconFactory.P.Seg(p, 0f, -0.74f, -0.6f, 0f, w), IconFactory.P.Seg(p, -0.6f, 0f, 0f, 0.74f, w), IconFactory.P.Circle(p, 0f, 0f, 0.16f));
            case 2: return IconFactory.P.U(IconFactory.P.Seg(p, 0f, -0.74f, 0f, 0.7f, w), IconFactory.P.Seg(p, -0.5f, 0.2f, 0f, 0.72f, w), IconFactory.P.Seg(p, 0.5f, 0.2f, 0f, 0.72f, w));
            case 3: return IconFactory.P.U(IconFactory.P.Arc(p, 0.08f, 0f, 0.6f, w, 70f, 290f), IconFactory.P.Circle(p, 0.3f, 0f, 0.15f));
            case 4: return IconFactory.P.U(IconFactory.P.Seg(p, -0.62f, -0.5f, 0.62f, -0.5f, w), IconFactory.P.Seg(p, 0.62f, -0.5f, 0f, 0.66f, w), IconFactory.P.Seg(p, 0f, 0.66f, -0.62f, -0.5f, w), IconFactory.P.Seg(p, 0f, -0.5f, 0f, -0.05f, w));
            case 5: return IconFactory.P.U(IconFactory.P.Seg(p, -0.52f, -0.66f, 0.52f, 0.66f, w), IconFactory.P.Seg(p, -0.52f, 0.66f, 0.52f, -0.66f, w), IconFactory.P.Seg(p, -0.62f, 0f, 0.62f, 0f, w));
            case 6: return IconFactory.P.U(IconFactory.P.Ring(p, 0f, 0f, 0.46f, w), IconFactory.P.Seg(p, 0f, -0.76f, 0f, 0.76f, w));
            case 8: return IconFactory.P.Circle(p, 0f, 0f, 0.62f);
            case 9: return IconFactory.P.U(IconFactory.P.Seg(p, -0.5f, -0.7f, -0.5f, 0.7f, w), IconFactory.P.Seg(p, 0.5f, -0.7f, 0.5f, 0.7f, w), IconFactory.P.Seg(p, -0.5f, 0.3f, 0.5f, -0.3f, w));
            case 10: return IconFactory.P.U(IconFactory.P.Arc(p, 0f, -0.2f, 0.5f, w, 0f, 180f), IconFactory.P.Seg(p, 0f, -0.2f, 0f, 0.74f, w), IconFactory.P.Circle(p, 0f, -0.62f, 0.14f));
            case 11: return IconFactory.P.U(IconFactory.P.Seg(p, -0.62f, 0.5f, 0.62f, 0.5f, w), IconFactory.P.Seg(p, -0.62f, -0.5f, 0.62f, -0.5f, w), IconFactory.P.Circle(p, 0f, 0f, 0.17f));
            default: return IconFactory.P.U(IconFactory.P.Seg(p, -0.5f, 0.66f, 0.3f, 0.3f, w), IconFactory.P.Seg(p, 0.3f, 0.3f, -0.3f, -0.3f, w), IconFactory.P.Seg(p, -0.3f, -0.3f, 0.5f, -0.66f, w));
        }
    }

    static float SparkleShape(int k, Vector2 p)
    {
        switch (k)
        {
            case 0: return RoundStar(p, 0.96f, 0.26f, 0.04f);    // the four-point twinkle
            case 1: return RoundStar5(p);
            case 2: return IconFactory.P.Star(p, 4, 0.82f, 0.5f, 90f);
            default: return IconFactory.P.Circle(p, 0f, 0f, 0.6f);
        }
    }
    static float RoundStar5(Vector2 p) => RoundStar(p, 0.92f, 0.44f, 0.06f);

    /// <summary>A star with <paramref name="outer"/> / <paramref name="inner"/> radii, softened by <paramref name="round"/> (four points when inner is
    /// small, five otherwise).</summary>
    static float RoundStar(Vector2 p, float outer, float inner, float round)
    {
        int points = inner < 0.35f ? 4 : 5;
        return IconFactory.P.Star(p, points, outer - round, inner - round * 0.5f, 90f) - round;
    }

    static Texture2D BuildBand()
    {
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "deacube_magic_band", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        Color ink = Look.InkColor;
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h, e = Mathf.Min(v, 1f - v);   // 0 at the edges, 0.5 in the middle
            // one bright line along the OUTER edge (v near 1) and a faint one inside: a ribbon, not a pair of rails
            float line = Mathf.Clamp01((0.055f - Mathf.Abs(v - 0.9f)) * 50f) + 0.35f * Mathf.Clamp01((0.03f - Mathf.Abs(v - 0.1f)) * 50f);
            float fill = Mathf.Clamp01((e - 0.06f) * 30f);                    // the ink ribbon under the runes
            float a = Mathf.Max(Mathf.Clamp01(line), fill * 0.5f);
            Color c = line > 0.01f ? Color.Lerp(ink, Color.white, line) : ink;
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    static Texture2D BuildPearls()
    {
        const int w = 64, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "deacube_magic_pearls", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        Color ink = Look.InkColor;
        float aa = 2.4f / h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // tile space: x in [-2, 2], y in [-1, 1] (a pearl of radius 0.62 in the middle of a 2:1 tile)
                var p = new Vector2((x + 0.5f) / w * 4f - 2f, (y + 0.5f) / h * 2f - 1f);
                float d = p.magnitude - 0.62f;
                float a = Mathf.Clamp01(0.5f - d / aa);
                float inkAmt = Mathf.Clamp01(0.5f + (d + 0.2f) / aa);
                float hi = Mathf.Clamp01((0.2f - (p - new Vector2(-0.2f, 0.22f)).magnitude) / aa * 0.5f);
                Color c = Color.Lerp(Color.Lerp(Color.white, ink, inkAmt), Color.white, hi * (1f - inkAmt));
                px[y * w + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildBall(int n, float radius, float rim, string name)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.4f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = p.magnitude - radius;
                float a = Mathf.Clamp01(0.5f - d / aa);
                float inkAmt = Mathf.Clamp01(0.5f + (d + rim) / aa);
                float hi = Mathf.Clamp01(0.5f - ((p - new Vector2(-0.26f, 0.3f)).magnitude - 0.2f) / aa);
                Color c = Color.Lerp(Color.white, ink, inkAmt);
                c = Color.Lerp(c, new Color(1f, 1f, 1f), hi * (1f - inkAmt));
                // a slightly darker lower half (a painted shade band) so the ball reads as a ball
                if (inkAmt < 0.5f && p.y < -0.18f && hi < 0.5f) c *= 0.86f;
                px[y * n + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildSpiral(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "deacube_magic_spiral", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float r = p.magnitude;
                float th = Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f);
                // three logarithmic arms winding inward
                float phase = th * 3f + Mathf.Log(Mathf.Max(r, 1e-3f)) * 1.6f;
                float f = Mathf.Abs(Mathf.Repeat(phase, 1f) - 0.5f);
                float width = Mathf.Lerp(0.03f, 0.12f, Mathf.Clamp01(r));
                float line = Mathf.Clamp01((width - Mathf.Abs(f - 0.25f)) * 60f * Mathf.Max(r, 0.2f));
                float fade = Mathf.Clamp01((1f - r) / 0.18f) * Mathf.Clamp01((r - 0.06f) / 0.2f);
                float a = line * fade;
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildFoam()
    {
        const int w = 32;
        var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false) { name = "deacube_magic_foam", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * 2];
        Color swell = Color.Lerp(Look.SeaSwell, Look.SeaLine, 0.55f), line = Color.Lerp(Look.SeaLine, Color.white, 0.35f);
        for (int x = 0; x < w; x++)
        {
            float u = (x + 0.5f) / w;
            Color c = u < 0.35f ? Color.Lerp(swell, line, u / 0.35f) : (u < 0.5f ? Color.Lerp(line, FoamCream, (u - 0.35f) / 0.15f) : FoamCream);
            var c32 = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
            px[x] = c32; px[w + x] = c32;
        }
        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    static Texture2D BuildSoft()
    {
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "deacube_magic_soft", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            byte a = (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.5f) * 255f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }
}
