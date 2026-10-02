using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 (SPEC v7 §16.4, package U2) — "you should also be able to hold shift and drag over a section to then select all the grids in that section
/// and like paste or delete or other options" — "or transpose up an octave or down an octave etc": the SELECTION BAR. While grids are selected
/// (GridSelection: the Shift marquee, Shift + click, a section plinth — package D / O) a floating paper card hangs over them (its tail on the
/// selection's top edge; below them when there is no room above; re-projected every frame), icons first, captions on hover:
/// COPY (the grids + their cubes to the grid clipboard) · PASTE (after the selection) · DUPLICATE · DELETE (the red tear-off) | OCTAVE ↑ /
/// OCTAVE ↓ (register ± 1: the grid rises on its tower) | REPEAT (cycles 1 → 4 passes, pips show the count) · EXTEND (v7 §21: the first grid
/// becomes the LONG GRID through the selection — "the grid extending and being longer and the cubes just going from left to right through the
/// grid") · BUILD-UP (the launch: a riser swells into the next section and a crash lands on it; §21.4: no fling). Every action is one SongOps
/// call = one History entry (O); the selection follows its grids (O re-selects), the bar hides when it empties. The keys (UIManager): Cmd+C /
/// Cmd+V / Delete / Shift+↑ / Shift+↓ act on the selection when there is one, Shift+→ toggles the build-up. The grids' own look (outline,
/// lift, shimmer) is W's (SelectionFx). Hints id "selectionBar".
/// </summary>
public class HudSelection : MonoBehaviour
{
    public static HudSelection I;
    public enum Act { Copy, Paste, Duplicate, Delete, OctaveUp, OctaveDown, Repeat, Extend, Launch, Lead }
    public static readonly string[] Captions = { "copy grids", "paste after", "duplicate", "delete", "octave up", "octave down", "repeat", "extend through", "build-up", "lead · spotlight" };
    public const float Btn = 46f, Pad = 12f, GroupGap = 14f, H = 66f, Above = 18f;
    /// <summary>Screen bands kept free (reference px): the column rail on top, the transport / deck at the bottom.</summary>
    public const float TopBand = 90f, BottomBand = 130f;

    /// <summary>The bar is up (a selection, the HUD showing).</summary>
    public bool Shown => rt != null && rt.gameObject.activeSelf && popState == 2;
    public RectTransform Root => rt;
    public InkButton Button(Act a) => btns[(int)a];
    /// <summary>The bar sits below the selection (no room above).</summary>
    public bool PlacedBelow => below;
    /// <summary>The selection's screen rect this frame (px; empty when nothing is on screen).</summary>
    public Rect SelectionScreenRect => selRect;
    /// <summary>Actions taken through the bar or the keys (tests) and the last one.</summary>
    public static int ActionCount { get; private set; }
    public static Act LastAction { get; private set; }

