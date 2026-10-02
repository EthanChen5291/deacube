using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The world HUD (SPEC v4 §5, scratchpad/v4/UI.md, research §4.1): "the world is the interface" — a few ink stickers, paper and pictures
/// drawn by the same hand as the world, one job per edge, ≈ 20 hit targets at rest. Top-left: the logo cube (menu strip) and the ink undo /
/// redo arrows (<see cref="HudMenuStrip"/>). Top centre: the column rail with bead stacks, the hopping playhead cube, the loop curl and the
/// focus-loop badge (<see cref="HudColumnRail"/>). Top-right: the PRESENT sticker and the contextual recentre sticker (<see cref="HudPresent"/>).
/// Left: the instrument column with mix mode (<see cref="HudInstruments"/>). Bottom-left: play, tempo, metronome, beat cubes, the sound
/// drawer and the punch pads (<see cref="HudTransport"/>). Bottom centre: the deck (<see cref="IslandTray"/>). On the islands: the component
/// header (<see cref="IslandHeader"/>). The cursor: <see cref="CursorKit"/>. Every icon-only control carries a hover caption (InkCaption).
/// Kept from v3 (public contract): the inspector card (until package U2's InspectorCard is live), the chord wheel, the sticker ring and the
/// twin variants, the toast, the cube hotkeys, selection (SelectMeasure / SelectMoon) and the v3 test hooks.
/// Esc, in order: whatever owns it this frame (draft, rings, wheel, popovers, the deck, drawers, mix mode, the menu strip) → the focus loop →
/// stop playback → open the menu strip. Cmd+O loads. When the inspector is open the instrument column and the deck step aside, the rail
/// dims to 50 % (its focus badge stays bright) and play / tempo / beat stay.
/// v6 (SPEC v6 §8.4 / §8.5, package U2): bottom-right, the clipboard chip (<see cref="HudClipboard"/>). Keys: 1–9 / 0 pick up the ten
/// groups' cubes (the same key again puts it down; while inspecting they still recolour the inspected cube — PathManager), Cmd+C copies the
/// inspected / hovered cube, Cmd+V pastes onto the hovered grid (else onto the next grid after the copied cube's home; with nothing to paste
/// on the pattern goes into the hand). A hand that holds something owns the Esc after the draft, the inspector, rings, menus and drawers
/// (PathManager puts it down), before the focus loop, the selection, playback and the strip.
/// v7 (SPEC v7 §8.3 / §15 / §16.4, package U2): the SIZE ROW beside the held chip (<see cref="HudSizeRow"/>), the SELECTION BAR over
/// Shift-selected grids (<see cref="HudSelection"/>) and the SHORTCUT SHEET (<see cref="HudShortcuts"/>, `?`). New keys (every older binding
/// kept): ⇧↑ / ⇧↓ an octave copy of the inspected (else the hovered) cube — with grids selected they move the selection an octave; ⌥↑ / ⌥↓ a
/// harmony above / below; U flips it; ⌘↑ / ⌘↓ paste as an echo ↑ / ↓, ⌘⌥↑ / ⌘⌥↓ paste one step ↑ / ↓, ⌘⇧V paste as an answer (the hovered
/// grid, else the next one); ⇧→ launches the focused island (the selection when there is one); with grids selected ⌘C / ⌘V / Delete copy,
/// paste after and delete them; `?` opens the sheet. A grid selection owns the Esc (PathManager clears it).
/// v7 §21 ("just keep cubes on each grid"): R (the v3 rider toggle) is retired — a cube never leaves its grid; ⇧→ is the BUILD-UP into the
/// next section (the launch keeps only its riser and crash: no fling).
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager I;
    public PathManager pathManager;      // legacy
    public TextMeshProUGUI buttonText;   // legacy

    public Canvas Canvas { get; private set; }

    RectTransform hudRoot; CanvasGroup hudGroup; float hudAlphaTarget = 1f;

    // v4 widgets
    HudMenuStrip strip; HudColumnRail rail; HudPresent present; HudInstruments instruments; HudTransport transport; HudClipboard clipboard;
    HudSizeRow sizeRow; HudSelection selectionBar;
    /// <summary>v7: the size row beside the held chip and the selection bar over the selected grids.</summary>
    public HudSizeRow SizeRow => sizeRow;
    public HudSelection SelectionBar => selectionBar;
    public HudMenuStrip Strip => strip;
    /// <summary>v6: the clipboard chip (bottom-right).</summary>
    public HudClipboard ClipboardChip => clipboard;
    public HudColumnRail Rail => rail;
    public HudPresent Present => present;
    public HudInstruments InstrumentColumn => instruments;
    public HudTransport Transport => transport;

    // punch-ins (pads live on the transport)
    bool wantStutter, wantFunnel, wantHalf, wantDrop;
    int selectedMeasure = -1, selectedMoon = -1;
    // (v3's inspector card lived here: package U2's InspectorCard replaces it; Card / Grid / CardShown forward to it)
    // rings, holds, hotkeys
    PopRing ring; AudioCube spotCube; float gDown = -1f; bool gFired;
    KeyBlock lastHoverIsland; float lastHoverT = -99f;
    // chord wheel
    CanvasGroup wheelGroup; RectTransform wheelRt; readonly List<HudButton> wedgeBtns = new List<HudButton>();
    HudButton qualityBtn; ChordQuality wheelQuality = ChordQuality.Major7; int wheelMeasure = -1; float wheelShown; bool wheelOpen;
    Image wheelHome;   // v5: a little house on the key's home root
    // toast: a small ink sticker with a glyph that pops above the transport
    RectTransform toastRt; CanvasGroup toastGroup; Image toastIcon; InkShape toastBody; float toastT = 9f;

    static readonly ChordQuality[] QualityCycle = { ChordQuality.Major7, ChordQuality.Minor7, ChordQuality.Dominant7, ChordQuality.Sus4, ChordQuality.Major9, ChordQuality.Minor9, ChordQuality.Dominant9 };
    static readonly string[] StickerIcons = { "ghost", "accent", "ratchet2", "ratchet3", "coin", "tie", "lift" };

    AudioCube Sel => PathManager.I != null ? PathManager.I.selectedCube : null;
    bool HasSong => SongManager.I != null && SongManager.I.HasSong;
    KeyBlock Island => HasSong && selectedMeasure >= 0 && selectedMeasure < SongManager.I.Islands.Count ? SongManager.I.Islands[selectedMeasure] : null;

    public static void Toast(string icon, Color c) { if (I != null) I.ShowToast(icon, c); }
    /// <summary>Shows / hides the whole HUD. The change is drawn on twos (T's leave, SPEC §7b): shown = alpha 0 → 0.55 → 1 while the
    /// corner clusters pop 0.92 → 1.04 → 1; hidden = 1 → 0.4 → 0.</summary>
    public void SetHudVisible(bool v)
    {
        float t = v ? 1f : 0f;
        if (!v && strip != null) strip.CloseNow();
        if (t == hudAlphaTarget) return;
        hudAlphaTarget = t;
        hudStep = 0; hudTick = int.MinValue;
    }
    /// <summary>The HUD pop-in, explicitly (MainMenu may call it at the end of its leave; SetHudVisible(true) does the same).</summary>
    public void PopHudIn() { hudAlphaTarget = 0f; SetHudVisible(true); }
    int hudStep = -1, hudTick = int.MinValue;
    static readonly float[] PopAlpha = { 0.55f, 1f, 1f }, PopScale = { 0.92f, 1.04f, 1f }, HideAlpha = { 0.4f, 0f };
    /// <summary>One bead per island on the column rail (v2 contract: == SongManager.Islands.Count).</summary>
    public int PillCount => rail != null ? rail.CountNow() : 0;
    /// <summary>The selected Moon (index into SongManager.Moons) or -1.</summary>
    public int SelectedMoon => selectedMoon;
    /// <summary>The selected island (index into SongManager.Islands) or -1: its component header stays up (IslandHeader).</summary>
    public int SelectedMeasure => selectedMeasure;
    /// <summary>The open world-anchored ring (sticker ring / twin variants), if any.</summary>
    public PopRing OpenRing => ring;
    /// <summary>The HUD root (pop rings and popovers are parented here).</summary>
    public RectTransform HudRoot => hudRoot;
    /// <summary>The chord wheel is open (Esc closes it first).</summary>
    public bool WheelOpen => wheelOpen;
    /// <summary>v5 tests: the wheel's root wedges (circle-of-fifths order from the top), its centre (the vibe) and the home-root house.</summary>
    public IReadOnlyList<HudButton> WheelWedges => wedgeBtns;
    public HudButton WheelQuality => qualityBtn;
    public RectTransform WheelHome => wheelHome != null ? wheelHome.rectTransform : null;
    /// <summary>The inspector card and its flat path grid: package U2's InspectorCard (v3 callers and the frozen tests read these).</summary>
    public RectTransform Card => InspectorCard.Root;
    public PathGridView Grid => InspectorCard.Grid;
    /// <summary>Card slide progress 0..1 (eased).</summary>
    public float CardShown => InspectorCard.Shown;

    // ------------------------------------------------------------------ v2 world-anchored rings (SPEC §3.3, §6)
    /// <summary>Sticker ring: 7 glyphs around node <paramref name="node"/> of <paramref name="cube"/>; a pick sets the sticker
    /// (picking the current one clears it), pushes History and auditions the node.</summary>
    public void OpenStickerRing(AudioCube cube, int node)
    {
        if (cube == null || node < 0 || node >= cube.nodes.Count || cube.nodes[node] == null) return;
        CloseRings();
        var tile = cube.nodes[node];
        int cur = node < cube.mods.Count ? cube.mods[node] : 0;
        var r = PopRing.Open(hudRoot, tile.Top + Vector3.up * 0.35f, StickerIcons, i =>
        {
            if (cube == null || node >= cube.nodes.Count) return;
            int mod = i + 2;
            int now = node < cube.mods.Count ? cube.mods[node] : 0;
            cube.SetMod(node, now == mod ? 0 : mod);
            History.Push();
            if (cube.nodes[node] != null) cube.nodes[node].PlayPreview(cube.instrument, cube.octave, cube.voice);
        }, 34f);
        if (cur >= 2 && cur - 2 < StickerIcons.Length) r.SetToggled(cur - 2, true);
        r.onClosed = () => { if (ring == r) ring = null; };
        ring = r;
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f);
    }
    /// <summary>Island halo (P1): the hub click selects the island (its header stays up).</summary>
    public void OpenIslandHalo(KeyBlock kb) { if (kb != null) HandleHubClicked(kb); }
    /// <summary>Moon halo (P1): selects the Moon (its header offers dice / move / delete).</summary>
    public void OpenMoonHalo(KeyBlock moon) { if (moon != null) HandleHubClicked(moon); }
    public void CloseRings() { if (ring != null) { var r = ring; ring = null; r.Close(); } }

    // ------------------------------------------------------------------ lifecycle
    void Awake()
    {
        I = this;
        BuildCanvas();
        BuildHud();
        CubeInspector.Ensure();   // SPEC v3 §2.2: the fly-to-camera inspector (not placed in the scene)
    }

    void OnEnable()
    {
        GlobalClock.OnPlay += HandlePlay; GlobalClock.OnPause += HandleTransport; GlobalClock.OnStop += HandleTransport;
        GlobalClock.OnBeat += HandleBeat; GlobalClock.OnLoop += HandleLoop; GlobalClock.OnMeterChanged += HandleMeter; GlobalClock.OnTempoChanged += HandleTempo;
        PathManager.OnSelectionChanged += HandleSelection;
        SongManager.OnSongControlsChanged += HandleSongControls; SongManager.OnTransposeChanged += HandleSongControls;
        SubscribeWorld();
    }
    void OnDisable()
    {
        GlobalClock.OnPlay -= HandlePlay; GlobalClock.OnPause -= HandleTransport; GlobalClock.OnStop -= HandleTransport;
        GlobalClock.OnBeat -= HandleBeat; GlobalClock.OnLoop -= HandleLoop; GlobalClock.OnMeterChanged -= HandleMeter; GlobalClock.OnTempoChanged -= HandleTempo;
        PathManager.OnSelectionChanged -= HandleSelection;
        SongManager.OnSongControlsChanged -= HandleSongControls; SongManager.OnTransposeChanged -= HandleSongControls;
        if (SongManager.I != null) SongManager.I.OnSongRebuilt -= HandleSongRebuilt;
        if (PathManager.I != null) { PathManager.I.OnNodeHeld -= HandleNodeHeld; PathManager.I.OnHubClicked -= HandleHubClicked; }
        worldSubscribed = false;
        CursorKit.ResetAll();
    }

    bool worldSubscribed;
    void SubscribeWorld()
    {
        if (worldSubscribed || PathManager.I == null) return;
        PathManager.I.OnNodeHeld += HandleNodeHeld; PathManager.I.OnHubClicked += HandleHubClicked;
        worldSubscribed = true;
    }

    void Start()
    {
        if (SongManager.I != null) SongManager.I.OnSongRebuilt += HandleSongRebuilt;
        SubscribeWorld();
        RefreshAll();
    }

    // ------------------------------------------------------------------ canvas
    void BuildCanvas()
    {
        var go = new GameObject("HUDCanvas", typeof(RectTransform));
        go.layer = 5;
        Canvas = go.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = 10;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
        go.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
        var hudGo = UIKit.Obj("HUD", go.transform);
        hudRoot = UIKit.RT(hudGo);
        UIKit.Stretch(hudRoot);
        hudGroup = hudGo.AddComponent<CanvasGroup>();
    }

    // ------------------------------------------------------------------ build
    void BuildHud()
    {
        instruments = HudInstruments.Build(hudRoot);
        transport = HudTransport.Build(hudRoot);
        rail = HudColumnRail.Build(hudRoot);
        present = HudPresent.Build(hudRoot);
        clipboard = HudClipboard.Build(hudRoot);
        sizeRow = HudSizeRow.Build(hudRoot);          // v7: beside the held chip (over the column)
        IslandHeader.Build(hudRoot);
        selectionBar = HudSelection.Build(hudRoot);   // v7: floats over the selected grids (above the island header)
        BuildChordWheel();
        BuildToast();
        strip = HudMenuStrip.Build(hudRoot);   // last: its veil covers the rest of the HUD
        IslandTray.Ensure();                   // the deck (bottom centre) registers addIsland / tray.button
        if (IslandHeader.I != null) IslandHeader.I.transform.SetAsFirstSibling();   // world-anchored: under every fixed control, the deck too
    }

    /// <summary>F22: an instrument chip recolours the inspected cube while inspecting (as the number keys do); v6: else it picks that group's
    /// cube up (placing mode) or, when it is already in the hand, puts it down (HudInstruments.ToggleHand).</summary>
    public void PickInstrument(int idx)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        if (CubeInspector.IsOpen && pm.selectedCube != null) pm.SetSelectedInstrument(idx);
        else HudInstruments.ToggleHand(idx, -1);
    }

    // ------------------------------------------------------------------ v6 copy / paste / the hand (SPEC v6 §8.5)
    int heldLast = -1;   // the group whose cube was in the hand at the end of the last frame (a digit toggles against it)

    /// <summary>Cmd+C: copies the inspected cube, else the hovered one (the clipboard chip flies in from it). False when there is none.</summary>
    public bool CopyKey()
    {
        var pm = PathManager.I;
        if (pm == null) return false;
        var c = pm.selectedCube != null ? pm.selectedCube : pm.Candidate;
        if (c == null && !CubeInspector.IsOpen)
        {
            // v7 (the user: "copy a certain grid's paths to a different chord progression"): no cube under the pointer — ⌘C over a grid copies
            // ALL its paths (Clipboard.CopyPaths); ⌘V over another grid then pastes them, each fitted to its chord
            var g = HoveredGrid();
            if (g != null && Clipboard.CopyPaths(g)) { g.Pulse(0.8f); AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.2f); PathsCopyKeyCount++; return true; }
        }
        if (c == null || !c.isFinalized || !Clipboard.Copy(c)) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return false; }
        var cam = Camera.main;
        Vector3 sp = cam != null && !CubeInspector.IsOpen ? cam.WorldToScreenPoint(c.transform.position) : new Vector3(Screen.width * 0.3f, Screen.height * 0.5f, 1f);
        if (sp.z > 0f) HudClipboard.NoteCopySource(sp);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.35f);
        return true;
    }

    /// <summary>The grid under the pointer (its tile, platform or cube), else null.</summary>
    public static KeyBlock HoveredGrid()
    {
        var pm = PathManager.I;
        if (pm == null) return null;
        if (pm.HoverPasteTarget != null) return pm.HoverPasteTarget;
        if (pm.HoverTile != null && pm.HoverTile.island != null) return pm.HoverTile.island;
        if (pm.hoverIsland != null) return pm.hoverIsland;
        if (pm.Candidate != null) return pm.Candidate.Island;
        return null;
    }

    /// <summary>Cmd+V: pastes onto the hovered grid (refused with a thud where the pattern cannot go), else onto the next grid after the
    /// copied cube's home (while inspecting: after the card closed); with nowhere to paste the pattern goes into the hand (paste mode).
    /// Returns the new cube (null: refused, pending or picked up).</summary>
    public AudioCube PasteKey() { return PasteKey(Clipboard.PasteMode.Plain); }

    /// <summary>v7: <see cref="PasteKey()"/> in a paste mode (⌘↑ / ⌘↓ echo ↑ / ↓, ⌘⌥↑ / ⌘⌥↓ step ↑ / ↓, ⌘⇧V answer; an echo may land on
    /// the copied cube's own grid: a doubling).</summary>
    public AudioCube PasteKey(Clipboard.PasteMode mode)
    {
        var pm = PathManager.I;
        // v7: a grid's paths were copied last — ⌘V pastes them all onto the hovered grid, each fitted to its chord (one History entry)
        if (mode == Clipboard.PasteMode.Plain && Clipboard.PathsLast && Clipboard.HasPaths && !CubeInspector.IsOpen)
        {
            var g = HoveredGrid();
            PasteKeyCount++; LastPasteKeyMode = mode;
            if (g == null || !Clipboard.CanPastePathsOn(g)) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); if (g != null) g.Shake(0.12f); return null; }
            var placed = Clipboard.PastePathsOn(g);
            return placed.Count > 0 ? placed[0] : null;
        }
        if (pm == null || !Clipboard.HasPattern) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return null; }
        if (CubeInspector.IsOpen) { InspectorCard.PasteNextAfterClose(mode); return null; }
        var hovered = HoveredGrid();
        AudioCube made;
        if (hovered != null)
        {
            if (!Clipboard.CanPasteOn(hovered, mode)) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.2f); return null; }
            made = Clipboard.PasteOn(hovered, mode);
        }
        else made = Clipboard.PasteNext(mode);
        if (made == null && hovered == null && pm.Hand != PathManager.HandKind.Pattern) pm.PickUpPattern(mode);
        PasteKeyCount++; LastPasteKeyMode = mode;
        return made;
    }
    /// <summary>v7 tests: pastes asked through the keys and the mode of the last one.</summary>
    public static int PasteKeyCount { get; private set; }
    /// <summary>v7 tests: grids whose paths ⌘C copied.</summary>
    public static int PathsCopyKeyCount { get; private set; }
    public static Clipboard.PasteMode LastPasteKeyMode { get; private set; }

    // ------------------------------------------------------------------ v7 cube ideas / launch from the keys (SPEC v7 §8.3)
    /// <summary>The cube the idea keys act on: the inspected cube, else the hovered one (null none).</summary>
    public static AudioCube IdeaTarget()
    {
        var pm = PathManager.I;
        if (pm == null) return null;
        var c = pm.selectedCube != null ? pm.selectedCube : pm.Candidate;
        return c != null && c.isFinalized && c.nodes.Count > 0 ? c : null;
    }

    /// <summary>⇧↑ / ⇧↓: an octave copy of the inspected / hovered cube (CubeOps.OctaveCopy: one History entry; a refusal is CubeOps' soft
    /// thud). Null when refused.</summary>
    public AudioCube OctaveCopyKey(int dir)
    {
        var c = IdeaTarget();
        if (c == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return null; }
        var made = CubeOps.OctaveCopy(c, dir);
        if (made == null) return null;
        Onboarding.Notify(Onboarding.Ev.OctaveCopied);
        IdeaKeyCount++;
        return made;
    }

    /// <summary>⌥↑ / ⌥↓: a support harmony above / below the inspected / hovered cube (CubeOps.Harmonize: one History entry).</summary>
    public AudioCube HarmonyKey(bool above)
    {
        var c = IdeaTarget();
        if (c == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return null; }
        var made = CubeOps.Harmonize(c, above);
        if (made == null) return null;
        Onboarding.Notify(Onboarding.Ev.HarmonyAdded);
        IdeaKeyCount++;
        return made;
    }

    /// <summary>U: the inspected / hovered cube upside down (CubeOps.Flip: one History entry). False when refused.</summary>
    public bool FlipKey()
    {
        var c = IdeaTarget();
        if (c == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return false; }
        if (CubeOps.FlipRefusal(c) != null) { CubeOps.Flip(c); return false; }   // (CubeOps' soft refusal: a thud, a squash)
        CubeOps.Flip(c);
        Onboarding.Notify(Onboarding.Ev.CubeFlipped);
        IdeaKeyCount++;
        return true;
    }
    /// <summary>v7 tests: ideas made through the keys.</summary>
    public static int IdeaKeyCount { get; private set; }

    /// <summary>The island the keys mean by "the focused island": the selected island (its header pinned), else the header's island, else
    /// the hovered grid, else the lit island while playing, else the first island of the focused column (IslandTray.FocusedColumn).</summary>
    public KeyBlock FocusedIsland()
    {
        if (!HasSong) return null;
        var sm = SongManager.I;
        if (selectedMeasure >= 0 && selectedMeasure < sm.Islands.Count && sm.Islands[selectedMeasure] != null) return sm.Islands[selectedMeasure];
        var h = IslandHeader.Current;
        if (h != null && !h.IsMoon) return h;
        var hov = HoveredGrid();
        if (hov != null && !hov.IsMoon) return hov;
        if (GlobalClock.IsPlaying) { int lit = sm.LitIsland; if (lit >= 0 && lit < sm.Islands.Count && sm.Islands[lit] != null) return sm.Islands[lit]; }
        int col = IslandTray.FocusedColumn();
        foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && kb.column == col) return kb;
        return null;
    }

    /// <summary>⇧→: the build-up (launch) on / off — the selected grids when there are some (SongOps.SetLaunchGrids), else the focused island
    /// (SongManager.SetLaunch). v7 §21.4: a riser swells into the next section's first downbeat and a crash lands on it; the cubes stay home.</summary>
    public bool LaunchKey()
    {
        if (GridSelection.Any && !CubeInspector.IsOpen) return HudSelection.Do(HudSelection.Act.Launch);
        var kb = FocusedIsland();
        var sm = SongManager.I;
        int at = kb != null && sm != null ? sm.Islands.IndexOf(kb) : -1;
        if (at < 0 || kb.IsStairs || kb.IsMoon) { AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.3f); return false; }
        sm.SetLaunch(at, !kb.launch);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.25f, 1.25f);
        LaunchKeyCount++;
        return true;
    }
    /// <summary>v7 tests: launches toggled through ⇧→ on an island.</summary>
    public static int LaunchKeyCount { get; private set; }

    /// <summary>1–9 / 0 (not while inspecting: PathManager recolours then): picks up group <paramref name="i"/>'s cube; the same key again
    /// (that group's cube in the hand since the last frame) puts it down.</summary>
    void HandKey(int i)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        if (pm.CubeInHand && heldLast == i) { pm.PutDown(); AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f); }
        else HudInstruments.ToggleHand(i, -1, false);   // (the key decided the put-down above, against the last frame's hand)
        heldLast = pm.CubeInHand ? pm.selectedInstrument : -1;
    }


    /// <summary>- / = on the inspected cube: one hit fewer / more in its bar (the necklace's count; v2 hotkeys).</summary>
    void NudgeHits(int d)
    {
        var c = Sel; if (c == null) return;
        int n = c.StepsPerBar;
        int eff;
        if (c.hits == -1) eff = n;
        else if (c.hits == -2) { eff = 0; var p = Rhythm.Pattern(c.hits, c.rot, c.mask, n); for (int j = 0; j < p.Length; j++) if (p[j]) eff++; }
        else eff = Mathf.Clamp(c.hits, 1, n);
        int want = Mathf.Clamp(eff + d, 1, n);
        c.SetHits(want >= n ? -1 : want);
        History.Push();
        CubeInspector.NoteRhythmEdited();
    }

    // ------------------------------------------------------------------ twins, stamps
    /// <summary>Drops a twin of the selected cube on the same tiles (SPEC §3.1 #10); variant 0 half speed + octave, 1 double, 2 reversed, 3 phase +1.</summary>
    void MakeTwin(int variant)
    {
        var c = Sel;
        if (c == null || PathManager.I == null) return;
        var t = PathManager.I.RestoreCube(c.TwinState(variant));
        if (t == null) { ShowToast("warning", Palette.Danger); return; }
        SequenceMaster.RecalculateTimeline();
        Fx.Burst(t.transform.position, t.Color, 12, 2.4f);
        AudioPool.UI(ProceduralAudio.Thud(), 0.5f);
        History.Push();
    }

    bool StampOk(KeyBlock kb, KeyBlock src) => kb != null && kb.kind == 0 && kb != src;

    /// <summary>The island a stamp goes to: the hovered island (or the one hovered in the last 4 s), else the selected pill,
    /// else the lit island, else the next island after the cube's own.</summary>
    KeyBlock StampTarget()
    {
        var c = Sel; if (c == null || !HasSong) return null;
        var src = c.Island;
        var islands = SongManager.I.Islands;
        KeyBlock t = PathManager.I != null ? PathManager.I.hoverIsland : null;
        if (t == null && Time.unscaledTime - lastHoverT < 4f) t = lastHoverIsland;
        if (StampOk(t, src)) return t;
        if (StampOk(Island, src)) return Island;
        if (GlobalClock.IsPlaying) { int lit = Mathf.Clamp(SongManager.I.LitIsland, 0, islands.Count - 1); if (StampOk(islands[lit], src)) return islands[lit]; }
        int i = Mathf.Max(0, islands.IndexOf(src));
        for (int k = 1; k < islands.Count; k++) { var kb = islands[(i + k) % islands.Count]; if (StampOk(kb, src)) return kb; }
        return null;
    }

    void StampClick()
    {
        if (PathManager.I == null) return;
        var t = StampTarget();
        var n = t != null ? PathManager.I.StampTo(t) : null;
        if (n == null) { ShowToast("warning", Palette.Danger); return; }
        History.Push();
        ShowToast("stamp", Palette.Ok);
    }

    void StampAll()
    {
        if (PathManager.I == null || Sel == null) return;
        PathManager.I.StampToAll();
        History.Push();
        ShowToast("stamp", Palette.Ok);
    }



    // ------------------------------------------------------------------ chord wheel + toast (built as before)
    void BuildChordWheel()
    {
        var go = UIKit.Obj("ChordWheel", hudRoot);
        UIKit.Stretch(UIKit.RT(go));
        wheelGroup = go.AddComponent<CanvasGroup>();
        var catcher = go.AddComponent<Image>();
        catcher.color = Comic.A(Comic.Ink, 0.58f); catcher.raycastTarget = true;
        var cb = go.AddComponent<HudButton>();
        cb.bg = null; cb.icon = null; cb.animateScale = false; cb.onClick = CloseWheel;

        var wheel = UIKit.Obj("Wheel", go.transform);
        wheelRt = UIKit.RT(wheel);
        UIKit.Anchor(wheelRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 420f));
        for (int i = 0; i < 12; i++)
        {
            int idx = i;
            var w = UIKit.Obj("Wedge" + i, wheel.transform);
            var wrt = UIKit.RT(w);
            UIKit.Anchor(wrt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 420f));
            wrt.localRotation = Quaternion.Euler(0f, 0f, 90f - i * 30f);
            var img = w.AddComponent<Image>();
            img.sprite = Comic.Wedge(30f);
            img.raycastTarget = true;
            img.alphaHitTestMinimumThreshold = 0.5f;
            Color hue = Palette.Hue(i / 12f, 0.62f, 0.95f);
            img.color = hue;
            var b = w.AddComponent<HudButton>();
            b.bg = img; b.icon = null; b.animateScale = false;
            b.bgColor = hue;
            b.accent = Color.Lerp(hue, Comic.Cream, 0.62f);
            b.onClick = () => PickRoot(idx);
            wedgeBtns.Add(b);
        }
        qualityBtn = UIKit.Button(wheel.transform, "Quality", "triUp", 104f, CycleQuality, 0.5f);
        UIKit.Anchor(UIKit.RT(qualityBtn.gameObject), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 104f));
        qualityBtn.iconColor = Color.white;
        wheelHome = Comic.GlyphImage(wheel.transform, "Home", Vibe.HomeIcon, Comic.Cream, 24f, true);
        wheelHome.raycastTarget = false;
        wheelGroup.alpha = 0f; wheelGroup.interactable = false; wheelGroup.blocksRaycasts = false;
        go.SetActive(false);
    }



    void BuildToast()
    {
        var go = UIKit.Obj("Toast", hudRoot);
        toastRt = UIKit.RT(go);
        UIKit.Anchor(toastRt, new Vector2(0f, 0f), new Vector2(150f, 140f), new Vector2(58f, 52f));
        toastGroup = go.AddComponent<CanvasGroup>(); toastGroup.blocksRaycasts = false;
        toastBody = go.AddComponent<InkShape>();
        toastBody.Shape = InkShape.Kind.Sticker; toastBody.color = Comic.Cream; toastBody.SetInk(2f, 3f); toastBody.ShadowOffset = Comic.StickerShadow; toastBody.raycastTarget = false;
        toastIcon = Comic.GlyphImage(go.transform, "Icon", "check", Palette.Ok, 30f, true);
        go.SetActive(false);
    }

    // ------------------------------------------------------------------ selection (the island header stays up for the selected island)
    public void SelectMeasure(int i, bool focus)
    {
        selectedMoon = -1;
        if (!HasSong) { selectedMeasure = -1; return; }
        selectedMeasure = Mathf.Clamp(i, 0, SongManager.I.Islands.Count - 1);
        GlobalClock.NotePick();   // play-from-grid: the next play starts at this pick (even the same grid clicked again)
        if (focus && OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(selectedMeasure);   // glide, no zoom jump
    }

    /// <summary>Selects Moon <paramref name="i"/> (its header offers dice / move / delete) and glides the camera to it.</summary>
    public void SelectMoon(int i)
    {
        if (!HasSong || i < 0 || i >= SongManager.I.Moons.Count) { selectedMoon = -1; return; }
        selectedMoon = i; selectedMeasure = -1;
        var kb = SongManager.I.Moons[i];
        if (kb != null && OrbitCamera.I != null) OrbitCamera.I.FocusIsland(kb);
    }

    /// <summary>Drops the island / Moon selection (Esc, a press on the empty sea, a new draft): the header may leave.</summary>
    public void ClearSelection() { selectedMeasure = -1; selectedMoon = -1; }

    /// <summary>Adds a drum Moon (N): v6 (SPEC v6 §11.4) its section starts at the FOCUSED column (IslandTray.FocusedColumn: the lit column
    /// while playing, else the selected / header / camera-focused island's column, else 0). One History entry (SongManager.AddMoon pushes it);
    /// refused (thud) at MaxMoons.</summary>
    public int AddMoon() { return AddMoon(IslandTray.FocusedColumn()); }

    /// <summary>Adds a drum Moon whose section starts at column <paramref name="column"/> (SongManager.AddMoon(column): one History entry),
    /// selects it and tells the tutorial (Onboarding moon.added). Returns its index or -1.</summary>
    public int AddMoon(int column)
    {
        if (!HasSong) return -1;
        if (SongManager.I.Moons.Count >= SongManager.MaxMoons) { AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return -1; }
        int i = SongManager.I.AddMoon(column);
        if (i < 0) { AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return -1; }
        if (rail != null) rail.MarkDirty();
        SelectMoon(i);
        AudioPool.UI(ProceduralAudio.Chime(), 0.3f);
        Onboarding.Notify(Onboarding.Ev.MoonAdded);
        return i;
    }

    /// <summary>Re-grooves Moon <paramref name="i"/> (its header's dice): one History entry.</summary>
    public void RollMoon(int i)
    {
        if (!HasSong || i < 0 || i >= SongManager.I.Moons.Count) return;
        SongManager.I.RollMoonGroove(i);
        History.Push();
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.3f);
    }

    /// <summary>Removes Moon <paramref name="i"/>: one History entry.</summary>
    public void RemoveMoon(int i)
    {
        if (!HasSong || i < 0 || i >= SongManager.I.Moons.Count) return;
        if (selectedMoon == i) selectedMoon = -1;
        SongManager.I.RemoveMoon(i);
        History.Push();
        if (rail != null) rail.MarkDirty();
    }

    // ------------------------------------------------------------------ chord wheel
    /// <summary>Opens the chord wheel for island <paramref name="measure"/> (the island header's more card, the "chord" hint).</summary>
    public void OpenChordWheel(int measure) { OpenWheel(measure); }


    void OpenWheel(int measure)
    {
        if (!HasSong || measure < 0 || measure >= SongManager.I.Islands.Count) return;
        wheelMeasure = measure;
        wheelQuality = SongManager.I.Islands[measure].quality;
        wheelOpen = true;
        wheelGroup.gameObject.SetActive(true);
        RefreshWheel();
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.25f);
    }
    void CloseWheel() { wheelOpen = false; }

    /// <summary>v5 (SPEC §7, the vibe language): every root wedge wears the colour the island would have on that root (its vibe's family colour,
    /// a tiny shade by root — the wheel reads as one weather), the key's home root carries a little house, the centre shows the vibe glyph
    /// and cycles the chord's weather (its caption names it).</summary>
    void RefreshWheel()
    {
        if (!HasSong || wheelMeasure < 0 || wheelMeasure >= SongManager.I.Islands.Count) { wheelOpen = false; return; }
        var kb = SongManager.I.Islands[wheelMeasure];
        int pc = ((kb.chordRootMIDI % 12) + 12) % 12;
        int fifths = (pc * 7) % 12;
        var semis = kb.semitoneList != null && kb.semitoneList.Count > 0 ? (IList<int>)kb.semitoneList : MusicTheory.Semitones(kb.quality);
        for (int i = 0; i < wedgeBtns.Count; i++)
        {
            wedgeBtns[i].toggled = i == fifths;
            Color c = Vibe.ChordColor(55 + (i * 7) % 12, semis);
            if (i % 2 == 1) c = Color.Lerp(c, Comic.Cream, 0.2f);   // every other wedge a shade lighter (tellable neighbours, no meaning)
            wedgeBtns[i].bgColor = c; wedgeBtns[i].accent = Color.Lerp(c, Comic.Cream, 0.62f);
            if (wedgeBtns[i].bg != null) wedgeBtns[i].bg.color = c;
            wedgeBtns[i].RefreshTheme();
        }
        qualityBtn.SetIcon(MusicTheory.QualityIcon(kb.quality));
        qualityBtn.bgColor = Palette.A(kb.chordColor, 0.95f);
        InkCaption.Attach(qualityBtn.gameObject, "weather · " + Vibe.Word(Vibe.Of(kb.quality)));
        wheelQuality = kb.quality;
        if (wheelHome != null)
        {
            int homeFifths = (MusicTheory.KeyOfSong().tonic * 7) % 12;
            float a = (90f - homeFifths * 30f) * Mathf.Deg2Rad;
            wheelHome.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 183f;
            wheelHome.transform.SetAsLastSibling();
        }
    }

    void PickRoot(int fifthsIndex)
    {
        if (!HasSong || wheelMeasure < 0 || wheelMeasure >= SongManager.I.Islands.Count) { wheelOpen = false; return; }   // S4: a stale wheel closes
        int pc = (fifthsIndex * 7) % 12;
        SongManager.I.SetChord(wheelMeasure, 55 + pc, MusicTheory.Semitones(wheelQuality));
        RefreshWheel();
    }

    void CycleQuality()
    {
        if (!HasSong || wheelMeasure < 0 || wheelMeasure >= SongManager.I.Islands.Count) { wheelOpen = false; return; }   // S4
        int i = Array.IndexOf(QualityCycle, wheelQuality);
        wheelQuality = QualityCycle[(i + 1) % QualityCycle.Length];
        var kb = SongManager.I.Islands[wheelMeasure];
        SongManager.I.SetChord(wheelMeasure, kb.chordRootMIDI, MusicTheory.Semitones(wheelQuality));
        RefreshWheel();
    }



    // ------------------------------------------------------------------ refresh
    void RefreshAll()
    {
        HandleTransport();
        HandleSongControls();
        if (rail != null) rail.MarkDirty();
    }

    /// <summary>The punch pads and keys drive Performance; the pads show the live state (called by the pads and every frame).</summary>
    public void RefreshPunch() { UpdatePunch(); }

    // ------------------------------------------------------------------ handlers
    void HandlePlay() { HandleTransport(); if (GlobalClock.SongBeat < 0.01f) Fx.StartSweep(); }
    void HandleTransport() { if (transport != null) transport.OnTransport(); }
    void HandleBeat(int beat, bool down) { if (transport != null) transport.OnBeat(beat, down); }
    void HandleLoop() { Fx.LoopSweep(); }
    void HandleMeter() { if (transport != null) transport.OnMeterChanged(); }
    void HandleTempo() { HandleSongControls(); }
    void HandleSongControls() { if (transport != null) transport.RefreshKnobs(); }
    void HandleSelection(AudioCube c) { CloseRings(); }
    void HandleSongRebuilt()
    {
        CloseRings(); RefreshAll();
        if (rail != null) rail.RebuildNow();
        if (HasSong && selectedMeasure >= SongManager.I.Islands.Count) selectedMeasure = -1;
        if (HasSong && selectedMoon >= SongManager.I.Moons.Count) selectedMoon = -1;
        if (wheelOpen) RefreshWheel();   // S4: the wheel re-checks its island (gone: it closes)
    }
    void HandleNodeHeld(AudioCube c, int node) { OpenStickerRing(c, node); }
    void HandleHubClicked(KeyBlock kb)
    {
        if (kb == null || !HasSong) return;
        int i = SongManager.I.Islands.IndexOf(kb);
        if (i >= 0) { SelectMeasure(i, false); return; }
        int m = SongManager.I.Moons.IndexOf(kb);
        if (m >= 0) { selectedMoon = m; selectedMeasure = -1; }
    }

    void ShowToast(string icon, Color c)
    {
        if (toastIcon == null) return;
        toastIcon.sprite = Comic.Glyph(icon, 30f, Comic.WantsOutline(c)); toastIcon.color = c;
        toastT = 0f;
        toastRt.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ per-frame
    void Update()
    {
        if (hudGroup == null) return;   // not built (or a domain reload dropped the references)
        float dt = Time.unscaledDeltaTime;
        UpdateHudFade();
        bool hudOn = hudAlphaTarget > 0.5f;
        hudGroup.interactable = hudOn; hudGroup.blocksRaycasts = hudOn;

        UpdateInspectingLayout();
        UpdateWheel(dt);
        UpdateToast(dt);
        UpdateHoverMemory();
        UpdateSelectionLife();
        UpdateHotkeys();
        UpdateEsc();
        UpdatePunch();
        UpdateSpotlight();
    }

    /// <summary>The HUD's show / hide, one drawing per 1/12 s (see <see cref="SetHudVisible"/>).</summary>
    void UpdateHudFade()
    {
        if (hudStep < 0) return;
        int t = HudKit.TwosTick;
        if (t == hudTick) return;
        hudTick = t;
        bool show = hudAlphaTarget > 0.5f;
        if (show)
        {
            hudGroup.alpha = Mathf.Max(hudGroup.alpha, PopAlpha[hudStep]);
            ScaleClusters(PopScale[hudStep]);
            if (++hudStep >= PopAlpha.Length) { hudStep = -1; hudGroup.alpha = 1f; ScaleClusters(1f); }
        }
        else
        {
            hudGroup.alpha = Mathf.Min(hudGroup.alpha, HideAlpha[hudStep]);
            if (++hudStep >= HideAlpha.Length) { hudStep = -1; hudGroup.alpha = 0f; }
        }
    }

    void ScaleClusters(float k)
    {
        var v = new Vector3(k, k, 1f);
        if (strip != null && strip.Cluster != null) strip.Cluster.localScale = v;
        if (rail != null) rail.Root.localScale = v;
        if (instruments != null) instruments.Root.localScale = v;
        if (transport != null) transport.transform.localScale = v;
        if (IslandTray.I != null && IslandTray.I.Stack != null) IslandTray.I.Stack.localScale = v;
    }

    void LateUpdate()
    {
        if (hudGroup == null) return;
        CursorKit.TickWorld(IslandHeader.GripHeld);
        heldLast = PathManager.I != null && PathManager.I.CubeInHand ? PathManager.I.selectedInstrument : -1;
        // Esc owners of this frame: next frame's Esc goes to them first (the focus loop is tracked apart: it is dismissed only when
        // nothing else took the Esc — SPEC R4)
        escOthersLast = EscOwned();
        escFocusLast = FocusLoop.Active;
    }
    bool escOthersLast, escFocusLast;

    bool EscOwned()
    {
        var pm = PathManager.I;
        return (pm != null && (pm.IsDrawing || (pm.Drag != null && pm.Drag.Busy) || pm.selectedCube != null || pm.Hand != PathManager.HandKind.Empty)) || wheelOpen || ring != null || PathGridView.AnyPopoverOpen
               || GridSelection.Any || HudShortcuts.IsOpen || (clipboard != null && clipboard.FanOpen)   // v7: PathManager clears a selection; the sheet / the fan close themselves
               || CubeInspector.IsOpen || IslandTray.IsOpen || (IslandTray.I != null && IslandTray.I.Dragging)
               || (strip != null && strip.IsOpen) || MainMenu.IsShown || Presenter.Active || (InterfaceController.I != null && InterfaceController.I.Visible);
    }

    /// <summary>
    /// Esc with nothing nearer to close (research §4.1, SPEC R4): the mix mode, the sound drawer and the header's more card close first;
    /// owners that read Esc themselves (a draft, a selected cube, the inspector, rings, the wheel, the deck, the strip) are skipped, this
    /// frame's and last frame's (whichever Update ran first already acted on it) — v6: a hand that holds a cube or a pattern among them
    /// (PathManager puts it down in its LateUpdate when nothing nearer took the key); an active focus loop is left to package R (PathManager
    /// dismisses it); then a selected island lets go of its header; then a playing song stops; then the menu strip opens. Every Esc taken
    /// here is claimed (FocusLoop.ClaimEsc) so the same press never also dismisses the focus loop.
    /// </summary>
    void UpdateEsc()
    {
        if (InputUtil.TypingInField || !KeyShim.Down(KeyCode.Escape)) return;
        if (WorldInput.KeysLocked) return;   // the strip (its own Esc), the menu, the presentation, the prompt
        if (clipboard != null && clipboard.FanOpen) { clipboard.Fan.Close(); FocusLoop.ClaimEsc(); return; }   // v7
        if (instruments != null && instruments.Mix) { instruments.SetMix(false); FocusLoop.ClaimEsc(); return; }
        if (transport != null && transport.DrawerOpen) { transport.SetDrawer(false); FocusLoop.ClaimEsc(); return; }
        if (IslandHeader.MoreOpen) { IslandHeader.CloseMore(); FocusLoop.ClaimEsc(); return; }
        if (EscOwned() || escOthersLast) return;
        if (FocusLoop.Active || escFocusLast) return;   // R dismisses the focus loop with this press
        FocusLoop.ClaimEsc();
        if (selectedMeasure >= 0 || selectedMoon >= 0) { ClearSelection(); IslandHeader.Hide(); return; }
        if (GlobalClock.IsPlaying) { GlobalClock.Stop(); return; }
        if (strip != null) strip.Open();
    }

    void UpdateHoverMemory()
    {
        var h = PathManager.I != null ? PathManager.I.hoverIsland : null;
        if (h != null) { lastHoverIsland = h; lastHoverT = Time.unscaledTime; }
    }

    /// <summary>The selection (and with it the pinned header) ends with a press on the empty sea or when a new path starts.</summary>
    void UpdateSelectionLife()
    {
        if (selectedMeasure < 0 && selectedMoon < 0) return;
        var pm = PathManager.I;
        if (pm == null) return;
        if (pm.IsDrawing) { ClearSelection(); return; }
        if (Input.GetMouseButtonDown(0) && pm.HoverIsEmpty && !InputUtil.PointerOverUI) ClearSelection();
    }

    /// <summary>Research §4.1 "When the inspector is open": the instrument column and the deck step aside, the rail dims to 50 % (the focus
    /// badge stays bright), play / tempo / beat stay.</summary>
    void UpdateInspectingLayout()
    {
        bool insp = CubeInspector.IsOpen;
        if (instruments != null) instruments.SetHidden(insp);
        if (rail != null) rail.SetDimmed(insp);
        if (insp && strip != null && strip.IsOpen) strip.CloseNow();
    }

    /// <summary>Cube keys (Appendix A): [ ] rotate, - = hits, arrows octave, T twin, W shadow, G stamp (hold: all) (v7 §21: R, the rider, retired); N adds a Moon (v6: at the focused column);
    /// Cmd+O loads the saved song.</summary>
    /// <summary>Tests: runs the hotkey dispatch once (use with KeyShim.Sim).</summary>
    public void RunHotkeysForTest() { UpdateHotkeys(); UpdateEsc(); UpdateSpotlight(); }

    static bool Alt => KeyShim.Held(KeyCode.LeftAlt) || KeyShim.Held(KeyCode.RightAlt);

    void UpdateHotkeys()
    {
        if (InputUtil.TypingInField || WorldInput.KeysLocked) return;   // the menu, the presentation and the prompt own the keys (SPEC v3 §1)
        var pm0 = PathManager.I;
        bool cmd = InputUtil.Cmd, shift = InputUtil.Shift, alt = Alt;
        // v7: ? opens the shortcut sheet (it closes itself: Esc / ?)
        if ((KeyShim.Down(KeyCode.Slash) && shift && !cmd) || KeyShim.Down(KeyCode.Question)) { HudShortcuts.Open(); return; }
        if (KeyShim.Down(KeyCode.N) && !cmd) { if (pm0 != null) pm0.BeforeStructuralKey(); AddMoon(); }   // S2 / S6
        if (KeyShim.Down(KeyCode.O) && cmd) MainMenu.ShowMySongs();   // "my songs": ⌘O opens the main menu's songs shelf
        // v7: with grids selected (and no cube open) ⌘C / ⌘V / Delete / ⇧↑ / ⇧↓ / ⇧→ act on the selection
        bool gridKeys = GridSelection.Any && !CubeInspector.IsOpen && (pm0 == null || !pm0.IsDrawing);
        if (gridKeys)
        {
            if (cmd && KeyShim.Down(KeyCode.C)) HudSelection.Do(HudSelection.Act.Copy);
            if (cmd && !shift && KeyShim.Down(KeyCode.V)) HudSelection.Do(HudSelection.Act.Paste);
            if (!cmd && (KeyShim.Down(KeyCode.Delete) || KeyShim.Down(KeyCode.Backspace))) HudSelection.Do(HudSelection.Act.Delete);
            if (shift && !cmd && !alt && KeyShim.Down(KeyCode.UpArrow)) HudSelection.Do(HudSelection.Act.OctaveUp);
            if (shift && !cmd && !alt && KeyShim.Down(KeyCode.DownArrow)) HudSelection.Do(HudSelection.Act.OctaveDown);
        }
        else
        {
            // v6: copy / paste; v7: the paste modes
            if (cmd && KeyShim.Down(KeyCode.C)) CopyKey();
            if (cmd && KeyShim.Down(KeyCode.V)) PasteKey(shift ? Clipboard.PasteMode.Answer : Clipboard.PasteMode.Plain);
            if (cmd && KeyShim.Down(KeyCode.UpArrow)) PasteKey(alt ? Clipboard.PasteMode.StepUp : Clipboard.PasteMode.EchoUp);
            if (cmd && KeyShim.Down(KeyCode.DownArrow)) PasteKey(alt ? Clipboard.PasteMode.StepDown : Clipboard.PasteMode.EchoDown);
            // v7: the cube ideas on the inspected (else the hovered) cube
            if (shift && !cmd && !alt && KeyShim.Down(KeyCode.UpArrow)) OctaveCopyKey(1);
            if (shift && !cmd && !alt && KeyShim.Down(KeyCode.DownArrow)) OctaveCopyKey(-1);
            if (alt && !cmd && !shift && KeyShim.Down(KeyCode.UpArrow)) HarmonyKey(true);
            if (alt && !cmd && !shift && KeyShim.Down(KeyCode.DownArrow)) HarmonyKey(false);
            if (!cmd && !alt && KeyShim.Down(KeyCode.U)) FlipKey();
        }
        if (shift && !cmd && !alt && KeyShim.Down(KeyCode.RightArrow)) LaunchKey();
        if (!cmd && !CubeInspector.IsOpen)
            for (int i = 0; i < 10 && i < Instruments.Count; i++)
                if (KeyShim.Down(i < 9 ? KeyCode.Alpha1 + i : KeyCode.Alpha0)) HandKey(i);
        var c = Sel;   // v3: the inspected cube
        if (c == null) { gDown = -1f; return; }
        if (KeyShim.Down(KeyCode.G)) { gDown = Time.unscaledTime; gFired = false; }
        if (gDown >= 0f && !gFired && KeyShim.Held(KeyCode.G) && Time.unscaledTime - gDown >= 0.8f) { gFired = true; StampAll(); }
        if (KeyShim.Up(KeyCode.G)) { if (gDown >= 0f && !gFired) StampClick(); gDown = -1f; gFired = false; }
        // [ ] turn the necklace — unless the inspector's length row is up for a selected note (U2: then [ ] . set that note's length)
        bool noteKeys = InspectorCard.NoteSelected;
        if (!noteKeys && KeyShim.Down(KeyCode.LeftBracket)) RotateNecklace(c, -1);
        if (!noteKeys && KeyShim.Down(KeyCode.RightBracket)) RotateNecklace(c, +1);
        if (KeyShim.Down(KeyCode.Minus) || KeyShim.Down(KeyCode.KeypadMinus)) NudgeHits(-1);
        if (KeyShim.Down(KeyCode.Equals) || KeyShim.Down(KeyCode.KeypadPlus)) NudgeHits(1);
        bool plainArrows = !cmd && !shift && !alt;   // v7: ⇧ / ⌥ / ⌘ + arrows are the octave copies, harmonies and echo pastes
        if (plainArrows && KeyShim.Down(KeyCode.UpArrow) && !c.IsDrums) { c.SetOctave(c.octave + 1); History.Push(); }
        if (plainArrows && KeyShim.Down(KeyCode.DownArrow) && !c.IsDrums) { c.SetOctave(c.octave - 1); History.Push(); }
        if (KeyShim.Down(KeyCode.T) && !InputUtil.Cmd) MakeTwin(0);
        if (KeyShim.Down(KeyCode.W) && !InputUtil.Cmd && !c.IsDrums) { c.SetFollow((c.follow + 1) % 4); History.Push(); }
    }

    /// <summary>[ / ]: turns the necklace notch; in explicit-mask mode the mask turns with it (as the dial's notch drag does), else the key would only move the notch.</summary>
    void RotateNecklace(AudioCube c, int d)
    {
        if (c.hits == -2) c.SetMask(NecklaceDial.RotateMask(c.mask, d, c.StepsPerBar));
        c.SetRot(c.rot + d);
        History.Push();
        CubeInspector.NoteRhythmEdited();
    }

    static void Apply(ref bool cur, bool want, Action<bool> set) { if (want == cur) return; cur = want; set(want); }

    /// <summary>Punch-ins: the rubber pads or the keys J / K / H / B (while playing) drive Performance; the pads show the live state.</summary>
    void UpdatePunch()
    {
        if (transport == null || transport.Stutter == null) return;
        bool keys = GlobalClock.IsPlaying && !InputUtil.TypingInField && !WorldInput.KeysLocked;
        Apply(ref wantStutter, transport.Stutter.Holding || (keys && Input.GetKey(KeyCode.J)), Performance.HoldStutter);
        Apply(ref wantFunnel, transport.Funnel.Holding || (keys && Input.GetKey(KeyCode.K)), Performance.HoldFunnel);
        Apply(ref wantHalf, transport.Half.Holding || (keys && Input.GetKey(KeyCode.H)), Performance.HoldHalf);
        Apply(ref wantDrop, transport.Drop.Holding || (keys && Input.GetKey(KeyCode.B)), Performance.HoldDrop);
        transport.ShowPads(Performance.StutterActive, Performance.FunnelActive, Performance.HalfActive, Performance.DropActive);
    }

    /// <summary>Spotlight: hold E while a cube is selected (the inspector card's chip hold is package U2's).</summary>
    void UpdateSpotlight()
    {
        bool key = !InputUtil.TypingInField && KeyShim.Held(KeyCode.E) && !InputUtil.Cmd && !WorldInput.KeysLocked;
        var c = Sel;
        AudioCube want = key && c != null ? c : null;
        if (want != spotCube)
        {
            if (spotCube != null) Performance.HoldSpotlight(spotCube, false);
            if (want != null) Performance.HoldSpotlight(want, true);
            spotCube = want;
        }
    }

    void UpdateWheel(float dt)
    {
        float target = wheelOpen ? 1f : 0f;
        wheelShown = Mathf.Lerp(wheelShown, target, 1f - Mathf.Exp(-dt * 14f));
        wheelGroup.alpha = wheelShown;
        wheelRt.localScale = Vector3.one * (0.85f + 0.15f * wheelShown);
        bool on = wheelShown > 0.5f;
        wheelGroup.interactable = on; wheelGroup.blocksRaycasts = on;
        if (!wheelOpen && wheelShown < 0.02f && wheelGroup.gameObject.activeSelf) wheelGroup.gameObject.SetActive(false);
        if (wheelOpen && !InputUtil.TypingInField && Input.GetKeyDown(KeyCode.Escape)) CloseWheel();
    }



    void UpdateToast(float dt)
    {
        if (!toastRt.gameObject.activeSelf) return;
        toastT += dt;
        float a = toastT < 0.15f ? toastT / 0.15f : (toastT > 1.2f ? 1f - (toastT - 1.2f) / 0.3f : 1f);
        toastGroup.alpha = Mathf.Clamp01(a);
        float st = Look.Stepped(Mathf.Clamp01(toastT / 0.3f), 12f / 0.3f * 0.3f);   // pops in on twos
        toastRt.localScale = Vector3.one * Ease.OutBack(st);
        toastRt.localEulerAngles = new Vector3(0f, 0f, toastT < 0.2f ? 6f : -4f);
        if (toastT > 1.5f) toastRt.gameObject.SetActive(false);
    }
}
