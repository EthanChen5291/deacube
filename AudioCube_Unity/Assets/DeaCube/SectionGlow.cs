using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §13.3, "B builds; W adds light"): a GLOW on the section playhead — B's plinth draws a soft line sweeping left → right
/// across the playing section; this lays a warm halo along it (a soft band of light on the plinth, spanning the section's lanes), stands a soft
/// blade of light on its front end (over the ruler, turned to the camera) and rides a bright bead there that breathes with the beat and sheds a
/// sparkle on every beat, so "where are we in the section" reads from across the world. The line comes from
/// SectionPlinth.Playhead (B); without a plinth the playhead is derived from the song beat: the playing column's pass progress over its grids'
/// x span, at the section's lanes, just under its grounds. No allocations while it plays. Owned and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class SectionGlow : MonoBehaviour
{
    public const float Width = 0.9f;

    /// <summary>True while the glow shows; its line's ends this frame; true when the line came from B's SectionPlinth.Playhead.</summary>
    public bool Active { get; private set; }
    public Vector3 From { get; private set; }
    public Vector3 To { get; private set; }
    public bool FromPlinth { get; private set; }
    public int Sparkles;

    Transform band, head, blade;
    Mesh bandMesh, bladeMesh;
    Material bandMat, headMat, bladeMat;
    int lastBeat = -1;
    readonly Vector3[] bv = new Vector3[4], lv = new Vector3[4];
    static readonly Vector2[] QuadUV = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
    static readonly int[] QuadTris = { 0, 2, 1, 0, 3, 2 };
    static readonly Color[] BladeCols = { Color.white, Color.white, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) };

    public void Step(float dt, bool world)
    {
        if (band == null) Build();
        Vector3 a = Vector3.zero, b = Vector3.zero; Color c = Color.white;
        bool on = world && GlobalClock.IsPlaying && Find(out a, out b, out c);
        Active = on;
        if (band.gameObject.activeSelf != on) { band.gameObject.SetActive(on); head.gameObject.SetActive(on); blade.gameObject.SetActive(on); }
        if (!on) { lastBeat = -1; return; }
        From = a; To = b;
        // a flat band of light along the line (it runs front → back across the lanes), a little above it
        Vector3 dir = b - a; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (Width * 0.5f);
        float y0 = a.y + 0.03f, y1 = b.y + 0.03f;
        bv[0] = a - side + Vector3.up * (y0 - a.y); bv[1] = a + side + Vector3.up * (y0 - a.y); bv[2] = b + side + Vector3.up * (y1 - b.y); bv[3] = b - side + Vector3.up * (y1 - b.y);
        bandMesh.vertices = bv; bandMesh.RecalculateBounds();
        Color warm = Color.Lerp(c, new Color(1f, 0.93f, 0.78f), 0.5f);
        bandMat.SetColor("_Color", warm);
        // the bead at the front end, breathing with the beat
        var cam = Camera.main;
        float ph = GlobalClock.BeatPhase;
        float s = 0.55f + 0.3f * Mathf.Exp(-ph * 5f);
        Vector3 front = a.z < b.z ? a : b;
        // the blade: a soft vertical light on the front end, turned about y to face the camera
        Vector3 toCam = cam != null ? cam.transform.position - front : Vector3.back; toCam.y = 0f;
        Vector3 right = toCam.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, toCam.normalized) : Vector3.right;
        const float bw = 0.42f, bh = 1.6f;
        lv[0] = front - right * bw; lv[1] = front + right * bw; lv[2] = front + right * bw + Vector3.up * bh; lv[3] = front - right * bw + Vector3.up * bh;
        bladeMesh.vertices = lv; bladeMesh.RecalculateBounds();
        bladeMat.SetColor("_Color", warm);
        bladeMat.SetFloat("_Intensity", 0.9f + 0.6f * Mathf.Exp(-ph * 4f));
        head.position = front + Vector3.up * 0.12f;
        if (cam != null) head.rotation = cam.transform.rotation;
        head.localScale = new Vector3(s, s, 1f);
        headMat.SetColor("_Color", Color.Lerp(c, Color.white, 0.6f));
        int beat = GlobalClock.BeatIndex;
        if (beat != lastBeat)
        {
            if (lastBeat >= 0)
            {
                var wm = WorldMagic.I;
                if (wm != null) { wm.Sparkle(front + Vector3.up * 0.2f, Vector3.up * 1.1f + UnityEngine.Random.insideUnitSphere * 0.3f, Color.Lerp(c, Color.white, 0.5f), 0.22f, 0.4f); Sparkles++; }
            }
            lastBeat = beat;
        }
    }

    void Build()
    {
        bandMat = Fx.Additive(MagicTextures.Soft, Color.white, 1.1f);
        headMat = Fx.Additive(MagicTextures.Bead, Color.white, 1.3f);
        bladeMat = Fx.AdditiveVertex(MagicTextures.Soft, 0.9f);
        var bl = new GameObject("SectionGlowBlade");
        bl.transform.SetParent(transform, false);
        bladeMesh = new Mesh { name = "sectionBlade" }; bladeMesh.MarkDynamic();
        bladeMesh.vertices = lv; bladeMesh.uv = QuadUV; bladeMesh.colors = BladeCols; bladeMesh.triangles = QuadTris;
        bl.AddComponent<MeshFilter>().sharedMesh = bladeMesh;
        var blr = bl.AddComponent<MeshRenderer>(); blr.sharedMaterial = bladeMat;
        blr.shadowCastingMode = ShadowCastingMode.Off; blr.receiveShadows = false; blr.lightProbeUsage = LightProbeUsage.Off;
        blade = bl.transform;
        bl.SetActive(false);
        var go = new GameObject("SectionGlowBand");
        go.transform.SetParent(transform, false);
        bandMesh = new Mesh { name = "sectionGlow" }; bandMesh.MarkDynamic();
        bandMesh.vertices = bv; bandMesh.uv = QuadUV; bandMesh.triangles = QuadTris;
        go.AddComponent<MeshFilter>().sharedMesh = bandMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = bandMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        band = go.transform;
        var h = new GameObject("SectionGlowHead");
        h.transform.SetParent(transform, false);
        h.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
        var hr = h.AddComponent<MeshRenderer>(); hr.sharedMaterial = headMat;
        hr.shadowCastingMode = ShadowCastingMode.Off; hr.receiveShadows = false; hr.lightProbeUsage = LightProbeUsage.Off;
        head = h.transform;
        go.SetActive(false); h.SetActive(false);
    }

    /// <summary>The playhead line now: B's, else derived from the beat.</summary>
    bool Find(out Vector3 a, out Vector3 b, out Color c)
    {
        a = b = Vector3.zero; c = Color.white;
        FromPlinth = false;
        if (SectionPlinth.I != null)
        {
            if (SectionPlinth.Playhead(out a, out b, out c)) { FromPlinth = true; return true; }
            return false;
        }
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return false;
        int col = sm.LitColumn;
        if (col < 0) return false;
        int s = sm.SectionOf(col);
        if (s < 0) return false;
        int c0 = sm.SectionFirst(s), c1 = sm.SectionLast(s);
        float x0 = float.MaxValue, x1 = float.MinValue, zf = float.MaxValue, zb = float.MinValue, ground = float.MaxValue;
        KeyBlock anchor = null;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.column < c0 || kb.column > c1) continue;
            var bb = kb.VisualBounds;
            zf = Mathf.Min(zf, bb.min.z); zb = Mathf.Max(zb, bb.max.z);
            ground = Mathf.Min(ground, kb.GroundY);
            if (kb.column == col) { x0 = Mathf.Min(x0, bb.min.x); x1 = Mathf.Max(x1, bb.max.x); if (anchor == null) anchor = kb; }
        }
        if (anchor == null || x1 <= x0) return false;
        float plen = sm.PassLength(col);
        double local = GlobalClock.SongBeatD - sm.ColumnStart(col);
        float u = plen > 1e-4f ? (float)((local % plen) / plen) : 0f;
        float x = Mathf.Lerp(x0, x1, Mathf.Clamp01(u));
        float y = ground - 0.95f + 0.2f;
        a = new Vector3(x, y, zf - 0.6f); b = new Vector3(x, y, zb + 0.6f);
        c = anchor.chordColor;
        return true;
    }

    void OnDestroy()
    {
        if (bandMat != null) Destroy(bandMat);
        if (headMat != null) Destroy(headMat);
        if (bladeMat != null) Destroy(bladeMat);
        if (bandMesh != null) Destroy(bandMesh);
        if (bladeMesh != null) Destroy(bladeMesh);
    }
}
