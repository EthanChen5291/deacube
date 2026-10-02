using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The playhead (SPEC §2.4): an additive sphere with a 0.6 s trail whose position is derived every frame from
/// GlobalClock.SongBeatD and SongManager.MeasureStarts (never integrated, so it is always on time and follows a
/// dragged island for free). It laps the lit island's beat ring for local ∈ [0, L − 1), flies along the Route cable on
/// an elevated arc during the last beat (pre-glowing the next hub), fires the arrival kit when the lit island changes,
/// freezes when paused and docks at hub 0 when stopped. Feeds the camera leash (OrbitCamera.LeashTarget).
/// v3 (SPEC v3 §3.3): a member of a merged group is lapped along its platform margin (KeyBlock.LapPoint: never through the
/// neighbour's tiles) and a seam is crossed with a low glide (Route.PositionAlong) instead of the high arc.
/// F10: the ball, its ring and its trail wear the lit island's chord colour as it is (no lerp toward white) with a faint glow (≤ 0.15 ×):
/// a white emissive ball under the bloom and the print misregistration read as a white blob with colour fringes.
/// v4 (SPEC v4 §3 K3): the comet laps the lit COLUMN's anchor along its beat track and flies anchor to anchor; every other island of the lit
/// column gets a satellite comet (SatelliteScale, the same style in its own chord colour) lapping its own track in sync, popping in when the
/// column starts and out when it ends. Focus loop (a GlobalClock region): the last-beat flight of the region's last column goes back to the
/// region's first column instead of on to the next one.
/// The arrival kit is soft (the user, 2026-09-29: the hub "shouldnt shoot a laser ray upon every measure"): the islands of the new column
/// pulse, the sea ripples under them and the bloom bumps once; no sky beam, no starburst at the hub (the hub fills as the column's gauge).
/// v5 (SPEC v5 §2.5): a column with repeats plays several PASSES: the comet laps its anchor once per pass (riding the belt with it: LapPoint
/// follows the island's shown position) and flies on only in the last beat of the last pass; satellites show on the islands playing the pass.
/// </summary>
public class Comet : MonoBehaviour
{
    public static Comet I;

    public Vector3 Position => transform.position;
    public bool InFlight { get; private set; }
    /// <summary>The island the comet is on / leaving, and the one it flies to (equal while lapping). v4: column anchors.</summary>
    public int FromIsland, ToIsland;
    /// <summary>v4: the column the comet is on / leaving and the one it flies to.</summary>
    public int FromColumn { get; private set; }
    public int ToColumn { get; private set; }
    /// <summary>0..1 along the flight (0 while lapping).</summary>
    public float FlightT { get; private set; }
    /// <summary>v4: satellites showing (the other islands of the lit column).</summary>
    public int SatelliteCount { get { int n = 0; foreach (var s in sats) if (s.island != null && s.shown > 0.01f && !s.leaving) n++; return n; } }
    /// <summary>v4: satellite <paramref name="k"/>'s island and position (tests / captures).</summary>
    public KeyBlock SatelliteIsland(int k) => k >= 0 && k < sats.Count ? sats[k].island : null;
    public Vector3 SatellitePosition(int k) => k >= 0 && k < sats.Count && sats[k].root != null ? sats[k].root.position : Vector3.zero;
    /// <summary>Arrival at a column's anchor (its index) while playing: after the pulse, the sea ripple and the bloom bump.</summary>
    public event Action<int> OnArrive;

    Transform core; Renderer coreRend; Material coreMat; Transform glow; Renderer glowRend; Material glowMat;
    TrailRenderer trail; Material trailMat;
    int lastLit = -1; bool looped; Color color = Color.white; bool visible = true; bool hooked;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

    class Sat
    {
        public Transform root, core, glow; public Material coreMat, glowMat, trailMat; public TrailRenderer trail;
        public KeyBlock island; public float shown; public bool leaving; public Vector3 lastPos;
    }
    readonly List<Sat> sats = new List<Sat>();
    readonly List<KeyBlock> colBuf = new List<KeyBlock>(ProjectConfig.MaxLanes);

