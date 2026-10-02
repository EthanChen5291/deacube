using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 (SPEC v7 §13.3, §14.3, §17.1; package B): the structure look's shared pieces — the beat-pip texture and materials (a cream disc in an ink
/// ring: dim at rest, the downbeat a little stronger, the playing beat Comic.Pop yellow), the stairs' direction arrows, the phrase roll's ink
/// lines, piano keys, tonic houses, chord-tint bands and safe-note dots, the ghost-measure dash (the section-name icons are U1's SectionRoles).
/// Everything is built once and shared (no per-island materials: tints go through MaterialPropertyBlocks), so a rebuilt island leaks nothing.
/// </summary>
public static class StructureLook
{
    public const string PipGlyph = "b7.pip", DotGlyph = "b7.dot", ArrowDownGlyph = "b7.arrowDown", ArrowUpGlyph = "b7.arrowUp";
    /// <summary>The beat pips' tints: at rest (a beat, a downbeat), the playing beat, a ghost measure's.</summary>
    public static readonly Color PipRest = new Color(0.95f, 0.91f, 0.98f, 0.74f), PipDownRest = new Color(1f, 0.96f, 1f, 0.95f),
        PipLitColor = new Color(1f, 0.83f, 0.30f, 1f), PipGhostColor = new Color(0.93f, 0.89f, 0.97f, 0.26f);
    /// <summary>v7 §19.1: a draft's fill on the pips — the used beats (a steady mint), the next note's span (a ghost of it), the part of the next note
    /// that would pass the grid's length (coral).</summary>
    public static readonly Color PipFillColor = new Color(0.20f, 0.84f, 0.60f, 1f), PipNextColor = new Color(0.62f, 1f, 0.84f, 0.62f), PipOverColor = new Color(1f, 0.40f, 0.42f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        IconFactory.Register(DotGlyph, p => IconFactory.P.Circle(p, 0f, 0f, 0.7f));
        // the run's direction: a bold arrow, shaft and head (↓ a fall, ↑ a climb)
        IconFactory.Register(ArrowDownGlyph, p => Mathf.Min(IconFactory.P.Seg(p, 0f, 0.78f, 0f, -0.2f, 0.17f), IconFactory.P.Head(p, 0f, -0.86f, -90f, 0.66f) - 0.04f));
        IconFactory.Register(ArrowUpGlyph, p => Mathf.Min(IconFactory.P.Seg(p, 0f, -0.78f, 0f, 0.2f, 0.17f), IconFactory.P.Head(p, 0f, 0.86f, 90f, 0.66f) - 0.04f));
    }

