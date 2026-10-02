using System.Collections.Generic;
using UnityEngine;

/// <summary>SPEC v5 §2.2: a chord quality read as weather (the game's mood families sun / cloud / storm, extended).</summary>
public enum VibeKind { Sunny = 0, Dreamy = 1, Rainy = 2, Moonlit = 3, Stormy = 4, Spicy = 5, Floaty = 6 }

/// <summary>
/// SPEC v5 §2.2 — the vibe language (the user, 2026-09-30: "rather than random we should assign some sort of meaning to them so that nonmusical
/// users can remember certain cards or shapes and stuff in terms of their vibe"). Every chord wears the weather of its quality, extending the
/// game's mood / climate families (sun = major, cloud = minor, storm = dominant): major 7 sunny (bright, happy), major 9 dreamy (a soft glow),
/// minor 7 rainy (sad, tender), minor 9 moonlit (wistful, pretty), dominant 7 stormy (tense: it wants to go home), dominant 9 spicy (funky
/// tension), sus 4 floaty (hanging, unresolved). One colour family and one glyph per vibe everywhere a chord shows (islands, tiles, cards, the
/// rail, the header, the presentation read it through MusicTheory.ChordColor / QualityIcon); the song key's tonic chord is "home".
/// </summary>
public static class Vibe
{
    public const int Count = 7;
    /// <summary>The home sticker's glyph (the key's tonic chord).</summary>
    public const string HomeIcon = "home";

    // family colours (HSV): kept apart from each other; KeyBlock blends them into the platform / tile tints
    static readonly Vector3[] Hsv =
    {
        new Vector3(0.125f, 0.60f, 1.00f),   // sunny: butter yellow
        new Vector3(0.930f, 0.38f, 1.00f),   // dreamy: pink glow
        new Vector3(0.580f, 0.52f, 0.96f),   // rainy: rain blue
        new Vector3(0.690f, 0.44f, 0.93f),   // moonlit: periwinkle night
        new Vector3(0.000f, 0.58f, 0.98f),   // stormy: coral red
        new Vector3(0.065f, 0.64f, 1.00f),   // spicy: tangerine
        new Vector3(0.440f, 0.46f, 0.92f),   // floaty: mint
    };
    static readonly string[] Words = { "sunny", "dreamy", "rainy", "moonlit", "stormy", "spicy", "floaty" };
    /// <summary>What each vibe feels like, in a non-musician's words (the second half of a card's caption).</summary>
    static readonly string[] Feelings = { "happy", "soft glow", "a bit sad", "wistful", "wants to go home", "funky", "hanging" };
    /// <summary>The vibe glyphs (VibeGlyphs registers the art before the first scene: a sun, a rainbow, a rain cloud, a moon with a star, a storm
    /// cloud with a bolt, a chili, a balloon); until one is registered <see cref="Icon"/> falls back to an existing glyph.</summary>
    static readonly string[] Icons = { "vibeSunny", "vibeDreamy", "vibeRainy", "vibeMoonlit", "vibeStormy", "vibeSpicy", "vibeFloaty" };
    static readonly string[] Fallback = { "sun", "sun", "cloud", "moon", "storm", "flame", "flourish" };

    public static VibeKind Of(ChordQuality q)
    {
        switch (q)
        {
            case ChordQuality.Major7: return VibeKind.Sunny;
            case ChordQuality.Major9: return VibeKind.Dreamy;
            case ChordQuality.Minor7: return VibeKind.Rainy;
            case ChordQuality.Minor9: return VibeKind.Moonlit;
            case ChordQuality.Dominant7: return VibeKind.Stormy;
            case ChordQuality.Dominant9: return VibeKind.Spicy;
            case ChordQuality.Sus4: return VibeKind.Floaty;
        }
        return VibeKind.Sunny;
    }

    public static VibeKind Of(IList<int> semis) => Of(MusicTheory.QualityOf(semis != null && semis.Count > 0 ? semis : new[] { 0, 4, 7 }));

    /// <summary>The vibe's family colour.</summary>
    public static Color ColorOf(VibeKind v)
    {
        var h = Hsv[Mathf.Clamp((int)v, 0, Count - 1)];
        var c = Color.HSVToRGB(h.x, h.y, h.z); c.a = 1f;
        return c;
    }

    /// <summary>A chord's colour: its vibe's family colour with a tiny shade by root (two sunny chords on different roots are not identical twins).</summary>
    public static Color ChordColor(int rootMidi, IList<int> semis)
    {
        var h = Hsv[(int)Of(semis)];
        int pc = MusicTheory.PitchClass(rootMidi);
        float hue = h.x + (pc - 5.5f) * 0.0025f;
        hue -= Mathf.Floor(hue);
        var c = Color.HSVToRGB(hue, h.y, Mathf.Clamp01(h.z * (0.95f + 0.05f * pc / 11f)));
        c.a = 1f;
        return c;
    }

    /// <summary>The vibe's glyph (IconFactory name).</summary>
    public static string Icon(VibeKind v)
    {
        int i = Mathf.Clamp((int)v, 0, Count - 1);
        return IconFactory.Has(Icons[i]) ? Icons[i] : Fallback[i];
    }

    /// <summary>The vibe's lowercase caption word.</summary>
    public static string Word(VibeKind v) => Words[Mathf.Clamp((int)v, 0, Count - 1)];

    /// <summary>What the vibe feels like ("happy", "wants to go home" …), lowercase.</summary>
    public static string Feeling(VibeKind v) => Feelings[Mathf.Clamp((int)v, 0, Count - 1)];

    /// <summary>A chord's one-line caption (deck cards, the header): the vibe word, then "home" on the key's home chord, else the feeling —
    /// "sunny · home", "stormy · wants to go home".</summary>
    public static string Caption(int rootMidi, IList<int> semis)
    {
        var v = Of(semis);
        return Word(v) + " · " + (IsHome(rootMidi) ? "home" : Feeling(v));
    }

    /// <summary>True when the chord rooted on <paramref name="rootMidi"/> is the song key's tonic ("home").</summary>
    public static bool IsHome(int rootMidi)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return false;
        return MusicTheory.PitchClass(rootMidi) == MusicTheory.KeyOfSong().tonic;
    }
}
