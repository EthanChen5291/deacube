using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The tutorial balloon (SPEC v4 §5, UI.md §2 "keep the tutorial's balloon idea, restyled as InkShape bubbles, lowercase Fredoka"): a cream
/// ink speech bubble (a wobbly hand-drawn outline heavier on the shadow side, a hard offset shadow, the tail drawn as part of the same
/// outline, reaching the target), one or two short lowercase lines, a small ink ▸ sticker for "next" on info steps, a ✕ sticker to skip and
/// progress dots. It sits on the side of the target with the most room (never over other HUD parts when it can help it), 20 px away,
/// clamped on screen; an off-screen world target gets an ink edge arrow and the bubble points at it. The target is circled by hand (UI) or
/// ringed (world), redrawn on twos; the rest of the screen dims lightly. It pops in and bobs on twos. It never blocks the gesture it
/// teaches: no raycasts while the pointer is held, a path is drawn or an island / a deck card is dragged. Pure view (Onboarding drives
/// it); the geometry is in screen pixels (bottom-left origin) so tests can read it back.
/// </summary>
public class TutorialBubble
{
    public const int SortOrder = 50;
    public const float Gap = 20f, Margin = 16f, BobPx = 4f, BobHz = 1.2f, PopSeconds = 0.3f, InkW = 3.2f;
    public static readonly Color Cream = Comic.Cream;
    public static readonly Color InkCol = Comic.Ink;
    public static readonly Color Pop = Comic.Pop;

    public Canvas Canvas { get; private set; }
    public Action onNext, onSkip;

    // read-back (screen px)
    public bool Visible { get; private set; }
    public bool Centered { get; private set; }
    public bool OffScreen { get; private set; }
    public int Side { get; private set; }             // 0 right of the target, 1 left, 2 above, 3 below, -1 centred
    public Rect BubbleRectPx { get; private set; }
    public Rect TargetRectPx { get; private set; }
    public Vector2 TailTipPx { get; private set; }
    public Vector2 TailBasePx { get; private set; }
    public string Key { get; private set; }
    public string Text => text != null ? text.text : "";
    public float Alpha => group != null ? group.alpha : 0f;
    /// <summary>Screen rects (px) the balloon should not cover (other HUD panels); Onboarding refreshes them every frame.</summary>
    public readonly List<Rect> Obstacles = new List<Rect>();
    public HudButton NextButton => nextBtn;
    public HudButton SkipButton => skipBtn;

    RectTransform root, bubble, content, arrow, dotsRow;
    InkShape body; InkPainter mark, arrowArt, dotsArt;
    Image hole; readonly Image[] vig = new Image[4];
    CanvasGroup group, vigGroup;
    TextMeshProUGUI text;
    HudButton nextBtn, skipBtn;
    Vector2 sizeUnits = new Vector2(300f, 90f);
    float popT = 9f, shownT, alpha, want;
    bool hasTarget, worldTarget, showNext, showSkip; int dotIndex, dotCount;
    Rect target; Vector2 offPoint; Vector2 arrowDir = Vector2.up; bool pressOutside;

