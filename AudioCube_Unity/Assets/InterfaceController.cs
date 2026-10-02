using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The "vibe" prompt overlay: one input field, a sparkle button (AI generation) and a dice button (offline generation).
/// v3 (SPEC §5.1): no auto-show at start (the main menu opens it from New; the HUD sparkle from the world); it has its own
/// canvas above the menu; while visible it locks the world and the keys ("prompt"). Success (online or dice): the generator
/// starts the song (SongManager.StartFreshSong once package B lands, StartNewSong before), the prompt hides, the menu leaves
/// into the world, then <see cref="Onboarding.StartTutorial"/>(false). ✕ / Esc: back where it was opened (menu or world);
/// from the world without a song it returns to the menu.
/// Fixes (v3 review): ✕ / Esc while a generation runs cancel it (the generator never applies it: no song swap, no stop, no leave to the
/// world) (F3 / S1); the empty field shows a soft pencil glyph and the blinking caret instead of example words (F23).
/// v4 look (package U1, scratchpad/v4/UI.md): the kit's ink language — a wobbly paper card with a hard violet print shadow, the field an
/// ink-outlined paper strip, the sparkle "go" the hero sticker (ink, magenta shadow), the dice a drawn die, ✕ a small cream sticker, the
/// waiting dots three hopping mini cubes, the logo the kit's cube.
/// </summary>
public class InterfaceController : MonoBehaviour
{
    public static InterfaceController I;
    public const int SortOrder = 30;

    [Header("System References")]
    public SongGenerator generator;

    Canvas canvas; RectTransform overlay; CanvasGroup group; TMP_InputField input; Image dim, placeholder; CubeGlyph logo;
    int request;   // S1: the generation this prompt waits for (0 none)
    HudButton goBtn, diceBtn, closeBtn; RectTransform card;
    CubeGlyph[] loadingDots = new CubeGlyph[3]; GameObject loadingRow; float loadingHop;
    Image errorIcon; float errorT = 9f; bool visible; bool busy; float shown; bool fromMenu; int hopIdx;

    /// <summary>The prompt is up (or fading in).</summary>
    public bool Visible => visible;
    /// <summary>It was opened over the main menu (✕ goes back to the menu).</summary>
    public bool OpenedFromMenu => fromMenu;
    public bool Busy => busy;
    public Canvas Canvas => canvas;
    public HudButton DiceButton => diceBtn;
    public HudButton GoButton => goBtn;
    public HudButton CloseButton => closeBtn;
    /// <summary>Number of successful generations handled (tests).</summary>
    public int Successes { get; private set; }

    void Awake() { I = this; }

    void Start()
    {
        if (generator == null) generator = FindAnyObjectByType<SongGenerator>();
        if (overlay == null) Build();
    }

