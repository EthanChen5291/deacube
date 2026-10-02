using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 (SPEC v7 §2.9 / §5.1; package H): one-click ideas for a cube — an OCTAVE COPY above / below that floats in its octave layer ("duplicate
/// that cube down an octave or up an octave"; an ECHO when it lands late: a leading rest on its first tile, its last note shortened by the same
/// amount so the loop stays aligned), a SUPPORT HARMONY a 4th / 5th away that dodges the other notes ("considering the notes that the person has
/// down ... (5ths or 4ths basically)": Harmony.Harmonize over what the other cubes of the grid and its chord sound under each note; each harmony
/// note stands on the tile of that exact pitch when the grid has one, else on the nearest tile with a BEND = the semitones it is off; keyboards:
/// the key of that pitch; phrases: the cell of that pitch in the same time column), and FLIP (the path upside down: a melodic mirror, "an
/// instant answer"). Each one (but the preview) is ONE History entry. The copies and the harmony cube glide out of their source (a short
/// real-time birth: AudioCube.Appear + the materialise pop) and play at once when the song is playing. Refusals are soft (a thud and a small
/// squash of the cube; <see cref="LastDeny"/> says why). <see cref="PreviewHarmony"/> shows the harmony as ghost beads on its tiles and
/// auditions both voices together on the preview bus, without changing the song.
/// </summary>
public static class CubeOps
{
    /// <summary>The "late" echo's delay: an eighth (the recording answers C5 with C4 an eighth later).</summary>
    public const int EchoLateTicks = 12;
    /// <summary>The support voice's range (sounding pitches: the tile pitch + the cube's octave and layer).</summary>
    public const int HarmonyLo = 36, HarmonyHi = 96;
    /// <summary>Seconds a new copy / harmony cube takes to glide out of its source into place.</summary>
    public const float BirthSeconds = 0.45f;

    /// <summary>Counters (tests / tips): octave copies, harmony cubes, flips and refusals made so far.</summary>
    public static int CopyCount, HarmonyCount, FlipCount, DenyCount, PreviewCount;
    /// <summary>Why the last op was refused ("" none).</summary>
    public static string LastDeny = "";
    /// <summary>The last cube an octave copy / harmony made.</summary>
    public static AudioCube LastMade;
    /// <summary>Fires after an octave copy / harmony cube was placed (after its History entry).</summary>
    public static event Action<AudioCube> OnMade;
    /// <summary>Fires after a flip (after its History entry).</summary>
    public static event Action<AudioCube> OnFlipped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        CopyCount = 0; HarmonyCount = 0; FlipCount = 0; DenyCount = 0; PreviewCount = 0; LastDeny = ""; LastMade = null;
        previewRoot = null; previewCube = null; previewSig = 0; previewMats.Clear();
    }

    // ================================================================== octave copies / echoes
    /// <summary>A copy of <paramref name="c"/> one octave layer up (<paramref name="dir"/> &gt; 0) / down (&lt; 0): same grid, path, lengths, stickers,
    /// sound and settings, layer = c.layer ± 1, echoOf = c.id, a new id / seed. <paramref name="delayTicks"/> &gt; 0 = an echo that lands late (a
    /// leading rest of that many ticks on the first tile, the tail shortened by it). One History entry. Returns the new cube (null when refused:
    /// drums, the layer limit, the same copy already there).</summary>
    public static AudioCube OctaveCopy(AudioCube c, int dir, int delayTicks = 0)
    {
        string why = OctaveCopyRefusal(c, dir, delayTicks);
        if (why != null) { Deny(c, why); return null; }
        var s = OctaveState(c, dir, delayTicks);
        if (s == null) { Deny(c, "nothing left to echo"); return null; }
        if (HasTwin(c, s)) { Deny(c, "that copy is already there"); return null; }
        var nc = Place(s, c, dir > 0 ? 1.3f : 0.85f);
        if (nc != null) CopyCount++;
        return nc;
    }

    /// <summary>Why <see cref="OctaveCopy"/> would refuse (null = it can go).</summary>
    public static string OctaveCopyRefusal(AudioCube c, int dir, int delayTicks = 0)
    {
        if (c == null || c.nodes.Count == 0 || c.nodes[0] == null) return "no cube";
        if (c.IsDrums || c.IsOnMoon) return "drums have no octave";
        if (dir == 0) return "no direction";
        int l = c.layer + (dir > 0 ? 1 : -1);
        if (l > ProjectConfig.MaxLayer || l < -ProjectConfig.MaxLayer) return "no layer further " + (dir > 0 ? "up" : "down");
        if (delayTicks > 0 && c.IsRunner) return "a runner cannot echo late";
        if (SongManager.I == null || PathManager.I == null) return "no song";
        return null;
    }
    public static bool CanOctaveCopy(AudioCube c, int dir) => OctaveCopyRefusal(c, dir) == null;

    /// <summary>The state an octave copy of <paramref name="c"/> gets (not placed; null when a delay leaves nothing to play).</summary>
    public static CubeState OctaveState(AudioCube c, int dir, int delayTicks = 0)
    {
        if (c == null || c.nodes.Count == 0) return null;
        var s = c.ToState();
        s.id = 0; s.seed = 0; s.twinOf = -1;
        s.layer = Mathf.Clamp(c.layer + (dir > 0 ? 1 : -1), -ProjectConfig.MaxLayer, ProjectConfig.MaxLayer);
        s.echoOf = c.id;
        if (delayTicks > 0)
        {
            var node = new List<int>(); var ticks = new List<int>(); var mods = new List<int>(); int total;
            PlayOrder(c, node, ticks, mods, out total);
            var xs = new int[c.nodes.Count]; var zs = new int[c.nodes.Count]; var bend = new int[c.nodes.Count]; var nudge = new int[c.nodes.Count];
            for (int i = 0; i < xs.Length; i++) { xs[i] = c.nodes[i] != null ? c.nodes[i].gridX : 0; zs[i] = c.nodes[i] != null ? c.nodes[i].gridZ : 0; bend[i] = c.BendAt(i); nudge[i] = c.NudgeAt(i); }
            if (!ApplyDelay(s, node, ticks, mods, xs, zs, bend, nudge, delayTicks)) return null;
        }
        return s;
    }

    /// <summary>The notes of one period of <paramref name="c"/> in play order: the node each one stands on, its length (ticks) and sticker. A
    /// durations cube looping forward from its first node: its path as it is; anything else (ping-pong, once, reversed, a phase, the legacy
    /// step engine): the notes its first window plays (AudioCube.TimelineEvents), a leading gap as a rest.</summary>
    public static void PlayOrder(AudioCube c, List<int> node, List<int> ticks, List<int> mods, out int totalTicks)
    {
        node.Clear(); ticks.Clear(); mods.Clear(); totalTicks = 0;
        if (c == null || c.nodes.Count == 0) return;
        int n = c.nodes.Count;
        if (c.HasDurations && c.mode == PathMode.Loop && !c.reverse && c.phase == 0)
        {
            for (int i = 0; i < n; i++) { node.Add(i); ticks.Add(c.durs[i]); mods.Add(c.ModOf(i)); totalTicks += c.durs[i]; }
            return;
        }
        var ev = new List<AudioCube.TimelineEvent>();
        c.TimelineEvents(0, ev);
        float len = c.windows.Count > 0 ? c.windows[0].length : c.measureLength;
        if (ev.Count == 0) { node.Add(Mathf.Max(0, c.HomeNode)); ticks.Add(Ticks(len)); mods.Add(1); totalTicks = ticks[0]; return; }
        if (ev[0].start > 1e-3f) { node.Add(ev[0].node); ticks.Add(Ticks(ev[0].start)); mods.Add(1); }
        foreach (var e in ev) { node.Add(Mathf.Clamp(e.node, 0, n - 1)); ticks.Add(Ticks(e.len)); mods.Add(e.mod); }
        foreach (int t in ticks) totalTicks += t;
    }

    static int Ticks(float beats) => Mathf.Clamp(Mathf.RoundToInt(beats * ProjectConfig.TicksPerBeat), 3, 192);

    /// <summary>Rewrites <paramref name="s"/>'s path as the play-order notes (<paramref name="node"/>[i] → tile (<paramref name="xsOfNode"/>,
    /// <paramref name="zsOfNode"/>) of that node) with a leading rest of <paramref name="delay"/> ticks on the first tile and the tail shortened by
    /// the same amount (last notes dropped when they are shorter): one period stays one period, so the echo keeps its place on every pass. The
    /// durations engine, forward loop (a once-mode cube stays once). False when nothing but the rest would be left.</summary>
    public static bool ApplyDelay(CubeState s, IList<int> node, IList<int> ticks, IList<int> mods, IList<int> xsOfNode, IList<int> zsOfNode, IList<int> bendOfNode, int delay)
        => ApplyDelay(s, node, ticks, mods, xsOfNode, zsOfNode, bendOfNode, null, delay);

    /// <summary>v9 (N): <see cref="ApplyDelay(CubeState, IList{int}, IList{int}, IList{int}, IList{int}, IList{int}, IList{int}, int)"/> with the nudge of each
    /// node (<paramref name="nudgeOfNode"/>, null = none): it travels with its note; the leading rest keeps none.</summary>
    public static bool ApplyDelay(CubeState s, IList<int> node, IList<int> ticks, IList<int> mods, IList<int> xsOfNode, IList<int> zsOfNode, IList<int> bendOfNode, IList<int> nudgeOfNode, int delay)
    {
        if (s == null || node == null || node.Count == 0 || delay <= 0) return false;
        var N = new List<int>(node); var T = new List<int>(ticks); var M = new List<int>(mods);
        N.Insert(0, N[0]); T.Insert(0, Mathf.Clamp(delay, 3, 192)); M.Insert(0, 1);
        int need = delay;
        while (need > 0 && N.Count > 1)
        {
            int last = N.Count - 1;
            if (T[last] - need >= 3) { T[last] -= need; need = 0; }
            else { need -= T[last]; N.RemoveAt(last); T.RemoveAt(last); M.RemoveAt(last); }
        }
        if (N.Count < 2) return false;
        int n = N.Count;
        s.xs = new int[n]; s.zs = new int[n]; s.mods = new int[n]; s.rests = new bool[n]; s.durs = new int[n];
        var b = new int[n]; bool bent = false;
        var g = new int[n]; bool nudged = false;
        for (int i = 0; i < n; i++)
        {
            int k = N[i];
            s.xs[i] = k >= 0 && k < xsOfNode.Count ? xsOfNode[k] : 0;
            s.zs[i] = k >= 0 && k < zsOfNode.Count ? zsOfNode[k] : 0;
            s.mods[i] = Mathf.Clamp(M[i], 0, 8); s.rests[i] = s.mods[i] == 1;
            s.durs[i] = AudioCube.ClampTicks(T[i]);
            b[i] = i == 0 || bendOfNode == null || k < 0 || k >= bendOfNode.Count ? 0 : bendOfNode[k];   // the leading rest keeps no bend
            if (b[i] != 0) bent = true;
            g[i] = i == 0 || nudgeOfNode == null || k < 0 || k >= nudgeOfNode.Count ? 0 : Nudge.Clamp(nudgeOfNode[k]);   // v9: nor a nudge
            if (g[i] != 0) nudged = true;
        }
        s.bend = bent ? b : null;
        s.nudges = nudged ? g : null;
        if (s.mode != (int)PathMode.Once) s.mode = (int)PathMode.Loop;
        s.reverse = false; s.phase = 0; s.hits = -1; s.rot = 0; s.mask = 0;
        return true;
    }

    /// <summary>True when a cube with the same path, layer and source is already there (a second identical copy only doubles the sound).</summary>
    static bool HasTwin(AudioCube c, CubeState s)
    {
        foreach (var o in SequenceMaster.Cubes)
        {
            if (o == null || o == c || !o.isFinalized || o.echoOf != c.id || o.layer != s.layer || o.nodes.Count != s.xs.Length) continue;
            bool same = true;
            for (int i = 0; i < s.xs.Length && same; i++)
            {
                var t = o.nodes[i];
                same = t != null && t.gridX == s.xs[i] && t.gridZ == s.zs[i] && o.ModOf(i) == (s.mods != null && i < s.mods.Length ? s.mods[i] : 0)
                       && (s.durs == null ? !o.HasDurations : o.HasDurations && o.durs[i] == s.durs[i]) && o.BendAt(i) == (s.bend != null && i < s.bend.Length ? s.bend[i] : 0)
                       && o.NudgeAt(i) == (s.nudges != null && i < s.nudges.Length ? s.nudges[i] : 0);   // v9 (N)
            }
            if (same) return true;
        }
        return false;
    }

    // ================================================================== support harmony
    /// <summary>A support voice for <paramref name="c"/> above / below (Harmony.Harmonize: the perfect 5th / 4th first, the others' notes and the
    /// chord dodged): a new cube with the same rhythm, stickers, sound and layer on the harmony's tiles (+ bends where the grid has no such tile),
    /// echoOf = c.id. One History entry. Returns the new cube (null when refused).</summary>
    public static AudioCube Harmonize(AudioCube c, bool above)
    {
        string why = HarmonyRefusal(c);
        if (why != null) { Deny(c, why); return null; }
        var plan = PlanHarmony(c, above);
        if (plan == null) { Deny(c, "no harmony fits"); return null; }
        var s = c.ToState();
        s.id = 0; s.seed = 0; s.twinOf = -1; s.echoOf = c.id;
        int n = plan.tiles.Length;
        s.xs = new int[n]; s.zs = new int[n];
        bool bent = false;
        for (int i = 0; i < n; i++) { s.xs[i] = plan.tiles[i].gridX; s.zs[i] = plan.tiles[i].gridZ; if (plan.bend[i] != 0) bent = true; }
        s.bend = bent ? (int[])plan.bend.Clone() : null;
        s.nudges = null;   // v9 (N): the harmony was planned over the source's SOUNDING notes (nudges in) and lands on tiles + bends: it carries no nudge of its own
        if (HasTwin(c, s)) { Deny(c, "that harmony is already there"); return null; }
        EndPreview();
        var nc = Place(s, c, above ? 1.2f : 0.9f);
        if (nc != null) HarmonyCount++;
        return nc;
    }

    /// <summary>Why <see cref="Harmonize"/> / <see cref="PreviewHarmony"/> would refuse (null = it can go).</summary>
    public static string HarmonyRefusal(AudioCube c)
    {
        if (c == null || c.nodes.Count == 0 || c.nodes[0] == null) return "no cube";
        if (c.IsDrums || c.IsOnMoon) return "drums have no harmony";
        if (c.IsRunner) return "a runner keeps its stairs";
        var kb = c.Island;
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return "no grid";
        bool anyNote = false;
        for (int i = 0; i < c.nodes.Count; i++) if (c.ModOf(i) != 1) { anyNote = true; break; }
        if (!anyNote) return "only rests";
        if (SongManager.I == null || PathManager.I == null) return "no song";
        return null;
    }

    /// <summary>A harmony for a cube: per node the source's sounding pitch (VoiceRules.NodeMidi without the song's transpose; −1 for rests), the
    /// harmony's pitch in the same space, and the tile + bend the harmony cube plays it on.</summary>
    public class HarmonyPlan
    {
        public int[] src, pitch, bend;
        public TileInteraction[] tiles;
        public bool above;
    }

    /// <summary>The harmony <see cref="Harmonize"/> would place (null when refused): Harmony.Harmonize over the cube's sounding pitches (its octave,
    /// layer, bends and the instrument's fold: VoiceRules.NodeMidi, the song's transpose taken out so the chord and the key match), the others'
    /// sounding notes under each node and the grid's chord tones, in <see cref="HarmonyLo"/>..<see cref="HarmonyHi"/>; each harmony note goes the
    /// same interval from the source node's tile (+ its bend) onto the grid (<see cref="TileFor"/>: the exact tile, else the nearest + a bend).</summary>
    public static HarmonyPlan PlanHarmony(AudioCube c, bool above)
    {
        if (HarmonyRefusal(c) != null) return null;
        var kb = c.Island;
        int n = c.nodes.Count, tr = SongManager.Transpose;
        var src = new int[n];
        for (int i = 0; i < n; i++)
        {
            int m = c.ModOf(i) == 1 || c.nodes[i] == null ? -1 : VoiceRules.NodeMidi(c, i);
            src[i] = m < 0 ? -1 : m - tr;
        }
        var others = OthersUnder(c, kb, tr);
        var w = Harmony.WeightsOf(c.HasDurations ? c.durs : null, n);
        int root; IList<int> semis;
        if (!Harmony.ChordOf(kb, out root, out semis) || semis == null) { root = kb.chordRootMIDI; semis = kb.semitoneList; }
        var key = MusicTheory.KeyOfSong();
        var h = Harmony.Harmonize(src, w, root, semis, key.tonic, key.minor, others, above, HarmonyLo, HarmonyHi);
        var plan = new HarmonyPlan { src = src, pitch = h, bend = new int[n], tiles = new TileInteraction[n], above = above };
        for (int i = 0; i < n; i++)
        {
            if (h[i] < 0 || src[i] < 0) { plan.pitch[i] = -1; continue; }
            int want = c.nodes[i].midi + c.BendAt(i) + VoiceRules.NudgeOf(c, c.nodes[i], i) + (h[i] - src[i]);   // the same interval, in the grid's pitches (v9: from the nudged note)
            var t = TileFor(kb, want, c.nodes[i], true);
            if (t == null) return null;
            plan.tiles[i] = t; plan.bend[i] = Mathf.Clamp(want - t.midi, -24, 24);
        }
        // rests: the second voice waits where it is (on its previous tile; a leading rest on its first note's tile); on a phrase in the rest's own
        // time column (D's phrase encoding: a gap is a rest on the cell where it starts)
        TileInteraction prev = null;
        for (int i = 0; i < n; i++) if (plan.tiles[i] != null) { prev = plan.tiles[i]; break; }
        if (prev == null) return null;
        for (int i = 0; i < n; i++)
        {
            if (plan.tiles[i] != null) { prev = plan.tiles[i]; continue; }
            var t = prev;
            if (kb.IsPhrase && c.nodes[i] != null) { var pt = kb.GetTile(Mathf.Clamp(c.nodes[i].gridX, 0, kb.cols - 1), prev.gridZ); if (pt != null) t = pt; }
            plan.tiles[i] = t; plan.bend[i] = 0;
        }
        return plan;
    }

    /// <summary>The tile of <paramref name="kb"/> for pitch <paramref name="midi"/> near <paramref name="near"/>: the tile of that exact pitch
    /// when there is one (nearest to <paramref name="near"/> on the grid), else the nearest pitch (then nearest on the grid). A phrase keeps the
    /// time column of <paramref name="near"/>. <paramref name="avoidNear"/>: never <paramref name="near"/> itself while another tile will do (two
    /// cubes on one tile stack, and the top one sounds an octave up).</summary>
    public static TileInteraction TileFor(KeyBlock kb, int midi, TileInteraction near, bool avoidNear)
    {
        if (kb == null) return null;
        TileInteraction best = null; long bestCost = long.MaxValue;
        foreach (var t in kb.tiles)
        {
            if (t == null) continue;
            if (kb.IsPhrase && near != null && t.gridX != near.gridX) continue;   // a phrase: the same time cell
            if (avoidNear && t == near) continue;
            long d = Math.Abs(t.midi - midi);
            long g = near != null ? Math.Abs(t.gridX - near.gridX) + Math.Abs(t.gridZ - near.gridZ) : t.gridX + t.gridZ;
            long cost = d * 1000 + (t.midi > midi ? 1 : 0) * 100 + g;
            if (cost < bestCost) { bestCost = cost; best = t; }
        }
        if (best == null && avoidNear) return TileFor(kb, midi, near, false);
        return best;
    }

    /// <summary>What the other voices sound under each node of <paramref name="c"/> (its first window): the other pitched cubes' notes on the
    /// same grid (their own first window there — residents and carried patterns alike: VoiceRules.NodeMidi on the tile they play) and the grid's
    /// chord tones; the song's transpose <paramref name="tr"/> taken out.</summary>
    static List<IList<int>> OthersUnder(AudioCube c, KeyBlock kb, int tr)
    {
        int n = c.nodes.Count;
        var at = new float[n]; var len = new float[n];
        for (int i = 0; i < n; i++) { at[i] = -1f; len[i] = 0f; }
        var ev = new List<AudioCube.TimelineEvent>();
        c.TimelineEvents(0, ev);
        foreach (var e in ev) if (e.node >= 0 && e.node < n && at[e.node] < 0f) { at[e.node] = e.start; len[e.node] = e.len; }
        var starts = new List<float>(); var ends = new List<float>(); var pitches = new List<int>();
        var oe = new List<AudioCube.TimelineEvent>();
        foreach (var o in SequenceMaster.Cubes)
        {
            if (o == null || o == c || !o.isFinalized || o.IsDrums || o.nodes.Count == 0) continue;
            int w = -1;
            for (int k = 0; k < o.windows.Count; k++) if (o.windows[k].island == kb && !o.windows[k].silent) { w = k; break; }
            if (w < 0) { if (o.Island == kb && o.windows.Count == 0) w = 0; else continue; }
            o.TimelineEvents(w, oe);
            foreach (var e in oe)
            {
                if (e.rest) continue;
                var t = o.TileAt(w, e.node);
                if (t == null) continue;
                int m = VoiceRules.NodeMidi(o, e.node, t);
                if (m < 0) continue;
                starts.Add(e.start); ends.Add(e.start + e.len); pitches.Add(m - tr);
            }
        }
        int root; IList<int> semis;
        Harmony.ChordOf(kb, out root, out semis);
        var r = new List<IList<int>>(n);
        for (int i = 0; i < n; i++)
        {
            var l = new List<int>();
            if (semis != null) foreach (int s in semis) l.Add(root + s);
            if (at[i] >= 0f)
            {
                float a = at[i], b = at[i] + Mathf.Max(0.01f, len[i]);
                for (int j = 0; j < pitches.Count; j++) if (starts[j] < b - 1e-4f && ends[j] > a + 1e-4f) l.Add(pitches[j]);
            }
            r.Add(l);
        }
        return r;
    }

    // ---- the preview (the cube card's hover): ghost beads on the harmony's tiles + both voices on the preview bus
    static GameObject previewRoot; static AudioCube previewCube; static int previewSig;
    static readonly List<Material> previewMats = new List<Material>();
    /// <summary>True while a harmony preview is shown.</summary>
    public static bool Previewing => previewRoot != null;
    /// <summary>The cube being previewed (null none).</summary>
    public static AudioCube PreviewCube => previewRoot != null ? previewCube : null;

    /// <summary>Shows the harmony of <paramref name="c"/> above / below as ghost beads on its tiles and auditions the cube and the harmony
    /// together (once per call with a new cube / side; calling again every frame of a hover keeps it). No song change. Returns the harmony's
    /// sounding pitches per node (as VoiceRules plays them, the song's transpose included; −1 for rests), null when refused.</summary>
    public static int[] PreviewHarmony(AudioCube c, bool above)
    {
        var plan = PlanHarmony(c, above);
        if (plan == null) { EndPreview(); return null; }
        var sounding = new int[plan.pitch.Length];
        for (int i = 0; i < sounding.Length; i++) sounding[i] = plan.pitch[i] < 0 ? -1 : plan.pitch[i] + SongManager.Transpose;
        int sig = c.GetInstanceID() * 2 + (above ? 1 : 0);
        if (previewRoot != null && previewCube == c && previewSig == sig) return sounding;
        EndPreview();
        previewCube = c; previewSig = sig;
        BuildGhosts(c, plan);
        AuditionBoth(c, plan);
        PreviewCount++;
        return sounding;
    }

    /// <summary>Ends a harmony preview (ghosts gone, the audition cut).</summary>
    public static void EndPreview()
    {
        if (previewRoot != null)
        {
            UnityEngine.Object.Destroy(previewRoot);
            if (Synth.Ready) Synth.CancelOwner(Synth.PreviewOwner);
        }
        foreach (var m in previewMats) if (m != null) UnityEngine.Object.Destroy(m);
        previewMats.Clear();
        previewRoot = null; previewCube = null; previewSig = 0;
    }

    static void BuildGhosts(AudioCube c, HarmonyPlan plan)
    {
        previewRoot = new GameObject("HarmonyPreview");
        var col = Color.Lerp(c.Color, Color.white, 0.45f);
        var ring = Fx.Alpha(IconFactory.GetTexture("ring"), Palette.A(col, 0.95f));
        var dot = Fx.Alpha(IconFactory.GetTexture("ringThin"), Palette.A(col, 0.7f));
        var glow = Fx.Additive(IconFactory.GetTexture("glow"), c.Color, 0.9f);
        previewMats.Add(ring); previewMats.Add(dot); previewMats.Add(glow);
        Vector3 lift = Vector3.up * 0.06f;
        var pts = new List<Vector3>();
        for (int i = 0; i < plan.tiles.Length; i++)
        {
            var t = plan.tiles[i];
            if (t == null) continue;
            bool rest = plan.pitch[i] < 0;
            // a bent note sits a touch off its tile toward its bend (up = back, down = front): "not quite this tile"
            Vector3 p = c.TopOf(t) + lift + new Vector3(0f, 0f, Mathf.Clamp(plan.bend[i], -3, 3) * 0.06f);   // the harmony shares the cube's layer
            float s = (i == 0 ? 0.56f : 0.4f) * (rest ? 0.7f : 1f) * AudioCube.FootOf(t);
            Quad(rest ? "GhostRest" : "GhostBead", rest ? dot : ring, p, s);
            if (!rest) Quad("GhostGlow", glow, p - Vector3.up * 0.01f, s * 1.9f);
            pts.Add(p + Vector3.up * 0.02f);
        }
        // the second voice itself, as a hologram where it would start: "a cube with a smaller partner"
        var first = plan.tiles[0];
        if (first != null)
        {
            var sh = Shader.Find("DeaCube/Hologram");
            var hm = sh != null ? new Material(sh) : Fx.Additive(IconFactory.GetTexture("white"), c.Color, 0.6f);
            hm.SetColor("_Color", c.Color);
            if (hm.HasProperty("_Alpha")) hm.SetFloat("_Alpha", 0.55f);
            if (hm.HasProperty("_Glitch")) hm.SetFloat("_Glitch", 0.15f);
            previewMats.Add(hm);
            var go = new GameObject("GhostCube");
            go.transform.SetParent(previewRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(Vector3.one, 0.14f, 4);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = hm;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            float size = ProjectConfig.CubeSize * AudioCube.SizeOf(c.DurBeats(0)) * AudioCube.FootOf(first) * 0.9f;
            go.transform.position = c.TopOf(first) + Vector3.up * (size * 0.5f + 0.02f);
            go.transform.localScale = Vector3.one * size;
        }
        if (pts.Count >= 2)
        {
            var lr = previewRoot.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.widthMultiplier = 0.07f; lr.positionCount = pts.Count;
            lr.textureMode = LineTextureMode.Tile; lr.alignment = LineAlignment.View;
            var flow = Fx.Flow(col, 1.1f, 2.2f, 2.4f, 0.12f);
            previewMats.Add(flow);
            lr.material = flow;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, pts[i]);
        }
    }

    static Transform Quad(string name, Material m, Vector3 p, float s)
    {
        var go = new GameObject(name);
        go.transform.SetParent(previewRoot.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        go.transform.position = p;
        go.transform.localScale = new Vector3(s, 1f, s);
        return go.transform;
    }

    /// <summary>The cube's first window of notes (up to ~3 s) with the harmony under every sounding note, on the preview bus — each voice as it
    /// will sound (VoiceRules.AuditionEvent: the voice's slot, octave, layer, bend and fold).</summary>
    static void AuditionBoth(AudioCube c, HarmonyPlan plan)
    {
        if (!Synth.Ready) return;
        var ev = new List<AudioCube.TimelineEvent>();
        c.TimelineEvents(0, ev);
        int slot = Instruments.SlotOf(c.instrument, c.voice);
        double bps = Math.Max(0.5, GlobalClock.BeatsPerSecond);
        double t0;
        if (GlobalClock.IsPlaying) { double end; VoiceRules.PreviewBeat(out t0, out end); }
        else t0 = GlobalClock.DspNow + 0.05;
        foreach (var e in ev)
        {
            if (e.start / bps > 3.0) break;
            if (e.rest || e.node < 0 || e.node >= plan.src.Length || plan.src[e.node] < 0) continue;
            double on = t0 + e.start / bps, secs = Math.Max(0.08, e.len / bps * 0.92);
            VoiceRules.Audition(VoiceRules.AuditionEvent(c, e.node, secs, 0.72f, on));
            var ht = plan.tiles[e.node];
            if (plan.pitch[e.node] < 0 || ht == null) continue;
            VoiceRules.Audition(VoiceRules.AuditionEvent(slot, ht, c.octave, secs, 0.62f, on, c.layer, plan.bend[e.node] + (c.ModOf(e.node) == 8 ? 12 : 0)));
        }
    }

    // ================================================================== flip
    /// <summary>Turns <paramref name="c"/>'s path upside down (a melodic mirror): a chord grid x → cols − 1 − x, z → rows − 1 − z; a keyboard's key
    /// → (lowest + highest key of the path) − key; a phrase keeps its time and mirrors its rows. Stickers and lengths stay on their notes, a bend
    /// turns the other way. In place, one History entry. Returns c.</summary>
    public static AudioCube Flip(AudioCube c)
    {
        string why = FlipRefusal(c);
        if (why != null) { Deny(c, why); return c; }
        var tiles = FlippedTiles(c);
        if (tiles == null) { Deny(c, "no grid"); return c; }
        Magic.Flip(c);   // W: the mirror plane sweeps, the new tiles flash (before the path changes)
        int n = c.nodes.Count;
        var mods = new List<int>(n); var bends = new List<int>(n); var nudges = new List<int>(n);
        for (int i = 0; i < n; i++) { mods.Add(c.ModOf(i)); bends.Add(-c.BendAt(i)); nudges.Add(-c.NudgeAt(i)); }
        c.SetPathAndMods(tiles, mods, c.HasDurations ? new List<int>(c.durs) : null, bends, nudges);   // v9: a nudge mirrors too
        History.Push();
        FlipCount++;
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.25f, 1.45f);
        OnFlipped?.Invoke(c);
        return c;
    }

    /// <summary>Why <see cref="Flip"/> would refuse (null = it can go).</summary>
    public static string FlipRefusal(AudioCube c)
    {
        if (c == null || c.nodes.Count == 0 || c.nodes[0] == null) return "no cube";
        if (c.IsRunner) return "a runner keeps its stairs";
        var kb = c.Island;
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return "no grid";
        return null;
    }

    /// <summary>The tiles <see cref="Flip"/> puts <paramref name="c"/> on (node for node; null when it cannot).</summary>
    public static List<TileInteraction> FlippedTiles(AudioCube c)
    {
        var kb = c != null ? c.Island : null;
        if (kb == null) return null;
        int n = c.nodes.Count;
        var r = new List<TileInteraction>(n);
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (var t in c.nodes) if (t != null) { lo = Mathf.Min(lo, t.gridX); hi = Mathf.Max(hi, t.gridX); }
        foreach (var t in c.nodes)
        {
            if (t == null) return null;
            int x, z;
            if (kb.IsKeyboard) { x = lo + hi - t.gridX; z = t.gridZ; }
            else if (kb.IsPhrase) { x = t.gridX; z = kb.rows - 1 - t.gridZ; }
            else { x = kb.cols - 1 - t.gridX; z = kb.rows - 1 - t.gridZ; }
            var nt = kb.GetTile(Mathf.Clamp(x, 0, kb.cols - 1), Mathf.Clamp(z, 0, kb.rows - 1));
            if (nt == null) return null;
            r.Add(nt);
        }
        return r;
    }

    // ================================================================== shared
    /// <summary>Places a new cube from <paramref name="s"/> (one History entry): it glides out of <paramref name="src"/> into place with the
    /// materialise pop; a playing song plays it at once (its windows come with the timeline recompute).</summary>
    static AudioCube Place(CubeState s, AudioCube src, float pitch)
    {
        var pm = PathManager.I;
        if (pm == null || s == null) return null;
        if (pm.IsDrawing) pm.FinishPath();
        var nc = pm.RestoreCube(s);
        if (nc == null) { Deny(src, "it does not fit"); return null; }
        SequenceMaster.RecalculateTimeline();
        History.Push();
        if (src != null) nc.Appear(src.transform.position, BirthSeconds);
        nc.Materialize();
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, pitch);
        LastMade = nc;
        OnMade?.Invoke(nc);
        return nc;
    }

    /// <summary>A soft refusal: a thud and a small squash of the cube.</summary>
    static void Deny(AudioCube c, string why)
    {
        DenyCount++;
        LastDeny = why ?? "";
        AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f);
        if (c != null) c.Squash(0.5f);
    }
}