    public void Build()
    {
        var go = new GameObject("OnboardingCanvas", typeof(RectTransform));
        go.layer = 5;
        UnityEngine.Object.DontDestroyOnLoad(go);
        Canvas = go.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = SortOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        root = UIKit.RT(go);
        group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // vignette: a soft hole around the target + four solid rects around it (never catches the pointer)
        var vg = UIKit.Obj("Vignette", root);
        UIKit.Stretch(UIKit.RT(vg));
        vigGroup = vg.AddComponent<CanvasGroup>(); vigGroup.blocksRaycasts = false; vigGroup.interactable = false;
        var dim = new Color(0.10f, 0.05f, 0.18f, 1f);
        hole = UIKit.Image(vg.transform, "Hole", "white", dim, Vector2.one);
        hole.sprite = MenuSprites.VignetteHole; hole.preserveAspect = false;
        BottomLeft(hole.rectTransform);
        for (int i = 0; i < 4; i++)
        {
            vig[i] = UIKit.Image(vg.transform, "Dim" + i, "white", dim, Vector2.one);
            vig[i].preserveAspect = false;
            BottomLeft(vig[i].rectTransform);
        }

        // the hand-drawn mark on the target (a circled UI rect, a ring on a world point) and the off-screen edge arrow
        var mrt = UIKit.RT(UIKit.Obj("TargetMark", root)); UIKit.Stretch(mrt);
        mark = mrt.gameObject.AddComponent<InkPainter>(); mark.raycastTarget = false; mark.onPaint = PaintMark;
        arrow = UIKit.RT(UIKit.Obj("EdgeArrow", root)); Centre(arrow); arrow.sizeDelta = new Vector2(64f, 64f);
        arrowArt = arrow.gameObject.AddComponent<InkPainter>(); arrowArt.raycastTarget = false; arrowArt.onPaint = PaintArrow;

        // the bubble: one InkShape (fill, outline, hard shadow, tail)
        var bgo = UIKit.Obj("Bubble", root);
        bubble = UIKit.RT(bgo); Centre(bubble);
        body = bgo.AddComponent<InkShape>();
        body.Shape = InkShape.Kind.Bubble; body.color = Cream; body.Radius = 22f; body.Wobble = 1.5f; body.RotJitter = 0.8f;
        body.SetInk(2.4f, 4.2f); body.ShadowOffset = new Vector2(6f, -7f); body.TailWidth = 30f; body.raycastTarget = true; body.Seed = 5519;

        content = UIKit.RT(UIKit.Obj("Content", bubble));
        UIKit.Stretch(content);
        text = UIKit.Text(content, "Text", "", 25f, InkCol, TextAlignmentOptions.MidlineLeft);
        text.fontSharedMaterial = Comic.Font != null ? Comic.Font.material : text.fontSharedMaterial;
        text.fontStyle = FontStyles.Normal; text.characterSpacing = 0.5f;
        text.enableWordWrapping = true; text.lineSpacing = -6f;
        nextBtn = StickerButton(content, "Next", 40f, InkCol, true, () => onNext?.Invoke(), "next");
        skipBtn = StickerButton(bubble, "Skip", 28f, Cream, false, () => onSkip?.Invoke(), "skip");
        dotsRow = UIKit.RT(UIKit.Obj("Dots", content));
        dotsArt = dotsRow.gameObject.AddComponent<InkPainter>(); dotsArt.raycastTarget = false; dotsArt.onPaint = PaintDots;
        Visible = false;
    }

    /// <summary>A small round sticker button: ▸ (next, ink with a cream triangle) or ✕ (skip, cream with an ink cross).</summary>
    static HudButton StickerButton(Transform parent, string name, float size, Color fill, bool next, Action onClick, string caption)
    {
        var rt = UIKit.RT(UIKit.Obj(name, parent)); Centre(rt); rt.sizeDelta = new Vector2(size, size);
        var s = rt.gameObject.AddComponent<InkShape>();
        s.Shape = next ? InkShape.Kind.Sticker : InkShape.Kind.Circle; s.color = fill; s.SetInk(1.8f, 2.6f); s.ShadowOffset = new Vector2(2.5f, -3f);
        if (next) { s.Ink = Cream; s.ShadowColor = Comic.Magenta; }
        var art = UIKit.RT(UIKit.Obj("Art", rt)); UIKit.Stretch(art);
        var p = art.gameObject.AddComponent<InkPainter>(); p.raycastTarget = false;
        p.onPaint = next
            ? (Action<InkPainter>)(q => { Vector2 c = q.Area.center; pts.Clear(); pts.Add(c + new Vector2(9f, 0f)); pts.Add(c + new Vector2(-6f, 9f)); pts.Add(c + new Vector2(-6f, -9f)); q.Fill(pts, 3, Cream); })
            : (q => { Vector2 c = q.Area.center; float r = 5f; q.Line(c + new Vector2(-r, -r), c + new Vector2(r, r), 2.4f, InkCol, true); q.Line(c + new Vector2(-r, r), c + new Vector2(r, -r), 2.4f, InkCol, true); });
        return HudKit.Control(s, caption, onClick, s, art, p);
    }
    static readonly List<Vector2> pts = new List<Vector2>(96);

