using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §6.4): TIME UNWINDING on a rewind island — "repeating same grid … make it like the grid is repeating itself and time
/// unwinding to the beginning everytime". In the last RewindBeats of every pass but its last (SongManager.RewindPhase: 0..1 through the unwind;
/// H zips the cubes back through their paths) a CLOCK rises over the island's hub — a cream face with ink ticks and a spiral arrow, its coral
/// hands whirling BACKWARD (counter-clockwise, two turns) while the face turns back too — the path's BEADS light in reverse order, last → first,
/// each one flaring as the light passes and shedding a streak toward the one before it, and at the pass boundary a brief NEGATIVE frame (ink)
/// then a SEPIA film flash (halftone dots, scratches) washes over the platform and every tile's top (printed on them, no parallax) and fades:
/// the grid is back at the beginning, replaying an old memory. Recognisable and ≤ RewindBeats long (the flash fades in 0.35 s after the boundary). When K's RewindPhase is still the M0 stub the
/// phase is derived here from the island's passes (the same definition). Built, re-bound and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class RewindFx : MonoBehaviour
{
    /// <summary>Seconds of the sepia flash after the boundary (the negative frame is its first 1/12 s).</summary>
    public const float FlashSeconds = 0.38f;
    /// <summary>How high above the hub the clock floats, and its size (world units).</summary>
    public const float ClockLift = 1.45f, ClockSize = 1.75f;

    public KeyBlock Island { get; private set; }
    public int Index { get; private set; }
    public bool Retiring { get; private set; }
    /// <summary>The unwind's phase last frame (−1 none) and the pass it ends.</summary>
    public float Phase { get; private set; } = -1f;
    public int PhasePass { get; private set; } = -1;
    /// <summary>Counters since Play (tests): unwinds begun, boundary flashes, beads lit, streaks.</summary>
    public int Unwinds, Flashes, BeadsLit, Streaks;
    /// <summary>The clock's show (0..1) and its hands' angle (degrees, counter-clockwise) now.</summary>
    public float ClockShow => clockShow;
    public float HandAngle => handAngle;
    public float FlashAlpha => flash;
    /// <summary>Beads lit now (tests / captures).</summary>
    public int LitNow { get; private set; }

    Transform clock, hands, sepia;
    MeshRenderer sepiaRend;
    Mesh sepiaMesh;
    Material faceMat, handsMat, sepiaMat, glowMat;
    float clockShow, handAngle, faceAngle, flash, flashAge = 9f, retireT;
    readonly List<Transform> glows = new List<Transform>();
    readonly List<MeshRenderer> glowRends = new List<MeshRenderer>();
    readonly List<Vector3> beadPos = new List<Vector3>();
    readonly List<Color> beadCol = new List<Color>();
    readonly List<int> beadCube = new List<int>(), beadK = new List<int>(), beadN = new List<int>();
    float[] beadHeat = new float[0];
    MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>The unwind at song beat <paramref name="beat"/> for <paramref name="kb"/> (K's SongManager.RewindPhase: −1 outside the last
    /// RewindBeats of every pass but its last, else 0..1).</summary>
    public static float PhaseOf(KeyBlock kb, double beat, out int pass)
    {
        pass = -1;
        var sm = SongManager.I;
        if (sm == null || kb == null || kb.IsMoon || !kb.rewind || kb.Passes < 2) return -1f;
        return sm.RewindPhase(kb, beat, out pass);
    }

    public void Bind(KeyBlock kb, int index)
    {
        Island = kb; Index = index;
        if (clock == null) Build();
    }

    void Build()
    {
        mpb = new MaterialPropertyBlock();
        faceMat = Fx.Alpha(MagicTextures.ClockFace, new Color(1f, 0.96f, 0.88f, 1f));
        faceMat.renderQueue = 3100;
        handsMat = Fx.Alpha(MagicTextures.ClockHands, Color.white);
        handsMat.renderQueue = 3101;
        sepiaMat = Fx.Alpha(MagicTextures.Sepia, new Color(0.62f, 0.42f, 0.22f, 0f));
        sepiaMat.renderQueue = 3090;
        glowMat = Fx.Additive(MagicTextures.Bead, Color.white, 1.2f);
        clock = Sprite("Clock", faceMat);
        hands = Sprite("Hands", handsMat);
        var s = new GameObject("Sepia");
        s.transform.SetParent(transform, false);
        sepiaMesh = new Mesh { name = "rewindSepia" };
        s.AddComponent<MeshFilter>().sharedMesh = sepiaMesh;
        sepiaRend = s.AddComponent<MeshRenderer>();
        sepiaRend.sharedMaterial = sepiaMat;
        sepiaRend.shadowCastingMode = ShadowCastingMode.Off; sepiaRend.receiveShadows = false; sepiaRend.lightProbeUsage = LightProbeUsage.Off;
        sepia = s.transform;
        s.SetActive(false);
    }

    Transform Sprite(string name, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        go.SetActive(false);
        return go.transform;
    }

    /// <summary>The island stopped rewinding (or is gone): the clock fades, then the look removes itself.</summary>
    public void Retire() { Retiring = true; retireT = 0f; }

    // ------------------------------------------------------------------ frame
    public void Step(float dt, bool world, double beat, double prevBeat, bool playing)
    {
        if (Island == null && !Retiring) Retire();
        if (Retiring)
        {
            retireT += dt;
            clockShow = Mathf.MoveTowards(clockShow, 0f, dt / 0.2f);
            PoseClock(dt, world && Island != null);
            if (retireT > 0.4f) Destroy(gameObject);
            return;
        }
        int pass = -1;
        float p = -1f;
        if (world && (playing || beat > 1e-6)) p = PhaseOf(Island, beat, out pass);
        if (p < 0f) pass = -1;
        // the boundary: the unwind ran and the beat moved on past it (not a seek) → the negative + sepia flash
        if (Phase >= 0f && p < 0f && playing && beat >= prevBeat && beat - prevBeat < 0.5) StartFlash();
        if (p >= 0f && Phase < 0f) { Unwinds++; CollectBeads(); }
        Phase = p; PhasePass = pass;
        // the clock pops up over the hub for the unwind, its hands whirling back two turns, the face half a turn
        clockShow = Mathf.MoveTowards(clockShow, p >= 0f ? 1f : 0f, dt / (p >= 0f ? 0.08f : 0.22f));
        if (p >= 0f)
        {
            float e = Ease.InOutCubic(p);
            handAngle = 720f * e;
            faceAngle = 160f * e;
        }
        PoseClock(dt, world);
        StepBeads(dt, p, world);
        StepFlash(dt, world);
    }

    void PoseClock(float dt, bool world)
    {
        bool on = world && clockShow > 0.001f && Island != null;
        if (clock.gameObject.activeSelf != on) { clock.gameObject.SetActive(on); hands.gameObject.SetActive(on); }
        if (!on) return;
        var cam = Camera.main;
        Vector3 at = Island.HubTop + Vector3.up * ClockLift;
        float s = ClockSize * Ease.OutBack(clockShow, 2.4f);
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        clock.position = at; hands.position = at + (cam != null ? -cam.transform.forward * 0.02f : Vector3.zero);
        clock.rotation = face * Quaternion.Euler(0f, 0f, Look.OnTwos ? Mathf.Round(faceAngle / 7.5f) * 7.5f : faceAngle);
        hands.rotation = face * Quaternion.Euler(0f, 0f, handAngle);
        clock.localScale = new Vector3(s, s, 1f);
        hands.localScale = new Vector3(s * 0.86f, s * 0.86f, 1f);
    }

    /// <summary>Remembers the island's path beads (every resident cube's, in path order) for this unwind.</summary>
    void CollectBeads()
    {
        beadPos.Clear(); beadCol.Clear(); beadCube.Clear(); beadK.Clear(); beadN.Clear();
        var cubes = SequenceMaster.Cubes;
        for (int ci = 0; ci < cubes.Count; ci++)
        {
            var c = cubes[ci];
            if (c == null || c.Island != Island || c.IsOnMoon || c.nodes.Count == 0 || !c.PathVisible) continue;
            for (int i = 0; i < c.nodes.Count; i++) { beadPos.Add(c.BeadPosition(i)); beadCol.Add(c.Color); beadCube.Add(ci); beadK.Add(i); beadN.Add(c.nodes.Count); }
        }
        if (beadHeat.Length < beadPos.Count) beadHeat = new float[beadPos.Count];
        for (int i = 0; i < beadHeat.Length; i++) beadHeat[i] = 0f;
    }

    void StepBeads(float dt, float p, bool world)
    {
        int n = beadPos.Count;
        // the light runs from the last bead to the first while the phase goes 0 → 1 (every cube its own path backward, together)
        int lit = 0;
        if (p >= 0f && n > 0)
        {
            for (int i = n - 1; i >= 0; i--)
            {
                int idx = Mathf.Clamp(Mathf.FloorToInt((1f - p) * beadN[i]), 0, beadN[i] - 1);
                if (beadK[i] < idx) continue;
                if (beadHeat[i] <= 0f)
                {
                    beadHeat[i] = 1f; BeadsLit++;
                    // a streak from this bead back toward the one before it (the way time runs)
                    var wm = WorldMagic.I;
                    if (wm != null && i > 0 && beadCube[i - 1] == beadCube[i])
                    {
                        Vector3 a = beadPos[i] + Vector3.up * 0.3f, b = beadPos[i - 1] + Vector3.up * 0.3f;
                        for (int k = 0; k < 4; k++) wm.Sparkle(Vector3.Lerp(a, b, k / 4f), (b - a).normalized * 2.2f + Random.insideUnitSphere * 0.3f, k % 2 == 0 ? Color.white : beadCol[i], Random.Range(0.12f, 0.2f), Random.Range(0.18f, 0.3f));
                        Streaks++;
                    }
                }
            }
        }
        for (int i = 0; i < n; i++) if (beadHeat[i] > 0f) { beadHeat[i] = Mathf.Max(0f, beadHeat[i] - dt / 0.45f); if (beadHeat[i] > 0.02f) lit++; }
        LitNow = lit;
        // glow sprites on the lit beads (pooled)
        var cam = Camera.main;
        int g = 0;
        if (world)
            for (int i = 0; i < n; i++)
            {
                if (beadHeat[i] <= 0.02f) continue;
                if (g >= glows.Count) NewGlow();
                var t = glows[g]; var r = glowRends[g]; g++;
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t.position = beadPos[i] + Vector3.up * 0.32f;
                if (cam != null) t.rotation = cam.transform.rotation;
                float s = 0.55f * (0.6f + 0.6f * beadHeat[i]);
                t.localScale = new Vector3(s, s, 1f);
                mpb.Clear(); mpb.SetColor(ColorId, Color.Lerp(beadCol[i], Color.white, 0.35f + 0.4f * beadHeat[i])); mpb.SetFloat(IntensityId, 0.5f + beadHeat[i]);
                r.SetPropertyBlock(mpb);
            }
        for (; g < glows.Count; g++) if (glows[g].gameObject.activeSelf) glows[g].gameObject.SetActive(false);
    }

    void NewGlow()
    {
        var go = new GameObject("RewindBead");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = glowMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        glows.Add(go.transform); glowRends.Add(mr);
    }

    /// <summary>The pass boundary: a negative frame, then the sepia film washing over the platform — printed on its top and on every tile's top
    /// (island-local, one mesh: no parallax).</summary>
    public void StartFlash()
    {
        if (Island == null) return;
        flashAge = 0f; Flashes++;
        BuildSepia(Island);
        sepiaMat.mainTextureOffset = new Vector2(Random.value, Random.value);
    }

    void BuildSepia(KeyBlock kb)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        // the platform's top (a rounded rectangle fan, a hair above it)
        var outline = new List<Vector3>(); var nrm = new List<Vector3>();
        float hx = kb.Width * 0.5f, hz = kb.Depth * 0.5f;
        Vector3 c = kb.transform.InverseTransformPoint(kb.VisualCenter); c.y = -0.03f + 0.01f;
        MagicMeshes.RoundRect(hx, hz, 0.24f, 4, outline, nrm);
        v.Add(c); uv.Add(new Vector2(c.x / 2.2f, c.z / 2.2f));
        foreach (var p in outline) { var q = c + p; v.Add(q); uv.Add(new Vector2(q.x / 2.2f, q.z / 2.2f)); }
        for (int i = 0; i < outline.Count; i++) { tris.Add(0); tris.Add(1 + (i + 1) % outline.Count); tris.Add(1 + i); }
        // every tile's top
        foreach (var t in kb.tiles)
        {
            if (t == null) continue;
            var r = t.GetComponent<Renderer>();
            Vector3 size = r != null ? r.bounds.size : Vector3.one;
            Vector3 top = kb.transform.InverseTransformPoint(t.Top) + Vector3.up * 0.012f;
            float ax = size.x * 0.5f, az = size.z * 0.5f;
            int b = v.Count;
            v.Add(top + new Vector3(-ax, 0f, -az)); v.Add(top + new Vector3(ax, 0f, -az)); v.Add(top + new Vector3(ax, 0f, az)); v.Add(top + new Vector3(-ax, 0f, az));
            for (int k = b; k < b + 4; k++) uv.Add(new Vector2(v[k].x / 2.2f, v[k].z / 2.2f));
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
        }
        sepiaMesh.Clear();
        sepiaMesh.SetVertices(v); sepiaMesh.SetUVs(0, uv); sepiaMesh.SetTriangles(tris, 0);
        sepiaMesh.RecalculateBounds();
    }

    void StepFlash(float dt, bool world)
    {
        flashAge += dt;
        bool on = world && Island != null && flashAge < FlashSeconds;
        if (sepia.gameObject.activeSelf != on) sepia.gameObject.SetActive(on);
        if (!on) { flash = 0f; return; }
        // printed on the island: the wash follows its pose
        sepia.position = Island.transform.position; sepia.rotation = Island.transform.rotation;
        float st = Look.Stepped(flashAge);
        bool negative = st < 1f / Look.TwosFps;
        flash = negative ? 0.78f : 0.62f * (1f - Mathf.Clamp01((st - 1f / Look.TwosFps) / (FlashSeconds - 1f / Look.TwosFps)));
        sepiaMat.SetColor("_Color", negative ? Palette.A(Look.InkColor, flash) : Palette.A(new Color(0.66f, 0.45f, 0.24f), flash));
    }

    void OnDestroy()
    {
        if (faceMat != null) Destroy(faceMat);
        if (handsMat != null) Destroy(handsMat);
        if (sepiaMat != null) Destroy(sepiaMat);
        if (glowMat != null) Destroy(glowMat);
        if (sepiaMesh != null) Destroy(sepiaMesh);
    }
}
