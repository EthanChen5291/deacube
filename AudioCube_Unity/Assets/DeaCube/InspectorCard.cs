using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SPEC v4 §6 / UI.md / ui_research §4.3: the inspected cube's card — its speech bubble. A cream paper page (wobbly ink, heavier on the shadow
/// side, a hard print shadow, tilted -1°) popping in beside the flown proxy with a comic tail pointing at it. One calm face (≤ 20 hit targets):
/// the instrument chip (click: the popover of the 10 instrument cubes, the current one toggles mute; right-click: mute; hold: spotlight), one
/// ink fader, the flat path grid (the hero), a tool row (pencil, reverse, the three path modes drawn as tiny path diagrams; the start ring of
/// the grid drags the whole path), the rhythm strip and, for a selected note, the note-length row under it. Everything else lives in the
/// "more ⋯" drawer pulled out of the card's right edge: twin / shadow / echo / rider / stamp as illustrated cards, gate as cube drawings, the
/// octave ladder and, for legacy (uniform-step) cubes only, the necklace and the pace row. Delete is a small red tear-off in the bottom-left
/// corner, close a sticker on the top-right corner. Every control acts on CubeInspector.Current and every gesture that changes the song ends
/// in exactly one History.Push. Pictures at rest, words only in hover captions (InkCaption).
/// Interim (until U1 removes UIManager's old cube panel): the old panel is scaled to zero while this card is up.
/// v6 (SPEC v6 §8.3, package U2) — "any given cube path pasted onto the next grid": the header gains COPY (two paper cards, the front one
/// carrying the pattern: Clipboard.Copy, the clipboard chip flies in) and PASTE → NEXT GRID (a pattern hopping along an arc onto the next
/// grid: the card closes, then Clipboard.PasteNext — the adapted copy flies there, one History entry; refused on a Moon or when no column
/// follows); the sound popover (the chip) shows under the ten groups the VOICES of the cube's group as small cubes with their pips (hover =
/// audition + the voice's name, click = the cube's sound changes: AudioCube.SetVoice, one History entry; a group pick keeps the popover up
/// on the new group's voices — the cube takes that group's brush voice); the chip wears the cube's voice pips and its caption is the voice's name.
/// v7 (SPEC v7 §8.1, package U2) — "for a given cube, there should also be controls to duplicate that cube down an octave or up an octave,
/// simple UI. OR ... a support harmony of that cube (5ths or 4ths basically)": under the rhythm strip (where a selected note's length row docks)
/// the IDEAS row — OCTAVE COPY (the cube with a glass ghost above and below it: the upper half copies it an octave up into the sky layer, the
/// lower half an octave down into the deep layer: CubeOps.OctaveCopy), HARMONY (the cube with a smaller partner above and below: hover = the
/// harmony previews — ghost beads and both voices, CubeOps.PreviewHarmony — click = CubeOps.Harmonize) and FLIP (the pattern over a mirror
/// line: CubeOps.Flip, "an instant answer"); each is one History entry (CubeOps pushes) and the card stays open. The row steps aside while a
/// note is selected (the length row takes the slot: the face stays ≤ 20 targets). Hovering paste → next grid (or a right-click on it) fans the
/// PASTE MODES (<see cref="PasteFan"/>: plain / echo ↑ / echo ↓ + late / step ↑ / step ↓ / answer): a pick copies the cube, the card closes and
/// the pattern lands on the next grid in that mode (Clipboard.PasteNext(mode)); a plain click stays the v6 paste.
/// v7 §21 ("just keep cubes on each grid"): the RIDER card is retired — a cube never leaves its grid, a saved rider plays on its own grid like
/// any cube — so the drawer's second row is stamp + octave, centred under the first.
/// </summary>
[DefaultExecutionOrder(210)]   // after CubeInspector (200): the proxy is posed for this frame
public class InspectorCard : MonoBehaviour
{
    // ------------------------------------------------------------------ layout (1920 x 1080 reference units)
    public const float W = 560f, Pad = 24f, HeaderTop = 18f, HeaderH = 58f, GridGap = 10f, ToolsH = 48f, RowH = 80f, BottomPad = 24f;
    public const float TailGap = 58f, DrawerW = 380f, TopMargin = 78f, BottomMargin = 20f, Tilt = -1f, ChipPx = 44f, FaderW = 180f;
    const float AppearAt = 0.3f;   // the card pops in this far into the proxy's flight
    const string ClosePopoverId = "u2.pop";

    public static InspectorCard I;
    /// <summary>The card is built (it replaces UIManager's cube panel).</summary>
    public static bool Live => I != null && I.built;
    /// <summary>The card's root (null before it is built).</summary>
    public static RectTransform Root => I != null ? I.card : null;
    public static PathGridView Grid => I != null ? I.grid : null;
    public static RhythmStrip Strip => I != null ? I.strip : null;
    /// <summary>Pop-in progress 0..1 (1 = fully in).</summary>
    public static float Shown => I != null ? I.shown : 0f;
    public static bool DrawerOpen => I != null && I.drawerOpen;
    /// <summary>The instrument popover, the twin variants or (v7) the paste fan are open (Esc / a click elsewhere closes them first).</summary>
    public static bool PopoverOpen => I != null && ((I.instPop != null && I.instPop.gameObject.activeSelf) || (I.twinPop != null && I.twinPop.gameObject.activeSelf) || (I.pasteFan != null && I.pasteFan.IsOpen));
    /// <summary>A note of the strip is selected (Backspace / Delete then delete that note, not the cube).</summary>
    public static bool NoteSelected => I != null && I.strip != null && I.cube != null && I.strip.SelectedNote >= 0;
    /// <summary>Where the note-length row docks (under the strip).</summary>
    public static RectTransform LengthSlot => I != null ? I.lengthSlot : null;
    /// <summary>The cube the card shows (null when closed).</summary>
    public static AudioCube Cube => I != null ? I.cube : null;

