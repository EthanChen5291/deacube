using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// v7 (SPEC v7 §13 / §16.4, package U1): the SECTION HEADER — the user: "i want to make really intuitive the idea of sections. like 4 beats in a
/// measure, 4 measures in a "section" of a musical idea … make it really visual, with support if needed". A small paper ribbon that hangs from
/// the front edge of a hovered section plinth (the ink-stone slab under a section's columns, package B), like the island header does for an
/// island: the section's LETTER tag (its measures as four pips — full ones inked, missing ones dotted: "a section is 4 measures"), then SPLIT
/// HERE (scissors: the boundary nearest the pointer — a dashed cut line shows it while the scissors are hovered), JOIN WITH THE NEXT, DUPLICATE
/// the section, DRUMS (a drum Moon whose part starts at the section's first column), LAUNCH INTO THE NEXT SECTION (§21: a build-up — its last
/// column's grids launch: a riser swells into the next section's first downbeat and a crash lands there; nothing moves) and DELETE (a red
/// tear-off ✕). Icons first, a caption on hover; every gesture = one History entry (the ops
/// push: SongOps / SongManager; a deduplicated safety push covers the rest). It shows after 0.2 s over a plinth where no island is hovered (the
/// island header wins), lingers 0.4 s, holds while the pointer is on it, hides while drawing / dragging / presenting / inspecting / in a menu.
/// Geometry: package B's plinths when they are there (SectionPlinth), else the union of the section's grids (+ margins).
/// SPEC v7 §17.1 (sections are objects — GarageBand's letters, Logic's arrangement markers): beside the letter a NAME button (the section's
/// role: intro, verse, chorus, bridge, drop, outro — or none) opens a fan of the six role pictures + "none" (hover = the word; a click =
/// SongOps.SetSectionRole). The pictures are <see cref="SectionRoles"/> glyphs, shared with the rail's brackets and the plinths.
/// Also here, <see cref="MeasureTag"/> (SPEC v7 §13.4): "while drawing a melody the length reads as measures" — an ink tag at a melody phrase's
/// end ("½ measure", "1 measure", "2 measures" … "4 measures · 1 section") that pops when the phrase grows.
/// </summary>
public class SectionHeader : MonoBehaviour
{
    public static SectionHeader I;
    /// <summary>The section whose header is on screen (-1 none).</summary>
    public static int Current => I != null && I.target >= 0 && I.shownA > 0.01f ? I.target : -1;
    public static bool IsShown => Current >= 0;
    /// <summary>Pins the header on section <paramref name="s"/> (tests, tips); <paramref name="worldX"/> = where along its front edge (NaN: its
    /// middle) — the split point follows it.</summary>
    public static void Show(int s, float worldX = float.NaN) { if (I != null) { I.pinned = s; I.pinX = worldX; I.lingerT = 0f; } }
    public static void Hide() { if (I != null) { I.pinned = -1; I.hoverCand = -1; I.lingerT = 9f; I.target = -1; } }

    public const float ShowDelay = 0.2f, Linger = 0.4f, BandW = 340f;
    /// <summary>The stone of the plinths (package B's SectionPlinth.Stone): the ribbon's tails, the letter's disc (its cream letter reads on it).</summary>
    public static Color Stone => SectionPlinth.Stone;

    // ------------------------------------------------------------------ test access
    public HudButton SplitButton => splitBtn;
    public HudButton JoinButton => joinBtn;
    public HudButton DuplicateButton => dupBtn;
    public HudButton DrumsButton => drumsBtn;
    public HudButton LaunchButton => launchBtn;
    public HudButton DeleteButton => deleteBtn;
    /// <summary>§17.1: the name button, the role fan (0 none, 1 intro … 6 outro) and whether it is open.</summary>
    public HudButton NameButton => nameBtn;
    public HudButton RoleItem(int role) => role >= 0 && role <= 6 ? roleBtns[role] : null;
    public bool RoleFanOpen => roleFanOpen;
    /// <summary>§17.1: the role the name button shows.</summary>
    public int RoleShown { get; private set; }
    public RectTransform Ribbon => ribbon;
    /// <summary>The column a split would start (-1: the section has one column).</summary>
    public int SplitColumn => splitCol;
    /// <summary>The letter on the tag.</summary>
    public string Letter => letterText != null ? letterText.text : "";
    public float Shown => shownA;
    public bool PointerOver => Hovering();
    public int Refusals { get; private set; }

    RectTransform root, ribbon, band, tagRt;
    InkShape bandShape, tailL, tailR; InkPainter tagArt, cutArt;
    TMPro.TextMeshProUGUI letterText;
    CanvasGroup group;
    HudButton splitBtn, joinBtn, dupBtn, drumsBtn, launchBtn, deleteBtn, nameBtn;
    InkPainter launchArt, drumsArt, joinArt, splitArt, nameArt;
    RectTransform roleFan; readonly HudButton[] roleBtns = new HudButton[7]; bool roleFanOpen;
    int pinned = -1, target = -1, hoverCand = -1, splitCol = -1, tick = int.MinValue, shownKey = int.MinValue;
    float pinX = float.NaN, hoverT, lingerT = 9f, shownA, anchorX = float.NaN, shakeT;
    Vector2 cut0, cut1; bool cutOk;

    public static SectionHeader Build(RectTransform hud)
    {
        var rt = HudKit.Stretch(hud, "SectionHeader");
        HudKit.SubCanvas(rt.gameObject, true);   // it moves with the camera: its own batch
        var h = rt.gameObject.AddComponent<SectionHeader>();
        h.root = rt;
        h.BuildParts();
        var tag = HudKit.Stretch(hud, "MeasureTag");
        HudKit.SubCanvas(tag.gameObject, false);
        tag.gameObject.AddComponent<MeasureTag>().Build(tag);
        return h;
    }

    void Awake() { I = this; }
    void Start() { UnderFixedHud(transform); }   // world-anchored: under every fixed control
    void OnDestroy() { if (I == this) I = null; }

    /// <summary>A world-anchored layer goes right after the island header (the HUD's first child: every fixed control stays above them).</summary>
    public static void UnderFixedHud(Transform t)
    {
        var ih = IslandHeader.I != null ? IslandHeader.I.transform : null;
        if (ih != null && ih.parent == t.parent) { if (ih.GetSiblingIndex() != 0) ih.SetAsFirstSibling(); t.SetSiblingIndex(1); }
        else t.SetAsFirstSibling();
    }

