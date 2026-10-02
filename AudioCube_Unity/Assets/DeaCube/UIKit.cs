using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>Helpers for building the word-free HUD from code (drawn in the comic style of <see cref="Comic"/>).</summary>
public static class UIKit
{
    /// <summary>The UI font (Fredoka; LiberationSans when the comic fonts are not installed).</summary>
    public static TMP_FontAsset Font => Comic.Font;

    public static GameObject Obj(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }
    public static RectTransform RT(GameObject go) => (RectTransform)go.transform;
    public static RectTransform RT(Component c) => (RectTransform)c.transform;

    /// <summary>The glyph name when IconFactory has it, else <paramref name="fallback"/> (never the placeholder disc).</summary>
    public static string Icon(string name, string fallback) => IconFactory.Has(name) ? name : fallback;

    public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }
    public static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }
    public static LayoutElement Fixed(GameObject go, float w, float h)
    {
        var le = go.GetComponent<LayoutElement>(); if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredWidth = w; le.preferredHeight = h; le.minWidth = w; le.minHeight = h;
        return le;
    }

    public static Image Image(Transform parent, string name, string sprite, Color color, Vector2 size, bool raycast = false)
    {
        var go = Obj(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = IconFactory.Get(sprite);
        img.color = color; img.raycastTarget = raycast; img.preserveAspect = sprite != "white";   // a solid "white" rect fills its rect (lines, bars); glyphs keep their aspect
        RT(go).sizeDelta = size;
        return img;
    }

    /// <summary>A comic panel (flat fill, ink border, hard shadow); a dark glass tint becomes indigo, a light one paper cream.</summary>
    public static Image Panel(Transform parent, string name, Vector2 size, Color color, float radius = 16f, bool raycast = true)
    {
        var go = Obj(name, parent);
        RT(go).sizeDelta = size;
        return Comic.Panel(go, Comic.PanelFill(color), Comic.PanelRadius, raycast);
    }
    /// <summary>A flat capsule (no ink).</summary>
    public static Image Pill(Transform parent, string name, Vector2 size, Color color, bool raycast = false)
    {
        return Comic.Shape(parent, name, size, color, 0f, 0f, false, raycast);
    }
    /// <summary>A flat rounded square (no ink; set <see cref="ComicShape.border"/> for one).</summary>
    public static Image Square(Transform parent, string name, Vector2 size, Color color, float radius = 12f, bool raycast = false)
    {
        return Comic.Shape(parent, name, size, color, Mathf.Max(1f, radius), 0f, false, raycast);
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = Obj(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = Font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        Comic.StyleText(t);   // light lettering gets the ink outline + hard shadow
        return t;
    }

    /// <summary>Builds the button shell — a comic disc (or a mini flat cube for <paramref name="bgSprite"/> "square") with an ink
    /// ring and a hard shadow, and an inked glyph — sized for layouts, and adds a <typeparamref name="T"/> behaviour.</summary>
    public static T Make<T>(Transform parent, string name, string icon, float size, float iconScale = 0.52f, string bgSprite = "dot") where T : HudButton
    {
        var go = Obj(name, parent);
        RT(go).sizeDelta = new Vector2(size, size);
        Fixed(go, size, size);
        var shape = go.AddComponent<ComicShape>();
        Comic.SetupButton(shape, size, bgSprite == "square");
        float glyph = size * iconScale;
        var ic = Comic.GlyphImage(go.transform, "Icon", icon, Palette.Icon, glyph, true);
        var b = go.AddComponent<T>();
        b.bg = shape; b.shape = shape; b.icon = ic; b.baseSize = size; b.iconName = icon; b.iconPx = glyph;
        if (shape.cube > 0f) { b.iconOffset = new Vector2(0f, size * shape.cube * 0.5f); ic.rectTransform.anchoredPosition = b.iconOffset; }
        return b;
    }

    public static HudButton Button(Transform parent, string name, string icon, float size, Action onClick, float iconScale = 0.52f, string bgSprite = "dot")
    {
        var b = Make<HudButton>(parent, name, icon, size, iconScale, bgSprite);
        b.onClick = onClick;
        return b;
    }

    /// <summary>A press-and-hold button (punch-ins, spotlight): onHoldChanged(true) on press, (false) on release / exit / disable.</summary>
    public static HoldButton Hold(Transform parent, string name, string icon, float size, Action<bool> onHoldChanged, float iconScale = 0.52f, string bgSprite = "dot")
    {
        var b = Make<HoldButton>(parent, name, icon, size, iconScale, bgSprite);
        b.onHoldChanged = onHoldChanged;
        return b;
    }

    /// <summary>A button that also steps a value when dragged (energy: click cycles, drag sets).</summary>
    public static DragButton Drag(Transform parent, string name, string icon, float size, Action onClick, Action<int> onDragStep, float iconScale = 0.52f, string bgSprite = "dot")
    {
        var b = Make<DragButton>(parent, name, icon, size, iconScale, bgSprite);
        b.onClick = onClick; b.onDragStep = onDragStep;
        return b;
    }

    /// <summary>A radial ring around a button showing a 0..1 level (multi-state cycles: shadow, echo, energy): a flat band with
    /// inked edges just outside the disc.</summary>
    public static Image LevelRing(HudButton b, Color color, float sizePx)
    {
        float s = Mathf.Max(sizePx, b.baseSize * 1.34f);
        return BandRing(b.transform, "Level", color, s);
    }
    public static void SetLevel(Image ring, float t01) { if (ring != null) ring.fillAmount = Mathf.Clamp01(t01); }

    /// <summary>A hold-progress ring for a HudButton with onHold (fills over holdSeconds), outside the level ring.</summary>
    public static Image HoldRing(HudButton b, Color color, float sizePx)
    {
        float s = Mathf.Max(sizePx, b.baseSize * 1.56f);
        var ring = BandRing(b.transform, "Hold", color, s);
        b.holdFill = ring;
        return ring;
    }

    /// <summary>A radial-filled comic band ring (inked edges, flat colour) of <paramref name="size"/> px, centred, empty.</summary>
    public static Image BandRing(Transform parent, string name, Color color, float size)
    {
        var go = Obj(name, parent);
        var ring = go.AddComponent<Image>();
        ring.sprite = Comic.RingSprite; ring.color = color; ring.raycastTarget = false; ring.preserveAspect = true;
        Anchor(ring.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        ring.type = UnityEngine.UI.Image.Type.Filled; ring.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; ring.fillOrigin = 2; ring.fillClockwise = true;
        ring.fillAmount = 0f;
        return ring;
    }

    public static HorizontalLayoutGroup Row(Transform parent, string name, float spacing, int pad = 8, TextAnchor align = TextAnchor.MiddleCenter, bool fit = true)
    {
        var go = Obj(name, parent);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing; h.padding = new RectOffset(pad, pad, pad, pad); h.childAlignment = align;
        h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        if (fit) { var f = go.AddComponent<ContentSizeFitter>(); f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; f.verticalFit = ContentSizeFitter.FitMode.PreferredSize; }
        return h;
    }
    public static VerticalLayoutGroup Column(Transform parent, string name, float spacing, int pad = 8, TextAnchor align = TextAnchor.MiddleCenter, bool fit = true)
    {
        var go = Obj(name, parent);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing; v.padding = new RectOffset(pad, pad, pad, pad); v.childAlignment = align;
        v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
        if (fit) { var f = go.AddComponent<ContentSizeFitter>(); f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; f.verticalFit = ContentSizeFitter.FitMode.PreferredSize; }
        return v;
    }
    /// <summary>Fixed-cell grid (2 x 5 swatch blocks).</summary>
    public static GridLayoutGroup Grid(Transform parent, string name, Vector2 cell, Vector2 spacing, int columns, int pad = 0)
    {
        var go = Obj(name, parent);
        var g = go.AddComponent<GridLayoutGroup>();
        g.cellSize = cell; g.spacing = spacing; g.padding = new RectOffset(pad, pad, pad, pad);
        g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = Mathf.Max(1, columns);
        g.childAlignment = TextAnchor.MiddleCenter;
        var f = go.AddComponent<ContentSizeFitter>(); f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return g;
    }
    public static GameObject Spacer(Transform parent, float w, float h)
    {
        var go = Obj("Spacer", parent);
        RT(go).sizeDelta = new Vector2(w, h);
        Fixed(go, w, h);
        return go;
    }

    /// <summary>Screen point of a HUD element's centre (overlay canvas).</summary>
    public static Vector2 ScreenOf(Component c) => RectTransformUtility.WorldToScreenPoint(null, c.transform.position);

    // ---- panel styling in one place (the HUD bars, the inspector card, the path-grid plate): a restyle touches only these
    /// <summary>The inspector card's surface: paper cream (panels given this colour take the light theme).</summary>
    public static readonly Color CardGlass = Comic.Cream;
    /// <summary>A comic panel on <paramref name="go"/> (HUD rows, the inspector card): flat fill (the dark glass → dusk indigo at
    /// 94 %, <see cref="CardGlass"/> → paper cream), 3 px ink border, 14 px corners, hard ink shadow (+5, −6).</summary>
    public static UnityEngine.UI.Image Glass(GameObject go, float radius = 22f, Color? color = null)
    {
        return Comic.Panel(go, Comic.PanelFill(color ?? Palette.Glass), Comic.PanelRadius, true);
    }
    /// <summary>The rounded plate under the flat path grid (legacy Image form; the grid uses <see cref="PlateShape"/>).</summary>
    public static void Plate(UnityEngine.UI.Image img, float radius = 16f)
    {
        img.sprite = IconFactory.Get("square"); img.type = UnityEngine.UI.Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 20f / Mathf.Max(3f, radius); img.raycastTarget = true;
    }
    /// <summary>The plate under the flat path grid: flat island tint, 3 px ink border, 12 px corners, no shadow (it sits in the card).</summary>
    public static ComicShape PlateShape(GameObject go)
    {
        var s = go.AddComponent<ComicShape>();
        s.kind = ComicShape.Kind.Rect; s.radius = 12f; s.border = 3f; s.shadowColor = Comic.A(Comic.Ink, 0f);
        s.raycastTarget = true;
        return s;
    }

    /// <summary>A plain rectangle (segments, dividers, dashes): the white sprite, no aspect lock.</summary>
    public static Image Rect(Transform parent, string name, Color color, Vector2 size)
    {
        var img = Image(parent, name, "white", color, size);
        img.preserveAspect = false;
        return img;
    }
}

/// <summary>
/// Hotkey reads the tests can drive (legacy Input cannot be faked): Down / Held / Up are true for the real key or for a key a
/// test simulates for one dispatch (<see cref="Sim"/> then the owner's hotkey method, then <see cref="Clear"/>).
/// Used by the package-A hotkey owners (PathManager, UIManager, CubeInspector).
/// </summary>
public static class KeyShim
{
    static readonly HashSet<KeyCode> down = new HashSet<KeyCode>(), held = new HashSet<KeyCode>(), up = new HashSet<KeyCode>();
    public static bool Down(KeyCode k) => Input.GetKeyDown(k) || (down.Count > 0 && down.Contains(k));
    public static bool Held(KeyCode k) => Input.GetKey(k) || (held.Count > 0 && held.Contains(k));
    public static bool Up(KeyCode k) => Input.GetKeyUp(k) || (up.Count > 0 && up.Contains(k));
    /// <summary>Simulates <paramref name="k"/>: pressed this dispatch (isDown), held (isHeld), released (isUp).</summary>
    public static void Sim(KeyCode k, bool isDown, bool isHeld, bool isUp)
    {
        if (isDown) down.Add(k); else down.Remove(k);
        if (isHeld) held.Add(k); else held.Remove(k);
        if (isUp) up.Add(k); else up.Remove(k);
    }
    public static void Clear() { down.Clear(); held.Clear(); up.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Clear(); }
}

/// <summary>
/// Icon button with hover / press / toggle feedback, optional hold-to-confirm (a click still fires when released before the hold
/// completes). Comic look (SPEC v3 §7 HUD): a flat disc or mini cube with an ink ring and a hard shadow; the glyph carries an
/// ink outline; hover grows it to <see cref="hoverScale"/> and boils the ring, drawn on twos (12 fps, <see cref="Look.Stepped(float)"/>);
/// a press slides the body onto its shadow and it pops back on release; toggled = the accent fill under a halftone screen.
/// Legacy tints (translucent <see cref="bgColor"/>) map to opaque fills of the surface the control sits on (indigo or paper).
/// </summary>
public class HudButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public Image bg, icon, holdFill;
    public Action onClick, onHold, onRightClick, onHover;
    public float holdSeconds = 0.8f;
    public bool toggled, interactable = true;
    public Color accent = Palette.Accent;
    public Color bgColor = Palette.GlassLight;
    public Color iconColor = Palette.Icon;
    public Color toggledIconColor = new Color(0.08f, 0.07f, 0.14f);
    public float pulse;
    public float baseSize;
    public float hoverScale = 1.08f;
    /// <summary>Scale applied while something else (the world) points at this control (pill <-> island hover link).</summary>
    public bool externalHover; public float externalHoverScale = 1.15f;
    public bool animateScale = true;
    /// <summary>The comic body (disc / mini cube); null for custom backgrounds (chord wedges) and bg-less controls (pills).</summary>
    public ComicShape shape;
    /// <summary>The glyph shown and its drawn size (px): the inked sprite is re-picked when the glyph turns light or dark.</summary>
    public string iconName; public float iconPx;
    /// <summary>Where the glyph sits on the body (a mini cube carries it on its top face).</summary>
    public Vector2 iconOffset;

    float scale = 1f, shownScale = -1f; bool hover, down; float holdT; bool holdFired;
    bool themeResolved, light, inked = true, redraw = true; int tick = int.MinValue; float press; string spriteOf;

    public bool Hover => hover;
    public bool Down => down;
    /// <summary>The control sits on a light (paper) surface: paper disc, ink glyph (from the nearest ComicShape above it).</summary>
    public bool Light { get { ResolveTheme(); return light; } }
    /// <summary>Re-reads the surface theme (after moving the control to another panel).</summary>
    public void RefreshTheme() { themeResolved = false; redraw = true; }

    void ResolveTheme()
    {
        if (themeResolved) return;
        themeResolved = true;
        var p = transform.parent != null ? transform.parent.GetComponentInParent<ComicShape>() : null;
        light = p != null && p.light;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        ResolveTheme();
        float target = !interactable ? 1f : (down ? 0.97f : (externalHover ? externalHoverScale : (hover ? hoverScale : 1f)));
        scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-dt * 20f));
        pulse *= Mathf.Exp(-dt * 9f);
        press = down && interactable ? 1f : Mathf.MoveTowards(press, 0f, dt * 9f);   // snaps onto the shadow, pops back in ~2 drawings
        if (!redraw)
        {
            if (!Look.OnTwos) redraw = true;
            else { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t != tick) { tick = t; redraw = true; } }
        }
        if (redraw) { redraw = false; Draw(); }
        if (onHold != null)
        {
            if (down && interactable)
            {
                holdT += dt;
                if (holdFill != null) holdFill.fillAmount = holdT / holdSeconds;
                if (holdT >= holdSeconds)
                {
                    holdT = 0f; down = false; holdFired = true; redraw = true;
                    if (holdFill != null) holdFill.fillAmount = 0f;
                    pulse = 1f;
                    AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
                    onHold();
                }
            }
            else
            {
                holdT = 0f;
                if (holdFill != null) holdFill.fillAmount = Mathf.MoveTowards(holdFill.fillAmount, 0f, dt * 4f);
            }
        }
    }

    /// <summary>One drawing (on twos): scale, body fill / halftone / press / ring boil, glyph colour and outline.</summary>
    void Draw()
    {
        bool hot = hover && interactable;
        float sc = scale * (1f + pulse * 0.18f);
        if (animateScale && Mathf.Abs(sc - shownScale) > 0.0005f) { shownScale = sc; transform.localScale = new Vector3(sc, sc, 1f); }
        Vector2 body = Vector2.zero;
        if (shape != null)
        {
            bool bare;
            Color fill = Comic.ButtonFill(bgColor, light, out bare);
            if (toggled) { fill = Comic.Solid(accent, light); bare = false; }
            if (hot && !bare) fill = Color.Lerp(fill, Color.white, light ? 0.45f : 0.16f);
            if (!interactable && !bare) fill = Color.Lerp(fill, light ? Comic.CreamDisc : Comic.IndigoDisc, 0.6f);
            if (!bare) body = shape.shadowOffset * press;
            Vector2 jit = Vector2.zero; float js = 1f;
            if (hot && !bare) { jit = new Vector2(UnityEngine.Random.Range(-0.7f, 0.7f), UnityEngine.Random.Range(-0.7f, 0.7f)); js = 1f + UnityEngine.Random.Range(-0.01f, 0.02f); }
            shape.SetLook(fill, bare, toggled ? 0.3f : 0f, body, jit, js);
        }
        else if (bg != null)
        {
            Color b = toggled ? Comic.Solid(accent, false) : (bgColor.a > 0.02f ? Comic.Opaque(bgColor) : bgColor);
            if (hot) b = Color.Lerp(b, Color.white, 0.22f);
            if (bg.color != b) bg.color = b;
        }
        if (icon != null)
        {
            Color ic = Comic.IconColor(toggled ? toggledIconColor : iconColor, light);
            if (!interactable) ic = new Color(ic.r, ic.g, ic.b, ic.a * 0.38f);
            if (icon.color != ic) icon.color = ic;
            if (iconName != null)
            {
                bool want = Comic.WantsOutline(ic);
                if (want != inked || spriteOf != iconName) { inked = want; spriteOf = iconName; icon.sprite = Comic.Glyph(iconName, iconPx, want); }
                Vector2 at = iconOffset + body;
                if (icon.rectTransform.anchoredPosition != at) icon.rectTransform.anchoredPosition = at;
            }
        }
    }

    public virtual void OnPointerEnter(PointerEventData e) { hover = true; redraw = true; if (interactable) onHover?.Invoke(); }
    public virtual void OnPointerExit(PointerEventData e) { hover = false; down = false; redraw = true; }
    public virtual void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { down = true; holdFired = false; redraw = true; } }
    public virtual void OnPointerUp(PointerEventData e) { down = false; redraw = true; }
    public virtual void OnPointerClick(PointerEventData e)
    {
        if (!interactable) return;
        if (e.button == PointerEventData.InputButton.Right) { onRightClick?.Invoke(); return; }
        if (e.button != PointerEventData.InputButton.Left) return;
        if (holdFired) { holdFired = false; return; }   // the hold already acted on this press
        if (onClick == null && onHold != null) return;  // hold-only control: a short tap does nothing
        pulse = 1f; redraw = true;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f);
        onClick?.Invoke();
    }
    public void SetIcon(string name)
    {
        if (icon == null) return;
        if (iconName != null) { iconName = name; spriteOf = name; icon.sprite = Comic.Glyph(name, iconPx, inked); redraw = true; }
        else icon.sprite = IconFactory.Get(name);
    }
}

