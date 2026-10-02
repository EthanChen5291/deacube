using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Route (SPEC §2.1, §2.3): the closed cable through every island's hub in SongManager.Islands order. One
/// LineRenderer per consecutive pair (24 samples, quadratic sag to ProjectConfig.CableSag at the midpoint, from hub A's
/// underside to hub B's underside); the last island links back to the first as a dim return link. Dashes march one per
/// beat (_Speed = beats per second) and freeze when the clock is not running. Also the drop-onto-wire gap preview and
/// the light pulse that runs the whole route after a reorder. One per song; SongManager rebuilds it after every build.
/// v3 (SPEC v3 §3.3): no cable between two members of a merged group (a seam): the comet crosses it with a low glide
/// (PositionAlong returns that path); the merge light sheet and the seam scissors glyph live here too.
/// F10: the cable is alpha-blended (not additive) and runs from hub A's chord colour to hub B's, saturated, at intensity ≤ 0.7:
/// the additive lavender-white cable read as a white blob with colour fringes around the comet.
/// v4 (SPEC v4 §3 K3): the cable joins consecutive COLUMN anchors (only anchors wear a hub): segment c runs from column c's anchor to
/// column c + 1's (the last one back to column 0); a segment is a seam (no cable, the comet's low glide) when the two anchors are merged.
/// v5 (SPEC v5 §2.5): the cable follows the anchors where they are shown (KeyBlock.VisualOffset: the slide plus the belt ride), refitting while
/// an anchor rides its belt.
/// </summary>
public class Route : MonoBehaviour
{
    public const int Samples = 24;
    const float PulseSeconds = 1.25f;

    class Seg
    {
        public LineRenderer lr; public Material mat; public Vector3[] pts = new Vector3[Samples + 1]; public Vector3 a, b; public bool seam;
        public Gradient grad; public readonly GradientColorKey[] gradKeys = new GradientColorKey[2];
        public readonly GradientAlphaKey[] alphaKeys = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
    }
    readonly List<Seg> segs = new List<Seg>();
    /// <summary>Cable segments (v4: = ColumnCount when there are at least two columns, else 0; segment c starts at column c's anchor).</summary>
    public int SegmentCount => segs.Count;
    public int GapSegment => gapSeg;

    LineRenderer gapA, gapB; Material gapMat; Transform ghost; Renderer ghostRend; Material ghostMat; MaterialPropertyBlock ghostMpb; int gapSeg = -1; Vector3 gapAt; float gapAge;
    Transform pulseT; Renderer pulseRend; Material pulseMat; MaterialPropertyBlock pulseMpb; float pulseAge = 9f;
    bool paused; float speed = -1f;
    readonly Vector3[] tmp = new Vector3[Samples + 1];
    // v3: merge light sheet + seam scissors
    Transform sheet; Renderer sheetRend; Material sheetMat; float sheetAge; bool sheetOn;
    Transform scissors; Renderer scissorsRend; Material scissorsMat; float scissorsHot, scissorsShown; bool scissorsOn; Vector3 scissorsAt; int scissorsFrame;

    static readonly int SpeedId = Shader.PropertyToID("_Speed");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly Color Amber = new Color(1f, 0.72f, 0.28f);
    static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend"), DstBlendId = Shader.PropertyToID("_DstBlend");
    /// <summary>F10: the cable's brightness (the return link is dimmer).</summary>
    public const float CableIntensity = 0.7f, ReturnIntensity = 0.5f;

    static SongManager SM => SongManager.I;

    // ------------------------------------------------------------------ geometry
    /// <summary>The cable curve between two hub undersides: straight in x/z, quadratic sag to CableSag at the midpoint.</summary>
    public static Vector3 Curve(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 p = Vector3.Lerp(a, b, t);
        float mid = (a.y + b.y) * 0.5f;
        p.y += 4f * t * (1f - t) * (ProjectConfig.CableSag - mid);
        return p;
    }