    void BuildParts()
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        // the cut line of "split here" (screen space, over the plinth)
        cutArt = InkPainter.Create(root, "Cut", Vector2.zero, PaintCut);
        var crt = cutArt.rectTransform; crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        ribbon = HudKit.Node(root, "Ribbon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BandW + 8f, 60f));
        tailL = InkShape.Create(ribbon, "TailL", InkShape.Kind.Ribbon, Stone, new Vector2(40f, 26f));
        tailL.raycastTarget = false; tailL.rectTransform.anchoredPosition = new Vector2(-BandW * 0.5f - 4f, -9f); tailL.rectTransform.localEulerAngles = new Vector3(0f, 0f, 8f); tailL.SetInk(1.8f, 2.8f);
        tailR = InkShape.Create(ribbon, "TailR", InkShape.Kind.Ribbon, Stone, new Vector2(40f, 26f));
        tailR.raycastTarget = false; tailR.rectTransform.anchoredPosition = new Vector2(BandW * 0.5f + 4f, -9f); tailR.rectTransform.localEulerAngles = new Vector3(0f, 0f, -8f); tailR.SetInk(1.8f, 2.8f);
        band = HudKit.Node(ribbon, "Band", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BandW, 46f));
        bandShape = band.gameObject.AddComponent<InkShape>();
        bandShape.Shape = InkShape.Kind.RoundRect; bandShape.color = Comic.Cream; bandShape.Radius = 7f; bandShape.Wobble = 1.1f; bandShape.RotJitter = 0.6f;
        bandShape.SetInk(2f, 3.2f); bandShape.ShadowOffset = new Vector2(3f, -4f); bandShape.raycastTarget = true;
        // the letter tag: a stone disc lettered in Bangers, the section's measures as pips under it
        tagRt = HudKit.Node(band, "Letter", new Vector2(0.5f, 0.5f), new Vector2(-146f, 1f), new Vector2(40f, 42f));
        tagArt = tagRt.gameObject.AddComponent<InkPainter>(); tagArt.onPaint = PaintTag; tagArt.raycastTarget = true;
        letterText = HudKit.Words(tagRt, "L", "A", 21f, Comic.Cream, TMPro.TextAlignmentOptions.Center);
        letterText.font = Comic.DigitFont; letterText.fontSharedMaterial = Comic.DigitFont != null ? Comic.DigitFont.material : letterText.fontSharedMaterial;
        var lrt = letterText.rectTransform; lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f); lrt.sizeDelta = new Vector2(30f, 26f); lrt.anchoredPosition = new Vector2(0f, 5f);
        InkCaption.Attach(tagRt.gameObject, "section");
        nameBtn = Item("Name", new Vector2(-106f, 0f), PaintName, "name it", () => SetRoleFan(!roleFanOpen)); nameArt = nameBtn.GetComponent<InkPainter>();
        splitBtn = Item("Split", new Vector2(-64f, 0f), PaintSplit, "split here", Split); splitArt = splitBtn.GetComponent<InkPainter>();
        joinBtn = Item("Join", new Vector2(-24f, 0f), PaintJoin, "join with the next", Join); joinArt = joinBtn.GetComponent<InkPainter>();
        dupBtn = Item("Duplicate", new Vector2(16f, 0f), PaintDup, "copy the section", Duplicate);
        drumsBtn = Item("Drums", new Vector2(56f, 0f), PaintDrums, "drums for this section", Drums); drumsArt = drumsBtn.GetComponent<InkPainter>();
        launchBtn = Item("Launch", new Vector2(96f, 0f), PaintLaunch, LaunchCaption, Launch); launchArt = launchBtn.GetComponent<InkPainter>();
        deleteBtn = Item("Delete", new Vector2(140f, 0f), PaintTearOff, "delete the section", Delete);
        BuildRoleFan();
        Hints.Register("section.name", nameBtn.transform as RectTransform);
        Hints.Register("section.header", band);
        Hints.Register("section.split", splitBtn.transform as RectTransform);
        Hints.Register("section.launch", launchBtn.transform as RectTransform);
    }

    /// <summary>§17.1: the role fan — "none" and the six role pictures in a gentle arc above the ribbon (hover = the word).</summary>
    void BuildRoleFan()
    {
        SectionRoles.Ensure();
        roleFan = HudKit.Node(ribbon, "RoleFan", new Vector2(0.5f, 0.5f), new Vector2(-70f, 92f), new Vector2(300f, 60f));
        for (int k = 0; k <= 6; k++)
        {
            int role = k;
            var rt = HudKit.Node(roleFan, "Role" + k, new Vector2(0.5f, 0.5f), new Vector2((k - 3f) * 42f, -Mathf.Abs(k - 3f) * 4f), new Vector2(38f, 38f));
            var body = rt.gameObject.AddComponent<InkShape>();
            body.Shape = InkShape.Kind.Circle; body.color = k == 0 ? Comic.Cream : SectionRoles.Tint(k); body.SetInk(1.7f, 2.6f); body.ShadowOffset = new Vector2(2.5f, -3f);
            RectTransform content = null;
            if (k > 0) { var img = Comic.GlyphImage(rt, "Glyph", SectionRoles.Glyph(k), Comic.Ink, 24f, false); img.raycastTarget = false; content = img.rectTransform; }
            else
            {
                var art = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f)).gameObject.AddComponent<InkPainter>();
                art.raycastTarget = false; art.onPaint = p => { p.Arc(p.Area.center, 9f, 1.6f, Comic.A(Comic.Ink, 0.7f), 0f, 360f, 3f, 2.4f); p.Line(p.Area.center + new Vector2(-6f, -6f), p.Area.center + new Vector2(6f, 6f), 1.8f, Comic.A(Comic.Ink, 0.7f), true); };
                content = art.rectTransform;
            }
            roleBtns[k] = HudKit.Control(body, k == 0 ? "no name" : SectionRoles.Word(k), () => PickRole(role), body, content);
            roleBtns[k].GetComponent<InkHover>().hoverScale = 1.15f;
            rt.localEulerAngles = new Vector3(0f, 0f, -(k - 3f) * 4f);
        }
        roleFan.gameObject.SetActive(false);
    }

    void SetRoleFan(bool on)
    {
        if (roleFan == null || roleFanOpen == on) return;
        if (on && target < 0) return;
        roleFanOpen = on;
        HudKit.SetActive(roleFan, on);
        if (on) { roleFan.SetAsLastSibling(); AudioPool.UI(ProceduralAudio.Pop(), 0.22f, 1.25f); }
    }

    /// <summary>§17.1: names section <paramref name="role"/> (SongOps.SetSectionRole — one History entry).</summary>
    void PickRole(int role)
    {
        var sm = SongManager.I; int s = target;
        SetRoleFan(false);
        if (sm == null || s < 0) return;
        if (sm.SectionRole(s) == role) return;
        if (!RoleSetter(s, role)) { Refuse(); return; }
        Commit();
        shownKey = int.MinValue;
        AudioPool.UI(ProceduralAudio.Chime(), 0.28f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.SectionNamed);
    }

    /// <summary>SongOps.SetSectionRole (package O, SPEC §17.1; one History entry).</summary>
    static bool RoleSetter(int s, int role) { var sm = SongManager.I; SongOps.SetSectionRole(s, role); return sm != null && sm.SectionRole(s) == role; }

    HudButton Item(string name, Vector2 pos, System.Action<InkPainter> paint, string caption, System.Action onClick)
    {
        var rt = HudKit.Node(band, name, new Vector2(0.5f, 0.5f), pos, new Vector2(36f, 36f));
        var art = rt.gameObject.AddComponent<InkPainter>();
        art.onPaint = paint;
        var b = HudKit.Control(art, caption, onClick, null, null, art);
        b.GetComponent<InkHover>().hoverScale = 1.12f;
        return b;
    }

    // ------------------------------------------------------------------ geometry (B's plinths, else the grids' union)
    static readonly List<KeyBlock> scratch = new List<KeyBlock>();

    /// <summary>World bounds of section <paramref name="s"/>'s plinth: the union of its grids (chord islands, keyboards, stairs, phrases; not Moons)
    /// as shown, plus the plinth's margins (the front one carries its letter and measure numbers). False without a song / grids.</summary>
    public static bool SectionBounds(int s, out Bounds b)
    {
        b = default(Bounds);
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || s < 0 || s >= sm.SectionCount) return false;
        if (SectionPlinth.I != null && s < SectionPlinth.Count) { b = SectionPlinth.Bounds(s); if (b.size.x > 0.1f) return true; }   // B's plinth
        int first = sm.SectionFirst(s), last = sm.SectionLast(s);
        bool any = false; float y = float.MaxValue;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.column < first || kb.column > last) continue;
            var vb = kb.VisualBounds;
            if (!any) { b = vb; any = true; } else b.Encapsulate(vb);
            y = Mathf.Min(y, kb.VisualCenter.y);
        }
        if (!any) return false;
        // the plinth: a low slab under the grids — margins around them, its top a little under the platforms
        Vector3 mn = b.min, mx = b.max;
        mn.x -= 0.45f; mx.x += 0.45f; mn.z -= 1.05f; mx.z += 0.45f;
        mn.y = y - 0.9f; mx.y = y - 0.12f;
        b.SetMinMax(mn, mx);
        return true;
    }

    /// <summary>The section whose plinth lies under screen point <paramref name="screen"/> (-1 none); <paramref name="world"/> = the point on it.</summary>
    public static int SectionUnder(Vector3 screen, out Vector3 world)
    {
        world = Vector3.zero;
        var sm = SongManager.I; var cam = Camera.main;
        if (sm == null || cam == null || !sm.HasSong) return -1;
        var ray = cam.ScreenPointToRay(screen);
        if (SectionPlinth.I != null && SectionPlinth.Count == sm.SectionCount)
        {
            // B's plinths: the block the ray meets first; the point on its top
            int ps = SectionPlinth.Pick(ray);
            Bounds pb; float pe;
            if (ps >= 0 && SectionBounds(ps, out pb) && new Plane(Vector3.up, new Vector3(0f, pb.max.y, 0f)).Raycast(ray, out pe) && pe > 0f) { world = ray.GetPoint(pe); return ps; }
            if (ps >= 0) return ps;
        }
        int best = -1; float bestT = float.MaxValue;
        for (int s = 0; s < sm.SectionCount; s++)
        {
            Bounds b;
            if (!SectionBounds(s, out b)) continue;
            float enter;
            if (!new Plane(Vector3.up, new Vector3(0f, b.max.y, 0f)).Raycast(ray, out enter) || enter <= 0f) continue;
            var p = ray.GetPoint(enter);
            if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
            if (enter < bestT) { bestT = enter; best = s; world = p; }
        }
        return best;
    }

    /// <summary>The column a split of section <paramref name="s"/> near world x <paramref name="x"/> would start: the inner column boundary nearest
    /// x (-1 when the section has a single column).</summary>
    public static int SplitColumnNear(int s, float x)
    {
        var sm = SongManager.I;
        if (sm == null || s < 0 || s >= sm.SectionCount) return -1;
        int first = sm.SectionFirst(s), last = sm.SectionLast(s), best = -1; float bestD = float.MaxValue;
        for (int c = first + 1; c <= last; c++)
        {
            float w0, e0, w1, e1;
            if (!ColumnSpan(c - 1, out w0, out e0) || !ColumnSpan(c, out w1, out e1)) continue;
            float d = Mathf.Abs((e0 + w1) * 0.5f - x);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>World x span of column <paramref name="col"/>'s lanes as shown (phrases, which span columns, and Moons left out).</summary>
    public static bool ColumnSpan(int col, out float west, out float east)
    {
        west = float.MaxValue; east = float.MinValue;
        var sm = SongManager.I;
        if (sm == null) return false;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.IsPhrase || kb.column != col) continue;
            var vb = kb.VisualBounds;
            west = Mathf.Min(west, vb.min.x); east = Mathf.Max(east, vb.max.x);
        }
        return west < east;
    }

    /// <summary>The grids of section <paramref name="s"/>'s last column that can launch (chord islands and keyboards).</summary>
    public static List<KeyBlock> LaunchGrids(int s)
    {
        var r = new List<KeyBlock>();
        var sm = SongManager.I;
        if (sm == null || s < 0 || s >= sm.SectionCount) return r;
        int last = sm.SectionLast(s);
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && !kb.IsStairs && !kb.IsPhrase && kb.column == last) r.Add(kb);
        return r;
    }

    /// <summary>Every launchable grid of the section's last column launches (the launch button reads "on").</summary>
    public static bool Launching(int s)
    {
        var g = LaunchGrids(s);
        if (g.Count == 0) return false;
        foreach (var kb in g) if (!kb.launch) return false;
        return true;
    }

    /// <summary>The letter of section <paramref name="s"/> — the one its plinth shows (A, B, C … by first appearance; sections with the same chords
    /// share one: package B's SectionPlinth.LetterOf), else by its index.</summary>
    public static string LetterOf(int s)
    {
        if (s < 0) return "";
        string l = SectionPlinth.I != null ? SectionPlinth.LetterOf(s) : "";
        return string.IsNullOrEmpty(l) ? ((char)('A' + (s % 26))).ToString() : l;
    }

    // ------------------------------------------------------------------ actions (each ends in exactly one History entry)
    static void Commit() { History.Push(); }
    void Refuse() { Refusals++; shakeT = 1f; AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); }

    void Split()
    {
        var sm = SongManager.I; int s = target;
        if (sm == null || s < 0 || splitCol <= sm.SectionFirst(s) || splitCol > sm.SectionLast(s)) { Refuse(); return; }
        int n = sm.SectionCount;
        SongOps.SplitSectionAt(splitCol);
        if (sm.SectionCount == n) { Refuse(); return; }
        Commit();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
        pinned = -1; target = -1;
    }

    void Join()
    {
        var sm = SongManager.I; int s = target;
        if (sm == null || s < 0 || s + 1 >= sm.SectionCount) { Refuse(); return; }
        int n = sm.SectionCount;
        SongOps.JoinSectionAt(sm.SectionFirst(s + 1));
        if (sm.SectionCount == n) { Refuse(); return; }
        Commit();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1f);
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
        pinned = -1; target = -1;
    }

    void Duplicate()
    {
        var sm = SongManager.I; int s = target;
        if (sm == null || s < 0) { Refuse(); return; }
        int n = SongOps.DuplicateSection(s);
        if (n < 0) { Refuse(); return; }
        Commit();
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.3f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
        pinned = -1; target = -1;
    }

    void Delete()
    {
        var sm = SongManager.I; int s = target;
        if (sm == null || s < 0 || sm.SectionCount <= 1) { Refuse(); return; }
        int n = sm.SectionCount;
        SongOps.DeleteSection(s);
        if (sm.SectionCount == n) { Refuse(); return; }
        Commit();
        if (UIManager.I != null) UIManager.I.ClearSelection();
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
        pinned = -1; target = -1;
    }

    void Drums()
    {
        var sm = SongManager.I; var ui = UIManager.I; int s = target;
        if (sm == null || ui == null || s < 0 || sm.Moons.Count >= SongManager.MaxMoons) { Refuse(); return; }
        int i = sm.AddMoon(Mathf.Max(0, sm.SectionFirst(s)));
        if (i < 0) { Refuse(); return; }
        Commit();
        if (ui.Rail != null) ui.Rail.MarkDirty();
        AudioPool.UI(ProceduralAudio.Chime(), 0.3f);
        drumsArt.Repaint();
        Onboarding.Notify(Onboarding.Ev.MoonAdded);
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
    }

    void Launch()
    {
        var sm = SongManager.I; int s = target;
        var grids = LaunchGrids(s);
        if (sm == null || s < 0 || grids.Count == 0) { Refuse(); return; }
        bool on = !Launching(s);
        if (on && sm.LaunchTarget(grids[0]) == null) { Refuse(); return; }   // the last section of a song that does not loop: nowhere to land
        SongOps.SetLaunchGrids(grids, on);
        Commit();
        launchArt.Repaint();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, on ? 1.3f : 0.85f);
        Onboarding.Notify(Onboarding.Ev.IslandLaunch);
        Onboarding.Notify(Onboarding.Ev.SectionEdited);
    }

    // ------------------------------------------------------------------ per frame
    bool Hovering()
    {
        if (group == null || !group.blocksRaycasts) return false;
        if (splitBtn.Hover || joinBtn.Hover || dupBtn.Hover || drumsBtn.Hover || launchBtn.Hover || deleteBtn.Hover || nameBtn.Hover) return true;
        if (roleFanOpen) { foreach (var b in roleBtns) if (b != null && b.Hover) return true; if (RectTransformUtility.RectangleContainsScreenPoint(roleFan, Input.mousePosition, null)) return true; }
        return RectTransformUtility.RectangleContainsScreenPoint(band, Input.mousePosition, null);
    }

    static bool Suppressed()
    {
        var pm = PathManager.I; var sm = SongManager.I;
        if (pm == null || sm == null || !sm.HasSong) return true;
        if (pm.IsDrawing || (pm.Drag != null && pm.Drag.Dragging)) return true;
        if (IslandTray.I != null && IslandTray.I.Dragging) return true;
        if (Presenter.Active || MainMenu.IsShown || CubeInspector.IsOpen || WorldInput.WorldLocked) return true;
        if (UIManager.I != null && UIManager.I.CardShown > 0.05f) return true;
        if (Onboarding.Active) return true;   // the tutorial teaches the island header: no second ribbon meanwhile
        return false;
    }

    void Update()
    {
        if (group == null || splitBtn == null) return;
        float dt = Time.unscaledDeltaTime;
        var sm = SongManager.I; var pm = PathManager.I;
        bool sup = Suppressed();
        int count = sm != null && sm.HasSong ? sm.SectionCount : 0;
        if (pinned >= count) pinned = -1;
        if (target >= count) target = -1;
        // the plinth under the pointer: only where nothing of the world is (no tile, cube or island edge: those are the island's — its header
        // wins) and not over the HUD
        int h = -1; Vector3 wp = Vector3.zero;
        bool hovering = Hovering();
        if (!sup && pm != null && pm.HoverIsEmpty && !hovering)
        {
            Vector3 mp = PathManager.SimOnly ? PathManager.SimPos : Input.mousePosition;
            bool overUi = !PathManager.SimOnly && InputUtil.PointerOverUI;
            if (!overUi) h = SectionUnder(mp, out wp);
        }
        if (h >= 0) { if (h == hoverCand) hoverT += dt; else { hoverCand = h; hoverT = 0f; } }
        else { hoverCand = -1; hoverT = 0f; }
        int want = -1;
        if (hoverCand >= 0 && hoverT >= ShowDelay)
        {
            want = hoverCand; lingerT = 0f;
            if (hoverCand != target || float.IsNaN(anchorX) || Mathf.Abs(wp.x - anchorX) > 2.5f) anchorX = wp.x;   // sticky: it follows the pointer only in steps
            splitCol = SplitColumnNear(hoverCand, wp.x);
        }
        else if (pinned >= 0 && pinned != target && !hovering) want = pinned;   // a pin takes over at once (no linger of the one before)
        else if (target >= 0 && (hovering || hoverCand == target)) { want = target; lingerT = 0f; }
        else if (target >= 0 && lingerT < Linger) { lingerT += dt; want = target; }
        if (want < 0 && pinned >= 0) want = pinned;
        if (want >= 0 && want == pinned && hoverCand < 0)
        {
            Bounds pb;
            if (SectionBounds(pinned, out pb)) { anchorX = float.IsNaN(pinX) ? pb.center.x : Mathf.Clamp(pinX, pb.min.x, pb.max.x); splitCol = SplitColumnNear(pinned, anchorX); }
        }
        if (IslandHeader.IsShown && !IslandHeader.BySelection && pinned < 0) want = -1;   // one header at a time: a hovered / held island's wins
        if (sup) want = -1;
        if (want != target)
        {
            target = want; shownKey = int.MinValue;
            SetRoleFan(false);
            if (target >= 0) Onboarding.SectionHeaderShown();
        }
        if (roleFanOpen && Input.GetMouseButtonDown(0) && !Hovering()) SetRoleFan(false);
        if (shakeT > 0f) { shakeT = Mathf.Max(0f, shakeT - dt / 0.35f); }
        int t = HudKit.TwosTick;
        if (t != tick)
        {
            tick = t;
            float goal = target >= 0 ? 1f : 0f;
            if (shownA != goal) shownA = goal > shownA ? Mathf.Min(1f, shownA + 0.5f) : Mathf.Max(0f, shownA - 0.5f);
            group.alpha = shownA;
            bool live = shownA > 0.5f && target >= 0;
            group.blocksRaycasts = live; group.interactable = live;
            if (live) Refresh();
        }
    }

    /// <summary>Repaints what the section's state changed (its letter, measures, drums, launch, join / split availability).</summary>
    void Refresh()
    {
        var sm = SongManager.I;
        if (sm == null || target < 0) return;
        int s = target;
        int bars = sm.SectionBars(s), moons = 0, first = sm.SectionFirst(s);
        for (int m = 0; m < sm.Moons.Count; m++) if (sm.Moons[m] != null && sm.MoonStartColumn(m) == first) moons++;
        int role = sm.SectionRole(s);
        string letter = LetterOf(s);
        int key = s * 1000003 + bars * 7919 + moons * 131 + (Launching(s) ? 17 : 0) + (s + 1 < sm.SectionCount ? 3 : 0) + splitCol * 29 + (sm.SectionCount << 20) + role * 71 + letter.GetHashCode() * 5;
        if (key == shownKey) return;
        shownKey = key;
        RoleShown = role;
        letterText.text = letter;
        letterText.color = SectionPlinth.LetterDeep(letter);
        InkCaption.Attach(tagRt.gameObject, "section " + LetterOf(s) + (role > 0 ? " · " + SectionRoles.Word(role) : "") + " · " + bars + (bars == 1 ? " measure" : " measures"));
        InkCaption.Attach(nameBtn.gameObject, role > 0 ? SectionRoles.Word(role) : "name it");
        tagArt.Repaint(); splitArt.Repaint(); joinArt.Repaint(); drumsArt.Repaint(); launchArt.Repaint(); nameArt.Repaint();
    }

    void LateUpdate()
    {
        if (target < 0 || group == null || ribbon == null) { cutOk = false; return; }
        var cam = Camera.main;
        Bounds b;
        if (cam == null || !SectionBounds(target, out b)) return;
        float ax = float.IsNaN(anchorX) ? b.center.x : Mathf.Clamp(anchorX, b.min.x + 1.2f, b.max.x - 1.2f);
        float y = b.max.y + 0.1f, z = b.min.z;
        Vector3 s0 = cam.WorldToScreenPoint(new Vector3(ax - 1.5f, y, z)), s1 = cam.WorldToScreenPoint(new Vector3(ax + 1.5f, y, z)), sm = cam.WorldToScreenPoint(new Vector3(ax, y, z));
        if (sm.z <= 0f || s0.z <= 0f || s1.z <= 0f) { group.alpha = 0f; group.blocksRaycasts = false; return; }
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sm, null, out lp)) return;
        float ang = Mathf.Clamp(Mathf.DeltaAngle(0f, Mathf.Atan2(s1.y - s0.y, s1.x - s0.x) * Mathf.Rad2Deg), -6f, 6f);
        ribbon.localEulerAngles = new Vector3(0f, 0f, ang);
        Vector2 pos = lp + (Vector2)(Quaternion.Euler(0f, 0f, ang) * new Vector2(0f, -30f));   // it hangs just in front of the plinth's edge
        Rect r = root.rect;
        float hw = BandW * 0.5f + 26f;
        pos.x = Mathf.Clamp(pos.x, r.xMin + IslandHeader.ClearLeft + hw, Mathf.Max(r.xMin + IslandHeader.ClearLeft + hw, r.xMax - IslandHeader.ClearRight - hw));
        pos.y = Mathf.Clamp(pos.y, r.yMin + IslandHeader.ClearBottom + 30f, Mathf.Max(r.yMin + IslandHeader.ClearBottom + 30f, r.yMax - IslandHeader.ClearTop - 30f));
        if (shakeT > 0f) pos.x += Mathf.Sin(shakeT * 40f) * 5f * shakeT;
        ribbon.anchoredPosition = pos;
        // "split here": the cut across the plinth at the boundary a split would take
        bool show = splitBtn.Hover && splitCol > 0;
        Vector2 c0 = Vector2.zero, c1 = Vector2.zero;
        if (show)
        {
            float w0, e0, w1, e1;
            bool okA = ColumnSpan(splitCol - 1, out w0, out e0), okB = ColumnSpan(splitCol, out w1, out e1);
            show = okA && okB;
            if (show)
            {
                float bx = (e0 + w1) * 0.5f;
                Vector3 a = cam.WorldToScreenPoint(new Vector3(bx, y, b.min.z)), c = cam.WorldToScreenPoint(new Vector3(bx, y, b.max.z));
                show = a.z > 0f && c.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, a, null, out c0) && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, c, null, out c1);
            }
        }
        if (show != cutOk || (show && ((c0 - cut0).sqrMagnitude > 0.25f || (c1 - cut1).sqrMagnitude > 0.25f))) { cutOk = show; cut0 = c0; cut1 = c1; cutArt.Repaint(); }
    }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> q = new List<Vector2>(64);

    void PaintCut(InkPainter p)
    {
        if (!cutOk) return;
        // cut0 / cut1 are in the root's local space; the painter fills the root
        Vector2 off = p.Area.center - root.rect.center;
        Vector2 a = cut0 + off, b = cut1 + off;
        q.Clear(); q.Add(a); q.Add(b);
        p.Stroke(q, 2, 5f, Comic.A(Comic.Ink, 0.8f), false, 9f, 7f);
        p.Stroke(q, 2, 2.6f, Comic.Cream, false, 9f, 7f);
    }

    /// <summary>The letter tag: a stone disc (the letter is TMP on top) and the section's measures as four pips underneath — inked for each
    /// measure it has, dotted for the ones a full section still needs ("a section is 4 measures").</summary>
    void PaintTag(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0f, 5f);
        var sm = SongManager.I;
        int bars = sm != null && target >= 0 ? sm.SectionBars(target) : 0, full = ProjectConfig.SectionBars;
        // v7: the letter's colours (SectionPlinth.LetterTint) — the disc pale until the section has its 4 measures, rim and shadow in the deep shade
        string letter = target >= 0 ? LetterOf(target) : "A";
        Color lt = SectionPlinth.LetterTint(letter), ld = SectionPlinth.LetterDeep(letter);
        p.Disc(c + new Vector2(1.5f, -2f), 14f, Comic.A(ld, 0.6f));
        p.Disc(c, 14f, ld);
        p.Disc(c, 12f, bars >= full ? lt : Color.Lerp(lt, Comic.Cream, 0.55f));
        int n = Mathf.Max(full, bars);
        float step = n > full ? 30f / n : 7.5f;
        for (int k = 0; k < n; k++)
        {
            Vector2 at = new Vector2(p.Area.center.x + (k - (n - 1) * 0.5f) * step, p.Area.yMin + 3f);
            if (k < bars) p.Disc(at, 2.4f, bars >= full ? Comic.Pop : Comic.Cream);
            if (k < bars) p.Arc(at, 2.4f, 1.1f, Comic.Ink);
            else p.Arc(at, 2.2f, 1f, Comic.A(Comic.Ink, 0.75f), 0f, 360f, 1.4f, 1.2f);
        }
    }

    /// <summary>§17.1: the name button — the role's picture on its tint (painted like the glyph: see SectionRoles), or a dotted name tag.</summary>
    void PaintName(InkPainter p)
    {
        Vector2 c = p.Area.center;
        var sm = SongManager.I;
        int role = sm != null && target >= 0 ? sm.SectionRole(target) : 0;
        if (role <= 0)
        {
            // an empty luggage tag: dotted outline, a hole, a string
            q.Clear(); q.Add(c + new Vector2(-10f, -8f)); q.Add(c + new Vector2(8f, -8f)); q.Add(c + new Vector2(13f, 0f)); q.Add(c + new Vector2(8f, 8f)); q.Add(c + new Vector2(-10f, 8f));
            p.Fill(q, q.Count, Comic.A(Comic.Cream, 0.4f));
            p.Stroke(q, q.Count, 1.8f, Comic.A(Comic.Ink, 0.8f), true, 2.6f, 2f);
            p.Disc(c + new Vector2(6f, 0f), 1.8f, Comic.Ink);
            p.Line(c + new Vector2(-4f, 0f), c + new Vector2(2f, 0f), 1.4f, Comic.A(Comic.Ink, 0.6f), true);
            return;
        }
        p.Disc(c + new Vector2(1.5f, -2f), 14f, Comic.A(Comic.Ink, 0.8f));
        p.Disc(c, 14f, Comic.Ink);
        p.Disc(c, 12.2f, SectionRoles.Tint(role));
        SectionRoles.Paint(p, role, c, 10f, Comic.Ink);
    }

    void PaintSplit(InkPainter p)
    {
        Vector2 c = p.Area.center;
        bool can = splitCol > 0;
        float a = can ? 1f : 0.35f;
        // the dashed cut line behind, then the scissors (two finger loops, two blades crossing)
        q.Clear(); q.Add(c + new Vector2(8f, -15f)); q.Add(c + new Vector2(8f, 15f));
        p.Stroke(q, 2, 1.6f, Comic.A(Comic.Ink, 0.55f * a), false, 3f, 2.5f);
        p.Line(c + new Vector2(-6f, -6f), c + new Vector2(11f, 9f), 3f, Comic.A(Comic.Ink, a), true);
        p.Line(c + new Vector2(-6f, 4f), c + new Vector2(11f, -9f) + new Vector2(0f, 6f), 3f, Comic.A(Comic.Ink, a), true);
        p.Disc(c + new Vector2(-9f, -8f), 4.6f, Comic.A(Comic.Ink, a)); p.Disc(c + new Vector2(-9f, -8f), 2.4f, Comic.Cream);
        p.Disc(c + new Vector2(-9f, 6f), 4.6f, Comic.A(Comic.Ink, a)); p.Disc(c + new Vector2(-9f, 6f), 2.4f, Comic.Cream);
        p.Disc(c + new Vector2(1.5f, -0.5f), 1.4f, Comic.Cream);
    }

    static void Slab(InkPainter p, Rect r, Color fill, bool dotted)
    {
        if (!dotted) { p.Shift = new Vector2(1.3f, -1.8f); p.RoundRect(r, 2.5f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero; }
        p.RoundRect(r, 2.5f, fill, 1.5f, Comic.Ink, dotted ? 2.4f : 0f, dotted ? 1.8f : 0f);
    }

    void PaintJoin(InkPainter p)
    {
        Vector2 c = p.Area.center;
        var sm = SongManager.I;
        bool can = sm != null && target >= 0 && target + 1 < sm.SectionCount;
        float a = can ? 1f : 0.35f;
        Color st = Comic.A(Comic.Opaque(Stone), a);
        Slab(p, new Rect(c.x - 16f, c.y - 9f, 12f, 11f), st, false);
        Slab(p, new Rect(c.x + 4f, c.y - 9f, 12f, 11f), st, false);
        // two arrows pushing them together
        p.Line(c + new Vector2(-14f, 9f), c + new Vector2(-4f, 9f), 1.8f, Comic.A(Comic.Ink, a), true); p.Head(c + new Vector2(-2f, 9f), Vector2.right, 4.5f, 5.5f, Comic.A(Comic.Ink, a));
        p.Line(c + new Vector2(14f, 9f), c + new Vector2(4f, 9f), 1.8f, Comic.A(Comic.Ink, a), true); p.Head(c + new Vector2(2f, 9f), Vector2.left, 4.5f, 5.5f, Comic.A(Comic.Ink, a));
    }

    void PaintDup(InkPainter p)
    {
        Vector2 c = p.Area.center;
        Slab(p, new Rect(c.x - 3f, c.y - 7f, 17f, 12f), Comic.A(Comic.Cream, 0.25f), true);
        Slab(p, new Rect(c.x - 15f, c.y - 4f, 17f, 12f), Comic.Opaque(Stone), false);
        p.Line(c + new Vector2(9f, 8f), c + new Vector2(9f, 16f), 2.2f, Comic.Ink, true);
        p.Line(c + new Vector2(5f, 12f), c + new Vector2(13f, 12f), 2.2f, Comic.Ink, true);
    }

    void PaintDrums(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0f, -2f);
        float w = 11f, h = 8f;
        q.Clear();
        q.Add(c + new Vector2(-w, h * 0.35f)); q.Add(c + new Vector2(w, h * 0.35f)); q.Add(c + new Vector2(w * 0.9f, -h)); q.Add(c + new Vector2(-w * 0.9f, -h));
        p.InkFill(q, 4, Comic.Opaque(KeyBlock.MoonColor), 1.6f, Comic.Ink);
        p.Disc(c + new Vector2(0f, h * 0.35f), w, h * 0.45f, Comic.Ink, 16);
        p.Disc(c + new Vector2(0f, h * 0.38f), w - 1.6f, h * 0.45f - 1.4f, Comic.Cream, 16);
        p.Line(c + new Vector2(-4f, 13f), c + new Vector2(2f, 6f), 1.8f, Comic.Ink, true);
        p.Line(c + new Vector2(8f, 13f), c + new Vector2(4f, 6f), 1.8f, Comic.Ink, true);
        // a dot per Moon that already starts here
        var sm = SongManager.I;
        if (sm == null || target < 0) return;
        int first = sm.SectionFirst(target), k = 0;
        for (int m = 0; m < sm.Moons.Count; m++) if (sm.Moons[m] != null && sm.MoonStartColumn(m) == first) { p.Disc(c + new Vector2(-12f + k * 5f, -12f), 1.8f, Comic.Ink); k++; }
    }

    /// <summary>§21: the section header's launch caption — a build-up into the next section (the riser and its crash).</summary>
    public const string LaunchCaption = "build-up into the next section";

    /// <summary>§21: the build-up picture (the grid header's): a swell rising into an arrow, a crash at its tip — gold while the section launches.</summary>
    void PaintLaunch(InkPainter p)
    {
        IslandHeader.PaintSwell(p, p.Area.center, target >= 0 && Launching(target));
    }

    static void PaintTearOff(InkPainter p)
    {
        Vector2 c = p.Area.center;
        q.Clear();
        q.Add(c + new Vector2(-13f, -11f)); q.Add(c + new Vector2(13f, -12f)); q.Add(c + new Vector2(12f, 11f));
        q.Add(c + new Vector2(5f, 8f)); q.Add(c + new Vector2(0f, 12f)); q.Add(c + new Vector2(-5f, 8f)); q.Add(c + new Vector2(-12f, 11f));
        p.Shift = new Vector2(2f, -2.5f); p.Fill(q, q.Count, Comic.A(Comic.Ink, 0.85f)); p.Shift = Vector2.zero;
        p.InkFill(q, q.Count, Comic.Danger, 1.6f, Comic.Ink);
        p.Line(c + new Vector2(-4.5f, -5f), c + new Vector2(4.5f, 4f), 2.6f, Comic.Cream, true);
        p.Line(c + new Vector2(-4.5f, 4f), c + new Vector2(4.5f, -5f), 2.6f, Comic.Cream, true);
    }
}

