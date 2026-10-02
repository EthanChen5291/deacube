using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The tutorial (SPEC v4 §5, UI.md §5): short lowercase sentences in ink bubbles that point at what they teach — draw with the hologram,
/// pick a length (fat = long), finish, open the cube, stretch a note on its strip, close it, add an island below, drag it within its
/// column, make it lower, place an island from the deck, present — then a last "you're composing!" that closes itself after the
/// presentation. Steps advance on the events every package reports through <see cref="Notify"/> and on state the tutorial watches
/// itself (a draft growing, the brush length changing, an island drag ending). Missing targets wait with a precondition hint where one
/// exists (else a centred bubble with a next arrow, so nobody gets stuck). The island chapter pins the island header on the island it
/// talks about (IslandHeader.Show). Bubbles never block the gesture they teach (no raycasts while the pointer is down or a draft / drag
/// runs). Progress persists in PlayerPrefs (deacube.tutorial.step / .done); the tutorial auto-starts on the first world entry until done;
/// the menu's Learn restarts it. After it: one-off contextual tips — v5 adds three (SPEC §7): the deck's weather cards ("vibes", on the first
/// deck open), moving an island anywhere ("moveAnywhere", on the first island drag) and repeat ("repeat", when the island header first stays
/// up under the pointer; it points at the repeat button); and, for package R's hearing tiles, "hear" (the first tile a hover plays: every tile
/// plays its note, higher sounds higher) and "draftRun" (the first run a drafting hover plays: that is what a click adds, the little card draws
/// the tune). The tutorial's own first step opens with the hearing line until two tiles have been heard; its deck step tells the weather and
/// marks "vibes" seen; its drag step says left = earlier, right = later. <see cref="Suppressed"/> (tests) hides everything, freezes progress and
/// still forwards every event to <see cref="OnEvent"/>. Registers the world targets world:island0, world:islandLast, world:cube and
/// world:draftEnd (the draft's last tile).
/// v6 (SPEC v6 §7.5): a click on a tile no longer draws — the player picks a cube first (package H: placing mode). The drawing step now opens
/// with the hearing line, then says "pick a cube (the left column), then click tiles" pointing at the instrument column while the hand is
/// empty, and "click a tile and keep clicking" at the island once a cube is in the hand. Six more one-off tips, light and short: "press" (the
/// first empty-hand click: it only plays the tile), "copyPaste" (the first copy), "carry" (the first carry click), "keyboard" (the first
/// keyboard island), "jobs" (a deck open after the vibes tip: same number, same job — swap them; a star fits your notes), "tower" (the first
/// raised grid: it rises on its turn). Events observed here: PathManager.OnHandChanged → cube.picked, Clipboard.OnChanged → pattern.copied.
/// v7 (SPEC v7 §7.5, §13.4 — the user: "make really intuitive the idea of sections. like 4 beats in a measure, 4 measures in a "section" of a
/// musical idea … make it really visual, with support if needed"): the SECTIONS tip "4 beats = 1 measure · 4 measures = 1 section" the first
/// time a section fills (every section that reaches 4 measures also sparkles: on the rail's bracket and at its plinth), and one light tip each
/// for the v7 ideas — stairs, heights, melody grids, echoes, harmony, rewind / vary, flow, launch, flip, selecting many grids and the shortcut
/// sheet. They come from the gestures (the header's / cards' events, package H's CubeOps.OnMade / OnFlipped, an echo paste, U2's sheet) and
/// from what a gesture changed (History: a new stairs / phrase / layer / bent cube, a ground that left 0, a section that filled — a load or an
/// undo never counts). v7 tips wait in a short queue while another tip is up (≤ 20 s), so a stairs tip does not swallow the heights tip it
/// causes. §17.1: the second section-header hover suggests naming sections; §19.1: the first time a path reaches its grid's capacity (package
/// D notifies capacity.reached) — "a grid holds its measures — expand it to keep going"; §19.2 (quiet drawing): the drawing step says space
/// plays what is there.
/// §21 (the user: "just keep cubes on each grid. only explore grids moving from left to right with the long grid"): the carry tip says EXTEND
/// ("extend a grid → its cubes walk on through the next chords"), the flow tip answers "fill the section" (one long grid to its end), the launch
/// tip says a riser swells into the next section — no wording of cubes flying, flung or riding to another grid anywhere.
/// </summary>
public class Onboarding : MonoBehaviour
{
    public static Onboarding I;

    /// <summary>Event names the packages report (SPEC v3 §5.3, v4 §5).</summary>
    public static class Ev
    {
        public const string InstrumentPicked = "instrument.picked";
        public const string PathStarted = "path.started";
        public const string PathFinished = "path.finished";
        public const string PlayStarted = "play.started";
        public const string CubeHovered = "cube.hovered";
        public const string CubeInspected = "cube.inspected";
        public const string GridEdited = "grid.edited";
        public const string RhythmEdited = "rhythm.edited";
        public const string InspectorClosed = "inspector.closed";
        public const string TrayOpened = "tray.opened";
        public const string IslandPlaced = "island.placed";
        public const string IslandsMerged = "islands.merged";
        public const string IslandSplit = "island.split";
        public const string PresentEntered = "present.entered";
        public const string PresentExited = "present.exited";
        public const string MenuShown = "menu.shown";
        public const string SongStarted = "song.started";
        // v4 (SPEC v4 §5, UI.md §5)
        public const string LengthPicked = "length.picked";       // a note length picked on the draft row / under the strip (U2)
        public const string NoteStretched = "note.stretched";     // a strip block stretched (U2)
        public const string IslandAdded = "island.added";         // + above / + below (IslandHeader)
        public const string IslandDuplicated = "island.duplicated";
        public const string IslandRegister = "island.register";   // ▼ / ▲ on the header
        public const string IslandToggled = "island.toggled";     // the header's eye
        public const string IslandMoved = "island.moved";         // an island dragged within its column (observed from IslandDrag)
        // v5 (SPEC §7)
        public const string IslandRepeat = "island.repeat";       // the header's repeat button (IslandHeader)
        // v6 (SPEC v6 §7.5)
        public const string CubePickedUp = "cube.picked";         // a cube picked up into the hand (observed here: PathManager.OnHandChanged)
        public const string TilePressed = "tile.pressed";         // an empty-hand click played a tile (observed here: PathManager.PressCount)
        public const string PatternCopied = "pattern.copied";     // a pattern copied (observed here: Clipboard.OnChanged)
        public const string PatternPasted = "pattern.pasted";     // a pattern pasted onto a grid (observed here: Clipboard.OnPasted)
        public const string IslandCarry = "island.carry";         // the header's carry button (IslandHeader)
        public const string KeyboardAdded = "keyboard.added";     // a keyboard island added (the deck's keyboard card)
        public const string MoonAdded = "moon.added";             // a drum Moon added at a column (the deck's Moon card; the N key)
        // v7 (SPEC v7 §7.5, §13.4)
        public const string StairsAdded = "stairs.added";         // a stairs island added (the deck's stairs card)
        public const string PhraseAdded = "phrase.added";         // a melody phrase added (the deck's melody card)
        public const string RepeatStyle = "island.rewind";        // the header's rewind / vary stickers
        public const string IslandFlow = "island.flow";           // the header's flow button / "fill the section"
        public const string IslandLaunch = "island.launch";       // the header's launch toggle / the section header's launch
        public const string StairEdited = "stairs.edited";        // the stairs header (type, direction, steps, rate, lead-in)
        public const string CubeFlipped = "cube.flipped";         // a cube flipped (U2: the card's flip, the hotkey)
        public const string OctaveCopied = "cube.octave";         // an octave copy / echo (U2; also observed here: a new layer cube)
        public const string HarmonyAdded = "cube.harmony";        // a harmony cube (U2; also observed here: a new bent cube)
        public const string ShortcutsOpened = "shortcuts.opened"; // the ? sheet opened (U2)
        public const string GridsSelected = "grids.selected";     // a grid selection made (observed here: GridSelection.OnChanged)
        public const string SectionFilled = "section.filled";     // a section reached 4 measures (observed here: History)
        public const string SectionEdited = "section.edited";     // the section header: split / join / duplicate / delete / drums / launch
        public const string SectionNamed = "section.named";       // the section header's name picker (SPEC §17.1)
        public const string CapacityReached = "capacity.reached"; // §19.1: a note refused past a grid's capacity (D notifies it)
        public const string KeysExtended = "keys.extended";       // §20.1: a keyboard grew by an octave (D's auto-extend while drawing; the header's + octave)
    }