    // ------------------------------------------------------------------ textures
    static Texture2D pipTex, dashTex, lineTex;
    /// <summary>The playhead's soft line: bright along its length (the ends fade in the last tenth), soft across it.</summary>
    public static Texture2D SoftLineTexture
    {
        get
        {
            if (lineTex != null) return lineTex;
            const int w = 32, h = 64;
            lineTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "b7_softline", hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / w * 2f - 1f), v = (y + 0.5f) / h;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - u), 1.7f) * Mathf.Clamp01(v / 0.1f) * Mathf.Clamp01((1f - v) / 0.1f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            lineTex.SetPixels32(px);
            lineTex.Apply(false, false);
            return lineTex;
        }
    }
    /// <summary>The pip: a cream-white disc in an ink ring (colour texture, straight alpha; a material tint multiplies it).</summary>
    public static Texture2D PipTexture
    {
        get
        {
            if (pipTex != null) return pipTex;
            const int n = 64;
            pipTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "b7_pip", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            Color ink = Look.InkColor;
            float aa = 2.2f / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    float outer = Mathf.Clamp01((0.94f - r) / aa + 0.5f), inner = Mathf.Clamp01((0.68f - r) / aa + 0.5f);
                    Color c = Color.Lerp(ink, Color.white, inner);
                    c.a = outer;
                    px[y * n + x] = c;
                }
            pipTex.SetPixels32(px);
            pipTex.Apply(true, false);
            return pipTex;
        }
    }

    /// <summary>The ghost measures' dashed line (repeat-wrapped along the line: a dash, then a gap).</summary>
    public static Texture2D DashTexture
    {
        get
        {
            if (dashTex != null) return dashTex;
            const int n = 32;
            dashTex = new Texture2D(n, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "b7_dash", hideFlags = HideFlags.DontSave };
            var px = new Color32[n * 2];
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                byte a = (byte)(u < 0.58f ? 255 : 0);
                px[x] = new Color32(255, 255, 255, a); px[n + x] = px[x];
            }
            dashTex.SetPixels32(px);
            dashTex.Apply(false, false);
            return dashTex;
        }
    }

    // ------------------------------------------------------------------ shared materials (rebuilt when Unity destroyed them: a new Play session)
    static Material pipRest, pipDownRest, pipLit, pipGhost, arrowUp, arrowDown, inkBar, inkThin, pianoIvory, pianoEbony, houseInk, houseCream, band, dot, landing,
        pipFill, pipNext, pipOver, pipGlow, nosing;
    public static Material PipFillMaterial => pipFill != null ? pipFill : (pipFill = Fx.Alpha(PipTexture, PipFillColor));
    public static Material PipNextMaterial => pipNext != null ? pipNext : (pipNext = Fx.Alpha(PipTexture, PipNextColor));
    public static Material PipOverMaterial => pipOver != null ? pipOver : (pipOver = Fx.Alpha(PipTexture, PipOverColor));
    /// <summary>The playing pip's halo (additive warm light).</summary>
    public static Material PipGlowMaterial => pipGlow != null ? pipGlow : (pipGlow = Fx.Additive(IconFactory.GetTexture("glow"), PipLitColor, 1.1f));
    /// <summary>The stairs' nosing (the pale lip along each tread's front edge).</summary>
    public static Material Nosing => nosing != null ? nosing : (nosing = Fx.Lit(new Color(1f, 0.97f, 0.93f), 0.3f, 0f));
    public static Material PipRestMaterial => pipRest != null ? pipRest : (pipRest = Fx.Alpha(PipTexture, PipRest));
    public static Material PipDownMaterial => pipDownRest != null ? pipDownRest : (pipDownRest = Fx.Alpha(PipTexture, PipDownRest));
    public static Material PipLitMaterial => pipLit != null ? pipLit : (pipLit = Fx.Alpha(PipTexture, PipLitColor));
    public static Material PipGhostMaterial => pipGhost != null ? pipGhost : (pipGhost = Fx.Alpha(PipTexture, PipGhostColor));
    /// <summary>The stairs' direction glyph (ink on the stone).</summary>
    public static Material ArrowMaterial(bool up)
    {
        if (up) return arrowUp != null ? arrowUp : (arrowUp = Fx.Alpha(IconFactory.GetTexture(ArrowUpGlyph), Palette.A(Look.InkColor, 0.86f)));
        return arrowDown != null ? arrowDown : (arrowDown = Fx.Alpha(IconFactory.GetTexture(ArrowDownGlyph), Palette.A(Look.InkColor, 0.86f)));
    }
    /// <summary>The phrase's bold measure lines and thin beat lines (flat ink, no outline pass).</summary>
    public static Material InkBar => inkBar != null ? inkBar : (inkBar = Fx.Lit(new Color(0.16f, 0.11f, 0.24f), 0.1f, 0f, false));
    public static Material InkThin => inkThin != null ? inkThin : (inkThin = Fx.Lit(new Color(0.26f, 0.21f, 0.36f), 0.1f, 0f, false));
    /// <summary>A multi-bar stair's landing (the stone of its steps, a touch deeper).</summary>
    public static Material LandingStone => landing != null ? landing : (landing = Fx.Lit(new Color(0.84f, 0.77f, 0.76f), 0.3f, 0f));
    public static Material PianoIvory => pianoIvory != null ? pianoIvory : (pianoIvory = Fx.Lit(new Color(1f, 0.95f, 0.86f), 0.3f, 0f));
    public static Material PianoEbony => pianoEbony != null ? pianoEbony : (pianoEbony = Fx.Lit(new Color(0.24f, 0.19f, 0.29f), 0.3f, 0f));
    public static Material HouseInk => houseInk != null ? houseInk : (houseInk = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(Look.InkColor, 0.86f)));
    public static Material HouseCream => houseCream != null ? houseCream : (houseCream = Fx.Alpha(IconFactory.GetTexture(Vibe.HomeIcon), Palette.A(new Color(1f, 0.95f, 0.84f), 0.95f)));
    /// <summary>A flat colour quad (the phrase's chord tints: the colour comes per renderer through <see cref="Tint"/>).</summary>
    public static Material BandMaterial => band != null ? band : (band = Fx.Alpha(IconFactory.GetTexture("white"), Color.white));
    /// <summary>The safe-note dot (tinted per renderer).</summary>
    public static Material DotMaterial => dot != null ? dot : (dot = Fx.Alpha(IconFactory.GetTexture(DotGlyph), Color.white));

    static MaterialPropertyBlock tintBlock;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    /// <summary>Tints one renderer of an Fx.Alpha material (DeaCube/AlphaBlend's _Color) without a new material.</summary>
    public static void Tint(Renderer r, Color c)
    {
        if (r == null) return;
        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(tintBlock);
        tintBlock.SetColor(ColorId, c);
        r.SetPropertyBlock(tintBlock);
    }
    /// <summary>The colour a renderer's block tints it with (tests); <paramref name="fallback"/> when none.</summary>
    public static Color TintOf(Renderer r, Color fallback)
    {
        if (r == null || !r.HasPropertyBlock()) return fallback;
        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(tintBlock);
        return tintBlock.GetColor(ColorId);
    }

    /// <summary>A flat decal quad (lying face up) under <paramref name="parent"/>: no shadows, no collider.</summary>
    public static Transform Decal(Transform parent, string name, Vector3 local, float size, Material m)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = local;
        g.transform.localScale = new Vector3(size, 1f, size);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        return g.transform;
    }

    /// <summary>A standing decal quad (the XY plane facing −z, pivot at its bottom centre) under <paramref name="parent"/>.</summary>
    public static Transform StandingDecal(Transform parent, string name, Vector3 bottomCentre, float size, Material m)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = bottomCentre;
        g.transform.localScale = new Vector3(size, size, 1f);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
        var r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        return g.transform;
    }

    /// <summary>
    /// The beat pip of island <paramref name="kb"/> that lights at song beat <paramref name="beat"/> (−1 none): while the island plays (its passes:
    /// start = startBeatOffset, a pass = LengthBeats, a phrase's = phraseBeats), the pip of the beat the song is in. Pure (tests sample it).
    /// </summary>
    public static int PipAt(KeyBlock kb, double beat, bool playing)
    {
        if (kb == null || kb.IsMoon || !playing) return -1;
        double s = kb.startBeatOffset;
        double len = kb.IsPhrase ? Mathf.Max(1, kb.phraseBeats) : kb.LengthBeats;
        if (len <= 0.0) return -1;
        int passes = kb.Passes;
        if (beat < s - 1e-6 || beat >= s + len * passes - 1e-6) return -1;
        double inPass = (beat - s) - System.Math.Floor((beat - s + 1e-7) / len) * len;
        int n = kb.IsPhrase ? Mathf.Max(1, kb.phraseBeats) : Mathf.RoundToInt((float)len);
        return Mathf.Clamp((int)System.Math.Floor(inPass + 1e-6), 0, Mathf.Max(0, n - 1));
    }
}