/// <summary>
/// v7 (SPEC v7 §13.4, package U1): "while drawing a melody the length reads as measures ("1 measure", "2 measures", "½")". An ink tag that stands
/// on a melody phrase's far end while a cube in the hand is over it (or it is being drawn), and for a moment after it grows (auto-grow: SongOps.
/// GrowPhrase): "½ measure", "1 measure", "1½ measures", "2 measures" … and at a whole section "4 measures · 1 section" (gold). It pops on twos
/// when the length changes. §19.1: a chord grid / keyboard that auto-expands while a path is drawn on it shows its new length the same way.
/// </summary>
public class MeasureTag : MonoBehaviour
{
    public static MeasureTag I;
    /// <summary>The label is on screen.</summary>
    public static bool Shown => I != null && I.shownA > 0.5f && I.target != null;
    /// <summary>What it reads now ("" hidden).</summary>
    public static string Words => I != null && I.target != null ? I.words : "";
    /// <summary>The phrase it stands on (null none).</summary>
    public static KeyBlock Phrase => I != null ? I.target : null;
    /// <summary>Times it popped for a longer phrase (tests: an auto-grow).</summary>
    public static int Grows => I != null ? I.grows : 0;
    /// <summary>Tests: hold the label on <paramref name="kb"/> (null releases).</summary>
    public static void Pin(KeyBlock kb) { if (I != null) I.pinned = kb; }