    /// <summary>Every notified event (tests and other listeners).</summary>
    public static event Action<string> OnEvent;
    /// <summary>A tutorial is in progress (and not suppressed).</summary>
    public static bool Active => I != null && I.step >= 1 && I.step <= StepCount && !Suppressed;
    /// <summary>Tests set this to keep bubbles and auto-starts away (events are still forwarded to OnEvent; progress is frozen).</summary>
    public static bool Suppressed;
    public static void Notify(string evt)
    {
        if (string.IsNullOrEmpty(evt)) return;
        OnEvent?.Invoke(evt);
        if (I != null) I.Handle(evt);
    }
    /// <summary>Starts the tutorial: <paramref name="force"/> = from step 1 (Learn); else only when not done (resuming a saved step).</summary>
    public static void StartTutorial(bool force) { var o = Ensure(); if (o != null) o.Begin(force); }

    // ------------------------------------------------------------------ steps
    public const int StepCount = 12;
    public const string PrefStep = "deacube.tutorial.step", PrefDone = "deacube.tutorial.done", PrefTip = "deacube.tip.";
    /// <summary>Step numbers (1-based) of the v4 tutorial.</summary>
    public const int SDraw = 1, SLength = 2, SFinish = 3, SOpen = 4, SStretch = 5, SClose = 6, SAddBelow = 7, SDragColumn = 8, SLower = 9, SDeck = 10, SPresent = 11, SDone = 12;
    /// <summary>Current step 1..StepCount (0 = none).</summary>
    public static int Step => I != null ? I.step : 0;
    public static bool Done => PlayerPrefs.GetInt(PrefDone, 0) == 1;
    public static TutorialBubble Bubble => I != null ? I.bubble : null;
    /// <summary>The info-step "next" (the bubble's arrow).</summary>
    public static void Next() { if (I != null) I.NextFromUser(); }
    /// <summary>Skip the tutorial (the bubble's ✕): marks it done.</summary>
    public static void Skip() { if (I != null) I.Finish(true); }
    /// <summary>Target id and event of a step (1-based), for tests.</summary>
    public static string TargetOf(int s) => s >= 1 && s <= StepCount ? Steps[s - 1].target : null;
    public static string EventOf(int s) => s >= 1 && s <= StepCount ? Steps[s - 1].ev : null;
    public static bool IsInfoStep(int s) => s >= 1 && s <= StepCount && Steps[s - 1].info;
    public static string TextOf(int s) => s >= 1 && s <= StepCount ? Steps[s - 1].text : null;
    /// <summary>The target the bubble is pointing at right now (a precondition hint may point elsewhere); null = centred.</summary>
    public static string ShownTarget => I != null ? I.shownTarget : null;
    /// <summary>The contextual tip on screen (null when none).</summary>
    public static string ActiveTip => I != null ? I.tipId : null;
    /// <summary>The island the island chapter talks about (the one added below; tests).</summary>
    public static KeyBlock ChapterIsland => I != null ? I.chapterIsland : null;

    public static Onboarding Ensure()
    {
        if (I != null) return I;
        var found = FindAnyObjectByType<Onboarding>();
        if (found != null) { I = found; return I; }
        if (!Application.isPlaying) return null;
        return new GameObject("Onboarding").AddComponent<Onboarding>();
    }

    struct StepDef
    {
        public string target, text, ev, ev2; public bool info;
        public StepDef(string target, string text, string ev, bool info, string ev2 = null) { this.target = target; this.text = text; this.ev = ev; this.info = info; this.ev2 = ev2; }
    }
    static readonly StepDef[] Steps =
    {
        new StepDef("palette", "pick a cube (the left column),\nthen click tiles", null, false),
        new StepDef("draft.lengths", "pick a length on the cube row\n(fat = long)", Ev.LengthPicked, false),
        new StepDef("world:draftEnd", "finish: click the last tile again", Ev.PathFinished, false),
        new StepDef("world:cube", "click your cube to open it", Ev.CubeInspected, false),
        new StepDef("inspector.rhythm", "stretch a note on the strip", Ev.NoteStretched, false, Ev.RhythmEdited),
        new StepDef("inspector.close", "close it", Ev.InspectorClosed, false),
        new StepDef("island.addBelow", "add an island below\n(the + under an island)", Ev.IslandAdded, false),
        new StepDef("island.move", "drag it by its edge —\nleft is earlier, right is later", Ev.IslandMoved, false),
        new StepDef("island.octave", "make it lower", Ev.IslandRegister, false),
        new StepDef("addIsland", "open the deck and place an island", Ev.IslandPlaced, false),
        new StepDef("present", "present your song", Ev.PresentEntered, false),
        new StepDef(null, "you're composing!", null, false),
    };
    // events remembered when they happen early: reaching their step completes it at once
    static readonly HashSet<string> Memorable = new HashSet<string> { Ev.PathFinished, Ev.CubeInspected, Ev.IslandAdded, Ev.IslandMoved, Ev.IslandRegister, Ev.IslandPlaced, Ev.PresentEntered, Ev.LengthPicked, Ev.NoteStretched };

