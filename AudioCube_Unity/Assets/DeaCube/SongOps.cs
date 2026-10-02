using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// v7 (SPEC v7 §2.4, §13, §14, §16, §17.1–17.2; package O): the song's STRUCTURAL OPS. Each op edits a SongState snapshot seen as COLUMNS and
/// SECTIONS (the <see cref="Plan"/>: measures, columns and section starts added / removed / moved), writes it back column-major (contiguous
/// columns, cubes remapped, merge groups kept inside their sections, st.sections set, each section's name on its first island) and rebuilds with
/// SongManager's seek-keeping rebuild (the lit column keeps its local beat while playing; islands slide to their new places), then pushes ONE
/// History entry — except <see cref="GrowPhrase"/>, which is part of the drawing stroke (the stroke pushes once). The selection follows its grids.
/// - STAIRS ("a stair-case grid that just is descending notes"): AddStairIsland / InsertStairColumn build a kind 3 island — its type the one
///   Harmony.StairFit stars for the chord (a SPARK fall on a dominant: the recording's ♭9 run into the next chord) — with ONE runner cube (the
///   lead group unless told) whose path is the run (<see cref="StairPathOf"/> = SongManager.StairPath on the state); SetStair changes the run
///   and re-derives every runner on the island in the same snapshot. The grounds follow in K's rebuild ("grids should abide by the height").
/// - PHRASES ("a 2 measure melody shouldnt be two separate grids", "let it autoexpand as they reach the limit"): AddPhrase, SetPhraseLength,
///   GrowPhrase (½ → 1 → 2 → 4 → 8 measures, §17.2; past its section the section takes the next measures, past the song's end measures with
///   MusicTheory.SuggestNext's chords are appended to the last section), DoublePhrase (the notes again in the new half), MovePhrase,
///   SetPhraseGrid. Notes are re-encoded with D's PhraseRoll (notes in time order, rests for the gaps and up to the phrase's end).
/// - SECTIONS ("4 beats in a measure, 4 measures in a section"): split / join / duplicate (the copy keeps the name; its letter follows from its
///   chords) / delete / move, SetSectionRole (§17.1: intro, verse, chorus, bridge, drop, outro).
/// - MANY GRIDS ("hold shift and drag over a section to then select all the grids in that section and like paste or delete or other options",
///   "or transpose up an octave or down an octave"): the GRID CLIPBOARD (copy; paste as new columns that join sections by the landing rule),
///   delete, transpose (register ± octaves), duplicate, repeat, flow through the selection, launch.
/// </summary>
public static class SongOps
{
    static SongManager SM => SongManager.I;
    /// <summary>The group a runner / a new phrase's hand gets when none is given (−1): the lead.</summary>
    public const int LeadGroup = 3;
    static int Pitched(int instrument) => instrument >= 0 && instrument < Instruments.Count && !Instruments.IsDrums(instrument) ? instrument : LeadGroup;
    static int Bpb => Mathf.Max(1, GlobalClock.BeatsPerBar);
    static int TPB => ProjectConfig.TicksPerBeat;
    /// <summary>A phrase starts on half measures (SPEC v7 §14.1): its offset snaps to this many ticks.</summary>
    public static int HalfMeasureTicks => Mathf.Max(1, Bpb * TPB / 2);

    /// <summary>True while an op of this class rebuilds the song (GridSelection leaves that rebuild alone: the op re-selects its grids).</summary>
    public static bool Busy { get; private set; }
    /// <summary>The phrase island after the last phrase op (AddPhrase / SetPhraseLength / GrowPhrase / DoublePhrase / MovePhrase / SetPhraseGrid all
    /// rebuild: the KeyBlock passed in is destroyed). D re-acquires the phrase here after growing it mid-stroke.</summary>
    public static KeyBlock GrownPhrase { get; private set; }
    /// <summary>Fires after a phrase's length changed: the rebuilt phrase and its old length in beats (the roll's grow animation).</summary>
    public static event Action<KeyBlock, int> OnPhraseResized;
    /// <summary>Fires after the grid clipboard changed (a copy, a clear).</summary>
    public static event Action OnGridClipChanged;
    /// <summary>Ops that changed the song (tests); a refused op does not count.</summary>
    public static int OpCount { get; private set; }

    // ================================================================== the column plan
    /// <summary>
    /// A snapshot seen as COLUMNS (lists of measure indices, the anchor first, phrases last) with a SECTION-start flag, an origin (the old column a
    /// column continues, −1 for a new one) and a section NAME carrier (§17.1: the role an old section's first column brought) per column. Ops add /
    /// remove / move measures and columns and edit the flags; <see cref="Commit"/> writes the snapshot back.
    /// </summary>
    sealed class Plan
    {
        public readonly SongState st;
        public readonly int nOld;
        public readonly List<MeasureState> ms = new List<MeasureState>();
        public readonly List<List<int>> cols = new List<List<int>>();
        public readonly List<bool> start = new List<bool>();
        public readonly List<int> origin = new List<int>();
        public readonly List<int> role = new List<int>();
        public readonly List<int> oldStarts;
        public readonly List<CubeState> cubes = new List<CubeState>();       // island cubes (measure = a plan index)
        public readonly List<CubeState> moonCubes = new List<CubeState>();   // Moon cubes (moon = an index in st.moons)
        public readonly List<KeyValuePair<MeasureState, int>> moonAt = new List<KeyValuePair<MeasureState, int>>();   // a new Moon + a plan measure of its start column
        public readonly List<int> appended = new List<int>();               // measures appended at the song's end (a phrase grew past it)
        public int[] final;                                                  // after Commit: plan index → st.measures index (−1 removed)

        public Plan(SongState s)
        {
            st = s;
            if (st.measures == null) st.measures = new MeasureState[0];
            if (st.cubes == null) st.cubes = new CubeState[0];
            nOld = st.measures.Length;
            ms.AddRange(st.measures);
            int prev = int.MinValue;
            for (int i = 0; i < nOld; i++)
            {
                if (cols.Count == 0 || ms[i].col != prev) { cols.Add(new List<int>()); prev = ms[i].col; }
                cols[cols.Count - 1].Add(i);
            }
            oldStarts = SongManager.SectionsOf(st);
            var set = new HashSet<int>(oldStarts);
            for (int c = 0; c < cols.Count; c++)
            {
                bool s0 = c == 0 || set.Contains(c);
                start.Add(s0); origin.Add(c);
                role.Add(s0 ? Mathf.Clamp(ms[cols[c][0]].secRole, 0, 6) : 0);
            }
            foreach (var c in st.cubes) { if (c == null) continue; if (c.moon >= 0) moonCubes.Add(c); else cubes.Add(c); }
        }

        /// <summary>Measures of column <paramref name="c"/> (its longest lane; a column of phrases only: its phrase's) — SongManager.SnapshotColumnBars.</summary>
        public int Bars(int c)
        {
            int b = 0; bool lane = false;
            foreach (int i in cols[c])
            {
                var m = ms[i];
                if (m.kind == 4) { if (!lane) b = Math.Max(b, Mathf.Clamp(m.bars, 1, 4)); continue; }
                if (!lane) { b = 0; lane = true; }
                b = Math.Max(b, Mathf.Clamp(m.bars, 1, 4));
            }
            return Math.Max(1, b);
        }
        /// <summary>Passes of column <paramref name="c"/>: the max repeat of its lanes (phrases never add passes).</summary>
        public int Passes(int c) { int p = 1; foreach (int i in cols[c]) if (ms[i].kind != 4) p = Math.Max(p, Mathf.Clamp(ms[i].repeat, 1, ProjectConfig.MaxRepeat)); return p; }
        /// <summary>The whole span of column <paramref name="c"/> in ticks (every pass).</summary>
        public int Ticks(int c) => Bars(c) * Bpb * TPB * Passes(c);
        public long StartTicks(int c) { long t = 0; for (int k = 0; k < c && k < cols.Count; k++) t += Ticks(k); return t; }
        public int ColumnOf(int i) { for (int c = 0; c < cols.Count; c++) if (cols[c].Contains(i)) return c; return -1; }
        public bool HasLanes(int c) { foreach (int i in cols[c]) if (ms[i].kind != 4) return true; return false; }
        /// <summary>The first lane of column <paramref name="c"/> (its anchor), else its first measure, −1 when empty.</summary>
        public int Anchor(int c) { foreach (int i in cols[c]) if (ms[i].kind != 4) return i; return cols[c].Count > 0 ? cols[c][0] : -1; }
        public List<int> SectionStarts() { var r = new List<int>(); for (int c = 0; c < cols.Count; c++) if (c == 0 || start[c]) r.Add(c); return r; }
        public int Add(MeasureState m) { ms.Add(m); return ms.Count - 1; }
        /// <summary>A new column at <paramref name="at"/> (its measures: lanes first, then phrases).</summary>
        public void InsertColumn(int at, List<int> members, bool sectionStart, int roleOf = 0)
        {
            at = Mathf.Clamp(at, 0, cols.Count);
            var sorted = members.OrderBy(i => ms[i].kind == 4 ? 1 : 0).ToList();   // stable: a phrase is never a column's anchor
            cols.Insert(at, sorted); start.Insert(at, sectionStart); origin.Insert(at, -1); role.Insert(at, roleOf);
        }
        /// <summary>Adds measure <paramref name="i"/> to column <paramref name="c"/>: a lane before the column's phrases, a phrase at the end.</summary>
        public void AddToColumn(int c, int i)
        {
            var l = cols[c];
            if (ms[i].kind == 4) { l.Add(i); return; }
            int k = 0; while (k < l.Count && ms[l[k]].kind != 4) k++;
            l.Insert(k, i);
        }
        public void Remove(int i) { foreach (var l in cols) l.Remove(i); }

        /// <summary>New columns (origin −1) join sections by K's landing rule (SongManager.RemapSections: strictly inside a section they join it; at a
        /// boundary / the front / the end the section before while it has fewer than SectionBars measures, else the one after, else a new one);
        /// continuing columns keep their sections. Call before editing the flags by hand.</summary>
        public void Land()
        {
            var live = new List<int>();
            for (int c = 0; c < cols.Count; c++) if (cols[c].Count > 0) live.Add(c);
            if (live.Count == 0) return;
            var org = new int[live.Count]; var bars = new int[live.Count];
            for (int k = 0; k < live.Count; k++) { org[k] = origin[live[k]]; bars[k] = Bars(live[k]); }
            var starts = SongManager.RemapSections(oldStarts, org, bars);
            for (int c = 0; c < cols.Count; c++) start[c] = false;
            foreach (int s in starts) if (s >= 0 && s < live.Count) start[live[s]] = true;
            start[live[0]] = true;
        }

