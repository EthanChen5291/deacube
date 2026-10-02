using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 package W (SPEC v7 §6.1): the stairs' magic — "allow the cubes to fall downwards like a stair for each note". B builds the steps (and
/// their ↓ / ↑ glyph), H drops the runner from step to step; this lights each STEP as a runner lands on it (the tile's own flash + a small ink
/// ring), throws DUST off the step's edges (flat cream puffs, drawn on twos, that slow and fade), and gives a falling runner comic SPEED LINES:
/// while its body drops fast (≥ 3 u/s) and once more at the landing when the step is lower than the one before (the fall reads even in a still
/// frame); a climbing runner's hop gets a smaller puff instead. Owned and stepped by <see cref="WorldMagic"/>, fed by AudioCube.OnLanded.
/// v9 (R; "the descending notes and the fall" must show): each step the runner lands on lights harder and puffs; a TRAIL of fading step OUTLINES
/// (ink rings sized to the steps) stays behind it over the steps it took (a pure function of the song beat: <see cref="TrailBeats"/>); on a LEAD-IN
/// run (KeyBlock.stairLead) the last step fires a small FLASH onto the chord grid in the next column it falls into (<see cref="LeadFlashes"/>).
/// Designed against KeyBlock's stairs data (steps = the stairs island's tiles; <see cref="StepTop"/> is the stub for -60's per-step API).
/// Plays in the present mode too.
/// </summary>
public class StairsFx
{
    /// <summary>Counters since Play (tests): steps lit, dust puffs thrown, speed-line bursts on falls, landings after a climb; v9: trail marks laid,
    /// lead-in flashes onto the next chord grid.</summary>
    public int StepFlashes, DustPuffs, FallLines, Climbs, TrailMarks, LeadFlashes;
    /// <summary>v9: how long a step outline stays behind the runner (beats); outlines shown now; the most at once.</summary>
    public static float TrailBeats = 1.75f;
    public int ActiveTrail { get; private set; }
    public int MaxTrail;
    public bool Grew { get; private set; }

