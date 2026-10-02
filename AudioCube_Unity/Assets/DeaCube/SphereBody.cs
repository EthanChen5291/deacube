using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPHERE pieces (the user, 2026-10-01: "when choosing, you can choose between sphere and cube. spheres can bounce and don't have to travel to
/// direct neighbours. that's it. it should also roll as it's bouncing"). A sphere is an AudioCube with <see cref="sphere"/> set: the same notes,
/// windows, stickers and edits — only three things differ:
///   • drawing: a click on a far tile adds JUST that tile (a leap), never the straight / diagonal line of tiles to it (PathManager.ExtendTo,
///     PathGridView.ExtendToCell); the draft preview draws the bounce as an arc;
///   • motion: a hop is a ballistic BOUNCE — constant ground speed, a parabola whose height and air time grow with the distance
///     (<see cref="SphereArc"/>, <see cref="SphereHopStart"/>), a bigger squash on landing and a stretch in the air;
///   • look: a ball (smooth shading: _FaceSnap 0) with two crossed ink seams that ROLL with every unit it travels (rotation = distance ÷ radius
///     about the axis across its motion, <see cref="SpherePose"/>) — on bounces, trips home, rewinds alike. The seams also set it apart from the
///     keyboard's floating sphere hands (KeyHands).
/// The roll lives on the seam child; the body itself stays upright so the squash / stretch is always vertical.
/// </summary>
public partial class AudioCube
{
    /// <summary>A sphere piece (CubeState.sphere): leaps instead of filling lines, bounces and rolls.</summary>
    public bool sphere;
    /// <summary>Tests: degrees the seams have rolled in total (grows with the distance travelled).</summary>
    public float RolledDegrees { get; private set; }
    /// <summary>Tests: the bounce height (world units above the straight line) of the current / last sphere hop.</summary>
    public float LastBounceHeight { get; private set; }

    /// <summary>A sphere's bounce is this × cubeHopIntensity for a neighbour and grows by <see cref="BouncePerTile"/> per extra tile (capped).</summary>
    public const float BounceBase = 1.15f, BouncePerTile = 0.4f, BounceMax = 2.6f;
    /// <summary>The landing squash of a sphere is this × a cube's.</summary>
    public const float SphereSquash = 1.35f;

    Transform seam; MeshRenderer seamR; Material seamMat; Color seamShown = new Color(-1f, 0f, 0f, 0f);
    Quaternion roll = Quaternion.identity; Vector3 rollLast; bool rollHas; float faceSnapCube = -1f; float airStretch = 1f;

    static Mesh ballMesh, seamMesh;
    /// <summary>The unit ball (diameter 1, like the unit rounded box) every sphere piece and its ghosts share.</summary>
    public static Mesh BallMesh { get { if (ballMesh == null) ballMesh = BuildBall(40, 24); return ballMesh; } }
    static Mesh SeamMesh { get { if (seamMesh == null) seamMesh = BuildSeams(0.49f, 0.042f, 56, 8); return seamMesh; } }

    /// <summary>The body mesh for a piece of this shape (the hand hologram, the inspector proxy).</summary>
    public static Mesh BodyMeshFor(bool round) => round ? BallMesh : MeshFactory.RoundedBox(Vector3.one, 0.14f, 4);

    /// <summary>Turns this piece into a sphere / back into a cube: mesh, shading, seams, the ghosts that copy the body.</summary>
    public void SetShape(bool round)
    {
        bool changed = sphere != round;
        sphere = round;
        var mf = GetComponent<MeshFilter>();
        Mesh m = BodyMeshFor(round);
        if (mf != null && mf.sharedMesh != m) mf.sharedMesh = m;
        bodyMesh = m;
        if (mat != null && mat.HasProperty("_FaceSnap"))
        {
            if (faceSnapCube < 0f) faceSnapCube = mat.GetFloat("_FaceSnap");
            mat.SetFloat("_FaceSnap", round ? 0f : faceSnapCube);
        }
        for (int i = 0; i < holoGhost.Length; i++) if (holoGhost[i] != null) { var g = holoGhost[i].GetComponent<MeshFilter>(); if (g != null) g.sharedMesh = m; }
        for (int i = 0; i < ghosts.Length; i++) if (ghosts[i] != null) { var g = ghosts[i].GetComponent<MeshFilter>(); if (g != null) g.sharedMesh = m; }
        if (round && seam == null) BuildSeam();
        if (seam != null) seam.gameObject.SetActive(round);
        if (round && changed)
        {
            // each ball starts in its own (seeded) orientation, so a row of them never looks stamped
            int s = Mathf.Abs(GetInstanceID());
            roll = Quaternion.Euler(18f + s % 37, (s / 37) % 360, 0f);
            rollHas = false;
        }
        if (!round) { transform.rotation = Quaternion.identity; airStretch = 1f; }
        if (changed) RefreshSeam();
    }

    void BuildSeam()
    {
        var go = new GameObject("Seams");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = SeamMesh;
        seamR = go.AddComponent<MeshRenderer>();
        seamMat = Fx.Lit(Color.white, 0.35f, 0f, false);
        if (seamMat.HasProperty("_FaceSnap")) seamMat.SetFloat("_FaceSnap", 0f);
        seamR.sharedMaterial = seamMat;
        seamR.shadowCastingMode = ShadowCastingMode.Off; seamR.receiveShadows = true;
        seam = go.transform;
    }

    void RefreshSeam()
    {
        if (seam == null) return;
        // the seams show on the solid body only (a draft hologram, a layer's glass and a hidden cube carry none)
        bool on = sphere && rend != null && rend.enabled && mat != null && rend.sharedMaterial == mat;
        if (seamR.enabled != on) seamR.enabled = on;
        Color want = Color.Lerp(Color, new Color(0.106f, 0.078f, 0.165f), 0.78f);   // ink seams (a basketball's): they read on every pastel
        if (want != seamShown) { seamShown = want; seamMat.SetColor("_BaseColor", want); }
        seam.localRotation = roll;
    }

    /// <summary>Grid distance between two tiles (same island: cells; else world distance in tile pitches).</summary>
    public static float GridGap(TileInteraction a, TileInteraction b)
    {
        if (a == null || b == null) return 1f;
        if (a.island == b.island)
        {
            float dx = a.gridX - b.gridX, dz = a.gridZ - b.gridZ;
            return Mathf.Max(1f, Mathf.Sqrt(dx * dx + dz * dz));
        }
        Vector3 d = b.Top - a.Top; d.y = 0f;
        return Mathf.Max(1f, d.magnitude / ProjectConfig.Spacing);
    }

    /// <summary>Where a sphere's hop starts inside step k (0..1): the air time grows with the leap (a neighbour ≈ a cube's hop, a far tile up to
    /// twice that), and the ball always sits at least the first quarter of its note.</summary>
    float SphereHopStart(float hs, TileInteraction ta, TileInteraction tb, float lenBeats)
    {
        float g = GridGap(ta, tb);
        if (lenBeats > 0f)
        {
            float air = ProjectConfig.HopMaxBeats * Mathf.Min(2f, 0.75f + 0.25f * g);
            return Mathf.Max(0.25f, 1f - air / Mathf.Max(1e-4f, lenBeats));
        }
        return Mathf.Clamp(hs - 0.06f * (g - 1f), 0.3f, hs);
    }

    /// <summary>The bounce: a parabola over the hop (0..1) whose height grows with the leap.</summary>
    float SphereArc(TileInteraction ta, TileInteraction tb, float f)
    {
        float h = BounceHeight(GridGap(ta, tb));
        LastBounceHeight = h;
        return 4f * f * (1f - f) * h;
    }

    /// <summary>A sphere's bounce height (world units) over a leap of <paramref name="gap"/> cells (the draft preview's arc uses it too).</summary>
    public static float BounceHeight(float gap) => ProjectConfig.cubeHopIntensity * Mathf.Min(BounceMax, BounceBase + BouncePerTile * (Mathf.Max(1f, gap) - 1f));

    /// <summary>After every pose of the frame: the ball rolls by the ground distance it moved (any motion: bounce, trip, rewind, drag), stays
    /// upright, and stretches a little along a fast vertical motion.</summary>
    void SpherePose(float dt)
    {
        if (!sphere) return;
        Vector3 p = transform.position;
        float vy = 0f;
        if (rollHas)
        {
            Vector3 d = p - rollLast; vy = d.y; d.y = 0f;
            float dist = d.magnitude;
            float r = 0.5f * Mathf.Max(0.05f, transform.lossyScale.x);
            if (dist > 1e-5f && dist < 3f)   // (a teleport — a rebuild, a snap home — does not spin it)
            {
                float deg = dist / r * Mathf.Rad2Deg;
                roll = Quaternion.AngleAxis(deg, Vector3.Cross(Vector3.up, d / dist)) * roll;
                RolledDegrees += deg;
            }
        }
        rollLast = p; rollHas = true;
        transform.rotation = Quaternion.identity;
        float speed = dt > 1e-4f ? Mathf.Abs(vy) / dt : 0f;
        float want = hopping ? 1f + Mathf.Clamp(speed * 0.045f, 0f, 0.16f) : 1f;
        airStretch = Mathf.Lerp(airStretch, want, 1f - Mathf.Exp(-dt * 30f));
        if (airStretch > 1.001f)
        {
            float k = 1f / Mathf.Sqrt(airStretch);
            var s = transform.localScale;
            transform.localScale = new Vector3(s.x * k, s.y * airStretch, s.z * k);
        }
        RefreshSeam();
    }

    // ------------------------------------------------------------------ meshes
    static Mesh BuildBall(int lon, int lat)
    {
        var v = new List<Vector3>((lon + 1) * (lat + 1)); var n = new List<Vector3>(v.Capacity); var uv = new List<Vector2>(v.Capacity);
        var t = new List<int>(lon * lat * 6);
        for (int j = 0; j <= lat; j++)
        {
            float th = j / (float)lat * Mathf.PI;
            for (int i = 0; i <= lon; i++)
            {
                float ph = i / (float)lon * Mathf.PI * 2f;
                var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(u * 0.5f); n.Add(u); uv.Add(new Vector2(i / (float)lon, 1f - j / (float)lat));
            }
        }
        for (int j = 0; j < lat; j++)
            for (int i = 0; i < lon; i++)
            {
                int a = j * (lon + 1) + i, b = a + lon + 1;
                t.Add(a); t.Add(a + 1); t.Add(b);
                t.Add(a + 1); t.Add(b + 1); t.Add(b);
            }
        var m = new Mesh { name = "SphereBall" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        return m;
    }

    /// <summary>Two crossed great-circle tubes (a beach-ball seam): one in the XY plane, one in YZ — rolling about any axis turns at least one.</summary>
    static Mesh BuildSeams(float R, float r, int seg, int sides)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int ring = 0; ring < 2; ring++)
        {
            int b0 = v.Count;
            Vector3 ax = ring == 0 ? Vector3.forward : Vector3.left;   // (left, not right: keeps both rings wound clockwise from outside)
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg * Mathf.PI * 2f;
                Vector3 c = ring == 0 ? new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f) : new Vector3(0f, Mathf.Sin(u), Mathf.Cos(u));
                for (int k = 0; k <= sides; k++)
                {
                    float w = k / (float)sides * Mathf.PI * 2f;
                    Vector3 nn = c * Mathf.Cos(w) + ax * Mathf.Sin(w);
                    v.Add(c * R + nn * r); n.Add(nn);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = b0 + i * (sides + 1) + k, b = a + sides + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
        }
        var m = new Mesh { name = "SphereSeams" };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
}
