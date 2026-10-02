using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §21.2 — "only explore grids moving from left to right with the long grid", ask 5: "the grid extending and being longer
/// and the cubes just going from left to right through the grid"): the light of one LONG GRID while its cubes walk. K joins an extended grid
/// (carry ≥ 1, any style) and the grids it covers into one continuous platform (SongManager.LongGrids: the run, source first — a chain of extends
/// is one long grid); H walks the extended grids' cubes left → right through it with ordinary steps. While the long grid's span plays this adds, subtly: a soft RIM LIGHT in the source's colour along the
/// long platform's front and back edges (continuous across the measures, closed at both ends — never over a platform), fading in as the walk
/// starts and out after it ends, and a soft GLOW travelling on the platform under every walking cube (it rides each step with the cube). Nothing
/// flies, nothing arcs. At rest nothing shows (K's joined platform says "one grid"). Replaces v7's FlowRibbon (the carpet, the walkways over gaps,
/// the unroll spell). Built, re-bound and stepped by <see cref="WorldMagic"/> (keyed by the source's island index).
/// </summary>
public class LongGridGlow : MonoBehaviour
{
    /// <summary>The rim light's width outside a platform's edge, its gap from the edge, the glow disc's size under a cube, the fades (s).</summary>
    public const float Rim = 0.34f, EdgeGap = 0.03f, GlowSize = 1.7f, FadeIn = 0.35f, FadeOut = 0.5f;
    const float StationStep = 0.5f;

    public KeyBlock Source { get; private set; }
    public int SourceIndex { get; private set; }
    public bool Retiring { get; private set; }
    /// <summary>The long grid's members after the source (K's run, else SongManager.CarryTargets).</summary>
    public int TargetCount => targets.Count;
    public KeyBlock Target(int k) => k >= 0 && k < targets.Count ? targets[k] : null;
    /// <summary>Measures of the long grid: the source + the grids it covers.</summary>
    public int Segments => targets.Count + 1;
    /// <summary>0 = dark .. 1 = lit (the walk plays).</summary>
    public float Show => show;
    /// <summary>True while the song plays inside the long grid's span (its cubes walk).</summary>
    public bool Walking { get; private set; }
    /// <summary>Glow discs shown now (one per walking cube) and the largest xz distance between a disc and its cube this frame (tests).</summary>
    public int Glows { get; private set; }
    public float GlowLag { get; private set; }
    /// <summary>Tests: the rim's vertices that lie over a member's platform footprint (must be 0); walks lit since Play.</summary>
    public int OverPlatform { get; private set; }
    public int Walks;
    public Color SpellColor { get; private set; } = Color.white;

    readonly List<KeyBlock> targets = new List<KeyBlock>();
    Transform rimT;
    Mesh rimMesh;
    Material rimMat, glowMat;
    readonly List<Transform> glows = new List<Transform>();
    readonly List<MeshRenderer> glowRends = new List<MeshRenderer>();
    struct Member { public float x0, x1, zf, zb, y, start, end; }
    readonly List<Member> members = new List<Member>();
    readonly List<Vector3> sv = new List<Vector3>(); readonly List<Vector2> suv = new List<Vector2>(); readonly List<int> stri = new List<int>();
    float show, retireT;
    MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    // ------------------------------------------------------------------ binding
    public void Bind(KeyBlock kb, int index)
    {
        Source = kb; SourceIndex = index;
        if (kb == null) return;
        SpellColor = kb.chordColor;
        if (rimT == null) Build();
        rimMat.SetColor(ColorId, Color.Lerp(SpellColor, Color.white, 0.3f));
    }

    public void SetTargets(List<KeyBlock> list)
    {
        targets.Clear();
        if (list != null) foreach (var t in list) if (t != null && t != Source) targets.Add(t);
    }

    void Build()
    {
        mpb = new MaterialPropertyBlock();
        rimMat = Fx.Additive(MagicTextures.Soft, Color.white, 0f);
        glowMat = Fx.Additive(IconFactory.GetTexture("glowSoft"), Color.white, 0.8f);
        var go = new GameObject("RimLight");
        go.transform.SetParent(transform, false);
        rimMesh = new Mesh { name = "longGridRim" }; rimMesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = rimMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = rimMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        rimT = go.transform;
        go.SetActive(false);
    }

