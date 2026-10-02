using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v4 world HUD, top-right (research §4.1): the one call to action in that corner — the PRESENT sticker, a picture with no word (v9: an island
/// rising out of the waves plus ▸ — the present mode raises the islands from the sea; it was v4's cascade of falling tiles), cream on an ink sticker with a cream die-cut edge, rotated −3°, printed with a 1.5 px magenta / cyan
/// misregistration and a hard magenta shadow. Hover: it jiggles on twos, the misprint widens to 3 px, the caption reads "present".
/// Also the contextual recentre sticker (research §4.1, Should): when no island centre has been in view for 1.5 s, a small sticker (an island
/// with a curved arrow) appears at the screen edge nearest the song; a click frames everything (the O key).
/// </summary>
public class HudPresent : MonoBehaviour
{
    public static HudPresent I;
    public HudButton Button => btn;
    public HudButton Recentre => recBtn;
    public RectTransform Sticker => rt;
    /// <summary>v9 (L): the "lights" switch beside the present sticker (StageLights.On: the dim stage and the melody spotlights while playing).</summary>
    public HudButton LightsButton => lightsBtn;
    public bool RecentreShown => recShown;

    RectTransform rt, recRt; HudButton btn, recBtn; InkPainter art; CanvasGroup group;
    HudButton lightsBtn; InkPainter lightsArt; CanvasGroup lightsGroup; int lightsShown = -1;   // v9 (L)
    float lostT, checkT; bool recShown, hoverShown;

    public static HudPresent Build(RectTransform hud)
    {
        var go = new GameObject("PresentCorner", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(hud, false);
        var root = (RectTransform)go.transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
        var p = go.AddComponent<HudPresent>();
        p.BuildParts(root);
        return p;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    void BuildParts(RectTransform root)
    {
        InkShape body;
        btn = HudKit.Sticker(root, "Present", new Vector2(1f, 1f), new Vector2(-74f, -58f), new Vector2(90f, 62f), InkShape.Kind.Sticker, Comic.Ink,
                             PaintRise, "present", Presenter.Toggle, out body, out art);
        rt = btn.transform as RectTransform;
        body.Ink = Comic.Cream; body.SetInk(1.8f, 2.6f);
        body.ShadowColor = Comic.Magenta; body.ShadowOffset = new Vector2(5f, -6f);
        body.RotJitter = 1.2f; body.Seed = 1311;
        rt.localEulerAngles = new Vector3(0f, 0f, -3f);
        var hov = btn.GetComponent<InkHover>(); hov.jiggle = 1.5f; hov.baseAngle = -3f; hov.hoverScale = 1.08f;
        Hints.Register("present", rt);
        group = rt.gameObject.AddComponent<CanvasGroup>();

        // v9 (L) the "lights" switch: a round cream sticker left of present — the standard stage spotlight, gold beam while on
        InkShape lb;
        lightsBtn = HudKit.Sticker(root, "Lights", new Vector2(1f, 1f), new Vector2(-152f, -56f), new Vector2(46f, 46f), InkShape.Kind.Circle, Comic.Cream,
                                   p => StageLights.PaintSpotlight(p, p.Area.center + new Vector2(0.5f, 1f), StageLights.On, 1.08f), "lights", StageLights.Toggle, out lb, out lightsArt);
        lb.SetInk(1.8f, 2.8f); lb.ShadowOffset = new Vector2(3f, -4f); lb.Seed = 1319;
        lightsGroup = lightsBtn.gameObject.AddComponent<CanvasGroup>();
        Hints.Register("lights", lightsBtn.transform as RectTransform);

        // the recentre sticker (hidden until the song is lost from view)
        InkShape rb; InkPainter ra;
        recBtn = HudKit.Sticker(root, "Frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 46f), InkShape.Kind.Sticker, Comic.Cream,
                                PaintRecentre, "find song", () => { if (OrbitCamera.I != null) OrbitCamera.I.FrameAll(); lostT = 0f; }, out rb, out ra);
        recRt = recBtn.transform as RectTransform;
        recRt.gameObject.SetActive(false);
    }

    static readonly List<Vector2> q = new List<Vector2>(8);

    void PaintRise(InkPainter p)
    {
        float m = btn != null && btn.Hover ? 3f : 1.5f;
        p.Shift = new Vector2(m, -0.4f); Rise(p, Comic.Magenta, false);
        p.Shift = new Vector2(-m, 0.4f); Rise(p, Comic.Cyan, false);
        p.Shift = Vector2.zero; Rise(p, Comic.Cream, true);
    }

    /// <summary>v9: an island (a platform with its lip) rising out of two wave lines, rise ticks above it and a splash drop each side, then ▸.</summary>
    static void Rise(InkPainter p, Color col, bool top)
    {
        Vector2 c = p.Area.center + new Vector2(-9f, 1f);
        // the island: its lip (the same rhombus 3 px lower, darker) and its top face
        float w = 14f, h = 6.5f;
        Vector2 t = c + new Vector2(0f, 6f) + p.Jit(1, 0.4f);
        Vector2 lip = new Vector2(0f, -3.2f);
        q.Clear(); q.Add(t + lip + new Vector2(-w, 0f)); q.Add(t + lip + new Vector2(0f, h)); q.Add(t + lip + new Vector2(w, 0f)); q.Add(t + lip + new Vector2(0f, -h));
        p.Fill(q, 4, top ? new Color(0.72f, 0.64f, 0.80f) : col);
        q.Clear(); q.Add(t + new Vector2(-w, 0f)); q.Add(t + new Vector2(0f, h)); q.Add(t + new Vector2(w, 0f)); q.Add(t + new Vector2(0f, -h));
        p.Fill(q, 4, col);
        // rising: two ticks above it
        p.Line(t + new Vector2(-5f, h + 3f), t + new Vector2(-5f, h + 8f), 1.6f, col, true);
        p.Line(t + new Vector2(5f, h + 2f), t + new Vector2(5f, h + 7f), 1.6f, col, true);
        // the splash: a drop thrown off each side
        p.Disc(t + new Vector2(-w - 4f, 3f) + p.Jit(3, 0.4f), 1.7f, col);
        p.Disc(t + new Vector2(w + 4f, 4f) + p.Jit(4, 0.4f), 1.5f, col);
        // the sea: two wave lines under it
        for (int k = 0; k < 2; k++)
        {
            pts.Clear();
            float y0 = c.y - 6f - k * 6f, x0 = c.x - 22f + k * 4f, x1 = c.x + 22f - k * 4f;
            for (int i = 0; i <= 12; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / 12f);
                pts.Add(new Vector2(x, y0 + Mathf.Sin((x - c.x) * 0.42f + k * 1.3f) * 1.6f) + p.Jit(10 + i + k * 13, 0.25f));
            }
            p.Stroke(pts, pts.Count, k == 0 ? 2.2f : 1.7f, col, false, 0f, 0f, -1f, true);
        }
        Vector2 tip = p.Area.center + new Vector2(33f, -3f);
        q.Clear(); q.Add(tip); q.Add(tip + new Vector2(-13f, 9f) + p.Jit(7, 0.5f)); q.Add(tip + new Vector2(-12f, -9f) + p.Jit(8, 0.5f));
        p.Fill(q, 3, col);
    }

    void PaintRecentre(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(-2f, -2f);
        // a little island (rhombus platform with a lip) and a curved arrow swinging back to it
        q.Clear(); q.Add(c + new Vector2(-11f, 0f)); q.Add(c + new Vector2(0f, 6f)); q.Add(c + new Vector2(11f, 0f)); q.Add(c + new Vector2(0f, -6f));
        p.InkFill(q, 4, KeyBlock.PlatformColorOf(Palette.Accent), 1.6f, Comic.Ink);
        var arc = pts; arc.Clear();
        InkPainter.ArcPoints(arc, c + new Vector2(1f, 1f), 17f, 13f, 20f, 150f, 14);
        p.Stroke(arc, arc.Count, 2.4f, Comic.Ink, false, 0f, 0f, -1f, true);
        Vector2 e = arc[arc.Count - 1], d = (e - arc[arc.Count - 3]).normalized;
        p.Head(e + d * 3f, d, 6f, 7f, Comic.Ink);
    }
    static readonly List<Vector2> pts = new List<Vector2>(32);

    void Update()
    {
        if (btn == null || art == null || group == null) return;   // not built (or a domain reload dropped the references)
        // the misprint widens on hover: one repaint when the hover changes (the boil keeps redrawing while hovered)
        bool hov = btn.Hover;
        if (hov != hoverShown) { hoverShown = hov; art.Repaint(); }
        // hidden while the inspector owns the right side (research §4.1 "When the inspector is open")
        bool inspecting = CubeInspector.IsOpen;
        float a = inspecting ? 0f : 1f;
        if (group.alpha != a) { group.alpha = a; group.blocksRaycasts = !inspecting; }
        if (lightsGroup != null && lightsGroup.alpha != a) { lightsGroup.alpha = a; lightsGroup.blocksRaycasts = !inspecting; }
        int lo = StageLights.On ? 1 : 0;   // v9 (L): the switch's picture and caption follow StageLights.On
        if (lo != lightsShown && lightsArt != null) { lightsShown = lo; lightsArt.Repaint(); InkCaption.Attach(lightsBtn.gameObject, lo == 1 ? "lights · on" : "lights · off"); }
        UpdateRecentre();
    }

    void UpdateRecentre()
    {
        var sm = SongManager.I; var cam = Camera.main;
        bool can = sm != null && sm.HasSong && cam != null && !CubeInspector.IsOpen && !Presenter.Active && !MainMenu.IsShown && !WorldInput.WorldLocked;
        if (!can) { lostT = 0f; if (recShown) Show(false); return; }
        checkT -= Time.unscaledDeltaTime;
        if (checkT <= 0f)
        {
            checkT = 0.25f;
            bool any = false;
            foreach (var kb in sm.Islands)
            {
                if (kb == null) continue;
                var v = cam.WorldToViewportPoint(kb.Center);
                if (v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f) { any = true; break; }
            }
            lostT = any ? 0f : lostT + 0.25f;
        }
        bool want = lostT >= 1.5f;
        if (want != recShown) Show(want);
        if (!recShown) return;
        // at the screen edge nearest the song
        var s = cam.WorldToScreenPoint(sm.SongCenter);
        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), d = new Vector2(s.x, s.y) - centre;
        if (s.z < 0f) d = -d;
        if (d.sqrMagnitude < 1f) d = Vector2.down;
        var parent = recRt.parent as RectTransform;
        Rect r = parent.rect;
        float mx = r.width * 0.5f - 70f, my = r.height * 0.5f - 90f;
        Vector2 dir = d.normalized;
        float k = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? mx / Mathf.Abs(dir.x) : float.MaxValue, Mathf.Abs(dir.y) > 1e-4f ? my / Mathf.Abs(dir.y) : float.MaxValue);
        recRt.anchoredPosition = dir * k;
    }

    void Show(bool on)
    {
        recShown = on;
        HudKit.SetActive(recRt, on);
        if (on) AudioPool.UI(ProceduralAudio.Blip(), 0.2f, 1.1f);
    }
}
