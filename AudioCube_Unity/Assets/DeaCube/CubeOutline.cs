using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPEC v3 §2.1: the hover outline (v4: a ROUNDED wire box matching the cube's rounded corners). One shared wire box (12 edges + 24 corner arcs, each segment a thin camera-facing quad drawn by the
/// DeaCube/MarchingEdge shader: marching dashes, bracket-bright corners, a soft pulse) retargeted to whichever cube the
/// pointer is near. It follows the cube's transform every frame (squash, hop, lean) at 1.22 x its size with its floor
/// just above the cube's floor (so the bottom edges are never buried in the tile), pops in from 1.45 x (OutBack, 0.18 s)
/// with a fade and fades out over 0.12 s. Colour = lerp(cube colour, white, 0.45). F6: shown again for the cube it just left
/// (within 0.3 s) it fades back without a second pop.
/// </summary>
public class CubeOutline : MonoBehaviour
{
    public const float Scale = 1.22f, PopScale = 1.45f, InSeconds = 0.18f, OutSeconds = 0.12f, Lift = 0.012f, RepopSeconds = 0.3f;

    static CubeOutline inst;
    static Mesh mesh;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    AudioCube target, last, left;
    MeshRenderer mr; Material mat;
    float inT = 9f, fade, leftT = -9f;
    /// <summary>F6: pop-ins so far (tests).</summary>
    public static int Pops { get; private set; }

    public static CubeOutline I
    {
        get
        {
            if (inst == null)
            {
                inst = FindAnyObjectByType<CubeOutline>();
                if (inst == null) inst = new GameObject("CubeOutline").AddComponent<CubeOutline>();
            }
            return inst;
        }
    }
    /// <summary>The cube the outline is (or is fading in) around; null when hidden or fading out.</summary>
    public static AudioCube Target => inst != null ? inst.target : null;
    /// <summary>True while any part of the outline is drawn (fading in, shown or fading out).</summary>
    public static bool Visible => inst != null && inst.mr != null && inst.mr.enabled;
    /// <summary>Current fade 0..1 (tests).</summary>
    public static float Fade => inst != null ? inst.fade : 0f;

    public static void Show(AudioCube c) { if (c == null) Hide(); else I.SetTarget(c); }
    public static void Hide() { if (inst != null) inst.SetTarget(null); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    void Awake()
    {
        inst = this;
        gameObject.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
        mr = gameObject.AddComponent<MeshRenderer>();
        var sh = Shader.Find("DeaCube/MarchingEdge");
        mat = sh != null ? new Material(sh) : Fx.Additive(IconFactory.GetTexture("white"));
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.enabled = false;
    }

    void OnDestroy() { if (mat != null) Destroy(mat); if (inst == this) inst = null; }

    void SetTarget(AudioCube c)
    {
        if (c == target) return;
        if (target != null) { left = target; leftT = Time.unscaledTime; }
        target = c;
        if (c == null) return;
        bool again = c == left && Time.unscaledTime - leftT < RepopSeconds;   // F6: the same cube back within 0.3 s: no second pop
        last = c;
        if (!again) { inT = 0f; Pops++; }   // (re)pop in around the new cube
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        if (target != null && (!target.isActiveAndEnabled || !target.BodyVisible)) target = null;
        bool on = target != null;
        fade = on ? Mathf.Min(1f, fade + dt / InSeconds) : Mathf.Max(0f, fade - dt / OutSeconds);
        var c = on ? target : last;
        if (c == null || fade <= 0f)
        {
            if (mr.enabled) mr.enabled = false;
            if (!on) last = null;
            return;
        }
        inT += dt;
        float k = Mathf.LerpUnclamped(PopScale, Scale, Ease.OutBack(Mathf.Clamp01(inT / InSeconds)));
        var ct = c.transform;
        Vector3 s = ct.lossyScale;
        transform.rotation = ct.rotation;
        transform.localScale = s * k;
        // grow around the cube but keep the box floor just above the cube's floor (the tile would bury the bottom edges)
        transform.position = ct.position + ct.rotation * new Vector3(0f, (k - 1f) * s.y * 0.5f + Lift, 0f);
        mat.SetColor(ColorId, Color.Lerp(c.Color, Color.white, 0.45f));
        mat.SetFloat(FadeId, Ease.OutCubic(fade));
        if (!mr.enabled) mr.enabled = true;
    }

    /// <summary>SPEC v4 (the user: "the outline should be a rounded cube corner too not a solid hard edge cube"): a unit ROUNDED wire box that
    /// matches the cube's rounded corners (MeshFactory.RoundedBox radius 0.14 grown by the outline's 1.22 scale): the 12 edges stop CornerR short
    /// of each corner and every corner is joined by three quarter arcs lying in the three faces that meet there (each face outline is a rounded
    /// rectangle). 4 vertices per segment: uv0 = (0 start / 1 end, -1 / +1 side), uv1 = the segment's other endpoint. The bottom and top loops run
    /// in opposite directions and the verticals rise, so the dashes still circulate; the short arc segments sit inside the shader's corner
    /// bracket, so every rounded corner reads as one bright curve.</summary>
    public const float CornerR = 0.2f;
    const int ArcSegments = 5;

    static Mesh BoxMesh()
    {
        if (mesh != null) return mesh;
        const float h = 0.5f, r = CornerR, e = h - r;
        var segs = new System.Collections.Generic.List<Vector3>();   // pairs (a, b)
        // straight edges: along x at (y, z) = (±h, ±h), along z at (x, y) = (±h, ±h), along y at (x, z) = (±h, ±h); directions as before
        void Edge(Vector3 a, Vector3 b) { segs.Add(a); segs.Add(b); }
        Edge(new Vector3(-e, -h, -h), new Vector3(e, -h, -h)); Edge(new Vector3(h, -h, -e), new Vector3(h, -h, e));     // bottom loop
        Edge(new Vector3(e, -h, h), new Vector3(-e, -h, h)); Edge(new Vector3(-h, -h, e), new Vector3(-h, -h, -e));
        Edge(new Vector3(-h, h, -e), new Vector3(-h, h, e)); Edge(new Vector3(-e, h, h), new Vector3(e, h, h));         // top loop (counter-rotating)
        Edge(new Vector3(h, h, e), new Vector3(h, h, -e)); Edge(new Vector3(e, h, -h), new Vector3(-e, h, -h));
        Edge(new Vector3(-h, -e, -h), new Vector3(-h, e, -h)); Edge(new Vector3(h, -e, -h), new Vector3(h, e, -h));     // verticals, rising
        Edge(new Vector3(h, -e, h), new Vector3(h, e, h)); Edge(new Vector3(-h, -e, h), new Vector3(-h, e, h));
        // corner arcs: for every corner and each of its three faces, a quarter circle of radius r in that face's plane
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    for (int face = 0; face < 3; face++)
                    {
                        // face 0: x = sx*h (in-plane axes y, z) · face 1: y = sy*h (x, z) · face 2: z = sz*h (x, y)
                        Vector3 c = new Vector3(sx * e, sy * e, sz * e);
                        Vector3 u, v;
                        if (face == 0) { c.x = sx * h; u = new Vector3(0f, sy, 0f); v = new Vector3(0f, 0f, sz); }
                        else if (face == 1) { c.y = sy * h; u = new Vector3(sx, 0f, 0f); v = new Vector3(0f, 0f, sz); }
                        else { c.z = sz * h; u = new Vector3(sx, 0f, 0f); v = new Vector3(0f, sy, 0f); }
                        for (int k = 0; k < ArcSegments; k++)
                        {
                            float a0 = k / (float)ArcSegments * Mathf.PI * 0.5f, a1 = (k + 1) / (float)ArcSegments * Mathf.PI * 0.5f;
                            segs.Add(c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r);
                            segs.Add(c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r);
                        }
                    }
        int n = segs.Count / 2;
        var verts = new Vector3[n * 4]; var uv0 = new Vector4[n * 4]; var uv1 = new Vector4[n * 4]; var tris = new int[n * 6];
        for (int s2 = 0; s2 < n; s2++)
        {
            Vector3 a = segs[s2 * 2], b = segs[s2 * 2 + 1];
            int vi = s2 * 4;
            verts[vi] = a; uv0[vi] = new Vector4(0f, -1f, 0f, 0f); uv1[vi] = b;
            verts[vi + 1] = a; uv0[vi + 1] = new Vector4(0f, 1f, 0f, 0f); uv1[vi + 1] = b;
            verts[vi + 2] = b; uv0[vi + 2] = new Vector4(1f, -1f, 0f, 0f); uv1[vi + 2] = a;
            verts[vi + 3] = b; uv0[vi + 3] = new Vector4(1f, 1f, 0f, 0f); uv1[vi + 3] = a;
            int t = s2 * 6;
            tris[t] = vi; tris[t + 1] = vi + 1; tris[t + 2] = vi + 2;
            tris[t + 3] = vi + 1; tris[t + 4] = vi + 3; tris[t + 5] = vi + 2;
        }
        mesh = new Mesh { name = "outline_rounded_box" };
        mesh.vertices = verts;
        mesh.SetUVs(0, new System.Collections.Generic.List<Vector4>(uv0));
        mesh.SetUVs(1, new System.Collections.Generic.List<Vector4>(uv1));
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.4f);   // the vertex shader widens the lines: never frustum-cull them early
        return mesh;
    }
}
