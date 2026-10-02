using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 world HUD, top centre (research §4.1 "the column rail", replaces the v3 overview strip): one hand-inked horizontal line (3 px, 1 px
/// wobble, at most 40 % of the screen) with one bead stack per column. The anchor island's bead sits on the line; the other islands of
/// the column stack above / below it in their on-screen (z) order; bead colour = chord colour (v5: the vibe's colour), bead width ∝ bars, a
/// sleeping island is a hollow dashed bead, glued (merged) columns touch, Moons are small round beads after a gap. v5 (SPEC §2.5 / §7): a
/// column is as wide as its passes (ColumnPasses — the belt): an island with repeat n draws its bead for the first pass and a thin dashed
/// belt with a ghost bead for each further pass (one that plays fewer passes than its column rests there: nothing drawn); the playhead walks
/// the passes; an island on the key's home chord carries a tiny house. The lit column grows 1.2× and a mini cube
/// hops from stack to stack on every downbeat (the playhead). Click a bead = select + frame that island (right-click: play from there).
/// While a deck card is dragged the rail shows a dotted ghost bead where the world drop would put it. Loop: the rail's right end curls
/// back toward the start (click the curl = LoopSong). Focus loop (hint focusLoop): an ink loop hugs that column's stack with a ✕ sticker
/// (FocusLoop.Dismiss). Rebuilt on song / history / column changes only; per frame it moves the playhead and scales the lit stack.
/// v6 (SPEC v6 §7.4): every chord island's bead wears its job badge, tiny, stuck on its left end (JobBadge — the job-1 house replaces the v5
/// house on home beads); (v6's dotted carry arcs: replaced by §21's long-grid bars, below);
/// a keyboard island's bead is a piano key strip (ivory with black keys). While a deck card is hovered (<see cref="SwapJob"/>) the beads of
/// the islands doing the same job pulse — the chords that card could swap with. v6 §11 (the user: "the percussion circle should stay in line
/// with the current part of the song … different percussions for different parts of the song (or even no percussion)"): the DRUM SECTIONS —
/// every Moon plays from its start column until the next Moon's start; under the beads each section covers runs a thin moon-grey line with a
/// little drum at its start (a dot per extra layered Moon), and columns without percussion show a gap. While the deck's Moon card is dragged
/// the rail shows a dotted drum where it would start (<see cref="MoonGhostColumn"/>).
/// v7 (SPEC v7 §7.4, §13.4, §17.1 — the user: "make really intuitive the idea of sections … 4 beats in a measure, 4 measures in a "section""):
/// the beads are grouped by SECTION — inside a section the columns touch, a gap between sections, and under each section a bracket with its
/// LETTER on a stone disc (gold once it has its 4 measures; it sparkles when it gets there), its NAME's picture (intro, verse, chorus …) and the
/// MEASURE NUMBERS 1 2 3 4 under its measures; a section short of 4 measures keeps dotted slots for the measures it still needs. The rail
/// follows the GROUND (SongManager.GroundOf): each column's beads sit at its height and the line slopes across a stairs column — a gentle
/// landscape. A stairs island's bead is a little staircase (falling / climbing); a melody PHRASE is a bar over the time it spans, above its
/// columns' stacks; LAUNCH is a small ↗ after the bead. §21 (the user: "just keep cubes on each grid. only explore grids moving from left to
/// right with the long grid"): a LONG GRID (package K's SongManager.LongGrids — a grid extended over the next grids of its lane in its section;
/// a chain of extends is one) is ONE bar around its beads — a rim in the extended grid's colour joining them (stepping where its grids sit at
/// different heights), an arrow at its right end — instead of v6's carry hops and v7's flow wave.
/// </summary>
public class HudColumnRail : MonoBehaviour
{
    public static HudColumnRail I;
    /// <summary>v9 (R): a launch's crash kicks the timeline cursor (the playhead glyph hops high); kicks since Play (tests).</summary>
    public static int Kicks;
    public static void Kick(float height = 1.6f) { Kicks++; if (I != null && I.playhead != null) I.playhead.Hop(height); }
    public const float MaxWidth = 760f, BeadH = 17f, LaneStep = 20f, ColGap = 11f, MoonGap = 28f;
    /// <summary>A bead's hit height (the drawing stays BeadH; stacked beads split the overlap halfway between their lanes).</summary>
    public const float HitH = 28f, MoonStep = 28f;

    /// <summary>Island beads (one per island, SongManager.Islands order).</summary>
    public int BeadCount => beads.Count;
    /// <summary>The bead count after bringing the rail up to date now (synchronous: tests read it right after a rebuild).</summary>
    public int CountNow()
    {
        var sm = SongManager.I;
        if (sm != null && (dirty || beads.Count != (sm.HasSong ? sm.Islands.Count : 0))) Rebuild();
        return beads.Count;
    }
    /// <summary>Rebuilds the beads now.</summary>
    public void RebuildNow() { Rebuild(); }
    public RectTransform Root => root;
    public HudButton LoopButton => loopBtn;
    public HudButton FocusButton => focusX;
    public RectTransform FocusBadge => focusRt;
    public CubeGlyph Playhead => playhead;
    /// <summary>The ghost bead the rail shows for a dragged deck card: -1 none, else the column it points at (tests).</summary>
    public int GhostColumn { get; private set; } = -1;
    public string GhostKind { get; private set; } = "";
    /// <summary>Local rect of island <paramref name="i"/>'s bead (tests / the tutorial).</summary>
    public RectTransform BeadOf(int i) => i >= 0 && i < beads.Count ? beads[i].rt : null;
    /// <summary>The beads' HudButtons (island order) (tests: a bead click selects + frames).</summary>
    public HudButton BeadButton(int i) => i >= 0 && i < beads.Count ? beads[i].btn : null;
    /// <summary>v5: the passes the rail draws for column <paramref name="col"/> (its belt) and its width in rail units (tests).</summary>
    public int PassesOf(int col) => col >= 0 && col < cols.Length ? cols[col].passes : 0;
    public float ColumnWidth(int col) => col >= 0 && col < cols.Length ? cols[col].w * scale : 0f;
    /// <summary>v5: island <paramref name="i"/>'s bead: the passes it draws (1 + its belt tails) and whether it wears the home house (tests).</summary>
    public int BeadPasses(int i) => i >= 0 && i < beads.Count ? beads[i].passes : 0;
    public bool BeadHome(int i) => i >= 0 && i < beads.Count && beads[i].home;
    /// <summary>The playhead's pass in its column (tests; -1 parked).</summary>
    public int PlayheadPass { get; private set; } = -1;
    // v6 (SPEC §7.4)
    /// <summary>Drawn size of a bead's job badge.</summary>
    public const float BadgePx = 16f;
    /// <summary>Island <paramref name="i"/>'s bead: its job (0 keyboards / none), the job its badge shows (tests), a keyboard's piano look.</summary>
    public int BeadJob(int i) => i >= 0 && i < beads.Count ? beads[i].job : 0;
    public int BadgeShown(int i) => i >= 0 && i < beads.Count ? JobBadge.JobShown(beads[i].badge) : 0;
    public bool BeadKeyboard(int i) => i >= 0 && i < beads.Count && beads[i].keyboard;
    /// <summary>The carry arcs drawn — §21: none any more (a carry is a long grid: <see cref="LongGridCount"/>).</summary>
    public int CarryArcCount => arcs.Count;
    /// <summary>§21: the long grids drawn (one bar each), each one's first and last island (Islands order) and its x span on the rail.</summary>
    public int LongGridCount => flows.Count;
    public int LongGridFrom(int k) => k >= 0 && k < flows.Count && flows[k].from < beads.Count ? beads[flows[k].from].island : -1;
    public int LongGridTo(int k) => k >= 0 && k < flows.Count && flows[k].to < beads.Count ? beads[flows[k].to].island : -1;
    /// <summary>§21: how many grids long grid <paramref name="k"/>'s bar holds (its beads).</summary>
    public int LongGridSize(int k) => k >= 0 && k < flowBeads.Count ? flowBeads[k].Length : 0;
    public float LongGridX0(int k) => k >= 0 && k < flows.Count && flows[k].from < beads.Count ? beads[flows[k].from].x - beads[flows[k].from].w * 0.5f - LongRim : 0f;
    public float LongGridX1(int k) => k >= 0 && k < flows.Count && flows[k].to < beads.Count ? beads[flows[k].to].x + beads[flows[k].to].w * 0.5f + LongRim : 0f;
    /// <summary>§21: how far a long grid's bar stands out around its beads (rail units).</summary>
    public const float LongRim = 3.5f;
    /// <summary>The job whose beads pulse (a deck card with that job is hovered; 0 = none), and how many beads pulse.</summary>
    public int SwapJob { get => swapJob; set { if (swapJob == value) return; swapJob = value; swapDirty = true; } }
    public int PulsingCount { get; private set; }
    /// <summary>v6 §11: the drum sections drawn (distinct Moon start columns → the next start), the start column of each, and whether column
    /// <paramref name="col"/> has percussion (tests).</summary>
    public int DrumSectionCount => drumSections.Count;
    public int DrumSectionStart(int i) => i >= 0 && i < drumSections.Count ? drumSections[i].start : -1;
    public int DrumSectionEnd(int i) => i >= 0 && i < drumSections.Count ? drumSections[i].end : -1;
    public bool HasDrums(int col) { foreach (var s in drumSections) if (col >= s.start && col < s.end) return true; return false; }
    /// <summary>The column where a dragged Moon card would start its section (-1 none): a dotted drum on the rail.</summary>
    public int MoonGhostColumn { get => moonGhost; set { if (moonGhost == value) return; moonGhost = value; if (line != null) line.Repaint(); } }
    /// <summary>The playhead's x on the rail (rail units, tests).</summary>
    public float PlayheadX => phTo;
    // v7 (SPEC v7 §7.4, §13.4)
    /// <summary>v7: the section brackets drawn, each one's first / last column, letter, name (role) and measures; the measure numbers written
    /// and the dotted measure slots of short sections (tests).</summary>
    public int SectionCountShown => secs.Count;
    public int SectionFirstShown(int s) => s >= 0 && s < secs.Count ? secs[s].first : -1;
    public int SectionLastShown(int s) => s >= 0 && s < secs.Count ? secs[s].last : -1;
    public string SectionLetterShown(int s) => s >= 0 && s < secs.Count ? secs[s].letter : "";
    public int SectionRoleShown(int s) => s >= 0 && s < secs.Count ? secs[s].role : 0;
    public int SectionBarsShown(int s) => s >= 0 && s < secs.Count ? secs[s].bars : 0;
    public int MeasureNumbers { get; private set; }
    public int GhostMeasureCount { get; private set; }
    /// <summary>v7: the gap between two sections' beads vs inside one (rail units; tests: grouped by section).</summary>
    public float GapAfter(int col) => col >= 0 && col + 1 < cols.Length ? cols[col + 1].x0 - (cols[col].x0 + cols[col].w * scale) : 0f;
    /// <summary>v7: island <paramref name="i"/>'s bead — a staircase (and its direction), a phrase bar, launching; its height on the rail.</summary>
    public bool BeadStairs(int i) => i >= 0 && i < beads.Count && beads[i].stairs;
    public bool BeadPhrase(int i) => i >= 0 && i < beads.Count && beads[i].phrase;
    public bool BeadLaunch(int i) => i >= 0 && i < beads.Count && beads[i].launch;
    public float BeadY(int i) => i >= 0 && i < beads.Count ? beads[i].y0 + beads[i].lane * LaneStep : 0f;
    public float BeadX(int i) => i >= 0 && i < beads.Count ? beads[i].x : 0f;
    public float BeadWidth(int i) => i >= 0 && i < beads.Count ? beads[i].w : 0f;
    public int BeadLane(int i) => i >= 0 && i < beads.Count ? beads[i].lane : 0;
    /// <summary>v7: column <paramref name="col"/>'s ground on the rail (target, px) and as drawn now.</summary>
    public float ColumnGround(int col) => col >= 0 && col < cols.Length ? cols[col].ground : 0f;
    public float ColumnGroundShown(int col) => ColY(col);
    /// <summary>v7: the flow waves (§21: none — a flow is a long grid now) and the launch marks drawn.</summary>
    public int FlowWaveCount => 0;
    public int LaunchMarkCount => launches.Count;
    /// <summary>v7: rail px per world unit of ground (the landscape's scale) and the rail units of one measure.</summary>
    public const float HeightPx = 4f, GroundClampPx = 12f, MeasureW = 18f, SecGap = 16f, GhostW = 24f, SparkleSeconds = 0.9f;
    /// <summary>Rail width of one pass: the first (with the bead's rounded ends) and each further one.</summary>
    public static float FirstPassW(int bars) => 24f + 18f * Mathf.Max(1, bars);
    public static float ExtraPassW(int bars) => 6f + 18f * Mathf.Max(1, bars);

    class Bead
    {
        public KeyBlock kb; public int island = -1, moon = -1, col, lane; public RectTransform rt; public InkPainter art; public HudButton btn;
        public float x, w; public bool hover, lit; public float shownScale = 1f;
        public int passes = 1, bars = 1; public bool home;   // v5: the island's passes (its repeat within its column's), its bars, the home house
        public int job; public bool keyboard, swap; public RectTransform badge;   // v6: its job badge, a keyboard's piano look, the swap pulse
        public bool stairs, phrase, launch; public float y0;                     // v7: a staircase / a phrase bar / launching; its ground on the rail
    }
    struct Arc { public int from, to; public Color color; }
    readonly List<Arc> arcs = new List<Arc>();
    int swapJob; bool swapDirty;
    struct DrumSection { public int start, end, layers; }
    readonly List<DrumSection> drumSections = new List<DrumSection>();
    readonly List<int> moonStarts = new List<int>();
    int moonGhost = -1; float drumY = -14f;
    struct Col { public float x0, w; public int bars, top, bottom, passes; public bool any; public int section; public float ground, groundShown, rise; public bool groundInit; }
    struct Sec { public int first, last, bars, ghosts, role; public float x0, x1, ghostX; public string letter; }
    readonly List<Sec> secs = new List<Sec>();
    readonly List<int> secStarts = new List<int>();
    readonly List<Arc> flows = new List<Arc>();
    readonly List<int[]> flowBeads = new List<int[]>();   // §21: each long grid's beads, west to east
    readonly List<Vector2> lgPts = new List<Vector2>(16);
    int BeadOf(KeyBlock kb) { if (kb == null) return -1; for (int k = 0; k < beads.Count; k++) if (beads[k].kb == kb) return k; return -1; }
    readonly List<int> launches = new List<int>();
    readonly List<Vector2> ghostMeasures = new List<Vector2>();
    RectTransform labelsRt; readonly List<TMPro.TextMeshProUGUI> labels = new List<TMPro.TextMeshProUGUI>(); readonly List<Image> roleImgs = new List<Image>();
    float bracketY = -40f; bool groundsInit;

    RectTransform root, beadsRt, phRt, focusRt, loopRt;
    InkPainter line, focusArt; CubeGlyph playhead; HudButton loopBtn, focusX; CanvasGroup group, focusGroup;
    readonly List<Bead> beads = new List<Bead>(), moons = new List<Bead>();
    readonly List<Bead> pool = new List<Bead>();
    Col[] cols = new Col[0];
    float x0Line = -60f, x1Line = 60f, scale = 1f;
    bool dirty = true, loopShown, focusShown; int focusCol = -2;
    // playhead
    int phCol = -1, phBar = -1; float phFrom, phTo, phT = 1f, phX; bool phParked = true;
    // drop ghost
    Rect ghostRect; bool ghostOn; int ghostKey = int.MinValue;
    static int modeFree = -9, modeInsert = -9, modeMerge = -9, modeStack = -9, modeBlocked = -9;

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
        SongManager.I.OnSongRebuilt += MarkDirty; SongManager.I.OnColumnsChanged += MarkDirty;
        SongManager.I.OnSectionsChanged += MarkDirty; SongManager.I.OnGroundsChanged += MarkDirty;   // v7
        SongManager.I.OnLongGridsChanged += MarkDirty;                                                 // v7 §21 (a grid moved into / out of a lane)
        History.OnChanged += MarkDirty; FocusLoop.OnChanged += MarkFocus;
        subscribed = true;
    }
    void Unsubscribe()
    {
        if (!subscribed) return;
        if (SongManager.I != null) { SongManager.I.OnSongRebuilt -= MarkDirty; SongManager.I.OnColumnsChanged -= MarkDirty; SongManager.I.OnSectionsChanged -= MarkDirty; SongManager.I.OnGroundsChanged -= MarkDirty; SongManager.I.OnLongGridsChanged -= MarkDirty; }
        History.OnChanged -= MarkDirty; FocusLoop.OnChanged -= MarkFocus;
        subscribed = false;
    }
    public void MarkDirty() { dirty = true; }
    void MarkFocus() { focusCol = -2; }

    void BuildParts()
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        line = InkPainter.Create(root, "Line", root.sizeDelta, PaintLine);
        beadsRt = HudKit.Stretch(root, "Beads");
        labelsRt = HudKit.Stretch(root, "Labels");   // v7: section letters, names, measure numbers
        // the curl's hit area (right end of the line)
        var lh = HudKit.Hit(root, "LoopCurl", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 36f));
        loopRt = lh.rectTransform;
        loopBtn = HudKit.Control(lh, "loop", () => { GlobalClock.LoopSong = !GlobalClock.LoopSong; History.Push(); line.Repaint(); }, null, null, line);
        // the playhead: a mini cube in its own canvas (it moves every frame while playing)
        phRt = HudKit.Node(root, "Playhead", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
        HudKit.SubCanvas(phRt.gameObject, false);
        playhead = phRt.gameObject.AddComponent<CubeGlyph>();
        playhead.color = Comic.Cream; playhead.raycastTarget = false;
        // the focus-loop badge: an ink loop around the looped stack + a ✕ sticker (its own group: stays bright while the rail is dimmed)
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
    static int ModeOf(string name)
    {
        return Enum.IsDefined(typeof(IslandGhost.Mode), name) ? (int)Enum.Parse(typeof(IslandGhost.Mode), name) : -1;
    }

    Bead Take()
    {
        Bead b;
        if (pool.Count > 0) { b = pool[pool.Count - 1]; pool.RemoveAt(pool.Count - 1); b.rt.gameObject.SetActive(true); }
        else
        {
            b = new Bead();
            b.rt = HudKit.Node(beadsRt, "Bead", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, HitH));
            b.art = b.rt.gameObject.AddComponent<InkPainter>();
            var bb = b;
            b.art.onPaint = p => PaintBead(p, bb);
            b.art.hitTest = lp => BeadHit(bb, lp);
            b.btn = HudKit.Control(b.art, "go there", null, null, null, b.art);
            b.btn.GetComponent<InkHover>().hoverScale = 1.12f;
            var holder = HudKit.Node(b.rt, "Job", new Vector2(0f, 0.5f), new Vector2(5f, 3f), new Vector2(BadgePx, BadgePx));
            b.badge = JobBadge.Build(holder, 0, BadgePx);
        }
        var bead = b;
        b.btn.onClick = () => ClickBead(bead);
        b.btn.onRightClick = () => SeekBead(bead);
        b.hover = false; b.lit = false; b.shownScale = 1f; b.rt.localScale = Vector3.one;
        b.job = 0; b.keyboard = false; b.swap = false; b.stairs = false; b.phrase = false; b.launch = false; b.y0 = 0f;
        if (b.badge != null) { JobBadge.Set(b.badge, 0, BadgePx); b.badge.localScale = Vector3.one; }
        return b;
    }

    void Rebuild()
    {
        dirty = false;
        foreach (var b in beads) { if (b.hover && b.kb != null) b.kb.SetHover(false); b.rt.gameObject.SetActive(false); pool.Add(b); }
        foreach (var b in moons) { if (b.hover && b.kb != null) b.kb.SetHover(false); b.rt.gameObject.SetActive(false); pool.Add(b); }
        beads.Clear(); moons.Clear();
        secs.Clear(); flows.Clear(); flowBeads.Clear(); launches.Clear(); ghostMeasures.Clear();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) { cols = new Col[0]; HideLabels(0, 0); line.Repaint(); return; }
        var islands = sm.Islands;
        int ncol = sm.ColumnCount;
        foreach (var kb in islands) if (kb != null) ncol = Mathf.Max(ncol, kb.column + 1);
        var old = cols;
        if (cols.Length != ncol) cols = new Col[ncol];
        for (int c = 0; c < ncol; c++)
        {
            float shown = c < old.Length && old[c].groundInit ? old[c].groundShown : float.NaN;
            cols[c] = new Col();
            cols[c].bars = Mathf.Clamp(sm.ColumnBars(c), 1, 8);   // v7: the column's pass length in measures (a 2-bar column is two measures wide)
            cols[c].ground = Mathf.Clamp(sm.GroundOf(c) * HeightPx, -GroundClampPx, GroundClampPx); cols[c].rise = sm.StairRiseOf(c) * HeightPx;   // (clamped: a tall stack stays on screen)
            cols[c].groundShown = float.IsNaN(shown) || !groundsInit ? cols[c].ground : shown; cols[c].groundInit = true;
        }
        groundsInit = true;
        // one bead per island (v7: a phrase is a bar over the time it spans, above its columns' stacks)
        for (int i = 0; i < islands.Count; i++)
        {
            var kb = islands[i]; if (kb == null) continue;
            var b = Take(); b.kb = kb; b.island = i; b.moon = -1; b.col = Mathf.Clamp(kb.column, 0, Mathf.Max(0, ncol - 1));
            b.rt.gameObject.name = "Bead" + i;
            b.passes = Mathf.Clamp(kb.repeat, 1, 4); b.home = Vibe.IsHome(kb.chordRootMIDI) && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase;
            b.keyboard = kb.IsKeyboard; b.stairs = kb.IsStairs; b.phrase = kb.IsPhrase; b.launch = kb.launch && !kb.IsStairs; b.job = Harmony.Job(kb);
            JobBadge.Set(b.badge, b.job, BadgePx);
            beads.Add(b);
            if (b.phrase) continue;
            if (!cols[b.col].any) { cols[b.col].any = true; cols[b.col].passes = 1; }
            cols[b.col].passes = Mathf.Max(cols[b.col].passes, b.passes);   // SPEC v5 §2.5: a column's passes = the max repeat of its islands
        }
        for (int c = 0; c < ncol; c++) cols[c].passes = Mathf.Clamp(Mathf.Max(cols[c].passes, sm.ColumnPasses(c)), 1, 4);
        for (int c = 0; c < ncol; c++)
        {
            KeyBlock anchor = null;
            foreach (var b in beads) if (b.col == c && !b.phrase) { if (anchor == null || b.kb.isAnchor && !anchor.isAnchor) anchor = b.kb; }
            if (anchor == null) continue;
            // above: larger z first nearest; below: smaller z
            int up = 0, dn = 0;
            foreach (var b in beads)
            {
                if (b.col != c || b.phrase) continue;
                if (b.kb == anchor) { b.lane = 0; continue; }
                int rank = 0;
                bool above = b.kb.pz > anchor.pz;
                foreach (var o in beads)
                {
                    if (o.col != c || o.phrase || o.kb == anchor || o == b) continue;
                    bool oa = o.kb.pz > anchor.pz;
                    if (oa != above) continue;
                    if (above ? o.kb.pz < b.kb.pz : o.kb.pz > b.kb.pz) rank++;
                }
                b.lane = above ? rank + 1 : -(rank + 1);
                if (above) up = Mathf.Max(up, rank + 1); else dn = Mathf.Max(dn, rank + 1);
            }
            cols[c].top = up; cols[c].bottom = dn;
        }
        // x layout — v7 (SPEC v7 §13.2): inside a section the columns touch, a gap between sections; a section short of 4 measures keeps dotted
        // slots for the measures it still needs (support: "a section is 4 measures")
        float x = 0f;
        secStarts.Clear(); secStarts.AddRange(sm.SectionStarts());
        if (secStarts.Count == 0 && ncol > 0) secStarts.Add(0);
        for (int s = 0; s < secStarts.Count; s++)
        {
            int first = secStarts[s], last = s + 1 < secStarts.Count ? secStarts[s + 1] - 1 : ncol - 1;
            if (first >= ncol) break;
            if (s > 0) x += SecGap;
            var sec = new Sec { first = first, last = Mathf.Min(last, ncol - 1), x0 = x, letter = SectionHeader.LetterOf(s), role = sm.SectionRole(s) };
            for (int c = sec.first; c <= sec.last; c++)
            {
                float w = FirstPassW(cols[c].bars) + (cols[c].passes - 1) * ExtraPassW(cols[c].bars);   // v5: the belt widens the column
                cols[c].x0 = x; cols[c].w = w; cols[c].section = s;
                sec.bars += cols[c].bars;
                x += w;
            }
            sec.ghosts = Mathf.Max(0, ProjectConfig.SectionBars - sec.bars);
            sec.ghostX = x;
            x += sec.ghosts * GhostW;
            sec.x1 = x;
            secs.Add(sec);
        }
        float natural = x;
        var ms = sm.Moons;
        if (ms.Count > 0) natural += MoonGap + ms.Count * 18f + (ms.Count - 1) * (MoonStep - 18f);
        scale = natural > MaxWidth ? MaxWidth / natural : 1f;
        float off = -natural * scale * 0.5f;
        for (int c = 0; c < ncol; c++) cols[c].x0 = off + cols[c].x0 * scale;
        for (int s = 0; s < secs.Count; s++) { var sc = secs[s]; sc.x0 = off + sc.x0 * scale; sc.x1 = off + sc.x1 * scale; sc.ghostX = off + sc.ghostX * scale; secs[s] = sc; }
        foreach (var b in beads)
        {
            if (b.phrase) continue;
            var col = cols[b.col];
            b.bars = Mathf.Max(1, col.bars); b.passes = Mathf.Clamp(b.passes, 1, col.passes);
            b.w = (FirstPassW(b.bars) + (b.passes - 1) * ExtraPassW(b.bars)) * scale; b.x = col.x0 + b.w * 0.5f;
            b.y0 = 0f;
            b.rt.sizeDelta = new Vector2(Mathf.Max(8f, b.w - 1f), HitH);
        }
        // v7 phrases (SPEC v7 §14): a bar over the song time the phrase spans, a row above its columns' stacks (overlapping phrases stack up)
        var placed = new List<Bead>();
        foreach (var b in beads)
        {
            if (!b.phrase) continue;
            float s0, s1; sm.PhraseSpan(b.kb, out s0, out s1);
            float xa = BeatToX(sm, s0), xb = BeatToX(sm, Mathf.Max(s0 + 0.01f, s1 - 0.001f));
            int c0 = ColumnOfBeat(sm, s0), c1 = ColumnOfBeat(sm, Mathf.Max(s0, s1 - 0.001f));
            int topLane = 0; for (int c = Mathf.Max(0, c0); c <= c1 && c < ncol; c++) topLane = Mathf.Max(topLane, cols[c].top);
            int lane = topLane + 1;
            for (bool bumped = true; bumped; )
            {
                bumped = false;
                foreach (var o in placed) { float o0, o1; sm.PhraseSpan(o.kb, out o0, out o1); if (o.lane == lane && o0 < s1 - 0.01f && s0 < o1 - 0.01f) { lane++; bumped = true; } }
            }
            b.col = Mathf.Clamp(c0, 0, Mathf.Max(0, ncol - 1)); b.lane = lane;
            b.bars = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, b.kb.phraseBeats) / (float)Mathf.Max(1, GlobalClock.BeatsPerBar)));
            b.w = Mathf.Max(10f, xb - xa); b.x = (xa + xb) * 0.5f; b.y0 = 0f;
            b.rt.sizeDelta = new Vector2(b.w, HitH);
            placed.Add(b);
        }
        foreach (var b in beads) { PlaceBead(b); b.art.Repaint(); }
        float mx = off + (x + MoonGap) * scale;
        for (int m = 0; m < ms.Count; m++)
        {
            if (ms[m] == null) continue;
            var b = Take(); b.kb = ms[m]; b.island = -1; b.moon = m; b.col = -1; b.lane = 0; b.job = 0;
            b.rt.gameObject.name = "Moon" + m;
            b.w = 17f; b.x = mx + 8.5f; mx += MoonStep;
            b.rt.sizeDelta = new Vector2(HitH, HitH); b.rt.anchoredPosition = new Vector2(b.x, 0f);
            b.art.Repaint();
            moons.Add(b);
        }
        x0Line = off - 14f; x1Line = off + natural * scale + 10f;
        loopRt.anchoredPosition = new Vector2(x1Line + 12f, 8f + EndY());
        phCol = -1; phBar = -1; focusCol = -2; ghostKey = int.MinValue;
        // §21 the LONG GRIDS (package K's SongManager.LongGrids: an extended grid and the grids its extend covers, in its section; a chain of
        // extends A → B → C is ONE long grid) — one bar around the beads of each, in the extended (west-most) grid's colour
        arcs.Clear(); flowBeads.Clear();
        var runs = sm.LongGrids;
        for (int r = 0; runs != null && r < runs.Count; r++)
        {
            var run = runs[r];
            if (run == null || run.Count < 2) continue;
            var mem = new List<int>(run.Count);
            foreach (var m in run) { int bi = BeadOf(m); if (bi >= 0 && !beads[bi].phrase && !mem.Contains(bi)) mem.Add(bi); }
            if (mem.Count < 2) continue;
            mem.Sort((a, c) => beads[a].x.CompareTo(beads[c].x));
            var src = beads[mem[0]].kb;
            Color col = Comic.Opaque(src != null && src.IsKeyboard ? KeyboardIvory : (src != null ? src.chordColor : Comic.Cream));
            flows.Add(new Arc { from = mem[0], to = mem[mem.Count - 1], color = col });
            flowBeads.Add(mem.ToArray());
        }
        // v7: launches — a small ↗ after the bead of every island that launches into the next section (§21: its riser and crash; nothing moves)
        for (int i = 0; i < beads.Count; i++) if (beads[i].launch) launches.Add(i);
        // v6 §11: the drum sections — each distinct Moon start column plays until the next one (or the song's end)
        drumSections.Clear(); moonStarts.Clear();
        for (int m = 0; m < ms.Count; m++) if (ms[m] != null) moonStarts.Add(Mathf.Clamp(sm.MoonStartColumn(m), 0, Mathf.Max(0, ncol - 1)));
        moonStarts.Sort();
        for (int k = 0; k < moonStarts.Count; k++)
        {
            int s = moonStarts[k], layers = 1;
            while (k + 1 < moonStarts.Count && moonStarts[k + 1] == s) { layers++; k++; }
            int e = k + 1 < moonStarts.Count ? moonStarts[k + 1] : ncol;
            if (ncol > 0 && e > s) drumSections.Add(new DrumSection { start = s, end = e, layers = layers });
        }
        // below everything: the drum line, then the section brackets (their letters, names and measure numbers)
        float lowest = -BeadH * 0.5f;
        for (int c = 0; c < ncol; c++) lowest = Mathf.Min(lowest, ColY(c) - cols[c].bottom * LaneStep - BeadH * 0.5f);
        drumY = lowest - 7f;
        bracketY = drumY - (drumSections.Count > 0 ? 13f : 6f);
        LayoutLabels();
        swapDirty = true;
        line.Repaint();
    }

    /// <summary>v7: a bead's place — its column's ground (the landscape) plus its lane (phrases: their row).</summary>
    void PlaceBead(Bead b)
    {
        if (b.moon >= 0) return;
        b.y0 = b.col >= 0 && b.col < cols.Length ? ColY(b.col) : 0f;
        b.rt.anchoredPosition = new Vector2(b.x, b.y0 + b.lane * LaneStep * b.shownScale);
    }

    /// <summary>v7: the rail's ground under column <paramref name="c"/> as drawn now (GroundOf × HeightPx, gliding on twos).</summary>
    float ColY(int c) => c >= 0 && c < cols.Length ? cols[c].groundShown : 0f;
    float EndY() => cols.Length > 0 ? ColY(cols.Length - 1) : 0f;

    /// <summary>v7: the column whose time holds song beat <paramref name="beat"/> (the last one past the end).</summary>
    static int ColumnOfBeat(SongManager sm, float beat)
    {
        int n = sm.ColumnCount;
        for (int c = 0; c < n; c++) if (beat < sm.ColumnStart(c) + sm.ColumnLength(c) - 1e-4f) return c;
        return n - 1;
    }

    /// <summary>v7: a song beat → rail x (linear inside its column's time: the first pass in the bead, further passes over the belt).</summary>
    float BeatToX(SongManager sm, float beat)
    {
        int c = ColumnOfBeat(sm, beat);
        if (c < 0 || c >= cols.Length) return x0Line;
        float len = Mathf.Max(0.001f, sm.ColumnLength(c)), u = Mathf.Clamp01((beat - sm.ColumnStart(c)) / len);
        float x0 = cols[c].x0 + 6f * scale, w = cols[c].w * scale - 12f * scale;
        return x0 + u * w;
    }

    /// <summary>v7: the section letters (a stone disc each, lettered in Bangers; gold when the section has its 4 measures), their names (the
    /// role pictures, §17.1) and the measure numbers 1 2 3 4 under each section's measures — pooled labels, placed on a rebuild.</summary>
    void LayoutLabels()
    {
        int used = 0, roles = 0;
        MeasureNumbers = 0; GhostMeasureCount = 0;
        bool nums = MeasureW * scale >= 9f;
        foreach (var sec in secs)
        {
            var l = Label(used++);
            l.font = Comic.DigitFont; if (Comic.DigitFont != null) l.fontSharedMaterial = Comic.DigitFont.material;
            l.fontSize = 13f; l.color = SectionPlinth.LetterDeep(sec.letter); l.text = sec.letter;   // v7: the letter's colours (SectionPlinth.LetterTint)
            l.rectTransform.anchoredPosition = new Vector2(sec.x0 + 7f, bracketY + 0.5f);
            if (SectionRoles.Valid(sec.role))
            {
                var img = RoleImage(roles++);
                img.sprite = Comic.Glyph(SectionRoles.Glyph(sec.role), 14f, false); img.color = Comic.Ink;
                img.rectTransform.anchoredPosition = new Vector2(sec.x0 + 21f, bracketY + 0.5f);
            }
            int m = 1;
            for (int c = sec.first; c <= sec.last && c < cols.Length; c++)
                for (int k = 0; k < cols[c].bars; k++, m++)
                {
                    MeasureNumbers++;
                    if (!nums) continue;
                    var n = Label(used++);
                    n.font = Comic.Font; if (Comic.Font != null) n.fontSharedMaterial = Comic.Font.material;
                    n.fontSize = 11f; n.color = Comic.A(Comic.Ink, 0.85f); n.text = m.ToString();
                    n.rectTransform.anchoredPosition = new Vector2(cols[c].x0 + (12f + MeasureW * (k + 0.5f)) * scale, bracketY - 9f);
                }
            for (int g = 0; g < sec.ghosts; g++, m++)
            {
                GhostMeasureCount++;
                ghostMeasures.Add(new Vector2(sec.ghostX + (g + 0.5f) * GhostW * scale, ColY(sec.last)));
                if (!nums) continue;
                var n = Label(used++);
                n.font = Comic.Font; if (Comic.Font != null) n.fontSharedMaterial = Comic.Font.material;
                n.fontSize = 11f; n.color = Comic.A(Comic.Ink, 0.4f); n.text = m.ToString();
                n.rectTransform.anchoredPosition = new Vector2(sec.ghostX + (g + 0.5f) * GhostW * scale, bracketY - 9f);
            }
        }
        HideLabels(used, roles);
    }

    TMPro.TextMeshProUGUI Label(int i)
    {
        while (labels.Count <= i)
        {
            var t = HudKit.Words(labelsRt, "Label", "", 11f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
            t.rectTransform.sizeDelta = new Vector2(22f, 16f);
            labels.Add(t);
        }
        var l = labels[i];
        if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        return l;
    }

    Image RoleImage(int i)
    {
        while (roleImgs.Count <= i)
        {
            var rt = HudKit.Node(labelsRt, "Role", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f * Comic.GlyphPad, 14f * Comic.GlyphPad));
            var img = rt.gameObject.AddComponent<Image>(); img.raycastTarget = false; img.preserveAspect = true;
            roleImgs.Add(img);
        }
        var r = roleImgs[i];
        if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
        return r;
    }

    void HideLabels(int used, int roles)
    {
        for (int i = used; i < labels.Count; i++) if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
        for (int i = roles; i < roleImgs.Count; i++) if (roleImgs[i].gameObject.activeSelf) roleImgs[i].gameObject.SetActive(false);
    }

    /// <summary>v7: the grounds glide on twos toward their targets (the rail's landscape follows the world's terraces); true when one moved.</summary>
    bool GlideGrounds()
    {
        bool moved = false;
        for (int c = 0; c < cols.Length; c++)
        {
            float g = cols[c].ground, s = cols[c].groundShown;
            if (Mathf.Abs(g - s) < 0.05f) { if (s != g) { cols[c].groundShown = g; moved = true; } continue; }
            cols[c].groundShown = Mathf.Lerp(s, g, 0.45f); moved = true;
        }
        return moved;
    }

    /// <summary>A keyboard bead's ivory.</summary>
    public static readonly Color KeyboardIvory = new Color(1f, 0.97f, 0.9f);

    /// <summary>v7 (SPEC v7 §13.4): section <paramref name="s"/> just reached 4 measures — its bracket sparkles.</summary>
    public void Sparkle(int s) { sparkleSection = s; sparkleT = Time.unscaledTime; SparkleCount++; if (line != null) line.Repaint(); }
    /// <summary>v7: sparkles shown (tests).</summary>
    public int SparkleCount { get; private set; }
    /// <summary>v7: the column where a dragged melody card would add its phrase (-1 none): a dotted phrase bar on the rail.</summary>
    public int PhraseGhostColumn { get => phraseGhost; set { if (phraseGhost == value) return; phraseGhost = value; if (line != null) line.Repaint(); } }
    int phraseGhost = -1;
    int sparkleSection = -1; float sparkleT = -9f;

    bool Glued(int a, int b)
    {
        var sm = SongManager.I;
        foreach (var x in sm.Islands)
        {
            if (x == null || x.column != a || x.group == 0) continue;
            foreach (var y in sm.Islands) if (y != null && y.column == b && y.group == x.group) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ painting
    static readonly List<Vector2> pts = new List<Vector2>(128);

    void PaintLine(InkPainter p)
    {
        if (SongManager.I == null || !SongManager.I.HasSong) return;
        // the rail: a hand-inked line with a 1 px wobble — v7: it follows the ground (flat under each column at its height, sloping across a
        // stairs column to where the stair ends): the rail becomes a gentle landscape
        pts.Clear();
        LinePoints(pts);
        for (int i = 0; i < pts.Count; i++) pts[i] += new Vector2(0f, p.Still(i) * 1f);
        p.Shift = new Vector2(2f, -2.5f);
        p.Stroke(pts, pts.Count, 3f, Comic.A(Comic.Ink, 0.55f), false, 0f, 0f, 2.2f, true);
        p.Shift = Vector2.zero;
        p.Stroke(pts, pts.Count, 3.2f, Comic.Ink, false, 0f, 0f, 2.4f, true);
        // the loop curl at the right end (solid when the song loops, a dashed ghost when it does not)
        bool on = GlobalClock.LoopSong;
        float ey = EndY();
        pts.Clear();
        Vector2 c = new Vector2(x1Line + 12f, 9f + ey);
        InkPainter.ArcPoints(pts, c, 12f, 9f, -95f, 170f, 22);
        pts.Insert(0, new Vector2(x1Line, ey));
        Vector2 end = pts[pts.Count - 1], dir = (end - pts[pts.Count - 3]).normalized;
        float a = on ? 1f : 0.45f;
        if (on) { p.Shift = new Vector2(2f, -2.5f); p.Stroke(pts, pts.Count, 3.4f, Comic.A(Comic.Ink, 0.5f), false, 0f, 0f, -1f, true); p.Shift = Vector2.zero; }
        p.Stroke(pts, pts.Count, 3.2f, Comic.A(Comic.Ink, a), false, on ? 0f : 4f, on ? 0f : 3.5f, -1f, true);
        p.Head(end + dir * 4f, dir, 8f, 10f, Comic.A(on ? Comic.Cream : Comic.Ink, a), 1.6f, Comic.A(Comic.Ink, a));
        // the dragged card's ghost bead (dotted)
        if (ghostOn) p.RoundRect(ghostRect, BeadH * 0.5f, Comic.A(Comic.Cream, 0.18f), 2f, Comic.Ink, 3.5f, 2.6f);
        // v7: the measures a short section still needs — dotted slots on the line
        foreach (var g in ghostMeasures)
        {
            float gw = Mathf.Max(6f, GhostW * scale - 5f), gh = BeadH * 0.62f;
            p.RoundRect(new Rect(g.x - gw * 0.5f, g.y - gh * 0.5f, gw, gh), gh * 0.5f, Comic.A(Comic.Cream, 0.14f), 1.6f, Comic.A(Comic.Ink, 0.8f), 2.6f, 2.2f);
        }
        // v6 §11: the drum sections under the beads — a thin moon-grey line per section, a little drum at its start (a dot per layered Moon);
        // no line where nothing drums
        foreach (var ds in drumSections)
        {
            if (ds.start < 0 || ds.end > cols.Length || ds.start >= ds.end) continue;
            float xa = cols[ds.start].x0 + 3f, xb = cols[ds.end - 1].x0 + cols[ds.end - 1].w * scale - 3f;
            pts.Clear(); pts.Add(new Vector2(xa + 8f, drumY)); pts.Add(new Vector2(xb, drumY));
            p.Stroke(pts, 2, 5.2f, Comic.A(Comic.Ink, 0.9f), false, 0f, 0f, -1f, true);
            p.Stroke(pts, 2, 2.8f, Comic.Opaque(KeyBlock.MoonColor), false, 0f, 0f, -1f, true);
            p.Disc(new Vector2(xb, drumY), 2.6f, Comic.Ink);   // where the section ends
            PaintDrum(p, new Vector2(xa + 2f, drumY + 1f), 1.25f);
            for (int l = 1; l < ds.layers; l++) p.Disc(new Vector2(xa + 13f + l * 5f, drumY - 6.5f), 1.7f, Comic.Ink);
        }
        if (moonGhost >= 0 && moonGhost < cols.Length)
        {
            float xg = cols[moonGhost].x0 + 5f;
            PaintDrum(p, new Vector2(xg, drumY), 1.25f, true);
        }
        // v7 (SPEC v7 §13.4): the sections — a bracket under each, its letter on a stone disc (gold once the section has its 4 measures), its
        // name's picture beside it (§17.1); the measure numbers are labels under the bracket
        for (int s = 0; s < secs.Count; s++)
        {
            var sec = secs[s];
            bool full = sec.bars >= ProjectConfig.SectionBars;
            Vector2 lc = new Vector2(sec.x0 + 7f, bracketY + 0.5f);
            float xa = sec.x0 + (SectionRoles.Valid(sec.role) ? 30f : 16f), xs = Mathf.Min(sec.x1 - 2f, sec.ghostX), xb = sec.x1 - 2f;
            if (xs > xa) { pts.Clear(); pts.Add(new Vector2(xa, bracketY)); pts.Add(new Vector2(xs, bracketY)); p.Stroke(pts, 2, 2f, Comic.Ink, false, 0f, 0f, -1f, true); }
            if (xb > xs + 2f) { pts.Clear(); pts.Add(new Vector2(Mathf.Max(xa, xs), bracketY)); pts.Add(new Vector2(xb, bracketY)); p.Stroke(pts, 2, 1.6f, Comic.A(Comic.Ink, 0.55f), false, 3f, 2.4f); }
            p.Line(new Vector2(xb, bracketY), new Vector2(xb, bracketY + 5f), full ? 2f : 1.6f, full ? Comic.Ink : Comic.A(Comic.Ink, 0.55f), true);
            // v7 (the user: "not black and white — more resonant colours"): the disc in the letter's colour, pale until the section has its 4
            // measures; its rim and shadow in the colour's deep shade
            Color lt = SectionPlinth.LetterTint(sec.letter), ld = SectionPlinth.LetterDeep(sec.letter);
            p.Disc(lc + new Vector2(1f, -1.5f), 7.6f, Comic.A(ld, 0.6f));
            p.Disc(lc, 7.6f, ld);
            p.Disc(lc, 6.3f, full ? lt : Color.Lerp(lt, Comic.Cream, 0.55f));
            if (SectionRoles.Valid(sec.role)) { Vector2 rc = new Vector2(sec.x0 + 21f, bracketY + 0.5f); p.Disc(rc, 6.8f, SectionRoles.Tint(sec.role)); p.Arc(rc, 6.8f, 1.1f, Comic.Ink); }
            // the sparkle when this section just reached 4 measures
            float st = Time.unscaledTime - sparkleT;
            if (s == sparkleSection && st >= 0f && st < SparkleSeconds)
            {
                float k = Look.Stepped(st / SparkleSeconds);
                for (int r = 0; r < 8; r++)
                {
                    float ang = (r * 45f + 22.5f) * Mathf.Deg2Rad; Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float r0 = 10f + 7f * k, r1 = r0 + (r % 2 == 0 ? 7f : 4f) * (1f - k * 0.5f);
                    p.Line(lc + d * r0, lc + d * r1, 2f, Comic.A(lt, 1f - k), true);
                }
                p.Disc(lc + new Vector2(13f, 11f) * (0.6f + k), 1.6f, Comic.A(ld, 1f - k));
            }
        }
        // §21 the long grids: ONE bar around the beads of each (its rim in the extended grid's colour, inked; the beads sit on it), a small arrow at
        // its right end — the same grid going on to the right, the chord changing bead by bead
        for (int g = 0; g < flows.Count && g < flowBeads.Count; g++)
        {
            var lg = flows[g]; var mem = flowBeads[g];
            if (lg.from < 0 || lg.to < 0 || lg.from >= beads.Count || lg.to >= beads.Count) continue;
            Bead bf = beads[lg.from], bt = beads[lg.to];
            float y = bf.y0 + bf.lane * LaneStep * bf.shownScale, hh = BeadH + 2f * LongRim, yEnd = bt.y0 + bt.lane * LaneStep * bt.shownScale;
            Color fill = Comic.A(Color.Lerp(lg.color, Comic.Cream, 0.25f), 0.95f);
            bool level = true;
            foreach (int bi in mem) if (bi >= 0 && bi < beads.Count && Mathf.Abs(beads[bi].y0 + beads[bi].lane * LaneStep * beads[bi].shownScale - y) > 0.5f) level = false;
            float xEnd = bt.x + bt.w * 0.5f + LongRim;
            if (level)
            {
                var r = Rect.MinMaxRect(bf.x - bf.w * 0.5f - LongRim, y - hh * 0.5f, xEnd, y + hh * 0.5f);
                p.Shift = new Vector2(1.5f, -2f); p.RoundRect(r, hh * 0.5f, Comic.A(Comic.Ink, 0.7f)); p.Shift = Vector2.zero;
                p.RoundRect(r, hh * 0.5f, fill, 2.2f, Comic.Ink);
            }
            else
            {
                // grids at different heights on the rail (another lane, a step of the landscape): still ONE bar — a capsule along each bead,
                // joined at the seams, inked once around the whole
                lgPts.Clear();
                foreach (int bi in mem)
                {
                    if (bi < 0 || bi >= beads.Count) continue;
                    var b = beads[bi]; float yb = b.y0 + b.lane * LaneStep * b.shownScale;
                    float xa = b.x - b.w * 0.5f - LongRim + hh * 0.5f, xb = Mathf.Max(xa, b.x + b.w * 0.5f + LongRim - hh * 0.5f);
                    lgPts.Add(new Vector2(xa, yb)); lgPts.Add(new Vector2(xb, yb));
                }
                p.Shift = new Vector2(1.5f, -2f);
                for (int k = 0; k + 1 < lgPts.Count; k++) p.Line(lgPts[k], lgPts[k + 1], hh, Comic.A(Comic.Ink, 0.7f), true);
                p.Shift = Vector2.zero;
                for (int k = 0; k + 1 < lgPts.Count; k++) p.Line(lgPts[k], lgPts[k + 1], hh + 4.4f, Comic.Ink, true);
                for (int k = 0; k + 1 < lgPts.Count; k++) p.Line(lgPts[k], lgPts[k + 1], hh, fill, true);
            }
            Vector2 tip = new Vector2(xEnd + 6.5f, yEnd);
            p.Line(new Vector2(xEnd - 1f, yEnd), tip, 2.2f, Comic.Ink, true);
            p.Head(tip + new Vector2(1.5f, 0f), Vector2.right, 4.5f, 5.5f, Comic.Ink);
        }
        // v7: launch — a small spring arrow ↗ after the bead
        foreach (int i in launches)
        {
            if (i < 0 || i >= beads.Count) continue;
            var b = beads[i];
            Vector2 s0 = new Vector2(b.x + b.w * 0.5f - 3f, b.y0 + b.lane * LaneStep + BeadH * 0.5f - 1f), s1 = s0 + new Vector2(7f, 7f);
            p.Line(s0, s1, 3.6f, Comic.Ink, true);
            p.Line(s0, s1, 1.8f, Comic.Pop, true);
            p.Head(s1 + new Vector2(1.5f, 1.5f), new Vector2(0.7071f, 0.7071f), 5f, 6f, Comic.Pop, 1.1f, Comic.Ink);
        }
        // v7: where a dragged melody card would add its phrase — a dotted bar over the column
        if (phraseGhost >= 0 && phraseGhost < cols.Length)
        {
            var cg = cols[phraseGhost];
            float y = ColY(phraseGhost) + (cg.top + 1) * LaneStep, w = FirstPassW(cg.bars) * scale;
            p.RoundRect(new Rect(cg.x0 + 2f, y - BeadH * 0.35f, w - 4f, BeadH * 0.7f), BeadH * 0.3f, Comic.A(IslandHeader.PhraseTint, 0.25f), 2f, Comic.Ink, 3.5f, 2.6f);
        }
    }

    /// <summary>v7: the line through the landscape — flat under each column at its ground, sloping across a stairs column to the next ground,
    /// straight across the section gaps; resampled every 14 px (the ink's wobble).</summary>
    void LinePoints(List<Vector2> into)
    {
        var raw = lineRaw; raw.Clear();
        raw.Add(new Vector2(x0Line, ColY(0)));
        for (int c = 0; c < cols.Length; c++)
        {
            float xa = cols[c].x0, xb = cols[c].x0 + cols[c].w * scale;
            float ya = ColY(c), yb = Mathf.Abs(cols[c].rise) > 0.01f ? (c + 1 < cols.Length ? ColY(c + 1) : ya + cols[c].rise) : ya;
            raw.Add(new Vector2(xa, ya));
            raw.Add(new Vector2(xb, yb));
        }
        raw.Add(new Vector2(x1Line, EndY()));
        into.Add(raw[0]);
        for (int i = 1; i < raw.Count; i++)
        {
            Vector2 a = raw[i - 1], b = raw[i];
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 14f));
            for (int k = 1; k <= n; k++) into.Add(Vector2.Lerp(a, b, k / (float)n));
        }
    }
    readonly List<Vector2> lineRaw = new List<Vector2>(64);

    void PaintBead(InkPainter p, Bead b)
    {
        if (b.kb == null) return;
        Rect r = p.Area;
        Color chord = Comic.Opaque(b.kb.IsMoon ? KeyBlock.MoonColor : b.kb.chordColor);
        bool asleep = b.kb.sleep;
        if (b.moon >= 0)
        {
            float mr = b.w * 0.5f;   // the drawn moon keeps its 17 px inside the 28 px hit square
            p.Disc(r.center + new Vector2(1.5f, -2f), mr, Comic.A(Comic.Ink, 0.8f));
            p.Disc(r.center, mr, Comic.Ink);
            p.Disc(r.center, mr - 2f, chord);
            return;
        }
        float bh = Mathf.Min(r.height, BeadH * b.shownScale);   // the drawn bead, centred in its taller hit rect
        r = new Rect(r.xMin, r.center.y - bh * 0.5f, r.width, bh);
        if (b.phrase) { PaintPhraseBead(p, r, b); return; }
        if (b.keyboard) chord = KeyboardIvory;
        if (b.stairs) chord = Comic.Opaque(IslandHeader.StairStone);
        // v6: a bead whose job matches the hovered deck card wears a cream halo (it pulses on twos)
        if (b.swap) p.RoundRect(new Rect(r.xMin - 3.5f, r.yMin - 3.5f, r.width + 7f, r.height + 7f), r.height * 0.5f + 3.5f, Comic.A(Comic.Cream, 0.35f), 2f, Comic.A(Comic.Cream, 0.95f), 3f, 2f);
        // v5: the first pass is the bead; every further pass a slot of the belt (a dashed line through it and a ghost bead)
        float first = FirstPassW(b.bars), extra = ExtraPassW(b.bars), total = first + (b.passes - 1) * extra;
        float k = r.width / Mathf.Max(1f, total);
        if (b.passes > 1)
        {
            float y = r.center.y, x0 = r.xMin + first * k - 2f, x1 = r.xMax - 2f;
            pts.Clear(); pts.Add(new Vector2(x0, y)); pts.Add(new Vector2(x1, y));
            p.Stroke(pts, 2, 2.2f, Comic.Ink, false, 3.2f, 2.6f);
            for (int s = 1; s < b.passes; s++)
            {
                float sx0 = r.xMin + (first + (s - 1) * extra) * k + 3f, sx1 = r.xMin + (first + s * extra) * k - 1f;
                float gh = r.height * 0.66f;
                var g = new Rect(sx0, y - gh * 0.5f, Mathf.Max(4f, sx1 - sx0), gh);
                p.RoundRect(g, gh * 0.5f, Comic.A(Color.Lerp(chord, Comic.Cream, 0.35f), asleep ? 0.3f : 0.85f), 1.5f, Comic.Ink, 2.6f, 2f);
            }
            r = new Rect(r.xMin, r.yMin, first * k, r.height);
        }
        float rad = r.height * 0.5f;
        if (b.stairs) { PaintStairsBead(p, r, b, asleep); return; }
        if (asleep)
        {
            // hollow: a dashed outline in the chord colour over a pale wash (it is still there, it just sleeps)
            p.RoundRect(r, rad, Comic.A(chord, 0.16f), 2f, Comic.Ink, 3.5f, 2.5f);
            return;
        }
        p.Shift = new Vector2(1.5f, -2f);
        p.RoundRect(r, rad, Comic.A(Comic.Ink, 0.75f));
        p.Shift = Vector2.zero;
        p.RoundRect(r, rad, chord, 2f, Comic.Ink);
        if (b.keyboard)
        {
            // v6: a piano strip — ink black keys in twos and threes along its top half
            float kw = Mathf.Max(3.2f, r.height * 0.3f);
            int n = Mathf.Max(2, Mathf.FloorToInt((r.width - rad) / kw));
            for (int kk = 1; kk < n; kk++)
            {
                int d = kk % 7;
                if (d == 3 || d == 0) continue;   // the gaps between E-F and B-C
                float x = r.xMin + rad * 0.5f + kk * kw;
                if (x > r.xMax - rad * 0.6f) break;
                p.RoundRect(new Rect(x - kw * 0.3f, r.center.y - 0.5f, kw * 0.6f, r.height * 0.5f - 1.5f), 0.8f, Comic.Ink);
            }
            return;
        }
        // a small paper glint on the top-left (the bead's roundness)
        p.Disc(new Vector2(r.xMin + rad * 0.9f, r.center.y + rad * 0.35f), rad * 0.28f, rad * 0.18f, Comic.A(Color.white, 0.65f), 10);
    }

    /// <summary>v6 §11: a little drum (moon-grey shell, cream skin, ink rims) — where a drum section starts; dotted: a Moon card's drop.</summary>
    static void PaintDrum(InkPainter p, Vector2 c, float k, bool ghost = false)
    {
        float w = 6.5f * k, h = 5f * k;
        Color shell = ghost ? Comic.A(Comic.Cream, 0.25f) : Comic.Opaque(KeyBlock.MoonColor);
        pts.Clear();
        pts.Add(c + new Vector2(-w, h * 0.35f)); pts.Add(c + new Vector2(w, h * 0.35f)); pts.Add(c + new Vector2(w * 0.9f, -h)); pts.Add(c + new Vector2(-w * 0.9f, -h));
        if (ghost) p.Stroke(pts, 4, 1.6f, Comic.Cream, true, 2.4f, 2f);
        else p.InkFill(pts, 4, shell, 1.4f, Comic.Ink);
        p.Disc(c + new Vector2(0f, h * 0.35f), w, h * 0.45f, ghost ? Comic.A(Comic.Cream, 0.35f) : Comic.Ink, 16);
        if (!ghost) p.Disc(c + new Vector2(0f, h * 0.38f), w - 1.4f, h * 0.45f - 1.2f, Comic.Cream, 16);
    }

    /// <summary>v7: a stairs bead — a little staircase of ink stone in the bead's first pass, falling (down to the right) or climbing.</summary>
    static void PaintStairsBead(InkPainter p, Rect r, Bead b, bool asleep)
    {
        bool up = b.kb != null && b.kb.stairDir > 0;
        int n = 4;
        float sw = r.width / n, a = asleep ? 0.35f : 1f;
        Color stone = Comic.A(Comic.Opaque(IslandHeader.StairStone), a);
        for (int k = 0; k < n; k++)
        {
            int h = up ? k : n - 1 - k;
            var sr = new Rect(r.xMin + k * sw, r.yMin, sw + 0.6f, r.height * (0.34f + 0.22f * h));
            if (!asleep) { p.Shift = new Vector2(1.2f, -1.6f); p.RoundRect(sr, 1.2f, Comic.A(Comic.Ink, 0.75f)); p.Shift = Vector2.zero; }
            p.RoundRect(sr, 1.2f, stone, 1.4f, Comic.A(Comic.Ink, a), asleep ? 2.4f : 0f, asleep ? 2f : 0f);
        }
    }

    /// <summary>v7: a phrase bar — a strip of the roll's colour over the time it spans, a line at every measure, a few note dashes.</summary>
    static void PaintPhraseBead(InkPainter p, Rect r, Bead b)
    {
        bool asleep = b.kb != null && b.kb.sleep;
        float rad = r.height * 0.45f;
        Color fill = Comic.A(Comic.Opaque(IslandHeader.PhraseTint), asleep ? 0.3f : 1f);
        if (!asleep) { p.Shift = new Vector2(1.5f, -2f); p.RoundRect(r, rad, Comic.A(Comic.Ink, 0.75f)); p.Shift = Vector2.zero; }
        p.RoundRect(r, rad, fill, 2f, Comic.Ink, asleep ? 3.5f : 0f, asleep ? 2.5f : 0f);
        int bars = Mathf.Max(1, b.bars);
        for (int k = 1; k < bars; k++) { float x = r.xMin + r.width * k / bars; p.Line(new Vector2(x, r.yMin + 2f), new Vector2(x, r.yMax - 2f), 1.2f, Comic.A(Comic.Ink, 0.7f)); }
        int notes = Mathf.Clamp(Mathf.RoundToInt(r.width / 9f), 2, 16);
        for (int k = 0; k < notes; k++)
        {
            float u = (k + 0.5f) / notes, y = r.center.y + Mathf.Sin(k * 1.7f + 0.4f) * r.height * 0.22f;
            p.Line(new Vector2(r.xMin + r.width * u - 2f, y), new Vector2(r.xMin + r.width * u + 2f, y), 1.8f, Comic.A(Comic.Ink, asleep ? 0.4f : 0.85f), true);
        }
    }

    /// <summary>A tiny cream house with an ink outline (the key's home chord).</summary>
    static void PaintHouse(InkPainter p, Vector2 c, float s)
    {
        pts.Clear();
        pts.Add(c + new Vector2(-0.95f * s, -0.9f * s)); pts.Add(c + new Vector2(0.95f * s, -0.9f * s)); pts.Add(c + new Vector2(0.95f * s, 0.1f * s));
        pts.Add(c + new Vector2(0f, 1.0f * s)); pts.Add(c + new Vector2(-0.95f * s, 0.1f * s));
        p.InkFill(pts, pts.Count, Comic.Cream, 1.2f, Comic.Ink);
        p.RoundRect(new Rect(c.x - 0.28f * s, c.y - 0.9f * s, 0.56f * s, 0.75f * s), 0.1f * s, Comic.Ink);
    }

    /// <summary>A bead's hit area: its 28 px rect, cut halfway to a stacked neighbour's lane (so a press lands on the nearer bead).</summary>
    bool BeadHit(Bead b, Vector2 lp)
    {
        if (b.moon >= 0 || b.phrase || b.col < 0 || b.col >= cols.Length) return true;
        float half = LaneStep * b.shownScale * 0.5f;
        if (b.lane < cols[b.col].top && lp.y > half) return false;
        if (b.lane > -cols[b.col].bottom && lp.y < -half) return false;
        return true;
    }

    void PaintFocus(InkPainter p)
    {
        // the loop curl motif around the stack: an ellipse drawn in one stroke that overshoots its start, with a little arrowhead
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
    void ClickBead(Bead b)
    {
        if (b.moon >= 0) { if (UIManager.I != null) UIManager.I.SelectMoon(b.moon); return; }
        if (b.island >= 0 && UIManager.I != null) UIManager.I.SelectMeasure(b.island, true);
        if (b.kb != null && OrbitCamera.I != null) OrbitCamera.I.FrameIsland(b.kb);
    }

    void SeekBead(Bead b)
    {
        var sm = SongManager.I;
        if (sm == null || b.island < 0 || b.island >= sm.MeasureStarts.Count) return;
        GlobalClock.Seek(sm.MeasureStarts[b.island]);
        SequenceMaster.ResetAllCubes();
    }

    // ------------------------------------------------------------------ per frame
    int tick = int.MinValue;

    void Update()
    {
        if (line == null || phRt == null) return;   // not built (or a domain reload dropped the references)
        Subscribe();
        var sm = SongManager.I;
        bool has = sm != null && sm.HasSong;
        if (group != null) { float a = !has ? 0f : (dimmed ? 0.5f : 1f); if (group.alpha != a) group.alpha = a; }
        if (!has) { if (beads.Count > 0 || moons.Count > 0) Rebuild(); HudKit.SetActive(phRt, false); HudKit.SetActive(focusRt, false); return; }
        if (dirty || beads.Count != sm.Islands.Count || moons.Count != CountMoons(sm)) Rebuild();
        if (loopShown != GlobalClock.LoopSong) { loopShown = GlobalClock.LoopSong; line.Repaint(); }
        UpdateHover(sm);
        UpdateV7();
        UpdatePlayhead(sm);
        UpdateFocus(sm);
        UpdateGhost(sm);
    }

    static int CountMoons(SongManager sm) { int n = 0; foreach (var m in sm.Moons) if (m != null) n++; return n; }

    int v7Tick = int.MinValue;
    /// <summary>v7 on twos: the landscape glides to new grounds (the beads ride along), a section's sparkle animates.</summary>
    void UpdateV7()
    {
        int t = HudKit.TwosTick;
        if (t == v7Tick) return;
        v7Tick = t;
        if (GlideGrounds()) { foreach (var b in beads) PlaceBead(b); loopRt.anchoredPosition = new Vector2(x1Line + 12f, 8f + EndY()); line.Repaint(); phCol = -1; focusCol = -2; ghostKey = int.MinValue; }
        if (sparkleSection >= 0 && Time.unscaledTime - sparkleT < SparkleSeconds + 0.1f) line.Repaint();
    }

    bool dimmed;
    /// <summary>The inspector is open: the rail dims to 50 % (the focus badge stays bright).</summary>
    public void SetDimmed(bool on)
    {
        dimmed = on;
        if (group != null) group.blocksRaycasts = !on;
    }

    void UpdateHover(SongManager sm)
    {
        var world = PathManager.I != null ? PathManager.I.hoverIsland : null;
        int lit = GlobalClock.IsPlaying ? sm.LitColumn : -1;
        bool step = HudKit.TwosTick != tick;
        if (step) tick = HudKit.TwosTick;
        for (int i = 0; i < beads.Count; i++)
        {
            var b = beads[i];
            bool hov = b.btn.Hover;
            if (hov != b.hover) { b.hover = hov; if (b.kb != null) b.kb.SetHover(hov); }
            if (!step) continue;
            float want = b.phrase ? (world == b.kb ? 1.06f : 1f) : (b.col == lit ? 1.2f : (world == b.kb ? 1.1f : 1f));   // (v7: a phrase bar spans columns: it never grows with one)
            if (Mathf.Abs(b.shownScale - want) > 0.002f)
            {
                b.shownScale = Mathf.Abs(want - b.shownScale) < 0.03f ? want : Mathf.Lerp(b.shownScale, want, 0.6f);
                // the stack grows around the line (the bead's own hover squash rides on the InkHover scale of its object)
                b.rt.anchoredPosition = new Vector2(b.x, b.y0 + b.lane * LaneStep * b.shownScale);
                b.rt.sizeDelta = new Vector2(Mathf.Max(8f, b.w - 1f) * b.shownScale, Mathf.Max(HitH, BeadH * b.shownScale));
                b.art.Repaint();
            }
        }
        for (int i = 0; i < moons.Count; i++)
        {
            var b = moons[i];
            bool hov = b.btn.Hover;
            if (hov != b.hover) { b.hover = hov; if (b.kb != null) b.kb.SetHover(hov); }
        }
        // v6: the beads of a hovered card's job pulse (a halo + the badge's heartbeat on twos)
        if (swapDirty)
        {
            swapDirty = false;
            int n = 0;
            foreach (var b in beads)
            {
                bool on = swapJob > 0 && b.job == swapJob;
                if (on) n++;
                if (on != b.swap) { b.swap = on; b.art.Repaint(); if (!on && b.badge != null) b.badge.localScale = Vector3.one; }
            }
            PulsingCount = n;
        }
        if (step && swapJob > 0)
        {
            int beat = HudKit.TwosTick % 6;
            float bs = beat == 0 ? 1.4f : (beat == 1 ? 1.2f : 1f);
            foreach (var b in beads) if (b.swap && b.badge != null) b.badge.localScale = new Vector3(bs, bs, 1f);
        }
    }

    void UpdatePlayhead(SongManager sm)
    {
        if (cols.Length == 0) { HudKit.SetActive(phRt, false); return; }
        HudKit.SetActive(phRt, true);
        float beat = GlobalClock.SongBeat;
        bool atStart = !GlobalClock.IsPlaying && beat < 0.01f;
        int col = -1, bar = 0, pass = 0;
        if (!atStart)
        {
            col = Mathf.Clamp(GlobalClock.IsPlaying ? sm.LitColumn : sm.ActiveColumn(beat), 0, cols.Length - 1);
            int bars = Mathf.Max(1, cols[col].bars), passes = Mathf.Max(1, cols[col].passes);
            float local = beat - sm.ColumnStart(col), passLen = passes > 1 ? Mathf.Max(0.001f, sm.PassLength(col)) : float.MaxValue;
            pass = passes > 1 ? Mathf.Clamp(Mathf.FloorToInt(local / passLen + 1e-4f), 0, passes - 1) : 0;
            if (passes > 1) local -= pass * passLen;
            bar = Mathf.Clamp(Mathf.FloorToInt(local / Mathf.Max(1, GlobalClock.BeatsPerBar) + 1e-4f), 0, bars - 1);
        }
        PlayheadPass = col < 0 ? -1 : pass;
        int step = pass * 8 + bar;
        if (col != phCol || step != phBar)
        {
            bool first = phCol == -1 && phBar == -1 && phParked;
            phCol = col; phBar = step; phParked = col < 0;
            float target;
            if (col < 0) target = x0Line + 8f;
            else
            {
                var c = cols[col]; int nb = Mathf.Max(1, c.bars);
                // v5: pass 0 in the bead (v4's spacing), pass p in its belt slot
                float u = pass == 0 ? FirstPassW(nb) * (bar + 0.5f) / nb : FirstPassW(nb) + (pass - 1) * ExtraPassW(nb) + ExtraPassW(nb) * (bar + 0.5f) / nb;
                target = c.x0 + u * scale;
            }
            phFrom = first ? target : phX; phTo = target; phT = first ? 1f : 0f;
            if (!first && GlobalClock.IsPlaying) playhead.Hop(0.9f);
        }
        // the hop's sideways glide is smooth (the playhead is never on twos); its lift is CubeGlyph's hop
        if (phT < 1f) phT = Mathf.Min(1f, phT + Time.unscaledDeltaTime / 0.2f);
        phX = Mathf.Lerp(phFrom, phTo, Ease.OutCubic(phT));
        float top = (col >= 0 ? (cols[col].top * LaneStep + BeadH * 0.5f) * (GlobalClock.IsPlaying ? 1.2f : 1f) : BeadH * 0.5f) + (col >= 0 ? ColY(col) : ColY(0));
        phRt.anchoredPosition = new Vector2(phX, top + 12f);
        var pc = GlobalClock.IsPlaying ? Comic.Cream : new Color(0.80f, 0.76f, 0.92f);
        if (playhead.color != pc) playhead.color = pc;
    }

    void UpdateFocus(SongManager sm)
    {
        bool want = FocusLoop.Active && FocusLoop.Column >= 0 && FocusLoop.Column < cols.Length;
        if (want != focusShown) { focusShown = want; HudKit.SetActive(focusRt, want); if (want) focusRt.SetAsLastSibling(); focusCol = -2; }
        if (!want || focusCol == FocusLoop.Column) return;
        focusCol = FocusLoop.Column;
        var c = cols[focusCol];
        float w = c.w * scale * 1.2f + 26f, top = (c.top * LaneStep + BeadH * 0.5f) * 1.2f + 12f, bottom = (c.bottom * LaneStep + BeadH * 0.5f) * 1.2f + 12f;
        focusRt.sizeDelta = new Vector2(w, top + bottom);
        focusRt.anchoredPosition = new Vector2(c.x0 + c.w * scale * 0.5f, (top - bottom) * 0.5f + ColY(focusCol));
        focusArt.Repaint();
    }

    void UpdateGhost(SongManager sm)
    {
        var tray = IslandTray.I;
        bool drag = tray != null && tray.Dragging && tray.Ghost != null;
        int key = int.MinValue; Rect r = default(Rect); string kind = "";
        GhostColumn = -1;
        if (drag)
        {
            if (modeFree == -9) { modeFree = ModeOf("Free"); modeInsert = ModeOf("Insert"); modeMerge = ModeOf("Merge"); modeStack = ModeOf("Stack"); modeBlocked = ModeOf("Blocked"); }
            var d = tray.Ghost.Snapshot();
            int m = (int)d.mode;
            float gw = 24f + 18f * Mathf.Clamp(tray.Ghost.Data != null ? tray.Ghost.Data.bars : 1, 1, 4);
            gw *= scale;
            if (d.visible && m != modeBlocked && cols.Length > 0)
            {
                if (m == modeMerge && d.target != null)
                {
                    int ci = Mathf.Clamp(d.target.column, 0, cols.Length - 1);
                    var c = cols[ci];
                    float x = d.east ? c.x0 + c.w * scale + gw * 0.5f : c.x0 - gw * 0.5f;
                    r = new Rect(x - gw * 0.5f, ColY(ci) - BeadH * 0.5f, gw, BeadH); kind = "merge"; GhostColumn = ci + (d.east ? 1 : 0);
                }
                else if (m == modeStack && d.column >= 0 && d.column < cols.Length)
                {
                    var c = cols[d.column];
                    var anchor = sm.AnchorOf(d.column);
                    bool above = anchor == null || d.pz > anchor.pz;
                    float y = above ? (c.top + 1) * LaneStep : -(c.bottom + 1) * LaneStep;
                    r = new Rect(c.x0, ColY(d.column) + y - BeadH * 0.5f, c.w * scale, BeadH); kind = above ? "above" : "below"; GhostColumn = d.column;
                }
                else if (m == modeInsert && d.insertAt >= 0)
                {
                    int ci = Mathf.Clamp(d.insertAt, 0, cols.Length);
                    float x = ci < cols.Length ? cols[ci].x0 - ColGap * 0.5f * scale : cols[cols.Length - 1].x0 + cols[cols.Length - 1].w * scale + ColGap * 0.5f * scale;
                    r = new Rect(x - gw * 0.5f, ColY(Mathf.Min(ci, cols.Length - 1)) - BeadH * 0.5f, gw, BeadH); kind = "insert"; GhostColumn = ci;
                }
                else
                {
                    var c = cols[cols.Length - 1];
                    float x = c.x0 + c.w * scale + ColGap * scale + gw * 0.5f;
                    r = new Rect(x - gw * 0.5f, EndY() - BeadH * 0.5f, gw, BeadH); kind = "end"; GhostColumn = cols.Length;
                }
                key = Mathf.RoundToInt(r.x * 4f) * 7919 + Mathf.RoundToInt(r.y * 4f) * 31 + Mathf.RoundToInt(r.width);
            }
        }
        GhostKind = kind;
        bool on = key != int.MinValue;
        if (on == ghostOn && key == ghostKey) return;
        ghostOn = on; ghostKey = key; ghostRect = r;
        line.Repaint();
    }
}