    public static InspectorCard Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<InspectorCard>();
            if (I == null) I = new GameObject("InspectorCard").AddComponent<InspectorCard>();
            I.Build();
        }
        return I;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        I = null; pastePending = false; CopyCount = 0; PasteNextCount = 0; VoicePicks = 0; LastPasteNext = null;
        pendingMode = Clipboard.PasteMode.Plain; LastPasteMode = Clipboard.PasteMode.Plain; IdeaCount = 0; PreviewCount = 0; LastIdea = null;
    }

    // ------------------------------------------------------------------ parts
    bool built;
    RectTransform card, lengthSlot, popLayer, drawer, drawerBody, legacyRt;
    // v9 (N): the selected note's nudge — "higher" ▲ / "lower" ▼ at the left of the length row (DeaCube/Nudge.cs)
    RectTransform nudgeRt; InkButton nudgeUp, nudgeDown; InkPath nudgeUpArt, nudgeDownArt; int nudgeShownNode = -2, nudgeShown = int.MinValue;
    public const float NudgeX = -206f, NudgeBtnW = 72f, NudgeBtnH = 36f;
    InkShape paper, drawerPaper;
    CanvasGroup cardGroup, drawerGroup;
    PathGridView grid; RhythmStrip strip;
    InkButton chip, closeBtn, delBtn, pencilBtn, reverseBtn, moreTab;
    readonly InkButton[] modeBtns = new InkButton[3];
    readonly InkPath[] modeArt = new InkPath[3];
    InkWave modeWave;
    CubeGlyph chipCube; InkShape pencilBack; InkPath pencilArt, reverseArt, lengthGhost;
    HudSlider fader; RectTransform faderRt; InkPath faderFill; InkShape knob;
    // drawer
    InkButton twinCard, shadowCard, echoCard, stampCard, octUp, octDown;
    readonly InkButton[] gateBtns = new InkButton[3];
    readonly InkButton[] paceBtns = new InkButton[5];
    readonly CubeGlyph[] paceCubes = new CubeGlyph[5];
    readonly List<CubeGlyph> tinted = new List<CubeGlyph>();   // drawer cubes in the cube's colour
    readonly CubeGlyph[] shadowCopies = new CubeGlyph[3], echoCopies = new CubeGlyph[2];
    InkPath ladder, shimmerArt; InkWave gateWave; NecklaceDial dial;
    // popovers
    RectTransform instPop, twinPop; InkShape instPopPaper, twinPopPaper;
    readonly CubeGlyph[] instCubes = new CubeGlyph[Instruments.Count];
    readonly InkButton[] instBtns = new InkButton[Instruments.Count];
    // v6: copy / paste → next grid on the face, the chip's voice pips, the popover's voice row
    InkButton copyBtn, pasteBtn; InkPath copyArt, pasteArt; InkPainter chipMark;
    const int MaxVoiceChips = 12;
    const float VoicePitch = 40f, VoiceRowH = 44f, PopW = 268f;
    /// <summary>v9 (G): the instrument popover's height without voices — its rows of five instrument chips (two for ten groups, three with FX) —
    /// and where the voice row starts under them (ten groups: 118 / 136 as before).</summary>
    static float InstPopH => 14f + ((Instruments.Count + 4) / 5) * 52f;
    static float VoiceTop => InstPopH + 18f;
    readonly InkButton[] voiceBtns = new InkButton[MaxVoiceChips];
    readonly InkPainter[] voiceArt = new InkPainter[MaxVoiceChips];
    InkWave voiceWave; int voiceN;
    static bool pastePending; static float pasteAskedT;
    // v7: the ideas row (octave copy ↑ / ↓, harmony ↑ / ↓, flip) and the paste fan on paste → next grid
    RectTransform ideasRt, octRt, harmRt;
    InkButton octCopyUp, octCopyDown, harmUp, harmDown, flipBtn, climbBtn;
    CubeGlyph octCube, octGhostUp, octGhostDown, harmCube, harmUpCube, harmDownCube;
    InkPath octMarks, harmTies, flipArt, climbArt;
    PasteFan pasteFan;
    InkPainter harmGhost; CubeOps.HarmonyPlan ghostPlan; Color ghostCol;
    static Clipboard.PasteMode pendingMode;
    float pasteHoverT = -1f; int harmHover; float harmHoverT; bool harmPreviewing, upExists, downExists;

    // ------------------------------------------------------------------ state
    AudioCube cube; float shown, popT; bool want, appear, drawerOpen; float drawerT;
    int lookSig = int.MinValue, gridRows = -1, gridCols = -1; float H = 640f;
    Vector2 lastTip = new Vector2(float.NaN, float.NaN);
    AudioCube spotCube; bool chipHold;
    int popOpenedFrame = -9;
    static readonly StepLen[] PaceOrder = { StepLen.Half, StepLen.Quarter, StepLen.Eighth, StepLen.Triplet, StepLen.Sixteenth };
    static readonly Vector3[] corners = new Vector3[4];
    static readonly float[] PopIn = { 0.62f, 0.9f, 1.06f, 1f }, PopOut = { 0.55f, 0.72f, 0.9f, 1f };

    void Awake() { if (I == null) I = this; }
    void OnDestroy() { if (I == this) I = null; if (spotCube != null) Performance.HoldSpotlight(spotCube, false); }

    // ================================================================== build
    void Build()
    {
        if (built) return;
        var root = InkUI.OverlayRoot;
        card = InkUI.Node("InspectorCard", root);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(W, H);
        card.localRotation = Quaternion.Euler(0f, 0f, Tilt);
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();

        BuildDrawer();   // behind the paper: it slides out from under the card's right edge

        paper = InkShape.Create(card, "Paper", InkShape.Kind.Bubble, InkUI.Paper, new Vector2(W, H));
        InkUI.Stretch(paper.rectTransform);
        paper.raycastTarget = true;                       // the page swallows clicks (no click reaches the dim through it)
        paper.Radius = 26f; paper.Wobble = 1.4f; paper.SetInk(2.6f, 4.4f);
        paper.ShadowOffset = new Vector2(7f, -9f); paper.ShadowColor = Comic.PrintShadow;
        paper.RotJitter = 0.4f; paper.TailWidth = 44f; paper.Seed = 4211;

        BuildHeader();
        grid = PathGridView.Create(card);
        // v7: the harmony preview as ghost beads on the card's own grid (the world's ghosts sit under the card while inspecting)
        harmGhost = InkPainter.Create(card, "HarmonyGhost", new Vector2(100f, 100f), PaintHarmonyGhost);
        harmGhost.raycastTarget = false;
        harmGhost.gameObject.SetActive(false);
        strip = RhythmStrip.Create(card);
        strip.OnNoteSelected += HandleNoteSelected;
        BuildTools();
        lengthSlot = InkUI.Node("LengthSlot", card);
        // the empty state (no note selected): a dotted shelf with the five lengths as faint ghost cubes — select a note to size it
        lengthGhost = InkPath.Create(lengthSlot, "EmptyShelf", new Vector2(260f, 60f));
        lengthGhost.UnitPx = 1f; lengthGhost.Wobble = 0.4f;
        for (float x = -120f; x < 120f; x += 13f) lengthGhost.Line(Palette.A(InkUI.Ink, 0.28f), 2f, new Vector2(x, -20f), new Vector2(x + 6f, -20f));
        float gx = -101f;
        for (int i = 0; i < 5; i++)
        {
            float px = DurationPicker.SlotPx(i);
            var g = CubeGlyph.Create(lengthGhost.transform, "Ghost" + i, Palette.A(InkUI.Ink, 0.3f), px);
            g.raycastTarget = false; g.Hollow = true;
            ((RectTransform)g.transform).anchoredPosition = new Vector2(gx + px * 0.5f, -20f + px * 0.5f + 1f);
            gx += px + DurationPicker.Gap;
        }
        lengthGhost.gameObject.SetActive(false);          // v7: the ideas row is the slot's resting state (the length row replaces it for a note)
        BuildIdeas();
        BuildNudge();
        BuildCorners();
        popLayer = InkUI.Node("Popovers", root);          // above the card, not tilted
        InkUI.Stretch(popLayer);
        BuildInstrumentPopover();
        BuildTwinPopover();
        pasteFan = PasteFan.Create(popLayer, "PasteFan");
        pasteFan.onPick = PasteModeClick;
        pasteFan.keepOpen = () => pasteBtn != null && pasteBtn.Hovered;

        Layout(-1, -1);   // placed for real on the first bind (the island's size decides the card's height)
        card.gameObject.SetActive(false);
        built = true;
        RegisterHints();
    }

    void RegisterHints()
    {
        Hints.Register("inspector", card);
        Hints.Register("inspector.grid", grid);
        Hints.Register("inspector.volume", faderRt);
        Hints.Register("inspector.close", closeBtn);
        Hints.Register("inspector.swatches", chip);
        Hints.Register("inspector.rhythm", strip);
        Hints.Register("inspector.necklace", dial != null && !IsDurations ? (Component)dial : moreTab);
        Hints.Register("inspector.octave", octRt);
        Hints.Register("inspector.harmony", harmRt);
        Hints.Register("inspector.flip", flipBtn);
        Hints.Register("inspector.paste", pasteBtn);
    }

    // ------------------------------------------------------------------ v7 the ideas row: octave copy ↑ / ↓, harmony ↑ / ↓, flip
    public const float IdeaGroupW = 84f, IdeaHalfH = 38f, IdeaSpread = 122f;   // (the pitch between the four groups: octave, harmony, flip, climb)

    void BuildIdeas()
    {
        ideasRt = InkUI.Node("Ideas", lengthSlot);
        InkUI.Stretch(ideasRt);
        // octave copy: the cube, a glass ghost above (the sky layer: up and back) and one below (the deep layer: down and toward you)
        octRt = InkUI.Node("OctaveCopy", ideasRt);
        InkUI.Centre(octRt, new Vector2(-1.5f * IdeaSpread, 0f), new Vector2(IdeaGroupW, 2f * IdeaHalfH));
        octMarks = InkPath.Create(octRt, "Marks", new Vector2(IdeaGroupW, 2f * IdeaHalfH));
        octMarks.UnitPx = 1f; octMarks.Wobble = 0.5f; octMarks.Seed = 7811;
        octGhostUp = IdeaCube(octRt, "GhostUp", new Vector2(9f, 24f), 25f);
        octCube = IdeaCube(octRt, "Cube", new Vector2(0f, 0f), 30f);
        octGhostDown = IdeaCube(octRt, "GhostDown", new Vector2(-9f, -24f), 25f);
        octCopyUp = IdeaHalf(octRt, "Up", true, "octave copy up");
        octCopyDown = IdeaHalf(octRt, "Down", false, "octave copy down");
        octCopyUp.onClick = () => OctaveCopyClick(1);
        octCopyDown.onClick = () => OctaveCopyClick(-1);
        octCopyUp.onHover = on => IdeaHover(octGhostUp, on, true);
        octCopyDown.onHover = on => IdeaHover(octGhostDown, on, true);
        // harmony: the cube with a smaller partner above and below (a duet), a slur joining each
        harmRt = InkUI.Node("Harmony", ideasRt);
        InkUI.Centre(harmRt, new Vector2(-0.5f * IdeaSpread, 0f), new Vector2(IdeaGroupW, 2f * IdeaHalfH));
        harmTies = InkPath.Create(harmRt, "Ties", new Vector2(IdeaGroupW, 2f * IdeaHalfH));
        harmTies.UnitPx = 1f; harmTies.Wobble = 0.5f; harmTies.Seed = 7813;
        harmUpCube = IdeaCube(harmRt, "PartnerUp", new Vector2(14f, 23f), 21f);
        harmCube = IdeaCube(harmRt, "Cube", new Vector2(-9f, 0f), 30f);
        harmDownCube = IdeaCube(harmRt, "PartnerDown", new Vector2(14f, -23f), 21f);
        harmUp = IdeaHalf(harmRt, "Above", true, "harmony above");
        harmDown = IdeaHalf(harmRt, "Below", false, "harmony below");
        harmUp.onClick = () => HarmonyClick(true);
        harmDown.onClick = () => HarmonyClick(false);
        harmUp.onHover = on => HarmonyHover(true, on);
        harmDown.onHover = on => HarmonyHover(false, on);
        // flip: the pattern over a mirror line and its mirror image
        flipBtn = InkButton.Create(ideasRt, "Flip", new Vector2(72f, 76f), "flip upside down");
        InkUI.Centre((RectTransform)flipBtn.transform, new Vector2(0.5f * IdeaSpread, 0f), new Vector2(72f, 76f));
        flipArt = flipBtn.AddDrawing(new Vector2(68f, 72f));
        flipArt.UnitPx = 1f; flipArt.Wobble = 0.6f; flipArt.Seed = 7817;
        flipBtn.hoverScale = 1.1f;
        flipBtn.onClick = FlipClick;
        // climb (DeaCube/Climb.cs): the path's high note reaches higher — off → ×1 → ×2 → ×3 → off (the pips)
        climbBtn = InkButton.Create(ideasRt, "Climb", new Vector2(72f, 76f), Climb.Caption(0));
        InkUI.Centre((RectTransform)climbBtn.transform, new Vector2(1.5f * IdeaSpread, 0f), new Vector2(72f, 76f));
        climbArt = climbBtn.AddDrawing(new Vector2(68f, 72f));
        climbArt.UnitPx = 1f; climbArt.Wobble = 0.6f; climbArt.Seed = 7819;
        climbBtn.hoverScale = 1.1f;
        climbBtn.onClick = ClimbClick;
    }

    /// <summary>CLIMB: the inspected path's high notes reach higher (Climb.Set, one History entry): off → ×1 → ×2 → ×3 → off.</summary>
    void ClimbClick()
    {
        var c = cube;
        if (c == null || !Climb.CanClimb(c)) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return; }
        int want = Climb.Next(c.climb);
        if (!Climb.Set(c, want)) return;
        Pop(climbBtn);
        InkCaption.Attach(climbBtn.gameObject, Climb.Caption(want));
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.85f + 0.1f * want);
        lookSig = int.MinValue;
    }

    /// <summary>The climb picture: a little path whose top bead climbs to a higher one (an up arrow between), pips for the level.</summary>
    void DrawClimb(Color col, int level)
    {
        if (climbArt == null) return;
        Color c = Comic.Opaque(col), faint = Palette.A(InkUI.Ink, 0.5f);
        climbArt.Clear();
        Vector2 a0 = new Vector2(-24f, -12f), a1 = new Vector2(-13f, 0f), a2 = new Vector2(-3f, -8f), a3 = new Vector2(9f, 2f), up = new Vector2(9f, 24f);
        climbArt.Line(InkUI.Ink, 4.4f, a0, a1, a2, a3).Line(c, 2.4f, a0, a1, a2, a3);
        climbArt.Dot(InkUI.Ink, a0, 3.5f).Dot(InkUI.Ink, a1, 3.5f).Dot(InkUI.Ink, a2, 3.5f).Dot(InkUI.Ink, a3, 3.5f);
        climbArt.Dot(c, a0, 2.2f).Dot(c, a1, 2.2f).Dot(c, a2, 2.2f).Dot(c, a3, 2.2f);
        Color arrow = level > 0 ? InkUI.Ink : faint;
        climbArt.Line(arrow, 2f, a3 + new Vector2(0f, 5f), up - new Vector2(0f, 9f));
        InkDraw.ArrowHead(climbArt, arrow, 2f, up - new Vector2(0f, 6f), new Vector2(0f, 1f), 5f);
        climbArt.Dot(arrow, up, 4.4f).Dot(level > 0 ? Comic.Pop : InkUI.Paper, up, 3f);
        for (int k = 0; k < Climb.MaxLevel; k++) { var pip = new Vector2(-10f + k * 10f, -28f); climbArt.Dot(InkUI.Ink, pip, 2.8f).Dot(k < level ? Comic.Pop : InkUI.Paper, pip, 1.7f); }
    }

    CubeGlyph IdeaCube(Transform parent, string name, Vector2 pos, float px)
    {
        var g = CubeGlyph.Create(parent, name, Color.white, px);
        g.raycastTarget = false;
        ((RectTransform)g.transform).anchoredPosition = pos;
        return g;
    }

    /// <summary>The upper / lower half of an idea's picture as its own hit area (a transparent InkButton).</summary>
    static InkButton IdeaHalf(RectTransform parent, string name, bool upper, string caption)
    {
        var b = InkButton.Create(parent, name, new Vector2(IdeaGroupW, IdeaHalfH), caption);
        InkUI.Centre((RectTransform)b.transform, new Vector2(0f, upper ? IdeaHalfH * 0.5f : -IdeaHalfH * 0.5f), new Vector2(IdeaGroupW, IdeaHalfH));
        b.hoverScale = 1f; b.pressOffset = Vector2.zero;
        return b;
    }

    /// <summary>A hovered half: its ghost fills and hops, its arrow inks in (the picture says which way the copy goes).</summary>
    void IdeaHover(CubeGlyph g, bool on, bool ghost)
    {
        if (g == null) return;
        if (ghost) PaintGhost(g, on || (g == octGhostUp ? upExists : downExists));
        if (on) InkUI.Hop(g, 0.3f);
        DrawIdeaMarks();
    }

    /// <summary>An octave ghost: glass (the cube's colour washed out, a soft ink) at rest; solid once its copy exists or while hovered.</summary>
    void PaintGhost(CubeGlyph g, bool solid)
    {
        Color col = cube != null ? Comic.Opaque(cube.Color) : Palette.Accent;
        g.Hollow = false;
        g.color = solid ? col : Palette.A(Color.Lerp(col, Color.white, 0.55f), 0.55f);
        g.Ink = solid ? InkUI.Ink : Palette.A(InkUI.Ink, 0.5f);
    }

    /// <summary>The harmony being previewed, as ghost beads on the card's grid: dashed rings on its tiles joined by a dashed line in the
    /// partner's colour (a bent note wears a small tick toward its bend).</summary>
    void PaintHarmonyGhost(InkPainter p)
    {
        var plan = ghostPlan;
        if (plan == null || plan.tiles == null || grid == null) return;
        ghostPts.Clear();
        Vector2 lift = new Vector2(7f, 7f);
        for (int i = 0; i < plan.tiles.Length; i++)
        {
            var t = plan.tiles[i];
            if (t == null || (plan.pitch != null && i < plan.pitch.Length && plan.pitch[i] < 0)) continue;
            ghostPts.Add(grid.CellPos(t.gridX, t.gridZ) + lift);
        }
        if (ghostPts.Count >= 2) { p.Stroke(ghostPts, ghostPts.Count, 6f, InkUI.Ink, false, 7f, 4f); p.Stroke(ghostPts, ghostPts.Count, 3f, InkUI.Paper, false, 7f, 4f); }
        int k = 0;
        for (int i = 0; i < plan.tiles.Length; i++)
        {
            var t = plan.tiles[i];
            if (t == null || (plan.pitch != null && i < plan.pitch.Length && plan.pitch[i] < 0)) continue;
            Vector2 c = ghostPts[k++];
            p.Disc(c + new Vector2(2f, -2.5f), 11f, Palette.A(InkUI.Ink, 0.6f));   // a hard little shadow: it floats
            p.Disc(c, 11f, InkUI.Paper);
            p.Arc(c, 11f, 3f, InkUI.Ink, 0f, 360f, 4f, 2.5f);
            p.Disc(c, 5.5f, InkUI.Ink);
            p.Disc(c, 4f, ghostCol);
            int b = plan.bend != null && i < plan.bend.Length ? plan.bend[i] : 0;
            if (b != 0) p.Line(c + new Vector2(12f, 0f), c + new Vector2(12f, b > 0 ? 9f : -9f), 2.4f, InkUI.Ink, true);
        }
    }
    static readonly List<Vector2> ghostPts = new List<Vector2>(32);

    /// <summary>An octave copy of <paramref name="c"/> already lives in <paramref name="layer"/> (its ghost in the picture is filled).</summary>
    static bool HasLayerCopy(AudioCube c, int layer)
    {
        foreach (var x in SequenceMaster.Cubes) if (x != null && x != c && x.isFinalized && x.echoOf == c.id && x.layer == layer) return true;
        return false;
    }

    void DrawIdeaMarks()
    {
        if (octMarks == null) return;
        Color col = cube != null ? Comic.Opaque(cube.Color) : Palette.Accent;
        bool upOn = octCopyUp != null && octCopyUp.Hovered, dnOn = octCopyDown != null && octCopyDown.Hovered;
        octMarks.Clear();
        // thin glass threads from the cube to its ghosts, and a chevron beside each (inked while its half is hovered)
        octMarks.Line(Palette.A(col, 0.7f), 1.6f, new Vector2(3f, 12f), new Vector2(7f, 16f)).Line(Palette.A(col, 0.7f), 1.6f, new Vector2(-3f, -12f), new Vector2(-7f, -16f));
        octMarks.Line(upOn ? InkUI.Ink : Palette.A(InkUI.Ink, 0.4f), 2.6f, new Vector2(27f, 16f), new Vector2(33f, 24f), new Vector2(39f, 16f));
        octMarks.Line(dnOn ? InkUI.Ink : Palette.A(InkUI.Ink, 0.4f), 2.6f, new Vector2(-39f, -16f), new Vector2(-33f, -24f), new Vector2(-27f, -16f));
        bool hu = harmUp != null && harmUp.Hovered, hd = harmDown != null && harmDown.Hovered;
        harmTies.Clear();
        harmTies.Arc(hu ? InkUI.Ink : Palette.A(InkUI.Ink, 0.5f), hu ? 2.6f : 1.8f, new Vector2(2f, 10f), 12f, 190f, 80f, 8);
        harmTies.Arc(hd ? InkUI.Ink : Palette.A(InkUI.Ink, 0.5f), hd ? 2.6f : 1.8f, new Vector2(2f, -10f), 12f, 170f, 280f, 8);
    }

    void DrawFlip(Color col)
    {
        if (flipArt == null) return;
        Color c = Comic.Opaque(col), faint = Color.Lerp(c, InkUI.Paper, 0.45f);
        flipArt.Clear();
        // the mirror line (dashed), the pattern above it, its mirror image below, a ↕ on the side
        for (float x = -24f; x < 20f; x += 8f) flipArt.Line(Palette.A(InkUI.Ink, 0.6f), 1.6f, new Vector2(x, 0f), new Vector2(x + 4.5f, 0f));
        Vector2 a0 = new Vector2(-18f, 6f), a1 = new Vector2(-8f, 20f), a2 = new Vector2(3f, 10f), a3 = new Vector2(12f, 25f);
        flipArt.Line(InkUI.Ink, 4.4f, a0, a1, a2, a3).Line(c, 2.4f, a0, a1, a2, a3);
        flipArt.Dot(InkUI.Ink, a0, 3.5f).Dot(InkUI.Ink, a1, 3.5f).Dot(InkUI.Ink, a2, 3.5f).Dot(InkUI.Ink, a3, 3.5f);
        flipArt.Dot(c, a0, 2.2f).Dot(c, a1, 2.2f).Dot(c, a2, 2.2f).Dot(c, a3, 2.2f);
        Vector2 b0 = new Vector2(a0.x, -a0.y), b1 = new Vector2(a1.x, -a1.y), b2 = new Vector2(a2.x, -a2.y), b3 = new Vector2(a3.x, -a3.y);
        flipArt.Line(Palette.A(InkUI.Ink, 0.55f), 3.8f, b0, b1, b2, b3).Line(faint, 2f, b0, b1, b2, b3);
        flipArt.Dot(Palette.A(InkUI.Ink, 0.55f), b0, 3.1f).Dot(Palette.A(InkUI.Ink, 0.55f), b1, 3.1f).Dot(Palette.A(InkUI.Ink, 0.55f), b2, 3.1f).Dot(Palette.A(InkUI.Ink, 0.55f), b3, 3.1f);
        flipArt.Dot(faint, b0, 1.9f).Dot(faint, b1, 1.9f).Dot(faint, b2, 1.9f).Dot(faint, b3, 1.9f);
        flipArt.Line(InkUI.Ink, 2f, new Vector2(24f, -16f), new Vector2(24f, 16f));
        InkDraw.ArrowHead(flipArt, InkUI.Ink, 2f, new Vector2(24f, 19f), new Vector2(0f, 1f), 5f);
        InkDraw.ArrowHead(flipArt, InkUI.Ink, 2f, new Vector2(24f, -19f), new Vector2(0f, -1f), 5f);
    }

    /// <summary>The ideas made from the card (octave copies, harmonies, flips; tests) and the last cube one made.</summary>
    public static int IdeaCount { get; private set; }
    public static AudioCube LastIdea { get; private set; }
    /// <summary>Harmony previews the card started (tests) and whether one runs now.</summary>
    public static int PreviewCount { get; private set; }
    public static bool HarmonyPreviewing => I != null && I.harmPreviewing;
    /// <summary>The ideas row is up (no note selected).</summary>
    public static bool IdeasShown => I != null && I.ideasRt != null && I.ideasRt.gameObject.activeInHierarchy;

    /// <summary>An octave copy of the inspected cube into the layer above (+1) / below (−1): CubeOps.OctaveCopy (one History entry).</summary>
    void OctaveCopyClick(int dir)
    {
        var c = cube;
        if (c == null) return;
        var made = CubeOps.OctaveCopy(c, dir);   // (its birth pop / a refusal's thud are CubeOps')
        var g = dir > 0 ? octGhostUp : octGhostDown;
        if (made == null) return;
        IdeaCount++; LastIdea = made;
        InkUI.Hop(g, 0.6f);
        Onboarding.Notify(Onboarding.Ev.OctaveCopied);
        lookSig = int.MinValue;
    }

    void HarmonyHover(bool above, bool on)
    {
        IdeaHover(above ? harmUpCube : harmDownCube, on, false);
        if (on) { harmHover = above ? 1 : -1; harmHoverT = Time.unscaledTime; }
        else if (harmHover == (above ? 1 : -1)) { harmHover = 0; EndHarmonyPreview(); }
    }

    /// <summary>After a short rest on a harmony half the harmony previews (ghost beads + both voices; CubeOps.PreviewHarmony every hovered
    /// frame — it rebuilds only for a new cube or side); leaving it ends the preview.</summary>
    void UpdateHarmonyPreview()
    {
        if (harmHover == 0 || cube == null) return;
        if (Time.unscaledTime - harmHoverT < 0.15f) return;
        if (!harmPreviewing)
        {
            harmPreviewing = true; PreviewCount++;
            ghostPlan = CubeOps.PlanHarmony(cube, harmHover > 0);
            ghostCol = Comic.Opaque(cube.Color);
            harmGhost.gameObject.SetActive(ghostPlan != null);
            harmGhost.Repaint();
        }
        CubeOps.PreviewHarmony(cube, harmHover > 0);
    }

    void EndHarmonyPreview()
    {
        if (!harmPreviewing) return;
        harmPreviewing = false; ghostPlan = null;
        if (harmGhost != null) harmGhost.gameObject.SetActive(false);
        CubeOps.EndPreview();
    }

    /// <summary>The harmony preview's ghost beads show on the card's grid (tests).</summary>
    public static bool HarmonyGhostShown => I != null && I.harmGhost != null && I.harmGhost.gameObject.activeInHierarchy && I.ghostPlan != null;

    /// <summary>A support harmony above / below the inspected cube: CubeOps.Harmonize (one History entry).</summary>
    void HarmonyClick(bool above)
    {
        var c = cube;
        if (c == null) return;
        EndHarmonyPreview();
        harmHover = 0;
        var made = CubeOps.Harmonize(c, above);
        if (made == null) return;
        IdeaCount++; LastIdea = made;
        InkUI.Hop(above ? harmUpCube : harmDownCube, 0.6f);
        Onboarding.Notify(Onboarding.Ev.HarmonyAdded);
        lookSig = int.MinValue;
    }

    /// <summary>The inspected cube's path upside down: CubeOps.Flip (one History entry).</summary>
    void FlipClick()
    {
        var c = cube;
        if (c == null || c.nodes.Count == 0) return;
        bool ok = CubeOps.FlipRefusal(c) == null;
        CubeOps.Flip(c);   // (the whoosh / a refusal's thud are CubeOps')
        if (!ok) return;
        IdeaCount++; LastIdea = c;
        Pop(flipBtn);
        Onboarding.Notify(Onboarding.Ev.CubeFlipped);
        lookSig = int.MinValue;
    }

    // ------------------------------------------------------------------ v7 the paste fan (hover paste → next grid, or right-click it)
    void OpenPasteFan(bool byHover)
    {
        var c = cube;
        if (c == null || pasteFan == null || !pasteBtn.Interactable) return;
        CloseInstrumentPopover(); CloseTwinPopover();
        pasteFan.SetColor(c.Color);
        pasteFan.SetSource(c.layer, c.IsDrums);
        pasteFan.Open((RectTransform)pasteBtn.transform, PasteFan.Side.Below, byHover);
        popOpenedFrame = Time.frameCount;
    }

    /// <summary>A paste fan pick: copy the cube, close the card, then paste it on the next grid in that mode (one History entry).</summary>
    void PasteModeClick(Clipboard.PasteMode mode)
    {
        var c = cube;
        if (c == null) return;
        pasteFan.Close();
        if (!CanPasteNext(c) || !Clipboard.Copy(c)) { AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return; }
        HudClipboard.NoteCopySource(ScreenOf(pasteBtn.transform));
        Pop(pasteBtn);
        PasteNextAfterClose(mode);
    }

    /// <summary>The paste fan (tests).</summary>
    public static PasteFan Fan => I != null ? I.pasteFan : null;
    public static InkButton OctaveCopyButton(int dir) => I == null ? null : (dir > 0 ? I.octCopyUp : I.octCopyDown);
    public static InkButton HarmonyButton(bool above) => I == null ? null : (above ? I.harmUp : I.harmDown);
    public static InkButton FlipButton => I != null ? I.flipBtn : null;
    public static InkButton ClimbButton => I != null ? I.climbBtn : null;
    /// <summary>The mode the last paste → next used.</summary>
    public static Clipboard.PasteMode LastPasteMode { get; private set; }

    bool IsDurations => cube != null && cube.HasDurations;

    // ------------------------------------------------------------------ header: instrument chip + the ink fader
    void BuildHeader()
    {
        chip = InkButton.Create(card, "Chip", new Vector2(58f, 58f), "sound");
        chip.holdSeconds = 0.35f;
        chipCube = CubeGlyph.Create(chip.body, "Cube", Color.white, ChipPx);
        chipCube.raycastTarget = false; chipCube.GroundShadow = true;
        chip.onClick = ToggleInstrumentPopover;
        chip.onRightClick = ToggleMute;
        chip.onHoldChanged = on => { chipHold = on; };
        // v6: the cube's voice pips on the chip's cube
        var mk = InkUI.Node("Mark", chipCube.rectTransform);
        InkUI.Stretch(mk);
        chipMark = mk.gameObject.AddComponent<InkPainter>(); chipMark.raycastTarget = false;
        chipMark.onPaint = p => { var c = cube; if (c == null) return; Rect r = p.Area; VoiceMark.Paint(p, r.center, Mathf.Min(r.width, r.height), ShownVoice(c), InkUI.Ink); };

        // v6: copy (two paper cards, the front one carrying the pattern) and paste → next grid (the pattern hops along an arc onto the next grid)
        copyBtn = InkButton.Create(card, "Copy", new Vector2(48f, 46f), "copy");
        copyArt = copyBtn.AddDrawing(new Vector2(44f, 42f));
        copyBtn.onClick = CopyClick;
        copyBtn.hoverScale = 1.12f;
        pasteBtn = InkButton.Create(card, "PasteNext", new Vector2(60f, 48f), "paste on the next grid");
        pasteArt = pasteBtn.AddDrawing(new Vector2(58f, 46f));
        pasteBtn.onClick = PasteNextClick;
        pasteBtn.hoverScale = 1.12f;
        // v7: hovering it (or a right-click) fans the paste modes
        pasteBtn.onHover = on => { pasteHoverT = on ? Time.unscaledTime : -1f; };
        pasteBtn.onRightClick = () => OpenPasteFan(false);

        faderRt = InkUI.Node("Volume", card);
        faderRt.sizeDelta = new Vector2(FaderW + 44f, 40f);
        InkUI.HitArea(faderRt.gameObject);
        var speaker = InkPath.Create(faderRt, "Speaker", new Vector2(24f, 24f));
        InkUI.Centre(speaker.rectTransform, new Vector2(-(FaderW + 44f) * 0.5f + 12f, 0f), new Vector2(24f, 24f));
        InkDraw.Speaker(speaker, InkUI.Ink, InkUI.Paper);
        var trackRt = InkUI.Node("Track", faderRt);
        InkUI.Centre(trackRt, new Vector2(11f, 0f), new Vector2(FaderW, 30f));
        var line = InkPath.Create(trackRt, "Line", new Vector2(FaderW, 30f));
        InkUI.Stretch(line.rectTransform);
        line.UnitPx = 1f; line.Wobble = 0.9f; line.Seed = 77;
        line.Line(Palette.A(InkUI.Ink, 0.55f), 2.2f, new Vector2(-FaderW * 0.5f, 0f), new Vector2(FaderW * 0.5f, 0f));
        faderFill = InkPath.Create(trackRt, "Fill", new Vector2(FaderW, 30f));
        InkUI.Stretch(faderFill.rectTransform);
        faderFill.UnitPx = 1f; faderFill.Wobble = 0.6f; faderFill.Seed = 78;
        knob = InkShape.Create(trackRt, "Knob", InkShape.Kind.Circle, InkUI.Paper, new Vector2(20f, 20f));
        knob.raycastTarget = false; knob.SetInk(1.8f, 2.8f); knob.ShadowOffset = new Vector2(2f, -3f); knob.Wobble = 0.6f;
        fader = faderRt.gameObject.AddComponent<HudSlider>();
        fader.vertical = false; fader.track = trackRt;
        fader.onChanged = v => { var c = cube; if (c != null) c.SetVolume(v); DrawFader(); };
        fader.onRelease = () => { if (cube != null) History.Push(); };
        InkCaption.Attach(faderRt.gameObject, "volume");
    }

    void DrawFader()
    {
        if (faderFill == null) return;
        var c = cube;
        float v = fader != null ? fader.value : 1f;
        float x0 = -FaderW * 0.5f, x1 = x0 + FaderW * Mathf.Clamp01(v);
        bool muted = c != null && (c.muted || Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)]);
        Color col = c != null ? (muted ? new Color(0.72f, 0.7f, 0.76f) : c.Color) : Palette.Accent;
        faderFill.Clear();
        if (x1 > x0 + 1f)
        {
            faderFill.Line(InkUI.Ink, 10f, new Vector2(x0, 0f), new Vector2(x1, 0f));
            faderFill.Line(Comic.Opaque(col), 5.5f, new Vector2(x0 + 0.5f, 0f), new Vector2(x1 - 0.5f, 0f));
        }
        InkUI.Centre(knob.rectTransform, new Vector2(x1, 0f), new Vector2(20f, 20f));
    }

    // ------------------------------------------------------------------ tool row
    void BuildTools()
    {
        pencilBtn = InkButton.Create(card, "Pencil", new Vector2(44f, 44f), "redraw");
        pencilBack = InkShape.Create(pencilBtn.body, "Back", InkShape.Kind.Circle, InkUI.Ink, new Vector2(40f, 40f));
        pencilBack.raycastTarget = false; pencilBack.HasShadow = false; pencilBack.SetInk(1.5f, 2f);
        pencilArt = pencilBtn.AddDrawing(new Vector2(38f, 38f));
        pencilBtn.onClick = () => { if (grid != null) grid.TogglePencil(); lookSig = int.MinValue; };
        reverseBtn = InkButton.Create(card, "Reverse", new Vector2(44f, 44f), "reverse");
        reverseArt = reverseBtn.AddDrawing(new Vector2(38f, 38f));
        InkDraw.Reverse(reverseArt, InkUI.Ink);
        reverseBtn.onClick = () => { if (grid != null) grid.Reverse(); };
        string[] words = { "loop", "back and forth", "once" };
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            var b = InkButton.Create(card, "Mode" + k, new Vector2(48f, 44f), words[k]);
            modeArt[k] = b.AddDrawing(new Vector2(44f, 34f));
            b.onClick = () => { var c = cube; if (c == null || (int)c.mode == kk) return; c.SetMode((PathMode)kk); History.Push(); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.1f + 0.1f * kk); };
            modeBtns[k] = b;
        }
        InkDraw.ModeLoop(modeArt[0], InkUI.Ink); InkDraw.ModePingPong(modeArt[1], InkUI.Ink); InkDraw.ModeOnce(modeArt[2], InkUI.Ink);
        modeWave = InkWave.Create(card, "ModeWave", InkUI.Ink, new Vector2(36f, 8f));
        modeWave.Animate = false; modeWave.Thickness = 2.2f;
    }

    // ------------------------------------------------------------------ corners: close sticker, tear-off delete
    void BuildCorners()
    {
        closeBtn = InkButton.Create(card, "Close", new Vector2(46f, 46f), "close");
        var st = InkShape.Create(closeBtn.body, "Sticker", InkShape.Kind.Sticker, InkUI.Paper, new Vector2(38f, 38f));
        st.raycastTarget = false; st.RotJitter = 4f; st.SetInk(2.2f, 3.4f); st.ShadowOffset = new Vector2(3f, -4f); st.Seed = 902;
        var x = closeBtn.AddDrawing(new Vector2(22f, 22f));
        InkDraw.Cross(x, InkUI.Ink, 3.6f);
        closeBtn.onClick = CubeInspector.Close;
        closeBtn.hoverScale = 1.12f;

        delBtn = InkButton.Create(card, "Delete", new Vector2(40f, 40f), "delete cube");
        var perf = InkPath.Create(delBtn.body, "Perforation", new Vector2(40f, 40f));   // the torn corner's perforation
        perf.UnitPx = 1f; perf.Wobble = 0.3f;
        for (int i = 0; i < 5; i++) { float t0 = -22f + i * 9f; perf.Line(Palette.A(InkUI.Ink, 0.45f), 1.6f, new Vector2(t0 + 30f, t0 + 1f), new Vector2(t0 + 34f, t0 + 5f)); }
        var ds = InkShape.Create(delBtn.body, "Sticker", InkShape.Kind.Sticker, InkUI.Red, new Vector2(30f, 30f));
        ds.raycastTarget = false; ds.RotJitter = 5f; ds.SetInk(2f, 3f); ds.ShadowOffset = new Vector2(2f, -3f); ds.Seed = 913;
        var dx = delBtn.AddDrawing(new Vector2(16f, 16f));
        InkDraw.Cross(dx, InkUI.Paper, 3.2f);
        delBtn.onClick = CubeInspector.DeleteCube;
    }

    // ------------------------------------------------------------------ the "more" drawer
    void BuildDrawer()
    {
        drawer = InkUI.Node("Drawer", card);
        drawer.anchorMin = drawer.anchorMax = new Vector2(0f, 1f);
        drawer.pivot = new Vector2(0f, 1f);
        drawer.sizeDelta = new Vector2(DrawerW, 480f);
        drawerPaper = InkShape.Create(drawer, "Paper", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(DrawerW, 480f));
        InkUI.Stretch(drawerPaper.rectTransform);
        drawerPaper.raycastTarget = true; drawerPaper.Radius = 20f; drawerPaper.SetInk(2.2f, 3.8f);
        drawerPaper.ShadowOffset = new Vector2(6f, -8f); drawerPaper.ShadowColor = Comic.PrintShadow; drawerPaper.Seed = 5301; drawerPaper.Wobble = 1.2f;
        drawerBody = InkUI.Node("Body", drawer);
        InkUI.Stretch(drawerBody);
        drawerGroup = drawerBody.gameObject.AddComponent<CanvasGroup>();

        // row 1: twin, shadow, echo · row 2: stamp, octave, centred under them (illustrated cards, 100 x 118; v7 §21: the rider is retired)
        twinCard = DrawerCard("Twin", 0, 0, "twin");
        CubeAt(twinCard.body, "A", new Vector2(-20f, 2f), 30f); CubeAt(twinCard.body, "B", new Vector2(20f, 2f), 30f);
        Ground(twinCard.body, -18f);
        twinCard.onClick = () => MakeTwin(0);
        twinCard.holdSeconds = 0.5f; twinCard.onHold = OpenTwinPopover;

        shadowCard = DrawerCard("Shadow", 1, 0, "shadow");
        for (int i = 2; i >= 0; i--) { shadowCopies[i] = CubeAt(shadowCard.body, "Copy" + i, new Vector2(-24f + 15f * (i + 1), -4f - 4f * (i + 1)), 26f); }
        CubeAt(shadowCard.body, "Lead", new Vector2(-24f, 0f), 30f);
        shadowCard.onClick = () => { var c = cube; if (c == null) return; c.SetFollow((c.follow + 1) % 4); History.Push(); Pop(shadowCard); };

        echoCard = DrawerCard("Echo", 2, 0, "echo");
        for (int i = 1; i >= 0; i--) echoCopies[i] = CubeAt(echoCard.body, "Echo" + i, new Vector2(-2f + 20f * (i + 1), 0f), 28f - 4f * (i + 1));
        CubeAt(echoCard.body, "Lead", new Vector2(-24f, 0f), 30f);
        shimmerArt = InkPath.Create(echoCard.body, "Shimmer", new Vector2(90f, 90f));
        shimmerArt.raycastTarget = false; InkDraw.Sparkles(shimmerArt, Comic.Pop);
        echoCard.onClick = () => { var c = cube; if (c == null) return; c.SetEcho((c.echo + 1) % 3); History.Push(); Pop(echoCard); };
        echoCard.holdSeconds = 0.5f;
        echoCard.onHold = () => { var c = cube; if (c == null) return; c.SetShimmer(!c.shimmer); History.Push(); AudioPool.UI(ProceduralAudio.Sparkle(), 0.25f, 1.2f); };

        stampCard = DrawerCard("Stamp", 0, 1, "stamp (hold: every island)", HalfPitch);
        var tile = InkPath.Create(stampCard.body, "Tile", new Vector2(80f, 80f));
        tile.raycastTarget = false; InkDraw.StampTile(tile, InkUI.Ink, InkUI.PaperDeep);
        CubeAt(stampCard.body, "Cube", new Vector2(0f, 12f), 30f);
        stampCard.onClick = StampClick;
        stampCard.holdSeconds = 0.6f; stampCard.onHold = StampAll;

        // the octave ladder card: ▲ upper half, ▼ lower half, the ladder between (the filled rung is the octave)
        var octRt = InkUI.Node("Octave", drawerBody);
        InkUI.AtTopLeft(octRt, CardX(1) + HalfPitch, CardY(1), new Vector2(100f, 118f));
        var octPaper = InkShape.Create(octRt, "Paper", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(100f, 118f));
        InkUI.Stretch(octPaper.rectTransform); octPaper.raycastTarget = false; octPaper.Radius = 12f; octPaper.SetInk(1.6f, 2.6f);
        octPaper.ShadowColor = InkUI.PaperDeep; octPaper.ShadowOffset = new Vector2(0f, -5f); octPaper.Seed = 7702;
        ladder = InkPath.Create(octRt, "Ladder", new Vector2(56f, 60f));
        ladder.raycastTarget = false;
        octUp = InkButton.Create(octRt, "Up", new Vector2(100f, 36f), "higher");
        InkUI.AtTopLeft((RectTransform)octUp.transform, 50f, 18f, new Vector2(100f, 36f));
        InkDraw.Triangle(octUp.AddDrawing(new Vector2(22f, 18f)), InkUI.Ink, true);
        octUp.onClick = () => { var c = cube; if (c == null || c.IsDrums || c.octave >= 1) return; c.SetOctave(c.octave + 1); History.Push(); };
        octDown = InkButton.Create(octRt, "Down", new Vector2(100f, 36f), "lower");
        InkUI.AtTopLeft((RectTransform)octDown.transform, 50f, 100f, new Vector2(100f, 36f));
        InkDraw.Triangle(octDown.AddDrawing(new Vector2(22f, 18f)), InkUI.Ink, false);
        octDown.onClick = () => { var c = cube; if (c == null || c.IsDrums || c.octave <= -1) return; c.SetOctave(c.octave - 1); History.Push(); };

        // gate: three cube drawings (a gap = short, touching = normal, an ink bridge = long)
        string[] gw = { "short notes", "normal notes", "long notes" };
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            var b = InkButton.Create(drawerBody, "Gate" + k, new Vector2(100f, 60f), gw[k]);
            InkUI.AtTopLeft((RectTransform)b.transform, CardX(k), 2f * 118f + 3f * 14f + 30f, new Vector2(100f, 60f));
            float half = k == 0 ? 19f : (k == 1 ? 12f : 21f);   // short: a gap · normal: touching · long: held across by an ink bridge
            if (k == 2)
            {
                var bridge = InkPath.Create(b.body, "Bridge", new Vector2(60f, 30f));
                bridge.raycastTarget = false; bridge.UnitPx = 1f;
                bridge.Line(InkUI.Ink, 8f, new Vector2(-half, 0f), new Vector2(half, 0f)).Line(Palette.A(InkUI.Paper, 0.9f), 3f, new Vector2(-half + 10f, 0f), new Vector2(half - 10f, 0f));
            }
            CubeAt(b.body, "A", new Vector2(-half, 0f), 24f); CubeAt(b.body, "B", new Vector2(half, 0f), 24f);
            b.onClick = () => { var c = cube; if (c == null || c.IsDrums || (int)c.gate == kk) return; c.SetGate((Gate)kk); History.Push(); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.9f + 0.15f * kk); };
            gateBtns[k] = b;
        }
        gateWave = InkWave.Create(drawerBody, "GateWave", InkUI.Ink, new Vector2(60f, 8f));
        gateWave.Animate = false;

        // legacy cubes only: the necklace and the pace row (the uniform step)
        legacyRt = InkUI.Node("Legacy", drawerBody);
        InkUI.AtTopLeft(legacyRt, 28f + (DrawerW - 28f) * 0.5f, 2f * 118f + 3f * 14f + 60f + 14f + 55f, new Vector2(DrawerW - 40f, 110f));
        dial = NecklaceDial.Create(legacyRt, "Necklace", 34f);
        var dialRt = (RectTransform)dial.transform;
        dialRt.anchorMin = dialRt.anchorMax = new Vector2(0f, 0.5f); dialRt.anchoredPosition = new Vector2(52f, 0f);
        dial.onHits = k => { var c = cube; if (c != null) c.SetHits(k); };
        dial.onRot = r => { var c = cube; if (c != null) c.SetRot(r); };
        dial.onMask = m => { var c = cube; if (c != null) c.SetMask(m); };
        dial.onBeadToggle = ToggleBead;
        dial.onRelease = () => { if (cube == null) return; History.Push(); CubeInspector.NoteRhythmEdited(); };
        InkCaption.Attach(dial.gameObject, "rhythm");
        float[] paceBeats = { 2f, 1f, 0.5f, 1f / 3f, 0.25f };
        string[] pw = { "slow", "steady", "quick", "lilting", "fast" };
        for (int k = 0; k < 5; k++)
        {
            int kk = k;
            float px = 26f * Mathf.Pow(AudioCube.SizeOf(paceBeats[k]), 1.5f);
            var b = InkButton.Create(legacyRt, "Pace" + k, new Vector2(34f, 56f), pw[k]);
            var br = (RectTransform)b.transform;
            br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f); br.anchoredPosition = new Vector2(122f + k * 38f, 0f);
            var cg = CubeGlyph.Create(b.body, "Cube", Color.white, px);
            cg.raycastTarget = false; cg.GroundShadow = true;
            ((RectTransform)cg.transform).anchoredPosition = new Vector2(0f, px * 0.5f - 12f);
            paceCubes[k] = cg; tinted.Add(cg);
            b.onClick = () => { var c = cube; if (c == null || c.step == PaceOrder[kk]) return; c.SetStep(PaceOrder[kk]); History.Push(); CubeInspector.NoteRhythmEdited(); };
            paceBtns[k] = b;
        }

        // the tab (always visible): three dots on a paper tongue sticking out of the card's right edge
        moreTab = InkButton.Create(drawer, "MoreTab", new Vector2(34f, 72f), "more");
        var tr = (RectTransform)moreTab.transform;
        tr.anchorMin = tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0f, 0.5f);
        var tongue = InkShape.Create(moreTab.body, "Tongue", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(30f, 68f));
        tongue.raycastTarget = false; tongue.Radius = 10f; tongue.SetInk(2f, 3.2f); tongue.ShadowOffset = new Vector2(4f, -5f); tongue.ShadowColor = Comic.PrintShadow; tongue.Seed = 6107;
        ((RectTransform)tongue.transform).anchoredPosition = new Vector2(2f, 0f);
        InkDraw.Dots3(moreTab.AddDrawing(new Vector2(12f, 44f)), InkUI.Ink);
        moreTab.onClick = () => SetDrawer(!drawerOpen);
        drawerGroup.alpha = 0f; drawerGroup.blocksRaycasts = false; drawerGroup.interactable = false;
    }

    static float CardX(int col) => 42f + 50f + col * (100f + 14f);   // the drawer's first 28 px stay under the card
    static float CardY(int row) => 14f + 59f + row * (118f + 14f);

    /// <summary>Half a drawer card's pitch: a row of two cards sits centred under a row of three.</summary>
    const float HalfPitch = (100f + 14f) * 0.5f;

    InkButton DrawerCard(string name, int col, int row, string caption, float xShift = 0f)
    {
        var b = InkButton.Create(drawerBody, name, new Vector2(100f, 118f), caption);
        InkUI.AtTopLeft((RectTransform)b.transform, CardX(col) + xShift, CardY(row), new Vector2(100f, 118f));
        var bg = InkShape.Create(b.body, "Paper", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(100f, 118f));
        InkUI.Stretch(bg.rectTransform);
        bg.raycastTarget = false; bg.Radius = 12f; bg.SetInk(1.6f, 2.6f);
        bg.ShadowColor = InkUI.PaperDeep; bg.ShadowOffset = new Vector2(0f, -5f);   // Melatonin's pale bottom lip (the card's thickness)
        bg.Seed = name.GetHashCode();
        b.hoverScale = 1.05f;
        return b;
    }

    CubeGlyph CubeAt(Transform parent, string name, Vector2 pos, float px)
    {
        var g = CubeGlyph.Create(parent, name, Color.white, px);
        g.raycastTarget = false;
        ((RectTransform)g.transform).anchoredPosition = pos;
        tinted.Add(g);
        return g;
    }

    static void Ground(Transform parent, float y)
    {
        var p = InkPath.Create(parent, "Ground", new Vector2(80f, 10f));
        p.raycastTarget = false; p.UnitPx = 1f; p.Wobble = 0.5f;
        ((RectTransform)p.transform).anchoredPosition = new Vector2(0f, y);
        p.Line(Palette.A(InkUI.Ink, 0.6f), 2f, new Vector2(-36f, 0f), new Vector2(36f, 0f));
    }

    static void Pop(InkButton b) { if (b != null && b.body != null) b.body.localScale = new Vector3(1.12f, 1.12f, 1f); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.2f); }

    public void SetDrawer(bool open)
    {
        if (drawerOpen == open) return;
        drawerOpen = open;
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.2f, open ? 1.35f : 1.15f);
        RegisterHints();
    }

    // ------------------------------------------------------------------ popovers
    void BuildInstrumentPopover()
    {
        instPop = InkUI.Node("InstrumentPopover", popLayer);
        instPop.anchorMin = instPop.anchorMax = new Vector2(0.5f, 0.5f); instPop.pivot = new Vector2(0f, 1f);
        instPop.sizeDelta = new Vector2(PopW, InstPopH);
        instPopPaper = InkShape.Create(instPop, "Paper", InkShape.Kind.Bubble, InkUI.Paper, new Vector2(PopW, InstPopH));
        InkUI.Stretch(instPopPaper.rectTransform);
        instPopPaper.raycastTarget = true; instPopPaper.Radius = 18f; instPopPaper.SetInk(2.2f, 3.6f); instPopPaper.ShadowColor = Comic.PrintShadow;
        instPopPaper.ShadowOffset = new Vector2(5f, -6f); instPopPaper.TailWidth = 26f; instPopPaper.Seed = 3203;
        for (int k = 0; k < Instruments.Count; k++)
        {
            int kk = k;
            var b = InkButton.Create(instPop, "Inst" + k, new Vector2(44f, 46f), InkUI.InstrumentWords[k]);
            InkUI.AtTopLeft((RectTransform)b.transform, 38f + (k % 5) * 48f, 32f + (k / 5) * 52f, new Vector2(44f, 46f));
            var g = CubeGlyph.Create(b.body, "Cube", Instruments.Colors[k], 32f);
            g.raycastTarget = false; g.GroundShadow = true;
            b.hoverScale = 1.14f;
            b.onClick = () => PickInstrument(kk);
            b.onHover = on => { if (on) { InkUI.Hop(g, 0.25f); if (cube != null && !cube.IsOnMoon) Instruments.Audition(kk); } };
            instCubes[k] = g; instBtns[k] = b;
        }
        // v6: the voices of the cube's group under a wavy line (small cubes with their pips; hover = audition, click = the cube's sound)
        voiceWave = InkWave.Create(instPop, "VoiceWave", Palette.A(InkUI.Ink, 0.55f), new Vector2(PopW - 60f, 8f));
        voiceWave.Animate = false; voiceWave.Thickness = 2f;
        InkUI.AtTopLeft(voiceWave.rectTransform, PopW * 0.5f, InstPopH - 4f, new Vector2(PopW - 60f, 8f));
        for (int v = 0; v < MaxVoiceChips; v++)
        {
            int vv = v;
            var b = InkButton.Create(instPop, "Voice" + v, new Vector2(38f, 42f), "voice");
            var art = InkUI.Node("Art", b.body);
            InkUI.Stretch(art);
            voiceArt[v] = art.gameObject.AddComponent<InkPainter>(); voiceArt[v].raycastTarget = false;
            voiceArt[v].onPaint = p => PaintVoiceChip(p, vv);
            b.hoverScale = 1.14f;
            b.onClick = () => PickVoice(vv);
            b.onHover = on => { if (on) AuditionVoice(vv); };
            voiceBtns[v] = b;
            b.gameObject.SetActive(false);
        }
        voiceWave.gameObject.SetActive(false);
        instPop.gameObject.SetActive(false);
    }

    void BuildTwinPopover()
    {
        twinPop = InkUI.Node("TwinPopover", popLayer);
        twinPop.anchorMin = twinPop.anchorMax = new Vector2(0.5f, 0.5f); twinPop.pivot = new Vector2(0.5f, 0f);
        twinPop.sizeDelta = new Vector2(236f, 76f);
        twinPopPaper = InkShape.Create(twinPop, "Paper", InkShape.Kind.Bubble, InkUI.Paper, new Vector2(236f, 76f));
        InkUI.Stretch(twinPopPaper.rectTransform);
        twinPopPaper.raycastTarget = true; twinPopPaper.Radius = 16f; twinPopPaper.SetInk(2.2f, 3.6f); twinPopPaper.ShadowColor = Comic.PrintShadow;
        twinPopPaper.ShadowOffset = new Vector2(5f, -6f); twinPopPaper.TailWidth = 24f; twinPopPaper.Seed = 3307;
        string[] words = { "slower + higher", "twice as fast", "backwards", "one step later" };
        for (int k = 0; k < 4; k++)
        {
            int kk = k;
            var b = InkButton.Create(twinPop, "Variant" + k, new Vector2(52f, 60f), words[k]);
            InkUI.AtTopLeft((RectTransform)b.transform, 30f + k * 58f, 38f, new Vector2(52f, 60f));
            switch (k)
            {
                case 0: CubeAt(b.body, "A", new Vector2(-9f, -8f), 18f); CubeAt(b.body, "B", new Vector2(9f, 6f), 26f); break;
                case 1: CubeAt(b.body, "A", new Vector2(-9f, -2f), 15f); CubeAt(b.body, "B", new Vector2(9f, -2f), 15f); break;
                case 2:
                {
                    CubeAt(b.body, "A", new Vector2(4f, -4f), 22f);
                    var arr = InkPath.Create(b.body, "Arrow", new Vector2(44f, 44f)); arr.raycastTarget = false;
                    InkDraw.BackArrow(arr, InkUI.Ink);
                    break;
                }
                default: CubeAt(b.body, "A", new Vector2(-10f, 4f), 18f); CubeAt(b.body, "B", new Vector2(10f, -6f), 18f); break;
            }
            b.onClick = () => { MakeTwin(kk); CloseTwinPopover(); };
        }
        twinPop.gameObject.SetActive(false);
    }

    void ToggleInstrumentPopover()
    {
        if (instPop == null || cube == null) return;
        if (instPop.gameObject.activeSelf) { CloseInstrumentPopover(); return; }
        CloseTwinPopover();
        instPop.gameObject.SetActive(true);
        popOpenedFrame = Time.frameCount;
        RefreshInstrumentPopover();
        PlacePopover(instPop, instPopPaper, (RectTransform)chip.transform, new Vector2(-18f, -40f));
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.25f);
    }

    void CloseInstrumentPopover() { if (instPop != null && instPop.gameObject.activeSelf) instPop.gameObject.SetActive(false); }

    void OpenTwinPopover()
    {
        if (twinPop == null || cube == null) return;
        CloseInstrumentPopover();
        twinPop.gameObject.SetActive(true);
        popOpenedFrame = Time.frameCount;
        foreach (var g in twinPop.GetComponentsInChildren<CubeGlyph>(true)) g.color = Comic.Opaque(cube.Color);
        PlacePopover(twinPop, twinPopPaper, (RectTransform)twinCard.transform, new Vector2(0f, 72f));
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.3f);
    }

    void CloseTwinPopover() { if (twinPop != null && twinPop.gameObject.activeSelf) twinPop.gameObject.SetActive(false); }

    /// <summary>Puts a popover near <paramref name="target"/> (offset in canvas units from its centre) with its tail on the target.</summary>
    void PlacePopover(RectTransform pop, InkShape bubble, RectTransform target, Vector2 offset)
    {
        var root = InkUI.OverlayRoot;
        target.GetWorldCorners(corners);
        Vector3 centreW = (corners[0] + corners[2]) * 0.5f;
        Vector2 centre = root.InverseTransformPoint(centreW);
        Vector2 pos = centre + offset;
        Rect rr = root.rect; Vector2 sz = pop.sizeDelta;
        float px = pop.pivot.x, py = pop.pivot.y;
        pos.x = Mathf.Clamp(pos.x, rr.xMin + 10f + sz.x * px, rr.xMax - 10f - sz.x * (1f - px));
        pos.y = Mathf.Clamp(pos.y, rr.yMin + 10f + sz.y * py, rr.yMax - 10f - sz.y * (1f - py));
        pop.anchoredPosition = pos;
        pop.localScale = Vector3.one;
        bubble.TailTip = bubble.rectTransform.InverseTransformPoint(centreW);
    }

    void RefreshInstrumentPopover()
    {
        var c = cube; if (c == null) return;
        bool moon = c.IsOnMoon;
        for (int k = 0; k < Instruments.Count; k++)
        {
            var g = instCubes[k];
            g.color = Instruments.Colors[k];
            g.Ring = c.instrument == k;
            g.Hollow = Instruments.Muted[k] || (c.instrument == k && c.muted);
            bool ok = !moon || Instruments.IsDrums(k);
            instBtns[k].Interactable = ok;
        }
        // v6: the group's voices (6 a row)
        int grp = Mathf.Clamp(c.instrument, 0, Instruments.Count - 1);
        int n = HudInstruments.VoicesOf(grp);
        voiceN = n >= 2 ? Mathf.Min(n, MaxVoiceChips) : 0;
        int perRow = 6, rows = voiceN > 0 ? (voiceN + perRow - 1) / perRow : 0;
        for (int v = 0; v < MaxVoiceChips; v++)
        {
            bool on = v < voiceN;
            if (voiceBtns[v].gameObject.activeSelf != on) voiceBtns[v].gameObject.SetActive(on);
            if (!on) continue;
            int row = v / perRow, inRow = Mathf.Min(perRow, voiceN - row * perRow), j = v % perRow;
            float x = PopW * 0.5f + (j - (inRow - 1) * 0.5f) * VoicePitch;
            InkUI.AtTopLeft((RectTransform)voiceBtns[v].transform, x, VoiceTop + row * VoiceRowH, new Vector2(38f, 42f));
            InkCaption.Attach(voiceBtns[v].gameObject, Instruments.VoiceName(grp, v));
            voiceArt[v].Repaint();
        }
        voiceWave.gameObject.SetActive(voiceN > 0);
        float h = voiceN > 0 ? VoiceTop + (rows - 1) * VoiceRowH + 34f : InstPopH;
        if (Mathf.Abs(instPop.sizeDelta.y - h) > 0.5f) instPop.sizeDelta = new Vector2(PopW, h);
    }

    /// <summary>The voice the card shows for <paramref name="c"/> (its voice, inside its group).</summary>
    static int ShownVoice(AudioCube c) { if (c == null) return 0; int n = HudInstruments.VoicesOf(c.instrument); return c.voice >= 0 && c.voice < n ? c.voice : 0; }   // out of range plays voice 0 (Instruments.SlotOf)

    void PaintVoiceChip(InkPainter p, int v)
    {
        var c = cube;
        if (c == null || v >= voiceN) return;
        Vector2 ctr = p.Area.center + new Vector2(0f, 2f);
        Color col = Comic.Opaque(Instruments.Colors[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)]);
        if (ShownVoice(c) == v)
        {
            // the cube's voice: the ink hexagon (selection is drawn, never a new hue)
            ringPts.Clear();
            for (int k = 0; k < 6; k++) { float a = (30f + 60f * k) * Mathf.Deg2Rad; ringPts.Add(ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 18f); }
            p.Stroke(ringPts, 6, 2.2f, InkUI.Ink, true);
        }
        p.Disc(ctr + new Vector2(0f, -14f), 11f, 3.2f, Palette.A(InkUI.Ink, 0.18f));
        p.Cube(ctr, 26f, col, InkUI.Ink, 1.6f);
        VoiceMark.Paint(p, ctr, 26f, v, InkUI.Ink);
    }
    static readonly List<Vector2> ringPts = new List<Vector2>(8);

    float lastVoiceAudT = -9f; int lastVoiceAud = -1;
    void AuditionVoice(int v)
    {
        var c = cube;
        if (c == null || v >= voiceN) return;
        if (v == lastVoiceAud && Time.unscaledTime - lastVoiceAudT < 0.3f) return;
        lastVoiceAud = v; lastVoiceAudT = Time.unscaledTime;
        Instruments.Audition(c.instrument, v);
    }

    /// <summary>v6: the cube's sound becomes voice <paramref name="v"/> of its group (re-sounded at once; one History entry). The popover stays up.</summary>
    void PickVoice(int v)
    {
        var c = cube;
        if (c == null || v < 0 || v >= voiceN) return;
        if (ShownVoice(c) == v) { AuditionVoice(v); return; }
        SetCubeVoice(c, v);
        History.Push();
        VoicePicks++;
        InkUI.AuditionNote(c, 0, 1f);
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f + 0.04f * v);
        lookSig = int.MinValue;
        for (int k = 0; k < voiceN; k++) voiceArt[k].Repaint();
    }

    /// <summary>Sets a cube's voice and re-sounds it (AudioCube.SetVoice: the queued notes are re-decided; no History — the caller pushes).</summary>
    static void SetCubeVoice(AudioCube c, int v) { c.SetVoice(v); }

    void PickInstrument(int k)
    {
        var c = cube; var pm = PathManager.I;
        if (c == null || pm == null) return;
        if (k == c.instrument) { ToggleMute(); CloseInstrumentPopover(); return; }   // the current colour: mute on / off
        if (c.IsOnMoon) { CloseInstrumentPopover(); return; }
        pm.SetSelectedInstrument(k);        // recolour (one History entry; the proxy bursts); v6: the cube takes the group's brush voice (AudioCube.SetInstrument)
        if (HudInstruments.VoicesOf(k) >= 2 && instPop.gameObject.activeSelf) { RefreshInstrumentPopover(); lookSig = int.MinValue; }   // v6: now pick its voice
        else CloseInstrumentPopover();
    }

    void ToggleMute()
    {
        var c = cube; if (c == null) return;
        c.SetMuted(!c.muted);
        History.Push();
        AudioPool.UI(ProceduralAudio.Tick(), 0.35f, c.muted ? 0.75f : 1.25f);
        lookSig = int.MinValue;
    }

    // ------------------------------------------------------------------ v6 copy / paste → next grid
    /// <summary>Copies made from the card, pastes asked with its paste → next, voices picked (tests).</summary>
    public static int CopyCount { get; private set; }
    public static int PasteNextCount { get; private set; }
    public static int VoicePicks { get; private set; }
    /// <summary>The cube the last paste → next made (null: refused / not landed yet).</summary>
    public static AudioCube LastPasteNext { get; private set; }
    /// <summary>A paste → next waits for the inspector to close (the flight must be seen, not dimmed).</summary>
    public static bool PastePending => pastePending;

    void CopyClick()
    {
        var c = cube;
        if (c == null) return;
        if (!Clipboard.Copy(c)) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return; }
        CopyCount++;
        HudClipboard.NoteCopySource(ScreenOf(copyBtn.transform));   // the clipboard chip flies in from the button
        Pop(copyBtn);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.35f);
    }

    void PasteNextClick()
    {
        var c = cube;
        if (c == null) return;
        if (pasteFan != null) pasteFan.Close();
        if (!CanPasteNext(c) || !Clipboard.Copy(c)) { AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return; }
        HudClipboard.NoteCopySource(ScreenOf(pasteBtn.transform));
        Pop(pasteBtn);
        PasteNextAfterClose();
    }

    /// <summary>Pastes the clipboard onto the next grid (Clipboard.PasteNext); while the inspector is open it closes first and the paste
    /// follows once it is closed, so the magic flight is seen in the undimmed world (UIManager's Cmd+V uses it too).</summary>
    public static void PasteNextAfterClose() { PasteNextAfterClose(Clipboard.PasteMode.Plain); }

    /// <summary>v7: <see cref="PasteNextAfterClose()"/> in a paste mode (echo ↑ / ↓, step ↑ / ↓, answer).</summary>
    public static void PasteNextAfterClose(Clipboard.PasteMode mode)
    {
        if (!Clipboard.HasPattern) return;
        if (CubeInspector.IsOpen || CubeInspector.State == CubeInspector.Phase.Closing)
        {
            pastePending = true; pasteAskedT = Time.unscaledTime; pendingMode = mode;
            CubeInspector.Close();
            return;
        }
        DoPasteNext(mode);
    }

    static AudioCube DoPasteNext(Clipboard.PasteMode mode)
    {
        PasteNextCount++;
        LastPasteMode = mode;
        // the song's last grid has no next one: a new grid is added after it and the pattern lands there (no wrap to the start)
        var home = Clipboard.SourceIsland;
        var made = IsLastColumn(home) ? PasteOnNewGrid(home, mode) : Clipboard.PasteNext(mode);
        LastPasteNext = made;
        if (made == null) { UIManager.Toast("warning", Palette.Danger); return null; }
        // the landing is shown: the view glides over when the new cube's island is off screen (the distance stays)
        var kb = made.Island; var cam = Camera.main;
        if (kb != null && cam != null && OrbitCamera.I != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(kb.Center);
            if (vp.z <= 0f || vp.x < 0.08f || vp.x > 0.92f || vp.y < 0.08f || vp.y > 0.92f) OrbitCamera.I.GlideTo(kb.Center, 0.6f);
        }
        return made;
    }

    /// <summary>Pastes made onto a grid added after the song's last column (tests).</summary>
    public static int NewGridPastes { get; private set; }

    /// <summary>True when <paramref name="kb"/> (not a Moon) stands in the song's last column: "paste on next grid" then adds a grid.</summary>
    public static bool IsLastColumn(KeyBlock kb)
    {
        var sm = SongManager.I;
        return sm != null && sm.HasSong && kb != null && !kb.IsMoon && kb.column >= 0 && kb.column >= sm.ColumnCount - 1;
    }

    /// <summary>"Paste on next grid" can go somewhere: a next grid, or (from the last column) a new one.</summary>
    public static bool CanPasteNext(AudioCube c) => c != null && (IsLastColumn(c.Island) || NextGridOf(c) != null);

    /// <summary>Adds a grid after <paramref name="home"/>'s column (the next chord, SongManager.AddMeasure) and pastes the clipboard on it in
    /// <paramref name="mode"/> — one undo step for both. A refused paste takes the new grid back. Null when nothing was pasted.</summary>
    static AudioCube PasteOnNewGrid(KeyBlock home, Clipboard.PasteMode mode)
    {
        var sm = SongManager.I;
        int hi = sm != null ? sm.Islands.IndexOf(home) : -1;
        if (hi < 0) return null;
        int at = sm.AddMeasure(hi);
        if (at < 0 || at >= sm.Islands.Count) return null;
        int h0 = History.UndoCount;
        var made = Clipboard.PasteOn(sm.Islands[at], mode);
        if (made == null) { History.Undo(); return null; }
        if (History.UndoCount > h0) History.FoldLast();
        NewGridPastes++;
        return made;
    }

    /// <summary>"The next grid" of <paramref name="c"/>'s pattern, as Clipboard.NextGrid finds it once the cube is copied (the next column,
    /// wrapping at the song's end, a column without one skipped: the island nearest in z of the same family — keyboards → keyboards, else chord
    /// islands; a Moon: the other Moon) — without touching the clipboard. Null when there is none.</summary>
    public static KeyBlock NextGridOf(AudioCube c)
    {
        var sm = SongManager.I; var home = c != null ? c.Island : null;
        if (sm == null || !sm.HasSong || home == null) return null;
        if (home.IsMoon)
        {
            foreach (var m in sm.Moons) if (m != null && m != home) return m;
            return null;
        }
        int cols = sm.ColumnCount;
        if (cols <= 0 || home.column < 0) return null;
        for (int k = 1; k <= cols; k++)
        {
            int col = (home.column + k) % cols;
            for (int pass = 0; pass < 2; pass++)
            {
                KeyBlock best = null; float bestD = float.MaxValue;
                foreach (var kb in sm.Islands)
                {
                    if (kb == null || kb == home || kb.IsMoon || kb.column != col || kb.cols <= 0 || kb.rows <= 0) continue;
                    bool same = kb.IsKeyboard == home.IsKeyboard;
                    if (pass == 0 ? !same : (kb.IsKeyboard || !home.IsKeyboard)) continue;
                    float d = Mathf.Abs(kb.Center.z - home.Center.z);
                    if (d < bestD - 1e-4f) { bestD = d; best = kb; }
                }
                if (best != null) return best;
            }
        }
        return null;
    }

    static Vector2 ScreenOf(Transform t) => RectTransformUtility.WorldToScreenPoint(null, t.position);

    // ------------------------------------------------------------------ drawer actions (twin, stamp, necklace beads)
    void MakeTwin(int variant)
    {
        var c = cube; var pm = PathManager.I;
        if (c == null || pm == null) return;
        var t = pm.RestoreCube(c.TwinState(variant));
        if (t == null) { UIManager.Toast("warning", Palette.Danger); return; }
        SequenceMaster.RecalculateTimeline();
        Fx.Burst(t.transform.position, t.Color, 12, 2.4f);
        AudioPool.UI(ProceduralAudio.Thud(), 0.5f);
        History.Push();
        Pop(twinCard);
    }

    static bool StampOk(KeyBlock kb, KeyBlock src) => kb != null && kb.kind == 0 && kb != src;

    /// <summary>The island a stamp goes to: the lit island while playing, else the next island after the cube's own (the world is locked while
    /// inspecting, so there is no hovered island).</summary>
    KeyBlock StampTarget()
    {
        var c = cube; var sm = SongManager.I;
        if (c == null || sm == null || !sm.HasSong) return null;
        var src = c.Island;
        var islands = sm.Islands;
        if (GlobalClock.IsPlaying) { int lit = Mathf.Clamp(sm.LitIsland, 0, islands.Count - 1); if (StampOk(islands[lit], src)) return islands[lit]; }
        int i = Mathf.Max(0, islands.IndexOf(src));
        for (int k = 1; k < islands.Count; k++) { var kb = islands[(i + k) % islands.Count]; if (StampOk(kb, src)) return kb; }
        return null;
    }

    void StampClick()
    {
        var pm = PathManager.I; if (pm == null || cube == null) return;
        var t = StampTarget();
        var n = t != null ? pm.StampTo(t) : null;
        if (n == null) { UIManager.Toast("warning", Palette.Danger); return; }
        History.Push();
        Pop(stampCard);
    }

    void StampAll()
    {
        var pm = PathManager.I; if (pm == null || cube == null) return;
        pm.StampToAll();
        History.Push();
        Pop(stampCard);
    }

    /// <summary>Necklace bead click: the visible pattern with step j flipped becomes an explicit mask (hits = -2).</summary>
    void ToggleBead(int j)
    {
        var c = cube; if (c == null || dial == null) return;
        var p = dial.Pattern();
        int m = 0;
        for (int i = 0; i < p.Length && i < 31; i++) if (p[i]) m |= 1 << i;
        if (j >= 0 && j < 31) m ^= 1 << j;
        c.SetMask(m);
        if (c.hits != -2) c.SetHits(-2);
        History.Push();
        CubeInspector.NoteRhythmEdited();
    }

    // ================================================================== binding + layout
    void Bind(AudioCube c)
    {
        if (c == cube) return;
        if (cube != null && c == null) { strip.Bind(null); }
        cube = c;
        lookSig = int.MinValue;
        if (c == null) return;
        grid.ResetTools();
        grid.Bind(c);
        strip.Bind(c);
        if (DurationPicker.Mode == DurationPicker.Where.Inspector) DurationPicker.Hide();
        fader.Set(c.volume, false);
        RegisterHints();
    }

    /// <summary>Positions every part for an island of <paramref name="cols"/> x <paramref name="rows"/> (the grid's size decides the card's height).</summary>
    void Layout(int cols, int rows)
    {
        gridCols = cols; gridRows = rows;
        var grt = (RectTransform)grid.transform;
        float gw = grt.sizeDelta.x, gh = grt.sizeDelta.y;
        float y = HeaderTop;
        InkUI.AtTopLeft((RectTransform)chip.transform, Pad + 26f, y + HeaderH * 0.5f, new Vector2(58f, 58f));
        InkUI.AtTopLeft(faderRt, Pad + 58f + 16f + (FaderW + 44f) * 0.5f, y + HeaderH * 0.5f, new Vector2(FaderW + 44f, 40f));
        InkUI.AtTopLeft((RectTransform)copyBtn.transform, W - 156f, y + HeaderH * 0.5f + 2f, new Vector2(48f, 46f));
        InkUI.AtTopLeft((RectTransform)pasteBtn.transform, W - 96f, y + HeaderH * 0.5f + 2f, new Vector2(60f, 48f));
        y += HeaderH + GridGap;
        InkUI.AtTopLeft(grt, W * 0.5f, y + gh * 0.5f, new Vector2(gw, gh));
        InkUI.AtTopLeft(harmGhost.rectTransform, W * 0.5f, y + gh * 0.5f, new Vector2(gw, gh));   // v7: over the grid, in its local space
        y += gh + 6f;
        float cy = y + ToolsH * 0.5f;
        float x0 = W * 0.5f - 138f;
        InkUI.AtTopLeft((RectTransform)pencilBtn.transform, x0 + 22f, cy, new Vector2(44f, 44f));
        InkUI.AtTopLeft((RectTransform)reverseBtn.transform, x0 + 72f, cy, new Vector2(44f, 44f));
        for (int k = 0; k < 3; k++) InkUI.AtTopLeft((RectTransform)modeBtns[k].transform, x0 + 136f + k * 52f, cy, new Vector2(48f, 44f));
        y += ToolsH + 6f;
        InkUI.AtTopLeft((RectTransform)strip.transform, W * 0.5f, y + RhythmStrip.Height * 0.5f, new Vector2(RhythmStrip.Width, RhythmStrip.Height));
        y += RhythmStrip.Height + 4f;
        InkUI.AtTopLeft(lengthSlot, W * 0.5f, y + RowH * 0.5f, new Vector2(W - 2f * Pad, RowH));
        y += RowH + BottomPad;
        H = y;
        card.sizeDelta = new Vector2(W, H);
        InkUI.AtTopLeft((RectTransform)closeBtn.transform, W - 10f, 8f, new Vector2(46f, 46f));
        InkUI.AtTopLeft((RectTransform)delBtn.transform, 12f, H - 12f, new Vector2(40f, 40f));
        // the drawer: under the card's right part, its tab on the card's right edge near the bottom
        float dh = DrawerHeight();
        drawer.sizeDelta = new Vector2(DrawerW, dh);
        ((RectTransform)moreTab.transform).anchoredPosition = new Vector2(-4f, -Mathf.Min(dh - 44f, H - 150f));
        PlaceDrawer();
    }

    float DrawerHeight() => IsDurations || cube == null ? 2f * 118f + 3f * 14f + 60f + 26f + 12f : 2f * 118f + 3f * 14f + 60f + 26f + 110f + 20f;

    void PlaceDrawer()
    {
        float e = Look.Stepped(drawerT);
        e = e * e * (3f - 2f * e);
        float closedX = W - DrawerW - 6f, openX = W - 28f;
        drawer.anchoredPosition = new Vector2(Mathf.Lerp(closedX, openX, e), -34f);
        drawerGroup.alpha = e > 0.02f ? 1f : 0f;
        bool on = drawerOpen && e > 0.98f;
        drawerGroup.blocksRaycasts = on; drawerGroup.interactable = on;
    }

    // ================================================================== per frame
    void LateUpdate()
    {
        if (!built) return;
        if (pastePending)
        {
            if (CubeInspector.State == CubeInspector.Phase.Closed) { pastePending = false; DoPasteNext(pendingMode); pendingMode = Clipboard.PasteMode.Plain; }
            else if (Time.unscaledTime - pasteAskedT > 3f) pastePending = false;
        }
        InkUI.TickHops();
        float dt = Time.unscaledDeltaTime;
        var c = CubeInspector.Current;
        want = CubeInspector.CardWanted && c != null;
        if (want && c != cube) Bind(c);
        appear = want && (CubeInspector.State == CubeInspector.Phase.Open || CubeInspector.PhaseTime >= AppearAt);
        // pop on twos (one drawing per 1/12 s): 0.62 → 0.9 → 1.06 → 1 in, 1 → 0.9 → 0.72 → 0.55 → gone out
        popT = Mathf.MoveTowards(popT, appear ? 1f : 0f, dt / (appear ? 0.25f : 0.25f));
        int st = popT >= 1f ? 3 : Mathf.FloorToInt(popT * 3f);
        shown = st / 3f;
        if (popT <= 0f)
        {
            if (card.gameObject.activeSelf) Hide();
            if (!want && cube != null) Bind(null);
            return;
        }
        if (!card.gameObject.activeSelf) Show();
        if (cube == null) return;
        HideOldPanel();

        var isl = cube.Island;
        int cols = isl != null ? Mathf.Max(1, isl.cols) : 1, rows = isl != null ? Mathf.Max(1, isl.rows) : 1;
        if (cols != gridCols || rows != gridRows) Layout(cols, rows);

        // look + state (only when something changed)
        int sig = LookSig(cube);
        if (sig != lookSig) { lookSig = sig; RefreshLook(); }
        UpdateNudgeButtons();   // v9 (N)
        if (fader != null && Mathf.Abs(fader.value - cube.volume) > 1e-3f) { fader.Set(cube.volume, false); DrawFader(); }
        UpdateSpotlight();

        // drawer slide on twos
        drawerT = Mathf.MoveTowards(drawerT, drawerOpen ? 1f : 0f, dt / 0.22f);
        PlaceDrawer();

        Place(appear ? PopIn[st] : PopOut[st]);
        cardGroup.alpha = 1f;
        cardGroup.interactable = shown >= 1f && want;
        cardGroup.blocksRaycasts = want;

        // the selected note shows in the grid and in the world
        grid.HighlightNode = strip.SelectedNote;
        UpdateWorldMark(dt);
        ClosePopoversOnOutsideClick();
        // v7: the paste fan opens after a short rest on paste → next grid; the harmony preview after a short rest on a harmony half
        if (pasteHoverT >= 0f && pasteBtn.Hovered && Time.unscaledTime - pasteHoverT >= 0.25f && !pasteFan.IsOpen && want && shown >= 1f) { pasteHoverT = -1f; OpenPasteFan(true); }
        UpdateHarmonyPreview();
    }

    void Show()
    {
        card.gameObject.SetActive(true);
        popLayer.gameObject.SetActive(true);
        lastTip = new Vector2(float.NaN, float.NaN);
        RegisterHints();
    }

    void Hide()
    {
        CloseInstrumentPopover(); CloseTwinPopover();
        if (pasteFan != null) pasteFan.Close();
        harmHover = 0; EndHarmonyPreview();
        drawerOpen = false; drawerT = 0f;
        if (DurationPicker.Mode == DurationPicker.Where.Inspector) DurationPicker.Hide();
        if (strip != null) strip.Deselect();
        if (delBtn != null && !delBtn.gameObject.activeSelf) delBtn.gameObject.SetActive(true);
        if (spotCube != null) { Performance.HoldSpotlight(spotCube, false); spotCube = null; }
        if (worldMark != null) worldMark.gameObject.SetActive(false);
        card.gameObject.SetActive(false);
    }

    /// <summary>Interim until U1 removes UIManager's old cube panel (requests_U2.md "card live"): it never shows together with this card.</summary>
    void HideOldPanel()
    {
        var ui = UIManager.I;
        var old = ui != null ? ui.Card : null;
        if (old == null || old == card || old.IsChildOf(card)) return;   // U1 may forward UIManager.Card to this card
        if (old.gameObject.activeSelf && old.localScale != Vector3.zero) old.localScale = Vector3.zero;
    }

    /// <summary>The card beside the proxy (its left edge a tail's length right of the cube, vertically on the cube, kept on screen and scaled
    /// down when the view is short) and the tail re-aimed at the cube when it moved more than 4 px.</summary>
    void Place(float pop)
    {
        var cam = Camera.main;
        var root = InkUI.OverlayRoot;
        Rect rr = root.rect;
        float upp = InkUI.UnitsPerPixel;
        Vector2 proxy = new Vector2(rr.xMin + rr.width * CubeInspector.TargetViewport.x, rr.yMin + rr.height * CubeInspector.TargetViewport.y);
        float rPx = CubeInspector.FocusCubeRadiusPx > 1f ? CubeInspector.FocusCubeRadiusPx : CubeInspector.ScreenShare * Screen.height * 0.42f;
        if (cam != null && CubeInspector.Proxy != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(CubeInspector.AnchorWorld);
            Vector2 lp;
            if (sp.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out lp)) proxy = lp;
        }
        float rLocal = rPx * upp;
        float s = Mathf.Clamp((rr.height - TopMargin - BottomMargin) / Mathf.Max(1f, H), 0.5f, 1f);
        float wTotal = (W + (drawerOpen ? DrawerW - 28f : 20f)) * s;
        float left = proxy.x + rLocal + TailGap * s;
        left = Mathf.Min(left, rr.xMax - 18f - wTotal);
        left = Mathf.Max(left, rr.xMin + 18f);
        float half = H * s * 0.5f;
        float cy = Mathf.Clamp(proxy.y, rr.yMin + BottomMargin + half, rr.yMax - TopMargin - half);
        card.anchoredPosition = new Vector2(left, cy);
        float k = s * pop;
        card.localScale = new Vector3(k, k, 1f);
        // the tail: from the card toward the cube, its tip just inside the cube's silhouette
        Vector2 toCard = new Vector2(left, cy) - proxy;
        Vector2 tipLocal = toCard.sqrMagnitude > 1f ? proxy + toCard.normalized * rLocal * 0.78f : proxy;
        Vector3 tipW = root.TransformPoint(tipLocal);
        Vector2 tip = paper.rectTransform.InverseTransformPoint(tipW);
        if (float.IsNaN(lastTip.x) || (tip - lastTip).sqrMagnitude > 16f) { lastTip = tip; paper.TailTip = tip; }
    }

    int LookSig(AudioCube c)
    {
        unchecked
        {
            int h = c.GetInstanceID();
            h = h * 31 + c.instrument;
            h = h * 31 + (c.muted ? 1 : 0) + (Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)] ? 2 : 0) + (c.shimmer ? 4 : 0)
                + (c.HasDurations ? 16 : 0) + (c.IsDrums ? 32 : 0) + (c.IsOnMoon ? 64 : 0) + (grid.PencilArmed ? 128 : 0) + (drawerOpen ? 256 : 0);
            h = h * 31 + (int)c.mode; h = h * 31 + (int)c.gate; h = h * 31 + c.octave; h = h * 31 + c.follow; h = h * 31 + c.echo;
            h = h * 31 + (int)c.step; h = h * 31 + c.hits; h = h * 31 + c.rot; h = h * 31 + c.mask;
            h = h * 31 + c.nodes.Count;
            h = h * 31 + (SongManager.I != null ? SongManager.I.Islands.Count : 0);
            h = h * 31 + c.climb;
            h = h * 31 + c.voice; h = h * 31 + (c.Island != null ? c.Island.column : -1) + (SongManager.I != null ? SongManager.I.ColumnCount * 1009 : 0);   // v6
            h = h * 31 + GlobalClock.BeatsPerBar;
            h = h * 31 + c.layer;   // v7: the octave copies' limits
            h = h * 31 + SequenceMaster.Cubes.Count;   // v7: a copy made / undone (the ghosts fill when their copy exists)
            return h;
        }
    }

    void RefreshLook()
    {
        var c = cube; if (c == null) return;
        Color col = Comic.Opaque(c.Color);
        bool muted = c.muted || Instruments.Muted[Mathf.Clamp(c.instrument, 0, Instruments.Count - 1)];
        bool drums = c.IsDrums;
        chipCube.color = col; chipCube.Hollow = muted;
        DrawFader();
        // v6: the voice pips on the chip and its name as the caption; copy / paste → next in the cube's colour
        chipMark.Repaint();
        int grp = Mathf.Clamp(c.instrument, 0, Instruments.Count - 1);
        InkCaption.Attach(chip.gameObject, HudInstruments.VoicesOf(grp) >= 2 ? Instruments.VoiceName(grp, ShownVoice(c)) : "sound");
        InkDraw.Copy(copyArt, InkUI.Ink, InkUI.Paper, InkUI.PaperDeep, col);
        InkDraw.PasteNext(pasteArt, InkUI.Ink, InkUI.Paper, InkUI.PaperDeep, col, Comic.Pop);
        copyBtn.Interactable = c.nodes.Count > 0;
        pasteBtn.Interactable = c.nodes.Count > 0 && CanPasteNext(c);
        // v7: the ideas row in the cube's colour; an octave copy stops at the outer layers; drums have no octaves, harmonies or flips
        octCube.color = col; harmCube.color = col;
        Color partner = Color.Lerp(col, InkUI.Paper, 0.3f);
        harmUpCube.color = partner; harmDownCube.color = partner;
        octCopyUp.Interactable = CubeOps.CanOctaveCopy(c, 1);
        octCopyDown.Interactable = CubeOps.CanOctaveCopy(c, -1);
        bool harm = CubeOps.HarmonyRefusal(c) == null;
        harmUp.Interactable = harm; harmDown.Interactable = harm;
        flipBtn.Interactable = !drums && c.nodes.Count >= 2 && CubeOps.FlipRefusal(c) == null;
        climbBtn.Interactable = Climb.CanClimb(c);
        InkCaption.Attach(climbBtn.gameObject, Climb.Caption(c.climb));
        upExists = HasLayerCopy(c, c.layer + 1); downExists = HasLayerCopy(c, c.layer - 1);
        PaintGhost(octGhostUp, octCopyUp.Hovered || upExists); PaintGhost(octGhostDown, octCopyDown.Hovered || downExists);
        DrawIdeaMarks();
        DrawFlip(col);
        DrawClimb(col, c.climb);
        // tools
        bool armed = grid.PencilArmed;
        pencilBack.gameObject.SetActive(armed);
        InkDraw.Pencil(pencilArt, armed ? InkUI.Paper : InkUI.Ink, Comic.Pop);
        pencilBtn.Active = armed;
        reverseBtn.Interactable = c.nodes.Count >= 2;
        int mode = (int)c.mode;
        for (int k = 0; k < 3; k++) { modeArt[k].color = k == mode ? Color.white : new Color(1f, 1f, 1f, 0.55f); }
        var mrt = (RectTransform)modeBtns[mode].transform;
        modeWave.rectTransform.anchorMin = modeWave.rectTransform.anchorMax = mrt.anchorMin;
        modeWave.rectTransform.anchoredPosition = mrt.anchoredPosition + new Vector2(0f, -21f);
        // drawer
        foreach (var g in tinted) if (g != null && g.transform.IsChildOf(drawerBody)) g.color = col;
        for (int i = 0; i < shadowCopies.Length; i++)
        {
            bool on = i < c.follow;
            shadowCopies[i].color = on ? Color.Lerp(col, InkUI.Ink, 0.55f + 0.12f * i) : Palette.A(col, 0.18f);
            shadowCopies[i].Hollow = !on;
        }
        for (int i = 0; i < echoCopies.Length; i++)
        {
            bool on = i < c.echo;
            echoCopies[i].color = on ? Palette.A(col, 0.62f - 0.22f * i) : Palette.A(col, 0.16f);
            echoCopies[i].Hollow = !on;
        }
        shimmerArt.gameObject.SetActive(c.shimmer);
        SetOn(shadowCard, c.follow > 0); SetOn(echoCard, c.echo > 0);
        shadowCard.Interactable = !drums; echoCard.Interactable = !drums;
        stampCard.Interactable = SongManager.I != null && SongManager.I.HasSong && SongManager.I.Islands.Count > 1 && !c.IsOnMoon;
        octUp.Interactable = !drums && c.octave < 1; octDown.Interactable = !drums && c.octave > -1;
        InkDraw.Ladder(ladder, InkUI.Ink, col, drums ? -9 : c.octave);
        ((RectTransform)ladder.transform).anchoredPosition = Vector2.zero;
        int gate = (int)c.gate;
        for (int k = 0; k < 3; k++) { gateBtns[k].Interactable = !drums; SetOn(gateBtns[k], k == gate && !drums); }
        var grt = (RectTransform)gateBtns[gate].transform;
        gateWave.rectTransform.anchorMin = gateWave.rectTransform.anchorMax = grt.anchorMin;
        gateWave.rectTransform.anchoredPosition = grt.anchoredPosition + new Vector2(0f, -26f);
        gateWave.gameObject.SetActive(!drums);
        bool legacy = !c.HasDurations;
        legacyRt.gameObject.SetActive(legacy);
        if (legacy)
        {
            dial.color = col;
            dial.Set(c.StepsPerBar, c.hits, c.rot, c.mask, false);
            for (int k = 0; k < 5; k++) { paceCubes[k].color = col; paceCubes[k].Ring = c.step == PaceOrder[k]; }
        }
        float dh = DrawerHeight();
        if (Mathf.Abs(drawer.sizeDelta.y - dh) > 0.5f) { drawer.sizeDelta = new Vector2(DrawerW, dh); ((RectTransform)moreTab.transform).anchoredPosition = new Vector2(-4f, -Mathf.Min(dh - 44f, H - 150f)); }
        if (instPop.gameObject.activeSelf) RefreshInstrumentPopover();
        RegisterHints();
    }

    /// <summary>A drawer card's "on" state: popped out with a 3° tilt and a heavier ink (selection is drawn, never a new hue).</summary>
    static void SetOn(InkButton b, bool on)
    {
        var bg = b.body.Find("Paper") != null ? b.body.Find("Paper").GetComponent<InkShape>() : null;
        b.transform.localRotation = Quaternion.Euler(0f, 0f, on ? 3f : 0f);
        if (bg != null) { if (on) bg.SetInk(2.6f, 3.8f); else bg.SetInk(1.6f, 2.6f); }
        b.Active = on;
    }

    // ------------------------------------------------------------------ spotlight (hold the chip)
    void UpdateSpotlight()
    {
        AudioCube wantSpot = chipHold && cube != null ? cube : null;
        if (wantSpot == spotCube) return;
        if (spotCube != null) Performance.HoldSpotlight(spotCube, false);
        if (wantSpot != null) Performance.HoldSpotlight(wantSpot, true);
        spotCube = wantSpot;
        chipCube.Ring = spotCube != null;
    }

    // ------------------------------------------------------------------ the selected note: length row, world mark
    void HandleNoteSelected(int node)
    {
        // v6: one tear-off at a time — with a note selected the strip's tear-off deletes the note and the cube's steps aside
        if (delBtn != null && delBtn.gameObject.activeSelf != (node < 0)) delBtn.gameObject.SetActive(node < 0);
        if (cube == null) return;
        if (node >= 0) DurationPicker.Show(cube, node);
        else if (DurationPicker.Mode == DurationPicker.Where.Inspector) DurationPicker.Hide();
        // v7: the ideas row rests in the slot; a selected note's length row takes it (the face stays ≤ 20 targets)
        if (ideasRt != null && ideasRt.gameObject.activeSelf != (node < 0)) { ideasRt.gameObject.SetActive(node < 0); if (node >= 0) { harmHover = 0; EndHarmonyPreview(); } }
    }

    Transform worldMark; Material markInk, markCream; float markT;

    // ------------------------------------------------------------------ v9 (N): the selected note's nudge
    /// <summary>Two stacked buttons left of the length row: ▲ "higher" / ▼ "lower" — the selected step plays the next scale note of the song key
    /// above / below its tile (±1 a click, up to ±2; the arrow doubles at 2). One History entry per effective click; the step auditions.</summary>
    void BuildNudge()
    {
        nudgeRt = InkUI.Node("Nudge", lengthSlot);
        InkUI.Centre(nudgeRt, new Vector2(NudgeX, 0f), new Vector2(NudgeBtnW, 2f * NudgeBtnH + 4f));
        nudgeUp = InkButton.Create(nudgeRt, "Higher", new Vector2(NudgeBtnW, NudgeBtnH), Nudge.Word(1));
        InkUI.Centre((RectTransform)nudgeUp.transform, new Vector2(0f, NudgeBtnH * 0.5f + 2f), new Vector2(NudgeBtnW, NudgeBtnH));
        nudgeUpArt = nudgeUp.AddDrawing(new Vector2(30f, 30f)); nudgeUpArt.UnitPx = 1f; nudgeUpArt.Wobble = 0.5f; nudgeUpArt.Seed = 9901;
        nudgeUp.onClick = () => NudgeClick(1);
        nudgeDown = InkButton.Create(nudgeRt, "Lower", new Vector2(NudgeBtnW, NudgeBtnH), Nudge.Word(-1));
        InkUI.Centre((RectTransform)nudgeDown.transform, new Vector2(0f, -NudgeBtnH * 0.5f - 2f), new Vector2(NudgeBtnW, NudgeBtnH));
        nudgeDownArt = nudgeDown.AddDrawing(new Vector2(30f, 30f)); nudgeDownArt.UnitPx = 1f; nudgeDownArt.Wobble = 0.5f; nudgeDownArt.Seed = 9902;
        nudgeDown.onClick = () => NudgeClick(-1);
        DrawNudge(0);
        nudgeRt.gameObject.SetActive(false);
    }

    /// <summary>The arrows for a nudge of <paramref name="n"/>: a solid triangle each way; the active way's doubles at ±2 and boils.</summary>
    void DrawNudge(int n)
    {
        DrawTri(nudgeUpArt, true, n >= 2, n > 0);
        DrawTri(nudgeDownArt, false, n <= -2, n < 0);
        nudgeUp.Active = n > 0; nudgeDown.Active = n < 0;
    }
    static void DrawTri(InkPath p, bool up, bool twice, bool on)
    {
        p.Clear();
        Color c = on ? InkUI.Ink : Palette.A(InkUI.Ink, 0.55f);
        float s = up ? 1f : -1f;
        if (!twice) Tri(p, c, 0f, s);
        else { Tri(p, c, 6.5f * s, s); Tri(p, c, -6.5f * s, s); }
    }
    static void Tri(InkPath p, Color c, float cy, float s)
    {
        p.Line(c, 2.6f, new Vector2(-8.5f, cy - 4.5f * s), new Vector2(0f, cy + 5.5f * s), new Vector2(8.5f, cy - 4.5f * s), new Vector2(-8.5f, cy - 4.5f * s));
    }

    void NudgeClick(int dir)
    {
        var c = cube;
        if (c == null || strip == null || c.IsDrums) return;
        int node = strip.SelectedNote;
        if (node < 0 || node >= c.nodes.Count) return;
        int cur = c.NudgeAt(node), next = Nudge.Clamp(cur + dir);
        if (next == cur) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return; }   // the limit: nothing to push
        c.SetNudge(node, next);
        History.Push();
        NudgeClicks++;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, next > 0 ? 1.25f : (next < 0 ? 0.85f : 1f));
        InkUI.AuditionNote(c, node, Mathf.Max(0.25f, c.DurBeats(node)));
        UpdateNudgeButtons();
    }

    /// <summary>Per frame: the buttons show with a selected pitched note and draw its nudge (an undo changes it under them).</summary>
    void UpdateNudgeButtons()
    {
        if (nudgeRt == null) return;
        var c = cube;
        int node = c != null && strip != null ? strip.SelectedNote : -1;
        bool show = c != null && node >= 0 && node < c.nodes.Count && !c.IsDrums;
        if (nudgeRt.gameObject.activeSelf != show) nudgeRt.gameObject.SetActive(show);
        if (!show) { nudgeShownNode = -2; return; }
        int n = c.NudgeAt(node);
        if (node == nudgeShownNode && n == nudgeShown) return;
        nudgeShownNode = node; nudgeShown = n;
        DrawNudge(n);
        nudgeUp.Interactable = n < Nudge.Max; nudgeDown.Interactable = n > -Nudge.Max;
    }

    /// <summary>v9 (N) tests: the ▲ / ▼ button (dir &gt; 0 = higher) and the effective clicks so far.</summary>
    public static InkButton NudgeButton(int dir) => I == null ? null : (dir > 0 ? I.nudgeUp : I.nudgeDown);
    public static int NudgeClicks { get; private set; }
    public static bool NudgeShown => I != null && I.nudgeRt != null && I.nudgeRt.gameObject.activeInHierarchy;

    void UpdateWorldMark(float dt)
    {
        int node = strip.SelectedNote;
        var c = cube;
        bool show = c != null && node >= 0 && node < c.nodes.Count && c.nodes[node] != null && want;
        if (!show) { if (worldMark != null && worldMark.gameObject.activeSelf) worldMark.gameObject.SetActive(false); return; }
        if (worldMark == null) BuildWorldMark();
        if (!worldMark.gameObject.activeSelf) { worldMark.gameObject.SetActive(true); markT = 0f; }
        markT += dt;
        float st = Look.Stepped(markT);
        float s = 0.95f + 0.07f * Mathf.Sin(st * 7f);
        worldMark.position = c.nodes[node].Top + Vector3.up * 0.07f;
        worldMark.localScale = new Vector3(s, 1f, s);
    }

    void BuildWorldMark()
    {
        worldMark = new GameObject("InspectorNoteMark").transform;
        markInk = Fx.Alpha(IconFactory.GetTexture("ring"), Palette.A(InkUI.Ink, 0.95f)); markInk.renderQueue = 3560;
        markCream = Fx.Alpha(IconFactory.GetTexture("ring"), Palette.A(InkUI.Paper, 1f)); markCream.renderQueue = 3561;
        Quad(worldMark, "Ink", markInk, 1.05f);
        Quad(worldMark, "Cream", markCream, 0.9f);
        worldMark.gameObject.SetActive(false);
    }

    static void Quad(Transform parent, string name, Material m, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size, 1f, size);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }

    // ------------------------------------------------------------------ input helpers
    void ClosePopoversOnOutsideClick()
    {
        if (!PopoverOpen || Time.frameCount - popOpenedFrame <= 1) return;
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;
        Vector2 m = Input.mousePosition;
        if (instPop.gameObject.activeSelf && !RectTransformUtility.RectangleContainsScreenPoint(instPop, m, null) && !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)chip.transform, m, null)) CloseInstrumentPopover();
        if (twinPop.gameObject.activeSelf && !RectTransformUtility.RectangleContainsScreenPoint(twinPop, m, null)) CloseTwinPopover();
    }

    /// <summary>Esc while the card is up: a popover closes first, then the drawer, then the selected note; true when something was closed
    /// (CubeInspector then keeps the card open).</summary>
    public static bool ConsumeEscape()
    {
        var c = I;
        if (c == null || !c.card.gameObject.activeSelf) return false;
        if (PopoverOpen) { c.CloseInstrumentPopover(); c.CloseTwinPopover(); if (c.pasteFan != null) c.pasteFan.Close(); return true; }
        if (c.drawerOpen) { c.SetDrawer(false); return true; }
        if (c.strip != null && c.strip.SelectedNote >= 0) { c.strip.Deselect(); return true; }
        return false;
    }

    /// <summary>Delete / Backspace with a selected note: that note goes (the cube keeps at least one). False when no note is selected.</summary>
    public static bool DeleteSelectedNote()
    {
        var c = I;
        if (c == null || c.strip == null || c.strip.SelectedNote < 0 || c.cube == null) return false;
        c.strip.DeleteSelected();
        return true;
    }

    // ================================================================== tests
    /// <summary>Hit targets on the card's face right now (active raycast targets outside the drawer and the popovers; the grid and the strip
    /// count once each, as they hit-test their cells / blocks themselves; the drawer tab counts once).</summary>
    public static int FaceTargetCount()
    {
        var c = I; if (c == null || !c.card.gameObject.activeInHierarchy) return 0;
        int n = 0;
        foreach (var g in c.card.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.raycastTarget || !g.isActiveAndEnabled) continue;
            if (g.transform.IsChildOf(c.drawerBody)) continue;
            if (g == c.paper || g == c.drawerPaper) continue;
            if (g.transform.IsChildOf(c.grid.transform) && g.transform != c.grid.transform) continue;
            if (g.transform.IsChildOf(c.strip.transform) && g.transform != c.strip.transform) continue;
            if (Hints.EffectiveAlpha(g.rectTransform) < 0.5f) continue;
            n++;
        }
        if (c.strip.SelectedNote >= 0) n++;   // the strip's tear-off on the selected note (hit-tested by the strip)
        return n;
    }

    /// <summary>Tests: the parts, as the pointer reaches them.</summary>
    public static InkButton ChipButton => I != null ? I.chip : null;
    public static InkButton CloseButton => I != null ? I.closeBtn : null;
    public static InkButton DeleteButton => I != null ? I.delBtn : null;
    public static InkButton MoreTab => I != null ? I.moreTab : null;
    public static InkButton ModeButton(int k) => I != null && k >= 0 && k < 3 ? I.modeBtns[k] : null;
    public static InkButton PencilButton => I != null ? I.pencilBtn : null;
    public static InkButton ReverseButton => I != null ? I.reverseBtn : null;
    public static InkButton DrawerButton(string name)
    {
        if (I == null) return null;
        switch (name)
        {
            case "twin": return I.twinCard; case "shadow": return I.shadowCard; case "echo": return I.echoCard;   // (v7 §21: no "rider" card)
            case "stamp": return I.stampCard; case "up": return I.octUp; case "down": return I.octDown;
            case "gate0": return I.gateBtns[0]; case "gate1": return I.gateBtns[1]; case "gate2": return I.gateBtns[2];
        }
        if (name.StartsWith("pace") && name.Length == 5) { int k = name[4] - '0'; if (k >= 0 && k < 5) return I.paceBtns[k]; }
        return null;
    }
    public static InkButton InstrumentButton(int k) => I != null && k >= 0 && k < Instruments.Count ? I.instBtns[k] : null;
    /// <summary>v6: the copy / paste → next buttons, the popover's voice chips (null when not shown) and how many it shows.</summary>
    public static InkButton CopyButton => I != null ? I.copyBtn : null;
    public static InkButton PasteNextButton => I != null ? I.pasteBtn : null;
    public static InkButton VoiceButton(int v) => I != null && v >= 0 && v < I.voiceN ? I.voiceBtns[v] : null;
    public static int VoiceChipCount => I != null && I.instPop != null && I.instPop.gameObject.activeSelf ? I.voiceN : 0;
    /// <summary>The voice whose pips the chip wears.</summary>
    public static int ChipVoice => I != null && I.cube != null ? ShownVoice(I.cube) : 0;
    public static RectTransform Popover => I != null ? I.instPop : null;
    public static HudSlider Fader => I != null ? I.fader : null;
    public static NecklaceDial Dial => I != null ? I.dial : null;
    public static InkShape Bubble => I != null ? I.paper : null;
    /// <summary>The tail's tip in screen pixels (tests: it points at the proxy).</summary>
    public static Vector2 TailTipScreen => I != null && I.paper != null ? (Vector2)RectTransformUtility.WorldToScreenPoint(null, I.paper.rectTransform.TransformPoint(I.paper.TailTip)) : Vector2.zero;
    /// <summary>Tests: one frame of the card's own work (GC measurements).</summary>
    public static void StepForTest() { if (I != null) I.LateUpdate(); }
    /// <summary>Drawer slide progress 0..1.</summary>
    public static float DrawerShown => I != null ? I.drawerT : 0f;
    public static Transform WorldMark => I != null ? I.worldMark : null;
}

