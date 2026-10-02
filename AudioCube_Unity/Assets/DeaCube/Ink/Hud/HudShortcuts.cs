using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v7 (SPEC v7 §8.3, package U2) — "think of other ideas too - just want to introduce musical ideas and make it intuitive and magical for
/// non-music theory producers with shortcuts and stuff": the SHORTCUT SHEET. `?` (or the menu strip's "shortcuts") lays a cream card over a
/// soft veil listing EVERY shortcut of the game in four columns of little sections (play · hand · cube · clipboard · grids · view · song):
/// each row is its key caps (inked paper keys with a lip: letters in lowercase Fredoka, the arrows and ⌘ ⇧ ⌥ drawn), a small picture of what
/// it does and a one- or two-word caption. Esc, `?`, a click on the veil or the × close it. While it is up the world and the other keys are
/// locked ("shortcuts"). Built on first open (nothing at rest). Hints id "shortcutSheet"; Onboarding "shortcuts.opened".
/// </summary>
public class HudShortcuts : MonoBehaviour
{
    public static HudShortcuts I;
    public const string LockOwner = "shortcuts";
    public const float W = 1580f, H = 744f, ColW = 360f, ColGap = 16f, RowH = 33f, CapH = 26f, HeadH = 42f;

    /// <summary>The sheet is up (or popping in).</summary>
    public static bool IsOpen => I != null && I.open;
    /// <summary>Up, or closed this very frame (the key that closed it is the sheet's: nobody else reads it).</summary>
    public static bool OwnsKeysThisFrame => I != null && (I.open || I.closeFrame == Time.frameCount);
    /// <summary>Opens so far (tests / the tutorial).</summary>
    public static int OpenCount { get; private set; }
    /// <summary>The rows the sheet lists (tests: every shortcut of the game).</summary>
    public static int RowCount => I != null ? I.rowCount : 0;
    public static RectTransform Card => I != null ? I.card : null;

    // ------------------------------------------------------------------ the map (the one place that lists every shortcut)
    /// <summary>One row: its caps (tokens: a word / letter = a cap; "+" joins a combo; "/" separates alternatives; "cmd" "shift" "alt" "up"
    /// "down" "left" "right" "wheel" "drag" are drawn), its picture and its caption.</summary>
    public struct Entry { public string[] keys; public string glyph, words; public Entry(string k, string g, string w) { keys = k.Split(' '); glyph = g; words = w; } }
    public struct Section { public string title; public Entry[] rows; }

    public static readonly Section[][] Columns =
    {
        new[]
        {
            new Section { title = "play", rows = new[]
            {
                new Entry("space", "play", "play / pause"), new Entry("home", "stop", "stop"), new Entry("l", "loop", "loop"),
                new Entry("m", "click", "metronome"), new Entry("p", "present", "present"),
                new Entry("j / k / h / b", "punch", "punch (hold)"),
            } },
            new Section { title = "hand", rows = new[]
            {
                new Entry("1 ... 0", "pick", "pick up"), new Entry("esc", "down", "put down"), new Entry("enter", "check", "finish"),
                new Entry("delete", "cross", "delete"), new Entry("[ / ]", "size", "size"), new Entry(".", "dot", "dotted"),
                new Entry("wheel", "size", "size"), new Entry("alt + click", "stack", "stack"), new Entry("space", "loop", "hear it"),
            } },
        },
        new[]
        {
            new Section { title = "cube", rows = new[]
            {
                new Entry("shift + up / down", "octave", "octave copy"), new Entry("alt + up / down", "harmony", "harmony"),
                new Entry("u", "flip", "flip"), new Entry("up / down", "ladder", "octave"), new Entry("t", "twin", "twin"),
                new Entry("w", "shadow", "shadow"), new Entry("g", "stamp", "stamp (hold: all)"),
                new Entry("- / =", "hits", "hits"), new Entry("e", "spot", "spotlight"),
            } },
            new Section { title = "clipboard", rows = new[]
            {
                new Entry("cmd + c", "copy", "copy"), new Entry("cmd + v", "paste", "paste"),
                new Entry("cmd + up / down", "echo", "paste echo"), new Entry("cmd + alt + up / down", "step", "paste step"),
                new Entry("cmd + shift + v", "answer", "paste answer"),
                new Entry("cmd + c on a grid", "copy", "copy its paths"), new Entry("cmd + v on a grid", "paste", "paste paths"),
            } },
        },
        new[]
        {
            new Section { title = "grids", rows = new[]
            {
                new Entry("shift + drag", "select", "select"), new Entry("cmd + c", "copy", "copy grids"), new Entry("cmd + v", "paste", "paste after"),
                new Entry("delete", "cross", "delete grids"), new Entry("shift + up / down", "tower", "octave"), new Entry("shift + right", "buildup", "build-up"),
                new Entry("n", "moon", "drums"), new Entry("i", "deck", "deck"),
            } },
            new Section { title = "song", rows = new[]
            {
                new Entry("cmd + z", "undo", "undo"), new Entry("cmd + shift + z / cmd + y", "redo", "redo"), new Entry("cmd + s", "save", "save"),
                new Entry("cmd + o", "load", "load"), new Entry("?", "keys", "this sheet"),
            } },
        },
        new[]
        {
            new Section { title = "view", rows = new[]
            {
                new Entry("w / a / s / d", "move", "move"), new Entry("left / right", "move", "move"), new Entry("q / e", "orbit", "orbit"),
                new Entry("tab", "next", "next grid"), new Entry("shift + tab", "prev", "back a grid"), new Entry("c", "reset", "reset view"),
                new Entry("o", "all", "see all"), new Entry("f", "follow", "follow"),
            } },
        },
    };

