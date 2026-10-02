using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SPEC v5 §6 (package R): the draft's MELODY LINE — the shape of the tune at a glance while drafting. A small paper card in the v4 ink language
/// floats beside the length row above the draft island (left of it; right when there is no room; above it as a last resort): each drafted note is
/// an inked dot at x = its start (the notes' lengths laid end to end over the island's span, a light hold bar for its length) and y = its pitch
/// within the island's range (up = higher), the dots joined by a hand-inked line in the instrument colour; the run a click would add (the hovered
/// tile's grid line, each at the brush length) is a dashed ghost extension with hollow dots. It grows as you click (the new dot pops in on twos),
/// shrinks on Backspace and leaves with the draft (finish / Esc). No letters, no staff: a faint beat ruler on the floor of the card is the only
/// scale. The card takes no pointer (clicks go through to the world); its picture is repainted only when the draft, the hover or the brush change.
/// v6 (H): on a KEYBOARD the card is the melody's own drawing — its height spans the tune (and the hovered key) with a little room, at least an
/// octave, instead of the keyboard's two octaves, so a short phrase still reads as a shape; a repeated note is a second dot at the same height.
/// v7 (D, SPEC v7 §14.4): a melody PHRASE draws here too — while a stroke paints on a phrase, or the pointer hovers one with a cube in the hand,
/// the card floats above the phrase's far edge and shows the held group's notes on it: x = each note's start over the phrase's length (its beat
/// ruler = the phrase's beats, bars taller), y = the row's pitch over the roll's range, a hold bar for its length; the note a click would place
/// (the hovered cell at the brush length) is a hollow ghost dot at its own time (no dashed link: it can land before the last note).
/// </summary>
public class MelodyLine : MonoBehaviour
{
    public const float CardW = 256f, CardH = 78f, PadX = 16f, PadTop = 13f, PadBottom = 16f, Gap = 10f;

    public static MelodyLine I;
    /// <summary>The card is on screen (a draft with at least one note, its length row shown).</summary>
    public static bool Shown => I != null && I.shown;
    /// <summary>The drafted notes' dots in the card, normalised (x 0 = left … 1 = right of the drawing area, y 0 = lowest … 1 = highest pitch), in order.</summary>
    public static IReadOnlyList<Vector2> Points => I != null ? (IReadOnlyList<Vector2>)I.pts : Empty;
    /// <summary>The ghost dots of the run a click would add (normalised like <see cref="Points"/>), in order; empty when nothing is hovered.</summary>
    public static IReadOnlyList<Vector2> GhostPoints => I != null ? (IReadOnlyList<Vector2>)I.ghost : Empty;
    /// <summary>The card's rect on screen (px).</summary>
    public static Rect ScreenRect => I != null && I.shown ? I.screenRect : new Rect();
    /// <summary>Repaints so far (tests: the picture is rebuilt on change only).</summary>
    public static int RepaintCount { get; private set; }
    /// <summary>v7: the card shows a melody phrase's notes (not a draft).</summary>
    public static bool PhraseMode => I != null && I.shown && I.phraseMode;
    /// <summary>v7: the phrase the card shows (null when it shows a draft or nothing).</summary>
    public static KeyBlock PhraseIsland => I != null && I.shown && I.phraseMode ? I.phraseKb : null;
    static readonly List<Vector2> Empty = new List<Vector2>();

