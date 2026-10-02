using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §6, §12.3, §13.3, §13.5, §16.2): WorldMagic's v7 part — the looks of the v7 musical ideas, synced from the song like the
/// v6 towers and bridges (keyed by island index; a rebuild re-binds) and stepped in LateUpdate after the islands and cubes moved:
/// a <see cref="Terrace"/> under every grid whose ground is not the sea's (the heights), an <see cref="EchoLayer"/> for every octave layer of a
/// grid where layer cubes live (sky / deep glass copies) + the <see cref="EchoLinks"/> threads between echoes and their sources, a
/// <see cref="RewindFx"/> on every rewind island (time unwinding), a <see cref="LongGridGlow"/> on every extended grid (carry ≥ 1, any style — §21's
/// long grid: a rim light and a glow under the walking cubes while they walk), a <see cref="LaunchFx"/> on every launching island (§21.4: the rim
/// brightening through A's riser, sparkles on the target's first measure at the crash), and the shared <see cref="StairsFx"/> (steps lit, dust,
/// speed lines), <see cref="MirrorFx"/> (the flip), <see cref="SelectionFx"/> (the selection's ink outline, lift and shimmer) and
/// <see cref="SectionGlow"/> (a halo on B's section playhead). Varied notes (vary ≥ 1, pass ≥ 1) sparkle on their tile as they play; a cube that
/// appears in place pops (<see cref="AppearPop"/>). It also owns two more particle pools (dust puffs, bubbles), K's FxLift for the whole world
/// magic (the selection lift; set once a frame) and a per-effect cost meter (≤ +1 ms each is the budget).
/// §21 ("the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid"): no carry bridges, no flow
/// carpet, no fling, no paste flights any more.
/// </summary>
public partial class WorldMagic
{
    // ------------------------------------------------------------------ diagnostics
    /// <summary>Calls since Play (tests): Magic.Flip, Magic.Fling (retired: counted only), Magic.Appear pops, paste pops; varied-note sparkles.</summary>
    public int FlipCalls, FlingCalls, Appears, PastePops, VariedSparkles;
    /// <summary>Average main-thread milliseconds per frame of each v7 look (terraces, echo layers + links, rewinds, long grids, launches, stairs,
    /// the mirror, the selection, the section glow).</summary>
    public double MsTerraces, MsLayers, MsRewind, MsLongGrid, MsLaunch, MsStairs, MsMirror, MsSelection, MsGlow;
    /// <summary>v9 (R): the section-change and echo effects' average ms; the v9 effects' master switch (perf A/B).</summary>
    public double MsSections, MsEchoes;
    public static bool V9Fx = true;
    /// <summary>v9 (R): AddLift quantised to <see cref="LiftStep"/> u — KeyBlock.SetFxLift re-poses the island and re-bakes its cubes' lines (LineRenderers)
    /// on every change, so a bounce or a swell's tense re-bakes a handful of times, not every frame (the toon look steps anyway).</summary>
    public const float LiftStep = 0.03f;
    /// <summary>v9 (R): the most a v9 effect lifts an island (u) and the most any island is lifted in all (u; the selection's own 0.22 stays whole).</summary>
    public const float FxMaxLift = 0.2f, MaxLift = 0.25f;
    public void AddLiftStepped(KeyBlock kb, float y) { AddLift(kb, Mathf.Clamp(Mathf.Round(y / LiftStep) * LiftStep, -FxMaxLift, FxMaxLift)); }
    public IReadOnlyList<Terrace> Terraces => terraces;
    public IReadOnlyList<EchoLayer> Layers => layers;
    public IReadOnlyList<RewindFx> Rewinds => rewinds;
    public IReadOnlyList<LongGridGlow> LongGlows => longGlows;
    public IReadOnlyList<LaunchFx> Launches => launches;
    public int TerraceCount { get { int n = 0; foreach (var t in terraces) if (t != null && !t.Retiring) n++; return n; } }
    public int LayerCount { get { int n = 0; foreach (var l in layers) if (l != null && !l.Retiring) n++; return n; } }
    public int RewindCount { get { int n = 0; foreach (var r in rewinds) if (r != null && !r.Retiring) n++; return n; } }
    public int LongGlowCount { get { int n = 0; foreach (var r in longGlows) if (r != null && !r.Retiring) n++; return n; } }
    public int LaunchCount { get { int n = 0; foreach (var f in launches) if (f != null && !f.Retiring) n++; return n; } }
    public EchoLinks Links => links;
    public StairsFx Stairs => stairs;
    public MirrorFx Mirror => mirror;
    public SelectionFx Selection => selection;
    public SectionGlow Glow => sectionGlow;
    /// <summary>v9 (R): the section-change effects and the echo afterimages.</summary>
    public SectionFx Sections => sectionFx;
    public EchoFx Echoes => echoFx;
    /// <summary>v9 (R): the song beat the effects read — the presentation's beat while presenting (a scrub lands exactly), else the clock's — and
    /// whether the song runs.</summary>
    public static double FxBeat => Presenter.Active && Presenter.CurrentPhase == Presenter.Phase.Presenting ? Presenter.SongBeat : GlobalClock.SongBeatD;
    public static bool FxPlaying => GlobalClock.IsPlaying || (Presenter.Active && Presenter.CurrentPhase == Presenter.Phase.Presenting && !Presenter.Paused && Presenter.Clock == Presenter.ClockMode.Live);
    /// <summary>v9 (R): managed bytes WorldMagic's LateUpdate allocated last frame (−1 unknown), frames measured / allocating since Play (a frame
    /// where a pool grew is excused: PoolGrew), the largest.</summary>
    public long AllocLast = -1, AllocMax;
    public int AllocFrames, AllocMeasured, AllocExcused;
    /// <summary>v9 (R): the same meter over the v9 effects alone (launches, sections, echoes, stairs).</summary>
    public long FxAllocLast = -1, FxAllocMax;
    public int FxAllocFrames, FxAllocMeasured, FxAllocExcused;
    public bool PoolGrew { get; private set; }
    static System.Func<long> allocProbe; static bool allocTried;
    static long AllocNow()
    {
        if (!allocTried)
        {
            allocTried = true;
            try
            {
                var mi = typeof(System.GC).GetMethod("GetAllocatedBytesForCurrentThread", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mi != null) allocProbe = (System.Func<long>)System.Delegate.CreateDelegate(typeof(System.Func<long>), mi);
            }
            catch (System.Exception) { allocProbe = null; }
        }
        return allocProbe != null ? allocProbe() : -1L;
    }
    void MeterAlloc(long a0)
    {
        if (a0 < 0) { AllocLast = -1; return; }
        long used = AllocNow() - a0;
        AllocLast = used;
        PoolGrew = sectionFx.Grew || echoFx.Grew || stairs.Grew;
        if (PoolGrew) { AllocExcused++; return; }
        AllocMeasured++;
        if (used > 0) { AllocFrames++; if (used > AllocMax) AllocMax = used; }
    }
    public int LivePuffs => puffs != null ? puffs.particleCount : 0;
    public int LiveBubbles => bubbles != null ? bubbles.particleCount : 0;

