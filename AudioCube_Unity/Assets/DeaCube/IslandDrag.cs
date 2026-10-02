using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Island / Moon dragging (SPEC §2.2, §2.3), a state machine driven from PathManager.Update. Armed by a left press on a
/// platform margin or hub with no tile/cube under the cursor: a release without motion is a click (select + glide to frame it,
/// double-click frames tighter + OnHubClicked; F8: a hub click no longer cycles the bars); more than the drag threshold of motion starts a
/// drag (EditorState.DraggingIsland). The drag hum sounds only while the clock is stopped.
/// v4 (SPEC v4 §3 K5) — three kinds of drag:
/// - InColumn (a press on an island's platform margin = PathManager.HoverGround, or the island header's grip = <see cref="Grip"/>): x stays in
///   the column, z follows the pointer with the grab offset (snapped 0.5 u, clamped to the allowed range shown by the column band); the column's
///   other islands are pushed away and swap sides when the dragged island's centre passes theirs (SongManager.MoveIslandInColumn preview); a
///   group member moves its whole group (each member in its own column). Pulling sideways toward an island of the adjacent column in the same
///   lane offers the merge (light sheet + magnet); the release merges (SongManager.MergeIslands) or commits the move (one History entry).
/// - Column (a press on an anchor's HUB): the whole column follows the pointer (x and z, lifted), its islands leave their groups (tear-out);
///   dropping the hub on a Route segment that does not touch the column reorders the columns (SongManager.MoveColumn); an island's edge near
///   another island's outer end opens the merge zone (the release merges); elsewhere the column goes back to its x and keeps the z shift.
/// - Moon: the v2/v3 free drag (push-out on overlap).
/// Esc / right-click cancels (everything goes back, no History). Hovering a seam (two merged islands of adjacent columns) shows the scissors at
/// its front end; clicking them splits the group there.
/// v5 (SPEC v5 §2.6, the user: "you should be able to drag grids to the left and right too not just up and down so if i want to move a grid to
/// an earlier section i can do that"): the platform / grip drag (Kind.InColumn) is FREE — the island (its merge group as a block) follows the
/// pointer in x and z, lifted, with a soft shadow on the sea, and the drop slot is decided continuously and previewed in the world (<see cref="Slot"/>):
/// its centre over another column's x span (inset IslandGhost.StackInset, belts included) → JOIN that column at the pointer's z (the column's
/// islands make room, its band lights up); in the gap between two columns / before the first / after the last → a NEW column there (the two
/// neighbours part, ColumnBands draws the insertion slit); its edge against another island's outer end in the same lane → the v3 MERGE (light
/// sheet + magnet); back over its own column → today's in-column move (push / swap, the range band). The release commits through
/// SongManager.MoveIslandToColumn / MergeIslands / MoveIslandInColumn (one History entry); Esc / right-click puts everything back.
/// v6: keyboard islands drag like any island; their merge zone only opens against keyboards (one family per group).
/// v6 §11.3: a dragged Moon lights the band of the column it is in line with (<see cref="MoonColumn"/>, the band stretching out to the Moon); the
/// release makes that column its section's start (SongManager.SetMoonColumn: it settles in its drum lane, one History entry).
/// v7: a column is its measures wide (SongManager.ColumnWidth) — the drop slots measure each column's own span; inside a section the columns touch,
/// so a NEW column slot between two of them is the boundary zone (StackInset either side: "a column inserted inside a section joins it"); stairs
/// and phrases never offer the merge.
/// </summary>
public class IslandDrag
{
    public enum Phase { Idle, Armed, Dragging, Sliding }
    /// <summary>v4: what the current drag moves.</summary>
    public enum Kind { None, InColumn, Column, Moon }
    public Phase State { get; private set; }
    public Kind DragKind { get; private set; }
    public KeyBlock Block { get; private set; }
    /// <summary>True while the pointer is committed to an island (drag, push-out slide, or the frame of a scissors click): PathManager suppresses picking.</summary>
    public bool Busy => State == Phase.Dragging || State == Phase.Sliding || Time.frameCount <= consumeFrame;
    public bool Dragging => State == Phase.Dragging;
    public int GapSegment => gapSeg;
    /// <summary>v3: the island whose outer end offers the merge while dragging (null otherwise) and its side.</summary>
    public KeyBlock MergeTarget => State == Phase.Dragging && inZone ? slot.target : null;
    public bool MergeEast => slot.east;
    /// <summary>v3: this drag tore its island(s) out of a group (hub drag).</summary>
    public bool TornOut => tornOut;
    /// <summary>v3: the islands moving with this drag (InColumn: the grabbed island's group; Column: the column's islands; Moon: itself).</summary>
    public IList<KeyBlock> Moving => members;
    /// <summary>v3: nearest platform-edge gap (x/z) from the dragged block to any other island (onboarding: "push together to merge"); +inf when idle.</summary>
    public float NearestGap { get; private set; } = float.PositiveInfinity;
    /// <summary>v3: the seam under the pointer (the island index of its west member), -1 none.</summary>
    public int HoverSeam => hoverSeam;
    /// <summary>v4: the allowed z range of the grabbed island's root during an in-column drag (the column band shows it).</summary>
    public float RangeLo { get; private set; }
    public float RangeHi { get; private set; }
    /// <summary>v4 (tests): times a neighbour swapped sides during the current in-column drag.</summary>
    public int Swaps { get; private set; }
    /// <summary>v5: where a free island drag would drop: its own column (in-column move), JOIN another column, a NEW column, or MERGE (the magnet).</summary>
    public enum DropSlot { None, Own, Join, NewColumn, Merge }
    /// <summary>v5: the current drop slot of a free island drag (None otherwise).</summary>
    public DropSlot Slot { get; private set; }
    /// <summary>v5: the column a JOIN targets, or the column position a NEW column takes (0 = the front … ColumnCount = the end); -1 otherwise.</summary>
    public int SlotColumn { get; private set; } = -1;
    /// <summary>v5: the islands shown moved by the slot preview (the target column making room, the neighbours parting).</summary>
    public int PreviewCount => previewed.Count;
    /// <summary>v6 §11.3: the column a dragged Moon is in line with (its release starts the Moon's section there); -1 otherwise.</summary>
    public int MoonColumn { get; private set; } = -1;

