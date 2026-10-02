using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §6.7): the FLIP's mirror — "flip (the path upside down: an instant answer)". As a cube's path flips (CubeOps.Flip calls
/// Magic.Flip right before it changes the path; without the call W recognises a mirrored path itself from AudioCube.OnAnyChanged: every node at
/// cols − 1 − x, rows − 1 − z, or a keyboard's keys mirrored between the path's lowest and highest) a tall pane of silvered glass stands up at
/// the island's front edge and sweeps to the back (0.5 s), sparkles running along its top edge; as it passes each tile of the cube's new path
/// that tile flashes in the cube's colour — the melody reflected row by row. Pooled; owned and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class MirrorFx : MonoBehaviour
{
    public const float SweepSeconds = 0.5f, PaneHeight = 1.9f;

    /// <summary>Counters since Play (tests): sweeps begun (explicit + detected), sweeps from detection, tiles flashed.</summary>
    public int Sweeps, Detected, TileFlashes;
    /// <summary>Sweeps running now, and the first one's progress (0..1) and pane z.</summary>
    public int Active { get { int n = 0; foreach (var s in sweeps) if (s.on) n++; return n; } }
    public float Progress { get { foreach (var s in sweeps) if (s.on) return s.t / SweepSeconds; return -1f; } }
    public float PaneZ { get { foreach (var s in sweeps) if (s.on && s.pane != null) return s.pane.position.z; return float.NaN; } }

    class Sweep { public KeyBlock kb; public AudioCube cube; public float t; public bool on; public Transform pane, edge; public MeshRenderer paneRend; public readonly HashSet<TileInteraction> flashed = new HashSet<TileInteraction>(); }
    readonly List<Sweep> sweeps = new List<Sweep>();
    readonly Dictionary<AudioCube, int[]> shapes = new Dictionary<AudioCube, int[]>();
    readonly Dictionary<AudioCube, KeyBlock> shapeIsland = new Dictionary<AudioCube, KeyBlock>();
    readonly Dictionary<AudioCube, float> explicitAt = new Dictionary<AudioCube, float>();
    Material paneMat, edgeMat;

    /// <summary>Magic.Flip: sweep the mirror over <paramref name="c"/>'s island now.</summary>
    public void Begin(AudioCube c, bool detected = false)
    {
        if (c == null || c.Island == null) return;
        if (!detected) explicitAt[c] = Time.time;
        if (paneMat == null)
        {
            paneMat = Fx.Alpha(MagicTextures.Mirror, new Color(0.8f, 0.86f, 1f, 0.62f));
            paneMat.renderQueue = 3050;
            edgeMat = Fx.Additive(MagicTextures.Soft, new Color(0.95f, 0.95f, 1f), 1.3f);
        }
        Sweep s = null;
        foreach (var o in sweeps) if (!o.on) { s = o; break; }
        if (s == null)
        {
            s = new Sweep();
            var go = new GameObject("MirrorPane");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            s.paneRend = go.AddComponent<MeshRenderer>();
            s.paneRend.sharedMaterial = paneMat;
            s.paneRend.shadowCastingMode = ShadowCastingMode.Off; s.paneRend.receiveShadows = false; s.paneRend.lightProbeUsage = LightProbeUsage.Off;
            s.pane = go.transform;
            var e = new GameObject("MirrorEdge");
            e.transform.SetParent(transform, false);
            e.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            var er = e.AddComponent<MeshRenderer>();
            er.sharedMaterial = edgeMat;
            er.shadowCastingMode = ShadowCastingMode.Off; er.receiveShadows = false; er.lightProbeUsage = LightProbeUsage.Off;
            s.edge = e.transform;
            sweeps.Add(s);
        }
        s.kb = c.Island; s.cube = c; s.t = 0f; s.on = true; s.flashed.Clear();
        s.pane.gameObject.SetActive(true); s.edge.gameObject.SetActive(true);
        Sweeps++;
        if (detected) Detected++;
    }

    /// <summary>AudioCube.OnAnyChanged: when <paramref name="c"/>'s path became the mirror of what it was (and nobody called Magic.Flip for it
    /// just now), sweep.</summary>
    public void OnCubeChanged(AudioCube c)
    {
        if (c == null) return;
        var now = Shape(c);
        int[] was; KeyBlock wasIsland;
        bool had = shapes.TryGetValue(c, out was) & shapeIsland.TryGetValue(c, out wasIsland);
        shapes[c] = now; shapeIsland[c] = c.Island;
        if (!had || was == null || now == null || wasIsland != c.Island || c.Island == null) return;
        float t;
        if (explicitAt.TryGetValue(c, out t) && Time.time - t < 1f) return;
        if (IsMirror(c.Island, was, now)) Begin(c, true);
    }

    /// <summary>Remembers every cube's path shape (a rebuild or a load: nothing to compare with before).</summary>
    public void Snapshot()
    {
        shapes.Clear(); shapeIsland.Clear();
        foreach (var c in SequenceMaster.Cubes) if (c != null) { shapes[c] = Shape(c); shapeIsland[c] = c.Island; }
    }

    static int[] Shape(AudioCube c)
    {
        if (c.nodes.Count == 0) return null;
        var a = new int[c.nodes.Count * 2];
        for (int i = 0; i < c.nodes.Count; i++) { var t = c.nodes[i]; a[i * 2] = t != null ? t.gridX : -1; a[i * 2 + 1] = t != null ? t.gridZ : -1; }
        return a;
    }

    /// <summary>True when <paramref name="now"/> is <paramref name="was"/> turned upside down on <paramref name="kb"/> (and not the same path).</summary>
    public static bool IsMirror(KeyBlock kb, int[] was, int[] now)
    {
        if (was.Length != now.Length || was.Length < 4) return false;
        int n = was.Length / 2;
        bool same = true;
        for (int i = 0; i < was.Length; i++) if (was[i] != now[i]) { same = false; break; }
        if (same) return false;
        if (kb.IsKeyboard)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < n; i++) { lo = Mathf.Min(lo, was[i * 2]); hi = Mathf.Max(hi, was[i * 2]); }
            for (int i = 0; i < n; i++) if (now[i * 2] != lo + hi - was[i * 2]) return false;
            return true;
        }
        for (int i = 0; i < n; i++)
        {
            if (now[i * 2] != kb.cols - 1 - was[i * 2]) return false;
            if (now[i * 2 + 1] != kb.rows - 1 - was[i * 2 + 1]) return false;
        }
        return true;
    }

    public void Step(float dt, bool world)
    {
        var wm = WorldMagic.I;
        foreach (var s in sweeps)
        {
            if (!s.on) continue;
            s.t += dt;
            if (s.kb == null || s.t >= SweepSeconds + 0.12f || !world)
            {
                s.on = false; s.pane.gameObject.SetActive(false); s.edge.gameObject.SetActive(false);
                continue;
            }
            float u = Mathf.Clamp01(s.t / SweepSeconds);
            float e = Ease.InOutCubic(u);
            var b = s.kb.VisualBounds;
            float z0 = b.min.z - 0.35f, z1 = b.max.z + 0.35f, z = Mathf.Lerp(z0, z1, e);
            float y0 = s.kb.transform.position.y - 0.05f;
            float show = Mathf.Clamp01(u / 0.12f) * (1f - Mathf.Clamp01((s.t - SweepSeconds) / 0.12f));
            float h = PaneHeight * Ease.OutBack(Mathf.Clamp01(u / 0.2f), 2f) * show;
            s.pane.position = new Vector3(b.center.x, y0, z);
            s.pane.rotation = Quaternion.identity;
            s.pane.localScale = new Vector3(b.size.x + 0.6f, Mathf.Max(0.01f, h), 1f);
            // the bright top edge: a thin glow strip facing the camera, riding the pane's top
            s.edge.position = new Vector3(b.center.x, y0 + h - 0.06f, z);
            s.edge.rotation = Quaternion.identity;
            s.edge.localScale = new Vector3(b.size.x + 0.7f, 0.2f * show, 1f);
            if (wm != null && Random.value < 0.8f)
                wm.Sparkle(new Vector3(Random.Range(b.min.x, b.max.x), y0 + h, z), Vector3.up * Random.Range(0.3f, 0.9f) + Vector3.forward * 0.5f, Random.value < 0.5f ? Color.white : new Color(0.85f, 0.9f, 1f), Random.Range(0.12f, 0.22f), Random.Range(0.25f, 0.4f));
            // the path's (new) tiles flash as the pane passes them
            if (s.cube != null)
                foreach (var t in s.cube.nodes)
                {
                    if (t == null || s.flashed.Contains(t) || t.Top.z > z) continue;
                    s.flashed.Add(t);
                    t.Flash(Color.Lerp(s.cube.Color, Color.white, 0.4f), 1.2f);
                    TileFlashes++;
                }
        }
    }

    void OnDestroy()
    {
        if (paneMat != null) Destroy(paneMat);
        if (edgeMat != null) Destroy(edgeMat);
    }
}
