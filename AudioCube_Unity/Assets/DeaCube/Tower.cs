using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v6 package W (SPEC v6 §6.1): the octave tower of one island with register ≠ 0 — "if you raise a grid by an octave, the grid itself should rise
/// (with waves coming out from underneath like it's a tower rising) on its turn and then slowly go down after its done". K poses the island
/// (KeyBlock.TowerLift / TowerState: 0 rest, 1 rising, 2 up, 3 sinking); this draws what pushes it up. A RAISED island stands on a pillar of
/// ink-outlined stone drums (chamfered: the joints read as grooves; a little wider toward its foot, like a tower) under a flared capital, with
/// one glowing band in the island's colour per octave twice over — under the capital (what shows above the water at rest) and lower down (what
/// shows under the platform while it is up). At rest only a short stub shows (the capital and its band) and now and then a slow ring leaves it:
/// the hint that it will rise. On its turn the pillar surges out of the sea to the platform's underside and holds it up (its top = the
/// underside); while the island sinks slowly back the pillar goes down with it and lets go into the sea at the end. The waves: when a rise starts
/// from rest, three crest rings spread from the foot, a crown of rounded water tongues jumps up and falls back, droplets fly and comic speed
/// lines run up the pillar; while it is up a small ring leaves the foot on every downbeat (the column's first downbeat included); one slow wide
/// ring as it starts to sink. A scalloped foam skirt rings the foot whenever the pillar stands in the water. A LOWERED island has no pillar: a
/// whirl turns on the sea under it (faint at rest), winding up and pulling ink ripples inward while it dips. The pillar is a rigid stack whose
/// top is placed each frame (the opaque sea at y = −4 hides what is below); the effects run on twos. Built and stepped by
/// <see cref="WorldMagic"/> (index-keyed, re-bound after rebuilds). Waves fire on K's phase changes, guarded against the glide a seek / pause /
/// undo reports (a real rise starts at rest, a real sink near the top).
/// v7 (SPEC v7 §12.3 — "the tower things that rise should rise like 60% taller and also only go back after the song resets (not in sync, but
/// slightly out of sync)"): K's TowerRise is 3.2 per octave and a raised island STAYS UP after its turn until the song resets, then sinks with its
/// own small delay. The pillar is long enough for the tallest rise on the highest ground (<see cref="PillarDepth"/>), the downbeat rings run only
/// during the island's own turn (a tower holding up the rest of the song stands quietly: no ring every bar), and the slow sink ring comes when K
/// lets it sink after the reset. On a terrace (a ground ≠ 0) the pillar pushes up out of the stone: the rise throws stone dust round its foot.
/// Every v7 grid stands over its SECTION PLINTH (B), which hides the sea under it: the pillar stands in the front half of the platform (more of
/// it shows under the front edge), its stone keeps more of its own light (it reads against the plinth's ink-stone in the platform's shadow), and
/// where it breaks through the plinth a ring of dust and an ink crack ring burst on the rise (and a puff on each downbeat of its turn) — the sea
/// waves still spread round the plinth.
/// </summary>
public class Tower : MonoBehaviour
{
    public const float SeaY = WorldMagic.SeaY;
    /// <summary>How far the capital shows above the water at rest (the stub).</summary>
    public const float StubHeight = 0.85f;
    /// <summary>Seconds of the pillar's surge to the underside when the rise begins, and of its letting go when the sink ends.</summary>
    public const float SurgeSeconds = 0.09f, ReleaseSeconds = 0.4f;
    /// <summary>Crest rings at the start of a rise.</summary>
    public const int RiseRingCount = 3;
    /// <summary>v7: the pillar's length from its capital down (the top of a register-2 tower on the highest ground, 4.8 + 6.4, still reaches
    /// below the sea at −4).</summary>
    public const float PillarDepth = 18f;
    /// <summary>v7: downbeats that passed without a ring because the island's turn was over while it held up / stayed dipped (tests).</summary>
    public int QuietDownbeats;

    /// <summary>TESTS ONLY: ≥ 0 replaces KeyBlock.TowerState for every tower (captures of the look before / without K's pose).</summary>
    public static int ForceState = -1;

