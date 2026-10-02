using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A drawn comic shape in one mesh from the <see cref="Comic"/> atlas: a hard offset ink shadow (no blur), an ink border, the
/// flat fill (<see cref="Graphic.color"/>), optionally the darker front face and the cream glint of a mini flat cube, and a
/// halftone overlay (the "active" state). Rounded rects (9-sliced; radius ≤ 0 = a capsule) or discs. <see cref="bodyOffset"/>
/// slides everything but the shadow (a pressed button sits on its shadow); <see cref="inkJitter"/> / <see cref="inkScale"/>
/// boil the ink ring on hover. It is an Image so every Image-typed hook of the HUD keeps working; it never sizes a layout.
/// </summary>
public class ComicShape : Image
{
    public enum Kind { Rect, Disc }
    public Kind kind = Kind.Rect;
    /// <summary>Corner radius (px) of a rect; ≤ 0 = a capsule.</summary>
    public float radius = Comic.PanelRadius;
    /// <summary>Ink border width (px); 0 = none.</summary>
    public float border = Comic.PanelBorder;
    public Color ink = Comic.Ink;
    public Color shadowColor = Comic.Shadow;
    public Vector2 shadowOffset = Comic.PanelShadow;
    /// <summary>A light (paper) surface: controls on it take the light theme.</summary>
    public bool light;
    /// <summary>Mini flat cube: the front face takes this fraction of the height, darker (0 = off).</summary>
    public float cube;
    /// <summary>Mini flat cube: alpha of the cream glint on the top face (0 = off).</summary>
    public float glint;
    /// <summary>Multiplies every layer's alpha (fade the ink with the fill).</summary>
    public float opacity = 1f;
    /// <summary>Nothing is drawn (a bare glyph button keeps its hit area).</summary>
    public bool bare;
    public Vector2 bodyOffset, inkJitter;
    public float inkScale = 1f;
    /// <summary>Halftone overlay alpha (0 = off) and colour.</summary>
    public float halftone;
    public Color halftoneColor = Comic.Ink;
    /// <summary>A sticker border drawn behind everything, <see cref="halo"/> px outside the shape (0 = off): the selected pill.</summary>
    public float halo;
    public Color haloColor = Comic.Cream;

    protected override void Awake()
    {
        base.Awake();
        if (sprite == null) sprite = Comic.AtlasSprite;
        type = Type.Simple; preserveAspect = false;
    }

    Rect drawn; bool drawnOnce;
    /// <summary>The rect the current mesh was built for (tests: a stale mesh shows as a mismatch).</summary>
    public Rect DrawnRect => drawn;

    /// <summary>Self-heal: a mesh built for another rect (a layout resize that slipped past the dirty flags, e.g. while a parent
    /// CanvasGroup hid it) is rebuilt on the next frame.</summary>
    void LateUpdate()
    {
        if (!drawnOnce || rectTransform.rect != drawn) SetVerticesDirty();
    }

    public override float preferredWidth => 0f;
    public override float preferredHeight => 0f;
    public override float flexibleWidth => -1f;
    public override float flexibleHeight => -1f;

    /// <summary>Sets the per-frame look; the mesh is rebuilt only when something changed.</summary>
    public void SetLook(Color fill, bool isBare, float halftoneAlpha, Vector2 body, Vector2 jitter, float jitterScale)
    {
        bool dirty = isBare != bare || halftoneAlpha != halftone || body != bodyOffset || jitter != inkJitter || jitterScale != inkScale;
        bare = isBare; halftone = halftoneAlpha; bodyOffset = body; inkJitter = jitter; inkScale = jitterScale;
        if (color != fill) color = fill;   // dirties itself
        else if (dirty) SetVerticesDirty();
    }
    public void SetOpacity(float a) { if (Mathf.Abs(a - opacity) > 0.002f) { opacity = a; SetVerticesDirty(); } }
    public void SetInk(Color c) { if (c != ink) { ink = c; SetVerticesDirty(); } }
    public void SetShadow(Color c, Vector2 offset) { if (c != shadowColor || offset != shadowOffset) { shadowColor = c; shadowOffset = offset; SetVerticesDirty(); } }
    public void SetBorder(float w) { if (w != border) { border = w; SetVerticesDirty(); } }
    public void SetHalo(float px, Color c) { if (Mathf.Abs(px - halo) > 0.01f || c != haloColor) { halo = px; haloColor = c; SetVerticesDirty(); } }

