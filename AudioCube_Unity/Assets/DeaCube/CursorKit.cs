using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SPEC v4 §2.5 / §5, research §4.8: hardware cursors drawn procedurally in the HUD's ink style, rotated −8°: MoveV (the name is kept; v5
/// SPEC §2.6 / §7: four chunky cream arrowheads on a cross, ink outline, hard shadow — "this island moves anywhere": up / down its column,
/// left / right into an earlier or later one), Grab (a cream closed mitten) and Draw (a brush tip in
/// the instrument colour, hotspot on the tip). Owners ask for a cursor and release it; the most recent live request wins (an owner asking
/// again moves its request to the top). <see cref="TickWorld"/> (UIManager's LateUpdate) is the world's owner: MoveV while the pointer rests
/// on an island's platform margin (PathManager.HoverGround) and nothing is being drawn, Grab while an island (or a deck card's ghost) is
/// dragged or the margin is pressed, Draw while a path is drawn. Textures are 32 px (64 on Retina), built once per kind (Draw: per colour).
/// v6 (SPEC v6 §8.2, package U2) — the hand shows at the pointer: with an EMPTY hand a tile under the pointer gets Point (a cream pointing
/// hand, hotspot on the fingertip: a click only plays the tile) and, the first few times it rests there, the words "press to hear"; a CUBE
/// in the hand rides the cursor as a small cube of its group's colour (tilted, bobbing on twos, a hard shadow, its voice pips) and a copied
/// PATTERN in the hand as a tiny paper card with the pattern's dots and line (HudClipboard.PaintPattern). The rider is drawn in the UI
/// (its own overlay canvas, so screenshots show it), follows the pointer smoothly, and leaves while a path is drawn (the brush cursor and the
/// hologram take over), while the world is locked (inspector, deck drag, menu, presentation, prompt) and when the hand is empty.
/// v7 (SPEC v7 §15, package U2) — "select the size of the cube THEN place it down": the cube riding the cursor IS the brush — its size is the
/// size the next placed node gets (PathManager.BrushTicks: px = 30 · SizeOf^1.5, a sixteenth tiny … a whole note fat) and a dotted brush
/// wears the satellite; while a path is drawn a small ECHO of the size row rides next to the cursor (the five sizes as tiny cubes, the chosen
/// one in the group's colour inside the ink hexagon, + the dot) unless the draft's own length row (DurationPicker, package D) is up. A pattern
/// picked up in a paste MODE (the clipboard chip's fan: PathManager.PatternMode) wears the mode on its card's corner (echo ↑ / echo ↓ / answer).
/// </summary>
public static class CursorKit
{
    public enum Kind { Default, MoveV, Grab, Draw, Point }

    /// <summary>The cursor on screen now.</summary>
    public static Kind Current { get; private set; }
    /// <summary>Number of Cursor.SetCursor calls (tests: cursors change only when the winner changes).</summary>
    public static int Applied { get; private set; }

    static readonly List<object> owners = new List<object>(8);
    static readonly List<Kind> kinds = new List<Kind>(8);
    static readonly object World = new object();
    static Color drawColor = Color.white, shownDraw = new Color(-1f, 0f, 0f);

    /// <summary>Asks for <paramref name="k"/> on behalf of <paramref name="owner"/> (Default = the same as Release).</summary>
    public static void Want(Kind k, object owner)
    {
        if (owner == null) return;
        if (k == Kind.Default) { Release(owner); return; }
        int i = owners.IndexOf(owner);
        if (i >= 0)
        {
            if (kinds[i] == k && i == owners.Count - 1) return;   // already the live top request
            if (kinds[i] == k) { Apply(); return; }                // an older request keeps its place (no churn)
            owners.RemoveAt(i); kinds.RemoveAt(i);
        }
        owners.Add(owner); kinds.Add(k);
        Apply();
    }

    public static void Release(object owner)
    {
        if (owner == null) return;
        int i = owners.IndexOf(owner);
        if (i < 0) return;
        owners.RemoveAt(i); kinds.RemoveAt(i);
        Apply();
    }

    /// <summary>The Draw cursor's paint colour (the instrument of the path being drawn).</summary>
    public static void SetDrawColor(Color c)
    {
        c.a = 1f;
        if ((c - drawColor).maxColorComponent < 0.01f && (drawColor - c).maxColorComponent < 0.01f) return;
        drawColor = c;
        if (Current == Kind.Draw) Apply();
    }

