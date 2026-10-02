using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The world preview of an island card dragged out of the tray (SPEC v3 §3.1): a translucent platform + tile grid + hub in the
/// chord colour (45 % alpha) with a pulsing rim, following the cursor on the sea. The tray drives it (Track) and reads the state on release
/// (Snapshot, then Place).
/// v4 (SPEC v4 §3 K5): the ghost snaps to COLUMN slots. Over a column's inner span: STACK — the empty slot above / below an island of that
/// column nearest the pointer (LaneGap clear; soft rim in the chord colour; PlaceIslandInColumn); BLOCKED when the column is full (MaxLanes,
/// red). Near a boundary between columns or either end: MERGE — flush against the east / west edge of an island in the pointer's lane (the
/// light sheet on its edge; the new column glues to it: PlaceIslandMerged) — else INSERT — a new column there (amber, the Route gap opens:
/// PlaceIslandAsColumn); BLOCKED between two glued columns (nothing can squeeze in). Free (v3: appended at the END) is kept for old callers.
/// v5: a column's conveyor belts belong to it (its span reaches the end of its longest belt; a merge east of a belted island lands at the belt's end).
/// v6: a keyboard card (MeasureData.kind 2, SongManager.KeyboardMeasure) shows a translucent piano (one row of keys, the wood case) and keeps its
/// kind through Place; MERGE slots only offer islands of the same family (a keyboard never glues to a chord island).
/// v7: a STAIRS card (kind 3) is a one-row footprint like the keyboard, a translucent stone staircase (never a MERGE slot: stairs never merge); a
/// card of several measures is that many measures wide; a column's span is its own measures (SongManager.ColumnWidth); phrases never count as lanes.
/// </summary>
public class IslandGhost : MonoBehaviour
{
    public enum Mode { Free, Blocked, Insert, Merge, Stack }
    public Mode State { get; private set; }
    public SongManager.MeasureData Data { get; private set; }
    /// <summary>v4: the column position a new column would take (Insert: 0 = the front of the song … ColumnCount = the end), -1 otherwise.</summary>
    public int InsertAt { get; private set; } = -1;
    /// <summary>v4: the column a STACK (or a full column's BLOCKED) refers to, -1 otherwise.</summary>
    public int Column { get; private set; } = -1;
    public KeyBlock MergeTarget => State == Mode.Merge ? slot.target : null;
    public bool MergeEast => slot.east;
    public int Rows { get; private set; }
    /// <summary>v6: the card is a keyboard island (kind 2).</summary>
    public bool IsKeyboard => Data != null && Data.kind == 2;
    /// <summary>v7: the card is a stairs island (kind 3): one row, never merges.</summary>
    public bool IsStairs => Data != null && Data.kind == 3;
    public float Width => KeyBlock.IslandWidth * Mathf.Clamp(Data != null && Data.bars > 0 ? Data.bars : 1, 1, 4);   // v7: a card of several measures
    public float Depth => KeyBlock.DepthOf(Rows);
    /// <summary>The ghost's root (tile 0,0 centre) at sea level: the snapped cursor spot, or the merge slot.</summary>
    public Vector3 Root { get; private set; }
    public Vector3 Centre => Root + KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, Rows);
    public bool Visible => visible;

    /// <summary>SPEC v4: what a release would do, captured before the ghost is destroyed (the tray reads it, then calls Place).</summary>
    public struct Drop { public Mode mode; public bool visible; public KeyBlock target; public bool east; public int insertAt; public Vector3 centre; public int column; public float pz; }
    public Drop Snapshot() => new Drop { mode = State, visible = visible, target = MergeTarget, east = MergeEast, insertAt = InsertAt, centre = Centre, column = Column, pz = Root.z };
    /// <summary>SPEC v4: places <paramref name="md"/> as <paramref name="d"/> says: Stack → PlaceIslandInColumn(column, pz), Insert → PlaceIslandAsColumn
    /// (insertAt, in the ghost's lane), Merge → PlaceIslandMerged(target, east), Free → a new column at the end; Blocked → nothing. One History entry
    /// (the SongManager op pushes). Returns the new island's index, -1 when nothing was placed.</summary>
    public static int Place(Drop d, SongManager.MeasureData md)
    {
        var sm = SongManager.I;
        if (sm == null || md == null || d.mode == Mode.Blocked) return -1;
        if (d.mode == Mode.Merge && d.target != null) return sm.PlaceIslandMerged(md, d.target, d.east);
        if (d.mode == Mode.Stack && d.column >= 0) return sm.PlaceIslandInColumn(md, d.column, d.pz);
        var copy = SongManager.CopyMeasure(md);
        copy.placed = true; copy.pz = d.pz;
        if (d.mode == Mode.Insert && d.insertAt >= 0) return sm.PlaceIslandAsColumn(copy, d.insertAt);
        return sm.PlaceIslandAsColumn(copy, sm.ColumnCount);
    }

    /// <summary>How far inside a column's platform span (x) the pointer must be for a STACK; nearer the edges it is a boundary (MERGE / INSERT).</summary>
    public const float StackInset = 1.6f;

    Color chord, platColor;
    readonly List<Material> mats = new List<Material>();
    Material rimMat; LineRenderer rim; Transform body;
    readonly List<Renderer> rends = new List<Renderer>();
    Vector3 shown; bool visible = true, hasShown, ownsGap, ownsSheet; float age;
    SongManager.MergeSlot slot;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly Color Amber = new Color(1f, 0.72f, 0.28f);
    // v7: the stairs ghost's stone (the family of B's stairs and W's tower)
    static readonly Color StoneStep = new Color(0.95f, 0.89f, 0.86f), StoneBase = new Color(0.47f, 0.41f, 0.50f), StoneGlow = new Color(1f, 0.9f, 0.78f);

    public static IslandGhost Create(SongManager.MeasureData md)
    {
        var go = new GameObject("IslandGhost");
        var g = go.AddComponent<IslandGhost>();
        g.Build(md);
        return g;
    }

    void Build(SongManager.MeasureData md)
    {
        Data = md;
        int[] semis = md.semitones != null && md.semitones.Length > 0 ? md.semitones : new[] { 0, 4, 7 };
        if (md.kind == 2 || md.kind == 3) semis = new[] { 0 };   // v7: a stairs card is one row too
        Rows = KeyBlock.RowsOf(semis);
        chord = md.kind == 2 ? KeyBlock.KeyboardGlow : md.kind == 3 ? StoneGlow : MusicTheory.ChordColor(md.chordRootMIDI, semis);
        platColor = md.kind == 2 ? KeyBlock.KeyboardWood : md.kind == 3 ? StoneBase : KeyBlock.PlatformColorOf(chord);
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        float w = Width, d = Depth, thick = ProjectConfig.PlatformThickness;
        Vector3 off = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, Rows);

        var plat = MakeMesh("Platform", MeshFactory.RoundedBox(new Vector3(w, thick, d), 0.24f, 3), Palette.A(Color.Lerp(platColor, chord, 0.35f), 0.45f));
        plat.localPosition = new Vector3(off.x, -thick * 0.5f - 0.03f, off.z);
        if (md.kind == 3)
        {
            // v7: a stairs ghost = a translucent stone staircase (its steps falling / climbing across the first measure, the tower's pale stone)
            int n = Mathf.Clamp(md.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
            float sw = KeyBlock.IslandWidth / n, e0 = KeyBlock.EdgeInset, stepD = KeyBlock.DepthOf(1) - 0.9f;
            var stone = NewMat(Palette.A(StoneStep, 0.55f));
            for (int i = 0; i < n; i++)
            {
                float top = md.stairDir > 0 ? (i + 1) * ProjectConfig.StairStepRise : -i * ProjectConfig.StairStepRise + (n - 1) * ProjectConfig.StairStepRise;
                float h = Mathf.Max(0.12f, top + 0.12f);
                var t = MakeMesh("Step", MeshFactory.RoundedBox(new Vector3(sw - 0.06f, h, stepD), 0.05f, 2), stone);
                t.localPosition = new Vector3(-e0 + (i + 0.5f) * sw, h * 0.5f, -e0 + 0.55f + stepD * 0.5f);
            }
        }
        else if (md.kind == 2)
        {
            // v6: a keyboard ghost = its keys (the real layout from its lowest key), ivory and ink, translucent
            int n = ProjectConfig.KeyboardKeys, low = md.chordRootMIDI > 0 ? md.chordRootMIDI : SongManager.KeyboardLowestKey();
            var cx = new float[n]; var cz = new float[n]; var kw = new float[n]; var kl = new float[n];
            KeyBlock.KeyLayout(low, cx, cz, kw, kl);
            var ivory = NewMat(Palette.A(new Color(1f, 0.95f, 0.86f), 0.55f)); var ebony = NewMat(Palette.A(new Color(0.2f, 0.16f, 0.26f), 0.6f));
            for (int k = 0; k < n; k++)
            {
                bool black = KeyBlock.IsBlackKeyMidi(low + k);
                var t = MakeMesh(black ? "BlackKey" : "Key", MeshFactory.RoundedBox(new Vector3(kw[k], 0.14f, kl[k]), 0.05f, 2), black ? ebony : ivory);
                t.localPosition = new Vector3(cx[k], black ? 0.2f : 0.07f, cz[k]);
            }
        }
        else
        {
            var tileMesh = MeshFactory.RoundedBox(new Vector3(ProjectConfig.TileSize, 0.14f, ProjectConfig.TileSize), 0.08f, 2);
            Color tileC = Palette.A(Color.Lerp(KeyBlock.TileTintOf(chord), Color.white, 0.25f), 0.5f);
            var tileMat = NewMat(tileC);
            for (int x = 0; x < ProjectConfig.numInversions; x++)
                for (int z = 0; z < Rows; z++)
                {
                    var t = MakeMesh("Tile", tileMesh, tileMat);
                    t.localPosition = new Vector3(x * ProjectConfig.Spacing, 0.07f, z * ProjectConfig.Spacing);
                }
        }
        int bars = Mathf.Clamp(md.bars <= 0 ? 1 : md.bars, 1, 4);
        var hub = MakeMesh("Hub", MeshFactory.Prism(bars + 2, ProjectConfig.HubRadius, ProjectConfig.HubHeight), Palette.A(chord, 0.6f));
        hub.localPosition = new Vector3(off.x, 0.15f - ProjectConfig.HubHeight * 0.5f, off.z - (d * 0.5f + 0.9f));

        var rg = new GameObject("Rim");
        rg.transform.SetParent(body, false);
        rg.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // flat ribbon (TransformZ alignment, +Z up)
        rim = rg.AddComponent<LineRenderer>();
        rim.useWorldSpace = false; rim.loop = true; rim.alignment = LineAlignment.TransformZ;
        rim.widthMultiplier = 0.1f; rim.numCornerVertices = 2;
        rim.shadowCastingMode = ShadowCastingMode.Off; rim.receiveShadows = false; rim.lightProbeUsage = LightProbeUsage.Off;
        rimMat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white);   // a crisp flat line; its colour says the state
        mats.Add(rimMat);
        rim.sharedMaterial = rimMat;
        float e = KeyBlock.EdgeInset, r = 0.24f, y = 0.02f;
        float x0 = -e + 0.02f, x1 = -e + w - 0.02f, z0 = -e + 0.02f, z1 = -e + d - 0.02f;
        var pts = new List<Vector3>();
        Corner(pts, x0 + r, z0 + r, r, 270f, 180f, y);
        Corner(pts, x0 + r, z1 - r, r, 180f, 90f, y);
        Corner(pts, x1 - r, z1 - r, r, 90f, 0f, y);
        Corner(pts, x1 - r, z0 + r, r, 0f, -90f, y);
        var toRim = Quaternion.Euler(90f, 0f, 0f);
        var arr = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++) arr[i] = toRim * pts[i];
        rim.positionCount = arr.Length; rim.SetPositions(arr);
        rends.Add(rim);
    }

    static void Corner(List<Vector3> pts, float cx, float cz, float r, float a0, float a1, float y)
    {
        for (int i = 0; i <= 4; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / 4f) * Mathf.Deg2Rad;
            pts.Add(new Vector3(cx + Mathf.Cos(a) * r, y, cz + Mathf.Sin(a) * r));
        }
    }

    Material NewMat(Color c) { var m = Fx.Alpha(IconFactory.GetTexture("white"), c); mats.Add(m); return m; }

    Transform MakeMesh(string name, Mesh mesh, Color c) { return MakeMesh(name, mesh, NewMat(c)); }
    Transform MakeMesh(string name, Mesh mesh, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(body, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        rends.Add(mr);
        return go.transform;
    }

    /// <summary>Follows the cursor's sea point <paramref name="ground"/> and snaps to the column slot there (see the class summary), deciding the state.</summary>
    public void Track(Vector3 ground)
    {
        var sm = SongManager.I;
        Vector3 off = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, Rows);
        float snap = ProjectConfig.IslandSnap;
        Vector3 raw = new Vector3(Mathf.Round((ground.x - off.x) / snap) * snap, 0f, Mathf.Round((ground.z - off.z) / snap) * snap);
        Mode mode = Mode.Blocked; int insertAt = -1, column = -1; int gapSeg = -1; Vector3 gapAt = Vector3.zero;
        SongManager.MergeSlot s = new SongManager.MergeSlot();
        Vector3 root = raw;
        if (sm != null && sm.HasSong && sm.ColumnCount > 0)
        {
            int nc = sm.ColumnCount; float e = KeyBlock.EdgeInset;
            int over = -1;
            for (int c = 0; c < nc; c++) { float west = sm.ColumnX(c) - e; if (ground.x > west + StackInset && ground.x < west + sm.ColumnWidth(c) + sm.BeltExtent(c) - StackInset) { over = c; break; } }   // v7: the column's own measures
            if (over >= 0)
            {
                column = over;
                if (sm.LaneCount(over) >= ProjectConfig.MaxLanes) mode = Mode.Blocked;   // a full column: red, the card flies back (v7: phrases are no lanes)
                else { mode = Mode.Stack; root = new Vector3(sm.ColumnX(over), 0f, StackSlot(sm, over, raw.z)); }
            }
            else
            {
                // a boundary: b = the column position a new column would take (0 = before column 0, nc = after the last)
                int b = 0;
                while (b < nc && sm.ColumnX(b) - e + (sm.ColumnWidth(b) + sm.BeltExtent(b)) * 0.5f < ground.x) b++;
                float bx = b == 0 ? sm.ColumnX(0) - e : (b == nc ? sm.ColumnEastEdge(nc - 1) : (sm.ColumnEastEdge(b - 1) + sm.ColumnX(b) - e) * 0.5f);
                bool glued = b > 0 && b < nc && sm.Glued(b - 1);
                var L = b > 0 && !IsStairs ? InLane(sm, b - 1, raw.z) : null;   // v7: a stairs card never merges
                var R = b < nc && !IsStairs ? InLane(sm, b, raw.z) : null;
                bool gap = b == 0 || b == nc || sm.SectionBoundaryAfter(b - 1);   // v7: inside a section the columns touch — a card there INSERTS a column (it joins the section); a MERGE only glues across a gap
                bool okL = L != null && sm.GroupNeighbour(L, +1) == null && !glued && gap;
                bool okR = R != null && sm.GroupNeighbour(R, -1) == null && !glued && gap;
                if (okL && (!okR || ground.x <= bx)) { mode = Mode.Merge; s = new SongManager.MergeSlot { target = L, east = true, gap = 0f, rootX = L.px + L.Width + sm.BeltExtent(b - 1), rootZ = L.pz }; root = new Vector3(s.rootX, 0f, s.rootZ); }
                else if (okR) { mode = Mode.Merge; s = new SongManager.MergeSlot { target = R, east = false, gap = 0f, rootX = R.px - Width, rootZ = R.pz }; root = new Vector3(s.rootX, 0f, s.rootZ); }
                else if (glued) mode = Mode.Blocked;
                else
                {
                    mode = Mode.Insert; insertAt = b;
                    root = new Vector3(Mathf.Round((bx - off.x) / snap) * snap, 0f, raw.z);
                    if (b > 0 && b < nc && sm.Route != null && sm.Route.SegmentCount > 0) { gapSeg = b - 1; gapAt = root + off + new Vector3(0f, 0.15f - ProjectConfig.HubHeight * 0.5f, -(Depth * 0.5f + 0.9f)); }
                }
            }
        }
        var route = sm != null ? sm.Route : null;
        if (route != null)
        {
            if (mode == Mode.Insert) { route.ShowGap(gapSeg, gapAt); ownsGap = true; }
            else if (ownsGap) { route.HideGap(); ownsGap = false; }
            if (mode == Mode.Merge && s.target != null)
            {
                float edge = s.east ? s.rootX - KeyBlock.EdgeInset : s.target.WestEdge;   // v5: east of a belted island = at its belt's end
                route.ShowMergeSheet(new Vector3(edge, 0f, s.target.FrontEdge), s.target.Depth, Color.Lerp(s.target.chordColor, chord, 0.5f));
                ownsSheet = true;
            }
            else if (ownsSheet) { route.HideMergeSheet(); ownsSheet = false; }
        }
        if (mode != State || (mode == Mode.Merge && (s.target != slot.target || s.east != slot.east)))
        {
            if (mode == Mode.Merge) AudioPool.UI(ProceduralAudio.Sparkle(), 0.24f, 1.35f);
            else if (mode == Mode.Insert) AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.5f);
            else if (mode == Mode.Blocked) AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.7f);
            else if (mode == Mode.Stack) AudioPool.UI(ProceduralAudio.Tick(), 0.24f, 1.2f);
        }
        State = mode; InsertAt = insertAt; Column = column; slot = s;
        Root = root;
        if (!hasShown) { shown = root; hasShown = true; }
    }

    /// <summary>The ghost's root z in column <paramref name="col"/>: the empty slot nearest <paramref name="rawZ"/> — above the top island, below the bottom
    /// one, or in a gap between two islands wide enough (depth + 2 LaneGap) — LaneGap clear of its neighbours.</summary>
    float StackSlot(SongManager sm, int col, float rawZ)
    {
        var list = sm.ColumnIslands(col);
        list.Sort((a, b) => a.FrontEdge.CompareTo(b.FrontEdge));
        float g = ProjectConfig.LaneGap, e = KeyBlock.EdgeInset, d = Depth;
        float best = rawZ, bestD = float.MaxValue;
        System.Action<float> consider = z => { float dd = Mathf.Abs(z - rawZ); if (dd < bestD) { bestD = dd; best = z; } };
        if (list.Count == 0) return rawZ;
        consider(list[list.Count - 1].BackEdge + g + e);                 // above the top island
        consider(list[0].FrontEdge - g - d + e);                         // below the bottom one
        for (int k = 0; k + 1 < list.Count; k++)
        {
            float lo = list[k].BackEdge + g, hi = list[k + 1].FrontEdge - g;
            if (hi - lo >= d) consider(Mathf.Clamp(rawZ - e, lo, hi - d) + e);
        }
        return best;
    }

    /// <summary>The island of column <paramref name="col"/> in the ghost's lane (front-back overlap ≥ MergeOverlap with the ghost at <paramref name="rootZ"/>), null none.</summary>
    KeyBlock InLane(SongManager sm, int col, float rootZ)
    {
        float f = rootZ - KeyBlock.EdgeInset, b = f + Depth;
        KeyBlock best = null; float bestOv = 0f;
        foreach (var kb in sm.ColumnIslands(col))
        {
            if (kb == null || kb.IsKeyboard != IsKeyboard || kb.IsStairs || kb.IsPhrase) continue;   // v6: MERGE only within a family; v7: never stairs / phrases
            float ov = Mathf.Min(b, kb.BackEdge) - Mathf.Max(f, kb.FrontEdge);
            if (ov >= ProjectConfig.MergeOverlap * Mathf.Min(Depth, kb.Depth) && ov > bestOv) { bestOv = ov; best = kb; }
        }
        return best;
    }

    /// <summary>Hides (pointer back over the HUD) or shows the ghost; hiding also clears its cable-gap / light-sheet previews.</summary>
    public void SetVisible(bool v)
    {
        if (visible == v) return;
        visible = v;
        foreach (var r in rends) if (r != null) r.enabled = v;
        if (!v) ClearPreviews();
    }

    void ClearPreviews()
    {
        var route = SongManager.I != null ? SongManager.I.Route : null;
        if (route != null) { if (ownsGap) route.HideGap(); if (ownsSheet) route.HideMergeSheet(); }
        ownsGap = false; ownsSheet = false;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        age += dt;
        shown = Vector3.Lerp(shown, Root, 1f - Mathf.Exp(-dt * 28f));
        body.position = shown + Vector3.up * (0.28f + 0.05f * Mathf.Sin(age * 3.2f));
        Color rc = State == Mode.Blocked ? Palette.Danger : (State == Mode.Insert ? Amber : (State == Mode.Merge ? Color.Lerp(chord, Color.white, 0.55f) : (State == Mode.Stack ? Color.Lerp(chord, Color.white, 0.75f) : Color.white)));
        rc.a = 0.7f + 0.3f * Mathf.Sin(age * 7f);   // the pulse
        rimMat.SetColor(ColorId, rc);
        if (rim != null) rim.widthMultiplier = State == Mode.Merge ? 0.13f : 0.1f;
    }

    void OnDestroy()
    {
        ClearPreviews();
        foreach (var m in mats) if (m != null) Destroy(m);
        mats.Clear();
    }
}
