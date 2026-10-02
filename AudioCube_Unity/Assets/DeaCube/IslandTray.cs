using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "The deck" (SPEC v4 §5, research §4.2; v3 SPEC §3.1 behaviour kept): the island cards. At rest a stack of three card backs peeks 26 px
/// above the bottom edge, fanned ±4° (hover: it rises 20 px); a click or I opens it. Open, the cards rise into a fanned hand along an arc
/// (radius 1400 px, −9°…+9°) over a soft violet vignette on the bottom 30 % of the screen — no shelf. Next (the generator's remaining
/// progression first, then suggestions): hero cards 116 × 150; deck order by position only — the next card front-left, 8 % larger, raised
/// 10 px with a ▸ sticker, each later one a little lower. Key (7) and Colour (3): two small stacks either side whose thickness is their count;
/// hovering a stack fans it out above the hand (stepped). Tools at the right end, as stickers: dice, duplicate (two overlapping cards), wand
/// (the whole remaining deck) and the Moon card (a drum Moon; replaces v3's "kit"). v5 (SPEC §7): a card IS its vibe — the vibe colour as its
/// paper, the vibe's weather picture big in the middle (sun, rainbow, rain, moon, storm, chili, balloon), the island small in ¾ view below, a
/// house sticker on the key's home chord, the caption = the vibe word and its feeling ("stormy · wants to go home"); wobbly ink border, hard
/// shadow. Hover: it lifts 14 px, tilts toward the pointer
/// on twos and auditions its chord (stopped only: harmonic safety). Click: placed after the focused island; drag: the IslandGhost in the
/// world (IslandGhost.Snapshot / Place decide the column slot — package K), the fan closes the gap on twos and the column rail shows the
/// drop slot. Every placement is one History.Push; used deck cards leave the deck (undo brings them back).
/// Contract kept: IsOpen, Toggle, Open, Close, SetDeck, DeckCount, Deck, DeckStates, Cards / CountIn (Next 4, Key 7, Colour 3 in that
/// order), the Sim* test hooks, AuditionCount / QuietHoverCount, hints tray / tray.next0 / tray.wand / tray.button (+ addIsland → the stack).
/// v3 fixes kept: never opens while the inspector, the presentation or the menu own the screen (F19 / S8); Esc closes it unless something
/// nearer the pointer took the Esc (F18).
/// v6 (SPEC v6 §7.1 / §7.2 — the user: "some ui showing which grid cards fit given the patterns that they have. so not just the vibe but also
/// like a star as well as a number (like 1 2 3 4 in the chord progression … 1234 can also work as 1432 so they should know it can be swapped)"):
/// every chord card wears its JOB badge (Harmony.Job: 1 house, 2 flag, 3 heart, 4 curling arrow — JobBadge; the job-1 house replaces v5's home
/// sticker in the same corner) and a gold STAR on its top edge when its chord fits the reference pattern (Harmony.Fit ≥ StarThreshold): the
/// pattern on the clipboard / in the hand, else the notes of the focused island (the header's island, the selected cube's, the selected one,
/// the lit one while playing, the last one), else — no notes yet — the two best next chords after the focused chord (MusicTheory.SuggestNext).
/// The best card's star twinkles. Stars follow the reference (Clipboard.OnChanged, History.OnChanged, PathManager.OnHandChanged, the focus).
/// Hover caption: "2 · away · swaps with any 2 · rainy"; while a card is hovered every island of the same job wears a small ⇄ swap chip over it
/// in the world and its rail bead pulses (HudColumnRail.SwapJob). A fifth tool, the KEYBOARD card (a little piano): a click adds a keyboard
/// island to the focused island's column (SongManager.AddKeyboardIsland; a full column → a new last column), a drag places it through the ghost.
/// v6 §11 (Moons follow the song): the Moon card adds a drum Moon whose section STARTS at the focused column (the lit column while playing,
/// else the selected / focused island's, else 0: SongManager.AddMoon(column)); dragged, it follows the pointer, the column it is in line with
/// lights (ColumnBands) and the rail shows a dotted drum there; the drop starts the Moon at that column.
/// v7 (SPEC v7 §7.1 / §14.6): two more cards in the tools row — STAIRS (a little staircase with its runner cube: "some stair-case grid that just
/// is descending notes") and MELODY (a little roll: "a 2 measure melody shouldnt be two separate grids"). A click adds a stairs island to the
/// focused island's column (SongManager.AddStairIsland with the hand's group, else the lead; a full column → a new last column) / a melody
/// phrase at the focused column (SongOps.AddPhrase: the hand picks the cube up, ready to draw). Dragged like the keyboard card: the stairs
/// card through the ghost (a STACK slot → that column, above / below the nearest island; the end → a new column), the melody card like the
/// Moon card (the column it is in line with lights, the rail shows a dotted phrase there; the drop adds it at that column).
/// </summary>
public class IslandTray : MonoBehaviour
{
    public static IslandTray I;
    static SongManager.MeasureData[] deck = new SongManager.MeasureData[0];

    // ------------------------------------------------------------------ M0 contract
    public static bool IsOpen => I != null && I.open;
    public static int DeckCount => deck.Length;
    /// <summary>The generator's remaining progression (in order); null = none. Called by StartFreshSong and by every rebuild from a snapshot.</summary>
    public static void SetDeck(SongManager.MeasureData[] d) { deck = CopyAll(d); if (I != null) I.dirty = true; }
    public static void Open() { var t = Ensure(); if (t != null) t.SetOpen(true); }
    public static void Close() { if (I != null) I.SetOpen(false); }
    public static void Toggle() { if (IsOpen) Close(); else Open(); }

    // ------------------------------------------------------------------ v3 additions
    /// <summary>A copy of the deck.</summary>
    public static SongManager.MeasureData[] Deck => CopyAll(deck);
    /// <summary>The deck as snapshot entries (SongState.Capture).</summary>
    public static MeasureState[] DeckStates()
    {
        var r = new MeasureState[deck.Length];
        for (int i = 0; i < deck.Length; i++) r[i] = MeasureState.From(deck[i]);
        return r;
    }

    /// <summary>The deck under the HUD (created on first use; the HUD canvas must exist).</summary>
    public static IslandTray Ensure()
    {
        if (I != null) return I;
        var ui = UIManager.I;
        if (ui == null || ui.Canvas == null) return null;
        Transform parent = ui.Canvas.transform.Find("HUD");
        if (parent == null) parent = ui.Canvas.transform;
        RegisterGlyphs();
        var go = UIKit.Obj("IslandTray", parent);
        UIKit.Stretch(UIKit.RT(go));
        go.transform.SetAsFirstSibling();   // behind the rest of the HUD
        return go.AddComponent<IslandTray>();
    }

    public const float CardW = 116f, CardH = 150f, SmallW = 72f, SmallH = 92f, ArcR = 1400f;
    /// <summary>v3 names kept for callers: the hand's height and max width.</summary>
    public const float ShelfH = CardH, MaxShelfW = 1100f;
    const int NextCount = 4, DeckShown = 3;

    /// <summary>Cards in section order: Next, Key, Colour (tests).</summary>
    public IReadOnlyList<TrayCard> Cards => cards;
    public int CountIn(int section) { int n = 0; foreach (var c in cards) if (c != null && c.section == section) n++; return n; }
    /// <summary>True while a card is being dragged (its ghost is in the world).</summary>
    public bool Dragging => ghost != null;
    public IslandGhost Ghost => ghost;
    /// <summary>The open hand's root (v3 name).</summary>
    public RectTransform Shelf => hand;
    /// <summary>The resting stack of card backs (bottom centre).</summary>
    public RectTransform Stack => stack;
    public float ShownAmount => shown;
    /// <summary>A stack's fan-out progress (1 Key, 2 Colour) — tests.</summary>
    public float FanOf(int section) => section == 1 ? fanKey : (section == 2 ? fanColour : 0f);
    public TrayTool Tool(int i) => i == 0 ? diceBtn : (i == 1 ? dupBtn : (i == 2 ? wandBtn : (i == 3 ? moonBtn : (i == 4 ? kbBtn : (i == 5 ? stairsBtn : melodyBtn)))));
    /// <summary>v7: the stairs card (Tool(5)) and the melody card (Tool(6)).</summary>
    public TrayTool StairsCard => stairsBtn;
    public TrayTool MelodyCard => melodyBtn;
    /// <summary>v7: stairs islands / melody phrases added by the cards (tests), and the column a dragged melody card points at (-1 none).</summary>
    public int StairsAdded { get; private set; }
    public int PhrasesAdded { get; private set; }
    public int MelodyDropColumn => melodyDrag ? melodyDropCol : -1;

    // ------------------------------------------------------------------ v6: stars, swap chips (SPEC v6 §7.1)
    /// <summary>What the cards' stars measure: the clipboard / hand pattern, the focused island's notes, the next-chord ranking (no notes), none.</summary>
    public enum StarSource { None, Pattern, Island, Next }
    public StarSource StarRef { get; private set; }
    /// <summary>The island whose notes (or chord, for the next-chord ranking) the stars follow (null for the pattern).</summary>
    public KeyBlock StarIsland { get; private set; }
    /// <summary>Notes in the reference (0 for the next-chord ranking).</summary>
    public int StarNotes { get; private set; }
    /// <summary>Starred cards now, and the one whose star twinkles (null = none).</summary>
    public int StarCount { get; private set; }
    public TrayCard BestCard { get; private set; }
    /// <summary>Times the stars were recomputed (tests: a reference change refreshes them).</summary>
    public int StarRefreshes { get; private set; }
    /// <summary>The job whose ⇄ chips are up (0 = none) and how many chips are on screen (tests).</summary>
    public int SwapJob { get; private set; }
    public int SwapChipsShown { get; private set; }
    /// <summary>The islands the chips stand over (tests).</summary>
    public IReadOnlyList<KeyBlock> SwapIslands => swapIslands;
    /// <summary>A card's hover caption (v6): its job number and word, what it swaps with, its vibe — "2 · away · swaps with any 2 · rainy".</summary>
    public static string JobCaption(SongManager.MeasureData md)
    {
        if (md == null) return "";
        int job = Harmony.Job(md);
        var v = Vibe.Of(CardSemis(md));
        if (!JobBadge.Valid(job)) return Vibe.Word(v);
        return job + " · " + Harmony.JobWord(job) + " · swaps with any " + job + " · " + Vibe.Word(v);
    }
    /// <summary>The chord a card shows (its stored tones under the song's climate, the root first).</summary>
    public static List<int> CardSemis(SongManager.MeasureData md)
    {
        int[] stored = md.semitones != null && md.semitones.Length > 0 ? md.semitones : new[] { 0, 4, 7 };
        var semis = new List<int>(MusicTheory.EffectiveSemis(stored, 0, SongManager.Climate));
        if (!semis.Contains(0)) semis.Insert(0, 0);
        semis.Sort();
        return semis;
    }

    RectTransform hand, stack, vignette, keyStackRt, colourStackRt, stackHit;
    CanvasGroup handGroup, stackGroup, vigGroup; Canvas canvas;
    readonly List<TrayCard> cards = new List<TrayCard>();
    readonly List<RectTransform> backs = new List<RectTransform>();
    TrayTool diceBtn, dupBtn, wandBtn, moonBtn, kbBtn, stairsBtn, melodyBtn;
    bool stairsDrag, melodyDrag; int melodyDropCol = -1;
    // v6: the stars' reference, the ⇄ chips, the keyboard card's drag
    static readonly List<int> refMidi = new List<int>(128); static readonly List<float> refW = new List<float>(128);
    bool starsDirty = true, subsV6; int starKey = int.MinValue; float lastHeaderT = -99f;
    RectTransform chipsRt; readonly List<SwapChip> chips = new List<SwapChip>(); readonly List<KeyBlock> swapIslands = new List<KeyBlock>();
    TrayCard swapCard, forcedSwap; bool toolDrag, moonDrag; int moonDropCol = -1;
    HudButton stackBtn;
    bool open, dirty = true, subscribed; float shown, stackLift, fanKey, fanColour, keyHoverT, colourHoverT, hiddenA = 1f;
    int tick = int.MinValue;
    TrayCard dragCard; IslandGhost ghost; RectTransform floatCard; bool simDrag;
    Coroutine audition; float auditionHold;
    bool escTakenLast;   // F18: last frame something else owned Esc (a draft, an island drag, a ring or the chord wheel)

    static SongManager.MeasureData[] CopyAll(SongManager.MeasureData[] d)
    {
        if (d == null) return new SongManager.MeasureData[0];
        var list = new List<SongManager.MeasureData>(d.Length);
        foreach (var m in d) if (m != null) list.Add(Copy(m));
        return list.ToArray();
    }

    static SongManager.MeasureData Copy(SongManager.MeasureData m) => new SongManager.MeasureData
    {
        index = m.index, chordKey = m.chordKey, chordRootMIDI = m.chordRootMIDI, semitones = m.semitones != null ? (int[])m.semitones.Clone() : new[] { 0, 4, 7 },
        measureDuration = m.measureDuration, bars = m.bars <= 0 ? 1 : m.bars, px = m.px, pz = m.pz, placed = false, kind = 0, mood = m.mood,
        energy = m.energy, fill = m.fill, sleep = m.sleep, repeat = m.repeat <= 0 ? 1 : m.repeat, group = 0, fall = m.fall, col = -1, reg = m.reg
    };

