using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SPEC v4 §2.5 / §6, ui_research §4.6: the inspected cube's timeline. One block per note the cube plays in its window (AudioCube.TimelineEvents,
/// both engines): x = start, width = length, filled with the note's tile colour (its cell's colour on the grid), a mini cube inside sized like
/// the length row (30 · SizeOf^1.5; dotted lengths carry the orbiting satellite), rests hollow and dotted, accents taller, ghosts lighter and
/// smaller, ties bridged, ratchets as two / three cubes; the loop repeats of a short path faded; a ruler of beat and bar ticks (a tiny cube
/// mark per bar, no numerals); a cream playhead line and a mini cube hopping from block top to block top, landing on each note when it
/// sounds; the playing block bounces. The view zooms to the path: one pass fills most of the width (a short path shows a faded repeat or two,
/// at most <see cref="MaxPxPerBeat"/>); a path longer than its window shows the whole window.
/// Editing (each gesture = exactly one History.Push): click a block = select that note (wavy underline, its bead highlights in the grid and the
/// world, the length row appears under the strip, the note plays); drag a block's right edge = stretch / shrink through the ten lengths (the
/// cube grows / shrinks live, each snap auditions; one push on release); right-click = rest on / off; the tear-off ✕ on the selected block (or
/// Backspace / Delete through CubeInspector) = delete the note (at least one stays). The first edit of a legacy cube calls BakeDurations.
/// </summary>
public class RhythmStrip : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const float Width = 512f, Height = 110f, RulerH = 18f, BlockH = 56f, BlockBottom = -Height * 0.5f + 20f, LeftPad = 10f, RightPad = 10f;
    public const float MaxPxPerBeat = 150f, EdgePx = 9f, PlayCubePx = 16f, TearPx = 26f;
    /// <summary>The ten lengths a note can take (ticks; 24 = one beat), shortest first: 16th, dotted 16th, 8th, … whole, dotted whole.</summary>
    public static readonly int[] Lengths = { 6, 9, 12, 18, 24, 36, 48, 72, 96, 144 };
    /// <summary>Onboarding event of a committed stretch.</summary>
    public const string EvStretched = "note.stretched";

    public event Action<int> OnNoteSelected;
    public AudioCube Cube { get; private set; }
    /// <summary>The selected note's node index (-1 none).</summary>
    public int SelectedNote { get; private set; } = -1;
    /// <summary>The selected block (index into <see cref="Events"/>; -1 none).</summary>
    public int SelectedEvent => selEv;
    public IReadOnlyList<AudioCube.TimelineEvent> Events => events;
    /// <summary>Blocks drawn (events inside the visible range).</summary>
    public int BlockCount => nBlocks;
    public float PxPerBeat => ppb;
    public float VisibleBeats => visBeats;
    public float CycleBeats => cycleBeats;
    /// <summary>Events of one pass through the path (the rest are faded repeats).</summary>
    public int Period => period;
    public int Window => window;
    /// <summary>Committed strip edits (tests).</summary>
    public int EditCount { get; private set; }
    /// <summary>Block bounces raised by the cube's landings (tests).</summary>
    public int BounceCount { get; private set; }
    /// <summary>BakeDurations calls made by strip / length-row edits (tests: the first edit of a legacy cube bakes).</summary>
    public static int BakeCalls { get; private set; }
    /// <summary>The strip of the open inspector (the length row applies its picks through it).</summary>
    public static RhythmStrip Active { get; private set; }

    readonly List<AudioCube.TimelineEvent> events = new List<AudioCube.TimelineEvent>(64);
    class Block
    {
        public RectTransform rt; public InkShape shape; public readonly CubeGlyph[] cubes = new CubeGlyph[3]; public InkShape sat;
        public float x0, x1, h, cubePx; public float bounceT = 9f; public bool dotted, faded;
    }
    readonly List<Block> blocks = new List<Block>(32);
    RectTransform rt, blocksRoot;
    InkPath shade, ruler, bridges, handle, tearX;
    InkShape tear; RectTransform tearRt;
    InkWave underline;
    Image playLine; CubeGlyph playCube;
    int nBlocks, window = -1, period = 1, sig = int.MinValue, selEv = -1, hoverEv = -1, hoverEdge = -1;
    float winLen = 4f, cycleBeats = 4f, visBeats = 4f, ppb = 40f;
    int lo, hi; bool moonIsl;
    // gesture
    enum G { None, Select, Stretch, Rest, Tear, Empty }
    G gesture; int pressEv = -1, stretchNode = -1, startTicks, lastTicks; float stretchStart; bool stretchChanged, stretchCapped, cursorHeld;
    AudioCube.CubePose pose; bool poseOk;

    float X0 => -Width * 0.5f + LeftPad;
    float XVisEnd => X0 + visBeats * ppb;

    public static RhythmStrip Create(Transform parent)
    {
        var go = new GameObject("RhythmStrip", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<RhythmStrip>();
        s.Build();
        return s;
    }

    void Build()
    {
        rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Width, Height);
        InkUI.HitArea(gameObject);
        shade = Layer("Shade"); ruler = Layer("Ruler"); bridges = Layer("Bridges");
        ruler.Wobble = 0.35f; bridges.Wobble = 0.5f; shade.Wobble = 0f;
        blocksRoot = InkUI.Node("Blocks", transform);
        InkUI.Stretch(blocksRoot);
        handle = InkPath.Create(transform, "Handle", new Vector2(14f, 30f));
        handle.UnitPx = 1f; handle.Wobble = 0.3f;
        handle.Line(InkUI.Ink, 2.2f, new Vector2(-2.5f, -9f), new Vector2(-2.5f, 9f)).Line(InkUI.Ink, 2.2f, new Vector2(2.5f, -9f), new Vector2(2.5f, 9f));
        handle.gameObject.SetActive(false);
        underline = InkWave.Create(transform, "Underline", InkUI.Ink, new Vector2(40f, 8f));
        underline.Thickness = 2.4f;
        underline.gameObject.SetActive(false);
        playLine = UIKit.Image(transform, "Playhead", "white", Palette.A(InkUI.Ink, 0.6f), new Vector2(4f, Height - RulerH - 8f));   // ink edges ...
        playLine.raycastTarget = false; playLine.preserveAspect = false;
        var cream = UIKit.Image(playLine.transform, "Cream", "white", Palette.A(InkUI.Paper, 0.95f), new Vector2(2f, Height - RulerH - 8f));   // ... a 2 px cream line
        cream.raycastTarget = false; cream.preserveAspect = false;
        InkUI.Centre(cream.rectTransform, Vector2.zero, new Vector2(2f, Height - RulerH - 8f));
        playCube = CubeGlyph.Create(transform, "PlayCube", InkUI.Paper, PlayCubePx);
        playCube.raycastTarget = false;
        tearRt = InkUI.Node("Tear", transform);
        tearRt.sizeDelta = new Vector2(TearPx, TearPx);
        tear = InkShape.Create(tearRt, "Sticker", InkShape.Kind.Sticker, InkUI.Red, new Vector2(TearPx - 4f, TearPx - 4f));
        tear.raycastTarget = false; tear.RotJitter = 6f; tear.SetInk(1.6f, 2.4f); tear.ShadowOffset = new Vector2(2f, -2f); tear.Seed = 2207;
        tearX = InkPath.Create(tearRt, "X", new Vector2(12f, 12f));
        InkDraw.Cross(tearX, InkUI.Paper, 2.6f);
        tearRt.gameObject.SetActive(false);
    }

    InkPath Layer(string name)
    {
        var p = InkPath.Create(transform, name, new Vector2(Width, Height));
        InkUI.Stretch(p.rectTransform);
        p.UnitPx = 1f;
        return p;
    }

    public void Bind(AudioCube c)
    {
        if (c == Cube && c != null) return;
        EndGesture();
        Cube = c; SelectedNote = -1; selEv = -1; sig = int.MinValue; hoverEv = -1; hoverEdge = -1;
        if (c == null) { HideAll(); return; }
        Rebuild();
    }

    void OnEnable() { AudioCube.OnLanded += HandleLanded; Active = this; }
    void OnDisable()
    {
        AudioCube.OnLanded -= HandleLanded;
        if (gesture == G.Stretch && stretchChanged) CommitStretch();   // cut short by the card closing: what it changed is committed
        EndGesture();
        if (Active == this) Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { BakeCalls = 0; Active = null; }

    // ================================================================== building the view
    int PickWindow(AudioCube c)
    {
        if (poseOk && pose.active && pose.window >= 0) return pose.window;
        if (window >= 0 && window < c.windows.Count) return window;
        return c.windows.Count > 0 ? 0 : -1;
    }

    int Sig(AudioCube c)
    {
        unchecked
        {
            int h = c.GetInstanceID();
            int n = c.nodes.Count;
            h = h * 31 + n;
            for (int i = 0; i < n; i++)
            {
                var t = c.nodes[i];
                h = h * 31 + (t != null ? t.GetInstanceID() : 0);
                h = h * 31 + c.ModOf(i);
                h = h * 31 + (i < c.durs.Count ? c.durs[i] : -1);
            }
            h = h * 31 + (int)c.step; h = h * 31 + c.hits; h = h * 31 + c.rot; h = h * 31 + c.mask; h = h * 31 + (int)c.mode;
            h = h * 31 + (c.reverse ? 1 : 0); h = h * 31 + c.phase; h = h * 31 + c.instrument;
            h = h * 31 + (c.muted ? 1 : 0) + (Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)] ? 2 : 0);
            int w = PickWindow(c);
            h = h * 31 + w; h = h * 31 + c.windows.Count;
            if (w >= 0 && w < c.windows.Count) h = h * 31 + c.windows[w].length.GetHashCode();
            h = h * 31 + GlobalClock.Swing.GetHashCode(); h = h * 31 + GlobalClock.BeatsPerBar;
            var isl = c.Island; h = h * 31 + (isl != null ? isl.GetInstanceID() : 0);
            return h;
        }
    }

    void Rebuild()
    {
        var c = Cube;
        events.Clear(); nBlocks = 0;
        if (c == null || c.nodes.Count == 0) { HideAll(); return; }
        sig = Sig(c);
        window = PickWindow(c);
        winLen = window >= 0 && window < c.windows.Count ? c.windows[window].length : Mathf.Max(1f, c.measureLength);
        c.TimelineEvents(window, events);
        int n = c.nodes.Count;
        period = c.mode == PathMode.PingPong ? Mathf.Max(1, 2 * n - 2) : n;
        float lastEnd = 0f;
        for (int i = 0; i < events.Count && i < period; i++) lastEnd = Mathf.Max(lastEnd, events[i].start + events[i].len);
        cycleBeats = events.Count > period ? events[period].start : Mathf.Min(winLen, Mathf.Max(0.25f, lastEnd));
        // zoom: one pass fills most of the width (≤ MaxPxPerBeat), the whole window when the pass nearly fills it
        float usable = Width - LeftPad - RightPad;
        float wantBeats = cycleBeats >= winLen * 0.8f ? winLen : Mathf.Min(winLen, cycleBeats * 1.3f);
        ppb = Mathf.Min(MaxPxPerBeat, usable / Mathf.Max(0.25f, wantBeats));
        visBeats = usable / ppb;
        if (visBeats > winLen) { visBeats = winLen; ppb = usable / winLen; }
        PitchRange(c.Island);
        DrawRuler();
        // blocks
        Color cubeCol = Comic.Opaque(c.Color);
        bool muted = c.muted || Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)];
        if (muted) cubeCol = Color.Lerp(cubeCol, new Color(0.62f, 0.6f, 0.68f), 0.7f);
        float xEnd = XVisEnd;
        bridges.Clear();
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            float bx0 = X0 + e.start * ppb + 1f, bx1 = X0 + (e.start + e.len) * ppb - 1f;
            if (bx0 >= xEnd - 2f) break;
            bx1 = Mathf.Min(bx1, xEnd);
            var b = BlockAt(nBlocks++);
            bool faded = i >= period;
            bool rest = e.rest;
            float hgt = BlockH * (e.mod == 3 ? 1.15f : (e.mod == 2 ? 0.8f : 1f));
            b.x0 = bx0; b.x1 = Mathf.Max(bx0 + 3f, bx1); b.h = hgt; b.faded = faded;
            float w = b.x1 - b.x0;
            b.rt.gameObject.SetActive(true);
            b.rt.anchoredPosition = new Vector2((b.x0 + b.x1) * 0.5f, BlockBottom);
            b.rt.sizeDelta = new Vector2(w, hgt);
            b.rt.localScale = Vector3.one; b.bounceT = 9f;
            var tile = e.node >= 0 && e.node < c.nodes.Count ? c.nodes[e.node] : null;
            Color fill = tile != null ? PathGridView.TileColor(tile, lo, hi, moonIsl) : new Color(0.8f, 0.78f, 0.9f);
            if (e.mod == 2) fill = Color.Lerp(fill, Color.white, 0.45f);
            float a = faded ? 0.38f : 1f;
            b.shape.Shape = rest ? InkShape.Kind.Ghost : InkShape.Kind.RoundRect;
            b.shape.color = rest ? Palette.A(fill, 0.22f * a) : Palette.A(fill, a);
            b.shape.Ink = Palette.A(InkUI.Ink, faded ? 0.4f : 1f);
            b.shape.HasShadow = !rest && !faded;
            // the mini cube(s): the length row's size, clamped into the block
            float px = DurationPicker.SizePx(e.len);
            px = Mathf.Clamp(px, 7f, Mathf.Min(w - 4f, hgt - 8f));
            if (e.mod == 2) px *= 0.8f;
            int count = e.mod == 4 ? 2 : (e.mod == 5 ? 3 : 1);
            float rpx = count > 1 ? Mathf.Min(px * 0.7f, (w - 6f) / count) : px;
            b.cubePx = rpx;
            for (int k = 0; k < 3; k++)
            {
                var g = b.cubes[k];
                bool on = k < count && rpx >= 5f;
                if (g.gameObject.activeSelf != on) g.gameObject.SetActive(on);
                if (!on) continue;
                float off = count > 1 ? (k - (count - 1) * 0.5f) * (rpx + 1f) : 0f;
                var grt = (RectTransform)g.transform;
                grt.anchoredPosition = new Vector2(off, 0f);
                grt.sizeDelta = new Vector2(rpx, rpx);
                g.color = Palette.A(cubeCol, faded ? 0.45f : 1f);
                g.Hollow = rest;
                g.Ink = Palette.A(InkUI.Ink, faded ? 0.45f : 1f);
            }
            int ticks = c.HasDurations && e.node >= 0 && e.node < c.durs.Count ? c.durs[e.node] : Mathf.RoundToInt(e.len * ProjectConfig.TicksPerBeat);
            int sz; bool dotted;
            b.dotted = AudioCube.TryDecode(ticks, out sz, out dotted) && dotted && !rest && count == 1;
            b.sat.gameObject.SetActive(b.dotted);
            if (b.dotted) b.sat.color = Color.Lerp(cubeCol, Color.white, 0.25f);
            // a tie: an ink bridge to the next block
            if (e.mod == 7 && i + 1 < events.Count)
            {
                float nx = X0 + events[i + 1].start * ppb + 1f;
                if (nx < xEnd)
                {
                    float y = BlockBottom + hgt * 0.5f;
                    bridges.Line(Palette.A(InkUI.Ink, faded ? 0.4f : 1f), 6f, new Vector2(b.x1 - 3f, y), new Vector2(nx + 3f, y));
                }
            }
        }
        for (int i = nBlocks; i < blocks.Count; i++) if (blocks[i].rt.gameObject.activeSelf) blocks[i].rt.gameObject.SetActive(false);
        // the selection survives a rebuild (by node, in the first pass)
        if (SelectedNote >= 0)
        {
            int found = -1;
            for (int i = 0; i < nBlocks && i < period; i++) if (events[i].node == SelectedNote) { found = i; break; }
            if (found < 0 && SelectedNote >= c.nodes.Count) { SetSelection(-1, true); }
            else selEv = found;
        }
        PlaceSelection();
    }

    void PitchRange(KeyBlock isl)
    {
        lo = int.MaxValue; hi = int.MinValue; moonIsl = isl != null && isl.IsMoon;
        if (isl == null) { lo = hi = 60; return; }
        foreach (var t in isl.tiles) if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); }
        if (lo > hi) lo = hi = 60;
    }

    void DrawRuler()
    {
        shade.Clear(); ruler.Clear();
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float top = Height * 0.5f, yRuler = top - RulerH;
        float x0 = X0, xEnd = XVisEnd;
        // faint shading on alternate bars
        for (int bar = 1; bar * bpb < visBeats + 1e-3f; bar += 2)
        {
            float a = x0 + bar * bpb * ppb, b = Mathf.Min(xEnd, x0 + (bar + 1) * bpb * ppb);
            if (b <= a) continue;
            shade.Begin().P(a, BlockBottom - 6f).P(b, BlockBottom - 6f).P(b, top - 2f).P(a, top - 2f).EndFill(Palette.A(InkUI.Ink, 0.055f));
        }
        // the ruler line, beat ticks, bar ticks with a tiny cube mark
        ruler.Line(Palette.A(InkUI.Ink, 0.7f), 1.8f, new Vector2(x0, yRuler), new Vector2(xEnd, yRuler));
        float step = ppb < 14f ? bpb : 1f;   // very long windows: bar ticks only
        for (float bt = 0f; bt <= visBeats + 1e-3f; bt += step)
        {
            float x = x0 + bt * ppb;
            bool bar = Mathf.Abs(bt / bpb - Mathf.Round(bt / bpb)) < 1e-3f;
            if (bar)
            {
                ruler.Line(InkUI.Ink, 2.2f, new Vector2(x, yRuler - 2f), new Vector2(x, yRuler + 11f));
                ruler.Begin().P(x, yRuler + 16f).P(x + 3.2f, yRuler + 13.8f).P(x, yRuler + 11.6f).P(x - 3.2f, yRuler + 13.8f).EndPoly(InkUI.Paper, InkUI.Ink, 1.4f);
            }
            else ruler.Line(Palette.A(InkUI.Ink, 0.6f), 1.6f, new Vector2(x, yRuler), new Vector2(x, yRuler + 6f));
        }
        // the window's end (the island ends here) when it is in view
        if (winLen <= visBeats + 1e-3f)
        {
            float x = x0 + winLen * ppb;
            ruler.Line(Palette.A(InkUI.Ink, 0.5f), 2f, new Vector2(x, BlockBottom - 4f), new Vector2(x, yRuler));
        }
    }

    Block BlockAt(int i)
    {
        while (blocks.Count <= i)
        {
            var b = new Block();
            b.rt = InkUI.Node("Block" + blocks.Count, blocksRoot);
            b.rt.anchorMin = b.rt.anchorMax = new Vector2(0.5f, 0.5f);
            b.rt.pivot = new Vector2(0.5f, 0f);
            b.shape = InkShape.Create(b.rt, "Shape", InkShape.Kind.RoundRect, Color.white, new Vector2(20f, 20f));
            InkUI.Stretch(b.shape.rectTransform);
            b.shape.raycastTarget = false; b.shape.Radius = 7f; b.shape.Wobble = 0.8f; b.shape.SetInk(1.5f, 2.4f);
            b.shape.ShadowOffset = new Vector2(2f, -2.5f); b.shape.ShadowColor = Palette.A(InkUI.Ink, 0.35f); b.shape.RotJitter = 0.6f; b.shape.SetDash(4f, 3f);
            var mid = InkUI.Node("Cubes", b.rt);
            mid.anchorMin = mid.anchorMax = new Vector2(0.5f, 0.5f); mid.anchoredPosition = Vector2.zero; mid.sizeDelta = Vector2.zero;
            for (int k = 0; k < 3; k++) { var g = CubeGlyph.Create(mid, "Cube" + k, Color.white, 20f); g.raycastTarget = false; b.cubes[k] = g; }
            b.sat = InkShape.Create(mid, "Satellite", InkShape.Kind.Circle, Color.white, new Vector2(7f, 7f));
            b.sat.raycastTarget = false; b.sat.HasShadow = false; b.sat.SetInk(1.2f, 1.6f); b.sat.Wobble = 0.2f;
            b.sat.gameObject.SetActive(false);
            blocks.Add(b);
        }
        return blocks[i];
    }

    void HideAll()
    {
        nBlocks = 0; events.Clear();
        foreach (var b in blocks) if (b.rt.gameObject.activeSelf) b.rt.gameObject.SetActive(false);
        if (shade != null) { shade.Clear(); ruler.Clear(); bridges.Clear(); }
        if (underline != null) underline.gameObject.SetActive(false);
        if (tearRt != null) tearRt.gameObject.SetActive(false);
        if (handle != null) handle.gameObject.SetActive(false);
    }

    // ================================================================== per frame
    void Update()
    {
        var c = Cube;
        if (c == null) return;
        if (c.nodes.Count == 0) { if (nBlocks > 0) HideAll(); return; }
        poseOk = c.TryGetPose(out pose);
        if (Sig(c) != sig) Rebuild();
        float dt = Time.unscaledDeltaTime;
        UpdateHover();
        UpdatePlayhead();
        AnimateBlocks(dt);
    }

    void UpdateHover()
    {
        Vector2 lp;
        bool over = gesture == G.None && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null) && Local(Input.mousePosition, out lp);
        int edge = -1, blk = -1;
        if (over) { Local(Input.mousePosition, out lp); edge = EdgeAt(lp); blk = edge >= 0 ? edge : BlockUnder(lp); }
        hoverEv = blk; hoverEdge = edge;
        int h = gesture == G.Stretch ? pressEv : (edge >= 0 ? edge : (selEv >= 0 && selEv < nBlocks ? selEv : -1));
        bool showHandle = h >= 0 && h < nBlocks && !events[h].rest;
        if (handle.gameObject.activeSelf != showHandle) handle.gameObject.SetActive(showHandle);
        if (showHandle)
        {
            var b = blocks[h];
            handle.rectTransform.anchoredPosition = new Vector2(b.x1, BlockBottom + b.h * 0.5f);
            handle.Boil = gesture == G.Stretch || edge >= 0;
        }
        bool wantCursor = gesture == G.Stretch || edge >= 0;
        if (wantCursor != cursorHeld) { cursorHeld = wantCursor; if (wantCursor) CursorKit.Want(CursorKit.Kind.Grab, this); else CursorKit.Release(this); }
    }

    /// <summary>The playhead: a cream line at the current beat (a short path shows the equivalent place in its first pass) and a mini cube
    /// that sits on the playing note's block and hops to the next one over the note's last part (as the world cube does).</summary>
    void UpdatePlayhead()
    {
        var c = Cube;
        float local = -1f;
        if (poseOk && pose.active && pose.window == window && window >= 0 && window < c.windows.Count) local = GlobalClock.SongBeat - c.windows[window].start;
        bool playing = local >= 0f && nBlocks > 0;
        float shown = playing ? local : 0f;
        if (playing && shown > visBeats - 1e-3f && cycleBeats > 0.01f) shown = Mathf.Repeat(local, cycleBeats);
        float x = X0 + shown * ppb;
        playLine.rectTransform.anchoredPosition = new Vector2(x, BlockBottom + (Height - RulerH - 8f) * 0.5f - 6f);
        if (playLine.gameObject.activeSelf != playing) playLine.gameObject.SetActive(playing);
        if (nBlocks == 0) { if (playCube.gameObject.activeSelf) playCube.gameObject.SetActive(false); return; }
        if (!playCube.gameObject.activeSelf) playCube.gameObject.SetActive(true);
        int cur = 0;
        for (int i = 0; i < nBlocks; i++) if (events[i].start <= shown + 1e-4f) cur = i; else break;
        var e = events[cur];
        var a = blocks[cur];
        Vector2 pa = new Vector2((a.x0 + a.x1) * 0.5f, BlockBottom + a.h + PlayCubePx * 0.5f - 1f);
        Vector2 pos = pa;
        if (playing && cur + 1 < nBlocks)
        {
            float into = shown - e.start;
            float hopAt = Mathf.Max(ProjectConfig.snapThreshold * e.len, e.len - ProjectConfig.HopMaxBeats);
            if (into > hopAt)
            {
                var b = blocks[cur + 1];
                Vector2 pb = new Vector2((b.x0 + b.x1) * 0.5f, BlockBottom + b.h + PlayCubePx * 0.5f - 1f);
                float u = Mathf.Clamp01((into - hopAt) / Mathf.Max(1e-3f, e.len - hopAt));
                pos = Vector2.Lerp(pa, pb, u) + new Vector2(0f, Mathf.Sin(u * Mathf.PI) * 14f);
            }
        }
        ((RectTransform)playCube.transform).anchoredPosition = pos;
    }

    void HandleLanded(AudioCube c, int w, int k, AudioCube.Hit hit)
    {
        if (c == null || c != Cube || w != window || !hit.hit) return;
        for (int i = 0; i < nBlocks; i++)
            if (events[i].k == k) { blocks[i].bounceT = 0f; BounceCount++; break; }
    }

    void AnimateBlocks(float dt)
    {
        int step = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps);
        for (int i = 0; i < nBlocks; i++)
        {
            var b = blocks[i];
            if (b.bounceT < 1f)
            {
                b.bounceT += dt / 0.28f;
                float u = Look.Stepped(Mathf.Clamp01(b.bounceT) * 0.28f) / 0.28f;   // on twos
                float sy = u < 0.3f ? 1.14f : (u < 0.62f ? 0.95f : 1f), sx = u < 0.3f ? 0.95f : (u < 0.62f ? 1.03f : 1f);
                if (b.bounceT >= 1f) { sx = 1f; sy = 1f; }
                b.rt.localScale = new Vector3(sx, sy, 1f);
            }
            if (b.dotted)
            {
                float a = step / Look.TwosFps * Mathf.PI * 1.2f;
                float r = b.cubePx * 0.5f;
                b.sat.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * r * 1.35f, Mathf.Sin(a) * r * 0.55f + r * 0.3f);
            }
        }
    }

    // ================================================================== hit tests
    bool Local(Vector2 screen, out Vector2 lp) => RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out lp);

    /// <summary>The block under a strip-local point (-1 none).</summary>
    public int BlockUnder(Vector2 lp)
    {
        for (int i = 0; i < nBlocks; i++)
        {
            var b = blocks[i];
            if (lp.x >= b.x0 - 1f && lp.x <= b.x1 + 1f && lp.y >= BlockBottom - 4f && lp.y <= BlockBottom + b.h + 4f) return i;
        }
        return -1;
    }

    /// <summary>The block whose right edge is under a strip-local point (-1 none); rests have no edge.</summary>
    public int EdgeAt(Vector2 lp)
    {
        if (lp.y < BlockBottom - 4f || lp.y > BlockBottom + BlockH * 1.15f + 4f) return -1;
        int best = -1; float bd = EdgePx;
        for (int i = 0; i < nBlocks; i++)
        {
            var b = blocks[i];
            if (events[i].rest || lp.y > BlockBottom + b.h + 4f) continue;
            float d = Mathf.Abs(lp.x - b.x1);
            if (d <= bd && (b.x1 - b.x0) > 6f) { bd = d; best = i; }
        }
        return best;
    }

    bool TearHit(Vector2 lp) => tearRt.gameObject.activeSelf && (lp - tearRt.anchoredPosition).magnitude <= TearPx * 0.62f;

    /// <summary>Strip-local centre of block <paramref name="i"/> (tests).</summary>
    public Vector2 BlockCentre(int i) => i >= 0 && i < nBlocks ? new Vector2((blocks[i].x0 + blocks[i].x1) * 0.5f, BlockBottom + blocks[i].h * 0.5f) : Vector2.zero;
    /// <summary>Strip-local right edge (middle) of block <paramref name="i"/> (tests).</summary>
    public Vector2 BlockEdge(int i) => i >= 0 && i < nBlocks ? new Vector2(blocks[i].x1, BlockBottom + blocks[i].h * 0.5f) : Vector2.zero;
    public float BlockWidth(int i) => i >= 0 && i < nBlocks ? blocks[i].x1 - blocks[i].x0 : 0f;
    public float BlockCubePx(int i) => i >= 0 && i < nBlocks ? blocks[i].cubePx : 0f;
    public bool BlockFaded(int i) => i >= 0 && i < nBlocks && blocks[i].faded;
    public bool BlockHollow(int i) => i >= 0 && i < nBlocks && blocks[i].shape.Shape == InkShape.Kind.Ghost;
    public bool BlockDotted(int i) => i >= 0 && i < nBlocks && blocks[i].dotted;
    /// <summary>Strip-local x of beat <paramref name="beat"/> (tests).</summary>
    public float XOfBeat(float beat) => X0 + beat * ppb;
    public Vector2 PlayCubePos => playCube != null ? ((RectTransform)playCube.transform).anchoredPosition : Vector2.zero;
    public float PlayLineX => playLine != null ? playLine.rectTransform.anchoredPosition.x : 0f;
    public bool PlayLineShown => playLine != null && playLine.gameObject.activeSelf;
    public Vector2 TearPos => tearRt != null ? tearRt.anchoredPosition : Vector2.zero;
    public bool TearShown => tearRt != null && tearRt.gameObject.activeSelf;

    // ================================================================== gestures
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Middle) return;
        Vector2 lp; if (!Local(e.position, out lp)) return;
        Down(lp, e.button == PointerEventData.InputButton.Right ? 1 : 0);
    }
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { Vector2 lp; if (Local(e.position, out lp)) Drag(lp); }
    public void OnEndDrag(PointerEventData e) { }
    public void OnPointerUp(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Middle) return;
        Vector2 lp; Local(e.position, out lp);
        Up(lp, e.button == PointerEventData.InputButton.Right ? 1 : 0);
    }

    void Down(Vector2 lp, int button)
    {
        gesture = G.None; pressEv = -1;
        if (Cube == null || nBlocks == 0) return;
        if (button == 1) { int bl = BlockUnder(lp); if (bl >= 0) { gesture = G.Rest; pressEv = bl; } return; }
        if (selEv >= 0 && TearHit(lp)) { gesture = G.Tear; return; }
        int edge = EdgeAt(lp);
        if (edge >= 0)
        {
            gesture = G.Stretch; pressEv = edge;
            var e = events[edge];
            stretchNode = e.node; stretchStart = e.start; stretchChanged = false; stretchCapped = false;
            startTicks = TicksOf(edge); lastTicks = startTicks;
            return;
        }
        int b = BlockUnder(lp);
        if (b >= 0) { gesture = G.Select; pressEv = b; return; }
        gesture = G.Empty;
    }

    void Drag(Vector2 lp)
    {
        if (gesture != G.Stretch || Cube == null || stretchNode < 0 || stretchNode >= Cube.nodes.Count) return;
        float endBeat = (lp.x - X0) / Mathf.Max(1e-3f, ppb);
        int ticks = Snap((endBeat - stretchStart) * ProjectConfig.TicksPerBeat);
        if (ticks > startTicks)   // v7 §19.1: a stretch stops at the grid's capacity (the longest length that fits; one refusal per gesture)
        {
            int max = MaxTicksFor(Cube, stretchNode);
            if (ticks > max)
            {
                int fit = startTicks;
                foreach (int L in Lengths) if (L > fit && L <= max) fit = L;
                ticks = fit;
                if (!stretchCapped) { stretchCapped = true; CapacityRefused(Cube); }
            }
        }
        if (ticks == lastTicks) return;
        lastTicks = ticks;
        EnsureDurations(Cube);
        Cube.SetDuration(stretchNode, ticks);
        stretchChanged = ticks != startTicks;
        InkUI.AuditionNote(Cube, stretchNode, ticks / (float)ProjectConfig.TicksPerBeat);
        if (pressEv >= 0 && pressEv < nBlocks) blocks[pressEv].bounceT = 0f;
    }

    void Up(Vector2 lp, int button)
    {
        var g = gesture; gesture = G.None;
        if (Cube == null) return;
        switch (g)
        {
            case G.Rest: if (button == 1 && BlockUnder(lp) == pressEv && pressEv >= 0 && pressEv < nBlocks) ToggleRestAt(events[pressEv].node); break;
            case G.Tear: if (TearHit(lp)) DeleteSelected(); break;
            case G.Select: if (BlockUnder(lp) == pressEv) SelectEvent(pressEv, true); break;
            case G.Stretch:
                if (stretchChanged) CommitStretch();
                else if (EdgeAt(lp) == pressEv || BlockUnder(lp) == pressEv) SelectEvent(pressEv, true);
                break;
            case G.Empty: Deselect(); break;
        }
        pressEv = -1;
    }

    void EndGesture()
    {
        gesture = G.None; pressEv = -1; stretchNode = -1; stretchChanged = false;
        if (cursorHeld) { cursorHeld = false; CursorKit.Release(this); }
    }

    void CommitStretch()
    {
        int node = stretchNode;
        stretchChanged = false;
        History.Push();
        EditCount++;
        CubeInspector.NoteRhythmEdited();
        Onboarding.Notify(EvStretched);
        if (node >= 0) { SelectedNote = node; RaiseSelected(node); }
        sig = int.MinValue;
    }

    /// <summary>Ticks the note of block <paramref name="i"/> lasts (the durations engine's own value; a legacy note's gap to the next hit).</summary>
    public int TicksOf(int i)
    {
        if (i < 0 || i >= events.Count || Cube == null) return 24;
        var e = events[i];
        if (Cube.HasDurations && e.node >= 0 && e.node < Cube.durs.Count) return Cube.durs[e.node];
        return Snap(e.len * ProjectConfig.TicksPerBeat);
    }

    /// <summary>The nearest of the ten lengths (in doublings: a stretch feels even at every size).</summary>
    public static int Snap(float ticks)
    {
        float lt = Mathf.Log(Mathf.Max(1f, ticks), 2f);
        int best = Lengths[0]; float bd = float.MaxValue;
        for (int i = 0; i < Lengths.Length; i++) { float d = Mathf.Abs(Mathf.Log(Lengths[i], 2f) - lt); if (d < bd) { bd = d; best = Lengths[i]; } }
        return best;
    }

    // ================================================================== selection and edits
    void RaiseSelected(int node) { OnNoteSelected?.Invoke(node); }

    /// <summary>Selects block <paramref name="i"/> (its note plays when <paramref name="audition"/>); -1 deselects.</summary>
    public void SelectEvent(int i, bool audition)
    {
        if (Cube == null || i < 0 || i >= nBlocks) { Deselect(); return; }
        selEv = i;
        SelectedNote = events[i].node;
        PlaceSelection();
        if (audition && !events[i].rest) InkUI.AuditionNote(Cube, SelectedNote, events[i].len);
        else if (audition) AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f);
        blocks[i].bounceT = 0f;
        RaiseSelected(SelectedNote);
    }

    /// <summary>Selects the first block of node <paramref name="node"/> (-1 deselects).</summary>
    public void SelectNode(int node)
    {
        if (node < 0) { Deselect(); return; }
        for (int i = 0; i < nBlocks; i++) if (events[i].node == node) { SelectEvent(i, false); return; }
    }

    public void Deselect() { SetSelection(-1, true); }

    void SetSelection(int node, bool raise)
    {
        bool was = SelectedNote >= 0;
        SelectedNote = node; selEv = -1;
        PlaceSelection();
        if (raise && (was || node >= 0)) RaiseSelected(node);
    }

    void PlaceSelection()
    {
        bool on = selEv >= 0 && selEv < nBlocks && SelectedNote >= 0;
        if (underline.gameObject.activeSelf != on) underline.gameObject.SetActive(on);
        if (tearRt.gameObject.activeSelf != on) tearRt.gameObject.SetActive(on);
        if (!on) return;
        var b = blocks[selEv];
        float w = Mathf.Max(14f, b.x1 - b.x0);
        InkUI.Centre(underline.rectTransform, new Vector2((b.x0 + b.x1) * 0.5f, BlockBottom - 9f), new Vector2(w, 8f));
        tearRt.anchoredPosition = new Vector2(Mathf.Min(b.x1 - 2f, Width * 0.5f - TearPx * 0.5f), Mathf.Min(BlockBottom + b.h + 2f, Height * 0.5f - RulerH - 2f));
    }

    /// <summary>Rest on / off for a node (one History entry).</summary>
    public void ToggleRestAt(int node)
    {
        var c = Cube;
        if (c == null || node < 0 || node >= c.nodes.Count) return;
        ApplyRest(c, node);
    }

    /// <summary>Deletes the selected note (the cube keeps at least one; a refused delete shakes); one History entry.</summary>
    public void DeleteSelected()
    {
        var c = Cube;
        int node = SelectedNote;
        if (c == null || node < 0 || node >= c.nodes.Count) return;
        if (c.nodes.Count <= 1) { Refuse(); return; }
        EnsureDurations(c);
        if (!c.RemoveNode(node)) { Refuse(); return; }
        AudioPool.UI(ProceduralAudio.Pop(), 0.35f, 1.1f);
        History.Push();
        EditCount++;
        CubeInspector.NoteRhythmEdited();
        sig = int.MinValue;
        int next = Mathf.Min(node, c.nodes.Count - 1);
        Rebuild();
        SelectNode(next);
        if (SelectedNote < 0) SetSelection(-1, true);
    }

    /// <summary>Tests: a stretch of block <paramref name="i"/> to <paramref name="ticks"/> through the drag path (one History entry).</summary>
    public bool SimStretch(int i, int ticks)
    {
        if (i < 0 || i >= nBlocks) return false;
        Down(BlockEdge(i), 0);
        if (gesture != G.Stretch) { gesture = G.None; return false; }
        float end = stretchStart + ticks / (float)ProjectConfig.TicksPerBeat;
        Drag(new Vector2(X0 + end * ppb, BlockEdge(i).y));
        Up(new Vector2(X0 + end * ppb, BlockEdge(i).y), 0);
        return true;
    }
    /// <summary>Tests: one frame of the strip's own work (GC measurements).</summary>
    public void StepForTest() { Update(); }
    /// <summary>Tests: pointer steps in strip-local coordinates (button 0 left, 1 right).</summary>
    public void SimDown(Vector2 lp, int button = 0) { Down(lp, button); }
    public void SimDrag(Vector2 lp) { Drag(lp); }
    public void SimUp(Vector2 lp, int button = 0) { Up(lp, button); }

    void Refuse()
    {
        AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.4f);
        if (selEv >= 0 && selEv < nBlocks) blocks[selEv].bounceT = 0f;
    }

    // ------------------------------------------------------------------ the one path every length / rest edit takes (strip and length row)
    static void EnsureDurations(AudioCube c)
    {
        if (c == null || c.HasDurations) return;
        c.BakeDurations();   // the first rhythm edit of a legacy cube: its uniform steps / necklace gaps become per-note lengths
        BakeCalls++;
    }

    /// <summary>Node <paramref name="node"/> of <paramref name="c"/> lasts <paramref name="ticks"/> from now: a legacy cube is baked first,
    /// then AudioCube.SetDuration; one History entry; the note plays at its new length.</summary>
    public static void ApplyLength(AudioCube c, int node, int ticks)
    {
        if (c == null || node < 0 || node >= c.nodes.Count) return;
        EnsureDurations(c);
        if (node < c.durs.Count && ticks > c.durs[node] && ticks > MaxTicksFor(c, node)) { CapacityRefused(c); return; }   // v7 §19.1: finished paths keep the cap
        c.SetDuration(node, ticks);
        History.Push();
        CubeInspector.NoteRhythmEdited();
        Onboarding.Notify(DurationPicker.EvLengthPicked);
        InkUI.AuditionNote(c, node, ticks / (float)ProjectConfig.TicksPerBeat);
        if (Active != null && Active.Cube == c) { Active.EditCount++; Active.sig = int.MinValue; }
    }

    /// <summary>v7 §19.1 "finished paths keep the cap": the longest node <paramref name="node"/> of <paramref name="c"/> may last (ticks) before the path
    /// outgrows its grid (PathManager.CapacityOf: a chord grid / keyboard = its column's beats, a phrase = its length); int.MaxValue = no cap (Moons,
    /// stairs, a cube without durations, the pre-v6 suites' AutoHand).</summary>
    public static int MaxTicksFor(AudioCube c, int node)
    {
        if (c == null || PathManager.AutoHand || !c.HasDurations || node < 0 || node >= c.durs.Count) return int.MaxValue;
        float cap = PathManager.CapacityOf(c.Island);
        if (cap <= 0f) return int.MaxValue;
        int others = 0;
        for (int i = 0; i < c.durs.Count; i++) if (i != node) others += c.durs[i];
        return Mathf.RoundToInt(cap * ProjectConfig.TicksPerBeat) - others;
    }

    /// <summary>Lengths refused at the grid's capacity (tests).</summary>
    public static int CapacityRefusals { get; private set; }

    /// <summary>A length past the grid's capacity: a thud, the grid shakes, the one-time "capacity reached" tip and the grid view's EXPAND chip.</summary>
    static void CapacityRefused(AudioCube c)
    {
        CapacityRefusals++;
        AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.4f);
        var kb = c != null ? c.Island : null;
        if (kb != null) kb.Shake(0.12f);
        Onboarding.Notify(Onboarding.Ev.CapacityReached);
        var g = InspectorCard.Grid;
        if (g != null) g.ShowCapacityChip();
    }

    /// <summary>Rest on / off for node <paramref name="node"/> (a legacy cube is baked first); one History entry.</summary>
    public static void ApplyRest(AudioCube c, int node)
    {
        if (c == null || node < 0 || node >= c.nodes.Count) return;
        EnsureDurations(c);
        c.ToggleRest(node);
        History.Push();
        CubeInspector.NoteRhythmEdited();
        if (c.ModOf(node) == 1) AudioPool.UI(ProceduralAudio.Tick(), 0.35f, 0.75f);
        else InkUI.AuditionNote(c, node, c.DurBeats(node));
        if (Active != null && Active.Cube == c) { Active.EditCount++; Active.sig = int.MinValue; }
    }
}