    public KeyBlock Island { get; private set; }
    public int Index { get; private set; }
    public bool Retiring { get; private set; }
    /// <summary>World y of the pillar's top (the capital's top face) and of the island's underside this frame.</summary>
    public float PillarTop { get; private set; }
    public float UndersideY { get; private set; }
    /// <summary>0 = the stub at rest .. 1 = holding the platform.</summary>
    public float Attach => attach;
    /// <summary>KeyBlock.TowerState as seen in the last step.</summary>
    public int State { get; private set; }
    public int Register => reg;
    public float Radius => radius;
    /// <summary>The whirl of a lowered island (0 faint at rest .. 1 wound up while it dips).</summary>
    public float Whirl => whirl;
    /// <summary>Counters (tests): rises, crest rings spawned by the last rise, rings on downbeats while up, sink rings, all rings, splashes,
    /// inward ripples.</summary>
    public int Rises, LastRiseRings, DownbeatRings, SinkRings, TotalRings, Splashes, InwardRings, Dips;
    /// <summary>The pillar's root (tests read its renderer / bounds).</summary>
    public Transform PillarTransform => pillar;

    int reg, builtReg = int.MinValue; float radius = 1.2f, builtRadius = -1f; Color builtColor = Color.clear;
    float attach, retireT, whirl, whirlAngle, crownT = 9f, foamWobble, twos, seedPhase, restClock = 1f, appear, burstCool;
    int prevState = 0;   // a new tower starts from rest: K's ▲ preview (or the turn) that begins with it still throws the rise's waves
    Transform pillar, foam, crown, whirlDisc;
    MeshFilter pillarFilter; MeshRenderer pillarRend, foamRend, crownRend, whirlRend;
    Material stoneMat, bandMat, foamMat, whirlMat;
    Vector3 home;

    static readonly Color Stone = new Color(0.96f, 0.91f, 0.88f);

    /// <summary>(Re-)binds the look to <paramref name="kb"/> at island index <paramref name="index"/>; rebuilds the pillar when the size, the
    /// register or the colour changed.</summary>
    public void Bind(KeyBlock kb, int index)
    {
        Island = kb; Index = index;
        if (kb == null) return;
        reg = kb.register;
        if (pillar == null) Build();
        float r = Mathf.Clamp(Mathf.Min(kb.Width, kb.Depth) * 0.26f, 0.8f, 1.55f);
        Color band = kb.PlatformColor;
        if (reg > 0 && (Mathf.Abs(r - builtRadius) > 0.01f || reg != builtReg || band != builtColor))
        {
            radius = r; builtRadius = r; builtReg = reg; builtColor = band;
            pillarFilter.sharedMesh = MagicMeshes.Pillar(r, Mathf.Clamp(reg, 1, 2), PillarDepth, 12, 7 + index % 5);
            bandMat.SetColor("_BaseColor", band);
            bandMat.SetColor("_EmissionColor", kb.chordColor * 0.85f);   // the octave bands glow in the island's colour
        }
        radius = r;
        bool up = reg > 0;
        if (pillar.gameObject.activeSelf != up) pillar.gameObject.SetActive(up);
        if (whirlDisc.gameObject.activeSelf != (reg < 0)) whirlDisc.gameObject.SetActive(reg < 0);
        if (reg < 0) whirlMat.SetColor("_Color", Palette.A(Color.Lerp(kb.chordColor, MagicTextures.FoamCream, 0.55f), 0f));
    }