    /// <summary>
    /// The comet's low glide across a seam (SPEC v3 §3.3): from A's lap exit point (its last beat starts) around the seam's front end onto
    /// B's lap start (front centre), SeamGlideHeight above the platforms with a small hop: never the high arc, never through a tile.
    /// </summary>
    public static Vector3 GlidePoint(KeyBlock A, KeyBlock B, float t)
    {
        t = Mathf.Clamp01(t);
        float h = ProjectConfig.SeamGlideHeight;
        float L = Mathf.Max(1f, A.LengthBeats);
        Vector3 p0 = A.LapPoint((L - 1f) / L, h);
        Vector3 p2 = B.LapPoint(0f, h);
        Vector3 ac = A.VisualCenter;
        Vector3 p1 = new Vector3(ac.x + A.Width * 0.5f, (p0.y + p2.y) * 0.5f, ac.z - A.Depth * 0.5f + ProjectConfig.LapInset);
        float u = 1f - t;
        Vector3 p = u * u * p0 + 2f * u * t * p1 + t * t * p2;
        p.y += 0.12f * Mathf.Sin(t * Mathf.PI);
        return p;
    }

    /// <summary>Recreates every segment from SongManager's column anchors (call after every build / order change / relayout).</summary>
    public void Rebuild()
    {
        int n = SM != null ? SM.ColumnCount : 0;
        int want = n >= 2 ? n : 0;
        HideGap();
        while (segs.Count > want) { var s = segs[segs.Count - 1]; if (s.lr != null) Destroy(s.lr.gameObject); if (s.mat != null) Destroy(s.mat); segs.RemoveAt(segs.Count - 1); }
        while (segs.Count < want) segs.Add(MakeSeg(segs.Count));
        for (int i = 0; i < want; i++) FitSegment(i);
        speed = -1f;
        ApplySpeed();
    }

    /// <summary>v3: group ids changed without a rebuild (tear-out, split): re-decide which segments are seams.</summary>
    public void RefreshSeams() { for (int i = 0; i < segs.Count; i++) FitSegment(i); }

    Seg MakeSeg(int i)
    {
        var go = new GameObject("Cable_" + i);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.positionCount = Samples + 1; lr.widthMultiplier = 0.09f;
        lr.textureMode = LineTextureMode.Tile; lr.alignment = LineAlignment.View; lr.numCapVertices = 2; lr.numCornerVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.lightProbeUsage = LightProbeUsage.Off;
        var mat = Fx.Flow(Color.white, CableIntensity, 0.7f, 1.6f, 0.16f);
        mat.SetFloat(SrcBlendId, (float)BlendMode.One);                     // F10: alpha-blended (the Flow output is premultiplied)
        mat.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        lr.material = mat;
        return new Seg { lr = lr, mat = mat };
    }

    /// <summary>v9 (W): a cable whose end is this far under its place (KeyBlock.SeaDrop, world units) is not drawn.</summary>
    public const float SunkDrop = 0.3f;
    readonly List<float> seaShown = new List<float>();
    /// <summary>v9: true when island <paramref name="i"/>'s sea drop changed since its cables were last fitted (it sinks / rises: refit them).</summary>
    bool SeaMoved(int i, KeyBlock kb)
    {
        while (seaShown.Count <= i) seaShown.Add(0f);
        float d = kb.SeaDrop;
        if (Mathf.Abs(d - seaShown[i]) < 1e-4f) return false;
        seaShown[i] = d;
        return true;
    }

    /// <summary>F10: a chord colour that reads as colour on the cable (saturation ≥ 0.6, value ≥ 0.8; greys stay grey).</summary>
    public static Color CableColor(Color c)
    {
        float h, s, v;
        Color.RGBToHSV(c, out h, out s, out v);
        if (s < 0.05f) return new Color(c.r, c.g, c.b, 1f);
        return Color.HSVToRGB(h, Mathf.Max(s, 0.6f), Mathf.Max(v, 0.8f));
    }

