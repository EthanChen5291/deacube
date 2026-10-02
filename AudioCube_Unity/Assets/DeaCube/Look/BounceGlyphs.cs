using UnityEngine;

/// <summary>
/// Glyphs for the island's presentation style (KeyBlock.fall; the island header's more card): how the presentation's cubes ricochet down
/// through that island's notes (package P, v4) — 0 auto, 1 floaty (bigger throws, wide steps), 2 skim (flat launches, small steps),
/// 3 zigzag (alternating steps), 4 spiral (circles the lane axis in depth), 5 cascade (runs to the lane edge and back). Every picture shows
/// a small cube and its path falling from the top-left. Registered through IconFactory.Register before the HUD builds; <see cref="Names"/>
/// are the hover captions.
/// </summary>
public static class BounceGlyphs
{
    public static readonly string[] Icons = { "present", "bounceFloaty", "bounceSkim", "bounceZigzag", "bounceSpiral", "bounceCascade" };
    /// <summary>The lowercase words of the six styles (hover captions).</summary>
    public static readonly string[] Names = { "auto", "floaty", "skim", "zigzag", "spiral", "cascade" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        // floaty: two big throws, each landing on a ledge further down and far to the side
        IconFactory.Register("bounceFloaty", p => IconFactory.P.U(
            IconFactory.P.Box(p, -0.66f, 0.70f, 0.13f, 0.13f, 0.04f),
            IconFactory.P.Arc(p, -0.16f, 0.30f, 0.46f, 0.065f, 20f, 160f),
            IconFactory.P.Arc(p, 0.46f, -0.42f, 0.36f, 0.065f, 15f, 175f),
            IconFactory.P.Box(p, 0.30f, 0.22f, 0.24f, 0.05f, 0.02f),
            IconFactory.P.Box(p, 0.78f, -0.52f, 0.18f, 0.05f, 0.02f)));
        // skim: flat launches, many small steps
        IconFactory.Register("bounceSkim", p => IconFactory.P.U(
            IconFactory.P.Box(p, -0.80f, 0.56f, 0.12f, 0.12f, 0.03f),
            IconFactory.P.Arc(p, -0.52f, 0.30f, 0.20f, 0.06f, 0f, 180f),
            IconFactory.P.Arc(p, -0.12f, 0.02f, 0.20f, 0.06f, 0f, 180f),
            IconFactory.P.Arc(p, 0.28f, -0.26f, 0.20f, 0.06f, 0f, 180f),
            IconFactory.P.Arc(p, 0.66f, -0.54f, 0.18f, 0.06f, 0f, 180f),
            IconFactory.P.Seg(p, -0.78f, 0.18f, 0.84f, -0.70f, 0.035f)));
        // zigzag: alternating steps down
        IconFactory.Register("bounceZigzag", p => IconFactory.P.U(
            IconFactory.P.Seg(p, -0.62f, 0.52f, 0.30f, 0.20f, 0.07f),
            IconFactory.P.Seg(p, 0.30f, 0.20f, -0.46f, -0.18f, 0.07f),
            IconFactory.P.Seg(p, -0.46f, -0.18f, 0.40f, -0.60f, 0.07f),
            IconFactory.P.Box(p, -0.62f, 0.68f, 0.13f, 0.13f, 0.03f)));
        // spiral: circling the lane's axis on the way down
        IconFactory.Register("bounceSpiral", p => IconFactory.P.U(
            IconFactory.P.Arc(p, 0f, -0.05f, 0.66f, 0.07f, 100f, 400f),
            IconFactory.P.Arc(p, 0f, -0.05f, 0.36f, 0.07f, -80f, 200f),
            IconFactory.P.Box(p, 0f, 0.72f, 0.13f, 0.13f, 0.03f)));
        // cascade: runs out to the lane's edge (the wall) and back
        IconFactory.Register("bounceCascade", p => IconFactory.P.U(
            IconFactory.P.Box(p, -0.66f, 0.70f, 0.13f, 0.13f, 0.04f),
            IconFactory.P.Seg(p, -0.56f, 0.52f, 0.58f, 0.02f, 0.065f),
            IconFactory.P.Seg(p, 0.58f, 0.02f, -0.34f, -0.62f, 0.065f),
            IconFactory.P.Box(p, 0.80f, -0.04f, 0.05f, 0.62f, 0.02f),
            IconFactory.P.Head(p, -0.40f, -0.66f, 214f, 0.26f)));
    }
}
