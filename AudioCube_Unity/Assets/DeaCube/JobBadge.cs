using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SPEC v6 §7.1 (package U1): the job badges — the number a chord wears on its card, its island's platform (package K) and its rail bead
/// (the user: "a star as well as a number (like 1 2 3 4 in the chord progression … 1234 can also work as 1432 so they should know it can be
/// swapped)"). The number is the chord's JOB (Harmony.Job: 1 home, 2 away, 3 heart, 4 pull), drawn in ink on its family's shape so a player
/// can read it without the digit: 1 a little house (home), 2 a flag (away: stepping out), 3 a heart (the feelings), 4 a plate with an arrow
/// curling back over it (pull: it wants to go home). Family colours: honey, sky, rose, lilac (pastels on the cream-paper language, apart from
/// the vibe colours of the cards they sit on).
/// Glyphs (IconFactory.Register, before the first scene like VibeGlyphs): "job1".."job4" = the shape with the digit cut out (one alpha
/// channel: tinted, the digit is a window — what a single world quad shows), "jobN.shape" = the solid shape, "jobN.digit" = the digit alone
/// (the layers a card paints: <see cref="Build"/>), plus "fitStar" (+ "fitStar.1", its glint) for the cards' fit stars and "swap" (⇄) for
/// the swap chips. <see cref="WorldTexture"/> is the whole inked badge in colour (family fill, ink outline, ink digit) for a world quad.
/// The drawings mirror the offline prototype scratchpad/v6/u1/proto/jobs.py (same coordinates: [-1, 1], +y up, negative inside).
/// </summary>
public static class JobBadge
{
    /// <summary>The badge glyphs (IconFactory names; index = job, 0 unused).</summary>
    public static readonly string[] Names = { "", "job1", "job2", "job3", "job4" };
    public const string StarGlyph = "fitStar", StarGlint = "fitStar.1", SwapGlyph = "swap";
    /// <summary>The fit star's gold.</summary>
    public static readonly Color StarGold = new Color(1f, 0.80f, 0.22f);

    // the family colours (job 1..4): honey, sky, rose, lilac
    static readonly Color[] Fills =
    {
        new Color(0.92f, 0.90f, 0.88f),
        new Color(1.00f, 0.84f, 0.47f),
        new Color(0.60f, 0.85f, 0.98f),
        new Color(1.00f, 0.64f, 0.72f),
        new Color(0.78f, 0.68f, 1.00f),
    };
    // where each shape holds its digit: centre x, centre y, height
    static readonly Vector3[] DigitBox =
    {
        Vector3.zero,
        new Vector3(0f, -0.2f, 0.88f),
        new Vector3(0.06f, 0.22f, 0.74f),
        new Vector3(0f, 0.05f, 0.78f),
        new Vector3(0.1f, -0.13f, 0.7f),
    };

    public static bool Registered { get; private set; }

    public static bool Valid(int job) => job >= 1 && job <= 4;
    /// <summary>The badge glyph of <paramref name="job"/> (the shape with the digit as a window); "" for 0 (keyboards, Moons).</summary>
    public static string Glyph(int job) => Valid(job) ? Names[job] : "";
    public static string ShapeGlyph(int job) => Valid(job) ? Names[job] + ".shape" : "";
    public static string DigitGlyph(int job) => Valid(job) ? Names[job] + ".digit" : "";
    /// <summary>The family colour of <paramref name="job"/> (the badge's fill).</summary>
    public static Color Tint(int job) => Fills[Valid(job) ? job : 0];

