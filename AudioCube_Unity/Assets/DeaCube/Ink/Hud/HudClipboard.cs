using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// v6 (SPEC v6 §8.4, package U2): the clipboard chip — "any given cube path pasted onto the next grid". After a copy (the cube card's copy,
/// Cmd+C, the island header) a small paper card pops in at the bottom-right corner (it flies there from what was copied): the pattern drawn
/// as dots joined by a line in its instrument colour (a mini melody: x = when, y = pitch), the instrument's cube with its voice pips in the
/// corner, and a small × sticker (clear). Click = pick the pattern up (PathManager.PickUpPattern: paste mode — the cursor carries the card and
/// hovering a grid shows the adapted notes; a second click puts it down; while inspecting the card closes first); drag it onto a grid = paste
/// there (Clipboard.PasteOn: one History entry, the magic flight), the hand then goes back to what it held. While the pattern is in the hand
/// the chip lifts like a held instrument chip. The × shows only while the chip (or the ×) is hovered: one hit target at rest.
/// Hidden while the clipboard is empty and with the HUD (presenting, menus, the prompt).
/// Test hooks: <see cref="SimClick"/>, <see cref="SimDrop"/>, <see cref="Shown"/>, <see cref="Card"/>, <see cref="ClearButton"/>.
/// v7 (SPEC v7 §8.2, package U2): "click = plain paste mode (as v6); a press-and-hold / right-click opens the same fan" — the PASTE FAN
/// (<see cref="PasteFan"/>, shared with the cube card's paste → next grid): plain / echo ↑ / echo ↓ / answer + the "late" toggle for echoes
/// (Clipboard.EchoLate). A fan chip click picks the pattern up in that mode (PathManager.PickUpPattern(mode): a click on a grid pastes it
/// so — an echo may land on the source's own grid, a doubling); dragging a fan chip onto a grid pastes there in that mode.
/// </summary>
public class HudClipboard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static HudClipboard I;
    public const float W = 118f, H = 84f;
    /// <summary>Where the card rests (centre; bottom-right anchor, reference px).</summary>
    public static readonly Vector2 Home = new Vector2(-104f, 92f);

    public HudButton Card => cardBtn;
    public HudButton ClearButton => clearBtn;
    /// <summary>The chip itself (inactive while hidden; its parent, which runs this component, always stays active).</summary>
    public RectTransform Root => node;
    /// <summary>The chip is on screen (the clipboard holds a pattern and the HUD shows).</summary>
    public bool Shown => node != null && node.gameObject.activeSelf && popState == 2 && Visible;
    /// <summary>The chip would show (a pattern, the HUD up, no presentation / menu).</summary>
    public bool Visible => Clipboard.HasPattern && !Presenter.Active && !MainMenu.IsShown && (UIManager.I == null || UIManager.I.HudRoot == null || HudAlpha() > 0.5f);
    /// <summary>The pattern is in the hand (the chip is lifted).</summary>
    public bool Lifted => liftT > 0.5f;
    /// <summary>Drops onto a grid so far (tests) and the island of the last one.</summary>
    public static int DropCount { get; private set; }
    public static KeyBlock LastDropTarget { get; private set; }
    /// <summary>Repaints of the card's picture (tests: only on a copy / clear).</summary>
    public static int PaintCount { get; private set; }

    /// <summary>v7: the paste fan (opened by a right-click / a press-and-hold on the chip).</summary>
    public PasteFan Fan => fan;
    public bool FanOpen => fan != null && fan.IsOpen;
    /// <summary>v7: drops of a fan chip onto a grid (tests) and the mode of the last one.</summary>
    public static int ModeDropCount { get; private set; }
    public static Clipboard.PasteMode LastDropMode { get; private set; }

    RectTransform root, node, body; CanvasGroup cg; InkShape paper; InkPainter art; HudButton cardBtn, clearBtn; PasteFan fan;
    // the pop: 0 hidden, 1 popping in (step popK), 2 shown, 3 popping out (step popK)
    int popState, popK, popTick = int.MinValue; float liftT; float flyT = 9f; Vector2 flyFrom; bool dragging;
    PathManager.HandKind handBeforeDrag;

    public static HudClipboard Build(RectTransform hud)
    {
        var rt = HudKit.Node(hud, "Clipboard", new Vector2(1f, 0f), Home, new Vector2(W + 24f, H + 24f));
        var c = rt.gameObject.AddComponent<HudClipboard>();
        c.root = rt;
        c.BuildParts();
        return c;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; Clipboard.OnChanged -= HandleChanged; }
    void OnEnable() { Clipboard.OnChanged -= HandleChanged; Clipboard.OnChanged += HandleChanged; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { DropCount = 0; LastDropTarget = null; PaintCount = 0; pendingFrom = null; ModeDropCount = 0; LastDropMode = Clipboard.PasteMode.Plain; }

    void BuildParts()
    {
        node = HudKit.Node(root, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, root.sizeDelta);
        cg = node.gameObject.AddComponent<CanvasGroup>();
        body = HudKit.Node(node, "Body", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
        body.localEulerAngles = new Vector3(0f, 0f, 3f);
        paper = body.gameObject.AddComponent<InkShape>();
        paper.Shape = InkShape.Kind.RoundRect; paper.color = Comic.Cream; paper.Radius = 11f; paper.SetInk(2f, 3.4f);
        paper.ShadowOffset = Comic.StickerShadow; paper.ShadowColor = Comic.PrintShadow; paper.Wobble = 1.1f; paper.Seed = 7717;
        var content = HudKit.Node(body, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
        art = content.gameObject.AddComponent<InkPainter>(); art.raycastTarget = false;
        art.onPaint = PaintCard;
        cardBtn = HudKit.Control(paper, "paste", ClickCard, paper, content, art);
        cardBtn.name = "Clipboard";
        cardBtn.GetComponent<InkHover>().hoverScale = 1.05f;
        // v7: a right-click or a press-and-hold opens the paste fan (a short click stays the plain paste mode)
        cardBtn.onRightClick = ToggleFan;
        cardBtn.onHold = OpenFan; cardBtn.holdSeconds = 0.45f;
        fan = PasteFan.Create(root.parent != null ? root.parent : root, "ClipboardFan");
        fan.onPick = PickMode;
        fan.onDragBegin = BeginModeDrag;
        fan.onDragEnd = EndModeDrag;
        // the × sticker on the top-right corner (its own 28 px hit)
        InkShape xb; InkPainter xa;
        clearBtn = HudKit.Sticker(node, "ClipClear", new Vector2(0.5f, 0.5f), new Vector2(W * 0.5f - 2f, H * 0.5f - 2f), new Vector2(28f, 28f), InkShape.Kind.Sticker, Comic.Cream,
                                  PaintCross, "clear", ClickClear, out xb, out xa);
        xb.SetInk(1.5f, 2.4f); xb.ShadowOffset = new Vector2(2f, -3f);
        clearBtn.gameObject.SetActive(false);   // shown while the chip is hovered
        Hints.Register("clipboard", node);
        node.gameObject.SetActive(false);
    }

    float clearUntil;
    /// <summary>The × is up (the chip or the × hovered in the last 0.4 s; tests: <see cref="ShowClear"/>).</summary>
    public bool ClearShown => clearBtn != null && clearBtn.gameObject.activeSelf;
    /// <summary>Tests: shows the × as a hover would.</summary>
    public void ShowClear() { clearUntil = Time.unscaledTime + 0.4f; if (clearBtn != null) clearBtn.gameObject.SetActive(true); }

    CanvasGroup hudGroup;
    float HudAlpha()
    {
        var ui = UIManager.I;
        if (ui == null || ui.HudRoot == null) return 1f;
        if (hudGroup == null) hudGroup = ui.HudRoot.GetComponent<CanvasGroup>();
        return hudGroup != null ? hudGroup.alpha : 1f;
    }

    /// <summary>Tests: one frame of the chip's own work (GC measurements).</summary>
    public void StepForTest() { Update(); }

    // ------------------------------------------------------------------ copy source (the chip flies in from it)
    static Vector2? pendingFrom;
    /// <summary>The next copy flies in from this screen point (the card's copy button, the copied cube on screen).</summary>
    public static void NoteCopySource(Vector2 screen) { pendingFrom = screen; if (I != null) I.StartFly(); }

    void StartFly()
    {
        if (pendingFrom == null || root == null) return;
        Vector2 lp;
        var parent = root.parent as RectTransform;
        if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pendingFrom.Value, null, out lp))
        {
            // parent-local → our anchored frame (bottom-right anchor)
            Vector2 anchorPt = new Vector2(parent.rect.xMax, parent.rect.yMin);
            flyFrom = lp - anchorPt; flyT = 0f;
        }
        pendingFrom = null;
    }

    void HandleChanged()
    {
        PaintCount++;
        if (art != null) art.Repaint();
        if (!Clipboard.HasPattern) return;
        if (popState == 2 || popState == 1) { popState = 1; popK = 0; }   // a new copy pops the card again
        StartFly();
    }

    // ------------------------------------------------------------------ v7 the paste fan
    public void OpenFan()
    {
        if (fan == null || !Clipboard.HasPattern || popState != 2) return;
        if (CubeInspector.IsOpen) CubeInspector.Close();   // paste modes need the world: the card goes first
        fan.SetSource(Clipboard.SourceLayer, Clipboard.IsDrums);
        fan.SetColor(Instruments.Colors[Mathf.Clamp(Clipboard.Instrument, 0, Instruments.Count - 1)]);
        fan.Open((RectTransform)paper.transform, PasteFan.Side.Above);
    }

    public void ToggleFan() { if (fan == null) return; if (fan.IsOpen) fan.Close(); else OpenFan(); }

    /// <summary>A fan chip: the pattern goes into the hand in that paste mode (a click on a grid pastes it so); the same mode again puts it down.</summary>
    void PickMode(Clipboard.PasteMode mode)
    {
        var pm = PathManager.I;
        if (pm == null || !Clipboard.HasPattern) return;
        fan.Close();
        if (pm.Hand == PathManager.HandKind.Pattern && pm.PatternMode == mode) { pm.PutDown(); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f); return; }
        if (pm.PickUpPattern(mode)) AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.2f + 0.06f * (int)mode);
    }

    PathManager.HandKind handBeforeModeDrag; Clipboard.PasteMode modeBeforeDrag;
    void BeginModeDrag(Clipboard.PasteMode mode)
    {
        var pm = PathManager.I;
        if (pm == null || !Clipboard.HasPattern || CubeInspector.IsOpen) return;
        handBeforeModeDrag = pm.Hand; modeBeforeDrag = pm.PatternMode;
        pm.PickUpPattern(mode);   // the hovered grid shows the pattern where it would land
    }

    void EndModeDrag(Clipboard.PasteMode mode, Vector2 screen)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        DropMode(screen, mode);
        if (handBeforeModeDrag == PathManager.HandKind.Pattern) pm.PickUpPattern(modeBeforeDrag);   // back to what the hand held
        else if (pm.Hand == PathManager.HandKind.Pattern) pm.PutDown();
        fan.Close();
    }

    /// <summary>A pattern dropped at <paramref name="screen"/> in <paramref name="mode"/> (Clipboard.PasteOn(target, mode): one History entry).</summary>
    AudioCube DropMode(Vector2 screen, Clipboard.PasteMode mode)
    {
        var pm = PathManager.I;
        if (pm == null) return null;
        KeyBlock target = pm.HoverPasteTarget != null ? pm.HoverPasteTarget : TargetAt(screen);
        AudioCube made = null;
        if (target != null && Clipboard.CanPasteOn(target, mode)) made = Clipboard.PasteOn(target, mode);
        if (made != null) { ModeDropCount++; LastDropMode = mode; DropCount++; LastDropTarget = target; }
        else AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.2f);
        return made;
    }

    /// <summary>Tests: a fan chip dragged from the fan and released over <paramref name="target"/> (a tile near its middle).</summary>
    public AudioCube SimModeDrop(KeyBlock target, Clipboard.PasteMode mode)
    {
        var pm = PathManager.I;
        if (pm == null || target == null || Camera.main == null) return null;
        BeginModeDrag(mode);
        var t = target.GetTile(Mathf.Max(0, target.cols / 2), Mathf.Max(0, target.rows / 2));
        Vector3 s = Camera.main.WorldToScreenPoint(t != null ? t.Top : target.Center + Vector3.up * 0.3f);
        var made = DropMode(s, mode);
        if (handBeforeModeDrag == PathManager.HandKind.Pattern) pm.PickUpPattern(modeBeforeDrag);
        else if (pm.Hand == PathManager.HandKind.Pattern) pm.PutDown();
        return made;
    }

    // ------------------------------------------------------------------ gestures
    void ClickCard()
    {
        var pm = PathManager.I;
        if (pm == null || !Clipboard.HasPattern) return;
        if (pm.Hand == PathManager.HandKind.Pattern) { pm.PutDown(); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f); return; }
        if (CubeInspector.IsOpen) CubeInspector.Close();   // paste mode needs the world: the card goes first
        if (pm.PickUpPattern()) AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.3f);
    }

    void ClickClear()
    {
        var pm = PathManager.I;
        if (pm != null && pm.Hand == PathManager.HandKind.Pattern) pm.PutDown();
        Clipboard.Clear();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.2f, 1.4f);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var pm = PathManager.I;
        if (pm == null || !Clipboard.HasPattern || e.button != PointerEventData.InputButton.Left) return;
        if (CubeInspector.IsOpen) return;   // the world is locked: a drag has nowhere to go
        dragging = true;
        handBeforeDrag = pm.Hand;
        if (pm.Hand != PathManager.HandKind.Pattern) pm.PickUpPattern();   // the cursor carries the card; grids show the adapted notes
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        Drop(e.position);
    }

    void Drop(Vector2 screen)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        KeyBlock target = pm.HoverPasteTarget != null ? pm.HoverPasteTarget : (pm.CubeInHand ? null : TargetAt(screen));
        AudioCube made = null;
        if (target != null && Clipboard.CanPasteOn(target)) made = Clipboard.PasteOn(target);
        if (made != null) { DropCount++; LastDropTarget = target; }
        else AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.2f);
        if (handBeforeDrag != PathManager.HandKind.Pattern && pm.Hand == PathManager.HandKind.Pattern) pm.PutDown();   // back to what the hand held
    }

    /// <summary>The grid a drop at <paramref name="screen"/> lands on: the island of the tile / cube / platform under it.</summary>
    static KeyBlock TargetAt(Vector2 screen)
    {
        var pm = PathManager.I;
        if (pm == null || Camera.main == null) return null;
        var r = pm.PickAt(screen);
        if (r.tile != null && r.tile.island != null) return r.tile.island;
        if (r.island != null) return r.island;
        if (r.cube != null) return r.cube.Island;
        return null;
    }

    /// <summary>Tests: the card's click.</summary>
    public void SimClick() { ClickCard(); }
    /// <summary>Tests: a drag from the chip released over <paramref name="target"/> (its platform centre on screen).</summary>
    public void SimDrop(KeyBlock target)
    {
        var pm = PathManager.I;
        if (pm == null || target == null || Camera.main == null) return;
        handBeforeDrag = pm.Hand;
        if (pm.Hand != PathManager.HandKind.Pattern) pm.PickUpPattern();
        Vector3 s = Camera.main.WorldToScreenPoint(target.Center + Vector3.up * 0.3f);
        var t = target.GetTile(Mathf.Max(0, target.cols / 2), Mathf.Max(0, target.rows / 2));
        if (t != null) s = Camera.main.WorldToScreenPoint(t.Top);
        Drop(s);
    }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> pts = new List<Vector2>(64);

    void PaintCard(InkPainter p)
    {
        if (!Clipboard.HasPattern) return;
        Rect r = p.Area;
        int inst = Mathf.Clamp(Clipboard.Instrument, 0, Instruments.Count - 1);
        Color col = Comic.Opaque(Instruments.Colors[inst]);
        // faint ruled lines (a card of paper), then the melody, then the instrument's cube in the top-left corner
        for (int k = 0; k < 3; k++)
        {
            float y = r.yMin + 16f + k * 17f;
            p.Line(new Vector2(r.xMin + 12f, y), new Vector2(r.xMax - 10f, y), 1.2f, Comic.A(Comic.Ink, 0.12f));
        }
        PaintPattern(p, new Rect(r.xMin + 30f, r.yMin + 12f, r.width - 42f, r.height - 26f), col, Clipboard.Midi, Clipboard.State, 1f);
        Vector2 cc = new Vector2(r.xMin + 19f, r.yMax - 19f);
        p.Disc(cc + new Vector2(0f, -12f), 10f, 3f, Comic.A(Comic.Ink, 0.18f));
        p.Cube(cc, 24f, col, Comic.Ink, 1.6f);
        VoiceMark.Paint(p, cc, 24f, Clipboard.Voice, Comic.Ink);
    }

    /// <summary>A pattern as a mini melody inside <paramref name="r"/>: one dot per note (x = its start, from the note lengths when the
    /// cube has them, else evenly; y = its pitch within the pattern's range, at least a fifth tall) joined by an inked line in
    /// <paramref name="col"/>. Shared with the cursor's pattern card (CursorKit). Allocation-free.</summary>
    public static void PaintPattern(InkPainter p, Rect r, Color col, int[] midi, CubeState st, float scale)
    {
        if (midi == null || midi.Length == 0) return;
        int n = Mathf.Min(midi.Length, 32);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < n; i++) { lo = Mathf.Min(lo, midi[i]); hi = Mathf.Max(hi, midi[i]); }
        float mid = (lo + hi) * 0.5f, span = Mathf.Max(7f, hi - lo);
        bool durs = st != null && st.durs != null && st.durs.Length >= n;
        float total = 0f;
        if (durs) for (int i = 0; i < n; i++) total += Mathf.Max(1, st.durs[i]);
        pts.Clear();
        float acc = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = n == 1 ? 0.5f : (durs ? acc / Mathf.Max(1f, total - Mathf.Max(1, st.durs[n - 1])) : i / (float)(n - 1));
            if (durs) acc += Mathf.Max(1, st.durs[i]);
            float v = 0.5f + (midi[i] - mid) / span;
            pts.Add(new Vector2(r.xMin + r.width * Mathf.Clamp01(u), r.yMin + r.height * Mathf.Clamp01(v)));
        }
        if (n >= 2)
        {
            p.Stroke(pts, n, 4.6f * scale, Comic.Ink);
            p.Stroke(pts, n, 2.4f * scale, col);
        }
        bool rests = st != null && st.rests != null && st.rests.Length >= n;
        for (int i = 0; i < n; i++)
        {
            bool rest = rests && st.rests[i];
            p.Disc(pts[i], 4.2f * scale, Comic.Ink);
            p.Disc(pts[i], 2.7f * scale, rest ? Comic.Cream : col);
        }
    }

    static void PaintCross(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Line(c + new Vector2(-5f, -5f), c + new Vector2(5f, 5f), 2.4f, Comic.Ink, true);
        p.Line(c + new Vector2(-5f, 5f), c + new Vector2(5f, -5f), 2.4f, Comic.Ink, true);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (root == null) return;
        bool want = Visible;
        if (want && (popState == 0 || popState == 3)) { popState = 1; popK = 0; if (!node.gameObject.activeSelf) node.gameObject.SetActive(true); art.Repaint(); }
        else if (!want && (popState == 1 || popState == 2)) { popState = 3; popK = 0; }
        var pm = PathManager.I;
        bool inHand = pm != null && pm.Hand == PathManager.HandKind.Pattern;
        if (fan != null && fan.IsOpen && (popState != 2 || !want)) fan.Close();
        // the × while the chip or the × is hovered (a short linger so the pointer can travel to it)
        if (cardBtn.Hover || clearBtn.Hover) clearUntil = Time.unscaledTime + 0.4f;
        bool showClear = popState == 2 && Time.unscaledTime < clearUntil && !dragging;
        if (clearBtn.gameObject.activeSelf != showClear) clearBtn.gameObject.SetActive(showClear);
        // stepped (on twos): the pop, the fly-in, the lift while the pattern is in the hand
        int t = HudKit.TwosTick;
        if (t == popTick) return;
        popTick = t;
        float dt = 1f / Look.TwosFps;
        float scale = 1f, alpha = 1f;
        if (popState == 1) { scale = PopIn[popK]; if (++popK >= PopIn.Length) popState = 2; }
        else if (popState == 3)
        {
            if (popK < PopOut.Length) { scale = PopOut[popK]; alpha = 1f - 0.3f * popK; popK++; }
            else { node.gameObject.SetActive(false); popState = 0; liftT = 0f; return; }
        }
        Vector2 at = Home;
        if (flyT < 1f)
        {
            flyT = Mathf.Min(1f, flyT + dt / 0.42f);
            float e = Ease.OutCubic(flyT);
            at = Vector2.Lerp(flyFrom, Home, e) + new Vector2(0f, 90f * Mathf.Sin(e * Mathf.PI));   // a little arc over
            scale *= Mathf.Lerp(0.45f, 1f, e);
        }
        liftT = Mathf.MoveTowards(liftT, inHand ? 1f : 0f, dt / 0.16f);
        float le = Ease.OutBack(liftT, 1.6f);
        node.anchoredPosition = at - Home + new Vector2(-8f, 10f) * le;   // (the node moves inside its fixed parent at Home)
        node.localScale = new Vector3(scale, scale, 1f);
        paper.ShadowOffset = Vector2.Lerp(Comic.StickerShadow, new Vector2(8f, -10f), liftT);
        float sway = liftT >= 1f ? Mathf.Round(2f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 1.8f) * 2f) * 0.5f : 0f;
        body.localEulerAngles = new Vector3(0f, 0f, 3f + sway);
        cg.alpha = dragging ? 0.55f : alpha;
    }

    static readonly float[] PopIn = { 0.62f, 1.08f, 1f }, PopOut = { 0.9f, 0.7f, 0.5f };
}

