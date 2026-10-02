using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mouse interaction with the world: draw paths by click or drag across adjacent tiles, the hover candidate (outline) and
/// cube clicks (open the inspector), island / Moon dragging (IslandDrag), hub clicks, and keyboard shortcuts.
/// v3 (SPEC v3 §2.1-§2.2, §2.6): the candidate cube = the cube under the ray, else the top cube resting on the tile under the
/// ray, else (the ray hits no free tile) the finalized cube whose projected screen rect + 16 px holds the cursor; a free tile
/// under the ray always wins. A left click (press + release within 6 px) on the candidate opens CubeInspector; a drag that
/// starts on a cube does nothing; Alt/Option + press on a tile that carries a cube starts a new path there (stacking).
/// All pointer logic runs through <see cref="Step"/> so tests can drive it with synthetic screen points (<see cref="SimPointer"/>).
/// Fixes (v3 review): a platform hit inside the tile grid (a seam between raised tiles) is the nearest tile, so only the outer margin
/// and the hub are island handles, and while drawing the same island's margin does nothing (F1); a drag extends the path only when
/// the pointer moved (F5); while playing only the cube body picks a cube and hover ticks wait for the stop (F6); right-click deletes
/// only a cube body over land (F7); a HUD press, N or undo finish / trim the draft instead of dropping it and cancel an island drag (S2, S6).
/// v4 (SPEC v4 §4 R3-R4): the draft is a HOLOGRAM that plays live inside the focus loop of its column (every node gets the brush length,
/// <see cref="BrushTicks"/>); hovering a tile of the draft island auditions its note (at most 10 per second, once per entry) and previews
/// what a click adds (a dashed segment and ghost beads along the grid line); a click adds and solidifies those beads; [ ] . change the
/// latest length, Backspace / Cmd+Z take the last node back; finishing solidifies the cube (one History entry), Esc dissolves it.
/// Esc with nothing else to close and a click on empty sea dismiss the focus loop.
/// v5 (R, "I have no idea what the next note sounds like"): hover audition EVERYWHERE — a free tile the pointer enters plays its note with the
/// brush instrument (Moons: the kit piece) when idle too, once per entry, at most 8 per second, and always audibly (the synth's preview bus;
/// the song ducks under it while playing). An entry counts only when the pointer made it (a view gliding under a still pointer plays nothing);
/// idle, the pointer must also REST on the tile (<see cref="IdleDwellSeconds"/>, nearly still), so a sweep across islands to the HUD is silent.
/// Drafting: the pointer auditions the RUN a click would add (the grid line from the last node to the tile) as a quick arpeggio in order, each
/// tile flashing as its note plays, and the hologram peeks toward the tile; the draft's melody line (MelodyLine) draws the tune so far.
/// v6 (H, "players should be able to click the grid without placing a cube every time - they have to actively select the cube to go into placing
/// mode"): THE HAND (<see cref="Hand"/>). Empty: hovering auditions as in v5; a click on a tile PRESSES it (the note plays loud through
/// Synth.PressNote, the tile sinks and springs back, the island's header shows) and creates nothing; a press on a tile that becomes a drag moves the
/// island (IslandDrag takes over, as from its platform); Alt + drag plays every tile the pointer enters (a glissando on a keyboard); a click on a
/// cube opens the inspector. A CUBE in the hand (<see cref="PickUpCube"/>: the HUD chip, a digit): the hovered free tile shows a hologram cube (the
/// instrument's colour, the brush length's size) and a click draws exactly as v5; after finishing the cube stays in the hand (a tool). A PATTERN in
/// the hand (<see cref="PickUpPattern"/>: the clipboard chip, Cmd+V): the hovered grid shows the adapted pattern as ghost beads (Clipboard.AdaptedFor:
/// the exact notes a click places) and a click pastes it (Clipboard.PasteOn). Esc (when not drawing), a right-click on the empty sea (a pattern:
/// any right-click off a cube) or the held chip again puts the hand down. KEYBOARD islands (kind 2, one row of piano keys): a click adds exactly the
/// clicked key (melodies leap; no run), the last key again adds a repeated note, a drag adds each key it enters (a glissando run), finishing is
/// Enter, a right-click, a click off the island or the length row's done; hover / run auditions play single keys. Stamps paste ADAPTED
/// (Clipboard.Adapt). <see cref="AutoHand"/> (tests only) keeps the pre-v6 suites drawing by clicking tiles with an empty hand.
/// v7 (D; SPEC v7 §14.4, §15, §16.1, §5.3). SIZE FIRST ("when they're selecting the path of the cube, make them select the size of the cube THEN place
/// it down"): <see cref="BrushTicks"/> is the size of the NEXT node — the hologram at the cursor always shows it (grids, keyboards, the draft's next
/// tile, stairs, phrases: there a bar over its cells), U2's size row / the draft's length row / the wheel over a grid / [ ] . change it (auditioned)
/// and a click places exactly that size; a node already placed never changes (v4's [ ] resized the latest one). PHRASES (kind 4, a melody roll):
/// with a cube in the hand a press on a cell places a note of the brush length there, a drag paints on (a note per cell reached after the last
/// note's end; inside the newest note another row moves it), a right-click erases a note; the held group's cube on the phrase = its notes in time
/// order (<see cref="PhraseRoll"/>: gaps are rests); a note reaching into the last half measure grows the phrase (SongOps.GrowPhrase: "let it
/// autoexpand as they reach the limit"); the whole stroke is ONE History entry; the phrase loops while it is drawn. STAIRS: with a cube in the hand
/// a click on any step places the held group's RUNNER (the whole run; again replaces it); the empty hand presses steps. SHIFT: Shift + left-drag
/// draws an ink-dashed marquee selecting the grids it touches (GridSelection), Shift + click toggles a grid or selects a section (its plinth),
/// Esc / a click on the empty sea clears; the camera never pans meanwhile (<see cref="EditorState.Selecting"/>).
/// v7 §19 (the user: "add a cap on beats per path … if max is hit then it says capacity reached … when making notes at first, it can auto-expand
/// if needed or auto-unexpand" / "whenever the user is actively creating a cube path, it shouldnt be looping over and over unless they press a
/// key or press ui"): QUIET DRAWING — a path (a grid / keyboard draft, a phrase being drawn) never starts a loop by itself; if the song plays when
/// one begins it PAUSES; Space or the draft row's loop button start / stop the loop over the draft's grid (or phrase) in context
/// (<see cref="ToggleDrawLoop"/>); finishing plays nothing. CAPACITY — a path holds its grid's length in beats (<see cref="CapacityOf"/>); a note
/// that would pass it grows the grid (a chord grid / keyboard: K's live column resize; a phrase: SongOps.GrowPhrase) while its SECTION stays
/// within SectionBars measures (a phrase: up to its section's end), Backspace shrinks what that added (never below the grid's length when the
/// drawing began); past the limit the note is REFUSED (a thud, the grid shakes, "capacity reached" + an EXPAND chip on the draft row:
/// <see cref="ExpandDrawGrid"/>). The grid's beat pips show the draft's fill (KeyBlock.SetDraftFill). <see cref="AutoHand"/> (the pre-v6 suites)
/// keeps the earlier rules (the column loops while drawn, no cap).
/// v7 §20.1 ("for the piano keyboard, it should also be extensible if needed (like if a melody goes beyond an octave)"): while drawing on a KEYBOARD a
/// note on its lowest / highest key extends it by an octave that way, live (K's SongManager.ExtendKeysLive: the keys are kept and re-indexed, the
/// draft survives), so the melody can go on; finishing gives back the octaves the draft added that hold no notes, a dropped draft all of them.
/// v7 §21 (the user: "the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid"): nothing the hand
/// does flies a cube across grids any more — a stamped cube APPEARS on its target grid (W's Magic.Appear: a pop + sparkle there). Keys clicked
/// into a keyboard draft raise <see cref="OnDraftKey"/> (the keyboard's cat plays the notes being placed).
/// </summary>
public class PathManager : MonoBehaviour
{
    public static PathManager I;

    public enum EditorState { Idle, PlacingStart, DrawingPath, DraggingIsland, Recording, Selecting }   // v7: Selecting = a Shift press / marquee (the camera does not pan)
    public EditorState currentState = EditorState.Idle;
    public bool isSettingPath = false;     // legacy

    [Header("Instrument Palette")]
    public List<GameObject> cubePrefabs;
    public int selectedInstrument = 0;

    public List<TileInteraction> currentPathTiles = new List<TileInteraction>();
    public List<TileInteraction> allTiles;   // legacy
    public GameObject audioCube;             // legacy

    public AudioCube selectedCube;
    public bool selectedByClick;   // rests are only editable after the cube itself was clicked
    public static event Action<AudioCube> OnSelectionChanged;
    public static event Action<int> OnInstrumentChanged;
    /// <summary>Raised when a node marker (bead) of the click-selected cube is held 0.35 s (WP-D opens the sticker ring).</summary>
    public event Action<AudioCube, int> OnNodeHeld;
    /// <summary>Raised when a press-and-release lands on an island's or Moon's platform/hub with no tile or cube under the cursor.</summary>
    public event Action<KeyBlock> OnHubClicked;
    public bool StampArmed;
    public int StampShape = -1;          // -1 none, 0..3 shape stamps, 4 dice (M4)
    /// <summary>True when nothing (tile, cube, island) was under the cursor last frame and the pointer was not over the HUD.</summary>
    public bool HoverIsEmpty { get; private set; }
    /// <summary>The island or Moon whose platform/hub is under the cursor with no tile or cube in front of it, else null (set every frame).</summary>
    public KeyBlock hoverIsland;
    /// <summary>True when hoverIsland's hub (not its platform) is under the cursor.</summary>
    public bool hoverIsHub;
    /// <summary>The island / Moon drag state machine (SPEC §2.2).</summary>
    public IslandDrag Drag => islandDrag;

    // ---- v3 hover candidate (SPEC v3 §2.1)
    /// <summary>The cube the pointer is near (outlined): what a left click opens and a right click deletes. Null while drawing,
    /// over the HUD, while the world is locked or during an island drag.</summary>
    public AudioCube Candidate => candidate;
    /// <summary>The free tile under the pointer last frame (its hover highlight shows), else null.</summary>
    public TileInteraction HoverTile => hoverTile;
    /// <summary>Screen-rect slack of the hover rule (px).</summary>
    public const float NearPx = 16f;

    /// <summary>What a screen point picks (SPEC v3 §2.1). rule: 0 nothing / island, 1 cube under the ray, 2 top cube on the tile
    /// under the ray, 3 screen rect + 16 px, 4 free tile (or any tile while drawing), 5 Alt: a tile to stack a new path on.
    /// seam: the tile came from a platform hit inside the tile grid (F1); overSea: nothing of the world is under the ray (F7).</summary>
    public struct PickResult { public TileInteraction tile; public AudioCube cube; public KeyBlock island; public bool onHub; public int rule; public bool seam, overSea; }

    /// <summary>Tests: while set, the real mouse is ignored and every frame uses an idle pointer at <see cref="SimPos"/>.</summary>
    public static bool SimOnly;
    public static Vector3 SimPos;
    public static bool SimAlt;

    struct PointerIn { public Vector3 pos; public bool down, held, up, rightUp, overUI, alt, shift; }

    AudioCube activeCube;
    TileInteraction hoverTile; AudioCube hoverCube; AudioCube candidate;
    bool dragging; Transform cubesRoot; Camera cam;
    bool deselectArmed; Vector3 deselectPx;   // a press on empty sea deselects on release, unless it became a camera pan
    IslandDrag islandDrag;
    bool strokeExtended;   // this press-and-drag added at least one tile: only such a stroke finishes the path on release (SPEC v3 §2.6)
    Vector3 extendPx;      // F5: the pointer at the last press / extension: a drag extends only after the pointer itself moved (not the camera)
    AudioCube pressCube; Vector3 pressCubePx;   // a left press on the candidate: opens the inspector on release within 6 px
    float lastHoverTick = -9f;
    // bead hold (rest toggle on release, sticker ring after 0.35 s) — v2; with v3 a selected cube is always inspected (world locked)
    AudioCube holdCube; TileInteraction holdTile; int holdNode = -1; float holdStart; Vector3 holdPx; bool holdFired;
    readonly RaycastHit[] hits = new RaycastHit[64];
    static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
    static readonly Vector3[] corners = new Vector3[8];

    public bool IsDrawing => currentState == EditorState.DrawingPath;

    // ------------------------------------------------------------------ v4 draft contract (SPEC v4 §2.4 / §4 R3)
    /// <summary>The cube being drawn (a hologram until finished), null when not drawing.</summary>
    public AudioCube Draft => IsDrawing ? activeCube : null;
    /// <summary>Tests: points of the draft's dashed preview line (a sphere's arc has 15) and its highest point, 0 when hidden.</summary>
    public int DraftPreviewPoints => previewLine != null && previewLine.enabled ? previewLine.positionCount : 0;
    public float DraftPreviewTop { get { float y = 0f; if (previewLine != null && previewLine.enabled) for (int i = 0; i < previewLine.positionCount; i++) y = Mathf.Max(y, previewLine.GetPosition(i).y); return y; } }
    // ------------------------------------------------------------------ v7 size first (SPEC v7 §15; package D)
    int brushTicks = 24;
    /// <summary>v7 (§15, "when they're selecting the path of the cube, make them select the size of the cube THEN place it down"): the size of the
    /// NEXT node (ticks; 24 = one beat), chosen BEFORE placing — the hologram at the cursor shows it and a click places a node of exactly this length
    /// (grids, keyboards, phrases; a stairs runner runs at the rate nearest to it, <see cref="RunnerRate"/>). U2's size row, the draft's length row,
    /// the wheel over a grid (a cube in the hand) and [ ] . set it; it never changes a node already placed (select its bead: the inspector's length
    /// row). Setting it = <see cref="SetBrush"/>.</summary>
    public int BrushTicks { get { return brushTicks; } set { SetBrush(value); } }
    /// <summary>v7: the brush changed (U2's size row polls <see cref="BrushTicks"/>; the event is for anyone who would rather listen).</summary>
    public static event Action OnBrushChanged;
    /// <summary>v7: brush changes and brush auditions so far (tests).</summary>
    public static int BrushChanges, BrushAuditions;
    /// <summary>v7: the brush's size, 0 (very short: a 16th) .. 4 (very long: a whole note); a length that is none of the ten reads as the nearest.</summary>
    public int BrushSize { get { int s; bool d; DecodeBrush(brushTicks, out s, out d); return s; } }
    /// <summary>v7: the brush is dotted (x 1.5).</summary>
    public bool BrushDotted { get { int s; bool d; DecodeBrush(brushTicks, out s, out d); return d; } }
    static void DecodeBrush(int ticks, out int size, out bool dotted)
    {
        if (!AudioCube.TryDecode(ticks, out size, out dotted)) AudioCube.TryDecode(AudioCube.SnapTicks(ticks), out size, out dotted);
    }

    /// <summary>v7: sets the size of the NEXT node (ticks, clamped 3..192); nothing already placed changes. The hologram and the draft's ghost beads
    /// take the new size at once. <paramref name="audition"/>: plays the new length (<see cref="AuditionBrush"/>). Raises <see cref="OnBrushChanged"/>
    /// when it changed. No History (the brush is a tool, not the song).</summary>
    public void SetBrush(int ticks, bool audition = false)
    {
        ticks = AudioCube.ClampTicks(ticks);
        if (ticks != brushTicks)
        {
            brushTicks = ticks;
            previewTo = null;   // the draft's ghost beads take the new size
            BrushChanges++;
            OnBrushChanged?.Invoke();
        }
        if (audition) AuditionBrush();
    }

