using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title menu's "my songs" shelf (<see cref="SongLibrary"/>): choosing "my songs" sinks the DEACUBE title into the wall (MenuStage.SetTitleDown)
/// and the player's songs fill the page — a grid of ink cards (<see cref="ShelfGrid"/>: up to 5 × 2 a page, their 1080p size, scaled down to fit),
/// "back" at the top-left, "undo" at the top-right, nothing else but the wall and its cubes; the gallery shelf's family (<see cref="MenuGallery"/>):
/// dealt in one by one on twos, a seeded tilt at rest, the focused card straight, lifted, boiling, its name underlined with the wavy ink line,
/// the wall behind lit in the song's first chord colour. The first card is "+ new song" (a
/// dotted ghost card with a big +: the vibe prompt, whose song lands as a new library entry); then one cream card per song, newest first: its
/// chords as a strip of little inked cubes in their colours (its first 8 columns, "+N" when it is longer), its tempo on a sticker, its name and
/// "edited 5 min ago", and three small controls with the standard pictures and a word under each — rename (a pencil: the name becomes an inline
/// text field, Enter keeps it, Esc cancels), duplicate (⧉: a copy, shown first) and delete (a trash can: a first click arms it — "click
/// again to delete", "delete?" — a second click or a hold deletes; the song goes to the library's trash and "undo" at the top-right brings it back). Clicking a card
/// opens its song into the world through the menu's comic leave (<see cref="MainMenu.OpenFromSongs"/>). Ten cards a page; with more, ‹ › beside the
/// grid and "1 / 2" under it page through. ← → move along a row, ↑ ↓ between rows ("back" comes before the first card; past a page's edge the
/// keys go on to the next / previous page); Enter opens; Esc / Backspace / "back" return to the words (the title rises again).
/// Own overlay canvas above the menu's (<see cref="SortOrder"/>), faded with the menu UI.
/// </summary>
public class MenuSongs : MonoBehaviour
{
    public const int SortOrder = MainMenu.SortOrder + 1;
    /// <summary>A card (1080p reference px); the page grid (<see cref="ShelfGrid"/>) scales it down to fit smaller screens.</summary>
    public const float CardW = 224f, CardH = 284f;
    public const int PerPage = ShelfGrid.PerPage;
    /// <summary>How far the wall's light and hops spread behind a card (MenuStage.HoverSpread; a word is 1).</summary>
    public const float CardSpread = 2.4f;
    /// <summary>Seconds an armed trash can waits for its second click; seconds "undo" stays after a delete.</summary>
    public const float ArmSeconds = 3f, UndoSeconds = 12f;

    MainMenu menu; MenuMusic music;
    Canvas canvas; CanvasGroup group; RectTransform root, shelf, backRoot, undoRoot;
    MenuButton back, undoBtn;
    InkPainter undoArt, prevArt, nextArt;
    HudButton prevBtn, nextBtn;
    TextMeshProUGUI undoText, pageText;
    readonly List<SongEntry> items = new List<SongEntry>();   // [0] null = "+ new song"; then the songs, newest first
    readonly List<SongCard> cards = new List<SongCard>();     // the cards of the page on screen
    bool open, dirty = true, focusFromPointer;
    Vector3 restMouse; bool pointerLive = true;   // a cursor resting where the shelf, the keys or a new page put a card focuses nothing until it moves
    float shown, dealT, pointerLeftAt = -1f, undoUntil = -1f, armedAt = -9f;
    int focus = -1, page, dealt, renameEndedFrame = -10, focusItemAfter = -1;
    float renameEndedAt = -9f;
    string undoId, undoName, armedId, focusIdAfter, popId;
    SongCard renaming;
    Vector4 laidOut = new Vector4(-1f, -1f, -1f, -1f);

    // ------------------------------------------------------------------ read-backs (MainMenu, tests)
    public bool IsOpen => open;
    /// <summary>0 = closed … 1 = open (the words fade the other way).</summary>
    public float Shown => shown;
    public Canvas Canvas => canvas;
    /// <summary>The cards on screen (one page).</summary>
    public IReadOnlyList<SongCard> Cards => cards;
    /// <summary>What the shelf lists: [0] = null ("+ new song"), then the songs as the library had them when the shelf last rebuilt.</summary>
    public IReadOnlyList<SongEntry> Items => items;
    public MenuButton BackButton => back;
    public MenuButton UndoButton => undoBtn;
    public HudButton PrevButton => prevBtn;
    public HudButton NextButton => nextBtn;
    public bool UndoShown => undoRoot != null && undoRoot.gameObject.activeSelf;
    public string UndoText => undoText != null ? undoText.text : "";
    public string PageText => pageText != null && pageText.gameObject.activeSelf ? pageText.text : "";
    public int Page => page;
    public int Pages => Mathf.Max(1, (items.Count + PerPage - 1) / PerPage);
    /// <summary>The focused item (0 = "+ new song", k = the k-th song), -1 for none or "back".</summary>
    public int FocusItem => focus >= 1 ? focus - 1 : -1;
    public bool BackFocused => focus == 0;
    public SongCard FocusedCard => CardOfItem(FocusItem);
    /// <summary>The song whose trash can waits for its second click (null: none).</summary>
    public string ArmedId => armedId != null && Time.unscaledTime - armedAt < ArmSeconds ? armedId : null;
    public bool Renaming => renaming != null;
    public SongCard RenamingCard => renaming;
    /// <summary>The wall's light behind the focused card: the song's first chord colour (the lilac accent for "+ new song"; null for "back").</summary>
    public Color? Tint { get { var c = FocusedCard; return c != null ? c.Tint : (Color?)null; } }
    public float HoverSpread => FocusedCard != null ? CardSpread : 1f;
    public Vector2 HoverScreen { get { var c = FocusedCard; if (c != null) return c.ScreenCenter; return back != null && focus == 0 ? back.ScreenCenter : Vector2.zero; } }
    public float HoverAmount { get { var c = FocusedCard; if (c != null) return c.FocusAmount; return back != null && focus == 0 ? back.HoverAmount : 0f; } }
    public float CardScale { get; private set; } = 1f;
    public int Opens { get; private set; }
    public int Closes { get; private set; }
    public int Deals => dealt;
    public int Rebuilds { get; private set; }

