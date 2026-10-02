using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The user (v4): "when the cubes move ... add a bit of smearing, like handrawn organic smearing that has no outline", then "the smearing
/// doesnt look organic at all". A 2D PAINT BLOB behind a fast cube: a camera-facing quad at the cube's depth (just behind it) on which
/// DeaCube/Smear draws the signed distance to a chain of circles laid along the cube's REAL recent path (a quadratic Bezier through where
/// it is now and where it was at the last two drawings, so a hop smears along its arc): the head is half the cube's width, the circles
/// shrink to ~0.18 of it at a ROUND tail end, a smooth minimum fuses them into one gooey drop, two octaves of noise (re-rolled ON TWOS)
/// make the edge lumpy and soft-cornered, a couple of drybrush gaps open near the tail. Flat paint (the cube's colour a touch lighter),
/// a one-pixel anti-aliased edge, no outline, no shading, no glow. The drop is 1.2-1.8 cube widths long (a smear frame, not a comet tail),
/// longer the faster the cube moves.
/// Immediate mode (Graphics.RenderMesh of one shared quad, nothing to pool or hide, no garbage): <see cref="DrawBody"/> / <see cref="Draw"/>
/// draw this frame only. <see cref="Track"/> keeps a moving body's positions on the twos clock and draws while it is fast on screen (at
/// least <see cref="MinWidthsPerDrawing"/> of its own screen width per drawing, the camera's own motion excluded); gone one to two
/// drawings after the motion slows. <see cref="Cut"/> after a jump (a snap, a respawn), <see cref="Forget"/> when the owner is gone.
/// A colour with zero alpha draws nothing. (The class stays a MonoBehaviour TYPE only so older code that looked for its pooled objects
/// still compiles; nothing is instantiated.)
/// </summary>
public class CubeSmear : MonoBehaviour
{
    public const int Circles = 6;
    /// <summary>Track draws while the body moves at least this many of its own screen widths per drawing.</summary>
    public static float MinWidthsPerDrawing = 0.6f;
    /// <summary>A jump larger than this (world units) between two drawings is a teleport (a rebuild, a snap): no smear across it.</summary>
    public static float TeleportDistance = 2.6f;
    /// <summary>The paint is the colour lifted this much toward white.</summary>
    public static float PaintLighten = 0.12f;
    /// <summary>The drop's length from the head's centre to the tail's end, in cube widths: the path length clamped into this range.</summary>
    public static float MinLengthWidths = 1.2f, MaxLengthWidths = 1.8f;
    /// <summary>Head / tail circle radius as a fraction of the cube's width.</summary>
    public const float HeadRadius = 0.5f, TailRadius = 0.18f;

    class Entry
    {
        // the positions on the twos clock: [0] the latest drawing, [1] one before, [2] two before
        public readonly Vector3[] hist = new Vector3[3];
        public int histCount, tick = int.MinValue, drawnFrame = -9;
        public MaterialPropertyBlock mpb;
    }

    static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    static Material mat;
    static Mesh quad;
    static readonly Vector4[] circles = new Vector4[8];
    const int ArcSamples = 12;
    static readonly float[] arcLen = new float[ArcSamples + 1];
    static readonly Vector3[] arcPt = new Vector3[ArcSamples + 1];
    static readonly int ColorId = Shader.PropertyToID("_Color"), SeedId = Shader.PropertyToID("_Seed"), CountId = Shader.PropertyToID("_Count"),
        HeadRId = Shader.PropertyToID("_HeadR"), QuadId = Shader.PropertyToID("_Quad"), CirclesId = Shader.PropertyToID("_Circles");
    static int countFrame = -1, count;

    /// <summary>Smears drawn this frame (tests).</summary>
    public static int Visible => countFrame == Time.frameCount ? count : 0;
    /// <summary>Alias of <see cref="Visible"/>.</summary>
    public static int DrawnThisFrame => Visible;
    /// <summary>The last drop's length in cube widths (tests).</summary>
    public static float LastLengthWidths { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries.Clear(); mat = null; quad = null; countFrame = -1; count = 0; }

