using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural, anti-aliased icon sprites rasterized from signed distance fields.
/// The HUD is word-free, so every glyph the app needs is generated here at runtime.
/// Coordinates are normalized to [-1, 1] with +y up.
/// </summary>
public static class IconFactory
{
    public const int Size = 128;
    const float AA = 2.4f / Size;
    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    delegate float SDF(Vector2 p);

    // ---- v3: glyphs registered by other files (SPEC v3 §6: IconFactory is frozen during the build; use Register + P)
    static readonly Dictionary<string, Func<Vector2, float>> custom = new Dictionary<string, Func<Vector2, float>>();
    /// <summary>Registers (or replaces) a glyph drawn from the public primitives in <see cref="P"/>; call it before the first Get of that name.</summary>
    public static void Register(string name, Func<Vector2, float> sdf)
    {
        if (string.IsNullOrEmpty(name) || sdf == null) return;
        custom[name] = sdf;
        sprites.Remove(name); textures.Remove(name);
    }

    /// <summary>
    /// Every glyph and soft texture added for v2 (tests rasterize each one). "polyN" is parametric for N = 3..8.
    /// "shadow" is the pre-existing soft drop-shadow texture and keeps rendering as before, so the cube-modifier
    /// glyph (solid box outline with a dashed outline above) is registered as "shadowMod".
    /// </summary>
    public static readonly string[] V2Names =
    {
        "keys", "pluck", "pad", "bolt", "bass", "bell", "strings", "choir", "piano", "kit", "fx",
        "cloud", "storm", "climateAuto", "climateSun", "climateCloud", "climateStorm", "flame", "flourish", "moon", "repeat2", "repeat4",
        "poly3", "poly4", "poly5", "poly6",
        "ghost", "accent", "ratchet2", "ratchet3", "coin", "tie", "lift",
        "octUp", "octDown", "twin", "shadowMod", "echo", "comet", "stamp",
        "stairsUp", "stairsDown", "zigzag", "mountain", "record", "pencil",
        "eclipse", "waves3", "funnel", "dropOut", "spot", "chevronU", "diamondDot", "necklace", "mute", "half",
        "burst", "noise",
    };

    public static Sprite Get(string name)
    {
        if (sprites.TryGetValue(name, out var s) && s != null) return s;
        var tex = GetTexture(name);
        Vector4 border = Vector4.zero;
        if (name == "panel") border = new Vector4(30, 30, 30, 30);
        else if (name == "pill") border = new Vector4(60, 60, 60, 60);
        else if (name == "square") border = new Vector4(24, 24, 24, 24);
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.name = "icon_" + name;
        sprites[name] = s;
        return s;
    }

    public static Texture2D GetTexture(string name)
    {
        if (textures.TryGetValue(name, out var t) && t != null) return t;
        t = Build(name);
        textures[name] = t;
        return t;
    }

    /// <summary>
    /// True when <paramref name="name"/> resolves to a real glyph or soft texture, i.e. Get/GetTexture would not
    /// fall back to the placeholder disc. Never rasterizes (the noise field is built lazily on first sampling).
    /// </summary>
    public static bool Has(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return SoftAlpha(name) != null || ShapeOrNull(name) != null;
    }

    // ------------------------------------------------------------------ rasterizer
    static Texture2D Build(string name)
    {
        int n = Size;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "icon_" + name };
        if (name == "noise") tex.wrapMode = TextureWrapMode.Repeat;   // the value-noise lattice wraps, so the texture tiles
        var px = new Color32[n * n];
        Func<Vector2, float> soft = SoftAlpha(name);
        SDF f = soft == null ? Shape(name) : null;
        float aa = 2.4f / n;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float a;
                if (soft != null) a = Mathf.Clamp01(soft(p));
                else { float d = f(p); a = Mathf.Clamp01(0.5f - d / aa); }
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    /// <summary>Coverage of an SDF value with the same anti-aliasing width the rasterizer uses (for two-pass soft glyphs).</summary>
    static float SdfAlpha(float d) => Mathf.Clamp01(0.5f - d / AA);