    public static MenuSongs Create(Transform parent, MainMenu menu, MenuMusic music)
    {
        var go = new GameObject("MenuSongs", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<MenuSongs>();
        s.menu = menu; s.music = music;
        s.Build();
        return s;
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

        // "back": the gallery's ink arrow and lowercase word, at the page's top-left
        backRoot = UIKit.RT(UIKit.Obj("Back", root));
        backRoot.anchorMin = backRoot.anchorMax = new Vector2(0.5f, 0.5f); backRoot.pivot = new Vector2(0f, 1f);
        backRoot.sizeDelta = new Vector2(220f, 52f);
        var arrow = InkPainter.Create(backRoot, "Arrow", new Vector2(34f, 34f), p => PaintChevronArrow(p, -1, false));
        var art = arrow.rectTransform; art.anchorMin = art.anchorMax = art.pivot = new Vector2(0f, 1f); art.anchoredPosition = new Vector2(0f, -12f);
        back = MenuButton.CreateText(backRoot, "BackWord", "back", 30f, 30f, false);
        var brt = UIKit.RT(back); brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 1f); brt.anchoredPosition = new Vector2(40f, 0f);
        back.onHover = () => { focusFromPointer = true; pointerLeftAt = -1f; SetFocus(0, true); };
        back.onPress = () => { if (MainMenu.Stage != null) MainMenu.Stage.Press(back.PressPoint, new Color(0.55f, 0.6f, 1f)); };
        back.onClick = () => Close(true);

        // "undo" after a delete: "‹name› deleted", the curved undo arrow and the word, at the page's top-right
        undoRoot = UIKit.RT(UIKit.Obj("Undo", root));
        undoRoot.anchorMin = undoRoot.anchorMax = new Vector2(0.5f, 0.5f); undoRoot.pivot = new Vector2(1f, 1f);
        undoRoot.sizeDelta = new Vector2(560f, 52f);
        undoBtn = MenuButton.CreateText(undoRoot, "UndoWord", "undo", 30f, 30f, false);
        var urt = UIKit.RT(undoBtn); urt.anchorMin = urt.anchorMax = urt.pivot = new Vector2(1f, 1f); urt.anchoredPosition = Vector2.zero;
        float uw = urt.sizeDelta.x;
        undoArt = InkPainter.Create(undoRoot, "UndoArrow", new Vector2(38f, 38f), PaintUndo);
        var uart = undoArt.rectTransform; uart.anchorMin = uart.anchorMax = new Vector2(1f, 1f); uart.pivot = new Vector2(0.5f, 0.5f); uart.anchoredPosition = new Vector2(-uw - 14f, -27f);
        undoText = UIKit.Text(undoRoot, "UndoText", "", 22f, Comic.Cream, TextAlignmentOptions.MidlineRight);
        undoText.overflowMode = TextOverflowModes.Ellipsis;
        var utr = undoText.rectTransform; utr.anchorMin = utr.anchorMax = utr.pivot = new Vector2(1f, 1f); utr.sizeDelta = new Vector2(330f, 40f); utr.anchoredPosition = new Vector2(-uw - 40f, -6f);
        undoBtn.onHover = () => { };
        undoBtn.onPress = () => { if (MainMenu.Stage != null) MainMenu.Stage.Press(undoBtn.PressPoint, new Color(0.55f, 0.6f, 1f)); };
        undoBtn.onClick = UndoDelete;
        undoRoot.gameObject.SetActive(false);

        // the pager: ‹ › beside the row, "1 / 3" under it (only when there is more than one page)
        prevArt = InkPainter.Create(root, "PagePrev", new Vector2(46f, 64f), p => PaintChevronArrow(p, -1, true), true);
        prevBtn = HudKit.Control(prevArt, "newer songs", () => PageStep(-1), null, null, prevArt);
        nextArt = InkPainter.Create(root, "PageNext", new Vector2(46f, 64f), p => PaintChevronArrow(p, 1, true), true);
        nextBtn = HudKit.Control(nextArt, "older songs", () => PageStep(1), null, null, nextArt);
        pageText = UIKit.Text(root, "PageText", "", 24f, Comic.Cream, TextAlignmentOptions.Center);
        Comic.StyleText(pageText, Comic.DigitFont);
        pageText.characterSpacing = 2f;
        pageText.rectTransform.sizeDelta = new Vector2(160f, 34f);
        prevArt.gameObject.SetActive(false); nextArt.gameObject.SetActive(false); pageText.gameObject.SetActive(false);

        canvas.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> pts = new List<Vector2>(48);

    /// <summary>The back arrow (<paramref name="chevron"/> false: a stroke with its head) or a page chevron ‹ ›, in cream ink printed over a
    /// violet offset (the words' print shadow).</summary>
    static void PaintChevronArrow(InkPainter p, int dir, bool chevron)
    {
        Rect r = p.Area;
        for (int pass = 0; pass < 2; pass++)
        {
            Color col = pass == 0 ? Comic.PrintShadow : Comic.Cream;
            p.Shift = pass == 0 ? new Vector2(2.5f, -3f) : Vector2.zero;
            if (chevron)
            {
                float w = r.width * 0.32f, hgt = r.height * 0.36f;
                Vector2 c = r.center + new Vector2(-dir * 3f, 0f), tip = c + new Vector2(dir * w * 0.5f, 0f);
                p.Line(c + new Vector2(-dir * w * 0.5f, hgt), tip, 5f, col, true);
                p.Line(tip, c + new Vector2(-dir * w * 0.5f, -hgt), 5f, col, true);
            }
            else
            {
                Vector2 a = new Vector2(r.xMax - 4f, r.center.y), b = new Vector2(r.xMin + 8f, r.center.y);
                p.Line(a, b, 4.2f, col, true);
                p.Head(b - new Vector2(1f, 0f), Vector2.left, 12f, 16f, col);
            }
        }
        p.Shift = Vector2.zero;
    }

    /// <summary>The standard undo arrow: a hook that climbs over the top and comes down with its head on the left, cream over the print shadow.</summary>
    static void PaintUndo(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(1f, -2f);
        pts.Clear();
        InkPainter.ArcPoints(pts, c, 11f, 9.5f, -35f, 205f, 24);
        Vector2 end = pts[pts.Count - 1], dir = (end - pts[pts.Count - 3]).normalized;
        p.Shift = new Vector2(2.5f, -3f);
        p.Stroke(pts, pts.Count, 4.4f, Comic.PrintShadow, false, 0f, 0f, -1f, true);
        p.Head(end + dir * 3f, dir, 10f, 13f, Comic.PrintShadow);
        p.Shift = Vector2.zero;
        p.Stroke(pts, pts.Count, 4.4f, Comic.Cream, false, 0f, 0f, -1f, true);
        p.Head(end + dir * 3f, dir, 10f, 13f, Comic.Cream);
    }

    // ------------------------------------------------------------------ the list and the cards
    /// <summary>The library changed (SongLibrary.OnChanged): the shelf rebuilds on its next tick (at once when it opens).</summary>
    public void MarkDirty() { dirty = true; }

    void RebuildItems()
    {
        items.Clear();
        items.Add(null);
        foreach (var e in SongLibrary.Entries) items.Add(e);
        dirty = false;
    }

    int ItemOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 1; i < items.Count; i++) if (items[i] != null && items[i].id == id) return i;
        return -1;
    }

    public SongCard CardOfItem(int item) { foreach (var c in cards) if (c != null && c.Item == item) return c; return null; }
    public SongCard CardOf(string id) { foreach (var c in cards) if (c != null && c.Entry != null && c.Entry.id == id) return c; return null; }

    /// <summary>The cards of page <see cref="page"/> (destroyed and made again: a page holds at most <see cref="PerPage"/>).</summary>
    void BuildPage(bool deal)
    {
        if (renaming != null) EndRename(renaming, true);
        foreach (var c in cards) if (c != null) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        cards.Clear();
        RestPointer();
        page = Mathf.Clamp(page, 0, Pages - 1);
        int first = page * PerPage, last = Mathf.Min(items.Count, first + PerPage);
        for (int g = first; g < last; g++) cards.Add(SongCard.Create(shelf, this, items[g], g));
        Rebuilds++;
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
        Layout();
        if (deal) Deal();
        foreach (var c in cards) c.SetFocused(focus == c.Item + 1);
        RefreshPager();
    }

    void Deal()
    {
        dealT = 0f;
        for (int i = 0; i < cards.Count; i++) cards[i].BeginDeal(1 + i);
    }

    /// <summary>Rebuilds after a library change, keeping the focus where it makes sense: on the song asked for (a duplicate, a renamed or
    /// restored song), on the slot a deleted song left, else where it was.</summary>
    void RebuildKeepFocus()
    {
        string wantId = focusIdAfter; focusIdAfter = null;
        int wantItem = focusItemAfter; focusItemAfter = -1;
        int old = focus;
        RebuildItems();
        int g = wantId != null ? ItemOf(wantId) : -1;
        if (g < 0 && wantItem >= 0) g = Mathf.Min(wantItem, items.Count - 1);
        if (g < 0 && old >= 1) g = Mathf.Min(old - 1, items.Count - 1);
        focus = -1;
        if (g >= 0) page = g / PerPage;
        BuildPage(false);
        if (g >= 0) SetFocus(g + 1, false); else if (old == 0) SetFocus(0, false);
        if (popId != null)
        {
            var pc = CardOf(popId); popId = null;
            if (pc != null) { dealT = 0f; pc.BeginDeal(1); }
        }
    }

    // ------------------------------------------------------------------ open / close
    /// <summary>Swaps the words for the shelf: the cards deal in; the world's song (else the current one, else the newest) takes the focus.</summary>
    public void Open(string focusId = null)
    {
        if (open) return;
        SongLibrary.EnsureImported();
        open = true; Opens++;
        RestPointer();
        canvas.gameObject.SetActive(true);
        HideUndo(); armedId = null; renaming = null;
        RebuildItems();
        string want = focusId;
        if (string.IsNullOrEmpty(want)) want = SongLibrary.LiveId;
        if (string.IsNullOrEmpty(want)) want = SongLibrary.CurrentId;
        int g = ItemOf(want);
        if (g < 0) g = items.Count > 1 ? 1 : 0;
        focus = -1;
        page = g / PerPage;
        BuildPage(true);
        focusFromPointer = false; pointerLeftAt = -1f;
        SetFocus(g + 1, false);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.22f, 1.25f);
    }

    /// <summary>Back to the words ("back", Esc).</summary>
    public void Close(bool sound = true)
    {
        if (!open) return;
        if (renaming != null) EndRename(renaming, true);
        open = false; Closes++;
        armedId = null;
        SetFocus(-1, false);
        if (sound) AudioPool.UI(ProceduralAudio.Whoosh(), 0.2f, 0.85f);
        if (menu != null) menu.OnSongsClosed();
    }

    /// <summary>Closed and out of sight at once (the menu re-enters, hides or leaves): the cards reset, "undo" gone.</summary>
    public void CloseImmediate()
    {
        if (renaming != null) EndRename(renaming, true);
        open = false; shown = 0f;
        focus = -1; focusFromPointer = false; pointerLeftAt = -1f; armedId = null;
        HideUndo();
        foreach (var c in cards) if (c != null) { c.SetFocused(false); c.ResetLook(); }
        if (back != null) { back.SetFocused(false); back.Unsquash(); }
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    /// <summary>The leave took over the screen (the held frame shows the shelf from now on).</summary>
    public void HideCanvas() { if (canvas != null) canvas.gameObject.SetActive(false); }

    /// <summary>A leave began: the shelf keeps its look for the held frame but takes no more input.</summary>
    public void BlockInput() { if (group != null) { group.interactable = false; group.blocksRaycasts = false; } }

    // ------------------------------------------------------------------ focus, pages, keys
    void SetFocus(int f, bool sound)
    {
        f = Mathf.Clamp(f, -1, items.Count);
        if (f == focus) return;
        focus = f;
        if (f >= 1)
        {
            int pg = (f - 1) / PerPage;
            if (pg != page) { page = pg; BuildPage(true); }
        }
        if (back != null) back.SetFocused(f == 0);
        foreach (var c in cards) c.SetFocused(f == c.Item + 1);
        if (sound && f >= 0) AudioPool.UI(ProceduralAudio.Tick(), 0.14f, f == 0 ? 0.9f : 1.05f + 0.04f * Mathf.Min(f, 8));
        var fc = FocusedCard;
        if (armedId != null && (fc == null || fc.Entry == null || fc.Entry.id != armedId)) armedId = null;   // moving on disarms the trash can
    }

    /// <summary>One step along "back", "+ new song", the songs in order (MainMenu.MoveFocus; no wrap; the page follows).</summary>
    public void Step(int dir)
    {
        if (!open || renaming != null) return;
        focusFromPointer = false; pointerLeftAt = -1f;
        RestPointer();
        int f = focus < 0 ? (dir > 0 ? 1 : items.Count) : Mathf.Clamp(focus + (dir > 0 ? 1 : -1), 0, items.Count);
        SetFocus(f, true);
    }

    int PageCount(int pg) => Mathf.Clamp(items.Count - pg * PerPage, 0, PerPage);
    int ColsOf(int pg) => ShelfGrid.ColsFor(PageCount(pg), Pages > 1);
    int RowsOf(int pg) { int c = ColsOf(pg); return Mathf.Max(1, (PageCount(pg) + c - 1) / c); }
    /// <summary>The item at (<paramref name="row"/>, <paramref name="col"/>) of page <paramref name="pg"/>, clamped to the cards there.</summary>
    int CellOf(int pg, int row, int col)
    {
        int cnt = PageCount(pg), cols = ColsOf(pg);
        if (cnt == 0) return -1;
        row = Mathf.Clamp(row, 0, RowsOf(pg) - 1);
        int inRow = Mathf.Min(cols, cnt - row * cols);
        return pg * PerPage + row * cols + Mathf.Clamp(col, 0, inRow - 1);
    }

    /// <summary>The arrows on the page's grid: ← → along a row (← from the top row's first card: "back"; past a row's end: the next / previous
    /// page, the same row), ↑ ↓ between rows (↑ from the top row: the previous page's bottom row, or "back"; ↓ from the bottom row: the next
    /// page's top row). From "back" (or nothing), → and ↓ go to the page's first card. No wrap.</summary>
    public void Move(int dx, int dy)
    {
        if (!open || renaming != null) return;
        focusFromPointer = false; pointerLeftAt = -1f;
        RestPointer();
        if (focus <= 0) { if (dx > 0 || dy > 0) SetFocus(page * PerPage + 1, true); return; }
        int g = focus - 1, pg = g / PerPage, idx = g - pg * PerPage;
        int cnt = PageCount(pg), cols = ColsOf(pg), rows = RowsOf(pg), row = idx / cols, col = idx % cols;
        int t = -2;   // -2 stay, -1 "back"
        if (dx > 0) { if (col < cols - 1 && idx + 1 < cnt) t = g + 1; else if (pg + 1 < Pages) t = CellOf(pg + 1, row, 0); }
        else if (dx < 0) { if (col > 0) t = g - 1; else if (pg > 0) t = CellOf(pg - 1, row, ShelfGrid.Cols - 1); else if (row == 0) t = -1; }
        else if (dy > 0) { if (row + 1 < rows) t = CellOf(pg, row + 1, col); else if (pg + 1 < Pages) t = CellOf(pg + 1, 0, col); }
        else if (dy < 0) { if (row > 0) t = CellOf(pg, row - 1, col); else if (pg > 0) t = CellOf(pg - 1, RowsOf(pg - 1) - 1, col); else t = -1; }
        if (t == -1) SetFocus(0, true);
        else if (t >= 0) SetFocus(t + 1, true);
    }

    /// <summary>‹ ›: the previous / next page, its first card focused.</summary>
    public void PageStep(int dir)
    {
        if (!open || renaming != null) return;
        int pg = Mathf.Clamp(page + (dir > 0 ? 1 : -1), 0, Pages - 1);
        if (pg == page) return;
        page = pg;
        focus = -1;
        BuildPage(true);
        focusFromPointer = false;
        SetFocus(pg * PerPage + 1, true);
    }

    /// <summary>The pointer takes no focus until it moves (the shelf opened, the keys moved or a page was dealt under a resting cursor: a card that
    /// appears under it is not the player's choice). Real pointer events only; simulated ones (tests) always count.</summary>
    public bool PointerLive => pointerLive;
    void RestPointer() { restMouse = Input.mousePosition; pointerLive = false; }

    /// <summary>Focuses item <paramref name="item"/> (0 = "+ new song", k = the k-th song; tests, keys).</summary>
    public void FocusItemAt(int item) { if (!open || item < 0 || item >= items.Count) return; focusFromPointer = false; SetFocus(item + 1, true); }
    public void FocusSong(string id) { int g = ItemOf(id); if (g >= 0) FocusItemAt(g); }
    public void FocusBack() { if (!open) return; focusFromPointer = false; SetFocus(0, true); }

    /// <summary>The shelf's keys (MainMenu calls it while the shelf is open and no field has the keys): the arrows move on the grid (<see cref="Move"/>),
    /// Enter opens (or goes back), Esc disarms an armed trash can, else Esc / Backspace go back.</summary>
    public void HandleKeys()
    {
        if (!open || renaming != null || Time.frameCount - renameEndedFrame <= 1) return;   // (the Enter / Esc that ended a rename is not ours)
        if (KeyShim.Down(KeyCode.Escape) || KeyShim.Down(KeyCode.Backspace))
        {
            if (ArmedId != null) { armedId = null; return; }
            Close(true); return;
        }
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

    // ------------------------------------------------------------------ card events (SongCard)
    internal void CardEntered(SongCard c)
    {
        if (!open) return;
        focusFromPointer = true; pointerLeftAt = -1f;
        SetFocus(c.Item + 1, true);
    }

    internal void CardExited(SongCard c)
    {
        if (open && focusFromPointer && focus == c.Item + 1) pointerLeftAt = Time.unscaledTime;
    }

    internal void CardPressed(SongCard c, Vector2 at)
    {
        if (MainMenu.Stage != null) MainMenu.Stage.Press(at, Color.Lerp(c.Tint, Color.white, 0.15f));
    }

    /// <summary>A card was clicked (or Enter): "+ new song" opens the vibe prompt; a song opens into the world.</summary>
    internal void Activate(SongCard c, Vector2 at)
    {
        if (!open || c == null || menu == null || renaming != null) return;
        if (Time.unscaledTime - renameEndedAt < 0.6f) return;   // the click that ended a rename (outside its field) only ends it
        armedId = null;
        if (c.IsNew) { menu.NewFromSongs(); return; }
        menu.OpenFromSongs(c.Entry, at);
    }

    /// <summary>The leave's anticipation on the chosen card (it stays squashed in the held frame).</summary>
    public void SquashCard(string id) { var c = CardOf(id); if (c != null) c.Squash(); }

    /// <summary>The song could not be opened: its card shakes.</summary>
    public void Refuse(string id) { var c = CardOf(id); if (c != null) c.Shake(); }

    // ------------------------------------------------------------------ rename / duplicate / delete
    /// <summary>The pencil: the card's name becomes a text field (all of it selected; Enter keeps the new name, Esc keeps the old one).</summary>
    public void BeginRename(SongCard c)
    {
        if (!open || c == null || c.IsNew) return;
        if (renaming != null && renaming != c) EndRename(renaming, true);
        armedId = null;
        SetFocus(c.Item + 1, false);
        renaming = c;
        c.ShowField(true);
    }

    /// <summary>The field let go (Enter, Esc, a click elsewhere): the typed name is kept unless <paramref name="commit"/> is false.</summary>
    public void EndRename(SongCard c, bool commit)
    {
        if (renaming == null || renaming != c) return;
        renaming = null;
        renameEndedFrame = Time.frameCount; renameEndedAt = Time.unscaledTime;
        string typed = c.FieldText;
        c.ShowField(false);
        // a hidden field that stays selected would keep InputUtil.TypingInField true and silence every key
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(c.transform)) es.SetSelectedGameObject(null);
        if (!commit || c.Entry == null) return;
        string n = SongLibrary.CleanName(typed);
        if (n == null || n == c.Entry.name) return;
        if (SongLibrary.Rename(c.Entry.id, n)) { focusIdAfter = c.Entry.id; AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.3f); }
        else { c.Shake(); AudioPool.UI(ProceduralAudio.Pop(), 0.4f, 0.7f); }
    }

    /// <summary>⧉: a copy of the song as a new card, first in the list and focused.</summary>
    public void DuplicateOf(SongCard c)
    {
        if (!open || c == null || c.IsNew) return;
        if (renaming != null) EndRename(renaming, true);
        armedId = null;
        var copy = SongLibrary.Duplicate(c.Entry.id);
        if (copy == null) { c.Shake(); AudioPool.UI(ProceduralAudio.Pop(), 0.4f, 0.7f); return; }
        focusIdAfter = copy.id; popId = copy.id;
        dirty = true;
        AudioPool.UI(ProceduralAudio.Pop(), 0.32f, 1.2f);
    }

    /// <summary>The trash can: the first click arms it ("click again", ringed in red, for <see cref="ArmSeconds"/>), the second deletes.</summary>
    public void DeleteClicked(SongCard c)
    {
        if (!open || c == null || c.IsNew) return;
        if (renaming != null) EndRename(renaming, true);
        if (ArmedId == c.Entry.id) { DeleteNow(c); return; }
        SetFocus(c.Item + 1, false);
        armedId = c.Entry.id; armedAt = Time.unscaledTime;
        AudioPool.UI(ProceduralAudio.Tick(), 0.32f, 0.7f);
    }

    /// <summary>Deletes the card's song (a second click, or a hold on the trash can): it goes to the library's trash; "undo" shows.</summary>
    public void DeleteNow(SongCard c)
    {
        if (!open || c == null || c.IsNew) return;
        armedId = null;
        string id = c.Entry.id, name = c.Entry.name;
        int item = c.Item;
        if (!SongLibrary.Delete(id)) { c.Shake(); AudioPool.UI(ProceduralAudio.Pop(), 0.4f, 0.7f); return; }
        undoId = id; undoName = name; undoUntil = Time.unscaledTime + UndoSeconds;
        ShowUndo();
        focusItemAfter = item;
        dirty = true;
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.3f, 0.8f);
    }

    /// <summary>"undo": the song deleted last comes back from the trash, focused.</summary>
    public void UndoDelete()
    {
        if (undoId == null) return;
        string id = undoId;
        undoId = null;
        HideUndo();
        if (SongLibrary.Restore(id)) { focusIdAfter = id; popId = id; dirty = true; AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.1f); }
    }

    void ShowUndo()
    {
        if (undoRoot == null) return;
        undoText.text = "\"" + SongCard.Clean(undoName ?? "") + "\" deleted";
        undoRoot.gameObject.SetActive(true);
        undoBtn.Unsquash();
        undoBtn.SetFocused(true);   // underlined from the start: the one thing to click on that line
    }

    void HideUndo()
    {
        undoId = null;
        if (undoRoot != null && undoRoot.gameObject.activeSelf) { undoBtn.SetFocused(false); undoRoot.gameObject.SetActive(false); }
    }

    void RefreshPager()
    {
        bool many = Pages > 1;
        if (prevArt == null) return;
        prevArt.gameObject.SetActive(many && page > 0);
        nextArt.gameObject.SetActive(many && page < Pages - 1);
        pageText.gameObject.SetActive(many);
        if (many) pageText.text = (page + 1) + " / " + Pages;
    }

    // ------------------------------------------------------------------ per frame (MainMenu, while the menu is up and not leaving)
    public void Tick(float dt, float uiAlpha, bool live)
    {
        float now = Time.unscaledTime;
        if (open) dealT += dt;
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
        if (!open && shown <= 0f) return;
        if (open && dirty && renaming == null) RebuildKeepFocus();
        Layout();
        // the deal: a card lands on its drawing with a soft paper tick
        int d = Mathf.FloorToInt(dealT * Look.TwosFps + 1e-4f);
        foreach (var c in cards) if (c.DealStep(d)) { dealt++; AudioPool.UI(ProceduralAudio.Tick(), 0.16f, 0.85f + 0.06f * (c.Item % PerPage)); }
        // the pointer left the card it focused (and entered none): the focus goes
        if (pointerLeftAt > 0f && now - pointerLeftAt > 0.2f)
        {
            pointerLeftAt = -1f;
            bool any = false; foreach (var c in cards) if (c.Hover) { any = true; break; }
            if (!any && focusFromPointer && focus >= 1 && renaming == null && ArmedId == null) SetFocus(-1, false);
        }
        if (armedId != null && now - armedAt >= ArmSeconds) armedId = null;
        if (undoId != null && now >= undoUntil) HideUndo();
    }

    readonly List<Vector2> centers = new List<Vector2>(PerPage);

    /// <summary>The page (the title sunk into the wall meanwhile): "back" at the top-left, "undo" at the top-right; the page's cards in a centred grid
    /// below them (<see cref="ShelfGrid"/>); ‹ › beside it and the page count under it when there is more than one page.</summary>
    void Layout()
    {
        if (root == null) return;
        var key = new Vector4(Screen.width, Screen.height, cards.Count + 16f * page + 1000f * Pages, root.lossyScale.x);
        if (key == laidOut) return;
        laidOut = key;
        bool paged = Pages > 1;
        int n = cards.Count, cols = ColsOf(page);
        Vector2 tl, tr, pv, nx, pg;
        CardScale = ShelfGrid.Layout(root.rect, n, cols, CardW, CardH, paged, centers, out tl, out tr, out pv, out nx, out pg);
        backRoot.anchoredPosition = tl;
        undoRoot.anchoredPosition = tr;
        for (int i = 0; i < n; i++)
        {
            var rt = cards[i].Root;
            rt.localScale = new Vector3(CardScale, CardScale, 1f);
            rt.anchoredPosition = centers[i];
        }
        if (prevArt != null)
        {
            prevArt.rectTransform.anchoredPosition = pv;
            nextArt.rectTransform.anchoredPosition = nx;
            pageText.rectTransform.anchoredPosition = pg;
        }
    }

    Vector2 Local(Vector2 screen)
    {
        Vector2 l;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out l);
        return l;
    }
}

