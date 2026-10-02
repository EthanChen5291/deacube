using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builders shared by the v4 world HUD widgets (scratchpad/v4/UI.md, research §4.1): nodes placed by anchor, sticker controls (an InkShape
/// or an InkPainter as the hit area + a HudButton for the events + InkHover for the look + an InkCaption), bare hit areas for painted
/// pictures, nested canvases that keep per-frame movers (the playhead cube, the needle, the beat cubes, the island header) from re-batching
/// the whole HUD, and lowercase Fredoka labels. Every control built here is captioned (research §4.10: words on demand).
/// </summary>
public static class HudKit
{
    public static RectTransform Node(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    public static RectTransform Stretch(Transform parent, string name)
    {
        var rt = Node(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>A nested canvas on <paramref name="go"/> (own batch: movers inside it never re-batch the HUD); with a raycaster when it
    /// holds controls.</summary>
    public static Canvas SubCanvas(GameObject go, bool raycast)
    {
        var c = go.GetComponent<Canvas>(); if (c == null) c = go.AddComponent<Canvas>();
        if (raycast && go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
        return c;
    }

    /// <summary>A control whose hit area is <paramref name="hit"/> (a raycast graphic on the same object): adds the HudButton events,
    /// the hover look and the caption.</summary>
    public static HudButton Control(Graphic hit, string caption, Action onClick, InkShape body = null, RectTransform content = null, params InkPainter[] painters)
    {
        hit.raycastTarget = true;
        var b = hit.gameObject.GetComponent<HudButton>();
        if (b == null) b = hit.gameObject.AddComponent<HudButton>();
        b.bg = null; b.icon = null; b.shape = null; b.animateScale = false;
        b.onClick = onClick;
        InkHover.Attach(b, body, content, painters);
        if (!string.IsNullOrEmpty(caption)) InkCaption.Attach(hit.gameObject, caption);
        return b;
    }

    /// <summary>A transparent raycast rect (the hit area of a painted picture).</summary>
    public static Image Hit(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rt = Node(parent, name, anchor, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f); img.raycastTarget = true;
        return img;
    }

    /// <summary>A cream sticker (InkShape, ink outline, hard ink shadow) with a painter on top for its picture; the sticker is the hit area.</summary>
    public static HudButton Sticker(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, InkShape.Kind kind, Color fill,
                                    Action<InkPainter> picture, string caption, Action onClick, out InkShape body, out InkPainter art)
    {
        var rt = Node(parent, name, anchor, pos, size);
        body = rt.gameObject.AddComponent<InkShape>();
        body.Shape = kind; body.color = fill; body.SetInk(2.2f, 3.4f); body.ShadowOffset = Comic.StickerShadow; body.Radius = Mathf.Min(size.x, size.y) * 0.3f;
        if (kind == InkShape.Kind.Sticker) body.RotJitter = 3f;
        var content = Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, size);
        art = content.gameObject.AddComponent<InkPainter>();
        art.raycastTarget = false; art.onPaint = picture;
        return Control(body, caption, onClick, body, content, art);
    }

    /// <summary>Lowercase Fredoka words (menus, captions): ink on paper, no outline material.</summary>
    public static TextMeshProUGUI Words(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var rt = Node(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, size * 1.4f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = Comic.Font; t.fontSharedMaterial = Comic.Font != null ? Comic.Font.material : null;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    /// <summary>Stepped (on twos) 0..1 progress toward <paramref name="target"/> at <paramref name="seconds"/> per full swing.</summary>
    public static float Toward(float v, float target, float seconds)
    {
        return Mathf.MoveTowards(v, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds));
    }

    /// <summary>The current "on twos" drawing index (12 per second): UI tweens update only when it changes (research §3 rule 8).</summary>
    public static int TwosTick => Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps);

    public static void SetActive(Component c, bool on) { if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

    /// <summary>Screen px → local point of <paramref name="rt"/> (overlay canvas).</summary>
    public static bool Local(RectTransform rt, Vector2 screen, out Vector2 lp) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out lp);
}

/// <summary>Scroll-wheel glue: forwards vertical scroll steps (trackpad fractions accumulate; sideways swipes ignored) and a commit
/// <see cref="HudDial.ScrollCommitDelay"/> s after the last event (one History entry per scroll gesture, F2).</summary>
public class HudScroll : MonoBehaviour, IScrollHandler
{
    public Action<float> onStep;
    public Action onCommit;
    public float stepPerUnit = 1f;
    float acc, lastT = -9f; bool pending;
    public bool Pending => pending;
    public float LastScrollTime => lastT;

    public void OnScroll(PointerEventData e)
    {
        Vector2 d = e.scrollDelta;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return;
        if (Time.unscaledTime - lastT > HudDial.ScrollCommitDelay) acc = 0f;
        lastT = Time.unscaledTime;
        acc += d.y * stepPerUnit;
        int steps = (int)acc;
        if (steps == 0) return;
        acc -= steps;
        pending = true;
        onStep?.Invoke(steps);
    }

    void Update() { if (pending && Time.unscaledTime - lastT >= HudDial.ScrollCommitDelay) Flush(); }
    void OnDisable() { Flush(); }
    void Flush() { if (!pending) return; pending = false; acc = 0f; onCommit?.Invoke(); }
}

/// <summary>Press-drag glue for painted controls (tongues, faders): local-point callbacks, one release.</summary>
public class HudDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public Action<Vector2> onPoint;
    public Action onRelease;
    bool pressed;
    public bool Pressed => pressed;
    RectTransform Rt => (RectTransform)transform;

    public void OnPointerDown(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left) return; pressed = true; Feed(e); }
    public void OnDrag(PointerEventData e) { if (pressed) Feed(e); }
    public void OnPointerUp(PointerEventData e) { if (!pressed) return; pressed = false; onRelease?.Invoke(); }
    void OnDisable() { if (pressed) { pressed = false; onRelease?.Invoke(); } }
    void Feed(PointerEventData e) { Vector2 lp; if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Rt, e.position, e.pressEventCamera, out lp)) onPoint?.Invoke(lp); }
    /// <summary>Tests: a synthetic press / drag at a local point (release with <see cref="SimRelease"/>).</summary>
    public void SimAt(Vector2 local) { pressed = true; onPoint?.Invoke(local); }
    public void SimRelease() { if (!pressed) return; pressed = false; onRelease?.Invoke(); }
}