    void Build()
    {
        seedPhase = Random.value * 10f;
        // the pillar stands in the island's shadow: a little self-light keeps the pale stone pale (the shade band stays a warm lilac, not grey)
        stoneMat = Fx.Lit(Stone, 0.25f, 0f, true);
        stoneMat.SetColor("_EmissionColor", Stone * 0.42f);   // v7: more of its own light: it reads against the plinth in the platform's shadow
        if (stoneMat.HasProperty("_FaceSnap")) stoneMat.SetFloat("_FaceSnap", 0f);
        bandMat = Fx.Lit(Color.white, 0.3f, 0f, true);
        if (bandMat.HasProperty("_FaceSnap")) bandMat.SetFloat("_FaceSnap", 0f);
        foamMat = Fx.Lit(MagicTextures.FoamCream, 0.15f, 0f, true);
        if (foamMat.HasProperty("_FaceSnap")) foamMat.SetFloat("_FaceSnap", 0f);

        pillar = new GameObject("Pillar").transform;
        pillar.SetParent(transform, false);
        pillarFilter = pillar.gameObject.AddComponent<MeshFilter>();
        pillarRend = pillar.gameObject.AddComponent<MeshRenderer>();
        pillarRend.sharedMaterials = new[] { stoneMat, bandMat };
        pillarRend.shadowCastingMode = ShadowCastingMode.On; pillarRend.receiveShadows = true; pillarRend.lightProbeUsage = LightProbeUsage.Off;

        foam = new GameObject("FoamSkirt").transform;
        foam.SetParent(transform, false);
        foam.gameObject.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.FoamSkirt();
        foamRend = foam.gameObject.AddComponent<MeshRenderer>();
        foamRend.sharedMaterial = foamMat;
        foamRend.shadowCastingMode = ShadowCastingMode.Off; foamRend.receiveShadows = true; foamRend.lightProbeUsage = LightProbeUsage.Off;

        crown = new GameObject("SplashCrown").transform;
        crown.SetParent(transform, false);
        crown.gameObject.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Crown(15, 5);
        crownRend = crown.gameObject.AddComponent<MeshRenderer>();
        crownRend.sharedMaterial = foamMat;
        crownRend.shadowCastingMode = ShadowCastingMode.Off; crownRend.receiveShadows = false; crownRend.lightProbeUsage = LightProbeUsage.Off;
        crown.gameObject.SetActive(false);

        whirlDisc = new GameObject("Whirl").transform;
        whirlDisc.SetParent(transform, false);
        whirlDisc.gameObject.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        whirlRend = whirlDisc.gameObject.AddComponent<MeshRenderer>();
        whirlMat = Fx.Alpha(MagicTextures.Spiral, new Color(1f, 1f, 1f, 0f));
        whirlRend.sharedMaterial = whirlMat;
        whirlRend.shadowCastingMode = ShadowCastingMode.Off; whirlRend.receiveShadows = false; whirlRend.lightProbeUsage = LightProbeUsage.Off;
        whirlDisc.gameObject.SetActive(false);
    }

    /// <summary>Register back to 0 (or the island gone): the pillar sinks into the sea (the whirl fades), then the look removes itself.</summary>
    public void Retire()
    {
        if (Retiring) return;
        Retiring = true; retireT = 0f;
        var wm = WorldMagic.I;
        if (wm != null && reg > 0 && PillarTop > SeaY) wm.SpawnCrest(home, radius * 1.3f, radius * 1.3f + 3f, 0.25f, 0.8f);
    }

    /// <summary>v7: the plinth's top under the island (B's SectionPlinth block of its column), else NaN.</summary>
    public static float PlinthTopUnder(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (kb == null || sm == null || SectionPlinth.I == null || kb.column < 0 || kb.column >= sm.ColumnCount) return float.NaN;
        int s = sm.SectionOf(kb.column);
        float x0, x1; bool ghost; int col;
        for (int m = 0; SectionPlinth.MeasureSpan(s, m, out x0, out x1, out ghost, out col); m++) if (!ghost && col == kb.column) return SectionPlinth.TopOf(s, m);
        return float.NaN;
    }

    /// <summary>v7: the world y where the pillar breaks out — the plinth's top under the island, else the sea.</summary>
    public float BaseY { get; private set; } = SeaY;

    /// <summary>The platform's centre where it stands this frame. v8 (S17: "the pillar rides with the island; it stays up at its tower height in
    /// every slot"): the pillar rides the belt with the island instead of holding the belt at home.</summary>
    static Vector3 HomeCenter(KeyBlock kb) => kb.VisualCenter;

    /// <summary>World y of the island's underside where the pillar meets it: the platform's bottom face (v7: B's BaseUndersideY — a falling stair's
    /// base slab sits BaseDrop lower), or the belt's under a belted island (the belt is the island's child, lifted with it but not hopping with the
    /// glide back).</summary>
    public static float UndersideOf(KeyBlock kb)
    {
        if (kb == null) return SeaY;
        float y = kb.transform.position.y;
        if (kb.Belt != null) return y - kb.BeltOffset.y + ProjectConfig.BeltTopY - Belt.Thick - kb.BaseDrop;
        return kb.BaseUndersideY;
    }

