using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu (SPEC v3 §5.1), Tonecraft style and alive under the pointer. The title is not text: DEACUBE is spelled by the
/// wall's own cubes (<see cref="MenuStage"/>), revealed by a light sweep and a column-by-column rise that plays its name.
/// Shown at boot and from the HUD home button (leaving the world autosaves to <see cref="SongIO.AutosavePath"/>, never the
/// user's main save). While shown: WorldInput is locked ("menu", keys too), the HUD is hidden, the world camera is off and the
/// stage camera renders the menu layer only; <see cref="MenuMusic"/> plays. Own canvas above the HUD: a vignette and, v4
/// (SPEC §7b, ui_research §4.7), a Melatonin text menu left-aligned under the title's D — lowercase words, no boxes: continue
/// (newest save/autosave), new song (vibe prompt), learn (offline dice song + tutorial from the start), quit; the focused word
/// (pointer or ↑ ↓, Enter invokes it) wears a wavy ink underline and the wall behind it lights up and hops on the beat.
/// Leaving (v4, replaces the v3 wall that broke apart): <see cref="MenuLeave"/>'s hand-made comic cut — the clicked word squashes,
/// the frame holds (captured; behind the held copy the world camera takes over), a two-tone impact frame with a lettered sticker,
/// then the held frame cut into slanted panels slides and swings off on twos over the live world (a fresh song's island rising),
/// the HUD comes back at the end; ≈1.08 s. <see cref="Hide"/> restores the world at once (tests call it first).
/// Fixes (v3 review): quitting autosaves too (F15); Learn cancels a pending generation before it builds its song and backs up a song
/// with cubes first (S1, F15); a Continue whose file fails to load tries the other slot before shaking (S10).
/// v5 (SPEC §4): a fifth word, "gallery" (between new song and learn; hidden while the gallery has no songs), swaps the words for
/// <see cref="MenuGallery"/>'s shelf of demo-song cards (their previews play while the menu music holds, the wall lights up in the song's
/// vibe and hops on its beat); choosing a card opens it (<see cref="OpenFromGallery"/>: Gallery.Open — a song with cubes backed up first,
/// never the user's main save —, the camera framing the song (all of it when it fits before the fog, else its opening columns), the comic
/// leave "HIT IT!" on the song's own chord, the song playing from bar 0 once the world is back); "back" / Esc returns to the words.
/// "My songs" (<see cref="SongLibrary"/>): a word after continue (shown while the player has songs) swaps the words for <see cref="MenuSongs"/>'
/// shelf of the player's own songs ("+ new song", then a card per song: open it, rename, duplicate, delete with undo). The word lives on its own
/// small canvas over a slot in the word column, so <see cref="Words"/> and the menu canvas keep the five words. With the library on, Continue
/// resumes the world's song as it was left, or (no song in the world: the boot) opens the current library song; leaving the world and quitting
/// also save the world's song into its library file (SongLibrary.AutoSave — never while SongIO.QuitAutosave is off).
/// </summary>
public class MainMenu : MonoBehaviour
{
    public static MainMenu I;
    public static bool IsShown => I != null && I.shown;
    /// <summary>Opens the menu (from the world: fade to dark, then the quick reveal). Autosaves the song first.</summary>
    public static void Show() { var m = Ensure(); if (m != null) m.Enter(false); }
    /// <summary>Closes the menu at once and restores the world: main camera, HUD, WorldInput (works with or without a song).</summary>
    public static void Hide() { if (I != null) I.HideNow(); }

    // ------------------------------------------------------------------ v3 D additions
    public const int SortOrder = 20;
    public enum Phase { Hidden, Entering, Shown, Leaving }
    public static Phase State => I != null ? I.phase : Phase.Hidden;
    public static MenuStage Stage => I != null ? I.stage : null;
    public static MenuMusic Music => I != null ? I.music : null;
    public static Canvas MenuCanvas => I != null ? I.canvas : null;
    /// <summary>The world camera the menu switches off (the scene's main camera).</summary>
    public static Camera WorldCamera => I != null ? I.mainCam : null;
    public static MenuButton ContinueButton => I != null ? I.continueBtn : null;
    public static MenuButton NewButton => I != null ? I.newBtn : null;
    public static MenuButton LearnButton => I != null ? I.learnBtn : null;
    public static MenuButton QuitButton => I != null ? I.quitBtn : null;
    /// <summary>v5: the "gallery" word (hidden while <see cref="Gallery.Entries"/> is empty).</summary>
    public static MenuButton GalleryButton => I != null ? I.galleryBtn : null;
    /// <summary>v5: the menu's words in order — continue, new song, gallery, learn, quit — hidden ones included.</summary>
    public static MenuButton[] Words => I != null && I.buttons != null ? (MenuButton[])I.buttons.Clone() : new MenuButton[0];
    /// <summary>v5: the gallery shelf (its cards, focus and preview).</summary>
    public static MenuGallery Shelf => I != null ? I.gallery : null;
    public static bool ShelfOpen => I != null && I.gallery != null && I.gallery.IsOpen;
    /// <summary>"my songs" (SongLibrary): the word after continue — kept out of <see cref="Words"/> and off the menu canvas (its own small canvas
    /// over a slot in the word column), shown while the player has songs.</summary>
    public static MenuButton SongsButton => I != null ? I.songsBtn : null;
    /// <summary>The "my songs" shelf (the player's songs as cards: open, rename, duplicate, delete).</summary>
    public static MenuSongs SongsShelf => I != null ? I.songs : null;
    public static bool SongsOpen => I != null && I.songs != null && I.songs.IsOpen;
    /// <summary>Library songs opened from the "my songs" shelf this session (tests).</summary>
    public static int SongOpens { get; private set; }
    /// <summary>Opens the menu with the "my songs" shelf up (the HUD strip's "my songs", ⌘O).</summary>
    public static void ShowMySongs()
    {
        var m = Ensure();
        if (m == null) return;
        m.songsPending = true;
        m.Enter(false);
        m.OpenSongsIfPending();
    }
    /// <summary>v5: gallery songs opened from the shelf this session (tests).</summary>
    public static int GalleryOpens { get; private set; }
    /// <summary>v5: the words' opacity (0 while the shelf is open; tests).</summary>
    public static float WordsAlpha => I != null && I.itemsGroup != null ? I.itemsGroup.alpha : 1f;
    /// <summary>Number of autosaves written this session (tests).</summary>
    public static int Autosaves { get; private set; }
    /// <summary>v3: true while leaving used the URP camera stack. v4: the panel leave always shows the live world behind a held
    /// frame and never stacks cameras, so this stays false.</summary>
    public static bool LastLeaveStacked { get; private set; }
    /// <summary>v3 experiment (the menu camera stacked over the world while the wall fell). v4: ignored — the panel leave reveals the
    /// world by itself (kept for API compatibility).</summary>
    public static bool StackedLeave = false;
    /// <summary>v4: the exit transition (tests read its drawings, panels and capture state).</summary>
    public static MenuLeave Transition => I != null ? I.leave : null;
    /// <summary>v4: the focused menu word (underlined; Enter invokes it).</summary>
    public static MenuButton FocusedButton => I != null ? I.FocusedItem : null;
    /// <summary>v4: moves the focus to the next (+1) / previous (-1) visible word, as ↓ / ↑ do (v5: along the gallery's cards while its shelf is open).</summary>
    public static void MoveFocus(int dir)
    {
        if (I == null || !I.shown || I.phase == Phase.Leaving) return;
        if (I.songs != null && I.songs.IsOpen) I.songs.Step(dir);
        else if (I.gallery != null && I.gallery.IsOpen) I.gallery.Step(dir); else I.StepFocus(dir);
    }