// ====================================================================================================================================
// Shared U2 helpers: the overlay canvas, the ink palette, the length audition, a line-drawing graphic, an icon-only ink control, pictures.
// ====================================================================================================================================

/// <summary>
/// U2 helpers shared by the inspector card, the rhythm strip and the note-length row: the overlay canvas they live on (above the HUD, below the
/// menu and the tutorial bubble), the ink palette, small placement helpers and the length audition (a note played for its length).
/// </summary>
public static class InkUI
{
    public const int SortOrder = 15;
    public static readonly Color Paper = Comic.Cream;
    public static readonly Color Ink = Comic.Ink;
    /// <summary>A shade darker than the page: drawer cards' lip, the tile in the stamp picture.</summary>
    public static readonly Color PaperDeep = new Color(0.9f, 0.83f, 0.8f);
    /// <summary>The one red: tear-off deletes.</summary>
    public static readonly Color Red = new Color(0.92f, 0.32f, 0.36f);
    public static readonly string[] InstrumentWords = { "keys", "pluck", "pad", "lead", "bass", "bells", "strings", "choir", "piano", "drums", "fx" };   // v9 (G): + fx

    static Canvas canvas; static RectTransform root;
    public static Canvas Canvas { get { Ensure(); return canvas; } }
    /// <summary>The overlay canvas's root (1920 x 1080 reference, centre pivot).</summary>
    public static RectTransform OverlayRoot { get { Ensure(); return root; } }
    /// <summary>Canvas units per screen pixel.</summary>
    public static float UnitsPerPixel { get { Ensure(); return canvas.scaleFactor > 1e-4f ? 1f / canvas.scaleFactor : 1f; } }

