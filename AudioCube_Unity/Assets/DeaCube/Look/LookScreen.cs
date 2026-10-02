using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SPEC v3 §7 (package E): the printed page over every base camera — a faint paper-fibre texture whose offset jumps ~8 times per
/// second, and the impact frames (1-2 drawn frames of ink speed lines on downbeats / a ring of Ben-Day dots on accents, faint,
/// rate limited, never bright). One camera-aligned quad per base camera through Graphics.DrawMesh (shader DeaCube/InkScreen,
/// a 2x multiply: grey = no change), drawn before post so the grade applies; the HUD (screen-space overlay) is never touched.
/// Also pushes Look's global switches every frame. Created by Fx.
/// </summary>
[DefaultExecutionOrder(900)]
public class LookScreen : MonoBehaviour
{
    public static LookScreen I;
    Mesh quad; Material mat; MaterialPropertyBlock mpb;
    Camera[] cams = new Camera[16];
    readonly Dictionary<Camera, UniversalAdditionalCameraData> camData = new Dictionary<Camera, UniversalAdditionalCameraData>();
    Vector4 paperOffset; float paperClock;
    int impactMode; Vector3 impactWorld; float impactStrength, impactT = 9f, impactSeed, lastImpact = -9f;
    /// <summary>Impact frames shown since start (diagnostics).</summary>
    public int ImpactsShown { get; private set; }
    /// <summary>True while an impact frame is on screen.</summary>
    public bool ImpactVisible => impactT < ImpactSeconds;
    /// <summary>Quads drawn last frame (one per base camera).</summary>
    public int Drawn { get; private set; }

    /// <summary>Two drawn frames at 24 fps.</summary>
    public const float ImpactSeconds = 2f / 24f;
    const float PaperStrength = 0.34f, ImpactAlpha = 0.3f;

    static readonly int PaperTexId = Shader.PropertyToID("_PaperTex"), PaperId = Shader.PropertyToID("_Paper"), PaperOffId = Shader.PropertyToID("_PaperOffset");
    static readonly int ImpactId = Shader.PropertyToID("_Impact"), ImpactModeId = Shader.PropertyToID("_ImpactMode"), ImpactCenterId = Shader.PropertyToID("_ImpactCenter");
    static readonly int ImpactSeedId = Shader.PropertyToID("_ImpactSeed"), InkId = Shader.PropertyToID("_Ink");

    void Awake()
    {
        I = this;
        var sh = Shader.Find("DeaCube/InkScreen");
        if (sh != null)
        {
            mat = new Material(sh) { name = "InkScreen", hideFlags = HideFlags.DontSave };
            mat.SetTexture(PaperTexId, LookTextures.Paper);
            mat.SetColor(InkId, new Color(0.32f, 0.22f, 0.48f));
        }
        quad = new Mesh { name = "inkScreenQuad", hideFlags = HideFlags.DontSave };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        mpb = new MaterialPropertyBlock();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (mat != null) Destroy(mat);
        if (quad != null) Destroy(quad);
    }

    /// <summary>Queues an impact frame at <paramref name="world"/>: mode 0 = speed lines (downbeats), 1 = a Ben-Day ring (accents).
    /// Rate limited (0.3 s between frames; accents 0.6 s); a downbeat overrides an accent of the same frame.</summary>
    public static void Request(int mode, Vector3 world, float strength)
    {
        var s = I;
        if (s == null || !Look.ImpactFrames || strength <= 0f) return;
        float now = Time.unscaledTime;
        bool sameFrame = s.impactT < 0.001f;
        if (sameFrame) { if (mode == 0) { s.impactMode = 0; s.impactWorld = world; s.impactStrength = Mathf.Max(s.impactStrength, strength); } return; }
        float gap = mode == 0 ? 0.3f : 0.6f;
        if (now - s.lastImpact < gap) return;
        s.lastImpact = now; s.impactT = 0f; s.impactMode = mode; s.impactWorld = world; s.impactStrength = Mathf.Clamp01(strength);
        s.impactSeed = Random.value * 97f;
        s.ImpactsShown++;
    }

    void LateUpdate()
    {
        Look.PushGlobals();
        float dt = Time.unscaledDeltaTime;
        paperClock += dt;
        if (paperClock >= 0.125f) { paperClock = 0f; paperOffset = new Vector4(Random.Range(0f, 256f), Random.Range(0f, 256f), 0f, 0f); }
        Drawn = 0;
        if (mat == null) { impactT += dt; return; }
        if (Camera.allCamerasCount > cams.Length) cams = new Camera[Camera.allCamerasCount + 8];
        int n = Camera.GetAllCameras(cams);
        for (int i = 0; i < n; i++) Draw(cams[i]);
        impactT += dt;
    }

    void Draw(Camera cam)
    {
        if (cam == null || !cam.enabled || cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
        UniversalAdditionalCameraData d;
        if (!camData.TryGetValue(cam, out d) || d == null) { d = cam.GetUniversalAdditionalCameraData(); camData[cam] = d; }
        if (d != null && d.renderType != CameraRenderType.Base) return;
        int mask = cam.cullingMask;
        if (mask == 0) return;
        int layer = 0;
        while (((mask >> layer) & 1) == 0 && layer < 31) layer++;

        float dist = cam.nearClipPlane * 1.5f + 0.01f;
        float h = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        var t = cam.transform;
        var m = Matrix4x4.TRS(t.position + t.forward * dist, t.rotation, new Vector3(w * 1.004f, h * 1.004f, 1f));

        mpb.SetFloat(PaperId, Look.Paper ? PaperStrength : 0f);
        mpb.SetVector(PaperOffId, paperOffset);
        float imp = 0f;
        bool world = (mask & 1) != 0;   // impact frames only over the edit world (not the menu, not the presentation stage)
        if (world && impactT < ImpactSeconds && Look.ImpactFrames)
        {
            imp = ImpactAlpha * impactStrength * (impactT < ImpactSeconds * 0.5f ? 1f : 0.55f);
            Vector3 vp = cam.WorldToViewportPoint(impactWorld);
            if (vp.z <= 0f) vp = new Vector3(0.5f, 0.5f, 1f);
            mpb.SetVector(ImpactCenterId, new Vector4(vp.x, vp.y, 0f, 0f));
            mpb.SetFloat(ImpactModeId, impactMode);
            mpb.SetFloat(ImpactSeedId, impactSeed);
        }
        mpb.SetFloat(ImpactId, imp);
        Graphics.DrawMesh(quad, m, mat, layer, cam, 0, mpb, ShadowCastingMode.Off, false);
        Drawn++;
    }
}