/// <summary>Drag up/down (or scroll) to change a value; shows a ring fill and an optional number. F2: a trackpad scroll accumulates
/// (one step per whole unit, sideways swipes ignored) and ends in one onRelease ~0.35 s after the last scroll event.</summary>
public class HudDial : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>F2: seconds without a scroll event before a scroll's change is committed (one History entry per scroll gesture).</summary>
    public const float ScrollCommitDelay = 0.35f;
    public float min = 40f, max = 240f, value = 100f;
    public Action<float> onChanged;
    public Action onTap;
    public Action onRelease;
    public Func<float, string> format;
    public Image fill, ring;
    public TextMeshProUGUI label;
    public float pixelsForFullRange = 300f;
    public bool integer = true;
    public float pulse;
    /// <summary>Track ring colour at rest / while hovered or dragged (the comic dials keep an ink track that warms up).</summary>
    public Color ringColor = Palette.A(Color.white, 0.18f), ringHoverColor = Palette.A(Color.white, 0.45f);
    bool dragging; float scale = 1f, shownScale = -1f; bool hover; int tick = int.MinValue;
    float scrollAcc, lastScrollT = -9f; bool scrollPending;
    /// <summary>F2: a scroll change waits for its commit (tests).</summary>
    public bool ScrollPending => scrollPending;
    /// <summary>v4: a drag is in progress (the tempo digits show their range arc meanwhile).</summary>
    public bool Dragging => dragging;
    public bool Hover => hover;

    public void Set(float v, bool notify)
    {
        value = Mathf.Clamp(v, min, max);
        Refresh();
        if (notify) onChanged?.Invoke(value);
    }
    void Refresh()
    {
        if (fill != null) fill.fillAmount = Mathf.InverseLerp(min, max, value);
        if (label != null) label.text = format != null ? format(value) : (integer ? Mathf.RoundToInt(value).ToString() : "");
    }
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (scrollPending && Time.unscaledTime - lastScrollT >= ScrollCommitDelay) FlushScroll();
        pulse *= Mathf.Exp(-dt * 8f);
        scale = Mathf.Lerp(scale, hover || dragging ? 1.06f : 1f, 1f - Mathf.Exp(-dt * 16f));
        if (Look.OnTwos) { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t == tick) return; tick = t; }   // drawn on twos
        float sc = scale * (1f + pulse * 0.1f);
        if (Mathf.Abs(sc - shownScale) > 0.0005f) { shownScale = sc; transform.localScale = new Vector3(sc, sc, 1f); }
        if (ring != null) { var c = hover || dragging ? ringHoverColor : ringColor; if (ring.color != c) ring.color = c; }
    }
    public void OnBeginDrag(PointerEventData e) { dragging = true; }
    public void OnDrag(PointerEventData e) { float d = (e.delta.y + e.delta.x * 0.4f) / pixelsForFullRange * (max - min); Set(value + d, true); }
    public void OnEndDrag(PointerEventData e) { dragging = false; onRelease?.Invoke(); }
    public void OnScroll(PointerEventData e)
    {
        Vector2 d = e.scrollDelta;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return;                       // F2: a sideways swipe is not a turn of the dial
        if (Time.unscaledTime - lastScrollT > ScrollCommitDelay) scrollAcc = 0f;
        lastScrollT = Time.unscaledTime;
        scrollAcc += d.y;
        int steps = (int)scrollAcc;                                          // whole units only: a trackpad sends fractions
        if (steps == 0) return;
        scrollAcc -= steps;
        float before = value;
        Set(value + steps * (integer ? 1f : (max - min) * 0.02f), true);
        if (value != before) scrollPending = true;
    }
    void FlushScroll() { if (!scrollPending) return; scrollPending = false; scrollAcc = 0f; onRelease?.Invoke(); }
    /// <summary>S5: a scroll or a drag cut short (the panel closed) still commits what it changed.</summary>
    void OnDisable() { FlushScroll(); if (dragging) { dragging = false; onRelease?.Invoke(); } }
    public void OnPointerClick(PointerEventData e) { if (dragging || e.button != PointerEventData.InputButton.Left) return; pulse = 1f; onTap?.Invoke(); }
    public void OnPointerEnter(PointerEventData e) { hover = true; }
    public void OnPointerExit(PointerEventData e) { hover = false; }
}

