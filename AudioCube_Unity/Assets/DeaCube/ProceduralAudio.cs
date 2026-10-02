using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Tiny synthesized clips for UI ticks, metronome and feedback. No audio assets required.</summary>
public static class ProceduralAudio
{
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    const int SR = 44100;

    static AudioClip Synth(string name, float seconds, Func<float, float> f)
    {
        if (cache.TryGetValue(name, out var c) && c != null) return c;
        int n = Mathf.Max(8, (int)(seconds * SR));
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, SR, false);
        clip.SetData(data, 0);
        cache[name] = clip;
        return clip;
    }

    static float Noise(int i) { uint x = (uint)(i * 747796405 + 2891336453); x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737u; x = (x >> 22) ^ x; return (x / 4294967295f) * 2f - 1f; }

    public static AudioClip Click(bool accent) => Synth(accent ? "clickHi" : "clickLo", 0.07f, t =>
    {
        float f = accent ? 1900f : 1250f;
        float env = Mathf.Exp(-t * 55f);
        float s = Mathf.Sin(2f * Mathf.PI * f * t) * env;
        float n = Noise((int)(t * SR)) * Mathf.Exp(-t * 260f) * 0.5f;
        return (s + n) * (accent ? 0.9f : 0.65f);
    });

    public static AudioClip Tick() => Synth("tick", 0.035f, t => Mathf.Sin(2f * Mathf.PI * 2600f * t) * Mathf.Exp(-t * 220f) * 0.5f + Noise((int)(t * SR)) * Mathf.Exp(-t * 400f) * 0.25f);

    public static AudioClip Pop() => Synth("pop", 0.16f, t =>
    {
        float f = Mathf.Lerp(620f, 140f, Mathf.Clamp01(t * 8f));
        return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * 0.7f;
    });

    public static AudioClip Chime() => Synth("chime", 0.6f, t =>
    {
        float env = Mathf.Exp(-t * 5.5f);
        return (Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1318.5f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.15f) * env * 0.5f;
    });

    public static AudioClip Whoosh() => Synth("whoosh", 0.32f, t =>
    {
        float env = Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI);
        int i = (int)(t * SR);
        float n = (Noise(i) + Noise(i - 1) + Noise(i - 2) + Noise(i - 3)) * 0.25f;
        return n * env * 0.35f;
    });

    public static AudioClip Thud() => Synth("thud", 0.2f, t =>
    {
        float f = Mathf.Lerp(160f, 55f, Mathf.Clamp01(t * 6f));
        return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 18f) * 0.8f;
    });

    public static AudioClip Sparkle() => Synth("sparkle", 0.45f, t =>
    {
        float env = Mathf.Exp(-t * 7f);
        float f = 1760f * Mathf.Pow(2f, Mathf.Floor(t * 22f) / 12f * 2f);
        return Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.35f;
    });

    /// <summary>v3 (SPEC §5.2): the soft "blip" of a tutorial bubble popping in — a short sine that glides up a fifth, with a
    /// quiet octave partial; phase-continuous glide, 2 ms attack, ~0.12 s decay.</summary>
    public static AudioClip Blip() => Synth("blip", 0.16f, t =>
    {
        const float f0 = 660f, f1 = 990f, T = 0.05f;
        float ph = t < T ? f0 * t + (f1 - f0) * t * t / (2f * T) : f0 * T + (f1 - f0) * T * 0.5f + f1 * (t - T);
        float env = Mathf.Clamp01(t * 500f) * Mathf.Exp(-t * 24f);
        return (Mathf.Sin(2f * Mathf.PI * ph) + 0.18f * Mathf.Sin(4f * Mathf.PI * ph)) * env * 0.42f;
    });

    /// <summary>v3 (SPEC §5.2): a softer, lower "boop" when a tutorial step is completed and the next one is about to pop.</summary>
    public static AudioClip StepDone() => Synth("stepDone", 0.3f, t =>
    {
        float env = Mathf.Clamp01(t * 400f) * Mathf.Exp(-t * 11f);
        float a = Mathf.Sin(2f * Mathf.PI * 784f * t), b = t > 0.07f ? Mathf.Sin(2f * Mathf.PI * 1175f * (t - 0.07f)) * Mathf.Exp(-(t - 0.07f) * 12f) : 0f;
        return (a * env * 0.3f + b * 0.3f);
    });
}

/// <summary>Small pool of AudioSources for one-shot previews and UI sounds.</summary>
public class AudioPool : MonoBehaviour
{
    static AudioPool inst;
    AudioSource[] src; int idx;

    public static AudioPool I
    {
        get
        {
            if (inst == null)
            {
                var go = new GameObject("AudioPool");
                inst = go.AddComponent<AudioPool>();
            }
            return inst;
        }
    }

    void Awake()
    {
        inst = this;
        src = new AudioSource[12];
        for (int i = 0; i < src.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0f;
            src[i] = s;
        }
    }

    public AudioSource Play(AudioClip clip, float pitch = 1f, float volume = 1f, AudioMixerGroup group = null)
    {
        if (clip == null) return null;
        var s = src[idx++ % src.Length];
        s.Stop(); s.clip = clip; s.pitch = pitch; s.volume = volume * ProjectConfig.MaxSystemVolume; s.outputAudioMixerGroup = group; s.Play();
        return s;
    }

    public static void UI(AudioClip clip, float volume = 0.45f, float pitch = 1f) { I.Play(clip, pitch, volume); }
}
