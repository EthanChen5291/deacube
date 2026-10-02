using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SPEC v4 §2.5 / §5, research §4.5: the island "component header". A paper ribbon banner (Melatonin's "DREAM ABOUT" ribbon) pinned on the
/// middle of the hovered island's front edge, re-projected every LateUpdate, turned with the edge (±6°), notched tails in the chord colour.
/// Left to right, 30 px pictures: grip (2×3 mini cubes: press = move the island anywhere — v5: left / right into other columns too —
/// IslandDrag.Grip; the 4-way move cursor), eye (open = on, closed = off: SetSleep), register ▼ ladder ▲ (the filled rung is the register:
/// SetRegister), v5 repeat (a little conveyor belt carrying the island; a ×2 / ×3 / ×4 sticker, plain at ×1: SetRepeat cycles 1 → 2 → 3 → 4
/// → 1), duplicate → (a new column) and ↓ (same column) (DuplicateIsland), more ⋯ (a small attached card: chord wheel, weather (mood: the
/// vibe glyphs), fill, length blocks, energy, fall style, delete). With it, two
/// dotted ghost islands dock above and below the island on screen, each with a +: AddIslandInColumn (a full column shakes the ghost).
/// Moons get grip, dice (re-groove) and more ⋯ (delete). Timing: appears after 0.15 s of hovering the island, lingers 0.4 s after the
/// pointer leaves (the header itself holds it), stays while the island is selected or pinned (<see cref="Show"/>); hidden while drawing,
/// dragging, presenting, inspecting, in a menu and during fast camera moves; fades in two stepped drawings. Every gesture that changes the
/// song ends in exactly one History entry (the ops push; a deduplicated safety push covers ops that do not).
/// v6 (SPEC v6 §7.3): a CARRY button after repeat — a wand throwing an arc onto the next island, a ×1 / ×2 / ×3 sticker (plain at 0) —
/// cycles 0 → 1 → 2 → 3 → 0 through SongManager.SetCarry ("carry to next chord": the island's patterns also play on the next chords, fitted
/// to them; the last column has no next chord and refuses with a thud). Keyboard islands get grip, eye, register, repeat, carry, duplicate
/// and more (delete only: no chord wheel, weather, fill, length, energy or fall; no + ghosts), their tails in wood. While the clipboard holds
/// a pattern that fits the island (Clipboard.CanPasteOn) a "paste here" tab stands on the ribbon (Clipboard.PasteOn: package H's adapted
/// paste, one History entry).
/// v7 (SPEC v7 §7.2 / §7.3 / §13.5 / §14.6 / §17.2 — the user's reel: a run falling into the next chord, the same grid replayed "with time
/// unwinding", a pattern that "flows" through the chords, cubes "flung … to the next grid"): every grid's band (chord islands and keyboards)
/// grows to 492 and gains FLOW (≈ waves beside the carry wand, cycling 0 → 3 in flow style: SongManager.SetCarry(at, n, 1); the carry wand now
/// sets the hop style) and LAUNCH (a spring arrow ↗: SetLaunch); from ×2 on two small stickers stand on the ribbon over the repeat button — the
/// repeat STYLE (a clock ⟲: time unwinds instead of the belt, SetRewind) and the VARY dice (same / vary / answer, SetVary) — and while more
/// columns follow in its section a "FILL THE SECTION" tab stands over flow (flow to the section's last column in one click: SPEC §13.5). The
/// STAIRS island (kind 3) gets its own band: grip, eye, TYPE (its glyph; a click opens a fan of the six — chord, scale, spark, slide, bright,
/// walk — hover plays that run, a gold star on the one that fits the chord best: Harmony.StairFit), DIRECTION ↓ / ↑, STEPS − n +, RATE (a
/// cube's size: big = slow steps), LEAD-IN (the run ends at the chord change / starts with it), register, repeat, more (duplicate, delete);
/// every change = SongOps.SetStair. The melody PHRASE (kind 4): grip, eye, LENGTH ½ 1 2 4 + auto (fits its notes), DOUBLE (twice as long, the
/// notes repeated: SongOps.DoublePhrase), CELLS (wide / fine), register, repeat, more (duplicate, delete).
/// §21 (the user: "the cubes jumping form grid to grid doesnt look good - scrap that actually just keep cubes on each grid. only explore grids
/// moving from left to right with the long grid"): the carry wand and the flow waves are ONE control, EXTEND → (a grid stretched to the right
/// with an arrow; ×1 → ×2 → ×3 → off, as far as its section reaches: SetCarry(at, n, 1) — the LONG GRID, its cubes walk on through the next
/// chords); "fill the section" extends it to the section's end; LAUNCH ↗ is a build-up (a swell rising into an arrow, a crash at its tip: the
/// riser into the next section and its crash) — nothing flies to another grid.
/// §20.1 (the user: "for the piano keyboard, it should also be extensible if needed (like if a melody goes beyond an octave)"): a keyboard's
/// ribbon carries a sticker on each end — "+ octave below" on its low (left) end, "+ octave above" on its high (right) end, a little piano with
/// an arrow growing out of that side (SongManager.SetKeyRange: one History entry, the notes keep their pitch) — and a "fit to the melody" tab
/// while its notes use fewer octaves than it has (whole octaves from its lowest key, at least one).
/// </summary>
public class IslandHeader : MonoBehaviour
{
    public static IslandHeader I;
    /// <summary>The island whose header is on screen (null = none).</summary>
    public static KeyBlock Current { get; private set; }
    public static bool IsShown => Current != null;
    /// <summary>The grip is pressed (its drag has not begun yet): the cursor closes into the mitten.</summary>
    public static bool GripHeld { get; private set; }
    /// <summary>v7: the header is up only because its island is selected (not hovered, held or pinned) — a hovered section plinth's header may
    /// take the screen then (SectionHeader).</summary>
    public static bool BySelection { get; private set; }
    bool shownByHover;
    /// <summary>The attached "more" card is open.</summary>
    public static bool MoreOpen => I != null && I.moreOpen;

    /// <summary>Pins the header on <paramref name="kb"/> (the tutorial, tests) until <see cref="Hide"/> or another pin.</summary>
    public static void Show(KeyBlock kb) { if (I != null) { I.pinned = kb; I.lingerT = 0f; IndexOf(kb, out I.pinIdx, out I.pinMoon); } }
    /// <summary>Unpins and hides the header (it fades out).</summary>
    public static void Hide() { if (I != null) { I.pinned = null; I.hoverCand = null; I.lingerT = 9f; I.SetMore(false); I.target = null; } }
    public static void CloseMore() { if (I != null) I.SetMore(false); }

    public const float ShowDelay = 0.15f, Linger = 0.4f, FastPx = 1500f;

    // ------------------------------------------------------------------ test / tutorial access
    public HudButton Grip => gripBtn;
    public HudButton Eye => eyeBtn;
    public HudButton RegisterDown => regDownBtn;
    public HudButton RegisterUp => regUpBtn;
    /// <summary>v5: the repeat button (cycles the island's passes 1 → 2 → 3 → 4 → 1).</summary>
    public HudButton Repeat => repeatBtn;
    /// <summary>v6: the carry button (cycles 0 → 1 → 2 → 3 → 0) and the "paste here" tab (shown while the clipboard's pattern can go here).</summary>
    public HudButton Carry => carryBtn;
    public HudButton PasteHereButton => pasteBtn;
    /// <summary>v7: the copy-paths / paste-paths stickers (tests) and whether they show.</summary>
    public HudButton CopyPathsButton => copyPathsBtn;
    public HudButton PastePathsButton => pastePathsBtn;
    public bool CopyPathsShown => copyPathsBtn != null && copyPathsBtn.gameObject.activeSelf;
    public bool PastePathsShown => pastePathsBtn != null && pastePathsBtn.gameObject.activeSelf;
    /// <summary>v7: grids whose paths the header copied (tests).</summary>
    public static int CopyPathsCount { get; private set; }
    public bool PasteShown => pasteBtn != null && pasteBtn.gameObject.activeSelf;
    /// <summary>v6: the carry sticker's text ("" at 0, when it is hidden) and times the carry button refused (the last column) — tests.</summary>
    public string CarrySticker => carrySticker != null && carrySticker.gameObject.activeSelf && carryText != null ? carryText.text : "";
    public int CarryRefusals { get; private set; }
    // v7 (SPEC v7 §7.2 / §7.3 / §14.6)
    /// <summary>v7: the repeat style sticker (⟲ rewind on / off), the vary dice (same → vary → answer), flow (0 → 3 in flow style), "fill the
    /// section", launch — shown for chord islands and keyboards (style / vary also on phrases, from ×2 on).</summary>
    public HudButton Style => styleBtn;
    public HudButton Vary => varyBtn;
    public HudButton Flow => carryBtn;
    public HudButton FillSection => fillSecBtn;
    public HudButton Launch => launchBtn;
    public bool StyleShown => styleBtn != null && styleBtn.gameObject.activeInHierarchy;
    public bool VaryShown => varyBtn != null && varyBtn.gameObject.activeInHierarchy;
    public bool FillShown => fillSecBtn != null && fillSecBtn.gameObject.activeInHierarchy;
    public bool FlowShown => carryBtn != null && carryBtn.gameObject.activeInHierarchy;
    public bool LaunchShown => launchBtn != null && launchBtn.gameObject.activeInHierarchy;
    /// <summary>v9 (L) the stage lights: the LEAD spotlight button (chord grids: SetLead) and whether it shows.</summary>
    public HudButton LeadSpot => spotBtn;
    public bool LeadSpotShown => spotBtn != null && spotBtn.gameObject.activeInHierarchy;
    public const string LeadCaptionOff = "lead · spotlight this grid's melody", LeadCaptionOn = "lead · this grid is the melody";
    /// <summary>§21: carry and flow are ONE control now — EXTEND → (the long grid); Flow / FlowSticker / FlowShown are kept as its aliases.</summary>
    public HudButton Extend => carryBtn;
    public string ExtendSticker => CarrySticker;
    public string FlowSticker => CarrySticker;
    /// <summary>v7: times flow / fill / launch refused (nothing after this column, nowhere to land).</summary>
    public int V7Refusals { get; private set; }
    /// <summary>v7 stairs: the type button (opens the fan), the fan's six (one per type), direction, steps − / +, rate, lead-in.</summary>
    public HudButton StairType => typeBtn;
    public HudButton FanItem(int type) => type >= 0 && type < 6 ? fanBtns[type] : null;
    public bool FanOpen => fanOpen;
    public HudButton StairDir => dirBtn;
    public HudButton StepsDown => stepsDownBtn;
    public HudButton StepsUp => stepsUpBtn;
    public HudButton StairRate => rateBtn;
    public HudButton StairLead => leadBtn;
    /// <summary>v7 stairs: the digit between − and +, the type whose fan item wears the gold star, runs auditioned and the last one's pitches.</summary>
    public string StepsShown => stepsText != null ? stepsText.text : "";
    public int FanStar => fanStar;
    public int RunAuditions { get; private set; }
    public int[] LastRun { get; private set; }
    public int LastRunSlot { get; private set; }
    /// <summary>v7 phrase: the length buttons (0 ½, 1 one measure, 2 two, 3 four, 4 auto), double, cells.</summary>
    public HudButton PhraseLength(int k) => k >= 0 && k < 5 ? lenBtns[k] : null;
    public HudButton PhraseDouble => doubleBtn;
    public HudButton PhraseCells => cellsBtn;
    /// <summary>§20.1: a keyboard's range items — + an octave below / above, fit to the melody (shown while its notes use fewer octaves).</summary>
    public HudButton KeysBelow => keysBelowBtn;
    public HudButton KeysAbove => keysAboveBtn;
    public HudButton KeysFit => keysFitBtn;
    public bool KeysFitShown => keysFitBtn != null && keysFitBtn.gameObject.activeInHierarchy;
    /// <summary>v7: the duplicate buttons a stairs / phrase shows in its more card.</summary>
    public HudButton MoreDuplicateRight => moreDupRight;
    public HudButton MoreDuplicateBelow => moreDupBelow;
    /// <summary>v7: the kind the band is laid out for (0 chord, 1 Moon, 2 keyboard, 3 stairs, 4 phrase; -1 none).</summary>
    public int KindShown => shownKind;
    /// <summary>v7: the names of the band's controls on screen now, left to right (tests: the item set per kind).</summary>
    public List<string> ItemsShown()
    {
        var r = new List<string>();
        var items = new List<RectTransform>();
        foreach (Transform t in band) if (t.gameObject.activeSelf && t.GetComponent<HudButton>() != null) items.Add((RectTransform)t);
        items.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
        foreach (var t in items) r.Add(t.name);
        return r;
    }
    /// <summary>v5: the sprites of the more card's weather (mood) and chord glyphs ("ink_&lt;glyph&gt;|&lt;outline&gt;") — tests.</summary>
    public string MoodGlyphName => moodGlyph != null && moodGlyph.sprite != null ? moodGlyph.sprite.name : "";
    public string ChordGlyphName => chordGlyph != null && chordGlyph.sprite != null ? chordGlyph.sprite.name : "";
    /// <summary>v5: the ×n sticker's text ("" at ×1, when it is hidden) — tests.</summary>
    public string RepeatSticker => repeatSticker != null && repeatSticker.gameObject.activeSelf && repeatText != null ? repeatText.text : "";
    public HudButton DuplicateRight => dupRightBtn;
    public HudButton DuplicateBelow => dupBelowBtn;
    public HudButton More => moreBtn;
    public HudButton AddAbove => addAboveBtn;
    public HudButton AddBelow => addBelowBtn;
    public HudButton ChordButton => chordBtn;
    public HudButton ClimbButton => climbBtn;
    public HudButton MoodButton => moodBtn;
    public HudButton FillButton => fillBtn;
    public HudButton EnergyButton => energyBtn;
    public HudButton FallButton => fallBtn;
    public HudButton DeleteButton => deleteBtn;
    public HudButton DiceButton => diceBtn;
    public HudButton BarsButton(int bars) => bars >= 1 && bars <= 4 ? barBtns[bars - 1] : null;
    public RectTransform Ribbon => ribbon;
    /// <summary>0 hidden … 1 fully shown.</summary>
    public float Shown => shownA;
    /// <summary>v5: the pointer is on the header (its band, a button, the more card or a + ghost).</summary>
    public bool PointerOver => Hovering();
    /// <summary>The island the header's buttons act on (the one on screen, else the selected one).</summary>
    public KeyBlock Target => ResolveTarget();
    /// <summary>Times a + ghost refused because its column was full (tests: the shake).</summary>
    public int Refusals { get; private set; }

    RectTransform root, ribbon, band, more, regNode, dupNode, plusAbove, plusBelow, plusLeft, plusRight;
    InkShape bandShape, tailL, tailR, moreShape; InkPainter ghostAbove, ghostBelow;
    CanvasGroup group, moreGroup;
    HudButton gripBtn, eyeBtn, regDownBtn, regUpBtn, dupRightBtn, dupBelowBtn, moreBtn, addAboveBtn, addBelowBtn, diceBtn, repeatBtn;
    HudButton climbBtn; InkPainter climbArt; int shownClimb = -1;
    InkPainter repeatArt; RectTransform repeatSticker, repeatMinus, carryMinus; TMPro.TextMeshProUGUI repeatText; float repeatPop;
    // v6: carry, the keyboard's item set, paste here
    HudButton carryBtn, pasteBtn; InkPainter carryArt, pasteArt; RectTransform carrySticker; TMPro.TextMeshProUGUI carryText; float carryPop, carryShake;
    // v7 (the user: "copy a certain grid's paths to a different chord progression … make it a button"): copy all paths / paste them here
    HudButton copyPathsBtn, pastePathsBtn; InkPainter copyPathsArt, pastePathsArt; TMPro.TextMeshProUGUI pastePathsText; RectTransform pastePathsBadge; int shownPathsKey = -1; float copyPathsPop;
    readonly List<GameObject> chordOnly = new List<GameObject>(); RectTransform deleteRt; bool wasKeyboard; int shownCarry = -1, shownPasteKey = int.MinValue;
    HudButton chordBtn, moodBtn, fillBtn, energyBtn, fallBtn, deleteBtn;
    readonly HudButton[] barBtns = new HudButton[4];
    InkPainter eyeArt, ladderArt, regDownArt, regUpArt, energyArt; Image chordGlyph, moodGlyph, fallGlyph; InkShape chordDisc, fillDisc;
    readonly List<InkPainter> barArts = new List<InkPainter>();
    readonly List<GameObject> islandOnly = new List<GameObject>(), moonOnly = new List<GameObject>();
    /// <summary>v6: the tails / picture colour of a keyboard island (its wood platform, not a vibe colour).</summary>
    public static readonly Color KeyboardWood = new Color(0.78f, 0.57f, 0.38f);
    KeyBlock pinned, target, hoverCand, shownFor; float hoverT, lingerT = 9f, shownA; int tick = int.MinValue;
    // islands are rebuilt by many ops (bars, mood, chord, undo): the header follows its island by index across a rebuild
    int pinIdx = -1, pinMoon = -1, tgtIdx = -1, tgtMoon = -1;

    static void IndexOf(KeyBlock kb, out int island, out int moon)
    {
        island = -1; moon = -1;
        var sm = SongManager.I;
        if (sm == null || kb == null) return;
        island = sm.Islands.IndexOf(kb);
        if (island < 0) moon = sm.Moons.IndexOf(kb);
    }

    static KeyBlock FromIndex(int island, int moon)
    {
        var sm = SongManager.I;
        if (sm == null) return null;
        if (island >= 0 && island < sm.Islands.Count) return sm.Islands[island];
        if (moon >= 0 && moon < sm.Moons.Count) return sm.Moons[moon];
        return null;
    }

    /// <summary>A rebuild destroyed the header's island (a destroyed KeyBlock compares equal to null but is not a null reference): take the
    /// island that now sits at the same index.</summary>
    void Revalidate()
    {
        if (!ReferenceEquals(pinned, null) && pinned == null) pinned = FromIndex(pinIdx, pinMoon);
        if (!ReferenceEquals(target, null) && target == null)
        {
            target = FromIndex(tgtIdx, tgtMoon);
            if (target != null) { ForceRefresh(); lastAnchor = new Vector2(float.NaN, 0f); }
        }
        if (!ReferenceEquals(hoverCand, null) && hoverCand == null) hoverCand = null;
    }
    bool moreOpen, wasMoon; Vector2 lastAnchor; float fastT; float shakeAbove, shakeBelow;
    // v7: the grid items, the stairs band, the phrase band, the per-kind masks
    HudButton styleBtn, varyBtn, fillSecBtn, launchBtn; InkPainter styleArt, varyArt, fillSecArt, launchArt;
    HudButton spotBtn; InkPainter spotArt; int shownSpot = -1;   // v9 (L): the lead spotlight
    float v7Shake; int v7ShakeWhich;
    HudButton typeBtn, dirBtn, stepsDownBtn, stepsUpBtn, rateBtn, leadBtn; InkPainter typeArt, dirArt, stepsDownArt, stepsUpArt, rateArt, leadArt; TMPro.TextMeshProUGUI stepsText;
    RectTransform fan; CanvasGroup fanGroup; readonly HudButton[] fanBtns = new HudButton[6]; readonly InkPainter[] fanArts = new InkPainter[6]; bool fanOpen; int fanHover = -1, fanStar = -1;
    readonly HudButton[] lenBtns = new HudButton[5]; readonly InkPainter[] lenArts = new InkPainter[5]; HudButton doubleBtn, cellsBtn; InkPainter doubleArt, cellsArt;
    HudButton moreDupRight, moreDupBelow;
    // §20.1: a keyboard's range — + an octave below / above (stickers at the ribbon's low / high ends), fit to the melody (a tab while it can shrink)
    HudButton keysBelowBtn, keysAboveBtn, keysFitBtn; InkPainter keysBelowArt, keysAboveArt, keysFitArt; int shownKeys = int.MinValue;
    readonly Dictionary<GameObject, int> kindMask = new Dictionary<GameObject, int>();
    int shownKind = -1, shownRewind = -1, shownVary = -1, shownLaunch = -1, shownFillSec = -1, shownStair = int.MinValue, shownPhrase = int.MinValue, shownStyleVis = -1;
    const int KChord = 1, KMoon = 2, KKeys = 4, KStairs = 8, KPhrase = 16, KGrids = KChord | KKeys | KStairs | KPhrase, KPlaying = KChord | KKeys;
    /// <summary>v7: the stone of a stairs island (tails, pictures) and the melody phrase's colour.</summary>
    public static readonly Color StairStone = new Color(0.66f, 0.63f, 0.72f), PhraseTint = new Color(0.56f, 0.80f, 0.86f);
    readonly Vector2[] polyAbove = new Vector2[4], polyBelow = new Vector2[4];
    bool aboveOk, belowOk, hasPoly;
    int shownSleep = -1, shownReg = 99, shownBars = -1, shownEnergy = -1, shownFall = -1, shownMood = -1, shownFill = -1, shownRepeat = -1; ChordQuality shownQ = (ChordQuality)(-1);
    Color shownChord = new Color(-1f, 0f, 0f);

    /// <summary>v5: the more card's weather (mood) button shows the vibe the island takes under that mood (sun → sunny / dreamy, cloud →
    /// rainy / moonlit, storm → stormy / spicy; a sus4 stays floaty); "as written" = a thin ring.</summary>
    static string MoodIcon(KeyBlock kb, int mood)
    {
        if (kb == null) return "ringThin";
        if (mood <= 0) return Vibe.Icon(Vibe.Of(kb.quality));   // as written: the chord's own weather
        var stored = kb.storedSemis != null && kb.storedSemis.Length > 0 ? MusicTheory.QualityOf(kb.storedSemis) : kb.quality;
        return Vibe.Icon(Vibe.Of(MusicTheory.MoodMap(stored, Mathf.Clamp(mood, 1, 3))));
    }
    static string MoodWords(KeyBlock kb, int mood)
    {
        if (mood <= 0 || kb == null) return "weather · as written";
        var stored = kb.storedSemis != null && kb.storedSemis.Length > 0 ? MusicTheory.QualityOf(kb.storedSemis) : kb.quality;
        return "weather · " + Vibe.Word(Vibe.Of(MusicTheory.MoodMap(stored, Mathf.Clamp(mood, 1, 3))));
    }

    // ------------------------------------------------------------------ build
    public static IslandHeader Build(RectTransform hud)
    {
        var rt = HudKit.Stretch(hud, "IslandHeader");
        HudKit.SubCanvas(rt.gameObject, true);   // it moves every frame with the camera: its own batch
        var h = rt.gameObject.AddComponent<IslandHeader>();
        h.root = rt;
        h.BuildParts();
        if (SectionHeader.I == null) SectionHeader.Build(hud);   // v7: the section plinths' header and the measure tag (world-anchored, under the fixed HUD)
        return h;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) { I = null; Current = null; GripHeld = false; } }

    void BuildParts()
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;
        // the dotted ghost islands (+ above / + below): painters over the whole HUD, hit-tested to their polygons
        ghostAbove = InkPainter.Create(root, "GhostAbove", Vector2.zero, p => PaintGhost(p, polyAbove, aboveOk, shakeAbove), true);
        Fill(ghostAbove.rectTransform);
        ghostAbove.hitTest = lp => aboveOk && Inside(polyAbove, lp);
        addAboveBtn = HudKit.Control(ghostAbove, "add above", () => AddIsland(true));
        ghostAbove.GetComponent<InkHover>().hoverScale = 1f;
        ghostBelow = InkPainter.Create(root, "GhostBelow", Vector2.zero, p => PaintGhost(p, polyBelow, belowOk, shakeBelow), true);
        Fill(ghostBelow.rectTransform);
        ghostBelow.hitTest = lp => belowOk && Inside(polyBelow, lp);
        addBelowBtn = HudKit.Control(ghostBelow, "add below", () => AddIsland(false));
        ghostBelow.GetComponent<InkHover>().hoverScale = 1f;
        plusAbove = Plus("PlusAbove"); plusBelow = Plus("PlusBelow");
        // the user: "add to its left and right too … add to left and right just basically translates to copy": a + on each side edge
        plusLeft = EdgePlus("PlusLeft", "add left · a copy before it", () => Duplicate(3), out addLeftBtn);
        plusRight = EdgePlus("PlusRight", "add right · a copy after it", () => Duplicate(0), out addRightBtn);
        islandOnly.Add(plusLeft.gameObject); islandOnly.Add(plusRight.gameObject); chordOnly.Add(plusLeft.gameObject); chordOnly.Add(plusRight.gameObject);
        islandOnly.Add(ghostAbove.gameObject); islandOnly.Add(ghostBelow.gameObject); islandOnly.Add(plusAbove.gameObject); islandOnly.Add(plusBelow.gameObject);

        // the ribbon: two notched tails in the chord colour behind a paper band
        ribbon = HudKit.Node(root, "Ribbon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BandW + 8f, 60f));
        tailL = InkShape.Create(ribbon, "TailL", InkShape.Kind.Ribbon, Color.white, new Vector2(44f, 28f));
        tailL.raycastTarget = false; tailL.rectTransform.anchoredPosition = new Vector2(-BandW * 0.5f - 4f, -9f); tailL.rectTransform.localEulerAngles = new Vector3(0f, 0f, 8f); tailL.SetInk(1.8f, 2.8f);
        tailR = InkShape.Create(ribbon, "TailR", InkShape.Kind.Ribbon, Color.white, new Vector2(44f, 28f));
        tailR.raycastTarget = false; tailR.rectTransform.anchoredPosition = new Vector2(BandW * 0.5f + 4f, -9f); tailR.rectTransform.localEulerAngles = new Vector3(0f, 0f, -8f); tailR.SetInk(1.8f, 2.8f);
        band = HudKit.Node(ribbon, "Band", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BandW, 46f));
        bandShape = band.gameObject.AddComponent<InkShape>();
        bandShape.Shape = InkShape.Kind.RoundRect; bandShape.color = Comic.Cream; bandShape.Radius = 7f; bandShape.Wobble = 1.1f; bandShape.RotJitter = 0.6f;
        bandShape.SetInk(2f, 3.2f); bandShape.ShadowOffset = new Vector2(3f, -4f); bandShape.raycastTarget = true;   // the band itself eats clicks (no world pick through it)

        // grip (v5: the island moves anywhere — the 4-way cursor)
        var gp = Item("Grip", new Vector2(-166f, 0f), new Vector2(32f, 36f), PaintGrip, "move anywhere", null);
        gripBtn = gp;
        gp.gameObject.AddComponent<IslandHeaderGrip>().header = this;
        // eye
        eyeBtn = Item("Eye", new Vector2(-128f, 0f), new Vector2(36f, 34f), PaintEye, "turn off", ToggleSleep);
        eyeArt = eyeBtn.GetComponent<InkPainter>();
        islandOnly.Add(eyeBtn.gameObject);
        // register: ▼ ladder ▲
        regNode = HudKit.Node(band, "Register", new Vector2(0.5f, 0.5f), new Vector2(-74f, 0f), new Vector2(76f, 38f));
        regDownBtn = Item("RegDown", new Vector2(-96f, 0f), new Vector2(28f, 34f), p => PaintTri(p, false), "lower", () => Register(-1));
        regDownArt = regDownBtn.GetComponent<InkPainter>();
        var lad = HudKit.Node(band, "Ladder", new Vector2(0.5f, 0.5f), new Vector2(-74f, 0f), new Vector2(18f, 38f));
        ladderArt = lad.gameObject.AddComponent<InkPainter>(); ladderArt.raycastTarget = false; ladderArt.onPaint = PaintLadder;
        regUpBtn = Item("RegUp", new Vector2(-52f, 0f), new Vector2(28f, 34f), p => PaintTri(p, true), "higher", () => Register(1));
        regUpArt = regUpBtn.GetComponent<InkPainter>();
        islandOnly.Add(regDownBtn.gameObject); islandOnly.Add(lad.gameObject); islandOnly.Add(regUpBtn.gameObject); islandOnly.Add(regNode.gameObject);
        // v5 repeat: a conveyor belt carrying the island (its passes as slots); the ×n sticker (Bangers, hero punctuation) from ×2 on
        repeatBtn = Item("Repeat", new Vector2(-14f, 0f), new Vector2(40f, 36f), PaintRepeat, "repeat", CycleRepeat);
        repeatArt = repeatBtn.GetComponent<InkPainter>();
        BuildRepeatSticker();
        islandOnly.Add(repeatBtn.gameObject);
        // v6 carry: a wand throwing an arc onto the next island; the ×n sticker from ×1 on
        // §21: EXTEND → (the v6 carry wand and the v7 flow waves are one control now: the long grid)
        carryBtn = Item("Extend", new Vector2(-22f, 0f), new Vector2(48f, 36f), PaintExtend, "extend →", CycleExtend);
        carryArt = carryBtn.GetComponent<InkPainter>();
        carrySticker = BuildTimesSticker(carryBtn.transform, "×1", out carryText);
        carryMinus = BuildMinus(carryBtn.transform, "un-extend · split off the last grid", StepDownExtend);
        islandOnly.Add(carryBtn.gameObject);
        // duplicates
        dupNode = HudKit.Node(band, "Duplicate", new Vector2(0.5f, 0.5f), new Vector2(92f, 0f), new Vector2(80f, 38f));
        dupRightBtn = Item("DupRight", new Vector2(72f, 0f), new Vector2(38f, 36f), p => PaintDup(p, true), "copy right", () => Duplicate(0));
        dupBelowBtn = Item("DupBelow", new Vector2(112f, 0f), new Vector2(38f, 36f), p => PaintDup(p, false), "copy below", () => Duplicate(1));
        islandOnly.Add(dupRightBtn.gameObject); islandOnly.Add(dupBelowBtn.gameObject); islandOnly.Add(dupNode.gameObject);
        // Moon: dice (re-groove)
        diceBtn = Item("Dice", new Vector2(-60f, 0f), new Vector2(32f, 32f), PaintDice, "new groove", () => { var t = ResolveTarget(); if (t != null && t.IsMoon && UIManager.I != null) UIManager.I.RollMoon(SongManager.I.Moons.IndexOf(t)); });
        moonOnly.Add(diceBtn.gameObject);
        // more ⋯
        moreBtn = Item("More", new Vector2(162f, 0f), new Vector2(34f, 34f), PaintDots, "more", () => SetMore(!moreOpen));
        BuildMore();
        BuildPaste();
        // v6: what a keyboard island does not show (no + ghosts: its column takes islands through the deck)
        chordOnly.Add(ghostAbove.gameObject); chordOnly.Add(ghostBelow.gameObject); chordOnly.Add(plusAbove.gameObject); chordOnly.Add(plusBelow.gameObject);
        BuildV7();   // (after the lists above: its per-kind masks read them)

        Hints.Register("island.header", band);
        Hints.Register("island.move", gripBtn.transform as RectTransform);
        Hints.Register("island.repeat", repeatBtn.transform as RectTransform);
        Hints.Register("island.carry", carryBtn.transform as RectTransform);
        Hints.Register("island.toggle", eyeBtn.transform as RectTransform);
        Hints.Register("island.octave", regNode);
        Hints.Register("island.dup", dupNode);
        Hints.Register("island.addAbove", plusAbove);
        Hints.Register("island.addBelow", plusBelow);
        root.gameObject.SetActive(true);
    }

    static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

    /// <summary>A clickable + disc riding on an island's side edge (add left / add right = a copy there).</summary>
    RectTransform EdgePlus(string name, string caption, System.Action onClick, out HudButton btn)
    {
        var rt = HudKit.Node(root, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
        var s = rt.gameObject.AddComponent<InkShape>();
        s.Shape = InkShape.Kind.Circle; s.color = Comic.Cream; s.SetInk(1.8f, 2.8f); s.ShadowOffset = new Vector2(3f, -3.5f);
        var art = HudKit.Node(rt, "Plus", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f)).gameObject.AddComponent<InkPainter>();
        art.raycastTarget = false;
        art.onPaint = p => { Vector2 c = p.Area.center; p.Line(c + new Vector2(-7f, 0f), c + new Vector2(7f, 0f), 3.2f, Comic.Ink, true); p.Line(c + new Vector2(0f, -7f), c + new Vector2(0f, 7f), 3.2f, Comic.Ink, true); };
        btn = HudKit.Control(s, caption, onClick, s);
        btn.GetComponent<InkHover>().hoverScale = 1.15f;
        return rt;
    }

    /// <summary>The + on the left / right side edge (tests).</summary>
    public HudButton AddLeftButton => addLeftBtn;
    public HudButton AddRightButton => addRightBtn;
    HudButton addLeftBtn, addRightBtn;

    RectTransform Plus(string name)
    {
        var rt = HudKit.Node(root, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        var s = rt.gameObject.AddComponent<InkShape>();
        s.Shape = InkShape.Kind.Circle; s.color = Comic.Cream; s.SetInk(1.8f, 2.8f); s.ShadowOffset = new Vector2(3f, -3.5f); s.raycastTarget = false;
        var art = HudKit.Node(rt, "Plus", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f)).gameObject.AddComponent<InkPainter>();
        art.raycastTarget = false;
        art.onPaint = p => { Vector2 c = p.Area.center; p.Line(c + new Vector2(-8.5f, 0f), c + new Vector2(8.5f, 0f), 3.6f, Comic.Ink, true); p.Line(c + new Vector2(0f, -8.5f), c + new Vector2(0f, 8.5f), 3.6f, Comic.Ink, true); };
        return rt;
    }

    HudButton Item(string name, Vector2 pos, Vector2 size, System.Action<InkPainter> paint, string caption, System.Action onClick)
    {
        var rt = HudKit.Node(band, name, new Vector2(0.5f, 0.5f), pos, size);
        var art = rt.gameObject.AddComponent<InkPainter>();
        art.onPaint = paint;
        var b = HudKit.Control(art, caption, onClick, null, null, art);
        b.GetComponent<InkHover>().hoverScale = 1.12f;
        return b;
    }

    void BuildMore()
    {
        more = HudKit.Node(ribbon, "MoreCard", new Vector2(0.5f, 0.5f), new Vector2(MoreX, -119f), new Vector2(264f, 176f));
        moreShape = more.gameObject.AddComponent<InkShape>();
        moreShape.Shape = InkShape.Kind.RoundRect; moreShape.color = Comic.Cream; moreShape.Radius = 12f; moreShape.Wobble = 1.3f; moreShape.RotJitter = 1.2f;
        moreShape.SetInk(2f, 3.4f); moreShape.ShadowOffset = new Vector2(4f, -5f); moreShape.raycastTarget = true;
        moreGroup = more.gameObject.AddComponent<CanvasGroup>();
        // row 1: chord, mood, fill
        chordDisc = MoreDisc("Chord", new Vector2(-90f, 52f), Comic.Ink, out chordBtn, "chord", () => { int i = TargetIsland(); if (i >= 0 && UIManager.I != null) { UIManager.I.OpenChordWheel(i); SetMore(false); } });
        // the chord picker: the standard piano keys (🎹) on the chord's colour
        var keysArt = HudKit.Node(chordBtn.transform, "Keys", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f)).gameObject.AddComponent<InkPainter>();
        keysArt.raycastTarget = false; keysArt.onPaint = PaintPianoKeys;
        Hints.Register("chord", chordBtn.transform as RectTransform);
        var md = MoreDisc("Mood", new Vector2(-30f, 52f), Comic.Cream, out moodBtn, "weather", () => { int i = TargetIsland(); var t = ResolveTarget(); if (i >= 0 && t != null) SongManager.I.SetMood(i, (t.mood + 1) % 4); });
        moodGlyph = Comic.GlyphImage(moodBtn.transform, "Glyph", "ringThin", Comic.Ink, 22f, false);
        fillDisc = MoreDisc("Fill", new Vector2(30f, 52f), Comic.Cream, out fillBtn, "fill", () => { int i = TargetIsland(); var t = ResolveTarget(); if (i >= 0 && t != null) SongManager.I.SetFill(i, !t.fill); });
        // the drum fill: the standard drum and sticks (🥁)
        var drumArt = HudKit.Node(fillBtn.transform, "Drum", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f)).gameObject.AddComponent<InkPainter>();
        drumArt.raycastTarget = false; drumArt.onPaint = PaintDrum;
        // climb (DeaCube/Climb.cs): the grid's high notes reach higher (every path on it) — off → ×1 → ×2 → ×3 → off; the pips show the level
        MoreDisc("Climb", new Vector2(90f, 52f), Comic.Cream, out climbBtn, Climb.Caption(0), CycleClimb);
        climbArt = HudKit.Node(climbBtn.transform, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f)).gameObject.AddComponent<InkPainter>();
        climbArt.raycastTarget = false; climbArt.onPaint = PaintClimb;
        // row 2: length — 1..4 bar blocks
        for (int k = 1; k <= 4; k++)
        {
            int bars = k;
            var rt = HudKit.Node(more, "Bars" + k, new Vector2(0.5f, 0.5f), new Vector2(-90f + (k - 1) * 30f, -2f), new Vector2(28f, 28f));
            var art = rt.gameObject.AddComponent<InkPainter>();
            art.onPaint = p => PaintBarBlock(p, bars);
            var num = HudKit.Words(rt, "N", bars.ToString(), 16f, Comic.Ink, TMPro.TextAlignmentOptions.Center);   // the measures, as a digit
            num.font = Comic.DigitFont; num.fontSharedMaterial = Comic.DigitFont != null ? Comic.DigitFont.material : num.fontSharedMaterial; num.raycastTarget = false;
            var nrt = num.rectTransform; nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one; nrt.offsetMin = Vector2.zero; nrt.offsetMax = new Vector2(0f, 1f);
            barArts.Add(art);
            barBtns[k - 1] = HudKit.Control(art, "length", () => { int i = TargetIsland(); if (i >= 0) SongManager.I.SetBars(i, bars); }, null, null, art);
        }
        // row 3: energy — and delete (a trash can apart at the right); v9: the fall style went with the plunge presentation (hidden)
        var ert = HudKit.Node(more, "Energy", new Vector2(0.5f, 0.5f), new Vector2(-90f, -46f), new Vector2(40f, 40f));
        energyArt = ert.gameObject.AddComponent<InkPainter>(); energyArt.onPaint = PaintEnergy;
        energyBtn = HudKit.Control(energyArt, "energy", () => { int i = TargetIsland(); var t = ResolveTarget(); if (i >= 0 && t != null) SongManager.I.SetEnergy(i, (t.energy + 1) % 4); }, null, null, energyArt);
        MoreDisc("Fall", new Vector2(-30f, -46f), Comic.Cream, out fallBtn, "fall style", () => { int i = TargetIsland(); var t = ResolveTarget(); if (i >= 0 && t != null) SongManager.I.SetFall(i, (Mathf.Clamp(t.fall, 0, 5) + 1) % 6); });
        fallGlyph = Comic.GlyphImage(fallBtn.transform, "Glyph", BounceGlyphs.Icons[0], Comic.Ink, 22f, false);
        fallBtn.gameObject.SetActive(false);   // v9: the plunge presentation (its fall styles) is gone, so the button is never shown (the field stays in saves)
        var drt = HudKit.Node(more, "Remove", new Vector2(0.5f, 0.5f), new Vector2(98f, -46f), new Vector2(40f, 38f));
        deleteRt = drt;
        var dart = drt.gameObject.AddComponent<InkPainter>(); dart.onPaint = PaintTearOff;
        deleteBtn = HudKit.Control(dart, "delete", DeleteTarget, null, null, dart);
        var dl = HudKit.Words(drt, "Label_delete", "delete", 13f, Comic.A(Comic.Ink, 0.85f), TMPro.TextAlignmentOptions.Center);   // rides with the button on every card
        dl.rectTransform.sizeDelta = new Vector2(70f, 18f); dl.rectTransform.anchoredPosition = new Vector2(0f, -29f);
        // (the Moon's more card: only delete)
        foreach (var b in new[] { chordBtn, moodBtn, fillBtn, energyBtn, climbBtn }) { islandOnly.Add(b.gameObject); chordOnly.Add(b.gameObject); }
        foreach (var b in barBtns) { islandOnly.Add(b.gameObject); chordOnly.Add(b.gameObject); }
        // a word under every button (the pictures alone did not say what they do)
        foreach (var l in new[] { MoreLabel("chord", -90f, 24f), MoreLabel("mood", -30f, 24f), MoreLabel("drum fill", 30f, 24f), MoreLabel("climb", 90f, 24f),
                                  MoreLabel("length", 0f, -2f, 24f), MoreLabel("energy", -90f, -75f) })
        { islandOnly.Add(l); chordOnly.Add(l); }
        more.gameObject.SetActive(false);
    }

    /// <summary>v6: the "paste here" tab — a cream sticker standing on the ribbon's right side: the copied pattern as a little card dropping
    /// onto an island.</summary>
    void BuildPaste()
    {
        var rt = HudKit.Node(ribbon, "PasteHere", new Vector2(0.5f, 0.5f), new Vector2(BandW * 0.5f - 64f, 47f), new Vector2(48f, 40f));
        var body = rt.gameObject.AddComponent<InkShape>();
        body.Shape = InkShape.Kind.Sticker; body.color = Comic.Cream; body.SetInk(1.8f, 2.8f); body.ShadowOffset = new Vector2(3f, -3.5f); body.RotJitter = 2f;
        var content = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 40f));
        pasteArt = content.gameObject.AddComponent<InkPainter>(); pasteArt.raycastTarget = false; pasteArt.onPaint = PaintPaste;
        pasteBtn = HudKit.Control(body, "paste here", PasteHere, body, content, pasteArt);
        pasteBtn.GetComponent<InkHover>().hoverScale = 1.12f;
        rt.localEulerAngles = new Vector3(0f, 0f, -5f);
        Hints.Register("island.paste", rt);
        rt.gameObject.SetActive(false);
        // v7: every path of this grid copied at once (Clipboard.CopyPaths) and pasted onto another grid, each fitted to its chord
        copyPathsBtn = TopSticker("CopyPaths", new Vector2(74f, 47f), PaintCopyPaths, "copy all paths", CopyPathsHere, out copyPathsArt, new Vector2(42f, 32f));
        pastePathsBtn = TopSticker("PastePaths", new Vector2(126f, 47f), PaintPastePaths, "paste the copied paths here", PastePathsHere, out pastePathsArt, new Vector2(46f, 34f));
        pastePathsBadge = BuildTimesSticker(pastePathsBtn.transform, "×1", out pastePathsText);   // the ×n badge on its corner (as repeat's / extend's)
        Hints.Register("island.copyPaths", copyPathsBtn.transform as RectTransform);
        Hints.Register("island.pastePaths", pastePathsBtn.transform as RectTransform);
        copyPathsBtn.gameObject.SetActive(false); pastePathsBtn.gameObject.SetActive(false);
    }

    /// <summary>v7: copies every path of this grid (Clipboard.CopyPaths): the grid pulses, the sticker pops; the paste-paths sticker then shows on the
    /// grids they can go to (⌘V pastes them too).</summary>
    void CopyPathsHere()
    {
        var t = ResolveTarget();
        if (t == null || !Clipboard.CopyPaths(t)) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        t.Pulse(0.8f);
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.22f, 1.2f);
        copyPathsPop = 1f; shownPathsKey = -1;
        CopyPathsCount++;
    }

    /// <summary>v7: pastes the copied paths here, each adapted to this grid's chord (Clipboard.PastePathsOn: one History entry, they appear); refused = a thud.</summary>
    void PastePathsHere()
    {
        var t = ResolveTarget();
        if (t == null || !Clipboard.CanPastePathsOn(t)) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        if (Clipboard.PastePathsOn(t).Count == 0) return;
        shownPathsKey = -1;
    }

    /// <summary>v7: two grid cards, one behind the other (a copy), the front one carrying this grid's paths in their colours.</summary>
    void PaintCopyPaths(InkPainter p)
    {
        Vector2 c = p.Area.center;
        Rect back = new Rect(c.x - 6f, c.y - 5f, 18f, 14f), front = new Rect(c.x - 12f, c.y - 9f, 18f, 14f);
        p.RoundRect(back, 3f, Comic.Cream, 1.3f, Comic.Ink);
        p.Shift = new Vector2(1f, -1.3f); p.RoundRect(front, 3f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
        p.RoundRect(front, 3f, Color.white, 1.4f, Comic.Ink);
        PaintPathLines(p, front);
    }

    /// <summary>v7: paste the copied paths — the standard clipboard (📋) with the copied paths on its paper (the count stands in its corner).</summary>
    void PaintPastePaths(InkPainter p)
    {
        Rect paper = PaintClipboard(p, p.Area.center + new Vector2(-2f, 0f));
        PaintPathLines(p, paper);
    }

    // two little paths (bead, line, bead, line, bead) in the colours of the grid's first cubes (the copied ones on the paste sticker)
    static readonly List<Color> pathCols = new List<Color>(2);
    void PaintPathLines(InkPainter p, Rect r)
    {
        pathCols.Clear();
        var src = p == pastePathsArt ? Clipboard.PathsSource : shownFor;
        if (src != null) foreach (var cube in SequenceMaster.Cubes) { if (cube != null && cube.isFinalized && cube.Island == src) { pathCols.Add(Instruments.Colors[Mathf.Clamp(cube.instrument, 0, Instruments.Colors.Length - 1)]); if (pathCols.Count == 2) break; } }
        if (pathCols.Count == 0) pathCols.Add(Palette.Accent);
        for (int k = 0; k < pathCols.Count; k++)
        {
            float y = r.yMin + (pathCols.Count == 1 ? r.height * 0.5f : (k == 0 ? r.height * 0.32f : r.height * 0.7f));
            Vector2 a = new Vector2(r.xMin + 4f, y - 1.5f), b = new Vector2(r.center.x, y + 1.5f), d = new Vector2(r.xMax - 4f, y - 1f);
            q.Clear(); q.Add(a); q.Add(b); q.Add(d);
            p.Stroke(q, 3, 1.3f, Comic.Opaque(pathCols[k]), false, 0f, 0f, -1f, true);
            p.Disc(a, 1.5f, Comic.Ink); p.Disc(b, 1.5f, Comic.Ink); p.Disc(d, 1.5f, Comic.Ink);
        }
    }

    // ------------------------------------------------------------------ v7: build
    /// <summary>v7: the grid items (flow, launch, the style / vary stickers, the fill tab), the stairs band (type + its fan, direction, steps,
    /// rate, lead-in), the phrase band (lengths, double, cells), the more card's duplicates for stairs / phrases, and the per-kind masks.</summary>
    void BuildV7()
    {
        // every grid: launch (↗ — §21: a build-up into the next section, the riser and its crash; nothing moves)
        launchBtn = Item("Launch", new Vector2(32f, 0f), new Vector2(38f, 36f), PaintLaunch, LaunchCaptionOff, ToggleLaunch);
        launchArt = launchBtn.GetComponent<InkPainter>();
        // v9 (L) chord grids: LEAD (the stage lights' spotlight comes down on it while it sounds), between copy below and more
        spotBtn = Item("LeadSpot", new Vector2(172f, 0f), new Vector2(36f, 36f), PaintLeadSpot, LeadCaptionOff, ToggleLeadSpot);
        spotArt = spotBtn.GetComponent<InkPainter>();
        // the stickers standing on the ribbon: repeat style + vary (from ×2), fill the section (over flow)
        styleBtn = TopSticker("Style", new Vector2(-88f, 48f), PaintStyle, "rewind", ToggleRewind, out styleArt);
        varyBtn = TopSticker("Vary", new Vector2(-56f, 48f), PaintVary, "vary each repeat", CycleVary, out varyArt);
        fillSecBtn = TopSticker("FillSection", new Vector2(-14f, 48f), PaintFillSection, "fill the section", FillTheSection, out fillSecArt, new Vector2(48f, 30f));
        // stairs
        typeBtn = Item("StairType", new Vector2(-146f, 0f), new Vector2(40f, 38f), PaintTypeButton, "stairs type", () => SetFan(!fanOpen));
        typeArt = typeBtn.GetComponent<InkPainter>();
        dirBtn = Item("StairDir", new Vector2(-104f, 0f), new Vector2(34f, 36f), PaintDir, "falls", ToggleDir);
        dirArt = dirBtn.GetComponent<InkPainter>();
        stepsDownBtn = Item("StepsDown", new Vector2(-72f, 0f), new Vector2(28f, 34f), p => PaintPlusMinus(p, false), "fewer steps", () => StepSteps(-1));
        stepsDownArt = stepsDownBtn.GetComponent<InkPainter>();
        var sd = HudKit.Node(band, "StepsDigit", new Vector2(0.5f, 0.5f), new Vector2(-50f, 1f), new Vector2(22f, 30f));
        stepsText = HudKit.Words(sd, "N", "4", 24f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
        stepsText.font = Comic.DigitFont; stepsText.fontSharedMaterial = Comic.DigitFont != null ? Comic.DigitFont.material : stepsText.fontSharedMaterial;
        var srt = stepsText.rectTransform; srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
        stepsUpBtn = Item("StepsUp", new Vector2(-28f, 0f), new Vector2(28f, 34f), p => PaintPlusMinus(p, true), "more steps", () => StepSteps(1));
        stepsUpArt = stepsUpBtn.GetComponent<InkPainter>();
        rateBtn = Item("StairRate", new Vector2(12f, 0f), new Vector2(38f, 36f), PaintRate, "step size", CycleRate);
        rateArt = rateBtn.GetComponent<InkPainter>();
        leadBtn = Item("StairLead", new Vector2(54f, 0f), new Vector2(38f, 36f), PaintLead, "lead-in", ToggleLead);
        leadArt = leadBtn.GetComponent<InkPainter>();
        BuildFan();
        // phrase: ½ 1 2 4 + auto, double, cells
        string[] lenWords = { "½", "1", "2", "4" };
        for (int k = 0; k < 5; k++)
        {
            int kk = k;
            var b = Item(k < 4 ? "Len" + lenWords[k] : "LenAuto", new Vector2(k < 4 ? -150f + k * 29f : -30f, 0f), new Vector2(k < 4 ? 28f : 32f, 34f), p => PaintLen(p, kk),
                         k < 4 ? LenCaption(k) : "auto length", () => SetLength(kk));
            lenBtns[k] = b; lenArts[k] = b.GetComponent<InkPainter>();
            if (k < 4)
            {
                var d = HudKit.Words(b.transform, "N", lenWords[k], k == 0 ? 19f : 20f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
                d.font = Comic.DigitFont; d.fontSharedMaterial = Comic.DigitFont != null ? Comic.DigitFont.material : d.fontSharedMaterial;
                var drt = d.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = Vector2.zero; drt.offsetMax = new Vector2(0f, 1f);
            }
        }
        doubleBtn = Item("PhraseDouble", new Vector2(8f, 0f), new Vector2(36f, 36f), PaintDouble, "double it", DoubleIt);
        doubleArt = doubleBtn.GetComponent<InkPainter>();
        cellsBtn = Item("PhraseCells", new Vector2(46f, 0f), new Vector2(36f, 36f), PaintCells, "cells", ToggleCells);
        cellsArt = cellsBtn.GetComponent<InkPainter>();
        // §20.1 the keyboard's range: + an octave at its low end (left) / its high end (right) — the side where the keys would grow — and fit
        keysBelowBtn = EndSticker("KeysBelow", -1, p => PaintKeysGrow(p, false), "+ octave below", () => GrowKeys(-1), out keysBelowArt);
        keysAboveBtn = EndSticker("KeysAbove", 1, p => PaintKeysGrow(p, true), "+ octave above", () => GrowKeys(1), out keysAboveArt);
        keysFitBtn = TopSticker("KeysFit", new Vector2(120f, 48f), PaintKeysFit, "fit to the melody", FitKeysToMelody, out keysFitArt, new Vector2(44f, 30f));
        // the more card's duplicates for stairs / phrases (their bands have no room)
        var mr = HudKit.Node(more, "DupRight2", new Vector2(0.5f, 0.5f), new Vector2(-44f, 0f), new Vector2(38f, 36f));
        var mra = mr.gameObject.AddComponent<InkPainter>(); mra.onPaint = p => PaintDup(p, true);
        moreDupRight = HudKit.Control(mra, "copy right", () => Duplicate(0), null, null, mra);
        var mb = HudKit.Node(more, "DupBelow2", new Vector2(0.5f, 0.5f), new Vector2(-4f, 8f), new Vector2(38f, 36f));
        var mba = mb.gameObject.AddComponent<InkPainter>(); mba.onPaint = p => PaintDup(p, false);
        moreDupBelow = HudKit.Control(mba, "copy below", () => Duplicate(1), null, null, mba);

        // which kinds show what (bit per kind: chord, Moon, keyboard, stairs, phrase)
        foreach (var go in islandOnly) kindMask[go] = KGrids;
        foreach (var go in moonOnly) kindMask[go] = KMoon;
        foreach (var go in chordOnly) kindMask[go] = KChord;
        kindMask[moreBtn.gameObject] = KGrids | KMoon;
        kindMask[gripBtn.gameObject] = KGrids | KMoon;
        foreach (var go in new[] { carryBtn.gameObject, dupRightBtn.gameObject, dupBelowBtn.gameObject, dupNode.gameObject, launchBtn.gameObject, fillSecBtn.gameObject }) kindMask[go] = KPlaying;
        kindMask[styleBtn.gameObject] = KPlaying | KPhrase; kindMask[varyBtn.gameObject] = KPlaying | KPhrase;
        kindMask[spotBtn.gameObject] = KChord;   // v9 (L): keyboards, stairs and phrases are lead anyway
        foreach (var b in new[] { typeBtn, dirBtn, stepsDownBtn, stepsUpBtn, rateBtn, leadBtn }) kindMask[b.gameObject] = KStairs;
        kindMask[sd.gameObject] = KStairs;
        foreach (var b in lenBtns) kindMask[b.gameObject] = KPhrase;
        kindMask[doubleBtn.gameObject] = KPhrase; kindMask[cellsBtn.gameObject] = KPhrase;
        kindMask[moreDupRight.gameObject] = KStairs | KPhrase; kindMask[moreDupBelow.gameObject] = KStairs;
        kindMask[keysBelowBtn.gameObject] = KKeys; kindMask[keysAboveBtn.gameObject] = KKeys; kindMask[keysFitBtn.gameObject] = KKeys;
        keysBelowBtn.gameObject.SetActive(false); keysAboveBtn.gameObject.SetActive(false); keysFitBtn.gameObject.SetActive(false);
        foreach (var kv in kindMask) if (kv.Value == KStairs || kv.Value == KPhrase || kv.Value == (KStairs | KPhrase)) kv.Key.SetActive(false);
        styleBtn.gameObject.SetActive(false); varyBtn.gameObject.SetActive(false); fillSecBtn.gameObject.SetActive(false);

        Hints.Register("island.flow", carryBtn.transform as RectTransform);     // (§21: the extend control)
        Hints.Register("island.extend", carryBtn.transform as RectTransform);
        Hints.Register("island.launch", launchBtn.transform as RectTransform);
        Hints.Register("island.lead", spotBtn.transform as RectTransform);
        Hints.Register("island.style", styleBtn.transform as RectTransform);
        Hints.Register("island.vary", varyBtn.transform as RectTransform);
        Hints.Register("island.fillSection", fillSecBtn.transform as RectTransform);
        Hints.Register("stairs.type", typeBtn.transform as RectTransform);
        Hints.Register("stairs.dir", dirBtn.transform as RectTransform);
        Hints.Register("phrase.length", lenBtns[1].transform as RectTransform);
    }

    /// <summary>v7: a small cream sticker standing on the ribbon's top edge (the repeat style, the vary dice, the fill tab).</summary>
    HudButton TopSticker(string name, Vector2 pos, System.Action<InkPainter> paint, string caption, System.Action onClick, out InkPainter art, Vector2 size = default(Vector2))
    {
        if (size == Vector2.zero) size = new Vector2(30f, 30f);
        var rt = HudKit.Node(ribbon, name, new Vector2(0.5f, 0.5f), pos, size);
        var body = rt.gameObject.AddComponent<InkShape>();
        body.Shape = InkShape.Kind.Sticker; body.color = Comic.Cream; body.SetInk(1.6f, 2.5f); body.ShadowOffset = new Vector2(2.5f, -3f); body.RotJitter = 3f; body.Radius = 8f;
        var content = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, size);
        art = content.gameObject.AddComponent<InkPainter>(); art.raycastTarget = false; art.onPaint = paint;
        var b = HudKit.Control(body, caption, onClick, body, content, art);
        b.GetComponent<InkHover>().hoverScale = 1.15f;
        rt.localEulerAngles = new Vector3(0f, 0f, name == "Vary" ? 6f : -5f);
        return b;
    }

    /// <summary>v7: the stairs type fan — six stickers in a gentle arc above the ribbon, one per type (its glyph; the best fit wears a gold star);
    /// hover plays that run, a click picks it.</summary>
    void BuildFan()
    {
        fan = HudKit.Node(ribbon, "TypeFan", new Vector2(0.5f, 0.5f), new Vector2(-96f, 96f), new Vector2(270f, 64f));
        fanGroup = fan.gameObject.AddComponent<CanvasGroup>();
        for (int k = 0; k < 6; k++)
        {
            int type = k;
            float x = (k - 2.5f) * 44f, y = -Mathf.Abs(k - 2.5f) * 5f;
            var rt = HudKit.Node(fan, "Type" + k, new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(40f, 40f));
            var body = rt.gameObject.AddComponent<InkShape>();
            body.Shape = InkShape.Kind.Circle; body.color = Comic.Cream; body.SetInk(1.7f, 2.6f); body.ShadowOffset = new Vector2(2.5f, -3f);
            var content = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            var art = content.gameObject.AddComponent<InkPainter>(); art.raycastTarget = false; art.onPaint = p => PaintFanItem(p, type);
            fanArts[k] = art;
            fanBtns[k] = HudKit.Control(body, Harmony.StairTypeWord(type), () => PickType(type), body, content, art);
            fanBtns[k].GetComponent<InkHover>().hoverScale = 1.15f;
            rt.localEulerAngles = new Vector3(0f, 0f, -(k - 2.5f) * 4f);
        }
        fan.gameObject.SetActive(false);
    }

    static string LenCaption(int k) => k == 0 ? "½ measure" : (k == 1 ? "1 measure" : (k == 2 ? "2 measures" : "4 measures · a section"));

    /// <summary>§20.1: a sticker pinned on one end of the ribbon (<paramref name="side"/> −1 the left / low end, +1 the right / high end), over its tail.</summary>
    HudButton EndSticker(string name, int side, System.Action<InkPainter> paint, string caption, System.Action onClick, out InkPainter art)
    {
        var b = TopSticker(name, new Vector2(side * (BandW * 0.5f + 32f), 3f), paint, caption, onClick, out art, new Vector2(46f, 40f));
        b.transform.localEulerAngles = new Vector3(0f, 0f, side * -4f);
        return b;
    }

    // ---- §20.1 the keyboard's range
    /// <summary>A keyboard's range: 13 (one octave) .. 61 keys (five octaves).</summary>
    public const int MinKeys = ProjectConfig.KeyboardMinKeys, MaxKeys = ProjectConfig.KeyboardMaxKeys, LowestKeyMidi = 21, HighestKeyMidi = 108;

    /// <summary>The range "fit to the melody" picks: whole octaves from the keyboard's lowest key (its pitch class kept) that hold every note
    /// [<paramref name="minNote"/>, <paramref name="maxNote"/>], at least one octave (13 keys). False when there are no notes (min &gt; max).</summary>
    public static bool FitKeys(int low, int count, int minNote, int maxNote, out int newLow, out int newCount)
    {
        newLow = low; newCount = count;
        if (minNote > maxNote) return false;
        int lo = low + 12 * Mathf.FloorToInt((minNote - low) / 12f);
        int top = low + 12 * Mathf.CeilToInt((maxNote - low) / 12f);
        if (top <= lo) top = lo + 12;
        newLow = lo; newCount = Mathf.Clamp(top - lo + 1, MinKeys, MaxKeys);
        return true;
    }

    /// <summary>The lowest / highest key pitch the cubes on keyboard <paramref name="kb"/> play (its keys' midi; max &lt; min without notes).</summary>
    public static void KeyNotes(KeyBlock kb, out int min, out int max)
    {
        min = int.MaxValue; max = int.MinValue;
        if (kb == null) return;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.Island != kb) continue;
            for (int i = 0; i < c.nodes.Count; i++)
            {
                var t = c.nodes[i]; if (t == null) continue;
                if (i < c.mods.Count && c.mods[i] == 1) continue;   // a rest
                int m = t.midi - 12 * kb.register;                   // (the key itself, before the register)
                if (m < min) min = m; if (m > max) max = m;
            }
        }
    }

    static int KeyLow(KeyBlock kb) => kb.chordRootMIDI > 0 ? kb.chordRootMIDI : SongManager.KeyboardLowestKey();
    static int KeyCountOf(KeyBlock kb) => kb.KeyCount;

    /// <summary>§20.1: + an octave at the low (<paramref name="dir"/> −1) / high (+1) end (SongManager.SetKeyRange: notes keep their pitch). At five
    /// octaves (or the piano's ends) it refuses.</summary>
    void GrowKeys(int dir)
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsKeyboard) return;
        int low = KeyLow(t), n = KeyCountOf(t);
        int nl = dir < 0 ? low - 12 : low, nn = n + 12;
        if (nn > MaxKeys || nl < LowestKeyMidi || nl + nn - 1 > HighestKeyMidi) { RefuseV7(dir < 0 ? 8 : 9); return; }
        sm.SetKeyRange(i, nl, nn);
        var now = ResolveTarget();
        if (now == null || !now.IsKeyboard || now.KeyCount != nn) { RefuseV7(dir < 0 ? 8 : 9); return; }
        Commit();
        shownKeys = int.MinValue;
        AudioPool.UI(ProceduralAudio.Pop(), 0.28f, dir < 0 ? 0.85f : 1.25f);
        Onboarding.Notify(Onboarding.Ev.KeysExtended);
    }

    /// <summary>§20.1: shrinks the keyboard to the octaves its notes use (at least one).</summary>
    void FitKeysToMelody()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsKeyboard) return;
        int min, max, nl, nn;
        KeyNotes(t, out min, out max);
        if (!FitKeys(KeyLow(t), KeyCountOf(t), min, max, out nl, out nn) || (nl == KeyLow(t) && nn == KeyCountOf(t))) { RefuseV7(10); return; }
        sm.SetKeyRange(i, nl, nn);
        Commit();
        shownKeys = int.MinValue;
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.9f);
    }

    // ------------------------------------------------------------------ v7: actions
    /// <summary>A refusal on a v7 button: a thud, the picture shakes.</summary>
    void RefuseV7(int which) { V7Refusals++; v7Shake = 1f; v7ShakeWhich = which; AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); }

    /// <summary>§21: how far a grid extends (its carry, any style: every carry is a long grid now).</summary>
    public static int ExtendOf(KeyBlock kb) => kb == null || kb.IsMoon || kb.IsStairs ? 0 : Mathf.Clamp(kb.carry, 0, ProjectConfig.MaxCarry);

    /// <summary>§21.2: how far <paramref name="kb"/> CAN extend — the grids of its lane after it in its section (package K's SongManager.LaneNext:
    /// the next column's grid it shares a lane with, never across a section boundary, never a Moon / stairs / phrase), at most MaxCarry. The
    /// extend control cycles up to it and "fill the section" goes all the way to it; 0 = nothing to extend over (the control refuses).</summary>
    public static int ExtendRoom(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null || kb.IsMoon || kb.IsStairs || kb.IsPhrase) return 0;
        int n = 0; var x = kb;
        while (n < ProjectConfig.MaxCarry && (x = sm.LaneNext(x)) != null) n++;
        return n;
    }

    /// <summary>§21 launch captions: a build-up into the next section (the riser and its crash) — nothing is flung.</summary>
    public const string LaunchCaptionOff = "launch · build-up into the next section", LaunchCaptionOn = "launch · a riser into the next section";

    /// <summary>v7 (SPEC v7 §13.5): one long grid to the section's end in one click — carry = the section's last column − this column (§21: SetCarry
    /// style is ignored, every carry is a long grid).</summary>
    void FillTheSection()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        int n = ExtendRoom(t);   // §21.2: as far as its lane goes in the section (the section's end when the lane is whole)
        if (n <= 0) { RefuseV7(2); return; }
        sm.SetCarry(i, n, 1);
        Commit();
        carryPop = 1f;   // (the extend control's ×n sticker pops)
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.3f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.IslandFlow);
    }

    /// <summary>v7: the columns after <paramref name="kb"/>'s in its section (what "fill the section" — §21: a long grid — extends over).</summary>
    public static int FillCount(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null || kb.IsMoon) return 0;
        int s = sm.SectionOf(kb.column);
        return s < 0 ? 0 : Mathf.Max(0, sm.SectionLast(s) - kb.column);
    }

    /// <summary>v7: launch on / off (SetLaunch); nowhere to land (the last column of a song that does not loop) refuses.</summary>
    void ToggleLaunch()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        bool on = !t.launch;
        if (on && sm.LaunchTarget(t) == null) { RefuseV7(3); return; }
        sm.SetLaunch(i, on);
        Commit();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, on ? 1.35f : 0.85f);
        Onboarding.Notify(Onboarding.Ev.IslandLaunch);
    }

    /// <summary>v9 (L): lead on / off (SongManager.SetLead: the stage lights spotlight the grid in every measure it sounds).</summary>
    void ToggleLeadSpot()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || t.IsMoon) return;
        bool on = !t.lead;
        sm.SetLead(i, on);
        Commit();
        if (spotArt != null) spotArt.Repaint();
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f, on ? 1.3f : 0.8f);
    }

    /// <summary>v7: the repeat style — the belt (v5) ⟷ time unwinding (SetRewind).</summary>
    void ToggleRewind()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        sm.SetRewind(i, !t.rewind);
        Commit();
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.25f, t.rewind ? 1.2f : 0.8f);
        Onboarding.Notify(Onboarding.Ev.RepeatStyle);
    }

    /// <summary>v7: vary 0 same → 1 vary → 2 answer → 0 (SetVary).</summary>
    void CycleVary()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        sm.SetVary(i, (Mathf.Clamp(t.vary, 0, 2) + 1) % 3);
        Commit();
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 1.2f);
        Onboarding.Notify(Onboarding.Ev.RepeatStyle);
    }

    // ---- stairs
    void SetFan(bool on)
    {
        if (fan == null || fanOpen == on) return;
        var t = ResolveTarget();
        if (on && (t == null || !t.IsStairs)) return;
        fanOpen = on; fanHover = -1;
        HudKit.SetActive(fan, on);
        if (on) { fan.SetAsLastSibling(); fanStar = BestStairType(t); foreach (var a in fanArts) a.Repaint(); AudioPool.UI(ProceduralAudio.Pop(), 0.22f, 1.25f); }
    }

    /// <summary>v7: the stairs type that fits the island's chord best (Harmony.StairFit; ties: the lower type).</summary>
    public static int BestStairType(KeyBlock kb)
    {
        if (kb == null) return 0;
        int root; IList<int> semis;
        Harmony.ChordOf(kb, out root, out semis);
        var k = MusicTheory.KeyOfSong();
        int job = semis != null ? Harmony.Job(k.tonic, k.minor, root, semis) : 0;
        int best = 0; float bf = -1f;
        for (int t = 0; t < 6; t++) { float f = Harmony.StairFit(t, semis, job); if (f > bf + 1e-4f) { bf = f; best = t; } }
        return best;   // (= SongOps.BestStairType(column): the type a new stairs island gets)
    }

    /// <summary>v7: the pitches the island's run would play with type <paramref name="type"/> (SongManager.StairPitches with that type).</summary>
    public static int[] RunOf(KeyBlock kb, int type)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null || !kb.IsStairs) return new int[0];
        int keep = kb.stairType;
        kb.stairType = Mathf.Clamp(type, 0, 5);
        try { return sm.StairPitches(kb); }
        finally { kb.stairType = keep; }
    }

    /// <summary>v7: plays a run of the island (type <paramref name="type"/>) at its rate on the preview bus — its runner's sound, else the lead's.</summary>
    public void PlayRun(KeyBlock kb, int type)
    {
        if (kb == null || !kb.IsStairs) return;
        var run = RunOf(kb, type);
        int inst = 3, voice = Instruments.BrushVoiceOf(3);
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == kb && !c.IsDrums) { inst = c.instrument; voice = c.voice; break; }
        int slot = Instruments.SlotOf(inst, voice);
        LastRun = run; LastRunSlot = slot; RunAuditions++;
        if (!Synth.Ready || run.Length == 0) return;
        float step = Mathf.Clamp(MeasureState.StairRate(kb.stairRate) / (float)ProjectConfig.TicksPerBeat * 60f / Mathf.Max(30f, GlobalClock.BPM), 0.09f, 0.45f);
        Synth.CancelOwner(Synth.PreviewOwner);
        double t0 = Synth.DspNow + 0.04;
        for (int k = 0; k < run.Length; k++)
        {
            int m = SynthBank.ClampToRegister(slot, run[k] + SongManager.Transpose);
            Synth.Audition(slot, m, k == run.Length - 1 ? 96 : 84, t0 + k * step, t0 + k * step + step * (k == run.Length - 1 ? 1.6f : 0.92f));
        }
    }

    void AfterStair(KeyBlock before)
    {
        Commit();
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.1f);
        Onboarding.Notify(Onboarding.Ev.StairEdited);
        shownStair = int.MinValue;
        var now = ResolveTarget();
        if (now != null && now.IsStairs && !GlobalClock.IsPlaying) PlayRun(now, now.stairType);   // hear what changed
    }

    void PickType(int type)
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        SetFan(false);
        if (i < 0 || t == null || sm == null || !t.IsStairs) return;
        if (t.stairType != type) sm.SetStair(i, type, 0, -1, -1, -1);
        AfterStair(t);
    }

    void ToggleDir()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsStairs) return;
        sm.SetStair(i, -1, t.stairDir > 0 ? -1 : 1, -1, -1, -1);
        AfterStair(t);
    }

    void StepSteps(int d)
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsStairs) return;
        int want = t.stairSteps + d;
        if (want < ProjectConfig.StairMinSteps || want > ProjectConfig.StairMaxSteps) { RefuseV7(d < 0 ? 4 : 5); return; }
        sm.SetStair(i, -1, 0, want, -1, -1);
        AfterStair(t);
    }

    /// <summary>The rates big → small: quarters, eighths, triplet eighths, sixteenths (ticks per step).</summary>
    public static readonly int[] Rates = { 24, 12, 8, 6 };
    static string RateWord(int ticks) => ticks == 24 ? "step size · slow" : (ticks == 8 ? "step size · triplets" : (ticks == 6 ? "step size · quick" : "step size · even"));

    void CycleRate()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsStairs) return;
        int cur = System.Array.IndexOf(Rates, MeasureState.StairRate(t.stairRate));
        sm.SetStair(i, -1, 0, -1, Rates[(Mathf.Max(0, cur) + 1) % Rates.Length], -1);
        AfterStair(t);
    }

    void ToggleLead()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null || !t.IsStairs) return;
        sm.SetStair(i, -1, 0, -1, -1, t.stairLead ? 0 : 1);
        AfterStair(t);
    }

    // ---- phrase
    /// <summary>v7: the length "auto" picks — the shortest of ½, 1, 2, 4, 8 measures (then whole measures) that holds every note on the phrase.</summary>
    public static int AutoBeats(KeyBlock kb)
    {
        if (kb == null) return 4;
        int ticks = 0;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.Island != kb) continue;
            int sum = 0; foreach (int d in c.durs) sum += d;
            ticks = Mathf.Max(ticks, sum);
        }
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        float beats = ticks / (float)ProjectConfig.TicksPerBeat;
        if (beats <= 0.01f) return bpb;
        return SongOps.GrowSize(Mathf.CeilToInt(beats - 1e-3f));   // §17.2: the next of ½, 1, 2, 4, 8 measures (O's auto-grow sizes)
    }

    static int LenBeats(int k) { int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar); return k == 0 ? Mathf.Max(1, bpb / 2) : (k == 1 ? bpb : (k == 2 ? 2 * bpb : 4 * bpb)); }

    void SetLength(int k)
    {
        var t = ResolveTarget();
        if (t == null || !t.IsPhrase) return;
        int beats = k < 4 ? LenBeats(k) : AutoBeats(t);
        if (beats == t.phraseBeats) { AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.9f); return; }
        SongOps.SetPhraseLength(t, beats);
        Commit();
        shownPhrase = int.MinValue;
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.8f + 0.1f * k);
    }

    /// <summary>v7 (SPEC v7 §17.2): twice as long, the notes repeated in the new half (SongOps.DoublePhrase). Past PhraseMaxBeats it refuses.</summary>
    void DoubleIt()
    {
        var t = ResolveTarget();
        if (t == null || !t.IsPhrase) return;
        if (t.phraseBeats * 2 > ProjectConfig.PhraseMaxBeats) { RefuseV7(6); return; }
        int before = t.phraseBeats;
        PhraseDoubler(t);
        var now = ResolveTarget();
        if (now == null || now.phraseBeats == before) { RefuseV7(6); return; }
        Commit();
        shownPhrase = int.MinValue;
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.28f, 1.1f);
    }

    /// <summary>v7: the phrase's cells wide (eighths) ⟷ fine (sixteenths) — SongOps.SetPhraseGrid (package O).</summary>
    void ToggleCells()
    {
        var t = ResolveTarget();
        if (t == null || !t.IsPhrase) return;
        int want = t.phraseGrid == 6 ? 12 : 6;
        if (!PhraseGridSetter(t, want)) { RefuseV7(7); return; }
        Commit();
        shownPhrase = int.MinValue;
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, want == 6 ? 1.3f : 0.9f);
    }

    /// <summary>The phrase ops of package O: DoublePhrase (SPEC §17.2) and SetPhraseGrid (each one History entry).</summary>
    static void PhraseDoubler(KeyBlock kb) { SongOps.DoublePhrase(kb); }
    static bool PhraseGridSetter(KeyBlock kb, int ticks)
    {
        var sm = SongManager.I; int i = sm != null ? sm.Islands.IndexOf(kb) : -1;
        SongOps.SetPhraseGrid(kb, ticks);
        var now = sm != null && i >= 0 && i < sm.Islands.Count ? sm.Islands[i] : null;
        return now != null && now.IsPhrase && now.phraseGrid == ticks;
    }

    /// <summary>A small lowercase word on the more card at (x, y) (centred; <paramref name="dx"/> shifts it right of a row of blocks).</summary>
    GameObject MoreLabel(string text, float x, float y, float dx = 0f)
    {
        var t = HudKit.Words(more, "Label_" + text, text, 13f, Comic.A(Comic.Ink, 0.85f), dx > 0f ? TMPro.TextAlignmentOptions.MidlineLeft : TMPro.TextAlignmentOptions.Center);
        var rt = t.rectTransform; rt.sizeDelta = new Vector2(dx > 0f ? 80f : 70f, 18f); rt.anchoredPosition = new Vector2(x + dx + (dx > 0f ? 40f : 0f), y);
        return t.gameObject;
    }

    InkShape MoreDisc(string name, Vector2 pos, Color fill, out HudButton btn, string caption, System.Action onClick)
    {
        var rt = HudKit.Node(more, name, new Vector2(0.5f, 0.5f), pos, new Vector2(40f, 40f));
        var s = rt.gameObject.AddComponent<InkShape>();
        s.Shape = InkShape.Kind.Circle; s.color = fill; s.SetInk(1.8f, 2.6f); s.ShadowOffset = new Vector2(2.5f, -3f);
        btn = HudKit.Control(s, caption, onClick, s);
        btn.GetComponent<InkHover>().hoverScale = 1.1f;
        return s;
    }

    // ------------------------------------------------------------------ actions (each ends in exactly one History entry)
    KeyBlock ResolveTarget()
    {
        Revalidate();
        if (target != null) return target;
        if (pinned != null) return pinned;
        var ui = UIManager.I; var sm = SongManager.I;
        if (ui != null && sm != null && sm.HasSong)
        {
            if (ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count) return sm.Islands[ui.SelectedMeasure];
            if (ui.SelectedMoon >= 0 && ui.SelectedMoon < sm.Moons.Count) return sm.Moons[ui.SelectedMoon];
        }
        return null;
    }

    int TargetIsland()
    {
        var t = ResolveTarget(); var sm = SongManager.I;
        if (t == null || sm == null || t.IsMoon) return -1;
        return sm.Islands.IndexOf(t);
    }

    /// <summary>After a world op that may or may not push itself: one deduplicated push (History ignores an identical snapshot).</summary>
    static void Commit() { History.Push(); }

    void ToggleSleep()
    {
        int i = TargetIsland(); var t = ResolveTarget();
        if (i < 0 || t == null) return;
        SongManager.I.SetSleep(i, !t.sleep);
        Onboarding.Notify(Onboarding.Ev.IslandToggled);
    }

    void Register(int d)
    {
        int i = TargetIsland(); var t = ResolveTarget();
        if (i < 0 || t == null) return;
        int want = Mathf.Clamp(t.register + d, -2, 2);
        if (want == t.register) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        SongManager.I.SetRegister(i, want);
        Commit();
        ladderArt.Repaint();
        Onboarding.Notify(Onboarding.Ev.IslandRegister);
    }

    /// <summary>v5: the island's passes 1 → 2 → 3 → 4 → 1 (SetRepeat pushes its History entry; the deduplicated safety push adds none).</summary>
    void CycleRepeat()
    {
        int i = TargetIsland(); var t = ResolveTarget();
        if (i < 0 || t == null) return;
        int want = t.repeat >= 4 ? 1 : Mathf.Max(1, t.repeat) + 1;
        SongManager.I.SetRepeat(i, want);
        Commit();
        repeatPop = 1f;
        var now = ResolveTarget();
        RefreshRepeat(now != null ? now.repeat : want);
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.8f + 0.12f * want);
        Onboarding.Notify(Onboarding.Ev.IslandRepeat);
    }

    /// <summary>§21 EXTEND →: the grid extends over the next grids of its lane in its section — ×1 → ×2 → ×3 → off (as far as <see cref="ExtendRoom"/>
    /// lets it), one LONG GRID whose cubes walk left → right through the chords (SetCarry(at, n, 1); it pushes the History entry). Nothing to extend
    /// over (its section's last column, or no grid in its lane in the next column) refuses: a thud, the picture shakes.</summary>
    void CycleExtend()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        int room = ExtendRoom(t);   // §21.2: its lane in its section (K's LaneNext), at most MaxCarry
        int cur = ExtendOf(t);
        if (room <= 0 && cur == 0) { CarryRefusals++; carryShake = 1f; AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        int want = cur >= room ? 0 : cur + 1;
        if (want < cur) sm.SplitExtend(i, want);   // back to off: the long grid is cut into separate grids, each keeping its notes
        else sm.SetCarry(i, want, 1);
        Commit();
        carryPop = 1f;
        var now = ResolveTarget();
        RefreshCarry(now != null ? ExtendOf(now) : want);
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.9f + 0.12f * want);
        Onboarding.Notify(Onboarding.Ev.IslandCarry);
    }

    /// <summary>v6: pastes the clipboard's pattern here, adapted to this island's chord (package H: Clipboard.PasteOn — §21: the cube appears there, one
    /// History entry); refused = a thud.</summary>
    void PasteHere()
    {
        var t = ResolveTarget();
        if (t == null || !Clipboard.CanPasteOn(t)) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        var c = Clipboard.PasteOn(t);
        if (c == null) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        Commit();
    }

    void Duplicate(int where)
    {
        int i = TargetIsland();
        if (i < 0) return;
        var sm = SongManager.I;
        int n = sm.DuplicateIsland(i, where);
        if (n < 0) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        Commit();
        if (UIManager.I != null && n < sm.Islands.Count) UIManager.I.SelectMeasure(n, false);
        pinned = null; target = null; SetMore(false);   // the header moves on to the new island (the selection)
        Onboarding.Notify(Onboarding.Ev.IslandDuplicated);
    }

    void AddIsland(bool above) => AddIslandAt(above, MeasureUnderPointer(above));

    /// <summary>v8: the measure of a multi-measure grid under the pointer on its + ghost (0 when it spans one measure).</summary>
    int MeasureUnderPointer(bool above)
    {
        int slots = GhostSlots();
        if (slots < 2) return 0;
        Vector2 sp = PathManager.SimOnly ? (Vector2)PathManager.SimPos : (Vector2)Input.mousePosition;
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out lp)) return 0;
        var poly = above ? polyAbove : polyBelow;
        Vector2 m0 = (poly[0] + poly[3]) * 0.5f, m1 = (poly[1] + poly[2]) * 0.5f, d = m1 - m0;
        float t = d.sqrMagnitude > 1e-4f ? Vector2.Dot(lp - m0, d) / d.sqrMagnitude : 0f;
        return Mathf.Clamp(Mathf.FloorToInt(t * slots), 0, slots - 1);
    }

    /// <summary>v8: how many measures the target grid's + ghosts offer (a chord grid of several measures: one slot each).</summary>
    int GhostSlots() { var t = shownFor; return t != null && t.kind == 0 ? Mathf.Clamp(t.bars, 1, 4) : 1; }

    /// <summary>v8 (tests and the ghost click): adds a normal one-measure grid above / below the target grid on its measure <paramref name="measure"/>.</summary>
    public void AddIslandAt(bool above, int measure)
    {
        int i = TargetIsland();
        if (i < 0) return;
        var sm = SongManager.I; var t = sm.Islands[i];
        if (ColumnFull(t)) { Refusals++; if (above) shakeAbove = 1f; else shakeBelow = 1f; AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return; }
        int n = sm.AddIslandInColumn(i, above, measure);
        if (n < 0) { Refusals++; if (above) shakeAbove = 1f; else shakeBelow = 1f; AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return; }
        Commit();
        if (UIManager.I != null && n < sm.Islands.Count) UIManager.I.SelectMeasure(n, false);
        pinned = null; target = null; SetMore(false);   // the header moves on to the new island (the selection)
        Onboarding.Notify(Onboarding.Ev.IslandAdded);
    }

    static bool ColumnFull(KeyBlock kb)
    {
        var sm = SongManager.I;
        if (sm == null || kb == null) return true;
        int n = 0; foreach (var x in sm.Islands) if (x != null && x.column == kb.column) n++;
        return n >= ProjectConfig.MaxLanes;
    }

    void DeleteTarget()
    {
        var t = ResolveTarget(); var sm = SongManager.I;
        if (t == null || sm == null) return;
        SetMore(false);
        if (t.IsMoon) { if (UIManager.I != null) UIManager.I.RemoveMoon(sm.Moons.IndexOf(t)); }
        else
        {
            int i = sm.Islands.IndexOf(t);
            if (i < 0 || sm.Islands.Count <= 1) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
            sm.RemoveMeasure(i);
        }
        if (UIManager.I != null) UIManager.I.ClearSelection();
        pinned = null; target = null;
    }

    void SetMore(bool on)
    {
        if (on) SetFan(false);
        if (moreOpen == on) return;
        moreOpen = on;
        HudKit.SetActive(more, on);
        if (on) { more.SetAsLastSibling(); ForceRefresh(); AudioPool.UI(ProceduralAudio.Pop(), 0.22f, 1.2f); }
    }

    internal void GripDown(PointerEventData e)
    {
        var t = ResolveTarget(); var pm = PathManager.I;
        if (t == null || pm == null || pm.Drag == null || WorldInput.WorldLocked) return;
        GripHeld = true;
        pm.Drag.Arm(t, false, Input.mousePosition);
        AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.9f);
    }
    internal void GripUp() { GripHeld = false; }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> q = new List<Vector2>(64);

    Color Chord => shownFor != null ? ChordOf(shownFor) : Palette.Accent;
    /// <summary>An island's colour on the header: the chord colour, a Moon's moon grey, a keyboard's wood.</summary>
    static Color ChordOf(KeyBlock kb) => Comic.Opaque(kb.IsMoon ? KeyBlock.MoonColor : (kb.IsKeyboard ? KeyboardWood : (kb.IsStairs ? StairStone : (kb.IsPhrase ? PhraseTint : kb.chordColor))));
    /// <summary>v7: the band's kind for an island: 0 chord, 1 Moon, 2 keyboard, 3 stairs, 4 phrase.</summary>
    static int KindOf(KeyBlock kb) => kb == null ? -1 : (kb.IsMoon ? 1 : (kb.IsKeyboard ? 2 : (kb.IsStairs ? 3 : (kb.IsPhrase ? 4 : 0))));

    /// <summary>Move: the standard four-way arrow (✥).</summary>
    void PaintGrip(InkPainter p)
    {
        Vector2 c = p.Area.center; Color ink = Comic.Ink;
        p.Line(c + new Vector2(-9f, 0f), c + new Vector2(9f, 0f), 2.2f, ink, true);
        p.Line(c + new Vector2(0f, -9f), c + new Vector2(0f, 9f), 2.2f, ink, true);
        p.Head(c + new Vector2(14f, 0f), Vector2.right, 5.5f, 7f, ink);
        p.Head(c + new Vector2(-14f, 0f), Vector2.left, 5.5f, 7f, ink);
        p.Head(c + new Vector2(0f, 14f), Vector2.up, 5.5f, 7f, ink);
        p.Head(c + new Vector2(0f, -14f), Vector2.down, 5.5f, 7f, ink);
    }

    /// <summary>Sound on / off: the standard speaker — sound waves while it plays (🔊), a ✕ while it is turned off (🔇).</summary>
    void PaintEye(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(-4f, 0f);
        bool asleep = shownFor != null && shownFor.sleep;
        q.Clear();
        q.Add(c + new Vector2(-9f, -4f)); q.Add(c + new Vector2(-4f, -4f)); q.Add(c + new Vector2(3f, -10f));
        q.Add(c + new Vector2(3f, 10f)); q.Add(c + new Vector2(-4f, 4f)); q.Add(c + new Vector2(-9f, 4f));
        p.InkFill(q, q.Count, asleep ? Comic.Cream : Chord, 1.8f, Comic.Ink);
        if (!asleep)
        {
            p.Arc(c + new Vector2(3f, 0f), 5.5f, 2f, Comic.Ink, -45f, 45f);
            p.Arc(c + new Vector2(3f, 0f), 10f, 2f, Comic.Ink, -50f, 50f);
        }
        else
        {
            Vector2 x = c + new Vector2(11f, 0f);
            p.Line(x + new Vector2(-4f, -4f), x + new Vector2(4f, 4f), 2.2f, Comic.Ink, true);
            p.Line(x + new Vector2(-4f, 4f), x + new Vector2(4f, -4f), 2.2f, Comic.Ink, true);
        }
    }

    void PaintTri(InkPainter p, bool up)
    {
        Vector2 c = p.Area.center;
        var t = shownFor;
        bool can = t != null && (up ? t.register < 2 : t.register > -2);
        float a = can ? 1f : 0.35f;
        q.Clear();
        if (up) { q.Add(c + new Vector2(0f, 7f)); q.Add(c + new Vector2(-7f, -5f)); q.Add(c + new Vector2(7f, -5f)); }
        else { q.Add(c + new Vector2(0f, -7f)); q.Add(c + new Vector2(7f, 5f)); q.Add(c + new Vector2(-7f, 5f)); }
        p.Fill(q, 3, Comic.A(Comic.Ink, a));
    }

    /// <summary>The octave between ▼ and ▲: a note on a little staff, sitting higher or lower with the island's register (stem down above
    /// the middle line, as in notation).</summary>
    void PaintLadder(InkPainter p)
    {
        Rect r = p.Area; Vector2 c = r.center;
        int reg = shownFor != null ? Mathf.Clamp(shownFor.register, -2, 2) : 0;
        for (int k = -1; k <= 1; k++) p.Line(c + new Vector2(-8f, k * 6f), c + new Vector2(8f, k * 6f), 1.1f, Comic.A(Comic.Ink, 0.55f));
        Vector2 h = c + new Vector2(-1f, reg * 4.5f);
        p.Disc(h, 4.4f, 3.5f, Comic.Ink);
        p.Disc(h, 3.1f, 2.2f, Chord);
        if (reg > 0) p.Line(h + new Vector2(-3.8f, 0f), h + new Vector2(-3.8f, -11f), 1.6f, Comic.Ink, true);
        else p.Line(h + new Vector2(3.8f, 0f), h + new Vector2(3.8f, 11f), 1.6f, Comic.Ink, true);
    }

    /// <summary>Duplicate to the right / below: the standard "insert" picture — this grid as a filled square, an empty dashed one beside it
    /// (or under it) with a +.</summary>
    void PaintDup(InkPainter p, bool right)
    {
        Vector2 c = p.Area.center; Color col = Chord;
        Vector2 sz = right ? new Vector2(13f, 15f) : new Vector2(17f, 11f);
        Vector2 a = right ? c + new Vector2(-8f, 0f) : c + new Vector2(0f, 7f);
        Vector2 b = right ? c + new Vector2(8f, 0f) : c + new Vector2(0f, -7f);
        p.RoundRect(new Rect(b - sz * 0.5f, sz), 3f, Comic.A(Comic.Cream, 0.4f), 1.5f, Comic.A(Comic.Ink, 0.85f), 2.4f, 1.8f);
        p.Line(b + new Vector2(-3.5f, 0f), b + new Vector2(3.5f, 0f), 1.8f, Comic.Ink, true);
        p.Line(b + new Vector2(0f, -3.5f), b + new Vector2(0f, 3.5f), 1.8f, Comic.Ink, true);
        p.RoundRect(new Rect(a - sz * 0.5f, sz), 3f, col, 1.6f, Comic.Ink);
    }

    static void Island(InkPainter p, Vector2 c, Color fill, bool dotted)
    {
        q.Clear(); q.Add(c + new Vector2(-8f, 0f)); q.Add(c + new Vector2(0f, 4.5f)); q.Add(c + new Vector2(8f, 0f)); q.Add(c + new Vector2(0f, -4.5f));
        if (!dotted)
        {
            var lip = new Vector2(0f, -2.4f);
            for (int i = 0; i < 4; i++) q[i] += lip;
            p.Fill(q, 4, Comic.Ink);
            for (int i = 0; i < 4; i++) q[i] -= lip;
        }
        p.Fill(q, 4, fill);
        p.Stroke(q, 4, 1.5f, Comic.Ink, true, dotted ? 2.2f : 0f, dotted ? 1.8f : 0f);
    }

    /// <summary>The ×n sticker: a small yellow paper tag, tilted, lettered in Bangers (hero punctuation); hidden at ×1.</summary>
    void BuildRepeatSticker() { repeatSticker = BuildTimesSticker(repeatBtn.transform, "×2", out repeatText); repeatMinus = BuildMinus(repeatBtn.transform, "repeat one time less", StepDownRepeat); }

    /// <summary>A small − disc on a button's lower-left corner (hidden until shown): steps the repeat / the extend down by one.</summary>
    RectTransform BuildMinus(Transform on, string caption, System.Action onClick)
    {
        var s = InkShape.Create(on, "Minus", InkShape.Kind.Circle, Comic.Cream, new Vector2(17f, 17f));
        s.SetInk(1.3f, 2f); s.ShadowOffset = new Vector2(1.5f, -2f);
        var rt = s.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); rt.anchoredPosition = new Vector2(-1f, -3f);
        var art = InkPainter.Create(rt, "Art", new Vector2(17f, 17f), p => { Vector2 c = p.Area.center; p.Line(c + new Vector2(-4f, 0f), c + new Vector2(4f, 0f), 2.2f, Comic.Ink, true); });
        var b = HudKit.Control(s, caption, onClick, s);
        b.GetComponent<InkHover>().hoverScale = 1.2f;
        rt.gameObject.SetActive(false);
        return rt;
    }

    /// <summary>The − on the repeat: one pass fewer (SetRepeat, one History entry).</summary>
    void StepDownRepeat()
    {
        int i = TargetIsland(); var t = ResolveTarget();
        if (i < 0 || t == null || t.repeat <= 1) return;
        SongManager.I.SetRepeat(i, t.repeat - 1);
        Commit();
        repeatPop = 1f;
        var now = ResolveTarget();
        RefreshRepeat(now != null ? now.repeat : 1);
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.7f);
    }

    /// <summary>The − on the extend: the long grid lets go of its last grid, which keeps the notes it played (SongManager.SplitExtend).</summary>
    void StepDownExtend()
    {
        int i = TargetIsland(); var t = ResolveTarget(); var sm = SongManager.I;
        if (i < 0 || t == null || sm == null) return;
        int cur = ExtendOf(t);
        if (cur <= 0) return;
        sm.SplitExtend(i, cur - 1);
        Commit();
        carryPop = 1f;
        var now = ResolveTarget();
        RefreshCarry(now != null ? ExtendOf(now) : cur - 1);
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.75f);
    }

    /// <summary>A ×n sticker on a button's top-right corner (repeat, v6 carry), hidden until shown.</summary>
    static RectTransform BuildTimesSticker(Transform on, string text, out TMPro.TextMeshProUGUI label)
    {
        var st = InkShape.Create(on, "Times", InkShape.Kind.Sticker, Comic.Pop, new Vector2(28f, 20f));
        st.raycastTarget = false; st.SetInk(1.3f, 2f); st.ShadowOffset = new Vector2(2f, -2.5f); st.RotJitter = 0f;
        var rt = st.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.anchoredPosition = new Vector2(5f, 6f); rt.localEulerAngles = new Vector3(0f, 0f, 9f);   // on the corner, clear of the picture
        label = HudKit.Words(rt, "N", text, 17f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
        label.font = Comic.DigitFont; label.fontSharedMaterial = Comic.DigitFont != null ? Comic.DigitFont.material : label.fontSharedMaterial;
        var trt = label.rectTransform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = new Vector2(0f, 1f);
        rt.gameObject.SetActive(false);
        return rt;
    }

    /// <summary>§21: the extend picture and its ×n sticker / caption follow how far the grid extends.</summary>
    void RefreshCarry(int n)
    {
        n = Mathf.Clamp(n, 0, ProjectConfig.MaxCarry);
        if (carryArt != null) carryArt.Repaint();
        if (fillSecArt != null) fillSecArt.Repaint();
        if (carryMinus != null && carryMinus.gameObject.activeSelf != n > 0) carryMinus.gameObject.SetActive(n > 0);
        if (carrySticker == null) return;
        carrySticker.gameObject.SetActive(n > 0);
        if (n > 0 && carryText != null) carryText.text = "×" + n;
        InkCaption.Attach(carryBtn.gameObject, n > 0 ? ExtendCaption(n) : ExtendCaptionOff);
    }

    /// <summary>§21: the extend control's captions.</summary>
    public const string ExtendCaptionOff = "extend →";
    public static string ExtendCaption(int n) => "extend · " + (n == 1 ? "over the next chord" : "over the next " + n + " chords");

    /// <summary>§21 EXTEND →: the standard "stretch to the right" picture — an arrow from a bar on the left to a bar on the right (|→|), inked
    /// in the grid's colour while the grid extends (the ×n tag says over how many chords), dashed when it does not.</summary>
    void PaintExtend(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int n = ExtendOf(shownFor);
        if (carryShake > 0f) c.x += Mathf.Sin(carryShake * 40f) * 3f * carryShake;
        Color ink = n > 0 ? Comic.Ink : Comic.A(Comic.Ink, 0.75f);
        p.Line(c + new Vector2(-16f, -9f), c + new Vector2(-16f, 9f), 2.4f, ink, true);
        p.Line(c + new Vector2(16f, -9f), c + new Vector2(16f, 9f), 2.4f, ink, true);
        q.Clear(); q.Add(c + new Vector2(-11f, 0f)); q.Add(c + new Vector2(6f, 0f));
        if (n > 0) p.InkStroke(q, 2, 3.4f, Chord, 1.3f, Comic.Ink);
        else p.Stroke(q, 2, 2f, ink, false, 2.6f, 2.2f);
        p.Head(c + new Vector2(12.5f, 0f), Vector2.right, 6.5f, 9f, n > 0 ? Chord : Comic.Cream, 1.3f, ink);
    }

    /// <summary>v6 paste here: the standard clipboard (📋), the copied pattern on its paper in its instrument's colour.</summary>
    void PaintPaste(InkPainter p)
    {
        Color ins = Instruments.Colors[Mathf.Clamp(Clipboard.Instrument, 0, Instruments.Colors.Length - 1)];
        Rect paper = PaintClipboard(p, p.Area.center);
        Vector2 d0 = new Vector2(paper.xMin + 3f, paper.yMin + 4f), d1 = new Vector2(paper.center.x, paper.yMax - 4f), d2 = new Vector2(paper.xMax - 3f, paper.yMin + 6f);
        q.Clear(); q.Add(d0); q.Add(d1); q.Add(d2);
        p.Stroke(q, 3, 1.4f, Comic.Opaque(ins), false, 0f, 0f, -1f, true);
        p.Disc(d0, 1.6f, Comic.Ink); p.Disc(d1, 1.6f, Comic.Ink); p.Disc(d2, 1.6f, Comic.Ink);
    }

    /// <summary>A clipboard (board, paper, clip) centred at <paramref name="c"/>; returns the paper's rect to draw on.</summary>
    static Rect PaintClipboard(InkPainter p, Vector2 c)
    {
        Rect board = new Rect(c.x - 10f, c.y - 13f, 20f, 25f);
        p.Shift = new Vector2(1.2f, -1.6f); p.RoundRect(board, 3f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
        p.RoundRect(board, 3f, new Color(0.86f, 0.64f, 0.42f), 1.5f, Comic.Ink);
        Rect paper = new Rect(board.xMin + 3f, board.yMin + 3f, board.width - 6f, board.height - 8f);
        p.RoundRect(paper, 1.5f, Color.white, 1.1f, Comic.Ink);
        p.RoundRect(new Rect(c.x - 5f, board.yMax - 4f, 10f, 6f), 2f, Comic.A(Comic.Ink, 0.85f), 1.2f, Comic.Ink);
        return paper;
    }

    void RefreshRepeat(int n)
    {
        n = Mathf.Clamp(n, 1, 4);
        if (repeatArt != null) repeatArt.Repaint();
        if (repeatMinus != null && repeatMinus.gameObject.activeSelf != n > 1) repeatMinus.gameObject.SetActive(n > 1);
        if (repeatSticker == null) return;
        repeatSticker.gameObject.SetActive(n > 1);
        if (n > 1 && repeatText != null) repeatText.text = "×" + n;
        InkCaption.Attach(repeatBtn.gameObject, n > 1 ? "repeat · plays " + (n == 2 ? "twice" : (n == 3 ? "3 times" : "4 times")) : "repeat");
    }

    /// <summary>Repeat: the standard loop arrows (🔁) — inked in the grid's colour from ×2 (the ×n tag says how many times).</summary>
    void PaintRepeat(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int n = shownFor != null && !shownFor.IsMoon ? Mathf.Clamp(shownFor.repeat, 1, 4) : 1;
        for (int side = 0; side < 2; side++)
        {
            float s = side == 0 ? 1f : -1f;
            q.Clear();
            q.Add(c + new Vector2(-14f, -1f) * s); q.Add(c + new Vector2(-14f, 3.5f) * s); q.Add(c + new Vector2(-13f, 6f) * s);
            q.Add(c + new Vector2(-10.5f, 7.5f) * s); q.Add(c + new Vector2(8f, 7.5f) * s);
            if (n > 1) p.InkStroke(q, q.Count, 3.4f, Chord, 1.3f, Comic.Ink);
            else p.Stroke(q, q.Count, 2.4f, Comic.Ink, false, 0f, 0f, -1f, true);
            p.Head(c + new Vector2(14.5f, 7.5f) * s, Vector2.right * s, 6.5f, 8.5f, n > 1 ? Chord : Comic.Ink, n > 1 ? 1.3f : 0f, Comic.Ink);
        }
    }

    // ------------------------------------------------------------------ v7 pictures
    Vector2 ShakeOf(int which) => v7Shake > 0f && v7ShakeWhich == which ? new Vector2(Mathf.Sin(v7Shake * 40f) * 3f * v7Shake, 0f) : Vector2.zero;

    /// <summary>§21 launch = a BUILD-UP: a swell (a crescendo hairpin opening to the right) rising into an arrow ↗, a small crash burst at its tip —
    /// gold while the island launches into the next section; faint and dotted when not. Nothing in the picture flies.</summary>
    void PaintLaunch(InkPainter p)
    {
        Vector2 c = p.Area.center + ShakeOf(3);
        bool on = shownFor != null && !shownFor.IsMoon && shownFor.launch;
        PaintSwell(p, c, on);
    }

    /// <summary>§21: the launch picture (shared with the section header's launch): the standard rocket (🚀) pointing up and to the right — gold
    /// fins and a flame while the grid launches into the next section; faint and unlit when not.</summary>
    public static void PaintSwell(InkPainter p, Vector2 c, bool on)
    {
        Color ink = on ? Comic.Ink : Comic.A(Comic.Ink, 0.7f);
        Vector2 d = new Vector2(0.7071f, 0.7071f), n = new Vector2(-0.7071f, 0.7071f);
        System.Func<float, float, Vector2> P = (u, v) => c + d * u + n * v;
        // the flame behind it
        if (on)
        {
            q.Clear(); q.Add(P(-8f, 3f)); q.Add(P(-19f, 0f)); q.Add(P(-8f, -3f));
            p.InkFill(q, 3, new Color(1f, 0.55f, 0.2f), 1.2f, Comic.Ink);
            q.Clear(); q.Add(P(-8f, 1.5f)); q.Add(P(-14f, 0f)); q.Add(P(-8f, -1.5f));
            p.Fill(q, 3, Comic.Pop);
        }
        // the fins
        for (int side = -1; side <= 1; side += 2)
        {
            q.Clear(); q.Add(P(-1f, 4.5f * side)); q.Add(P(-10f, 10f * side)); q.Add(P(-9f, 3.5f * side));
            p.InkFill(q, 3, on ? Comic.Pop : Comic.Cream, 1.4f, ink);
        }
        // the body: a nose cone on a rounded tube
        q.Clear();
        q.Add(P(15f, 0f)); q.Add(P(10f, 3.6f)); q.Add(P(6f, 4.8f)); q.Add(P(-7f, 4.8f)); q.Add(P(-9f, 3f));
        q.Add(P(-9f, -3f)); q.Add(P(-7f, -4.8f)); q.Add(P(6f, -4.8f)); q.Add(P(10f, -3.6f));
        p.InkFill(q, q.Count, on ? Color.white : Comic.Cream, 1.6f, ink);
        // the window
        p.Disc(P(3.5f, 0f), 3.4f, ink);
        p.Disc(P(3.5f, 0f), 2.2f, on ? new Color(0.55f, 0.8f, 1f) : Comic.Cream);
    }

    /// <summary>v9 (L): the lead button — the standard stage spotlight (StageLights.PaintSpotlight): its beam and lens gold while the grid is lead,
    /// faint and unlit when not.</summary>
    void PaintLeadSpot(InkPainter p) { StageLights.PaintSpotlight(p, p.Area.center, shownFor != null && !shownFor.IsMoon && shownFor.lead, 1.1f); }

    /// <summary>v7 repeat style: the standard rewind (⏪) — filled gold when time unwinds before each repeat, a faint outline when the belt rides.</summary>
    void PaintStyle(InkPainter p)
    {
        Vector2 c = p.Area.center;
        bool on = shownFor != null && shownFor.rewind;
        for (int k = 0; k < 2; k++)
        {
            float x = c.x + (k == 0 ? -1f : 9f);
            q.Clear(); q.Add(new Vector2(x - 10f, c.y)); q.Add(new Vector2(x, c.y + 7f)); q.Add(new Vector2(x, c.y - 7f));
            if (on) p.InkFill(q, 3, Comic.Pop, 1.4f, Comic.Ink);
            else p.Stroke(q, 3, 1.6f, Comic.A(Comic.Ink, 0.6f), true);
        }
    }

    /// <summary>v7 vary: the standard shuffle (🔀) — faint and dashed = the same every time; inked = small changes each repeat; with a little
    /// curl = the last repeat also answers (resolves home).</summary>
    void PaintVary(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int v = shownFor != null ? Mathf.Clamp(shownFor.vary, 0, 2) : 0;
        Color ink = v == 0 ? Comic.A(Comic.Ink, 0.55f) : Comic.Ink;
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? 1f : -1f;
            q.Clear();
            q.Add(c + new Vector2(-12f, -6f * s)); q.Add(c + new Vector2(-6f, -6f * s)); q.Add(c + new Vector2(2f, 6f * s)); q.Add(c + new Vector2(7f, 6f * s));
            if (v == 0) p.Stroke(q, q.Count, 1.6f, ink, false, 2.4f, 2f);
            else p.Stroke(q, q.Count, 2f, ink, false, 0f, 0f, -1f, true);
            p.Head(c + new Vector2(12f, 6f * s), Vector2.right, 5f, 6.5f, ink);
        }
        if (v == 2)
        {
            q.Clear();
            InkPainter.ArcPoints(q, c + new Vector2(0f, 11f), 3.5f, 3.5f, 180f, -60f, 10);
            p.Stroke(q, q.Count, 1.6f, Comic.Ink, false, 0f, 0f, -1f, true);
            Vector2 end = q[q.Count - 1], dir = (end - q[q.Count - 3]).normalized;
            p.Head(end + dir * 1.5f, dir, 3.5f, 4f, Comic.Pop, 1f, Comic.Ink);
        }
    }

    /// <summary>v7 fill the section (§21: one long grid to the section's end): the standard "stretch to fit" picture (|→ … →|) over four
    /// measure dots, this grid's measure in its colour.</summary>
    void PaintFillSection(InkPainter p)
    {
        Vector2 c = p.Area.center + ShakeOf(2);
        Color col = Chord;
        p.Line(c + new Vector2(-19f, -2f), c + new Vector2(-19f, 10f), 2.2f, Comic.Ink, true);
        p.Line(c + new Vector2(19f, -2f), c + new Vector2(19f, 10f), 2.2f, Comic.Ink, true);
        q.Clear(); q.Add(c + new Vector2(-15f, 4f)); q.Add(c + new Vector2(10f, 4f));
        p.InkStroke(q, 2, 3f, col, 1.2f, Comic.Ink);
        p.Head(c + new Vector2(15.5f, 4f), Vector2.right, 5.5f, 7.5f, col, 1.2f, Comic.Ink);
        for (int k = 0; k < 4; k++) p.Disc(c + new Vector2(-13.5f + k * 9f, -7f), 2.2f, k == 0 ? col : Comic.A(Comic.Ink, 0.45f));
    }

    /// <summary>v7: a type's glyph — a little run of steps in the type's manner (descending), <paramref name="k"/> = scale.</summary>
    static void PaintStairGlyph(InkPainter p, int type, Vector2 c, float k, Color stone)
    {
        switch (type)
        {
            case 0:   // chord: three tall steps, a dot on each (the chord's tones)
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(c.x + (-11f + i * 7.5f) * k, c.y + (-9f) * k, 7f * k, (18f - i * 6f) * k);
                    p.RoundRect(r, 1f * k, stone, 1.2f, Comic.Ink);
                    p.Disc(new Vector2(r.center.x, r.yMax - 2.6f * k), 1.3f * k, Comic.Ink);
                }
                break;
            case 1:   // scale: five even small steps
                for (int i = 0; i < 5; i++) p.RoundRect(new Rect(c.x + (-12f + i * 4.8f) * k, c.y - 9f * k, 4.6f * k, (16f - i * 3f) * k), 0.8f * k, stone, 1.1f, Comic.Ink);
                break;
            case 2:   // spark: a lightning fall with a spark on top (the ♭9 fall of a dominant)
            {
                q.Clear();
                q.Add(c + new Vector2(-8f, 10f) * k); q.Add(c + new Vector2(1f, 10f) * k); q.Add(c + new Vector2(-2f, 3f) * k); q.Add(c + new Vector2(5f, 3f) * k);
                q.Add(c + new Vector2(-3f, -11f) * k); q.Add(c + new Vector2(-1f, -1f) * k); q.Add(c + new Vector2(-8f, -1f) * k);
                p.InkFill(q, q.Count, Comic.Pop, 1.3f, Comic.Ink);
                p.Disc(c + new Vector2(9f, 9f) * k, 1.6f * k, Comic.Ink); p.Disc(c + new Vector2(11f, 4f) * k, 1.1f * k, Comic.Ink);
                break;
            }
            case 3:   // slide: a smooth ramp with fine ticks (every half step)
            {
                q.Clear(); q.Add(c + new Vector2(-12f, 9f) * k); q.Add(c + new Vector2(12f, -9f) * k); q.Add(c + new Vector2(-12f, -9f) * k);
                p.InkFill(q, 3, stone, 1.2f, Comic.Ink);
                for (int i = 1; i < 6; i++) { float u = i / 6f; Vector2 a = Vector2.Lerp(c + new Vector2(-12f, 9f) * k, c + new Vector2(12f, -9f) * k, u); p.Line(a, a + new Vector2(0f, -2.4f * k), 1f, Comic.Ink); }
                break;
            }
            case 4:   // bright: four steps and a small sun over the top one
                for (int i = 0; i < 4; i++) p.RoundRect(new Rect(c.x + (-9f + i * 5.5f) * k, c.y - 10f * k, 5.3f * k, (12f - i * 3f) * k), 0.8f * k, stone, 1.1f, Comic.Ink);
                p.Disc(c + new Vector2(-8f, 8f) * k, 3.6f * k, Comic.Pop); p.Arc(c + new Vector2(-8f, 8f) * k, 3.6f * k, 1f, Comic.Ink);
                for (int r = 0; r < 6; r++) { float a = r * 60f * Mathf.Deg2Rad; Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.Line(c + new Vector2(-8f, 8f) * k + d * 5f * k, c + new Vector2(-8f, 8f) * k + d * 6.8f * k, 1f, Comic.Ink); }
                break;
            default:  // walk: low steps and two little feet walking down them (a bass walk)
                for (int i = 0; i < 4; i++) p.RoundRect(new Rect(c.x + (-12f + i * 6f) * k, c.y - 11f * k, 5.8f * k, (9f - i * 2f) * k), 0.8f * k, stone, 1.1f, Comic.Ink);
                p.Disc(c + new Vector2(-6f, 4f) * k, 2.4f * k, 1.6f * k, Comic.Ink); p.Disc(c + new Vector2(2f, 1f) * k, 2.4f * k, 1.6f * k, Comic.Ink);
                break;
        }
    }

    /// <summary>v7 stairs: the type button — the current type's glyph and a small ▾ (a click opens the fan).</summary>
    void PaintTypeButton(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0f, 2f);
        int type = shownFor != null && shownFor.IsStairs ? Mathf.Clamp(shownFor.stairType, 0, 5) : 0;
        PaintStairGlyph(p, type, c, 0.95f, Comic.Opaque(StairStone));
        q.Clear(); q.Add(c + new Vector2(10f, -14f)); q.Add(c + new Vector2(16f, -14f)); q.Add(c + new Vector2(13f, -18f));
        p.Fill(q, 3, Comic.Ink);
        if (fanStar == type && shownFor != null && shownFor.IsStairs) PaintStar(p, c + new Vector2(13f, 12f), 4.5f);
    }

    static void PaintStar(InkPainter p, Vector2 c, float r)
    {
        q.Clear();
        for (int k = 0; k < 10; k++) { float a = (90f + k * 36f) * Mathf.Deg2Rad; float rr = k % 2 == 0 ? r : r * 0.45f; q.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr); }
        p.InkFill(q, q.Count, JobBadge.StarGold, 1.1f, Comic.Ink);
    }

    void PaintFanItem(InkPainter p, int type)
    {
        Vector2 c = p.Area.center;
        PaintStairGlyph(p, type, c, 0.9f, Comic.Opaque(StairStone));
        if (type == fanStar) PaintStar(p, c + new Vector2(13f, 13f), 5.5f);
        if (shownFor != null && shownFor.IsStairs && shownFor.stairType == type) p.Arc(c, 18.5f, 2f, Comic.Ink, 0f, 360f, 3f, 2.4f);   // the current one: a dotted ring
    }

    /// <summary>v7 stairs direction: four steps falling (↓) or climbing (↑) with the arrow beside them.</summary>
    void PaintDir(InkPainter p)
    {
        Vector2 c = p.Area.center;
        bool up = shownFor != null && shownFor.IsStairs && shownFor.stairDir > 0;
        Color st = Comic.Opaque(StairStone);
        for (int i = 0; i < 4; i++)
        {
            int h = up ? i : 3 - i;
            p.RoundRect(new Rect(c.x - 14f + i * 5.2f, c.y - 11f, 5f, 5f + h * 4f), 0.8f, st, 1.1f, Comic.Ink);
        }
        Vector2 a0 = up ? c + new Vector2(10f, -8f) : c + new Vector2(10f, 10f), a1 = up ? c + new Vector2(10f, 8f) : c + new Vector2(10f, -6f);
        p.Line(a0, a1, 2.4f, Comic.Ink, true);
        p.Head(a1 + (a1 - a0).normalized * 2f, (a1 - a0).normalized, 6f, 7f, Comic.Ink);
    }

    void PaintPlusMinus(InkPainter p, bool plus)
    {
        Vector2 c = p.Area.center;
        var t = shownFor;
        bool can = t != null && t.IsStairs && (plus ? t.stairSteps < ProjectConfig.StairMaxSteps : t.stairSteps > ProjectConfig.StairMinSteps);
        float a = can ? 1f : 0.35f;
        c += ShakeOf(plus ? 5 : 4);
        p.Disc(c, 9f, Comic.A(Comic.Ink, a)); p.Disc(c, 7.4f, Comic.Cream);
        p.Line(c + new Vector2(-4f, 0f), c + new Vector2(4f, 0f), 2.4f, Comic.A(Comic.Ink, a), true);
        if (plus) p.Line(c + new Vector2(0f, -4f), c + new Vector2(0f, 4f), 2.4f, Comic.A(Comic.Ink, a), true);
    }

    /// <summary>v7 stairs rate — the size language: a big cube = slow steps, a small one = quick; triplets = three pips over the cube.</summary>
    void PaintRate(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int r = shownFor != null && shownFor.IsStairs ? MeasureState.StairRate(shownFor.stairRate) : 12;
        float s = r == 24 ? 22f : (r == 12 ? 16f : (r == 8 ? 13f : 10.5f));
        p.Cube(c + new Vector2(0f, -2f), s, Comic.Opaque(StairStone), Comic.Ink, 1.4f);
        if (r == 8) for (int k = 0; k < 3; k++) p.Disc(c + new Vector2(-5f + k * 5f, 12f), 1.5f, Comic.Ink);
        if (r == 6) { p.Line(c + new Vector2(-15f, 2f), c + new Vector2(-10f, 2f), 1.4f, Comic.Ink, true); p.Line(c + new Vector2(-16f, -3f), c + new Vector2(-10f, -3f), 1.4f, Comic.Ink, true); }
    }

    /// <summary>v7 stairs lead-in: a bar line (the chord change) and the run — falling INTO it (lead-in) or starting right after it.</summary>
    void PaintLead(InkPainter p)
    {
        Vector2 c = p.Area.center;
        bool lead = shownFor == null || !shownFor.IsStairs || shownFor.stairLead;
        bool up = shownFor != null && shownFor.IsStairs && shownFor.stairDir > 0;
        float bx = lead ? c.x + 12f : c.x - 13f;
        q.Clear(); q.Add(new Vector2(bx, c.y - 14f)); q.Add(new Vector2(bx, c.y + 14f));
        p.Stroke(q, 2, 2.4f, Comic.Ink, false, 3.4f, 2.2f);
        Color st = Comic.Opaque(StairStone);
        float x0 = lead ? c.x - 14f : c.x - 10f;
        for (int i = 0; i < 3; i++)
        {
            int h = up ? i : 2 - i;
            p.RoundRect(new Rect(x0 + i * 7f, c.y - 10f, 6.5f, 5f + h * 5f), 0.8f, st, 1.1f, Comic.Ink);
        }
        if (lead) p.Head(new Vector2(bx - 1.5f, c.y + (up ? 7f : -2f)), Vector2.right, 4f, 5f, Comic.Pop, 1f, Comic.Ink);
    }

    /// <summary>v7 phrase length tabs: ½ 1 2 4 (Bangers digits on top) and auto (a stretch arrow); the phrase's length is filled in its colour.</summary>
    void PaintLen(InkPainter p, int k)
    {
        Rect r = p.Area;
        var t = shownFor;
        bool sel = false;
        if (t != null && t.IsPhrase) sel = k < 4 ? t.phraseBeats == LenBeats(k) : t.phraseBeats == AutoBeats(t);
        var box = new Rect(r.xMin + 2f, r.center.y - 12f, r.width - 4f, 24f);
        if (sel) { p.Shift = new Vector2(1.5f, -2f); p.RoundRect(box, 5f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero; }
        p.RoundRect(box, 5f, sel ? Comic.Opaque(PhraseTint) : Comic.A(Comic.Cream, 0.6f), sel ? 1.8f : 1.2f, sel ? Comic.Ink : Comic.A(Comic.Ink, 0.6f));
        if (k < 4) return;
        Vector2 c = r.center;
        p.Line(c + new Vector2(-8f, 0f), c + new Vector2(8f, 0f), 2f, Comic.Ink, true);
        p.Head(c + new Vector2(11f, 0f), Vector2.right, 4.5f, 6f, Comic.Ink); p.Head(c + new Vector2(-11f, 0f), Vector2.left, 4.5f, 6f, Comic.Ink);
        p.Line(c + new Vector2(0f, -5f), c + new Vector2(0f, 5f), 1.4f, Comic.A(Comic.Ink, 0.7f), true);
    }

    static void Roll(InkPainter p, Rect r, Color fill, bool dotted)
    {
        if (!dotted) { p.Shift = new Vector2(1.2f, -1.6f); p.RoundRect(r, 2f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero; }
        p.RoundRect(r, 2f, fill, 1.3f, Comic.Ink, dotted ? 2.2f : 0f, dotted ? 1.7f : 0f);
        float[] ys = { 0.25f, 0.62f, 0.45f };
        for (int k = 0; k < 3; k++) { float x = r.xMin + 2f + k * (r.width - 4f) / 3f; p.RoundRect(new Rect(x, r.yMin + r.height * ys[k] - 1.3f, (r.width - 4f) / 3f - 1f, 2.6f), 1f, dotted ? Comic.A(Comic.Ink, 0.5f) : Comic.Ink); }
    }

    /// <summary>v7 phrase double: the phrase and its copy joined on the right (dotted: the notes repeated there).</summary>
    void PaintDouble(InkPainter p)
    {
        Vector2 c = p.Area.center + ShakeOf(6);
        Roll(p, new Rect(c.x - 15f, c.y - 7f, 14f, 13f), Comic.Opaque(PhraseTint), false);
        Roll(p, new Rect(c.x + 0f, c.y - 7f, 14f, 13f), Comic.A(PhraseTint, 0.35f), true);
        p.Line(c + new Vector2(-15f, 11f), c + new Vector2(14f, 11f), 1.4f, Comic.Ink, true);
        p.Head(c + new Vector2(16.5f, 11f), Vector2.right, 4f, 5f, Comic.Ink);
    }

    /// <summary>§20.1: a little piano and an arrow growing it — out of its low end (left) or its high end (right) — with a +.</summary>
    void PaintKeysGrow(InkPainter p, bool up)
    {
        Vector2 c = p.Area.center + ShakeOf(up ? 9 : 8);
        var t = shownFor;
        bool can = t != null && t.IsKeyboard && KeyCountOf(t) + 12 <= MaxKeys;
        float a = can ? 1f : 0.4f, s = up ? 1f : -1f;
        Rect piano = new Rect(c.x - 10.5f - s * 6f, c.y - 9f, 21f, 16f);
        p.Shift = new Vector2(1.2f, -1.6f); p.RoundRect(piano, 2.4f, Comic.A(Comic.Ink, 0.75f * a)); p.Shift = Vector2.zero;
        p.RoundRect(piano, 2.4f, Comic.A(KeyboardWood, a), 1.4f, Comic.A(Comic.Ink, a));
        Rect keys = new Rect(piano.xMin + 2f, piano.yMin + 2f, piano.width - 4f, piano.height - 6f);
        p.RoundRect(keys, 1f, Comic.A(IslandTray.Ivory, a), 1f, Comic.A(Comic.Ink, a));
        for (int k = 1; k < 5; k++) { float x = keys.xMin + k * keys.width / 5f; p.Line(new Vector2(x, keys.yMin + 0.6f), new Vector2(x, keys.yMax - 0.6f), 0.9f, Comic.A(Comic.Ink, a)); if (k != 3) p.RoundRect(new Rect(x - 1.4f, keys.center.y - 0.6f, 2.8f, keys.height * 0.55f), 0.5f, Comic.A(Comic.Ink, a)); }
        Vector2 a0 = new Vector2(c.x + s * 5.5f, c.y - 1f), a1 = new Vector2(c.x + s * 14f, c.y - 1f);
        p.Line(a0, a1, 2.4f, Comic.A(Comic.Ink, a), true);
        p.Head(a1 + new Vector2(s * 2.5f, 0f), new Vector2(s, 0f), 5f, 6.5f, Comic.A(Comic.Ink, a));
        Vector2 pc = new Vector2(c.x + s * 10f, c.y + 10f);
        p.Line(pc + new Vector2(-3.5f, 0f), pc + new Vector2(3.5f, 0f), 2.2f, Comic.A(Comic.Ink, a), true);
        p.Line(pc + new Vector2(0f, -3.5f), pc + new Vector2(0f, 3.5f), 2.2f, Comic.A(Comic.Ink, a), true);
    }

    /// <summary>§20.1: fit to the melody — a little piano squeezed by two arrows.</summary>
    void PaintKeysFit(InkPainter p)
    {
        Vector2 c = p.Area.center + ShakeOf(10);
        Rect piano = new Rect(c.x - 7f, c.y - 6f, 14f, 12f);
        p.RoundRect(piano, 2f, KeyboardWood, 1.2f, Comic.Ink);
        p.RoundRect(new Rect(piano.xMin + 1.5f, piano.yMin + 1.5f, piano.width - 3f, piano.height - 4.5f), 1f, IslandTray.Ivory, 0.8f, Comic.Ink);
        p.Line(new Vector2(c.x - 18f, c.y), new Vector2(c.x - 11f, c.y), 1.8f, Comic.Ink, true); p.Head(new Vector2(c.x - 9f, c.y), Vector2.right, 4f, 5f, Comic.Ink);
        p.Line(new Vector2(c.x + 18f, c.y), new Vector2(c.x + 11f, c.y), 1.8f, Comic.Ink, true); p.Head(new Vector2(c.x + 9f, c.y), Vector2.left, 4f, 5f, Comic.Ink);
    }

    /// <summary>v7 phrase cells: a strip of four wide cells (eighths) or eight fine ones (sixteenths).</summary>
    void PaintCells(InkPainter p)
    {
        Vector2 c = p.Area.center + ShakeOf(7);
        bool fine = shownFor != null && shownFor.IsPhrase && shownFor.phraseGrid == 6;
        int n = fine ? 8 : 4;
        float w = 28f / n;
        for (int k = 0; k < n; k++)
        {
            var r = new Rect(c.x - 14f + k * w + 0.4f, c.y - 6f, w - 0.8f, 12f);
            p.RoundRect(r, 1f, k % (fine ? 4 : 2) == 0 ? Comic.Opaque(PhraseTint) : Comic.Cream, 1.1f, Comic.Ink);
        }
    }

    static void PaintDots(InkPainter p)
    {
        Vector2 c = p.Area.center;
        for (int k = -1; k <= 1; k++) p.Disc(c + new Vector2(k * 7f, 0f) + p.Jit(k + 2, 0.4f), 2.6f, Comic.Ink);
    }

    static void PaintDice(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Cube(c, 24f, Comic.Cream, Comic.Ink, 1.6f);
        // pips on the top and the left faces
        p.Disc(c + new Vector2(0f, 6f), 1.8f, Comic.Ink);
        p.Disc(c + new Vector2(-6f, -1f), 1.5f, Comic.Ink); p.Disc(c + new Vector2(-4f, -6f), 1.5f, Comic.Ink);
        p.Disc(c + new Vector2(5f, -2f), 1.5f, Comic.Ink); p.Disc(c + new Vector2(5f, -6f), 1.5f, Comic.Ink);
    }

    void PaintBarBlock(InkPainter p, int k)
    {
        Rect r = p.Area;
        int bars = shownFor != null ? Mathf.Clamp(shownFor.bars, 1, 4) : 1;
        Rect b = new Rect(r.xMin + 3f, r.center.y - 9f, r.width - 6f, 18f);
        if (k <= bars) { p.Shift = new Vector2(1.5f, -2f); p.RoundRect(b, 4f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero; p.RoundRect(b, 4f, Chord, 1.8f, Comic.Ink); }
        else p.RoundRect(b, 4f, Comic.A(Comic.Ink, 0.05f), 1.6f, Comic.A(Comic.Ink, 0.75f), 3f, 2.5f);
    }

    /// <summary>Energy: the standard level bars (▂▄▆█), as many lit as the island's energy, warmer as it rises.</summary>
    void PaintEnergy(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int e = shownFor != null ? Mathf.Clamp(shownFor.energy, 0, 3) : 2;
        Color warm = Color.Lerp(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.42f, 0.2f), e / 3f);
        for (int k = 0; k < 4; k++)
        {
            float h = 7f + k * 5f;
            var r = new Rect(c.x - 15f + k * 8f, c.y - 12f, 6f, h);
            if (k <= e) p.RoundRect(r, 1.5f, warm, 1.4f, Comic.Ink);
            else p.RoundRect(r, 1.5f, Comic.A(Comic.Ink, 0.06f), 1.2f, Comic.A(Comic.Ink, 0.5f), 2.2f, 1.8f);
        }
    }

    /// <summary>CLIMB on the more card: every path on the grid climbs (Climb.SetGrid, one History entry) — off → ×1 → ×2 → ×3 → off; a grid with
    /// no pitched paths refuses with a thud.</summary>
    void CycleClimb()
    {
        var t = ResolveTarget();
        if (t == null) return;
        int want = Climb.Next(Climb.LevelOf(t));
        if (!Climb.SetGrid(t, want)) { AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        shownClimb = -1;
        AudioPool.UI(ProceduralAudio.Tick(), 0.28f, 0.85f + 0.1f * want);
    }

    /// <summary>The climb picture: a note, the same note risen higher with an up arrow beside it (the high note reaching higher); pips = the level.</summary>
    void PaintClimb(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int lv = shownFor != null ? Climb.LevelOf(shownFor) : 0;
        Color ink = lv > 0 ? Comic.Ink : Comic.A(Comic.Ink, 0.6f);
        p.Disc(c + new Vector2(-2f, -6f), 3.8f, 3f, ink);
        p.Disc(c + new Vector2(7f, 5f), 4.4f, 3.5f, Comic.Ink); p.Disc(c + new Vector2(7f, 5f), 3f, 2.2f, lv > 0 ? Comic.Pop : Comic.Cream);
        p.Line(c + new Vector2(1.4f, -6f), c + new Vector2(1.4f, 5f), 1.4f, ink, true);
        p.Line(c + new Vector2(10.6f, 5f), c + new Vector2(10.6f, 15f), 1.5f, Comic.Ink, true);
        q.Clear(); q.Add(c + new Vector2(-11f, -10f)); q.Add(c + new Vector2(-11f, 5f));
        p.Stroke(q, 2, 2f, ink, false, 0f, 0f, -1f, true);
        p.Head(c + new Vector2(-11f, 10f), Vector2.up, 5f, 6.5f, ink);
        for (int k = 0; k < Climb.MaxLevel; k++) { Vector2 d = c + new Vector2(-6f + k * 6f, -15f); p.Disc(d, 2f, Comic.Ink); p.Disc(d, 1.2f, k < lv ? Comic.Pop : Comic.Cream); }
    }

    /// <summary>The chord picker's picture: the standard piano keys (🎹) — four white keys and three black ones.</summary>
    static void PaintPianoKeys(InkPainter p)
    {
        Vector2 c = p.Area.center;
        const float w = 5.5f, h = 17f;
        float x0 = c.x - 2f * w, y0 = c.y - h * 0.5f;
        p.RoundRect(new Rect(x0, y0, 4f * w, h), 1.5f, Color.white, 1.5f, Comic.Ink);
        for (int k = 1; k < 4; k++) p.Line(new Vector2(x0 + k * w, y0 + 1f), new Vector2(x0 + k * w, y0 + h - 1f), 1.1f, Comic.Ink);
        for (int k = 1; k < 4; k++) p.RoundRect(new Rect(x0 + k * w - 1.8f, y0 + h * 0.42f, 3.6f, h * 0.58f), 0.8f, Comic.Ink);
    }

    /// <summary>The drum fill's picture: the standard drum with two crossed sticks (🥁).</summary>
    static void PaintDrum(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0f, -3f);
        p.Line(c + new Vector2(-10f, 15f), c + new Vector2(3f, 3f), 2f, Comic.Ink, true);
        p.Line(c + new Vector2(10f, 15f), c + new Vector2(-3f, 3f), 2f, Comic.Ink, true);
        p.Disc(c + new Vector2(-10f, 15f), 2f, Comic.Ink); p.Disc(c + new Vector2(10f, 15f), 2f, Comic.Ink);
        var body = new Rect(c.x - 10f, c.y - 9f, 20f, 10f);
        p.RoundRect(body, 2f, Comic.Danger, 1.5f, Comic.Ink);
        for (int k = 0; k < 4; k++) { float x = body.xMin + 2.5f + k * 5f; p.Line(new Vector2(x, body.yMin + 1f), new Vector2(x + 2.5f, body.yMax - 1f), 1f, Comic.Cream); }
        p.Disc(new Vector2(c.x, body.yMax), 10.8f, 3.8f, Comic.Ink);
        p.Disc(new Vector2(c.x, body.yMax), 9.4f, 2.6f, Color.white);
    }

    static void PaintTearOff(InkPainter p)
    {
        Vector2 c = p.Area.center;
        // a small red paper corner with a ragged edge and a cream trash can (🗑)
        q.Clear();
        q.Add(c + new Vector2(-15f, -12f)); q.Add(c + new Vector2(15f, -13f)); q.Add(c + new Vector2(14f, 12f));
        q.Add(c + new Vector2(6f, 9f)); q.Add(c + new Vector2(0f, 13f)); q.Add(c + new Vector2(-6f, 9f)); q.Add(c + new Vector2(-14f, 12f));
        p.Shift = new Vector2(2.5f, -3f); p.Fill(q, q.Count, Comic.A(Comic.Ink, 0.85f)); p.Shift = Vector2.zero;
        p.InkFill(q, q.Count, Comic.Danger, 1.8f, Comic.Ink);
        Vector2 t = c + new Vector2(0f, -1f);
        q.Clear(); q.Add(t + new Vector2(-5.5f, 3.5f)); q.Add(t + new Vector2(5.5f, 3.5f)); q.Add(t + new Vector2(4.2f, -8f)); q.Add(t + new Vector2(-4.2f, -8f));
        p.Fill(q, 4, Comic.Cream);
        p.Line(t + new Vector2(-7.5f, 5.5f), t + new Vector2(7.5f, 5.5f), 2.2f, Comic.Cream, true);
        p.Line(t + new Vector2(-2f, 7.5f), t + new Vector2(2f, 7.5f), 2f, Comic.Cream, true);
        for (int k = -1; k <= 1; k++) p.Line(t + new Vector2(k * 2.2f, 1.5f), t + new Vector2(k * 1.8f, -6f), 1f, Comic.Danger);
    }

    void PaintGhost(InkPainter p, Vector2[] poly, bool ok, float shake)
    {
        if (!ok || !hasPoly) return;
        Vector2 off = shake > 0f ? new Vector2(Mathf.Sin(shake * 40f) * 6f * shake, 0f) : Vector2.zero;
        // a dotted island outline: the footprint pulled in a little, its corners cut round (an empty slot, Melatonin)
        Vector2 cen = Centroid(poly);
        q.Clear();
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = Vector2.Lerp(poly[i], cen, 0.08f), prev = Vector2.Lerp(poly[(i + 3) % 4], cen, 0.08f), next = Vector2.Lerp(poly[(i + 1) % 4], cen, 0.08f);
            q.Add(Vector2.Lerp(c, prev, 0.12f) + off);
            q.Add(Vector2.Lerp(c, Vector2.Lerp(prev, next, 0.5f), 0.035f) + off);
            q.Add(Vector2.Lerp(c, next, 0.12f) + off);
        }
        bool hot = p == ghostAbove ? addAboveBtn != null && addAboveBtn.Hover : addBelowBtn != null && addBelowBtn.Hover;
        bool full = shownFor != null && ColumnFull(shownFor);
        p.Fill(q, q.Count, Comic.A(full ? Comic.Danger : Comic.Cream, hot ? 0.16f : 0.05f));
        p.Stroke(q, q.Count, hot ? 3f : 2.2f, full ? Comic.A(Comic.Danger, 0.9f) : Comic.A(Comic.Cream, hot ? 1f : 0.8f), true, 8f, 6f);
        // v8: a grid of several measures offers one slot per measure (a normal one-measure grid on that measure): dotted dividers and a + each
        int slots = GhostSlots();
        if (slots >= 2 && !full)
        {
            int hotSlot = hot ? MeasureUnderPointer(p == ghostAbove) : -1;
            for (int k = 1; k < slots; k++)
            {
                float f = k / (float)slots;
                Vector2 a = Vector2.Lerp(poly[0], poly[1], f) + off, b = Vector2.Lerp(poly[3], poly[2], f) + off;
                q.Clear(); q.Add(Vector2.Lerp(a, b, 0.1f)); q.Add(Vector2.Lerp(a, b, 0.9f));
                p.Stroke(q, 2, 1.8f, Comic.A(Comic.Cream, 0.7f), false, 6f, 5f);
            }
            for (int k = 1; k < slots; k++)   // slot 0's + is the real button (plusAbove / plusBelow: the tutorial's target); the others are painted
            {
                float f = (k + 0.5f) / slots;
                Vector2 c = Vector2.Lerp(Vector2.Lerp(poly[0], poly[1], f), Vector2.Lerp(poly[3], poly[2], f), 0.5f) + off;
                bool h = k == hotSlot;
                p.Disc(c, h ? 15f : 13f, Comic.Ink); p.Disc(c, h ? 13f : 11f, Comic.Cream);
                p.Line(c + new Vector2(-6f, 0f), c + new Vector2(6f, 0f), 2.6f, Comic.Ink, true);
                p.Line(c + new Vector2(0f, -6f), c + new Vector2(0f, 6f), 2.6f, Comic.Ink, true);
            }
        }
    }

    static bool Inside(Vector2[] poly, Vector2 pt)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > pt.y) != (poly[j].y > pt.y) && pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y + 1e-6f) + poly[i].x) inside = !inside;
        return inside;
    }

    // ------------------------------------------------------------------ per frame
    bool Hovering()
    {
        if (group == null || !group.blocksRaycasts) return false;
        if (gripBtn.Hover || eyeBtn.Hover || regDownBtn.Hover || regUpBtn.Hover || dupRightBtn.Hover || dupBelowBtn.Hover || moreBtn.Hover || diceBtn.Hover || repeatBtn.Hover) return true;
        if ((carryBtn != null && carryBtn.Hover) || (pasteBtn != null && pasteBtn.gameObject.activeInHierarchy && pasteBtn.Hover)) return true;
        if ((copyPathsBtn != null && copyPathsBtn.gameObject.activeInHierarchy && copyPathsBtn.Hover) || (pastePathsBtn != null && pastePathsBtn.gameObject.activeInHierarchy && pastePathsBtn.Hover)) return true;
        if (V7Hover()) return true;
        if (addAboveBtn.Hover || addBelowBtn.Hover) return true;
        if (moreOpen && RectTransformUtility.RectangleContainsScreenPoint(more, Input.mousePosition, null)) return true;
        return RectTransformUtility.RectangleContainsScreenPoint(band, Input.mousePosition, null);
    }

    HudButton[] v7Btns;
    int kindsTick = int.MinValue;
    /// <summary>v7: the pointer is on one of the v7 controls (the stickers stand above the band; the fan is above them).</summary>
    bool V7Hover()
    {
        if (v7Btns == null) v7Btns = new[] { styleBtn, varyBtn, fillSecBtn, launchBtn, spotBtn, typeBtn, dirBtn, stepsDownBtn, stepsUpBtn, rateBtn, leadBtn, doubleBtn, cellsBtn, keysBelowBtn, keysAboveBtn, keysFitBtn };
        foreach (var b in v7Btns)
            if (b != null && b.gameObject.activeInHierarchy && b.Hover) return true;
        foreach (var b in lenBtns) if (b != null && b.gameObject.activeInHierarchy && b.Hover) return true;
        if (fanOpen) { foreach (var b in fanBtns) if (b != null && b.Hover) return true; if (RectTransformUtility.RectangleContainsScreenPoint(fan, Input.mousePosition, null)) return true; }
        return false;
    }

    bool Suppressed()
    {
        var pm = PathManager.I;
        if (pm == null || SongManager.I == null || !SongManager.I.HasSong) return true;
        if (pm.IsDrawing || (pm.Drag != null && pm.Drag.Dragging)) return true;
        if (IslandTray.I != null && IslandTray.I.Dragging) return true;
        if (Presenter.Active || MainMenu.IsShown || CubeInspector.IsOpen) return true;
        if (WorldInput.WorldLocked) return true;
        if (UIManager.I != null && UIManager.I.CardShown > 0.05f) return true;
        return false;
    }

    void Update()
    {
        if (group == null || gripBtn == null) return;   // not built (or a domain reload dropped the references)
        if (GripHeld && !Input.GetMouseButton(0)) GripHeld = false;
        float dt = Time.unscaledDeltaTime;
        var pm = PathManager.I; var ui = UIManager.I; var sm = SongManager.I;
        Revalidate();
        KeyBlock want = null;
        bool sup = Suppressed();
        if (pinned != null && (sm == null || (!sm.Islands.Contains(pinned) && !sm.Moons.Contains(pinned)))) pinned = null;
        KeyBlock sel = null;
        if (ui != null && sm != null && sm.HasSong)
        {
            if (ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count) sel = sm.Islands[ui.SelectedMeasure];
            else if (ui.SelectedMoon >= 0 && ui.SelectedMoon < sm.Moons.Count) sel = sm.Moons[ui.SelectedMoon];
        }
        var h = pm != null && !sup ? pm.hoverIsland : null;
        if (h != null) { if (h == hoverCand) hoverT += dt; else { hoverCand = h; hoverT = 0f; } }
        else { hoverCand = null; hoverT = 0f; }
        bool hovering = Hovering();
        bool bySel = false;
        if (hoverCand != null && hoverT >= ShowDelay && hoverCand != target) { want = hoverCand; lingerT = 0f; }
        else if (target != null && (hovering || moreOpen || (hoverCand == target))) { want = target; lingerT = 0f; }
        else if (target != null && lingerT < Linger && (pinned == null || pinned == target)) { lingerT += dt; want = target; bySel = target == sel && !shownByHover; }   // (v7: a pin takes over at once)
        if (want == null && pinned != null) want = pinned;
        if (want == null && sel != null && SectionHeader.Current < 0) { want = sel; bySel = true; }   // v7: a hovered section's header beats the selection's
        if (SectionHeader.Current >= 0 && want != null && want != pinned && !hovering && hoverCand != want) { want = null; bySel = false; }
        if (want != null && !bySel) shownByHover = true; else if (want == null || bySel) shownByHover = want != null && !bySel;
        BySelection = want != null && bySel;
        // fast camera moves hide it (it would lag behind a whipping view)
        if (fastT > 0f) fastT -= dt;
        if (sup || fastT > 0f) want = null;
        if (want != target)
        {
            if (want == null || target == null || want != target) SetMore(false);
            target = want;
            IndexOf(target, out tgtIdx, out tgtMoon);
            if (target != null) { lastAnchor = new Vector2(float.NaN, 0f); ForceRefresh(); }
        }
        Current = target != null && shownA > 0.01f ? target : (target != null ? target : null);
        if (target == null) Current = null;
        if (shakeAbove > 0f) { shakeAbove = Mathf.Max(0f, shakeAbove - dt / 0.35f); ghostAbove.Repaint(); }
        if (shakeBelow > 0f) { shakeBelow = Mathf.Max(0f, shakeBelow - dt / 0.35f); ghostBelow.Repaint(); }
        // stepped fade (two drawings)
        int t = HudKit.TwosTick;
        if (t != tick)
        {
            tick = t;
            float goal = target != null ? 1f : 0f;
            if (shownA != goal) shownA = goal > shownA ? Mathf.Min(1f, shownA + 0.5f) : Mathf.Max(0f, shownA - 0.5f);
            group.alpha = shownA;
            bool live = shownA > 0.5f && target != null;
            group.blocksRaycasts = live; group.interactable = live;
        }
        if (target != null) RefreshLook();
        UpdateV7(dt);
    }

    /// <summary>v7 per frame: the fan (hover plays that run; a click elsewhere or the header leaving closes it), the flow sticker's pop, shakes.</summary>
    void UpdateV7(float dt)
    {
        if (fanOpen)
        {
            var t = target;
            if (t == null || !t.IsStairs || shownA < 0.5f) SetFan(false);
            else
            {
                int h = -1;
                for (int k = 0; k < 6; k++) if (fanBtns[k] != null && fanBtns[k].Hover) { h = k; break; }
                if (h != fanHover) { fanHover = h; if (h >= 0) PlayRun(t, h); }
                if (Input.GetMouseButtonDown(0) && !V7Hover() && !(typeBtn != null && typeBtn.Hover)) SetFan(false);
            }
        }

        if (v7Shake > 0f)
        {
            v7Shake = Mathf.Max(0f, v7Shake - dt / 0.35f);
            var a = v7ShakeWhich == 1 ? carryArt : (v7ShakeWhich == 2 ? fillSecArt : (v7ShakeWhich == 3 ? launchArt : (v7ShakeWhich == 4 ? stepsDownArt : (v7ShakeWhich == 5 ? stepsUpArt
                  : (v7ShakeWhich == 6 ? doubleArt : (v7ShakeWhich == 8 ? keysBelowArt : (v7ShakeWhich == 9 ? keysAboveArt : (v7ShakeWhich == 10 ? keysFitArt : cellsArt))))))));
            if (a != null) a.Repaint();
        }
    }

    void ForceRefresh() { shownClimb = -1; shownSleep = -1; shownReg = 99; shownBars = -1; shownEnergy = -1; shownFall = -1; shownMood = -1; shownFill = -1; shownRepeat = -1; shownCarry = -1; shownPasteKey = int.MinValue; shownQ = (ChordQuality)(-1); shownChord = new Color(-1f, 0f, 0f); shownFor = null;
        shownKind = -1; shownRewind = -1; shownVary = -1; shownLaunch = -1; shownSpot = -1; shownFillSec = -1; shownStair = int.MinValue; shownPhrase = int.MinValue; shownStyleVis = -1; shownKeys = int.MinValue; }

    /// <summary>Repaints only what changed on the target island.</summary>
    void RefreshLook()
    {
        var kb = target;
        bool moon = kb.IsMoon, keyboard = kb.IsKeyboard;
        int kind = KindOf(kb);
        if (shownFor != kb || moon != wasMoon || keyboard != wasKeyboard || kind != shownKind)
        {
            if (kind != shownKind || shownFor != kb) SetFan(false);
            shownFor = kb; wasMoon = moon; wasKeyboard = keyboard; shownKind = kind;
            LayoutBand(kind);
            // v7: every control shows for the kinds it serves (v6: a keyboard keeps grip, eye, register, repeat, carry, duplicate, more (delete);
            // v7: stairs and phrases have their own bands, their duplicates in the more card)
            int bit = 1 << kind;
            foreach (var kv in kindMask) { bool on = (kv.Value & bit) != 0; if (kv.Key.activeSelf != on) kv.Key.SetActive(on); }
            bool chordy = kind == 0;
            if (!chordy) { aboveOk = belowOk = false; }
            if (more != null) more.sizeDelta = chordy ? new Vector2(264f, 176f) : (kind == 3 ? new Vector2(150f, 86f) : (kind == 4 ? new Vector2(112f, 86f) : new Vector2(84f, 86f)));   // (room for the word under delete)
            if (deleteRt != null) deleteRt.anchoredPosition = chordy ? new Vector2(98f, -46f) : (kind == 3 ? new Vector2(44f, 8f) : (kind == 4 ? new Vector2(24f, 8f) : new Vector2(0f, 8f)));
            if (moreDupRight != null) moreDupRight.GetComponent<RectTransform>().anchoredPosition = kind == 4 ? new Vector2(-24f, 8f) : new Vector2(-44f, 8f);
            if (moreOpen) more.gameObject.SetActive(true);
            gripBtn.GetComponent<InkPainter>().Repaint(); diceBtn.GetComponent<InkPainter>().Repaint();
            if (carryArt != null) carryArt.Repaint();
            shownStyleVis = -1; shownFillSec = -1;
        }
        RefreshV7(kb, kind);
        if (kind >= 2 && (HudKit.TwosTick != kindsTick || shownStair == int.MinValue || shownPhrase == int.MinValue || shownKeys == int.MinValue)) { kindsTick = HudKit.TwosTick; RefreshKinds(kb, kind); }   // (on twos: it reads the chord / the notes)
        Color chord = ChordOf(kb);
        if (chord != shownChord)
        {
            shownChord = chord;
            tailL.color = chord; tailR.color = chord;
            gripBtn.GetComponent<InkPainter>().Repaint(); eyeArt.Repaint(); ladderArt.Repaint(); dupRightBtn.GetComponent<InkPainter>().Repaint(); dupBelowBtn.GetComponent<InkPainter>().Repaint(); repeatArt.Repaint();
            if (carryArt != null) carryArt.Repaint();
            if (pasteArt != null) pasteArt.Repaint();
            foreach (var a in barArts) a.Repaint();
            if (chordDisc != null) chordDisc.color = chord;
        }
        int sleep = kb.sleep ? 1 : 0;
        if (sleep != shownSleep)
        {
            shownSleep = sleep; eyeArt.Repaint();
            InkCaption.Attach(eyeBtn.gameObject, kb.sleep ? "turn on" : "turn off");
            bandShape.color = kb.sleep ? new Color(0.86f, 0.83f, 0.88f) : Comic.Cream;   // an off island's ribbon desaturates
        }
        if (kb.register != shownReg) { shownReg = kb.register; ladderArt.Repaint(); regDownArt.Repaint(); regUpArt.Repaint(); }
        if (!moon && kb.repeat != shownRepeat) { shownRepeat = kb.repeat; RefreshRepeat(kb.repeat); }
        if (repeatPop > 0f && repeatSticker != null)
        {
            repeatPop = Mathf.Max(0f, repeatPop - Time.unscaledDeltaTime / 0.3f);
            float k = Look.Stepped(1f - repeatPop);
            repeatSticker.localScale = Vector3.one * (k < 0.34f ? 1.35f : (k < 0.67f ? 0.92f : 1f));
        }
        // v6: carry (its sticker pops like repeat's; a refusal shakes the picture) and the paste tab
        if (!moon && ExtendOf(kb) != shownCarry) { shownCarry = ExtendOf(kb); RefreshCarry(shownCarry); }
        if (carryPop > 0f && carrySticker != null)
        {
            carryPop = Mathf.Max(0f, carryPop - Time.unscaledDeltaTime / 0.3f);
            float k = Look.Stepped(1f - carryPop);
            carrySticker.localScale = Vector3.one * (k < 0.34f ? 1.35f : (k < 0.67f ? 0.92f : 1f));
        }
        if (carryShake > 0f) { carryShake = Mathf.Max(0f, carryShake - Time.unscaledDeltaTime / 0.35f); if (carryArt != null) carryArt.Repaint(); }
        bool paste = Clipboard.CanPasteOn(kb);
        int pasteKey = paste ? 1 + Clipboard.Instrument * 7 : 0;
        if (pasteKey != shownPasteKey)
        {
            shownPasteKey = pasteKey;
            HudKit.SetActive(pasteBtn, paste);
            if (paste && pasteArt != null) pasteArt.Repaint();
        }
        // v7: grid paths — copy (this grid has paths), paste (the copied paths can go here: never on the grid they came from)
        bool pathKind = !moon && !kb.IsStairs;
        int have = pathKind ? Clipboard.PathCount(kb) : 0, fit = pathKind ? Clipboard.PathsFitOn(kb) : 0;
        int pathsKey = (have > 0 ? 1 : 0) + fit * 2 + (Clipboard.PathsVersion % 1000) * 64 + (have % 8) * 64000;
        if (pathsKey != shownPathsKey)
        {
            shownPathsKey = pathsKey;
            HudKit.SetActive(copyPathsBtn, have > 0);
            HudKit.SetActive(pastePathsBtn, fit > 0);
            if (have > 0 && copyPathsArt != null) copyPathsArt.Repaint();
            if (fit > 0) { if (pastePathsText != null) pastePathsText.text = "×" + fit; if (pastePathsBadge != null) pastePathsBadge.gameObject.SetActive(true); if (pastePathsArt != null) pastePathsArt.Repaint(); }
        }
        if (copyPathsPop > 0f && copyPathsBtn != null)
        {
            copyPathsPop = Mathf.Max(0f, copyPathsPop - Time.unscaledDeltaTime / 0.3f);
            float k = Look.Stepped(1f - copyPathsPop);
            copyPathsBtn.transform.localScale = Vector3.one * (copyPathsPop <= 0f ? 1f : (k < 0.34f ? 1.3f : (k < 0.67f ? 0.92f : 1f)));
        }
        if (!moreOpen || moon) return;
        if (kb.bars != shownBars) { shownBars = kb.bars; foreach (var a in barArts) a.Repaint(); }
        if (kb.energy != shownEnergy) { shownEnergy = kb.energy; energyArt.Repaint(); }
        if (kb.fall != shownFall)
        {
            shownFall = kb.fall;
            int f = Mathf.Clamp(kb.fall, 0, 5);
            fallGlyph.sprite = Comic.Glyph(BounceGlyphs.Icons[f], 22f, false);
            InkCaption.Attach(fallBtn.gameObject, BounceGlyphs.Names[f]);   // the style's own word (P's ricochet styles)
        }
        if (kb.mood != shownMood || kb.quality != shownQ)
        {
            shownMood = kb.mood;
            moodGlyph.sprite = Comic.Glyph(MoodIcon(kb, kb.mood), 22f, true);
            moodGlyph.color = Comic.Opaque(kb.chordColor);   // the weather it gives, in its colour (inked)
            InkCaption.Attach(moodBtn.gameObject, MoodWords(kb, kb.mood));
        }
        int fill = kb.fill ? 1 : 0;
        if (fill != shownFill) { shownFill = fill; fillDisc.color = kb.fill ? Comic.Pop : Comic.Cream; }
        int climbLv = Climb.LevelOf(kb);
        if (climbLv != shownClimb) { shownClimb = climbLv; if (climbArt != null) climbArt.Repaint(); if (climbBtn != null) InkCaption.Attach(climbBtn.gameObject, Climb.Caption(climbLv)); }
        if (kb.quality != shownQ)
        {
            shownQ = kb.quality;
            InkCaption.Attach(chordBtn.gameObject, "chord · " + Vibe.Word(Vibe.Of(kb.quality)));
        }
    }

    /// <summary>v7: repaints what changed on the v7 controls (style / vary stickers from ×2, flow, fill, launch; the stairs and phrase bands).</summary>
    void RefreshV7(KeyBlock kb, int kind)
    {
        int bit = 1 << Mathf.Max(0, kind);
        bool playing = (bit & KPlaying) != 0;
        // the repeat style + vary stickers stand up from ×2 (grids that repeat; stairs never vary)
        int vis = ((bit & (KPlaying | KPhrase)) != 0 && kb.repeat >= 2) ? 1 : 0;
        if (vis != shownStyleVis) { shownStyleVis = vis; HudKit.SetActive(styleBtn, vis == 1); HudKit.SetActive(varyBtn, vis == 1); if (vis == 1) { styleArt.Repaint(); varyArt.Repaint(); } }
        int rw = kb.rewind ? 1 : 0;
        if (rw != shownRewind) { shownRewind = rw; if (styleArt != null) styleArt.Repaint(); InkCaption.Attach(styleBtn.gameObject, kb.rewind ? "rewind · on" : "rewind"); }
        if (kb.vary != shownVary)
        {
            shownVary = kb.vary; if (varyArt != null) varyArt.Repaint();
            InkCaption.Attach(varyBtn.gameObject, kb.vary == 1 ? "vary · small changes" : (kb.vary == 2 ? "vary · with an answer" : "vary each repeat"));
        }
        if (!playing) return;
        int lc = kb.launch ? 1 : 0;
        if (lc != shownLaunch) { shownLaunch = lc; launchArt.Repaint(); InkCaption.Attach(launchBtn.gameObject, kb.launch ? LaunchCaptionOn : LaunchCaptionOff); }
        int ld = kb.lead ? 1 : 0;   // v9 (L)
        if (ld != shownSpot && spotBtn != null) { shownSpot = ld; spotArt.Repaint(); InkCaption.Attach(spotBtn.gameObject, kb.lead ? LeadCaptionOn : LeadCaptionOff); }
        int room = ExtendRoom(kb), fc = room > 0 && ExtendOf(kb) < room ? 1 : 0;
        if (fc != shownFillSec) { shownFillSec = fc; HudKit.SetActive(fillSecBtn, fc == 1); if (fc == 1) fillSecArt.Repaint(); }
    }

    /// <summary>v7: the stairs / phrase bands' pictures follow their island (a hash of what they show); §20.1 a keyboard's range items.</summary>
    void RefreshKinds(KeyBlock kb, int kind)
    {
        if (kind == 2)
        {
            int min, max, nl, nn;
            KeyNotes(kb, out min, out max);
            bool canFit = FitKeys(KeyLow(kb), KeyCountOf(kb), min, max, out nl, out nn) && (nl != KeyLow(kb) || nn != KeyCountOf(kb));
            int h = KeyLow(kb) * 131 + KeyCountOf(kb) * 7 + (canFit ? 1 : 0);
            if (h == shownKeys) return;
            shownKeys = h;
            HudKit.SetActive(keysFitBtn, canFit);
            keysBelowArt.Repaint(); keysAboveArt.Repaint(); if (canFit) keysFitArt.Repaint();
            InkCaption.Attach(keysFitBtn.gameObject, canFit ? "fit to the melody · " + ((nn - 1) / 12) + (nn > 13 ? " octaves" : " octave") : "fit to the melody");
            return;
        }
        if (kind == 3)
        {
            int best = BestStairType(kb);
            int h = kb.stairType + 7 * (kb.stairDir > 0 ? 1 : 0) + 17 * kb.stairSteps + 211 * MeasureState.StairRate(kb.stairRate) + 5003 * (kb.stairLead ? 1 : 0) + 50021 * best;
            if (h == shownStair) return;
            shownStair = h; fanStar = best;
            stepsText.text = kb.stairSteps.ToString();
            typeArt.Repaint(); dirArt.Repaint(); stepsDownArt.Repaint(); stepsUpArt.Repaint(); rateArt.Repaint(); leadArt.Repaint();
            foreach (var a in fanArts) a.Repaint();
            InkCaption.Attach(typeBtn.gameObject, "stairs · " + Harmony.StairTypeWord(kb.stairType));
            InkCaption.Attach(dirBtn.gameObject, kb.stairDir > 0 ? "climbs" : "falls");
            InkCaption.Attach(rateBtn.gameObject, RateWord(MeasureState.StairRate(kb.stairRate)));
            InkCaption.Attach(leadBtn.gameObject, kb.stairLead ? "lead-in · ends at the chord change" : "starts with the chord");
        }
        else if (kind == 4)
        {
            int h = kb.phraseBeats + 97 * kb.phraseGrid + 9973 * AutoBeats(kb);
            if (h == shownPhrase) return;
            shownPhrase = h;
            foreach (var a in lenArts) a.Repaint();
            doubleArt.Repaint(); cellsArt.Repaint();
            InkCaption.Attach(cellsBtn.gameObject, kb.phraseGrid == 6 ? "cells · fine" : "cells · wide");
            InkCaption.Attach(lenBtns[4].gameObject, "auto length · " + MeasureTag.WordsFor(AutoBeats(kb)));
        }
    }

    void LateUpdate()
    {
        if (target == null || group == null || ribbon == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var kb = target;
        // the anchor: the middle of the island's front edge (the ribbon hangs from it); its on-screen tilt follows the edge (±6°). v5: the
        // platform as shown this frame (VisualBounds: the merge slide and the repeat belt's ride move it off its logical slot 0)
        Bounds b = kb.VisualBounds;
        float y = kb.VisualCenter.y + 0.05f;
        Vector3 w0 = new Vector3(b.min.x + 0.6f, y, b.min.z), w1 = new Vector3(b.max.x - 0.6f, y, b.min.z), wm = new Vector3(b.center.x, y, b.min.z);
        Vector3 s0 = cam.WorldToScreenPoint(w0), s1 = cam.WorldToScreenPoint(w1), sm = cam.WorldToScreenPoint(wm);
        if (sm.z <= 0f || s0.z <= 0f || s1.z <= 0f) { group.alpha = 0f; group.blocksRaycasts = false; return; }
        Vector2 lp;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sm, null, out lp)) return;
        // fast camera moves: the anchor whipping across the screen hides the header until it settles
        if (!float.IsNaN(lastAnchor.x) && Time.unscaledDeltaTime > 0f && (lp - lastAnchor).magnitude / Time.unscaledDeltaTime > FastPx && !Hovering()) fastT = 0.15f;
        lastAnchor = lp;
        float ang = Mathf.Atan2(s1.y - s0.y, s1.x - s0.x) * Mathf.Rad2Deg;
        ang = Mathf.Clamp(Mathf.DeltaAngle(0f, ang), -6f, 6f);
        float span = (new Vector2(s1.x, s1.y) - new Vector2(s0.x, s0.y)).magnitude / Mathf.Max(0.01f, root.lossyScale.x);
        float sc = Mathf.Clamp(span / 360f, 1f, 1.12f);   // never below 1×: every hit stays ≥ 28 px
        ribbon.localEulerAngles = new Vector3(0f, 0f, ang);
        ribbon.localScale = new Vector3(sc, sc, 1f);
        ribbon.anchoredPosition = KeepClear(lp + (Vector2)(Quaternion.Euler(0f, 0f, ang) * new Vector2(0f, 23f * sc)), sc, ang);   // the band's bottom rests on the edge
        // the + ghosts: the footprint a new island would take above (behind) and below (in front of) this one in its column
        if (!kb.IsMoon && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase) PlaceGhosts(cam, kb, y, kb.VisualOffset);
        else { aboveOk = belowOk = false; }
    }

    // the fixed HUD around the screen's edges (instrument column left, deck bottom centre, transport + its punch row bottom left, logo + rail +
    // present top): the ribbon and its more card stay clear of it (the header draws under the fixed HUD, so a ghost passing beneath never
    // steals its clicks). HUD units (1920×1080 reference).
    public const float ClearLeft = 100f, ClearRight = 16f, ClearBottom = 84f, ClearTop = 92f;
    /// <summary>The transport's corner: its right end, and the clearance above it (the punch row tops out at 176 while playing).</summary>
    public const float TransportRight = 400f, ClearTransport = 188f;
    /// <summary>The paper band's width (v5: 336, room for the repeat button; v6: 380, room for carry; v7: 492, room for flow and launch — and the
    /// stairs / phrase bands) and the more card's x under it (its right edge under the ⋯).</summary>
    public const float BandW = 492f, MoreX = 99f;
    const float HalfW = BandW * 0.5f + 28f, HalfH = 27f, MoreDrop = 181f, MoreHalfW = 132f;
    bool moreUp, moreSmall;

    /// <summary>v6 (integration): a Moon's ribbon holds only the grip, the dice and ⋯ — a short band with them side by side (on the islands' wide
    /// band they stood far apart on an empty strip); islands and keyboards keep the full band.</summary>
    void LayoutBand(int kind)
    {
        bool moon = kind == 1;
        float w = moon ? MoonBandW : BandW;
        band.sizeDelta = new Vector2(w, 46f); ribbon.sizeDelta = new Vector2(w + 8f, 60f);
        tailL.rectTransform.anchoredPosition = new Vector2(-w * 0.5f - 4f, -9f); tailR.rectTransform.anchoredPosition = new Vector2(w * 0.5f + 4f, -9f);
        Pos(gripBtn, moon ? -50f : -226f);
        Pos(diceBtn, moon ? 0f : -60f);
        Pos(moreBtn, moon ? 50f : 214f);
        if (moon) return;
        Pos(eyeBtn, -188f);
        // register ▼ ladder ▲ and repeat: where each kind has room for them
        float reg = kind == 3 ? 112f : (kind == 4 ? 102f : -134f), rep = kind == 3 ? 172f : (kind == 4 ? 166f : -72f);
        Pos(regDownBtn, reg - 22f); Pos(regUpBtn, reg + 22f); regNode.anchoredPosition = new Vector2(reg, 0f);
        var lad = band.Find("Ladder") as RectTransform; if (lad != null) lad.anchoredPosition = new Vector2(reg, 0f);
        Pos(repeatBtn, rep);
        // chord islands / keyboards: carry, flow, launch, the duplicates (v7 positions on the 492 band)
        Pos(carryBtn, -22f); Pos(launchBtn, 32f); Pos(dupRightBtn, 90f); Pos(dupBelowBtn, 132f); Pos(spotBtn, 173f); dupNode.anchoredPosition = new Vector2(111f, 0f);   // (§21: one extend control)
        // the stickers standing over the repeat button; the paste tab clear of them
        styleBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(rep - 16f, 48f);
        varyBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(rep + 16f, 48f);
        if (pasteBtn != null) pasteBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(kind == 4 ? 40f : w * 0.5f - 64f, 47f);
        if (copyPathsBtn != null) copyPathsBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(kind == 4 ? 92f : 74f, 47f);
        if (pastePathsBtn != null) pastePathsBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(kind == 4 ? 144f : 126f, 47f);
    }

    static void Pos(HudButton b, float x) { if (b != null) b.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, 0f); }
    /// <summary>v6: the width of a Moon's ribbon band (islands: <see cref="BandW"/>).</summary>
    public const float MoonBandW = 170f;
    float moreSmallX = -1f;

    Vector2 KeepClear(Vector2 c, float sc, float ang)
    {
        Rect r = root.rect;
        float sn = Mathf.Abs(Mathf.Sin(ang * Mathf.Deg2Rad)), cs = Mathf.Cos(ang * Mathf.Deg2Rad);   // the tilt lowers one end
        float halfW = HalfW + (wasKeyboard ? 26f : 0f);   // §20.1: a keyboard's range stickers stand past the ribbon's ends
        float hw = (halfW * cs + HalfH * sn) * sc, hh = (HalfH * cs + halfW * sn) * sc, drop = (MoreDrop * cs + MoreHalfW * sn) * sc;
        float x0 = r.xMin + ClearLeft + hw, x1 = r.xMax - ClearRight - hw;
        if (x0 <= x1) c.x = Mathf.Clamp(c.x, x0, x1);
        // over the transport's corner the floor rises (eased over 80 units, so sliding past its end never jumps)
        float bottom = Mathf.Lerp(ClearBottom, ClearTransport, Mathf.Clamp01((TransportRight + 40f - (c.x - hw - r.xMin)) / 80f));
        float y0 = r.yMin + bottom + hh, y1 = r.yMax - ClearTop - hh;
        if (y0 <= y1) c.y = Mathf.Clamp(c.y, y0, y1);
        // the more card hangs under the band; without room there it opens above
        bool up = c.y - drop < r.yMin + bottom && c.y + drop <= r.yMax - ClearTop;
        bool small = wasMoon || wasKeyboard || shownKind == 3 || shownKind == 4;   // v6: a Moon's / keyboard's more card holds only the delete: small, under the ⋯
        float smallX = wasMoon ? 28f : (shownKind == 3 ? 156f : (shownKind == 4 ? 175f : 188f));   // (v7: stairs / phrases add their duplicates)
        if (up != moreUp || small != moreSmall || smallX != moreSmallX) { moreUp = up; moreSmall = small; moreSmallX = smallX; more.anchoredPosition = small ? new Vector2(smallX, up ? 76f : -76f) : new Vector2(MoreX, up ? 119f : -119f); }
        return c;
    }

    void PlaceGhosts(Camera cam, KeyBlock kb, float y, Vector3 shown)
    {
        var sm = SongManager.I;
        float gap = ProjectConfig.LaneGap, d = kb.Depth;
        float x0 = kb.WestEdge + shown.x, x1 = kb.EastEdge + shown.x;   // v5: the ghosts ride along with the island (its belt / slide)
        // free z span beyond each edge (a neighbour of the column shrinks the ghost to the gap)
        float aboveMin = kb.BackEdge + gap, aboveMax = aboveMin + d, belowMax = kb.FrontEdge - gap, belowMin = belowMax - d;
        foreach (var o in sm.Islands)
        {
            if (o == null || o == kb || o.column != kb.column) continue;
            if (o.FrontEdge >= kb.BackEdge - 0.01f) aboveMax = Mathf.Min(aboveMax, Mathf.Max(aboveMin + 0.9f, o.FrontEdge - 0.25f));
            if (o.BackEdge <= kb.FrontEdge + 0.01f) belowMin = Mathf.Max(belowMin, Mathf.Min(belowMax - 0.9f, o.BackEdge + 0.25f));
        }
        bool okA = Project(cam, x0, x1, aboveMin + shown.z, aboveMax + shown.z, y, polyAbove);
        bool okB = Project(cam, x0, x1, belowMin + shown.z, belowMax + shown.z, y, polyBelow);
        bool changed = okA != aboveOk || okB != belowOk || !hasPoly;
        aboveOk = okA; belowOk = okB; hasPoly = true;
        if (changed || moved) { ghostAbove.Repaint(); ghostBelow.Repaint(); moved = false; }
        int slotsN = GhostSlots();   // v8: a multi-measure grid's ghosts draw a + per measure themselves, except slot 0's: that stays the real button
        HudKit.SetActive(plusAbove, okA); HudKit.SetActive(plusBelow, okB);
        // the side +s: on the middle of the west / east edges (a copy before / after it)
        float zm = (kb.FrontEdge + kb.BackEdge) * 0.5f + shown.z;
        Vector2 lp, rp;
        const float out_ = 0.55f;   // just past the edge, clear of the grid's own tiles and cubes
        bool okL = EdgePoint(cam, new Vector3(x0 - out_, y, zm), out lp), okR = EdgePoint(cam, new Vector3(x1 + out_, y, zm), out rp);
        HudKit.SetActive(plusLeft, okL); HudKit.SetActive(plusRight, okR);
        if (okL) plusLeft.anchoredPosition = lp;
        if (okR) plusRight.anchoredPosition = rp;
        if (okA) plusAbove.anchoredPosition = slotsN < 2 ? Centroid(polyAbove) : SlotCentre(polyAbove, 0, slotsN);
        if (okB) plusBelow.anchoredPosition = slotsN < 2 ? Centroid(polyBelow) : SlotCentre(polyBelow, 0, slotsN);
    }
    /// <summary>v9: the centre of measure slot <paramref name="k"/> of a ghost footprint drawn in <paramref name="slots"/> slots (as PaintGhost lays them).</summary>
    static Vector2 SlotCentre(Vector2[] poly, int k, int slots)
    {
        float f = (k + 0.5f) / slots;
        return Vector2.Lerp(Vector2.Lerp(poly[0], poly[1], f), Vector2.Lerp(poly[3], poly[2], f), 0.5f);
    }
    bool moved;

    bool EdgePoint(Camera cam, Vector3 world, out Vector2 local)
    {
        local = Vector2.zero;
        Vector3 s = cam.WorldToScreenPoint(world);
        if (!Sane(s, 4f * Mathf.Max(Screen.width, Screen.height))) return false;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(root, s, null, out local);
    }

    bool Project(Camera cam, float x0, float x1, float z0, float z1, float y, Vector2[] into)
    {
        Vector3 a = cam.WorldToScreenPoint(new Vector3(x0, y, z0)), b = cam.WorldToScreenPoint(new Vector3(x1, y, z0));
        Vector3 c = cam.WorldToScreenPoint(new Vector3(x1, y, z1)), d = cam.WorldToScreenPoint(new Vector3(x0, y, z1));
        // v7 (integration): a corner just in front of the camera projects thousands of screens away and the dotted outline then ran past the UI
        // mesh's 65000 vertices — such a ghost (the camera almost on it) is not shown
        float lim = 4f * Mathf.Max(Screen.width, Screen.height);
        if (!Sane(a, lim) || !Sane(b, lim) || !Sane(c, lim) || !Sane(d, lim)) return false;
        Vector2 la, lb, lc, ld;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, a, null, out la);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, b, null, out lb);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, c, null, out lc);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, d, null, out ld);
        if ((into[0] - la).sqrMagnitude > 0.25f || (into[2] - lc).sqrMagnitude > 0.25f) moved = true;
        into[0] = la; into[1] = lb; into[2] = lc; into[3] = ld;
        return true;
    }

    static Vector2 Centroid(Vector2[] p) => (p[0] + p[1] + p[2] + p[3]) * 0.25f;
    static bool Sane(Vector3 sp, float lim) => sp.z > 0.3f && Mathf.Abs(sp.x) < lim && Mathf.Abs(sp.y) < lim;
}

/// <summary>The header's grip: a press arms the island drag (IslandDrag decides click vs drag, as a margin press does); hovering it shows
/// the MoveV cursor.</summary>
public class IslandHeaderGrip : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    public IslandHeader header;
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && header != null) header.GripDown(e); }
    public void OnPointerUp(PointerEventData e) { if (header != null) header.GripUp(); }
    public void OnPointerEnter(PointerEventData e) { CursorKit.Want(CursorKit.Kind.MoveV, this); }
    public void OnPointerExit(PointerEventData e) { CursorKit.Release(this); }
    void OnDisable() { CursorKit.Release(this); }
}
