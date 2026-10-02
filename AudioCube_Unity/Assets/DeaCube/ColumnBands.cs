using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPEC v4 §2.5 / §3 K3: the soft bands on the sea under each column. A band runs from the column's front-most island's front edge − BandPadZ to
/// its back-most island's back edge + BandPadZ and is IslandWidth + 2·BandPadX wide (glued columns meet without a pad between them); it is a
/// feathered rounded plate in a pale sea tint. The lit column's band glows faintly in its anchor's chord colour (breathing on the beat); while
/// an island is dragged in its column, that column's band stretches over the allowed range and brightens (ShowRange / ClearRange).
/// SongManager calls Refresh after every layout change (OnColumnsChanged); the bands follow the islands' live positions each frame.
/// v5 (SPEC v5 §2.6): a column's band covers its conveyor belts; while an island is dragged freely the band of the column it would JOIN lights up
/// (ShowRange over the joining island's z), a new column's slot shows an insertion SLIT (a bright pulsing line on the sea between the parted
/// columns) and the dragged island casts a soft shadow on the sea.
/// v6 §11.3: a faint dotted TRACK on the sea marks each Moon section (the percussion part): from the start column's Moon (in its drum lane) to the
/// next section's start or the song's end, a bigger dot where it begins — so the player sees where each drum part begins and ends.
/// v7: columns that TOUCH (one section, or a merge group) meet without a pad; a band spans its column's measures and belts (a rewind island has
/// none); phrases (melody tracks behind the lanes, spanning several columns) never stretch a column's band.
/// </summary>
public static class ColumnBands
{
    /// <summary>Rebuilds the bands from SongManager's columns (count, sizes, positions).</summary>
    public static void Refresh() { var v = ColumnBandsView.Ensure(); if (v != null) v.Rebuild(); }
    /// <summary>While an island of column <paramref name="col"/> is dragged: its band covers z ∈ [<paramref name="zMin"/>, <paramref name="zMax"/>] (world, edges).</summary>
    public static void ShowRange(int col, float zMin, float zMax) { var v = ColumnBandsView.Ensure(); if (v != null) v.SetRange(col, zMin, zMax); }
    public static void ClearRange() { if (ColumnBandsView.I != null) ColumnBandsView.I.SetRange(-1, 0f, 0f); }
    /// <summary>Bands currently shown (tests).</summary>
    public static int Count => ColumnBandsView.I != null ? ColumnBandsView.I.Count : 0;
    /// <summary>The world rectangle of column <paramref name="col"/>'s band (centre, size; y = the sea) — tests.</summary>
    public static Bounds BandBounds(int col) => ColumnBandsView.I != null ? ColumnBandsView.I.BoundsOf(col) : new Bounds();
    /// <summary>The column whose band is stretched over a drag range (-1 none).</summary>
    public static int RangeColumn => ColumnBandsView.I != null ? ColumnBandsView.I.RangeCol : -1;
    /// <summary>The glow (0..1) of column <paramref name="col"/>'s band: 1 = the lit column (tests).</summary>
    public static float Glow(int col) => ColumnBandsView.I != null ? ColumnBandsView.I.GlowOf(col) : 0f;
    /// <summary>v5: the free drag's insertion slit at world x <paramref name="x"/> over z ∈ [<paramref name="zMin"/>, <paramref name="zMax"/>] (a new column goes there).</summary>
    public static void ShowSlit(float x, float zMin, float zMax) { var v = ColumnBandsView.Ensure(); if (v != null) v.SetSlit(true, x, zMin, zMax); }
    public static void HideSlit() { if (ColumnBandsView.I != null) ColumnBandsView.I.SetSlit(false, 0f, 0f, 0f); }
    /// <summary>v5: true while the insertion slit shows; its x (tests).</summary>
    public static bool SlitShown => ColumnBandsView.I != null && ColumnBandsView.I.SlitOn;
    public static float SlitX => ColumnBandsView.I != null ? ColumnBandsView.I.SlitAt : 0f;
    /// <summary>v5: the dragged island's soft shadow on the sea under the world rectangle <paramref name="r"/> (x, z).</summary>
    public static void ShowShadow(Rect r) { var v = ColumnBandsView.Ensure(); if (v != null) v.SetShadow(true, r); }
    public static void HideShadow() { if (ColumnBandsView.I != null) ColumnBandsView.I.SetShadow(false, new Rect()); }
    public static bool ShadowShown => ColumnBandsView.I != null && ColumnBandsView.I.ShadowOn;
    /// <summary>v5: a free drag began: a column whose island is lifted (dragged away) keeps its band where it was until the drop.</summary>
    public static void PinColumns() { if (ColumnBandsView.I != null) ColumnBandsView.I.Pin(true); }
    public static void UnpinColumns() { if (ColumnBandsView.I != null) ColumnBandsView.I.Pin(false); }
    /// <summary>v6 §11.3: the Moon section tracks drawn (tests): how many, and each one's world span (x from / to, z of its lane).</summary>
    public static int MoonTrackCount => ColumnBandsView.I != null ? ColumnBandsView.I.TrackCount : 0;
    public static bool MoonTrackSpan(int i, out float x0, out float x1, out float z)
    {
        x0 = x1 = z = 0f;
        return ColumnBandsView.I != null && ColumnBandsView.I.TrackSpan(i, out x0, out x1, out z);
    }
}

