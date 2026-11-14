using UnityEngine;

/// <summary>
/// The Shift marquee as an object of the 3D world (the user, 2026-11-13: "when highlighting with mouse it shouldnt be 2d square highlight it should be
/// 3d square so that u can choose on the 3d plane and select easier"). The rectangle lies ON THE GROUND (world X / Z, the axes the grids are laid
/// out on), so under any camera angle its edges run along the columns and the lanes and it covers exactly the grids it will select; a screen
/// rectangle only did that from straight above. Look: a low glass box standing on the sea — a faint paper wash on its top and walls, a rim of
/// marching dashes (stepped on twos like every ink line of the game) along the top and the foot, a post at each corner. One pooled object, built
/// on the first Shift + drag; PathManager shows / hides it (<see cref="Show"/> / <see cref="Hide"/>) and owns what it selects.
/// </summary>
public class Marquee3D : MonoBehaviour
{
    /// <summary>The box's height above its foot, its foot's lift above the ground plane, the rim's width and one dash + gap (world units).</summary>
    public const float Height = 1.5f, Foot = 0.05f, Rim = 0.22f, DashPeriod = 1.1f;

    static Marquee3D inst;
    /// <summary>The marquee is on screen.</summary>
    public static bool Shown => inst != null && inst.gameObject.activeSelf;
    /// <summary>The ground rectangle shown last (x = world X, y = world Z).</summary>
    public static Rect ShownRect => inst != null ? inst.rect : default(Rect);
    /// <summary>Marquee meshes rebuilt so far (tests).</summary>
    public static int Builds;

    Rect rect; float baseY;
    Mesh washMesh, rimMesh;
    Material washMat, rimMat;
    readonly Vector3[] wv = new Vector3[20]; readonly Vector2[] wu = new Vector2[20]; readonly int[] wt = new int[60];
    Vector3[] rv; Vector2[] ru; int[] rt; int rn, rti;

    /// <summary>Shows the marquee over <paramref name="ground"/> (world X / Z) standing on height <paramref name="y"/>.</summary>
    public static void Show(Rect ground, float y)
    {
        if (inst == null)
        {
            var go = new GameObject("Marquee3D");
            inst = go.AddComponent<Marquee3D>();
            inst.Build();
        }
        if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
        if (ground != inst.rect || y != inst.baseY || Builds == 0) { inst.rect = ground; inst.baseY = y; inst.Rebuild(); }
    }

    public static void Hide() { if (inst != null && inst.gameObject.activeSelf) inst.gameObject.SetActive(false); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; Builds = 0; }

    void Build()
    {
        washMesh = new Mesh { name = "MarqueeWash" }; washMesh.MarkDynamic();
        rimMesh = new Mesh { name = "MarqueeRim" }; rimMesh.MarkDynamic();
        washMat = Fx.Alpha(IconFactory.GetTexture("white"), new Color(1f, 0.96f, 0.86f, 0.2f));
        washMat.renderQueue = 3030;
        rimMat = Fx.Alpha(StructureLook.DashTexture, new Color(1f, 0.97f, 0.9f, 0.96f));
        rimMat.renderQueue = 3031;
        Child("Wash", washMesh, washMat);
        Child("Rim", rimMesh, rimMat);
        rv = new Vector3[8 * 4 + 4 * 8]; ru = new Vector2[rv.Length]; rt = new int[(8 + 4 * 2) * 12];
    }

    void Child(string name, Mesh m, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }

    void Update()
    {
        // the dashes march along the rim, drawn on twos
        if (rimMat != null) rimMat.mainTextureOffset = new Vector2(-Look.Stepped(Time.unscaledTime) * 0.9f, 0f);
    }