    static bool glyphs;
    /// <summary>The wand glyph (IconFactory is frozen: registered from here).</summary>
    static void RegisterGlyphs()
    {
        if (glyphs) return;
        glyphs = true;
        if (!IconFactory.Has("wand"))
            IconFactory.Register("wand", p => IconFactory.P.U(
                IconFactory.P.Seg(p, -0.7f, -0.7f, 0.1f, 0.1f, 0.1f),
                IconFactory.P.Star(new Vector2(p.x - 0.36f, p.y - 0.36f), 4, 0.46f, 0.13f),
                IconFactory.P.Circle(p, -0.18f, 0.66f, 0.08f),
                IconFactory.P.Circle(p, 0.7f, -0.2f, 0.07f)));
    }

    // ------------------------------------------------------------------ lifecycle
    void Awake()
    {
        I = this;
        canvas = GetComponentInParent<Canvas>();
        BuildShell();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (ghost != null) CancelDrag();
        if (SongManager.I != null && subscribed) SongManager.I.OnSongRebuilt -= MarkDirty;
        if (subsV6) { Clipboard.OnChanged -= MarkStars; History.OnChanged -= MarkStars; PathManager.OnHandChanged -= MarkStars; }
        if (UIManager.I != null && UIManager.I.Rail != null) UIManager.I.Rail.SwapJob = 0;
    }

    void MarkDirty() { dirty = true; }
    void MarkStars() { starsDirty = true; }

    static readonly Color BackFill = new Color(1f, 0.953f, 0.902f), BackLine = new Color(0.74f, 0.64f, 1f);

    void BuildShell()
    {
        // the bottom vignette (only while open) — a vertical violet gradient, no panel
        vignette = HudKit.Node(transform, "Vignette", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 0f));
        vignette.anchorMin = new Vector2(0f, 0f); vignette.anchorMax = new Vector2(1f, 0.3f); vignette.offsetMin = Vector2.zero; vignette.offsetMax = Vector2.zero;
        var vimg = vignette.gameObject.AddComponent<RawImage>(); vimg.texture = Gradient(); vimg.color = new Color(0.16f, 0.09f, 0.30f, 0.62f); vimg.raycastTarget = false;
        vigGroup = vignette.gameObject.AddComponent<CanvasGroup>(); vigGroup.alpha = 0f; vigGroup.blocksRaycasts = false;
        vignette.gameObject.SetActive(false);
        // v6: the ⇄ swap chips over the islands of a hovered card's job (world-anchored; under the hand)
        chipsRt = HudKit.Stretch(transform, "SwapChips");