    /// <summary>Tests: the coverage (0..1) of cursor <paramref name="k"/>'s texture at a glyph-space point (before the −8° turn), e.g. an
    /// arrow tip — the v5 move cursor must have four.</summary>
    public static float CoverageAt(Kind k, Vector2 glyph)
    {
        Vector2 h;
        var t = TextureOf(k, out h);
        Vector2 px = HotOf(glyph, t.width);   // top-left origin
        return t.GetPixel(Mathf.Clamp(Mathf.RoundToInt(px.x), 0, t.width - 1), Mathf.Clamp(t.height - 1 - Mathf.RoundToInt(px.y), 0, t.height - 1)).a;
    }

    /// <summary>Tests: writes the cursors side by side (move, grab, draw, v6 point; on a sky-violet backdrop) to a PNG so they can be looked at.</summary>
    public static bool SavePreview(string path, Color brush)
    {
        var keep = drawColor; drawColor = brush; brush.a = 1f;
        Vector2 h;
        var a = TextureOf(Kind.MoveV, out h); var b = TextureOf(Kind.Grab, out h); var c = TextureOf(Kind.Draw, out h); var d = TextureOf(Kind.Point, out h);
        drawColor = keep;
        int n = a.width, pad = n / 4, w = n * 4 + pad * 5, hh = n + pad * 2;
        var o = new Texture2D(w, hh, TextureFormat.RGBA32, false);
        var bg = new Color(0.36f, 0.30f, 0.62f, 1f);
        var fill = new Color32[w * hh]; for (int i = 0; i < fill.Length; i++) fill[i] = bg; o.SetPixels32(fill);
        var texs = new[] { a, b, c, d };
        for (int k = 0; k < 4; k++)
        {
            var src = texs[k].GetPixels();
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var sc = src[y * n + x]; int ox = pad + k * (n + pad) + x, oy = pad + y;
                o.SetPixel(ox, oy, Color.Lerp(o.GetPixel(ox, oy), new Color(sc.r, sc.g, sc.b, 1f), sc.a));
            }
        }
        o.Apply(false, false);
        try { System.IO.File.WriteAllBytes(path, o.EncodeToPNG()); return true; }
        catch (System.Exception) { return false; }
        finally { Object.Destroy(o); }
    }

    /// <summary>Tests / teardown: drops every request and restores the system cursor.</summary>
    public static void ResetAll() { owners.Clear(); kinds.Clear(); Apply(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        owners.Clear(); kinds.Clear(); Current = Kind.Default; shownDraw = new Color(-1f, 0f, 0f);
        rider = null; riderKind = 0; pressBase = 0; pointSince = -1f; hintOn = false;
    }

    static void Apply()
    {
        Kind k = owners.Count > 0 ? kinds[kinds.Count - 1] : Kind.Default;
        if (k == Current && (k != Kind.Draw || shownDraw == drawColor)) return;
        Current = k;
        Applied++;
        if (k == Kind.Default) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        Vector2 hot;
        var tex = TextureOf(k, out hot);
        if (k == Kind.Draw) shownDraw = drawColor;
        Cursor.SetCursor(tex, hot, CursorMode.Auto);
    }

    // ------------------------------------------------------------------ the world's cursor
    /// <summary>The world's request, from the editor state (UIManager.LateUpdate). <paramref name="gripHeld"/>: the island header's grip is
    /// pressed (its drag has not begun yet).</summary>
    public static void TickWorld(bool gripHeld)
    {
        var pm = PathManager.I;
        Kind want = Kind.Default;
        if (pm != null && !WorldInput.WorldLocked)
        {
            var drag = pm.Drag;
            if (pm.IsDrawing)
            {
                want = Kind.Draw;
                var d = pm.Draft;
                SetDrawColor(d != null ? d.Color : Instruments.Colors[Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1)]);
            }
            else if (gripHeld || (drag != null && (drag.Dragging || drag.State == IslandDrag.Phase.Armed))) want = Kind.Grab;
            else if (pm.HoverGround && !InputUtil.PointerOverUI) want = Kind.MoveV;
            else if (pm.HoverPressTile != null && pm.Candidate == null && !InputUtil.PointerOverUI) want = Kind.Point;   // v6: an empty-hand click only plays it
        }
        else if (WorldInput.IsLockedBy("tray") && IslandTray.I != null && IslandTray.I.Dragging) want = Kind.Grab;   // a deck card's ghost in the world
        if (want == Kind.Default) Release(World); else Want(want, World);
        TickPressHint(want == Kind.Point);
        TickRider();
    }

    // ------------------------------------------------------------------ v6 the hand at the pointer (SPEC v6 §8.2)
    /// <summary>What rides the cursor now: 0 nothing, 1 a cube (the hand holds one), 2 a pattern card.</summary>
    public static int RiderKind => riderKind;
    public static bool RiderShown => riderKind != 0;
    /// <summary>The rider's centre in screen px (tests: it follows the pointer, below-right of the hotspot).</summary>
    public static Vector2 RiderScreen { get { if (rider == null || riderKind == 0) return new Vector2(-1f, -1f); return RectTransformUtility.WorldToScreenPoint(null, rider.Body.position); } }
    /// <summary>The riding cube's colour / voice pips / current bob (px) and tilt (deg) — tests.</summary>
    public static Color RiderColor => rider != null ? rider.CubeColor : Color.clear;
    public static int RiderVoice => rider != null ? rider.Voice : 0;
    /// <summary>v7: the riding cube's drawn size (reference px; 30 = a quarter note), whether it wears the dotted satellite, the paste mode
    /// its pattern card shows, and whether the size echo rides the cursor (while drawing) with which size / dot.</summary>
    public static float RiderCubePx => rider != null ? rider.CubeSizePx : 0f;
    public static bool RiderDotted => rider != null && rider.Dotted;
    public static int RiderMode => rider != null ? rider.Mode : 0;
    public static bool EchoShown => rider != null && rider.EchoOn;
    public static int EchoSize => rider != null ? rider.EchoSize : -1;
    public static bool EchoDotted => rider != null && rider.EchoDotted;
    public static Vector2 EchoScreen { get { if (rider == null || !rider.EchoOn) return new Vector2(-1f, -1f); return RectTransformUtility.WorldToScreenPoint(null, rider.Echo.position); } }
    public static float RiderBob => rider != null ? rider.Bob : 0f;
    public static float RiderTilt => rider != null && rider.Body != null ? Mathf.DeltaAngle(0f, rider.Body.localEulerAngles.z) : 0f;
    /// <summary>Where the rider sits from the pointer (reference px): below-right of the arrow's tip, clear of it.</summary>
    public static readonly Vector2 RiderOffset = new Vector2(30f, -32f);
    /// <summary>"press to hear" shows while the pointing hand rests on tiles, until the player has pressed tiles this many times.</summary>
    public const int PressHintTimes = 3;
    public const float PressHintDelay = 0.6f;
    public const string PressHintWords = "press to hear";
    /// <summary>Tile presses since the words may show (PathManager.PressCount counts every empty-hand press).</summary>
    public static int PressCount => PathManager.PressCount - pressBase;
    /// <summary>The "press to hear" words are asked for now (InkCaption shows them after its own delay).</summary>
    public static bool PressHintShown => hintOn;
    /// <summary>Tests: the words may show again.</summary>
    public static void ResetPressHint() { pressBase = PathManager.PressCount; pointSince = -1f; }

    static CursorRider rider;
    static int riderKind, pressBase;
    static float pointSince = -1f; static bool hintOn;

    static Vector2 PointerScreen => PathManager.SimOnly ? (Vector2)PathManager.SimPos : (Vector2)Input.mousePosition;

    static void TickPressHint(bool point)
    {
        bool want = false;
        if (point && PressCount < PressHintTimes)
        {
            if (pointSince < 0f) pointSince = Time.unscaledTime;
            want = Time.unscaledTime - pointSince >= PressHintDelay;
        }
        else pointSince = -1f;
        if (want == hintOn) return;
        hintOn = want;
        var r = EnsureRider();
        if (r == null) return;
        if (want) { r.Place(PointerScreen); InkCaption.Show(r.Hint, PressHintWords); }
        else InkCaption.Hide(r.Hint);
    }

    static void TickRider()
    {
        var pm = PathManager.I;
        int want = 0; bool echo = false;
        if (pm != null && !WorldInput.WorldLocked && !Presenter.Active && !MainMenu.IsShown)
        {
            if (!pm.IsDrawing)
            {
                if (pm.Hand == PathManager.HandKind.Cube) want = 1;
                else if (pm.Hand == PathManager.HandKind.Pattern && Clipboard.HasPattern) want = 2;
            }
            else echo = !DraftRowNear(PointerScreen);   // v7: next to the draft's own length row the row itself is the echo
        }
        Vector2 at = PointerScreen;
        if (at.x < 0f || at.y < 0f || at.x > Screen.width || at.y > Screen.height) { want = 0; echo = false; }
        var r = want != 0 || echo || riderKind != 0 || hintOn || (rider != null && rider.EchoOn) ? EnsureRider() : rider;
        if (r == null) { riderKind = 0; return; }
        int g = pm != null ? Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1) : 0;
        int brush = pm != null ? pm.BrushTicks : 24;
        if (want == 1) r.ShowCube(g, brush);
        else if (want == 2) r.ShowPattern(pm.PatternMode);
        else r.HideBody();
        var d = pm != null ? pm.Draft : null;
        r.SetEcho(echo, d != null ? d.Color : Instruments.Colors[g], brush);
        riderKind = want;
        if (want != 0 || hintOn || echo) r.Place(at);
    }

    /// <summary>v7: the pointer is on / near (70 reference px) the draft's floating length row (DurationPicker, drafting): the size echo stays away.</summary>
    static bool DraftRowNear(Vector2 at)
    {
        if (!DurationPicker.IsShown || DurationPicker.Mode != DurationPicker.Where.Draft) return false;
        var r = DurationPicker.RowScreenRect;
        if (r.width <= 1f) return false;
        float m = 70f * Mathf.Max(0.3f, Screen.height / 1080f);
        return at.x > r.xMin - m && at.x < r.xMax + m && at.y > r.yMin - m && at.y < r.yMax + m;
    }

    static CursorRider EnsureRider()
    {
        if (rider != null) return rider;
        var go = new GameObject("CursorRider", typeof(RectTransform));
        rider = go.AddComponent<CursorRider>();
        rider.Build();
        return rider;
    }

    // ------------------------------------------------------------------ drawing (signed distances in [-1, 1] glyph space)
    static readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();
    static readonly Color Cream = new Color(1f, 0.953f, 0.902f, 1f);

    static int Px { get { float d = Screen.dpi; return d > 150f ? 64 : 32; } }

    static Texture2D TextureOf(Kind k, out Vector2 hotspot)
    {
        int n = Px;
        Color32 key32 = drawColor;
        int key = (int)k * 1000003 + n * 7 + (k == Kind.Draw ? (key32.r << 16 | key32.g << 8 | key32.b) * 31 : 0);
        // hotspots in texture pixels (top-left origin, as Cursor.SetCursor expects)
        if (k == Kind.Draw) hotspot = HotOf(new Vector2(-0.70f, -0.70f), n);   // the brush tip (lower-left)
        else if (k == Kind.Point) hotspot = HotOf(PointTip, n);                 // the fingertip
        else hotspot = new Vector2(n * 0.5f, n * 0.5f);
        Texture2D t;
        if (cache.TryGetValue(key, out t) && t != null) return t;
        t = Render(k, n);
        cache[key] = t;
        return t;
    }

    /// <summary>A glyph-space point (after the −8° turn) → texture pixel, top-left origin.</summary>
    static Vector2 HotOf(Vector2 g, int n)
    {
        // Render samples glyph p = R(+8°)·q·1.12 at texture point q, so the texture point of a glyph point is q = R(−8°)·p / 1.12
        float a = Rot * Mathf.Deg2Rad;
        g /= 1.12f;
        Vector2 r = new Vector2(g.x * Mathf.Cos(a) - g.y * Mathf.Sin(a), g.x * Mathf.Sin(a) + g.y * Mathf.Cos(a));
        return new Vector2((r.x * 0.5f + 0.5f) * n, (1f - (r.y * 0.5f + 0.5f)) * n);
    }

    const float Rot = -8f;
    /// <summary>The pointing hand's fingertip in glyph space (its hotspot).</summary>
    static readonly Vector2 PointTip = new Vector2(-0.20f, 0.86f);

    static float Shape(Kind k, Vector2 p, out int part)
    {
        part = 0;
        switch (k)
        {
            case Kind.MoveV:
            {
                // a chunky ✥: four arrowheads on a fat cross (v5: the island moves anywhere — up / down its column, left / right in time)
                const float a = 0.24f, b = 0.50f, t = 0.92f, w = 0.10f;   // narrow heads: clear gaps between the arms at 32 px
                float up = IconFactory.P.Tri(p, -a, b, a, b, 0f, t);
                float dn = IconFactory.P.Tri(p, -a, -b, 0f, -t, a, -b);
                float lf = IconFactory.P.Tri(p, -b, a, -t, 0f, -b, -a);
                float rt = IconFactory.P.Tri(p, b, -a, t, 0f, b, a);
                float cross = IconFactory.P.U(IconFactory.P.Box(p, 0f, 0f, w, b + 0.02f, 0.03f), IconFactory.P.Box(p, 0f, 0f, b + 0.02f, w, 0.03f));
                return IconFactory.P.U(up, dn, lf, rt, cross) - 0.02f;
            }
            case Kind.Grab:
            {
                // a closed mitten: a round palm, a knuckle ridge on top, the thumb folded over the front
                float palm = IconFactory.P.Box(p, 0.02f, -0.10f, 0.50f, 0.46f, 0.30f);
                float knuckles = IconFactory.P.U(IconFactory.P.Circle(p, -0.30f, 0.34f, 0.20f), IconFactory.P.Circle(p, 0.02f, 0.40f, 0.21f), IconFactory.P.Circle(p, 0.34f, 0.33f, 0.19f));
                float thumb = IconFactory.P.Seg(p, -0.52f, -0.02f, -0.18f, -0.30f, 0.17f);
                float cuff = IconFactory.P.Box(p, 0.04f, -0.66f, 0.40f, 0.13f, 0.06f);
                float body = IconFactory.P.U(palm, knuckles, thumb, cuff);
                if (thumb < 0.02f) part = 1;
                return body;
            }
            case Kind.Point:
            {
                // v6: a pointing hand — the index finger up (slightly left), the other fingers curled into the fist, the thumb along its left side
                float finger = IconFactory.P.Seg(p, -0.20f, -0.12f, -0.20f, 0.70f, 0.155f);
                float fist = IconFactory.P.Box(p, 0.10f, -0.34f, 0.40f, 0.30f, 0.22f);
                float curls = IconFactory.P.U(IconFactory.P.Circle(p, 0.08f, -0.06f, 0.155f), IconFactory.P.Circle(p, 0.32f, -0.10f, 0.15f), IconFactory.P.Circle(p, 0.52f, -0.20f, 0.13f));
                float thumb = IconFactory.P.Seg(p, -0.26f, -0.46f, -0.50f, -0.20f, 0.13f);
                float cuff = IconFactory.P.Box(p, 0.12f, -0.76f, 0.34f, 0.11f, 0.05f);
                float body = IconFactory.P.U(finger, fist, curls, thumb, cuff);
                if (curls < 0.02f && finger > 0.0f) part = 1;
                return body;
            }
            default:
            {
                // a brush: handle up-right, a ferrule, the tip (instrument colour) at the lower-left
                float handle = IconFactory.P.Seg(p, 0.10f, 0.10f, 0.72f, 0.72f, 0.13f);
                float ferrule = IconFactory.P.Seg(p, -0.12f, -0.12f, 0.12f, 0.12f, 0.17f);
                float tip = IconFactory.P.U(IconFactory.P.Seg(p, -0.36f, -0.36f, -0.10f, -0.10f, 0.19f), IconFactory.P.Tri(p, -0.49f, -0.28f, -0.28f, -0.49f, -0.72f, -0.72f));
                if (tip < ferrule && tip < handle) part = 2; else if (ferrule < handle) part = 3;
                return IconFactory.P.U(handle, ferrule, tip);
            }
        }
    }

    static Texture2D Render(Kind k, int n)
    {
        var px = new Color32[n * n];
        Color ink = Look.InkColor;
        float texel = 2f / n;                       // glyph units per pixel
        float inkW = (n >= 64 ? 3.2f : 1.9f) * texel;
        Vector2 shadowOff = new Vector2(0.09f, -0.11f) * (n >= 64 ? 1f : 1.2f);
        float cs = Mathf.Cos(-Rot * Mathf.Deg2Rad), sn = Mathf.Sin(-Rot * Mathf.Deg2Rad);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 q = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                Vector2 p = new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs) * 1.12f;   // turned −8°, a little padding
                int part;
                float d = Shape(k, p, out part);
                int dummy;
                float ds = Shape(k, p - shadowOff, out dummy);
                float aFill = Mathf.Clamp01(0.5f - d / texel);
                float aInk = Mathf.Clamp01(0.5f - (d - inkW) / texel);
                float aShadow = Mathf.Clamp01(0.5f - (ds - inkW) / texel) * 0.85f;
                Color fill = part == 2 ? drawColor : (part == 3 ? new Color(0.72f, 0.70f, 0.80f) : Cream);
                if ((k == Kind.Grab || k == Kind.Point) && part == 1) fill = Color.Lerp(Cream, new Color(0.93f, 0.86f, 0.80f), 0.6f);   // the folded thumb / curled fingers a shade darker
                // composite: shadow, then ink, then fill (straight alpha)
                Color c = new Color(ink.r, ink.g, ink.b, aShadow);
                c = Over(new Color(ink.r, ink.g, ink.b, aInk), c);
                c = Over(new Color(fill.r, fill.g, fill.b, aFill), c);
                px[y * n + x] = c;   // Texture2D rows run bottom-up, as glyph y does
            }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "cursor_" + k, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }

    static Color Over(Color top, Color under)
    {
        float a = top.a + under.a * (1f - top.a);
        if (a < 1e-5f) return new Color(0f, 0f, 0f, 0f);
        Color c = (top * top.a + under * under.a * (1f - top.a)) / a;
        c.a = a;
        return c;
    }
}

