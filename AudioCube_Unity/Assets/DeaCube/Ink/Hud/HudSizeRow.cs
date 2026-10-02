using System;
using UnityEngine;

/// <summary>
/// v7 (SPEC v7 §15, package U2) — "for rhythm control, when they're selecting the path of the cube, make them select the size of the cube THEN
/// place it down": the SIZE ROW. While a cube is in the hand a paper tongue sticks out of the held chip (the instrument column) with the five
/// cube sizes standing on an inked shelf — the note-length row's sizes (px = 30 · SizeOf^1.5 × <see cref="Scale"/>: tiny = a sixteenth …
/// fat = a whole note) — and the DOT (dotted: × 1.5, the satellite that orbits dotted cubes in the world). The size the next placed node gets
/// (PathManager.BrushTicks) stands inside the ink hexagon on a cream pedestal in the group's colour (the others at 70 % saturation); hover =
/// the cube hops and that length auditions; click = the brush takes that size (clicking the chosen size again toggles the dot, as the length
/// row does); the dot toggles dotted. When a cube is picked up the chosen size PULSES once ("the last used size is preselected and pulses once
/// so the player notices it can be chosen"). The wheel over a grid and [ ] . (PathManager, package D) change the brush too: the row follows
/// BrushTicks (polled every frame — no event needed). While a path is drawn the row stays (a press on it never finishes the draft:
/// PathManager.DraftUiHover) and a small echo of it rides next to the cursor (CursorKit). It steps aside for the voice fan, mix mode, the
/// inspector and a hidden HUD. Hints id "sizeRow".
/// </summary>
[DefaultExecutionOrder(-50)]   // DraftUiHover is fresh before PathManager reads it (a press on the row must not finish the draft)
public class HudSizeRow : MonoBehaviour
{
    public static HudSizeRow I;
    /// <summary>The row's cubes relative to the length row's (DurationPicker.SlotPx).</summary>
    public const float Scale = 0.82f;
    public const float Gap = 7f, RowH = 76f, ShelfY = -18f, PadL = 16f, PadR = 12f, DotW = 34f, ChipGap = 14f;
    /// <summary>Seconds of the pick-up pulse.</summary>
    public const float PulseSeconds = 0.42f;
    static readonly string[] Words = { "very short", "short", "medium", "long", "very long" };

    /// <summary>The row is up (popped in, the HUD showing).</summary>
    public bool Shown => row != null && row.gameObject.activeSelf && popState == 2;
    public RectTransform Row => row;
    public InkButton Slot(int i) => i >= 0 && i < 5 ? slots[i] : null;
    public InkButton DotButton => dotBtn;
    public CubeGlyph CubeOf(int i) => i >= 0 && i < 5 ? cubes[i] : null;
    /// <summary>The size / dot the row shows as chosen (the brush).</summary>
    public int SelectedSize => selSize;
    public bool SelectedDotted => selDotted;
    /// <summary>The chosen cube's current pulse scale (1 at rest).</summary>
    public float PulseScale => pulseScale;
    /// <summary>Pulses so far (tests: one per pick-up), picks through the row, hover auditions.</summary>
    public static int PulseCount { get; private set; }
    public static int PickCount { get; private set; }
    public static int AuditionCount { get; private set; }
    /// <summary>The pointer is over the row this frame.</summary>
    public static bool PointerOver { get; private set; }

    RectTransform root, row; CanvasGroup group; InkShape paper; InkPath shelf;
    readonly InkButton[] slots = new InkButton[5];
    readonly CubeGlyph[] cubes = new CubeGlyph[5];
    readonly InkShape[] pedestals = new InkShape[5];
    readonly float[] slotX = new float[5], slotPx = new float[5];
    InkButton dotBtn; CubeGlyph dotCube; InkShape dotPip;
    float rowW;
    int shownTicks = int.MinValue, selSize = 2, lastGroup = -1, tick = int.MinValue, popState, popK;
    bool selDotted, heldLast;
    float pulseT = 9f, pulseScale = 1f;
    Color shownCol = new Color(-1f, 0f, 0f);
    Vector2 lastTail = new Vector2(float.NaN, float.NaN);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { PulseCount = 0; PickCount = 0; AuditionCount = 0; PointerOver = false; }