        /// <summary>
        /// Writes the plan back into <see cref="st"/>: columns renumbered (emptied ones dropped — an emptied section start passes to the next column,
        /// with its name when the section goes on), measures column-major, cubes of removed measures dropped and the rest remapped, st.sections set,
        /// every section's NAME (the first named carrier among its columns, a passed-on one first) on the first island of its first column and 0 on
        /// every other island, merge groups split at section boundaries (a group lives inside its section), the new Moons' start columns resolved.
        /// Returns map[old index] = st index (−1 removed); <see cref="final"/> covers every plan index.
        /// </summary>
        public int[] Commit()
        {
            final = new int[ms.Count];
            for (int i = 0; i < final.Length; i++) final[i] = -1;
            var list = new List<MeasureState>(ms.Count);
            var secs = new List<int>(); var secOf = new List<int>(); var secRole = new List<int>(); var firstIdx = new List<int>();
            bool pend = false; int pendRole = 0, col = 0;
            for (int c = 0; c < cols.Count; c++)
            {
                if (cols[c].Count == 0) { if (start[c]) { pend = true; if (pendRole == 0) pendRole = role[c]; } continue; }
                bool own = start[c];
                if (col == 0 || own || pend)
                {
                    secs.Add(col);
                    secRole.Add(own ? 0 : (pend ? pendRole : 0));
                    firstIdx.Add(list.Count);
                }
                pend = false; pendRole = 0;
                int s = secs.Count - 1;
                if (role[c] > 0 && secRole[s] == 0) secRole[s] = role[c];
                secOf.Add(s);
                foreach (int i in cols[c]) { final[i] = list.Count; ms[i].col = col; ms[i].secRole = 0; list.Add(ms[i]); }
                col++;
            }
            for (int s = 0; s < secs.Count; s++) list[firstIdx[s]].secRole = secRole[s];
            // merge groups never span a section boundary: a later run of a group gets a new id (NormalizeColumns validates the rest)
            int maxId = 0; foreach (var m in list) maxId = Math.Max(maxId, m.group);
            var firstSec = new Dictionary<int, int>(); var renamed = new Dictionary<long, int>();
            foreach (var m in list)
            {
                if (m.group <= 0) continue;
                int sec = secOf[m.col], f;
                if (!firstSec.TryGetValue(m.group, out f)) { firstSec[m.group] = sec; continue; }
                if (f == sec) continue;
                long key = ((long)m.group << 20) | (long)sec;
                int id; if (!renamed.TryGetValue(key, out id)) { id = ++maxId; renamed[key] = id; }
                m.group = id;
            }
            var kept = new List<CubeState>(cubes.Count + moonCubes.Count);
            foreach (var c in cubes) { int f = c.measure >= 0 && c.measure < final.Length ? final[c.measure] : -1; if (f < 0) continue; c.measure = f; kept.Add(c); }
            kept.AddRange(moonCubes);
            st.measures = list.ToArray(); st.cubes = kept.ToArray(); st.sections = secs.ToArray();
            foreach (var kv in moonAt) { int f = kv.Value >= 0 && kv.Value < final.Length ? final[kv.Value] : -1; kv.Key.col = f >= 0 ? list[f].col : Mathf.Max(0, col - 1); }
            var map = new int[nOld];
            for (int i = 0; i < nOld; i++) map[i] = final[i];
            return map;
        }
    }

    /// <summary>Commits <paramref name="p"/>, keeps the song's first column (else its first surviving island) where it stands, and rebuilds keeping the
    /// lit column (<paramref name="movers"/>: old islands the op moved; <paramref name="pinPlan"/>: a plan measure that keeps its z). Returns
    /// result[old island] = new island index (−1 removed); the new measures' indices are <c>p.final</c>.</summary>
    static int[] Apply(Plan p, ICollection<int> movers = null, int pinPlan = -1)
    {
        var sm = SM;
        var map = p.Commit();
        var nm = SongManager.NormalizeColumns(p.st);   // lays the columns out (x by sections, z by lanes); the plan is column-major already
        if (nm != null)
        {
            for (int i = 0; i < map.Length; i++) map[i] = map[i] >= 0 && map[i] < nm.Length ? nm[map[i]] : -1;
            for (int i = 0; i < p.final.Length; i++) p.final[i] = p.final[i] >= 0 && p.final[i] < nm.Length ? nm[p.final[i]] : -1;
        }
        KeepX(p.st, map, movers);
        int pin = pinPlan >= 0 && pinPlan < p.final.Length ? p.final[pinPlan] : -1;
        Busy = true;
        try { return sm.RebuildKeepingLit(p.st, map, pin, movers); }
        finally { Busy = false; OpCount++; }
    }

