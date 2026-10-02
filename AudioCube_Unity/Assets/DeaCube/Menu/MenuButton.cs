using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A title-menu item. v4 (SPEC §7b, ui_research §4.7): <see cref="CreateText"/> — a lowercase word in Fredoka on the wall, no box
/// (Melatonin): paper cream with a hard print shadow; the focused item is drawn, not filled — a wavy ink underline that draws itself
/// left to right in three drawings and a small nudge right; hover focuses; a press squashes the word; drawn on twos. The v3 pill
/// (<see cref="Create"/>: icon + word on a comic capsule) is kept for compatibility. The menu listens to <see cref="onHover"/> /
/// <see cref="onPress"/> to light and hop the wall behind the item; <see cref="Squash"/> is the leave's anticipation.
/// The root keeps its layout slot; only the inner body moves.
/// </summary>
public class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public Action onClick, onHover, onPress;
    public RectTransform body;
    public Image fill, border, glow, icon;
    public TextMeshProUGUI label;
    /// <summary>Text style: the wavy underline of the focused item (null for pills).</summary>
    public InkWave underline;
    public bool primary, interactable = true;
    public int index;

    bool hover, down, focused, squashed, textStyle; float h, d, pulse, shakeT = 9f, busyT = -1f;
    Color fillBase, fillHover, labelBase;
    ComicShape pill; string iconName; float iconPx; int tick = int.MinValue;
    float wordPx, bigPx, ulDrawn; Vector2 pressAt; float pressT = -9f;
    static readonly Vector2 ShadowOffset = new Vector2(5f, -6f);
    static readonly float[] UnderlineSteps = { 0f, 0.45f, 0.8f, 1f };
    /// <summary>The leave's anticipation pose: wide and short, like a word pressed flat.</summary>
    static readonly Vector3 SquashScale = new Vector3(1.2f, 0.72f, 1f);
    static Material printMat;

    public bool Hover => hover;
    public float HoverAmount => h;
    public bool IsText => textStyle;
    public bool Focused => focused;
    public bool Squashed => squashed;
    /// <summary>Fraction of the focus underline drawn (0 = none).</summary>
    public float UnderlineDrawn => textStyle && underline != null && underline.gameObject.activeSelf ? UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)] : 0f;
    public Vector2 ScreenCenter
    {
        get
        {
            if (textStyle && label != null)
            {
                var lr = label.rectTransform;
                return RectTransformUtility.WorldToScreenPoint(null, lr.TransformPoint(lr.rect.center));
            }
            return RectTransformUtility.WorldToScreenPoint(null, (body != null ? body : (RectTransform)transform).position);
        }
    }
    /// <summary>Where the item was last pressed with the pointer (within the last second), else its centre: the leave's impact point.</summary>
    public Vector2 PressPoint => Time.unscaledTime - pressT < 1f ? pressAt : ScreenCenter;

    // ------------------------------------------------------------------ v4 text item
    /// <summary>A lowercase menu word of <paramref name="px"/> (reference 1080p units; <paramref name="primaryPx"/> while primary).</summary>
    public static MenuButton CreateText(Transform parent, string name, string word, float px, float primaryPx, bool primary)
    {
        var root = UIKit.Obj(name, parent);
        var b = root.AddComponent<MenuButton>();
        b.textStyle = true; b.primary = primary;
        b.wordPx = px; b.bigPx = Mathf.Max(px, primaryPx);
        var hit = root.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true;
        var bodyGo = UIKit.Obj("Body", root.transform);
        b.body = UIKit.RT(bodyGo);
        UIKit.Stretch(b.body);
        b.body.pivot = new Vector2(0.3f, 0.35f);
        b.label = UIKit.Text(bodyGo.transform, "Label", word.ToLowerInvariant(), px, Comic.Cream, TextAlignmentOptions.MidlineLeft);
        b.label.fontStyle = FontStyles.Normal;
        b.label.characterSpacing = 1.5f;
        var print = PrintMaterial();
        if (print != null) b.label.fontSharedMaterial = print;
        b.underline = InkWave.Create(bodyGo.transform, "Underline", Comic.Cream, new Vector2(10f, 12f));
        b.underline.Amplitude = 2.4f; b.underline.Wavelength = 13f;
        b.underline.gameObject.SetActive(false);
        b.Layout();
        return b;
    }

    /// <summary>The words' ink: paper cream letters with a thin ink outline and a hard print-violet shadow (no blur) — the comic
    /// underlay on the near-black wall, where an ink shadow would vanish.</summary>
    static Material PrintMaterial()
    {
        if (printMat != null) return printMat;
        var baseMat = Comic.InkMaterial(Comic.Font);
        if (baseMat == null) return null;
        printMat = new Material(baseMat) { name = "MenuWord Print (runtime)" };
        printMat.SetColor("_UnderlayColor", Comic.PrintShadow);
        printMat.SetFloat("_UnderlayOffsetX", 0.55f); printMat.SetFloat("_UnderlayOffsetY", -0.7f);
        printMat.SetFloat("_UnderlayDilate", 0.2f);
        printMat.SetFloat("_OutlineWidth", 0.18f);
        return printMat;
    }

    void Layout()
    {
        if (!textStyle || label == null) return;
        float px = primary ? bigPx : wordPx;
        label.fontSize = px;
        float w = label.GetPreferredValues(label.text, 2000f, 400f).x;
        float hgt = Mathf.Round(px + 20f);   // an even rhythm between words of different sizes
        var lr = label.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, 0.5f);
        lr.anchoredPosition = new Vector2(0f, px * 0.1f); lr.sizeDelta = new Vector2(w + 8f, hgt);
        var ur = underline.rectTransform;
        ur.anchorMin = ur.anchorMax = ur.pivot = new Vector2(0f, 0.5f);
        ur.anchoredPosition = new Vector2(-2f, -px * 0.46f);
        ur.sizeDelta = new Vector2(w + 6f, 12f);
        underline.Thickness = Mathf.Lerp(2.4f, 3.4f, Mathf.InverseLerp(26f, 44f, px));
        UIKit.RT(gameObject).sizeDelta = new Vector2(w + 30f, hgt);
        UIKit.Fixed(gameObject, w + 30f, hgt);
    }

    // ------------------------------------------------------------------ v3 pill (kept)
    public static MenuButton Create(Transform parent, string name, string iconName, string word, bool primary, float height = 66f)
    {
        var root = UIKit.Obj(name, parent);
        var b = root.AddComponent<MenuButton>();
        b.primary = primary;
        // hit area on the root (so the lift never makes the button flicker under the pointer)
        var hit = root.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true;
        // the drawn pill: flat fill, ink border, a hard ink shadow that stays put while the body lifts / presses
        var pillGo = UIKit.Obj("Pill", root.transform);
        UIKit.Stretch(UIKit.RT(pillGo));
        b.pill = pillGo.AddComponent<ComicShape>();
        b.pill.kind = ComicShape.Kind.Rect; b.pill.radius = 0f; b.pill.border = 3f;
        b.pill.shadowColor = Comic.PrintShadow; b.pill.shadowOffset = ShadowOffset; b.pill.raycastTarget = false;
        b.fill = b.pill;
        var bodyGo = UIKit.Obj("Body", root.transform);
        b.body = UIKit.RT(bodyGo);
        UIKit.Stretch(b.body);
        float iconSize = height * 0.42f;
        b.iconName = iconName; b.iconPx = iconSize;
        b.icon = UIKit.Image(bodyGo.transform, "Icon", iconName, Color.white, new Vector2(iconSize * Comic.GlyphPad, iconSize * Comic.GlyphPad));
        b.label = UIKit.Text(bodyGo.transform, "Label", word, 25f, Color.white, TextAlignmentOptions.MidlineLeft);
        b.label.fontStyle = FontStyles.Bold;
        b.label.characterSpacing = 6f;
        float textW = b.label.GetPreferredValues(word, 1000f, 100f).x;
        float padL = 24f, gap = 12f, padR = 30f;
        float width = padL + iconSize + gap + textW + padR;
        UIKit.Anchor(b.icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(padL + iconSize * 0.5f, 0f), new Vector2(iconSize * Comic.GlyphPad, iconSize * Comic.GlyphPad));
        b.icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        UIKit.Anchor(b.label.rectTransform, new Vector2(0f, 0.5f), new Vector2(padL + iconSize + gap, 0f), new Vector2(textW + 8f, height));
        b.label.rectTransform.pivot = new Vector2(0f, 0.5f);
        UIKit.RT(root).sizeDelta = new Vector2(width, height);
        UIKit.Fixed(root, width, height);
        b.ApplyStyle();
        return b;
    }

    void ApplyStyle()
    {
        if (textStyle) { Layout(); return; }
        if (primary)
        {
            fillBase = new Color(0.80f, 0.72f, 1f, 1f); fillHover = new Color(0.89f, 0.84f, 1f, 1f);
            labelBase = Comic.Ink;
        }
        else
        {
            fillBase = Comic.Cream; fillHover = Color.white;   // the tutorial balloons' paper
            labelBase = Comic.Ink;
        }
        fill.color = fillBase;
        label.color = labelBase; icon.color = labelBase;
        Comic.StyleText(label);   // ink lettering plain, paper lettering inked (outline + hard shadow)
        if (iconName != null) icon.sprite = Comic.Glyph(iconName, iconPx, false);
        if (border != null) border.color = primary ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.24f);
    }

    // ------------------------------------------------------------------ control
    public void SetPrimary(bool p) { if (primary == p) return; primary = p; ApplyStyle(); }
    /// <summary>Text style: the item the keyboard and the pointer point at (underline + nudge); the menu keeps exactly one focused.</summary>
    public void SetFocused(bool f)
    {
        if (focused == f) return;
        focused = f;
        if (!f) ulDrawn = 0f;
        tick = int.MinValue;
    }
    public void Shake() { shakeT = 0f; }
    /// <summary>Busy (e.g. generating): the icon (the word) pulses until cleared.</summary>
    public void SetBusy(bool b) { busyT = b ? 0f : -1f; }
    public void Invoke() { if (!interactable) return; pulse = 1f; onPress?.Invoke(); onClick?.Invoke(); }
    /// <summary>The leave's anticipation: the word squashes at once (wide and short) and stays so until <see cref="Unsquash"/>.</summary>
    public void Squash()
    {
        squashed = true;
        if (body != null) body.localScale = SquashScale;
        if (pill != null) pill.rectTransform.localScale = body.localScale;
    }
    public void Unsquash() { squashed = false; tick = int.MinValue; }

    public void OnPointerEnter(PointerEventData e) { hover = true; if (interactable) onHover?.Invoke(); }
    public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || !interactable) return;
        down = true; pressAt = e.position; pressT = Time.unscaledTime;
        onPress?.Invoke();
    }
    public void OnPointerUp(PointerEventData e) { down = false; }
    public void OnPointerClick(PointerEventData e)
    {
        if (!interactable || e.button != PointerEventData.InputButton.Left) return;
        pressAt = e.position; pressT = Time.unscaledTime;
        pulse = 1f;
        onClick?.Invoke();
    }

    void OnDisable() { hover = false; down = false; h = 0f; d = 0f; }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        h = Mathf.Lerp(h, (hover || (textStyle && focused)) && interactable ? 1f : 0f, 1f - Mathf.Exp(-dt * 14f));
        d = down ? 1f : Mathf.MoveTowards(d, 0f, dt * 9f);   // snaps onto the shadow, pops back in ~2 drawings
        pulse *= Mathf.Exp(-dt * 7f);
        if (busyT >= 0f) busyT += dt;
        float shake = 0f;
        if (shakeT < 0.5f) { shakeT += dt; shake = Mathf.Sin(shakeT * 55f) * 9f * (1f - shakeT / 0.5f); }
        if (textStyle) { UpdateText(shake); return; }
        if (Look.OnTwos && busyT < 0f) { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t == tick) return; tick = t; }   // drawn on twos
        // hover lifts the pill off its shadow (up-left), a press slides it onto the shadow
        Vector2 lift = new Vector2(-1.5f, 3f) * h * (1f - d) + ShadowOffset * d + new Vector2(shake, 0f);
        if (pill != null) pill.SetLook(Color.Lerp(fillBase, fillHover, h), false, 0f, lift, Vector2.zero, 1f);
        if (body != null)
        {
            body.anchoredPosition = lift;
            float sc = 1f + 0.04f * h - 0.02f * d + 0.06f * pulse;
            body.localScale = squashed ? SquashScale : new Vector3(sc, sc, 1f);
            if (pill != null) pill.rectTransform.localScale = body.localScale;
        }
        if (border != null) border.color = new Color(1f, 1f, 1f, (primary ? 0.55f : 0.24f) + 0.4f * h);
        if (glow != null)
        {
            Color gc = primary ? new Color(0.78f, 0.66f, 1f) : new Color(0.55f, 0.45f, 1f);
            glow.color = new Color(gc.r, gc.g, gc.b, (primary ? 0.22f : 0f) + 0.3f * h + 0.35f * pulse);
        }
        if (pill != null) pill.SetOpacity(interactable ? 1f : 0.7f);
        float alpha = interactable ? 1f : 0.45f;
        if (label != null) label.color = new Color(labelBase.r, labelBase.g, labelBase.b, labelBase.a * alpha);
        if (icon != null)
        {
            float ib = 1f;
            if (busyT >= 0f) ib = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(busyT * 5f));
            icon.color = new Color(labelBase.r, labelBase.g, labelBase.b, labelBase.a * alpha * ib);
            icon.rectTransform.localEulerAngles = busyT >= 0f ? new Vector3(0f, 0f, -busyT * 220f) : Vector3.zero;
        }
    }

    /// <summary>The text item, one drawing at a time (12 fps): the underline draws itself in three drawings, the word nudges right
    /// while focused, dips while pressed, shakes when refused and pulses while busy; the squash holds.</summary>
    void UpdateText(float shake)
    {
        if (Look.OnTwos && shakeT >= 0.5f) { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t == tick) return; tick = t; }
        bool on = focused && interactable;
        if (on && ulDrawn < 3f) ulDrawn += 1f;
        if (underline != null)
        {
            bool show = on && ulDrawn > 0f;
            if (underline.gameObject.activeSelf != show) underline.gameObject.SetActive(show);
            if (show)
            {
                float full = label.rectTransform.sizeDelta.x - 2f;
                var ur = underline.rectTransform;
                float w = full * UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)];
                if (Mathf.Abs(ur.sizeDelta.x - w) > 0.5f) ur.sizeDelta = new Vector2(w, ur.sizeDelta.y);
            }
        }
        if (body != null)
        {
            body.anchoredPosition = new Vector2((on ? 8f : 0f) + shake, d > 0.5f ? -2f : 0f);
            body.localScale = squashed ? SquashScale : (d > 0.5f ? new Vector3(1.05f, 0.92f, 1f) : Vector3.one);
        }
        float a = !interactable ? 0.4f : (on ? 1f : 0.72f);
        if (busyT >= 0f) a *= 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(busyT * 5f));
        if (label != null)
        {
            var c = Comic.Cream; c.a = a;
            if (label.color != c) label.color = c;
        }
    }
}