    /// <summary>v5 tips (SPEC §7): ids and words.</summary>
    public const string TipVibes = "vibes", TipMove = "moveAnywhere", TipRepeat = "repeat", TipHear = "hear", TipDraftRun = "draftRun";
    /// <summary>The first step's opening line (until two tiles have been heard, or 7 s): the user could not tell what a tile sounds like.</summary>
    public const string HearText = "hover a tile to hear it —\nhigher tiles sound higher";
    public const string HearTipText = "every tile plays its note —\nhigher tiles sound higher";
    public const string DraftRunText = "you hear the run a click adds —\nthe little card draws your tune";
    public const string VibesText = "each card has a weather —\nsun feels like home,\nstorms want to go home";
    public const string MoveText = "drag it anywhere —\nleft is earlier, right is later";
    public const string RepeatText = "repeat puts an island on a belt:\nit plays again and rides forward";
    /// <summary>The deck step's words once the deck is open (v5: the weather of the cards, then what to do).</summary>
    public const string DeckOpenText = "each card has a weather —\nsun feels like home,\nstorms want to go home.\ndrag one onto the sea";
    /// <summary>v6: the drawing step once a cube is in the hand (before: its own words, "pick a cube (the left column), then click tiles").
    /// v7 (§19.2, quiet drawing: the grid no longer loops while you draw): space plays what is there.</summary>
    public const string DrawText = "click a tile and keep clicking —\neach note plays; space plays them all";
    /// <summary>v6 tips (SPEC v6 §7.5): ids and words.</summary>
    public const string TipPress = "press", TipCopy = "copyPaste", TipCarry = "carry", TipKeyboard = "keyboard", TipJobs = "jobs", TipTower = "tower", TipMoons = "moons";
    public const string PressText = "a click just plays a tile —\npick a cube to draw";
    public const string CopyText = "copied! paste it on another grid —\nit changes to fit that chord";
    /// <summary>§21: the carry is EXTEND → now (the long grid): its tip says what it does.</summary>
    public const string CarryText = "extend a grid → its cubes walk on\nthrough the next chords";
    public const string KeyboardText = "a keyboard: one note per key —\nclick keys to write a melody";
    public const string JobsText = "same number, same job:\nswap them freely —\na star: it fits your notes";
    public const string TowerText = "raise a grid: it rises on its turn\nand slowly sinks after";
    /// <summary>v6 §11: a Moon's drums play from its column until the next Moon.</summary>
    public const string MoonsText = "put a drum moon under a part:\nit plays from there until the next one";
    /// <summary>v7 tips (SPEC v7 §7.5, §13.4): ids and words — short, lowercase, one idea each.</summary>
    public const string TipSections = "sections", TipStairs = "stairs", TipHeights = "heights", TipMelody = "melody", TipEchoes = "echoes", TipHarmony = "harmony",
        TipRewind = "rewind", TipFlow = "flow", TipLaunch = "launch", TipFlip = "flip", TipSelect = "select", TipShortcuts = "shortcuts", TipNames = "names";
    /// <summary>§17.1: naming sections.</summary>
    public const string NamesText = "name your sections:\nverse, chorus…";
    /// <summary>§19.1: the first time a path reaches its grid's capacity.</summary>
    public const string TipCapacity = "capacity";
    public const string CapacityText = "a grid holds its measures —\nexpand it to keep going";
    /// <summary>§20.1: the first time a keyboard grows by an octave.</summary>
    public const string TipKeys = "keys";
    public const string KeysText = "the keyboard grows when your\nmelody needs more keys";
    /// <summary>The sections line (SPEC v7 §13.4, verbatim).</summary>
    public const string SectionsText = "4 beats = 1 measure ·\n4 measures = 1 section";
    public const string StairsText = "stairs: a run that falls\ninto the next chord";
    public const string HeightsText = "after a stair, the next grids\nstand where it ends";
    public const string MelodyText = "a melody grid: time runs right,\nhigher rows sound higher —\nit grows as you draw";
    public const string EchoesText = "an echo: your tune an octave away —\nit phases in when it plays";
    public const string HarmonyText = "a harmony cube sings along,\na 4th or a 5th away";
    public const string RewindText = "rewind: time unwinds\nand the grid plays again";
    /// <summary>§21: "fill the section" (the flow tip now answers it: one long grid to the section's end) and launch as a build-up (no fling).</summary>
    public const string FlowText = "fill the section: one long grid\nall the way to its end";
    public const string LaunchText = "launch: a riser swells\ninto the next section";
    public const string FlipText = "flip: the tune upside down —\nan instant answer";
    public const string SelectText = "shift-drag picks many grids —\ncopy, paste or move them together";
    public const string ShortcutsText = "press ? to see every shortcut";
    /// <summary>Every tip id since v4 (tests keep and restore their PlayerPrefs).</summary>
    public static readonly string[] AllTips = { "hover", "play", "merge", TipVibes, TipMove, TipRepeat, TipHear, TipDraftRun, TipPress, TipCopy, TipCarry, TipKeyboard, TipJobs, TipTower, TipMoons,
        TipSections, TipStairs, TipHeights, TipMelody, TipEchoes, TipHarmony, TipRewind, TipFlow, TipLaunch, TipFlip, TipSelect, TipShortcuts, TipNames, TipCapacity, TipKeys };
    /// <summary>The v6 tips (older suites mark them told while they test theirs).</summary>
    public static readonly string[] V6Tips = { TipPress, TipCopy, TipCarry, TipKeyboard, TipJobs, TipTower, TipMoons };
    /// <summary>The v7 tips (older suites mark them told while they test theirs).</summary>
    public static readonly string[] V7Tips = { TipSections, TipStairs, TipHeights, TipMelody, TipEchoes, TipHarmony, TipRewind, TipFlow, TipLaunch, TipFlip, TipSelect, TipShortcuts, TipNames, TipCapacity, TipKeys };