    /// <summary>The grid no longer extends (or is gone): the light fades, then the look removes itself.</summary>
    public void Retire() { if (Retiring) return; Retiring = true; retireT = 0f; }

    // ------------------------------------------------------------------ frame
    public void Step(float dt, bool world, double beat, bool playing)
    {
        if (Source == null && !Retiring) Retire();
        var sm = SongManager.I;
        members.Clear();
        if (Source != null && sm != null) { AddMember(Source, sm); foreach (var t in targets) AddMember(t, sm); }
        bool walk = false;
        if (!Retiring && world && playing && members.Count > 0)
        {
            float b = (float)beat;
            walk = b >= members[0].start && b < members[members.Count - 1].end;
        }
        if (walk && !Walking) Walks++;
        Walking = walk;
        show = Mathf.MoveTowards(show, walk ? 1f : 0f, dt / (walk ? FadeIn : FadeOut));
        if (Retiring) { retireT += dt; if (show <= 0.001f || retireT > 2f) { Destroy(gameObject); return; } }
        bool on = world && show > 0.002f && members.Count > 0;
        if (rimT.gameObject.activeSelf != on) rimT.gameObject.SetActive(on);
        if (!on) { HideGlows(0); Glows = 0; return; }
        BuildRim();
        rimMat.SetFloat(IntensityId, 0.75f * Ease.InOutCubic(show));
        PoseGlows();
    }

    void AddMember(KeyBlock kb, SongManager sm)
    {
        if (kb == null) return;
        var b = kb.VisualBounds;
        float st = sm.ColumnStart(kb.column);
        // a member's platform may reach further back than its grid (K: a triad joined to a seventh chord takes the run's depth): hug its platform
        float zb = Mathf.Max(b.max.z, kb.PlatformBackEdge + kb.VisualOffset.z);
        members.Add(new Member { x0 = b.min.x, x1 = b.max.x, zf = b.min.z, zb = zb, y = kb.transform.position.y - 0.03f + 0.014f, start = st, end = st + sm.ColumnLength(kb.column) });
    }