    static void Ensure()
    {
        if (root != null) return;
        var go = new GameObject("InspectorCanvas", typeof(RectTransform));
        go.layer = 5;
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        sc.referencePixelsPerUnit = 100f;
        go.AddComponent<GraphicRaycaster>();
        root = (RectTransform)go.transform;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { canvas = null; root = null; AuditionCount = 0; lastAudition = -9f; hopGlyphs.Clear(); hopEnds.Clear(); }

    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Places a centred child at (cx, cy) measured from the parent's top-left corner (y down).</summary>
    public static void AtTopLeft(RectTransform r, float cx, float cyDown, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.anchoredPosition = new Vector2(cx, -cyDown);
    }

    public static void Centre(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
    }

    public static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }

    /// <summary>An invisible raycast target filling <paramref name="go"/> (the hit area of a drawn control).</summary>
    public static Image HitArea(GameObject go)
    {
        var im = go.GetComponent<Image>();
        if (im == null) im = go.AddComponent<Image>();
        im.color = new Color(1f, 1f, 1f, 0.001f);
        im.raycastTarget = true;
        return im;
    }

    // ------------------------------------------------------------------ CubeGlyph hops that land
    static readonly List<CubeGlyph> hopGlyphs = new List<CubeGlyph>(8);
    static readonly List<float> hopEnds = new List<float>(8);

