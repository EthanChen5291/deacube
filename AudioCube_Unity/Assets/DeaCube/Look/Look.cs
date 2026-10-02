using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SPEC v3 §7 (package E): the look — 3D that reads as 2D (Spider-Verse ink + print, Melatonin pastels), calm and readable.
/// Owns the world palette (pastel-dusk sky bands, sea, fog, ink, key light), the global switches the Toon / Ink shaders read,
/// the post-processing written into the Global Volume's runtime profile (Fx.SetupEnvironment calls <see cref="ConfigurePost"/>),
/// and the "on twos" clock for secondary motion (<see cref="Stepped(float)"/>).
/// </summary>
public static class Look
{
    // ------------------------------------------------------------------ on twos (Spider-Verse: 12 drawings per second)
    /// <summary>Global "on twos" switch for secondary motion (Fx effects, dust, the sea, the sky details).</summary>
    public static bool OnTwos = true;
    public const float TwosFps = 12f;
    /// <summary><paramref name="t"/> held on 1/12 s steps while <see cref="OnTwos"/> is set (unchanged otherwise). Use it for
    /// secondary motion only (never for anything the music is timed on).</summary>
    public static float Stepped(float t) => OnTwos ? Mathf.Floor(t * TwosFps) / TwosFps : t;
    /// <summary><paramref name="t"/> held on 1/<paramref name="fps"/> s steps (always, whatever <see cref="OnTwos"/> says).</summary>
    public static float Stepped(float t, float fps) => fps > 0f ? Mathf.Floor(t * fps) / fps : t;
    /// <summary>Time.time on twos.</summary>
    public static float Time12 => Stepped(Time.time);

    // ------------------------------------------------------------------ switches (the shaders read them as globals; 0 = on)
    public static bool Ink = true, Print = true, Boil = true, Misregistration = true, FlatGlows = true, Paper = true, ImpactFrames = true;

    // ------------------------------------------------------------------ palette (sRGB)
    /// <summary>Dark violet-black ink of every outline and comic effect.</summary>
    public static readonly Color InkColor = new Color(0.10f, 0.06f, 0.17f);
    public static readonly Color SkyGlow = new Color(1.00f, 0.84f, 0.69f);   // peach horizon glow
    public static readonly Color SkyLow = new Color(0.97f, 0.66f, 0.75f);    // pink
    public static readonly Color SkyMid = new Color(0.75f, 0.55f, 0.84f);    // orchid
    public static readonly Color SkyHigh = new Color(0.49f, 0.41f, 0.77f);   // periwinkle
    public static readonly Color SkyTop = new Color(0.24f, 0.20f, 0.53f);    // dusk indigo
    public static readonly Color SeaDeep = new Color(0.29f, 0.25f, 0.53f);
    public static readonly Color SeaSwell = new Color(0.34f, 0.29f, 0.60f);
    public static readonly Color SeaLine = new Color(0.58f, 0.50f, 0.86f);
    public static readonly Color SeaFar = new Color(0.69f, 0.54f, 0.78f);    // horizon haze = fog = the sky below the horizon
    public static readonly Color SeaShadow = new Color(0.20f, 0.15f, 0.40f);
    public static readonly Color Ambient = new Color(0.46f, 0.40f, 0.62f);
    public static readonly Color LightColor = new Color(1.00f, 0.95f, 0.87f);
    /// <summary>Key light: from the upper left and a little in front of the home view (top faces lit, fronts mid, right sides in shade).</summary>
    public static readonly Vector3 LightEuler = new Vector3(58f, 53f, 0f);
    /// <summary>Sky moon / sun direction (a flat pale disc low over the horizon, ahead of the home view).</summary>
    public static readonly Vector3 SunDir = new Vector3(-0.62f, 0.065f, 0.78f);
    public const float FogStart = 30f, FogEnd = 125f;

    // ------------------------------------------------------------------ post (Global Volume overrides)
    public const float BloomIntensity = 0.3f, BloomThreshold = 1.2f, BloomScatter = 0.55f;
    public const float Contrast = 12f, Saturation = 14f, Aberration = 0.1f, Grain = 0.12f, VignetteAmount = 0.26f;
    public static readonly Color SplitShadows = new Color(0.52f, 0.42f, 0.68f), SplitHighlights = new Color(0.64f, 0.53f, 0.43f);
    public static readonly Color VignetteColor = new Color(0.16f, 0.09f, 0.27f);

    static readonly int InkOffId = Shader.PropertyToID("_DeaInkOff");
    static readonly int PrintOffId = Shader.PropertyToID("_DeaPrintOff");
    static readonly int BoilOffId = Shader.PropertyToID("_DeaBoilOff");
    static readonly int MisregOffId = Shader.PropertyToID("_DeaMisregOff");
    static readonly int GlowOffId = Shader.PropertyToID("_DeaGlowOff");