    public static HudSizeRow Build(RectTransform hud)
    {
        var rt = HudKit.Node(hud, "SizeRow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
        var s = rt.gameObject.AddComponent<HudSizeRow>();
        s.root = rt;
        s.BuildParts();
        return s;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; PointerOver = false; }
    void OnDisable() { if (PointerOver) { PointerOver = false; PathManager.DraftUiHover = false; } }

    // ================================================================== build
    void BuildParts()
    {
        float total = 0f;
        for (int i = 0; i < 5; i++) { slotPx[i] = DurationPicker.SlotPx(i) * Scale; total += Mathf.Max(30f, slotPx[i] + 8f); }
        total += 4f * Gap;
        rowW = PadL + total + Gap + DotW + PadR;
        row = InkUI.Node("Row", root);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0f, 0.5f);
        row.sizeDelta = new Vector2(rowW, RowH);
        group = row.gameObject.AddComponent<CanvasGroup>();
        paper = InkShape.Create(row, "Paper", InkShape.Kind.Bubble, Comic.Cream, new Vector2(rowW, RowH));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = true;   // the paper swallows the pointer between the cubes (no tile under the row is pressed)
        paper.Radius = 18f; paper.SetInk(2f, 3.4f); paper.Wobble = 1.1f; paper.ShadowOffset = Comic.StickerShadow; paper.ShadowColor = Comic.PrintShadow;
        paper.TailWidth = 18f; paper.Seed = 9107;
        shelf = InkPath.Create(row, "Shelf", new Vector2(rowW, 20f));
        shelf.UnitPx = 1f; shelf.Wobble = 0.8f; shelf.Seed = 9109;
        InkUI.Stretch(shelf.rectTransform);
        float x = -rowW * 0.5f + PadL;
        for (int i = 0; i < 5; i++)
        {
            int ii = i;
            float px = slotPx[i], w = Mathf.Max(30f, px + 8f);
            slotX[i] = x + w * 0.5f;
            x += w + Gap;
            var b = InkButton.Create(row, "Size" + i, new Vector2(w, RowH - 6f), Words[i]);
            InkUI.Centre((RectTransform)b.transform, new Vector2(slotX[i], 0f), new Vector2(w, RowH - 6f));
            b.hoverScale = 1.06f;
            var ped = InkShape.Create(b.body, "Pedestal", InkShape.Kind.Circle, Comic.Cream, new Vector2(px * 1.3f + 10f, px * 0.36f + 6f));
            ped.raycastTarget = false; ped.SetInk(1.5f, 2.2f); ped.ShadowOffset = new Vector2(0f, -3f); ped.ShadowColor = InkUI.PaperDeep; ped.Wobble = 0.6f;
            ((RectTransform)ped.transform).anchoredPosition = new Vector2(0f, ShelfY);
            var g = CubeGlyph.Create(b.body, "Cube", Color.white, px);
            g.raycastTarget = false; g.GroundShadow = true;
            ((RectTransform)g.transform).anchoredPosition = new Vector2(0f, ShelfY + px * 0.5f + 2f);
            b.onClick = () => Pick(ii);
            b.onHover = on => { if (on) HoverSlot(ii); };
            slots[i] = b; cubes[i] = g; pedestals[i] = ped;
        }
        float shelfL = -rowW * 0.5f + PadL - 6f, shelfR = slotX[4] + Mathf.Max(30f, slotPx[4] + 8f) * 0.5f + 4f;
        shelf.Line(Comic.Ink, 2.2f, new Vector2(shelfL, ShelfY + 1f), new Vector2(shelfR, ShelfY + 1f));
        // the dot: a small cube with its satellite (the dot that orbits dotted cubes in the world) — on = the cube in colour, the dot inked
        float dx = rowW * 0.5f - PadR - DotW * 0.5f;
        dotBtn = InkButton.Create(row, "Dot", new Vector2(DotW, RowH - 6f), "dotted: half as long again");
        InkUI.Centre((RectTransform)dotBtn.transform, new Vector2(dx, 0f), new Vector2(DotW, RowH - 6f));
        dotBtn.hoverScale = 1.1f;
        dotCube = CubeGlyph.Create(dotBtn.body, "Cube", Palette.A(Comic.Ink, 0.5f), 15f);
        dotCube.raycastTarget = false; dotCube.Hollow = true;
        ((RectTransform)dotCube.transform).anchoredPosition = new Vector2(-3f, ShelfY + 9f);
        dotPip = InkShape.Create(dotBtn.body, "Pip", InkShape.Kind.Circle, Comic.Cream, new Vector2(9f, 9f));
        dotPip.raycastTarget = false; dotPip.HasShadow = false; dotPip.SetInk(1.6f, 1.8f); dotPip.Wobble = 0.1f; dotPip.RotJitter = 0f;
        ((RectTransform)dotPip.transform).anchoredPosition = new Vector2(8f, ShelfY + 21f);
        dotBtn.onClick = ToggleDotClick;
        dotBtn.onHover = on => { if (on) HoverDot(); };
        Hints.Register("sizeRow", row);
        row.gameObject.SetActive(false);
    }

