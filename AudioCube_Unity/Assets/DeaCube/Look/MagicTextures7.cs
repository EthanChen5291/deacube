using UnityEngine;

/// <summary>
/// v7 package W (SPEC v7 §6): the procedural textures of the v7 world magic, built once on first use (no assets), in the same print look as
/// v6 (flat fills, dark violet ink rims, alpha = the shape): the glass tile of the echo layers (a faint fill, a bright rim, a highlight streak),
/// the rising bubble of the deep layer, the water-glass caustics, the dust puff of a runner's landing, the rewind clock (a face with ticks and
/// a spiral arrow winding back) and its hands, the sepia / film halftone of the unwind's flash, the translucent tile strip of the flow ribbon,
/// the mirror's silver sheen and the rounded frame of a glass pane.
/// </summary>
public static partial class MagicTextures
{
    static Texture2D glassTile, bubble, caustics, puff, clockFace, clockHands, sepia, carpet, mirror, pane;

    /// <summary>A glass tile (64 px): a faint white fill (alpha 0.2), a bright rim, a highlight streak across the upper-left corner and an ink
    /// hairline just outside the rim (it reads on pale and dark grounds alike). Tinted by the material / vertex colour.</summary>
    public static Texture2D GlassTile { get { if (glassTile == null) glassTile = BuildGlassTile(64); return glassTile; } }
    /// <summary>A bubble (64 px): an ink ring round a pale fill (alpha 0.25) with a highlight at the upper left.</summary>
    public static Texture2D Bubble { get { if (bubble == null) bubble = BuildBubble(64); return bubble; } }
    /// <summary>Tileable water caustics (128 px): the bright net of a cell pattern's edges (alpha = the line), for the deep layer's water-glass.</summary>
    public static Texture2D Caustics { get { if (caustics == null) caustics = BuildCaustics(128); return caustics; } }
    /// <summary>A dust puff (64 px): a three-lobed cloud, cream-white fill with a shaded belly and an ink rim.</summary>
    public static Texture2D Puff { get { if (puff == null) puff = BuildPuff(64); return puff; } }
    /// <summary>The rewind clock's face (256 px): a white disc in an ink rim, twelve ink ticks and an ink spiral arrow winding counter-clockwise
    /// (time going back).</summary>
    public static Texture2D ClockFace { get { if (clockFace == null) clockFace = BuildClockFace(256); return clockFace; } }
    /// <summary>The rewind clock's hands (256 px, pivot at the centre): a long and a short ink hand with pale cores and a centre pin.</summary>
    public static Texture2D ClockHands { get { if (clockHands == null) clockHands = BuildClockHands(256); return clockHands; } }
    /// <summary>Tileable sepia film (128 px): Ben-Day halftone dots, a few vertical scratches and a soft grain (alpha = the print).</summary>
    public static Texture2D Sepia { get { if (sepia == null) sepia = BuildSepia(128); return sepia; } }
    /// <summary>Tileable flow-ribbon cell (64 px): a translucent rounded tile (fill alpha 0.5) with a bright rim in a faint gap.</summary>
    public static Texture2D Carpet { get { if (carpet == null) carpet = BuildCarpet(64); return carpet; } }
    /// <summary>The flip mirror's face (128 px): a pale silver sheen with diagonal streaks and a bright edge on both sides.</summary>
    public static Texture2D Mirror { get { if (mirror == null) mirror = BuildMirror(128); return mirror; } }
    /// <summary>A glass pane's frame (128 px, 9-slice friendly): a faint fill, a bright rounded rim and small corner ticks.</summary>
    public static Texture2D Pane { get { if (pane == null) pane = BuildPane(128); return pane; } }

    // ------------------------------------------------------------------ helpers
    static Texture2D NewTex(int w, int h, bool mips, TextureWrapMode wrap, string name)
        => new Texture2D(w, h, TextureFormat.RGBA32, mips) { name = name, wrapMode = wrap, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear, hideFlags = HideFlags.DontSave };

    static Color32 C32(Color c, float a) => new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

