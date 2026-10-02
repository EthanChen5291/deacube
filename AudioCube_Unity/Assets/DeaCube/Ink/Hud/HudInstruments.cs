using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 world HUD, left edge (research §4.1 "the instrument column", replaces the 30-control palette): ten 40 px CubeGlyph chips in one
/// frameless column 14 px from the edge (Townscaper). Muted = hollow; used in the song = a small ink shadow under the chip; hover = it hops
/// and auditions with a caption. The scroll wheel over a chip changes its volume at any time (a ring meter shows while scrolling; one History
/// entry per scroll). Mix mode (the fader sticker under the column; it or Esc closes it): every chip grows a horizontal ink tongue (120 px)
/// whose filled length is the volume (drag anywhere on it), a speaker at its end toggles mute, and a chip click toggles mute. v3 contract
/// kept: HUD/Palette holds ten HudButtons "Pick" (instrument order) and ten "Mute"; hints palette and swatch.&lt;i&gt;.
/// v6 (SPEC v6 §8.1, package U2) — "players should actively select the cube to go into placing mode" and "the instruments expanded into
/// groups": the ten chips are the ten instrument GROUPS and the column is where a cube is picked up. A chip click PICKS UP that group's cube
/// (PathManager.PickUpCube: placing mode) and the chip lifts out of the column "in the hand" — out to the right and up, 1.2×, the ink
/// hexagon, a hard ink shadow under it and a slow stepped sway; a second click (or Esc, or a right-click on the sea: PathManager) puts it down.
/// Hovering a chip ≈ 0.25 s fans the group's VOICES out to the right on a paper tongue: one small cube per voice in the group colour with its
/// variant mark (<see cref="VoiceMark"/> pips; the first voice plain), hover = that voice auditions and its name is the caption, click = pick
/// up that voice (Instruments.BrushVoice). The group's brush voice shows as pips on its chip. While inspecting a chip click still recolours
/// the inspected cube (v3 F22); mix mode and the scroll-wheel volume are unchanged (group volume / mute).
/// </summary>
public class HudInstruments : MonoBehaviour
{
    public static HudInstruments I;
    public const float Chip = 40f, Gap = 8f, Edge = 14f, TongueW = 120f;
    /// <summary>v6: hover this long on a chip to fan its voices out; the fan stays this long after the pointer left chip and fan.</summary>
    public const float FanDelay = 0.25f, FanGrace = 0.3f;
    /// <summary>v6: the voice fan's chip pitch / drawn cube (px at the 1920 × 1080 reference); the hit area is 36 × 42.</summary>
    public const float FanPitch = 38f, FanCube = 30f, MaxVoices = 12;
    /// <summary>v6: how far the held chip lifts out of the column (right, up) and its hard shadow's offset.</summary>
    public static readonly Vector2 HeldLift = new Vector2(18f, 6f), HeldShadow = new Vector2(8f, -10f);
    public static readonly string[] Names = { "keys", "pluck", "pad", "lead", "bass", "bells", "strings", "choir", "piano", "drums", "fx" };   // v9 (G): + fx

    public bool Mix => mix;
    public RectTransform Root => root;
    public HudButton MixButton => mixBtn;
    public CubeGlyph ChipOf(int i) => i >= 0 && i < chips.Length ? chips[i] : null;
    public HudButton PickOf(int i) => i >= 0 && i < picks.Length ? picks[i] : null;
    public HudButton MuteOf(int i) => i >= 0 && i < mutes.Length ? mutes[i] : null;
    public HudScroll ScrollOf(int i) => i >= 0 && i < scrolls.Length ? scrolls[i] : null;
    public HudDrag TongueOf(int i) => i >= 0 && i < drags.Length ? drags[i] : null;
    /// <summary>SPHERE (DeaCube/SphereBody.cs): the shape switch above the column — its cube (false) / ball (true) button.</summary>
    public HudButton ShapeButton(bool round) => round ? shapeBall : shapeCube;
    /// <summary>True while the chips (and the switch) show the ball: the sphere shape is chosen.</summary>
    public bool ChipsRound => roundShown;
    /// <summary>Instruments used by the song's cubes (a shadow under their chips).</summary>
    public bool Used(int i) => i >= 0 && i < used.Length && used[i];