    /// <summary>CubeGlyph.Hop, then a redraw once it has landed (the kit's hop stops redrawing the moment it ends, which can leave the last
    /// in-flight drawing up when the hop ends inside a twos-frame; see kit_notes.md). Call <see cref="TickHops"/> every frame.</summary>
    public static void Hop(CubeGlyph g, float height)
    {
        if (g == null) return;
        g.Hop(height);
        int i = hopGlyphs.IndexOf(g);
        if (i < 0) { hopGlyphs.Add(g); hopEnds.Add(0f); i = hopGlyphs.Count - 1; }
        hopEnds[i] = Time.unscaledTime + 0.32f;
    }

    public static void TickHops()
    {
        for (int i = hopGlyphs.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime < hopEnds[i]) continue;
            var g = hopGlyphs[i];
            if (g != null) g.SetVerticesDirty();
            hopGlyphs.RemoveAt(i); hopEnds.RemoveAt(i);
        }
    }

    public static Color Desaturate(Color c, float sat)
    {
        float h, s, v; Color.RGBToHSV(c, out h, out s, out v);
        var o = Color.HSVToRGB(h, s * sat, v); o.a = c.a; return o;
    }

    // ------------------------------------------------------------------ the length audition
    /// <summary>Length auditions played so far (tests).</summary>
    public static int AuditionCount { get; private set; }
    static float lastAudition = -9f;
    static readonly VoiceRules.NoteEvent[] ev1 = new VoiceRules.NoteEvent[1];

    /// <summary>Plays node <paramref name="node"/> of <paramref name="c"/> (its tile, instrument and octave) for <paramref name="beats"/> beats
    /// (x 0.92, the normal gate): at once when stopped, on the next 16th while playing (preview owner, never past the island's window); where a
    /// preview is not harmonic right now (another island is lit) the tile only flashes. <paramref name="throttle"/>: at most 10 a second.</summary>
    public static bool AuditionNote(AudioCube c, int node, float beats, bool throttle = false)
    {
        if (c == null || node < 0 || node >= c.nodes.Count) return false;
        var tile = c.nodes[node];
        if (tile == null) return false;
        if (throttle && Time.unscaledTime - lastAudition < 0.1f) return false;
        lastAudition = Time.unscaledTime;
        AuditionCount++;
        int inst = Mathf.Clamp(c.instrument, 0, Instruments.Count - 1);
        if (!Synth.Ready) { tile.PlayPreview(inst, c.octave, c.voice); return true; }
        int slot = Instruments.SlotOf(inst, c.voice);   // v6: the cube's own sound in its group
        if (!VoiceRules.PreviewAllowed(tile, slot)) { tile.PlayPreview(inst, c.octave, c.voice); return true; }
        var e = VoiceRules.PreviewEvent(c, node);   // v7 (A): the node's layer and bend (an octave copy / a harmony cube previews as it sounds)
        bool drums = SynthBank.Def(slot).drums;
        double len = Math.Max(0.06, beats / Math.Max(0.1, GlobalClock.BeatsPerSecond) * 0.92);
        if (GlobalClock.IsPlaying)
        {
            if (!drums)
            {
                double on, end;
                VoiceRules.PreviewBeat(out on, out end);
                e.offDsp = Math.Max(e.onDsp + 0.03, Math.Min(e.onDsp + len, end - 0.005));
            }
            ev1[0] = e;
            VoiceRules.Dispatch(ev1, 1, VoiceRules.OwnerPreview);
        }
        else Synth.Preview(e.slot, e.midi, e.vel, drums ? 0.12f : (float)len);
        tile.Press(0.6f);
        tile.Flash(c.Color, 0.7f);
        return true;
    }
}