/// <summary>
/// v6 (SPEC v6 §8.2, package U2): what the hand holds, drawn at the pointer on its own overlay canvas (above the HUD, the inspector and the
/// tutorial, under the captions): a small cube in the held group's colour (tilted −14°, a hard ink shadow, its voice pips, bobbing on twos)
/// or a tiny paper card with the copied pattern's dots and line. Driven by <see cref="CursorKit"/> (UIManager.LateUpdate); it only moves
/// (smoothly, with the pointer) and redraws on twos — nothing else per frame.
/// </summary>
public class CursorRider : MonoBehaviour
{
    public const int SortOrder = 390;
    /// <summary>The riding cube at a quarter-note brush (the size row's px formula: 30 · SizeOf^1.5).</summary>
    public const float CubePx = 30f;
    /// <summary>v7: the size echo's cubes relative to the size row's (DurationPicker.SlotPx) and where it rides from the pointer (reference px).</summary>
    public const float EchoScale = 0.36f;
    public static readonly Vector2 EchoOffset = new Vector2(46f, -34f);
    Canvas canvas; RectTransform root, anchor, hint, body, cubeRt, cardRt, echoRt;
    CubeGlyph cube, shadow; InkPainter mark, cardArt, echoArt; InkShape card;
    int group = -1, voice = -1, tick = int.MinValue, patternSig; float bob;
    // v7: the brush on the riding cube, the paste mode on the pattern card, the size echo while drawing
    int brushShown = -1, mode, echoSize = -1; bool dotted, echoOn, echoDotted; float cubePx = CubePx; Color echoCol = Color.white;