/// <summary>
/// v7 (SPEC v7 §8.1 / §8.2 / §17.3, package U2): the PASTE FAN — the ways a copied pattern can land, as pictures on a paper tongue (icons
/// first, captions on hover): PLAIN (the v6 adapted paste), ECHO ↑ / ECHO ↓ (the pattern in the octave layer above / below: "some other cubes
/// above if higher and below if lower octave phase in and plays it ... like create an echo, higher or lower of a melodic voice") with the LATE
/// toggle beside them (Clipboard.EchoLate: echoes land an eighth late, the recording's D6 → D4), STEP ↑ / STEP ↓ (a sequence: the pattern one
/// scale step higher / lower) and ANSWER (the phrase comes home on its last note). Shared by the cube card's paste → next grid (hover it) and
/// the clipboard chip (right-click / press-and-hold). The owner decides what a pick does (<see cref="onPick"/>); a drag of a chip reports its
/// begin / release (<see cref="onDragBegin"/> / <see cref="onDragEnd"/>). Built from InkButtons (no HUD discs: it also serves the card).
/// </summary>
public class PasteFan : MonoBehaviour
{
    public enum Side { Above, Below }
    /// <summary>The fan's chips, left → right.</summary>
    public static readonly Clipboard.PasteMode[] Modes =
        { Clipboard.PasteMode.Plain, Clipboard.PasteMode.EchoUp, Clipboard.PasteMode.EchoDown, Clipboard.PasteMode.StepUp, Clipboard.PasteMode.StepDown, Clipboard.PasteMode.Answer };
    public static readonly string[] Captions = { "paste", "echo up", "echo down", "step up", "step down", "answer" };
    public const float ChipW = 44f, ChipH = 52f, LateW = 38f, Pad = 12f, GroupGap = 10f, H = 72f;
    /// <summary>Seconds the pointer may be away from the fan (and its opener) before a hover-opened fan folds.</summary>
    public const float Grace = 0.3f;