    /// <summary>Writes the switches into the shader globals (every frame from LookScreen; cheap).</summary>
    public static void PushGlobals()
    {
        Shader.SetGlobalFloat(InkOffId, Ink ? 0f : 1f);
        Shader.SetGlobalFloat(PrintOffId, Print ? 0f : 1f);
        Shader.SetGlobalFloat(BoilOffId, Boil ? 0f : 1f);
        Shader.SetGlobalFloat(MisregOffId, Misregistration ? 0f : 1f);
        Shader.SetGlobalFloat(GlowOffId, FlatGlows ? 0f : 1f);
    }

    /// <summary>
    /// The comic post stack on a (runtime) profile: tonemapping None (flat colours stay exactly as painted), contrast and saturation
    /// up, split toning (violet shadows, warm highlights), a little chromatic aberration (print misregistration), fine film grain,
    /// a soft violet vignette and a minimal bloom (threshold up, intensity down: glows read as flat colour, not haze).
    /// </summary>
    public static Bloom ConfigurePost(VolumeProfile p)
    {
        if (p == null) return null;
        var tm = Get<Tonemapping>(p); tm.active = true; tm.mode.Override(TonemappingMode.None);
        var ca = Get<ColorAdjustments>(p); ca.active = true;
        ca.contrast.Override(Contrast); ca.saturation.Override(Saturation); ca.postExposure.Override(0f);
        var st = Get<SplitToning>(p); st.active = true;
        st.shadows.Override(SplitShadows); st.highlights.Override(SplitHighlights); st.balance.Override(-8f);
        var ab = Get<ChromaticAberration>(p); ab.active = true; ab.intensity.Override(Aberration);
        var fg = Get<FilmGrain>(p); fg.active = true;
        fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(Grain); fg.response.Override(0.8f);
        var vg = Get<Vignette>(p); vg.active = true;
        vg.intensity.Override(VignetteAmount); vg.smoothness.Override(0.5f); vg.color.Override(VignetteColor); vg.rounded.Override(false);
        var bl = Get<Bloom>(p); bl.active = true;
        bl.threshold.Override(BloomThreshold); bl.intensity.Override(BloomIntensity); bl.scatter.Override(BloomScatter);
        return bl;
    }

    static T Get<T>(VolumeProfile p) where T : VolumeComponent
    {
        T c;
        if (!p.TryGet(out c)) c = p.Add<T>(false);
        return c;
    }

    /// <summary>One line with the values the post stack actually carries (for checks and reports).</summary>
    public static string DescribePost(VolumeProfile p)
    {
        if (p == null) return "no profile";
        var sb = new System.Text.StringBuilder();
        Tonemapping tm; if (p.TryGet(out tm)) sb.Append("tonemap ").Append(tm.active && tm.mode.overrideState ? tm.mode.value.ToString() : "off").Append(", ");
        ColorAdjustments ca; if (p.TryGet(out ca)) sb.Append("contrast ").Append(ca.contrast.value.ToString("F0")).Append(" saturation ").Append(ca.saturation.value.ToString("F0")).Append(", ");
        SplitToning st; if (p.TryGet(out st)) sb.Append("split ").Append(Hex(st.shadows.value)).Append("/").Append(Hex(st.highlights.value)).Append(", ");
        ChromaticAberration ab; if (p.TryGet(out ab)) sb.Append("aberration ").Append(ab.intensity.value.ToString("F2")).Append(", ");
        FilmGrain fg; if (p.TryGet(out fg)) sb.Append("grain ").Append(fg.type.value).Append(" ").Append(fg.intensity.value.ToString("F2")).Append(", ");
        Vignette vg; if (p.TryGet(out vg)) sb.Append("vignette ").Append(vg.intensity.value.ToString("F2")).Append(", ");
        Bloom bl; if (p.TryGet(out bl)) sb.Append("bloom thr ").Append(bl.threshold.value.ToString("F2")).Append(" int ").Append(bl.intensity.value.ToString("F2"));
        return sb.ToString();
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    /// <summary>Anti-aliasing for the ink lines and band edges (the pipeline asset has MSAA off): SMAA on the world camera.</summary>
    public static void ConfigureCamera(Camera cam)
    {
        if (cam == null) return;
        var d = cam.GetUniversalAdditionalCameraData();
        if (d == null) return;
        d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        d.antialiasingQuality = AntialiasingQuality.High;
        d.renderPostProcessing = true;
    }

    /// <summary>Key light: one directional light tuned for clean bands (crisp full-strength shadows; the Toon shader thresholds them).</summary>
    public static void ConfigureLight(Light l)
    {
        if (l == null || l.type != LightType.Directional) return;
        l.transform.rotation = Quaternion.Euler(LightEuler);
        l.color = LightColor; l.intensity = 1.1f;
        l.shadows = LightShadows.Soft; l.shadowStrength = 1f;
    }

    /// <summary>Asks for an impact frame (1-2 drawn frames of ink speed lines = mode 0, or a Ben-Day ring = mode 1) at a world point.</summary>
    public static void Impact(int mode, Vector3 worldPos, float strength = 1f) { LookScreen.Request(mode, worldPos, strength); }
}