    /// <summary>v7: one size longer (<paramref name="dir"/> &gt; 0) or shorter (&lt; 0), dotted kept — ] [ and the wheel; auditions the new length
    /// (at the shortest / longest a soft tick instead).</summary>
    public void StepBrush(int dir)
    {
        if (dir == 0) return;
        int size; bool dotted; DecodeBrush(brushTicks, out size, out dotted);
        int ns = Mathf.Clamp(size + (dir > 0 ? 1 : -1), 0, AudioCube.BaseTicks.Length - 1);
        int t = AudioCube.TicksOf(ns, dotted);
        if (t == brushTicks) { AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 0.6f); return; }
        SetBrush(t, true);
    }

    /// <summary>v7: dotted on / off (x 1.5) — the . key; auditions the new length.</summary>
    public void ToggleBrushDotted()
    {
        int size; bool dotted; DecodeBrush(brushTicks, out size, out dotted);
        SetBrush(AudioCube.TicksOf(size, !dotted), true);
    }

    /// <summary>v7: plays the brush's length once — the tiny audition of a size change: on the hovered tile, else the draft's last node, else the tile
    /// the hologram stands on, else the lit chord's root in the held group's register. False when nothing sounded.</summary>
    public bool AuditionBrush() => AuditionTicks(brushTicks);

    /// <summary>v7: plays a length of <paramref name="ticks"/> once (the size row's hover): the hovered tile, else the draft's last node, else the
    /// hologram's tile, else the lit chord's root in the held group's register.</summary>
    public bool AuditionTicks(int ticks)
    {
        var t = hoverTile != null ? hoverTile : (IsDrawing ? Last : HandGhostTile);
        if (t != null) { bool ok = AuditionLength(ticks, t); if (ok) BrushAuditions++; return ok; }
        if (!Synth.Ready) return false;
        int inst = Mathf.Clamp(activeCube != null ? activeCube.instrument : selectedInstrument, 0, Instruments.Count - 1);
        int slot = Instruments.SlotOf(inst, Instruments.BrushVoiceOf(inst));
        var sm = SongManager.I;
        int root = 60;
        if (sm != null && sm.HasSong && sm.Islands.Count > 0) { var kb = sm.Islands[Mathf.Clamp(sm.LitIsland, 0, sm.Islands.Count - 1)]; if (kb != null) root = kb.chordRootMIDI; }
        int midi = SynthBank.Def(slot).drums ? 36 : SynthBank.ClampToRegister(slot, root + SongManager.Transpose);
        float sec = AuditionSeconds(ticks);
        if (!GlobalClock.IsPlaying) Synth.Preview(slot, midi, 80, sec);
        else { double at, end; VoiceRules.PreviewBeat(out at, out end); Synth.Audition(slot, midi, 86, at, at + sec); }
        BrushAuditions++;
        return true;
    }

    /// <summary>v4: sets the brush AND, while drawing, the latest draft node's length (the hologram grows / shrinks to it at once). v7 (§15): the
    /// player's controls no longer resize a node already placed — the size row, the draft's length row, the wheel and [ ] . set only the brush
    /// (<see cref="SetBrush"/>: the NEXT node); this stays for scripts and the older checks. No History (the draft is not in the song until it
    /// is finished); no audition (the caller does: <see cref="AuditionLength"/>).</summary>
    public void SetDraftDuration(int ticks)
    {
        SetBrush(ticks);
        if (!IsDrawing || activeCube == null || activeCube.nodes.Count == 0) return;
        activeCube.SetDuration(activeCube.nodes.Count - 1, brushTicks);
        activeCube.HoldSize(AudioCube.SizeOf(brushTicks / (float)ProjectConfig.TicksPerBeat), 0.9f);
        previewTo = null;   // the ghost beads take the new size
        RaiseDraft(1, activeCube);
    }
    /// <summary>The tile the pointer auditions while drawing (a tile of the draft island other than the last node; null none).</summary>
    public TileInteraction AuditionTile => IsDrawing ? hoverAud : null;
    /// <summary>Takes the draft's last node back (Backspace, Cmd+Z); the last one left cancels the draft. No History entry.</summary>
    public void RemoveLastNode()
    {
        if (!IsDrawing) return;
        if (currentPathTiles.Count <= 1 || activeCube == null) { CancelPath(); return; }
        currentPathTiles.RemoveAt(currentPathTiles.Count - 1);
        var d = DraftDurs();
        while (d.Count > currentPathTiles.Count) d.RemoveAt(d.Count - 1);
        activeCube.SetPathAndDurations(currentPathTiles, d);
        if (!GlobalClock.IsPlaying) activeCube.PreviewHopTo(Last);
        UpdateHighlights();
        previewTo = null;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f);
        AutoUnexpand();   // v7 §19.1: the grid gives back what auto-expand added
        RaiseDraft(1, activeCube);
    }
    /// <summary>The pointer is over an island's platform margin (not a tile, cube or hub): the island's move handle.</summary>
    public bool HoverGround => hoverIsland != null && hoverTile == null && candidate == null && !hoverIsHub;
    /// <summary>OnDraftStarted: a draft began. OnDraftChanged: a node was added / taken back or a length changed. OnDraftFinished(cube): the
    /// draft was finished (solidified; one History entry) — or cancelled / dissolved, then with null.</summary>
    public event Action OnDraftStarted, OnDraftChanged;
    public event Action<AudioCube> OnDraftFinished;
    void RaiseDraft(int kind, AudioCube c) { if (kind == 0) OnDraftStarted?.Invoke(); else if (kind == 1) OnDraftChanged?.Invoke(); else OnDraftFinished?.Invoke(c); }

    /// <summary>Plays the latest draft node's note (else the hovered or given tile) for <paramref name="ticks"/> — the note-length audition
    /// of the length keys (the draft row has its own). False when nothing sounded.</summary>
    public bool AuditionLength(int ticks, TileInteraction tile = null)
    {
        var t = tile != null ? tile : (IsDrawing ? Last : hoverTile);
        if (t == null) return false;
        int inst = activeCube != null ? activeCube.instrument : selectedInstrument;
        int oct = activeCube != null ? activeCube.octave : 0;
        return t.Audition(inst, oct, AuditionSeconds(ticks), 0.55f);
    }
    /// <summary>Seconds a length audition sounds: 0.92 of the note at the current tempo (0.08 .. 2.5 s).</summary>
    public static float AuditionSeconds(int ticks) => Mathf.Clamp((float)(0.92 * ticks / ProjectConfig.TicksPerBeat / GlobalClock.BeatsPerSecond), 0.08f, 2.5f);

    /// <summary>Hover auditions played so far (tests).</summary>
    public static int AuditionCount;
    /// <summary>The tiles a click on the hovered tile would add (the ghost beads; empty when none is shown).</summary>
    public IReadOnlyList<TileInteraction> PreviewTiles => previewTiles;

    List<int> DraftDurs()
    {
        var d = activeCube != null && activeCube.HasDurations ? new List<int>(activeCube.durs) : new List<int>();
        while (d.Count < currentPathTiles.Count) d.Add(BrushTicks);
        return d;
    }

    // ---- v4 draft hover: the hologram preview of what a click adds (a dashed segment + ghost beads along the grid line); v5: the audition moved
    // to UpdateHoverAudition (idle and drafting)
    readonly List<TileInteraction> previewTiles = new List<TileInteraction>();
    readonly List<TileInteraction> lineScratch = new List<TileInteraction>();
    TileInteraction previewFrom, previewTo; int previewInst = -1;
    LineRenderer previewLine; Material previewLineMat, previewBeadMat;
    readonly List<Transform> previewBeads = new List<Transform>();
    static readonly int PreviewColorId = Shader.PropertyToID("_Color"), PreviewIntensityId = Shader.PropertyToID("_Intensity");

    void UpdateDraftHover(TileInteraction tile, bool stroking)
    {
        var last = Last;
        bool valid = IsDrawing && activeCube != null && tile != null && last != null && tile.island == last.island && (tile != last || Keys(tile.island)) && !stroking;
        if (!valid) { HideDraftPreview(); if (activeCube != null) activeCube.Peek(null); return; }
        if (tile != previewTo || last != previewFrom || activeCube.instrument != previewInst) BuildDraftPreview(last, tile);
        AnimateDraftPreview();
        activeCube.Peek(tile);   // v5: the hologram leans / hops toward the tile a click goes to
    }

    // ---- v5 R: hover audition everywhere — idle (the brush instrument) and drafting (the RUN a click adds, as a quick arpeggio)
    /// <summary>Minimum spacing of hover auditions (s): at most 8 per second; a fast sweep plays where it rests.</summary>
    public const float AuditionGap = 0.125f;
    /// <summary>v5: an IDLE hover plays only once the pointer has rested on its tile this long (s), nearly still: a sweep across islands (to
    /// reach the HUD, say) stays silent, a pause on a tile to consider it plays it. Drafting auditions stay immediate (the player is choosing
    /// the next note: once per entry, <see cref="AuditionGap"/>).</summary>
    public const float IdleDwellSeconds = 0.1f;
    /// <summary>Below this screen speed (px/s at 96 dpi, DPI-scaled like OrbitCamera.DragThresholdPx) the pointer counts as resting.</summary>
    public const float RestSpeedPx = 300f;
    /// <summary>DSP time at which the last hover audition was triggered (tests).</summary>
    public double LastAuditionDsp { get; private set; }
    /// <summary>How long the pointer had rested on its tile when the last idle audition played (s; tests).</summary>
    public float LastIdleDwell { get; private set; }
    /// <summary>Seconds an idle hover audition sounds.</summary>
    public const float IdleAuditionSeconds = 0.42f;
    /// <summary>The tile whose entry the pointer auditions (idle: a free tile of any island; drafting: a tile of the draft island), null none.</summary>
    public TileInteraction HoverAuditionTile => hoverAud;
    /// <summary>The tiles the last hover audition played, in order (idle: one; drafting: the run a click on the target adds).</summary>
    public IReadOnlyList<TileInteraction> LastAuditionTiles => lastAudTiles;
    /// <summary>The onset DSP time of each note of the last hover audition, in order (0 = at once).</summary>
    public IReadOnlyList<double> LastAuditionOnsets => lastAudOnsets;
    /// <summary>Hover auditions while not drafting; run auditions (two notes or more) while drafting (tests).</summary>
    public static int IdleAuditionCount, RunAuditionCount;

    TileInteraction hoverAud; bool hoverAudPending; float lastAuditionT = -9f;
    Vector3 lastPointerPx = new Vector3(float.NaN, 0f, 0f); float lastPointerMoveT = -9f;
    float restStartT = -9f, lastStepT = -9f, hoverEntryT = -9f;
    readonly List<TileInteraction> lastAudTiles = new List<TileInteraction>();
    readonly List<double> lastAudOnsets = new List<double>();
    readonly List<TileInteraction> runScratch = new List<TileInteraction>();
    struct RunFlash { public TileInteraction tile; public double dsp; public Color color; }
    readonly RunFlash[] runFlashes = new RunFlash[16]; int runFlashCount;

    static bool CameraDragging => OrbitCamera.I != null && (OrbitCamera.I.Panning || OrbitCamera.I.Orbiting);

    /// <summary>Once per entry of a tile the pointer made (not the camera), at most every <see cref="AuditionGap"/> s: idle — a free tile of any
    /// island plays its note with the brush (Moons: the kit piece) once the pointer has rested on it <see cref="IdleDwellSeconds"/>; drafting —
    /// a tile of the draft island plays the run a click would add, at once. Nothing over the HUD, while a button is held (a stroke, a pan, a
    /// drag) or while the camera is dragged; the world locks (menu, presentation, prompt, inspector, the deck's ghost) and island drags never
    /// reach here.</summary>
    void UpdateHoverAudition(PointerIn p, PickResult r)
    {
        float now = Time.unscaledTime;
        float dt = now - lastStepT; lastStepT = now;
        float dist = float.IsNaN(lastPointerPx.x) ? 0f : (p.pos - lastPointerPx).magnitude;
        bool moved = float.IsNaN(lastPointerPx.x) || dist >= 1.5f;
        bool fast = dist >= 1.5f && dist / Mathf.Max(dt, 1e-3f) > RestSpeedPx * OrbitCamera.DpiScale;
        lastPointerPx = p.pos;
        bool dragged = p.held || p.up || CameraDragging;
        if (dragged) { lastPointerMoveT = -9f; restStartT = now; }   // a drag's own motion does not arm the tile it ends on
        else { if (moved) lastPointerMoveT = now; if (fast) restStartT = now; }
        TileInteraction want = null;
        var tile = p.overUI ? null : r.tile;
        if (IsDrawing)
        {
            var last = Last;
            if (activeCube != null && tile != null && last != null && tile.island == last.island && (tile != last || Keys(tile.island)) && !(p.held && dragging)) want = tile;
        }
        else if (tile != null && tile.island != null && r.rule == 4 && !dragged && Hand != HandKind.Pattern) want = tile;   // v6: a held pattern shows its ghost, silent
        if (want != hoverAud)
        {
            hoverAud = want;
            hoverEntryT = now;
            hoverAudPending = want != null && now - lastPointerMoveT <= 0.25f;   // an entry the pointer made; a view gliding under a still pointer plays nothing
        }
        if (!hoverAudPending || hoverAud == null || now - lastAuditionT < AuditionGap) return;
        if (!IsDrawing)
        {
            float rest = now - Mathf.Max(hoverEntryT, restStartT);
            if (rest < IdleDwellSeconds) return;   // idle: only once the pointer rests on the tile
            LastIdleDwell = rest;
        }
        hoverAudPending = false;
        lastAuditionT = now;
        LastAuditionDsp = GlobalClock.DspNow;
        AuditionCount++;
        PlayHoverAudition(hoverAud);
    }

    void PlayHoverAudition(TileInteraction t)
    {
        // a run still arpeggiating from the previous entry is cut (its later notes would blur the new one)
        int k = lastAudOnsets.Count;
        if (k > 1 && lastAudOnsets[k - 1] > GlobalClock.DspNow) Synth.CancelOwner(Synth.PreviewOwner);
        runFlashCount = 0;
        lastAudTiles.Clear();
        if (IsDrawing && activeCube != null)
        {
            if (Leaps(t)) { runScratch.Clear(); runScratch.Add(t); }   // v6: a keyboard (SPHERE: a ball) auditions the single tile a click adds
            else LineTiles(Last, t, runScratch);
            if (runScratch.Count == 0) runScratch.Add(t);
            lastAudTiles.AddRange(runScratch);
            TileInteraction.AuditionRun(runScratch, activeCube.instrument, activeCube.octave, Mathf.Clamp(AuditionSeconds(BrushTicks), 0.3f, 1.2f), lastAudOnsets, activeCube.voice);
            if (runScratch.Count >= 2) RunAuditionCount++;
            Color c = activeCube.Color;
            for (int i = 0; i < runScratch.Count && i < lastAudOnsets.Count && runFlashCount < runFlashes.Length; i++)
                runFlashes[runFlashCount++] = new RunFlash { tile = runScratch[i], dsp = lastAudOnsets[i], color = c };
            FireRunFlashes();
            return;
        }
        int inst = t.island != null && t.island.IsMoon ? DrumsInstrument : selectedInstrument;
        lastAudTiles.Add(t);
        lastAudOnsets.Clear();
        // v7: a phrase cell hovered with a cube in the hand sounds at the brush's length (the note a click places)
        float sec = Hand == HandKind.Cube && t.island != null && t.island.IsPhrase ? Mathf.Clamp(AuditionSeconds(brushTicks), 0.15f, 1.2f) : IdleAuditionSeconds;
        if (Synth.Ready)
        {
            lastAudOnsets.Add(t.Sound(inst, 0, sec, TileInteraction.HoverVolume));
            t.Flash(Instruments.Colors[Mathf.Clamp(inst, 0, Instruments.Count - 1)], 0.35f);
            t.Press(0.2f);
        }
        else { t.Audition(inst, 0, sec, 0.35f, TileInteraction.HoverVolume); lastAudOnsets.Add(0.0); }
        IdleAuditionCount++;
    }

    /// <summary>The run's tiles flash as their notes play (DSP-timed; the first note of a playing audition waits for its 16th).</summary>
    void FireRunFlashes()
    {
        if (runFlashCount == 0) return;
        double now = GlobalClock.DspNow;
        int w = 0;
        for (int i = 0; i < runFlashCount; i++)
        {
            var f = runFlashes[i];
            if (f.tile == null) continue;
            if (f.dsp <= 0.0 || now >= f.dsp - 0.01) { f.tile.Flash(f.color, 0.5f); f.tile.Press(0.25f); continue; }
            runFlashes[w++] = f;
        }
        runFlashCount = w;
    }

    void ResetHoverAudition() { hoverAud = null; hoverAudPending = false; runFlashCount = 0; }

    /// <summary>The tiles on the straight / diagonal grid line from <paramref name="from"/> to <paramref name="to"/> (excluding from).</summary>
    static void LineTiles(TileInteraction from, TileInteraction to, List<TileInteraction> into)
    {
        into.Clear();
        if (from == null || to == null || from.island != to.island || from.island == null) return;
        var isl = from.island;
        int x = from.gridX, z = from.gridZ, guard = 0;
        while ((x != to.gridX || z != to.gridZ) && guard++ < 64)
        {
            x += Math.Sign(to.gridX - x); z += Math.Sign(to.gridZ - z);
            var t = isl.GetTile(x, z);
            if (t == null) break;
            if (t == from) continue;
            into.Add(t);
        }
    }

    void BuildDraftPreview(TileInteraction from, TileInteraction to)
    {
        previewFrom = from; previewTo = to; previewInst = activeCube.instrument;
        if (Leaps(to)) { previewTiles.Clear(); previewTiles.Add(to); }   // v6: a keyboard click adds exactly the hovered key (a repeat when it is the last); SPHERE: the leap's tile
        else LineTiles(from, to, previewTiles);
        if (previewLine == null)
        {
            var go = new GameObject("DraftPreviewLine");
            previewLine = go.AddComponent<LineRenderer>();
            previewLine.useWorldSpace = true; previewLine.widthMultiplier = 0.075f;
            previewLine.numCapVertices = 3; previewLine.textureMode = LineTextureMode.Tile; previewLine.alignment = LineAlignment.View;
            previewLineMat = Fx.Flow(Color.white, 1.1f, 4.5f, 1.2f, 0f);   // base 0: gaps between the dashes
            previewLineMat.SetFloat("_SrcBlend", 1f); previewLineMat.SetFloat("_DstBlend", 10f);   // alpha-blended dashes (the Flow output is premultiplied)
            previewLine.material = previewLineMat;
            previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; previewLine.receiveShadows = false;
            previewBeadMat = Fx.Alpha(IconFactory.GetTexture("ring"), Color.white);
        }
        Color c = activeCube.Color;
        previewColor = c;
        previewLineMat.SetColor(PreviewColorId, c);
        previewBeadMat.SetColor(PreviewColorId, c);
        if (activeCube.sphere && !Keys(to.island) && to != from)
        {
            // SPHERE: the dashes trace the bounce it will make (a parabola as high as the real one)
            const int n = 14;
            float bh = AudioCube.BounceHeight(AudioCube.GridGap(from, to));
            previewLine.positionCount = n + 1;
            for (int i = 0; i <= n; i++) { float u = i / (float)n; previewLine.SetPosition(i, Vector3.Lerp(from.Top, to.Top, u) + Vector3.up * (0.07f + 4f * u * (1f - u) * bh)); }
        }
        else
        {
            previewLine.positionCount = previewTiles.Count + 1;
            previewLine.SetPosition(0, from.Top + Vector3.up * 0.07f);
            for (int i = 0; i < previewTiles.Count; i++) previewLine.SetPosition(i + 1, previewTiles[i].Top + Vector3.up * 0.07f);
        }
        previewLine.enabled = true;
        float bs = 0.34f * AudioCube.SizeOf(BrushTicks / (float)ProjectConfig.TicksPerBeat) * AudioCube.FootOf(to);
        while (previewBeads.Count < previewTiles.Count)
        {
            var b = new GameObject("DraftGhostBead");
            b.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var mr = b.AddComponent<MeshRenderer>();
            mr.sharedMaterial = previewBeadMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            previewBeads.Add(b.transform);
        }
        for (int i = 0; i < previewBeads.Count; i++)
        {
            bool on = i < previewTiles.Count;
            if (previewBeads[i].gameObject.activeSelf != on) previewBeads[i].gameObject.SetActive(on);
            if (!on) continue;
            previewBeads[i].position = PreviewBeadPos(previewTiles[i]);
            previewBeads[i].localScale = new Vector3(bs, 1f, bs);
        }
    }

    /// <summary>v6: where a ghost bead of the draft preview sits: the tile's top, or — a keyboard key the draft already visits (a repeat) — one
    /// repeat step toward the key's front, where the new bead will stand.</summary>
    Vector3 PreviewBeadPos(TileInteraction t)
    {
        if (t == null) return Vector3.zero;
        if (Keys(t.island) && currentPathTiles.Contains(t))
        {
            int total = 0; foreach (var x in currentPathTiles) if (x == t) total++;
            return t.Top + new Vector3(0f, 0.05f, ((total) * 0.5f - total) * AudioCube.RepeatStep);
        }
        return t.Top + Vector3.up * 0.05f;
    }

    // the ghost flickers on twos like the hologram
    Color previewColor = Color.white;
    void AnimateDraftPreview()
    {
        if (previewBeadMat == null) return;
        // v5 (K's repeat belt): while the draft island rides its belt, the dashed segment and the ghost beads ride with it
        if (previewFrom != null && previewFrom.island != null && previewFrom.island.Riding && previewLine != null && previewLine.positionCount == previewTiles.Count + 1)
        {
            previewLine.SetPosition(0, previewFrom.Top + Vector3.up * 0.07f);
            for (int i = 0; i < previewTiles.Count; i++)
            {
                var t = previewTiles[i];
                if (t == null) continue;
                previewLine.SetPosition(i + 1, t.Top + Vector3.up * 0.07f);
                if (i < previewBeads.Count) previewBeads[i].position = PreviewBeadPos(t);
            }
        }
        float h = Rhythm.Hash01(Mathf.FloorToInt(Time.time * 12f), 5, 11);
        previewBeadMat.SetColor(PreviewColorId, Palette.A(previewColor, 0.6f + 0.35f * h));
        previewLineMat.SetFloat(PreviewIntensityId, 0.85f + 0.3f * h);
    }

    void HideDraftPreview()
    {
        previewTiles.Clear(); previewFrom = null; previewTo = null;
        if (previewLine != null && previewLine.enabled) { previewLine.enabled = false; previewLine.positionCount = 0; }
        for (int i = 0; i < previewBeads.Count; i++) if (previewBeads[i].gameObject.activeSelf) previewBeads[i].gameObject.SetActive(false);
    }

    // ---- v4 R4: Esc dismisses the focus loop only when nothing else took the key (decided in LateUpdate, after every Update ran)
    int escDismissFrame = -10; bool escBusyLast;
    static bool EscBusyNow()
    {
        var ui = UIManager.I;
        return IslandTray.IsOpen || CubeInspector.IsOpen || PathGridView.AnyPopoverOpen || Presenter.Active || MainMenu.IsShown
            || (ui != null && (ui.WheelOpen || ui.OpenRing != null)) || (InterfaceController.I != null && InterfaceController.I.Visible);
    }

    void LateUpdate()
    {
        bool busy = EscBusyNow();
        // v7: an Esc no other owner took clears the grid selection (claimed; decided here so UIManager saw the selection this frame)
        if (escSelFrame == Time.frameCount && !busy && !escBusyLast && !FocusLoop.EscClaimedRecently) { GridSelection.Clear(); FocusLoop.ClaimEsc(); }
        // v6: an Esc no other owner took puts the hand down first (claimed: the same press does not also end the focus loop)
        if (escHandFrame == Time.frameCount && !busy && !escBusyLast && !FocusLoop.EscClaimedRecently) { PutDown(); FocusLoop.ClaimEsc(); }
        if (escDismissFrame == Time.frameCount && !busy && !escBusyLast && !FocusLoop.EscClaimedRecently) FocusLoop.Dismiss();
        escBusyLast = busy || IsDrawing || (islandDrag != null && islandDrag.Busy);
    }
    int escHandFrame = -10, escSelFrame = -10;

    /// <summary>U2 (requests_U2.md): true while the pointer is over a HUD control that belongs to the draft besides the length row
    /// (<c>DurationPicker.PointerOver</c>, read directly): a press there does not finish the draft like other HUD presses (S2).</summary>
    public static bool DraftUiHover;

    static bool DraftRowUnderPointer() => DraftUiHover || DurationPicker.PointerOver;
    TileInteraction Last => currentPathTiles.Count > 0 ? currentPathTiles[currentPathTiles.Count - 1] : null;
    static int DrumsInstrument { get { for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) return i; return Instruments.Count - 1; } }
    static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SimOnly = false; SimAlt = false; AuditionCount = 0; IdleAuditionCount = 0; RunAuditionCount = 0; DraftUiHover = false;
        AutoHand = false; PressCount = 0; GlissCount = 0; DragHandovers = 0; KeyAdds = 0; OnHandChanged = null; OnTilePressed = null;
        OnBrushChanged = null; BrushChanges = 0; BrushAuditions = 0;   // v7
        SimShift = false; OwnsWheel = false; WheelSteps = 0; PhraseNotesPlaced = 0; PhraseGrows = 0; PhraseStrokes = 0; PhraseErases = 0;
        RunnersPlaced = 0; MarqueeCount = 0; ShiftClicks = 0;
        CapacityRefusals = 0; AutoExpands = 0; AutoUnexpands = 0; DrawLoopToggles = 0; Expands = 0; KeyExtends = 0; KeyShrinks = 0;
        Appears = 0; OnDraftKey = null; DraftKeyEvents = 0;   // v7 §21, C
    }

    void Awake()
    {
        I = this;
        Instruments.LoadFrom(cubePrefabs);
        cubesRoot = new GameObject("Cubes").transform;
        islandDrag = new IslandDrag(this);
    }

    // v4 (integration): a new song or a load (History reset) starts the note-length brush over at a quarter note
    void OnEnable() { History.OnChanged += HandleHistory; Clipboard.OnChanged += HandleClipboard; }
    void OnDisable() { History.OnChanged -= HandleHistory; Clipboard.OnChanged -= HandleClipboard; }
    // v6: a cleared clipboard empties a hand that held its pattern; a new copy rebuilds the ghost
    void HandleClipboard() { patternKey = int.MinValue; if (Hand == HandKind.Pattern && !Clipboard.HasPattern) PutDown(); }
    void HandleHistory() { historyEvents++; if (History.UndoCount == 0 && !IsDrawing) SetBrush(24); }
    int historyEvents;   // v7: History changes seen (PlaceRunner: did SongOps.SetStair push?)

    void Update()
    {
        FocusLoop.Tick();
        if (drawLoop && !FocusLoop.Active) drawLoop = false;   // v7: the draw loop went (dismissed elsewhere)
        FireRunFlashes();
        UpdatePendingShow();
        if (pendingFit != null) { var f = pendingFit; pendingFit = null; if (f.isFinalized && !IsDrawing) FitColumnToCube(f); }
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        PointerIn p;
        if (SimOnly) p = new PointerIn { pos = SimPos, alt = SimAlt, shift = SimShift };
        else p = new PointerIn
        {
            pos = Input.mousePosition, down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0),
            rightUp = Input.GetMouseButtonUp(1), overUI = InputUtil.PointerOverUI, alt = AltHeld, shift = InputUtil.Shift
        };
        Step(p, true);
    }

    /// <summary>Tests: runs one pointer step at a synthetic screen point exactly as a real mouse frame would (no hotkeys, no
    /// island drag — those read the real input); <paramref name="overUI"/> = the point is over the HUD; v7 <paramref name="shift"/> = Shift held
    /// (<see cref="SimShift"/> also counts).</summary>
    public void SimPointer(Vector3 screen, bool down, bool held, bool up, bool rightUp = false, bool alt = false, bool overUI = false, bool shift = false)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Step(new PointerIn { pos = screen, down = down, held = held, up = up, rightUp = rightUp, alt = alt, overUI = overUI, shift = shift || SimShift }, false);
    }

    void Step(PointerIn p, bool frame)
    {
        if (WorldInput.WorldLocked)
        {
            // the menu, the presentation, the prompt or the inspector own the pointer (SPEC v3 §1)
            if (IsDrawing) FinishPath();
            if (strokeIsland >= 0) EndPhraseStroke();   // v7: a phrase stroke ends (its one History entry)
            CancelShift(); OwnsWheel = false;          // v7: a marquee in progress is dropped; the wheel is the camera's
            if (islandDrag.Busy) islandDrag.Cancel();
            SetHover(null, null); SetCandidate(null);
            hoverIsland = null; hoverIsHub = false; HoverIsEmpty = false;
            dragging = false; strokeExtended = false; deselectArmed = false; holdCube = null; holdTile = null; pressCube = null;
            hoverAud = null; hoverAudPending = false; lastPointerMoveT = -9f; lastPointerPx = p.pos;   // v5: motion under a lock arms nothing
            armKind = ArmKind.None; HideHandGhost(); HidePatternGhost();                               // v6: nothing of the hand shows under a lock
            if (Hand != HandKind.Empty && (MainMenu.IsShown || Presenter.Active)) PutDown();          // the menu / the presentation take the hand
            if (frame && !WorldInput.KeysLocked) HandleHotkeys();   // the inspector keeps the slot keys, Delete and undo live
            return;
        }
        if (frame && !WorldInput.KeysLocked) HandleHotkeys();
        if (frame) UpdateFill();   // v7 §19.1: the grid's pips show the draft's fill

        // an island drag (or its push-out slide) owns the pointer: no picking, hover or tile clicks meanwhile (SPEC §2.2)
        if (frame) islandDrag.Update(cam);
        if (islandDrag.Busy)
        {
            if (strokeIsland >= 0) EndPhraseStroke();
            CancelShift(); OwnsWheel = false;
            SetHover(null, null); SetCandidate(null);
            hoverIsland = null; hoverIsHub = false; HoverIsEmpty = false;
            currentState = EditorState.DraggingIsland;
            hoverAud = null; hoverAudPending = false; lastPointerMoveT = -9f; lastPointerPx = p.pos;
            armKind = ArmKind.None; HideHandGhost(); HidePatternGhost();
            return;
        }
        if (currentState == EditorState.DraggingIsland) currentState = EditorState.Idle;

        var r = p.overUI ? new PickResult() : PickAt(p.pos, p.alt);
        SetHover(r.tile, r.cube);
        SetCandidate(r.cube);
        hoverIsland = r.island; hoverIsHub = r.onHub;
        HoverIsEmpty = !p.overUI && r.tile == null && r.cube == null && r.island == null;

        if (p.down && p.overUI && IsDrawing && !DraftRowUnderPointer()) FinishPath();   // S2: a HUD press ends a click-drawn draft first (not the draft's own length row)
        if (p.down && !p.overUI)
        {
            if (p.shift && !IsDrawing && strokeIsland < 0) BeginShift(r, p);   // v7 (§16.1): Shift + press selects grids (a click toggles, a drag draws the marquee)
            else OnPress(r, p);
        }
        UpdateShift(p, r);                                                 // v7: the marquee follows the pointer; the release selects / toggles
        UpdateWheel(p, r, frame);                                          // v7 (§15): the wheel over a grid with a cube in the hand = the brush
        UpdateArm(p, r);                                                   // v6: an empty-hand press / a paste click decides on release; a drag moves the island
        UpdateNodeHold(p);
        UpdateHandGhost(p, r);                                             // v6: the held cube's hologram on the hovered free tile
        UpdatePatternGhost(p, r);                                          // v6: the held pattern's adapted ghost on the hovered grid
        // dragging across tiles extends the path; tiles a fast drag skipped are filled along the grid line (SPEC v3 §2.6); F5: only when
        // the pointer itself moved since the press / last extension (a view still gliding under a still pointer draws nothing)
        if (p.held && dragging && IsDrawing && r.tile != null && r.tile != Last && (p.pos - extendPx).sqrMagnitude >= 1f && ExtendTo(r.tile)) { strokeExtended = true; extendPx = p.pos; }
        // v7 (§14.4): a phrase stroke paints on — a note in each cell reached after its last note's end, another row inside the newest note moves it
        if (p.held && strokeIsland >= 0 && !p.overUI && r.tile != null && (p.pos - strokePx).sqrMagnitude >= 1f) { strokePx = p.pos; StrokeAt(r.tile, false); }
        UpdateDraftHover(p.overUI ? null : r.tile, p.held && dragging);   // v4 R3: the ghost of what a click adds (v5: + the hologram's peek)
        UpdateHoverAudition(p, r);                                         // v5 R: hover audition everywhere (drafting: the run a click adds)
        if (p.up)
        {
            if (strokeIsland >= 0) EndPhraseStroke();   // v7: the whole phrase stroke is ONE History entry
            // only a drag stroke finishes the path on release; plain clicks keep drawing (click tile by tile, then click the end tile, Enter or right-click);
            // v6: a keyboard's glissando stroke does not finish its melody (Enter, a right-click, a click off the island or the done tick do)
            if (dragging && IsDrawing && strokeExtended && currentPathTiles.Count >= 2 && !Keys(Last != null ? Last.island : null)) FinishPath();
            dragging = false; strokeExtended = false;
            // a press on the candidate cube opens the inspector when released in place; a drag that started on a cube does nothing
            if (pressCube != null)
            {
                var c = pressCube; pressCube = null;
                if ((p.pos - pressCubePx).magnitude <= OrbitCamera.DragThresholdPx && c.isFinalized) CubeInspector.Open(c);
            }
            // a press on empty sea deselects only when released in place: a left-drag there is a camera pan (SPEC 2.6) and keeps the selection
            if (deselectArmed) { deselectArmed = false; if ((p.pos - deselectPx).magnitude <= OrbitCamera.DragThresholdPx) { Deselect(); FocusLoop.Dismiss(); GridSelection.Clear(); } }   // v4 R4: a click on empty sea ends the focus loop; v7: and clears the grid selection
        }
        if (p.rightUp && !p.overUI && (OrbitCamera.I == null || !OrbitCamera.I.RightDragged))
        {
            if (IsDrawing) FinishPath();
            else if ((Hand == HandKind.Cube || AutoHand) && r.tile != null && r.tile.island != null && r.tile.island.IsPhrase && ErasePhraseNote(r.tile)) { }   // v7: a right-click erases a note of the held group's melody
            else if (r.cube != null && r.rule == 1 && !r.overSea) DeleteCube(r.cube);   // F7: only a right-click on the cube body itself (over land) deletes it
            // v6: a right-click on the empty sea puts the hand down (a selection goes with it: the cube riding the cursor is what the player sees,
            // so one right-click must never only deselect while the hand stays full)
            else if ((Hand == HandKind.Cube && r.overSea) || Hand == HandKind.Pattern) { PutDown(); if (selectedCube != null) Deselect(); }
            else if (selectedCube != null) Deselect();
        }
    }

    // ------------------------------------------------------------------ picking (SPEC v3 §2.1)
    /// <summary>What <paramref name="screen"/> points at under the v3 hover rule (see <see cref="PickResult"/>). Pure apart from
    /// the raycast: tests call it with synthetic screen points.</summary>
    public PickResult PickAt(Vector3 screen, bool alt = false)
    {
        var r = new PickResult();
        if (cam == null) cam = Camera.main;
        if (cam == null) return r;
        var ray = cam.ScreenPointToRay(screen);
        bool drawing = IsDrawing;
        bool live = GlobalClock.IsPlaying;   // F6: while playing only the cube body picks a cube (rules 2 / 3 would retarget with every hop)
        // cubes are picked analytically (their kinematic rigidbodies do not detect collisions, so physics rays pass through them)
        float cubeDist = float.MaxValue;
        AudioCube rayCube = drawing ? null : RayCubes(ray, out cubeDist);
        int n = Physics.RaycastNonAlloc(ray, hits, 600f);
        if (n > 1) Array.Sort(hits, 0, n, ByDistance);
        TileInteraction tile = null; KeyBlock island = null; bool onHub = false, seam = false; float firstDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            var c = h.collider.GetComponentInParent<AudioCube>();
            if (c != null)
            {
                // a cube collider that does take queries: the nearer of it and the analytic hit wins
                if (!drawing && c.isFinalized && c.BodyVisible && h.distance < cubeDist) { rayCube = c; cubeDist = h.distance; }
                continue;
            }
            bool first = firstDist == float.MaxValue;
            if (first) firstDist = h.distance;
            var t = h.collider.GetComponentInParent<TileInteraction>();
            if (t != null) { tile = t; break; }
            if (island == null)
            {
                var kb = h.collider.GetComponentInParent<KeyBlock>();   // platform or hub collider (no tag needed)
                if (kb != null && kb.enabled)
                {
                    bool hub = kb.Hub != null && (h.collider.transform == kb.Hub || h.collider.transform.IsChildOf(kb.Hub));
                    // F1: tiles stand raised by pitch on a 1.12 pitch with no collider under them, so a ray through a seam meets the
                    // platform: inside the tile grid that is the nearest tile (only the outer margin and the hub are island handles)
                    if (first && !hub) { var st = SeamTile(kb, ray, h.point); if (st != null) { tile = st; seam = true; break; } }
                    island = kb; onHub = hub;
                }
            }
        }
        r.overSea = firstDist == float.MaxValue;
        bool cubeInFront = rayCube != null && cubeDist < firstDist;   // nothing of the world between the camera and the cube
        if (drawing)
        {
            if (tile != null) { r.tile = tile; r.rule = 4; r.seam = seam; }
            else { r.island = island; r.onHub = onHub; }
            return r;
        }
        if (alt)
        {
            if (cubeInFront) { tile = rayCube.CurrentOrHomeTile; seam = false; }   // Alt + press on a cube: a new path on its tile
            if (tile != null) { r.tile = tile; r.rule = 5; r.seam = seam; }
            else { r.island = island; r.onHub = onHub; }
            return r;
        }
        // v7: on a phrase or stairs the cell / step itself is what a click draws or presses — a cube resting there never takes the pick, and with a
        // cube in the hand not even its body (the empty hand still opens a cube by its body)
        bool drawKinds = tile != null && tile.island != null && (tile.island.IsPhrase || tile.island.IsStairs);
        if (cubeInFront && !(drawKinds && (Hand == HandKind.Cube || AutoHand))) { r.cube = rayCube; r.rule = 1; return r; }
        if (tile != null)
        {
            var top = live || drawKinds ? null : TopCubeOn(tile);
            if (top != null) { r.cube = top; r.rule = 2; return r; }
            r.tile = tile; r.rule = 4; r.seam = seam;   // a free tile wins: what is highlighted is what a click does
            return r;
        }
        var near = live ? null : NearestByScreenRect(screen);
        if (near != null) { r.cube = near; r.rule = 3; return r; }
        r.island = island; r.onHub = onHub;
        return r;
    }

    /// <summary>F1: the tile a platform hit stands for when it lies inside the tile grid (tile centres ± half a pitch, island-local x/z);
    /// null outside it (the margin: an island handle). The nearest tile is judged where the ray crosses each candidate tile's top, not at
    /// the platform point: tiles are raised by pitch, so the point under a seam lies below and behind the seam the pointer is on.</summary>
    public static TileInteraction SeamTile(KeyBlock kb, Ray ray, Vector3 platformPoint)
    {
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return null;
        if (kb.IsKeyboard || kb.IsStairs) return KeyUnder(kb, ray);   // v6: piano keys are not on the tile grid; v7: nor are stairs steps
        if (kb.IsPhrase) return PhraseCellUnder(kb, ray);             // v7: a phrase's cells (time x scale rows)
        var tr = kb.transform;
        float s = ProjectConfig.Spacing, h = s * 0.5f;
        Vector3 lp = tr.InverseTransformPoint(platformPoint);
        if (lp.x < -h || lp.z < -h || lp.x > (kb.cols - 1) * s + h || lp.z > (kb.rows - 1) * s + h) return null;
        int gx = Mathf.Clamp(Mathf.RoundToInt(lp.x / s), 0, kb.cols - 1), gz = Mathf.Clamp(Mathf.RoundToInt(lp.z / s), 0, kb.rows - 1);
        Vector3 o = tr.InverseTransformPoint(ray.origin), d = tr.InverseTransformDirection(ray.direction);
        TileInteraction best = null; float bestD = float.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var t = kb.GetTile(gx + dx, gz + dz);
                if (t == null || Mathf.Abs(d.y) < 1e-5f) continue;
                Vector3 top = tr.InverseTransformPoint(t.Top);
                float u = (top.y - o.y) / d.y;
                if (u <= 0f) continue;
                Vector3 q = o + d * u;
                float dd = Mathf.Max(Mathf.Abs(q.x - top.x), Mathf.Abs(q.z - top.z));   // grid distance where the ray passes this tile's top
                if (dd < bestD) { bestD = dd; best = t; }
            }
        return best != null ? best : kb.GetTile(gx, gz);
    }

    /// <summary>v6: the piano key a ray points at on a keyboard island (its keys are not on the Spacing grid): the key whose top face (its collider's
    /// bounds) the ray crosses — the raised black keys first — else, in the thin gaps between keys, the nearest key; null off the keys (the case's
    /// margin stays the island's handle). Each key is tested at its own height, so the rise of the keys and the raised black keys cause no parallax.</summary>
    public static TileInteraction KeyUnder(KeyBlock kb, Ray ray)
    {
        if (kb == null) return null;
        TileInteraction best = null; float bestD = float.MaxValue, bestTop = float.MinValue;
        foreach (var t in kb.tiles)
        {
            if (t == null) continue;
            var col = t.GetComponent<Collider>();
            Bounds b = col != null ? col.bounds : new Bounds(t.Top, new Vector3(0.5f, 0.1f, 1f));
            float y = b.max.y;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) continue;
            float u = (y - ray.origin.y) / ray.direction.y;
            if (u <= 0f) continue;
            Vector3 q = ray.origin + ray.direction * u;
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - q.x, q.x - b.max.x)), dz = Mathf.Max(0f, Mathf.Max(b.min.z - q.z, q.z - b.max.z));
            float d = dx + dz;
            if (d <= 1e-4f) { if (y > bestTop) { bestTop = y; best = t; bestD = 0f; } continue; }   // inside: the highest key wins (a black key over the white)
            if (bestTop == float.MinValue && d < bestD) { bestD = d; best = t; }
        }
        return best != null && (bestTop > float.MinValue || bestD < KeyGapSlack) ? best : null;
    }

    /// <summary>How far (world units) outside every key a ray may cross the keys' height and still pick the nearest key (the gaps between keys).</summary>
    public const float KeyGapSlack = 0.12f;

    /// <summary>The nearest finalized, visible cube whose box (its transform: squash, hop and lean included) the ray passes
    /// through, with the distance along the ray; null when none.</summary>
    public static AudioCube RayCubes(Ray ray, out float dist)
    {
        dist = float.MaxValue; AudioCube best = null;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || !c.BodyVisible) continue;
            var m = c.transform.worldToLocalMatrix;
            Vector3 o = m.MultiplyPoint3x4(ray.origin), d = m.MultiplyVector(ray.direction);   // unit-box space; the ray parameter stays world distance
            float t0 = 0f, t1 = float.MaxValue; bool hit = true;
            for (int a = 0; a < 3 && hit; a++)
            {
                float oa = o[a], da = d[a];
                if (Mathf.Abs(da) < 1e-8f) { if (oa < -0.5f || oa > 0.5f) hit = false; continue; }
                float ta = (-0.5f - oa) / da, tb = (0.5f - oa) / da;
                if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) hit = false;
            }
            if (hit && t0 < dist) { dist = t0; best = c; }
        }
        return best;
    }

    /// <summary>The top finalized, visible cube resting on <paramref name="tile"/> (null = a free tile).</summary>
    public static AudioCube TopCubeOn(TileInteraction tile)
    {
        if (tile == null) return null;
        AudioCube best = null; int bestIdx = -1;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || !c.BodyVisible) continue;
            int idx = SequenceMaster.IndexOn(tile, c);
            if (idx > bestIdx) { bestIdx = idx; best = c; }
        }
        return best;
    }

    /// <summary>The finalized, visible cube whose projected screen rect (8 renderer-bounds corners) grown by 16 px holds
    /// <paramref name="screen"/>; the nearest rect centre wins (null when none).</summary>
    public AudioCube NearestByScreenRect(Vector3 screen)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return null;
        AudioCube best = null; float bestD = float.MaxValue;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || !c.BodyVisible) continue;
            Rect rc;
            if (!ScreenRectOf(c, out rc)) continue;
            if (screen.x < rc.xMin - NearPx || screen.x > rc.xMax + NearPx || screen.y < rc.yMin - NearPx || screen.y > rc.yMax + NearPx) continue;
            float d = (new Vector2(screen.x, screen.y) - rc.center).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>Screen-pixel rect of a cube's renderer bounds (false when a corner is behind the camera).</summary>
    public bool ScreenRectOf(AudioCube c, out Rect rc)
    {
        rc = default(Rect);
        if (cam == null) cam = Camera.main;
        var rend = c != null ? c.BodyRenderer : null;
        if (cam == null || rend == null) return false;
        Bounds b = rend.bounds;
        Vector3 mn = b.min, mx = b.max;
        for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 s = cam.WorldToScreenPoint(corners[i]);
            if (s.z <= 0f) return false;
            x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
        }
        rc = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    void SetHover(TileInteraction tile, AudioCube cube)
    {
        if (hoverTile != tile) { if (hoverTile != null) hoverTile.SetHover(false); hoverTile = tile; if (hoverTile != null) hoverTile.SetHover(true); }
        if (hoverCube != cube) { if (hoverCube != null) hoverCube.SetHover(false); hoverCube = cube; if (hoverCube != null) hoverCube.SetHover(true); }
    }

    /// <summary>Outline + soft tick (pitch by instrument, at most 4 per second) + the onboarding event, once per hover.</summary>
    void SetCandidate(AudioCube c)
    {
        if (c == candidate) return;
        candidate = c;
        if (c == null) { CubeOutline.Hide(); return; }
        CubeOutline.Show(c);
        if (!GlobalClock.IsPlaying && Time.unscaledTime - lastHoverTick >= 0.25f)   // F6: no ticks over the music
        {
            lastHoverTick = Time.unscaledTime;
            AudioPool.UI(ProceduralAudio.Tick(), 0.14f, HoverPitch(c.instrument));
        }
        Onboarding.Notify(Onboarding.Ev.CubeHovered);
    }

    static float HoverPitch(int instrument)
    {
        if (Instruments.IsDrums(instrument)) return 0.72f;
        if (instrument == 4) return 0.62f;                       // bass: low
        return 0.8f + 0.055f * Mathf.Clamp(instrument, 0, 9);
    }

    void OnPress(PickResult r, PointerIn pin)
    {
        Vector3 px = pin.pos;
        var tile = r.tile;
        if (IsDrawing)
        {
            if (tile != null && Last != null && tile.island == Last.island && Keys(tile.island))
            {
                // v6 keyboard: a click adds exactly the clicked key (the last key again: a repeated note); a drag from here adds each key it enters
                AddKey(tile);
                dragging = true; strokeExtended = false;
                extendPx = px;
                return;
            }
            if (tile != null && Last != null && tile.island == Last.island)
            {
                // clicking the end tile again finishes; F1: a seam press beside it is ambiguous (the tile next to it may be meant): nothing
                if (tile == Last && !r.seam) { FinishPath(); return; }
                if (tile != Last) ExtendTo(tile);                  // a neighbour, or the straight / diagonal grid line to a farther tile
                dragging = true; strokeExtended = false;           // a drag from here keeps extending; a plain click never finishes
                extendPx = px;
            }
            else if (tile == null && r.island != null && Last != null && r.island == Last.island) { }   // F1: the same island's margin or hub: nothing
            else FinishPath();                                     // another island or empty space: done (this press starts nothing)
            return;
        }
        if (sessionPhrase >= 0 && (tile == null || tile.island == null || !tile.island.IsPhrase || tile.island.column != sessionCol || tile.island.phraseOffset != sessionOffset))
            EndPhraseSession();   // v7: a press anywhere else ends drawing on the phrase
        if (Hand == HandKind.Pattern)
        {
            // v6: a pattern in the hand pastes on the grid under the pointer when the press is released in place (a drag moves the island)
            var kb = PasteTargetOf(r);
            if (kb != null) { Arm(ArmKind.Paste, tile, kb, r.onHub, px, false); return; }
            deselectArmed = true; deselectPx = px;
            return;
        }
        if (r.cube != null) { pressCube = r.cube; pressCubePx = px; return; }   // v3: opens the inspector on release (package A)
        if (tile == null && r.island != null)
        {
            // platform / hub press: IslandDrag decides on release (click: select / cycle bars + OnHubClicked; drag: move)
            islandDrag.Arm(r.island, r.onHub, px);
            return;
        }
        if (tile != null)
        {
            if (r.rule != 5 && selectedCube != null && selectedByClick && selectedCube.nodes.Contains(tile))
            {
                // a bead of the click-selected cube: a plain click toggles the rest (on release); holding 0.35 s raises OnNodeHeld
                holdCube = selectedCube; holdTile = tile; holdNode = selectedCube.NodeIndexOf(tile);
                holdStart = Time.unscaledTime; holdPx = px; holdFired = false;
                tile.Press(0.3f);
                return;
            }
            // v7: a cube in the hand on STAIRS places the group's runner (the whole run); on a PHRASE a press starts a stroke of notes
            if ((Hand == HandKind.Cube || AutoHand) && tile.island != null && tile.island.IsStairs) { PlaceRunner(tile.island, selectedInstrument); return; }
            if ((Hand == HandKind.Cube || AutoHand) && tile.island != null && tile.island.IsPhrase) { BeginPhraseStroke(tile, px); return; }
            if (Hand == HandKind.Cube || AutoHand)
            {
                StartPath(tile);   // a free tile, or Alt + a tile that carries a cube (the new cube stacks on top)
                dragging = true; strokeExtended = false; extendPx = px;
                return;
            }
            // v6: the EMPTY hand presses the tile (its note, loud, at once); a drag from it moves the island, Alt + drag plays across the tiles
            Arm(ArmKind.Press, tile, tile.island, false, px, pin.alt);
            PressTile(tile);
            return;
        }
        deselectArmed = true; deselectPx = px;
    }

    void UpdateNodeHold(PointerIn p)
    {
        if (holdCube == null) return;
        if (holdTile == null || holdNode < 0) { holdCube = null; return; }
        if (p.up)
        {
            if (!holdFired && holdNode < holdCube.rests.Count)
            {
                holdCube.ToggleRest(holdNode);
                holdTile.Press(0.5f);
                AudioPool.UI(ProceduralAudio.Tick(), 0.4f, holdCube.rests[holdNode] ? 0.8f : 1.2f);
                History.Push();
            }
            holdCube = null; holdTile = null; holdNode = -1;
            return;
        }
        if (!p.held) { holdCube = null; holdTile = null; return; }
        if ((p.pos - holdPx).magnitude > OrbitCamera.DragThresholdPx) { holdCube = null; holdTile = null; return; }
        if (!holdFired && Time.unscaledTime - holdStart >= 0.35f)
        {
            holdFired = true;
            holdTile.Press(0.6f); holdTile.Flash(holdCube.Color, 0.6f);
            AudioPool.UI(ProceduralAudio.Pop(), 0.35f, 1.3f);
            OnNodeHeld?.Invoke(holdCube, holdNode);
        }
    }

    internal void RaiseHubClicked(KeyBlock kb) { if (kb != null) OnHubClicked?.Invoke(kb); }

    void StartPath(TileInteraction tile)
    {
        // v7 §19.1: the first note must fit the grid too (auto-expanded when its section has room; else refused: nothing starts)
        if (!AutoHand && tile.island != null && !GridRoom(tile.island, brushTicks / (float)ProjectConfig.TicksPerBeat, 0f)) return;
        EndPhraseSession();
        Deselect();
        bool onMoon = tile.island != null && tile.island.IsMoon;
        int inst = onMoon ? DrumsInstrument : selectedInstrument;   // Moons carry no chord: only drums live there (§2.9)
        activeCube = SpawnCube(inst);
        activeCube.SetShape(BrushSphere);                           // SPHERE: the chosen shape (a cube by default)
        activeCube.SetHologram(true);                               // v4 R3: a hologram until it is finished
        if (onMoon)
        {
            activeCube.moon = SongManager.I != null ? Mathf.Max(0, SongManager.I.Moons.IndexOf(tile.island)) : 0;
            activeCube.Moon = tile.island;
        }
        activeCube.PlaceOn(tile, false);
        currentPathTiles.Clear();
        currentPathTiles.Add(tile);
        activeCube.SetPath(currentPathTiles);
        activeCube.SetAllDurations(new[] { BrushTicks });           // node 0 gets the brush length: the draft runs the durations engine
        activeCube.Materialize();                                   // it beams in where it stands (no drop: it plays at once)
        MelodyLine.Ensure();                                        // v5: the draft's melody line
        currentState = EditorState.DrawingPath; isSettingPath = true;
        SequenceMaster.RecalculateTimeline();                       // windows like a finished cube: the draft plays live
        var sm = SongManager.I;
        draftCol = !onMoon && tile.island != null ? tile.island.column : -1;
        draftFloor = draftCol >= 0 && sm != null ? sm.ColumnBars(draftCol) : -1;
        drawLoop = false; CapacityReached = false;
        if (AutoHand) { if (sm != null) FocusLoop.Begin(onMoon ? sm.LitColumn : tile.island.column); }   // v4 R4 (the pre-v6 suites): its column loops while it is drawn
        else QuietStart();   // v7 §19.2: no loop by itself; a playing song pauses
        bool loopRestarted = GlobalClock.IsPlaying && GlobalClock.HasRegion && Math.Abs(GlobalClock.SongBeatD - GlobalClock.RegionStart) < 1e-6;
        if (!loopRestarted) tile.PlayPreview(inst, 0, activeCube.voice);
        else { tile.Press(); tile.Flash(Instruments.Colors[Mathf.Clamp(inst, 0, Instruments.Count - 1)], 0.9f); }   // the loop (re)starts on this column: the draft plays node 0 itself
        RaiseDraftKey(tile);   // v7 (C): the cat plays the first key too
        UpdateHighlights();
        // no camera focus on a draw (SPEC §2.6): the leash follows the comet, nothing cuts
        RaiseDraft(0, activeCube);
        Onboarding.Notify(Onboarding.Ev.PathStarted);
        keyExtLow = 0; keyExtHigh = 0;
        MaybeExtendKeys(tile);   // v7 §20.1: a melody starting on an edge key gets room that way
    }

    AudioCube SpawnCube(int instrument)
    {
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        var go = Instantiate(cubePrefabs[Mathf.Min(instrument, cubePrefabs.Count - 1)], cubesRoot);   // slots 6-9 reuse a prefab and are recoloured by Init
        go.name = "Cube_" + instrument;
        var c = go.GetComponent<AudioCube>();
        if (c == null) c = go.AddComponent<AudioCube>();
        c.Init(instrument);
        c.voice = Instruments.BrushVoiceOf(instrument);   // v6: the group's brush sound (a restore overrides it)
        return c;
    }

    /// <summary>Adds <paramref name="tile"/> (same island as the path's end) to the path: a neighbour directly, a farther tile through the
    /// straight / diagonal grid line to it, so fast drags and far clicks never skip tiles (SPEC v3 §2.6). True when a tile was added.</summary>
    bool ExtendTo(TileInteraction tile)
    {
        var last = Last;
        if (tile == null || last == null || tile == last || tile.island != last.island || activeCube == null) return false;
        if (activeCube.sphere) { lineScratch.Clear(); lineScratch.Add(tile); }   // SPHERE: a leap — just the clicked tile, no line of tiles to it
        else LineTiles(last, tile, lineScratch);
        if (lineScratch.Count == 0) return false;
        if (!DraftRoom(lineScratch.Count * brushTicks / (float)ProjectConfig.TicksPerBeat)) return false;   // v7 §19.1: the grid's capacity (auto-expand / refuse)
        var d = DraftDurs();
        Color c = activeCube.Color;
        foreach (var t in lineScratch) { currentPathTiles.Add(t); d.Add(BrushTicks); t.Flash(c, 0.7f); }   // v4 R3: every new node gets the brush length
        activeCube.SetPathAndDurations(currentPathTiles, d);       // their beads pop in (0 -> 1.2 -> 1)
        if (!GlobalClock.IsPlaying) activeCube.PreviewHopTo(Last); // while the loop plays the draft traces its path in rhythm instead
        Last.PlayPreview(activeCube.instrument, activeCube.octave, activeCube.voice, activeCube.layer, VoiceRules.BendOf(activeCube, Last, activeCube.nodes.Count - 1));   // v7 (A): its layer + bend
        RaiseDraftKey(Last);     // v7 (C): the key that sounded (a run's end)
        MaybeExtendKeys(Last);   // v7 §20.1: a glissando reaching the keyboard's edge extends it
        UpdateHighlights();
        previewTo = null;
        RaiseDraft(1, activeCube);
        return true;
    }

    static bool Adjacent(TileInteraction a, TileInteraction b)
    {
        if (a == null || b == null || a.island != b.island) return false;
        return Mathf.Max(Mathf.Abs(a.gridX - b.gridX), Mathf.Abs(a.gridZ - b.gridZ)) == 1;
    }

    /// <summary>Finishes the draft: it solidifies (v4 R3: glitch, the split converges, a white flash, the solid cube + ink burst + pop), the
    /// timeline is recomputed and History gets ONE entry. The focus loop stays until dismissed.</summary>
    public void FinishPath()
    {
        if (!IsDrawing) return;
        GiveBackKeys(false);   // v7 §20.1: octaves the draft added that hold no notes go back (before the draft lets go of its tiles)
        var c = activeCube;
        var fillIsland = Last != null ? Last.island : null;
        activeCube = null;
        currentState = EditorState.Idle; isSettingPath = false;
        ClearHighlights(); HideDraftPreview(); ResetHoverAudition();
        currentPathTiles.Clear();
        ClearFill(fillIsland); CapacityReached = false; draftCol = -1;   // v7 §19.1 (the grid keeps its size: the finish's History entry has it)
        if (c != null)
        {
            c.Peek(null);
            c.Finalize();
            c.Solidify();
            SequenceMaster.RecalculateTimeline();
            History.Push();   // v3: a finished path does not select its cube, so the next path is one click away (click the cube to inspect it)
            RaiseDraft(2, c);
            Onboarding.Notify(Onboarding.Ev.PathFinished);
            pendingFit = c;   // next frame: the finish frame already serialised a snapshot; a rebuild on top of it can starve the audio lookahead
        }
    }

    AudioCube pendingFit;

    /// <summary>v4 (integration): a freshly drawn melody whose notes run past its island's span would be cut off at the island's end (the
    /// later notes never sound); the island's column grows to fit it (SongManager.SetBars, at most 4 bars, one more History entry). Runs
    /// after the draft's events: SetBars rebuilds the song, so <paramref name="c"/> is replaced by its restored twin.</summary>
    void FitColumnToCube(AudioCube c)
    {
        var sm = SongManager.I;
        if (c == null || sm == null || !c.HasDurations || c.IsOnMoon || c.Moon != null) return;
        var isl = c.Island;
        if (isl == null || isl.IsMoon) return;
        float cycle = 0f;
        for (int i = 0; i < c.durs.Count; i++) cycle += c.durs[i] / (float)ProjectConfig.TicksPerBeat;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        int want = Mathf.Clamp(Mathf.CeilToInt(cycle / bpb - 1e-4f), 1, 4);
        if (want <= isl.bars) return;
        int idx = sm.Islands.IndexOf(isl);
        if (idx >= 0) sm.SetBars(idx, want);
    }

    /// <summary>Drops the draft: its hologram dissolves (a scanline wipe). No History entry.</summary>
    public void CancelPath() { CancelPath(true); }
    /// <param name="animate">true: the hologram dissolves (Esc, Backspace on the last node); false: it is gone at once (a song rebuild).</param>
    public void CancelPath(bool animate)
    {
        if (!IsDrawing) return;
        if (animate) GiveBackKeys(true); else { keyExtLow = 0; keyExtHigh = 0; }   // v7 §20.1: a dropped draft gives back every octave it added
        var c = activeCube;
        var fillIsland = Last != null ? Last.island : null;
        activeCube = null;
        currentState = EditorState.Idle; isSettingPath = false;
        ClearHighlights(); HideDraftPreview(); ResetHoverAudition();
        currentPathTiles.Clear();
        ClearFill(fillIsland); CapacityReached = false;
        // v7 §19.1: a dropped draft gives back the measures auto-expand added for it (an explicit EXPAND stays: it is in History)
        var smc = SongManager.I;
        if (animate && !AutoHand && smc != null && draftCol >= 0 && draftCol < smc.ColumnCount && draftFloor > 0 && smc.ColumnBars(draftCol) > draftFloor && smc.ResizeColumnLive(draftCol, draftFloor)) AutoUnexpands++;
        draftCol = -1;
        if (c != null)
        {
            c.Peek(null);
            if (animate) c.Dissolve();
            else { SequenceMaster.Unregister(c); c.Delete(false); }
        }
        RaiseDraft(2, null);
    }

    /// <summary>S2: Cmd+Z while drawing takes back the draft's last node (the last one left: the draft is cancelled). No History entry:
    /// the draft is not in the song until it is finished. v4: the same as <see cref="RemoveLastNode"/>.</summary>
    public void UndoDraftNode() { RemoveLastNode(); }

    /// <summary>S2 / S6: a keyboard structural op (N, undo, redo) is about to rebuild the song: a click-drawn draft is finished first
    /// (a rebuild would drop it) and an island drag is cancelled (its merge sheet and gap preview with it).</summary>
    public void BeforeStructuralKey()
    {
        if (islandDrag.Busy) islandDrag.Cancel();
        if (IsDrawing) FinishPath();
        if (strokeIsland >= 0) EndPhraseStroke();   // v7
        CancelShift();
    }

    void UpdateHighlights()
    {
        var last = Last; if (last == null) return;
        bool keys = Leaps(last);   // v6: on a keyboard every key is one click away (no candidate ring); SPHERE: every tile is
        foreach (var t in last.island.tiles)
        {
            int idx = currentPathTiles.IndexOf(t);
            if (idx == 0) t.SetHighlight(TileInteraction.Highlight.Start);
            else if (idx > 0) t.SetHighlight(TileInteraction.Highlight.Path);
            else if (!keys && Adjacent(last, t)) t.SetHighlight(TileInteraction.Highlight.Candidate);
            else t.SetHighlight(TileInteraction.Highlight.None);
        }
    }

    public void ClearAllHighlights() => ClearHighlights();
    void ClearHighlights()
    {
        if (SongManager.I == null) return;
        foreach (var kb in SongManager.I.Islands) if (kb != null) foreach (var t in kb.tiles) t.SetHighlight(TileInteraction.Highlight.None);
        foreach (var kb in SongManager.I.Moons) if (kb != null) foreach (var t in kb.tiles) t.SetHighlight(TileInteraction.Highlight.None);
    }

    // ---------------- selection (v3: a selected cube is the inspected cube — CubeInspector keeps the two in step)
    public void Select(AudioCube cube, bool byClick = false)
    {
        selectedByClick = byClick;
        if (selectedCube == cube) return;
        if (selectedCube != null) selectedCube.SetSelected(false);
        selectedCube = cube;
        if (selectedCube != null) { selectedCube.SetSelected(true); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.4f); }
        OnSelectionChanged?.Invoke(selectedCube);
    }
    public void Deselect()
    {
        if (selectedCube == null) return;
        selectedCube.SetSelected(false);
        selectedCube = null;
        OnSelectionChanged?.Invoke(null);
    }
    public void DeleteSelected() { if (selectedCube != null) DeleteCube(selectedCube); }
    public void DeleteCube(AudioCube cube) { DeleteCube(cube, true); }
    /// <summary>Deletes a cube (deselecting it first), recomputes the timeline and pushes History; <paramref name="fx"/> = the world pop.</summary>
    public void DeleteCube(AudioCube cube, bool fx)
    {
        if (cube == null) return;
        if (selectedCube == cube) Deselect();
        if (candidate == cube) SetCandidate(null);
        if (hoverCube == cube) { hoverCube.SetHover(false); hoverCube = null; }
        cube.Delete(fx);
        SequenceMaster.Unregister(cube);   // Destroy lands at the end of the frame: without this the snapshot below still holds the cube and History drops it as a duplicate
        SequenceMaster.RecalculateTimeline();
        History.Push();
    }

    public void ClearAllPaths(bool record = true)
    {
        CancelPath(false);
        Deselect();
        holdCube = null; holdTile = null; pressCube = null;
        SetCandidate(null); hoverCube = null;
        foreach (var c in new List<AudioCube>(SequenceMaster.Cubes)) if (c != null) c.Delete(false);
        SequenceMaster.Cubes.Clear();
        SequenceMaster.ClearOccupancy();
        currentPathTiles.Clear();
        if (record) { SequenceMaster.RecalculateTimeline(); History.Push(); AudioPool.UI(ProceduralAudio.Whoosh(), 0.5f); }
    }

    // ------------------------------------------------------------------ v6 the hand (SPEC v6 §2.3; M0 contract — H implements)
    /// <summary>What the player holds: nothing (clicks on tiles only play them), a cube (clicks place paths: placing mode) or a copied pattern
    /// (clicks paste it). Only an explicit pick-up (a HUD chip, a hotkey, the clipboard chip) fills the hand.</summary>
    public enum HandKind { Empty, Cube, Pattern }
    public HandKind Hand { get; private set; }
    public bool CubeInHand => Hand == HandKind.Cube;
    /// <summary>Fires after the hand changed (the HUD chip lifts, the cursor carries the cube / the pattern).</summary>
    public static event Action OnHandChanged;
    /// <summary>Tests only: while true a press on a free tile with an empty hand draws with <see cref="selectedInstrument"/>, exactly as before v6
    /// (the older suites draw by clicking tiles). The hand itself stays empty, so Esc, the hover and the cursor behave as in v5. The game never sets it.</summary>
    public static bool AutoHand;
    /// <summary>SPHERE (DeaCube/SphereBody.cs): the shape the next piece is drawn as — false a cube (steps through every tile on its way), true a
    /// sphere (leaps straight to the clicked tile, bounces, rolls). Chosen on the HUD's shape switch above the instrument column.</summary>
    public bool BrushSphere { get; private set; }
    /// <summary>Fires after <see cref="BrushSphere"/> changed (the HUD's switch and chips repaint).</summary>
    public static event Action OnShapeChanged;
    /// <summary>Sets the shape of the next piece; a draft in progress becomes that shape too.</summary>
    public void SetBrushShape(bool round)
    {
        if (BrushSphere == round) return;
        BrushSphere = round;
        if (IsDrawing && activeCube != null) activeCube.SetShape(round);
        OnShapeChanged?.Invoke();
    }
    /// <summary>A draft that leaps (adds exactly the clicked tile): a keyboard's keys, or any grid for a sphere.</summary>
    bool Leaps(TileInteraction t) => Keys(t != null ? t.island : null) || (activeCube != null && activeCube.sphere);

    /// <summary>Picks up a cube of <paramref name="instrument"/> (<paramref name="voice"/> -1 = Instruments.BrushVoice of the group, else it becomes
    /// the brush voice): placing mode. Picking up while drawing re-instruments the draft. Always raises <see cref="OnHandChanged"/>.</summary>
    public void PickUpCube(int instrument, int voice = -1)
    {
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        if (voice >= 0) Instruments.BrushVoice[instrument] = Mathf.Clamp(voice, 0, Mathf.Max(0, Instruments.VoiceCount(instrument) - 1));
        SelectInstrument(instrument);
        if (activeCube != null && !activeCube.IsOnMoon) activeCube.SetVoice(Instruments.BrushVoiceOf(instrument));
        Hand = HandKind.Cube;
        HidePatternGhost(); armKind = ArmKind.None;
        OnHandChanged?.Invoke();
    }
    /// <summary>v7 (U2's paste fan): how the held pattern pastes — Plain, EchoUp / EchoDown (an octave echo; the source's own island is allowed: a
    /// doubling), Answer. <see cref="PutDown"/> resets it to Plain.</summary>
    public Clipboard.PasteMode PatternMode { get; private set; }
    /// <summary>Picks up the clipboard's pattern (paste mode; a draft in progress is finished first); false when the clipboard is empty. v7: Plain.</summary>
    public bool PickUpPattern() => PickUpPattern(Clipboard.PasteMode.Plain);
    /// <summary>v7: picks up the clipboard's pattern to paste as <paramref name="mode"/> (see <see cref="PatternMode"/>).</summary>
    public bool PickUpPattern(Clipboard.PasteMode mode)
    {
        if (!Clipboard.HasPattern) return false;
        if (IsDrawing) FinishPath();
        PatternMode = mode;
        Hand = HandKind.Pattern;
        HideHandGhost(); patternKey = int.MinValue; armKind = ArmKind.None;
        OnHandChanged?.Invoke();
        return true;
    }
    /// <summary>Empties the hand (a draft in progress is finished first).</summary>
    public void PutDown()
    {
        if (Hand == HandKind.Empty) return;
        if (IsDrawing) FinishPath();
        EndPhraseSession();
        Hand = HandKind.Empty;
        PatternMode = Clipboard.PasteMode.Plain;
        HideHandGhost(); HidePatternGhost(); armKind = ArmKind.None;
        AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.72f);
        OnHandChanged?.Invoke();
    }

    // ---- v6: presses. The empty hand PRESSES tiles (a click plays the note loud at once: Synth.PressNote, the song ducks under it); a press that
    // turns into a drag hands the island to IslandDrag (as a press on its platform does); Alt + drag plays every tile the pointer enters (a glissando
    // on a keyboard). A pattern in the hand pastes on the grid a press is released on (a drag moves that island instead).
    enum ArmKind { None, Press, Paste }
    ArmKind armKind; TileInteraction armTile, glissTile; KeyBlock armIsland; bool armOnHub, armAlt; Vector3 armPx;
    /// <summary>Empty-hand presses, glissando notes, press → island-drag handovers and keys added to keyboard drafts so far (tests).</summary>
    public static int PressCount, GlissCount, DragHandovers, KeyAdds;
    /// <summary>The last pressed tile (tests).</summary>
    public TileInteraction LastPressTile { get; private set; }
    /// <summary>Fires when the empty hand pressed a tile (a click or a glissando note): the tutorial's "a click just plays it", the cursor.</summary>
    public static event Action<TileInteraction> OnTilePressed;
    /// <summary>The tile an empty-hand click would press (the free tile under the pointer), else null (the cursor's "press to hear").</summary>
    public TileInteraction HoverPressTile => Hand == HandKind.Empty && !AutoHand && !IsDrawing ? hoverTile : null;
    /// <summary>How long a press sounds: the brush length at the tempo (0.35 .. 1.2 s).</summary>
    public float PressSeconds => Mathf.Clamp(AuditionSeconds(BrushTicks), 0.35f, 1.2f);

    /// <summary>Plays <paramref name="t"/> as a press with the brush group and its brush voice (Moons: the kit), loud, at once. True when the synth
    /// played it.</summary>
    public bool PressTile(TileInteraction t)
    {
        if (t == null) return false;
        int inst = t.island != null && t.island.IsMoon ? DrumsInstrument : selectedInstrument;
        PressCount++; LastPressTile = t;
        bool sounded = t.PlayPress(inst, -1, 0, PressSeconds) >= 0.0;
        OnTilePressed?.Invoke(t);
        return sounded;
    }

    void Arm(ArmKind k, TileInteraction t, KeyBlock kb, bool onHub, Vector3 px, bool alt)
    {
        armKind = k; armTile = t; armIsland = kb; armOnHub = onHub; armPx = px; armAlt = alt; glissTile = t;
    }

    /// <summary>An armed press: held + Alt → a glissando over the tiles entered; held + moved past the drag threshold → IslandDrag takes the island
    /// (its next update begins the drag from the press point); released in place → a press shows the island's header, a paste pastes.</summary>
    void UpdateArm(PointerIn p, PickResult r)
    {
        if (armKind == ArmKind.None) return;
        if (p.held && !p.up)
        {
            if (armKind == ArmKind.Press && armAlt)
            {
                var t = p.overUI ? null : r.tile;
                if (t != null && t != glissTile) { glissTile = t; PressTile(t); GlissCount++; }
                return;
            }
            if ((p.pos - armPx).magnitude > OrbitCamera.DragThresholdPx)
            {
                var kb = armIsland; bool hub = armOnHub; var px = armPx;
                armKind = ArmKind.None;
                if (kb != null) { islandDrag.Arm(kb, hub, px); DragHandovers++; }
            }
            return;
        }
        if (!p.up) return;   // kept until the release (like a stroke): an idle frame between a press and its release changes nothing
        if ((p.pos - armPx).magnitude <= OrbitCamera.DragThresholdPx)
        {
            if (armKind == ArmKind.Paste) Clipboard.PasteOn(armIsland, PatternMode);
            else if (armKind == ArmKind.Press) ShowHeaderOf(armIsland);
        }
        armKind = ArmKind.None;
    }

    /// <summary>A pressed island's header shows (the island is selected without moving the camera; a press on the sea or a new draft lets it go).</summary>
    static void ShowHeaderOf(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (kb == null || sm == null || UIManager.I == null || kb.IsMoon) return;
        int idx = sm.Islands.IndexOf(kb);
        if (idx >= 0) UIManager.I.SelectMeasure(idx, false);
    }

    /// <summary>The grid a paste would land on for this pick: the tile's island, else the platform's, else the picked cube's home.</summary>
    static KeyBlock PasteTargetOf(PickResult r)
    {
        if (r.tile != null) return r.tile.island;
        if (r.island != null) return r.island;
        if (r.cube != null) return r.cube.Moon != null ? r.cube.Moon : r.cube.Island;
        return null;
    }

    // ---- v6: a keyboard island (kind 2): its draft adds keys one by one
    static bool Keys(KeyBlock kb) => kb != null && kb.IsKeyboard;

    /// <summary>Keyboard drawing: appends exactly <paramref name="tile"/> (the last key again = a repeated note: the cube re-hops on it, the beads
    /// stack along the key). The click plays the key (a press). No History (the draft is not in the song until it is finished).</summary>
    public bool AddKey(TileInteraction tile)
    {
        var last = Last;
        if (!IsDrawing || activeCube == null || tile == null || last == null || tile.island != last.island) return false;
        if (!DraftRoom(brushTicks / (float)ProjectConfig.TicksPerBeat)) return false;   // v7 §19.1
        var d = DraftDurs();
        currentPathTiles.Add(tile); d.Add(BrushTicks);
        tile.Flash(activeCube.Color, 0.7f);
        activeCube.SetPathAndDurations(currentPathTiles, d);
        if (!GlobalClock.IsPlaying) activeCube.PreviewHopTo(Last);
        Last.PlayPreview(activeCube.instrument, activeCube.octave, activeCube.voice, activeCube.layer, VoiceRules.BendOf(activeCube, Last, activeCube.nodes.Count - 1));   // v7 (A): its layer + bend
        RaiseDraftKey(tile);     // v7 (C): the cat plays it
        MaybeExtendKeys(tile);   // v7 §20.1: a note on the lowest / highest key extends the keyboard that way
        UpdateHighlights();
        previewTo = null;
        KeyAdds++;
        RaiseDraft(1, activeCube);
        return true;
    }

    // ---- v6: the cube in the hand hovers as a hologram on the free tile under the pointer (the instrument's colour, the brush length's size).
    // v7 (§15, size first): the hologram IS the next node — its size is the brush (chosen before the click: the size row, the wheel, [ ] .), shown
    // while drawing too (on the tile a click goes to); on a PHRASE it is the note a click places: a bar over the brush's cells from the hovered
    // cell (past the roll's end when the note will grow it); on STAIRS the runner's size (the brush's rate) on the hovered step + ghost rings on
    // the steps the run plays.
    Transform handGhost; Material handGhostMat; TileInteraction handGhostTile; float handGhostT;
    /// <summary>The free tile the held cube's hologram sits on (null when none shows; tests).</summary>
    public TileInteraction HandGhostTile => handGhost != null && handGhost.gameObject.activeSelf ? handGhostTile : null;
    /// <summary>The hologram's transform (null before the first show; tests / captures).</summary>
    public Transform HandGhost => handGhost;
    /// <summary>v7: the note length the hologram shows (ticks: the brush; on stairs the runner's rate); -1 when none shows (tests).</summary>
    public int HandGhostTicks { get; private set; } = -1;
    /// <summary>v7: the hologram's size (world units: the cube's edge; on a phrase the bar's length over the cells); 0 when none shows (tests).</summary>
    public float HandGhostSize { get; private set; }
    static readonly int GhostColorId = Shader.PropertyToID("_Color"), GhostAlphaId = Shader.PropertyToID("_Alpha"), GhostGlitchId = Shader.PropertyToID("_Glitch");

    void UpdateHandGhost(PointerIn p, PickResult r)
    {
        TileInteraction t = null;
        if (!p.overUI && Hand == HandKind.Cube && !shiftArmed && r.tile != null && strokeIsland < 0)
        {
            if (!IsDrawing) { if (r.rule == 4 || r.rule == 5) t = r.tile; }
            else { var last = Last; if (last != null && r.tile.island == last.island && (r.tile != last || Keys(r.tile.island))) t = r.tile; }   // v7: the next node
        }
        if (t == null) { HideHandGhost(); return; }
        if (handGhost == null) BuildHandGhost();
        if (t != handGhostTile) { handGhostTile = t; handGhostT = 0f; }
        handGhostT += Time.unscaledDeltaTime;
        var kb = t.island;
        int inst = kb != null && kb.IsMoon ? DrumsInstrument : selectedInstrument;
        float pop = handGhostT < 0.16f ? Mathf.LerpUnclamped(0.55f, 1f, Ease.OutBack(handGhostT / 0.16f)) : 1f;
        float bob = 0.05f * Mathf.Sin(Time.unscaledTime * 5.2f);
        if (kb != null && kb.IsPhrase)
        {
            // the note a click places: from the hovered cell's left edge over brush / cell cells, a slab one row deep
            float cellW = CellPitchX(kb, t);
            float span = Mathf.Max(0.08f, brushTicks / (float)Mathf.Max(1, kb.phraseGrid) * cellW);
            float depth = Mathf.Min(ProjectConfig.PhraseRowPitch * 0.8f, 0.42f), h = 0.22f;
            handGhost.rotation = kb.transform.rotation;
            handGhost.position = t.Top + kb.transform.right * (span * 0.5f - cellW * 0.5f) + Vector3.up * (h * 0.5f + 0.06f + bob * 0.3f);
            handGhost.localScale = new Vector3(Mathf.Max(0.04f, span - 0.05f) * pop, h * pop, depth * pop);
            HandGhostTicks = brushTicks; HandGhostSize = span;
            SetHandGhostShape(false);   // a phrase note is a slab
            HideStairGhost();
            if (MelodyLine.I == null) MelodyLine.Ensure();   // the phrase's melody card shows while it is hovered
        }
        else
        {
            int ticks = kb != null && kb.IsStairs ? RunnerRate(brushTicks) : brushTicks;   // a runner runs at the stairs rate nearest the brush
            float size = ProjectConfig.CubeSize * AudioCube.SizeOf(ticks / (float)ProjectConfig.TicksPerBeat) * AudioCube.FootOf(t);   // v6: smaller on keys
            float below = r.rule == 5 && !IsDrawing ? SequenceMaster.StackHeight(t, SequenceMaster.CountOn(t)) : 0f;   // Alt over a cube: it would stack on top
            handGhost.position = t.Top + Vector3.up * (below + size * 0.5f + 0.08f + bob);
            handGhost.localScale = Vector3.one * (size * pop);
            handGhost.rotation = Quaternion.Euler(0f, 14f * Mathf.Sin(Time.unscaledTime * 1.4f), 0f);
            SetHandGhostShape(BrushSphere);   // SPHERE: the hologram in the hand is a ball
            HandGhostTicks = ticks; HandGhostSize = size;
            if (kb != null && kb.IsStairs) ShowStairGhost(kb, ticks, inst); else HideStairGhost();
        }
        float h01 = Rhythm.Hash01(Mathf.FloorToInt(Time.time * 12f), 7, 13);   // the hologram flickers on twos
        handGhostMat.SetColor(GhostColorId, Instruments.Colors[Mathf.Clamp(inst, 0, Instruments.Count - 1)]);
        handGhostMat.SetFloat(GhostAlphaId, 0.58f + 0.14f * h01);
        handGhostMat.SetFloat(GhostGlitchId, h01 > 0.86f ? 0.8f : 0.2f);
        if (!handGhost.gameObject.activeSelf) handGhost.gameObject.SetActive(true);
    }

    /// <summary>v7: world units between two neighbouring cells of a phrase (along its time axis), from the cells themselves.</summary>
    static float CellPitchX(KeyBlock kb, TileInteraction t)
    {
        var n = kb.GetTile(t.gridX + 1, t.gridZ);
        if (n == null) n = kb.GetTile(t.gridX - 1, t.gridZ);
        if (n != null) { float d = Mathf.Abs(kb.transform.InverseTransformPoint(n.Top).x - kb.transform.InverseTransformPoint(t.Top).x); if (d > 1e-3f) return d; }
        return kb.cols > 0 ? kb.PhraseWidth / kb.cols : 0.5f;
    }

    // v7: the stairs ghost — rings on the steps the run a click places plays (its rate: the brush's)
    readonly List<Transform> stairBeads = new List<Transform>(); Material stairBeadMat;
    /// <summary>v7: the steps the stairs ghost shows (tests).</summary>
    public int StairGhostCount { get { int n = 0; foreach (var b in stairBeads) if (b != null && b.gameObject.activeSelf) n++; return n; } }

    void ShowStairGhost(KeyBlock kb, int rate, int inst)
    {
        if (stairBeadMat == null) stairBeadMat = Fx.Alpha(IconFactory.GetTexture("ring"), Color.white);
        int n = Mathf.Clamp(kb.stairSteps, ProjectConfig.StairMinSteps, ProjectConfig.StairMaxSteps);
        int pass = Mathf.Max(rate, Mathf.RoundToInt(Mathf.Max(1, kb.bars) * GlobalClock.BeatsPerBar * ProjectConfig.TicksPerBeat));
        int fit = Mathf.Clamp(pass / Mathf.Max(1, rate), 1, n), first = kb.stairLead ? n - fit : 0;   // as SongManager.StairPath
        float bs = 0.4f * AudioCube.SizeOf(rate / (float)ProjectConfig.TicksPerBeat);
        int k = 0;
        for (int i = first; i < first + fit; i++)
        {
            var t = kb.GetTile(i, 0);
            if (t == null) continue;
            while (stairBeads.Count <= k)
            {
                var b = new GameObject("StairGhostBead");
                b.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
                var mr = b.AddComponent<MeshRenderer>();
                mr.sharedMaterial = stairBeadMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                stairBeads.Add(b.transform);
            }
            var bt = stairBeads[k++];
            if (!bt.gameObject.activeSelf) bt.gameObject.SetActive(true);
            bt.position = t.Top + Vector3.up * 0.05f;
            bt.localScale = new Vector3(bs, 1f, bs);
        }
        for (; k < stairBeads.Count; k++) if (stairBeads[k] != null && stairBeads[k].gameObject.activeSelf) stairBeads[k].gameObject.SetActive(false);
        float h = Rhythm.Hash01(Mathf.FloorToInt(Time.time * 12f), 3, 19);
        stairBeadMat.SetColor(PreviewColorId, Palette.A(Instruments.Colors[Mathf.Clamp(inst, 0, Instruments.Count - 1)], 0.55f + 0.35f * h));
    }

    void HideStairGhost() { for (int i = 0; i < stairBeads.Count; i++) if (stairBeads[i] != null && stairBeads[i].gameObject.activeSelf) stairBeads[i].gameObject.SetActive(false); }

    void BuildHandGhost()
    {
        var go = new GameObject("HandHologram");
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RoundedBox(Vector3.one, 0.14f, 4);
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("DeaCube/Hologram");
        handGhostMat = sh != null ? new Material(sh) : Fx.Additive(IconFactory.GetTexture("white"), Color.white, 0.8f);
        mr.sharedMaterial = handGhostMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        handGhost = go.transform;
        go.SetActive(false);
    }

    bool handGhostRound;
    void SetHandGhostShape(bool round)
    {
        if (handGhost == null || handGhostRound == round) return;
        handGhostRound = round;
        var mf = handGhost.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = AudioCube.BodyMeshFor(round);
    }

    void HideHandGhost()
    {
        handGhostTile = null; HandGhostTicks = -1; HandGhostSize = 0f;
        if (handGhost != null && handGhost.gameObject.activeSelf) handGhost.gameObject.SetActive(false);
        HideStairGhost();
    }

    // ---- v6: a pattern in the hand shows its adapted copy on the grid under the pointer (ghost beads + a dashed line: the exact notes a click pastes)
    KeyBlock patternTarget; int patternKey = int.MinValue; Color patternColor = Color.white;
    readonly List<TileInteraction> patternTiles = new List<TileInteraction>();
    readonly List<Vector3> patternPos = new List<Vector3>();
    LineRenderer patternLine; Material patternLineMat, patternBeadMat;
    readonly List<Transform> patternBeads = new List<Transform>();
    /// <summary>The island a click would paste the held pattern on (null when none is hovered or the pattern cannot go there).</summary>
    public KeyBlock HoverPasteTarget => Hand == HandKind.Pattern ? patternTarget : null;
    /// <summary>The tiles the pattern ghost shows (in node order; tests).</summary>
    public IReadOnlyList<TileInteraction> PatternGhostTiles => patternTiles;

    void UpdatePatternGhost(PointerIn p, PickResult r)
    {
        KeyBlock kb = null;
        if (Hand == HandKind.Pattern && !p.overUI && !IsDrawing)
        {
            kb = PasteTargetOf(r);
            if (kb != null && !Clipboard.CanPasteOn(kb, PatternMode)) kb = null;
        }
        if (kb == null) { HidePatternGhost(); return; }
        int key; unchecked { key = (kb.GetInstanceID() * 397 + Clipboard.Version * 31 + kb.tiles.Count) * 7 + (int)PatternMode; }
        if (kb != patternTarget || key != patternKey) BuildPatternGhost(kb, key);
        AnimatePatternGhost();
    }

    void BuildPatternGhost(KeyBlock kb, int key)
    {
        patternTarget = kb; patternKey = key;
        patternTiles.Clear();
        var s = Clipboard.AdaptedFor(kb);
        if (s != null) for (int i = 0; i < s.xs.Length; i++) { var t = kb.GetTile(s.xs[i], s.zs[i]); if (t != null) patternTiles.Add(t); }
        if (patternLine == null)
        {
            var go = new GameObject("PatternGhostLine");
            patternLine = go.AddComponent<LineRenderer>();
            patternLine.useWorldSpace = true; patternLine.widthMultiplier = 0.075f;
            patternLine.numCapVertices = 3; patternLine.textureMode = LineTextureMode.Tile; patternLine.alignment = LineAlignment.View;
            patternLineMat = Fx.Flow(Color.white, 1.1f, 4.5f, 1.2f, 0f);
            patternLineMat.SetFloat("_SrcBlend", 1f); patternLineMat.SetFloat("_DstBlend", 10f);
            patternLine.material = patternLineMat;
            patternLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; patternLine.receiveShadows = false;
            patternBeadMat = Fx.Alpha(IconFactory.GetTexture("ring"), Color.white);
        }
        patternColor = Instruments.Colors[Mathf.Clamp(Clipboard.Instrument, 0, Instruments.Count - 1)];
        patternLineMat.SetColor(PreviewColorId, patternColor);
        while (patternBeads.Count < patternTiles.Count)
        {
            var b = new GameObject("PatternGhostBead");
            b.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
            var mr = b.AddComponent<MeshRenderer>();
            mr.sharedMaterial = patternBeadMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            patternBeads.Add(b.transform);
        }
        var durs = s != null ? s.durs : null;
        for (int i = 0; i < patternBeads.Count; i++)
        {
            bool on = i < patternTiles.Count;
            if (patternBeads[i].gameObject.activeSelf != on) patternBeads[i].gameObject.SetActive(on);
            if (!on) continue;
            float beats = durs != null && i < durs.Length ? durs[i] / (float)ProjectConfig.TicksPerBeat : 1f;
            float bs = (i == 0 ? 0.46f : 0.3f) * AudioCube.SizeOf(beats) * AudioCube.FootOf(patternTiles[i]);
            patternBeads[i].localScale = new Vector3(bs, 1f, bs);
        }
        patternLine.positionCount = patternTiles.Count >= 2 ? patternTiles.Count : 0;
        patternLine.enabled = patternTiles.Count >= 2;
    }

    /// <summary>The ghost follows its island (a belt ride, a tower) and flickers on twos like the hologram; repeated tiles fan out like real beads.</summary>
    void AnimatePatternGhost()
    {
        patternPos.Clear();
        for (int i = 0; i < patternTiles.Count; i++)
        {
            var t = patternTiles[i];
            int occ = 0, total = 0;
            for (int j = 0; j < patternTiles.Count; j++) if (patternTiles[j] == t) { if (j < i) occ++; total++; }
            Vector3 off = Vector3.zero;
            if (total > 1)
            {
                if (Keys(t.island)) off = new Vector3(0f, 0.02f * occ, ((total - 1) * 0.5f - occ) * AudioCube.RepeatStep);
                else { float a = (90f + 360f * occ / total) * Mathf.Deg2Rad; off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.22f; }
            }
            patternPos.Add(t.Top + off + Vector3.up * 0.06f);
        }
        for (int i = 0; i < patternPos.Count && i < patternBeads.Count; i++) patternBeads[i].position = patternPos[i];
        if (patternLine.enabled) for (int i = 0; i < patternPos.Count; i++) patternLine.SetPosition(i, patternPos[i] + Vector3.up * 0.02f);
        float h = Rhythm.Hash01(Mathf.FloorToInt(Time.time * 12f), 9, 17);
        patternBeadMat.SetColor(PreviewColorId, Palette.A(patternColor, 0.62f + 0.33f * h));
        patternLineMat.SetFloat(PreviewIntensityId, 0.85f + 0.3f * h);
    }

    void HidePatternGhost()
    {
        patternTarget = null; patternKey = int.MinValue; patternTiles.Clear();
        if (patternLine != null && patternLine.enabled) { patternLine.enabled = false; patternLine.positionCount = 0; }
        for (int i = 0; i < patternBeads.Count; i++) if (patternBeads[i].gameObject.activeSelf) patternBeads[i].gameObject.SetActive(false);
    }

    // ---- v6: a pasted cube stays hidden while its ghost beads fly, then materialises as they land
    struct PendingShow { public AudioCube cube; public float at; }
    readonly List<PendingShow> pendingShow = new List<PendingShow>();
    /// <summary>Hides <paramref name="c"/> (body and path) and materialises it after <paramref name="seconds"/> (the paste flight lands). v7 §21: pastes
    /// appear in place (no flight) — kept for callers that still delay an appearance.</summary>
    public void ShowAfter(AudioCube c, float seconds)
    {
        if (c == null) return;
        c.SetWorldVisible(false, false);
        pendingShow.Add(new PendingShow { cube = c, at = Time.unscaledTime + Mathf.Max(0f, seconds) });
    }

    void UpdatePendingShow()
    {
        for (int i = pendingShow.Count - 1; i >= 0; i--)
        {
            var e = pendingShow[i];
            if (e.cube == null) { pendingShow.RemoveAt(i); continue; }
            if (Time.unscaledTime < e.at) continue;
            pendingShow.RemoveAt(i);
            e.cube.SetWorldVisible(true, true);
            e.cube.Materialize();
        }
    }

    public void SelectInstrument(int index)
    {
        selectedInstrument = Mathf.Clamp(index, 0, Instruments.Count - 1);
        if (activeCube != null && !activeCube.IsOnMoon) activeCube.SetInstrument(selectedInstrument);
        OnInstrumentChanged?.Invoke(selectedInstrument);
        Onboarding.Notify(Onboarding.Ev.InstrumentPicked);
    }

    /// <summary>Recolour (re-instrument) the selected cube from the inspector (Moon cubes stay drums). One History entry.</summary>
    public void SetSelectedInstrument(int index)
    {
        if (selectedCube == null || selectedCube.IsOnMoon) return;
        index = Mathf.Clamp(index, 0, Instruments.Count - 1);
        if (selectedCube.instrument == index) return;
        selectedCube.SetInstrument(index);
        if (!CubeInspector.IsOpen) Fx.Burst(selectedCube.transform.position, selectedCube.Color, 10, 2f);   // the inspector bursts at its proxy instead
        History.Push();
    }

    /// <summary>Rebuild a cube from saved state (undo/redo, load, structural edits). moon ≥ 0 ⇒ a drum cube on SongManager.Moons[moon].</summary>
    public AudioCube RestoreCube(CubeState s)
    {
        if (s == null || SongManager.I == null || s.xs == null || s.zs == null) return null;
        KeyBlock island; int instrument;
        if (s.moon >= 0)
        {
            if (s.moon >= SongManager.I.Moons.Count || SongManager.I.Moons[s.moon] == null) return null;
            island = SongManager.I.Moons[s.moon];
            instrument = DrumsInstrument;
        }
        else
        {
            if (s.measure < 0 || s.measure >= SongManager.I.Islands.Count || SongManager.I.Islands[s.measure] == null) return null;
            island = SongManager.I.Islands[s.measure];
            instrument = Mathf.Clamp(s.instrument, 0, Instruments.Count - 1);
        }
        var tiles = new List<TileInteraction>();
        List<int> kept = null;
        int count = Mathf.Min(s.xs.Length, s.zs.Length);
        for (int i = 0; i < count; i++)
        {
            // rows / cols clamped like a Rider's TileAt: a chord edit that drops a row (9th -> 7th) keeps the cube instead of truncating or losing it
            var t = island.GetTile(Mathf.Clamp(s.xs[i], 0, island.cols - 1), Mathf.Clamp(s.zs[i], 0, island.rows - 1));
            if (t == null) { if (kept == null) { kept = new List<int>(); for (int j = 0; j < i; j++) kept.Add(j); } continue; }
            tiles.Add(t);
            if (kept != null) kept.Add(i);
        }
        if (tiles.Count == 0) return null;
        if (kept != null || count != s.xs.Length || count != s.zs.Length) s = Aligned(s, kept, count);   // v4: mods / rests / durs stay with their nodes
        var c = SpawnCube(instrument);
        if (s.moon >= 0) c.Moon = island;
        c.SetPath(tiles);
        c.ApplySettings(s);
        c.PlaceOn(tiles[0], false);
        c.Finalize();
        return c;
    }

    /// <summary>A copy of <paramref name="s"/> whose per-node arrays keep only the nodes in <paramref name="kept"/> (null: the first
    /// <paramref name="count"/>), so stickers and lengths stay on their nodes when a restore skips a tile.</summary>
    static CubeState Aligned(CubeState s, List<int> kept, int count)
    {
        if (kept == null) { kept = new List<int>(count); for (int i = 0; i < count; i++) kept.Add(i); }
        var mods = s.ModsOrDerived();
        var o = new CubeState
        {
            instrument = s.instrument, measure = s.measure, step = s.step, gate = s.gate, mode = s.mode, volume = s.volume, muted = s.muted,
            moon = s.moon, hits = s.hits, rot = s.rot, mask = s.mask, octave = s.octave, reverse = s.reverse, phase = s.phase, follow = s.follow,
            echo = s.echo, shimmer = s.shimmer, rider = s.rider, seed = s.seed, twinOf = s.twinOf, id = s.id, voice = s.voice,
            layer = s.layer, echoOf = s.echoOf,   // SPEC v7 §2.1
            climb = s.climb, sphere = s.sphere,
            xs = new int[kept.Count], zs = new int[kept.Count], rests = new bool[kept.Count], mods = new int[kept.Count],
            durs = s.durs != null && s.durs.Length == s.xs.Length ? new int[kept.Count] : null,
            bend = s.bend != null && s.bend.Length == s.xs.Length ? new int[kept.Count] : null
        };
        for (int j = 0; j < kept.Count; j++)
        {
            int i = kept[j];
            o.xs[j] = s.xs[i]; o.zs[j] = s.zs[i];
            o.mods[j] = i < mods.Length ? mods[i] : 0; o.rests[j] = o.mods[j] == 1;
            if (o.durs != null) o.durs[j] = s.durs[i];
            if (o.bend != null) o.bend[j] = s.bend[i];   // v7 (H): a harmony cube's bends stay on their nodes
        }
        if (o.seed == 0) o.seed = s.SeedOrDerived();   // the derived seed hashes the node count: keep the original one
        return o;
    }

    /// <summary>Tests: runs the hotkey dispatch once (use with KeyShim.Sim).</summary>
    public void RunHotkeysForTest() { HandleHotkeys(); }

    void HandleHotkeys()
    {
        if (InputUtil.TypingInField) return;
        bool inspecting = CubeInspector.IsOpen && selectedCube != null;
        for (int i = 0; i < 10 && i < Instruments.Count && inspecting; i++)
        {
            // v3: the number keys recolour the inspected cube (SPEC v3 §2.3); v6: outside the inspector the digits pick up / put down the hand (UIManager, U2)
            var key = i < 9 ? KeyCode.Alpha1 + i : KeyCode.Alpha0;
            if (KeyShim.Down(key)) SetSelectedInstrument(i);
        }
        if (KeyShim.Down(KeyCode.Escape))
        {
            if (IsDrawing) CancelPath();                                                          // v4 R3: the hologram dissolves (v6: the hand keeps the cube)
            else if (CubeInspector.IsOpen) { /* the inspector reads Esc (a ring closes first) */ }
            else if (!islandDrag.Busy)
            {
                if (GridSelection.Any) escSelFrame = Time.frameCount;                              // v7 (§16.1): the grid selection clears first (one thing per Esc)
                else if (selectedCube != null) Deselect();
                else if (Hand != HandKind.Empty) escHandFrame = Time.frameCount;                  // v6: LateUpdate puts the hand down if nothing else took this Esc
                else escDismissFrame = Time.frameCount;                                           // v4 R4: LateUpdate dismisses the focus loop if nothing else took this Esc
            }
        }
        if (KeyShim.Down(KeyCode.Return) || KeyShim.Down(KeyCode.KeypadEnter)) FinishPath();
        // v7 (U2): with Shift-selected grids Delete belongs to UIManager (SongOps.DeleteGrids); order: a draft > the inspector > the grids > a cube
        if (KeyShim.Down(KeyCode.Delete) || KeyShim.Down(KeyCode.Backspace))
        {
            if (IsDrawing) RemoveLastNode();
            else if (CubeInspector.IsOpen) CubeInspector.DeleteCurrent();
            else if (DrawPhrase != null && Hand == HandKind.Cube) RemoveLastPhraseNote();   // v7 §19.1: a phrase being drawn gives back its last note (and the growth)
            else if (!GridSelection.Any) DeleteSelected();
        }
        // v7 §19.2: Space while drawing = hear it (the loop over the draft's grid / phrase on / off); SequenceMaster's play / pause skips it (OwnsSpace)
        if (OwnsSpace && !InputUtil.Cmd && KeyShim.Down(KeyCode.Space)) ToggleDrawLoop();
        if ((IsDrawing || Hand == HandKind.Cube) && !CubeInspector.IsOpen && !InputUtil.Cmd)
        {
            // v7 (§15): the size of the NEXT node (the brush) — [ shorter, ] longer, . dotted, each auditioned; a node already placed never changes
            // (v4 resized the latest node). The inspector keeps these keys (the necklace, the selected note's length).
            if (KeyShim.Down(KeyCode.LeftBracket)) StepBrush(-1);
            if (KeyShim.Down(KeyCode.RightBracket)) StepBrush(1);
            if (KeyShim.Down(KeyCode.Period) || KeyShim.Down(KeyCode.KeypadPeriod)) ToggleBrushDotted();
        }
        if (InputUtil.Cmd && KeyShim.Down(KeyCode.Z))
        {
            if (IsDrawing) { if (!InputUtil.Shift) UndoDraftNode(); }   // S2: undo trims the draft; redo waits until the draft ends
            else { if (islandDrag.Busy) islandDrag.Cancel(); if (strokeIsland >= 0) EndPhraseStroke(); if (InputUtil.Shift) History.Redo(); else History.Undo(); }   // S6
        }
        if (InputUtil.Cmd && KeyShim.Down(KeyCode.Y) && !IsDrawing) { if (islandDrag.Busy) islandDrag.Cancel(); History.Redo(); }
        if (InputUtil.Cmd && KeyShim.Down(KeyCode.S)) { if (SongIO.Save()) UIManager.Toast("check", Palette.Ok); }
        // N (add a Moon) and G (stamp; hold = all) are handled by UIManager.UpdateHotkeys (Appendix A): one owner per key.
    }

    // ---------------- stamps (SPEC §3.1 #16)
    /// <summary>Copies the selected cube (settings, stickers, lengths, sound) onto <paramref name="target"/>. v6: ADAPTED like a paste
    /// (Clipboard.Adapt: the shape kept by voice leading on another chord, a melody moved by scale steps; drums as they are) instead of the v3 raw
    /// grid offsets. Returns the new cube (null if nothing to stamp). The caller pushes History.</summary>
    public AudioCube StampTo(KeyBlock target)
    {
        var c = StampInternal(target);
        if (c != null) SequenceMaster.RecalculateTimeline();
        return c;
    }

    /// <summary>Stamps the selected cube onto every other island. The caller pushes History.</summary>
    public void StampToAll()
    {
        if (selectedCube == null || SongManager.I == null) return;
        int n = 0;
        foreach (var kb in new List<KeyBlock>(SongManager.I.Islands)) if (StampInternal(kb) != null) n++;
        if (n > 0) { SequenceMaster.RecalculateTimeline(); AudioPool.UI(ProceduralAudio.Chime(), 0.3f); }
    }

    AudioCube StampInternal(KeyBlock target)
    {
        var src = selectedCube;
        if (src == null || target == null || target.IsMoon || src.IsOnMoon || src.nodes.Count == 0 || target == src.Island || target.cols <= 0 || target.rows <= 0) return null;
        if (src.IsDrums && target.IsKeyboard) return null;   // a drum pattern has no keys to go to
        var s = src.ToState();
        s.measure = SongManager.I != null ? Mathf.Max(0, SongManager.I.Islands.IndexOf(target)) : target.measureIndex;
        s.id = 0; s.seed = 0; s.twinOf = -1; s.moon = -1;
        int[] xs, zs;
        if (Clipboard.Adapt(Clipboard.SourceOf(src.nodes, src.Island, src.instrument, src.HasDurations ? src.durs : null), target, out xs, out zs)) { s.xs = xs; s.zs = zs; }
        else for (int i = 0; i < s.xs.Length; i++) { s.xs[i] = Mathf.Clamp(s.xs[i], 0, target.cols - 1); s.zs[i] = Mathf.Clamp(s.zs[i], 0, target.rows - 1); }
        var c = RestoreCube(s);
        if (c != null) AppearHere(c);   // v7 §21: it appears on its grid (v6 flew its beads over from the source)
        return c;
    }

    // ------------------------------------------------------------------ v7 the wheel = the brush (SPEC v7 §15; package D)
    // "make them select the size of the cube THEN place it down": the mouse wheel over a grid (a tile, a platform, a cube on one) while a cube is in the
    // hand or a path is drawn steps the brush — one size per notch (a trackpad's small deltas add up), auditioned — and the camera does not zoom
    // meanwhile (OrbitCamera skips its wheel while OwnsWheel; Shift + wheel stays its sideways pan).
    /// <summary>v7: true this frame while the wheel belongs to the brush (a cube in the hand or a draft, the pointer over a grid, not over the HUD, no
    /// Shift). PathManager.Update runs before OrbitCamera.LateUpdate.</summary>
    public static bool OwnsWheel { get; private set; }
    /// <summary>v7: brush steps the wheel made (tests).</summary>
    public static int WheelSteps;
    /// <summary>v7: minimum seconds between two wheel steps (a trackpad swipe does not race through the sizes).</summary>
    public const float WheelGap = 0.08f;
    float wheelAcc, wheelStepT = -9f, simWheelDy;
    /// <summary>Tests: a wheel delta (+ = up = longer) the next pointer step reads as if the real wheel had turned.</summary>
    public void SimWheel(float dy) { simWheelDy += dy; }

    void UpdateWheel(PointerIn p, PickResult r, bool frame)
    {
        bool grid = r.tile != null || r.island != null || r.cube != null;
        bool own = !p.overUI && (Hand == HandKind.Cube || IsDrawing) && grid && !p.shift && !shiftArmed;
        OwnsWheel = own;
        float dy = simWheelDy; simWheelDy = 0f;
        if (frame && !SimOnly && !p.overUI) dy += Input.mouseScrollDelta.y;
        if (!own) { wheelAcc = 0f; return; }
        if (Mathf.Abs(dy) < 1e-4f) return;
        if (wheelAcc != 0f && Mathf.Sign(dy) != Mathf.Sign(wheelAcc)) wheelAcc = 0f;   // a reversal starts over
        wheelAcc = Mathf.Clamp(wheelAcc + dy, -1.5f, 1.5f);
        float now = Time.unscaledTime;
        if (Mathf.Abs(wheelAcc) < 0.99f || now - wheelStepT < WheelGap) return;
        int dir = wheelAcc > 0f ? 1 : -1;
        wheelAcc -= dir; wheelStepT = now; WheelSteps++;
        StepBrush(dir);
    }

    // ------------------------------------------------------------------ v7 the Shift marquee (SPEC v7 §16.1; package D)
    // "you should also be able to hold shift and drag over a section to then select all the grids in that section and like paste or delete or other
    // options": Shift + left-drag draws an ink-dashed screen rectangle selecting (live) every grid whose platform it touches — chord islands, keyboards,
    // stairs, phrases; Moons only when it covers their centre; Shift + click toggles the grid under the pointer, Shift + click on a section plinth
    // selects the whole section, Shift + click on the empty sea clears; Esc / a plain click on the empty sea clear too. While a Shift press is live
    // currentState = Selecting (OrbitCamera arms its left pan only when Idle: Shift + drag never pans) and nothing else of the press happens. The
    // selected grids' look is W's (SelectionFx); the ops are U2's selection bar / O's SongOps.
    bool shiftArmed, marqueeOn; Vector3 shiftPx; PickResult shiftPick; Rect marqueeRect; int marqueeSig;
    RectTransform marqueeRt; InkPainter marqueeInk;
    /// <summary>v7: a Shift + drag marquee is being drawn.</summary>
    public bool Marqueeing => shiftArmed && marqueeOn;
    /// <summary>v7: a Shift press is live (a click or a marquee until its release).</summary>
    public bool ShiftGesture => shiftArmed;
    /// <summary>v7: the marquee's screen rect (px; the last one after the release).</summary>
    public Rect MarqueeRect => marqueeRect;
    /// <summary>v7: marquees drawn and Shift + clicks so far (tests).</summary>
    public static int MarqueeCount, ShiftClicks;
    /// <summary>Tests: while set, SimOnly frames and SimPointer presses count as Shift held.</summary>
    public static bool SimShift;

    void BeginShift(PickResult r, PointerIn p)
    {
        shiftArmed = true; marqueeOn = false; shiftPx = p.pos; shiftPick = r;
        currentState = EditorState.Selecting;
        armKind = ArmKind.None; pressCube = null; deselectArmed = false;
        HideHandGhost(); HidePatternGhost();
    }

    void UpdateShift(PointerIn p, PickResult r)
    {
        if (!shiftArmed) return;
        if (p.held && !p.up)
        {
            if (!marqueeOn && (p.pos - shiftPx).magnitude > OrbitCamera.DragThresholdPx) { marqueeOn = true; MarqueeCount++; marqueeSig = int.MinValue; }
            if (marqueeOn) MarqueeTo(p.pos);
            return;
        }
        if (!p.up) return;   // kept until the release (an idle frame between a press and its release changes nothing)
        if (marqueeOn) MarqueeTo(p.pos);
        else ShiftClick(shiftPick, p.pos);
        EndShift();
    }

    void MarqueeTo(Vector3 pos)
    {
        marqueeRect = Rect.MinMaxRect(Mathf.Min(shiftPx.x, pos.x), Mathf.Min(shiftPx.y, pos.y), Mathf.Max(shiftPx.x, pos.x), Mathf.Max(shiftPx.y, pos.y));
        var grids = GridsInRect(marqueeRect);
        int sig = 17;
        unchecked { foreach (var kb in grids) sig = sig * 31 + kb.GetInstanceID(); sig = sig * 31 + grids.Count; }
        if (sig != marqueeSig) { marqueeSig = sig; GridSelection.Set(grids); }   // live: the grids light up as the rectangle reaches them (W's look)
        ShowMarquee(true);
    }

    void EndShift()
    {
        shiftArmed = false; marqueeOn = false;
        ShowMarquee(false);
        if (currentState == EditorState.Selecting) currentState = EditorState.Idle;
    }

    void CancelShift() { if (shiftArmed || marqueeOn) EndShift(); }

    /// <summary>A Shift + click: the grid under the pointer toggles (a tile, its platform / hub, a cube on it); else a section plinth selects its section;
    /// else (the empty sea) the selection clears.</summary>
    void ShiftClick(PickResult r, Vector3 pos)
    {
        ShiftClicks++;
        KeyBlock kb = r.tile != null ? r.tile.island : r.island;
        if (kb == null && r.cube != null) kb = r.cube.Moon != null ? r.cube.Moon : r.cube.Island;
        if (kb != null)
        {
            GridSelection.Toggle(kb);
            AudioPool.UI(ProceduralAudio.Tick(), 0.3f, GridSelection.Contains(kb) ? 1.3f : 0.9f);
            return;
        }
        if (cam == null) cam = Camera.main;
        int s = cam != null ? PlinthUnder(cam.ScreenPointToRay(pos)) : -1;
        if (s >= 0) { GridSelection.SelectSection(s); AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.2f); return; }
        GridSelection.Clear();
    }

    /// <summary>v7: the grids a screen rectangle (px) selects (§16.1): chord islands, keyboards, stairs and phrases whose platform — its top face, and
    /// the same face raised to its highest tile — the rectangle touches; Moons only when it covers their centre. Song order is GridSelection's.</summary>
    public List<KeyBlock> GridsInRect(Rect r)
    {
        var list = new List<KeyBlock>();
        var sm = SongManager.I;
        if (cam == null) cam = Camera.main;
        if (sm == null || cam == null) return list;
        foreach (var kb in sm.Islands) if (kb != null && kb.enabled && !kb.IsMoon && PlatformTouches(kb, r)) list.Add(kb);
        foreach (var m in sm.Moons)
        {
            if (m == null || !m.enabled) continue;
            Vector3 sp = cam.WorldToScreenPoint(m.VisualCenter);
            if (sp.z > 0f && r.Contains(new Vector2(sp.x, sp.y))) list.Add(m);
        }
        return list;
    }

    static readonly Vector2[] quad = new Vector2[4];
    bool PlatformTouches(KeyBlock kb, Rect r)
    {
        var tr = kb.transform;
        Vector3 c = tr.InverseTransformPoint(kb.VisualCenter);   // the platform centre in island space (its CenterOffset)
        float hw = kb.Width * 0.5f, hd = kb.Depth * 0.5f, top = -0.03f, high = top;
        int n = kb.tiles.Count, stride = Mathf.Max(1, n / 48);
        for (int i = 0; i < n; i += stride) { var t = kb.tiles[i]; if (t != null) high = Mathf.Max(high, tr.InverseTransformPoint(t.Top).y); }
        return FaceTouches(tr, c, hw, hd, top, r) || (high > top + 0.05f && FaceTouches(tr, c, hw, hd, high, r));
    }

    bool FaceTouches(Transform tr, Vector3 c, float hw, float hd, float y, Rect r)
    {
        for (int k = 0; k < 4; k++)
        {
            Vector3 lp = new Vector3(c.x + ((k == 1 || k == 2) ? hw : -hw), y, c.z + (k >= 2 ? hd : -hd));
            Vector3 sp = cam.WorldToScreenPoint(tr.TransformPoint(lp));
            if (sp.z <= 0f) return false;
            quad[k] = new Vector2(sp.x, sp.y);
        }
        return QuadTouchesRect(quad, r);
    }

    /// <summary>v7: a convex quad (screen px) and an axis-aligned rect overlap (separating axes: the rect's two, the quad's four edge normals).</summary>
    public static bool QuadTouchesRect(Vector2[] q, Rect r)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++) { x0 = Mathf.Min(x0, q[i].x); y0 = Mathf.Min(y0, q[i].y); x1 = Mathf.Max(x1, q[i].x); y1 = Mathf.Max(y1, q[i].y); }
        if (x1 < r.xMin || x0 > r.xMax || y1 < r.yMin || y0 > r.yMax) return false;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = q[i], b = q[(i + 1) % 4];
            Vector2 nrm = new Vector2(a.y - b.y, b.x - a.x);
            if (nrm.sqrMagnitude < 1e-8f) continue;
            float qa = float.MaxValue, qb = float.MinValue, ra = float.MaxValue, rb = float.MinValue;
            for (int j = 0; j < 4; j++) { float d = Vector2.Dot(q[j], nrm); qa = Mathf.Min(qa, d); qb = Mathf.Max(qb, d); }
            for (int j = 0; j < 4; j++)
            {
                var cr = new Vector2((j == 1 || j == 2) ? r.xMax : r.xMin, j >= 2 ? r.yMax : r.yMin);
                float d = Vector2.Dot(cr, nrm); ra = Mathf.Min(ra, d); rb = Mathf.Max(rb, d);
            }
            if (qb < ra || rb < qa) return false;
        }
        return true;
    }

    /// <summary>v7: the section whose plinth <paramref name="ray"/> hits first (B's SectionPlinth.Pick), -1 none.</summary>
    public static int PlinthUnder(Ray ray) => SectionPlinth.Pick(ray);

    void ShowMarquee(bool on)
    {
        if (!on) { if (marqueeRt != null && marqueeRt.gameObject.activeSelf) marqueeRt.gameObject.SetActive(false); return; }
        if (marqueeRt == null)
        {
            marqueeRt = InkUI.Node("Marquee", InkUI.OverlayRoot);
            InkUI.Stretch(marqueeRt);
            marqueeInk = InkPainter.Create(marqueeRt, "Dashes", Vector2.zero, PaintMarquee);
            InkUI.Stretch(marqueeInk.rectTransform);
            marqueeInk.raycastTarget = false; marqueeInk.Seed = 8821; marqueeInk.Boil = true;   // drawn on twos
        }
        if (!marqueeRt.gameObject.activeSelf) marqueeRt.gameObject.SetActive(true);
        marqueeRt.SetAsLastSibling();
        marqueeInk.Repaint();
    }

    /// <summary>The marquee: a faint paper wash inside an ink-dashed rounded rectangle, small ink corner ticks.</summary>
    void PaintMarquee(InkPainter g)
    {
        var rt = marqueeInk.rectTransform;
        Vector2 a, b;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, new Vector2(marqueeRect.xMin, marqueeRect.yMin), null, out a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, new Vector2(marqueeRect.xMax, marqueeRect.yMax), null, out b);
        var rr = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        if (rr.width < 2f || rr.height < 2f) return;
        g.RoundRect(rr, 7f, Palette.A(InkUI.Paper, 0.2f), 2.6f, Palette.A(InkUI.Ink, 0.92f), 11f, 7f);
        float k = Mathf.Min(14f, Mathf.Min(rr.width, rr.height) * 0.3f);
        Color ink = InkUI.Ink;
        g.Line(new Vector2(rr.xMin, rr.yMax), new Vector2(rr.xMin + k, rr.yMax), 3.2f, ink); g.Line(new Vector2(rr.xMin, rr.yMax), new Vector2(rr.xMin, rr.yMax - k), 3.2f, ink);
        g.Line(new Vector2(rr.xMax, rr.yMin), new Vector2(rr.xMax - k, rr.yMin), 3.2f, ink); g.Line(new Vector2(rr.xMax, rr.yMin), new Vector2(rr.xMax, rr.yMin + k), 3.2f, ink);
    }

    // ------------------------------------------------------------------ v7 melody phrases (SPEC v7 §14.4; package D)
    // "a 2 measure melody shouldnt be two separate grids" / "let it autoexpand as they reach the limit": on a PHRASE (kind 4: time along x, the key's
    // scale along z) a press with a cube in the hand places a note of the brush length at the cell's time on its row and starts a STROKE; the drag
    // paints on (a note in each cell reached at / after the newest note's end; inside the newest note another row moves it: the drag draws its pitch);
    // the held group's melody cube on the phrase is its notes in time order (PhraseRoll: gaps are rests; created on the first note); a note placed over
    // others replaces what it covers; a note reaching into the last half measure grows the phrase first (SongOps.GrowPhrase: a rebuild — the phrase and
    // the cube are found again by index / id); the release pushes ONE History entry for the whole stroke. The phrase loops while it is drawn
    // (FocusLoop.BeginPhrase). A right-click (a cube in the hand) erases the note under it (one History entry).
    int strokeIsland = -1, strokeCubeId, strokeInst, strokeLast = -1, strokeCol = -1, strokeOffset;
    List<PhraseNote> strokeNotes; bool strokeChanged; Vector3 strokePx;
    /// <summary>v7: a phrase stroke is in progress (from the press until its release).</summary>
    public bool PhraseStroking => strokeIsland >= 0;
    /// <summary>v7: the notes of the stroke in progress (null when none; tests).</summary>
    public IReadOnlyList<PhraseNote> StrokeNotes => strokeNotes;
    /// <summary>v7: notes placed, phrase growths, strokes and erases so far (tests).</summary>
    public static int PhraseNotesPlaced, PhraseGrows, PhraseStrokes, PhraseErases;

    /// <summary>v7: the melody cube of group <paramref name="instrument"/> on phrase <paramref name="kb"/> — the one drawing edits (never an octave copy,
    /// echo or harmony cube); null when the group has none there yet.</summary>
    public static AudioCube PhraseCubeOf(KeyBlock kb, int instrument)
    {
        if (kb == null) return null;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && c.Island == kb && c.instrument == instrument && c.layer == 0 && c.echoOf < 0) return c;
        return null;
    }

    void BeginPhraseStroke(TileInteraction tile, Vector3 px)
    {
        var sm = SongManager.I; var kb = tile.island;
        int idx = sm != null ? sm.Islands.IndexOf(kb) : -1;
        if (idx < 0) return;
        Deselect();
        int inst = Mathf.Clamp(selectedInstrument, 0, Instruments.Count - 1);
        var cube = PhraseCubeOf(kb, inst);
        strokeIsland = idx; strokeInst = inst; strokeCubeId = cube != null ? cube.id : 0; strokeCol = kb.column; strokeOffset = kb.phraseOffset;
        strokeNotes = cube != null ? PhraseRoll.NotesOf(cube) : new List<PhraseNote>();
        strokeChanged = false; strokeLast = -1; strokePx = px; dragging = true;
        PhraseStrokes++;
        MelodyLine.Ensure();   // the card draws the phrase's melody
        DurationPicker.Ensure();   // the draft row (sizes, hear it, n / m beats, expand) floats over the phrase
        if (sessionCol != kb.column || sessionOffset != kb.phraseOffset || sessionPhrase < 0)
        {
            // v7 §19: a phrase drawing session begins — quiet (no loop by itself; a playing song pauses), its length remembered for the unexpand
            sessionPhrase = idx; sessionCol = kb.column; sessionOffset = kb.phraseOffset; sessionBeats0 = kb.phraseBeats;
            drawLoop = false; CapacityReached = false;
            if (AutoHand) FocusLoop.BeginPhrase(idx); else QuietStart();
        }
        StrokeAt(tile, true);
    }

    /// <summary>The phrase the stroke draws on (found again after a rebuild: the same index, else the phrase at the same column and offset).</summary>
    KeyBlock StrokeKb()
    {
        var sm = SongManager.I;
        if (sm == null || strokeIsland < 0) return null;
        if (strokeIsland < sm.Islands.Count) { var kb = sm.Islands[strokeIsland]; if (kb != null && kb.IsPhrase && kb.column == strokeCol && kb.phraseOffset == strokeOffset) return kb; }
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb != null && kb.IsPhrase && kb.column == strokeCol && kb.phraseOffset == strokeOffset) { strokeIsland = i; return kb; }
        }
        return null;
    }

    AudioCube StrokeCube(KeyBlock kb)
    {
        if (strokeCubeId == 0 || kb == null) return null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == strokeCubeId && c.Island == kb) return c;
        return null;
    }

    /// <summary>A note of the brush length at <paramref name="tile"/>'s time on its row (the press; a cell the drag reached at / after the newest note's
    /// end); a drag inside the newest note on another row moves that note there.</summary>
    void StrokeAt(TileInteraction tile, bool press)
    {
        var kb = StrokeKb();
        if (kb == null || tile == null || tile.island != kb || strokeNotes == null) return;
        int grid = Mathf.Max(1, kb.phraseGrid), start = tile.gridX * grid, row = tile.gridZ;
        if (!press && strokeLast >= 0 && strokeLast < strokeNotes.Count)
        {
            var last = strokeNotes[strokeLast];
            if (start < last.End)
            {
                if (start >= last.start && row != last.row)
                {
                    last.row = row; strokeNotes[strokeLast] = last;
                    ApplyStroke(kb);
                    var mt = kb.GetTile(last.start / grid, row);
                    if (mt != null) SoundNote(mt, last.len);
                }
                return;
            }
        }
        int len = brushTicks;
        bool grew = false;
        int want = PhraseRoll.GrowBeats(kb.phraseBeats, start + len, GlobalClock.BeatsPerBar, ProjectConfig.PhraseMaxBeats);
        // v7 §19.1: auto-grow only while the grown phrase stays inside its section (past it: EXPAND); §17.2: O lands it on ½, 1, 2, 4, 8 measures
        if (want > kb.phraseBeats && (AutoHand || PhraseFitsSection(kb, SongOps.GrowSize(want))))
        {
            int before = kb.phraseBeats;
            kb = GrowStroke(kb, want);
            if (kb == null) return;
            grew = kb.phraseBeats > before;
        }
        int total = PhraseRoll.TicksOf(kb);
        if (start >= total) return;
        if (start + len > total)
        {
            if (!AutoHand) { RefuseCapacity(kb); return; }   // v7 §19.1: a note past the phrase that cannot grow is refused ("capacity reached")
            len = total - start;   // the pre-v6 suites: the note stops at the end
        }
        if (len < PhraseRoll.MinNoteTicks) return;
        CapacityReached = false;
        bool changed = PhraseRoll.Place(strokeNotes, new PhraseNote(start, row, len));
        strokeLast = PhraseRoll.IndexOf(strokeNotes, start, row, len);
        if (changed) PhraseNotesPlaced++;
        if (changed || grew) ApplyStroke(kb);
        var nt = kb.GetTile(start / grid, row);
        if (nt != null) { SoundNote(nt, len); nt.Flash(Instruments.Colors[Mathf.Clamp(strokeInst, 0, Instruments.Count - 1)], 0.8f); }
    }

    void SoundNote(TileInteraction t, int ticks)
    {
        var c = StrokeCube(t.island);
        t.PlayPress(strokeInst, c != null ? c.voice : -1, 0, Mathf.Clamp(AuditionSeconds(ticks), 0.18f, 1.2f));
    }

    /// <summary>Auto-grow mid-stroke (O's SongOps.GrowPhrase: no History of its own, a rebuild); the phrase is found again; the roll's growth is B's
    /// / K's look, the soft tick is here. Returns the phrase now (the old one when it did not grow).</summary>
    KeyBlock GrowStroke(KeyBlock kb, int beats)
    {
        bool grew = false;
        try { grew = SongOps.GrowPhrase(kb, beats); }
        catch (Exception e) { Debug.LogWarning("[PathManager] GrowPhrase threw: " + e.Message); }
        var now = StrokeKb();
        if (!grew) return now;
        Physics.SyncTransforms();   // the rebuilt roll's cells answer the very next ray (the drag goes on in this frame)
        PhraseGrows++;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 1.25f);
        if (now != null) sessionPhrase = strokeIsland;
        if (now != null && FocusLoop.Active && FocusLoop.Phrase >= 0) FocusLoop.BeginPhrase(strokeIsland);   // a loop on it follows the longer phrase
        return now;
    }

    /// <summary>The stroke's notes into the held group's cube on the phrase (created on the first note, removed when none is left). No History: the
    /// stroke pushes once when it ends.</summary>
    bool ApplyStroke(KeyBlock kb)
    {
        if (kb == null || strokeNotes == null) return false;
        strokeChanged = true;
        int[] xs, zs, mods, durs;
        PhraseRoll.Encode(strokeNotes, PhraseRoll.TicksOf(kb), kb.phraseGrid, kb.cols, out xs, out zs, out mods, out durs);
        var cube = StrokeCube(kb);
        if (xs.Length == 0)
        {
            if (cube != null) RemoveCubeQuiet(cube);
            strokeCubeId = 0;
            SequenceMaster.RecalculateTimeline();
            return true;
        }
        var tiles = new List<TileInteraction>(xs.Length);
        for (int i = 0; i < xs.Length; i++)
        {
            var t = kb.GetTile(xs[i], zs[i]);
            if (t == null) { Debug.LogWarning("[PathManager] phrase cell " + xs[i] + "," + zs[i] + " missing (" + kb.cols + "x" + kb.rows + ")"); return false; }
            tiles.Add(t);
        }
        if (cube == null)
        {
            var st = new CubeState
            {
                instrument = strokeInst, measure = strokeIsland, xs = xs, zs = zs, mods = mods, durs = durs, rests = new bool[xs.Length],
                volume = 1f, moon = -1, voice = Instruments.BrushVoiceOf(strokeInst)
            };
            for (int i = 0; i < xs.Length; i++) st.rests[i] = mods[i] == 1;
            cube = RestoreCube(st);
            if (cube == null) return false;
            cube.Materialize();
            strokeCubeId = cube.id;
        }
        else cube.SetPathAndMods(tiles, new List<int>(mods), durs);
        SequenceMaster.RecalculateTimeline();
        return true;
    }

    void EndPhraseStroke()
    {
        if (strokeIsland < 0) return;
        bool changed = strokeChanged;
        strokeIsland = -1; strokeNotes = null; strokeLast = -1; strokeChanged = false; dragging = false;
        if (changed) { SequenceMaster.RecalculateTimeline(); History.Push(); }
    }

    /// <summary>v7: a right-click with a cube in the hand on a phrase cell erases the held group's note sounding there on that row (one History entry).</summary>
    bool ErasePhraseNote(TileInteraction tile)
    {
        var sm = SongManager.I; var kb = tile != null ? tile.island : null;
        if (sm == null || kb == null || !kb.IsPhrase) return false;
        int inst = Mathf.Clamp(selectedInstrument, 0, Instruments.Count - 1);
        var cube = PhraseCubeOf(kb, inst);
        if (cube == null) return false;
        var notes = PhraseRoll.NotesOf(cube);
        int tick = tile.gridX * Mathf.Max(1, kb.phraseGrid), at = -1;
        for (int i = 0; i < notes.Count; i++) if (notes[i].row == tile.gridZ && tick >= notes[i].start && tick < notes[i].End) { at = i; break; }
        if (at < 0) return false;
        notes.RemoveAt(at);
        strokeIsland = sm.Islands.IndexOf(kb); strokeInst = inst; strokeCubeId = cube.id; strokeCol = kb.column; strokeOffset = kb.phraseOffset;
        strokeNotes = notes; strokeLast = -1;
        ApplyStroke(kb);
        EndPhraseStroke();
        PhraseErases++;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f);
        return true;
    }

    /// <summary>Removes a cube without a History entry (the gesture owner pushes once).</summary>
    void RemoveCubeQuiet(AudioCube c)
    {
        if (c == null) return;
        if (selectedCube == c) Deselect();
        if (candidate == c) SetCandidate(null);
        if (hoverCube == c) { hoverCube.SetHover(false); hoverCube = null; }
        c.Delete(true);
        SequenceMaster.Unregister(c);
    }

    /// <summary>v7: the phrase cell a ray points at through a gap between cells (the cells stand on a regular grid: time pitch x, row pitch z, read from
    /// the corner cells); null off the roll (its margin and the tiny piano stay the island's handle).</summary>
    public static TileInteraction PhraseCellUnder(KeyBlock kb, Ray ray)
    {
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return null;
        var a = kb.GetTile(0, 0); var b = kb.GetTile(kb.cols - 1, 0); var c = kb.GetTile(0, kb.rows - 1);
        if (a == null || b == null || c == null) return null;
        var tr = kb.transform;
        Vector3 la = tr.InverseTransformPoint(a.Top), lb = tr.InverseTransformPoint(b.Top), lc = tr.InverseTransformPoint(c.Top);
        float cw = kb.cols > 1 ? (lb.x - la.x) / (kb.cols - 1) : 1f, rp = kb.rows > 1 ? (lc.z - la.z) / (kb.rows - 1) : 1f;
        if (Mathf.Abs(cw) < 1e-4f || Mathf.Abs(rp) < 1e-4f) return null;
        Vector3 o = tr.InverseTransformPoint(ray.origin), d = tr.InverseTransformDirection(ray.direction);
        if (Mathf.Abs(d.y) < 1e-5f) return null;
        float u = ((la.y + lc.y) * 0.5f - o.y) / d.y;
        if (u <= 0f) return null;
        Vector3 q = o + d * u;
        float fx = (q.x - la.x) / cw, fz = (q.z - la.z) / rp;
        if (fx < -0.5f || fz < -0.5f || fx > kb.cols - 0.5f || fz > kb.rows - 0.5f) return null;
        return kb.GetTile(Mathf.Clamp(Mathf.RoundToInt(fx), 0, kb.cols - 1), Mathf.Clamp(Mathf.RoundToInt(fz), 0, kb.rows - 1));
    }

    // ------------------------------------------------------------------ v7 capacity + quiet drawing (SPEC v7 §19; package D)
    int draftCol = -1, draftFloor = -1;                    // the draft's column and the bars auto-unexpand never goes below (its bars at the start)
    int sessionPhrase = -1, sessionCol = -1, sessionOffset, sessionBeats0;   // the phrase being drawn (a cube in the hand) and its length then
    bool drawLoop;                                         // the loop Space / the draft row's button started (never by itself)
    /// <summary>v7 §19.1: the last note was refused (the grid is full and cannot grow by itself): the draft row says "capacity reached" and offers
    /// EXPAND. Cleared by the next note that fits, an expand, the drawing's end.</summary>
    public bool CapacityReached { get; private set; }
    /// <summary>v7: capacity refusals, auto-expands, auto-unexpands, draw-loop toggles and explicit expands so far (tests).</summary>
    public static int CapacityRefusals, AutoExpands, AutoUnexpands, DrawLoopToggles, Expands;
    /// <summary>v7 §19.2: Space belongs to the drawing (hear it) — a draft is in progress or a phrase is being drawn with a cube in the hand;
    /// SequenceMaster's play / pause skips Space meanwhile.</summary>
    public static bool OwnsSpace => I != null && (I.IsDrawing || (I.Hand == HandKind.Cube && I.DrawPhrase != null));
    /// <summary>v7: the loop Space / the draft row's button started is playing.</summary>
    public bool DrawLoopOn => drawLoop && FocusLoop.Active;
    /// <summary>v7: the phrase being drawn (its session: from the first stroke until the hand is put down or a press lands elsewhere), else null.</summary>
    public KeyBlock DrawPhrase
    {
        get
        {
            var sm = SongManager.I;
            if (sessionPhrase < 0 || sm == null) return null;
            if (sessionPhrase < sm.Islands.Count) { var kb = sm.Islands[sessionPhrase]; if (kb != null && kb.IsPhrase && kb.column == sessionCol && kb.phraseOffset == sessionOffset) return kb; }
            for (int i = 0; i < sm.Islands.Count; i++)
            {
                var kb = sm.Islands[i];
                if (kb != null && kb.IsPhrase && kb.column == sessionCol && kb.phraseOffset == sessionOffset) { sessionPhrase = i; return kb; }
            }
            return null;
        }
    }
    /// <summary>v7: the grid being drawn on — the draft's island, else the phrase being drawn — null when none.</summary>
    public KeyBlock DrawIsland => IsDrawing && Last != null ? Last.island : DrawPhrase;

    /// <summary>v7 §19.1: a path's capacity on <paramref name="kb"/> in beats — a chord grid / keyboard: its column's bars x beats per bar; a phrase: its
    /// length; 0 = no cap (Moons; stairs runners are derived).</summary>
    public static float CapacityOf(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (kb == null || sm == null || kb.IsMoon || kb.IsStairs) return 0f;
        if (kb.IsPhrase) return Mathf.Max(1, kb.phraseBeats);
        return sm.ColumnBars(kb.column) * Mathf.Max(1, GlobalClock.BeatsPerBar);
    }

    /// <summary>v7 §19.1: the beats the drawing uses now (a draft: its notes' lengths; a phrase: its held group's last note's end).</summary>
    public float DrawUsedBeats
    {
        get
        {
            if (IsDrawing && activeCube != null) return DraftBeats();
            var kb = DrawPhrase;
            if (kb == null) return 0f;
            var notes = strokeNotes;
            if (notes == null) { var c = PhraseCubeOf(kb, Mathf.Clamp(selectedInstrument, 0, Instruments.Count - 1)); notes = c != null ? PhraseRoll.NotesOf(c) : null; }
            int end = 0; if (notes != null) foreach (var n in notes) end = Mathf.Max(end, n.End);
            return end / (float)ProjectConfig.TicksPerBeat;
        }
    }
    /// <summary>v7 §19.1: the capacity of the grid being drawn on (beats; 0 none).</summary>
    public float DrawCapacityBeats => CapacityOf(DrawIsland);

    float DraftBeats()
    {
        if (activeCube == null) return 0f;
        float b = 0f;
        for (int i = 0; i < currentPathTiles.Count; i++) b += (i < activeCube.durs.Count ? activeCube.durs[i] : brushTicks) / (float)ProjectConfig.TicksPerBeat;
        return b;
    }

    /// <summary>A playing song pauses when a path begins (only the placed / hovered notes sound); a loop left from before is let go (its region only).</summary>
    void QuietStart()
    {
        if (FocusLoop.Active) FocusLoop.Release();
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
    }

    /// <summary>v7 §19.2 "unless they press a key or press ui": Space / the draft row's loop button — the loop over the draft's grid (its column) or the phrase
    /// being drawn, in context; again = off (the transport back as it was: paused).</summary>
    public void ToggleDrawLoop()
    {
        DrawLoopToggles++;
        if (drawLoop && FocusLoop.Active) { drawLoop = false; FocusLoop.Dismiss(); return; }
        var sm = SongManager.I;
        if (sm == null) return;
        if (IsDrawing && Last != null && Last.island != null)
        {
            if (Last.island.IsMoon) FocusLoop.Begin(sm.LitColumn); else FocusLoop.Begin(Last.island.column);
            drawLoop = FocusLoop.Active;
        }
        else if (DrawPhrase != null) { FocusLoop.BeginPhrase(sessionPhrase); drawLoop = FocusLoop.Active; }
        if (drawLoop) AudioPool.UI(ProceduralAudio.Blip(), 0.25f, 1.2f);
    }

    /// <summary>Room on <paramref name="kb"/> for a path of <paramref name="needBeats"/> in all (<paramref name="already"/> of them placed): fits, or the
    /// grid grows (K's live resize: no rebuild, no History) while its section stays within SectionBars measures; false = refused (capacity reached).</summary>
    bool GridRoom(KeyBlock kb, float needBeats, float already)
    {
        float cap = CapacityOf(kb);
        if (cap <= 0f || kb.IsPhrase || needBeats <= cap + 1e-4f) return true;
        var sm = SongManager.I;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar), col = kb.column;
        int want = Mathf.CeilToInt(needBeats / bpb - 1e-4f);
        if (sm != null && want <= 4 && sm.SectionBarsIfResized(col, want) <= ProjectConfig.SectionBars && sm.ResizeColumnLive(col, want))
        {
            AutoExpands++; CapacityReached = false;
            AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.3f);
            return true;
        }
        RefuseCapacity(kb);
        return false;
    }

    /// <summary>The draft may take <paramref name="addBeats"/> more (auto-expanding its grid); false = refused.</summary>
    bool DraftRoom(float addBeats)
    {
        if (AutoHand || activeCube == null || Last == null || Last.island == null) return true;   // the pre-v6 suites draw as before (no cap)
        float used = DraftBeats();
        if (!GridRoom(Last.island, used + addBeats, used)) return false;
        CapacityReached = false;
        return true;
    }

    /// <summary>Backspace: the grid shrinks back to the smallest length that holds the draft (never below its length when the draft began).</summary>
    void AutoUnexpand()
    {
        var sm = SongManager.I;
        CapacityReached = false;
        if (AutoHand || sm == null || activeCube == null || draftCol < 0 || draftCol >= sm.ColumnCount || draftFloor <= 0) return;
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        int want = Mathf.Max(draftFloor, Mathf.CeilToInt(DraftBeats() / bpb - 1e-4f));
        if (want < sm.ColumnBars(draftCol) && sm.ResizeColumnLive(draftCol, want)) AutoUnexpands++;
    }

    /// <summary>"if max is hit then it says capacity reached": a soft thud, the grid shakes, the draft row's caption + EXPAND chip, U1's tip.</summary>
    void RefuseCapacity(KeyBlock kb)
    {
        CapacityRefusals++;
        CapacityReached = true;
        AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 0.9f);
        if (kb != null) kb.Shake(0.14f);
        Onboarding.Notify(Onboarding.Ev.CapacityReached);
    }

    /// <summary>v7 §19.1 the EXPAND chip: one more measure for the grid being drawn on, beyond the auto limit (O's SongOps.ExpandGrid: a chord grid /
    /// keyboard live, a phrase through a rebuild; one History entry). The draft / the phrase drawing continues. True when it grew.</summary>
    public bool ExpandDrawGrid()
    {
        var kb = DrawIsland;
        if (kb == null) return false;
        bool phrase = kb.IsPhrase;
        bool ok = false;
        try { ok = SongOps.ExpandGrid(kb); } catch (Exception e) { Debug.LogWarning("[PathManager] ExpandGrid threw: " + e.Message); }
        if (!ok) return false;
        Expands++;
        if (phrase) Physics.SyncTransforms();
        CapacityReached = false;
        var sm = SongManager.I;
        if (!phrase && IsDrawing && draftCol >= 0 && sm != null) draftFloor = Mathf.Max(draftFloor, sm.ColumnBars(draftCol));   // the player's measure stays
        if (phrase) { var p = DrawPhrase; if (p != null) sessionBeats0 = Mathf.Max(sessionBeats0, p.phraseBeats); }
        return true;
    }

    /// <summary>v7 §19.1: a phrase grown to <paramref name="beats"/> would still end inside its section.</summary>
    static bool PhraseFitsSection(KeyBlock kb, int beats)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null) return false;
        float a, b; sm.PhraseSpan(kb, out a, out b);
        int s = sm.SectionOf(kb.column);
        return s >= 0 && a + beats <= sm.SectionEndBeat(s) + 1e-3f;
    }

    /// <summary>Backspace while drawing on a phrase: its held group's last note (in time) goes, and the phrase shrinks back to the smallest length that
    /// holds the rest (O's SongOps.ShrinkPhraseForDraft; never below its length when the drawing began). One History entry.</summary>
    public bool RemoveLastPhraseNote()
    {
        var kb = DrawPhrase; var sm = SongManager.I;
        if (kb == null || sm == null) return false;
        int inst = Mathf.Clamp(selectedInstrument, 0, Instruments.Count - 1);
        var cube = PhraseCubeOf(kb, inst);
        if (cube == null) return false;
        var notes = PhraseRoll.NotesOf(cube);
        if (notes.Count == 0) return false;
        notes.RemoveAt(notes.Count - 1);
        strokeIsland = sm.Islands.IndexOf(kb); strokeInst = inst; strokeCubeId = cube.id; strokeCol = kb.column; strokeOffset = kb.phraseOffset;
        strokeNotes = notes; strokeLast = -1;
        ApplyStroke(kb);
        int end = 0; foreach (var n in notes) end = Mathf.Max(end, n.End);
        int need = Mathf.Max(sessionBeats0, Mathf.CeilToInt(end / (float)ProjectConfig.TicksPerBeat - 1e-4f));
        if (!AutoHand && need < kb.phraseBeats)
        {
            bool shrank = false;
            try { shrank = SongOps.ShrinkPhraseForDraft(kb, need); } catch (Exception e) { Debug.LogWarning("[PathManager] ShrinkPhraseForDraft threw: " + e.Message); }
            if (shrank) { AutoUnexpands++; var p = DrawPhrase; if (p != null) strokeIsland = sessionPhrase; }
        }
        EndPhraseStroke();
        CapacityReached = false;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f);
        return true;
    }

    void EndPhraseSession()
    {
        if (sessionPhrase < 0) return;
        var kb = DrawPhrase;
        ClearFill(kb);
        sessionPhrase = -1; sessionCol = -1; CapacityReached = false;
    }

    // the grid's beat pips show the draft's fill (B: KeyBlock.SetDraftFill(beats, nextBeats); -1 clears)
    KeyBlock fillIsland; float fillUsed = -9f, fillNext = -9f;
    void UpdateFill()
    {
        var kb = DrawIsland;
        if (kb != fillIsland) { ClearFill(fillIsland); fillIsland = kb; fillUsed = -9f; }
        if (kb == null) return;
        float used = DrawUsedBeats, next = used + brushTicks / (float)ProjectConfig.TicksPerBeat;
        if (kb.IsPhrase && hoverTile != null && hoverTile.island == kb) next = Mathf.Max(used, (hoverTile.gridX * kb.phraseGrid + brushTicks) / (float)ProjectConfig.TicksPerBeat);
        if (Mathf.Abs(used - fillUsed) < 1e-4f && Mathf.Abs(next - fillNext) < 1e-4f) return;
        fillUsed = used; fillNext = next;
        kb.SetDraftFill(used, next);
    }
    void ClearFill(KeyBlock kb) { if (kb != null) kb.SetDraftFill(-1f, 0f); if (kb == fillIsland) { fillIsland = null; fillUsed = -9f; } }

    // ------------------------------------------------------------------ v7 §20.1 the keyboard extends under a melody (package D)
    int keyExtLow, keyExtHigh;   // octaves the draft in progress added below / above its keyboard
    /// <summary>v7 §20.1: octaves auto-added and given back so far (tests, U1's tip).</summary>
    public static int KeyExtends, KeyShrinks;
    /// <summary>v7: octaves the draft in progress added below / above its keyboard.</summary>
    public int DraftKeyOctavesBelow => keyExtLow;
    public int DraftKeyOctavesAbove => keyExtHigh;

    /// <summary>A note on a keyboard's lowest / highest key (a melody reaching the edge) extends it by an octave that way, live.</summary>
    void MaybeExtendKeys(TileInteraction t)
    {
        if (AutoHand || !IsDrawing || t == null || t.island == null || !t.island.IsKeyboard) return;
        var kb = t.island;
        int dir = t.gridX <= 0 ? -1 : (t.gridX >= kb.cols - 1 ? 1 : 0);
        if (dir == 0 || !KeysLive(true, kb, dir)) return;
        if (dir < 0) keyExtLow++; else keyExtHigh++;
        KeyExtends++;
        previewTo = null;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, dir > 0 ? 1.4f : 0.9f);
        Onboarding.Notify(Onboarding.Ev.KeysExtended);   // U1's one-time tip
    }

    /// <summary>The octaves this draft added: those that hold none of its notes go back (all of them when <paramref name="all"/>: a dropped draft).</summary>
    void GiveBackKeys(bool all)
    {
        if (keyExtLow == 0 && keyExtHigh == 0) return;
        var kb = Last != null ? Last.island : null;
        if (kb == null || !kb.IsKeyboard) { keyExtLow = 0; keyExtHigh = 0; return; }
        while (keyExtLow > 0)
        {
            int lowest = int.MaxValue; foreach (var t in currentPathTiles) if (t != null) lowest = Mathf.Min(lowest, t.gridX);
            if (!all && lowest < 12) break;   // the added octave holds a note
            if (!KeysLive(false, kb, -1)) break;
            keyExtLow--; KeyShrinks++;
        }
        while (keyExtHigh > 0)
        {
            int highest = int.MinValue; foreach (var t in currentPathTiles) if (t != null) highest = Mathf.Max(highest, t.gridX);
            if (!all && highest > kb.cols - 13) break;
            if (!KeysLive(false, kb, 1)) break;
            keyExtHigh--; KeyShrinks++;
        }
        keyExtLow = 0; keyExtHigh = 0;
    }

    /// <summary>v7: K's live keyboard range (tests; always there now).</summary>
    public static bool KeysLiveAvailable => true;
    /// <summary>K's SongManager.ExtendKeysLive / ShrinkKeysLive (no rebuild, no History: the keys are kept and re-indexed; a shrink refuses an octave a
    /// cube plays in, the draft included).</summary>
    static bool KeysLive(bool extend, KeyBlock kb, int dir)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null) return false;
        return extend ? sm.ExtendKeysLive(kb, dir) : sm.ShrinkKeysLive(kb, dir);
    }

    // ------------------------------------------------------------------ v7 stairs runners (SPEC v7 §5.3; package D)
    /// <summary>v7: runners placed so far (tests).</summary>
    public static int RunnersPlaced;
    static readonly int[] RunnerRates = { 6, 8, 12, 24 };
    /// <summary>v7 (§15 on stairs): the stairs rate (ticks per step: 6, 8, 12 or 24) a runner placed with a brush of <paramref name="ticks"/> runs at —
    /// the nearest on a log scale (a tie: the shorter).</summary>
    public static int RunnerRate(int ticks)
    {
        float l = Mathf.Log(Mathf.Max(1, ticks), 2f); int best = 12; float bd = float.MaxValue;
        foreach (int r in RunnerRates) { float d = Mathf.Abs(Mathf.Log(r, 2f) - l); if (d < bd - 1e-4f) { bd = d; best = r; } }
        return best;
    }

    /// <summary>v7 (§5.3): with a cube in the hand a click on any step places a RUNNER of group <paramref name="instrument"/> on stairs <paramref name="kb"/>
    /// — the whole run (SongManager.StairPath) — replacing that group's runner there (one per group per stairs); size first (§15): the run takes the
    /// brush's rate (<see cref="RunnerRate"/>; a new rate re-derives every runner there through SongOps.SetStair). ONE History entry. Returns the
    /// runner (after a rate change: its rebuilt twin), null when refused.</summary>
    public AudioCube PlaceRunner(KeyBlock kb, int instrument)
    {
        var sm = SongManager.I;
        if (kb == null || sm == null || !kb.IsStairs) return null;
        int idx = sm.Islands.IndexOf(kb);
        if (idx < 0) return null;
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        int rate = RunnerRate(brushTicks), col = kb.column;
        foreach (var old in new List<AudioCube>(SequenceMaster.Cubes))
            if (old != null && old.isFinalized && !old.IsOnMoon && old.Island == kb && old.instrument == instrument && old.layer == 0 && old.echoOf < 0) RemoveCubeQuiet(old);
        int[] xs, mods, durs;
        sm.StairPath(kb, out xs, out mods, out durs);
        if (xs.Length == 0) return null;
        var st = new CubeState
        {
            instrument = instrument, measure = idx, xs = xs, zs = new int[xs.Length], mods = mods, durs = durs, rests = new bool[xs.Length],
            volume = 1f, moon = -1, voice = Instruments.BrushVoiceOf(instrument)
        };
        for (int i = 0; i < xs.Length; i++) st.rests[i] = mods[i] == 1;
        var cube = RestoreCube(st);
        if (cube == null) { History.Push(); return null; }
        cube.Materialize();
        SequenceMaster.RecalculateTimeline();
        RunnersPlaced++;
        int id = cube.id;
        var top = kb.GetTile(xs[xs.Length - 1], 0);
        if (top != null) top.PlayPress(instrument, cube.voice, 0, Mathf.Clamp(AuditionSeconds(rate), 0.18f, 0.6f));
        bool pushed = false;
        if (rate != MeasureState.StairRate(kb.stairRate))
        {
            int h0 = historyEvents;
            try { SongOps.SetStair(idx, -1, 0, -1, rate, -1); } catch (Exception e) { Debug.LogWarning("[PathManager] SetStair threw: " + e.Message); }
            pushed = historyEvents != h0;
        }
        if (!pushed) History.Push();
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c;
        return cube;
    }

    // ------------------------------------------------------------------ v7 §21 cubes stay on their grid (package D's part)
    /// <summary>v7 §21: cubes the hand made appear in place so far (stamps; tests).</summary>
    public static int Appears;
    /// <summary>v7 §21: a cube made on another grid by the hand (a stamp) APPEARS there (W's Magic.Appear: a pop + sparkle on its grid) — never a
    /// flight across grids.</summary>
    public static void AppearHere(AudioCube c)
    {
        if (c == null) return;
        Appears++;
        Magic.Appear(c);
    }

    /// <summary>v7 (C, the keyboard's cat): a key a keyboard DRAFT just added and sounded (the first key, a click, the end of a glissando run), so the
    /// cat plays the notes being placed (empty-hand presses raise <see cref="OnTilePressed"/>).</summary>
    public static event Action<TileInteraction> OnDraftKey;
    /// <summary>v7: draft keys raised so far (tests).</summary>
    public static int DraftKeyEvents;
    static void RaiseDraftKey(TileInteraction t)
    {
        if (t == null || t.island == null || !t.island.IsKeyboard) return;
        DraftKeyEvents++;
        OnDraftKey?.Invoke(t);
    }

    // ---- legacy API
    public void OnTileClicked(TileInteraction clickedTile) { }
    public void FinalizePath() => FinishPath();
    public AudioSource GetSelectedAudioSource() => cubePrefabs[Mathf.Clamp(selectedInstrument, 0, cubePrefabs.Count - 1)].GetComponent<AudioSource>();
}