/// <summary>Minimal slider: click or drag along the track.</summary>
public class HudSlider : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    public bool vertical = true;
    public float value = 1f;
    public Action<float> onChanged;
    public Action onRelease;
    public RectTransform track;
    public Image fill, knob, bg;
    public Color color = Palette.Accent;
    bool hover, themeResolved, light, pressed;
    const float Ink = 2f;

    public void Set(float v, bool notify)
    {
        value = Mathf.Clamp01(v);
        Refresh();
        if (notify) onChanged?.Invoke(value);
    }
    public void Refresh()
    {
        if (fill != null)
        {
            var rt = fill.rectTransform;
            var tr = track != null ? track.rect : new Rect(0f, 0f, 1f, 1f);
            if (fill is ComicShape)
            {
                // the flat fill grows inside the ink border from the start of the track
                float len = Mathf.Max(0f, (vertical ? tr.height : tr.width) - 2f * Ink) * value;
                rt.pivot = new Vector2(0f, 0f);
                if (vertical) { rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.offsetMin = new Vector2(Ink, Ink); rt.offsetMax = new Vector2(-Ink, Ink + len); }
                else { rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f); rt.offsetMin = new Vector2(Ink, Ink); rt.offsetMax = new Vector2(Ink + len, -Ink); }
                fill.enabled = len > 1f;
            }
            else fill.fillAmount = value;
            fill.color = new Color(color.r, color.g, color.b, 1f);
        }
        if (knob != null)
        {
            var rt = knob.rectTransform;
            rt.anchorMin = rt.anchorMax = vertical ? new Vector2(0.5f, value) : new Vector2(value, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            if (!(knob is ComicShape)) knob.color = Color.Lerp(color, Color.white, 0.5f);
        }
    }
    void Update()
    {
        if (!themeResolved)
        {
            themeResolved = true;
            var p = transform.parent != null ? transform.parent.GetComponentInParent<ComicShape>() : null;
            light = p != null && p.light;
        }
        if (bg != null)
        {
            Color c = bg is ComicShape ? (light ? (hover ? Comic.CreamDisc : Comic.CreamDeep) : (hover ? Comic.IndigoDisc : Comic.IndigoDeep))
                                      : (hover ? Palette.A(Color.white, 0.22f) : Palette.A(Color.white, 0.1f));
            if (bg.color != c) bg.color = c;
        }
    }
    void FromPointer(PointerEventData e)
    {
        if (track == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var lp);
        var r = track.rect;
        float v = vertical ? Mathf.InverseLerp(r.yMin, r.yMax, lp.y) : Mathf.InverseLerp(r.xMin, r.xMax, lp.x);
        Set(v, true);
    }
    public void OnPointerDown(PointerEventData e) { pressed = true; FromPointer(e); }
    public void OnPointerUp(PointerEventData e) { pressed = false; onRelease?.Invoke(); }
    /// <summary>S5: the panel closed mid-drag (no pointer up will come): the change is still committed.</summary>
    void OnDisable() { if (pressed) { pressed = false; onRelease?.Invoke(); } }
    public void OnDrag(PointerEventData e) { FromPointer(e); }
    public void OnPointerEnter(PointerEventData e) { hover = true; }
    public void OnPointerExit(PointerEventData e) { hover = false; }

    public static HudSlider Create(Transform parent, string name, bool vertical, Vector2 size, Color color, float value, Action<float> onChanged)
    {
        var go = UIKit.Obj(name, parent);
        UIKit.RT(go).sizeDelta = size;
        UIKit.Fixed(go, size.x, size.y);
        var hit = go.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0.001f); hit.raycastTarget = true;
        var s = go.AddComponent<HudSlider>();
        s.vertical = vertical; s.color = color; s.onChanged = onChanged;
        // an inked capsule track, the flat colour fill inside its border and a paper knob with an ink ring and a hard shadow
        const float thick = 12f;
        var track = Comic.Shape(go.transform, "Track", Vector2.zero, Comic.IndigoDeep, 0f, Ink);
        var trt = track.rectTransform;
        if (vertical) UIKit.Stretch(trt, size.x * 0.5f - thick * 0.5f, size.x * 0.5f - thick * 0.5f, 5f, 5f); else UIKit.Stretch(trt, 5f, 5f, size.y * 0.5f - thick * 0.5f, size.y * 0.5f - thick * 0.5f);
        s.track = trt; s.bg = track;
        var fill = Comic.Shape(track.transform, "Fill", Vector2.zero, color, 0f, 0f);
        s.fill = fill;
        var knob = Comic.Shape(track.transform, "Knob", new Vector2(17f, 17f), Comic.Cream, 0f, 2f, true);
        knob.shadowColor = Comic.Shadow; knob.shadowOffset = new Vector2(1.5f, -2f);
        UIKit.Anchor(knob.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(17f, 17f));
        s.knob = knob;
        s.Set(value, false);
        return s;
    }
}

