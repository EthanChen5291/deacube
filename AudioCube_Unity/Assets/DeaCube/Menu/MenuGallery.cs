using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SPEC v5 §4 (package T) — the title menu's gallery shelf. Choosing "gallery" swaps the word list for ink cards, one per <see cref="Gallery.Entries"/>
/// song, dealt in one by one on twos. 2026-10-01 (the user: "the song gallery should fill up the whole page — DEACUBE should disappear and it should
/// just be the background and projects"): the title sinks into the wall while the shelf is open (MenuStage.SetTitleDown, driven by MainMenu) and
/// the cards fill the page (<see cref="ShelfGrid"/>: a centred grid of balanced rows, 6 → 3 + 3, "back" at the top-left); ← → move along a row,
/// ↑ ↓ between rows, "back" before the first card. A card is one family with the deck's vibe cards (package U): the
/// song's vibe colour as the paper (IslandTray.PaperOf), its vibe glyph big in a lighter window (VibeGlyphs.Build, in the glyph's own
/// colours), a tiny map of the song under it (its columns and lanes as little cubes in their chords' colours, a Moon as a disc), the
/// lowercase title, its tempo digits on a cream sticker; the focused card straightens, lifts, boils and shows "after …" in small ink with a
/// wavy underline under its title. Hover or focus a card (the arrows on the grid; the "back" word comes before the first card): after a short dwell its hook
/// plays (<see cref="MenuPreview"/>: the song's own notes, mix and tempo; the menu music held meanwhile and back a moment after), the wall
/// behind lights up in the song's vibe colour and hops on the preview's beat, the card's glyph bounces on it; leaving the card stops the
/// preview at once. Click / Enter opens the song (<see cref="MainMenu.OpenFromGallery"/>: Gallery.Open, the comic leave "HIT IT!", playing
/// from bar 0); "back" / Esc returns to the words. Own overlay canvas just above the menu's (<see cref="SortOrder"/>), faded with the menu UI.
/// </summary>
public class MenuGallery : MonoBehaviour
{
    public const int SortOrder = MainMenu.SortOrder + 1;
    /// <summary>A card (1080p reference px; the page grid, <see cref="ShelfGrid"/>, scales it down to fit smaller screens) and the old row's gap.</summary>
    public const float CardW = 204f, CardH = 268f, Gap = 30f;
    /// <summary>Seconds a card holds the focus before its preview starts; seconds of silence before the menu music comes back.</summary>
    public const float Dwell = 0.22f, HoldRelease = 0.45f;
    /// <summary>How far the wall's light and hops spread behind a card (MenuStage.HoverSpread; a word is 1).</summary>
    public const float CardSpread = 2.4f;

    MainMenu menu; MenuMusic music;
    Canvas canvas; CanvasGroup group; RectTransform root, shelf, backRoot;
    MenuButton back;
    readonly List<GalleryCard> cards = new List<GalleryCard>();
    readonly MenuPreview preview = new MenuPreview();
    readonly Dictionary<string, SongState> states = new Dictionary<string, SongState>();
    readonly Dictionary<string, MenuPreview.Track> tracks = new Dictionary<string, MenuPreview.Track>();
    bool open, dirty = true, focusFromPointer;
    Vector3 restMouse; bool pointerLive = true;   // a cursor resting where the shelf (or the keys) put a card focuses nothing until it moves
    float shown, openT, pointerLeftAt = -1f, wantSince, holdReleaseAt = -1f;
    int focus = -1, dealt;
    GalleryCard previewCard;
    Vector4 laidOut = new Vector4(-1f, -1f, -1f, -1f);

    // ------------------------------------------------------------------ read-backs (MainMenu, tests)
    public bool IsOpen => open;
    /// <summary>0 = closed … 1 = open (the words fade the other way).</summary>
    public float Shown => shown;
    public Canvas Canvas => canvas;
    public IReadOnlyList<GalleryCard> Cards => cards;
    public MenuButton BackButton => back;
    public MenuPreview Preview => preview;
    /// <summary>Index of the focused card (-1: none, or "back").</summary>
    public int FocusIndex => focus >= 1 && focus <= cards.Count ? focus - 1 : -1;
    public bool BackFocused => focus == 0;
    public GalleryCard FocusedCard => focus >= 1 && focus <= cards.Count ? cards[focus - 1] : null;
    /// <summary>The card whose preview plays (or is about to, after the dwell).</summary>
    public GalleryCard PreviewCard => previewCard;
    public string PreviewId => preview.Playing ? preview.Id : "";
    /// <summary>The wall's light behind the focused item: the focused card's vibe colour (null for "back" / nothing).</summary>
    public Color? Tint { get { var c = FocusedCard; return c != null ? c.VibeColor : (Color?)null; } }
    public float HoverSpread => FocusedCard != null ? CardSpread : 1f;
    public Vector2 HoverScreen { get { var c = FocusedCard; if (c != null) return c.ScreenCenter; return back != null && focus == 0 ? back.ScreenCenter : Vector2.zero; } }
    public float HoverAmount { get { var c = FocusedCard; if (c != null) return c.FocusAmount; return back != null && focus == 0 ? back.HoverAmount : 0f; } }
    /// <summary>The cards' scale in the row (1 = <see cref="CardW"/> × <see cref="CardH"/> at 1080p).</summary>
    public float CardScale { get; private set; } = 1f;
    public int Opens { get; private set; }
    public int Closes { get; private set; }
    public int PreviewsStarted { get; private set; }
    public int Deals => dealt;

