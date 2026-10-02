using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pooled visual effects (ripples, light pillars, sky beams, starbursts, spark bursts, ambient dust), the sea plane,
/// runtime materials and the scene's look (light, fog, bloom, sky tint). SPEC §7: the rhythm legibility kit
/// (BeatBump, SkyBeam, SeaRipple, Starburst) is fired by the Comet, KeyBlock and AudioCube; everything here is pooled
/// and costs O(active effects) per frame.
/// v3 package E (SPEC v3 §7, "3D that reads as 2D"): Fx.Lit makes DeaCube/Toon materials (matte cel bands, Ben-Day print, ink
/// outline), Fx.Additive flat posterised glows (DeaCube/InkGlow); ripples are ink rings, pillars / sky beams flat hard-edged beams
/// with ink edges, starbursts comic accents (Ben-Day halo + radial ink speed lines + ink ring), sparks flat confetti, dust flat specks; everything of Fx's own
/// animates on twos (Look.Stepped). The sky is a posterised pastel dusk (DeaCube/SkyToon), the sea flat pastel with toon wave
/// lines (DeaCube/SeaToon); SetupEnvironment writes the key light, flat ambient, flat fog and the comic post stack (Look).
/// Downbeats and accents fire faint impact frames (LookScreen). Comic helpers: Multiples, SpeedLines, KirbyDots (FxComic.cs).
/// </summary>
public partial class Fx : MonoBehaviour
{
    static Fx inst;
    public static Fx I
    {
        get
        {
            if (inst == null)
            {
                inst = FindAnyObjectByType<Fx>();
                if (inst == null) inst = new GameObject("Fx").AddComponent<Fx>();
            }
            return inst;
        }
    }

    /// <summary>The lit shader every Fx.Lit material (and the cube prefab materials) uses.</summary>
    public const string ToonShaderName = "DeaCube/Toon";

    static Shader addShader, alphaShader, flowShader, litShader, addVertShader, inkShader;
    static Shader AddShader => addShader != null ? addShader : (addShader = FindShader("DeaCube/InkGlow", "DeaCube/Additive"));
    static Shader AlphaShader => alphaShader != null ? alphaShader : (alphaShader = Shader.Find("DeaCube/AlphaBlend"));
    static Shader FlowShader => flowShader != null ? flowShader : (flowShader = Shader.Find("DeaCube/Flow"));
    static Shader LitShader => litShader != null ? litShader : (litShader = FindShader(ToonShaderName, "Universal Render Pipeline/Lit"));
    static Shader AddVertShader => addVertShader != null ? addVertShader : (addVertShader = Shader.Find("DeaCube/AdditiveVertex"));
    static Shader InkShader => inkShader != null ? inkShader : (inkShader = Shader.Find("DeaCube/InkFx"));

    static Shader FindShader(string name, string fallback)
    {
        var s = Shader.Find(name);
        if (s == null || !s.isSupported) { if (s == null) Debug.LogWarning("Fx: shader " + name + " missing, using " + fallback); s = Shader.Find(fallback); }
        return s;
    }

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int ShapeId = Shader.PropertyToID("_Shape");
    static readonly int InkId = Shader.PropertyToID("_Ink");

    public static Material Additive(Texture tex, Color? color = null, float intensity = 1f)
    {
        var m = new Material(AddShader);
        m.mainTexture = tex != null ? tex : IconFactory.GetTexture("white");
        m.SetColor(ColorId, color ?? Color.white);
        m.SetFloat(IntensityId, intensity);
        return m;
    }
    public static Material Alpha(Texture tex, Color color)
    {
        var m = new Material(AlphaShader);
        m.mainTexture = tex != null ? tex : IconFactory.GetTexture("white");
        m.SetColor(ColorId, color);
        return m;
    }
    public static Material Flow(Color color, float intensity = 1f, float tiling = 1.3f, float speed = 1.6f, float baseBright = 0.22f)
    {
        var m = new Material(FlowShader);
        m.SetColor(ColorId, color); m.SetFloat(IntensityId, intensity);
        m.SetFloat("_Tiling", tiling); m.SetFloat("_Speed", speed); m.SetFloat("_Base", baseBright);
        return m;
    }
    /// <summary>A lit material: DeaCube/Toon (URP Lit's property names, so _BaseColor / _EmissionColor / _Smoothness / _Metallic and
    /// MaterialPropertyBlocks work as before). <paramref name="smoothness"/> only sizes the small painted highlight (clamped matte).</summary>
    public static Material Lit(Color baseColor, float smoothness = 0.4f, float metallic = 0f)
    {
        var m = new Material(LitShader);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m;
    }
    /// <summary>Fx.Lit with the ink outline switched on or off: <paramref name="outline"/> = false disables the outline pass on this
    /// material (Material.SetShaderPassEnabled on the SRPDefaultUnlit pass: no draw call at all), for crowds of small props that
    /// should read as one mass (e.g. the menu wall's plain pins).</summary>
    public static Material Lit(Color baseColor, float smoothness, float metallic, bool outline)
    {
        var m = Lit(baseColor, smoothness, metallic);
        SetOutline(m, outline);
        return m;
    }
    /// <summary>Turns a Toon material's ink outline pass on / off (off = the pass is skipped, not just drawn at zero width).</summary>
    public static void SetOutline(Material m, bool on)
    {
        if (m == null) return;
        m.SetShaderPassEnabled("SRPDefaultUnlit", on);
        if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", on ? 2.5f : 0f);
    }
    /// <summary>Additive material whose colour comes from the mesh's vertex colours × texture × _Color × _Intensity
    /// (shader DeaCube/AdditiveVertex): the segmented beat ring and the Moon ring.</summary>
    public static Material AdditiveVertex(Texture tex, float intensity)
    {
        var m = new Material(AddVertShader != null ? AddVertShader : AddShader);
        m.mainTexture = tex != null ? tex : IconFactory.GetTexture("white");
        m.SetColor(ColorId, Color.white);
        m.SetFloat(IntensityId, intensity);
        return m;
    }
    /// <summary>A flat comic-effect material (DeaCube/InkFx): 0 starburst, 1 ring, 2 beam, 3 streak, 4 Kirby dots, 5 solid echo.
    /// Falls back to an additive glow when the shader is missing.</summary>
    public static Material InkMaterial(int shape)
    {
        if (InkShader == null) return Additive(IconFactory.GetTexture(shape == 1 ? "ringBand" : (shape == 2 ? "pillar" : "glow")));
        var m = new Material(InkShader);
        m.SetFloat(ShapeId, shape);
        m.SetColor(InkId, Look.InkColor);
        m.SetColor(ColorId, Color.white);
        m.SetFloat(IntensityId, 1f);
        return m;
    }

