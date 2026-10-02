using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One rigid paper shape of the title screen's exit (SPEC v4 §7b, driven by <see cref="MenuLeave"/>): a convex polygon in local
/// canvas units, drawn as <see cref="Mode.Fill"/> (the captured menu frame, mapped by where each point sat on the canvas when the
/// frame was taken; drawn with the DeaCube/MenuPanel shader, which ignores the capture's alpha), <see cref="Mode.Solid"/> (flat
/// colour: the hard offset shadow, the paper fallback) or <see cref="Mode.Ink"/> (a thick hand-inked border lying inside the edges:
/// seeded wobble on its inner side, heavier on the shadow side, an optional thin paper line inside it; <see cref="Boil"/> redraws
/// the wobble per drawing). The RectTransform carries the pose (pivot = the local origin), so moving a panel never rebuilds a mesh.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class MenuPanel : MaskableGraphic
{
    public enum Mode { Fill, Solid, Ink }

    Mode mode = Mode.Solid;
    readonly List<Vector2> poly = new List<Vector2>(12);
    Texture tex;
    Vector2 uvOrigin, uvSize = Vector2.one;
    bool flipV;
    float inkMin = 8f, inkMax = 12f, wobble = 1.6f, paperLine;
    Color paper = Color.white;
    Vector2 shadowDir = new Vector2(0.6f, -0.8f);
    int seed = 1, boil;

    public Mode Kind => mode;
    public int PointCount => poly.Count;
    public Vector2 Point(int i) => poly[i];
    public override Texture mainTexture => mode == Mode.Fill && tex != null ? tex : s_WhiteTexture;

    public static MenuPanel Create(Transform parent, string name, Mode mode, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        var p = go.AddComponent<MenuPanel>();
        p.mode = mode; p.color = c; p.raycastTarget = false;
        return p;
    }

    /// <summary>The outline (convex, counter-clockwise, local units).</summary>
    public void SetPolygon(IList<Vector2> pts)
    {
        poly.Clear();
        for (int i = 0; i < pts.Count; i++) poly.Add(pts[i]);
        SetVerticesDirty();
    }

    /// <summary>Fill: <paramref name="t"/> maps local point p to uv (p - <paramref name="origin"/>) / <paramref name="size"/> (v flipped when
    /// <paramref name="flip"/>): origin = the canvas' bottom-left corner in this panel's local space at the moment of the capture.</summary>
    public void SetTexture(Texture t, Vector2 origin, Vector2 size, bool flip)
    {
        tex = t; uvOrigin = origin; uvSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y)); flipV = flip;
        SetMaterialDirty(); SetVerticesDirty();
    }

    /// <summary>Ink: stroke width on the lit side and on the shadow side, inner-edge wobble (px), a paper line of <paramref name="paperPx"/>
    /// inside the ink (0 = none), the seed of the wobble.</summary>
    public void SetInk(float min, float max, float wobblePx, float paperPx, Color paperColour, int inkSeed)
    {
        inkMin = min; inkMax = max; wobble = wobblePx; paperLine = paperPx; paper = paperColour; seed = inkSeed;
        SetVerticesDirty();
    }

    /// <summary>The wobble's phase (a new drawing of the same line); rebuilds only when it changes.</summary>
    public int Boil { get => boil; set { if (boil != value) { boil = value; if (mode == Mode.Ink && wobble > 0f) SetVerticesDirty(); } } }

    static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u; h = (h ^ (uint)c) * 16777619u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h >> 8) / 16777216f * 2f - 1f;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = poly.Count;
        if (n < 3) return;
        if (mode == Mode.Ink) { Ink(vh); return; }
        Color32 c = color;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = poly[i];
            Vector2 uv = Vector2.zero;
            if (mode == Mode.Fill)
            {
                uv = new Vector2((p.x - uvOrigin.x) / uvSize.x, (p.y - uvOrigin.y) / uvSize.y);
                if (flipV) uv.y = 1f - uv.y;
            }
            vh.AddVert(p, c, uv);
        }
        for (int i = 1; i < n - 1; i++) vh.AddTriangle(0, i, i + 1);
    }

    /// <summary>The border: per edge a strip from the edge inward (so neighbouring panels' borders meet back to back in the gutter),
    /// sampled every ~22 px; its inner side wobbles and it is heavier where the edge faces the shadow.</summary>
    void Ink(VertexHelper vh)
    {
        int n = poly.Count;
        Color32 ink = color, inkClear = new Color(color.r, color.g, color.b, 0f);
        Color32 pap = paper, papClear = new Color(paper.r, paper.g, paper.b, 0f);
        Vector2 sd = shadowDir.normalized;
        for (int e = 0; e < n; e++)
        {
            Vector2 a = poly[e], b = poly[(e + 1) % n];
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 0.5f) continue;
            d /= len;
            Vector2 inward = new Vector2(-d.y, d.x);                 // CCW polygon: the interior is on the left
            float side = Mathf.Max(0f, Vector2.Dot(-inward, sd));
            float w0 = Mathf.Lerp(inkMin, inkMax, side);
            int segs = Mathf.Clamp(Mathf.CeilToInt(len / 22f), 1, 90);
            int baseV = vh.currentVertCount;
            for (int s = 0; s <= segs; s++)
            {
                float u = s / (float)segs;
                Vector2 p = a + d * (len * u);
                // the wobble fades out at the corners so the strips of two edges meet cleanly
                float taper = Mathf.Clamp01(Mathf.Min(u, 1f - u) * len / 30f);
                float wob = wobble * taper * (0.65f * Hash(seed, e * 131 + s, boil) + 0.35f * Hash(seed + 7, e * 57 + s / 2, boil));
                float w = Mathf.Max(1.5f, w0 + wob);
                vh.AddVert(p - inward * 0.8f, inkClear, Vector2.zero);   // feather outside the edge
                vh.AddVert(p, ink, Vector2.zero);
                vh.AddVert(p + inward * w, ink, Vector2.zero);
                vh.AddVert(p + inward * (w + 0.9f), inkClear, Vector2.zero);
            }
            for (int s = 0; s < segs; s++)
            {
                int i0 = baseV + s * 4, i1 = i0 + 4;
                for (int q = 0; q < 3; q++) { vh.AddTriangle(i0 + q, i1 + q, i1 + q + 1); vh.AddTriangle(i0 + q, i1 + q + 1, i0 + q + 1); }
            }
            if (paperLine <= 0f) continue;
            // a thin paper line just inside the ink: the cut edge of the printed page
            baseV = vh.currentVertCount;
            for (int s = 0; s <= segs; s++)
            {
                float u = s / (float)segs;
                Vector2 p = a + d * (len * u);
                float taper = Mathf.Clamp01(Mathf.Min(u, 1f - u) * len / 30f);
                float wob = wobble * taper * (0.65f * Hash(seed, e * 131 + s, boil) + 0.35f * Hash(seed + 7, e * 57 + s / 2, boil));
                float w = Mathf.Max(1.5f, w0 + wob) + 0.4f;
                vh.AddVert(p + inward * w, pap, Vector2.zero);
                vh.AddVert(p + inward * (w + paperLine), pap, Vector2.zero);
                vh.AddVert(p + inward * (w + paperLine + 0.8f), papClear, Vector2.zero);
            }
            for (int s = 0; s < segs; s++)
            {
                int i0 = baseV + s * 3, i1 = i0 + 3;
                for (int q = 0; q < 2; q++) { vh.AddTriangle(i0 + q, i1 + q, i1 + q + 1); vh.AddTriangle(i0 + q, i1 + q + 1, i0 + q + 1); }
            }
        }
    }
}