    static void BottomLeft(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero; }
    static void Centre(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f); }

    float Scale => Canvas != null && Canvas.scaleFactor > 0f ? Canvas.scaleFactor : 1f;

    /// <summary>New content: a different key re-pops the bubble; the same key only updates text/controls. (Icons are not drawn in v4:
    /// the words and the pointed target carry the step.)</summary>
    public void SetContent(string key, string msg, string iconName, bool next, bool skip, int dotIdx, int dotCnt)
    {
        if (Canvas == null) Build();
        bool changed = key != Key;
        Key = key;
        text.text = (msg ?? "").ToLowerInvariant();
        showNext = next; showSkip = skip; dotIndex = dotIdx; dotCount = dotCnt;
        nextBtn.gameObject.SetActive(next);
        skipBtn.gameObject.SetActive(skip);
        LayoutContent();
        if (changed) { popT = 0f; shownT = 0f; }
    }

    void LayoutContent()
    {
        const float padX = 30f, padY = 22f, nextS = 40f, nextGap = 16f, maxText = 400f;
        text.ForceMeshUpdate();
        Vector2 pref = text.GetPreferredValues(text.text, maxText, 0f);
        float tw = Mathf.Min(maxText, pref.x) + 4f, th = Mathf.Max(28f, pref.y);
        bool dotsOn = dotCount > 1;
        float dotsH = dotsOn ? 16f : 0f;
        float w = padX + tw + (showNext ? nextGap + nextS : 0f) + padX;
        float rowH = Mathf.Max(th, showNext ? nextS : 0f);
        float h = padY + rowH + dotsH + padY;
        w = Mathf.Max(w, dotsOn ? padX * 2f + dotCount * 12f : 170f);
        sizeUnits = new Vector2(w, h);
        bubble.sizeDelta = sizeUnits;
        float rowY = dotsH * 0.5f;
        var trt = text.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f); trt.pivot = new Vector2(0f, 0.5f);
        trt.sizeDelta = new Vector2(tw, th); trt.anchoredPosition = new Vector2(-w * 0.5f + padX, rowY);
        if (showNext) UIKit.Anchor(UIKit.RT(nextBtn), new Vector2(0.5f, 0.5f), new Vector2(w * 0.5f - padX - nextS * 0.5f + 6f, rowY), new Vector2(nextS, nextS));
        if (showSkip) UIKit.Anchor(UIKit.RT(skipBtn), new Vector2(0.5f, 0.5f), new Vector2(w * 0.5f - 6f, h * 0.5f - 6f), new Vector2(28f, 28f));
        dotsRow.anchorMin = dotsRow.anchorMax = new Vector2(0.5f, 0.5f); dotsRow.pivot = new Vector2(0.5f, 0.5f);
        dotsRow.anchoredPosition = new Vector2(0f, -h * 0.5f + padY * 0.5f + 6f); dotsRow.sizeDelta = new Vector2(Mathf.Max(20f, dotCount * 12f), 12f);
        dotsRow.gameObject.SetActive(dotsOn);
        dotsArt.Repaint();
    }

    void PaintDots(InkPainter p)
    {
        Vector2 c = p.Area.center;
        for (int i = 0; i < dotCount; i++)
        {
            Vector2 at = c + new Vector2((i - (dotCount - 1) * 0.5f) * 12f, 0f);
            if (i == dotIndex) { p.Disc(at, 4.6f, InkCol); p.Disc(at, 2.4f, Comic.Magenta); }
            else if (i < dotIndex) p.Disc(at, 2.8f, Comic.A(InkCol, 0.8f));
            else p.Arc(at, 2.6f, 1.2f, Comic.A(InkCol, 0.45f));
        }
    }

    /// <summary>Where to point: a screen rect (px) of a UI or world target; <paramref name="offScreenPoint"/> is used when the world
    /// point is off screen (edge arrow). No target = centred bubble.</summary>
    public void SetTarget(bool has, Rect targetPx, bool world, bool offscreen, Vector2 offScreenPoint)
    {
        hasTarget = has; target = targetPx; worldTarget = world; OffScreen = has && offscreen; offPoint = offScreenPoint;
    }

    public void Show(bool on) { want = on ? 1f : 0f; if (on && Canvas == null) Build(); }
    public void HideNow() { want = 0f; alpha = 0f; if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; } Visible = false; Key = null; }

    public void Tick(float dt)
    {
        if (Canvas == null) return;
        alpha = Mathf.MoveTowards(alpha, want, dt * (want > alpha ? 7f : 9f));
        Visible = want > 0.5f;
        group.alpha = alpha;
        // F20 + v4: the balloon never blocks the gesture it teaches — no raycasts while a deck card is dragged, the pointer is held, a path
        // is drawn or an island is dragged (a release over it must land in the world)
        // (a press that starts on the bubble keeps it clickable: its ▸ / ✕ work; a press elsewhere passes through until released)
        if (Input.GetMouseButtonDown(0)) pressOutside = !BubbleRectPx.Contains(Input.mousePosition);
        if (!Input.GetMouseButton(0)) pressOutside = false;
        var pm = PathManager.I;
        bool busy = (IslandTray.I != null && IslandTray.I.Dragging) || pressOutside
                    || (pm != null && (pm.IsDrawing || (pm.Drag != null && pm.Drag.Busy)));
        bool live = Visible && !busy;
        group.blocksRaycasts = live; group.interactable = live;
        if (alpha <= 0.001f) return;
        popT += dt; shownT += dt;
        Layout();
    }

    /// <summary>Recomputes the placement now (tests call it after SetTarget to read the geometry without waiting a frame).</summary>
    public void Layout()
    {
        float s = Scale, W = Screen.width, H = Screen.height;
        // pop and bob on twos
        float pop = Ease.OutBack(Mathf.Clamp01(Mathf.Floor(popT * Look.TwosFps) / Look.TwosFps / PopSeconds), 1.4f);
        float bobT = Mathf.Floor(shownT * Look.TwosFps) / Look.TwosFps;
        float bw = sizeUnits.x * s, bh = sizeUnits.y * s, gap = Gap * s, m = Margin * s;
        float bob = Mathf.Sin(bobT * Mathf.PI * 2f * BobHz) * BobPx * s;
        Rect t = target;
        if (hasTarget && OffScreen)
        {
            Vector2 c = new Vector2(W * 0.5f, H * 0.5f);
            Vector2 dir = offPoint - c; if (dir.sqrMagnitude < 1f) dir = Vector2.up;
            float inset = 44f * s;
            float kx = Mathf.Abs(dir.x) > 1e-3f ? (W * 0.5f - inset) / Mathf.Abs(dir.x) : float.MaxValue;
            float ky = Mathf.Abs(dir.y) > 1e-3f ? (H * 0.5f - inset) / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 a = c + dir * Mathf.Min(kx, ky);
            arrow.gameObject.SetActive(true);
            arrowDir = dir.normalized;
            Vector2 ap = a / s + arrowDir * (Mathf.Sin(bobT * 7f) * 5f);
            arrow.anchoredPosition = ap;
            arrowArt.Repaint();
            float asz = 46f * s;
            t = new Rect(a.x - asz * 0.5f, a.y - asz * 0.5f, asz, asz);
        }
        else arrow.gameObject.SetActive(false);
        TargetRectPx = t;

        Vector2 centre;
        if (!hasTarget)
        {
            Centered = true; Side = -1;
            centre = new Vector2(W * 0.5f, H * 0.56f + bob);
        }
        else
        {
            Centered = false;
            // the side that fits and covers the least of the other HUD panels (and never the target); ties: above, right, below, left
            float[] room = { W - t.xMax - m, t.xMin - m, H - t.yMax - m, t.yMin - m };
            float[] need = { bw + gap, bw + gap, bh + gap, bh + gap };
            int[] order = { 2, 0, 3, 1 };
            int best = 2; float bestScore = float.MaxValue; Vector2 bestC = Vector2.zero;
            for (int oi = 0; oi < order.Length; oi++)
            {
                int sd = order[oi];
                Vector2 c = SideCentre(sd, t, bw, bh, gap, m, W, H);
                Rect br = new Rect(c.x - bw * 0.5f, c.y - bh * 0.5f, bw, bh);
                float slack = room[sd] - need[sd];
                float score = oi * 50f + (slack < 0f ? 1e6f - slack * 100f : 0f) + Overlap(br, t) * 4f;
                foreach (var o in Obstacles) score += Overlap(br, o);
                if (score < bestScore) { bestScore = score; best = sd; bestC = c; }
            }
            Side = best;
            centre = bestC;
            centre.y += bob;
            centre.y = Mathf.Clamp(centre.y, m + bh * 0.5f, Mathf.Max(m + bh * 0.5f, H - m - bh * 0.5f));
        }
        Vector2 cu = centre / s;
        bubble.anchoredPosition = cu;
        bubble.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, pop);
        BubbleRectPx = new Rect(centre.x - bw * 0.5f, centre.y - bh * 0.5f, bw, bh);

        // the tail: from the bubble's edge facing the target to the nearest point of the target rect (part of the bubble's outline)
        if (hasTarget)
        {
            var b = BubbleRectPx;
            float cr = 34f * s;
            Vector2 bp;
            switch (Side)
            {
                case 0: bp = new Vector2(b.xMin, Mathf.Clamp(t.center.y, b.yMin + cr, Mathf.Max(b.yMin + cr, b.yMax - cr))); break;
                case 1: bp = new Vector2(b.xMax, Mathf.Clamp(t.center.y, b.yMin + cr, Mathf.Max(b.yMin + cr, b.yMax - cr))); break;
                case 2: bp = new Vector2(Mathf.Clamp(t.center.x, b.xMin + cr, Mathf.Max(b.xMin + cr, b.xMax - cr)), b.yMin); break;
                default: bp = new Vector2(Mathf.Clamp(t.center.x, b.xMin + cr, Mathf.Max(b.xMin + cr, b.xMax - cr)), b.yMax); break;
            }
            Vector2 tp = new Vector2(Mathf.Clamp(bp.x, t.xMin, t.xMax), Mathf.Clamp(bp.y, t.yMin, t.yMax));
            Vector2 d = tp - bp; float len = d.magnitude;
            Vector2 dir = len > 1e-3f ? d / len : Vector2.down;
            float pull = Mathf.Min(6f * s, len * 0.3f);
            Vector2 tip = bp + dir * Mathf.Max(0f, len - pull) * Mathf.Clamp01(pop);
            if (body.Shape != InkShape.Kind.Bubble) body.Shape = InkShape.Kind.Bubble;
            // the tail tip in the bubble's local space (its scale undone)
            float k = Mathf.Max(0.01f, bubble.localScale.x);
            body.TailTip = (tip - centre) / s / k;
            TailBasePx = bp;
            TailTipPx = tip;
        }
        else
        {
            if (body.Shape != InkShape.Kind.RoundRect) body.Shape = InkShape.Kind.RoundRect;
            TailBasePx = TailTipPx = centre;
        }

        // the mark on the target: redrawn on twos
        int tw = Mathf.FloorToInt(shownT * Look.TwosFps);
        if (tw != markTick) { markTick = tw; mark.Repaint(); }

        // vignette: a soft hole on the target (or a plain light dim for centred balloons)
        vigGroup.alpha = 0.26f;
        Vector2 hc; float hr;
        if (hasTarget) { hc = t.center; hr = Mathf.Max(t.width, t.height) * 0.5f + 46f * s; }
        else { hc = new Vector2(W * 0.5f, H * 0.5f); hr = 0f; }
        float S = hr > 0f ? 2f * hr / MenuSprites.HoleInner : 0f;
        Rect hrct = new Rect(hc.x - S * 0.5f, hc.y - S * 0.5f, S, S);
        hole.gameObject.SetActive(S > 0f);
        if (S > 0f) { hole.rectTransform.anchoredPosition = hrct.position / s; hole.rectTransform.sizeDelta = hrct.size / s; }
        float xl = Mathf.Clamp(hrct.xMin, 0f, W), xr = Mathf.Clamp(hrct.xMax, 0f, W), yb = Mathf.Clamp(hrct.yMin, 0f, H), yt = Mathf.Clamp(hrct.yMax, 0f, H);
        SetDim(0, new Rect(0f, 0f, xl, H), s);
        SetDim(1, new Rect(xr, 0f, W - xr, H), s);
        SetDim(2, new Rect(xl, 0f, xr - xl, yb), s);
        SetDim(3, new Rect(xl, yt, xr - xl, H - yt), s);
    }
    int markTick = -1;

    /// <summary>The target circled by hand (a UI rect: a wobbly loop drawn in one overshooting stroke) or ringed (a world point: an ink
    /// ring that breathes on twos).</summary>
    void PaintMark(InkPainter p)
    {
        if (!hasTarget || OffScreen) return;
        float s = Scale;
        Rect t = TargetRectPx;
        // screen px (bottom-left origin) → this full-screen painter's local space (pivot at the centre)
        Vector2 c = new Vector2(p.Area.xMin, p.Area.yMin) + t.center / s;
        float ph = 0.5f + 0.5f * Mathf.Sin(Mathf.Floor(shownT * Look.TwosFps) / Look.TwosFps * Mathf.PI * 2f * 1.3f);
        bool point = worldTarget && t.width / s < 90f && t.height / s < 90f;   // a cube, a tile: a ring; an island or a HUD part: circled by hand
        if (!point)
        {
            float rx = t.width / s * 0.5f + 12f + ph * 3f, ry = t.height / s * 0.5f + 10f + ph * 3f;
            pts.Clear();
            InkPainter.ArcPoints(pts, c, rx, ry, 100f, 100f + 385f, 56);
            for (int i = 0; i < pts.Count; i++) pts[i] += p.Jit(i + markTick * 3, 1.2f);
            p.Stroke(pts, pts.Count, 4.6f, Cream, false, 0f, 0f, 2.4f, true);
            p.Stroke(pts, pts.Count, 2.6f, Comic.Magenta, false, 0f, 0f, 1.2f, true);
        }
        else
        {
            float r = 24f + ph * 8f;
            p.Arc(c, r, 5f, Comic.A(Cream, 0.9f));
            p.Arc(c, r, 2.6f, Comic.Magenta);
        }
    }

    void PaintArrow(InkPainter p)
    {
        Vector2 c = p.Area.center, d = arrowDir;
        Vector2 n = new Vector2(-d.y, d.x);
        pts.Clear();
        pts.Add(c + d * 22f); pts.Add(c + n * 16f); pts.Add(c + n * 7f); pts.Add(c + n * 7f - d * 18f); pts.Add(c - n * 7f - d * 18f); pts.Add(c - n * 7f); pts.Add(c - n * 16f);
        p.Shift = new Vector2(3f, -4f); p.Fill(pts, pts.Count, Comic.A(InkCol, 0.9f)); p.Shift = Vector2.zero;
        p.InkFill(pts, pts.Count, Comic.Magenta, 2.4f, InkCol);
    }

    static Vector2 SideCentre(int sd, Rect t, float bw, float bh, float gap, float m, float W, float H)
    {
        Vector2 c;
        switch (sd)
        {
            case 0: c = new Vector2(t.xMax + gap + bw * 0.5f, t.center.y); break;
            case 1: c = new Vector2(t.xMin - gap - bw * 0.5f, t.center.y); break;
            case 2: c = new Vector2(t.center.x, t.yMax + gap + bh * 0.5f); break;
            default: c = new Vector2(t.center.x, t.yMin - gap - bh * 0.5f); break;
        }
        c.x = Mathf.Clamp(c.x, m + bw * 0.5f, Mathf.Max(m + bw * 0.5f, W - m - bw * 0.5f));
        c.y = Mathf.Clamp(c.y, m + bh * 0.5f, Mathf.Max(m + bh * 0.5f, H - m - bh * 0.5f));
        return c;
    }
    static float Overlap(Rect a, Rect b)
    {
        float ix = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), iy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return ix > 0f && iy > 0f ? ix * iy : 0f;
    }

    void SetDim(int i, Rect r, float s)
    {
        bool on = r.width > 0.5f && r.height > 0.5f;
        vig[i].gameObject.SetActive(on);
        if (!on) return;
        vig[i].rectTransform.anchoredPosition = r.position / s;
        vig[i].rectTransform.sizeDelta = r.size / s;
    }

    /// <summary>Distance (px) from the tail tip to the target rect (0 when inside).</summary>
    public float TipDistance()
    {
        var r = TargetRectPx; var p = TailTipPx;
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
