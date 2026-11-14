using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The song's timeline, top centre — rebuilt to the bare minimum (the owner, twice: "this timeline thing is just not readable at all", then
/// on a richer redesign "way too complicate again … BE SIMPLE AND MINIMALISTIC"). It answers three things and draws nothing else:
///
/// THE FORM: one row, one PILL per section — the section letter's colour (its plinth's) and its letter. A pill is as wide as the time it
/// plays (measures × passes), with a floor so the letter always fits. When the song is small enough that a measure gets at least
/// <see cref="CellMin"/> px, a pill also shows its measures as plain flat cells in each measure's chord colour, divided by a hairline —
/// colour only, no marks. When even the floors overflow the rail's width the row scrolls to keep the current pill in view (pills past the
/// ends are hidden, the ones at the ends fade); nothing ever shrinks below the floor.
///
/// WHERE YOU ARE: the pill holding the song position is the only one at full strength (the others a step quieter), and one progress bar
/// under it fills with the song (its own canvas: it moves every frame, the rail repaints only on change; under the pill, so it never crosses
/// the letter).
///
/// GOING SOMEWHERE: a click on a pill (on a cell: that measure's column) goes there — the camera frames it, its first grid is picked and the
/// song position jumps to it, so Play starts there (a playing song plays on from there); a right-click only moves the song position.
///
/// The controls that are controls stay: the loop curl at the row's end (click = GlobalClock.LoopSong) and, while a column is looped for
/// editing, the ink loop around its place on the row with its ✕ (FocusLoop.Dismiss). While a deck card is dragged a dotted slot shows where
/// on the row it would land. Gone on purpose: a bead per island, job badges, belts, stairs / keyboard / phrase beads, long-grid bars, launch
/// arrows, Moon beads and drum lines, section brackets, measure numbers, dotted slots for missing measures, the ground landscape, the rail's
/// pulse for a hovered deck card and its bead ↔ island hover link.
/// </summary>
public class HudColumnRail : MonoBehaviour
{
    public static HudColumnRail I;
    /// <summary>v9 (R): a launch's crash kicks the timeline's progress bar (it jumps thicker for a drawing); kicks since Play (tests).</summary>
    public static int Kicks;
    public static void Kick(float height = 1.6f) { Kicks++; if (I != null) I.kickStep = 0; }

    // ------------------------------------------------------------------ geometry (rail units: HUD px at 1080p)
    /// <summary>The row's widest (at most 40 % of the screen), a pill's height and its centre on the rail, the gap between pills, the least a
    /// pill is wide (its letter fits), the most a measure is given, the least a measure needs for the pills to show their measures as cells.</summary>
    public const float MaxWidth = 760f, PillH = 42f, PillY = 10f, PillGap = 9f, PillMinW = 38f, MeasureMax = 44f, CellMin = 16f, CellH = 12f, CellInset = 5f, BarH = 7f, BarDrop = 8f;