/// <summary>
/// Hold button: raises onHoldChanged(true) on press and (false) on release / pointer exit / disable (punch-ins, spotlight).
/// A tap makes no click sound and fires nothing else: the hold is the whole gesture.
/// </summary>
public class HoldButton : HudButton
{
    public Action<bool> onHoldChanged;
    public bool Holding => holding;
    bool holding;

    public override void OnPointerDown(PointerEventData e)
    {
        base.OnPointerDown(e);
        if (e.button != PointerEventData.InputButton.Left || !interactable || holding) return;
        holding = true;
        pulse = 0.6f;
        onHoldChanged?.Invoke(true);
    }
    public override void OnPointerUp(PointerEventData e) { base.OnPointerUp(e); Release(); }
    public override void OnPointerExit(PointerEventData e) { base.OnPointerExit(e); Release(); }
    public override void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Right) onRightClick?.Invoke(); }
    void OnDisable() { Release(); }
    void Release() { if (!holding) return; holding = false; onHoldChanged?.Invoke(false); }
}

/// <summary>A HudButton that also steps a value while dragged (every <see cref="pixelsPerStep"/> px up/right = +1). A drag suppresses the click.</summary>
public class DragButton : HudButton, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Action<int> onDragStep;
    public Action onDragEnd;
    public float pixelsPerStep = 18f;
    float acc; bool stepped, dragging;

    public void OnBeginDrag(PointerEventData e) { acc = 0f; stepped = false; dragging = true; }
    public void OnDrag(PointerEventData e)
    {
        if (!interactable) return;
        acc += e.delta.y + e.delta.x * 0.5f;
        while (acc >= pixelsPerStep) { acc -= pixelsPerStep; stepped = true; onDragStep?.Invoke(+1); }
        while (acc <= -pixelsPerStep) { acc += pixelsPerStep; stepped = true; onDragStep?.Invoke(-1); }
    }
    public void OnEndDrag(PointerEventData e) { if (stepped) onDragEnd?.Invoke(); stepped = false; dragging = false; }
    /// <summary>The press and the drag land on the same object, so Unity still sends the click on release (before OnEndDrag): a drag swallows it.</summary>
    public override void OnPointerClick(PointerEventData e) { if (dragging || stepped || e.dragging) return; base.OnPointerClick(e); }
}

