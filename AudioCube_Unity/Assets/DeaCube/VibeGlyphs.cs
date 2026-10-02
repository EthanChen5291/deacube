using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SPEC v5 §7 (package U): the vibe glyphs — the pictures a non-musician remembers a chord by (the user: "rather than random we should assign
/// some sort of meaning to them so that nonmusical users can remember certain cards or shapes and stuff in terms of their vibe"). One weather
/// picture per <see cref="VibeKind"/>, bold enough for 14 px on an island's platform and characterful at 60 px on a card: sunny = a sun,
/// dreamy = a rainbow on two puffs, rainy = a cloud with three drops, moonlit = a crescent with a star, stormy = a slate cloud split by a bolt,
/// spicy = a chili pepper, floaty = a balloon on a string. Registered through IconFactory.Register before the first scene loads, so islands
/// built at boot already wear them (<see cref="Vibe.Icon"/> returns these names once <see cref="Registered"/>).
/// Each glyph also has accent layers (the drops, the bolt, the star …) so a card can paint it in two or three colours: <see cref="Build"/> lays
/// the inked silhouette (tinted <see cref="Fill"/>) and the plain accent layers exactly on top of each other. Everywhere else (platforms, the
/// header, the chord wheel) the silhouette is used alone, tinted by its owner.
/// The drawings mirror the offline prototype scratchpad/v5/u/glyphs/final.py (same coordinates: [-1, 1], +y up, negative inside).
/// </summary>
public static class VibeGlyphs
{
    /// <summary>The silhouettes (IconFactory names), <see cref="VibeKind"/> order.</summary>
    public static readonly string[] Names = { "vibeSunny", "vibeDreamy", "vibeRainy", "vibeMoonlit", "vibeStormy", "vibeSpicy", "vibeFloaty" };

    // the card colours: [vibe][layer] — layer 0 fills the whole silhouette, the later layers paint their part over it
    static readonly Color[][] LayerColors =
    {
        new[] { new Color(1.00f, 0.74f, 0.24f) },                                                               // a warm gold sun
        new[] { new Color(1.00f, 1.00f, 1.00f), new Color(1.00f, 0.80f, 0.52f), new Color(0.62f, 0.86f, 1.00f) }, // white puffs, peach and sky bands
        new[] { new Color(1.00f, 0.97f, 0.93f), new Color(0.22f, 0.45f, 0.92f) },                               // a paper cloud, blue drops
        new[] { new Color(1.00f, 0.92f, 0.60f), new Color(1.00f, 1.00f, 1.00f) },                               // a pale moon, a white star
        new[] { new Color(0.44f, 0.43f, 0.54f), new Color(1.00f, 0.86f, 0.25f) },                               // a slate cloud, a yellow bolt
        new[] { new Color(0.92f, 0.20f, 0.20f), new Color(0.40f, 0.76f, 0.30f) },                               // a red pod, a green cap
        new[] { new Color(0.84f, 0.80f, 1.00f), new Color(1.00f, 1.00f, 1.00f), Look.InkColor },                // a lilac balloon, its shine, an ink string
    };

    public static bool Registered { get; private set; }

    /// <summary>Layers of vibe <paramref name="v"/>'s card picture (1 = the silhouette only).</summary>
    public static int LayerCount(VibeKind v) => LayerColors[Index(v)].Length;
    /// <summary>The IconFactory glyph of layer <paramref name="layer"/> (0 = the silhouette, <see cref="Names"/>).</summary>
    public static string LayerGlyph(VibeKind v, int layer) => layer <= 0 ? Names[Index(v)] : Names[Index(v)] + "." + layer;
    /// <summary>The card colour of layer <paramref name="layer"/>.</summary>
    public static Color LayerColor(VibeKind v, int layer) { var c = LayerColors[Index(v)]; return c[Mathf.Clamp(layer, 0, c.Length - 1)]; }
    /// <summary>The silhouette's card colour (layer 0).</summary>
    public static Color Fill(VibeKind v) => LayerColors[Index(v)][0];

    static int Index(VibeKind v) => Mathf.Clamp((int)v, 0, Vibe.Count - 1);

    /// <summary>The card picture of vibe <paramref name="v"/>, <paramref name="px"/> px big (the rect is Comic.GlyphPad larger for the ink
    /// outline), centred on <paramref name="parent"/>: the inked silhouette tinted <see cref="Fill"/> (child "Glyph") and the accent layers
    /// on top (children "Layer1", "Layer2"). No raycasts.</summary>
    public static RectTransform Build(Transform parent, string name, VibeKind v, float px)
    {
        Ensure();
        float s = px * Comic.GlyphPad;
        var root = HudKit.Node(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s, s));
        Comic.GlyphImage(root, "Glyph", Vibe.Icon(v), Fill(v), px, true);
        int n = LayerCount(v);
        for (int k = 1; k < n; k++)
        {
            var img = Comic.GlyphImage(root, "Layer" + k, LayerGlyph(v, k), LayerColor(v, k), px, false);
            img.raycastTarget = false;
        }
        return root;
    }

    /// <summary>Registers the glyphs now (idempotent; boot does it before the first scene).</summary>
    public static void Ensure() { if (!Registered || !IconFactory.Has(Names[0])) Register(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        IconFactory.Register(Names[0], Sunny);
        IconFactory.Register(Names[1], p => Dreamy(p, 0));
        IconFactory.Register(Names[1] + ".1", p => Dreamy(p, 1));
        IconFactory.Register(Names[1] + ".2", p => Dreamy(p, 2));
        IconFactory.Register(Names[2], p => Rainy(p, false));
        IconFactory.Register(Names[2] + ".1", p => Rainy(p, true));
        IconFactory.Register(Names[3], p => Moonlit(p, false));
        IconFactory.Register(Names[3] + ".1", p => Moonlit(p, true));
        IconFactory.Register(Names[4], p => Stormy(p, false));
        IconFactory.Register(Names[4] + ".1", p => Stormy(p, true));
        IconFactory.Register(Names[5], p => Spicy(p, false));
        IconFactory.Register(Names[5] + ".1", p => Spicy(p, true));
        IconFactory.Register(Names[6], p => Floaty(p, 0));
        IconFactory.Register(Names[6] + ".1", p => Floaty(p, 1));
        IconFactory.Register(Names[6] + ".2", p => Floaty(p, 2));
        Registered = true;
    }

    // ------------------------------------------------------------------ the drawings
    static float Sunny(Vector2 p)
    {
        // a round sun with eight rounded rays set between the axes
        float d = IconFactory.P.Circle(p, 0f, 0f, 0.42f);
        for (int i = 0; i < 8; i++)
        {
            float a = (i * 45f + 22.5f) * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            d = Mathf.Min(d, IconFactory.P.Seg(p, c * 0.62f, s * 0.62f, c * 0.88f, s * 0.88f, 0.095f));
        }
        return d;
    }

    static float Dreamy(Vector2 p, int layer)
    {
        // a two-band rainbow standing on two little puffs (the bands cut clear of the puffs)
        const float cy = -0.30f;
        float clouds = Mathf.Min(Puff(p, -0.62f, -0.42f, 0.42f), Puff(p, 0.64f, -0.40f, 0.40f));
        float outer = IconFactory.P.Sub(IconFactory.P.Arc(p, 0f, cy, 0.71f, 0.12f, 0f, 180f), clouds - 0.08f);
        float inner = IconFactory.P.Sub(IconFactory.P.Arc(p, 0f, cy, 0.42f, 0.11f, 0f, 180f), clouds - 0.08f);
        if (layer == 1) return outer;
        if (layer == 2) return inner;
        return Mathf.Min(Mathf.Min(outer, inner), clouds);
    }

    static float Rainy(Vector2 p, bool dropsOnly)
    {
        float drops = Mathf.Min(Drop(p, -0.44f, -0.56f), Mathf.Min(Drop(p, 0.02f, -0.74f), Drop(p, 0.46f, -0.54f)));
        if (dropsOnly) return drops;
        return Mathf.Min(Cloud(p, 0.80f, 0f, 0.34f), drops);
    }

    static float Moonlit(Vector2 p, bool starOnly)
    {
        float star = IconFactory.P.Star(p - new Vector2(0.52f, 0.48f), 5, 0.30f, 0.13f);
        if (starOnly) return star;
        float crescent = IconFactory.P.Sub(IconFactory.P.Circle(p, -0.12f, -0.04f, 0.76f), IconFactory.P.Circle(p, 0.30f, 0.22f, 0.62f));
        return Mathf.Min(crescent, star);
    }

    static float Stormy(Vector2 p, bool boltOnly)
    {
        float bolt = Bolt(p, 0.52f, 0.06f, -0.44f, 0.02f);
        if (boltOnly) return bolt;
        float cloud = IconFactory.P.Sub(Cloud(p, 0.84f, 0f, 0.30f), bolt - 0.085f);   // the bolt splits the cloud (a gap the ink fills)
        return Mathf.Min(cloud, bolt);
    }

    // the chili's spine: (x, y, radius) — a tapered pod curving down to a hooked tip
    static readonly Vector3[] Chili =
    {
        new Vector3(-0.46f, 0.34f, 0.25f), new Vector3(-0.26f, 0.00f, 0.25f), new Vector3(0.02f, -0.30f, 0.21f),
        new Vector3(0.32f, -0.50f, 0.15f), new Vector3(0.60f, -0.57f, 0.09f), new Vector3(0.80f, -0.46f, 0.045f),
    };

    static float Spicy(Vector2 p, bool capOnly)
    {
        // the green cap (a tilted lozenge) and its stalk
        const float ang = -28f * Mathf.Deg2Rad;
        float cx = p.x + 0.50f, cy = p.y - 0.56f;
        var r = new Vector2(cx * Mathf.Cos(ang) - cy * Mathf.Sin(ang), cx * Mathf.Sin(ang) + cy * Mathf.Cos(ang));
        float cap = IconFactory.P.Box(r, 0f, 0f, 0.27f, 0.085f, 0.08f);
        float stalk = Mathf.Min(IconFactory.P.Seg(p, -0.52f, 0.62f, -0.46f, 0.84f, 0.065f), IconFactory.P.Seg(p, -0.46f, 0.84f, -0.26f, 0.93f, 0.065f));
        float green = Mathf.Min(cap, stalk);
        if (capOnly) return green;
        float body = float.MaxValue;
        for (int i = 0; i + 1 < Chili.Length; i++)
            body = Mathf.Min(body, RoundCone(p, new Vector2(Chili[i].x, Chili[i].y), new Vector2(Chili[i + 1].x, Chili[i + 1].y), Chili[i].z, Chili[i + 1].z));
        body = IconFactory.P.Sub(body, green - 0.07f);
        return Mathf.Min(body, green);
    }

    static float Floaty(Vector2 p, int layer)
    {
        // a balloon (an egg with a tapered bottom), a curved shine, the knot and a two-stroke string
        float shine = IconFactory.P.Arc(p, 0.02f, 0.32f, 0.34f, 0.06f, 105f, 165f);
        if (layer == 1) return shine;
        float knot = IconFactory.P.Tri(p, -0.09f, -0.49f, 0.09f, -0.49f, 0f, -0.33f) - 0.015f;
        float line = Mathf.Min(IconFactory.P.Seg(p, 0f, -0.49f, 0.10f, -0.70f, 0.026f), IconFactory.P.Seg(p, 0.10f, -0.70f, -0.04f, -0.93f, 0.026f));
        float str = Mathf.Min(knot, line);
        if (layer == 2) return str;
        float body = Mathf.Min(Ellipse(p, 0f, 0.30f, 0.50f, 0.58f), IconFactory.P.Tri(p, -0.30f, -0.06f, 0.30f, -0.06f, 0f, -0.36f) - 0.06f);
        return Mathf.Min(body, str);
    }

    // ------------------------------------------------------------------ helpers (IconFactory keeps its cloud and bolt private)
    /// <summary>IconFactory's cloud silhouette (three lobes on a flat base), scaled by s, offset by (dx, dy).</summary>
    static float Cloud(Vector2 p, float s, float dx, float dy)
    {
        var q = new Vector2((p.x - dx) / s, (p.y - dy) / s);
        return s * Mathf.Min(Mathf.Min(IconFactory.P.Circle(q, -0.4f, -0.1f, 0.4f), IconFactory.P.Circle(q, 0.02f, 0.16f, 0.48f)),
                             Mathf.Min(IconFactory.P.Circle(q, 0.42f, -0.1f, 0.38f), IconFactory.P.Box(q, 0f, -0.34f, 0.8f, 0.22f, 0.14f)));
    }

    static readonly Vector2[] BoltPts = { new Vector2(-0.1f, 0.86f), new Vector2(-0.58f, -0.02f), new Vector2(-0.16f, -0.02f), new Vector2(-0.36f, -0.86f), new Vector2(0.58f, 0.14f), new Vector2(0.16f, 0.14f), new Vector2(0.4f, 0.86f) };
    /// <summary>IconFactory's lightning bolt, scaled by s, offset by (dx, dy), fattened by <paramref name="round"/>.</summary>
    static float Bolt(Vector2 p, float s, float dx, float dy, float round)
    {
        var q = new Vector2((p.x - dx) / s, (p.y - dy) / s);
        return s * IconFactory.P.Poly(q, BoltPts) - round;
    }

    /// <summary>A small cloud puff (the rainbow's feet).</summary>
    static float Puff(Vector2 p, float x, float y, float s)
    {
        var q = new Vector2((p.x - x) / s, (p.y - y) / s);
        return s * Mathf.Min(Mathf.Min(IconFactory.P.Circle(q, -0.34f, 0f, 0.34f), IconFactory.P.Circle(q, 0.10f, 0.14f, 0.42f)),
                             Mathf.Min(IconFactory.P.Circle(q, 0.46f, -0.02f, 0.30f), IconFactory.P.Box(q, 0.05f, -0.18f, 0.72f, 0.18f, 0.16f)));
    }

    /// <summary>A rain drop: a round belly with a point on top.</summary>
    static float Drop(Vector2 p, float x, float y)
    {
        const float r = 0.14f, h = 0.30f;
        return Mathf.Min(IconFactory.P.Circle(p, x, y, r), IconFactory.P.Tri(p, x - r * 0.92f, y + r * 0.38f, x + r * 0.92f, y + r * 0.38f, x, y + h) - 0.01f);
    }

    /// <summary>An ellipse (approximate distance: exact on the axes, close enough for a 128 px glyph).</summary>
    static float Ellipse(Vector2 p, float cx, float cy, float rx, float ry)
    {
        float qx = (p.x - cx) / rx, qy = (p.y - cy) / ry;
        return (Mathf.Sqrt(qx * qx + qy * qy) - 1f) * Mathf.Min(rx, ry);
    }

    /// <summary>The exact distance to a round cone (a capsule whose radius goes from <paramref name="r1"/> at a to <paramref name="r2"/> at b).</summary>
    static float RoundCone(Vector2 p, Vector2 a, Vector2 b, float r1, float r2)
    {
        Vector2 ba = b - a;
        float l2 = Vector2.Dot(ba, ba), rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1f / l2;
        Vector2 pa = p - a;
        float y = Vector2.Dot(pa, ba), z = y - l2;
        Vector2 xv = pa * l2 - ba * y;
        float x2 = Vector2.Dot(xv, xv), y2 = y * y * l2, z2 = z * z * l2;
        float k = Mathf.Sign(rr) * rr * rr * x2;
        if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
        if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
        return (Mathf.Sqrt(Mathf.Max(0f, x2 * a2 * il2)) + y * rr) * il2 - r1;
    }
}
