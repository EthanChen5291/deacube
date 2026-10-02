using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v4 world HUD, bottom-left (research §4.1 "the transport", replaces the 10-control pill): time lives in one corner.
/// Play is the hero — a 76 px cream sticker disc with a slightly uneven ink ▶ (❚❚ while playing) that breathes on every beat; holding it
/// 0.5 s stops and rewinds (a radial ink ring fills while held); when stopped partway, a ⏮ sticker peeks out from behind it. The tempo is
/// the Bangers digits (the only digits on the HUD, misprinted in magenta): drag them up / down, scroll, or click to tap the tempo; a thin
/// range arc shows only while dragging. A metronome needle swings on the beat (click = metronome, filled bob = on). The beat is a row of
/// mini cubes (the current one hops and turns cream, the downbeat one is bigger; click = beats per bar). A knob sticker opens the paper
/// sound drawer upward: swing, key, tone, space, weather and volume as picture knobs with an ink arc for the value (names on hover).
/// While playing, rubber punch pads (J K H B) sit above. v3 contract: HUD/Transport/Main/Tempo/Label (the digits), HUD/Transport/Punch.
/// </summary>
public class HudTransport : MonoBehaviour
{
    public static HudTransport I;

    public HudButton PlayButton => playBtn;
    public HudButton RewindButton => rewindBtn;
    public HudDial Tempo => tempo;
    public HudButton MetronomeButton => metroBtn;
    public HudButton MeterButton => meterBtn;
    public HudButton SoundButton => soundBtn;
    public bool DrawerOpen => drawerOpen;
    public RectTransform Drawer => drawer;
    public RectTransform Punch => punch;
    public HoldButton Stutter => pads[0];
    public HoldButton Funnel => pads[1];
    public HoldButton Half => pads[2];
    public HoldButton Drop => pads[3];
    public HudDial Knob(string name) { foreach (var k in knobs) if (k.name == name) return k; return null; }
    public HudButton WeatherButton => weatherBtn;
    public int BeatCubeCount => beatCubes.Count;
    public CubeGlyph BeatCube(int i) => i >= 0 && i < beatCubes.Count ? beatCubes[i] : null;

    RectTransform root, main, breath, punch, drawer, beatsRt, rangeRt;
    HudButton playBtn, rewindBtn, metroBtn, meterBtn, soundBtn, weatherBtn;
    InkPainter playArt, metroArt, rangeArt, weatherArt;
    HudDial tempo; TextMeshProUGUI tempoLabel;
    readonly List<CubeGlyph> beatCubes = new List<CubeGlyph>();
    readonly HoldButton[] pads = new HoldButton[4];
    readonly InkShape[] padBody = new InkShape[4];
    readonly RectTransform[] padContent = new RectTransform[4];
    readonly bool[] padShown = new bool[4];
    readonly List<HudDial> knobs = new List<HudDial>();
    readonly Dictionary<HudDial, Func<float>> knobGet = new Dictionary<HudDial, Func<float>>();
    CanvasGroup drawerGroup; Image weatherArc;
    bool playShown, drawerOpen, metroShown; int tick = int.MinValue, beatShown = -2, breathTicks, climateShown = -1;
    float drawerT, tempoShown = -1f;

    public static HudTransport Build(RectTransform hud)
    {
        var rt = HudKit.Node(hud, "Transport", new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(360f, 92f));
        rt.pivot = new Vector2(0f, 0f);
        var t = rt.gameObject.AddComponent<HudTransport>();
        t.root = rt;
        t.BuildParts();
        return t;
    }

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    static RectTransform At(Transform parent, string name, Vector2 centre, Vector2 size)
    {
        var rt = HudKit.Node(parent, name, new Vector2(0f, 0f), centre, size);
        return rt;
    }