        // the resting stack: three card backs peeking 26 px above the bottom edge
        stack = HudKit.Node(transform, "Deck", new Vector2(0.5f, 0f), new Vector2(0f, 26f - CardH * 0.5f), new Vector2(CardW + 30f, CardH));
        stackGroup = stack.gameObject.AddComponent<CanvasGroup>();
        for (int k = 0; k < 3; k++)
        {
            var b = HudKit.Node(stack, "Back" + k, new Vector2(0.5f, 0.5f), new Vector2((k - 1) * 7f, -k * 2f), new Vector2(CardW, CardH));
            b.localEulerAngles = new Vector3(0f, 0f, (k - 1) * -4f);
            var s = b.gameObject.AddComponent<InkShape>();
            s.Shape = InkShape.Kind.RoundRect; s.color = BackFill; s.Radius = 12f; s.Wobble = 1.2f; s.SetInk(2.2f, 3.4f); s.ShadowOffset = new Vector2(4f, -5f); s.raycastTarget = false;
            var art = HudKit.Node(b, "Pattern", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH)).gameObject.AddComponent<InkPainter>();
            art.raycastTarget = false; art.onPaint = PaintBack;
            backs.Add(b);
        }
        // the hit area: the peeking strip, fully on screen (its bottom on the screen's bottom edge at rest)
        var hit = HudKit.Hit(stack, "StackHit", new Vector2(0.5f, 0.5f), new Vector2(0f, CardH * 0.5f - 26f + 25f), new Vector2(CardW + 40f, 50f));
        stackHit = hit.rectTransform;
        stackBtn = HudKit.Control(hit, "islands", () => Toggle());
        stackBtn.gameObject.name = "DeckStack";
        Hints.Register("tray.button", stackHit); Hints.Register("addIsland", stackHit);

        // the open hand (cards are built on open)
        hand = HudKit.Node(transform, "Hand", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1200f, 420f));
        hand.pivot = new Vector2(0.5f, 0f);
        handGroup = hand.gameObject.AddComponent<CanvasGroup>(); handGroup.alpha = 0f; handGroup.interactable = false; handGroup.blocksRaycasts = false;
        keyStackRt = HudKit.Node(hand, "KeyStack", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SmallW + 24f, SmallH + 30f));
        colourStackRt = HudKit.Node(hand, "ColourStack", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(SmallW + 24f, SmallH + 30f));
        // (the stacks' areas are hit-tested geometrically: a raycast rect over them would eat the fanned cards' clicks)
        BuildTools();
        hand.gameObject.SetActive(false);
        Hints.Register("tray", hand);
    }

    static Texture2D gradTex;
    static Texture2D Gradient()
    {
        if (gradTex != null) return gradTex;
        gradTex = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "deckVignette" };
        var px = new Color32[2 * 64];
        for (int y = 0; y < 64; y++) { float a = Mathf.Pow(1f - y / 63f, 1.6f); byte b = (byte)Mathf.RoundToInt(a * 255f); px[y * 2] = px[y * 2 + 1] = new Color32(255, 255, 255, b); }
        gradTex.SetPixels32(px); gradTex.Apply(false, false);
        return gradTex;
    }

    static readonly List<Vector2> q = new List<Vector2>(64);

    static void PaintBack(InkPainter p)
    {
        Rect r = p.Area;
        // a dotted cream inner border and a lattice of tiny cubes (the motif), like a playing-card back
        p.RoundRect(new Rect(r.xMin + 7f, r.yMin + 7f, r.width - 14f, r.height - 14f), 8f, new Color(0f, 0f, 0f, 0f), 1.8f, Comic.A(Comic.Ink, 0.55f), 3f, 3f);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 4; x++)
            {
                Vector2 c = new Vector2(r.xMin + 22f + x * 24f + (y % 2) * 12f, r.yMax - 22f - y * 22f);
                if (c.x > r.xMax - 14f) continue;
                p.Cube(c, 10f, BackLine, Comic.A(Comic.Ink, 0.85f), 1f);
            }
    }

    void BuildTools()
    {
        diceBtn = MakeTool("TrayDice", "roll one", PaintDiceTool, RollDice);
        dupBtn = MakeTool("TrayDuplicate", "copy", PaintDupTool, DuplicateFocused);
        wandBtn = MakeTool("TrayWand", "place all", PaintWandTool, PlaceWholeDeck);
        moonBtn = MakeTool("TrayMoon", "drum moon", PaintMoonTool, AddMoonCard);
        moonBtn.onBeginDrag = BeginMoonDrag; moonBtn.onEndDrag = EndMoonDrag;
        kbBtn = MakeTool("TrayKeyboard", "keyboard", PaintKeyboardTool, AddKeyboardCard);
        kbBtn.onBeginDrag = BeginKeyboardDrag; kbBtn.onEndDrag = EndKeyboardDrag;
        // v7: the stairs card and the melody card
        stairsBtn = MakeTool("TrayStairs", "stairs", PaintStairsTool, AddStairsCard);
        stairsBtn.onBeginDrag = BeginStairsDrag; stairsBtn.onEndDrag = EndStairsDrag;
        melodyBtn = MakeTool("TrayMelody", "melody", PaintMelodyTool, AddMelodyCard);
        melodyBtn.onBeginDrag = BeginMelodyDrag; melodyBtn.onEndDrag = EndMelodyDrag;
        Hints.Register("tray.stairs", stairsBtn.transform as RectTransform);
        Hints.Register("tray.melody", melodyBtn.transform as RectTransform);
        // v8: the melody card is retired ("way too complex" — the keyboard is the melody tool: it grows as you draw and the cubes carry the time);
        // saved phrases still load and play, and SimTool(6) still adds one for the v7 suites
        melodyBtn.gameObject.SetActive(false);
        Hints.Register("tray.wand", wandBtn.transform as RectTransform);
        Hints.Register("addMoon", moonBtn.transform as RectTransform);
        Hints.Register("tray.keyboard", kbBtn.transform as RectTransform);
    }

    TrayTool MakeTool(string name, string caption, Action<InkPainter> paint, Action onClick)
    {
        var rt = HudKit.Node(hand, name, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(52f, 52f));
        var s = rt.gameObject.AddComponent<InkShape>();
        s.Shape = InkShape.Kind.Sticker; s.color = Comic.Cream; s.SetInk(1.8f, 2.8f); s.ShadowOffset = new Vector2(3f, -4f); s.raycastTarget = true;
        var art = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f)).gameObject.AddComponent<InkPainter>();
        art.raycastTarget = false; art.onPaint = paint;
        var t = rt.gameObject.AddComponent<TrayTool>();
        t.body = s; t.art = art; t.onClick = onClick;
        InkCaption.Attach(rt.gameObject, caption);
        return t;
    }

    // ------------------------------------------------------------------ per frame
    /// <summary>v5: the card pictures (inked vibe silhouettes + their accent layers) are rasterised one per frame while the deck rests, so the
    /// first open does not hitch on ~15 glyph builds.</summary>
    static int warm;
    static void WarmGlyphs()
    {
        int k = warm;
        for (int v = 0; v < Vibe.Count; v++)
            for (int l = 0; l < VibeGlyphs.LayerCount((VibeKind)v); l++)
            {
                if (k-- > 0) continue;
                VibeGlyphs.Ensure();
                Comic.Glyph(VibeGlyphs.LayerGlyph((VibeKind)v, l), l == 0 ? 62f : 40f, l == 0);
                warm++;
                return;
            }
        warm = int.MaxValue;
    }

    void Update()
    {
        CardPreview.Tick();   // v7: the song starting cuts a card's pattern preview
        if (stackBtn == null || hand == null) return;   // not built (or a domain reload dropped the references)
        if (!open && warm != int.MaxValue && SongManager.I != null && SongManager.I.HasSong) WarmGlyphs();
        float dt = Time.unscaledDeltaTime;
        if (!subscribed && SongManager.I != null) { SongManager.I.OnSongRebuilt += MarkDirty; subscribed = true; }
        if (!subsV6) { Clipboard.OnChanged += MarkStars; History.OnChanged += MarkStars; PathManager.OnHandChanged += MarkStars; subsV6 = true; }
        TrackHeader();
        bool keys = !WorldInput.KeysLocked && !InputUtil.TypingInField && !InputUtil.Cmd;
        if (keys && !CubeInspector.IsOpen && KeyShim.Down(KeyCode.I)) Toggle();   // F19: not while inspecting
        // F18: Esc closes the deck (a card being dragged takes its own Esc: it flies back); the owner of last frame's Esc goes first
        if (open && ghost == null && !escTakenLast && !WorldInput.KeysLocked && !InputUtil.TypingInField && KeyShim.Down(KeyCode.Escape)) SetOpen(false);
        if (open && (Presenter.Active || MainMenu.IsShown || CubeInspector.IsOpen || SongManager.I == null || !SongManager.I.HasSong)) SetOpen(false);
        if (open && dirty && ghost == null) Rebuild();
        if (open && !dirty && (starsDirty || (HudKit.TwosTick != tick && StarKeyNow() != starKey))) RefreshStars();   // (the focus is polled on twos)
        if (ghost != null && !simDrag) UpdateDrag();
        if (moonDrag && !simDrag) UpdateMoonDrag(Input.mousePosition);
        if (melodyDrag && !simDrag) UpdateMelodyDrag(Input.mousePosition);

        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float step = 1f / Look.TwosFps;
        // the resting stack: hidden while inspecting / while the hand is up / without a song; rises when hovered
        bool hasSong = SongManager.I != null && SongManager.I.HasSong;
        float wantHidden = (!hasSong || CubeInspector.IsOpen) ? 0f : 1f;
        hiddenA = Mathf.MoveTowards(hiddenA, wantHidden, step / 0.18f);
        float lift = stackBtn.Hover && !open ? 20f : 0f;
        stackLift = Mathf.MoveTowards(stackLift, lift, step * 20f / 0.12f);
        float drop = Ease.OutCubic(shown) * 70f;   // the backs sink while the hand is up
        stack.anchoredPosition = new Vector2(0f, 26f - CardH * 0.5f + stackLift - drop);
        stackGroup.alpha = hiddenA * (1f - 0.6f * shown);
        stackGroup.blocksRaycasts = wantHidden > 0.5f;
        // the hand
        shown = Mathf.MoveTowards(shown, open ? 1f : 0f, step / 0.24f);
        if (!open && shown <= 0f) { if (hand.gameObject.activeSelf) { hand.gameObject.SetActive(false); vignette.gameObject.SetActive(false); } return; }
        handGroup.alpha = Mathf.Clamp01(shown * 1.6f);
        vigGroup.alpha = Ease.OutCubic(shown);
        bool on = open && shown > 0.6f;
        handGroup.interactable = on; handGroup.blocksRaycasts = on;
        keyHoverT = Hovering(1) || forcedFan == 1 ? 0f : keyHoverT + step; colourHoverT = Hovering(2) || forcedFan == 2 ? 0f : colourHoverT + step;
        fanKey = Mathf.MoveTowards(fanKey, keyHoverT < 0.2f && on ? 1f : 0f, step / 0.16f);
        fanColour = Mathf.MoveTowards(fanColour, colourHoverT < 0.2f && on ? 1f : 0f, step / 0.16f);
        LayoutHand();
    }

    void LateUpdate()
    {
        var pm = PathManager.I; var ui = UIManager.I;
        escTakenLast = (pm != null && (pm.IsDrawing || (pm.Drag != null && pm.Drag.Busy))) || (ui != null && (ui.WheelOpen || ui.OpenRing != null)) || PathGridView.AnyPopoverOpen;
        UpdateSwapChips();
    }

    bool Hovering(int section)
    {
        var rt = section == 1 ? keyStackRt : colourStackRt;
        if (rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null)) return true;
        foreach (var c in cards) if (c != null && c.section == section && (c.Hovered || c.IsDragged)) return true;
        return false;
    }

    void SetOpen(bool on)
    {
        if (open == on) return;
        if (on && (SongManager.I == null || !SongManager.I.HasSong)) return;
        if (on && (CubeInspector.IsOpen || Presenter.Active || MainMenu.IsShown)) return;   // S8: the screen belongs to them (no flicker, no TrayOpened)
        open = on;
        if (on)
        {
            hand.gameObject.SetActive(true); vignette.gameObject.SetActive(true);
            starsDirty = true;
            Rebuild();
            auditionHold = Time.unscaledTime + 0.3f;
            AudioPool.UI(ProceduralAudio.Pop(), 0.28f, 1.1f);
            Onboarding.Notify(Onboarding.Ev.TrayOpened);
        }
        else
        {
            if (ghost != null) CancelDrag();
            fanKey = fanColour = 0f;
            AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.8f);
        }
    }

    // ------------------------------------------------------------------ building the hand
    void Rebuild()
    {
        dirty = false;
        foreach (var c in cards) if (c != null) Destroy(c.gameObject);
        cards.Clear();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return;
        var key = MusicTheory.KeyOfSong();
        var last = LastChordIsland();
        int bars = last != null ? Mathf.Clamp(last.bars, 1, 4) : 1;
        int lastRoot = last != null ? last.chordRootMIDI : 60;

        // Next: the deck first (in order), then the ranked follow-ups of the last island in song order
        var next = new List<SongManager.MeasureData>(); var nextDeck = new List<int>();
        for (int i = 0; i < deck.Length && next.Count < DeckShown; i++) { next.Add(deck[i]); nextDeck.Add(i); }
        foreach (var s in MusicTheory.SuggestNext(key, lastRoot, 7, bars))
        {
            if (next.Count >= NextCount) break;
            bool dup = false; foreach (var m in next) if (MusicTheory.SameChord(m, s)) { dup = true; break; }
            if (!dup) { next.Add(s); nextDeck.Add(-1); }
        }
        var keySet = MusicTheory.DiatonicSet(key, bars);
        var colours = MusicTheory.Borrowed(key, bars);
        for (int i = 0; i < next.Count; i++) cards.Add(MakeCard(next[i], nextDeck[i], 0, i, false));
        for (int i = 0; i < keySet.Length; i++) cards.Add(MakeCard(keySet[i], -1, 1, i, true));
        for (int i = 0; i < colours.Length; i++) cards.Add(MakeCard(colours[i], -1, 2, i, true));
        // sibling order: stacks behind, the Next hand in front with the next card on top (front-left)
        for (int i = cards.Count - 1; i >= 0; i--) if (cards[i].section != 0) cards[i].transform.SetAsFirstSibling();
        for (int i = cards.Count - 1; i >= 0; i--) if (cards[i].section == 0) cards[i].transform.SetAsLastSibling();
        foreach (var c in cards) c.baseSibling = c.transform.GetSiblingIndex();
        foreach (var tt in new[] { diceBtn, dupBtn, wandBtn, moonBtn }) tt.transform.SetAsLastSibling();
        if (wandBtn != null) wandBtn.dim = deck.Length == 0;
        if (moonBtn != null) moonBtn.dim = sm.Moons.Count >= SongManager.MaxMoons;
        if (cards.Count > 0) Hints.Register("tray.next0", UIKit.RT(cards[0]));
        if (cards.Count > 0 && cards[0].badge != null) Hints.Register("tray.badge0", cards[0].badge);
        if (kbBtn != null) kbBtn.transform.SetAsLastSibling();
        if (stairsBtn != null) stairsBtn.transform.SetAsLastSibling();
        if (melodyBtn != null) melodyBtn.transform.SetAsLastSibling();
        LayoutHand(true);
        RefreshStars();
    }

    /// <summary>v6: the last island that holds a chord (keyboards carry no chord of their own: SPEC v6 §3.1; v7: nor do stairs and phrases).</summary>
    static KeyBlock LastChordIsland()
    {
        var sm = SongManager.I;
        for (int i = sm.Islands.Count - 1; i >= 0; i--) { var kb = sm.Islands[i]; if (kb != null && !kb.IsMoon && !kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase) return kb; }
        return sm.Islands.Count > 0 ? sm.Islands[sm.Islands.Count - 1] : null;
    }

    TrayCard MakeCard(SongManager.MeasureData md, int deckIndex, int section, int rank, bool small)
    {
        float w = small ? SmallW : CardW, h = small ? SmallH : CardH;
        var go = UIKit.Obj(section == 0 ? "Card" : (section == 1 ? "KeyCard" : "ColourCard"), hand);
        var rt = UIKit.RT(go);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(w, h);
        var hit = go.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
        var innerGo = UIKit.Obj("Inner", go.transform);
        var inner = UIKit.RT(innerGo);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.pivot = new Vector2(0.5f, 0.5f); inner.offsetMin = Vector2.zero; inner.offsetMax = Vector2.zero;
        CardFace face;
        Color chord = DrawCard(inner, md, small, section == 0 && rank == 0 && deckIndex >= 0, out face);
        var card = go.AddComponent<TrayCard>();
        card.Init(this, md, deckIndex, section, inner, chord);
        card.vibe = face.vibe; card.home = face.home; card.glyph = face.glyph; card.paper = face.paper; card.caption = face.caption;
        card.job = face.job; card.badge = face.badge; card.star = face.star;
        InkCaption.Attach(go, face.caption);   // v6: its job, what it swaps with and its weather ("2 · away · swaps with any 2 · rainy")
        card.rank = rank; card.small = small;
        return card;
    }

    /// <summary>What a card face shows (read back by TrayCard for tests).</summary>
    struct CardFace { public VibeKind vibe; public bool home; public string glyph, caption; public Color paper; public int job; public RectTransform badge, star; }

    /// <summary>A card's paper: its vibe colour, a touch of cream so the ink and the glyph stay the loudest thing on it.</summary>
    public static Color PaperOf(Color chord) => Color.Lerp(Comic.Opaque(chord), Comic.Cream, 0.12f);

    /// <summary>SPEC v5 §7: the card IS its vibe — the vibe colour as the paper, the vibe glyph big in the middle (painted in its own colours:
    /// VibeGlyphs.Build), the island itself small in ¾ view at the bottom (platform, a lip, the cream tile grid brightening with pitch), a
    /// little house sticker on the key's home chord (Vibe.IsHome) and, on the next deck card, a small ▸ sticker. A player remembers "the storm
    /// card makes it tense, the sun card with the house is home".</summary>
    static Color DrawCard(RectTransform parent, SongManager.MeasureData md, bool small, bool next, out CardFace face)
    {
        int[] stored = md.semitones != null && md.semitones.Length > 0 ? md.semitones : new[] { 0, 4, 7 };
        var semis = new List<int>(MusicTheory.EffectiveSemis(stored, 0, SongManager.Climate));
        if (!semis.Contains(0)) semis.Insert(0, 0);
        semis.Sort();
        Color chord = MusicTheory.ChordColor(md.chordRootMIDI, semis);   // = Vibe.ChordColor: the vibe's family colour
        var vibe = Vibe.Of(semis);
        bool home = Vibe.IsHome(md.chordRootMIDI);
        float W = small ? SmallW : CardW, H = small ? SmallH : CardH;
        face = new CardFace { vibe = vibe, home = home, glyph = Vibe.Icon(vibe), caption = JobCaption(md), paper = PaperOf(chord), job = Harmony.Job(md) };
        var body = parent.gameObject.AddComponent<InkShape>();
        body.Shape = InkShape.Kind.RoundRect; body.color = face.paper; body.Radius = small ? 9f : 12f; body.Wobble = 1.3f;
        body.SetInk(small ? 1.8f : 2.2f, small ? 2.8f : 3.4f); body.ShadowOffset = small ? new Vector2(3f, -4f) : new Vector2(4f, -5f); body.raycastTarget = false;
        // a soft lighter window behind the glyph (the sky it sits in) and the island, small, at the bottom
        var art = HudKit.Node(parent, "Island", new Vector2(0.5f, 0.5f), Vector2.zero, parent.sizeDelta.sqrMagnitude > 1f ? parent.sizeDelta : new Vector2(W, H));
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = Vector2.zero; art.offsetMax = Vector2.zero;
        var painter = art.gameObject.AddComponent<InkPainter>(); painter.raycastTarget = false;
        int rows = semis.Count, cols = ProjectConfig.numInversions;
        // tile brightness by pitch (columns are inversions), as on the island
        var inv = new List<int>(semis); var midi = new int[cols, rows]; int lo = int.MaxValue, hi = int.MinValue;
        for (int x = 0; x < cols; x++)
        {
            if (x > 0) { int low = inv[0]; inv.RemoveAt(0); inv.Add(low + 12); inv.Sort(); }
            for (int z = 0; z < rows; z++) { midi[x, z] = inv[z]; lo = Mathf.Min(lo, inv[z]); hi = Mathf.Max(hi, inv[z]); }
        }
        Color tint = KeyBlock.TileTintOf(chord), plat = Comic.Opaque(KeyBlock.PlatformColorOf(chord));
        Color window = Color.Lerp(face.paper, Color.white, 0.38f);
        float glyphY = H * (small ? 0.12f : 0.13f), islandY = -H * (small ? 0.30f : 0.31f), k = small ? 0.34f : 0.5f, winR = W * (small ? 0.36f : 0.35f);
        painter.onPaint = p =>
        {
            Vector2 c = p.Area.center;
            p.Disc(c + new Vector2(0f, glyphY), winR, winR, window, 40);
            PaintIsland(p, cols, rows, midi, lo, hi, tint, plat, k, c.y + islandY);
        };
        // the vibe glyph, big and centred in its window
        var g = VibeGlyphs.Build(parent, "Vibe", vibe, small ? 40f : 62f);
        g.anchoredPosition = new Vector2(0f, glyphY);
        if (JobBadge.Valid(face.job))
        {
            // v6: the job badge where v5 stuck the home sticker (the job-1 badge IS the house) — top-right on a hand card (its left side is
            // under the card before it), top-left on a stack card (a fanned stack covers each card's right side); tilted like a sticker
            float bs = small ? 24f : 32f;
            var holder = HudKit.Node(parent, "Job", new Vector2(small ? 0f : 1f, 1f), new Vector2(small ? bs * 0.4f : -bs * 0.4f, -bs * 0.4f), new Vector2(bs, bs));
            holder.localEulerAngles = new Vector3(0f, 0f, small ? 8f : -8f);
            face.badge = JobBadge.Build(holder, face.job, bs);
        }
        {
            // v6: the fit star, stuck on the card's top edge (shown when the chord fits the reference pattern; the best one twinkles)
            float ss = small ? 24f : 32f;
            var st = HudKit.Node(parent, "Star", new Vector2(0.5f, 1f), new Vector2(0f, small ? 1f : 2f), new Vector2(ss, ss));
            Comic.GlyphImage(st, "Glyph", JobBadge.StarGlyph, JobBadge.StarGold, ss, true);
            Comic.GlyphImage(st, "Glint", JobBadge.StarGlint, Color.white, ss, false);
            st.gameObject.SetActive(false);
            face.star = st;
        }
        if (next)
        {
            var st = InkShape.Create(parent, "NextSticker", InkShape.Kind.Sticker, Comic.Ink, new Vector2(26f, 24f));
            st.raycastTarget = false; st.Ink = Comic.Cream; st.SetInk(1.4f, 2f); st.ShadowColor = Comic.Magenta; st.ShadowOffset = new Vector2(2.5f, -3f);
            var srt = st.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f); srt.anchoredPosition = new Vector2(12f, -10f); srt.localEulerAngles = new Vector3(0f, 0f, 8f);
            var tri = HudKit.Node(srt, "Tri", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 24f)).gameObject.AddComponent<InkPainter>();
            tri.raycastTarget = false;
            tri.onPaint = p => { Vector2 c = p.Area.center; q.Clear(); q.Add(c + new Vector2(6f, 0f)); q.Add(c + new Vector2(-4f, 6f)); q.Add(c + new Vector2(-4f, -6f)); p.Fill(q, 3, Comic.Cream); };
        }
        return chord;
    }

    static void PaintIsland(InkPainter p, int cols, int rows, int[,] midi, int lo, int hi, Color tint, Color plat, float k, float centreY)
    {
        Rect r = p.Area;
        float cell = 14f * k;
        Vector2 ax = new Vector2(cell * 0.94f, -cell * 0.2f), az = new Vector2(cell * 0.34f, cell * 0.4f);
        Vector2 size = ax * cols + az * rows;
        Vector2 o = new Vector2(r.center.x - size.x * 0.5f, centreY - size.y * 0.5f + cols * cell * 0.1f);
        float pad = cell * 0.42f;
        Vector2 p0 = o - ax.normalized * pad - az.normalized * pad, p1 = o + ax * cols + ax.normalized * pad - az.normalized * pad;
        Vector2 p2 = o + ax * cols + az * rows + ax.normalized * pad + az.normalized * pad, p3 = o + az * rows - ax.normalized * pad + az.normalized * pad;
        // the lip (the platform's thickness), then the platform, then the tiles
        Vector2 lip = new Vector2(0f, -5.5f * k);
        q.Clear(); q.Add(p0 + lip); q.Add(p1 + lip); q.Add(p2 + lip); q.Add(p3 + lip);
        p.Fill(q, 4, Color.Lerp(plat, Comic.Ink, 0.55f));
        q.Clear(); q.Add(p0 + lip); q.Add(p1 + lip); q.Add(p1); q.Add(p0);
        p.Fill(q, 4, Color.Lerp(plat, Comic.Ink, 0.4f));
        q.Clear(); q.Add(p0); q.Add(p1); q.Add(p2); q.Add(p3);
        p.Fill(q, 4, plat);
        q.Clear(); q.Add(p0 + lip); q.Add(p1 + lip); q.Add(p1); q.Add(p2); q.Add(p3); q.Add(p0);
        p.Stroke(q, q.Count, 1.8f * Mathf.Max(0.75f, k), Comic.Ink, true);
        for (int z = rows - 1; z >= 0; z--)
            for (int x = 0; x < cols; x++)
            {
                float b = hi > lo ? (midi[x, z] - lo) / (float)(hi - lo) : 0.5f;
                Color c = Color.Lerp(Color.Lerp(tint, Comic.Cream, 0.45f), Comic.Cream, 0.25f + 0.75f * b); c.a = 1f;
                Vector2 t0 = o + ax * (x + 0.12f) + az * (z + 0.12f);
                q.Clear(); q.Add(t0); q.Add(t0 + ax * 0.76f); q.Add(t0 + ax * 0.76f + az * 0.76f); q.Add(t0 + az * 0.76f);
                p.Fill(q, 4, c);
            }
    }

    /// <summary>Places the hand: Next cards on the arc (the next one front-left, bigger, raised; later ones lower), the Key / Colour stacks
    /// either side (fanned out above the hand while hovered), the tools at the right end. Drawn on twos (the gap of a dragged card closes).</summary>
    void LayoutHand(bool snap = false)
    {
        float e = Ease.OutCubic(shown);
        Vector2 centre = new Vector2(0f, -ArcR + 118f);
        int nNext = 0; foreach (var c in cards) if (c.section == 0 && !c.IsDragged) nNext++;
        float spread = 4.4f;
        int slot = 0;
        foreach (var c in cards)
        {
            if (c.section != 0) continue;
            if (c.IsDragged) continue;
            float ang = (slot - (nNext - 1) * 0.5f) * spread;
            float rad = ang * Mathf.Deg2Rad;
            Vector2 pos = centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * ArcR;
            pos.y += (slot == 0 ? 10f : -5f * slot);
            float sc = slot == 0 ? 1.08f : 1f;
            Place(c, pos, -ang, sc, e, snap);
            slot++;
        }
        float keyAng = -(nNext * 0.5f * spread + 6.5f), colAng = nNext * 0.5f * spread + 6.5f;
        LayoutStack(1, keyStackRt, centre, keyAng, fanKey, e, snap);
        LayoutStack(2, colourStackRt, centre, colAng, fanColour, e, snap);
        // tools: stickers past the Colour stack — dice, duplicate, wand over the Moon card, (v6) the keyboard card and (v7) the stairs and melody cards
        float cr = colAng * Mathf.Deg2Rad;
        float tx = (centre + new Vector2(Mathf.Sin(cr), Mathf.Cos(cr)) * ArcR).x + SmallW * 0.5f + 52f;
        TrayTool[] tools = { diceBtn, dupBtn, wandBtn, moonBtn, kbBtn, stairsBtn, melodyBtn };
        for (int i = 0; i < tools.Length; i++)
        {
            if (tools[i] == null) continue;
            int row = i < 3 ? 0 : 1, colI = i < 3 ? i : i - 3;
            Vector2 pos = new Vector2(tx + colI * 60f + row * 30f, row == 0 ? 96f : 38f);
            var rt = tools[i].transform as RectTransform;
            rt.anchoredPosition = Vector2.Lerp(new Vector2(pos.x * 0.3f, -60f), pos, e);
            rt.localEulerAngles = new Vector3(0f, 0f, (i % 2 == 0 ? 3f : -4f) + (row == 0 ? -2f : 2f));
        }
    }

    void LayoutStack(int section, RectTransform hitRt, Vector2 centre, float ang, float fan, float e, bool snap)
    {
        float rad = ang * Mathf.Deg2Rad;
        Vector2 basePos = centre + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * ArcR + new Vector2(0f, -22f);
        hitRt.anchoredPosition = basePos + new Vector2(0f, fan * 105f); hitRt.localEulerAngles = new Vector3(0f, 0f, -ang * (1f - fan));
        hitRt.sizeDelta = new Vector2(SmallW + 24f + fan * 330f, SmallH + 30f + fan * 220f);
        int n = 0; foreach (var c in cards) if (c.section == section) n++;
        int i = 0;
        float fe = Ease.OutCubic(fan);
        foreach (var c in cards)
        {
            if (c.section != section) continue;
            // collapsed: a neat pile whose thickness is the count; fanned: an arc above the hand
            Vector2 pile = basePos + new Vector2(-i * 1.2f, i * 2.2f);
            float fa = (i - (n - 1) * 0.5f) * (section == 1 ? 9f : 12f);
            Vector2 fanPos = basePos + new Vector2((i - (n - 1) * 0.5f) * (SmallW - 12f), 205f - Mathf.Abs(i - (n - 1) * 0.5f) * 10f);   // above the hand
            Vector2 pos = Vector2.Lerp(pile, fanPos, fe);
            float rot = Mathf.Lerp(-ang + (i - (n - 1) * 0.5f) * 0.8f, -fa * 0.5f, fe);
            Place(c, pos, rot, 1f, e, snap);
            c.fanned = fan > 0.9f;
            i++;
        }
    }

    void Place(TrayCard c, Vector2 pos, float rot, float scale, float e, bool snap)
    {
        var rt = UIKit.RT(c);
        Vector2 from = new Vector2(pos.x * 0.25f, -CardH * 0.6f);
        Vector2 target = Vector2.Lerp(from, pos, e);
        c.slotPos = target; c.slotRot = Mathf.Lerp(0f, rot, e); c.slotScale = scale;
        if (snap) { rt.anchoredPosition = c.slotPos; rt.localEulerAngles = new Vector3(0f, 0f, c.slotRot); rt.localScale = new Vector3(scale, scale, 1f); }
    }

    // ------------------------------------------------------------------ tools' pictures
    static void PaintDiceTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Cube(c, 30f, Comic.Cream, Comic.Ink, 1.8f);
        p.Disc(c + new Vector2(0f, 7.5f), 2.2f, Comic.Ink);
        p.Disc(c + new Vector2(-8f, -1f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(-5f, -8f), 1.9f, Comic.Ink);
        p.Disc(c + new Vector2(6f, -2f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(8f, -7f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(5f, -11f), 1.9f, Comic.Ink);
    }

    static void PaintDupTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.RoundRect(new Rect(c.x - 12f, c.y - 9f, 17f, 22f), 3f, Comic.A(Palette.Accent, 0.9f), 1.8f, Comic.Ink, 3f, 2f);
        p.Shift = new Vector2(1.5f, -2f); p.RoundRect(new Rect(c.x - 4f, c.y - 13f, 17f, 22f), 3f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
        p.RoundRect(new Rect(c.x - 4f, c.y - 13f, 17f, 22f), 3f, Palette.Accent, 1.8f, Comic.Ink);
    }

    static void PaintWandTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Line(c + new Vector2(-12f, -12f), c + new Vector2(4f, 4f), 3.6f, Comic.Ink, true);
        q.Clear();
        for (int i = 0; i < 10; i++) { float a = (90f + i * 36f) * Mathf.Deg2Rad; float rr = i % 2 == 0 ? 10f : 4.2f; q.Add(c + new Vector2(6f, 6f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr); }
        p.InkFill(q, q.Count, Comic.Pop, 1.6f, Comic.Ink);
        p.Disc(c + new Vector2(-6f, 12f), 1.6f, Comic.Ink); p.Disc(c + new Vector2(14f, -6f), 1.4f, Comic.Ink);
    }

    static void PaintMoonTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.RoundRect(new Rect(c.x - 12f, c.y - 15f, 24f, 30f), 4f, Comic.A(KeyBlock.MoonColor, 1f), 1.8f, Comic.Ink);
        p.Disc(c + new Vector2(0f, 1f), 7.5f, Comic.Ink);
        p.Disc(c + new Vector2(0f, 1f), 5.8f, Comic.Cream);
        p.Disc(c + new Vector2(2.6f, 3f), 4.6f, Comic.A(KeyBlock.MoonColor, 1f));
    }

    /// <summary>v7: a little staircase of ink stone falling to the right, its runner cube on the top step and a dotted fall to the next.</summary>
    static void PaintStairsTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        Color stone = Comic.Opaque(IslandHeader.StairStone);
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect(c.x - 17f + i * 8.5f, c.y - 15f, 8f, 22f - i * 5f);
            p.Shift = new Vector2(1.2f, -1.6f); p.RoundRect(r, 1.2f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
            p.RoundRect(r, 1.2f, stone, 1.4f, Comic.Ink);
            p.Line(new Vector2(r.xMin + 1.2f, r.yMax - 1.6f), new Vector2(r.xMax - 1.2f, r.yMax - 1.6f), 1f, Comic.A(Color.white, 0.55f));
        }
        p.Cube(c + new Vector2(-13f, 13.5f), 10f, Instruments.Colors[3], Comic.Ink, 1.2f);
        q.Clear();
        for (int k = 0; k <= 8; k++) { float u = k / 8f; q.Add(new Vector2(c.x - 8f + u * 9f, c.y + 12f - u * u * 9f)); }
        p.Stroke(q, q.Count, 1.4f, Comic.Ink, false, 2f, 1.8f, -1f, true);
    }

    /// <summary>v7: a little melody roll — a paper strip with a tiny piano on its left end and note bars at different heights (time runs right).</summary>
    static void PaintMelodyTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        Rect body = new Rect(c.x - 19f, c.y - 13f, 38f, 26f);
        p.Shift = new Vector2(1.5f, -2f); p.RoundRect(body, 3.5f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
        p.RoundRect(body, 3.5f, Comic.Opaque(IslandHeader.PhraseTint), 1.8f, Comic.Ink);
        Rect keys = new Rect(body.xMin + 2f, body.yMin + 2f, 6f, body.height - 4f);
        p.RoundRect(keys, 1f, Ivory, 1f, Comic.Ink);
        for (int k = 1; k < 4; k++) { float y = keys.yMin + k * keys.height / 4f; p.Line(new Vector2(keys.xMin, y), new Vector2(keys.xMax, y), 0.8f, Comic.Ink); }
        p.Line(new Vector2(body.xMin + 20f, body.yMin + 2f), new Vector2(body.xMin + 20f, body.yMax - 2f), 1f, Comic.A(Comic.Ink, 0.55f));
        float[] ys = { 0.3f, 0.55f, 0.72f, 0.45f }; float[] ws = { 6f, 4f, 7f, 5f };
        float x = body.xMin + 10f;
        for (int k = 0; k < 4; k++) { p.RoundRect(new Rect(x, body.yMin + body.height * ys[k] - 2f, ws[k], 4f), 1.6f, Comic.Cream, 1.1f, Comic.Ink); x += ws[k] + 1.5f; }
    }

    /// <summary>The ivory of the keyboard card's keys, and the wood of its cheek.</summary>
    public static readonly Color Ivory = new Color(1f, 0.97f, 0.9f), Wood = new Color(0.72f, 0.52f, 0.36f);

    /// <summary>v6: a little piano — a wood cheek, five ivory keys, three ink black keys (the C D E F G of a real keyboard).</summary>
    static void PaintKeyboardTool(InkPainter p)
    {
        Vector2 c = p.Area.center;
        Rect body = new Rect(c.x - 18f, c.y - 12f, 36f, 23f);
        p.Shift = new Vector2(1.5f, -2f); p.RoundRect(body, 3.5f, Comic.A(Comic.Ink, 0.8f)); p.Shift = Vector2.zero;
        p.RoundRect(body, 3.5f, Wood, 1.8f, Comic.Ink);
        Rect keys = new Rect(body.xMin + 2.5f, body.yMin + 2.5f, body.width - 5f, body.height - 8f);
        p.RoundRect(keys, 1.5f, Ivory, 1.2f, Comic.Ink);
        float kw = keys.width / 5f;
        for (int k = 1; k < 5; k++) { float x = keys.xMin + k * kw; p.Line(new Vector2(x, keys.yMin + 0.8f), new Vector2(x, keys.yMax - 0.8f), 1.1f, Comic.Ink); }
        for (int k = 1; k <= 4; k++)
        {
            if (k == 3) continue;   // no black key between E and F
            float x = keys.xMin + k * kw;
            p.RoundRect(new Rect(x - 2.6f, keys.yMin + keys.height * 0.42f, 5.2f, keys.height * 0.58f), 1f, Comic.Ink);
        }
    }

    // ------------------------------------------------------------------ v6: the fit stars (SPEC v6 §7.1)
    int lastHeaderIdx = -1;
    /// <summary>Remembers the island the header showed last (a player looks at an island, then reaches for the deck).</summary>
    void TrackHeader()
    {
        var h = IslandHeader.Current; var sm = SongManager.I;
        if (h == null || h.IsMoon || sm == null) return;
        lastHeaderIdx = sm.Islands.IndexOf(h); lastHeaderT = Time.unscaledTime;
    }

    /// <summary>The island the stars listen to: the header's island, the selected cube's island, the island the header showed last (≤ 20 s
    /// ago), the selected island, the lit island while playing, the camera's focus, the last island.</summary>
    KeyBlock FocusForStars()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return null;
        var h = IslandHeader.Current;
        if (h != null && !h.IsMoon) return h;
        var pm = PathManager.I;
        var cube = CubeInspector.Current != null ? CubeInspector.Current : (pm != null ? pm.selectedCube : null);
        if (cube != null && cube.Island != null && !cube.Island.IsMoon) return cube.Island;
        if (lastHeaderIdx >= 0 && lastHeaderIdx < sm.Islands.Count && Time.unscaledTime - lastHeaderT < 20f && sm.Islands[lastHeaderIdx] != null) return sm.Islands[lastHeaderIdx];
        var ui = UIManager.I;
        if (ui != null && ui.SelectedMoon < 0 && ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count && sm.Islands[ui.SelectedMeasure] != null) return sm.Islands[ui.SelectedMeasure];
        if (GlobalClock.IsPlaying)
        {
            int col = sm.LitColumn;
            KeyBlock lit = null;
            foreach (var kb in sm.Islands)
            {
                if (kb == null || kb.column != col) continue;
                if (lit == null) lit = kb;
                foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && c.Island == kb) return kb;   // the lit island that plays notes
            }
            if (lit != null) return lit;
        }
        var oc = OrbitCamera.I;
        if (oc != null && oc.FocusedMeasure >= 0 && oc.FocusedMeasure < sm.Islands.Count && sm.Islands[oc.FocusedMeasure] != null) return sm.Islands[oc.FocusedMeasure];
        return sm.Islands[sm.Islands.Count - 1];
    }

    /// <summary>A cheap fingerprint of the stars' reference (the stars recompute when it changes).</summary>
    int StarKeyNow()
    {
        if (Clipboard.HasPattern && !Clipboard.IsDrums) return -7;   // (a new copy fires Clipboard.OnChanged)
        var f = FocusForStars(); var sm = SongManager.I;
        return f == null || sm == null ? -1 : sm.Islands.IndexOf(f) * 4 + (f.IsKeyboard ? 1 : 0);
    }

    /// <summary>v6: every card's star — the fit of the reference pattern over its chord (Harmony.Fit ≥ StarThreshold) or, with no notes yet, the
    /// two best next chords after the focused chord; the best card's star twinkles.</summary>
    void RefreshStars()
    {
        starsDirty = false;
        starKey = StarKeyNow();
        StarRefreshes++;
        refMidi.Clear(); refW.Clear();
        StarRef = StarSource.None; StarIsland = null; StarNotes = 0; StarCount = 0; BestCard = null;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) { foreach (var c in cards) if (c != null) c.SetStar(false, false, 0f); return; }
        if (Clipboard.HasPattern && !Clipboard.IsDrums && Clipboard.Midi != null && Clipboard.Midi.Length > 0)
        {
            // the pattern on the clipboard (in the hand while pasting)
            refMidi.AddRange(Clipboard.Midi);
            if (Clipboard.Weights != null && Clipboard.Weights.Length == Clipboard.Midi.Length) refW.AddRange(Clipboard.Weights);
            StarRef = StarSource.Pattern;
        }
        else
        {
            var focus = FocusForStars();
            StarIsland = focus;
            if (focus != null && Harmony.NotesOfIsland(focus, refMidi, refW) > 0) StarRef = StarSource.Island;
            else if (focus != null) { refMidi.Clear(); refW.Clear(); StarRef = StarSource.Next; }
        }
        StarNotes = refMidi.Count;
        TrayCard best = null; float bestFit = -1f;
        if (StarRef == StarSource.Pattern || StarRef == StarSource.Island)
        {
            foreach (var c in cards)
            {
                if (c == null) continue;
                float f = Harmony.Fit(refMidi, refW.Count == refMidi.Count ? refW : null, c.data);
                c.fit = f; c.starred = f >= Harmony.StarThreshold;
                if (c.starred && f > bestFit + 1e-4f) { bestFit = f; best = c; }
            }
        }
        else if (StarRef == StarSource.Next)
        {
            int root; IList<int> semis;
            int rootMidi = StarIsland.chordRootMIDI;
            if (Harmony.ChordOf(StarIsland, out root, out semis)) rootMidi = root;
            var next = MusicTheory.SuggestNext(MusicTheory.KeyOfSong(), rootMidi, 2);
            foreach (var c in cards)
            {
                if (c == null) continue;
                int rank = -1;
                for (int k = 0; k < next.Length; k++) if (MusicTheory.SameChord(c.data, next[k])) { rank = k; break; }
                c.fit = rank == 0 ? 1f : (rank == 1 ? 0.9f : 0f); c.starred = rank >= 0;
                if (rank == 0 && best == null) best = c;
            }
        }
        else foreach (var c in cards) if (c != null) { c.fit = 0f; c.starred = false; }
        int n = 0;
        foreach (var c in cards) { if (c == null) continue; c.SetStar(c.starred, c == best, c.fit); if (c.starred) n++; }
        StarCount = n; BestCard = best;
    }

    // ------------------------------------------------------------------ v6: the ⇄ swap chips (SPEC v6 §7.1)
    class SwapChip { public RectTransform rt, badge; public int job; public float pop; }

    SwapChip ChipAt(int i)
    {
        while (chips.Count <= i)
        {
            var rt = HudKit.Node(chipsRt, "SwapChip" + chips.Count, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
            var disc = rt.gameObject.AddComponent<InkShape>();
            disc.Shape = InkShape.Kind.Circle; disc.color = Comic.Cream; disc.SetInk(1.6f, 2.4f); disc.ShadowOffset = new Vector2(2.5f, -3f); disc.raycastTarget = false;
            Comic.GlyphImage(rt, "Swap", JobBadge.SwapGlyph, Comic.Ink, 22f, false);
            var holder = HudKit.Node(rt, "Job", new Vector2(1f, 0f), new Vector2(-2f, 3f), new Vector2(22f, 22f));
            var badge = JobBadge.Build(holder, 1, 22f);
            rt.gameObject.SetActive(false);
            chips.Add(new SwapChip { rt = rt, badge = badge, job = 1 });
        }
        return chips[i];
    }

    int chipTick = int.MinValue;
    /// <summary>While a card is hovered, every chord island doing the same job wears a ⇄ chip over it (screen space, following the camera) and its
    /// rail bead pulses: those are the chords this card can swap with.</summary>
    void UpdateSwapChips()
    {
        TrayCard hov = null;
        if (open && shown > 0.6f && ghost == null)
        {
            if (forcedSwap != null) hov = forcedSwap;
            else foreach (var c in cards) if (c != null && c.Hovered && !c.IsDragged) { hov = c; break; }
        }
        int job = hov != null ? hov.job : 0;
        var rail = UIManager.I != null ? UIManager.I.Rail : null;
        var sm = SongManager.I; var cam = Camera.main;
        if (!JobBadge.Valid(job) || sm == null || cam == null || chipsRt == null)
        {
            if (SwapChipsShown > 0 || swapCard != null) foreach (var ch in chips) HudKit.SetActive(ch.rt, false);
            swapCard = null; SwapJob = 0; SwapChipsShown = 0; swapIslands.Clear();
            if (rail != null && rail.SwapJob != 0) rail.SwapJob = 0;
            return;
        }
        int t = HudKit.TwosTick; bool step = t != chipTick; chipTick = t;
        if (hov != swapCard || step)
        {
            if (hov != swapCard) foreach (var ch in chips) ch.pop = 0f;
            swapCard = hov;
            swapIslands.Clear();
            foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && !kb.IsKeyboard && Harmony.Job(kb) == job) swapIslands.Add(kb);
        }
        SwapJob = job;
        if (rail != null && rail.SwapJob != job) rail.SwapJob = job;
        int shownN = 0;
        for (int i = 0; i < swapIslands.Count; i++)
        {
            var kb = swapIslands[i]; var chip = ChipAt(i);
            Vector3 sp = kb != null ? cam.WorldToScreenPoint(kb.VisualCenter + Vector3.up * 1.3f) : new Vector3(0f, 0f, -1f);
            if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) { HudKit.SetActive(chip.rt, false); continue; }
            HudKit.SetActive(chip.rt, true);
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(chipsRt, sp, null, out lp);
            if (chip.job != job) { JobBadge.Set(chip.badge, job, 22f); chip.job = job; }
            if (step) chip.pop = Mathf.Min(3f, chip.pop + 1f);
            float s = chip.pop < 1.5f ? 0.6f : (chip.pop < 2.5f ? 1.1f : 1f);   // pops in over two drawings
            chip.rt.localScale = new Vector3(s, s, 1f);
            chip.rt.anchoredPosition = lp + new Vector2(0f, ((t + i) % 4 < 2 ? 1.5f : -1.5f));   // a small bob on twos
            shownN++;
        }
        for (int i = swapIslands.Count; i < chips.Count; i++) HudKit.SetActive(chips[i].rt, false);
        SwapChipsShown = shownN;
    }

    // ------------------------------------------------------------------ hover audition (harmonic safety: only while stopped)
    /// <summary>Hover auditions started (stopped) and quiet hovers (playing: a tick + a pulse only) — tests.</summary>
    public int AuditionCount { get; private set; }
    public int QuietHoverCount { get; private set; }

    internal void OnCardHover(TrayCard c)
    {
        if (Time.unscaledTime < auditionHold) return;
        if (GlobalClock.IsPlaying || !Synth.Ready)
        {
            c.Pulse();
            AudioPool.UI(ProceduralAudio.Tick(), 0.22f, 1.25f);
            QuietHoverCount++;
            return;
        }
        if (audition != null) StopCoroutine(audition);
        // v7 (the user: "instead of the chord playing, play the last (live) cube grid pattern … in that chord, adapted; if no chord, the fallback"):
        // the reference column's patterns over this card's chord (CardPreview); a card without a chord, or a song without notes: the arpeggio
        if (CardPreview.Play(c.data, FocusedIsland())) { PatternAuditionCount++; AuditionCount++; return; }
        audition = StartCoroutine(Arpeggio(c.data, 0.07f, 64, 0.5f));
        AuditionCount++;
    }
    /// <summary>v7: hovers that played the reference column's patterns over the card's chord (CardPreview) — tests.</summary>
    public int PatternAuditionCount { get; private set; }

    /// <summary>The chord's tones, root up, <paramref name="gap"/> s apart on the Keys slot (Synth.Preview); stops if the song starts.</summary>
    IEnumerator Arpeggio(SongManager.MeasureData md, float gap, int vel, float len)
    {
        int[] stored = md.semitones != null && md.semitones.Length > 0 ? md.semitones : new[] { 0, 4, 7 };
        var semis = new List<int>(MusicTheory.EffectiveSemis(stored, 0, SongManager.Climate));
        if (!semis.Contains(0)) semis.Insert(0, 0);
        semis.Sort();
        for (int i = 0; i < semis.Count; i++)
        {
            if (GlobalClock.IsPlaying || !Synth.Ready) yield break;
            Synth.Preview(0, SynthBank.ClampToRegister(0, md.chordRootMIDI + semis[i] + SongManager.Transpose), vel, len);
            yield return new WaitForSecondsRealtime(gap);
        }
        audition = null;
    }

    void Strum(SongManager.MeasureData md)
    {
        if (GlobalClock.IsPlaying || !Synth.Ready) return;
        if (audition != null) StopCoroutine(audition);
        audition = StartCoroutine(Arpeggio(md, 0.04f, 74, 0.8f));
    }

    // ------------------------------------------------------------------ placing
    /// <summary>The island a click places after: the selected island, else the camera's focused island, else the last one.</summary>
    static int FocusedIsland()
    {
        var sm = SongManager.I;
        int n = sm.Islands.Count;
        int sel = UIManager.I != null ? UIManager.I.SelectedMeasure : -1;
        if (sel >= 0 && sel < n && (UIManager.I == null || UIManager.I.SelectedMoon < 0)) return sel;
        int f = OrbitCamera.I != null ? OrbitCamera.I.FocusedMeasure : -1;
        if (f >= 0 && f < n) return f;
        return n - 1;
    }

    /// <summary>Removes a deck card from the deck before the placement's snapshot (undo brings it back); returns the chord to place.</summary>
    SongManager.MeasureData Take(TrayCard c, out SongManager.MeasureData[] before)
    {
        before = deck;
        var md = Copy(c.data);
        if (c.deckIndex >= 0)
        {
            int at = Array.IndexOf(deck, c.data);
            if (at < 0) for (int i = 0; i < deck.Length; i++) if (MusicTheory.SameChord(deck[i], c.data)) { at = i; break; }
            if (at >= 0)
            {
                var list = new List<SongManager.MeasureData>(deck);
                list.RemoveAt(at);
                deck = list.ToArray();
            }
        }
        return md;
    }

    internal void OnCardClick(TrayCard c)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || c == null || ghost != null) return;
        SongManager.MeasureData[] before;
        var md = Take(c, out before);
        int at = sm.PlaceIslandAfter(md, FocusedIsland());
        if (at < 0) { deck = before; dirty = true; return; }
        AfterPlace(at, md, true);
    }

    void AfterPlace(int at, SongManager.MeasureData md, bool strum)
    {
        var sm = SongManager.I;
        if (sm == null || at < 0 || at >= sm.Islands.Count) return;
        var kb = sm.Islands[at];
        if (UIManager.I != null) UIManager.I.SelectMeasure(at, false);
        if (OrbitCamera.I != null && kb != null) OrbitCamera.I.FrameIsland(kb);
        if (strum) Strum(md);
        auditionHold = Time.unscaledTime + 0.4f;
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.IslandPlaced);
    }

    void RollDice()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null) return;
        var key = MusicTheory.KeyOfSong();
        var last = LastChordIsland();   // v6: a keyboard carries no chord of its own
        int bars = last != null ? Mathf.Clamp(last.bars, 1, 4) : 1;
        var pool = new List<SongManager.MeasureData>(MusicTheory.SuggestNext(key, last != null ? last.chordRootMIDI : 60, 3, bars));
        pool.AddRange(pool);                              // the ranked follow-ups weigh double
        pool.AddRange(MusicTheory.DiatonicSet(key, bars));
        var md = pool[UnityEngine.Random.Range(0, pool.Count)];
        int at = sm.PlaceIslandAfter(md, FocusedIsland());
        if (at >= 0) { if (diceBtn != null) diceBtn.Pulse(); AfterPlace(at, md, true); }
    }

    void DuplicateFocused()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null) return;
        int f = FocusedIsland();
        int at = sm.DuplicateMeasure(f);
        if (at >= 0) AfterPlace(at, sm.Islands[at].ToData(), false);
    }

    void PlaceWholeDeck()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null) return;
        if (deck.Length == 0) { if (wandBtn != null) wandBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        var all = deck;
        deck = new SongManager.MeasureData[0];            // the snapshot of this gesture carries the emptied deck
        int n = sm.PlaceAll(all);
        if (n <= 0) { deck = all; dirty = true; return; }
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.8f);
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.IslandPlaced);
    }

    void AddMoonCard()
    {
        if (UIManager.I == null || ghost != null || moonDrag) return;
        AddMoonAt(FocusedColumn());
    }

    // ------------------------------------------------------------------ v6 §11: a Moon starts at a column
    /// <summary>Moons added by the Moon card (tests).</summary>
    public int MoonsAdded { get; private set; }

    /// <summary>The column a new Moon starts at: the lit column while playing, else the selected / focused island's column, else 0.</summary>
    public static int FocusedColumn()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || sm.ColumnCount == 0) return 0;
        int last = sm.ColumnCount - 1;
        if (GlobalClock.IsPlaying) return Mathf.Clamp(sm.LitColumn, 0, last);
        var ui = UIManager.I;
        if (ui != null && ui.SelectedMoon < 0 && ui.SelectedMeasure >= 0 && ui.SelectedMeasure < sm.Islands.Count && sm.Islands[ui.SelectedMeasure] != null) return Mathf.Clamp(sm.Islands[ui.SelectedMeasure].column, 0, last);
        var h = IslandHeader.Current;
        if (h != null && !h.IsMoon) return Mathf.Clamp(h.column, 0, last);
        var oc = OrbitCamera.I;
        if (oc != null && oc.FocusedMeasure >= 0 && oc.FocusedMeasure < sm.Islands.Count && sm.Islands[oc.FocusedMeasure] != null) return Mathf.Clamp(sm.Islands[oc.FocusedMeasure].column, 0, last);
        return 0;
    }

    /// <summary>A drum Moon whose section starts at column <paramref name="col"/> (SongManager.AddMoon(col)): one History entry, then selected;
    /// refused at MaxMoons (a thud, the card pulses). Returns its index or -1.</summary>
    int AddMoonAt(int col)
    {
        var sm = SongManager.I; var ui = UIManager.I;
        if (sm == null || !sm.HasSong || ui == null) return -1;
        if (sm.Moons.Count >= SongManager.MaxMoons) { if (moonBtn != null) moonBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return -1; }
        int i = sm.AddMoon(Mathf.Clamp(col, 0, Mathf.Max(0, sm.ColumnCount - 1)));
        if (i < 0) { if (moonBtn != null) moonBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return -1; }
        History.Push();   // (deduplicated when the op pushed already)
        if (ui.Rail != null) ui.Rail.MarkDirty();
        ui.SelectMoon(i);
        AudioPool.UI(ProceduralAudio.Chime(), 0.3f);
        if (moonBtn != null) moonBtn.dim = sm.Moons.Count >= SongManager.MaxMoons;
        MoonsAdded++;
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.MoonAdded);
        return i;
    }

    /// <summary>The column a sea point is in line with (package K's SongManager.ColumnNearX: a dragged Moon snaps its start there too).</summary>
    public static int ColumnAtX(float x)
    {
        var sm = SongManager.I;
        return sm == null || sm.ColumnCount == 0 ? -1 : sm.ColumnNearX(x);
    }

    void BeginMoonDrag()
    {
        if (ghost != null) CancelDrag();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || moonBtn == null || moonDrag) return;
        if (sm.Moons.Count >= SongManager.MaxMoons) { moonBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.4f, 1.3f); return; }
        moonDrag = true; moonDropCol = -1;
        floatCard = MakeFloatOf(moonBtn.transform as RectTransform, new Vector2(52f, 52f));
        if (floatCard != null) { floatCard.gameObject.SetActive(true); floatCard.anchoredPosition = ToCanvas(Input.mousePosition); }
        WorldInput.Lock("tray");
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f);
    }

    /// <summary>The dragged Moon card follows the pointer; over the sea the column it is in line with lights and the rail shows the drum there.</summary>
    void UpdateMoonDrag(Vector3 mp)
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { FlyBack(); CleanupMoonDrag(); return; }
        if (floatCard != null) floatCard.anchoredPosition = ToCanvas(mp);
        Vector3 g;
        SetMoonDrop(!InputUtil.PointerOverUI && GroundAt(mp, out g) ? ColumnAtX(g.x) : -1);
    }

    void SetMoonDrop(int col)
    {
        if (col == moonDropCol) return;
        moonDropCol = col;
        var rail = UIManager.I != null ? UIManager.I.Rail : null;
        if (rail != null) rail.MoonGhostColumn = col;
        var sm = SongManager.I;
        if (col >= 0 && sm != null)
        {
            float z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var kb in sm.Islands) if (kb != null && kb.column == col) { z0 = Mathf.Min(z0, kb.FrontEdge); z1 = Mathf.Max(z1, kb.BackEdge); }
            if (z0 < z1) ColumnBands.ShowRange(col, z0 - 6f, z1);
        }
        else ColumnBands.ClearRange();
    }

    void EndMoonDrag()
    {
        if (!moonDrag || simDrag) return;
        ResolveMoonDrop();
    }

    int ResolveMoonDrop()
    {
        int col = moonDropCol;
        if (col < 0) { FlyBack(); CleanupMoonDrag(); return -1; }
        CleanupMoonDrag();
        return AddMoonAt(col);
    }

    void CleanupMoonDrag()
    {
        if (floatCard != null) { Destroy(floatCard.gameObject); floatCard = null; }
        SetMoonDrop(-1);
        moonDrag = false; simDrag = false;
        WorldInput.Unlock("tray");
    }

    // ------------------------------------------------------------------ v6: the keyboard card (SPEC v6 §7.2)
    /// <summary>Keyboard islands added by the card (tests).</summary>
    public int KeyboardsAdded { get; private set; }

    /// <summary>A click: a keyboard island into the focused island's column (below it); a full column (or none) → a new last column. K's
    /// AddKeyboardIsland pushes the one History entry.</summary>
    void AddKeyboardCard()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null) return;
        int at = sm.AddKeyboardIsland(FocusedIsland(), false);
        if (at < 0) at = sm.AddKeyboardIsland(-1, false);
        if (at < 0) { if (kbBtn != null) kbBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        AfterKeyboard(at);
    }

    void AfterKeyboard(int at)
    {
        var sm = SongManager.I;
        KeyboardsAdded++;
        if (at >= 0 && at < sm.Islands.Count)
        {
            if (UIManager.I != null) UIManager.I.SelectMeasure(at, false);
            if (OrbitCamera.I != null && sm.Islands[at] != null) OrbitCamera.I.FrameIsland(sm.Islands[at]);
        }
        if (kbBtn != null) kbBtn.Pulse();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.2f);
        auditionHold = Time.unscaledTime + 0.4f;
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.KeyboardAdded);
        Onboarding.Notify(Onboarding.Ev.IslandPlaced);
    }

    /// <summary>What the keyboard card places: a keyboard's data (package K: the key's tonic as its lowest key) with the focused island's length.</summary>
    static SongManager.MeasureData KeyboardData()
    {
        var sm = SongManager.I;
        int f = FocusedIsland();
        int bars = f >= 0 && f < sm.Islands.Count && sm.Islands[f] != null ? Mathf.Clamp(sm.Islands[f].bars, 1, 4) : 1;
        return SongManager.KeyboardMeasure(bars);
    }

    void BeginKeyboardDrag()
    {
        if (ghost != null) CancelDrag();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || kbBtn == null) return;
        toolDrag = true; dragCard = null;
        ghost = IslandGhost.Create(KeyboardData());
        ghost.SetVisible(false);
        floatCard = MakeFloatOf(kbBtn.transform as RectTransform, new Vector2(52f, 52f));
        WorldInput.Lock("tray");
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f);
    }

    void EndKeyboardDrag()
    {
        if (ghost == null || !toolDrag || simDrag) return;
        ResolveKeyboardDrop();
    }

    /// <summary>The keyboard card's release: the ghost decides like a card's (IslandGhost.Snapshot / Place — package K builds a keyboard in a STACK
    /// slot or as an INSERTed column; it never offers a merge beside a chord island); blocked / over the HUD → it flies back.</summary>
    int ResolveKeyboardDrop()
    {
        var sm = SongManager.I;
        if (sm == null || ghost == null) { CleanupDrag(); return -1; }
        var md = ghost.Data != null ? ghost.Data : KeyboardData();
        var drop = ghost.Snapshot();
        if (!drop.visible || drop.mode == IslandGhost.Mode.Blocked) { if (drop.visible) AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.2f); FlyBack(); CleanupDrag(); return -1; }
        CleanupDrag();
        int at = IslandGhost.Place(drop, md);
        if (at < 0 || at >= sm.Islands.Count || sm.Islands[at] == null || !sm.Islands[at].IsKeyboard) at = at >= 0 ? at : sm.AddKeyboardIsland(-1, false);
        if (at < 0) { if (kbBtn != null) kbBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return -1; }
        AfterKeyboard(at);
        return at;
    }

    // ------------------------------------------------------------------ v7: the stairs card and the melody card (SPEC v7 §7.1 / §14.6)
    /// <summary>The group a new stairs runner / phrase cube gets: the cube in the hand, else -1 (the lead).</summary>
    static int HandGroup() { var pm = PathManager.I; return pm != null && pm.CubeInHand ? pm.selectedInstrument : -1; }

    /// <summary>A click: a stairs island into the focused island's column (below it), with a runner of the hand's group (else the lead); a full
    /// column → a new last column. The op pushes the one History entry.</summary>
    void AddStairsCard()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null) return;
        int g = HandGroup();
        int at = sm.AddStairIsland(FocusedIsland(), false, g);
        if (at < 0) at = sm.AddStairIsland(-1, false, g);
        if (at < 0) { if (stairsBtn != null) stairsBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return; }
        AfterStairs(at);
    }

    void AfterStairs(int at)
    {
        var sm = SongManager.I;
        History.Push();   // (deduplicated: the op pushed already)
        StairsAdded++;
        if (at >= 0 && at < sm.Islands.Count)
        {
            if (UIManager.I != null) UIManager.I.SelectMeasure(at, false);
            if (OrbitCamera.I != null && sm.Islands[at] != null) OrbitCamera.I.FrameIsland(sm.Islands[at]);
        }
        if (stairsBtn != null) stairsBtn.Pulse();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.25f);
        auditionHold = Time.unscaledTime + 0.4f;
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.StairsAdded);
        Onboarding.Notify(Onboarding.Ev.IslandPlaced);
    }

    /// <summary>What the stairs card drags: a stairs island's data (kind 3: a falling run of four eighths leading into the next chord) with the
    /// focused island's length — the ghost's one-row footprint.</summary>
    static SongManager.MeasureData StairsData()
    {
        var sm = SongManager.I;
        int f = FocusedIsland();
        int bars = f >= 0 && f < sm.Islands.Count && sm.Islands[f] != null ? Mathf.Clamp(sm.Islands[f].bars, 1, 4) : 1;
        return new SongManager.MeasureData { chordKey = "Stairs", chordRootMIDI = 60, semitones = new[] { 0 }, bars = bars, measureDuration = bars * GlobalClock.BeatsPerBar, kind = 3,
                                             energy = 2, repeat = 1, stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true };
    }

    void BeginStairsDrag()
    {
        if (ghost != null) CancelDrag();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || stairsBtn == null) return;
        toolDrag = true; stairsDrag = true; dragCard = null;
        ghost = IslandGhost.Create(StairsData());
        ghost.SetVisible(false);
        floatCard = MakeFloatOf(stairsBtn.transform as RectTransform, new Vector2(52f, 52f));
        WorldInput.Lock("tray");
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f);
    }

    void EndStairsDrag()
    {
        if (ghost == null || !stairsDrag || simDrag) return;
        ResolveStairsDrop();
    }

    /// <summary>The stairs card's release: a STACK slot → that column (above / below the island nearest the slot); an INSERT slot → a new column
    /// there (SongOps.InsertStairColumn; stairs never merge — K's ghost offers no merge slot); the end of the song → a new last column; blocked /
    /// over the HUD → it flies back.</summary>
    int ResolveStairsDrop()
    {
        var sm = SongManager.I;
        if (sm == null || ghost == null) { CleanupDrag(); return -1; }
        var drop = ghost.Snapshot();
        if (!drop.visible || drop.mode == IslandGhost.Mode.Blocked) { if (drop.visible) AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.2f); FlyBack(); CleanupDrag(); return -1; }
        CleanupDrag();
        int g = HandGroup(), col = -1;
        if (drop.mode == IslandGhost.Mode.Stack) col = drop.column;
        else if (drop.mode == IslandGhost.Mode.Merge && drop.target != null) col = drop.target.column + (drop.east ? 1 : 0);
        else if (drop.mode == IslandGhost.Mode.Insert) col = drop.insertAt;
        else col = sm.ColumnCount;
        int at;
        if (col < 0 || col >= sm.ColumnCount) at = sm.AddStairIsland(-1, false, g);
        else if (drop.mode != IslandGhost.Mode.Stack) at = SongOps.InsertStairColumn(col, g);   // a new column there (O)
        else
        {
            // the island of that column nearest the slot: the stairs go above (behind) or below (in front of) it
            KeyBlock near = null; float best = float.MaxValue;
            foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && !kb.IsPhrase && kb.column == col) { float d = Mathf.Abs(kb.pz - drop.pz); if (d < best) { best = d; near = kb; } }
            int idx = near != null ? sm.Islands.IndexOf(near) : -1;
            at = sm.AddStairIsland(idx, near != null && drop.pz > near.pz, g);
            if (at < 0) at = sm.AddStairIsland(-1, false, g);
        }
        if (at < 0) { if (stairsBtn != null) stairsBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return -1; }
        AfterStairs(at);
        return at;
    }

    /// <summary>A click: a one-measure melody phrase at the focused column (SongOps.AddPhrase: the hand picks up the cube, ready to draw).</summary>
    void AddMelodyCard()
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || ghost != null || melodyDrag) return;
        AddPhraseAt(FocusedColumn());
    }

    int AddPhraseAt(int col)
    {
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return -1;
        int at = SongOps.AddPhrase(Mathf.Clamp(col, 0, Mathf.Max(0, sm.ColumnCount - 1)), HandGroup());
        if (at < 0) { if (melodyBtn != null) melodyBtn.Pulse(); AudioPool.UI(ProceduralAudio.Thud(), 0.35f, 1.3f); return -1; }
        History.Push();   // (deduplicated: the op pushed already)
        PhrasesAdded++;
        if (at < sm.Islands.Count && sm.Islands[at] != null)
        {
            if (UIManager.I != null) UIManager.I.SelectMeasure(at, false);
            if (OrbitCamera.I != null) OrbitCamera.I.FrameIsland(sm.Islands[at]);
        }
        if (melodyBtn != null) melodyBtn.Pulse();
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.1f);
        dirty = true;
        Onboarding.Notify(Onboarding.Ev.PhraseAdded);
        Onboarding.Notify(Onboarding.Ev.IslandPlaced);
        return at;
    }

    void BeginMelodyDrag()
    {
        if (ghost != null) CancelDrag();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || melodyBtn == null || melodyDrag) return;
        melodyDrag = true; melodyDropCol = -1;
        floatCard = MakeFloatOf(melodyBtn.transform as RectTransform, new Vector2(52f, 52f));
        if (floatCard != null) { floatCard.gameObject.SetActive(true); floatCard.anchoredPosition = ToCanvas(Input.mousePosition); }
        WorldInput.Lock("tray");
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f);
    }

    void UpdateMelodyDrag(Vector3 mp)
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { FlyBack(); CleanupMelodyDrag(); return; }
        if (floatCard != null) floatCard.anchoredPosition = ToCanvas(mp);
        Vector3 g;
        SetMelodyDrop(!InputUtil.PointerOverUI && GroundAt(mp, out g) ? ColumnAtX(g.x) : -1);
    }

    void SetMelodyDrop(int col)
    {
        if (col == melodyDropCol) return;
        melodyDropCol = col;
        var rail = UIManager.I != null ? UIManager.I.Rail : null;
        if (rail != null) rail.PhraseGhostColumn = col;
        var sm = SongManager.I;
        if (col >= 0 && sm != null)
        {
            float z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var kb in sm.Islands) if (kb != null && kb.column == col) { z0 = Mathf.Min(z0, kb.FrontEdge); z1 = Mathf.Max(z1, kb.BackEdge); }
            if (z0 < z1) ColumnBands.ShowRange(col, z0, z1 + 4f);   // the melody tracks lie behind the lanes
        }
        else ColumnBands.ClearRange();
    }

    void EndMelodyDrag()
    {
        if (!melodyDrag || simDrag) return;
        ResolveMelodyDrop();
    }

    int ResolveMelodyDrop()
    {
        int col = melodyDropCol;
        if (col < 0) { FlyBack(); CleanupMelodyDrag(); return -1; }
        CleanupMelodyDrag();
        return AddPhraseAt(col);
    }

    void CleanupMelodyDrag()
    {
        if (floatCard != null) { Destroy(floatCard.gameObject); floatCard = null; }
        SetMelodyDrop(-1);
        melodyDrag = false; simDrag = false;
        WorldInput.Unlock("tray");
    }

    // ------------------------------------------------------------------ dragging a card into the world
    internal void BeginCardDrag(TrayCard c)
    {
        if (ghost != null) CancelDrag();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return;
        dragCard = c;
        ghost = IslandGhost.Create(c.data);
        ghost.SetVisible(false);
        floatCard = MakeFloat(c);
        c.SetDragged(true);
        WorldInput.Lock("tray");                          // SPEC v3 §3.1: no world picking / camera input while a ghost is live
        AudioPool.UI(ProceduralAudio.Tick(), 0.25f, 1.2f);
    }

    void UpdateDrag()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { FlyBack(); CleanupDrag(); return; }
        Vector3 mp = Input.mousePosition;
        Vector3 g;
        if (!InputUtil.PointerOverUI && GroundAt(mp, out g))
        {
            ghost.SetVisible(true);
            ghost.Track(g);
            if (floatCard != null) floatCard.gameObject.SetActive(false);
        }
        else
        {
            ghost.SetVisible(false);
            if (floatCard != null) { floatCard.gameObject.SetActive(true); floatCard.anchoredPosition = ToCanvas(mp); }
        }
    }

    internal void EndCardDrag(TrayCard c)
    {
        if (ghost == null || c != dragCard || simDrag) return;
        ResolveDrop();
    }

    /// <summary>The release: IslandGhost.Snapshot decides (SPEC v4: STACK / INSERT / MERGE / BLOCKED, package K); blocked / over the HUD =
    /// the card flies back.</summary>
    int ResolveDrop()
    {
        var sm = SongManager.I;
        var c = dragCard;
        if (sm == null || ghost == null || c == null) { CleanupDrag(); return -1; }
        var drop = ghost.Snapshot();
        var mode = drop.mode;
        bool visible = drop.visible;
        if (!visible || mode == IslandGhost.Mode.Blocked) { if (visible) AudioPool.UI(ProceduralAudio.Thud(), 0.3f, 1.2f); FlyBack(); CleanupDrag(); return -1; }
        CleanupDrag();
        SongManager.MeasureData[] before;
        var md = Take(c, out before);
        int at = IslandGhost.Place(drop, md);
        if (at < 0) { deck = before; dirty = true; return -1; }
        AfterPlace(at, md, mode != IslandGhost.Mode.Merge);
        return at;
    }

    void CancelDrag() { FlyBack(); CleanupDrag(); }

    void CleanupDrag()
    {
        if (floatCard != null) { Destroy(floatCard.gameObject); floatCard = null; }
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
        if (dragCard != null) dragCard.SetDragged(false);
        dragCard = null; simDrag = false; toolDrag = false; stairsDrag = false;
        WorldInput.Unlock("tray");
    }

    /// <summary>The dragged card's copy flies back to its slot (0.22 s) and fades.</summary>
    void FlyBack()
    {
        if (floatCard == null) return;
        var f = floatCard; floatCard = null;
        RectTransform home = dragCard != null ? UIKit.RT(dragCard) : (stairsDrag && stairsBtn != null ? stairsBtn.transform as RectTransform : (toolDrag && kbBtn != null ? kbBtn.transform as RectTransform
            : (moonDrag && moonBtn != null ? moonBtn.transform as RectTransform : (melodyDrag && melodyBtn != null ? melodyBtn.transform as RectTransform : null))));
        if (home == null || !isActiveAndEnabled) { Destroy(f.gameObject); return; }
        if (!f.gameObject.activeSelf) { f.gameObject.SetActive(true); f.anchoredPosition = ToCanvas(Input.mousePosition); }
        StartCoroutine(Fly(f, home));
    }

    IEnumerator Fly(RectTransform f, RectTransform home)
    {
        Vector2 from = f.anchoredPosition;
        Vector3 w = home != null ? home.TransformPoint(home.rect.center) : Vector3.zero;
        Vector2 to = home != null ? ToCanvas(RectTransformUtility.WorldToScreenPoint(canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, w)) : from;
        var g = f.GetComponent<CanvasGroup>();
        float t = 0f;
        while (t < 1f && f != null)
        {
            t += Time.unscaledDeltaTime / 0.22f;
            f.anchoredPosition = Vector2.LerpUnclamped(from, to, Ease.OutCubic(t));
            if (g != null) g.alpha = 0.92f * (1f - Ease.InQuad(t));
            yield return null;
        }
        if (f != null) Destroy(f.gameObject);
    }

    RectTransform MakeFloat(TrayCard c) => MakeFloatOf(c.inner, c.small ? new Vector2(SmallW, SmallH) : new Vector2(CardW, CardH));

    /// <summary>A see-through copy of <paramref name="src"/> that follows the pointer over the HUD while a card (or the keyboard card) is dragged.</summary>
    RectTransform MakeFloatOf(RectTransform src, Vector2 size)
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null || src == null) return null;
        var copy = Instantiate(src.gameObject, canvas.transform);
        copy.name = "TrayDragCard";
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (mb is TrayTool || mb is HudButton || mb is InkCaptionHover) Destroy(mb);
        var rt = (RectTransform)copy.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size; rt.localScale = Vector3.one * 0.92f; rt.localRotation = Quaternion.Euler(0f, 0f, 4f);
        foreach (var gr in copy.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
        var cg = copy.AddComponent<CanvasGroup>(); cg.alpha = 0.92f; cg.blocksRaycasts = false; cg.interactable = false;
        copy.transform.SetAsLastSibling();
        copy.SetActive(false);
        return rt;
    }

    Vector2 ToCanvas(Vector2 screen)
    {
        if (canvas == null) return screen;
        var crt = (RectTransform)canvas.transform;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(crt, screen, cam, out lp);
        return lp;
    }

    static bool GroundAt(Vector3 screen, out Vector3 p)
    {
        p = Vector3.zero;
        var cam = Camera.main;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(screen);
        float enter;
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out enter) || enter <= 0f || enter > 2000f) return false;
        p = ray.GetPoint(enter);
        return true;
    }

    // ------------------------------------------------------------------ tests (legacy Input cannot be faked)
    /// <summary>Tests: rebuilds the hand now (cards reflect the current deck / key).</summary>
    public void RebuildNow() { Rebuild(); }
    /// <summary>v7 tests: the cards in the hand and card <paramref name="index"/>'s chord data (null out of range).</summary>
    public int CardCount => cards.Count;
    public SongManager.MeasureData CardData(int index) => index >= 0 && index < cards.Count && cards[index] != null ? cards[index].data : null;
    /// <summary>Tests: a click on card <paramref name="index"/> (Cards order). Returns the new island's index or -1.</summary>
    public int SimClick(int index)
    {
        if (index < 0 || index >= cards.Count) return -1;
        var sm = SongManager.I; int before = sm != null ? sm.Islands.Count : 0;
        OnCardClick(cards[index]);
        return sm != null && sm.Islands.Count > before ? (UIManager.I != null ? UIManager.I.SelectedMeasure : -1) : -1;
    }
    /// <summary>Tests: starts dragging card <paramref name="index"/> (a ghost that only SimDragAt moves).</summary>
    public IslandGhost SimDragStart(int index)
    {
        if (index < 0 || index >= cards.Count) return null;
        BeginCardDrag(cards[index]);
        simDrag = ghost != null;
        return ghost;
    }
    /// <summary>Tests: the pointer's sea point (the ghost's centre goes there).</summary>
    public void SimDragAt(Vector3 ground) { if (ghost == null) return; ghost.SetVisible(true); ghost.Track(ground); if (floatCard != null) floatCard.gameObject.SetActive(false); }
    /// <summary>Tests: releases the synthetic drag; returns the placed island's index or -1 (flew back).</summary>
    public int SimDragRelease() { if (ghost == null) return -1; return stairsDrag ? ResolveStairsDrop() : (toolDrag ? ResolveKeyboardDrop() : ResolveDrop()); }
    /// <summary>Tests: the pointer enters card <paramref name="index"/> (hover audition rules).</summary>
    public void SimHover(int index) { if (index < 0 || index >= cards.Count) return; auditionHold = 0f; OnCardHover(cards[index]); }
    /// <summary>Tests / captures: holds a stack (1 Key, 2 Colour) fanned out as if hovered.</summary>
    public void SimFan(int section, bool on) { forcedFan = on ? section : 0; }
    int forcedFan;
    /// <summary>Tests: invokes a tool (0 dice, 1 duplicate, 2 wand, 3 the Moon card, 4 the keyboard card).</summary>
    public void SimTool(int i) { if (i == 0) RollDice(); else if (i == 1) DuplicateFocused(); else if (i == 2) PlaceWholeDeck(); else if (i == 3) AddMoonCard(); else if (i == 4) AddKeyboardCard(); else if (i == 5) AddStairsCard(); else AddMelodyCard(); }
    /// <summary>v7 tests: starts dragging the stairs card (a ghost that only SimDragAt moves; SimDragRelease drops it).</summary>
    public IslandGhost SimStairsDragStart() { BeginStairsDrag(); simDrag = ghost != null; return ghost; }
    /// <summary>v7 tests: drags the melody card to sea point <paramref name="ground"/> and releases it there (returns the new phrase's index or -1).</summary>
    public int SimMelodyDrop(Vector3 ground)
    {
        BeginMelodyDrag();
        if (!melodyDrag) return -1;
        simDrag = true;
        SetMelodyDrop(ColumnAtX(ground.x));
        simDrag = false;
        return ResolveMelodyDrop();
    }
    /// <summary>Tests: drags the Moon card to sea point <paramref name="ground"/> and releases it there (returns the new Moon's index or -1).</summary>
    public int SimMoonDrop(Vector3 ground)
    {
        BeginMoonDrag();
        if (!moonDrag) return -1;
        simDrag = true;
        SetMoonDrop(ColumnAtX(ground.x));
        int col = moonDropCol;
        simDrag = false;
        int i = ResolveMoonDrop();
        return i >= 0 && col >= 0 ? i : -1;
    }
    /// <summary>Tests: the column a dragged Moon card points at now (-1 none).</summary>
    public int MoonDropColumn => moonDrag ? moonDropCol : -1;
    /// <summary>Tests: starts dragging the keyboard card (a ghost that only SimDragAt moves; SimDragRelease drops it).</summary>
    public IslandGhost SimKeyboardDragStart() { BeginKeyboardDrag(); simDrag = ghost != null; return ghost; }
    /// <summary>Tests / captures: holds card <paramref name="index"/> as hovered for the swap chips (-1 releases).</summary>
    public void SimSwapHover(int index) { forcedSwap = index >= 0 && index < cards.Count ? cards[index] : null; }
    /// <summary>Tests: recomputes the stars now.</summary>
    public void RefreshStarsNow() { RefreshStars(); }
}