    // ------------------------------------------------------------------ public
    /// <summary>The pills shown (one per section) after bringing the rail up to date now (UIManager.PillCount; tests read it right after an edit).</summary>
    public int CountNow()
    {
        var sm = SongManager.I;
        if (sm != null && (dirty || pillCount != (sm.HasSong ? sm.SectionCount : 0))) Rebuild();
        return pillCount;
    }
    public int PillsShown => pillCount;
    public void RebuildNow() { Rebuild(); }
    public void MarkDirty() { dirty = true; }
    public RectTransform Root => root;
    public HudButton LoopButton => loopBtn;
    public HudButton FocusButton => focusX;
    public RectTransform FocusBadge => focusRt;
    /// <summary>Section <paramref name="s"/>'s pill: its letter, first column, the measures it plays (measures × passes), its left edge and
    /// width (rail px), whether it is inside the scroll window, its control and rect.</summary>
    public string PillLetter(int s) => Ok(s) ? pills[s].letter : "";
    public int PillFirst(int s) => Ok(s) ? pills[s].first : -1;
    public int PillMeasures(int s) => Ok(s) ? pills[s].played : 0;
    public float PillX(int s) => Ok(s) ? pills[s].x : 0f;
    public float PillWidth(int s) => Ok(s) ? pills[s].w : 0f;
    public bool PillVisible(int s) => Ok(s) && pills[s].visible;
    public HudButton PillButton(int s) => Ok(s) ? pills[s].btn : null;
    public RectTransform PillOf(int s) => Ok(s) ? pills[s].rt : null;
    public RectTransform LetterOf(int s) => Ok(s) && pills[s].label != null ? pills[s].label.rectTransform : null;
    public Color PillColour(int s) => Ok(s) ? pills[s].tint : Color.clear;
    /// <summary>True when the pills show their measures as cells (every measure has at least CellMin px); a pill's cells (one per played
    /// measure), each one's column, colour and x span (rail px).</summary>
    public bool CellsShown => cellsShown;
    public int CellCount(int s) => Ok(s) && cellsShown ? pills[s].played : 0;
    public int CellColumn(int s, int k) => Ok(s) && k >= 0 && k < pills[s].cellCol.Length ? pills[s].cellCol[k] : -1;
    public Color CellColour(int s, int k) => Ok(s) && k >= 0 && k < pills[s].cellColour.Length ? (Color)pills[s].cellColour[k] : Color.clear;
    public float CellX(int s, int k) => Ok(s) ? pills[s].x + CellInset + k * CellWidth(s) : 0f;
    public float CellWidth(int s) => Ok(s) ? (pills[s].w - 2f * CellInset) / Mathf.Max(1, pills[s].played) : 0f;
    /// <summary>Column <paramref name="col"/>'s span on the row (rail px).</summary>
    public float ColumnX(int col) => col >= 0 && col < cols.Length ? cols[col].x0 : 0f;
    public float ColumnWidth(int col) => col >= 0 && col < cols.Length ? cols[col].w : 0f;
    /// <summary>The pill at full strength: the section holding the song position (-1 without a song).</summary>
    public int CurrentSection => current;
    /// <summary>The progress bar under the current pill: where its fill ends (the song position's x on the rail), the pass of its column the
    /// song is in, the bar (its track: the current pill's span) and its fill.</summary>
    public float PlayheadX => barEndX;
    public int PlayheadPass { get; private set; } = -1;
    public RectTransform Bar => phRt;
    public RectTransform BarFill => fillRt;
    /// <summary>How far the row is scrolled and how far it can be (0 when every pill fits).</summary>
    public float ScrollX => scrollShown;
    public float ScrollMax => Mathf.Max(0f, contentW - MaxWidth);
    /// <summary>The slot the rail shows for a dragged deck card: -1 none, else the column it points at, and how it would land (tests).</summary>
    public int GhostColumn { get; private set; } = -1;
    public string GhostKind { get; private set; } = "";
    /// <summary>Kept for their callers (IslandTray): the rail draws nothing for a hovered card's job, a dragged Moon or a dragged melody any more.</summary>
    public int SwapJob { get; set; }
    public int MoonGhostColumn { get; set; } = -1;
    public int PhraseGhostColumn { get; set; } = -1;
    /// <summary>A section just reached its 4 measures (Onboarding): its pill pops once; pops so far (tests).</summary>
    public void Sparkle(int s) { SparkleCount++; if (Ok(s) && pills[s].btn != null) pills[s].btn.pulse = 1f; }
    public int SparkleCount { get; private set; }
    /// <summary>The row painter's paints so far (tests: the rail repaints only on change).</summary>
    public int PaintCount { get; private set; }
    /// <summary>Everything the rail draws: pills (a letter each), cells, the progress bar, the loop curl.</summary>
    public int GlyphCount { get { int n = 2; for (int s = 0; s < pillCount; s++) if (pills[s].visible) n += 1 + (cellsShown ? pills[s].played : 0); return n; } }
    /// <summary>A click on section <paramref name="s"/>'s pill (on its cell <paramref name="cell"/>, -1 = the pill itself) / a right-click (tests;
    /// the pointer's own clicks land here too).</summary>
    public void Click(int s, int cell = -1) { if (Ok(s)) GoTo(pills[s], cell, true); }
    public void RightClick(int s, int cell = -1) { if (Ok(s)) GoTo(pills[s], cell, false); }
    /// <summary>A pill that is not the current one: its colour a step quieter.</summary>
    public static Color Quiet(Color c) => Color.Lerp(c, QuietBase, 0.42f);
    static readonly Color QuietBase = new Color(0.56f, 0.49f, 0.70f);

    // ------------------------------------------------------------------ state
    class Pill
    {
        public int index, first, last, played; public string letter = ""; public Color tint;
        public float beat0, beat1, cx, w, x, alpha = 1f; public bool visible = true;
        public Color32[] cellColour = new Color32[0]; public int[] cellCol = new int[0], cellIsland = new int[0];
        public RectTransform rt; public InkPainter art; public HudButton btn; public TMPro.TextMeshProUGUI label;
    }
    struct Col { public float x0, w, start, len; public int bars, passes, section; }
    readonly List<Pill> pills = new List<Pill>(); int pillCount;
    Col[] cols = new Col[0];
    readonly List<int> secStarts = new List<int>();
    bool Ok(int s) => s >= 0 && s < pillCount;

    RectTransform root, beadsRt, phRt, fillRt, focusRt, loopRt;
    InkPainter line, barArt, focusArt; HudButton loopBtn, focusX; CanvasGroup group, focusGroup;
    float x0Line = -60f, x1Line = 60f, contentW, scrollShown, scrollTo, barEndX;
    bool dirty = true, cellsShown, loopShown, focusShown, dimmed; int current = -1, focusCol = -2, focusLast = -2, tick = int.MinValue, kickStep = 9;
    // a dragged card's slot
    Rect ghostRect; bool ghostOn; int ghostKey = int.MinValue;
    static int modeInsert = -9, modeMerge = -9, modeStack = -9, modeBlocked = -9;