    /// <summary>One frame (WorldMagic's LateUpdate, after the island was posed).</summary>
    public void Step(float dt, bool world)
    {
        if (Island == null && !Retiring) Retire();
        twos += dt;
        bool drawTwos = twos >= 1f / Look.TwosFps || !Look.OnTwos;
        if (drawTwos) twos = 0f;
        if (Island != null)
        {
            home = HomeCenter(Island);
            // v7: the pillar stands in the platform's front half (the platform hides what is under its back half from the home camera)
            home.z -= Mathf.Max(0f, Island.Depth * 0.5f - radius * 1.12f - 0.45f);
            float pt = PlinthTopUnder(Island);
            BaseY = float.IsNaN(pt) ? SeaY : pt;
            UndersideY = UndersideOf(Island);
            State = ForceState >= 0 ? ForceState : Island.TowerState;
        }
        if (Retiring)
        {
            retireT += dt;
            float k = Mathf.Clamp01(retireT / 0.5f);
            if (reg > 0)
            {
                float top = Mathf.Lerp(PillarTop, SeaY - 0.8f, Ease.InCubic(k));
                PlacePillar(top, drawTwos);
            }
            whirl = Mathf.MoveTowards(whirl, 0f, dt * 3f);
            PoseWhirl(dt, drawTwos, 1f - k);
            if (k >= 1f) { Destroy(gameObject); }
            return;
        }
        if (!world) return;
        if (reg > 0) StepRaised(dt, drawTwos);
        else if (reg < 0) StepLowered(dt, drawTwos);
        prevState = State;
    }

    void StepRaised(float dt, bool drawTwos)
    {
        int st = State;
        bool held = st >= 1 && st <= 3;
        float target = held ? 1f : 0f;
        attach = Mathf.MoveTowards(attach, target, dt / (target > attach ? SurgeSeconds : ReleaseSeconds));
        // a new tower's stub comes up out of the water (0.45 s) instead of popping in
        appear = Mathf.MoveTowards(appear, 1f, dt / 0.45f);
        float stub = SeaY - 1f + (StubHeight + 1f) * (appear >= 1f ? 1f : Ease.OutBack(appear, 2.2f));
        float e = target > 0.5f ? Ease.OutCubic(attach) : Ease.InOutCubic(attach);
        // never above the underside (the capital holds the platform), never below the stub while bound
        float top = Mathf.Lerp(stub, Mathf.Max(stub, UndersideY), e);
        PillarTop = top;
        PlacePillar(top, drawTwos);
        // the waves, on the transitions K reports — a real rise starts near rest and a real sink near the top (a glide after a seek, a pause
        // or an undo reports 1 / 3 by its direction too: no second splash for those)
        float k01 = LiftFraction();
        burstCool -= dt;
        if (prevState >= 0)
        {
            if (prevState == 0 && (st == 1 || st == 2) && k01 < 0.35f && burstCool <= 0f) { RiseBurst(); burstCool = 1f; }
            else if ((prevState == 1 || prevState == 2) && st == 3 && k01 > 0.6f) SinkRing();
        }
        if (crownT < 1f)
        {
            crownT += dt / 0.7f;
            PoseCrown(drawTwos);
        }
        // at rest the stub breathes: a slow ring leaves it now and then (something waits down there)
        if (st == 0 && attach < 0.05f)
        {
            restClock -= dt;
            if (restClock <= 0f)
            {
                restClock = Random.Range(2.2f, 3.4f);
                Fx.Ripple(new Vector3(home.x, SeaY, home.z), MagicTextures.FoamCream, radius * 0.9f, 1.3f);
            }
        }
    }

    void PlacePillar(float top, bool drawTwos)
    {
        if (pillar == null) return;
        pillar.position = new Vector3(home.x, top, home.z);
        bool wet = top > SeaY - 0.05f;
        if (foam.gameObject.activeSelf != wet) foam.gameObject.SetActive(wet);
        if (!wet) return;
        if (drawTwos) foamWobble = Mathf.Sin((Time.time + seedPhase) * 5.3f) * 0.035f + (attach > 0.02f && attach < 0.98f ? 0.12f : 0f);
        float fr = WaterRadius(top) * (1.32f + foamWobble) * (1f + 0.3f * crownPulse);
        foam.position = new Vector3(home.x, SeaY + 0.03f, home.z);
        foam.localScale = new Vector3(fr, 1f, fr);
        foam.rotation = Quaternion.Euler(0f, (Time.time * 6f + seedPhase * 30f) % 360f, 0f);
    }

    float crownPulse => crownT < 1f ? Mathf.Sin(Mathf.Clamp01(crownT) * Mathf.PI) : 0f;

    /// <summary>The pillar's radius at the waterline when its top is at <paramref name="top"/> (MagicMeshes.Pillar widens 6 % per unit down).</summary>
    float WaterRadius(float top) => radius * (1f + 0.06f * Mathf.Max(0f, top - SeaY - 0.5f));