    void FitSegment(int i)
    {
        if (SM == null) return;
        int n = SM.ColumnCount;
        if (i < 0 || i >= segs.Count || i >= n) return;
        var A = SM.AnchorOf(i); var B = SM.AnchorOf((i + 1) % n);
        if (A == null || B == null) return;
        var s = segs[i];
        s.seam = SM.AnchorSeam(i);
        float da = A.SeaDrop, db = B.SeaDrop;   // v9 (W): an anchor under the sea (the present mode, a keyboard between its parts) takes its cable down with it
        s.a = A.HubUnderside + A.VisualOffset + Vector3.down * da; s.b = B.HubUnderside + B.VisualOffset + Vector3.down * db;
        for (int k = 0; k <= Samples; k++) s.pts[k] = Curve(s.a, s.b, k / (float)Samples);
        s.lr.positionCount = Samples + 1;
        s.lr.SetPositions(s.pts);
        s.lr.enabled = !s.seam && i != gapSeg && da < SunkDrop && db < SunkDrop;   // v9: no cable to an island under the sea
        bool ret = i == n - 1;
        // F10: hub A's chord colour flows into hub B's (vertex colours), alpha-blended, never washed toward white
        Color ca = CableColor(A.chordColor), cb = CableColor(B.chordColor);
        if (s.grad == null) s.grad = new Gradient();
        s.gradKeys[0] = new GradientColorKey(ca, 0f); s.gradKeys[1] = new GradientColorKey(cb, 1f);
        s.grad.SetKeys(s.gradKeys, s.alphaKeys);
        s.lr.colorGradient = s.grad;
        s.mat.SetColor(ColorId, new Color(1f, 1f, 1f, ret ? 0.35f : 1f));
        s.mat.SetFloat(IntensityId, ret ? ReturnIntensity : CableIntensity);
    }

    /// <summary>Refits the two segments touching island <paramref name="islandIndex"/> when it is a column's anchor (drag frames; other islands carry no hub).</summary>
    public void Refit(int islandIndex)
    {
        int n = segs.Count; if (n == 0 || SM == null) return;
        if (islandIndex < 0 || islandIndex >= SM.Islands.Count) return;
        var kb = SM.Islands[islandIndex];
        if (kb == null || !kb.IsAnchor) return;
        int c = kb.column;
        FitSegment(((c - 1) % n + n) % n);
        FitSegment(((c % n) + n) % n);
        if (gapSeg >= 0) LayoutGap();
    }

    /// <summary>True when segment <paramref name="i"/> joins two members of one group (no cable is drawn there).</summary>
    public bool IsSeamSegment(int i) => i >= 0 && i < segs.Count && segs[i].seam;
    /// <summary>True when segment <paramref name="i"/>'s cable is drawn (tests).</summary>
    public bool CableVisible(int i) => i >= 0 && i < segs.Count && segs[i].lr != null && segs[i].lr.enabled;

    /// <summary>Point on the cable from column <paramref name="fromIndex"/>'s anchor hub to the next column's at <paramref name="t01"/> (0 = hub A underside,
    /// 1 = hub B underside); across a seam: the comet's low glide (GlidePoint).</summary>
    public Vector3 PositionAlong(int fromIndex, float t01)
    {
        int n = segs.Count;
        if (n == 0)
        {
            var a0 = SM != null ? SM.AnchorOf(0) : null;
            return a0 != null ? a0.HubUnderside : Vector3.zero;
        }
        int i = ((fromIndex % n) + n) % n;
        var s = segs[i];
        if (s.seam && SM != null)
        {
            var A = SM.AnchorOf(i); var B = SM.AnchorOf(i + 1);
            if (A != null && B != null) return GlidePoint(A, B, t01);
        }
        return Curve(s.a, s.b, t01);
    }

    /// <summary>Nearest drawn cable segment to <paramref name="p"/> measured in the sea plane (x/z); seams have no cable. False when there is none.</summary>
    public bool NearestSegment(Vector3 p, out int seg, out float dist, out Vector3 closest) { return NearestSegment(p, -1, out seg, out dist, out closest); }

