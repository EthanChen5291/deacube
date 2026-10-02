using UnityEngine;

/// <summary>
/// SPEC v5 §2.4 / §6 (package R): how a chord tile shows its pitch — height (a staircase: <see cref="Rise"/> world units per semitone above the
/// island's lowest note) and brightness (<see cref="TileColor"/>: low = deeper, high = lighter). KeyBlock.Build reads both; the tray's card art and
/// the inspector grid follow the tiles' own colours. v5 tuning: nothing else is printed on a chord tile any more (the role glyphs are gone), so the
/// staircase is steeper (0.045 → <see cref="Rise"/>) and the brightness ramp wider, keeping the hue (the island's vibe) at every height.
/// </summary>
public static class PitchLook
{
    /// <summary>World units a tile rises per semitone above its island's lowest note (v4: ProjectConfig.PitchRise 0.045).</summary>
    public static float Rise = 0.075f;

    /// <summary>A tile's face colour from its island's tint and its pitch rank <paramref name="t01"/> (0 = the island's lowest note, 1 = its highest):
    /// the same hue throughout; low notes deeper and a little richer, high notes lighter and paler.</summary>
    public static Color TileColor(Color tint, float t01)
    {
        float h, s, v;
        Color.RGBToHSV(tint, out h, out s, out v);
        t01 = Mathf.Clamp01(t01);
        float sat = Mathf.Lerp(Mathf.Min(1f, s * 1.22f + 0.04f), s * 0.6f, t01);
        float val = Mathf.Lerp(v * 0.64f, Mathf.Lerp(v, 1f, 0.66f), t01);
        Color c = Color.HSVToRGB(h, Mathf.Clamp01(sat), Mathf.Clamp01(val));
        c.a = tint.a;
        return c;
    }
}