/// <summary>
/// v7 (SPEC v7 §13.3, package B): the structure marks one grid wears — its BEAT PIPS (built by KeyBlock.BuildPips, lit here: the playing beat's
/// pip turns Comic.Pop yellow and pops a little while the island plays, a pure function of the song beat: seeks and loops land right) and a
/// phrase's chord-tint bands (KeyBlock.RefreshPhraseChords). A child of the island: K's Clear destroys it with the build.
/// </summary>
[DefaultExecutionOrder(900)]
public class StructureMarks : MonoBehaviour
{
    KeyBlock kb;
    readonly List<Renderer> pips = new List<Renderer>();
    readonly List<Vector3> pipBase = new List<Vector3>();
    bool[] pipDown = new bool[0];
    bool pipFace;
    int lit = -1;
    float litAge;
    float fillBeats = -1f, nextBeats;
    Transform halo;
    Transform bandRoot;
    readonly List<Renderer> bands = new List<Renderer>();
    readonly List<Color> bandColors = new List<Color>();

    public KeyBlock Island => kb;
    /// <summary>Pips on this grid (one per beat of its length).</summary>
    public int PipCount => pips.Count;
    /// <summary>The pip lit now (−1 none).</summary>
    public int LitPip => lit;
    /// <summary>True when pip <paramref name="i"/> is a downbeat (drawn bigger).</summary>
    public bool PipIsDown(int i) => i >= 0 && i < pipDown.Length && pipDown[i];
    /// <summary>Pip <paramref name="i"/>'s size at rest (world units).</summary>
    public float PipRestSize(int i) => i >= 0 && i < pipBase.Count ? pipBase[i].x : 0f;
    /// <summary>Pip <paramref name="i"/>'s transform (tests / U1).</summary>
    public Transform Pip(int i) => i >= 0 && i < pips.Count && pips[i] != null ? pips[i].transform : null;
    /// <summary>The material pip <paramref name="i"/> shows now (tests).</summary>
    public Material PipMaterial(int i) => i >= 0 && i < pips.Count && pips[i] != null ? pips[i].sharedMaterial : null;
    /// <summary>§19.1: the draft fill shown (beats from the grid's start; −1 none) and where the next note would END (beats from the grid's start).</summary>
    public float FillBeats => fillBeats;
    public float NextBeats => nextBeats;
    /// <summary>§19.1: pip <paramref name="i"/>'s fill state — 0 rest, 1 used by the draft, 2 under the next note, 3 the next note past the grid's end.</summary>
    public int FillStateOf(int i)
    {
        int n = pips.Count;
        if (fillBeats < 0f || i < 0 || i >= n) return 0;
        float end = nextBeats;
        bool next = end > fillBeats + 1e-4f;
        bool over = next && end > n + 1e-3f;                         // the next note would pass the grid's length (capacity)
        if (over && i == n - 1 && fillBeats >= n - 1e-3f) return 3;  // a full grid: its last pip says so
        if (fillBeats >= i + 1f - 1e-3f) return 1;                   // a used beat
        if (fillBeats > i + 1e-3f) return over ? 3 : 1;              // a partly used beat (the next note starts in it)
        if (next && end > i + 1e-3f) return over ? 3 : 2;
        return 0;
    }
    /// <summary>The phrase's chord-tint bands (tests).</summary>
    public int BandCount { get { int n = 0; foreach (var b in bands) if (b != null && b.gameObject.activeSelf) n++; return n; } }
    public Color BandColor(int i) => i >= 0 && i < bandColors.Count ? bandColors[i] : Color.clear;
    public Renderer Band(int i) => i >= 0 && i < bands.Count ? bands[i] : null;

