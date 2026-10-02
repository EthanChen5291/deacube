using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SPEC v3 §2.2: the fly-to-camera cube inspector (package A). Open(cube): the cube is selected (every UIManager binding uses
/// PathManager.selectedCube), the world is locked ("inspector": picking + camera; cube hotkeys stay live), the cube's body
/// hides (its path stays, drawn above the dim), and a proxy — the same rounded box, lit, instrument colour + emission — flies
/// 0.55 s along a Bezier (start → start + 1.6 u up → target) to a camera-anchored point (viewport 0.28, 0.52 at 3.0 u, sized
/// to ~24 % of the screen height), its rotation slerping to a hero pose (tilt 24°, yaw 38°) plus one extra full spin
/// (InOutSine), position OutCubic, with a whoosh and a short trail. A camera-parented dim quad at 3.6 u fades in behind it
/// (the proxy, its focus backdrop and the cube's path draw after the dim, so nothing pops or z-fights while it crosses the dim) and the
/// card slides in (UIManager). While open the proxy turns (12°/s) and floats, spins with inertia when dragged, squashes and
/// flashes on its cube's landings, bursts on a recolour, glows with the volume and greys when muted. Behind it a comic focus
/// (manga focus lines pointing at the cube over a Ben-Day halftone halo) opens with the fly-in, kicks on every landing and
/// folds back into the cube before the return flight.
/// Close (Esc, the card's ✕, a click / right-click on the dim): the proxy flies back 0.45 s to the cube's CURRENT pose
/// (re-targeted every frame), then the cube shows again with a thud + ripple, the lock is released and the cube deselected.
/// Delete: the proxy pops, the cube is deleted, no return flight. Invariant: selectedCube != null ⇔ open / opening / closing
/// (a selection made elsewhere opens the inspector; an undo or structural rebuild re-targets the cube with the same id).
/// F11: a landing flash adds at most 0.3 of the colour to the glow and is skipped when the previous one was under 0.33 s ago (fast
/// rhythms strobed); the squash stays on every landing.
/// v4 (SPEC §6, package U2): the card is InspectorCard (the cube's speech bubble, built here at boot); opening begins the focus loop on the
/// cube's column (FocusLoop.Begin: the column loops while you edit; it stays after closing until dismissed); Esc closes the card's popover,
/// drawer or selected note before the inspector; Delete / Backspace with a strip note selected deletes that note (DeleteCurrent), the
/// card's tear-off deletes the cube (DeleteCube).
/// v7 (package U2): the Esc of an open shortcut sheet is the sheet's (the card stays).
/// </summary>
[DefaultExecutionOrder(200)]   // after OrbitCamera.LateUpdate: the proxy is posed against this frame's camera
public class CubeInspector : MonoBehaviour
{
    public enum Phase { Closed, Opening, Open, Closing, Popping }

    public const float FlyIn = 0.55f, FlyOut = 0.45f, DimIn = 0.35f, PopSeconds = 0.3f;
    /// <summary>The focus backdrop starts this long into the fly-in; on close it folds back into the cube during Retract before the flight.</summary>
    public const float FocusDelay = 0.22f, Retract = 0.14f;
    public const float TargetDistance = 3.0f, DimDistance = 3.6f, DimAlpha = 0.62f, ScreenShare = 0.24f;
    /// <summary>F11: the landing flash's glow add is capped here and a new flash needs this many seconds since the last one.</summary>
    public const float FlashAddMax = 0.3f, FlashGap = 0.33f;
    public const float HeroTilt = 24f, HeroYaw = 38f, IdleSpin = 12f, FloatAmp = 0.05f, FloatHz = 0.6f;
    public static readonly Vector2 TargetViewport = new Vector2(0.28f, 0.52f);
    static readonly Color DimColor = new Color(0.02f, 0.01f, 0.05f, 1f);
    /// <summary>Projected height of the hero-posed unit cube (tilt 24°, yaw 38°) in edge lengths: the proxy edge = share × view height / this.</summary>
    const float HeroSpan = 1.485f;
    const string LockId = "inspector";

    public static CubeInspector I;
    static Phase phase = Phase.Closed;
    static AudioCube current;

    /// <summary>The inspector is on screen for a cube: opening, open or flying back.</summary>
    public static bool IsOpen => current != null && (phase == Phase.Opening || phase == Phase.Open || phase == Phase.Closing);
    /// <summary>The inspected cube (null when closed).</summary>
    public static AudioCube Current => IsOpen ? current : null;
    public static Phase State => phase;
    /// <summary>The card should be in (opening or open; it slides out while the proxy flies back or pops).</summary>
    public static bool CardWanted => current != null && (phase == Phase.Opening || phase == Phase.Open);
    /// <summary>The proxy (null when none).</summary>
    public static Transform Proxy => I != null ? I.proxy : null;
    /// <summary>The dim's fade progress 0..1 (1 = the full 0.62 dim).</summary>
    public static float Dim => I != null ? I.dimA : 0f;
    /// <summary>Seconds since the current phase began (tests: flight midpoints).</summary>
    public static float PhaseTime => I != null ? I.t : 0f;
    /// <summary>The focus backdrop's root (on the camera → cube ray; null when none).</summary>
    public static Transform Focus => I != null && I.focus != null ? I.focus.Root : null;
    /// <summary>The cube radius on screen (px) the focus was sized from this frame.</summary>
    public static float FocusCubeRadiusPx => I != null && I.focus != null ? I.focus.CubeRadiusPx : 0f;
    /// <summary>The proxy's anchor in world space this frame (no float, no squash).</summary>
    public static Vector3 AnchorWorld => I != null && I.cam != null ? I.Anchor() : Vector3.zero;

    public static void Open(AudioCube c)
    {
        if (c == null || PathManager.I == null) return;
        Ensure().DoOpen(c);
    }

    public static void Close() { if (I != null) I.DoClose(false); }
    /// <summary>Closes at once (no flight): the menu / presentation / prompt took the screen, or a test.</summary>
    public static void CloseImmediate() { if (I != null) I.DoClose(true); }
    /// <summary>The Delete / Backspace key while inspecting: the note selected in the rhythm strip is deleted (v4); with no note selected the
    /// cube is (see <see cref="DeleteCube"/>).</summary>
    public static void DeleteCurrent()
    {
        if (IsOpen && InspectorCard.DeleteSelectedNote()) return;
        DeleteCube();
    }

    /// <summary>The card's tear-off: the proxy pops, the cube is deleted (one History entry), no return flight.</summary>
    public static void DeleteCube()
    {
        if (I != null && current != null && (phase == Phase.Opening || phase == Phase.Open)) { I.StartPop(true); return; }
        if (PathManager.I != null) PathManager.I.DeleteSelected();
    }

    /// <summary>The column the last opening asked FocusLoop.Begin for (-1 none; tests).</summary>
    public static int LastFocusColumn { get; private set; } = -1;

    /// <summary>The column the focus loop plays while <paramref name="c"/> is inspected: its island's column; for a Moon cube the column
    /// playing (or parked) now.</summary>
    public static int FocusColumnOf(AudioCube c)
    {
        var sm = SongManager.I;
        if (c == null || sm == null || !sm.HasSong) return -1;
        var isl = c.Island;
        if (isl != null && !isl.IsMoon) return isl.column;
        return Mathf.Max(0, sm.LitColumn);   // a Moon cube plays in every column: the one playing (or parked) now
    }

    /// <summary>Tests: runs the inspector's input step once (Esc through KeyShim; the mouse as it is).</summary>
    public static void RunInputForTest() { if (I != null && (phase == Phase.Opening || phase == Phase.Open)) I.HandleInput(); }

    /// <summary>Tests: one pointer step at a synthetic screen point, exactly as a real mouse frame (left / right buttons;
    /// <paramref name="overUI"/> = the point is over the HUD or the card).</summary>
    public static void SimPointer(Vector3 screen, bool down0, bool held0, bool up0, bool down1 = false, bool up1 = false, bool overUI = false)
    {
        if (I == null || (phase != Phase.Opening && phase != Phase.Open)) return;
        I.Step(new Ptr { pos = screen, down0 = down0, held0 = held0, up0 = up0, down1 = down1, up1 = up1, overUI = overUI }, Mathf.Max(1e-3f, Time.unscaledDeltaTime));
    }
    /// <summary>The proxy's spin offset (deg) and its current drag velocity (tests).</summary>
    public static float SpinYaw => I != null ? I.spinYaw : 0f;
    public static float SpinVelocity => I != null ? I.spinVel : 0f;
    /// <summary>Screen point of the proxy's centre (tests).</summary>
    public static Vector3 ProxyScreen => I != null && I.proxy != null && I.cam != null ? I.cam.WorldToScreenPoint(I.proxy.position) : Vector3.zero;

    /// <summary>The first committed grid edit of each opening raises Onboarding "grid.edited" (called by PathGridView).</summary>
    public static void NoteGridEdited()
    {
        if (I == null || !IsOpen || I.gridEdited) return;
        I.gridEdited = true;
        Onboarding.Notify(Onboarding.Ev.GridEdited);
    }
    /// <summary>A committed necklace / step change (the card and the rhythm hotkeys).</summary>
    public static void NoteRhythmEdited() { if (IsOpen) Onboarding.Notify(Onboarding.Ev.RhythmEdited); }

    public static CubeInspector Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<CubeInspector>();
            if (I == null) I = new GameObject("CubeInspector").AddComponent<CubeInspector>();
        }
        return I;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { phase = Phase.Closed; current = null; I = null; }

    // ------------------------------------------------------------------ state
    Camera cam;
    Transform proxy, dim;
    MeshRenderer proxyRend; Material proxyMat, dimMat, trailMat, burstMat;
    FocusBackdrop focus; float focusOpen = -1f, focusRetract, focusPulse, poseEdge;
    TrailRenderer trail; ParticleSystem burst;
    float t, dimA, lifeT;
    Vector3 fromPos, fromScale; Quaternion fromRot;
    float spinYaw, spinVel, tiltOff, tiltVel, floatT, floatAmp;
    float squashT = 9f, squashKind = 1f, flash, bumpT = 9f, lastFlashT = -9f;
    Color lookCol; int lastInstrument = -1;
    bool guard, lost, gridEdited; int lostId, openSeed; AudioCube lostCube;
    bool pressValid, pressOnProxy, dragging; int pressButton; Vector3 pressPx, dragLast;
    bool ringOpenLast;
    Vector3 lastTargetPos; Quaternion lastTargetRot = Quaternion.identity; Vector3 lastTargetScale = Vector3.one * ProjectConfig.CubeSize;
    SongManager rebuiltSub;
    static Mesh quad;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake() { if (I == null) I = this; }

    void Start()
    {
        InspectorCard.Ensure();    // v4: the card, the rhythm strip and the note-length row (U2) are built at boot, after the HUD
        DurationPicker.Ensure();
    }

    void OnEnable()
    {
        PathManager.OnSelectionChanged += HandleSelection;
        AudioCube.OnLanded += HandleLanded;
    }

    void OnDisable()
    {
        PathManager.OnSelectionChanged -= HandleSelection;
        AudioCube.OnLanded -= HandleLanded;
        if (rebuiltSub != null) { rebuiltSub.OnSongRebuilt -= HandleRebuilt; rebuiltSub = null; }
    }

    void OnDestroy()
    {
        if (phase != Phase.Closed) FinishClose(true);
        Kill(proxyMat); Kill(dimMat); Kill(trailMat); Kill(burstMat);
        if (dim != null) Destroy(dim.gameObject);
        if (burst != null) Destroy(burst.gameObject);
        if (I == this) I = null;
    }

    static void Kill(Object o) { if (o != null) Destroy(o); }

    // ------------------------------------------------------------------ open / close / delete
    void DoOpen(AudioCube c)
    {
        if (phase != Phase.Closed)
        {
            if (c == current && (phase == Phase.Opening || phase == Phase.Open)) return;
            if (phase == Phase.Popping) FinishPop(); else FinishClose(true);   // close then open: never two proxies
        }
        // Menu / Present / Prompt own the screen: exactly one mode at a time (SPEC v3 §1)
        if (Presenter.Active || MainMenu.IsShown || WorldInput.IsLockedBy("menu") || WorldInput.IsLockedBy("present") || WorldInput.IsLockedBy("prompt")) return;
        cam = Camera.main;
        if (cam == null || !c.isFinalized) return;
        EnsureSubscriptions();
        current = c; phase = Phase.Opening; t = 0f; lifeT = 0f;
        lost = false; gridEdited = false;
        openSeed = SongManager.I != null ? SongManager.I.demoSeed : 0;
        guard = true; try { PathManager.I.Select(c, true); } finally { guard = false; }
        WorldInput.Lock(LockId);
        CubeOutline.Hide();
        c.SetWorldVisible(false, true);
        c.SetInspectHighlight(true);
        var ct = c.transform;
        fromPos = ct.position; fromRot = ct.rotation; fromScale = ct.lossyScale;
        lastTargetPos = fromPos; lastTargetRot = fromRot; lastTargetScale = fromScale;
        BuildProxy(c);
        spinYaw = 0f; spinVel = 0f; tiltOff = 0f; tiltVel = 0f; floatT = 0f; floatAmp = 0f;
        squashT = 9f; flash = 0f; bumpT = 9f; dragging = false; pressValid = false;
        lastInstrument = c.instrument;
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.45f, 1.15f);
        int col = FocusColumnOf(c);
        LastFocusColumn = col;
        if (col >= 0) FocusLoop.Begin(col);   // v4 (R4): the cube's column loops while it is inspected (it stays after closing until dismissed)
        Onboarding.Notify(Onboarding.Ev.CubeInspected);
    }

    void DoClose(bool snap)
    {
        if (phase == Phase.Closed) return;
        if (phase == Phase.Popping) { if (snap) FinishPop(); return; }
        if (snap || current == null || proxy == null || cam == null) { FinishClose(true); return; }
        if (phase == Phase.Closing) return;
        phase = Phase.Closing; t = 0f;
        fromPos = proxy.position; fromRot = proxy.rotation; fromScale = proxy.localScale;
        dragging = false; pressValid = false;
        if (trail != null) { trail.Clear(); trail.emitting = true; }
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.35f, 0.82f);
    }

    /// <summary>Arrival of the return flight (or an immediate close): the cube shows again, the lock goes, the cube is deselected.</summary>
    void FinishClose(bool snap)
    {
        var c = current;
        DestroyProxy();
        if (c != null)
        {
            c.SetWorldVisible(true, true);
            c.SetInspectHighlight(false);
            if (!snap)
            {
                c.Squash(1f);
                var tile = c.CurrentOrHomeTile;
                if (tile != null) { tile.Press(1f); Fx.Ripple(tile.Top, c.Color, 1f, 0.45f); }
                AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.05f);
            }
        }
        WorldInput.Unlock(LockId);
        guard = true;
        try { if (PathManager.I != null && PathManager.I.selectedCube != null && (c == null || PathManager.I.selectedCube == c)) PathManager.I.Deselect(); }
        finally { guard = false; }
        current = null; phase = Phase.Closed; lost = false; lostCube = null;
        if (snap) dimA = 0f;
        Onboarding.Notify(Onboarding.Ev.InspectorClosed);
    }

    /// <summary>The proxy pops (scale-out + burst) and the inspector closes without a return flight; <paramref name="deleteCube"/>
    /// deletes the inspected cube first (the card's trash, Delete key).</summary>
    void StartPop(bool deleteCube)
    {
        var c = current;
        if (deleteCube && c != null)
        {
            guard = true;
            try { if (PathManager.I != null) PathManager.I.DeleteCube(c, false); }   // deselects + recomputes + one History entry
            finally { guard = false; }
        }
        else if (c != null) { c.SetWorldVisible(true, true); c.SetInspectHighlight(false); }
        current = null;
        phase = Phase.Popping; t = 0f; lost = false; lostCube = null;
        if (proxy != null)
        {
            Burst(proxy.position, lookCol, 26, 1.2f);
            if (trail != null) trail.emitting = false;
        }
        AudioPool.UI(ProceduralAudio.Pop(), 0.55f, 0.95f);
    }

    void FinishPop()
    {
        DestroyProxy();
        WorldInput.Unlock(LockId);
        current = null; phase = Phase.Closed;
        Onboarding.Notify(Onboarding.Ev.InspectorClosed);
    }

    void Retarget(AudioCube c)
    {
        current = c; lost = false; lostCube = null;
        guard = true; try { PathManager.I.Select(c, true); } finally { guard = false; }
        c.SetWorldVisible(false, true);
        c.SetInspectHighlight(true);
        if (c.instrument != lastInstrument) { lastInstrument = c.instrument; bumpT = 0f; if (proxy != null) Burst(proxy.position, c.Color, 14, 0.8f); }
    }

    // ------------------------------------------------------------------ world events
    void HandleSelection(AudioCube c)
    {
        if (guard) return;
        if (c != null)
        {
            if (c == current && (phase == Phase.Opening || phase == Phase.Open)) return;
            DoOpen(c);   // a selection made elsewhere opens the inspector: selection ⇔ inspector
            return;
        }
        if ((phase == Phase.Opening || phase == Phase.Open) && current != null)
        {
            lost = true; lostId = current.id; lostCube = current;   // an undo / rebuild re-finds it by id; otherwise it was a deselect
        }
    }

    void HandleRebuilt() { if (lost) ResolveLost(true); }

    void ResolveLost(bool rebuilt)
    {
        if (!lost) return;
        lost = false;
        if (phase != Phase.Opening && phase != Phase.Open) return;
        var sm = SongManager.I;
        AudioCube found = null;
        if (rebuilt && lostId != 0 && sm != null && sm.demoSeed == openSeed)
            foreach (var c in SequenceMaster.Cubes) if (c != null && c != lostCube && c.isFinalized && c.id == lostId) { found = c; break; }
        if (found != null) { Retarget(found); return; }
        if (!rebuilt && lostCube != null && SequenceMaster.Cubes.Contains(lostCube)) { current = lostCube; DoClose(false); return; }   // a plain deselect: fly back
        StartPop(false);   // the cube is gone (deleted, or an undo removed it): no return flight
    }

    void HandleLanded(AudioCube c, int w, int k, AudioCube.Hit hit)
    {
        if (c == null || c != current || phase == Phase.Closed) return;
        squashT = 0f;
        squashKind = hit.mod == 1 ? 0.45f : (hit.weight == 0 ? 0.6f : (hit.weight == 2 ? 1.3f : 1f));
        if (hit.fires && Time.unscaledTime - lastFlashT >= FlashGap)   // F11: no strobe on fast rhythms (the squash still lands)
        {
            lastFlashT = Time.unscaledTime;
            flash = Mathf.Max(flash, hit.weight == 2 ? 1.4f : (hit.weight == 0 ? 0.55f : 1f));
        }
        focusPulse = Mathf.Max(focusPulse, hit.fires ? (hit.weight == 2 ? 1.3f : 1f) : 0.4f);   // the focus lines kick outward
    }

    void EnsureSubscriptions()
    {
        var sm = SongManager.I;
        if (sm == rebuiltSub) return;
        if (rebuiltSub != null) rebuiltSub.OnSongRebuilt -= HandleRebuilt;
        rebuiltSub = sm;
        if (sm != null) sm.OnSongRebuilt += HandleRebuilt;
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        EnsureSubscriptions();
        if (phase == Phase.Closed) return;
        if (Presenter.Active || MainMenu.IsShown || WorldInput.IsLockedBy("menu") || WorldInput.IsLockedBy("present") || WorldInput.IsLockedBy("prompt"))
        {
            if (phase == Phase.Popping) FinishPop(); else FinishClose(true);
            return;
        }
        if (lost) ResolveLost(false);
        if (phase == Phase.Opening || phase == Phase.Open)
        {
            if (current == null) { StartPop(false); return; }
            HandleInput();
        }
    }

    struct Ptr { public Vector3 pos; public bool down0, held0, up0, down1, up1, overUI; }

    void HandleInput()
    {
        if (!InputUtil.TypingInField && KeyShim.Down(KeyCode.Escape) && !HudShortcuts.OwnsKeysThisFrame)
        {
            // v4: the card's popover, drawer or selected note close first (a grid sticker ring / HUD ring closes itself on Esc)
            bool others = PathGridView.AnyPopoverOpen || (UIManager.I != null && (UIManager.I.OpenRing != null || UIManager.I.WheelOpen));
            if (!others && InspectorCard.ConsumeEscape()) return;
            if (!ringOpenLast) { DoClose(false); return; }
        }
        Step(new Ptr
        {
            pos = Input.mousePosition, down0 = Input.GetMouseButtonDown(0), held0 = Input.GetMouseButton(0), up0 = Input.GetMouseButtonUp(0),
            down1 = Input.GetMouseButtonDown(1), up1 = Input.GetMouseButtonUp(1), overUI = InputUtil.PointerOverUI
        }, Mathf.Max(1e-3f, Time.unscaledDeltaTime));
    }

    /// <summary>The pointer while open: a drag on the proxy spins it (inertia on release), a click on it pokes it, a click or
    /// right-click on the dim (not the HUD, the card or the proxy) sends the cube back.</summary>
    void Step(Ptr p, float dt)
    {
        Vector3 m = p.pos;
        if (p.down0 || p.down1)
        {
            pressButton = p.down0 ? 0 : 1;
            pressPx = m;
            pressValid = !p.overUI && !ringOpenLast;
            pressOnProxy = pressValid && OverProxy(m);
            dragging = false;
        }
        if (pressValid && pressButton == 0 && pressOnProxy && p.held0)
        {
            if (!dragging && (m - pressPx).magnitude > OrbitCamera.DragThresholdPx) { dragging = true; dragLast = pressPx; }
            if (dragging)
            {
                Vector3 d = m - dragLast; dragLast = m;
                spinYaw -= d.x * 0.45f;
                spinVel = Mathf.Lerp(spinVel, -d.x * 0.45f / dt, 0.5f);
                tiltOff = Mathf.Clamp(tiltOff - d.y * 0.3f, -40f, 40f);   // the face under the cursor follows it (trackball)
            }
        }
        bool up = pressButton == 0 ? p.up0 : p.up1;
        if (pressValid && up)
        {
            pressValid = false;
            bool click = (m - pressPx).magnitude <= OrbitCamera.DragThresholdPx;
            if (dragging) dragging = false;                             // the spin keeps its momentum
            else if (click && pressOnProxy && pressButton == 0) Poke();
            else if (click && !pressOnProxy) DoClose(false);            // a click / right-click on the dim sends the cube back
        }
    }

    /// <summary>A click on the proxy: it bounces and plays the note it stands on.</summary>
    void Poke()
    {
        var c = current; if (c == null) return;
        squashT = 0f; squashKind = 0.9f;
        var tile = c.CurrentOrHomeTile;
        if (tile == null) return;
        // v7 (A, SPEC v7 §4.1): the note as the cube sounds it — an octave copy in its layer, a harmony cube with its bend
        int node = Mathf.Max(0, c.NodeIndexOf(tile));
        if (!VoiceRules.Audition(VoiceRules.AuditionEvent(c, node, 0.35, 0.85f, 0.0, tile))) tile.PlayPreview(c.instrument, c.octave, c.voice);
        else tile.Press(0.6f);
    }

    bool OverProxy(Vector3 screen)
    {
        if (proxy == null || cam == null) return false;
        Vector3 s = cam.WorldToScreenPoint(proxy.position);
        if (s.z <= 0f) return false;
        float pxPerUnit = Screen.height / (2f * s.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        float r = proxy.localScale.x * 0.95f * pxPerUnit + 12f;
        return (new Vector2(screen.x, screen.y) - new Vector2(s.x, s.y)).magnitude <= r;
    }

    void LateUpdate()
    {
        ringOpenLast = PathGridView.AnyPopoverOpen || InspectorCard.PopoverOpen || (UIManager.I != null && (UIManager.I.OpenRing != null || UIManager.I.WheelOpen));
        float dt = Time.deltaTime;
        if (phase == Phase.Closed) { UpdateDim(dt); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null) { FinishClose(true); return; }
        t += dt; lifeT += dt;
        focusPulse *= Mathf.Exp(-dt * 7f);
        switch (phase)
        {
            case Phase.Opening:
                focusOpen = t - FocusDelay;
                PoseOpening();
                if (t >= FlyIn) { phase = Phase.Open; t = 0f; if (trail != null) trail.emitting = false; }
                break;
            case Phase.Open:
                focusOpen += dt;
                PoseOpen(dt);
                break;
            case Phase.Closing:
                focusRetract = Mathf.Clamp01(t / Retract);
                PoseClosing();
                if (t >= Retract + FlyOut) { FinishClose(false); UpdateDim(dt); return; }
                break;
            case Phase.Popping:
                focusRetract = Mathf.Clamp01(t / 0.12f);
                PosePopping();
                if (t >= PopSeconds) { FinishPop(); UpdateDim(dt); return; }
                break;
        }
        UpdateLook(dt);
        PoseFocus(dt);
        UpdateDim(dt);
    }

    Vector3 Anchor() => cam.ViewportToWorldPoint(new Vector3(TargetViewport.x, TargetViewport.y, TargetDistance));
    float HeroSize() => ScreenShare * 2f * TargetDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / HeroSpan;
    Quaternion Hero(float yaw, float tilt) => cam.transform.rotation * Quaternion.Euler(-(HeroTilt + tilt), 0f, 0f) * Quaternion.Euler(0f, HeroYaw + yaw, 0f);
    static float InOutSine(float x) => 0.5f - 0.5f * Mathf.Cos(Mathf.Clamp01(x) * Mathf.PI);
    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u) { float v = 1f - u; return v * v * a + 2f * v * u * b + u * u * c; }

    void PoseOpening()
    {
        float u = Mathf.Clamp01(t / FlyIn);
        float pe = Ease.OutCubic(u), re = InOutSine(u);
        Vector3 pos = Bezier(fromPos, fromPos + Vector3.up * 1.6f, Anchor(), pe);
        Quaternion rot = Quaternion.AngleAxis(360f * re, cam.transform.up) * Quaternion.Slerp(fromRot, Hero(0f, 0f), re);
        Vector3 scale = Vector3.Lerp(fromScale, Vector3.one * HeroSize(), pe);
        Apply(pos, rot, scale, true);
    }

    void PoseOpen(float dt)
    {
        if (!dragging)
        {
            spinVel *= Mathf.Exp(-dt / 0.6f);                                  // drag inertia decays into the idle turn
            float k = 1f - Mathf.Exp(-dt * 9f);                                  // the tilt springs back
            tiltVel = Mathf.Lerp(tiltVel, -tiltOff * 10f, k);
            tiltOff += tiltVel * dt;
            spinYaw += (IdleSpin + spinVel) * dt;
        }
        floatT += dt;
        floatAmp = Mathf.Min(1f, floatAmp + dt / 0.8f);                           // eases in after the landing (no bump at arrival)
        Vector3 pos = Anchor() + cam.transform.up * (Mathf.Sin(floatT * FloatHz * 2f * Mathf.PI) * FloatAmp * Ease.OutCubic(floatAmp));
        Apply(pos, Hero(spinYaw, tiltOff), Vector3.one * HeroSize(), true);
    }

    void PoseClosing()
    {
        var c = current;
        if (c != null)
        {
            var ct = c.transform;
            lastTargetPos = ct.position; lastTargetRot = ct.rotation; lastTargetScale = ct.lossyScale;   // re-targeted every frame
        }
        if (t < Retract)
        {
            // the focus lines and the halo fold back into the cube first; the cube dips a little in anticipation
            Apply(fromPos, fromRot, fromScale * (1f - 0.07f * Mathf.Sin(Mathf.Clamp01(t / Retract) * Mathf.PI)), false);
            return;
        }
        float u = Mathf.Clamp01((t - Retract) / FlyOut);
        float e = Ease.InOutCubic(u);
        Vector3 mid = Vector3.Lerp(fromPos, lastTargetPos, 0.55f) + Vector3.up * 1.1f;
        Vector3 pos = Bezier(fromPos, mid, lastTargetPos, e);
        Quaternion rot = Quaternion.Slerp(fromRot, lastTargetRot, e);
        Vector3 scale = Vector3.Lerp(fromScale, lastTargetScale, e);
        Apply(pos, rot, scale, false);
    }

    void PosePopping()
    {
        if (proxy == null) return;
        float u = Mathf.Clamp01(t / PopSeconds);
        float s = u < 0.3f ? 1f + 0.25f * Ease.OutCubic(u / 0.3f) : 1.25f * (1f - Ease.InCubic((u - 0.3f) / 0.7f));
        proxy.localScale = Vector3.one * HeroSize() * Mathf.Max(0.001f, s);
        poseEdge = HeroSize();
    }

    /// <summary>Poses the proxy (landing squash, recolour bump; never nearer than the near plane allows).</summary>
    void Apply(Vector3 pos, Quaternion rot, Vector3 scale, bool squash)
    {
        if (proxy == null) return;
        float dt = Time.deltaTime;
        poseEdge = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));   // the focus is sized from the unsquashed cube (landings kick its lines instead)
        squashT += dt; bumpT += dt;
        if (squash)
        {
            float w = Ease.Wobble(squashT, 17f, 7.5f);
            float sy = 1f - 0.3f * w * squashKind, sxz = 1f + 0.18f * w * squashKind;
            float bump = bumpT < 0.3f ? 1f + 0.2f * (1f - Ease.OutCubic(bumpT / 0.3f)) : 1f;
            scale = new Vector3(scale.x * sxz, scale.y * sy, scale.z * sxz) * bump;
        }
        // never clipped by the near plane: keep the proxy at least near + its half diagonal in front of the camera
        Vector3 local = cam.transform.InverseTransformPoint(pos);
        float minZ = cam.nearClipPlane + scale.magnitude * 0.6f + 0.05f;
        if (local.z < minZ) { local.z = minZ; pos = cam.transform.TransformPoint(local); }
        proxy.SetPositionAndRotation(pos, rot);
        proxy.localScale = scale;
        if (trail != null) trail.widthMultiplier = scale.x * 0.55f;
    }

    /// <summary>The focus backdrop follows the proxy every frame: centred on the camera → cube ray, sized from the cube on screen.</summary>
    void PoseFocus(float dt)
    {
        if (focus == null || proxy == null || cam == null) return;
        var c = current;
        float vol = c != null ? c.volume : 1f;
        bool muted = c != null && (c.muted || Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)]);
        float haloAlpha = muted ? 0.12f : 0.2f + 0.16f * vol + 0.08f * flash;    // the halo glows with the volume
        focus.Pose(cam, proxy.position, poseEdge, focusOpen, focusRetract, focusPulse, lookCol, haloAlpha, dt);
    }

    void UpdateLook(float dt)
    {
        var c = current;
        if (proxyMat == null) return;
        Color want = lookCol; float vol = 1f; bool muted = false;
        if (c != null)
        {
            want = c.Color;
            vol = c.volume;
            muted = c.muted || Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)];
            if (muted) want = Color.Lerp(want, new Color(0.55f, 0.55f, 0.62f), 0.75f);
            if (c.instrument != lastInstrument)
            {
                lastInstrument = c.instrument;
                bumpT = 0f;
                if (proxy != null) Burst(proxy.position, c.Color, 22, 0.9f);   // recolour burst
                AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.2f);
            }
        }
        lookCol = Color.Lerp(lookCol, want, 1f - Mathf.Exp(-dt * 12f));
        flash *= Mathf.Exp(-dt * 7f);
        float em = muted ? 0.015f : 0.04f + 0.2f * vol;                                  // glow by volume (low: the lit faces keep their shape under the bloom)
        proxyMat.SetColor(BaseColorId, lookCol);
        proxyMat.SetColor(EmissionId, lookCol * (em + FlashAdd));
        if (trail != null) { trail.startColor = Palette.A(lookCol, 0.75f); trail.endColor = Palette.A(lookCol, 0f); }
    }

    /// <summary>F11: the landing flash's share of the proxy glow (0..0.3).</summary>
    float FlashAdd => Mathf.Min(FlashAddMax, flash * FlashAddMax);
    /// <summary>The glow the landing flash adds right now (tests: never above FlashAddMax).</summary>
    public static float FlashGlow => I != null ? I.FlashAdd : 0f;

    void UpdateDim(float dt)
    {
        bool want = phase == Phase.Opening || phase == Phase.Open;
        float target = want ? 1f : 0f;
        float seconds = want ? DimIn : (phase == Phase.Popping ? PopSeconds : Retract + FlyOut);
        dimA = Mathf.MoveTowards(dimA, target, dt / seconds);
        if (dimA <= 0f) { if (dim != null && dim.gameObject.activeSelf) dim.gameObject.SetActive(false); return; }
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        EnsureDim();
        if (dim.parent != cam.transform) dim.SetParent(cam.transform, false);
        if (!dim.gameObject.activeSelf) dim.gameObject.SetActive(true);
        float h = 2f * DimDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.3f;
        dim.localPosition = new Vector3(0f, 0f, DimDistance);
        dim.localRotation = Quaternion.identity;
        dim.localScale = new Vector3(h * Mathf.Max(1f, cam.aspect) * 1.3f, h, 1f);
        // 0.62 is meant as seen: the project blends in linear space, where 0.62 would read as a ~35 % dim, so the alpha is lifted to match
        float a = DimAlpha * Mathf.SmoothStep(0f, 1f, dimA);
        if (QualitySettings.activeColorSpace == ColorSpace.Linear) a = 1f - Mathf.Pow(1f - a, 2.2f);
        dimMat.SetColor(ColorId, new Color(DimColor.r, DimColor.g, DimColor.b, a));
    }

    // ------------------------------------------------------------------ objects
    void BuildProxy(AudioCube c)
    {
        DestroyProxy();
        lookCol = c.Color;
        var go = new GameObject("InspectorProxy");
        go.AddComponent<MeshFilter>().sharedMesh = c.sphere ? AudioCube.BallMesh : MeshFactory.RoundedBox(Vector3.one, 0.14f, 6);   // SPHERE: a ball
        proxyRend = go.AddComponent<MeshRenderer>();
        Kill(proxyMat);
        proxyMat = Fx.Lit(lookCol, 0.7f, 0.05f);
        if (c.sphere && proxyMat.HasProperty("_FaceSnap")) proxyMat.SetFloat("_FaceSnap", 0f);
        proxyMat.renderQueue = 3700;   // after the dim (3400) and the highlighted path (3500): never dimmed, never z-fighting it
        proxyRend.sharedMaterial = proxyMat;
        proxyRend.shadowCastingMode = ShadowCastingMode.Off; proxyRend.receiveShadows = false;
        proxy = go.transform;
        proxy.SetPositionAndRotation(fromPos, fromRot);
        proxy.localScale = fromScale;
        // trail
        trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.16f; trail.minVertexDistance = 0.02f; trail.widthMultiplier = 0.25f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        Kill(trailMat);
        trailMat = Fx.Additive(IconFactory.GetTexture("white"), Color.white, 1f);   // a flat tapering ribbon
        trailMat.renderQueue = 3650;
        trail.material = trailMat;
        trail.startColor = Palette.A(lookCol, 0.75f); trail.endColor = Palette.A(lookCol, 0f);
        trail.shadowCastingMode = ShadowCastingMode.Off; trail.receiveShadows = false;
        trail.alignment = LineAlignment.View;
        trail.emitting = true;
        // the comic focus behind the proxy: manga focus lines over a Ben-Day halo, centred on the cube every frame
        focus = new FocusBackdrop(c.id * 7919 + Time.frameCount);
        focusOpen = -FocusDelay; focusRetract = 0f; focusPulse = 0f; poseEdge = Mathf.Max(fromScale.x, Mathf.Max(fromScale.y, fromScale.z));
    }

    void DestroyProxy()
    {
        if (proxy != null) Destroy(proxy.gameObject);
        if (focus != null) { focus.Destroy(); focus = null; }
        proxy = null; trail = null; proxyRend = null;
        Kill(proxyMat); Kill(trailMat);
        proxyMat = null; trailMat = null;
    }

    void EnsureDim()
    {
        if (dim != null) return;
        var go = new GameObject("InspectorDim");
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var mr = go.AddComponent<MeshRenderer>();
        dimMat = Fx.Alpha(IconFactory.GetTexture("white"), new Color(DimColor.r, DimColor.g, DimColor.b, 0f));
        dimMat.renderQueue = 3400;   // after the world's transparents (ripples, pillars, cables): they dim too
        mr.sharedMaterial = dimMat;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        dim = go.transform;
        go.SetActive(false);
    }

    void Burst(Vector3 pos, Color c, int count, float speed)
    {
        if (burst == null) BuildBurst();
        float size = proxy != null ? proxy.localScale.x : 0.4f;
        for (int i = 0; i < count; i++)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + Random.insideUnitSphere * size * 0.3f,
                velocity = Random.onUnitSphere * Random.Range(0.4f, 1f) * speed,
                startColor = Color.Lerp(c, Color.white, Random.Range(0f, 0.4f)),
                startSize = Random.Range(0.03f, 0.075f),
                startLifetime = Random.Range(0.3f, 0.6f)
            };
            burst.Emit(ep, 1);
        }
    }

    void BuildBurst()
    {
        var go = new GameObject("InspectorBurst");
        burst = go.AddComponent<ParticleSystem>();
        var main = burst.main;
        main.startLifetime = 0.5f; main.startSpeed = 0f; main.startSize = 0.05f; main.gravityModifier = 0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400; main.playOnAwake = false; main.loop = false;
        var em = burst.emission; em.enabled = false;
        var sh = burst.shape; sh.enabled = false;
        var col = burst.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var sz = burst.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        burstMat = Fx.Additive(IconFactory.GetTexture("dot"), Color.white, 1.2f);   // flat confetti
        burstMat.renderQueue = 3750;
        r.material = burstMat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }

    /// <summary>Unit quad in the XY plane centred on its pivot (the dim and the focus halo face the camera).</summary>
    static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "inspector_quad" };
        quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateBounds();
        return quad;
    }

    // ------------------------------------------------------------------ the comic focus behind the cube
    /// <summary>
    /// The comic focus behind the inspected cube (SPEC v3 §2.2 + the §7 look): manga focus lines — thin tapered ink strokes of
    /// varied length and weight that point AT the cube and leave a clear circle around it (the darkened, saturated instrument
    /// colour, a few white accents), boiling on twos (re-jittered 10 times a second) and turning very slowly — over a Ben-Day
    /// halftone halo in the instrument colour (dots largest at the cube, shrinking to nothing at a clean circle with a thin ink
    /// ring). Everything lies in one plane parallel to the image plane whose centre is ON the camera → cube ray, so it projects as
    /// circles centred exactly on the cube, and it is sized every frame from the cube's size on screen. All materials come from
    /// Fx.Alpha (one colour each: no vertex colours), the textures and meshes are generated here.
    /// </summary>
    sealed class FocusBackdrop
    {
        const int Strokes = 38, AccentCount = 6, TexSize = 512, RingSegs = 128;
        const float Clear = 1.3f, HaloRadius = 1.72f, BackOffset = 0.35f;   // radii in cube radii; the plane sits BackOffset cube edges behind the cube
        const float BoilHz = 10f, SpinDegPerSec = 2.5f;
        static Texture2D haloTex; static Mesh ringMesh;

        public readonly Transform Root;
        /// <summary>The cube's radius on screen (px) the backdrop was sized from this frame.</summary>
        public float CubeRadiusPx { get; private set; }
        readonly Transform haloT, ringT;
        readonly Material haloMat, ringMat, inkMat, whiteMat;
        readonly Mesh inkMesh, whiteMesh;
        readonly Vector3[] inkV = new Vector3[Strokes * 3], whiteV = new Vector3[Strokes * 3];
        readonly float[] ang = new float[Strokes], rIn = new float[Strokes], rOut = new float[Strokes], wid = new float[Strokes], lag = new float[Strokes];
        readonly bool[] accent = new bool[Strokes];
        readonly float[] jA = new float[Strokes], jIn = new float[Strokes], jOut = new float[Strokes], jW = new float[Strokes];
        readonly System.Random rng;
        float boilT, spin;

        public FocusBackdrop(int seed)
        {
            rng = new System.Random(seed);
            Root = new GameObject("InspectorFocus").transform;
            var white = IconFactory.GetTexture("white");
            haloMat = Fx.Alpha(HaloTexture(), Color.white); haloMat.renderQueue = 3640;   // after the dim (3400) and the cube's path (3500), before the proxy (3700)
            ringMat = Fx.Alpha(white, Color.white); ringMat.renderQueue = 3641;
            inkMat = Fx.Alpha(white, Color.white); inkMat.renderQueue = 3644;
            whiteMat = Fx.Alpha(white, Color.white); whiteMat.renderQueue = 3645;
            haloT = Child("Halo", Quad(), haloMat);
            ringT = Child("Ring", RingMesh(), ringMat);
            inkMesh = StrokeMesh("ink"); whiteMesh = StrokeMesh("accent");
            Child("Lines", inkMesh, inkMat);
            Child("Accents", whiteMesh, whiteMat);
            haloT.localScale = Vector3.zero; ringT.localScale = Vector3.zero;
            Design();
            Boil();
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Kill(haloMat); Kill(ringMat); Kill(inkMat); Kill(whiteMat); Kill(inkMesh); Kill(whiteMesh);
        }

        Transform Child(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        static Mesh StrokeMesh(string name)
        {
            var m = new Mesh { name = "inspector_focus_" + name };
            m.MarkDynamic();
            m.vertices = new Vector3[Strokes * 3];
            var tris = new int[Strokes * 3];
            for (int i = 0; i < tris.Length; i++) tris[i] = i;
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 12f);
            return m;
        }

        float R01() => (float)rng.NextDouble();

        /// <summary>This opening's strokes: evenly spread with a little scatter, varied inner end (all outside the clear circle),
        /// length and weight; seven thinner, shorter white accents spread around.</summary>
        void Design()
        {
            int placed = 0, guard = 0;
            while (placed < AccentCount && guard++ < 400)
            {
                int k = rng.Next(Strokes);
                if (accent[k] || accent[(k + 1) % Strokes] || accent[(k + Strokes - 1) % Strokes]) continue;
                accent[k] = true; placed++;
            }
            for (int i = 0; i < Strokes; i++)
            {
                ang[i] = (i + R01() * 0.7f - 0.35f) * (360f / Strokes);
                rIn[i] = Clear + R01() * 0.34f;
                float len = accent[i] ? 0.34f + R01() * 0.42f : 0.36f + R01() * R01() * 0.95f;
                rOut[i] = rIn[i] + len;
                wid[i] = accent[i] ? 0.018f + R01() * 0.014f : 0.022f + R01() * 0.05f;
                lag[i] = R01();
            }
        }

        /// <summary>On twos: every stroke's angle, ends and weight re-jitter a little.</summary>
        void Boil()
        {
            for (int i = 0; i < Strokes; i++)
            {
                jA[i] = (R01() * 2f - 1f) * 1.4f;
                jIn[i] = (R01() * 2f - 1f) * 0.045f;
                jOut[i] = (R01() * 2f - 1f) * 0.11f;
                jW[i] = 0.85f + R01() * 0.3f;
            }
        }

        /// <param name="open">seconds since the focus started (the lines shoot out from the centre over ~0.3 s, the halo scales up
        /// with a slight overshoot over 0.35 s); negative = not yet</param>
        /// <param name="retract">0..1: everything folds back into the cube</param>
        /// <param name="pulse">landing kick (decays in the caller)</param>
        public void Pose(Camera cam, Vector3 centre, float edge, float open, float retract, float pulse, Color col, float haloAlpha, float dt)
        {
            var ct = cam.transform;
            Vector3 camPos = ct.position, fwd = ct.forward;
            Vector3 ray = centre - camPos;
            float dist = ray.magnitude;
            if (dist < 1e-4f) return;
            Vector3 pos = camPos + ray * ((dist + edge * BackOffset) / dist);   // on the camera → cube ray, a little behind the cube
            Root.SetPositionAndRotation(pos, ct.rotation);                         // parallel to the image plane: circles stay circles, centred on the cube
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float zCube = Mathf.Max(1e-3f, Vector3.Dot(ray, fwd)), zBack = Mathf.Max(1e-3f, Vector3.Dot(pos - camPos, fwd));
            // the posed cube's visual radius on screen (0.8 edge: between its half extent and its circumradius; rotation-invariant, so the
            // backdrop does not pump with the turntable), then the world radius that shows that many pixels at the backdrop's depth
            float rPx = edge * 0.8f * cam.pixelHeight / (2f * zCube * tanHalf);
            CubeRadiusPx = rPx;
            float worldR = rPx * 2f * zBack * tanHalf / Mathf.Max(1f, cam.pixelHeight);
            Root.localScale = new Vector3(worldR, worldR, worldR);

            // halo + ink ring: scale up with a slight overshoot, fold back on retract
            float grow = open <= 0f ? 0f : Ease.OutBack(Mathf.Clamp01(open / 0.35f), 1.3f);
            float hr = HaloRadius * Mathf.Max(0f, grow) * (1f - Mathf.SmoothStep(0f, 1f, retract)) * (1f + 0.025f * pulse);
            haloT.localScale = new Vector3(hr * 2f, hr * 2f, 1f);
            ringT.localScale = new Vector3(hr, hr, 1f);
            float h, s, v;
            Color.RGBToHSV(col, out h, out s, out v);
            Color ink = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.2f + 0.15f), Mathf.Clamp01(v * 0.52f));   // darkened, more saturated: an ink of the cube's colour
            haloMat.SetColor(ColorId, Palette.A(Color.Lerp(col, Color.white, 0.1f), haloAlpha));
            ringMat.SetColor(ColorId, Palette.A(ink, 0.9f));
            inkMat.SetColor(ColorId, Palette.A(ink, 0.9f));
            whiteMat.SetColor(ColorId, Palette.A(Color.white, 0.88f));

            // lines: boil on twos, turn slowly, shoot out from the centre, kick outward on landings, fold back into the cube
            boilT += dt;
            if (boilT >= 1f / BoilHz) { boilT -= 1f / BoilHz; if (boilT > 1f / BoilHz) boilT = 0f; Boil(); }
            spin += SpinDegPerSec * dt;
            float fold = 1f - Mathf.SmoothStep(0f, 1f, retract);
            for (int i = 0; i < Strokes; i++)
            {
                float e = open <= 0f ? 0f : Ease.OutCubic(Mathf.Clamp01((open - lag[i] * 0.08f) / 0.3f)) * fold;
                float a = (ang[i] + jA[i] + spin) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                float r0 = (rIn[i] + jIn[i]) * (1f + 0.05f * pulse) * e;
                float r1 = (rOut[i] + jOut[i]) * (1f + 0.16f * pulse) * e;
                float w = wid[i] * jW[i] * (0.35f + 0.65f * e) * 0.5f;
                var arr = accent[i] ? whiteV : inkV; var other = accent[i] ? inkV : whiteV;
                int k = i * 3;
                arr[k] = new Vector3(dx * r0, dy * r0, 0f);                                   // the point, aimed at the cube
                arr[k + 1] = new Vector3(dx * r1 - dy * w, dy * r1 + dx * w, 0f);             // the broad outer end
                arr[k + 2] = new Vector3(dx * r1 + dy * w, dy * r1 - dx * w, 0f);
                other[k] = other[k + 1] = other[k + 2] = Vector3.zero;
            }
            inkMesh.vertices = inkV;
            whiteMesh.vertices = whiteV;
        }

        /// <summary>The Ben-Day halo (512 px, mipmapped): a 45° dot grid whose dots are largest inside the cube's radius and shrink
        /// to nothing toward a clean circle.</summary>
        static Texture2D HaloTexture()
        {
            if (haloTex != null) return haloTex;
            const int N = TexSize;
            float half = N * 0.5f, pitch = N / 24f, maxR = pitch * 0.46f;
            float inner = 1f / HaloRadius;               // the cube's radius in halo units: dots are largest up to here
            const float c45 = 0.70710678f;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x + 0.5f - half, v = y + 0.5f - half;
                    float ru = (u + v) * c45, rv = (v - u) * c45;
                    float cu = Mathf.Round(ru / pitch) * pitch, cv = Mathf.Round(rv / pitch) * pitch;
                    float dd = Mathf.Sqrt((ru - cu) * (ru - cu) + (rv - cv) * (rv - cv));
                    float dr = Mathf.Sqrt(cu * cu + cv * cv) / half;
                    float k = Mathf.Clamp01((dr - inner * 0.9f) / (0.93f - inner * 0.9f));
                    float rad = dr >= 0.93f ? 0f : maxR * Mathf.Pow(1f - k, 1.9f);   // largest at the cube, gone well before the rim
                    float a = Mathf.Clamp01(rad - dd + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            haloTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "inspector_bendday", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            haloTex.SetPixels32(px);
            haloTex.Apply(true, false);
            return haloTex;
        }

        /// <summary>A thin ring (unit outer radius) in the XY plane: the ink edge of the halo.</summary>
        static Mesh RingMesh()
        {
            if (ringMesh != null) return ringMesh;
            const float r0 = 0.988f, r1 = 1f;
            var v = new Vector3[RingSegs * 2]; var t = new int[RingSegs * 6];
            for (int i = 0; i < RingSegs; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegs, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                v[i * 2] = new Vector3(cs * r0, sn * r0, 0f);
                v[i * 2 + 1] = new Vector3(cs * r1, sn * r1, 0f);
                int j = (i + 1) % RingSegs, k = i * 6;
                t[k] = i * 2; t[k + 1] = j * 2; t[k + 2] = i * 2 + 1;
                t[k + 3] = i * 2 + 1; t[k + 4] = j * 2; t[k + 5] = j * 2 + 1;
            }
            ringMesh = new Mesh { name = "inspector_focus_ring", vertices = v, triangles = t };
            ringMesh.RecalculateBounds();
            return ringMesh;
        }
    }
}