// ============================================================================================================================================
/// <summary>v7 (SPEC v7 §14.4; package D): one note of a melody phrase — its start and length in ticks from the phrase's start, its row (the
/// key's scale step: 0 = the lowest row) and its sticker (AudioCube mods: 0 none, 2 ghost, 3 accent …; never 1: a rest is a gap).</summary>
public struct PhraseNote
{
    public int start, row, len, mod;
    public int End => start + len;
    public PhraseNote(int start, int row, int len, int mod = 0) { this.start = start; this.row = row; this.len = len; this.mod = mod; }
    public override string ToString() => start + ":" + row + "x" + len;
}

/// <summary>
/// v7 (SPEC v7 §14.4; package D): a melody PHRASE's notes and the cube path that plays them. On a phrase (KeyBlock kind 4: time along x in cells
/// of phraseGrid ticks, the key's scale along z) a cube's path is its notes in TIME order: a note's node stands on cell (start / phraseGrid, row)
/// and lasts its length; a GAP between notes is a REST node (mod 1) on the cell where the gap starts (the previous note's row; the first note's for a
/// leading gap), so the cube walks left → right through time and waits in the rests; the path ENDS with rests up to the phrase's end, so it plays
/// once per pass and never loops inside it. Rest nodes last at most <see cref="MaxNodeTicks"/> (AudioCube.ClampTicks). A note placed over others
/// replaces what it covers (<see cref="Place"/>). "let it autoexpand as they reach the limit": <see cref="GrowBeats"/>. Pure (no Unity state
/// besides the constants): PathManager draws with it; O re-encodes on a length change (<see cref="Resize"/>); H / U may read notes (<see cref="NotesOf(AudioCube)"/>).
/// </summary>
public static class PhraseRoll
{
    public const int MaxNodeTicks = 192, MinNoteTicks = 3;

