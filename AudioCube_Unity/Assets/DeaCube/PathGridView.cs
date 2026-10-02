using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SPEC v3 §2.4 + v4 §6: the inspected cube's island as a flat, editable grid inside the inspector card, drawn in the v4 ink language. The
/// plate is the island itself (its chord colour, an ink rim, a darker lip for thickness); cells = the island grid (x left → right, row z bottom
/// → top; Moons 6 x 4) as little inked tiles in each tile's own colour (pitch brightness included) with a lip (v5: taller for higher notes, like the
/// world's staircase; chord cells carry no glyph any more — the kit pictograms on Moons / in drum clothes). The path is one hand-inked line (an ink stroke under the cube's colour, round joins) with a chevron at each
/// midpoint; Loop = a dashed closing segment, PingPong = double chevrons at both ends, Once = an end bar. Beads are sized by their note's
/// length (AudioCube.SizeOf of DurBeats: fast notes small, long notes fat), rests hollow, ghosts small, accents large, a glyph for ratchet /
/// coin / tie / lift; node 0 wears an open cream ring — the path's handle: drag the ring to move the whole path (the old shift arrows);
/// repeated cells fan their beads out; the bead of the node the cube is on glows and pulses on every landing; the note selected in the
/// rhythm strip (<see cref="HighlightNode"/>) wears an open ink ring.
/// Gestures (each committed change = cube setters, then exactly one History.Push on release; notes preview through TileInteraction.PlayPreview):
/// click an empty cell = append · drag from the LAST bead = extend (each entered cell, consecutive duplicates ignored, skipped cells filled
/// along the grid line) · drag another bead = move that node (its sticker moves with it) · drag node 0's ring = shift the whole path (refused
/// cells shake) · click a bead = rest on/off · hold a bead 0.5 s = sticker popover · right-click a bead = remove it (one node always stays) ·
/// pencil armed: the next press clears the path to that cell and the drag appends. Path tools (called by the card): Reverse, Shift,
/// TogglePencil. mods / rests / gridX / gridZ (and durs, R) stay aligned through the AudioCube node setters.
/// Fixes (v3 review): the hold is 0.5 s and a drag after the popover opened closes it and moves / extends as usual (F13); the drag threshold
/// is the DPI-aware OrbitCamera.DragThresholdPx in screen pixels (F17); a gesture cut short by the card closing is committed when it changed
/// the path (F18 / S5).
/// v6 (H): a KEYBOARD cube (kind-2 island) shows one row of piano keys instead of cells — white keys long and ivory, black keys short, dark and
/// set back (drawn over them), the island's own key colours — and the same gestures work on keys (a click on a free key appends it, a drag
/// from the last bead extends along the keys, a bead click toggles its rest); a repeated note's beads stack along its key so each stays clickable.
/// v7 (D): a grid larger than <see cref="MaxGridW"/> x <see cref="MaxGridH"/> (a melody phrase: up to 64 time cells x 15 rows) shrinks its cells
/// (and beads) to fit; on a PHRASE or STAIRS the path is derived (time order / the run), so the path gestures (append, extend, move, shift,
/// pencil) are off there — bead clicks (rest) and stickers stay. §19.1 "finished paths keep the cap": an append / extend that would pass the
/// grid's capacity (PathManager.CapacityOf) is refused (a thud, the grid shakes) and a small "capacity reached" chip offers EXPAND
/// (SongOps.ExpandGrid: + one measure, one History entry).
/// </summary>
public class PathGridView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public const float Cell = 58f, Gap = 6f, Pad = 12f, DragPx = 6f, HoldSeconds = 0.5f, FanR = 12f, BeadPx = 21f, RingR = 21f;
    /// <summary>v7: the largest grid drawn at full cell size (px); bigger grids scale their cells down to fit.</summary>
    public const float MaxGridW = 640f, MaxGridH = 400f;
    float cell = Cell, gap = Gap, cellK = 1f;   // v7: the cell size in use (Cell x cellK)
    /// <summary>v7: the scale of the cells (1 = Cell px; a long phrase is smaller).</summary>
    public float CellScale => cellK;
    /// <summary>v7: path gestures are off on this island (a phrase's path is its notes in time order, a runner's is its run).</summary>
    public bool PathLocked => island != null && (island.IsPhrase || island.IsStairs);
    /// <summary>v7: appends / extends refused by the grid's capacity (tests).</summary>
    public int CapacityRefusals { get; private set; }
    /// <summary>Popover order = sticker index (0 none … 8 lift).</summary>
    public static readonly string[] StickerIcons = { "dot", "rest", "ghost", "accent", "ratchet2", "ratchet3", "coin", "tie", "lift" };
    static readonly Color Ink = Comic.Ink;

    /// <summary>A committed grid gesture or path tool (after its History.Push).</summary>
    public static event Action<AudioCube> OnCommitted;
    /// <summary>True while a sticker popover of any grid is open (the inspector lets Esc close it first).</summary>
    public static bool AnyPopoverOpen => openPopovers > 0;
    static int openPopovers;

    public AudioCube Cube => cube;
    public KeyBlock Island => island;
    public int Cols => cols;
    public int Rows => rows;
    public bool PencilArmed => pencil;
    public PopRing Popover => popover;
    /// <summary>Committed edits since the component was created (tests).</summary>
    public int CommitCount { get; private set; }
    /// <summary>Bead centres (grid-local px, fan-out applied), one per node.</summary>
    public IReadOnlyList<Vector2> BeadPositions => beadPos;
    /// <summary>Bead diameters (px, before the pulse), one per node: the note's length makes it bigger or smaller.</summary>
    public IReadOnlyList<float> BeadSizes => beadBase.GetRange(0, Mathf.Min(beadBase.Count, cube != null ? cube.nodes.Count : 0));   // the list only grows: expose the current cube's beads
    /// <summary>The node whose bead glows as "the cube is here" (-1 none).</summary>
    public int CurrentNode => curNode;
    /// <summary>The node selected in the rhythm strip (-1 none): its bead wears an open ink ring.</summary>
    public int HighlightNode { get => highlight; set { if (highlight != value) { highlight = value; PlaceHighlight(); } } }
    /// <summary>A start-ring drag is moving the whole path.</summary>
    public bool Shifting => g == G.Shift;

    AudioCube cube; KeyBlock island; int cols, rows;
    RectTransform rt, cellsRoot, lineRoot, glowRoot, beadRoot, topRoot;
    InkShape plate; LayoutElement layout;
    float gridW, gridH;
    readonly List<InkShape> cellImgs = new List<InkShape>();
    readonly List<Image> cellGlyphs = new List<Image>();
    readonly List<TileInteraction> cellTiles = new List<TileInteraction>();
    readonly List<string> cellIcons = new List<string>();
    int cellLookSig = int.MinValue, builtSig = int.MinValue;
    InkPath pathInk, startRing, selRing;
    readonly List<InkShape> beads = new List<InkShape>();
    readonly List<Image> glyphs = new List<Image>();
    /// <summary>v9 (N): the ▲ / ▼ nudge mark beside a bead — one slot per bead, LAZY: null until that slot first shows a nudge (a plain path
    /// builds no Image for it), disabled again when the nudge returns to 0.</summary>
    readonly List<Image> nudgeGlyphs = new List<Image>();
    /// <summary>v9 (N): px a bead sits higher / lower per nudge step.</summary>
    public const float NudgePx = 5f;
    int beadsUsed;
    Image glowImg, hoverRing;
    readonly List<Vector2> beadPos = new List<Vector2>();
    readonly List<int> beadGX = new List<int>(), beadGZ = new List<int>();
    readonly List<float> beadBase = new List<float>(), beadPulse = new List<float>(), beadPop = new List<float>();
    int curNode = -1, highlight = -1; Vector2 glowPos; float glowA, landPulse; Color markColor = Color.white;
    Vector2 nudge; float nudgeT = 9f; float shakeT = 9f;
    readonly Dictionary<int, int> fanTotal = new Dictionary<int, int>(), fanSeen = new Dictionary<int, int>();
    // gesture state
    enum G { None, Append, Extend, MoveNode, Bead, Pencil, Right, Shift }
    G g; int pressNode = -1; Vector2 pressLocal, lastLocal; float pressTime; bool moved, changed, popped, pressing, simPress, pencil;
    int shiftX, shiftZ; bool ringHover, cursorHeld;
    PopRing popover; int popoverClosedFrame = -9;
    int hoverBead = -1; int hoverX = -1, hoverZ = -1;

    /// <summary>Builds the view (plate + layers) under <paramref name="parent"/>.</summary>
    public static PathGridView Create(Transform parent)
    {
        var go = UIKit.Obj("PathGrid", parent);
        var v = go.AddComponent<PathGridView>();
        v.Build();
        return v;
    }

    void Build()
    {
        rt = (RectTransform)transform;
        plate = gameObject.AddComponent<InkShape>();   // the island: chord colour, ink rim, a darker lip under it
        plate.raycastTarget = true; plate.Radius = 16f; plate.Wobble = 1.1f; plate.SetInk(2.2f, 3.4f); plate.RotJitter = 0.5f;
        plate.ShadowOffset = new Vector2(0f, -8f); plate.Seed = 1409;
        plate.color = new Color(0.3f, 0.26f, 0.44f, 1f);
        layout = UIKit.Fixed(gameObject, 200f, 200f);
        cellsRoot = Layer("Cells"); lineRoot = Layer("Lines"); glowRoot = Layer("Glow"); beadRoot = Layer("Beads"); topRoot = Layer("Top");
        pathInk = InkPath.Create(lineRoot, "Path", new Vector2(10f, 10f));
        pathInk.UnitPx = 1f; pathInk.Wobble = 0.7f; pathInk.Seed = 1511;
        glowImg = UIKit.Image(glowRoot, "Now", "white", new Color(1f, 1f, 1f, 0f), new Vector2(54f, 54f));
        glowImg.sprite = Comic.RingSprite;   // a flat inked ring marks the node the cube is on
        Centre(glowImg.rectTransform, Vector2.zero, new Vector2(54f, 54f));
        startRing = InkPath.Create(topRoot, "Start", new Vector2(56f, 56f));
        startRing.UnitPx = 1f; startRing.Wobble = 0.5f; startRing.Seed = 1601;
        startRing.Arc(Ink, 6.5f, Vector2.zero, RingR, 118f, 118f + 322f, 26).Arc(Comic.Cream, 3.2f, Vector2.zero, RingR, 118f, 118f + 322f, 26);
        startRing.gameObject.SetActive(false);
        selRing = InkPath.Create(topRoot, "Selected", new Vector2(64f, 64f));
        selRing.UnitPx = 1f; selRing.Wobble = 0.6f; selRing.Seed = 1607;
        selRing.Arc(Comic.Cream, 7f, Vector2.zero, 25f, -52f, -52f + 318f, 28).Arc(Ink, 3.4f, Vector2.zero, 25f, -52f, -52f + 318f, 28);
        selRing.gameObject.SetActive(false);
        hoverRing = UIKit.Image(topRoot, "Hover", "white", Palette.A(Comic.Cream, 0f), new Vector2(cell + 8f, cell + 8f));
        hoverRing.sprite = Comic.SelectSprite;
        Centre(hoverRing.rectTransform, Vector2.zero, new Vector2(cell + 8f, cell + 8f));
        Resize(1, 1);
    }

    RectTransform Layer(string name)
    {
        var go = UIKit.Obj(name, transform);
        var r = UIKit.RT(go);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = Vector2.zero; r.sizeDelta = Vector2.zero;
        return r;
    }

    static void Centre(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
    }

    void Resize(int c, int r)
    {
        // v7: a big grid (a long phrase) shrinks its cells to fit MaxGridW x MaxGridH
        float fw = c * Cell + Mathf.Max(0, c - 1) * Gap, fh = r * Cell + Mathf.Max(0, r - 1) * Gap;
        cellK = keys ? 1f : Mathf.Min(1f, Mathf.Min(MaxGridW / Mathf.Max(1f, fw), MaxGridH / Mathf.Max(1f, fh)));
        cell = Cell * cellK; gap = Gap * cellK;
        gridW = c * cell + Mathf.Max(0, c - 1) * gap;
        gridH = r * cell + Mathf.Max(0, r - 1) * gap;
        if (keys) { int w = 0; for (int k = 0; k < keyBlack.Length; k++) if (!keyBlack[k]) w++; gridW = w * WhiteW + Mathf.Max(0, w - 1) * KeyGapPx; gridH = WhiteH; }
        rt.sizeDelta = new Vector2(gridW + 2f * Pad, gridH + 2f * Pad);
        layout.preferredWidth = layout.minWidth = gridW + 2f * Pad;
        layout.preferredHeight = layout.minHeight = gridH + 2f * Pad;
        if (pathInk != null) pathInk.rectTransform.sizeDelta = rt.sizeDelta;
    }

    /// <summary>The glyph drawn on cell (x, z): the tile's kit pictogram in drum clothes (v5: chord cells have none; null = none / outside).</summary>
    public string CellGlyph(int x, int z) { int i = z * cols + x; return x >= 0 && z >= 0 && x < cols && z < rows && i < cellIcons.Count ? cellIcons[i] : null; }

    /// <summary>Grid-local centre of cell (x, z) (px, relative to the view's centre). v6: on a keyboard, where key x's bead sits (a white key's
    /// front end, a black key's lower end).</summary>
    public Vector2 CellPos(int x, int z)
    {
        if (keys && x >= 0 && x < keyRect.Length) return keyBead[x];
        return new Vector2(-gridW * 0.5f + cell * 0.5f + x * (cell + gap), -gridH * 0.5f + cell * 0.5f + z * (cell + gap));
    }

    // ---- v6: the keyboard layout (one row of piano keys: white keys side by side, black keys over their boundaries, set back)
    public const float WhiteW = 24f, WhiteH = 108f, BlackW = 15f, BlackH = 62f, KeyGapPx = 2f, KeyBeadStep = 15f;
    bool keys;
    bool[] keyBlack = new bool[0];
    Rect[] keyRect = new Rect[0];
    Vector2[] keyBead = new Vector2[0];
    /// <summary>The view shows a keyboard's keys (tests).</summary>
    public bool IsKeys => keys;
    /// <summary>Grid-local rect of key <paramref name="k"/> (empty when the view is not a keyboard; tests).</summary>
    public Rect KeyRect(int k) => keys && k >= 0 && k < keyRect.Length ? keyRect[k] : new Rect();

    void LayoutKeys()
    {
        int n = island != null ? island.cols : 0;
        keyBlack = new bool[n]; keyRect = new Rect[n]; keyBead = new Vector2[n];
        int low = island != null && island.GetTile(0, 0) != null ? island.GetTile(0, 0).midi : 60;
        int whites = 0;
        for (int k = 0; k < n; k++) { int pc = ((low + k) % 12 + 12) % 12; keyBlack[k] = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10; if (!keyBlack[k]) whites++; }
        gridW = whites * WhiteW + Mathf.Max(0, whites - 1) * KeyGapPx; gridH = WhiteH;
        int wi = 0;
        for (int k = 0; k < n; k++)
        {
            if (!keyBlack[k])
            {
                float x0 = -gridW * 0.5f + wi * (WhiteW + KeyGapPx);
                keyRect[k] = new Rect(x0, -gridH * 0.5f, WhiteW, WhiteH);
                keyBead[k] = new Vector2(x0 + WhiteW * 0.5f, -gridH * 0.5f + 17f);
                wi++;
            }
            else
            {
                float cx = -gridW * 0.5f + wi * (WhiteW + KeyGapPx) - KeyGapPx * 0.5f;   // on the boundary before the next white key
                cx = Mathf.Clamp(cx, -gridW * 0.5f + BlackW * 0.5f, gridW * 0.5f - BlackW * 0.5f);
                keyRect[k] = new Rect(cx - BlackW * 0.5f, gridH * 0.5f - BlackH, BlackW, BlackH);
                keyBead[k] = new Vector2(cx, gridH * 0.5f - BlackH + 15f);
            }
        }
    }

    /// <summary>The key under a grid-local point (a black key wins where it covers a white one); -1 off the keys.</summary>
    int KeyAt(Vector2 lp, float inset)
    {
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < keyRect.Length; k++)
            {
                if (keyBlack[k] != (pass == 0)) continue;
                Rect r = keyRect[k];
                float ix = r.width * inset, iy = r.height * inset;
                if (lp.x >= r.xMin + ix && lp.x <= r.xMax - ix && lp.y >= r.yMin + iy && lp.y <= r.yMax - iy) return k;
            }
        return -1;
    }

    /// <summary>A tile's colour on the flat views (grid cell, strip block): its own colour, brighter for higher pitches within its island
    /// (<paramref name="lo"/> .. <paramref name="hi"/> = the island's midi range; Moons keep one brightness). v5: the tile's own colour already
    /// carries the wider PitchLook ramp, so the extra flat-view ramp is gentler.</summary>
    public static Color TileColor(TileInteraction t, int lo, int hi, bool moon)
    {
        if (t == null) return new Color(0.8f, 0.78f, 0.9f);
        float pitch = hi > lo && !moon ? (t.midi - lo) / (float)(hi - lo) : 0.5f;
        Color c = Palette.Mul(t.BaseColor, Mathf.Lerp(0.9f, 1.06f, pitch)); c.a = 1f;
        return c;
    }

    /// <summary>v5: the cell's lip (px) for a pitch rank 0..1: higher notes stand taller, like the world's staircase.</summary>
    public static float LipPx(float pitch01) => 3f + 5f * Mathf.Clamp01(pitch01);
    /// <summary>The kit pictogram's size on a cell (px).</summary>
    public const float GlyphPx = 34f;

    // ------------------------------------------------------------------ binding and drawing
    /// <summary>Shows <paramref name="c"/>'s island and path (null clears). Cheap when nothing changed.</summary>
    public void Bind(AudioCube c)
    {
        if (c != cube) { cube = c; pencil = false; ClosePopover(); g = G.None; pressing = false; curNode = -1; glowA = 0f; highlight = -1; ShowCapChip(false); }
        var isl = c != null ? c.Island : null;
        if (isl != island || (isl != null && (cols != isl.cols || rows != isl.rows)))
        {
            island = isl;
            BuildCells();
            builtSig = int.MinValue;
        }
        RefreshCells(false);
        EnsureOverlay();
    }

    void BuildCells()
    {
        foreach (var im in cellImgs) if (im != null) Destroy(im.gameObject);
        cellImgs.Clear(); cellGlyphs.Clear(); cellTiles.Clear(); cellIcons.Clear();
        cols = island != null ? Mathf.Max(1, island.cols) : 1;
        rows = island != null ? Mathf.Max(1, island.rows) : 1;
        keys = island != null && island.IsKeyboard;
        if (keys) LayoutKeys();
        Resize(cols, rows);
        cellLookSig = int.MinValue;
        if (island == null) return;
        if (keys)
        {
            // v6: white keys first, then the black keys over them (cells are indexed by key)
            var order = new List<int>(cols);
            for (int k = 0; k < cols; k++) if (!keyBlack[k]) order.Add(k);
            for (int k = 0; k < cols; k++) if (keyBlack[k]) order.Add(k);
            var shapes = new InkShape[cols]; var gls = new Image[cols];
            foreach (int k in order)
            {
                Rect r = keyRect[k];
                var cell = InkShape.Create(cellsRoot, "Key" + k, InkShape.Kind.RoundRect, Color.white, r.size);
                cell.raycastTarget = false; cell.Radius = keyBlack[k] ? 4f : 6f; cell.Wobble = 0.6f; cell.SetInk(1.4f, 2f); cell.RotJitter = 0.4f;
                cell.ShadowOffset = new Vector2(0f, -3f);
                Centre(cell.rectTransform, r.center, r.size);
                cell.transform.SetAsLastSibling();
                var gl = UIKit.Image(cell.transform, "Glyph", "dot", Color.clear, new Vector2(14f, 14f));
                Centre(gl.rectTransform, keyBead[k] - r.center + new Vector2(0f, 20f), new Vector2(14f, 14f));
                gl.raycastTarget = false;
                shapes[k] = cell; gls[k] = gl;
            }
            for (int k = 0; k < cols; k++) { cellImgs.Add(shapes[k]); cellGlyphs.Add(gls[k]); cellTiles.Add(island.GetTile(k, 0)); cellIcons.Add(null); }
            return;
        }
        for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                var cell = InkShape.Create(cellsRoot, "Cell" + x + "_" + z, InkShape.Kind.RoundRect, Color.white, new Vector2(this.cell, this.cell));
                cell.raycastTarget = false; cell.Radius = 10f; cell.Wobble = 0.9f; cell.SetInk(1.4f, 2.2f); cell.RotJitter = 1.2f;
                cell.ShadowOffset = new Vector2(0f, -5f);   // the tile's lip (a darker copy under it)
                Centre(cell.rectTransform, CellPos(x, z), new Vector2(this.cell, this.cell - 3f * cellK));
                cell.transform.SetAsFirstSibling();   // v5: back rows draw first, so a front cell covers the taller lip of the cell behind it
                var gl = UIKit.Image(cell.transform, "Glyph", "dot", Color.clear, new Vector2(GlyphPx, GlyphPx));
                Centre(gl.rectTransform, new Vector2(0f, 1f), new Vector2(GlyphPx, GlyphPx));
                gl.raycastTarget = false;
                cellImgs.Add(cell); cellGlyphs.Add(gl); cellTiles.Add(island.GetTile(x, z)); cellIcons.Add(null);
            }
    }

    /// <summary>Cell colours and glyphs follow the tiles (drum clothes, mood rebuilds of the same island object).</summary>
    void RefreshCells(bool force)
    {
        if (island == null) return;
        int sig = 17;
        unchecked { for (int i = 0; i < cellTiles.Count; i++) { var t = cellTiles[i]; if (t == null) continue; sig = sig * 31 + (t.KitLook ? 1 : 0); sig = sig * 31 + t.BaseColor.GetHashCode(); } sig = sig * 31 + island.chordColor.GetHashCode(); }
        if (!force && sig == cellLookSig) return;
        cellLookSig = sig;
        Color pc = keys ? island.PlatformColor : Color.Lerp(new Color(0.2f, 0.17f, 0.32f), island.chordColor, island.IsMoon ? 0.3f : 0.55f);
        plate.color = Comic.Opaque(Palette.Mul(pc, 0.7f));
        plate.ShadowColor = Comic.Opaque(Palette.Mul(pc, 0.36f));
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (var t in cellTiles) if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); }
        for (int i = 0; i < cellImgs.Count; i++)
        {
            var t = cellTiles[i];
            var shp = cellImgs[i];
            if (t == null) { shp.color = Palette.A(Color.white, 0.12f); shp.HasShadow = false; cellGlyphs[i].color = Color.clear; continue; }
            Color c = TileColor(t, lo, hi, island.IsMoon);
            shp.color = c;
            shp.HasShadow = true;
            shp.ShadowColor = Comic.Opaque(Palette.Mul(c, 0.62f));
            float pitch01 = hi > lo && !island.IsMoon ? (t.midi - lo) / (float)(hi - lo) : 0.3f;
            shp.ShadowOffset = new Vector2(0f, keys ? -3f : -LipPx(pitch01));   // v5: taller lip = higher note (v6: keys lie flat)
            if (keys) { shp.color = Comic.Opaque(t.BaseColor); shp.ShadowColor = Comic.Opaque(Palette.Mul(t.BaseColor, 0.55f)); }
            string icon = t.MarkerIcon;
            cellIcons[i] = icon;
            if (icon == null) { cellGlyphs[i].color = Color.clear; continue; }
            cellGlyphs[i].sprite = IconFactory.Get(icon);
            cellGlyphs[i].color = Palette.A(Color.Lerp(c, Ink, 0.8f), 0.9f);
        }
    }

    int PathSig()
    {
        if (cube == null) return 0;
        unchecked
        {
            int h = 17;
            h = h * 31 + cube.nodes.Count;
            for (int i = 0; i < cube.nodes.Count; i++)
            {
                var t = cube.nodes[i];
                h = h * 31 + (t != null ? t.gridX * 64 + t.gridZ : -1);
                h = h * 31 + cube.ModOf(i);
                h = h * 31 + cube.NudgeAt(i);   // v9 (N)
                h = h * 31 + (i < cube.durs.Count ? cube.durs[i] : -1);
            }
            h = h * 31 + (int)cube.mode; h = h * 31 + cube.instrument; h = h * 31 + (cube.muted ? 1 : 0); h = h * 31 + (int)cube.step;
            h = h * 31 + (Instruments.Muted[Mathf.Clamp(cube.instrument, 0, Instruments.Count - 1)] ? 1 : 0);
            h = h * 31 + (island != null ? island.GetInstanceID() : 0);
            return h;
        }
    }

    void EnsureOverlay() { int s = PathSig(); if (s != builtSig) RebuildOverlay(s); }

    /// <summary>Bead diameter of node <paramref name="i"/>: the note's length (the world cube's SizeOf), then the sticker (ghost small, accent large).</summary>
    float BeadSize(int i)
    {
        int mod = cube.ModOf(i);
        float s = BeadPx * Mathf.Max(0.45f, cellK) * AudioCube.SizeOf(cube.DurBeats(i));
        return mod == 2 ? s * 0.62f : (mod == 3 ? s * 1.3f : (mod >= 4 ? Mathf.Max(s, 24f) : s));
    }

    void RebuildOverlay(int sig)
    {
        builtSig = sig;
        beadPos.Clear(); beadGX.Clear(); beadGZ.Clear();
        int n = cube != null && island != null ? cube.nodes.Count : 0;
        // bead centres, repeated cells fanned out per visit
        if (n > 0)
        {
            fanTotal.Clear(); fanSeen.Clear();
            for (int i = 0; i < n; i++) { var t = cube.nodes[i]; int key = t != null ? t.gridX * 64 + t.gridZ : -1; int v; fanTotal.TryGetValue(key, out v); fanTotal[key] = v + 1; }
            for (int i = 0; i < n; i++)
            {
                var t = cube.nodes[i];
                int gx = t != null ? t.gridX : 0, gz = t != null ? t.gridZ : 0, key = t != null ? gx * 64 + gz : -1;
                int k; fanSeen.TryGetValue(key, out k); fanSeen[key] = k + 1;
                int m = fanTotal[key];
                Vector2 p = CellPos(gx, gz);
                if (m > 1 && keys) p += new Vector2(0f, (gx >= 0 && gx < keyBlack.Length && keyBlack[gx] ? -1f : 1f) * k * KeyBeadStep);   // v6: a repeated note stacks along its key
                else if (m > 1) { float a = (90f + 360f * k / m) * Mathf.Deg2Rad; p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * FanR * cellK; }
                p.y += NudgePx * cube.NudgeAt(i);   // v9 (N): a nudged bead sits a touch higher / lower
                beadPos.Add(p); beadGX.Add(gx); beadGZ.Add(gz);
            }
        }
        while (beadBase.Count < n) { beadBase.Add(BeadPx); beadPulse.Add(0f); beadPop.Add(9f); }
        Color col = cube != null ? cube.Color : Palette.Accent;
        bool muted = cube != null && (cube.muted || Instruments.Muted[Mathf.Clamp(cube.instrument, 0, Instruments.Count - 1)]);
        if (muted) col = Color.Lerp(col, new Color(0.6f, 0.6f, 0.66f), 0.7f);
        col = Comic.Opaque(col);
        // chevrons and end marks: cream on a coloured path, ink on a very light one (drums are white)
        Color mark = Comic.Lum(col) > 0.72f ? Ink : Comic.Cream;
        markColor = mark;
        // the path: one inked line (ink under, the cube's colour on top), chevrons at the midpoints
        pathInk.Clear();
        if (n >= 2)
        {
            pathInk.Begin(); for (int i = 0; i < n; i++) pathInk.P(beadPos[i]); pathInk.EndLine(Ink, 9.5f);
            pathInk.Begin(); for (int i = 0; i < n; i++) pathInk.P(beadPos[i]); pathInk.EndLine(col, 5f);
            for (int i = 0; i + 1 < n; i++)
            {
                Vector2 a = beadPos[i], b = beadPos[i + 1], d = b - a;
                float len = d.magnitude;
                if (len > 30f) Chevron((a + b) * 0.5f, d / len, 5.5f, mark);
            }
        }
        PathMode mode = cube != null ? cube.mode : PathMode.Loop;
        if (n >= 3 && mode == PathMode.Loop)
        {
            Vector2 a = beadPos[n - 1], b = beadPos[0], d = b - a;
            float len = d.magnitude;
            if (len > 14f)
            {
                Vector2 u = d / len;
                for (float s = 12f; s < len - 10f; s += 13f) pathInk.Line(Palette.A(col, 0.55f), 3f, a + u * s, a + u * Mathf.Min(s + 6f, len - 10f));
            }
        }
        else if (n >= 2 && mode == PathMode.PingPong)
        {
            EndChevrons(beadPos[0], beadPos[1]);
            EndChevrons(beadPos[n - 1], beadPos[n - 2]);
        }
        else if (n >= 1 && mode == PathMode.Once)
        {
            Vector2 last = beadPos[n - 1];
            Vector2 outDir = n >= 2 ? (last - beadPos[n - 2]) : Vector2.right;
            if (outDir.sqrMagnitude < 1e-3f) outDir = Vector2.right;
            outDir.Normalize();
            Vector2 c0 = last + outDir * 18f, nrm = new Vector2(-outDir.y, outDir.x);
            pathInk.Line(Ink, 7f, c0 - nrm * 10f, c0 + nrm * 10f).Line(mark, 3f, c0 - nrm * 9f, c0 + nrm * 9f);
        }
        // beads: sized by note length, hollow rests, a glyph for the other stickers
        beadsUsed = 0;
        for (int i = 0; i < n; i++)
        {
            int mod = cube.ModOf(i);
            float size = BeadSize(i);
            beadBase[i] = size;
            var bd = TakeBead();
            Centre(bd.rectTransform, beadPos[i], new Vector2(size, size));
            bd.Hollow = mod == 1;
            bd.color = mod == 2 ? Palette.A(col, 0.75f) : col;
            bd.Ink = Ink;
            string icon = mod >= 4 ? AudioCube.StickerIcon(mod) : null;
            var gl = glyphs[beadsUsed - 1];
            if (icon != null)
            {
                gl.sprite = IconFactory.Get(icon);
                Centre(gl.rectTransform, beadPos[i], new Vector2(15f, 15f));
                gl.color = Palette.A(Ink, 0.9f);
                if (!gl.gameObject.activeSelf) gl.gameObject.SetActive(true);
            }
            else if (gl.gameObject.activeSelf) gl.gameObject.SetActive(false);
            // v9 (N): the nudge mark — ▲ at the bead's upper right (▲▲ for +2), ▼ at its lower right
            int nd = cube.NudgeAt(i);
            var ng = nudgeGlyphs[beadsUsed - 1];
            if (nd != 0)
            {
                if (ng == null)
                {
                    ng = UIKit.Image(beadRoot, "Nudge" + (beadsUsed - 1), "dot", Color.clear, new Vector2(17f, 17f));   // v9 (N): made on first use only
                    ng.preserveAspect = true; ng.raycastTarget = false;
                    nudgeGlyphs[beadsUsed - 1] = ng;
                }
                ng.sprite = IconFactory.Get(Nudge.Icon(nd));
                float r = size * 0.5f;
                Centre(ng.rectTransform, beadPos[i] + new Vector2(r + 8f, nd > 0 ? r + 4f : -r - 4f), new Vector2(17f, 17f));
                ng.color = Palette.A(Ink, 0.95f);
                if (!ng.gameObject.activeSelf) ng.gameObject.SetActive(true);
            }
            else if (ng != null && ng.gameObject.activeSelf) ng.gameObject.SetActive(false);
        }
        for (int i = beadsUsed; i < beads.Count; i++) { if (beads[i].gameObject.activeSelf) beads[i].gameObject.SetActive(false); if (glyphs[i].gameObject.activeSelf) glyphs[i].gameObject.SetActive(false); if (nudgeGlyphs[i] != null && nudgeGlyphs[i].gameObject.activeSelf) nudgeGlyphs[i].gameObject.SetActive(false); }
        startRing.gameObject.SetActive(n > 0);
        if (n > 0) startRing.rectTransform.anchoredPosition = beadPos[0];
        glowImg.color = Palette.A(col, glowImg.color.a);
        if (curNode >= n) curNode = -1;
        if (hoverBead >= n) hoverBead = -1;
        PlaceHighlight();
    }

    void Chevron(Vector2 at, Vector2 dir, float size, Color c)
    {
        Vector2 n = new Vector2(-dir.y, dir.x);
        pathInk.Line(c, 2.4f, at - dir * size + n * size, at + dir * size * 0.4f, at - dir * size - n * size);
    }

    void EndChevrons(Vector2 end, Vector2 neighbour)
    {
        Vector2 outDir = end - neighbour;
        if (outDir.sqrMagnitude < 1e-3f) outDir = Vector2.right;
        outDir.Normalize();
        for (int k = 0; k < 2; k++) Chevron(end + outDir * (17f + 7f * k), -outDir, 5f, Palette.A(markColor, 1f - 0.3f * k));   // pointing back into the path: it turns around here
    }

    InkShape TakeBead()
    {
        if (beadsUsed < beads.Count)
        {
            var b = beads[beadsUsed++];
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            b.rectTransform.localScale = Vector3.one;
            return b;
        }
        var s = InkShape.Create(beadRoot, "Bead" + beads.Count, InkShape.Kind.Circle, Color.white, new Vector2(20f, 20f));
        s.raycastTarget = false; s.SetInk(1.8f, 2.8f); s.ShadowOffset = new Vector2(1.5f, -2.5f); s.Wobble = 0.5f; s.RotJitter = 0f;
        var gl = UIKit.Image(beadRoot, "Glyph" + beads.Count, "dot", Color.clear, new Vector2(15f, 15f));
        gl.preserveAspect = true; gl.raycastTarget = false;
        beads.Add(s); glyphs.Add(gl); nudgeGlyphs.Add(null);   // v9 (N): the nudge mark's slot, made on first use
        beadsUsed++;
        return s;
    }

    void PlaceHighlight()
    {
        if (selRing == null) return;
        bool on = highlight >= 0 && highlight < beadPos.Count && cube != null;
        if (selRing.gameObject.activeSelf != on) selRing.gameObject.SetActive(on);
        selRing.Boil = on;
        if (!on) return;
        float r = Mathf.Max(beadBase[highlight] * 0.5f + 8f, 17f);
        selRing.rectTransform.anchoredPosition = beadPos[highlight];
        float s = r / 25f;
        selRing.rectTransform.localScale = new Vector3(s, s, 1f);
    }

    // ------------------------------------------------------------------ per frame
    void OnEnable() { AudioCube.OnLanded += HandleLanded; }
    void OnDisable()
    {
        AudioCube.OnLanded -= HandleLanded; ClosePopover();
        if (cursorHeld) { cursorHeld = false; CursorKit.Release(this); }
        // F18 / S5: the card closed mid-gesture (Esc, the inspector left): what the gesture already changed is committed, not dropped
        if (pressing)
        {
            pressing = false; simPress = false;
            if (g == G.Pencil) pencil = false;
            if (changed && cube != null) Commit();
            g = G.None; pressNode = -1; changed = false;
        }
    }

    void HandleLanded(AudioCube c, int w, int k, AudioCube.Hit hit)
    {
        if (c == null || c != cube || hit.node < 0 || hit.node >= beadPulse.Count) return;
        beadPulse[hit.node] = hit.fires ? 1f : 0.45f;
        if (hit.fires) landPulse = hit.weight == 2 ? 1.3f : 1f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (cube == null) { if (beadPos.Count > 0 || island != null) Bind(null); return; }
        var isl = cube.Island;
        if (isl != island || (isl != null && (isl.cols != cols || isl.rows != rows))) Bind(cube);
        RefreshCells(false);
        EnsureOverlay();
        // real pointer: moves while pressed (a release outside the view still arrives through OnPointerUp)
        if (pressing && !simPress)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) { pressing = false; Up(lastLocal, g == G.Right ? 1 : 0); }
            else { Vector2 lp; if (LocalOf(Input.mousePosition, out lp)) Move(lp); }
        }
        UpdateHover();
        UpdateNow(dt);
        AnimateBeads(dt);
        // shift nudge + refusal shake
        nudgeT += dt; shakeT += dt;
        Vector2 off = Vector2.zero;
        if (nudgeT < 0.18f) off += nudge * (1f - Ease.OutCubic(nudgeT / 0.18f));
        if (shakeT < 0.3f) off += new Vector2(Mathf.Sin(shakeT * 70f) * 7f * (1f - shakeT / 0.3f), 0f);
        if (lineRoot.anchoredPosition != off) lineRoot.anchoredPosition = glowRoot.anchoredPosition = beadRoot.anchoredPosition = topRoot.anchoredPosition = off;
    }

    void UpdateHover()
    {
        Vector2 lp; int x, z;
        bool inside = !pressing && popover == null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null) && LocalOf(Input.mousePosition, out lp);
        bool over = inside && LocalOf(Input.mousePosition, out lp) && CellAt(lp, out x, out z);
        bool ring = false;
        if (!over) { hoverBead = -1; hoverX = hoverZ = -1; }
        else
        {
            LocalOf(Input.mousePosition, out lp); CellAt(lp, out x, out z);
            hoverX = x; hoverZ = z;
            ring = OnStartRing(lp);
            hoverBead = ring ? -1 : BeadAt(lp);
        }
        if (ring != ringHover) { ringHover = ring; startRing.Boil = ring; }
        bool wantCursor = ringHover || g == G.Shift;
        if (wantCursor != cursorHeld) { cursorHeld = wantCursor; if (wantCursor) CursorKit.Want(CursorKit.Kind.Grab, this); else CursorKit.Release(this); }
        float a = over && !ring ? (pencil ? 0.75f : 0.4f) : 0f;
        var hc = hoverRing.color;
        float na = Mathf.Lerp(hc.a, a * 1.6f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
        if (Mathf.Abs(na - hc.a) > 0.004f || (pencil ? hc.r != Palette.Ok.r : hc.r != Comic.Cream.r)) hoverRing.color = Palette.A(pencil ? Palette.Ok : Comic.Cream, na);
        if (over)
        {
            if (keys && hoverX >= 0 && hoverX < keyRect.Length) { hoverRing.rectTransform.anchoredPosition = keyRect[hoverX].center; hoverRing.rectTransform.sizeDelta = keyRect[hoverX].size + new Vector2(6f, 6f); }
            else
            {
                hoverRing.rectTransform.anchoredPosition = CellPos(hoverX, hoverZ);
                if (hoverRing.rectTransform.sizeDelta.x != cell + 8f) hoverRing.rectTransform.sizeDelta = new Vector2(cell + 8f, cell + 8f);
            }
        }
    }

    /// <summary>The bead of the node the cube stands on now glows; it glides from bead to bead with the cube.</summary>
    void UpdateNow(float dt)
    {
        int now = -1;
        AudioCube.CubePose p;
        if (GlobalClock.IsPlaying && cube.TryGetPose(out p) && p.active && p.hit.node >= 0 && p.hit.node < beadPos.Count) now = p.hit.node;
        if (now >= 0)
        {
            if (curNode < 0) glowPos = beadPos[now];
            curNode = now;
            glowPos = Vector2.Lerp(glowPos, beadPos[now], 1f - Mathf.Exp(-dt * 22f));
        }
        else curNode = -1;
        float na = Mathf.Lerp(glowA, now >= 0 ? 0.85f : 0f, 1f - Mathf.Exp(-dt * 10f));
        landPulse *= Mathf.Exp(-dt * 6f);
        if (na < 0.003f && glowA < 0.003f && landPulse < 0.003f) { glowA = 0f; if (glowImg.color.a != 0f) glowImg.color = new Color(1f, 1f, 1f, 0f); return; }
        glowA = na;
        float s = 54f * (1f + 0.4f * landPulse);
        glowImg.rectTransform.anchoredPosition = glowPos;
        glowImg.rectTransform.sizeDelta = new Vector2(s, s);
        var c = cube.Color;
        glowImg.color = new Color(c.r, c.g, c.b, glowA * (0.75f + 0.25f * landPulse));
    }

    void AnimateBeads(float dt)
    {
        int n = Mathf.Min(beadPos.Count, beadsUsed);
        for (int i = 0; i < n; i++)
        {
            beadPulse[i] *= Mathf.Exp(-dt * 6f);
            beadPop[i] += dt;
            float pop = beadPop[i] < 0.22f ? Mathf.LerpUnclamped(1.6f, 1f, Ease.OutBack(beadPop[i] / 0.22f)) : 1f;
            float s = (1f + 0.5f * beadPulse[i]) * (i == hoverBead ? 1.22f : 1f) * (g == G.MoveNode && i == pressNode ? 1.3f : 1f) * pop;
            var t = beads[i].rectTransform;
            if (Mathf.Abs(t.localScale.x - s) > 0.002f)
            {
                t.localScale = new Vector3(s, s, 1f);
                glyphs[i].rectTransform.localScale = t.localScale;
            }
        }
    }

    void PopBead(int i) { if (i >= 0 && i < beadPop.Count) beadPop[i] = 0f; }

    // ------------------------------------------------------------------ hit tests
    bool LocalOf(Vector2 screen, out Vector2 lp) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out lp);

    /// <summary>The cell under a grid-local point (gaps belong to the cell before them); false outside the grid.</summary>
    public bool CellAt(Vector2 lp, out int x, out int z)
    {
        if (keys) { x = KeyAt(lp, 0f); z = 0; return island != null && x >= 0; }   // v6: the key under the point
        x = Mathf.FloorToInt((lp.x + gridW * 0.5f + gap * 0.5f) / (cell + gap));
        z = Mathf.FloorToInt((lp.y + gridH * 0.5f + gap * 0.5f) / (cell + gap));
        return island != null && x >= 0 && z >= 0 && x < cols && z < rows;
    }

    /// <summary>A cell the drag has really entered (inner 84 %): no flicker along cell borders.</summary>
    bool CellEntered(Vector2 lp, out int x, out int z)
    {
        if (keys) { x = KeyAt(lp, 0.08f); z = 0; return island != null && x >= 0; }
        if (!CellAt(lp, out x, out z)) return false;
        Vector2 d = lp - CellPos(x, z);
        return Mathf.Abs(d.x) <= cell * 0.42f && Mathf.Abs(d.y) <= cell * 0.42f;
    }

    /// <summary>The point is on node 0's ring (the band outside its bead, inside the ring) and no other bead is nearer.</summary>
    public bool OnStartRing(Vector2 lp)
    {
        if (beadPos.Count == 0 || cube == null) return false;
        float d = (lp - beadPos[0]).magnitude;
        float inner = Mathf.Max(beadBase[0] * 0.5f + 1f, 9f);
        if (d < inner || d > RingR + 6f) return false;
        for (int i = 1; i < beadPos.Count; i++) if ((lp - beadPos[i]).magnitude < d - 2f) return false;
        return true;
    }

    /// <summary>The bead under a grid-local point: any bead within 18 px, or the nearest bead of the pressed cell; the last
    /// node wins a tie inside its cell (extending is the common gesture). -1 = none.</summary>
    public int BeadAt(Vector2 lp)
    {
        int n = beadPos.Count;
        if (n == 0) return -1;
        int cx, cz; bool inCell = CellAt(lp, out cx, out cz);
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (beadPos[i] - lp).sqrMagnitude;
            bool same = inCell && beadGX[i] == cx && beadGZ[i] == cz;
            if (!same && d > 18f * 18f) continue;
            if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0 && best != n - 1 && beadGX[n - 1] == beadGX[best] && beadGZ[n - 1] == beadGZ[best] && (beadPos[n - 1] - lp).magnitude < Mathf.Sqrt(bd) + 8f) best = n - 1;
        return best;
    }

    // ------------------------------------------------------------------ gestures
    public void OnPointerDown(PointerEventData e)
    {
        Vector2 lp; if (!LocalOf(e.position, out lp)) return;
        int b = e.button == PointerEventData.InputButton.Right ? 1 : (e.button == PointerEventData.InputButton.Left ? 0 : 2);
        if (b > 1) return;
        pressing = true; simPress = false;
        Down(lp, b);
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (!pressing || simPress) return;
        Vector2 lp; LocalOf(e.position, out lp);
        int b = e.button == PointerEventData.InputButton.Right ? 1 : (e.button == PointerEventData.InputButton.Left ? 0 : 2);
        if (b > 1) return;
        pressing = false;
        Up(lp, b);
    }

    void Down(Vector2 lp, int button)
    {
        EnsureOverlay();
        lastLocal = lp;
        g = G.None; pressNode = -1;
        if (cube == null || island == null || cube.nodes.Count == 0) return;
        if (popover != null || (!simPress && Time.frameCount - popoverClosedFrame <= 1)) { ClosePopover(); return; }   // this press only dismisses the popover
        if (button == 1) { g = G.Right; pressNode = PathLocked ? -1 : BeadAt(lp); return; }   // v7: a derived path loses no node
        pressLocal = lp; pressTime = Time.unscaledTime; moved = false; changed = false; popped = false;
        int cx, cz; bool inCell = CellAt(lp, out cx, out cz);
        if (PathLocked)
        {
            // v7: a phrase / a runner: only its beads answer (rest on / off, the sticker popover)
            int pb = BeadAt(lp);
            if (pb >= 0) { g = G.Bead; pressNode = pb; PopBead(pb); }
            return;
        }
        if (pencil)
        {
            if (!inCell) return;
            var t = island.GetTile(cx, cz); if (t == null) return;
            cube.SetPathAndMods(new List<TileInteraction> { t }, new List<int> { 0 });   // the pencil clears the path to this cell
            g = G.Pencil; changed = true;
            EnsureOverlay(); PopBead(0); Preview(t);
            return;
        }
        if (OnStartRing(lp)) { g = G.Shift; shiftX = cx; shiftZ = cz; startRing.Boil = true; return; }   // node 0's ring: move the whole path
        int bead = BeadAt(lp);
        if (bead >= 0) { g = G.Bead; pressNode = bead; PopBead(bead); return; }
        if (!inCell) return;
        var tile = island.GetTile(cx, cz); if (tile == null) return;
        if (!Room(1)) return;   // v7 §19.1: the grid's capacity
        cube.InsertNode(cube.nodes.Count, tile, 0);   // click an empty cell: append (non-adjacent is legal for playback)
        g = G.Append; changed = true;
        EnsureOverlay(); PopBead(cube.nodes.Count - 1); Preview(tile);
    }

    void Move(Vector2 lp)
    {
        lastLocal = lp;
        if (g == G.None || g == G.Right || cube == null || island == null || cube.nodes.Count == 0) return;
        if (!moved && (lp - pressLocal).magnitude * PxPerUnit > OrbitCamera.DragThresholdPx) moved = true;   // F17: the one DPI-aware threshold, in pixels
        if (g == G.Shift)
        {
            int sx, sz;
            if (!moved || !CellEntered(lp, out sx, out sz) || (sx == shiftX && sz == shiftZ)) return;
            int dx = sx - shiftX, dz = sz - shiftZ;
            if (cube.CanShift(dx, dz) && cube.ShiftPath(dx, dz))
            {
                shiftX = sx; shiftZ = sz; changed = true;
                EnsureOverlay();
                nudge = new Vector2(-dx, -dz) * (cell + gap) * 0.35f; nudgeT = 0f;
                AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.15f);
                if (cube.nodes.Count > 0) Preview(cube.nodes[0]);
            }
            else if (shakeT > 0.3f) Refuse();
            return;
        }
        if (g == G.Bead)
        {
            if (!moved)
            {
                if (!popped && Time.unscaledTime - pressTime >= HoldSeconds) { popped = true; OpenPopover(pressNode); }
                return;
            }
            if (popped) { ClosePopover(); popped = false; }                  // F13: dragging on after the hold: the popover goes, the drag wins
            if (PathLocked) return;                                          // v7: a derived path does not move
            g = pressNode == cube.nodes.Count - 1 ? G.Extend : G.MoveNode;   // drag the last bead = extend, any other = move it
        }
        int cx, cz;
        if (!CellEntered(lp, out cx, out cz)) return;
        if (g == G.MoveNode)
        {
            var t = island.GetTile(cx, cz);
            if (t != null && pressNode >= 0 && pressNode < cube.nodes.Count && cube.MoveNode(pressNode, t)) { changed = true; EnsureOverlay(); PopBead(pressNode); Preview(t); }
        }
        else ExtendToCell(cx, cz);
    }

    /// <summary>Screen pixels per grid-local unit (the canvas scale times the card's scale).</summary>
    float PxPerUnit => rt != null ? Mathf.Max(1e-3f, Mathf.Abs(rt.lossyScale.x)) : 1f;

    void Up(Vector2 lp, int button)
    {
        lastLocal = lp;
        if (button == 1)
        {
            if (g == G.Right && cube != null) { int b = BeadAt(lp); if (b >= 0 && b == pressNode) RemoveAt(b); }
            g = G.None; pressNode = -1;
            return;
        }
        if (g == G.Bead && !moved && !popped && cube != null && pressNode >= 0 && pressNode < cube.nodes.Count)
        {
            cube.ToggleRest(pressNode);   // click a bead: rest on / off
            changed = true;
            EnsureOverlay(); PopBead(pressNode);
            if (cube.ModOf(pressNode) == 1) AudioPool.UI(ProceduralAudio.Tick(), 0.35f, 0.75f);
            else HearNode(pressNode);
        }
        if (g == G.Pencil) pencil = false;
        if (g == G.Shift) startRing.Boil = ringHover;
        if (changed) Commit();
        g = G.None; pressNode = -1; changed = false;
    }

    void ExtendToCell(int cx, int cz)
    {
        var last = cube.nodes[cube.nodes.Count - 1];
        if (last == null) return;
        int x = last.gridX, z = last.gridZ, guard = 0;
        if (x == cx && z == cz) return;                  // consecutive duplicates are ignored
        var add = new List<TileInteraction>();
        if (cube.sphere) { var leap = island.GetTile(cx, cz); if (leap != null) add.Add(leap); x = cx; z = cz; }   // SPHERE: a leap, no line of cells
        while ((x != cx || z != cz) && guard++ < 64)
        {
            x += Math.Sign(cx - x); z += Math.Sign(cz - z);   // a fast drag fills the cells it skipped (straight / diagonal line)
            var t = island.GetTile(x, z);
            if (t == null) break;
            add.Add(t);
        }
        if (add.Count == 0) return;
        if (!Room(add.Count)) return;   // v7 §19.1: the grid's capacity
        var nodes = new List<TileInteraction>(cube.nodes); var mods = new List<int>(nodes.Count + add.Count);
        for (int i = 0; i < nodes.Count; i++) mods.Add(cube.ModOf(i));
        foreach (var t in add) { nodes.Add(t); mods.Add(0); }
        cube.SetPathAndMods(nodes, mods);
        changed = true;
        EnsureOverlay(); PopBead(cube.nodes.Count - 1);
        Preview(add[add.Count - 1]);
    }

    void RemoveAt(int b)
    {
        if (cube.RemoveNode(b)) { AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.1f); EnsureOverlay(); Commit(); }
        else Refuse();
    }

    void Preview(TileInteraction t) { if (t != null && cube != null) t.PlayPreview(cube.instrument, cube.octave, cube.voice); }
    /// <summary>v9 (N): hear node <paramref name="n"/> as it sounds — a nudged / bent node through the engine's node pitch (InkUI.AuditionNote:
    /// VoiceRules.PreviewEvent of the node), any other the tile's plain preview as before.</summary>
    void HearNode(int n)
    {
        if (cube == null || n < 0 || n >= cube.nodes.Count || cube.nodes[n] == null) return;
        if (cube.NudgeAt(n) != 0 || cube.BendAt(n) != 0) InkUI.AuditionNote(cube, n, Mathf.Max(0.25f, cube.DurBeats(n)));
        else cube.nodes[n].PlayPreview(cube.instrument, cube.octave, cube.voice);
    }

    void Commit()
    {
        History.Push();
        CommitCount++;
        CubeInspector.NoteGridEdited();
        OnCommitted?.Invoke(cube);
    }

    void Refuse() { shakeT = 0f; AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.4f); }

    // ---- v7 §19.1: "finished paths keep the cap" — appends / extends past the grid's capacity are refused and the chip offers EXPAND
    InkButton capChip; bool capFull;
    /// <summary>v7: the "capacity reached" chip shows (tests) and a click on it (tests).</summary>
    public bool CapacityChipShown => capChip != null && capChip.gameObject.activeInHierarchy;
    public void SimClickExpand() { if (CapacityChipShown) capChip.Click(); }

    /// <summary>Room for <paramref name="nodesAdded"/> more notes at the last note's length on this grid; else refused (a thud, the grid shakes, the chip).</summary>
    bool Room(int nodesAdded)
    {
        if (cube == null || island == null || !cube.HasDurations) return true;
        float cap = PathManager.CapacityOf(island);
        if (cap <= 0f) return true;
        float used = 0f; for (int i = 0; i < cube.nodes.Count; i++) used += cube.DurBeats(i);
        float add = nodesAdded * cube.DurBeats(cube.nodes.Count - 1);
        if (used + add <= cap + 1e-4f) return true;
        CapacityRefusals++;
        Refuse();
        island.Shake(0.12f);
        Onboarding.Notify(Onboarding.Ev.CapacityReached);
        ShowCapChip(true);
        return false;
    }

    /// <summary>v7 §19.1: the inspector's length row / note stretch refused a length past the grid's capacity (RhythmStrip): the chip offers EXPAND.</summary>
    public void ShowCapacityChip() { if (cube != null && island != null) { shakeT = 0f; ShowCapChip(true); } }

    void ShowCapChip(bool on)
    {
        capFull = on;
        if (!on) { if (capChip != null && capChip.gameObject.activeSelf) capChip.gameObject.SetActive(false); return; }
        if (capChip == null)
        {
            capChip = InkButton.Create(topRoot, "CapacityExpand", new Vector2(150f, 26f), "expand");
            var crt = (RectTransform)capChip.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); crt.pivot = new Vector2(1f, 0f);
            var paper = InkShape.Create(capChip.body, "Paper", InkShape.Kind.RoundRect, Color.Lerp(InkUI.Paper, Palette.Danger, 0.18f), new Vector2(150f, 26f));
            paper.raycastTarget = false; paper.Radius = 9f; paper.SetInk(1.6f, 2.2f); paper.Wobble = 0.6f;
            var tx = UIKit.Text(capChip.body, "Text", "capacity reached  +", 14f, Palette.Danger);
            tx.font = Comic.Font; tx.raycastTarget = false; tx.alignment = TMPro.TextAlignmentOptions.Center;
            InkUI.Stretch(tx.rectTransform);
            capChip.onClick = () =>
            {
                if (island == null) return;
                bool ok = false;
                try { ok = SongOps.ExpandGrid(island); } catch (Exception e) { Debug.LogWarning("[PathGridView] ExpandGrid threw: " + e.Message); }
                if (ok) { ShowCapChip(false); AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.2f); }
            };
        }
        ((RectTransform)capChip.transform).anchoredPosition = new Vector2(gridW * 0.5f + Pad, gridH * 0.5f + Pad + 2f);
        capChip.transform.SetAsLastSibling();
        if (!capChip.gameObject.activeSelf) capChip.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ sticker popover
    void OpenPopover(int node)
    {
        if (cube == null || node < 0 || node >= cube.nodes.Count || node >= beadPos.Count) return;
        ClosePopover();
        Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, beadRoot.TransformPoint(beadPos[node]));
        var c = cube; int n = node;
        var r = PopRing.OpenAtScreen(InkUI.OverlayRoot, sp, StickerIcons, i =>   // on the inspector's canvas: above the card
        {
            if (c == null || n >= c.nodes.Count) return;
            c.SetMod(n, i);
            Commit();
            PopBead(n);
            if (i != 1 && c.nodes[n] != null) HearNode(n);
        }, 46f);
        r.SetToggled(Mathf.Clamp(cube.ModOf(node), 0, StickerIcons.Length - 1), true);
        openPopovers++;
        r.onClosed = () => { openPopovers = Mathf.Max(0, openPopovers - 1); if (popover == r) popover = null; popoverClosedFrame = Time.frameCount; };
        popover = r;
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.25f);
    }

    public void ClosePopover() { if (popover != null) { var p = popover; popover = null; p.Close(); } }

    /// <summary>A fresh opening: pencil disarmed, no gesture or popover left over.</summary>
    public void ResetTools() { pencil = false; ClosePopover(); g = G.None; pressNode = -1; pressing = false; simPress = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { openPopovers = 0; }

    // ------------------------------------------------------------------ path tools (the card's buttons)
    /// <summary>Arms / disarms the pencil: the next press redraws the path from that cell.</summary>
    public void TogglePencil()
    {
        if (cube == null) return;
        pencil = !pencil;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, pencil ? 1.3f : 0.9f);
    }

    /// <summary>Node order and stickers reversed; one History entry.</summary>
    public bool Reverse()
    {
        if (cube == null || cube.nodes.Count < 2) { Refuse(); return false; }
        cube.ReversePath();
        EnsureOverlay();
        for (int i = 0; i < beadPop.Count; i++) beadPop[i] = -0.02f * i;   // a quick ripple along the path
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.3f, 1.3f);
        Commit();
        return true;
    }

    /// <summary>Moves the whole path one cell when every node stays inside the grid (else a refusal shake); one History entry.</summary>
    public bool Shift(int dx, int dz)
    {
        if (cube == null) return false;
        if (!cube.ShiftPath(dx, dz)) { Refuse(); return false; }
        EnsureOverlay();
        nudge = new Vector2(-dx, -dz) * (cell + gap) * 0.45f; nudgeT = 0f;
        AudioPool.UI(ProceduralAudio.Tick(), 0.35f, 1.15f);
        if (cube.nodes.Count > 0) Preview(cube.nodes[0]);
        Commit();
        return true;
    }

    // ------------------------------------------------------------------ test hooks (same code paths as the pointer)
    /// <summary>Tests: one frame of the grid's own work (GC measurements).</summary>
    public void StepForTest() { Update(); }
    /// <summary>Press at the centre of cell (x, z).</summary>
    public void SimDownCell(int x, int z, int button = 0) { pressing = true; simPress = true; Down(CellPos(x, z), button); }
    /// <summary>Press on the bead of node <paramref name="node"/>.</summary>
    public bool SimDownNode(int node, int button = 0)
    {
        EnsureOverlay();
        if (node < 0 || node >= beadPos.Count) return false;
        pressing = true; simPress = true;
        Down(beadPos[node], button);
        return true;
    }
    /// <summary>Press on node 0's ring (the path's drag handle).</summary>
    public bool SimDownRing()
    {
        EnsureOverlay();
        if (beadPos.Count == 0) return false;
        pressing = true; simPress = true;
        Down(beadPos[0] + new Vector2(0f, Mathf.Max(beadBase[0] * 0.5f + 4f, RingR - 3f)), 0);
        return g == G.Shift;
    }
    /// <summary>Drag to the centre of cell (x, z).</summary>
    public void SimMoveCell(int x, int z) { if (g == G.Shift && !moved) moved = true; Move(CellPos(x, z)); }
    /// <summary>Release where the pointer is.</summary>
    public void SimUp(int button = 0) { pressing = false; simPress = false; Up(lastLocal, button); }
    /// <summary>Lets the hold timer run out (a held bead opens the sticker popover).</summary>
    public void SimHold() { pressTime -= HoldSeconds + 0.05f; Move(lastLocal); }
}