    /// <summary>The terrace under <paramref name="kb"/>'s column (null without one).</summary>
    public Terrace TerraceOf(KeyBlock kb) { if (kb == null) return null; foreach (var t in terraces) if (t != null && !t.Retiring && t.Column == kb.column) return t; return null; }
    /// <summary>The terrace under column <paramref name="col"/> (null without one).</summary>
    public Terrace TerraceOfColumn(int col) { foreach (var t in terraces) if (t != null && !t.Retiring && t.Column == col) return t; return null; }
    public EchoLayer LayerOf(KeyBlock kb, int layer) { foreach (var l in layers) if (l != null && !l.Retiring && l.Island == kb && l.Layer == layer) return l; return null; }
    public RewindFx RewindOf(KeyBlock kb) { foreach (var r in rewinds) if (r != null && !r.Retiring && r.Island == kb) return r; return null; }
    public LongGridGlow LongGlowOf(KeyBlock kb) { foreach (var r in longGlows) if (r != null && !r.Retiring && r.Source == kb) return r; return null; }
    public LaunchFx LaunchOf(KeyBlock kb) { foreach (var f in launches) if (f != null && !f.Retiring && f.Island == kb) return f; return null; }

    // ------------------------------------------------------------------ state
    readonly List<Terrace> terraces = new List<Terrace>();
    readonly List<EchoLayer> layers = new List<EchoLayer>();
    readonly List<RewindFx> rewinds = new List<RewindFx>();
    readonly List<LongGridGlow> longGlows = new List<LongGridGlow>();
    readonly List<List<KeyBlock>> runScratch = new List<List<KeyBlock>>();
    readonly List<LaunchFx> launches = new List<LaunchFx>();
    // paste pops waiting for their moment (Magic.PasteFlight, §21)
    struct Pop { public Vector3 p; public Color c; public float at; }
    readonly List<Pop> pops = new List<Pop>();
    EchoLinks links; readonly StairsFx stairs = new StairsFx(); MirrorFx mirror; SelectionFx selection; SectionGlow sectionGlow;
    // v9 (R): section-change effects and echo afterimages
    readonly SectionFx sectionFx = new SectionFx(); readonly EchoFx echoFx = new EchoFx();
    ParticleSystem puffs, bubbles;
    readonly Dictionary<KeyBlock, float> liftAcc = new Dictionary<KeyBlock, float>();
    readonly List<KeyBlock> lifted = new List<KeyBlock>(), liftScratch = new List<KeyBlock>();
    readonly Dictionary<long, List<TileInteraction>> layerTiles = new Dictionary<long, List<TileInteraction>>();
    readonly Dictionary<long, int> layerCubes = new Dictionary<long, int>();
    readonly System.Diagnostics.Stopwatch sw7 = new System.Diagnostics.Stopwatch();
    double prevBeat7 = -1.0;
    float ribbonsClock;

