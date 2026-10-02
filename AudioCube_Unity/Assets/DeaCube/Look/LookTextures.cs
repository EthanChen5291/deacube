using UnityEngine;

/// <summary>
/// SPEC v3 §7 (package E): procedural textures of the look, built once on first use (no assets): the paper-fibre page
/// (LookScreen) and the flat confetti sheet with ink rims (Fx.Burst).
/// </summary>
public static class LookTextures
{
    static Texture2D paper, confetti;

    /// <summary>256² tileable paper: grey 0.5 = neutral; soft mottling, a fine tooth and a few hundred short fibres (linear R8).</summary>
    public static Texture2D Paper { get { if (paper == null) paper = BuildPaper(256); return paper; } }

    /// <summary>128² sheet of 2x2 confetti shapes (strip, triangle, disc, four-point star): white fill (tinted by the particle
    /// colour) inside a dark violet ink rim; alpha = the shape.</summary>
    public static Texture2D Confetti { get { if (confetti == null) confetti = BuildConfetti(); return confetti; } }

    static Texture2D BuildPaper(int n)
    {
        var rnd = new System.Random(4242);
        var v = new float[n * n];
        for (int i = 0; i < v.Length; i++) v[i] = 0.5f;
        // mottling: two octaves of tileable value noise
        AddNoise(v, n, 8, 0.045f, rnd);
        AddNoise(v, n, 32, 0.03f, rnd);
        // tooth
        for (int i = 0; i < v.Length; i++) v[i] += ((float)rnd.NextDouble() - 0.5f) * 0.05f;
        // fibres: short, slightly curved strokes, mostly a little darker, some lighter
        for (int f = 0; f < 420; f++)
        {
            float x = (float)rnd.NextDouble() * n, y = (float)rnd.NextDouble() * n;
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, bend = ((float)rnd.NextDouble() - 0.5f) * 0.12f;
            int len = 6 + rnd.Next(18);
            float amt = (rnd.NextDouble() < 0.72 ? -1f : 1f) * (0.05f + 0.09f * (float)rnd.NextDouble());
            for (int k = 0; k < len; k++)
            {
                a += bend;
                x += Mathf.Cos(a); y += Mathf.Sin(a);
                float fade = Mathf.Sin(Mathf.PI * (k + 0.5f) / len);
                Splat(v, n, x, y, amt * fade);
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.R8, false, true) { name = "LookPaper", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[n * n];
        for (int i = 0; i < px.Length; i++) { byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v[i]) * 255f); px[i] = new Color32(b, b, b, 255); }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static void AddNoise(float[] v, int n, int cells, float amp, System.Random rnd)
    {
        var g = new float[cells * cells];
        for (int i = 0; i < g.Length; i++) g[i] = (float)rnd.NextDouble() * 2f - 1f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float fx = x / (float)n * cells, fy = y / (float)n * cells;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells; x0 %= cells; y0 %= cells;
                float a = Mathf.Lerp(g[y0 * cells + x0], g[y0 * cells + x1], tx);
                float b = Mathf.Lerp(g[y1 * cells + x0], g[y1 * cells + x1], tx);
                v[y * n + x] += Mathf.Lerp(a, b, ty) * amp;
            }
        }
    }

    static void Splat(float[] v, int n, float x, float y, float amt)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float tx = x - x0, ty = y - y0;
        Add(v, n, x0, y0, amt * (1f - tx) * (1f - ty));
        Add(v, n, x0 + 1, y0, amt * tx * (1f - ty));
        Add(v, n, x0, y0 + 1, amt * (1f - tx) * ty);
        Add(v, n, x0 + 1, y0 + 1, amt * tx * ty);
    }

    static void Add(float[] v, int n, int x, int y, float a)
    {
        x = ((x % n) + n) % n; y = ((y % n) + n) % n;
        v[y * n + x] += a;
    }

    static Texture2D BuildConfetti()
    {
        const int size = 128, tile = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "LookConfetti", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[size * size];
        Color ink = Look.InkColor;
        const float rim = 0.2f, aa = 2.2f / tile * 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int tx = x / tile, ty = y / tile;
                var p = new Vector2(((x % tile) + 0.5f) / tile * 2f - 1f, ((y % tile) + 0.5f) / tile * 2f - 1f);
                float d = Shape(tx + ty * 2, p);
                float a = Mathf.Clamp01(0.5f - d / aa);
                float inkAmt = Mathf.Clamp01(0.5f + (d + rim) / aa);
                Color c = Color.Lerp(Color.white, ink, inkAmt);
                px[y * size + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // signed distances in the tile's [-1, 1] square (< 0 inside)
    static float Shape(int k, Vector2 p)
    {
        switch (k)
        {
            case 0: return IconFactory.P.Box(p, 0f, 0f, 0.78f, 0.34f, 0.12f);
            case 1: return IconFactory.P.Tri(p, -0.8f, -0.62f, 0.8f, -0.62f, 0f, 0.76f) - 0.04f;
            case 2: return IconFactory.P.Circle(p, 0f, 0f, 0.62f);
            default: return IconFactory.P.Star(p, 4, 0.86f, 0.3f);
        }
    }
}
