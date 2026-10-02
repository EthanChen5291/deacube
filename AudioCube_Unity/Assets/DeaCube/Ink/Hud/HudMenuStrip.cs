using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 world HUD, top-left (research §4.1): the DeaCube logo cube is the only menu button; beside it undo / redo are two hand-drawn curved
/// arrows (no disc; dashed at 35 % when there is nothing to undo). The logo (or Esc) slides in a Melatonin-style paper strip from the left:
/// full height, 360 px, cream, a wobbly right edge with a hard shadow, lowercase Fredoka items at 30 px (resume, new song, save, load, learn,
/// settings → fullscreen / camera follow / captions, clear song (hold), main menu); the focused item gets the wavy underline and the scene
/// dims under a violet veil. ↑ ↓ Enter work while it is open; the world and its keys are locked meanwhile ("strip").
/// Button names keep the v3 contract: "Home" (main menu), "NewSong", "Save", "Load", "Undo", "Redo".
/// "My songs" (SongLibrary): "save" saves the world's song into its library song; "Load" reads "my songs" and opens the main menu with its songs
/// shelf; the world's song shows by its library name under the title ("not saved yet" while it is not in the library).
/// </summary>
public class HudMenuStrip : MonoBehaviour
{
    public static HudMenuStrip I;
    public const float Width = 360f;
    public const string LockOwner = "strip";

    public bool IsOpen => open;
    public HudButton LogoButton => logoBtn;
    public HudButton UndoButton => undoBtn;
    public HudButton RedoButton => redoBtn;
    public RectTransform Cluster => cluster;
    /// <summary>The open strip's slide progress (0 closed … 1 open).</summary>
    public float Shown => shown;
    /// <summary>Items in order (tests): name → button.</summary>
    public readonly List<HudButton> Items = new List<HudButton>();

    RectTransform cluster, strip, veilRt, subRt;
    Image veil; CanvasGroup stripGroup, veilGroup;
    HudButton logoBtn, undoBtn, redoBtn; InkPainter undoArt, redoArt, logoArt; bool logoHovShown;
    bool open, settingsOpen, lastCanUndo = true, lastCanRedo = true;
    float shown; int tick = int.MinValue, focus = -1, openFrame = -1;
    readonly List<Row> rows = new List<Row>();
    TextMeshProUGUI songName; Image songNote;

    class Row
    {
        public RectTransform rt; public TextMeshProUGUI text; public HudButton btn; public InkWave wave; public bool sub; public Func<bool> state; public InkPainter toggle;
    }