/// <summary>
/// Necklace dial (SPEC §3.1 #1-3): n beads on a ring of radius <see cref="radiusPx"/> (bead size clamp(220/n, 6, 14) px),
/// filled = hit in the cube colour, hollow otherwise, a bar notch at bead <c>rot</c>. Drag on the dial = hits (onHits),
/// drag on the notch = rotate (onRot; in explicit-mask mode the mask turns with it: onMask), click a bead = onBeadToggle(j),
/// scroll = hits +-1; onRelease after every drag/scroll (the owner pushes History). Set() rebuilds the beads when n changes.
/// F2: a scroll accumulates (one step per whole unit, sideways swipes ignored) and raises one onRelease ~0.35 s after its last event;
/// S5: a drag or scroll cut short by the card closing still raises it.
/// </summary>
public class NecklaceDial : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IScrollHandler
{
    public int n = 4, hits = -1, rot, mask;
    public Color color = Palette.Accent;
    public Action<int> onHits, onRot, onBeadToggle;
    /// <summary>Raised with the rotated mask when the notch is dragged while hits == -2 (explicit mask).</summary>
    public Action<int> onMask;
    public Action onRelease;
    public float radiusPx = 34f;
    /// <summary>Bead lit by the playhead (-1 none); set by the owner each frame.</summary>
    public int playStep = -1;