    /// <summary>The phrase's length in ticks (phraseBeats x TicksPerBeat).</summary>
    public static int TicksOf(KeyBlock kb) => kb == null ? 0 : Mathf.Max(1, kb.phraseBeats) * ProjectConfig.TicksPerBeat;

    /// <summary>The notes of a path: node times come from the lengths (a rest node is a gap), rows from zs.</summary>
    public static List<PhraseNote> Decode(IList<int> zs, IList<int> mods, IList<int> durs)
    {
        var notes = new List<PhraseNote>();
        if (zs == null || durs == null) return notes;
        int n = Mathf.Min(zs.Count, durs.Count), t = 0;
        for (int i = 0; i < n; i++)
        {
            int d = Mathf.Max(1, durs[i]);
            int m = mods != null && i < mods.Count ? mods[i] : 0;
            if (m != 1) notes.Add(new PhraseNote(t, zs[i], d, m));
            t += d;
        }
        return notes;
    }

    /// <summary>The notes a cube plays on its phrase (a cube without per-node lengths: its step length per node).</summary>
    public static List<PhraseNote> NotesOf(AudioCube c)
    {
        if (c == null || c.nodes.Count == 0) return new List<PhraseNote>();
        var zs = new List<int>(c.nodes.Count); var ds = new List<int>(c.nodes.Count);
        for (int i = 0; i < c.nodes.Count; i++)
        {
            zs.Add(c.nodes[i] != null ? c.nodes[i].gridZ : 0);
            ds.Add(Mathf.Max(1, Mathf.RoundToInt(c.DurBeats(i) * ProjectConfig.TicksPerBeat)));
        }
        return Decode(zs, c.mods, ds);
    }

