using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Package U (v5) Play-mode checks (SPEC v5 §7, scratchpad/v5/brief_U.md): the vibe glyphs (registered before the islands build, distinct,
/// accent layers inside their silhouettes, worn by the island platforms), the deck's vibe cards (a test deck so all seven vibes show: every
/// card's colour = Vibe.ChordColor of its chord, its glyph = Vibe.Icon(vibe), the house sticker exactly on the key's home chords, the
/// caption = the vibe word + its feeling), the header's weather / chord glyphs, the chord wheel in vibe colours, the island header's repeat
/// button (cycles 1 → 2 → 3 → 4 → 1, one History entry per click, the ×n sticker), the column rail (beads widen with passes, belt tails, the
/// playhead walks the passes, the home house), the 4-way move cursor, the tutorial's v5 words (step 1 opens with "hover a tile to hear it") and
/// the new one-off tips — vibes, repeat, moveAnywhere, and package R's hearing tips hear / draftRun — (each fires once),
/// Synth late / errors 0 and the user's save untouched. v6 (package U1): the house sticker became the job-1 badge (the house with a 1) and the
/// caption became "n · job · swaps with any n · vibe" — the deck check reads those; the v6 tips are marked seen while the v5 tips are tested
/// (and restored after). Captures (1920×1080 Game view): u5_glyphs, u5_deck, u5_deck_key, u5_deck_colour,
/// u5_header_x1, u5_header_x3, u5_header_more, u5_wheel, u5_rail, u5_header_ride, u5_tip_vibes, u5_tip_repeat, u5_tip_move, u5_tip_hear, u5_tip_draft,
/// u5_tutorial_hear, u5_tutorial_deck, u5_cursors. Start with <see cref="Run"/>, poll <see cref="Done"/> or Captures/u5_report.txt.
/// </summary>
public static class V5ChecksU
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u5_report.txt");
    static StringBuilder sb; static int num, pass, fail;
    static int pushes;
    static readonly List<string> events = new List<string>();

    static void Line(bool ok, string what, string detail = null)
    {
        num++; if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" — ").Append(detail);
        sb.Append('\n');
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }
    static void Flush(bool final)
    {
        Report = "V5ChecksU: " + pass + " PASS, " + fail + " FAIL" + (final ? "" : " (running)") + "\n" + sb;
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float s) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < s) yield return null; }

    static bool captures;
    static IEnumerator Shot(string file)
    {
        if (!captures) yield break;
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
        yield return Wait(0.3f);
    }

    static void OnHistory() { pushes++; }
    static void OnEvent(string e) { events.Add(e); }

    static void LoadFixture()
    {
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    static void Frame(Vector3 centre, float dist)
    {
        if (OrbitCamera.I == null) return;
        OrbitCamera.I.FrameBounds(new Bounds(centre, new Vector3(dist, 2f, dist * 0.6f)), 0.05f, true);
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static readonly string[] TipIds = Onboarding.AllTips;   // v6: every tip since v4 (was the v5 five + hover, play, merge)

    static Vector3 ScreenOf(Vector3 world) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero; }

    /// <summary>A free tile of <paramref name="kb"/> (no cube on it), skipping <paramref name="skip"/> free ones.</summary>
    static TileInteraction FreeTile(KeyBlock kb, int skip)
    {
        int k = 0;
        foreach (var t in kb.tiles) { if (t == null || PathManager.TopCubeOn(t) != null) continue; if (k++ < skip) continue; return t; }
        return null;
    }

    /// <summary>A free tile and another free one 2-3 columns along its row with free tiles between (a run a drafting hover plays).</summary>
    static bool RunPair(out KeyBlock island, out TileInteraction a, out TileInteraction b)
    {
        island = null; a = b = null;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || kb.IsMoon) continue;
            foreach (var t in kb.tiles)
            {
                if (t == null || PathManager.TopCubeOn(t) != null) continue;
                for (int dx = 3; dx >= 2; dx--)
                {
                    bool ok = true;
                    for (int s = 1; s <= dx && ok; s++) { var u = kb.GetTile(t.gridX + s, t.gridZ); ok = u != null && PathManager.TopCubeOn(u) == null; }
                    if (!ok) continue;
                    island = kb; a = t; b = kb.GetTile(t.gridX + dx, t.gridZ);
                    return true;
                }
            }
        }
        return false;
    }

    // ================================================================== run
    public static string Run(bool withCaptures = true)
    {
        if (UIManager.I == null || SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        if (!Done) return "already running";
        Done = false; captures = withCaptures;
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        UIManager.I.StartCoroutine(Routine());
        return "started";
    }

    static IEnumerator Routine()
    {
        sb = new StringBuilder(); num = pass = fail = 0;
        SongIO.QuitAutosave = false;
        string userMd5 = Md5(SongIO.Path);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        // keep the tutorial / tip PlayerPrefs (every one this suite may touch)
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        var tipPrefs = new int[TipIds.Length];
        for (int k = 0; k < TipIds.Length; k++) tipPrefs[k] = PlayerPrefs.GetInt(Onboarding.PrefTip + TipIds[k], -1);
        bool suppressed = Onboarding.Suppressed;
        History.OnChanged += OnHistory; Onboarding.OnEvent += OnEvent;
        V4ChecksU1.Prepare();
        try { LoadFixture(); } catch (Exception e) { Line(false, "fixture", e.Message); }
        yield return null; yield return null;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return Wait(0.6f);
        var sm = SongManager.I;
        Info("screen " + Screen.width + "x" + Screen.height + ", islands " + sm.Islands.Count + ", columns " + sm.ColumnCount + ", key " + MusicTheory.KeyOfSong() + ", K passes live " + KPassesLive());

        IEnumerator[] parts = { Glyphs(), Deck(), Header(), Wheel(), Rail(), Ride(), Cursor(), Tips(), Tutorial() };
        foreach (var part in parts)
        {
            bool threw = false; string why = null;
            while (true)
            {
                object cur;
                try { if (!part.MoveNext()) break; cur = part.Current; }
                catch (Exception e) { threw = true; why = e.GetType().Name + ": " + e.Message; break; }
                yield return cur;
            }
            if (threw) Line(false, "a check section threw", why);
            Flush(false);
            Restore();
            yield return null;
        }

        Line(Synth.LateEvents == late0 && Synth.Errors == err0, "Synth: no late events or errors over the whole run", "late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " | " + Synth.Stats());
        // restore
        History.OnChanged -= OnHistory; Onboarding.OnEvent -= OnEvent;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        for (int k = 0; k < TipIds.Length; k++) RestorePref(Onboarding.PrefTip + TipIds[k], tipPrefs[k]);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        try { LoadFixture(); } catch (Exception) { }
        Line(Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + userMd5);
        Flush(true);
        Done = true;
    }

    static void RestorePref(string key, int v) { if (v < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, v); }

    static void Restore()
    {
        CubeInspector.CloseImmediate();
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        var pm = PathManager.I;
        if (pm != null && pm.Drag != null && pm.Drag.State != IslandDrag.Phase.Idle) pm.Drag.Cancel();
        if (pm != null && pm.IsDrawing) pm.CancelPath();
        IslandHeader.CloseMore(); IslandHeader.Hide();
        var ui = UIManager.I;
        if (ui != null) { ui.ClearSelection(); if (ui.Strip != null) ui.Strip.CloseNow(); if (ui.WheelOpen) { var wr = ui.HudRoot.Find("ChordWheel"); var wb = wr != null ? wr.GetComponent<HudButton>() : null; if (wb != null && wb.onClick != null) wb.onClick(); } }
        if (IslandTray.I != null) IslandTray.I.SimFan(0, false);
        if (IslandTray.IsOpen) IslandTray.Close();
        CursorKit.ResetAll();
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
    }

    /// <summary>K's belt timeline is in (a repeated column is longer than one pass).</summary>
    static bool KPassesLive()
    {
        var sm = SongManager.I;
        for (int c = 0; c < sm.ColumnCount; c++) if (sm.ColumnPasses(c) > 1 && sm.PassLength(c) < sm.ColumnLength(c) - 0.01f) return true;
        return sm.GetType().GetMethod("ColumnPasses") != null && sm.PassLength(0) < sm.ColumnLength(0) - 0.01f;
    }

    // ------------------------------------------------------------------ 1. the glyphs
    static IEnumerator Glyphs()
    {
        var sm = SongManager.I;
        // registered (before the islands built) and returned by Vibe.Icon
        var bad = new List<string>();
        for (int v = 0; v < Vibe.Count; v++)
        {
            var k = (VibeKind)v;
            if (Vibe.Icon(k) != VibeGlyphs.Names[v] || !IconFactory.Has(VibeGlyphs.Names[v])) bad.Add(Vibe.Word(k) + "→" + Vibe.Icon(k));
            for (int l = 1; l < VibeGlyphs.LayerCount(k); l++) if (!IconFactory.Has(VibeGlyphs.LayerGlyph(k, l))) bad.Add(VibeGlyphs.LayerGlyph(k, l));
        }
        Line(VibeGlyphs.Registered && bad.Count == 0, "vibe glyphs: all seven registered through IconFactory.Register (+ their accent layers) and Vibe.Icon returns them", bad.Count > 0 ? "missing " + string.Join(", ", bad) : string.Join(", ", VibeGlyphs.Names));
        // bold (coverage), distinct (pairwise), accents inside the silhouette
        var alpha = new float[Vibe.Count][];
        var cov = new List<string>(); bool covOk = true, inside = true; string insideWhy = "";
        for (int v = 0; v < Vibe.Count; v++)
        {
            alpha[v] = Alpha(VibeGlyphs.Names[v]);
            float c = 0f; foreach (var a in alpha[v]) c += a; c /= alpha[v].Length;
            cov.Add(Vibe.Word((VibeKind)v) + " " + (c * 100f).ToString("F0") + "%");
            covOk &= c > 0.10f && c < 0.60f;
            for (int l = 1; l < VibeGlyphs.LayerCount((VibeKind)v); l++)
            {
                var la = Alpha(VibeGlyphs.LayerGlyph((VibeKind)v, l));
                int outside = 0, on = 0;
                for (int i = 0; i < la.Length; i++) { if (la[i] > 0.5f) { on++; if (alpha[v][i] < 0.5f) outside++; } }
                if (on == 0 || outside > on * 0.01f) { inside = false; insideWhy += VibeGlyphs.LayerGlyph((VibeKind)v, l) + " " + outside + "/" + on + " "; }
            }
        }
        float minDiff = 1f; string pairMin = "";
        for (int a = 0; a < Vibe.Count; a++)
            for (int b = a + 1; b < Vibe.Count; b++)
            {
                int diff = 0, any = 0;
                for (int i = 0; i < alpha[a].Length; i++) { bool pa = alpha[a][i] > 0.5f, pb = alpha[b][i] > 0.5f; if (pa || pb) any++; if (pa != pb) diff++; }
                float f = any > 0 ? diff / (float)any : 0f;
                if (f < minDiff) { minDiff = f; pairMin = Vibe.Word((VibeKind)a) + "/" + Vibe.Word((VibeKind)b); }
            }
        Line(covOk, "vibe glyphs are bold silhouettes (ink coverage 10-60 % of the 128² glyph)", string.Join(", ", cov));
        Line(minDiff > 0.35f, "vibe glyphs are distinct shapes (every pair differs on > 35 % of their combined ink)", "closest pair " + pairMin + " " + (minDiff * 100f).ToString("F0") + "%");
        Line(inside, "every accent layer lies inside its glyph's silhouette (a card's two / three colours never spill)", insideWhy.Length > 0 ? insideWhy : "all inside");
        // the islands built at boot / on load wear them on their platforms
        int ok = 0, n = 0; var wrong = new List<string>();
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon) continue;
            n++;
            var g = kb.transform.Find("QualityGlyph"); var mr = g != null ? g.GetComponent<MeshRenderer>() : null;
            string tex = mr != null && mr.sharedMaterial != null && mr.sharedMaterial.mainTexture != null ? mr.sharedMaterial.mainTexture.name : "none";
            string want = "icon_" + Vibe.Icon(Vibe.Of(kb.quality));
            if (tex == want) ok++; else wrong.Add(kb.name + ": " + tex + " ≠ " + want);
        }
        Line(n > 0 && ok == n, "island platforms wear their vibe glyph (MusicTheory.QualityIcon → Vibe.Icon)", ok + "/" + n + (wrong.Count > 0 ? "; " + string.Join("; ", wrong) : ""));
        // the sheet: every glyph on its paper (62 / 40 px), on the header's chord disc (22 px) and plain on a platform colour (14 / 20 px)
        if (captures)
        {
            var sheet = BuildSheet();
            yield return Wait(0.4f);
            yield return Shot("u5_glyphs.png");
            UnityEngine.Object.Destroy(sheet);
            yield return null;
        }
    }

    static float[] Alpha(string glyph)
    {
        var t = IconFactory.GetTexture(glyph);
        var px = t.GetPixels32();
        var a = new float[px.Length];
        for (int i = 0; i < px.Length; i++) a[i] = px[i].a / 255f;
        return a;
    }

    static GameObject BuildSheet()
    {
        var go = new GameObject("U5GlyphSheet", typeof(RectTransform));
        go.layer = 5;
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)go.transform;
        var bg = HudKit.Node(root, "Bg", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 760f));
        var bgi = bg.gameObject.AddComponent<Image>(); bgi.color = new Color(0.30f, 0.22f, 0.50f, 1f); bgi.raycastTarget = false;
        for (int v = 0; v < Vibe.Count; v++)
        {
            var k = (VibeKind)v;
            float x = -630f + v * 210f;
            Color chord = Vibe.ChordColor(58 + v, MusicTheory.Semitones(Quality(k)));
            // a hero card
            var card = InkShape.Create(bg, "Card" + v, InkShape.Kind.RoundRect, IslandTray.PaperOf(chord), new Vector2(IslandTray.CardW, IslandTray.CardH));
            card.Radius = 12f; card.Wobble = 1.3f; card.SetInk(2.2f, 3.4f); card.ShadowOffset = new Vector2(4f, -5f); card.raycastTarget = false;
            card.rectTransform.anchoredPosition = new Vector2(x, 200f);
            var g = VibeGlyphs.Build(card.rectTransform, "Vibe", k, 62f); g.anchoredPosition = new Vector2(0f, 8f);
            var word = HudKit.Words(card.rectTransform, "Word", Vibe.Word(k), 20f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
            word.rectTransform.anchoredPosition = new Vector2(0f, -56f);
            // a small card
            var small = InkShape.Create(bg, "Small" + v, InkShape.Kind.RoundRect, IslandTray.PaperOf(chord), new Vector2(IslandTray.SmallW, IslandTray.SmallH));
            small.Radius = 9f; small.Wobble = 1.3f; small.SetInk(1.8f, 2.8f); small.ShadowOffset = new Vector2(3f, -4f); small.raycastTarget = false;
            small.rectTransform.anchoredPosition = new Vector2(x - 40f, 20f);
            var gs = VibeGlyphs.Build(small.rectTransform, "Vibe", k, 40f); gs.anchoredPosition = new Vector2(0f, 6f);
            // the header's chord disc (22 px, cream, inked)
            var disc = InkShape.Create(bg, "Disc" + v, InkShape.Kind.Circle, chord, new Vector2(40f, 40f));
            disc.SetInk(1.8f, 2.6f); disc.ShadowOffset = new Vector2(2.5f, -3f); disc.raycastTarget = false;
            disc.rectTransform.anchoredPosition = new Vector2(x + 50f, 20f);
            Comic.GlyphImage(disc.rectTransform, "Glyph", Vibe.Icon(k), Comic.Cream, 22f, true);
            // plain on a platform colour, as the island shows it
            var plat = HudKit.Node(bg, "Plat" + v, new Vector2(0.5f, 0.5f), new Vector2(x, -150f), new Vector2(170f, 80f));
            var pi = plat.gameObject.AddComponent<Image>(); pi.color = Comic.Opaque(KeyBlock.PlatformColorOf(chord)); pi.raycastTarget = false;
            float[] sizes = { 14f, 20f, 28f };
            for (int s = 0; s < sizes.Length; s++)
            {
                var im = HudKit.Node(plat, "G" + s, new Vector2(0.5f, 0.5f), new Vector2(-52f + s * 50f, 0f), new Vector2(sizes[s], sizes[s])).gameObject.AddComponent<Image>();
                im.sprite = IconFactory.Get(Vibe.Icon(k)); im.color = Palette.A(Color.Lerp(chord, Color.white, 0.35f), 0.9f); im.raycastTarget = false;
            }
            // on cream (a sticker / the gallery)
            var cream = InkShape.Create(bg, "Cream" + v, InkShape.Kind.Circle, Comic.Cream, new Vector2(84f, 84f));
            cream.SetInk(2f, 3f); cream.ShadowOffset = new Vector2(3f, -4f); cream.raycastTarget = false;
            cream.rectTransform.anchoredPosition = new Vector2(x, -290f);
            VibeGlyphs.Build(cream.rectTransform, "Vibe", k, 54f);
        }
        return go;
    }

    static ChordQuality Quality(VibeKind v)
    {
        switch (v)
        {
            case VibeKind.Sunny: return ChordQuality.Major7;
            case VibeKind.Dreamy: return ChordQuality.Major9;
            case VibeKind.Rainy: return ChordQuality.Minor7;
            case VibeKind.Moonlit: return ChordQuality.Minor9;
            case VibeKind.Stormy: return ChordQuality.Dominant7;
            case VibeKind.Spicy: return ChordQuality.Dominant9;
        }
        return ChordQuality.Sus4;
    }

    // ------------------------------------------------------------------ 2. the deck's vibe cards
    static List<int> CardSemis(SongManager.MeasureData md)
    {
        int[] stored = md.semitones != null && md.semitones.Length > 0 ? md.semitones : new[] { 0, 4, 7 };
        var semis = new List<int>(MusicTheory.EffectiveSemis(stored, 0, SongManager.Climate));
        if (!semis.Contains(0)) semis.Insert(0, 0);
        semis.Sort();
        return semis;
    }

    static IEnumerator Deck()
    {
        var sm = SongManager.I;
        var key = MusicTheory.KeyOfSong();
        var keep = IslandTray.Deck;
        int climate = SongManager.Climate;
        // a test deck that brings the three ninth vibes into the hand (the Key / Colour stacks bring sunny, rainy, stormy, floaty)
        var test = new[]
        {
            MusicTheory.Chord(MusicTheory.VoiceRoot(key.tonic), ChordQuality.Major9),
            MusicTheory.Chord(MusicTheory.VoiceRoot(key.tonic + (key.minor ? 3 : 9)), ChordQuality.Minor9),
            MusicTheory.Chord(MusicTheory.VoiceRoot(key.tonic + 7), ChordQuality.Dominant9),
        };
        IslandTray.SetDeck(test);
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I != null && IslandTray.I.ShownAmount > 0.98f, 2f);
        var t = IslandTray.I;
        if (t == null) { Line(false, "deck", "missing"); yield break; }
        t.RebuildNow();
        yield return Wait(0.5f);
        int n = 0, colourOk = 0, glyphOk = 0, homeOk = 0, captionOk = 0, paperOk = 0, homes = 0;
        var vibes = new HashSet<VibeKind>(); var why = new List<string>();
        foreach (var c in t.Cards)
        {
            if (c == null) continue;
            n++;
            var semis = CardSemis(c.data);
            var v = Vibe.Of(semis);
            vibes.Add(c.vibe);
            Color want = Vibe.ChordColor(c.data.chordRootMIDI, semis), got = c.ChordColor;
            bool col = Mathf.Abs(want.r - got.r) < 0.002f && Mathf.Abs(want.g - got.g) < 0.002f && Mathf.Abs(want.b - got.b) < 0.002f && c.vibe == v;
            if (col) colourOk++; else why.Add("colour " + c.name + " " + got + " ≠ " + want);
            var body = c.inner != null ? c.inner.GetComponent<InkShape>() : null;
            if (body != null && (body.color - IslandTray.PaperOf(want)).maxColorComponent < 0.003f && (IslandTray.PaperOf(want) - body.color).maxColorComponent < 0.003f) paperOk++;
            var gi = c.inner != null ? c.inner.Find("Vibe/Glyph") : null; var img = gi != null ? gi.GetComponent<Image>() : null;
            string sprite = img != null && img.sprite != null ? img.sprite.name : "none";
            bool glyph = c.glyph == Vibe.Icon(v) && sprite.StartsWith("ink_" + Vibe.Icon(v) + "|", StringComparison.Ordinal) && gi.parent.childCount == VibeGlyphs.LayerCount(v);
            if (glyph) glyphOk++; else why.Add("glyph " + c.name + " " + sprite + " / " + c.glyph + " ≠ " + Vibe.Icon(v));
            bool isHome = Vibe.IsHome(c.data.chordRootMIDI);
            // v6: the home sticker became the job-1 badge (a house with a 1) in the same corner
            bool sticker = c.badge != null && JobBadge.JobShown(c.badge) == 1;
            if (isHome) homes++;
            if ((sticker == isHome || (isHome && Harmony.IsDominant(c.data.semitones))) && c.home == isHome) homeOk++; else why.Add("home " + c.name + " house " + sticker + " ≠ " + isHome);
            var cap = c.GetComponent<InkCaptionHover>();
            string words = cap != null ? cap.words : "";
            // v6: "n · job · swaps with any n · vibe" (the vibe word ends it; "home" is job 1's word)
            bool capOk = words == IslandTray.JobCaption(c.data) && words.EndsWith(Vibe.Word(v), StringComparison.Ordinal) && (words.Contains("home") == (c.job == 1)) && words == words.ToLowerInvariant();
            if (capOk) captionOk++; else why.Add("caption " + c.name + " \"" + words + "\"");
        }
        Info("deck (Next " + t.CountIn(0) + ", Key " + t.CountIn(1) + ", Colour " + t.CountIn(2) + "): " + CardList(t));
        Line(n == 14 && vibes.Count == Vibe.Count, "deck: a hand of 14 cards shows all seven vibes (test deck of maj9 / m9 / 9 + the Key and Colour stacks)", "cards " + n + ", vibes " + vibes.Count);
        Line(colourOk == n && paperOk == n, "every card's colour = Vibe.ChordColor of its chord (its paper = that colour with a touch of cream)", "colour " + colourOk + "/" + n + ", paper " + paperOk + "/" + n);
        Line(glyphOk == n, "every card wears its vibe glyph big (Vibe.Icon(vibe), inked, with its accent layers)", glyphOk + "/" + n);
        Line(homeOk == n && homes > 0, "the house sits exactly on the key's home chords (Vibe.IsHome; v6: the job-1 badge is the house)", homeOk + "/" + n + " right, " + homes + " home cards (key " + key + ")");
        Line(captionOk == n, "every card's hover caption names its weather (v6: \"n · job · swaps with any n · vibe\"), lowercase", captionOk + "/" + n);
        if (why.Count > 0) Info("deck mismatches: " + string.Join("; ", why.GetRange(0, Mathf.Min(8, why.Count))));
        yield return Shot("u5_deck.png");
        t.SimFan(1, true); yield return Wait(0.45f); yield return Shot("u5_deck_key.png"); t.SimFan(1, false);
        t.SimFan(2, true); yield return Wait(0.45f); yield return Shot("u5_deck_colour.png"); t.SimFan(2, false);
        yield return Wait(0.2f);
        // the deck still works: hover auditions (stopped), a click places one island with one History entry, the card leaves the deck
        int a0 = t.AuditionCount;
        t.SimHover(0);
        yield return Wait(0.35f);
        int i0 = sm.Islands.Count, p0 = pushes, d0 = IslandTray.DeckCount;
        UIManager.I.SelectMeasure(0, false);
        int at = t.SimClick(0);
        yield return null;
        Line(t.AuditionCount == a0 + 1 && at >= 0 && sm.Islands.Count == i0 + 1 && pushes - p0 == 1 && IslandTray.DeckCount == d0 - 1, "the vibe deck still auditions on hover and places a card with a click (one History entry, the card leaves the deck)",
             "auditions +" + (t.AuditionCount - a0) + ", placed at " + at + ", islands " + i0 + " → " + sm.Islands.Count + ", pushes " + (pushes - p0) + ", deck " + d0 + " → " + IslandTray.DeckCount);
        if (at >= 0 && at < sm.Islands.Count) Info("the placed island's platform glyph: " + Vibe.Icon(Vibe.Of(sm.Islands[at].quality)) + " (" + Vibe.Word(Vibe.Of(sm.Islands[at].quality)) + ")");
        IslandTray.Close();
        yield return Wait(0.3f);
        IslandTray.SetDeck(keep);
        if (SongManager.Climate != climate) Info("climate changed during the deck checks: " + climate + " → " + SongManager.Climate);
        LoadFixture();
        yield return null;
    }

    static string CardList(IslandTray t)
    {
        var s = new StringBuilder();
        foreach (var c in t.Cards)
        {
            if (c == null) continue;
            if (s.Length > 0) s.Append(", ");
            s.Append(MusicTheory.ChordName(c.data.chordRootMIDI, CardSemis(c.data))).Append('=').Append(Vibe.Word(c.vibe)).Append(c.home ? "+home" : "");
        }
        return s.ToString();
    }

    // ------------------------------------------------------------------ 3. the header: repeat, weather, chord
    static IEnumerator Header()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        if (h == null) { Line(false, "island header", "missing"); yield break; }
        int i = Mathf.Min(1, sm.Islands.Count - 1);
        var kb = sm.Islands[i];
        Frame(kb.Center, 16f); yield return Wait(0.5f);
        IslandHeader.Show(kb);
        yield return Wait(0.45f);
        var rb = h.Repeat;
        bool exists = rb != null && rb.gameObject.activeInHierarchy;
        var cap = rb != null ? rb.GetComponent<InkCaptionHover>() : null;
        float k = UIManager.I.Canvas != null ? Mathf.Max(0.01f, UIManager.I.Canvas.scaleFactor) : 1f;
        var corners = new Vector3[4]; float w = 0f, hh = 0f;
        if (rb != null) { ((RectTransform)rb.transform).GetWorldCorners(corners); w = Vector3.Distance(corners[0], corners[3]) / k; hh = Vector3.Distance(corners[0], corners[1]) / k; }
        Line(exists && cap != null && cap.words.StartsWith("repeat", StringComparison.Ordinal) && w >= 27.5f && hh >= 27.5f && Hints.Has("island.repeat") && h.RepeatSticker == "",
             "header: a repeat button (belt picture, caption \"repeat\", ≥ 28 px, hint island.repeat), plain at ×1", "size " + w.ToString("F0") + "×" + hh.ToString("F0") + ", caption \"" + (cap != null ? cap.words : "") + "\", sticker \"" + h.RepeatSticker + "\", repeat " + kb.repeat);
        var grip = h.Grip != null ? h.Grip.GetComponent<InkCaptionHover>() : null;
        Line(grip != null && grip.words == "move anywhere", "header: the grip's caption says it moves anywhere", grip != null ? grip.words : "none");
        yield return Shot("u5_header_x1.png");
        // cycle: 1 → 2 → 3 → 4 → 1, one History entry and one island.repeat event per click, the ×n sticker follows, the header stays on it
        int r0 = kb.repeat;
        var seq = new List<string>(); bool okAll = true;
        for (int c = 0; c < 4; c++)
        {
            int before = sm.Islands[i].repeat, want = before >= 4 ? 1 : before + 1;
            int p0 = pushes; events.Clear();
            h.Repeat.onClick();
            yield return null; yield return null;
            var now = sm.Islands[i];
            bool one = pushes - p0 == 1, evt = events.Contains(Onboarding.Ev.IslandRepeat), same = IslandHeader.Current == now;
            string sticker = h.RepeatSticker;
            bool st = want == 1 ? sticker == "" : sticker == "×" + want;
            okAll &= now.repeat == want && one && evt && same && st;
            seq.Add(before + "→" + now.repeat + (one ? "" : " pushes " + (pushes - p0)) + (evt ? "" : " no-event") + (same ? "" : " header-lost") + " \"" + sticker + "\"");
            if (want == 3) { yield return Wait(0.45f); yield return Shot("u5_header_x3.png"); }
        }
        Line(okAll && sm.Islands[i].repeat == r0, "header repeat: each click cycles 1 → 2 → 3 → 4 → 1 through SetRepeat, one History entry and one island.repeat event per click, the ×n sticker follows, the header stays on its island", string.Join(", ", seq));
        // the more card: the weather (mood) glyph = the vibe the island takes under that mood; the chord glyph = its vibe
        IslandHeader.Show(sm.Islands[i]); yield return null;
        h.More.onClick(); yield return Wait(0.3f);
        var moods = new List<string>(); bool moodOk = true;
        int m0 = sm.Islands[i].mood;
        for (int m = 0; m < 4; m++)
        {
            int target = (m0 + 1 + m) % 4;
            h.MoodButton.onClick(); yield return null; yield return null;
            var now = sm.Islands[i];
            string want = target == 0 ? Vibe.Icon(Vibe.Of(now.quality)) : Vibe.Icon(Vibe.Of(MusicTheory.MoodMap(MusicTheory.QualityOf(now.storedSemis), target)));
            bool ok = now.mood == target && h.MoodGlyphName.StartsWith("ink_" + want + "|", StringComparison.Ordinal);
            moodOk &= ok;
            moods.Add(target + ":" + h.MoodGlyphName + (ok ? "" : " ✗"));
            if (target == 3) { yield return Wait(0.35f); yield return Shot("u5_header_more.png"); }
        }
        Line(moodOk && sm.Islands[i].mood == m0, "header more card: the weather button shows the vibe glyph the island takes (as written: the chord's own; sun / cloud / storm → its sunny / rainy / stormy family)", string.Join(", ", moods));
        IslandHeader.CloseMore(); IslandHeader.Hide();
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 4. the chord wheel
    static IEnumerator Wheel()
    {
        var sm = SongManager.I; var ui = UIManager.I;
        int i = Mathf.Min(1, sm.Islands.Count - 1);
        ui.OpenChordWheel(i);
        yield return Wait(0.5f);
        var kb = sm.Islands[i];
        var v = Vibe.Of(kb.quality);
        int family = 0; string off = "";
        var wedges = ui.WheelWedges;
        for (int k = 0; k < wedges.Count; k++)
        {
            Color c = wedges[k].bgColor;
            float best = float.MaxValue; int bi = -1;
            for (int f = 0; f < Vibe.Count; f++) { Color fc = Vibe.ColorOf((VibeKind)f); float d = Mathf.Abs(fc.r - c.r) + Mathf.Abs(fc.g - c.g) + Mathf.Abs(fc.b - c.b); if (d < best) { best = d; bi = f; } }
            if (bi == (int)v) family++; else off += k + ":" + Vibe.Word((VibeKind)bi) + " ";
        }
        var q = ui.WheelQuality;
        bool centre = q != null && q.iconName == Vibe.Icon(v);
        var qcap = q != null ? q.GetComponent<InkCaptionHover>() : null;
        var home = ui.WheelHome;
        int homeFifths = (MusicTheory.KeyOfSong().tonic * 7) % 12;
        float a = (90f - homeFifths * 30f) * Mathf.Deg2Rad;
        bool homeOk = home != null && Vector2.Distance(home.anchoredPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 183f) < 1f;
        Line(family == wedges.Count && centre && qcap != null && qcap.words == "weather · " + Vibe.Word(v) && homeOk,
             "chord wheel: every root wedge wears the island's vibe colour, the centre its vibe glyph (caption = the weather), a house marks the home root",
             "wedges in the " + Vibe.Word(v) + " family " + family + "/" + wedges.Count + (off.Length > 0 ? " (" + off + ")" : "") + ", centre " + (q != null ? q.iconName : "none") + ", caption \"" + (qcap != null ? qcap.words : "") + "\", home at fifths " + homeFifths + " " + homeOk);
        yield return Shot("u5_wheel.png");
        var wr = ui.HudRoot.Find("ChordWheel"); var wb = wr != null ? wr.GetComponent<HudButton>() : null; if (wb != null && wb.onClick != null) wb.onClick();
        yield return Wait(0.3f);
    }

    // ------------------------------------------------------------------ 5. the rail
    static IEnumerator Rail()
    {
        var sm = SongManager.I; var rail = UIManager.I.Rail;
        if (rail == null) { Line(false, "rail", "missing"); yield break; }
        int i = Mathf.Min(2, sm.Islands.Count - 1);
        int col = sm.Islands[i].column;
        rail.CountNow();
        float w1 = rail.ColumnWidth(col);
        int p0 = pushes;
        sm.SetRepeat(i, 3);
        yield return null; yield return null;
        rail.CountNow();
        col = sm.Islands[i].column;
        float scaleNow = rail.ColumnWidth(col) / Mathf.Max(1f, RailW(rail, col, 3));
        float w3 = rail.ColumnWidth(col);
        int bars = Mathf.Max(1, sm.Islands[i].bars);
        float wantRatio = (HudColumnRail.FirstPassW(bars) + 2f * HudColumnRail.ExtraPassW(bars)) / HudColumnRail.FirstPassW(bars);
        var others = new List<string>(); bool othersOk = true;
        for (int j = 0; j < sm.Islands.Count; j++)
        {
            if (j == i) continue;
            int want = sm.Islands[j].column == col ? Mathf.Clamp(sm.Islands[j].repeat, 1, 3) : Mathf.Clamp(sm.Islands[j].repeat, 1, 4);
            if (rail.BeadPasses(j) != want) { othersOk = false; others.Add(j + ":" + rail.BeadPasses(j) + "≠" + want); }
        }
        Line(pushes - p0 == 1 && rail.PassesOf(col) == 3 && rail.BeadPasses(i) == 3 && othersOk && w3 > w1 * 1.5f,
             "rail: an island at ×3 widens its column's beads by its passes (the bead + two belt tails), the other beads keep theirs",
             "column " + col + " passes " + rail.PassesOf(col) + ", bead passes " + rail.BeadPasses(i) + ", width " + w1.ToString("F0") + " → " + w3.ToString("F0") + " (layout ratio " + wantRatio.ToString("F2") + ", scale " + scaleNow.ToString("F2") + ")" + (others.Count > 0 ? ", others " + string.Join(" ", others) : ""));
        // the home house rides on exactly the home islands' beads
        int homeOk = 0, homes = 0;
        for (int j = 0; j < sm.Islands.Count; j++) { bool hme = Vibe.IsHome(sm.Islands[j].chordRootMIDI); if (hme) homes++; if (rail.BeadHome(j) == hme) homeOk++; }
        Line(homeOk == sm.Islands.Count && homes > 0, "rail: a tiny house on exactly the beads of the key's home islands", homeOk + "/" + sm.Islands.Count + ", " + homes + " home");
        // the playhead walks the passes (K's belt timeline: pass p of the column = its p-th slot)
        if (KPassesLive() && sm.PassLength(col) < sm.ColumnLength(col) - 0.01f)
        {
            float start = sm.ColumnStart(col), pl = sm.PassLength(col);
            var got = new List<string>(); bool ok = true;
            for (int p = 0; p < 3; p++)
            {
                GlobalClock.Seek(start + pl * (p + 0.5f));
                yield return null; yield return null;
                ok &= rail.PlayheadPass == p;
                got.Add(p + "→" + rail.PlayheadPass + " x " + rail.PlayheadX.ToString("F0"));
            }
            Line(ok, "rail: the playhead walks the passes (mid-pass p → slot p)", string.Join(", ", got));
            GlobalClock.Seek(start + pl * 1.5f);
            GlobalClock.Play();
            yield return Wait(0.6f);
            yield return Shot("u5_rail.png");
            GlobalClock.Stop();
        }
        else
        {
            Info("rail playhead passes: K's belt timeline not in yet (PassLength " + sm.PassLength(col).ToString("F1") + " = ColumnLength " + sm.ColumnLength(col).ToString("F1") + ") — the playhead stays in the first pass; re-test after report_K.md");
            yield return Shot("u5_rail.png");
        }
        GlobalClock.Seek(0);
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 5b. the header rides the belt with its island (K's request: VisualBounds)
    static IEnumerator Ride()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        int i = Mathf.Min(2, sm.Islands.Count - 1);
        sm.SetRepeat(i, 2);
        yield return null; yield return null;
        var kb = sm.Islands[i]; int col = kb.column;
        if (!(sm.PassLength(col) < sm.ColumnLength(col) - 0.01f)) { Info("header ride: K's belt timeline not live"); LoadFixture(); yield break; }
        Frame(kb.Center + new Vector3(KeyBlock.SlotPitch * 0.5f, 0f, 0f), 30f);
        yield return Wait(0.5f);
        GlobalClock.Seek(sm.ColumnStart(col) + sm.PassLength(col) * 1.5f);
        GlobalClock.Play();
        yield return Wait(0.25f);
        GlobalClock.Pause();   // paused mid-pass 2: the island stands on its second slot
        kb = sm.Islands[i];
        IslandHeader.Show(kb);
        yield return Wait(0.9f);
        kb = sm.Islands[i];
        var cam = Camera.main;
        Bounds vb = kb.VisualBounds, wb = kb.WorldBounds;
        float y = kb.VisualCenter.y + 0.05f;
        Vector3 sv = cam.WorldToScreenPoint(new Vector3(vb.center.x, y, vb.min.z)), sw = cam.WorldToScreenPoint(new Vector3(wb.center.x, y, wb.min.z));
        Vector2 rib = h.Ribbon != null ? RectTransformUtility.WorldToScreenPoint(null, h.Ribbon.position) : Vector2.zero;
        bool shown = IslandHeader.Current == kb && h.Shown > 0.9f;
        bool follows = Mathf.Abs(rib.x - sv.x) < 24f && Mathf.Abs(sv.x - sw.x) > 40f;
        yield return Shot("u5_header_ride.png");
        GlobalClock.Stop();
        Line(shown && follows, "header: on an island riding its belt (×2, paused in pass 2) the ribbon stays on the platform as shown (VisualBounds), not on its home slot",
             "ride offset " + kb.VisualOffset.x.ToString("F2") + " u, ribbon x " + rib.x.ToString("F0") + " px, platform front-centre as shown " + sv.x.ToString("F0") + " px, home slot " + sw.x.ToString("F0") + " px, shown " + shown);
        IslandHeader.Hide();
        GlobalClock.Seek(0);
        LoadFixture(); yield return null;
    }

    static float RailW(HudColumnRail rail, int col, int passes)
    {
        var sm = SongManager.I; var a = sm.AnchorOf(col);
        int bars = a != null ? Mathf.Max(1, a.bars) : 1;
        return HudColumnRail.FirstPassW(bars) + (passes - 1) * HudColumnRail.ExtraPassW(bars);
    }

    // ------------------------------------------------------------------ 6. the cursor
    static IEnumerator Cursor()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        float up = CursorKit.CoverageAt(CursorKit.Kind.MoveV, new Vector2(0f, 0.74f)), dn = CursorKit.CoverageAt(CursorKit.Kind.MoveV, new Vector2(0f, -0.74f));
        float lf = CursorKit.CoverageAt(CursorKit.Kind.MoveV, new Vector2(-0.74f, 0f)), rt = CursorKit.CoverageAt(CursorKit.Kind.MoveV, new Vector2(0.74f, 0f));
        float diag = CursorKit.CoverageAt(CursorKit.Kind.MoveV, new Vector2(0.62f, 0.62f));
        Line(up > 0.5f && dn > 0.5f && lf > 0.5f && rt > 0.5f && diag < 0.35f, "the move cursor points four ways (arrowheads up, down, left, right; clear between them)",
             "up " + up.ToString("F2") + ", down " + dn.ToString("F2") + ", left " + lf.ToString("F2") + ", right " + rt.ToString("F2") + ", diagonal " + diag.ToString("F2"));
        // it still shows over an island's margin
        var kb = sm.Islands[0];
        Frame(kb.Center, 14f); yield return Wait(0.4f);
        CursorKit.ResetAll();
        PathManager.SimPos = Camera.main.WorldToScreenPoint(new Vector3(kb.WestEdge + 0.35f, kb.VisualCenter.y + 0.05f, kb.Center.z));
        yield return null; yield return null; yield return null;
        var over = CursorKit.Current; bool ground = pm.HoverGround;
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return null; yield return null;
        Line(ground && over == CursorKit.Kind.MoveV && CursorKit.Current == CursorKit.Kind.Default, "the 4-way move cursor shows over an island's margin (HoverGround) and leaves with the pointer", "margin " + over + " (hover ground " + ground + "), away " + CursorKit.Current);
        if (captures) Info("cursor preview Captures/u5_cursors.png " + CursorKit.SavePreview(Path.Combine(CaptureDir, "u5_cursors.png"), Instruments.Colors[5]));
    }

    // ------------------------------------------------------------------ 7. the new tips: each fires once
    static IEnumerator Tips()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        PlayerPrefs.SetInt(Onboarding.PrefDone, 1);
        foreach (var id in TipIds) PlayerPrefs.DeleteKey(Onboarding.PrefTip + id);
        // v6: the v6 tips are told already (they would answer the same gestures: a press, a deck open); v7: the v7 tips too (a section filled by
        // a placed card, an inspected cube)
        foreach (var id in Onboarding.V6Tips) PlayerPrefs.SetInt(Onboarding.PrefTip + id, 1);
        foreach (var id in Onboarding.V7Tips) PlayerPrefs.SetInt(Onboarding.PrefTip + id, 1);
        Onboarding.Suppressed = false;
        var bub = Onboarding.Bubble;
        // a) the deck's weather: the first deck open
        IslandTray.Open();
        yield return WaitFor(() => Onboarding.ActiveTip == Onboarding.TipVibes, 1.5f);
        yield return Wait(0.6f);
        bub = Onboarding.Bubble;
        bool vShown = Onboarding.ActiveTip == Onboarding.TipVibes && bub != null && bub.Visible && bub.Text == Onboarding.VibesText.ToLowerInvariant() && Onboarding.ShownTarget == "tray.next0";
        string vText = bub != null ? bub.Text.Replace('\n', ' ') : "";
        yield return Shot("u5_tip_vibes.png");
        IslandTray.Close();
        yield return WaitFor(() => Onboarding.ActiveTip == null, 1.5f);
        bool vEnded = Onboarding.ActiveTip == null;
        yield return Wait(0.4f);
        IslandTray.Open(); yield return Wait(0.6f);
        bool vOnce = Onboarding.ActiveTip != Onboarding.TipVibes;
        IslandTray.Close(); yield return Wait(0.4f);
        Line(vShown && vEnded && vOnce && PlayerPrefs.GetInt(Onboarding.PrefTip + Onboarding.TipVibes, 0) == 1, "tip \"vibes\": the first deck open tells the cards' weather, pointing at the hand; it ends with the deck and never comes back", "\"" + vText + "\", ended " + vEnded + ", once " + vOnce);
        // b) repeat: the first time an island's header stays up under the pointer (hovering its margin), pointing at the repeat button; a
        //    repeat click ends it. A header pinned without the pointer on it (a selection) does not start it.
        int i = Mathf.Min(1, sm.Islands.Count - 1);
        Frame(sm.Islands[i].Center, 16f); yield return Wait(0.4f);
        IslandHeader.Show(sm.Islands[i]);
        yield return Wait(1.2f);
        bool rQuiet = Onboarding.ActiveTip == null;
        string rWhy = rQuiet ? "" : " (tip " + Onboarding.ActiveTip + ", header under the pointer " + (IslandHeader.I != null && IslandHeader.I.PointerOver) + ", real mouse " + Input.mousePosition + ")";
        var hk = sm.Islands[i];
        PathManager.SimPos = Camera.main.WorldToScreenPoint(new Vector3(hk.WestEdge + 0.35f, hk.VisualCenter.y + 0.05f, hk.Center.z));
        yield return WaitFor(() => Onboarding.ActiveTip == Onboarding.TipRepeat, 2.5f);
        yield return Wait(0.5f);
        bub = Onboarding.Bubble;
        bool rShown = Onboarding.ActiveTip == Onboarding.TipRepeat && bub != null && bub.Visible && Onboarding.ShownTarget == "island.repeat" && bub.Text == Onboarding.RepeatText.ToLowerInvariant();
        float tipDist = bub != null ? bub.TipDistance() : 999f;
        string rText = bub != null ? bub.Text.Replace('\n', ' ') : "";
        yield return Shot("u5_tip_repeat.png");
        IslandHeader.I.Repeat.onClick(); yield return null; yield return null;
        bool rEnded = Onboarding.ActiveTip == null;
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        IslandHeader.Hide(); yield return Wait(0.6f);
        hk = sm.Islands[i];
        PathManager.SimPos = Camera.main.WorldToScreenPoint(new Vector3(hk.WestEdge + 0.35f, hk.VisualCenter.y + 0.05f, hk.Center.z));
        yield return Wait(1.4f);
        bool rOnce = Onboarding.ActiveTip != Onboarding.TipRepeat;
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        IslandHeader.Hide(); yield return Wait(0.3f);
        Line(rQuiet && rShown && rEnded && rOnce && tipDist <= 24f, "tip \"repeat\": the first header that stays up under the pointer points at the repeat button (a selection alone does not); a repeat click ends it; never again", "quiet while only pinned " + rQuiet + rWhy + ", \"" + rText + "\", tail " + tipDist.ToString("F1") + " px, ended by the click " + rEnded + ", once " + rOnce);
        LoadFixture(); yield return null;
        // c) move anywhere: the first island drag (at the pointer)
        var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.4f);
        Vector3 g0 = new Vector3(kb.Center.x, 0f, kb.Center.z);
        pm.Drag.SimBegin(kb, false, g0);
        yield return null; yield return null;
        pm.Drag.SimMove(g0 + new Vector3(0.3f, 0f, -0.3f));
        yield return Wait(0.5f);
        bub = Onboarding.Bubble;
        bool mShown = Onboarding.ActiveTip == Onboarding.TipMove && bub != null && bub.Visible && bub.Text == Onboarding.MoveText.ToLowerInvariant();
        string mText = bub != null ? bub.Text.Replace('\n', ' ') : "";
        yield return Shot("u5_tip_move.png");
        pm.Drag.Cancel();
        yield return WaitFor(() => Onboarding.ActiveTip == null, 4.5f);
        pm.Drag.SimBegin(kb, false, g0); yield return null; yield return null;
        bool mOnce = Onboarding.ActiveTip != Onboarding.TipMove;
        pm.Drag.Cancel(); yield return Wait(0.3f);
        Line(mShown && mOnce, "tip \"moveAnywhere\": the first island drag says it moves anywhere (left = earlier, right = later); never again", "\"" + mText + "\", once " + mOnce);
        LoadFixture(); yield return null;
        // d) hear (package R's hover auditions): the first tile a hover plays says every tile plays its note, higher sounds higher
        var kb0 = sm.Islands[0];
        Frame(kb0.Center, 14f); yield return Wait(0.5f);
        var h0 = FreeTile(kb0, 0); var h1 = FreeTile(kb0, 4);
        if (h0 != null && h1 != null)
        {
            yield return WaitFor(() => Onboarding.ActiveTip == null, 4.5f);
            int a0 = PathManager.IdleAuditionCount;
            PathManager.SimPos = ScreenOf(h0.Top);
            yield return Wait(0.6f);
            bool heard = PathManager.IdleAuditionCount > a0;
            bub = Onboarding.Bubble;
            bool hShown = Onboarding.ActiveTip == Onboarding.TipHear && bub != null && bub.Visible && bub.Text == Onboarding.HearTipText.ToLowerInvariant();
            string hText = bub != null ? bub.Text.Replace('\n', ' ') : "";
            yield return Shot("u5_tip_hear.png");
            yield return WaitFor(() => Onboarding.ActiveTip == null, 4.5f);
            int a1 = PathManager.IdleAuditionCount;
            PathManager.SimPos = ScreenOf(h1.Top);
            yield return Wait(0.6f);
            bool heard2 = PathManager.IdleAuditionCount > a1;
            bool hOnce = Onboarding.ActiveTip != Onboarding.TipHear;
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            if (heard) Line(hShown && hOnce, "tip \"hear\": the first tile a hover plays says every tile plays its note, higher sounds higher (at the pointer); never again", "\"" + hText + "\", once " + hOnce + (heard2 ? "" : " (second hover played nothing)"));
            else Info("tip \"hear\": no idle hover audition happened (PathManager.IdleAuditionCount " + a0 + " → " + PathManager.IdleAuditionCount + ") — package R's hover audition not live here");
        }
        else Info("tip \"hear\": no free tiles on island 0");
        // e) draftRun: the first run a drafting hover plays says that is what a click adds and the card draws the tune
        KeyBlock rk; TileInteraction r0, r1;
        if (RunPair(out rk, out r0, out r1))
        {
            Frame(rk.Center, 14f); yield return Wait(0.5f);
            yield return WaitFor(() => Onboarding.ActiveTip == null, 4.5f);
            Vector3 s0 = ScreenOf(r0.Top);
            PathManager.SimPos = s0; pm.SimPointer(s0, true, true, false); pm.SimPointer(s0, false, false, true);
            yield return Wait(0.3f);
            yield return WaitFor(() => Onboarding.ActiveTip == null, 4.5f);   // (the start tile's own hover may have said "hear")
            int ra = PathManager.RunAuditionCount;
            PathManager.SimPos = ScreenOf(r1.Top);
            yield return Wait(0.7f);
            bool runHeard = PathManager.RunAuditionCount > ra;
            bub = Onboarding.Bubble;
            bool dShown = Onboarding.ActiveTip == Onboarding.TipDraftRun && bub != null && bub.Visible && bub.Text == Onboarding.DraftRunText.ToLowerInvariant();
            string dText = bub != null ? bub.Text.Replace('\n', ' ') : "";
            yield return Shot("u5_tip_draft.png");
            if (pm.IsDrawing) pm.CancelPath();
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            yield return WaitFor(() => Onboarding.ActiveTip == null, 5f);
            bool dOnce = PlayerPrefs.GetInt(Onboarding.PrefTip + Onboarding.TipDraftRun, 0) == 1;
            if (runHeard) Line(dShown && dOnce, "tip \"draftRun\": the first run a drafting hover plays says it is what a click adds and the little card draws the tune; never again", "\"" + dText + "\", marked " + dOnce);
            else Info("tip \"draftRun\": no run audition happened (RunAuditionCount " + ra + " → " + PathManager.RunAuditionCount + ", drawing " + pm.IsDrawing + ")");
        }
        else Info("tip \"draftRun\": no free run of tiles found");
        Onboarding.Suppressed = true;
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 8. the tutorial's v5 words
    static IEnumerator Tutorial()
    {
        // step 1 opens with the hearing line; after two tiles have been heard it asks to draw
        {
            var sm = SongManager.I;
            PlayerPrefs.DeleteKey(Onboarding.PrefTip + Onboarding.TipHear);
            Onboarding.Suppressed = false;
            Frame(sm.Islands[0].Center, 16f); yield return Wait(0.4f);
            Onboarding.StartTutorial(true);
            yield return Wait(0.9f);
            var bub1 = Onboarding.Bubble;
            bool opens = Onboarding.Step == Onboarding.SDraw && bub1 != null && bub1.Visible && bub1.Text == Onboarding.HearText.ToLowerInvariant() && Onboarding.ShownTarget == "world:island0";
            string t1 = bub1 != null ? bub1.Text.Replace('\n', ' ') : "";
            yield return Shot("u5_tutorial_hear.png");
            var kb0 = sm.Islands[0]; var f0 = FreeTile(kb0, 1); var f1 = FreeTile(kb0, 5);
            int a0 = PathManager.IdleAuditionCount;
            if (f0 != null) { PathManager.SimPos = ScreenOf(f0.Top); yield return Wait(0.5f); }
            if (f1 != null) { PathManager.SimPos = ScreenOf(f1.Top); yield return Wait(0.6f); }
            int heard = PathManager.IdleAuditionCount - a0;
            bool then = bub1 != null && bub1.Text == Onboarding.TextOf(Onboarding.SDraw).ToLowerInvariant();
            string t2 = bub1 != null ? bub1.Text.Replace('\n', ' ') : "";
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            bool tipQuiet = Onboarding.ActiveTip == null;   // tips never talk over a tutorial step
            Onboarding.Skip();
            Onboarding.Suppressed = true;
            yield return Wait(0.3f);
            if (heard >= 2) Line(opens && then && tipQuiet, "tutorial step 1 opens with \"hover a tile to hear it — higher tiles sound higher\", then (two tiles heard) asks to draw", "\"" + t1 + "\" → heard " + heard + " → \"" + t2 + "\"");
            else Line(opens, "tutorial step 1 opens with \"hover a tile to hear it — higher tiles sound higher\" (the hovers played " + heard + " tiles here: the switch to drawing is timed, 7 s)", "\"" + t1 + "\"");
        }
        string drag = Onboarding.TextOf(Onboarding.SDragColumn);
        Line(drag != null && drag.Contains("left") && drag.Contains("right") && drag == drag.ToLowerInvariant(), "tutorial step 8 (drag an island) says it moves left / right in time", "\"" + (drag ?? "").Replace('\n', ' ') + "\"");
        // the deck step, resumed: the open deck tells the weather and marks the vibes tip as told
        PlayerPrefs.SetInt(Onboarding.PrefDone, 0); PlayerPrefs.SetInt(Onboarding.PrefStep, Onboarding.SDeck);
        PlayerPrefs.DeleteKey(Onboarding.PrefTip + Onboarding.TipVibes);
        Onboarding.Suppressed = false;
        Onboarding.StartTutorial(false);
        yield return Wait(0.3f);
        bool atDeck = Onboarding.Step == Onboarding.SDeck;
        IslandTray.Open();
        yield return Wait(1.0f);
        var bub = Onboarding.Bubble;
        bool words = bub != null && bub.Visible && bub.Text == Onboarding.DeckOpenText.ToLowerInvariant() && Onboarding.ShownTarget == "tray.next0";
        string text = bub != null ? bub.Text.Replace('\n', ' ') : "";
        yield return Shot("u5_tutorial_deck.png");
        bool told = PlayerPrefs.GetInt(Onboarding.PrefTip + Onboarding.TipVibes, 0) == 1;
        IslandTray.Close();
        Onboarding.Skip();
        Onboarding.Suppressed = true;
        yield return Wait(0.3f);
        Line(atDeck && words && told, "tutorial step 10 (the deck): once the deck is open it tells the cards' weather and what to do; the vibes tip is then marked told", "step " + Onboarding.Step + " → deck " + atDeck + ", \"" + text + "\", tip told " + told);
    }
}