    readonly List<Image> beads = new List<Image>();
    Image notch, guide;
    RectTransform rt;
    int builtN = -1; float beadSize = 14f; bool built;

    enum DragMode { None, Hits, Rot }
    DragMode drag; int pressBead = -1; bool pressNotch, dragged, dirty; int startHits; float dragAcc;
    float scrollAcc, lastScrollT = -9f; bool scrollPending;
    /// <summary>F2: a scroll change waits for its commit (tests).</summary>
    public bool ScrollPending => scrollPending;

    static readonly Color Hollow = Comic.Cream;

    /// <summary>Creates a dial GameObject (square of 2 * (radius + 20) px) with its hit area.</summary>
    public static NecklaceDial Create(Transform parent, string name, float radiusPx = 34f)
    {
        var go = UIKit.Obj(name, parent);
        float size = 2f * (radiusPx + 20f);
        UIKit.RT(go).sizeDelta = new Vector2(size, size);
        UIKit.Fixed(go, size, size);
        var d = go.AddComponent<NecklaceDial>();
        d.radiusPx = radiusPx;
        d.EnsureBuilt();
        return d;
    }

    void Awake() { EnsureBuilt(); }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        rt = (RectTransform)transform;
        if (rt.sizeDelta.sqrMagnitude < 1f) { float s = 2f * (radiusPx + 20f); rt.sizeDelta = new Vector2(s, s); }
        var hit = GetComponent<Image>();
        if (hit == null) hit = gameObject.AddComponent<Image>();
        hit.sprite = IconFactory.Get("dot"); hit.color = new Color(1f, 1f, 1f, 0.001f); hit.raycastTarget = true;
        float ringPx = radiusPx / 0.82f * 2f;   // the ringThin glyph sits at 0.82 of its half-size
        guide = UIKit.Image(transform, "Guide", "ringThin", Palette.A(Comic.Ink, 0.75f), new Vector2(ringPx, ringPx));
        UIKit.Anchor(guide.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ringPx, ringPx));
        notch = Comic.Shape(transform, "Notch", new Vector2(6f, 15f), Comic.Pop, 0f, 1.6f);
        UIKit.Anchor(notch.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 15f));
        Rebuild();
        Redraw();
    }

    /// <summary>Sets the model (n = StepsPerBar, hits/rot/mask as on the cube); notify raises onHits/onRot with the new values.</summary>
    public void Set(int n, int hits, int rot, int mask, bool notify)
    {
        EnsureBuilt();
        this.n = Mathf.Clamp(n, 1, 24); this.hits = hits; this.rot = rot; this.mask = mask;
        if (builtN != this.n) Rebuild();
        Redraw();
        // callbacks may re-enter Set() (cube setter -> OnAnyChanged -> panel refresh), so notify with the values as given
        int h = this.hits, r = this.rot;
        if (notify) { onHits?.Invoke(h); onRot?.Invoke(r); }
    }
    /// <summary>The current per-bar pattern (Rhythm.Pattern) for drawing the beads.</summary>
    public bool[] Pattern() => Rhythm.Pattern(hits, rot, mask, n);
    /// <summary>Hits as a count 1..n (-1 = n, -2 = the mask's popcount).</summary>
    public int EffectiveHits
    {
        get
        {
            if (hits == -1) return n;
            if (hits == -2) { int c = 0; var p = Pattern(); for (int j = 0; j < p.Length; j++) if (p[j]) c++; return Mathf.Max(0, c); }
            return Mathf.Clamp(hits, 1, n);
        }
    }

    Vector2 BeadPos(int j) { float a = (90f - j * 360f / Mathf.Max(1, n)) * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radiusPx; }
    Vector2 NotchPos() { float a = (90f - rot * 360f / Mathf.Max(1, n)) * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radiusPx + beadSize * 0.5f + 7f); }
    int IndexAtAngle(Vector2 lp)
    {
        if (lp.sqrMagnitude < 1f) return rot;
        float deg = Mathf.Atan2(lp.y, lp.x) * Mathf.Rad2Deg;   // 90 = top
        float t = Mathf.Repeat(90f - deg, 360f) / 360f * n;
        return Mathf.RoundToInt(t) % n;
    }
    int BeadAt(Vector2 lp)
    {
        int best = -1; float bd = beadSize * 0.5f + 5f;
        for (int j = 0; j < n; j++) { float d = Vector2.Distance(lp, BeadPos(j)); if (d < bd) { bd = d; best = j; } }
        return best;
    }

    void Rebuild()
    {
        foreach (var b in beads) if (b != null) Destroy(b.gameObject);
        beads.Clear();
        beadSize = Mathf.Clamp(220f / Mathf.Max(1, n), 6f, 14f);
        for (int j = 0; j < n; j++)
        {
            var b = Comic.Shape(transform, "Bead" + j, new Vector2(beadSize, beadSize), color, 0f, beadSize >= 11f ? 2f : 1.5f, true);
            UIKit.Anchor(b.rectTransform, new Vector2(0.5f, 0.5f), BeadPos(j), new Vector2(beadSize, beadSize));
            beads.Add(b);
        }
        if (notch != null) notch.transform.SetAsLastSibling();
        builtN = n;
    }

    void Redraw()
    {
        var p = Pattern();
        for (int j = 0; j < beads.Count && j < p.Length; j++)
        {
            var c = p[j] ? new Color(color.r, color.g, color.b, 1f) : Hollow;
            if (beads[j].color != c) beads[j].color = c;
        }
        if (notch != null)
        {
            var np = NotchPos();
            notch.rectTransform.anchoredPosition = np;
            notch.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(np.y, np.x) * Mathf.Rad2Deg - 90f);
            notch.color = hits == -2 ? Comic.Cream : Comic.Pop;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (scrollPending && Time.unscaledTime - lastScrollT >= HudDial.ScrollCommitDelay) FlushScroll();
        for (int j = 0; j < beads.Count; j++)
        {
            float target = j == playStep ? 1.45f : 1f;
            var t = beads[j].rectTransform;
            float s = Mathf.Lerp(t.localScale.x, target, 1f - Mathf.Exp(-dt * 22f));
            if (Mathf.Abs(s - t.localScale.x) > 0.001f) t.localScale = Vector3.one * s;
        }
    }

    /// <summary>The explicit mask turned by <paramref name="d"/> steps (bit j moves to (j + d) mod n): the notch drag and the [ ] hotkeys.</summary>
    public static int RotateMask(int m, int d, int n)
    {
        int r = 0;
        for (int j = 0; j < n && j < 31; j++) if (((m >> j) & 1) != 0) r |= 1 << ((((j + d) % n) + n) % n);
        return r;
    }

    void ApplyHits(int want)
    {
        want = Mathf.Clamp(want, 1, n);
        if (want == EffectiveHits && hits != -2) return;
        int newHits = want >= n ? -1 : want;
        hits = newHits;
        Redraw();
        dirty = true;
        onHits?.Invoke(newHits);
        AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 1.3f);
    }
    void ApplyRot(int idx)
    {
        idx = ((idx % n) + n) % n;
        if (idx == rot) return;
        int d = idx - rot;
        int newRot = idx, newMask = mask; bool maskMode = hits == -2;
        if (maskMode) newMask = RotateMask(mask, d, n);
        rot = newRot; mask = newMask;
        Redraw();
        dirty = true;
        onRot?.Invoke(newRot);                    // re-entrant refreshes re-run Set(); the locals keep this gesture's values
        if (maskMode) onMask?.Invoke(newMask);
        AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.9f);
    }

    bool Local(PointerEventData e, out Vector2 lp) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out lp);

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        Vector2 lp; if (!Local(e, out lp)) return;
        dragged = false;
        pressNotch = Vector2.Distance(lp, NotchPos()) < 11f;
        pressBead = pressNotch ? -1 : BeadAt(lp);
    }
    public void OnPointerUp(PointerEventData e) { }
    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        dragged = true; dirty = false; dragAcc = 0f; startHits = EffectiveHits;
        drag = pressNotch ? DragMode.Rot : DragMode.Hits;
    }
    public void OnDrag(PointerEventData e)
    {
        if (drag == DragMode.None) return;
        if (drag == DragMode.Rot)
        {
            Vector2 lp; if (Local(e, out lp)) ApplyRot(IndexAtAngle(lp));
        }
        else
        {
            dragAcc += e.delta.y + e.delta.x * 0.6f;
            int want = Mathf.Clamp(startHits + Mathf.RoundToInt(dragAcc / 14f), 1, n);
            if (want != EffectiveHits) ApplyHits(want);
        }
    }
    public void OnEndDrag(PointerEventData e)
    {
        drag = DragMode.None;
        if (dirty) onRelease?.Invoke();
        dirty = false;
    }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || dragged) return;
        if (pressBead < 0) return;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f);
        onBeadToggle?.Invoke(pressBead);
    }
    public void OnScroll(PointerEventData e)
    {
        Vector2 d = e.scrollDelta;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return;                       // F2: a sideways swipe is not a turn of the dial
        if (Time.unscaledTime - lastScrollT > HudDial.ScrollCommitDelay) scrollAcc = 0f;
        lastScrollT = Time.unscaledTime;
        scrollAcc += d.y;
        int steps = (int)scrollAcc;                                          // whole units only: a trackpad sends fractions
        if (steps == 0) return;
        scrollAcc -= steps;
        int want = Mathf.Clamp(EffectiveHits + steps, 1, n);
        if (want == EffectiveHits) return;
        bool wasDirty = dirty;
        ApplyHits(want);
        dirty = wasDirty;                                                    // a drag's own flag; the scroll commits on its timer
        scrollPending = true;
    }
    void FlushScroll() { if (!scrollPending) return; scrollPending = false; scrollAcc = 0f; onRelease?.Invoke(); }
    /// <summary>S5: the card closed mid-gesture: a pending scroll or a drag's change is still committed (one History entry).</summary>
    void OnDisable()
    {
        bool pushed = scrollPending;
        FlushScroll();
        if (drag != DragMode.None && dirty && !pushed) onRelease?.Invoke();
        drag = DragMode.None; dirty = false;
    }
}

