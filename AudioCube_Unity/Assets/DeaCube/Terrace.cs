using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v7 package W (SPEC v7 §6.2): the TERRACE of one column whose ground is not the sea's level — the heights made visible ("since there's the
/// addition of heights, grids should abide by the height … if there was a staircase up then grids spawn in line with the top of the staircase").
/// A stone mesa rises from the sea to the underside of what stands on the ground there: the column's block of its SECTION PLINTH (B: a low slab
/// 0.95 under the ground, stepping with the grounds), else — no plinth — its anchor grid's underside at its ground (the octave tower's lift and
/// W's own FxLift left out: a tower rises off its terrace, a selected grid lifts off it). Its footprint is the plinth block's (the column's span,
/// the section's lanes), so the terraces of neighbouring columns stand side by side and a section reads as one landscape: the song climbs and
/// falls. The walls are cut into drums one StairStepRise high whose top edges are bevelled in ink — the strata line up across the landscape, so
/// the eye counts the steps a column climbed — and lean out a little toward the foot; a scalloped foam ring laps the waterline. A raised ground
/// stands on warm sandstone, a lowered one on a low shelf of darker wet rock. It rises out of the sea when the ground leaves 0, sinks back when it
/// returns (<see cref="ShowSeconds"/>) and between those follows the plinth's / the island's glide every frame. Built (keyed by column),
/// re-bound and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class Terrace : MonoBehaviour
{
    public const float SeaY = WorldMagic.SeaY;
    /// <summary>The slab / platform above overhangs its terrace by this much on every side (its ink edge reads on top of the stone).</summary>
    public const float Inset = 0.14f;
    /// <summary>How far the mesa reaches down from its top (always past the sea, whatever the ground: GroundMax − SeaY + margin).</summary>
    public const float Depth = 10f;
    /// <summary>Seconds of the terrace rising out of the sea (a ground that left 0) and sinking back (a ground back at 0).</summary>
    public const float ShowSeconds = 0.45f;

    /// <summary>The column's anchor grid (its ground) and the column.</summary>
    public KeyBlock Island { get; private set; }
    public int Column { get; private set; }
    public int Index => Column;
    public bool Retiring { get; private set; }
    /// <summary>World y of the terrace's top face this frame, and of the underside it meets once shown (the plinth block's, else the grid's).</summary>
    public float TopY { get; private set; }
    public float UndersideY { get; private set; }
    /// <summary>True when the top meets the section plinth's block (false: the grid's own underside).</summary>
    public bool UnderPlinth { get; private set; }
    /// <summary>SongManager.GroundOf(column) at the last bind (the target the grounds glide to).</summary>
    public float GroundTarget { get; private set; }
    /// <summary>0 = sunk in the sea .. 1 = standing under the column.</summary>
    public float Presence => presence;
    /// <summary>True for a lowered ground (the wet rock shelf).</summary>
    public bool Lowered => lowered;
    /// <summary>The footprint this frame (world x / z ranges of the top face).</summary>
    public float X0 { get; private set; }
    public float X1 { get; private set; }
    public float Z0 { get; private set; }
    public float Z1 { get; private set; }
    /// <summary>The mesa (tests read its renderer / bounds).</summary>
    public Transform Mesa => mesa;

    Transform mesa, foam;
    MeshFilter mesaFilter, foamFilter;
    MeshRenderer mesaRend, foamRend;
    Material stoneMat, inkMat, foamMat;
    Mesh mesaMesh, foamMesh;
    float builtHx = -1f, builtHz = -1f, hx, hz, presence, twos, wobble, seedPhase;
    bool lowered, builtLowered;
    Vector3 centre;

    static readonly Color HighStone = new Color(0.78f, 0.66f, 0.7f);
    static readonly Color LowRock = new Color(0.47f, 0.43f, 0.62f);

    /// <summary>(Re-)binds the terrace to column <paramref name="column"/> (anchor <paramref name="kb"/>) whose ground target is
    /// <paramref name="groundTarget"/>.</summary>
    public void Bind(KeyBlock kb, int column, float groundTarget)
    {
        Island = kb; Column = column; GroundTarget = groundTarget;
        if (kb == null) return;
        if (mesa == null) Build();
        lowered = Mathf.Abs(groundTarget) > 0.01f ? groundTarget < 0f : kb.GroundY < 0f;
    }

    void Build()
    {
        seedPhase = Random.value * 10f;
        stoneMat = Fx.Lit(HighStone, 0.2f, 0f, true);
        if (stoneMat.HasProperty("_FaceSnap")) stoneMat.SetFloat("_FaceSnap", 0f);
        inkMat = Fx.Lit(Look.InkColor, 0.05f, 0f, false);
        if (inkMat.HasProperty("_FaceSnap")) inkMat.SetFloat("_FaceSnap", 0f);
        foamMat = Fx.Lit(Color.Lerp(MagicTextures.FoamCream, Look.SeaLine, 0.3f), 0.15f, 0f, false);
        if (foamMat.HasProperty("_FaceSnap")) foamMat.SetFloat("_FaceSnap", 0f);

        mesa = new GameObject("Mesa").transform;
        mesa.SetParent(transform, false);
        mesaFilter = mesa.gameObject.AddComponent<MeshFilter>();
        mesaRend = mesa.gameObject.AddComponent<MeshRenderer>();
        mesaRend.sharedMaterials = new[] { stoneMat, inkMat };
        mesaRend.shadowCastingMode = ShadowCastingMode.On; mesaRend.receiveShadows = true; mesaRend.lightProbeUsage = LightProbeUsage.Off;

        foam = new GameObject("Foam").transform;
        foam.SetParent(transform, false);
        foamFilter = foam.gameObject.AddComponent<MeshFilter>();
        foamRend = foam.gameObject.AddComponent<MeshRenderer>();
        foamRend.sharedMaterial = foamMat;
        foamRend.shadowCastingMode = ShadowCastingMode.Off; foamRend.receiveShadows = true; foamRend.lightProbeUsage = LightProbeUsage.Off;
        foam.gameObject.SetActive(false);
    }

    /// <summary>The ground there is at the sea's level again (or the column is gone): the terrace sinks and removes itself.</summary>
    public void Retire() { Retiring = true; }

    /// <summary>World y of <paramref name="kb"/>'s underside at its ground this frame: its real underside (B's BaseUndersideY: a falling stair's base
    /// sits lower) or its belt's, with the octave tower's lift and W's FxLift taken out (the rise-in and a drag's lift stay).</summary>
    public static float GroundUnderside(KeyBlock kb)
    {
        if (kb == null) return SeaY;
        return Tower.UndersideOf(kb) - kb.TowerLift - kb.FxLift;
    }

    /// <summary>The footprint and top this frame: the column's plinth block (B), else the anchor grid (+ its belt).</summary>
    bool Place(out float x0, out float x1, out float z0, out float z1, out float top)
    {
        x0 = x1 = z0 = z1 = top = 0f;
        var sm = SongManager.I;
        if (sm == null || Island == null) return false;
        if (SectionPlinth.I != null && Column >= 0 && Column < sm.ColumnCount)
        {
            int s = sm.SectionOf(Column);
            float bx0, bx1; bool ghost; int col;
            for (int m = 0; SectionPlinth.MeasureSpan(s, m, out bx0, out bx1, out ghost, out col); m++)
            {
                if (ghost || col != Column) continue;
                var box = SectionPlinth.Bounds(s);
                x0 = sm.ColumnWestEdge(Column); x1 = Mathf.Max(x0 + sm.ColumnWidth(Column), sm.ColumnEastEdge(Column));
                z0 = box.min.z; z1 = box.max.z;
                top = SectionPlinth.TopOf(s, m) - SectionPlinth.Thick;
                UnderPlinth = true;
                return true;
            }
        }
        UnderPlinth = false;
        var kb = Island;
        var b = kb.VisualBounds;
        x0 = b.min.x - kb.BeltOffset.x; x1 = x0 + kb.Width + (kb.Belt != null && kb.Passes >= 2 ? (kb.Passes - 1) * KeyBlock.SlotPitch : 0f);
        z0 = b.min.z; z1 = b.max.z;
        top = GroundUnderside(kb);
        return true;
    }

    /// <summary>One frame (WorldMagic's LateUpdate, after the islands and the plinths were posed).</summary>
    public void Step(float dt, bool world)
    {
        if (Island == null && !Retiring) Retire();
        twos += dt;
        bool drawTwos = twos >= 1f / Look.TwosFps || !Look.OnTwos;
        if (drawTwos) twos = 0f;
        float x0, x1, z0, z1, top;
        bool placed = Place(out x0, out x1, out z0, out z1, out top);
        if (placed)
        {
            UndersideY = top;
            X0 = x0 + Inset; X1 = x1 - Inset; Z0 = z0 + Inset; Z1 = z1 - Inset;
            centre = new Vector3((X0 + X1) * 0.5f, 0f, (Z0 + Z1) * 0.5f);
            float nhx = Mathf.Max(0.4f, (X1 - X0) * 0.5f), nhz = Mathf.Max(0.4f, (Z1 - Z0) * 0.5f);
            if (Mathf.Abs(nhx - builtHx) > 0.02f || Mathf.Abs(nhz - builtHz) > 0.02f || lowered != builtLowered) Rebuild(nhx, nhz);
        }
        bool want = !Retiring && Island != null && placed && (Mathf.Abs(GroundTarget) > 0.01f || Mathf.Abs(Island.GroundY) > 0.02f);
        presence = Mathf.MoveTowards(presence, want ? 1f : 0f, dt / ShowSeconds);
        if (!want && presence <= 0f) { Retiring = true; Destroy(gameObject); return; }
        bool on = (world || Retiring) && builtHx > 0f;
        if (mesa.gameObject.activeSelf != on) mesa.gameObject.SetActive(on);
        if (!on) { if (foam.gameObject.activeSelf) foam.gameObject.SetActive(false); return; }
        float e = want ? Ease.OutCubic(presence) : Ease.InOutCubic(presence);
        TopY = Mathf.Lerp(SeaY - 0.6f, UndersideY, e);
        mesa.position = new Vector3(centre.x, TopY, centre.z);
        // the foam laps the foot where the wall meets the water (the wall leans out 4 % per unit down)
        bool wet = TopY > SeaY + 0.04f;
        if (foam.gameObject.activeSelf != wet) foam.gameObject.SetActive(wet);
        if (!wet) return;
        if (drawTwos) wobble = Mathf.Sin((Time.time + seedPhase) * 4.1f) * 0.01f;
        float grow = 0.04f * (TopY - SeaY);
        float sx = (hx + grow) / Mathf.Max(0.1f, hx) * (1f + wobble), sz = (hz + grow) / Mathf.Max(0.1f, hz) * (1f + wobble);
        foam.position = new Vector3(centre.x, SeaY + 0.028f, centre.z);
        foam.localScale = new Vector3(sx, 1f, sz);
    }

    void Rebuild(float nhx, float nhz)
    {
        hx = nhx; hz = nhz; builtHx = nhx; builtHz = nhz; builtLowered = lowered;
        if (mesaMesh != null) Destroy(mesaMesh);
        if (foamMesh != null) Destroy(foamMesh);
        float r = Mathf.Min(0.35f, Mathf.Min(hx, hz) * 0.3f);
        mesaMesh = MagicMeshes.Terrace(hx, hz, r, Depth, ProjectConfig.StairStepRise, 11 + Column * 7);
        mesaFilter.sharedMesh = mesaMesh;
        foamMesh = MagicMeshes.FoamRect(hx, hz, r, 0.05f, 0.12f);
        foamFilter.sharedMesh = foamMesh;
        Color stone = lowered ? LowRock : HighStone;
        stoneMat.SetColor("_BaseColor", stone);
        stoneMat.SetColor("_EmissionColor", stone * (lowered ? 0.06f : 0.1f));
        inkMat.SetColor("_BaseColor", Color.Lerp(Look.InkColor, stone, 0.15f));
    }

    void OnDestroy()
    {
        if (stoneMat != null) Destroy(stoneMat);
        if (inkMat != null) Destroy(inkMat);
        if (foamMat != null) Destroy(foamMat);
        if (mesaMesh != null) Destroy(mesaMesh);
        if (foamMesh != null) Destroy(foamMesh);
    }
}