/// <summary>The scene object behind <see cref="ColumnBands"/>: one feathered plate per column, pooled.</summary>
public class ColumnBandsView : MonoBehaviour
{
    public static ColumnBandsView I;
    const float SeaY = -3.96f;           // just above the sea plane (Fx: y = -4)
    const float Feather = 0.9f, Corner = 1.1f;
    static readonly Color Pale = new Color(0.86f, 0.82f, 1f);
    static readonly int ColorId = Shader.PropertyToID("_Color");

    class Band { public Transform t; public MeshFilter mf; public Material mat; public Mesh mesh; public float w, d; public float glow; public Rect rect; }
    readonly List<Band> bands = new List<Band>();
    readonly List<KeyBlock> buf = new List<KeyBlock>(ProjectConfig.MaxLanes);
    int rangeCol = -1; float rangeMin, rangeMax;
    int count;
    // v5: the insertion slit and the drag shadow
    Transform slit, shadow, slitSheet; Material slitMat, shadowMat, sheetMat; Mesh slitMesh, shadowMesh; float slitLen = -1f, shadowW = -1f, shadowD = -1f, slitAge;
    static readonly Color Amber = new Color(1f, 0.72f, 0.28f);
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    bool slitOn, shadowOn; float slitX, slitZ0, slitZ1; Rect shadowRect;
    public bool SlitOn => slitOn;
    public float SlitAt => slitX;
    public bool ShadowOn => shadowOn;

    public int Count => count;
    public int RangeCol => rangeCol;
    // v6 §11.3: the Moon section tracks (one mesh of dots)
    Transform tracks; Mesh trackMesh; Material trackMat;
    readonly List<Vector3> trackSpans = new List<Vector3>();   // x0, x1, z per section
    public int TrackCount => trackSpans.Count;
    public bool TrackSpan(int i, out float x0, out float x1, out float z)
    {
        x0 = x1 = z = 0f;
        if (i < 0 || i >= trackSpans.Count) return false;
        x0 = trackSpans[i].x; x1 = trackSpans[i].y; z = trackSpans[i].z;
        return true;
    }

    public static ColumnBandsView Ensure()
    {
        if (I != null) return I;
        if (!Application.isPlaying) return null;
        I = FindAnyObjectByType<ColumnBandsView>();
        if (I == null) I = new GameObject("ColumnBands").AddComponent<ColumnBandsView>();
        return I;
    }

    void Awake() { I = this; }

    public void SetRange(int col, float zMin, float zMax) { rangeCol = col; rangeMin = zMin; rangeMax = zMax; }

    readonly List<Rect> pinned = new List<Rect>(); bool pinOn;
    public void Pin(bool on)
    {
        pinOn = on; pinned.Clear();
        if (on) for (int k = 0; k < count && k < bands.Count; k++) pinned.Add(bands[k].rect);
    }