    /// <summary>The song does not jump sideways: an island of the old first column that the op did not move keeps its x; when the op moved the whole
    /// first column elsewhere, the new first column takes its place (the song's start stays); when it removed it, the first island left keeps its x
    /// (the rest of the song stays put, like SongManager.RemoveMeasure).</summary>
    static void KeepX(SongState st, int[] map, ICollection<int> movers)
    {
        var sm = SM;
        if (sm == null || st.measures.Length == 0) return;
        int refOld = -1; bool moved0 = false;
        for (int i = 0; i < map.Length && i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb == null || kb.column != 0 || kb.IsPhrase || map[i] < 0) continue;
            if (movers != null && movers.Contains(i)) { moved0 = true; continue; }
            refOld = i; break;
        }
        float dx;
        if (refOld >= 0 && map[refOld] < st.measures.Length) dx = sm.Islands[refOld].px - st.measures[map[refOld]].px;
        else if (moved0) dx = sm.ColumnX(0) - st.measures[0].px;
        else
        {
            int best = int.MaxValue;
            for (int i = 0; i < map.Length && i < sm.Islands.Count; i++) if (map[i] >= 0 && map[i] < best && sm.Islands[i] != null && !sm.Islands[i].IsPhrase) { best = map[i]; refOld = i; }
            if (refOld < 0 || map[refOld] >= st.measures.Length) return;
            dx = sm.Islands[refOld].px - st.measures[map[refOld]].px;
        }
        if (Mathf.Abs(dx) < 1e-4f) return;
        foreach (var m in st.measures) m.px += dx;
    }

    /// <summary>A removed island's body sinks into the sea while the song is rebuilt without it (SongManager's husk).</summary>
    static void Husk(KeyBlock kb)
    {
        if (kb == null) return;
        kb.transform.SetParent(null, true);
        kb.enabled = false;
        foreach (var c in kb.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
        foreach (var t in kb.GetComponentsInChildren<TileInteraction>()) t.enabled = false;
        kb.gameObject.AddComponent<SinkingHusk>();
        Fx.SeaRipple(kb.Center, kb.chordColor, 3f);
    }

    /// <summary>A refused op: the column shakes (SongManager.Refuse), else a soft thud.</summary>
    static void Deny(int col)
    {
        var sm = SM;
        if (sm != null && col >= 0 && col < sm.ColumnCount) sm.Refuse(col);
        else AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.2f);
    }
    static void Deny(KeyBlock kb) { if (kb != null) kb.Shake(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.2f); }

    static KeyBlock IslandAt(int i) { var sm = SM; return sm != null && i >= 0 && i < sm.Islands.Count ? sm.Islands[i] : null; }
    static KeyBlock NewIsland(Plan p, int planIndex) => planIndex >= 0 && p.final != null && planIndex < p.final.Length ? IslandAt(p.final[planIndex]) : null;
    static void RiseAll(List<KeyBlock> list) { for (int k = 0; k < list.Count; k++) if (list[k] != null) list[k].RiseIn(0.08f * k); }

    // ---- the selection follows its grids
    struct SelMark { public List<int> isl, moons; public bool any; }
    static SelMark MarkSelection()
    {
        var m = new SelMark { isl = new List<int>(), moons = new List<int>() };
        var sm = SM;
        if (sm == null) return m;
        foreach (var kb in GridSelection.Selected)
        {
            int i = sm.Islands.IndexOf(kb);
            if (i >= 0) { m.isl.Add(i); continue; }
            int j = sm.Moons.IndexOf(kb);
            if (j >= 0) m.moons.Add(j);
        }
        m.any = m.isl.Count + m.moons.Count > 0;
        return m;
    }
    /// <summary>Re-selects the grids of <paramref name="mark"/> after a rebuild (<paramref name="result"/>[old island] = new island; <paramref name="moonMap"/>
    /// [old Moon] = new Moon, null = unchanged).</summary>
    static void Follow(SelMark mark, int[] result, int[] moonMap)
    {
        var sm = SM;
        if (sm == null || !mark.any) return;
        var list = new List<KeyBlock>();
        foreach (int i in mark.isl) { int f = result != null ? (i < result.Length ? result[i] : -1) : i; var kb = IslandAt(f); if (kb != null) list.Add(kb); }
        foreach (int j in mark.moons) { int f = moonMap != null ? (j < moonMap.Length ? moonMap[j] : -1) : j; if (f >= 0 && f < sm.Moons.Count && sm.Moons[f] != null) list.Add(sm.Moons[f]); }
        GridSelection.Set(list);
    }

    /// <summary>The live grids of <paramref name="grids"/> (islands in song order, then Moons), without duplicates.</summary>
    static List<KeyBlock> Clean(IList<KeyBlock> grids)
    {
        var sm = SM;
        var r = new List<KeyBlock>();
        if (sm == null || grids == null) return r;
        foreach (var g in grids) if (g != null && !r.Contains(g) && (sm.Islands.Contains(g) || sm.Moons.Contains(g))) r.Add(g);
        r.Sort((a, b) =>
        {
            int ka = a.IsMoon ? 100000 + sm.Moons.IndexOf(a) : sm.Islands.IndexOf(a), kb = b.IsMoon ? 100000 + sm.Moons.IndexOf(b) : sm.Islands.IndexOf(b);
            return ka.CompareTo(kb);
        });
        return r;
    }

    /// <summary>A stable id one past every cube id of the snapshot and of the live cubes.</summary>
    static int NextCubeId(Plan p)
    {
        int m = 0;
        foreach (var c in p.cubes) m = Math.Max(m, c.id);
        foreach (var c in p.moonCubes) m = Math.Max(m, c.id);
        foreach (var c in SequenceMaster.Cubes) if (c != null) m = Math.Max(m, c.id);
        return m + 1;
    }

    /// <summary>A new column never lands inside a glued run (a merge group): position <paramref name="at"/> moves past it (SongManager's rule).</summary>
    static int SkipGlued(int at)
    {
        var sm = SM;
        int nc = sm != null ? sm.ColumnCount : 0;
        at = Mathf.Clamp(at, 0, nc);
        while (at > 0 && at < nc && sm.Glued(at - 1)) at++;
        return at;
    }

    /// <summary>The key's tonic voiced nearest <paramref name="around"/> (a placeholder root that round-trips).</summary>
    static int TonicNear(int around)
    {
        var k = MusicTheory.KeyOfSong();
        int r = around + ((k.tonic - around) % 12 + 12) % 12;
        if (r - around > 6) r -= 12;
        return r;
    }

    // ================================================================== stairs (SPEC v7 §2.4)
    /// <summary>Adds a stairs island to island <paramref name="island"/>'s column, LaneGap above (<paramref name="above"/>, +z) or below it (the column's
    /// others make room), with the column's bars; with <paramref name="island"/> −1 or a full column, as a new column after the last (the landing rule
    /// puts it in a section). Its type is the one Harmony.StairFit stars for the chord it plays over (a spark fall on a dominant), falling, 4 steps of
    /// eighths leading into the next chord, and ONE runner cube of <paramref name="instrument"/> (−1: the lead group) so it sounds at once. One
    /// History entry; it rises from the sea. Returns its index (−1 without a song).</summary>
    public static int AddStairIsland(int island, bool above, int instrument = -1)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return -1;
        int inst = Pitched(instrument);
        var mark = MarkSelection();
        var p = new Plan(SongState.Capture());
        MeasureState ms; int col;
        if (sm.ValidIsland(island) && sm.LaneCount(sm.Islands[island].column) < ProjectConfig.MaxLanes)
        {
            col = sm.Islands[island].column;
            var src = p.ms[island];
            if (src.kind == 4) { int a = p.Anchor(col); if (a >= 0) src = p.ms[a]; }   // beside a phrase: beside its column's lanes
            ms = StairsMeasure(col, p.Bars(col));
            ms.px = src.px;
            ms.pz = SongManager.LanePz(src, ms, above);
            int idx0 = p.Add(ms);
            p.AddToColumn(col, idx0);
        }
        else
        {
            int nc = p.cols.Count;
            int a = nc > 0 ? p.Anchor(nc - 1) : -1;
            ms = StairsMeasure(nc - 1, sm.ValidIsland(island) ? Mathf.Max(1, sm.Islands[island].bars) : (a >= 0 ? p.ms[a].bars : 1));
            ms.pz = a >= 0 ? p.ms[a].pz : 0f; ms.px = a >= 0 ? p.ms[a].px : 0f;
            int idx0 = p.Add(ms);
            p.InsertColumn(nc, new List<int> { idx0 }, false);
            p.Land();
        }
        int idx = p.ms.Count - 1;
        var runner = RunnerState(ms, inst); runner.measure = idx;
        p.cubes.Add(runner);
        var result = Apply(p, null, idx);
        History.Push();
        var kb = NewIsland(p, idx);
        if (kb != null) { kb.RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); sm.RaiseStairChanged(kb); }
        Follow(mark, result, null);
        return kb != null ? sm.Islands.IndexOf(kb) : -1;
    }

    /// <summary>U1's stairs card dropped in an INSERT slot: a stairs island (+ its runner of <paramref name="instrument"/>, −1 the lead group) as a NEW
    /// column at position <paramref name="insertAt"/> (0 = the front, ColumnCount = the end; the landing rule puts it in a section), one bar, in the
    /// neighbouring anchor's lane. One History entry; it rises. Returns its index (−1 without a song).</summary>
    public static int InsertStairColumn(int insertAt, int instrument = -1)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return -1;
        var mark = MarkSelection();
        var p = new Plan(SongState.Capture());
        insertAt = SkipGlued(insertAt);
        var ms = StairsMeasure(insertAt - 1, 1);   // a stairs-only column plays over the chord before it
        int nb = p.cols.Count > 0 ? p.Anchor(Mathf.Clamp(insertAt - 1, 0, p.cols.Count - 1)) : -1;
        ms.pz = nb >= 0 ? p.ms[nb].pz : 0f; ms.px = nb >= 0 ? p.ms[nb].px : 0f;
        int idx = p.Add(ms);
        p.InsertColumn(insertAt, new List<int> { idx }, false);
        p.Land();
        var runner = RunnerState(ms, Pitched(instrument)); runner.measure = idx;
        p.cubes.Add(runner);
        var result = Apply(p, null, idx);
        History.Push();
        var kb = NewIsland(p, idx);
        if (kb != null) { kb.RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); sm.RaiseStairChanged(kb); }
        Follow(mark, result, null);
        return kb != null ? sm.Islands.IndexOf(kb) : -1;
    }

    /// <summary>
    /// Changes island <paramref name="at"/>'s run: <paramref name="type"/> 0..5 (−1 keeps), <paramref name="dir"/> −1 falls / +1 climbs (0 keeps),
    /// <paramref name="steps"/> StairMinSteps..StairMaxSteps (≤ 0 keeps), <paramref name="rate"/> ticks per step 6 / 8 / 12 / 24 (≤ 0 keeps),
    /// <paramref name="lead"/> 1 a lead-in into the next chord / 0 from the column's start (−1 keeps). Every runner on the island gets the new run
    /// (StairPath) in the same snapshot; new steps; the grounds after it follow (K's rebuild raises OnGroundsChanged). One History entry,
    /// OnStairChanged. Nothing changes → nothing happens.
    /// </summary>
    public static void SetStair(int at, int type, int dir, int steps, int rate, int lead)
    {
        var sm = SM;
        if (sm == null || !sm.ValidIsland(at) || !sm.Islands[at].IsStairs) return;
        var p = new Plan(SongState.Capture());
        var m = p.ms[at];
        int t = type < 0 ? m.stairType : Mathf.Clamp(type, 0, 5);
        int d = dir == 0 ? (m.stairDir > 0 ? 1 : -1) : (dir > 0 ? 1 : -1);
        int s = steps <= 0 ? m.stairSteps : Mathf.Clamp(steps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        int r = rate <= 0 ? m.stairRate : MeasureState.StairRate(rate);
        bool l = lead < 0 ? m.stairLead : lead != 0;
        if (t == m.stairType && d == m.stairDir && s == m.stairSteps && r == m.stairRate && l == m.stairLead) return;
        var mark = MarkSelection();
        m.stairType = t; m.stairDir = d; m.stairSteps = s; m.stairRate = r; m.stairLead = l;
        RederiveRunners(p.cubes, m, at);
        var result = Apply(p, null, at);
        History.Push();
        var kb = IslandAt(result != null && at < result.Length ? result[at] : at);
        if (kb != null) sm.RaiseStairChanged(kb);
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.1f);
        Follow(mark, result, null);
    }

    /// <summary>Re-derives the path of every runner of every stairs island of <paramref name="st"/> from its island's run (for an op that changed a
    /// column's bars: K may call it before its rebuild). Returns how many runners changed.</summary>
    public static int RefreshRunners(SongState st)
    {
        if (st == null || st.measures == null || st.cubes == null) return 0;
        int n = 0;
        for (int i = 0; i < st.measures.Length; i++)
        {
            var m = st.measures[i];
            if (m == null || m.kind != 3) continue;
            n += RederiveRunners(st.cubes, m, i);
        }
        return n;
    }

    static int RederiveRunners(IList<CubeState> cubes, MeasureState m, int measure)
    {
        int[] xs, mods, durs;
        StairPathOf(m.stairSteps, m.stairRate, m.bars, m.stairLead, out xs, out mods, out durs);
        int n = 0;
        foreach (var c in cubes)
        {
            if (c == null || c.moon >= 0 || c.measure != measure) continue;
            bool same = c.xs != null && c.durs != null && c.mods != null && c.xs.SequenceEqual(xs) && c.durs.SequenceEqual(durs) && c.mods.SequenceEqual(mods);
            if (same) continue;
            c.xs = (int[])xs.Clone(); c.zs = new int[xs.Length]; c.mods = (int[])mods.Clone(); c.durs = (int[])durs.Clone();
            c.rests = new bool[xs.Length]; for (int k = 0; k < xs.Length; k++) c.rests[k] = mods[k] == 1;
            c.bend = null;
            n++;
        }
        return n;
    }

    /// <summary>
    /// The runner's path of a stairs run (the same rule as SongManager.StairPath, from the run's fields): exactly one pass (<paramref name="bars"/>
    /// measures) long. Lead-in: a rest on the first step that plays for (pass − steps × rate) ticks (rests of at most 192 ticks; steps that do not
    /// fit are dropped from the front), then every step at the rate — the run ENDS at the column's end, leading into the next chord. From the start:
    /// every step at the rate, the last one holding (ties) to the pass end.
    /// </summary>
    public static void StairPathOf(int steps, int rate, int bars, bool lead, out int[] xs, out int[] mods, out int[] durs)
    {
        var X = new List<int>(); var M = new List<int>(); var D = new List<int>();
        int n = Mathf.Clamp(steps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        int r = MeasureState.StairRate(rate);
        int pass = Mathf.Max(r, Mathf.RoundToInt(Mathf.Max(1, bars) * GlobalClock.BeatsPerBar * ProjectConfig.TicksPerBeat));
        int fit = Mathf.Clamp(pass / r, 1, n);
        if (lead)
        {
            int first = n - fit, rest = pass - fit * r;
            while (rest > 0) { int chunk = Mathf.Min(rest, 192); if (chunk < 3) { if (D.Count > 0) D[D.Count - 1] += chunk; break; } X.Add(first); M.Add(1); D.Add(chunk); rest -= chunk; }
            for (int i = first; i < n; i++) { X.Add(i); M.Add(0); D.Add(r); }
        }
        else
        {
            for (int i = 0; i < fit; i++) { X.Add(i); M.Add(0); D.Add(r); }
            int left = pass - fit * r;
            while (left > 0 && D.Count > 0)
            {
                int room = 192 - D[D.Count - 1];
                if (room >= left) { D[D.Count - 1] += left; left = 0; }
                else { D[D.Count - 1] = 192; left -= room; if (left > 0) { X.Add(fit - 1); M.Add(7); D.Add(Mathf.Min(192, Mathf.Max(3, left))); left -= D[D.Count - 1]; } }
            }
        }
        xs = X.ToArray(); mods = M.ToArray(); durs = D.ToArray();
    }

    /// <summary>A stairs island's state: the placeholder chord "Stairs" {0} (the key's tonic as its root), <paramref name="bars"/> bars, the type
    /// the chord of column <paramref name="chordCol"/> stars, falling 4 steps of eighths, a lead-in.</summary>
    static MeasureState StairsMeasure(int chordCol, int bars)
    {
        return new MeasureState
        {
            chordKey = "Stairs", root = TonicNear(60), semis = new[] { 0 }, bars = Mathf.Clamp(bars, 1, 4), kind = 3, placed = true, energy = 2, repeat = 1,
            stairType = BestStairType(chordCol), stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true, col = Mathf.Max(0, chordCol)
        };
    }

    /// <summary>The stairs type Harmony.StairFit rates best over the chord column <paramref name="col"/> plays (SongManager.ChordOfColumn; none: the
    /// key's home chord) — the header's star: spark on a dominant (the ♭9 fall), bright on home / heart chords, else chord.</summary>
    public static int BestStairType(int col)
    {
        var sm = SM;
        var ch = sm != null && col >= 0 ? sm.ChordOfColumn(col) : null;
        IList<int> semis; int job;
        if (ch != null) { semis = Harmony.SemisOf(ch); job = Harmony.Job(ch); }
        else { var home = MusicTheory.Diatonic(MusicTheory.KeyOfSong(), 0); semis = home.semitones; job = 1; }
        int best = 0; float bf = -1f;
        for (int t = 0; t < 6; t++) { float f = Harmony.StairFit(t, semis, job); if (f > bf + 1e-4f) { bf = f; best = t; } }
        return best;
    }

    /// <summary>A runner of <paramref name="instrument"/> for the run of <paramref name="ms"/> (the group's brush voice; mode loop; measure set by the caller).</summary>
    static CubeState RunnerState(MeasureState ms, int instrument)
    {
        int[] xs, mods, durs;
        StairPathOf(ms.stairSteps, ms.stairRate, ms.bars, ms.stairLead, out xs, out mods, out durs);
        var rests = new bool[xs.Length];
        for (int i = 0; i < xs.Length; i++) rests[i] = mods[i] == 1;
        return new CubeState
        {
            instrument = instrument, measure = -1, moon = -1, xs = xs, zs = new int[xs.Length], rests = rests, mods = mods, durs = durs,
            step = (int)StepLen.Eighth, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1, echoOf = -1, voice = Instruments.BrushVoiceOf(instrument)
        };
    }

    // ================================================================== phrases (SPEC v7 §14, §17.2)
    /// <summary>The lengths auto-grow lands on, in beats (§17.2 "musical units"): ½ measure (when the meter halves evenly), then 1, 2, 4, 8 measures,
    /// never past PhraseMaxBeats.</summary>
    public static List<int> GrowSizes()
    {
        var r = new List<int>();
        int bpb = Bpb;
        if (bpb % 2 == 0) r.Add(bpb / 2);
        for (int m = 1; m <= 8; m *= 2) if (m * bpb <= ProjectConfig.PhraseMaxBeats) r.Add(m * bpb);
        return r;
    }
    /// <summary>The longest phrase in beats (whole measures up to PhraseMaxBeats).</summary>
    public static int MaxPhraseBeats => Mathf.Max(Bpb, ProjectConfig.PhraseMaxBeats / Bpb * Bpb);
    /// <summary>The auto-grow length for a phrase that needs <paramref name="beats"/>: the next of ½, 1, 2, 4, 8 measures (the longest when more).</summary>
    public static int GrowSize(int beats)
    {
        var s = GrowSizes();
        foreach (int b in s) if (b >= beats) return b;
        return s.Count > 0 ? s[s.Count - 1] : MaxPhraseBeats;
    }
    /// <summary>Any settable length (the header may set 3, 5, 6, 7 measures): ½ measure, else whole measures rounded up, clamped to MaxPhraseBeats.</summary>
    public static int PhraseLengthFor(int beats)
    {
        int bpb = Bpb;
        if (beats <= bpb / 2 && bpb % 2 == 0) return bpb / 2;
        int m = Mathf.Max(1, Mathf.CeilToInt(beats / (float)bpb));
        return Mathf.Min(MaxPhraseBeats, m * bpb);
    }

    static bool LivePhrase(KeyBlock kb) { var sm = SM; return kb != null && kb.IsPhrase && sm != null && sm.Islands.Contains(kb); }

    /// <summary>
    /// Adds a ONE-MEASURE melody phrase (kind 4, offset 0, cells of eighths, no cube: the player draws) in column <paramref name="col"/>'s melody
    /// track (K lays the tracks out behind the lanes), and the hand picks up <paramref name="instrument"/> (−1: the lead group) ready to draw. One
    /// History entry; it rises. Returns its index (−1 without a song).
    /// </summary>
    public static int AddPhrase(int col, int instrument = -1)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong || sm.ColumnCount == 0) return -1;
        var mark = MarkSelection();
        var p = new Plan(SongState.Capture());
        col = Mathf.Clamp(col, 0, p.cols.Count - 1);
        var m = new MeasureState
        {
            chordKey = "Melody", root = TonicNear(60), semis = new[] { 0 }, bars = p.Bars(col), kind = 4, placed = true, energy = 2, repeat = 1,
            phraseOffset = 0, phraseBeats = Bpb, phraseGrid = 12
        };
        int back = -1; float backEdge = float.MinValue;
        foreach (int i in p.cols[col])
        {
            var o = p.ms[i];
            float e = o.pz - KeyBlock.EdgeInset + (o.kind == 4 ? KeyBlock.PhraseDepth : KeyBlock.DepthOf(KeyBlock.RowsOf(o.semis)));
            if (e > backEdge) { backEdge = e; back = i; }
        }
        m.px = back >= 0 ? p.ms[back].px : 0f;
        m.pz = back >= 0 ? backEdge + ProjectConfig.LaneGap + KeyBlock.EdgeInset : 0f;
        int idx = p.Add(m);
        p.AddToColumn(col, idx);
        EnsureFits(p, idx, true);
        var result = Apply(p);
        History.Push();
        var kb = NewIsland(p, idx);
        GrownPhrase = kb;
        if (kb != null) { kb.RiseIn(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f); }
        foreach (int a in p.appended) { var ak = NewIsland(p, a); if (ak != null) ak.RiseIn(0.1f); }
        if (PathManager.I != null) PathManager.I.PickUpCube(Pitched(instrument));
        Follow(mark, result, null);
        return kb != null ? sm.Islands.IndexOf(kb) : -1;
    }

    /// <summary>Sets a phrase's length in beats (½ measure, else whole measures rounded up, up to MaxPhraseBeats — the header may set any of them): its
    /// notes are re-encoded (past the end dropped, one crossing it trimmed, the trailing rest re-padded; a cube left without notes goes); a longer
    /// phrase that no longer fits its section takes the next measures into it (past the song's end measures are appended). One History entry.</summary>
    public static void SetPhraseLength(KeyBlock kb, int beats)
    {
        if (!LivePhrase(kb)) return;
        int want = PhraseLengthFor(beats), cur = Mathf.Max(1, kb.phraseBeats);
        if (want == cur) return;
        Resize(kb, want, cur, true, false);
    }

    /// <summary>
    /// Auto-grow while drawing ("people wont know how long their melody is at first so let it autoexpand as they reach the limit"): grows
    /// <paramref name="kb"/> to the next of ½, 1, 2, 4, 8 measures that holds <paramref name="toBeats"/> (§17.2: never 3, 5, 6, 7 by itself; at most
    /// MaxPhraseBeats). Past its section's last column the section takes the next columns (the following section starts later; an emptied one
    /// disappears); past the song's end new measures (MusicTheory.SuggestNext's chords, one bar each) are appended to the last section. NO History
    /// push — part of the caller's stroke (it pushes once). Rebuilds: <see cref="GrownPhrase"/> is the phrase afterwards (a draft is cancelled by
    /// the rebuild: finish the stroke's cube first). True when it grew.
    /// </summary>
    public static bool GrowPhrase(KeyBlock kb, int toBeats)
    {
        if (!LivePhrase(kb)) return false;
        int cur = Mathf.Max(1, kb.phraseBeats);
        int want = GrowSize(Mathf.Max(1, toBeats));
        if (want <= cur) return false;
        Resize(kb, want, cur, false, false);
        return true;
    }

    /// <summary>§19.1 AUTO-UNEXPAND while drawing ("it can auto-expand if needed or auto-unexpand"): the no-History counterpart of GrowPhrase — the
    /// phrase shrinks to the next of ½, 1, 2, 4, 8 measures that holds <paramref name="beats"/> (the caller never asks below the length the phrase had
    /// when its draft began); notes past the new end are dropped, the trailing rest re-padded. Rebuilds: <see cref="GrownPhrase"/> is the phrase
    /// afterwards. True when it shrank.</summary>
    public static bool ShrinkPhraseForDraft(KeyBlock kb, int beats)
    {
        if (!LivePhrase(kb)) return false;
        int cur = Mathf.Max(1, kb.phraseBeats);
        int want = GrowSize(Mathf.Max(1, beats));
        if (want >= cur) return false;
        Resize(kb, want, cur, false, false);
        return true;
    }

    /// <summary>The island after the last <see cref="ExpandGrid"/> (a rebuild re-creates it).</summary>
    public static KeyBlock ExpandedGrid { get; private set; }

    /// <summary>
    /// §19.1 the EXPAND chip ("if max is hit then it says capacity reached in which they'll have to expand the grid to continue"): one more measure
    /// for <paramref name="kb"/> beyond the auto limit. A chord grid / keyboard / stairs: its COLUMN's bars + 1 (at most 4) through K's LIVE resize
    /// (SongManager.ResizeColumnLive: no rebuild — the islands, their tiles, the cubes and a draft being drawn stay; the section may grow past
    /// SectionBars measures, no new boundary; the column's stairs runners re-derived one pass long). A phrase: + 1 measure (½ → 1, n → n + 1; the
    /// section takes the next measure or one is appended, like GrowPhrase — a rebuild: <see cref="GrownPhrase"/>). One History entry (a draft is not
    /// in it: the stroke pushes its own). True when it grew (at the limit: a soft deny).
    /// </summary>
    public static bool ExpandGrid(KeyBlock kb)
    {
        var sm = SM;
        if (sm == null || kb == null || kb.IsMoon || !sm.Islands.Contains(kb)) return false;
        if (kb.IsPhrase)
        {
            int cur = Mathf.Max(1, kb.phraseBeats), bpb = Bpb;
            int want = Mathf.Min(MaxPhraseBeats, (cur / bpb + 1) * bpb);
            if (want <= cur) { Deny(kb); return false; }
            Resize(kb, want, cur, true, false);
            ExpandedGrid = GrownPhrase;
            return true;
        }
        int col = kb.column, bars = sm.ColumnBars(col);
        if (bars >= 4 || !sm.ResizeColumnLive(col, bars + 1)) { Deny(col); return false; }
        History.Push();
        OpCount++;
        ExpandedGrid = kb;
        kb.Pulse(0.9f);
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.35f);
        return true;
    }

    /// <summary>§17.2 DOUBLE (Ableton's "duplicate loop"): the phrase becomes twice as long (at most MaxPhraseBeats) with its notes played again in
    /// the new half; it takes the measures it needs like a grown phrase. One History entry; at the longest a soft deny.</summary>
    public static void DoublePhrase(KeyBlock kb)
    {
        if (!LivePhrase(kb)) return;
        int cur = Mathf.Max(1, kb.phraseBeats);
        if (cur >= MaxPhraseBeats) { Deny(kb); return; }
        Resize(kb, Mathf.Min(MaxPhraseBeats, cur * 2), cur, true, true);
    }

    static void Resize(KeyBlock kb, int want, int cur, bool push, bool twice)
    {
        var sm = SM;
        int at = sm.Islands.IndexOf(kb);
        var mark = MarkSelection();
        var p = new Plan(SongState.Capture());
        var m = p.ms[at];
        m.phraseBeats = want;
        int grid = m.phraseGrid == 6 ? 6 : 12, total = want * TPB, cells = Mathf.Max(1, total / grid);
        for (int k = p.cubes.Count - 1; k >= 0; k--)
        {
            var c = p.cubes[k];
            if (c.measure != at) continue;
            bool ok;
            if (twice)
            {
                var notes = PhraseRoll.NotesOf(c);
                int n0 = notes.Count;
                for (int i = 0; i < n0; i++) { var nt = notes[i]; notes.Add(new PhraseNote(nt.start + cur * TPB, nt.row, nt.len, nt.mod)); }
                ok = Encode(c, notes, total, grid, cells);
            }
            else ok = PhraseRoll.Resize(c, total, grid, cells);
            if (!ok) p.cubes.RemoveAt(k);
        }
        if (want > cur) EnsureFits(p, at, true);
        var result = Apply(p, null, at);
        if (push) History.Push();
        var nk = IslandAt(result != null && at < result.Length ? result[at] : -1);
        GrownPhrase = nk;
        foreach (int a in p.appended) { var ak = NewIsland(p, a); if (ak != null) ak.RiseIn(0.1f); }
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, want > cur ? 1.25f : 0.85f);
        if (nk != null && OnPhraseResized != null) OnPhraseResized(nk, cur);
        Follow(mark, result, null);
    }

    /// <summary>Writes <paramref name="notes"/> into cube state <paramref name="c"/> as a phrase path (PhraseRoll.Encode); false when no note is left.</summary>
    static bool Encode(CubeState c, List<PhraseNote> notes, int total, int grid, int cells)
    {
        int[] xs, zs, mods, durs;
        PhraseRoll.Encode(notes, total, grid, cells, out xs, out zs, out mods, out durs);
        if (xs.Length == 0) return false;
        c.xs = xs; c.zs = zs; c.mods = mods; c.durs = durs; c.bend = null;
        c.rests = new bool[xs.Length];
        for (int i = 0; i < xs.Length; i++) c.rests[i] = mods[i] == 1;
        return true;
    }

    /// <summary>The phrase header's cells (§14.1): 12 = eighths, 6 = sixteenths. The notes keep their times (re-encoded on the new cells). One History entry.</summary>
    public static void SetPhraseGrid(KeyBlock kb, int ticks)
    {
        if (!LivePhrase(kb)) return;
        int g = ticks == 6 ? 6 : 12;
        if ((kb.phraseGrid == 6 ? 6 : 12) == g) return;
        var sm = SM;
        int at = sm.Islands.IndexOf(kb);
        var mark = MarkSelection();
        var p = new Plan(SongState.Capture());
        var m = p.ms[at];
        m.phraseGrid = g;
        int total = Mathf.Max(1, m.phraseBeats) * TPB, cells = Mathf.Max(1, total / g);
        for (int k = p.cubes.Count - 1; k >= 0; k--)
        {
            var c = p.cubes[k];
            if (c.measure != at) continue;
            if (!Encode(c, PhraseRoll.NotesOf(c), total, g, cells)) p.cubes.RemoveAt(k);
        }
        var result = Apply(p, null, at);
        History.Push();
        GrownPhrase = IslandAt(result != null && at < result.Length ? result[at] : -1);
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, g == 6 ? 1.3f : 0.9f);
        Follow(mark, result, null);
    }

    /// <summary>Moves a phrase to start in column <paramref name="col"/> + <paramref name="offsetTicks"/> (snapped to half measures; an offset past the
    /// column's end continues into the next columns); its notes keep their times inside it. When it no longer fits its section the section takes the
    /// next measures (past the song's end measures are appended). One History entry.</summary>
    public static void MovePhrase(KeyBlock kb, int col, int offsetTicks)
    {
        if (!LivePhrase(kb)) return;
        var sm = SM;
        int at = sm.Islands.IndexOf(kb);
        var p = new Plan(SongState.Capture());
        col = Mathf.Clamp(col, 0, p.cols.Count - 1);
        int half = HalfMeasureTicks;
        long off = Math.Max(0L, (long)Math.Round(offsetTicks / (double)half) * half);
        while (col + 1 < p.cols.Count && off >= p.Ticks(col)) { off -= p.Ticks(col); col++; }
        off = Math.Min(off, Math.Max(0, p.Ticks(col) - half));
        var m = p.ms[at];
        if (p.ColumnOf(at) == col && m.phraseOffset == (int)off) return;
        var mark = MarkSelection();
        p.Remove(at);
        p.AddToColumn(col, at);
        m.phraseOffset = (int)off;
        m.bars = p.Bars(col);
        EnsureFits(p, at, true);
        var result = Apply(p, new[] { at });
        History.Push();
        GrownPhrase = IslandAt(result != null && at < result.Length ? result[at] : -1);
        foreach (int a in p.appended) { var ak = NewIsland(p, a); if (ak != null) ak.RiseIn(0.1f); }
        AudioPool.UI(ProceduralAudio.Thud(), 0.28f, 1.1f);
        Follow(mark, result, null);
    }

    /// <summary>
    /// A phrase never crosses a section boundary (SPEC v7 §14.2): the phrase <paramref name="idx"/> spans its start column's time + its offset +
    /// phraseBeats × repeat; every section start inside that span goes (its section takes those measures), and the rest of the last crossed section
    /// stays a section (starting after the span, with its name). With <paramref name="allowAppend"/> a span past the song's end appends measures
    /// (MusicTheory.SuggestNext's chords, one bar each, in the last section). Returns how many were appended.
    /// </summary>
    static int EnsureFits(Plan p, int idx, bool allowAppend)
    {
        var m = p.ms[idx];
        int c0 = p.ColumnOf(idx);
        if (c0 < 0) return 0;
        long s0 = p.StartTicks(c0);
        long end = s0 + Math.Max(0, m.phraseOffset) + (long)Math.Max(1, m.phraseBeats) * Math.Max(1, m.repeat) * TPB;
        int last = c0, appended = 0;
        long t = s0 + p.Ticks(c0);
        while (t < end)
        {
            if (last + 1 >= p.cols.Count) { if (!allowAppend) break; AppendMeasure(p); appended++; }
            last++;
            t += p.Ticks(last);
        }
        bool cut = false; int movedRole = 0;
        for (int c = c0 + 1; c <= last; c++) if (p.start[c]) { p.start[c] = false; movedRole = p.role[c]; p.role[c] = 0; cut = true; }
        if (cut && last + 1 < p.cols.Count && !p.start[last + 1]) { p.start[last + 1] = true; p.role[last + 1] = movedRole; }
        return appended;
    }

    /// <summary>Appends one measure at the song's end: MusicTheory.SuggestNext's first chord after the last chord island (else the home chord), one
    /// bar, in the last column's lane, in the last section.</summary>
    static int AppendMeasure(Plan p)
    {
        var k = MusicTheory.KeyOfSong();
        int lastRoot = -1;
        for (int c = p.cols.Count - 1; c >= 0 && lastRoot < 0; c--) foreach (int i in p.cols[c]) if (p.ms[i].kind == 0) { lastRoot = p.ms[i].root; break; }
        var sug = lastRoot >= 0 ? MusicTheory.SuggestNext(k, lastRoot, 1) : null;
        var md = sug != null && sug.Length > 0 ? sug[0] : MusicTheory.Diatonic(k, 0);
        var ms = MeasureState.From(md);
        ms.kind = 0; ms.group = 0; ms.reg = 0; ms.bars = 1; ms.repeat = 1; ms.carry = 0; ms.mood = 0; ms.placed = true; ms.secRole = 0;
        int lc = p.cols.Count - 1, a = lc >= 0 ? p.Anchor(lc) : -1;
        ms.pz = a >= 0 ? p.ms[a].pz : 0f; ms.px = a >= 0 ? p.ms[a].px : 0f;
        int idx = p.Add(ms);
        p.InsertColumn(p.cols.Count, new List<int> { idx }, false);
        p.appended.Add(idx);
        return p.cols.Count - 1;
    }

    /// <summary>The plan index of a phrase that starts before column <paramref name="col"/> and still plays at its start (−1 none).</summary>
    static int PhraseAcross(Plan p, int col)
    {
        long b = p.StartTicks(col);
        for (int c = 0; c < col; c++)
            foreach (int i in p.cols[c])
            {
                var m = p.ms[i];
                if (m.kind != 4) continue;
                long s = p.StartTicks(c) + Math.Max(0, m.phraseOffset);
                if (s + (long)Math.Max(1, m.phraseBeats) * Math.Max(1, m.repeat) * TPB > b) return i;
            }
        return -1;
    }

    // ================================================================== sections (SPEC v7 §13, §16.3, §17.1)
    /// <summary>A new section starts at column <paramref name="col"/> (the section header's "split here"). A merge group across the cut splits there; a
    /// melody phrase playing across it refuses the split (the phrase shakes: a phrase never crosses a section boundary). One History entry.</summary>
    public static void SplitSectionAt(int col)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var p = new Plan(SongState.Capture());
        if (col <= 0 || col >= p.cols.Count || p.start[col]) { Deny(col); return; }
        int across = PhraseAcross(p, col);
        if (across >= 0) { Deny(IslandAt(across)); return; }
        var mark = MarkSelection();
        p.start[col] = true; p.role[col] = 0;
        var result = Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.3f);
        Follow(mark, result, null);
    }

    /// <summary>Removes the section boundary AT column <paramref name="col"/> — the first column of a section: it joins the section before (the section
    /// header's "join with the next" = JoinSectionAt(SectionFirst(s + 1))). When <paramref name="col"/> is not a section's first column, the section
    /// that holds it joins the NEXT one. The joined section keeps the first name it has. One History entry; nothing to join → a soft deny.</summary>
    public static void JoinSectionAt(int col)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var p = new Plan(SongState.Capture());
        int b = -1;
        if (col > 0 && col < p.cols.Count && p.start[col]) b = col;
        else if (col >= 0 && col < p.cols.Count) for (int c = col + 1; c < p.cols.Count; c++) if (p.start[c]) { b = c; break; }
        if (b < 0) { Deny(col); return; }
        var mark = MarkSelection();
        p.start[b] = false;
        var result = Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.85f);
        Follow(mark, result, null);
    }

    /// <summary>Duplicates section <paramref name="s"/> — its columns, islands (chords, stairs, keyboards, phrases, every setting) and cubes (new ids; echo /
    /// harmony links kept inside the copy) — as NEW columns right after it: a new section with the source's name (its letter follows from its chords:
    /// the same letter). One History entry; the copies rise. Returns the new section's index (−1 refused).</summary>
    public static int DuplicateSection(int s)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return -1;
        var p = new Plan(SongState.Capture());
        var starts = p.SectionStarts();
        if (s < 0 || s >= starts.Count) return -1;
        int a = starts[s], b = s + 1 < starts.Count ? starts[s + 1] - 1 : p.cols.Count - 1;
        var mark = MarkSelection();
        var src = new List<int>();
        for (int c = a; c <= b; c++) src.AddRange(p.cols[c]);
        var clip = ClipOfPlan(p, src, a, null);
        int nc;
        var added = PasteClip(p, clip, b + 1, null, false, out nc);
        // the copy is a section of its own, with the source's name; the section after it keeps its start
        for (int c = b + 1; c <= b + nc && c < p.cols.Count; c++) { p.start[c] = c == b + 1; p.role[c] = c == b + 1 ? p.role[a] : 0; }
        foreach (int i in added) if (p.ms[i].kind == 4) EnsureFits(p, i, true);
        var result = Apply(p);
        History.Push();
        var news = new List<KeyBlock>();
        foreach (int i in added) { var kb = NewIsland(p, i); if (kb != null) news.Add(kb); }
        RiseAll(news);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.45f);
        Follow(mark, result, null);
        return s + 1;
    }

    /// <summary>Deletes section <paramref name="s"/>: its columns, islands and cubes (the bodies sink); the later sections slide left. The song keeps at
    /// least one section (the last one refuses). One History entry.</summary>
    public static void DeleteSection(int s)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var p = new Plan(SongState.Capture());
        var starts = p.SectionStarts();
        if (s < 0 || s >= starts.Count) return;
        int a = starts[s], b = s + 1 < starts.Count ? starts[s + 1] - 1 : p.cols.Count - 1;
        if (starts.Count <= 1) { Deny(a); return; }
        var mark = MarkSelection();
        var gone = new List<int>();
        for (int c = a; c <= b; c++) { gone.AddRange(p.cols[c]); p.cols[c].Clear(); }
        foreach (int i in gone) Husk(IslandAt(i));
        var result = Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
        Follow(mark, result, null);
    }

    /// <summary>Moves section <paramref name="s"/> (its columns as one block, with its name, its phrases and its Moons' starts) so it becomes section
    /// <paramref name="to"/> (0 .. SectionCount − 1). One History entry; the islands slide to their new places.</summary>
    public static void MoveSection(int s, int to)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var p = new Plan(SongState.Capture());
        var starts = p.SectionStarts();
        int n = starts.Count;
        if (s < 0 || s >= n) return;
        to = Mathf.Clamp(to, 0, n - 1);
        if (to == s) return;
        var mark = MarkSelection();
        var secs = new List<List<int>>();
        for (int k = 0; k < n; k++) { var l = new List<int>(); int e = k + 1 < n ? starts[k + 1] : p.cols.Count; for (int c = starts[k]; c < e; c++) l.Add(c); secs.Add(l); }
        var moved = secs[s]; secs.RemoveAt(s); secs.Insert(to, moved);
        var cols = new List<List<int>>(); var st0 = new List<bool>(); var org = new List<int>(); var rl = new List<int>();
        foreach (var sec in secs) for (int k = 0; k < sec.Count; k++) { int c = sec[k]; cols.Add(p.cols[c]); st0.Add(k == 0); org.Add(p.origin[c]); rl.Add(k == 0 ? p.role[c] : 0); }
        p.cols.Clear(); p.cols.AddRange(cols); p.start.Clear(); p.start.AddRange(st0); p.origin.Clear(); p.origin.AddRange(org); p.role.Clear(); p.role.AddRange(rl);
        var movers = new List<int>();   // the moved section's islands (the rebuild's lit-column reference and the Moons' starts prefer the others)
        for (int k = 0, c0 = 0; k < secs.Count; c0 += secs[k].Count, k++) if (secs[k] == moved) for (int q = 0; q < secs[k].Count; q++) movers.AddRange(p.cols[c0 + q]);
        var result = Apply(p, movers);
        History.Push();
        AudioPool.UI(ProceduralAudio.Thud(), 0.28f, 1.0f);
        Follow(mark, result, null);
    }

    /// <summary>§17.1: names section <paramref name="s"/> (0 none, 1 intro, 2 verse, 3 chorus, 4 bridge, 5 drop, 6 outro) — the secRole of the first
    /// island of its first column (live: no rebuild; OnSectionsChanged). One History entry.</summary>
    public static void SetSectionRole(int s, int role)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong || s < 0 || s >= sm.SectionCount) return;
        role = Mathf.Clamp(role, 0, 6);
        var a = sm.AnchorOf(sm.SectionFirst(s));
        if (a == null || a.secRole == role) return;
        a.secRole = role;
        int i = sm.Islands.IndexOf(a);
        if (sm.currentSongData != null && sm.currentSongData.measures != null && i >= 0 && i < sm.currentSongData.measures.Length && sm.currentSongData.measures[i] != null) sm.currentSongData.measures[i].secRole = role;
        sm.RaiseSectionsChanged();
        History.Push();
        OpCount++;
        a.Pulse(0.8f);
        AudioPool.UI(ProceduralAudio.Chime(), 0.25f, 1.1f);
    }

    // ================================================================== many grids (SPEC v7 §16.2–16.3)
    /// <summary>The grid clipboard: copied measures (their relative column: column − the first copied column) with their cubes, and copied Moons
    /// (their relative start column) with theirs.</summary>
    sealed class GridClip
    {
        public readonly List<MeasureState> ms = new List<MeasureState>();
        public readonly List<int> rel = new List<int>();
        public readonly List<CubeState> cubes = new List<CubeState>();      // measure = an index in ms
        public readonly List<MeasureState> moons = new List<MeasureState>();
        public readonly List<int> moonRel = new List<int>();
        public readonly List<CubeState> moonCubes = new List<CubeState>();  // moon = an index in moons
        public int Count => ms.Count + moons.Count;
        public bool PhrasesOnly { get { if (ms.Count == 0 || moons.Count > 0) return false; foreach (var m in ms) if (m.kind != 4) return false; return true; } }
    }
    static GridClip gridClip;

    /// <summary>True when grids were copied (<see cref="CopyGrids"/>).</summary>
    public static bool HasCopiedGrids => gridClip != null && gridClip.Count > 0;
    /// <summary>How many grids (Moons included) the grid clipboard holds.</summary>
    public static int CopiedGridCount => gridClip != null ? gridClip.Count : 0;
    /// <summary>How many columns a paste adds (the copied columns holding a lane; a clip of melodies alone adds none).</summary>
    public static int CopiedColumnCount { get { if (gridClip == null) return 0; var s = new HashSet<int>(); for (int i = 0; i < gridClip.ms.Count; i++) if (gridClip.ms[i].kind != 4) s.Add(gridClip.rel[i]); return s.Count; } }
    public static void ClearCopiedGrids() { if (gridClip == null) return; gridClip = null; if (OnGridClipChanged != null) OnGridClipChanged(); }

    static GridClip ClipOfPlan(Plan p, List<int> idxs, int firstCol, List<int> moonIdxs)
    {
        var clip = new GridClip();
        var pos = new Dictionary<int, int>();
        foreach (int i in idxs)
        {
            int c = p.ColumnOf(i);
            if (c < 0 || pos.ContainsKey(i)) continue;
            var m = MeasureState.Clone(p.ms[i]); m.secRole = 0;
            pos[i] = clip.ms.Count; clip.ms.Add(m); clip.rel.Add(c - firstCol);
        }
        foreach (var c in p.cubes) { int k; if (c.moon < 0 && pos.TryGetValue(c.measure, out k)) { var d = CubeState.Clone(c); d.measure = k; clip.cubes.Add(d); } }
        if (moonIdxs != null && p.st.moons != null)
            foreach (int j in moonIdxs)
            {
                if (j < 0 || j >= p.st.moons.Length || p.st.moons[j] == null) continue;
                var mm = MeasureState.Clone(p.st.moons[j]);
                clip.moons.Add(mm); clip.moonRel.Add(Math.Max(0, mm.col - firstCol));
                int k = clip.moons.Count - 1;
                foreach (var c in p.moonCubes) if (c.moon == j) { var d = CubeState.Clone(c); d.moon = k; clip.moonCubes.Add(d); }
            }
        return clip;
    }

    static GridClip ClipOfGrids(Plan p, List<KeyBlock> list)
    {
        var sm = SM;
        var idxs = new List<int>(); var moons = new List<int>();
        int first = int.MaxValue;
        foreach (var g in list)
        {
            if (g.IsMoon) { int j = sm.Moons.IndexOf(g); if (j >= 0) moons.Add(j); continue; }
            int i = sm.Islands.IndexOf(g);
            if (i < 0 || i >= p.nOld) continue;
            idxs.Add(i); first = Math.Min(first, p.ColumnOf(i));
        }
        if (first == int.MaxValue) { foreach (int j in moons) if (p.st.moons != null && j < p.st.moons.Length) first = Math.Min(first, Math.Max(0, p.st.moons[j].col)); }
        if (first == int.MaxValue) first = 0;
        return ClipOfPlan(p, idxs, first, moons);
    }

    static MeasureState Fresh(MeasureState src, Dictionary<int, int> gmap, ref int maxGroup)
    {
        var m = MeasureState.Clone(src);
        m.placed = true; m.secRole = 0;
        if (m.group > 0) { int g; if (!gmap.TryGetValue(m.group, out g)) { g = ++maxGroup; gmap[m.group] = g; } m.group = g; }
        return m;
    }

    /// <summary>
    /// Inserts <paramref name="clip"/> into the plan at column position <paramref name="at"/>: every relative column that holds a lane (chord island,
    /// keyboard, stairs) becomes a NEW column (in order; its lanes keep their z, groups get new ids); a phrase rides in the new column of its
    /// relative column (the nearest lane column at or before it) — a clip of melodies alone lands in the song's own columns from <paramref name="at"/>
    /// on (appending a measure past the end); cubes get new ids (echo / harmony / twin links kept inside the copy); Moons start at the new column of
    /// their relative column (up to MaxMoons). With <paramref name="land"/> the new columns join sections by the landing rule. Returns the new
    /// measures' plan indices; <paramref name="newCols"/> = columns inserted.
    /// </summary>
    static List<int> PasteClip(Plan p, GridClip clip, int at, List<int> moonsAdded, bool land, out int newCols)
    {
        var added = new List<int>();
        newCols = 0;
        if (clip == null) return added;
        at = Mathf.Clamp(at, 0, p.cols.Count);
        var laneRels = new List<int>();
        for (int i = 0; i < clip.ms.Count; i++) if (clip.ms[i].kind != 4 && !laneRels.Contains(clip.rel[i])) laneRels.Add(clip.rel[i]);
        laneRels.Sort();
        int maxGroup = 0; foreach (var m in p.ms) maxGroup = Math.Max(maxGroup, m.group);
        var gmap = new Dictionary<int, int>();
        var idxOf = new int[clip.ms.Count];
        for (int i = 0; i < idxOf.Length; i++) idxOf[i] = -1;
        for (int k = 0; k < laneRels.Count; k++)
        {
            var members = new List<int>();
            for (int i = 0; i < clip.ms.Count; i++)
            {
                if (clip.rel[i] != laneRels[k] || clip.ms[i].kind == 4) continue;
                int idx = p.Add(Fresh(clip.ms[i], gmap, ref maxGroup));
                members.Add(idx); idxOf[i] = idx; added.Add(idx);
            }
            p.InsertColumn(at + k, members, false);
        }
        newCols = laneRels.Count;
        for (int i = 0; i < clip.ms.Count; i++)
        {
            if (clip.ms[i].kind != 4) continue;
            int target;
            if (laneRels.Count > 0) { int k = 0; for (int q = 0; q < laneRels.Count; q++) if (laneRels[q] <= clip.rel[i]) k = q; target = at + k; }
            else { target = at + Math.Max(0, clip.rel[i]); for (int guard = 0; guard < 64 && target >= p.cols.Count; guard++) AppendMeasure(p); target = Mathf.Min(target, p.cols.Count - 1); }
            var m = Fresh(clip.ms[i], gmap, ref maxGroup);
            m.group = 0; m.bars = p.Bars(target);
            int idx = p.Add(m);
            p.AddToColumn(target, idx); idxOf[i] = idx; added.Add(idx);
        }
        int nextId = NextCubeId(p);
        var ids = new Dictionary<int, int>();
        var fresh = new List<CubeState>();
        foreach (var cs in clip.cubes)
        {
            if (cs.measure < 0 || cs.measure >= idxOf.Length || idxOf[cs.measure] < 0) continue;
            var c = CubeState.Clone(cs);
            c.measure = idxOf[cs.measure];
            int nid = nextId++;
            if (cs.id != 0) ids[cs.id] = nid;
            c.id = nid;
            fresh.Add(c);
        }
        foreach (var c in fresh)
        {
            int v;
            c.echoOf = c.echoOf >= 0 && ids.TryGetValue(c.echoOf, out v) ? v : -1;
            c.twinOf = c.twinOf >= 0 && ids.TryGetValue(c.twinOf, out v) ? v : -1;
        }
        p.cubes.AddRange(fresh);
        if (clip.moons.Count > 0 && p.cols.Count > 0)
        {
            var list = p.st.moons != null ? p.st.moons.ToList() : new List<MeasureState>();
            for (int j = 0; j < clip.moons.Count && list.Count < SongManager.MaxMoons; j++)
            {
                var mm = MeasureState.Clone(clip.moons[j]); mm.kind = 1; mm.placed = true; mm.secRole = 0;
                int nj = list.Count; list.Add(mm);
                if (moonsAdded != null) moonsAdded.Add(nj);
                int k = 0; for (int q = 0; q < laneRels.Count; q++) if (laneRels[q] <= clip.moonRel[j]) k = q;
                int col = laneRels.Count > 0 ? at + k : Mathf.Clamp(at + clip.moonRel[j], 0, p.cols.Count - 1);
                p.moonAt.Add(new KeyValuePair<MeasureState, int>(mm, p.cols[col].Count > 0 ? p.cols[col][0] : -1));
                foreach (var cs in clip.moonCubes) if (cs.moon == j) { var c = CubeState.Clone(cs); c.moon = nj; c.id = nextId++; c.echoOf = -1; c.twinOf = -1; p.moonCubes.Add(c); }
            }
            p.st.moons = list.ToArray();
        }
        if (land) p.Land();
        return added;
    }

    /// <summary>Copies <paramref name="grids"/> (chord islands, keyboards, stairs, phrases; Moons when selected) and their cubes into the GRID CLIPBOARD,
    /// relative to the first copied column. The song does not change: no History entry. False when nothing was copied.</summary>
    public static bool CopyGrids(IList<KeyBlock> grids)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return false;
        var list = Clean(grids);
        if (list.Count == 0) return false;
        gridClip = ClipOfGrids(new Plan(SongState.Capture()), list);
        foreach (var g in list) g.Pulse(0.8f);
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.4f);
        if (OnGridClipChanged != null) OnGridClipChanged();
        return gridClip.Count > 0;
    }

    /// <summary>Pastes the grid clipboard as NEW columns at column position <paramref name="atColumn"/> (0 = the front, ColumnCount = the end; the
    /// selection bar pastes after the selection): they join the section they land in (the landing rule: inside a section they join it; at a section's
    /// edge the section before while it has fewer than 4 measures, else the one after, else they start a new one). A clipboard of melodies alone is
    /// laid over the song's columns from there. The pasted grids (and Moons) become the selection. One History entry; they rise. Returns them.</summary>
    public static List<KeyBlock> PasteGrids(int atColumn)
    {
        var r = new List<KeyBlock>();
        var sm = SM;
        if (sm == null || !sm.HasSong || !HasCopiedGrids) return r;
        var p = new Plan(SongState.Capture());
        var moons = new List<int>(); int nc;
        var added = PasteClip(p, gridClip, gridClip.PhrasesOnly ? Mathf.Clamp(atColumn, 0, p.cols.Count) : SkipGlued(atColumn), moons, true, out nc);
        if (added.Count == 0 && moons.Count == 0) return r;
        foreach (int i in added) if (p.ms[i].kind == 4) EnsureFits(p, i, true);
        Apply(p);
        History.Push();
        Arrive(p, added, moons, r);
        return r;
    }

    /// <summary>The new grids of an op: they rise one after another, a sparkle, and become the selection.</summary>
    static void Arrive(Plan p, List<int> added, List<int> moons, List<KeyBlock> into)
    {
        var sm = SM;
        foreach (int i in added) { var kb = NewIsland(p, i); if (kb != null) into.Add(kb); }
        RiseAll(into);
        foreach (int a in p.appended) { var ak = NewIsland(p, a); if (ak != null && !into.Contains(ak)) ak.RiseIn(0.1f); }
        if (moons != null) foreach (int j in moons) if (j >= 0 && j < sm.Moons.Count && sm.Moons[j] != null) { sm.Moons[j].RiseIn(); into.Add(sm.Moons[j]); }
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.45f);
        GridSelection.Set(into);
    }

    /// <summary>Deletes <paramref name="grids"/> and their cubes (the bodies sink; Moons too); emptied columns disappear and the later ones slide left; a
    /// phrase whose column lost every lane moves to the next column that still has one. The song keeps at least one grid (deleting every one refuses).
    /// The selection clears. One History entry.</summary>
    public static void DeleteGrids(IList<KeyBlock> grids)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var list = Clean(grids);
        var islands = list.Where(g => !g.IsMoon).ToList();
        var moons = list.Where(g => g.IsMoon).ToList();
        if (islands.Count == 0 && moons.Count == 0) return;
        int lanesLeft = 0;
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsPhrase && !islands.Contains(kb)) lanesLeft++;
        if (lanesLeft == 0) { Deny(islands.Count > 0 ? islands[0].column : -1); return; }
        var p = new Plan(SongState.Capture());
        foreach (var g in islands) p.Remove(sm.Islands.IndexOf(g));
        RehomePhrases(p);
        RemoveMoons(p, moons);
        foreach (var g in islands) Husk(g);
        Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
        GridSelection.Clear();
    }

    /// <summary>Phrases whose column lost every lane move to the next column that still has one (their offset kept), else to the previous one (at its start).</summary>
    static void RehomePhrases(Plan p)
    {
        for (int c = 0; c < p.cols.Count; c++)
        {
            if (p.cols[c].Count == 0 || p.HasLanes(c)) continue;
            int to = -1; bool later = true;
            for (int k = c + 1; k < p.cols.Count && to < 0; k++) if (p.HasLanes(k)) to = k;
            if (to < 0) { later = false; for (int k = c - 1; k >= 0 && to < 0; k--) if (p.HasLanes(k)) to = k; }
            if (to < 0) continue;
            foreach (int i in new List<int>(p.cols[c]))
            {
                p.Remove(i); p.AddToColumn(to, i);
                var m = p.ms[i];
                m.bars = p.Bars(to);
                m.phraseOffset = later ? Mathf.Clamp(m.phraseOffset, 0, Mathf.Max(0, p.Ticks(to) - HalfMeasureTicks)) : 0;
            }
        }
    }

    /// <summary>Removes Moons from the plan's snapshot (their cubes too; later Moon cubes renumbered) and from the live Moon list (their bodies sink), so
    /// the rebuild's Moon remap lines up with the snapshot. Returns map[old Moon] = new Moon (−1 removed).</summary>
    static int[] RemoveMoons(Plan p, List<KeyBlock> moons)
    {
        var sm = SM;
        int n = sm.Moons.Count;
        var map = new int[n];
        for (int j = 0; j < n; j++) map[j] = j;
        if (moons == null || moons.Count == 0 || p.st.moons == null) return map;
        var gone = new List<int>();
        foreach (var m in moons) { int j = sm.Moons.IndexOf(m); if (j >= 0 && j < p.st.moons.Length && !gone.Contains(j)) gone.Add(j); }
        if (gone.Count == 0) return map;
        gone.Sort();
        var keep = new List<MeasureState>();
        var full = new int[p.st.moons.Length];
        for (int j = 0; j < full.Length; j++) { if (gone.Contains(j)) { full[j] = -1; continue; } full[j] = keep.Count; keep.Add(p.st.moons[j]); }
        for (int j = 0; j < n; j++) map[j] = j < full.Length ? full[j] : -1;
        p.moonCubes.RemoveAll(c => c.moon < 0 || c.moon >= full.Length || full[c.moon] < 0);
        foreach (var c in p.moonCubes) c.moon = full[c.moon];
        p.st.moons = keep.ToArray();
        for (int k = gone.Count - 1; k >= 0; k--) { if (gone[k] >= sm.Moons.Count) continue; var m = sm.Moons[gone[k]]; sm.Moons.RemoveAt(gone[k]); Husk(m); }
        return map;
    }

    /// <summary>Transposes <paramref name="grids"/> by <paramref name="octaves"/> — their register (−2..+2, clamped; Moons keep theirs): their tiles sound
    /// octaves higher / lower and the towers rise on their turns. Nothing can move → a soft deny. One History entry; the selection follows.</summary>
    public static void TransposeGrids(IList<KeyBlock> grids, int octaves)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong || octaves == 0) return;
        var list = Clean(grids).Where(g => !g.IsMoon).ToList();
        if (list.Count == 0) return;
        var p = new Plan(SongState.Capture());
        bool any = false;
        foreach (var g in list)
        {
            var m = p.ms[sm.Islands.IndexOf(g)];
            int r = Mathf.Clamp(m.reg + octaves, -2, 2);
            if (r != m.reg) { m.reg = r; any = true; }
        }
        if (!any) { foreach (var g in list) g.Shake(0.08f); AudioPool.UI(ProceduralAudio.Thud(), 0.2f, 1.3f); return; }
        var mark = MarkSelection();
        var result = Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, octaves > 0 ? 1.5f : 0.8f);
        Follow(mark, result, null);
    }

    /// <summary>Duplicates <paramref name="grids"/> (+ cubes, Moons) as new columns right after the last selected column (joined into sections by the
    /// landing rule, like a paste); a selection of melodies alone is copied right after each melody ends (it continues itself). The copies become
    /// the selection. One History entry; they rise. Returns them.</summary>
    public static List<KeyBlock> DuplicateGrids(IList<KeyBlock> grids)
    {
        var r = new List<KeyBlock>();
        var sm = SM;
        if (sm == null || !sm.HasSong) return r;
        var list = Clean(grids);
        if (list.Count == 0) return r;
        var p = new Plan(SongState.Capture());
        var clip = ClipOfGrids(p, list);
        var moons = new List<int>();
        List<int> added;
        if (clip.PhrasesOnly) added = DuplicatePhrasesAfter(p, list);
        else
        {
            int last = -1;
            foreach (var g in list) if (!g.IsMoon && !g.IsPhrase) last = Math.Max(last, g.column);
            if (last < 0) foreach (var g in list) last = Math.Max(last, g.column);
            int nc;
            added = PasteClip(p, clip, SkipGlued(last + 1), moons, true, out nc);
        }
        if (added.Count == 0 && moons.Count == 0) return r;
        foreach (int i in added) if (p.ms[i].kind == 4) EnsureFits(p, i, true);
        Apply(p);
        History.Push();
        Arrive(p, added, moons, r);
        return r;
    }

    /// <summary>A copy of each selected phrase starting where it ends (snapped to half measures; measures appended past the song's end), cubes and all.</summary>
    static List<int> DuplicatePhrasesAfter(Plan p, List<KeyBlock> list)
    {
        var sm = SM;
        var added = new List<int>();
        int nextId = NextCubeId(p), half = HalfMeasureTicks;
        foreach (var g in list)
        {
            if (!g.IsPhrase) continue;
            int i = sm.Islands.IndexOf(g), c0 = p.ColumnOf(i);
            if (c0 < 0) continue;
            var src = p.ms[i];
            long endT = p.StartTicks(c0) + Math.Max(0, src.phraseOffset) + (long)Math.Max(1, src.phraseBeats) * Math.Max(1, src.repeat) * TPB;
            endT = (endT + half - 1) / half * half;
            int col = -1; long colStart = 0;
            for (int guard = 0; guard < 64 && col < 0; guard++)
            {
                long t0 = 0;
                for (int c = 0; c < p.cols.Count; c++) { long t1 = t0 + p.Ticks(c); if (endT < t1) { col = c; colStart = t0; break; } t0 = t1; }
                if (col < 0) AppendMeasure(p);
            }
            if (col < 0) continue;
            var m = MeasureState.Clone(src);
            m.secRole = 0; m.group = 0; m.phraseOffset = (int)(endT - colStart); m.bars = p.Bars(col); m.placed = true;
            int idx = p.Add(m);
            p.AddToColumn(col, idx);
            added.Add(idx);
            var ids = new Dictionary<int, int>(); var fresh = new List<CubeState>();
            foreach (var cs in p.cubes) if (cs.moon < 0 && cs.measure == i) { var c = CubeState.Clone(cs); c.measure = idx; int nid = nextId++; if (cs.id != 0) ids[cs.id] = nid; c.id = nid; fresh.Add(c); }
            foreach (var c in fresh) { int v; c.echoOf = c.echoOf >= 0 && ids.TryGetValue(c.echoOf, out v) ? v : -1; c.twinOf = c.twinOf >= 0 && ids.TryGetValue(c.twinOf, out v) ? v : -1; }
            p.cubes.AddRange(fresh);
        }
        return added;
    }

    /// <summary>Every selected grid plays <paramref name="n"/> passes (1..MaxRepeat; Moons keep theirs). One History entry; the selection follows.</summary>
    public static void SetRepeatGrids(IList<KeyBlock> grids, int n)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        n = Mathf.Clamp(n, 1, ProjectConfig.MaxRepeat);
        var list = Clean(grids).Where(g => !g.IsMoon).ToList();
        if (list.Count == 0) return;
        var p = new Plan(SongState.Capture());
        bool any = false;
        foreach (var g in list) { var m = p.ms[sm.Islands.IndexOf(g)]; if (m.repeat != n) { m.repeat = n; any = true; } }
        if (!any) return;
        var mark = MarkSelection();
        var result = Apply(p);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, n > 1 ? 1.3f : 0.85f);
        Follow(mark, result, null);
    }

    /// <summary>
    /// FLOW THROUGH THE SELECTION (SPEC v7 §2.7, §13.5 "the same pattern repeated in different chords in one section"): <paramref name="n"/> ≥ 1 — the
    /// grids of the first selected column that can carry (chord islands, keyboards) flow over the next chords up to the last selected column (carry =
    /// last − first column, at most MaxCarry; a selection of one column: n), style 1 (flow: adapted by pedal, the cube glides). <paramref name="n"/> ≤ 0
    /// — every selected grid that flows stops (carry 0). One History entry, OnCarryChanged; the selection follows.
    /// </summary>
    public static void SetFlowGrids(IList<KeyBlock> grids, int n)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var list = Clean(grids).Where(g => !g.IsMoon).ToList();
        if (list.Count == 0) return;
        var p = new Plan(SongState.Capture());
        var changed = new List<int>();
        if (n <= 0)
        {
            foreach (var g in list) { int i = sm.Islands.IndexOf(g); var m = p.ms[i]; if (m.carry > 0 && m.carryStyle == 1) { m.carry = 0; changed.Add(i); } }
        }
        else
        {
            var carriers = list.Where(g => g.kind == 0 || g.kind == 2).ToList();
            if (carriers.Count == 0) { Deny(list[0].column); return; }
            int first = carriers.Min(g => g.column), last = list.Max(g => g.column);
            int span = last > first ? Mathf.Min(ProjectConfig.MaxCarry, last - first) : Mathf.Clamp(n, 1, ProjectConfig.MaxCarry);
            foreach (var g in carriers)
            {
                if (g.column != first) continue;
                int i = sm.Islands.IndexOf(g); var m = p.ms[i];
                if (m.carry == span && m.carryStyle == 1) continue;
                m.carry = span; m.carryStyle = 1; changed.Add(i);
            }
        }
        if (changed.Count == 0) return;
        var mark = MarkSelection();
        var result = Apply(p);
        History.Push();
        foreach (int i in changed) { var kb = IslandAt(result != null && i < result.Length ? result[i] : i); if (kb != null) sm.RaiseCarryChanged(kb); }   // W's flow spell
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, n > 0 ? 1.2f : 0.85f);
        Follow(mark, result, null);
    }

    /// <summary>LAUNCH (SPEC v7 §2.8 — "the cubes play and then from their last location, get flung upwards and to the right to the next grid"):
    /// <paramref name="on"/> — the grids of the LAST selected column that can launch (not stairs, not Moons) fling their cubes into what follows (a
    /// section's launch into the next section); off — every selected grid stops launching. One History entry, OnLaunchChanged; the selection follows.</summary>
    public static void SetLaunchGrids(IList<KeyBlock> grids, bool on)
    {
        var sm = SM;
        if (sm == null || !sm.HasSong) return;
        var list = Clean(grids).Where(g => !g.IsMoon && !g.IsStairs).ToList();
        if (list.Count == 0) return;
        var p = new Plan(SongState.Capture());
        var changed = new List<int>();
        int last = list.Max(g => g.column);
        foreach (var g in list)
        {
            if (on && g.column != last) continue;
            int i = sm.Islands.IndexOf(g); var m = p.ms[i];
            if (m.launch == on) continue;
            m.launch = on; changed.Add(i);
        }
        if (changed.Count == 0) return;
        var mark = MarkSelection();
        var result = Apply(p);
        History.Push();
        foreach (int i in changed) { var kb = IslandAt(result != null && i < result.Length ? result[i] : i); if (kb != null) sm.RaiseLaunchChanged(kb); }   // W's fling look
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, on ? 1.4f : 0.85f);
        Follow(mark, result, null);
    }
}