    public void Bind(KeyBlock owner) { kb = owner; }

    /// <summary>Builds the pips at island-local <paramref name="pos"/> (flat on the margin, or standing on the platform's front face); a live
    /// resize calls it again (the draft fill carries over).</summary>
    public void BuildPips(Vector3[] pos, bool[] down, bool face, float size, float downSize)
    {
        foreach (var p in pips) if (p != null) Destroy(p.gameObject);
        pips.Clear(); pipBase.Clear();
        pipDown = down ?? new bool[0];
        pipFace = face;
        lit = -1;
        var old = transform.Find("Pips");
        if (old != null) Destroy(old.gameObject);
        var root = new GameObject("Pips").transform;
        root.SetParent(transform, false);
        for (int i = 0; i < pos.Length; i++)
        {
            bool d = i < pipDown.Length && pipDown[i];
            float s = d ? downSize : size;
            var m = d ? StructureLook.PipDownMaterial : StructureLook.PipRestMaterial;
            Transform t = face ? StructureLook.StandingDecal(root, "Pip" + i, pos[i] - new Vector3(0f, s * 0.5f, 0f), s, m)
                               : StructureLook.Decal(root, "Pip" + i, pos[i], s, m);
            pips.Add(t.GetComponent<Renderer>());
            pipBase.Add(new Vector3(s, pos[i].y, 0f));
        }
        if (halo == null)
        {
            halo = face ? StructureLook.StandingDecal(transform, "PipHalo", Vector3.zero, 1f, StructureLook.PipGlowMaterial)
                        : StructureLook.Decal(transform, "PipHalo", Vector3.zero, 1f, StructureLook.PipGlowMaterial);
        }
        halo.gameObject.SetActive(false);
        for (int i = 0; i < pips.Count; i++) SetPipLook(i);
    }

    /// <summary>§19.1: the draft fill (see KeyBlock.SetDraftFill: the used beats and the next note's END, both from the grid's start);
    /// <paramref name="beats"/> &lt; 0 clears it.</summary>
    public void SetFill(float beats, float next)
    {
        if (beats < 0f) { beats = -1f; next = 0f; }
        next = Mathf.Max(beats, next);
        if (Mathf.Abs(beats - fillBeats) < 1e-4f && Mathf.Abs(next - nextBeats) < 1e-4f) return;
        fillBeats = beats; nextBeats = next;
        for (int i = 0; i < pips.Count; i++) SetPipLook(i);
    }

