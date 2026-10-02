using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v6 package W (SPEC v6 §6.2): the carry bridge of one island with carry ≥ 1 — "certain grids should also be able to be transposed to the next
/// chord progression … when they do this is should look like magic, and on the UI there should be something (similar to the conveyor belt's
/// vibe) that makes it look unique when played and recognizable".
/// At rest: a SIGIL rings the source platform (a dark ink band following its rounded outline, bright border lines, pale runes drifting round it,
/// a flat glow in the chord colour); from it a dotted ARC of light (Magic.ArcPoint between the platform centres, a little above them) leaps to
/// each target (SongManager.CarryTargets, chained source → 1 → 2 → 3, one colour: one spell); every target wears a smaller matching sigil with
/// pips for its place in the chain, and tiny GHOST STARS float over the tiles the carried pattern plays there (AudioCube.TilesOn of the
/// source's resident cubes: H adapts them). The arc's pearls ride it like a conveyor of light: they drift toward the target at rest and, while
/// the song plays, hop one pearl forward on every beat (the belt's jerk).
/// Playing: in the half beat before a target's column starts a bright head runs along its arc with a comet trail (the flying cube's escort; H
/// flies the cube), the target's sigil flares as the column starts, and its ghost stars ignite in the pattern's colour as the notes land
/// (AudioCube.OnLanded on the carried window), then fade back.
/// The gesture (SongManager.OnCarryChanged): raising the carry casts a SPELL — the source sigil draws itself rune by rune (the first time), the
/// new arc shoots out like a thrown streamer (whipping, a bright tip), ghost copies of the pattern's beads fly along to the target tiles and
/// land in sparkles (the ghost stars appear where they land), the target flashes its chord colour; lowering it: the last arc reels back and
/// fizzles into sparks, its sigil un-draws. Built, re-bound and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class CarryBridge : MonoBehaviour
{
    /// <summary>Beats of the flight before a target column starts (the pulse runs in them; H's cube flight has the same span).</summary>
    public const float FlightBeats = 0.5f;
    /// <summary>Seconds: the sigil drawing itself, the streamer's throw, a retract.</summary>
    public const float SigilDrawSeconds = 0.5f, CastSeconds = 0.6f, RetractSeconds = 0.42f, QuickSeconds = 0.25f;
    /// <summary>How high above the platform the arcs leave and land.</summary>
    public const float ArcLift = 1.3f;
    const int ArcPoints = 40;

    public KeyBlock Source { get; private set; }
    public int SourceIndex { get; private set; }
    public bool Retiring { get; private set; }
    public Color SpellColor { get; private set; } = Color.white;
    /// <summary>Counters (tests): spells cast / retracts by gesture, pulses run, sigil flares, ghost-star ignitions, spell beads launched.</summary>
    public int Casts, Retracts, Pulses, Flares, Ignitions, SpellBeads, Fizzles;

    // ------------------------------------------------------------------ parts
    class Star
    {
        public TileInteraction tile; public Vector3 local; public Transform t; public MeshRenderer r;
        public float phase, reveal, revealTo = 1f, ignite; public Color pale, hot; public bool held, dead;
    }

    class Hop
    {
        public KeyBlock target; public int index;
        public LineRenderer dots, glow; public MaterialPropertyBlock mpb;
        public Sigil sigil;
        public int mode;             // 0 live, 1 casting, 2 retracting
        public bool full;            // the gesture's full spell (beads, flash, fizzles) instead of the quick draw
        public float t, delay, draw = 1f, drawFrom;
        public int bornFrame = -1, retireFrame = -1;
        public bool reached, done;
        public Vector3 a, b; public float h; public bool haveEnds;
        public readonly List<Star> stars = new List<Star>();
        public bool starsDirty = true; public float starsClock;
        public Transform head; public MeshRenderer headRend; public TrailRenderer headTrail;
        public bool pulsing; public float shed;
        public Vector3 pulsePos;
    }

    Sigil sigil;
    readonly List<Hop> hops = new List<Hop>();
    readonly List<Hop> dying = new List<Hop>();
    Material dotsMat, glowMat, starMat, headMat;
    readonly Vector3[] pts = new Vector3[ArcPoints];
    double prevBeat = -1.0;
    float twos, retireClock; bool sigilUndraw; float undrawDelay;
    MaterialPropertyBlock starMpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    // ------------------------------------------------------------------ diagnostics
    /// <summary>Live hops (targets in chain order; retracting ones excluded).</summary>
    public int HopCount => hops.Count;
    public KeyBlock HopTarget(int k) => k >= 0 && k < hops.Count ? hops[k].target : null;
    /// <summary>How much of hop <paramref name="k"/>'s arc is drawn (0..1).</summary>
    public float HopDraw(int k) => k >= 0 && k < hops.Count ? hops[k].draw : 0f;
    public bool HopCasting(int k) => k >= 0 && k < hops.Count && hops[k].mode == 1;
    /// <summary>Arcs fully drawn and at rest.</summary>
    public int DrawnArcs { get { int n = 0; foreach (var h in hops) if (h.mode == 0 && h.draw >= 0.999f) n++; return n; } }
    /// <summary>Target sigils shown (revealed past half).</summary>
    public int TargetSigils { get { int n = 0; foreach (var h in hops) if (h.sigil != null && h.sigil.Reveal > 0.5f) n++; return n; } }
    public int RetractingArcs => dying.Count;
    public float SourceReveal => sigil != null ? sigil.Reveal : 0f;
    public int SourceRunes => sigil != null ? sigil.RuneCount : 0;
    /// <summary>Ghost stars shown on hop <paramref name="k"/>'s target (all hops when k &lt; 0).</summary>
    public int GhostStars(int k = -1)
    {
        int n = 0;
        for (int i = 0; i < hops.Count; i++) if (k < 0 || k == i) foreach (var s in hops[i].stars) if (!s.dead && s.reveal > 0.5f) n++;
        return n;
    }
    /// <summary>The tiles hop <paramref name="k"/>'s ghost stars stand on.</summary>
    public List<TileInteraction> StarTiles(int k)
    {
        var l = new List<TileInteraction>();
        if (k >= 0 && k < hops.Count) foreach (var s in hops[k].stars) if (!s.dead) l.Add(s.tile);
        return l;
    }
    public bool PulseActive(int k) => k >= 0 && k < hops.Count && hops[k].pulsing;
    public Vector3 PulsePosition(int k) => k >= 0 && k < hops.Count ? hops[k].pulsePos : Vector3.zero;
    /// <summary>The arc of hop <paramref name="k"/>: its ends and height (world).</summary>
    public bool ArcOf(int k, out Vector3 a, out Vector3 b, out float h)
    {
        a = b = Vector3.zero; h = 0f;
        if (k < 0 || k >= hops.Count || !hops[k].haveEnds) return false;
        a = hops[k].a; b = hops[k].b; h = hops[k].h; return true;
    }
    /// <summary>Ignited stars glowing now (tests).</summary>
    public int HotStars { get { int n = 0; foreach (var h in hops) foreach (var s in h.stars) if (s.ignite > 0.3f) n++; return n; } }

    // ------------------------------------------------------------------ binding
    /// <summary>(Re-)binds the bridge to its source island (a rebuild re-created it).</summary>
    public void Bind(KeyBlock kb, int index)
    {
        Source = kb; SourceIndex = index;
        if (kb == null) return;
        SpellColor = kb.chordColor;
        if (dotsMat == null)
        {
            dotsMat = Fx.Alpha(MagicTextures.Pearls, Color.white);
            dotsMat.mainTextureScale = new Vector2(1f / 0.62f, 1f);
            glowMat = Fx.Additive(MagicTextures.Soft, Color.white, 0.55f);
            starMat = Fx.Alpha(MagicTextures.Star, Color.white);
            headMat = Fx.Alpha(MagicTextures.Star, Color.white);
            starMpb = new MaterialPropertyBlock();
        }
        dotsMat.SetColor(ColorId, Palette.A(Color.Lerp(SpellColor, Color.white, 0.5f), 1f));
        glowMat.SetColor(ColorId, SpellColor);
        headMat.SetColor(ColorId, Color.Lerp(SpellColor, Color.white, 0.55f));
        if (sigil == null) { sigil = new Sigil(transform, "Sigil", false); sigil.Build(kb, SpellColor, 0); sigil.StartReveal(0f, QuickSeconds); }
        else sigil.Build(kb, SpellColor, 0);
    }

    /// <summary>The targets in chain order (SongManager.CarryTargets): matching hops re-bind, changed ones are replaced (the old one retracts,
    /// the new one draws itself in quickly), extra ones retract.</summary>
    public void SetTargets(List<KeyBlock> targets, SongManager sm)
    {
        if (Retiring || sm == null) return;
        int k = 0;
        for (; k < targets.Count; k++)
        {
            var want = targets[k];
            if (want == null) break;
            int idx = sm.Islands.IndexOf(want);
            if (k < hops.Count && hops[k].index == idx)
            {
                if (hops[k].target != want) { hops[k].target = want; if (hops[k].sigil != null) hops[k].sigil.Build(want, SpellColor, k + 1); hops[k].starsDirty = true; }
                continue;
            }
            if (k < hops.Count) { StartRetract(hops[k], false, 0f); dying.Add(hops[k]); hops.RemoveAt(k); }
            var h = NewHop(want, idx, k);
            hops.Insert(k, h);
        }
        while (hops.Count > k) { var h = hops[hops.Count - 1]; StartRetract(h, false, 0f); dying.Add(h); hops.RemoveAt(hops.Count - 1); }
    }

    Hop NewHop(KeyBlock target, int idx, int k)
    {
        var h = new Hop { target = target, index = idx, mpb = new MaterialPropertyBlock(), bornFrame = Time.frameCount };
        h.glow = MakeLine("ArcGlow_" + k, glowMat, 0.9f, LineTextureMode.Stretch);
        h.dots = MakeLine("ArcDots_" + k, dotsMat, 0.34f, LineTextureMode.Tile);
        h.sigil = new Sigil(transform, "TargetSigil_" + k, true);
        h.sigil.Build(target, SpellColor, k + 1);
        // a hop found by a re-sync draws itself in quickly; the gesture (same frame) turns it into the full spell
        h.mode = 1; h.full = false; h.t = 0f; h.delay = 0f; h.draw = 0f; h.drawFrom = 0.55f; h.reached = false;
        h.sigil.StartReveal(0f, QuickSeconds);
        return h;
    }

    LineRenderer MakeLine(string name, Material mat, float width, LineTextureMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.positionCount = ArcPoints; lr.alignment = LineAlignment.View; lr.textureMode = mode;
        lr.widthMultiplier = width; lr.numCapVertices = 2; lr.numCornerVertices = 0;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.lightProbeUsage = LightProbeUsage.Off;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
        lr.colorGradient = g;
        return lr;
    }

    // ------------------------------------------------------------------ the gesture
    /// <summary>SongManager.OnCarryChanged for this island: <paramref name="before"/> → <paramref name="after"/>. Raising casts the spell on the new
    /// hop(s) (the sigil draws itself first when the carry was 0); lowering reels the removed arcs back with fizzling sparks (the last first); the
    /// chain stopping short (no island in the next column) fizzles at the source.</summary>
    public void Gesture(int before, int after)
    {
        int frame = Time.frameCount;
        if (after > before)
        {
            float lead = 0f;
            if (before == 0 && sigil != null) { sigil.StartReveal(0f, SigilDrawSeconds); lead = SigilDrawSeconds * 0.75f; }
            int cast = 0;
            for (int k = 0; k < hops.Count; k++)
            {
                var h = hops[k];
                if (h.bornFrame != frame || k < before) continue;
                h.mode = 1; h.full = true; h.t = 0f; h.delay = lead + 0.15f * cast; h.draw = 0f; h.drawFrom = 0f; h.reached = false;
                h.sigil.StartReveal(0f, 0.01f); h.sigil.Hide();
                foreach (var s in h.stars) { s.held = true; s.reveal = 0f; }
                h.starsDirty = true;
                cast++;
                Casts++;
            }
            if (cast == 0 && Source != null)
            {
                // nothing to carry onto (the chain ends here): the spell fizzles at the source
                var wm = WorldMagic.I;
                if (wm != null) wm.Fizzle(Source.VisualCenter + Vector3.up * ArcLift, SpellColor, 14);
                Fizzles++;
            }
        }
        else if (after < before)
        {
            int order = 0;
            for (int i = dying.Count - 1; i >= 0; i--)
            {
                var h = dying[i];
                if (h.retireFrame != frame) continue;
                StartRetract(h, true, 0.16f * order);
                order++;
                Retracts++;
            }
            if (after == 0) { sigilUndraw = true; undrawDelay = 0.16f * order + 0.25f; }
        }
    }

    void StartRetract(Hop h, bool full, float delay)
    {
        h.mode = 2; h.full = full; h.t = 0f; h.delay = delay; h.drawFrom = h.draw; h.retireFrame = Time.frameCount; h.pulsing = false;
        if (h.head != null) h.head.gameObject.SetActive(false);
    }

    /// <summary>The carry went to 0 (or the island is gone): every arc reels in, the sigil un-draws, then the bridge removes itself.</summary>
    public void Retire(bool full)
    {
        if (Retiring) return;
        Retiring = true; retireClock = 0f;
        int order = 0;
        for (int i = hops.Count - 1; i >= 0; i--) { StartRetract(hops[i], full, 0.12f * order); dying.Add(hops[i]); order++; }
        hops.Clear();
        sigilUndraw = true; undrawDelay = 0.12f * order + (full ? 0.25f : 0.05f);
    }

    /// <summary>A seek / stop / play: no pulse carries over (its trail is cleared).</summary>
    public void OnJump()
    {
        prevBeat = -1.0;
        foreach (var h in hops) { h.pulsing = false; if (h.head != null) { h.head.gameObject.SetActive(false); if (h.headTrail != null) h.headTrail.Clear(); } }
    }

    public void MarkStarsDirty() { foreach (var h in hops) h.starsDirty = true; }

    /// <summary>A note of <paramref name="cube"/> landed in window <paramref name="w"/> (AudioCube.OnLanded): the ghost star on that tile ignites.</summary>
    public void OnLanded(AudioCube cube, int w, AudioCube.Hit hit)
    {
        var win = cube.windows[w];
        TileInteraction tile = hit.node >= 0 ? cube.TileAt(w, hit.node) : null;
        if (tile == null) return;
        foreach (var h in hops)
        {
            if (h.target != win.island) continue;
            foreach (var s in h.stars)
            {
                if (s.dead || s.tile != tile) continue;
                s.ignite = 1f; s.hot = cube.Color;
                Ignitions++;
                var wm = WorldMagic.I;
                if (wm != null && s.t != null) for (int i = 0; i < 3; i++) wm.Sparkle(s.t.position, Random.insideUnitSphere * 0.9f + Vector3.up * 0.8f, i == 0 ? Color.white : cube.Color, 0.16f, 0.4f);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ frame
    public void Step(float dt, bool world)
    {
        twos += dt;
        bool drawTwos = twos >= 1f / Look.TwosFps || !Look.OnTwos;
        if (drawTwos) twos = 0f;
        var sm = SongManager.I;
        bool visible = world && sm != null;
        if (Source == null && !Retiring) Retire(false);
        bool playing = GlobalClock.IsPlaying;
        double beat = GlobalClock.SongBeatD;
        float dim = Source != null && Source.sleep ? 0.35f : 1f;

        // the source sigil: charging while the source column plays, un-drawing when retired
        if (sigil != null)
        {
            if (sigilUndraw)
            {
                undrawDelay -= dt;
                if (undrawDelay <= 0f && !sigil.Undrawing) { sigil.StartUndraw(Retiring ? 0.35f : 0.35f); }
            }
            bool charging = playing && Source != null && sm != null && sm.IsActiveAt(Source, (float)beat);
            if (Source != null) sigil.Pose(Source, dt, drawTwos, charging ? 1.35f : 1f, dim, visible);
            else sigil.Pose(null, dt, drawTwos, 1f, dim, false);
        }

        // flowing pearls: a slow drift at rest, one pearl per beat while playing (the belt's jerk)
        if (dotsMat != null && drawTwos)
        {
            float off;
            if (playing) { double f = beat - System.Math.Floor(beat); off = -(float)(System.Math.Floor(beat) % 1000.0) - Ease.InOutCubic(Mathf.Clamp01((float)f / 0.3f)); }
            else off = -Look.Stepped(Time.time) * 0.7f;
            dotsMat.mainTextureOffset = new Vector2(off, 0f);
        }

        for (int k = 0; k < hops.Count; k++)
        {
            var h = hops[k];
            var from = k == 0 ? Source : hops[k - 1].target;
            StepHop(h, from, k, dt, drawTwos, playing, beat, sm, visible);
        }
        for (int i = dying.Count - 1; i >= 0; i--)
        {
            var h = dying[i];
            StepHop(h, null, -1, dt, drawTwos, playing, beat, sm, visible);
            if (h.done) { DestroyHop(h); dying.RemoveAt(i); }
        }
        prevBeat = playing ? beat : -1.0;

        if (Retiring)
        {
            retireClock += dt;
            bool sigilGone = sigil == null || (sigil.Undrawing && sigil.Reveal <= 0.001f);
            if ((dying.Count == 0 && sigilGone) || retireClock > 4f) Destroy(gameObject);
        }
    }

    void StepHop(Hop h, KeyBlock from, int k, float dt, bool drawTwos, bool playing, double beat, SongManager sm, bool visible)
    {
        // ends of the arc (a retracting hop keeps its last ends when an island went away)
        if (from != null && h.target != null)
        {
            h.a = from.VisualCenter + Vector3.up * ArcLift;
            h.b = h.target.VisualCenter + Vector3.up * ArcLift;
            h.h = Magic.ArcHeight(h.a, h.b);
            h.haveEnds = true;
        }
        else if (k < 0 && h.target != null && h.haveEnds)
        {
            h.b = h.target.VisualCenter + Vector3.up * ArcLift;
        }
        if (!h.haveEnds) { SetVisible(h, false); return; }
        var wm = WorldMagic.I;

        float whip = 0f;
        if (h.mode == 1)
        {
            if (h.delay > 0f) { h.delay -= dt; h.draw = 0f; }
            else
            {
                float dur = h.full ? CastSeconds : QuickSeconds;
                h.t += dt;
                float u = Mathf.Clamp01(h.t / dur);
                h.draw = Mathf.Lerp(h.drawFrom, 1f, Ease.OutCubic(u));
                whip = h.full ? (1f - u) : 0f;
                if (h.full) CastTip(h, wm);
                if (u >= 1f) { h.mode = 0; h.draw = 1f; Reach(h, wm); if (h.head != null && !h.pulsing) h.head.gameObject.SetActive(false); }
            }
        }
        else if (h.mode == 2)
        {
            if (h.delay > 0f) h.delay -= dt;
            else
            {
                float dur = h.full ? RetractSeconds : QuickSeconds;
                h.t += dt;
                float u = Mathf.Clamp01(h.t / dur);
                h.draw = h.drawFrom * (1f - Ease.InCubic(u));
                if (h.full && wm != null && drawTwos)
                {
                    Vector3 tip = Magic.ArcPoint(h.a, h.b, h.h, h.draw);
                    wm.Fizzle(tip, SpellColor, 3);
                }
                if (h.sigil != null && !h.sigil.Undrawing) h.sigil.StartUndraw(h.full ? 0.3f : 0.15f);
                foreach (var s in h.stars) if (!s.dead && s.revealTo > 0f) { s.revealTo = 0f; if (h.full && wm != null && s.t != null) wm.Spark(s.t.position, Random.insideUnitSphere + Vector3.up, SpellColor, 0.14f, 0.5f); }
                if (u >= 1f) { h.done = true; SetVisible(h, false); return; }
            }
        }

        // the arc
        bool show = visible && h.draw > 0.002f;
        SetVisible(h, show);
        float dimHop = (Source != null && Source.sleep) || (h.target != null && h.target.sleep) ? 0.35f : 1f;
        if (show)
        {
            Vector3 side = Vector3.Cross(Vector3.up, h.b - h.a);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.forward;
            float hh = h.h * (1f + 0.45f * whip * Mathf.Sin(Mathf.PI * (1f - whip)));
            for (int i = 0; i < ArcPoints; i++)
            {
                float u = h.draw * i / (ArcPoints - 1f);
                Vector3 p = Magic.ArcPoint(h.a, h.b, hh, u);
                if (whip > 0f) p += side * (0.9f * whip * u * Mathf.Sin(Mathf.PI * 2f * (1.5f * u - 2.2f * (1f - whip))));
                pts[i] = p;
            }
            h.dots.SetPositions(pts);
            h.glow.SetPositions(pts);
            float boost = h.pulsing ? 2.6f : (h.mode == 1 && h.full ? 1.8f : 1f);
            h.mpb.Clear(); h.mpb.SetFloat(IntensityId, 0.5f * boost * dimHop); h.mpb.SetColor(ColorId, SpellColor);
            h.glow.SetPropertyBlock(h.mpb);
            h.mpb.Clear(); h.mpb.SetColor(ColorId, Palette.A(Color.Lerp(SpellColor, Color.white, 0.5f), dimHop)); h.dots.SetPropertyBlock(h.mpb);
        }

        // the target's sigil
        if (h.sigil != null)
        {
            if (h.target != null) h.sigil.Pose(h.target, dt, drawTwos, 1f, dimHop, visible);
            else h.sigil.Pose(null, dt, drawTwos, 1f, dimHop, false);
        }

        if (k >= 0 && h.mode == 0 && visible) StepPulse(h, dt, playing, beat, sm, wm);
        else if (h.pulsing && h.mode != 1) { h.pulsing = false; if (h.head != null) h.head.gameObject.SetActive(false); }

        // ghost stars
        if (h.target != null && h.mode != 2)
        {
            h.starsClock += dt;
            if (h.starsDirty && h.starsClock > 0.25f) { h.starsClock = 0f; h.starsDirty = false; RefreshStars(h); }
        }
        PoseStars(h, dt, drawTwos, visible);
    }

    void SetVisible(Hop h, bool on)
    {
        if (h.dots != null && h.dots.enabled != on) h.dots.enabled = on;
        if (h.glow != null && h.glow.enabled != on) h.glow.enabled = on;
    }

    /// <summary>The streamer's tip while it is thrown: a bright star shedding sparkles.</summary>
    void CastTip(Hop h, WorldMagic wm)
    {
        EnsureHead(h);
        Vector3 tip = Magic.ArcPoint(h.a, h.b, h.h, h.draw);
        if (!h.head.gameObject.activeSelf) { h.head.gameObject.SetActive(true); if (h.headTrail != null) h.headTrail.Clear(); }
        h.head.position = tip;
        PoseHead(h, 0.7f);
        h.shed += Time.deltaTime;
        if (wm != null && h.shed > 1f / 30f)
        {
            h.shed = 0f;
            wm.Sparkle(tip, Random.insideUnitSphere * 0.6f, Random.value < 0.5f ? SpellColor : Color.white, Random.Range(0.14f, 0.26f), Random.Range(0.3f, 0.55f));
        }
    }

    /// <summary>The streamer reached its target: the target flashes its chord colour, its sigil draws itself, and (the full spell) the pattern's
    /// beads fly along to the target tiles, the ghost stars appearing where they land.</summary>
    void Reach(Hop h, WorldMagic wm)
    {
        if (h.reached) return;
        h.reached = true;
        if (h.target == null) return;
        if (!h.full) { h.sigil.StartReveal(Mathf.Max(0f, h.sigil.Reveal), QuickSeconds); foreach (var s in h.stars) { s.held = false; } return; }
        h.target.Pulse(1.4f);
        Fx.Ripple(h.target.VisualCenter, h.target.chordColor, 3f, 0.6f);
        Fx.KirbyDots(h.b + Vector3.up * 0.2f, SpellColor, 1.1f);
        h.sigil.StartReveal(0f, 0.32f);
        if (wm != null) wm.LandBurst(h.b, SpellColor);
        // ghost copies of the pattern's beads, from where the pattern plays before this hop to where it plays on the target
        RefreshStars(h);
        foreach (var s in h.stars) { s.held = true; s.reveal = 0f; s.revealTo = 0f; }
        int launched = 0;
        var bound = new HashSet<Star>();
        // a quick stagger that never drags: the whole volley leaves within ≈ 0.4 s
        int total = 0;
        foreach (var c in SequenceMaster.Cubes) if (IsResident(c)) total += c.nodes.Count;
        float stagger = Mathf.Min(0.045f, 0.4f / Mathf.Max(1, Mathf.Min(total, 28) - 1));
        var fromIsland = hops.IndexOf(h) <= 0 ? Source : hops[hops.IndexOf(h) - 1].target;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (!IsResident(c)) continue;
            var src = TilesFor(c, fromIsland);
            var dst = TilesFor(c, h.target);
            int n = Mathf.Min(src.Count, dst.Count);
            for (int i = 0; i < n && launched < 28; i++)
            {
                if (src[i] == null || dst[i] == null) continue;
                Vector3 p0 = src[i].Top + Vector3.up * 0.3f, p1 = dst[i].Top + Vector3.up * 0.3f;
                var star = FindStar(h, dst[i]);
                if (star != null) bound.Add(star);
                Color col = c.Color;
                if (wm != null)
                    wm.LaunchBead(p0, p1, col, Magic.ArcHeight(p0, p1) * 0.75f, 0.55f, stagger * launched, 0.4f,
                        () => { if (star != null) { star.held = false; star.revealTo = 1f; star.ignite = 0.8f; star.hot = col; } });
                launched++; SpellBeads++;
            }
        }
        // stars without a bead of their own (or no bead at all) appear at once
        foreach (var s in h.stars) if (!bound.Contains(s)) { s.held = false; s.revealTo = 1f; }
    }

    Star FindStar(Hop h, TileInteraction tile) { foreach (var s in h.stars) if (!s.dead && s.tile == tile) return s; return null; }

    // ------------------------------------------------------------------ the pulse (playing)
    void StepPulse(Hop h, float dt, bool playing, double beat, SongManager sm, WorldMagic wm)
    {
        bool on = false;
        // a pure function of the song beat: paused mid-flight, the pulse holds where the beat is (stopped at 0: none)
        if ((playing || beat > 1e-6) && sm != null && h.target != null)
        {
            int col = h.target.column;
            double cs = sm.ColumnStart(col);
            double t0 = cs - FlightBeats;
            if (beat >= t0 && beat < cs)
            {
                on = true;
                float u = (float)((beat - t0) / FlightBeats);
                EnsureHead(h);
                if (!h.pulsing) { Pulses++; if (h.headTrail != null) h.headTrail.Clear(); }
                if (!h.head.gameObject.activeSelf) h.head.gameObject.SetActive(true);
                Vector3 p = Magic.ArcPoint(h.a, h.b, h.h, Ease.InOutCubic(u));
                h.head.position = p; h.pulsePos = p;
                PoseHead(h, 0.85f + 0.3f * Mathf.Sin(u * Mathf.PI));
                h.shed += dt;
                if (wm != null && h.shed > 1f / 30f)
                {
                    h.shed = 0f;
                    wm.Sparkle(p, Random.insideUnitSphere * 0.5f, Random.value < 0.5f ? SpellColor : Color.white, Random.Range(0.14f, 0.26f), Random.Range(0.3f, 0.5f));
                }
            }
            // the column starts: the target's sigil flares
            if (playing && prevBeat >= 0.0 && prevBeat < cs && beat >= cs && beat - prevBeat < 1.0)
            {
                Flares++;
                if (h.sigil != null) h.sigil.Flare();
                h.target.Pulse(0.9f);
                if (wm != null)
                    for (int i = 0; i < 8; i++)
                        wm.Sparkle(h.b, Random.insideUnitSphere * 1.6f + Vector3.up * 0.8f, i % 2 == 0 ? SpellColor : Color.white, Random.Range(0.16f, 0.3f), Random.Range(0.35f, 0.6f));
            }
        }
        if (!on && h.pulsing && h.head != null) h.head.gameObject.SetActive(false);
        h.pulsing = on;
    }

    void EnsureHead(Hop h)
    {
        if (h.head != null) return;
        var go = new GameObject("PulseHead");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
        h.headRend = go.AddComponent<MeshRenderer>();
        h.headRend.sharedMaterial = headMat;
        h.headRend.shadowCastingMode = ShadowCastingMode.Off; h.headRend.receiveShadows = false; h.headRend.lightProbeUsage = LightProbeUsage.Off;
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.32f; tr.startWidth = 0.34f; tr.endWidth = 0f; tr.minVertexDistance = 0.05f; tr.numCapVertices = 3;
        tr.alignment = LineAlignment.View; tr.textureMode = LineTextureMode.Stretch;
        var tm = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Color.Lerp(SpellColor, Color.white, 0.25f), 0.9f));
        tr.material = tm;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;
        tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
        h.headTrail = tr;
        h.head = go.transform;
        go.SetActive(false);
    }

    void PoseHead(Hop h, float size)
    {
        var cam = Camera.main;
        if (cam != null) h.head.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, Look.Stepped(Time.time) * 140f);
        h.head.localScale = new Vector3(size, size, 1f);
    }

    // ------------------------------------------------------------------ ghost stars
    bool IsResident(AudioCube c) => c != null && Source != null && !c.rider && c.moon < 0 && c.Moon == null && c.Island == Source && c.nodes.Count > 0;

    /// <summary>The tiles cube <paramref name="c"/> plays on <paramref name="island"/> (AudioCube.TilesOn; a cube that has no window there yet —
    /// the timeline without K's carried windows — maps its grid offsets like a Rider, rows clamped).</summary>
    static List<TileInteraction> TilesFor(AudioCube c, KeyBlock island)
    {
        var l = c.TilesOn(island);
        if (l.Count > 0 || island == null || island == c.Island) return l;
        bool hasWindow = false;
        foreach (var w in c.windows) if (w.island == island) { hasWindow = true; break; }
        if (hasWindow || island.cols <= 0 || island.rows <= 0) return l;
        for (int n = 0; n < c.nodes.Count; n++)
        {
            var home = c.nodes[n];
            if (home == null) continue;
            int x = Mathf.Clamp(n < c.gridX.Count ? c.gridX[n] : home.gridX, 0, island.cols - 1);
            int z = Mathf.Clamp(n < c.gridZ.Count ? c.gridZ[n] : home.gridZ, 0, island.rows - 1);
            var t = island.GetTile(x, z);
            if (t != null) l.Add(t);
        }
        return l;
    }

    /// <summary>The distinct tiles the source's resident cubes play on hop <paramref name="h"/>'s target (for the tests' expected count).</summary>
    public static List<TileInteraction> ExpectedTiles(KeyBlock source, KeyBlock target)
    {
        var set = new List<TileInteraction>();
        if (source == null || target == null) return set;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.rider || c.moon >= 0 || c.Moon != null || c.Island != source || c.nodes.Count == 0) continue;
            foreach (var t in TilesFor(c, target)) if (t != null && !set.Contains(t)) set.Add(t);
        }
        return set;
    }

    void RefreshStars(Hop h)
    {
        var want = new List<TileInteraction>();
        var col = new List<Color>();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (!IsResident(c)) continue;
            foreach (var t in TilesFor(c, h.target)) if (t != null && !want.Contains(t)) { want.Add(t); col.Add(c.Color); }
        }
        foreach (var s in h.stars) if (!want.Contains(s.tile)) { s.dead = true; s.revealTo = 0f; }
        for (int i = 0; i < want.Count; i++)
        {
            Star s = null;
            foreach (var o in h.stars) if (!o.dead && o.tile == want[i]) { s = o; break; }
            if (s == null)
            {
                s = new Star { tile = want[i], phase = Random.value * 6.28f, reveal = 0f, revealTo = 1f, held = h.mode == 1 && h.full && !h.reached };
                var go = new GameObject("GhostStar");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.Sprite;
                s.r = go.AddComponent<MeshRenderer>();
                s.r.sharedMaterial = starMat;
                s.r.shadowCastingMode = ShadowCastingMode.Off; s.r.receiveShadows = false; s.r.lightProbeUsage = LightProbeUsage.Off;
                s.t = go.transform;
                s.t.localScale = Vector3.one * 0.001f;
                h.stars.Add(s);
            }
            s.local = h.target.transform.InverseTransformPoint(want[i].Top);
            s.pale = Color.Lerp(col[i], MagicTextures.FoamCream, 0.45f);
            if (s.hot.a <= 0f) s.hot = col[i];
        }
    }

    void PoseStars(Hop h, float dt, bool drawTwos, bool visible)
    {
        var cam = Camera.main;
        for (int i = h.stars.Count - 1; i >= 0; i--)
        {
            var s = h.stars[i];
            float to = s.held ? 0f : s.revealTo;
            s.reveal = Mathf.MoveTowards(s.reveal, to, dt / 0.22f);
            s.ignite = Mathf.MoveTowards(s.ignite, 0f, dt / 0.75f);
            if (s.dead && s.reveal <= 0f)
            {
                if (s.t != null) Destroy(s.t.gameObject);
                h.stars.RemoveAt(i);
                continue;
            }
            if (s.t == null) continue;
            bool on = visible && s.reveal > 0.001f && h.target != null;
            if (s.t.gameObject.activeSelf != on) s.t.gameObject.SetActive(on);
            if (!on) continue;
            // the star follows its tile every frame (the island rises, rides its belt); its bob, twinkle and colour are drawn on twos
            float t12 = Look.Stepped(Time.time);
            Vector3 p = h.target.transform.TransformPoint(s.local) + Vector3.up * (0.36f + 0.06f * Mathf.Sin(t12 * 2.2f + s.phase));
            s.t.position = p;
            if (cam != null) s.t.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, 8f * Mathf.Sin(s.phase + t12 * 1.3f));
            if (!drawTwos && s.reveal >= 1f && s.ignite <= 0f) continue;
            float tw = 1f + 0.14f * Mathf.Sin(t12 * 5f + s.phase);
            float sc = 0.42f * Ease.OutBack(s.reveal) * tw * (1f + 0.75f * s.ignite);
            s.t.localScale = new Vector3(sc, sc, 1f);
            Color c = Color.Lerp(s.pale, Color.Lerp(s.hot, Color.white, 0.15f * s.ignite), s.ignite);
            c.a = Mathf.Lerp(0.62f, 1f, s.ignite) * Mathf.Clamp01(s.reveal * 2f);
            starMpb.Clear(); starMpb.SetColor(ColorId, c);
            s.r.SetPropertyBlock(starMpb);
        }
    }

    void DestroyHop(Hop h)
    {
        if (h.dots != null) Destroy(h.dots.gameObject);
        if (h.glow != null) Destroy(h.glow.gameObject);
        if (h.head != null) { if (h.headTrail != null && h.headTrail.material != null) Destroy(h.headTrail.material); Destroy(h.head.gameObject); }
        if (h.sigil != null) h.sigil.Destroy();
        foreach (var s in h.stars) if (s.t != null) Destroy(s.t.gameObject);
        h.stars.Clear();
    }

    void OnDestroy()
    {
        foreach (var h in hops) DestroyHop(h);
        foreach (var h in dying) DestroyHop(h);
        hops.Clear(); dying.Clear();
        if (sigil != null) sigil.Destroy();
        if (dotsMat != null) Destroy(dotsMat);
        if (glowMat != null) Destroy(glowMat);
        if (starMat != null) Destroy(starMat);
        if (headMat != null) Destroy(headMat);
    }

    // ================================================================== the sigil
    /// <summary>
    /// A ring of runes round a platform: a dark ink band following the platform's rounded outline (outside the beat track) with bright border
    /// lines, pale runes drifting round it (one per ~0.55 u), a flat additive glow in the chord colour under it; a target's is thinner, with its
    /// place in the chain as pips at its front. It draws itself rune by rune (from the front centre, clockwise like the beat track), un-draws in
    /// reverse, and flares (brighter, bigger runes) when its column starts.
    /// </summary>
    class Sigil
    {
        readonly Transform root;
        readonly bool small;
        MeshRenderer bandRend, glowRend, runeRend;
        MeshFilter bandFilter, glowFilter, runeFilter;
        Mesh bandMesh, glowMesh, runeMesh;
        Material bandMat, glowMat, runeMat;
        Vector3[] rv; Vector2[] ruv; Color[] rc; int[] rt; Color[] bandCols;
        float hx, hz, cr, width; int n, pips; float drift;
        float reveal = 1f, revealFrom, revealTo = 1f, revealT = 9f, revealDur = 0.3f; bool undrawing;
        float flare; Color colour; int lastShown = -1; float builtW = -1f, builtD = -1f;
        public float Reveal => reveal;
        public bool Undrawing => undrawing;
        public int RuneCount => n;
        const float Margin = 0.95f, LocalY = -0.32f;

        public Sigil(Transform parent, string name, bool small)
        {
            this.small = small;
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            glowRend = Part("Glow", out glowFilter);
            bandRend = Part("Band", out bandFilter);
            runeRend = Part("Runes", out runeFilter);
            bandMat = Fx.Alpha(MagicTextures.Band, Color.white);
            glowMat = Fx.Additive(MagicTextures.Soft, Color.white, 0.4f);
            runeMat = Fx.Alpha(MagicTextures.Runes, Color.white);
            runeMat.renderQueue = bandMat.renderQueue + 1;
            bandRend.sharedMaterial = bandMat; glowRend.sharedMaterial = glowMat; runeRend.sharedMaterial = runeMat;
            runeMesh = new Mesh { name = "sigilRunes" }; runeMesh.MarkDynamic();
            runeFilter.sharedMesh = runeMesh;
        }

        MeshRenderer Part(string name, out MeshFilter mf)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            return mr;
        }

        /// <summary>Fits the sigil to <paramref name="kb"/>'s platform; <paramref name="place"/> ≥ 1 = a target's place in the chain (its pips).</summary>
        public void Build(KeyBlock kb, Color c, int place)
        {
            colour = c; pips = place;
            bandMat.SetColor("_Color", Color.Lerp(c, Color.white, 0.62f));
            glowMat.SetColor("_Color", c);
            if (kb == null) return;
            float w = kb.Width, d = kb.Depth;
            if (Mathf.Abs(w - builtW) < 0.01f && Mathf.Abs(d - builtD) < 0.01f && runeMesh.vertexCount > 0) return;
            builtW = w; builtD = d;
            float margin = small ? Margin - 0.12f : Margin;
            width = small ? 0.38f : 0.52f;
            hx = w * 0.5f + margin; hz = d * 0.5f + margin; cr = 0.24f + margin;
            if (bandMesh != null) Object.Destroy(bandMesh);
            if (glowMesh != null) Object.Destroy(glowMesh);
            bandMesh = MagicMeshes.TrackBand(hx, hz, cr, width, 160);
            bandCols = new Color[bandMesh.vertexCount];
            for (int i = 0; i < bandCols.Length; i++) bandCols[i] = Color.white;
            bandMesh.colors = bandCols;
            bandFilter.sharedMesh = bandMesh;
            glowMesh = MagicMeshes.TrackBand(hx, hz, cr, width * 3.2f, 160);
            glowFilter.sharedMesh = glowMesh;
            float per = MeshFactory.TrackLength(hx, hz, cr);
            n = Mathf.Max(8, Mathf.RoundToInt(per / (small ? 0.92f : 0.7f)));
            int pipN = small ? Mathf.Clamp(pips, 0, 3) : 0;
            int quads = n + pipN;
            rv = new Vector3[quads * 4]; ruv = new Vector2[quads * 4]; rc = new Color[quads * 4]; rt = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                rt[q * 6] = q * 4; rt[q * 6 + 1] = q * 4 + 2; rt[q * 6 + 2] = q * 4 + 1; rt[q * 6 + 3] = q * 4; rt[q * 6 + 4] = q * 4 + 3; rt[q * 6 + 5] = q * 4 + 2;
            }
            runeMesh.Clear();
            runeMesh.vertices = rv; runeMesh.uv = ruv; runeMesh.colors = rc; runeMesh.triangles = rt;
            runeMesh.bounds = new Bounds(Vector3.zero, new Vector3(hx * 2f + 2f, 2f, hz * 2f + 2f));
            lastShown = -1;
            WriteRunes(0f);
        }

        public void StartReveal(float from, float seconds) { undrawing = false; revealFrom = Mathf.Clamp01(from); reveal = revealFrom; revealTo = 1f; revealT = 0f; revealDur = Mathf.Max(0.01f, seconds); lastShown = -1; }
        public void StartUndraw(float seconds) { undrawing = true; revealFrom = reveal; revealTo = 0f; revealT = 0f; revealDur = Mathf.Max(0.01f, seconds); }
        public void Hide() { reveal = 0f; revealT = 9f; revealTo = 0f; undrawing = false; }
        public void Flare() { flare = 1f; }

        public void Pose(KeyBlock kb, float dt, bool drawTwos, float bright, float dim, bool visible)
        {
            if (revealT < 1f)
            {
                revealT = Mathf.Min(1f, revealT + dt / revealDur);
                float prev = reveal;
                reveal = Mathf.Lerp(revealFrom, revealTo, revealT);
                // a sparkle per rune as it is written (or rubbed out)
                var wm = WorldMagic.I;
                if (wm != null && kb != null && n > 0 && revealDur > 0.2f)
                {
                    int a = Mathf.FloorToInt(Mathf.Min(prev, reveal) * n), b = Mathf.FloorToInt(Mathf.Max(prev, reveal) * n);
                    for (int i = a; i < b && i < n; i++)
                    {
                        Vector3 nn; Vector3 p = MeshFactory.TrackPoint(hx, hz, cr, (i + 0.5f) / n, out nn);
                        Vector3 w = root.TransformPoint(p);
                        if (undrawing) wm.Spark(w, Random.insideUnitSphere * 0.8f + Vector3.up * 0.6f, colour, 0.14f, 0.45f);
                        else wm.Sparkle(w + Vector3.up * 0.1f, Vector3.up * 0.9f + Random.insideUnitSphere * 0.4f, i % 2 == 0 ? colour : Color.white, 0.2f, 0.45f);
                    }
                }
            }
            flare = Mathf.MoveTowards(flare, 0f, dt / 0.7f);
            bool on = visible && kb != null && reveal > 0.001f;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            root.position = kb.VisualCenter + Vector3.up * LocalY;
            drift += dt * (small ? -0.004f : 0.006f);
            float glow = (small ? 0.3f : 0.42f) * bright * (1f + 2.2f * flare) * dim;
            glowMat.SetFloat("_Intensity", glow);
            glowMat.SetFloat("_Fill", reveal >= 0.999f ? 2f : reveal);
            if (drawTwos || revealT < 1f) WriteRunes(bright);
        }

        void WriteRunes(float bright)
        {
            if (rv == null) return;
            float s = (small ? 0.32f : 0.44f) * (1f + 0.25f * flare);
            Color pale = Color.Lerp(colour, Color.white, 0.7f + 0.25f * flare);
            float fillTo = reveal * n;
            for (int i = 0; i < n; i++)
            {
                float t01 = Mathf.Repeat((i + 0.5f) / n + drift, 1f);
                Vector3 nn;
                Vector3 p = MeshFactory.TrackPoint(hx, hz, cr, t01, out nn);
                Vector3 tan = new Vector3(-nn.z, 0f, nn.x);   // counter-clockwise: the rune's right
                Vector3 up = -nn;                              // inward: the rune's top
                float k = Mathf.Clamp01(fillTo - i);
                float pop = k <= 0f ? 0f : (k >= 1f ? 1f : Ease.OutBack(k, 2.2f));
                float ss = s * pop;
                int b = i * 4;
                rv[b] = p - tan * ss * 0.5f - up * ss * 0.5f + Vector3.up * 0.02f;
                rv[b + 1] = p + tan * ss * 0.5f - up * ss * 0.5f + Vector3.up * 0.02f;
                rv[b + 2] = p + tan * ss * 0.5f + up * ss * 0.5f + Vector3.up * 0.02f;
                rv[b + 3] = p - tan * ss * 0.5f + up * ss * 0.5f + Vector3.up * 0.02f;
                SetTile(b, (i * 5 + 3) % MagicTextures.RuneTiles);
                Color c = pale; c.a = k > 0f ? 1f : 0f;
                rc[b] = rc[b + 1] = rc[b + 2] = rc[b + 3] = c;
            }
            // pips: a target's place in the chain, small dots at its front centre (1, 2 or 3)
            int q = n;
            for (int j = 0; j < rv.Length / 4 - n; j++, q++)
            {
                Vector3 p = new Vector3((j - (rv.Length / 4 - n - 1) * 0.5f) * 0.36f, 0.02f, -hz);
                float ss = 0.28f * (reveal > 0.9f ? 1f : 0f);
                int b = q * 4;
                rv[b] = p + new Vector3(-ss, 0f, -ss) * 0.5f; rv[b + 1] = p + new Vector3(ss, 0f, -ss) * 0.5f;
                rv[b + 2] = p + new Vector3(ss, 0f, ss) * 0.5f; rv[b + 3] = p + new Vector3(-ss, 0f, ss) * 0.5f;
                SetTile(b, MagicTextures.PipTile);
                rc[b] = rc[b + 1] = rc[b + 2] = rc[b + 3] = Color.white;
            }
            runeMesh.vertices = rv; runeMesh.uv = ruv; runeMesh.colors = rc;
            // the band draws itself with the runes
            if (bandCols != null)
            {
                int segs = bandCols.Length / 2 - 1;
                float a = reveal >= 0.999f ? 1f : reveal;
                for (int i = 0; i <= segs; i++)
                {
                    float u = i / (float)segs;
                    float al = Mathf.Clamp01((a - u) * 30f);
                    bandCols[i * 2] = new Color(1f, 1f, 1f, al); bandCols[i * 2 + 1] = new Color(1f, 1f, 1f, al);
                }
                bandMesh.colors = bandCols;
            }
        }

        /// <summary>The atlas cell of tile <paramref name="tile"/> (MagicTextures.Runes: 4 x 3, row-major from the bottom).</summary>
        void SetTile(int b, int tile)
        {
            float du = 1f / MagicTextures.RuneCols, dv = 1f / MagicTextures.RuneRows;
            float u0 = (tile % MagicTextures.RuneCols) * du, v0 = (tile / MagicTextures.RuneCols) * dv;
            ruv[b] = new Vector2(u0, v0); ruv[b + 1] = new Vector2(u0 + du, v0); ruv[b + 2] = new Vector2(u0 + du, v0 + dv); ruv[b + 3] = new Vector2(u0, v0 + dv);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            if (bandMat != null) Object.Destroy(bandMat);
            if (glowMat != null) Object.Destroy(glowMat);
            if (runeMat != null) Object.Destroy(runeMat);
            if (bandMesh != null) Object.Destroy(bandMesh);
            if (glowMesh != null) Object.Destroy(glowMesh);
            if (runeMesh != null) Object.Destroy(runeMesh);
        }
    }
}