    /// <summary>A length in beats as measures, in words: ½ measure, 1 measure, 1½ measures, 2 measures … (a full section says so).</summary>
    public static string WordsFor(float beats)
    {
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float m = beats / bpb;
        int whole = Mathf.FloorToInt(m + 1e-3f);
        float frac = m - whole;
        string f = frac < 0.12f ? "" : (frac < 0.37f ? "¼" : (frac < 0.62f ? "½" : (frac < 0.88f ? "¾" : "")));
        if (frac >= 0.88f) { whole++; f = ""; }
        string num = whole == 0 ? (f == "" ? "0" : f) : whole + f;
        bool one = whole == 1 && f == "" || whole == 0;
        string w = num + (one ? " measure" : " measures");
        int sec = ProjectConfig.SectionBars;
        if (f == "" && whole > 0 && whole % sec == 0) w += " · " + (whole / sec) + (whole / sec == 1 ? " section" : " sections");
        return w;
    }

    /// <summary>A grid's length in beats: a phrase's, else its measures (bars) × beats per bar; -1 for Moons / stairs (their length is their column's).</summary>
    public static int LengthOf(KeyBlock kb) => kb == null || kb.IsMoon || kb.IsStairs ? -1 : (kb.IsPhrase ? kb.phraseBeats : Mathf.Max(1, kb.bars) * Mathf.Max(1, GlobalClock.BeatsPerBar));

