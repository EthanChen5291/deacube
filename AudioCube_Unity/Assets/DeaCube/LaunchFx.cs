using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §21.4 — the launch is a BUILD-UP, no fling: "just keep cubes on each grid"): the look of one launching island's riser.
/// A's LaunchRiser swells through the grid's last bar (LaunchRiser.PlannedSeconds) into the next section's first downbeat (SongManager.TurnEnd)
/// and a crash lands there; the cubes do not move. Through the swell the launching grid's RIM brightens like the riser, a pure function of the
/// song beat (a seek lands right); at the crash the rim flashes and fades, and sparkles burst from the target's FIRST MEASURE
/// (SongManager.LaunchTarget). Only while the launch is heard (LaunchRiser.Audible) and has a target.
/// v9 (R; the user: "the transitions should be cooler. like there should be a visual for a riser … so just more effects"): the riser is BIG and
/// legible — a BEAM of light climbing from the island into the sky, its height the swell's progress; RINGS climbing the beam faster and faster
/// toward the top (the rising pitch made visible); sparks streaming up it; the sea under the island glowing and rippling outward; the cubes
/// crouching near the top; a camera breath (<see cref="BreathAt"/>: OrbitCamera and PresentRig read it). THE CRASH at the landing: a flash,
/// a shockwave ring racing across the sea from the target column, the target's lanes bouncing front to back, spray, the wave crest, the section
/// letter popping (SectionPlinth.Kick), the HUD's timeline cursor kicking (HudColumnRail.Kick); everything settles within a beat. Sized by the
/// launching island's ENERGY (<see cref="Big"/>: energy 3 = the full beam, rings, breath and a sea-wide shockwave; 0–1 a modest riser and a local
/// crash). The beam, rings, sea glow / rings, shockwave, flash and lane bounce are pure functions of the song beat (pause / seek / replay are exact);
/// the one-shots (sparkles, spray, bloom bump, kicks) fire on the landing's crossing. Pooled renderers, one MaterialPropertyBlock: no per-frame GC.
/// Plays in the present mode too (WorldMagic.FxBeat). Built, re-bound and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class LaunchFx : MonoBehaviour
{
    // ------------------------------------------------------------------ tuning (static: a look pass can adjust them live)
    /// <summary>The beam's full height (u, energy 3) and base radius; the rings' total cycles over the swell; the shockwave's reach (u) and life (beats).</summary>
    public static float BeamMaxHeight = 36f, BeamBaseRadius = 1.15f, RingCycles = 5.5f, ShockReach = 48f, ShockBeats = 1.5f;
    /// <summary>The crash's settle (beats: the breath, the flash, the lane bounce) and the lanes' bounce (u, energy 3).</summary>
    public static float SettleBeats = 1.5f, LaneBounce = 0.2f;
    /// <summary>The launching island TENSES through the swell (S17: "a slight lift and a lean back growing with u, released at the landing"): its lift
    /// (u, energy 3; WorldMagic.AddLift, so a long grid rises as one) and its lean back (degrees, about its visual centre, the front edge up).</summary>
    public static float IslandLift = 0.15f, IslandLean = 4f;
    public const int RingCount = 7, SeaRingCount = 3;

    public KeyBlock Island { get; private set; }
    public int Index { get; private set; }
    public bool Retiring { get; private set; }
    /// <summary>The rim's light now (0..1 through the swell, the crash flash after it) and the swell's progress (−1 outside it).</summary>
    public float RimLight { get; private set; }
    public float Swell { get; private set; } = -1f;
    /// <summary>Counters since Play (tests): swells lit, crash bursts, sparkles thrown at the crash, rising sparks, crouches, shockwaves, spray drops.</summary>
    public int Swells, Crashes, CrashSparkles, Sparks, Crouches, Shockwaves, SprayDrops;
    /// <summary>The last crash's target.</summary>
    public KeyBlock LastTarget { get; private set; }
    // v9
    /// <summary>True while the launch is heard and has a target (cached each step: the camera breath reads it).</summary>
    public bool Heard { get; private set; }
    /// <summary>The landing beat and the swell's start (song beats, cached each step).</summary>
    public double TurnEndBeat { get; private set; }
    public double SwellStart { get; private set; }
    /// <summary>How big this launch is (0.4 .. 1 by the island's energy 0..3).</summary>
    public float Big { get; private set; } = 1f;
    /// <summary>The beam's height now (0 off), its base, the rings' phase (cycles) and speed (cycles per beat), rings shown, the sea glow (0..1),
    /// the shockwave's radius now (0 off), the crash flash (0..1).</summary>
    public float BeamHeight { get; private set; }
    public bool BeamActive => BeamHeight > 0.01f;
    public Vector3 BeamBase { get; private set; }
    public float RingPhase { get; private set; }
    public float RingSpeed { get; private set; }
    public int RingsShown { get; private set; }
    public float WaterGlow { get; private set; }
    public float ShockRadius { get; private set; }
    public float Flash { get; private set; }
    /// <summary>The biggest lane lift this frame (the crash bounce); the launching island's tense this frame (its lift in u and its lean in degrees).</summary>
    public float LaneLift { get; private set; }
    public float TenseLift { get; private set; }
    public float Lean { get; private set; }
    /// <summary>True while the crash may fire at the next crossing of the landing (tests); true while this launch is live (near its swell or crash).</summary>
    public bool Armed => armed;
    public bool Live => live;

    Transform rim; MeshFilter rimFilter; Mesh rimMesh;
    Material rimMat;
    float builtW = -1f, builtD = -1f, sparkClock, lastCrashTime = -9f;
    double lastCrashBeat = double.NaN;
    bool wasSwelling, crouched, armed = true, live;
    // v9 pieces
    Transform beam, core, seaGlow, shock, flash;
    MeshRenderer beamR, coreR, seaGlowR, shockR, flashR;
    readonly Transform[] rings = new Transform[RingCount]; readonly MeshRenderer[] ringR = new MeshRenderer[RingCount];
    readonly Transform[] seaRings = new Transform[SeaRingCount]; readonly MeshRenderer[] seaRingR = new MeshRenderer[SeaRingCount];
    Material beamMat, ringMat, glowMat, shockMat;
    readonly List<KeyBlock> lanes = new List<KeyBlock>(8);
    readonly List<TileInteraction> first = new List<TileInteraction>(32);
    KeyBlock lanesFor;
    static Mesh beamMesh;
    static MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    public void Bind(KeyBlock kb, int index)
    {
        Island = kb; Index = index;
        if (rim == null)
        {
            rimMat = Fx.Additive(MagicTextures.Soft, Color.white, 0f);
            var go = new GameObject("LaunchRim");
            go.transform.SetParent(transform, false);
            rimFilter = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = rimMat;
            Quiet(mr);
            rim = go.transform;
            go.SetActive(false);
        }
        if (beam == null) BuildPieces();
        if (kb != null)
        {
            Color col = Color.Lerp(kb.chordColor, Color.white, 0.4f);
            rimMat.SetColor(ColorId, col);
            beamMat.SetColor(ColorId, Color.Lerp(kb.chordColor, Color.white, 0.55f));
            glowMat.SetColor(ColorId, col);
        }
        lanesFor = null;
    }

    public void Retire() { Retiring = true; }

    /// <summary>The swell in beats now: the riser's planned length at this tempo.</summary>
    public static double SwellBeats => LaunchRiser.PlannedSeconds() * GlobalClock.BeatsPerSecond;

    static void Quiet(MeshRenderer mr) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; }

    Transform Piece(string name, Mesh mesh, Material mat, out MeshRenderer mr)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        Quiet(mr);
        go.SetActive(false);
        return go.transform;
    }

    void BuildPieces()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (beamMesh == null) beamMesh = BuildBeamMesh(18);
        beamMat = Fx.AdditiveVertex(IconFactory.GetTexture("white"), 1f);
        ringMat = Fx.InkMaterial(1); ringMat.renderQueue = 3005;
        glowMat = Fx.Additive(IconFactory.GetTexture("glow"), Color.white, 1f);
        shockMat = Fx.InkMaterial(1); shockMat.renderQueue = 3006;
        beam = Piece("RiserBeam", beamMesh, beamMat, out beamR);
        core = Piece("RiserCore", beamMesh, beamMat, out coreR);
        var ringMesh = MagicMeshes.FlatRing(0.8f);
        for (int i = 0; i < RingCount; i++) rings[i] = Piece("RiserRing", ringMesh, ringMat, out ringR[i]);
        seaGlow = Piece("RiserSeaGlow", MeshFactory.FlatQuad(), glowMat, out seaGlowR);
        var thinRing = MagicMeshes.FlatRing(0.93f);
        for (int i = 0; i < SeaRingCount; i++) seaRings[i] = Piece("RiserSeaRing", thinRing, ringMat, out seaRingR[i]);
        shock = Piece("CrashShock", MagicMeshes.FlatRing(0.9f), shockMat, out shockR);
        flash = Piece("CrashFlash", MeshFactory.FlatQuad(), glowMat, out flashR);
    }

    /// <summary>A tapered tube (unit radius at the base, 0.55 at the top, unit height), vertex alpha bright at the base and fading to the top.</summary>
    static Mesh BuildBeamMesh(int sides)
    {
        const int bands = 6;
        var v = new Vector3[(sides + 1) * (bands + 1)]; var c = new Color[v.Length]; var uv = new Vector2[v.Length]; var n = new Vector3[v.Length];
        for (int b = 0; b <= bands; b++)
        {
            float y = b / (float)bands, r = Mathf.Lerp(1f, 0.55f, y);
            float a = Mathf.Pow(1f - y, 1.35f) * (b == 0 ? 0.75f : 1f);
            for (int i = 0; i <= sides; i++)
            {
                float ang = i / (float)sides * Mathf.PI * 2f;
                int k = b * (sides + 1) + i;
                v[k] = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
                n[k] = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                uv[k] = new Vector2(i / (float)sides, y);
                c[k] = new Color(1f, 1f, 1f, a);
            }
        }
        var tris = new int[sides * bands * 12];
        int t = 0;
        for (int b = 0; b < bands; b++)
            for (int i = 0; i < sides; i++)
            {
                int a0 = b * (sides + 1) + i, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                // both windings: the beam is seen from outside and, when the camera is inside it, from within
                tris[t++] = a0; tris[t++] = b0; tris[t++] = a1; tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
                tris[t++] = a0; tris[t++] = a1; tris[t++] = b0; tris[t++] = a1; tris[t++] = b1; tris[t++] = b0;
            }
        var m = new Mesh { name = "riser_beam", vertices = v, colors = c, uv = uv, normals = n, triangles = tris };
        m.RecalculateBounds();
        return m;
    }

    public void Step(float dt, bool world, double beat, double prevBeat, bool playing)
    {
        if (Island == null && !Retiring) Retire();
        if (Retiring) { Lean = 0f; PostLift(); HideAll(); Destroy(gameObject); return; }
        var sm = SongManager.I;
        var wm = WorldMagic.I;
        if (sm == null) return;
        // the cheap gate (perf: the song has many launches; an idle one costs one TurnEnd and a few compares a frame — no target / audible scans,
        // no transform work): live only from a beat before the swell to the crash's settle (the song's wrap handled)
        double L = sm.TurnEnd(Island), sb = SwellBeats, S = L - sb;
        TurnEndBeat = L; SwellStart = S;
        double settleAll = System.Math.Max(ShockBeats, SettleBeats), loopLen = GlobalClock.LoopLengthBeats;
        double bb = beat;
        if (L >= GlobalClock.LoopEndBeat - 0.05 && loopLen > 1.0 && beat < GlobalClock.LoopStartBeat + settleAll + 2.0) bb = beat + loopLen;
        bool near = world && bb >= S - 1.0 && bb < L + settleAll + 0.5;
        if (!near)
        {
            if (bb < L - 0.5 || !playing && beat < 1e-6) armed = true;
            if (live) { live = false; Heard = false; Swell = -1f; RimLight = 0f; TenseLift = 0f; Lean = 0f; wasSwelling = false; crouched = false; sparkClock = 0f; HideAll(); }
            return;
        }
        live = true;
        var target = sm.LaunchTarget(Island);
        bool heard = target != null && LaunchRiser.Enabled && LaunchRiser.Audible(Island);
        Heard = heard;
        Big = 0.4f + 0.2f * Mathf.Clamp(Island.energy, 0, 3);
        bool running = playing || beat > 1e-6;
        float light = 0f, u = -1f; Swell = -1f;
        if (heard && running)
        {
            if (beat >= S && beat < L) { u = (float)((beat - S) / sb); Swell = u; light = u * u * (0.85f + 0.15f * Mathf.Sin((float)beat * (6f + 18f * u))); }
            else if (beat >= L && beat < L + 0.6) light = 1.15f * (1f - (float)((beat - L) / 0.6));
        }
        // the island tenses through the swell: a slight lift (the lean is applied after the lifts: PostLift)
        float tense = Swell >= 0f && Island.SeaDrop < 0.01f ? Mathf.Pow(Swell, 1.5f) : 0f;
        TenseLift = IslandLift * Big * tense; Lean = IslandLean * Big * tense;
        if (TenseLift > 1e-4f && wm != null) wm.AddLiftStepped(Island, TenseLift);
        bool swelling = Swell >= 0f && playing;
        if (swelling && !wasSwelling) Swells++;
        wasSwelling = swelling;
        RimLight = light;
        PoseRim(light);
        PoseBeam(u, beat);
        PoseCrash(beat, L, heard && running, target, wm);
        // sparks stream up the beam as it builds (more and faster toward the top)
        if (swelling && wm != null)
        {
            sparkClock -= dt;
            float interval = Mathf.Lerp(0.07f, 0.014f, u) / Mathf.Max(0.4f, Big);
            int guard = 0;
            while (sparkClock <= 0f && guard++ < 6)
            {
                sparkClock += interval;
                Vector3 b = BeamBase;
                Vector2 d = Random.insideUnitCircle * BeamBaseRadius * Big * 0.9f;
                Vector3 p = new Vector3(b.x + d.x, b.y + Random.Range(0f, 0.6f), b.z + d.y);
                Vector3 vel = Vector3.up * Random.Range(3f, 7.5f) * (0.5f + u) * (0.6f + 0.4f * Big) + new Vector3(d.x, 0f, d.y) * 0.3f;
                wm.Sparkle(p, vel, Random.value < 0.4f ? Color.white : Color.Lerp(Island.chordColor, Color.white, Random.value * 0.6f), Random.Range(0.13f, 0.24f), Random.Range(0.55f, 0.95f));
                Sparks++;
            }
        }
        else sparkClock = 0f;
        // the cubes crouch near the top of the swell (once per swell)
        if (swelling && u >= 0.86f && !crouched)
        {
            crouched = true; Crouches++;
            foreach (var c in SequenceMaster.Cubes) if (c != null && !c.IsOnMoon && c.Island == Island) c.Squash(0.55f);
        }
        if (!swelling || u < 0.5f) crouched = false;
        // the crash: crossing TurnEnd going forward (or the song wrapping at it), once — re-armed only once the beat is well before the landing
        // again (a seek back, the wrap), so a pause's small rewind on resume (GlobalClock.PlayLatency) never fires it twice
        if (beat < L - 0.5 || !playing && beat < 1e-6) armed = true;
        bool forward = playing && beat >= prevBeat && beat - prevBeat < 2.0;   // (a stalled editor frame can step most of a beat: still a crossing)
        bool wrapped = playing && beat < prevBeat - 0.5;
        bool crashNow = forward && prevBeat < L && beat >= L;
        if (!crashNow && wrapped && prevBeat < L && L >= GlobalClock.LoopEndBeat - 0.05 && prevBeat >= L - 1.0) crashNow = true;
        if (heard && crashNow && armed)
        {
            armed = false; lastCrashBeat = L; lastCrashTime = Time.time;
            Crash(wm, target, sm);
        }
    }

    /// <summary>After WorldMagic applied the lifts (KeyBlock.SetFxLift re-poses the island upright): the lean back about the island's visual centre,
    /// the way KeyBlock's own drag tilt pivots (position = upright position + off − R·off), based on the upright pose so it never drifts; a lean of 0
    /// puts the island exactly upright again (the lift's return to 0 re-poses it too).</summary>
    public void PostLift()
    {
        if (Island == null || Retiring) return;
        var t = Island.transform;
        Quaternion cur = t.rotation;
        bool was = Quaternion.Angle(cur, Quaternion.identity) > 1e-3f;
        Vector3 off = Quaternion.Inverse(cur) * (Island.VisualCenter - t.position);   // KeyBlock's CenterOffset (VisualCenter = TransformPoint(CenterOffset))
        if (Lean <= 1e-3f) { if (was && leaned) { t.rotation = Quaternion.identity; t.position = t.position - (off - cur * off); } leaned = false; return; }
        if (was && !leaned) return;   // someone else tilts it (a drag, the sea): leave it
        Vector3 p0 = t.position - (off - cur * off);
        Quaternion r = Quaternion.Euler(Lean, 0f, 0f);
        t.rotation = r;
        t.position = p0 + (off - r * off);
        leaned = true;
    }
    bool leaned;

    void HideAll()
    {
        if (rim != null) rim.gameObject.SetActive(false);
        Show(beam, false); Show(core, false); Show(seaGlow, false); Show(shock, false); Show(flash, false);
        for (int i = 0; i < RingCount; i++) Show(rings[i], false);
        for (int i = 0; i < SeaRingCount; i++) Show(seaRings[i], false);
        BeamHeight = 0f; RingsShown = 0; WaterGlow = 0f; ShockRadius = 0f; Flash = 0f; LaneLift = 0f;
    }

    static void Show(Transform t, bool on) { if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

    void Tint(MeshRenderer r, Color c, float intensity)
    {
        mpb.Clear();
        mpb.SetColor(ColorId, c);
        mpb.SetFloat(IntensityId, intensity);
        r.SetPropertyBlock(mpb);
    }

    void PoseRim(float light)
    {
        bool on = light > 0.003f && Island != null;
        if (rim.gameObject.activeSelf != on) rim.gameObject.SetActive(on);
        if (!on) return;
        float w = Island.Width, d = Island.Depth;
        if (Mathf.Abs(w - builtW) > 0.01f || Mathf.Abs(d - builtD) > 0.01f)
        {
            builtW = w; builtD = d;
            if (rimMesh != null) Destroy(rimMesh);
            rimMesh = MagicMeshes.TrackBand(w * 0.5f + 0.2f, d * 0.5f + 0.2f, 0.42f, 0.5f, 140);
            rimFilter.sharedMesh = rimMesh;
        }
        Vector3 c = Island.VisualCenter;
        rim.position = new Vector3(c.x, Island.transform.position.y - 0.03f + 0.015f, c.z);
        rimMat.SetFloat(IntensityId, 1.25f * light);
    }

    /// <summary>The riser (a pure function of the swell's progress <paramref name="u"/>; −1 = off): the beam, its rings, the sea glow and rings.</summary>
    void PoseBeam(float u, double beat)
    {
        if (u < 0f || Island == null)
        {
            Show(beam, false); Show(core, false); Show(seaGlow, false);
            for (int i = 0; i < RingCount; i++) Show(rings[i], false);
            for (int i = 0; i < SeaRingCount; i++) Show(seaRings[i], false);
            BeamHeight = 0f; RingsShown = 0; RingPhase = 0f; RingSpeed = 0f; WaterGlow = 0f;
            return;
        }
        float big = Big;
        Vector3 c = Island.VisualCenter;
        Vector3 b = new Vector3(c.x, Island.transform.position.y + 0.25f, c.z);
        BeamBase = b;
        float h = big * (2.5f + (BeamMaxHeight - 2.5f) * Mathf.Pow(u, 1.25f));
        float r = BeamBaseRadius * big * (0.7f + 0.5f * u);
        float flicker = 0.9f + 0.1f * Mathf.Sin((float)beat * (8f + 22f * u));
        Color col = Color.Lerp(Island.chordColor, Color.white, 0.5f);
        BeamHeight = h;
        Show(beam, true); Show(core, true);
        beam.position = b; beam.localScale = new Vector3(r, h, r);
        core.position = b; core.localScale = new Vector3(r * 0.42f, h * 1.04f, r * 0.42f);
        Tint(beamR, Color.Lerp(Island.chordColor, Color.white, 0.3f), (0.35f + 1.0f * u) * flicker);
        Tint(coreR, col, (0.55f + 1.2f * u) * flicker);
        // rings climbing the beam faster and faster (their phase accelerates with the swell)
        float T = RingCycles * Mathf.Pow(u, 2.2f);
        RingPhase = T;
        RingSpeed = (float)(2.2f * RingCycles * Mathf.Pow(Mathf.Max(u, 1e-3f), 1.2f) / System.Math.Max(1e-3, TurnEndBeat - SwellStart));
        int shown = 0;
        for (int i = 0; i < RingCount; i++)
        {
            float p = T + i / (float)RingCount; p -= Mathf.Floor(p);
            bool on = p > 0.02f && p < 0.97f && p * h < h;
            Show(rings[i], on);
            if (!on) continue;
            float s = r * (2.3f - 1.2f * p) * 2f;   // the ring mesh is radius 1: scale = diameter
            rings[i].position = b + Vector3.up * (p * h);
            rings[i].localScale = new Vector3(s, 1f, s);
            Tint(ringR[i], Color.Lerp(col, Color.white, 0.3f), Mathf.Pow(1f - p, 0.8f) * Mathf.Min(1f, u * 4f) * 0.95f);
            shown++;
        }
        RingsShown = shown;
        // the water under the island glows and ripples outward
        WaterGlow = u * u;
        Show(seaGlow, true);
        float gs = (Island.Width + 6f) * (0.5f + 0.9f * u) * big;
        seaGlow.position = new Vector3(c.x, WorldMagic.SeaY + 0.05f, c.z);
        seaGlow.localScale = new Vector3(gs, 1f, gs);
        Tint(seaGlowR, col, 1.5f * u * u);
        float r0 = Mathf.Max(Island.Width, Island.Depth) * 0.6f;
        for (int i = 0; i < SeaRingCount; i++)
        {
            float p = u * 3f + i / (float)SeaRingCount; p -= Mathf.Floor(p);
            float a = Mathf.Min(1f, u * 2.5f) * Mathf.Pow(1f - p, 1.2f) * 0.9f;
            bool on = a > 0.03f;
            Show(seaRings[i], on);
            if (!on) continue;
            float rr = Mathf.Lerp(r0, r0 + 9f * big, p) * 2f;
            seaRings[i].position = new Vector3(c.x, WorldMagic.SeaY + 0.04f, c.z);
            seaRings[i].localScale = new Vector3(rr, 1f, rr);
            Tint(seaRingR[i], col, a);
        }
    }

    /// <summary>The crash's pure part (a function of the beat after the landing <paramref name="L"/>): the shockwave ring racing over the sea from the
    /// target column, the flash on the target, its lanes bouncing front to back; all settled by L + SettleBeats.</summary>
    void PoseCrash(double beat, double L, bool on, KeyBlock target, WorldMagic wm)
    {
        LaneLift = 0f;
        double bb = beat;
        double loopLen = GlobalClock.LoopLengthBeats;
        if (L >= GlobalClock.LoopEndBeat - 0.05 && loopLen > 1.0 && beat < GlobalClock.LoopStartBeat + SettleBeats + 2.0) bb = beat + loopLen;   // the landing at the song's end: the next pass's first beats
        double shockBeats = ShockBeats;
        double settle = System.Math.Max(shockBeats, SettleBeats);
        if (!on || target == null || bb < L || bb >= L + settle)
        {
            Show(shock, false); Show(flash, false);
            ShockRadius = 0f; Flash = 0f;
            return;
        }
        float big = Big;
        Vector3 tc = target.VisualCenter;
        float v = (float)((bb - L) / shockBeats);
        if (v < 1f)
        {
            float radius = Mathf.Lerp(2.5f, ShockReach * big, Ease.OutCubic(v));
            ShockRadius = radius;
            Show(shock, true);
            shock.position = new Vector3(tc.x, WorldMagic.SeaY + 0.06f, tc.z);
            shock.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            Tint(shockR, Color.Lerp(target.chordColor, Color.white, 0.6f), Mathf.Pow(1f - v, 1.4f) * (0.5f + 0.5f * big));
        }
        else { Show(shock, false); ShockRadius = 0f; }
        float fv = (float)((bb - L) / 0.5);
        if (fv < 1f)
        {
            Flash = (1f - fv) * (1f - fv);
            Show(flash, true);
            float fs = (3f + 12f * fv) * big;
            flash.position = new Vector3(tc.x, target.transform.position.y + 0.35f, tc.z);
            flash.localScale = new Vector3(fs, 1f, fs);
            Tint(flashR, Color.Lerp(target.chordColor, Color.white, 0.7f), 2.4f * Flash * big);
        }
        else { Show(flash, false); Flash = 0f; }
        // the target's lanes bounce in sequence, front to back (a wave through the column)
        if (wm != null)
        {
            if (lanesFor != target) Lanes(target);
            for (int i = 0; i < lanes.Count; i++)
            {
                var kb = lanes[i];
                if (kb == null) continue;
                float lv = (float)((bb - L - i * 0.125) / 0.75);
                if (lv < 0f || lv >= 1f) continue;
                float lift = LaneBounce * big * Mathf.Sin(lv * Mathf.PI) * (1f - lv);
                wm.AddLiftStepped(kb, lift);
                if (lift > LaneLift) LaneLift = lift;
            }
        }
    }

    /// <summary>The target column's islands, front (small z) to back.</summary>
    void Lanes(KeyBlock target)
    {
        lanesFor = target;
        var sm = SongManager.I;
        if (sm == null) { lanes.Clear(); return; }
        sm.ColumnIslands(target.column, lanes);
        for (int i = lanes.Count - 1; i >= 0; i--) if (lanes[i] == null || lanes[i].IsMoon) lanes.RemoveAt(i);
        for (int i = 1; i < lanes.Count; i++)   // insertion sort by z (no allocation)
        {
            var k = lanes[i]; int j = i - 1;
            while (j >= 0 && lanes[j].pz > k.pz) { lanes[j + 1] = lanes[j]; j--; }
            lanes[j + 1] = k;
        }
    }

    /// <summary>The crash's one-shots: sparkles and a ring on the target's first measure, its tiles glinting, a bloom bump and starburst, spray off the
    /// target's edges, a wave crest, the target's cubes squashing, the section letter and the HUD cursor kicking.</summary>
    void Crash(WorldMagic wm, KeyBlock target, SongManager sm)
    {
        Crashes++;
        LastTarget = target;
        if (target == null || wm == null) return;
        float big = Big;
        // the target's first measure: its tiles within one IslandWidth of its west edge
        float west = target.WestEdge + (target.transform.position.x - target.px);
        first.Clear();
        foreach (var t in target.tiles) if (t != null && t.Top.x < west + KeyBlock.IslandWidth) first.Add(t);
        if (first.Count == 0) foreach (var t in target.tiles) if (t != null) first.Add(t);
        Vector3 centre = Vector3.zero;
        foreach (var t in first) centre += t.Top;
        centre /= Mathf.Max(1, first.Count);
        Color col = Color.Lerp(Island != null ? Island.chordColor : Color.white, Color.white, 0.35f);
        Fx.Ripple(centre + Vector3.up * 0.05f, col, 2.2f + 1.5f * big, 0.45f);
        int n = 9 + Mathf.RoundToInt(12f * big);
        for (int i = 0; i < n; i++)
        {
            var t = first[Random.Range(0, first.Count)];
            wm.Sparkle(t.Top + Vector3.up * 0.15f, Random.insideUnitSphere * 0.9f + Vector3.up * Random.Range(1.2f, 2.2f + 2f * big), i % 3 == 0 ? Color.white : col, Random.Range(0.16f, 0.26f), Random.Range(0.4f, 0.65f));
            CrashSparkles++;
        }
        for (int i = 0; i < Mathf.Min(5, first.Count); i++) first[Random.Range(0, first.Count)].Flash(col, 0.7f);
        // v9: the flash and the shockwave's one-shots
        Shockwaves++;
        Fx.BeatBump(0.4f + 0.4f * big);
        Fx.Starburst(centre + Vector3.up * 0.9f, col, 1.2f + 1.2f * big);
        Vector3 tc = target.VisualCenter;
        float r0 = Mathf.Max(target.Width, target.Depth) * 0.6f;
        wm.SpawnCrest(tc, r0, r0 + 6f + 10f * big, 0.35f + 0.4f * big, 1.1f);
        Fx.SeaRipple(tc, col, 4f + 5f * big);
        // spray off the target's edges
        var vb = target.VisualBounds;
        int drops = 10 + Mathf.RoundToInt(16f * big);
        float top = target.transform.position.y + 0.1f;
        for (int i = 0; i < drops; i++)
        {
            bool xEdge = Random.value < 0.5f;
            Vector3 p = xEdge ? new Vector3(Random.value < 0.5f ? vb.min.x : vb.max.x, top, Random.Range(vb.min.z, vb.max.z))
                              : new Vector3(Random.Range(vb.min.x, vb.max.x), top, Random.value < 0.5f ? vb.min.z : vb.max.z);
            Vector3 away = (p - tc); away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            wm.Drop(p, away * Random.Range(1.5f, 3.5f) * (0.6f + 0.6f * big) + Vector3.up * Random.Range(2.5f, 5f), PresentWater.Spray, Random.Range(0.12f, 0.22f), Random.Range(0.5f, 0.9f));
            SprayDrops++;
        }
        // the target column's cubes land hard
        foreach (var c in SequenceMaster.Cubes) if (c != null && !c.IsOnMoon && c.Island != null && c.Island.column == target.column) c.Squash(1.3f);
        // the section letter pops; the HUD's timeline cursor kicks
        if (sm != null) SectionPlinth.Kick(sm.SectionOf(target.column), 0.35f + 0.4f * big);
        HudColumnRail.Kick(1.2f + 0.8f * big);
    }

    // ------------------------------------------------------------------ the camera breath
    /// <summary>The breath of one launch at <paramref name="beat"/>: 0 → 1 over the swell (slow, then fast), a kick down at the landing and a settle
    /// over SettleBeats; sized by <paramref name="big"/>.</summary>
    public static float BreathOf(double beat, double S, double L, float big)
    {
        double sb = L - S;
        if (sb <= 1e-6) return 0f;
        if (beat >= S && beat < L) { float u = (float)((beat - S) / sb); return big * u * u * u; }
        if (beat >= L && beat < L + SettleBeats) { float v = (float)((beat - L) / SettleBeats); return big * 0.55f * (1f - v) * (1f - v); }
        return 0f;
    }

    /// <summary>The camera breath at song beat <paramref name="beat"/> (0..1): the largest of every heard launch's (the song's wrap handled: a landing
    /// at the song's end settles over the next pass's first beats). OrbitCamera lifts and widens by it in play; PresentRig in the present mode.</summary>
    public static float BreathAt(double beat)
    {
        var wm = WorldMagic.I;
        if (wm == null) return 0f;
        var ls = wm.Launches;
        float m = 0f;
        double loopLen = GlobalClock.LoopLengthBeats, loopEnd = GlobalClock.LoopEndBeat, loopStart = GlobalClock.LoopStartBeat;
        for (int i = 0; i < ls.Count; i++)
        {
            var f = ls[i];
            if (f == null || f.Retiring || !f.Heard) continue;
            double b = beat;
            if (f.TurnEndBeat >= loopEnd - 0.05 && loopLen > 1.0 && beat < loopStart + SettleBeats + 1.0) b = beat + loopLen;
            float v = BreathOf(b, f.SwellStart, f.TurnEndBeat, f.Big);
            if (v > m) m = v;
        }
        return m;
    }

    /// <summary>The breath now (WorldMagic.FxBeat).</summary>
    public static float BreathNow => BreathAt(WorldMagic.FxBeat);

    void OnDestroy()
    {
        if (rimMat != null) Destroy(rimMat);
        if (rimMesh != null) Destroy(rimMesh);
        if (beamMat != null) Destroy(beamMat);
        if (ringMat != null) Destroy(ringMat);
        if (glowMat != null) Destroy(glowMat);
        if (shockMat != null) Destroy(shockMat);
    }
}