    void BuildParts()
    {
        main = HudKit.Node(root, "Main", new Vector2(0f, 0f), Vector2.zero, root.sizeDelta);
        main.pivot = Vector2.zero;
        InkShape body; InkPainter art;
        // ⏮ first: it peeks out from behind the play disc
        rewindBtn = HudKit.Sticker(main, "Rewind", new Vector2(0f, 0f), new Vector2(0f, 74f), new Vector2(32f, 30f), InkShape.Kind.Sticker, Comic.Cream,
                                   PaintRewind, "rewind", () => { GlobalClock.Seek(0); SequenceMaster.ResetAllCubes(); }, out body, out art);
        body.SetInk(1.6f, 2.6f); body.ShadowOffset = new Vector2(3f, -3.5f);
        (rewindBtn.transform as RectTransform).localEulerAngles = new Vector3(0f, 0f, 12f);
        rewindBtn.gameObject.SetActive(false);
        // play (the hero), in a "breath" node that pulses on the beat
        breath = At(main, "Breath", new Vector2(38f, 38f), new Vector2(76f, 76f));
        playBtn = HudKit.Sticker(breath, "Play", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 76f), InkShape.Kind.Circle, Comic.Cream,
                                 p => PaintPlay(p), "play", GlobalClock.Toggle, out body, out playArt);
        body.SetInk(3f, 4.8f); body.ShadowOffset = new Vector2(4f, -5f); body.Wobble = 1.4f;
        var hold = UIKit.BandRing(playBtn.transform, "Hold", Comic.Ink, 100f);
        hold.fillOrigin = 2;
        playBtn.holdFill = hold; playBtn.holdSeconds = 0.5f;
        playBtn.onHold = () => { GlobalClock.Stop(); GlobalClock.Seek(0); SequenceMaster.ResetAllCubes(); };
        InkCaption.Attach(playBtn.gameObject, "play");
        // tempo digits (a HudDial on a bare hit area)
        var trt = At(main, "Tempo", new Vector2(134f, 42f), new Vector2(98f, 64f));
        var hit = trt.gameObject.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
        tempo = trt.gameObject.AddComponent<HudDial>();
        tempo.min = 40f; tempo.max = 240f; tempo.integer = true; tempo.pixelsForFullRange = 340f;
        tempo.onChanged = GlobalClock.SetBPM; tempo.onTap = TapTempo; tempo.onRelease = History.Push;
        tempoLabel = HudKit.Words(trt, "Label", "", 54f, Comic.Cream, TextAlignmentOptions.Center);
        tempoLabel.font = Comic.DigitFont; tempoLabel.fontSharedMaterial = Comic.MisprintMaterial(Comic.DigitFont); tempoLabel.characterSpacing = 3f;
        var lrt = tempoLabel.rectTransform; lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = new Vector2(0f, 4f);
        tempo.label = tempoLabel;
        tempo.Set(GlobalClock.BPM, false);
        InkCaption.Attach(trt.gameObject, "tempo");
        rangeRt = HudKit.Node(trt, "Range", new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(130f, 96f));
        rangeArt = rangeRt.gameObject.AddComponent<InkPainter>(); rangeArt.raycastTarget = false; rangeArt.onPaint = PaintRange;
        rangeRt.gameObject.SetActive(false);
        // metronome needle
        var mrt = At(main, "Metronome", new Vector2(197f, 42f), new Vector2(28f, 48f));
        metroArt = mrt.gameObject.AddComponent<InkPainter>(); metroArt.onPaint = PaintMetronome;
        metroBtn = HudKit.Control(metroArt, "metronome", () => { GlobalClock.SetMetronome(!GlobalClock.Metronome); metroArt.Repaint(); }, null, null, metroArt);
        // beat cubes (click = beats per bar)
        var bh = HudKit.Hit(main, "Meter", new Vector2(0f, 0f), new Vector2(258f, 38f), new Vector2(92f, 36f));
        beatsRt = bh.rectTransform;
        meterBtn = HudKit.Control(bh, "beats", CycleMeter);
        // the sound drawer's knob
        soundBtn = HudKit.Sticker(main, "MoreToggle", new Vector2(0f, 0f), new Vector2(330f, 40f), new Vector2(38f, 38f), InkShape.Kind.Circle, Comic.Cream,
                                  PaintKnob, "sound", ToggleDrawer, out body, out art);
        body.SetInk(1.8f, 2.8f); body.ShadowOffset = new Vector2(3f, -4f);

