using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPEC v3 §7 (package E): comic secondary-motion helpers on Fx (instead of motion blur), for anyone to call:
/// <see cref="Fx.Multiples"/> (flat fading echo copies trailing a fast move), <see cref="Fx.SpeedLines"/> (tapered ink streaks) and
/// <see cref="Fx.KirbyDots"/> (a cluster of ink + colour energy dots for big moments). All pooled, all animated on twos
/// (Look.Stepped), all drawn with DeaCube/InkFx (flat colour + ink, alpha blended).
/// </summary>
public partial class Fx
{
    // ------------------------------------------------------------------ public helpers
    /// <summary>
    /// Multiples: <paramref name="count"/> (1-4) flat copies of <paramref name="src"/>'s mesh stamped at its pose now and every
    /// 1/24 s after, each fading over ~0.22 s (on twos) — the Spider-Verse "multiples" smear behind a fast move. Call it when the
    /// move starts (a hop, a fling). Needs a MeshFilter on <paramref name="src"/> (or a child).
    /// </summary>
    public static void Multiples(Transform src, Color c, int count = 3)
    {
        if (src == null) return;
        var mf = src.GetComponent<MeshFilter>();
        if (mf == null) mf = src.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var f = I;
        count = Mathf.Clamp(count, 1, 4);
        f.echoJobs.Add(new EchoJob { src = mf.transform, mesh = mf.sharedMesh, color = c, left = count, next = 0f, clock = 0f });
    }

    /// <summary>
    /// Speed lines: 4 tapered ink streaks around <paramref name="pos"/> trailing opposite to <paramref name="dir"/> (the direction of
    /// motion), with a thin <paramref name="c"/> core; they draw in and retract over ~0.3 s, on twos.
    /// </summary>
    public static void SpeedLines(Vector3 pos, Vector3 dir, Color c)
    {
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
        var f = I;
        var cam = Camera.main;
        Vector3 side = cam != null ? Vector3.Cross(dir.normalized, cam.transform.forward) : Vector3.Cross(dir.normalized, Vector3.forward);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
        side.Normalize();
        for (int i = 0; i < 4; i++)
        {
            var s = f.GetStreak();
            float lateral = (i - 1.5f) * 0.22f + Random.Range(-0.06f, 0.06f);
            s.active = true; s.age = 0f; s.dur = Random.Range(0.26f, 0.34f); s.c = c;
            s.dir = dir.normalized; s.len = Random.Range(0.8f, 1.5f); s.width = Random.Range(0.07f, 0.11f);
            s.anchor = pos + side * lateral - s.dir * Random.Range(0.1f, 0.35f);
            s.t.gameObject.SetActive(true);
            f.PoseStreak(s, 0f, cam);
        }
    }

    /// <summary>
    /// Kirby dots: a cluster of black ink, <paramref name="c"/> and pale energy dots popping one after another inside a camera-facing
    /// disc of <paramref name="radius"/> at <paramref name="pos"/> (~0.5 s, on twos). For big moments: downbeats on the lit island,
    /// merges, an island rising, island changes in the presentation.
    /// </summary>
    public static void KirbyDots(Vector3 pos, Color c, float radius = 1f)
    {
        var f = I;
        var k = f.GetKirby();
        k.active = true; k.age = 0f; k.dur = 0.5f; k.c = c; k.size = Mathf.Max(0.1f, radius) * 2f; k.seed = Random.Range(0f, 50f);
        k.t.position = pos;
        k.t.gameObject.SetActive(true);
        f.PoseKirby(k, 0f, Camera.main);
    }

    /// <summary>Live comic helper effects (diagnostics): echo copies, streaks, Kirby clusters.</summary>
    public int ActiveEchoes => echoes.Count;
    public int ActiveStreaks { get { int n = 0; foreach (var s in streakPool) if (s.active) n++; return n; } }
    public int ActiveKirby { get { int n = 0; foreach (var k in kirbyPool) if (k.active) n++; return n; } }

    // ------------------------------------------------------------------ internals
    class EchoJob { public Transform src; public Mesh mesh; public Color color; public int left; public float next, clock; }
    struct Echo { public Mesh mesh; public Matrix4x4 m; public Color c; public float age; }
    class StreakFx { public Transform t; public MeshRenderer r; public float age, dur, len, width; public Vector3 anchor, dir; public Color c; public bool active; }
    class KirbyFx { public Transform t; public MeshRenderer r; public float age, dur, size, seed; public Color c; public bool active; }

    readonly List<EchoJob> echoJobs = new List<EchoJob>();
    readonly List<Echo> echoes = new List<Echo>();
    readonly List<StreakFx> streakPool = new List<StreakFx>();
    readonly List<KirbyFx> kirbyPool = new List<KirbyFx>();
    Material echoMat, streakMat, kirbyMat;
    MaterialPropertyBlock comicMpb;
    const float EchoLife = 0.22f, EchoGap = 1f / 24f;
    static readonly int AgeId = Shader.PropertyToID("_Age");
    static readonly int SeedId = Shader.PropertyToID("_Seed");