    // ---- v6 tests / other packages
    /// <summary>The group whose cube is in the hand (its chip is lifted), -1 when the hand holds no cube.</summary>
    public int Held => held;
    /// <summary>The lift progress 0..1 of chip <paramref name="i"/> (1 = fully "in the hand").</summary>
    public float LiftOf(int i) => i >= 0 && i < lift.Length ? lift[i] : 0f;
    /// <summary>The chip's row offset from its column slot (px), its current tilt (deg) and whether its hard shadow shows.</summary>
    public Vector2 RowOffset(int i) => i >= 0 && i < rows.Length && rows[i] != null ? rows[i].anchoredPosition - new Vector2(Chip * 0.5f, rowY[i]) : Vector2.zero;
    public float ChipAngle(int i) => i >= 0 && i < chips.Length && chips[i] != null ? Mathf.DeltaAngle(0f, chips[i].rectTransform.localEulerAngles.z) : 0f;
    public bool ShadowShown(int i) => i >= 0 && i < shadows.Length && shadows[i] != null && shadows[i].gameObject.activeSelf;
    /// <summary>The voice whose mark chip <paramref name="i"/> wears (its group's brush voice; 0 = plain).</summary>
    public int MarkVoice(int i) => i >= 0 && i < markShown.Length ? Mathf.Max(0, markShown[i]) : 0;
    public bool FanOpen => fanGroup >= 0;
    public int FanGroup => fanGroup;
    public int FanCount => fanGroup >= 0 ? fanN : 0;
    public HudButton FanChip(int v) => fanGroup >= 0 && v >= 0 && v < fanN ? fanBtns[v] : null;
    public RectTransform FanPaper => fanPaper != null ? fanPaper.rectTransform : null;
    /// <summary>Voice auditions the column played (chip hovers + fan hovers; tests).</summary>
    public static int AuditionCount { get; private set; }
    /// <summary>Tests: pretend every group has this many voices (0 = ask Instruments.VoiceCount) — the fan and the card's voice row lay out 1..12.</summary>
    public static int TestVoices;
    /// <summary>Voices of group <paramref name="g"/> as the UI shows them (1..12).</summary>
    public static int VoicesOf(int g) => Mathf.Clamp(TestVoices > 0 ? TestVoices : Instruments.VoiceCount(Mathf.Clamp(g, 0, Instruments.Count - 1)), 1, (int)MaxVoices);
    /// <summary>The voice a new cube of group <paramref name="g"/> gets, inside the group (out of range: its first sound).</summary>
    public static int BrushOf(int g) { g = Mathf.Clamp(g, 0, Instruments.Count - 1); int v = Instruments.BrushVoice[g]; return v >= 0 && v < VoicesOf(g) ? v : 0; }

    RectTransform root; CanvasGroup group;
    readonly RectTransform[] rows = new RectTransform[Instruments.Count];
    readonly float[] rowY = new float[Instruments.Count];
    readonly CubeGlyph[] chips = new CubeGlyph[Instruments.Count], shadows = new CubeGlyph[Instruments.Count];
    readonly HudButton[] picks = new HudButton[Instruments.Count], mutes = new HudButton[Instruments.Count];
    readonly InkPainter[] meters = new InkPainter[Instruments.Count], tongues = new InkPainter[Instruments.Count], muteArt = new InkPainter[Instruments.Count];
    readonly InkPainter[] marks = new InkPainter[Instruments.Count];
    readonly HudScroll[] scrolls = new HudScroll[Instruments.Count];
    readonly HudDrag[] drags = new HudDrag[Instruments.Count];
    readonly float[] lift = new float[Instruments.Count], meterA = new float[Instruments.Count], tongueT = new float[Instruments.Count];
    readonly float[] hopStart = new float[Instruments.Count], hopHeight = new float[Instruments.Count];
    readonly bool[] used = new bool[Instruments.Count], mutedShown = new bool[Instruments.Count], usedShown = new bool[Instruments.Count];
    readonly int[] markShown = new int[Instruments.Count];
    readonly float[] volShown = new float[Instruments.Count];
    HudButton mixBtn; InkHover mixHover;
    HudButton shapeCube, shapeBall; CubeGlyph shapeCubeG, shapeBallG; bool roundShown; int shapeColShown = -1;
    bool mix, usedDirty = true, subscribed, hidden; int held = -1, tick = int.MinValue;
    float lastAuditionT = -9f; int lastAuditionIdx = -1; float heldSince;
    // the voice fan
    RectTransform fanRoot; InkShape fanPaper; HudHoverZone fanZone; CanvasGroup fanGroupCg;
    readonly RectTransform[] fanSlots = new RectTransform[(int)MaxVoices];
    readonly HudButton[] fanBtns = new HudButton[(int)MaxVoices];
    readonly InkPainter[] fanArt = new InkPainter[(int)MaxVoices];
    readonly int[] fanVoice = new int[(int)MaxVoices];
    int fanGroup = -1, fanN, fanStep = -1, hoverIdx = -1, fanBrushShown = -1; float hoverStart, fanLeaveT, fanT; bool fanForced;
    float lastVoiceAudT = -9f; int lastVoiceAud = -1;

