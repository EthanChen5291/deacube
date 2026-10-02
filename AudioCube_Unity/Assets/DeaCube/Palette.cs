using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Colours shared by the 3D world and the HUD.</summary>
public static class Palette
{
    // v3 package E (SPEC v3 §7): the world's backdrop is a pastel dusk now: the camera clear colour and the fog are the horizon
    // haze (= Look.SeaFar, the sky below the horizon), so far islands melt into flat lilac layers. HUD colours are unchanged.
    public static readonly Color Bg = new Color(0.69f, 0.54f, 0.78f);
    public static readonly Color Fog = new Color(0.69f, 0.54f, 0.78f);
    public static readonly Color Glass = new Color(0.09f, 0.08f, 0.15f, 0.80f);
    public static readonly Color GlassLight = new Color(1f, 1f, 1f, 0.07f);
    public static readonly Color Icon = new Color(0.95f, 0.94f, 1f, 0.92f);
    public static readonly Color IconDim = new Color(0.95f, 0.94f, 1f, 0.32f);
    public static readonly Color Accent = new Color(0.74f, 0.64f, 1f);
    public static readonly Color AccentSoft = new Color(0.74f, 0.64f, 1f, 0.35f);
    public static readonly Color Danger = new Color(1f, 0.42f, 0.46f);
    public static readonly Color Ok = new Color(0.45f, 1f, 0.72f);
    public static readonly Color TileBase = new Color(0.80f, 0.78f, 0.90f);

    public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
    public static Color Mul(Color c, float m) => new Color(c.r * m, c.g * m, c.b * m, c.a);
    public static Color Boost(Color c, float sat, float val)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Clamp01(s * sat), Mathf.Clamp01(v * val));
    }
    public static Color Hue(float hue, float s = 0.6f, float v = 0.95f) => Color.HSVToRGB(Mathf.Repeat(hue, 1f), s, v);
}