    public static HudColumnRail Build(RectTransform hud)
    {
        var rt = HudKit.Node(hud, "Rail", new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(MaxWidth + 120f, 100f));
        var r = rt.gameObject.AddComponent<HudColumnRail>();
        r.root = rt;
        r.BuildParts();
        return r;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; Unsubscribe(); }

    bool subscribed;
    void Subscribe()
    {
        if (subscribed || SongManager.I == null) return;
        SongManager.I.OnSongRebuilt += MarkDirty; SongManager.I.OnColumnsChanged += MarkDirty; SongManager.I.OnSectionsChanged += MarkDirty;
        History.OnChanged += MarkDirty; FocusLoop.OnChanged += MarkFocus;
        subscribed = true;
    }
    void Unsubscribe()
    {
        if (!subscribed) return;
        if (SongManager.I != null) { SongManager.I.OnSongRebuilt -= MarkDirty; SongManager.I.OnColumnsChanged -= MarkDirty; SongManager.I.OnSectionsChanged -= MarkDirty; }
        History.OnChanged -= MarkDirty; FocusLoop.OnChanged -= MarkFocus;
        subscribed = false;
    }
    void MarkFocus() { focusCol = -2; }

    void BuildParts()
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        line = InkPainter.Create(root, "Line", root.sizeDelta, PaintLine);
        beadsRt = HudKit.Stretch(root, "Beads");   // the pills (the name is the one the hit-target count knows the rail's controls by)
        // the curl's hit area (the row's right end)
        var lh = HudKit.Hit(root, "LoopCurl", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 36f));
        loopRt = lh.rectTransform;
        loopBtn = HudKit.Control(lh, "loop", () => { GlobalClock.LoopSong = !GlobalClock.LoopSong; History.Push(); line.Repaint(); }, null, null, line);
        // the progress bar under the current pill, in its own canvas (its fill grows every frame while playing): an ink track, a cream fill
        phRt = HudKit.Node(root, "Playhead", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, BarH));
        phRt.pivot = new Vector2(0f, 0.5f);
        HudKit.SubCanvas(phRt.gameObject, false);
        barArt = phRt.gameObject.AddComponent<InkPainter>(); barArt.raycastTarget = false;
        barArt.onPaint = p => { Rect r = p.Area; p.RoundRect(r, r.height * 0.5f, Comic.Ink); };
        var fill = HudKit.Hit(phRt, "Fill", new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(4f, BarH - 4f));
        fill.raycastTarget = false; fill.color = Comic.Cream;
        fillRt = fill.rectTransform; fillRt.pivot = new Vector2(0f, 0.5f);
        // the focus-loop badge: an ink loop around the looped columns' place + a ✕ sticker (its own group: stays bright while the rail is dimmed)
        focusRt = HudKit.Node(root, "FocusLoop", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 60f));
        focusGroup = focusRt.gameObject.AddComponent<CanvasGroup>(); focusGroup.ignoreParentGroups = true;
        focusArt = focusRt.gameObject.AddComponent<InkPainter>(); focusArt.raycastTarget = false; focusArt.onPaint = PaintFocus; focusArt.Boil = true;
        InkShape xb; InkPainter xa;
        focusX = HudKit.Sticker(focusRt, "FocusX", new Vector2(1f, 1f), new Vector2(-2f, -2f), new Vector2(28f, 28f), InkShape.Kind.Circle, Comic.Cream,
            p => { Vector2 c = p.Area.center; float r = 5f; p.Line(c + new Vector2(-r, -r), c + new Vector2(r, r), 2.6f, Comic.Ink, true); p.Line(c + new Vector2(-r, r), c + new Vector2(r, -r), 2.6f, Comic.Ink, true); },
            "end loop", () => FocusLoop.Dismiss(), out xb, out xa);
        xb.SetInk(1.6f, 2.4f); xb.ShadowOffset = new Vector2(2f, -2.5f);
        focusRt.gameObject.SetActive(false);
        Hints.Register("pills", root);
        Hints.Register("focusLoop", focusRt);
        Hints.Register("loop", loopRt);
    }

    // ------------------------------------------------------------------ layout
    Pill Take(int i)
    {
        while (pills.Count <= i)
        {
            var pl = new Pill();
            pl.rt = HudKit.Node(beadsRt, "Pill" + pills.Count, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, PillH));
            pl.art = pl.rt.gameObject.AddComponent<InkPainter>();
            var c = pl;
            pl.art.onPaint = p => PaintPill(p, c);
            pl.btn = HudKit.Control(pl.art, "go there", null, null, null, pl.art);
            pl.btn.onClick = () => GoTo(c, CellUnderPointer(c), true);
            pl.btn.onRightClick = () => GoTo(c, CellUnderPointer(c), false);
            pl.label = HudKit.Words(pl.rt, "Letter", "", 20f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
            pl.label.font = Comic.DigitFont; if (Comic.DigitFont != null) pl.label.fontSharedMaterial = Comic.DigitFont.material;
            pl.label.rectTransform.sizeDelta = new Vector2(36f, 22f);
            pills.Add(pl);
        }
        return pills[i];
    }

    void Hide(Pill pl)
    {
        if (pl.btn != null && pl.btn.Hover) pl.btn.OnPointerExit(null);
        HudKit.SetActive(pl.rt, false);
        pl.visible = false;
    }

    /// <summary>The colour of measure <paramref name="bar"/> of column <paramref name="col"/>: its chord grid's there (the column's first grid
    /// that holds the measure; a keyboard or stairs only when no chord grid does); the island it came from.</summary>
    static Color32 MeasureColour(SongManager sm, int col, int bar, out int island)
    {
        island = -1; int fallback = -1;
        int a = sm.ColumnFirst(col), n = sm.ColumnSize(col);
        for (int k = a; k >= 0 && k < a + n && k < sm.Islands.Count; k++)
        {
            var kb = sm.Islands[k];
            if (kb == null || kb.IsMoon || kb.IsPhrase) continue;
            int off = SongManager.OffsetBars(kb);
            if (bar < off || bar >= off + Mathf.Max(1, kb.bars)) continue;
            if (kb.IsKeyboard || kb.IsStairs) { if (fallback < 0) fallback = k; continue; }
            island = k; break;
        }
        if (island < 0) island = fallback >= 0 ? fallback : a;
        var src = island >= 0 && island < sm.Islands.Count ? sm.Islands[island] : null;
        if (src == null) return Comic.Cream;
        return Comic.Opaque(src.IsKeyboard ? new Color(1f, 0.97f, 0.9f) : (src.IsStairs ? IslandHeader.StairStone : src.chordColor));
    }

    void Rebuild()
    {
        dirty = false;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || sm.ColumnCount == 0)
        {
            for (int s = 0; s < pills.Count; s++) Hide(pills[s]);
            pillCount = 0; cols = new Col[0]; current = -1; contentW = 0f; scrollShown = scrollTo = 0f;
            line.Repaint();
            return;
        }
        int ncol = sm.ColumnCount;
        float bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        if (cols.Length != ncol) cols = new Col[ncol];
        for (int c = 0; c < ncol; c++)
        {
            cols[c] = new Col { start = sm.ColumnStart(c), len = Mathf.Max(0.001f, sm.ColumnLength(c)), bars = Mathf.Clamp(sm.ColumnBars(c), 1, 8), passes = Mathf.Clamp(sm.ColumnPasses(c), 1, 4) };
        }
        secStarts.Clear(); secStarts.AddRange(sm.SectionStarts());
        if (secStarts.Count == 0) secStarts.Add(0);
        pillCount = 0;
        for (int s = 0; s < secStarts.Count; s++)
        {
            int first = secStarts[s], last = Mathf.Min(s + 1 < secStarts.Count ? secStarts[s + 1] - 1 : ncol - 1, ncol - 1);
            if (first >= ncol) break;
            var pl = Take(pillCount);
            pl.index = pillCount++; pl.first = first; pl.last = last;
            pl.letter = SectionHeader.LetterOf(s) ?? ""; pl.tint = SectionPlinth.LetterTint(pl.letter);
            pl.beat0 = cols[first].start; pl.beat1 = cols[last].start + cols[last].len;
            pl.played = Mathf.Max(1, Mathf.RoundToInt((pl.beat1 - pl.beat0) / bpb));
            for (int c = first; c <= last; c++) cols[c].section = pl.index;
            if (pl.label.text != pl.letter) pl.label.text = pl.letter;
        }
        for (int s = pillCount; s < pills.Count; s++) Hide(pills[s]);
        // the widths: the time each section plays, a floor so its letter fits; never thinner — past that the row scrolls
        float avail = MaxWidth - (pillCount - 1) * PillGap, ppm = MeasureMax;
        for (int it = 0; it < 16; it++)
        {
            float fixedW = 0f; int free = 0;
            for (int s = 0; s < pillCount; s++) { if (pills[s].played * ppm < PillMinW) fixedW += PillMinW; else free += pills[s].played; }
            if (free == 0) break;
            float want = Mathf.Min(MeasureMax, (avail - fixedW) / free);
            if (want >= ppm - 0.005f) break;
            ppm = want;
        }
        ppm = Mathf.Max(ppm, PillMinW / 8f);
        float x = 0f;
        for (int s = 0; s < pillCount; s++) { var pl = pills[s]; pl.w = Mathf.Max(PillMinW, pl.played * ppm); pl.cx = x; x += pl.w + PillGap; }
        contentW = Mathf.Max(0f, x - PillGap);
        scrollTo = Mathf.Clamp(scrollTo, 0f, ScrollMax); scrollShown = Mathf.Clamp(scrollShown, 0f, ScrollMax);
        // the measures as cells, when each gets enough room
        cellsShown = ppm >= CellMin;
        for (int s = 0; s < pillCount && cellsShown; s++)
        {
            var pl = pills[s];
            if (pl.cellColour.Length != pl.played) { pl.cellColour = new Color32[pl.played]; pl.cellCol = new int[pl.played]; pl.cellIsland = new int[pl.played]; }
            int k = 0;
            for (int c = pl.first; c <= pl.last && k < pl.played; c++)
                for (int pass = 0; pass < cols[c].passes && k < pl.played; pass++)
                    for (int bar = 0; bar < cols[c].bars && k < pl.played; bar++, k++)
                    {
                        int island; pl.cellColour[k] = MeasureColour(sm, c, bar, out island);
                        pl.cellCol[k] = c; pl.cellIsland[k] = island;
                    }
            for (; k < pl.played; k++) { pl.cellColour[k] = pl.tint; pl.cellCol[k] = pl.last; pl.cellIsland[k] = -1; }
        }
        current = SectionAt(sm);
        Place();
        for (int s = 0; s < pillCount; s++) pills[s].art.Repaint();
        line.Repaint();
    }

    /// <summary>Places the pills (the scroll applied: pills outside the window hidden, the ones at its ends faded), their letters, their
    /// columns' spans, the line's ends and the curl.</summary>
    void Place()
    {
        float half = Mathf.Min(contentW, MaxWidth) * 0.5f, org = -half - scrollShown;
        bool scrolls = contentW > MaxWidth + 0.5f;
        for (int s = 0; s < pillCount; s++)
        {
            var pl = pills[s];
            pl.x = org + pl.cx;
            float a = 1f; bool vis = true;
            if (scrolls) { float inside = Mathf.Min(pl.x + pl.w + half, half - pl.x); vis = inside > 4f; a = Mathf.Clamp01(inside / 34f); }
            pl.alpha = a;
            if (!vis) Hide(pl);
            else
            {
                pl.visible = true; HudKit.SetActive(pl.rt, true);
                pl.rt.sizeDelta = new Vector2(pl.w, PillH); pl.rt.anchoredPosition = new Vector2(pl.x + pl.w * 0.5f, PillY);
                var tint = new Color(1f, 1f, 1f, a);
                if (pl.art.color != tint) pl.art.color = tint;
                PlaceLetter(pl);
            }
            float dur = Mathf.Max(0.001f, pl.beat1 - pl.beat0), inner = pl.w - 2f * CellInset;
            for (int c = pl.first; c <= pl.last && c < cols.Length; c++) { cols[c].x0 = pl.x + CellInset + (cols[c].start - pl.beat0) / dur * inner; cols[c].w = cols[c].len / dur * inner; }
        }
        if (pillCount > 0)
        {
            x0Line = (scrolls ? -half : pills[0].x) - 12f;
            x1Line = (scrolls ? half : pills[pillCount - 1].x + pills[pillCount - 1].w) + 8f;
        }
        loopRt.anchoredPosition = new Vector2(x1Line + 12f, 8f + PillY);
        ghostKey = int.MinValue; focusCol = -2;
    }

    /// <summary>A pill's letter: centred on it (above its cells when it shows them); the current pill's at full ink, the others' a step quieter.</summary>
    void PlaceLetter(Pill pl)
    {
        var l = pl.label;
        float size = cellsShown ? 18f : 22f, y = cellsShown ? (CellInset + CellH) * 0.5f + 0.5f : 0.5f;
        if (l.fontSize != size) l.fontSize = size;
        l.rectTransform.anchoredPosition = new Vector2(0f, y);
        float a = pl.alpha * (pl.index == current ? 1f : 0.72f);
        if (l.alpha != a) l.alpha = a;
    }

    int SectionAt(SongManager sm)
    {
        if (pillCount == 0 || cols.Length == 0) return -1;
        int col = Mathf.Clamp(GlobalClock.IsPlaying ? sm.LitColumn : sm.ActiveColumn(GlobalClock.SongBeat), 0, cols.Length - 1);
        return Mathf.Clamp(cols[col].section, 0, pillCount - 1);
    }

    // ------------------------------------------------------------------ painting
    static readonly List<Vector2> pts = new List<Vector2>(64);

    /// <summary>A pill: the section's colour with an ink rim (the current one at full strength on a hard shadow, the others a step quieter),
    /// and — when the song is small enough — its measures as flat cells in their chords' colours, a hairline between them.</summary>
    void PaintPill(InkPainter p, Pill pl)
    {
        if (pl.index >= pillCount) return;
        Rect a = p.Area;
        Rect r = new Rect(a.xMin + 1.5f, a.yMin + 1.5f, a.width - 3f, a.height - 3f);
        bool cur = pl.index == current;
        const float rad = 10f;
        if (cur) { p.Shift = new Vector2(2f, -3f); p.RoundRect(r, rad, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero; }
        Color ink = cur ? Comic.Ink : Comic.A(Comic.Ink, 0.7f);
        p.RoundRect(r, rad, cur ? pl.tint : Quiet(pl.tint), cur ? 2.6f : 2f, ink);
        if (!cellsShown || pl.played < 1 || pl.cellColour.Length < pl.played) return;
        float x0 = a.xMin + CellInset, x1 = a.xMax - CellInset, y0 = a.yMin + CellInset, y1 = y0 + CellH, cw = (x1 - x0) / pl.played;
        p.RoundRect(Rect.MinMaxRect(x0 - 1.4f, y0 - 1.4f, x1 + 1.4f, y1 + 1.4f), 3f, ink);   // (the ink shows between the cells: the hairlines)
        for (int k = 0; k < pl.played; k++)
        {
            Color c = pl.cellColour[k]; if (!cur) c = Quiet(c);
            float xa = x0 + k * cw + (k == 0 ? 0f : 0.6f), xb = x0 + (k + 1) * cw - (k == pl.played - 1 ? 0f : 0.6f);
            p.Quad(new Vector2(xa, y0), new Vector2(xb, y0), new Vector2(xb, y1), new Vector2(xa, y1), c);
        }
    }

    void PaintLine(InkPainter p)
    {
        PaintCount++;
        if (pillCount == 0) return;
        // the line the pills sit on (a hand-inked line with a 1 px wobble)
        pts.Clear();
        int n = Mathf.Max(1, Mathf.CeilToInt((x1Line - x0Line) / 14f));
        for (int i = 0; i <= n; i++) pts.Add(new Vector2(Mathf.Lerp(x0Line, x1Line, i / (float)n), PillY + p.Still(i) * 1f));
        p.Stroke(pts, pts.Count, 3.2f, Comic.Ink, false, 0f, 0f, 2.4f, true);
        // the loop curl at the right end (solid when the song loops, a dashed ghost when it does not)
        bool on = GlobalClock.LoopSong;
        pts.Clear();
        Vector2 c = new Vector2(x1Line + 12f, 9f + PillY);
        InkPainter.ArcPoints(pts, c, 12f, 9f, -95f, 170f, 22);
        pts.Insert(0, new Vector2(x1Line, PillY));
        Vector2 end = pts[pts.Count - 1], dir = (end - pts[pts.Count - 3]).normalized;
        float al = on ? 1f : 0.45f;
        if (on) { p.Shift = new Vector2(2f, -2.5f); p.Stroke(pts, pts.Count, 3.4f, Comic.A(Comic.Ink, 0.5f), false, 0f, 0f, -1f, true); p.Shift = Vector2.zero; }
        p.Stroke(pts, pts.Count, 3.2f, Comic.A(Comic.Ink, al), false, on ? 0f : 4f, on ? 0f : 3.5f, -1f, true);
        p.Head(end + dir * 4f, dir, 8f, 10f, Comic.A(on ? Comic.Cream : Comic.Ink, al), 1.6f, Comic.A(Comic.Ink, al));
        // a dragged card's slot (dotted)
        if (ghostOn) p.RoundRect(ghostRect, 6f, Comic.A(Comic.Cream, 0.22f), 2f, Comic.Ink, 3.5f, 2.6f);
    }

    void PaintFocus(InkPainter p)
    {
        // the loop curl motif around the looped place: an ellipse drawn in one stroke that overshoots its start, with a little arrowhead
        Rect r = p.Area;
        Vector2 c = r.center;
        float rx = r.width * 0.5f - 7f, ry = r.height * 0.5f - 6f;
        pts.Clear();
        InkPainter.ArcPoints(pts, c, rx, ry, 110f, 110f + 380f, 48);
        for (int i = 0; i < pts.Count; i++) pts[i] += p.Jit(i, 0.5f);
        p.Stroke(pts, pts.Count, 2.6f, Comic.Cream, false, 0f, 0f, 1.6f, true);
        p.Stroke(pts, pts.Count, 1.6f, Comic.Ink, false, 0f, 0f, 1.0f, true);
        Vector2 end = pts[pts.Count - 1], dir = (end - pts[pts.Count - 3]).normalized;
        p.Head(end + dir * 3f, dir, 7f, 8f, Comic.Cream, 1.3f, Comic.Ink);
    }

    // ------------------------------------------------------------------ interaction
    /// <summary>The cell of <paramref name="pl"/> under the pointer (-1 when the pills show no cells).</summary>
    int CellUnderPointer(Pill pl)
    {
        if (!cellsShown || pl.played < 1) return -1;
        Vector2 lp;
        if (!HudKit.Local(pl.rt, Input.mousePosition, out lp)) return -1;
        float inner = pl.w - 2f * CellInset;
        return Mathf.Clamp(Mathf.FloorToInt((lp.x + pl.w * 0.5f - CellInset) / Mathf.Max(1f, inner) * pl.played), 0, pl.played - 1);
    }

    /// <summary>Go there: the section's first column (a cell: that measure's column). A click frames it, picks its grid (UIManager.SelectMeasure:
    /// Play starts there) and moves the song position to it; a right-click only moves the song position.</summary>
    void GoTo(Pill pl, int cell, bool pick)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || pl.first >= sm.ColumnCount) return;
        bool onCell = cellsShown && cell >= 0 && cell < pl.cellCol.Length;
        int col = onCell ? pl.cellCol[cell] : pl.first;
        if (pick)
        {
            int island = onCell ? pl.cellIsland[cell] : -1;
            if (island < 0 || island >= sm.Islands.Count) { var a = sm.AnchorOf(col); island = a != null ? sm.Islands.IndexOf(a) : -1; }
            if (island >= 0 && UIManager.I != null) UIManager.I.SelectMeasure(island, true);
            Bounds b;
            if (!onCell && OrbitCamera.I != null && SectionHeader.SectionBounds(pl.index, out b)) OrbitCamera.I.FrameBounds(b);
        }
        GlobalClock.Seek(sm.ColumnStart(col));
        SequenceMaster.ResetAllCubes();
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (line == null || phRt == null) return;   // not built (or a domain reload dropped the references)
        Subscribe();
        var sm = SongManager.I;
        bool has = sm != null && sm.HasSong;
        if (group != null) { float a = !has ? 0f : (dimmed ? 0.5f : 1f); if (group.alpha != a) group.alpha = a; }
        if (!has) { if (pillCount > 0) Rebuild(); HudKit.SetActive(phRt, false); HudKit.SetActive(focusRt, false); return; }
        bool twos = HudKit.TwosTick != tick;
        if (twos) { tick = HudKit.TwosTick; WatchLetters(); }
        if (dirty || pillCount != sm.SectionCount) Rebuild();
        if (loopShown != GlobalClock.LoopSong) { loopShown = GlobalClock.LoopSong; line.Repaint(); }
        UpdateCurrent(sm);
        if (twos) StepTwos();
        UpdateBar(sm);
        UpdateFocus();
        UpdateGhost(sm);
    }

    /// <summary>The plinths letter their sections a moment after the song is rebuilt (no event says so): on twos, a letter the rail shows that
    /// is no longer its plinth's asks for a rebuild.</summary>
    void WatchLetters()
    {
        if (dirty || SectionPlinth.I == null || SectionPlinth.Count != pillCount) return;
        for (int s = 0; s < pillCount; s++) { string l = SectionPlinth.LetterOf(s); if (!string.IsNullOrEmpty(l) && l != pills[s].letter) { dirty = true; return; } }
    }

    /// <summary>The pill at full strength follows the song position; a scrolling row brings it into view.</summary>
    void UpdateCurrent(SongManager sm)
    {
        int now = SectionAt(sm);
        if (now != current)
        {
            int old = current; current = now;
            if (Ok(old)) { pills[old].art.Repaint(); if (pills[old].visible) PlaceLetter(pills[old]); }
            if (Ok(now)) { pills[now].art.Repaint(); if (pills[now].visible) PlaceLetter(pills[now]); }
        }
        if (Ok(current) && contentW > MaxWidth + 0.5f)
        {
            var pl = pills[current]; float left = pl.cx - scrollTo, right = left + pl.w;
            if (left < 40f || right > MaxWidth - 40f) scrollTo = Mathf.Clamp(pl.cx + pl.w * 0.5f - MaxWidth * 0.5f, 0f, ScrollMax);
        }
    }

    /// <summary>On twos: the scroll eases to its target, a kicked bar settles.</summary>
    void StepTwos()
    {
        if (Mathf.Abs(scrollShown - scrollTo) > 0.01f)
        {
            scrollShown = Mathf.Abs(scrollTo - scrollShown) < 1.5f ? scrollTo : Mathf.Lerp(scrollShown, scrollTo, 0.5f);
            Place();
            line.Repaint();
        }
        if (kickStep < 3)
        {
            float k = kickStep == 0 ? 1.35f : (kickStep == 1 ? 1.15f : 1f);
            phRt.localScale = new Vector3(1f, k, 1f);
            kickStep++;
        }
    }

    /// <summary>The inspector is open: the rail dims to 50 % (the focus badge stays bright).</summary>
    public void SetDimmed(bool on)
    {
        dimmed = on;
        if (group != null) group.blocksRaycasts = !on;
    }

    /// <summary>The progress bar: its track spans the current pill (just under it), its fill grows with the song, linear in the section's time
    /// (so the song position never goes back on the rail while the song goes on).</summary>
    void UpdateBar(SongManager sm)
    {
        if (!Ok(current)) { HudKit.SetActive(phRt, false); return; }
        var pl = pills[current];
        float beat = GlobalClock.SongBeat, bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float u = Mathf.Clamp01((beat - pl.beat0) / Mathf.Max(0.001f, pl.beat1 - pl.beat0));
        float x0 = pl.x + CellInset, w = pl.w - 2f * CellInset;
        barEndX = x0 + u * w;
        HudKit.SetActive(phRt, pl.visible);
        if (barOf != current || barX != x0 || barW != w)
        {
            barOf = current; barX = x0; barW = w;
            phRt.sizeDelta = new Vector2(w, BarH); phRt.anchoredPosition = new Vector2(x0, PillY - PillH * 0.5f - BarDrop);
        }
        float fw = Mathf.Max(0f, u * (w - 4f));
        if (Mathf.Abs(fw - fillW) > 0.05f) { fillW = fw; fillRt.sizeDelta = new Vector2(fw, BarH - 4f); }
        // (the pass of its column the song is in: tests)
        int col = Mathf.Clamp(GlobalClock.IsPlaying ? sm.LitColumn : sm.ActiveColumn(beat), 0, cols.Length - 1);
        float passLen = cols[col].bars * bpb;
        PlayheadPass = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, beat - cols[col].start) / passLen + 1e-4f), 0, cols[col].passes - 1);
    }
    int barOf = -1; float barX = float.NaN, barW = float.NaN, fillW = -1f;

    /// <summary>The focus loop's badge: an ink loop around the looped columns' place on the row, its ✕ on the corner.</summary>
    void UpdateFocus()
    {
        bool want = FocusLoop.Active && FocusLoop.Column >= 0 && FocusLoop.Column < cols.Length;
        if (want != focusShown) { focusShown = want; HudKit.SetActive(focusRt, want); if (want) focusRt.SetAsLastSibling(); focusCol = -2; }
        if (!want) return;
        int last = Mathf.Clamp(FocusLoop.LastColumn, FocusLoop.Column, cols.Length - 1);   // (a loop may cover a range of columns)
        if (focusCol == FocusLoop.Column && focusLast == last) return;
        focusCol = FocusLoop.Column; focusLast = last;
        float xa = cols[focusCol].x0, xb = cols[last].x0 + cols[last].w;
        focusRt.sizeDelta = new Vector2(Mathf.Max(30f, xb - xa) + 34f, PillH + 34f);
        focusRt.anchoredPosition = new Vector2((xa + xb) * 0.5f, PillY - 3f);
        focusArt.Repaint();
    }

    /// <summary>While a deck card is dragged: a dotted slot where it would land on the row (around the column it would join, else at the place
    /// between columns it would open).</summary>
    void UpdateGhost(SongManager sm)
    {
        var tray = IslandTray.I;
        bool drag = tray != null && tray.Dragging && tray.Ghost != null;
        int key = int.MinValue; Rect r = default(Rect); string kind = "";
        GhostColumn = -1;
        if (drag && cols.Length > 0)
        {
            if (modeInsert == -9) { modeInsert = ModeOf("Insert"); modeMerge = ModeOf("Merge"); modeStack = ModeOf("Stack"); modeBlocked = ModeOf("Blocked"); }
            var d = tray.Ghost.Snapshot();
            int m = (int)d.mode;
            if (d.visible && m != modeBlocked)
            {
                float at; bool around = false; int ci;
                if (m == modeMerge && d.target != null)
                {
                    ci = Mathf.Clamp(d.target.column, 0, cols.Length - 1);
                    at = d.east ? cols[ci].x0 + cols[ci].w : cols[ci].x0; kind = "merge"; GhostColumn = ci + (d.east ? 1 : 0);
                }
                else if (m == modeStack && d.column >= 0 && d.column < cols.Length)
                {
                    ci = d.column; var anchor = sm.AnchorOf(ci);
                    at = cols[ci].x0; around = true; kind = anchor == null || d.pz > anchor.pz ? "above" : "below"; GhostColumn = ci;
                }
                else if (m == modeInsert && d.insertAt >= 0)
                {
                    ci = Mathf.Clamp(d.insertAt, 0, cols.Length);
                    at = ci < cols.Length ? cols[ci].x0 : cols[cols.Length - 1].x0 + cols[cols.Length - 1].w; kind = "insert"; GhostColumn = ci;
                    ci = Mathf.Min(ci, cols.Length - 1);
                }
                else
                {
                    ci = cols.Length - 1;
                    at = cols[ci].x0 + cols[ci].w + CellInset + PillGap * 0.5f + 8f; kind = "end"; GhostColumn = cols.Length;
                }
                r = around ? new Rect(at - 3f, PillY - PillH * 0.5f - 4f, cols[ci].w + 6f, PillH + 8f) : new Rect(at - 8f, PillY - PillH * 0.5f - 4f, 16f, PillH + 8f);
                key = Mathf.RoundToInt(r.x * 4f) * 7919 + Mathf.RoundToInt(r.width * 4f);
            }
        }
        GhostKind = kind;
        bool on = key != int.MinValue;
        if (on == ghostOn && key == ghostKey) return;
        ghostOn = on; ghostKey = key; ghostRect = r;
        line.Repaint();
    }

    static int ModeOf(string name)
    {
        return Enum.IsDefined(typeof(IslandGhost.Mode), name) ? (int)Enum.Parse(typeof(IslandGhost.Mode), name) : -1;
    }
}