    /// <summary>Textures with soft (non-SDF) alpha: glows, gradients, translucent two-pass glyphs, noise.</summary>
    static Func<Vector2, float> SoftAlpha(string name)
    {
        switch (name)
        {
            case "glow": return p => Mathf.Pow(Mathf.Clamp01(1f - p.magnitude), 2.2f);
            case "glowSoft": return p => Mathf.Pow(Mathf.Clamp01(1f - p.magnitude), 4f);
            case "dust": return p => Mathf.Pow(Mathf.Clamp01(1f - p.magnitude * 1.05f), 1.6f);
            case "pillar": return p => { float v = (p.y + 1f) * 0.5f; float u = 1f - Mathf.Abs(p.x); return Mathf.Pow(Mathf.Clamp01(u), 2.4f) * Mathf.Pow(1f - v, 1.6f) * (0.35f + 0.65f * Mathf.Clamp01(v * 6f)); };
            case "ringBand": return p => { float v = (p.y + 1f) * 0.5f; return Mathf.Pow(Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI), 1.4f); };
            case "hgrad": return p => Mathf.Clamp01((p.x + 1f) * 0.5f);
            case "vgrad": return p => Mathf.Clamp01((p.y + 1f) * 0.5f);
            case "shadow": return p => Mathf.Pow(Mathf.Clamp01(1f - p.magnitude), 1.2f) * 0.9f;
            case "white": return p => 1f;
            case "segment": return p => { float u = (p.x + 1f) * 0.5f; return Mathf.Clamp01((u - 0.04f) / 0.03f) * Mathf.Clamp01((0.92f - u) / 0.03f); };
            // ---- v2
            case "pad": return p => Mathf.Clamp01(0.5f - Box(p, 0, 0, 0.66f, 0.42f, 0.3f) / 0.26f);          // soft-edged cushion
            case "dropOut":                                                                                    // layers, top two bars at 30 %
                return p => Mathf.Max(SdfAlpha(Box(p, 0, -0.45f, 0.62f, 0.14f, 0.1f)),
                                      0.3f * SdfAlpha(U(Box(p, 0, 0f, 0.62f, 0.14f, 0.1f), Box(p, 0, 0.45f, 0.62f, 0.14f, 0.1f))));
            case "spot":                                                                                       // lamp + translucent beam + lit floor
                return p =>
                {
                    float solid = U(Box(p, 0, 0.7f, 0.3f, 0.15f, 0.08f), Box(p, 0, -0.7f, 0.76f, 0.09f, 0.09f));
                    float beam = Poly(p, new Vector2(-0.18f, 0.56f), new Vector2(0.18f, 0.56f), new Vector2(0.76f, -0.62f), new Vector2(-0.76f, -0.62f));
                    return Mathf.Max(SdfAlpha(solid), 0.35f * SdfAlpha(beam));
                };
            case "burst": return Burst;
            case "noise":
                {
                    float[] field = null;   // built lazily so Has() stays allocation-free
                    return p =>
                    {
                        if (field == null) field = NoiseField();
                        int x = Mathf.Clamp(Mathf.RoundToInt((p.x + 1f) * 0.5f * Size - 0.5f), 0, Size - 1);
                        int y = Mathf.Clamp(Mathf.RoundToInt((p.y + 1f) * 0.5f * Size - 0.5f), 0, Size - 1);
                        return field[y * Size + x];
                    };
                }
        }
        return null;
    }

    /// <summary>36 radial spokes over a bright core, fading out by r = 1 (Fx.Starburst).</summary>
    static float Burst(Vector2 p)
    {
        float r = p.magnitude;
        float theta = Mathf.Atan2(p.y, p.x);
        float spokes = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(36f * theta)), 6f);
        float fall = Mathf.Pow(Mathf.Clamp01(1f - r), 1.2f);
        float core = Mathf.Clamp01((0.36f - r) / 0.1f);
        return Mathf.Clamp01(Mathf.Max(core, spokes * fall));
    }

    /// <summary>Tileable value noise, 2 octaves (8 and 16 cells), normalised to 0..1 over the texture.</summary>
    static float[] NoiseField()
    {
        int n = Size;
        var f = new float[n * n];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                float val = ValueNoise(u, v, 8, 1) * 0.667f + ValueNoise(u, v, 16, 2) * 0.333f;
                f[y * n + x] = val;
                if (val < lo) lo = val;
                if (val > hi) hi = val;
            }
        }
        float inv = hi > lo ? 1f / (hi - lo) : 1f;
        for (int i = 0; i < f.Length; i++) f[i] = (f[i] - lo) * inv;
        return f;
    }

    static float ValueNoise(float u, float v, int cells, int seed)
    {
        float fx = u * cells, fy = v * cells;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;   // wrap the lattice: the texture tiles
        x0 %= cells; y0 %= cells;
        float a = Lattice(x0, y0, seed), b = Lattice(x1, y0, seed), c = Lattice(x0, y1, seed), d = Lattice(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    static float Lattice(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // ------------------------------------------------------------------ sdf primitives
    static float Len(Vector2 v) => v.magnitude;
    static float Circle(Vector2 p, float cx, float cy, float r) => Len(new Vector2(p.x - cx, p.y - cy)) - r;
    static float Ring(Vector2 p, float cx, float cy, float r, float w) => Mathf.Abs(Circle(p, cx, cy, r)) - w;
    static float Box(Vector2 p, float cx, float cy, float hx, float hy, float r = 0f)
    {
        var q = new Vector2(Mathf.Abs(p.x - cx) - hx + r, Mathf.Abs(p.y - cy) - hy + r);
        return Len(Vector2.Max(q, Vector2.zero)) + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }
    static float Seg(Vector2 p, float ax, float ay, float bx, float by, float w)
    {
        var a = new Vector2(ax, ay); var b = new Vector2(bx, by);
        var pa = p - a; var ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
        return Len(pa - ba * h) - w;
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float Tri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var e0 = b - a; var e1 = c - b; var e2 = a - c;
        var v0 = p - a; var v1 = p - b; var v2 = p - c;
        var pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
        var pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
        var pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
        float s = Mathf.Sign(Cross(e0, e2));
        var d = Vector2.Min(Vector2.Min(new Vector2(Vector2.Dot(pq0, pq0), s * Cross(v0, e0)), new Vector2(Vector2.Dot(pq1, pq1), s * Cross(v1, e1))), new Vector2(Vector2.Dot(pq2, pq2), s * Cross(v2, e2)));
        return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
    }
    static float Tri(Vector2 p, float ax, float ay, float bx, float by, float cx, float cy) => Tri(p, new Vector2(ax, ay), new Vector2(bx, by), new Vector2(cx, cy));
    static float Poly(Vector2 p, params Vector2[] v)
    {
        int n = v.Length;
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = n - 1; i < n; j = i, i++)
        {
            var e = v[j] - v[i]; var w = p - v[i];
            var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c0 = p.y >= v[i].y, c1 = p.y < v[j].y, c2 = e.x * w.y > e.y * w.x;
            if ((c0 && c1 && c2) || (!c0 && !c1 && !c2)) s *= -1f;
        }
        return s * Mathf.Sqrt(d);
    }
    static float Arc(Vector2 p, float cx, float cy, float r, float w, float a0, float a1)
    {
        var q = new Vector2(p.x - cx, p.y - cy);
        float ang = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
        float span = a1 - a0;
        float rel = Mathf.Repeat(ang - a0, 360f);
        if (rel <= span) return Mathf.Abs(Len(q) - r) - w;
        var e0 = new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
        var e1 = new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
        return Mathf.Min(Len(q - e0), Len(q - e1)) - w;
    }
    static float Head(Vector2 p, float tipx, float tipy, float dirDeg, float size)
    {
        var dir = new Vector2(Mathf.Cos(dirDeg * Mathf.Deg2Rad), Mathf.Sin(dirDeg * Mathf.Deg2Rad));
        var perp = new Vector2(-dir.y, dir.x);
        var tip = new Vector2(tipx, tipy);
        var b = tip - dir * size;
        return Tri(p, tip, b + perp * size * 0.72f, b - perp * size * 0.72f);
    }
    static float Star(Vector2 p, int points, float outer, float inner, float rotDeg = 90f)
    {
        var v = new Vector2[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            float a = (rotDeg + i * 180f / points) * Mathf.Deg2Rad;
            float r = (i % 2 == 0) ? outer : inner;
            v[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return Poly(p, v);
    }
    static float Wedge(Vector2 p, float rInner, float rOuter, float a0, float a1)
    {
        float rr = Len(p);
        float band = Mathf.Max(rr - rOuter, rInner - rr);
        float mid = (a0 + a1) * 0.5f * Mathf.Deg2Rad;
        float half = (a1 - a0) * 0.5f * Mathf.Deg2Rad;
        float ang = Mathf.Atan2(p.y, p.x);
        float da = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, mid * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
        float angular = (da - half) * Mathf.Max(rr, 0.05f);
        return Mathf.Max(band, angular);
    }
    static Vector2 Rot(Vector2 p, float deg)
    {
        float c = Mathf.Cos(deg * Mathf.Deg2Rad), s = Mathf.Sin(deg * Mathf.Deg2Rad);
        return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
    }
    static float U(float a, float b) => Mathf.Min(a, b);
    static float U(float a, float b, float c) => Mathf.Min(a, Mathf.Min(b, c));
    static float U(params float[] v) { float m = v[0]; for (int i = 1; i < v.Length; i++) m = Mathf.Min(m, v[i]); return m; }
    static float Sub(float a, float b) => Mathf.Max(a, -b);

    // ------------------------------------------------------------------ compound pieces shared by several glyphs
    static float Loop(Vector2 p) => U(Arc(p, 0, 0, 0.6f, 0.11f, -60f, 240f), Head(p, -0.3f + Mathf.Cos(330f * Mathf.Deg2Rad) * 0.16f, -0.52f + Mathf.Sin(330f * Mathf.Deg2Rad) * 0.16f, 330f, 0.34f));
    static float SquareRing(Vector2 p, float cx, float cy, float half, float stroke) => Sub(Box(p, cx, cy, half, half, 0.1f), Box(p, cx, cy, half - stroke, half - stroke, 0.05f));
    static float ChevronU(Vector2 p, float cx, float cy, float s, float w) => U(Seg(p, cx - 0.62f * s, cy - 0.28f * s, cx, cy + 0.3f * s, w), Seg(p, cx, cy + 0.3f * s, cx + 0.62f * s, cy - 0.28f * s, w));
    static float ChevronD(Vector2 p, float cx, float cy, float s, float w) => U(Seg(p, cx - 0.62f * s, cy + 0.28f * s, cx, cy - 0.3f * s, w), Seg(p, cx, cy - 0.3f * s, cx + 0.62f * s, cy + 0.28f * s, w));
    /// <summary>Cloud silhouette (three lobes on a flat base), scaled by s and offset by (dx, dy).</summary>
    static float Cloud(Vector2 p, float s, float dx, float dy)
    {
        var q = new Vector2((p.x - dx) / s, (p.y - dy) / s);
        return s * U(Circle(q, -0.4f, -0.1f, 0.4f), Circle(q, 0.02f, 0.16f, 0.48f), Circle(q, 0.42f, -0.1f, 0.38f), Box(q, 0, -0.34f, 0.8f, 0.22f, 0.14f));
    }
    static readonly Vector2[] BoltPts = { new Vector2(-0.1f, 0.86f), new Vector2(-0.58f, -0.02f), new Vector2(-0.16f, -0.02f), new Vector2(-0.36f, -0.86f), new Vector2(0.58f, 0.14f), new Vector2(0.16f, 0.14f), new Vector2(0.4f, 0.86f) };
    /// <summary>Lightning bolt, scaled by s, offset by (dx, dy) and fattened by <paramref name="round"/>.</summary>
    static float Bolt(Vector2 p, float s, float dx, float dy, float round)
    {
        var q = new Vector2((p.x - dx) / s, (p.y - dy) / s);
        return s * Poly(q, BoltPts) - round;
    }
    static float Sun(Vector2 p, float rCore, float r0, float r1, float w)
    {
        float d = Circle(p, 0, 0, rCore);
        for (int i = 0; i < 8; i++) { float a = i * 45f * Mathf.Deg2Rad; d = U(d, Seg(p, Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, w)); }
        return d;
    }

    // ------------------------------------------------------------------ icon library
    /// <summary>v3: the SDF primitives for glyphs registered from other files (coordinates [-1, 1], +y up, negative inside).</summary>
    public static class P
    {
        public static float Circle(Vector2 p, float cx, float cy, float r) => IconFactory.Circle(p, cx, cy, r);
        public static float Ring(Vector2 p, float cx, float cy, float r, float w) => IconFactory.Ring(p, cx, cy, r, w);
        public static float Box(Vector2 p, float cx, float cy, float hx, float hy, float r = 0f) => IconFactory.Box(p, cx, cy, hx, hy, r);
        public static float Seg(Vector2 p, float ax, float ay, float bx, float by, float w) => IconFactory.Seg(p, ax, ay, bx, by, w);
        public static float Tri(Vector2 p, float ax, float ay, float bx, float by, float cx, float cy) => IconFactory.Tri(p, ax, ay, bx, by, cx, cy);
        public static float Poly(Vector2 p, params Vector2[] v) => IconFactory.Poly(p, v);
        public static float Arc(Vector2 p, float cx, float cy, float r, float w, float a0, float a1) => IconFactory.Arc(p, cx, cy, r, w, a0, a1);
        public static float Head(Vector2 p, float tipx, float tipy, float dirDeg, float size) => IconFactory.Head(p, tipx, tipy, dirDeg, size);
        public static float Star(Vector2 p, int points, float outer, float inner, float rotDeg = 90f) => IconFactory.Star(p, points, outer, inner, rotDeg);
        public static float SquareRing(Vector2 p, float cx, float cy, float half, float stroke) => IconFactory.SquareRing(p, cx, cy, half, stroke);
        public static float U(params float[] v) => IconFactory.U(v);
        public static float Sub(float a, float b) => IconFactory.Sub(a, b);
    }

    static SDF Shape(string name)
    {
        var f = ShapeOrNull(name);
        if (f != null) return f;
        Debug.LogWarning("IconFactory: unknown icon " + name);
        return p => Circle(p, 0, 0, 0.5f);
    }

    /// <summary>The glyph library; null when the name is unknown (Shape() adds the placeholder fallback).</summary>
    static SDF ShapeOrNull(string name)
    {
        Func<Vector2, float> cf;
        if (custom.TryGetValue(name, out cf)) return p => cf(p);
        int n; float span;
        if (name.StartsWith("dots") && int.TryParse(name.Substring(4), out n))
        {
            int c = Mathf.Clamp(n, 1, 8);
            return p =>
            {
                float d = 9f;
                float r = Mathf.Min(0.16f, 0.62f / c);
                for (int i = 0; i < c; i++)
                {
                    float x = -0.82f + (i + 0.5f) * 1.64f / c;
                    d = U(d, Circle(p, x, 0f, r));
                }
                return d;
            };
        }
        if (name.StartsWith("slices") && int.TryParse(name.Substring(6), out n))
        {
            int c = Mathf.Clamp(n, 1, 16);
            return p =>
            {
                float d = Circle(p, 0, 0, 0.74f);
                if (c <= 1) return d;
                for (int k = 0; k < c; k++)
                {
                    float a = k * 360f / c * Mathf.Deg2Rad;
                    d = Sub(d, Seg(p, 0, 0, Mathf.Cos(a) * 0.9f, Mathf.Sin(a) * 0.9f, 0.07f));
                }
                return d;
            };
        }
        if (name.StartsWith("wedge") && float.TryParse(name.Substring(5), out span))
        {
            float sp = span;
            return p => Wedge(p, 0.5f, 0.97f, -sp * 0.5f + 1.5f, sp * 0.5f - 1.5f);
        }
        if (name.StartsWith("poly") && int.TryParse(name.Substring(4), out n))
        {
            // regular N-gon ring, vertex up: outer r 0.8, inner N-gon inset so every edge is 0.2 thick
            int sides = Mathf.Clamp(n, 3, 8);
            float rOuter = 0.8f, rInner = rOuter - 0.2f / Mathf.Cos(Mathf.PI / sides);
            var outer = new Vector2[sides]; var inner = new Vector2[sides];
            float ymin = 1f, ymax = -1f;
            for (int i = 0; i < sides; i++)
            {
                float a = (90f + i * 360f / sides) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                outer[i] = dir * rOuter; inner[i] = dir * rInner;
                ymin = Mathf.Min(ymin, outer[i].y); ymax = Mathf.Max(ymax, outer[i].y);
            }
            float dy = (ymax + ymin) * 0.5f;   // centre the bounding box (odd N-gons sit low otherwise)
            return p => { var q = new Vector2(p.x, p.y + dy); return Sub(Poly(q, outer), Poly(q, inner)); };
        }
        switch (name)
        {
            case "dot": return p => Circle(p, 0, 0, 0.92f);
            case "dotSmall": return p => Circle(p, 0, 0, 0.5f);
            case "ring": return p => Ring(p, 0, 0, 0.72f, 0.14f);
            case "ringThin": return p => Ring(p, 0, 0, 0.82f, 0.08f);
            case "square": return p => Box(p, 0, 0, 0.94f, 0.94f, 0.32f);
            case "panel": return p => Box(p, 0, 0, 0.985f, 0.985f, 0.42f);
            case "pill": return p => Box(p, 0, 0, 0.985f, 0.985f, 0.9f);
            case "bar": return p => Box(p, 0, 0, 0.14f, 0.95f, 0.1f);
            case "hbar": return p => Box(p, 0, 0, 0.95f, 0.14f, 0.1f);
            case "play": return p => Tri(p, -0.5f, -0.64f, -0.5f, 0.64f, 0.72f, 0f) - 0.07f;
            case "pause": return p => U(Box(p, -0.3f, 0, 0.17f, 0.6f, 0.07f), Box(p, 0.3f, 0, 0.17f, 0.6f, 0.07f));
            case "stop": return p => Box(p, 0, 0, 0.55f, 0.55f, 0.14f);
            case "rewind": return p => U(Box(p, -0.62f, 0, 0.1f, 0.58f, 0.05f), Tri(p, 0.7f, -0.58f, 0.7f, 0.58f, -0.34f, 0f) - 0.05f);
            case "loop": return p => Loop(p);
            case "metronome":
                return p => U(Sub(Tri(p, -0.66f, -0.66f, 0.66f, -0.66f, 0f, 0.76f) - 0.03f, Tri(p, -0.4f, -0.5f, 0.4f, -0.5f, 0f, 0.42f)), Seg(p, 0, -0.5f, 0.34f, 0.38f, 0.065f), Circle(p, 0.34f, 0.38f, 0.13f));
            case "pencil":
                return p => { var q = Rot(p, -45f); return U(Sub(Box(q, 0.12f, 0.02f, 0.17f, 0.56f, 0.04f), Box(q, 0.12f, 0.42f, 0.24f, 0.045f)), Tri(q, -0.05f, -0.54f, 0.29f, -0.54f, 0.12f, -0.88f)); };
            case "trash":
                return p => U(Sub(Box(p, 0, -0.17f, 0.44f, 0.5f, 0.1f), U(Box(p, -0.16f, -0.17f, 0.04f, 0.3f, 0.02f), Box(p, 0.16f, -0.17f, 0.04f, 0.3f, 0.02f))), Box(p, 0, 0.47f, 0.6f, 0.075f, 0.05f), Box(p, 0, 0.62f, 0.2f, 0.09f, 0.05f));
            case "undo":
                return p => U(Arc(p, 0.04f, -0.04f, 0.52f, 0.1f, -110f, 160f), Head(p, -0.45f + Mathf.Cos(250f * Mathf.Deg2Rad) * 0.14f, 0.14f + Mathf.Sin(250f * Mathf.Deg2Rad) * 0.14f, 250f, 0.34f));
            case "redo":
                return p => { var q = new Vector2(-p.x, p.y); return U(Arc(q, 0.04f, -0.04f, 0.52f, 0.1f, -110f, 160f), Head(q, -0.45f + Mathf.Cos(250f * Mathf.Deg2Rad) * 0.14f, 0.14f + Mathf.Sin(250f * Mathf.Deg2Rad) * 0.14f, 250f, 0.34f)); };
            case "plus": return p => U(Box(p, 0, 0, 0.62f, 0.12f, 0.06f), Box(p, 0, 0, 0.12f, 0.62f, 0.06f));
            case "minus": return p => Box(p, 0, 0, 0.62f, 0.12f, 0.06f);
            case "close": return p => { var q = Rot(p, 45f); return U(Box(q, 0, 0, 0.6f, 0.12f, 0.06f), Box(q, 0, 0, 0.12f, 0.6f, 0.06f)); };
            case "check": return p => U(Seg(p, -0.62f, 0.02f, -0.2f, -0.44f, 0.11f), Seg(p, -0.2f, -0.44f, 0.68f, 0.46f, 0.11f));
            case "sparkle": return p => U(Star(p, 4, 0.92f, 0.2f), Star(new Vector2(p.x - 0.58f, p.y - 0.55f), 4, 0.3f, 0.07f));
            // v9 (G): the FX instrument — sparkles (one big four-point star and two small ones, like the familiar "effects" mark)
            case "fx": return p => U(Star(new Vector2(p.x + 0.14f, p.y + 0.12f), 4, 0.78f, 0.17f), Star(new Vector2(p.x - 0.6f, p.y - 0.58f), 4, 0.3f, 0.07f), Star(new Vector2(p.x - 0.62f, p.y + 0.6f), 4, 0.2f, 0.05f));
            case "speaker": return p => U(Poly(p, new Vector2(-0.75f, -0.26f), new Vector2(-0.75f, 0.26f), new Vector2(-0.42f, 0.26f), new Vector2(0.02f, 0.64f), new Vector2(0.02f, -0.64f), new Vector2(-0.42f, -0.26f)), Arc(p, 0.02f, 0, 0.36f, 0.075f, -42f, 42f), Arc(p, 0.02f, 0, 0.64f, 0.075f, -42f, 42f));
            case "mute": return p => U(Poly(p, new Vector2(-0.75f, -0.26f), new Vector2(-0.75f, 0.26f), new Vector2(-0.42f, 0.26f), new Vector2(0.02f, 0.64f), new Vector2(0.02f, -0.64f), new Vector2(-0.42f, -0.26f)), Seg(p, 0.28f, 0.26f, 0.72f, -0.18f, 0.085f), Seg(p, 0.28f, -0.18f, 0.72f, 0.26f, 0.085f));
            case "target": return p => U(Ring(p, 0, 0, 0.56f, 0.085f), Circle(p, 0, 0, 0.17f), Seg(p, 0, 0.6f, 0, 0.9f, 0.075f), Seg(p, 0, -0.6f, 0, -0.9f, 0.075f), Seg(p, 0.6f, 0, 0.9f, 0, 0.075f), Seg(p, -0.6f, 0, -0.9f, 0, 0.075f));
            case "frame":
                return p => U(Seg(p, -0.75f, 0.75f, -0.3f, 0.75f, 0.075f), Seg(p, -0.75f, 0.75f, -0.75f, 0.3f, 0.075f), Seg(p, 0.75f, 0.75f, 0.3f, 0.75f, 0.075f), Seg(p, 0.75f, 0.75f, 0.75f, 0.3f, 0.075f),
                              Seg(p, -0.75f, -0.75f, -0.3f, -0.75f, 0.075f), Seg(p, -0.75f, -0.75f, -0.75f, -0.3f, 0.075f), Seg(p, 0.75f, -0.75f, 0.3f, -0.75f, 0.075f), Seg(p, 0.75f, -0.75f, 0.75f, -0.3f, 0.075f), Circle(p, 0, 0, 0.14f));
            case "chevronL": return p => U(Seg(p, 0.28f, 0.62f, -0.3f, 0f, 0.11f), Seg(p, -0.3f, 0f, 0.28f, -0.62f, 0.11f));
            case "chevronR": return p => U(Seg(p, -0.28f, 0.62f, 0.3f, 0f, 0.11f), Seg(p, 0.3f, 0f, -0.28f, -0.62f, 0.11f));
            case "chevronD": return p => U(Seg(p, -0.62f, 0.28f, 0f, -0.3f, 0.11f), Seg(p, 0f, -0.3f, 0.62f, 0.28f, 0.11f));
            case "chevronU": return p => ChevronU(p, 0, 0, 1f, 0.11f);
            case "gate0": return p => Circle(p, 0, 0, 0.24f);
            case "gate1": return p => Box(p, 0, 0, 0.44f, 0.17f, 0.17f);
            case "gate2": return p => Box(p, 0, 0, 0.8f, 0.17f, 0.17f);
            case "modeLoop": return Shape("loop");
            case "modePingPong": return p => U(Seg(p, -0.42f, 0, 0.42f, 0, 0.095f), Head(p, 0.8f, 0, 0f, 0.36f), Head(p, -0.8f, 0, 180f, 0.36f));
            case "modeOnce": return p => U(Seg(p, -0.72f, 0, 0.2f, 0, 0.095f), Head(p, 0.5f, 0, 0f, 0.34f), Box(p, 0.7f, 0, 0.075f, 0.48f, 0.04f));
            case "save": return p => U(Seg(p, 0, 0.72f, 0, -0.02f, 0.1f), Head(p, 0, -0.3f, -90f, 0.34f), Seg(p, -0.66f, -0.3f, -0.66f, -0.72f, 0.085f), Seg(p, -0.66f, -0.72f, 0.66f, -0.72f, 0.085f), Seg(p, 0.66f, -0.72f, 0.66f, -0.3f, 0.085f));
            case "load": return p => U(Seg(p, 0, -0.2f, 0, 0.44f, 0.1f), Head(p, 0, 0.74f, 90f, 0.34f), Seg(p, -0.66f, -0.3f, -0.66f, -0.72f, 0.085f), Seg(p, -0.66f, -0.72f, 0.66f, -0.72f, 0.085f), Seg(p, 0.66f, -0.72f, 0.66f, -0.3f, 0.085f));
            case "dice": return p => Sub(Box(p, 0, 0, 0.66f, 0.66f, 0.17f), U(Circle(p, -0.32f, 0.32f, 0.11f), Circle(p, 0.32f, 0.32f, 0.11f), Circle(p, 0, 0, 0.11f), Circle(p, -0.32f, -0.32f, 0.11f), Circle(p, 0.32f, -0.32f, 0.11f)));
            case "wave":
                return p =>
                {
                    float d = 9f; int segs = 26;
                    for (int i = 0; i < segs; i++)
                    {
                        float x0 = -0.8f + 1.6f * i / segs, x1 = -0.8f + 1.6f * (i + 1) / segs;
                        d = U(d, Seg(p, x0, 0.42f * Mathf.Sin(x0 * 4.9f), x1, 0.42f * Mathf.Sin(x1 * 4.9f), 0.085f));
                    }
                    return d;
                };
            case "grid":
                return p =>
                {
                    float d = 9f;
                    for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) d = U(d, Box(p, i * 0.56f, j * 0.56f, 0.19f, 0.19f, 0.05f));
                    return d;
                };
            case "rest": return p => Sub(Ring(p, 0, 0, 0.52f, 0.085f), U(Seg(p, -0.9f, -0.9f, 0.9f, 0.9f, 0.12f), Seg(p, -0.9f, 0.9f, 0.9f, -0.9f, 0.12f)));
            case "warning": return p => Sub(Tri(p, -0.78f, -0.62f, 0.78f, -0.62f, 0f, 0.74f) - 0.06f, U(Seg(p, 0, 0.32f, 0, -0.12f, 0.075f), Circle(p, 0, -0.38f, 0.085f)));
            case "note": return p => U(Circle(p, -0.22f, -0.42f, 0.3f), Seg(p, 0.05f, -0.4f, 0.05f, 0.64f, 0.075f), Seg(p, 0.05f, 0.64f, 0.5f, 0.3f, 0.09f));
            case "cube":
                return p =>
                {
                    var hex = new Vector2[6];
                    for (int i = 0; i < 6; i++) { float a = (30f + 60f * i) * Mathf.Deg2Rad; hex[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.82f; }
                    var inner = new Vector2[6]; for (int i = 0; i < 6; i++) inner[i] = hex[i] * 0.82f;
                    float d = Sub(Poly(p, hex), Poly(p, inner));
                    d = U(d, Seg(p, 0, 0, hex[1].x, hex[1].y, 0.07f), Seg(p, 0, 0, hex[3].x, hex[3].y, 0.07f), Seg(p, 0, 0, hex[5].x, hex[5].y, 0.07f));
                    return d;
                };
            // ---- v3 glyphs (SPEC v3 §1)
            case "present": return p => U(Box(p, -0.52f, 0.34f, 0.34f, 0.1f, 0.05f), Box(p, 0.02f, -0.04f, 0.34f, 0.1f, 0.05f), Box(p, 0.56f, -0.42f, 0.34f, 0.1f, 0.05f), Box(p, -0.52f, 0.64f, 0.15f, 0.15f, 0.04f), Circle(p, 0.06f, 0.3f, 0.08f));
            case "scissors": return p => U(Ring(p, -0.36f, -0.52f, 0.2f, 0.07f), Ring(p, 0.36f, -0.52f, 0.2f, 0.07f), Seg(p, -0.22f, -0.34f, 0.42f, 0.78f, 0.075f), Seg(p, 0.22f, -0.34f, -0.42f, 0.78f, 0.075f));
            case "island": return p => U(Box(p, 0f, -0.42f, 0.86f, 0.16f, 0.08f), Box(p, -0.5f, -0.06f, 0.2f, 0.12f, 0.04f), Box(p, 0f, -0.06f, 0.2f, 0.12f, 0.04f), Box(p, 0.5f, -0.06f, 0.2f, 0.12f, 0.04f), Box(p, -0.5f, 0.34f, 0.2f, 0.12f, 0.04f), Box(p, 0f, 0.34f, 0.2f, 0.12f, 0.04f), Box(p, 0.5f, 0.34f, 0.2f, 0.12f, 0.04f));
            case "reverse": return p => U(Seg(p, -0.62f, 0.3f, 0.5f, 0.3f, 0.08f), Head(p, 0.8f, 0.3f, 0f, 0.32f), Seg(p, 0.62f, -0.3f, -0.5f, -0.3f, 0.08f), Head(p, -0.8f, -0.3f, 180f, 0.32f));
            case "merge": return p => U(SquareRing(p, -0.44f, 0f, 0.36f, 0.1f), SquareRing(p, 0.44f, 0f, 0.36f, 0.1f), Box(p, 0f, 0f, 0.05f, 0.5f, 0.02f));
            case "fallWave": return p => U(Box(p, -0.56f, 0.34f, 0.14f, 0.42f, 0.06f), Box(p, 0f, 0.06f, 0.14f, 0.42f, 0.06f), Box(p, 0.56f, -0.22f, 0.14f, 0.42f, 0.06f));
            case "fallRows": return p => U(Box(p, -0.12f, 0.52f, 0.52f, 0.12f, 0.06f), Box(p, 0.02f, 0.02f, 0.52f, 0.12f, 0.06f), Box(p, 0.16f, -0.48f, 0.52f, 0.12f, 0.06f));
            case "fallSpiral": return p => U(Arc(p, 0f, 0f, 0.68f, 0.085f, 0f, 270f), Arc(p, 0f, 0f, 0.4f, 0.085f, 90f, 360f), Circle(p, 0f, 0f, 0.12f));
            case "fallRain": return p => U(Seg(p, -0.56f, 0.62f, -0.56f, 0.26f, 0.075f), Seg(p, 0f, 0.34f, 0f, -0.02f, 0.075f), Seg(p, 0.52f, 0.78f, 0.52f, 0.42f, 0.075f), Seg(p, -0.3f, -0.2f, -0.3f, -0.56f, 0.075f), Seg(p, 0.34f, -0.12f, 0.34f, -0.48f, 0.075f));
            case "fallBloom": return p => U(Circle(p, 0f, 0f, 0.16f), Ring(p, 0f, 0f, 0.46f, 0.07f), Ring(p, 0f, 0f, 0.8f, 0.05f));
            case "home": return p => U(Tri(p, -0.85f, 0.08f, 0.85f, 0.08f, 0f, 0.82f), Sub(Box(p, 0, -0.32f, 0.52f, 0.42f, 0.04f), Box(p, 0, -0.5f, 0.13f, 0.26f, 0.03f)));
            case "duplicate": return p => U(Sub(Box(p, 0.18f, -0.18f, 0.5f, 0.5f, 0.1f), Box(p, 0.18f, -0.18f, 0.36f, 0.36f, 0.05f)), Box(p, -0.18f, 0.18f, 0.5f, 0.5f, 0.1f));
            case "triUp": return p => Tri(p, -0.72f, -0.52f, 0.72f, -0.52f, 0f, 0.7f) - 0.06f;
            case "triDown": return p => Tri(p, -0.72f, 0.52f, 0f, -0.7f, 0.72f, 0.52f) - 0.06f;
            case "diamond": return p => { var q = Rot(p, 45f); return Box(q, 0, 0, 0.55f, 0.55f, 0.08f); };
            case "half": return p => Sub(Circle(p, 0, 0, 0.72f), Box(p, 0.5f, 0, 0.5f, 1f));
            case "eye": return p => { float lens = Mathf.Max(Circle(p, 0, -0.62f, 1.18f), Circle(p, 0, 0.62f, 1.18f)); return U(Mathf.Abs(lens) - 0.07f, Circle(p, 0, 0, 0.26f)); };
            case "tap": return p => U(Ring(p, 0, 0.1f, 0.45f, 0.07f), Seg(p, 0, 0.1f, 0, -0.7f, 0.11f), Seg(p, 0, -0.7f, 0.42f, -0.55f, 0.1f));
            case "sun": return p => { float d = Circle(p, 0, 0, 0.36f); for (int i = 0; i < 8; i++) { float a = i * 45f * Mathf.Deg2Rad; d = U(d, Seg(p, Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.55f, Mathf.Cos(a) * 0.85f, Mathf.Sin(a) * 0.85f, 0.075f)); } return d; };
            case "layers": return p => U(Box(p, 0, 0.45f, 0.62f, 0.14f, 0.1f), Box(p, 0, 0f, 0.62f, 0.14f, 0.1f), Box(p, 0, -0.45f, 0.62f, 0.14f, 0.1f));
            case "arrowR": return p => U(Seg(p, -0.7f, 0, 0.25f, 0, 0.1f), Head(p, 0.72f, 0, 0f, 0.42f));
            case "squareRing": return p => Sub(Box(p, 0, 0, 0.94f, 0.94f, 0.32f), Box(p, 0, 0, 0.78f, 0.78f, 0.22f));
            case "triUpDot": return p => U(Tri(p, -0.72f, -0.66f, 0.72f, -0.66f, 0f, 0.36f) - 0.06f, Circle(p, 0, 0.72f, 0.14f));
            case "triDownDot": return p => U(Tri(p, -0.72f, 0.66f, 0f, -0.36f, 0.72f, 0.66f) - 0.06f, Circle(p, 0, -0.72f, 0.14f));
            case "help": return p => U(Arc(p, 0, 0.22f, 0.36f, 0.09f, -30f, 200f), Seg(p, 0, -0.04f, 0, -0.3f, 0.09f), Circle(p, 0, -0.62f, 0.11f));

            // ---------------------------------------------------------- v2: instruments (palette silhouettes)
            case "keys":   // keyboard: white keys with three black keys notched from the top edge
                return p =>
                {
                    float d = Box(p, 0, 0, 0.82f, 0.56f, 0.1f);
                    for (int i = -1; i <= 1; i++)
                    {
                        float x = i * 0.41f;
                        d = Sub(d, Box(p, x, 0.3f, 0.085f, 0.34f, 0.02f));
                        d = Sub(d, Box(p, x, -0.32f, 0.03f, 0.28f));
                    }
                    return d;
                };
            case "pluck":  // banjo: round body with a sound hole, neck to the top-right, headstock
                return p =>
                {
                    var s = new Vector2(p.x + 0.08f, p.y + 0.08f);
                    var q = Rot(s, 45f);
                    return U(Sub(Circle(s, -0.24f, -0.24f, 0.5f), Circle(s, -0.24f, -0.24f, 0.16f)), Seg(s, 0.1f, 0.1f, 0.66f, 0.66f, 0.1f), Box(q, 0f, 1.02f, 0.14f, 0.17f, 0.05f));
                };
            case "bolt": return p => Bolt(p, 1f, 0, 0, 0.02f);
            case "bass": return p => U(Ring(p, 0, 0, 0.78f, 0.1f), Ring(p, 0, 0, 0.42f, 0.1f), Circle(p, 0, 0, 0.14f));   // woofer rings
            case "bell": return p => U(Circle(p, 0, 0.24f, 0.42f), Box(p, 0, -0.08f, 0.42f, 0.34f), Box(p, 0, -0.46f, 0.66f, 0.1f, 0.07f), Circle(p, 0, -0.72f, 0.14f), Circle(p, 0, 0.72f, 0.1f));
            case "strings":   // violin: figure-eight body, neck and scroll
                return p => U(Circle(p, 0, -0.38f, 0.44f), Circle(p, 0, 0.14f, 0.34f), Seg(p, 0, 0.3f, 0, 0.74f, 0.08f), Circle(p, 0, 0.78f, 0.11f));
            case "choir":     // three singers: heads over rounded shoulders
                return p => U(Circle(p, -0.52f, 0.3f, 0.18f), Circle(p, 0f, 0.46f, 0.2f), Circle(p, 0.52f, 0.3f, 0.18f),
                              Box(p, -0.52f, -0.32f, 0.32f, 0.34f, 0.28f), Box(p, 0f, -0.26f, 0.36f, 0.4f, 0.32f), Box(p, 0.52f, -0.32f, 0.32f, 0.34f, 0.28f));
            case "piano":     // grand piano from above with the keyboard strip and black-key marks
                return p =>
                {
                    float body = Poly(p, new Vector2(-0.8f, -0.66f), new Vector2(0.8f, -0.66f), new Vector2(0.8f, -0.12f), new Vector2(0.62f, 0.3f), new Vector2(0.3f, 0.62f), new Vector2(-0.2f, 0.74f), new Vector2(-0.8f, 0.74f)) - 0.03f;
                    body = Sub(body, Box(p, 0, -0.42f, 0.7f, 0.04f));
                    for (int i = 0; i < 5; i++) { float x = -0.46f + i * 0.2f + (i > 1 ? 0.12f : 0f); body = Sub(body, Box(p, x, -0.5f, 0.045f, 0.09f)); }
                    return body;
                };
            case "kit":       // drum with crossed sticks
                return p =>
                {
                    var q = new Vector2(p.x, p.y + 0.05f);
                    return U(Sub(Box(q, 0, -0.4f, 0.7f, 0.36f, 0.14f), Box(q, 0, -0.16f, 0.58f, 0.035f)),
                             Seg(q, -0.62f, 0.8f, 0.3f, -0.02f, 0.08f), Seg(q, 0.62f, 0.8f, -0.3f, -0.02f, 0.08f),
                             Circle(q, -0.62f, 0.8f, 0.11f), Circle(q, 0.62f, 0.8f, 0.11f));
                };

            // ---------------------------------------------------------- v2: weather, sections
            case "cloud": return p => Cloud(p, 1f, 0, 0);
            case "storm": return p => U(Cloud(p, 0.85f, 0, 0.28f), Bolt(p, 0.42f, 0.05f, -0.49f, 0.03f));
            case "climateAuto": return p => Ring(p, 0, 0, 0.86f, 0.07f);
            case "climateSun": return p => U(Ring(p, 0, 0, 0.86f, 0.07f), Sun(p, 0.24f, 0.36f, 0.56f, 0.065f));
            case "climateCloud": return p => U(Ring(p, 0, 0, 0.86f, 0.07f), Cloud(p, 0.64f, 0, 0));
            case "climateStorm": return p => U(Ring(p, 0, 0, 0.86f, 0.07f), Cloud(p, 0.56f, 0, 0.19f), Bolt(p, 0.28f, 0.03f, -0.32f, 0.03f));
            case "flame":     // teardrop with a bite on the right and a second tongue on the left
                return p => { var q = new Vector2(p.x - 0.06f, p.y); return Sub(U(Circle(q, 0, -0.34f, 0.5f), Tri(q, -0.5f, -0.34f, 0.5f, -0.34f, 0.12f, 0.86f), Tri(q, -0.62f, -0.1f, -0.2f, -0.1f, -0.52f, 0.4f) - 0.04f), Circle(q, 0.54f, 0.2f, 0.32f)); };
            case "flourish":  // 270-degree curl ending in a sparkle
                return p => U(Arc(p, 0.14f, -0.12f, 0.5f, 0.1f, -90f, 180f), Star(new Vector2(p.x + 0.5f, p.y - 0.52f), 4, 0.3f, 0.09f));
            case "moon": return p => Sub(Circle(p, 0.1f, 0, 0.78f), Circle(p, 0.46f, 0.2f, 0.68f));
            case "repeat2": return p => U(Loop(p), Box(p, -0.17f, 0, 0.1f, 0.1f, 0.02f), Box(p, 0.17f, 0, 0.1f, 0.1f, 0.02f));
            case "repeat4": return p => U(Loop(p), Box(p, -0.17f, 0.17f, 0.1f, 0.1f, 0.02f), Box(p, 0.17f, 0.17f, 0.1f, 0.1f, 0.02f), Box(p, -0.17f, -0.17f, 0.1f, 0.1f, 0.02f), Box(p, 0.17f, -0.17f, 0.1f, 0.1f, 0.02f));

            // ---------------------------------------------------------- v2: stickers (node ring)
            case "ghost": return p => Ring(p, 0, 0, 0.42f, 0.08f);
            case "accent":    // bold tapered ">" (the accent mark)
                return p => Poly(p, new Vector2(-0.7f, 0.66f), new Vector2(0.74f, 0f), new Vector2(-0.7f, -0.66f), new Vector2(-0.7f, -0.4f), new Vector2(0.24f, 0f), new Vector2(-0.7f, 0.4f)) - 0.02f;
            case "ratchet2":  // tremolo slashes
                return p => U(Seg(p, -0.52f, -0.5f, 0.52f, -0.16f, 0.11f), Seg(p, -0.52f, 0.16f, 0.52f, 0.5f, 0.11f));
            case "ratchet3":
                return p => U(Seg(p, -0.52f, -0.64f, 0.52f, -0.32f, 0.1f), Seg(p, -0.52f, -0.16f, 0.52f, 0.16f, 0.1f), Seg(p, -0.52f, 0.32f, 0.52f, 0.64f, 0.1f));
            case "coin": return p => U(Ring(p, 0, 0, 0.7f, 0.08f), Sub(Circle(p, 0, 0, 0.66f), Box(p, 0.5f, 0, 0.5f, 1f)));   // half filled
            case "tie": return p => U(Arc(p, 0, -0.38f, 0.85f, 0.09f, 24f, 156f), Circle(p, -0.5f, -0.42f, 0.17f), Circle(p, 0.5f, -0.42f, 0.17f));   // slur over two notes
            case "lift": return p => U(Circle(p, 0, -0.46f, 0.22f), ChevronU(p, 0, 0.24f, 0.97f, 0.11f));

            // ---------------------------------------------------------- v2: cube modifiers
            case "octUp": return p => U(SquareRing(p, 0, -0.3f, 0.44f, 0.14f), Seg(p, -0.36f, 0.36f, 0f, 0.68f, 0.11f), Seg(p, 0f, 0.68f, 0.36f, 0.36f, 0.11f));
            case "octDown": return p => U(SquareRing(p, 0, 0.3f, 0.44f, 0.14f), Seg(p, -0.36f, -0.36f, 0f, -0.68f, 0.11f), Seg(p, 0f, -0.68f, 0.36f, -0.36f, 0.11f));
            case "twin": return p => U(SquareRing(p, -0.34f, 0.22f, 0.42f, 0.13f), SquareRing(p, 0.1f, -0.22f, 0.42f, 0.13f), Head(p, 0.78f, -0.22f, 0f, 0.3f));
            case "shadowMod":   // solid outline below, dashed outline above (the harmony follower)
                return p => U(SquareRing(p, 0, -0.44f, 0.4f, 0.13f),
                              Sub(SquareRing(p, 0, 0.44f, 0.4f, 0.13f), U(Box(p, 0, 0.44f, 0.1f, 0.5f), Box(p, 0, 0.44f, 0.5f, 0.1f))));
            case "echo": return p => U(Circle(p, -0.6f, 0, 0.28f), Circle(p, -0.02f, 0, 0.2f), Circle(p, 0.4f, 0, 0.14f), Circle(p, 0.72f, 0, 0.1f));
            case "comet": return p => U(Circle(p, 0.34f, 0.3f, 0.36f), Tri(p, 0.55f, 0.08f, 0.13f, 0.52f, -0.8f, -0.78f));
            case "stamp":     // knob, stem, base block and the wavy imprint
                return p => U(Box(p, 0, 0.65f, 0.27f, 0.18f, 0.13f), Box(p, 0, 0.27f, 0.13f, 0.22f), Box(p, 0, -0.11f, 0.64f, 0.2f, 0.07f),
                              Arc(p, -0.32f, -0.63f, 0.16f, 0.06f, 0f, 180f), Arc(p, 0f, -0.63f, 0.16f, 0.06f, 180f, 360f), Arc(p, 0.32f, -0.63f, 0.16f, 0.06f, 0f, 180f));

            // ---------------------------------------------------------- v2: pencil ring shapes, record
            case "stairsUp": return p => U(Box(p, -0.53f, -0.53f, 0.27f, 0.27f), Box(p, 0f, -0.27f, 0.27f, 0.53f), Box(p, 0.53f, 0f, 0.27f, 0.8f));
            case "stairsDown": return p => U(Box(p, 0.53f, -0.53f, 0.27f, 0.27f), Box(p, 0f, -0.27f, 0.27f, 0.53f), Box(p, -0.53f, 0f, 0.27f, 0.8f));
            case "zigzag": return p => U(Seg(p, -0.76f, -0.5f, -0.38f, 0.5f, 0.1f), Seg(p, -0.38f, 0.5f, 0f, -0.5f, 0.1f), Seg(p, 0f, -0.5f, 0.38f, 0.5f, 0.1f), Seg(p, 0.38f, 0.5f, 0.76f, -0.5f, 0.1f));
            case "mountain": return p => U(Tri(p, -0.82f, -0.58f, 0.32f, -0.58f, -0.25f, 0.7f), Tri(p, 0f, -0.58f, 0.82f, -0.58f, 0.42f, 0.14f)) - 0.03f;
            case "record": return p => U(Ring(p, 0, 0, 0.74f, 0.09f), Circle(p, 0, 0, 0.3f));

            // ---------------------------------------------------------- v2: transport, dials, wheel
            case "eclipse": return p => U(Ring(p, -0.17f, 0, 0.6f, 0.09f), Circle(p, 0.3f, 0, 0.56f));
            case "waves3": return p => U(Circle(p, -0.42f, 0, 0.13f), Arc(p, -0.42f, 0, 0.36f, 0.08f, -55f, 55f), Arc(p, -0.42f, 0, 0.62f, 0.08f, -55f, 55f), Arc(p, -0.42f, 0, 0.88f, 0.08f, -55f, 55f));
            case "funnel": return p => U(Poly(p, new Vector2(-0.8f, 0.76f), new Vector2(0.8f, 0.76f), new Vector2(0.16f, 0f), new Vector2(-0.16f, 0f)), Box(p, 0, -0.38f, 0.15f, 0.4f, 0.05f));
            case "diamondDot": return p => { var q = Rot(new Vector2(p.x, p.y + 0.17f), 45f); return U(Box(q, 0, 0, 0.45f, 0.45f, 0.06f), Circle(p, 0, 0.69f, 0.13f)); };
            case "necklace":  // ring with six beads, alternately filled and hollow
                return p =>
                {
                    float d = Ring(p, 0, 0, 0.66f, 0.07f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (90f + i * 60f) * Mathf.Deg2Rad;
                        float bx = Mathf.Cos(a) * 0.66f, by = Mathf.Sin(a) * 0.66f;
                        if (i % 2 == 0) d = U(d, Circle(p, bx, by, 0.16f));
                        else d = U(Sub(d, Circle(p, bx, by, 0.16f)), Ring(p, bx, by, 0.16f, 0.06f));
                    }
                    return d;
                };
        }
        return null;
    }
}
