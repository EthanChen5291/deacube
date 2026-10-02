using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Island-based, low-friction camera rig (SPEC v3 §3.4). The camera never chases the playhead: it moves only when you move it
/// or ask it to frame something (a click on an island's platform/hub, Tab / Shift+Tab, C reset view, O frame all, a tray placement),
/// or — with F (follow) on — continuously with the playhead while playing (v8; see the follow block in LateUpdate).
/// Pointer: left-drag on empty sea or middle-drag = grab-the-ground pan with no smoothing lag (the grabbed point stays exactly
/// under the cursor), release = inertia (velocity of the last 0.08 s, decay τ 0.35 s); right-drag orbits directly with yaw
/// inertia (τ 0.3 s); the wheel zooms ×0.88 / ×1.14 per notch toward the cursor (16/s); trackpad sideways scroll and
/// Shift+wheel pan. Keys: WASD / arrows pan (0.9 × distance per second, 0.12 s ramp). Soft bounds (60 u past the song, 0.8 s
/// spring) bring a lost view back. WorldInput locks gate every input (smoothing and inertia keep running); Suspended hands the
/// camera to the presentation / menu; SaveView / RestoreView keep the edit view across them.
/// Fixes (v3 review): a trackpad scroll gesture keeps the axis it started on (F4); Q / E held orbit ±90°/s and C resets the view
/// (F4); any world press freezes a coasting or gliding view (F5); no overview refit while an island or a tray card is dragged (F9);
/// orbit speed per pixel and every press-vs-drag threshold are DPI-aware and the release coast is capped at 110°/s (F12, F17).
/// v8 (the user: "the camera follow should be constant rather than jumping per measure"): follow no longer glides once per column; the focus
/// tracks a target that moves linearly with the song beat between the platforms' middles (belts included), lightly damped (FollowSmooth).
/// v4 (SPEC v4 §3 K6): follow (F) frames the lit COLUMN (the bounds of all its islands; the distance only grows when the column would not
/// fit); a focus loop keeps it on the looped column (the lit column never leaves it); FrameIsland (group aware) and the overview are unchanged.
/// v7 (SPEC v7 §3.3 — "grids should abide by the height"): the framing calls (frame an island / a column / the overview, reset view, follow) also
/// frame the HEIGHT the framed islands stand on (their grounds after a stairs island): the focus point rises / sinks to it (a smoothed height,
/// <see cref="FocusHeight"/>), the fit distance counts the height span, and pans / zooms grab the plane at that height. Shift / Alt + an arrow is a
/// cube / grid shortcut (U2), never a pan. With a cube in the hand the wheel over a grid is the BRUSH (PathManager.OwnsWheel, SPEC §15), not a zoom.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    public static OrbitCamera I;

    [Header("References")]
    public SongManager songManager;

    // legacy inspector fields (unused)
    public Vector3 targetDestination;
    public Vector3 targetRotation;
    [Range(5f, 50f)] public float distance = 14f;
    public float xSpeed = 40f;
    public float ySpeed = 2f;
    public float distanceToNextGrid;

    /// <summary>F / the target button: follow — while playing the focus moves steadily with the playhead from platform to platform (v8; it was one
    /// glide per lit column before).
    /// Off by default (Awake overrides the scene's serialized v2 value).</summary>
    public bool followPlayhead = false;
    public bool overview;
    /// <summary>SPEC v3 §1: while true the rig does nothing (the presentation or the menu drive the camera).</summary>
    public bool Suspended;
    public int FocusedMeasure => focused;
    public bool RightDragged => rightDragDist > DragThresholdPx;
    /// <summary>F17: screen pixels per 96-dpi pixel (Retina 2-3; 1 when the dpi is unknown): pixel thresholds and orbit speeds scale with it.</summary>
    public static float DpiScale { get { float d = Screen.dpi; return d > 0f ? Mathf.Clamp(d / 96f, 1f, 3f) : 1f; } }
    /// <summary>F17: the one press-vs-drag threshold every world gesture uses (6 px at 96 dpi).</summary>
    public static float DragThresholdPx => BaseDragPx * DpiScale;
    /// <summary>v2 leash inputs, still assigned by the Comet; v3 has no leash (kept for compatibility).</summary>
    public Func<Vector3> LeashTarget;
    public Func<bool> LeashInFlight;

    // tuning (SPEC v3 §3.4)
    public const float BaseDragPx = 6f, DoubleClickSeconds = 0.35f;
    public const float OrbitDegPerPx = 0.25f, OrbitPitchPerPx = 0.2f;   // per 96-dpi pixel (F12)
    public const float MaxReleaseYawSpeed = 110f;                        // F12: the release coast is at most ~33° (110°/s × τ)
    public const float KeyOrbitSpeed = 90f;                              // F4: Q / E held, degrees per second
    public const float ScrollGestureGap = 0.15f;                         // F4: a pause this long ends a trackpad scroll gesture
    /// <summary>F4: the reset-view key (Q orbits now).</summary>
    public const KeyCode ResetViewKey = KeyCode.C;
    public const float MinDist = 5f, MaxDist = 80f;
    public const float PanTau = 0.35f, PanStopSpeed = 0.05f, VelocityWindow = 0.08f;
    public const float YawTau = 0.3f, YawStopSpeed = 2f;
    public const float ZoomInStep = 0.88f, ZoomOutStep = 1.14f, ZoomRate = 16f;
    public const float KeyPanSpeed = 0.9f, KeyRamp = 0.12f;
    public const float FollowGlide = 0.7f, FrameGlide = 0.55f;
    /// <summary>v8: the continuous follow's damping (SmoothDamp time): a steady lag of about FollowSmooth × speed; seeks and loop wraps glide in ~1 s.</summary>
    public const float FollowSmooth = 0.3f;
    public const float SoftBoundsPad = 60f, SoftBoundsSpring = 0.8f, SoftBoundsSlack = 2f;
    public const float HomeYaw = -24f, HomePitch = 52f, HomeDist = 14f;   // HomeDist, MinDist and MaxDist are for a 60° field of view (see FovScale)
    /// <summary>v9 (R): the launch riser's camera BREATH — a gentle lift (u) and a slight field-of-view widening (degrees) by LaunchFx.BreathAt
    /// (0..1 through a launch's swell, a kick down at the landing, settled within a beat); additive on top of the pose, removed before the next
    /// frame's work so FovScale and the picks see the plain view.</summary>
    public static float BreathLift = 0.9f, BreathFovDeg = 6f;
    public float BreathNow { get; private set; }
    float breathFovApplied;
    /// <summary>tan(30°) / tan(fov / 2): distances that should frame the same ground at any field of view are scaled by this (1 at 60°).</summary>
    public float FovScale { get { if (cam == null) cam = GetComponent<Camera>(); float f = cam != null ? cam.fieldOfView : 60f; return Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Clamp(f, 10f, 120f) * 0.5f * Mathf.Deg2Rad); } }
    /// <summary>The home (reset view, C) distance at the current field of view.</summary>
    public float HomeDistance => HomeDist * FovScale;
    // v2 names kept for compatibility (v3 has no leash)
    public const float BandMin = 0.30f, BandMax = 0.70f, LeashSmooth = 0.6f, InputCooldown = 2.5f, BandHysteresis = 0.03f, FlightSmooth = 0.28f;

    // diagnostics (read-only; used by the verification scripts)
    public Vector3 FocusTarget => followActive ? followTarget : gliding ? glideTarget : focus;
    /// <summary>v8: true while the continuous follow drives the focus this frame (F on, playing, the user has not taken the view).</summary>
    public bool Following => followActive;
    public Vector3 Focus => focus;
    /// <summary>v7: the height the view is framed at (the ground of the framed islands; smoothed) and where it is going.</summary>
    public float FocusHeight => focusY;
    public float FocusHeightTarget => focusYT;
    public float Dist => dist;
    public float DistT => distT;
    public float Yaw => yaw;
    public float Pitch => pitch;
    public bool LeashActive => false;
    public bool LeftPanning => leftPan;
    public float Cooldown => 0f;
    /// <summary>A pan gesture holds the ground (left/middle drag or a synthetic pan).</summary>
    public bool Panning => panning;
    public bool Orbiting => orbiting;
    public bool Gliding => gliding;
    /// <summary>Inertia after a pan release (world units per second, flat).</summary>
    public Vector3 PanVelocity => panVel;
    /// <summary>Yaw inertia after an orbit release (degrees per second).</summary>
    public float YawVelocity => yawVel;
    /// <summary>True when <paramref name="p"/> lies inside the viewport band [0.30, 0.70] (v2 helper).</summary>
    public bool InBand(Vector3 p, float pad = 0f)
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null) return false;
        Vector3 vp = cam.WorldToViewportPoint(p);
        return vp.z > 0f && vp.x >= BandMin + pad && vp.x <= BandMax - pad && vp.y >= BandMin + pad && vp.y <= BandMax - pad;
    }

    Vector3 focus = new Vector3(3f, 0f, 2f), glideTarget, glideVel, panVel, keyVel, boundsVel;
    float focusY, focusYT, focusYVel;   // v7: the framed height (the islands' ground)
    float yaw = HomeYaw, pitch = HomePitch, yawT = HomeYaw, pitchT = HomePitch, dist = HomeDist, distT = HomeDist, distVel, yawVel;
    bool gliding, distGlide, zoomAnchored, zoomPxFixed, springing; float glideSmooth = 0.22f; Vector3 zoomPx;
    int focused = -1, followGroupKey = int.MinValue;
    // v8 continuous follow: the target the playhead implies, its damping velocity, and "the user took the view" (until the next column / a wrap)
    Vector3 followTarget, followVel; bool followActive, followHold, followWasPlaying, worldPress; double followLastBeat;
    // the song beat the follow reads: SongBeatD comes from AudioSettings.dspTime, which advances in audio-buffer steps (~21 ms: most 120 fps frames
    // see no change), so the follow runs its own beat at the tempo and eases it onto the clock (a seek / wrap / play start takes the clock as is)
    double followBeat;
    bool dragOrbit, midPan, leftPan, leftArmed, panning, orbiting;
    Vector3 leftDownPos, lastMouse, lastPanPx; float rightDragDist;
    Vector3 grabG; bool grabValid;
    float distBeforeOverview = -1f;
    float lastScrollT = -9f; int scrollAxis;   // F4: the axis the current scroll gesture latched (1 sideways pan, 2 zoom)
    Camera cam;
    // release velocity: the last frames of a gesture
    const int SampleCap = 48;
    readonly Vector3[] panSamples = new Vector3[SampleCap]; readonly float[] panTimes = new float[SampleCap]; int panHead, panCount;
    readonly float[] yawSamples = new float[SampleCap]; readonly float[] yawTimes = new float[SampleCap]; int yawHead, yawCount;
    // synthetic input (tests: legacy Input cannot be faked)
    bool simPan, simOrbit; Vector3 simPointer; Vector2 simScroll; Vector3 simScrollPx; bool simScrollAt;
    static readonly HashSet<KeyCode> simDown = new HashSet<KeyCode>(), simHeld = new HashSet<KeyCode>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { simDown.Clear(); simHeld.Clear(); }

    void Awake()
    {
        I = this; cam = GetComponent<Camera>();
        followPlayhead = false;   // SPEC v3 §3.4: no auto-chase (the scene still serializes the v2 default)
    }

    void Start()
    {
        if (songManager == null) songManager = SongManager.I != null ? SongManager.I : FindAnyObjectByType<SongManager>();
        Apply();
    }

    void LateUpdate()
    {
        if (Suspended) { simDown.Clear(); return; }
        float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        float now = Time.unscaledTime;
        if (cam == null) cam = GetComponent<Camera>();
        if (breathFovApplied != 0f) { if (cam != null) cam.fieldOfView -= breathFovApplied; breathFovApplied = 0f; }   // v9: last frame's breath off
        if (songManager == null) songManager = SongManager.I;
        bool locked = WorldInput.WorldLocked;   // menu / present / prompt / inspector / tray ghost: no pointer or key input, smoothing and inertia keep running (SPEC v3 §1)
        if (locked) EndPointer(now);
        bool overUI = locked || InputUtil.PointerOverUI;
        bool keysOn = !locked && !WorldInput.KeysLocked && !InputUtil.TypingInField;
        bool hasSong = songManager != null && songManager.HasSong;
        Vector3 mouse = simPan || simOrbit ? simPointer : Input.mousePosition;

        // ---- pointer gestures (the real mouse; a synthetic pan / orbit replaces it)
        if (!locked && !simPan && !simOrbit)
        {
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                // left-drag on empty sea or sky pans after 6 px; the press itself is PathManager's (a plain click deselects)
                var pm = PathManager.I;
                leftArmed = pm != null && pm.HoverIsEmpty && !pm.IsDrawing && pm.currentState == PathManager.EditorState.Idle;
                leftDownPos = mouse;
                StopForPress();   // F5: any world press catches a coasting or gliding view (a pressed tile must not slide away under the pointer)
            }
            if (leftArmed && !leftPan && Input.GetMouseButton(0) && (mouse - leftDownPos).magnitude > DragThresholdPx) { leftPan = true; BeginPan(leftDownPos, now); }
            if (!Input.GetMouseButton(0)) { if (leftPan) EndPan(now); leftPan = false; leftArmed = false; }
            if (Input.GetMouseButtonDown(2) && !overUI && !panning) { midPan = true; StopMotion(); BeginPan(mouse, now); }
            if (midPan && !Input.GetMouseButton(2)) { midPan = false; EndPan(now); }
            if (Input.GetMouseButtonDown(1) && !overUI) { dragOrbit = true; lastMouse = mouse; rightDragDist = 0f; StopForPress(); }
            if (dragOrbit && !Input.GetMouseButton(1)) { dragOrbit = false; if (orbiting) EndOrbit(now); }
            // v8: a held world press freezes the follow (a pressed tile must not slide away under the pointer, F5); it resumes on release
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !overUI) worldPress = true;
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) worldPress = false;
        }

        // ---- orbit: direct while dragging (no lag), yaw inertia after the release
        if (dragOrbit)
        {
            Vector3 d = mouse - lastMouse; lastMouse = mouse;
            rightDragDist += d.magnitude;
            if (RightDragged)
            {
                if (!orbiting) { orbiting = true; yawVel = 0f; yawCount = 0; yawHead = 0; }
                if (d.sqrMagnitude > 0f)
                {
                    float k = 1f / DpiScale;   // F12 / F17: the same hand motion turns the view as far on a Retina display
                    yaw += d.x * OrbitDegPerPx * k; yawT = yaw;
                    pitch = Mathf.Clamp(pitch - d.y * OrbitPitchPerPx * k, 18f, 82f); pitchT = pitch;
                }
                PushYaw(now, yaw);
            }
        }

        // ---- wheel: zoom toward the cursor; trackpad sideways scroll and Shift + wheel pan
        bool simWheel = simScrollAt;
        Vector2 scroll = locked ? Vector2.zero : (simWheel ? simScroll : Input.mouseScrollDelta);
        Vector3 scrollPx = simWheel ? simScrollPx : mouse;
        simScroll = Vector2.zero; simScrollAt = false;
        if ((!overUI || simWheel) && scroll.sqrMagnitude > 1e-8f && !locked && (simWheel || !PathManager.OwnsWheel))   // v7 (D, SPEC §15): with a cube in the hand the wheel over a grid sets the brush
        {
            // F4: a trackpad gesture keeps the axis it started on (a slightly diagonal swipe neither zooms while panning nor drifts while zooming);
            // a pause of ScrollGestureGap ends the gesture
            if (now - lastScrollT > ScrollGestureGap) scrollAxis = Mathf.Abs(scroll.x) > Mathf.Abs(scroll.y) ? 1 : 2;
            lastScrollT = now;
            if (InputUtil.Shift)
            {
                float f = Mathf.Abs(scroll.y) >= Mathf.Abs(scroll.x) ? scroll.y : scroll.x;   // macOS turns Shift + wheel into a sideways scroll
                PanBy(FlatForward() * (f * dist * 0.06f));
            }
            else if (scrollAxis == 1) { if (Mathf.Abs(scroll.x) > 1e-4f) PanBy(FlatRight() * (scroll.x * dist * 0.06f)); }
            else if (Mathf.Abs(scroll.y) > 1e-4f) Zoom(scroll.y, scrollPx, simWheel);
        }

        // ---- keys: WASD / arrows pan with a short ramp; Tab / Shift+Tab step islands; Q / E orbit; C reset view; O frame all; F follow
        Vector2 keyIn = Vector2.zero; float keyYaw = 0f;
        if (keysOn)
        {
            bool cubeKeys = PathManager.I != null && PathManager.I.selectedCube != null;   // a selected cube owns W and the up/down arrows (octave, shadow)
            // v7 (U2's hotkeys): Shift / Alt + an arrow is a cube / grid shortcut (octave copy, harmony, launch), never a pan
            bool arrows = !InputUtil.Shift && !KeyShim.Held(KeyCode.LeftAlt) && !KeyShim.Held(KeyCode.RightAlt);
            if (KeyHeld(KeyCode.A) || (arrows && KeyHeld(KeyCode.LeftArrow))) keyIn.x -= 1f;
            if (KeyHeld(KeyCode.D) || (arrows && KeyHeld(KeyCode.RightArrow))) keyIn.x += 1f;
            if (!cubeKeys && (KeyHeld(KeyCode.W) || (arrows && KeyHeld(KeyCode.UpArrow)))) keyIn.y += 1f;
            if (!cubeKeys && (KeyHeld(KeyCode.S) || (arrows && KeyHeld(KeyCode.DownArrow)))) keyIn.y -= 1f;
            if (InputUtil.Cmd) keyIn = Vector2.zero;   // Cmd+S saves, Cmd+Z undoes: never a pan
            if (KeyDown(KeyCode.Tab)) StepIsland(InputUtil.Shift ? -1 : 1);
            if (!InputUtil.Cmd)
            {
                if (KeyDown(ResetViewKey)) ResetView();
                if (KeyDown(KeyCode.O)) FrameAll();
                if (KeyDown(KeyCode.F)) ToggleFollow();
                if (KeyHeld(KeyCode.Q)) keyYaw -= 1f;   // F4: keyboard orbit (no inertia: it stops with the key)
                if (KeyHeld(KeyCode.E)) keyYaw += 1f;
            }
        }
        if (keyYaw != 0f && !orbiting) { yawVel = 0f; yaw += keyYaw * KeyOrbitSpeed * dt; yawT = yaw; }
        if (keyIn.sqrMagnitude > 1f) keyIn.Normalize();
        Vector3 keyTarget = (FlatRight() * keyIn.x + FlatForward() * keyIn.y) * (KeyPanSpeed * dist);
        keyVel = Vector3.Lerp(keyVel, keyTarget, 1f - Mathf.Exp(-dt / KeyRamp));
        if (keyIn.sqrMagnitude > 0f) { gliding = false; overview = false; panVel = Vector3.zero; springing = false; }
        else if (keyVel.sqrMagnitude < 1e-5f) keyVel = Vector3.zero;

        // ---- column follow (v8, continuous): the focus moves with the playhead — from the middle of the playing pass's platform (a belt: its
        // slot) to the middle of the next, linear in the song beat — damped (FollowSmooth) so tempo changes, seeks and loop wraps glide.
        // The user takes the view with a pan (drag / keys / sideways scroll), the overview or any framing (click, Tab, tray): the follow
        // waits until the lit column changes, the song wraps or play starts again; a held world press only freezes it until release.
        bool playingNow = GlobalClock.IsPlaying;
        bool wasFollowing = followActive;
        followActive = false;
        if (panning || keyIn.sqrMagnitude > 0f || overview) followHold = true;
        if (followPlayhead && hasSong && playingNow)
        {
            double beat = GlobalClock.SongBeatD;
            bool resume = !followWasPlaying || beat < followLastBeat - 1e-3;   // play start, a loop wrap or a seek back
            if (!followWasPlaying || Math.Abs(beat - followBeat) > 0.5) followBeat = beat;
            else
            {
                followBeat += dt * GlobalClock.BeatsPerSecond;
                followBeat += (beat - followBeat) * (1.0 - Math.Exp(-dt / 0.25));
            }
            int col = Mathf.Clamp(songManager.LitColumn, 0, Mathf.Max(0, songManager.ColumnCount - 1));
            if (col != followGroupKey) { followGroupKey = col; resume = true; }
            followLastBeat = beat;
            if (resume && !panning && !overview && keyIn.sqrMagnitude == 0f)
            {
                followHold = false;
                // v4: the distance only grows when the lit column would not fit (follow never zooms in on you)
                float s = FovScale;
                float fit = Mathf.Clamp(FitDistanceFor(songManager.ColumnBounds(col)), (MinDist + 2f) * s, 60f * s);
                if (fit > distT + 0.05f) { distT = fit; distGlide = true; zoomAnchored = false; distVel = 0f; }
                var a = songManager.AnchorOf(col);
                if (a != null) { int i = songManager.Islands.IndexOf(a); if (i >= 0) focused = i; }
            }
            bool pressed = worldPress || dragOrbit || orbiting || simPan || simOrbit;
            if (!followHold && !pressed && FollowTargetAt(followBeat, out followTarget, out float fh))
            {
                followActive = true;
                if (!wasFollowing) followVel = Vector3.zero;
                gliding = false; glideVel = Vector3.zero; panVel = Vector3.zero; keyVel = Vector3.zero; springing = false;
                zoomAnchored = false;   // a wheel zoom while following zooms about the followed focus (no tug of war with the cursor anchor)
                focusYT = fh;           // v7: the ground under the followed platforms
            }
        }
        else if (!playingNow) followGroupKey = int.MinValue;
        followWasPlaying = playingNow;

        // ---- overview (O): keeps framing the whole song until you pan, zoom or press O again (F9: frozen while an island or a tray
        // card is dragged: the song bounds move with the drag and the view would run away from it)
        bool dragging = (PathManager.I != null && PathManager.I.Drag != null && PathManager.I.Drag.Busy) || (IslandTray.I != null && IslandTray.I.Dragging);
        if (overview && hasSong && !dragging)
        {
            Vector3 fwd = FlatForward();
            var b = songManager.SongBounds;
            Vector3 want = songManager.SongCenter - fwd * (b.size.z * 0.08f + 1.5f);
            if ((want - FocusTarget).sqrMagnitude > 1e-4f) GlideFocus(want, 0.6f);
            focusYT = HeightOf(songManager.FrameSongBounds);   // v7: frame all includes the heights
            float fit = FitDistance();
            if (Mathf.Abs(fit - distT) > 1e-3f) { distT = fit; distGlide = true; zoomAnchored = false; }
        }

        // ---- integrate: rotation, distance (zoom toward the cursor), focus (glide, inertia, keys, soft bounds)
        if (!orbiting)
        {
            if (yawVel != 0f)
            {
                yaw += yawVel * dt; yawT = yaw;
                yawVel *= Mathf.Exp(-dt / YawTau);
                if (Mathf.Abs(yawVel) < YawStopSpeed) yawVel = 0f;
            }
            else yaw = Mathf.LerpAngle(yaw, yawT, 1f - Mathf.Exp(-dt * 10f));
            pitch = Mathf.Lerp(pitch, pitchT, 1f - Mathf.Exp(-dt * 10f));
        }
        float newDist;
        if (distGlide) newDist = Mathf.SmoothDamp(dist, distT, ref distVel, glideSmooth, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
        else newDist = Mathf.Abs(dist - distT) < 1e-3f ? distT : Mathf.Lerp(dist, distT, 1f - Mathf.Exp(-ZoomRate * dt));
        if (zoomAnchored && !distGlide && Mathf.Abs(newDist - dist) > 1e-5f)
        {
            // the ground point under the cursor stays under it: F' = G + (F - G) * d'/d (exact for a fixed orientation)
            Apply();
            Vector3 g;
            if (GroundAt(zoomPxFixed ? zoomPx : mouse, out g) && (g - transform.position).magnitude < 12f * Mathf.Max(1f, dist))
            {
                Vector3 f = g + (focus - g) * (newDist / dist); f.y = 0f;
                focus = f;
                if (gliding) gliding = false;
            }
        }
        if (Mathf.Abs(newDist - distT) < 1e-3f) { zoomAnchored = false; if (distGlide && !gliding) { distGlide = false; distVel = 0f; } }
        dist = newDist;

        // v7: the framed height follows its target smoothly (a glide like the focus')
        if (Mathf.Abs(focusY - focusYT) > 1e-4f) focusY = Mathf.SmoothDamp(focusY, focusYT, ref focusYVel, 0.22f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
        else { focusY = focusYT; focusYVel = 0f; }
        if (!panning)
        {
            if (followActive)
                focus = Vector3.SmoothDamp(focus, followTarget, ref followVel, FollowSmooth, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
            else if (gliding)
            {
                focus = Vector3.SmoothDamp(focus, glideTarget, ref glideVel, glideSmooth, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
                if ((focus - glideTarget).sqrMagnitude < 1e-5f && glideVel.sqrMagnitude < 1e-4f) { focus = glideTarget; gliding = false; glideVel = Vector3.zero; }
            }
            if (panVel != Vector3.zero)
            {
                focus += panVel * dt;
                panVel *= Mathf.Exp(-dt / PanTau);
                if (panVel.magnitude < PanStopSpeed) panVel = Vector3.zero;
            }
            if (keyVel != Vector3.zero) focus += keyVel * dt;
            if (hasSong && !gliding && !orbiting && !followActive) SoftBounds(dt);
            focus.y = 0f;
        }
        Apply();
        if (panning) { CorrectPan(mouse); PushPan(now, focus); }
        simDown.Clear();
        ApplyBreath();
    }

    /// <summary>v9 (R): the riser's breath on top of the pose (see BreathLift).</summary>
    void ApplyBreath()
    {
        float br = 0f;
        try { br = LaunchFx.BreathAt(WorldMagic.FxBeat); } catch (Exception) { br = 0f; }
        BreathNow = br;
        if (br <= 0f) return;
        transform.position += Vector3.up * (BreathLift * br);
        if (cam != null) { breathFovApplied = BreathFovDeg * br; cam.fieldOfView += breathFovApplied; }
    }

    void Apply()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.rotation = rot;
        transform.position = focus + Vector3.up * focusY + rot * new Vector3(0f, 0f, -dist);   // v7: at the framed height
    }

    // ------------------------------------------------------------------ gestures
    void StopMotion() { panVel = Vector3.zero; yawVel = 0f; springing = false; }

    /// <summary>F5: a press in the world freezes the view: inertia stops and a framing glide (focus and distance) ends where it is
    /// (an overview in progress too, or it would start the glide again next frame).</summary>
    void StopForPress()
    {
        StopMotion();
        if (!gliding && !distGlide) return;
        gliding = false; glideVel = Vector3.zero;
        if (distGlide) { distT = dist; distGlide = false; distVel = 0f; }
        overview = false;
    }

    /// <summary>Grab the ground under <paramref name="pressPx"/>: from now on that point stays exactly under the pointer.</summary>
    void BeginPan(Vector3 pressPx, float now)
    {
        panning = true; gliding = false; overview = false; zoomAnchored = false; springing = false;
        panVel = Vector3.zero; keyVel = Vector3.zero;
        Apply();
        Vector3 g;
        grabValid = GroundAt(pressPx, out g) && (g - transform.position).magnitude < 10f * Mathf.Max(1f, dist);
        grabG = g;
        lastPanPx = pressPx;
        panCount = 0; panHead = 0;
        PushPan(now, focus);
    }

    /// <summary>Release: the view keeps the pointer's last ~0.08 s velocity and coasts to a stop (τ 0.35 s).</summary>
    void EndPan(float now)
    {
        if (!panning) return;
        panning = false;
        Vector3 v = ReleaseVelocity(now);
        float max = 6f * Mathf.Max(5f, dist);
        if (v.magnitude > max) v = v.normalized * max;
        panVel = v.magnitude >= PanStopSpeed ? v : Vector3.zero;
    }

    void EndOrbit(float now)
    {
        orbiting = false;
        if (yawCount < 2) { yawVel = 0f; return; }
        int newest = (yawHead - 1 + SampleCap) % SampleCap;
        int idx = newest;
        for (int k = 1; k < yawCount; k++) { int j = (newest - k + SampleCap) % SampleCap; idx = j; if (yawTimes[newest] - yawTimes[j] >= VelocityWindow) break; }
        float span = yawTimes[newest] - yawTimes[idx];
        float v = span > 1e-3f && now - yawTimes[newest] < 0.05f ? (yawSamples[newest] - yawSamples[idx]) / span : 0f;
        yawVel = Mathf.Clamp(v, -MaxReleaseYawSpeed, MaxReleaseYawSpeed);
        if (Mathf.Abs(yawVel) < YawStopSpeed) yawVel = 0f;
    }

    void EndPointer(float now)
    {
        if (panning) EndPan(now);
        if (orbiting) EndOrbit(now);
        dragOrbit = false; midPan = false; leftPan = false; leftArmed = false; simPan = false; simOrbit = false; worldPress = false;
    }

    /// <summary>Keeps the grabbed ground point exactly under the pointer for the pose about to render (zero-frame lag); near the horizon it
    /// falls back to a screen-space pan and re-grabs when the ground is back under the pointer.</summary>
    void CorrectPan(Vector3 mouse)
    {
        Vector3 h;
        bool ok = GroundAt(mouse, out h) && (h - transform.position).magnitude < 10f * Mathf.Max(1f, dist);
        if (ok && grabValid)
        {
            Vector3 d = grabG - h; d.y = 0f;
            if (d.sqrMagnitude > 0f) { focus += d; Apply(); }
        }
        else if (ok) { grabG = h; grabValid = true; }
        else
        {
            Vector3 px = mouse - lastPanPx;
            focus -= (FlatRight() * px.x + FlatForward() * px.y) * dist * 0.0016f;
            focus.y = 0f;
            grabValid = false;
            Apply();
        }
        lastPanPx = mouse;
    }

    void PanBy(Vector3 d)
    {
        d.y = 0f;
        focus += d; gliding = false; overview = false; panVel = Vector3.zero; springing = false; followHold = true;   // v8: a pan takes the view from the follow
    }

    void Zoom(float notches, Vector3 px, bool fixedPx)
    {
        float f = notches > 0f ? Mathf.Pow(ZoomInStep, notches) : Mathf.Pow(ZoomOutStep, -notches);
        float old = distT;
        float s = FovScale;
        distT = Mathf.Clamp(distT * f, MinDist * s, MaxDist * s);
        overview = false;
        if (Mathf.Approximately(old, distT)) return;
        zoomAnchored = true; zoomPx = px; zoomPxFixed = fixedPx; distGlide = false; distVel = 0f; gliding = false;
    }

    void PushPan(float t, Vector3 p) { panSamples[panHead] = p; panTimes[panHead] = t; panHead = (panHead + 1) % SampleCap; panCount = Mathf.Min(SampleCap, panCount + 1); }
    void PushYaw(float t, float y) { yawSamples[yawHead] = y; yawTimes[yawHead] = t; yawHead = (yawHead + 1) % SampleCap; yawCount = Mathf.Min(SampleCap, yawCount + 1); }

    Vector3 ReleaseVelocity(float now)
    {
        if (panCount < 2) return Vector3.zero;
        int newest = (panHead - 1 + SampleCap) % SampleCap;
        int idx = newest;
        for (int k = 1; k < panCount; k++) { int j = (newest - k + SampleCap) % SampleCap; idx = j; if (panTimes[newest] - panTimes[j] >= VelocityWindow) break; }
        float span = panTimes[newest] - panTimes[idx];
        if (span < 1e-3f || now - panTimes[newest] > 0.05f) return Vector3.zero;
        Vector3 v = (panSamples[newest] - panSamples[idx]) / span; v.y = 0f;
        return v;
    }

    void SoftBounds(float dt)
    {
        var b = songManager.SongBounds;
        b.Expand(SoftBoundsPad * 2f);
        Vector3 c = b.ClosestPoint(new Vector3(focus.x, b.center.y, focus.z)); c.y = 0f;
        Vector3 off = new Vector3(focus.x - c.x, 0f, focus.z - c.z);
        float m = off.magnitude;
        if (!springing && m <= SoftBoundsSlack) { boundsVel = Vector3.zero; return; }
        springing = m > 0.02f;
        if (!springing) { boundsVel = Vector3.zero; return; }
        panVel = Vector3.zero;
        focus = Vector3.SmoothDamp(focus, c, ref boundsVel, SoftBoundsSpring * 0.4f, Mathf.Infinity, Mathf.Max(dt, 1e-4f));
    }

    // ------------------------------------------------------------------ helpers
    Vector3 FlatForward() { float a = yaw * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
    Vector3 FlatRight() { float a = yaw * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)); }
    bool KeyDown(KeyCode k) => Input.GetKeyDown(k) || (simDown.Count > 0 && simDown.Contains(k));
    bool KeyHeld(KeyCode k) => Input.GetKey(k) || (simHeld.Count > 0 && simHeld.Contains(k));

    /// <summary>Where the ray through <paramref name="screenPos"/> meets the sea plane y = 0 (current transform).</summary>
    bool GroundAt(Vector3 screenPos, out Vector3 p)
    {
        p = Vector3.zero;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(screenPos);
        float enter;
        if (!new Plane(Vector3.up, new Vector3(0f, focusY, 0f)).Raycast(ray, out enter) || enter <= 0f) return false;   // v7: the plane at the framed height
        p = ray.GetPoint(enter);
        p.y = 0f;   // (x / z on the framed plane; the focus lives on y = 0 + FocusHeight)
        return true;
    }

    /// <summary>v7: the height a framing of <paramref name="b"/> looks at: the ground its islands stand on (GroundedBounds' centres sit 0.6 above it),
    /// clamped to the grounds' range.</summary>
    static float HeightOf(Bounds b) => Mathf.Clamp(b.center.y - 0.6f, ProjectConfig.GroundMin, ProjectConfig.GroundMax);

    void GlideFocus(Vector3 target, float seconds)
    {
        glideTarget = new Vector3(target.x, 0f, target.z);
        gliding = true; springing = false;
        followHold = true;   // v8: any framing (click, Tab, tray, overview, restore) takes the view from the follow until the next column
        glideSmooth = Mathf.Max(0.05f, seconds * 0.4f);
        panVel = Vector3.zero;
    }

    /// <summary>A framing centre for <paramref name="b"/>: a little toward the camera so the transport at the bottom does not cover the front.</summary>
    Vector3 FrameCentre(Bounds b)
    {
        Vector3 c = b.center; c.y = 0f;
        return c - FlatForward() * 0.8f;
    }

    /// <summary>Distance that fits <paramref name="b"/> (seen from the current yaw and the target pitch) with room for the HUD.</summary>
    float FitDistanceFor(Bounds b)
    {
        if (cam == null) cam = GetComponent<Camera>();
        float vfov = (cam != null ? cam.fieldOfView : 60f) * Mathf.Deg2Rad;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov * 0.5f) * aspect);
        Vector3 r = FlatRight(), f = FlatForward();
        float ex = b.extents.x, ez = b.extents.z;
        float w = 2f * (Mathf.Abs(r.x) * ex + Mathf.Abs(r.z) * ez) + 5f;
        float d = 2f * (Mathf.Abs(f.x) * ex + Mathf.Abs(f.z) * ez);
        float s = Mathf.Sin(pitchT * Mathf.Deg2Rad);
        float h = Mathf.Max(0f, b.size.y - 3f);   // v7: the height span of the framed islands (grounds) takes screen height too
        float dW = w / (2f * Mathf.Tan(hfov * 0.5f));
        float dD = (d * s + h * Mathf.Cos(pitchT * Mathf.Deg2Rad) + 5f) / (2f * Mathf.Tan(vfov * 0.5f));
        return Mathf.Max(dW, dD) * 1.25f;
    }

    // ------------------------------------------------------------------ v8 continuous follow
    /// <summary>The follow waypoint of pass <paramref name="pass"/> of column <paramref name="col"/>: the middle of the platform that plays it (a belt
    /// carries the island one slot east per pass, SongManager.XEdgeOfBeat; rewind islands and phrases replay in place), at the column's depth
    /// (a little toward the camera, like FrameCentre) and the ground it stands on.</summary>
    void FollowPoint(int col, int pass, out Vector3 p, out float h)
    {
        var sm = songManager;
        var b = sm.ColumnBounds(col);
        float x = sm.ColumnCenterX(col);
        if (pass > 0 && sm.BeltExtent(col) > 0f) x += pass * (sm.ColumnWidth(col) + ProjectConfig.BeltGap);
        p = new Vector3(x, 0f, b.center.z) - FlatForward() * 0.8f;
        h = HeightOf(b);
    }

    /// <summary>Where the follow wants the focus at song beat <paramref name="beat"/>: piecewise linear in the beat through the waypoint of each
    /// (column, pass) at the middle of that pass, so the view moves at a steady speed and is centred on a platform halfway through its turn. It holds
    /// on the first / last waypoint of the song and at the ends of a focus loop (it never leans toward a column that will not play).</summary>
    bool FollowTargetAt(double beat, out Vector3 target, out float height)
    {
        target = focus; height = focusYT;
        var sm = songManager;
        int nc = sm.ColumnCount;
        if (nc == 0) return false;
        int c = Mathf.Clamp(sm.ActiveColumn((float)beat), 0, nc - 1);
        double pl = Math.Max(1e-3, sm.PassLength(c));
        int np = Mathf.Max(1, sm.ColumnPasses(c));
        int p = Mathf.Clamp((int)Math.Floor((beat - sm.ColumnStart(c)) / pl + 1e-6), 0, np - 1);
        double s0 = sm.ColumnStart(c) + p * pl, mid = s0 + pl * 0.5;
        FollowPoint(c, p, out target, out height);
        int oc, op;
        if (beat >= mid)
        {
            if (p + 1 < np) { oc = c; op = p + 1; } else if (c + 1 < nc) { oc = c + 1; op = 0; } else return true;
            if (GlobalClock.HasRegion && sm.PassStart(oc, op) >= GlobalClock.RegionEnd - 1e-4) return true;
        }
        else
        {
            if (p > 0) { oc = c; op = p - 1; } else if (c > 0) { oc = c - 1; op = Mathf.Max(1, sm.ColumnPasses(c - 1)) - 1; } else return true;
            if (GlobalClock.HasRegion && s0 <= GlobalClock.RegionStart + 1e-4) return true;
        }
        double omid = sm.PassStart(oc, op) + Math.Max(1e-3, sm.PassLength(oc)) * 0.5;
        if (Math.Abs(omid - mid) < 1e-6) return true;
        float u = Mathf.Clamp01((float)((beat - mid) / (omid - mid)));   // 0 at this pass's middle, 1 at the neighbour's
        FollowPoint(oc, op, out Vector3 q, out float qh);
        target = Vector3.Lerp(target, q, u);
        height = Mathf.Lerp(height, qh, u);
        return true;
    }

    // ------------------------------------------------------------------ public API (v2 contract + v3)
    /// <summary>Glides to frame island <paramref name="index"/>'s group (fit distance); <paramref name="immediate"/> = no glide (a song's first frame).</summary>
    public void FocusMeasure(int index, bool immediate = false)
    {
        if (songManager == null) songManager = SongManager.I;
        if (songManager == null || !songManager.HasSong) return;
        index = Mathf.Clamp(index, 0, songManager.Islands.Count - 1);
        focused = index; overview = false;
        var kb = songManager.Islands[index];
        if (kb == null) return;
        FrameIsland(kb, false, FrameGlide, false);
        if (immediate) SnapToTargets();
    }

    /// <summary>Glides the focus to <paramref name="target"/> over about <paramref name="seconds"/>; never changes the distance.</summary>
    public void GlideTo(Vector3 target, float seconds = 0.5f) { overview = false; GlideFocus(target, seconds); }

    /// <summary>v2 double-click / Moon pill: frames the island or Moon (group aware).</summary>
    public void FocusIsland(KeyBlock kb) { FrameIsland(kb, false); }

    /// <summary>SPEC v3 §3.4 island focus: glides (0.55 s) to frame <paramref name="kb"/>'s whole group; <paramref name="tight"/> (double-click) frames closer.</summary>
    public void FrameIsland(KeyBlock kb, bool tight = false) { FrameIsland(kb, tight, FrameGlide, false); }

    /// <summary>Frames <paramref name="kb"/>'s group over <paramref name="seconds"/>; <paramref name="keepDistance"/> only moves the focus (follow).</summary>
    public void FrameIsland(KeyBlock kb, bool tight, float seconds, bool keepDistance)
    {
        if (kb == null) return;
        if (songManager == null) songManager = SongManager.I;
        if (songManager != null) { int i = songManager.Islands.IndexOf(kb); if (i >= 0) focused = i; }
        var b = songManager != null && !kb.IsMoon ? songManager.GroupBounds(kb) : kb.WorldBounds;
        FrameBounds(b, seconds, false, tight ? 0.7f : 1f, keepDistance);
    }

    /// <summary>Frames a world box: focus glide + fit distance (× <paramref name="tightness"/>), or instantly.</summary>
    public void FrameBounds(Bounds b, float seconds = FrameGlide, bool immediate = false, float tightness = 1f, bool keepDistance = false)
    {
        overview = false;
        GlideFocus(FrameCentre(b), seconds);
        focusYT = HeightOf(b);   // v7: the height the framed islands stand on
        if (!keepDistance) { float s = FovScale; distT = Mathf.Clamp(FitDistanceFor(b) * tightness, (MinDist + 2f) * s, 60f * s); distGlide = true; zoomAnchored = false; distVel = 0f; }
        if (immediate) SnapToTargets();
    }

    /// <summary>A fresh song's first frame (SPEC v3 §3.2): home yaw/pitch, <paramref name="b"/> (island 0 + the Moon) framed instantly.</summary>
    public void FrameStart(Bounds b)
    {
        yawT = HomeYaw; pitchT = HomePitch; yaw = yawT; pitch = pitchT;
        StopMotion(); keyVel = Vector3.zero; followGroupKey = int.MinValue; focused = 0;
        FrameBounds(b, FrameGlide, true);
    }

    void SnapToTargets()
    {
        if (gliding) focus = glideTarget;
        gliding = false; glideVel = Vector3.zero;
        yaw = yawT; pitch = pitchT; dist = distT; distGlide = false; distVel = 0f; zoomAnchored = false;
        focusY = focusYT; focusYVel = 0f;
        Apply();
    }

    /// <summary>Any user gesture in the world (v2: paused the leash). Kept for callers; v3 has nothing to pause.</summary>
    public void NotifyInput() { }

    /// <summary>Tab / Shift+Tab: the next / previous island in song order (column by column, a column's anchor first; the other members of the
    /// current island's merged group are skipped: a group counts as one), glide + frame.</summary>
    public void StepIsland(int delta)
    {
        if (songManager == null) songManager = SongManager.I;
        if (songManager == null || !songManager.HasSong) return;
        int n = songManager.Islands.Count;
        int cur = focused >= 0 && focused < n ? focused : (GlobalClock.IsPlaying ? songManager.LitIsland : -1);
        int next = cur < 0 ? (delta >= 0 ? 0 : n - 1) : cur;
        if (cur >= 0)
        {
            var group = songManager.GroupOf(songManager.Islands[cur]);
            int dir = delta >= 0 ? 1 : -1;
            for (int k = 0; k < n; k++)
            {
                next = ((next + dir) % n + n) % n;
                if (!group.Contains(songManager.Islands[next])) break;
            }
        }
        var kb = songManager.Islands[next];
        if (kb == null) return;
        var g = songManager.GroupOf(kb);
        int first = g.Count > 0 ? songManager.Islands.IndexOf(g[0]) : next;
        FrameIsland(kb, false);
        focused = kb.group != 0 && first >= 0 ? first : next;
    }

    /// <summary>v4: glides to frame column <paramref name="col"/> (all its islands) over <paramref name="seconds"/>; the distance only grows when the
    /// column would not fit at the current one (follow never zooms in on you).</summary>
    public void FrameColumn(int col, float seconds = FrameGlide)
    {
        if (songManager == null) songManager = SongManager.I;
        if (songManager == null || !songManager.HasSong) return;
        var b = songManager.ColumnBounds(col);
        float fit = Mathf.Clamp(FitDistanceFor(b), (MinDist + 2f) * FovScale, 60f * FovScale);
        bool grow = fit > distT + 0.05f;
        FrameBounds(b, seconds, false, 1f, !grow);
        var a = songManager.AnchorOf(col);
        if (a != null) { int i = songManager.Islands.IndexOf(a); if (i >= 0) focused = i; }
    }

    /// <summary>O / frame button: toggles the overview (SongCenter + FitDistance over SongBounds, Moons included); leaving it glides back to the focused island at the distance from before.</summary>
    public void FrameAll()
    {
        if (!overview) { distBeforeOverview = distT; overview = true; StopMotion(); return; }
        overview = false;
        if (distBeforeOverview > 0f) { distT = Mathf.Clamp(distBeforeOverview, MinDist * FovScale, MaxDist * FovScale); distGlide = true; }
        if (songManager != null && songManager.HasSong)
        {
            var kb = songManager.Islands[Mathf.Clamp(Mathf.Max(0, focused), 0, songManager.Islands.Count - 1)];
            if (kb != null) { var gb = songManager.GroupBounds(kb); GlideFocus(FrameCentre(gb), FrameGlide); focusYT = HeightOf(gb); }
        }
    }

    /// <summary>F / target button: island follow on/off (on: glide to the lit island's group now and whenever it changes).</summary>
    public void ToggleFollow() { followPlayhead = !followPlayhead; followGroupKey = int.MinValue; followHold = false; if (followPlayhead) overview = false; }

    /// <summary>C (reset view): reset yaw/pitch/distance and glide to the lit column (v4: all its islands) while playing, else to the focused
    /// island's group.</summary>
    public void ResetView()
    {
        yawT = HomeYaw; pitchT = HomePitch; distT = HomeDistance; distGlide = true; distVel = 0f; overview = false; StopMotion();
        if (songManager == null) songManager = SongManager.I;
        if (songManager == null || !songManager.HasSong) return;
        int i = GlobalClock.IsPlaying ? songManager.LitIsland : Mathf.Max(0, focused);
        i = Mathf.Clamp(i, 0, songManager.Islands.Count - 1);
        focused = i;
        var kb = songManager.Islands[i];
        if (kb == null) return;
        var b = GlobalClock.IsPlaying ? songManager.ColumnBounds(kb.column) : songManager.GroupBounds(kb);
        GlideFocus(FrameCentre(b), FrameGlide);
        focusYT = HeightOf(b);   // v7
        followHold = false;      // v8: while playing with follow on, C hands the view back to the follow
    }

    // ------------------------------------------------------------------ v3 contract: keep the edit view across the presentation (SPEC v3 §1, §4.4)
    struct SavedView { public Vector3 focus; public float yaw, pitch, dist, height; public bool overview; }
    SavedView savedView; bool hasSavedView;
    public bool HasSavedView => hasSavedView;
    /// <summary>Remembers the current target view (focus, yaw, pitch, distance, overview).</summary>
    public void SaveView() { savedView = new SavedView { focus = FocusTarget, yaw = yawT, pitch = pitchT, dist = distT, height = focusYT, overview = overview }; hasSavedView = true; }
    /// <summary>Returns to the saved view; <paramref name="immediate"/> = no glide (used behind a fade).</summary>
    public void RestoreView(bool immediate = true)
    {
        if (!hasSavedView) return;
        StopMotion(); keyVel = Vector3.zero;
        yawT = savedView.yaw; pitchT = savedView.pitch; distT = savedView.dist; distGlide = true; distVel = 0f; zoomAnchored = false;
        GlideFocus(savedView.focus, FrameGlide);
        focusYT = savedView.height;
        overview = savedView.overview;
        if (immediate) SnapToTargets();
    }

    float FitDistance()
    {
        if (cam == null) cam = GetComponent<Camera>();
        var b = songManager.SongBounds;   // SongManager encapsulates Moons too
        float w = b.size.x + 8f;
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov * 0.5f) * cam.aspect);
        float dW = w / (2f * Mathf.Tan(hfov * 0.5f));
        float dD = (b.size.z + 12f) / (2f * Mathf.Tan(vfov * 0.5f));
        float s = FovScale;
        return Mathf.Clamp(Mathf.Max(dW, dD) * 1.22f, 9f * s, 85f * s);   // the extra margin keeps the song clear of the strip and the transport
    }

    // ------------------------------------------------------------------ tests (legacy Input cannot be faked)
    /// <summary>Tests: starts a pan at a virtual pointer <paramref name="px"/> (the real mouse is ignored until SimPanEnd): the ground under px is grabbed.</summary>
    public void SimPanBegin(Vector2 px) { EndPointer(Time.unscaledTime); simPan = true; simPointer = px; StopMotion(); BeginPan(px, Time.unscaledTime); }
    /// <summary>Tests: moves the virtual pointer; the next LateUpdate puts the grabbed point under it.</summary>
    public void SimPanMove(Vector2 px) { simPointer = px; }
    /// <summary>Tests: releases the virtual pan (inertia from the last 0.08 s).</summary>
    public void SimPanEnd() { if (!simPan) return; EndPan(Time.unscaledTime); simPan = false; }
    /// <summary>Tests: a right-drag orbit with a virtual pointer starting at <paramref name="px"/> (SimOrbitMove moves it, SimOrbitEnd releases: yaw inertia).</summary>
    /// <summary>Tests: a simulated right-click (PathManager.SimPointer rightUp) has no press to reset the last right-drag's distance (a real right
    /// press does, line 164): call this first so the click is not read as the end of an orbit.</summary>
    public void SimRightPress() { rightDragDist = 0f; }
    public void SimOrbitBegin(Vector2 px) { EndPointer(Time.unscaledTime); StopMotion(); simOrbit = true; simPointer = px; dragOrbit = true; lastMouse = px; rightDragDist = 0f; }
    public void SimOrbitMove(Vector2 px) { simPointer = px; }
    public void SimOrbitEnd() { if (!simOrbit) return; if (orbiting) EndOrbit(Time.unscaledTime); dragOrbit = false; simOrbit = false; }
    /// <summary>Tests: a wheel / trackpad scroll of <paramref name="delta"/> at pixel <paramref name="px"/>, applied in the next LateUpdate.</summary>
    public void SimScroll(Vector2 delta, Vector2 px) { simScroll += delta; simScrollPx = px; simScrollAt = true; }
    /// <summary>Tests: key <paramref name="k"/> pressed for the next LateUpdate.</summary>
    public static void SimKeyDown(KeyCode k) { simDown.Add(k); }
    /// <summary>Tests: key <paramref name="k"/> held (true) or released (false).</summary>
    public static void SimKeyHold(KeyCode k, bool held) { if (held) simHeld.Add(k); else simHeld.Remove(k); }
    /// <summary>Tests: the sea point under a screen pixel for the pose last applied.</summary>
    public bool GroundUnder(Vector2 px, out Vector3 p) { return GroundAt(px, out p); }
}