/// <summary>
/// World-anchored ring of icon buttons (sticker ring, twin variants): buttons at radius <see cref="radiusPx"/> around a
/// world point projected with Camera.WorldToScreenPoint every LateUpdate (or a fixed screen point), clamped to the safe
/// area, faded when the anchor is off screen or behind the camera; closes on click-away or Esc.
/// </summary>
public class PopRing : MonoBehaviour
{
    public Vector3 worldPos;
    public string[] icons;
    public Action<int> onPick;
    public Action onClosed;
    public float radiusPx = 34f;
    public float buttonSize = 30f;
    public bool screenAnchored; public Vector2 screenPos;
    public readonly List<HudButton> Buttons = new List<HudButton>();
    public bool Closing => closing;

    RectTransform rt, root; CanvasGroup group; Camera cam; Image centre;
    float shown, age, ringR, targetAlpha = 1f; bool closing, built;

    public static PopRing Open(Transform hudRoot, Vector3 worldPos, string[] icons, Action<int> onPick, float radiusPx)
    {
        var go = UIKit.Obj("PopRing", hudRoot);
        var r = go.AddComponent<PopRing>();
        r.worldPos = worldPos; r.icons = icons ?? new string[0]; r.onPick = onPick; r.radiusPx = radiusPx;
        r.Build();
        return r;
    }
    /// <summary>A ring anchored to a HUD point (sub-rings of HUD buttons).</summary>
    public static PopRing OpenAtScreen(Transform hudRoot, Vector2 screenPos, string[] icons, Action<int> onPick, float radiusPx)
    {
        var go = UIKit.Obj("PopRing", hudRoot);
        var r = go.AddComponent<PopRing>();
        r.screenAnchored = true; r.screenPos = screenPos; r.icons = icons ?? new string[0]; r.onPick = onPick; r.radiusPx = radiusPx;
        r.Build();
        return r;
    }