    TutorialBubble bubble;
    int step; float stepT, closedT, waitT, doneShownT;
    readonly HashSet<string> seen = new HashSet<string>();
    string shownTarget, tipId; float tipT, tipLife = 3.2f, headerUpT; string tipTarget; bool tipAtScreen;
    int stepAud0, idleAudSeen = -1, runAudSeen = -1;   // PathManager's hover-audition counters at the step start / last frame (the hearing tips)
    bool heardNow, runHeardNow;                         // this frame a hover played a tile / a drafting hover played a run
    KeyBlock chapterIsland;
    int brushAtStep = -1; bool dragWatch; KeyBlock dragBlock; float dragZ0, dragX0; int dragPushes;

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
        RegisterWorldTargets();
    }
    void OnEnable() { GlobalClock.OnPlay += HandlePlay; History.OnChanged += HandleHistory; PathManager.OnHandChanged += HandleHand; Clipboard.OnChanged += HandleClipboard; Clipboard.OnPasted += HandlePasted; GridSelection.OnChanged += HandleSelection; CubeOps.OnMade += HandleMade; CubeOps.OnFlipped += HandleFlipped; }
    void OnDisable() { GlobalClock.OnPlay -= HandlePlay; History.OnChanged -= HandleHistory; PathManager.OnHandChanged -= HandleHand; Clipboard.OnChanged -= HandleClipboard; Clipboard.OnPasted -= HandlePasted; GridSelection.OnChanged -= HandleSelection; CubeOps.OnMade -= HandleMade; CubeOps.OnFlipped -= HandleFlipped; }
    static void HandleSelection() { if (GridSelection.Any) Notify(Ev.GridsSelected); }
    /// <summary>v7 (package H's CubeOps): an octave copy / echo floats in a layer; a harmony cube stays in the grid's octave.</summary>
    static void HandleMade(AudioCube c) { if (c != null) Notify(c.layer != 0 ? Ev.OctaveCopied : Ev.HarmonyAdded); }
    static void HandleFlipped(AudioCube c) { Notify(Ev.CubeFlipped); }
    static void HandleHand() { if (PathManager.I != null && PathManager.I.CubeInHand) Notify(Ev.CubePickedUp); }
    static void HandleClipboard() { if (Clipboard.HasPattern) Notify(Ev.PatternCopied); }
    static void HandlePasted(AudioCube c)
    {
        Notify(Ev.PatternPasted);
        if (Clipboard.LastMode == Clipboard.PasteMode.EchoUp || Clipboard.LastMode == Clipboard.PasteMode.EchoDown) Notify(Ev.OctaveCopied);   // v7: an echo paste
    }
    int pressSeen = -1;   // PathManager.PressCount last frame (an empty-hand press = tile.pressed)
    void HandleHistory() { if (dragWatch) dragPushes++; Observe(); }
    void OnDestroy() { if (I == this) I = null; }

    void HandlePlay() { Notify(Ev.PlayStarted); }

    // ------------------------------------------------------------------ world targets
    static void RegisterWorldTargets()
    {
        Hints.RegisterWorld("world:island0", () => IslandPoint(0));
        Hints.RegisterWorld("world:islandLast", () => IslandPoint(-1));
        Hints.RegisterWorld("world:cube", CubePoint);
        Hints.RegisterWorld("world:draftEnd", DraftEndPoint);
        Hints.RegisterWorld("world:keyboard", KeyboardPoint);
        Hints.RegisterWorld("world:moonLast", MoonPoint);
        // v7: the newest stairs / phrase, the first grid standing on a raised or lowered ground, the newest echo / harmony cube, a filled section
        Hints.RegisterWorld("world:stairsLast", () => LastIsland(k => k.IsStairs));
        Hints.RegisterWorld("world:phraseLast", () => LastIsland(k => k.IsPhrase));
        Hints.RegisterWorld("world:groundFirst", GroundPoint);
        Hints.RegisterWorld("world:echoLast", () => LastCube(c => c.layer != 0));
        Hints.RegisterWorld("world:harmonyLast", () => LastCube(IsHarmonyCube));
        Hints.RegisterWorld("world:sectionFilled", SectionPoint);
    }
    /// <summary>v7: the newest island matching <paramref name="f"/> (its platform's centre).</summary>
    static Vector3 LastIsland(Func<KeyBlock, bool> f)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return Hints.None;
        for (int i = sm.Islands.Count - 1; i >= 0; i--) { var kb = sm.Islands[i]; if (kb != null && f(kb)) return kb.VisualCenter + Vector3.up * 0.35f; }
        return Hints.None;
    }
    /// <summary>v7: the newest finished cube matching <paramref name="f"/>.</summary>
    static Vector3 LastCube(Func<AudioCube, bool> f)
    {
        for (int k = SequenceMaster.Cubes.Count - 1; k >= 0; k--)
        {
            var c = SequenceMaster.Cubes[k];
            if (c != null && c.isFinalized && f(c)) return c.transform.position + Vector3.up * 0.2f;
        }
        return Hints.None;
    }
    /// <summary>v7: a support-harmony cube (H's CubeOps.Harmonize: bent nodes, or a same-octave copy of another cube).</summary>
    static bool IsHarmonyCube(AudioCube c)
    {
        if (c == null || c.IsDrums) return false;
        if (c.bend != null) foreach (int b in c.bend) if (b != 0) return true;
        return c.echoOf >= 0 && c.layer == 0;
    }
    /// <summary>v7: the first island of the first column whose ground left 0 (the grids after a stair).</summary>
    static Vector3 GroundPoint()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return Hints.None;
        for (int c = 0; c < sm.ColumnCount; c++)
            if (Mathf.Abs(sm.GroundOf(c)) > 0.01f) { var a = sm.AnchorOf(c); if (a != null) return a.VisualCenter + Vector3.up * 0.35f; }
        return Hints.None;
    }
    /// <summary>v7: the section that filled last — its plinth's front edge under its last measure (the one that just completed it: on screen).</summary>
    static Vector3 SectionPoint() => FilledPoint(filledSection);
    /// <summary>v7: the front edge of section <paramref name="s"/>'s plinth under its last measure (NaN when there is none).</summary>
    public static Vector3 FilledPoint(int s)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || s < 0 || s >= sm.SectionCount) return Hints.None;
        Bounds b;
        if (!SectionHeader.SectionBounds(s, out b)) return Hints.None;
        float w, e, x = b.center.x;
        if (SectionHeader.ColumnSpan(sm.SectionLast(s), out w, out e)) x = (w + e) * 0.5f;
        return new Vector3(x, b.max.y + 0.25f, b.min.z + 0.3f);
    }
    /// <summary>v6 §11: the newest Moon (the moons tip points at it).</summary>
    static Vector3 MoonPoint()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || sm.Moons.Count == 0 || sm.Moons[sm.Moons.Count - 1] == null) return Hints.None;
        return sm.Moons[sm.Moons.Count - 1].VisualCenter + Vector3.up * 0.35f;
    }
    /// <summary>v6: the newest keyboard island (the keyboard tip points at it).</summary>
    static Vector3 KeyboardPoint()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return Hints.None;
        for (int i = sm.Islands.Count - 1; i >= 0; i--) { var kb = sm.Islands[i]; if (kb != null && kb.IsKeyboard) return kb.VisualCenter + Vector3.up * 0.35f; }
        return Hints.None;
    }
    static Vector3 IslandPoint(int i)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return Hints.None;
        if (i != 0 && sm.Islands.Count < 2) return Hints.None;   // F21: "the last island" needs a second one (checked before -1 becomes an index)
        if (i < 0) i = sm.Islands.Count - 1;
        if (i < 0 || i >= sm.Islands.Count || sm.Islands[i] == null) return Hints.None;
        return sm.Islands[i].VisualCenter + Vector3.up * 0.35f;
    }
    /// <summary>The player's newest finished cube (not a Moon groove cube when a user cube exists), else the first finished cube.</summary>
    public static AudioCube UserCube
    {
        get
        {
            AudioCube any = null;
            for (int k = SequenceMaster.Cubes.Count - 1; k >= 0; k--)
            {
                var c = SequenceMaster.Cubes[k];
                if (c == null || !c.isFinalized || c.nodes.Count == 0) continue;
                if (!c.IsOnMoon) return c;
                any = c;
            }
            return any;
        }
    }
    static Vector3 CubePoint()
    {
        var c = UserCube;
        return c != null ? c.transform.position + Vector3.up * 0.2f : Hints.None;
    }
    static Vector3 DraftEndPoint()
    {
        var pm = PathManager.I;
        if (pm == null || !pm.IsDrawing || pm.currentPathTiles.Count == 0) return Hints.None;
        var t = pm.currentPathTiles[pm.currentPathTiles.Count - 1];
        return t != null ? t.Top + Vector3.up * 0.2f : Hints.None;
    }

    // ------------------------------------------------------------------ flow
    void Begin(bool force)
    {
        if (Suppressed) return;
        if (bubble == null) BuildBubble();
        if (!force && Done) return;
        int s = 1;
        if (!force)
        {
            s = Mathf.Clamp(PlayerPrefs.GetInt(PrefStep, 1), 1, StepCount);
            // resume at the start of the chapter (the draft, the inspector, the island steps need their context)
            if (s <= SFinish) s = SDraw; else if (s <= SClose) s = SOpen; else if (s <= SLower) s = SAddBelow; else if (s >= SPresent) s = SPresent;
        }
        else { PlayerPrefs.SetInt(PrefDone, 0); }
        seen.Clear();
        tipId = null; chapterIsland = null;
        SetStep(s);
    }

    void BuildBubble()
    {
        bubble = new TutorialBubble();
        bubble.Build();
        bubble.onNext = NextFromUser;
        bubble.onSkip = () => { if (tipId != null && !Active) EndTip(); else Finish(true); };
    }

    void SetStep(int s)
    {
        if (s > StepCount) { Finish(false); return; }
        step = s; stepT = 0f; closedT = 0f; waitT = 0f; doneShownT = 0f; stepAud0 = PathManager.IdleAuditionCount;
        brushAtStep = PathManager.I != null ? PathManager.I.BrushTicks : -1;
        if (!Suppressed) PlayerPrefs.SetInt(PrefStep, s);
        PinHeader();
    }

    void Advance()
    {
        if (step < 1) return;
        AudioPool.UI(ProceduralAudio.StepDone(), 0.3f);
        SetStep(step + 1);
    }

    void NextFromUser()
    {
        if (!Active) return;
        if (step == StepCount) { Finish(false); return; }
        Advance();
    }

    void Finish(bool skipped)
    {
        bool hadPin = step >= SAddBelow && step <= SLower;
        step = 0; seen.Clear();
        PlayerPrefs.SetInt(PrefDone, 1);
        PlayerPrefs.SetInt(PrefStep, skipped ? PlayerPrefs.GetInt(PrefStep, 1) : StepCount);
        PlayerPrefs.Save();
        if (bubble != null) bubble.Show(false);
        shownTarget = null;
        if (hadPin) IslandHeader.Hide();
    }

    /// <summary>The island chapter keeps the header up on the island it talks about (so its + / grip / ▼ can be pointed at).</summary>
    void PinHeader()
    {
        if (Suppressed || SongManager.I == null || !SongManager.I.HasSong) return;
        if (step == SAddBelow)
        {
            var sm = SongManager.I;
            KeyBlock kb = UserCube != null && UserCube.Island != null && !UserCube.Island.IsMoon ? UserCube.Island : sm.Islands[0];
            IslandHeader.Show(kb);
        }
        else if (step == SDragColumn || step == SLower) IslandHeader.Show(chapterIsland != null ? chapterIsland : (SongManager.I.Islands.Count > 0 ? SongManager.I.Islands[SongManager.I.Islands.Count - 1] : null));
        else if (step == SDeck) IslandHeader.Hide();
    }

    void Handle(string evt)
    {
        if (Suppressed) return;
        if (evt == Ev.IslandAdded || evt == Ev.IslandDuplicated) RememberNewIsland();
        if (!Active) { HandleTipEvent(evt); return; }
        var d = Steps[step - 1];
        if (evt == d.ev || (d.ev2 != null && evt == d.ev2)) { Advance(); return; }
        if (Memorable.Contains(evt)) seen.Add(evt);
        // shortcuts: finishing the draft early skips to "open it"; opening a cube early skips to the inspector chapter; closing the
        // inspector mid-chapter moves on to the islands
        if (evt == Ev.PathFinished && (step == SDraw || step == SLength)) { SetStep(SOpen); return; }
        if (evt == Ev.CubeInspected && step < SOpen) { SetStep(SStretch); return; }
        if (evt == Ev.InspectorClosed && step == SStretch) { SetStep(SAddBelow); return; }
    }

    void RememberNewIsland()
    {
        var ui = UIManager.I; var sm = SongManager.I;
        if (ui != null && sm != null && ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count) chapterIsland = sm.Islands[ui.SelectedMeasure];
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        WatchIslandDrag();
        // the hearing counters are followed every frame (suppressed or not), so a tip only answers an audition that just happened
        int idle = PathManager.IdleAuditionCount, run = PathManager.RunAuditionCount;
        heardNow = idleAudSeen >= 0 && idle > idleAudSeen; runHeardNow = runAudSeen >= 0 && run > runAudSeen;
        idleAudSeen = idle; runAudSeen = run;
        // v6: an empty-hand press played a tile (package H counts them)
        int presses = PathManager.PressCount;
        if (pressSeen >= 0 && presses > pressSeen) Notify(Ev.TilePressed);
        pressSeen = presses;
        // (v5: the tips' own triggers run without a bubble too — a returning player has none until the first tip builds it)
        if (bubble == null && (Active || tipId != null)) BuildBubble();
        bool world = !MainMenu.IsShown && !(InterfaceController.I != null && InterfaceController.I.Visible);
        if (Suppressed || !world) { if (bubble != null) { bubble.Show(false); bubble.Tick(dt); } return; }
        if (Active) UpdateStep(dt);
        else UpdateTips(dt);
        if (bubble != null) bubble.Tick(dt);
    }

    /// <summary>An island drag that ends with the island moved = "dragged" (v5: in z within its column, in x into another column or a new one —
    /// a move that rebuilt the song replaces the block and pushes a History entry, both count; a cancelled drag restores it and counts nothing).</summary>
    void WatchIslandDrag()
    {
        var pm = PathManager.I;
        var drag = pm != null ? pm.Drag : null;
        bool dragging = drag != null && drag.Dragging;
        if (dragging && !dragWatch)
        {
            dragWatch = true; dragPushes = 0; dragBlock = drag.Block;
            dragZ0 = dragBlock != null ? dragBlock.pz : 0f; dragX0 = dragBlock != null ? dragBlock.px : 0f;
            if (dragBlock != null && !dragBlock.IsMoon && drag.DragKind != IslandDrag.Kind.Column) TryTip(TipMove, null, MoveText, true, 3.6f);
        }
        else if (!dragging && dragWatch && (drag == null || drag.State == IslandDrag.Phase.Idle))
        {
            dragWatch = false;
            bool replaced = !ReferenceEquals(dragBlock, null) && dragBlock == null;   // a rebuild destroyed it (a move across columns)
            bool moved = dragBlock != null && (Mathf.Abs(dragBlock.pz - dragZ0) > 0.25f || Mathf.Abs(dragBlock.px - dragX0) > 0.25f);
            if (moved || ((replaced || dragBlock != null) && dragPushes > 0))
            {
                if (dragBlock != null) chapterIsland = chapterIsland ?? dragBlock;
                Notify(Ev.IslandMoved);
            }
            dragBlock = null;
        }
    }

    void UpdateStep(float dt)
    {
        stepT += dt;
        var pm = PathManager.I;
        var d = Steps[step - 1];
        // state rules (what the step asks for is already true)
        if (step == SDraw && pm != null && pm.IsDrawing && pm.currentPathTiles.Count >= 2 && stepT > 0.4f) { Advance(); return; }
        if (step == SLength)
        {
            if (pm != null && brushAtStep >= 0 && pm.BrushTicks != brushAtStep && stepT > 0.2f) { Advance(); return; }
            if (pm != null && !pm.IsDrawing && stepT > 0.3f) { SetStep(UserCube != null ? SOpen : SDraw); return; }
        }
        if (step == SFinish && pm != null && !pm.IsDrawing && stepT > 0.3f) { SetStep(UserCube != null ? SOpen : SDraw); return; }
        if (step == SOpen && CubeInspector.IsOpen) { Advance(); return; }
        if (step == SStretch && !CubeInspector.IsOpen) { closedT += dt; if (closedT > 0.6f) { SetStep(SAddBelow); return; } } else closedT = 0f;
        if (step == SClose && !CubeInspector.IsOpen && stepT > 0.3f) { Advance(); return; }
        if (step == SDeck && IslandTray.IsOpen && shownTarget == "addIsland") { }   // (the target moves to the first card below)
        if (d.ev != null && seen.Contains(d.ev) && stepT > 0.35f) { seen.Remove(d.ev); Advance(); return; }
        if (step >= SAddBelow && step <= SLower && !IslandHeader.IsShown && stepT > 0.2f) PinHeader();

        // the last step waits for the presentation to end, shows 3 s, closes itself
        if (step == StepCount)
        {
            if (Presenter.Active) { bubble.Show(false); return; }
            doneShownT += dt;
            if (doneShownT > 3f) { Finish(false); return; }
        }
        else if (Presenter.Active) { bubble.Show(false); return; }

        // resolve the target (or a precondition hint)
        string tgt = d.target, msg = d.text; bool info = d.info;
        // v5 (package R's hearing tiles): the first step opens with "hover a tile to hear it" until two tiles have been heard (or 7 s);
        // v6: then "pick a cube (the left column), then click tiles" at the instrument column while the hand is empty, and "click a tile and
        // keep clicking" at the island once a cube is in the hand
        if (step == SDraw)
        {
            bool holding = pm != null && (pm.CubeInHand || pm.IsDrawing);
            if (!holding && PathManager.IdleAuditionCount - stepAud0 < 2 && stepT < 7f) { msg = HearText; tgt = "world:island0"; }
            else if (holding) { msg = DrawText; tgt = "world:island0"; }
        }
        if (step == SDeck && IslandTray.IsOpen)
        {
            tgt = "tray.next0"; msg = DeckOpenText;
            if (PlayerPrefs.GetInt(PrefTip + TipVibes, 0) == 0) PlayerPrefs.SetInt(PrefTip + TipVibes, 1);   // told here: the tip will not repeat it
        }
        if (tgt != null && !Hints.IsVisible(tgt))
        {
            if (step == SOpen && UserCube == null) { tgt = "world:island0"; msg = "draw a path first"; }
            else if ((step == SLength || step == SFinish) && (pm == null || !pm.IsDrawing)) { tgt = "world:island0"; msg = "click a tile to start a path"; }
            else if ((step == SStretch || step == SClose) && !CubeInspector.IsOpen) { tgt = "world:cube"; msg = "click your cube to open it"; }
            if (tgt != d.target && !Hints.IsVisible(tgt)) tgt = null;
            if (tgt == d.target)
            {
                waitT += dt;
                if (waitT < 1.2f) { bubble.Show(false); return; }   // give the target a moment (panels sliding in, the header fading in)
                tgt = null; info = true;                             // still missing: centred, with a next arrow
            }
        }
        else waitT = 0f;
        ShowBubble("step" + step + "|" + (tgt ?? "centre") + "|" + msg, tgt, msg, info || step == StepCount, true, step - 1, StepCount);
    }

    void ShowBubble(string key, string tgt, string msg, bool next, bool skip, int dot, int dots)
    {
        shownTarget = tgt;
        if (key != lastKey || bubble.Key != key)
        {
            bubble.SetContent(key, msg, null, next, skip, dot, dots);
            if (key != lastKey) AudioPool.UI(ProceduralAudio.Blip(), 0.35f);
            lastKey = key;
        }
        PointAt(tgt);
        bubble.Show(true);
    }
    string lastKey;

    void PointAt(string tgt)
    {
        CollectObstacles(tgt);
        if (tgt == null) { bubble.SetTarget(false, default(Rect), false, false, Vector2.zero); return; }
        bool world = Hints.IsWorld(tgt);
        Rect r;
        // islands: point at the whole projected platform (the balloon then sits beside it, not on it)
        if (tgt == "world:island0" || tgt == "world:islandLast")
        {
            var sm = SongManager.I;
            int idx = sm != null ? (tgt == "world:island0" ? 0 : sm.Islands.Count - 1) : -1;
            Rect ir;
            if (sm != null && idx >= 0 && idx < sm.Islands.Count && sm.Islands[idx] != null && IslandScreenRect(sm.Islands[idx], out ir))
            {
                bubble.SetTarget(true, ir, true, false, ir.center);
                return;
            }
        }
        if (world)
        {
            Vector3 sp;
            if (Hints.TryGetScreenPoint(tgt, out sp))
            {
                bool off = sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height;
                float b = Hints.WorldBoxPx;
                r = new Rect(sp.x - b * 0.5f, sp.y - b * 0.5f, b, b);
                bubble.SetTarget(true, r, true, off, new Vector2(sp.x, sp.y));
                return;
            }
            bubble.SetTarget(false, default(Rect), false, false, Vector2.zero);
            return;
        }
        if (Hints.TryGetScreenRect(tgt, out r)) bubble.SetTarget(true, r, false, false, Vector2.zero);
        else bubble.SetTarget(false, default(Rect), false, false, Vector2.zero);
    }

    static readonly Vector3[] corners8 = new Vector3[8];
    /// <summary>Screen rect (px) of an island's platform; false when it is behind the camera or mostly off screen.</summary>
    public static bool IslandScreenRect(KeyBlock kb, out Rect r)
    {
        r = default(Rect);
        var cam = Camera.main;
        if (cam == null || kb == null) return false;
        var b = kb.VisualBounds;   // v5: where the platform is shown (a repeat belt rides it forward while playing)
        Vector3 c = b.center, e = new Vector3(b.extents.x, 0.35f, b.extents.z);
        c.y = kb.VisualCenter.y + 0.2f;
        int k = 0;
        for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2) corners8[k++] = c + Vector3.Scale(e, new Vector3(sx, sy, sz));
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 s = cam.WorldToScreenPoint(corners8[i]);
            if (s.z <= 0f) return false;
            x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
        }
        r = Rect.MinMaxRect(x0, y0, x1, y1);
        float ix = Mathf.Max(0f, Mathf.Min(x1, Screen.width) - Mathf.Max(x0, 0f)), iy = Mathf.Max(0f, Mathf.Min(y1, Screen.height) - Mathf.Max(y0, 0f));
        return ix * iy >= 0.4f * r.width * r.height;
    }

    /// <summary>Visible HUD parts the balloon should not cover (everything registered, except the target and what lies inside it).</summary>
    void CollectObstacles(string tgt)
    {
        bubble.Obstacles.Clear();
        Rect tr = default(Rect);
        bool hasT = tgt != null && Hints.RectOf(tgt) != null && Hints.TryGetScreenRect(tgt, out tr);
        foreach (var id in Hints.Ids)
        {
            if (id == tgt || Hints.RectOf(id) == null || !Hints.IsVisible(id)) continue;
            if (id == "tray" && tgt != null && tgt.StartsWith("tray.", StringComparison.Ordinal)) continue;   // v5: a card's own hand is not in the way (the bubble may sit above it)
            Rect r;
            if (!Hints.TryGetScreenRect(id, out r)) continue;
            if (r.width > Screen.width * 0.8f || r.height > Screen.height * 0.8f) continue;   // full-screen layers are not obstacles
            if (hasT && r.xMin >= tr.xMin - 1f && r.xMax <= tr.xMax + 1f && r.yMin >= tr.yMin - 1f && r.yMax <= tr.yMax + 1f) continue;
            bubble.Obstacles.Add(r);
        }
    }

    // ------------------------------------------------------------------ contextual tips (after the tutorial, each once)
    void HandleTipEvent(string evt)
    {
        if (!Done || Presenter.Active) return;   // S9: no tips over the presentation (entering it starts the clock: "play")
        HandleV7Tip(evt);
        if (evt == Ev.CubeHovered) TryTip("hover", null, "click to open", true);
        else if (evt == Ev.TrayOpened)
        {
            TryTip(TipVibes, "tray.next0", VibesText, false, 5.5f);
            if (PlayerPrefs.GetInt(PrefTip + TipVibes, 0) == 1) TryTip(TipJobs, Hints.Has("tray.badge0") ? "tray.badge0" : "tray.next0", JobsText, false, 5.5f);   // v6: a later open, at the first card's badge
        }
        else if (evt == Ev.TilePressed) TryTip(TipPress, null, PressText, true, 3.6f);
        else if (evt == Ev.PatternCopied) TryTip(TipCopy, Hints.Has("clipboard") ? "clipboard" : null, CopyText, !Hints.Has("clipboard"), 4.5f);   // U2's clipboard chip (it pops up with the copy)
        else if (evt == Ev.IslandCarry) { EndTipIf(TipRepeat); TryTip(TipCarry, "island.carry", CarryText, false, 4.5f); }
        else if (evt == Ev.KeyboardAdded) TryTip(TipKeyboard, "world:keyboard", KeyboardText, false, 4.5f);
        else if (evt == Ev.MoonAdded) TryTip(TipMoons, "world:moonLast", MoonsText, false, 4.5f);
        else if (evt == Ev.IslandRegister && IslandHeader.Current != null && IslandHeader.Current.register > 0) TryTip(TipTower, "island.octave", TowerText, false, 4.5f);
        else if (evt == Ev.IslandRepeat) EndTipIf(TipRepeat);
        else if (evt == Ev.PlayStarted && UserCubeCount() == 0 && SongManager.I != null && SongManager.I.HasSong) TryTip("play", "world:island0", "draw a path first", false);
        else if (evt == Ev.IslandsMerged) EndTipIf("merge");
        else if (evt == Ev.CubeInspected) EndTipIf("hover");
        else if (evt == Ev.PathFinished) EndTipIf("play");
    }

    static int UserCubeCount() { int k = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsOnMoon) k++; return k; }

    // ------------------------------------------------------------------ v7: what a gesture changed (SPEC v7 §7.5, §13.4)
    /// <summary>The section that filled last (the sections tip and its sparkle point at it; -1 none).</summary>
    static int filledSection = -1;
    /// <summary>Sections that reached 4 measures through a gesture (tests: the sparkle count).</summary>
    public static int SectionsFilled { get; private set; }
    /// <summary>The last section that filled (-1 none; tests).</summary>
    public static int LastFilledSection => filledSection;
    struct Seen7 { public int stairs, phrases, layers, bent, grounds; public bool any; }
    Seen7 seen7;
    readonly List<int> sectionBars = new List<int>();
    int lastUndo = -1, lastRedo = -1;

    /// <summary>History changed: a gesture (a push) compares the song with the last one and reports what is new (a stairs island, a phrase, an
    /// echo / harmony cube, a ground that left 0, a section that reached 4 measures); a load (the stack reset) or an undo / redo only takes the
    /// new state as the baseline.</summary>
    void Observe()
    {
        var sm = SongManager.I;
        int undo = History.UndoCount, redo = History.RedoCount;
        bool gesture = seen7.any && undo > 1 && redo == 0 && (undo > lastUndo || undo >= 150) && lastRedo == 0;
        lastUndo = undo; lastRedo = redo;
        if (sm == null || !sm.HasSong) { seen7 = default(Seen7); sectionBars.Clear(); return; }
        var now = new Seen7 { any = true };
        foreach (var kb in sm.Islands) { if (kb == null) continue; if (kb.IsStairs) now.stairs++; if (kb.IsPhrase) now.phrases++; }
        foreach (var c in SequenceMaster.Cubes) { if (c == null || !c.isFinalized) continue; if (c.layer != 0) now.layers++; if (IsHarmonyCube(c)) now.bent++; }
        for (int c = 0; c < sm.ColumnCount; c++) if (Mathf.Abs(sm.GroundOf(c)) > 0.01f) now.grounds++;
        int ns = sm.SectionCount, newly = -1;
        for (int s = 0; s < ns; s++)
        {
            int bars = sm.SectionBars(s);
            bool was = s < sectionBars.Count && sectionBars[s] >= ProjectConfig.SectionBars;
            if (gesture && bars >= ProjectConfig.SectionBars && !was && s < sectionBars.Count && newly < 0) newly = s;
        }
        sectionBars.Clear();
        for (int s = 0; s < ns; s++) sectionBars.Add(sm.SectionBars(s));
        var before = seen7; seen7 = now;
        if (!gesture) return;
        if (now.stairs > before.stairs) Notify(Ev.StairsAdded);
        if (now.phrases > before.phrases) Notify(Ev.PhraseAdded);
        if (now.layers > before.layers) Notify(Ev.OctaveCopied);
        if (now.bent > before.bent) Notify(Ev.HarmonyAdded);
        if (now.grounds > 0 && before.grounds == 0) QueueTip(TipHeights, "world:groundFirst", HeightsText, false, 4.8f);
        if (newly >= 0)
        {
            filledSection = newly; SectionsFilled++;
            if (UIManager.I != null && UIManager.I.Rail != null) UIManager.I.Rail.Sparkle(newly);
            var fp = FilledPoint(newly);
            if (!float.IsNaN(fp.x)) Fx.Starburst(fp, Comic.Pop, 1.2f);   // at the measure that completed it
            AudioPool.UI(ProceduralAudio.Sparkle(), 0.35f, 1.15f);
            Notify(Ev.SectionFilled);
        }
    }

    // ------------------------------------------------------------------ v7: a short queue for the v7 tips
    struct PendingTip { public string id, target, text; public bool atPointer; public float life, at; }
    readonly List<PendingTip> pending = new List<PendingTip>();
    /// <summary>v7 tips waiting for the bubble (tests).</summary>
    public static int PendingTips => I != null ? I.pending.Count : 0;
    /// <summary>Tests: drops the waiting v7 tips (and the tip on screen).</summary>
    public static void ClearTips() { if (I == null) return; I.pending.Clear(); if (I.tipId != null) I.EndTip(); }

    /// <summary>A v7 tip: shown now when nothing else is up, else it waits (≤ 20 s) — each once, like every tip.</summary>
    void QueueTip(string id, string target, string msg, bool atPointer, float life)
    {
        if (Suppressed || !Done || Active || PlayerPrefs.GetInt(PrefTip + id, 0) == 1) return;
        if (tipId == null) { TryTip(id, target, msg, atPointer, life); return; }
        if (tipId == id) return;
        foreach (var p in pending) if (p.id == id) return;
        pending.Add(new PendingTip { id = id, target = target, text = msg, atPointer = atPointer, life = life, at = Time.unscaledTime });
    }

    void PumpPending()
    {
        if (tipId != null || pending.Count == 0 || Suppressed || !Done || Active) return;
        while (pending.Count > 0)
        {
            var p = pending[0]; pending.RemoveAt(0);
            if (Time.unscaledTime - p.at > 20f || PlayerPrefs.GetInt(PrefTip + p.id, 0) == 1) continue;
            TryTip(p.id, p.target, p.text, p.atPointer, p.life);
            return;
        }
    }

    /// <summary>The v7 gesture events → their tips (after the tutorial; SPEC v7 §7.5).</summary>
    void HandleV7Tip(string evt)
    {
        if (evt == Ev.StairsAdded) QueueTip(TipStairs, "world:stairsLast", StairsText, false, 5f);
        else if (evt == Ev.PhraseAdded) QueueTip(TipMelody, "world:phraseLast", MelodyText, false, 5f);
        else if (evt == Ev.OctaveCopied) QueueTip(TipEchoes, "world:echoLast", EchoesText, false, 4.5f);
        else if (evt == Ev.HarmonyAdded) QueueTip(TipHarmony, "world:harmonyLast", HarmonyText, false, 4.5f);
        else if (evt == Ev.RepeatStyle) QueueTip(TipRewind, Hints.IsVisible("island.style") ? "island.style" : "island.repeat", RewindText, false, 4.8f);
        else if (evt == Ev.IslandFlow) QueueTip(TipFlow, "island.extend", FlowText, false, 4.5f);   // §21: the fill-the-section tab (one long grid)
        else if (evt == Ev.IslandLaunch) QueueTip(TipLaunch, Hints.IsVisible("island.launch") ? "island.launch" : null, LaunchText, !Hints.IsVisible("island.launch"), 4.5f);
        else if (evt == Ev.CubeFlipped) QueueTip(TipFlip, Hints.IsVisible("inspector.flip") ? "inspector.flip" : null, FlipText, !Hints.IsVisible("inspector.flip"), 4f);
        else if (evt == Ev.GridsSelected) QueueTip(TipSelect, Hints.IsVisible("selectionBar") ? "selectionBar" : null, SelectText, !Hints.IsVisible("selectionBar"), 4.5f);
        else if (evt == Ev.SectionFilled) QueueTip(TipSections, "world:sectionFilled", SectionsText, false, 5.5f);
        else if (evt == Ev.SectionEdited) QueueTip(TipSections, null, SectionsText, true, 5.5f);
        else if (evt == Ev.SectionNamed) { EndTipIf(TipNames); if (PlayerPrefs.GetInt(PrefTip + TipNames, 0) == 0) PlayerPrefs.SetInt(PrefTip + TipNames, 1); }
        else if (evt == Ev.CapacityReached) QueueTip(TipCapacity, Hints.IsVisible("draft.expand") ? "draft.expand" : null, CapacityText, !Hints.IsVisible("draft.expand"), 4.8f);
        else if (evt == Ev.KeysExtended) QueueTip(TipKeys, "world:keyboard", KeysText, false, 4.5f);
        else if (evt == Ev.ShortcutsOpened) { EndTipIf(TipShortcuts); if (PlayerPrefs.GetInt(PrefTip + TipShortcuts, 0) == 0) PlayerPrefs.SetInt(PrefTip + TipShortcuts, 1); }
        else if (evt == Ev.CubeInspected && PlayerPrefs.GetInt(PrefTip + "hover", 0) == 1)
            QueueTip(TipShortcuts, Hints.IsVisible("shortcuts") ? "shortcuts" : null, ShortcutsText, !Hints.IsVisible("shortcuts"), 4f);
    }

    /// <summary>v7 (tests / the section header): the first section-header hover tells the sections line too.</summary>
    public static void SectionHeaderShown()
    {
        if (I == null || Suppressed) return;
        if (PlayerPrefs.GetInt(PrefTip + TipSections, 0) == 0) I.QueueTip(TipSections, null, SectionsText, true, 5.5f);
        else I.QueueTip(TipNames, Hints.IsVisible("section.name") ? "section.name" : null, NamesText, !Hints.IsVisible("section.name"), 4.5f);   // §17.1: the next time, naming
    }

    void TryTip(string id, string target, string msg, bool atPointer, float life = 3.2f)
    {
        if (Suppressed || tipId != null || !Done || Active || PlayerPrefs.GetInt(PrefTip + id, 0) == 1) return;
        if (bubble == null) BuildBubble();
        PlayerPrefs.SetInt(PrefTip + id, 1);
        tipId = id; tipT = 0f; tipTarget = target; tipAtScreen = atPointer; tipLife = life;
        bubble.SetContent("tip|" + id, msg, null, false, true, 0, 0);
        lastKey = "tip|" + id;
        AudioPool.UI(ProceduralAudio.Blip(), 0.3f);
    }
    void EndTip() { tipId = null; if (bubble != null) bubble.Show(false); }
    void EndTipIf(string id) { if (tipId == id) EndTip(); }

    float headerGoneT;   // seconds the island header has been hidden (the carry / tower tips end only when it stays gone)

    void UpdateTips(float dt)
    {
        // "push together to merge": the first island drag that comes near another island
        if (Done && tipId == null && PlayerPrefs.GetInt(PrefTip + "merge", 0) == 0 && PathManager.I != null && PathManager.I.Drag != null && PathManager.I.Drag.Dragging)
        {
            var drag = PathManager.I.Drag;
            if (drag.Block != null && (drag.MergeTarget != null || drag.NearestGap < 3f)) TryTip("merge", null, "push together to merge", true);
        }
        // v5 "repeat": the first time an island's header stays up under the pointer (on that island or on the header; not while dragging /
        // drawing), point at its repeat button
        var hdr = IslandHeader.I; var pmT = PathManager.I;
        bool headerUp = hdr != null && IslandHeader.IsShown && hdr.Shown > 0.9f && IslandHeader.Current != null && !IslandHeader.Current.IsMoon && !IslandHeader.MoreOpen
                        && pmT != null && !pmT.IsDrawing && (pmT.Drag == null || !pmT.Drag.Busy) && (hdr.PointerOver || pmT.hoverIsland == IslandHeader.Current);
        headerUpT = headerUp ? headerUpT + dt : 0f;
        if (Done && tipId == null && headerUpT > 0.8f && PlayerPrefs.GetInt(PrefTip + TipRepeat, 0) == 0 && Hints.IsVisible("island.repeat")) TryTip(TipRepeat, "island.repeat", RepeatText, false, 4.5f);
        if (tipId == TipRepeat && !IslandHeader.IsShown) EndTip();
        // v6: a keyboard's header up under the pointer (a keyboard from a loaded song) tells what a keyboard is, once
        if (Done && tipId == null && headerUpT > 0.8f && IslandHeader.Current != null && IslandHeader.Current.IsKeyboard && PlayerPrefs.GetInt(PrefTip + TipKeyboard, 0) == 0) TryTip(TipKeyboard, "island.header", KeyboardText, false, 4.5f);
        // a carry rebuilds the island: its header re-shows on the rebuilt island a frame or two later — only a header that stays gone ends the tip
        headerGoneT = IslandHeader.IsShown ? 0f : headerGoneT + dt;
        if ((tipId == TipCarry || tipId == TipTower || tipId == TipFlow || (tipId == TipRewind && tipTarget != null && tipTarget.StartsWith("island.", StringComparison.Ordinal))
             || (tipId == TipLaunch && tipTarget != null)) && headerGoneT > 0.35f) EndTip();
        PumpPending();
        // v5 hearing tips: the first tile a hover plays, the first run a drafting hover plays (at the pointer)
        if (heardNow && Done && tipId == null) TryTip(TipHear, null, HearTipText, true, 3.6f);
        if (runHeardNow && Done && tipId == null) TryTip(TipDraftRun, null, DraftRunText, true, 4.2f);
        if ((tipId == TipVibes || tipId == TipJobs) && !IslandTray.IsOpen) EndTip();
        if (tipId == TipDraftRun && (pmT == null || !pmT.IsDrawing)) EndTip();
        if (tipId == null || Presenter.Active) { if (bubble != null) bubble.Show(false); return; }   // S9: a tip waits out the presentation (its time too)
        tipT += dt;
        if (tipT > tipLife) { EndTip(); return; }
        if (tipId == TipDraftRun && MelodyLine.Shown && MelodyLine.ScreenRect.width > 1f)
        {
            // "the little card draws your tune": point at R's melody card (screen px), keeping clear of the length row and the rest of the HUD
            CollectObstacles(null);
            bubble.SetTarget(true, MelodyLine.ScreenRect, false, false, Vector2.zero);
        }
        else if (tipAtScreen)
        {
            Vector3 m = Input.mousePosition;
            float b = 40f;
            bubble.SetTarget(true, new Rect(m.x - b * 0.5f, m.y - b * 0.5f, b, b), false, false, Vector2.zero);
        }
        else PointAt(tipTarget);
        shownTarget = tipTarget;
        bubble.Show(true);
    }
}