    public static Comet Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<Comet>();
            if (I == null) I = new GameObject("Comet").AddComponent<Comet>();
        }
        return I;
    }

    void Awake() { I = this; Build(); }
    void OnEnable() { GlobalClock.OnSeek += Jump; GlobalClock.OnStop += Jump; GlobalClock.OnPlay += Jump; GlobalClock.OnLoop += HandleLoop; }
    void OnDisable() { GlobalClock.OnSeek -= Jump; GlobalClock.OnStop -= Jump; GlobalClock.OnPlay -= Jump; GlobalClock.OnLoop -= HandleLoop; }
    void Jump() { lastLit = -1; if (trail != null) trail.Clear(); foreach (var s in sats) if (s.trail != null) s.trail.Clear(); }
    void HandleLoop() { looped = true; }

    void Build()
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var sc = sphere.GetComponent<Collider>(); if (sc != null) Destroy(sc);
        sphere.name = "Core";
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * 0.44f;
        coreRend = sphere.GetComponent<MeshRenderer>();
        // integration (comic look): an ink-outlined, cel-shaded, self-lit ball instead of a flat additive blob
        coreMat = Fx.Lit(Color.white, 0.1f, 0f, true);
        coreRend.sharedMaterial = coreMat;
        coreRend.shadowCastingMode = ShadowCastingMode.Off; coreRend.receiveShadows = false; coreRend.lightProbeUsage = LightProbeUsage.Off;
        core = sphere.transform;

        var g = new GameObject("Glow");
        g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
        glowRend = g.AddComponent<MeshRenderer>();
        glowMat = Fx.Alpha(IconFactory.GetTexture("ringThin"), Color.white);   // a thin halo ring in the chord colour, not a glow disc
        glowRend.sharedMaterial = glowMat;
        glowRend.shadowCastingMode = ShadowCastingMode.Off; glowRend.receiveShadows = false; glowRend.lightProbeUsage = LightProbeUsage.Off;
        g.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
        glow = g.transform;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.45f; trail.startWidth = 0.16f; trail.endWidth = 0f; trail.minVertexDistance = 0.04f;
        trail.numCapVertices = 3; trail.numCornerVertices = 3; trail.alignment = LineAlignment.View; trail.textureMode = LineTextureMode.Stretch;
        trailMat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white);   // a flat tapered ribbon in the chord colour (no white additive laser)
        trail.material = trailMat;
        trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false; trail.lightProbeUsage = LightProbeUsage.Off;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = grad;
        trail.emitting = true;
        SetColor(Color.white);
    }

    /// <summary>v4: one satellite comet (pooled): the main comet's style at SatelliteScale.</summary>
    Sat MakeSat(int k)
    {
        var s = new Sat();
        var root = new GameObject("Satellite_" + k).transform;
        root.SetParent(transform.parent, false);
        s.root = root;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var sc = sphere.GetComponent<Collider>(); if (sc != null) Destroy(sc);
        sphere.name = "Core";
        sphere.transform.SetParent(root, false);
        var r = sphere.GetComponent<MeshRenderer>();
        s.coreMat = Fx.Lit(Color.white, 0.1f, 0f, true);
        r.sharedMaterial = s.coreMat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off;
        s.core = sphere.transform;
        var g = new GameObject("Glow");
        g.transform.SetParent(root, false);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
        var gr = g.AddComponent<MeshRenderer>();
        s.glowMat = Fx.Alpha(IconFactory.GetTexture("ringThin"), Color.white);
        gr.sharedMaterial = s.glowMat;
        gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = false; gr.lightProbeUsage = LightProbeUsage.Off;
        s.glow = g.transform;
        s.trail = root.gameObject.AddComponent<TrailRenderer>();
        s.trail.time = 0.32f; s.trail.startWidth = 0.1f; s.trail.endWidth = 0f; s.trail.minVertexDistance = 0.04f;
        s.trail.numCapVertices = 2; s.trail.alignment = LineAlignment.View; s.trail.textureMode = LineTextureMode.Stretch;
        s.trailMat = Fx.Alpha(IconFactory.GetTexture("white"), Color.white);
        s.trail.material = s.trailMat;
        s.trail.colorGradient = trail.colorGradient;
        s.trail.shadowCastingMode = ShadowCastingMode.Off; s.trail.receiveShadows = false; s.trail.lightProbeUsage = LightProbeUsage.Off;
        root.gameObject.SetActive(false);
        return s;
    }

    /// <summary>F10: the ball's glow as a share of its colour.</summary>
    public const float CoreEmission = 0.15f;

    void SetColor(Color c)
    {
        color = c;
        coreMat.SetColor(BaseColorId, c);
        coreMat.SetColor(EmissionId, c * CoreEmission);
        glowMat.SetColor(ColorId, new Color(c.r, c.g, c.b, 0.8f));
        trailMat.SetColor(ColorId, new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 0.9f));
    }

    void SetVisible(bool v)
    {
        if (visible == v) return;
        visible = v;
        if (core != null) core.gameObject.SetActive(v);
        if (glow != null) glow.gameObject.SetActive(v);
        if (trail != null) { trail.emitting = v; if (!v) trail.Clear(); }
    }

    void Hook()
    {
        if (hooked || OrbitCamera.I == null) return;
        OrbitCamera.I.LeashTarget = () => I != null ? I.Position : Vector3.zero;
        OrbitCamera.I.LeashInFlight = () => I != null && I.InFlight;
        hooked = true;
    }

    void Update()
    {
        Hook();
        var sm = SongManager.I;
        bool has = sm != null && sm.HasSong && sm.MeasureStarts.Count == sm.Islands.Count && sm.ColumnCount > 0;
        SetVisible(has);
        if (!has) { InFlight = false; FlightT = 0f; lastLit = -1; looped = false; UpdateSatellites(null, -1, 0f, 0); return; }

        int nc = sm.ColumnCount;
        bool playing = GlobalClock.IsPlaying;
        double beat = GlobalClock.SongBeatD;
        int col = Mathf.Clamp(sm.ActiveColumn((float)beat), 0, nc - 1);
        var kb = sm.AnchorOf(col);
        if (kb == null) return;
        int i = sm.ColumnFirst(col);
        // v5: one lap per pass; the flight to the next column happens in the last beat of the LAST pass
        int passes = Mathf.Max(1, sm.ColumnPasses(col));
        float L = Mathf.Max(1f, sm.PassLength(col));
        float colLocal = (float)(beat - sm.ColumnStart(col));
        int pass = Mathf.Clamp(Mathf.FloorToInt(colLocal / L + 1e-5f), 0, passes - 1);
        float local = colLocal - pass * L;
        bool lastPass = pass == passes - 1;
        int nextCol = (col + 1) % nc;
        bool loopBack = false;
        if (GlobalClock.HasRegion)
        {
            // the focus loop: the region's last column flies back to its first column (never on to the next one)
            int rFirst = Mathf.Clamp(sm.ActiveColumn((float)GlobalClock.RegionStart + 1e-3f), 0, nc - 1);
            int rLast = Mathf.Clamp(sm.ActiveColumn((float)GlobalClock.RegionEnd - 1e-3f), 0, nc - 1);
            if (col == rLast) { nextCol = rFirst; loopBack = rFirst != (col + 1) % nc || nc == 1; }
        }
        var nextKb = sm.AnchorOf(nextCol);
        bool flight = false; float t = 0f;
        Vector3 pos;
        bool docked = !playing && beat <= 1e-6;

        if (docked)
        {
            // stopped: docked on hub 0
            col = 0; kb = sm.AnchorOf(0); i = sm.ColumnFirst(0); nextCol = nc > 1 ? 1 : 0;
            pos = kb.HubTop + Vector3.up * (0.3f - kb.SeaDrop);   // v9 (W): under the sea with its island (the present mode's pre-roll)
        }
        else if (lastPass && local >= L - 1f && loopBack && nextKb != null)
        {
            // v4 focus loop: an arc from the lap exit back over to the region's first anchor (its lap start at the front)
            flight = true; t = Mathf.Clamp01(local - (L - 1f));
            Vector3 exit = kb.LapPoint((L - 1f) / L, 0.35f);
            Vector3 land = nextKb.LapPoint(0f, 0.35f);
            pos = Vector3.Lerp(exit, land, Ease.InOutCubic(t)) + Vector3.up * (1.6f * Mathf.Sin(t * Mathf.PI));
            nextKb.SetPreGlow(t);
        }
        else if (lastPass && nc >= 2 && local >= L - 1f && nextCol == col + 1 && sm.AnchorSeam(col) && nextKb != null)
        {
            // v3: the next anchor is merged onto this one: a low glide across the seam (no cable, no high arc)
            flight = true; t = Mathf.Clamp01(local - (L - 1f));
            pos = sm.Route != null && sm.Route.SegmentCount > 0 ? sm.Route.PositionAlong(col, t) : Route.GlidePoint(kb, nextKb, t);
            nextKb.SetPreGlow(t);
        }
        else if (lastPass && nc >= 2 && local >= L - 1f && nextKb != null)
        {
            // flight during the last beat: launch from the track exit point onto the cable, elevated arc, count-in glow ahead
            flight = true; t = Mathf.Clamp01(local - (L - 1f));
            Vector3 exit = kb.LapPoint((L - 1f) / L, 0.35f);
            Vector3 cable = sm.Route != null && sm.Route.SegmentCount > 0 && nextCol == (col + 1) % nc
                ? sm.Route.PositionAlong(col, t)
                : Route.Curve(kb.HubUnderside + kb.VisualOffset, nextKb.HubUnderside + nextKb.VisualOffset, t);
            Vector3 onCable = cable + Vector3.up * (1.2f + 1.6f * Mathf.Sin(t * Mathf.PI));
            float blend = Ease.OutQuad(Mathf.Clamp01(t / 0.35f));
            pos = Vector3.Lerp(exit, onCable, blend);
            nextKb.SetPreGlow(t);
        }
        else
        {
            // track lap: the in-bar pointer (θ = local / L of the lap, clockwise from the hub); one column laps the whole song
            // (a group member laps its platform margin instead of the track: KeyBlock.LapPoint)
            float lap = nc >= 2 || loopBack || passes > 1 ? Mathf.Clamp01(local / L) : Mathf.Repeat(local / L, 1f);
            pos = kb.LapPoint(lap, 0.35f);
        }

        InFlight = flight; FlightT = t; FromIsland = i; ToIsland = flight ? sm.ColumnFirst(nextCol) : i;
        FromColumn = col; ToColumn = flight ? nextCol : col;
        if ((pos - transform.position).sqrMagnitude > 36f && trail != null) trail.Clear();
        transform.position = pos;
        SetColor(kb.chordColor);   // F10: the chord colour as it is

        UpdateSatellites(sm, docked ? -1 : col, Mathf.Clamp01(local / L), docked ? 0 : pass);

        if (playing)
        {
            if (lastLit >= 0 && (i != lastLit || looped)) Arrive(i, kb, sm, col);
            lastLit = i;
        }
        else lastLit = -1;
        looped = false;
    }

    /// <summary>
    /// v4: satellites for the islands of column <paramref name="col"/> other than its anchor, lapping their own tracks at the main comet's lap fraction
    /// <paramref name="lap"/> (they finish the lap at the column's end: lap 1 = back at the front centre). A satellite whose island leaves the lit column
    /// pops out (0.12 s), a new one pops in (0.18 s, overshoot). <paramref name="col"/> -1 = none (stopped / no song). v5: only the islands playing
    /// <paramref name="pass"/> (an island resting in the column's later passes has none then).
    /// </summary>
    void UpdateSatellites(SongManager sm, int col, float lap, int pass)
    {
        float dt = Time.deltaTime;
        colBuf.Clear();
        if (sm != null && col >= 0)
        {
            sm.ColumnIslands(col, colBuf); if (colBuf.Count > 0) colBuf.RemoveAt(0);
            for (int k = colBuf.Count - 1; k >= 0; k--) if (colBuf[k] != null && colBuf[k].Passes <= pass) colBuf.RemoveAt(k);   // v5: resting this pass
        }
        // release satellites whose island is gone from the lit column
        foreach (var s in sats) if (s.island != null && !colBuf.Contains(s.island)) s.leaving = true;
        foreach (var isl in colBuf)
        {
            if (isl == null) continue;
            Sat s = null;
            foreach (var o in sats) if (o.island == isl && !o.leaving) { s = o; break; }
            if (s == null)
            {
                foreach (var o in sats) if (o.island == null) { s = o; break; }
                if (s == null) { s = MakeSat(sats.Count); sats.Add(s); }
                s.island = isl; s.shown = 0f; s.leaving = false;
                s.root.gameObject.SetActive(true);
                if (s.trail != null) s.trail.Clear();
                Color c = isl.chordColor;
                s.coreMat.SetColor(BaseColorId, c); s.coreMat.SetColor(EmissionId, c * CoreEmission);
                s.glowMat.SetColor(ColorId, new Color(c.r, c.g, c.b, 0.8f));
                s.trailMat.SetColor(ColorId, new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 0.9f));
                Fx.Burst(isl.LapPoint(lap, 0.35f), Color.Lerp(c, Color.white, 0.4f), 5, 1.2f);
            }
        }
        var cam = Camera.main;
        foreach (var s in sats)
        {
            if (s.island == null) continue;
            if (s.leaving)
            {
                s.shown = Mathf.MoveTowards(s.shown, 0f, dt / 0.12f);
                if (s.shown <= 0f || s.island == null) { s.island = null; s.leaving = false; s.root.gameObject.SetActive(false); continue; }
            }
            else s.shown = Mathf.MoveTowards(s.shown, 1f, dt / 0.18f);
            Vector3 p = s.leaving ? s.lastPos : s.island.LapPoint(lap, 0.35f);
            s.lastPos = p;
            s.root.position = p;
            float k = ProjectConfig.SatelliteScale * (s.leaving ? s.shown : Ease.OutBack(s.shown));
            s.core.localScale = Vector3.one * (0.44f * k);
            s.glow.localScale = new Vector3(1.1f * k, 1.1f * k, 1f);
            if (s.trail != null) { s.trail.widthMultiplier = k / ProjectConfig.SatelliteScale; s.trail.emitting = !s.leaving; }
            if (cam != null)
            {
                Vector3 d = s.glow.position - cam.transform.position;
                if (d.sqrMagnitude > 1e-4f) s.glow.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                s.glow.position = p - s.glow.up * (0.55f * k);
            }
        }
    }

    void Arrive(int index, KeyBlock kb, SongManager sm, int col)
    {
        kb.Pulse(1f);
        Fx.SeaRipple(kb.Center, kb.chordColor);
        Fx.BeatBump();
        // v4: the whole column lights up with it
        colBuf.Clear();
        if (sm != null) sm.ColumnIslands(col, colBuf);
        for (int k = 1; k < colBuf.Count; k++) { var o = colBuf[k]; if (o == null) continue; o.Pulse(0.85f); Fx.SeaRipple(o.Center, o.chordColor, 2.8f); }
        OnArrive?.Invoke(index);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || glow == null) return;
        Vector3 d = glow.position - cam.transform.position;
        if (d.sqrMagnitude > 1e-4f) glow.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        glow.position = transform.position - glow.up * 0.55f;
    }

    void OnDestroy()
    {
        foreach (var s in sats)
        {
            if (s.coreMat != null) Destroy(s.coreMat);
            if (s.glowMat != null) Destroy(s.glowMat);
            if (s.trailMat != null) Destroy(s.trailMat);
            if (s.root != null) Destroy(s.root.gameObject);
        }
        sats.Clear();
    }
}
