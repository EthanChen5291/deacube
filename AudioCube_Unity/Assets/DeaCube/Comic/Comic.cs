using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The comic HUD kit (package A restyle on package E's look, "3D that reads as 2D"): the palette of the drawn UI; one shape
/// atlas (rounded rect, disc, halftone disc and square, ring bands, a selection band, a burst badge) that
/// <see cref="ComicShape"/> draws panels, discs and mini cubes from; inked glyphs (IconFactory glyphs with a baked ink
/// outline); the chord-wheel wedge; the fonts (Fredoka for the UI, Bangers for the tempo digits: TMP font assets in
/// Resources/Fonts, LiberationSans as the fallback) and the text style (ink outline + hard offset underlay, no glow).
/// Everything is procedural and built once on first use.
/// </summary>
public static class Comic
{
    // ------------------------------------------------------------------ palette (sRGB)
    /// <summary>The ink of every outline (Look.InkColor).</summary>
    public static readonly Color Ink = Look.InkColor;
    /// <summary>Bars, pills and panels: dusk indigo #2E2560 at 94 %.</summary>
    public static readonly Color Indigo = new Color(0.180f, 0.145f, 0.376f, 0.94f);
    /// <summary>Round buttons on indigo.</summary>
    public static readonly Color IndigoDisc = new Color(0.290f, 0.243f, 0.557f);
    /// <summary>Recesses on indigo (slider tracks, dial faces).</summary>
    public static readonly Color IndigoDeep = new Color(0.118f, 0.094f, 0.259f);
    /// <summary>Paper cream #FFF3E6: the inspector card, the prompt card, the tray cards.</summary>
    public static readonly Color Cream = new Color(1f, 0.953f, 0.902f);
    /// <summary>Round buttons on cream (lavender paper).</summary>
    public static readonly Color CreamDisc = new Color(0.925f, 0.878f, 0.980f);
    /// <summary>Recesses on cream (slider tracks).</summary>
    public static readonly Color CreamDeep = new Color(0.835f, 0.776f, 0.878f);
    /// <summary>The hard offset shadow (ink, no blur).</summary>
    public static readonly Color Shadow = new Color(Ink.r, Ink.g, Ink.b, 0.92f);
    /// <summary>The offset "print" shadow on near-black backdrops (the menu wall, the prompt wash), where ink would vanish.</summary>
    public static readonly Color PrintShadow = new Color(0.40f, 0.22f, 0.72f, 1f);
    /// <summary>Comic accent: the lit-island badge and the selection bands.</summary>
    public static readonly Color Pop = new Color(1f, 0.824f, 0.29f);
    // v4 kit (scratchpad/v4/UI.md): print colours for hero punctuation only
    /// <summary>Misregistration / hero shadow magenta (the PRESENT sticker, the tempo digits' misprint).</summary>
    public static readonly Color Magenta = new Color(0.96f, 0.27f, 0.62f);
    /// <summary>Misregistration cyan.</summary>
    public static readonly Color Cyan = new Color(0.30f, 0.86f, 0.96f);
    /// <summary>The violet veil under the menu strip and drawers.</summary>
    public static readonly Color Veil = new Color(0.20f, 0.12f, 0.36f, 0.42f);
    /// <summary>A tear-off / delete red that reads on paper.</summary>
    public static readonly Color Danger = new Color(0.90f, 0.25f, 0.33f);
    /// <summary>The deep ink shadow a cream sticker casts on the sky (hard, no blur).</summary>
    public static readonly Vector2 StickerShadow = new Vector2(4f, -5f);
    public static readonly Vector2 PanelShadow = new Vector2(5f, -6f);
    public const float PanelRadius = 14f, PanelBorder = 3f;

    public static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
    public static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);
    public static bool IsLight(Color c) => Lum(c) > 0.6f;
    static bool Whiteish(Color c) => c.r > 0.9f && c.g > 0.9f && c.b > 0.9f;

    /// <summary>A panel fill for a legacy glass tint: dark glass → indigo, light / cream → cream, anything else opaque as given.</summary>
    public static Color PanelFill(Color c)
    {
        if (IsLight(c)) return Cream;
        if (c.r < 0.2f && c.g < 0.2f && c.b < 0.25f) return Indigo;
        return new Color(c.r, c.g, c.b, Mathf.Max(0.94f, c.a));
    }

    /// <summary>The flat, opaque disc fill a legacy (translucent) button tint stands for on a dark or light surface.
    /// <paramref name="bare"/>: fully transparent tint = a bare glyph (no disc, ring or shadow).</summary>
    public static Color ButtonFill(Color bg, bool light, out bool bare)
    {
        Color baseC = light ? CreamDisc : IndigoDisc;
        bare = bg.a <= 0.02f;
        if (bare) return A(baseC, 0f);
        if (Whiteish(bg)) return bg.a >= 0.5f ? Color.Lerp(baseC, light ? Color.white : Cream, 0.7f) : baseC;   // the white glass tints
        if (bg.a < 0.95f && bg.r < 0.2f && bg.g < 0.2f && bg.b < 0.25f) return light ? Ink : IndigoDeep;     // dark glass (an opaque dark tint stays)
        return Opaque(Color.Lerp(baseC, bg, Mathf.Clamp01(bg.a * 1.05f)));
    }

    /// <summary>The fill of a toggled button: its accent, opaque (a white accent reads as paper).</summary>
    public static Color Solid(Color accent, bool light) => Whiteish(accent) ? (light ? Color.white : Cream) : Opaque(accent);

    /// <summary>A glyph colour on a dark or light surface: the HUD's icon white becomes cream (dark) or ink (light), dark glyphs
    /// ink, pure white paper cream; other colours stay (alpha kept).</summary>
    public static Color IconColor(Color c, bool light)
    {
        bool paletteIcon = Mathf.Abs(c.r - 0.95f) < 0.03f && Mathf.Abs(c.g - 0.94f) < 0.03f && c.b > 0.97f;
        if (paletteIcon) return A(light ? Ink : Cream, c.a >= 0.85f ? 1f : Mathf.Clamp01(c.a * 1.3f));
        if (Lum(c) < 0.22f) return A(Ink, c.a);
        if (Whiteish(c) && c.r > 0.97f && c.g > 0.97f && c.b > 0.97f) return A(Cream, c.a >= 0.8f ? 1f : c.a);
        return c;
    }
    /// <summary>Light glyphs get the ink outline; ink glyphs are drawn plain.</summary>
    public static bool WantsOutline(Color c) => Lum(c) > 0.34f && c.a > 0.15f;

    /// <summary>Hard shadow / press slide of a button of <paramref name="size"/> px.</summary>
    public static Vector2 PressOffset(float size) => size >= 56f ? new Vector2(4f, -4f) : (size >= 30f ? new Vector2(3f, -3f) : new Vector2(2f, -2f));
    /// <summary>Ink ring width of a button of <paramref name="size"/> px.</summary>
    public static float RingWidth(float size) => size >= 40f ? 2.5f : (size >= 24f ? 2f : 1.5f);

    // ------------------------------------------------------------------ builders
    /// <summary>Sets a <see cref="ComicShape"/> up as a button body: disc (or a mini flat cube when <paramref name="square"/>),
    /// ink ring, hard shadow.</summary>
    public static void SetupButton(ComicShape s, float size, bool square)
    {
        s.kind = square ? ComicShape.Kind.Rect : ComicShape.Kind.Disc;
        s.radius = square ? Mathf.Max(4f, size * 0.22f) : 0f;
        s.border = RingWidth(size);
        s.shadowOffset = PressOffset(size);
        s.shadowColor = Shadow;
        s.cube = square ? 0.22f : 0f;
        s.glint = square ? 0.9f : 0f;
        s.color = IndigoDisc;
        s.raycastTarget = true;
    }

    /// <summary>A comic panel on <paramref name="go"/>: flat fill, 3 px ink border, 14 px corners, hard (+5, −6) shadow.</summary>
    public static ComicShape Panel(GameObject go, Color fill, float radius = PanelRadius, bool raycast = true)
    {
        var s = go.GetComponent<ComicShape>();
        if (s == null) s = go.AddComponent<ComicShape>();
        s.kind = ComicShape.Kind.Rect; s.radius = radius; s.border = PanelBorder;
        s.shadowColor = Shadow; s.shadowOffset = PanelShadow;
        s.color = fill; s.light = IsLight(fill); s.raycastTarget = raycast;
        return s;
    }

    /// <summary>A plain shape child (no shadow): <paramref name="border"/> px of ink, rounded by <paramref name="radius"/> px
    /// (≤ 0: a capsule), or a disc.</summary>
    public static ComicShape Shape(Transform parent, string name, Vector2 size, Color fill, float radius, float border, bool disc = false, bool raycast = false)
    {
        var go = UIKit.Obj(name, parent);
        UIKit.RT(go).sizeDelta = size;
        var s = go.AddComponent<ComicShape>();
        s.kind = disc ? ComicShape.Kind.Disc : ComicShape.Kind.Rect;
        s.radius = radius; s.border = border; s.shadowColor = A(Ink, 0f);
        s.color = fill; s.light = IsLight(fill); s.raycastTarget = raycast;
        return s;
    }

    /// <summary>A glyph image drawn <paramref name="px"/> px big (the rect is <see cref="GlyphPad"/> larger for the outline),
    /// centred on its parent.</summary>
    public static Image GlyphImage(Transform parent, string name, string glyph, Color color, float px, bool outline = true)
    {
        var go = UIKit.Obj(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = Glyph(glyph, px, outline); img.color = color; img.raycastTarget = false; img.preserveAspect = true;
        float s = px * GlyphPad;
        UIKit.Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s, s));
        return img;
    }
    /// <summary>A layout slot of <paramref name="px"/> px holding an inked glyph (keeps HUD rows spaced as before).</summary>
    public static Image GlyphSlot(Transform parent, string name, string glyph, Color color, float px)
    {
        var slot = UIKit.Obj(name, parent);
        UIKit.RT(slot).sizeDelta = new Vector2(px, px);
        UIKit.Fixed(slot, px, px);
        return GlyphImage(slot.transform, "Glyph", glyph, color, px, WantsOutline(color));
    }

    // ------------------------------------------------------------------ the shape atlas
    public const int AtlasW = 512, AtlasH = 256;
    /// <summary>Texel rects in the atlas (x, y, w, h; y up).</summary>
    public static readonly RectInt RR = new RectInt(0, 0, 128, 128), Disc = new RectInt(128, 0, 128, 128), HalftoneDisc = new RectInt(128, 128, 128, 128),
        HalftoneSquare = new RectInt(0, 128, 128, 128), RingBand = new RectInt(256, 0, 128, 128), RingDots = new RectInt(384, 0, 128, 128),
        SelectBand = new RectInt(256, 128, 128, 128), Badge = new RectInt(384, 128, 128, 128);
    /// <summary>Rounded-rect source: box inset 4 texels, corner radius 48; the 9-slice spans 2 texels outside the box to 52 inside.</summary>
    public const float RRRadius = 48f, RROuter = 2f, RRBorder = 52f;
    /// <summary>Discs: radius 60 texels in a 128 cell (quads are drawn 62/60 larger so the edge sits on the rect).</summary>
    public const float DiscR = 60f, DiscUV = 62f;
    /// <summary>Ring bands: outer radius 60, inner 49 texels of a 64 half cell.</summary>
    public const float BandOuter = 60f, BandInner = 49f;

    static Texture2D atlas; static Sprite atlasSprite; static readonly Dictionary<int, Sprite> atlasSprites = new Dictionary<int, Sprite>();
    public static Texture2D Atlas { get { if (atlas == null) atlas = BuildAtlas(); return atlas; } }
    /// <summary>A sprite over the whole atlas (ComicShape's texture).</summary>
    public static Sprite AtlasSprite { get { if (atlasSprite == null) { atlasSprite = Sprite.Create(Atlas, new Rect(0, 0, AtlasW, AtlasH), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect); atlasSprite.name = "comicAtlas"; } return atlasSprite; } }
    /// <summary>A sprite over one atlas cell (filled / radial images: ring bands, the badge, the selection band).</summary>
    public static Sprite Cell(RectInt r)
    {
        int key = r.x * 100000 + r.y * 100 + r.width / 32;
        Sprite s;
        if (atlasSprites.TryGetValue(key, out s) && s != null) return s;
        s = Sprite.Create(Atlas, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.name = "comic" + key;
        atlasSprites[key] = s;
        return s;
    }
    public static Sprite RingSprite => Cell(RingBand);
    public static Sprite RingDotsSprite => Cell(RingDots);
    public static Sprite SelectSprite => Cell(SelectBand);
    public static Sprite BadgeSprite => Cell(Badge);
    public static Sprite DiscSprite => Cell(Disc);

    static float Cov(float d) => Mathf.Clamp01(0.5f - d);   // coverage of a signed distance in texels (1-texel AA)

    delegate void Texel(float x, float y, out float cov, out float ink);

    static Texture2D BuildAtlas()
    {
        var px = new Color32[AtlasW * AtlasH];
        var clearWhite = new Color32(255, 255, 255, 0);
        for (int i = 0; i < px.Length; i++) px[i] = clearWhite;
        // rounded rect (9-slice source)
        Paint(px, RR, false, (float x, float y, out float c, out float k) => { c = Cov(Box(x - 64f, y - 64f, 60f, 60f, RRRadius)); k = 0f; });
        // disc
        Paint(px, Disc, false, (float x, float y, out float c, out float k) => { c = Cov(Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) - DiscR); k = 0f; });
        // halftone disc: a 45° dot screen (pitch 16, dots 0.34 of the pitch) inside the disc
        Paint(px, HalftoneDisc, false, (float x, float y, out float c, out float k) =>
        {
            float disc = Cov(Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f)) - (DiscR - 1f));
            c = disc * Cov(DotScreen(x, y, 16f, 5.4f)); k = 0f;
        });
        // halftone square
        Paint(px, HalftoneSquare, false, (float x, float y, out float c, out float k) =>
        {
            c = Cov(Box(x - 64f, y - 64f, 60f, 60f, 0f)) * Cov(DotScreen(x, y, 16f, 5.4f)); k = 0f;
        });
        // ring band with inked edges (level / hold rings, dial tracks)
        Paint(px, RingBand, true, (float x, float y, out float c, out float k) =>
        {
            float r = Mathf.Sqrt((x - 64f) * (x - 64f) + (y - 64f) * (y - 64f));
            float d = Mathf.Max(r - BandOuter, BandInner - r);
            c = Cov(d); k = Mathf.Clamp01(2.6f + d + 0.5f);   // ink within 2.6 texels of either edge
        });
        // halftone ring: two staggered rows of dots along the band (the tempo arc)
        Paint(px, RingDots, false, (float x, float y, out float c, out float k) =>
        {
            float dx = x - 64f, dy = y - 64f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Atan2(dy, dx);
            float best = 99f;
            for (int row = 0; row < 2; row++)
            {
                float rr = row == 0 ? 52.2f : 57.2f;
                float step = 5.6f / rr;
                float off = row == 0 ? 0f : step * 0.5f;
                float t = Mathf.Round((a - off) / step) * step + off;
                float ex = Mathf.Cos(t) * rr - dx, ey = Mathf.Sin(t) * rr - dy;
                best = Mathf.Min(best, Mathf.Sqrt(ex * ex + ey * ey) - 2.35f);
            }
            c = Cov(best) * Cov(Mathf.Max(r - BandOuter, BandInner - r)); k = 0f;
        });
        // selection band: a rounded square ring with inked edges
        Paint(px, SelectBand, true, (float x, float y, out float c, out float k) =>
        {
            float outer = Box(x - 64f, y - 64f, 60f, 60f, 20f);
            float d = Mathf.Max(outer, -(outer + 9f));
            c = Cov(d); k = Mathf.Clamp01(2.4f + d + 0.5f);
        });
        // badge: a ten-point burst with a heavy ink rim
        Paint(px, Badge, true, (float x, float y, out float c, out float k) =>
        {
            var p = new Vector2((x - 64f) / 64f, (y - 64f) / 64f);
            float d = IconFactory.P.Star(p, 10, 0.94f, 0.68f, 90f) * 64f;
            c = Cov(d); k = Mathf.Clamp01(6f + d + 0.5f);
        });
        var tex = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, 5, false) { name = "ComicAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return tex;
    }

    /// <summary>Paints one atlas cell texel by texel: <paramref name="f"/> gives coverage and ink amount (0 white, 1 ink) at the
    /// texel centre in cell coordinates. Inked cells keep ink under transparent texels so the mips fade to ink, not white.</summary>
    static void Paint(Color32[] px, RectInt cell, bool inked, Texel f)
    {
        Color ink = Ink;
        for (int y = 0; y < cell.height; y++)
            for (int x = 0; x < cell.width; x++)
            {
                float c, k;
                f(x + 0.5f, y + 0.5f, out c, out k);
                if (!inked) k = 0f;
                if (c <= 0f && !inked) continue;
                Color col = Color.Lerp(Color.white, ink, c > 0f ? k : 1f);
                px[(cell.y + y) * AtlasW + cell.x + x] = new Color32((byte)Mathf.RoundToInt(col.r * 255f), (byte)Mathf.RoundToInt(col.g * 255f), (byte)Mathf.RoundToInt(col.b * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c) * 255f));
            }
    }

    static float Box(float x, float y, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x) - hx + r, qy = Mathf.Abs(y) - hy + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// <summary>Signed distance (texels) to the nearest dot of a 45° halftone screen.</summary>
    static float DotScreen(float x, float y, float pitch, float radius)
    {
        const float k = 0.70710678f;
        float u = (x + y) * k, v = (x - y) * k;
        float cu = Mathf.Round(u / pitch) * pitch, cv = Mathf.Round(v / pitch) * pitch;
        float du = u - cu, dv = v - cv;
        return Mathf.Sqrt(du * du + dv * dv) - radius;
    }

    // ------------------------------------------------------------------ inked glyphs
    /// <summary>Inked glyph textures are 160² for a 128² glyph: the image rect is this much larger than the glyph.</summary>
    public const float GlyphPad = 160f / 128f;
    const int GlyphN = 160, GlyphOff = 16;
    static readonly Dictionary<string, Sprite> glyphs = new Dictionary<string, Sprite>();

    /// <summary>Outline width (glyph texels) for a glyph drawn <paramref name="px"/> px big: about 2 px on screen.</summary>
    public static int OutlineTexels(float px) => Mathf.Clamp(Mathf.RoundToInt(2.1f * 128f / Mathf.Max(8f, px) * 0.5f) * 2, 6, 14);

    /// <summary>The IconFactory glyph <paramref name="name"/> on a padded 160² sprite, with an ink outline about 2 px wide at
    /// <paramref name="px"/> px (white glyph, ink outline: tint colours the glyph, the outline stays dark) or plain.</summary>
    public static Sprite Glyph(string name, float px, bool outline)
    {
        if (string.IsNullOrEmpty(name)) name = "dot";
        int w = outline ? OutlineTexels(px) : 0;
        string key = name + "|" + w;
        Sprite s;
        if (glyphs.TryGetValue(key, out s) && s != null) return s;
        var tex = BuildGlyph(name, w);
        s = Sprite.Create(tex, new Rect(0, 0, GlyphN, GlyphN), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.name = "ink_" + key;
        glyphs[key] = s;
        return s;
    }

    static Texture2D BuildGlyph(string name, int w)
    {
        int n = GlyphN, src = IconFactory.Size;
        var srcPx = IconFactory.GetTexture(name).GetPixels32();
        var g = new float[n * n];
        var inside = new bool[n * n];
        for (int y = 0; y < src; y++)
            for (int x = 0; x < src; x++)
            {
                float a = srcPx[y * src + x].a / 255f;
                int i = (y + GlyphOff) * n + x + GlyphOff;
                g[i] = a; inside[i] = a >= 0.5f;
            }
        float[] dist = w > 0 ? Edt(inside, n) : null;
        Color ink = Ink;
        var px = new Color32[n * n];
        for (int i = 0; i < px.Length; i++)
        {
            float ga = g[i];
            float oa = dist != null ? Mathf.Clamp01(w + 1f - dist[i]) : 0f;
            float a = ga + oa * (1f - ga);
            Color c;
            if (a > 0.0001f) c = (Color.white * ga + ink * (oa * (1f - ga))) / a;
            else c = dist != null ? ink : Color.white;
            px[i] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, 4, false) { name = "ink_" + name + "_" + w, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }

    /// <summary>Euclidean distance (texels) from every texel to the nearest inside texel (Felzenszwalb–Huttenlocher, two passes).</summary>
    static float[] Edt(bool[] inside, int n)
    {
        const float Inf = 1e20f;
        var grid = new float[n * n];
        var f = new float[n]; var d = new float[n]; var v = new int[n]; var z = new float[n + 1];
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++) f[y] = inside[y * n + x] ? 0f : Inf;
            Dt1(f, d, v, z, n);
            for (int y = 0; y < n; y++) grid[y * n + x] = d[y];
        }
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++) f[x] = grid[y * n + x];
            Dt1(f, d, v, z, n);
            for (int x = 0; x < n; x++) grid[y * n + x] = Mathf.Sqrt(d[x]);
        }
        return grid;
    }

    static void Dt1(float[] f, float[] d, int[] v, float[] z, int n)
    {
        int k = 0; v[0] = 0; z[0] = -1e20f; z[1] = 1e20f;
        for (int q = 1; q < n; q++)
        {
            float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
            while (k > 0 && s <= z[k]) { k--; s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]); }
            k++; v[k] = q; z[k] = s; z[k + 1] = 1e20f;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            float dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }

    // ------------------------------------------------------------------ the chord-wheel wedge
    static readonly Dictionary<int, Sprite> wedges = new Dictionary<int, Sprite>();
    /// <summary>The chord-wheel wedge (ring sector r 0.5..0.97, span − 3°, pointing +x) with an inked rim, 512² (readable: the
    /// wheel hit-tests its alpha).</summary>
    public static Sprite Wedge(float span)
    {
        int key = Mathf.RoundToInt(span * 10f);
        Sprite s;
        if (wedges.TryGetValue(key, out s) && s != null) return s;
        const int n = 512;
        float a0 = -span * 0.5f + 1.5f, a1 = span * 0.5f - 1.5f;
        var px = new Color32[n * n];
        Color ink = Ink;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = WedgeSdf(p, 0.5f, 0.97f, a0, a1) * n * 0.5f;   // texels
                float c = Cov(d), k = Mathf.Clamp01(4.2f + d + 0.5f);
                Color col = Color.Lerp(Color.white, ink, c > 0f ? k : 1f);
                px[y * n + x] = new Color32((byte)Mathf.RoundToInt(col.r * 255f), (byte)Mathf.RoundToInt(col.g * 255f), (byte)Mathf.RoundToInt(col.b * 255f), (byte)Mathf.RoundToInt(c * 255f));
            }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, 4, false) { name = "comicWedge", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        tex.SetPixels32(px);
        tex.Apply(true, false);
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.name = "comicWedge" + key;
        wedges[key] = s;
        return s;
    }

    static float WedgeSdf(Vector2 p, float rInner, float rOuter, float a0, float a1)
    {
        float rr = p.magnitude;
        float band = Mathf.Max(rr - rOuter, rInner - rr);
        float mid = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f * Mathf.Deg2Rad;
        float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        float da = Mathf.Abs(Mathf.DeltaAngle(ang, mid)) * Mathf.Deg2Rad;
        float angular = (da - half) * Mathf.Max(rr, 0.05f);
        return Mathf.Max(band, angular);
    }

    // ------------------------------------------------------------------ fonts and text
    static TMP_FontAsset uiFont, digitFont; static bool fontsTried;
    static readonly Dictionary<TMP_FontAsset, Material> inkMats = new Dictionary<TMP_FontAsset, Material>();

    static void LoadFonts()
    {
        if (fontsTried) return;
        fontsTried = true;
        uiFont = Resources.Load<TMP_FontAsset>("Fonts/Fredoka SDF");
        digitFont = Resources.Load<TMP_FontAsset>("Fonts/Bangers SDF");
    }
    /// <summary>The rounded bold UI font (Fredoka SemiBold, SIL OFL); LiberationSans when the asset is missing.</summary>
    public static TMP_FontAsset Font { get { LoadFonts(); return uiFont != null ? uiFont : TMP_Settings.defaultFontAsset; } }
    /// <summary>The comic digits of the tempo (Bangers, SIL OFL); the UI font when missing.</summary>
    public static TMP_FontAsset DigitFont { get { LoadFonts(); return digitFont != null ? digitFont : Font; } }
    /// <summary>Both comic fonts were found in Resources/Fonts.</summary>
    public static bool FontsInstalled { get { LoadFonts(); return uiFont != null && digitFont != null; } }

    /// <summary>The ink material of a font: ink outline + a hard offset underlay (drop shadow without blur), no glow. The preset
    /// "Fonts/&lt;font&gt; Ink" (created with the font assets, so the shader variant ships) is copied when present.</summary>
    public static Material InkMaterial(TMP_FontAsset f)
    {
        if (f == null) return null;
        Material m;
        if (inkMats.TryGetValue(f, out m) && m != null) return m;
        var preset = Resources.Load<Material>("Fonts/" + f.name + " Ink");
        m = new Material(preset != null ? preset : f.material) { name = f.name + " Ink (runtime)" };
        m.SetTexture("_MainTex", f.atlasTexture);
        m.EnableKeyword("OUTLINE_ON"); m.EnableKeyword("UNDERLAY_ON");
        m.SetFloat("_FaceDilate", 0.12f);
        m.SetColor("_OutlineColor", Ink); m.SetFloat("_OutlineWidth", 0.32f); m.SetFloat("_OutlineSoftness", 0f);
        m.SetColor("_UnderlayColor", Ink); m.SetFloat("_UnderlayOffsetX", 0.5f); m.SetFloat("_UnderlayOffsetY", -0.62f);
        m.SetFloat("_UnderlayDilate", 0.44f); m.SetFloat("_UnderlaySoftness", 0f);
        inkMats[f] = m;
        return m;
    }

    static readonly Dictionary<TMP_FontAsset, Material> misprintMats = new Dictionary<TMP_FontAsset, Material>();

    /// <summary>v4 hero lettering (the tempo digits, research §3 rule 10): the ink outline of <see cref="InkMaterial"/> with the hard
    /// underlay printed in <see cref="Magenta"/>, a little off register (misregistration instead of the ink drop shadow).</summary>
    public static Material MisprintMaterial(TMP_FontAsset f)
    {
        if (f == null) return null;
        Material m;
        if (misprintMats.TryGetValue(f, out m) && m != null) return m;
        m = new Material(InkMaterial(f)) { name = f.name + " Misprint (runtime)" };
        m.SetColor("_UnderlayColor", Magenta); m.SetFloat("_UnderlayOffsetX", 0.72f); m.SetFloat("_UnderlayOffsetY", -0.42f);
        m.SetFloat("_UnderlayDilate", 0.5f); m.SetFloat("_UnderlaySoftness", 0f);
        misprintMats[f] = m;
        return m;
    }

    /// <summary>Comic lettering on <paramref name="t"/>: the UI font (or <paramref name="font"/>), light text inked (outline +
    /// hard shadow), dark text plain.</summary>
    public static void StyleText(TextMeshProUGUI t, TMP_FontAsset font = null)
    {
        if (t == null) return;
        var f = font != null ? font : Font;
        if (t.font != f) t.font = f;
        bool lightText = Lum(t.color) > 0.45f;
        var m = lightText ? InkMaterial(f) : f.material;
        if (m != null && t.fontSharedMaterial != m) t.fontSharedMaterial = m;
    }
}
