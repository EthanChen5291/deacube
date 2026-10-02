using UnityEngine;

/// <summary>Easing curves used by every animation in the app.</summary>
public static class Ease
{
    public static float Linear(float t) => Mathf.Clamp01(t);
    public static float InQuad(float t) { t = Mathf.Clamp01(t); return t * t; }
    public static float OutQuad(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
    public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
    public static float InOutCubic(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f; }
    public static float OutExpo(float t) { t = Mathf.Clamp01(t); return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t); }
    public static float OutBack(float t, float s = 1.70158f) { t = Mathf.Clamp01(t); float c3 = s + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + s * Mathf.Pow(t - 1f, 2f); }
    public static float InBack(float t, float s = 1.70158f) { t = Mathf.Clamp01(t); float c3 = s + 1f; return c3 * t * t * t - s * t * t; }
    public static float OutElastic(float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0f) return 0f; if (t >= 1f) return 1f;
        const float c4 = (2f * Mathf.PI) / 3f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
    }
    public static float OutBounce(float t)
    {
        t = Mathf.Clamp01(t);
        const float n1 = 7.5625f, d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1; return n1 * t * t + 0.984375f;
    }
    /// <summary>Damped spring impulse: starts at 1, wobbles to 0.</summary>
    public static float Wobble(float t, float freq = 14f, float damp = 6f)
    {
        if (t <= 0f) return 1f;
        return Mathf.Exp(-damp * t) * Mathf.Cos(freq * t);
    }
}