    float RadiusOf(Rect r) => kind == Kind.Disc || radius <= 0f ? Mathf.Min(r.width, r.height) * 0.5f : Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        drawn = rectTransform.rect; drawnOnce = true;
        if (bare || opacity <= 0f) return;
        if (sprite == null || sprite.texture != Comic.Atlas) sprite = Comic.AtlasSprite;
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f) return;
        float rad = RadiusOf(r);
        if (halo > 0f && haloColor.a > 0f) { Rect hr = Inset(r, -halo); Draw(vh, hr, kind == Kind.Disc ? 0f : RadiusOf(hr), Fade(haloColor)); }
        if (shadowColor.a > 0f && shadowOffset != Vector2.zero) Draw(vh, Move(r, shadowOffset), rad, Fade(shadowColor));
        Rect body = Move(r, bodyOffset);
        if (border > 0f && ink.a > 0f)
        {
            Rect ir = Move(body, inkJitter);
            if (inkScale != 1f) ir = Scale(ir, inkScale);
            Draw(vh, ir, RadiusOf(ir), Fade(ink));
        }
        Rect f = Inset(body, border);
        if (f.width <= 0.5f || f.height <= 0.5f) return;
        float fr = kind == Kind.Disc ? 0f : Mathf.Max(0.5f, rad - border);
        Color fill = color;
        if (fill.a > 0f)
        {
            if (cube > 0f && kind == Kind.Rect)
            {
                Color front = new Color(fill.r * 0.66f, fill.g * 0.62f, fill.b * 0.74f, fill.a);
                Draw(vh, f, fr, Fade(front));
                Rect top = f; top.yMin += f.height * cube;
                Draw(vh, top, Mathf.Min(fr, top.height * 0.5f), Fade(fill));
            }
            else Draw(vh, f, fr, Fade(fill));
        }
        if (halftone > 0f)
        {
            Color hc = new Color(halftoneColor.r, halftoneColor.g, halftoneColor.b, halftoneColor.a * halftone);
            if (kind == Kind.Disc) Quad(vh, Expand(f, Comic.DiscUV / Comic.DiscR), Comic.HalftoneDisc, 2f, Fade(hc));
            else { Rect h = Inset(f, fr * 0.32f + 1f); if (h.width > 1f && h.height > 1f) Quad(vh, h, Comic.HalftoneSquare, 4f, Fade(hc)); }
        }
        if (glint > 0f && kind == Kind.Rect)
        {
            float top = cube > 0f ? f.yMin + f.height * cube : f.yMin;
            float h = Mathf.Max(2f, (f.yMax - top) * 0.13f);
            var g = new Rect(f.xMin + f.width * 0.15f, f.yMax - (f.yMax - top) * 0.13f - h, f.width * 0.34f, h);
            Draw(vh, g, h * 0.5f, Fade(new Color(Comic.Cream.r, Comic.Cream.g, Comic.Cream.b, glint * fill.a)));
        }
    }

    Color Fade(Color c) => opacity >= 1f ? c : new Color(c.r, c.g, c.b, c.a * opacity);
    static Rect Move(Rect r, Vector2 d) => new Rect(r.x + d.x, r.y + d.y, r.width, r.height);
    static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2f * d, r.height - 2f * d);
    static Rect Scale(Rect r, float s) { var c = r.center; float w = r.width * s, h = r.height * s; return new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h); }
    static Rect Expand(Rect r, float k) => Scale(r, k);

    void Draw(VertexHelper vh, Rect r, float rad, Color c)
    {
        if (kind == Kind.Disc) Quad(vh, Expand(r, Comic.DiscUV / Comic.DiscR), Comic.Disc, 2f, c);
        else Sliced(vh, r, rad, c);
    }

    /// <summary>A quad over atlas cell <paramref name="cell"/> (inset <paramref name="inset"/> texels).</summary>
    static void Quad(VertexHelper vh, Rect r, RectInt cell, float inset, Color c)
    {
        float u0 = (cell.x + inset) / Comic.AtlasW, u1 = (cell.xMax - inset) / Comic.AtlasW;
        float v0 = (cell.y + inset) / Comic.AtlasH, v1 = (cell.yMax - inset) / Comic.AtlasH;
        Color32 c32 = c;
        int i = vh.currentVertCount;
        vh.AddVert(new Vector3(r.xMin, r.yMin), c32, new Vector4(u0, v0));
        vh.AddVert(new Vector3(r.xMin, r.yMax), c32, new Vector4(u0, v1));
        vh.AddVert(new Vector3(r.xMax, r.yMax), c32, new Vector4(u1, v1));
        vh.AddVert(new Vector3(r.xMax, r.yMin), c32, new Vector4(u1, v0));
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i + 2, i + 3, i);
    }

    static readonly float[] xs = new float[4], ys = new float[4], us = new float[4], vs = new float[4];

    /// <summary>A rounded rect of corner radius <paramref name="rad"/> px from the 9-slice cell (the AA ramp sits on the rect edge).</summary>
    static void Sliced(VertexHelper vh, Rect r, float rad, Color c)
    {
        var cell = Comic.RR;
        float s = Mathf.Max(0.25f, rad) / Comic.RRRadius;            // px per texel
        float e = Comic.RROuter * s;                                  // the quad starts RROuter texels outside the box edge
        Rect o = new Rect(r.xMin - e, r.yMin - e, r.width + 2f * e, r.height + 2f * e);
        float bx = Comic.RRBorder * s, by = bx;
        if (2f * bx > o.width) bx = o.width * 0.5f;
        if (2f * by > o.height) by = o.height * 0.5f;
        xs[0] = o.xMin; xs[1] = o.xMin + bx; xs[2] = o.xMax - bx; xs[3] = o.xMax;
        ys[0] = o.yMin; ys[1] = o.yMin + by; ys[2] = o.yMax - by; ys[3] = o.yMax;
        float lo = 4f - Comic.RROuter, hi = 124f + Comic.RROuter;     // the box spans texels 4..124 of the cell
        us[0] = (cell.x + lo) / Comic.AtlasW; us[1] = (cell.x + lo + Comic.RRBorder) / Comic.AtlasW;
        us[2] = (cell.x + hi - Comic.RRBorder) / Comic.AtlasW; us[3] = (cell.x + hi) / Comic.AtlasW;
        vs[0] = (cell.y + lo) / Comic.AtlasH; vs[1] = (cell.y + lo + Comic.RRBorder) / Comic.AtlasH;
        vs[2] = (cell.y + hi - Comic.RRBorder) / Comic.AtlasH; vs[3] = (cell.y + hi) / Comic.AtlasH;
        Color32 c32 = c;
        int start = vh.currentVertCount;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                vh.AddVert(new Vector3(xs[x], ys[y]), c32, new Vector4(us[x], vs[y]));
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                int i = start + y * 4 + x;
                vh.AddTriangle(i, i + 4, i + 5); vh.AddTriangle(i + 5, i + 1, i);
            }
    }
}