    void Build()
    {
        var cgo = new GameObject("PromptCanvas", typeof(RectTransform));
        cgo.layer = 5;
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();

        var go = UIKit.Obj("PromptOverlay", cgo.transform);
        overlay = UIKit.RT(go);
        UIKit.Stretch(overlay);
        group = go.AddComponent<CanvasGroup>();
        dim = go.AddComponent<Image>();
        dim.color = new Color(0.07f, 0.045f, 0.13f, 0.82f); dim.raycastTarget = true;   // comic restyle (package A): an ink-violet wash

        var lrt = HudKit.Node(go.transform, "Logo", new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(96f, 96f));
        logo = lrt.gameObject.AddComponent<CubeGlyph>(); logo.color = Comic.Opaque(Palette.Accent); logo.raycastTarget = false;

        // the paper card: wobbly ink, heavier on the shadow side, a hard violet print shadow (ink would vanish on the dark wash)
        var crt = HudKit.Node(go.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(690f, 150f));
        var paper = crt.gameObject.AddComponent<InkShape>();
        paper.Shape = InkShape.Kind.RoundRect; paper.color = Comic.Cream; paper.Radius = 20f; paper.Wobble = 2.2f; paper.WobbleWave = 90f; paper.RotJitter = 0.8f;
        paper.SetInk(2.6f, 4.4f); paper.ShadowColor = Comic.PrintShadow; paper.ShadowOffset = new Vector2(7f, -8f); paper.raycastTarget = true; paper.Seed = 2711;
        card = crt;

        // input field: a white paper strip with an ink outline
        var inGo = UIKit.Obj("Input", card);
        UIKit.Anchor(UIKit.RT(inGo), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(470f, 72f));
        var inBg = inGo.AddComponent<InkShape>();
        inBg.Shape = InkShape.Kind.RoundRect; inBg.color = Color.white; inBg.Radius = 16f; inBg.Wobble = 1.2f; inBg.SetInk(2.2f, 3f); inBg.HasShadow = false; inBg.RotJitter = 0.4f;
        inBg.raycastTarget = true;
        input = inGo.AddComponent<TMP_InputField>();
        var area = UIKit.Obj("Text Area", inGo.transform);
        area.AddComponent<RectMask2D>();
        UIKit.Stretch(UIKit.RT(area), 28f, 28f, 12f, 12f);
        // F23: no example words: a soft pencil glyph waits in the empty field (it breathes; the caret blinks beside it with focus)
        var ph = Comic.GlyphImage(area.transform, "Placeholder", "pencil", Comic.A(Comic.Ink, 0.45f), 38f, false);
        UIKit.Anchor(ph.rectTransform, new Vector2(0f, 0.5f), new Vector2(2f, 0f), ph.rectTransform.sizeDelta);
        placeholder = ph;
        var txt = UIKit.Text(area.transform, "Text", "", 30f, Comic.Ink, TextAlignmentOptions.MidlineLeft);
        UIKit.Stretch(UIKit.RT(txt.gameObject));
        input.textViewport = UIKit.RT(area);
        input.textComponent = txt;
        input.placeholder = ph;
        input.fontAsset = UIKit.Font;
        input.pointSize = 30f;
        input.caretColor = Comic.Magenta;
        input.customCaretColor = true;
        input.caretWidth = 3;
        input.selectionColor = Palette.AccentSoft;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 140;
        input.onSubmit.AddListener(_ => Submit());
        input.transition = Selectable.Transition.None;   // the paper strip stays white (no grey selection tint)

        // go: the hero sticker (ink, a cream sparkle, magenta shadow); the dice: a drawn die
        InkShape gb; InkPainter ga;
        goBtn = HudKit.Sticker(card, "Go", new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(70f, 64f), InkShape.Kind.Sticker, Comic.Ink, PaintSparkle, "create", Submit, out gb, out ga);
        gb.Ink = Comic.Cream; gb.SetInk(1.8f, 2.6f); gb.ShadowColor = Comic.Magenta; gb.ShadowOffset = new Vector2(5f, -6f);
        (goBtn.transform as RectTransform).localEulerAngles = new Vector3(0f, 0f, -4f);
        var gh = goBtn.GetComponent<InkHover>(); gh.jiggle = 1.5f; gh.baseAngle = -4f; gh.hoverScale = 1.08f;
        InkShape db; InkPainter da;
        diceBtn = HudKit.Sticker(card, "Dice", new Vector2(1f, 0.5f), new Vector2(-142f, 0f), new Vector2(54f, 52f), InkShape.Kind.Sticker, Comic.Cream, PaintDie, "roll one", Offline, out db, out da);
        db.SetInk(1.8f, 2.8f); db.ShadowOffset = new Vector2(3f, -4f);
        InkShape xb; InkPainter xa;
        closeBtn = HudKit.Sticker(go.transform, "Close", new Vector2(1f, 1f), new Vector2(-44f, -42f), new Vector2(40f, 40f), InkShape.Kind.Circle, Comic.Cream, PaintX, "close", Hide, out xb, out xa);
        xb.SetInk(1.8f, 2.6f); xb.ShadowColor = Comic.PrintShadow; xb.ShadowOffset = new Vector2(3f, -4f);

        loadingRow = UIKit.Obj("Loading", go.transform);
        UIKit.Anchor(UIKit.RT(loadingRow), new Vector2(0.5f, 0.5f), new Vector2(0f, -104f), new Vector2(120f, 40f));
        for (int i = 0; i < 3; i++)
        {
            var drt = HudKit.Node(loadingRow.transform, "Dot" + i, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 30f, 0f), new Vector2(22f, 22f));
            var d = drt.gameObject.AddComponent<CubeGlyph>(); d.color = Comic.Opaque(Palette.Accent); d.raycastTarget = false;
            loadingDots[i] = d;
        }
        loadingRow.SetActive(false);

