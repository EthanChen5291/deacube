using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Flow E2 — A QUIET WORLD (the owner: "so it's not just like a billion grids on the thing"; "BE SIMPLE AND MINIMALISTIC"): the eye should land
/// on the bars where something NEW happens. A chord grid goes to REST when it has nothing new to say:
///  · an EMPTY grid (no cube lives on it, none walks through it): its tiles rest flush (the pitch staircase lies down to the grid's lowest tile)
///    and it is a step paler (tiles and platform toward their own grey);
///  · a REPEATED grid — its cubes are the same pattern as the grid before it in its lane (same instruments, sounds, tiles, lengths, rests) —
///    takes the same calm look (the tiles its cubes stand on keep their height, so nothing floats) and wears ONE small repeat mark (the "%"-like
///    measure-repeat sign 𝄎) on its front edge.
/// A grid at rest WAKES — rises back to normal over a quarter of a second — when the pointer is over it, when a cube or a pattern is in the hand
/// (every grid: the player is about to place notes and reads pitch as height), while its column plays, while it is selected, dragged or its
/// ribbon is up. Sleeping (muted) grids, long grids, keyboards, staircases, melody rolls, Moons, the present mode and anything under the sea keep
/// their own looks: this one stays out of their way.
/// VISUAL ONLY: no layout, timing or save changes — a tile's <see cref="TileInteraction.Top"/>, its pitch and its collider stay where they are
/// (clicks and rays land as before), the cubes do not move, nothing is written to the song.
/// The scan (which grids rest) runs only when the song's grids or cubes changed, and never while a cube is in the hand or a path is drawn
/// (every grid stands up then); per frame it is one pass over the grids.
/// </summary>
[DefaultExecutionOrder(960)]
public class QuietGrids : MonoBehaviour
{
    public static QuietGrids I { get; private set; }
    /// <summary>The switch (on by default; off = every grid looks as it always did).</summary>
    public static bool Enabled = true;
    public enum Rest { None, Empty, Repeat }
    /// <summary>How far the platform goes toward its own grey / toward white at rest (the tiles: TileInteraction.QuietKeep / QuietLighten).</summary>
    public const float PlatformGrey = 0.34f, PlatformLighten = 0.16f;
    /// <summary>Seconds of the wake / rest ease of the platform and the mark (the tiles ease themselves), and the scan's debounce.</summary>
    public const float EaseS = 0.25f, ScanEvery = 0.12f, HoldS = 0.35f;
    /// <summary>The repeat mark: its size on the platform (world units) and its ink.</summary>
    public const float MarkSize = 0.88f;
    /// <summary>Scans so far (tests: only when something changed).</summary>
    public static int Scans { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (I == null && Application.isPlaying) I = new GameObject("QuietGrids").AddComponent<QuietGrids>(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Enabled = true; Scans = 0; markTex = null; markMat = null; }

    class State
    {
        public KeyBlock kb; public Rest rest; public KeyBlock before; public string sig = "";
        public float target = -1f, shown, holdUntil; public bool push = true; public float flushY;
        public readonly HashSet<TileInteraction> stand = new HashSet<TileInteraction>();   // the tiles its cubes stand on (they keep their height)
        public Renderer mark; public MaterialPropertyBlock mpb; public TileInteraction tile0; public int tileCount;
    }
    readonly Dictionary<KeyBlock, State> states = new Dictionary<KeyBlock, State>();
    readonly List<State> list = new List<State>();
    readonly Dictionary<KeyBlock, List<string>> sigs = new Dictionary<KeyBlock, List<string>>();
    readonly HashSet<KeyBlock> touched = new HashSet<KeyBlock>();
    bool dirty = true; float scanAt; int worldKey = int.MinValue;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake() { I = this; AudioCube.OnAnyChanged += HandleCube; }
    void OnDestroy() { AudioCube.OnAnyChanged -= HandleCube; if (I == this) I = null; }
    void HandleCube(AudioCube c) { dirty = true; }

    // ------------------------------------------------------------------ what tests / other looks read
    static State Of(KeyBlock kb) { State s; return I != null && kb != null && I.states.TryGetValue(kb, out s) ? s : null; }
    /// <summary>Why <paramref name="kb"/> may rest (None: it never does).</summary>
    public static Rest RestOf(KeyBlock kb) { var s = Of(kb); return s != null ? s.rest : Rest.None; }
    /// <summary>The grid before <paramref name="kb"/> in its lane that it repeats (null: it does not).</summary>
    public static KeyBlock Repeats(KeyBlock kb) { var s = Of(kb); return s != null && s.rest == Rest.Repeat ? s.before : null; }
    /// <summary>The grid is at rest right now (its goal: the look eases there).</summary>
    public static bool Resting(KeyBlock kb) { var s = Of(kb); return s != null && s.target > 0.5f; }
    /// <summary>The quiet look <paramref name="kb"/> shows now (0 normal … 1 at rest).</summary>
    public static float Shown(KeyBlock kb) { var s = Of(kb); return s != null ? s.shown : 0f; }
    /// <summary>The repeat mark on <paramref name="kb"/>'s front edge (null: none is drawn).</summary>
    public static Transform Mark(KeyBlock kb) { var s = Of(kb); return s != null && s.mark != null && s.mark.gameObject.activeSelf ? s.mark.transform : null; }
    /// <summary>Tests: scans now (which grids rest) and applies this frame's look.</summary>
    public static void ScanNow() { if (I != null) { I.Scan(); I.Step(0f); } }
    /// <summary>Grids at rest now, by reason (tests / captures).</summary>
    public static void Count(out int empty, out int repeat, out int awake)
    {
        empty = repeat = awake = 0;
        if (I == null) return;
        foreach (var s in I.list) { if (s.target > 0.5f) { if (s.rest == Rest.Empty) empty++; else repeat++; } else awake++; }
    }

    /// <summary>The pattern of the cubes living on <paramref name="kb"/> as one string (order-free): instrument, sound, layer, octave, span, mode and
    /// per note its tile, length, sticker (rest …), bend and nudge. "" = no cube lives on it.</summary>
    public static string PatternOf(KeyBlock kb)
    {
        var parts = new List<string>();
        foreach (var c in SequenceMaster.Cubes) if (Lives(c, kb)) parts.Add(CubeSig(c));
        parts.Sort(string.CompareOrdinal);
        return string.Join("|", parts.ToArray());
    }

    static bool Lives(AudioCube c, KeyBlock kb) { return c != null && c.isFinalized && !c.rider && !c.IsOnMoon && c.Island == kb; }

    static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
    static string CubeSig(AudioCube c)
    {
        sb.Length = 0;
        sb.Append(c.instrument).Append('.').Append(c.voice).Append('.').Append(c.layer).Append('.').Append(c.octave).Append('.').Append(c.span).Append('.').Append((int)c.mode)
          .Append(c.reverse ? 'r' : 'f').Append(c.muted ? 'm' : 's').Append(':');
        bool cells = c.gridX.Count == c.nodes.Count && c.gridZ.Count == c.nodes.Count;
        for (int i = 0; i < c.nodes.Count; i++)
        {
            var t = c.nodes[i];
            int x = cells ? c.gridX[i] : (t != null ? t.gridX : -1), z = cells ? c.gridZ[i] : (t != null ? t.gridZ : -1);
            sb.Append(x).Append(',').Append(z).Append(',').Append(i < c.durs.Count ? c.durs[i] : -1).Append(',').Append(i < c.mods.Count ? c.mods[i] : 0)
              .Append(',').Append(c.BendAt(i)).Append(',').Append(i < c.nudges.Count && c.nudges.Count == c.nodes.Count ? c.nudges[i] : 0).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>The grid before <paramref name="kb"/> in its lane: the nearest chord grid to its left (earlier) whose front-back span overlaps
    /// its own by at least half of the shallower one, no farther than most of a measure away (a neighbour, not a grid across a gap).</summary>
    public static KeyBlock Before(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null || kb.kind != 0) return null;
        KeyBlock best = null; float bestEast = float.MinValue, bestOv = 0f;
        float w = kb.WestEdge, f = kb.FrontEdge, b = kb.BackEdge;
        foreach (var o in sm.Islands)
        {
            if (o == null || o == kb || o.kind != 0) continue;
            float e = o.EastEdge;
            if (e > w + 0.05f || w - e > KeyBlock.IslandWidth * 0.75f) continue;
            float ov = Mathf.Min(b, o.BackEdge) - Mathf.Max(f, o.FrontEdge);
            if (ov < 0.5f * Mathf.Min(kb.Depth, o.Depth)) continue;
            if (e > bestEast + 0.01f || (Mathf.Abs(e - bestEast) <= 0.01f && ov > bestOv)) { best = o; bestEast = e; bestOv = ov; }
        }
        return best;
    }

    // ------------------------------------------------------------------ the scan (only when grids or cubes changed)
    void Scan()
    {
        Scans++; dirty = false;
        var sm = SongManager.I;
        list.Clear();
        if (sm == null || !sm.HasSong) { states.Clear(); return; }
        // who lives where; which grids a cube plays on (its windows: repeats, long grids, a span walking on)
        sigs.Clear(); touched.Clear();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null) continue;
            var home = c.Island;
            if (home != null) touched.Add(home);
            if (c.Moon != null) touched.Add(c.Moon);
            for (int i = 0; i < c.windows.Count; i++) if (c.windows[i].island != null) touched.Add(c.windows[i].island);
            if (!Lives(c, home)) continue;
            List<string> l;
            if (!sigs.TryGetValue(home, out l)) { l = new List<string>(); sigs[home] = l; }
            l.Add(CubeSig(c));
        }
        // keep the states of the grids that are still there
        var old = new Dictionary<KeyBlock, State>(states);
        states.Clear();
        foreach (var kb in sm.Islands)
        {
            if (kb == null) continue;
            State s;
            if (!old.TryGetValue(kb, out s)) s = new State { kb = kb };
            var t0 = kb.tiles.Count > 0 ? kb.tiles[0] : null;
            if (s.tile0 != t0 || s.tileCount != kb.tiles.Count) { s.tile0 = t0; s.tileCount = kb.tiles.Count; s.shown = 0f; s.target = -1f; s.mark = null; }   // (re)built: a fresh look
            List<string> l;
            if (sigs.TryGetValue(kb, out l)) { l.Sort(string.CompareOrdinal); s.sig = string.Join("|", l.ToArray()); } else s.sig = "";
            s.push = true;
            states[kb] = s; list.Add(s);
        }
        foreach (var s in list)
        {
            var kb = s.kb;
            s.rest = Rest.None; s.before = null; s.stand.Clear();
            if (kb.kind != 0 || kb.InLongGrid) continue;
            if (!touched.Contains(kb)) s.rest = Rest.Empty;
            else if (s.sig.Length > 0)
            {
                var w = Before(kb);
                State ws;
                if (w != null && !w.InLongGrid && states.TryGetValue(w, out ws) && ws.sig == s.sig) { s.rest = Rest.Repeat; s.before = w; }
            }
            if (s.rest == Rest.None) continue;
            float lo = float.MaxValue;
            foreach (var t in kb.tiles) if (t != null) lo = Mathf.Min(lo, t.RestY);
            s.flushY = lo == float.MaxValue ? 0f : lo;
        }
        // the tiles a resting grid's cubes stand on keep their height (nothing floats)
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null) continue;
            for (int i = 0; i < c.nodes.Count; i++)
            {
                var t = c.nodes[i];
                State s;
                if (t != null && t.island != null && states.TryGetValue(t.island, out s) && s.rest == Rest.Repeat) s.stand.Add(t);
            }
        }
    }

    static bool WorldAwake()
    {
        if (!Enabled || Presenter.Active) return true;
        var pm = PathManager.I;
        return pm == null || pm.Hand != PathManager.HandKind.Empty || pm.IsDrawing;   // a cube / a pattern in the hand: every grid stands up
    }

    static bool IsAwake(State s, bool world, KeyBlock hover, KeyBlock ribbon, KeyBlock selected, bool anySel)
    {
        var kb = s.kb;
        if (world || s.rest == Rest.None) return true;
        if (kb.sleep || kb.SleepShown) return true;                 // the sleep look owns a muted grid
        if (kb == hover || kb == ribbon || kb == selected) return true;
        if (kb.IsLit || kb.Lifted || kb.Rising || kb.Sliding) return true;
        if (kb.UnderSea || kb.Sinking || kb.Popping || kb.PoseDepth > 0.01f) return true;
        if (anySel && GridSelection.Selected.Contains(kb)) return true;
        return false;
    }

    void LateUpdate() { Step(Time.unscaledDeltaTime); }

    void Step(float dt)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) { if (list.Count > 0) { list.Clear(); states.Clear(); } worldKey = int.MinValue; return; }
        // the grids themselves changed (added, removed, rebuilt: new instances)
        int key = sm.Islands.Count * 7919 + SequenceMaster.Cubes.Count;
        unchecked
        {
            for (int i = 0; i < sm.Islands.Count; i++)
            {
                var kb = sm.Islands[i]; if (kb == null) continue;
                key = key * 31 + kb.GetInstanceID() + (kb.InLongGrid ? 1 : 0);
                if (kb.kind == 0 && kb.tiles.Count > 0 && kb.tiles[0] != null) key = key * 31 + kb.tiles[0].GetInstanceID();   // (rebuilt in place: new tiles)
            }
        }
        if (key != worldKey) { worldKey = key; dirty = true; }
        // (no scan while every grid stands up anyway — a cube in the hand, a path being drawn: placing notes costs nothing here)
        bool world = WorldAwake();
        if (dirty && !world && Time.unscaledTime >= scanAt) { scanAt = Time.unscaledTime + ScanEvery; Scan(); }

        var pmi = PathManager.I; var ui = UIManager.I;
        // under the pointer: the island's edge, one of its tiles, or a cube standing on it
        KeyBlock hover = pmi != null ? pmi.hoverIsland : null, ribbon = IslandHeader.Current, selected = null;
        if (hover == null && pmi != null && pmi.HoverTile != null) hover = pmi.HoverTile.island;
        if (hover == null && pmi != null && pmi.Candidate != null) hover = pmi.Candidate.Island;
        if (ui != null && ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count) selected = sm.Islands[ui.SelectedMeasure];
        bool anySel = GridSelection.Any;
        float step = dt <= 0f ? 1f : dt / EaseS;
        for (int n = 0; n < list.Count; n++)
        {
            var s = list[n];
            if (s.kb == null) continue;
            bool awake = IsAwake(s, world, hover, ribbon, selected, anySel);
            if (awake && s.rest != Rest.None && !world) s.holdUntil = Time.unscaledTime + HoldS;   // it lingers awake a moment (the pointer crossing a gap)
            float target = awake || Time.unscaledTime < s.holdUntil ? 0f : 1f;
            if (target != s.target || s.push)
            {
                s.target = target; s.push = false;
                bool flat = s.rest != Rest.None;
                foreach (var t in s.kb.tiles)
                {
                    if (t == null) continue;
                    float drop = flat && !s.stand.Contains(t) ? Mathf.Max(0f, t.RestY - s.flushY) : 0f;
                    t.SetQuiet(target, drop);
                }
                if (target > 0f && s.rest == Rest.Repeat) EnsureMark(s);
            }
            if (s.shown != s.target || dt <= 0f)
            {
                s.shown = dt <= 0f ? s.target : Mathf.MoveTowards(s.shown, s.target, step);
                s.kb.SetQuietLook(s.shown);
                ShowMark(s);
            }
        }
    }

    // ------------------------------------------------------------------ the repeat mark (𝄎) on a repeated grid's front edge
    void EnsureMark(State s)
    {
        if (s.mark != null) return;
        var kb = s.kb;
        var found = kb.transform.Find("RepeatMark");
        GameObject go = found != null ? found.gameObject : null;
        if (go == null)
        {
            go = new GameObject("RepeatMark");
            go.transform.SetParent(kb.transform, false);
            float pad = ProjectConfig.PlatformPad;
            // the platform's front-right corner margin (where a sleeping grid shows its closed eye: a grid never wears both)
            go.transform.localPosition = new Vector3(-KeyBlock.EdgeInset + kb.Width - pad * 0.5f - 0.08f, 0.016f, -KeyBlock.EdgeInset + pad * 0.5f + 0.08f);
            go.transform.localScale = new Vector3(MarkSize, 1f, MarkSize);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = MarkMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        s.mark = go.GetComponent<Renderer>();
        if (s.mpb == null) s.mpb = new MaterialPropertyBlock();
        go.SetActive(false);
    }

    void ShowMark(State s)
    {
        if (s.mark == null) return;
        bool on = s.rest == Rest.Repeat && s.shown > 0.02f;
        if (s.mark.gameObject.activeSelf != on) s.mark.gameObject.SetActive(on);
        if (!on) return;
        s.mpb.SetColor(ColorId, Palette.A(MarkInk, 0.86f * s.shown));
        s.mark.SetPropertyBlock(s.mpb);
    }

    /// <summary>The mark's ink (the world's ink colour).</summary>
    public static Color MarkInk => Look.InkColor;
    static Texture2D markTex; static Material markMat;
    static Material MarkMaterial { get { if (markMat == null) markMat = Fx.Alpha(MarkTexture, Palette.A(MarkInk, 0.86f)); return markMat; } }

    /// <summary>The measure-repeat sign 𝄎 ("%"-like): a thick slash rising to the right with a dot on each side, drawn once (white with alpha,
    /// tinted by its material).</summary>
    public static Texture2D MarkTexture
    {
        get
        {
            if (markTex != null) return markTex;
            const int n = 64;
            markTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "deacube_repeat_mark" };
            var px = new Color32[n * n];
            Vector2 a = new Vector2(-0.42f, -0.52f), b = new Vector2(0.42f, 0.52f), ab = b - a;
            Vector2 d0 = new Vector2(-0.46f, 0.34f), d1 = new Vector2(0.46f, -0.34f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float slash = (a + ab * t - p).magnitude - 0.15f;                         // the slash: a capsule
                    float dots = Mathf.Min((p - d0).magnitude, (p - d1).magnitude) - 0.17f;   // the two dots
                    float alpha = Mathf.Clamp01(-Mathf.Min(slash, dots) * n * 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            markTex.SetPixels32(px); markTex.Apply(false, false);
            return markTex;
        }
    }

    /// <summary>A platform colour at the quiet look <paramref name="amount"/>: toward its own grey, a little lighter.</summary>
    public static Color Rested(Color c, float amount)
    {
        if (amount <= 0f) return c;
        float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        Color pale = Color.Lerp(Color.Lerp(c, new Color(l, l, l * 1.03f, c.a), PlatformGrey), new Color(1f, 1f, 1f, c.a), PlatformLighten);
        return Color.Lerp(c, pale, amount);
    }
}

/// <summary>Flow E2: the island's part of the quiet look — its platform a step paler (the tiles: TileInteraction.SetQuiet; which grids rest and
/// when they wake: QuietGrids).</summary>
public partial class KeyBlock
{
    float quietLook;
    /// <summary>Flow E2: the quiet look the platform shows (0 normal … 1 at rest).</summary>
    public float QuietLook => quietLook;
    /// <summary>The platform's colour as built (before the sleep / quiet looks).</summary>
    public Color PlatformBase => platBase;
    /// <summary>Flow E2: the platform toward its own grey by <paramref name="a"/> (0 = as built). A sleeping grid keeps its sleep look (it owns
    /// the platform's colour); Moons never rest.</summary>
    public void SetQuietLook(float a)
    {
        quietLook = Mathf.Clamp01(a);
        if (platMat == null || IsMoon || sleepShown) return;
        platMat.SetColor("_BaseColor", QuietGrids.Rested(platBase, quietLook));
    }
}
