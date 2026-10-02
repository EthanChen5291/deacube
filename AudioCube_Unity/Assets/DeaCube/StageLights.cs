using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v9 (L) THE STAGE LIGHTS (the user, 2026-10-02: "is there a way to do a spotlight on the melody grid in any given measure (if there is a melody
/// grid)? like if it's a melody grid then it has lights and the lighting is darker by default so it's not just a bunch of grids jumping out at you
/// and stuff - organization"). While the song plays (and in the present mode) the stage DIMS: every Toon surface, the sea and the sky go darker,
/// a little less saturated and cooler, and the islands' glows drop (the shader globals of DeaStage.hlsl: _DeaStageDim), so the accompaniment sits
/// back. In every measure where a LEAD grid sounds (<see cref="SongManager.IsLead"/>: a grid marked lead, every keyboard, stairs and phrase; never a
/// Moon) a soft cone of light comes down onto it from above (a pooled toon beam mesh, DeaCube/StageBeam) and leaves a pool of light on its tiles
/// (_DeaSpotPos / _DeaSpotBox: the Toon and SeaToon shaders light the pool — a rounded-rectangle footprint, a bright core and a softer rim band —
/// at full colour, warm, inside the dim stage), and its cubes glow at full strength (<see cref="CubeGlow"/>, next to the hold-E spotlight dim).
/// WHEN: the lead grid's own sounding measures — every bar of its cubes' play windows (SongManager's windows: per-grid measures with barOffset,
/// belts / repeat passes, rewind, carried long grids, phrases' offsets; the KeyStage fragments are the same windows) in which a note fires —
/// merged where they touch. The light fades in over the beat before the part and out over the beat after it; a pure function of the song beat
/// (pause / seek / replay / the loop wrap land exactly: a part at the loop's start fades in before the wrap). WHERE: the grid's VisualCenter
/// every frame (it follows belts, the pop-in and sink of KeyStage, the present mode's rise, towers). At most <see cref="MaxSpots"/> at once, the
/// nearest the camera first. Stopped / editing = the normal look; play / stop ease the stage in / out over <see cref="EaseS"/>. The "lights"
/// switch (HudPresent, PlayerPrefs <see cref="PrefKey"/>, on by default): off = today's look exactly (dim 0, no pools, no beams, cube glow × 1).
/// Pooled; zero allocations per frame in the steady state (the schedule is rebuilt only when the song's structure or a lead cube changes).
/// </summary>
[DefaultExecutionOrder(985)]   // after KeyStage (970) and the islands' pose: the beams follow the grids where they stand this frame
public class StageLights : MonoBehaviour
{
    public static StageLights I { get; private set; }
    public const string PrefKey = "deacube.stageLights";
    public const int MaxSpots = 4;
    /// <summary>The stage's ease on play / stop (s); the spot's fade before / after its part (beats).</summary>
    public const float EaseS = 0.4f, FadeInBeats = 1f, FadeOutBeats = 1f;
    /// <summary>The beam: its height above the grid (world units), how far back (+z, away from the home view) its lamp hangs — the shaft leans
    /// toward the camera, so the follow camera's steep view sees it as a long shaft and not end-on —, the lamp end's radius, the pool's margin.</summary>
    public const float BeamHeight = 15f, BeamBack = 9f, LampRadius = 0.8f, PoolMargin = 0.55f;
    /// <summary>The pool's vertical reach: below the grid's centre (its platform) and above it (its cubes hop).</summary>
    public const float PoolBelow = 1.6f, PoolAbove = 5.5f;
    /// <summary>The emission of a lit grid's cubes at full light (× the normal glow).</summary>
    public const float LitGlow = 1.6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I == null && Application.isPlaying) I = new GameObject("StageLights").AddComponent<StageLights>(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { onLoaded = false; Rebuilds = 0; GcFramesMeasured = 0; GcFramesAllocating = 0; GcMaxBytes = 0; }

    // ------------------------------------------------------------------ the switch
    static bool onLoaded, on = true;
    /// <summary>The "lights" switch (persisted; on by default).</summary>
    public static bool On
    {
        get { if (!onLoaded) { onLoaded = true; try { on = PlayerPrefs.GetInt(PrefKey, 1) != 0; } catch (Exception) { on = true; } } return on; }
    }
    public static void SetOn(bool v)
    {
        onLoaded = true;
        if (on == v) return;
        on = v;
        try { PlayerPrefs.SetInt(PrefKey, v ? 1 : 0); } catch (Exception) { }
        OnSwitched?.Invoke();
    }
    public static void Toggle() { SetOn(!On); }
    public static event Action OnSwitched;

    // ------------------------------------------------------------------ diagnostics (tests)
    /// <summary>The stage's eased dim now (0 normal .. 1 the full dim stage) and the value the shaders got.</summary>
    public static float Dim => I != null ? I.shown : 0f;
    public static float ShaderDim => Shader.GetGlobalFloat(DimId);
    /// <summary>Schedule rebuilds since Play; frames measured / allocating in this component's LateUpdate (steady state), the most bytes.</summary>
    public static int Rebuilds;
    public static long GcFramesMeasured, GcFramesAllocating, GcMaxBytes;
    public static void ResetGcStats() { GcFramesMeasured = 0; GcFramesAllocating = 0; GcMaxBytes = 0; }
    /// <summary>Slot <paramref name="k"/>'s island and its light this frame (0..1, the stage ease included).</summary>
    public static KeyBlock SpotIsland(int k) => I != null && k >= 0 && k < MaxSpots && I.slots[k].amt > 0f ? I.slots[k].kb : null;
    public static float SpotAmount(int k) => I != null && k >= 0 && k < MaxSpots ? I.slots[k].amt : 0f;
    /// <summary>The beam object of slot <paramref name="k"/> (tests: where it stands, whether it is active).</summary>
    public static Transform Beam(int k) => I != null && k >= 0 && k < MaxSpots && I.beams[k] != null ? I.beams[k].transform : null;
    /// <summary>Active beam objects now.</summary>
    public static int ActiveBeams { get { if (I == null) return 0; int n = 0; for (int k = 0; k < MaxSpots; k++) if (I.beams[k] != null && I.beams[k].activeSelf) n++; return n; } }
    /// <summary>The light on island <paramref name="kb"/> now (0..1).</summary>
    public static float LitOf(KeyBlock kb)
    {
        if (I == null || kb == null) return 0f;
        for (int k = 0; k < MaxSpots; k++) if (I.slots[k].amt > 0f && ReferenceEquals(I.slots[k].kb, kb)) return I.slots[k].amt;
        return 0f;
    }
    /// <summary>AudioCube's emission factor: a lit grid's cubes glow at full strength (up to <see cref="LitGlow"/> × their normal glow); 1 elsewhere
    /// (the dim stage lowers the rest in the shader).</summary>
    public static float CubeGlow(AudioCube c)
    {
        if (I == null || c == null || I.shown <= 0f) return 1f;
        var kb = c.Island;
        if (kb == null) return 1f;
        float l = LitOf(kb);
        return l > 0f ? 1f + (LitGlow - 1f) * l : 1f;
    }
    /// <summary>The sounding parts of island <paramref name="kb"/> as the spotlight sees them (song beats: start, end), for tests.</summary>
    public static List<Vector2> PartsOf(KeyBlock kb)
    {
        var l = new List<Vector2>();
        if (I == null) return l;
        I.EnsureSchedule();
        for (int i = 0; i < I.spans.Count; i++) if (ReferenceEquals(I.spans[i].kb, kb)) l.Add(new Vector2(I.spans[i].s, I.spans[i].e));
        return l;
    }
    /// <summary>The schedule's light on island <paramref name="kb"/> at song beat <paramref name="beat"/> (0..1, before the stage ease; the loop
    /// wrap included) — the pure function the spots follow.</summary>
    public static float ScheduleAt(KeyBlock kb, double beat)
    {
        if (I == null || kb == null) return 0f;
        I.EnsureSchedule();
        float best = 0f;
        for (int i = 0; i < I.spans.Count; i++) if (ReferenceEquals(I.spans[i].kb, kb)) best = Mathf.Max(best, I.Amount(I.spans[i], beat));
        return best;
    }
    /// <summary>True when some lead grid's light is up at song beat <paramref name="beat"/> (full: inside its part).</summary>
    public static bool AnyLeadAt(double beat)
    {
        if (I == null) return false;
        I.EnsureSchedule();
        for (int i = 0; i < I.spans.Count; i++) if (beat >= I.spans[i].s && beat < I.spans[i].e) return true;
        return false;
    }
    /// <summary>Forces a schedule rebuild now (tests).</summary>
    public static void RefreshNow() { if (I != null) { I.dirty = true; I.EnsureSchedule(); } }

    // ------------------------------------------------------------------ shader globals
    static readonly int DimId = Shader.PropertyToID("_DeaStageDim");
    static readonly int SpotPosId = Shader.PropertyToID("_DeaSpotPos");
    static readonly int SpotBoxId = Shader.PropertyToID("_DeaSpotBox");
    static readonly int AmountId = Shader.PropertyToID("_Amount");
    readonly Vector4[] spotPos = new Vector4[MaxSpots], spotBox = new Vector4[MaxSpots];

    // ------------------------------------------------------------------ state
    struct Span { public KeyBlock kb; public float s, e; public bool inLoop; }
    struct Slot { public KeyBlock kb; public float amt; public bool used; }
    struct Cand { public KeyBlock kb; public float amt, dist; }
    readonly List<Span> spans = new List<Span>(256);
    readonly Slot[] slots = new Slot[MaxSpots];
    const int MaxCand = 32;
    readonly Cand[] cands = new Cand[MaxCand];
    int candCount;
    readonly GameObject[] beams = new GameObject[MaxSpots];
    readonly MeshFilter[] beamMf = new MeshFilter[MaxSpots];
    readonly Mesh[] beamMesh = new Mesh[MaxSpots];
    readonly MeshRenderer[] beamMr = new MeshRenderer[MaxSpots];
    readonly Vector2[] beamSize = new Vector2[MaxSpots];
    MaterialPropertyBlock mpb;
    Material beamMat;
    float t01, shown, pushedDim = -1f;
    double heldBeat;
    bool dirty = true, pushedZero;
    int sig = int.MinValue;
    // the beam mesh scratch (rebuilt only when a slot takes a grid of another size)
    const int Seg = 48;
    readonly List<Vector3> mv = new List<Vector3>(Seg * 2 + 2), mn = new List<Vector3>(Seg * 2 + 2);
    readonly List<Vector2> muv = new List<Vector2>(Seg * 2 + 2);
    readonly List<int> mt = new List<int>(Seg * 6);
    // the schedule's scratch
    readonly List<Vector2> bars = new List<Vector2>(64);
    readonly Dictionary<KeyBlock, List<Vector2>> perIsland = new Dictionary<KeyBlock, List<Vector2>>();
    readonly List<KeyBlock> order = new List<KeyBlock>();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        mpb = new MaterialPropertyBlock();
        AudioCube.OnAnyChanged += HandleCubeChanged;
    }

    void OnDestroy()
    {
        AudioCube.OnAnyChanged -= HandleCubeChanged;
        if (I == this) { I = null; PushZero(); }
        for (int k = 0; k < MaxSpots; k++) if (beamMesh[k] != null) Destroy(beamMesh[k]);
        if (beamMat != null) Destroy(beamMat);
    }

    void HandleCubeChanged(AudioCube c) { dirty = true; }

    /// <summary>Lights off / a reload: every global at its neutral value (the shaders draw exactly today's look).</summary>
    void PushZero()
    {
        for (int k = 0; k < MaxSpots; k++) { spotPos[k] = Vector4.zero; spotBox[k] = Vector4.zero; }
        Shader.SetGlobalFloat(DimId, 0f);
        Shader.SetGlobalVectorArray(SpotPosId, spotPos);
        Shader.SetGlobalVectorArray(SpotBoxId, spotBox);
        pushedDim = 0f; pushedZero = true;
    }

    // ================================================================== the frame
    void LateUpdate()
    {
        long a0 = AllocNow();
        bool steady = !dirty;
        Step();
        long used = AllocNow() - a0;
        if (a0 >= 0 && steady && !dirty && shown > 0f)
        {
            GcFramesMeasured++;
            if (used > 0) { GcFramesAllocating++; GcMaxBytes = Math.Max(GcMaxBytes, used); }
        }
    }

    void Step()
    {
        var sm = SongManager.I;
        bool present = Presenter.Active;
        bool want = On && sm != null && sm.HasSong && (present || (GlobalClock.IsPlaying && !MainMenu.IsShown));
        // the stage eases in / out in real time (play / stop is a real-time gesture; the spots themselves follow the beat)
        t01 = Mathf.MoveTowards(t01, want ? 1f : 0f, Time.unscaledDeltaTime / EaseS);
        if (!On) t01 = 0f;   // off = today's look at once
        shown = t01 * t01 * (3f - 2f * t01);
        if (shown <= 0f)
        {
            for (int k = 0; k < MaxSpots; k++) { slots[k].amt = 0f; slots[k].kb = null; if (beams[k] != null && beams[k].activeSelf) beams[k].SetActive(false); }
            if (!pushedZero) PushZero();
            return;
        }
        pushedZero = false;
        CheckSignature(sm);
        EnsureSchedule();
        // the beat: the present mode's own, else the song's; held while the stage fades out after a stop (no jump to the stop's beat 0)
        if (want) heldBeat = present ? Presenter.SongBeat : GlobalClock.SongBeatD;
        double beat = heldBeat;
        Gather(beat);
        Assign();
        Push();
    }

    /// <summary>A cheap per-frame signature of what the schedule reads (islands, their windows' starts, passes, lead marks; the cubes' windows).</summary>
    void CheckSignature(SongManager sm)
    {
        int h;
        unchecked
        {
            h = 17;
            var isl = sm.Islands;
            h = h * 31 + isl.Count;
            for (int i = 0; i < isl.Count; i++)
            {
                var kb = isl[i];
                if (kb == null) { h = h * 31 + 1; continue; }
                h = h * 31 + kb.GetInstanceID();
                h = h * 31 + Mathf.RoundToInt(kb.startBeatOffset * 8f);
                h = h * 31 + kb.Passes * 4 + kb.kind;
                h = h * 31 + (kb.lead ? 1 : 0) + (kb.sleep ? 2 : 0);
            }
            var cubes = SequenceMaster.Cubes;
            h = h * 31 + cubes.Count;
            for (int i = 0; i < cubes.Count; i++) { var c = cubes[i]; if (c != null) h = h * 31 + c.windows.Count; }
            h = h * 31 + Mathf.RoundToInt(GlobalClock.TotalBeats * 8f) + GlobalClock.BeatsPerBar * 7;
        }
        if (h != sig) { sig = h; dirty = true; }
    }

    // ================================================================== the schedule
    void EnsureSchedule()
    {
        if (!dirty) return;
        dirty = false;
        Rebuilds++;
        spans.Clear();
        foreach (var kv in perIsland) kv.Value.Clear();
        order.Clear();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        var cubes = SequenceMaster.Cubes;
        for (int ci = 0; ci < cubes.Count; ci++)
        {
            var c = cubes[ci];
            if (c == null || c.IsOnMoon || c.Moon != null || c.Hologram || c.nodes.Count == 0) continue;
            var home = c.Island;
            if (!SongManager.IsLead(home)) continue;
            for (int w = 0; w < c.windows.Count; w++)
            {
                var win = c.windows[w];
                if (win.silent || win.length <= 0f) continue;
                var target = win.island != null ? win.island : home;
                if (target == null || target.IsMoon) continue;
                // the measures of this window in which a note fires (Decide: rests, coins, mutes and a sleeping grid stay dark)
                bars.Clear();
                int lastBar = -1;
                for (int k = 0; k < 4096; k++)
                {
                    float sl = c.StepStartLocalBeat(k);
                    if (sl >= win.length - 1e-4f) break;
                    if (!c.Decide(w, k, win.order).fires) continue;
                    int b = Mathf.FloorToInt((sl + 1e-4f) / bpb);
                    if (b == lastBar) continue;
                    lastBar = b;
                    float s0 = win.start + b * bpb, s1 = Mathf.Min(win.start + win.length, s0 + bpb);
                    bars.Add(new Vector2(s0, s1));
                }
                if (bars.Count == 0) continue;
                List<Vector2> l;
                if (!perIsland.TryGetValue(target, out l)) { l = new List<Vector2>(16); perIsland[target] = l; }
                if (l.Count == 0) order.Add(target);
                l.AddRange(bars);
            }
        }
        double ls = GlobalClock.LoopStartBeat, le = GlobalClock.LoopEndBeat;
        for (int o = 0; o < order.Count; o++)
        {
            var kb = order[o];
            var l = perIsland[kb];
            l.Sort((a, b) => a.x.CompareTo(b.x));
            float s = l[0].x, e = l[0].y;
            for (int i = 1; i < l.Count; i++)
            {
                if (l[i].x <= e + 1e-3f) { e = Mathf.Max(e, l[i].y); continue; }
                spans.Add(new Span { kb = kb, s = s, e = e, inLoop = s >= ls - 1e-3 && e <= le + 1e-3 });
                s = l[i].x; e = l[i].y;
            }
            spans.Add(new Span { kb = kb, s = s, e = e, inLoop = s >= ls - 1e-3 && e <= le + 1e-3 });
        }
        spans.Sort((a, b) => a.s.CompareTo(b.s));
        // drop the lists of islands that left the song (destroyed keys)
        if (perIsland.Count > 4 * Mathf.Max(8, sm.Islands.Count))
        {
            var keep = new Dictionary<KeyBlock, List<Vector2>>();
            foreach (var kv in perIsland) if (kv.Key != null) keep[kv.Key] = kv.Value;
            perIsland.Clear(); foreach (var kv in keep) perIsland[kv.Key] = kv.Value;
        }
    }

    /// <summary>The light of span <paramref name="sp"/> at song beat <paramref name="beat"/>: up over the beat before it, full through it, down
    /// over the beat after it; a looping song also reads the next / previous pass of the loop for a part at its start / end.</summary>
    float Amount(Span sp, double beat)
    {
        float a = Ramp(sp, beat);
        bool looping = GlobalClock.LoopSong || GlobalClock.HasRegion;
        if (looping && sp.inLoop)
        {
            double L = GlobalClock.LoopEndBeat - GlobalClock.LoopStartBeat;
            if (L > 1e-3)
            {
                if (beat >= GlobalClock.LoopEndBeat - FadeInBeats) a = Mathf.Max(a, Ramp(sp, beat - L));
                if (beat < GlobalClock.LoopStartBeat + FadeOutBeats) a = Mathf.Max(a, Ramp(sp, beat + L));
            }
        }
        return a;
    }

    static float Ramp(Span sp, double beat)
    {
        if (beat < sp.s - FadeInBeats || beat >= sp.e + FadeOutBeats) return 0f;
        if (beat < sp.s) { float u = (float)((beat - (sp.s - FadeInBeats)) / FadeInBeats); return u * u * (3f - 2f * u); }
        if (beat < sp.e) return 1f;
        float d = 1f - (float)((beat - sp.e) / FadeOutBeats);
        return d * d * (3f - 2f * d);
    }

    // ================================================================== picking the spots
    void Gather(double beat)
    {
        candCount = 0;
        var cam = Camera.main;
        Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
        for (int i = 0; i < spans.Count; i++)
        {
            var sp = spans[i];
            if (sp.kb == null) continue;
            float a = Amount(sp, beat);
            if (a <= 0f) continue;
            int at = -1;
            for (int j = 0; j < candCount; j++) if (ReferenceEquals(cands[j].kb, sp.kb)) { at = j; break; }
            if (at >= 0) { if (a > cands[at].amt) cands[at].amt = a; continue; }
            if (candCount >= MaxCand) continue;
            cands[candCount++] = new Cand { kb = sp.kb, amt = a, dist = (sp.kb.VisualCenter - cp).sqrMagnitude };
        }
        // the nearest the camera first (insertion sort: a handful)
        for (int i = 1; i < candCount; i++)
        {
            var x = cands[i]; int j = i - 1;
            while (j >= 0 && cands[j].dist > x.dist) { cands[j + 1] = cands[j]; j--; }
            cands[j + 1] = x;
        }
        if (candCount > MaxSpots) candCount = MaxSpots;
    }

    /// <summary>Keeps a grid in the slot it had (its beam does not jump between slots); a new one takes a free slot.</summary>
    void Assign()
    {
        for (int k = 0; k < MaxSpots; k++) slots[k].used = false;
        for (int i = 0; i < candCount; i++)
            for (int k = 0; k < MaxSpots; k++)
                if (ReferenceEquals(slots[k].kb, cands[i].kb) && slots[k].kb != null) { slots[k].used = true; slots[k].amt = cands[i].amt * shown; cands[i].kb = null; break; }
        for (int i = 0; i < candCount; i++)
        {
            if (cands[i].kb == null) continue;
            for (int k = 0; k < MaxSpots; k++)
                if (!slots[k].used) { slots[k].used = true; slots[k].kb = cands[i].kb; slots[k].amt = cands[i].amt * shown; break; }
        }
        for (int k = 0; k < MaxSpots; k++) if (!slots[k].used) { slots[k].amt = 0f; slots[k].kb = null; }
    }

    // ================================================================== the look
    void Push()
    {
        for (int k = 0; k < MaxSpots; k++)
        {
            var kb = slots[k].kb;
            float a = slots[k].amt;
            if (kb == null || a <= 0f)
            {
                spotPos[k] = Vector4.zero; spotBox[k] = Vector4.zero;
                if (beams[k] != null && beams[k].activeSelf) beams[k].SetActive(false);
                continue;
            }
            Vector3 c = kb.VisualCenter;
            float hx = kb.Width * 0.5f + PoolMargin, hz = kb.Depth * 0.5f + PoolMargin;
            float top = Mathf.Max(c.y, KeyBlock.SeaSurfaceY + 0.2f) + PoolAbove;   // under the sea: the pool lights the water over it
            spotPos[k] = new Vector4(c.x, c.y, c.z, a);
            spotBox[k] = new Vector4(hx, hz, Mathf.Min(c.y, KeyBlock.SeaSurfaceY) - PoolBelow, top);
            var go = EnsureBeam(k);
            if (go == null) continue;
            if (!go.activeSelf) go.SetActive(true);
            if (Mathf.Abs(beamSize[k].x - hx) > 0.01f || Mathf.Abs(beamSize[k].y - hz) > 0.01f) { beamSize[k] = new Vector2(hx, hz); BuildBeam(beamMesh[k], hx, hz); }
            float foot = Mathf.Max(c.y, KeyBlock.SeaSurfaceY + 0.05f);   // the beam stands on the grid, or on the water over a grid under the sea
            go.transform.position = new Vector3(c.x, foot, c.z);
            mpb.SetFloat(AmountId, a);
            beamMr[k].SetPropertyBlock(mpb);
        }
        float d = shown;
        if (Mathf.Abs(d - pushedDim) > 1e-4f) { Shader.SetGlobalFloat(DimId, d); pushedDim = d; }
        Shader.SetGlobalVectorArray(SpotPosId, spotPos);
        Shader.SetGlobalVectorArray(SpotBoxId, spotBox);
    }

    GameObject EnsureBeam(int k)
    {
        if (beams[k] != null) return beams[k];
        if (beamMat == null)
        {
            var sh = Shader.Find("DeaCube/StageBeam");
            if (sh == null) return null;
            beamMat = new Material(sh) { name = "StageBeam" };
        }
        var go = new GameObject("StageBeam_" + k);
        go.transform.SetParent(transform, false);
        beamMf[k] = go.AddComponent<MeshFilter>();
        beamMesh[k] = new Mesh { name = "StageBeam_" + k };
        beamMesh[k].MarkDynamic();
        beamMf[k].sharedMesh = beamMesh[k];
        beamMr[k] = go.AddComponent<MeshRenderer>();
        beamMr[k].sharedMaterial = beamMat;
        beamMr[k].shadowCastingMode = ShadowCastingMode.Off; beamMr[k].receiveShadows = false;
        beamMr[k].lightProbeUsage = LightProbeUsage.Off; beamMr[k].reflectionProbeUsage = ReflectionProbeUsage.Off;
        beamSize[k] = new Vector2(-1f, -1f);
        go.SetActive(false);
        beams[k] = go;
        return go;
    }

    /// <summary>The beam: an open frustum from the pool's rounded-rectangle footprint (half extents hx, hz at y 0) up to the lamp's small circle
    /// (y BeamHeight); uv.y 0 at the pool, 1 at the lamp; normals point out from the beam's axis.</summary>
    void BuildBeam(Mesh m, float hx, float hz)
    {
        mv.Clear(); mn.Clear(); muv.Clear(); mt.Clear();
        for (int i = 0; i <= Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            // a superellipse (n = 4): the rounded rectangle of the grid's footprint
            float ex = Mathf.Sign(cs) * Mathf.Sqrt(Mathf.Abs(cs)), ez = Mathf.Sign(sn) * Mathf.Sqrt(Mathf.Abs(sn));
            var bottom = new Vector3(ex * hx, 0f, ez * hz);
            var topP = new Vector3(cs * LampRadius, BeamHeight, BeamBack + sn * LampRadius);
            var n = new Vector3(cs, 0.15f, sn).normalized;
            mv.Add(bottom); mn.Add(n); muv.Add(new Vector2(i / (float)Seg, 0f));
            mv.Add(topP); mn.Add(n); muv.Add(new Vector2(i / (float)Seg, 1f));
        }
        for (int i = 0; i < Seg; i++)
        {
            int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            mt.Add(b0); mt.Add(t0); mt.Add(b1);
            mt.Add(b1); mt.Add(t0); mt.Add(t1);
        }
        m.Clear();
        m.SetVertices(mv); m.SetNormals(mn); m.SetUVs(0, muv); m.SetTriangles(mt, 0);
        m.bounds = new Bounds(new Vector3(0f, BeamHeight * 0.5f, BeamBack * 0.5f), new Vector3(hx * 2f + 2f, BeamHeight + 1f, hz * 2f + BeamBack + 2f));
    }

    // ================================================================== the icon (the header's lead button, the selection bar, the HUD switch)
    static readonly List<Vector2> q = new List<Vector2>(16);
    /// <summary>The standard stage SPOTLIGHT: a lamp can on a yoke tilted down to the right, its beam fanning onto a little floor; gold beam and lens
    /// while <paramref name="on"/>, faint and unlit when not. <paramref name="k"/> scales it (1 = a 36 px button).</summary>
    public static void PaintSpotlight(InkPainter p, Vector2 c, bool on, float k = 1f)
    {
        Color ink = on ? Comic.Ink : Comic.A(Comic.Ink, 0.62f);
        Vector2 d = new Vector2(0.6f, -0.8f), n = new Vector2(0.8f, 0.6f);   // the lamp's axis (down-right) and its side
        Vector2 lamp = c + new Vector2(-6.5f, 7f) * k;
        Func<float, float, Vector2> P = (u, v) => lamp + (d * u + n * v) * k;
        // the beam: a fan from the lens to the floor (gold when on, a dotted outline when off)
        Vector2 l0 = P(6f, 4.2f), l1 = P(6f, -4.2f);
        Vector2 f0 = c + new Vector2(-1f, -12.5f) * k, f1 = c + new Vector2(14.5f, -8.5f) * k;
        q.Clear(); q.Add(l0); q.Add(f1); q.Add(f0); q.Add(l1);
        if (on) { p.Fill(q, 4, Comic.A(Comic.Pop, 0.85f)); p.Line(l0, f1, 1.1f * k, Comic.A(Comic.Ink, 0.5f)); p.Line(l1, f0, 1.1f * k, Comic.A(Comic.Ink, 0.5f)); }
        else { p.Line(l0, f1, 1.2f * k, Comic.A(Comic.Ink, 0.4f), false); p.Line(l1, f0, 1.2f * k, Comic.A(Comic.Ink, 0.4f), false); }
        // the pool on the floor
        p.Disc((f0 + f1) * 0.5f + new Vector2(0f, 0.5f) * k, 8.5f * k, 2.4f * k, on ? Comic.Pop : Comic.A(Comic.Ink, 0.18f));
        // the yoke (a U bracket) and its stand
        p.Line(P(-1f, 6.2f), P(-1f, -6.2f), 1.6f * k, ink, true);
        p.Line(lamp + new Vector2(-1.5f, 1f) * k, lamp + new Vector2(-6.5f, 7.5f) * k, 1.8f * k, ink, true);
        // the can: a short barrel, wider at the lens
        q.Clear(); q.Add(P(-7f, 3.4f)); q.Add(P(5.5f, 4.6f)); q.Add(P(5.5f, -4.6f)); q.Add(P(-7f, -3.4f));
        p.InkFill(q, 4, on ? Comic.Cream : Comic.A(Comic.Cream, 0.85f), 1.5f * k, ink);
        // the lens
        q.Clear(); q.Add(P(5.2f, 4.8f)); q.Add(P(7.2f, 4.8f)); q.Add(P(7.2f, -4.8f)); q.Add(P(5.2f, -4.8f));
        p.InkFill(q, 4, on ? Comic.Pop : Comic.CreamDeep, 1.3f * k, ink);
        // cooling fins on the barrel
        p.Line(P(-3.5f, 3.6f), P(-3.5f, -3.6f), 1f * k, ink);
        p.Line(P(-0.5f, 3.9f), P(-0.5f, -3.9f), 1f * k, ink);
    }

    // ------------------------------------------------------------------ allocation probe (the steady-state check)
    static Func<long> allocProbe; static bool probeTried;
    static long AllocNow()
    {
        if (!probeTried)
        {
            probeTried = true;
            try
            {
                var mi = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mi != null) allocProbe = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), mi);
            }
            catch (Exception) { allocProbe = null; }
        }
        return allocProbe != null ? allocProbe() : -1;
    }
}