    // ================================================================== the brush (package D's API)
    /// <summary>The brush length now (ticks; 24 = one beat).</summary>
    public static int BrushTicks => PathManager.I != null ? PathManager.I.BrushTicks : 24;

    /// <summary>The brush takes <paramref name="ticks"/> (PathManager.SetBrush: clamped, OnBrushChanged, the tiny length audition).</summary>
    public static void SetBrush(int ticks) { var pm = PathManager.I; if (pm != null) pm.SetBrush(ticks, true); }

    /// <summary>The brush one size shorter (−1) / longer (+1), keeping the dot (PathManager.StepBrush, auditioned).</summary>
    public static void StepBrush(int dir) { var pm = PathManager.I; if (pm != null) pm.StepBrush(dir); }

    /// <summary>The brush's dot on / off (PathManager.ToggleBrushDotted, auditioned).</summary>
    public static void ToggleBrushDotted() { var pm = PathManager.I; if (pm != null) pm.ToggleBrushDotted(); }

    /// <summary>The UI size (0 = a sixteenth … 4 = a whole note) and dot of <paramref name="ticks"/> (the nearest of the ten lengths).</summary>
    public static void Decode(int ticks, out int size, out bool dotted)
    {
        if (!AudioCube.TryDecode(ticks, out size, out dotted)) AudioCube.TryDecode(AudioCube.SnapTicks(ticks), out size, out dotted);
        size = Mathf.Clamp(size, 0, 4);
    }

    static readonly VoiceRules.NoteEvent[] ev1 = new VoiceRules.NoteEvent[1];