    public void SetSlit(bool on, float x, float zMin, float zMax)
    {
        if (on && !slitOn) slitAge = 0f;
        slitOn = on; slitX = x; slitZ0 = zMin; slitZ1 = zMax;
        if (on && slit == null)
        {
            var go = new GameObject("InsertSlit");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            slitMat = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Color.white, 0.6f));
            mr.sharedMaterial = slitMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            slit = go.transform;
            // and an amber light sheet standing in the gap at the islands' level (the tray's INSERT colour): "a new column goes here"
            var sh = new GameObject("InsertSheet");
            sh.transform.SetParent(transform, false);
            sh.AddComponent<MeshFilter>().sharedMesh = MeshFactory.VerticalQuad();
            var sr = sh.AddComponent<MeshRenderer>();
            sheetMat = Fx.Additive(IconFactory.GetTexture("pillar"), Amber, 1.4f);
            sr.sharedMaterial = sheetMat;
            sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false; sr.lightProbeUsage = LightProbeUsage.Off;
            slitSheet = sh.transform;
        }
        if (slit != null && slit.gameObject.activeSelf != on) slit.gameObject.SetActive(on);
        if (slitSheet != null && slitSheet.gameObject.activeSelf != on) slitSheet.gameObject.SetActive(on);
    }

    public void SetShadow(bool on, Rect r)
    {
        shadowOn = on; shadowRect = r;
        if (on && shadow == null)
        {
            var go = new GameObject("DragShadow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            shadowMat = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Look.SeaShadow, 0.42f));
            mr.sharedMaterial = shadowMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            shadow = go.transform;
        }
        if (shadow != null && shadow.gameObject.activeSelf != on) shadow.gameObject.SetActive(on);
    }

    /// <summary>The slit and the shadow follow their rectangles (the plate meshes are rebuilt only when their size changes).</summary>
    void UpdateDragMarks(float dt)
    {
        if (slitOn && slit != null)
        {
            slitAge += dt;
            float len = Mathf.Max(0.5f, slitZ1 - slitZ0);
            if (slitMesh == null || Mathf.Abs(slitLen - len) > 0.05f)
            {
                if (slitMesh != null) Destroy(slitMesh);
                slitLen = len;
                slitMesh = MeshFactory.SoftPlate(0.34f, Mathf.Max(0.2f, len - 0.5f), 0.17f, 0.5f);
                slit.GetComponent<MeshFilter>().sharedMesh = slitMesh;
            }
            float grow = Ease.OutBack(Mathf.Clamp01(slitAge / 0.22f));
            slit.position = new Vector3(slitX, SeaY + 0.02f, (slitZ0 + slitZ1) * 0.5f);
            slit.localScale = new Vector3(1f, 1f, Mathf.Max(0.05f, grow));
            slitMat.SetColor(ColorId, Palette.A(Color.Lerp(new Color(1f, 0.86f, 0.55f), Color.white, 0.4f), 0.55f + 0.3f * Mathf.Sin(slitAge * 8f)));
            if (slitSheet != null)
            {
                slitSheet.position = new Vector3(slitX, -1.1f, (slitZ0 + slitZ1) * 0.5f);
                slitSheet.rotation = Quaternion.Euler(0f, -90f, 0f);   // the quad's +x runs along +z
                slitSheet.localScale = new Vector3(Mathf.Max(0.5f, len - 1.2f) * Mathf.Max(0.05f, grow), 2.6f, 1f);
                sheetMat.SetFloat(IntensityId, (1.2f + 0.5f * Mathf.Sin(slitAge * 8f)) * Mathf.Clamp01(slitAge / 0.12f));
            }
        }
        if (shadowOn && shadow != null)
        {
            float w = Mathf.Max(0.5f, shadowRect.width), d = Mathf.Max(0.5f, shadowRect.height);
            if (shadowMesh == null || Mathf.Abs(shadowW - w) > 0.05f || Mathf.Abs(shadowD - d) > 0.05f)
            {
                if (shadowMesh != null) Destroy(shadowMesh);
                shadowW = w; shadowD = d;
                shadowMesh = MeshFactory.SoftPlate(Mathf.Max(0.2f, w - 0.6f), Mathf.Max(0.2f, d - 0.6f), 0.8f, 1.2f);
                shadow.GetComponent<MeshFilter>().sharedMesh = shadowMesh;
            }
            shadow.position = new Vector3(shadowRect.center.x, SeaY + 0.01f, shadowRect.center.y);
        }
    }

    public Bounds BoundsOf(int col)
    {
        if (col < 0 || col >= count) return new Bounds();
        var r = bands[col].rect;
        return new Bounds(new Vector3(r.center.x, SeaY, r.center.y), new Vector3(r.width, 0.01f, r.height));
    }

    public float GlowOf(int col) => col >= 0 && col < count ? bands[col].glow : 0f;

    public void Rebuild() { count = -1; LateUpdate(); RebuildTracks(); }

    /// <summary>v6 §11.3: one dotted line per Moon section, on the sea along its Moons' drum lane: from the start column's platform centre to the next
    /// section's start (or the song's end), a bigger dot where it begins. Rebuilt with the bands (layout / Moon changes).</summary>
    void RebuildTracks()
    {
        var sm = SongManager.I;
        trackSpans.Clear();
        bool on = sm != null && sm.HasSong && sm.Moons.Count > 0 && sm.ColumnCount > 0;
        if (on)
        {
            var starts = new List<int>();
            for (int j = 0; j < sm.Moons.Count; j++) { int c = sm.MoonStartColumn(j); if (c >= 0 && !starts.Contains(c)) starts.Add(c); }
            starts.Sort();
            foreach (int c in starts)
            {
                float z = float.MinValue;
                for (int j = 0; j < sm.Moons.Count; j++) if (sm.Moons[j] != null && sm.MoonStartColumn(j) == c) z = Mathf.Max(z, sm.Moons[j].Center.z);
                int e = -1; foreach (int o in starts) if (o > c) { e = o; break; }
                int last = sm.ColumnCount - 1;
                float x0 = sm.ColumnCenterX(c), x1 = e >= 0 ? sm.ColumnCenterX(e) : sm.ColumnEastEdge(last);   // v7: column centres (several measures wide)
                trackSpans.Add(new Vector3(x0, x1, z));
            }
        }
        if (trackSpans.Count == 0) { if (tracks != null && tracks.gameObject.activeSelf) tracks.gameObject.SetActive(false); return; }
        if (tracks == null)
        {
            var go = new GameObject("MoonTracks");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            trackMat = Fx.Alpha(IconFactory.GetTexture(KeyBlock.KeyDotGlyph), Palette.A(Color.Lerp(KeyBlock.MoonColor, Color.white, 0.25f), 0.42f));
            mr.sharedMaterial = trackMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            tracks = go.transform;
        }
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        const float step = 0.95f, dot = 0.13f, head = 0.34f;
        foreach (var t in trackSpans)
        {
            float len = t.y - t.x;
            int n = Mathf.Max(1, Mathf.FloorToInt(len / step));
            for (int k = 0; k <= n; k++)
            {
                float x = t.x + len * k / n, r = k == 0 ? head : dot;
                int v = verts.Count;
                verts.Add(new Vector3(x - r, SeaY + 0.03f, t.z - r)); verts.Add(new Vector3(x + r, SeaY + 0.03f, t.z - r));
                verts.Add(new Vector3(x + r, SeaY + 0.03f, t.z + r)); verts.Add(new Vector3(x - r, SeaY + 0.03f, t.z + r));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1); tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
            }
        }
        if (trackMesh == null) { trackMesh = new Mesh { name = "moonTracks" }; tracks.GetComponent<MeshFilter>().sharedMesh = trackMesh; }
        trackMesh.Clear();
        trackMesh.SetVertices(verts); trackMesh.SetUVs(0, uvs); trackMesh.SetTriangles(tris, 0);
        trackMesh.RecalculateBounds();
    }

    Band Make(int k)
    {
        var go = new GameObject("Band_" + k);
        go.transform.SetParent(transform, false);
        var b = new Band { t = go.transform, mf = go.AddComponent<MeshFilter>() };
        var mr = go.AddComponent<MeshRenderer>();
        b.mat = Fx.Alpha(IconFactory.GetTexture("white"), Palette.A(Pale, 0.1f));
        mr.sharedMaterial = b.mat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        return b;
    }

    /// <summary>The band rectangle of column <paramref name="c"/> (x, z min / size) from the islands' live positions.</summary>
    Rect RectOf(SongManager sm, int c)
    {
        sm.ColumnIslands(c, buf);
        if (buf.Count == 0) return new Rect();
        if (pinOn && c < pinned.Count)
        {
            // a free drag lifted an island of this column away: the band stays where the column is (plus the range over the drop's lane)
            bool lifted = false; foreach (var kb in buf) if (kb != null && kb.Lifted) { lifted = true; break; }
            if (lifted)
            {
                var r0 = pinned[c];
                if (c == rangeCol) r0 = Rect.MinMaxRect(r0.xMin, Mathf.Min(r0.yMin, rangeMin - ProjectConfig.BandPadZ), r0.xMax, Mathf.Max(r0.yMax, rangeMax + ProjectConfig.BandPadZ));
                return r0;
            }
        }
        float front = float.MaxValue, back = float.MinValue, west = float.MaxValue, east = float.MinValue;
        foreach (var kb in buf)
        {
            if (kb == null || (kb.IsPhrase && buf.Count > 1)) continue;   // v7: a phrase lives in its melody track (it spans other columns)
            front = Mathf.Min(front, kb.FrontEdge); back = Mathf.Max(back, kb.BackEdge);
            west = Mathf.Min(west, kb.WestEdge); east = Mathf.Max(east, kb.EastEdge + (kb.HasBelt ? (kb.Passes - 1) * kb.BeltPitch : 0f));   // v5: the belts belong to the column
        }
        if (west == float.MaxValue) return new Rect();
        float padW = sm.Touching(c - 1) ? 0f : ProjectConfig.BandPadX, padE = sm.Touching(c) ? 0f : ProjectConfig.BandPadX;   // v7: touching columns meet
        if (c == rangeCol) { front = Mathf.Min(front, rangeMin); back = Mathf.Max(back, rangeMax); }
        return Rect.MinMaxRect(west - padW, front - ProjectConfig.BandPadZ, east + padE, back + ProjectConfig.BandPadZ);
    }

    void LateUpdate()
    {
        var sm = SongManager.I;
        UpdateDragMarks(Time.deltaTime);
        int want = sm != null && sm.HasSong && !Presenter.Active ? sm.ColumnCount : 0;
        if (tracks != null) { bool show = want > 0 && trackSpans.Count > 0; if (tracks.gameObject.activeSelf != show) tracks.gameObject.SetActive(show); }   // v6 §11.3
        while (bands.Count < want) bands.Add(Make(bands.Count));
        for (int k = 0; k < bands.Count; k++) { bool on = k < want; if (bands[k].t.gameObject.activeSelf != on) bands[k].t.gameObject.SetActive(on); }
        count = want;
        if (want == 0) return;
        bool playing = GlobalClock.IsPlaying;
        int lit = playing ? sm.LitColumn : -1;
        float beatPulse = playing ? 1f - (float)(GlobalClock.SongBeatD - System.Math.Floor(GlobalClock.SongBeatD)) : 0f;
        float dt = Time.deltaTime;
        for (int c = 0; c < want; c++)
        {
            var b = bands[c];
            var r = RectOf(sm, c);
            b.rect = r;
            // the plate mesh only changes when the band's size does (moves are the transform)
            if (b.mesh == null || Mathf.Abs(b.w - r.width) > 0.02f || Mathf.Abs(b.d - r.height) > 0.02f)
            {
                if (b.mesh != null) Destroy(b.mesh);
                b.w = r.width; b.d = r.height;
                b.mesh = MeshFactory.SoftPlate(Mathf.Max(0.2f, r.width - Feather), Mathf.Max(0.2f, r.height - Feather), Corner, Feather);
                b.mf.sharedMesh = b.mesh;
            }
            b.t.position = new Vector3(r.center.x, SeaY, r.center.y);
            float target = c == lit ? 1f : 0f;
            b.glow = Mathf.MoveTowards(b.glow, target, dt / (target > b.glow ? 0.12f : 0.35f));
            var anchor = sm.AnchorOf(c);
            Color chord = anchor != null ? anchor.chordColor : Pale;
            Color col = Color.Lerp(Pale, Color.Lerp(chord, Color.white, 0.35f), b.glow * 0.8f);
            float a = 0.08f + b.glow * (0.07f + 0.04f * beatPulse);   // the lit column glows faintly, breathing on the beat
            if (c == rangeCol) { col = Color.Lerp(col, Color.white, 0.5f); a = Mathf.Max(a, 0.24f); }
            b.mat.SetColor(ColorId, Palette.A(col, a));
        }
    }

    void OnDestroy()
    {
        if (slitMat != null) Destroy(slitMat); if (slitMesh != null) Destroy(slitMesh); if (sheetMat != null) Destroy(sheetMat);
        if (shadowMat != null) Destroy(shadowMat); if (shadowMesh != null) Destroy(shadowMesh);
        foreach (var b in bands) { if (b.mat != null) Destroy(b.mat); if (b.mesh != null) Destroy(b.mesh); }
        bands.Clear();
        if (trackMat != null) Destroy(trackMat); if (trackMesh != null) Destroy(trackMesh);
        if (I == this) I = null;
    }
}
