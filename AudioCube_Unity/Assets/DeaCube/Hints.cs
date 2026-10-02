using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the onboarding bubbles can point at (SPEC v3 §1, §5.2): HUD rects and world points, by id. UIManager registers every
/// HUD element at build time; packages register their own panels; world targets return <see cref="None"/> while unavailable.
/// Package D adds: projected world points even when off screen (edge arrows) and a visibility test that respects CanvasGroup
/// fades and the screen bounds (a card slid off screen or a faded HUD is "not there yet").
/// </summary>
public static class Hints
{
    static readonly Dictionary<string, RectTransform> rects = new Dictionary<string, RectTransform>();
    static readonly Dictionary<string, Func<Vector3>> worlds = new Dictionary<string, Func<Vector3>>();
    static readonly Vector3[] corners = new Vector3[4];
    static readonly List<CanvasGroup> groups = new List<CanvasGroup>();

    public static readonly Vector3 None = new Vector3(float.NaN, float.NaN, float.NaN);
    /// <summary>World targets are a box of this many pixels around the projected point.</summary>
    public const float WorldBoxPx = 64f;

    public static void Register(string id, RectTransform rt)
    {
        if (string.IsNullOrEmpty(id) || rt == null) return;
        worlds.Remove(id);
        rects[id] = rt;
    }
    public static void Register(string id, Component c) { if (c != null) Register(id, c.transform as RectTransform); }
    public static void RegisterWorld(string id, Func<Vector3> worldPos)
    {
        if (string.IsNullOrEmpty(id) || worldPos == null) return;
        rects.Remove(id);
        worlds[id] = worldPos;
    }
    public static void Unregister(string id) { if (id == null) return; rects.Remove(id); worlds.Remove(id); }
    public static bool Has(string id) => id != null && (rects.ContainsKey(id) || worlds.ContainsKey(id));
    public static bool IsWorld(string id) => id != null && worlds.ContainsKey(id);
    public static IEnumerable<string> Ids { get { foreach (var k in rects.Keys) yield return k; foreach (var k in worlds.Keys) yield return k; } }
    public static RectTransform RectOf(string id) { RectTransform rt; return id != null && rects.TryGetValue(id, out rt) ? rt : null; }

    /// <summary>World position of a world target (NaN when missing or unavailable).</summary>
    public static Vector3 WorldOf(string id)
    {
        Func<Vector3> f;
        if (id == null || !worlds.TryGetValue(id, out f) || f == null) return None;
        try { return f(); } catch (Exception) { return None; }
    }

    /// <summary>Screen-pixel rect of a target: a HUD rect, or a 64 px box around a projected world point. False when the target is
    /// missing, inactive, zero-sized or behind the camera.</summary>
    public static bool TryGetScreenRect(string id, out Rect r)
    {
        r = default(Rect);
        if (id == null) return false;
        RectTransform rt;
        if (rects.TryGetValue(id, out rt))
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return r.width > 0.5f && r.height > 0.5f;
        }
        if (worlds.ContainsKey(id))
        {
            var cam = Camera.main;
            Vector3 w = WorldOf(id);
            if (cam == null || float.IsNaN(w.x)) return false;
            Vector3 s = cam.WorldToScreenPoint(w);
            if (s.z <= 0f) return false;
            r = new Rect(s.x - WorldBoxPx * 0.5f, s.y - WorldBoxPx * 0.5f, WorldBoxPx, WorldBoxPx);
            return true;
        }
        return false;
    }

    /// <summary>World target projected to the screen (px, z = depth). Behind the camera the point is mirrored so it still gives the
    /// direction for an edge arrow. False when the target is missing or not a world target.</summary>
    public static bool TryGetScreenPoint(string id, out Vector3 screen)
    {
        screen = Vector3.zero;
        if (id == null || !worlds.ContainsKey(id)) return false;
        var cam = Camera.main;
        Vector3 w = WorldOf(id);
        if (cam == null || float.IsNaN(w.x)) return false;
        screen = cam.WorldToScreenPoint(w);
        if (screen.z <= 0f)
        {
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 d = new Vector2(screen.x, screen.y) - c;
            screen = new Vector3(c.x - d.x * 50f, c.y - d.y * 50f, screen.z);
        }
        return true;
    }

    /// <summary>True when the target can be pointed at right now: it exists; a HUD rect is active, not faded out by its CanvasGroups
    /// (effective alpha ≥ 0.5) and at least half on screen; a world point projects in front of the camera (on or off screen).</summary>
    public static bool IsVisible(string id)
    {
        if (id == null) return false;
        RectTransform rt;
        if (rects.TryGetValue(id, out rt))
        {
            Rect r;
            if (!TryGetScreenRect(id, out r)) return false;
            if (EffectiveAlpha(rt) < 0.5f) return false;
            Rect scr = new Rect(0f, 0f, Screen.width, Screen.height);
            float ix = Mathf.Max(0f, Mathf.Min(r.xMax, scr.xMax) - Mathf.Max(r.xMin, scr.xMin));
            float iy = Mathf.Max(0f, Mathf.Min(r.yMax, scr.yMax) - Mathf.Max(r.yMin, scr.yMin));
            return ix * iy >= 0.5f * r.width * r.height;
        }
        Vector3 s;
        return TryGetScreenPoint(id, out s);
    }

    /// <summary>Product of the CanvasGroup alphas above a UI element (honours ignoreParentGroups); 0 when a canvas is disabled.</summary>
    public static float EffectiveAlpha(RectTransform rt)
    {
        if (rt == null) return 0f;
        float a = 1f;
        Transform t = rt;
        while (t != null)
        {
            groups.Clear();
            t.GetComponents(groups);
            bool stop = false;
            foreach (var g in groups) { if (!g.enabled) continue; a *= g.alpha; if (g.ignoreParentGroups) stop = true; }
            var cv = t.GetComponent<Canvas>();
            if (cv != null && !cv.enabled) return 0f;
            if (stop) break;
            t = t.parent;
        }
        return a;
    }
}