    void Rebuild()
    {
        Builds++;
        float x0 = rect.xMin, x1 = rect.xMax, z0 = rect.yMin, z1 = rect.yMax, y0 = baseY + Foot, y1 = y0 + Height;
        // ---- the wash: the top face and the four walls (both windings: seen from inside and outside)
        int v = 0, t = 0;
        Quad(ref v, ref t, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
        Quad(ref v, ref t, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0));
        Quad(ref v, ref t, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0));
        Quad(ref v, ref t, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1));
        Quad(ref v, ref t, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1));
        washMesh.Clear();
        washMesh.vertices = wv; washMesh.uv = wu; washMesh.triangles = wt;
        washMesh.RecalculateBounds();

        // ---- the rim: flat ribbons along the four top edges and the four foot edges (dashed along their length), two crossed blades per corner post
        rn = 0; rti = 0;
        float h = Rim * 0.5f;
        for (int k = 0; k < 2; k++)
        {
            float y = k == 0 ? y1 : y0 + 0.01f, w = k == 0 ? h : h * 0.75f;
            Ribbon(new Vector3(x0, y, z0), new Vector3(x1, y, z0), Vector3.forward * w);
            Ribbon(new Vector3(x1, y, z0), new Vector3(x1, y, z1), Vector3.left * w);
            Ribbon(new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.back * w);
            Ribbon(new Vector3(x0, y, z1), new Vector3(x0, y, z0), Vector3.right * w);
        }
        for (int c = 0; c < 4; c++)
        {
            var p = new Vector3((c == 1 || c == 2) ? x1 : x0, y0, c >= 2 ? z1 : z0);
            Post(p, Vector3.right * h); Post(p, Vector3.forward * h);
        }
        rimMesh.Clear();
        rimMesh.vertices = rv; rimMesh.uv = ru; rimMesh.triangles = rt;
        rimMesh.RecalculateBounds();
    }

    void Quad(ref int v, ref int t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        wv[v] = a; wv[v + 1] = b; wv[v + 2] = c; wv[v + 3] = d;
        wu[v] = wu[v + 1] = wu[v + 2] = wu[v + 3] = new Vector2(0.5f, 0.5f);
        wt[t] = v; wt[t + 1] = v + 2; wt[t + 2] = v + 1; wt[t + 3] = v; wt[t + 4] = v + 3; wt[t + 5] = v + 2;
        wt[t + 6] = v; wt[t + 7] = v + 1; wt[t + 8] = v + 2; wt[t + 9] = v; wt[t + 10] = v + 2; wt[t + 11] = v + 3;
        v += 4; t += 12;
    }

    /// <summary>A flat ribbon from a to b, half-width vector <paramref name="side"/>; u runs along it in dash periods.</summary>
    void Ribbon(Vector3 a, Vector3 b, Vector3 side)
    {
        float len = (b - a).magnitude / DashPeriod;
        RimQuad(a - side, a + side, b + side, b - side, 0f, len);
    }

    /// <summary>A corner post: an upright blade (solid ink: its u stays inside the dash).</summary>
    void Post(Vector3 foot, Vector3 side)
    {
        Vector3 top = foot + Vector3.up * Height;
        RimQuad(foot - side, foot + side, top + side, top - side, 0.2f, 0.2f);
    }

    void RimQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0, float u1)
    {
        int v = rn;
        rv[v] = a; rv[v + 1] = b; rv[v + 2] = c; rv[v + 3] = d;
        ru[v] = new Vector2(u0, 0f); ru[v + 1] = new Vector2(u0, 1f); ru[v + 2] = new Vector2(u1, 1f); ru[v + 3] = new Vector2(u1, 0f);
        int t = rti;
        rt[t] = v; rt[t + 1] = v + 2; rt[t + 2] = v + 1; rt[t + 3] = v; rt[t + 4] = v + 3; rt[t + 5] = v + 2;
        rt[t + 6] = v; rt[t + 7] = v + 1; rt[t + 8] = v + 2; rt[t + 9] = v; rt[t + 10] = v + 2; rt[t + 11] = v + 3;
        rn += 4; rti += 12;
    }
}
