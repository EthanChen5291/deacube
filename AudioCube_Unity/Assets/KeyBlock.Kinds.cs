using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 (SPEC v7 §2.4, §13.3, §14.3, §18; package B): KeyBlock's v7 builds, through the partial hooks KeyBlock.Build calls (KindGridSize,
/// KindPitches, KindTiles, KindStructure — declared in KeyBlock.cs):
/// - the STAIRS island (kind 3 — the user: "some stair-case grid that just is descending notes"): stairSteps solid ink-stone blocks across
///   IslandWidth, step i's top −i × StairStepRise (a fall) or (i + 1) × StairStepRise (a climb) above the root, every block reaching down to the
///   stair's base (the platform, moved under the lowest step: a staircase that reads from the default camera), a ledge in front for the beat pips,
///   the direction glyph (↓ / ↑) on the top step's face. tile.midi = SongManager.StairPitches; <see cref="RefreshStairs"/> re-pitches once the
///   layout knows the column (an island is built before its column's chord is known) and whenever the chords / the melody around it change.
/// - the melody PHRASE (kind 4 — "a 2 measure melody shouldnt be two separate grids, they should be together"): a roll — time cells along x
///   (phraseGrid ticks each, linear over the platform after the tiny piano at its west end: inside a section time maps to x like the columns),
///   the song key's scale along z (15 rows, two octaves voiced around 60..84 + the register; further back = higher, and a touch higher in y),
///   bold measure lines and thin beat lines, and under each column it spans a tint of that column's chord (its vibe colour) with a SAFE-NOTE dot on
///   every beat of the rows that are that chord's tones (<see cref="RefreshPhraseChords"/>).
/// - the structure marks every grid wears (§13.3 — "4 beats in a measure, 4 measures in a section … make it really visual"): BEAT PIPS along its
///   front margin, four per measure, the downbeat's bigger, the playing beat's pip lit while the island plays (<see cref="StructureMarks"/>).
/// Everything is posed relative to the island root and rebuilt with the island; materials are shared (StructureLook) or per-renderer blocks,
/// so nothing leaks when K's Clear destroys the children.
/// </summary>
public partial class KeyBlock
{
    // ------------------------------------------------------------------ v7 stairs (kind 3)
    /// <summary>v7 stairs: the ledge in front of the steps (the beat pips sit on it), the margin behind them, the groove between two steps, the
    /// lowest step's own height above the base.</summary>
    public const float StairLedge = 0.62f, StairBackPad = 0.12f, StairGroove = 0.05f, StairLowest = 0.3f;
    /// <summary>v7 stairs: the steps' pale stone (the tower's family; a step's brightness follows its pitch), the base slab's deeper ink-stone, a
    /// stairs island's chordColor (its pulses and core glow: warm stone light, not a vibe).</summary>
    public static readonly Color StairStone = new Color(0.96f, 0.90f, 0.87f), StairBaseStone = new Color(0.64f, 0.57f, 0.60f), StairGlow = new Color(1f, 0.90f, 0.78f);

    float kindBaseDrop;
    int[] stairShown;
    float[] stairTops;   // v9: each step's top (island-local y), following its pitch between the run's two ends

    /// <summary>v9 (the user, via S17: "a stairs island should be a visible 3D STAIRCASE … the whole island as deep as a chord grid"): a stairs
    /// island is at least this many chord rows deep (v7: one row — a thin strip at the back of its column).</summary>
    public const int StairRows = 4;
    /// <summary>v9: a stairs island's depth for chord tones <paramref name="semis"/> (the layout's: as deep as a chord grid, at least StairRows rows).</summary>
    public static float StairDepthOf(int[] semis) => DepthOf(Mathf.Max(StairRows, RowsOf(semis)));
    float StairDepth => DepthOf(Mathf.Max(StairRows, storedSemis != null ? storedSemis.Length : 1));

    /// <summary>v7 (B): how far this island's base sits below the default platform (world units): 0 for every island but a FALLING stairs island,
    /// whose platform moves under its lowest step ((steps − 1) × StairStepRise + StairLowest − 0.03).</summary>
    public float BaseDrop => kindBaseDrop;
    /// <summary>v7 (B): world y of the island's real underside this frame (<see cref="UndersideY"/> minus <see cref="BaseDrop"/>): what a terrace or
    /// a tower pillar under it should reach.</summary>
    public float BaseUndersideY => UndersideY - kindBaseDrop;
    /// <summary>v7 stairs: island-local y of step <paramref name="i"/>'s top. v9: the run's two ends stay where SPEC v7 §2.4 puts them (−i ×
    /// StairStepRise falling, (i + 1) × StairStepRise climbing: the terrace after it is unchanged); the steps between follow their PITCH (a bigger
    /// interval, a bigger drop).</summary>
    public float StairTopY(int i) => stairTops != null && i >= 0 && i < stairTops.Length ? stairTops[i] : StairTopOf(stairDir, i);
    public static float StairTopOf(int dir, int i) => dir > 0 ? (i + 1) * ProjectConfig.StairStepRise : -i * ProjectConfig.StairStepRise;
    /// <summary>v7 stairs: island-local y of the top of the stair's base (every step block reaches down to it): the default platform top for a climb,
    /// StairLowest under the lowest step for a fall.</summary>
    public static float StairBaseTopOf(int dir, int steps)
    {
        steps = Mathf.Clamp(steps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        return dir > 0 ? -0.03f : -(steps - 1) * ProjectConfig.StairStepRise - StairLowest;
    }
    /// <summary>v7 stairs: the pitches the steps sound now (null for other islands) — SongManager.StairPitches once the layout ran.</summary>
    public int[] StairPitchesShown => stairShown != null ? (int[])stairShown.Clone() : null;
    /// <summary>v7 stairs: the step that is the TOP of the run (the first of a fall, the last of a climb): it wears the direction glyph.</summary>
    public int StairTopStep => stairDir > 0 ? Mathf.Max(0, cols - 1) : 0;

    /// <summary>
    /// v7 (B): re-pitches a stairs island's steps from SongManager.StairPitches (the column's chord into the next chord, the melody around it, the
    /// type / direction / steps, the register) and re-colours them (pitch = brightness). Cheap and idempotent: nothing happens when the run is the
    /// same. SectionPlinth calls it after every rebuild / relayout (K may call it where RefreshKeyDots is called). Returns true when it changed.
    /// </summary>
    public bool RefreshStairs()
    {
        if (!IsStairs || grid == null || cols <= 0) return false;
        var p = StairPitchesNow();
        // compared with what the steps really sound (a register change or any other writer of tile.midi is caught too)
        bool same = true;
        for (int i = 0; i < cols && same; i++) { var t = grid[i, 0]; if (t != null && i < p.Length && t.midi != p[i]) same = false; }
        stairShown = p;
        if (same) { AimLeadIn(); return false; }
        for (int i = 0; i < cols && i < p.Length; i++)
        {
            var t = grid[i, 0];
            if (t == null) continue;
            t.midi = p[i];
            t.myFrequency = ProjectConfig.refFreq * t.GetPitch();
        }
        StairTopsFrom(p);
        ReposeSteps();
        InvalidateHideDepth();   // v9 perf: the steps changed height
        AimLeadIn();
        preLightDirty = true;
        return true;
    }

    /// <summary>Every step back at its rest pose with its stone (pitch = brightness): after a re-pitch or a live resize.</summary>
    void ReposeSteps()
    {
        if (grid == null) return;
        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < cols; i++) { var t = grid[i, 0]; if (t != null) { lo = Mathf.Min(lo, t.midi); hi = Mathf.Max(hi, t.midi); } }
        for (int i = 0; i < cols; i++) { var t = grid[i, 0]; if (t != null) { ReSetupTile(t, StairStepRest(i), StepColor(t.midi, lo, hi)); ShapeStep(t.gameObject, i); } }
    }

