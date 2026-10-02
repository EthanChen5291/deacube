using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Package U1 (v6) Play-mode checks (SPEC v6 §7, scratchpad/v6/brief_U1.md), with PathManager.AutoHand = false (the real game): the job
/// badges (glyphs registered, four distinct family shapes, the digit inside its shape, the world texture inked), the deck's numbers (every chord
/// card's badge = Harmony.Job, the house on the key's home chords, the caption "n · job · swaps with any n · vibe"), the stars (a grid
/// reference, a melody reference on a keyboard when package K's keyboards are live, the clipboard pattern, the next-chord ranking with no notes;
/// each star = Harmony.Fit ≥ StarThreshold computed independently here, the best card twinkles, a reference change refreshes them), the ⇄ swap
/// chips over the same-job islands while a card is hovered and the rail's pulsing beads, the keyboard card (a click and a drag through the ghost:
/// SongManager.AddKeyboardIsland, one History entry), the header's carry button (0 → 1 → 2 → 3 → 0, one History entry per click, the ×n sticker,
/// the last column refuses — §21: it is the EXTEND → control now), a keyboard island's header, "paste here", the rail (badges = jobs, §21: a
/// carry is one long-grid bar, no hops; piano beads), the
/// tutorial's drawing step (hear → pick a cube → draw) and the seven v6 tips (each once); SPEC §11 (Moons follow the song): the Moon card starts
/// a Moon at the focused column (a click) or at the column it is dropped in line with (a drag), the rail's drum sections (a line under the
/// columns each section covers, a drum at its start, a gap without percussion); Synth late / errors 0; the user's save untouched.
/// Captures (1920×1080): u1_6_badges, u1_6_deck_grid, u1_6_deck_melody, u1_6_deck_next, u1_6_swap, u1_6_keyboard_card, u1_6_header_carry2,
/// u1_6_header_keyboard, u1_6_header_paste, u1_6_rail, u1_6_rail_drums, u1_6_tutorial_pick, u1_6_tutorial_draw, u1_6_tip_jobs, u1_6_tip_carry.
/// Start with <see cref="Run"/>, poll <see cref="Done"/> or Captures/u1_6_report.txt.
/// </summary>
public static class V6ChecksU1
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));
    public static string ReportPath => Path.Combine(CaptureDir, "u1_6_report.txt");
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
        Report = "V6ChecksU1: " + pass + " PASS, " + fail + " FAIL" + (final ? "" : " (running)") + "\n" + sb;
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

    static Vector3 ScreenOf(Vector3 world) { var cam = Camera.main; return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero; }

    static TileInteraction FreeTile(KeyBlock kb, int skip)
    {
        int k = 0;
        foreach (var t in kb.tiles) { if (t == null || PathManager.TopCubeOn(t) != null) continue; if (k++ < skip) continue; return t; }
        return null;
    }

    /// <summary>Package K's keyboards are live (AddKeyboardIsland builds kind-2 islands).</summary>
    static bool kLive;

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
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        var tipPrefs = new int[Onboarding.AllTips.Length];
        for (int k = 0; k < tipPrefs.Length; k++) tipPrefs[k] = PlayerPrefs.GetInt(Onboarding.PrefTip + Onboarding.AllTips[k], -1);
        bool suppressed = Onboarding.Suppressed, autoHand = PathManager.AutoHand;
        PathManager.AutoHand = false;
        History.OnChanged += OnHistory; Onboarding.OnEvent += OnEvent;
        V4ChecksU1.Prepare();
        Clipboard.Clear();
        try { LoadFixture(); } catch (Exception e) { Line(false, "fixture", e.Message); }
        yield return null; yield return null;
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        yield return Wait(0.6f);
        var sm = SongManager.I;
        Info("screen " + Screen.width + "x" + Screen.height + ", islands " + sm.Islands.Count + ", columns " + sm.ColumnCount + ", key " + MusicTheory.KeyOfSong() + ", AutoHand " + PathManager.AutoHand);

        IEnumerator[] parts = { Badges(), Numbers(), Stars(), Swap(), KeyboardCard(), Header(), Rail(), Moons(), Tutorial(), Tips() };
        foreach (var part in parts)
        {
            bool threw = false; string why = null;
            while (true)
            {
                object cur;
                try { if (!part.MoveNext()) break; cur = part.Current; }
                catch (Exception e) { threw = true; why = e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]; break; }
                yield return cur;
            }
            if (threw) Line(false, "a check section threw", why);
            Flush(false);
            Restore();
            yield return null;
        }

        Line(Synth.LateEvents == late0 && Synth.Errors == err0, "Synth: no late events or errors over the whole run", "late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " | " + Synth.Stats());
        History.OnChanged -= OnHistory; Onboarding.OnEvent -= OnEvent;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        for (int k = 0; k < tipPrefs.Length; k++) RestorePref(Onboarding.PrefTip + Onboarding.AllTips[k], tipPrefs[k]);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        Clipboard.Clear();
        try { LoadFixture(); } catch (Exception) { }
        PathManager.AutoHand = autoHand;
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
        if (pm != null) pm.PutDown();
        IslandHeader.CloseMore(); IslandHeader.Hide();
        var ui = UIManager.I;
        if (ui != null) { ui.ClearSelection(); if (ui.Strip != null) ui.Strip.CloseNow(); }
        if (IslandTray.I != null) { IslandTray.I.SimFan(0, false); IslandTray.I.SimSwapHover(-1); }
        if (IslandTray.IsOpen) IslandTray.Close();
        CursorKit.ResetAll();
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
    }

    // ------------------------------------------------------------------ 1. the badges
    static float[] Alpha(string glyph)
    {
        var t = IconFactory.GetTexture(glyph);
        var px = t.GetPixels32();
        var a = new float[px.Length];
        for (int i = 0; i < px.Length; i++) a[i] = px[i].a / 255f;
        return a;
    }

    static IEnumerator Badges()
    {
        var missing = new List<string>();
        for (int j = 1; j <= 4; j++) foreach (var g in new[] { JobBadge.Glyph(j), JobBadge.ShapeGlyph(j), JobBadge.DigitGlyph(j) }) if (!IconFactory.Has(g)) missing.Add(g);
        foreach (var g in new[] { JobBadge.StarGlyph, JobBadge.StarGlint, JobBadge.SwapGlyph }) if (!IconFactory.Has(g)) missing.Add(g);
        Line(JobBadge.Registered && missing.Count == 0, "job badges: job1..job4 (+ .shape / .digit), fitStar (+ glint) and swap registered through IconFactory before the first scene", missing.Count > 0 ? "missing " + string.Join(", ", missing) : "all 15 glyphs");
        // distinct family shapes; the digit inside its shape; "jobN" = the shape with the digit cut out
        var shapes = new float[5][]; bool inside = true, cut = true; var why = new List<string>();
        for (int j = 1; j <= 4; j++)
        {
            shapes[j] = Alpha(JobBadge.ShapeGlyph(j));
            var dg = Alpha(JobBadge.DigitGlyph(j)); var win = Alpha(JobBadge.Glyph(j));
            int on = 0, outside = 0, holes = 0;
            for (int i = 0; i < dg.Length; i++) { if (dg[i] > 0.5f) { on++; if (shapes[j][i] < 0.5f) outside++; if (win[i] > 0.5f) holes++; } }
            if (on == 0 || outside > on * 0.01f) { inside = false; why.Add("digit " + j + " " + outside + "/" + on + " outside"); }
            if (holes > on * 0.02f) { cut = false; why.Add("job" + j + " keeps " + holes + "/" + on + " digit texels"); }
        }
        float minDiff = 1f; string pair = "";
        for (int a = 1; a <= 4; a++)
            for (int b = a + 1; b <= 4; b++)
            {
                int diff = 0, any = 0;
                for (int i = 0; i < shapes[a].Length; i++) { bool pa = shapes[a][i] > 0.5f, pb = shapes[b][i] > 0.5f; if (pa || pb) any++; if (pa != pb) diff++; }
                float f = any > 0 ? diff / (float)any : 0f;
                if (f < minDiff) { minDiff = f; pair = a + "/" + b; }
            }
        Line(minDiff > 0.3f, "job badges: four distinct family shapes (house, flag, heart, curling arrow: every pair differs on > 30 % of their ink)", "closest " + pair + " " + (minDiff * 100f).ToString("F0") + "%");
        Line(inside && cut, "each digit lies inside its shape, and job1..job4 are the shapes with the digit cut out (the window a world quad shows)", why.Count > 0 ? string.Join("; ", why) : "ok");
        // the world texture: the family fill inside, ink on the digit, an ink outline around
        bool worldOk = true; var wd = new List<string>();
        for (int j = 1; j <= 4; j++)
        {
            var tex = JobBadge.WorldTexture(j);
            if (tex == null || tex.width != 128) { worldOk = false; wd.Add(j + ": none"); continue; }
            var px = tex.GetPixels32();
            float fillLum = 0f, digLum = 0f; int nf = 0, nd = 0, rim = 0;
            var dg = Alpha(JobBadge.DigitGlyph(j));
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    // texel (x, y) of the world texture sits at glyph coordinate p · GlyphPad
                    float gx = ((x + 0.5f) / 128f * 2f - 1f) * Comic.GlyphPad, gy = ((y + 0.5f) / 128f * 2f - 1f) * Comic.GlyphPad;
                    int sx = Mathf.FloorToInt((gx + 1f) * 0.5f * 128f), sy = Mathf.FloorToInt((gy + 1f) * 0.5f * 128f);
                    Color32 c = px[y * 128 + x];
                    float lum = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / 255f;
                    bool inGlyph = sx >= 0 && sy >= 0 && sx < 128 && sy < 128;
                    float sh = inGlyph ? shapes[j][sy * 128 + sx] : 0f, dd = inGlyph ? dg[sy * 128 + sx] : 0f;
                    if (sh > 0.9f && dd < 0.05f && c.a > 250) { fillLum += lum; nf++; }
                    if (dd > 0.95f && c.a > 250) { digLum += lum; nd++; }
                    if (sh < 0.05f && c.a > 200) rim++;
                }
            fillLum = nf > 0 ? fillLum / nf : 0f; digLum = nd > 0 ? digLum / nd : 1f;
            bool ok = nf > 200 && nd > 50 && fillLum > 0.6f && digLum < 0.2f && rim > 100;
            worldOk &= ok;
            wd.Add(j + ": fill " + fillLum.ToString("F2") + " (" + nf + "), digit " + digLum.ToString("F2") + " (" + nd + "), outline " + rim);
        }
        Line(worldOk, "JobBadge.WorldTexture (K's platform quad): a light family fill, an ink digit and an ink outline", string.Join("; ", wd));
        if (captures)
        {
            var sheet = BuildSheet();
            yield return Wait(0.4f);
            yield return Shot("u1_6_badges.png");
            UnityEngine.Object.Destroy(sheet);
            yield return null;
        }
    }

    static GameObject BuildSheet()
    {
        var go = new GameObject("U16BadgeSheet", typeof(RectTransform));
        go.layer = 5;
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
        var scaler = go.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)go.transform;
        var bg = HudKit.Node(root, "Bg", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 760f));
        var bgi = bg.gameObject.AddComponent<Image>(); bgi.color = new Color(0.30f, 0.22f, 0.50f, 1f); bgi.raycastTarget = false;
        VibeKind[] vibes = { VibeKind.Sunny, VibeKind.Rainy, VibeKind.Dreamy, VibeKind.Stormy };
        for (int j = 1; j <= 4; j++)
        {
            float x = -560f + (j - 1) * 370f;
            var cream = InkShape.Create(bg, "Cream" + j, InkShape.Kind.RoundRect, Comic.Cream, new Vector2(300f, 200f));
            cream.Radius = 14f; cream.SetInk(2f, 3f); cream.ShadowOffset = new Vector2(4f, -5f); cream.raycastTarget = false;
            cream.rectTransform.anchoredPosition = new Vector2(x, 220f);
            float[] sizes = { 16f, 24f, 32f, 64f };
            float bx = -110f;
            foreach (float s in sizes) { var b = JobBadge.Build(cream.rectTransform, j, s); b.anchoredPosition = new Vector2(bx + s * 0.5f, 0f); bx += s + 26f; }
            var word = HudKit.Words(cream.rectTransform, "Word", j + " · " + Harmony.JobWord(j), 20f, Comic.Ink, TMPro.TextAlignmentOptions.Center);
            word.rectTransform.anchoredPosition = new Vector2(0f, -76f);
            // on a card of a vibe colour, with a star
            Color chord = Vibe.ColorOf(vibes[j - 1]);
            var card = InkShape.Create(bg, "Card" + j, InkShape.Kind.RoundRect, IslandTray.PaperOf(chord), new Vector2(IslandTray.CardW, IslandTray.CardH));
            card.Radius = 12f; card.Wobble = 1.3f; card.SetInk(2.2f, 3.4f); card.ShadowOffset = new Vector2(4f, -5f); card.raycastTarget = false;
            card.rectTransform.anchoredPosition = new Vector2(x - 70f, -40f);
            var g = VibeGlyphs.Build(card.rectTransform, "Vibe", vibes[j - 1], 62f); g.anchoredPosition = new Vector2(0f, 19f);
            var holder = HudKit.Node(card.rectTransform, "Job", new Vector2(1f, 1f), new Vector2(-12.8f, -12.8f), new Vector2(32f, 32f));
            holder.localEulerAngles = new Vector3(0f, 0f, -8f);
            JobBadge.Build(holder, j, 32f);
            var st = HudKit.Node(card.rectTransform, "Star", new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(32f, 32f));
            Comic.GlyphImage(st, "Glyph", JobBadge.StarGlyph, JobBadge.StarGold, 32f, true);
            Comic.GlyphImage(st, "Glint", JobBadge.StarGlint, Color.white, 32f, false);
            // the world texture on a platform colour (what K's platform shows), and a swap chip
            var plat = HudKit.Node(bg, "Plat" + j, new Vector2(0.5f, 0.5f), new Vector2(x + 90f, -40f), new Vector2(120f, 120f));
            var pi = plat.gameObject.AddComponent<Image>(); pi.color = Comic.Opaque(KeyBlock.PlatformColorOf(chord)); pi.raycastTarget = false;
            var wt = HudKit.Node(plat, "World", new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(70f, 70f)).gameObject.AddComponent<RawImage>();
            wt.texture = JobBadge.WorldTexture(j); wt.raycastTarget = false;
            var mono = HudKit.Node(plat, "Mono", new Vector2(0.5f, 0.5f), new Vector2(0f, -38f), new Vector2(30f, 30f)).gameObject.AddComponent<Image>();
            mono.sprite = IconFactory.Get(JobBadge.Glyph(j)); mono.color = Comic.Cream; mono.raycastTarget = false;
            var chip = InkShape.Create(bg, "Chip" + j, InkShape.Kind.Circle, Comic.Cream, new Vector2(38f, 38f));
            chip.SetInk(1.6f, 2.4f); chip.ShadowOffset = new Vector2(2.5f, -3f); chip.raycastTarget = false;
            chip.rectTransform.anchoredPosition = new Vector2(x, -250f);
            Comic.GlyphImage(chip.rectTransform, "Swap", JobBadge.SwapGlyph, Comic.Ink, 22f, false);
            var ch = HudKit.Node(chip.rectTransform, "Job", new Vector2(1f, 0f), new Vector2(-2f, 3f), new Vector2(22f, 22f));
            JobBadge.Build(ch, j, 22f);
        }
        return go;
    }

    // ------------------------------------------------------------------ 2. the numbers on the cards
    static IEnumerator Numbers()
    {
        var sm = SongManager.I;
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I != null && IslandTray.I.ShownAmount > 0.98f, 2f);
        var t = IslandTray.I;
        if (t == null) { Line(false, "deck", "missing"); yield break; }
        t.RebuildNow();
        yield return Wait(0.3f);
        int n = 0, jobOk = 0, capOk = 0, homeOk = 0, homes = 0; var jobs = new HashSet<int>(); var why = new List<string>();
        foreach (var c in t.Cards)
        {
            if (c == null) continue;
            n++;
            int want = Harmony.Job(c.data);
            jobs.Add(c.job);
            if (c.job == want && JobBadge.Valid(want) && JobBadge.JobShown(c.badge) == want) jobOk++; else why.Add(c.name + " job " + c.job + " shown " + JobBadge.JobShown(c.badge) + " want " + want);
            var cap = c.GetComponent<InkCaptionHover>();
            string words = cap != null ? cap.words : "";
            var semis = IslandTray.CardSemis(c.data);
            string vw = Vibe.Word(Vibe.Of(semis));
            if (words == IslandTray.JobCaption(c.data) && words.StartsWith(want + " · " + Harmony.JobWord(want) + " · swaps with any " + want, StringComparison.Ordinal) && words.EndsWith(vw, StringComparison.Ordinal) && words == words.ToLowerInvariant()) capOk++;
            else why.Add("caption \"" + words + "\"");
            bool isHome = Vibe.IsHome(c.data.chordRootMIDI);
            if (isHome) homes++;
            if (isHome == (c.job == 1) || (isHome && Harmony.IsDominant(c.data.semitones))) homeOk++; else why.Add("home " + c.name + " job " + c.job);
        }
        Line(n > 0 && jobOk == n && jobs.Count == 4, "deck: every chord card wears its job badge (Harmony.Job: 1 house, 2 flag, 3 heart, 4 curling arrow); all four jobs are in the hand", jobOk + "/" + n + ", jobs " + string.Join(",", jobs));
        Line(homes > 0 && homeOk == n, "the house (the job-1 badge) sits exactly on the key's home chords (v5's home sticker became it)", homeOk + "/" + n + ", " + homes + " home cards");
        Line(capOk == n, "every card's hover caption: \"n · job · swaps with any n · vibe\", lowercase", capOk + "/" + n + (n > 0 ? " e.g. \"" + IslandTray.JobCaption(t.Cards[0].data) + "\"" : ""));
        if (why.Count > 0) Info("number mismatches: " + string.Join("; ", why.GetRange(0, Mathf.Min(8, why.Count))));
        IslandTray.Close();
        yield return Wait(0.3f);
    }

    // ------------------------------------------------------------------ 3. the stars
    /// <summary>The stars the tray shows against an independent Harmony.Fit of <paramref name="midi"/> / <paramref name="w"/> over each card.</summary>
    static bool StarsMatch(IslandTray t, List<int> midi, List<float> w, out string detail)
    {
        int n = 0, ok = 0, starred = 0; float best = -1f; TrayCard bestCard = null; var bad = new List<string>();
        foreach (var c in t.Cards)
        {
            if (c == null) continue;
            n++;
            float f = Harmony.Fit(midi, w, c.data);
            bool want = f >= Harmony.StarThreshold;
            bool shown = c.star != null && c.star.gameObject.activeSelf;
            if (want) starred++;
            if (want && f > best + 1e-4f) { best = f; bestCard = c; }
            if (shown == want && c.starred == want && Mathf.Abs(c.fit - f) < 1e-4f) ok++; else bad.Add(c.name + " fit " + f.ToString("F2") + " star " + shown);
        }
        bool bestOk = bestCard == null ? t.BestCard == null : (t.BestCard == bestCard && bestCard.best);
        detail = ok + "/" + n + " cards right, " + starred + " starred, best " + (bestCard != null ? MusicTheory.ChordName(bestCard.data.chordRootMIDI, bestCard.data.semitones) + " " + best.ToString("F2") : "none") + (bestOk ? "" : " (tray best " + (t.BestCard != null ? t.BestCard.name : "none") + ")") + (bad.Count > 0 ? "; " + string.Join(", ", bad.GetRange(0, Mathf.Min(5, bad.Count))) : "");
        return ok == n && n > 0 && bestOk;
    }

    /// <summary>The best card's star twinkles: its scale / tilt change over a second.</summary>
    static IEnumerator Twinkle(IslandTray t, Action<bool> result)
    {
        var b = t.BestCard;
        if (b == null || b.star == null) { result(false); yield break; }
        var seen = new HashSet<string>();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.2f) { seen.Add(b.star.localScale.x.ToString("F2") + "/" + b.star.localEulerAngles.z.ToString("F0")); yield return null; }
        bool others = true;
        foreach (var c in t.Cards) if (c != null && c != b && c.star != null && c.star.gameObject.activeSelf && Mathf.Abs(c.star.localScale.x - 1f) > 0.001f) others = false;
        result(seen.Count >= 3 && others);
    }

    static IEnumerator Stars()
    {
        var sm = SongManager.I; var t = IslandTray.Ensure();
        // a) a grid reference: the island with the most notes, selected
        int bestI = -1, most = 0;
        for (int i = 0; i < sm.Islands.Count; i++) { var m = new List<int>(); var w = new List<float>(); int k = Harmony.NotesOfIsland(sm.Islands[i], m, w); if (k > most) { most = k; bestI = i; } }
        if (bestI < 0) { Line(false, "stars: the fixture has no pitched notes"); yield break; }
        var isl = sm.Islands[bestI];
        UIManager.I.SelectMeasure(bestI, false);
        Frame(isl.Center, 22f);
        yield return Wait(0.3f);
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
        t.RefreshStarsNow();
        var midi = new List<int>(); var wts = new List<float>();
        Harmony.NotesOfIsland(sm.Islands[bestI], midi, wts);
        string d = "";
        bool okA = t.StarRef == IslandTray.StarSource.Island && t.StarIsland == sm.Islands[bestI] && StarsMatch(t, midi, wts, out d);
        Line(okA, "stars, grid reference (the selected island's " + midi.Count + " notes): a card is starred exactly when Harmony.Fit ≥ " + Harmony.StarThreshold + ", the best one twinkles", "ref " + t.StarRef + ", " + d);
        bool tw = false; yield return Twinkle(t, v => tw = v);
        Line(tw, "the best card's star twinkles (pops and rocks on twos), the other stars sit still", t.BestCard != null ? t.BestCard.name : "no best");
        yield return Wait(0.2f);
        yield return Shot("u1_6_deck_grid.png");
        // b) the clipboard's pattern wins over the island, and a copy refreshes the stars
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && !c.IsOnMoon && c.nodes.Count >= 3 && c.Island != sm.Islands[bestI]) { src = c; break; }
        if (src != null)
        {
            int r0 = t.StarRefreshes;
            Clipboard.Copy(src);
            yield return null; yield return null;
            var cm = new List<int>(Clipboard.Midi); var cw = new List<float>(Clipboard.Weights);
            bool okB = t.StarRef == IslandTray.StarSource.Pattern && t.StarRefreshes > r0 && StarsMatch(t, cm, cw, out d);
            Line(okB, "stars follow the clipboard's pattern (a copy refreshes them: Clipboard.OnChanged)", "refreshes +" + (t.StarRefreshes - r0) + ", " + d);
            Clipboard.Clear();
            yield return null; yield return null;
            Line(t.StarRef == IslandTray.StarSource.Island, "clearing the clipboard gives the stars back to the focused island", "ref " + t.StarRef);
        }
        else Info("stars: no second pitched cube to copy");
        IslandTray.Close(); yield return Wait(0.3f);
        // c) a melody reference: a keyboard island with a tune (package K's keyboards)
        int kbAt = sm.AddKeyboardIsland(-1, false);
        kLive = kbAt >= 0 && kbAt < sm.Islands.Count && sm.Islands[kbAt] != null && sm.Islands[kbAt].IsKeyboard;
        if (kLive)
        {
            var kb = sm.Islands[kbAt];
            // a tune on its keys: a rising line through the key's scale, then home (key indices from the lowest key)
            var st = SongState.Capture();
            var cubes = new List<CubeState>(st.cubes ?? new CubeState[0]);
            int maxId = 0; foreach (var c in cubes) maxId = Mathf.Max(maxId, c.id);
            int[] keys = { 0, 4, 7, 9, 7, 4, 2, 0 };
            var cs = new CubeState { instrument = 3, measure = kbAt, xs = keys, zs = new int[keys.Length], rests = new bool[keys.Length], durs = new[] { 24, 24, 24, 24, 24, 24, 24, 24 }, id = maxId + 1 };
            cubes.Add(cs); st.cubes = cubes.ToArray();
            SongState.Apply(st); History.Push();
            yield return null; yield return null;
            kb = sm.Islands[kbAt];
            UIManager.I.SelectMeasure(kbAt, false);
            Frame(kb.Center, 22f); yield return Wait(0.3f);
            IslandTray.Open();
            yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
            t.RefreshStarsNow();
            midi.Clear(); wts.Clear();
            int notes = Harmony.NotesOfIsland(kb, midi, wts);
            bool okC = notes == keys.Length && t.StarRef == IslandTray.StarSource.Island && t.StarIsland == kb && StarsMatch(t, midi, wts, out d);
            Line(okC, "stars, melody reference (a tune on a keyboard island, " + notes + " notes): starred = Harmony.Fit ≥ threshold, the best twinkles", "ref " + t.StarRef + ", " + d);
            yield return Wait(0.3f);
            yield return Shot("u1_6_deck_melody.png");
            IslandTray.Close(); yield return Wait(0.3f);
        }
        else Info("stars, melody reference: package K's keyboards are not live yet (AddKeyboardIsland → " + kbAt + ")");
        LoadFixture(); yield return null;
        // d) no notes: the two best next chords after the focused chord
        sm.StartFreshSong(MusicTheory.RandomSong(6161));
        yield return Wait(0.5f);
        GlobalClock.Stop();
        int empty = -1;
        for (int i = 0; i < sm.Islands.Count; i++) { var m = new List<int>(); var w = new List<float>(); if (sm.Islands[i] != null && !sm.Islands[i].IsKeyboard && Harmony.NotesOfIsland(sm.Islands[i], m, w) == 0) { empty = i; break; } }
        if (empty >= 0)
        {
            UIManager.I.SelectMeasure(empty, false);
            Frame(sm.Islands[empty].Center, 22f); yield return Wait(0.3f);
            IslandTray.Open();
            yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
            t.RefreshStarsNow();
            var next = MusicTheory.SuggestNext(MusicTheory.KeyOfSong(), sm.Islands[empty].chordRootMIDI, 2);
            int ok = 0, n = 0, starred = 0; TrayCard first = null;
            foreach (var c in t.Cards)
            {
                if (c == null) continue;
                n++;
                bool want = MusicTheory.SameChord(c.data, next[0]) || MusicTheory.SameChord(c.data, next[1]);
                if (want) starred++;
                if (first == null && MusicTheory.SameChord(c.data, next[0])) first = c;
                if ((c.star != null && c.star.gameObject.activeSelf) == want) ok++;
            }
            Line(t.StarRef == IslandTray.StarSource.Next && ok == n && starred >= 2 && t.BestCard == first && first != null,
                 "stars with no notes yet: the two best next chords after the focused chord (MusicTheory.SuggestNext) are starred, the first twinkles",
                 "ref " + t.StarRef + ", " + ok + "/" + n + " right, " + starred + " starred (" + MusicTheory.ChordName(next[0].chordRootMIDI, next[0].semitones) + ", " + MusicTheory.ChordName(next[1].chordRootMIDI, next[1].semitones) + ")");
            yield return Wait(0.3f);
            yield return Shot("u1_6_deck_next.png");
            IslandTray.Close(); yield return Wait(0.3f);
        }
        else Info("stars with no notes: every island of the fresh song has notes");
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 4. the ⇄ swap chips and the rail's pulse
    static IEnumerator Swap()
    {
        var sm = SongManager.I; var t = IslandTray.Ensure(); var rail = UIManager.I.Rail;
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.05f, true);
        yield return Wait(0.4f);
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
        t.RebuildNow();
        yield return Wait(0.2f);
        // the job with the most islands, and a card doing it
        var count = new int[5];
        foreach (var kb in sm.Islands) if (kb != null) { int j = Harmony.Job(kb); if (j >= 1 && j <= 4) count[j]++; }
        int job = 1; for (int j = 2; j <= 4; j++) if (count[j] > count[job]) job = j;
        int ci = -1;
        for (int i = 0; i < t.Cards.Count; i++) if (t.Cards[i] != null && t.Cards[i].job == job && t.Cards[i].section == 0) { ci = i; break; }
        if (ci < 0) for (int i = 0; i < t.Cards.Count; i++) if (t.Cards[i] != null && t.Cards[i].job == job) { ci = i; break; }
        if (ci < 0) { Line(false, "swap chips: no card with job " + job); yield break; }
        var card = t.Cards[ci];
        // a real pointer enter on the card (its hover path), not the test hold
        var ev = new PointerEventData(EventSystem.current);
        card.OnPointerEnter(ev);
        yield return Wait(0.35f);
        int onScreen = 0;
        var cam = Camera.main;
        foreach (var kb in sm.Islands)
        {
            if (kb == null || kb.IsMoon || kb.IsKeyboard || Harmony.Job(kb) != job) continue;
            Vector3 sp = cam.WorldToScreenPoint(kb.VisualCenter + Vector3.up * 1.3f);
            if (sp.z > 0f && sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height) onScreen++;
        }
        bool chipsOk = t.SwapJob == job && t.SwapIslands.Count == count[job] && t.SwapChipsShown == onScreen && onScreen > 0;
        bool pulseOk = rail != null && rail.SwapJob == job && rail.PulsingCount == count[job];
        Line(chipsOk, "hovering a card puts a ⇄ swap chip over every island doing the same job (screen space, following the camera)", "job " + job + ": islands " + count[job] + ", chips " + t.SwapChipsShown + " (on screen " + onScreen + ")");
        Line(pulseOk, "…and those islands' rail beads pulse (HudColumnRail.SwapJob)", rail != null ? "pulsing " + rail.PulsingCount + " of " + count[job] : "no rail");
        yield return Wait(0.3f);
        yield return Shot("u1_6_swap.png");
        card.OnPointerExit(ev);
        yield return Wait(0.25f);
        Line(t.SwapChipsShown == 0 && t.SwapJob == 0 && (rail == null || (rail.SwapJob == 0 && rail.PulsingCount == 0)), "the chips and the pulse leave with the pointer", "chips " + t.SwapChipsShown + ", pulsing " + (rail != null ? rail.PulsingCount : -1));
        IslandTray.Close(); yield return Wait(0.3f);
    }

    // ------------------------------------------------------------------ 5. the keyboard card
    static IEnumerator KeyboardCard()
    {
        var sm = SongManager.I; var t = IslandTray.Ensure();
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
        var tool = t.Tool(4);
        var cap = tool != null ? tool.GetComponent<InkCaptionHover>() : null;
        Line(tool != null && tool.gameObject.activeInHierarchy && cap != null && cap.words == "keyboard" && Hints.Has("tray.keyboard"), "the deck's tools gain the keyboard card (a little piano, caption \"keyboard\", hint tray.keyboard)", tool != null ? tool.name : "none");
        yield return Wait(0.2f);
        yield return Shot("u1_6_keyboard_card.png");
        // a click: into the focused island's column (one History entry, K's op pushes it)
        UIManager.I.SelectMeasure(0, false);
        int n0 = sm.Islands.Count, p0 = pushes, k0 = t.KeyboardsAdded; events.Clear();
        int col0 = sm.Islands[0].column;
        t.SimTool(4);
        yield return null; yield return null;
        if (sm.Islands.Count == n0 + 1)
        {
            int at = UIManager.I.SelectedMeasure;
            var kb = at >= 0 && at < sm.Islands.Count ? sm.Islands[at] : null;
            bool inCol = kb != null && (kb.column == col0 || kb.column == sm.ColumnCount - 1);
            Line(kb != null && kb.IsKeyboard && inCol && pushes - p0 == 1 && t.KeyboardsAdded == k0 + 1 && events.Contains(Onboarding.Ev.KeyboardAdded),
                 "keyboard card click: a keyboard island into the focused island's column (else a new last column), one History entry, keyboard.added",
                 "island " + at + " kind " + (kb != null ? kb.kind : -1) + " column " + (kb != null ? kb.column : -1) + " (focused column " + col0 + "), pushes " + (pushes - p0));
        }
        else Info("keyboard card click: AddKeyboardIsland refused (package K's keyboards not live?) — islands " + n0 + " → " + sm.Islands.Count + ", pushes " + (pushes - p0) + ", the card pulsed");
        // a drag through the ghost onto the free sea → a new last column
        IslandTray.Close(); yield return Wait(0.2f);
        LoadFixture(); yield return null;
        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
        var b = sm.SongBounds;
        Vector3 drop = sm.FindFreeSpot(new Vector3(b.center.x, 0f, b.max.z + 12f), Vector3.forward, new Vector3(KeyBlock.IslandWidth + 1f, 3f, 8f));
        n0 = sm.Islands.Count; p0 = pushes;
        var g = t.SimKeyboardDragStart();
        yield return null;
        t.SimDragAt(drop);
        yield return null; yield return null;
        bool ghost = g != null;
        int at2 = t.SimDragRelease();
        yield return null;
        if (at2 >= 0) Line(ghost && sm.Islands.Count == n0 + 1 && sm.Islands[at2].IsKeyboard && pushes - p0 == 1 && !WorldInput.IsLockedBy("tray"), "keyboard card drag: the ghost follows the pointer on the sea; the drop places a keyboard island (one History entry, the tray's lock released)", "island " + at2 + ", column " + sm.Islands[at2].column);
        else Line(ghost && sm.Islands.Count == n0 && pushes == p0 && !WorldInput.IsLockedBy("tray") && !kLive, "keyboard card drag: the ghost works; the drop is refused until package K's keyboards are live (nothing placed, no History entry, lock released)", "ghost " + ghost + ", K live " + kLive);
        IslandTray.Close(); yield return Wait(0.3f);
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 6. the header: carry, keyboards, paste here
    static IEnumerator Header()
    {
        var sm = SongManager.I; var h = IslandHeader.I;
        if (h == null) { Line(false, "island header", "missing"); yield break; }
        int i = -1;
        for (int k = 0; k < sm.Islands.Count; k++) if (sm.Islands[k] != null && !sm.Islands[k].IsMoon && sm.Islands[k].column + 2 < sm.ColumnCount) { i = k; break; }
        if (i < 0) { Line(false, "carry: no island with two columns after it"); yield break; }
        var kb = sm.Islands[i];
        Frame(kb.Center, 16f); yield return Wait(0.5f);
        IslandHeader.Show(kb);
        yield return Wait(0.45f);
        var cb = h.Carry;
        var cap = cb != null ? cb.GetComponent<InkCaptionHover>() : null;
        float k0 = UIManager.I.Canvas != null ? Mathf.Max(0.01f, UIManager.I.Canvas.scaleFactor) : 1f;
        var corners = new Vector3[4]; float w = 0f, hh = 0f;
        if (cb != null) { ((RectTransform)cb.transform).GetWorldCorners(corners); w = Vector3.Distance(corners[0], corners[3]) / k0; hh = Vector3.Distance(corners[0], corners[1]) / k0; }
        // §21 (the user: "just keep cubes on each grid … the long grid"): the carry button is the EXTEND → control (one long grid) — was "carry to next chord"
        Line(cb != null && cb.gameObject.activeInHierarchy && cap != null && cap.words == IslandHeader.ExtendCaptionOff && w >= 27.5f && hh >= 27.5f && Hints.Has("island.carry") && h.CarrySticker == "" && kb.carry == 0,
             "header: the carry button is EXTEND → (§21: a grid stretched to the right, caption \"" + IslandHeader.ExtendCaptionOff + "\", ≥ 28 px, hint island.carry), plain at 0", "size " + w.ToString("F0") + "×" + hh.ToString("F0") + ", caption \"" + (cap != null ? cap.words : "") + "\"");
        var seq = new List<string>(); bool okAll = true;
        for (int c = 0; c < 4; c++)
        {
            int before = sm.Islands[i].carry, want = before >= ProjectConfig.MaxCarry ? 0 : before + 1;
            int p0 = pushes; events.Clear();
            h.Carry.onClick();
            yield return null; yield return null;
            var now = sm.Islands[i];
            bool one = pushes - p0 == 1, evt = events.Contains(Onboarding.Ev.IslandCarry), same = IslandHeader.Current == now;
            string st = h.CarrySticker;
            bool stOk = want == 0 ? st == "" : st == "×" + want;
            okAll &= now.carry == want && one && evt && same && stOk;
            seq.Add(before + "→" + now.carry + (one ? "" : " pushes " + (pushes - p0)) + (evt ? "" : " no-event") + (same ? "" : " header-lost") + " \"" + st + "\"");
            if (want == 2)
            {
                var tg = sm.CarryTargets(i);
                Info("carry ×2 on island " + i + ": targets " + tg.Count + " (" + string.Join(", ", tg.ConvertAll(x => "island " + sm.Islands.IndexOf(x))) + ")");
                yield return Wait(0.5f); yield return Shot("u1_6_header_carry2.png");
            }
        }
        Line(okAll && sm.Islands[i].carry == 0, "header carry: each click cycles 0 → 1 → 2 → 3 → 0 through SetCarry, one History entry and one island.carry event per click, the ×n sticker follows, the header stays on its island", string.Join(", ", seq));
        // the last column has no next chord: refused
        int last = -1;
        for (int k = sm.Islands.Count - 1; k >= 0; k--) if (sm.Islands[k] != null && !sm.Islands[k].IsMoon && sm.Islands[k].column == sm.ColumnCount - 1) { last = k; break; }
        if (last >= 0)
        {
            IslandHeader.Hide(); yield return Wait(0.2f);
            IslandHeader.Show(sm.Islands[last]); yield return Wait(0.6f);   // (the previous island lingers 0.4 s)
            int r0 = h.CarryRefusals, p0 = pushes;
            h.Carry.onClick(); yield return null;
            Line(h.CarryRefusals == r0 + 1 && pushes == p0 && sm.Islands[last].carry == 0, "header carry on the last column: refused (no next chord), no History entry", "refusals +" + (h.CarryRefusals - r0));
        }
        IslandHeader.Hide(); yield return Wait(0.2f);
        // paste here: only while the clipboard holds a pattern that can go there
        LoadFixture(); yield return null;
        kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.4f);
        IslandHeader.Show(kb); yield return Wait(0.4f);
        bool hiddenEmpty = !h.PasteShown;
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && !c.IsOnMoon && c.nodes.Count >= 2) { src = c; break; }
        bool shown = false, moonHidden = true;
        if (src != null)
        {
            Clipboard.Copy(src);
            yield return null; yield return null;
            shown = h.PasteShown && Hints.IsVisible("island.paste");
            yield return Wait(0.3f);
            yield return Shot("u1_6_header_paste.png");
            int cubes0 = SequenceMaster.Cubes.Count, p0 = pushes;
            h.PasteHereButton.onClick();
            yield return null; yield return null;
            if (SequenceMaster.Cubes.Count == cubes0 + 1) Line(pushes - p0 == 1, "paste here: Clipboard.PasteOn(this island) adds the adapted cube, one History entry", "cubes " + cubes0 + " → " + SequenceMaster.Cubes.Count + ", pushes " + (pushes - p0));
            else Info("paste here: Clipboard.PasteOn is still package H's stub (cubes " + cubes0 + " → " + SequenceMaster.Cubes.Count + ", pushes " + (pushes - p0) + ")");
            if (sm.Moons.Count > 0) { IslandHeader.Show(sm.Moons[0]); yield return Wait(0.3f); moonHidden = !h.PasteShown; }
            Clipboard.Clear();
        }
        Line(hiddenEmpty && shown && moonHidden, "header \"paste here\": hidden with an empty clipboard, shown after a copy on a grid it fits (hint island.paste), hidden on a Moon for a pitched pattern", "empty hidden " + hiddenEmpty + ", shown " + shown + ", Moon hidden " + moonHidden);
        IslandHeader.Hide(); yield return Wait(0.2f);
        // a keyboard island's header (package K's keyboards)
        LoadFixture(); yield return null;
        int kbAt = sm.AddKeyboardIsland(-1, false);
        if (kbAt >= 0 && sm.Islands[kbAt] != null && sm.Islands[kbAt].IsKeyboard)
        {
            var key = sm.Islands[kbAt];
            Frame(key.Center, 16f); yield return Wait(0.5f);
            IslandHeader.Show(key); yield return Wait(0.45f);
            h.More.onClick(); yield return Wait(0.3f);
            bool has = h.Grip.gameObject.activeInHierarchy && h.Eye.gameObject.activeInHierarchy && h.RegisterUp.gameObject.activeInHierarchy && h.RegisterDown.gameObject.activeInHierarchy
                    && h.Repeat.gameObject.activeInHierarchy && h.Carry.gameObject.activeInHierarchy && h.DuplicateRight.gameObject.activeInHierarchy && h.More.gameObject.activeInHierarchy && h.DeleteButton.gameObject.activeInHierarchy;
            bool lacks = !h.ChordButton.gameObject.activeInHierarchy && !h.MoodButton.gameObject.activeInHierarchy && !h.FillButton.gameObject.activeInHierarchy && !h.EnergyButton.gameObject.activeInHierarchy
                    && !h.FallButton.gameObject.activeInHierarchy && !h.BarsButton(1).gameObject.activeInHierarchy && !h.AddAbove.gameObject.activeInHierarchy && !h.AddBelow.gameObject.activeInHierarchy;
            Line(has && lacks, "a keyboard island's header: grip, eye, register, repeat, carry, duplicate, more (delete only) — no chord wheel, weather, fill, length, energy, fall or + ghosts", "has " + has + ", lacks the chord items " + lacks);
            yield return Wait(0.2f);
            yield return Shot("u1_6_header_keyboard.png");
            IslandHeader.CloseMore();
        }
        else Info("keyboard header: package K's keyboards not live (AddKeyboardIsland → " + kbAt + ")");
        IslandHeader.Hide();
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 7. the rail
    static IEnumerator Rail()
    {
        var sm = SongManager.I; var rail = UIManager.I.Rail;
        if (rail == null) { Line(false, "rail", "missing"); yield break; }
        int kbAt = sm.AddKeyboardIsland(-1, false);
        // a carry chain: the first island that has two columns after it, ×2
        int i = -1;
        for (int k = 0; k < sm.Islands.Count; k++) if (sm.Islands[k] != null && !sm.Islands[k].IsMoon && !sm.Islands[k].IsKeyboard && sm.Islands[k].column + 2 < sm.ColumnCount) { i = k; break; }
        if (i >= 0) sm.SetCarry(i, 2);
        yield return null; yield return null;
        rail.CountNow();
        int ok = 0, n = 0; var bad = new List<string>();
        for (int k = 0; k < sm.Islands.Count; k++)
        {
            var kb = sm.Islands[k]; if (kb == null) continue;
            n++;
            int want = Harmony.Job(kb);
            if (rail.BeadJob(k) == want && rail.BadgeShown(k) == want && rail.BeadKeyboard(k) == kb.IsKeyboard) ok++; else bad.Add(k + ": job " + rail.BeadJob(k) + "/" + rail.BadgeShown(k) + " want " + want);
        }
        Line(ok == n, "rail: every chord island's bead wears its job badge (keyboards and Moons none; a keyboard's bead is a piano strip)", ok + "/" + n + (bad.Count > 0 ? "; " + string.Join(", ", bad) : "") + (kbAt >= 0 ? ", keyboard bead " + kbAt : ", no keyboard (K not live)"));
        // §21: a carry is a LONG GRID — one bar around its beads, no hops (was: "a small arc hops between beads, arcs = Σ CarryTargets")
        // (§21, package K's runs: one bar per SongManager.LongGrids run — a chain of extends is one — and one of them starts at the extended grid)
        int longs = sm.LongGrids.Count; bool fromI = false;
        for (int k = 0; k < rail.LongGridCount; k++) if (rail.LongGridFrom(k) == i) fromI = true;
        Line(i >= 0 && rail.CarryArcCount == 0 && rail.LongGridCount == longs && longs > 0 && fromI, "rail (§21): a carry is one long-grid bar around its beads, no hops between them",
             "long grids " + rail.LongGridCount + " = " + longs + ", arcs " + rail.CarryArcCount + " (island " + i + " ×2, a bar from it " + fromI + ")");
        GlobalClock.Seek(sm.ColumnStart(Mathf.Max(0, sm.Islands[Mathf.Max(0, i)].column)) + 0.5f);
        GlobalClock.Play();
        yield return Wait(0.8f);
        yield return Shot("u1_6_rail.png");
        GlobalClock.Stop(); GlobalClock.Seek(0);
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 7b. SPEC §11: Moons start at a column; the rail's drum sections
    static IEnumerator Moons()
    {
        var sm = SongManager.I; var ui = UIManager.I; var rail = ui.Rail; var t = IslandTray.Ensure();
        if (sm.ColumnCount < 5) { Line(false, "moons: the fixture needs five columns", "columns " + sm.ColumnCount); yield break; }
        // the fixture's Moons out: a song without percussion, then Moons at two columns
        while (sm.Moons.Count > 0) ui.RemoveMoon(0);
        yield return null;
        int c2 = -1; for (int k = 0; k < sm.Islands.Count; k++) if (sm.Islands[k] != null && sm.Islands[k].column == 2) { c2 = k; break; }
        ui.SelectMeasure(c2, false);
        int focus = IslandTray.FocusedColumn();
        int n0 = sm.Moons.Count, p0 = pushes, a0 = t.MoonsAdded; events.Clear();
        IslandTray.Open(); yield return WaitFor(() => IslandTray.I.ShownAmount > 0.98f, 2f);
        t.SimTool(3);
        yield return null; yield return null;
        int m1 = sm.Moons.Count - 1;
        bool clickOk = focus == 2 && sm.Moons.Count == n0 + 1 && sm.MoonStartColumn(m1) == 2 && pushes - p0 == 1 && t.MoonsAdded == a0 + 1 && events.Contains(Onboarding.Ev.MoonAdded);
        Line(clickOk, "the Moon card starts a Moon at the focused column (the selected island's: SongManager.AddMoon(column)), one History entry, moon.added", "focused column " + focus + ", moons " + n0 + " → " + sm.Moons.Count + ", start " + (m1 >= 0 ? sm.MoonStartColumn(m1) : -1) + ", pushes " + (pushes - p0));
        // a drag: dropped in line with column 4
        var a4 = sm.AnchorOf(4);
        Vector3 ground = a4 != null ? new Vector3(a4.Center.x, 0f, a4.FrontEdge - 5f) : Vector3.zero;
        p0 = pushes;
        int m2 = t.SimMoonDrop(ground);
        yield return null; yield return null;
        bool dragOk = m2 >= 0 && sm.MoonStartColumn(m2) == 4 && pushes - p0 == 1;
        Line(dragOk, "dragging the Moon card onto the sea starts the Moon at the column it is in line with (column 4), one History entry", "moon " + m2 + " start " + (m2 >= 0 ? sm.MoonStartColumn(m2) : -1) + ", pushes " + (pushes - p0));
        IslandTray.Close(); yield return Wait(0.3f);
        // the rail: sections [2, 4) and [4, end); columns 0 and 1 have no percussion
        rail.CountNow();
        bool secOk = rail.DrumSectionCount == 2 && rail.DrumSectionStart(0) == 2 && rail.DrumSectionEnd(0) == 4 && rail.DrumSectionStart(1) == 4 && rail.DrumSectionEnd(1) == sm.ColumnCount
                     && !rail.HasDrums(0) && !rail.HasDrums(1) && rail.HasDrums(2) && rail.HasDrums(sm.ColumnCount - 1);
        var secs = new List<string>(); for (int k = 0; k < rail.DrumSectionCount; k++) secs.Add(rail.DrumSectionStart(k) + "→" + rail.DrumSectionEnd(k));
        Line(secOk, "rail: the drum sections — a line under the columns each Moon covers until the next Moon's start, a drum at each start, a gap where nothing drums", "sections " + string.Join(", ", secs) + ", drums at 0 " + rail.HasDrums(0) + ", 1 " + rail.HasDrums(1));
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(sm.SongBounds, 0.05f, true);
        yield return Wait(0.6f);
        yield return Shot("u1_6_rail_drums.png");
        LoadFixture(); yield return null;
    }

    // ------------------------------------------------------------------ 8. the tutorial's drawing step
    static IEnumerator Tutorial()
    {
        var sm = SongManager.I; var pm = PathManager.I;
        Line(Onboarding.TextOf(Onboarding.SDraw) == "pick a cube (the left column),\nthen click tiles" && Onboarding.TargetOf(Onboarding.SDraw) == "palette", "tutorial step 1 is now \"pick a cube (the left column), then click tiles\", pointing at the instrument column", "\"" + (Onboarding.TextOf(Onboarding.SDraw) ?? "").Replace('\n', ' ') + "\" → " + Onboarding.TargetOf(Onboarding.SDraw));
        Onboarding.Suppressed = false;
        Frame(sm.Islands[0].Center, 16f); yield return Wait(0.4f);
        Onboarding.StartTutorial(true);
        yield return Wait(0.9f);
        var bub = Onboarding.Bubble;
        bool hear = Onboarding.Step == Onboarding.SDraw && bub != null && bub.Visible && bub.Text == Onboarding.HearText.ToLowerInvariant() && Onboarding.ShownTarget == "world:island0";
        // two tiles heard (the empty hand's hover still auditions) → the pick-a-cube words at the instrument column
        var kb0 = sm.Islands[0]; var f0 = FreeTile(kb0, 1); var f1 = FreeTile(kb0, 5);
        int a0 = PathManager.IdleAuditionCount;
        if (f0 != null) { PathManager.SimPos = ScreenOf(f0.Top); yield return Wait(0.5f); }
        if (f1 != null) { PathManager.SimPos = ScreenOf(f1.Top); yield return Wait(0.6f); }
        PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        int heard = PathManager.IdleAuditionCount - a0;
        if (heard < 2) yield return WaitFor(() => bub != null && bub.Text != Onboarding.HearText.ToLowerInvariant(), 7.5f);
        yield return Wait(0.4f);
        bool pick = bub != null && bub.Visible && bub.Text == Onboarding.TextOf(Onboarding.SDraw).ToLowerInvariant() && Onboarding.ShownTarget == "palette";
        float tail = bub != null ? bub.TipDistance() : 999f;
        yield return Shot("u1_6_tutorial_pick.png");
        // a cube picked up → "click a tile and keep clicking" at the island
        pm.PickUpCube(2);
        yield return Wait(0.5f);
        bool draw = bub != null && bub.Visible && bub.Text == Onboarding.DrawText.ToLowerInvariant() && Onboarding.ShownTarget == "world:island0";
        yield return Shot("u1_6_tutorial_draw.png");
        pm.PutDown();
        yield return Wait(0.4f);
        bool back = bub != null && bub.Text == Onboarding.TextOf(Onboarding.SDraw).ToLowerInvariant();
        // drawing two tiles with the cube in the hand still completes the step
        pm.PickUpCube(2);
        yield return null;
        var t0 = FreeTile(kb0, 0); var t1 = FreeTile(kb0, 2);
        if (t0 != null && t1 != null)
        {
            Frame(kb0.Center, 14f); yield return Wait(0.4f);
            Vector3 s0 = ScreenOf(t0.Top), s1 = ScreenOf(t1.Top);
            pm.SimPointer(s0, true, true, false); pm.SimPointer(s0, false, false, true);
            yield return null;
            pm.SimPointer(s1, true, true, false); pm.SimPointer(s1, false, false, true);
            yield return WaitFor(() => Onboarding.Step != Onboarding.SDraw, 2f);
        }
        bool advanced = Onboarding.Step == Onboarding.SLength;
        int stepNow = Onboarding.Step;
        if (pm.IsDrawing) pm.CancelPath();
        pm.PutDown();
        Onboarding.Skip();
        Onboarding.Suppressed = true;
        yield return Wait(0.3f);
        Line(hear && pick && draw && back && tail <= 24f, "tutorial step 1: the hearing line, then \"pick a cube\" at the instrument column (empty hand), then \"click a tile and keep clicking\" at the island once a cube is in the hand (and back when it is put down)",
             "hear " + hear + " (heard " + heard + "), pick " + pick + " (tail " + tail.ToString("F1") + " px), draw " + draw + ", back " + back);
        Line(advanced, "tutorial step 1 completes when the held cube draws two tiles (AutoHand off)", "step " + stepNow);
    }

    // ------------------------------------------------------------------ 9. the v6 tips (each once)
    static IEnumerator Tips()
    {
        var sm = SongManager.I;
        Onboarding.Suppressed = true;
        if (Onboarding.Active) Onboarding.Skip();
        PlayerPrefs.SetInt(Onboarding.PrefDone, 1);
        foreach (var id in Onboarding.AllTips) PlayerPrefs.SetInt(Onboarding.PrefTip + id, 1);
        foreach (var id in Onboarding.V6Tips) PlayerPrefs.DeleteKey(Onboarding.PrefTip + id);
        Onboarding.Suppressed = false;
        yield return null;
        var got = new List<string>();
        // press (an empty-hand press: PathManager.PressTile counts it)
        var pt = FreeTile(sm.Islands[0], 0);
        yield return TipOnce(Onboarding.TipPress, Onboarding.PressText, () => { if (pt != null) PathManager.I.PressTile(pt); else Onboarding.Notify(Onboarding.Ev.TilePressed); }, null, got);
        // copy / paste (observed: Clipboard.OnChanged)
        AudioCube src = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.IsDrums && !c.IsOnMoon && c.nodes.Count >= 2) { src = c; break; }
        yield return TipOnce(Onboarding.TipCopy, Onboarding.CopyText, () => { if (src != null) Clipboard.Copy(src); }, null, got);
        Clipboard.Clear();
        // carry (the header's carry click)
        int ci = -1;
        for (int k = 0; k < sm.Islands.Count; k++) if (sm.Islands[k] != null && !sm.Islands[k].IsMoon && sm.Islands[k].column + 1 < sm.ColumnCount) { ci = k; break; }
        if (ci >= 0)
        {
            Frame(sm.Islands[ci].Center, 16f); yield return Wait(0.4f);
            IslandHeader.Show(sm.Islands[ci]); yield return Wait(0.4f);
            yield return TipOnce(Onboarding.TipCarry, Onboarding.CarryText, () => IslandHeader.I.Carry.onClick(), "u1_6_tip_carry.png", got);
            IslandHeader.Hide();
            LoadFixture(); yield return null;
        }
        // the keyboard (the deck's keyboard card reports keyboard.added)
        yield return TipOnce(Onboarding.TipKeyboard, Onboarding.KeyboardText, () => Onboarding.Notify(Onboarding.Ev.KeyboardAdded), null, got);
        // numbers & stars (a deck open once the vibes tip was seen)
        yield return TipOnce(Onboarding.TipJobs, Onboarding.JobsText, () => IslandTray.Open(), "u1_6_tip_jobs.png", got, () => { IslandTray.Close(); });
        // drum moons follow the song (the Moon card reports moon.added)
        yield return TipOnce(Onboarding.TipMoons, Onboarding.MoonsText, () => Onboarding.Notify(Onboarding.Ev.MoonAdded), null, got);
        // the tower (the first ▲)
        var kb = sm.Islands[Mathf.Min(1, sm.Islands.Count - 1)];
        Frame(kb.Center, 16f); yield return Wait(0.4f);
        IslandHeader.Show(kb); yield return Wait(0.4f);
        yield return TipOnce(Onboarding.TipTower, Onboarding.TowerText, () => IslandHeader.I.RegisterUp.onClick(), null, got);
        IslandHeader.Hide();
        Onboarding.Suppressed = true;
        LoadFixture(); yield return null;
        Line(got.Count == Onboarding.V6Tips.Length && !got.Exists(s => s.Contains("✗")), "the seven v6 tips (press, copyPaste, carry, keyboard, jobs, tower, moons) each show their words once, then never again", string.Join("; ", got));
    }

    static IEnumerator TipOnce(string id, string text, Action trigger, string shot, List<string> got, Action after = null)
    {
        yield return WaitFor(() => Onboarding.ActiveTip == null, 5f);
        trigger();
        yield return WaitFor(() => Onboarding.ActiveTip == id, 1.5f);
        yield return Wait(0.5f);
        var bub = Onboarding.Bubble;
        bool shown = Onboarding.ActiveTip == id && bub != null && bub.Visible && bub.Text == text.ToLowerInvariant();
        if (shot != null) yield return Shot(shot);
        if (after != null) after();
        yield return WaitFor(() => Onboarding.ActiveTip == null, 6f);
        trigger();
        yield return Wait(0.5f);
        bool once = Onboarding.ActiveTip != id && PlayerPrefs.GetInt(Onboarding.PrefTip + id, 0) == 1;
        if (after != null) after();
        if (Onboarding.ActiveTip != null && bub != null && bub.SkipButton != null) { bub.SkipButton.onClick(); yield return null; }
        got.Add(id + (shown && once ? "" : " ✗ (shown " + shown + ", once " + once + ", \"" + (bub != null ? bub.Text.Replace('\n', ' ') : "") + "\")"));
    }
}