    RectTransform root, rt; CanvasGroup cg; InkShape paper;
    readonly InkButton[] btns = new InkButton[10];   // v9 (L): + lead
    readonly InkPainter[] arts = new InkPainter[10];
    float width; bool below; Rect selRect; int popState, popK, tick = int.MinValue, sig = int.MinValue;
    Vector2 lastTail = new Vector2(float.NaN, float.NaN);
    static readonly Vector3[] corners = new Vector3[8];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ActionCount = 0; LastAction = Act.Copy; }

    public static HudSelection Build(RectTransform hud)
    {
        var node = HudKit.Node(hud, "SelectionBar", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        var s = node.gameObject.AddComponent<HudSelection>();
        s.root = node;   // (always active: the card inside it comes and goes)
        s.BuildParts();
        return s;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    // ================================================================== build
    void BuildParts()
    {
        width = Pad * 2f + Btn * btns.Length + GroupGap * 2f;
        rt = InkUI.Node("Card", root);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, H);
        rt.pivot = new Vector2(0.5f, 0.5f);
        cg = rt.gameObject.AddComponent<CanvasGroup>();
        paper = InkShape.Create(rt, "Paper", InkShape.Kind.Bubble, Comic.Cream, new Vector2(width, H));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = true;   // the card eats clicks between its buttons (no world pick through it)
        paper.Radius = 18f; paper.SetInk(2.2f, 3.8f); paper.Wobble = 1.2f; paper.ShadowOffset = new Vector2(6f, -7f); paper.ShadowColor = Comic.PrintShadow;
        paper.TailWidth = 22f; paper.Seed = 9403; paper.RotJitter = 0.5f;
        float x = -width * 0.5f + Pad;
        for (int i = 0; i < btns.Length; i++)
        {
            if (i == 4 || i == 6) x += GroupGap;
            int ii = i;
            var b = InkButton.Create(rt, ((Act)i).ToString(), new Vector2(Btn, Btn), Captions[i]);
            InkUI.Centre((RectTransform)b.transform, new Vector2(x + Btn * 0.5f, 0f), new Vector2(Btn, Btn));
            b.hoverScale = 1.12f;
            var art = InkUI.Node("Art", b.body);
            InkUI.Stretch(art);
            art.localScale = new Vector3(1.22f, 1.22f, 1f);   // the pictures fill the 46 px buttons
            arts[i] = art.gameObject.AddComponent<InkPainter>(); arts[i].raycastTarget = false;
            arts[i].onPaint = p => Paint(p, (Act)ii);
            b.onClick = () => Do((Act)ii);
            btns[i] = b;
            x += Btn;
        }
        Hints.Register("selectionBar", rt);
        rt.gameObject.SetActive(false);
    }

    // ================================================================== actions (one SongOps call each = one History entry)
    static List<KeyBlock> Sel => GridSelection.Selected;

    /// <summary>The column right after the selection's last column (where a paste lands).</summary>
    public static int ColumnAfter()
    {
        int last = -1;
        foreach (var kb in Sel) if (kb != null && !kb.IsMoon) last = Mathf.Max(last, kb.column);
        var sm = SongManager.I;
        return last < 0 ? (sm != null ? sm.ColumnCount : 0) : last + 1;
    }

    /// <summary>The selection's repeat now (the most passes among its grids, 1..4).</summary>
    public static int RepeatOf() { int n = 0; foreach (var kb in Sel) if (kb != null && !kb.IsMoon) n = Mathf.Max(n, kb.repeat); return Mathf.Max(1, n); }
    /// <summary>The selection is one long grid: its first grid extends (carry &gt; 0; §21.3: the carry style no longer matters).</summary>
    public static bool ExtendOf() { foreach (var kb in Sel) if (kb != null && !kb.IsMoon) return kb.carry > 0; return false; }
    /// <summary>The selection launches: a grid of its last column flings into the next grid (SongOps.SetLaunchGrids launches the selection from
    /// its end; off stops every selected grid).</summary>
    public static bool LaunchOf()
    {
        int last = -1;
        foreach (var kb in Sel) if (kb != null && !kb.IsMoon && !kb.IsStairs) last = Mathf.Max(last, kb.column);
        if (last < 0) return false;
        foreach (var kb in Sel) if (kb != null && !kb.IsMoon && !kb.IsStairs && kb.column == last && kb.launch) return true;
        return false;
    }
    /// <summary>v9 (L): the selection is lead — every chord grid of it is marked lead (keyboards, stairs and phrases are lead anyway).</summary>
    public static bool LeadOf()
    {
        bool any = false;
        foreach (var kb in Sel) { if (kb == null || kb.IsMoon || kb.kind != 0) continue; if (!kb.lead) return false; any = true; }
        return any;
    }
    static bool RegisterRoom(int dir) { foreach (var kb in Sel) if (kb != null && !kb.IsMoon && (dir > 0 ? kb.register < 2 : kb.register > -2)) return true; return false; }

    /// <summary>Does <paramref name="a"/> on the selection (the bar's buttons and UIManager's keys). False when refused (nothing selected,
    /// nothing copied, …).</summary>
    public static bool Do(Act a)
    {
        var sel = Sel;
        if (sel.Count == 0 && a != Act.Paste) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return false; }
        var list = new List<KeyBlock>(sel);
        bool ok = true;
        switch (a)
        {
            case Act.Copy:
                ok = SongOps.CopyGrids(list);
                if (ok) AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.35f);
                break;
            case Act.Paste:
                if (!SongOps.HasCopiedGrids) { ok = false; break; }
                ok = SongOps.PasteGrids(ColumnAfter()).Count > 0;
                break;
            case Act.Duplicate: ok = SongOps.DuplicateGrids(list).Count > 0; break;
            case Act.Delete: SongOps.DeleteGrids(list); break;
            case Act.OctaveUp: if (!RegisterRoom(1)) { ok = false; break; } SongOps.TransposeGrids(list, 1); break;
            case Act.OctaveDown: if (!RegisterRoom(-1)) { ok = false; break; } SongOps.TransposeGrids(list, -1); break;
            case Act.Repeat: SongOps.SetRepeatGrids(list, RepeatOf() % ProjectConfig.MaxRepeat + 1); break;
            case Act.Extend: SongOps.SetFlowGrids(list, ExtendOf() ? 0 : 1); break;
            case Act.Launch: SongOps.SetLaunchGrids(list, !LaunchOf()); break;
            case Act.Lead: ok = SongManager.I != null && SongManager.I.SetLeadGrids(list, !LeadOf()); break;
        }
        if (!ok) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return false; }
        ActionCount++; LastAction = a;
        if (I != null) { I.sig = int.MinValue; if (I.btns[(int)a] != null) Pop(I.btns[(int)a]); }
        return true;
    }

    static void Pop(InkButton b) { if (b != null && b.body != null) b.body.localScale = new Vector3(1.14f, 1.14f, 1f); }

    // ================================================================== per frame
    void LateUpdate()
    {
        if (rt == null) return;
        bool want = GridSelection.Any && HudAlpha() > 0.5f && !CubeInspector.IsOpen && !Presenter.Active && !MainMenu.IsShown;
        if (want && !Project()) want = false;   // nothing of the selection on screen
        if (want && (popState == 0 || popState == 3)) { popState = 1; popK = 0; if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true); sig = int.MinValue; lastTail.x = float.NaN; }
        else if (!want && (popState == 1 || popState == 2)) { popState = 3; popK = 0; }
        if (popState == 0) return;
        if (want) Place();
        int s = StateSig();
        if (s != sig) { sig = s; Refresh(); }
        Animate();
    }

    CanvasGroup hudGroup;
    float HudAlpha()
    {
        var ui = UIManager.I;
        if (ui == null || ui.HudRoot == null) return 1f;
        if (hudGroup == null) hudGroup = ui.HudRoot.GetComponent<CanvasGroup>();
        return hudGroup != null ? hudGroup.alpha : 1f;
    }

    /// <summary>The selection's screen rect from its grids' visual bounds (false when none is in front of the camera).</summary>
    bool Project()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        bool any = false;
        foreach (var kb in Sel)
        {
            if (kb == null) continue;
            var b = kb.VisualBounds;
            Vector3 c = b.center, e = b.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z);
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                any = true;
                x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
            }
        }
        if (!any || x1 < 0f || x0 > Screen.width || y1 < 0f || y0 > Screen.height) { selRect = new Rect(); return false; }
        selRect = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    /// <summary>Over the selection's top edge (centred, kept on screen and clear of the rail); below its bottom edge when there is no room above.</summary>
    void Place()
    {
        var parent = root.parent as RectTransform;   // (the root sits at the parent's centre: its local space is the parent's)
        if (parent == null) return;
        float upp = InkUI.UnitsPerPixel;
        float needAbove = (Above + H + TopBand) / Mathf.Max(1e-4f, upp);
        float cx = Mathf.Clamp(selRect.center.x, 0f, Screen.width);
        float topY = Mathf.Min(selRect.yMax, Screen.height), botY = Mathf.Max(selRect.yMin, 0f);
        below = topY + needAbove > Screen.height && botY - (Above + H + BottomBand) / Mathf.Max(1e-4f, upp) >= 0f;
        Vector2 edge;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(cx, below ? botY : topY), null, out edge)) return;
        Rect pr = parent.rect;
        float x = Mathf.Clamp(edge.x, pr.xMin + width * 0.5f + 12f, pr.xMax - width * 0.5f - 12f);
        float y = below ? edge.y - Above - H * 0.5f : edge.y + Above + H * 0.5f;
        y = Mathf.Clamp(y, pr.yMin + BottomBand * 0.6f + H * 0.5f, pr.yMax - TopBand - H * 0.5f);
        var at = new Vector2(x, y);
        if ((rt.anchoredPosition - at).sqrMagnitude > 0.01f) rt.anchoredPosition = at;
        // the tail points at the selection's edge (re-aimed only when it moved: no mesh rebuild at rest)
        Vector2 tip = new Vector2(Mathf.Clamp(edge.x - x, -width * 0.5f + 24f, width * 0.5f - 24f), below ? H * 0.5f + 12f : -H * 0.5f - 12f);
        if (float.IsNaN(lastTail.x) || (tip - lastTail).sqrMagnitude > 9f) { lastTail = tip; paper.TailTip = tip; }
    }

    int StateSig()
    {
        unchecked
        {
            int h = Sel.Count;
            h = h * 31 + RepeatOf(); h = h * 31 + (ExtendOf() ? 1 : 0); h = h * 31 + (LaunchOf() ? 1 : 0); h = h * 31 + (LeadOf() ? 1 : 0);
            h = h * 31 + (SongOps.HasCopiedGrids ? 1 : 0); h = h * 31 + (RegisterRoom(1) ? 1 : 0); h = h * 31 + (RegisterRoom(-1) ? 1 : 0);
            return h;
        }
    }

    void Refresh()
    {
        btns[(int)Act.Paste].Interactable = SongOps.HasCopiedGrids;
        btns[(int)Act.OctaveUp].Interactable = RegisterRoom(1);
        btns[(int)Act.OctaveDown].Interactable = RegisterRoom(-1);
        btns[(int)Act.Extend].Active = ExtendOf();
        btns[(int)Act.Launch].Active = LaunchOf();
        btns[(int)Act.Lead].Active = LeadOf();
        btns[(int)Act.Extend].transform.localRotation = Quaternion.Euler(0f, 0f, ExtendOf() ? 3f : 0f);
        btns[(int)Act.Launch].transform.localRotation = Quaternion.Euler(0f, 0f, LaunchOf() ? 3f : 0f);
        for (int i = 0; i < arts.Length; i++) arts[i].Repaint();
    }

    /// <summary>On twos: the pop in / out.</summary>
    void Animate()
    {
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float s = 1f, a = 1f;
        if (popState == 1) { s = PopIn[popK]; if (++popK >= PopIn.Length) popState = 2; }
        else if (popState == 3)
        {
            if (popK < PopOut.Length) { s = PopOut[popK]; a = 1f - 0.3f * popK; popK++; }
            else { rt.gameObject.SetActive(false); popState = 0; return; }
        }
        rt.localScale = new Vector3(s, s, 1f);
        cg.alpha = a;
        bool live = popState == 1 || popState == 2;
        cg.blocksRaycasts = live; cg.interactable = live;
    }

    /// <summary>Tests: one frame of the bar's own work.</summary>
    public void StepForTest() { LateUpdate(); }

    static readonly float[] PopIn = { 0.62f, 1.06f, 1f }, PopOut = { 0.9f, 0.7f, 0.5f };

    // ================================================================== pictures
    static readonly List<Vector2> pts = new List<Vector2>(32);

    /// <summary>A little grid: an isometric 2 × 2 tile in <paramref name="fill"/>, inked.</summary>
    static void Grid(InkPainter p, Vector2 c, float hw, float hh, Color fill, float inkW = 1.8f, float alpha = 1f, bool dashed = false)
    {
        pts.Clear(); pts.Add(c + new Vector2(-hw, 0f)); pts.Add(c + new Vector2(0f, hh)); pts.Add(c + new Vector2(hw, 0f)); pts.Add(c + new Vector2(0f, -hh));
        p.Fill(pts, 4, Comic.A(fill, fill.a * alpha));
        p.Stroke(pts, 4, inkW, Comic.A(Comic.Ink, alpha), true, dashed ? 3f : 0f, dashed ? 2.4f : 0f);
        Color hatch = Comic.A(Comic.Ink, 0.45f * alpha);
        p.Line(c + new Vector2(-hw * 0.5f, hh * 0.5f), c + new Vector2(hw * 0.5f, -hh * 0.5f), 1.1f, hatch);
        p.Line(c + new Vector2(-hw * 0.5f, -hh * 0.5f), c + new Vector2(hw * 0.5f, hh * 0.5f), 1.1f, hatch);
    }

    static void Card(InkPainter p, Vector2 c, float w, float h, float deg, Color fill)
    {
        pts.Clear();
        float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
        for (int k = 0; k < 4; k++)
        {
            float u = (k == 0 || k == 3) ? -0.5f : 0.5f, v = k < 2 ? -0.5f : 0.5f;
            Vector2 q = new Vector2(u * w, v * h);
            pts.Add(c + new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs));
        }
        p.InkFill(pts, 4, fill, 1.8f, Comic.Ink);
    }

    static void Arrow(InkPainter p, Vector2 from, Vector2 to, float w)
    {
        p.Line(from, to, w, Comic.Ink, true);
        p.Head(to + (to - from).normalized * 1.5f, to - from, 5.5f, 7f, Comic.Ink);
    }

    void Paint(InkPainter p, Act a)
    {
        Vector2 c = p.Area.center;
        Color tile = new Color(0.72f, 0.86f, 0.98f), paperDeep = InkUI.PaperDeep;
        switch (a)
        {
            case Act.Copy:
                Card(p, c + new Vector2(-4f, 4f), 22f, 18f, -10f, paperDeep);
                Card(p, c + new Vector2(3f, -2f), 22f, 18f, 5f, Comic.Cream);
                Grid(p, c + new Vector2(3f, -2f), 8f, 4.8f, tile, 1.4f);
                break;
            case Act.Paste:
            {
                Grid(p, c + new Vector2(-10f, 2f), 8f, 4.8f, tile, 1.4f, 0.55f);
                Grid(p, c + new Vector2(10f, -4f), 8f, 4.8f, tile, 1.6f);
                pts.Clear(); InkPainter.ArcPoints(pts, c + new Vector2(0f, 2f), 9f, 9f, 150f, 30f, 8);
                p.Stroke(pts, pts.Count, 1.6f, Comic.Ink);
                p.Head(pts[pts.Count - 1] + new Vector2(1f, -2f), new Vector2(0.45f, -1f), 5f, 6f, Comic.Ink);
                break;
            }
            case Act.Duplicate:
                Grid(p, c + new Vector2(-7f, 3f), 9f, 5.4f, tile, 1.6f);
                Grid(p, c + new Vector2(7f, -3f), 9f, 5.4f, tile, 1.6f, 1f, true);
                p.Line(c + new Vector2(12f, 9f), c + new Vector2(12f, 15f), 1.6f, Comic.Pop, true);
                p.Line(c + new Vector2(9f, 12f), c + new Vector2(15f, 12f), 1.6f, Comic.Pop, true);
                break;
            case Act.Delete:
            {
                pts.Clear();
                for (int k = 0; k < 4; k++) { float an = (45f + 90f * k + (k == 1 ? 6f : 0f)) * Mathf.Deg2Rad; pts.Add(c + new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * 13f); }
                p.Shift = new Vector2(2f, -2.5f); p.Fill(pts, 4, Comic.A(Comic.Ink, 0.85f)); p.Shift = Vector2.zero;
                p.InkFill(pts, 4, InkUI.Red, 1.8f, Comic.Ink);
                p.Line(c + new Vector2(-4.5f, -4.5f), c + new Vector2(4.5f, 4.5f), 2.6f, Comic.Cream, true);
                p.Line(c + new Vector2(-4.5f, 4.5f), c + new Vector2(4.5f, -4.5f), 2.6f, Comic.Cream, true);
                break;
            }
            case Act.OctaveUp:
            case Act.OctaveDown:
            {
                bool up = a == Act.OctaveUp;
                Vector2 g = c + new Vector2(-3f, up ? 4f : -6f);
                // a pillar under a raised grid (a tower), a dip for a lowered one
                if (up) { p.Line(g + new Vector2(-6f, -3f), g + new Vector2(-6f, -14f), 1.4f, Comic.A(Comic.Ink, 0.6f)); p.Line(g + new Vector2(6f, -3f), g + new Vector2(6f, -14f), 1.4f, Comic.A(Comic.Ink, 0.6f)); }
                else { p.Line(g + new Vector2(-11f, 8f), g + new Vector2(11f, 8f), 1.3f, Comic.A(Comic.Cyan, 0.9f)); }
                Grid(p, g, 10f, 6f, tile, 1.7f);
                Arrow(p, c + new Vector2(13f, up ? -8f : 8f), c + new Vector2(13f, up ? 9f : -9f), 1.8f);
                break;
            }
            case Act.Repeat:
            {
                int n = RepeatOf();
                pts.Clear(); InkPainter.ArcPoints(pts, c + new Vector2(0f, 2f), 11f, 9f, 110f, 420f, 20);
                p.Stroke(pts, pts.Count, 2f, Comic.Ink);
                p.Head(pts[pts.Count - 1], pts[pts.Count - 1] - pts[pts.Count - 3], 5f, 6f, Comic.Ink);
                Grid(p, c + new Vector2(0f, 2f), 5.5f, 3.4f, tile, 1.2f);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 d = c + new Vector2(-7.5f + 5f * k, -13f);
                    p.Disc(d, 2.2f, Comic.Ink);
                    if (k >= n) p.Disc(d, 1.2f, Comic.Cream);
                }
                break;
            }
            case Act.Extend: PaintExtend(p, c, 1f); break;
            case Act.Launch: PaintBuildUp(p, c, 1f); break;
            case Act.Lead: StageLights.PaintSpotlight(p, c, LeadOf(), 1f); break;
        }
    }

    /// <summary>v7 §21.2 — the LONG GRID: one platform of three measures joined (thin ink dividers at the chord changes), the first one solid
    /// with a cube on it, the two it extends over lighter, an arrow carrying on to the right. Shared with the shortcut sheet.</summary>
    public static void PaintExtend(InkPainter p, Vector2 c, float k)
    {
        Color tile = new Color(0.72f, 0.86f, 0.98f), light = new Color(0.86f, 0.93f, 0.99f);
        float w = 22f * k, h = 9f * k, x0 = c.x - 17f * k, y0 = c.y - 6f * k;
        var r = new Rect(x0, y0, w, h);
        p.RoundRect(r, 3f * k, light, 1.7f, Comic.Ink);
        p.RoundRect(new Rect(x0 + 1f, y0 + 1f, w / 3f - 1f, h - 2f), 2f * k, tile);
        p.Line(new Vector2(x0 + w / 3f, y0 + 1.5f), new Vector2(x0 + w / 3f, y0 + h - 1.5f), 1.2f, Comic.A(Comic.Ink, 0.7f));
        p.Line(new Vector2(x0 + 2f * w / 3f, y0 + 1.5f), new Vector2(x0 + 2f * w / 3f, y0 + h - 1.5f), 1.2f, Comic.A(Comic.Ink, 0.7f));
        p.Cube(new Vector2(x0 + w / 6f, y0 + h + 4.5f * k), 9f * k, tile, Comic.Ink, 1.1f);
        // the walk: dots stepping left → right along the long grid, then the arrow on
        for (int i = 1; i <= 2; i++) p.Disc(new Vector2(x0 + w / 6f + i * w / 3f, y0 + h + 2.5f * k), 1.6f * k, Comic.A(Comic.Ink, 0.6f));
        p.Line(new Vector2(x0 + w + 2f * k, c.y - 1.5f * k), new Vector2(x0 + w + 6f * k, c.y - 1.5f * k), 1.8f, Comic.Ink, true);
        p.Head(new Vector2(x0 + w + 9.5f * k, c.y - 1.5f * k), Vector2.right, 4.5f * k, 6f * k, Comic.Ink);
    }

    /// <summary>v7 §21.4 — the BUILD-UP: a hairpin swelling to the right (the riser) up to a bar line (the next section's downbeat), a crash
    /// burst on it. Shared with the shortcut sheet.</summary>
    public static void PaintBuildUp(InkPainter p, Vector2 c, float k)
    {
        Vector2 a = c + new Vector2(-14f, -1f) * k, bx = c + new Vector2(5f, 0f) * k;
        p.Line(a, bx + new Vector2(0f, 7f * k), 2f, Comic.Ink, true);
        p.Line(a, bx + new Vector2(0f, -9f * k), 2f, Comic.Ink, true);
        p.Line(c + new Vector2(8f, -11f) * k, c + new Vector2(8f, 10f) * k, 1.6f, Comic.A(Comic.Ink, 0.7f));   // the section's downbeat
        pts.Clear();
        Vector2 s = c + new Vector2(11.5f, 0f) * k;
        for (int i = 0; i < 12; i++) { float an = (90f + 30f * i) * Mathf.Deg2Rad; float rr = (i % 2 == 0 ? 7.5f : 3.4f) * k; pts.Add(s + new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * rr); }
        p.InkFill(pts, 12, Comic.Pop, 1.4f, Comic.Ink);
    }
}