    /// <summary>Signed distance to a rounded square of half size <paramref name="b"/> and corner radius <paramref name="r"/> (p in [-1, 1]²).</summary>
    static float RoundSquare(Vector2 p, float b, float r)
    {
        float qx = Mathf.Abs(p.x) - (b - r), qy = Mathf.Abs(p.y) - (b - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float Cover(float d, float aa) => Mathf.Clamp01(0.5f - d / aa);

    static Texture2D BuildGlassTile(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_glasstile");
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.2f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = RoundSquare(p, 0.86f, 0.26f);
                float inside = Cover(d, aa);
                float rim = Cover(Mathf.Abs(d + 0.07f) - 0.055f, aa);                       // the bright rim just inside the edge
                float hair = Cover(Mathf.Abs(d - 0.035f) - 0.022f, aa) * (1f - inside);      // the ink hairline just outside
                // a highlight streak across the upper-left corner (glass catching the light)
                float s = (p.x - p.y) * 0.7071f;                                             // across the streak
                float along = (p.x + p.y) * 0.7071f;
                float streak = Cover(Mathf.Abs(s + 0.5f) - 0.07f, aa) * Mathf.Clamp01(1f - Mathf.Abs(along) / 0.55f) * inside;
                float fill = 0.2f * inside;
                float a = Mathf.Max(Mathf.Max(fill, rim * 0.95f), Mathf.Max(streak * 0.55f, hair * 0.55f));
                Color c = hair > rim && hair > fill ? ink : Color.white;
                px[y * n + x] = C32(c, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildBubble(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_bubble");
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.4f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = p.magnitude - 0.8f;
                float inside = Cover(d, aa);
                float rim = Cover(Mathf.Abs(d + 0.06f) - 0.06f, aa);
                float hi = Cover((p - new Vector2(-0.3f, 0.32f)).magnitude - 0.17f, aa);
                float a = Mathf.Max(0.22f * inside, Mathf.Max(rim, hi * 0.95f));
                Color c = rim > hi ? Color.Lerp(Color.white, ink, 0.75f) : Color.white;
                px[y * n + x] = C32(c, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildCaustics(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Repeat, "deacube_magic_caustics");
        var px = new Color32[n * n];
        // a tileable cell pattern: 5 x 5 cells, one seeded point each; the bright net is where the two nearest points are nearly equidistant
        const int cells = 5;
        var rnd = new System.Random(71);
        var pts = new Vector2[cells * cells];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((i % cells + 0.15f + 0.7f * (float)rnd.NextDouble()) / cells, (i / cells + 0.15f + 0.7f * (float)rnd.NextDouble()) / cells);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float f1 = 9f, f2 = 9f;
                for (int i = 0; i < pts.Length; i++)
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            float d = (p - (pts[i] + new Vector2(ox, oy))).magnitude;
                            if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                        }
                float edge = f2 - f1;
                float line = Mathf.Clamp01(1f - edge / 0.035f);
                line = line * line * (3f - 2f * line);
                px[y * n + x] = C32(Color.white, line);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildPuff(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_puff");
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.4f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = Mathf.Min(Mathf.Min((p - new Vector2(-0.34f, -0.14f)).magnitude - 0.44f, (p - new Vector2(0.3f, -0.1f)).magnitude - 0.48f), (p - new Vector2(-0.02f, 0.26f)).magnitude - 0.46f);
                float a = Cover(d, aa);
                float inkAmt = 1f - Cover(d + 0.1f, aa);          // 1 in the rim band just inside the edge, 0 deeper inside
                Color c = Color.Lerp(Color.white, ink, inkAmt);
                if (inkAmt < 0.5f && p.y < -0.18f) c *= 0.86f;   // a shaded belly
                px[y * n + x] = C32(c, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildClockFace(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_clockface");
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.2f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float r = p.magnitude;
                float disc = Cover(r - 0.9f, aa);
                float rim = Cover(Mathf.Abs(r - 0.84f) - 0.06f, aa);
                float inner = Cover(Mathf.Abs(r - 0.72f) - 0.012f, aa);
                // twelve ticks (the quarters longer)
                float ang = Mathf.Atan2(p.y, p.x);
                float k = Mathf.Round(ang / (Mathf.PI / 6f));
                float ta = k * Mathf.PI / 6f;
                var dir = new Vector2(Mathf.Cos(ta), Mathf.Sin(ta));
                float along = Vector2.Dot(p, dir), across = Mathf.Abs(p.x * dir.y - p.y * dir.x);
                bool quarter = ((int)k % 3 + 3) % 3 == 0;
                float t0 = quarter ? 0.5f : 0.58f;
                float tick = Cover(Mathf.Max(across - (quarter ? 0.045f : 0.028f), Mathf.Max(t0 - along, along - 0.7f)), aa);
                // a spiral winding out counter-clockwise from the centre, its arrowhead pointing on counter-clockwise (the way time goes back)
                float spiral = 0f;
                if (r > 0.08f && r < 0.5f)
                {
                    float th = Mathf.Repeat(ang, Mathf.PI * 2f);
                    for (int turn = 0; turn < 3; turn++)
                    {
                        float tt = th + turn * Mathf.PI * 2f;          // total angle
                        if (tt > Mathf.PI * 3.3f) continue;
                        float sr = 0.1f + 0.036f * tt;                 // r(θ) of the spiral
                        spiral = Mathf.Max(spiral, Cover(Mathf.Abs(r - sr) - 0.026f, aa));
                    }
                }
                // the arrowhead at the spiral's outer end (θ = 3.3π: pointing counter-clockwise)
                float endA = Mathf.PI * 3.3f;
                float er = 0.1f + 0.036f * endA;
                var e = new Vector2(Mathf.Cos(endA), Mathf.Sin(endA)) * er;
                var tan = new Vector2(-Mathf.Sin(endA), Mathf.Cos(endA));   // counter-clockwise tangent
                var nrm = new Vector2(Mathf.Cos(endA), Mathf.Sin(endA));
                var q = p - e;
                float qa = Vector2.Dot(q, tan), qn = Vector2.Dot(q, nrm);
                float head = Cover(Mathf.Max(-qa, Mathf.Abs(qn) - (0.13f - qa * 0.9f)), aa) * (qa < 0.15f ? 1f : 0f);
                float marks = Mathf.Max(Mathf.Max(rim, inner * 0.6f), Mathf.Max(tick, Mathf.Max(spiral, head)));
                Color c = Color.Lerp(Color.white, ink, marks);
                px[y * n + x] = C32(c, Mathf.Max(disc, marks));
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildClockHands(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_clockhands");
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float aa = 2.2f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                // the long hand up (0 .. 0.66), the short hand right (0 .. 0.42), a centre pin
                float longH = Mathf.Max(Mathf.Abs(p.x) - (0.05f - 0.03f * Mathf.Clamp01(p.y / 0.66f)), Mathf.Max(-p.y - 0.08f, p.y - 0.66f));
                float shortH = Mathf.Max(Mathf.Abs(p.y) - (0.065f - 0.03f * Mathf.Clamp01(p.x / 0.42f)), Mathf.Max(-p.x - 0.08f, p.x - 0.42f));
                float d = Mathf.Min(Mathf.Min(longH, shortH), p.magnitude - 0.1f);
                float a = Cover(d, aa);
                float core = Cover(d + 0.03f, aa) * (p.magnitude > 0.12f ? 1f : 0f);
                Color c = Color.Lerp(ink, Color.Lerp(Pop, Color.white, 0.2f), core * 0.9f);
                px[y * n + x] = C32(c, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    /// <summary>The hero accent of the rewind hands (a warm coral, the comic pop).</summary>
    static readonly Color Pop = new Color(1f, 0.42f, 0.36f);

    static Texture2D BuildSepia(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Repeat, "deacube_magic_sepia");
        var px = new Color32[n * n];
        var rnd = new System.Random(19);
        int[] scratch = { 17, 58, 101 };
        const int cell = 8;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Ben-Day dots on a 45° grid, their size breathing slowly across the tile (a printed tone)
                float u = (x + 0.5f) / cell, v = (y + 0.5f) / cell;
                float gu = u - Mathf.Floor(u) - 0.5f, gv = v - Mathf.Floor(v) - 0.5f;
                float tone = 0.3f + 0.12f * Mathf.Sin(x * 2f * Mathf.PI / n) * Mathf.Sin(y * 4f * Mathf.PI / n);
                float dot = Mathf.Clamp01((tone - Mathf.Sqrt(gu * gu + gv * gv)) * 6f);
                float a = 0.3f + 0.55f * dot;
                foreach (int sx in scratch) { float dx = Mathf.Abs(x - sx - 2f * Mathf.Sin(y * 0.09f + sx)); if (dx < 0.9f) a = Mathf.Max(a, 0.9f); }
                a += ((float)rnd.NextDouble() - 0.5f) * 0.08f;
                px[y * n + x] = C32(Color.white, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildCarpet(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Repeat, "deacube_magic_carpet");
        var px = new Color32[n * n];
        float aa = 2.2f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = RoundSquare(p, 0.84f, 0.24f);
                float inside = Cover(d, aa);
                float rim = Cover(Mathf.Abs(d + 0.08f) - 0.05f, aa);
                float a = Mathf.Max(0.16f, Mathf.Max(0.48f * inside, rim * 0.95f));
                px[y * n + x] = C32(Color.white, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildMirror(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_mirror");
        var px = new Color32[n * n];
        float aa = 2.2f / n;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float streak = 0f;
                float s = u * 1.4f + v * 0.8f;
                for (int k = 0; k < 3; k++) { float c0 = 0.35f + 0.42f * k; streak = Mathf.Max(streak, Mathf.Clamp01(1f - Mathf.Abs(s - c0) / (k == 1 ? 0.09f : 0.05f))); }
                float edge = Mathf.Max(Cover(0.035f - u, aa), Cover(u - 0.965f, aa));
                float top = Cover(Mathf.Abs(v - 0.96f) - 0.025f, aa);               // a bright line along the top (the mirror's edge)
                float ink = Cover(Mathf.Abs(v - 0.995f) - 0.008f, aa);              // an ink hairline above it
                float a = Mathf.Clamp01(0.12f + 0.2f * v * v + 0.5f * streak + edge + top + ink * 0.8f);
                Color c = Color.Lerp(new Color(0.8f, 0.86f, 1f), Color.white, Mathf.Max(Mathf.Max(streak, edge), top));
                if (ink > top) c = Look.InkColor;
                px[y * n + x] = C32(c, a);
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }

    static Texture2D BuildPane(int n)
    {
        var tex = NewTex(n, n, true, TextureWrapMode.Clamp, "deacube_magic_pane");
        var px = new Color32[n * n];
        float aa = 2.2f / n * 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = RoundSquare(p, 0.96f, 0.3f);
                float inside = Cover(d, aa);
                float rim = Cover(Mathf.Abs(d + 0.04f) - 0.025f, aa);
                px[y * n + x] = C32(Color.white, Mathf.Max(0.12f * inside, rim));
            }
        tex.SetPixels32(px); tex.Apply(true, false);
        return tex;
    }
}