    // ------------------------------------------------------------------ build / hooks
    void BuildV7()
    {
        puffs = MakePuffSystem("MagicPuffs", MagicTextures.Puff, -0.04f, true);
        bubbles = MakePuffSystem("MagicBubbles", MagicTextures.Bubble, -0.12f, false);
        links = NewChild<EchoLinks>("EchoLinks");
        mirror = NewChild<MirrorFx>("Mirror");
        selection = NewChild<SelectionFx>("Selection");
        sectionGlow = NewChild<SectionGlow>("SectionGlow");
        sectionFx.Build(transform);
        echoFx.Build(transform);
    }

    T NewChild<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<T>();
    }

    ParticleSystem MakePuffSystem(string name, Texture2D tex, float gravity, bool grow)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = 0.6f; main.startSpeed = 0f; main.startSize = 0.3f; main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 1500; main.playOnAwake = false; main.loop = false;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  grow ? new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.8f, 0.55f), new GradientAlphaKey(0f, 1f) }
                       : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, grow
            ? new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.35f))
            : new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 1.4f)));
        if (grow)
        {
            // dust slows down as it spreads (drag), turning a little
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = new ParticleSystem.MinMaxCurve(0f); lv.dampen = 0.08f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = Fx.Alpha(tex, Color.white);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.maxParticleSize = 0.25f;
        ps.Pause();
        return ps;
    }

    /// <summary>A dust puff (flat cream cloud, ink rim) drifting from <paramref name="pos"/>, slowing, growing and fading.</summary>
    public void Puff(Vector3 pos, Vector3 vel, Color c, float size, float life) { Emit(puffs, pos, vel, c, size, life); }
    /// <summary>A rising bubble (an ink ring round a pale fill) — the deep layer's.</summary>
    public void Bubble(Vector3 pos, Vector3 vel, float size, float life) { Emit(bubbles, pos, vel, new Color(0.88f, 0.97f, 1f), size, life); }

    void StepParticles7(float step)
    {
        if (puffs != null) puffs.Simulate(step, true, false, false);
        if (bubbles != null) bubbles.Simulate(step, true, false, false);
    }

    void HookV7(SongManager sm)
    {
        sm.OnGroundsChanged += MarkDirty;
        sm.OnRepeatStyleChanged += MarkDirtyKb;
        sm.OnLaunchChanged += MarkDirtyKb;
        sm.OnStairChanged += MarkDirtyKb;
        sm.OnSectionsChanged += MarkDirty;
        sm.OnLongGridsChanged += MarkDirty;
    }

    void UnhookV7(SongManager sm)
    {
        sm.OnGroundsChanged -= MarkDirty;
        sm.OnRepeatStyleChanged -= MarkDirtyKb;
        sm.OnLaunchChanged -= MarkDirtyKb;
        sm.OnStairChanged -= MarkDirtyKb;
        sm.OnSectionsChanged -= MarkDirty;
        sm.OnLongGridsChanged -= MarkDirty;
    }

    void MarkDirty() { dirty = true; }
    void MarkDirtyKb(KeyBlock kb) { dirty = true; }

    // ------------------------------------------------------------------ Magic.Flip / Magic.Fling
    /// <summary>Magic.Flip: the mirror sweeps over <paramref name="c"/>'s island.</summary>
    public void FlipSweep(AudioCube c) { FlipCalls++; if (mirror != null) mirror.Begin(c); }

    /// <summary>Magic.Fling — retired by §21 (nothing flings): counted only.</summary>
    public void FlingStart(AudioCube c, KeyBlock from, KeyBlock to, float seconds) { FlingCalls++; }

    /// <summary>Magic.Appear (§21.1): <paramref name="c"/> appears on its grid — a ring and a star burst at its tile, a bright star, its path's tiles
    /// glinting one after another (path order, 0.035 s apart).</summary>
    public void AppearPop(AudioCube c)
    {
        Appears++;
        if (c == null) return;
        var tile = c.nodes.Count > 0 ? c.nodes[Mathf.Clamp(c.HomeNode, 0, c.nodes.Count - 1)] : null;
        Vector3 p = tile != null ? c.TopOf(tile) : c.transform.position;
        Color pale = Color.Lerp(c.Color, Color.white, 0.55f);
        Fx.Ripple(p + Vector3.up * 0.03f, c.Color, 0.9f, 0.38f);
        for (int i = 0; i < 10; i++)
        {
            float a = (i / 10f) * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Sparkle(p + Vector3.up * 0.25f + d * 0.2f, d * Random.Range(1.2f, 2.1f) + Vector3.up * Random.Range(1f, 2f), i % 2 == 0 ? c.Color : pale, Random.Range(0.15f, 0.26f), Random.Range(0.35f, 0.55f));
        }
        Sparkle(p + Vector3.up * 0.45f, Vector3.up * 0.5f, Color.white, 0.5f, 0.3f);
        // its path glints bead by bead (where it plays)
        for (int i = 0; i < c.nodes.Count; i++) { var t = c.nodes[i]; if (t != null) glints.Add(new Glint { tile = t, c = pale, at = Time.time + 0.035f * i }); }
    }

    struct Glint { public TileInteraction tile; public Color c; public float at; }
    readonly List<Glint> glints = new List<Glint>();
    /// <summary>Tiles glinted by appear pops since Play (tests).</summary>
    public int AppearGlints;

    /// <summary>A paste pop (Magic.PasteFlight, §21: in place of a flying bead) at <paramref name="p"/> after <paramref name="delay"/> seconds.</summary>
    public void PopAt(Vector3 p, Color c, float delay) { pops.Add(new Pop { p = p, c = c, at = Time.time + Mathf.Max(0f, delay) }); }

    void StepPops()
    {
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            if (Time.time < pops[i].at) continue;
            var q = pops[i];
            pops.RemoveAt(i);
            PastePops++;
            Fx.Ripple(q.p, q.c, 0.6f, 0.3f);
            for (int k = 0; k < 5; k++) Sparkle(q.p + Vector3.up * 0.1f, Random.insideUnitSphere * 0.8f + Vector3.up * 1.1f, k % 2 == 0 ? q.c : Color.white, 0.15f, 0.4f);
        }
        for (int i = glints.Count - 1; i >= 0; i--)
        {
            if (Time.time < glints[i].at) continue;
            var g = glints[i];
            glints.RemoveAt(i);
            if (g.tile != null) { g.tile.Flash(g.c, 0.7f); AppearGlints++; }
        }
    }

    // ------------------------------------------------------------------ sync
    /// <summary>The v7 part of Sync: terraces, echo layers, rewinds, flow ribbons, flings (<paramref name="firstOfFrame"/>: the frame's first sync —
    /// the flow carries shown until now become "before" for the gesture).</summary>
    void SyncV7(SongManager sm, bool firstOfFrame)
    {
        int n = sm.Islands.Count;
        // terraces: one per column whose ground (or the ground its anchor still shows) is not the sea's level
        int nc = sm.ColumnCount;
        for (int c = 0; c < nc; c++)
        {
            var kb = sm.AnchorOf(c);
            float target = sm.GroundOf(c);
            bool want = kb != null && !kb.IsMoon && (Mathf.Abs(target) > 0.01f || Mathf.Abs(kb.GroundY) > 0.02f);
            Terrace t = null;
            foreach (var o in terraces) if (o != null && !o.Retiring && o.Column == c) { t = o; break; }
            if (want)
            {
                if (t == null) { t = new GameObject("Terrace_" + c).AddComponent<Terrace>(); t.transform.SetParent(transform, false); terraces.Add(t); }
                t.Bind(kb, c, target);
            }
            else if (t != null) t.Retire();
        }
        foreach (var o in terraces) if (o != null && !o.Retiring && o.Column >= nc) o.Retire();

        // echo layers: (island, layer) pairs where layer cubes live, with the distinct tiles their paths use
        foreach (var l in layerTiles.Values) l.Clear();
        layerCubes.Clear();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.layer == 0 || c.IsOnMoon || c.Island == null || c.nodes.Count == 0) continue;
            int idx = sm.Islands.IndexOf(c.Island);
            if (idx < 0) continue;
            long key = (long)idx * 16 + (c.layer + 8);
            int cnt; layerCubes.TryGetValue(key, out cnt); layerCubes[key] = cnt + 1;
            List<TileInteraction> tl;
            if (!layerTiles.TryGetValue(key, out tl)) { tl = new List<TileInteraction>(); layerTiles[key] = tl; }
            foreach (var t in c.nodes) if (t != null && !tl.Contains(t)) tl.Add(t);
        }
        foreach (var l in layers) if (l != null && !l.Retiring) { long key = (long)l.Index * 16 + (l.Layer + 8); if (!layerCubes.ContainsKey(key) || l.Index >= n) l.Retire(); }
        foreach (var kv in layerCubes)
        {
            int idx = (int)(kv.Key / 16), layer = (int)(kv.Key % 16) - 8;
            var kb = sm.Islands[idx];
            EchoLayer el = null;
            foreach (var o in layers) if (o != null && !o.Retiring && o.Index == idx && o.Layer == layer) { el = o; break; }
            if (el == null) { el = new GameObject("EchoLayer_" + idx + (layer > 0 ? "_up" : "_down") + Mathf.Abs(layer)).AddComponent<EchoLayer>(); el.transform.SetParent(transform, false); layers.Add(el); }
            el.Bind(kb, idx, layer, kv.Value, layerTiles[kv.Key]);
        }

        // rewinds, flings
        for (int i = 0; i < n; i++)
        {
            var kb = sm.Islands[i];
            bool want = kb != null && !kb.IsMoon && kb.rewind && kb.Passes >= 2;
            RewindFx r = null;
            foreach (var o in rewinds) if (o != null && !o.Retiring && o.Index == i) { r = o; break; }
            if (want) { if (r == null) { r = new GameObject("Rewind_" + i).AddComponent<RewindFx>(); r.transform.SetParent(transform, false); rewinds.Add(r); } r.Bind(kb, i); }
            else if (r != null) r.Retire();

            bool fl = kb != null && !kb.IsMoon && kb.launch;
            LaunchFx f = null;
            foreach (var o in launches) if (o != null && !o.Retiring && o.Index == i) { f = o; break; }
            if (fl) { if (f == null) { f = new GameObject("Launch_" + i).AddComponent<LaunchFx>(); f.transform.SetParent(transform, false); launches.Add(f); } f.Bind(kb, i); }
            else if (f != null) f.Retire();
        }
        foreach (var o in rewinds) if (o != null && !o.Retiring && o.Index >= n) o.Retire();
        foreach (var o in launches) if (o != null && !o.Retiring && o.Index >= n) o.Retire();

        // long grids (§21: every carry, any style, is a long grid — K joins it into one run, H walks it; W lights it while it walks): one light per
        // run of K's (SongManager.LongGrids, the source first; a chain of extends is one run), else per carrying island (source + CarryTargets)
        runScratch.Clear();
        foreach (var run in sm.LongGrids) if (run != null && run.Count >= 2 && run[0] != null) runScratch.Add(run);
        bool kRuns = runScratch.Count > 0;
        for (int i = 0; i < n; i++)
        {
            var kb = sm.Islands[i];
            List<KeyBlock> members = null;
            if (kRuns) { foreach (var run in runScratch) if (run[0] == kb) { members = run; break; } }
            else if (kb != null && !kb.IsMoon && kb.carry >= 1) members = sm.CarryTargets(i);
            LongGridGlow r = null;
            foreach (var o in longGlows) if (o != null && !o.Retiring && o.SourceIndex == i) { r = o; break; }
            if (members != null && (kRuns || members.Count >= 1))
            {
                if (r == null) { r = new GameObject("LongGrid_" + i).AddComponent<LongGridGlow>(); r.transform.SetParent(transform, false); longGlows.Add(r); }
                r.Bind(kb, i);
                r.SetTargets(members);
            }
            else if (r != null) r.Retire();
        }
        foreach (var o in longGlows) if (o != null && !o.Retiring && o.SourceIndex >= n) o.Retire();
        if (mirror != null) mirror.Snapshot();
        sectionFx.Sync(sm);   // v9: the section starts, energies and lanes
    }

    /// <summary>OnCarryChanged: nothing to cast any more (§21: the long grid has no spell; K joins it, the light follows the walk).</summary>
    void CarryV7(KeyBlock kb, int idx) { }

    /// <summary>What the v7 looks depend on besides the v6 fingerprint: carry style, repeat style / passes, vary, launch, kind, the column's ground
    /// target; every cube's layer / source link / island (a copy made, a layer changed).</summary>
    int SignatureV7(SongManager sm)
    {
        unchecked
        {
            int h = 7;
            foreach (var kb in sm.Islands)
            {
                if (kb == null) continue;
                h = h * 31 + kb.carryStyle * 3 + (kb.rewind ? 1 : 0) + kb.Passes * 5 + kb.vary * 7 + (kb.launch ? 11 : 0) + kb.kind * 13 + kb.energy * 19;   // v9: + energy (the section effects' size)
                if (!kb.IsMoon) h = h * 31 + Mathf.RoundToInt(sm.GroundOf(kb.column) * 100f) + (Mathf.Abs(kb.GroundY) > 0.02f ? 17 : 0);
            }
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || (c.layer == 0 && c.echoOf < 0)) continue;
                h = h * 31 + c.layer * 5 + c.echoOf + c.nodes.Count * 3 + (c.Island != null ? c.Island.GetInstanceID() : 0);
            }
            return h;
        }
    }

    // ------------------------------------------------------------------ frame
    static double Avg(double avg, double v) => avg <= 0.0 ? v : avg + (v - avg) * 0.05;
    double Lap() { double ms = sw7.Elapsed.TotalMilliseconds; sw7.Reset(); sw7.Start(); return ms; }

    void StepV7(float dt, bool world, SongManager sm)
    {
        double beat = FxBeat;        // v9: the presentation's beat while presenting
        bool playing = FxPlaying;
        bool fxWorld = V9Fx && sm != null && sm.HasSong;   // v9: the launch / section / echo / stairs effects play in the present mode too
        double prev = prevBeat7 < 0.0 ? beat : prevBeat7;
        sw7.Reset(); sw7.Start();
        for (int i = terraces.Count - 1; i >= 0; i--) { var t = terraces[i]; if (t == null) { terraces.RemoveAt(i); continue; } t.Step(dt, world); }
        MsTerraces = Avg(MsTerraces, Lap());
        for (int i = layers.Count - 1; i >= 0; i--) { var l = layers[i]; if (l == null) { layers.RemoveAt(i); continue; } l.Step(dt, world); }
        if (links != null) links.Step(dt, world);
        MsLayers = Avg(MsLayers, Lap());
        for (int i = rewinds.Count - 1; i >= 0; i--) { var r = rewinds[i]; if (r == null) { rewinds.RemoveAt(i); continue; } r.Step(dt, world, beat, prev, playing); }
        MsRewind = Avg(MsRewind, Lap());
        ribbonsClock += dt;
        if (ribbonsClock > 0.5f && sm != null && world)
        {
            ribbonsClock = 0f;
            foreach (var r in longGlows)
            {
                if (r == null || r.Retiring || r.SourceIndex >= sm.Islands.Count) continue;
                var run = sm.LongGridOf(r.Source);
                r.SetTargets(run != null ? run : sm.CarryTargets(r.SourceIndex));
            }
        }
        for (int i = longGlows.Count - 1; i >= 0; i--) { var r = longGlows[i]; if (r == null) { longGlows.RemoveAt(i); continue; } r.Step(dt, world, beat, playing); }
        MsLongGrid = Avg(MsLongGrid, Lap());
        long fa0 = AllocNow();
        for (int i = launches.Count - 1; i >= 0; i--) { var f = launches[i]; if (f == null) { launches.RemoveAt(i); continue; } f.Step(dt, fxWorld, beat, prev, playing); }
        StepPops();
        MsLaunch = Avg(MsLaunch, Lap());
        sectionFx.Step(fxWorld, beat, prev, playing, this);
        MsSections = Avg(MsSections, Lap());
        echoFx.Step(fxWorld, beat, playing);
        MsEchoes = Avg(MsEchoes, Lap());
        stairs.Step(dt, fxWorld);
        if (fa0 >= 0)
        {
            long used = AllocNow() - fa0; FxAllocLast = used;
            if (sectionFx.Grew || echoFx.Grew || stairs.Grew) FxAllocExcused++;
            else { FxAllocMeasured++; if (used > 0) { FxAllocFrames++; if (used > FxAllocMax) FxAllocMax = used; } }
        }
        MsStairs = Avg(MsStairs, Lap());
        if (mirror != null) mirror.Step(dt, world);
        MsMirror = Avg(MsMirror, Lap());
        if (selection != null) selection.Step(dt, world);
        MsSelection = Avg(MsSelection, Lap());
        if (sectionGlow != null) sectionGlow.Step(dt, world);
        MsGlow = Avg(MsGlow, Lap());
        ApplyLifts();
        for (int i = 0; i < launches.Count; i++) { var f = launches[i]; if (f != null) f.PostLift(); }   // v9: the launching island's lean, after the lifts re-posed it
        sw7.Stop();
        prevBeat7 = beat;
    }

    /// <summary>Adds <paramref name="y"/> to <paramref name="kb"/>'s visual lift this frame (the selection's lift); WorldMagic sets it through K's
    /// KeyBlock.SetFxLift once a frame and puts it back to exactly 0 when nobody lifts it. A member of a LONG GRID (§21.2: its joined look is
    /// physical — members join only while their platforms meet exactly) lifts its whole long grid (SongManager.LongGridOf) by the largest lift
    /// any member got this frame, so the long grid rises and lands as one and stays joined.</summary>
    public void AddLift(KeyBlock kb, float y)
    {
        if (kb == null) return;
        float v; liftAcc.TryGetValue(kb, out v);
        liftAcc[kb] = v + y;
    }

    void ApplyLifts()
    {
        // a long grid lifts as one: every member gets the largest lift of its members
        var sm = SongManager.I;
        if (sm != null && liftAcc.Count > 0)
        {
            liftScratch.Clear();
            foreach (var kv in liftAcc) if (kv.Key != null) liftScratch.Add(kv.Key);
            foreach (var kb in liftScratch)
            {
                var run = sm.LongGridOf(kb);
                if (run == null) continue;
                float most = 0f;
                foreach (var m in run) { float v; if (m != null && liftAcc.TryGetValue(m, out v) && Mathf.Abs(v) > Mathf.Abs(most)) most = v; }
                foreach (var m in run) if (m != null) liftAcc[m] = most;
            }
        }
        foreach (var kv in liftAcc) if (kv.Key != null) kv.Key.SetFxLift(Mathf.Clamp(kv.Value, -MaxLift, MaxLift));   // v9: lifts never stack past MaxLift
        foreach (var kb in lifted) if (kb != null && !liftAcc.ContainsKey(kb)) kb.SetFxLift(0f);
        lifted.Clear();
        foreach (var kv in liftAcc) if (kv.Key != null) lifted.Add(kv.Key);
        liftAcc.Clear();
    }

    /// <summary>Every landing (AudioCube.OnLanded): a runner on a step, a layer cube's note, the echo links, a varied note.</summary>
    void LandedV7(AudioCube cube, int w, int k, AudioCube.Hit hit)
    {
        if (!hit.fires || hit.node < 0) { if (links != null) links.OnLanded(cube); return; }
        var win = cube.windows[w];
        TileInteraction tile = cube.TileAt(w, hit.node);
        if (tile != null && tile.island != null && tile.island.IsStairs) stairs.OnLanded(this, cube, tile, hit.node == cube.EndNode);
        // v9 (R): an echoing note (its repeats at the step length) and an echo copy's note get afterimages
        if (tile != null && !cube.IsDrums)
        {
            if (hit.echo > 0) echoFx.Fire(cube, tile, FxBeat, cube.HasDurations ? cube.StepLenBeats(k) : cube.StepBeats, hit.echo);
            else if (cube.echoOf >= 0) echoFx.Fire(cube, tile, FxBeat, 0f, 1);
        }
        if (cube.layer != 0 && tile != null)
        {
            var l = LayerOf(tile.island, cube.layer);
            if (l != null) l.OnNote(tile, cube.Color);
            else for (int i = 0; i < 4; i++) Sparkle(cube.transform.position, Random.insideUnitSphere * 0.8f + Vector3.up, i == 0 ? Color.white : cube.Color, 0.16f, 0.4f);
        }
        if (links != null) links.OnLanded(cube);
        // a varied note ("the differences" pop): H's AudioCube.IsVaried — a pass ≥ 1 of a vary island playing another note than the first pass
        if (tile != null && cube.IsVaried(w, hit.node)) VariedSparkle(tile, cube.Color);
    }

    void VariedSparkle(TileInteraction tile, Color c)
    {
        VariedSparkles++;
        Vector3 p = tile.Top + Vector3.up * 0.12f;
        Color pale = Color.Lerp(c, Color.white, 0.55f);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Sparkle(p + d * 0.25f, d * 1.1f + Vector3.up * 1.3f, i % 2 == 0 ? pale : Color.white, 0.17f, 0.42f);
        }
        Sparkle(p + Vector3.up * 0.3f, Vector3.up * 0.5f, Color.white, 0.5f, 0.3f);
        Fx.Ripple(p, c, 0.55f, 0.3f);
    }
}