        BuildPunch();
        BuildDrawer();
        RebuildBeats();
        Hints.Register("transport", root); Hints.Register("play", playBtn.transform as RectTransform); Hints.Register("tempo", trt);
        Hints.Register("punch", punch); Hints.Register("sound", soundBtn.transform as RectTransform);
    }

    // ------------------------------------------------------------------ punch pads (rubber, while playing)
    static readonly string[] PadIcons = { "bolt", "funnel", "half", "dropOut" };
    static readonly string[] PadNames = { "Stutter", "Funnel", "Half", "Drop" };
    static readonly string[] PadKeys = { "j", "k", "h", "b" };
    static readonly string[] PadWords = { "stutter", "funnel", "half time", "drop" };
    static readonly Color Rubber = new Color(0.33f, 0.27f, 0.58f), RubberLip = new Color(0.14f, 0.10f, 0.28f), RubberHot = new Color(0.52f, 0.44f, 0.84f);

    void BuildPunch()
    {
        punch = HudKit.Node(root, "Punch", new Vector2(0f, 0f), new Vector2(0f, 100f), new Vector2(232f, 52f));
        punch.pivot = Vector2.zero;
        for (int k = 0; k < 4; k++)
        {
            var rt = HudKit.Node(punch, PadNames[k], new Vector2(0f, 0f), new Vector2(26f + k * 56f, 26f), new Vector2(48f, 44f));
            var s = rt.gameObject.AddComponent<InkShape>();
            s.Shape = InkShape.Kind.RoundRect; s.color = Rubber; s.Radius = 12f; s.Wobble = 0.8f; s.RotJitter = 0.8f;
            s.SetInk(2f, 3f); s.ShadowColor = RubberLip; s.ShadowOffset = new Vector2(0f, -5f); s.raycastTarget = true;
            padBody[k] = s;
            var content = HudKit.Node(rt, "Face", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 44f));
            padContent[k] = content;
            var g = Comic.GlyphImage(content, "Glyph", PadIcons[k], Comic.Cream, 24f, true);
            g.rectTransform.anchoredPosition = new Vector2(-2f, -1f);
            var key = HudKit.Words(content, "Key", PadKeys[k], 13f, Comic.A(Comic.Cream, 0.85f), TextAlignmentOptions.Center);
            var kr = key.rectTransform; kr.anchorMin = kr.anchorMax = new Vector2(1f, 1f); kr.sizeDelta = new Vector2(14f, 14f); kr.anchoredPosition = new Vector2(-8f, -8f);
            var b = rt.gameObject.AddComponent<HoldButton>();
            b.bg = null; b.icon = null; b.shape = null; b.animateScale = false;
            b.onHoldChanged = _ => { if (UIManager.I != null) UIManager.I.RefreshPunch(); };
            InkCaption.Attach(rt.gameObject, PadWords[k]);
            pads[k] = b;
        }
        punch.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ the sound drawer
    void BuildDrawer()
    {
        drawer = HudKit.Node(root, "Drawer", new Vector2(0f, 0f), new Vector2(150f, 128f), new Vector2(336f, 212f));
        drawer.pivot = Vector2.zero;
        var paper = drawer.gameObject.AddComponent<InkShape>();
        paper.Shape = InkShape.Kind.RoundRect; paper.color = Comic.Cream; paper.Radius = 16f; paper.Wobble = 1.6f; paper.RotJitter = 0.6f;
        paper.SetInk(2.4f, 3.8f); paper.ShadowOffset = new Vector2(5f, -6f); paper.raycastTarget = true;
        drawerGroup = drawer.gameObject.AddComponent<CanvasGroup>();
        Knob("Swing", new Vector2(62f, 150f), "swing", 0f, 1f, false, () => GlobalClock.Swing, v => GlobalClock.SetSwing(v), true, PaintSwing);
        Knob("Key", new Vector2(168f, 150f), "key", -12f, 12f, true, () => SongManager.Transpose, v => SongManager.SetTranspose(Mathf.RoundToInt(v)), true, PaintKey);
        Knob("Tone", new Vector2(274f, 150f), "tone", 0.15f, 1f, false, () => SongManager.Tone, Performance.SetTone, true, PaintTone);
        Knob("Space", new Vector2(62f, 60f), "space", 0f, 1.6f, false, () => SongManager.Space, Performance.SetSpace, true, PaintSpace);
        // weather: a click cycles the climate (SongManager.SetClimate pushes its own History entry)
        InkShape wb; InkPainter wa;
        weatherBtn = HudKit.Sticker(drawer, "Weather", new Vector2(0f, 0f), new Vector2(168f, 60f), new Vector2(54f, 54f), InkShape.Kind.Circle, Comic.Cream,
                                    PaintWeather, "weather", () => { SongManager.SetClimate((SongManager.Climate + 1) % 4); weatherArt.Repaint(); }, out wb, out wa);
        wb.SetInk(2f, 3f); wb.ShadowOffset = new Vector2(3f, -4f);
        weatherArt = wa;
        weatherArc = UIKit.BandRing(weatherBtn.transform, "Level", Comic.Ink, 72f);
        weatherArc.fillOrigin = 0;
        Knob("Volume", new Vector2(274f, 60f), "volume", 0f, 1f, false, () => AudioListener.volume, v => AudioListener.volume = v, false, PaintVolume);
        drawer.gameObject.SetActive(false);
    }

    HudDial Knob(string name, Vector2 centre, string caption, float min, float max, bool integer, Func<float> get, Action<float> set, bool push, Action<InkPainter> picture)
    {
        var rt = At(drawer, name, centre, new Vector2(54f, 54f));
        var face = rt.gameObject.AddComponent<InkShape>();
        face.Shape = InkShape.Kind.Circle; face.color = Comic.Cream; face.SetInk(2f, 3f); face.ShadowOffset = new Vector2(3f, -4f); face.raycastTarget = true;
        var track = UIKit.BandRing(rt, "Track", Comic.A(Comic.Ink, 0.16f), 72f); track.fillAmount = 1f;
        var fill = UIKit.BandRing(rt, "Fill", Comic.Ink, 72f); fill.fillOrigin = 0;
        var pic = HudKit.Node(rt, "Art", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
        var art = pic.gameObject.AddComponent<InkPainter>(); art.raycastTarget = false; art.onPaint = picture;
        var d = rt.gameObject.AddComponent<HudDial>();
        d.min = min; d.max = max; d.integer = integer; d.fill = fill; d.pixelsForFullRange = integer ? 260f : 160f;
        d.onChanged = set;
        if (push) d.onRelease = History.Push;
        d.Set(get(), false);
        InkCaption.Attach(rt.gameObject, caption);
        knobs.Add(d); knobGet[d] = get;
        return d;
    }

    /// <summary>Re-reads every knob (song controls change on load, undo, climate, transpose).</summary>
    public void RefreshKnobs()
    {
        foreach (var d in knobs) { if (d.Dragging || d.ScrollPending) continue; Func<float> g; if (knobGet.TryGetValue(d, out g)) d.Set(g(), false); }
        if (weatherArc != null) weatherArc.fillAmount = SongManager.Climate / 3f;
        if (weatherArt != null && climateShown != SongManager.Climate) { climateShown = SongManager.Climate; weatherArt.Repaint(); }
    }

    public void ToggleDrawer() { SetDrawer(!drawerOpen); }

    public void SetDrawer(bool on)
    {
        if (drawerOpen == on) return;
        drawerOpen = on;
        var h = soundBtn.GetComponent<InkHover>(); if (h != null) h.active = on;
        if (on) { drawer.gameObject.SetActive(true); drawerT = 0f; RefreshKnobs(); drawer.SetAsLastSibling(); AudioPool.UI(ProceduralAudio.Pop(), 0.25f, 1.15f); }
        else AudioPool.UI(ProceduralAudio.Tick(), 0.2f, 0.85f);
    }

    // ------------------------------------------------------------------ pictures
    static readonly List<Vector2> q = new List<Vector2>(96);

    void PaintPlay(InkPainter p)
    {
        Vector2 c = p.Area.center;
        if (GlobalClock.IsPlaying)
        {
            p.RoundRect(new Rect(c.x - 12f + p.Still(1) * 0.8f, c.y - 13f, 8.5f, 26f + p.Still(2)), 3.5f, Comic.Ink);
            p.RoundRect(new Rect(c.x + 3.5f + p.Still(3) * 0.8f, c.y - 13f + p.Still(4), 8.5f, 26f), 3.5f, Comic.Ink);
        }
        else
        {
            // a slightly uneven hand-drawn ▶
            q.Clear();
            q.Add(c + new Vector2(17f, 0.5f) + new Vector2(p.Still(5), p.Still(6)) * 0.8f);
            q.Add(c + new Vector2(-9f, 15f) + new Vector2(p.Still(7), p.Still(8)) * 1.2f);
            q.Add(c + new Vector2(-8f, -15.5f) + new Vector2(p.Still(9), p.Still(10)) * 1.2f);
            p.Fill(q, 3, Comic.Ink);
        }
    }

    static void PaintRewind(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.RoundRect(new Rect(c.x - 9f, c.y - 7f, 3f, 14f), 1.2f, Comic.Ink);
        q.Clear(); q.Add(c + new Vector2(-5f, 0f)); q.Add(c + new Vector2(8f, 7.5f)); q.Add(c + new Vector2(8f, -7.5f));
        p.Fill(q, 3, Comic.Ink);
    }

    void PaintRange(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(0f, -8f);
        float r = 54f, t = Mathf.InverseLerp(tempo.min, tempo.max, tempo.value);
        p.Arc(c, r, 2f, Comic.A(Comic.Cream, 0.55f), 200f, -20f, 3f, 3f);
        float a = Mathf.Lerp(200f, -20f, t) * Mathf.Deg2Rad;
        Vector2 m = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        p.Disc(m, 5f, Comic.Ink); p.Disc(m, 3.2f, Comic.Magenta);
    }

    void PaintMetronome(InkPainter p)
    {
        Rect r = p.Area; Vector2 b = new Vector2(r.center.x, r.yMin + 7f);
        // the body: a little trapezoid, the needle swinging from its foot
        q.Clear(); q.Add(b + new Vector2(-10f, -3f)); q.Add(b + new Vector2(10f, -3f)); q.Add(b + new Vector2(6f, 7f)); q.Add(b + new Vector2(-6f, 7f));
        p.InkFill(q, 4, Comic.Cream, 1.8f, Comic.Ink);
        float ang = GlobalClock.IsPlaying ? 24f * Mathf.Cos((float)(GlobalClock.SongBeatD % 2.0) * Mathf.PI) : 0f;
        float a = (90f - ang) * Mathf.Deg2Rad;
        Vector2 tip = b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 32f;
        p.Line(b, tip, 4.2f, Comic.Ink, true);
        p.Line(b, tip, 1.8f, Comic.Cream, true);
        Vector2 bob = Vector2.Lerp(b, tip, 0.62f);
        bool on = GlobalClock.Metronome;
        p.Disc(bob, 5.2f, Comic.Ink);
        p.Disc(bob, 3.4f, on ? Comic.Pop : Comic.Cream);
        if (!on) p.Disc(bob, 1.4f, Comic.Ink);
    }

    static void PaintKnob(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Arc(c, 9f, 2.2f, Comic.Ink);
        p.Arc(c, 4.5f, 1.6f, Comic.Ink);
        p.Line(c + new Vector2(0f, 9f), c + new Vector2(0f, 13.5f), 2.2f, Comic.Ink, true);
    }

    static void PaintSwing(InkPainter p)
    {
        Vector2 c = p.Area.center; q.Clear();
        for (int i = 0; i <= 16; i++) { float x = -13f + i * (26f / 16f); q.Add(c + new Vector2(x, Mathf.Sin(i / 16f * Mathf.PI * 2f) * 6f * (1f - Mathf.Abs(x) / 20f))); }
        p.Stroke(q, q.Count, 2.4f, Comic.Ink, false, 0f, 0f, 1.2f, true);
    }

    static void PaintKey(InkPainter p)
    {
        Vector2 c = p.Area.center;
        // a little ladder with a cube on its middle rung
        p.Line(c + new Vector2(-7f, -13f), c + new Vector2(-7f, 12f), 1.8f, Comic.Ink, true);
        p.Line(c + new Vector2(7f, -13f), c + new Vector2(7f, 12f), 1.8f, Comic.Ink, true);
        for (int k = 0; k < 4; k++) { float y = -9f + k * 6.5f; p.Line(c + new Vector2(-7f, y), c + new Vector2(7f, y), 1.4f, Comic.Ink); }
        p.Cube(c + new Vector2(0f, 2.5f), 10f, Palette.Accent, Comic.Ink, 1.2f);
    }

    static void PaintTone(InkPainter p)
    {
        Vector2 c = p.Area.center; float r = 11f;
        p.Disc(c, r, Comic.Ink);
        q.Clear(); InkPainter.ArcPoints(q, c, r - 1.5f, r - 1.5f, 90f, 270f, 18);
        p.Fill(q, q.Count, Comic.Pop);
        p.Arc(c, r, 1.8f, Comic.Ink);
    }

    static void PaintSpace(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(-6f, 0f);
        p.Disc(c, 3.5f, Comic.Ink);
        p.Arc(c, 7.5f, 1.8f, Comic.Ink, -50f, 50f);
        p.Arc(c, 12.5f, 1.6f, Comic.A(Comic.Ink, 0.7f), -45f, 45f);
        p.Arc(c, 17.5f, 1.4f, Comic.A(Comic.Ink, 0.45f), -40f, 40f);
    }

    static void PaintWeather(InkPainter p)
    {
        Vector2 c = p.Area.center;
        int k = SongManager.Climate;
        if (k == 0 || k == 1)
        {
            Vector2 s = c + (k == 0 ? new Vector2(5f, 5f) : Vector2.zero);
            p.Disc(s, 6f, Comic.Pop); p.Arc(s, 6f, 1.6f, Comic.Ink);
            for (int i = 0; i < 8; i++) { float a = i * 45f * Mathf.Deg2Rad; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.Line(s + d * 8.5f, s + d * 11.5f, 1.6f, Comic.Ink, true); }
        }
        if (k != 1)
        {
            Vector2 o = c + (k == 0 ? new Vector2(-4f, -4f) : new Vector2(0f, 2f));
            Color cc = k == 3 ? new Color(0.62f, 0.60f, 0.74f) : Color.white;
            p.Disc(o + new Vector2(-5f, 0f), 5f, Comic.Ink); p.Disc(o + new Vector2(1f, 3f), 6.5f, Comic.Ink); p.Disc(o + new Vector2(7f, 0f), 5f, Comic.Ink);
            p.RoundRect(new Rect(o.x - 11f, o.y - 6f, 23f, 7.5f), 3.5f, Comic.Ink);
            p.Disc(o + new Vector2(-5f, 0f), 3.6f, cc); p.Disc(o + new Vector2(1f, 3f), 5f, cc); p.Disc(o + new Vector2(7f, 0f), 3.6f, cc);
            p.RoundRect(new Rect(o.x - 9.6f, o.y - 4.6f, 20.2f, 4.8f), 2.2f, cc);
            if (k == 3) { q.Clear(); q.Add(o + new Vector2(1f, -6f)); q.Add(o + new Vector2(-3f, -13f)); q.Add(o + new Vector2(0.5f, -12f)); q.Add(o + new Vector2(-2f, -18f)); q.Add(o + new Vector2(4f, -10f)); q.Add(o + new Vector2(0.5f, -11f)); p.Stroke(q, q.Count, 1.8f, Comic.Pop, false); }
        }
    }

    static void PaintVolume(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(-4f, 0f);
        q.Clear(); q.Add(c + new Vector2(-8f, -4f)); q.Add(c + new Vector2(-3f, -4f)); q.Add(c + new Vector2(4f, -10f)); q.Add(c + new Vector2(4f, 10f)); q.Add(c + new Vector2(-3f, 4f)); q.Add(c + new Vector2(-8f, 4f));
        p.InkFill(q, q.Count, Comic.Cream, 1.8f, Comic.Ink);
        p.Arc(c + new Vector2(5f, 0f), 6f, 1.8f, Comic.Ink, -45f, 45f);
        p.Arc(c + new Vector2(5f, 0f), 11f, 1.8f, Comic.Ink, -45f, 45f);
    }

    // ------------------------------------------------------------------ beat cubes
    void RebuildBeats()
    {
        foreach (var g in beatCubes) if (g != null) Destroy(g.gameObject);
        beatCubes.Clear();
        int n = Mathf.Clamp(GlobalClock.BeatsPerBar, 1, 8);
        float step = 19f, x0 = -(n - 1) * step * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float px = i == 0 ? 20f : 15.5f;
            var rt = HudKit.Node(beatsRt, "Beat" + i, new Vector2(0.5f, 0.5f), new Vector2(x0 + i * step, -2f), new Vector2(px, px));
            var g = rt.gameObject.AddComponent<CubeGlyph>();
            g.color = BeatGrey; g.raycastTarget = false;
            beatCubes.Add(g);
        }
        beatShown = -2;
    }
    static readonly Color BeatGrey = new Color(0.62f, 0.58f, 0.80f);

    void CycleMeter()
    {
        int n = GlobalClock.BeatsPerBar;
        int next = n == 3 ? 4 : (n == 4 ? 5 : (n == 5 ? 6 : 3));
        if (SongManager.I != null && SongManager.I.HasSong) SongManager.I.SetBeatsPerBar(next); else GlobalClock.SetBeatsPerBar(next);
        RebuildBeats();
    }

    readonly List<float> taps = new List<float>();
    void TapTempo()
    {
        float now = Time.unscaledTime;
        for (int i = taps.Count - 1; i >= 0; i--) if (now - taps[i] > 2.5f) taps.RemoveAt(i);
        taps.Add(now);
        AudioPool.UI(ProceduralAudio.Click(true), 0.4f);
        if (taps.Count >= 3)
        {
            float avg = (taps[taps.Count - 1] - taps[0]) / (taps.Count - 1);
            if (avg > 0.2f) { GlobalClock.SetBPM(60f / avg); History.Push(); }
        }
    }

    // ------------------------------------------------------------------ events (UIManager forwards them)
    public void OnMeterChanged() { RebuildBeats(); }
    public void OnBeat(int beat, bool down)
    {
        breathTicks = 1;
        int i = GlobalClock.BeatInBar;
        if (i >= 0 && i < beatCubes.Count) beatCubes[i].Hop(down ? 0.7f : 0.5f);
    }
    public void OnTransport()
    {
        HudKit.SetActive(punch, GlobalClock.IsPlaying);
        if (playShown != GlobalClock.IsPlaying)
        {
            playShown = GlobalClock.IsPlaying; playArt.Repaint();
            InkCaption.Attach(playBtn.gameObject, playShown ? "pause" : "play");
        }
    }

    /// <summary>Pads light up while a punch-in is live (held by pad or key).</summary>
    public void ShowPads(bool stutter, bool funnel, bool half, bool drop)
    {
        SetPad(0, stutter); SetPad(1, funnel); SetPad(2, half); SetPad(3, drop);
    }
    void SetPad(int k, bool on)
    {
        if (padShown[k] == on) return;
        padShown[k] = on;
        padBody[k].color = on ? RubberHot : Rubber;
        padBody[k].ShadowOffset = on ? new Vector2(0f, -1.5f) : new Vector2(0f, -5f);
        padContent[k].anchoredPosition = on ? new Vector2(0f, -3f) : Vector2.zero;
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (playArt == null || tempo == null || drawer == null) return;   // not built (or a domain reload dropped the references)
        if (playShown != GlobalClock.IsPlaying) OnTransport();
        bool mid = !GlobalClock.IsPlaying && GlobalClock.SongBeat > 0.01f;
        if (rewindBtn.gameObject.activeSelf != mid) rewindBtn.gameObject.SetActive(mid);
        float bpm = Mathf.Round(GlobalClock.BPM);
        if (bpm != tempoShown && !tempo.Dragging) { tempoShown = bpm; tempo.Set(GlobalClock.BPM, false); }
        if (rangeRt.gameObject.activeSelf != tempo.Dragging) { rangeRt.gameObject.SetActive(tempo.Dragging); }
        if (tempo.Dragging) rangeArt.Repaint();
        if (metroShown != GlobalClock.Metronome) { metroShown = GlobalClock.Metronome; metroArt.Repaint(); }
        // a click outside the open drawer closes it
        if (drawerOpen && Input.GetMouseButtonDown(0) && !OverDrawer(Input.mousePosition)) SetDrawer(false);

        int t = HudKit.TwosTick;
        if (t == tick) return;
        tick = t;
        // on twos: the breath, the needle, the beat cubes, the drawer slide
        float bs = breathTicks > 0 ? 1.05f : 1f;
        if (breathTicks > 0) breathTicks--;
        if (breath.localScale.x != bs) { breath.localScale = new Vector3(bs, bs, 1f); tempoLabel.rectTransform.localScale = new Vector3(bs, bs, 1f); }
        if (GlobalClock.IsPlaying) metroArt.Repaint();
        int cur = GlobalClock.IsPlaying ? GlobalClock.BeatInBar : -1;
        if (cur != beatShown)
        {
            beatShown = cur;
            for (int i = 0; i < beatCubes.Count; i++) beatCubes[i].color = i == cur ? Comic.Cream : BeatGrey;
        }
        if (drawerOpen || drawer.gameObject.activeSelf)
        {
            drawerT = Mathf.MoveTowards(drawerT, drawerOpen ? 1f : 0f, (1f / Look.TwosFps) / 0.18f);
            float e = Ease.OutCubic(drawerT);
            drawer.anchoredPosition = new Vector2(150f, 100f + 28f * e);
            drawerGroup.alpha = e;
            drawerGroup.blocksRaycasts = drawerOpen;
            if (!drawerOpen && drawerT <= 0f) drawer.gameObject.SetActive(false);
        }
    }

    bool OverDrawer(Vector2 screen)
    {
        if (RectTransformUtility.RectangleContainsScreenPoint(drawer, screen, null)) return true;
        var sb = soundBtn.transform as RectTransform;
        return RectTransformUtility.RectangleContainsScreenPoint(sb, screen, null);
    }
}
