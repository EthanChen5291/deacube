using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §16.2): the look of the selected grids (GridSelection.Selected — "hold shift and drag over a section to then select all
/// the grids in that section"): each one wears a hand-drawn INK OUTLINE just outside its platform's top edge (redrawn with a slight wobble at
/// 10 fps: it boils like a line being drawn), a SOFT LIFT (K's FxLift, eased in 0.25 s) and a SHIMMER — a soft diagonal wave of light running
/// across its tiles every 1.8 s (each tile glints as the wave passes it: TileInteraction.Flash, on the tiles themselves), the grids of a section
/// one after the other, with a star at the outline's corner as it passes. Deselected grids drop back
/// and their outline fades. The only selection look in the game (B / O draw none). Owned and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class SelectionFx : MonoBehaviour
{
    /// <summary>The soft lift of a selected grid (world units), the outline's width and its gap from the platform edge.</summary>
    public const float Lift = 0.22f, LineWidth = 0.15f, LineGap = 0.1f;
    public const float ShimmerSeconds = 1.8f;

    /// <summary>Grids wearing the look now (fully or fading), and shimmer sweeps run since Play (tests).</summary>
    public int Count { get { int n = 0; foreach (var l in looks) if (l.kb != null && l.show > 0.01f) n++; return n; } }
    public int Shimmers;
    /// <summary>The look's show (0..1) on <paramref name="kb"/>.</summary>
    public float ShowOf(KeyBlock kb) { foreach (var l in looks) if (l.kb == kb) return l.show; return 0f; }

    class Sel
    {
        public KeyBlock kb; public float show; public bool want;
        public Transform line, sheen; public Mesh lineMesh, sheenMesh; public MeshRenderer lineRend, sheenRend;
        public float boil, sheenT; public int order;
        public Vector3[] v; public Vector2[] uv; public int[] tris;
    }
    readonly List<Sel> looks = new List<Sel>();
    Material lineMat, sheenMat;
    const int Segs = 120;
    float clock;

    public void Step(float dt, bool world)
    {
        if (lineMat == null)
        {
            lineMat = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Look.InkColor, 0.92f));
            lineMat.renderQueue = 3020;
            sheenMat = Fx.Additive(MagicTextures.Soft, new Color(1f, 0.97f, 0.9f), 0.55f);
        }
        clock += dt;
        var sel = GridSelection.Selected;
        foreach (var l in looks) l.want = false;
        for (int i = 0; i < sel.Count; i++)
        {
            var kb = sel[i];
            if (kb == null) continue;
            Sel l = null;
            foreach (var o in looks) if (o.kb == kb) { l = o; break; }
            if (l == null) { l = New(kb); looks.Add(l); l.sheenT = -0.12f * i; }
            l.want = true; l.order = i;
        }
        var wm = WorldMagic.I;
        for (int i = looks.Count - 1; i >= 0; i--)
        {
            var l = looks[i];
            if (l.kb == null) { Kill(l); looks.RemoveAt(i); continue; }
            l.show = Mathf.MoveTowards(l.show, l.want && world ? 1f : 0f, dt / 0.25f);
            if (!l.want && l.show <= 0f) { Kill(l); looks.RemoveAt(i); continue; }
            float e = Ease.InOutCubic(l.show);
            if (wm != null && e > 1e-4f) wm.AddLift(l.kb, Lift * e);
            Pose(l, dt, world, e, wm);
        }
    }

    Sel New(KeyBlock kb)
    {
        var l = new Sel { kb = kb };
        var go = new GameObject("SelectOutline");
        go.transform.SetParent(transform, false);
        l.lineMesh = new Mesh { name = "selectOutline" }; l.lineMesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = l.lineMesh;
        l.lineRend = go.AddComponent<MeshRenderer>();
        l.lineRend.sharedMaterial = lineMat;
        l.lineRend.shadowCastingMode = ShadowCastingMode.Off; l.lineRend.receiveShadows = false; l.lineRend.lightProbeUsage = LightProbeUsage.Off;
        l.line = go.transform;
        var sh = new GameObject("SelectSheen");
        sh.transform.SetParent(transform, false);
        l.sheenMesh = new Mesh { name = "selectSheen" }; l.sheenMesh.MarkDynamic();
        sh.AddComponent<MeshFilter>().sharedMesh = l.sheenMesh;
        l.sheenRend = sh.AddComponent<MeshRenderer>();
        l.sheenRend.sharedMaterial = sheenMat;
        l.sheenRend.shadowCastingMode = ShadowCastingMode.Off; l.sheenRend.receiveShadows = false; l.sheenRend.lightProbeUsage = LightProbeUsage.Off;
        l.sheen = sh.transform;
        l.v = new Vector3[(Segs + 1) * 2]; l.uv = new Vector2[l.v.Length]; l.tris = new int[Segs * 6];
        for (int i = 0; i < Segs; i++)
        {
            int a = i * 2;
            l.tris[i * 6] = a; l.tris[i * 6 + 1] = a + 2; l.tris[i * 6 + 2] = a + 1; l.tris[i * 6 + 3] = a + 1; l.tris[i * 6 + 4] = a + 2; l.tris[i * 6 + 5] = a + 3;
        }
        l.boil = 9f;
        return l;
    }

    void Kill(Sel l)
    {
        if (l.line != null) Destroy(l.line.gameObject);
        if (l.sheen != null) Destroy(l.sheen.gameObject);
        if (l.lineMesh != null) Destroy(l.lineMesh);
        if (l.sheenMesh != null) Destroy(l.sheenMesh);
    }

    void Pose(Sel l, float dt, bool world, float e, WorldMagic wm)
    {
        bool on = world && e > 0.001f;
        if (l.line.gameObject.activeSelf != on) { l.line.gameObject.SetActive(on); l.sheen.gameObject.SetActive(on); }
        if (!on) return;
        var kb = l.kb;
        var b = kb.VisualBounds;
        float top = kb.transform.position.y - 0.03f + 0.012f;
        l.line.position = new Vector3(b.center.x, top, b.center.z);
        // the ink line boils: redrawn with a slight seeded wobble 10 times a second (held while Look.Boil is off)
        l.boil += dt;
        if (l.boil >= 0.1f)
        {
            l.boil = 0f;
            float hx = b.size.x * 0.5f + LineGap, hz = b.size.z * 0.5f + LineGap;
            int seed = Look.Boil ? Mathf.FloorToInt(Time.time * 10f) : 0;
            uint hsh = unchecked((uint)(seed * 7919 + kb.GetInstanceID()) * 2654435761u);
            float ph = (hsh >> 8) / 16777216f * 6.28f, amp = 0.018f;   // a seeded phase per drawing (no allocation)
            for (int i = 0; i <= Segs; i++)
            {
                Vector3 n;
                Vector3 p = MeshFactory.TrackPoint(hx, hz, 0.36f, i / (float)Segs, out n);
                float wob = amp * Mathf.Sin(i * 0.41f + ph) + amp * 0.6f * Mathf.Sin(i * 0.13f + ph * 1.7f);
                float w = LineWidth * (0.85f + 0.3f * Mathf.Sin(i * 0.07f + ph)) * e;
                p += n * wob;
                l.v[i * 2] = p - n * w * 0.5f; l.v[i * 2 + 1] = p + n * w * 0.5f;
                l.uv[i * 2] = new Vector2(0.5f, 0.5f); l.uv[i * 2 + 1] = new Vector2(0.5f, 0.5f);
            }
            l.lineMesh.Clear();
            l.lineMesh.vertices = l.v; l.lineMesh.uv = l.uv; l.lineMesh.triangles = l.tris;
            l.lineMesh.RecalculateBounds();
        }
        // the shimmer: a soft diagonal wave sweeping west → east over the tiles every ShimmerSeconds (the section's grids one after the other):
        // each tile glints as the wave front crosses it
        l.sheenT += dt;
        float period = ShimmerSeconds;
        float u0 = Mathf.Repeat(l.sheenT - dt, period) / 0.75f, u1 = Mathf.Repeat(l.sheenT, period) / 0.75f;   // the sweep takes 0.75 s
        if (u1 < u0) { u0 = -0.01f; Shimmers++; }
        if (l.sheen.gameObject.activeSelf) l.sheen.gameObject.SetActive(false);
        if (u0 <= 1f && u1 >= 0f)
        {
            const float slant = 0.45f;
            float span = b.size.x + 2f * slant;
            float f0 = b.min.x - slant + span * Ease.InOutCubic(Mathf.Clamp01(u0)), f1 = b.min.x - slant + span * Ease.InOutCubic(Mathf.Clamp01(u1));
            foreach (var t in kb.tiles)
            {
                if (t == null) continue;
                Vector3 p = t.Top;
                float d = p.x + slant * ((p.z - b.center.z) / Mathf.Max(0.5f, b.extents.z));   // the front leans: a diagonal wave
                if (d > f0 && d <= f1) t.Flash(new Color(1f, 0.97f, 0.88f), 0.32f * e);
            }
            if (wm != null && u0 < 0.95f && u1 >= 0.95f)
                wm.Sparkle(new Vector3(b.max.x + LineGap, top + 0.1f, b.max.z + LineGap), Vector3.up * 0.8f, Color.white, 0.32f, 0.45f);
        }
    }

    void OnDestroy()
    {
        foreach (var l in looks) Kill(l);
        if (lineMat != null) Destroy(lineMat);
        if (sheenMat != null) Destroy(sheenMat);
    }
}