    public static MelodyLine Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<MelodyLine>();
            if (I == null) I = new GameObject("MelodyLine").AddComponent<MelodyLine>();
            I.Build();
        }
        return I;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { I = null; RepaintCount = 0; }

    RectTransform card; CanvasGroup group; InkShape paper; InkPainter ink;
    bool built, shown;
    readonly List<Vector2> pts = new List<Vector2>(32), ghost = new List<Vector2>(16);
    readonly List<float> holds = new List<float>(32);   // each note's length (normalised x)
    float ghostHold;
    int sig = int.MinValue, lastCount;
    float popT = 9f; Color lineColor = Color.white; int beats = 4, bpb = 4; float spanBeats = 4f;
    Rect screenRect;
    bool phraseMode, ghostLinked = true; KeyBlock phraseKb;   // v7
    static readonly Vector3[] corners = new Vector3[4];
    static readonly List<Vector2> scratch = new List<Vector2>(256);

    void Awake() { if (I == null) I = this; }
    void OnDestroy() { if (I == this) I = null; }

    void Build()
    {
        if (built) return;
        built = true;
        card = InkUI.Node("MelodyLine", InkUI.OverlayRoot);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardW, CardH);
        group = card.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        paper = InkShape.Create(card, "Paper", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(CardW, CardH));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = false; paper.Radius = 16f; paper.SetInk(2f, 3.2f); paper.Wobble = 1f;
        paper.ShadowOffset = new Vector2(4f, -5f); paper.ShadowColor = Comic.PrintShadow; paper.Seed = 8807; paper.RotJitter = 0.8f;
        ink = InkPainter.Create(card, "Contour", new Vector2(CardW, CardH), Paint);
        InkUI.Stretch(ink.rectTransform);
        ink.raycastTarget = false; ink.Seed = 8809;
        card.gameObject.SetActive(false);
    }

    // ================================================================== per frame
    void LateUpdate()
    {
        if (!built) return;
        var pm = PathManager.I;
        var d = pm != null ? pm.Draft : null;
        bool free = !WorldInput.WorldLocked && !Presenter.Active && !MainMenu.IsShown;
        bool ok = d != null && d.nodes.Count > 0 && d.Island != null && free && DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Draft;
        Rect row = ok ? DurationPicker.RowScreenRect : new Rect();
        if (!ok || row.width < 1f)
        {
            // v7: a melody phrase being drawn (or hovered with a cube in the hand) draws its notes here
            var ph = free && pm != null ? PhraseTarget(pm) : null;
            if (ph == null || !MeasurePhrase(pm, ph)) { Hide(); return; }
            // beside the draft row when it floats over this phrase (as for a draft), else above the phrase itself
            Rect prow = DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Phrase && DurationPicker.PhraseIsland == ph ? DurationPicker.RowScreenRect : new Rect();
            if (prow.width >= 1f) Place(prow);
            else if (!PlaceAbove(ph)) { Hide(); return; }
            PopTick();
            return;
        }
        if (!shown || phraseMode) { shown = true; phraseMode = false; phraseKb = null; ghostLinked = true; card.gameObject.SetActive(true); sig = int.MinValue; lastCount = d.nodes.Count; }
        Measure(pm, d);
        Place(row);
        PopTick();
    }

    void PopTick()
    {
        // the newest dot pops in on twos (0.6 -> 1.18 -> 1 over 3 steps)
        if (popT < 0.3f)
        {
            int before = Mathf.FloorToInt(popT * Look.TwosFps);
            popT += Time.unscaledDeltaTime;
            if (Mathf.FloorToInt(popT * Look.TwosFps) != before) ink.Repaint();
        }
    }

    void Hide()
    {
        if (!shown) return;
        shown = false; phraseMode = false; phraseKb = null;
        pts.Clear(); ghost.Clear(); holds.Clear();
        if (card != null) card.gameObject.SetActive(false);
    }

    /// <summary>Recomputes the normalised points from the draft (and the hovered run) and repaints when they changed.</summary>
    void Measure(PathManager pm, AudioCube d)
    {
        var isl = d.Island;
        int lo = int.MaxValue, hi = int.MinValue;
        if (isl.IsKeyboard)
        {
            // v6: the tune's own range (+ the hovered key), 2 semitones of room, at least an octave (centred)
            foreach (var t in d.nodes) if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); }
            foreach (var t in pm.PreviewTiles) if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); }
            if (lo <= hi)
            {
                lo -= 2; hi += 2;
                if (hi - lo < 12) { int pad = 12 - (hi - lo); lo -= pad / 2; hi += pad - pad / 2; }
            }
        }
        else foreach (var t in isl.tiles) if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); }
        if (lo > hi) { lo = 48; hi = 72; }
        bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float tpb = ProjectConfig.TicksPerBeat;
        float total = 0f;
        for (int i = 0; i < d.nodes.Count; i++) total += d.DurBeats(i);
        var ghostTiles = pm.PreviewTiles;
        float brush = pm.BrushTicks / tpb;
        float ghostTotal = ghostTiles.Count * brush;
        float isleBeats = isl.LengthBeats;
        spanBeats = Mathf.Max(isleBeats, Mathf.Ceil((total + ghostTotal) / bpb) * bpb);
        beats = Mathf.RoundToInt(spanBeats);
        int s = 17;
        unchecked
        {
            s = s * 31 + d.nodes.Count; s = s * 31 + ghostTiles.Count; s = s * 31 + pm.BrushTicks; s = s * 31 + lo; s = s * 31 + hi; s = s * 31 + beats;
            for (int i = 0; i < d.nodes.Count; i++) { var t = d.nodes[i]; s = s * 31 + (t != null ? t.midi : -1); s = s * 31 + (i < d.durs.Count ? d.durs[i] : 0); }
            for (int i = 0; i < ghostTiles.Count; i++) { var t = ghostTiles[i]; s = s * 31 + (t != null ? t.midi : -1); }
            s = s * 31 + d.instrument;
        }
        if (s == sig) return;
        sig = s;
        pts.Clear(); holds.Clear(); ghost.Clear();
        float range = Mathf.Max(1, hi - lo), x = 0f;
        for (int i = 0; i < d.nodes.Count; i++)
        {
            var t = d.nodes[i];
            float len = d.DurBeats(i);
            pts.Add(new Vector2(x / spanBeats, t != null ? (t.midi - lo) / range : 0.5f));
            holds.Add(len / spanBeats);
            x += len;
        }
        for (int i = 0; i < ghostTiles.Count; i++)
        {
            var t = ghostTiles[i];
            ghost.Add(new Vector2(x / spanBeats, t != null ? (t.midi - lo) / range : 0.5f));
            x += brush;
        }
        ghostHold = brush / spanBeats;
        lineColor = Comic.Opaque(d.Color);
        if (d.nodes.Count > lastCount) popT = 0f;   // a note was added: the newest dot pops
        lastCount = d.nodes.Count;
        RepaintCount++;
        ink.Repaint();
    }

    // ================================================================== v7 phrase mode
    /// <summary>The phrase to draw: the one a stroke paints on, else the one the pointer hovers (a cell) while a cube is in the hand.</summary>
    static KeyBlock PhraseTarget(PathManager pm)
    {
        if (pm.PhraseStroking && pm.HoverTile != null && pm.HoverTile.island != null && pm.HoverTile.island.IsPhrase) return pm.HoverTile.island;
        if (pm.Hand == PathManager.HandKind.Cube && !pm.IsDrawing && pm.HoverTile != null && pm.HoverTile.island != null && pm.HoverTile.island.IsPhrase) return pm.HoverTile.island;
        return null;
    }

    /// <summary>The held group's notes on <paramref name="kb"/> (the stroke's while one runs) + the hovered note as a ghost; false when there is none.</summary>
    bool MeasurePhrase(PathManager pm, KeyBlock kb)
    {
        int inst = pm.selectedInstrument;
        IReadOnlyList<PhraseNote> notes = pm.PhraseStroking ? pm.StrokeNotes : null;
        List<PhraseNote> own = null;
        if (notes == null) { var c = PathManager.PhraseCubeOf(kb, inst); own = c != null ? PhraseRoll.NotesOf(c) : new List<PhraseNote>(); notes = own; }
        if (notes == null || notes.Count == 0) return false;
        int total = Mathf.Max(1, PhraseRoll.TicksOf(kb)), grid = Mathf.Max(1, kb.phraseGrid);
        int lo = int.MaxValue, hi = int.MinValue;
        for (int z = 0; z < kb.rows; z++) { var t = kb.GetTile(0, z); if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); } }
        if (lo > hi) { lo = 48; hi = 72; }
        var hov = pm.PhraseStroking ? null : pm.HoverTile;
        int gStart = hov != null && hov.island == kb ? hov.gridX * grid : -1, gRow = hov != null ? hov.gridZ : -1, gLen = pm.BrushTicks;
        bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        spanBeats = total / (float)ProjectConfig.TicksPerBeat;
        beats = Mathf.RoundToInt(spanBeats);
        int s = 29;
        unchecked
        {
            s = s * 31 + kb.GetInstanceID(); s = s * 31 + total; s = s * 31 + lo; s = s * 31 + hi; s = s * 31 + gStart; s = s * 31 + gRow; s = s * 31 + gLen; s = s * 31 + inst;
            foreach (var n in notes) { s = s * 31 + n.start; s = s * 31 + n.row; s = s * 31 + n.len; }
        }
        if (!shown || !phraseMode || phraseKb != kb) { shown = true; phraseMode = true; phraseKb = kb; ghostLinked = false; card.gameObject.SetActive(true); sig = int.MinValue; lastCount = notes.Count; }
        if (s == sig) return true;
        sig = s;
        pts.Clear(); holds.Clear(); ghost.Clear();
        float range = Mathf.Max(1, hi - lo);
        foreach (var n in notes)
        {
            var t = kb.GetTile(0, Mathf.Clamp(n.row, 0, kb.rows - 1));
            pts.Add(new Vector2(n.start / (float)total, t != null ? (t.midi - lo) / range : 0.5f));
            holds.Add(Mathf.Min(n.len, total - n.start) / (float)total);
        }
        if (gStart >= 0 && gStart < total)
        {
            var t = kb.GetTile(0, Mathf.Clamp(gRow, 0, kb.rows - 1));
            ghost.Add(new Vector2(gStart / (float)total, t != null ? (t.midi - lo) / range : 0.5f));
            ghostHold = Mathf.Min(gLen, total - gStart) / (float)total;
        }
        var cube = PathManager.PhraseCubeOf(kb, inst);
        lineColor = Comic.Opaque(cube != null ? cube.Color : Instruments.Colors[Mathf.Clamp(inst, 0, Instruments.Count - 1)]);
        if (notes.Count > lastCount) popT = 0f;
        lastCount = notes.Count;
        RepaintCount++;
        ink.Repaint();
        return true;
    }

    /// <summary>Above the phrase's far edge (its projected tiles' top), centred on it; under its near edge when there is no room above; false off screen.</summary>
    bool PlaceAbove(KeyBlock kb)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        var b = kb.VisualBounds;
        float top = float.NegativeInfinity, bot = float.PositiveInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        bool any = false;
        for (int k = 0; k < 8; k++)
        {
            Vector3 p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y + 0.9f : b.min.y + 1.4f, (k & 4) == 0 ? b.min.z : b.max.z);
            Vector3 sp = cam.WorldToScreenPoint(p);
            if (sp.z <= 0f) continue;
            any = true; top = Mathf.Max(top, sp.y); bot = Mathf.Min(bot, sp.y); minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
        }
        if (!any || maxX < 0f || minX > Screen.width || bot > Screen.height || top < 0f) return false;
        var root = InkUI.OverlayRoot;
        float upp = InkUI.UnitsPerPixel, h = CardH / upp, gap = Gap / upp;
        float cx = Mathf.Clamp((minX + maxX) * 0.5f, CardW * 0.5f / upp + 8f, Screen.width - CardW * 0.5f / upp - 8f);
        float y = top + gap + h * 0.5f;
        if (y + h * 0.5f > Screen.height - 90f / upp) y = bot - gap - h * 0.5f;   // under the rail: below the near edge instead
        y = Mathf.Clamp(y, h * 0.5f + 8f, Screen.height - h * 0.5f - 8f);
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(cx, y), null, out lp);
        card.anchoredPosition = lp;
        card.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), c2 = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        screenRect = Rect.MinMaxRect(Mathf.Min(a.x, c2.x), Mathf.Min(a.y, c2.y), Mathf.Max(a.x, c2.x), Mathf.Max(a.y, c2.y));
        return true;
    }

    /// <summary>Left of the length row (bottoms aligned: the card never goes lower than the row, so it never covers the island's tiles), else
    /// right of it, else above it; kept on screen.</summary>
    void Place(Rect row)
    {
        var root = InkUI.OverlayRoot;
        float upp = InkUI.UnitsPerPixel;
        float w = CardW / upp, h = CardH / upp, gap = Gap / upp;
        bool below = DurationPicker.PlacedBelow;
        float y = below ? row.yMax - h * 0.5f : row.yMin + h * 0.5f;   // screen px centre
        float x;
        if (row.xMin - gap - w >= 8f) x = row.xMin - gap - w * 0.5f;
        else if (row.xMax + gap + w <= Screen.width - 8f) x = row.xMax + gap + w * 0.5f;
        else { x = row.center.x; y = below ? row.yMin - gap - h * 0.5f : row.yMax + gap + h * 0.5f; }
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(x, y), null, out lp);
        card.anchoredPosition = lp;
        card.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        screenRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ================================================================== the picture
    Vector2 Map(Rect r, Vector2 n) => new Vector2(Mathf.Lerp(r.xMin, r.xMax, n.x), Mathf.Lerp(r.yMin, r.yMax, n.y));

    /// <summary>A hand-inked polyline through <paramref name="p0"/> … : each segment subdivided with a small seeded wobble across it (stable at rest).</summary>
    void Wobbly(InkPainter g, List<Vector2> into, Vector2 a, Vector2 b, int key)
    {
        int n = Mathf.Clamp(Mathf.CeilToInt((b - a).magnitude / 7f), 1, 16);
        Vector2 dir = b - a, nrm = dir.sqrMagnitude > 1e-6f ? new Vector2(-dir.y, dir.x).normalized : Vector2.up;
        for (int i = into.Count == 0 ? 0 : 1; i <= n; i++)
        {
            float u = i / (float)n;
            float amp = (i == 0 || i == n) ? 0f : 0.8f;
            into.Add(Vector2.Lerp(a, b, u) + nrm * (g.Still(key * 37 + i) * amp));
        }
    }

    void Paint(InkPainter g)
    {
        if (pts.Count == 0) return;
        Rect r = g.Area;
        Rect box = Rect.MinMaxRect(r.xMin + PadX, r.yMin + PadBottom, r.xMax - PadX, r.yMax - PadTop);
        Color inkC = InkUI.Ink, faint = Palette.A(InkUI.Ink, 0.22f);
        // the beat ruler on the card's floor: a tick per beat, taller on the bars
        float floorY = r.yMin + PadBottom * 0.45f;
        g.Line(new Vector2(box.xMin, floorY), new Vector2(box.xMax, floorY), 1.2f, faint);
        for (int k = 0; k <= beats; k++)
        {
            float x = Mathf.Lerp(box.xMin, box.xMax, k / Mathf.Max(1f, spanBeats));
            float th = k % bpb == 0 ? 5f : 2.5f;
            g.Line(new Vector2(x, floorY), new Vector2(x, floorY + th), 1.2f, faint);
        }
        // the ghost run: a dashed extension from the last note, hollow dots, faint hold bars (v7 phrases: the ghost stands alone at its own time)
        Vector2 lastP = Map(box, pts[pts.Count - 1]);
        if (ghost.Count > 0)
        {
            scratch.Clear();
            scratch.Add(lastP);
            for (int i = 0; i < ghost.Count; i++) scratch.Add(Map(box, ghost[i]));
            if (ghostLinked) g.Stroke(scratch, scratch.Count, 2.4f, Palette.A(inkC, 0.6f), false, 6f, 4.5f);
            for (int i = 0; i < ghost.Count; i++)
            {
                Vector2 c = Map(box, ghost[i]);
                g.Line(c, c + new Vector2(ghostHold * box.width, 0f), 3f, Palette.A(lineColor, 0.35f));
                g.Disc(c, 5.4f, 5.4f, InkUI.Paper, 16);
                g.Arc(c, 5.4f, 1.8f, Palette.A(inkC, 0.75f));
            }
        }
        // hold bars (each note's length), the line, the dots
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 c = Map(box, pts[i]);
            g.Line(c, c + new Vector2(holds[i] * box.width, 0f), 4f, Palette.A(lineColor, 0.5f));
        }
        if (pts.Count >= 2)
        {
            scratch.Clear();
            for (int i = 0; i + 1 < pts.Count; i++) Wobbly(g, scratch, Map(box, pts[i]), Map(box, pts[i + 1]), i);
            g.InkStroke(scratch, scratch.Count, 4f, lineColor, 1.6f, inkC);
        }
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 c = Map(box, pts[i]);
            float rad = 6.4f;
            if (i == pts.Count - 1 && popT < 0.3f)
            {
                int step = Mathf.FloorToInt(popT * Look.TwosFps);
                rad *= step <= 0 ? 0.6f : (step == 1 ? 1.18f : 1f);
            }
            g.Disc(c, rad + 1.6f, rad + 1.6f, inkC, 16);
            g.Disc(c, rad, rad, i == pts.Count - 1 ? Color.Lerp(lineColor, Color.white, 0.3f) : lineColor, 16);
        }
    }
}