    RectTransform root, label; InkShape body; TMPro.TextMeshProUGUI text; CanvasGroup group;
    KeyBlock target, pinned, grewKb; string words = ""; float shownA, grewT = -9f, pop; int grows, tick = int.MinValue;
    readonly List<int> beatsOf = new List<int>();

    public void Build(RectTransform r)
    {
        I = this;
        root = r;
        group = r.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        label = HudKit.Node(r, "Tag", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 34f));
        body = label.gameObject.AddComponent<InkShape>();
        body.Shape = InkShape.Kind.Sticker; body.color = Comic.Cream; body.SetInk(1.8f, 2.8f); body.ShadowOffset = new Vector2(3f, -4f); body.RotJitter = 1.5f; body.raycastTarget = false;
        text = HudKit.Words(label, "Words", "1 measure", 19f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
        var trt = text.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(8f, 0f); trt.offsetMax = new Vector2(-8f, 1f);
        label.localEulerAngles = new Vector3(0f, 0f, 3f);
    }

    void Awake() { I = this; }
    void Start() { SectionHeader.UnderFixedHud(transform); }
    void OnDestroy() { if (I == this) I = null; }

    void Update()
    {
        if (label == null) return;
        var sm = SongManager.I; var pm = PathManager.I;
        KeyBlock want = null;
        if (sm != null && sm.HasSong)
        {
            // a grid that just grew: remember it for a moment (only when the islands stayed the same: an insert shifts the indices)
            bool same = sm.Islands.Count == beatsOf.Count;
            for (int i = 0; i < sm.Islands.Count; i++)
            {
                var kb = sm.Islands[i];
                int b = LengthOf(kb);
                if (i >= beatsOf.Count) beatsOf.Add(b);
                else if (beatsOf[i] != b) { if (same && b > beatsOf[i] && beatsOf[i] > 0) { grewKb = kb; grewT = Time.unscaledTime; grows++; } beatsOf[i] = b; }
            }
            if (beatsOf.Count > sm.Islands.Count) beatsOf.RemoveRange(sm.Islands.Count, beatsOf.Count - sm.Islands.Count);
            if (pinned != null) want = pinned;
            else if (pm != null && !Presenter.Active && !MainMenu.IsShown)
            {
                var over = pm.HoverTile != null ? pm.HoverTile.island : pm.hoverIsland;   // (over a cell hoverIsland is null: the tile's island)
                if (pm.IsDrawing && pm.currentPathTiles.Count > 0 && pm.currentPathTiles[0] != null && pm.currentPathTiles[0].island != null && pm.currentPathTiles[0].island.IsPhrase) want = pm.currentPathTiles[0].island;
                else if (pm.CubeInHand && over != null && over.IsPhrase) want = over;
                else if (grewKb != null && Time.unscaledTime - grewT < 1.6f) want = grewKb;
            }
        }
        if (want != null && LengthOf(want) <= 0) want = null;
        if (want != null && !want.IsPhrase && want != grewKb) want = null;   // chord grids: only when they just grew
        target = want;
        if (target != null)
        {
            string w = WordsFor(LengthOf(target));
            if (w != words) { words = w; text.text = w; pop = 1f; bool sec = w.Contains("section"); body.color = sec ? Comic.Pop : Comic.Cream; label.sizeDelta = new Vector2(Mathf.Max(120f, text.preferredWidth + 30f), 34f); }
        }
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float goal = target != null ? 1f : 0f;
        if (shownA != goal) shownA = goal > shownA ? Mathf.Min(1f, shownA + 0.5f) : Mathf.Max(0f, shownA - 0.5f);
        group.alpha = shownA;
        if (pop > 0f) { pop = Mathf.Max(0f, pop - 1f / (Look.TwosFps * 0.3f)); float k = 1f - pop; label.localScale = Vector3.one * (k < 0.34f ? 1.3f : (k < 0.67f ? 0.93f : 1f)); }
    }