    public static HudInstruments Build(RectTransform hud)
    {
        float h = Instruments.Count * Chip + (Instruments.Count - 1) * Gap;
        var rt = HudKit.Node(hud, "Palette", new Vector2(0f, 0.5f), new Vector2(Edge, 18f), new Vector2(Chip + 20f, h + 60f));
        rt.pivot = new Vector2(0f, 0.5f);
        var c = rt.gameObject.AddComponent<HudInstruments>();
        c.root = rt;
        c.BuildParts(h);
        return c;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; Unsubscribe(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { AuditionCount = 0; TestVoices = 0; }

    void Subscribe()
    {
        if (subscribed) return;
        AudioCube.OnAnyChanged += MarkUsed; History.OnChanged += MarkUsed; Instruments.OnChanged += MarkUsed;
        if (SongManager.I != null) SongManager.I.OnSongRebuilt += MarkUsed;
        subscribed = SongManager.I != null;
        if (!subscribed) { AudioCube.OnAnyChanged -= MarkUsed; History.OnChanged -= MarkUsed; Instruments.OnChanged -= MarkUsed; }
    }
    void Unsubscribe()
    {
        if (!subscribed) return;
        AudioCube.OnAnyChanged -= MarkUsed; History.OnChanged -= MarkUsed; Instruments.OnChanged -= MarkUsed;
        if (SongManager.I != null) SongManager.I.OnSongRebuilt -= MarkUsed;
        subscribed = false;
    }
    void MarkUsed(AudioCube c) { usedDirty = true; }
    void MarkUsed() { usedDirty = true; }

    void BuildParts(float h)
    {
        group = root.gameObject.AddComponent<CanvasGroup>();
        for (int i = 0; i < Instruments.Count; i++)
        {
            int idx = i;
            float y = h * 0.5f - Chip * 0.5f - i * (Chip + Gap);
            rowY[i] = y;
            var row = HudKit.Node(root, "Inst" + i, new Vector2(0f, 0.5f), new Vector2(Chip * 0.5f, y), new Vector2(Chip, Chip));
            rows[i] = row;
            // the ring meter behind the chip (only while scrolling)
            var mrt = HudKit.Node(row, "Meter", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Chip + 22f, Chip + 22f));
            meters[i] = mrt.gameObject.AddComponent<InkPainter>(); meters[i].raycastTarget = false;
            meters[i].onPaint = p => PaintMeter(p, idx);
            mrt.gameObject.SetActive(false);
            // v6: the hard ink shadow of a chip in the hand (under the chip, only while held)
            var sh = CubeGlyph.Create(row, "HeldShadow", Comic.A(Comic.Ink, 0.92f), Chip);
            sh.raycastTarget = false; sh.Ink = Comic.Ink;
            sh.gameObject.SetActive(false);
            shadows[i] = sh;
            // the chip (the pick) — hover auditions, the wheel sets the volume
            var crt = HudKit.Node(row, "Pick", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Chip, Chip));
            var g = crt.gameObject.AddComponent<CubeGlyph>();
            g.color = Comic.Opaque(Instruments.Colors[i]);
            chips[i] = g;
            var b = HudKit.Control(g, Names[i], () => ClickChip(idx));
            b.onHover = () => { Hop(idx, 0.3f); Audition(idx); };
            b.GetComponent<InkHover>().hoverScale = 1.08f;
            picks[i] = b;
            // v6: the brush voice's pips on the chip's top face (they follow its hop)
            var mk = HudKit.Stretch(crt, "Mark");
            marks[i] = mk.gameObject.AddComponent<InkPainter>(); marks[i].raycastTarget = false;
            marks[i].onPaint = p => PaintMark(p, idx);
            markShown[i] = -1;
            var sc = crt.gameObject.AddComponent<HudScroll>();
            sc.stepPerUnit = 1f;
            sc.onStep = s => { Instruments.SetVolume(idx, Mathf.Clamp01(Instruments.Volume[idx] + 0.05f * s)); meterA[idx] = 1f; ShowMeter(idx); };
            sc.onCommit = History.Push;
            scrolls[i] = sc;
            Hints.Register("swatch." + i, crt);
            // mix mode: the tongue (drag = volume) and the speaker at its end (mute)
            var trt = HudKit.Node(row, "Tongue", new Vector2(0.5f, 0.5f), new Vector2(Chip * 0.5f + 12f + TongueW * 0.5f, 0f), new Vector2(TongueW, 28f));   // 28 px hit; the painted track stays 10 px
            tongues[i] = trt.gameObject.AddComponent<InkPainter>(); tongues[i].raycastTarget = true;
            tongues[i].onPaint = p => PaintTongue(p, idx);
            var dg = trt.gameObject.AddComponent<HudDrag>();
            dg.onPoint = lp => { float v = Mathf.Clamp01((lp.x + TongueW * 0.5f - 4f) / (TongueW - 8f)); Instruments.SetVolume(idx, v); tongues[idx].Repaint(); };
            dg.onRelease = History.Push;
            drags[i] = dg;
            InkCaption.Attach(trt.gameObject, Names[i] + " volume");
            var murt = HudKit.Node(row, "Mute", new Vector2(0.5f, 0.5f), new Vector2(Chip * 0.5f + 12f + TongueW + 18f, 0f), new Vector2(28f, 28f));
            muteArt[i] = murt.gameObject.AddComponent<InkPainter>();
            muteArt[i].onPaint = p => PaintSpeaker(p, Instruments.Muted[idx]);
            mutes[i] = HudKit.Control(muteArt[i], "mute", () => { Instruments.ToggleMute(idx); History.Push(); muteArt[idx].Repaint(); }, null, null, muteArt[i]);
            trt.gameObject.SetActive(false); murt.gameObject.SetActive(false);
            volShown[i] = -1f;
            hopStart[i] = -9f;
        }
        // the fader sticker under the column
        InkShape body; InkPainter art;
        mixBtn = HudKit.Sticker(root, "Mix", new Vector2(0f, 0.5f), new Vector2(Chip * 0.5f, -h * 0.5f - 24f), new Vector2(30f, 30f), InkShape.Kind.Sticker, Comic.Cream,
                                PaintFader, "mix", ToggleMix, out body, out art);
        body.SetInk(1.6f, 2.6f); body.ShadowOffset = new Vector2(3f, -3.5f);
        mixHover = mixBtn.GetComponent<InkHover>();
        BuildShapeSwitch(h);
        BuildFan();
        Hints.Register("palette", root);
        Hints.Register("mix", mixBtn.transform as RectTransform);
    }

