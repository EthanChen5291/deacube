using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural sprites for the menu and the tutorial bubbles (SPEC v3 §5): a sliced rounded outline (target pulse, button
/// borders), a sliced rounded fill, the bubble tail, the vignette hole and a soft radial glow. Built once, cached.
/// </summary>
public static class MenuSprites
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    /// <summary>Rounded-rect outline, 3 px stroke, 20 px corner radius, 9-sliced (border 26).</summary>
    public static Sprite Outline => Get("mOutline", 64, 26, (x, y, n) => Stroke(RoundRect(x, y, n, 20f), 3f));
    /// <summary>Thin rounded-rect outline (1.6 px) for button borders, 9-sliced.</summary>
    public static Sprite OutlineThin => Get("mOutlineThin", 64, 26, (x, y, n) => Stroke(RoundRect(x, y, n, 20f), 1.6f));
    /// <summary>Rounded-rect fill, 20 px radius, 9-sliced.</summary>
    public static Sprite Fill => Get("mFill", 64, 26, (x, y, n) => Cover(RoundRect(x, y, n, 20f)));
    /// <summary>Bubble tail: a triangle with its base on the bottom edge and the tip at the top centre (pivot the image at the base).</summary>
    public static Sprite Tail => Get("mTail", 64, 0, (x, y, n) =>
    {
        float u = (x + 0.5f) / n, v = (y + 0.5f) / n;        // 0..1
        float half = 0.5f * (1f - v);                        // straight sides, rounded a touch at the tip
        float d = (Mathf.Abs(u - 0.5f) - half) * n;          // horizontal distance to a side, px
        return Mathf.Clamp01(0.5f - d) * Mathf.Clamp01((1f - v) * n * 0.5f + 0.5f);
    });
    /// <summary>Vignette with a soft hole: alpha 0 inside r = 0.34, 1 beyond r = 1 (r = distance / half size); corners are 1.</summary>
    public static Sprite VignetteHole => Get("mVignette", 256, 0, (x, y, n) =>
    {
        float cx = (x + 0.5f) / n * 2f - 1f, cy = (y + 0.5f) / n * 2f - 1f;
        float r = Mathf.Sqrt(cx * cx + cy * cy);
        float t = Mathf.Clamp01((r - HoleInner) / (1f - HoleInner));
        return t * t * (3f - 2f * t);
    });
    public const float HoleInner = 0.34f;
    /// <summary>Soft radial glow (bright centre, smooth falloff to the edge).</summary>
    public static Sprite Glow => Get("mGlow", 128, 0, (x, y, n) =>
    {
        float cx = (x + 0.5f) / n * 2f - 1f, cy = (y + 0.5f) / n * 2f - 1f;
        float r = Mathf.Clamp01(Mathf.Sqrt(cx * cx + cy * cy));
        return Mathf.Pow(1f - r, 2.2f);
    });
    /// <summary>Horizontal soft bar (fades out left and right): underline accents.</summary>
    public static Sprite SoftBar => Get("mSoftBar", 128, 0, (x, y, n) =>
    {
        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
        return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 1.5f) * Mathf.Clamp01(1f - Mathf.Abs(v) * 1.4f);
    });

    // ------------------------------------------------------------------ raster
    static Sprite Get(string key, int n, float border, Func<int, int, int, float> alpha)
    {
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = key };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y, n)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        s.name = key;
        cache[key] = s;
        return s;
    }

    /// <summary>Signed distance (px) from pixel (x, y) to a rounded rect inset 1 px in an n x n texture.</summary>
    static float RoundRect(int x, int y, int n, float r)
    {
        float h = n * 0.5f - 1f;
        float px = Mathf.Abs(x + 0.5f - n * 0.5f), py = Mathf.Abs(y + 0.5f - n * 0.5f);
        float qx = px - (h - r), qy = py - (h - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }
    static float Cover(float d) => Mathf.Clamp01(0.5f - d);
    static float Stroke(float d, float w) => Mathf.Clamp01(0.5f - (Mathf.Abs(d + w * 0.5f) - w * 0.5f));
}