    /// <summary>A length audition of <paramref name="ticks"/> in the hand's sound: the hovered tile / the draft's last note when there is one
    /// (PathManager.AuditionLength), else the lit island's root in the held group's voice (at once when stopped, on the next 16th while playing).</summary>
    public static void AuditionTicks(int ticks)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        AuditionCount++;
        if (pm.AuditionLength(ticks)) return;
        if (!Synth.Ready) return;
        int g = Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1);
        int slot = Instruments.SlotOf(g, Instruments.BrushVoiceOf(g));
        bool drums = SynthBank.Def(slot).drums;
        var sm = SongManager.I;
        bool hasSong = sm != null && sm.HasSong && sm.Islands.Count > 0;
        float secs = PathManager.AuditionSeconds(ticks);
        if (!GlobalClock.IsPlaying)
        {
            int m = drums ? 38 : SynthBank.ClampToRegister(slot, (hasSong ? sm.Islands[Mathf.Clamp(sm.LitIsland, 0, sm.Islands.Count - 1)].chordRootMIDI + SongManager.Transpose : 60) + 7);
            Synth.Preview(slot, m, 80, drums ? 0.2f : secs);
            return;
        }
        double t, end;
        double b = VoiceRules.PreviewBeat(out t, out end);
        int midi = 38;
        if (!drums)
        {
            int root = 60;
            if (hasSong) { int at = Mathf.Clamp(sm.ActiveMeasureIndex((float)b), 0, sm.Islands.Count - 1); root = sm.Islands[at].chordRootMIDI + SongManager.Transpose; }
            midi = SynthBank.ClampToRegister(slot, root + 7);
        }
        double off = drums ? t + 0.2 : Math.Max(t + 0.03, Math.Min(t + secs, end - 0.005));
        ev1[0] = new VoiceRules.NoteEvent { slot = slot, midi = midi, vel = 80, onDsp = t, offDsp = off };
        VoiceRules.Dispatch(ev1, 1, VoiceRules.OwnerPreview);
    }

    // ================================================================== gestures
    int TicksFor(int size, bool click)
    {
        bool dotted = size == selSize && (click ? !selDotted : selDotted);   // the chosen size again: the dot on / off
        return AudioCube.TicksOf(size, dotted);
    }

    void HoverSlot(int size)
    {
        InkUI.Hop(cubes[size], 0.3f);
        AuditionTicks(TicksFor(size, false));
    }

    void HoverDot() { InkUI.Hop(cubes[selSize], 0.25f); AuditionTicks(AudioCube.TicksOf(selSize, !selDotted)); }

    void Pick(int size)
    {
        SetBrush(TicksFor(size, true));
        PickCount++;
        InkUI.Hop(cubes[size], 0.45f);
        AudioPool.UI(ProceduralAudio.Tick(), 0.22f, 0.9f + 0.1f * size);
        Refresh(true);
        Onboarding.Notify(DurationPicker.EvLengthPicked);
    }

    void ToggleDotClick()
    {
        ToggleBrushDotted();
        PickCount++;
        AudioPool.UI(ProceduralAudio.Tick(), 0.22f, 1.35f);
        Refresh(true);
    }

    /// <summary>Tests: a click on size <paramref name="size"/> (5 = the dot), as the pointer does it.</summary>
    public void SimClick(int size) { if (size >= 0 && size < 5) slots[size].Click(); else if (dotBtn != null) dotBtn.Click(); }
    /// <summary>Tests: the pulse, as a pick-up starts it.</summary>
    public void Pulse() { pulseT = 0f; PulseCount++; if (selSize >= 0 && selSize < 5) InkUI.Hop(cubes[selSize], 0.5f); }

    // ================================================================== per frame
    void Update()
    {
        if (row == null) return;
        var pm = PathManager.I;
        var col = HudInstruments.I;
        bool held = pm != null && pm.Hand == PathManager.HandKind.Cube;
        int g = held ? Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1) : -1;
        bool anchor = held && col != null && col.Held >= 0 && col.LiftOf(col.Held) > 0.5f;
        bool want = anchor && !col.FanOpen && !col.Mix && !CubeInspector.IsOpen && !Presenter.Active && !MainMenu.IsShown && HudAlpha() > 0.5f;
        // the pick-up pulse: a new cube in the hand (or another group's)
        if (held && (!heldLast || g != lastGroup)) Pulse();
        heldLast = held; lastGroup = g;
        if (want && (popState == 0 || popState == 3)) { popState = 1; popK = 0; if (!row.gameObject.activeSelf) row.gameObject.SetActive(true); shownTicks = int.MinValue; shownCol.r = -1f; }
        else if (!want && (popState == 1 || popState == 2)) { popState = 3; popK = 0; }
        // the pointer over the row: a press there must not finish a draft (PathManager reads DraftUiHover)
        bool over = row.gameObject.activeSelf && popState != 0 && group.blocksRaycasts && RectTransformUtility.RectangleContainsScreenPoint(row, PointerScreen, null);
        if (over != PointerOver) { PointerOver = over; PathManager.DraftUiHover = over; }
        if (popState == 0) return;
        if (pm != null && (pm.BrushTicks != shownTicks)) Refresh(false);
        Color c = g >= 0 ? Comic.Opaque(Instruments.Colors[g]) : shownCol;
        if (g >= 0 && c != shownCol) { shownCol = c; Refresh(true); }
        if (anchor) Place(col);
        Animate();
    }

    /// <summary>The five sizes as the row lays them out (reference px): cube px (tests: the fattest is the whole note).</summary>
    public float SlotCubePx(int i) => i >= 0 && i < 5 ? slotPx[i] : 0f;

    static Vector2 PointerScreen => PathManager.SimOnly ? (Vector2)PathManager.SimPos : (Vector2)Input.mousePosition;

    CanvasGroup hudGroup;
    float HudAlpha()
    {
        var ui = UIManager.I;
        if (ui == null || ui.HudRoot == null) return 1f;
        if (hudGroup == null) hudGroup = ui.HudRoot.GetComponent<CanvasGroup>();
        return hudGroup != null ? hudGroup.alpha : 1f;
    }

    /// <summary>Beside the held chip: the tongue's left edge a little right of the lifted chip, its tail on the chip's right edge.</summary>
    void Place(HudInstruments col)
    {
        var chip = col.ChipOf(col.Held);
        if (chip == null) return;
        var parent = root.parent as RectTransform;
        if (parent == null) return;
        Vector2 c = parent.InverseTransformPoint(chip.rectTransform.position);
        float half = chip.rectTransform.rect.width * 0.5f;
        Rect pr = parent.rect;
        float x = c.x + half + ChipGap + 10f;
        float y = Mathf.Clamp(c.y, pr.yMin + RowH * 0.5f + 90f, pr.yMax - RowH * 0.5f - 90f);   // clear of the rail band and the transport
        var at = new Vector2(x, y);
        if ((root.anchoredPosition - at).sqrMagnitude > 0.01f) root.anchoredPosition = at;
        // the tail points back at the chip (paper-local px: its pivot is the paper's centre)
        Vector2 tip = new Vector2(-rowW * 0.5f - ChipGap - 6f, c.y - y);
        if (float.IsNaN(lastTail.x) || (tip - lastTail).sqrMagnitude > 4f) { lastTail = tip; paper.TailTip = tip; }
    }

    /// <summary>The chosen size (the pedestal + the ink hexagon, the satellite when dotted), the others at 70 % saturation, in the group's colour.</summary>
    void Refresh(bool force)
    {
        var pm = PathManager.I;
        int ticks = pm != null ? pm.BrushTicks : 24;
        if (!force && ticks == shownTicks) return;
        bool changed = ticks != shownTicks && shownTicks != int.MinValue;
        shownTicks = ticks;
        int sz; bool dot;
        Decode(ticks, out sz, out dot);
        if (changed && sz != selSize) InkUI.Hop(cubes[sz], 0.35f);
        selSize = sz; selDotted = dot;
        Color col = shownCol.r >= 0f ? shownCol : Comic.Opaque(Palette.Accent);
        for (int i = 0; i < 5; i++)
        {
            bool on = i == selSize;
            cubes[i].color = on ? col : InkUI.Desaturate(col, 0.7f);
            cubes[i].Ring = on;
            cubes[i].Satellite = on && selDotted;
            pedestals[i].gameObject.SetActive(on);
            slots[i].Active = on;
        }
        dotBtn.Active = selDotted;
        dotPip.color = selDotted ? Comic.Ink : Comic.Cream;
        dotCube.Hollow = !selDotted;
        dotCube.color = selDotted ? col : Palette.A(Comic.Ink, 0.5f);
        dotBtn.transform.localRotation = Quaternion.Euler(0f, 0f, selDotted ? 4f : 0f);
    }

    /// <summary>On twos: the pop in / out and the pick-up pulse of the chosen cube.</summary>
    void Animate()
    {
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float dt = 1f / Look.TwosFps;
        float s = 1f, a = 1f;
        if (popState == 1) { s = PopIn[popK]; if (++popK >= PopIn.Length) popState = 2; }
        else if (popState == 3)
        {
            if (popK < PopOut.Length) { s = PopOut[popK]; a = 1f - 0.3f * popK; popK++; }
            else { row.gameObject.SetActive(false); popState = 0; return; }
        }
        row.localScale = new Vector3(s, s, 1f);
        group.alpha = a;
        bool live = popState == 2 || popState == 1;
        group.blocksRaycasts = live; group.interactable = live;
        // the pulse: the chosen cube swells and settles (1.4 → 1.25 → 1.12 → 1.05 → 1), its ring boils meanwhile
        pulseT += dt;
        float ps = 1f;
        if (pulseT < PulseSeconds)
        {
            int k = Mathf.Clamp(Mathf.FloorToInt(pulseT / PulseSeconds * PulseSteps.Length), 0, PulseSteps.Length - 1);
            ps = PulseSteps[k];
        }
        if (Mathf.Abs(ps - pulseScale) > 1e-4f)
        {
            pulseScale = ps;
            for (int i = 0; i < 5; i++) cubes[i].rectTransform.localScale = i == selSize ? new Vector3(ps, ps, 1f) : Vector3.one;
        }
    }

    /// <summary>Tests: one frame of the row's own work (GC measurements).</summary>
    public void StepForTest() { Update(); }

    static readonly float[] PopIn = { 0.6f, 1.08f, 1f }, PopOut = { 0.9f, 0.7f, 0.5f };
    static readonly float[] PulseSteps = { 1.4f, 1.25f, 1.12f, 1.05f, 1f };
}
