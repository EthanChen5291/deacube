using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 (SPEC v7 §13.3, §17.1; package B): the SECTION PLINTHS — the user: "make really intuitive the idea of sections. like 4 beats in a measure,
/// 4 measures in a 'section' of a musical idea … make it really visual, with support if needed". Every section stands on one low ink-stone slab
/// under all its columns and lanes (and the melody phrases behind them), made of one block per MEASURE (grooves between them: a section of four
/// reads as four stones), each column's blocks at that column's ground (a stair inside a section steps the plinth). Along its front edge a RULER:
/// the measure numbers 1 2 3 4 at each measure's start and four BEAT PIPS per measure (the downbeat's bigger, the playing beat's lit); its LETTER
/// (A, B, C … by first appearance; sections with the same chords in the same order share a letter, so a duplicated section keeps its source's)
/// in the margin before the first measure, with the section's NAME beside it when it has one (§17.1: intro / verse / chorus / bridge / drop /
/// outro — icon + lowercase word); a section shorter than SectionBars shows faint GHOST MEASURES (dashed outlines, faint numbers and pips) where
/// the rest would go ("a section is 4 measures"); while it plays a soft PLAYHEAD line sweeps left → right across it (W glows it:
/// <see cref="Playhead"/>). Created at runtime like WorldMagic; follows SongManager (OnSongRebuilt, OnColumnsChanged, OnSectionsChanged,
/// OnGroundsChanged) and re-pitches / re-tints the stairs and phrases after every rebuild (KeyBlock.RefreshStairs / RefreshPhraseChords).
/// No colliders: <see cref="PlinthAt"/> / <see cref="Pick"/> answer by geometry (D's Shift + click, U1's section header), so a plinth never
/// blocks a tile, island or sea pick.
/// </summary>
[DefaultExecutionOrder(940)]
public class SectionPlinth : MonoBehaviour
{
    public static SectionPlinth I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { Ensure(); }