    /// <summary>Tests: drive the wall with a synthetic pointer (screen px) instead of the mouse.</summary>
    public static void SimulatePointer(Vector2 screen) { if (I != null && I.stage != null) I.stage.PointerOverride = screen; }
    public static void ReleasePointer() { if (I != null && I.stage != null) I.stage.PointerOverride = null; }
    /// <summary>Tests: a click on the wall at a screen point (ripple + strum).</summary>
    public static void SimulateClick(Vector2 screen) { if (I != null && I.shown) I.WallClick(screen); }
    /// <summary>Animated exit into the world; <paramref name="after"/> runs once the world is back.</summary>
    public static void LeaveToWorld(Action after) { if (I != null) I.Leave(null, after, null, "WHAM!"); }
    public static void LeaveToWorld(Vector2 fromScreen, Action after) { if (I != null) I.Leave(fromScreen, after, null, "POP!"); }
    /// <summary>Plays the boot intro again (tests, captures).</summary>
    public static void ReplayIntro() { if (I != null && I.shown && I.phase != Phase.Leaving) I.StartReveal(false, true); }

    public static MainMenu Ensure()
    {
        if (I != null) return I;
        var found = FindAnyObjectByType<MainMenu>();
        if (found != null) { I = found; return I; }
        if (!Application.isPlaying) return null;
        return new GameObject("MainMenu").AddComponent<MainMenu>();
    }

    // ------------------------------------------------------------------ state
    MenuStage stage;
    MenuLeave leave;
    MenuGallery gallery;
    MenuSongs songs;
    readonly MenuMusic music = new MenuMusic();
    Canvas canvas; RectTransform root, front, items; CanvasGroup frontGroup, itemsGroup; Image cover, vignette, footer;
    MenuButton continueBtn, newBtn, galleryBtn, learnBtn, quitBtn, songsBtn;
    Func<double> galleryBeat;
    MenuButton[] buttons;   // the five words of Words: continue, new song, gallery, learn, quit
    MenuButton[] order;     // the focus order (↑ ↓): continue, my songs, new song, gallery, learn, quit
    RectTransform songsSlot; Canvas songsCanvas; CanvasGroup songsGroup; bool songsPending;
    Camera mainCam;
    bool shown, bootPending, built, suspendedByMenu, hudShownOnLeave, learnBusy, swapped, revealStarted, worldOn;
    Phase phase = Phase.Hidden;
    float t, uiAlpha, coverAlpha, revealAt;
    int focus = -1;
    Vector4 laidOut = new Vector4(-1f, -1f, -1f, -1f);
    Action afterLeave;
    const float WorldFade = 0.22f;
    /// <summary>The words, reference px (1080p): the primary one is bigger (continue, or new song when there is nothing to continue).</summary>
    const float WordPx = 34f, PrimaryPx = 44f, QuitPx = 26f;

    static bool glyphs;
    static void RegisterGlyphs()
    {
        if (glyphs) return;
        glyphs = true;
        IconFactory.Register("power", p => IconFactory.P.U(IconFactory.P.Arc(p, 0f, -0.08f, 0.62f, 0.1f, 122f, 418f), IconFactory.P.Seg(p, 0f, 0.02f, 0f, 0.8f, 0.1f)));
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
        bootPending = true;
        RegisterGlyphs();
        Gallery.OnEntriesChanged += OnGalleryChanged;
        SongLibrary.OnChanged += OnSongsChanged;
    }

    void Start()
    {
        EnsureBuilt();
        if (bootPending) { bootPending = false; Enter(true); }
    }

    void OnDestroy() { Gallery.OnEntriesChanged -= OnGalleryChanged; SongLibrary.OnChanged -= OnSongsChanged; if (I == this) { music.Stop(); I = null; } }

