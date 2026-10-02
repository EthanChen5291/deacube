using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The title screen's exit (SPEC v4 §7b), replacing the v3 wall that broke apart: a hand-made comic cut, counted in drawings on twos
/// (<see cref="Drawing"/> = 1/12 s). Anticipation: the clicked item squashes (drawing 0) and the frame holds (drawing 1: the frame is
/// captured, and behind the held copy the stage camera hands over to the world camera). Impact (drawing 2): the same frame printed
/// in two tones on white paper, the cut lines already inked, a lettered onomatopoeia sticker (Bangers) slapped on the click point.
/// Then (drawings 3-9) the held frame, cut into 3-4 slanted panels with thick ink gutters radiating from the click point, slides and
/// swings off-screen in different directions with hard offset shadows, revealing the live world; the HUD pops in at drawing
/// <see cref="HudAt"/> and the leave ends at <see cref="EndAt"/> (≈1.08 s). Rigid ink-bordered paper shapes only (<see cref="MenuPanel"/>,
/// the kit's InkShape sticker): no particles, no shatter, no dissolve. Its own overlay canvas (<see cref="SortOrder"/>: above the
/// menu and the vibe prompt, below the tutorial balloons); canvas units = screen pixels. <see cref="MainMenu"/> drives it and owns the
/// world side (camera swap, HUD, the island rising). If the frame cannot be captured the panels are plain paper.
/// v5: a gallery song leaves with "HIT IT!" (its card squashes itself; the leave gets no word to squash, so the frame is captured at once).
/// </summary>
public class MenuLeave : MonoBehaviour
{
    public const int SortOrder = 40;
    public const float Drawing = 1f / 12f;
    /// <summary>The timeline in drawings from the click: 0 squash, <see cref="HoldAt"/> hold, <see cref="ImpactAt"/> impact,
    /// <see cref="PanelsAt"/> panels fly, <see cref="HudAt"/> HUD, <see cref="EndAt"/> done.</summary>
    public const int HoldAt = 1, ImpactAt = 2, PanelsAt = 3, HudAt = 10, EndAt = 13;
    public static float Seconds => EndAt * Drawing;
    /// <summary>Tests / captures: the leave's clock never passes this time (null = it runs free). A lease: it lapses by itself
    /// <see cref="FreezeLease"/> s after it was last set, so an interrupted capture routine can never leave a leave frozen.</summary>
    public static float? FreezeAt
    {
        get { return freezeAt.HasValue && Time.realtimeSinceStartup < freezeUntil ? freezeAt : null; }
        set { freezeAt = value; freezeUntil = value.HasValue ? Time.realtimeSinceStartup + FreezeLease : 0f; }
    }
    public const float FreezeLease = 3f;
    /// <summary>Captures: while the leave is pinned at <see cref="FreezeAt"/> the world's scaled time stops with it (Time.timeScale 0), so what
    /// happens behind the panels (the island rising) stays in step; <see cref="ApplyWorldFreeze"/> (MainMenu, every frame) restores
    /// scaled time the moment the pin or the lease goes.</summary>
    public static bool FreezeWorld;
    /// <summary>Tests: forces the capture's vertical flip (null = <see cref="SystemInfo.graphicsUVStartsAtTop"/>).</summary>
    public static bool? FlipOverride;
    static float? freezeAt; static float freezeUntil; static bool worldFrozen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { freezeAt = null; freezeUntil = 0f; FreezeWorld = false; worldFrozen = false; FlipOverride = null; }

    /// <summary>Stops scaled time while <paramref name="pinned"/> (the leave held at <see cref="FreezeAt"/>) and <see cref="FreezeWorld"/>,
    /// and gives it back as soon as either lapses. Only ever undoes its own freeze.</summary>
    public static void ApplyWorldFreeze(bool pinned)
    {
        bool want = FreezeWorld && pinned && FreezeAt.HasValue;
        if (want && !worldFrozen) { worldFrozen = true; Time.timeScale = 0f; }
        else if (!want && worldFrozen) { worldFrozen = false; Time.timeScale = 1f; }
    }
    /// <summary>The clock is held at <see cref="FreezeAt"/> right now.</summary>
    public bool Pinned => Active && FreezeAt.HasValue && T >= FreezeAt.Value - 1e-4f;