/// <summary>
/// U2: a small hand-inked line drawing — polylines, closed loops, filled polygons and dots — in a unit box (-1..1, y up) mapped onto the square
/// of min(width, height) centred in the rect, or in pixels around the rect centre (<see cref="UnitPx"/> = 1). Lines get a seeded
/// low-frequency wobble along their length (tapered to the joints) and round joins / caps; <see cref="Boil"/> re-draws the wobble at 10 fps
/// (hovered / pressed / active only). Stroke colours are multiplied by Graphic.color. The mesh is rebuilt only when the drawing or a
/// parameter changes, without allocating (Begin / P / End* add strokes allocation-free; the params helpers are for build time).
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class InkPath : MaskableGraphic
{
    public const float BoilFps = 10f, Feather = 0.9f;
    struct Stroke { public int start, count; public byte kind; public Color32 ink, fill; public float width; }
    const byte KLine = 0, KLoop = 1, KPoly = 2, KFill = 3, KDot = 4;

    readonly List<Stroke> strokes = new List<Stroke>(8);
    readonly List<Vector2> pts = new List<Vector2>(32);
    [SerializeField] float wobble = 0.7f;
    [SerializeField] int seed;
    [SerializeField] float unitPx;
    bool boil; int boilFrame = -1, open = -1;

    /// <summary>Wobble amplitude (px).</summary>
    public float Wobble { get => wobble; set { if (wobble != value) { wobble = value; SetVerticesDirty(); } } }
    public bool Boil { get => boil; set { if (boil != value) { boil = value; SetVerticesDirty(); } } }
    public int Seed { get => seed; set { if (seed != value) { seed = value; SetVerticesDirty(); } } }
    /// <summary>Pixels per drawing unit; ≤ 0 = half of min(width, height) (the unit box fills the rect).</summary>
    public float UnitPx { get => unitPx; set { if (unitPx != value) { unitPx = value; SetVerticesDirty(); } } }
    public int StrokeCount => strokes.Count;

    public static InkPath Create(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<InkPath>();
        p.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = size;
        return p;
    }

    public InkPath Clear() { strokes.Clear(); pts.Clear(); open = -1; SetVerticesDirty(); return this; }
    public InkPath Line(Color c, float w, params Vector2[] p) => Add(KLine, c, c, w, p);
    public InkPath Loop(Color c, float w, params Vector2[] p) => Add(KLoop, c, c, w, p);
    public InkPath Poly(Color fill, Color ink, float w, params Vector2[] p) => Add(KPoly, ink, fill, w, p);
    public InkPath Fill(Color fill, params Vector2[] p) => Add(KFill, fill, fill, 0f, p);
    public InkPath Dot(Color c, Vector2 centre, float radiusPx) { Begin(); P(centre); return End(KDot, c, c, radiusPx); }
    public InkPath Arc(Color c, float w, Vector2 centre, float r, float fromDeg, float toDeg, int steps = 18)
    {
        Begin();
        for (int i = 0; i <= steps; i++) { float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad; P(centre.x + Mathf.Cos(a) * r, centre.y + Mathf.Sin(a) * r); }
        return EndLine(c, w);
    }
    public InkPath Begin() { open = pts.Count; return this; }
    public InkPath P(float x, float y) { pts.Add(new Vector2(x, y)); return this; }
    public InkPath P(Vector2 v) { pts.Add(v); return this; }
    public InkPath EndLine(Color c, float w) => End(KLine, c, c, w);
    public InkPath EndLoop(Color c, float w) => End(KLoop, c, c, w);
    public InkPath EndPoly(Color fill, Color ink, float w) => End(KPoly, ink, fill, w);
    public InkPath EndFill(Color fill) => End(KFill, fill, fill, 0f);

    InkPath Add(byte kind, Color ink, Color fill, float w, Vector2[] p) { Begin(); for (int i = 0; i < p.Length; i++) pts.Add(p[i]); return End(kind, ink, fill, w); }

    InkPath End(byte kind, Color ink, Color fill, float w)
    {
        if (open < 0) open = pts.Count;
        int n = pts.Count - open;
        if (n > 0) strokes.Add(new Stroke { start = open, count = n, kind = kind, ink = ink, fill = fill, width = w });
        open = -1;
        SetVerticesDirty();
        return this;
    }

    int EffectiveSeed { get { if (seed != 0) return seed; unchecked { int h = 13; foreach (char ch in gameObject.name) h = h * 31 + ch; return h == 0 ? 1 : h; } } }

    void Update()
    {
        if (!boil) return;
        int f = Mathf.FloorToInt(Time.unscaledTime * BoilFps);
        if (f != boilFrame) { boilFrame = f; SetVerticesDirty(); }
    }

    static float Hash01(int a, int b)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h >> 8) / 16777216f;
        }
    }
    static float Signed(int a, int b) => Hash01(a, b) * 2f - 1f;

    // per-rebuild mapping state (no allocation)
    Vector2 mc; float mk; int ms, mphase;

    Vector2 Map(int i)
    {
        Vector2 p = mc + pts[i] * mk;
        if (wobble > 0f) { int s = ms + mphase * 7919; p += new Vector2(Signed(s, i * 2), Signed(s, i * 2 + 1)) * (wobble * 0.6f); }
        return p;
    }

    static Color32 Tint(Color32 a, Color t) => new Color32((byte)(a.r * t.r), (byte)(a.g * t.g), (byte)(a.b * t.b), (byte)(a.a * t.a));

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        mc = r.center;
        mk = unitPx > 0f ? unitPx : Mathf.Min(r.width, r.height) * 0.5f;
        if (mk <= 0f) return;
        ms = EffectiveSeed;
        mphase = boil ? Mathf.FloorToInt(Time.unscaledTime * BoilFps) : 0;
        Color t = color;
        for (int si = 0; si < strokes.Count; si++)
        {
            var s = strokes[si];
            Color32 ink = Tint(s.ink, t), fill = Tint(s.fill, t);
            Color32 inkOff = ink; inkOff.a = 0;
            switch (s.kind)
            {
                case KDot: Disc(vh, Map(s.start), s.width, ink, inkOff); break;
                case KFill: FanFill(vh, s.start, s.count, fill); break;
                case KPoly: FanFill(vh, s.start, s.count, fill); Polyline(vh, s, true, ink, inkOff, si); break;
                case KLoop: Polyline(vh, s, true, ink, inkOff, si); break;
                default: Polyline(vh, s, false, ink, inkOff, si); break;
            }
        }
    }

    void Polyline(VertexHelper vh, Stroke s, bool closed, Color32 on, Color32 off, int si)
    {
        if (s.width <= 0f || on.a == 0) return;
        int n = s.count;
        float h = s.width * 0.5f;
        if (n == 1) { Disc(vh, Map(s.start), h, on, off); return; }
        int segs = closed ? n : n - 1;
        for (int j = 0; j < segs; j++)
        {
            Vector2 a = Map(s.start + j), b = Map(s.start + (j + 1) % n);
            Segment(vh, a, b, h, on, off, ms ^ (si * 131 + j * 17 + mphase * 7919));
        }
        for (int j = 0; j < n; j++) Disc(vh, Map(s.start + j), h, on, off);   // round joins and caps
    }

    void Segment(VertexHelper vh, Vector2 a, Vector2 b, float h, Color32 on, Color32 off, int nseed)
    {
        Vector2 d = b - a; float len = d.magnitude;
        if (len < 0.01f) return;
        Vector2 n = new Vector2(-d.y, d.x) / len;
        int pieces = wobble > 0f ? Mathf.Clamp(Mathf.CeilToInt(len / 7f), 1, 48) : 1;
        int b0 = vh.currentVertCount;
        for (int j = 0; j <= pieces; j++)
        {
            float u = j / (float)pieces;
            Vector2 p = a + d * u;
            if (pieces > 1) p += n * (PerpNoise(nseed, u * len) * wobble * Mathf.Sin(u * Mathf.PI));
            vh.AddVert(p + n * (h + Feather), off, Vector2.zero);
            vh.AddVert(p + n * h, on, Vector2.zero);
            vh.AddVert(p - n * h, on, Vector2.zero);
            vh.AddVert(p - n * (h + Feather), off, Vector2.zero);
            if (j > 0)
            {
                int q = b0 + (j - 1) * 4, w = b0 + j * 4;
                for (int k = 0; k < 3; k++) { vh.AddTriangle(q + k, q + k + 1, w + k + 1); vh.AddTriangle(q + k, w + k + 1, w + k); }
            }
        }
    }

    static float PerpNoise(int s, float arcPx)
    {
        float x = arcPx / 22f; int i = Mathf.FloorToInt(x); float f = x - i; f = f * f * (3f - 2f * f);
        return Mathf.Lerp(Signed(s, i), Signed(s, i + 1), f);
    }

    static void Disc(VertexHelper vh, Vector2 c, float r, Color32 on, Color32 off)
    {
        if (r <= 0f || on.a == 0) return;
        int N = r < 3f ? 8 : (r < 9f ? 14 : 24);
        int b0 = vh.currentVertCount;
        vh.AddVert(c, on, Vector2.zero);
        for (int i = 0; i < N; i++)
        {
            float a = i * (Mathf.PI * 2f / N);
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            vh.AddVert(c + d * r, on, Vector2.zero);
            vh.AddVert(c + d * (r + Feather), off, Vector2.zero);
        }
        for (int i = 0; i < N; i++)
        {
            int j = (i + 1) % N;
            int ri = b0 + 1 + i * 2, rj = b0 + 1 + j * 2;
            vh.AddTriangle(b0, ri, rj);
            vh.AddTriangle(ri, ri + 1, rj + 1);
            vh.AddTriangle(ri, rj + 1, rj);
        }
    }

    void FanFill(VertexHelper vh, int start, int count, Color32 col)
    {
        if (count < 3 || col.a == 0) return;
        Vector2 c = Vector2.zero;
        for (int i = 0; i < count; i++) c += Map(start + i);
        c /= count;
        int b0 = vh.currentVertCount;
        vh.AddVert(c, col, Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddVert(Map(start + i), col, Vector2.zero);
        for (int i = 0; i < count; i++) vh.AddTriangle(b0, b0 + 1 + i, b0 + 1 + (i + 1) % count);
    }
}

