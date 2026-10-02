using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v9 (W; the user, 2026-10-01: "scrap the current falling play mode, just make it record of the cube path as we have now, with camera slowly
/// falling and islands appearing from the watter as the measures pass (with water animations)"): the present mode's ISLANDS. On enter every island
/// and Moon is under the sea (KeyBlock.SeaDepth = 1: its cubes, hub, belt and glyphs go with it) and so is every section floor block
/// (SectionPlinth.ColumnSinks). Each column comes up just before it plays: its islands burst out of the sea one lane after another on sixteenths,
/// the last lane <see cref="LeadBeats"/> before the column's downbeat; then its floor rises under them (an eighth before the downbeat: always
/// below its islands, never through one) and the islands dip a touch on the downbeat as they settle. Everything stays up. A Moon rises with its
/// start column (after the islands' lanes), a long grid's islands with its first column, a belt with its island.
/// Keyboards KeyStage drives (parts with a gap) are KeyStage's: while the song plays this leaves them alone (it pops them in for their parts and
/// sinks them after); only where KeyStage would hold them up — the pre-roll, a pause, the moment play starts or resumes (its ease) — this holds
/// them at the pose KeyStage gives while playing (the deeper of the two pose inputs wins). Their own water effects (KeyBlock's, drawn at their
/// floor) stay off while this holds them or while their column's floor is still under the sea.
/// The depth of everything is a PURE FUNCTION of the song beat (pause, seek, replay and the loop land exactly): a rise is OutCubic over a window
/// (fast as it breaks the surface, slowing into place), placed so the riser's top crosses the sea exactly at its breach beat; PresentWater draws
/// the splash from the same numbers. Per frame: no allocation.
/// </summary>
public sealed class PresentSea
{
    // ---------------------------------------------------------------- tuning (beats unless noted; static so a look pass can adjust them live)
    /// <summary>The last lane of a column breaks the surface this long before the column's downbeat.</summary>
    public static float LeadBeats = 1f;
    /// <summary>Between two lanes of a column (a sixteenth), at most <see cref="SpreadMaxBeats"/> over the whole column.</summary>
    public static float StaggerBeats = 0.25f, SpreadMaxBeats = 0.75f;
    /// <summary>A column's floor (its section plinth's measure blocks) breaks the surface this long before the downbeat.</summary>
    public static float FloorLeadBeats = 0.5f;
    /// <summary>A whole rise (fully under → in place) in beats, clamped to [RiseMinSeconds, RiseMaxSeconds].</summary>
    public static float RiseBeats = 1f, RiseMinSeconds = 0.38f, RiseMaxSeconds = 0.85f;
    /// <summary>The settle: a dip of BobDepth (× the island's KeyBlock.HideDepth: ≈ 0.15–0.2 u) BobBeats long, peaking BobDelay after the column's downbeat.</summary>
    public static float BobDepth = 0.025f, BobBeats = 0.75f, BobDelay = 0.12f;
    /// <summary>Fx's sea plane (world y).</summary>
    public const float SeaY = -4f;

    public enum Kind { Island, Moon, Floor }

    /// <summary>One thing that rises on the schedule: an island, a Moon or a column's floor.</summary>
    public struct Riser
    {
        public Kind kind;
        public KeyBlock kb;                 // the island / Moon (null for a floor)
        public int col, lane;               // the column it rises with (a long grid's: its first), its lane in that column
        public double downbeat;             // the downbeat of its column (song beats)
        public double breach, start;        // its top crosses the sea / the rise starts (song beats)
        public float window;                // the rise (beats)
        public float breachDepth;           // the depth (0..1) at which its top crosses the sea
        public float hide;                  // how far under its place it is at depth 1 (world units)
        public float top;                   // its top at rest (world y)
        public Vector3 centre;              // footprint centre at rest (world; y = its top)
        public Vector2 size;                // footprint (x, z)
        public Color color;
        public Vector3 restPos;             // transform position at rest (exit checks)
        public double Arrive => start + window;
    }

    /// <summary>A keyboard KeyStage drives.</summary>
    public struct Staged { public KeyBlock kb; public bool waterWas; public Vector3 restPos; }

    readonly List<Riser> risers = new List<Riser>();
    readonly List<Staged> staged = new List<Staged>();
    float[] colSink = new float[0], lastDepth = new float[0];
    double total = 1.0, bps = 2.0;
    int colCount;

    public int Count => risers.Count;
    public Riser this[int i] => risers[i];
    public int StagedCount => staged.Count;
    public Staged StagedAt(int i) => staged[i];
    public double TotalBeats => total;
    public int Columns => colCount;
    /// <summary>The section floor sinks by column this frame (world units; SectionPlinth reads it).</summary>
    public float[] ColumnSinks => colSink;

    /// <summary>Reads the song: one riser per island (lane order inside its column), per Moon and per column floor, every breach on the grid; the
    /// keyboards KeyStage drives aside.</summary>
    public bool Build(SongManager sm)
    {
        risers.Clear(); staged.Clear();
        if (sm == null || !sm.HasSong) return false;
        colCount = sm.ColumnCount;
        if (colCount == 0) return false;
        total = Math.Max(1.0, sm.TotalBeats);
        bps = Math.Max(0.1, GlobalClock.BeatsPerSecond);
        if (colSink.Length != colCount) colSink = new float[colCount];
        float win = Mathf.Clamp(RiseBeats, RiseMinSeconds * (float)bps, RiseMaxSeconds * (float)bps);
        var lanes = new int[colCount];
        var laneOf = new Dictionary<KeyBlock, int>();
        // islands: column order, anchor first; a long grid's later members ride with its first column (their own lane there)
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb == null) continue;
            if (KeyStage.Drives(kb)) { staged.Add(new Staged { kb = kb, waterWas = kb.SeaWater, restPos = kb.transform.position }); continue; }
            int col = Mathf.Clamp(kb.column, 0, colCount - 1);
            var run = sm.LongGridOf(kb);
            int lane;
            if (run != null && run.Count > 0 && run[0] != null && run[0] != kb && laneOf.TryGetValue(run[0], out lane)) col = Mathf.Clamp(run[0].column, 0, colCount - 1);
            else { lane = lanes[col]++; laneOf[kb] = lane; }
            risers.Add(Make(kb, Kind.Island, col, lane, sm));
        }
        // Moons: after the islands of their start column
        for (int j = 0; j < sm.Moons.Count; j++)
        {
            var m = sm.Moons[j];
            if (m == null) continue;
            int col = Mathf.Clamp(sm.MoonStartColumn(j), 0, colCount - 1);
            risers.Add(Make(m, Kind.Moon, col, lanes[col]++, sm));
        }
        // the schedule: lanes on sixteenths, the last one LeadBeats before the downbeat
        for (int i = 0; i < risers.Count; i++)
        {
            var r = risers[i];
            int n = Mathf.Max(1, lanes[r.col]);
            float st = n > 1 ? Mathf.Min(StaggerBeats, SpreadMaxBeats / (n - 1)) : 0f;
            r.breach = r.downbeat - LeadBeats - (n - 1 - r.lane) * st;
            Place(ref r, win);
            risers[i] = r;
        }
        // the floors: one per column, under the islands (they start rising after every lane of it started: never through an island)
        for (int c = 0; c < colCount; c++)
        {
            Vector3 centre; Vector2 size; float top;
            if (!FloorFootprint(c, out centre, out size, out top)) continue;
            var f = new Riser { kind = Kind.Floor, col = c, lane = lanes[c], downbeat = sm.ColumnStart(c), top = top, centre = centre, size = size, color = SectionPlinth.Stone };
            f.hide = Mathf.Max(KeyBlock.SeaHide, top - SeaY + FloorClear);   // a floor on a raised ground (a climbing stair) goes deeper
            for (int k = 0; k < sm.Islands.Count; k++) { var kb = sm.Islands[k]; if (kb != null && Mathf.Clamp(kb.column, 0, colCount - 1) == c) f.hide = Mathf.Max(f.hide, IslandHide(kb) + 0.2f); }   // and never above an island of its own
            f.breachDepth = BreachDepth(top, f.hide);
            f.breach = f.downbeat - FloorLeadBeats;
            Place(ref f, win);
            double latest = double.NegativeInfinity;
            foreach (var r in risers) if (r.col == c && r.kind != Kind.Floor) latest = Math.Max(latest, r.start);
            if (f.start < latest + 0.05) { double shift = latest + 0.05 - f.start; f.start += shift; f.breach += shift; }
            risers.Add(f);
        }
        if (lastDepth.Length != risers.Count) lastDepth = new float[risers.Count];
        for (int i = 0; i < lastDepth.Length; i++) lastDepth[i] = -1f;
        return true;
    }

    Riser Make(KeyBlock kb, Kind kind, int col, int lane, SongManager sm)
    {
        var r = new Riser { kind = kind, kb = kb, col = col, lane = lane, downbeat = sm.ColumnStart(col), color = kb.chordColor, restPos = kb.transform.position };
        var b = kb.IsMoon ? kb.WorldBounds : kb.FootprintBounds;
        float top = RestTop(kb);
        r.top = top;
        r.centre = new Vector3(b.center.x, top, b.center.z);
        r.size = new Vector2(Mathf.Max(0.5f, b.size.x), Mathf.Max(0.5f, b.size.z));
        r.hide = IslandHide(kb);
        r.breachDepth = BreachDepth(top, r.hide);
        return r;
    }

    /// <summary>The island's top at rest: its renderers' highest point with no sea drop (measured as it stands now, plus any drop it shows).</summary>
    static float RestTop(KeyBlock kb)
    {
        float drop = kb.SeaDrop;
        float top = float.NegativeInfinity;
        var rs = kb.GetComponentsInChildren<Renderer>(false);
        foreach (var rr in rs) { if (rr == null || !rr.enabled) continue; if (rr is ParticleSystemRenderer || rr is LineRenderer || rr is TrailRenderer) continue; top = Mathf.Max(top, rr.bounds.max.y); }
        float y = kb.transform.position.y + drop;
        if (float.IsNegativeInfinity(top)) return y + ProjectConfig.TileThickness;
        return Mathf.Clamp(top + drop, y, y + 6f);
    }

    static float BreachDepth(float top, float hide) => Mathf.Clamp((top - SeaY) / Mathf.Max(0.1f, hide), 0.05f, 0.95f);

    /// <summary>A floor's top this far under the water at depth 1 (world units).</summary>
    public const float FloorClear = 0.8f;
    /// <summary>How far island <paramref name="kb"/> goes under its place at SeaDepth 1 (KeyBlock.HideDepth: deep enough for its tallest tile and a cube
    /// on it, on its ground).</summary>
    public static float IslandHide(KeyBlock kb) => kb.HideDepth;

    /// <summary>The rise window and its start, so OutCubic carries the top across the sea exactly at the breach beat.</summary>
    static void Place(ref Riser r, float win)
    {
        r.window = win;
        float ub = 1f - Mathf.Pow(r.breachDepth, 1f / 3f);            // 1 - OutCubic(ub) = breachDepth
        r.start = r.breach - ub * win;
    }

    /// <summary>Column <paramref name="c"/>'s floor: the union of its measure blocks on its section's plinth (rest pose), false when it has none.</summary>
    static bool FloorFootprint(int c, out Vector3 centre, out Vector2 size, out float top)
    {
        centre = Vector3.zero; size = Vector2.zero; top = 0f;
        int n = SectionPlinth.Count;
        bool any = false; float x0 = 0f, x1 = 0f, z0 = 0f, z1 = 0f;
        for (int s = 0; s < n; s++)
        {
            var box = SectionPlinth.Bounds(s);
            int shown = SectionPlinth.MeasuresShown(s) + SectionPlinth.GhostsShown(s);
            for (int m = 0; m < shown; m++)
            {
                float a, b; bool ghost; int col;
                if (!SectionPlinth.MeasureSpan(s, m, out a, out b, out ghost, out col)) break;
                if (col != c) continue;
                float t = SectionPlinth.TopOf(s, m) + SectionPlinth.SinkOf(c);
                if (!any) { x0 = a; x1 = b; z0 = box.min.z; z1 = box.max.z; top = t; any = true; }
                else { x0 = Mathf.Min(x0, a); x1 = Mathf.Max(x1, b); z0 = Mathf.Min(z0, box.min.z); z1 = Mathf.Max(z1, box.max.z); top = Mathf.Max(top, t); }
            }
        }
        if (!any) return false;
        centre = new Vector3((x0 + x1) * 0.5f, top, (z0 + z1) * 0.5f);
        size = new Vector2(Mathf.Max(0.5f, x1 - x0), Mathf.Max(0.5f, z1 - z0));
        return true;
    }

    // ---------------------------------------------------------------- the pure pose
    /// <summary>Riser <paramref name="i"/>'s depth (0 in place .. 1 under) at song beat <paramref name="b"/>: the rise plus the settle dip.</summary>
    public float DepthAt(int i, double b)
    {
        var r = risers[i];
        float d = RiseDepth(r, b);
        if (r.kind != Kind.Floor) d += Bob(r, b);
        return Mathf.Clamp01(d);
    }

    /// <summary>The rise alone: 1 before its start, OutCubic up to 0 at its arrival.</summary>
    public static float RiseDepth(in Riser r, double b)
    {
        double u = (b - r.start) / Math.Max(1e-4, r.window);
        if (u <= 0.0) return 1f;
        if (u >= 1.0) return 0f;
        float v = 1f - (float)u;
        return v * v * v;
    }

    static float Bob(in Riser r, double b)
    {
        double x = (b - (r.downbeat + BobDelay)) / Math.Max(0.05, BobBeats);
        if (x <= -0.5 || x >= 0.5) return 0f;
        float c = Mathf.Cos((float)(Math.PI * x));
        return BobDepth * c * c;
    }

    /// <summary>The pose KeyStage gives keyboard <paramref name="kb"/> at song beat <paramref name="b"/> while playing (before the song's start: under,
    /// but for the pop of a part that starts at the top), clamped to 0..1.</summary>
    public float StagedDepthAt(KeyBlock kb, double b)
    {
        if (b >= 0.0) return Mathf.Clamp01(KeyStage.DepthAt(kb, b));
        var parts = KeyStage.PartsOf(kb);
        if (parts == null || parts.Count == 0) return 1f;
        double s0 = parts[0].x;
        double popB = KeyStage.PopS * bps, popEnd = s0 - KeyStage.LeadBeats, popStart = popEnd - popB;
        if (b < popStart) return 1f;
        if (b >= popEnd) return 0f;
        return Mathf.Clamp01(1f - Ease.OutBack((float)((b - popStart) / popB), KeyBlock.PopOvershoot));
    }

    /// <summary>The beat inside the song for a presentation beat (the preroll is negative; a looping song wraps).</summary>
    public double Local(double B, bool loops) => B < 0.0 || !loops ? B : B - Math.Floor(B / total) * total;

    /// <summary>Poses every island, Moon and floor for song beat <paramref name="b"/> (call every frame while presenting). <paramref name="keyStageLive"/>:
    /// KeyStage shows the song's pose now (playing and settled), so its keyboards are left to it. Returns how many islands crossed half depth this
    /// frame (KeyBlock turns their colliders on / off there).</summary>
    public int Apply(double b, bool keyStageLive)
    {
        int crossings = 0;
        for (int c = 0; c < colSink.Length; c++) colSink[c] = 0f;
        for (int i = 0; i < risers.Count; i++)
        {
            var r = risers[i];
            float d = DepthAt(i, b);
            if ((lastDepth[i] < 0.5f) != (d < 0.5f) && lastDepth[i] >= 0f) crossings++;
            lastDepth[i] = d;
            if (r.kind == Kind.Floor) { if (r.col >= 0 && r.col < colSink.Length) colSink[r.col] = r.hide * d; continue; }
            if (r.kb != null && Mathf.Abs(r.kb.SeaDepth - d) > 1e-5f) r.kb.SeaDepth = d;
        }
        for (int i = 0; i < staged.Count; i++)
        {
            var s = staged[i];
            if (s.kb == null) continue;
            float d = keyStageLive ? 0f : StagedDepthAt(s.kb, b);
            // its own water (KeyBlock's, at its floor's height) only once its column's floor is up: a run KeyStage pops early comes out of the
            // open sea (KeyStage splashes there), not through a floor that is still under it
            int sc = Mathf.Clamp(s.kb.column, 0, Mathf.Max(0, colSink.Length - 1));
            bool water = keyStageLive && s.waterWas && (colSink.Length == 0 || colSink[sc] < 0.05f);
            if (s.kb.SeaWater != water) s.kb.SeaWater = water;
            if ((s.kb.SeaDepth < 0.5f) != (d < 0.5f)) crossings++;
            if (Mathf.Abs(s.kb.SeaDepth - d) > 1e-5f) s.kb.SeaDepth = d;
        }
        SectionPlinth.ColumnSinks = colSink;
        return crossings;
    }

    /// <summary>Everything back in its place at once (exit): SurfaceNow on every island and Moon (a keyboard KeyStage drives: only its SeaDepth and
    /// its water switch), the floors' sinks cleared.</summary>
    public void SurfaceAll()
    {
        for (int i = 0; i < risers.Count; i++)
        {
            var r = risers[i];
            if (r.kind == Kind.Floor || r.kb == null) continue;
            r.kb.SurfaceNow();
        }
        for (int i = 0; i < staged.Count; i++) { var s = staged[i]; if (s.kb == null) continue; s.kb.SeaDepth = 0f; s.kb.SeaWater = s.waterWas; }
        for (int c = 0; c < colSink.Length; c++) colSink[c] = 0f;
        SectionPlinth.ColumnSinks = null;
    }

    /// <summary>True while every island and Moon of the build still exists (a rebuild during the presentation ends it).</summary>
    public bool StillValid(SongManager sm)
    {
        if (sm == null || !sm.HasSong || sm.ColumnCount != colCount) return false;
        for (int i = 0; i < risers.Count; i++) if (risers[i].kind != Kind.Floor && risers[i].kb == null) return false;
        for (int i = 0; i < staged.Count; i++) if (staged[i].kb == null) return false;
        return true;
    }

    // ---------------------------------------------------------------- queries (tests, the camera, the water)
    /// <summary>The largest depth any scheduled island or Moon of column <paramref name="c"/> has at beat <paramref name="b"/> (the rise alone).</summary>
    public float ColumnDepth(int c, double b)
    {
        float d = 0f;
        for (int i = 0; i < risers.Count; i++) if (risers[i].col == c && risers[i].kind != Kind.Floor) d = Mathf.Max(d, RiseDepth(risers[i], b));
        return d;
    }

    /// <summary>The smallest depth of the scheduled islands and Moons of column <paramref name="c"/> at beat <paramref name="b"/> (1 when it has none: its
    /// keyboards are all KeyStage's).</summary>
    public float ColumnMinDepth(int c, double b)
    {
        float d = 1f; bool any = false;
        for (int i = 0; i < risers.Count; i++) if (risers[i].col == c && risers[i].kind != Kind.Floor) { d = Mathf.Min(d, RiseDepth(risers[i], b)); any = true; }
        return any ? d : 1f;
    }

    /// <summary>The earliest rise start of all (song beats): the presentation's pre-roll starts before it.</summary>
    public double FirstStart { get { double s = 0.0; foreach (var r in risers) s = Math.Min(s, r.start); return s; } }
}
