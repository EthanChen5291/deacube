using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md): the hand-inked shape all v4 chrome is drawn with. A paper fill, a wobbly ink outline that is heavier on
/// the shadow side, and a hard offset shadow (no blur), with seeded imperfection: outline wobble, a small rotation, chamfered sticker
/// corners. Kinds: rounded rect, circle, notched ribbon, speech bubble with a tail, irregular sticker, dotted ghost outline. The fill is
/// Graphic.color. The mesh is rebuilt only when a parameter changes, or at <see cref="BoilFps"/> while <see cref="Boil"/> is on (hovered,
/// pressed, active). Every look value is stable per <see cref="Seed"/> (default: derived from the object's name).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class InkShape : MaskableGraphic
{
    public enum Kind { RoundRect, Circle, Ribbon, Bubble, Sticker, Ghost }
    public const float BoilFps = 10f;

    [SerializeField] Kind kind = Kind.RoundRect;
    [SerializeField] int seed;
    [SerializeField] float radius = 14f;
    [SerializeField] float wobble = 1.2f;
    [SerializeField] float inkMin = 2.2f, inkMax = 3.6f;
    [SerializeField] Color ink = new Color(0.106f, 0.078f, 0.165f, 1f);
    [SerializeField] bool shadow = true;
    [SerializeField] Vector2 shadowOffset = new Vector2(4f, -5f);
    [SerializeField] Color shadowColor = new Color(0.106f, 0.078f, 0.165f, 0.92f);
    [SerializeField] float rotJitter = 1.5f;
    [SerializeField] bool hollow;
    [SerializeField] bool boil;
    [SerializeField] Vector2 tailTip = new Vector2(-80f, -40f);
    [SerializeField] float tailWidth = 40f;
    [SerializeField] float dash = 7f, gap = 5f;
    [SerializeField] float wobbleWave;

    public Kind Shape { get => kind; set { if (kind != value) { kind = value; SetVerticesDirty(); } } }
    /// <summary>Seed of every random look value; 0 = derived from the GameObject's name (stable across runs).</summary>
    public int Seed { get => seed; set { if (seed != value) { seed = value; SetVerticesDirty(); } } }
    /// <summary>Corner radius (px) of RoundRect / Bubble / Ghost / Sticker.</summary>
    public float Radius { get => radius; set { if (radius != value) { radius = value; SetVerticesDirty(); } } }
    /// <summary>Outline wobble amplitude (px, 0 = clean).</summary>
    public float Wobble { get => wobble; set { if (wobble != value) { wobble = value; SetVerticesDirty(); } } }
    /// <summary>Outline width on the lit side and on the shadow side (px).</summary>
    public void SetInk(float min, float max) { inkMin = min; inkMax = max; SetVerticesDirty(); }
    public Color Ink { get => ink; set { if (ink != value) { ink = value; SetVerticesDirty(); } } }
    public bool HasShadow { get => shadow; set { if (shadow != value) { shadow = value; SetVerticesDirty(); } } }
    public Vector2 ShadowOffset { get => shadowOffset; set { if (shadowOffset != value) { shadowOffset = value; SetVerticesDirty(); } } }
    public Color ShadowColor { get => shadowColor; set { if (shadowColor != value) { shadowColor = value; SetVerticesDirty(); } } }
    /// <summary>Maximum seeded tilt in degrees (±); stickers use up to 4.</summary>
    public float RotJitter { get => rotJitter; set { if (rotJitter != value) { rotJitter = value; SetVerticesDirty(); } } }
    /// <summary>No fill (outline + shadow only).</summary>
    public bool Hollow { get => hollow; set { if (hollow != value) { hollow = value; SetVerticesDirty(); } } }
    /// <summary>Re-draws the wobble at BoilFps (use while hovered / pressed / active; off at rest).</summary>
    public bool Boil { get => boil; set { if (boil != value) { boil = value; SetVerticesDirty(); } } }
    /// <summary>Bubble tail tip in this rect's local space (px, pivot-relative).</summary>
    public Vector2 TailTip { get => tailTip; set { if ((tailTip - value).sqrMagnitude > 0.25f) { tailTip = value; SetVerticesDirty(); } } }
    public float TailWidth { get => tailWidth; set { value = Mathf.Max(8f, value); if (tailWidth != value) { tailWidth = value; SetVerticesDirty(); } } }
    public void SetDash(float on, float off) { dash = Mathf.Max(1f, on); gap = Mathf.Max(1f, off); SetVerticesDirty(); }
    /// <summary>Approximate wobble wavelength in px (0 = relative to the perimeter, fine for small shapes; big panels want ~120-200 so
    /// a long edge still reads hand-drawn).</summary>
    public float WobbleWave { get => wobbleWave; set { if (wobbleWave != value) { wobbleWave = value; SetVerticesDirty(); } } }

    /// <summary>Creates an InkShape child: a stretched or sized RectTransform under <paramref name="parent"/>.</summary>
    public static InkShape Create(Transform parent, string name, Kind kind, Color fill, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<InkShape>();
        s.kind = kind; s.color = fill;
        ((RectTransform)go.transform).sizeDelta = size;
        if (kind == Kind.Sticker) s.rotJitter = 3f;
        if (kind == Kind.Ghost) { s.shadow = false; s.hollow = true; }
        return s;
    }

    int boilFrame = -1;
    static readonly List<Vector2> tailScratch = new List<Vector2>(200);
    readonly List<Vector2> pts = new List<Vector2>(160);
    readonly List<Vector2> nrm = new List<Vector2>(160);
    readonly List<float> arc = new List<float>(160);

    int nameSeed;
    // v7: points every ~10 px, but never more than ~1500 along the outline (a runaway rect would overflow the 65000-vertex UI mesh)
    float lineStep = 10f;
    bool warnedHuge;
    int EffectiveSeed
    {
        get
        {
            if (seed != 0) return seed;
            if (nameSeed == 0) { unchecked { int h = 17; foreach (char ch in gameObject.name) h = h * 31 + ch; nameSeed = h == 0 ? 1 : h; } }   // cached: name reads allocate
            return nameSeed;
        }
    }

    void Update()
    {
        if (!boil) return;
        int f = Mathf.FloorToInt(Time.unscaledTime * BoilFps);
        if (f != boilFrame) { boilFrame = f; SetVerticesDirty(); }
    }

    // ---------------------------------------------------------------- deterministic noise
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
    static float Signed(int a, int b) => Hash01(a, b) * 2f - 1f;

    /// <summary>Loop-periodic smooth noise in [-1, 1] at fraction u of the perimeter (three seeded harmonics).</summary>
    static float LoopNoise(int s, float u, int phase, int fmul = 1)
    {
        float v = 0f;
        for (int k = 0; k < 3; k++)
        {
            int f = (2 + k * 3 + Mathf.FloorToInt(Hash01(s, 11 + k) * 3f)) * fmul;
            float ph = Hash01(s + phase * 7919, 23 + k) * Mathf.PI * 2f;
            v += Mathf.Sin(u * f * Mathf.PI * 2f + ph) / (1f + k * 0.8f);
        }
        return v / 1.9f;
    }

    // ---------------------------------------------------------------- outline
    void AddArc(float cx, float cy, float r, float a0, float a1, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)steps);
            pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r));
        }
    }

    void AddLine(Vector2 a, Vector2 b, float step)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / step));
        for (int i = 1; i < n; i++) pts.Add(Vector2.Lerp(a, b, i / (float)n));
    }

    /// <summary>Counter-clockwise rounded rect with points every ~10 px along the straight edges.</summary>
    void RoundRectPoints(Rect r, float rad, int s, bool chamfer)
    {
        rad = Mathf.Clamp(rad, 0f, Mathf.Min(r.width, r.height) * 0.5f);
        // corners: 0 bottom-right, 1 top-right, 2 top-left, 3 bottom-left (CCW order starting at the bottom edge)
        var cutA = chamfer ? Mathf.FloorToInt(Hash01(s, 3) * 4f) : -1;
        var cutB = chamfer && Hash01(s, 4) > 0.45f ? (cutA + 2) % 4 : -1;
        Vector2[] c = { new Vector2(r.xMax - rad, r.yMin + rad), new Vector2(r.xMax - rad, r.yMax - rad), new Vector2(r.xMin + rad, r.yMax - rad), new Vector2(r.xMin + rad, r.yMin + rad) };
        float[] a0 = { -Mathf.PI * 0.5f, 0f, Mathf.PI * 0.5f, Mathf.PI };
        int steps = Mathf.Clamp(Mathf.CeilToInt(rad / 3f), 2, 10);
        for (int k = 0; k < 4; k++)
        {
            Vector2 start = pts.Count > 0 ? pts[pts.Count - 1] : Vector2.zero;
            if (k == cutA || k == cutB)
            {
                float cut = Mathf.Lerp(6f, 14f, Hash01(s, 5 + k)) + rad;
                Vector2 corner = new Vector2(k == 0 || k == 1 ? r.xMax : r.xMin, k == 1 || k == 2 ? r.yMax : r.yMin);
                Vector2 inDir = k == 0 ? Vector2.right : (k == 1 ? Vector2.up : (k == 2 ? Vector2.left : Vector2.down));
                Vector2 outDir = k == 0 ? Vector2.up : (k == 1 ? Vector2.left : (k == 2 ? Vector2.down : Vector2.right));
                Vector2 p0 = corner - inDir * cut, p1 = corner + outDir * cut;
                if (pts.Count > 0) AddLine(start, p0, lineStep);
                pts.Add(p0); AddLine(p0, p1, lineStep); pts.Add(p1);
                continue;
            }
            Vector2 first = c[k] + new Vector2(Mathf.Cos(a0[k]), Mathf.Sin(a0[k])) * rad;
            if (pts.Count > 0) AddLine(start, first, lineStep);
            AddArc(c[k].x, c[k].y, rad, a0[k], a0[k] + Mathf.PI * 0.5f, steps);
        }
        // close the bottom edge back to the first point
        if (pts.Count > 1) { var last = pts[pts.Count - 1]; var first0 = pts[0]; int n = Mathf.Max(1, Mathf.CeilToInt((first0 - last).magnitude / lineStep)); for (int i = 1; i < n; i++) pts.Add(Vector2.Lerp(last, first0, i / (float)n)); }
    }

    void BuildOutline(Rect r, int s, int phase)
    {
        pts.Clear();
        float perim = 2f * (Mathf.Abs(r.width) + Mathf.Abs(r.height));
        lineStep = Mathf.Max(10f, perim / 1500f);
        if (perim > 20000f && !warnedHuge) { warnedHuge = true; Debug.LogWarning($"InkShape '{name}' under '{(transform.parent != null ? transform.parent.name : "-")}' is {r.width:0}×{r.height:0} px — outline capped"); }
        switch (kind)
        {
            case Kind.Circle:
            {
                int n = Mathf.Clamp(Mathf.CeilToInt((r.width + r.height) * 0.35f), 24, 96);
                for (int i = 0; i < n; i++)
                {
                    float a = -Mathf.PI * 0.5f + i / (float)n * Mathf.PI * 2f;
                    pts.Add(new Vector2(r.center.x + Mathf.Cos(a) * r.width * 0.5f, r.center.y + Mathf.Sin(a) * r.height * 0.5f));
                }
                break;
            }
            case Kind.Ribbon:
            {
                float notch = Mathf.Min(r.height * 0.38f, r.width * 0.12f);
                var bl = new Vector2(r.xMin, r.yMin); var br = new Vector2(r.xMax, r.yMin); var tr = new Vector2(r.xMax, r.yMax); var tl = new Vector2(r.xMin, r.yMax);
                var nr = new Vector2(r.xMax - notch, r.center.y); var nl = new Vector2(r.xMin + notch, r.center.y);
                pts.Add(bl); AddLine(bl, br, lineStep); pts.Add(br); AddLine(br, nr, lineStep); pts.Add(nr); AddLine(nr, tr, lineStep); pts.Add(tr);
                AddLine(tr, tl, lineStep); pts.Add(tl); AddLine(tl, nl, lineStep); pts.Add(nl); AddLine(nl, bl, lineStep);
                break;
            }
            case Kind.Sticker: RoundRectPoints(r, Mathf.Min(radius, 7f), s, true); break;
            default: RoundRectPoints(r, radius, s, false); break;
        }
        if (kind == Kind.Bubble) InsertTail(r);
        // arc length
        arc.Clear();
        float total = 0f;
        for (int i = 0; i < pts.Count; i++) { arc.Add(total); total += (pts[(i + 1) % pts.Count] - pts[i]).magnitude; }
        if (total < 1e-3f) total = 1f;
        // normals (CCW → outward = (dy, -dx) of the averaged tangent)
        nrm.Clear();
        int m = pts.Count;
        for (int i = 0; i < m; i++)
        {
            Vector2 t = (pts[(i + 1) % m] - pts[(i - 1 + m) % m]).normalized;
            nrm.Add(new Vector2(t.y, -t.x));
        }
        // wobble along the normal (loop-periodic, seeded; boil steps its phase)
        if (wobble > 0f)
        {
            int fmul = wobbleWave > 0f ? Mathf.Max(1, Mathf.RoundToInt(total / (wobbleWave * 5f))) : 1;
            for (int i = 0; i < m; i++)
            {
                float u = arc[i] / total;
                float amp = kind == Kind.Sticker ? wobble * 1.4f : wobble;
                pts[i] += nrm[i] * (LoopNoise(s, u, phase, fmul) * amp);
            }
        }
        // tilt around the centre
        float rot = Signed(s, 1) * rotJitter * Mathf.Deg2Rad;
        if (Mathf.Abs(rot) > 1e-4f)
        {
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot); Vector2 c0 = r.center;
            for (int i = 0; i < m; i++) { var d = pts[i] - c0; pts[i] = c0 + new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs); var q = nrm[i]; nrm[i] = new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs); }
        }
    }

    /// <summary>Replaces the outline span facing <see cref="tailTip"/> with a spike to it (the bubble's tail).</summary>
    void InsertTail(Rect r)
    {
        int m = pts.Count; if (m < 8) return;
        Vector2 c0 = r.center, dir = (tailTip - c0);
        if (dir.sqrMagnitude < 1f || r.Contains(tailTip)) return;
        dir.Normalize();
        int best = 0; float bd = float.NegativeInfinity;
        for (int i = 0; i < m; i++) { float d = Vector2.Dot((pts[i] - c0).normalized, dir); if (d > bd) { bd = d; best = i; } }
        // walk out to half the tail width on each side
        float half = tailWidth * 0.5f; int a = best, b = best; float la = 0f, lb = 0f;
        while (la < half && a > best - m / 4) { var p = pts[(a - 1 + m) % m]; la += (pts[(a + m) % m] - p).magnitude; a--; }
        while (lb < half && b < best + m / 4) { var p = pts[(b + 1) % m]; lb += (p - pts[b % m]).magnitude; b++; }
        var keep = tailScratch; keep.Clear();
        int ia = (a + m) % m, ib = b % m;
        if (ia == ib) return;
        // rebuild: from ib around to ia (the untouched span), then the tip
        int i2 = ib;
        keep.Add(pts[ib]);
        while (i2 != ia) { i2 = (i2 + 1) % m; keep.Add(pts[i2]); }
        keep.Add(tailTip);
        pts.Clear(); pts.AddRange(keep);
    }

    // ---------------------------------------------------------------- mesh
    static void Fan(VertexHelper vh, List<Vector2> p, Vector2 centre, Vector2 off, Color32 c)
    {
        int b = vh.currentVertCount;
        vh.AddVert(centre + off, c, Vector2.zero);
        for (int i = 0; i < p.Count; i++) vh.AddVert(p[i] + off, c, Vector2.zero);
        for (int i = 0; i < p.Count; i++) vh.AddTriangle(b, b + 1 + i, b + 1 + (i + 1) % p.Count);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 2f || r.height < 2f) return;
        int s = EffectiveSeed;
        int phase = boil ? Mathf.FloorToInt(Time.unscaledTime * BoilFps) : 0;
        BuildOutline(r, s, phase);
        int m = pts.Count; if (m < 3) return;
        Vector2 centre = r.center;
        bool ghost = kind == Kind.Ghost;
        // 1 shadow, 2 fill
        if (shadow && !ghost) Fan(vh, pts, centre, shadowOffset, shadowColor);
        if (!hollow && !ghost) Fan(vh, pts, centre, Vector2.zero, color);
        else if (ghost && color.a > 0.01f) Fan(vh, pts, centre, Vector2.zero, color);
        // 3 outline: a strip heavier on the shadow side with a 1 px feather on both edges
        Vector2 sd = shadowOffset.sqrMagnitude > 1e-3f ? shadowOffset.normalized : new Vector2(0.6f, -0.8f);
        Color32 inkC = ink, inkClear = new Color(ink.r, ink.g, ink.b, 0f);
        float total = 0f; for (int i = 0; i < m; i++) total += (pts[(i + 1) % m] - pts[i]).magnitude;
        float run = 0f;
        for (int i = 0; i < m; i++)
        {
            int j = (i + 1) % m;
            float segLen = (pts[j] - pts[i]).magnitude;
            bool on = !ghost || ((run % (dash + gap)) < dash);
            run += segLen;
            if (!on) continue;
            Vector2 ni = SafeNormal(i, m), nj = SafeNormal(j, m);
            float wi = Mathf.Lerp(inkMin, inkMax, Mathf.Max(0f, Vector2.Dot(ni, sd)));
            float wj = Mathf.Lerp(inkMin, inkMax, Mathf.Max(0f, Vector2.Dot(nj, sd)));
            if (ghost) { wi = wj = inkMin; }
            int b = vh.currentVertCount;
            // rings: outer feather, outer, inner, inner feather
            vh.AddVert(pts[i] + ni * (wi * 0.5f + 0.9f), inkClear, Vector2.zero);
            vh.AddVert(pts[i] + ni * (wi * 0.5f), inkC, Vector2.zero);
            vh.AddVert(pts[i] - ni * (wi * 0.5f), inkC, Vector2.zero);
            vh.AddVert(pts[i] - ni * (wi * 0.5f + 0.9f), inkClear, Vector2.zero);
            vh.AddVert(pts[j] + nj * (wj * 0.5f + 0.9f), inkClear, Vector2.zero);
            vh.AddVert(pts[j] + nj * (wj * 0.5f), inkC, Vector2.zero);
            vh.AddVert(pts[j] - nj * (wj * 0.5f), inkC, Vector2.zero);
            vh.AddVert(pts[j] - nj * (wj * 0.5f + 0.9f), inkClear, Vector2.zero);
            for (int q = 0; q < 3; q++) { vh.AddTriangle(b + q, b + q + 1, b + 4 + q + 1); vh.AddTriangle(b + q, b + 4 + q + 1, b + 4 + q); }
        }
    }

    Vector2 SafeNormal(int i, int m)
    {
        Vector2 t = (pts[(i + 1) % m] - pts[(i - 1 + m) % m]);
        if (t.sqrMagnitude < 1e-6f) return Vector2.up;
        t.Normalize();
        return new Vector2(t.y, -t.x);
    }

#if UNITY_EDITOR
    protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif
}