    /// <summary>The notes a saved cube plays on its phrase (null lengths: a beat per node).</summary>
    public static List<PhraseNote> NotesOf(CubeState s)
    {
        if (s == null || s.zs == null) return new List<PhraseNote>();
        var ds = new int[s.zs.Length];
        for (int i = 0; i < ds.Length; i++) ds[i] = s.durs != null && i < s.durs.Length ? s.durs[i] : ProjectConfig.TicksPerBeat;
        return Decode(s.zs, s.ModsOrDerived(), ds);
    }

    /// <summary>The path that plays <paramref name="notes"/> over a phrase of <paramref name="totalTicks"/> (cells of <paramref name="grid"/> ticks,
    /// <paramref name="cols"/> of them): notes in time order, rests for the gaps and up to the end; notes past the end are dropped, a note crossing it
    /// is trimmed (dropped under <see cref="MinNoteTicks"/>). No notes: an empty path (the caller removes the cube).</summary>
    public static void Encode(IList<PhraseNote> notes, int totalTicks, int grid, int cols, out int[] xs, out int[] zs, out int[] mods, out int[] durs)
    {
        var X = new List<int>(); var Z = new List<int>(); var M = new List<int>(); var D = new List<int>();
        var sorted = new List<PhraseNote>();
        if (notes != null) foreach (var n in notes) if (n.len > 0 && n.start >= 0 && n.start < totalTicks) sorted.Add(n);
        sorted.Sort((a, b) => a.start.CompareTo(b.start));
        grid = Mathf.Max(1, grid); cols = Mathf.Max(1, cols);
        int t = 0, prevRow = sorted.Count > 0 ? sorted[0].row : 0, emitted = 0;
        foreach (var n in sorted)
        {
            int s0 = Mathf.Max(n.start, t);
            int e = Mathf.Min(n.End, totalTicks);
            if (e - s0 < MinNoteTicks) continue;
            if (s0 > t) Rest(X, Z, M, D, t, s0 - t, emitted == 0 ? n.row : prevRow, grid, cols);
            int left = e - s0, at = s0; bool first = true;
            while (left > 0)
            {
                int c = Mathf.Min(left, MaxNodeTicks);
                if (c < MinNoteTicks && D.Count > 0) { D[D.Count - 1] += c; break; }
                X.Add(Mathf.Clamp(at / grid, 0, cols - 1)); Z.Add(n.row); M.Add(first ? (n.mod == 1 ? 0 : n.mod) : 7); D.Add(c);   // a note past 192 ticks goes on as a tie
                at += c; left -= c; first = false;
            }
            t = e; prevRow = n.row; emitted++;
        }
        if (emitted > 0 && t < totalTicks) Rest(X, Z, M, D, t, totalTicks - t, prevRow, grid, cols);
        xs = X.ToArray(); zs = Z.ToArray(); mods = M.ToArray(); durs = D.ToArray();
    }