    static readonly Color Paper = new Color(1f, 0.98f, 0.95f);
    static readonly Color StickerFill = new Color(1.0f, 0.42f, 0.78f);      // the title's pink (MenuStage.WordColor(0))
    static readonly Color Misprint = new Color(0.42f, 0.9f, 1.0f);          // the title's cyan: the sticker's misregistration

    class Panel
    {
        public RectTransform rt, shadowRt;
        public MenuPanel fill, ink, shadow;
        public Vector2 hinge, dir; public float angle, dist; public int start, drawings; public bool swing, gone;
        public readonly List<Vector2> local = new List<Vector2>(10);
    }

    Canvas canvas; CanvasGroup group; RectTransform root, shadowsRoot, panelsRoot, stickerRoot;
    MenuPanel held; Image white;
    InkShape sticker; TextMeshProUGUI word, wordMis;
    readonly List<Panel> panels = new List<Panel>(4);
    readonly List<Vector2> scratch = new List<Vector2>(12);
    Material mat;
    RenderTexture rt;
    MenuButton item;
    Vector2 originCanvas, screenSize;
    float k = 1f;
    int seed, lastDrawn = -1, poses;
    bool captureStarted, dirty, whooshed, heldDrawn, warmed;
    float realT;

    // ------------------------------------------------------------------ read-backs (tests)
    public bool Active { get; private set; }
    /// <summary>Seconds since <see cref="Begin"/> on the leave's own clock (unscaled, <see cref="FreezeAt"/> aware).</summary>
    public float T { get; private set; }
    public int DrawingNow => Mathf.FloorToInt(T / Drawing + 1e-4f);
    public Vector2 Origin { get; private set; }
    public string Word { get; private set; } = "";
    public bool Captured { get; private set; }
    public bool CaptureFailed { get; private set; }
    public bool Flipped { get; private set; }
    public int PanelCount => panels.Count;
    public int PanelsVisible { get { int c = 0; foreach (var p in panels) if (p.rt.gameObject.activeSelf) c++; return c; } }
    public bool HeldShown => held != null && held.gameObject.activeSelf;
    public bool ImpactShown { get; private set; }
    public bool StickerShown => stickerRoot != null && stickerRoot.gameObject.activeSelf;
    /// <summary>Pose updates of the panels so far (on twos: one per drawing, however many frames).</summary>
    public int PoseUpdates => poses;
    public Canvas Canvas => canvas;
    /// <summary>The sticker's lettering font (Bangers when installed).</summary>
    public TMP_FontAsset WordFont => word != null ? word.font : null;
    /// <summary>The sticker's screen centre (px).</summary>
    public Vector2 StickerScreen => stickerRoot != null ? RectTransformUtility.WorldToScreenPoint(null, stickerRoot.position) : Vector2.zero;
    public Texture Frame => rt;
    /// <summary>Screen rect (px) of panel <paramref name="i"/>'s outline in its current pose (tests: off-screen checks).</summary>
    public Rect PanelScreenRect(int i)
    {
        if (i < 0 || i >= panels.Count) return new Rect();
        var p = panels[i];
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var q in p.local)
        {
            Vector3 w = p.rt.TransformPoint(q);
            Vector2 s = RectTransformUtility.WorldToScreenPoint(null, w);
            x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    public static MenuLeave Create(Transform parent)
    {
        var go = new GameObject("MenuLeave", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var m = go.AddComponent<MenuLeave>();
        m.Build();
        return m;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        root = (RectTransform)transform;

        var sh = Shader.Find("DeaCube/MenuPanel");
        if (sh != null) mat = new Material(sh) { name = "MenuPanel (runtime)" };
        else Debug.LogWarning("MenuLeave: DeaCube/MenuPanel shader missing: the held frame uses the default UI shader");

        held = MenuPanel.Create(root, "Held", MenuPanel.Mode.Fill, Color.white);
        if (mat != null) held.material = mat;
        white = new GameObject("Paper", typeof(RectTransform)).AddComponent<Image>();
        white.gameObject.layer = 5;
        white.transform.SetParent(root, false);
        UIKit.Stretch(white.rectTransform);
        white.color = Paper; white.raycastTarget = false;
        shadowsRoot = UIKit.RT(UIKit.Obj("Shadows", root)); UIKit.Stretch(shadowsRoot);
        panelsRoot = UIKit.RT(UIKit.Obj("Panels", root)); UIKit.Stretch(panelsRoot);

        stickerRoot = UIKit.RT(UIKit.Obj("Sticker", root));
        stickerRoot.anchorMin = stickerRoot.anchorMax = stickerRoot.pivot = new Vector2(0.5f, 0.5f);
        sticker = InkShape.Create(stickerRoot, "StickerShape", InkShape.Kind.Sticker, StickerFill, new Vector2(360f, 160f));
        sticker.raycastTarget = false;
        sticker.Ink = Comic.Ink; sticker.ShadowColor = Comic.A(Comic.Ink, 0.96f);
        sticker.Radius = 6f; sticker.RotJitter = 3f;
        wordMis = MakeWord("WordMisprint", Misprint, false);
        word = MakeWord("Word", Comic.Cream, true);
        // the stickers' letters rasterised now (a dynamic font would otherwise do it on the leave's impact frame)
        var digits = Comic.DigitFont;
        if (digits != null && digits.atlasPopulationMode == TMPro.AtlasPopulationMode.Dynamic) digits.TryAddCharacters("ZOMPBINGWHAT! ");   // v5: + "HIT IT!" (a gallery song)

        canvas.gameObject.SetActive(false);
    }

    TextMeshProUGUI MakeWord(string name, Color c, bool inked)
    {
        var go = UIKit.Obj(name, stickerRoot);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = Comic.DigitFont; t.fontSize = 120f; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow;
        t.characterSpacing = 3f;
        if (inked) { var m = Comic.InkMaterial(Comic.DigitFont); if (m != null) t.fontSharedMaterial = m; }
        UIKit.RT(go).sizeDelta = new Vector2(600f, 160f);
        return t;
    }

    void OnDestroy() { ReleaseFrame(); if (mat != null) Destroy(mat); }

    /// <summary>Draws every piece of the leave once, invisibly (1/255 alpha), for a couple of frames while the menu boots: the panel
    /// shader, the sticker's inked Bangers and shape meet the GPU there, not on the held frame. Once per session.</summary>
    public void Warm()
    {
        if (warmed || Active) return;
        warmed = true;
        canvas.gameObject.SetActive(true);
        group.alpha = 1f / 255f;
        screenSize = new Vector2(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height));
        k = Mathf.Sqrt(screenSize.x / 1920f * (screenSize.y / 1080f));
        Word = "ZOOM!"; originCanvas = Vector2.zero; seed = 7;
        BuildPanels(); BuildSticker();
        held.gameObject.SetActive(true); white.gameObject.SetActive(false);
        shadowsRoot.gameObject.SetActive(true); panelsRoot.gameObject.SetActive(true); stickerRoot.gameObject.SetActive(true);
        StartCoroutine(WarmEnd());
    }

    IEnumerator WarmEnd()
    {
        yield return null; yield return null; yield return null;
        if (!Active) { canvas.gameObject.SetActive(false); group.alpha = 1f; }
    }

    // ------------------------------------------------------------------ control (MainMenu)
    /// <summary>Starts the leave from <paramref name="screen"/> (px): <paramref name="clicked"/> (may be null: no squash, the frame is
    /// captured at once) squashes, <paramref name="onomatopoeia"/> is lettered on the sticker.</summary>
    public void Begin(Vector2 screen, MenuButton clicked, string onomatopoeia)
    {
        End();
        group.alpha = 1f;
        Active = true; T = 0f; realT = 0f; lastDrawn = -1; poses = 0; captureStarted = false; dirty = true; whooshed = false; ImpactShown = false; heldDrawn = false;
        Captured = false; CaptureFailed = false;
        item = clicked;
        Word = string.IsNullOrEmpty(onomatopoeia) ? "WHAM!" : onomatopoeia;
        screenSize = new Vector2(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height));
        k = Mathf.Sqrt(screenSize.x / 1920f * (screenSize.y / 1080f));
        Origin = new Vector2(Mathf.Clamp(screen.x, 0f, screenSize.x), Mathf.Clamp(screen.y, 0f, screenSize.y));
        originCanvas = Origin - screenSize * 0.5f;
        unchecked { seed = 1 + (int)((uint)(Word.GetHashCode() ^ (int)(Origin.x * 7f) ^ ((int)(Origin.y * 13f) << 8)) % 100000u); }
        if (item != null) item.Squash();
        BuildPanels();
        BuildSticker();
        canvas.gameObject.SetActive(true);
        held.gameObject.SetActive(false); white.gameObject.SetActive(false);
        shadowsRoot.gameObject.SetActive(false); panelsRoot.gameObject.SetActive(false); stickerRoot.gameObject.SetActive(false);
        if (mat != null) mat.SetFloat("_Impact", 0f);
    }

