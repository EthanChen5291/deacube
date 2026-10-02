using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md): the hand-drawn wavy line — the selection underline of text items and the wavy divider (Melatonin). A
/// sine polyline across the rect's width at its vertical centre; its phase steps at 8 fps while <see cref="Animate"/> is on.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class InkWave : MaskableGraphic
{
    [SerializeField] float amplitude = 2f, wavelength = 11f, thickness = 2.5f;
    [SerializeField] bool animate = true;
    int frame = -1;

    public float Amplitude { get => amplitude; set { if (amplitude != value) { amplitude = value; SetVerticesDirty(); } } }
    public float Wavelength { get => wavelength; set { value = Mathf.Max(2f, value); if (wavelength != value) { wavelength = value; SetVerticesDirty(); } } }
    public float Thickness { get => thickness; set { if (thickness != value) { thickness = value; SetVerticesDirty(); } } }
    public bool Animate { get => animate; set { animate = value; SetVerticesDirty(); } }

    public static InkWave Create(Transform parent, string name, Color c, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var w = go.AddComponent<InkWave>();
        w.color = c; w.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = size;
        return w;
    }

    void Update()
    {
        if (!animate) return;
        int f = Mathf.FloorToInt(Time.unscaledTime * 8f);
        if (f != frame) { frame = f; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 2f) return;
        float phase = animate ? Mathf.Floor(Time.unscaledTime * 8f) * 0.9f : 0f;
        int n = Mathf.Clamp(Mathf.CeilToInt(r.width / 2.5f), 4, 400);
        Color32 on = color, off = new Color(color.r, color.g, color.b, 0f);
        float h = thickness * 0.5f;
        Vector2 prevN = Vector2.up;
        for (int i = 0; i <= n; i++)
        {
            float x = Mathf.Lerp(r.xMin, r.xMax, i / (float)n);
            float k = Mathf.PI * 2f / wavelength;
            float y = r.center.y + Mathf.Sin(x * k + phase) * amplitude;
            float dy = Mathf.Cos(x * k + phase) * amplitude * k;
            Vector2 nrm = new Vector2(-dy, 1f).normalized;
            // taper the ends so the stroke reads as drawn
            float taper = Mathf.Clamp01(Mathf.Min(i, n - i) / 3f);
            float hh = h * Mathf.Lerp(0.45f, 1f, taper);
            Vector2 p = new Vector2(x, y);
            vh.AddVert(p + nrm * (hh + 0.8f), off, Vector2.zero);
            vh.AddVert(p + nrm * hh, on, Vector2.zero);
            vh.AddVert(p - nrm * hh, on, Vector2.zero);
            vh.AddVert(p - nrm * (hh + 0.8f), off, Vector2.zero);
            if (i > 0)
            {
                int b = (i - 1) * 4, c = i * 4;
                for (int q = 0; q < 3; q++) { vh.AddTriangle(b + q, b + q + 1, c + q + 1); vh.AddTriangle(b + q, c + q + 1, c + q); }
            }
            prevN = nrm;
        }
    }
}