    /// <summary>A pip's look now: lit (the playing beat) › the draft fill (used / next / past the end) › rest (a downbeat stronger).</summary>
    void SetPipLook(int i)
    {
        var r = i >= 0 && i < pips.Count ? pips[i] : null;
        if (r == null) return;
        float f = 1f;
        Material m;
        if (i == lit) { m = StructureLook.PipLitMaterial; f = 1.22f; }
        else
        {
            switch (FillStateOf(i))
            {
                case 1: m = StructureLook.PipFillMaterial; f = 0.78f + 0.4f * Mathf.Clamp01(fillBeats - i); break;   // a partly used beat: a smaller fill
                case 2: m = StructureLook.PipNextMaterial; f = 1.05f; break;
                case 3: m = StructureLook.PipOverMaterial; f = 1.1f; break;
                default: m = PipIsDown(i) ? StructureLook.PipDownMaterial : StructureLook.PipRestMaterial; break;
            }
        }
        if (r.sharedMaterial != m) r.sharedMaterial = m;
        Size(i, f);
    }

    /// <summary>A phrase's chord-tint bands: <paramref name="n"/> of them (pooled).</summary>
    public void SetBandCount(int n)
    {
        if (bandRoot == null) { bandRoot = new GameObject("ChordTints").transform; bandRoot.SetParent(transform, false); }
        while (bands.Count < n)
        {
            var t = StructureLook.Decal(bandRoot, "Tint" + bands.Count, Vector3.zero, 1f, StructureLook.BandMaterial);
            bands.Add(t.GetComponent<Renderer>());
            bandColors.Add(Color.clear);
        }
        for (int i = 0; i < bands.Count; i++) if (bands[i] != null && bands[i].gameObject.activeSelf != (i < n)) bands[i].gameObject.SetActive(i < n);
    }

    /// <summary>Band <paramref name="i"/>: island-local centre, size (x, z), colour.</summary>
    public void SetBand(int i, Vector3 centre, Vector2 size, Color c)
    {
        if (i < 0 || i >= bands.Count || bands[i] == null) return;
        var t = bands[i].transform;
        t.localPosition = centre;
        t.localScale = new Vector3(Mathf.Max(0.01f, size.x), 1f, Mathf.Max(0.01f, size.y));
        c.a = 1f;
        StructureLook.Tint(bands[i], c);
        bandColors[i] = c;
    }

    void LateUpdate()
    {
        if (kb == null || pips.Count == 0) return;
        int want = StructureLook.PipAt(kb, GlobalClock.SongBeatD, GlobalClock.IsPlaying);
        if (want >= pips.Count) want = -1;
        if (want != lit)
        {
            int prev = lit;
            lit = want; litAge = 0f;
            SetPipLook(prev);
            SetPipLook(lit);
            if (halo != null)
            {
                bool on = lit >= 0 && pips[lit] != null;
                if (halo.gameObject.activeSelf != on) halo.gameObject.SetActive(on);
                if (on)
                {
                    float hs = pipBase[lit].x * 2.6f;
                    var p = pips[lit].transform.localPosition;
                    if (pipFace) { halo.localPosition = new Vector3(p.x, pipBase[lit].y - hs * 0.5f, p.z - 0.004f); halo.localScale = new Vector3(hs, hs, 1f); }
                    else { halo.localPosition = new Vector3(p.x, p.y - 0.004f, p.z); halo.localScale = new Vector3(hs, 1f, hs); }
                }
            }
        }
        if (lit >= 0 && litAge < 0.3f)
        {
            litAge += Time.deltaTime;
            float k = Mathf.Clamp01(litAge / 0.22f);
            Size(lit, 1.22f + 0.32f * (1f - k) * (1f - k));   // a quick pop, then a slightly bigger rest while lit
        }
    }

    void Size(int i, float f)
    {
        if (i < 0 || i >= pips.Count) return;
        var r = pips[i]; if (r == null) return;
        float s = pipBase[i].x * f;
        var t = r.transform;
        if (pipFace) { t.localScale = new Vector3(s, s, 1f); var p = t.localPosition; p.y = pipBase[i].y - s * 0.5f; t.localPosition = p; }
        else t.localScale = new Vector3(s, 1f, s);
    }
}