    void LateUpdate()
    {
        if (target == null || label == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var b = target.VisualBounds;
        Vector3 w = new Vector3(b.max.x - 0.3f, target.VisualCenter.y + 0.25f, b.max.z);
        Vector3 s = cam.WorldToScreenPoint(w);
        if (s.z <= 0f) { group.alpha = 0f; return; }
        Vector2 lp;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, s, null, out lp)) label.anchoredPosition = lp + new Vector2(0f, 30f);
    }
}

/// <summary>
/// v7 (SPEC v7 §17.1, package U1): the six SECTION NAMES — intro, verse, chorus, bridge, drop, outro (0 = none: the letter only) — as pictures a
/// player without music theory reads at once: intro a sunrise, verse a page of lines (the story), chorus a loudspeaker (the part everyone sings),
/// bridge an arch, drop a lightning bolt falling, outro a sunset. Each is an IconFactory glyph ("role1" … "role6": HUD images, world textures
/// through IconFactory.GetTexture — package B's plinths can use the same) and an InkPainter drawing (<see cref="Paint"/>: the rail, the name
/// button), with a soft tint per role.
/// </summary>
public static class SectionRoles
{
    public const int Count = 7;
    public static readonly string[] Words = { "", "intro", "verse", "chorus", "bridge", "drop", "outro" };
    static readonly Color[] Tints =
    {
        new Color(0.92f, 0.90f, 0.88f),
        new Color(1.00f, 0.86f, 0.55f),   // intro: dawn
        new Color(0.72f, 0.86f, 1.00f),   // verse: paper blue
        new Color(1.00f, 0.66f, 0.74f),   // chorus: rose
        new Color(0.74f, 0.92f, 0.78f),   // bridge: mint
        new Color(0.82f, 0.72f, 1.00f),   // drop: violet
        new Color(1.00f, 0.74f, 0.56f),   // outro: dusk
    };
    public static bool Valid(int role) => role >= 1 && role < Count;
    /// <summary>The role's lowercase word ("" for none).</summary>
    public static string Word(int role) => Valid(role) ? Words[role] : "";
    /// <summary>The role's IconFactory glyph ("" for none).</summary>
    public static string Glyph(int role) => Valid(role) ? "role" + role : "";
    public static Color Tint(int role) => Tints[Valid(role) ? role : 0];
    public static bool Registered { get; private set; }
    public static void Ensure() { if (!Registered || !IconFactory.Has("role1")) Register(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        for (int r = 1; r < Count; r++) { int role = r; IconFactory.Register(Glyph(role), p => Shape(p, role)); }
        Registered = true;
    }

    /// <summary>The role's picture as a signed distance (glyph units, −1..1, +y up).</summary>
    public static float Shape(Vector2 p, int role)
    {
        switch (role)
        {
            case 1:   // intro: the sun rising over the horizon, rays up
            case 6:   // outro: the sun setting, an arrow down into it
            {
                float horizon = IconFactory.P.Seg(p, -0.92f, -0.42f, 0.92f, -0.42f, 0.08f);
                float sun = Mathf.Max(IconFactory.P.Circle(p, 0f, -0.42f, 0.44f), -0.42f - p.y);
                float d = Mathf.Min(horizon, sun);
                if (role == 1)
                    for (int k = 0; k < 5; k++)
                    {
                        float a = (18f + k * 36f) * Mathf.Deg2Rad;
                        d = Mathf.Min(d, IconFactory.P.Seg(p, Mathf.Cos(a) * 0.6f, -0.42f + Mathf.Sin(a) * 0.6f, Mathf.Cos(a) * 0.86f, -0.42f + Mathf.Sin(a) * 0.86f, 0.065f));
                    }
                else d = Mathf.Min(d, Mathf.Min(IconFactory.P.Seg(p, 0f, 0.9f, 0f, 0.42f, 0.08f), IconFactory.P.Head(p, 0f, 0.1f, -90f, 0.34f)));
                return d;
            }
            case 2:   // verse: a page with lines
            {
                float page = Mathf.Max(IconFactory.P.Box(p, 0f, 0f, 0.62f, 0.82f, 0.1f), -IconFactory.P.Box(p, 0f, 0f, 0.49f, 0.69f, 0.05f));
                float l = IconFactory.P.U(IconFactory.P.Seg(p, -0.3f, 0.38f, 0.3f, 0.38f, 0.065f), IconFactory.P.Seg(p, -0.3f, 0.1f, 0.3f, 0.1f, 0.065f),
                                          IconFactory.P.Seg(p, -0.3f, -0.18f, 0.3f, -0.18f, 0.065f), IconFactory.P.Seg(p, -0.3f, -0.46f, 0.08f, -0.46f, 0.065f));
                return Mathf.Min(page, l);
            }
            case 3:   // chorus: a loudspeaker and two sound waves (the part everyone sings)
            {
                float box = IconFactory.P.Box(p, -0.62f, 0f, 0.17f, 0.24f, 0.04f);
                float cone = IconFactory.P.Poly(p, new Vector2(-0.45f, 0.24f), new Vector2(0.02f, 0.6f), new Vector2(0.02f, -0.6f), new Vector2(-0.45f, -0.24f));
                float w1 = IconFactory.P.Arc(p, 0.05f, 0f, 0.36f, 0.075f, -48f, 48f), w2 = IconFactory.P.Arc(p, 0.05f, 0f, 0.66f, 0.075f, -48f, 48f);
                return IconFactory.P.U(box, cone, w1, w2);
            }
            case 4:   // bridge: a deck on an arch, two pillars, the water
            {
                float deck = IconFactory.P.Seg(p, -0.92f, 0.22f, 0.92f, 0.22f, 0.09f);
                float arch = IconFactory.P.Arc(p, 0f, -0.66f, 0.84f, 0.085f, 0f, 180f);
                float p0 = IconFactory.P.Seg(p, -0.8f, -0.66f, -0.8f, 0.22f, 0.075f), p1 = IconFactory.P.Seg(p, 0.8f, -0.66f, 0.8f, 0.22f, 0.075f);
                float water = IconFactory.P.Seg(p, -0.92f, -0.72f, 0.92f, -0.72f, 0.055f);
                return IconFactory.P.U(deck, arch, p0, p1, water);
            }
            case 5:   // drop: a lightning bolt falling
                return IconFactory.P.Poly(p, new Vector2(-0.12f, 0.92f), new Vector2(0.42f, 0.92f), new Vector2(0.08f, 0.2f), new Vector2(0.44f, 0.2f),
                                          new Vector2(-0.24f, -0.92f), new Vector2(-0.02f, -0.1f), new Vector2(-0.42f, -0.1f));
        }
        return 9f;
    }

    static readonly List<Vector2> q = new List<Vector2>(32);

    /// <summary>The role's picture painted with an InkPainter at <paramref name="c"/>, <paramref name="r"/> = its half size (px).</summary>
    public static void Paint(InkPainter p, int role, Vector2 c, float r, Color ink)
    {
        float w = Mathf.Max(1.1f, r * 0.16f);
        switch (role)
        {
            case 1:
            case 6:
            {
                Vector2 h = c + new Vector2(0f, -0.42f * r);
                q.Clear(); InkPainter.ArcPoints(q, h, 0.44f * r, 0.44f * r, 0f, 180f, 12);
                p.Fill(q, q.Count, ink);
                p.Line(h + new Vector2(-0.92f * r, 0f), h + new Vector2(0.92f * r, 0f), w, ink, true);
                if (role == 1)
                    for (int k = 0; k < 5; k++) { float a = (18f + k * 36f) * Mathf.Deg2Rad; Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.Line(h + d * 0.6f * r, h + d * 0.86f * r, w * 0.8f, ink, true); }
                else { p.Line(c + new Vector2(0f, 0.9f * r), c + new Vector2(0f, 0.42f * r), w, ink, true); p.Head(c + new Vector2(0f, 0.1f * r), Vector2.down, 0.34f * r, 0.5f * r, ink); }
                break;
            }
            case 2:
                p.RoundRect(new Rect(c.x - 0.56f * r, c.y - 0.76f * r, 1.12f * r, 1.52f * r), 0.1f * r, new Color(0f, 0f, 0f, 0f), w, ink);
                for (int k = 0; k < 4; k++) { float y = c.y + (0.38f - k * 0.28f) * r; p.Line(new Vector2(c.x - 0.3f * r, y), new Vector2(c.x + (k == 3 ? 0.08f : 0.3f) * r, y), w * 0.8f, ink, true); }
                break;
            case 3:
                p.RoundRect(new Rect(c.x - 0.79f * r, c.y - 0.24f * r, 0.34f * r, 0.48f * r), 0.04f * r, ink);
                q.Clear(); q.Add(c + new Vector2(-0.45f, 0.24f) * r); q.Add(c + new Vector2(0.02f, 0.6f) * r); q.Add(c + new Vector2(0.02f, -0.6f) * r); q.Add(c + new Vector2(-0.45f, -0.24f) * r);
                p.Fill(q, 4, ink);
                p.Arc(c + new Vector2(0.05f * r, 0f), 0.36f * r, w * 0.8f, ink, -48f, 48f);
                p.Arc(c + new Vector2(0.05f * r, 0f), 0.66f * r, w * 0.8f, ink, -48f, 48f);
                break;
            case 4:
                p.Line(c + new Vector2(-0.92f, 0.22f) * r, c + new Vector2(0.92f, 0.22f) * r, w, ink, true);
                p.Arc(c + new Vector2(0f, -0.66f * r), 0.84f * r, w * 0.9f, ink, 0f, 180f);
                p.Line(c + new Vector2(-0.8f, -0.66f) * r, c + new Vector2(-0.8f, 0.22f) * r, w * 0.9f, ink, true);
                p.Line(c + new Vector2(0.8f, -0.66f) * r, c + new Vector2(0.8f, 0.22f) * r, w * 0.9f, ink, true);
                break;
            case 5:
                q.Clear();
                q.Add(c + new Vector2(-0.12f, 0.92f) * r); q.Add(c + new Vector2(0.42f, 0.92f) * r); q.Add(c + new Vector2(0.08f, 0.2f) * r); q.Add(c + new Vector2(0.44f, 0.2f) * r);
                q.Add(c + new Vector2(-0.24f, -0.92f) * r); q.Add(c + new Vector2(-0.02f, -0.1f) * r); q.Add(c + new Vector2(-0.42f, -0.1f) * r);
                p.Fill(q, q.Count, ink);
                break;
        }
    }
}