    // ------------------------------------------------------------------ parts
    RectTransform root, card, veilRt; CanvasGroup group; InkShape paper; InkButton closeBtn;
    bool built, open, unlockPending; int rowCount, popK = -1, tick = int.MinValue, openFrame = -9, closeFrame = -9;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OpenCount = 0; }

    public static HudShortcuts Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("ShortcutSheet", typeof(RectTransform));
        go.layer = 5;
        I = go.AddComponent<HudShortcuts>();
        return I;
    }

    void Awake() { if (I == null) I = this; }
    void OnDestroy() { if (I == this) I = null; WorldInput.Unlock(LockOwner); }

    public static void Open() { Ensure().DoOpen(); }
    public static void Close() { if (I != null) I.DoClose(); }
    public static void Toggle() { if (IsOpen) Close(); else Open(); }

    void DoOpen()
    {
        if (open || MainMenu.IsShown || Presenter.Active) return;
        if (!built) Build();
        open = true; openFrame = Time.frameCount; OpenCount++;
        root.gameObject.SetActive(true);
        root.SetAsLastSibling();
        popK = 0; card.localScale = new Vector3(0.6f, 0.6f, 1f); group.alpha = 0.5f;
        WorldInput.Lock(LockOwner, true);
        if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.FinishPath();
        if (UIManager.I != null && UIManager.I.Strip != null && UIManager.I.Strip.IsOpen) UIManager.I.Strip.CloseNow();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.ShortcutsOpened);
    }

    void DoClose()
    {
        if (!open) return;
        open = false; popK = -1; closeFrame = Time.frameCount;
        unlockPending = true;   // the keys stay locked to the end of this frame: the Esc / ? that closed it is nobody else's
        if (root != null) root.gameObject.SetActive(false);
        FocusLoop.ClaimEsc();
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 0.85f);
    }

    void LateUpdate()
    {
        if (!unlockPending) return;
        unlockPending = false;
        if (!open) WorldInput.Unlock(LockOwner);
    }

    // ================================================================== build (on first open)
    void Build()
    {
        built = true;
        root = InkUI.Node("Shortcuts", InkUI.OverlayRoot);
        InkUI.Stretch(root);
        group = root.gameObject.AddComponent<CanvasGroup>();
        veilRt = InkUI.Node("Veil", root);
        InkUI.Stretch(veilRt);
        var veil = veilRt.gameObject.AddComponent<Image>();
        veil.color = Comic.Veil; veil.raycastTarget = true;
        var vb = veilRt.gameObject.AddComponent<InkButton>();
        vb.onClick = Close; vb.hoverScale = 1f; vb.pressOffset = Vector2.zero;
        card = InkUI.Node("Card", root);
        InkUI.Centre(card, new Vector2(0f, -6f), new Vector2(W, H));
        card.localRotation = Quaternion.Euler(0f, 0f, -0.6f);
        paper = InkShape.Create(card, "Paper", InkShape.Kind.RoundRect, Comic.Cream, new Vector2(W, H));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = true; paper.Radius = 26f; paper.SetInk(2.6f, 4.6f); paper.Wobble = 1.6f;
        paper.ShadowOffset = new Vector2(9f, -11f); paper.ShadowColor = Comic.PrintShadow; paper.Seed = 9511;
        // title: a key cap with "?" and the word
        var tc = Cap(card, "?", 0f);
        ((RectTransform)tc.transform).anchorMin = ((RectTransform)tc.transform).anchorMax = new Vector2(0f, 1f);
        ((RectTransform)tc.transform).anchoredPosition = new Vector2(58f, -46f);
        var title = HudKit.Words(card, "Title", "shortcuts", 38f, Comic.Ink);
        var trt = title.rectTransform; trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0f, 0.5f); trt.anchoredPosition = new Vector2(86f, -44f);
        var wave = InkWave.Create(card, "TitleWave", Comic.Ink, new Vector2(Mathf.Max(80f, title.preferredWidth), 10f));
        wave.Animate = false;
        var wrt = wave.rectTransform; wrt.anchorMin = wrt.anchorMax = new Vector2(0f, 1f); wrt.pivot = new Vector2(0f, 0.5f); wrt.anchoredPosition = new Vector2(84f, -68f);
        // the × sticker
        closeBtn = InkButton.Create(card, "Close", new Vector2(46f, 46f), "close");
        var ct = (RectTransform)closeBtn.transform; ct.anchorMin = ct.anchorMax = new Vector2(1f, 1f); ct.anchoredPosition = new Vector2(-30f, -30f);
        var st = InkShape.Create(closeBtn.body, "Sticker", InkShape.Kind.Sticker, Comic.Cream, new Vector2(38f, 38f));
        st.raycastTarget = false; st.RotJitter = 4f; st.SetInk(2.2f, 3.4f); st.ShadowOffset = new Vector2(3f, -4f); st.Seed = 9513;
        InkDraw.Cross(closeBtn.AddDrawing(new Vector2(20f, 20f)), Comic.Ink, 3.4f);
        closeBtn.onClick = Close;
        // the columns
        for (int c = 0; c < Columns.Length; c++)
        {
            float x = 48f + c * (ColW + ColGap), y = 108f;
            foreach (var sec in Columns[c])
            {
                var head = HudKit.Words(card, "Head_" + sec.title, sec.title, 26f, Comic.Ink);
                var hrt = head.rectTransform; hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 1f); hrt.pivot = new Vector2(0f, 0.5f); hrt.anchoredPosition = new Vector2(x, -y);
                var hw = InkWave.Create(card, "HeadWave_" + sec.title, Comic.A(Comic.Ink, 0.55f), new Vector2(ColW - 30f, 8f));
                hw.Animate = false; hw.Thickness = 1.8f;
                var hwr = hw.rectTransform; hwr.anchorMin = hwr.anchorMax = new Vector2(0f, 1f); hwr.pivot = new Vector2(0f, 0.5f); hwr.anchoredPosition = new Vector2(x, -y - 20f);
                y += HeadH;
                foreach (var e in sec.rows) { Row(card, e, x, y); y += RowH; rowCount++; }
                y += 16f;
            }
        }
        Hints.Register("shortcutSheet", card);
        root.gameObject.SetActive(false);
    }

    void Row(RectTransform parent, Entry e, float x, float y)
    {
        var row = InkUI.Node("Row_" + e.words, parent);
        row.anchorMin = row.anchorMax = new Vector2(0f, 1f); row.pivot = new Vector2(0f, 0.5f);
        row.sizeDelta = new Vector2(ColW, RowH); row.anchoredPosition = new Vector2(x, -y);
        float cx = 0f;
        foreach (var k in e.keys)
        {
            if (k == "...")
            {
                var dots = InkPainter.Create(row, "Dots", new Vector2(18f, RowH), q => { Vector2 m = q.Area.center; for (int d = -1; d <= 1; d++) q.Disc(m + new Vector2(d * 4.5f, -2f), 1.5f, Comic.A(Comic.Ink, 0.7f)); });
                var drt = dots.rectTransform; drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f); drt.pivot = new Vector2(0.5f, 0.5f); drt.anchoredPosition = new Vector2(cx + 9f, 0f);
                cx += 18f;
                continue;
            }
            if (k == "+" || k == "/")
            {
                var sep = HudKit.Words(row, "Sep", k, 18f, Comic.A(Comic.Ink, 0.6f), TextAlignmentOptions.Center);
                var srt = sep.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f); srt.pivot = new Vector2(0.5f, 0.5f);
                srt.sizeDelta = new Vector2(14f, RowH); srt.anchoredPosition = new Vector2(cx + 7f, 1f);
                cx += 14f;
                continue;
            }
            var cap = Cap(row, k, cx);
            cx += ((RectTransform)cap.transform).sizeDelta.x + 3f;
        }
        float gx = Mathf.Max(cx + 10f, 176f);
        var g = InkPainter.Create(row, "Glyph", new Vector2(30f, 30f), p => PaintGlyph(p, e.glyph));
        var grt = g.rectTransform; grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f); grt.pivot = new Vector2(0.5f, 0.5f); grt.anchoredPosition = new Vector2(gx + 15f, 0f);
        var w = HudKit.Words(row, "Words", e.words, 21f, Comic.Ink);
        var wrt = w.rectTransform; wrt.anchorMin = wrt.anchorMax = new Vector2(0f, 0.5f); wrt.pivot = new Vector2(0f, 0.5f); wrt.anchoredPosition = new Vector2(gx + 38f, 1f);
    }

    static bool Drawn(string k) => k == "cmd" || k == "shift" || k == "alt" || k == "up" || k == "down" || k == "left" || k == "right" || k == "wheel" || k == "drag";

    /// <summary>A key cap: inked paper with a lip, the key's letter / word in lowercase Fredoka, or its drawn symbol.</summary>
    InkShape Cap(Transform parent, string k, float x)
    {
        bool drawn = Drawn(k);
        float w = drawn ? (k == "shift" || k == "wheel" || k == "drag" ? 38f : 28f) : Mathf.Max(28f, 14f + 10.5f * k.Length);
        var cap = InkShape.Create(parent, "Key_" + k, InkShape.Kind.RoundRect, Comic.Cream, new Vector2(w, CapH));
        cap.raycastTarget = false; cap.Radius = 6f; cap.SetInk(1.5f, 2.2f); cap.ShadowOffset = new Vector2(0f, -3.5f); cap.ShadowColor = InkUI.PaperDeep; cap.Wobble = 0.5f;
        var rt = cap.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x + w * 0.5f, 1f);
        if (drawn)
        {
            var p = InkPainter.Create(cap.transform, "Sym", new Vector2(w, CapH), q => PaintKey(q, k));
            InkUI.Stretch(p.rectTransform);
        }
        else
        {
            var t = HudKit.Words(cap.transform, "Label", k, k.Length > 1 ? 16f : 19f, Comic.Ink, TextAlignmentOptions.Center);
            InkUI.Stretch(t.rectTransform);
            t.rectTransform.anchoredPosition = new Vector2(0f, 1f);
        }
        return cap;
    }

    static readonly List<Vector2> pts = new List<Vector2>(32);

    /// <summary>The drawn key symbols: arrows, ⌘, ⇧, ⌥, the wheel, a drag.</summary>
    static void PaintKey(InkPainter p, string k)
    {
        Vector2 c = p.Area.center + new Vector2(0f, 1f);
        Color ink = Comic.Ink;
        switch (k)
        {
            case "up": case "down": case "left": case "right":
            {
                Vector2 d = k == "up" ? Vector2.up : k == "down" ? Vector2.down : k == "left" ? Vector2.left : Vector2.right;
                p.Line(c - d * 6f, c + d * 2f, 2f, ink, true);
                p.Head(c + d * 7f, d, 6f, 8f, ink);
                break;
            }
            case "shift":
            {
                pts.Clear();
                pts.Add(c + new Vector2(0f, 8f)); pts.Add(c + new Vector2(8f, 0f)); pts.Add(c + new Vector2(3.5f, 0f)); pts.Add(c + new Vector2(3.5f, -7f));
                pts.Add(c + new Vector2(-3.5f, -7f)); pts.Add(c + new Vector2(-3.5f, 0f)); pts.Add(c + new Vector2(-8f, 0f));
                p.Stroke(pts, pts.Count, 1.6f, ink, true);
                break;
            }
            case "alt":
                p.Line(c + new Vector2(-8f, 5f), c + new Vector2(-3f, 5f), 1.6f, ink, true);
                p.Line(c + new Vector2(-3f, 5f), c + new Vector2(3f, -5f), 1.6f, ink, true);
                p.Line(c + new Vector2(3f, -5f), c + new Vector2(8f, -5f), 1.6f, ink, true);
                p.Line(c + new Vector2(2f, 5f), c + new Vector2(8f, 5f), 1.6f, ink, true);
                break;
            case "cmd":
            {
                float r = 2.4f, s = 3.4f;
                p.Arc(c + new Vector2(-s - r * 0.2f, s + r * 0.2f), r, 1.4f, ink, 0f, 270f);
                p.Arc(c + new Vector2(s + r * 0.2f, s + r * 0.2f), r, 1.4f, ink, -90f, 180f);
                p.Arc(c + new Vector2(s + r * 0.2f, -s - r * 0.2f), r, 1.4f, ink, 180f, 450f);
                p.Arc(c + new Vector2(-s - r * 0.2f, -s - r * 0.2f), r, 1.4f, ink, 90f, 360f);
                p.Line(c + new Vector2(-s, s), c + new Vector2(s, s), 1.4f, ink); p.Line(c + new Vector2(-s, -s), c + new Vector2(s, -s), 1.4f, ink);
                p.Line(c + new Vector2(-s, -s), c + new Vector2(-s, s), 1.4f, ink); p.Line(c + new Vector2(s, -s), c + new Vector2(s, s), 1.4f, ink);
                break;
            }
            case "wheel":
                p.RoundRect(new Rect(c.x - 6f, c.y - 9f, 12f, 17f), 6f, Comic.Cream, 1.5f, ink);
                p.Line(c + new Vector2(0f, 7f), c + new Vector2(0f, 2f), 2.2f, ink, true);
                p.Head(c + new Vector2(12f, 6f), Vector2.up, 4f, 5f, ink); p.Head(c + new Vector2(12f, -6f), Vector2.down, 4f, 5f, ink);
                break;
            case "drag":
                p.RoundRect(new Rect(c.x - 12f, c.y - 7f, 20f, 13f), 2f, Comic.A(Comic.Cyan, 0.25f), 1.3f, ink, 3f, 2f);
                p.Head(c + new Vector2(13f, -8f), new Vector2(1f, -1f), 5f, 5f, ink);
                break;
        }
    }

    /// <summary>The row pictures: small ink drawings of what each shortcut does.</summary>
    static void PaintGlyph(InkPainter p, string g)
    {
        Vector2 c = p.Area.center;
        Color ink = Comic.Ink, col = new Color(0.62f, 0.8f, 1f), pop = Comic.Pop;
        switch (g)
        {
            case "play":
                pts.Clear(); pts.Add(c + new Vector2(-6f, 8f)); pts.Add(c + new Vector2(9f, 0f)); pts.Add(c + new Vector2(-6f, -8f));
                p.InkFill(pts, 3, pop, 1.6f, ink); break;
            case "stop": p.RoundRect(new Rect(c.x - 7f, c.y - 7f, 14f, 14f), 2.5f, Comic.Magenta, 1.6f, ink); break;
            case "loop":
                pts.Clear(); InkPainter.ArcPoints(pts, c, 9f, 7f, 120f, 420f, 18);
                p.Stroke(pts, pts.Count, 1.8f, ink); p.Head(pts[pts.Count - 1], pts[pts.Count - 1] - pts[pts.Count - 3], 5f, 6f, ink); break;
            case "click":
                pts.Clear(); pts.Add(c + new Vector2(-7f, -9f)); pts.Add(c + new Vector2(7f, -9f)); pts.Add(c + new Vector2(3f, 9f)); pts.Add(c + new Vector2(-3f, 9f));
                p.InkFill(pts, 4, Comic.Cream, 1.5f, ink); p.Line(c + new Vector2(0f, -6f), c + new Vector2(6f, 7f), 1.6f, ink, true); break;
            case "present":
                pts.Clear();
                for (int i = 0; i < 10; i++) { float a = (90f + 36f * i) * Mathf.Deg2Rad; float r = i % 2 == 0 ? 10f : 4.5f; pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                p.InkFill(pts, 10, pop, 1.5f, ink); break;
            case "punch":
                p.Disc(c + new Vector2(1.5f, -2f), 9f, Comic.A(ink, 0.85f)); p.Disc(c, 9f, ink); p.Disc(c, 7.4f, Comic.Magenta); p.Disc(c + new Vector2(-2f, 2f), 2.4f, Comic.A(Color.white, 0.8f)); break;
            case "pick": p.Cube(c + new Vector2(0f, 2f), 16f, col, ink, 1.4f); p.Line(c + new Vector2(-8f, -10f), c + new Vector2(8f, -10f), 1.2f, Comic.A(ink, 0.5f)); break;
            case "down": p.Cube(c + new Vector2(0f, -3f), 15f, Comic.A(col, 0.5f), Comic.A(ink, 0.6f), 1.2f, true); p.Head(c + new Vector2(0f, 7f), Vector2.down, 5f, 6f, ink); break;
            case "check": p.Line(c + new Vector2(-8f, 0f), c + new Vector2(-2f, -6f), 2.6f, ink, true); p.Line(c + new Vector2(-2f, -6f), c + new Vector2(9f, 7f), 2.6f, ink, true); break;
            case "cross": p.Line(c + new Vector2(-6f, -6f), c + new Vector2(6f, 6f), 2.6f, InkUI.Red, true); p.Line(c + new Vector2(-6f, 6f), c + new Vector2(6f, -6f), 2.6f, InkUI.Red, true); break;
            case "size": p.Cube(c + new Vector2(-7f, -4f), 9f, col, ink, 1.1f); p.Cube(c + new Vector2(5f, 1f), 17f, col, ink, 1.3f); break;
            case "dot": p.Cube(c + new Vector2(-2f, 0f), 15f, col, ink, 1.3f); p.Disc(c + new Vector2(10f, 7f), 3f, ink); p.Disc(c + new Vector2(10f, 7f), 1.8f, Comic.Cream); break;
            case "octave":
                p.Cube(c + new Vector2(4f, 6f), 12f, Comic.A(col, 0.4f), Comic.A(ink, 0.7f), 1.1f, true);
                p.Cube(c + new Vector2(-2f, -3f), 14f, col, ink, 1.3f);
                p.Cube(c + new Vector2(-7f, -11f), 10f, Comic.A(col, 0.4f), Comic.A(ink, 0.5f), 1f, true); break;
            case "harmony":
                p.Cube(c + new Vector2(-4f, -2f), 15f, col, ink, 1.3f); p.Cube(c + new Vector2(8f, 8f), 10f, Color.Lerp(col, Comic.Cream, 0.35f), ink, 1.1f);
                p.Arc(c + new Vector2(3f, 4f), 6f, 1.2f, ink, 110f, 20f); break;
            case "flip":
                for (float x = -11f; x < 11f; x += 5f) p.Line(c + new Vector2(x, 0f), c + new Vector2(x + 2.8f, 0f), 1.2f, Comic.A(ink, 0.6f));
                p.Line(c + new Vector2(-8f, 3f), c + new Vector2(0f, 10f), 2f, ink, true); p.Line(c + new Vector2(0f, 10f), c + new Vector2(8f, 5f), 2f, ink, true);
                p.Line(c + new Vector2(-8f, -3f), c + new Vector2(0f, -10f), 1.6f, Comic.A(ink, 0.5f), true); p.Line(c + new Vector2(0f, -10f), c + new Vector2(8f, -5f), 1.6f, Comic.A(ink, 0.5f), true); break;
            case "ladder":
                p.Line(c + new Vector2(-5f, -11f), c + new Vector2(-5f, 11f), 1.5f, ink); p.Line(c + new Vector2(5f, -11f), c + new Vector2(5f, 11f), 1.5f, ink);
                p.Line(c + new Vector2(-5f, 6f), c + new Vector2(5f, 6f), 1.2f, Comic.A(ink, 0.6f)); p.Line(c + new Vector2(-5f, -6f), c + new Vector2(5f, -6f), 1.2f, Comic.A(ink, 0.6f));
                p.Line(c + new Vector2(-5f, 0f), c + new Vector2(5f, 0f), 4f, ink); break;
            case "twin": p.Cube(c + new Vector2(-6f, 0f), 12f, col, ink, 1.2f); p.Cube(c + new Vector2(7f, 0f), 12f, col, ink, 1.2f); break;
            case "shadow":
                p.Cube(c + new Vector2(8f, -5f), 9f, Comic.A(col, 0.3f), Comic.A(ink, 0.35f), 1f); p.Cube(c + new Vector2(3f, -2f), 11f, Comic.A(col, 0.55f), Comic.A(ink, 0.6f), 1.1f);
                p.Cube(c + new Vector2(-5f, 2f), 14f, col, ink, 1.3f); break;
            case "stamp":
                pts.Clear(); pts.Add(c + new Vector2(-10f, -5f)); pts.Add(c + new Vector2(0f, -1f)); pts.Add(c + new Vector2(10f, -5f)); pts.Add(c + new Vector2(0f, -9f));
                p.InkFill(pts, 4, InkUI.PaperDeep, 1.3f, ink); p.Cube(c + new Vector2(0f, 5f), 12f, col, ink, 1.2f); break;
            case "hits":
                for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.PI * 2f; Vector2 d = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8f; p.Disc(d, 2.6f, ink); if (i % 2 == 1) p.Disc(d, 1.4f, Comic.Cream); }
                break;
            case "spot":
                pts.Clear(); pts.Add(c + new Vector2(-3f, 11f)); pts.Add(c + new Vector2(3f, 11f)); pts.Add(c + new Vector2(10f, -8f)); pts.Add(c + new Vector2(-10f, -8f));
                p.Fill(pts, 4, Comic.A(pop, 0.55f)); p.Cube(c + new Vector2(0f, -6f), 9f, col, ink, 1f); break;
            case "copy":
                p.RoundRect(new Rect(c.x - 9f, c.y - 3f, 13f, 12f), 2f, InkUI.PaperDeep, 1.3f, ink);
                p.RoundRect(new Rect(c.x - 4f, c.y - 9f, 13f, 12f), 2f, Comic.Cream, 1.3f, ink); break;
            case "paste": PasteFan.PaintMode(p, c, 30f, 0, col, false); break;
            case "echo": PasteFan.PaintMode(p, c, 30f, 1, col, false); break;
            case "step": PasteFan.PaintMode(p, c, 30f, 4, col, false); break;
            case "stack": p.Cube(c + new Vector2(0f, -6f), 13f, col, ink, 1.2f); p.Cube(c + new Vector2(0f, 6f), 11f, Color.Lerp(col, pop, 0.5f), ink, 1.1f); break;
            case "answer": PasteFan.PaintMode(p, c, 30f, 3, col, false); break;
            case "select":
                p.RoundRect(new Rect(c.x - 11f, c.y - 8f, 22f, 16f), 2f, Comic.A(Comic.Cyan, 0.2f), 1.4f, ink, 3f, 2.2f);
                p.Disc(c + new Vector2(-4f, 0f), 2.6f, col); p.Disc(c + new Vector2(4f, 0f), 2.6f, col); break;
            case "tower":
                p.Line(c + new Vector2(-4f, -2f), c + new Vector2(-4f, -11f), 1.2f, Comic.A(ink, 0.6f)); p.Line(c + new Vector2(4f, -2f), c + new Vector2(4f, -11f), 1.2f, Comic.A(ink, 0.6f));
                pts.Clear(); pts.Add(c + new Vector2(-9f, 2f)); pts.Add(c + new Vector2(0f, 7f)); pts.Add(c + new Vector2(9f, 2f)); pts.Add(c + new Vector2(0f, -3f));
                p.InkFill(pts, 4, col, 1.3f, ink); break;
            case "buildup": HudSelection.PaintBuildUp(p, c, 0.85f); break;
            case "moon":
                p.Disc(c, 9f, ink); p.Disc(c, 7.6f, new Color(0.96f, 0.92f, 0.76f)); p.Disc(c + new Vector2(4f, 3f), 6.5f, Comic.Cream); break;
            case "deck":
                p.RoundRect(new Rect(c.x - 10f, c.y - 6f, 10f, 14f), 2f, InkUI.PaperDeep, 1.2f, ink);
                p.RoundRect(new Rect(c.x - 5f, c.y - 8f, 10f, 14f), 2f, Comic.Cream, 1.2f, ink);
                p.RoundRect(new Rect(c.x + 0f, c.y - 10f, 10f, 14f), 2f, col, 1.2f, ink); break;
            case "undo": case "redo":
            {
                float sx = g == "redo" ? -1f : 1f;
                pts.Clear(); InkPainter.ArcPoints(pts, c, 7f, 6f, -35f, 205f, 16);
                for (int i = 0; i < pts.Count; i++) { var q = pts[i] - c; pts[i] = c + new Vector2(q.x * sx, q.y); }
                p.Stroke(pts, pts.Count, 2f, ink); p.Head(pts[pts.Count - 1], pts[pts.Count - 1] - pts[pts.Count - 3], 5f, 6f, ink); break;
            }
            case "save": p.RoundRect(new Rect(c.x - 8f, c.y - 9f, 16f, 18f), 2f, Comic.Cream, 1.4f, ink); p.Line(c + new Vector2(-4f, -1f), c + new Vector2(-1f, -4f), 1.8f, ink, true); p.Line(c + new Vector2(-1f, -4f), c + new Vector2(5f, 3f), 1.8f, ink, true); break;
            case "load": p.RoundRect(new Rect(c.x - 10f, c.y - 7f, 20f, 13f), 2f, pop, 1.4f, ink); p.Line(c + new Vector2(-10f, 6f), c + new Vector2(-3f, 6f), 1.4f, ink); break;
            case "keys": p.RoundRect(new Rect(c.x - 9f, c.y - 8f, 18f, 16f), 4f, Comic.Cream, 1.4f, ink); p.Disc(c + new Vector2(0f, -4f), 1.4f, ink); p.Arc(c + new Vector2(0f, 2f), 3f, 1.4f, ink, 180f, -60f); break;
            case "move":
                p.Head(c + new Vector2(0f, 10f), Vector2.up, 5f, 6f, ink); p.Head(c + new Vector2(0f, -10f), Vector2.down, 5f, 6f, ink);
                p.Head(c + new Vector2(-10f, 0f), Vector2.left, 5f, 6f, ink); p.Head(c + new Vector2(10f, 0f), Vector2.right, 5f, 6f, ink);
                p.Disc(c, 2.4f, ink); break;
            case "orbit":
                pts.Clear(); InkPainter.ArcPoints(pts, c, 10f, 5f, 200f, 520f, 20);
                p.Stroke(pts, pts.Count, 1.6f, ink); p.Head(pts[pts.Count - 1], pts[pts.Count - 1] - pts[pts.Count - 3], 5f, 5f, ink); p.Cube(c, 9f, col, ink, 1f); break;
            case "next": case "prev":
            {
                float d = g == "next" ? 1f : -1f;
                p.Line(c + new Vector2(-8f * d, 0f), c + new Vector2(5f * d, 0f), 1.8f, ink, true); p.Head(c + new Vector2(9f * d, 0f), new Vector2(d, 0f), 5f, 6f, ink);
                p.Line(c + new Vector2(11f * d, -7f), c + new Vector2(11f * d, 7f), 1.8f, ink, true); break;
            }
            case "reset":
                p.Disc(c, 8f, ink); p.Disc(c, 6.4f, Comic.Cream); p.Disc(c, 2.4f, ink);
                p.Line(c + new Vector2(0f, 8f), c + new Vector2(0f, 12f), 1.4f, ink); p.Line(c + new Vector2(0f, -8f), c + new Vector2(0f, -12f), 1.4f, ink); break;
            case "all":
                p.Line(c + new Vector2(-10f, 5f), c + new Vector2(-10f, 10f), 1.8f, ink); p.Line(c + new Vector2(-10f, 10f), c + new Vector2(-5f, 10f), 1.8f, ink);
                p.Line(c + new Vector2(10f, 5f), c + new Vector2(10f, 10f), 1.8f, ink); p.Line(c + new Vector2(10f, 10f), c + new Vector2(5f, 10f), 1.8f, ink);
                p.Line(c + new Vector2(-10f, -5f), c + new Vector2(-10f, -10f), 1.8f, ink); p.Line(c + new Vector2(-10f, -10f), c + new Vector2(-5f, -10f), 1.8f, ink);
                p.Line(c + new Vector2(10f, -5f), c + new Vector2(10f, -10f), 1.8f, ink); p.Line(c + new Vector2(10f, -10f), c + new Vector2(5f, -10f), 1.8f, ink);
                p.Disc(c + new Vector2(-3f, 0f), 2.2f, col); p.Disc(c + new Vector2(3f, 0f), 2.2f, col); break;
            case "follow":
                pts.Clear(); InkPainter.ArcPoints(pts, c, 10f, 6f, 0f, 360f, 24);
                p.Fill(pts, pts.Count, Comic.Cream); p.Stroke(pts, pts.Count, 1.5f, ink, true);
                p.Disc(c, 3.6f, ink); p.Disc(c + new Vector2(-1f, 1f), 1.1f, Comic.Cream); break;
        }
    }

    // ================================================================== per frame
    void Update()
    {
        if (!open) return;
        if (MainMenu.IsShown || Presenter.Active) { DoClose(); return; }
        if (!InputUtil.TypingInField && Time.frameCount != openFrame)
        {
            bool q = KeyShim.Down(KeyCode.Slash) && InputUtil.Shift;
            if (KeyShim.Down(KeyCode.Escape) || q || KeyShim.Down(KeyCode.Question)) { DoClose(); return; }
        }
        if (popK < 0) return;
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float s = popK == 0 ? 0.92f : (popK == 1 ? 1.03f : 1f);
        card.localScale = new Vector3(s, s, 1f);
        group.alpha = popK == 0 ? 0.85f : 1f;
        if (++popK > 2) popK = -1;
    }

    /// <summary>Tests: one frame of the sheet's own key reading (with KeyShim.Sim).</summary>
    public void StepForTest() { Update(); }
}