    public static HudMenuStrip Build(RectTransform hud)
    {
        var go = new GameObject("MenuStrip", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(hud, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var m = go.AddComponent<HudMenuStrip>();
        m.BuildAll(hud);
        return m;
    }

    static readonly List<Vector2> logoQ = new List<Vector2>(6);

    /// <summary>v7: the logo cube printed on the ink sticker — a magenta and a cyan copy a hair to each side (wider on hover), then the cube in cream
    /// (top), soft lilac (left) and lilac (right) with thin ink seams, the present sticker's palette.</summary>
    void PaintLogo(InkPainter p)
    {
        float m = logoBtn != null && logoBtn.Hover ? 3f : 1.5f;
        Vector2 c = p.Area.center + new Vector2(0f, 0.5f);
        float R = 15f, hx = R * 0.8660254f;
        Vector2 top = c + new Vector2(0f, R), ur = c + new Vector2(hx, R * 0.5f), lr = c + new Vector2(hx, -R * 0.5f);
        Vector2 bot = c + new Vector2(0f, -R), ll = c + new Vector2(-hx, -R * 0.5f), ul = c + new Vector2(-hx, R * 0.5f);
        // the misregistration: the cube's silhouette in magenta and cyan, offset
        logoQ.Clear(); logoQ.Add(top); logoQ.Add(ur); logoQ.Add(lr); logoQ.Add(bot); logoQ.Add(ll); logoQ.Add(ul);
        p.Shift = new Vector2(m, -0.4f); p.Fill(logoQ, 6, Comic.Magenta);
        p.Shift = new Vector2(-m, 0.4f); p.Fill(logoQ, 6, Comic.Cyan);
        p.Shift = Vector2.zero;
        // the faces
        p.Quad(top, ur, c, ul, Comic.Cream);
        p.Quad(ul, c, bot, ll, new Color(0.86f, 0.80f, 0.94f));
        p.Quad(c, ur, lr, bot, new Color(0.72f, 0.64f, 0.80f));
        p.Line(c, ul, 1f, Comic.Ink); p.Line(c, ur, 1f, Comic.Ink); p.Line(c, bot, 1f, Comic.Ink);
    }

    void Awake() { I = this; SongLibrary.OnChanged += RefreshSongName; }
    void OnDestroy() { SongLibrary.OnChanged -= RefreshSongName; if (I == this) I = null; WorldInput.Unlock(LockOwner); }

    // ------------------------------------------------------------------ build
    void BuildAll(RectTransform hud)
    {
        // the cluster (always on screen)
        cluster = HudKit.Node(hud, "TopLeft", new Vector2(0f, 1f), new Vector2(92f, -46f), new Vector2(160f, 60f));
        // v7 (the user: "the main menu button should be the same vibe as the play vibe"): the logo cube printed on an INK sticker like the
        // present button's (HudPresent) — a cream die-cut edge, a hard magenta shadow, tilted the other way (+3°), the cube in cream / lilac with
        // the magenta / cyan misregistration; hover jiggles on twos and widens the misprint
        InkShape logoBody;
        logoBtn = HudKit.Sticker(cluster, "Logo", new Vector2(0.5f, 0.5f), new Vector2(-50f, 0f), new Vector2(54f, 50f), InkShape.Kind.Sticker, Comic.Ink,
                                 PaintLogo, "menu", Toggle, out logoBody, out logoArt);
        var lrt = logoBtn.transform as RectTransform;
        logoBody.Ink = Comic.Cream; logoBody.SetInk(1.8f, 2.6f);
        logoBody.ShadowColor = Comic.Magenta; logoBody.ShadowOffset = new Vector2(4f, -5f);
        logoBody.RotJitter = 1.2f; logoBody.Seed = 1312;
        logoBody.Radius = 6f; logoBody.Wobble = 0.7f;   // the present's crisp die-cut corners at this smaller size (the same wobble reads as a blob here)
        lrt.localEulerAngles = new Vector3(0f, 0f, 3f);
        var lh = logoBtn.GetComponent<InkHover>(); lh.jiggle = 1.5f; lh.baseAngle = 3f; lh.hoverScale = 1.08f;

        undoArt = InkPainter.Create(cluster, "Undo", new Vector2(36f, 36f), p => PaintArrow(p, false, History.CanUndo), true);
        undoArt.rectTransform.anchoredPosition = new Vector2(3f, 0f);
        undoBtn = HudKit.Control(undoArt, "undo", History.Undo, null, null, undoArt);
        redoArt = InkPainter.Create(cluster, "Redo", new Vector2(36f, 36f), p => PaintArrow(p, true, History.CanRedo), true);
        redoArt.rectTransform.anchoredPosition = new Vector2(43f, 0f);
        redoBtn = HudKit.Control(redoArt, "redo", History.Redo, null, null, redoArt);

        // the veil (clicks on it close the strip) and the strip itself
        veilRt = HudKit.Stretch(transform, "Veil");
        veil = veilRt.gameObject.AddComponent<Image>();
        veil.color = Comic.Veil; veil.raycastTarget = true;
        veilGroup = veilRt.gameObject.AddComponent<CanvasGroup>();
        var vb = veilRt.gameObject.AddComponent<HudButton>(); vb.bg = null; vb.icon = null; vb.animateScale = false; vb.onClick = Close;
        veilRt.gameObject.name = "StripVeil";

        strip = HudKit.Node(transform, "Strip", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(Width + 20f, 0f));
        strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(0f, 1f); strip.pivot = new Vector2(0f, 0.5f);
        strip.offsetMin = new Vector2(-20f, -24f); strip.offsetMax = new Vector2(Width, 24f);
        stripGroup = strip.gameObject.AddComponent<CanvasGroup>();
        var paper = strip.gameObject.AddComponent<InkShape>();
        paper.Shape = InkShape.Kind.RoundRect; paper.color = Comic.Cream; paper.Radius = 22f; paper.Wobble = 3.4f; paper.WobbleWave = 150f; paper.RotJitter = 0f;
        paper.SetInk(2.4f, 4.2f); paper.ShadowOffset = new Vector2(8f, -3f); paper.raycastTarget = true;   // the paper eats clicks (no world picks through it)

        // title: the logo motif + the word, lowercase
        var title = HudKit.Node(strip, "Title", new Vector2(0f, 1f), new Vector2(64f, -92f), new Vector2(36f, 36f));
        var tc = title.gameObject.AddComponent<CubeGlyph>(); tc.color = Comic.Opaque(Palette.Accent); tc.raycastTarget = false;
        var tw = HudKit.Words(strip, "TitleWords", "deacube", 36f, Comic.Ink);
        var twr = tw.rectTransform; twr.anchorMin = twr.anchorMax = new Vector2(0f, 1f); twr.pivot = new Vector2(0f, 0.5f); twr.anchoredPosition = new Vector2(92f, -90f);
        // "my songs": the world's song by its library name under the title (a small note before it); "not saved yet" while it is not in the library
        songNote = Comic.GlyphImage(strip, "SongNote", "note", Comic.A(Comic.Ink, 0.6f), 18f, false);
        var snn = songNote.rectTransform; snn.anchorMin = snn.anchorMax = new Vector2(0f, 1f); snn.pivot = new Vector2(0.5f, 0.5f); snn.anchoredPosition = new Vector2(101f, -127f);
        songName = HudKit.Words(strip, "SongName", "", 21f, Comic.A(Comic.Ink, 0.66f));
        songName.overflowMode = TextOverflowModes.Ellipsis;
        var snr = songName.rectTransform; snr.anchorMin = snr.anchorMax = new Vector2(0f, 1f); snr.pivot = new Vector2(0f, 0.5f); snr.sizeDelta = new Vector2(Width - 140f, 28f); snr.anchoredPosition = new Vector2(114f, -126f);
        songNote.gameObject.SetActive(false);

        float y = -178f;
        AddRow("Resume", "resume", ref y, false, () => Close(), true);
        AddRow("NewSong", "new song", ref y, false, () => { Close(); if (InterfaceController.I != null) InterfaceController.I.Show(); }, false);
        AddRow("Save", "save", ref y, false, () => { bool ok = SongIO.Save(); UIManager.Toast(ok ? "check" : "warning", ok ? Palette.Ok : Palette.Danger); RefreshSongName(); }, false);
        // "my songs" (the v3 name "Load" kept): the main menu with the player's songs on its shelf — only from the open strip
        AddRow("Load", "my songs", ref y, false, () => { CloseNow(); MainMenu.ShowMySongs(); }, true);
        AddRow("Learn", "learn", ref y, false, () => { Close(); Onboarding.StartTutorial(true); }, true);
        var keysRow = AddRow("Shortcuts", "shortcuts", ref y, false, () => { CloseNow(); HudShortcuts.Open(); }, true);   // v7 (U2): the ? sheet
        Hints.Register("shortcuts", keysRow.rt);
        AddRow("Settings", "settings", ref y, false, () => { settingsOpen = !settingsOpen; LayoutRows(); }, true);
        AddToggle("Fullscreen", "fullscreen", () => Screen.fullScreen, () => { Screen.fullScreen = !Screen.fullScreen; });
        AddToggle("Follow", "camera follow", () => OrbitCamera.I != null && OrbitCamera.I.followPlayhead, () => { if (OrbitCamera.I != null) OrbitCamera.I.ToggleFollow(); });
        AddToggle("Captions", "captions", () => InkCaption.Enabled, () => { InkCaption.Enabled = !InkCaption.Enabled; });
        var clear = AddRow("Clear", "clear song", ref y, false, null, true);
        clear.btn.onHold = () => { if (PathManager.I != null) PathManager.I.ClearAllPaths(true); Close(); };
        clear.btn.holdSeconds = 0.9f;
        var bar = HudKit.Node(clear.rt, "HoldBar", new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(200f, 6f));
        bar.pivot = new Vector2(0f, 0.5f);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = Comic.Danger; barImg.raycastTarget = false; barImg.type = Image.Type.Filled; barImg.fillMethod = Image.FillMethod.Horizontal; barImg.fillAmount = 0f;
        barImg.sprite = IconFactory.Get("white");
        clear.btn.holdFill = barImg;
        InkCaption.Attach(clear.btn.gameObject, "hold to clear");
        // the wavy divider (Melatonin) and the way back to the title
        var div = InkWave.Create(strip, "Divider", Comic.A(Comic.Ink, 0.55f), new Vector2(Width - 90f, 10f));
        div.Animate = false;
        var drt = div.rectTransform; drt.anchorMin = drt.anchorMax = new Vector2(0f, 1f); drt.pivot = new Vector2(0f, 0.5f);
        rows.Add(new Row { rt = drt, sub = false });
        AddRow("Home", "main menu", ref y, false, () => { Close(); MainMenu.Show(); }, false);

        LayoutRows();
        strip.gameObject.SetActive(false); veilRt.gameObject.SetActive(false);
        Hints.Register("topLeft", cluster); Hints.Register("undo", undoArt.rectTransform); Hints.Register("menu", logoBtn.transform as RectTransform);
    }

    Row AddRow(string name, string words, ref float y, bool sub, Action act, bool onlyWhenOpen)
    {
        var rt = HudKit.Node(strip, name, new Vector2(0f, 1f), new Vector2(56f, y), new Vector2(Width - 90f, 48f));
        rt.pivot = new Vector2(0f, 0.5f);
        var hit = rt.gameObject.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
        var b = rt.gameObject.AddComponent<HudButton>();
        b.bg = null; b.icon = null; b.shape = null; b.animateScale = false;
        if (act != null) b.onClick = onlyWhenOpen ? (() => { if (open) act(); }) : act;
        var t = HudKit.Words(rt, "Words", words, sub ? 24f : 30f, Comic.Ink);
        var trt = t.rectTransform; trt.anchorMin = trt.anchorMax = new Vector2(0f, 0.5f); trt.pivot = new Vector2(0f, 0.5f); trt.anchoredPosition = new Vector2(sub ? 26f : 0f, 2f);
        var wave = InkWave.Create(rt, "Underline", Comic.Ink, new Vector2(Mathf.Max(40f, t.preferredWidth + 6f), 10f));
        var wrt = wave.rectTransform; wrt.anchorMin = wrt.anchorMax = new Vector2(0f, 0.5f); wrt.pivot = new Vector2(0f, 0.5f); wrt.anchoredPosition = new Vector2(sub ? 24f : -2f, -18f);
        wave.gameObject.SetActive(false);
        var row = new Row { rt = rt, text = t, btn = b, wave = wave, sub = sub };
        rows.Add(row);
        Items.Add(b);
        b.onHover = () => { focus = rows.IndexOf(row); };
        y -= 56f;
        return row;
    }

    void AddToggle(string name, string words, Func<bool> state, Action flip)
    {
        float y = 0f;
        var row = AddRow(name, words, ref y, true, () => { flip(); Repaint(); }, true);
        row.state = state;
        var pr = HudKit.Node(row.rt, "Toggle", new Vector2(1f, 0.5f), new Vector2(-18f, 1f), new Vector2(44f, 24f));
        row.toggle = pr.gameObject.AddComponent<InkPainter>();
        row.toggle.raycastTarget = false;
        var r0 = row;
        row.toggle.onPaint = p =>
        {
            bool on = r0.state != null && r0.state();
            Rect a = p.Area; Rect pill = new Rect(a.xMin + 3f, a.center.y - 8f, a.width - 6f, 16f);
            p.RoundRect(pill, 8f, on ? Comic.Ink : Comic.Cream, 2f, Comic.Ink);
            float kx = on ? pill.xMax - 8f : pill.xMin + 8f;
            p.Disc(new Vector2(kx, pill.center.y), 5.2f, on ? Comic.Cream : Comic.Ink);
        };
    }

    void Repaint() { foreach (var r in rows) if (r.toggle != null) r.toggle.Repaint(); }

    /// <summary>The song name under the title: the world's library song, "not saved yet" while it is not one (nothing while the library is off).</summary>
    void RefreshSongName()
    {
        if (songName == null) return;
        string t = "";
        if (SongLibrary.Enabled && SongManager.I != null && SongManager.I.HasSong)
        {
            var e = SongLibrary.LiveEntry;
            t = e != null ? SongCard.Clean(e.name) : "not saved yet";
        }
        if (songName.text != t) songName.text = t;
        if (songNote != null && songNote.gameObject.activeSelf != (t.Length > 0)) songNote.gameObject.SetActive(t.Length > 0);
    }

    /// <summary>The song name shown under the title (tests).</summary>
    public string SongNameShown => songName != null ? songName.text : "";

    /// <summary>Places the rows top-down (settings' sub-rows only while it is expanded).</summary>
    void LayoutRows()
    {
        float y = -178f;
        foreach (var r in rows)
        {
            bool show = !r.sub || settingsOpen;
            if (r.rt.gameObject.activeSelf != show) r.rt.gameObject.SetActive(show);
            if (!show) continue;
            if (r.btn == null) { r.rt.anchoredPosition = new Vector2(52f, y + 20f); y -= 16f; continue; }   // the divider
            r.rt.anchoredPosition = new Vector2(56f, y);
            y -= r.sub ? 44f : 56f;
        }
        Repaint();
    }

    // ------------------------------------------------------------------ the undo / redo arrows
    static readonly List<Vector2> arc = new List<Vector2>(64);

    static void PaintArrow(InkPainter p, bool redo, bool enabled)
    {
        Vector2 c = p.Area.center + new Vector2(0f, -1.5f);
        float sx = redo ? -1f : 1f;
        arc.Clear();
        // a hand-drawn hook: starts low on the far side, climbs over the top and comes down on the near side
        InkPainter.ArcPoints(arc, c, 10.5f, 9f, -35f, 205f, 24);
        for (int i = 0; i < arc.Count; i++) { var q = arc[i] - c; arc[i] = c + new Vector2(q.x * sx, q.y) + p.Jit(i, 0.35f); }
        Vector2 end = arc[arc.Count - 1], dir = (end - arc[arc.Count - 3]).normalized;
        float a = enabled ? 1f : 0.35f;
        Color cream = Comic.A(Comic.Cream, a), ink = Comic.A(Comic.Ink, a);
        float dash = enabled ? 0f : 4.5f, gap = enabled ? 0f : 3.5f;
        // hard shadow, ink, cream
        p.Shift = new Vector2(2f, -2.5f);
        p.Stroke(arc, arc.Count, 7.6f, Comic.A(Comic.Ink, 0.85f * a), false, dash, gap, -1f, true);
        p.Head(end + dir * 3f, dir, 9f, 12f, Comic.A(Comic.Ink, 0.85f * a));
        p.Shift = Vector2.zero;
        p.InkStroke(arc, arc.Count, 3.8f, cream, 1.9f, ink, false, dash, gap);
        p.Head(end + dir * 3f, dir, 9f, 12f, cream, 1.9f, ink);
    }

    // ------------------------------------------------------------------ open / close
    public void Toggle() { if (open) Close(); else Open(); }

    public void Open()
    {
        if (open || MainMenu.IsShown || Presenter.Active) return;
        open = true; settingsOpen = false; focus = -1; openFrame = Time.frameCount;
        LayoutRows();
        strip.gameObject.SetActive(true); veilRt.gameObject.SetActive(true);
        transform.SetAsLastSibling();   // the veil covers the whole HUD
        WorldInput.Lock(LockOwner, true);
        if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.FinishPath();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.05f);
        RefreshSongName();
        Repaint();
    }

    public void Close()
    {
        if (!open) return;
        open = false; focus = -1;
        WorldInput.Unlock(LockOwner);
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 0.85f);
    }