    /// <summary>v9: each step's top from its pitch — the first and last steps at their v7 heights, the ones between at their pitch's place between
    /// the first and last pitch (clamped inside: a run that turns back stays inside its ends); evenly spaced when the ends sound the same.</summary>
    void StairTopsFrom(int[] p)
    {
        int n = cols;
        if (stairTops == null || stairTops.Length != n) stairTops = new float[n];
        float a = StairTopOf(stairDir, 0), b = StairTopOf(stairDir, n - 1);
        int p0 = p != null && p.Length > 0 ? p[0] : 0, p1 = p != null && p.Length >= n ? p[n - 1] : p0;
        for (int i = 0; i < n; i++)
        {
            float u = n > 1 ? i / (float)(n - 1) : 0f;
            if (p != null && i < p.Length && p1 != p0) u = Mathf.Clamp01((p[i] - p0) / (float)(p1 - p0));
            stairTops[i] = Mathf.Lerp(a, b, u);
        }
    }

    /// <summary>v9: step <paramref name="i"/>'s block (mesh and collider) from its top down to the stair's base.</summary>
    void ShapeStep(GameObject go, int i)
    {
        if (go == null) return;
        float t = ProjectConfig.TileThickness, h = StairTopY(i) - StairBaseTopOf(stairDir, cols);
        var size = new Vector3(StairX1(i) - StairX0(i), h, StairZ1 - StairZ0);
        var centre = new Vector3(0f, (t - h) * 0.5f, 0f);
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = MeshFactory.RoundedBoxAt(size, centre, 0.08f, 3);   // cached by size: shared, never destroyed
        var col = go.GetComponent<BoxCollider>();
        if (col != null) { col.size = size; col.center = centre; }
        var lip = go.transform.Find("Nosing");
        if (lip != null) lip.localPosition = new Vector3(0f, t * 0.5f - 0.035f, -size.z * 0.5f + 0.06f);
    }

    /// <summary>v9 (S17: "a falling run that leads into the next chord should point at that chord's grid"): a lead-in's last step wears a flat
    /// ink arrow on its tread, aimed at the next column's grid in the nearest lane (the chord the run falls into). Re-aimed after every relayout
    /// (RefreshStairs); hidden for a run that starts its column or with nothing after it.</summary>
    void AimLeadIn()
    {
        var step = IsStairs && grid != null && cols > 0 ? GetTile(cols - 1, 0) : null;
        if (step == null) return;
        var arrow = step.transform.Find("LeadIn");
        KeyBlock target = stairLead ? LeadInTarget() : null;
        if (target == null) { if (arrow != null) arrow.gameObject.SetActive(false); return; }
        if (arrow == null)
        {
            var g = new GameObject("LeadIn");
            g.transform.SetParent(step.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = StructureLook.ArrowMaterial(false);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            arrow = g.transform;
        }
        arrow.gameObject.SetActive(true);
        // from this step's centre to the middle of the target's west edge (both at rest: the layout's places)
        Vector3 rest = StairStepRest(cols - 1);
        Vector3 from = new Vector3(px + rest.x, 0f, pz + rest.z);
        Vector3 to = new Vector3(target.px - EdgeInset, 0f, target.pz - EdgeInset + target.Depth * 0.5f);
        Vector3 dir = to - from; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.right;
        float yaw = Mathf.Clamp(Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg, -LeadInMaxYaw, LeadInMaxYaw);   // it falls east, into the next column
        dir = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, Mathf.Sin(yaw * Mathf.Deg2Rad));
        float s = Mathf.Min(0.9f, (StairX1(cols - 1) - StairX0(cols - 1)) * 0.8f, (StairZ1 - StairZ0) * 0.6f);
        arrow.localPosition = new Vector3(0f, ProjectConfig.TileThickness * 0.5f + 0.012f, 0f);
        arrow.localRotation = Quaternion.LookRotation(Vector3.down, -dir);   // the glyph's ↓ (its −y) along dir, its face up
        arrow.localScale = new Vector3(s, s, 1f);
        LeadInYaw = yaw;
    }

    /// <summary>v9: the lead-in arrow's heading (degrees from +x toward +z), for tests.</summary>
    public float LeadInYaw { get; private set; }
    /// <summary>v9: the lead-in arrow turns at most this far from east.</summary>
    public const float LeadInMaxYaw = 70f;

    /// <summary>v9: the grid a lead-in falls into — the chord of the measure after the run's end (S17: "with an offset, that is the column's grid
    /// at measure (barOffset + bars) when one exists, else the next column's first grid"): in this column a chord grid sounding on that measure,
    /// else in the next column a non-Moon island; of those, the one whose lane is nearest this one's (a chord grid first). Null when none.</summary>
    public KeyBlock LeadInTarget()
    {
        var sm = SongManager.I;
        if (sm == null || column < 0 || column >= sm.ColumnCount) return null;
        var landing = sm.ChordAfterIsland(this);   // v9: the chord StairPitches falls into (S17's SongManager): the arrow and the notes agree
        if (landing != null) return landing;
        int end = SongManager.OffsetBars(this) + Mathf.Max(1, bars);
        KeyBlock best = NearestLane(sm, column, end);
        if (best == null && column + 1 < sm.ColumnCount) best = NearestLane(sm, column + 1, -1);
        return best;
    }