    public static MenuGallery Create(Transform parent, MainMenu menu, MenuMusic music)
    {
        var go = new GameObject("MenuGallery", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var g = go.AddComponent<MenuGallery>();
        g.menu = menu; g.music = music;
        g.Build();
        return g;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        root = (RectTransform)transform;

        shelf = UIKit.RT(UIKit.Obj("Shelf", root));
        UIKit.Stretch(shelf);

        // "back": an ink arrow and the lowercase word, at the page's top-left (Layout)
        backRoot = UIKit.RT(UIKit.Obj("Back", root));
        backRoot.anchorMin = backRoot.anchorMax = new Vector2(0.5f, 0.5f); backRoot.pivot = new Vector2(0f, 1f);
        backRoot.sizeDelta = new Vector2(220f, 52f);
        var arrow = InkPainter.Create(backRoot, "Arrow", new Vector2(34f, 34f), PaintArrow);
        var art = arrow.rectTransform; art.anchorMin = art.anchorMax = art.pivot = new Vector2(0f, 1f); art.anchoredPosition = new Vector2(0f, -12f);
        back = MenuButton.CreateText(backRoot, "BackWord", "back", 30f, 30f, false);
        var brt = UIKit.RT(back); brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f); brt.anchoredPosition = new Vector2(40f, 0f);
        back.onHover = () => { focusFromPointer = true; pointerLeftAt = -1f; SetFocus(0, true); };
        back.onPress = () => { if (MainMenu.Stage != null) MainMenu.Stage.Press(back.PressPoint, new Color(0.55f, 0.6f, 1f)); };
        back.onClick = () => Close(true);

        canvas.gameObject.SetActive(false);
    }

    /// <summary>The back arrow: a cream ink stroke with its head, printed over a violet offset (the words' print shadow).</summary>
    static void PaintArrow(InkPainter p)
    {
        Rect r = p.Area;
        Vector2 a = new Vector2(r.xMax - 4f, r.center.y), b = new Vector2(r.xMin + 8f, r.center.y);
        p.Shift = new Vector2(2.5f, -3f);
        p.Line(a, b, 4.2f, Comic.PrintShadow, true);
        p.Head(b - new Vector2(1f, 0f), Vector2.left, 12f, 16f, Comic.PrintShadow);
        p.Shift = Vector2.zero;
        p.Line(a, b, 4.2f, Comic.Cream, true);
        p.Head(b - new Vector2(1f, 0f), Vector2.left, 12f, 16f, Comic.Cream);
    }

    // ------------------------------------------------------------------ the cards
    /// <summary>The gallery changed (Gallery.OnEntriesChanged): the cards are rebuilt the next time the shelf opens (at once when it is open).</summary>
    public void MarkDirty()
    {
        dirty = true;
        states.Clear(); tracks.Clear();
        if (open) { StopPreview(true); Rebuild(); if (cards.Count == 0) Close(false); else { Deal(); SetFocus(1, false); } }
    }