        errorIcon = UIKit.Image(go.transform, "Error", "warning", Palette.Danger, new Vector2(50f, 50f));
        errorIcon.sprite = Comic.Glyph("warning", 40f, true);
        UIKit.Anchor(errorIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(50f, 50f));
        errorIcon.gameObject.SetActive(false);
        go.SetActive(false);
    }

    public void Show()
    {
        if (overlay == null) Build();
        if (generator == null) generator = FindAnyObjectByType<SongGenerator>();
        fromMenu = MainMenu.IsShown;
        visible = true;
        overlay.gameObject.SetActive(true);
        group.interactable = true; group.blocksRaycasts = true;
        dim.color = new Color(0.07f, 0.045f, 0.13f, fromMenu ? 0.66f : 0.82f);   // over the menu the wall stays visible
        logo.gameObject.SetActive(!fromMenu);                                    // over the menu the title is already in the wall
        closeBtn.gameObject.SetActive(true);
        WorldInput.Lock("prompt", true);
        if (!fromMenu && UIManager.I != null) UIManager.I.SetHudVisible(false);
        input.ActivateInputField();
        input.Select();
    }

    /// <summary>✕ / Esc: back to where the prompt was opened (from the world without a song: to the menu).</summary>
    public void Hide() { Close(true); }

    /// <summary>Hides the prompt. <paramref name="toMenuIfNoSong"/>: opened from the world with no song, open the menu instead of an empty world.</summary>
    public void Close(bool toMenuIfNoSong)
    {
        if (overlay == null || !visible) return;
        if (busy) { if (generator != null) generator.Cancel(); SetBusy(false); }   // F3 / S1: closing cancels the generation: its song never lands
        visible = false;
        group.interactable = false; group.blocksRaycasts = false;
        input.DeactivateInputField();
        // integration fix: a hidden input field that stays the EventSystem's selection makes InputUtil.TypingInField true, which
        // silently blocks every hotkey (Space, P, 1-9, undo...) until the next click
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.transform.IsChildOf(overlay)) es.SetSelectedGameObject(null);
        WorldInput.Unlock("prompt");
        if (fromMenu) return;   // the menu is still up behind it
        bool hasSong = SongManager.I != null && SongManager.I.HasSong;
        if (hasSong || !toMenuIfNoSong) { if (UIManager.I != null) UIManager.I.SetHudVisible(true); }
        else MainMenu.Show();
    }

    void Submit()
    {
        if (busy || generator == null) return;
        string p = input.text.Trim();
        if (string.IsNullOrEmpty(p)) { Offline(); return; }
        SetBusy(true);
        request = generator.GenerateNewSong(p, OnDone);
    }

    void Offline()
    {
        if (busy || generator == null) return;
        SetBusy(true);
        request = generator.GenerateOffline(input.text, OnDone);
    }

    void SetBusy(bool b)
    {
        busy = b;
        if (!b) request = 0;
        loadingRow.SetActive(b);
        goBtn.interactable = !b; diceBtn.interactable = !b;
        if (b) errorIcon.gameObject.SetActive(false);
    }

    void OnDone(bool ok)
    {
        SetBusy(false);
        if (!visible) return;   // S1: never for a closed prompt (Close cancels; the generator drops a cancelled result)
        if (!ok) { errorT = 0f; errorIcon.gameObject.SetActive(true); AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 0.7f); return; }
        Successes++;
        AudioPool.UI(ProceduralAudio.Sparkle(), 0.45f);
        GlobalClock.Stop();   // a fresh song waits for play (SPEC v3 §3.2), whichever generator path built it
        bool overMenu = fromMenu && MainMenu.IsShown;
        Close(false);
        if (overMenu) MainMenu.LeaveToWorld(UIKit.ScreenOf(diceBtn), () => Onboarding.StartTutorial(false));
        else Onboarding.StartTutorial(false);
    }

    void Update()
    {
        if (overlay == null) return;
        float dt = Time.unscaledDeltaTime;
        shown = Mathf.Lerp(shown, visible ? 1f : 0f, 1f - Mathf.Exp(-dt * 12f));
        group.alpha = shown;
        if (card != null) card.localScale = Vector3.one * (0.94f + 0.06f * shown);
        if (!visible && shown < 0.01f && overlay.gameObject.activeSelf) overlay.gameObject.SetActive(false);
        // S1: a generation that ended without calling back (cancelled elsewhere, e.g. Learn; or refused because one was running) frees the prompt
        if (busy && (generator == null || !generator.IsGenerating || generator.RequestId != request)) SetBusy(false);
        if (placeholder != null && placeholder.enabled) placeholder.color = Comic.A(Comic.Ink, 0.45f + 0.15f * Mathf.Sin(Time.unscaledTime * 2.6f));
        if (busy)
        {
            // the waiting cubes hop one after the other (on twos)
            loadingHop += dt;
            if (loadingHop >= 0.22f) { loadingHop = 0f; hopIdx = (hopIdx + 1) % 3; loadingDots[hopIdx].Hop(0.6f); }
        }
        if (errorIcon.gameObject.activeSelf)
        {
            errorT += dt;
            float shake = Mathf.Sin(errorT * 40f) * 8f * Mathf.Max(0f, 1f - errorT * 2.5f);
            errorIcon.rectTransform.anchoredPosition = new Vector2(shake, -100f);
            errorIcon.color = Palette.A(Palette.Danger, Mathf.Clamp01(3f - errorT));
            if (errorT > 3f) errorIcon.gameObject.SetActive(false);
        }
        if (visible && Input.GetKeyDown(KeyCode.Escape)) Hide();   // F3: Esc while busy cancels too
    }

    // ------------------------------------------------------------------ v4 pictures
    static readonly System.Collections.Generic.List<Vector2> pts = new System.Collections.Generic.List<Vector2>(24);

    static void PaintSparkle(InkPainter p)
    {
        Vector2 c = p.Area.center + new Vector2(-2f, 0f);
        pts.Clear();
        for (int i = 0; i < 8; i++) { float a = (90f + i * 45f) * Mathf.Deg2Rad; float r = i % 2 == 0 ? 17f : 5.5f; pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r + p.Jit(i, 0.5f)); }
        p.Fill(pts, pts.Count, Comic.Cream);
        pts.Clear();
        for (int i = 0; i < 8; i++) { float a = (90f + i * 45f) * Mathf.Deg2Rad; float r = i % 2 == 0 ? 7f : 2.4f; pts.Add(c + new Vector2(13f, 11f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
        p.Fill(pts, pts.Count, Comic.Pop);
    }

    static void PaintDie(InkPainter p)
    {
        Vector2 c = p.Area.center;
        p.Cube(c, 30f, Color.white, Comic.Ink, 1.8f);
        p.Disc(c + new Vector2(0f, 7.5f), 2.2f, Comic.Ink);
        p.Disc(c + new Vector2(-8f, -1f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(-5f, -8f), 1.9f, Comic.Ink);
        p.Disc(c + new Vector2(6f, -2f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(8f, -7f), 1.9f, Comic.Ink); p.Disc(c + new Vector2(5f, -11f), 1.9f, Comic.Ink);
    }

    static void PaintX(InkPainter p)
    {
        Vector2 c = p.Area.center; float r = 7f;
        p.Line(c + new Vector2(-r, -r), c + new Vector2(r, r), 3f, Comic.Ink, true);
        p.Line(c + new Vector2(-r, r), c + new Vector2(r, -r), 3f, Comic.Ink, true);
    }
}