    // ------------------------------------------------------------------ pools
    class RippleFx { public Transform t; public MeshRenderer r; public float age, dur, scale; public Color c; public bool active; }
    class PillarFx { public Transform t; public MeshRenderer r; public float age, dur, h, w; public Color c; public bool active, slow; }
    class BurstFx { public Transform t; public MeshRenderer r; public float age, dur, size, spin, seed; public Color c; public bool active; }
    readonly List<RippleFx> ripples = new List<RippleFx>();
    readonly List<PillarFx> pillars = new List<PillarFx>();
    readonly List<BurstFx> bursts = new List<BurstFx>();
    Material ripMat, pillarMat, burstMat; Mesh ringMesh, vquad, cquad; MaterialPropertyBlock mpb;
    bool inkFx;
    ParticleSystem sparks, dust, streaks;
    float twosClock;
    Material skyMat; Transform sky; bool toonSky;
    Transform sea; Material seaA, seaB; Vector2 seaOffA, seaOffB; bool toonSea;
    Color skyTint = new Color(0.30f, 0.16f, 0.46f), skyTintTarget = new Color(0.30f, 0.16f, 0.46f);
    static Bloom bloomRef; const float BloomBase = Look.BloomIntensity; const float BumpSeconds = 0.1f; float bumpAmt, bumpAge = 9f;
    int downbeatFrame = -1;
    int climateTint; float emberMul = -1f, emberAbs = -1f, emberBase = 40f;
    static readonly Color[] ClimateTints = { new Color(1f, 0.85f, 0.6f), new Color(0.62f, 0.42f, 1f), new Color(0.22f, 0.2f, 0.62f) };   // warm, violet, indigo
    static readonly int HorizonId = Shader.PropertyToID("_Horizon");
    static readonly int TopId = Shader.PropertyToID("_Top");
    static readonly int BottomId = Shader.PropertyToID("_Bottom");
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int SunDirId = Shader.PropertyToID("_SunDir");
    static readonly int AgeFxId = Shader.PropertyToID("_Age");
    static readonly int SeedFxId = Shader.PropertyToID("_Seed");
    const float SeaY = -4f, SeaRadius = 300f, SeaTile = 40f;

    // diagnostics
    public int ActivePillars { get { int n = 0; foreach (var p in pillars) if (p.active) n++; return n; } }
    public int ActiveBursts { get { int n = 0; foreach (var b in bursts) if (b.active) n++; return n; } }
    public int ActiveRipples { get { int n = 0; foreach (var r in ripples) if (r.active) n++; return n; } }
    public bool HasSea => sea != null;
    public static float BloomNow => bloomRef != null ? bloomRef.intensity.value : -1f;
    public Color SkyTint => skyTint;
    /// <summary>The sky / sea materials (checks read their shaders).</summary>
    public Material SkyMaterial => skyMat;
    public Material SeaMaterial => seaA;