    public Action<Clipboard.PasteMode> onPick;
    public Action<Clipboard.PasteMode> onDragBegin;
    public Action<Clipboard.PasteMode, Vector2> onDragEnd;
    /// <summary>A hover-opened fan also stays while this returns true (its opener is hovered).</summary>
    public Func<bool> keepOpen;

    public bool IsOpen => open;
    public RectTransform Root => rt;
    public InkButton Chip(Clipboard.PasteMode m) { int i = Array.IndexOf(Modes, m); return i >= 0 ? chips[i] : null; }
    public InkButton LateButton => lateBtn;
    /// <summary>The pointer is over the fan's paper or one of its chips.</summary>
    public bool PointerInside => zone != null && (zone.Inside || AnyHover());
    /// <summary>Opens so far (tests).</summary>
    public static int OpenCount { get; private set; }

    RectTransform rt; CanvasGroup cg; InkShape paper; HudHoverZone zone;
    readonly InkButton[] chips = new InkButton[Modes.Length];
    readonly InkPainter[] arts = new InkPainter[Modes.Length];
    InkButton lateBtn; InkPainter lateArt;
    bool open, hoverOpened, drums, lateShown; float leaveT; int popK = -1, tick = int.MinValue;
    Color col = Comic.Opaque(Palette.Accent);
    float width;
    static readonly Vector3[] corners = new Vector3[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OpenCount = 0; }