    /// <summary>Same, ignoring the two segments that touch column <paramref name="skipColumn"/>'s anchor (a dragged hub drags its own cables along:
    /// they are always the nearest and never a place to drop it).</summary>
    public bool NearestSegment(Vector3 p, int skipColumn, out int seg, out float dist, out Vector3 closest)
    {
        seg = -1; dist = float.MaxValue; closest = p;
        int n = segs.Count;
        for (int i = 0; i < n; i++)
        {
            if (segs[i].seam) continue;
            if (skipColumn >= 0 && (i == skipColumn || (i + 1) % n == skipColumn)) continue;
            var pts = segs[i].pts;
            for (int k = 0; k < Samples; k++)
            {
                Vector3 c = ClosestOnSegmentXZ(pts[k], pts[k + 1], p);
                float dx = c.x - p.x, dz = c.z - p.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < dist) { dist = d; seg = i; closest = c; }
            }
        }
        return seg >= 0;
    }

    static Vector3 ClosestOnSegmentXZ(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), p2 = new Vector2(p.x, p.z);
        Vector2 ab = b2 - a2;
        float len2 = ab.sqrMagnitude;
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / len2);
        return Vector3.Lerp(a, b, t);
    }

    // ------------------------------------------------------------------ drop-onto-wire gap (SPEC §2.3)
    /// <summary>Opens segment <paramref name="seg"/> into A → ghost hub at <paramref name="at"/> → B, amber and bright.</summary>
    public void ShowGap(int seg, Vector3 at)
    {
        if (seg < 0 || seg >= segs.Count || segs[seg].seam) { HideGap(); return; }
        if (gapSeg != seg) { if (gapSeg >= 0 && gapSeg < segs.Count) segs[gapSeg].lr.enabled = !segs[gapSeg].seam; gapAge = 0f; }
        gapSeg = seg; gapAt = at;
        EnsureGapObjects();
        segs[seg].lr.enabled = false;
        gapA.enabled = true; gapB.enabled = true; ghost.gameObject.SetActive(true);
        LayoutGap();
    }

    public void HideGap()
    {
        if (gapSeg >= 0 && gapSeg < segs.Count && segs[gapSeg].lr != null) segs[gapSeg].lr.enabled = !segs[gapSeg].seam;
        gapSeg = -1;
        if (gapA != null) gapA.enabled = false;
        if (gapB != null) gapB.enabled = false;
        if (ghost != null) ghost.gameObject.SetActive(false);
    }

    void EnsureGapObjects()
    {
        if (gapA != null) return;
        gapMat = Fx.Flow(Amber, 2.2f, 0.9f, 2.4f, 0.3f);
        gapA = MakeGapLine("GapA"); gapB = MakeGapLine("GapB");
        var g = new GameObject("GhostHub");
        g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Ring(0.55f, 1f, 48);
        ghostRend = g.AddComponent<MeshRenderer>();
        ghostMat = Fx.Additive(IconFactory.GetTexture("ringBand"), Amber, 1.6f);
        ghostRend.sharedMaterial = ghostMat;
        ghostRend.shadowCastingMode = ShadowCastingMode.Off; ghostRend.receiveShadows = false;
        ghostMpb = new MaterialPropertyBlock();
        ghost = g.transform;
        ghost.gameObject.SetActive(false);
    }

    LineRenderer MakeGapLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.positionCount = Samples + 1; lr.widthMultiplier = 0.12f;
        lr.textureMode = LineTextureMode.Tile; lr.alignment = LineAlignment.View; lr.numCapVertices = 2;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.material = gapMat;
        lr.enabled = false;
        return lr;
    }

    void LayoutGap()
    {
        if (gapSeg < 0 || gapSeg >= segs.Count || gapA == null) return;
        var s = segs[gapSeg];
        Vector3 g = gapAt;
        for (int k = 0; k <= Samples; k++) tmp[k] = Curve(s.a, g, k / (float)Samples);
        gapA.SetPositions(tmp);
        for (int k = 0; k <= Samples; k++) tmp[k] = Curve(g, s.b, k / (float)Samples);
        gapB.SetPositions(tmp);
        ghost.position = g + Vector3.down * 0.02f;
        ghost.localScale = Vector3.one * 1.1f;
    }

    // ------------------------------------------------------------------ v3 merge light sheet (SPEC v3 §3.3)
    /// <summary>A vertical light sheet standing on a target edge: bottom front end at <paramref name="edgeFront"/>, running <paramref name="length"/> toward +z.</summary>
    public void ShowMergeSheet(Vector3 edgeFront, float length, Color c)
    {
        if (sheet == null)
        {
            var go = new GameObject("MergeSheet");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            sheetRend = go.AddComponent<MeshRenderer>();
            sheetMat = Fx.Additive(IconFactory.GetTexture("pillar"), Color.white, 2.2f);
            sheetRend.sharedMaterial = sheetMat;
            sheetRend.shadowCastingMode = ShadowCastingMode.Off; sheetRend.receiveShadows = false;
            sheet = go.transform;
        }
        if (!sheetOn) sheetAge = 0f;
        sheetOn = true;
        sheet.gameObject.SetActive(true);
        sheet.position = new Vector3(edgeFront.x, edgeFront.y - 0.1f, edgeFront.z + length * 0.5f);
        sheet.rotation = Quaternion.Euler(0f, -90f, 0f);   // the quad's +x runs along +z
        sheet.localScale = new Vector3(length + 0.6f, 2.8f, 1f);
        sheetMat.SetColor(ColorId, Color.Lerp(c, Color.white, 0.5f));
    }

    public void HideMergeSheet() { sheetOn = false; if (sheet != null) sheet.gameObject.SetActive(false); }
    public bool MergeSheetVisible => sheetOn;

    // ------------------------------------------------------------------ v3 seam scissors (SPEC v3 §3.3)
    /// <summary>The scissors glyph at a seam's front end (<paramref name="hot"/> = the pointer is on it: bigger, brighter).</summary>
    public void ShowScissors(Vector3 at, bool hot)
    {
        if (scissors == null)
        {
            var go = new GameObject("SeamScissors");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            scissorsRend = go.AddComponent<MeshRenderer>();
            scissorsMat = Fx.Alpha(IconFactory.GetTexture("scissors"), Color.white);
            scissorsRend.sharedMaterial = scissorsMat;
            scissorsRend.shadowCastingMode = ShadowCastingMode.Off; scissorsRend.receiveShadows = false;
            scissors = go.transform;
        }
        if (!scissorsOn) scissorsShown = 0f;
        scissorsOn = true; scissorsAt = at; scissorsFrame = Time.frameCount;
        scissorsHot = hot ? 1f : 0f;
        scissors.gameObject.SetActive(true);
    }

    public void HideScissors() { scissorsOn = false; if (scissors != null) scissors.gameObject.SetActive(false); }
    public bool ScissorsVisible => scissorsOn;

    // ------------------------------------------------------------------ pulse + dash speed
    /// <summary>A bright dot travels the whole Route once (after a reorder).</summary>
    public void RunPulse()
    {
        if (segs.Count == 0) return;
        if (pulseT == null)
        {
            var p = new GameObject("RoutePulse");
            p.transform.SetParent(transform, false);
            p.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            pulseRend = p.AddComponent<MeshRenderer>();
            pulseMat = Fx.Additive(IconFactory.GetTexture("glow"), new Color(0.95f, 0.9f, 1f), 2.2f);
            pulseRend.sharedMaterial = pulseMat;
            pulseRend.shadowCastingMode = ShadowCastingMode.Off; pulseRend.receiveShadows = false;
            pulseMpb = new MaterialPropertyBlock();
            pulseT = p.transform;
            pulseT.localScale = new Vector3(0.7f, 0.7f, 1f);
        }
        pulseAge = 0f;
        pulseT.gameObject.SetActive(true);
    }

    /// <summary>Freezes (true) or runs (false) the cable dashes; one dash per beat while running.</summary>
    public void SetPaused(bool p) { paused = p; ApplySpeed(); }

    void ApplySpeed()
    {
        float want = paused ? 0f : (float)GlobalClock.BeatsPerSecond;
        if (Mathf.Approximately(want, speed)) return;
        speed = want;
        for (int i = 0; i < segs.Count; i++) if (segs[i].mat != null) segs[i].mat.SetFloat(SpeedId, want);
        if (gapMat != null) gapMat.SetFloat(SpeedId, want * 1.5f);
    }

    void OnDestroy()
    {
        foreach (var s in segs) if (s.mat != null) Destroy(s.mat);
        if (gapMat != null) Destroy(gapMat);
        if (ghostMat != null) Destroy(ghostMat);
        if (pulseMat != null) Destroy(pulseMat);
        if (sheetMat != null) Destroy(sheetMat);
        if (scissorsMat != null) Destroy(scissorsMat);
    }

    void Update()
    {
        SetPaused(!GlobalClock.IsPlaying);
        float dt = Time.deltaTime;
        // anchors easing into place (merge snap-in, split, a relayout), rising / sinking to a new register or riding their belt (v5): their
        // cables follow the visual hub
        var sm = SM;
        if (sm != null && segs.Count > 0) for (int i = 0; i < sm.Islands.Count; i++) { var kb = sm.Islands[i]; if (kb != null && kb.IsAnchor && (kb.Sliding || kb.Lifting || kb.Riding || kb.Shifting || kb.FxLifting || SeaMoved(i, kb))) Refit(i); }   // v7: + W's visual lift (the ground glide reports Lifting); v9: + the sea
        if (gapSeg >= 0 && ghostRend != null)
        {
            gapAge += dt;
            ghostMpb.SetFloat(IntensityId, 1.2f + 0.6f * Mathf.Sin(gapAge * 9f));
            ghostRend.SetPropertyBlock(ghostMpb);
        }
        var cam = Camera.main;
        if (sheetOn && sheet != null)
        {
            sheetAge += dt;
            sheetMat.SetFloat(IntensityId, (2.2f + 0.8f * Mathf.Sin(sheetAge * 10f)) * Mathf.Clamp01(sheetAge / 0.12f));
        }
        if (scissorsOn && Time.frameCount - scissorsFrame > 2) HideScissors();   // its owner (the idle island drag) stopped refreshing it: a lock, a drag, a draw
        if (scissorsOn && scissors != null)
        {
            scissorsShown = Mathf.MoveTowards(scissorsShown, 1f, dt / 0.15f);
            float s = (0.8f + 0.25f * scissorsHot) * Ease.OutBack(scissorsShown);
            scissors.localScale = new Vector3(s, s, 1f);
            scissors.position = scissorsAt;
            if (cam != null) scissors.rotation = Quaternion.LookRotation(scissors.position - cam.transform.position, Vector3.up);
            scissorsMat.SetColor(ColorId, Palette.A(Color.Lerp(new Color(0.85f, 0.84f, 0.95f), Color.white, scissorsHot), 0.75f + 0.25f * scissorsHot));
        }
        if (pulseT != null && pulseAge < 1f)
        {
            pulseAge += dt / PulseSeconds;
            if (pulseAge >= 1f || segs.Count == 0) { pulseT.gameObject.SetActive(false); pulseAge = 9f; return; }
            float u = pulseAge * segs.Count;
            int si = Mathf.Min(segs.Count - 1, Mathf.FloorToInt(u));
            Vector3 p = PositionAlong(si, u - si) + Vector3.up * 0.05f;
            pulseT.position = p - Vector3.up * 0.35f;
            if (cam != null) pulseT.rotation = Quaternion.LookRotation(pulseT.position - cam.transform.position, Vector3.up);
            pulseMpb.SetFloat(IntensityId, 2.2f * Mathf.Sin(pulseAge * Mathf.PI) + 0.6f);
            pulseRend.SetPropertyBlock(pulseMpb);
        }
    }
}
