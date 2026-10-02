using UnityEngine;

/// <summary>
/// v4 UI kit (scratchpad/v4/UI.md §1, research §3 rule 8): the hand-made feedback of a sticker control driven by a <see cref="HudButton"/>
/// (the button keeps the events: click, hold, hover): hover = the ink boils (redrawn at 10 fps) and the sticker squashes up to
/// <see cref="hoverScale"/> in 12 fps steps; press = the body slides onto its hard shadow; a pulse (the button's) pops it. Nothing is drawn
/// or scaled at rest (no per-frame cost besides this check). Optional <see cref="jiggle"/>: a stepped ±deg wobble while hovered (PRESENT).
/// </summary>
public class InkHover : MonoBehaviour
{
    public HudButton button;
    /// <summary>The sticker body (boils while hot; its shadow is where a press lands). May be null.</summary>
    public InkShape body;
    /// <summary>Extra painters that boil with the body (the picture on the sticker).</summary>
    public InkPainter[] painters;
    /// <summary>What moves on a press (default: this transform's first child, else nothing).</summary>
    public RectTransform content;
    public float hoverScale = 1.06f;
    /// <summary>± degrees of a stepped jiggle while hovered (0 = none).</summary>
    public float jiggle;
    /// <summary>Keeps the boil on (an "active" sticker: the open drawer's knob, mix mode).</summary>
    public bool active;
    /// <summary>The resting rotation (degrees) the jiggle adds to.</summary>
    public float baseAngle;

    bool wasHot, wasDown, jiggled; int tick = int.MinValue; Vector2 contentHome; bool homeSet; float shown = 1f, applied = 1f;

    public static InkHover Attach(HudButton b, InkShape body, RectTransform content = null, params InkPainter[] painters)
    {
        var h = b.gameObject.GetComponent<InkHover>();
        if (h == null) h = b.gameObject.AddComponent<InkHover>();
        h.button = b; h.body = body; h.content = content; h.painters = painters;
        b.animateScale = false;
        return h;
    }

    void OnDisable()
    {
        if (body != null) body.Boil = false;
        if (painters != null) foreach (var p in painters) if (p != null) p.Boil = false;
        wasHot = false;
        transform.localScale = Vector3.one; shown = applied = 1f;
        if (content != null && homeSet) content.anchoredPosition = contentHome;
    }

    void Update()
    {
        if (button == null) return;
        bool hot = button.Hover && button.interactable;
        bool down = button.Down && button.interactable;
        bool boil = hot || down || active;
        if (boil != wasHot)
        {
            wasHot = boil;
            if (body != null) body.Boil = boil;
            if (painters != null) foreach (var p in painters) if (p != null) p.Boil = boil;
        }
        if (content != null && !homeSet) { contentHome = content.anchoredPosition; homeSet = true; }
        if (down != wasDown)
        {
            wasDown = down;
            if (content != null) content.anchoredPosition = contentHome + (down && body != null ? body.ShadowOffset : Vector2.zero);
        }
        // scale on twos: target hover / pulse, drawn in 1/12 s steps; nothing is written at rest
        int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps);
        if (t == tick) return;
        tick = t;
        float target = (hot ? hoverScale : 1f) * (1f + button.pulse * 0.16f);
        shown = Mathf.Abs(target - shown) > 0.003f ? Mathf.Lerp(shown, target, 0.6f) : target;
        if (shown != applied) { applied = shown; transform.localScale = new Vector3(shown, shown, 1f); }
        if (jiggle > 0f && (hot || jiggled))
        {
            jiggled = hot;
            float a = baseAngle + (hot ? (((t * 7919) % 5) - 2) * 0.5f * jiggle : 0f);
            transform.localEulerAngles = new Vector3(0f, 0f, a);
        }
    }
}