/// <summary>
/// v9 (G): an eleventh role, FX (group 10: lime, sparkles; the effects presets), appended after the drums so every save keeps its indices.
/// The ten instrument roles (SPEC §4.1): cube colours (v4: pastels), silhouette icons, synth slots (identity onto SynthBank.Defs),
/// pans, fallback clips (0-5 only), volume and mute. Slots 0-5 keep the six prefab hues in their index order.
/// v6 (A, SPEC v6 §2.7 / §4.1 — "the instruments should be expanded into groups"): each role is a GROUP of voices (SynthBank's table: 5-9
/// sounds each from the SoundFont we ship, voice 0 = the v5 sound). Colour, icon, volume, mute, pan and the sends' Space scaling stay per
/// GROUP and reach every voice's slot (SetVolume / SetMuted / PushToSynth); the sound follows <see cref="SlotOf(int, int)"/>;
/// <see cref="SlotOf(int)"/> stays voice 0 (every caller that knows no voice keeps its v5 sound). <see cref="BrushVoice"/> is the voice a new
/// cube gets (the HUD's voice flyout / PathManager.PickUpCube set it) and what a chip audition plays.
/// </summary>
public static class Instruments
{
    public const int Count = 11;   // v9 (G): + FX (group 10, the effects presets; appended so every save's 0..9 and drums = 9 stay)
    /// <summary>The ten role colours (v4, the user: "less saturated and more pastel-ish"): pastels at saturation ≈ 0.35-0.45 and value
    /// ≈ 0.93-1, each role keeping the hue it has always had in the game (slots 0-5 used to come from the prefab materials: blue, red,
    /// green, orange, magenta, yellow; then teal, rose, ivory, white) — now sky, coral, mint, peach, lavender, butter, teal, rose, ivory and
    /// a cool pearl (kept apart from each other and from the warm paper cream of the UI). The prefab materials no longer decide them.</summary>
    static readonly Color[] Defaults =
    {
        Pastel(0.600f, 0.42f, 1.00f),   // 0 keys: sky (was blue)
        Pastel(0.995f, 0.42f, 1.00f),   // 1 pluck: coral (was red)
        Pastel(0.360f, 0.40f, 0.95f),   // 2 pad: mint (was green)
        Pastel(0.070f, 0.44f, 1.00f),   // 3 lead: peach (was orange)
        Pastel(0.780f, 0.36f, 1.00f),   // 4 bass: lavender (was magenta)
        Pastel(0.135f, 0.42f, 1.00f),   // 5 bells: butter (was yellow)
        Pastel(0.495f, 0.44f, 0.93f),   // 6 strings: teal
        Pastel(0.930f, 0.36f, 1.00f),   // 7 choir: rose
        Pastel(0.105f, 0.20f, 0.97f),   // 8 piano: ivory (warmer and deeper than the UI's paper)
        Pastel(0.690f, 0.10f, 0.95f),   // 9 drums: pearl (cool lilac-grey; was white)
        Pastel(0.240f, 0.42f, 0.97f),   // 10 fx: lime (v9: the widest free hue, between butter and mint)
    };
    static Color Pastel(float h, float s, float v) { var c = Color.HSVToRGB(h, s, v); c.a = 1f; return c; }
    public static readonly Color[] Colors = new Color[Count];
    /// <summary>Silhouette icon per role (IconFactory names; drawn by WP-D).</summary>
    public static readonly string[] Icons = { "keys", "pluck", "pad", "bolt", "bass", "bell", "strings", "choir", "piano", "kit", "fx" };   // v9: fx = sparkles
    public static readonly AudioClip[] Clips = new AudioClip[Count];            // fallback samples (no SoundFont); 6-9 stay null
    public static readonly AudioMixerGroup[] Groups = new AudioMixerGroup[Count];
    /// <summary>Palette instrument -> SynthBank slot: Keys, Pluck, Pad, Lead, Bass, Bells, Strings, Choir, Piano, Drums (identity).</summary>
    public static readonly int[] SynthSlot = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, SynthBank.SlotOf(10, 0) };   // v9: FX voice 0 lives after every v6 slot
    public static readonly float[] Pan = { -0.15f, 0.25f, 0f, -0.10f, 0f, 0.30f, -0.25f, 0.10f, 0f, 0f, 0.20f };
    public static readonly float[] Volume = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
    public static readonly bool[] Muted = new bool[Count];
    public static event Action OnChanged;
    static bool loaded;

    static Instruments() { for (int i = 0; i < Count; i++) Colors[i] = Defaults[i]; }

    public static int SlotOf(int instrument) => SynthSlot[Mathf.Clamp(instrument, 0, Count - 1)];

    // ---- v6 instrument groups (SPEC v6 §2.7, A): each of the ten roles is a GROUP of sounds (voices); voice 0 is the group's v5 sound, so every
    // pre-v6 song sounds the same.
    /// <summary>The group's name ("keys", "pluck" …): the role's word, lowercase.</summary>
    public static readonly string[] GroupWords = { "keys", "pluck", "pad", "lead", "bass", "bells", "strings", "choir", "piano", "drums", "fx" };
    /// <summary>Sounds in group <paramref name="instrument"/> (at least 1).</summary>
    public static int VoiceCount(int instrument) => SynthBank.VoiceCount(Mathf.Clamp(instrument, 0, Count - 1));
    /// <summary>A sound's lowercase caption of one or two words ("e.piano", "koto" …); a voice out of range names the group's voice 0.</summary>
    public static string VoiceName(int instrument, int voice) => SynthBank.Def(SlotOf(instrument, voice)).caption;
    /// <summary>The synth slot that plays sound <paramref name="voice"/> of group <paramref name="instrument"/> (a voice out of range: voice 0).</summary>
    public static int SlotOf(int instrument, int voice) => SynthBank.SlotOf(Mathf.Clamp(instrument, 0, Count - 1), voice);
    /// <summary>Every synth slot of group <paramref name="instrument"/> (voice order; a copy).</summary>
    public static int[] SlotsOf(int instrument) => SynthBank.SlotsOf(Mathf.Clamp(instrument, 0, Count - 1));
    /// <summary>The group a synth slot belongs to (SynthBank.GroupOf).</summary>
    public static int GroupOfSlot(int slot) => SynthBank.GroupOf(slot);
    /// <summary>The voice a new cube of each group gets (the HUD's variant flyout sets it; PathManager.PickUpCube too); a chip audition plays it.</summary>
    public static readonly int[] BrushVoice = new int[Count];
    /// <summary>The brush voice of group <paramref name="instrument"/>, clamped into the group.</summary>
    public static int BrushVoiceOf(int instrument)
    {
        int g = Mathf.Clamp(instrument, 0, Count - 1), v = BrushVoice[g];
        return v >= 0 && v < VoiceCount(g) ? v : 0;
    }
    /// <summary>Auditions one sound of a group (the HUD flyout's hover): like <see cref="Audition(int)"/> with that voice's slot.</summary>
    public static void Audition(int instrument, int voice) => AuditionSlot(Mathf.Clamp(instrument, 0, Count - 1), SlotOf(instrument, voice));
    public static bool IsDrums(int instrument) => instrument == 9;
    /// <summary>Legacy (v1) instrument index -> v2 role; identity today (the six colours kept their index order).</summary>
    public static int RemapLegacy(int inst) => Mathf.Clamp(inst, 0, Count - 1);
    /// <summary>Fallback clip for an instrument (roles without a sample use clip 0, pitched).</summary>
    public static AudioClip ClipOf(int instrument) { int i = Mathf.Clamp(instrument, 0, Count - 1); return Clips[i] != null ? Clips[i] : Clips[0]; }
    public static AudioMixerGroup GroupOf(int instrument) { int i = Mathf.Clamp(instrument, 0, Count - 1); return Groups[i] != null ? Groups[i] : Groups[0]; }

    public static void LoadFrom(List<GameObject> prefabs)
    {
        for (int i = 0; i < Count; i++)
        {
            Colors[i] = Defaults[i];   // v4: the pastel palette is authoritative (the prefabs' saturated materials are not read back)
            if (prefabs == null || i >= prefabs.Count || prefabs[i] == null) continue;
            var a = prefabs[i].GetComponent<AudioSource>();
            if (a != null) { Clips[i] = a.clip; Groups[i] = a.outputAudioMixerGroup; }
        }
        loaded = true;
    }
    public static bool Loaded => loaded;
    public static float Gain(int i) => Muted[i] ? 0f : Volume[i];
    /// <summary>Slider level; applied to every voice slot of the group at once (deferred to the release while a Drop punch-in is held).</summary>
    public static void SetVolume(int i, float v)
    {
        i = Mathf.Clamp(i, 0, Count - 1);
        Volume[i] = Mathf.Clamp01(v);
        if (Synth.Ready && !Performance.DropActive) for (int k = 0, n = SynthBank.VoiceCount(i); k < n; k++) Synth.SetSlotGain(SynthBank.SlotOf(i, k), Volume[i]);
        OnChanged?.Invoke();
    }
    /// <summary>Mutes / unmutes every voice slot of the group.</summary>
    public static void SetMuted(int i, bool m)
    {
        i = Mathf.Clamp(i, 0, Count - 1);
        Muted[i] = m;
        if (Synth.Ready) for (int k = 0, n = SynthBank.VoiceCount(i); k < n; k++) Synth.SetSlotMute(SynthBank.SlotOf(i, k), Muted[i]);
        OnChanged?.Invoke();
    }
    public static void ToggleMute(int i) => SetMuted(i, !Muted[i]);
    /// <summary>Restores saved levels/mutes; roles beyond the arrays (v1 saves have six; null = a fresh song) get the defaults, so nothing leaks from the previous song.</summary>
    public static void Restore(float[] volumes, bool[] mutes)
    {
        for (int i = 0; i < Count; i++)
        {
            Volume[i] = volumes != null && i < volumes.Length ? Mathf.Clamp01(volumes[i]) : 1f;
            Muted[i] = mutes != null && i < mutes.Length && mutes[i];
        }
        PushToSynth();
        OnChanged?.Invoke();
    }

    /// <summary>Re-applies every role's volume, mute, pan and sends (reverb x Space, chorus x min(Space, 1)) to the synth — v6: to every voice slot
    /// of each group (each voice's own sends). Idempotent.</summary>
    public static void PushToSynth() => PushMix(Volume, Muted, SongManager.Space);

    /// <summary>v6: pushes a mix (per group: level, mute; the Space dial) to every voice slot of every group — the song's (<see cref="PushToSynth"/>)
    /// or another one (the gallery preview's). Groups beyond the arrays get level 1, unmuted.</summary>
    public static void PushMix(float[] volumes, bool[] mutes, float space)
    {
        if (!Synth.Ready) return;
        for (int i = 0; i < Count; i++)
        {
            float vol = volumes != null && i < volumes.Length ? Mathf.Clamp01(volumes[i]) : 1f;
            bool mute = mutes != null && i < mutes.Length && mutes[i];
            for (int v = 0, n = SynthBank.VoiceCount(i); v < n; v++)
            {
                int slot = SynthBank.SlotOf(i, v);
                Synth.SetSlotGain(slot, vol); Synth.SetSlotMute(slot, mute);
                Synth.SetSlotPan(slot, Pan[i]);
                var def = SynthBank.Defs[slot];
                Synth.SetSlotReverb(slot, Mathf.Clamp01(def.reverb * space));
                Synth.SetSlotChorus(slot, Mathf.Clamp01(def.chorus * Mathf.Min(1f, space)));
            }
        }
    }

    static readonly VoiceRules.NoteEvent[] auditionEv = new VoiceRules.NoteEvent[1];

    /// <summary>Auditions a palette swatch (SPEC §4.6): the slot's register centre (60 folded) when no song exists, else the lit island's root
    /// (+ transpose) folded into the slot's register; immediately when stopped, on the next 16th (owner 9) while playing. Drums audition the kick (36).
    /// v6: the group's brush voice (<see cref="BrushVoice"/>: the sound the chip holds).</summary>
    public static void Audition(int instrument)
    {
        instrument = Mathf.Clamp(instrument, 0, Count - 1);
        AuditionSlot(instrument, SlotOf(instrument, BrushVoiceOf(instrument)));
    }

    static void AuditionSlot(int instrument, int slot)
    {
        if (!Synth.Ready) return;
        bool drums = SynthBank.Def(slot).drums;
        var sm = SongManager.I;
        bool hasSong = sm != null && sm.HasSong;
        if (!GlobalClock.IsPlaying)
        {
            int midi = drums ? 36 : SynthBank.ClampToRegister(slot, hasSong ? sm.Islands[Mathf.Clamp(sm.LitIsland, 0, sm.Islands.Count - 1)].chordRootMIDI + SongManager.Transpose : 60);
            Synth.Preview(slot, midi, 80, 0.3f);
            return;
        }
        // while playing: on the next 16th, with the root of the island lit on that 16th (SPEC 2.9 rule 5), released before its window ends
        double t, end;
        double b = VoiceRules.PreviewBeat(out t, out end);
        int m = 36;
        if (!drums)
        {
            int root = 60;
            if (hasSong) { int at = Mathf.Clamp(sm.ActiveMeasureIndex((float)b), 0, sm.Islands.Count - 1); root = sm.Islands[at].chordRootMIDI + SongManager.Transpose; }
            m = SynthBank.ClampToRegister(slot, root);
        }
        double off = drums ? t + 0.3 : Math.Max(t + 0.03, Math.Min(t + 0.3, end - 0.005));
        auditionEv[0] = new VoiceRules.NoteEvent { slot = slot, midi = m, vel = 80, onDsp = t, offDsp = off };
        VoiceRules.Dispatch(auditionEv, 1, VoiceRules.OwnerPreview);
    }
}