/// <summary>
/// One card of the "my songs" shelf (<see cref="MenuSongs"/>): "+ new song" (<see cref="Entry"/> null) or a song. The root keeps its slot and takes
/// the pointer; the body inside moves on twos — a seeded tilt at rest, straight, lifted and a little bigger while focused, dipped while pressed,
/// squashed as the leave's anticipation, shaken when refused; it pops in on its deal drawing. Its controls (HudButtons on inked discs) take their
/// own clicks, so a click on one never opens the song.
/// </summary>
public class SongCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public SongEntry Entry { get; private set; }
    public bool IsNew => Entry == null;
    /// <summary>The card's item on the shelf (0 = "+ new song", k = the k-th song).</summary>
    public int Item { get; private set; }
    public RectTransform Root { get; private set; }
    public RectTransform body;
    public InkShape paper, sticker, plusDisc, fieldPaper;
    public InkPainter chords;
    public TextMeshProUGUI title, edited, tempo, more, newWords;
    public TextMeshProUGUI renameLabel, duplicateLabel, deleteLabel;
    public InkWave wave;
    public HudButton RenameButton, DuplicateButton, DeleteButton;
    public InkShape deleteDisc;
    public Image holdRing;
    public InkPainter armRing;
    public TMP_InputField field;
    public Color Tint { get; private set; }

    MenuSongs owner;
    bool hover, down, focused, squashed, armedShown; float h, shakeT = 9f, tilt, ulDrawn, pressT = -9f; int tick = int.MinValue, dealAt = -1, dealStep = 99;
    Vector2 pressAt;
    RectTransform deleteArt;
    static readonly float[] UnderlineSteps = { 0f, 0.45f, 0.8f, 1f };
    static readonly float[] DealScale = { 1.16f, 0.95f, 1f }, DealY = { -30f, 5f, 0f };
    static readonly Vector3 SquashScale = new Vector3(1.18f, 0.78f, 1f);

    public bool Hover => hover;
    public bool Focused => focused;
    public bool Squashed => squashed;
    /// <summary>0 … 1: how focused the card looks (smoothed; the wall's light follows it).</summary>
    public float FocusAmount => h;
    public float UnderlineDrawn => wave != null && wave.gameObject.activeSelf ? UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)] : 0f;
    public bool Dealt => dealStep >= 0 && dealStep < 99 && body != null && body.gameObject.activeSelf;
    public string TitleText => title != null ? title.text : (newWords != null ? newWords.text : "");
    public string EditedText => edited != null ? edited.text : "";
    public bool FieldActive => field != null && field.gameObject.activeSelf;
    public string FieldText => field != null ? field.text : "";
    public bool Armed => armedShown;
    public Vector2 ScreenCenter => RectTransformUtility.WorldToScreenPoint(null, (body != null ? body : Root).TransformPoint((body != null ? body : Root).rect.center));
    /// <summary>The card's slot on screen (px).</summary>
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
    public static SongCard Create(Transform parent, MenuSongs owner, SongEntry e, int item)
    {
        var go = UIKit.Obj(e != null ? "Song_" + e.id : "NewSong", parent);
        var rt = UIKit.RT(go);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(MenuSongs.CardW, MenuSongs.CardH);
        var hit = go.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true;
        var c = go.AddComponent<SongCard>();
        c.owner = owner; c.Entry = e; c.Item = item; c.Root = rt;
        c.Tint = e != null && e.ColorCount > 0 ? e.ColorAt(0) : Palette.Accent;
        string key = e != null ? e.id : "new";
        unchecked { int hs = 17; foreach (char ch in key) hs = hs * 31 + ch; c.tilt = ((hs & 0xFFFF) / 65535f * 2f - 1f) * 2.8f; }
        var bgo = UIKit.Obj("Body", go.transform);
        c.body = UIKit.RT(bgo);
        UIKit.Stretch(c.body);
        c.body.pivot = new Vector2(0.5f, 0.5f);
        if (e == null) c.BuildNew(); else c.BuildSong();
        c.ResetLook();
        return c;
    }

    int SeedOf(int salt) { unchecked { int hs = 23 + salt; foreach (char ch in Entry != null ? Entry.id : "new") hs = hs * 37 + ch; return hs == 0 ? 1 : hs; } }

    /// <summary>"+ new song": a dotted cream ghost of a card, a big + on a cream disc, the words in the menu's lettering.</summary>
    void BuildNew()
    {
        float W = MenuSongs.CardW, H = MenuSongs.CardH;
        paper = InkShape.Create(body, "Ghost", InkShape.Kind.Ghost, Comic.A(Comic.Cream, 0.07f), new Vector2(W, H));
        paper.raycastTarget = false; paper.Ink = Comic.Cream; paper.SetInk(2.6f, 2.6f); paper.SetDash(11f, 7f); paper.Radius = 16f; paper.Wobble = 1.2f; paper.RotJitter = 0f;
        paper.Seed = SeedOf(1);
        plusDisc = InkShape.Create(body, "PlusDisc", InkShape.Kind.Circle, Comic.Cream, new Vector2(86f, 86f));
        plusDisc.raycastTarget = false; plusDisc.Ink = Comic.Ink; plusDisc.SetInk(2.4f, 3.4f); plusDisc.ShadowColor = Comic.PrintShadow; plusDisc.ShadowOffset = new Vector2(5f, -6f);
        plusDisc.rectTransform.anchoredPosition = new Vector2(0f, 30f);
        var plus = InkPainter.Create(plusDisc.rectTransform, "Plus", new Vector2(86f, 86f), PaintPlus);
        plus.raycastTarget = false;
        newWords = UIKit.Text(body, "Words", "new song", 30f, Comic.Cream, TextAlignmentOptions.Center);
        var wrt = UIKit.RT(newWords); wrt.sizeDelta = new Vector2(W - 20f, 40f); wrt.anchoredPosition = new Vector2(0f, -54f);
        wave = InkWave.Create(body, "Underline", Comic.Cream, new Vector2(10f, 12f));
        wave.Amplitude = 2.2f; wave.Wavelength = 12f; wave.Thickness = 2.6f;
        wave.rectTransform.anchoredPosition = new Vector2(0f, -78f);
        wave.gameObject.SetActive(false);
    }

    /// <summary>A song: cream paper, its chords as little cubes in a lilac window, the tempo sticker, the name, "edited …", the three controls.</summary>
    void BuildSong()
    {
        float W = MenuSongs.CardW, H = MenuSongs.CardH;
        Color ink = Comic.Ink;
        paper = InkShape.Create(body, "Paper", InkShape.Kind.RoundRect, Comic.Cream, new Vector2(W, H));
        paper.raycastTarget = false; paper.Ink = ink; paper.SetInk(2.4f, 4.2f); paper.Radius = 16f; paper.Wobble = 1.5f; paper.RotJitter = 0f;
        paper.ShadowColor = Comic.PrintShadow; paper.ShadowOffset = new Vector2(6f, -8f); paper.Seed = SeedOf(2);

        // the song's chords: a row of little inked cubes in its first columns' chord colours, on a lilac window
        chords = InkPainter.Create(body, "Chords", new Vector2(W - 30f, 84f), PaintChords);
        chords.raycastTarget = false;
        chords.rectTransform.anchoredPosition = new Vector2(0f, H * 0.5f - 62f);
        int extra = Entry.columns - Entry.ColorCount;
        if (extra > 0)
        {
            more = UIKit.Text(chords.rectTransform, "More", "+" + extra, 18f, Comic.A(ink, 0.8f), TextAlignmentOptions.MidlineRight);
            more.font = Comic.DigitFont; more.fontSharedMaterial = Comic.DigitFont.material;
            var mrt = more.rectTransform; mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(1f, 0.5f); mrt.sizeDelta = new Vector2(44f, 24f); mrt.anchoredPosition = new Vector2(-12f, -2f);
        }

        // the tempo on a marigold sticker slapped on the top-right corner (the gallery cards' tempo sticker)
        if (Entry.bpm > 0f)
        {
            sticker = InkShape.Create(body, "Tempo", InkShape.Kind.Sticker, Comic.Pop, new Vector2(62f, 36f));
            sticker.raycastTarget = false; sticker.Ink = ink; sticker.SetInk(1.8f, 2.8f); sticker.ShadowColor = Comic.A(ink, 0.9f); sticker.ShadowOffset = new Vector2(3f, -4f);
            sticker.Seed = SeedOf(3);
            var srt = sticker.rectTransform; srt.anchoredPosition = new Vector2(W * 0.5f - 16f, H * 0.5f - 8f); srt.localEulerAngles = new Vector3(0f, 0f, -9f);
            tempo = UIKit.Text(srt, "Digits", Mathf.RoundToInt(Entry.bpm).ToString(), 25f, ink, TextAlignmentOptions.Center);
            tempo.font = Comic.DigitFont; tempo.fontSharedMaterial = Comic.DigitFont.material; tempo.characterSpacing = 2f;
            UIKit.RT(tempo).sizeDelta = new Vector2(62f, 36f); UIKit.RT(tempo).anchoredPosition = new Vector2(0f, 1f);
        }

        // the name (as the player typed it), its wavy underline (focused), "edited …"
        title = UIKit.Text(body, "Title", Clean(Entry.name), 30f, ink, TextAlignmentOptions.Center);
        title.enableAutoSizing = true; title.fontSizeMin = 17f; title.fontSizeMax = 30f; title.overflowMode = TextOverflowModes.Ellipsis;
        var trt = UIKit.RT(title); trt.sizeDelta = new Vector2(W - 26f, 40f); trt.anchoredPosition = new Vector2(0f, -2f);
        wave = InkWave.Create(body, "Underline", ink, new Vector2(10f, 12f));
        wave.Amplitude = 2.2f; wave.Wavelength = 12f; wave.Thickness = 2.6f;
        wave.rectTransform.anchoredPosition = new Vector2(0f, -25f);
        wave.gameObject.SetActive(false);
        edited = UIKit.Text(body, "Edited", SongLibrary.Edited(Entry), 16f, Comic.A(ink, 0.72f), TextAlignmentOptions.Center);
        var ert = UIKit.RT(edited); ert.sizeDelta = new Vector2(W - 20f, 22f); ert.anchoredPosition = new Vector2(0f, -46f);

        // the controls: the standard pictures on inked discs, a word under each (the pictures alone never say enough)
        float cy = -H * 0.5f + 54f, ly = -H * 0.5f + 21f;
        InkShape disc;
        RenameButton = Control("Rename", new Vector2(-68f, cy), Comic.Cream, PaintPencil, "rename", () => { if (owner != null) owner.BeginRename(this); }, out disc);
        DuplicateButton = Control("Duplicate", new Vector2(0f, cy), Comic.Cream, PaintCopy, "make a copy", () => { if (owner != null) owner.DuplicateOf(this); }, out disc);
        DeleteButton = Control("Delete", new Vector2(68f, cy), Comic.Danger, PaintTrash, "delete (click twice, or hold)", () => { if (owner != null) owner.DeleteClicked(this); }, out deleteDisc);
        deleteArt = deleteDisc.transform.Find("Art") as RectTransform;
        DeleteButton.onHold = () => { if (owner != null) owner.DeleteNow(this); };
        DeleteButton.holdSeconds = 0.8f;
        // red rings around the trash can: one fills while the can is held (the button's hold fill), one shows whole while the can is armed
        holdRing = UIKit.Image(deleteDisc.transform, "HoldRing", "ringThin", Comic.Danger, new Vector2(74f, 74f));
        holdRing.type = Image.Type.Filled; holdRing.fillMethod = Image.FillMethod.Radial360; holdRing.fillOrigin = (int)Image.Origin360.Top; holdRing.fillClockwise = true; holdRing.fillAmount = 0f;
        holdRing.raycastTarget = false;
        holdRing.rectTransform.SetAsFirstSibling();
        DeleteButton.holdFill = holdRing;
        armRing = InkPainter.Create(deleteDisc.transform, "ArmRing", new Vector2(80f, 80f), p =>
        {
            // an inked red ring with a gap of paper between it and the disc
            Vector2 c = p.Area.center;
            p.Arc(c, 30f, 6.6f, Comic.Ink);
            p.Arc(c, 30f, 3.4f, Comic.Danger);
        });
        armRing.raycastTarget = false;
        armRing.rectTransform.SetAsFirstSibling();
        armRing.gameObject.SetActive(false);
        renameLabel = Label("rename", -68f, ly);
        duplicateLabel = Label("duplicate", 0f, ly);
        deleteLabel = Label("delete", 68f, ly);
        BuildField();
    }

    HudButton Control(string name, Vector2 pos, Color fill, Action<InkPainter> paint, string caption, Action onClick, out InkShape disc)
    {
        var rt = HudKit.Node(body, name, new Vector2(0.5f, 0.5f), pos, new Vector2(42f, 42f));
        disc = rt.gameObject.AddComponent<InkShape>();
        disc.Shape = InkShape.Kind.Circle; disc.color = fill; disc.Ink = Comic.Ink; disc.SetInk(1.8f, 2.6f); disc.ShadowOffset = new Vector2(2.5f, -3f);
        disc.Seed = SeedOf(name.Length * 7 + 11);
        var art = InkPainter.Create(rt, "Art", new Vector2(42f, 42f), paint);
        art.raycastTarget = false;
        var b = HudKit.Control(disc, caption, onClick, disc, art.rectTransform, art);
        b.GetComponent<InkHover>().hoverScale = 1.12f;
        return b;
    }

    TextMeshProUGUI Label(string words, float x, float y)
    {
        var t = HudKit.Words(body, "Label_" + words, words, 14f, Comic.A(Comic.Ink, 0.85f), TextAlignmentOptions.Center);
        var rt = t.rectTransform; rt.sizeDelta = new Vector2(84f, 20f); rt.anchoredPosition = new Vector2(x, y);
        return t;
    }

    /// <summary>The inline name field: a white paper strip with an ink outline over the name (the vibe prompt's field, smaller).</summary>
    void BuildField()
    {
        float W = MenuSongs.CardW;
        var go = UIKit.Obj("NameField", body);
        go.SetActive(false);   // built asleep: a Selectable woken under a non-interactable group keeps its grey "disabled" tint for good
        var frt = UIKit.RT(go);
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
        frt.sizeDelta = new Vector2(W - 16f, 46f); frt.anchoredPosition = new Vector2(0f, -2f);
        fieldPaper = go.AddComponent<InkShape>();
        fieldPaper.Shape = InkShape.Kind.RoundRect; fieldPaper.color = Color.white; fieldPaper.Radius = 12f; fieldPaper.Wobble = 1f; fieldPaper.SetInk(2.2f, 3f);
        fieldPaper.HasShadow = false; fieldPaper.RotJitter = 0.3f; fieldPaper.raycastTarget = true;
        field = go.AddComponent<TMP_InputField>();
        var area = UIKit.Obj("Text Area", go.transform);
        area.AddComponent<RectMask2D>();
        UIKit.Stretch(UIKit.RT(area), 12f, 12f, 6f, 6f);
        var txt = UIKit.Text(area.transform, "Text", "", 25f, Comic.Ink, TextAlignmentOptions.Midline);
        UIKit.Stretch(UIKit.RT(txt.gameObject));
        field.textViewport = UIKit.RT(area);
        field.textComponent = txt;
        field.fontAsset = UIKit.Font;
        field.pointSize = 25f;
        field.caretColor = Comic.Magenta;
        field.customCaretColor = true;
        field.caretWidth = 3;
        field.selectionColor = Palette.AccentSoft;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = SongLibrary.MaxName;
        field.onFocusSelectAll = true;
        field.transition = Selectable.Transition.None;   // the paper strip stays white (no grey selection tint)
        field.onSubmit.AddListener(_ => { if (owner != null) owner.EndRename(this, true); });
        field.onEndEdit.AddListener(_ => { if (owner != null) owner.EndRename(this, !field.wasCanceled); });
    }

    /// <summary>Shows the name field (its text the song's name, all selected, the caret blinking) instead of the name, or hides it again.</summary>
    public void ShowField(bool on)
    {
        if (field == null) return;
        if (on)
        {
            field.text = Entry != null ? Entry.name : "";
            field.gameObject.SetActive(true);
            if (fieldPaper != null) fieldPaper.canvasRenderer.SetColor(Color.white);   // (never a selection tint on the paper strip)
            if (title != null) title.gameObject.SetActive(false);
            if (wave != null) wave.gameObject.SetActive(false);
            field.ActivateInputField();
            field.Select();
        }
        else
        {
            if (field.gameObject.activeSelf) field.gameObject.SetActive(false);
            if (title != null) title.gameObject.SetActive(true);
            tick = int.MinValue;
        }
    }

    /// <summary>Tests: types <paramref name="text"/> into the open field (as the keys would).</summary>
    public void SimType(string text) { if (field != null && field.gameObject.activeSelf) field.text = text; }

    /// <summary>Characters the UI font cannot draw are left out (a missing glyph would print a box).</summary>
    public static string Clean(string s)
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

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> q = new List<Vector2>(16);

    void PaintChords(InkPainter p)
    {
        Rect r = p.Area;
        p.RoundRect(new Rect(r.xMin + 2f, r.yMin + 2f, r.width - 4f, r.height - 4f), 12f, Comic.CreamDisc, 1.6f, Comic.A(Comic.Ink, 0.5f));
        int n = Entry != null ? Entry.ColorCount : 0;
        bool extra = Entry != null && Entry.columns > n;
        float room = r.width - 22f - (extra ? 40f : 0f);
        if (n == 0) { p.Cube(r.center, 26f, Comic.Cream, Comic.A(Comic.Ink, 0.6f), 1.4f, true); return; }
        float s = Mathf.Clamp(room / (n * 0.95f), 10f, 30f), dx = s * 0.95f, inkW = Mathf.Clamp(s * 0.09f, 1f, 1.8f);
        float x0 = r.center.x - (extra ? 20f : 0f) - (n - 1) * dx * 0.5f;
        for (int i = 0; i < n; i++)
        {
            // a gentle walk (odd columns a hair higher) so a row of equal chords still reads as steps of a song
            float y = r.center.y - 1f + ((i & 1) == 1 ? s * 0.12f : 0f);
            p.Cube(new Vector2(x0 + i * dx, y), s, Entry.ColorAt(i), Comic.Ink, inkW);
        }
    }

    /// <summary>The big + of "+ new song" (ink).</summary>
    static void PaintPlus(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Line(c + new Vector2(-17f, 0f), c + new Vector2(17f, 0f), 8f, Comic.Ink, true);
        p.Line(c + new Vector2(0f, -17f), c + new Vector2(0f, 17f), 8f, Comic.Ink, true);
    }

    /// <summary>✏ rename: a marigold pencil on the diagonal (pink eraser, cream wood, ink lead), inked.</summary>
    static void PaintPencil(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0.5f, 0.5f);
        Vector2 d = new Vector2(1f, 1f).normalized, n = new Vector2(-d.y, d.x);
        float hw = 3.6f;
        Vector2 tip = c - d * 12.5f, w0 = c - d * 6.5f, b1 = c + d * 7.5f, e1 = c + d * 12f;
        // outline (one silhouette, drawn first a little wider in ink)
        q.Clear(); q.Add(tip); q.Add(w0 - n * hw); q.Add(e1 - n * hw); q.Add(e1 + n * hw); q.Add(w0 + n * hw);
        p.Stroke(q, q.Count, 3.2f, Comic.Ink, true);
        // wood, body, eraser, lead
        q.Clear(); q.Add(tip); q.Add(w0 - n * hw); q.Add(w0 + n * hw); p.Fill(q, 3, new Color(0.98f, 0.86f, 0.68f));
        q.Clear(); q.Add(w0 - n * hw); q.Add(b1 - n * hw); q.Add(b1 + n * hw); q.Add(w0 + n * hw); p.Fill(q, 4, Comic.Pop);
        q.Clear(); q.Add(b1 - n * hw); q.Add(e1 - n * hw); q.Add(e1 + n * hw); q.Add(b1 + n * hw); p.Fill(q, 4, new Color(0.98f, 0.55f, 0.66f));
        p.Line(w0 - n * hw, w0 + n * hw, 1.3f, Comic.Ink);
        p.Line(b1 - n * hw, b1 + n * hw, 1.3f, Comic.Ink);
        p.Line(w0, b1, 1f, Comic.A(Comic.Ink, 0.45f));
        q.Clear(); q.Add(tip); q.Add(tip + d * 3.4f - n * 1.9f); q.Add(tip + d * 3.4f + n * 1.9f); p.Fill(q, 3, Comic.Ink);
    }

    /// <summary>⧉ duplicate: the standard copy picture — a square outline behind, a filled square in front, both inked.</summary>
    static void PaintCopy(InkPainter p)
    {
        Vector2 c = p.Area.center;
        float s = 15f;
        p.RoundRect(new Rect(c.x - 9.5f, c.y - 2.5f, s, s), 3f, new Color(0f, 0f, 0f, 0f), 2.2f, Comic.Ink);
        p.RoundRect(new Rect(c.x - 5f, c.y - 7.5f, s, s), 3f, Color.white, 2.2f, Comic.Ink);
    }

    /// <summary>🗑 delete: a cream trash can (lid, handle, ribbed bin) on the red disc — never an ✕.</summary>
    static void PaintTrash(InkPainter p)
    {
        Vector2 t = p.Area.center + new Vector2(0f, -1f);
        q.Clear(); q.Add(t + new Vector2(-6.4f, 4.6f)); q.Add(t + new Vector2(6.4f, 4.6f)); q.Add(t + new Vector2(4.9f, -9.5f)); q.Add(t + new Vector2(-4.9f, -9.5f));
        p.Fill(q, 4, Comic.Cream);
        p.Line(t + new Vector2(-9f, 7f), t + new Vector2(9f, 7f), 2.6f, Comic.Cream, true);
        p.Line(t + new Vector2(-2.6f, 9.6f), t + new Vector2(2.6f, 9.6f), 2.3f, Comic.Cream, true);
        for (int k = -1; k <= 1; k++) p.Line(t + new Vector2(k * 2.7f, 2f), t + new Vector2(k * 2.2f, -7f), 1.2f, Comic.Danger);
    }

    // ------------------------------------------------------------------ control (MenuSongs)
    public void SetFocused(bool f)
    {
        if (focused == f) return;
        focused = f;
        if (!f) ulDrawn = 0f;
        tick = int.MinValue;
    }

    /// <summary>Hidden until deal drawing <paramref name="drawing"/> (1/12 s steps from the deal).</summary>
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
        squashed = false; shakeT = 9f; down = false; hover = false; h = 0f; ulDrawn = 0f;
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
    /// <summary>The pointer started moving: the card it rested on since the shelf opened (or the keys moved, or the page was dealt) takes the hover now.</summary>
    internal void WakePointer() { if (!pendingReal) return; pendingReal = false; hover = true; if (owner != null) owner.CardEntered(this); }
    bool pendingReal;
    public void OnPointerDown(PointerEventData e)
    {
        if (e != null && e.button != PointerEventData.InputButton.Left) return;
        if (FieldActive) return;
        down = true; pressAt = e != null ? e.position : ScreenCenter; pressT = Time.unscaledTime;
        if (owner != null) owner.CardPressed(this, pressAt);
    }
    public void OnPointerUp(PointerEventData e) { down = false; }
    public void OnPointerClick(PointerEventData e)
    {
        if (e != null && e.button != PointerEventData.InputButton.Left) return;
        if (FieldActive) return;
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
        if (Look.OnTwos && shakeT >= 0.5f) { int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps); if (t == tick) return; tick = t; }
        if (body == null) return;
        bool on = focused;
        if (on && ulDrawn < 3f) ulDrawn += 1f;
        // the underline draws itself under the name (not while the field is up)
        bool ul = on && ulDrawn > 0f && !FieldActive;
        if (wave != null)
        {
            if (wave.gameObject.activeSelf != ul) wave.gameObject.SetActive(ul);
            if (ul)
            {
                var label = title != null ? title : newWords;
                float full = Mathf.Min(label.preferredWidth, label.rectTransform.sizeDelta.x) + 6f;
                float w = full * UnderlineSteps[Mathf.Clamp((int)ulDrawn, 0, 3)];
                var wr = wave.rectTransform;
                if (Mathf.Abs(wr.sizeDelta.x - w) > 0.5f) wr.sizeDelta = new Vector2(w, wr.sizeDelta.y);
            }
        }
        if (paper != null && paper.Boil != on) paper.Boil = on;
        // the trash can armed: "click again to delete" in red on the edited line, "delete?" under the can, a whole red ring, the can wobbling on twos
        bool armed = Entry != null && owner != null && owner.ArmedId == Entry.id;
        if (deleteLabel != null && armed != armedShown)
        {
            armedShown = armed;
            deleteLabel.text = armed ? "delete?" : "delete";
            deleteLabel.color = armed ? Comic.Danger : Comic.A(Comic.Ink, 0.85f);
            var dlp = deleteLabel.rectTransform.anchoredPosition;   // (a hair lower while the ring is up, so the ring never covers it)
            deleteLabel.rectTransform.anchoredPosition = new Vector2(dlp.x, -MenuSongs.CardH * 0.5f + (armed ? 15f : 21f));
            if (edited != null)
            {
                edited.text = armed ? "click again to delete" : SongLibrary.Edited(Entry);
                edited.color = armed ? Comic.Danger : Comic.A(Comic.Ink, 0.72f);
            }
            if (armRing != null) armRing.gameObject.SetActive(armed);
            if (!armed && deleteArt != null) deleteArt.localEulerAngles = Vector3.zero;
        }
        if (armed && deleteArt != null) deleteArt.localEulerAngles = new Vector3(0f, 0f, ((tick & 1) == 0 ? 7f : -7f));
        // pose: seeded tilt at rest, straight + lifted + bigger when focused; the deal pop; press dip; squash; shake
        float lift = on ? 16f : 0f, sc = on ? 1.06f : 1f, rot = on ? 0f : tilt;
        float dealS = 1f, dealY = 0f;
        if (dealStep >= 0 && dealStep < 3) { dealS = DealScale[dealStep]; dealY = DealY[dealStep]; }
        bool pressed = down || Time.unscaledTime - pressT < 0.12f;
        body.anchoredPosition = new Vector2(shake, lift + dealY + (pressed ? -3f : 0f));
        body.localEulerAngles = new Vector3(0f, 0f, rot);
        body.localScale = squashed ? SquashScale : (pressed ? new Vector3(sc * 1.04f, sc * 0.94f, 1f) : new Vector3(sc * dealS, sc * dealS, 1f));
        if (paper != null && !IsNew) paper.ShadowOffset = on ? new Vector2(9f, -12f) : new Vector2(6f, -8f);
        if (plusDisc != null) plusDisc.ShadowOffset = on ? new Vector2(7f, -9f) : new Vector2(5f, -6f);
    }
}