    /// <summary>The instance (created on first use in Play mode).</summary>
    public static SectionPlinth Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<SectionPlinth>();
            if (I == null && Application.isPlaying) I = new GameObject("SectionPlinths").AddComponent<SectionPlinth>();
        }
        return I;
    }

    // ------------------------------------------------------------------ geometry
    /// <summary>The plinth's top sits this far under its column's ground (under the beat tracks, −0.68, and the belts, −0.74), this thick.</summary>
    public const float TopDrop = 0.95f, Thick = 0.34f;
    /// <summary>Margins around the section's islands: in front (the ruler), behind, the label margin before the first measure, after the last.</summary>
    public const float FrontMargin = 1.35f, BackMargin = 0.45f, LabelMargin = 1.8f, EndMargin = 0.5f;
    /// <summary>The ruler's row: this far in front of the section's front-most platform edge.</summary>
    public const float RulerZ = 0.66f;
    /// <summary>A falling stair's column: the plinth's top this far under the stair's base.</summary>
    public const float BaseClear = 0.14f;
    /// <summary>The groove between two measure blocks; the ruler's pip sizes (a beat, a downbeat); the measure numbers' and the letter's heights.</summary>
    public const float Groove = 0.14f, PipSize = 0.32f, PipDownSize = 0.48f, NumberHeight = 0.6f, LetterHeight = 0.7f, WordHeight = 0.34f;
    /// <summary>v7 (the user: "the letter a bit smaller, with a circle around it"): the letter's circle (diameter) and its rim, inside the front margin.</summary>
    public const float LetterDisc = 1.56f, LetterRim = 0.12f;
    public static readonly Color Stone = new Color(0.45f, 0.39f, 0.51f), GhostStone = new Color(0.66f, 0.60f, 0.78f, 0.17f);
    public static readonly Color NumberColor = new Color(0.97f, 0.93f, 1f, 0.92f), LetterColor = new Color(1f, 0.95f, 0.88f, 1f);
    public static readonly Color PlayheadColor = new Color(1f, 0.93f, 0.72f, 1f);

    // ------------------------------------------------------------------ the static API (D, U1, W, tests)
    /// <summary>Plinths shown (one per section).</summary>
    public static int Count => I != null ? I.views.Count : 0;
    // v7 (the user: "the section letters shouldn't be black and white — more resonant colours"): every letter has its own colour (sections that
    // share a letter share it) — the plinth's circle, the column rail's disc and the section header's tag all wear it; the rim and the letter's
    // outline are its deep shade, never black.
    static readonly Color[] LetterHues =
    {
        new Color(0.98f, 0.70f, 0.20f),   // A marigold
        new Color(0.95f, 0.42f, 0.53f),   // B coral rose
        new Color(0.19f, 0.75f, 0.71f),   // C teal
        new Color(0.58f, 0.46f, 0.96f),   // D violet
        new Color(0.49f, 0.79f, 0.33f),   // E leaf
        new Color(0.97f, 0.55f, 0.29f),   // F tangerine
        new Color(0.32f, 0.63f, 0.96f),   // G azure
        new Color(0.88f, 0.38f, 0.78f),   // H magenta
    };
    static int LetterHue(string letter) => !string.IsNullOrEmpty(letter) && letter[0] >= 'A' && letter[0] <= 'Z' ? (letter[0] - 'A') % LetterHues.Length : 0;
    /// <summary>The colour of section letter <paramref name="letter"/> (A marigold, B coral, C teal, D violet, E leaf, F tangerine, G azure, H magenta, …).</summary>
    public static Color LetterTint(string letter) => LetterHues[LetterHue(letter)];
    /// <summary>The deep shade of a letter's colour: its circle's rim, the letter's outline, the HUD's letter.</summary>
    public static Color LetterDeep(string letter)
    {
        Color.RGBToHSV(LetterTint(letter), out float h, out float sat, out float v);
        return Color.HSVToRGB(h, Mathf.Clamp01(sat * 1.05f + 0.12f), v * 0.42f);
    }
    /// <summary>The letter's face on the plinth: cream warmed by its colour.</summary>
    public static Color LetterFace(string letter) => Color.Lerp(LetterColor, LetterTint(letter), 0.14f);

    /// <summary>The letter plinth <paramref name="s"/> shows ("A", "B", … ; "" when there is none).</summary>
    public static string LetterOf(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].letter : "";
    /// <summary>The section name (§17.1) plinth <paramref name="s"/> shows (0 none, 1 intro .. 6 outro).</summary>
    public static int RoleShown(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].role : 0;
    /// <summary>The section whose plinth lies under world point <paramref name="world"/> (x / z inside its top footprint, ghosts included); −1 none.</summary>
    public static int PlinthAt(Vector3 world)
    {
        if (I == null) return -1;
        for (int s = 0; s < I.views.Count; s++)
        {
            var v = I.views[s];
            if (world.x >= v.west && world.x <= v.east && world.z >= v.zFront && world.z <= v.zBack) return s;
        }
        return -1;
    }
    /// <summary>The section whose plinth the ray hits first (its blocks' boxes); −1 none.</summary>
    public static int Pick(Ray ray)
    {
        if (I == null) return -1;
        int best = -1; float bestD = float.MaxValue;
        for (int s = 0; s < I.views.Count; s++)
            foreach (var b in I.views[s].blocks)
            {
                if (b.t == null || !b.t.gameObject.activeSelf) continue;
                float d;
                if (b.WorldBox.IntersectRay(ray, out d) && d < bestD) { bestD = d; best = s; }
            }
        return best;
    }
    /// <summary>World bounds of plinth <paramref name="s"/> (every block, ghosts included).</summary>
    public static Bounds Bounds(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].Box() : new Bounds();
    /// <summary>World point at the front-left corner of plinth <paramref name="s"/>'s top (a UI anchor).</summary>
    public static Vector3 FrontLeft(int s)
    {
        if (I == null || s < 0 || s >= I.views.Count) return Vector3.zero;
        var v = I.views[s];
        return new Vector3(v.west, v.TopAt(0), v.zFront);
    }
    /// <summary>
    /// The playhead line this frame (for W's glow): its world end points across the playing section's plinth (front → back at the song's time
    /// on the lit column's platform) and its colour. False when no section plays (stopped or nothing there).
    /// </summary>
    public static bool Playhead(out Vector3 from, out Vector3 to, out Color colour)
    {
        from = to = Vector3.zero; colour = PlayheadColor;
        if (I == null || I.playSection < 0 || I.playSection >= I.views.Count) return false;
        var v = I.views[I.playSection];
        from = new Vector3(I.playX, I.playY, v.zFront + 0.12f);
        to = new Vector3(I.playX, I.playY, v.zBack - 0.12f);
        return true;
    }
    /// <summary>The section playing now (−1 none) and the lit ruler pip (section-wide beat index: measure × BeatsPerBar + beat; −1 none).</summary>
    public static int PlayingSection => I != null ? I.playSection : -1;
    public static int LitRulerPip => I != null ? I.litPip : -1;
    /// <summary>Tests: the measures plinth <paramref name="s"/> shows (real; ghosts apart).</summary>
    public static int MeasuresShown(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].barsReal : 0;
    public static int GhostsShown(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].ghosts : 0;
    /// <summary>Tests: the measure numbers plinth <paramref name="s"/> shows, in order ("1", "2", …; ghosts included).</summary>
    public static List<string> NumbersShown(int s)
    {
        var r = new List<string>();
        if (I == null || s < 0 || s >= I.views.Count) return r;
        foreach (var b in I.views[s].blocks) if (b.number != null && b.t.gameObject.activeSelf) r.Add(b.number.text);
        return r;
    }
    /// <summary>Tests: the ruler pips of plinth <paramref name="s"/> (count; which are downbeats), and the world x span of its measure <paramref name="m"/>.</summary>
    public static int PipsShown(int s) { int n = 0; if (I != null && s >= 0 && s < I.views.Count) foreach (var b in I.views[s].blocks) if (b.t.gameObject.activeSelf) n += b.pips.Count; return n; }
    public static bool MeasureSpan(int s, int m, out float x0, out float x1, out bool ghost, out int col)
    {
        x0 = x1 = 0f; ghost = false; col = -1;
        if (I == null || s < 0 || s >= I.views.Count || m < 0 || m >= I.views[s].blocks.Count) return false;
        var b = I.views[s].blocks[m];
        x0 = b.beatX0; x1 = b.beatX1; ghost = b.ghost; col = b.col;
        return true;
    }
    /// <summary>Tests: how far measure <paramref name="m"/>'s block top sits under its column's ground (TopDrop; more under a falling stair).</summary>
    public static float DropOf(int s, int m) => I != null && s >= 0 && s < I.views.Count && m >= 0 && m < I.views[s].blocks.Count ? I.views[s].blocks[m].drop : TopDrop;
    /// <summary>Tests: the section-name picture plinth <paramref name="s"/> shows (null when none).</summary>
    public static Transform RoleIconShown(int s) => I != null && s >= 0 && s < I.views.Count && I.views[s].roleIcon != null && I.views[s].roleIcon.gameObject.activeSelf ? I.views[s].roleIcon : null;
    public static string RoleWordShown(int s) => I != null && s >= 0 && s < I.views.Count && I.views[s].roleText != null && I.views[s].roleText.gameObject.activeSelf ? I.views[s].roleText.text : "";
    /// <summary>Tests: the world y of the top of measure <paramref name="m"/>'s block in plinth <paramref name="s"/>.</summary>
    public static float TopOf(int s, int m) => I != null && s >= 0 && s < I.views.Count ? I.views[s].TopAt(m) : 0f;
    /// <summary>v9 (W, the present mode — "islands appearing from the water as the measures pass"): how far each column's measure blocks stand under
    /// their place (world units, by column; null = none). The presentation writes it every frame from the song beat (a column's floor rises from the
    /// sea under its islands) and clears it on exit; UpdateGrounds applies it (the ruler, the letter and the playhead ride on the blocks).</summary>
    public static float[] ColumnSinks;
    /// <summary>v9: column <paramref name="col"/>'s floor sink now (0 when none).</summary>
    public static float SinkOf(int col) { var s = ColumnSinks; return s != null && col >= 0 && col < s.Length ? s[col] : 0f; }
    /// <summary>Tests: re-reads the song now (the next LateUpdate would).</summary>
    public static void RefreshNow() { var p = Ensure(); if (p != null) { p.dirty = true; p.Sync(); } }
    /// <summary>Milliseconds of the last LateUpdate / the last relayout.</summary>
    public static double LastMs => I != null ? I.lastMs : 0.0;
    public static double LastLayoutMs => I != null ? I.layoutMs : 0.0;
    /// <summary>Relayouts since Play (tests: nothing rebuilds while the song just plays).</summary>
    public static int Layouts => I != null ? I.layouts : 0;

    // ------------------------------------------------------------------ state
    class Block
    {
        public Transform t; public MeshFilter mf; public MeshRenderer mr; public Vector3 size; public float x0, x1, beatX0, beatX1, zc;
        public int col, measure; public bool ghost; public float shownGround = float.NaN, drop = TopDrop;
        public TextMeshPro number; public readonly List<Renderer> pips = new List<Renderer>(); public LineRenderer dash;
        public Bounds WorldBox => new Bounds(t.position + new Vector3(0f, -Thick * 0.5f, 0f), size);
    }
    class View
    {
        public Transform root; public int section, role, barsReal, ghosts, firstCol, lastCol; public string letter = "";
        public float west, east, zFront, zBack, zRuler;
        public readonly List<Block> blocks = new List<Block>();
        public TextMeshPro letterText, roleText; public Transform roleIcon; public Renderer roleIconR;
        public Transform letterDisc; public Renderer letterRimR, letterFillR;
        public Transform playhead, playTick, halo; public Renderer playheadR;
        public float pulse;   // v9 (R): the letter disc's pulse now
        public float TopAt(int m) => m >= 0 && m < blocks.Count && blocks[m].t != null && blocks[m].t.gameObject.activeSelf ? blocks[m].t.position.y : 0f;
        public Bounds Box()
        {
            bool any = false; var b = new Bounds();
            foreach (var k in blocks) { if (k.t == null || !k.t.gameObject.activeSelf) continue; if (!any) { b = k.WorldBox; any = true; } else b.Encapsulate(k.WorldBox); }
            return b;
        }
    }
    struct Col { public int col; public float west, east, width, front, back, drop; public int bars; public KeyBlock anchor; }

    readonly List<View> views = new List<View>();
    SongManager hooked;
    bool dirty = true, stairsDirty = true;
    float stairsClock, sigClock;
    int layoutSig;
    int playSection = -1, litPip = -1; float playX, playY;
    double lastMs, layoutMs; int layouts;
    readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
    Material stoneMat, ghostMat, playMat, dashMat, roleMat;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void OnEnable() { AudioCube.OnAnyChanged += HandleCube; }
    void OnDisable() { AudioCube.OnAnyChanged -= HandleCube; Unhook(); }
    void OnDestroy()
    {
        if (I == this) I = null;
        foreach (var m in new[] { stoneMat, ghostMat, playMat, dashMat, roleMat }) if (m != null) Destroy(m);
    }

    void Hook()
    {
        var sm = SongManager.I;
        if (sm == hooked) return;
        Unhook();
        if (sm == null) return;
        sm.OnSongRebuilt += HandleRebuilt;
        sm.OnColumnsChanged += HandleColumns;
        sm.OnSectionsChanged += HandleColumns;
        sm.OnGroundsChanged += HandleColumns;
        hooked = sm;
        dirty = true; stairsDirty = true;
    }

    void Unhook()
    {
        if (hooked == null) return;
        hooked.OnSongRebuilt -= HandleRebuilt;
        hooked.OnColumnsChanged -= HandleColumns;
        hooked.OnSectionsChanged -= HandleColumns;
        hooked.OnGroundsChanged -= HandleColumns;
        hooked = null;
    }

    // a rebuild re-created every KeyBlock: the stairs / phrases re-read their chords at once (before the first frame is drawn)
    void HandleRebuilt() { dirty = true; RefreshKinds(); }
    void HandleColumns() { dirty = true; stairsDirty = true; }
    void HandleCube(AudioCube c) { stairsDirty = true; }

    /// <summary>Every stairs island re-pitches (StairPitches reads the column's chord, the next chord and the melody around it) and every phrase
    /// re-reads its column chords.</summary>
    static void RefreshKinds()
    {
        var sm = SongManager.I;
        if (sm == null) return;
        foreach (var kb in sm.Islands)
        {
            if (kb == null) continue;
            if (kb.IsStairs) kb.RefreshStairs();
            else if (kb.IsPhrase) kb.RefreshPhraseChords();
        }
    }

    void LateUpdate()
    {
        sw.Reset(); sw.Start();
        Hook();
        float dt = Time.unscaledDeltaTime;
        sigClock += dt;
        if (sigClock > 0.4f) { sigClock = 0f; if (LayoutSignature() != layoutSig) dirty = true; }
        if (dirty) Sync();
        stairsClock += dt;
        if (stairsDirty && stairsClock > 0.25f) { stairsClock = 0f; stairsDirty = false; RefreshKinds(); }
        UpdateGrounds();
        UpdatePlay();
        sw.Stop();
        lastMs = sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>A cheap hash of what the layout reads (the islands' logical positions, columns, kinds, widths, bars; the sections): a change that
    /// came without an event still relayouts (checked every 0.4 s).</summary>
    int LayoutSignature()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return 0;
        unchecked
        {
            int h = 17 + sm.Islands.Count * 7919;
            foreach (var kb in sm.Islands)
            {
                if (kb == null) continue;
                h = h * 31 + kb.column; h = h * 31 + kb.kind; h = h * 31 + kb.bars; h = h * 31 + kb.Passes;
                h = h * 31 + Mathf.RoundToInt(kb.px * 20f); h = h * 31 + Mathf.RoundToInt(kb.pz * 20f); h = h * 31 + Mathf.RoundToInt(kb.Width * 20f);
            }
            foreach (int s in sm.SectionStarts()) h = h * 31 + s;
            h = h * 31 + GlobalClock.BeatsPerBar;
            return h;
        }
    }

    // ------------------------------------------------------------------ layout
    /// <summary>Rebuilds every plinth from the song as it is laid out now (pooled: blocks, numbers and pips are reused).</summary>
    void Sync()
    {
        dirty = false;
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        layoutSig = LayoutSignature();
        layouts++;
        var sm = SongManager.I;
        int nSec = sm != null && sm.HasSong ? sm.SectionCount : 0;
        // columns of every section, their extents
        var secCols = new List<List<Col>>(nSec);
        for (int s = 0; s < nSec; s++) secCols.Add(ColumnsOf(sm, s));
        var letters = Letters(sm, nSec);
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        for (int s = 0; s < nSec; s++)
        {
            var cols = secCols[s];
            View v = s < views.Count ? views[s] : null;
            if (v == null) { v = NewView(s); views.Add(v); }
            v.section = s; v.letter = letters[s]; v.role = RoleOf(sm, s);
            if (cols.Count == 0) { HideView(v); continue; }
            if (!v.root.gameObject.activeSelf) v.root.gameObject.SetActive(true);
            v.firstCol = cols[0].col; v.lastCol = cols[cols.Count - 1].col;
            float front = float.MaxValue, back = float.MinValue;
            foreach (var c in cols) { front = Mathf.Min(front, c.front); back = Mathf.Max(back, c.back); }
            // the melody phrases behind the section's columns
            float sb = sm.SectionStartBeat(s), se = sm.SectionEndBeat(s);
            foreach (var kb in sm.Islands)
            {
                if (kb == null || !kb.IsPhrase) continue;
                float ps, pe; sm.PhraseSpan(kb, out ps, out pe);
                if (pe <= sb + 1e-3f || ps >= se - 1e-3f) continue;
                front = Mathf.Min(front, kb.FrontEdge); back = Mathf.Max(back, kb.BackEdge);
            }
            v.zFront = front - FrontMargin; v.zBack = back + BackMargin; v.zRuler = front - RulerZ;
            // the measures: each column's bars over its platform span (a belt's slots stay on its last measure's block)
            var slots = new List<Block>();
            int measure = 0;
            foreach (var c in cols)
                for (int m = 0; m < c.bars; m++)
                {
                    var b = BlockAt(v, measure);
                    b.col = c.col; b.measure = measure; b.ghost = false; b.drop = c.drop;
                    b.beatX0 = c.west + m * c.width / c.bars; b.beatX1 = c.west + (m + 1) * c.width / c.bars;
                    b.x0 = b.beatX0; b.x1 = m == c.bars - 1 ? Mathf.Max(b.beatX1, c.east) : b.beatX1;
                    slots.Add(b); measure++;
                }
            v.barsReal = measure;
            // ghost measures after the last one (as many as fit before the next section's plinth)
            int want = Mathf.Max(0, ProjectConfig.SectionBars - measure), fit = want;
            float lastX1 = slots[slots.Count - 1].x1, w = KeyBlock.IslandWidth;
            if (s + 1 < nSec && secCols[s + 1].Count > 0)
            {
                float nextWest = float.MaxValue; foreach (var c in secCols[s + 1]) nextWest = Mathf.Min(nextWest, c.west);
                float room = nextWest - LabelMargin - 0.6f - EndMargin - lastX1;
                fit = Mathf.Clamp(Mathf.FloorToInt(room / w + 1e-3f), 0, want);
            }
            for (int g = 0; g < fit; g++)
            {
                var b = BlockAt(v, measure);
                b.col = cols[cols.Count - 1].col; b.measure = measure; b.ghost = true; b.drop = cols[cols.Count - 1].drop;
                b.beatX0 = lastX1 + g * w; b.beatX1 = b.beatX0 + w; b.x0 = b.beatX0; b.x1 = b.beatX1;
                slots.Add(b); measure++;
            }
            v.ghosts = fit;
            slots[0].x0 -= LabelMargin;
            slots[slots.Count - 1].x1 += EndMargin;
            v.west = slots[0].x0; v.east = slots[slots.Count - 1].x1;
            for (int i = 0; i < slots.Count; i++) PoseBlock(v, slots[i], i == 0, i == slots.Count - 1, bpb);
            for (int i = slots.Count; i < v.blocks.Count; i++) if (v.blocks[i].t != null && v.blocks[i].t.gameObject.activeSelf) v.blocks[i].t.gameObject.SetActive(false);
            PoseLabel(v, slots[0]);
        }
        for (int s = views.Count - 1; s >= nSec; s--) { if (views[s].root != null) Destroy(views[s].root.gameObject); views.RemoveAt(s); }
        foreach (var v in views) if (v.halo != null && v.halo.gameObject.activeSelf) v.halo.gameObject.SetActive(false);   // the blocks were re-lit at rest
        playSection = -1; litPip = -1;
        layoutMs = t0.Elapsed.TotalMilliseconds;
    }

    /// <summary>The columns of section <paramref name="s"/>: K's platform span (ColumnWestEdge .. + ColumnWidth = bars × IslandWidth; the east edge
    /// with its belts: ColumnEastEdge), its lanes' front / back edges (logical: the belt ride, drags and slides never move a plinth) and bars;
    /// phrases never set a column's span.</summary>
    static List<Col> ColumnsOf(SongManager sm, int s)
    {
        var r = new List<Col>();
        int a = sm.SectionFirst(s), b = sm.SectionLast(s);
        for (int c = a; c >= 0 && c <= b; c++)
        {
            var col = new Col { col = c, front = float.MaxValue, back = float.MinValue, bars = Mathf.Max(1, sm.ColumnBars(c)), anchor = sm.AnchorOf(c), drop = TopDrop };
            if (col.anchor == null) continue;
            col.west = sm.ColumnWestEdge(c); col.width = sm.ColumnWidth(c); col.east = Mathf.Max(col.west + col.width, sm.ColumnEastEdge(c));
            foreach (var kb in sm.Islands)
            {
                if (kb == null || kb.IsMoon || kb.IsPhrase || kb.column != c) continue;
                col.front = Mathf.Min(col.front, kb.FrontEdge); col.back = Mathf.Max(col.back, kb.BackEdge);
                // a falling stair reaches below the default platform: the column's plinth drops under its base (the lowest steps stay in view)
                col.drop = Mathf.Max(col.drop, 0.03f + ProjectConfig.PlatformThickness + kb.BaseDrop + BaseClear);
            }
            if (col.front > col.back) { col.front = col.anchor.FrontEdge; col.back = col.anchor.BackEdge; }
            r.Add(col);
        }
        return r;
    }

    /// <summary>A letter per section by content (§13.3 "A, B, C … by first appearance; a duplicated section keeps its source's letter"): sections
    /// whose columns carry the same chords (root pitch class + chord tones) and bars, in the same order, share a letter.</summary>
    static List<string> Letters(SongManager sm, int nSec)
    {
        var r = new List<string>(nSec);
        var seen = new Dictionary<string, string>();
        var sb = new StringBuilder();
        for (int s = 0; s < nSec; s++)
        {
            string own = sm.ExplicitLetter(s);   // v9 (G): a song may name its sections itself (SongState.sectionLetters: the gallery's "get proto")
            if (own != null) { r.Add(own); continue; }
            sb.Length = 0;
            int a = sm.SectionFirst(s), b = sm.SectionLast(s);
            for (int c = a; c >= 0 && c <= b; c++)
            {
                var ch = sm.ChordOfColumn(c);
                if (ch == null) sb.Append("-");
                else
                {
                    sb.Append(Harmony.Pc(ch.chordRootMIDI)).Append(':');
                    var semis = new List<int>(Harmony.SemisOf(ch)); semis.Sort();
                    foreach (int x in semis) sb.Append(x).Append(',');
                }
                sb.Append('x').Append(sm.ColumnBars(c)).Append('|');
            }
            string key = sb.ToString(), letter;
            if (!seen.TryGetValue(key, out letter)) { letter = LetterName(seen.Count); seen[key] = letter; }
            r.Add(letter);
        }
        return r;
    }

    /// <summary>A, B, … Z, then A2, B2 …</summary>
    public static string LetterName(int i) => i < 26 ? ((char)('A' + i)).ToString() : ((char)('A' + i % 26)).ToString() + (i / 26 + 1);

    /// <summary>§17.1: the section's name (K: SongManager.SectionRole — the secRole of its first column's anchor); 0 none.</summary>
    static int RoleOf(SongManager sm, int s)
    {
        if (sm == null) return 0;
        int r = sm.SectionRole(s);
        return SectionRoles.Valid(r) ? r : 0;
    }

    // ------------------------------------------------------------------ building (pooled)
    Material StoneMat { get { if (stoneMat == null) { stoneMat = Fx.Lit(Stone, 0.2f, 0f); stoneMat.SetColor("_EmissionColor", Stone * 0.12f); } return stoneMat; } }
    Material GhostMat => ghostMat != null ? ghostMat : (ghostMat = Fx.Alpha(IconFactory.GetTexture("white"), GhostStone));
    Material PlayMat => playMat != null ? playMat : (playMat = Fx.Additive(StructureLook.SoftLineTexture, PlayheadColor, 1.7f));
    Material DashMat { get { if (dashMat == null) { dashMat = Fx.Alpha(StructureLook.DashTexture, new Color(0.96f, 0.92f, 1f, 0.62f)); } return dashMat; } }
    Material RoleMat => roleMat != null ? roleMat : (roleMat = Fx.Alpha(IconFactory.GetTexture("white"), LetterColor));

    View NewView(int s)
    {
        var v = new View();
        v.root = new GameObject("Plinth_" + s).transform;
        v.root.SetParent(transform, false);
        var ph = StructureLook.Decal(v.root, "Playhead", Vector3.zero, 1f, PlayMat);
        v.playhead = ph; v.playheadR = ph.GetComponent<Renderer>();
        ph.gameObject.SetActive(false);
        // the playhead's tick on the slab's front face (the line on top hides under the islands from the default camera)
        v.playTick = StructureLook.StandingDecal(v.root, "PlayheadTick", Vector3.zero, 1f, PlayMat);
        v.playTick.gameObject.SetActive(false);
        v.halo = StructureLook.Decal(v.root, "PipHalo", Vector3.zero, 1f, StructureLook.PipGlowMaterial);
        v.halo.gameObject.SetActive(false);
        return v;
    }

    static void HideView(View v) { if (v.root != null && v.root.gameObject.activeSelf) v.root.gameObject.SetActive(false); v.barsReal = 0; v.ghosts = 0; }

    Block BlockAt(View v, int i)
    {
        while (v.blocks.Count <= i)
        {
            var b = new Block();
            var go = new GameObject("Measure" + v.blocks.Count);
            go.transform.SetParent(v.root, false);
            b.t = go.transform;
            b.mf = go.AddComponent<MeshFilter>();
            b.mr = go.AddComponent<MeshRenderer>();
            b.mr.shadowCastingMode = ShadowCastingMode.Off;   // a low stage: it takes the islands' shadows, casts none
            b.mr.receiveShadows = true;
            v.blocks.Add(b);
        }
        var r = v.blocks[i];
        if (!r.t.gameObject.activeSelf) r.t.gameObject.SetActive(true);
        return r;
    }

    /// <summary>Poses one measure block: the slab (its top at the column's ground − TopDrop), the measure number at its start, its beat pips,
    /// a ghost's dashed outline.</summary>
    void PoseBlock(View v, Block b, bool first, bool last, int bpb)
    {
        float x0 = b.x0 + (first ? 0f : Groove * 0.5f), x1 = b.x1 - (last ? 0f : Groove * 0.5f);
        var size = new Vector3(Mathf.Max(0.2f, x1 - x0), Thick, Mathf.Max(0.2f, v.zBack - v.zFront));
        b.zc = (v.zFront + v.zBack) * 0.5f;
        b.size = size;
        b.mf.sharedMesh = MeshFactory.RoundedBoxAt(size, new Vector3(0f, -Thick * 0.5f, 0f), 0.1f, 2);
        b.mr.sharedMaterial = b.ghost ? GhostMat : StoneMat;
        float ground = GroundOf(b.col);
        b.shownGround = ground;
        b.t.position = new Vector3((x0 + x1) * 0.5f, ground - b.drop, b.zc);
        // the measure number at the measure's start, on the ruler row
        if (b.number == null) b.number = Text(b.t, "Number", "1", NumberHeight, NumberColor, TextAlignmentOptions.Center);
        b.number.text = (b.measure + 1).ToString();
        b.number.color = b.ghost ? Palette.A(NumberColor, 0.5f) : NumberColor;
        b.number.transform.position = new Vector3(b.beatX0 + 0.42f, b.t.position.y + 0.012f, v.zRuler);
        // the beat pips (BeatsPerBar of them; the downbeat's bigger)
        while (b.pips.Count > bpb) { var p = b.pips[b.pips.Count - 1]; if (p != null) Destroy(p.gameObject); b.pips.RemoveAt(b.pips.Count - 1); }
        while (b.pips.Count < bpb) b.pips.Add(StructureLook.Decal(b.t, "Pip" + b.pips.Count, Vector3.zero, 1f, StructureLook.PipRestMaterial).GetComponent<Renderer>());
        float bw = (b.beatX1 - b.beatX0) / bpb;
        for (int k = 0; k < bpb; k++)
        {
            var p = b.pips[k];
            float s = k == 0 ? PipDownSize : PipSize;
            p.transform.position = new Vector3(b.beatX0 + (k + 0.5f) * bw, b.t.position.y + 0.014f, v.zRuler);
            p.transform.localScale = new Vector3(s, 1f, s);
            p.sharedMaterial = b.ghost ? StructureLook.PipGhostMaterial : (k == 0 ? StructureLook.PipDownMaterial : StructureLook.PipRestMaterial);
        }
        // a ghost's dashed outline around its top face
        if (b.ghost)
        {
            if (b.dash == null)
            {
                var go = new GameObject("GhostOutline");
                go.transform.SetParent(b.t, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // TransformZ alignment: the ribbon lies flat
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false; lr.loop = true; lr.alignment = LineAlignment.TransformZ;
                lr.widthMultiplier = 0.07f; lr.textureMode = LineTextureMode.Tile; lr.numCornerVertices = 0; lr.numCapVertices = 0;
                lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.lightProbeUsage = LightProbeUsage.Off;
                lr.sharedMaterial = DashMat;
                DashMat.mainTextureScale = new Vector2(2.2f, 1f);
                b.dash = lr;
            }
            float hx = size.x * 0.5f - 0.1f, hz = size.z * 0.5f - 0.1f, y = -0.012f;
            // island-local rect in the rotated frame (x stays x, local y = world z, local z = world −y)
            b.dash.positionCount = 4;
            b.dash.SetPosition(0, new Vector3(-hx, -hz, y)); b.dash.SetPosition(1, new Vector3(-hx, hz, y));
            b.dash.SetPosition(2, new Vector3(hx, hz, y)); b.dash.SetPosition(3, new Vector3(hx, -hz, y));
            if (!b.dash.gameObject.activeSelf) b.dash.gameObject.SetActive(true);
        }
        else if (b.dash != null && b.dash.gameObject.activeSelf) b.dash.gameObject.SetActive(false);
    }

    /// <summary>The letter in the label margin before the first measure, on the ruler row; a named section (§17.1) adds its picture behind it —
    /// U1's SectionRoles glyph in ink on a disc of the role's tint (the rail's and the section header's language) — and its lowercase word.</summary>
    void PoseLabel(View v, Block first)
    {
        var parent = first.t;
        float cx = v.west + LabelMargin * 0.5f, y = parent.position.y + 0.013f;
        if (v.letterText == null) v.letterText = Text(parent, "Letter", "A", LetterHeight, LetterColor, TextAlignmentOptions.MidlineGeoAligned);
        else if (v.letterText.transform.parent != parent) v.letterText.transform.SetParent(parent, true);
        v.letterText.text = v.letter;
        float zl = v.zRuler + 0.17f;   // the circle stays inside the front margin (its front edge 1.27 in front of the grids, the plinth's 1.35)
        v.letterText.transform.position = new Vector3(cx, y, zl);
        v.letterText.color = LetterFace(v.letter);
        var lm = LetterMaterial(v.letter); if (lm != null && v.letterText.fontSharedMaterial != lm) v.letterText.fontSharedMaterial = lm;
        // centred on its circle by the glyph's own mesh (the alignment alone left the A off centre)
        v.letterText.ForceMeshUpdate();
        var lmesh = v.letterText.mesh;
        if (lmesh != null && lmesh.vertexCount > 0)
        {
            Vector3 off = v.letterText.transform.TransformVector(lmesh.bounds.center);
            v.letterText.transform.position = new Vector3(cx - off.x, y, zl - off.z + LetterHeight * 0.05f);   // a hair up: an A's mass sits low
        }
        // the circle: a rim in the letter's deep shade, a disc of its colour, under the letter
        if (v.letterDisc == null)
        {
            v.letterDisc = StructureLook.Decal(parent, "LetterDisc", Vector3.zero, LetterDisc, LetterDiscMaterial);
            v.letterRimR = v.letterDisc.GetComponent<Renderer>();
            var fill = StructureLook.Decal(v.letterDisc, "Fill", new Vector3(0f, 0.003f, 0f), (LetterDisc - 2f * LetterRim) / LetterDisc, LetterDiscMaterial);
            v.letterFillR = fill.GetComponent<Renderer>();
        }
        else if (v.letterDisc.parent != parent) v.letterDisc.SetParent(parent, true);
        v.letterDisc.position = new Vector3(cx, y - 0.006f, zl);
        StructureLook.Tint(v.letterRimR, LetterDeep(v.letter));
        StructureLook.Tint(v.letterFillR, LetterTint(v.letter));
        bool role = SectionRoles.Valid(v.role);
        if (role)
        {
            if (v.roleIcon == null)
            {
                v.roleIcon = StructureLook.Decal(parent, "RoleDisc", Vector3.zero, 0.98f, StructureLook.DotMaterial);
                var icon = StructureLook.Decal(v.roleIcon, "RoleIcon", new Vector3(0f, 0.004f, 0f), 0.66f, RoleIconMaterial(v.role));
                v.roleIconR = icon.GetComponent<Renderer>();
            }
            else if (v.roleIcon.parent != parent) v.roleIcon.SetParent(parent, true);
            StructureLook.Tint(v.roleIcon.GetComponent<Renderer>(), SectionRoles.Tint(v.role));
            v.roleIconR.sharedMaterial = RoleIconMaterial(v.role);
            v.roleIcon.position = new Vector3(cx, y + 0.002f, v.zRuler + 1.52f);
            if (v.roleText == null) v.roleText = Text(parent, "RoleWord", "", WordHeight, LetterColor, TextAlignmentOptions.Center);
            else if (v.roleText.transform.parent != parent) v.roleText.transform.SetParent(parent, true);
            v.roleText.text = SectionRoles.Word(v.role);
            v.roleText.transform.position = new Vector3(cx, y, v.zRuler + 2.32f);
        }
        if (v.roleIcon != null && v.roleIcon.gameObject.activeSelf != role) v.roleIcon.gameObject.SetActive(role);
        if (v.roleText != null && v.roleText.gameObject.activeSelf != role) v.roleText.gameObject.SetActive(role);
    }

    static readonly Dictionary<int, Material> roleMats = new Dictionary<int, Material>();
    /// <summary>U1's section-name picture in ink (IconFactory "role1".."role6").</summary>
    static Material RoleIconMaterial(int role)
    {
        Material m;
        if (roleMats.TryGetValue(role, out m) && m != null) return m;
        SectionRoles.Ensure();
        m = Fx.Alpha(IconFactory.GetTexture(SectionRoles.Glyph(role)), Palette.A(Look.InkColor, 0.92f));
        roleMats[role] = m;
        return m;
    }

    static Material letterDiscMat;
    /// <summary>The letter circle's disc: the dot decal drawn in the queue just before TextMeshPro's (Transparent), so the letter lies on top.</summary>
    static Material LetterDiscMaterial
    {
        get
        {
            if (letterDiscMat == null) { letterDiscMat = new Material(StructureLook.DotMaterial) { name = "SectionLetterDisc" }; letterDiscMat.renderQueue = 2999; }
            return letterDiscMat;
        }
    }
    static readonly Dictionary<int, Material> letterMats = new Dictionary<int, Material>();
    /// <summary>The comic lettering with its outline in the letter's deep shade (one material per letter colour).</summary>
    static Material LetterMaterial(string letter)
    {
        int k = LetterHue(letter);
        Material m;
        if (letterMats.TryGetValue(k, out m) && m != null) return m;
        var f = Comic.Font; var ink = f != null ? Comic.InkMaterial(f) : null;
        if (ink == null) return null;
        m = new Material(ink) { name = "SectionLetter" + k };
        m.SetColor("_OutlineColor", LetterDeep(letter));
        m.SetFloat("_OutlineWidth", 0.26f);
        // the drop shadow in the deep shade too (never black), shorter: on a circle a long offset shadow makes the letter look off centre
        m.SetColor("_UnderlayColor", Color.Lerp(LetterDeep(letter), Color.black, 0.2f));
        m.SetFloat("_UnderlayOffsetX", 0.28f); m.SetFloat("_UnderlayOffsetY", -0.34f);
        letterMats[k] = m;
        return m;
    }

    /// <summary>A world text lying flat on the plinth (readable from the default camera), in the comic ink lettering (cream, ink outline).</summary>
    static TextMeshPro Text(Transform parent, string name, string text, float height, Color c, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var t = go.AddComponent<TextMeshPro>();
        var f = Comic.Font;
        if (f != null) { t.font = f; var m = Comic.InkMaterial(f); if (m != null) t.fontSharedMaterial = m; }
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.alignment = align;
        t.rectTransform.sizeDelta = new Vector2(4f, height * 1.6f);
        t.fontSize = height * FontPerUnit;
        t.color = c;
        t.text = text;
        var r = go.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        return t;
    }

    /// <summary>TextMeshPro font size per world unit of cap height (Fredoka at TMP's 3D text scale; measured).</summary>
    public static float FontPerUnit = 13.9f;

    // ------------------------------------------------------------------ per frame
    float GroundOf(int col)
    {
        var sm = SongManager.I;
        var a = sm != null ? sm.AnchorOf(col) : null;
        return a != null ? a.GroundY : 0f;
    }

    /// <summary>Each block follows its column's ground as it glides (K: KeyBlock.GroundY).</summary>
    void UpdateGrounds()
    {
        foreach (var v in views)
        {
            if (v.root == null || !v.root.gameObject.activeSelf) continue;
            foreach (var b in v.blocks)
            {
                if (b.t == null || !b.t.gameObject.activeSelf) continue;
                float g = GroundOf(b.col) - SinkOf(b.col);   // v9: the present mode's floor rising from the sea
                if (Mathf.Abs(g - b.shownGround) < 1e-4f) continue;
                b.shownGround = g;
                var p = b.t.position; p.y = g - b.drop; b.t.position = p;
            }
        }
    }

    // ------------------------------------------------------------------ v9 (R): the letter's pulse on its section's first downbeat
    /// <summary>v9 (R): the letter disc's pulse window (beats) and its size (energy 0 .. 3).</summary>
    public static float PulseBeats = 0.8f, PulseMin = 0.22f, PulseMax = 0.46f;
    /// <summary>v9 (R): the pulse plinth <paramref name="s"/>'s letter disc shows now (0 at rest) and pulses seen since Play (tests).</summary>
    public static float PulseOf(int s) => I != null && s >= 0 && s < I.views.Count ? I.views[s].pulse : 0f;
    public static int Pulses => I != null ? I.pulses : 0;
    int pulses, kickSection = -1; float kickStrength; double kickBeat = double.NaN;
    float[] secStart = new float[0]; int secStartLayouts = -1, secStartCount = -1;
    /// <summary>v9 (R): a launch's crash kicks section <paramref name="section"/>'s letter (an extra pop of <paramref name="strength"/> at this beat).</summary>
    public static void Kick(int section, float strength) { if (I == null) return; I.kickSection = section; I.kickStrength = strength; I.kickBeat = WorldMagic.FxBeat; }

    void UpdatePulses(SongManager sm)
    {
        double b = WorldMagic.FxBeat;
        bool running = sm != null && sm.HasSong && (WorldMagic.FxPlaying || b > 1e-6);
        double loopLen = GlobalClock.LoopLengthBeats;
        if (running && (secStartLayouts != layouts || secStartCount != views.Count))
        {
            secStartLayouts = layouts; secStartCount = views.Count;
            if (secStart.Length < views.Count) secStart = new float[views.Count + 4];
            for (int s = 0; s < views.Count; s++) secStart[s] = sm.SectionStartBeat(s);
        }
        for (int s = 0; s < views.Count; s++)
        {
            var v = views[s];
            if (v.letterDisc == null) continue;
            float pulse = 0f;
            if (running)
            {
                double age = b - secStart[s];
                if (age < 0.0 && loopLen > 1.0 && s == 0) age += loopLen;   // the song's wrap: the first section comes round again
                if (age >= 0.0 && age < PulseBeats)
                {
                    float a = (float)(age / PulseBeats);
                    var anchor = sm.AnchorOf(v.firstCol);
                    int e = anchor != null ? Mathf.Clamp(anchor.energy, 0, 3) : 2;
                    pulse = Mathf.Sin(a * Mathf.PI) * (1f - a) * Mathf.Lerp(PulseMin, PulseMax, e / 3f);
                }
                if (kickSection == s && !double.IsNaN(kickBeat) && System.Math.Abs(b - kickBeat) < 1.0) pulse += kickStrength * (float)(1.0 - System.Math.Abs(b - kickBeat));
            }
            if (pulse > 0.001f && v.pulse <= 0.001f) pulses++;
            if (Mathf.Abs(pulse - v.pulse) < 1e-4f) continue;
            v.pulse = pulse;
            float sc = LetterDisc * (1f + pulse);
            v.letterDisc.localScale = new Vector3(sc, 1f, sc);   // (the disc only: scaling the TMP letter would regenerate its mesh every frame)
        }
    }

    /// <summary>The playing section's playhead and lit ruler pip (a pure function of the song beat: the lit column, its pass, the beat in it).</summary>
    void UpdatePlay()
    {
        UpdatePulses(SongManager.I);
        var sm = SongManager.I;
        int sec = -1, pip = -1; float x = 0f, y = 0f;
        if (sm != null && sm.HasSong && GlobalClock.IsPlaying && views.Count > 0)
        {
            float beat = GlobalClock.SongBeat;
            int c = sm.ActiveColumn(beat);
            int s = c >= 0 ? sm.SectionOf(c) : -1;
            if (s >= 0 && s < views.Count && views[s].root != null && views[s].root.gameObject.activeSelf)
            {
                var v = views[s];
                float pass = Mathf.Max(1e-3f, sm.PassLength(c)), start = sm.ColumnStart(c);
                float inPass = Mathf.Repeat(beat - start, pass);
                int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
                // the column's first measure block in this plinth
                int m0 = -1; for (int i = 0; i < v.blocks.Count; i++) if (!v.blocks[i].ghost && v.blocks[i].col == c && v.blocks[i].t.gameObject.activeSelf) { m0 = i; break; }
                if (m0 >= 0)
                {
                    int bars = Mathf.Max(1, sm.ColumnBars(c));
                    int m = Mathf.Clamp(Mathf.FloorToInt(inPass / bpb), 0, bars - 1);
                    var blk = v.blocks[Mathf.Min(m0 + m, v.blocks.Count - 1)];
                    int k = Mathf.Clamp(Mathf.FloorToInt(inPass - m * bpb), 0, bpb - 1);
                    pip = (m0 + m) * bpb + k;
                    float colX0 = v.blocks[m0].beatX0, colX1 = v.blocks[Mathf.Min(m0 + bars - 1, v.blocks.Count - 1)].beatX1;
                    var anchor = sm.AnchorOf(c);
                    float ride = anchor != null ? anchor.VisualOffset.x : 0f;
                    x = Mathf.Lerp(colX0, colX1, inPass / pass) + ride;
                    y = blk.t.position.y + 0.02f;
                    sec = s;
                }
            }
        }
        // the lit ruler pip
        if (sec != playSection || pip != litPip)
        {
            SetRulerPip(playSection, litPip, false);
            SetRulerPip(sec, pip, true);
        }
        // the playhead line
        for (int s = 0; s < views.Count; s++)
        {
            var v = views[s]; if (v.playhead == null) continue;
            bool on = s == sec;
            if (v.playhead.gameObject.activeSelf != on) v.playhead.gameObject.SetActive(on);
            if (v.playTick != null && v.playTick.gameObject.activeSelf != on) v.playTick.gameObject.SetActive(on);
            if (!on) continue;
            v.playhead.position = new Vector3(x, y, (v.zFront + v.zBack) * 0.5f);
            v.playhead.localScale = new Vector3(0.34f, 1f, Mathf.Max(0.2f, v.zBack - v.zFront - 0.24f));
            if (v.playTick != null)
            {
                v.playTick.position = new Vector3(x, y - 0.02f - Thick, v.zFront - 0.015f);
                v.playTick.localScale = new Vector3(0.3f, Thick + 0.02f, 1f);
            }
        }
        playSection = sec; litPip = pip; playX = x; playY = y;
    }

    void SetRulerPip(int s, int pip, bool lit)
    {
        if (s < 0 || s >= views.Count || pip < 0) return;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar), m = pip / bpb, k = pip % bpb;
        var v = views[s];
        if (m >= v.blocks.Count) return;
        var b = v.blocks[m];
        if (k >= b.pips.Count || b.pips[k] == null) return;
        var r = b.pips[k];
        r.sharedMaterial = lit ? StructureLook.PipLitMaterial : (b.ghost ? StructureLook.PipGhostMaterial : (k == 0 ? StructureLook.PipDownMaterial : StructureLook.PipRestMaterial));
        float s0 = k == 0 ? PipDownSize : PipSize, f = lit ? 1.3f : 1f;
        r.transform.localScale = new Vector3(s0 * f, 1f, s0 * f);
        if (v.halo != null)
        {
            if (v.halo.gameObject.activeSelf != lit) v.halo.gameObject.SetActive(lit);
            if (lit) { v.halo.position = r.transform.position - new Vector3(0f, 0.004f, 0f); v.halo.localScale = new Vector3(s0 * 2.6f, 1f, s0 * 2.6f); }
        }
    }

    /// <summary>Tests: whether ruler pip <paramref name="pip"/> (measure × BeatsPerBar + beat) of plinth <paramref name="s"/> shows lit.</summary>
    public static bool RulerPipLit(int s, int pip)
    {
        if (I == null || s < 0 || s >= I.views.Count || pip < 0) return false;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar), m = pip / bpb, k = pip % bpb;
        var v = I.views[s];
        if (m >= v.blocks.Count || k >= v.blocks[m].pips.Count || v.blocks[m].pips[k] == null) return false;
        return v.blocks[m].pips[k].sharedMaterial == StructureLook.PipLitMaterial;
    }
}