    /// <summary>Closes at once (the main menu or the presentation takes the screen).</summary>
    public void CloseNow()
    {
        open = false; focus = -1; shown = 0f;
        WorldInput.Unlock(LockOwner);
        if (strip != null) strip.gameObject.SetActive(false);
        if (veilRt != null) veilRt.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (undoBtn == null || strip == null) return;   // not built (or a domain reload dropped the references)
        // the logo sticker's misprint widens on hover (one repaint when the hover changes, as the present sticker's)
        if (logoBtn != null && logoArt != null && logoBtn.Hover != logoHovShown) { logoHovShown = logoBtn.Hover; logoArt.Repaint(); }
        // undo / redo arrows repaint only when their state changes
        bool cu = History.CanUndo, cr = History.CanRedo;
        if (cu != lastCanUndo) { lastCanUndo = cu; undoBtn.interactable = cu; undoArt.Repaint(); }
        if (cr != lastCanRedo) { lastCanRedo = cr; redoBtn.interactable = cr; redoArt.Repaint(); }
        if (open && (MainMenu.IsShown || Presenter.Active)) CloseNow();
        if (!open && shown <= 0f) return;

        // the slide, on twos (0.2 s)
        int t = HudKit.TwosTick;
        if (t != tick)
        {
            tick = t;
            shown = Mathf.MoveTowards(shown, open ? 1f : 0f, (1f / Look.TwosFps) / 0.2f);
            float e = Ease.OutCubic(shown);
            strip.offsetMin = new Vector2(-20f - (Width + 60f) * (1f - e), strip.offsetMin.y);
            strip.offsetMax = new Vector2(Width - (Width + 60f) * (1f - e), strip.offsetMax.y);
            veilGroup.alpha = e;
            stripGroup.alpha = 1f;
            if (!open && shown <= 0f) { strip.gameObject.SetActive(false); veilRt.gameObject.SetActive(false); return; }
        }
        veilGroup.blocksRaycasts = open; stripGroup.blocksRaycasts = open; stripGroup.interactable = open;
        if (!open) return;

        // keys: ↑ ↓ Enter, Esc (the owner reads its own keys under its lock)
        if (!InputUtil.TypingInField)
        {
            if (KeyShim.Down(KeyCode.Escape) && Time.frameCount != openFrame) { Close(); FocusLoop.ClaimEsc(); return; }   // (not the Esc that opened it)
            if (KeyShim.Down(KeyCode.DownArrow)) MoveFocus(1);
            if (KeyShim.Down(KeyCode.UpArrow)) MoveFocus(-1);
            if ((KeyShim.Down(KeyCode.Return) || KeyShim.Down(KeyCode.KeypadEnter)) && focus >= 0 && focus < rows.Count && rows[focus].btn != null)
            {
                var b = rows[focus].btn;
                if (b.onClick != null) { AudioPool.UI(ProceduralAudio.Tick(), 0.3f); b.onClick(); }
            }
        }
        // the focused (hovered) item carries the wavy underline
        int hovered = -1;
        for (int i = 0; i < rows.Count; i++) if (rows[i].btn != null && rows[i].btn.Hover && rows[i].rt.gameObject.activeInHierarchy) hovered = i;
        if (hovered >= 0) focus = hovered;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; if (r.wave == null) continue;
            bool on = i == focus && r.rt.gameObject.activeInHierarchy;
            if (r.wave.gameObject.activeSelf != on) r.wave.gameObject.SetActive(on);
        }
    }

    void MoveFocus(int d)
    {
        int n = rows.Count; if (n == 0) return;
        int f = focus;
        for (int k = 0; k < n; k++)
        {
            f = f < 0 ? (d > 0 ? 0 : n - 1) : (f + d + n) % n;
            if (rows[f].btn != null && rows[f].rt.gameObject.activeInHierarchy) { focus = f; AudioPool.UI(ProceduralAudio.Tick(), 0.18f, 1.2f); return; }
        }
    }
}