/// <summary>
/// U2: an icon-only ink control — an invisible hit area and a body (drawings, a backing shape, cubes) that grows on hover in twos-steps and
/// slides onto its shadow while pressed. Click → onClick; right click → onRightClick; holding <see cref="holdSeconds"/> → onHold and
/// onHoldChanged(true), the click is then swallowed (onHoldChanged(false) on release); hover → onHover and a caption (InkCaption.Attach).
/// Disabled = 35 % and no response. Its drawings boil while hovered, pressed or <see cref="Active"/> (never at rest).
/// </summary>
public class InkButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public Action onClick, onRightClick, onHold;
    public Action<bool> onHoldChanged, onHover;
    public float holdSeconds;
    public float hoverScale = 1.08f;
    public Vector2 pressOffset = new Vector2(2f, -2f);
    public RectTransform body;
    public InkPath drawing;
    readonly List<InkPath> boils = new List<InkPath>(2);
    CanvasGroup group;
    bool interactable = true, active, hover, pressed, holdFired, holding;
    float pressT, anim = 1f; int lastStep = int.MinValue;

    public bool Hovered => hover;
    public bool Holding => holding;
    public bool Interactable
    {
        get => interactable;
        set
        {
            if (interactable == value) return;
            interactable = value;
            if (group != null) group.alpha = value ? 1f : 0.35f;
            if (!value) { hover = false; pressed = false; ReleaseHold(); }
            ApplyBoil();
        }
    }
    public bool Active { get => active; set { if (active == value) return; active = value; ApplyBoil(); } }

    public static InkButton Create(Transform parent, string name, Vector2 hitSize, string caption)
    {
        var rt = InkUI.Node(name, parent);
        rt.sizeDelta = hitSize;
        InkUI.HitArea(rt.gameObject);
        var b = rt.gameObject.AddComponent<InkButton>();
        b.body = InkUI.Node("Body", rt);
        InkUI.Stretch(b.body);
        b.group = b.body.gameObject.AddComponent<CanvasGroup>();
        b.group.blocksRaycasts = false;   // the hit area alone takes the pointer
        if (!string.IsNullOrEmpty(caption)) InkCaption.Attach(rt.gameObject, caption);
        return b;
    }

    /// <summary>Adds a drawing to the body (it boils with the button).</summary>
    public InkPath AddDrawing(Vector2 size)
    {
        var p = InkPath.Create(body, "Drawing" + boils.Count, size);
        boils.Add(p);
        if (drawing == null) drawing = p;
        return p;
    }

    void ApplyBoil() { bool on = interactable && Look.Boil && (hover || pressed || active); for (int i = 0; i < boils.Count; i++) if (boils[i] != null) boils[i].Boil = on; }

    public void OnPointerEnter(PointerEventData e) { if (!interactable) return; hover = true; ApplyBoil(); onHover?.Invoke(true); }
    public void OnPointerExit(PointerEventData e) { bool was = hover; hover = false; pressed = false; ReleaseHold(); ApplyBoil(); if (was) onHover?.Invoke(false); }
    public void OnPointerDown(PointerEventData e)
    {
        if (!interactable || e.button != PointerEventData.InputButton.Left) return;
        pressed = true; pressT = Time.unscaledTime; holdFired = false; ApplyBoil();
    }
    public void OnPointerUp(PointerEventData e) { if (e.button != PointerEventData.InputButton.Left) return; pressed = false; ReleaseHold(); ApplyBoil(); }
    public void OnPointerClick(PointerEventData e)
    {
        if (!interactable) return;
        if (e.button == PointerEventData.InputButton.Right) { onRightClick?.Invoke(); return; }
        if (e.button != PointerEventData.InputButton.Left) return;
        if (holdFired) { holdFired = false; return; }
        Click();
    }
    /// <summary>The click (tests call it directly).</summary>
    public void Click() { if (!interactable) return; onClick?.Invoke(); }
    /// <summary>Tests: the hold as the pointer does it (onHold, then release).</summary>
    public void SimHold() { if (!interactable) return; holdFired = false; onHold?.Invoke(); onHoldChanged?.Invoke(true); onHoldChanged?.Invoke(false); }
    public void SimHover(bool on) { if (on) OnPointerEnter(null); else OnPointerExit(null); }

    void ReleaseHold() { if (holding) { holding = false; onHoldChanged?.Invoke(false); } }

    void OnDisable()
    {
        bool was = hover;
        hover = false; pressed = false; ReleaseHold(); ApplyBoil();
        if (was) onHover?.Invoke(false);
        anim = 1f;
        if (body != null) { body.localScale = Vector3.one; body.anchoredPosition = Vector2.zero; }
    }

    void Update()
    {
        if (pressed && holdSeconds > 0f && !holdFired && Time.unscaledTime - pressT >= holdSeconds)
        { holdFired = true; holding = true; onHold?.Invoke(); onHoldChanged?.Invoke(true); }
        if (body == null) return;
        int step = Mathf.FloorToInt(Time.unscaledTime * Look.TwosFps);
        if (step == lastStep) return;
        lastStep = step;
        float target = hover && interactable ? hoverScale : 1f;
        float cur = body.localScale.x;
        if (Mathf.Abs(cur - target) > 1e-3f) { anim = Mathf.MoveTowards(cur, target, Mathf.Max(0.04f, Mathf.Abs(cur - target) * 0.5f)); body.localScale = new Vector3(anim, anim, 1f); }
        Vector2 off = pressed ? pressOffset : Vector2.zero;
        if (body.anchoredPosition != off) body.anchoredPosition = off;
    }
}

