using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v6 package W (SPEC v6 §6): the world's magic, one MonoBehaviour created at runtime (after the scene loads) that follows SongManager
/// (OnSongRebuilt, OnColumnsChanged, OnCarryChanged) and owns one <see cref="Tower"/> per island with register ≠ 0 (the octave tower: a stone
/// pillar pushing up out of the sea with waves on its turn, a whirl under a lowered island) and one <see cref="CarryBridge"/> per island with
/// carry ≥ 1 (the sigils, the dotted arcs of light, the ghost stars on the carried tiles, the spell when the carry is raised or lowered),
/// created / retired as the song changes (keyed by island index: a rebuild re-creates the KeyBlocks and the looks re-bind). It also holds the
/// shared effect pools behind <see cref="Magic"/>: star sparkles (floaty), falling sparks, water droplets (all flat, ink-rimmed, simulated on
/// twos like Fx's confetti), the spreading wave crests on the sea, the inward ripples of a whirl, paste beads and the sparkle trails of flying
/// cubes. Everything is posed in LateUpdate, after the islands, the cubes and the comet moved (KeyBlock runs at order −20), and its cost is
/// measured (<see cref="AvgMs"/>).
/// v7 (SPEC v7 §6, package W): the v7 looks live in WorldMagic7.cs (terraces, echo layers + links, rewinds, long grids, launches, the stairs'
/// dust and speed lines, the flip mirror, the selection, the section glow); here they are built, hooked, synced and stepped after the v6 looks.
/// v7 §21 ("the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid"): the v6 CARRY BRIDGE is
/// retired — a carry is now K's long grid (one joined platform the cubes walk through), so no bridge, arc, sigil or ghost star is ever built
/// (<see cref="BridgeOf"/> answers null; the CarryBridge class stays only for old callers).
/// </summary>
[DefaultExecutionOrder(950)]
public partial class WorldMagic : MonoBehaviour
{
    public static WorldMagic I { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { Ensure(); }

    /// <summary>The instance (created on first use in Play mode).</summary>
    public static WorldMagic Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<WorldMagic>();
            if (I == null && Application.isPlaying) I = new GameObject("WorldMagic").AddComponent<WorldMagic>();
        }
        return I;
    }

    /// <summary>World y of the sea plane (Fx.BuildSea).</summary>
    public const float SeaY = -4f;

    // ------------------------------------------------------------------ diagnostics
    /// <summary>Milliseconds of main-thread work in the last LateUpdate (towers, bridges, pools) and its running average.</summary>
    public double LastMs { get; private set; }
    public double AvgMs { get; private set; }
    public int TowerCount { get { int n = 0; foreach (var t in towers) if (t != null && !t.Retiring) n++; return n; } }
    public int BridgeCount { get { int n = 0; foreach (var b in bridges) if (b != null && !b.Retiring) n++; return n; } }
    public int ActiveCrests { get { int n = 0; foreach (var c in crests) if (c.active) n++; return n; } }
    public int ActiveInward { get { int n = 0; foreach (var c in inward) if (c.active) n++; return n; } }
    public int ActiveBeads { get { int n = 0; foreach (var b in beads) if (b.active) n++; return n; } }
    public int ActiveTrails { get { int n = 0; foreach (var t in trails) if (t.active) n++; return n; } }
    public int LiveSparkles => (sparkles != null ? sparkles.particleCount : 0) + (sparks != null ? sparks.particleCount : 0);
    public int LiveDrops => drops != null ? drops.particleCount : 0;
    /// <summary>Counters since Play (tests): crests spawned, inward ripples, paste beads launched / landed, landings, trails started.</summary>
    public int CrestsSpawned, InwardSpawned, BeadsLaunched, BeadsLanded, Lands, TrailsStarted;
    public IReadOnlyList<Tower> Towers => towers;
    public IReadOnlyList<CarryBridge> Bridges => bridges;

    /// <summary>The live (not retiring) tower of <paramref name="kb"/> (null without one).</summary>
    public Tower TowerOf(KeyBlock kb) { foreach (var t in towers) if (t != null && !t.Retiring && t.Island == kb) return t; return null; }
    /// <summary>The live (not retiring) bridge carried from <paramref name="kb"/> (null without one).</summary>
    public CarryBridge BridgeOf(KeyBlock kb) { foreach (var b in bridges) if (b != null && !b.Retiring && b.Source == kb) return b; return null; }

    readonly List<Tower> towers = new List<Tower>();
    readonly List<CarryBridge> bridges = new List<CarryBridge>();
    readonly Dictionary<int, int> carryNow = new Dictionary<int, int>(), carryBefore = new Dictionary<int, int>();
    SongManager hooked;
    bool dirty = true;
    int signature, syncFrame = -1;
    float targetsClock;
    readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        BuildParticles();
        BuildV7();
    }

    void OnEnable()
    {
        GlobalClock.OnBeat += HandleBeat;
        GlobalClock.OnSeek += HandleJump; GlobalClock.OnStop += HandleJump; GlobalClock.OnPlay += HandleJump;
        AudioCube.OnLanded += HandleLanded;
        AudioCube.OnAnyChanged += HandleCubeChanged;
    }

    void OnDisable()
    {
        GlobalClock.OnBeat -= HandleBeat;
        GlobalClock.OnSeek -= HandleJump; GlobalClock.OnStop -= HandleJump; GlobalClock.OnPlay -= HandleJump;
        AudioCube.OnLanded -= HandleLanded;
        AudioCube.OnAnyChanged -= HandleCubeChanged;
        Unhook();
    }

    void OnDestroy() { if (I == this) I = null; }

    void Hook()
    {
        var sm = SongManager.I;
        if (sm == hooked) return;
        Unhook();
        if (sm == null) return;
        sm.OnSongRebuilt += HandleRebuilt;
        sm.OnColumnsChanged += HandleColumns;
        sm.OnCarryChanged += HandleCarry;
        HookV7(sm);
        hooked = sm;
        dirty = true;
    }

    void Unhook()
    {
        if (hooked == null) return;
        hooked.OnSongRebuilt -= HandleRebuilt;
        hooked.OnColumnsChanged -= HandleColumns;
        hooked.OnCarryChanged -= HandleCarry;
        UnhookV7(hooked);
        hooked = null;
    }

    // the rebuild re-created every KeyBlock: re-bind at once (SetCarry raises OnCarryChanged right after it, before any frame is drawn)
    void HandleRebuilt() { Sync(); }
    void HandleColumns() { dirty = true; }
    void HandleCarry(KeyBlock kb)
    {
        if (syncFrame != Time.frameCount) Sync();   // (the rebuild that raised it has synced already)
        var sm = SongManager.I;
        if (sm == null || kb == null) return;
        int idx = sm.Islands.IndexOf(kb);
        if (idx < 0) return;
        int before;
        if (!carryBefore.TryGetValue(idx, out before)) before = 0;
        int now;
        if (!carryNow.TryGetValue(idx, out now)) now = 0;
        foreach (var b in bridges) if (b != null && b.SourceIndex == idx && (b.Source == kb || b.Retiring)) b.Gesture(before, now);
        CarryV7(kb, idx);
    }
    void HandleBeat(int beat, bool downbeat) { if (!downbeat) return; foreach (var t in towers) if (t != null) t.OnDownbeat(); }
    void HandleJump() { foreach (var b in bridges) if (b != null) b.OnJump(); foreach (var t in trails) if (t.active && t.lr != null) t.lr.Clear(); }
    void HandleLanded(AudioCube cube, int w, int k, AudioCube.Hit hit)
    {
        if (cube == null || w < 0 || w >= cube.windows.Count) return;
        LandedV7(cube, w, k, hit);
        var win = cube.windows[w];
        if (win.island == null || win.island == cube.Island) return;
        foreach (var b in bridges) if (b != null && !b.Retiring && b.Source == cube.Island) b.OnLanded(cube, w, hit);
    }
    void HandleCubeChanged(AudioCube c)
    {
        foreach (var b in bridges) if (b != null) b.MarkStarsDirty();
        if (mirror != null) mirror.OnCubeChanged(c);
        if (c != null && c.layer != 0) dirty = true;   // v7: a layer cube's path moved (the layer's threads follow)
    }

    /// <summary>Re-reads the song: a tower for every island with register ≠ 0, a bridge for every island with carry ≥ 1 (their targets from
    /// SongManager.CarryTargets); the others retire (a tower sinks its pillar into the sea, a bridge retracts its arcs and fizzles).</summary>
    public void Sync()
    {
        dirty = false;
        var sm = SongManager.I;
        if (sm == null) return;
        int n = sm.Islands.Count;
        bool firstOfFrame = syncFrame != Time.frameCount;
        // towers
        for (int i = 0; i < n; i++)
        {
            var kb = sm.Islands[i];
            bool want = kb != null && !kb.IsMoon && kb.register != 0;
            Tower t = null;
            foreach (var o in towers) if (o != null && !o.Retiring && o.Index == i) { t = o; break; }
            if (want)
            {
                if (t == null) { t = new GameObject("Tower_" + i).AddComponent<Tower>(); t.transform.SetParent(transform, false); towers.Add(t); }
                t.Bind(kb, i);
            }
            else if (t != null) t.Retire();
        }
        foreach (var o in towers) if (o != null && !o.Retiring && o.Index >= n) o.Retire();
        // bridges ("before" = the carries at the frame's first sync: a rebuild and the OnCarryChanged after it, or several SetCarry calls in one
        // frame, all compare with what was shown)
        if (firstOfFrame)
        {
            carryBefore.Clear();
            foreach (var kv in carryNow) carryBefore[kv.Key] = kv.Value;
        }
        syncFrame = Time.frameCount;
        carryNow.Clear();
        for (int i = 0; i < n; i++)
        {
            var kb = sm.Islands[i];
            // v7 §21: a carry is a long grid (K's joined platform; W's LongGridGlow lights its walk): no bridge, ever
            int c = 0;
            carryNow[i] = c;
            CarryBridge b = null;
            foreach (var o in bridges) if (o != null && !o.Retiring && o.SourceIndex == i) { b = o; break; }
            if (c >= 1)
            {
                if (b == null) { b = new GameObject("CarryBridge_" + i).AddComponent<CarryBridge>(); b.transform.SetParent(transform, false); bridges.Add(b); }
                b.Bind(kb, i);
                b.SetTargets(sm.CarryTargets(i), sm);
            }
            else if (b != null) b.Retire(false);
        }
        foreach (var o in bridges) if (o != null && !o.Retiring && o.SourceIndex >= n) o.Retire(false);
        SyncV7(sm, firstOfFrame);
        signature = Signature(sm);
    }

    /// <summary>A cheap fingerprint of what the looks depend on (island identity, register, carry, column, sleep; v7: WorldMagic7.SignatureV7):
    /// a change the events missed re-syncs (e.g. SetRegister's live change, which raises OnColumnsChanged, or a test poking a field).</summary>
    int Signature(SongManager sm)
    {
        unchecked
        {
            int h = 17 + sm.Islands.Count;
            foreach (var kb in sm.Islands)
            {
                if (kb == null) { h = h * 31 + 7; continue; }
                h = h * 31 + kb.GetInstanceID();
                h = h * 31 + kb.register * 5 + kb.carry;
                h = h * 31 + kb.column;
            }
            return h * 31 + SignatureV7(sm);
        }
    }

    // ------------------------------------------------------------------ frame
    void LateUpdate()
    {
        long a0 = AllocNow();   // v9 (R): the per-frame allocation meter (tests: zero in steady state)
        sw.Reset(); sw.Start();
        Hook();
        var sm = SongManager.I;
        float dt = Time.deltaTime;
        bool world = sm != null && sm.HasSong && !Presenter.Active;
        if (sm != null && (dirty || Signature(sm) != signature)) Sync();
        if (world)
        {
            targetsClock += dt;
            if (targetsClock > 0.5f) { targetsClock = 0f; foreach (var b in bridges) if (b != null && !b.Retiring) b.SetTargets(sm.CarryTargets(b.SourceIndex), sm); }
        }
        for (int i = towers.Count - 1; i >= 0; i--)
        {
            var t = towers[i];
            if (t == null) { towers.RemoveAt(i); continue; }
            t.Step(dt, world);
        }
        for (int i = bridges.Count - 1; i >= 0; i--)
        {
            var b = bridges[i];
            if (b == null) { bridges.RemoveAt(i); continue; }
            b.Step(dt, world);
        }
        StepV7(dt, world, sm);
        StepCrests(dt);
        StepInward(dt);
        StepBeads(dt);
        StepTrails(dt);
        StepParticles(dt);
        sw.Stop();
        MeterAlloc(a0);
        LastMs = sw.Elapsed.TotalMilliseconds;
        AvgMs = AvgMs <= 0.0 ? LastMs : AvgMs + (LastMs - AvgMs) * 0.05;
    }

    // ------------------------------------------------------------------ particles: sparkles (floaty stars), sparks (falling), droplets
    ParticleSystem sparkles, sparks, drops;
    float twosClock;

    void BuildParticles()
    {
        sparkles = MakeSystem("MagicSparkles", MagicTextures.Sparkles, -0.06f, 0f, 0.499f, false);
        sparks = MakeSystem("MagicSparks", MagicTextures.Sparkles, 0.9f, 0f, 0.999f, false);
        drops = MakeSystem("MagicDrops", MagicTextures.Droplet, 1.5f, 0f, 0f, true);
    }

    ParticleSystem MakeSystem(string name, Texture2D tex, float gravity, float f0, float f1, bool stretch)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = 0.6f; main.startSpeed = 0f; main.startSize = 0.2f; main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 3000; main.playOnAwake = false; main.loop = false;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, stretch
            ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.85f), new Keyframe(1f, 0.2f))
            : new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1.25f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (!stretch)
        {
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var tsa = ps.textureSheetAnimation; tsa.enabled = true;
            tsa.numTilesX = 2; tsa.numTilesY = 2; tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(f0, f1);   // a random shape per piece, held for its life
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }
        else
        {
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.6f;
        }
        r.material = Fx.Alpha(tex, Color.white);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.maxParticleSize = 0.2f;
        ps.Pause();
        return ps;
    }

    static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, Color c, float size, float life)
    {
        if (ps == null) return;
        var ep = new ParticleSystem.EmitParams { position = pos, velocity = vel, startColor = c, startSize = size, startLifetime = life, rotation = UnityEngine.Random.Range(0f, 360f) };
        ps.Emit(ep, 1);
    }
    /// <summary>A floaty star sparkle (four- or five-point, ink rim) at <paramref name="pos"/>.</summary>
    public void Sparkle(Vector3 pos, Vector3 vel, Color c, float size, float life) { Emit(sparkles, pos, vel, c, size, life); }
    /// <summary>A falling spark (any sparkle shape, gravity).</summary>
    public void Spark(Vector3 pos, Vector3 vel, Color c, float size, float life) { Emit(sparks, pos, vel, c, size, life); }
    /// <summary>A water droplet (stretched along its velocity, gravity).</summary>
    public void Drop(Vector3 pos, Vector3 vel, Color c, float size, float life) { Emit(drops, pos, vel, c, size, life); }

    void StepParticles(float dt)
    {
        twosClock += dt;
        if (Look.OnTwos && twosClock < 1f / Look.TwosFps) return;
        float step = twosClock; twosClock = 0f;
        if (sparkles != null) sparkles.Simulate(step, true, false, false);
        if (sparks != null) sparks.Simulate(step, true, false, false);
        if (drops != null) drops.Simulate(step, true, false, false);
        StepParticles7(step);
    }

    // ------------------------------------------------------------------ bursts
    /// <summary>The landing of a magic note (Magic.Land): a flat ink ring on the tile and a ring of star sparkles bursting out and up.</summary>
    public void LandBurst(Vector3 p, Color c)
    {
        Lands++;
        Color pale = Color.Lerp(c, Color.white, 0.6f);
        const int n = 10;
        float a0 = UnityEngine.Random.Range(0f, 360f);
        for (int i = 0; i < n; i++)
        {
            float a = (a0 + i * 360f / n) * Mathf.Deg2Rad;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Sparkle(p + Vector3.up * 0.12f, d * UnityEngine.Random.Range(1.3f, 2.3f) + Vector3.up * UnityEngine.Random.Range(1f, 2.4f), i % 2 == 0 ? c : pale, UnityEngine.Random.Range(0.17f, 0.28f), UnityEngine.Random.Range(0.4f, 0.65f));
        }
        Sparkle(p + Vector3.up * 0.3f, Vector3.up * 0.6f, Color.white, 0.46f, 0.32f);
        Fx.Ripple(p, c, 0.75f, 0.38f);
    }

    /// <summary>Fizzling sparks (a retracting arc, a sigil that un-draws): a small shower falling from <paramref name="p"/>.</summary>
    public void Fizzle(Vector3 p, Color c, int count)
    {
        Color pale = Color.Lerp(c, Color.white, 0.55f);
        for (int i = 0; i < count; i++)
            Spark(p, UnityEngine.Random.insideUnitSphere * 1.6f + Vector3.up * 1.1f, i % 3 == 0 ? pale : c, UnityEngine.Random.Range(0.1f, 0.2f), UnityEngine.Random.Range(0.35f, 0.7f));
    }

    // ------------------------------------------------------------------ wave crests on the sea
    class Crest { public Transform t; public bool active; public float age, dur, delay, r0, r1, h; public Vector3 c; }
    readonly List<Crest> crests = new List<Crest>();
    Material crestMat;

    /// <summary>A wave crest spreading over the sea from radius <paramref name="r0"/> to <paramref name="r1"/> round <paramref name="centre"/> (its
    /// y ignored), <paramref name="height"/> high at the start, over <paramref name="seconds"/> (after <paramref name="delay"/>): it rises out of
    /// the water, spreads with an ease-out, flattens and sinks back in (drawn on twos, foam cream on a violet swell, ink outlined).</summary>
    public void SpawnCrest(Vector3 centre, float r0, float r1, float height, float seconds, float delay = 0f)
    {
        if (crestMat == null)
        {
            crestMat = Fx.Lit(Color.white, 0.2f, 0f, true);
            crestMat.SetTexture("_BaseMap", MagicTextures.Foam);
            if (crestMat.HasProperty("_FaceSnap")) crestMat.SetFloat("_FaceSnap", 0f);
        }
        Crest w = null;
        foreach (var o in crests) if (!o.active) { w = o; break; }
        if (w == null)
        {
            var go = new GameObject("waveCrest");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Crest();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = crestMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; mr.lightProbeUsage = LightProbeUsage.Off;
            w = new Crest { t = go.transform };
            crests.Add(w);
        }
        w.active = true; w.age = 0f; w.delay = Mathf.Max(0f, delay); w.dur = Mathf.Max(0.1f, seconds); w.r0 = r0; w.r1 = r1; w.h = height;
        w.c = new Vector3(centre.x, SeaY, centre.z);
        PoseCrest(w, 0f);
        w.t.gameObject.SetActive(w.delay <= 0f);
        CrestsSpawned++;
    }

    void StepCrests(float dt)
    {
        foreach (var w in crests)
        {
            if (!w.active) continue;
            if (w.delay > 0f) { w.delay -= dt; if (w.delay > 0f) continue; w.t.gameObject.SetActive(true); }
            w.age += dt;
            if (w.age >= w.dur) { w.active = false; w.t.gameObject.SetActive(false); continue; }
            PoseCrest(w, Look.Stepped(w.age) / w.dur);
        }
    }

    static void PoseCrest(Crest w, float t)
    {
        t = Mathf.Clamp01(t);
        float r = Mathf.Lerp(w.r0, w.r1, Ease.OutCubic(t));
        float rise = Mathf.Clamp01(t / 0.1f);
        float h = w.h * Ease.OutQuad(rise) * Mathf.Pow(1f - t, 1.1f);
        w.t.position = new Vector3(w.c.x, SeaY + 0.02f - 0.35f * t * t, w.c.z);
        w.t.localScale = new Vector3(r, Mathf.Max(0.001f, h), r);
    }

    // ------------------------------------------------------------------ inward ripples (a lowered island's whirl)
    class Ring { public Transform t; public MeshRenderer r; public bool active; public float age, dur, delay, r0, r1; public Color col; public Vector3 c; }
    readonly List<Ring> inward = new List<Ring>();
    Material inwardMat; MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>An ink ripple on the sea closing IN from radius <paramref name="r0"/> to <paramref name="r1"/> (accelerating toward the eye).</summary>
    public void SpawnInward(Vector3 centre, Color c, float r0, float r1, float seconds, float delay = 0f)
    {
        if (inwardMat == null) inwardMat = Fx.InkMaterial(1);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        Ring w = null;
        foreach (var o in inward) if (!o.active) { w = o; break; }
        if (w == null)
        {
            var go = new GameObject("inwardRipple");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.FlatRing(0.82f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = inwardMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            w = new Ring { t = go.transform, r = mr };
            inward.Add(w);
        }
        w.active = true; w.age = 0f; w.delay = Mathf.Max(0f, delay); w.dur = Mathf.Max(0.1f, seconds); w.r0 = r0; w.r1 = r1; w.col = c;
        w.c = new Vector3(centre.x, SeaY + 0.03f, centre.z);
        PoseInward(w, 0f);
        w.t.gameObject.SetActive(w.delay <= 0f);
        InwardSpawned++;
    }

    void StepInward(float dt)
    {
        foreach (var w in inward)
        {
            if (!w.active) continue;
            if (w.delay > 0f) { w.delay -= dt; if (w.delay > 0f) continue; w.t.gameObject.SetActive(true); }
            w.age += dt;
            if (w.age >= w.dur) { w.active = false; w.t.gameObject.SetActive(false); continue; }
            PoseInward(w, Look.Stepped(w.age) / w.dur);
        }
    }

    void PoseInward(Ring w, float t)
    {
        t = Mathf.Clamp01(t);
        float r = Mathf.Lerp(w.r0, w.r1, Mathf.Pow(t, 1.6f));
        w.t.position = w.c;
        w.t.localScale = new Vector3(r, 1f, r);
        float life = Mathf.Clamp01(t / 0.2f) * (1f - Mathf.Clamp01((t - 0.75f) / 0.25f));
        mpb.Clear();
        mpb.SetColor(ColorId, w.col);
        mpb.SetFloat(IntensityId, Mathf.Lerp(0.25f, 0.9f, life));
        w.r.SetPropertyBlock(mpb);
        if (life <= 0.02f && t > 0.5f) w.t.localScale = new Vector3(0.001f, 1f, 0.001f);
    }

    // ------------------------------------------------------------------ paste beads (Magic.PasteFlight) and spell beads
    class BeadFx
    {
        public Transform t; public MeshRenderer r; public TrailRenderer tr; public Material trMat; public bool active; public float age, delay, dur, h, size, shed; public Vector3 a, b; public Color c;
        public Action onLand;
    }
    readonly List<BeadFx> beads = new List<BeadFx>();
    Material beadMat;

    /// <summary>One ghost bead flying from <paramref name="from"/> to <paramref name="to"/> along the magic arc (Magic.ArcPoint, height
    /// <paramref name="height"/>) after <paramref name="delay"/> seconds, for <paramref name="seconds"/>, shedding sparkles; it lands with
    /// Magic.Land and calls <paramref name="onLand"/>.</summary>
    public void LaunchBead(Vector3 from, Vector3 to, Color c, float height, float seconds, float delay, float size = 0.3f, Action onLand = null)
    {
        if (beadMat == null) beadMat = Fx.Alpha(MagicTextures.Bead, Color.white);
        if (mpb == null) mpb = new MaterialPropertyBlock();
        BeadFx w = null;
        foreach (var o in beads) if (!o.active) { w = o; break; }
        if (w == null)
        {
            var go = new GameObject("magicBead");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = beadMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.2f; tr.startWidth = 1f; tr.endWidth = 0f; tr.widthMultiplier = 0.22f; tr.minVertexDistance = 0.04f; tr.numCapVertices = 2;
            tr.alignment = LineAlignment.View; tr.textureMode = LineTextureMode.Stretch; tr.emitting = false;
            var trMat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white);
            tr.material = trMat;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; tr.lightProbeUsage = LightProbeUsage.Off;
            w = new BeadFx { t = go.transform, r = mr, tr = tr, trMat = trMat };
            beads.Add(w);
        }
        w.active = true; w.age = 0f; w.delay = Mathf.Max(0f, delay); w.dur = Mathf.Max(0.08f, seconds); w.h = height; w.size = size; w.shed = 0f;
        w.a = from; w.b = to; w.c = c; w.onLand = onLand;
        w.t.position = from;
        w.t.localScale = Vector3.one * 0.001f;
        if (w.trMat != null) w.trMat.SetColor(ColorId, new Color(c.r, c.g, c.b, 0.9f));
        if (w.tr != null) { w.tr.Clear(); w.tr.emitting = true; w.tr.widthMultiplier = size * 0.7f; }
        w.t.gameObject.SetActive(w.delay <= 0f);
        BeadsLaunched++;
    }

    void StepBeads(float dt)
    {
        var cam = Camera.main;
        foreach (var w in beads)
        {
            if (!w.active) continue;
            if (w.delay > 0f) { w.delay -= dt; if (w.delay > 0f) continue; w.t.gameObject.SetActive(true); }
            w.age += dt;
            float t = w.age / w.dur;
            if (t >= 1f)
            {
                w.active = false; w.t.gameObject.SetActive(false); BeadsLanded++;
                if (w.tr != null) { w.tr.emitting = false; w.tr.Clear(); }
                LandBurst(w.b, w.c);
                if (w.onLand != null) { var a = w.onLand; w.onLand = null; try { a(); } catch (Exception ex) { Debug.LogException(ex); } }
                continue;
            }
            float e = Ease.InOutCubic(t);
            Vector3 p = Magic.ArcPoint(w.a, w.b, w.h, e);
            w.t.position = p;
            float s = w.size * Mathf.Min(1f, t / 0.12f + 0.2f) * (1f + 0.15f * Mathf.Sin(t * Mathf.PI));
            w.t.localScale = new Vector3(s, s, 1f);
            if (cam != null) w.t.rotation = cam.transform.rotation;
            mpb.Clear(); mpb.SetColor(ColorId, new Color(w.c.r, w.c.g, w.c.b, 0.92f)); w.r.SetPropertyBlock(mpb);
            w.shed += dt;
            if (w.shed >= 1f / 24f)
            {
                w.shed = 0f;
                Sparkle(p, UnityEngine.Random.insideUnitSphere * 0.35f, UnityEngine.Random.value < 0.5f ? w.c : Color.Lerp(w.c, Color.white, 0.6f), UnityEngine.Random.Range(0.1f, 0.18f), UnityEngine.Random.Range(0.25f, 0.45f));
            }
        }
    }

    // ------------------------------------------------------------------ sparkle trails behind flying cubes (Magic.Trail)
    class TrailFx { public Transform t, target; public TrailRenderer lr; public Material mat; public bool active; public float left, shed; public Color c; }
    readonly List<TrailFx> trails = new List<TrailFx>();

    /// <summary>A comet-like sparkle trail following <paramref name="target"/> for <paramref name="seconds"/>: a flat tapered ribbon in the colour
    /// and star sparkles shed along the way (twinkling out, on twos). A second call for the same target extends it.</summary>
    public void StartTrail(Transform target, Color c, float seconds)
    {
        if (target == null) return;
        foreach (var o in trails) if (o.active && o.target == target) { o.left = Mathf.Max(o.left, seconds); o.c = c; SetTrailColor(o, c); o.lr.emitting = true; return; }
        TrailFx w = null;
        foreach (var o in trails) if (!o.active && (o.lr == null || o.lr.positionCount == 0)) { w = o; break; }
        if (w == null)
        {
            var go = new GameObject("magicTrail");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<TrailRenderer>();
            lr.time = 0.38f; lr.startWidth = 0.26f; lr.endWidth = 0f; lr.minVertexDistance = 0.05f;
            lr.numCapVertices = 3; lr.numCornerVertices = 2; lr.alignment = LineAlignment.View; lr.textureMode = LineTextureMode.Stretch;
            var mat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white);
            lr.material = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.lightProbeUsage = LightProbeUsage.Off;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.25f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            lr.colorGradient = g;
            w = new TrailFx { t = go.transform, lr = lr, mat = mat };
            trails.Add(w);
        }
        w.active = true; w.target = target; w.left = Mathf.Max(0.05f, seconds); w.shed = 0f; w.c = c;
        w.t.position = target.position;
        w.lr.Clear();
        w.lr.emitting = true;
        SetTrailColor(w, c);
        TrailsStarted++;
    }

    static void SetTrailColor(TrailFx w, Color c) { if (w.mat != null) w.mat.SetColor(ColorId, new Color(c.r, c.g, c.b, 0.92f)); }

    void StepTrails(float dt)
    {
        foreach (var w in trails)
        {
            if (!w.active) continue;
            if (w.target == null) { w.left = 0f; }
            else w.t.position = w.target.position;
            w.left -= dt;
            if (w.left <= 0f)
            {
                w.lr.emitting = false;
                w.active = false;
                continue;
            }
            w.shed += dt;
            if (w.shed >= 1f / 24f)
            {
                w.shed = 0f;
                Vector3 p = w.t.position + UnityEngine.Random.insideUnitSphere * 0.12f;
                Sparkle(p, UnityEngine.Random.insideUnitSphere * 0.4f + Vector3.down * 0.15f, UnityEngine.Random.value < 0.55f ? w.c : Color.Lerp(w.c, Color.white, 0.65f), UnityEngine.Random.Range(0.12f, 0.24f), UnityEngine.Random.Range(0.3f, 0.55f));
            }
        }
    }
}