/// <summary>One deck card: hover lifts it 14 px and tilts it toward the pointer (on twos) and auditions its chord; click places it; a drag
/// takes its ghost into the world. Its slot (position, tilt, scale) comes from the hand's layout; it glides there on twos.</summary>
public class TrayCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public SongManager.MeasureData data;
    public int deckIndex = -1, section, baseSibling, rank;
    public bool small, fanned;
    public RectTransform inner;
    [NonSerialized] public Vector2 slotPos; [NonSerialized] public float slotRot, slotScale = 1f;
    // v5 (SPEC §7): what the face shows — its vibe, its glyph (IconFactory name), the house sticker on the key's home chord, the paper and the caption
    [NonSerialized] public VibeKind vibe; [NonSerialized] public bool home; [NonSerialized] public string glyph, caption; [NonSerialized] public Color paper;
    // v6 (SPEC §7.1): its job badge, its fit star (the fit with the tray's reference, starred at ≥ Harmony.StarThreshold, the best one twinkles)
    [NonSerialized] public int job; [NonSerialized] public RectTransform badge, star; [NonSerialized] public float fit; [NonSerialized] public bool starred, best;
    Color chord; IslandTray tray; CanvasGroup cg;
    float lift, pulse, tilt; bool hover, dragging, dragged; int tick = int.MinValue; InkShape body;
    /// <summary>The card's chord colour (MusicTheory.ChordColor = Vibe.ChordColor of its chord under the song's climate).</summary>
    public Color ChordColor => chord;
    public bool Hovered => hover;
    public bool IsDragged => dragged;
    /// <summary>The card glides to its slot on twos (true while it is not there yet).</summary>
    public bool Animating { get; private set; }

    public void Init(IslandTray t, SongManager.MeasureData md, int deckIdx, int sec, RectTransform innerRt, Color chordColor)
    {
        tray = t; data = md; deckIndex = deckIdx; section = sec; inner = innerRt; chord = chordColor;
        cg = gameObject.AddComponent<CanvasGroup>();
        body = innerRt != null ? innerRt.GetComponent<InkShape>() : null;
        slotPos = ((RectTransform)transform).anchoredPosition; slotScale = 1f;
    }

    public void Pulse() { pulse = 1f; }
    public void SetDragged(bool on) { dragged = on; if (cg != null) cg.alpha = on ? 0.35f : 1f; }

    /// <summary>v6: shows / hides the fit star (<paramref name="isBest"/>: this card's star twinkles).</summary>
    public void SetStar(bool on, bool isBest, float f)
    {
        fit = f; starred = on; best = on && isBest;
        if (star == null) return;
        if (star.gameObject.activeSelf != on) star.gameObject.SetActive(on);
        if (!best) { star.localScale = Vector3.one; star.localEulerAngles = Vector3.zero; SetGlint(0.85f); }
    }
    void SetGlint(float a)
    {
        var g = star != null ? star.Find("Glint") : null; var img = g != null ? g.GetComponent<Image>() : null;
        if (img != null && Mathf.Abs(img.color.a - a) > 0.01f) img.color = new Color(1f, 1f, 1f, a);
    }

    void Update()
    {
        var rt = (RectTransform)transform;
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float dt = 1f / Look.TwosFps;
        // glide to the slot (the fan closes a dragged card's gap on twos)
        Vector2 at = rt.anchoredPosition;
        Animating = (at - slotPos).sqrMagnitude > 0.25f;
        if (Animating) rt.anchoredPosition = Vector2.Lerp(at, slotPos, 0.55f);
        float rz = Mathf.LerpAngle(rt.localEulerAngles.z, slotRot, 0.55f);
        rt.localEulerAngles = new Vector3(0f, 0f, rz);
        float s = Mathf.Lerp(rt.localScale.x, slotScale, 0.55f);
        rt.localScale = new Vector3(s, s, 1f);
        // hover: lift, tilt toward the pointer, boil
        bool up = hover && !dragging && !dragged;
        lift = Mathf.MoveTowards(lift, up ? 14f : 0f, 14f * dt / 0.12f);
        float want = 0f;
        if (up) { Vector2 lp; if (HudKit.Local(rt, Input.mousePosition, out lp)) want = Mathf.Clamp(-lp.x / Mathf.Max(1f, rt.rect.width * 0.5f) * 5f, -5f, 5f); }
        tilt = Mathf.MoveTowards(tilt, want, 5f * dt / 0.1f);
        pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.35f);
        if (inner != null)
        {
            inner.anchoredPosition = new Vector2(0f, lift);
            inner.localEulerAngles = new Vector3(0f, 0f, tilt);
            inner.localScale = Vector3.one * (1f + 0.06f * pulse + (up ? 0.03f : 0f));
        }
        if (body != null) body.Boil = up;
        // v6: the best card's star twinkles — a pop and a glint once a second, rocking on twos
        if (best && star != null)
        {
            int k = t % 12;
            float ts = k == 0 ? 1.26f : (k == 1 ? 1.12f : (k == 6 ? 1.07f : 1f));
            star.localScale = new Vector3(ts, ts, 1f);
            star.localEulerAngles = new Vector3(0f, 0f, k < 6 ? -7f : 7f);
            SetGlint(k < 2 || k == 6 ? 1f : 0.45f);
        }
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (dragged) return;
        hover = true;
        transform.SetAsLastSibling();   // the whole card shows above its fan neighbours
        if (tray != null && !e.dragging) tray.OnCardHover(this);
    }

    public void OnPointerExit(PointerEventData e)
    {
        hover = false;
        if (transform.parent != null) transform.SetSiblingIndex(Mathf.Min(baseSibling, transform.parent.childCount - 1));
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || dragging || e.dragging || dragged) return;
        if (section != 0 && !fanned) return;   // a collapsed stack: the hover fans it out first
        pulse = 1f;
        if (tray != null) tray.OnCardClick(this);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || tray == null) return;
        dragging = true; hover = false;
        tray.BeginCardDrag(this);
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        if (tray != null) tray.EndCardDrag(this);
    }
}