    readonly PathManager pm;
    bool armedOnHub; Vector3 pressPx; Vector3 grabOffset; Vector3 startBase, shown; Vector2 velDir; float humT; int gapSeg = -1;
    readonly List<KeyBlock> members = new List<KeyBlock>(); readonly List<Vector3> memberStart = new List<Vector3>(); readonly List<int> memberGroup = new List<int>();
    int blockAt;
    bool tornOut; int tornGroup;
    SongManager.MergeSlot slot; bool inZone; float magnet; Vector3 slotBase;
    Vector3 slideFrom, slideTo; float slideT;
    float lastClickT = -10f; int lastClickKey = int.MinValue;
    int hoverSeam = -1, consumeFrame = -1, simSeamFrame = -1;
    bool simActive;
    // free (v5) / in-column drag state
    int blockIndex = -1; float lastZ; readonly List<bool> sideAbove = new List<bool>();
    bool ownSession; Vector3 lastRaw, snapOrigin; readonly List<KeyBlock> previewed = new List<KeyBlock>();
    readonly Dictionary<KeyBlock, float> joinZ = new Dictionary<KeyBlock, float>(); readonly HashSet<KeyBlock> previewNow = new HashSet<KeyBlock>();
    readonly List<KeyBlock> excludeBuf = new List<KeyBlock>(); readonly List<KeyBlock> colBuf = new List<KeyBlock>();
    readonly List<float> colX0 = new List<float>();   // the columns' x when the drag began (a dragged anchor carries its column's x along)
    /// <summary>How far the two columns beside a new column's slot part while it is previewed.</summary>
    public const float PartOffset = 1.6f;

    public IslandDrag(PathManager owner) { pm = owner; }

    static SongManager SM => SongManager.I;

    /// <summary>A left press landed on <paramref name="kb"/>'s platform or hub with nothing in front of it.</summary>
    public void Arm(KeyBlock kb, bool onHub, Vector3 mousePx)
    {
        if (kb == null || Busy) return;
        Block = kb; armedOnHub = onHub; pressPx = mousePx; State = Phase.Armed;
    }

    /// <summary>v4: the island header's grip was pressed (U1): arms an in-column drag of <paramref name="kb"/> at the pointer; motion past the drag
    /// threshold starts it, a release in place is a click (select + frame).</summary>
    public void Grip(KeyBlock kb) { Arm(kb, false, Input.mousePosition); }

    /// <summary>Esc / right-click / a world lock: puts the islands back where the drag started (no History); torn-out members rejoin.</summary>
    public void Cancel()
    {
        if (State == Phase.Dragging && Block != null)
        {
            if (DragKind == Kind.InColumn)
            {
                foreach (var m in members) if (m != null) m.SetDragLift(0f, Vector2.zero);
                if (SM != null)
                {
                    if (ownSession) SM.CancelIslandMove();
                    ownSession = false;
                    for (int k = 0; k < members.Count; k++) if (members[k] != null) SM.MoveIsland(members[k], memberStart[k].x, memberStart[k].z);
                }
                ClearPreview();
            }
            else
            {
                for (int k = 0; k < members.Count; k++) if (members[k] != null) { members[k].SetDragLift(0f, Vector2.zero); if (SM != null) SM.MoveIsland(members[k], memberStart[k].x, memberStart[k].z); }
                if (tornOut) { for (int k = 0; k < members.Count && k < memberGroup.Count; k++) if (members[k] != null) members[k].group = memberGroup[k]; if (SM != null) SM.RefreshGroupLook(); }
            }
            if (SM != null && SM.Route != null) { SM.Route.HideGap(); SM.Route.HideMergeSheet(); }
        }
        else if (State == Phase.Sliding && Block != null)
        {
            MoveMembers(slideTo);   // a push-out in progress lands where it was going (its release already decided the move)
            Commit();
            return;
        }
        if (pm.currentState == PathManager.EditorState.DraggingIsland) pm.currentState = PathManager.EditorState.Idle;
        ResetState();
    }

    void ResetState()
    {
        State = Phase.Idle; Block = null; DragKind = Kind.None; members.Clear(); memberStart.Clear(); memberGroup.Clear(); planeY = 0f;
        tornOut = false; gapSeg = -1; inZone = false; magnet = 0f; simActive = false; NearestGap = float.PositiveInfinity; blockIndex = -1;
        ownSession = false; Slot = DropSlot.None; SlotColumn = -1; MoonColumn = -1;
        ClearPreview();
        if (SM != null && SM.Route != null) { SM.Route.HideGap(); SM.Route.HideMergeSheet(); }   // S6: never a preview left behind (undo / rebuild mid-drag)
        ColumnBands.ClearRange(); ColumnBands.HideSlit(); ColumnBands.HideShadow(); ColumnBands.UnpinColumns();
        colX0.Clear();
    }

    public void Update(Camera cam)
    {
        if (Block == null && State != Phase.Idle) { ResetState(); if (pm.currentState == PathManager.EditorState.DraggingIsland) pm.currentState = PathManager.EditorState.Idle; return; }
        switch (State)
        {
            case Phase.Armed: UpdateArmed(cam); break;
            case Phase.Dragging: UpdateDragging(cam); break;
            case Phase.Sliding: UpdateSliding(); break;
        }
        if (State == Phase.Idle) { if (Time.frameCount > simSeamFrame) UpdateSeams(cam); }
        else if (SM != null && SM.Route != null) { SM.Route.HideScissors(); hoverSeam = -1; }
    }

    // ------------------------------------------------------------------ armed: click or begin drag
    void UpdateArmed(Camera cam)
    {
        if (Input.GetMouseButtonUp(0)) { Click(); return; }
        if (!Input.GetMouseButton(0)) { State = Phase.Idle; Block = null; return; }
        if ((Input.mousePosition - pressPx).magnitude > OrbitCamera.DragThresholdPx)
        {
            Vector3 g;
            planeY = Block != null ? Block.GroundY : 0f;   // v7: the pointer moves on the ground the island stands on (no parallax on a terrace)
            BeginDrag(GroundAt(cam, pressPx, out g) ? g : new Vector3(Block.px, 0f, Block.pz));
        }
    }

    void Click()
    {
        var kb = Block; State = Phase.Idle; Block = null;
        if (kb == null || SM == null) return;
        int key = kb.IsMoon ? -1 - SM.Moons.IndexOf(kb) : SM.Islands.IndexOf(kb);
        bool dbl = key == lastClickKey && Time.unscaledTime - lastClickT < OrbitCamera.DoubleClickSeconds;   // the second half of a double-click frames tighter and does not cycle again
        lastClickT = dbl ? -10f : Time.unscaledTime; lastClickKey = key;
        if (!kb.IsMoon)
        {
            int idx = SM.Islands.IndexOf(kb);
            if (idx < 0) return;
            if (UIManager.I != null) UIManager.I.SelectMeasure(idx, false);   // F8: a hub click selects + frames only (bars live in the measure bar)
        }
        if (kb != null && OrbitCamera.I != null) OrbitCamera.I.FrameIsland(kb, dbl);   // SPEC v3 §3.4 island focus (group aware)
        if (kb != null) pm.RaiseHubClicked(kb);
    }