    /// <summary>The island of column <paramref name="col"/> (sounding on measure <paramref name="measure"/>; −1 = any, its first measure) whose lane
    /// is nearest this one's — chord grids before other kinds.</summary>
    KeyBlock NearestLane(SongManager sm, int col, int measure)
    {
        int f = sm.ColumnFirst(col), n = sm.ColumnSize(col);
        KeyBlock best = null; float bestD = float.MaxValue;
        for (int k = f; k >= 0 && k < f + n && k < sm.Islands.Count; k++)
        {
            var o = sm.Islands[k];
            if (o == null || o == this || o.IsMoon || o.IsPhrase) continue;
            int o0 = SongManager.OffsetBars(o), o1 = o0 + Mathf.Max(1, o.bars);
            if (measure >= 0 ? (o.kind != 0 || measure < o0 || measure >= o1) : o0 != 0) continue;
            float d = Mathf.Abs((o.pz + o.Depth * 0.5f) - (pz + Depth * 0.5f)) + (o.kind == 0 ? 0f : 100f);
            if (d < bestD) { bestD = d; best = o; }
        }
        return best;
    }

    /// <summary>The run's pitches as SongManager answers them now (a scale run from the key's fifth when there is no song manager or its answer
    /// does not fit the steps: the build never fails).</summary>
    int[] StairPitchesNow()
    {
        int n = Mathf.Clamp(stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        int[] p = null;
        var sm = SongManager.I;
        if (sm != null) { try { p = sm.StairPitches(this); } catch (System.Exception) { p = null; } }
        if (p == null || p.Length != n) p = FallbackStairs(n);
        return p;
    }

    int[] FallbackStairs(int n)
    {
        var k = MusicTheory.KeyOfSong();
        var pool = new List<int>();
        for (int m = 40; m <= 100; m++) if (Harmony.InScale(k.tonic, k.minor, Harmony.Pc(m))) pool.Add(m);
        int want = stairDir > 0 ? 64 : 76, at = 0;
        for (int i = 0; i < pool.Count; i++) if (Mathf.Abs(pool[i] - want) < Mathf.Abs(pool[at] - want)) at = i;
        int dir = stairDir > 0 ? 1 : -1;
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = pool[Mathf.Clamp(at + dir * i, 0, pool.Count - 1)] + 12 * register;
        return r;
    }

    /// <summary>A step's stone: brightness follows its pitch (PitchLook's ramp over its upper two thirds: the lowest step stays a mid stone, never
    /// as dark as the plinth under it).</summary>
    static Color StepColor(int m, int lo, int hi) => PitchLook.TileColor(StairStone, 0.34f + 0.66f * (hi > lo ? (m - lo) / (float)(hi - lo) : 0.5f));

    // the step blocks' footprint: stairSteps blocks across one measure (IslandWidth), from the ledge to the back pad. A stair of n bars (its
    // column's length; Width = n × IslandWidth) keeps its steps in ONE measure — the last of a lead-in run (it ends at the column's end, falling
    // into the next chord), the first otherwise — and a LANDING block (the run's start / end height) covers the other measures.
    float StairPitchX => IslandWidth / Mathf.Max(1, cols);
    int StairBars => Mathf.Max(1, bars);
    /// <summary>v7 stairs: island-local x of the west edge of the measure holding the steps.</summary>
    public float StairAreaWest => -EdgeInset + (stairLead ? (StairBars - 1) * IslandWidth : 0f);
    bool StairLandingWest => StairBars > 1 && stairLead;
    bool StairLandingEast => StairBars > 1 && !stairLead;
    float StairX0(int i) => StairAreaWest + i * StairPitchX + (i == 0 && !StairLandingWest ? 0.04f : StairGroove * 0.5f);
    float StairX1(int i) => StairAreaWest + (i + 1) * StairPitchX - (i == cols - 1 && !StairLandingEast ? 0.04f : StairGroove * 0.5f);
    static float StairZ0 => -EdgeInset + StairLedge;
    float StairZ1 => -EdgeInset + StairDepth - StairBackPad;   // v9: as deep as a chord grid
    /// <summary>The rest pose of step <paramref name="i"/>'s tile transform (TileInteraction.Top = its top face).</summary>
    Vector3 StairStepRest(int i) => new Vector3((StairX0(i) + StairX1(i)) * 0.5f, StairTopY(i) - ProjectConfig.TileThickness * 0.5f, (StairZ0 + StairZ1) * 0.5f);

    void BuildSteps(Transform tilesRoot, int[,] midi, int lowest, int highest)
    {
        int n = cols;
        float t = ProjectConfig.TileThickness;
        float baseTop = StairBaseTopOf(stairDir, n);
        kindBaseDrop = Mathf.Max(0f, -0.03f - baseTop);
        // the base: K's platform (its collider stays the island's handle) moved under the lowest step, in a deeper ink-stone (not a vibe)
        if (platform != null)
        {
            platform.transform.localPosition += Vector3.down * kindBaseDrop;
            PlatformColor = StairBaseStone; platBase = StairBaseStone;
            if (platMat != null) platMat.SetColor("_BaseColor", StairBaseStone);
        }
        float depth = StairZ1 - StairZ0;
        stairShown = new int[n];
        var pitches = new int[n];
        for (int i = 0; i < n; i++) pitches[i] = midi[i, 0];
        StairTopsFrom(pitches);   // v9: the heights follow the pitches
        for (int i = 0; i < n; i++)
        {
            float top = StairTopY(i), h = top - baseTop;
            var go = new GameObject($"Step_{measureIndex}_{i}");
            go.transform.SetParent(tilesRoot, false);
            go.transform.localPosition = StairStepRest(i);
            var size = new Vector3(StairX1(i) - StairX0(i), h, depth);
            var centre = new Vector3(0f, (t - h) * 0.5f, 0f);   // the block runs from its top face down to the base
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBoxAt(size, centre, 0.08f, 3);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TileMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;   // a taller step shades the ledge and the step below it: the run reads as a staircase
            var col = go.AddComponent<BoxCollider>();
            col.size = size; col.center = centre;
            go.tag = "Tile";
            var tile = go.AddComponent<TileInteraction>();
            tile.Setup(this, i, 0, midi[i, 0], StepColor(midi[i, 0], lowest, highest));
            tiles.Add(tile);
            grid[i, 0] = tile;
            stairShown[i] = midi[i, 0];
            // the nosing: a pale lip along the step's top front edge (each tread's edge reads as a stair's, a child: it rides the press)
            var lip = new GameObject("Nosing");
            lip.transform.SetParent(go.transform, false);
            lip.transform.localPosition = new Vector3(0f, t * 0.5f - 0.035f, -depth * 0.5f + 0.06f);
            lip.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(size.x + 0.04f, 0.09f, 0.2f), 0.04f, 2);
            var lr = lip.AddComponent<MeshRenderer>();
            lr.sharedMaterial = StructureLook.Nosing;
            lr.shadowCastingMode = ShadowCastingMode.Off;
        }
        BuildStairLanding(tilesRoot);
    }