    // ------------------------------------------------------------------ SPHERE: the shape switch (cube | ball) above the column
    void BuildShapeSwitch(float h)
    {
        var srt = HudKit.Node(root, "Shape", new Vector2(0f, 0.5f), new Vector2(Chip * 0.5f + 6f, h * 0.5f + 36f), new Vector2(66f, 36f));
        var paper = InkShape.Create(srt, "Paper", InkShape.Kind.Sticker, Comic.Cream, new Vector2(66f, 36f));
        var prt = paper.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        paper.raycastTarget = true;   // a press between the two never reaches the world
        paper.SetInk(1.6f, 2.6f); paper.ShadowOffset = new Vector2(3f, -3.5f); paper.Seed = 4471;
        shapeCubeG = ShapeGlyph(srt, false); shapeBallG = ShapeGlyph(srt, true);
        shapeCube = HudKit.Control(shapeCubeG, "cube", () => PickShape(false));
        shapeBall = HudKit.Control(shapeBallG, "ball", () => PickShape(true));
        shapeCube.GetComponent<InkHover>().hoverScale = 1.12f; shapeBall.GetComponent<InkHover>().hoverScale = 1.12f;
        Hints.Register("shape", srt);
        PaintShapeSwitch(false);
    }

    static CubeGlyph ShapeGlyph(RectTransform parent, bool round)
    {
        var g = CubeGlyph.Create(parent, round ? "Ball" : "Cube", Color.white, 30f);
        g.rectTransform.anchoredPosition = new Vector2(round ? 15f : -15f, 0f);
        g.Round = round; g.Scale = 0.72f;
        return g;
    }