    void BeginDrag(Vector3 pressGround)
    {
        var kb = Block;
        members.Clear(); memberStart.Clear(); memberGroup.Clear(); tornOut = false; Swaps = 0;
        var sm = SM;
        int idx = sm != null ? sm.Islands.IndexOf(kb) : -1;
        if (kb.IsMoon || sm == null || idx < 0) { DragKind = Kind.Moon; members.Add(kb); }
        else if (armedOnHub && kb.IsAnchor)
        {
            // the anchor's hub moves its whole column (tear-out of every group it belongs to)
            DragKind = Kind.Column;
            sm.ColumnIslands(kb.column, members);
            foreach (var m in members) memberGroup.Add(m.group);
            TearOut(kb);
        }
        else
        {
            // v5: the free drag (the block starts over its own column: the in-column session is live until it leaves it)
            DragKind = Kind.InColumn;
            members.AddRange(sm.GroupOf(kb));
            blockIndex = idx;
            sm.BeginIslandMove(idx); ownSession = true;
            RangeLo = sm.IslandMoveLo; RangeHi = sm.IslandMoveHi;
            sideAbove.Clear(); float cz0 = kb.Center.z; foreach (var o in sm.Islands) sideAbove.Add(o != null && o.Center.z > cz0);
            Slot = DropSlot.Own; SlotColumn = kb.column;
            colX0.Clear(); for (int c = 0; c < sm.ColumnCount; c++) colX0.Add(sm.ColumnX(c));
            ColumnBands.PinColumns();
        }
        blockAt = Mathf.Max(0, members.IndexOf(kb));
        foreach (var m in members) memberStart.Add(new Vector3(m.px, 0f, m.pz));
        startBase = new Vector3(kb.px, 0f, kb.pz);
        // v5: an island riding its belt (playing, pass p) is grabbed where it is shown: it leaves the belt and its logical root moves there
        Vector3 ride = kb.IsMoon ? new Vector3(kb.MoonRideX, 0f, 0f) : new Vector3(kb.BeltOffset.x, 0f, 0f);   // v6 §11: a following Moon too
        foreach (var m in members) if (m != null) m.ResetRide();
        shown = startBase + ride; lastZ = kb.pz; lastRaw = shown; snapOrigin = shown;
        grabOffset = startBase + ride - new Vector3(pressGround.x, 0f, pressGround.z);
        if (ride.sqrMagnitude > 1e-6f) MoveMembers(startBase + ride);
        velDir = Vector2.zero; humT = 0.2f; gapSeg = -1; inZone = false; magnet = 0f; NearestGap = float.PositiveInfinity;
        State = Phase.Dragging;
        pm.currentState = PathManager.EditorState.DraggingIsland;
        if (OrbitCamera.I != null) OrbitCamera.I.overview = false;   // F9: the overview would chase the song bounds the drag is changing
        foreach (var m in members) if (m != null) m.SetDragLift(1f, Vector2.zero);
        if (DragKind == Kind.InColumn) ColumnBands.ShowRange(kb.column, RangeLo - KeyBlock.EdgeInset, RangeHi - KeyBlock.EdgeInset + kb.Depth);
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 0.9f);
    }

    /// <summary>A hub drag: the column's islands leave their groups now (the look follows at once; the layout settles on release), spark lines at the seams.</summary>
    void TearOut(KeyBlock anchor)
    {
        var sm = SM;
        bool any = false;
        foreach (var m in members)
        {
            if (m == null || m.group == 0) continue;
            var left = sm.GroupNeighbour(m, -1); var right = sm.GroupNeighbour(m, +1);
            Color c = Color.Lerp(m.chordColor, Color.white, 0.5f);
            for (int side = 0; side < 2; side++)
            {
                if ((side == 0 && left == null) || (side == 1 && right == null)) continue;
                float x = side == 0 ? m.WestEdge : m.EastEdge;
                for (int s = 0; s < 4; s++) Fx.Burst(new Vector3(x, 0.15f, m.FrontEdge + m.Depth * (s + 0.5f) / 4f), c, 5, 1.6f);
            }
            if (m == anchor) tornGroup = m.group;
            m.group = 0; any = true;
        }
        if (!any) return;
        tornOut = true;
        sm.RefreshGroupLook();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.3f, 1.45f);      // a reversed zip: UI sounds only (harmonic safety)
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.28f, 0.7f);
    }

    // ------------------------------------------------------------------ dragging
    void UpdateDragging(Camera cam)
    {
        if (simActive) return;   // a test drives this drag (SimMove / SimRelease)
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { Cancel(); return; }
        if (!Input.GetMouseButton(0)) { Release(); return; }
        if (OrbitCamera.I != null) OrbitCamera.I.NotifyInput();
        Vector3 g;
        if (GroundAt(cam, Input.mousePosition, out g)) DragTo(g, false);
    }

    void DragTo(Vector3 g, bool settle)
    {
        var kb = Block;
        if (kb == null || SM == null) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        Vector3 want = g + grabOffset;
        float snap = ProjectConfig.IslandSnap;
        Vector3 raw = new Vector3(Mathf.Round(want.x / snap) * snap, 0f, Mathf.Round(want.z / snap) * snap);
        // v5 free drag: the displacement snaps (the block stays on its own grid: a column whose x is off the 0.5 grid is not nudged by a drag that
        // did not move sideways); the commits snap where they land
        if (DragKind == Kind.InColumn) raw = snapOrigin + new Vector3(Mathf.Round((want.x - snapOrigin.x) / snap) * snap, 0f, Mathf.Round((want.z - snapOrigin.z) / snap) * snap);

        // merge zone: the moving block's platform edge near another island's outer end (SPEC v3 §3.3, v4 §3 K4)
        UpdateMerge(raw);
        float goal = inZone ? 1f : 0f;
        magnet = settle ? goal : Mathf.Lerp(magnet, goal, 1f - Mathf.Exp(-dt * (inZone ? 16f : 22f)));
        if (!inZone && magnet < 0.01f) magnet = 0f;
        if (DragKind == Kind.InColumn) DragFree(raw, dt, settle);
        else
        {
            Vector3 target = magnet > 0f ? Vector3.Lerp(raw, slotBase, magnet) : raw;
            if ((target - shown).sqrMagnitude > 1e-6f)
            {
                Vector3 v = (target - shown) / dt;
                velDir = Vector2.Lerp(velDir, new Vector2(v.x, v.z) / 10f, 0.35f);
                MoveMembers(target);
                shown = target;
            }
            else velDir = Vector2.Lerp(velDir, Vector2.zero, 1f - Mathf.Exp(-dt * 8f));
            if (DragKind == Kind.Moon) ShowMoonColumn(kb);
        }
        bool single = members.Count == 1;
        foreach (var m in members) if (m != null) m.SetDragLift(1f, single ? velDir : Vector2.zero);

        // harmonic-safe hum: the pad plays the island's root only while the clock is stopped (§2.2)
        if (!kb.IsMoon && !GlobalClock.IsPlaying && GlobalClock.SongBeatD <= 1e-6 && Synth.Ready)
        {
            humT += dt;
            if (humT >= 0.25f) { humT = 0f; Synth.Preview(2, SynthBank.ClampToRegister(2, kb.chordRootMIDI + 12 * kb.register), 60, 0.25f); }
        }
        if (DragKind == Kind.Column) UpdateGap(kb);
    }

    // ------------------------------------------------------------------ v5 free drag (SPEC v5 §2.6)
    /// <summary>How deep the block may overlap an island's platform and still be offered the merge with it (deeper = dropped over a column / gap).</summary>
    public const float MergeMaxOverlap = 0.5f;

    /// <summary>One frame of the free drag: the block follows <paramref name="raw"/> (the grabbed island's snapped root under the pointer), the drop
    /// slot is decided (the merge zone wins) and previewed.</summary>
    void DragFree(Vector3 raw, float dt, bool settle)
    {
        var sm = SM; var kb = Block;
        lastRaw = raw;
        int col = -1;
        DropSlot want = inZone ? DropSlot.Merge : DecideSlot(raw, out col);
        bool changed = want != Slot || col != SlotColumn;
        if (changed) SlotChanged(want, col);
        Slot = want; SlotColumn = col;
        Vector3 target = raw;
        if (want == DropSlot.Own)
        {
            // today's in-column move: z clamped to the range (the column's others pushed / swapped), x free (it snaps back on release)
            bool fresh = !ownSession;
            if (fresh)
            {
                // back over its column: the session restarts from where the drag began (the same range as at the start)
                for (int k = 0; k < members.Count; k++) if (members[k] != null) sm.MoveIsland(members[k], members[k].px, memberStart[k].z);
                sm.BeginIslandMove(blockIndex); ownSession = true; RangeLo = sm.IslandMoveLo; RangeHi = sm.IslandMoveHi; ResetSides();
            }
            float z = Mathf.Clamp(raw.z, RangeLo, RangeHi);
            if (fresh || settle || Mathf.Abs(z - lastZ) > 1e-4f)
            {
                sm.MoveIslandInColumn(blockIndex, z, false);
                lastZ = z;
                CountSwaps();
            }
            float dx = raw.x - startBase.x;
            for (int k = 0; k < members.Count; k++) if (members[k] != null && Mathf.Abs(members[k].px - (memberStart[k].x + dx)) > 1e-4f) sm.MoveIsland(members[k], memberStart[k].x + dx, members[k].pz);
            target = new Vector3(raw.x, 0f, z);
            ColumnBands.ShowRange(kb.column, RangeLo - KeyBlock.EdgeInset, RangeHi - KeyBlock.EdgeInset + kb.Depth);
            ClearPreview();
        }
        else
        {
            if (ownSession) { sm.CancelIslandMove(); ownSession = false; }
            if (want == DropSlot.Merge && magnet > 0f) target = Vector3.Lerp(raw, slotBase, magnet);
            if ((target - new Vector3(kb.px, 0f, kb.pz)).sqrMagnitude > 1e-6f) MoveMembers(target);
            if (want == DropSlot.Join) PreviewJoin(col);
            else if (want == DropSlot.NewColumn) PreviewNewColumn(col);
            else ClearPreview();
        }
        if ((target - shown).sqrMagnitude > 1e-6f) { Vector3 v = (target - shown) / dt; velDir = Vector2.Lerp(velDir, new Vector2(v.x, v.z) / 10f, 0.35f); shown = target; }
        else velDir = Vector2.Lerp(velDir, Vector2.zero, 1f - Mathf.Exp(-dt * 8f));
        UpdateShadow();
    }

    /// <summary>
    /// The drop slot of the free drag at <paramref name="raw"/>: the block's west member's centre over column c's span (its platform plus its
    /// belts, inset IslandGhost.StackInset on a side that is not glued) → Own (its own column) or Join c; otherwise the column position b between the
    /// columns → NewColumn b (inside a glued run there is no gap: the nearer column; the block alone in its own columns dropped right beside them =
    /// the same order: Own).
    /// </summary>
    DropSlot DecideSlot(Vector3 raw, out int col)
    {
        var sm = SM; col = -1;
        int nc = sm.ColumnCount;
        if (nc == 0 || members.Count == 0 || members[0] == null) return DropSlot.None;
        float e = KeyBlock.EdgeInset, inset = IslandGhost.StackInset;
        float cx = memberStart[0].x + (raw.x - startBase.x) + (members[0].Width * 0.5f - e);
        int own = members[0].column, count = members.Count;
        for (int c = 0; c < nc; c++)
        {
            float west = ColX(c) - e, east = west + sm.ColumnWidth(c) + sm.BeltExtent(c);   // v7: the column's own measures
            float inW = sm.Glued(c - 1) ? 0f : inset, inE = sm.Glued(c) ? 0f : inset;
            if (cx >= west + inW && cx <= east - inE) { col = c; return c == own ? DropSlot.Own : DropSlot.Join; }
        }
        int b = 0;
        while (b < nc && ColX(b) - e + (sm.ColumnWidth(b) + sm.BeltExtent(b)) * 0.5f < cx) b++;
        if (b > 0 && b < nc && sm.Glued(b - 1))
        {
            // no gap inside a glued run: the nearer of the two columns
            float dl = Mathf.Abs(cx - (ColX(b - 1) - e + (sm.ColumnWidth(b - 1) + sm.BeltExtent(b - 1)) * 0.5f)), dr = Mathf.Abs(cx - (ColX(b) - e + (sm.ColumnWidth(b) + sm.BeltExtent(b)) * 0.5f));
            col = dl <= dr ? b - 1 : b;
            return col == own ? DropSlot.Own : DropSlot.Join;
        }
        bool alone = true;
        foreach (var m in members) if (m == null || sm.ColumnSize(m.column) != 1) { alone = false; break; }
        if (alone && (b == own || b == own + count)) { col = own; return DropSlot.Own; }
        col = b;
        return DropSlot.NewColumn;
    }

    /// <summary>Column <paramref name="c"/>'s x as the drag began (live when the snapshot does not cover it).</summary>
    float ColX(int c) => c >= 0 && c < colX0.Count ? colX0[c] : (SM != null ? SM.ColumnX(c) : 0f);

    void SlotChanged(DropSlot want, int col)
    {
        if (want != DropSlot.Own) ColumnBands.ClearRange();
        if (want != DropSlot.NewColumn) ColumnBands.HideSlit();
        if (want == DropSlot.Join) AudioPool.UI(ProceduralAudio.Tick(), 0.26f, 1.2f);
        else if (want == DropSlot.NewColumn) AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.5f);
        else if (want == DropSlot.Own) AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.95f);
    }

    /// <summary>JOIN preview: the target column(s) make room where the block would land (the commit's own z resolution), its band lights up over
    /// the joining island's lane.</summary>
    void PreviewJoin(int col)
    {
        var sm = SM;
        previewNow.Clear();
        int nc = sm.ColumnCount;
        float zMin = float.MaxValue, zMax = float.MinValue;
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k]; int c = col + k;
            if (m == null || c >= nc) break;
            int mi = sm.Islands.IndexOf(m);
            sm.JoinPreviewZ(mi, c, m.pz, joinZ);
            foreach (var kv in joinZ)
            {
                var o = kv.Key; if (o == null || members.Contains(o)) continue;
                o.SetPreviewOffset(new Vector3(0f, 0f, kv.Value - o.pz));
                previewNow.Add(o);
                if (!previewed.Contains(o)) previewed.Add(o);
            }
            if (k == 0) { zMin = m.FrontEdge; zMax = m.BackEdge; }
        }
        DropStalePreviews();
        if (zMin < zMax) ColumnBands.ShowRange(col, zMin, zMax);
    }

    /// <summary>NEW column preview: the two columns beside the slot part (PartOffset each way) and the insertion slit shows between them.</summary>
    void PreviewNewColumn(int b)
    {
        var sm = SM;
        previewNow.Clear();
        int nc = sm.ColumnCount;
        float e = KeyBlock.EdgeInset;
        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var m in members) if (m != null) { zMin = Mathf.Min(zMin, m.FrontEdge); zMax = Mathf.Max(zMax, m.BackEdge); }
        for (int side = 0; side < 2; side++)
        {
            int c = side == 0 ? b - 1 : b;
            if (c < 0 || c >= nc) continue;
            sm.ColumnIslands(c, colBuf);
            foreach (var o in colBuf)
            {
                if (o == null || members.Contains(o)) continue;
                o.SetPreviewOffset(new Vector3(side == 0 ? -PartOffset : PartOffset, 0f, 0f));
                previewNow.Add(o);
                if (!previewed.Contains(o)) previewed.Add(o);
                zMin = Mathf.Min(zMin, o.FrontEdge); zMax = Mathf.Max(zMax, o.BackEdge);
            }
        }
        DropStalePreviews();
        float left = b > 0 ? ColX(b - 1) - e + sm.ColumnWidth(b - 1) + sm.BeltExtent(b - 1) : ColX(0) - e - ProjectConfig.SectionGap;
        float right = b < nc ? ColX(b) - e : ColX(nc - 1) - e + sm.ColumnWidth(nc - 1) + sm.BeltExtent(nc - 1) + ProjectConfig.SectionGap;
        ColumnBands.ShowSlit((left + right) * 0.5f, zMin - ProjectConfig.BandPadZ, zMax + ProjectConfig.BandPadZ);
    }

    void DropStalePreviews()
    {
        for (int i = previewed.Count - 1; i >= 0; i--)
        {
            var o = previewed[i];
            if (o != null && previewNow.Contains(o)) continue;
            if (o != null) o.SetPreviewOffset(Vector3.zero);
            previewed.RemoveAt(i);
        }
    }

    /// <summary>Every preview offset eases back to zero (a slot left, a cancel, a release).</summary>
    void ClearPreview()
    {
        foreach (var o in previewed) if (o != null) o.SetPreviewOffset(Vector3.zero);
        previewed.Clear();
    }

    /// <summary>The dragged block's soft shadow on the sea (its platforms' rectangle where they are dropped).</summary>
    void UpdateShadow()
    {
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var m in members) { if (m == null) continue; x0 = Mathf.Min(x0, m.WestEdge); x1 = Mathf.Max(x1, m.EastEdge); z0 = Mathf.Min(z0, m.FrontEdge); z1 = Mathf.Max(z1, m.BackEdge); }
        if (x0 < x1) ColumnBands.ShowShadow(Rect.MinMaxRect(x0 - 0.3f, z0 - 0.3f, x1 + 0.3f, z1 + 0.3f));
    }

    /// <summary>The column-mates' sides of the grabbed island (swap counting restarts when the block comes back over its column).</summary>
    void ResetSides()
    {
        var sm = SM; if (sm == null || blockIndex < 0 || blockIndex >= sm.Islands.Count) return;
        float cz = sm.Islands[blockIndex].Center.z;
        sideAbove.Clear(); foreach (var o in sm.Islands) sideAbove.Add(o != null && o.Center.z > cz);
    }

    /// <summary>In-column drags: a column-mate that changed sides of the dragged island (centre order) = one swap (tests, a soft tick).</summary>
    void CountSwaps()
    {
        var sm = SM; if (sm == null || blockIndex < 0 || blockIndex >= sm.Islands.Count) return;
        var kb = sm.Islands[blockIndex];
        float cz = kb.Center.z;
        for (int i = 0; i < sm.Islands.Count && i < sideAbove.Count; i++)
        {
            var o = sm.Islands[i];
            if (o == null || o == kb || o.column != kb.column) continue;
            bool nowAbove = o.Center.z > cz;
            if (nowAbove != sideAbove[i]) { Swaps++; sideAbove[i] = nowAbove; AudioPool.UI(ProceduralAudio.Tick(), 0.22f, nowAbove ? 1.3f : 0.9f); }
        }
    }

    void UpdateMerge(Vector3 raw)
    {
        var sm = SM;
        if (sm == null || Block == null || Block.IsMoon || Block.IsStairs || Block.IsPhrase || members.Count == 0) { SetZone(false); NearestGap = float.PositiveInfinity; return; }   // v7: stairs / phrases never merge
        SongManager.MergeSlot s;
        bool found;
        if (DragKind == Kind.InColumn)
        {
            // v5 free drag: the probe is the block under the pointer; any island outside its own columns qualifies when the block is pushed
            // against its outer end (not dropped deep over it: that is a join / a new column)
            Vector3 d = raw - startBase;
            var first = members[0]; var last = members[members.Count - 1];
            Vector3 root = memberStart[0] + d;
            float bw = 0f; foreach (var m in members) if (m != null) bw += m.Width;   // v7: the block's own measures
            var p = SongManager.ProbeAt(root.x, root.z, members.Count, first.Depth, last.Depth, bw);
            p.keyboard = Block.IsKeyboard;   // v6: a keyboard block merges only with keyboards
            NearestGap = EdgeGap(p);
            excludeBuf.Clear(); excludeBuf.AddRange(members);
            foreach (var m in members) { if (m == null) continue; sm.ColumnIslands(m.column, colBuf); excludeBuf.AddRange(colBuf); }
            found = sm.FindMerge(p, excludeBuf, MergeMaxOverlap, out s);
            if (found) { slotBase = new Vector3(s.rootX, 0f, s.rootZ) + (memberStart[blockAt] - memberStart[0]); }
        }
        else
        {
            // a column drag: the grabbed anchor is the probe; the rest of its column follows it rigidly
            Vector3 d = raw - startBase;
            Vector3 root = memberStart[blockAt] + d;
            var p = SongManager.ProbeAt(root.x, root.z, 1, Block.Depth, Block.Depth, Block.Width);
            p.keyboard = Block.IsKeyboard;
            NearestGap = EdgeGap(p);
            found = sm.FindMerge(p, members, out s);
            if (found) slotBase = new Vector3(s.rootX, 0f, s.rootZ);
        }
        if (found)
        {
            bool changed = !inZone || s.target != slot.target || s.east != slot.east;
            slot = s;
            SetZone(true, changed);
        }
        else SetZone(false);
    }

    /// <summary>Smallest x/z gap between the moving block's platforms and any other island (0 when overlapping).</summary>
    float EdgeGap(SongManager.MergeProbe p)
    {
        float best = float.PositiveInfinity;
        float back = Mathf.Max(p.backWest, p.backEast);
        foreach (var kb in SM.Islands)
        {
            if (kb == null || members.Contains(kb)) continue;
            float dx = Mathf.Max(0f, Mathf.Max(kb.WestEdge - p.east, p.west - kb.EastEdge));
            float dz = Mathf.Max(0f, Mathf.Max(kb.FrontEdge - back, p.front - kb.BackEdge));
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }

    void SetZone(bool on, bool changed = false)
    {
        var route = SM != null ? SM.Route : null;
        if (on && (!inZone || changed)) AudioPool.UI(ProceduralAudio.Sparkle(), 0.24f, 1.35f);   // the rising shimmer (UI only: harmonic safety)
        inZone = on;
        if (route == null) return;
        if (on && slot.target != null)
        {
            var t = slot.target;
            float edge = slot.east ? slot.rootX - KeyBlock.EdgeInset : t.WestEdge;   // v5: east of a belted island = at its belt's end
            route.ShowMergeSheet(new Vector3(edge, 0f, t.FrontEdge), t.Depth, Color.Lerp(t.chordColor, Block.chordColor, 0.5f));
        }
        else route.HideMergeSheet();
    }

    void UpdateGap(KeyBlock kb)
    {
        var route = SM != null ? SM.Route : null;
        int newGap = -1;
        if (!inZone && !kb.IsMoon && route != null && route.SegmentCount >= 3)
        {
            int c = kb.column;
            int seg; float dist; Vector3 closest;
            if (route.NearestSegment(kb.HubPos, c, out seg, out dist, out closest) && dist <= ProjectConfig.DropOnWireDist) newGap = seg;   // never its own cables
        }
        if (newGap != gapSeg)
        {
            gapSeg = newGap;
            if (route != null) { if (gapSeg >= 0) route.ShowGap(gapSeg, kb.HubUnderside); else route.HideGap(); }
            if (gapSeg >= 0) AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.5f);
        }
        else if (gapSeg >= 0 && route != null) route.ShowGap(gapSeg, kb.HubUnderside);
    }

    void Release()
    {
        var kb = Block;
        foreach (var m in members) if (m != null) m.SetDragLift(0f, Vector2.zero);
        pm.currentState = PathManager.EditorState.Idle;
        simActive = false;
        var sm = SM;
        if (sm == null || kb == null) { ResetState(); return; }
        if (sm.Route != null) { sm.Route.HideGap(); sm.Route.HideMergeSheet(); }
        var kind = DragKind;

        // merge: the release inside the zone (SPEC v3 §3.3, v4 §3 K4). The logical positions go back first (the layout's X[0] is the first
        // island's px): the rebuild then glides the moved islands in from where the drag left them
        if (inZone && slot.target != null && !kb.IsMoon)
        {
            var target = slot.target; bool east = slot.east;
            var list = new List<KeyBlock>(members); var starts = new List<Vector3>(memberStart);
            var dropped = new List<Vector3>(); foreach (var m in list) dropped.Add(m != null ? new Vector3(m.px, 0f, m.pz) : Vector3.zero);
            ResetState();
            if (kind == Kind.InColumn) sm.CancelIslandMove();
            for (int k = 0; k < list.Count; k++) if (list[k] != null) { sm.MoveIsland(list[k], starts[k].x, starts[k].z); sm.ShownAt(list[k], dropped[k]); }
            if (!sm.MergeIslands(kb, target, east))
            {
                // refused: the moved islands settle back into the layout
                sm.RelayoutLive(-1, 0.25f);
                History.Push();
            }
            return;
        }

        if (kind == Kind.InColumn)
        {
            int idx = blockIndex; float z = lastZ; var slotNow = Slot; int col = SlotColumn;
            var list = new List<KeyBlock>(members); var starts = new List<Vector3>(memberStart);
            var dropped = new List<Vector3>(); foreach (var m in list) dropped.Add(m != null ? new Vector3(m.px, 0f, m.pz) : Vector3.zero);
            // v8: a chord grid dropped over another measure of its own column moves there (a normal grid on any measure of a longer column)
            if (slotNow != DropSlot.Join && slotNow != DropSlot.NewColumn && list.Count == 1 && list[0] != null && list[0].kind == 0)
            {
                var g0 = list[0];
                int measure = Mathf.RoundToInt((dropped[0].x - sm.ColumnX(g0.column)) / KeyBlock.IslandWidth);
                int maxM = Mathf.Max(0, 4 - Mathf.Clamp(g0.bars, 1, 4));
                measure = Mathf.Clamp(measure, 0, maxM);
                if (measure != SongManager.OffsetBars(g0))
                {
                    if (ownSession) { sm.CancelIslandMove(); ownSession = false; }
                    sm.MoveIsland(g0, starts[0].x, starts[0].z); sm.ShownAt(g0, dropped[0]);
                    ResetState();
                    if (!sm.SetBarOffset(idx, measure, z)) { sm.ClearShownAt(); g0.SlideFrom(dropped[0] - starts[0], 0.3f, false); }
                    return;
                }
            }
            if (slotNow != DropSlot.Join && slotNow != DropSlot.NewColumn)
            {
                // over its own column: the in-column move commits (z); the block glides back into its column (x)
                for (int k = 0; k < list.Count; k++) if (list[k] != null && Mathf.Abs(list[k].px - starts[k].x) > 1e-4f) sm.MoveIsland(list[k], starts[k].x, list[k].pz);
                bool session = ownSession;
                ownSession = false;
                ResetState();
                if (session) sm.MoveIslandInColumn(idx, z, true);
                for (int k = 0; k < list.Count; k++) if (list[k] != null && Mathf.Abs(dropped[k].x - starts[k].x) > 1e-3f) list[k].SlideFrom(new Vector3(dropped[k].x - starts[k].x, 0f, 0f), 0.22f, false);
                AudioPool.UI(ProceduralAudio.Thud(), 0.22f, 1.1f);
                History.Push();
                return;
            }
            // JOIN / NEW column: the logical positions go back first; the rebuild glides the islands in from where the drag and the preview left them
            float dropZ = blockAt < dropped.Count ? dropped[blockAt].z : z;
            foreach (var o in previewed) if (o != null) sm.ShownAt(o, new Vector3(o.px, 0f, o.pz) + o.PreviewOffset);
            if (ownSession) { sm.CancelIslandMove(); ownSession = false; }
            for (int k = 0; k < list.Count; k++) if (list[k] != null) { sm.MoveIsland(list[k], starts[k].x, starts[k].z); sm.ShownAt(list[k], dropped[k]); }
            ResetState();
            int dropMeasure = slotNow == DropSlot.Join && col >= 0 && blockAt < dropped.Count ? Mathf.Max(0, Mathf.RoundToInt((dropped[blockAt].x - sm.ColumnX(col)) / KeyBlock.IslandWidth)) : 0;
            int at = sm.MoveIslandToColumn(idx, col, slotNow == DropSlot.NewColumn, dropZ, dropMeasure);
            if (at < 0)
            {
                // refused (a full column): everything glides back, no History
                sm.ClearShownAt();
                for (int k = 0; k < list.Count; k++) if (list[k] != null) list[k].SlideFrom(dropped[k] - starts[k], 0.3f, false);
            }
            return;
        }

        if (kind == Kind.Column)
        {
            var list = new List<KeyBlock>(members); var starts = new List<Vector3>(memberStart);
            var dropped = new List<Vector3>(); foreach (var m in list) dropped.Add(m != null ? new Vector3(m.px, 0f, m.pz) : Vector3.zero);
            if (gapSeg >= 0)
            {
                // reorder: remove the column, insert it between the gap segment's ends (a position in the column list after removal)
                int from = kb.column, seg = gapSeg;
                ResetState();
                for (int k = 0; k < list.Count; k++) if (list[k] != null) { sm.MoveIsland(list[k], starts[k].x, starts[k].z); sm.ShownAt(list[k], dropped[k]); }
                sm.MoveColumn(from, from < seg ? seg : seg + 1);      // rebuild + re-seek + History.Push
                if (sm.Route != null) sm.Route.RunPulse();
                AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f);
                return;
            }
            // elsewhere: the column goes back to its x and keeps the z shift (snapped); the torn groups settle (a run of one dissolves)
            float dz = Mathf.Round((shown.z - startBase.z) / ProjectConfig.IslandSnap) * ProjectConfig.IslandSnap;
            ResetState();
            for (int k = 0; k < list.Count; k++) if (list[k] != null) sm.MoveIsland(list[k], starts[k].x, starts[k].z + dz);
            sm.RelayoutLive(sm.Islands.IndexOf(kb), 0.25f);
            for (int k = 0; k < list.Count; k++) if (list[k] != null) list[k].SlideFrom(dropped[k] - new Vector3(list[k].px, 0f, list[k].pz), 0.25f, false);
            AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 0.9f);
            History.Push();
            return;
        }

        // v6 §11.3: a Moon's release starts its section at the column it is in line with; it settles in its drum lane (SetMoonColumn: the layout,
        // the sections, the windows, one History entry) gliding from where it was dropped
        if (kind == Kind.Moon && kb.IsMoon && sm.ColumnCount > 0 && sm.Moons.IndexOf(kb) >= 0)
        {
            int col = MoonColumn >= 0 ? MoonColumn : sm.ColumnNearX(kb.Center.x);
            Vector3 dropped = new Vector3(kb.px, 0f, kb.pz);
            ResetState();
            MoonColumn = -1;
            sm.SetMoonColumn(sm.Moons.IndexOf(kb), col);
            Vector3 d = dropped - new Vector3(kb.px, 0f, kb.pz);
            if (d.sqrMagnitude > 1e-4f) kb.SlideFrom(d, 0.3f, true);
            AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 0.9f);
            return;
        }
        // Moons (no columns): overlap push-out, the whole moving set slides out along the centre-to-centre vector to the nearest free spot on the 0.5 grid
        Bounds all = members[0].WorldBounds;
        for (int k = 1; k < members.Count; k++) if (members[k] != null) all.Encapsulate(members[k].WorldBounds);
        KeyBlock other = Overlapping(all);
        if (other != null)
        {
            Vector3 c = all.center; c.y = 0f;
            Vector3 dir = c - other.Center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.back;
            Vector3 centre = sm.FindFreeSpot(c, dir, all.size, members, null);
            Vector3 to = shown + (centre - c);
            float snap = ProjectConfig.IslandSnap;
            slideFrom = shown;
            slideTo = new Vector3(Mathf.Round(to.x / snap) * snap, 0f, Mathf.Round(to.z / snap) * snap);
            slideT = 0f;
            State = Phase.Sliding;
            pm.currentState = PathManager.EditorState.DraggingIsland;
            return;
        }
        Commit();
    }

    /// <summary>v6 §11.3: the column the dragged Moon is in line with (its centre's nearest column) lights its band, stretched out to the Moon.</summary>
    void ShowMoonColumn(KeyBlock moon)
    {
        var sm = SM;
        if (sm == null || moon == null || sm.ColumnCount == 0) { MoonColumn = -1; return; }
        int col = sm.ColumnNearX(moon.Center.x);
        if (col != MoonColumn && MoonColumn >= 0) AudioPool.UI(ProceduralAudio.Tick(), 0.22f, 1.25f);
        MoonColumn = col;
        float r = moon.RingRadius;
        ColumnBands.ShowRange(col, moon.Center.z - r, moon.Center.z + r);
    }

    KeyBlock Overlapping(Bounds moving)
    {
        var b = moving; b.Expand(ProjectConfig.IslandGap * 2f);
        KeyBlock best = null; float bestD = float.MaxValue;
        for (int pass = 0; pass < 2; pass++)
        {
            var list = pass == 0 ? SM.Islands : SM.Moons;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || members.Contains(o) || !b.Intersects(o.WorldBounds)) continue;
                float d = (o.Center - moving.center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = o; }
            }
        }
        return best;
    }

    void UpdateSliding()
    {
        var kb = Block;
        slideT += Time.deltaTime / 0.25f;
        if (slideT >= 1f)
        {
            MoveMembers(slideTo);
            AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 0.9f);
            if (kb != null) Fx.SeaRipple(kb.Center, kb.chordColor, 2.2f);
            pm.currentState = PathManager.EditorState.Idle;
            Commit();
            return;
        }
        MoveMembers(Vector3.LerpUnclamped(slideFrom, slideTo, Ease.OutBack(slideT)));
    }

    void Commit()
    {
        ResetState();
        if (SM != null) SM.RefreshBounds();
        History.Push();
    }

    /// <summary>Moves every member rigidly so the grabbed island's root sits at <paramref name="blockBase"/>.</summary>
    void MoveMembers(Vector3 blockBase)
    {
        if (SM == null) return;
        Vector3 d = blockBase - startBase;
        for (int k = 0; k < members.Count; k++) if (members[k] != null) SM.MoveIsland(members[k], memberStart[k].x + d.x, memberStart[k].z + d.z);
    }

    // ------------------------------------------------------------------ seam scissors (SPEC v3 §3.3, v4: seams between adjacent columns)
    void UpdateSeams(Camera cam) { SeamStep(cam, Input.mousePosition, Input.GetMouseButtonDown(0), InputUtil.PointerOverUI); }

    /// <summary>The seam under the pointer <paramref name="mp"/> shows its scissors; a press on them (<paramref name="press"/>) splits the group there.</summary>
    void SeamStep(Camera cam, Vector3 mp, bool press, bool overUI)
    {
        var sm = SM;
        var route = sm != null ? sm.Route : null;
        hoverSeam = -1;
        if (route == null) return;
        if (cam == null || pm.IsDrawing || sm.Islands.Count < 2 || overUI || WorldInput.WorldLocked) { route.HideScissors(); return; }
        Vector3 g;
        if (!GroundAt(cam, mp, out g)) { route.HideScissors(); return; }
        float best = 0.75f; Vector3 glyph = Vector3.zero;
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var a = sm.Islands[i]; if (a == null || a.group == 0) continue;
            var b = sm.GroupNeighbour(a, +1);
            if (b == null) continue;
            float bx = b.WestEdge, front = Mathf.Min(a.FrontEdge, b.FrontEdge), back = Mathf.Min(a.BackEdge, b.BackEdge);   // v5: after a belt the seam is where b starts
            float dx = Mathf.Abs(g.x - bx);
            if (dx < best && g.z > front - 1.6f && g.z < back) { best = dx; hoverSeam = i; glyph = new Vector3(bx, 0.25f + a.LiftY + a.GroundY, front - 0.62f); }   // v7: on the ground the islands stand on
        }
        if (hoverSeam < 0) { route.HideScissors(); return; }
        // the glyph stands up from its foot: test the pointer against its projected centre in screen space
        Vector3 mid = glyph + Vector3.up * 0.45f;
        Vector3 sc = cam.WorldToScreenPoint(mid), sr = cam.WorldToScreenPoint(mid + cam.transform.right * 0.5f);
        float rpx = Mathf.Max(14f, new Vector2(sr.x - sc.x, sr.y - sc.y).magnitude);
        bool hot = sc.z > 0f && new Vector2(mp.x - sc.x, mp.y - sc.y).magnitude < rpx;
        route.ShowScissors(glyph, hot);
        if (hot && press)
        {
            int seam = hoverSeam;
            route.HideScissors(); hoverSeam = -1;
            consumeFrame = Time.frameCount;   // this press is the scissors': PathManager and the camera ignore it
            sm.SplitGroup(seam);
        }
    }

    // ------------------------------------------------------------------ tests (legacy Input cannot be faked)
    /// <summary>Tests: grabs <paramref name="kb"/> (its platform = an in-column drag, or its hub = a column drag) at sea point <paramref name="ground"/>
    /// and starts a drag that only SimMove / SimRelease drive (the real mouse is ignored until the release).</summary>
    public void SimBegin(KeyBlock kb, bool onHub, Vector3 ground)
    {
        if (kb == null) return;
        if (State != Phase.Idle) Cancel();
        Block = kb; armedOnHub = onHub; simActive = true;
        BeginDrag(ground);
        simActive = true;
    }
    /// <summary>Tests: the pointer's sea point this frame (the merge magnet settles at once).</summary>
    public void SimMove(Vector3 ground) { if (State == Phase.Dragging && simActive) DragTo(ground, true); }
    /// <summary>Tests: releases the synthetic drag (merge / reorder / move / commit exactly as a mouse release).</summary>
    public void SimRelease() { if (State == Phase.Dragging && simActive) Release(); }
    /// <summary>Tests: a click (press + release in place) on <paramref name="kb"/>'s platform or hub: select, frame (a second click within 0.35 s = double-click).</summary>
    public void SimClick(KeyBlock kb, bool onHub) { if (kb == null || State != Phase.Idle) return; Block = kb; armedOnHub = onHub; State = Phase.Armed; Click(); }
    /// <summary>Tests: one scissors step at screen point <paramref name="px"/> (<paramref name="press"/> = a click); returns the hovered seam (-1 none).</summary>
    public int SimSeam(Vector2 px, bool press) { simSeamFrame = Time.frameCount + 1; SeamStep(Camera.main, px, press, false); return hoverSeam; }

    /// <summary>v7: the height of the plane the pointer is read on during a drag (the dragged island's ground; 0 = the sea plane).</summary>
    float planeY;
    bool GroundAt(Camera cam, Vector3 screenPos, out Vector3 p)
    {
        p = Vector3.zero;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(screenPos);
        float enter;
        float y = State == Phase.Idle ? 0f : planeY;
        if (!new Plane(Vector3.up, new Vector3(0f, y, 0f)).Raycast(ray, out enter) || enter <= 0f) return false;
        p = ray.GetPoint(enter);
        p.y = 0f;
        return true;
    }
}