    /// <summary>F15: quitting keeps the song (the autosave slot, never the user's main save) and, "my songs", its library file (a song with the
    /// player's work gets an entry). SPEC v4: tests switch QuitAutosave off — then neither is written.</summary>
    void OnApplicationQuit()
    {
        if (I != this || !SongIO.QuitAutosave || SongManager.I == null || !SongManager.I.HasSong) return;
        if (SongIO.SaveTo(SongIO.AutosavePath)) Autosaves++;
        SongLibrary.AutoSave();
    }

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        RegisterGlyphs();
        if (mainCam == null) mainCam = FindWorldCamera();
        stage = MenuStage.Create(transform);
        stage.Music = music;
        BuildUI();
        gallery = MenuGallery.Create(transform, this, music);
        galleryBeat = () => gallery != null ? gallery.Preview.Beat : double.NaN;
        songs = MenuSongs.Create(transform, this, music);
        leave = MenuLeave.Create(transform);
    }

    static Camera FindWorldCamera()
    {
        var c = Camera.main;
        if (c != null) return c;
        if (OrbitCamera.I != null) { c = OrbitCamera.I.GetComponent<Camera>(); if (c != null) return c; }
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (cam.CompareTag("MainCamera")) return cam;
        return null;
    }

    // ------------------------------------------------------------------ UI
    void BuildUI()
    {
        var go = new GameObject("MenuCanvas", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        root = UIKit.RT(go);

        // a vignette that frames the wall (the title lives in the wall itself: no text here)
        vignette = UIKit.Image(root, "Vignette", "white", new Color(0.004f, 0.004f, 0.012f, 0.72f), Vector2.one);
        vignette.sprite = MenuSprites.VignetteHole; vignette.preserveAspect = false;
        UIKit.Stretch(vignette.rectTransform, -260f, -260f, -200f, -200f);

        var fgo = UIKit.Obj("Front", root);
        front = UIKit.RT(fgo);
        UIKit.Stretch(front);
        frontGroup = fgo.AddComponent<CanvasGroup>();

        // the words: lowercase, no boxes, left-aligned under the title's D (placed in LayoutItems once the stage camera is known)
        var col = UIKit.Obj("Items", front);
        items = UIKit.RT(col);
        items.anchorMin = items.anchorMax = new Vector2(0.5f, 0.5f); items.pivot = new Vector2(0f, 1f);
        items.sizeDelta = new Vector2(460f, 400f);
        itemsGroup = col.AddComponent<CanvasGroup>();
        var vg = col.AddComponent<VerticalLayoutGroup>();
        vg.childAlignment = TextAnchor.UpperLeft; vg.spacing = 6f;
        vg.childControlWidth = false; vg.childControlHeight = false; vg.childForceExpandWidth = false; vg.childForceExpandHeight = false;
        continueBtn = MenuButton.CreateText(col.transform, "Continue", "continue", WordPx, PrimaryPx, true);
        songsSlot = UIKit.RT(UIKit.Obj("MySongsSlot", col.transform));   // "my songs" sits here (its word lives on its own canvas: BuildSongsWord)
        newBtn = MenuButton.CreateText(col.transform, "New", "new song", WordPx, PrimaryPx, false);
        galleryBtn = MenuButton.CreateText(col.transform, "Gallery", "gallery", WordPx, WordPx, false);
        learnBtn = MenuButton.CreateText(col.transform, "Learn", "learn", WordPx, WordPx, false);
        quitBtn = MenuButton.CreateText(col.transform, "Quit", "quit", QuitPx, QuitPx, false);
        buttons = new[] { continueBtn, newBtn, galleryBtn, learnBtn, quitBtn };
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i]; int idx = i;
            b.index = i;
            b.onHover = () => SetFocus(idx, true);
            b.onPress = () => { if (stage != null) stage.Press(b.PressPoint, b.primary ? Palette.Accent : new Color(0.55f, 0.6f, 1f)); };
        }
        BuildSongsWord();
        continueBtn.onClick = DoContinue;
        newBtn.onClick = DoNew;
        galleryBtn.onClick = DoGallery;
        learnBtn.onClick = DoLearn;
        quitBtn.onClick = DoQuit;

        footer = UIKit.Image(front, "Footer", "cube", new Color(1f, 1f, 1f, 0.45f), new Vector2(36f, 36f));
        UIKit.Anchor(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(36f, 36f));

        cover = UIKit.Image(root, "Cover", "white", new Color(0.012f, 0.013f, 0.024f, 1f), Vector2.one);
        cover.preserveAspect = false;
        UIKit.Stretch(cover.rectTransform);
        cover.raycastTarget = false;
        SetCover(0f);
        canvas.gameObject.SetActive(false);
    }

    void SetCover(float a)
    {
        coverAlpha = a;
        if (cover == null) return;
        var c = cover.color; c.a = a; cover.color = c;
        cover.raycastTarget = a > 0.02f || phase == Phase.Leaving;
        cover.enabled = a > 0.001f || phase == Phase.Leaving;
    }

    void RefreshButtons()
    {
        bool hasSong = SongManager.I != null && SongManager.I.HasSong;
        bool canContinue = SongIO.AnyExists || hasSong || (SongLibrary.Enabled && SongLibrary.CurrentExists);
        continueBtn.gameObject.SetActive(canContinue);
        continueBtn.SetPrimary(canContinue);
        galleryBtn.gameObject.SetActive(Gallery.Entries.Count > 0);   // v5: the word only when there are songs to show
        if (gallery != null) gallery.CloseImmediate();
        ShowSongsWord(SongLibrary.HasSongs);   // "my songs": the word only while the player has songs
        if (songs != null) songs.CloseImmediate();
        newBtn.SetPrimary(!canContinue);
        foreach (var b in order) { b.interactable = true; b.SetBusy(false); b.Unsquash(); }
        learnBusy = false;
        focus = -1;
        SetFocusOn(canContinue ? continueBtn : newBtn, false);
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
    }

    // ------------------------------------------------------------------ focus (pointer or keys) and layout
    MenuButton FocusedItem => order != null && focus >= 0 && focus < order.Length ? order[focus] : null;

    /// <summary>Focuses word <paramref name="i"/> of <see cref="Words"/> (continue 0 … quit 4).</summary>
    void SetFocus(int i, bool sound) { if (buttons != null && i >= 0 && i < buttons.Length) SetFocusOn(buttons[i], sound); }

    /// <summary>Focuses <paramref name="b"/> (any word, "my songs" included; the focus is its place in the focus order).</summary>
    void SetFocusOn(MenuButton b, bool sound)
    {
        if (order == null || b == null || !b.gameObject.activeSelf) return;
        int i = Array.IndexOf(order, b);
        if (i < 0 || i == focus) return;
        focus = i;
        for (int k = 0; k < order.Length; k++) order[k].SetFocused(k == i);
        if (sound) music.Hover(i);
    }

    /// <summary>v5: the gallery's list changed (tests inject songs; the builder wrote new ones): the word follows, the shelf rebuilds.</summary>
    void OnGalleryChanged()
    {
        if (galleryBtn == null) return;
        bool has = Gallery.Entries.Count > 0;
        galleryBtn.gameObject.SetActive(has);
        if (!has && FocusedItem == galleryBtn) { focus = -1; SetFocusOn(continueBtn.gameObject.activeSelf ? continueBtn : newBtn, false); }
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
        if (gallery != null) gallery.MarkDirty();
    }

    void StepFocus(int dir)
    {
        if (order == null) return;
        int n = order.Length, i = focus < 0 ? 0 : focus;
        for (int s = 0; s < n; s++) { i = (i + (dir >= 0 ? 1 : -1) + n) % n; if (order[i].gameObject.activeSelf) { SetFocusOn(order[i], true); return; } }
    }

    /// <summary>Puts the words' top-left corner under the title's D, a little below the word (the camera at rest), whenever the
    /// screen or the stage's framing changes.</summary>
    void LayoutItems()
    {
        if (stage == null || items == null || root == null) return;
        var key = new Vector4(Screen.width, Screen.height, stage.CamDist, root.lossyScale.x);
        if (key == laidOut) return;
        laidOut = key;
        Vector2 screen = stage.RestScreenPoint(new Vector2(MenuStage.WordLeft + 0.1f, MenuStage.WordBottom - 2.3f), MenuStage.LetterDepth);
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local)) items.anchoredPosition = local;
    }

    // ------------------------------------------------------------------ enter / leave
    void Enter(bool boot)
    {
        EnsureBuilt();
        if (phase == Phase.Shown || phase == Phase.Entering) return;
        bootPending = false;
        if (phase == Phase.Leaving) FinishLeave(false);
        if (mainCam == null) mainCam = FindWorldCamera();
        bool hasSong = SongManager.I != null && SongManager.I.HasSong;
        bool fromWorld = !boot && mainCam != null && mainCam.enabled;

        // the world steps aside: autosave (never the user's main save), pause, close whatever owns the pointer
        if (hasSong && SongIO.SaveTo(SongIO.AutosavePath)) Autosaves++;
        if (hasSong) SongLibrary.AutoSave();   // "my songs": the world's song keeps its library file (never while SongIO.QuitAutosave is off)
        FocusLoop.Dismiss();   // SPEC v4 R4: entering the menu ends a focus loop (restores the transport it found) before the pause
        if (GlobalClock.IsPlaying) GlobalClock.Pause();
        if (CubeInspector.IsOpen) CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        if (Presenter.Active) Presenter.Exit();
        if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.CancelPath();
        if (UIManager.I != null) { UIManager.I.CloseRings(); UIManager.I.SetHudVisible(false); }
        WorldInput.Lock("menu", true);
        if (OrbitCamera.I != null && !OrbitCamera.I.Suspended) { OrbitCamera.I.Suspended = true; suspendedByMenu = true; }

        shown = true; phase = Phase.Entering; t = 0f; afterLeave = null;
        canvas.gameObject.SetActive(true);
        if (leave != null) leave.Warm();   // once: the leave's shader and sticker meet the GPU during the intro, not on its held frame
        RefreshButtons();
        OpenSongsIfPending();
        uiAlpha = 0f; frontGroup.alpha = 0f; frontGroup.interactable = false; frontGroup.blocksRaycasts = false;
        if (fromWorld)
        {
            // fade the world to dark first, then the quick reveal
            swapped = false; revealStarted = false;
            SetCover(0.001f);
        }
        else
        {
            SwapToStage();
            StartReveal(false, false);
            SetCover(1f);
        }
        Onboarding.Notify(Onboarding.Ev.MenuShown);
    }

    void SwapToStage()
    {
        swapped = true;
        stage.Activate();
        if (mainCam != null) mainCam.enabled = false;
        music.Start();
    }

    // the world's fog would wash the wall (the stage is ~35 u from its camera): off while the stage is on screen, restored after
    bool fogSaved, fogWas;
    void LateUpdate()
    {
        bool stageOn = stage != null && stage.Active && swapped;
        if (stageOn) { if (!fogSaved) { fogWas = RenderSettings.fog; fogSaved = true; } RenderSettings.fog = false; }
        else if (fogSaved) { RenderSettings.fog = fogWas; fogSaved = false; }
    }

    void StartReveal(bool quick, bool replay)
    {
        revealStarted = true;
        if (replay) { music.Stop(); music.Start(); phase = Phase.Entering; t = 0f; uiAlpha = 0f; SetCover(1f); }
        stage.BeginReveal(quick);
        revealAt = t;
    }

    void HideNow()
    {
        bootPending = false;
        bool wasShown = shown || phase != Phase.Hidden;
        if (!wasShown) { WorldInput.Unlock("menu"); if (gallery != null) gallery.CloseImmediate(); if (songs != null) songs.CloseImmediate(); return; }
        FinishLeave(false);
    }

    /// <summary>Restores the world. <paramref name="runAfter"/>: also run the pending after-leave action.</summary>
    void FinishLeave(bool runAfter)
    {
        shown = false; phase = Phase.Hidden; swapped = false; revealStarted = false; worldOn = false;
        if (leave != null) leave.End();
        if (gallery != null) gallery.CloseImmediate();
        if (songs != null) songs.CloseImmediate();
        if (songsCanvas != null) songsCanvas.gameObject.SetActive(false);
        if (stage != null) { stage.SetTitleDown(false, true); stage.Deactivate(); }
        if (fogSaved) { RenderSettings.fog = fogWas; fogSaved = false; }
        if (mainCam == null) mainCam = FindWorldCamera();
        if (mainCam != null) mainCam.enabled = true;
        if (suspendedByMenu && OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        suspendedByMenu = false;
        WorldInput.Unlock("menu");
        if (UIManager.I != null) UIManager.I.SetHudVisible(true);
        music.Stop();
        if (canvas != null) canvas.gameObject.SetActive(false);
        SetCover(0f);
        if (InterfaceController.I != null && InterfaceController.I.Visible && InterfaceController.I.OpenedFromMenu) InterfaceController.I.Close(false);
        var a = afterLeave; afterLeave = null;
        if (runAfter && a != null) a();
    }

    /// <summary>v4 (SPEC §7b): the comic panel leave from <paramref name="fromScreen"/> (the screen centre when null); <paramref name="item"/>
    /// (a menu word, or null when the leave comes from elsewhere, e.g. the prompt's dice) squashes; <paramref name="word"/> is lettered on
    /// the impact sticker. Input is off at once (the menu canvas group blocks, CanAct refuses); the words keep their look for the
    /// held frame.</summary>
    void Leave(Vector2? fromScreen, Action after, MenuButton item, string word, int[] hitChord = null)
    {
        if (!shown || phase == Phase.Leaving) return;
        EnsureBuilt();
        if (!swapped) { SwapToStage(); }
        if (gallery != null) { gallery.StopPreview(true); gallery.BlockInput(); }   // v5: a gallery preview never outlives the menu
        if (songs != null) songs.BlockInput();
        phase = Phase.Leaving; t = 0f; afterLeave = after; hudShownOnLeave = false; worldOn = false;
        uiAlpha = 1f;
        foreach (var b in order) { b.interactable = true; b.SetBusy(false); }
        if (item != null) SetFocusOn(item, false);
        music.Stop();
        music.LeaveHit(MenuLeave.ImpactAt * MenuLeave.Drawing, hitChord);
        if (mainCam == null) mainCam = FindWorldCamera();
        LastLeaveStacked = false;
        SetCover(0f);
        Vector2 origin = fromScreen ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        leave.Begin(origin, item, word);
    }

    /// <summary>Behind the held frame: the stage goes dark, the world camera renders (the panels will reveal it), the menu canvas is
    /// retired (the held frame shows it from now on).</summary>
    void SwapToWorld()
    {
        worldOn = true;
        if (stage != null && stage.Active) stage.Deactivate();
        if (mainCam == null) mainCam = FindWorldCamera();
        if (mainCam != null) mainCam.enabled = true;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (gallery != null) gallery.HideCanvas();
        if (songs != null) songs.HideCanvas();
        if (songsCanvas != null) songsCanvas.gameObject.SetActive(false);
        RiseFreshIslands(MenuLeave.Seconds + 0.04f - leave.T);
    }

    /// <summary>"The island rising": a fresh song's islands in view (no cubes on any island yet) rise from the sea behind the panels and
    /// land just after the leave ends (KeyBlock.RiseIn, 0.7 s; its landing flourish belongs to the world). A song with cubes stays as it
    /// was left (a rise would leave its paths behind).</summary>
    void RiseFreshIslands(float landIn)
    {
        IslandsRisen = 0;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || mainCam == null) return;
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; var kb = c.Island; if (kb != null && !kb.IsMoon) return; }
        int risen = 0;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.Rising || risen >= 4) continue;
            Vector3 v = mainCam.WorldToViewportPoint(kb.Center);
            if (v.z <= 0f || v.x < -0.1f || v.x > 1.1f || v.y < -0.1f || v.y > 1.1f) continue;
            kb.RiseIn(Mathf.Max(0f, landIn - 0.7f) + 0.05f * risen);
            risen++;
        }
        IslandsRisen = risen;
    }
    /// <summary>Islands the last leave raised from the sea (tests).</summary>
    public static int IslandsRisen { get; private set; }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        MenuLeave.ApplyWorldFreeze(leave != null && leave.Pinned);   // captures only; lapses by itself
        if (phase == Phase.Hidden) { music.TickPendingOnly(); return; }
        t += dt;
        switch (phase)
        {
            case Phase.Entering:
                if (!swapped)
                {
                    SetCover(Mathf.Clamp01(t / WorldFade));
                    if (t >= WorldFade) { SwapToStage(); StartReveal(true, false); }
                    break;
                }
                music.Tick();
                if (coverAlpha > 0f) SetCover(Mathf.MoveTowards(coverAlpha, 0f, dt / 0.5f));
                if (stage.RevealTime >= stage.UiAt || stage.RevealDone) uiAlpha = Mathf.MoveTowards(uiAlpha, 1f, dt / 0.45f);
                if (uiAlpha >= 0.99f && coverAlpha <= 0f) phase = Phase.Shown;
                break;
            case Phase.Shown:
                music.Tick();
                if (coverAlpha > 0f) SetCover(Mathf.MoveTowards(coverAlpha, 0f, dt / 0.5f));
                uiAlpha = Mathf.MoveTowards(uiAlpha, 1f, dt / 0.3f);
                UpdateShownInput();
                break;
            case Phase.Leaving:
                music.TickPendingOnly();
                leave.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.2f));   // a hitch skips drawings rather than stretching the leave
                // the held frame is up (or, without a capture, the impact frame covers the screen): the world takes over behind it
                if (!worldOn && (leave.Captured || (leave.CaptureFailed && leave.DrawingNow >= MenuLeave.ImpactAt) || leave.DrawingNow >= MenuLeave.PanelsAt)) SwapToWorld();
                if (!hudShownOnLeave && leave.DrawingNow >= MenuLeave.HudAt) { hudShownOnLeave = true; if (UIManager.I != null) UIManager.I.SetHudVisible(true); }
                if (leave.T >= MenuLeave.Seconds) FinishLeave(true);
                break;
        }
        if (frontGroup != null)
        {
            frontGroup.alpha = uiAlpha;
            bool live = (phase == Phase.Shown || (phase == Phase.Entering && uiAlpha > 0.6f)) && !PromptOpen;
            frontGroup.interactable = live; frontGroup.blocksRaycasts = live;
            front.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, Ease.OutCubic(uiAlpha));
            // v5: the shelf and the words take turns (the words fade on twos while the cards deal in)
            if (gallery != null && phase != Phase.Leaving)
            {
                gallery.Tick(dt, uiAlpha, live && phase == Phase.Shown);
                if (songs != null) songs.Tick(dt, uiAlpha, live && phase == Phase.Shown);
                float wa = 1f - Mathf.Max(gallery.Shown, songs != null ? songs.Shown : 0f);
                if (Look.OnTwos) wa = Mathf.Round(wa * 3f) / 3f;
                itemsGroup.alpha = wa;
                bool wordsLive = wa > 0.99f;
                itemsGroup.interactable = wordsLive; itemsGroup.blocksRaycasts = wordsLive;
                // the shelves fill the page (the user: "DEACUBE should disappear and it should just be the background and projects"): the title
                // sinks into the wall while one is open and rises when it closes; the footer cube goes with the words
                if (stage != null) stage.SetTitleDown(gallery.IsOpen || (songs != null && songs.IsOpen));
                if (footer != null) { var fc = footer.color; float fa = 0.45f * wa; if (Mathf.Abs(fc.a - fa) > 0.003f) { fc.a = fa; footer.color = fc; } }
            }
        }
        if (phase != Phase.Leaving) LayoutItems();
        SyncSongsWord();
        UpdateButtonLights();
    }

    static bool PromptOpen => InterfaceController.I != null && InterfaceController.I.Visible;

    void UpdateShownInput()
    {
        if (PromptOpen || stage == null) return;
        if (Input.GetMouseButtonDown(0) && !InputUtil.PointerOverUI && stage.PointerOverride == null) WallClick(Input.mousePosition);
        if (InputUtil.TypingInField) return;
        if (songs != null && songs.IsOpen) { songs.HandleKeys(); return; }         // "my songs" has the keys while it is open
        if (gallery != null && gallery.IsOpen) { gallery.HandleKeys(); return; }   // v5: the shelf has the keys while it is open
        if (KeyShim.Down(KeyCode.DownArrow)) StepFocus(1);
        if (KeyShim.Down(KeyCode.UpArrow)) StepFocus(-1);
        if (KeyShim.Down(KeyCode.Return) || KeyShim.Down(KeyCode.KeypadEnter))
        {
            var p = FocusedItem;
            if (p == null || !p.gameObject.activeSelf) p = continueBtn.gameObject.activeSelf ? continueBtn : newBtn;
            p.Invoke();
        }
    }

    void WallClick(Vector2 screen)
    {
        if (stage == null) return;
        stage.Click(screen, Color.Lerp(Palette.Accent, Color.white, 0.2f), 1.1f);
        music.Strum(1f);
    }

    void UpdateButtonLights()
    {
        if (stage == null || buttons == null) return;
        // "my songs": behind its shelf the wall takes the focused song's first chord colour
        if (songs != null && phase != Phase.Leaving && (songs.IsOpen || songs.Shown > 0.01f))
        {
            stage.HoverTint = songs.Tint; stage.HoverSpread = songs.HoverSpread; stage.BeatSource = null;
            if (songs.IsOpen && songs.HoverAmount > 0.02f) stage.SetHover(songs.HoverScreen, songs.HoverAmount);
            else stage.SetHover(Vector2.zero, 0f);
            return;
        }
        // v5: behind the shelf the wall takes the focused song's vibe colour and hops on its preview's beat
        if (gallery != null && phase != Phase.Leaving && (gallery.IsOpen || gallery.Shown > 0.01f))
        {
            stage.HoverTint = gallery.Tint; stage.HoverSpread = gallery.HoverSpread; stage.BeatSource = galleryBeat;
            stage.SourceBar = gallery.Preview.BeatsPerBar;
            if (gallery.IsOpen && gallery.HoverAmount > 0.02f) stage.SetHover(gallery.HoverScreen, gallery.HoverAmount);
            else stage.SetHover(Vector2.zero, 0f);
            return;
        }
        stage.HoverTint = null; stage.HoverSpread = 1f; stage.BeatSource = null;
        MenuButton hot = null; float best = 0.02f;
        foreach (var b in order) if (b != null && b.gameObject.activeInHierarchy && b.HoverAmount > best) { best = b.HoverAmount; hot = b; }
        if (hot != null && phase != Phase.Leaving) stage.SetHover(hot.ScreenCenter, hot.HoverAmount);
        else stage.SetHover(Vector2.zero, 0f);
    }

    bool CanAct => (phase == Phase.Shown || (phase == Phase.Entering && uiAlpha > 0.6f)) && swapped;

    // ------------------------------------------------------------------ buttons
    void DoContinue()
    {
        if (!CanAct) return;
        if (SongLibrary.Enabled && ContinueLibrary()) return;
        string path = SongIO.NewestExisting;
        bool live = SongManager.I != null && SongManager.I.HasSong;
        if (path == null && !live) { continueBtn.Shake(); return; }
        bool resume = false;
        if (live)
        {
            if (path == null) resume = true;
            else { try { resume = File.ReadAllText(path) == SongState.Capture().ToJson(); } catch (Exception) { resume = false; } }
        }
        if (!resume)
        {
            // S10: a slot that fails to load (a torn write, a bad file) falls back to the other slot before giving up
            string other = path == SongIO.Path ? SongIO.AutosavePath : SongIO.Path;
            if (!SongIO.LoadFrom(path) && !(File.Exists(other) && SongIO.LoadFrom(other))) { continueBtn.Shake(); AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 0.7f); return; }
            GlobalClock.Stop();
            if (OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(0, true);
        }
        Leave(continueBtn.PressPoint, () => Onboarding.StartTutorial(false), continueBtn, "ZOOM!");
    }

    /// <summary>"my songs" Continue: a song in the world (the menu was opened from it) resumes as it was left; with no song in the world (the
    /// boot) the current library song opens. False when neither applies (no library song yet: the old slots decide, as before the library).</summary>
    bool ContinueLibrary()
    {
        SongLibrary.EnsureImported();
        bool live = SongManager.I != null && SongManager.I.HasSong;
        if (!live)
        {
            string cur = SongLibrary.CurrentId;
            if (cur == "" || !SongLibrary.Open(cur)) return false;
            GlobalClock.Stop();
            if (OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(0, true);
        }
        Leave(continueBtn.PressPoint, () => Onboarding.StartTutorial(false), continueBtn, "ZOOM!");
        return true;
    }

    void DoNew()
    {
        if (!CanAct) return;
        if (InterfaceController.I != null) InterfaceController.I.Show();
        else DoLearnSong(false);
    }

    void DoLearn()
    {
        if (!CanAct || learnBusy) return;
        DoLearnSong(true);
    }

    /// <summary>An offline dice song, then into the world (and the tutorial from its first step when <paramref name="tutorial"/>).</summary>
    void DoLearnSong(bool tutorial)
    {
        learnBusy = true;
        learnBtn.SetBusy(true);
        foreach (var b in order) b.interactable = false;
        SongGenerator gen = InterfaceController.I != null && InterfaceController.I.generator != null ? InterfaceController.I.generator : FindAnyObjectByType<SongGenerator>();
        Action<bool> done = ok =>
        {
            learnBtn.SetBusy(false);
            learnBusy = false;
            if (!ok || SongManager.I == null || !SongManager.I.HasSong) { RefreshButtons(); learnBtn.Shake(); return; }
            GlobalClock.Stop();
            Leave(learnBtn.PressPoint, () => { if (tutorial) Onboarding.StartTutorial(true); else Onboarding.StartTutorial(false); }, learnBtn, tutorial ? "BOING!" : "POP!");
        };
        if (gen != null && gen.IsGenerating) gen.Cancel();   // S1: a pending generation never races the Learn song
        if (gen != null) { gen.GenerateOffline("learn", done); return; }
        // no generator in the scene: build the dice song here
        bool ok2 = false;
        try
        {
            var song = MusicTheory.RandomSong(MusicTheory.SeedFromPrompt("learn" + DateTime.Now.Ticks), "learn");
            if (SongManager.I != null) { SongIO.Backup(); SongManager.I.StartFreshSong(song); ok2 = true; }   // F15: the song it replaces is kept
        }
        catch (Exception e) { Debug.LogWarning("Learn song failed: " + e.Message); }
        done(ok2);
    }

    /// <summary>v5: the words make way for the gallery's shelf.</summary>
    void DoGallery()
    {
        if (!CanAct || gallery == null) return;
        if (Gallery.Entries.Count == 0) { galleryBtn.Shake(); return; }
        gallery.Open();
    }

    /// <summary>v5: the shelf closed ("back", Esc): the words come back with "gallery" focused.</summary>
    public void OnShelfClosed()
    {
        if (galleryBtn != null && galleryBtn.gameObject.activeSelf) { focus = -1; SetFocusOn(galleryBtn, false); }
    }

    // ------------------------------------------------------------------ "my songs"
    /// <summary>The "my songs" word: a menu word on its own small canvas (sorted with the shelves), kept over its slot in the word column — so
    /// <see cref="Words"/> and the menu canvas keep the five words.</summary>
    void BuildSongsWord()
    {
        var go = new GameObject("MenuSongsWord", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        songsCanvas = go.AddComponent<Canvas>();
        songsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        songsCanvas.sortingOrder = MenuSongs.SortOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        songsGroup = go.AddComponent<CanvasGroup>();
        songsBtn = MenuButton.CreateText(go.transform, "MySongs", "my songs", WordPx, WordPx, false);
        var rt = UIKit.RT(songsBtn);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        songsSlot.sizeDelta = rt.sizeDelta;
        UIKit.Fixed(songsSlot.gameObject, rt.sizeDelta.x, rt.sizeDelta.y);
        songsBtn.index = buttons.Length;   // after the five words of Words
        songsBtn.onHover = () => SetFocusOn(songsBtn, true);
        songsBtn.onPress = () => { if (stage != null) stage.Press(songsBtn.PressPoint, new Color(0.55f, 0.6f, 1f)); };
        songsBtn.onClick = DoMySongs;
        order = new[] { continueBtn, songsBtn, newBtn, galleryBtn, learnBtn, quitBtn };
        go.SetActive(false);
    }

    void ShowSongsWord(bool on)
    {
        if (songsBtn == null || songsSlot == null) return;
        if (songsBtn.gameObject.activeSelf != on) songsBtn.gameObject.SetActive(on);
        if (songsSlot.gameObject.activeSelf != on) songsSlot.gameObject.SetActive(on);
        if (!on && FocusedItem == songsBtn) { focus = -1; SetFocusOn(continueBtn.gameObject.activeSelf ? continueBtn : newBtn, false); }
        laidOut = new Vector4(-1f, -1f, -1f, -1f);
    }

    /// <summary>The word follows its slot (position, scale), the words' fade and their input; its canvas is up only while the menu's is.</summary>
    void SyncSongsWord()
    {
        if (songsCanvas == null || songsBtn == null || songsSlot == null) return;
        bool on = canvas != null && canvas.gameObject.activeInHierarchy && songsBtn.gameObject.activeSelf && phase != Phase.Hidden && !worldOn;
        if (songsCanvas.gameObject.activeSelf != on) songsCanvas.gameObject.SetActive(on);
        if (!on) return;
        var rt = UIKit.RT(songsBtn);
        rt.position = songsSlot.position;
        Vector3 ps = rt.parent.lossyScale, ss = songsSlot.lossyScale;
        rt.localScale = new Vector3(ps.x > 1e-5f ? ss.x / ps.x : 1f, ps.y > 1e-5f ? ss.y / ps.y : 1f, 1f);
        songsGroup.alpha = (frontGroup != null ? frontGroup.alpha : 1f) * (itemsGroup != null ? itemsGroup.alpha : 1f);
        bool live = phase != Phase.Leaving && frontGroup != null && frontGroup.interactable && itemsGroup != null && itemsGroup.interactable;
        songsGroup.interactable = live; songsGroup.blocksRaycasts = live;
    }

    /// <summary>"my songs": the words make way for the player's songs.</summary>
    void DoMySongs()
    {
        if (!CanAct || songs == null) return;
        if (gallery != null && gallery.IsOpen) gallery.CloseImmediate();
        songs.Open();
    }

    /// <summary>The HUD asked for "my songs" (<see cref="ShowMySongs"/>): the shelf opens as soon as the menu is up.</summary>
    void OpenSongsIfPending()
    {
        if (!songsPending || songs == null || !shown) return;
        songsPending = false;
        if (gallery != null && gallery.IsOpen) gallery.CloseImmediate();
        if (!songs.IsOpen) songs.Open();
    }

    /// <summary>The songs shelf closed ("back", Esc): the words come back with "my songs" focused (the first word when it is gone now).</summary>
    public void OnSongsClosed()
    {
        ShowSongsWord(SongLibrary.HasSongs);
        focus = -1;
        if (songsBtn != null && songsBtn.gameObject.activeSelf) SetFocusOn(songsBtn, false);
        else SetFocusOn(continueBtn.gameObject.activeSelf ? continueBtn : newBtn, false);
    }

    /// <summary>The library changed: the word follows (not while its shelf is open: it waits behind it), the shelf rebuilds.</summary>
    void OnSongsChanged()
    {
        if (songsBtn == null) return;
        if (songs == null || !songs.IsOpen) ShowSongsWord(SongLibrary.HasSongs);
        if (songs != null) songs.MarkDirty();
    }

    /// <summary>A song card was chosen at <paramref name="at"/> (screen px): the library opens it (the world's own song, chosen again, just
    /// resumes), the camera goes to its start, then the comic leave. The card shakes when the song cannot be read.</summary>
    public void OpenFromSongs(SongEntry e, Vector2 at)
    {
        if (!CanAct || e == null || songs == null) return;
        if (SongLibrary.LiveId != e.id)
        {
            if (!SongLibrary.Open(e.id)) { songs.Refuse(e.id); AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 0.7f); return; }
            GlobalClock.Stop();
            if (OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(0, true);
        }
        SongOpens++;
        songs.SquashCard(e.id);
        Leave(at, () => Onboarding.StartTutorial(false), null, "ZOOM!");
    }

    /// <summary>The shelf's "+ new song": the vibe prompt, as the "new song" word (the song it makes lands as a new library entry).</summary>
    public void NewFromSongs() { DoNew(); }

    /// <summary>
    /// v5 (SPEC §4): gallery song <paramref name="e"/> was chosen at <paramref name="at"/> (screen px). Its preview stops, Gallery.Open makes it
    /// the working song (a song with cubes of the player's own is backed up first; the user's main save is never touched), the camera frames
    /// the whole song, then the comic leave ("HIT IT!", the impact on the song's opening chord) and, once the world is back, the song plays
    /// from bar 0 (and the tutorial resumes where it was, as Continue does). The card shakes when the song cannot be read.
    /// </summary>
    public void OpenFromGallery(GalleryEntry e, Vector2 at)
    {
        if (!CanAct || e == null || gallery == null) return;
        int[] chord = gallery.ChordOf(e);
        gallery.StopPreview(true);
        if (!Gallery.Open(e.id, false)) { gallery.Refuse(e); AudioPool.UI(ProceduralAudio.Pop(), 0.5f, 0.7f); return; }
        GalleryOpens++;
        gallery.SquashCard(e.id);
        var sm = SongManager.I;
        if (OrbitCamera.I != null && sm != null && sm.HasSong) { LastOpenFrame = OpeningBounds(sm); OrbitCamera.I.FrameStart(LastOpenFrame); }
        Leave(at, () => { GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow(); Onboarding.StartTutorial(false); }, null, "HIT IT!", chord);
    }

    /// <summary>v5: what the camera framed when the last gallery song opened (tests).</summary>
    public static Bounds LastOpenFrame { get; private set; }
    /// <summary>v5: the farthest the opening frame of a gallery song may sit (× the camera's FovScale): beyond it the world's fog
    /// (Look.FogStart 30 u) washes the islands out.</summary>
    public const float OpenFrameComfort = 34f;

    /// <summary>v5: the song as the camera first sees it: the whole song when it fits within <see cref="OpenFrameComfort"/> at the home view, else
    /// its opening — column 0 and every following column that still fits (where bar 0 plays).</summary>
    static Bounds OpeningBounds(SongManager sm)
    {
        var all = sm.SongBounds;
        float limit = OpenFrameComfort * (OrbitCamera.I != null ? OrbitCamera.I.FovScale : 1f);
        if (HomeFit(all) <= limit || sm.ColumnCount <= 1) return all;
        var b = sm.ColumnBounds(0);
        for (int c = 1; c < sm.ColumnCount; c++)
        {
            var nb = b; nb.Encapsulate(sm.ColumnBounds(c));
            if (HomeFit(nb) > limit) break;
            b = nb;
        }
        return b;
    }

    /// <summary>OrbitCamera's fit distance for <paramref name="b"/> seen from the home yaw and pitch (its own is private).</summary>
    static float HomeFit(Bounds b)
    {
        var cam = I != null && I.mainCam != null ? I.mainCam : Camera.main;
        float vfov = (cam != null ? cam.fieldOfView : 60f) * Mathf.Deg2Rad;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov * 0.5f) * aspect);
        float a = OrbitCamera.HomeYaw * Mathf.Deg2Rad;
        Vector3 f = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), r = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
        float ex = b.extents.x, ez = b.extents.z;
        float w = 2f * (Mathf.Abs(r.x) * ex + Mathf.Abs(r.z) * ez) + 5f;
        float d = 2f * (Mathf.Abs(f.x) * ex + Mathf.Abs(f.z) * ez);
        float s = Mathf.Sin(OrbitCamera.HomePitch * Mathf.Deg2Rad);
        return Mathf.Max(w / (2f * Mathf.Tan(hfov * 0.5f)), (d * s + 5f) / (2f * Mathf.Tan(vfov * 0.5f))) * 1.25f;
    }

    void DoQuit()
    {
        if (!CanAct) return;
#if UNITY_EDITOR
        quitBtn.Shake();   // the editor keeps playing; a build quits
#else
        Application.Quit();
#endif
    }
}
