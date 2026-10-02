using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The menu's 3D stage (SPEC v3 §5.1 + the title-in-the-wall revision + the comic look of §7), far below the world (y = -600)
/// on layer <see cref="DeaLayers.Menu"/>, seen only by its own camera. A relief wall of rounded pins on a square grid; the title
/// DEACUBE is spelled by the wall's own pins (5 x 7 pixel font, 41 columns) standing 1.25 u out of the wall with a pastel-neon
/// pink → violet → cyan gradient, dropping comic shadows on the dark wall. All pins use <see cref="Fx.Lit"/> materials (shared
/// buckets, SRP-batched; package E's toon shader gives them cel bands + ink outlines); flashes / shimmer / hover use a transient
/// MaterialPropertyBlock only while they last. Intro: the wall starts flat and dark, a light sweep with speed lines crosses it
/// (the relief pops up behind it), then the letters rise column by column with overshoot and one note per letter (the title
/// plays its own name) — no starbursts or confetti (the user, 2026-10-01: "when the title animation appears in the menu screen, there
/// shouldnt be like little pop emojis"). Pointer life: pins near the pointer extrude through a damped spring
/// (ω 12, ζ 0.55) fed by a short heat memory (a moving pointer leaves a wake); letters only ring and light up; touched pins flash
/// an instrument colour and play a pentatonic note by row; a click sends a ripple; the camera orbits ±5° toward the pointer;
/// three stylised translucent cubes float in front with parallax, pixellated (v4 user request: "liquid pixellated like
/// goose/cookingsim": drawn into a 1/<see cref="GlassPixelScale"/>-screen buffer with dithered cel bands, nearest-upscaled on an overlay
/// just under the menu canvas, so their pixel edges and dots crawl as they drift and turn; the wall stays crisp); flat dust drifts. The wall behind the focused menu item lights up
/// and hops on the menu clock's beats. v4 (SPEC §7b): the wall no longer breaks apart when the menu is left — the exit is
/// <see cref="MenuLeave"/>'s comic panel cut over a held frame; pins never leave their slots.
/// v5 (SPEC §4, the gallery): behind a focused gallery card the wall takes the song's vibe colour (<see cref="HoverTint"/>, a wider patch:
/// <see cref="HoverSpread"/>) and hops — and flashes in that colour — on the song preview's beat (<see cref="BeatSource"/>) instead of the
/// menu clock.
/// </summary>
public class MenuStage : MonoBehaviour
{
    public static MenuStage I { get; private set; }
    public static readonly Vector3 Origin = new Vector3(0f, -600f, 0f);
    public const float Fov = 50f, Pitch = 1.08f, PinDepth = 3f;
    public const float Omega = 12f, Zeta = 0.55f;
    public const float Reach = 4f, Sigma = 1.6f, PointerLift = 0.75f, HeatTau = 0.3f;
    public const float LetterDepth = 1.25f, RippleSpeed = 12f, OrbitDeg = 5f;
    public const string Word = "DEACUBE";
    public const int WordCols = 41, WordRows = 7, WordRow0 = 1;          // word grid rows 1..7 (bottom..top), columns -20..20