/// <summary>
/// The title menu's shelves as a page (<see cref="MenuSongs"/>, <see cref="MenuGallery"/>; the user: "the song gallery should fill up the whole page —
/// DEACUBE should disappear and it should just be the background and projects"): with the title sunk into the wall the cards use the whole screen —
/// a centred grid of up to <see cref="Cols"/> × <see cref="Rows"/> under a top line ("back" at its left, "undo" at its right), the cards at their
/// 1080p size and scaled down to fit other screens; a short page balances its rows (6 → 3 + 3) and centres a short last row; with more pages, ‹ ›
/// stand beside the grid and the page count under it.
/// </summary>
public static class ShelfGrid
{
    public const int Cols = 5, Rows = 2, PerPage = Cols * Rows;
    /// <summary>Screen margin, the top line's height, the gaps between cards, the room for ‹ › on either side (reference px).</summary>
    public const float Margin = 60f, TopLine = 64f, GapX = 30f, GapY = 40f, PagerRoom = 76f;

    /// <summary>Columns for <paramref name="n"/> cards on a page: all <see cref="Cols"/> when paging, else balanced rows (6 → 3 + 3, 7 → 4 + 3).</summary>
    public static int ColsFor(int n, bool paged)
    {
        if (paged) return Cols;
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / (float)Cols));
        return Mathf.Clamp(Mathf.CeilToInt(n / (float)rows), 1, Cols);
    }

    /// <summary>Lays <paramref name="n"/> cards of <paramref name="cardW"/> × <paramref name="cardH"/> out in <paramref name="cols"/> columns on a page of
    /// <paramref name="r"/> (the canvas root's local rect): their centres (row-major), the top line's left and right corners, the ‹ › and page-count
    /// spots. Returns the cards' scale.</summary>
    public static float Layout(Rect r, int n, int cols, float cardW, float cardH, bool paged, List<Vector2> centers,
                               out Vector2 topLeft, out Vector2 topRight, out Vector2 prev, out Vector2 next, out Vector2 pageAt)
    {
        cols = Mathf.Max(1, cols);
        float top = r.yMax - Margin, left = r.xMin + Margin, right = r.xMax - Margin, bottom = r.yMin + Margin + (paged ? 44f : 0f);
        topLeft = new Vector2(left, top); topRight = new Vector2(right, top);
        float areaTop = top - TopLine - 16f, side = paged ? PagerRoom : 0f;
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, n) / (float)cols));
        float gridW = cols * cardW + (cols - 1) * GapX, gridH = rows * cardH + (rows - 1) * GapY;
        float k = Mathf.Clamp(Mathf.Min(1f, Mathf.Min((right - left - 2f * side) / gridW, (areaTop - bottom) / gridH)), 0.45f, 1f);
        float cx = (left + right) * 0.5f, cy = (areaTop + bottom) * 0.5f;
        centers.Clear();
        for (int i = 0; i < n; i++)
        {
            int row = i / cols, col = i % cols;
            int inRow = Mathf.Min(cols, n - row * cols);   // a short last row is centred
            centers.Add(new Vector2(cx + (col - (inRow - 1) * 0.5f) * (cardW + GapX) * k, cy + ((rows - 1) * 0.5f - row) * (cardH + GapY) * k));
        }
        float halfW = gridW * k * 0.5f;
        prev = new Vector2(cx - halfW - side * 0.5f - 6f, cy);
        next = new Vector2(cx + halfW + side * 0.5f + 6f, cy);
        pageAt = new Vector2(cx, cy - gridH * k * 0.5f - 30f);
        return k;
    }
}