    /// <summary>Advances the leave by <paramref name="dt"/> (unscaled seconds) and redraws when a new drawing is due.</summary>
    public void Tick(float dt)
    {
        if (!Active) return;
        dt = Mathf.Max(0f, dt);
        realT += dt;
        float next = T + dt;
        // A hitch (a heavy song build just before the first tick) never skips the key drawings and never lets the capture see the
        // leave's own drawings: until the frame is captured (0.5 s at most) and the held frame has been on screen once, the clock stays
        // short of the impact; the impact is drawn once before the panels. Each limit only holds a clock that is still short of it —
        // it never takes the clock back, so it can never pin it for good.
        float hold = ImpactAt * Drawing - 1e-3f, impact = PanelsAt * Drawing - 1e-3f;
        bool waitCapture = !Captured && !CaptureFailed && realT < 0.5f;
        if ((waitCapture || (Captured && !heldDrawn)) && T <= hold) next = Mathf.Min(next, hold);
        if (!ImpactShown && T <= impact) next = Mathf.Min(next, impact);
        var freeze = FreezeAt;
        if (freeze.HasValue) next = Mathf.Min(next, Mathf.Max(T, freeze.Value));
        T = Mathf.Max(T, next);
        if (!captureStarted && (item == null || T >= HoldAt * Drawing)) { captureStarted = true; StartCoroutine(CaptureAtEndOfFrame()); }
        int d = DrawingNow;
        if (d != lastDrawn || dirty) { lastDrawn = d; dirty = false; Draw(d); }
        else if (!Look.OnTwos && d >= PanelsAt) PosePanels(T);
    }