    void Rebuild()
    {
        dirty = false;
        StopPreview(true);
        focus = -1; previewCard = null;
        foreach (var c in cards) if (c != null) Destroy(c.gameObject);
        cards.Clear();
        var list = Gallery.Entries;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            cards.Add(GalleryCard.Create(shelf, this, e, i, StateOf(e)));
        }
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
    }

    /// <summary>The parsed song of <paramref name="e"/> (cached; null when it cannot be read).</summary>
    public SongState StateOf(GalleryEntry e)
    {
        if (e == null) return null;
        SongState st;
        if (states.TryGetValue(e.id, out st)) return st;
        st = Gallery.LoadState(e.id);
        states[e.id] = st;
        return st;
    }

    /// <summary>The preview of <paramref name="e"/> (built once from its song; null when it cannot be read).</summary>
    public MenuPreview.Track TrackOf(GalleryEntry e)
    {
        if (e == null) return null;
        MenuPreview.Track t;
        if (tracks.TryGetValue(e.id, out t)) return t;
        var st = StateOf(e);
        t = st != null ? MenuPreview.Build(st, Mathf.Max(0, e.hook)) : null;
        if (t != null) t.id = e.id;
        tracks[e.id] = t;
        return t;
    }

    /// <summary>The chord the preview of <paramref name="e"/> starts on (the leave's impact plays it; null = the menu's).</summary>
    public int[] ChordOf(GalleryEntry e) { var t = TrackOf(e); return t != null ? t.chord : null; }

    public GalleryCard CardOf(string id) { foreach (var c in cards) if (c.Entry != null && c.Entry.id == id) return c; return null; }

    // ------------------------------------------------------------------ open / close
    /// <summary>Swaps the words for the shelf: the cards deal in, the song opened last (or the first) takes the focus.</summary>
    public void Open()
    {
        if (open) return;
        if (dirty || cards.Count != Gallery.Entries.Count) Rebuild();
        if (cards.Count == 0) return;
        open = true; Opens++;
        RestPointer();
        canvas.gameObject.SetActive(true);
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
        Layout();
        Deal();
        int start = 0; string cur = Gallery.CurrentId;
        for (int i = 0; i < cards.Count; i++) if (!string.IsNullOrEmpty(cur) && cards[i].Entry.id == cur) start = i;
        focusFromPointer = false; pointerLeftAt = -1f;
        focus = -1;
        SetFocus(start + 1, false);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.22f, 1.25f);
    }

    void Deal()
    {
        openT = 0f;
        for (int i = 0; i < cards.Count; i++) cards[i].BeginDeal(1 + i);
    }

    /// <summary>Back to the words ("back", Esc): the preview stops, the menu music returns a moment later.</summary>
    public void Close(bool sound = true)
    {
        if (!open) return;
        open = false; Closes++;
        StopPreview(false);
        SetFocus(-1, false);
        if (sound) AudioPool.UI(ProceduralAudio.Whoosh(), 0.2f, 0.85f);
        if (menu != null) menu.OnShelfClosed();
    }

    /// <summary>Closed and out of sight at once (the menu re-enters, hides or leaves): no preview, the menu music released, the cards reset.</summary>
    public void CloseImmediate()
    {
        open = false; shown = 0f;
        StopPreview(true);
        focus = -1; focusFromPointer = false; pointerLeftAt = -1f;
        foreach (var c in cards) if (c != null) { c.SetFocused(false); c.ResetLook(); }
        if (back != null) { back.SetFocused(false); back.Unsquash(); }
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    /// <summary>The leave took over the screen (the held frame shows the shelf from now on).</summary>
    public void HideCanvas() { if (canvas != null) canvas.gameObject.SetActive(false); }

    /// <summary>A leave began: the shelf keeps its look for the held frame but takes no more input.</summary>
    public void BlockInput() { if (group != null) { group.interactable = false; group.blocksRaycasts = false; } }

    /// <summary>Stops the preview at once; the menu music comes back now (<paramref name="releaseNow"/>) or after <see cref="HoldRelease"/> s.</summary>
    public void StopPreview(bool releaseNow)
    {
        if (preview.Playing) preview.Stop();
        previewCard = null; wantSince = Time.unscaledTime;
        if (music == null) return;
        if (releaseNow) { if (music.Held) music.Hold(false); holdReleaseAt = -1f; }
        else if (music.Held) holdReleaseAt = Time.unscaledTime + HoldRelease;
    }

    // ------------------------------------------------------------------ focus and keys
    void SetFocus(int f, bool sound)
    {
        f = Mathf.Clamp(f, -1, cards.Count);
        if (f == focus) return;
        focus = f;
        if (back != null) back.SetFocused(f == 0);
        for (int i = 0; i < cards.Count; i++) cards[i].SetFocused(f == i + 1);
        if (sound && f >= 0) AudioPool.UI(ProceduralAudio.Tick(), 0.14f, f == 0 ? 0.9f : 1.05f + 0.04f * f);
    }

    /// <summary>The arrows on the grid: ← → along a row (← from the top row's first card: "back"), ↑ ↓ between rows (↑ from the top row: "back");
    /// from "back" (or nothing), → and ↓ go to the first card. No wrap.</summary>
    public void Move(int dx, int dy)
    {
        if (!open) return;
        int n = cards.Count;
        if (n == 0) return;
        focusFromPointer = false; pointerLeftAt = -1f;
        RestPointer();
        if (focus <= 0) { if (dx > 0 || dy > 0) SetFocus(1, true); return; }
        int g = focus - 1, cols = ShelfGrid.ColsFor(n, false), rows = (n + cols - 1) / cols, row = g / cols, col = g % cols;
        int t = -2;   // -2 stay, -1 "back"
        if (dx > 0) { if (col < cols - 1 && g + 1 < n) t = g + 1; }
        else if (dx < 0) { if (col > 0) t = g - 1; else if (row == 0) t = -1; }
        else if (dy > 0) { if (row + 1 < rows) t = Mathf.Min((row + 1) * cols + col, n - 1); }
        else if (dy < 0) t = row > 0 ? g - cols : -1;
        if (t == -1) SetFocus(0, true);
        else if (t >= 0) SetFocus(t + 1, true);
    }

    /// <summary>One step along "back", card 1 … card N in order (MainMenu.MoveFocus; no wrap).</summary>
    public void Step(int dir)
    {
        if (!open) return;
        focusFromPointer = false; pointerLeftAt = -1f;
        RestPointer();
        int f = focus < 0 ? (dir > 0 ? 1 : cards.Count) : Mathf.Clamp(focus + (dir > 0 ? 1 : -1), 0, cards.Count);
        SetFocus(f, true);
    }

    /// <summary>The pointer takes no focus until it moves (the shelf opened or the keys moved under a resting cursor: a card that appears under it is
    /// not the player's choice). Real pointer events only; simulated ones (tests) always count.</summary>
    public bool PointerLive => pointerLive;
    void RestPointer() { restMouse = Input.mousePosition; pointerLive = false; }

    /// <summary>Focuses card <paramref name="i"/> (tests, keys).</summary>
    public void FocusCard(int i) { if (!open || i < 0 || i >= cards.Count) return; focusFromPointer = false; SetFocus(i + 1, true); }
    public void FocusBack() { if (!open) return; focusFromPointer = false; SetFocus(0, true); }

    /// <summary>The shelf's keys (MainMenu calls it while the shelf is open): the arrows move on the grid (<see cref="Move"/>), Enter opens (or goes
    /// back), Esc / Backspace back.</summary>
    public void HandleKeys()
    {
        if (!open) return;
        if (KeyShim.Down(KeyCode.Escape) || KeyShim.Down(KeyCode.Backspace)) { Close(true); return; }
        if (KeyShim.Down(KeyCode.RightArrow)) Move(1, 0);
        if (KeyShim.Down(KeyCode.LeftArrow)) Move(-1, 0);
        if (KeyShim.Down(KeyCode.DownArrow)) Move(0, 1);
        if (KeyShim.Down(KeyCode.UpArrow)) Move(0, -1);
        if (KeyShim.Down(KeyCode.Return) || KeyShim.Down(KeyCode.KeypadEnter))
        {
            if (focus == 0) { back.Invoke(); return; }
            var c = FocusedCard;
            if (c == null) { Step(1); return; }
            Activate(c, c.ScreenCenter);
        }
    }

    // ------------------------------------------------------------------ card events (GalleryCard)
    internal void CardEntered(GalleryCard c)
    {
        if (!open) return;
        focusFromPointer = true; pointerLeftAt = -1f;
        SetFocus(c.Index + 1, true);
    }

    internal void CardExited(GalleryCard c)
    {
        if (open && focusFromPointer && focus == c.Index + 1) pointerLeftAt = Time.unscaledTime;
    }

    internal void CardPressed(GalleryCard c, Vector2 at)
    {
        if (MainMenu.Stage != null) MainMenu.Stage.Press(at, Color.Lerp(c.VibeColor, Color.white, 0.15f));
    }

    internal void Activate(GalleryCard c, Vector2 at)
    {
        if (!open || c == null || menu == null) return;
        menu.OpenFromGallery(c.Entry, at);
    }

    /// <summary>The leave's anticipation on the chosen card (it stays squashed in the held frame).</summary>
    public void SquashCard(string id) { var c = CardOf(id); if (c != null) c.Squash(); }

    /// <summary>The song could not be opened: the card shakes (and stays silent from now on).</summary>
    public void Refuse(GalleryEntry e) { var c = e != null ? CardOf(e.id) : null; if (c != null) { c.Shake(); c.Playable = false; } }

    /// <summary>Tests: the pointer enters / leaves card <paramref name="i"/>.</summary>
    public void SimulateEnter(int i) { if (i >= 0 && i < cards.Count) cards[i].OnPointerEnter(null); }
    public void SimulateExit(int i) { if (i >= 0 && i < cards.Count) cards[i].OnPointerExit(null); }

    // ------------------------------------------------------------------ per frame (MainMenu, while the menu is up and not leaving)
    public void Tick(float dt, float uiAlpha, bool live)
    {
        float now = Time.unscaledTime;
        if (open) openT += dt;
        if (open && !pointerLive && (Input.mousePosition - restMouse).sqrMagnitude > 16f) { pointerLive = true; foreach (var c in cards) if (c != null) c.WakePointer(); }
        shown = Mathf.MoveTowards(shown, open ? 1f : 0f, dt / 0.16f);
        float stepped = Look.OnTwos ? Mathf.Round(shown * 3f) / 3f : shown;
        if (canvas != null)
        {
            bool on = open || shown > 0.001f;
            if (canvas.gameObject.activeSelf != on) canvas.gameObject.SetActive(on);
            group.alpha = uiAlpha * stepped;
            bool input = live && open && shown > 0.6f;
            group.interactable = input; group.blocksRaycasts = input;
        }
        if (!open && shown <= 0f) { TickMusic(now); preview.Tick(); return; }
        Layout();
        // the deal: a card lands on its drawing with a soft paper tick
        int d = Mathf.FloorToInt(openT * Look.TwosFps + 1e-4f);
        foreach (var c in cards) if (c.DealStep(d)) { dealt++; AudioPool.UI(ProceduralAudio.Tick(), 0.16f, 0.85f + 0.06f * c.Index); }
        // the pointer left the card it focused (and entered none): the focus goes, and with it the preview
        if (pointerLeftAt > 0f && now - pointerLeftAt > 0.2f)
        {
            pointerLeftAt = -1f;
            bool any = false; foreach (var c in cards) if (c.Hover) { any = true; break; }
            if (!any && focusFromPointer && focus >= 1) SetFocus(-1, false);
        }
        // the preview follows the focused card (after a short dwell); leaving it stops it at once
        var want = open && live && shown >= 0.99f ? FocusedCard : null;
        if (want != previewCard)
        {
            if (preview.Playing) preview.Stop();
            if (music != null && music.Held) holdReleaseAt = now + HoldRelease;
            previewCard = want; wantSince = now;
        }
        if (previewCard != null && !preview.Playing && previewCard.Playable && now - wantSince >= Dwell)
        {
            var tr = TrackOf(previewCard.Entry);
            if (tr != null && tr.notes.Count > 0 && Synth.Ready)
            {
                if (music != null) music.Hold(true);
                holdReleaseAt = -1f;
                if (preview.Play(tr)) PreviewsStarted++; else previewCard.Playable = false;
            }
            else previewCard.Playable = false;   // nothing to hear: no retry every frame
        }
        TickMusic(now);
        preview.Tick();
        // the playing card's glyph bounces on the preview's beat
        double beat = preview.Beat;
        foreach (var c in cards) c.Beat = c == previewCard && preview.Playing ? beat : double.NaN;
    }

    void TickMusic(float now)
    {
        if (music == null || preview.Playing || !music.Held) return;
        if (holdReleaseAt < 0f) holdReleaseAt = now + HoldRelease;
        if (now >= holdReleaseAt) { music.Hold(false); holdReleaseAt = -1f; }
    }

    readonly List<Vector2> centers = new List<Vector2>(ShelfGrid.PerPage);

    /// <summary>The page (the title sunk into the wall meanwhile): "back" at the top-left; the cards in a centred grid of balanced rows below it
    /// (<see cref="ShelfGrid"/>: their 1080p size, scaled down to fit).</summary>
    void Layout()
    {
        if (root == null) return;
        var key = new Vector4(Screen.width, Screen.height, cards.Count, 1000f * root.lossyScale.x);
        if (key == laidOut) return;
        laidOut = key;
        int n = cards.Count;
        Vector2 tl, tr, pv, nx, pg;
        float k = ShelfGrid.Layout(root.rect, n, ShelfGrid.ColsFor(n, false), CardW, CardH, false, centers, out tl, out tr, out pv, out nx, out pg);
        backRoot.anchoredPosition = tl;
        if (n == 0) return;
        CardScale = k;
        for (int i = 0; i < n; i++)
        {
            var rt = cards[i].Root;
            rt.localScale = new Vector3(k, k, 1f);
            rt.anchoredPosition = centers[i];
        }
    }

    Vector2 Local(Vector2 screen)
    {
        Vector2 l;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out l);
        return l;
    }

    void OnDestroy() { preview.Stop(); }
}