    public static PasteFan Create(Transform parent, string name)
    {
        var r = InkUI.Node(name, parent);
        var f = r.gameObject.AddComponent<PasteFan>();
        f.rt = r;
        f.Build();
        return f;
    }

    void Build()
    {
        // plain · echo ↑ echo ↓ late · step ↑ step ↓ · answer
        width = Pad * 2f + ChipW * Modes.Length + LateW + GroupGap * 3f;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, H);
        cg = rt.gameObject.AddComponent<CanvasGroup>();
        paper = InkShape.Create(rt, "Paper", InkShape.Kind.Bubble, Comic.Cream, new Vector2(width, H));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = true; paper.Radius = 16f; paper.SetInk(2f, 3.4f); paper.Wobble = 1f;
        paper.ShadowOffset = Comic.StickerShadow; paper.ShadowColor = Comic.PrintShadow; paper.TailWidth = 18f; paper.Seed = 9311;
        zone = paper.gameObject.AddComponent<HudHoverZone>();
        float x = -width * 0.5f + Pad;
        for (int i = 0; i < Modes.Length; i++)
        {
            if (i == 1 || i == 3 || i == 5) x += GroupGap;
            int ii = i;
            var b = InkButton.Create(rt, "Mode" + (int)Modes[i], new Vector2(ChipW, ChipH), Captions[i]);
            InkUI.Centre((RectTransform)b.transform, new Vector2(x + ChipW * 0.5f, 0f), new Vector2(ChipW, ChipH));
            b.hoverScale = 1.12f;
            var art = InkUI.Node("Art", b.body);
            InkUI.Stretch(art);
            arts[i] = art.gameObject.AddComponent<InkPainter>(); arts[i].raycastTarget = false;
            arts[i].onPaint = p => PaintMode(p, p.Area.center, Mathf.Min(p.Area.width, p.Area.height), (int)Modes[ii], col, Clipboard.EchoLate);
            b.onClick = () => Pick(Modes[ii]);
            var dg = b.gameObject.AddComponent<PasteFanDrag>();
            dg.fan = this; dg.mode = Modes[i];
            chips[i] = b;
            x += ChipW;
            if (i == 2)
            {
                // the late toggle rides with the echoes
                lateBtn = InkButton.Create(rt, "Late", new Vector2(LateW, ChipH), "late echo");
                InkUI.Centre((RectTransform)lateBtn.transform, new Vector2(x + LateW * 0.5f, 0f), new Vector2(LateW, ChipH));
                lateBtn.hoverScale = 1.12f;
                var la = InkUI.Node("Art", lateBtn.body);
                InkUI.Stretch(la);
                lateArt = la.gameObject.AddComponent<InkPainter>(); lateArt.raycastTarget = false;
                lateArt.onPaint = p => PaintLate(p, p.Area.center, Clipboard.EchoLate, col);
                lateBtn.onClick = ToggleLate;
                x += LateW;
            }
        }
        rt.gameObject.SetActive(false);
    }

    bool AnyHover() { for (int i = 0; i < chips.Length; i++) if (chips[i] != null && chips[i].Hovered) return true; return lateBtn != null && lateBtn.Hovered; }

    /// <summary>Drums: only the plain paste (echoes, steps and answers are for pitched patterns).</summary>
    public void SetDrums(bool on) { SetSource(0, on); }

    /// <summary>What can go (Clipboard.CanPasteOn's rules, package H): the plain paste always; an echo needs a layer left beyond the source's
    /// <paramref name="layer"/> (± MaxLayer); echoes, steps and answers need a pitched pattern (not <paramref name="isDrums"/>).</summary>
    public void SetSource(int layer, bool isDrums)
    {
        drums = isDrums;
        for (int i = 0; i < chips.Length; i++) chips[i].Interactable = Allowed(Modes[i], layer, isDrums);
        if (lateBtn != null) lateBtn.Interactable = !isDrums;
    }

    public static bool Allowed(Clipboard.PasteMode m, int layer, bool isDrums)
    {
        switch (m)
        {
            case Clipboard.PasteMode.Plain: return true;
            case Clipboard.PasteMode.EchoUp: return !isDrums && layer + 1 <= ProjectConfig.MaxLayer;
            case Clipboard.PasteMode.EchoDown: return !isDrums && layer - 1 >= -ProjectConfig.MaxLayer;
            default: return !isDrums;
        }
    }

    /// <summary>The pictures' colour (the pattern's instrument).</summary>
    public void SetColor(Color c)
    {
        c = Comic.Opaque(c);
        if (c == col) return;
        col = c;
        for (int i = 0; i < arts.Length; i++) arts[i].Repaint();
        lateArt.Repaint();
    }

    /// <summary>Opens beside <paramref name="target"/> (above or below it, the tail on it, kept on screen). <paramref name="byHover"/>: it folds
    /// once the pointer has left the fan and its opener for <see cref="Grace"/> s.</summary>
    public void Open(RectTransform target, Side side, bool byHover = false)
    {
        if (target == null) return;
        var parent = rt.parent as RectTransform;
        if (parent == null) return;
        target.GetWorldCorners(corners);
        Vector2 a = parent.InverseTransformPoint(corners[0]), b = parent.InverseTransformPoint(corners[2]);
        Vector2 centre = (a + b) * 0.5f;
        float halfH = Mathf.Abs(b.y - a.y) * 0.5f;
        Rect pr = parent.rect;
        float y = side == Side.Above ? centre.y + halfH + 18f + H * 0.5f : centre.y - halfH - 18f - H * 0.5f;
        float x = Mathf.Clamp(centre.x, pr.xMin + width * 0.5f + 10f, pr.xMax - width * 0.5f - 10f);
        y = Mathf.Clamp(y, pr.yMin + H * 0.5f + 10f, pr.yMax - H * 0.5f - 10f);
        rt.anchoredPosition = new Vector2(x, y);
        paper.TailTip = new Vector2(centre.x - x, side == Side.Above ? -H * 0.5f - 14f : H * 0.5f + 14f);
        hoverOpened = byHover; leaveT = Time.unscaledTime;
        lateShown = Clipboard.EchoLate; lateBtn.Active = lateShown;
        if (!open)
        {
            open = true; OpenCount++;
            rt.gameObject.SetActive(true);
            rt.SetAsLastSibling();
            popK = 0; rt.localScale = new Vector3(0.6f, 0.8f, 1f);
            AudioPool.UI(ProceduralAudio.Whoosh(), 0.14f, 1.45f);
        }
    }

    public void Close() { if (!open) return; open = false; popK = -1; rt.gameObject.SetActive(false); }

    void Pick(Clipboard.PasteMode m)
    {
        var b = Chip(m);
        if (b != null && !b.Interactable) return;
        onPick?.Invoke(m);
    }

    void ToggleLate()
    {
        Clipboard.EchoLate = !Clipboard.EchoLate;
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, Clipboard.EchoLate ? 1.3f : 0.9f);
        RefreshLate();
    }

    void RefreshLate()
    {
        lateShown = Clipboard.EchoLate;
        lateBtn.Active = lateShown;
        lateBtn.transform.localRotation = Quaternion.Euler(0f, 0f, lateShown ? 4f : 0f);
        lateArt.Repaint();
        arts[1].Repaint(); arts[2].Repaint();   // the echoes show it too
    }

    internal void DragBegin(Clipboard.PasteMode m) { var b = Chip(m); if (b != null && !b.Interactable) return; onDragBegin?.Invoke(m); }
    internal void DragEnd(Clipboard.PasteMode m, Vector2 screen) { var b = Chip(m); if (b != null && !b.Interactable) return; onDragEnd?.Invoke(m, screen); }

    void Update()
    {
        if (!open) return;
        if (Clipboard.EchoLate != lateShown) RefreshLate();
        float now = Time.unscaledTime;
        if (PointerInside || (keepOpen != null && keepOpen())) leaveT = now;
        if (hoverOpened && now - leaveT > Grace) { Close(); return; }
        // a click outside the fan (and not on its opener) folds it
        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !PointerInside && !(keepOpen != null && keepOpen()) && popK < 0 && !PathManager.SimOnly) { Close(); return; }
        if (popK < 0) return;
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float s = popK == 0 ? 0.92f : (popK == 1 ? 1.05f : 1f);
        rt.localScale = new Vector3(s, s, 1f);
        if (++popK > 2) popK = -1;
    }

    // ================================================================== pictures (shared with the cursor's pattern card badge)
    static readonly List<Vector2> pts = new List<Vector2>(32);

    static void Grid(InkPainter p, Vector2 c, float hw, float hh, Color fill, Color ink, float inkW, bool ghost)
    {
        pts.Clear(); pts.Add(c + new Vector2(-hw, 0f)); pts.Add(c + new Vector2(0f, hh)); pts.Add(c + new Vector2(hw, 0f)); pts.Add(c + new Vector2(0f, -hh));
        if (!ghost) p.InkFill(pts, 4, fill, inkW, ink);
        else { p.Fill(pts, 4, Comic.A(fill, 0.35f)); p.Stroke(pts, 4, inkW * 0.8f, Comic.A(ink, 0.7f), true, 3f, 2.4f); }
        Color hatch = Comic.A(ink, ghost ? 0.3f : 0.5f);
        p.Line(c + new Vector2(-hw * 0.5f, hh * 0.5f), c + new Vector2(hw * 0.5f, -hh * 0.5f), 1.1f, hatch);
        p.Line(c + new Vector2(-hw * 0.5f, -hh * 0.5f), c + new Vector2(hw * 0.5f, hh * 0.5f), 1.1f, hatch);
    }

    /// <summary>A little melody: three notes joined by an inked line in <paramref name="col"/> (<paramref name="rise"/> = the middle note's lift).</summary>
    static void Melody(InkPainter p, Vector2 a, float step, float rise, Color col, float scale, float alpha = 1f)
    {
        pts.Clear(); pts.Add(a); pts.Add(a + new Vector2(step, rise)); pts.Add(a + new Vector2(step * 2f, rise * 0.35f));
        p.Stroke(pts, 3, 4.2f * scale, Comic.A(Comic.Ink, alpha), false, 0f, 0f, -1f, true);
        p.Stroke(pts, 3, 2.2f * scale, Comic.A(col, alpha), false, 0f, 0f, -1f, true);
        for (int i = 0; i < 3; i++) { p.Disc(pts[i], 3.4f * scale, Comic.A(Comic.Ink, alpha)); p.Disc(pts[i], 2.1f * scale, Comic.A(col, alpha)); }
    }

    static void Arrow(InkPainter p, Vector2 from, Vector2 to, float w, Color ink)
    {
        p.Line(from, to, w, ink, true);
        p.Head(to + (to - from).normalized * 1.5f, to - from, 5.5f, 7f, ink);
    }

    /// <summary>The picture of paste mode <paramref name="mode"/> (Clipboard.PasteMode as int) centred at <paramref name="c"/> in a box of
    /// <paramref name="s"/> px, the pattern in <paramref name="col"/>; <paramref name="late"/>: the echoes trail a late mark.</summary>
    public static void PaintMode(InkPainter p, Vector2 c, float s, int mode, Color col, bool late)
    {
        float k = s / 44f;
        Color ink = Comic.Ink;
        switch (mode)
        {
            case 1:   // echo ↑: the grid, its ghost floating above, the pattern landing on the ghost
            case 2:   // echo ↓: the ghost below the grid, water lines between
            {
                bool up = mode == 1;
                Vector2 g = c + new Vector2(0f, up ? -11f : 9f) * k, gh = c + new Vector2(0f, up ? 8f : -12f) * k;
                if (!up)
                {
                    p.Line(c + new Vector2(-13f, -2f) * k, c + new Vector2(-5f, -1f) * k, 1.2f, Comic.A(Comic.Cyan, 0.9f));
                    p.Line(c + new Vector2(5f, -2.5f) * k, c + new Vector2(13f, -1.5f) * k, 1.2f, Comic.A(Comic.Cyan, 0.9f));
                }
                Grid(p, g, 13f * k, 7.5f * k, Comic.Cream, ink, 1.8f, false);
                Grid(p, gh, 13f * k, 7.5f * k, col, ink, 1.8f, true);
                Melody(p, gh + new Vector2(-8f, -1f) * k, 8f * k, 4f * k, col, 0.8f * k);
                Arrow(p, c + new Vector2(15f, up ? -8f : 6f) * k, c + new Vector2(15f, up ? 8f : -10f) * k, 1.6f, ink);
                if (late) { p.Disc(gh + new Vector2(14f, 6f) * k, 2.6f * k, ink); p.Disc(gh + new Vector2(14f, 6f) * k, 1.4f * k, Comic.Pop); }
                break;
            }
            case 3:   // answer: the phrase climbs, then its last note comes home (a little house)
            {
                Vector2 h = c + new Vector2(12f, -10f) * k;
                pts.Clear(); pts.Add(c + new Vector2(-15f, -4f) * k); pts.Add(c + new Vector2(-7f, 6f) * k); pts.Add(c + new Vector2(1f, 11f) * k); pts.Add(h + new Vector2(0f, 6f) * k);
                p.Stroke(pts, 4, 4.2f * k, ink, false, 0f, 0f, -1f, true);
                p.Stroke(pts, 4, 2.2f * k, col, false, 0f, 0f, -1f, true);
                for (int i = 0; i < 3; i++) { p.Disc(pts[i], 3.4f * k, ink); p.Disc(pts[i], 2.1f * k, col); }
                // the house
                pts.Clear();
                pts.Add(h + new Vector2(-6f, -5f) * k); pts.Add(h + new Vector2(6f, -5f) * k); pts.Add(h + new Vector2(6f, 1.5f) * k); pts.Add(h + new Vector2(0f, 7f) * k); pts.Add(h + new Vector2(-6f, 1.5f) * k);
                p.InkFill(pts, 5, Comic.Pop, 1.6f, ink);
                p.Disc(h + new Vector2(0f, -2f) * k, 1.6f * k, ink);
                break;
            }
            case 4:   // step ↑: the pattern, then again one stair higher (a sequence)
            case 5:   // step ↓
            {
                float d = mode == 4 ? 1f : -1f;
                Melody(p, c + new Vector2(-16f, -4f * d) * k, 5f * k, 5f * k, col, 0.75f * k, 0.55f);
                Melody(p, c + new Vector2(2f, 5f * d) * k, 5f * k, 5f * k, col, 0.75f * k);
                // the stair under them
                pts.Clear();
                pts.Add(c + new Vector2(-17f, -10f * d - 2f) * k); pts.Add(c + new Vector2(-1f, -10f * d - 2f) * k); pts.Add(c + new Vector2(-1f, -1f * d - 2f) * k); pts.Add(c + new Vector2(16f, -1f * d - 2f) * k);
                p.Stroke(pts, 4, 1.6f, Comic.A(ink, 0.7f));
                p.Head(c + new Vector2(8f, 12f * d) * k, new Vector2(1f, d), 5f, 6f, ink);
                break;
            }
            default:   // plain: the pattern APPEARS on the next grid (v7 §21: nothing jumps between grids) — a short arrow, the pattern on the grid, a sparkle
            {
                Vector2 g = c + new Vector2(6f, -8f) * k;
                Grid(p, g, 13f * k, 7.5f * k, Comic.Cream, ink, 1.8f, false);
                Melody(p, c + new Vector2(-18f, 7f) * k, 4f * k, 4f * k, col, 0.6f * k, 0.55f);
                p.Line(c + new Vector2(-7f, 6f) * k, c + new Vector2(0f, 6f) * k, 1.6f, ink, true);
                p.Head(c + new Vector2(3f, 6f) * k, Vector2.right, 4.5f, 5.5f, ink);
                Melody(p, g + new Vector2(-6f, -1f) * k, 5f * k, 4f * k, col, 0.7f * k);
                Vector2 sp = g + new Vector2(13f, 9f) * k;
                p.Line(sp + new Vector2(-3f, 0f) * k, sp + new Vector2(3f, 0f) * k, 1.6f, Comic.Pop, true);
                p.Line(sp + new Vector2(0f, -3f) * k, sp + new Vector2(0f, 3f) * k, 1.6f, Comic.Pop, true);
                break;
            }
        }
    }

    /// <summary>The late toggle: a cube and its echo trailing an eighth behind (a dashed time arrow); on = the echo solid in colour.</summary>
    public static void PaintLate(InkPainter p, Vector2 c, bool on, Color col)
    {
        p.Cube(c + new Vector2(-6f, 6f), 14f, col, Comic.Ink, 1.3f);
        if (on) p.Cube(c + new Vector2(7f, -7f), 12f, Color.Lerp(col, Comic.Cream, 0.35f), Comic.Ink, 1.2f);
        else p.Cube(c + new Vector2(7f, -7f), 12f, Comic.A(col, 0.3f), Comic.A(Comic.Ink, 0.55f), 1f, true);
        pts.Clear(); pts.Add(c + new Vector2(-12f, -9f)); pts.Add(c + new Vector2(-2f, -13f));
        p.Stroke(pts, 2, 1.4f, Comic.A(Comic.Ink, 0.75f), false, 2.5f, 2f);
        p.Head(c + new Vector2(0f, -14f), new Vector2(1f, -0.4f), 4f, 5f, Comic.A(Comic.Ink, 0.75f));
    }

    /// <summary>A small cream badge with the mode's glyph (↑ echo up, ↓ echo down, the house for an answer, stairs for steps) — the corner of
    /// the pattern card riding the cursor.</summary>
    public static void PaintModeBadge(InkPainter p, Vector2 corner, int mode, Color col)
    {
        if (mode == 0) return;
        Vector2 c = corner + new Vector2(-6f, -6f);
        p.Disc(c + new Vector2(1.2f, -1.6f), 8.2f, Comic.A(Comic.Ink, 0.85f));
        p.Disc(c, 8.2f, Comic.Ink);
        p.Disc(c, 6.8f, Comic.Cream);
        switch (mode)
        {
            case 1: p.Line(c + new Vector2(0f, -3.5f), c + new Vector2(0f, 2.5f), 1.8f, Comic.Ink); p.Head(c + new Vector2(0f, 5f), Vector2.up, 3.6f, 5.2f, Comic.Ink); break;
            case 2: p.Line(c + new Vector2(0f, 3.5f), c + new Vector2(0f, -2.5f), 1.8f, Comic.Ink); p.Head(c + new Vector2(0f, -5f), Vector2.down, 3.6f, 5.2f, Comic.Ink); break;
            case 3:
                pts.Clear(); pts.Add(c + new Vector2(-3.8f, -3.2f)); pts.Add(c + new Vector2(3.8f, -3.2f)); pts.Add(c + new Vector2(3.8f, 1f)); pts.Add(c + new Vector2(0f, 4.4f)); pts.Add(c + new Vector2(-3.8f, 1f));
                p.InkFill(pts, 5, Comic.Pop, 1.2f, Comic.Ink); break;
            default:
            {
                float d = mode == 4 ? 1f : -1f;
                pts.Clear(); pts.Add(c + new Vector2(-4.5f, -2.5f * d)); pts.Add(c + new Vector2(-0.5f, -2.5f * d)); pts.Add(c + new Vector2(-0.5f, 1.5f * d)); pts.Add(c + new Vector2(4.5f, 1.5f * d));
                p.Stroke(pts, 4, 1.6f, Comic.Ink); break;
            }
        }
    }
}

/// <summary>v7: a paste fan chip's drag (the clipboard chip's fan: drag a mode onto a grid to paste it there so).</summary>
public class PasteFanDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public PasteFan fan;
    public Clipboard.PasteMode mode;
    bool dragging;
    public void OnBeginDrag(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left || fan == null) return; dragging = true; fan.DragBegin(mode); }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { if (!dragging) return; dragging = false; if (fan != null) fan.DragEnd(mode, e.position); }
}