    void PickShape(bool round)
    {
        var pm = PathManager.I;
        if (pm == null) return;
        if (pm.BrushSphere == round) { (round ? shapeBallG : shapeCubeG).Hop(0.25f); return; }
        pm.SetBrushShape(round);
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, round ? 1.3f : 1.05f);
        (round ? shapeBallG : shapeCubeG).Hop(round ? 0.6f : 0.35f);   // the ball bounces higher
    }

    /// <summary>The chosen shape is solid with the ink ring, the other an open outline; both in the colour the hand picks up.</summary>
    void PaintShapeSwitch(bool round)
    {
        var pm = PathManager.I;
        int g = pm != null ? Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1) : 0;
        Color c = Comic.Opaque(Instruments.Colors[g]);
        shapeColShown = g;
        shapeCubeG.color = c; shapeBallG.color = c;
        shapeCubeG.Hollow = round; shapeCubeG.Ring = !round;
        shapeBallG.Hollow = !round; shapeBallG.Ring = round;
    }

    // ------------------------------------------------------------------ v6 the voice fan (built once; up to 12 pooled chips)
    void BuildFan()
    {
        fanRoot = HudKit.Node(root, "VoiceFan", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 52f));
        fanRoot.pivot = new Vector2(0f, 0.5f);
        fanGroupCg = fanRoot.gameObject.AddComponent<CanvasGroup>();
        fanPaper = InkShape.Create(fanRoot, "Paper", InkShape.Kind.Bubble, Comic.Cream, new Vector2(100f, 52f));
        var prt = fanPaper.rectTransform; prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
        fanPaper.raycastTarget = true;   // the paper swallows the pointer between the chips (no tile under the fan is pressed)
        fanPaper.Radius = 16f; fanPaper.SetInk(2f, 3.2f); fanPaper.ShadowOffset = Comic.StickerShadow; fanPaper.ShadowColor = Comic.PrintShadow;
        fanPaper.TailWidth = 16f; fanPaper.Wobble = 1f; fanPaper.Seed = 6607;
        fanZone = fanPaper.gameObject.AddComponent<HudHoverZone>();
        for (int v = 0; v < (int)MaxVoices; v++)
        {
            int vv = v;
            var slot = HudKit.Node(fanRoot, "Slot" + v, new Vector2(0f, 0.5f), new Vector2(14f + FanPitch * (v + 0.5f), 0f), new Vector2(36f, 42f));
            fanSlots[v] = slot;
            var hit = HudKit.Hit(slot, "Voice" + v, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 42f));
            var art = HudKit.Node(hit.rectTransform, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 42f));
            fanArt[v] = art.gameObject.AddComponent<InkPainter>(); fanArt[v].raycastTarget = false;
            fanArt[v].onPaint = p => PaintFanChip(p, vv);
            var b = HudKit.Control(hit, "voice", () => ClickVoice(vv), null, art, fanArt[v]);
            b.onHover = () => AuditionVoice(vv);
            b.GetComponent<InkHover>().hoverScale = 1.14f;
            fanBtns[v] = b;
            slot.gameObject.SetActive(false);
        }
        fanRoot.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ behaviour
    void ClickChip(int i)
    {
        if (mix) { Instruments.ToggleMute(i); History.Push(); if (muteArt[i] != null) muteArt[i].Repaint(); return; }
        if (UIManager.I != null) UIManager.I.PickInstrument(i);   // inspecting: recolour (F22); else the hand toggle
        else ToggleHand(i, -1);
    }

    /// <summary>v6: picks up group <paramref name="i"/>'s cube (<paramref name="voice"/> -1 = its brush voice), or — when
    /// <paramref name="mayPutDown"/> and that group's cube is already in the hand — puts it down (a voice pick never puts down). Returns true
    /// when the hand now holds the cube.</summary>
    public static bool ToggleHand(int i, int voice, bool mayPutDown = true)
    {
        var pm = PathManager.I;
        if (pm == null) return false;
        i = Mathf.Clamp(i, 0, Instruments.Count - 1);
        if (mayPutDown && voice < 0 && pm.CubeInHand && pm.selectedInstrument == i)
        {
            pm.PutDown();
            AudioPool.UI(ProceduralAudio.Tick(), 0.3f, 0.8f);
            return false;
        }
        pm.PickUpCube(i, voice);
        AudioPool.UI(ProceduralAudio.Pop(), 0.3f, 1.15f + 0.03f * i);
        if (I != null) I.Hop(i, 0.5f);
        return pm.CubeInHand;
    }

    void Hop(int i, float h)
    {
        if (i < 0 || i >= chips.Length || chips[i] == null) return;
        chips[i].Hop(h);
        hopStart[i] = Time.unscaledTime; hopHeight[i] = h;
        if (markShown[i] > 0) marks[i].Repaint();
    }

    void Audition(int i)
    {
        if (i == lastAuditionIdx && Time.unscaledTime - lastAuditionT < 0.3f) return;
        lastAuditionIdx = i; lastAuditionT = Time.unscaledTime;
        AuditionCount++;
        Instruments.Audition(i, BrushOf(i));
    }

    void AuditionVoice(int v)
    {
        if (fanGroup < 0 || v >= fanN) return;
        if (v == lastVoiceAud && Time.unscaledTime - lastVoiceAudT < 0.3f) return;
        lastVoiceAud = v; lastVoiceAudT = Time.unscaledTime;
        AuditionCount++;
        Instruments.Audition(fanGroup, v);
        fanArt[v].Repaint();
    }

    void ClickVoice(int v)
    {
        if (fanGroup < 0 || v >= fanN) return;
        int g = fanGroup;
        if (CubeInspector.IsOpen) return;   // (the column steps aside while inspecting; the card has its own voice row)
        ToggleHand(g, v);
        RepaintFan();
    }

    public void ToggleMix() { SetMix(!mix); }

    public void SetMix(bool on)
    {
        if (mix == on) return;
        mix = on;
        if (on) CloseFan();
        if (mixHover != null) mixHover.active = on;
        for (int i = 0; i < Instruments.Count; i++)
        {
            tongueT[i] = 0f;
            HudKit.SetActive(tongues[i], on); HudKit.SetActive(muteArt[i], on);
            if (on) { tongues[i].rectTransform.localScale = new Vector3(0.05f, 1f, 1f); tongues[i].Repaint(); muteArt[i].Repaint(); }
        }
        AudioPool.UI(on ? ProceduralAudio.Pop() : ProceduralAudio.Tick(), 0.25f, on ? 1.1f : 0.9f);
    }

    void ShowMeter(int i) { HudKit.SetActive(meters[i], true); meters[i].Repaint(); }

    /// <summary>The inspector is open: the column steps aside (research §4.1).</summary>
    public void SetHidden(bool on)
    {
        if (hidden == on) return;
        hidden = on;
        if (on && mix) SetMix(false);
        if (on) CloseFan();
        group.blocksRaycasts = !on; group.interactable = !on;
    }

    /// <summary>v6: fans group <paramref name="g"/>'s voices out (a hover does it after <see cref="FanDelay"/>; tests and captures call it
    /// directly — then it stays until <see cref="CloseFan"/> or another chip's hover).</summary>
    public void OpenFan(int g, bool forced = true)
    {
        if (g < 0 || g >= Instruments.Count || mix || hidden) return;
        int n = VoicesOf(g);
        if (n < 2) { CloseFan(); return; }
        fanForced = forced;
        fanLeaveT = Time.unscaledTime;
        if (g == fanGroup && n == fanN) return;
        fanGroup = g; fanN = n;
        float w = 28f + FanPitch * n;
        fanRoot.sizeDelta = new Vector2(w, 52f);
        fanRoot.gameObject.SetActive(true);
        fanRoot.SetAsLastSibling();
        for (int v = 0; v < (int)MaxVoices; v++)
        {
            bool on = v < n;
            fanSlots[v].gameObject.SetActive(false);   // they pop in one after another (on twos)
            if (!on) continue;
            fanVoice[v] = v;
            InkCaption.Attach(fanBtns[v].gameObject, Instruments.VoiceName(g, v));
            fanArt[v].Repaint();
        }
        fanBrushShown = BrushOf(g);
        fanStep = 0; fanT = 0f;
        PlaceFan();
        fanRoot.localScale = new Vector3(0.6f, 0.8f, 1f);
        AudioPool.UI(ProceduralAudio.Whoosh(), 0.14f, 1.5f);
    }

    public void CloseFan()
    {
        if (fanGroup < 0) return;
        fanGroup = -1; fanN = 0; fanForced = false; fanStep = -1;
        if (fanRoot != null) fanRoot.gameObject.SetActive(false);
    }

    /// <summary>The fan beside its group's chip (right of the chip's lifted position), the paper's tail on the chip's right edge.</summary>
    void PlaceFan()
    {
        if (fanGroup < 0) return;
        var row = rows[fanGroup];
        float x0 = row.anchoredPosition.x + Chip * 0.5f * chips[fanGroup].rectTransform.sizeDelta.x / Chip + 14f;
        fanRoot.anchoredPosition = new Vector2(x0, row.anchoredPosition.y);
        // the tail points back at the chip (paper-local px: pivot at the paper's centre)
        float w = fanRoot.sizeDelta.x;
        fanPaper.TailTip = new Vector2(-w * 0.5f - 11f, 0f);
    }

    void RepaintFan() { for (int v = 0; v < fanN; v++) fanArt[v].Repaint(); }

    void UpdateFan(float now)
    {
        if (mix || hidden || group.alpha < 0.5f) { CloseFan(); hoverIdx = -1; return; }
        int hov = -1;
        for (int i = 0; i < picks.Length; i++) if (picks[i] != null && picks[i].Hover) { hov = i; break; }
        bool overFan = fanGroup >= 0 && (fanZone.Inside || AnyFanHover());
        if (hov >= 0)
        {
            if (hov != hoverIdx) { hoverIdx = hov; hoverStart = now; }
            if (hov != fanGroup && now - hoverStart >= FanDelay) OpenFan(hov, false);
            if (hov == fanGroup) fanLeaveT = now;
        }
        else hoverIdx = -1;
        if (overFan) fanLeaveT = now;
        if (fanGroup >= 0 && !fanForced && now - fanLeaveT > FanGrace) CloseFan();
        if (fanGroup >= 0 && BrushOf(fanGroup) != fanBrushShown) { fanBrushShown = BrushOf(fanGroup); RepaintFan(); }
    }

    bool AnyFanHover() { for (int v = 0; v < fanN; v++) if (fanBtns[v].Hover) return true; return false; }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> pts = new List<Vector2>(96);

    void PaintMeter(InkPainter p, int i)
    {
        Vector2 c = p.Area.center;
        float r = p.Area.width * 0.5f - 4f, v = Instruments.Volume[i];
        p.Arc(c, r, 5.5f, Comic.A(Comic.Ink, 0.35f), 225f, -45f);
        if (v > 0.001f) p.Arc(c, r, 3.6f, Comic.Cream, 225f, 225f - 270f * v);
        if (v > 0.001f) p.Arc(c, r, 1.4f, Comic.Opaque(Instruments.Colors[i]), 225f, 225f - 270f * v);
    }

    /// <summary>The chip's brush-voice pips, riding the glyph's stepped hop (the same arc and squash as CubeGlyph).</summary>
    void PaintMark(InkPainter p, int i)
    {
        int v = markShown[i];
        if (v <= 0) return;
        Rect r = p.Area;
        float size = Mathf.Min(r.width, r.height);
        float t = (Time.unscaledTime - hopStart[i]) / 0.26f, lift = 0f, squash = 1f;
        if (t >= 0f && t < 1f)
        {
            float ts = Mathf.Floor(t * 12f) / 12f;
            lift = Mathf.Sin(ts * Mathf.PI) * hopHeight[i] * size;
            squash = ts < 0.15f || ts > 0.85f ? 0.88f : 1.04f;
        }
        VoiceMark.Paint(p, r.center + new Vector2(0f, lift), size, v, Comic.Ink, squash);
    }

    void PaintFanChip(InkPainter p, int v)
    {
        if (fanGroup < 0 || v >= fanN) return;
        Vector2 c = p.Area.center + new Vector2(0f, 2f);
        Color col = Comic.Opaque(Instruments.Colors[fanGroup]);
        bool cur = BrushOf(fanGroup) == v;
        bool muted = Instruments.Muted[fanGroup];
        if (cur)
        {
            // the group's current voice: a small ink hexagon ring (selection is drawn, never a new hue)
            pts.Clear();
            float rr = FanCube * 0.5f * 1.36f;
            for (int k = 0; k < 6; k++) { float a = (30f + 60f * k) * Mathf.Deg2Rad; pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr); }
            p.Stroke(pts, 6, 2.2f, Comic.Ink, true);
        }
        p.Disc(c + new Vector2(0f, -FanCube * 0.52f), FanCube * 0.44f, FanCube * 0.12f, Comic.A(Comic.Ink, 0.18f));
        p.Cube(c, FanCube, col, Comic.Ink, 1.8f, muted);
        VoiceMark.Paint(p, c, FanCube, v, Comic.Ink);
    }

    void PaintTongue(InkPainter p, int i)
    {
        Rect r = p.Area; float v = Instruments.Volume[i];
        Rect track = new Rect(r.xMin + 2f, r.center.y - 5f, r.width - 4f, 10f);
        p.RoundRect(track, 5f, Comic.A(Comic.Cream, 0.85f), 2f, Comic.Ink);
        float len = Mathf.Max(10f, (track.width - 2f) * v);
        Color col = Instruments.Muted[i] ? Comic.A(Instruments.Colors[i], 0.35f) : Comic.Opaque(Instruments.Colors[i]);
        if (v > 0.001f)
        {
            // the filled tongue: a slightly swollen tip (it is a tongue, not a bar)
            p.RoundRect(new Rect(track.xMin + 1f, track.yMin + 1f, len, track.height - 2f), 4f, col);
            p.Disc(new Vector2(track.xMin + len - 1f, track.center.y), 6.5f, col);
            p.Arc(new Vector2(track.xMin + len - 1f, track.center.y), 6.5f, 1.8f, Comic.Ink);
        }
    }

    static void PaintSpeaker(InkPainter p, bool muted)
    {
        Vector2 c = p.Area.center + new Vector2(-3f, 0f);
        pts.Clear();
        pts.Add(c + new Vector2(-7f, -3.5f)); pts.Add(c + new Vector2(-3f, -3.5f)); pts.Add(c + new Vector2(3f, -8f)); pts.Add(c + new Vector2(3f, 8f)); pts.Add(c + new Vector2(-3f, 3.5f)); pts.Add(c + new Vector2(-7f, 3.5f));
        p.InkFill(pts, pts.Count, muted ? Comic.A(Comic.Cream, 0.6f) : Comic.Cream, 1.6f, Comic.Ink);
        if (muted) { p.Line(c + new Vector2(7f, -5f), c + new Vector2(13f, 5f), 2f, Comic.Danger, true); p.Line(c + new Vector2(13f, -5f), c + new Vector2(7f, 5f), 2f, Comic.Danger, true); }
        else { p.Arc(c + new Vector2(4f, 0f), 5f, 1.6f, Comic.Ink, -40f, 40f); p.Arc(c + new Vector2(4f, 0f), 9f, 1.6f, Comic.Ink, -40f, 40f); }
    }

    static void PaintFader(InkPainter p)
    {
        Vector2 c = p.Area.center;
        for (int k = 0; k < 3; k++)
        {
            float y = c.y + (1 - k) * 6.5f;
            p.Line(new Vector2(c.x - 9f, y), new Vector2(c.x + 9f, y), 1.8f, Comic.Ink, true);
            float kx = c.x + (k == 0 ? 4f : (k == 1 ? -3f : 1.5f));
            p.Disc(new Vector2(kx, y), 3.2f, Comic.Ink);
            p.Disc(new Vector2(kx, y), 1.8f, Comic.Cream);
        }
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (chips[0] == null || SongManager.I == null) return;   // not built (or a domain reload dropped the references)
        Subscribe();
        if (usedDirty) RefreshUsed();
        float now = Time.unscaledTime;
        var pm = PathManager.I;
        int h = pm != null && pm.CubeInHand ? Mathf.Clamp(pm.selectedInstrument, 0, Instruments.Count - 1) : -1;
        if (h != held)
        {
            if (held >= 0 && held < chips.Length) chips[held].Ring = false;
            held = h;
            if (held >= 0) { chips[held].Ring = true; heldSince = now; }
        }
        for (int i = 0; i < Instruments.Count; i++)
        {
            bool m = Instruments.Muted[i];
            if (m != mutedShown[i]) { mutedShown[i] = m; chips[i].Hollow = m; if (mix) { tongues[i].Repaint(); muteArt[i].Repaint(); } if (fanGroup == i) RepaintFan(); }
            if (used[i] != usedShown[i]) { usedShown[i] = used[i]; chips[i].GroundShadow = used[i]; }
            float v = Instruments.Volume[i];
            if (Mathf.Abs(v - volShown[i]) > 0.0005f) { volShown[i] = v; if (mix) tongues[i].Repaint(); if (meters[i].gameObject.activeSelf) meters[i].Repaint(); }
            int mv = VoicesOf(i) > 1 ? BrushOf(i) : 0;
            if (mv != markShown[i]) { markShown[i] = mv; marks[i].Repaint(); }
        }
        // SPHERE: the chips (and their held shadows) are balls while the sphere shape is chosen
        bool rnd = pm != null && pm.BrushSphere;
        if (rnd != roundShown)
        {
            roundShown = rnd;
            for (int i = 0; i < chips.Length; i++) { chips[i].Round = rnd; shadows[i].Round = rnd; }
            PaintShapeSwitch(rnd);
        }
        else if (pm != null && pm.selectedInstrument != shapeColShown) PaintShapeSwitch(rnd);
        UpdateFan(now);
        // stepped motion: the held chip's lift and sway, the meters' fade, the tongues sliding out, the hidden column, the fan's pop
        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        float dt = 1f / Look.TwosFps;
        float ga = hidden ? 0f : 1f;
        if (group.alpha != ga) group.alpha = Mathf.MoveTowards(group.alpha, ga, dt / 0.18f);
        for (int i = 0; i < Instruments.Count; i++)
        {
            float want = i == held ? 1f : 0f;
            if (lift[i] != want)
            {
                lift[i] = Mathf.MoveTowards(lift[i], want, dt / 0.16f);
                float e = Ease.OutBack(lift[i], 1.6f);
                var row = rows[i];
                row.anchoredPosition = new Vector2(Chip * 0.5f, rowY[i]) + HeldLift * e;
                float k = 1f + 0.2f * e;
                chips[i].rectTransform.sizeDelta = new Vector2(Chip * k, Chip * k);   // the chip grows, not its row (the tongue keeps its size)
                var sh = shadows[i];
                bool shOn = lift[i] > 0.02f;
                if (sh.gameObject.activeSelf != shOn) sh.gameObject.SetActive(shOn);
                if (shOn) { sh.rectTransform.sizeDelta = new Vector2(Chip * k, Chip * k); sh.rectTransform.anchoredPosition = HeldShadow * Mathf.Clamp01(e); }
                if (lift[i] <= 0f) chips[i].rectTransform.localEulerAngles = Vector3.zero;
                if (fanGroup == i) PlaceFan();
            }
            if (i == held && lift[i] >= 1f)
            {
                // the sway of a thing held in the hand: ±3° on twos, about one swing a second
                float a = 3f * Mathf.Sin((now - heldSince) * Mathf.PI * 2f * 0.9f);
                chips[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.Round(a * 2f) * 0.5f);
                shadows[i].rectTransform.localEulerAngles = chips[i].rectTransform.localEulerAngles;
            }
            if (markShown[i] > 0 && now - hopStart[i] < 0.3f) marks[i].Repaint();   // the pips ride the hop (on twos)
            if (meters[i].gameObject.activeSelf)
            {
                bool scrolling = scrolls[i].Pending || Time.unscaledTime - scrolls[i].LastScrollTime < 0.5f;
                meterA[i] = scrolling ? 1f : Mathf.MoveTowards(meterA[i], 0f, dt / 0.35f);
                meters[i].color = new Color(1f, 1f, 1f, meterA[i]);
                if (meterA[i] <= 0f) meters[i].gameObject.SetActive(false);
            }
            if (mix && tongueT[i] < 1f)
            {
                tongueT[i] = Mathf.MoveTowards(tongueT[i], 1f, dt / 0.16f);
                var tr = tongues[i].rectTransform;
                float e = Ease.OutCubic(tongueT[i]);
                tr.localScale = new Vector3(Mathf.Max(0.05f, e), 1f, 1f);
                tr.anchoredPosition = new Vector2(Chip * 0.5f + 12f + TongueW * 0.5f * e, 0f);
            }
        }
        if (fanGroup >= 0 && fanStep >= 0)
        {
            // the fan pops on twos (0.6 → 1.05 → 1) and deals its chips left to right, three per drawing
            fanT += dt;
            int st = Mathf.Min(3, ++fanStep);
            float s = st == 1 ? 0.92f : (st == 2 ? 1.05f : 1f);
            fanRoot.localScale = new Vector3(s, s, 1f);
            int shown = Mathf.Min(fanN, fanStep * 3);
            for (int v = 0; v < shown; v++) if (!fanSlots[v].gameObject.activeSelf) fanSlots[v].gameObject.SetActive(true);
            if (shown >= fanN && st >= 3) fanStep = -1;
        }
    }

    /// <summary>Tests: one frame of the column's own work (GC measurements).</summary>
    public void StepForTest() { Update(); }

    void RefreshUsed()
    {
        usedDirty = false;
        for (int i = 0; i < used.Length; i++) used[i] = false;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.nodes.Count == 0) continue;
            int k = Mathf.Clamp(c.instrument, 0, used.Length - 1);
            used[k] = true;
        }
    }
}