    readonly Dictionary<AudioCube, float> lastStepY = new Dictionary<AudioCube, float>();
    readonly Dictionary<AudioCube, float> lastBodyY = new Dictionary<AudioCube, float>();
    readonly Dictionary<AudioCube, float> lineClock = new Dictionary<AudioCube, float>();
    readonly List<AudioCube> runners = new List<AudioCube>();
    float listClock;
    // v9: the trail
    struct Mark { public TileInteraction tile; public double beat; public float sx, sz; public Color c; public bool used; }
    const int MarkCap = 40;
    readonly Mark[] marks = new Mark[MarkCap];
    int markHead;
    Transform trailRoot; Material trailMat;
    readonly List<Transform> trail = new List<Transform>(); readonly List<MeshRenderer> trailR = new List<MeshRenderer>();
    MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>v9: world position of the tread of step <paramref name="i"/> of <paramref name="stairs"/> (-60's staircase: KeyBlock.GetTile(i, 0).Top;
    /// StairTopY when the tile is missing).</summary>
    public static Vector3 StepTop(KeyBlock stairs, int i)
    {
        if (stairs == null) return Vector3.zero;
        var t = stairs.GetTile(i, 0);
        if (t != null) return t.Top;
        return stairs.transform.TransformPoint(new Vector3(0f, stairs.StairTopY(i), 0f));
    }

    /// <summary>A runner's note landed on <paramref name="step"/> (a stairs tile).</summary>
    public void OnLanded(WorldMagic wm, AudioCube c, TileInteraction step) { OnLanded(wm, c, step, false); }

    /// <summary>A runner's note landed on <paramref name="step"/>; <paramref name="lastNode"/>: the last node of its path (a lead-in's last step flashes
    /// the grid it falls into).</summary>
    public void OnLanded(WorldMagic wm, AudioCube c, TileInteraction step, bool lastNode)
    {
        if (c == null || step == null || wm == null) return;
        StepFlashes++;
        Color pale = Color.Lerp(c.Color, Color.white, 0.5f);
        step.Flash(pale, 1.5f);
        Vector3 top = step.Top;
        Fx.Ripple(top + Vector3.up * 0.02f, c.Color, 0.8f, 0.32f);
        float prev;
        bool fell = lastStepY.TryGetValue(c, out prev) && top.y < prev - 0.05f;
        bool climbed = lastStepY.TryGetValue(c, out prev) && top.y > prev + 0.05f;
        lastStepY[c] = top.y;
        // dust off the step's front and sides (a fall stirs more)
        var r = step.GetComponent<Renderer>();
        Vector3 size = r != null ? r.bounds.size : new Vector3(1f, 0.3f, 1f);
        int n = fell ? 9 : 6;
        Color dust = Color.Lerp(MagicTextures.FoamCream, new Color(0.78f, 0.72f, 0.8f), 0.35f);
        for (int i = 0; i < n; i++)
        {
            float u = (i + 0.5f) / n;
            Vector3 p; Vector3 v;
            if (i % 3 == 0) { p = top + new Vector3((u - 0.5f) * size.x, 0f, -size.z * 0.5f); v = new Vector3((u - 0.5f) * 1.2f, 0.25f, -Random.Range(0.6f, 1.2f)); }
            else if (i % 3 == 1) { p = top + new Vector3(-size.x * 0.5f, 0f, (u - 0.5f) * size.z); v = new Vector3(-Random.Range(0.6f, 1.1f), 0.25f, (u - 0.5f)); }
            else { p = top + new Vector3(size.x * 0.5f, 0f, (u - 0.5f) * size.z); v = new Vector3(Random.Range(0.6f, 1.1f), 0.25f, (u - 0.5f)); }
            wm.Puff(p + Vector3.up * 0.06f, v, dust, Random.Range(0.28f, 0.46f), Random.Range(0.4f, 0.65f));
            DustPuffs++;
        }
        if (fell) { Fx.SpeedLines(top + Vector3.up * 0.75f, Vector3.down, MagicTextures.FoamCream); FallLines++; }
        if (climbed) Climbs++;
        // v9: a step outline stays behind the runner
        marks[markHead] = new Mark { tile = step, beat = WorldMagic.FxBeat, sx = Mathf.Max(0.3f, size.x) * 0.62f, sz = Mathf.Max(0.3f, size.z) * 0.62f, c = pale, used = true };
        markHead = (markHead + 1) % MarkCap;
        TrailMarks++;
        // v9: a lead-in run's last step flashes the chord grid it falls into
        if (lastNode && step.island != null && step.island.IsStairs && step.island.stairLead) LeadFlash(wm, c, step.island);
    }

    /// <summary>v9: the chord grid of the next column (the one the lead-in falls into; the first column's after the last when the song loops) lights
    /// up: its first measure's tiles glint, a ring and a starburst at their centre, sparkles.</summary>
    void LeadFlash(WorldMagic wm, AudioCube c, KeyBlock stairs)
    {
        var sm = SongManager.I;
        if (sm == null) return;
        KeyBlock best = stairs.LeadInTarget();   // -60's: the grid the lead-in falls into (the same one its ink arrow points at)
        if (best == null)
        {
            int col = stairs.column + 1;
            if (col >= sm.ColumnCount) { if (!GlobalClock.LoopSong) return; col = 0; }
            float bestD = float.MaxValue; int bestRank = int.MaxValue;
            foreach (var o in sm.Islands)
            {
                if (o == null || o.IsMoon || o.IsStairs || o.column != col) continue;
                int rank = o.IsPhrase ? 2 : (o.IsKeyboard ? 1 : 0);
                float d = Mathf.Abs(o.pz - stairs.pz);
                if (rank < bestRank || (rank == bestRank && d < bestD)) { bestRank = rank; bestD = d; best = o; }
            }
        }
        if (best == null) return;
        LeadFlashes++;
        float west = best.WestEdge + (best.transform.position.x - best.px);
        Vector3 centre = Vector3.zero; int n = 0, flashed = 0;
        Color pale = Color.Lerp(c.Color, Color.white, 0.5f);
        foreach (var t in best.tiles)
        {
            if (t == null || t.Top.x >= west + KeyBlock.IslandWidth) continue;
            centre += t.Top; n++;
            if (flashed < 6 && (n % 2 == 0)) { t.Flash(pale, 0.9f); flashed++; }
        }
        if (n == 0) { centre = best.VisualCenter + Vector3.up * 0.3f; n = 1; }
        centre /= n;
        Fx.Ripple(centre + Vector3.up * 0.05f, pale, 1.6f, 0.4f);
        Fx.Starburst(centre + Vector3.up * 0.5f, c.Color, 0.9f);
        for (int i = 0; i < 7; i++) wm.Sparkle(centre + Vector3.up * 0.2f, Random.insideUnitSphere * 0.8f + Vector3.up * 1.5f, i % 2 == 0 ? Color.white : pale, Random.Range(0.14f, 0.24f), Random.Range(0.35f, 0.6f));
    }

    void EnsureTrail(int i)
    {
        if (trailRoot == null)
        {
            var go = new GameObject("StairsTrail");
            if (WorldMagic.I != null) go.transform.SetParent(WorldMagic.I.transform, false);
            trailRoot = go.transform;
            trailMat = Fx.InkMaterial(1); trailMat.renderQueue = 3004;
            mpb = new MaterialPropertyBlock();
        }
        while (trail.Count <= i)
        {
            var go = new GameObject("stepOutline");
            go.transform.SetParent(trailRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.FlatRing(0.86f);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = trailMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            go.SetActive(false);
            trail.Add(go.transform); trailR.Add(mr);
            Grew = true;
        }
    }

    /// <summary>v9: the trail of step outlines, a pure function of the song beat since each landing.</summary>
    void StepTrail(bool world)
    {
        Grew = false;
        double beat = WorldMagic.FxBeat;
        bool running = world && (WorldMagic.FxPlaying || beat > 1e-6);
        int shown = 0;
        for (int i = 0; i < MarkCap; i++)
        {
            if (!marks[i].used) continue;
            float age = (float)(beat - marks[i].beat);
            if (!running || marks[i].tile == null || age < 0f || age > TrailBeats) { marks[i].used = false; continue; }
            float a = age / TrailBeats;
            EnsureTrail(shown);
            var t = trail[shown];
            t.position = marks[i].tile.Top + Vector3.up * 0.04f;
            t.localScale = new Vector3(marks[i].sx * (1f + 0.15f * a) * 2f, 1f, marks[i].sz * (1f + 0.15f * a) * 2f);
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            mpb.Clear();
            mpb.SetColor(ColorId, marks[i].c);
            mpb.SetFloat(IntensityId, Mathf.Pow(1f - a, 1.2f) * 0.9f);
            trailR[shown].SetPropertyBlock(mpb);
            shown++;
        }
        for (int i = shown; i < trail.Count; i++) if (trail[i].gameObject.activeSelf) trail[i].gameObject.SetActive(false);
        ActiveTrail = shown;
        if (shown > MaxTrail) MaxTrail = shown;
    }

    /// <summary>One frame: runners falling fast wear speed lines (every 0.09 s while they drop); v9: the step-outline trail.</summary>
    public void Step(float dt, bool world)
    {
        listClock -= dt;
        if (listClock <= 0f)
        {
            listClock = 0.5f;
            runners.Clear();
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.IsRunner) runners.Add(c);
        }
        StepTrail(world);
        if (!world || !WorldMagic.FxPlaying) return;
        for (int i = 0; i < runners.Count; i++)
        {
            var c = runners[i];
            if (c == null) continue;
            float y = c.transform.position.y, py;
            if (!lastBodyY.TryGetValue(c, out py)) { lastBodyY[c] = y; continue; }
            lastBodyY[c] = y;
            float vy = (y - py) / Mathf.Max(1e-4f, dt);
            float clock;
            if (!lineClock.TryGetValue(c, out clock)) clock = 0f;
            clock -= dt;
            if (vy < -3f && clock <= 0f) { clock = 0.09f; Fx.SpeedLines(c.transform.position + Vector3.up * 0.45f, Vector3.down, MagicTextures.FoamCream); FallLines++; }
            lineClock[c] = clock;
        }
    }
}
