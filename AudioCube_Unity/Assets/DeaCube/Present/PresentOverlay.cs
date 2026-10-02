using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The presentation's minimal overlay (SPEC v3 §4.4) on its own small canvas above the HUD: a full-screen indigo fade for the enter / exit
/// transitions (and v9: a looping song's loop point), a thin progress line at the bottom (one dot per column in its chord colour), an exit button
/// top-right that fades in on mouse move and out after 2 s idle, a pause glyph, and the end state (replay + exit, each a standard icon with its
/// short lowercase word).
/// </summary>
public sealed class PresentOverlay
{
    readonly GameObject root;
    readonly Image fade;
    readonly RectTransform fill, head;
    readonly CanvasGroup exitGroup, endGroup, pauseGroup, barGroup;
    readonly Image[] dots;
    readonly float[] dotAt;
    float exitAlpha, endAlpha, pauseAlpha, barAlpha;
    float idle = 99f; Vector3 lastMouse;

    public float Fade { get; private set; }
    public bool Alive => root != null;
    public GameObject Root => root;

    /// <summary>v9: the veil's colour (a deep dusk indigo; it was the plunge's abyss).</summary>
    public static readonly Color VeilColor = new Color(0.10f, 0.068f, 0.2f);

    public PresentOverlay(int islands, Color[] colors, float[] startFractions, Action onExit, Action onReplay)
    {
        root = new GameObject("PresentOverlay");
        root.layer = 5;
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 880;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // progress line
        var bar = Rect("Progress", root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(760f, 3f));
        barGroup = bar.gameObject.AddComponent<CanvasGroup>(); barGroup.blocksRaycasts = false; barGroup.interactable = false;
        var bg = bar.gameObject.AddComponent<Image>(); bg.sprite = IconFactory.Get("white"); bg.color = new Color(1f, 1f, 1f, 0.13f); bg.raycastTarget = false;
        fill = Rect("Fill", bar, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        fill.pivot = new Vector2(0f, 0.5f);
        var fi = fill.gameObject.AddComponent<Image>(); fi.sprite = IconFactory.Get("white"); fi.color = new Color(1f, 1f, 1f, 0.78f); fi.raycastTarget = false;
        head = Rect("Head", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
        var hi = head.gameObject.AddComponent<Image>(); hi.sprite = IconFactory.Get("glow"); hi.color = new Color(1f, 1f, 1f, 0.95f); hi.raycastTarget = false;
        int n = Mathf.Max(0, islands);
        dots = new Image[n]; dotAt = new float[n];
        for (int i = 0; i < n; i++)
        {
            float f = startFractions != null && i < startFractions.Length ? startFractions[i] : i / (float)Mathf.Max(1, n);
            dotAt[i] = f;
            var d = Rect("Island" + i, bar, new Vector2(f, 0.5f), new Vector2(f, 0.5f), new Vector2(0f, 10f), new Vector2(7f, 7f));
            var di = d.gameObject.AddComponent<Image>(); di.sprite = IconFactory.Get("dot"); di.raycastTarget = false;
            Color c = colors != null && i < colors.Length ? colors[i] : Color.white; c.a = 0.5f; di.color = c;
            dots[i] = di;
        }

        // pause glyph
        var pg = Rect("Paused", root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 74f), new Vector2(30f, 30f));
        pauseGroup = pg.gameObject.AddComponent<CanvasGroup>(); pauseGroup.alpha = 0f; pauseGroup.blocksRaycasts = false;
        var pgi = pg.gameObject.AddComponent<Image>(); pgi.sprite = IconFactory.Get("pause"); pgi.color = new Color(1f, 1f, 1f, 0.8f); pgi.raycastTarget = false;

        // exit ✕ (top-right)
        var ex = IconButton("Exit", root.transform, new Vector2(1f, 1f), new Vector2(-46f, -46f), 48f, "close", onExit);
        exitGroup = ex.gameObject.AddComponent<CanvasGroup>(); exitGroup.alpha = 0f;

        // end state: replay + exit
        var end = Rect("End", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(300f, 110f));
        endGroup = end.gameObject.AddComponent<CanvasGroup>(); endGroup.alpha = 0f; endGroup.blocksRaycasts = false; endGroup.interactable = false;
        var rb = IconButton("Replay", end, new Vector2(0.5f, 0.5f), new Vector2(-64f, 8f), 88f, IconFactory.Has("loop") ? "loop" : "rewind", onReplay);
        var xb = IconButton("ExitEnd", end, new Vector2(0.5f, 0.5f), new Vector2(64f, 8f), 88f, "close", onExit);
        Word(rb, "replay"); Word(xb, "exit");

        // fade (last: on top of everything)
        var fr = Rect("Fade", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fade = fr.gameObject.AddComponent<Image>(); fade.sprite = IconFactory.Get("white");
        fade.color = new Color(VeilColor.r, VeilColor.g, VeilColor.b, 0f);
        fade.raycastTarget = false;
        lastMouse = Input.mousePosition;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static RectTransform IconButton(string name, Transform parent, Vector2 anchor, Vector2 pos, float size, string icon, Action onClick)
    {
        var rt = Rect(name, parent, anchor, anchor, pos, new Vector2(size, size));
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = IconFactory.Get("dot"); bg.color = new Color(0.10f, 0.08f, 0.18f, 0.72f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<PresentHoverLift>();
        var ic = Rect("Icon", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.46f, size * 0.46f));
        var ii = ic.gameObject.AddComponent<Image>(); ii.sprite = IconFactory.Get(icon); ii.color = new Color(0.96f, 0.94f, 1f, 0.95f); ii.raycastTarget = false;
        return rt;
    }

    /// <summary>v9: a short lowercase word under an icon button.</summary>
    static void Word(RectTransform button, string text)
    {
        var t = UIKit.Text(button, "Word", text, 26f, new Color(0.96f, 0.94f, 1f, 0.92f));
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -8f);
        rt.sizeDelta = new Vector2(160f, 34f);
    }

    public void SetFade(float a)
    {
        Fade = Mathf.Clamp01(a);
        if (fade == null) return;
        var c = fade.color; c.a = Fade; fade.color = c;
        fade.raycastTarget = Fade > 0.5f;
    }

    /// <summary>Per frame: progress (0..1 of the song), the current island, pause and end states; the ✕ follows mouse activity.</summary>
    public void Tick(float dt, float progress, int island, bool paused, bool ended, bool showChrome)
    {
        if (root == null) return;
        Vector3 m = Input.mousePosition;
        if ((m - lastMouse).sqrMagnitude > 4f || Input.GetMouseButton(0)) idle = 0f; else idle += dt;
        lastMouse = m;
        float k = 1f - Mathf.Exp(-dt * 8f);
        exitAlpha = Mathf.Lerp(exitAlpha, showChrome && !ended && idle < 2f ? 1f : 0f, k);
        endAlpha = Mathf.Lerp(endAlpha, showChrome && ended ? 1f : 0f, 1f - Mathf.Exp(-dt * 5f));
        pauseAlpha = Mathf.Lerp(pauseAlpha, showChrome && paused && !ended ? 1f : 0f, k);
        barAlpha = Mathf.Lerp(barAlpha, showChrome ? 1f : 0f, k);
        exitGroup.alpha = exitAlpha; exitGroup.blocksRaycasts = exitAlpha > 0.2f; exitGroup.interactable = exitAlpha > 0.2f;
        endGroup.alpha = endAlpha; endGroup.blocksRaycasts = endAlpha > 0.5f; endGroup.interactable = endAlpha > 0.5f;
        pauseGroup.alpha = pauseAlpha;
        barGroup.alpha = barAlpha;
        progress = Mathf.Clamp01(progress);
        fill.anchorMax = new Vector2(progress, 1f);
        fill.sizeDelta = Vector2.zero;
        head.anchorMin = head.anchorMax = new Vector2(progress, 0.5f);
        for (int i = 0; i < dots.Length; i++)
        {
            bool cur = i == island;
            var c = dots[i].color; c.a = Mathf.Lerp(c.a, cur ? 1f : 0.45f, k); dots[i].color = c;
            float s = Mathf.Lerp(dots[i].rectTransform.localScale.x, cur ? 1.6f : 1f, k);
            dots[i].rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }

    public bool EndShown => endAlpha > 0.5f;

    public void Destroy() { if (root != null) UnityEngine.Object.Destroy(root); }
}

/// <summary>Hover lift for the overlay's icon buttons (scale 1.08, eased).</summary>
public sealed class PresentHoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    float target = 1f, s = 1f;
    public void OnPointerEnter(PointerEventData e) { target = 1.08f; }
    public void OnPointerExit(PointerEventData e) { target = 1f; }
    void Update()
    {
        s = Mathf.Lerp(s, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
        transform.localScale = new Vector3(s, s, 1f);
    }
}