/// <summary>U2: the pictures of the card, the strip and the length row, drawn into an InkPath (unit box, y up; build time only).</summary>
public static class InkDraw
{
    static Vector2 V(float x, float y) => new Vector2(x, y);
    static Vector2 Rot(Vector2 v, float deg) { float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
    static Vector2 Card(Vector2 centre, float w, float h, float deg, float u, float v) => centre + Rot(new Vector2(u * w * 0.5f, v * h * 0.5f), deg);

    /// <summary>v6 copy: two paper cards, the back one askew, the front one carrying the pattern (three notes joined by a line in
    /// <paramref name="col"/>).</summary>
    public static void Copy(InkPath p, Color ink, Color paper, Color paperDeep, Color col)
    {
        p.Clear();
        Vector2 b = V(-0.2f, 0.16f), f = V(0.12f, -0.1f);
        float bw = 1.0f, bh = 0.78f, fw = 1.04f, fh = 0.8f, ba = -12f, fa = 5f;
        p.Poly(paperDeep, ink, 2f, Card(b, bw, bh, ba, -1f, -1f), Card(b, bw, bh, ba, 1f, -1f), Card(b, bw, bh, ba, 1f, 1f), Card(b, bw, bh, ba, -1f, 1f));
        p.Poly(paper, ink, 2.4f, Card(f, fw, fh, fa, -1f, -1f), Card(f, fw, fh, fa, 1f, -1f), Card(f, fw, fh, fa, 1f, 1f), Card(f, fw, fh, fa, -1f, 1f));
        Vector2 n0 = Card(f, fw, fh, fa, -0.55f, -0.35f), n1 = Card(f, fw, fh, fa, 0f, 0.3f), n2 = Card(f, fw, fh, fa, 0.55f, -0.05f);
        p.Line(ink, 4.4f, n0, n1, n2).Line(Comic.Opaque(col), 2.4f, n0, n1, n2);
        p.Dot(ink, n0, 3.6f).Dot(ink, n1, 3.6f).Dot(ink, n2, 3.6f);
        p.Dot(Comic.Opaque(col), n0, 2.3f).Dot(Comic.Opaque(col), n1, 2.3f).Dot(Comic.Opaque(col), n2, 2.3f);
    }

    /// <summary>v6 paste → next grid (v7 §21: it APPEARS there, nothing jumps between grids): the copied pattern (three notes joined by a line in
    /// <paramref name="col"/>), a short arrow, the same pattern standing on the next grid (an isometric 2 × 2 tile) with a sparkle.</summary>
    public static void PasteNext(InkPath p, Color ink, Color paper, Color paperDeep, Color col, Color spark)
    {
        p.Clear();
        Color c = Comic.Opaque(col);
        // the grid it lands on
        Vector2 g = V(0.6f, -0.44f);
        float hw = 0.54f, hh = 0.31f;
        p.Poly(paper, ink, 2.4f, g + V(-hw, 0f), g + V(0f, hh), g + V(hw, 0f), g + V(0f, -hh));
        p.Line(Palette.A(ink, 0.55f), 1.5f, g + V(-hw * 0.5f, hh * 0.5f), g + V(hw * 0.5f, -hh * 0.5f)).Line(Palette.A(ink, 0.55f), 1.5f, g + V(-hw * 0.5f, -hh * 0.5f), g + V(hw * 0.5f, hh * 0.5f));
        // v7 §21 (nothing jumps between grids): the pattern APPEARS on the next grid — the copied pattern on the left, a short straight arrow,
        // the same pattern standing on the grid with a sparkle where it popped in
        Vector2 n0 = V(-1.06f, 0.36f), n1 = V(-0.84f, 0.68f), n2 = V(-0.6f, 0.46f);
        p.Line(Palette.A(ink, 0.6f), 3.8f, n0, n1, n2).Line(Palette.A(c, 0.6f), 2f, n0, n1, n2);
        p.Dot(Palette.A(ink, 0.6f), n0, 3.2f).Dot(Palette.A(ink, 0.6f), n1, 3.2f).Dot(Palette.A(ink, 0.6f), n2, 3.2f);
        p.Line(ink, 2.4f, V(-0.42f, 0.2f), V(-0.04f, 0.2f));
        ArrowHead(p, ink, 2.4f, V(0.04f, 0.2f), V(1f, 0f), 0.2f);
        Vector2 m0 = g + V(-0.3f, -0.02f), m1 = g + V(-0.06f, 0.2f), m2 = g + V(0.2f, 0.06f);
        p.Line(ink, 4.8f, m0, m1, m2).Line(c, 2.6f, m0, m1, m2);
        p.Dot(ink, m0, 3.9f).Dot(ink, m1, 3.9f).Dot(ink, m2, 3.9f);
        p.Dot(c, m0, 2.5f).Dot(c, m1, 2.5f).Dot(c, m2, 2.5f);
        Vector2 sp = V(1.0f, 0.12f);
        p.Line(spark, 2.4f, sp + V(-0.14f, 0f), sp + V(0.14f, 0f)).Line(spark, 2.4f, sp + V(0f, -0.14f), sp + V(0f, 0.14f));
    }



    public static void Cross(InkPath p, Color ink, float w)
    {
        p.Clear().Line(ink, w, V(-0.55f, -0.55f), V(0.55f, 0.55f)).Line(ink, w, V(-0.55f, 0.55f), V(0.55f, -0.55f));
    }

    public static void Dots3(InkPath p, Color ink)
    {
        p.Clear().UnitPx = 1f;
        p.Dot(ink, V(0f, 12f), 3.4f).Dot(ink, V(0f, 0f), 3.4f).Dot(ink, V(0f, -12f), 3.4f);
    }

    public static void ArrowHead(InkPath p, Color ink, float w, Vector2 tip, Vector2 dir, float size)
    {
        dir.Normalize();
        var n = new Vector2(-dir.y, dir.x);
        p.Line(ink, w, tip - dir * size + n * size * 0.7f, tip, tip - dir * size - n * size * 0.7f);
    }

    /// <summary>A pencil (yellow body, ink outline, a dark tip) lying on the diagonal.</summary>
    public static void Pencil(InkPath p, Color ink, Color body)
    {
        p.Clear();
        var u = new Vector2(0.7071f, 0.7071f); var v = new Vector2(-0.7071f, 0.7071f);
        float hw = 0.21f, t0 = -0.9f, t1 = 0.3f, t2 = 0.82f;
        p.Poly(body, ink, 2.4f, u * t0 + v * hw, u * t1 + v * hw, u * t1 - v * hw, u * t0 - v * hw);
        p.Poly(new Color(0.98f, 0.86f, 0.74f), ink, 2.4f, u * t1 + v * hw, u * t2, u * t1 - v * hw);
        p.Fill(ink, u * (t2 - 0.2f) + v * hw * 0.38f, u * t2, u * (t2 - 0.2f) - v * hw * 0.38f);
        p.Line(ink, 2f, u * (t0 + 0.22f) + v * hw, u * (t0 + 0.22f) - v * hw);
    }

    /// <summary>Two curved arrows chasing each other (the path turned around).</summary>
    public static void Reverse(InkPath p, Color ink)
    {
        p.Clear();
        p.Arc(ink, 3f, V(0f, -0.18f), 0.62f, 160f, 28f, 14);
        ArrowHead(p, ink, 3f, V(Mathf.Cos(28f * Mathf.Deg2Rad) * 0.62f, -0.18f + Mathf.Sin(28f * Mathf.Deg2Rad) * 0.62f), V(0.47f, -0.88f), 0.26f);
        p.Arc(ink, 3f, V(0f, 0.18f), 0.62f, -20f, -152f, 14);
        ArrowHead(p, ink, 3f, V(Mathf.Cos(-152f * Mathf.Deg2Rad) * 0.62f, 0.18f + Mathf.Sin(-152f * Mathf.Deg2Rad) * 0.62f), V(-0.47f, 0.88f), 0.26f);
    }

    /// <summary>Loop: a closed track with beads, a chevron showing the way round.</summary>
    public static void ModeLoop(InkPath p, Color ink)
    {
        p.Clear().Begin();
        float r = 0.42f, x = 0.5f;
        for (int i = 0; i <= 10; i++) { float a = (90f + 180f * i / 10f) * Mathf.Deg2Rad; p.P(-x + Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
        for (int i = 0; i <= 10; i++) { float a = (-90f + 180f * i / 10f) * Mathf.Deg2Rad; p.P(x + Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
        p.EndLoop(ink, 2.6f);
        p.Dot(ink, V(-x, r), 3.2f).Dot(ink, V(x, r), 3.2f).Dot(ink, V(0f, -r), 3.2f);
        ArrowHead(p, ink, 2.4f, V(0.2f, r), V(1f, 0f), 0.2f);
    }

    /// <summary>Back and forth: a line with an arrowhead at both ends.</summary>
    public static void ModePingPong(InkPath p, Color ink)
    {
        p.Clear().Line(ink, 2.6f, V(-0.85f, 0f), V(0.85f, 0f));
        ArrowHead(p, ink, 2.6f, V(-0.9f, 0f), V(-1f, 0f), 0.26f);
        ArrowHead(p, ink, 2.6f, V(0.9f, 0f), V(1f, 0f), 0.26f);
        p.Dot(ink, V(-0.35f, 0f), 3.2f).Dot(ink, V(0.3f, 0f), 3.2f);
    }

    /// <summary>Once: a line that ends in a bar.</summary>
    public static void ModeOnce(InkPath p, Color ink)
    {
        p.Clear().Line(ink, 2.6f, V(-0.9f, 0f), V(0.62f, 0f)).Line(ink, 3.4f, V(0.8f, -0.42f), V(0.8f, 0.42f));
        p.Dot(ink, V(-0.55f, 0f), 3.2f).Dot(ink, V(0.05f, 0f), 3.2f);
        ArrowHead(p, ink, 2.4f, V(0.62f, 0f), V(1f, 0f), 0.2f);
    }

    /// <summary>A speaker cone with one sound arc.</summary>
    public static void Speaker(InkPath p, Color ink, Color paper)
    {
        p.Clear().Poly(paper, ink, 2.2f, V(-0.85f, -0.28f), V(-0.45f, -0.28f), V(0.05f, -0.7f), V(0.05f, 0.7f), V(-0.45f, 0.28f), V(-0.85f, 0.28f));
        p.Arc(ink, 2.2f, V(0.1f, 0f), 0.5f, -48f, 48f, 8);
    }

    public static void Triangle(InkPath p, Color ink, bool up)
    {
        float s = up ? 1f : -1f;
        p.Clear().Poly(ink, ink, 2f, V(-0.7f, -0.45f * s), V(0.7f, -0.45f * s), V(0f, 0.6f * s));
    }

    /// <summary>The octave ladder: two rails, three rungs; the rung of <paramref name="octave"/> (-1..1) is a fat rung in the cube's colour.</summary>
    public static void Ladder(InkPath p, Color ink, Color fill, int octave)
    {
        p.Clear().Line(ink, 2.4f, V(-0.42f, -0.95f), V(-0.42f, 0.95f)).Line(ink, 2.4f, V(0.42f, -0.95f), V(0.42f, 0.95f));
        for (int k = -1; k <= 1; k++)
        {
            float y = k * 0.55f;
            if (k == octave) { p.Line(ink, 9f, V(-0.42f, y), V(0.42f, y)); p.Line(Comic.Opaque(fill), 5f, V(-0.4f, y), V(0.4f, y)); }
            else p.Line(Palette.A(ink, 0.55f), 2f, V(-0.42f, y), V(0.42f, y));
        }
    }

    /// <summary>The stamp: an isometric tile with impact ticks (the cube presses it).</summary>
    public static void StampTile(InkPath p, Color ink, Color tile)
    {
        p.Clear().Poly(tile, ink, 2.2f, V(-0.66f, -0.5f), V(0f, -0.2f), V(0.66f, -0.5f), V(0f, -0.8f));
        p.Line(ink, 2f, V(-0.8f, -0.3f), V(-0.95f, -0.18f)).Line(ink, 2f, V(0.8f, -0.3f), V(0.95f, -0.18f));
        p.Line(ink, 2f, V(-0.34f, 0.72f), V(-0.34f, 0.92f)).Line(ink, 2f, V(0.34f, 0.72f), V(0.34f, 0.92f));
    }

    public static void Sparkles(InkPath p, Color c)
    {
        p.Clear();
        Vector2[] at = { V(-0.62f, 0.62f), V(0.66f, 0.5f), V(0.5f, -0.66f) };
        foreach (var a in at) { p.Line(c, 2.2f, a + V(-0.1f, 0f), a + V(0.1f, 0f)); p.Line(c, 2.2f, a + V(0f, -0.1f), a + V(0f, 0.1f)); }
    }

    /// <summary>A curved arrow pointing back (a reversed twin).</summary>
    public static void BackArrow(InkPath p, Color ink)
    {
        p.Clear().Arc(ink, 2.4f, V(0f, 0.1f), 0.6f, 20f, 170f, 12);
        ArrowHead(p, ink, 2.4f, V(-0.6f, 0.2f), V(-0.2f, -1f), 0.24f);
    }
}