    /// <summary>The rim: soft light strips along the front and back of every measure (continuous across touching measures) and the two end caps.</summary>
    void BuildRim()
    {
        sv.Clear(); suv.Clear(); stri.Clear(); OverPlatform = 0;
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            bool touchPrev = k > 0 && m.x0 - members[k - 1].x1 <= 0.15f;
            bool touchNext = k + 1 < members.Count && members[k + 1].x0 - m.x1 <= 0.15f;
            float xa = touchPrev ? m.x0 : m.x0 - Rim, xb = touchNext ? m.x1 : m.x1 + Rim;
            Strip(xa, xb, m.zf - Rim, m.zf - EdgeGap, m.y, true);
            Strip(xa, xb, m.zb + EdgeGap, m.zb + Rim, m.y, false);
            if (!touchPrev) StripZ(m.x0 - Rim, m.x0 - EdgeGap, m.zf - EdgeGap, m.zb + EdgeGap, m.y, true);
            if (!touchNext) StripZ(m.x1 + EdgeGap, m.x1 + Rim, m.zf - EdgeGap, m.zb + EdgeGap, m.y, false);
        }
        for (int i = 0; i < sv.Count; i++)
        {
            var p = sv[i];
            foreach (var m in members) if (p.x > m.x0 + 0.02f && p.x < m.x1 - 0.02f && p.z > m.zf + 0.02f && p.z < m.zb - 0.02f) { OverPlatform++; break; }
        }
        rimMesh.Clear();
        rimMesh.SetVertices(sv); rimMesh.SetUVs(0, suv); rimMesh.SetTriangles(stri, 0);
        rimMesh.RecalculateBounds();
    }

    /// <summary>A strip along x (the glow brightest at the platform's edge: <paramref name="innerAtZ1"/> = the edge is at z1).</summary>
    void Strip(float x0, float x1, float z0, float z1, float y, bool innerAtZ1)
    {
        if (x1 - x0 < 0.01f) return;
        int n = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / StationStep));
        int b = sv.Count;
        for (int i = 0; i <= n; i++)
        {
            float x = Mathf.Lerp(x0, x1, i / (float)n);
            sv.Add(new Vector3(x, y, z0)); sv.Add(new Vector3(x, y, z1));
            // the Soft texture peaks at v = 0.5: v runs 0 (outside) → 0.6 (the edge) so the light hugs the platform
            suv.Add(new Vector2(0f, innerAtZ1 ? 0f : 0.6f)); suv.Add(new Vector2(0f, innerAtZ1 ? 0.6f : 0f));
        }
        for (int i = 0; i < n; i++) { int a = b + i * 2; stri.Add(a); stri.Add(a + 2); stri.Add(a + 1); stri.Add(a + 1); stri.Add(a + 2); stri.Add(a + 3); }
    }

    /// <summary>An end cap (a strip along z between x0 and x1; the edge at x1 when <paramref name="innerAtX1"/>).</summary>
    void StripZ(float x0, float x1, float z0, float z1, float y, bool innerAtX1)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / StationStep));
        int b = sv.Count;
        for (int i = 0; i <= n; i++)
        {
            float z = Mathf.Lerp(z0, z1, i / (float)n);
            sv.Add(new Vector3(x0, y, z)); sv.Add(new Vector3(x1, y, z));
            suv.Add(new Vector2(0f, innerAtX1 ? 0f : 0.6f)); suv.Add(new Vector2(0f, innerAtX1 ? 0.6f : 0f));
        }
        for (int i = 0; i < n; i++) { int a = b + i * 2; stri.Add(a); stri.Add(a + 2); stri.Add(a + 1); stri.Add(a + 1); stri.Add(a + 2); stri.Add(a + 3); }
    }

    /// <summary>A soft glow on the platform under every walking cube of the source (it rides each step with the cube).</summary>
    void PoseGlows()
    {
        int g = 0; float lag = 0f;
        foreach (var c in SequenceMaster.Cubes)
        {
            // the walking cubes: the cubes of every extended grid of the run (the source, and a covered grid that extends further itself)
            if (c == null || c.IsOnMoon || c.nodes.Count == 0 || c.IsLayered || !c.BodyVisible) continue;
            var home = c.Island;
            if (home == null || home.carry <= 0 || (home != Source && !targets.Contains(home))) continue;
            Vector3 p = c.transform.position;
            // the platform under it: the member whose x range holds it (its top)
            float y = members[0].y; bool inside = false;
            foreach (var m in members) if (p.x >= m.x0 - 0.2f && p.x <= m.x1 + 0.2f) { y = m.y; inside = true; break; }
            if (!inside) continue;
            if (g >= glows.Count) NewGlow();
            var t = glows[g]; var r = glowRends[g]; g++;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.position = new Vector3(p.x, y + 0.006f, p.z);
            float s = GlowSize * (0.9f + 0.1f * Mathf.Exp(-GlobalClock.BeatPhase * 5f));
            t.localScale = new Vector3(s, 1f, s);
            mpb.Clear(); mpb.SetColor(ColorId, Color.Lerp(c.Color, Color.white, 0.25f)); mpb.SetFloat(IntensityId, 0.55f * Ease.InOutCubic(show));
            r.SetPropertyBlock(mpb);
            lag = Mathf.Max(lag, new Vector2(t.position.x - p.x, t.position.z - p.z).magnitude);
        }
        Glows = g; GlowLag = lag;
        HideGlows(g);
    }

    void HideGlows(int from) { for (int i = from; i < glows.Count; i++) if (glows[i].gameObject.activeSelf) glows[i].gameObject.SetActive(false); }

    void NewGlow()
    {
        var go = new GameObject("WalkGlow");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = glowMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        glows.Add(go.transform); glowRends.Add(mr);
    }

    void OnDestroy()
    {
        if (rimMat != null) Destroy(rimMat);
        if (glowMat != null) Destroy(glowMat);
        if (rimMesh != null) Destroy(rimMesh);
    }
}