    static void Rest(List<int> X, List<int> Z, List<int> M, List<int> D, int start, int len, int row, int grid, int cols)
    {
        while (len > 0)
        {
            int c = Mathf.Min(len, MaxNodeTicks);
            if (c < MinNoteTicks && D.Count > 0) { D[D.Count - 1] += c; return; }
            X.Add(Mathf.Clamp(start / grid, 0, cols - 1)); Z.Add(row); M.Add(1); D.Add(Mathf.Max(MinNoteTicks, c));
            start += c; len -= c;
        }
    }

    /// <summary>Places <paramref name="n"/>: the notes it covers go, a note it starts under is trimmed to end where it starts (dropped under
    /// <see cref="MinNoteTicks"/>). False when that exact note is already there (nothing changes).</summary>
    public static bool Place(List<PhraseNote> notes, PhraseNote n)
    {
        if (notes == null || n.len <= 0) return false;
        foreach (var o in notes) if (o.start == n.start && o.row == n.row && o.len == n.len && o.mod == n.mod) return false;
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var o = notes[i];
            if (o.End <= n.start || o.start >= n.End) continue;   // no overlap
            if (o.start < n.start) { o.len = n.start - o.start; if (o.len < MinNoteTicks) notes.RemoveAt(i); else notes[i] = o; }
            else notes.RemoveAt(i);
        }
        notes.Add(n);
        notes.Sort((a, b) => a.start.CompareTo(b.start));
        return true;
    }

    /// <summary>The index of the note sounding at <paramref name="tick"/> (start ≤ tick &lt; end), -1 none.</summary>
    public static int IndexAt(IList<PhraseNote> notes, int tick)
    {
        if (notes == null) return -1;
        for (int i = 0; i < notes.Count; i++) if (tick >= notes[i].start && tick < notes[i].End) return i;
        return -1;
    }

    /// <summary>Index of the note starting at <paramref name="start"/> on <paramref name="row"/> with <paramref name="len"/> (-1 none).</summary>
    public static int IndexOf(IList<PhraseNote> notes, int start, int row, int len)
    {
        if (notes == null) return -1;
        for (int i = 0; i < notes.Count; i++) if (notes[i].start == start && notes[i].row == row && notes[i].len == len) return i;
        return -1;
    }

    /// <summary>"bear in mind people wont know how long their melody is at first so let it autoexpand as they reach the limit": the length (beats) a
    /// phrase of <paramref name="curBeats"/> needs for a note ending at <paramref name="endTicks"/> — a note that reaches into the LAST HALF MEASURE
    /// grows it by one measure (½ → 1 → 2 → 3 → 4 …; again while a long note still reaches into the new last half), at most
    /// <paramref name="maxBeats"/>; <paramref name="curBeats"/> when it fits.</summary>
    public static int GrowBeats(int curBeats, int endTicks, int beatsPerBar, int maxBeats)
    {
        int tpb = ProjectConfig.TicksPerBeat, bpb = Mathf.Max(1, beatsPerBar);
        int beats = Mathf.Max(1, curBeats);
        for (int guard = 0; guard < 64; guard++)
        {
            int total = beats * tpb;
            int half = Mathf.Min(bpb * tpb / 2, total / 2);   // the last half measure (a ½-measure phrase: its second half)
            if (endTicks <= total - half || beats >= maxBeats) break;
            beats = Mathf.Min(maxBeats, (beats / bpb + 1) * bpb);
        }
        return beats;
    }

    /// <summary>For a phrase length change (O's SetPhraseLength / GrowPhrase): re-encodes a phrase cube's state for <paramref name="newTotalTicks"/> —
    /// notes past the end go, one crossing it is trimmed, the trailing rest re-padded to the new end. False (s unchanged) when no note is left
    /// (the caller drops the cube).</summary>
    public static bool Resize(CubeState s, int newTotalTicks, int grid, int cols)
    {
        if (s == null) return false;
        var notes = NotesOf(s);
        int[] xs, zs, mods, durs;
        Encode(notes, newTotalTicks, grid, cols, out xs, out zs, out mods, out durs);
        if (xs.Length == 0) return false;
        s.xs = xs; s.zs = zs; s.mods = mods; s.durs = durs;
        s.rests = new bool[xs.Length];
        for (int i = 0; i < xs.Length; i++) s.rests[i] = mods[i] == 1;
        s.bend = null;
        return true;
    }
}