/// <summary>A deck tool sticker (dice, duplicate, wand, the Moon card): an ink sticker with a picture; hover boils it and squashes it on
/// twos; a click acts. Not a HudButton (the HUD's button sweeps must not place islands).</summary>
public class TrayTool : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public InkShape body; public InkPainter art;
    public Action onClick;
    /// <summary>v6: a tool that can also be dragged into the world (the keyboard card): begin / end of the drag.</summary>
    public Action onBeginDrag, onEndDrag;
    public bool dim;
    float scale = 1f, pulse; bool hover, dimShown, dragging; int tick = int.MinValue;
    public bool Hover => hover;

    public void Pulse() { pulse = 1.4f; }

    void Update()
    {
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float dt = 1f / Look.TwosFps;
        scale = Mathf.Lerp(scale, hover ? 1.1f : 1f, 0.6f);
        pulse = Mathf.MoveTowards(pulse, 0f, dt / 0.3f);
        transform.localScale = Vector3.one * (scale * (1f + 0.16f * pulse));
        if (dim != dimShown) { dimShown = dim; if (art != null) art.color = new Color(1f, 1f, 1f, dim ? 0.4f : 1f); if (body != null) body.color = dim ? new Color(0.85f, 0.82f, 0.86f) : Comic.Cream; }
    }

    public void OnPointerEnter(PointerEventData e) { hover = true; if (body != null) body.Boil = true; if (art != null) art.Boil = true; }
    public void OnPointerExit(PointerEventData e) { hover = false; if (body != null) body.Boil = false; if (art != null) art.Boil = false; }
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || e.dragging || dragging) return;
        pulse = 1f;
        AudioPool.UI(ProceduralAudio.Tick(), 0.3f);
        onClick?.Invoke();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (onBeginDrag == null || e.button != PointerEventData.InputButton.Left) return;
        dragging = true; hover = false;
        onBeginDrag();
    }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        onEndDrag?.Invoke();
    }
}