    // 5 x 7 glyphs, top row first
    static readonly string[][] Glyphs =
    {
        new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },   // D
        new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },   // E
        new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },   // A
        new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },   // C
        new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },   // U
        new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },   // B
        new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },   // E
    };
    public static bool IsLetterCell(int wordCol, int glyphRowFromTop)
    {
        if (wordCol < 0 || wordCol >= WordCols || glyphRowFromTop < 0 || glyphRowFromTop >= WordRows) return false;
        int k = wordCol / 6, gx = wordCol % 6;
        if (gx >= 5) return false;
        return Glyphs[k][glyphRowFromTop][gx] == '#';
    }
    /// <summary>Number of pins that spell the word.</summary>
    public static int LetterPinCount { get { int c = 0; for (int wc = 0; wc < WordCols; wc++) for (int r = 0; r < WordRows; r++) if (IsLetterCell(wc, r)) c++; return c; } }

    public Camera Cam { get; private set; }
    public MenuMusic Music;
    /// <summary>Tests: a synthetic pointer (screen px) used instead of the mouse; null = the mouse.</summary>
    public Vector2? PointerOverride;
    public bool Active { get; private set; }
    public bool Overlay { get; private set; }
    public int Count => n;
    public int Touches { get; private set; }
    public int RippleHits { get; private set; }
    public int Bursts { get; private set; }
    public Vector2 PointerLocal => ptr;
    public bool PointerOn => ptrOn;
    public float CamDist => camDist;
    /// <summary>Confetti particles emitted since <see cref="Activate"/> (only the intro and wall clicks emit; never a menu press or the leave).</summary>
    public int ConfettiEmitted { get; private set; }
    /// <summary>Particles alive in the stage's systems (confetti + dust).</summary>
    public int ParticlesAlive => (confetti != null ? confetti.particleCount : 0) + (dust != null ? dust.particleCount : 0);
    /// <summary>Wall hops given behind the focused menu item (one per menu beat while an item is focused).</summary>
    public int Hops { get; private set; }
    /// <summary>v5 (the gallery): the colour of the light behind the focused item (null = the menu accent): a card lights the wall in its
    /// song's vibe colour.</summary>
    public Color? HoverTint;
    /// <summary>v5: how wide the light, the bulge and the hops spread behind the focused item (1 = a menu word; a card is bigger).</summary>
    public float HoverSpread = 1f;
    /// <summary>v5: while set and not NaN, the beat the wall hops on (a gallery preview's beats since it began) instead of the menu clock;
    /// with a <see cref="HoverTint"/> every hop also flashes the pins behind the item in that colour (harder on each <see cref="SourceBar"/>th beat).</summary>
    public Func<double> BeatSource;
    public int SourceBar = 4;
    /// <summary>v5: hops given on the <see cref="BeatSource"/> (tests).</summary>
    public int SourceHops { get; private set; }
    /// <summary>v5: pins flashed in the hover tint by those hops (tests).</summary>
    public int TintFlashes { get; private set; }
    /// <summary>Largest distance (u) of any pin from its wall slot in the wall's plane (0: pins only ever move in and out).</summary>
    public float MaxPinDrift
    {
        get
        {
            if (pinT == null) return 0f;
            float m = 0f;
            for (int i = 0; i < n; i++) { var lp = pinT[i].localPosition; m = Mathf.Max(m, new Vector2(lp.x - p[i].x, lp.y - p[i].y).magnitude); }
            return m;
        }
    }
    /// <summary>Pins currently carrying a MaterialPropertyBlock (flash, shimmer, hover); the rest are SRP-batched.</summary>
    public int PropertyBlockPins { get { int c = 0; for (int i = 0; i < n; i++) if (hasMpb[i]) c++; return c; } }
    // reveal
    /// <summary>"my songs" / the gallery shelf: the title sinks into the wall (<see cref="SetTitleDown"/>) — its letter pins ease down to the plain
    /// wall's depth and colour, left to right, in <see cref="TitleSinkSeconds"/> (+ the stagger); on "back" they rise again column by column on
    /// the intro's spring (no flash, no starburst, no confetti: the user banned the little pops on the title).</summary>
    public const float TitleSinkSeconds = 0.3f, TitleSinkStagger = 0.12f, TitleRiseStagger = 0.24f;
    public bool TitleDown => titleDown;
    /// <summary>The sink or the rise has finished (every letter pin is down in the wall, or released to stand again).</summary>
    public bool TitleSettled => titleT >= (titleDown ? TitleSinkStagger + TitleSinkSeconds : TitleRiseStagger + 0.45f);
    /// <summary>The deepest a letter pin stands out now, and the plain wall's highest pin around the word (a sunk title is no higher).</summary>
    public float MaxLetterDepth { get { float m = 0f; for (int i = 0; i < n; i++) if (isLetter[i] && x[i] > m) m = x[i]; return m; } }
    public float MaxPlainWordDepth { get { float m = 0f; for (int i = 0; i < n; i++) if (!isLetter[i] && wcol[i] >= 0 && plainD[i] > m) m = plainD[i]; return m; } }
    /// <summary>How far the highest letter pin stands above its plain-wall depth now, and the same for the plain pins around the word (they share
    /// the hover bulge and the pointer lift): a sunk title stands no higher than its surroundings.</summary>
    public float MaxLetterLift { get { float m = float.MinValue; for (int i = 0; i < n; i++) if (isLetter[i]) m = Mathf.Max(m, x[i] - plainD[i]); return m; } }
    public float MaxWallLiftInWord { get { float m = float.MinValue; for (int i = 0; i < n; i++) if (!isLetter[i] && wcol[i] >= 0) m = Mathf.Max(m, x[i] - plainD[i]); return m; } }
    /// <summary>Letter pins drawn as letters now (their colour, not the wall's).</summary>
    public int LetterPinsLit { get { int c = 0; for (int i = 0; i < n; i++) if (isLetter[i] && curMat[i] >= MatLetter0) c++; return c; } }

    /// <summary>The title sinks into the wall (<paramref name="down"/>) or rises again; <paramref name="instant"/>: at once (the menu closing).</summary>
    public void SetTitleDown(bool down, bool instant = false)
    {
        if (down == titleDown && !instant) return;
        titleDown = down;
        titleT = instant ? 99f : 0f;
        if (p == null) return;
        for (int i = 0; i < n; i++)
        {
            if (!isLetter[i]) continue;
            sinkFrom[i] = x[i];
            if (instant) { x[i] = down ? plainD[i] : LetterDepth; v[i] = 0f; lastZ[i] = float.NaN; }
        }
    }

    /// <summary>A letter pin in the title's sink / rise: 0 standing (a letter), 1 down (a plain wall pin), between: sinking (eased, left to right).
    /// Rising, each column waits for its turn down in the wall, then springs up.</summary>
    float LetterDown(int i)
    {
        if (titleT >= 99f) return titleDown ? 1f : 0f;
        float colF = Mathf.Clamp01(wcol[i] / (float)(WordCols - 1));
        if (titleDown) return Mathf.Clamp01((titleT - colF * TitleSinkStagger) / TitleSinkSeconds);
        return titleT < colF * TitleRiseStagger ? 1f : 0f;
    }

    /// <summary>The title's screen rect standing (px): where its bottom edge is with the letters up, whatever they do now.</summary>
    public Rect TitleRestRect()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (!isLetter[i]) continue;
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    Vector3 s = Cam.WorldToScreenPoint(Origin + new Vector3(p[i].x + cx * 0.5f, p[i].y + cy * 0.5f, -LetterDepth));
                    x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
                }
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    public float RevealTime => revealT;
    public bool RevealDone => revealT < 0f || revealT > spec.end;
    public int LettersRisen { get; private set; }
    /// <summary>Seconds after the reveal start when the menu UI should appear.</summary>
    public float UiAt => spec.ui;

    struct RevealSpec { public float sweep0, sweepDur, letters0, colStep, ui, end; }
    static readonly RevealSpec BootSpec = new RevealSpec { sweep0 = 0.25f, sweepDur = 1.0f, letters0 = 1.0f, colStep = 1f / 18f, ui = 2.9f, end = 4.2f };
    static readonly RevealSpec QuickSpec = new RevealSpec { sweep0 = 0.05f, sweepDur = 0.55f, letters0 = 0.35f, colStep = 0.2f / 6f, ui = 0.95f, end = 2.1f };
    RevealSpec spec = BootSpec;

    // pins
    int n; Vector2[] p; int[] wcol, deg, tintIdx; bool[] isLetter;
    float[] baseD, x, v, heat, flash, touchT, lit, delay;
    float[] plainD, sinkFrom;   // the depth a pin has as plain wall (a letter's when the title is down); where a sinking letter started
    bool titleDown; float titleT = 99f, titleShown = 1f;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    Color[] fcol;
    float[] lastZ; float[] lastS; byte[] curMat; bool[] hasMpb; float[] lastEm;
    int halfCols, halfRows; float builtAspect = -1f, halfW, halfH;
    readonly float[] letterGlow = new float[7], letterNoteT = new float[7];
    float revealT = -1f; bool revealFirst; int burstLetters;
    int lastHopBeat = int.MinValue;
    // pointer / hover / clicks
    Vector2 ptr, ptrPrev; bool ptrOn; float ptrSpeed;
    Vector2 hoverAt; float hoverAmt, hoverTarget;
    struct Ripple { public Vector2 o; public float r; public Color c; public float k; }
    readonly List<Ripple> ripples = new List<Ripple>();
    float shimmerT = -1f, nextShimmer; bool shimmerArmed;
    // camera
    Vector2 look, lookVel; float camDist = 35f;
    // render
    Mesh pinMesh, quadMesh;
    Material matDark, matWall, matBand; readonly Material[] matTint = new Material[5]; readonly Material[] matLetter = new Material[WordCols];
    const byte MatDark = 0, MatWall = 1, MatBand = 2, MatTint0 = 3, MatLetter0 = 8;
    Transform pinsRoot; Transform[] pinT; MeshRenderer[] pinR;
    MaterialPropertyBlock mpbField;   // created lazily: Unity forbids MaterialPropertyBlock in a MonoBehaviour field initializer
    MaterialPropertyBlock mpb => mpbField ?? (mpbField = new MaterialPropertyBlock());
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), FadeId = Shader.PropertyToID("_Fade");
    UniversalAdditionalCameraData camData, stackBase;
    // extras: glow line under the word, comic starbursts, speed lines, flat confetti + dust
    Transform glowLine; Material glowMat; float wordAppear;
    ParticleSystem confetti, dust;
    class FxQuad { public Transform t, ink; public Material mat, inkMat; public float age = 9f, dur, size, spin; public Vector3 vel; public bool line; }
    readonly List<FxQuad> fxPool = new List<FxQuad>();
    float nextSpeedLine;

    class Glass
    {
        public Transform t; public Material mat; public Vector3 home; public float size, par, spin, phase; public Vector3 axis; public Quaternion tilt;
        public Vector2 ndc; public float depth01, size01;
    }
    readonly List<Glass> glass = new List<Glass>();

    static readonly Color Bg = new Color(0.012f, 0.013f, 0.024f);
    static readonly Color Ink = new Color(0.07f, 0.045f, 0.12f);
    // the word's gradient (sRGB, pastel neon): pink → violet → cyan, like the glass cubes
    static readonly Color GradA = new Color(1.0f, 0.42f, 0.78f), GradB = new Color(0.72f, 0.52f, 1.0f), GradC = new Color(0.42f, 0.9f, 1.0f);
    public static Color WordColor(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        return t01 < 0.5f ? Color.Lerp(GradA, GradB, t01 * 2f) : Color.Lerp(GradB, GradC, (t01 - 0.5f) * 2f);
    }
    static Vector4 Lin(Color c, float k) { var l = c.linear; return new Vector4(l.r * k, l.g * k, l.b * k, 1f); }

    // ------------------------------------------------------------------ lifecycle
    public static MenuStage Create(Transform parent)
    {
        var go = new GameObject("MenuStage");
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = Origin;
        var s = go.AddComponent<MenuStage>();
        s.Build();
        return s;
    }

    void Awake() { I = this; }
    void OnDestroy()
    {
        if (I == this) I = null;
        if (glassCmd != null) { glassCmd.Release(); glassCmd = null; }
        if (glassRT != null) { glassRT.Release(); Destroy(glassRT); glassRT = null; }
    }

    void Build()
    {
        var camGo = new GameObject("MenuCamera");
        camGo.transform.SetParent(transform, false);
        Cam = camGo.AddComponent<Camera>();
        Cam.clearFlags = CameraClearFlags.SolidColor;
        Cam.backgroundColor = Bg;
        Cam.cullingMask = 1 << DeaLayers.Menu;
        Cam.fieldOfView = Fov; Cam.nearClipPlane = 0.5f; Cam.farClipPlane = 160f;
        Cam.allowHDR = true; Cam.allowMSAA = false; Cam.depth = 5f;
        camData = Cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;                 // bloom, vignette and package E's grading from the global volume
        camData.renderShadows = true;                         // the letters drop comic shadows on the wall
        camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        camData.antialiasingQuality = AntialiasingQuality.High;
        camData.volumeLayerMask = 1;                          // the Global Volume lives on Default
        Cam.enabled = false;

        pinMesh = MeshFactory.RoundedBox(new Vector3(1f, 1f, PinDepth), 0.1f, 3);
        quadMesh = CenteredQuad();
        BuildMaterials();
        BuildGrid(AspectNow());
        BuildGlass();
        BuildPixelGlass();
        BuildExtras();
        PlaceGlass();
        SetExtrasVisible(false);
    }

    void BuildMaterials()
    {
        matDark = Fx.Lit(WallDark, 0.15f); matDark.name = "MenuPinDark";
        matWall = Fx.Lit(WallBase, 0.3f); matWall.name = "MenuPinWall";
        matBand = Fx.Lit(new Color(0.36f, 0.32f, 0.56f), 0.45f); matBand.name = "MenuPinBand";
        matBand.SetVector(EmissionId, Lin(new Color(0.62f, 0.55f, 1f), 0.55f));
        for (int k = 0; k < matTint.Length; k++)
        {
            Color ic = InstColor(k * 2);
            matTint[k] = Fx.Lit(Color.Lerp(WallBase, ic, 0.12f), 0.3f);
            matTint[k].SetVector(EmissionId, Lin(ic, 0.02f));
            matTint[k].name = "MenuPinTint" + k;
        }
        for (int c = 0; c < WordCols; c++)
        {
            Color wc = WordColor(c / (float)(WordCols - 1));
            matLetter[c] = Fx.Lit(wc, 0.55f);
            matLetter[c].SetVector(EmissionId, Lin(wc, LetterGlow));
            matLetter[c].name = "MenuLetter" + c;
        }
    }
    /// <summary>The plain wall (sRGB): dark, so the title reads at once.</summary>
    public static readonly Color WallBase = new Color(0.085f, 0.08f, 0.14f), WallDark = new Color(0.03f, 0.03f, 0.05f);
    /// <summary>Steady emission of the letters (linear multiplier of their colour).</summary>
    public const float LetterGlow = 0.32f;

    Material MatOf(byte id)
    {
        if (id == MatDark) return matDark;
        if (id == MatWall) return matWall;
        if (id == MatBand) return matBand;
        if (id < MatLetter0) return matTint[id - MatTint0];
        return matLetter[Mathf.Clamp(id - MatLetter0, 0, WordCols - 1)];
    }

    static float AspectNow() => Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
    static float TanHalf => Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
    /// <summary>Camera distance so the word keeps a margin of plain wall on either side at this aspect (inside URP's shadow range).</summary>
    static float DistFor(float aspect)
    {
        float wordW = WordCols * Pitch + 8f * Pitch;
        return Mathf.Max(35f, wordW / (2f * TanHalf * Mathf.Max(0.5f, aspect)));
    }

    static Mesh CenteredQuad()
    {
        var m = new Mesh { name = "menuQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }

    static float Hash01(int i, int salt)
    {
        unchecked
        {
            uint h = (uint)(i * 747796405 + salt * 2891336453u);
            h = ((h >> ((int)(h >> 28) + 4)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    void BuildGrid(float aspect)
    {
        builtAspect = Mathf.Clamp(aspect, 16f / 9f, 2.1f);
        camDist = DistFor(aspect);
        halfH = camDist * TanHalf + 2f;
        halfW = camDist * TanHalf * builtAspect + 2f;
        halfCols = Mathf.CeilToInt(halfW / Pitch); halfRows = Mathf.CeilToInt(halfH / Pitch);
        var cells = new List<Vector2Int>();
        for (int r = -halfRows; r <= halfRows; r++) for (int c = -halfCols; c <= halfCols; c++) cells.Add(new Vector2Int(c, r));
        n = cells.Count;
        p = new Vector2[n]; wcol = new int[n]; deg = new int[n]; tintIdx = new int[n]; isLetter = new bool[n];
        baseD = new float[n]; x = new float[n]; v = new float[n]; heat = new float[n]; flash = new float[n]; touchT = new float[n]; lit = new float[n]; delay = new float[n];
        plainD = new float[n]; sinkFrom = new float[n];
        fcol = new Color[n]; lastZ = new float[n]; lastS = new float[n]; curMat = new byte[n]; hasMpb = new bool[n]; lastEm = new float[n];
        for (int i = 0; i < n; i++)
        {
            var cell = cells[i];
            Vector2 q = new Vector2(cell.x * Pitch, cell.y * Pitch);
            p[i] = q;
            int wc = cell.x + WordCols / 2, gy = WordRow0 + WordRows - 1 - cell.y;   // glyph row from the top
            isLetter[i] = IsLetterCell(wc, gy);
            wcol[i] = (wc >= 0 && wc < WordCols && gy >= 0 && gy < WordRows) ? wc : -1;
            float h1 = Mathf.PerlinNoise(q.x * 0.07f + 13.1f, q.y * 0.07f + 7.7f);
            float h2 = Hash01(i, 3);
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 30f, q.magnitude));
            plainD[i] = (0.03f + 0.32f * h1 * h1 + 0.2f * h2 * h2 + 0.3f * edge) * (wcol[i] >= 0 ? 0.45f : 1f);   // (the plain wall right around the word stays low)
            baseD[i] = isLetter[i] ? LetterDepth : plainD[i];
            sinkFrom[i] = baseD[i];
            deg[i] = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-halfRows + 1, halfRows - 1, cell.y) * 12f), 0, 12);
            tintIdx[i] = !isLetter[i] && wcol[i] < 0 && Hash01(i, 11) < 0.06f ? Mathf.FloorToInt(Hash01(i, 17) * 4.999f) : -1;
            x[i] = baseD[i]; lit[i] = 1f; touchT[i] = -9f; lastZ[i] = float.NaN; lastS[i] = 1f; curMat[i] = 255; lastEm[i] = 0f;
        }
        BuildPins();
    }

    /// <summary>One MeshRenderer per pin, shared mesh and a handful of shared Fx.Lit materials: SRP-batched.</summary>
    void BuildPins()
    {
        bool wasActive = pinsRoot != null && pinsRoot.gameObject.activeSelf;
        if (pinsRoot != null) Destroy(pinsRoot.gameObject);
        var root = new GameObject("Pins");
        root.layer = DeaLayers.Menu;
        root.transform.SetParent(transform, false);
        pinsRoot = root.transform;
        pinT = new Transform[n]; pinR = new MeshRenderer[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject(isLetter[i] ? "Letter" : "Pin");
            go.layer = DeaLayers.Menu;
            var tr = go.transform;
            tr.SetParent(pinsRoot, false);
            tr.localPosition = new Vector3(p[i].x, p[i].y, -x[i] + PinDepth * 0.5f);
            go.AddComponent<MeshFilter>().sharedMesh = pinMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = matWall; curMat[i] = MatWall;
            r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            pinT[i] = tr; pinR[i] = r;
        }
        root.SetActive(wasActive);
    }

    static Color InstColor(int i)
    {
        i = Mathf.Clamp(i, 0, Instruments.Count - 1);
        Color c = Instruments.Colors[i];
        return c.maxColorComponent < 0.05f ? Palette.Accent : c;
    }
    /// <summary>Flash colour of a pentatonic degree (each note keeps its colour).</summary>
    static Color DegreeColor(int k)
    {
        switch (((k % 5) + 5) % 5)
        {
            case 0: return InstColor(4);
            case 1: return InstColor(5);
            case 2: return InstColor(7);
            case 3: return InstColor(1);
            default: return InstColor(6);
        }
    }

    // ------------------------------------------------------------------ glass cubes (stylised: flat translucent colour, crisp ink rim)
    void BuildGlass()
    {
        var sh = Shader.Find("DeaCube/MenuGlass");
        var cube = MeshFactory.RoundedBox(Vector3.one, 0.03f, 2);
        glassMesh = cube;
        // pink (upper right), violet-blue (lower left), cyan (small, upper left): Tonecraft key art; placed in viewport terms
        AddGlass(sh, cube, new Vector2(0.9f, 0.78f), 0.5f, 0.25f, 1.4f, new Color(1f, 0.45f, 0.8f), new Color(0.86f, 0.24f, 0.7f), new Vector3(0.3f, 1f, 0.15f), 7f, 0.1f);
        AddGlass(sh, cube, new Vector2(-0.88f, -0.72f), 0.6f, 0.27f, 1.7f, new Color(0.45f, 0.62f, 1f), new Color(0.58f, 0.36f, 1f), new Vector3(-0.2f, 1f, 0.35f), -6f, 2.1f);
        AddGlass(sh, cube, new Vector2(-0.74f, 0.76f), 0.42f, 0.085f, 2.6f, new Color(0.45f, 0.95f, 1f), new Color(0.3f, 0.7f, 1f), new Vector3(0.5f, 1f, -0.3f), 11f, 4.3f);
    }

    void AddGlass(Shader sh, Mesh cube, Vector2 ndc, float depth01, float size01, float par, Color a, Color b, Vector3 axis, float spinDeg, float phase)
    {
        var g = new Glass { ndc = ndc, depth01 = depth01, size01 = size01, par = par, axis = axis.normalized, spin = spinDeg, phase = phase };
        g.tilt = Quaternion.Euler(28f + phase * 9f, 36f + phase * 13f, 12f * phase);
        var go = new GameObject("GlassCube" + glass.Count);
        go.layer = DeaLayers.Menu;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        g.mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "MenuGlass" };
        g.mat.SetColor("_ColA", a); g.mat.SetColor("_ColB", b); g.mat.SetColor("_Ink", Ink);
        g.mat.SetFloat("_Pixel", 1f); g.mat.SetFloat("_Bands", GlassBands);
        mr.sharedMaterial = g.mat;
        mr.enabled = false;   // drawn into the pixel buffer (RenderPixelGlass), never by the stage camera
        g.t = go.transform;
        glass.Add(g);
    }

    // ------------------------------------------------------------------ pixel glass (the cubes, liquid pixellated)
    /// <summary>The glass cubes' buffer is 1/GlassPixelScale of the screen per side; their shading falls into GlassBands dithered bands.</summary>
    public const int GlassPixelScale = 4;
    public const float GlassBands = 3f;
    /// <summary>The low-res buffer the glass cubes are drawn into this frame (null before the first draw).</summary>
    public RenderTexture GlassTexture => glassRT;
    /// <summary>The overlay that shows <see cref="GlassTexture"/> nearest-upscaled (just under the menu canvas).</summary>
    public Canvas GlassCanvas => pixCanvas;
    public int GlassDraws { get; private set; }
    /// <summary>The buffer is drawn with the texture-space projection (GL.GetGPUProjectionMatrix(.., true)); on platforms whose UVs start at
    /// the top (Metal, D3D, Vulkan) the overlay then samples it upside down, so it flips its uv rect. Tests may force it.</summary>
    public static bool? GlassFlipOverride;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { GlassFlipOverride = null; }
    public static bool GlassFlip => GlassFlipOverride ?? SystemInfo.graphicsUVStartsAtTop;
    Mesh glassMesh; RenderTexture glassRT; CommandBuffer glassCmd; Canvas pixCanvas; RawImage pixImage;

    void BuildPixelGlass()
    {
        glassCmd = new CommandBuffer { name = "MenuGlassPixels" };
        var go = new GameObject("MenuPixelCanvas", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        pixCanvas = go.AddComponent<Canvas>();
        pixCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        pixCanvas.sortingOrder = MainMenu.SortOrder - 1;   // over the stage camera's frame, under the vignette and the words
        var ig = new GameObject("GlassPixels", typeof(RectTransform));
        ig.layer = 5;
        ig.transform.SetParent(go.transform, false);
        var rt = (RectTransform)ig.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        pixImage = ig.AddComponent<RawImage>();
        pixImage.raycastTarget = false;
        var sh = Shader.Find("DeaCube/MenuPixel");
        if (sh != null) pixImage.material = new Material(sh) { name = "MenuPixel (runtime)" };
        else Debug.LogWarning("MenuStage: DeaCube/MenuPixel shader missing: the pixel glass composites with the default UI shader");
        pixImage.enabled = false;
        go.SetActive(false);
    }

    void EnsureGlassBuffer()
    {
        int w = Mathf.Max(32, Mathf.RoundToInt(Screen.width / (float)GlassPixelScale)), h = Mathf.Max(32, Mathf.RoundToInt(Screen.height / (float)GlassPixelScale));
        if (glassRT != null && glassRT.width == w && glassRT.height == h) return;
        if (glassRT != null) { glassRT.Release(); Destroy(glassRT); }
        glassRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "MenuGlassPixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, antiAliasing = 1
        };
        glassRT.Create();
        if (pixImage != null) pixImage.texture = glassRT;
        if (pixImage != null) pixImage.uvRect = GlassFlip ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
    }

    /// <summary>Draws the glass cubes (back faces, then front faces) into the low-res buffer with the stage camera's view; the
    /// overlay shows it nearest-upscaled. Called every frame the stage is on, after the cubes have moved.</summary>
    void RenderPixelGlass()
    {
        if (glassMesh == null || glassCmd == null || Cam == null) return;
        EnsureGlassBuffer();
        glassCmd.Clear();
        glassCmd.SetRenderTarget(glassRT);
        glassCmd.ClearRenderTarget(false, true, Color.clear);
        glassCmd.SetViewProjectionMatrices(Cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(Cam.projectionMatrix, true));
        foreach (var g in glass)
        {
            if (g.t == null || !g.t.gameObject.activeInHierarchy || g.t.localScale.x < 0.002f) continue;
            Matrix4x4 m = g.t.localToWorldMatrix;
            glassCmd.DrawMesh(glassMesh, m, g.mat, 0, 0);
            glassCmd.DrawMesh(glassMesh, m, g.mat, 0, 1);
        }
        Graphics.ExecuteCommandBuffer(glassCmd);
        GlassDraws++;
        if (pixImage != null && !pixImage.enabled) pixImage.enabled = true;
    }

    /// <summary>Glass homes from their viewport placement at the current camera distance and aspect.</summary>
    void PlaceGlass()
    {
        float asp = Mathf.Max(AspectNow(), 1.3f);
        foreach (var g in glass)
        {
            float d = camDist * g.depth01;                      // distance from the camera
            float hh = d * TanHalf, hw = hh * asp;
            g.home = new Vector3(g.ndc.x * hw, g.ndc.y * hh, -(camDist - d));
            g.size = g.size01 * 2f * hh;
        }
    }

    // ------------------------------------------------------------------ comic FX
    void BuildExtras()
    {
        glowLine = Quad("WordGlowLine", Fx.Additive(MenuSprites.SoftBar.texture, new Color(0.7f, 0.5f, 1f), 0f), out glowMat);
        confetti = MakeParticles("MenuConfetti", Fx.Alpha(IconFactory.GetTexture("diamond"), Color.white), false);
        dust = MakeParticles("MenuDust", Fx.Alpha(IconFactory.GetTexture("dot"), Color.white), true);
        IconFactory.Register("mBurst", q => IconFactory.P.Star(q, 12, 0.98f, 0.56f, 90f));
        IconFactory.Register("mBurstIn", q => IconFactory.P.Star(q, 12, 0.8f, 0.46f, 90f));
    }

    Transform Quad(string name, Material m, out Material mat)
    {
        var q = new GameObject(name);
        q.layer = DeaLayers.Menu;
        q.transform.SetParent(transform, false);
        q.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        var r = q.AddComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.sharedMaterial = m; mat = m;
        return q.transform;
    }

    FxQuad GetFx(bool line)
    {
        foreach (var f in fxPool) if (f.age >= f.dur && f.line == line) return f;
        var fq = new FxQuad { line = line };
        if (line) fq.t = Quad("SpeedLine", Fx.Alpha(IconFactory.GetTexture("white"), Color.white), out fq.mat);
        else
        {
            fq.ink = Quad("BurstInk", Fx.Alpha(IconFactory.GetTexture("mBurst"), Ink), out fq.inkMat);
            fq.t = Quad("Burst", Fx.Alpha(IconFactory.GetTexture("mBurstIn"), Color.white), out fq.mat);
        }
        fxPool.Add(fq);
        return fq;
    }

    /// <summary>A flat comic starburst with an ink rim at a wall-local point (pops, spins a little, vanishes).</summary>
    void ComicBurst(Vector2 local, float z, Color c, float size)
    {
        var f = GetFx(false);
        f.age = 0f; f.dur = 0.42f; f.size = size; f.spin = UnityEngine.Random.Range(-40f, 40f);
        f.t.position = Origin + new Vector3(local.x, local.y, z - 0.05f);
        f.ink.position = Origin + new Vector3(local.x, local.y, z);
        f.t.rotation = f.ink.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 30f));
        f.mat.color = Color.Lerp(c, Color.white, 0.25f);
        f.t.gameObject.SetActive(true); f.ink.gameObject.SetActive(true);
        Bursts++;
    }

    void SpeedLine(float x0, float y, float len)
    {
        var f = GetFx(true);
        f.age = 0f; f.dur = 0.32f; f.size = len; f.vel = new Vector3(46f, 0f, 0f);
        f.t.position = Origin + new Vector3(x0, y, -LetterDepth - 1.2f);
        f.t.rotation = Quaternion.identity;
        f.t.gameObject.SetActive(true);
    }

    void UpdateFx(float dt)
    {
        foreach (var f in fxPool)
        {
            if (f.age >= f.dur) continue;
            f.age += dt;
            float u = Mathf.Clamp01(f.age / f.dur);
            if (f.line)
            {
                f.t.position += f.vel * dt;
                f.t.localScale = new Vector3(f.size * (1f - 0.4f * u), 0.07f, 1f);
                f.mat.color = new Color(1f, 1f, 1f, 0.75f * (1f - u));
                if (u >= 1f) f.t.gameObject.SetActive(false);
            }
            else
            {
                float s = f.size * (u < 0.3f ? Ease.OutBack(u / 0.3f, 2.2f) : Mathf.Lerp(1f, 0.2f, Ease.InCubic((u - 0.3f) / 0.7f)));
                f.t.localScale = Vector3.one * s * 0.94f;
                f.ink.localScale = Vector3.one * s;
                var r = Quaternion.Euler(0f, 0f, f.spin * dt);
                f.t.rotation = r * f.t.rotation; f.ink.rotation = f.t.rotation;
                float a = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
                var c = f.mat.color; c.a = a; f.mat.color = c;
                var ic = f.inkMat.color; ic.a = a; f.inkMat.color = ic;
                if (u >= 1f) { f.t.gameObject.SetActive(false); f.ink.gameObject.SetActive(false); }
            }
        }
    }

    ParticleSystem MakeParticles(string name, Material mat, bool isDust)
    {
        var go = new GameObject(name);
        go.layer = DeaLayers.Menu;
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles = isDust ? 160 : 900;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        if (isDust)
        {
            main.loop = true; main.prewarm = true; main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.75f, 1f, 0.55f), new Color(0.6f, 0.9f, 1f, 0.4f));
            var em = ps.emission; em.enabled = true; em.rateOverTime = 12f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(56f, 32f, 14f);
            go.transform.position = Origin + new Vector3(0f, 0f, -8f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.15f; noise.scrollSpeed = 0.1f;
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        }
        else
        {
            main.loop = false; main.duration = 1f;
            main.gravityModifier = 0.9f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        }
        col.color = new ParticleSystem.MinMaxGradient(grad);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    void SetExtrasVisible(bool on)
    {
        foreach (var g in glass) if (g.t != null) g.t.gameObject.SetActive(on);
        if (pixCanvas != null) { pixCanvas.gameObject.SetActive(on); if (!on && pixImage != null) pixImage.enabled = false; }
        if (glowLine != null) glowLine.gameObject.SetActive(on);
        if (pinsRoot != null) pinsRoot.gameObject.SetActive(on);
        if (confetti != null) { confetti.gameObject.SetActive(on); if (on) confetti.Play(); }
        if (dust != null) { dust.gameObject.SetActive(on); if (on) dust.Play(); else dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        if (!on) foreach (var f in fxPool) { f.age = f.dur; if (f.t != null) f.t.gameObject.SetActive(false); if (f.ink != null) f.ink.gameObject.SetActive(false); }
    }

    // ------------------------------------------------------------------ control (MainMenu)
    public void Activate()
    {
        float a = AspectNow();
        if (Mathf.Abs(Mathf.Clamp(a, 16f / 9f, 2.1f) - builtAspect) > 0.02f || Mathf.Abs(DistFor(a) - camDist) > 0.5f) BuildGrid(a);
        PlaceGlass();
        Active = true;
        Cam.enabled = true;
        SetExtrasVisible(true);
        ripples.Clear(); hoverAmt = 0f; hoverTarget = 0f; Touches = 0; RippleHits = 0; Bursts = 0; ConfettiEmitted = 0; Hops = 0; lastHopBeat = int.MinValue;
        look = Vector2.zero; lookVel = Vector2.zero;
        for (int i = 0; i < n; i++) lastZ[i] = float.NaN;
    }

    public void Deactivate()
    {
        MakeBase();
        Active = false;
        if (Cam != null) Cam.enabled = false;
        SetExtrasVisible(false);
        ripples.Clear(); revealT = -1f; PointerOverride = null;
        HoverTint = null; HoverSpread = 1f; BeatSource = null;
    }

    /// <summary>Render on top of <paramref name="baseCam"/> (URP camera stack) so the world shows wherever no pin is; false when not possible.</summary>
    public bool TryOverlayOn(Camera baseCam)
    {
        if (baseCam == null || baseCam == Cam) return false;
        try
        {
            var bd = baseCam.GetUniversalAdditionalCameraData();
            if (bd == null || bd.renderType != CameraRenderType.Base || bd.cameraStack == null) return false;
            camData.antialiasing = AntialiasingMode.None;
            camData.renderType = CameraRenderType.Overlay;
            if (!bd.cameraStack.Contains(Cam)) bd.cameraStack.Add(Cam);
            stackBase = bd; Overlay = true; Cam.enabled = true;
            return true;
        }
        catch (Exception e) { Debug.LogWarning("MenuStage overlay failed: " + e.Message); MakeBase(); return false; }
    }

    /// <summary>Back to a standalone base camera (removed from any stack).</summary>
    public void MakeBase()
    {
        try
        {
            if (stackBase != null && stackBase.cameraStack != null) stackBase.cameraStack.Remove(Cam);
            if (camData != null)
            {
                camData.renderType = CameraRenderType.Base;
                camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }
        catch (Exception e) { Debug.LogWarning("MenuStage base failed: " + e.Message); }
        stackBase = null; Overlay = false;
    }

    /// <summary>The intro: flat dark wall → a light sweep lifts the relief → the letters rise column by column (one note each).
    /// <paramref name="quick"/>: the short version for coming back from the world.</summary>
    public void BeginReveal(bool quick)
    {
        spec = quick ? QuickSpec : BootSpec;
        revealT = 0f; revealFirst = true; LettersRisen = 0; burstLetters = 0;
        if (titleDown) titleT = 99f;   // a shelf already open (the HUD's "my songs"): the letters stay in the wall through the reveal
        shimmerT = -1f; shimmerArmed = false; wordAppear = 0f; nextSpeedLine = 0f;
        for (int i = 0; i < n; i++)
        {
            x[i] = 0f; v[i] = 0f; heat[i] = 0f; flash[i] = 0f; lit[i] = 0f; lastZ[i] = float.NaN;
            delay[i] = wcol[i] >= 0 && isLetter[i] ? spec.letters0 + wcol[i] * spec.colStep : -1f;
        }
        for (int k = 0; k < letterGlow.Length; k++) { letterGlow[k] = 0f; letterNoteT[k] = -9f; }
        if (Music != null) Music.PlayName(AudioSettings.dspTime + MenuMusic.Lead + spec.letters0, spec.colStep * 6f);
    }

    static Vector2 LetterCentre(int k) => new Vector2((k * 6 + 2 - WordCols / 2) * Pitch, (WordRow0 + 3) * Pitch);

    public void Click(Vector2 screen, Color c, float strength = 1f)
    {
        Vector2 at = ScreenToWall(screen);
        ripples.Add(new Ripple { o = at, r = 0f, c = c, k = strength });
        ComicBurst(at, -1.4f, c, 1.6f);
    }

    /// <summary>A menu item press: nearby pins punch forward and flash, plus a small ripple (v4: no confetti, no burst — the leave's
    /// impact sticker is the punctuation).</summary>
    public void Press(Vector2 screen, Color c)
    {
        Vector2 at = ScreenToWall(screen);
        for (int i = 0; i < n; i++)
        {
            float d2 = (p[i] - at).sqrMagnitude;
            if (d2 > 20f) continue;
            float k = Mathf.Exp(-d2 / (2f * 2.2f * 2.2f));
            v[i] += 10f * k;
            if (k > 0.2f) { flash[i] = Mathf.Max(flash[i], k); fcol[i] = Color.Lerp(c, DegreeColor(deg[i]), 0.35f); }
        }
        ripples.Add(new Ripple { o = at, r = 0f, c = c, k = 0.7f });
    }

    /// <summary>Hovered menu button (screen px): the wall behind it lights up and bulges a little. amount 0 = none.</summary>
    public void SetHover(Vector2 screen, float amount)
    {
        hoverTarget = Mathf.Clamp01(amount);
        if (amount > 0.001f) hoverAt = ScreenToWall(screen);
    }

    void EmitConfetti(int i, int count, Color c, float speed)
    {
        if (confetti == null) return;
        ConfettiEmitted += count;
        Vector3 front = FrontOf(i);
        var ep = new ParticleSystem.EmitParams();
        for (int k = 0; k < count; k++)
        {
            Vector3 dir = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.2f, 1.3f), UnityEngine.Random.Range(-1.4f, -0.3f)).normalized;
            ep.position = front + new Vector3(UnityEngine.Random.Range(-0.45f, 0.45f), UnityEngine.Random.Range(-0.45f, 0.45f), -0.1f);
            ep.velocity = dir * speed * UnityEngine.Random.Range(0.5f, 1.2f);
            ep.startSize = UnityEngine.Random.Range(0.16f, 0.34f);
            ep.startLifetime = UnityEngine.Random.Range(0.5f, 0.9f);
            ep.startColor = Color.Lerp(c, Color.white, UnityEngine.Random.Range(0f, 0.3f));
            confetti.Emit(ep, 1);
        }
    }

    // ------------------------------------------------------------------ queries (tests)
    public Vector2 ScreenToWall(Vector2 screen)
    {
        if (Cam == null) return Vector2.zero;
        Ray r = Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
        float planeZ = Origin.z - 0.6f;
        if (Mathf.Abs(r.direction.z) < 1e-4f) return Vector2.zero;
        float t = (planeZ - r.origin.z) / r.direction.z;
        Vector3 h = r.origin + r.direction * t - Origin;
        return new Vector2(h.x, h.y);
    }
    public Vector3 FrontOf(int i) => Origin + new Vector3(p[i].x, p[i].y, -x[i]);
    /// <summary>Screen px of wall-local point (<paramref name="local"/>, <paramref name="z"/> toward the camera) as seen by the camera at
    /// rest (no pointer orbit, sway or dolly): the menu lays its items out against the title with it.</summary>
    public Vector2 RestScreenPoint(Vector2 local, float z)
    {
        float aspect = AspectNow(), depth = Mathf.Max(1f, camDist - z);
        float nx = local.x / (depth * TanHalf * aspect), ny = local.y / (depth * TanHalf);
        return new Vector2((nx * 0.5f + 0.5f) * Screen.width, (ny * 0.5f + 0.5f) * Screen.height);
    }
    /// <summary>Wall-local x of the title's left edge (the D) and y of its bottom edge.</summary>
    public static float WordLeft => -(WordCols / 2) * Pitch - 0.5f;
    public static float WordBottom => WordRow0 * Pitch - 0.5f;
    public Vector2 ScreenOf(int i) { if (Cam == null) return Vector2.zero; Vector3 s = Cam.WorldToScreenPoint(FrontOf(i)); return new Vector2(s.x, s.y); }
    /// <summary>How far pin i sticks out beyond its resting relief (u).</summary>
    public float ExtrusionOf(int i) => x[i] - baseD[i];
    public float DepthOf(int i) => x[i];
    public bool IsLetter(int i) => isLetter[i];
    public float HeatOf(int i) => heat[i];
    public Vector2 LocalOf(int i) => p[i];
    public Material MaterialOf(int i) => pinR != null && i >= 0 && i < n ? pinR[i].sharedMaterial : null;

    /// <summary>Largest extra extrusion of plain wall pins within <paramref name="radiusPx"/> of <paramref name="screen"/> and beyond 3x that radius, and where the maximum is.
    /// v4: the wall behind the focused menu word (it bulges and hops on the beat while focused) is not the pointer's: it counts for
    /// neither the far maximum nor the argmax.</summary>
    public void Probe(Vector2 screen, float radiusPx, out float nearMax, out float farMax, out Vector2 argmax)
    {
        nearMax = 0f; farMax = 0f; argmax = Vector2.zero; float best = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (isLetter[i]) continue;
            Vector2 s = ScreenOf(i);
            if (s.x < 0f || s.y < 0f || s.x > Screen.width || s.y > Screen.height) continue;
            float e = ExtrusionOf(i);
            float d = (s - screen).magnitude;
            if (d <= radiusPx) nearMax = Mathf.Max(nearMax, e);
            else if (InFocusZone(i)) continue;
            else if (d >= radiusPx * 3f) farMax = Mathf.Max(farMax, e);
            if (e > best) { best = e; argmax = s; }
        }
    }

    /// <summary>Pin i is part of the wall behind the focused menu word (within 3.2 sigma of its bulge).</summary>
    public bool InFocusZone(int i) { float z = FocusZone * Mathf.Max(1f, HoverSpread); return hoverAmt > 0.01f && (p[i] - hoverAt).sqrMagnitude < z * z; }
    public const float FocusZone = 7.5f;

    /// <summary>The word as seen now: its screen rect (px), the smallest letter-pin depth and the letter-pin count.</summary>
    public Rect WordScreenRect(out float minDepth, out int letters)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; minDepth = float.MaxValue; letters = 0;
        for (int i = 0; i < n; i++)
        {
            if (!isLetter[i]) continue;
            letters++;
            minDepth = Mathf.Min(minDepth, x[i]);
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    Vector3 s = Cam.WorldToScreenPoint(Origin + new Vector3(p[i].x + cx * 0.5f, p[i].y + cy * 0.5f, -x[i]));
                    x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
                }
        }
        if (letters == 0) minDepth = 0f;
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (!Active) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (dt <= 0f) return;
        float now = Time.realtimeSinceStartup;
        if (revealT >= 0f) { if (revealFirst) revealFirst = false; else revealT += dt; }
        float titleT0 = titleT;
        if (titleT < 99f) titleT += dt;
        titleShown = Mathf.MoveTowards(titleShown, titleDown ? 0f : 1f, dt / 0.3f);

        UpdatePointer(dt);
        UpdateCamera(dt);
        UpdateRipples(dt);
        hoverAmt = Mathf.Lerp(hoverAmt, hoverTarget, 1f - Mathf.Exp(-dt * 10f));
        for (int k = 0; k < letterGlow.Length; k++) letterGlow[k] *= Mathf.Exp(-dt * 2.2f);
        if (RevealDone)
        {
            if (!shimmerArmed) { shimmerArmed = true; nextShimmer = now + 1.2f; }   // the first shimmer soon after the reveal
            if (shimmerT >= 0f) { shimmerT += dt; if (shimmerT > 1.8f) { shimmerT = -1f; nextShimmer = now + 4.5f; } }
            else if (now >= nextShimmer) shimmerT = 0f;
        }

        bool revealing = revealT >= 0f && revealT <= spec.end;
        float sweepX = revealing ? Mathf.Lerp(-halfW - 3f, halfW + 3f, (revealT - spec.sweep0) / spec.sweepDur) : 1e6f;
        if (revealing && sweepX > -halfW - 2f && sweepX < halfW + 2f && now >= nextSpeedLine)
        {
            nextSpeedLine = now + 0.028f;
            SpeedLine(sweepX - UnityEngine.Random.Range(4f, 9f), UnityEngine.Random.Range(-halfH * 0.85f, halfH * 0.85f), UnityEngine.Random.Range(3f, 8f));
        }
        float decay = Mathf.Exp(-dt / HeatTau), fdecay = Mathf.Exp(-dt * 2.4f);
        float spread = Mathf.Max(0.5f, HoverSpread);
        float inv2s2 = 1f / (2f * Sigma * Sigma), reach2 = Reach * Reach, hov2 = 1f / (2f * 2.4f * 2.4f * spread * spread);
        int sub = Mathf.Clamp(Mathf.CeilToInt(dt * 120f), 1, 6); float h = dt / sub;
        float w2 = Omega * Omega, c2 = 2f * Zeta * Omega;
        bool pointerLive = ptrOn && (!revealing || revealT > spec.letters0);
        // the wall behind the focused menu item hops on the menu clock's beats (v5: on a gallery preview's beats while one plays)
        int hopBeat = int.MinValue; bool fromSource = false;
        if (hoverAmt > 0.3f && !revealing)
        {
            double sb = BeatSource != null ? BeatSource() : double.NaN;
            if (!double.IsNaN(sb)) { if (sb >= 0.0) { hopBeat = (int)Math.Floor(sb); fromSource = true; } }
            else if (Music != null && Music.Running && !Music.Held)
            {
                double beats = (AudioSettings.dspTime - Music.ClockStart) * MenuMusic.Bpm / 60.0;
                if (beats >= 0.0) hopBeat = (int)Math.Floor(beats);
            }
        }
        bool hop = hopBeat != int.MinValue && hopBeat != lastHopBeat;
        if (hop) { lastHopBeat = hopBeat; Hops++; if (fromSource) SourceHops++; }
        float hopK = 1f / (2f * 1.9f * 1.9f * spread * spread);
        int bar = Math.Max(1, SourceBar);
        float flashK = hop && fromSource && HoverTint.HasValue ? (((hopBeat % bar) + bar) % bar == 0 ? 0.85f : 0.5f) : 0f;
        Color tint = HoverTint ?? Palette.Accent;
        float strength = Mathf.Clamp01(ptrSpeed / 14f);
        for (int i = 0; i < n; i++)
        {
            float f = 0f;
            if (pointerLive)
            {
                float d2 = (p[i] - ptr).sqrMagnitude;
                if (d2 < reach2) f = Mathf.Exp(-d2 * inv2s2) * Mathf.Clamp01((reach2 - d2) * 0.6f);
            }
            float oldHeat = heat[i];
            heat[i] = Mathf.Max(heat[i] * decay, f);
            float ld = isLetter[i] ? LetterDown(i) : 0f;
            if (ld > 0f && ld < 1f)
            {
                // the title sinking (a shelf opened): straight down to the plain wall's depth, eased, no spring bounce
                x[i] = Mathf.Lerp(sinkFrom[i], plainD[i], Ease.InOutCubic(ld)); v[i] = 0f;
                lit[i] = 1f; flash[i] *= fdecay;
                continue;
            }
            float tgt;
            if (isLetter[i] && ld <= 0f)
            {
                // the title rising again after a shelf: each column springs up on its turn (the intro's rise, quicker, without its flash)
                if (!titleDown && titleT < 99f)
                {
                    float rt = Mathf.Clamp01(wcol[i] / (float)(WordCols - 1)) * TitleRiseStagger;
                    if (titleT0 < rt && titleT >= rt) v[i] += 5f;
                }
                // letters keep their pose; the pointer only makes them ring (and light up)
                bool up = !revealing || revealT >= delay[i];
                if (revealing && up && revealT - dt < delay[i])
                {
                    v[i] += 6.5f; flash[i] = 1.2f; fcol[i] = Color.white;   // (no confetti: the user asked for no little pops on the title)
                }
                tgt = up ? LetterDepth + 0.22f * heat[i] : 0f;
                if (f > 0.55f && oldHeat < 0.55f)
                {
                    v[i] += 2.2f;
                    int k = wcol[i] / 6;
                    letterGlow[k] = 1f;
                    if (now - letterNoteT[k] > 0.35f && Music != null) { letterNoteT[k] = now; Music.PlayLetter(k, 0.6f); }
                }
            }
            else
            {
                float relief = isLetter[i] ? plainD[i] : baseD[i];   // (a letter down in the wall is a plain wall pin)
                if (revealing) relief *= Mathf.Clamp01((sweepX - p[i].x) / 2.5f);   // flat until the sweep passes, then the relief pops up
                tgt = relief + PointerLift * heat[i];
                if (hoverAmt > 0.01f) tgt += 0.6f * hoverAmt * Mathf.Exp(-(p[i] - hoverAt).sqrMagnitude * hov2);
                if (hop)
                {
                    float hk = hoverAmt * Mathf.Exp(-(p[i] - hoverAt).sqrMagnitude * hopK);
                    v[i] += 4.2f * hk;
                    if (flashK > 0f && hk * flashK > 0.08f && hk * flashK > flash[i]) { flash[i] = hk * flashK; fcol[i] = tint; TintFlashes++; }
                }
                if (f > 0.7f && now - touchT[i] > 0.6f && !revealing)
                {
                    touchT[i] = now;
                    flash[i] = 1f; fcol[i] = DegreeColor(deg[i] + Mathf.RoundToInt(p[i].x / Pitch) / 2);
                    Touches++;
                    if (Music != null) Music.RequestNote(deg[i], strength);
                }
            }
            for (int s = 0; s < sub; s++) { float a = w2 * (tgt - x[i]) - c2 * v[i]; v[i] += a * h; x[i] += v[i] * h; }
            lit[i] = revealing ? Mathf.Max(lit[i], Mathf.Clamp01((sweepX - p[i].x + 1f) / 3f)) : 1f;
            flash[i] *= fdecay;
        }
        // a small punch as each letter's first column lands (no comic starburst: the user asked for no little pops on the title)
        if (revealing)
        {
            LettersRisen = 0;
            for (int k = 0; k < 7; k++) if (revealT >= spec.letters0 + (k * 6) * spec.colStep) LettersRisen = k + 1;
            while (burstLetters < LettersRisen) { burstLetters++; punch = 1f; }
        }
        else LettersRisen = 7;
        wordAppear = Mathf.MoveTowards(wordAppear, revealing ? Mathf.Clamp01((revealT - spec.letters0) / (WordCols * spec.colStep)) : 1f, dt * 3f);
        UpdateGlass(revealing);
        RenderPixelGlass();
        UpdateFx(dt);
        Draw(revealing, sweepX);
    }

    void UpdatePointer(float dt)
    {
        Vector2 screen; bool on;
        if (PointerOverride.HasValue) { screen = PointerOverride.Value; on = true; }
        else
        {
            Vector3 m = Input.mousePosition;
            screen = new Vector2(m.x, m.y);
            on = m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
        }
        ptrPrev = ptr;
        Vector2 now = ScreenToWall(screen);
        float spd = ptrOn && on ? (now - ptrPrev).magnitude / Mathf.Max(dt, 1e-4f) : 0f;
        ptrSpeed = Mathf.Lerp(ptrSpeed, spd, 1f - Mathf.Exp(-dt * 12f));
        ptr = now; ptrOn = on;
        Vector2 pn = Vector2.zero;
        if (on && Screen.width > 0 && Screen.height > 0) pn = new Vector2(Mathf.Clamp(screen.x / Screen.width * 2f - 1f, -1f, 1f), Mathf.Clamp(screen.y / Screen.height * 2f - 1f, -1f, 1f));
        look = Vector2.SmoothDamp(look, pn, ref lookVel, 0.45f, 20f, dt);
    }

    float punch;
    void UpdateCamera(float dt)
    {
        if (Cam == null) return;
        float tt = Time.realtimeSinceStartup;
        float swayX = Mathf.Sin(tt * 0.21f) * 0.4f, swayY = Mathf.Sin(tt * 0.17f + 1.3f) * 0.3f;
        // orbit around the wall centre toward the pointer: the letters' sides slide into view (parallax)
        Quaternion q = Quaternion.Euler(-look.y * OrbitDeg * 0.8f + swayY, look.x * OrbitDeg + swayX, 0f);
        // the intro dollies in slowly; each landing letter gives the camera a small punch
        float dolly = revealT >= 0f && revealT <= spec.end ? 1f + 0.1f * (1f - Ease.OutCubic(Mathf.Clamp01(revealT / spec.end))) : 1f;
        punch *= Mathf.Exp(-dt * 9f);
        Vector3 pos = Origin + q * new Vector3(0f, 0f, -camDist * dolly + punch * 0.35f);
        Cam.transform.position = pos;
        Cam.transform.rotation = Quaternion.LookRotation(Origin - pos, Vector3.up);
    }

    void UpdateRipples(float dt)
    {
        for (int r = ripples.Count - 1; r >= 0; r--)
        {
            var rp = ripples[r];
            float r0 = rp.r, r1 = rp.r + RippleSpeed * dt;
            float fade = Mathf.Clamp01(1f - r1 / 30f);
            float lo = r0 * r0, hi = r1 * r1;
            for (int i = 0; i < n; i++)
            {
                float d2 = (p[i] - rp.o).sqrMagnitude;
                if (d2 <= lo || d2 > hi) continue;
                v[i] += (isLetter[i] ? 4f : 7.5f) * rp.k * fade;
                float fl = 0.85f * rp.k * fade;
                if (fl > flash[i]) { flash[i] = fl; fcol[i] = Color.Lerp(rp.c, DegreeColor(deg[i]), 0.5f); }
                RippleHits++;
            }
            rp.r = r1;
            if (r1 > 34f) ripples.RemoveAt(r); else ripples[r] = rp;
        }
    }

    void UpdateGlass(bool revealing)
    {
        float tt = Time.realtimeSinceStartup;
        float appear = 1f;
        if (revealing) appear = Ease.OutBack(Mathf.Clamp01((revealT - spec.sweep0 - spec.sweepDur * 0.4f) / 0.8f), 1.2f);
        foreach (var g in glass)
        {
            if (g.t == null) continue;
            Vector3 bob = new Vector3(Mathf.Sin(tt * 0.31f + g.phase) * 0.3f, Mathf.Sin(tt * 0.47f + g.phase * 1.7f) * 0.45f, 0f);
            Vector3 par = new Vector3(-look.x * g.par, -look.y * g.par * 0.7f, 0f);
            g.t.position = Origin + g.home + bob + par;
            g.t.rotation = Quaternion.AngleAxis(tt * g.spin, g.axis) * g.tilt;
            g.t.localScale = Vector3.one * (g.size * Mathf.Max(0.001f, appear));
            g.mat.SetFloat(FadeId, Mathf.Clamp01(appear));
        }
        // a faint glow line under the word
        if (glowLine != null)
        {
            float wa = wordAppear * titleShown * (0.85f + 0.15f * Mathf.Sin(tt * 1.3f));
            glowLine.position = Origin + new Vector3(0f, (WordRow0 - 0.5f) * Pitch - 1.0f, -LetterDepth - 0.4f);
            glowLine.rotation = Quaternion.identity;
            glowLine.localScale = new Vector3(WordCols * Pitch * 1.1f, 1.2f, 1f);
            glowMat.SetFloat("_Intensity", 0.75f * wa + (shimmerT >= 0f ? 0.35f * Mathf.Sin(Mathf.Clamp01(shimmerT / 1.8f) * Mathf.PI) : 0f));
        }
    }

    void Draw(bool revealing, float sweepX)
    {
        if (pinT == null) return;
        float tt = Time.realtimeSinceStartup;
        float shimmerX = shimmerT >= 0f ? Mathf.Lerp(-8f, WordCols + 8f, shimmerT / 1.8f) : -999f;
        float spreadL = Mathf.Max(0.5f, HoverSpread);
        float hovLight = 1f / (2f * 1.7f * 1.7f * spreadL * spreadL);
        Color hoverCol = HoverTint ?? Palette.Accent; float hoverGain = HoverTint.HasValue ? 0.34f : 0.24f;
        for (int i = 0; i < n; i++)
        {
            float xi = x[i];
            Vector3 pos = new Vector3(p[i].x, p[i].y, -xi + PinDepth * 0.5f);
            var tr = pinT[i];
            if (float.IsNaN(lastZ[i]) || Mathf.Abs(lastZ[i] - pos.z) > 0.0006f)
            {
                tr.SetLocalPositionAndRotation(pos, Quaternion.identity);
                if (lastS[i] != 1f) { tr.localScale = Vector3.one; lastS[i] = 1f; }
                lastZ[i] = pos.z;
            }
            // shared material bucket (SRP-batched): dark until the intro sweep passes, the sweep band, wall / tint, letter column
            float ldn = isLetter[i] ? LetterDown(i) : 0f;
            bool risen = isLetter[i] && ldn < 1f && (!revealing || revealT >= delay[i]);
            byte want;
            if (revealing && lit[i] < 0.5f && !risen) want = MatDark;
            else if (revealing && !risen && Mathf.Abs(p[i].x - sweepX) < 1.7f) want = MatBand;
            else if (risen) want = (byte)(MatLetter0 + wcol[i]);
            else want = tintIdx[i] >= 0 ? (byte)(MatTint0 + tintIdx[i]) : MatWall;
            if (want != curMat[i]) { pinR[i].sharedMaterial = MatOf(want); curMat[i] = want; }
            if (risen && ldn > 0f)
            {
                // sinking: the letter's colour and glow fade into the plain wall's (a property block while it lasts)
                float k = Ease.InOutCubic(ldn);
                Color wc = WordColor(wcol[i] / (float)(WordCols - 1));
                mpb.Clear();
                mpb.SetColor(BaseColorId, Color.Lerp(wc, WallBase, k));
                mpb.SetVector(EmissionId, Lin(wc, LetterGlow * (1f - k)));
                pinR[i].SetPropertyBlock(mpb);
                hasMpb[i] = true; lastEm[i] = -1f;
                continue;
            }
            // transient emission (flash / letter shimmer + hover / button hover light): a property block only while it lasts
            float fl = flash[i];
            Color fc = fcol[i];
            Vector4 em = Vector4.zero; float extra = 0f;
            if (fl > 0.02f) { var l = fc.linear; em = new Vector4(l.r * fl * 1.6f, l.g * fl * 1.6f, l.b * fl * 1.6f, 1f); extra = fl; }
            if (risen)
            {
                float k = letterGlow[wcol[i] / 6];
                float sh = shimmerT >= 0f ? Mathf.Exp(-(wcol[i] - shimmerX) * (wcol[i] - shimmerX) / 10f) : 0f;
                float boost = 0.9f * k + 1.1f * sh;
                if (boost > 0.02f || extra > 0f)
                {
                    var wc = WordColor(wcol[i] / (float)(WordCols - 1)).linear;
                    float inten = LetterGlow + boost;
                    em = new Vector4(em.x + wc.r * inten, em.y + wc.g * inten, em.z + wc.b * inten, 1f);
                    extra += boost;
                }
            }
            else if (hoverAmt > 0.02f && fl <= 0.02f)
            {
                float hk = hoverAmt * Mathf.Exp(-(p[i] - hoverAt).sqrMagnitude * hovLight);
                if (hk > 0.03f) { var ac = hoverCol.linear; em = new Vector4(ac.r * hoverGain * hk, ac.g * hoverGain * hk, ac.b * hoverGain * hk, 1f); extra = hk; }
            }
            if (extra > 0.02f)
            {
                float sig = em.x + em.y * 1.3f + em.z * 0.7f;
                if (!hasMpb[i] || Mathf.Abs(sig - lastEm[i]) > 0.004f)
                {
                    mpb.Clear();
                    mpb.SetVector(EmissionId, em);
                    pinR[i].SetPropertyBlock(mpb);
                    hasMpb[i] = true; lastEm[i] = sig;
                }
            }
            else if (hasMpb[i]) { pinR[i].SetPropertyBlock(null); hasMpb[i] = false; lastEm[i] = 0f; }
        }
    }
}