    static bool Ready()
    {
        if (mat == null)
        {
            var sh = Shader.Find("DeaCube/Smear");
            if (sh == null || !sh.isSupported) return false;
            mat = new Material(sh) { name = "CubeSmear" };
        }
        if (quad == null)
        {
            quad = new Mesh { name = "smear_quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
        }
        return true;
    }

    static Entry Get(int key)
    {
        Entry e;
        if (!entries.TryGetValue(key, out e))
        {
            e = new Entry { mpb = new MaterialPropertyBlock() };
            e.mpb.SetVectorArray(CirclesId, circles);   // fixes the array size once
            entries[key] = e;
        }
        return e;
    }

    /// <summary>The tracked body jumped (a snap home, a placement): forget its history so no smear spans the jump.</summary>
    public static void Cut(int key) { Entry e; if (entries.TryGetValue(key, out e)) e.histCount = 0; }

    /// <summary>Releases <paramref name="key"/> (its owner is gone).</summary>
    public static void Forget(int key) { entries.Remove(key); }

    /// <summary>
    /// Draws the smear of a body placed by <paramref name="model"/> (its width = the largest scale; <paramref name="mesh"/> is not needed by
    /// the blob and may be null) this frame: a paint drop along the Bezier through <paramref name="head"/> (the body now),
    /// <paramref name="mid"/> and <paramref name="tail"/> (where it was), in <paramref name="colour"/> (lightened a touch), on
    /// <paramref name="layer"/>. Call it every frame the smear should show.
    /// </summary>
    public static void DrawBody(int key, Mesh mesh, Matrix4x4 model, Vector3 head, Vector3 mid, Vector3 tail, Color colour, int layer = 0)
    {
        var s = model.lossyScale;
        DrawBlob(key, head, mid, tail, Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))), colour, layer);
    }

    /// <summary>Simple form (P's falling cubes): a drop <paramref name="width"/> wide at <paramref name="to"/> smeared straight back toward
    /// <paramref name="from"/>. Call it every frame the smear should show.</summary>
    public static void Draw(int key, Vector3 from, Vector3 to, float width, Color colour, int layer = 0)
    {
        DrawBlob(key, to, (to + from) * 0.5f, from, width, colour, layer);
    }

    static Vector3 Bez(Vector3 a, Vector3 c, Vector3 b, float u) { float v = 1f - u; return v * v * a + 2f * v * u * c + u * u * b; }

    static void DrawBlob(int key, Vector3 head, Vector3 mid, Vector3 tail, float width, Color colour, int layer)
    {
        if (width <= 0f || colour.a <= 0f || !Ready()) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 ctrl = 2f * mid - 0.5f * (head + tail);   // the Bezier passes through mid at u = 0.5
        // arc-length table of the path (head -> tail)
        arcPt[0] = head; arcLen[0] = 0f;
        for (int i = 1; i <= ArcSamples; i++) { arcPt[i] = Bez(head, ctrl, tail, i / (float)ArcSamples); arcLen[i] = arcLen[i - 1] + (arcPt[i] - arcPt[i - 1]).magnitude; }
        float L = arcLen[ArcSamples];
        if (L < width * 0.3f) return;
        float len = Mathf.Clamp(L, MinLengthWidths * width, MaxLengthWidths * width);
        LastLengthWidths = len / width;
        Vector3 tailDir = (arcPt[ArcSamples] - arcPt[ArcSamples - 1]);
        tailDir = tailDir.sqrMagnitude > 1e-8f ? tailDir.normalized : (tail - head).normalized;
        var ct = cam.transform;
        Vector3 right = ct.right, up = ct.up, fwd = ct.forward;
        float headR = HeadRadius * width;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < Circles; i++)
        {
            float f = i / (float)(Circles - 1);
            float sDist = len * f;
            Vector3 c;
            if (sDist >= L) c = arcPt[ArcSamples] + tailDir * (sDist - L);   // a short path: the drop runs on past the old position
            else
            {
                int j = 1; while (j < ArcSamples && arcLen[j] < sDist) j++;
                float seg = Mathf.Max(1e-6f, arcLen[j] - arcLen[j - 1]);
                c = Vector3.Lerp(arcPt[j - 1], arcPt[j], (sDist - arcLen[j - 1]) / seg);
            }
            float r = Mathf.Lerp(HeadRadius, TailRadius, Mathf.Pow(f, 0.8f)) * width;
            Vector3 d = c - head;
            float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
            circles[i] = new Vector4(x, y, r, f);
            minX = Mathf.Min(minX, x - r); maxX = Mathf.Max(maxX, x + r); minY = Mathf.Min(minY, y - r); maxY = Mathf.Max(maxY, y + r);
        }
        float margin = 0.4f * headR;
        float qx = (minX + maxX) * 0.5f, qy = (minY + maxY) * 0.5f;
        float qw = maxX - minX + 2f * margin, qh = maxY - minY + 2f * margin;
        for (int i = 0; i < Circles; i++) { circles[i].x -= qx; circles[i].y -= qy; }
        // at the cube's depth, pushed just behind its body (the real cube covers the head)
        Vector3 centre = head + right * qx + up * qy + fwd * (0.55f * width);
        var e = Get(key);
        int tick = Mathf.FloorToInt(Time.time * Look.TwosFps);
        var p = e.mpb;
        p.SetColor(ColorId, Color.Lerp(colour, Color.white, PaintLighten));
        p.SetFloat(SeedId, (key & 1023) * 0.37f + tick * 1.13f);
        p.SetFloat(CountId, Circles);
        p.SetFloat(HeadRId, headR);
        p.SetVector(QuadId, new Vector4(qw, qh, 0f, 0f));
        p.SetVectorArray(CirclesId, circles);
        var model = Matrix4x4.TRS(centre, ct.rotation, new Vector3(qw, qh, 1f));
        var b = new Bounds(centre, new Vector3(qw, qh, Mathf.Max(qw, qh)));
        var rp = new RenderParams(mat) { layer = layer, matProps = p, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = b };
        Graphics.RenderMesh(rp, quad, 0, model);
        e.drawnFrame = Time.frameCount;
        if (countFrame != Time.frameCount) { countFrame = Time.frameCount; count = 0; }
        count++;
    }

    /// <summary>
    /// Tracks a moving body (a cube) under <paramref name="key"/> and draws its smear while it is fast: the drop follows its positions at
    /// the last two drawings (kept on the twos clock; the head follows the body every frame). <paramref name="mesh"/> is accepted for the
    /// API and not needed by the blob. Returns true when a smear was drawn this frame.
    /// </summary>
    public static bool Track(int key, Transform body, Mesh mesh, Color colour, int layer = 0)
    {
        if (body == null) return false;
        var s = body.lossyScale;
        return TrackCore(key, body.position, Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))), colour, layer);
    }

    /// <summary>Tracks a moving point with a drop <paramref name="width"/> wide (see the transform overload).</summary>
    public static bool Track(int key, Vector3 position, float width, Color colour, int layer = 0) => TrackCore(key, position, width, colour, layer);

    static bool TrackCore(int key, Vector3 position, float width, Color colour, int layer)
    {
        var cam = Camera.main;
        if (cam == null || width <= 0f) return false;
        var e = Get(key);
        int tick = Mathf.FloorToInt(Time.time * Look.TwosFps);
        if (tick != e.tick)
        {
            if (e.tick != int.MinValue && tick - e.tick > 3) e.histCount = 0;   // not tracked for a while (hidden, paused): stale history
            if (e.histCount > 0 && (position - e.hist[0]).sqrMagnitude > TeleportDistance * TeleportDistance) e.histCount = 0;   // a teleport
            e.hist[2] = e.hist[1]; e.hist[1] = e.hist[0]; e.hist[0] = position;
            e.histCount = Mathf.Min(3, e.histCount + 1);
            e.tick = tick;
        }
        if (e.histCount < 2) return false;
        Vector3 tail = e.hist[1], mid = e.hist[0];                              // the path of the last one to two drawings
        float d2 = (position - tail).sqrMagnitude;
        if (d2 < width * width * 0.36f) return false;                           // barely moved (idle, a crouch): no projections needed
        if (d2 > TeleportDistance * TeleportDistance * 4f) { e.histCount = 0; return false; }
        // fast on screen? both points through the CURRENT camera: the camera's own motion is not the cube's
        Vector3 a = cam.WorldToScreenPoint(tail), b = cam.WorldToScreenPoint(position);
        if (a.z <= 0f || b.z <= 0f) return false;
        Vector3 c = cam.WorldToScreenPoint(position + cam.transform.right * width);
        float px = Mathf.Max(1f, ((Vector2)(c - b)).magnitude);
        if (((Vector2)(b - a)).magnitude < MinWidthsPerDrawing * px) return false;
        DrawBlob(key, position, mid, tail, width, colour, layer);
        return true;
    }
}