    void Awake()
    {
        inst = this;
        mpb = new MaterialPropertyBlock();
        ringMesh = MeshFactory.Ring(0.6f, 1f, 64);
        vquad = MeshFactory.VerticalQuad();
        cquad = CenteredQuad();
        inkFx = InkShader != null;
        ripMat = inkFx ? InkMaterial(1) : Additive(IconFactory.GetTexture("ringBand"));
        pillarMat = inkFx ? InkMaterial(2) : Additive(IconFactory.GetTexture("pillar"));
        burstMat = inkFx ? InkMaterial(0) : Additive(IconFactory.GetTexture(IconFactory.Has("burst") ? "burst" : "glow"));
        BuildSparks();
        BuildDust();
        BuildStreaks();
        BuildSky();
        BuildSea();
        BuildComic();
        if (GetComponent<LookScreen>() == null) gameObject.AddComponent<LookScreen>();
        Look.PushGlobals();
    }

    /// <summary>Unit quad in the XY plane centred on its pivot, double sided (starbursts face the camera).</summary>
    static Mesh CenteredQuad()
    {
        var m = new Mesh { name = "cquad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }

    static void Quiet(MeshRenderer mr)
    {
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    RippleFx GetRipple()
    {
        foreach (var r in ripples) if (!r.active) return r;
        var go = new GameObject("ripple");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ripMat; Quiet(mr);
        var rp = new RippleFx { t = go.transform, r = mr };
        ripples.Add(rp);
        return rp;
    }
    PillarFx GetPillar()
    {
        foreach (var p in pillars) if (!p.active) return p;
        var go = new GameObject("pillar");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = vquad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = pillarMat; Quiet(mr);
        var p2 = new PillarFx { t = go.transform, r = mr };
        pillars.Add(p2);
        return p2;
    }
    BurstFx GetBurst()
    {
        foreach (var b in bursts) if (!b.active) return b;
        var go = new GameObject("starburst");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = cquad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = burstMat; Quiet(mr);
        var b2 = new BurstFx { t = go.transform, r = mr };
        bursts.Add(b2);
        return b2;
    }

    /// <summary>Expanding ink ring on a surface.</summary>
    public static void Ripple(Vector3 pos, Color c, float scale = 1f, float dur = 0.5f)
    {
        var f = I;
        var r = f.GetRipple();
        r.active = true; r.age = 0f; r.dur = Mathf.Max(0.05f, dur); r.scale = scale; r.c = c;
        r.t.position = pos + Vector3.up * 0.025f;
        f.PoseRipple(r, 0f);
        r.t.gameObject.SetActive(true);
    }

    /// <summary>Vertical note beam rising from a point (flat, hard-edged, ink edges). AudioCube.Land passes its own height/width.</summary>
    public static void Pillar(Vector3 pos, Color c, float height = 2.2f, float dur = 0.55f, float width = 0.55f) { PillarEx(pos, c, height, dur, width, false); }

    static void PillarEx(Vector3 pos, Color c, float height, float dur, float width, bool slow)
    {
        var f = I;
        var p = f.GetPillar();
        p.active = true; p.age = 0f; p.dur = Mathf.Max(0.05f, dur); p.h = height; p.c = c; p.w = width; p.slow = slow;
        p.t.position = pos;
        f.PosePillar(p, 0f, Camera.main);
        p.t.gameObject.SetActive(true);
    }

    /// <summary>Flat confetti (strips, triangles, discs, stars with ink rims) flung from <paramref name="pos"/>; every fifth piece is white.</summary>
    public static void Burst(Vector3 pos, Color c, int count = 10, float speed = 2.2f)
    {
        var ps = I.sparks;
        for (int i = 0; i < count; i++)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = (Random.onUnitSphere * Random.Range(0.35f, 1f) + Vector3.up * 0.75f) * speed,
                startColor = i % 5 == 4 ? new Color(1f, 0.97f, 0.94f, c.a) : c,
                startSize = Random.Range(0.08f, 0.17f),
                startLifetime = Random.Range(0.4f, 0.8f),
                rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }
    }

    public static void Pop(Vector3 pos, Color c) { Burst(pos, c, 18, 3f); Ripple(pos, c, 1.4f, 0.5f); }

    // ------------------------------------------------------------------ v2 pulse kit (SPEC §7)
    /// <summary>Bloom intensity base → base + amount, decaying over 100 ms (downbeats, comet arrivals). No-op without a Bloom override.
    /// v3: also queues a faint impact frame of ink speed lines (a Starburst in the same frame centres it).</summary>
    public static void BeatBump(float amount = 0.2f)
    {
        var f = I;
        if (bloomRef == null)
        {
            var vol = FindAnyObjectByType<Volume>();
            Bloom b;
            if (vol != null && vol.profile != null && vol.profile.TryGet(out b)) bloomRef = b;
        }
        float remaining = f.bumpAmt * (1f - Mathf.Clamp01(f.bumpAge / BumpSeconds));
        f.bumpAmt = Mathf.Max(amount, remaining); f.bumpAge = 0f;
        f.downbeatFrame = Time.frameCount;
        var cam = Camera.main;
        if (cam != null) LookScreen.Request(0, cam.transform.position + cam.transform.forward * 10f, Mathf.Clamp01(0.6f + amount));
    }
    void UpdateBump(float dt)
    {
        if (bumpAge > BumpSeconds) return;
        bumpAge += dt;
        float k = 1f - Mathf.Clamp01(bumpAge / BumpSeconds);
        if (bloomRef != null) bloomRef.intensity.Override(BloomBase + bumpAmt * k);
    }

    /// <summary>A tall, wide (1.6 u), slow-fading beam over 0.9 s: island arrivals (9-12 u) and the lit island's downbeats
    /// (KeyBlock passes an energy-scaled height). Fades slowly so overlapping beams show polyphony.</summary>
    public static void SkyBeam(Vector3 pos, Color c, float height) { PillarEx(pos, c, Mathf.Clamp(height, 2f, 16f), 0.9f, 1.6f, true); }

    /// <summary>An ink ring on the sea plane (y = -4) under an island: scale 3.5, 0.8 s.</summary>
    public static void SeaRipple(Vector3 pos, Color c, float scale = 3.5f) { Ripple(new Vector3(pos.x, SeaY, pos.z), c, scale, 0.8f); }

    /// <summary>Node-hit accent: a pooled camera-facing comic accent centred on <paramref name="pos"/> (DeaCube/InkFx shape 0: a Ben-Day
    /// halo blooming out in the colour, radial ink speed lines, an ink ring on the halo's front, a pale pop at the centre; no star
    /// silhouette) over 0.4 s, on twos (big ones on twos, then ones). Spawned by the Comet on arrival and by AudioCube.Land on
    /// accents; also fires a faint impact frame (speed lines on a downbeat, a dot ring otherwise).</summary>
    public static void Starburst(Vector3 pos, Color c) { Starburst(pos, c, 1f); }
    public static void Starburst(Vector3 pos, Color c, float size)
    {
        var f = I;
        var b = f.GetBurst();
        b.active = true; b.age = 0f; b.dur = 0.4f; b.c = c; b.size = Mathf.Max(0.1f, size); b.spin = Random.Range(0f, 360f); b.seed = Random.Range(0f, 50f);
        b.t.position = pos;
        f.PoseBurst(b, 0f, Camera.main);
        b.t.gameObject.SetActive(true);
        LookScreen.Request(f.downbeatFrame == Time.frameCount ? 0 : 1, pos, f.downbeatFrame == Time.frameCount ? 0.9f : 0.7f);
    }

    /// <summary>Ambient dust over the song. r ≤ 4 is a multiplier of the automatic rate (e.g. Rhythm.EnergyScale of the lit
    /// island's energy), a larger r is an absolute particles-per-second rate, a negative r restores the automatic rate.</summary>
    public static void SetEmberRate(float r)
    {
        var f = I;
        if (r < 0f) { f.emberMul = -1f; f.emberAbs = -1f; }
        else if (r <= 4f) { f.emberMul = r; f.emberAbs = -1f; }
        else { f.emberAbs = r; f.emberMul = -1f; }
        f.ApplyEmberRate();
    }
    void ApplyEmberRate()
    {
        if (dust == null) return;
        float rate = emberAbs >= 0f ? emberAbs : emberBase * (emberMul >= 0f ? emberMul : 1f);
        var em = dust.emission; em.rateOverTime = rate;
    }
    public float EmberRate { get { if (dust == null) return 0f; return dust.emission.rateOverTime.constant; } }

    /// <summary>Climate 0..3 (auto / warm / violet / indigo) tints the sky at 0.35. DriveSkyTint reads SongManager.Climate directly;
    /// this setter is kept for callers that change the climate outside the song (the stored value is the fallback).</summary>
    public static void SetClimateTint(int climate) { if (inst != null || Application.isPlaying) I.climateTint = Mathf.Clamp(climate, 0, 3); }

    // ------------------------------------------------------------------ per-frame
    void Update() { Step(Time.deltaTime); }

    /// <summary>One frame of every effect (public so a timing test can drive it; Update calls it with Time.deltaTime).</summary>
    public void Step(float dt)
    {
        var cam = Camera.main;
        DriveSkyTint();
        UpdateBump(dt);
        UpdateSky(dt, cam);
        UpdateFog();
        UpdateSea(dt);
        UpdateParticles(dt);
        for (int i = 0; i < ripples.Count; i++)
        {
            var r = ripples[i]; if (!r.active) continue;
            r.age += dt;
            if (r.age >= r.dur) { r.active = false; r.t.gameObject.SetActive(false); continue; }
            PoseRipple(r, Look.Stepped(r.age) / r.dur);
        }
        for (int i = 0; i < pillars.Count; i++)
        {
            var p = pillars[i]; if (!p.active) continue;
            p.age += dt;
            if (p.age >= p.dur) { p.active = false; p.t.gameObject.SetActive(false); continue; }
            PosePillar(p, Look.Stepped(p.age) / p.dur, cam);
        }
        for (int i = 0; i < bursts.Count; i++)
        {
            var b = bursts[i]; if (!b.active) continue;
            b.age += dt; float t = b.age / b.dur;
            if (t >= 1f) { b.active = false; b.t.gameObject.SetActive(false); continue; }
            bool big = b.size >= 1.3f;
            PoseBurst(b, big && t > 0.5f ? t : Look.Stepped(b.age) / b.dur, cam);   // big bursts: on twos, then ones
        }
        StepComic(dt, cam);
    }

    void PoseRipple(RippleFx r, float t)
    {
        float e = Ease.OutCubic(Mathf.Clamp01(t));
        float s = Mathf.Lerp(0.3f, inkFx ? 1.9f : 2.4f, e) * r.scale;   // ink rings spread a little less than the old glow rings
        r.t.localScale = new Vector3(s, 1f, s);
        mpb.Clear();
        mpb.SetColor(ColorId, r.c);
        mpb.SetFloat(IntensityId, inkFx ? 1f - Mathf.Clamp01(t) : (1f - t) * (1f - t) * 1.7f);
        r.r.SetPropertyBlock(mpb);
    }

    void PosePillar(PillarFx p, float t, Camera cam)
    {
        float grow = Ease.OutCubic(Mathf.Clamp01(t * (p.slow ? 4f : 2.4f)));
        float fade = p.slow ? 1f - Ease.InCubic(t) : 1f - Ease.InQuad(t);
        p.t.localScale = new Vector3(p.w * (inkFx ? 0.7f + 0.3f * fade : 0.6f + 0.4f * fade), p.h * grow, 1f);
        if (cam != null) { var d = cam.transform.position - p.t.position; d.y = 0f; if (d.sqrMagnitude > 1e-4f) p.t.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up); }
        mpb.Clear();
        mpb.SetColor(ColorId, p.c);
        mpb.SetFloat(IntensityId, inkFx ? fade : fade * (p.slow ? 2.0f : 1.5f));   // v2: beams a little brighter than note pillars
        p.r.SetPropertyBlock(mpb);
    }

    void PoseBurst(BurstFx b, float t, Camera cam)
    {
        // v3 comic accent: the quad barely grows (the halo, the speed lines and the ring animate inside the shader), a slow turn
        float s = (inkFx ? Mathf.Lerp(1.7f, 2f, Ease.OutCubic(Mathf.Clamp01(t))) : Mathf.Lerp(0.5f, 1.6f, Ease.OutCubic(Mathf.Clamp01(t)))) * b.size;
        b.t.localScale = new Vector3(s, s, 1f);
        var rot = cam != null ? cam.transform.rotation : Quaternion.identity;
        b.t.rotation = rot * Quaternion.AngleAxis(b.spin + (inkFx ? 18f : 90f) * Ease.OutQuad(Mathf.Clamp01(t)), Vector3.forward);
        mpb.Clear();
        mpb.SetColor(ColorId, b.c);
        mpb.SetFloat(IntensityId, inkFx ? 1f : Mathf.Pow(1f - t, 1.5f) * 1.7f);
        mpb.SetFloat(AgeFxId, Mathf.Clamp01(t));
        mpb.SetFloat(SeedFxId, b.seed);
        b.r.SetPropertyBlock(mpb);
    }

    /// <summary>Fx's particle systems (confetti, dust, streaks) are simulated by hand: on twos (12 Hz holds) while Look.OnTwos.</summary>
    void UpdateParticles(float dt)
    {
        twosClock += dt;
        float step;
        if (Look.OnTwos) { if (twosClock < 1f / Look.TwosFps) return; step = twosClock; }
        else step = twosClock;
        twosClock = 0f;
        Sim(sparks, step); Sim(dust, step); Sim(streaks, step);
    }
    static void Sim(ParticleSystem ps, float step) { if (ps != null) ps.Simulate(step, true, false, false); }

    void BuildSparks()
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.startLifetime = 0.55f; main.startSpeed = 0f; main.startSize = 0.1f; main.gravityModifier = 0.7f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 4000; main.playOnAwake = false; main.loop = false;
        var em = sparks.emission; em.enabled = false;
        var sh = sparks.shape; sh.enabled = false;
        var col = sparks.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sz = sparks.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.75f, 0.9f), new Keyframe(1f, 0f)));
        var rot = sparks.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        if (InkShader != null)
        {
            var tsa = sparks.textureSheetAnimation; tsa.enabled = true;
            tsa.numTilesX = 2; tsa.numTilesY = 2; tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // a random shape per piece, held for its life
            r.material = Alpha(LookTextures.Confetti, Color.white);
        }
        else r.material = Additive(IconFactory.GetTexture("glow"), Color.white, 1.4f);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        sparks.Pause();
    }

    // ------------------------------------------------------------------ sky
    void BuildSky()
    {
        var shader = Shader.Find("DeaCube/SkyToon");
        toonSky = shader != null && shader.isSupported;
        if (!toonSky) shader = Shader.Find("DeaCube/Sky");
        if (shader == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Sky";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * 300f;
        skyMat = new Material(shader);
        if (toonSky)
        {
            skyMat.SetColor("_C0", Look.SkyGlow); skyMat.SetColor("_C1", Look.SkyLow); skyMat.SetColor("_C2", Look.SkyMid);
            skyMat.SetColor("_C3", Look.SkyHigh); skyMat.SetColor("_C4", Look.SkyTop); skyMat.SetColor("_Below", Look.SeaFar);
            skyMat.SetVector(SunDirId, Look.SunDir.normalized);
        }
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = skyMat; Quiet(mr);
        sky = go.transform;
    }

    /// <summary>The sky slowly takes the colour of the island that is playing (or focused while editing).</summary>
    public static void SetSkyTint(Color c) { I.skyTintTarget = c; }

    /// <summary>Active island colour blended with the climate tint (warm / violet / indigo at 0.35 when SongManager.Climate != 0).</summary>
    void DriveSkyTint()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return;
        int idx = GlobalClock.IsPlaying ? sm.LitIsland : (OrbitCamera.I != null ? OrbitCamera.I.FocusedMeasure : 0);
        idx = Mathf.Clamp(idx, 0, sm.Islands.Count - 1);
        var kb = sm.Islands[idx];
        if (kb == null) return;
        Color target = Color.Lerp(kb.chordColor, new Color(0.36f, 0.2f, 0.56f), 0.35f);
        int cl = Mathf.Clamp(SongManager.Climate, 0, 3);
        if (cl == 0) cl = climateTint;
        if (cl > 0) target = Color.Lerp(target, ClimateTints[cl - 1], 0.35f);
        skyTintTarget = target;
    }

    void UpdateSky(float dt, Camera cam)
    {
        skyTint = Color.Lerp(skyTint, skyTintTarget, 1f - Mathf.Exp(-dt * 1.1f));
        if (toonSky)
        {
            // flat-colour fog: the horizon haze, leaning a little toward the song (not while the main menu owns RenderSettings)
            if (!MainMenu.IsShown) RenderSettings.fogColor = Color.Lerp(Look.SeaFar, skyTint, 0.12f);
            if (sky == null || skyMat == null) return;
            if (cam != null) sky.position = cam.transform.position;
            skyMat.SetColor(TintId, skyTint);
            return;
        }
        if (sky == null || skyMat == null) return;
        if (cam != null) sky.position = cam.transform.position;
        Color horizon = Color.Lerp(new Color(0.15f, 0.08f, 0.27f), skyTint, 0.5f);
        Color top = Color.Lerp(new Color(0.03f, 0.02f, 0.07f), skyTint, 0.12f);
        Color bottom = Color.Lerp(new Color(0.02f, 0.015f, 0.05f), skyTint, 0.08f);
        skyMat.SetColor(HorizonId, horizon); skyMat.SetColor(TopId, top); skyMat.SetColor(BottomId, bottom);
        RenderSettings.fogColor = Color.Lerp(horizon, top, 0.45f);
    }

    // ------------------------------------------------------------------ v9: the fog follows the camera's distance
    /// <summary>v9 (S17 / G9; W): the linear fog starts no nearer than the camera's distance to what it frames minus FogLead, and ends FogSpan
    /// later. A song framed from far (get proto's 4-bar, 7-lane columns put the follow camera at ~85–105 u) is no longer a pink haze; songs framed
    /// at 25–45 u keep Look.FogStart..FogEnd exactly. Set every frame for play, editing and the present mode (OrbitCamera's distance, or the
    /// present camera's); left alone while the menu owns the fog (it switches it off for its stage).</summary>
    public const float FogLead = 10f, FogSpan = Look.FogEnd - Look.FogStart;
    /// <summary>v9: the camera-to-focus distance the fog used last (diagnostics; 0 = none known, the default fog).</summary>
    public static float FogFocusDistance { get; private set; }
    /// <summary>v9: the fog's start for a camera <paramref name="dist"/> from its focus (its end is FogSpan later).</summary>
    public static float FogStartFor(float dist) => Mathf.Max(Look.FogStart, dist - FogLead);

    void UpdateFog()
    {
        if (MainMenu.IsShown) return;
        float d = CamFocusDistance();
        FogFocusDistance = d;
        float start = FogStartFor(d), end = start + FogSpan;
        if (Mathf.Abs(RenderSettings.fogStartDistance - start) > 1e-3f) RenderSettings.fogStartDistance = start;
        if (Mathf.Abs(RenderSettings.fogEndDistance - end) > 1e-3f) RenderSettings.fogEndDistance = end;
    }

    /// <summary>The camera's distance to the point it frames: the present mode's camera while presenting, else OrbitCamera's (0 when neither).</summary>
    static float CamFocusDistance()
    {
        if (Presenter.Active && Presenter.Rig != null) return Presenter.Rig.Target.distance;
        var oc = OrbitCamera.I;
        return oc != null && !oc.Suspended ? oc.Dist : 0f;
    }

    // ------------------------------------------------------------------ sea plane (SPEC §7): y = -4, 600 u across
    void BuildSea()
    {
        var toon = Shader.Find("DeaCube/SeaToon");
        toonSea = toon != null && toon.isSupported;
        if (!toonSea && (AddVertShader == null || !IconFactory.Has("noise"))) return;
        var mesh = SeaMesh();
        var root = new GameObject("Sea");
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(0f, SeaY, 0f);
        if (toonSea)
        {
            // one opaque flat-pastel layer with toon wave lines; it receives the islands' shadows
            var go = new GameObject("SeaToon");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            seaA = new Material(toon);
            seaA.SetColor("_Deep", Look.SeaDeep); seaA.SetColor("_Swell", Look.SeaSwell); seaA.SetColor("_Line", Look.SeaLine);
            seaA.SetColor("_Far", Look.SeaFar); seaA.SetColor("_ShadowCol", Look.SeaShadow);
            mr.sharedMaterial = seaA;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        else
        {
            seaA = SeaLayer(root.transform, mesh, "SeaA", 1f, 0.2f);
            seaB = SeaLayer(root.transform, mesh, "SeaB", 2.35f, 0.13f);
            seaOffB = new Vector2(0.37f, 0.61f);
        }
        sea = root.transform;
    }
    Material SeaLayer(Transform parent, Mesh mesh, string name, float tiling, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var m = AdditiveVertex(IconFactory.GetTexture("noise"), intensity);
        m.mainTextureScale = new Vector2(tiling, tiling);
        mr.sharedMaterial = m; Quiet(mr);
        return m;
    }
    /// <summary>Disc of concentric rings whose vertex alpha fades from 1 at the centre to 0 by r = 150 (the far half is invisible, so
    /// no fog is needed in the shader); uv in world units / SeaTile so the noise tiles.</summary>
    static Mesh SeaMesh()
    {
        float[] rr = { 0f, 30f, 70f, 110f, 150f, SeaRadius };
        float[] aa = { 1f, 1f, 0.7f, 0.32f, 0f, 0f };
        const int segs = 48;
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
        verts.Add(Vector3.zero); uvs.Add(Vector2.zero); cols.Add(new Color(1f, 1f, 1f, aa[0]));
        for (int k = 1; k < rr.Length; k++)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * rr[k], 0f, Mathf.Sin(a) * rr[k]);
                verts.Add(p); uvs.Add(new Vector2(p.x / SeaTile, p.z / SeaTile)); cols.Add(new Color(1f, 1f, 1f, aa[k]));
            }
        }
        for (int i = 0; i < segs; i++) { int a = 1 + i, b = 1 + (i + 1) % segs; tris.Add(0); tris.Add(b); tris.Add(a); }
        for (int k = 1; k < rr.Length - 1; k++)
        {
            int r0 = 1 + (k - 1) * segs, r1 = 1 + k * segs;
            for (int i = 0; i < segs; i++)
            {
                int i1 = (i + 1) % segs;
                int a = r0 + i, b = r0 + i1, c = r1 + i, d = r1 + i1;
                tris.Add(a); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(b); tris.Add(d);
            }
        }
        var m = new Mesh { name = "sea" };
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols); m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
    void UpdateSea(float dt)
    {
        if (sea == null) return;
        if (toonSea) { if (seaA != null) seaA.SetColor(TintId, skyTint); return; }
        seaOffA = new Vector2(Mathf.Repeat(seaOffA.x + 0.011f * dt, 1f), Mathf.Repeat(seaOffA.y + 0.006f * dt, 1f));
        seaOffB = new Vector2(Mathf.Repeat(seaOffB.x - 0.017f * dt, 1f), Mathf.Repeat(seaOffB.y + 0.010f * dt, 1f));
        seaA.mainTextureOffset = seaOffA; seaB.mainTextureOffset = seaOffB;
        Color tint = Color.Lerp(skyTint, Color.white, 0.15f);
        seaA.SetColor(ColorId, tint); seaB.SetColor(ColorId, tint);
    }

    /// <summary>Slow upward hairlines behind the islands (flat, on twos).</summary>
    void BuildStreaks()
    {
        var go = new GameObject("Streaks");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(10f, -5f, 10f);
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        streaks = go.AddComponent<ParticleSystem>();
        streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = streaks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new Color(1f, 0.95f, 1f, 0.2f);
        main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.prewarm = false; main.playOnAwake = false;
        var em = streaks.emission; em.rateOverTime = 3.5f;
        var sh = streaks.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(50f, 26f, 1f);
        var col = streaks.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.45f; r.lengthScale = 2.2f; r.maxParticleSize = 0.004f;
        r.material = inkFx ? Alpha(IconFactory.GetTexture("white"), Color.white) : Additive(IconFactory.GetTexture("glowSoft"), Color.white, 0.9f);
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        streaks.Simulate(8f, true, true, false);
        streaks.Pause();
    }

    /// <summary>Ambient specks (flat pale dots, on twos).</summary>
    void BuildDust()
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(10f, 2.5f, 0f);
        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dust.main;
        main.startLifetime = 10f; main.startSpeed = 0.1f; main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new Color(1f, 0.96f, 1f, 0.55f); main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.prewarm = false; main.playOnAwake = false;
        var em = dust.emission; em.rateOverTime = 40f;
        var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 6f, 16f);
        var noise = dust.noise; noise.enabled = true; noise.strength = 0.22f; noise.frequency = 0.25f; noise.scrollSpeed = 0.1f;
        var col = dust.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = inkFx ? Alpha(IconFactory.GetTexture("dot"), Color.white) : Additive(IconFactory.GetTexture("dust"), Color.white, 0.55f);
        r.maxParticleSize = 0.008f;   // specks stay specks even right in front of the camera
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        dust.Simulate(6f, true, true, false);
        dust.Pause();
    }

    /// <summary>v9 (W): centres the sea plane (it stays at y = SeaY) on <paramref name="c"/>. The present mode keeps it under its camera every frame (a long
    /// song reaches past the disc's 300 u radius); its pattern is drawn in world space, so the water does not slide. SetSongBounds puts it back.</summary>
    public static void CenterSea(Vector3 c) { var f = inst; if (f != null && f.sea != null) f.sea.position = new Vector3(c.x, SeaY, c.z); }
    /// <summary>v9: where the sea plane is centred now (world).</summary>
    public static Vector3 SeaCentre => inst != null && inst.sea != null ? inst.sea.position : Vector3.zero;

    /// <summary>Song bounds changed (build, island move, Moon added): re-centre the dust, streaks and sea; SongManager passes SongBounds (Moons included).</summary>
    public void SetSongBounds(Bounds b)
    {
        if (sea != null) sea.position = new Vector3(b.center.x, SeaY, b.center.z);
        if (dust == null) return;
        dust.transform.position = b.center + Vector3.up * 2.5f;
        var sh = dust.shape; sh.scale = new Vector3(b.size.x + 14f, 6f, b.size.z + 12f);
        emberBase = Mathf.Clamp(b.size.x * 1.6f, 25f, 120f);
        ApplyEmberRate();
        dust.Simulate(6f, true, true, false); dust.Pause();
        if (streaks != null)
        {
            streaks.transform.position = new Vector3(b.center.x, -6f, b.center.z + 8f);
            var ss = streaks.shape; ss.scale = new Vector3(b.size.x + 36f, b.size.z + 28f, 1f);
            streaks.Simulate(8f, true, true, false); streaks.Pause();
        }
    }

    // ------------------------------------------------------------------ environment
    /// <summary>The scene's look from code (the scene is frozen): flat soft ambient, flat-colour linear fog, one key light tuned for
    /// clean cel bands, the camera (SMAA for the ink lines) and the comic post stack on the Global Volume's runtime profile.</summary>
    public static void SetupEnvironment()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Look.Ambient;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Palette.Fog; RenderSettings.fogStartDistance = Look.FogStart; RenderSettings.fogEndDistance = Look.FogEnd;   // far islands turn into flat haze layers
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Look.ConfigureLight(l);
        var cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Palette.Bg; cam.fieldOfView = 36f;   /* integration: a longer lens flattens perspective ("3D that looks 2D"); OrbitCamera scales its distances by FOV */ cam.nearClipPlane = 0.2f; cam.farClipPlane = 400f; Look.ConfigureCamera(cam); }
        var vol = FindAnyObjectByType<Volume>();
        if (vol != null && vol.profile != null)
        {
            var bloom = Look.ConfigurePost(vol.profile);
            if (bloom != null) { bloom.intensity.Override(BloomBase); bloomRef = bloom; }
        }
        Look.PushGlobals();
    }

    /// <summary>Song wrapped around: a light sweep runs across every island.</summary>
    public static void LoopSweep() { if (SongManager.I != null) I.StartCoroutine(I.SweepRoutine(1.1f, 14, 0.045f)); }
    /// <summary>First play: a softer welcome sweep.</summary>
    public static void StartSweep() { if (SongManager.I != null) I.StartCoroutine(I.SweepRoutine(0.7f, 8, 0.07f)); }

    IEnumerator SweepRoutine(float pulse, int sparksPer, float stagger)
    {
        var islands = new List<KeyBlock>(SongManager.I.Islands);
        foreach (var kb in islands)
        {
            if (kb == null || kb.SeaDrop > 0.5f) continue;   // v9 (W): an island under the sea (the present mode, a keyboard between its parts) has no sweep
            kb.Pulse(pulse);
            Burst(kb.Center + Vector3.up * 0.4f, Palette.A(kb.chordColor, 1f), sparksPer, 2.6f);
            Ripple(kb.Center, kb.chordColor, 2.6f, 0.7f);
            yield return new WaitForSeconds(stagger);
        }
    }

    public static void IslandCelebrate(KeyBlock kb)
    {
        if (kb == null) return;
        kb.Pulse(1.2f);
        Burst(kb.Center + Vector3.up * 0.5f, kb.chordColor, 26, 3f);
        Ripple(kb.Center, kb.chordColor, 3f, 0.8f);
        KirbyDots(kb.Center + Vector3.up * 1.2f, kb.chordColor, 1.6f);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.4f);
    }
}