    void BuildComic()
    {
        comicMpb = new MaterialPropertyBlock();
        echoMat = InkMaterial(5);
        streakMat = InkMaterial(3);
        kirbyMat = InkMaterial(4);
    }

    StreakFx GetStreak()
    {
        foreach (var s in streakPool) if (!s.active) return s;
        var go = new GameObject("speedLine");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = cquad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = streakMat; Quiet(mr);
        var s2 = new StreakFx { t = go.transform, r = mr };
        streakPool.Add(s2);
        return s2;
    }

    KirbyFx GetKirby()
    {
        foreach (var k in kirbyPool) if (!k.active) return k;
        var go = new GameObject("kirbyDots");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = cquad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = kirbyMat; Quiet(mr);
        var k2 = new KirbyFx { t = go.transform, r = mr };
        kirbyPool.Add(k2);
        return k2;
    }

    void StepComic(float dt, Camera cam)
    {
        // multiples: stamp the source's pose every 1/24 s, then draw every live stamp fading on twos
        for (int i = echoJobs.Count - 1; i >= 0; i--)
        {
            var j = echoJobs[i];
            if (j.src == null || j.left <= 0) { echoJobs.RemoveAt(i); continue; }
            j.clock += dt;
            if (j.clock >= j.next)
            {
                echoes.Add(new Echo { mesh = j.mesh, m = j.src.localToWorldMatrix, c = j.color, age = 0f });
                j.left--; j.next += EchoGap;
            }
        }
        for (int i = echoes.Count - 1; i >= 0; i--)
        {
            var e = echoes[i];
            e.age += dt;
            if (e.age >= EchoLife) { echoes.RemoveAt(i); continue; }
            echoes[i] = e;
            if (echoMat == null) continue;
            float t = Look.Stepped(e.age) / EchoLife;
            comicMpb.Clear();
            comicMpb.SetColor(ColorId, new Color(e.c.r, e.c.g, e.c.b, 0.62f));
            comicMpb.SetFloat(IntensityId, 1f - t);
            Graphics.DrawMesh(e.mesh, e.m, echoMat, 0, null, 0, comicMpb, ShadowCastingMode.Off, false);
        }
        for (int i = 0; i < streakPool.Count; i++)
        {
            var s = streakPool[i]; if (!s.active) continue;
            s.age += dt; float t = s.age / s.dur;
            if (t >= 1f) { s.active = false; s.t.gameObject.SetActive(false); continue; }
            PoseStreak(s, Look.Stepped(s.age) / s.dur, cam);
        }
        for (int i = 0; i < kirbyPool.Count; i++)
        {
            var k = kirbyPool[i]; if (!k.active) continue;
            k.age += dt; float t = k.age / k.dur;
            if (t >= 1f) { k.active = false; k.t.gameObject.SetActive(false); continue; }
            PoseKirby(k, Look.Stepped(k.age) / k.dur, cam);
        }
    }

    void PoseStreak(StreakFx s, float t, Camera cam)
    {
        Vector3 view = cam != null ? cam.transform.forward : Vector3.forward;
        Vector3 along = -s.dir;                                   // head at the anchor, tail trailing behind the motion
        Vector3 side = Vector3.Cross(along, view);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(along, Vector3.up);
        side.Normalize();
        Vector3 normal = Vector3.Cross(side, along).normalized;
        // the centred quad's local x must run head (uv.x 0) -> tail (uv.x 1)
        var rot = Quaternion.LookRotation(normal, side);
        if (Vector3.Dot(rot * Vector3.right, along) < 0f) rot = rot * Quaternion.Euler(0f, 0f, 180f);
        s.t.rotation = rot;
        s.t.position = s.anchor + along * (s.len * 0.5f);
        s.t.localScale = new Vector3(s.len, s.width, 1f);
        comicMpb.Clear();
        comicMpb.SetColor(ColorId, s.c);
        comicMpb.SetFloat(AgeId, Mathf.Clamp01(t));
        s.r.SetPropertyBlock(comicMpb);
    }

    void PoseKirby(KirbyFx k, float t, Camera cam)
    {
        if (cam != null) k.t.rotation = cam.transform.rotation;
        k.t.localScale = new Vector3(k.size, k.size, 1f);
        comicMpb.Clear();
        comicMpb.SetColor(ColorId, k.c);
        comicMpb.SetFloat(AgeId, Mathf.Clamp01(t));
        comicMpb.SetFloat(SeedId, k.seed);
        k.r.SetPropertyBlock(comicMpb);
    }
}
