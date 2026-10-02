using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md): the mini isometric cube — the one motif of the whole v4 UI (instrument chips, the note-length row, rhythm
/// strip blocks, beat cubes, playheads, the grip, the logo). Three rhombi (top lighter, left the colour, right darker with the toon shader's
/// violet shade), an ink outline heavier on the shadow side with the inner "Y", a seeded hand wobble; options: hollow (muted / rest),
/// selection hexagon ring, a small shadow ellipse, the dotted-note satellite dot orbiting on twos, and a stepped hop with squash.
/// The cube fills the smaller rect dimension times <see cref="Scale"/>.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class CubeGlyph : MaskableGraphic
{
    [SerializeField] float scale = 1f;
    [SerializeField] bool hollow, ring, groundShadow, satellite, round;
    [SerializeField] Color ink = new Color(0.106f, 0.078f, 0.165f, 1f);
    [SerializeField] int seed;

    /// <summary>Cube size as a fraction of min(rect width, height) (the hexagon's height).</summary>
    public float Scale { get => scale; set { value = Mathf.Max(0.05f, value); if (scale != value) { scale = value; SetVerticesDirty(); } } }
    /// <summary>Outline only (a muted instrument, a rest).</summary>
    public bool Hollow { get => hollow; set { if (hollow != value) { hollow = value; SetVerticesDirty(); } } }
    /// <summary>The ink hexagon selection ring.</summary>
    public bool Ring { get => ring; set { if (ring != value) { ring = value; SetVerticesDirty(); } } }
    /// <summary>A soft ink ellipse under the cube (used / placed).</summary>
    public bool GroundShadow { get => groundShadow; set { if (groundShadow != value) { groundShadow = value; SetVerticesDirty(); } } }
    /// <summary>The dotted-note satellite dot (orbits on twos).</summary>
    public bool Satellite { get => satellite; set { if (satellite != value) { satellite = value; SetVerticesDirty(); } } }
    /// <summary>SPHERE (DeaCube/SphereBody.cs): draw a ball with the two crossed seams instead of the cube (the HUD while the sphere shape is chosen).</summary>
    public bool Round { get => round; set { if (round != value) { round = value; SetVerticesDirty(); } } }
    public Color Ink { get => ink; set { if (ink != value) { ink = value; SetVerticesDirty(); } } }
    public int Seed { get => seed; set { if (seed != value) { seed = value; SetVerticesDirty(); } } }

    float hopT = 9f, hopH = 0.3f; int lastFrame = -1;
    static readonly Vector2[] HexScratch = new Vector2[6];

    /// <summary>True while a hop is in flight (tests, the HUD's beat cubes).</summary>
    public bool Hopping => hopT < 1f;

    /// <summary>A small stepped hop (0.26 s) with a squash on landing; <paramref name="height"/> is a fraction of the cube size.</summary>
    public void Hop(float height = 0.3f) { hopT = 0f; hopH = height; SetVerticesDirty(); }

    public static CubeGlyph Create(Transform parent, string name, Color c, float px)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var g = go.AddComponent<CubeGlyph>();
        g.color = c;
        ((RectTransform)go.transform).sizeDelta = new Vector2(px, px);
        return g;
    }

    void Update()
    {
        bool anim = hopT < 1f || satellite;
        if (!anim) return;
        if (hopT < 1f)
        {
            hopT += Time.unscaledDeltaTime / 0.26f;
            if (hopT >= 1f) { SetVerticesDirty(); return; }   // the landing is always drawn (it may fall inside a twos frame)
        }
        int f = Mathf.FloorToInt(Time.unscaledTime * 12f);   // on twos
        if (f != lastFrame) { lastFrame = f; SetVerticesDirty(); }
    }

    static float Hash01(int a, int b)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h >> 8) / 16777216f;
        }
    }

    int nameSeed;
    int EffectiveSeed { get { if (seed != 0) return seed; if (nameSeed == 0) { unchecked { int h = 7; foreach (char ch in gameObject.name) h = h * 31 + ch; nameSeed = h == 0 ? 1 : h; } } return nameSeed; } }

    static Color Mul(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);

    void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, col, Vector2.zero); vh.AddVert(b, col, Vector2.zero); vh.AddVert(c, col, Vector2.zero); vh.AddVert(d, col, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    /// <summary>An ink segment a→b of width w with 0.9 px feathered edges.</summary>
    void Line(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
    {
        Vector2 d = b - a; if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized;
        Color32 on = c, off = new Color(c.r, c.g, c.b, 0f);
        float h = w * 0.5f;
        int i = vh.currentVertCount;
        vh.AddVert(a + n * (h + 0.9f), off, Vector2.zero); vh.AddVert(a + n * h, on, Vector2.zero); vh.AddVert(a - n * h, on, Vector2.zero); vh.AddVert(a - n * (h + 0.9f), off, Vector2.zero);
        vh.AddVert(b + n * (h + 0.9f), off, Vector2.zero); vh.AddVert(b + n * h, on, Vector2.zero); vh.AddVert(b - n * h, on, Vector2.zero); vh.AddVert(b - n * (h + 0.9f), off, Vector2.zero);
        for (int q = 0; q < 3; q++) { vh.AddTriangle(i + q, i + q + 1, i + 5 + q); vh.AddTriangle(i + q, i + 5 + q, i + 4 + q); }
    }

    /// <summary>A dashed ink segment a→b (dash ≈ 2.2 × width, gap ≈ 1.8 × width).</summary>
    void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
    {
        float len = (b - a).magnitude; if (len < 1e-3f) return;
        float dash = Mathf.Max(1.5f, w * 2.2f), gap = Mathf.Max(1.2f, w * 1.8f);
        for (float s = 0f; s < len; s += dash + gap) Line(vh, Vector2.Lerp(a, b, s / len), Vector2.Lerp(a, b, Mathf.Min(len, s + dash) / len), w, c);
    }

    void Disc(VertexHelper vh, Vector2 c, float rx, float ry, Color fill, int n = 20)
    {
        int b = vh.currentVertCount;
        Color32 f = fill;
        vh.AddVert(c, f, Vector2.zero);
        for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2f; vh.AddVert(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), f, Vector2.zero); }
        for (int i = 0; i < n; i++) vh.AddTriangle(b, b + 1 + i, b + 1 + (i + 1) % n);
    }

    void HexRing(VertexHelper vh, Vector2 c, float r, float w, Color col, int sides, float rot)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = rot + i / (float)sides * Mathf.PI * 2f, a1 = rot + (i + 1) / (float)sides * Mathf.PI * 2f;
            Line(vh, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, w, col);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float size = Mathf.Min(rect.width, rect.height) * scale;
        if (size < 3f) return;
        float R = size * 0.5f;                       // centre-to-vertex of the hexagon
        float hx = R * 0.8660254f;
        int s = EffectiveSeed;
        // hop: a stepped sine arc with a squash at both ends
        float t = Mathf.Clamp01(hopT);
        float ts = Mathf.Floor(t * 12f) / 12f;
        float lift = hopT < 1f ? Mathf.Sin(ts * Mathf.PI) * hopH * size : 0f;
        float squash = hopT < 1f ? (ts < 0.15f || ts > 0.85f ? 0.88f : 1.04f) : 1f;
        Vector2 c = rect.center + new Vector2(0f, lift);
        float sq = Mathf.Sqrt(squash);
        // (local functions, not delegates: no allocation per rebuild — satellites and hops redraw at 12 fps)
        Vector2 P(Vector2 v) => c + new Vector2(v.x / sq, v.y * squash);
        // hand wobble: each vertex nudged by up to 3 % of R (seeded, stable)
        Vector2 J(int k) => new Vector2(Hash01(s, 40 + k) - 0.5f, Hash01(s, 60 + k) - 0.5f) * (R * 0.06f);
        Vector2 top = P(new Vector2(0f, R) + J(0)), ur = P(new Vector2(hx, R * 0.5f) + J(1)), lr = P(new Vector2(hx, -R * 0.5f) + J(2));
        Vector2 bot = P(new Vector2(0f, -R) + J(3)), ll = P(new Vector2(-hx, -R * 0.5f) + J(4)), ul = P(new Vector2(-hx, R * 0.5f) + J(5)), mid = P(J(6) * 0.5f);
        Color baseC = color;
        if (groundShadow) Disc(vh, rect.center + new Vector2(0f, -R * 1.02f), R * 0.95f, R * 0.26f, new Color(ink.r, ink.g, ink.b, 0.22f * baseC.a));
        if (ring) HexRing(vh, rect.center, R * 1.38f, Mathf.Max(1.6f, R * 0.12f), ink, round ? 22 : 6, Mathf.PI / 6f);
        if (round) { Ball(vh, c, R * 0.9f, sq, squash, baseC); DrawSatellite(vh, rect, R, baseC); return; }
        if (!hollow)
        {
            Color topC = Mul(baseC, 1.15f), leftC = baseC;
            Color rightC = Color.Lerp(Mul(baseC, 0.72f), new Color(0.36f, 0.24f, 0.62f, baseC.a), 0.14f);
            Quad(vh, top, ur, mid, ul, topC);
            Quad(vh, ul, mid, bot, ll, leftC);
            Quad(vh, mid, ur, lr, bot, rightC);
        }
        float w = Mathf.Max(1.3f, R * 0.13f), wi = w * 0.6f;
        Color ic = new Color(ink.r, ink.g, ink.b, ink.a * Mathf.Max(0.35f, baseC.a));
        if (hollow)
        {
            // hollow (muted / rest): an open wire cube — a thin ink hexagon with the colour drawn just inside it, no face fill, the
            // inner "Y" dashed and faint, so it reads as an empty cube of that colour (never a solid dark hexagon)
            float hw = Mathf.Max(1.1f, R * 0.085f);
            Color cc = new Color(baseC.r, baseC.g, baseC.b, 0.95f * baseC.a);
            var hex = HexScratch; hex[0] = top; hex[1] = ur; hex[2] = lr; hex[3] = bot; hex[4] = ll; hex[5] = ul;
            for (int k = 0; k < 6; k++) Line(vh, hex[k], hex[(k + 1) % 6], hw, ic);
            float inset = hw * 1.25f;
            for (int k = 0; k < 6; k++)
            {
                Vector2 a = Vector2.MoveTowards(hex[k], mid, inset), b = Vector2.MoveTowards(hex[(k + 1) % 6], mid, inset);
                Line(vh, a, b, Mathf.Max(1f, hw * 0.9f), cc);
            }
            Color faintInk = new Color(ink.r, ink.g, ink.b, 0.45f * ink.a * baseC.a);
            Dashed(vh, mid, ul, Mathf.Max(0.9f, hw * 0.7f), faintInk); Dashed(vh, mid, ur, Mathf.Max(0.9f, hw * 0.7f), faintInk); Dashed(vh, mid, bot, Mathf.Max(0.9f, hw * 0.7f), faintInk);
        }
        else
        {
            // outline: heavier on the right / bottom (shadow side)
            Line(vh, top, ur, w, ic); Line(vh, ur, lr, w * 1.25f, ic); Line(vh, lr, bot, w * 1.25f, ic);
            Line(vh, bot, ll, w * 1.1f, ic); Line(vh, ll, ul, w, ic); Line(vh, ul, top, w, ic);
            Line(vh, mid, ul, wi, ic); Line(vh, mid, ur, wi, ic); Line(vh, mid, bot, wi, ic);
        }
        DrawSatellite(vh, rect, R, baseC);
    }

    void DrawSatellite(VertexHelper vh, Rect rect, float R, Color baseC)
    {
        float w = Mathf.Max(1.3f, R * 0.13f);
        Color ic = new Color(ink.r, ink.g, ink.b, ink.a * Mathf.Max(0.35f, baseC.a));
        if (satellite)
        {
            float a = Mathf.Floor(Time.unscaledTime * 12f) / 12f * Mathf.PI * 1.2f;
            Vector2 sp = rect.center + new Vector2(Mathf.Cos(a) * R * 1.3f, Mathf.Sin(a) * R * 0.55f + R * 0.25f);
            float sr = Mathf.Max(2.2f, R * 0.22f);
            Disc(vh, sp, sr + w * 0.7f, sr + w * 0.7f, ic, 14);
            Disc(vh, sp, sr, sr, Mul(baseC, 1.2f), 14);
        }
    }

    /// <summary>The ball: an ink disc, the colour shaded toward the lower right, a highlight, and two crossed ink seams (front halves).</summary>
    void Ball(VertexHelper vh, Vector2 c, float r, float sq, float squash, Color baseC)
    {
        float w = Mathf.Max(1.3f, r * 0.14f);
        float rx = r / sq, ry = r * squash;
        Color ic = new Color(ink.r, ink.g, ink.b, ink.a * Mathf.Max(0.35f, baseC.a));
        if (hollow)
        {
            // an open ball (muted / rest): a thin ink circle with the colour just inside it, no fill
            float hw = Mathf.Max(1.1f, r * 0.085f);
            HexRing(vh, c, r, hw, ic, 28, 0f);
            HexRing(vh, c, r - hw * 1.25f, Mathf.Max(1f, hw * 0.9f), new Color(baseC.r, baseC.g, baseC.b, 0.95f * baseC.a), 28, 0f);
            return;
        }
        Color shade = Color.Lerp(Mul(baseC, 0.72f), new Color(0.36f, 0.24f, 0.62f, baseC.a), 0.14f);
        Disc(vh, c, rx + w, ry + w, ic, 28);
        Disc(vh, c, rx, ry, shade, 28);
        Disc(vh, c + new Vector2(-0.13f * rx, 0.13f * ry), rx * 0.84f, ry * 0.84f, baseC, 28);
        Disc(vh, c + new Vector2(-0.36f * rx, 0.4f * ry), rx * 0.16f, ry * 0.16f, Mul(baseC, 1.25f), 12);
        // the seams: two tilted great circles, front halves only, in ink (the 3D ball's)
        Color cream = new Color(ink.r, ink.g, ink.b, ink.a * 0.85f * baseC.a);   // ink seams, like the 3D ball's
        float sw = Mathf.Max(1.1f, r * 0.1f);
        Vector2 prev = Vector2.zero;
        for (int ring = 0; ring < 2; ring++)
        {
            float tilt = ring == 0 ? -0.35f : 1.22f, flat = ring == 0 ? 0.38f : 0.42f;
            float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            for (int i = 0; i <= 14; i++)
            {
                float t = Mathf.PI * (1f + i / 14f);   // the lower (front) half of the ellipse
                Vector2 e = new Vector2(Mathf.Cos(t) * rx * 0.97f, Mathf.Sin(t) * ry * flat);
                Vector2 q = c + new Vector2(e.x * ct - e.y * st, e.x * st + e.y * ct);
                if (i > 0) Line(vh, prev, q, sw, cream);
                prev = q;
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif
}