/// <summary>
/// One gallery card (<see cref="MenuGallery"/>): the root keeps its slot in the row and takes the pointer; the body inside moves on twos — a
/// seeded tilt at rest, straight, lifted and a little bigger while focused, dipped while pressed, squashed as the leave's anticipation,
/// shaken when the song cannot be opened; it pops in on its deal drawing. The glyph hops on the preview's beat while this card plays.
/// </summary>
public class GalleryCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public GalleryEntry Entry { get; private set; }
    public int Index { get; private set; }
    public RectTransform Root { get; private set; }
    public RectTransform body, glyphRoot;
    public InkShape paper, sticker;
    /// <summary>The vibe glyph's silhouette image (VibeGlyphs.Build's "Glyph"; its accent layers sit on top).</summary>
    public Image glyph;
    public InkPainter window;
    public InkPainter map;
    public TextMeshProUGUI title, tempo, after;
    public InkWave wave;
    public Color VibeColor { get; private set; }
    public VibeKind Vibe { get; private set; }
    /// <summary>False once the song turned out to have nothing to play or could not be opened (no preview retries).</summary>
    public bool Playable = true;
    /// <summary>The playing preview's beat (NaN when this card is silent).</summary>
    [NonSerialized] public double Beat = double.NaN;

    MenuGallery owner;
    bool hover, down, focused, squashed; float h, shakeT = 9f, tilt, ulDrawn, pressT = -9f; int tick = int.MinValue, dealAt = -1, dealStep = 99;
    Vector2 pressAt;
    static readonly float[] UnderlineSteps = { 0f, 0.45f, 0.8f, 1f };
    static readonly float[] DealScale = { 1.16f, 0.95f, 1f }, DealY = { -30f, 5f, 0f };
    static readonly Vector3 SquashScale = new Vector3(1.18f, 0.78f, 1f);

    public bool Hover => hover;
    public bool Focused => focused;
    public bool Squashed => squashed;
    /// <summary>0 … 1: how focused the card looks (smoothed; the wall's light follows it).</summary>
    public float FocusAmount => h;
    public bool AfterShown => after != null && after.gameObject.activeInHierarchy;
    public bool Dealt => dealStep >= 0 && dealStep < 99 && body != null && body.gameObject.activeSelf;
    public float UnderlineDrawn => wave != null && wave.gameObject.activeSelf ? UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)] : 0f;
    public Vector2 ScreenCenter => RectTransformUtility.WorldToScreenPoint(null, (body != null ? body : Root).TransformPoint((body != null ? body : Root).rect.center));
    /// <summary>The card's rect on screen (its slot, px).</summary>
    public Rect ScreenRect
    {
        get
        {
            var c = new Vector3[4]; Root.GetWorldCorners(c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, c[0]), b = RectTransformUtility.WorldToScreenPoint(null, c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }

    // ------------------------------------------------------------------ building
    public static GalleryCard Create(Transform parent, MenuGallery owner, GalleryEntry e, int index, SongState st)
    {
        var go = UIKit.Obj("Card_" + e.id, parent);
        var rt = UIKit.RT(go);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(MenuGallery.CardW, MenuGallery.CardH);
        var hit = go.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true;
        var c = go.AddComponent<GalleryCard>();
        c.owner = owner; c.Entry = e; c.Index = index; c.Root = rt;
        c.Vibe = (VibeKind)Mathf.Clamp(e.vibe, 0, global::Vibe.Count - 1);
        c.VibeColor = global::Vibe.ColorOf(c.Vibe);
        unchecked { int hs = 17; foreach (char ch in e.id) hs = hs * 31 + ch; c.tilt = ((hs & 0xFFFF) / 65535f * 2f - 1f) * 2.8f; }
        c.BuildFace(st);
        c.ResetLook();
        return c;
    }

    void BuildFace(SongState st)
    {
        float W = MenuGallery.CardW, H = MenuGallery.CardH;
        var bgo = UIKit.Obj("Body", transform);
        body = UIKit.RT(bgo);
        UIKit.Stretch(body);
        body.pivot = new Vector2(0.5f, 0.5f);
        Color ink = Comic.Ink, vibe = Comic.Opaque(VibeColor);

        // the paper: the vibe colour (the deck cards' paper), inked, a hard print-violet shadow (visible on the dark wall)
        paper = InkShape.Create(body, "Paper", InkShape.Kind.RoundRect, IslandTray.PaperOf(vibe), new Vector2(W, H));
        paper.raycastTarget = false; paper.Ink = ink; paper.SetInk(2.4f, 4.2f); paper.Radius = 16f; paper.Wobble = 1.5f; paper.RotJitter = 0f;
        paper.ShadowColor = Comic.PrintShadow; paper.ShadowOffset = new Vector2(6f, -8f); paper.Seed = 101 + Index * 7;

        // a lighter window (the sky the glyph sits in), the vibe glyph in its own colours, the song's map under it
        Color sky = Color.Lerp(IslandTray.PaperOf(vibe), Color.white, 0.38f);
        float glyphY = H * 0.5f - 72f;
        window = InkPainter.Create(body, "Window", new Vector2(W, H), p => p.Disc(p.Area.center + new Vector2(0f, glyphY), W * 0.33f, W * 0.33f, sky, 40));
        UIKit.Stretch(window.rectTransform);
        glyphRoot = VibeGlyphs.Build(body, "Vibe", Vibe, 74f);
        glyphRoot.anchoredPosition = new Vector2(0f, glyphY);
        var gt = glyphRoot.Find("Glyph");
        glyph = gt != null ? gt.GetComponent<Image>() : null;
        if (st != null)
        {
            var s = st;
            map = InkPainter.Create(body, "Map", new Vector2(W - 36f, 32f), p => PaintMap(p, s));
            map.rectTransform.anchoredPosition = new Vector2(0f, -22f);
        }

        // the tempo digits on a cream sticker slapped on the top-right corner
        sticker = InkShape.Create(body, "Tempo", InkShape.Kind.Sticker, Comic.Cream, new Vector2(64f, 38f));
        sticker.raycastTarget = false; sticker.Ink = ink; sticker.SetInk(1.8f, 2.8f); sticker.ShadowColor = Comic.A(ink, 0.9f); sticker.ShadowOffset = new Vector2(3f, -4f);
        sticker.Seed = 307 + Index * 17;
        var srt = sticker.rectTransform; srt.anchoredPosition = new Vector2(W * 0.5f - 18f, H * 0.5f - 10f); srt.localEulerAngles = new Vector3(0f, 0f, -9f);
        float bpm = Entry.bpm > 0f ? Entry.bpm : (st != null ? st.bpm : 0f);
        tempo = UIKit.Text(srt, "Digits", bpm > 0f ? Mathf.RoundToInt(bpm).ToString() : "", 27f, ink, TextAlignmentOptions.Center);
        tempo.font = Comic.DigitFont; tempo.fontSharedMaterial = Comic.DigitFont.material; tempo.characterSpacing = 2f;
        UIKit.RT(tempo).sizeDelta = new Vector2(64f, 38f); UIKit.RT(tempo).anchoredPosition = new Vector2(0f, 1f);

        // the title (lowercase), its wavy underline, "after …" (small ink, focused only)
        title = UIKit.Text(body, "Title", Clean((Entry.title ?? Entry.id).ToLowerInvariant()), 31f, ink, TextAlignmentOptions.Center);
        title.enableAutoSizing = true; title.fontSizeMin = 18f; title.fontSizeMax = 31f;
        var trt = UIKit.RT(title); trt.sizeDelta = new Vector2(W - 22f, 38f); trt.anchoredPosition = new Vector2(0f, -H * 0.5f + 72f);
        wave = InkWave.Create(body, "Underline", ink, new Vector2(10f, 12f));
        wave.Amplitude = 2.2f; wave.Wavelength = 12f; wave.Thickness = 2.6f;
        var wrt = wave.rectTransform; wrt.anchoredPosition = new Vector2(0f, -H * 0.5f + 50f);
        wave.gameObject.SetActive(false);
        string a = string.IsNullOrEmpty(Entry.after) ? "" : "after " + Clean(Entry.after.ToLowerInvariant());
        after = UIKit.Text(body, "After", a, 16f, Comic.A(ink, 0.82f), TextAlignmentOptions.Center);
        after.enableWordWrapping = true; after.overflowMode = TextOverflowModes.Ellipsis; after.maxVisibleLines = 2; after.lineSpacing = -8f;
        var art = UIKit.RT(after); art.sizeDelta = new Vector2(W - 26f, 40f); art.anchoredPosition = new Vector2(0f, -H * 0.5f + 25f);
        after.gameObject.SetActive(false);
    }

    /// <summary>Characters the UI font cannot draw are left out (a missing glyph would print a box).</summary>
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var f = Comic.Font;
        if (f == null) return s;
        uint[] missing;
        if (f.HasCharacters(s, out missing, true, true) || missing == null || missing.Length == 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s) if (Array.IndexOf(missing, (uint)ch) < 0) sb.Append(ch);
        return sb.ToString().Trim();
    }

    /// <summary>The song's shape: each column a stack of little inked cubes (its lanes, back to front) in the chords' colours, a Moon as a
    /// cream disc before the first column. A long song wraps into two or three rows (then each column shows its anchor, a second lane as a
    /// smaller cube behind it), so a cube never shrinks below ~11 px.</summary>
    static void PaintMap(InkPainter p, SongState st)
    {
        if (st == null || st.measures == null || st.measures.Length == 0) return;
        Rect r = p.Area;
        var ms = st.measures;
        int n = ms.Length;
        for (int i = 0; i < n; i++) if (ms[i] == null) return;
        var col = MenuPreview.ColumnsOf(ms);
        int C = 0; foreach (int c in col) C = Mathf.Max(C, c + 1);
        var laneOf = new int[n]; var lanes = new int[C]; int maxLanes = 1;
        for (int i = 0; i < n; i++) { laneOf[i] = lanes[col[i]]; lanes[col[i]]++; maxLanes = Mathf.Max(maxLanes, lanes[col[i]]); }
        bool moon = st.moons != null && st.moons.Length > 0;
        int slots = C + (moon ? 1 : 0);
        int rowsN = slots <= 12 ? 1 : (slots <= 26 ? 2 : 3);
        int perRow = Mathf.CeilToInt(slots / (float)rowsN);
        bool stacked = rowsN == 1;
        int laneRows = stacked ? maxLanes : 1;
        float rowH = r.height / rowsN;
        float s = Mathf.Clamp(Mathf.Min((r.width - 6f) / (perRow * 0.95f), (rowH - 2f) / (1f + 0.5f * (laneRows - 1))), 5f, 17f);
        float dx = s * 0.95f, dy = s * 0.5f, inkW = Mathf.Clamp(s * 0.11f, 0.8f, 1.5f);
        float x0 = r.center.x - (perRow - 1) * dx * 0.5f;
        float yTop = r.center.y + (rowsN - 1) * rowH * 0.5f - (laneRows - 1) * dy * 0.5f;
        if (moon)
        {
            Vector2 mc = new Vector2(x0, yTop + (laneRows - 1) * dy * 0.5f);
            p.Disc(mc, s * 0.44f + 1.3f, Comic.Ink);
            p.Disc(mc, s * 0.44f, Comic.Cream);
        }
        // back lanes first so the front ones overlap them
        for (int lane = maxLanes - 1; lane >= 0; lane--)
            for (int i = 0; i < n; i++)
            {
                int c = col[i], l = laneOf[i];
                if (l != lane || (!stacked && l > 1)) continue;
                int k = c + (moon ? 1 : 0), row = k / perRow, pos = k % perRow;
                var m = ms[i];
                var semis = m.semis != null && m.semis.Length > 0 ? m.semis : new[] { 0, 4, 7 };
                Color cc = Comic.Opaque(MusicTheory.ChordColor(m.root, semis));
                Vector2 at = new Vector2(x0 + pos * dx, yTop - row * rowH);
                if (stacked) { at += new Vector2(l * dy * 0.35f, l * dy); p.Cube(at, s, cc, Comic.Ink, inkW); }
                else if (l == 1) p.Cube(at + new Vector2(s * 0.3f, s * 0.32f), s * 0.8f, cc, Comic.Ink, inkW * 0.8f);
                else p.Cube(at, s, cc, Comic.Ink, inkW);
            }
    }

    // ------------------------------------------------------------------ control (MenuGallery)
    public void SetFocused(bool f)
    {
        if (focused == f) return;
        focused = f;
        if (!f) ulDrawn = 0f;
        tick = int.MinValue;
    }

    /// <summary>Hidden until deal drawing <paramref name="drawing"/> (1/12 s steps from the shelf opening).</summary>
    public void BeginDeal(int drawing)
    {
        dealAt = drawing; dealStep = -1;
        squashed = false; shakeT = 9f; down = false;
        if (body != null) body.gameObject.SetActive(false);
        tick = int.MinValue;
    }

    /// <summary>Advances the deal to drawing <paramref name="d"/>; true on the drawing the card lands.</summary>
    public bool DealStep(int d)
    {
        if (dealAt < 0) return false;
        int k = d - dealAt;
        if (k < 0) return false;
        bool landed = dealStep < 0;
        dealStep = Mathf.Min(k, 2);
        if (landed && body != null) body.gameObject.SetActive(true);
        if (dealStep >= 2) dealAt = -1;
        tick = int.MinValue;
        return landed;
    }

    public void ResetLook()
    {
        squashed = false; shakeT = 9f; down = false; hover = false; h = 0f; ulDrawn = 0f; Beat = double.NaN;
        dealAt = -1; dealStep = 99;
        if (body != null) body.gameObject.SetActive(true);
        tick = int.MinValue;
    }

    public void Squash() { squashed = true; if (body != null) body.localScale = SquashScale; }
    public void Shake() { shakeT = 0f; }

    // ------------------------------------------------------------------ pointer
    public void OnPointerEnter(PointerEventData e)
    {
        if (e != null && owner != null && !owner.PointerLive) { pendingReal = true; return; }   // a resting cursor: not until it moves
        hover = true; if (owner != null) owner.CardEntered(this);
    }
    public void OnPointerExit(PointerEventData e) { pendingReal = false; hover = false; down = false; if (owner != null) owner.CardExited(this); }
    /// <summary>The pointer started moving: the card it rested on since the shelf opened (or the keys moved) takes the hover now.</summary>
    internal void WakePointer() { if (!pendingReal) return; pendingReal = false; hover = true; if (owner != null) owner.CardEntered(this); }
    bool pendingReal;
    public void OnPointerDown(PointerEventData e)
    {
        if (e != null && e.button != PointerEventData.InputButton.Left) return;
        down = true; pressAt = e != null ? e.position : ScreenCenter; pressT = Time.unscaledTime;
        if (owner != null) owner.CardPressed(this, pressAt);
    }
    public void OnPointerUp(PointerEventData e) { down = false; }
    public void OnPointerClick(PointerEventData e)
    {
        if (e != null && e.button != PointerEventData.InputButton.Left) return;
        Vector2 at = e != null ? e.position : ScreenCenter;
        if (owner != null) owner.Activate(this, at);
    }

    void OnDisable() { hover = false; down = false; }

    // ------------------------------------------------------------------ drawing (on twos)
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        h = Mathf.Lerp(h, focused ? 1f : 0f, 1f - Mathf.Exp(-dt * 14f));
        float shake = 0f;
        if (shakeT < 0.5f) { shakeT += dt; shake = Mathf.Sin(shakeT * 55f) * 10f * (1f - shakeT / 0.5f); }
        bool bouncing = !double.IsNaN(Beat);
        if (Look.OnTwos && shakeT >= 0.5f) { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t == tick) return; tick = t; }
        if (body == null) return;
        bool on = focused;
        if (on && ulDrawn < 3f) ulDrawn += 1f;
        // the underline draws itself; "after …" shows from the drawing after the focus
        bool ul = on && ulDrawn > 0f;
        if (wave.gameObject.activeSelf != ul) wave.gameObject.SetActive(ul);
        if (ul)
        {
            float full = Mathf.Min(title.preferredWidth, title.rectTransform.sizeDelta.x) + 6f;
            float w = full * UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)];
            var wr = wave.rectTransform;
            if (Mathf.Abs(wr.sizeDelta.x - w) > 0.5f) wr.sizeDelta = new Vector2(w, wr.sizeDelta.y);
        }
        bool showAfter = on && ulDrawn >= 2f && after.text.Length > 0;
        if (after.gameObject.activeSelf != showAfter) after.gameObject.SetActive(showAfter);
        if (paper.Boil != on) paper.Boil = on;
        // pose: seeded tilt at rest, straight + lifted + bigger when focused; the deal pop; press dip; squash; shake
        float lift = on ? 16f : 0f, sc = on ? 1.06f : 1f, rot = on ? 0f : tilt;
        float dealS = 1f, dealY = 0f;
        if (dealStep >= 0 && dealStep < 3) { dealS = DealScale[dealStep]; dealY = DealY[dealStep]; }
        bool pressed = down || Time.unscaledTime - pressT < 0.12f;
        body.anchoredPosition = new Vector2(shake, lift + dealY + (pressed ? -3f : 0f));
        body.localEulerAngles = new Vector3(0f, 0f, rot);
        body.localScale = squashed ? SquashScale : (pressed ? new Vector3(sc * 1.04f, sc * 0.94f, 1f) : new Vector3(sc * dealS, sc * dealS, 1f));
        paper.ShadowOffset = on ? new Vector2(9f, -12f) : new Vector2(6f, -8f);
        // the glyph hops on the preview's beat
        float hop = 0f;
        if (bouncing) { double f = Beat - Math.Floor(Beat); if (Beat >= 0.0) hop = 9f * Mathf.Pow(1f - (float)f, 3f); }
        glyphRoot.anchoredPosition = new Vector2(0f, MenuGallery.CardH * 0.5f - 72f + hop);
    }
}