    public RectTransform Anchor => anchor;
    public RectTransform Echo => echoRt;
    public float CubeSizePx => cubeRt != null && cubeRt.gameObject.activeSelf ? cubePx : 0f;
    public bool Dotted => dotted;
    public int Mode => cardRt != null && cardRt.gameObject.activeSelf ? mode : 0;
    public bool EchoOn => echoOn;
    public int EchoSize => echoSize;
    public bool EchoDotted => echoDotted;
    /// <summary>The caption target of "press to hear" (just above-right of the pointer's tip).</summary>
    public RectTransform Hint => hint;
    public RectTransform Body => body;
    public Color CubeColor => cube != null && cubeRt.gameObject.activeSelf ? cube.color : Color.clear;
    public int Voice => voice < 0 ? 0 : voice;
    public float Bob => bob;

    public void Build()
    {
        gameObject.layer = 5;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = SortOrder;
        var sc = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        root = (RectTransform)transform;
        anchor = HudKit.Node(root, "Anchor", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        hint = HudKit.Node(root, "Hint", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
        body = HudKit.Node(anchor, "Body", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 56f));
        cubeRt = HudKit.Node(body, "Cube", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CubePx + 8f, CubePx + 8f));
        shadow = CubeGlyph.Create(cubeRt, "Shadow", Comic.A(Comic.Ink, 0.85f), CubePx);
        shadow.raycastTarget = false; shadow.rectTransform.anchoredPosition = new Vector2(4f, -5f);
        cube = CubeGlyph.Create(cubeRt, "Glyph", Color.white, CubePx);
        cube.raycastTarget = false;
        var mk = HudKit.Node(cube.rectTransform, "Mark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CubePx, CubePx));
        mark = mk.gameObject.AddComponent<InkPainter>(); mark.raycastTarget = false;
        mark.onPaint = p => { Rect r = p.Area; VoiceMark.Paint(p, r.center, Mathf.Min(r.width, r.height), Voice, Comic.Ink); };
        cardRt = HudKit.Node(body, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 42f));
        card = cardRt.gameObject.AddComponent<InkShape>();
        card.Shape = InkShape.Kind.RoundRect; card.color = Comic.Cream; card.Radius = 7f; card.SetInk(1.6f, 2.6f); card.raycastTarget = false;
        card.ShadowOffset = new Vector2(3f, -4f); card.ShadowColor = Comic.PrintShadow; card.Seed = 7719;
        var ca = HudKit.Node(cardRt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 42f));
        cardArt = ca.gameObject.AddComponent<InkPainter>(); cardArt.raycastTarget = false;
        cardArt.onPaint = p =>
        {
            if (!Clipboard.HasPattern) return;
            Rect r = p.Area;
            Color col = Comic.Opaque(Instruments.Colors[Mathf.Clamp(Clipboard.Instrument, 0, Instruments.Count - 1)]);
            HudClipboard.PaintPattern(p, new Rect(r.xMin + 9f, r.yMin + 8f, r.width - 18f, r.height - 16f), col, Clipboard.Midi, Clipboard.State, 0.72f);
            if (mode != 0) PasteFan.PaintModeBadge(p, new Vector2(r.xMax - 4f, r.yMax - 3f), mode, col);   // v7: the paste mode on the corner
        };
        // v7: the size echo (drawn by one painter, repainted only when the brush or the colour changes)
        echoRt = HudKit.Node(root, "SizeEcho", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(EchoWidth() + 12f, 34f));
        echoArt = echoRt.gameObject.AddComponent<InkPainter>(); echoArt.raycastTarget = false;
        echoArt.onPaint = PaintEcho;
        echoRt.gameObject.SetActive(false);
        cubeRt.gameObject.SetActive(false); cardRt.gameObject.SetActive(false);
    }

    public void ShowCube(int g) { ShowCube(g, PathManager.I != null ? PathManager.I.BrushTicks : 24); }

    /// <summary>The held group's cube at the brush's size (<paramref name="brushTicks"/>; the satellite when dotted).</summary>
    public void ShowCube(int g, int brushTicks)
    {
        if (!cubeRt.gameObject.activeSelf) { cubeRt.gameObject.SetActive(true); tick = int.MinValue; }
        if (cardRt.gameObject.activeSelf) cardRt.gameObject.SetActive(false);
        int v = HudInstruments.VoicesOf(g) < 2 ? 0 : HudInstruments.BrushOf(g);
        if (g != group) { group = g; cube.color = Comic.Opaque(Instruments.Colors[g]); cube.Hop(0.35f); }
        bool rnd = PathManager.I != null && PathManager.I.BrushSphere;   // SPHERE: the piece in the hand is a ball
        if (cube.Round != rnd) { cube.Round = rnd; shadow.Round = rnd; cube.Hop(0.35f); }
        if (v != voice) { voice = v; mark.Repaint(); }
        if (brushTicks != brushShown)
        {
            bool first = brushShown < 0;
            brushShown = brushTicks;
            cubePx = DurationPicker.SizePx(brushTicks / (float)ProjectConfig.TicksPerBeat);
            dotted = AudioCube.IsDottedTicks(brushTicks);
            var sz = new Vector2(cubePx, cubePx);
            cube.rectTransform.sizeDelta = sz; shadow.rectTransform.sizeDelta = sz; mark.rectTransform.sizeDelta = sz;
            cubeRt.sizeDelta = sz + new Vector2(8f, 8f);
            shadow.rectTransform.anchoredPosition = new Vector2(4f, -5f) * Mathf.Clamp(cubePx / CubePx, 0.6f, 1.4f);
            cube.Satellite = dotted;
            mark.Repaint();
            if (!first) cube.Hop(0.3f);   // a new size: the cube in the hand hops
        }
        Animate();
    }

    public void ShowPattern() { ShowPattern(Clipboard.PasteMode.Plain); }

    /// <summary>The copied pattern's card; a pattern picked up in a paste mode wears it on the corner.</summary>
    public void ShowPattern(Clipboard.PasteMode m)
    {
        if (!cardRt.gameObject.activeSelf) { cardRt.gameObject.SetActive(true); tick = int.MinValue; patternSig = 0; }
        if (cubeRt.gameObject.activeSelf) { cubeRt.gameObject.SetActive(false); group = -1; }
        int sig = (Clipboard.State != null ? Clipboard.State.GetHashCode() : 0) * 7 + (int)m;
        mode = (int)m;
        if (sig != patternSig) { patternSig = sig; cardArt.Repaint(); }
        Animate();
    }

    /// <summary>v7: the size echo on / off (while drawing), in the draft's colour, showing <paramref name="brushTicks"/>.</summary>
    public void SetEcho(bool on, Color col, int brushTicks)
    {
        if (on != echoOn) { echoOn = on; echoRt.gameObject.SetActive(on); if (on) { echoSize = -1; } }
        if (!on) return;
        int s; bool d;
        HudSizeRow.Decode(brushTicks, out s, out d);
        col = Comic.Opaque(col);
        if (s != echoSize || d != echoDotted || col != echoCol) { echoSize = s; echoDotted = d; echoCol = col; echoArt.Repaint(); }
    }

    static float EchoWidth()
    {
        float w = 0f;
        for (int i = 0; i < 5; i++) w += DurationPicker.SlotPx(i) * EchoScale + 4f;
        return w + 10f;
    }

    /// <summary>The five sizes as tiny cubes on a hairline shelf (the chosen one in colour inside the ink hexagon, the rest faint outlines), the
    /// dot after them (filled when dotted): the size row, small, for the eye that is on the cursor.</summary>
    void PaintEcho(InkPainter p)
    {
        if (echoSize < 0) return;
        Rect r = p.Area;
        float y0 = r.yMin + 8f, x = r.xMin + 6f;
        p.RoundRect(new Rect(r.xMin + 1f, r.yMin + 2f, r.width - 2f, r.height - 4f), 9f, Comic.A(Comic.Cream, 0.9f), 1.4f, Comic.A(Comic.Ink, 0.85f));
        p.Line(new Vector2(r.xMin + 5f, y0), new Vector2(r.xMax - 16f, y0), 1.2f, Comic.A(Comic.Ink, 0.6f));
        for (int i = 0; i < 5; i++)
        {
            float px = DurationPicker.SlotPx(i) * EchoScale;
            Vector2 c = new Vector2(x + px * 0.5f, y0 + px * 0.5f + 1f);
            if (i == echoSize)
            {
                ringPts.Clear();
                for (int k = 0; k < 6; k++) { float a = (30f + 60f * k) * Mathf.Deg2Rad; ringPts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (px * 0.5f + 3.2f)); }
                p.Stroke(ringPts, 6, 1.4f, Comic.Ink, true);
                p.Cube(c, px, echoCol, Comic.Ink, 1.1f);
            }
            else p.Cube(c, px, Comic.A(Comic.Ink, 0.35f), Comic.A(Comic.Ink, 0.45f), 0.9f, true);
            x += px + 4f;
        }
        Vector2 dc = new Vector2(r.xMax - 9f, y0 + 5f);
        p.Disc(dc, 3.4f, Comic.Ink);
        if (!echoDotted) p.Disc(dc, 2f, Comic.Cream);
    }
    static readonly System.Collections.Generic.List<Vector2> ringPts = new System.Collections.Generic.List<Vector2>(8);

    public void HideBody()
    {
        if (cubeRt.gameObject.activeSelf) cubeRt.gameObject.SetActive(false);
        if (cardRt.gameObject.activeSelf) cardRt.gameObject.SetActive(false);
        group = -1; bob = 0f;
    }

    /// <summary>The anchor at the pointer (screen px) + the offset (reference px) — every frame, smooth.</summary>
    public void Place(Vector2 screen)
    {
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out lp)) return;
        anchor.anchoredPosition = lp + CursorKit.RiderOffset;
        hint.anchoredPosition = lp + new Vector2(20f, 16f);
        if (echoOn) echoRt.anchoredPosition = lp + EchoOffset;
    }

    /// <summary>The bob and the tilt, redrawn on twos (a held thing: it hangs a little below the pointer and swings).</summary>
    void Animate()
    {
        int t = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps);
        if (t == tick) return;
        tick = t;
        float ph = t / Look.TwosFps;
        bob = Mathf.Round(3f * Mathf.Sin(ph * Mathf.PI * 2f * 1.4f));
        body.anchoredPosition = new Vector2(0f, bob);
        float tilt = cubeRt.gameObject.activeSelf ? -14f + 3f * Mathf.Sin(ph * Mathf.PI * 2f * 0.7f) : 6f + 2f * Mathf.Sin(ph * Mathf.PI * 2f * 0.7f);
        body.localEulerAngles = new Vector3(0f, 0f, Mathf.Round(tilt));
    }
}