    /// <summary>The card / rail / chip badge of <paramref name="job"/>, <paramref name="px"/> px big (the rect is Comic.GlyphPad larger for the
    /// ink outline), centred on <paramref name="parent"/>: the inked shape tinted <see cref="Tint"/> (child "Shape") and the ink digit on top
    /// (child "Digit"). No raycasts. Job 0 builds it hidden (<see cref="Set"/> can show it later).</summary>
    public static RectTransform Build(RectTransform parent, int job, float px)
    {
        Ensure();
        float s = px * Comic.GlyphPad;
        var root = HudKit.Node(parent, "JobBadge", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s, s));
        var shape = Comic.GlyphImage(root, "Shape", Valid(job) ? ShapeGlyph(job) : ShapeGlyph(1), Tint(job), px, true);
        var digit = Comic.GlyphImage(root, "Digit", Valid(job) ? DigitGlyph(job) : DigitGlyph(1), Comic.Ink, px, false);
        shape.raycastTarget = false; digit.raycastTarget = false;
        root.gameObject.SetActive(Valid(job));
        return root;
    }

    /// <summary>Re-points a badge made by <see cref="Build"/> at another job (pooled beads); job 0 hides it.</summary>
    public static void Set(RectTransform badge, int job, float px)
    {
        if (badge == null) return;
        bool on = Valid(job);
        if (badge.gameObject.activeSelf != on) badge.gameObject.SetActive(on);
        if (!on) return;
        var shape = badge.Find("Shape") != null ? badge.Find("Shape").GetComponent<Image>() : null;
        var digit = badge.Find("Digit") != null ? badge.Find("Digit").GetComponent<Image>() : null;
        if (shape != null) { shape.sprite = Comic.Glyph(ShapeGlyph(job), px, true); shape.color = Tint(job); }
        if (digit != null) { digit.sprite = Comic.Glyph(DigitGlyph(job), px, false); digit.color = Comic.Ink; }
    }

    /// <summary>The job a badge made by <see cref="Build"/> shows (0 hidden) — tests.</summary>
    public static int JobShown(RectTransform badge)
    {
        if (badge == null || !badge.gameObject.activeSelf) return 0;
        var t = badge.Find("Shape"); var img = t != null ? t.GetComponent<Image>() : null;
        string n = img != null && img.sprite != null ? img.sprite.name : "";
        for (int j = 1; j <= 4; j++) if (n.StartsWith("ink_" + Names[j] + ".shape|", System.StringComparison.Ordinal)) return j;
        return 0;
    }

    // ------------------------------------------------------------------ the world badge (package K's platforms)
    static readonly Texture2D[] world = new Texture2D[5];
    /// <summary>The whole badge of <paramref name="job"/> as one colour texture (128², mipmapped, straight alpha): the family-tinted shape, an
    /// ink outline around it and the ink digit — draw it on a flat quad with Fx.Alpha(JobBadge.WorldTexture(n), Color.white). The glyph fills
    /// the inner 1 / Comic.GlyphPad of the texture (the outline needs the margin), like the inked UI glyphs. Null for job 0.</summary>
    public static Texture2D WorldTexture(int job)
    {
        if (!Valid(job)) return null;
        if (world[job] != null) return world[job];
        const int n = 128;
        const float outline = 0.1f;               // ink band around the shape, glyph units
        float aa = 2.4f / n * Comic.GlyphPad;
        Color fill = Tint(job), ink = Comic.Ink;
        var px = new Color32[n * n];
        var box = DigitBox[job];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f) * Comic.GlyphPad;
                float ds = Shape(p, job), dd = Digit(p, job, box.x, box.y, box.z);
                float inside = Mathf.Clamp01(0.5f - ds / aa), outer = Mathf.Clamp01(0.5f - (ds - outline) / aa), dig = Mathf.Clamp01(0.5f - dd / aa);
                Color c = Color.Lerp(ink, fill, inside);
                c = Color.Lerp(c, ink, dig);
                c.a = outer;
                px[y * n + x] = c;
            }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { name = "jobBadge" + job, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
        tex.SetPixels32(px);
        tex.Apply(true, false);
        world[job] = tex;
        return tex;
    }

    // ------------------------------------------------------------------ registration
    /// <summary>Registers the glyphs now (idempotent; boot does it before the first scene).</summary>
    public static void Ensure() { if (!Registered || !IconFactory.Has(Names[1])) Register(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        for (int j = 1; j <= 4; j++)
        {
            int job = j;
            var b = DigitBox[job];
            IconFactory.Register(Names[job], p => IconFactory.P.Sub(Shape(p, job), Digit(p, job, b.x, b.y, b.z)));
            IconFactory.Register(Names[job] + ".shape", p => Shape(p, job));
            IconFactory.Register(Names[job] + ".digit", p => Digit(p, job, b.x, b.y, b.z));
        }
        IconFactory.Register(StarGlyph, p => IconFactory.P.Star(new Vector2(p.x, p.y + 0.04f), 5, 0.92f, 0.45f) - 0.05f);
        IconFactory.Register(StarGlint, p => IconFactory.P.Sub(IconFactory.P.Circle(p, -0.22f, 0.2f, 0.13f), IconFactory.P.Circle(p, -0.14f, 0.12f, 0.13f)));
        IconFactory.Register(SwapGlyph, p => Mathf.Min(
            Mathf.Min(IconFactory.P.Seg(p, -0.62f, 0.32f, 0.36f, 0.32f, 0.12f), IconFactory.P.Head(p, 0.86f, 0.32f, 0f, 0.46f)),
            Mathf.Min(IconFactory.P.Seg(p, 0.62f, -0.32f, -0.36f, -0.32f, 0.12f), IconFactory.P.Head(p, -0.86f, -0.32f, 180f, 0.46f))));
        Registered = true;
    }

    // ------------------------------------------------------------------ the drawings
    /// <summary>The family shape of <paramref name="job"/> (signed distance, glyph units).</summary>
    public static float Shape(Vector2 p, int job)
    {
        switch (job)
        {
            case 1: return House(p);
            case 2: return Flag(p);
            case 3: return Heart(p);
            case 4: return Curl(p);
        }
        return IconFactory.P.Circle(p, 0f, 0f, 0.6f);
    }

    static float House(Vector2 p)
    {
        // a little house: the body, a pitched roof, a chimney
        float body = IconFactory.P.Box(p, 0f, -0.28f, 0.62f, 0.52f, 0.07f);
        float roof = IconFactory.P.Tri(p, -0.86f, 0.12f, 0.86f, 0.12f, 0f, 0.84f) - 0.05f;
        float chim = IconFactory.P.Box(p, 0.46f, 0.46f, 0.1f, 0.2f, 0.03f);
        return Mathf.Min(body, Mathf.Min(roof, chim));
    }

    static Vector2[] flagPts;
    static float Flag(Vector2 p)
    {
        // a flag: a pole with a knob and a waving pennant with a swallowtail notch
        if (flagPts == null)
        {
            const int n = 12; const float x0 = -0.62f, x1 = 0.86f;
            var pts = new Vector2[2 * (n + 1) + 1];
            int k = 0;
            for (int i = 0; i <= n; i++) { float x = x0 + (x1 - x0) * i / n; pts[k++] = new Vector2(x, 0.72f + 0.07f * Mathf.Sin((x - x0) * 3.6f)); }
            pts[k++] = new Vector2(0.62f, 0.23f + 0.07f * Mathf.Sin((0.62f - x0) * 3.6f) * 0.5f);
            for (int i = n; i >= 0; i--) { float x = x0 + (x1 - x0) * i / n; pts[k++] = new Vector2(x, -0.28f + 0.07f * Mathf.Sin((x - x0) * 3.6f)); }
            flagPts = pts;
        }
        float pole = Mathf.Min(IconFactory.P.Seg(p, -0.66f, -0.9f, -0.66f, 0.76f, 0.075f), IconFactory.P.Circle(p, -0.66f, 0.8f, 0.12f));
        return Mathf.Min(pole, IconFactory.P.Poly(p, flagPts) - 0.03f);
    }

    static float Heart(Vector2 p)
    {
        // two round lobes and a soft point
        float lobes = Mathf.Min(IconFactory.P.Circle(p, -0.38f, 0.27f, 0.44f), IconFactory.P.Circle(p, 0.38f, 0.27f, 0.44f));
        return Mathf.Min(lobes, IconFactory.P.Tri(p, -0.8f, 0.14f, 0.8f, 0.14f, 0f, -0.8f) - 0.06f);
    }

    static float Curl(Vector2 p)
    {
        // a plate with an arrow curling over it and back down (↶: it wants to go home)
        const float cx = 0.1f, cy = -0.1f, R = 0.76f, a1 = 172f, size = 0.44f;
        float plate = IconFactory.P.Circle(p, cx, cy - 0.03f, 0.53f);
        float band = IconFactory.P.Arc(p, cx, cy, R, 0.1f, -18f, a1);
        float ex = cx + R * Mathf.Cos(a1 * Mathf.Deg2Rad), ey = cy + R * Mathf.Sin(a1 * Mathf.Deg2Rad), d = (a1 + 90f) * Mathf.Deg2Rad;
        float head = IconFactory.P.Head(p, ex + Mathf.Cos(d) * size * 0.85f, ey + Mathf.Sin(d) * size * 0.85f, a1 + 90f, size) - 0.02f;
        return Mathf.Min(plate, Mathf.Min(band, head));
    }

    /// <summary>A bold rounded digit 1..4 centred on (<paramref name="cx"/>, <paramref name="cy"/>), <paramref name="h"/> tall (signed distance).</summary>
    public static float Digit(Vector2 p, int n, float cx, float cy, float h)
    {
        const float s = 0.13f, t = 0.5f - s;
        switch (n)
        {
            case 1:
                return Mathf.Min(DSeg(p, cx, cy, h, 0.07f, t, 0.07f, -t, s), Mathf.Min(DSeg(p, cx, cy, h, 0.07f, t, -0.19f, 0.27f, s), DSeg(p, cx, cy, h, -0.2f, -t, 0.33f, -t, s)));
            case 2:
            {
                const float r = 0.235f; float ay = t - r;
                float ex = r * Mathf.Cos(-38f * Mathf.Deg2Rad), ey = ay + r * Mathf.Sin(-38f * Mathf.Deg2Rad);
                return Mathf.Min(DArc(p, cx, cy, h, 0f, ay, r, s, -38f, 168f), Mathf.Min(DSeg(p, cx, cy, h, ex, ey, -0.25f, -t, s), DSeg(p, cx, cy, h, -0.25f, -t, 0.3f, -t, s)));
            }
            case 3:
            {
                const float r1 = 0.2f, r2 = 0.235f;
                return Mathf.Min(DArc(p, cx, cy, h, -0.01f, t - r1, r1, s, -90f, 155f), DArc(p, cx, cy, h, 0f, -t + r2, r2, s, -160f, 90f));
            }
            case 4:
                return Mathf.Min(DSeg(p, cx, cy, h, 0.15f, t, 0.15f, -t, s), Mathf.Min(DSeg(p, cx, cy, h, 0.15f, t, -0.3f, -0.1f, s), DSeg(p, cx, cy, h, -0.3f, -0.1f, 0.33f, -0.1f, s)));
        }
        return 9f;
    }

    static float DSeg(Vector2 p, float cx, float cy, float h, float ax, float ay, float bx, float by, float s)
        => IconFactory.P.Seg(p, cx + ax * h, cy + ay * h, cx + bx * h, cy + by * h, s * h);
    static float DArc(Vector2 p, float cx, float cy, float h, float ux, float uy, float r, float s, float a0, float a1)
        => IconFactory.P.Arc(p, cx + ux * h, cy + uy * h, r * h, s * h, a0, a1);
}