    /// <summary>Where the island's lift is between its rest (0) and its turn's top (1) this frame (K: SongManager.TowerRestOf / TowerTopOf).</summary>
    float LiftFraction()
    {
        if (Island == null || reg == 0) return 0f;
        float rest = SongManager.TowerRestOf(reg), top = SongManager.TowerTopOf(reg);
        return Mathf.Abs(top - rest) < 1e-4f ? 0f : Mathf.Clamp01((Island.TowerLift - rest) / (top - rest));
    }

    void PoseCrown(bool drawTwos)
    {
        if (crown == null) return;
        bool on = crownT < 1f;
        if (crown.gameObject.activeSelf != on) crown.gameObject.SetActive(on);
        if (!on || !drawTwos) return;
        float t = Mathf.Clamp01(crownT);
        float h = 2.6f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.7f) * (1f - 0.35f * t);
        float r = WaterRadius(PillarTop) * (1.2f + 0.6f * Ease.OutCubic(t));
        crown.position = new Vector3(home.x, SeaY - 0.05f, home.z);
        crown.localScale = new Vector3(r, Mathf.Max(0.001f, h), r);
    }

    void RiseBurst()
    {
        var wm = WorldMagic.I;
        Rises++; Splashes++;
        LastRiseRings = 0;
        crownT = 0f;
        if (wm == null) return;
        // v7: it breaks out of the section plinth — a ring of dust and an ink crack ring where it comes through
        if (BaseY > SeaY + 0.5f)
        {
            float rp = radius * 1.15f;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                wm.Puff(new Vector3(home.x, BaseY + 0.06f, home.z) + d * rp, d * Random.Range(1f, 2.2f) + Vector3.up * Random.Range(0.3f, 0.9f),
                        Color.Lerp(MagicTextures.FoamCream, SectionPlinth.Stone, Random.Range(0.1f, 0.45f)), Random.Range(0.4f, 0.65f), Random.Range(0.55f, 0.9f));
            }
            Fx.Ripple(new Vector3(home.x, BaseY + 0.01f, home.z), Look.InkColor, rp * 1.9f, 0.5f);
            Fx.KirbyDots(new Vector3(home.x, BaseY + 0.4f, home.z - rp), Island.chordColor, 0.9f);
        }
        float rw = WaterRadius(UndersideY), r0 = rw * 1.3f;
        for (int i = 0; i < RiseRingCount; i++)
        {
            wm.SpawnCrest(home, r0 + 0.15f * i, r0 + 5.5f + 1.3f * i, 0.75f - 0.13f * i, 1.3f + 0.22f * i, 0.12f * i);
            LastRiseRings++; TotalRings++;
        }
        Color drop = Color.Lerp(Look.SeaLine, MagicTextures.FoamCream, 0.55f);
        for (int i = 0; i < 40; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            wm.Drop(new Vector3(home.x, SeaY + 0.2f, home.z) + d * rw * Random.Range(1f, 1.4f), d * Random.Range(1.6f, 4.2f) + Vector3.up * Random.Range(5.5f, 9.5f),
                    i % 3 == 0 ? MagicTextures.FoamCream : drop, Random.Range(0.2f, 0.38f), Random.Range(0.8f, 1.2f));
        }
        // v7: on a terrace the pillar breaks up through the stone — a ring of dust round its foot on the terrace's top
        var tr = wm.TerraceOf(Island);
        if (tr != null && tr.Presence > 0.5f)
        {
            float dustY = tr.UnderPlinth ? tr.TopY + SectionPlinth.Thick + 0.08f : tr.TopY + 0.1f;   // the stone it breaks through: the plinth's top
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                wm.Puff(new Vector3(home.x, dustY, home.z) + d * rw * 1.05f, d * Random.Range(0.8f, 1.8f) + Vector3.up * Random.Range(0.2f, 0.7f),
                        Color.Lerp(MagicTextures.FoamCream, new Color(0.82f, 0.76f, 0.8f), Random.value), Random.Range(0.35f, 0.6f), Random.Range(0.55f, 0.9f));
            }
        }
        // comic speed lines up the pillar's sides: it is pushing up, fast
        for (int i = 0; i < 3; i++)
        {
            float a = (200f + i * 70f) * Mathf.Deg2Rad;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Fx.SpeedLines(new Vector3(home.x, SeaY + 1.6f + 0.5f * i, home.z) + d * rw * 1.3f, Vector3.up, MagicTextures.FoamCream);
        }
    }

    void SinkRing()
    {
        var wm = WorldMagic.I;
        SinkRings++; TotalRings++;
        if (wm != null) { float rw = WaterRadius(PillarTop); wm.SpawnCrest(home, rw * 1.35f, rw * 1.35f + 8f, 0.5f, 2.8f); }
    }

    /// <summary>GlobalClock downbeat: a small ring leaves the foot of a tower that is up (and the whirl of a dipped island pulls one in).</summary>
    public void OnDownbeat()
    {
        if (Retiring || Island == null || !GlobalClock.IsPlaying) return;
        var wm = WorldMagic.I;
        if (wm == null) return;
        // "while it is up": the hold, and the column's own first downbeat that lands in the last quarter beat of the rise (a one-bar turn has no
        // other downbeat before it sinks). v7: only during its own turn — after it the tower holds up quietly until the song resets.
        var sm = SongManager.I;
        bool inTurn = sm != null && sm.IsActiveAt(Island, GlobalClock.SongBeat + 0.02f);
        if (!inTurn) { if (State == 1 || State == 2) QuietDownbeats++; return; }
        // (attach: the pillar has surged at least part way — a frame that stalled right at the rise must not lose its downbeat)
        if (reg > 0 && (State == 2 || (State == 1 && attach > 0.3f)))
        {
            DownbeatRings++; TotalRings++;
            if (BaseY > SeaY + 0.5f)
                for (int i = 0; i < 6; i++)
                {
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    wm.Puff(new Vector3(home.x, BaseY + 0.05f, home.z) + d * radius * 1.1f, d * Random.Range(0.6f, 1.2f) + Vector3.up * 0.3f, Color.Lerp(MagicTextures.FoamCream, SectionPlinth.Stone, 0.3f), Random.Range(0.3f, 0.45f), 0.55f);
                }
            float rw = WaterRadius(PillarTop);
            wm.SpawnCrest(home, rw * 1.35f, rw * 1.35f + 3.2f, 0.36f, 0.95f);
            for (int i = 0; i < 6; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                wm.Drop(new Vector3(home.x, SeaY + 0.15f, home.z) + d * rw * 1.3f, d * Random.Range(0.8f, 1.8f) + Vector3.up * Random.Range(2.2f, 3.6f), MagicTextures.FoamCream, Random.Range(0.1f, 0.18f), 0.6f);
            }
        }
        else if (reg < 0 && (State == 1 || State == 2))
        {
            InwardRings++;
            wm.SpawnInward(home, Color.Lerp(Island.chordColor, MagicTextures.FoamCream, 0.35f), WhirlRadius * 1.05f, radius * 0.5f, 0.9f);
        }
    }

    float WhirlRadius => Island != null ? Mathf.Max(Island.Width, Island.Depth) * 0.55f : 4f;

    void StepLowered(float dt, bool drawTwos)
    {
        int st = State;
        float target = st == 1 || st == 2 ? 1f : (st == 3 ? 0.35f : 0f);
        whirl = Mathf.MoveTowards(whirl, target, dt * (target > whirl ? 2.5f : 0.8f));
        burstCool -= dt;
        if (prevState == 0 && (st == 1 || st == 2) && LiftFraction() < 0.35f && burstCool <= 0f)
        {
            burstCool = 1f;
            Dips++;
            var wm = WorldMagic.I;
            if (wm != null)
                for (int i = 0; i < 3; i++)
                {
                    wm.SpawnInward(home, Color.Lerp(Island.chordColor, MagicTextures.FoamCream, 0.35f), WhirlRadius * (1.2f - 0.1f * i), radius * 0.5f, 0.85f, 0.14f * i);
                    InwardRings++;
                }
        }
        PoseWhirl(dt, drawTwos, 1f);
    }

    void PoseWhirl(float dt, bool drawTwos, float fade)
    {
        if (whirlDisc == null || !whirlDisc.gameObject.activeSelf) return;
        whirlAngle += dt * (25f + 190f * whirl);
        if (!drawTwos) return;
        float r = WhirlRadius * (0.85f + 0.2f * whirl);
        whirlDisc.position = new Vector3(home.x, SeaY + 0.025f, home.z);
        whirlDisc.rotation = Quaternion.Euler(0f, -whirlAngle, 0f);
        whirlDisc.localScale = new Vector3(r * 2f, 1f, r * 2f);
        var c = whirlMat.GetColor("_Color");
        c.a = (0.2f + 0.65f * whirl) * fade;
        whirlMat.SetColor("_Color", c);
    }

    void OnDestroy()
    {
        if (stoneMat != null) Destroy(stoneMat);
        if (bandMat != null) Destroy(bandMat);
        if (foamMat != null) Destroy(foamMat);
        if (whirlMat != null) Destroy(whirlMat);
    }
}