    /// <summary>Hides everything and releases the captured frame (end of the leave, Hide, a new Show).</summary>
    public void End()
    {
        StopAllCoroutines();
        Active = false;
        if (item != null) item.Unsquash();
        item = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        ReleaseFrame();
    }

    void ReleaseFrame()
    {
        if (rt == null) return;
        if (held != null) held.SetTexture(null, Vector2.zero, Vector2.one, false);
        foreach (var p in panels) if (p.fill != null) p.fill.SetTexture(null, Vector2.zero, Vector2.one, false);
        RenderTexture.ReleaseTemporary(rt);
        rt = null;
    }

    IEnumerator CaptureAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        if (!Active) yield break;
        try
        {
            int w = Screen.width, h = Screen.height;
            rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.name = "MenuLeaveFrame";
            if (!rt.IsCreated()) rt.Create();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            Flipped = FlipOverride ?? SystemInfo.graphicsUVStartsAtTop;
            Vector2 size = new Vector2(w, h);
            held.SetTexture(rt, -size * 0.5f, size, Flipped);
            foreach (var p in panels) p.fill.SetTexture(rt, -size * 0.5f - p.hinge, size, Flipped);
            Captured = true;
        }
        catch (Exception e)
        {
            CaptureFailed = true;
            ReleaseFrame();
            Debug.LogWarning("MenuLeave: the menu frame could not be captured (" + e.Message + "): paper panels");
        }
        dirty = true;
    }

    // ------------------------------------------------------------------ geometry
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

    /// <summary>Where the ray from <paramref name="o"/> along <paramref name="d"/> leaves <paramref name="r"/>, and its perimeter coordinate
    /// (0..4, counter-clockwise from the bottom-left corner).</summary>
    static Vector2 Exit(Rect r, Vector2 o, Vector2 d, out float s)
    {
        float t = float.MaxValue;
        if (d.x > 1e-5f) t = Mathf.Min(t, (r.xMax - o.x) / d.x); else if (d.x < -1e-5f) t = Mathf.Min(t, (r.xMin - o.x) / d.x);
        if (d.y > 1e-5f) t = Mathf.Min(t, (r.yMax - o.y) / d.y); else if (d.y < -1e-5f) t = Mathf.Min(t, (r.yMin - o.y) / d.y);
        Vector2 h = o + d * t;
        s = Perimeter(r, h);
        return h;
    }

    static float Perimeter(Rect r, Vector2 h)
    {
        const float e = 0.5f;
        if (Mathf.Abs(h.y - r.yMin) < e && h.x < r.xMax - e) return (h.x - r.xMin) / r.width;               // bottom, left → right
        if (Mathf.Abs(h.x - r.xMax) < e && h.y < r.yMax - e) return 1f + (h.y - r.yMin) / r.height;         // right, up
        if (Mathf.Abs(h.y - r.yMax) < e && h.x > r.xMin + e) return 2f + (r.xMax - h.x) / r.width;          // top, right → left
        return 3f + (r.yMax - h.y) / r.height;                                                              // left, down
    }

    static Vector2 Corner(Rect r, int c)
    {
        switch (((c % 4) + 4) % 4)
        {
            case 0: return new Vector2(r.xMin, r.yMin);
            case 1: return new Vector2(r.xMax, r.yMin);
            case 2: return new Vector2(r.xMax, r.yMax);
            default: return new Vector2(r.xMin, r.yMax);
        }
    }

    static float Area(List<Vector2> p)
    {
        float a = 0f;
        for (int i = 0; i < p.Count; i++) { Vector2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
        return 0.5f * a;
    }

    static Vector2 Centroid(List<Vector2> p)
    {
        float a = 0f; Vector2 c = Vector2.zero;
        for (int i = 0; i < p.Count; i++)
        {
            Vector2 u = p[i], v = p[(i + 1) % p.Count];
            float cr = u.x * v.y - v.x * u.y;
            a += cr; c += (u + v) * cr;
        }
        return Mathf.Abs(a) > 1e-3f ? c / (3f * a) : p[0];
    }

    /// <summary>Cut lines: 3-4 slanted rays from the click point (never within 12° of the screen's axes, at least 55° apart); a wedge
    /// smaller than 5 % of the screen gives its ray up (3 panels). Each wedge is a panel: P, its ray's exit, the corners in between, the
    /// next ray's exit.</summary>
    void BuildPanels()
    {
        Rect r = new Rect(-screenSize * 0.5f, screenSize);
        Vector2 P = new Vector2(Mathf.Clamp(originCanvas.x, r.xMin + 24f * k, r.xMax - 24f * k), Mathf.Clamp(originCanvas.y, r.yMin + 24f * k, r.yMax - 24f * k));
        var angles = new List<float>(4);
        float a0 = 45f + Mathf.Lerp(-22f, 22f, Hash01(seed, 1));
        for (int i = 0; i < 4; i++)
        {
            float a = a0 + 90f * i + Mathf.Lerp(-16f, 16f, Hash01(seed, 10 + i));
            float m = Mathf.Repeat(a, 90f);
            if (m < 12f) a += 12f - m; else if (m > 78f) a -= m - 78f;
            angles.Add(Mathf.Repeat(a, 360f));
        }
        angles.Sort();
        for (int pass = 0; pass < 2; pass++)
        {
            // drop a ray whose wedge would be a sliver (or too narrow an angle): 3 panels
            if (angles.Count <= 3) break;
            int worst = -1; float worstArea = float.MaxValue;
            for (int i = 0; i < angles.Count; i++)
            {
                Wedge(r, P, angles[i], angles[(i + 1) % angles.Count], scratch);
                float span = Mathf.Repeat(angles[(i + 1) % angles.Count] - angles[i], 360f);
                float ar = Mathf.Abs(Area(scratch)) / (r.width * r.height);
                if ((ar < 0.05f || span < 55f) && ar < worstArea) { worstArea = ar; worst = i; }
            }
            if (worst < 0) break;
            angles.RemoveAt((worst + 1) % angles.Count);
        }

        EnsurePanels(angles.Count);
        for (int i = 0; i < panels.Count; i++)
        {
            var p = panels[i];
            Wedge(r, P, angles[i], angles[(i + 1) % angles.Count], scratch);
            Vector2 c = Centroid(scratch);
            p.dir = (c - P).sqrMagnitude > 1f ? (c - P).normalized : new Vector2(Mathf.Cos(angles[i] * Mathf.Deg2Rad), Mathf.Sin(angles[i] * Mathf.Deg2Rad));
            // swing panels hinge on their vertex farthest from the click (a screen corner); slides turn a little about their centre
            p.swing = Hash01(seed, 40 + i) < 0.5f && scratch.Count >= 4;
            Vector2 hinge = c; float far = -1f;
            if (p.swing) foreach (var q in scratch) { float dd = (q - P).sqrMagnitude; if (dd > far) { far = dd; hinge = q; } }
            p.hinge = hinge;
            Vector2 tip = P - hinge;
            float sgn = Mathf.Sign(Vector2.Dot(new Vector2(-tip.y, tip.x), p.dir));
            if (sgn == 0f) sgn = 1f;
            p.angle = p.swing ? sgn * Mathf.Lerp(26f, 44f, Hash01(seed, 50 + i)) : (Hash01(seed, 60 + i) < 0.5f ? -1f : 1f) * Mathf.Lerp(5f, 11f, Hash01(seed, 70 + i));
            p.local.Clear();
            foreach (var q in scratch) p.local.Add(q - hinge);
            p.start = PanelsAt + (i % 2 == 0 ? 0 : 1);
            p.drawings = 5 + (Hash01(seed, 80 + i) < 0.5f ? 0 : 1);
            p.dist = ExitDistance(p, r);
            p.gone = false;

            float inkW = 8.5f * k;
            p.fill.SetPolygon(p.local);
            p.fill.color = Color.white;
            if (mat != null && p.fill.material != mat) p.fill.material = mat;   // a paper fallback may have cleared it
            p.ink.SetPolygon(p.local);
            p.ink.SetInk(inkW * 0.8f, inkW * 1.25f, 1.8f * k, 3f * k, Comic.Cream, seed * 31 + i);
            p.shadow.SetPolygon(p.local);
            p.rt.anchoredPosition = hinge; p.rt.localRotation = Quaternion.identity; p.rt.localScale = Vector3.one;
            p.shadowRt.anchoredPosition = hinge; p.shadowRt.localRotation = Quaternion.identity; p.shadowRt.localScale = Vector3.one;
            p.rt.gameObject.SetActive(true); p.shadowRt.gameObject.SetActive(true);
        }
        // the held frame covers the whole screen
        scratch.Clear();
        scratch.Add(new Vector2(r.xMin, r.yMin)); scratch.Add(new Vector2(r.xMax, r.yMin)); scratch.Add(new Vector2(r.xMax, r.yMax)); scratch.Add(new Vector2(r.xMin, r.yMax));
        held.SetPolygon(scratch);
        held.rectTransform.anchoredPosition = Vector2.zero;
    }

    void Wedge(Rect r, Vector2 P, float aDeg, float bDeg, List<Vector2> into)
    {
        into.Clear();
        Vector2 da = new Vector2(Mathf.Cos(aDeg * Mathf.Deg2Rad), Mathf.Sin(aDeg * Mathf.Deg2Rad));
        Vector2 db = new Vector2(Mathf.Cos(bDeg * Mathf.Deg2Rad), Mathf.Sin(bDeg * Mathf.Deg2Rad));
        float sa, sb;
        Vector2 ha = Exit(r, P, da, out sa), hb = Exit(r, P, db, out sb);
        if (sb <= sa + 1e-4f) sb += 4f;
        into.Add(P); into.Add(ha);
        for (int c = Mathf.FloorToInt(sa) + 1; c < sb; c++) into.Add(Corner(r, c));
        into.Add(hb);
    }

    /// <summary>How far along its direction a panel must travel (with its final turn) so that it and its shadow are off the screen.</summary>
    float ExitDistance(Panel p, Rect r)
    {
        Rect grown = new Rect(r.xMin - 30f * k, r.yMin - 30f * k, r.width + 60f * k, r.height + 60f * k);
        float lo = 0f, hi = (r.width + r.height) * 2f;
        Quaternion q = Quaternion.Euler(0f, 0f, p.angle);
        for (int it = 0; it < 24; it++)
        {
            float mid = 0.5f * (lo + hi);
            bool outside = true;
            Vector2 at = p.hinge + p.dir * mid;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var v in p.local)
            {
                Vector2 w = at + (Vector2)(q * (v * 1.04f));
                x0 = Mathf.Min(x0, w.x); y0 = Mathf.Min(y0, w.y); x1 = Mathf.Max(x1, w.x); y1 = Mathf.Max(y1, w.y);
            }
            if (x1 > grown.xMin && x0 < grown.xMax && y1 > grown.yMin && y0 < grown.yMax) outside = false;
            if (outside) hi = mid; else lo = mid;
        }
        return hi;
    }

    void EnsurePanels(int count)
    {
        while (panels.Count < count)
        {
            int i = panels.Count;
            var p = new Panel();
            p.shadow = MenuPanel.Create(shadowsRoot, "Shadow" + i, MenuPanel.Mode.Solid, Comic.A(Comic.Ink, 0.92f));
            p.shadowRt = p.shadow.rectTransform;
            p.rt = UIKit.RT(UIKit.Obj("Panel" + i, panelsRoot));
            p.rt.anchorMin = p.rt.anchorMax = p.rt.pivot = new Vector2(0.5f, 0.5f); p.rt.sizeDelta = Vector2.zero;
            p.fill = MenuPanel.Create(p.rt, "Frame", MenuPanel.Mode.Fill, Color.white);
            if (mat != null) p.fill.material = mat;
            p.ink = MenuPanel.Create(p.rt, "Ink", MenuPanel.Mode.Ink, Comic.Ink);
            panels.Add(p);
        }
        while (panels.Count > count)
        {
            var p = panels[panels.Count - 1];
            Destroy(p.rt.gameObject); Destroy(p.shadowRt.gameObject);
            panels.RemoveAt(panels.Count - 1);
        }
    }

    void BuildSticker()
    {
        word.text = Word; wordMis.text = Word;
        word.fontSize = 124f * k; wordMis.fontSize = 124f * k;
        float w = word.GetPreferredValues(Word, 4000f, 400f).x;
        Vector2 size = new Vector2(w + 84f * k, 162f * k);
        UIKit.RT(word).sizeDelta = size; UIKit.RT(wordMis).sizeDelta = size;
        UIKit.RT(word).anchoredPosition = new Vector2(0f, 4f * k);
        UIKit.RT(wordMis).anchoredPosition = new Vector2(-6f * k, 8f * k);
        sticker.rectTransform.sizeDelta = size;
        sticker.SetInk(3.5f * k, 6.5f * k);
        sticker.ShadowOffset = new Vector2(10f, -12f) * k;
        sticker.Wobble = 1.6f * k;
        sticker.Seed = seed;
        // keep the whole sticker on the screen
        Vector2 half = size * 0.5f + new Vector2(28f, 28f) * k;
        Rect r = new Rect(-screenSize * 0.5f, screenSize);
        Vector2 at = new Vector2(Mathf.Clamp(originCanvas.x, r.xMin + half.x, r.xMax - half.x), Mathf.Clamp(originCanvas.y, r.yMin + half.y, r.yMax - half.y));
        stickerRoot.anchoredPosition = at;
    }

    // ------------------------------------------------------------------ drawing
    void Draw(int d)
    {
        bool frame = Captured;
        if (d < ImpactAt)
        {
            // anticipation: the live menu (the squashed item) until the frame is captured, then the held frame
            held.gameObject.SetActive(frame);
            if (frame) heldDrawn = true;
            return;
        }
        if (d == ImpactAt)
        {
            // the impact frame: the page printed in two tones, the cuts inked, the sticker slapped on
            if (!ImpactShown) AudioPool.UI(ProceduralAudio.Pop(), 0.42f, 0.72f);
            ImpactShown = true;
            if (mat != null) mat.SetFloat("_Impact", 1f);
            held.gameObject.SetActive(frame);
            white.gameObject.SetActive(!frame);
            shadowsRoot.gameObject.SetActive(false);
            panelsRoot.gameObject.SetActive(true);
            foreach (var p in panels) { p.fill.gameObject.SetActive(false); p.ink.Boil = d; }
            PoseStickerAt(d);
            return;
        }
        if (!ImpactShown) ImpactShown = true;   // a hitch skipped the impact drawing
        if (mat != null) mat.SetFloat("_Impact", 0f);
        held.gameObject.SetActive(false);
        white.gameObject.SetActive(false);
        shadowsRoot.gameObject.SetActive(true);
        panelsRoot.gameObject.SetActive(true);
        foreach (var p in panels)
        {
            p.fill.gameObject.SetActive(true);
            if (!frame) { p.fill.SetTexture(null, Vector2.zero, Vector2.one, false); p.fill.material = null; p.fill.color = Comic.Cream; }
            p.ink.Boil = d;
        }
        if (!whooshed) { whooshed = true; AudioPool.UI(ProceduralAudio.Whoosh(), 0.32f, 0.82f); }
        PosePanels(Look.OnTwos ? d * Drawing : T);
        PoseStickerAt(d);
    }

    /// <summary>Panel poses at leave time <paramref name="t"/>: a crack (the gutters open, the paper lifts), then each panel
    /// accelerates out along its direction, slides turning a little or swinging on its hinge corner.</summary>
    void PosePanels(float t)
    {
        poses++;
        float j0 = t / Drawing;
        foreach (var p in panels)
        {
            float j = j0 - p.start;
            if (j < 0f) { p.rt.anchoredPosition = p.hinge; p.shadowRt.anchoredPosition = p.hinge + ShadowOffset(0f); continue; }
            float u = j < 1f ? Mathf.Lerp(0.025f, 0.07f, j) : 0.07f + 0.93f * Mathf.Pow(Mathf.Clamp01((j - 1f) / Mathf.Max(1f, p.drawings - 1f)), 2.2f);
            float lift = Mathf.Clamp01((j + 1f) / 2f);
            float rot = p.angle * (p.swing ? Mathf.Pow(u, 0.7f) : u);
            Vector2 at = p.hinge + p.dir * (p.dist * u);
            var q = Quaternion.Euler(0f, 0f, rot);
            float s = 1f + 0.035f * lift;
            p.rt.anchoredPosition = at; p.rt.localRotation = q; p.rt.localScale = new Vector3(s, s, 1f);
            p.shadowRt.anchoredPosition = at + ShadowOffset(lift); p.shadowRt.localRotation = q; p.shadowRt.localScale = p.rt.localScale;
            bool gone = u >= 0.999f;
            if (gone != p.gone) { p.gone = gone; p.rt.gameObject.SetActive(!gone); p.shadowRt.gameObject.SetActive(!gone); }
        }
    }

    /// <summary>The sticker per drawing from the impact: slapped on big, settles, holds, puffs, gone.</summary>
    static readonly float[] StickerScale = { 1.3f, 0.94f, 1f, 1f, 1.1f }, StickerTurn = { -12f, -7f, -8f, -8f, -6f };

    Vector2 ShadowOffset(float lift) => new Vector2(9f, -12f) * k * (1f + 1.4f * lift);

    void PoseStickerAt(int d)
    {
        int s = d - ImpactAt;
        if (s < 0 || s > 4) { stickerRoot.gameObject.SetActive(false); return; }
        stickerRoot.gameObject.SetActive(true);
        float jitter = Mathf.Lerp(-3f, 3f, Hash01(seed, 90));
        stickerRoot.localScale = Vector3.one * StickerScale[s];
        stickerRoot.localRotation = Quaternion.Euler(0f, 0f, StickerTurn[s] + jitter);
    }
}
