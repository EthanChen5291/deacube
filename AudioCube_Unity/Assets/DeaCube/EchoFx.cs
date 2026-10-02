using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v9 (R; the user: "echos should have ui effects too"): the look of an ECHO firing. A repeated softer note (AudioCube.Hit.echo: the note repeats
/// at its step length) shows, at each repeat, a fading AFTERIMAGE of the cube hopping again on its tile (a ghost cube in the cube's colour, smaller
/// and dimmer each time), with an ink RING on the tile shrinking with each repeat; an octave echo copy (AudioCube.echoOf ≥ 0) gets one afterimage
/// when its note fires. A pure function of the song beat from the landing (pause / seek / replay are exact; a seek back forgets the record), pooled
/// ghosts and rings, one MaterialPropertyBlock: no per-frame GC. Fed by AudioCube.OnLanded through <see cref="WorldMagic"/>; plays in the present
/// mode too.
/// </summary>
public class EchoFx
{
    /// <summary>The hop's height (u), the most beats one afterimage lasts, the ghost's opacity.</summary>
    public static float HopHeight = 0.42f, MaxBeats = 0.5f, Alpha = 0.62f;
    public const int Cap = 48, GhostCap = 24;

    /// <summary>Counters since Play (tests): echo notes recorded, afterimages scheduled, the most shown at once; shown now; records live.</summary>
    public int Fires, Images, MaxActive;
    public int ActiveImages { get; private set; }
    public int ActiveRecords { get; private set; }
    public bool Grew { get; private set; }

    struct Rec { public AudioCube cube; public TileInteraction tile; public double start; public float step; public int count; public Color c; public bool used; }
    readonly Rec[] recs = new Rec[Cap];
    int next;
    Transform root; Mesh ghostMesh; Material ghostMat, ringMat;
    readonly List<Transform> ghosts = new List<Transform>(), rings = new List<Transform>();
    readonly List<MeshRenderer> ghostR = new List<MeshRenderer>(), ringR = new List<MeshRenderer>();
    MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    public void Build(Transform parent)
    {
        if (root != null) return;
        var go = new GameObject("EchoFx");
        go.transform.SetParent(parent, false);
        root = go.transform;
        ghostMesh = MeshFactory.RoundedBox(Vector3.one, 0.14f, 3);
        ghostMat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white); ghostMat.renderQueue = 3020;
        ringMat = Fx.InkMaterial(1); ringMat.renderQueue = 3004;
        mpb = new MaterialPropertyBlock();
    }

    /// <summary>A note with <paramref name="count"/> echoes landed on <paramref name="tile"/> at song beat <paramref name="beat"/>, repeating every
    /// <paramref name="step"/> beats (0: one afterimage now — an echo copy's note).</summary>
    public void Fire(AudioCube cube, TileInteraction tile, double beat, float step, int count)
    {
        if (cube == null || tile == null || count <= 0) return;
        int slot = -1;
        for (int i = 0; i < Cap; i++) { int k = (next + i) % Cap; if (!recs[k].used) { slot = k; break; } }
        if (slot < 0) slot = next;
        next = (slot + 1) % Cap;
        recs[slot] = new Rec { cube = cube, tile = tile, start = beat, step = Mathf.Max(0f, step), count = count, c = cube.Color, used = true };
        Fires++; Images += count;
    }

    void Ensure(int i)
    {
        while (ghosts.Count <= i)
        {
            var g = new GameObject("echoGhost");
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = ghostMesh;
            var gr = g.AddComponent<MeshRenderer>(); gr.sharedMaterial = ghostMat;
            gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = false; gr.lightProbeUsage = LightProbeUsage.Off;
            g.SetActive(false);
            ghosts.Add(g.transform); ghostR.Add(gr);
            var r = new GameObject("echoRing");
            r.transform.SetParent(root, false);
            r.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.FlatRing(0.78f);
            var rr = r.AddComponent<MeshRenderer>(); rr.sharedMaterial = ringMat;
            rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false; rr.lightProbeUsage = LightProbeUsage.Off;
            r.SetActive(false);
            rings.Add(r.transform); ringR.Add(rr);
            Grew = true;
        }
    }

    /// <summary>One frame at song beat <paramref name="beat"/> (WorldMagic.FxBeat).</summary>
    public void Step(bool world, double beat, bool playing)
    {
        Grew = false;
        if (root == null) return;
        bool running = world && (playing || beat > 1e-6);
        int g = 0, live = 0;
        for (int i = 0; i < Cap; i++)
        {
            if (!recs[i].used) continue;
            ref var r = ref recs[i];
            float D = r.step > 1e-3f ? Mathf.Min(MaxBeats, r.step * 0.85f) : MaxBeats;
            double end = r.start + r.count * r.step + D;
            if (!running || r.tile == null || r.cube == null || beat < r.start - 0.5 || beat > end + 0.1) { r.used = false; continue; }
            live++;
            Vector3 top = r.cube.TopOf(r.tile);
            for (int k = 1; k <= r.count && g < GhostCap; k++)
            {
                double tk = r.start + (r.step > 1e-3f ? k * r.step : 0.0);
                float age = (float)(beat - tk);
                if (age < 0f || age >= D) continue;
                float a = age / D, fade = 1f - (k - 1) / (float)(r.count + 1);
                Ensure(g);
                float size = ProjectConfig.CubeSize * (0.95f - 0.12f * (k - 1));
                float hop = HopHeight * (1f - 0.22f * (k - 1)) * Mathf.Sin(a * Mathf.PI);
                var gt = ghosts[g];
                gt.position = top + Vector3.up * (size * 0.5f + 0.02f + hop);
                gt.localScale = new Vector3(size, size * (1f + 0.25f * Mathf.Sin(a * Mathf.PI)), size);
                if (!gt.gameObject.activeSelf) gt.gameObject.SetActive(true);
                mpb.Clear();
                mpb.SetColor(ColorId, new Color(r.c.r, r.c.g, r.c.b, Alpha * fade * Mathf.Pow(1f - a, 0.7f)));
                ghostR[g].SetPropertyBlock(mpb);
                var rt = rings[g];
                float d = Mathf.Lerp(0.5f, 1.25f, a) * (1f - 0.18f * (k - 1)) * 2f;
                rt.position = top + Vector3.up * 0.035f;
                rt.localScale = new Vector3(d, 1f, d);
                if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
                mpb.Clear();
                mpb.SetColor(ColorId, Color.Lerp(r.c, Color.white, 0.4f));
                mpb.SetFloat(IntensityId, (1f - a) * (0.85f - 0.2f * (k - 1)));
                ringR[g].SetPropertyBlock(mpb);
                g++;
            }
        }
        for (int i = g; i < ghosts.Count; i++)
        {
            if (ghosts[i].gameObject.activeSelf) ghosts[i].gameObject.SetActive(false);
            if (rings[i].gameObject.activeSelf) rings[i].gameObject.SetActive(false);
        }
        ActiveImages = g; ActiveRecords = live;
        if (g > MaxActive) MaxActive = g;
    }
}