    /// <summary>A stair of n bars: the landing over its other measures, at the height the run starts from (a lead-in) / ends on — stone, not a
    /// tile (the base's collider under it stays the island's handle). Rebuilt after a live resize.</summary>
    void BuildStairLanding(Transform tilesRoot)
    {
        if (tilesRoot == null) return;
        var old = tilesRoot.Find("StairLanding");
        if (old != null) Destroy(old.gameObject);
        if (StairBars <= 1) return;
        int n = cols;
        float baseTop = StairBaseTopOf(stairDir, n), depth = StairZ1 - StairZ0;
        int edge = StairLandingWest ? 0 : n - 1;
        float top = StairTopOf(stairDir, edge), h = top - baseTop;
        float x0 = StairLandingWest ? -EdgeInset + 0.04f : StairAreaWest + IslandWidth + StairGroove * 0.5f;
        float x1 = StairLandingWest ? StairAreaWest - StairGroove * 0.5f : -EdgeInset + Width - 0.04f;
        var go = new GameObject("StairLanding");
        go.transform.SetParent(tilesRoot, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, top - h * 0.5f, (StairZ0 + StairZ1) * 0.5f);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(Mathf.Max(0.2f, x1 - x0), h, depth), 0.08f, 3);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = StructureLook.LandingStone;
        mr.shadowCastingMode = ShadowCastingMode.On;
    }

    /// <summary>After K built the track, core glow, hub and eye: a fall's track and glow sink with its base, the closed eye moves onto the ledge,
    /// the top step wears the direction glyph.</summary>
    void StairExtras()
    {
        if (kindBaseDrop > 0f)
        {
            if (ringFilter != null) ringFilter.transform.localPosition += Vector3.down * kindBaseDrop;
            if (coreGlow != null) coreGlow.transform.localPosition += Vector3.down * kindBaseDrop;
        }
        float baseTop = StairBaseTopOf(stairDir, cols);
        if (eye != null)
        {
            eye.localPosition = new Vector3(-EdgeInset + Width - 0.4f, baseTop + 0.016f, -EdgeInset + StairLedge * 0.5f);
            eye.localScale = new Vector3(0.48f, 1f, 0.48f);
        }
        var step = GetTile(StairTopStep, 0);
        if (step != null)
        {
            // the direction glyph on the top step's front face (a child: it rides the step's hover / press)
            float top = StairTopOf(stairDir, StairTopStep), faceH = top - baseTop, faceW = StairX1(StairTopStep) - StairX0(StairTopStep);
            float s = Mathf.Min(1.15f, faceH * 0.84f, faceW * 0.78f);
            var g = new GameObject("StairGlyph");
            g.transform.SetParent(step.transform, false);
            Vector3 rest = StairStepRest(StairTopStep);
            float midY = (baseTop + top) * 0.5f - rest.y;
            g.transform.localPosition = new Vector3(0f, midY - s * 0.5f, StairZ0 - rest.z - 0.012f);
            g.transform.localScale = new Vector3(s, s, 1f);
            g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = StructureLook.ArrowMaterial(stairDir > 0);
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
    }

    // ------------------------------------------------------------------ v7 phrase (kind 4)
    /// <summary>v7 phrase: the tiny piano at the west end, the pad at the east end, the gaps between cells (x) and rows (z), the front row's top
    /// above the root, the rise per row ("further back = higher, a touch higher in y").</summary>
    public const float PhrasePianoWidth = 0.46f, PhraseEndPad = 0.08f, PhraseCellGap = 0.07f, PhraseRowGap = 0.07f, PhraseCellTop = 0.1f, PhraseRowLift = 0.014f;
    /// <summary>v7 phrase: the roll's platform (the margins around the chord tints) and a phrase's chordColor (its pulses and core glow).</summary>
    public static readonly Color PhraseRoll = new Color(0.30f, 0.26f, 0.40f), PhraseGlow = new Color(0.86f, 0.80f, 1f);
    /// <summary>v7 phrase: the cells' tint before the column chords are known (a pale lilac paper).</summary>
    static readonly Color PhrasePaper = new Color(0.90f, 0.87f, 0.97f);

    int[] phraseRows;
    int phraseKeySig = -1, phraseChordSig;
    readonly List<PhraseSeg> phraseSegs = new List<PhraseSeg>();
    readonly Dictionary<int, Transform> safeDots = new Dictionary<int, Transform>();
    Color[] cellShown;

    /// <summary>v7 phrase: a column segment of the roll — beats [b0, b1) from the phrase's start over column <c>col</c>'s chord.</summary>
    public struct PhraseSeg { public float b0, b1; public int col, root; public int[] semis; public Color vibe; }

    /// <summary>v7 phrase: time cells (phraseBeats × TicksPerBeat / phraseGrid).</summary>
    public int PhraseCells => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, phraseBeats) * ProjectConfig.TicksPerBeat / (float)Mathf.Max(1, phraseGrid)));
    float PhraseX0 => -EdgeInset + PhrasePianoWidth;
    float PhraseX1 => -EdgeInset + PhraseWidth - PhraseEndPad;
    /// <summary>v7 phrase: world units per time cell (the cells are linear in time over [west + PhrasePianoWidth, east − PhraseEndPad]).</summary>
    public float PhraseCellWidth => (PhraseX1 - PhraseX0) / PhraseCells;
    /// <summary>v7 phrase: island-local x of <paramref name="beat"/> beats after the phrase's start (0 = the first cell's west edge).</summary>
    public float PhraseBeatX(float beat) => PhraseX0 + beat / Mathf.Max(1, phraseBeats) * (PhraseX1 - PhraseX0);
    /// <summary>v7 phrase: island-local z of row <paramref name="row"/> (row 0 = the lowest pitch, at the root's z; further back = higher).</summary>
    public static float PhraseRowZ(int row) => row * ProjectConfig.PhraseRowPitch;
    /// <summary>v7 phrase: island-local y of row <paramref name="row"/>'s cell tops (a touch higher per row).</summary>
    public static float PhraseRowTop(int row) => PhraseCellTop + row * PhraseRowLift;
    /// <summary>v7 phrase: island-local top centre of cell (<paramref name="x"/>, <paramref name="z"/>) at rest.</summary>
    public Vector3 PhraseCellLocal(int x, int z) => new Vector3(PhraseX0 + (x + 0.5f) * PhraseCellWidth, PhraseRowTop(z), PhraseRowZ(z));
    /// <summary>v7 phrase: the cell under world point <paramref name="world"/> (x by time, z by the nearest row); false off the roll.</summary>
    public bool PhraseCellAt(Vector3 world, out int x, out int z)
    {
        x = -1; z = -1;
        if (!IsPhrase) return false;
        var l = transform.InverseTransformPoint(world);
        float cw = PhraseCellWidth;
        if (cw <= 1e-5f || l.x < PhraseX0 || l.x >= PhraseX1) return false;
        x = Mathf.Clamp(Mathf.FloorToInt((l.x - PhraseX0) / cw), 0, PhraseCells - 1);
        z = Mathf.RoundToInt(l.z / ProjectConfig.PhraseRowPitch);
        if (z < 0 || z >= ProjectConfig.PhraseRows || Mathf.Abs(l.z - PhraseRowZ(z)) > ProjectConfig.PhraseRowPitch * 0.5f + 0.001f) { x = -1; z = -1; return false; }
        return true;
    }
    /// <summary>v7 phrase: the rows' pitches (the song key's scale over two octaves from the tonic voiced into 54..65, + 12 × register): 15 rows.</summary>
    public static int[] PhraseScale(int tonic, bool minor, int register)
    {
        int[] steps = minor ? new[] { 0, 2, 3, 5, 7, 8, 10 } : new[] { 0, 2, 4, 5, 7, 9, 11 };
        int t0 = 54 + Harmony.Pc(tonic - 54);
        var r = new int[ProjectConfig.PhraseRows];
        for (int z = 0; z < r.Length; z++) r[z] = t0 + 12 * (z / 7) + steps[z % 7] + 12 * register;
        return r;
    }
    /// <summary>v7 phrase: the pitch of row <paramref name="row"/> (register included); -1 off the roll.</summary>
    public int PhraseRowMidi(int row) => phraseRows != null && row >= 0 && row < phraseRows.Length ? phraseRows[row] : -1;
    /// <summary>v7 phrase: the column segments the roll shows now (tests / U1).</summary>
    public IList<PhraseSeg> PhraseSegments => phraseSegs;
    /// <summary>v7 phrase: true when cell (x, z) wears the safe-note dot (a beat's first cell on a chord-tone row of its segment).</summary>
    public bool PhraseSafeDot(int x, int z) { Transform d; return IsPhrase && safeDots.TryGetValue(x * 64 + z, out d) && d != null && d.gameObject.activeSelf; }
    /// <summary>v7 phrase: how many safe-note dots show.</summary>
    public int PhraseSafeDotsShown { get { int n = 0; foreach (var kv in safeDots) if (kv.Value != null && kv.Value.gameObject.activeSelf) n++; return n; } }
    /// <summary>v7 phrase: the face colour cell (x, z) shows at rest (tests).</summary>
    public Color PhraseCellColor(int x, int z) { var t = GetTile(x, z); return t != null ? t.BaseColor : Color.clear; }
    /// <summary>v7 phrase: the index of the segment holding cell <paramref name="x"/> (−1 none).</summary>
    public int PhraseSegmentOf(int x)
    {
        float b = (x + 0.001f) * Mathf.Max(1, phraseGrid) / (float)ProjectConfig.TicksPerBeat;
        for (int i = 0; i < phraseSegs.Count; i++) if (b >= phraseSegs[i].b0 - 1e-4f && b < phraseSegs[i].b1 - 1e-4f) return i;
        return phraseSegs.Count > 0 ? phraseSegs.Count - 1 : -1;
    }

    /// <summary>The rest pose of cell (x, z)'s tile transform (TileInteraction.Top = the cell's top face).</summary>
    Vector3 PhraseCellRest(int x, int z) => PhraseCellLocal(x, z) - new Vector3(0f, ProjectConfig.TileThickness * 0.5f, 0f);

    void BuildRoll(Transform tilesRoot, int[,] midi)
    {
        if (platform != null)
        {
            PlatformColor = PhraseRoll; platBase = PhraseRoll;
            if (platMat != null) platMat.SetColor("_BaseColor", PhraseRoll);
        }
        float t = ProjectConfig.TileThickness, cw = PhraseCellWidth;
        float wCell = Mathf.Max(0.08f, cw - PhraseCellGap), dCell = ProjectConfig.PhraseRowPitch - PhraseRowGap;
        var meshes = new Mesh[rows]; var sizes = new Vector3[rows]; var centres = new Vector3[rows];
        for (int z = 0; z < rows; z++)
        {
            float h = PhraseRowTop(z) + 0.03f;   // every cell reaches down to the platform top
            sizes[z] = new Vector3(wCell, h, dCell);
            centres[z] = new Vector3(0f, (t - h) * 0.5f, 0f);
            meshes[z] = MeshFactory.RoundedBoxAt(sizes[z], centres[z], Mathf.Min(0.05f, wCell * 0.2f), 2);
        }
        cellShown = new Color[cols * rows];
        safeDots.Clear();
        for (int x = 0; x < cols; x++)
            for (int z = 0; z < rows; z++)
            {
                var go = new GameObject($"Cell_{measureIndex}_{x}_{z}");
                go.transform.SetParent(tilesRoot, false);
                go.transform.localPosition = PhraseCellRest(x, z);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[z];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = TileMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var col = go.AddComponent<BoxCollider>();
                col.size = sizes[z]; col.center = centres[z];
                go.tag = "Tile";
                var tile = go.AddComponent<TileInteraction>();
                Color c = PitchLook.TileColor(PhrasePaper, z / (float)Mathf.Max(1, rows - 1));
                tile.Setup(this, x, z, midi[x, z], c);
                cellShown[x * rows + z] = c;
                tiles.Add(tile);
                grid[x, z] = tile;
            }
        phraseChordSig = 0;
    }

    /// <summary>After K's build: the measure / beat lines and the tiny piano (the chord tints and safe dots: RefreshPhraseChords).</summary>
    void PhraseExtras()
    {
        var root = new GameObject("Roll").transform;
        root.SetParent(transform, false);
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar), beats = Mathf.Max(1, phraseBeats);
        float offBeats = phraseOffset / (float)ProjectConfig.TicksPerBeat;
        float z0 = PhraseRowZ(0) - ProjectConfig.PhraseRowPitch * 0.5f - 0.08f, z1 = PhraseRowZ(rows - 1) + ProjectConfig.PhraseRowPitch * 0.5f + 0.08f;
        float slope = PhraseRowLift / ProjectConfig.PhraseRowPitch;
        for (int b = 1; b < beats; b++)
        {
            bool bar = Mathf.Abs(Mathf.Repeat(offBeats + b, bpb)) < 0.01f;
            float w = bar ? 0.1f : 0.036f, h = bar ? 0.09f : 0.04f, lift = bar ? 0.05f : -0.012f;
            float x = PhraseBeatX(b), zm = (z0 + z1) * 0.5f, len = (z1 - z0) * Mathf.Sqrt(1f + slope * slope);
            var l = new GameObject(bar ? "MeasureLine" : "BeatLine");
            l.transform.SetParent(root, false);
            l.transform.localPosition = new Vector3(x, PhraseCellTop + zm * slope + lift - h * 0.5f, zm);
            l.transform.localRotation = Quaternion.Euler(-Mathf.Atan(slope) * Mathf.Rad2Deg, 0f, 0f);
            l.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(w, h, len), Mathf.Min(w, h) * 0.45f, 2);
            var r = l.AddComponent<MeshRenderer>();
            r.sharedMaterial = bar ? StructureLook.InkBar : StructureLook.InkThin;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        // the tiny piano at the west end: one key per row (a black key where the row's note is a black key), the key's tonic wears the house
        var k = MusicTheory.KeyOfSong();
        float kx = -EdgeInset + PhrasePianoWidth * 0.5f, kw = PhrasePianoWidth - 0.12f, kd = ProjectConfig.PhraseRowPitch - 0.06f;
        for (int z = 0; z < rows; z++)
        {
            int m = PhraseRowMidi(z);
            bool black = IsBlackKeyMidi(m);
            float top = PhraseRowTop(z) + (black ? 0.09f : 0.03f), h = top + 0.03f;
            var key = new GameObject("PianoKey" + z);
            key.transform.SetParent(root, false);
            key.transform.localPosition = new Vector3(kx, top - h * 0.5f, PhraseRowZ(z));
            key.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(black ? kw * 0.8f : kw, h, kd), 0.05f, 2);
            var r = key.AddComponent<MeshRenderer>();
            r.sharedMaterial = black ? StructureLook.PianoEbony : StructureLook.PianoIvory;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (Harmony.Pc(m) == k.tonic)
            {
                var hm = new GameObject("KeyHouse");
                hm.transform.SetParent(key.transform, false);
                hm.transform.localPosition = new Vector3(0f, h * 0.5f + 0.012f, 0f);
                hm.transform.localScale = new Vector3(kd * 0.9f, 1f, kd * 0.9f);
                hm.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
                var hr = hm.AddComponent<MeshRenderer>();
                hr.sharedMaterial = black ? StructureLook.HouseCream : StructureLook.HouseInk;
                hr.shadowCastingMode = ShadowCastingMode.Off; hr.receiveShadows = false;
            }
        }
    }

    /// <summary>
    /// v7 (B, SPEC v7 §14.3): the roll's harmony — one segment per column its time overlaps (SongManager.PhraseSpan against the columns' spans;
    /// ChordOfColumn: a keyboard / stairs / phrase-only column plays over the chord before it), each tinting its cells and the platform under
    /// them with that chord's vibe colour, and a SAFE-NOTE dot on every beat's first cell of the rows that are its chord tones. The rows re-pitch
    /// when the song key changed. Cheap when nothing changed. SectionPlinth calls it after every rebuild / relayout (K may call it where
    /// RefreshKeyDots is called). Returns true when the look changed.
    /// </summary>
    public bool RefreshPhraseChords() => RefreshPhraseChords(false);

    bool RefreshPhraseChords(bool force)
    {
        if (!IsPhrase || grid == null || cols <= 0 || kindMarks == null) return false;
        var key = MusicTheory.KeyOfSong();
        int keySig = key.tonic * 2 + (key.minor ? 1 : 0) + 100 * (register + 2);
        if (keySig != phraseKeySig)
        {
            bool first = phraseKeySig < 0;
            phraseKeySig = keySig;
            var rowsNow = PhraseScale(key.tonic, key.minor, register);
            if (!first && phraseRows != null)
                for (int x = 0; x < cols; x++)
                    for (int z = 0; z < rows; z++) { var t = grid[x, z]; if (t != null) { t.midi = rowsNow[z]; t.myFrequency = ProjectConfig.refFreq * t.GetPitch(); } }
            phraseRows = rowsNow;
            force = true;
        }
        PhraseSegmentsInto(phraseSegs);
        int sig = 17;
        foreach (var s in phraseSegs)
        {
            sig = sig * 31 + Mathf.RoundToInt(s.b0 * 96f); sig = sig * 31 + Mathf.RoundToInt(s.b1 * 96f); sig = sig * 31 + s.root;
            if (s.semis != null) foreach (int v in s.semis) sig = sig * 31 + v;
            sig = sig * 31 + s.vibe.GetHashCode();
        }
        if (!force && sig == phraseChordSig) return false;
        phraseChordSig = sig;
        // the tints under the cells (one band per segment)
        float za = PhraseRowZ(0) - ProjectConfig.PhraseRowPitch * 0.5f - 0.1f, zb = PhraseRowZ(rows - 1) + ProjectConfig.PhraseRowPitch * 0.5f + 0.1f;
        kindMarks.SetBandCount(phraseSegs.Count);
        for (int i = 0; i < phraseSegs.Count; i++)
        {
            var s = phraseSegs[i];
            float xa = i == 0 ? PhraseX0 - 0.04f : PhraseBeatX(s.b0), xb = i == phraseSegs.Count - 1 ? PhraseX1 + 0.04f : PhraseBeatX(s.b1);
            kindMarks.SetBand(i, new Vector3((xa + xb) * 0.5f, -0.02f, (za + zb) * 0.5f), new Vector2(xb - xa, zb - za), PlatformColorOf(s.vibe));
        }
        // the cells take their segment's hue (pitch = brightness), the safe dots their chord tones
        int ticksPerCell = Mathf.Max(1, phraseGrid);
        float dotSize = Mathf.Min(0.26f, PhraseCellWidth * 0.46f);
        for (int x = 0; x < cols; x++)
        {
            int si = PhraseSegmentOf(x);
            if (si < 0) continue;
            var s = phraseSegs[si];
            Color tint = TileTintOf(s.vibe), dotCol = Color.Lerp(PlatformColorOf(s.vibe), Look.InkColor, 0.25f);   // deeper than the pastel cells
            bool beatStart = (x * ticksPerCell) % ProjectConfig.TicksPerBeat == 0;
            for (int z = 0; z < rows; z++)
            {
                var t = grid[x, z];
                if (t == null) continue;
                Color c = PitchLook.TileColor(tint, z / (float)Mathf.Max(1, rows - 1));
                int ci = x * rows + z;
                if (cellShown == null || ci >= cellShown.Length || cellShown[ci] != c)
                {
                    ReSetupTile(t, PhraseCellRest(x, z), c);
                    if (cellShown != null && ci < cellShown.Length) cellShown[ci] = c;
                }
                bool safe = false;
                if (beatStart && s.semis != null)
                {
                    int rel = Harmony.Pc(t.midi - s.root);
                    for (int j = 0; j < s.semis.Length; j++) if (Harmony.Pc(s.semis[j]) == rel) { safe = true; break; }
                }
                Transform dot;
                safeDots.TryGetValue(x * 64 + z, out dot);
                if (safe && dot == null)
                {
                    dot = StructureLook.Decal(t.transform, "SafeDot", new Vector3(0f, ProjectConfig.TileThickness * 0.5f + 0.012f, 0f), dotSize, StructureLook.DotMaterial);
                    safeDots[x * 64 + z] = dot;
                }
                if (dot != null)
                {
                    if (dot.gameObject.activeSelf != safe) dot.gameObject.SetActive(safe);
                    if (safe) StructureLook.Tint(dot.GetComponent<Renderer>(), Palette.A(dotCol, 0.95f));
                }
            }
        }
        preLightDirty = true;
        return true;
    }

    /// <summary>The column segments of the roll (beats from the phrase's start): the columns whose spans overlap the phrase's; past the song's end
    /// the last chord carries on; without a song (or before its columns exist) one segment over the chord Harmony.ChordOf answers.</summary>
    void PhraseSegmentsInto(List<PhraseSeg> into)
    {
        into.Clear();
        float len = Mathf.Max(1, phraseBeats);
        var sm = SongManager.I;
        if (sm != null && sm.ColumnCount > 0 && column >= 0 && column < sm.ColumnCount)
        {
            float ps, pe;
            sm.PhraseSpan(this, out ps, out pe);
            for (int c = 0; c < sm.ColumnCount; c++)
            {
                float cs = sm.ColumnStart(c), ce = cs + sm.ColumnLength(c);
                float a = Mathf.Max(cs, ps), b = Mathf.Min(ce, ps + len);
                if (b - a <= 1e-4f) continue;
                var ch = sm.ChordOfColumn(c);
                if (ch == null) continue;
                IList<int> semis = ch.semitoneList;
                var seg = new PhraseSeg { b0 = a - ps, b1 = b - ps, col = c, root = ch.chordRootMIDI, semis = new int[semis.Count], vibe = ch.chordColor };
                for (int j = 0; j < semis.Count; j++) seg.semis[j] = semis[j];
                if (into.Count > 0 && into[into.Count - 1].b1 < seg.b0 - 1e-4f) { var prev = into[into.Count - 1]; prev.b1 = seg.b0; into[into.Count - 1] = prev; }
                into.Add(seg);
            }
        }
        if (into.Count == 0)
        {
            int root; IList<int> semis;
            Harmony.ChordOf(this, out root, out semis);
            if (semis == null || semis.Count == 0) semis = new[] { 0, 4, 7 };
            var seg = new PhraseSeg { b0 = 0f, b1 = len, col = column, root = root, semis = new int[semis.Count], vibe = MusicTheory.ChordColor(root, semis) };
            for (int j = 0; j < semis.Count; j++) seg.semis[j] = semis[j];
            into.Add(seg);
            return;
        }
        var f = into[0]; f.b0 = 0f; into[0] = f;
        var l = into[into.Count - 1]; l.b1 = len; into[into.Count - 1] = l;
    }

    // ------------------------------------------------------------------ the hooks (declared in KeyBlock.cs)
    /// <summary>K's PhraseWidth = PhraseCellsWidth (the phrase's measures × IslandWidth) + these margins: the piano west of the first cell, a pad
    /// east of the last, so the cells span exactly the phrase's measures and K lays the first cell's west edge at the phrase's start.</summary>
    static partial void KindPhraseMargins(ref float west, ref float east) { west = PhrasePianoWidth; east = PhraseEndPad; }

    partial void KindGridSize()
    {
        if (IsStairs)
        {
            cols = Mathf.Clamp(stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps); rows = 1;
            chordColor = StairGlow;   // not a vibe: the steps are stone (their pulses and core glow a warm stone light)
        }
        else if (IsPhrase)
        {
            cols = PhraseCells; rows = ProjectConfig.PhraseRows;
            chordColor = PhraseGlow;   // the chords live in the roll's segments
        }
    }

    partial void KindPitches(int[,] midi, ref int lowest, ref int highest)
    {
        kindBaseDrop = 0f;
        if (IsStairs)
        {
            var p = StairPitchesNow();
            for (int x = 0; x < cols; x++)
            {
                midi[x, 0] = x < p.Length ? p[x] : p[p.Length - 1];
                lowest = Mathf.Min(lowest, midi[x, 0]); highest = Mathf.Max(highest, midi[x, 0]);
            }
        }
        else if (IsPhrase)
        {
            var k = MusicTheory.KeyOfSong();
            phraseRows = PhraseScale(k.tonic, k.minor, register);
            phraseKeySig = k.tonic * 2 + (k.minor ? 1 : 0) + 100 * (register + 2);
            for (int x = 0; x < cols; x++) for (int z = 0; z < rows; z++) midi[x, z] = phraseRows[z];
            lowest = phraseRows[0]; highest = phraseRows[phraseRows.Length - 1];
        }
    }

    partial void KindTiles(Transform tilesRoot, int[,] midi, int lowest, int highest, Color tileTint)
    {
        if (IsStairs) BuildSteps(tilesRoot, midi, lowest, highest);
        else if (IsPhrase) BuildRoll(tilesRoot, midi);
    }

    StructureMarks kindMarks;
    /// <summary>v7 (B): the structure-marks component (beat pips; a phrase's chord tints) — null on Moons.</summary>
    public StructureMarks KindMarks => kindMarks;

    partial void KindStructure()
    {
        kindMarks = null;
        if (!IsStairs) { kindBaseDrop = 0f; stairShown = null; }
        if (!IsPhrase) { phraseRows = null; safeDots.Clear(); phraseSegs.Clear(); cellShown = null; }
        if (IsMoon) return;
        var go = new GameObject("StructureMarks");
        go.transform.SetParent(transform, false);
        kindMarks = go.AddComponent<StructureMarks>();
        kindMarks.Bind(this);
        if (IsStairs) StairExtras();
        else if (IsPhrase) PhraseExtras();
        BuildPips();
        if (IsPhrase) RefreshPhraseChords(true);
    }

    /// <summary>v7 §19.1 (K's ResizeMeasures after a live column resize — the tiles stay): the beat pips re-laid for the new bars (the draft fill
    /// carries over), a stair's steps moved into their measure (the last one of a lead-in) and its landing rebuilt.</summary>
    partial void KindResize()
    {
        if (IsMoon || kindMarks == null) return;
        if (IsStairs && grid != null)
        {
            ReposeSteps();
            BuildStairLanding(tilesContainer);
            if (eye != null) eye.localPosition = new Vector3(-EdgeInset + Width - 0.4f, StairBaseTopOf(stairDir, cols) + 0.016f, -EdgeInset + StairLedge * 0.5f);
        }
        BuildPips();
    }

    // ------------------------------------------------------------------ beat pips (every grid but Moons)
    /// <summary>v7: the pips' sizes on a grid (a beat, a downbeat).</summary>
    public const float PipSize = 0.25f, PipDownSize = 0.38f;

    /// <summary>The pips along the front margin: one per beat of the island's length (a phrase: of its phraseBeats), centred on the beat's slice of
    /// the platform's width (a phrase: the beat's cells), the downbeats bigger. A keyboard (no front margin: its keys reach the lip) wears them
    /// on its platform's front face.</summary>
    void BuildPips()
    {
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        int n = IsPhrase ? Mathf.Max(1, phraseBeats) : Mathf.Max(1, bars) * bpb;
        float offBeats = IsPhrase ? phraseOffset / (float)ProjectConfig.TicksPerBeat : 0f;
        var pos = new Vector3[n]; var down = new bool[n];
        bool face = IsKeyboard;
        for (int k = 0; k < n; k++)
        {
            down[k] = Mathf.Abs(Mathf.Repeat(offBeats + k, bpb)) < 0.01f;
            float x;
            if (IsPhrase) x = PhraseBeatX(k + 0.5f);
            else x = -EdgeInset + (k + 0.5f) * Width / n;
            if (IsStairs) pos[k] = new Vector3(x, StairBaseTopOf(stairDir, cols) + 0.013f, -EdgeInset + StairLedge * 0.5f);
            else if (IsPhrase) pos[k] = new Vector3(x, 0.013f, -EdgeInset + 0.42f);
            else if (face) pos[k] = new Vector3(x, -0.03f - ProjectConfig.PlatformThickness * 0.5f, -EdgeInset - 0.012f);
            else pos[k] = new Vector3(x, 0.013f, -EdgeInset + 0.3f);
        }
        kindMarks.BuildPips(pos, down, face, PipSize, PipDownSize);
    }

    // ------------------------------------------------------------------ v7 §19.1: a draft's fill on the pips
    /// <summary>
    /// v7 (SPEC v7 §19.1 — "a cap on beats per path … capacity reached"): shows how full this grid is while a path is DRAWN on it — the beat pips
    /// of the draft's used <paramref name="beats"/> (from the grid's start) fill with a steady teal (a partly used beat: a smaller fill), the
    /// pips under the NEXT note — from the used beats to <paramref name="nextEnd"/>, where the next note (the brush; on a phrase the hovered
    /// cell's note) would END, in beats from the grid's start — show as pale ghosts, coral when it would pass the grid's length (a full grid: its
    /// last pip). D (PathManager.UpdateFill) calls it whenever the draft changes with (used, used + brush); <c>SetDraftFill(-1, 0)</c> clears it.
    /// The playing beat's yellow wins over the fill.
    /// </summary>
    public void SetDraftFill(float beats, float nextEnd)
    {
        if (kindMarks != null) kindMarks.SetFill(beats, nextEnd);
    }
    /// <summary>v7 §19.1: the draft fill shown now (beats; −1 none) and where the next note would end (beats from the grid's start).</summary>
    public float DraftFillBeats => kindMarks != null ? kindMarks.FillBeats : -1f;
    public float DraftNextBeats => kindMarks != null ? kindMarks.NextBeats : 0f;

    /// <summary>Re-colours a tile at its rest pose (TileInteraction keeps its hover / press state; its rest is re-read from the pose we give).</summary>
    static void ReSetupTile(TileInteraction t, Vector3 rest, Color c)
    {
        if (t == null || t.island == null) return;
        t.transform.localPosition = rest;
        t.Setup(t.island, t.gridX, t.gridZ, t.midi, c);
    }
}