/// <summary>Pointer-inside flag for a painted area that is not a control (the voice fan's paper): the owner polls <see cref="Inside"/>.</summary>
public class HudHoverZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool Inside { get; private set; }
    public void OnPointerEnter(PointerEventData e) { Inside = true; }
    public void OnPointerExit(PointerEventData e) { Inside = false; }
    void OnDisable() { Inside = false; }
    /// <summary>Tests: the pointer enters / leaves.</summary>
    public void Sim(bool inside) { Inside = inside; }
}

/// <summary>
/// v6 (SPEC v6 §2.7 / §8.1, package U2): the variant mark of a voice inside an instrument group — ink pips on a mini cube's top face, laid
/// out like a die (voice 0, the group's first sound, is plain; 1–9 pips on a 3 × 3 lattice; 10–12 on a 4 × 3 one). Pictures, not digits. Shared
/// by the instrument column (the group's brush voice on its chip), the voice fan, the cube card's sound popover, the cube riding the cursor
/// and the clipboard chip, so one voice always wears the same mark. Geometry = CubeGlyph / InkPainter.Cube: hexagon height = size, the top
/// face spans mid (the centre) → upper-left / upper-right vertices.
/// </summary>
public static class VoiceMark
{
    static readonly List<Vector2> face = new List<Vector2>(12);
    static readonly float[] L3 = { 0.24f, 0.5f, 0.76f }, L4 = { 0.2f, 0.4f, 0.6f, 0.8f };
    // die layouts on the 3 × 3 lattice (i along the face's left edge, j along its right edge): 1..9 pips
    static readonly int[][] Die =
    {
        new int[0],
        new[] { 1, 1 },
        new[] { 0, 2, 2, 0 },
        new[] { 0, 2, 1, 1, 2, 0 },
        new[] { 0, 0, 0, 2, 2, 0, 2, 2 },
        new[] { 0, 0, 0, 2, 2, 0, 2, 2, 1, 1 },
        new[] { 0, 0, 0, 1, 0, 2, 2, 0, 2, 1, 2, 2 },
        new[] { 0, 0, 0, 1, 0, 2, 2, 0, 2, 1, 2, 2, 1, 1 },
        new[] { 0, 0, 0, 1, 0, 2, 1, 0, 1, 2, 2, 0, 2, 1, 2, 2 },
        new[] { 0, 0, 0, 1, 0, 2, 1, 0, 1, 1, 1, 2, 2, 0, 2, 1, 2, 2 },
    };

    /// <summary>Face coordinates (0..1 along the top face's left and right edges from the centre vertex) of voice <paramref name="voice"/>'s
    /// pips (voice 0: none). Returns the count.</summary>
    public static int Pips(int voice, List<Vector2> into)
    {
        into.Clear();
        if (voice <= 0) return 0;
        if (voice <= 9)
        {
            var d = Die[voice];
            for (int k = 0; k + 1 < d.Length; k += 2) into.Add(new Vector2(L3[d[k]], L3[d[k + 1]]));
            return into.Count;
        }
        int n = Mathf.Min(voice, 12);
        for (int i = 0; i < 4 && into.Count < n; i++)
            for (int j = 0; j < 3 && into.Count < n; j++) into.Add(new Vector2(L4[i], L3[j]));
        return into.Count;
    }

    /// <summary>The top-face point at face coordinates <paramref name="f"/> of a cube centred at <paramref name="c"/> with hexagon height
    /// <paramref name="size"/> (a CubeGlyph squash of <paramref name="squash"/> applied like the glyph's hop).</summary>
    public static Vector2 FacePoint(Vector2 c, float size, Vector2 f, float squash = 1f)
    {
        float R = size * 0.5f, hx = R * 0.8660254f;
        Vector2 v = new Vector2(-hx * f.x + hx * f.y, R * 0.5f * (f.x + f.y));
        float sq = Mathf.Sqrt(Mathf.Max(0.1f, squash));
        return c + new Vector2(v.x / sq, v.y * squash);
    }

    /// <summary>Paints voice <paramref name="voice"/>'s pips onto the top face of a cube drawn at <paramref name="c"/> / <paramref name="size"/>.</summary>
    public static void Paint(InkPainter p, Vector2 c, float size, int voice, Color ink, float squash = 1f)
    {
        int n = Pips(voice, face);
        if (n == 0) return;
        float r = Mathf.Max(1.4f, size * (n >= 7 ? 0.043f : (n >= 5 ? 0.05f : 0.058f)));
        for (int k = 0; k < n; k++) p.Disc(FacePoint(c, size, face[k], squash), r, ink);
    }
}
