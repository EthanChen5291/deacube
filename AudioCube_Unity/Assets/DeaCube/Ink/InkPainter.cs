using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md): a small immediate-mode ink drawing surface for the pictures the other kit shapes do not cover (curved
/// arrows, the column rail and its beads, the loop curl, the metronome needle, ladders, eyes, knobs, the deck's island cards). The owner sets
/// <see cref="onPaint"/> once; it runs only when the mesh rebuilds (<see cref="Repaint"/>, a size change, or at <see cref="InkShape.BoilFps"/>
/// while <see cref="Boil"/> is on), so a resting picture costs nothing per frame. Every primitive works in the rect's local pixels (pivot at
/// 0,0; <see cref="Area"/> is the rect while painting), is anti-aliased with a 0.9 px feather and is tinted by Graphic.color (fade a whole
/// picture through its alpha). <see cref="Shift"/> offsets everything drawn after it (hard shadows, misregistration). <see cref="hitTest"/>
/// narrows raycasts to a shape (ghost islands). No allocation while painting: the primitives share static scratch buffers.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class InkPainter : MaskableGraphic, ICanvasRaycastFilter
{
    public const float Feather = 0.9f;

    /// <summary>Draws the picture into this painter (called from OnPopulateMesh).</summary>
    public Action<InkPainter> onPaint;
    /// <summary>Optional hit test in local px: raycasts outside it pass through (null = the whole rect).</summary>
    public Func<Vector2, bool> hitTest;

    [SerializeField] bool boil;
    [SerializeField] int seed;

    /// <summary>Re-draws at <see cref="InkShape.BoilFps"/> with a new <see cref="Phase"/> (hovered / pressed / active only).</summary>
    public bool Boil { get => boil; set { if (boil != value) { boil = value; SetVerticesDirty(); } } }
    /// <summary>Seed of <see cref="Noise"/> / <see cref="Jit"/>; 0 = derived from the GameObject's name.</summary>
    public int Seed { get => seed; set { if (seed != value) { seed = value; SetVerticesDirty(); } } }
    /// <summary>The boil phase while painting (0 at rest).</summary>
    public int Phase { get; private set; }
    /// <summary>The pixel-adjusted rect while painting (local px, pivot at 0,0).</summary>
    public Rect Area { get; private set; }
    /// <summary>Offset added to every vertex drawn after it is set (reset to zero before each paint).</summary>
    [NonSerialized] public Vector2 Shift;

    VertexHelper vh;
    int boilFrame = -1, nameSeed;

    /// <summary>Asks for a repaint (the mesh rebuilds once, this frame).</summary>
    public void Repaint() { SetVerticesDirty(); }

    public static InkPainter Create(Transform parent, string name, Vector2 size, Action<InkPainter> paint, bool raycast = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<InkPainter>();
        p.onPaint = paint; p.raycastTarget = raycast;
        ((RectTransform)go.transform).sizeDelta = size;
        return p;
    }

    protected override void Awake()
    {
        base.Awake();
        unchecked { int h = 29; foreach (char ch in gameObject.name) h = h * 31 + ch; nameSeed = h == 0 ? 1 : h; }
    }

    void Update()
    {
        if (!boil) return;
        int f = Mathf.FloorToInt(Time.unscaledTime * InkShape.BoilFps);
        if (f != boilFrame) { boilFrame = f; SetVerticesDirty(); }
    }

    public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
    {
        if (hitTest == null) return true;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out lp)) return false;
        return hitTest(lp);
    }

    protected override void OnPopulateMesh(VertexHelper v)
    {
        v.Clear();
        Area = GetPixelAdjustedRect();
        Phase = boil ? Mathf.FloorToInt(Time.unscaledTime * InkShape.BoilFps) : 0;
        Shift = Vector2.zero;
        if (onPaint == null) return;
        vh = v;
        overBudget = false;
        try { onPaint(this); }
        catch (Exception e) { Debug.LogException(e, this); }
        finally
        {
            if (overBudget && !warnedBudget) { warnedBudget = true; Debug.LogWarning("InkPainter '" + name + "' under '" + (transform.parent != null ? transform.parent.name : "-") + "' reached the UI mesh limit (" + v.currentVertCount + " vertices, " + Area.width.ToString("0") + "×" + Area.height.ToString("0") + " px) — the rest of its drawing was skipped"); }
            vh = null; Shift = Vector2.zero;
        }
    }

    // ---------------------------------------------------------------- noise (seeded, stable at rest, stepped by the boil phase)
    int S => seed != 0 ? seed : (nameSeed != 0 ? nameSeed : 1);

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

    /// <summary>A seeded value in [-1, 1] for key <paramref name="k"/> (changes with the boil phase).</summary>
    public float Noise(int k) => Hash01(S + Phase * 7919, k) * 2f - 1f;
    /// <summary>A seeded value in [-1, 1] that ignores the boil (layout jitter that must not jump).</summary>
    public float Still(int k) => Hash01(S, k + 100003) * 2f - 1f;
    /// <summary>A seeded offset of up to <paramref name="amp"/> px for key <paramref name="k"/>.</summary>
    public Vector2 Jit(int k, float amp) => new Vector2(Noise(k * 2), Noise(k * 2 + 1)) * amp;

    // ---------------------------------------------------------------- primitives
    Color32 Tint(Color c) { Color g = color; return new Color(c.r * g.r, c.g * g.g, c.b * g.b, c.a * g.a); }
    static Color Clear(Color c) => new Color(c.r, c.g, c.b, 0f);

    int V(Vector2 p, Color32 c) { int i = vh.currentVertCount; vh.AddVert(p + Shift, c, Vector2.zero); return i; }

    // v7 (integration): a UI mesh holds at most 65000 vertices — past that Unity throws and the painter draws nothing (seen at the end of the v3
    // suites). Each primitive checks its vertex count first: at the budget the rest is skipped (the picture is cut short, not lost) and the
    // painter says so once.
    public const int MaxVerts = 64000;
    bool overBudget, warnedBudget;
    bool Over(int add) { if (vh.currentVertCount + add <= MaxVerts) return false; overBudget = true; return true; }

    public void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        if (vh == null || Over(3)) return;
        Color32 k = Tint(col);
        int i = V(a, k); V(b, k); V(c, k);
        vh.AddTriangle(i, i + 1, i + 2);
    }

    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        if (vh == null || Over(4)) return;
        Color32 k = Tint(col);
        int i = V(a, k); V(b, k); V(c, k); V(d, k);
        vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
    }

    static readonly List<Vector2> fillN = new List<Vector2>(256);

    /// <summary>A filled polygon (convex or star-shaped around its centroid) with a feathered edge.</summary>
    public void Fill(IList<Vector2> p, int n, Color col)
    {
        if (vh == null || n < 3 || Over(1 + 2 * n)) return;
        Vector2 c = Vector2.zero; for (int i = 0; i < n; i++) c += p[i]; c /= n;
        Color32 k = Tint(col), kc = Tint(Clear(col));
        fillN.Clear();
        for (int i = 0; i < n; i++)
        {
            Vector2 a = p[(i - 1 + n) % n], b = p[(i + 1) % n];
            Vector2 t = b - a; Vector2 nr = t.sqrMagnitude > 1e-8f ? new Vector2(t.y, -t.x).normalized : (p[i] - c).normalized;
            if (Vector2.Dot(nr, p[i] - c) < 0f) nr = -nr;
            fillN.Add(nr);
        }
        int ci = V(c, k);
        int b0 = vh.currentVertCount;
        for (int i = 0; i < n; i++) { V(p[i], k); V(p[i] + fillN[i] * Feather, kc); }
        for (int i = 0; i < n; i++)
        {
            int a = b0 + i * 2, b = b0 + ((i + 1) % n) * 2;
            vh.AddTriangle(ci, a, b);
            vh.AddTriangle(a, a + 1, b + 1); vh.AddTriangle(a, b + 1, b);
        }
    }

    static readonly List<Vector2> discP = new List<Vector2>(128);

    /// <summary>A filled ellipse (feathered).</summary>
    public void Disc(Vector2 c, float rx, float ry, Color col, int seg = 0)
    {
        if (vh == null || rx <= 0f || ry <= 0f) return;
        if (seg <= 0) seg = Mathf.Clamp(Mathf.CeilToInt((rx + ry) * 0.9f), 10, 72);
        discP.Clear();
        for (int i = 0; i < seg; i++) { float a = i / (float)seg * Mathf.PI * 2f; discP.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry)); }
        Fill(discP, seg, col);
    }
    public void Disc(Vector2 c, float r, Color col) { Disc(c, r, r, col, 0); }

    static readonly List<Vector2> arcP = new List<Vector2>(128);

    /// <summary>Appends the points of an elliptical arc (degrees, counter-clockwise from +x) to <paramref name="into"/>.</summary>
    public static void ArcPoints(List<Vector2> into, Vector2 c, float rx, float ry, float a0Deg, float a1Deg, int seg = 0)
    {
        float span = Mathf.Abs(a1Deg - a0Deg);
        if (seg <= 0) seg = Mathf.Clamp(Mathf.CeilToInt(span / 360f * (rx + ry) * 0.9f), 4, 96);
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Lerp(a0Deg, a1Deg, i / (float)seg) * Mathf.Deg2Rad;
            into.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
        }
    }

    /// <summary>An arc stroke (ring when 0..360).</summary>
    public void Arc(Vector2 c, float r, float w, Color col, float a0Deg = 0f, float a1Deg = 360f, float dash = 0f, float gap = 0f)
    {
        arcP.Clear();
        bool full = Mathf.Abs(a1Deg - a0Deg) >= 359.9f;
        ArcPoints(arcP, c, r, r, a0Deg, full ? a0Deg + 360f * (1f - 1f / 72f) : a1Deg, full ? 72 : 0);
        Stroke(arcP, arcP.Count, w, col, full, dash, gap);
    }

    // ---------------------------------------------------------------- strokes
    static readonly List<float> sLen = new List<float>(256);
    static readonly List<Vector2> sPts = new List<Vector2>(256);
    static readonly List<float> sW = new List<float>(256);

    /// <summary>A polyline stroke <paramref name="w"/> px wide (tapering to <paramref name="w1"/> at the end when ≥ 0), mitred joins,
    /// feathered edges; <paramref name="dash"/> &gt; 0 draws it dashed; <paramref name="caps"/> rounds open ends.</summary>
    public void Stroke(IList<Vector2> p, int n, float w, Color col, bool closed = false, float dash = 0f, float gap = 0f, float w1 = -1f, bool caps = false)
    {
        if (vh == null || n < 2) return;
        if (w1 < 0f) w1 = w;
        // cumulative length
        sLen.Clear(); float total = 0f; sLen.Add(0f);
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++) { total += (p[(i + 1) % n] - p[i]).magnitude; sLen.Add(total); }
        if (total < 1e-4f) return;
        if (dash <= 0f || gap <= 0f)
        {
            if (closed && Mathf.Approximately(w, w1)) { StrokeClosed(p, n, w, col); return; }
            StrokeRange(p, n, closed, 0f, total, total, w, w1, col, caps);
            return;
        }
        float period = dash + gap;
        for (float s = 0f; s < total - 0.01f; s += period)
            StrokeRange(p, n, closed, s, Mathf.Min(total, s + dash), total, w, w1, col, caps);
    }

    Vector2 PointAt(IList<Vector2> p, int n, float s, out int seg)
    {
        int segs = sLen.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            if (s <= sLen[i + 1] || i == segs - 1)
            {
                float l = sLen[i + 1] - sLen[i];
                float t = l > 1e-5f ? Mathf.Clamp01((s - sLen[i]) / l) : 0f;
                seg = i;
                return Vector2.Lerp(p[i], p[(i + 1) % n], t);
            }
        }
        seg = 0; return p[0];
    }

    void StrokeRange(IList<Vector2> p, int n, bool closed, float s0, float s1, float total, float w0, float w1, Color col, bool caps)
    {
        sPts.Clear(); sW.Clear();
        int seg0, seg1;
        Vector2 a = PointAt(p, n, s0, out seg0), b = PointAt(p, n, s1, out seg1);
        sPts.Add(a); sW.Add(Mathf.Lerp(w0, w1, s0 / total));
        for (int i = seg0 + 1; i <= seg1; i++) { sPts.Add(p[i % n]); sW.Add(Mathf.Lerp(w0, w1, sLen[i] / total)); }
        if ((sPts[sPts.Count - 1] - b).sqrMagnitude > 1e-6f) { sPts.Add(b); sW.Add(Mathf.Lerp(w0, w1, s1 / total)); }
        if (sPts.Count < 2) return;
        Strip(sPts, sW, false, col);
        if (caps)
        {
            Disc(sPts[0], sW[0] * 0.5f, sW[0] * 0.5f, col, 12);
            Disc(sPts[sPts.Count - 1], sW[sW.Count - 1] * 0.5f, sW[sW.Count - 1] * 0.5f, col, 12);
        }
    }

    void StrokeClosed(IList<Vector2> p, int n, float w, Color col)
    {
        sPts.Clear(); sW.Clear();
        for (int i = 0; i < n; i++) { sPts.Add(p[i]); sW.Add(w); }
        Strip(sPts, sW, true, col);
    }

    /// <summary>Emits a feathered strip along <paramref name="q"/> (4 vertices per point: clear, ink, ink, clear).</summary>
    void Strip(List<Vector2> q, List<float> ws, bool closed, Color col)
    {
        int m = q.Count;
        if (Over(4 * m)) return;
        Color32 on = Tint(col), off = Tint(Clear(col));
        int b0 = vh.currentVertCount;
        for (int i = 0; i < m; i++)
        {
            Vector2 prev = closed ? q[(i - 1 + m) % m] : q[Mathf.Max(0, i - 1)];
            Vector2 next = closed ? q[(i + 1) % m] : q[Mathf.Min(m - 1, i + 1)];
            Vector2 d0 = (q[i] - prev), d1 = (next - q[i]);
            if (d0.sqrMagnitude < 1e-8f) d0 = d1; if (d1.sqrMagnitude < 1e-8f) d1 = d0;
            d0.Normalize(); d1.Normalize();
            Vector2 n0 = new Vector2(-d0.y, d0.x), n1 = new Vector2(-d1.y, d1.x);
            Vector2 nm = n0 + n1; nm = nm.sqrMagnitude > 1e-8f ? nm.normalized : n1;
            float miter = 1f / Mathf.Max(0.35f, Vector2.Dot(nm, n1));
            float h = ws[i] * 0.5f * miter, f = Feather * miter;
            V(q[i] + nm * (h + f), off); V(q[i] + nm * h, on); V(q[i] - nm * h, on); V(q[i] - nm * (h + f), off);
        }
        int last = closed ? m : m - 1;
        for (int i = 0; i < last; i++)
        {
            int a = b0 + i * 4, b = b0 + ((i + 1) % m) * 4;
            for (int k = 0; k < 3; k++) { vh.AddTriangle(a + k, a + k + 1, b + k + 1); vh.AddTriangle(a + k, b + k + 1, b + k); }
        }
    }

    public void Line(Vector2 a, Vector2 b, float w, Color col, bool caps = false)
    {
        arcP.Clear(); arcP.Add(a); arcP.Add(b);
        Stroke(arcP, 2, w, col, false, 0f, 0f, -1f, caps);
    }

    /// <summary>An inked stroke: the ink line (<paramref name="w"/> + 2 × <paramref name="inkW"/>) under the coloured one.</summary>
    public void InkStroke(IList<Vector2> p, int n, float w, Color col, float inkW, Color ink, bool closed = false, float dash = 0f, float gap = 0f, bool caps = true)
    {
        Stroke(p, n, w + 2f * inkW, ink, closed, dash, gap, -1f, caps);
        Stroke(p, n, w, col, closed, dash, gap, -1f, caps);
    }

    /// <summary>An inked filled polygon: the fill, then its ink outline.</summary>
    public void InkFill(IList<Vector2> p, int n, Color fill, float inkW, Color ink)
    {
        Fill(p, n, fill);
        if (inkW > 0f) Stroke(p, n, inkW, ink, true);
    }

    static readonly List<Vector2> headP = new List<Vector2>(4);

    /// <summary>A filled arrowhead with its tip at <paramref name="tip"/> pointing along <paramref name="dir"/>.</summary>
    public void Head(Vector2 tip, Vector2 dir, float len, float width, Color col, float inkW = 0f, Color ink = default(Color))
    {
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();
        Vector2 nrm = new Vector2(-dir.y, dir.x), back = tip - dir * len;
        headP.Clear(); headP.Add(tip); headP.Add(back + nrm * width * 0.5f); headP.Add(back - nrm * width * 0.5f);
        if (inkW > 0f)
        {
            // an ink copy grown by inkW on every side under the colour
            Vector2 t2 = tip + dir * inkW * 1.8f, b2 = back - dir * inkW;
            arcP.Clear(); arcP.Add(t2); arcP.Add(b2 + nrm * (width * 0.5f + inkW * 1.6f)); arcP.Add(b2 - nrm * (width * 0.5f + inkW * 1.6f));
            Fill(arcP, 3, ink);
        }
        Fill(headP, 3, col);
    }

    /// <summary>A rounded rectangle fill (feathered) — beads, chips, pads.</summary>
    public void RoundRect(Rect r, float rad, Color col, float inkW = 0f, Color ink = default(Color), float dash = 0f, float gap = 0f)
    {
        arcP.Clear();
        RoundRectPoints(arcP, r, rad);
        if (col.a > 0.001f) Fill(arcP, arcP.Count, col);
        if (inkW > 0f) Stroke(arcP, arcP.Count, inkW, ink, true, dash, gap);
    }

    /// <summary>Appends a counter-clockwise rounded rectangle outline (from the bottom-right corner).</summary>
    public static void RoundRectPoints(List<Vector2> into, Rect r, float rad)
    {
        rad = Mathf.Clamp(rad, 0f, Mathf.Min(r.width, r.height) * 0.5f);
        int st = Mathf.Clamp(Mathf.CeilToInt(rad / 2.5f), 1, 10);
        ArcPointsNoDup(into, new Vector2(r.xMax - rad, r.yMin + rad), rad, -90f, 0f, st);
        ArcPointsNoDup(into, new Vector2(r.xMax - rad, r.yMax - rad), rad, 0f, 90f, st);
        ArcPointsNoDup(into, new Vector2(r.xMin + rad, r.yMax - rad), rad, 90f, 180f, st);
        ArcPointsNoDup(into, new Vector2(r.xMin + rad, r.yMin + rad), rad, 180f, 270f, st);
    }

    static void ArcPointsNoDup(List<Vector2> into, Vector2 c, float r, float a0, float a1, int st)
    {
        for (int i = 0; i <= st; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)st) * Mathf.Deg2Rad;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (into.Count > 0 && (into[into.Count - 1] - p).sqrMagnitude < 0.01f) continue;
            into.Add(p);
        }
    }

    /// <summary>A small inked isometric cube (the kit's motif) drawn inline: top / left / right faces, ink outline.</summary>
    public void Cube(Vector2 c, float size, Color col, Color ink, float inkW = 1.4f, bool hollow = false)
    {
        float R = size * 0.5f, hx = R * 0.8660254f;
        Vector2 top = c + new Vector2(0f, R), ur = c + new Vector2(hx, R * 0.5f), lr = c + new Vector2(hx, -R * 0.5f);
        Vector2 bot = c + new Vector2(0f, -R), ll = c + new Vector2(-hx, -R * 0.5f), ul = c + new Vector2(-hx, R * 0.5f);
        if (!hollow)
        {
            Quad(top, ur, c, ul, new Color(Mathf.Clamp01(col.r * 1.15f), Mathf.Clamp01(col.g * 1.15f), Mathf.Clamp01(col.b * 1.15f), col.a));
            Quad(ul, c, bot, ll, col);
            Quad(c, ur, lr, bot, Color.Lerp(new Color(col.r * 0.72f, col.g * 0.72f, col.b * 0.72f, col.a), new Color(0.36f, 0.24f, 0.62f, col.a), 0.14f));
        }
        arcP.Clear(); arcP.Add(top); arcP.Add(ur); arcP.Add(lr); arcP.Add(bot); arcP.Add(ll); arcP.Add(ul);
        Stroke(arcP, 6, inkW, ink, true);
        if (!hollow) { Line(c, ul, inkW * 0.6f, ink); Line(c, ur, inkW * 0.6f, ink); Line(c, bot, inkW * 0.6f, ink); }
    }
}