    void Build()
    {
        if (built) return;
        built = true;
        rt = (RectTransform)transform; root = transform.parent as RectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = Vector2.zero;
        group = gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f;
        int count = icons.Length;
        ringR = Mathf.Max(radiusPx, count * (buttonSize + 4f) / (2f * Mathf.PI));
        centre = Comic.Shape(transform, "Centre", new Vector2(11f, 11f), Comic.Cream, 0f, 2f, true);
        UIKit.Anchor(centre.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            var b = UIKit.Button(transform, "Pick" + i, icons[i], buttonSize, () => Pick(idx), 0.62f);
            float a = (90f - i * 360f / Mathf.Max(1, count)) * Mathf.Deg2Rad;
            UIKit.Anchor(UIKit.RT(b.gameObject), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR, new Vector2(buttonSize, buttonSize));
            Buttons.Add(b);
        }
        transform.localScale = Vector3.one * 0.6f;
        Place();
    }

    /// <summary>Marks entry i as the current choice.</summary>
    public void SetToggled(int i, bool on) { if (i >= 0 && i < Buttons.Count) Buttons[i].toggled = on; }

    /// <summary>Picks entry i (raises onPick) and closes.</summary>
    public void Pick(int i) { if (closing) return; var cb = onPick; Close(); cb?.Invoke(i); }
    public void Close()
    {
        if (closing) return;
        closing = true;
        onClosed?.Invoke();
        Destroy(gameObject);
    }

    bool Contains(Vector2 screen)
    {
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out lp)) return false;
        return lp.magnitude <= ringR + buttonSize * 0.75f;
    }

    void Update()
    {
        if (closing) return;
        float dt = Time.unscaledDeltaTime;
        age += dt;
        shown = Mathf.Lerp(shown, 1f, 1f - Mathf.Exp(-dt * 16f));
        transform.localScale = Vector3.one * (0.6f + 0.4f * Ease.OutBack(Mathf.Clamp01(age / 0.22f)));
        group.alpha = Mathf.Lerp(group.alpha, targetAlpha * shown, 1f - Mathf.Exp(-dt * 18f));
        bool on = group.alpha > 0.5f;
        group.interactable = on; group.blocksRaycasts = on;
        if (!InputUtil.TypingInField && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (age > 0.08f && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !Contains(Input.mousePosition)) Close();
    }

    void LateUpdate() { if (!closing) Place(); }

    void Place()
    {
        if (root == null) return;
        Vector2 sp; bool visible = true;
        if (screenAnchored) sp = screenPos;
        else
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var w = cam.WorldToScreenPoint(worldPos);
            sp = new Vector2(w.x, w.y);
            visible = w.z > 0f && w.x > -40f && w.x < Screen.width + 40f && w.y > -40f && w.y < Screen.height + 40f;
        }
        targetAlpha = visible ? 1f : 0f;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out lp)) return;
        var r = root.rect;
        var sa = Screen.safeArea;
        Vector2 saMin, saMax;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sa.min, null, out saMin) && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sa.max, null, out saMax))
        {
            r.xMin = Mathf.Max(r.xMin, saMin.x); r.yMin = Mathf.Max(r.yMin, saMin.y);
            r.xMax = Mathf.Min(r.xMax, saMax.x); r.yMax = Mathf.Min(r.yMax, saMax.y);
        }
        float m = ringR + buttonSize * 0.5f + 6f;
        lp.x = Mathf.Clamp(lp.x, r.xMin + m, r.xMax - m);
        lp.y = Mathf.Clamp(lp.y, r.yMin + m, r.yMax - m);
        rt.anchoredPosition = lp;
    }
}
