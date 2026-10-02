using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPEC v5 §2.5 (package K): the conveyor belt of an island with repeat ≥ 2 (the user, 2026-09-30: "it establishes almost like a track that the
/// grid moves along (like when you're at walmart and you're placing things on the thing that moves the items closer to the cashier) except it's
/// not constant speed and does it every repeat"). A straight belt laid toward +x (the time direction) from the island's home slot, one slot per
/// pass (slot pitch = IslandWidth + BeltGap), just under the platform: ink-outlined dark rubber in a light frame (side rails), whose slats and
/// centre chevrons move with the island, a drum at each end that turns only while the belt moves, and a faint footprint with pass pips per slot. The island rides it (KeyBlock applies
/// SongManager.RideSlot); the belt stays put (it is a child of the island whose local position counters the ride). It grows / shrinks with a
/// springy animation when the repeat changes (the rebuilt island carries the old length) and removes itself when it shrinks to one slot.
/// The ride itself is <see cref="Ride(double,int,float,bool,out float)"/>: a pure function of the column-local beat.
/// v7: a slot is the island's own width + BeltGap (a 2-measure island moves two measures per pass: KeyBlock.BeltPitch); a falling stairs island's
/// belt sits under its lowered base (KeyBlock.BaseDrop, B); rewind islands and phrases have no belt (KeyBlock.HasBelt).
/// v8 (the user: "the piano itself should also be able to repeat -> in which it just has like checkpoints (shallow circles on the sea where its
/// moving) in which after the measure finishes it gets dragged to that circle"): a KEYBOARD's belt is drawn as CHECKPOINTS instead — no rubber,
/// rails or drums: one shallow circle on the sea per pass (a soft disc in a cream ring, its pass pips inside) under where the keyboard stands for
/// that pass, fixed in the world while the keyboard is dragged from one to the next (the same ride: <see cref="Ride(double,int,float,bool,out float)"/>;
/// KeyBlock swings it on the drag, <see cref="DragYaw"/>).
/// v9 (the user, via S17: the empty pass-2/3 slots "are big dark rails that dominate the column"): the RAIL (rubber, slats, chevrons, side rails,
/// drums) is one slot long and lies only UNDER THE ISLAND — it travels with the ride (the jerk, the glide back) while the slats still run with the
/// carry; every slot is drawn as a faint DOTTED outline on the floor with its pass pips (the slots ahead are a promise, not a road).
/// </summary>
public class Belt : MonoBehaviour
{
    public const float InsetX = 0.3f, InsetZ = 0.35f, Thick = 0.3f;
    /// <summary>Slats and chevrons per slot (whole numbers: the surface pattern repeats exactly once per slot, so a belt at rest always shows the same slats).</summary>
    public const int SlatsPerSlot = 14, ChevronsPerSlot = 7;
    const float ArrowStrip = 1.15f, RailH = 0.1f, RailD = 0.08f;
    const float AnimSeconds = 0.42f;

    KeyBlock kb;
    int target = 1; float shown = 1f, animFrom = 1f, animT = 9f; bool dying;
    float travel, lastRide; bool rideInit;
    Transform body, top, arrows, drumW, drumE, railF, railB;
    readonly List<LineRenderer> feet = new List<LineRenderer>();
    readonly List<Transform> pips = new List<Transform>();
    readonly List<int> pipSlot = new List<int>();
    Material bodyMat, topMat, arrowMat, drumMat, footMat, pipMat;
    float builtLen = -1f;
    static Texture2D chevTex, slatTex;

    /// <summary>Passes the belt is laid for (its slot count).</summary>
    public int Slots => target;
    /// <summary>The belt's shown length in slots (animates toward <see cref="Slots"/>).</summary>
    public float SlotsShown => shown;
    /// <summary>World units the belt's surface has carried the island forward (the slats and the drums follow it).</summary>
    public float Travel => travel;
    /// <summary>True when the surface moved this frame (the drums turn only then).</summary>
    public bool Moving { get; private set; }
    /// <summary>The end drums' turn in degrees (tests).</summary>
    public float DrumAngle => -travel / DrumRadius * Mathf.Rad2Deg;
    static float DrumRadius => Thick * 0.5f + 0.1f;

    /// <summary>v8: true when this belt is drawn as checkpoints on the sea (a keyboard's).</summary>
    public bool Checkpoints => rings;
    /// <summary>v8: the checkpoint circles laid out (tests) and circle <paramref name="k"/>'s world centre.</summary>
    public int CheckpointCount { get { int n = 0; foreach (var r in ringT) if (r != null && r.gameObject.activeSelf) n++; return n; } }
    public Vector3 CheckpointCenter(int k) => k >= 0 && k < ringT.Count && ringT[k] != null ? ringT[k].position : Vector3.zero;
    /// <summary>v8: the checkpoints' fallback height (the sea), a circle's diameter (× the island's depth) and where a dragged keyboard is grabbed
    /// (island-local x as a fraction of its width, on its front edge: between the bottom centre and the bottom-right corner).</summary>
    public const float RingY = -3.97f, RingOfDepth = 0.62f, GrabX = 0.75f;
    /// <summary>v8 (the user: "rather than it gets dragged after each measure ends, it should get dragged at a constant speed, with waves around it
    /// since it's getting dragged across the ocean, and with a bit of rotation so it looks natural"): the lean of a dragged keyboard (degrees, CCW
    /// seen from above) and its slow sway around it (± degrees, a period of SwayBeats beats); the ease in / out of the drag (fraction of the way).</summary>
    public const float SwayBase = 3.2f, SwayAmp = 1.4f, SwayBeats = 3f, DragEase = 0.08f;
    /// <summary>v8: a dragged keyboard's wake — a ripple every WakeEvery seconds at full speed, alternating round the hull; ripples so far (tests).</summary>
    public const float WakeEvery = 0.09f;
    public static int WakeRipples;

    /// <summary>
    /// v8: a KEYBOARD's ride over its whole turn of <paramref name="passes"/> passes of <paramref name="passLen"/> beats, at <paramref name="local"/>
    /// beats into it, in slots: dragged at a CONSTANT speed from its first checkpoint (slot 0) to its last (slot passes − 1), arriving as its turn ends,
    /// with a short ease in and out (DragEase of the way); <paramref name="selfLoop"/> (a one-column loop) leaves the last BeltGlideBeats for the glide
    /// home (with a hop, <paramref name="lift"/>), as the belt does.
    /// </summary>
    public static float DragRide(double local, int passes, float passLen, bool selfLoop, out float lift)
    {
        lift = 0f;
        if (passes < 2 || passLen <= 0f || local < 0.0) return 0f;
        double end = passes * (double)passLen;
        float glide = Mathf.Min(ProjectConfig.BeltGlideBeats, passLen * (selfLoop ? 0.5f : 1f));
        double travel = selfLoop ? end - glide : end;
        if (local >= travel)
        {
            if (!selfLoop) return passes - 1;
            double u = (local - travel) / glide;
            if (u >= 1.0) return 0f;
            lift = ProjectConfig.BeltGlideLift * Mathf.Sin((float)u * Mathf.PI);
            return (passes - 1) * (1f - Ease.InOutCubic((float)u));
        }
        return (passes - 1) * Trapezoid((float)(local / travel), DragEase);
    }

    /// <summary>Progress along a move at <paramref name="x"/> ∈ [0, 1] of its time at a constant speed, easing in over the first <paramref name="a"/>
    /// of the time and out over the last (the speed ramps linearly; continuous position and speed).</summary>
    public static float Trapezoid(float x, float a)
    {
        x = Mathf.Clamp01(x); a = Mathf.Clamp(a, 1e-4f, 0.5f);
        float v = 1f / (1f - a);   // the cruising speed
        if (x < a) return 0.5f * v * x * x / a;
        if (x > 1f - a) { float y = 1f - x; return 1f - 0.5f * v * y * y / a; }
        return 0.5f * v * a + v * (x - a);
    }

    /// <summary>v8: the wake of a keyboard dragged at <paramref name="speed01"/> of its cruising speed (0 = still): ripples on the floor round its hull
    /// — along its front and back edges and off its trailing end — more of them the faster it goes.</summary>
    public void Wake(float speed01, float dt)
    {
        if (!rings || kb == null || speed01 <= 0.02f) { wakeT = 0f; return; }
        wakeT += dt * Mathf.Clamp01(speed01);
        float e = KeyBlock.EdgeInset - (kb.Width - kb.PlatformWidth) * 0.5f, w = kb.PlatformWidth, d = kb.Depth;   // v9: round its (hugging) platform
        var t = kb.transform;
        while (wakeT >= WakeEvery)
        {
            wakeT -= WakeEvery;
            int i = wakeI++ % 6;
            // 0 / 1: the trailing end (west), 2 / 3: along the front edge, 4 / 5: along the back edge
            Vector3 local;
            switch (i)
            {
                case 0: local = new Vector3(-e - 0.25f, 0f, -e + d * 0.3f); break;
                case 1: local = new Vector3(-e - 0.25f, 0f, -e + d * 0.7f); break;
                case 2: local = new Vector3(-e + w * 0.3f, 0f, -e - 0.3f); break;
                case 3: local = new Vector3(-e + w * 0.75f, 0f, -e - 0.3f); break;
                case 4: local = new Vector3(-e + w * 0.3f, 0f, -e + d + 0.3f); break;
                default: local = new Vector3(-e + w * 0.75f, 0f, -e + d + 0.3f); break;
            }
            Vector3 p = t.TransformPoint(local); p.y = CheckpointY;
            float sc = (i < 2 ? 1.5f : 1.1f) * (0.75f + 0.5f * Mathf.Clamp01(speed01));
            Fx.Ripple(p, Color.Lerp(Look.SeaLine, Color.white, 0.55f), sc, 0.95f);
            WakeRipples++;
        }
    }
    float wakeT; int wakeI;

    /// <summary>Lays a belt under <paramref name="owner"/> (a child of its transform).</summary>
    public static Belt Create(KeyBlock owner)
    {
        var go = new GameObject("Belt");
        go.transform.SetParent(owner.transform, false);
        var b = go.AddComponent<Belt>();
        b.Init(owner);
        return b;
    }

    // ------------------------------------------------------------------ the ride (pure)
    /// <summary>
    /// SPEC v5 §2.5 the ride of an island that plays <paramref name="passes"/> (≥ 2) passes of <paramref name="passLen"/> beats, at
    /// <paramref name="local"/> beats after its column's start, in slots: pass p plays at slot p; near each pass boundary the belt jerks it one
    /// slot forward (<see cref="Jerk"/>: accelerate, travel, a small overshoot by the boundary, a settling clunk just after it); after its last pass
    /// it glides back to slot 0 with a hop (<paramref name="lift"/>, world units) over BeltGlideBeats. <paramref name="selfLoop"/> = the column
    /// loops onto itself (a one-column loop): the glide happens in the final beats of its last pass instead, so it is home when the column restarts.
    /// </summary>
    public static float Ride(double local, int passes, float passLen, bool selfLoop, out float lift)
    {
        lift = 0f;
        if (passes < 2 || passLen <= 0f || local < 0.0) return 0f;
        double end = passes * (double)passLen;
        float glide = Mathf.Min(ProjectConfig.BeltGlideBeats, passLen * (selfLoop ? 0.5f : 1f));
        double gStart = selfLoop ? end - glide : end;
        if (local >= gStart)
        {
            double u = (local - gStart) / glide;
            if (u >= 1.0) return 0f;
            lift = ProjectConfig.BeltGlideLift * Mathf.Sin((float)u * Mathf.PI);
            return (passes - 1) * (1f - Ease.InOutCubic((float)u));
        }
        int p = (int)System.Math.Floor(local / passLen);
        double inPass = local - p * (double)passLen;
        float lead = Mathf.Min(ProjectConfig.JerkLead, passLen * 0.45f), settle = ProjectConfig.JerkSettle, dur = lead + settle, tb = lead / dur;
        if (p < passes - 1 && inPass >= passLen - lead) return p + Jerk((float)((inPass - (passLen - lead)) / dur), tb);
        if (p >= 1 && inPass < settle) return (p - 1) + Jerk((float)((lead + inPass) / dur), tb);
        return p;
    }

    /// <summary>The belt's jerk over one slot at <paramref name="tau"/> ∈ [0, 1] of its duration: an ease in / out to a hair past the slot by the
    /// pass boundary (<paramref name="tauBoundary"/>), then a damped clunk back onto it.</summary>
    public static float Jerk(float tau, float tauBoundary)
    {
        const float over = 0.05f;
        if (tau <= 0f) return 0f;
        if (tau >= 1f) return 1f;
        if (tau < tauBoundary) return (1f + over) * Ease.InOutCubic(tau / tauBoundary);
        float u = (tau - tauBoundary) / Mathf.Max(1e-4f, 1f - tauBoundary);
        return 1f + over * Mathf.Exp(-3f * u) * Mathf.Cos(2.5f * Mathf.PI * u);
    }

    // ------------------------------------------------------------------ build
    void Init(KeyBlock owner)
    {
        kb = owner;
        if (owner.IsKeyboard) { InitRings(); return; }   // v8: a keyboard's checkpoints
        Color chord = owner.chordColor;
        Color rubber = Color.Lerp(Look.InkColor, chord, 0.26f);
        bodyMat = Fx.Lit(rubber, 0.18f, 0f);
        topMat = Fx.Alpha(SlatTexture, Palette.A(Color.Lerp(chord, Color.white, 0.7f), 0.22f));
        arrowMat = Fx.Alpha(ChevronTexture, Palette.A(Color.Lerp(chord, Color.white, 0.5f), 0.62f));
        drumMat = Fx.Lit(Color.Lerp(Look.InkColor, new Color(0.78f, 0.76f, 0.88f), 0.62f), 0.3f, 0f);
        footMat = Fx.Alpha(DotTexture, Palette.A(new Color(1f, 0.95f, 0.86f), 0.55f));   // v9: a dotted outline per slot
        footMat.mainTextureScale = new Vector2(1f / DotPeriod, 1f);
        pipMat = Fx.Alpha(IconFactory.GetTexture("dot"), Palette.A(new Color(1f, 0.95f, 0.86f), 0.62f));

        body = Part("Body", null, bodyMat, true);
        top = Part("Slats", MeshFactory.FlatQuad(), topMat, false);
        arrows = Part("Chevrons", MeshFactory.FlatQuad(), arrowMat, false);
        float r = DrumRadius, zw = owner.Depth - 2f * InsetZ + 0.22f;
        drop = owner.BaseDrop;   // v7: under a falling stair's lowered base
        var drumMesh = MeshFactory.Prism(8, r, zw);
        drumW = Part("DrumW", drumMesh, drumMat, true);
        drumE = Part("DrumE", drumMesh, drumMat, true);
        railF = Part("RailF", null, drumMat, false);
        railB = Part("RailB", null, drumMat, false);
        // v9: the slots' dotted outlines and pips lie on the section's floor (KeyBlock.SetHome), not at the rail's height under a raised island
        slotRoot = new GameObject("Slots").transform;
        slotRoot.SetParent(transform, false);
        slotRoot.localPosition = new Vector3(0f, ProjectConfig.BeltTopY - drop, 0f);
        builtLen = -1f;
        Layout();
    }

    Transform Part(string name, Mesh mesh, Material mat, bool shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var mf = go.AddComponent<MeshFilter>();
        if (mesh != null) mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;
        mr.lightProbeUsage = LightProbeUsage.Off;
        return go.transform;
    }

    /// <summary>The belt's slot count: <paramref name="slots"/> (≤ 1 = remove it). <paramref name="animate"/>: grow / shrink from <paramref name="from"/>
    /// slots with a springy ease (a shrink to one slot removes the belt at its end).</summary>
    public void SetSlots(int slots, bool animate, float from)
    {
        target = Mathf.Clamp(slots, 1, ProjectConfig.MaxRepeat);
        dying = target <= 1;
        if (animate && Mathf.Abs(from - target) > 0.01f) { animFrom = Mathf.Max(1f, from); shown = animFrom; animT = 0f; }
        else { shown = target; animT = 9f; if (dying) { Destroy(gameObject); return; } }
        builtLen = -1f;
        Layout();
    }

    /// <summary>KeyBlock, every frame: the island's shown ride (slots) and whether the belt is carrying it (<paramref name="carried"/>: the jerk and
    /// its clunk, both ways). The surface moves with it (the slats slide, the drums turn); the glide back after the last pass (the island hops over
    /// the belt) and a catch-up after a seek do not run the belt, so one jerk moves the surface exactly one slot.</summary>
    public void Ride(float slot, bool carried)
    {
        if (!rideInit) { rideInit = true; lastRide = slot; Moving = false; return; }
        float d = slot - lastRide;
        lastRide = slot;
        if (Mathf.Abs(d) > 1e-5f) PlaceRail();   // v9: the rail lies under the island wherever it is
        Moving = carried && Mathf.Abs(d) > 1e-5f;
        if (!Moving) return;
        travel += d * Pitch;
        ApplySurface();
    }

    void Update()
    {
        if (kb == null) { Destroy(gameObject); return; }
        if (!rings && (rowCheck += Time.unscaledDeltaTime) > 0.5f)
        {
            rowCheck = 0f;
            if (kb.BeltRowLeader != rowLead || Mathf.Abs(kb.RowWidth - rowW) > 1e-3f) { builtLen = -1f; Layout(); }
        }
        if (animT < 1f)
        {
            animT = Mathf.Min(1f, animT + Time.deltaTime / AnimSeconds);
            float e = target > animFrom ? Ease.OutBack(animT, 1.2f) : Ease.OutCubic(animT);
            shown = Mathf.LerpUnclamped(animFrom, target, e);
            Layout();
            if (animT >= 1f && dying) { Destroy(gameObject); return; }
        }
    }

    // ------------------------------------------------------------------ layout (island-local, the ride countered by the transform)
    void Layout()
    {
        if (rings) { LayoutRings(); return; }
        if (kb == null || body == null) return;
        // v9: a grid shorter than its column lays its ROW's belt (the column's width, from the column's west edge) — only the row's first grid
        // draws it; the others ride it without one
        bool lead = kb.BeltRowLeader;
        rowLead = lead; rowW = kb.RowWidth;
        if (lead != rowShown) { rowShown = lead; for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(lead); }
        if (!lead) return;
        float e = KeyBlock.EdgeInset, w = rowW, d = kb.Depth, pitch = Pitch, r = DrumRadius;
        float xs = rowW > kb.Width + 1e-3f ? kb.ColumnWestLocal + e : 0f;   // the row starts at the column's west edge
        rowXs = xs;
        // v9: the rail is ONE slot long (the island's row) and sits under the island: PlaceRail moves it with the ride
        float len = Mathf.Max(0.6f, w - 2f * InsetX);
        float topY = ProjectConfig.BeltTopY - drop;
        float zw = d - 2f * InsetZ;
        float bodyLen = Mathf.Max(0.2f, len - 2f * r);
        if (builtLen < 0f || Mathf.Abs(builtLen - bodyLen) > 1e-3f)
        {
            builtLen = bodyLen;
            body.GetComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(new Vector3(builtLen, Thick, zw), 0.06f, 2);
            var railMesh = MeshFactory.RoundedBox(new Vector3(builtLen + 2f * r, RailH, RailD), 0.03f, 1);
            railF.GetComponent<MeshFilter>().sharedMesh = railMesh;
            railB.GetComponent<MeshFilter>().sharedMesh = railMesh;
        }
        body.localScale = Vector3.one;
        top.localScale = new Vector3(bodyLen, 1f, zw - 0.14f);
        topMat.mainTextureScale = new Vector2(bodyLen / SlatPeriod, 1f);
        arrows.localScale = new Vector3(bodyLen, 1f, ArrowStrip);
        arrowMat.mainTextureScale = new Vector2(bodyLen / ArrowPeriod, 1f);
        railF.localScale = railB.localScale = Vector3.one;
        railLen = len; railTopY = topY;
        PlaceRail();
        ApplySurface();
        LayoutFeet(e, w, d, pitch);
    }

    bool rowLead = true, rowShown = true; float rowW, rowXs, rowCheck;
    float railLen = -1f, railTopY;

    /// <summary>v9: the rail's slot this frame — under the island (its shown ride, inside the belt's slots).</summary>
    public float RailSlot => kb != null ? Mathf.Clamp(kb.RideSlotShown, 0f, Mathf.Max(0f, Mathf.Max(1f, shown) - 1f)) : 0f;

    /// <summary>v9: lays the one-slot rail (body, slats, chevrons, side rails, drums) under the island's shown ride.</summary>
    void PlaceRail()
    {
        if (rings || body == null || kb == null || railLen < 0f || !rowShown) return;
        float e = KeyBlock.EdgeInset, d = kb.Depth, r = DrumRadius;
        float x0 = -e + InsetX + rowXs + RailSlot * Pitch, len = railLen, topY = railTopY;
        float zc = -e + d * 0.5f, zw = d - 2f * InsetZ, yc = topY - Thick * 0.5f, xm = x0 + len * 0.5f;
        body.localPosition = new Vector3(xm, yc, zc);
        top.localPosition = new Vector3(xm, topY + 0.004f, zc);
        arrows.localPosition = new Vector3(xm, topY + 0.008f, zc);
        railF.localPosition = new Vector3(xm, yc + Thick * 0.18f, zc - zw * 0.5f - RailD * 0.3f);
        railB.localPosition = new Vector3(xm, yc + Thick * 0.18f, zc + zw * 0.5f + RailD * 0.3f);
        float zStart = zc - (zw + 0.22f) * 0.5f;
        drumW.localPosition = new Vector3(x0 + r, yc, zStart);
        drumE.localPosition = new Vector3(x0 + len - r, yc, zStart);
    }

    /// <summary>v9: the rail's island-local x range (tests).</summary>
    public Vector2 RailSpanLocal => body != null ? new Vector2(body.localPosition.x - railLen * 0.5f, body.localPosition.x + railLen * 0.5f) : Vector2.zero;
    /// <summary>v9: true when this belt is drawn (its row's first grid); false: the grid rides its row's belt without one of its own.</summary>
    public bool Drawn => rings || rowShown;

    /// <summary>v7: this belt's slot (its island's width + BeltGap).</summary>
    float Pitch => kb != null ? kb.BeltPitch : KeyBlock.SlotPitch;
    float drop;
    float SlatPeriod => Pitch / SlatsPerSlot;
    float ArrowPeriod => Pitch / ChevronsPerSlot;

    void ApplySurface()
    {
        if (topMat == null) return;
        topMat.mainTextureOffset = new Vector2(-travel / SlatPeriod, 0f);
        if (arrowMat != null) arrowMat.mainTextureOffset = new Vector2(-travel / ArrowPeriod, 0f);
        var spin = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, DrumAngle, 0f);
        if (drumW != null) drumW.localRotation = spin;
        if (drumE != null) drumE.localRotation = spin;
    }

    /// <summary>A faint footprint per slot (where the platform stands during that pass) with pass pips (1 … n dots) at its front.</summary>
    void LayoutFeet(float e, float w, float d, float pitch)
    {
        int want = Mathf.Max(target, Mathf.CeilToInt(shown - 0.01f));
        while (feet.Count < want) feet.Add(MakeFoot(feet.Count));
        float y = 0.012f;   // v9: in the Slots root (on the floor)
        float z0 = -e + InsetZ + 0.16f, z1 = -e + d - InsetZ - 0.16f;
        for (int k = 0; k < feet.Count; k++)
        {
            bool on = k < want && k + 1 <= shown + 0.2f;
            var lr = feet[k];
            if (lr.gameObject.activeSelf != on) lr.gameObject.SetActive(on);
            if (!on) continue;
            float x0 = -e + rowXs + k * pitch + 0.3f, x1 = -e + rowXs + w + k * pitch - 0.3f;
            SetRect(lr, x0, x1, z0, z1, y);
        }
        // pips: slot k carries k + 1 dots at its front-left (pass 1, 2, 3, 4)
        int n = 0;
        for (int k = 0; k < want; k++) n += k + 1;
        while (pips.Count < n) { var t = Part("Pip", MeshFactory.FlatQuad(), pipMat, false); t.SetParent(slotRoot, false); t.localScale = new Vector3(0.2f, 1f, 0.2f); pips.Add(t); pipSlot.Add(0); }
        int i = 0;
        for (int k = 0; k < want; k++)
            for (int j = 0; j <= k; j++, i++)
            {
                pipSlot[i] = k;
                pips[i].localPosition = new Vector3(-e + rowXs + k * pitch + 0.62f + j * 0.3f, y + 0.002f, z0 + 0.3f);
                bool on = k + 1 <= shown + 0.2f;
                if (pips[i].gameObject.activeSelf != on) pips[i].gameObject.SetActive(on);
            }
        for (; i < pips.Count; i++) if (pips[i].gameObject.activeSelf) pips[i].gameObject.SetActive(false);
    }

    LineRenderer MakeFoot(int k)
    {
        var go = new GameObject("Foot_" + k);
        go.transform.SetParent(slotRoot != null ? slotRoot : transform, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // TransformZ alignment with +Z up: the line lies flat on the belt
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.loop = true; lr.alignment = LineAlignment.TransformZ;
        lr.widthMultiplier = 0.09f; lr.numCornerVertices = 2; lr.numCapVertices = 0;
        lr.textureMode = LineTextureMode.Tile;   // v9: dots along the outline (one per DotPeriod world units)
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.lightProbeUsage = LightProbeUsage.Off;
        lr.sharedMaterial = footMat;
        return lr;
    }

    static readonly Vector3[] rectPts = new Vector3[20];
    static void SetRect(LineRenderer lr, float x0, float x1, float z0, float z1, float y)
    {
        // a rounded rectangle, points in the line's rotated frame (island-local p -> rim-local Euler(90,0,0) * p)
        const float r = 0.28f; int n = 0;
        var toLine = Quaternion.Euler(90f, 0f, 0f);
        float[] cx = { x0 + r, x0 + r, x1 - r, x1 - r }, cz = { z0 + r, z1 - r, z1 - r, z0 + r };
        float[] a0 = { 270f, 180f, 90f, 0f };
        for (int c = 0; c < 4; c++)
            for (int s = 0; s < 5; s++)
            {
                float a = (a0[c] - s * 22.5f) * Mathf.Deg2Rad;
                rectPts[n++] = toLine * new Vector3(cx[c] + Mathf.Cos(a) * r, y, cz[c] + Mathf.Sin(a) * r);
            }
        lr.positionCount = n;
        lr.SetPositions(rectPts);
    }

    /// <summary>v9: the slot outlines' dot spacing (world units).</summary>
    public const float DotPeriod = 0.24f;
    static Texture2D dotTex;
    /// <summary>v9: one round dot per u period (white with alpha, repeat-wrapped along u): a slot's dotted outline.</summary>
    static Texture2D DotTexture
    {
        get
        {
            if (dotTex != null) return dotTex;
            const int w = 32, h = 16;
            dotTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "deacube_belt_dot" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w * 2f - 0.5f, dy = (y + 0.5f) / h - 0.5f;   // a dot of radius ~0.4 in the left half of each period
                    float dd = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01((0.42f - dd) * 12f) * 255f));
                }
            dotTex.SetPixels32(px); dotTex.Apply(false, false);
            return dotTex;
        }
    }

    /// <summary>A straight slat across the belt (white with alpha, repeat-wrapped along u): the rubber's ridges.</summary>
    static Texture2D SlatTexture
    {
        get
        {
            if (slatTex != null) return slatTex;
            const int n = 32;
            slatTex = new Texture2D(n, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "deacube_belt_slat" };
            var px = new Color32[n * 4];
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                float a = Mathf.Clamp01((0.11f - Mathf.Abs(u - 0.5f)) * n * 0.5f);
                for (int y = 0; y < 4; y++) px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            slatTex.SetPixels32(px); slatTex.Apply(false, false);
            return slatTex;
        }
    }

    /// <summary>A ">" chevron (white with alpha, repeat-wrapped along u), pointing +u = the direction the belt carries the island.</summary>
    static Texture2D ChevronTexture
    {
        get
        {
            if (chevTex != null) return chevTex;
            const int n = 64;
            chevTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "deacube_belt_chevron" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    // the chevron's two strokes: (0.28, 0.12) -> (0.62, 0.5) -> (0.28, 0.88)
                    float dd = Mathf.Min(SegDist(u, v, 0.28f, 0.12f, 0.62f, 0.5f), SegDist(u, v, 0.62f, 0.5f, 0.28f, 0.88f));
                    float a = Mathf.Clamp01((0.075f - dd) * n * 0.7f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            chevTex.SetPixels32(px); chevTex.Apply(false, false);
            return chevTex;
        }
    }

    static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
        float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
        float dx = ax + vx * t - px, dy = ay + vy * t - py;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ------------------------------------------------------------------ v8: a keyboard's checkpoints
    bool rings;
    Transform ringRoot;
    readonly List<Transform> ringT = new List<Transform>();
    Material ringMat, discMat;
    Vector3 home; bool hasHome;

    const string RingGlyph = "k8.checkRing", DiscGlyph = "k8.checkDisc";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterGlyphs()
    {
        IconFactory.Register(RingGlyph, p => IconFactory.P.Ring(p, 0f, 0f, 0.86f, 0.06f));
        IconFactory.Register(DiscGlyph, p => IconFactory.P.Circle(p, 0f, 0f, 0.84f));
    }

    void InitRings()
    {
        rings = true;   // v8: no rubber — the keyboard is dragged across the floor between its checkpoints
        // a shallow dip in the floor: a darker disc with a crisp cream rim
        ringMat = Fx.Alpha(IconFactory.GetTexture(RingGlyph), Palette.A(new Color(1f, 0.95f, 0.86f), 0.8f));
        discMat = Fx.Alpha(IconFactory.GetTexture(DiscGlyph), Palette.A(Look.SeaShadow, 0.34f));
        pipMat = Fx.Alpha(IconFactory.GetTexture("dot"), Palette.A(new Color(1f, 0.95f, 0.86f), 0.7f));
        builtLen = -1f;
        LayoutRings();
    }

    /// <summary>v8: KeyBlock tells the checkpoints where the keyboard's root stands at home (world, no ride, no swing, no lift or shake) and the height
    /// of the floor under it (the section's plinth top); the circles lie there.</summary>
    public void SetHome(Vector3 rootAtHome, float floorY)
    {
        if (!rings) { floor = floorY; hasHome = true; return; }   // v9: a belt's slot outlines lie on this floor
        if (hasHome && (rootAtHome - home).sqrMagnitude < 1e-8f && Mathf.Abs(floorY - floor) < 1e-5f) return;
        home = rootAtHome; floor = floorY; hasHome = true;
        PlaceRings();
    }
    float floor = RingY;
    /// <summary>v8: the height the circles lie at now (world).</summary>
    public float CheckpointY => (hasHome ? floor : RingY) - (kb != null ? SectionPlinth.SinkOf(kb.column) : 0f);   // v9: the present mode's floor sinks / rises with its column

    void LayoutRings()
    {
        if (kb == null) return;
        int want = Mathf.Max(target, Mathf.CeilToInt(shown - 0.01f));
        while (ringT.Count < want)
        {
            int k = ringT.Count;
            if (ringRoot == null) ringRoot = new GameObject("KeyboardCheckpoints " + kb.name).transform;   // not under the island: it never carries them
            var root = new GameObject("Checkpoint_" + k).transform;
            root.SetParent(ringRoot, false);
            var disc = Part("Disc", MeshFactory.FlatQuad(), discMat, false); disc.SetParent(root, false);
            var ring = Part("Ring", MeshFactory.FlatQuad(), ringMat, false); ring.SetParent(root, false); ring.localPosition = Vector3.up * 0.004f;
            for (int j = 0; j <= k; j++)
            {
                var pip = Part("Pip", MeshFactory.FlatQuad(), pipMat, false); pip.SetParent(root, false);
                pip.localScale = new Vector3(0.09f, 1f, 0.09f); pip.localPosition = new Vector3((j - k * 0.5f) * 0.14f, 0.008f, 0f);
            }
            ringT.Add(root);
        }
        PlaceRings();
    }

    void PlaceRings()
    {
        if (kb == null) return;
        float e = KeyBlock.EdgeInset, d = kb.Depth * RingOfDepth;
        Vector3 basis = hasHome ? home : kb.transform.position;
        Vector3 centre = basis + new Vector3(-e + kb.Width * 0.5f, 0f, -e + kb.Depth * 0.5f);
        for (int k = 0; k < ringT.Count; k++)
        {
            var r = ringT[k];
            // circle k pops in as the shown slot count reaches it (the repeat grows / shrinks with a spring)
            float grow = Mathf.Clamp01(shown - k + 0.2f);
            bool on = k < Mathf.Max(target, Mathf.CeilToInt(shown - 0.01f)) && grow > 0.01f;
            if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
            if (!on) continue;
            r.SetPositionAndRotation(new Vector3(centre.x + k * Pitch, CheckpointY, centre.z), Quaternion.identity);   // fixed on the floor, never swung
            float sc = d * Mathf.Min(1f, grow);
            r.localScale = new Vector3(sc, 1f, sc);
        }
    }

    void LateUpdate()
    {
        if (rings) PlaceRings();   // the island under them rides and swings: the circles stay put on the sea
        else if (slotRoot != null && hasHome)
        {
            // v9: the dotted slots stay on the floor while the island rises (a tower), dips or bobs
            Vector3 p = transform.InverseTransformPoint(new Vector3(transform.position.x, CheckpointY, transform.position.z));
            if (Mathf.Abs(p.y - slotRoot.localPosition.y) > 1e-4f) slotRoot.localPosition = new Vector3(0f, p.y, 0f);   // only when the island moved
        }
    }
    Transform slotRoot;
    /// <summary>v9: world y the slots' dotted outlines lie at (tests).</summary>
    public float SlotsY => slotRoot != null ? slotRoot.position.y : float.NaN;

    void OnEnable() { if (ringRoot != null) ringRoot.gameObject.SetActive(true); }
    void OnDisable() { if (ringRoot != null) ringRoot.gameObject.SetActive(false); }

    void OnDestroy()
    {
        if (ringRoot != null) Destroy(ringRoot.gameObject);
        if (ringMat != null) Destroy(ringMat);
        if (discMat != null) Destroy(discMat);
        if (bodyMat != null) Destroy(bodyMat);
        if (topMat != null) Destroy(topMat);
        if (arrowMat != null) Destroy(arrowMat);
        if (drumMat != null) Destroy(drumMat);
        if (footMat != null) Destroy(footMat);
        if (pipMat != null) Destroy(pipMat);
    }
}
