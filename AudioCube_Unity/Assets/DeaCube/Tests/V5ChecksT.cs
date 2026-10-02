using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package T Play-mode checks (SPEC v5 §4): the title menu's gallery. With injected test songs (Gallery.UseTestSongs: a four-island song with
/// a second lane, a two-pass column, a bass rider, keys, a pad and a Moon kick; an eight-bar strings song; the v1 fixture) so they do not
/// depend on package G: the "gallery" word (hidden with no songs, between "new song" and "learn" otherwise), the shelf (one dealt card per
/// entry, on screen under the title, lowercase titles, tempo digits, vibe colours, "after …" on the focused card only), focus and keys
/// (← → ↑ ↓, "back"), the preview (its notes derived from the song — pitches, lengths, passes, rider, Moon — scheduled under its own owner,
/// audible (a WAV), the menu music held, the wall tinted and hopping on its beat), leaving the card (stopped within a beat, nothing queued
/// after, the menu music back), Esc / back, opening a song (Gallery.Open, "HIT IT!", playing from bar 0 once the world is back, the camera
/// framing it, a backup only when the replaced song had cubes of its own); then the real index (package G's songs) when it has entries.
/// Captures Captures/t5_*.png, report Captures/t5_report.txt. Start with <see cref="Run"/> right after entering Play mode (after
/// SongIO.QuitAutosave = false); poll <see cref="Done"/> or the report. Restores the autosave / backups, the tutorial prefs and the test
/// injection; never writes the user's save.
/// </summary>
public static class V5ChecksT
{
    public static bool Done = true;
    public static string Report = "";
    public static string CaptureDir => V2Checks.CapturePath;
    public static string ReportPath => Path.Combine(CaptureDir, "t5_report.txt");
    static StringBuilder sb;
    static int num;
    static readonly List<string> errors = new List<string>();

    public static string Run(bool captures = true, bool realSongs = true)
    {
        if (!Application.isPlaying || MainMenu.I == null) return "FAIL needs Play mode (MainMenu missing)";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0;
        try { Directory.CreateDirectory(CaptureDir); if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        MainMenu.I.StartCoroutine(Routine(captures, realSongs));
        return "started";
    }

    static void Line(bool ok, string what) { num++; sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(what).Append('\n'); }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); }

    static void Shoot(string file)
    {
        Directory.CreateDirectory(CaptureDir);
        string p = Path.Combine(CaptureDir, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static IEnumerator WaitFor(Func<bool> cond, float seconds)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            bool ok = false; try { ok = cond(); } catch (Exception) { }
            if (ok) yield break;
            yield return null;
        }
    }

    static IEnumerator ShowMenu()
    {
        if (!MainMenu.IsShown) MainMenu.Show();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return new WaitForSecondsRealtime(0.35f);
    }

    static IEnumerator Key(KeyCode k)
    {
        KeyShim.Sim(k, true, true, false);
        yield return null;
        KeyShim.Clear();
        yield return null;
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) lock (errors) errors.Add(type + ": " + (msg.Length > 200 ? msg.Substring(0, 200) : msg));
    }

    // ------------------------------------------------------------------ the test songs (SongState built in code)
    static MeasureState M(string key, int root, int[] semis, int col, int repeat, float pz = 0f)
    {
        return new MeasureState { chordKey = key, root = root, semis = semis, bars = 1, col = col, repeat = repeat, placed = true, pz = pz, energy = 2 };
    }

    static int cubeId;
    static CubeState C(int inst, int measure, int[] xs, int[] zs, int[] durs)
    {
        int n = xs.Length;
        cubeId++;
        return new CubeState
        {
            instrument = inst, measure = measure, xs = xs, zs = zs, rests = new bool[n], mods = new int[n], durs = durs,
            step = 1, gate = 1, mode = 0, volume = 1f, hits = -1, moon = -1, twinOf = -1, seed = 7919 * cubeId + 13, id = 900 + cubeId
        };
    }

    static SongState Blank(string name, float bpm, int tonic, bool minor)
    {
        var st = new SongState { name = name, bpm = bpm, beatsPerBar = 4, loop = true, version = SongState.CurrentVersion, keyTonic = tonic, keyMinor = minor, tone = 1f, space = 1f };
        st.instVolume = new float[Instruments.Count]; for (int i = 0; i < st.instVolume.Length; i++) st.instVolume[i] = 0.9f;
        st.instMuted = new bool[Instruments.Count];
        st.moons = new MeasureState[0];
        return st;
    }

    /// <summary>Song A (120 BPM): column 0 = C maj7 + a second lane G7, column 1 = A m7 at ×2 (two passes), column 2 = F maj7; a Moon.
    /// Lead C E G (8th 8th quarter) on C; a bass rider (half notes on the roots); keys C E on A m7's first inversion; a pad on G7's second
    /// row; a kick every beat on the Moon.</summary>
    static SongState SongA()
    {
        cubeId = 0;
        var st = Blank("t5 sun", 120f, 0, false);
        st.measures = new[]
        {
            M("Cmaj7", 60, new[] { 0, 4, 7, 11 }, 0, 1, 0f),
            M("G7", 67, new[] { 0, 4, 7, 10 }, 0, 1, -7.5f),
            M("Am7", 57, new[] { 0, 3, 7, 10 }, 1, 2, 0f),
            M("Fmaj7", 65, new[] { 0, 4, 7, 11 }, 2, 1, 0f),
        };
        st.moons = new[] { new MeasureState { chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, px = -9f, pz = 4f, energy = 2 } };
        var bass = C(4, 0, new[] { 0 }, new[] { 0 }, new[] { 48 }); bass.rider = true;
        var kick = C(9, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 0, 0, 0 }, new[] { 24, 24, 24, 24 }); kick.moon = 0;
        st.cubes = new[]
        {
            C(3, 0, new[] { 0, 0, 0 }, new[] { 0, 1, 2 }, new[] { 12, 12, 24 }),
            bass,
            C(0, 2, new[] { 1, 1 }, new[] { 0, 1 }, new[] { 24, 24 }),
            kick,
            C(2, 1, new[] { 0 }, new[] { 1 }, new[] { 96 }),
        };
        return st;
    }

    /// <summary>Song B (90 BPM, D minor): eight one-bar columns, a strings whole note on each root, bells 8ths on the first island (the preview
    /// keeps 6 bars: 16 s).</summary>
    static SongState SongB()
    {
        cubeId = 100;
        var st = Blank("t5 rain", 90f, 2, true);
        st.measures = new[]
        {
            M("Dm9", 62, new[] { 0, 3, 7, 10, 14 }, 0, 1), M("A#maj7", 58, new[] { 0, 4, 7, 11 }, 1, 1), M("Gm7", 55, new[] { 0, 3, 7, 10 }, 2, 1),
            M("A7", 57, new[] { 0, 4, 7, 10 }, 3, 1), M("Dm7", 62, new[] { 0, 3, 7, 10 }, 4, 1), M("Fmaj7", 65, new[] { 0, 4, 7, 11 }, 5, 1),
            M("C7", 60, new[] { 0, 4, 7, 10 }, 6, 1), M("Asus4", 57, new[] { 0, 5, 7, 10 }, 7, 1),
        };
        var cubes = new List<CubeState>();
        for (int i = 0; i < 8; i++) cubes.Add(C(6, i, new[] { 0 }, new[] { 0 }, new[] { 96 }));
        cubes.Add(C(5, 0, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 1, 1 }, new[] { 12, 12, 12, 12 }));
        st.cubes = cubes.ToArray();
        return st;
    }

    static readonly string[] Ids = { "t5-sun", "t5-rain", "t5-fixture" };

    static void InjectSongs()
    {
        var entries = new[]
        {
            new GalleryEntry { id = Ids[0], title = "Sun Test", after = "Melt · ryo (supercell)", vibe = (int)VibeKind.Sunny, bpm = 120f, bars = 5, file = "Gallery/" + Ids[0], blurb = "test" },
            new GalleryEntry { id = Ids[1], title = "rain test", after = "Rolling Girl · wowaka", vibe = (int)VibeKind.Rainy, bpm = 90f, bars = 8, file = "Gallery/" + Ids[1] },
            new GalleryEntry { id = Ids[2], title = "old friend", after = "the v1 fixture · deacube", vibe = (int)VibeKind.Dreamy, bpm = 121f, bars = 6, file = "Gallery/" + Ids[2] },
        };
        var songs = new Dictionary<string, string>
        {
            { entries[0].file, SongA().ToJson() }, { entries[1].file, SongB().ToJson() }, { entries[2].file, File.ReadAllText(V2Checks.FixturePath) },
        };
        Gallery.UseTestSongs(entries, songs);
    }

    // ------------------------------------------------------------------ helpers on the shelf
    static bool OnScreen(Rect r) => r.xMin >= -1f && r.yMin >= -1f && r.xMax <= Screen.width + 1f && r.yMax <= Screen.height + 1f;

    static string NotesOn(MenuPreview.Track t, int slot, out List<double> on, out List<int> midi)
    {
        on = new List<double>(); midi = new List<int>();
        var s = new StringBuilder();
        foreach (var n in t.notes) if (n.slot == slot) { on.Add(n.on); midi.Add(n.midi); if (s.Length < 160) s.Append(n.midi).Append('@').Append(n.on.ToString("0.##")).Append(' '); }
        return s.ToString().Trim();
    }

    static bool Seq(List<double> on, List<int> midi, double[] wantOn, int[] wantMidi)
    {
        if (on.Count != wantOn.Length || midi.Count != wantMidi.Length) return false;
        for (int i = 0; i < wantOn.Length; i++) if (Math.Abs(on[i] - wantOn[i]) > 1e-3 || midi[i] != wantMidi[i]) return false;
        return true;
    }

    /// <summary>16-bit stereo WAV: overall RMS and peak (dBFS) and the fraction of 50 ms windows above −50 dBFS.</summary>
    static void WavStats(string path, out float rmsDb, out float peakDb, out float active)
    {
        rmsDb = -120f; peakDb = -120f; active = 0f;
        if (!File.Exists(path)) return;
        var b = File.ReadAllBytes(path);
        int n = (b.Length - 44) / 2;
        if (n <= 0) return;
        double sum = 0, win = 0; int peak = 0, wn = 0, windows = 0, loud = 0, per = Synth.SampleRate / 20 * 2;
        for (int i = 0; i < n; i++)
        {
            int v = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8));
            int a = Math.Abs(v); if (a > peak) peak = a;
            sum += (double)v * v; win += (double)v * v; wn++;
            if (wn >= per) { windows++; if (10.0 * Math.Log10(win / wn / (32768.0 * 32768.0) + 1e-12) > -50.0) loud++; win = 0; wn = 0; }
        }
        rmsDb = (float)(10.0 * Math.Log10(sum / n / (32768.0 * 32768.0) + 1e-12));
        peakDb = (float)(20.0 * Math.Log10(peak / 32768.0 + 1e-9));
        active = windows > 0 ? loud / (float)windows : 0f;
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine(bool captures, bool realSongs)
    {
        sb = new StringBuilder();
        lock (errors) errors.Clear();
        Application.logMessageReceived += OnLog;
        string userSave = Md5(SongIO.Path);
        var saves = V3Fixes.SnapshotSaves();
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, -1), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, -1);
        bool suppressed = Onboarding.Suppressed;
        Onboarding.Suppressed = true;
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        SongState fixture = SongState.FromJson(File.ReadAllText(V2Checks.FixturePath));
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        yield return null;
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        yield return null;

        // ---- 1. no songs: no word
        Gallery.UseTestSongs(new GalleryEntry[0], null);
        yield return ShowMenu();
        var words = MainMenu.Words;
        var gw = MainMenu.GalleryButton;
        Line(words.Length == 5 && gw != null && !gw.gameObject.activeSelf && Gallery.Entries.Count == 0, "no gallery songs: the \"gallery\" word is built but hidden (" + words.Length + " words, gallery shown " + (gw != null && gw.gameObject.activeSelf) + ")");
        MainMenu.MoveFocus(-9); yield return null;   // (StepFocus moves one step whatever the sign's size)
        int f0 = MainMenu.FocusedButton != null ? MainMenu.FocusedButton.index : -1;
        var seen = new List<int>();
        for (int i = 0; i < 5; i++) { MainMenu.MoveFocus(1); yield return null; seen.Add(MainMenu.FocusedButton != null ? MainMenu.FocusedButton.index : -1); }
        Line(!seen.Contains(2), "no gallery songs: ↓ never lands on the hidden word (focus " + f0 + " → " + string.Join(" ", seen) + ")");

        // ---- 2. three songs: the word appears between "new song" and "learn"
        InjectSongs();
        yield return null; yield return null;
        words = MainMenu.Words;
        bool order = words.Length == 5 && words[1] == MainMenu.NewButton && words[2] == gw && words[3] == MainMenu.LearnButton;
        Canvas.ForceUpdateCanvases();
        yield return null;
        float yNew = MainMenu.NewButton.ScreenCenter.y, yGal = gw.ScreenCenter.y, yLearn = MainMenu.LearnButton.ScreenCenter.y;
        Line(gw.gameObject.activeSelf && order && yNew > yGal && yGal > yLearn && gw.label.text == "gallery" && gw.label.font == Comic.Font && gw.IsText,
             "gallery songs: the lowercase word \"" + gw.label.text + "\" shows between new song and learn (screen y " + yNew.ToString("F0") + " > " + yGal.ToString("F0") + " > " + yLearn.ToString("F0") + ")");
        // focus it with the keys
        for (int i = 0; i < 6 && (MainMenu.FocusedButton == null || MainMenu.FocusedButton != gw); i++) { yield return Key(KeyCode.DownArrow); }
        yield return new WaitForSecondsRealtime(0.4f);
        Line(MainMenu.FocusedButton == gw && Mathf.Approximately(gw.UnderlineDrawn, 1f), "keys: ↓ reaches \"gallery\" (focused, underlined " + gw.UnderlineDrawn.ToString("F2") + ")");
        if (captures) { MainMenu.SimulatePointer(new Vector2(Screen.width * 0.86f, Screen.height * 0.12f)); yield return new WaitForSecondsRealtime(0.5f); Shoot("t5_menu.png"); yield return new WaitForSecondsRealtime(0.4f); }

        // ---- 3. Enter on the word: the shelf
        var shelf = MainMenu.Shelf;
        int opens0 = shelf.Opens;
        yield return Key(KeyCode.Return);
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.55f);   // the three cards deal in on twos
        var cards = shelf.Cards;
        bool allDealt = cards.Count == 3; foreach (var c in cards) if (!c.body.gameObject.activeSelf) allDealt = false;
        // 2026-10-01 (the user: "the song gallery should fill up the whole page — DEACUBE should disappear and it should just be the background and
        // projects"): the title sinks into the wall while the shelf is open and the cards use the page (ShelfGrid), no longer a row under the title
        var stT = MainMenu.Stage;
        Rect title = stT.TitleRestRect();
        bool inside = true, apart = true; float topEdge = float.MinValue; string rects = "";
        for (int i = 0; i < cards.Count; i++)
        {
            Rect r = cards[i].ScreenRect;
            rects += "[" + r.xMin.ToString("F0") + ".." + r.xMax.ToString("F0") + " × " + r.yMin.ToString("F0") + ".." + r.yMax.ToString("F0") + "] ";
            if (!OnScreen(r)) inside = false;
            topEdge = Mathf.Max(topEdge, r.yMax);
            for (int j = 0; j < i; j++) if (cards[j].ScreenRect.Overlaps(r)) apart = false;
        }
        bool sunk = stT.TitleDown && stT.LetterPinsLit == 0 && stT.MaxLetterLift <= stT.MaxWallLiftInWord + 0.2f;
        Line(MainMenu.ShelfOpen && shelf.Opens == opens0 + 1 && allDealt && shelf.Deals >= 3 && MainMenu.WordsAlpha < 0.01f,
             "Enter on \"gallery\": the words give way (alpha " + MainMenu.WordsAlpha.ToString("F2") + ") to the shelf, " + cards.Count + " cards dealt (" + shelf.Deals + " deals)");
        Line(inside && apart && sunk && topEdge > title.yMin, "shelf: the title sank into the wall (letters lift " + stT.MaxLetterLift.ToString("F2") + " ≤ the wall's " + stT.MaxWallLiftInWord.ToString("F2")
             + ", none lit) and the cards use the page — every card on screen, none overlapping, the top row (top y " + topEdge.ToString("F0") + ") above where the title's bottom edge stood (y "
             + title.yMin.ToString("F0") + "), scale " + shelf.CardScale.ToString("F2") + ": " + rects);
        bool faces = true; string faceInfo = "";
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i]; var e = Gallery.Entries[i];
            bool ok = c.title.text == (e.title ?? "").ToLowerInvariant() && c.tempo.text == Mathf.RoundToInt(e.bpm).ToString() && c.VibeColor == Vibe.ColorOf((VibeKind)e.vibe)
                      && c.paper.color == IslandTray.PaperOf(Vibe.ColorOf((VibeKind)e.vibe)) && c.glyph != null && c.glyph.sprite != null && c.glyph.sprite == Comic.Glyph(Vibe.Icon(c.Vibe), 74f, true)
                      && c.title.font == Comic.Font && c.tempo.font == Comic.DigitFont;
            if (!ok) faces = false;
            faceInfo += "\"" + c.title.text + "\" " + c.tempo.text + " " + Vibe.Word(c.Vibe) + "; ";
        }
        Line(faces, "cards: lowercase Fredoka titles, Bangers tempo digits, the vibe's paper (the deck cards' IslandTray.PaperOf) and glyph (VibeGlyphs) (" + faceInfo.Trim() + ")");
        var bk = shelf.BackButton;
        Line(bk != null && bk.gameObject.activeInHierarchy && bk.label.text == "back" && OnScreen(new Rect(bk.ScreenCenter, Vector2.one)), "shelf: the \"back\" word is up (at " + (bk != null ? bk.ScreenCenter.ToString() : "?") + ")");
        int afterShown = 0; foreach (var c in cards) if (c.AfterShown) afterShown++;
        Line(shelf.FocusIndex == 0 && cards[0].Focused && cards[0].AfterShown && afterShown == 1 && cards[0].after.text.StartsWith("after melt"),
             "focus: the first card has it on opening and alone shows \"" + cards[0].after.text + "\" (" + afterShown + " shown)");

        // ---- 4. keys along the shelf
        yield return Key(KeyCode.RightArrow); int k1 = shelf.FocusIndex;
        yield return Key(KeyCode.DownArrow); int k2 = shelf.FocusIndex;
        yield return Key(KeyCode.DownArrow); int k3 = shelf.FocusIndex;   // stays on the last card (no wrap)
        yield return Key(KeyCode.LeftArrow); int k4 = shelf.FocusIndex;
        yield return Key(KeyCode.UpArrow); int k5 = shelf.FocusIndex;
        yield return Key(KeyCode.LeftArrow); bool kBack = shelf.BackFocused;
        yield return Key(KeyCode.RightArrow); int k6 = shelf.FocusIndex;
        MainMenu.MoveFocus(1); yield return null; int k7 = shelf.FocusIndex;
        Line(k1 == 1 && k2 == 1 && k3 == 1 && k4 == 0 && k5 == -1 && kBack && k6 == 0 && k7 == 1,
             "keys on the grid (one row of 3 cards): → along the row, ↓ ↓ stay (no row below), ← back along it, ↑ from the top row to \"back\", ← stays, → to the first card; MoveFocus steps one ("
             + k1 + " " + k2 + " " + k3 + " " + k4 + " " + k5 + " back " + kBack + " " + k6 + " " + k7 + ")");

        // ---- 5. the preview of song A (focus card 0; first "back" until the menu music is back, so the hold is counted afresh)
        var music = MainMenu.Music;
        shelf.FocusBack();
        yield return WaitFor(() => !music.Held && !shelf.Preview.Playing, 2.5f);
        int holds0 = music.Holds;
        shelf.FocusCard(0);
        yield return WaitFor(() => shelf.Preview.Playing && shelf.PreviewId == Ids[0], 2f);
        var pv = shelf.Preview;
        var trA = pv.Current;
        Line(pv.Playing && shelf.PreviewId == Ids[0] && music.Held && music.Holds == holds0 + 1, "preview: card 0's song plays after the dwell (" + shelf.PreviewId + "), the menu music held (holds +" + (music.Holds - holds0) + ")");
        if (trA != null)
        {
            List<double> on; List<int> mid;
            string lead = NotesOn(trA, 3, out on, out mid);
            bool leadOk = Seq(on, mid, new double[] { 0, 0.5, 1, 2, 2.5, 3 }, new[] { 60, 64, 67, 60, 64, 67 });
            string bass = NotesOn(trA, 4, out on, out mid);
            bool bassOk = Seq(on, mid, new double[] { 0, 2 }, new[] { 48, 48 });   // v7 §21: a cube stays on its grid — the rider flag no longer plays the other columns
            string keys = NotesOn(trA, 0, out on, out mid);
            bool keysOk = Seq(on, mid, new double[] { 4, 5, 6, 7, 8, 9, 10, 11 }, new[] { 60, 64, 60, 64, 60, 64, 60, 64 });
            string kick = NotesOn(trA, 9, out on, out mid);
            bool kickOk = on.Count == 16; for (int i = 0; i < on.Count; i++) if (Math.Abs(on[i] - i) > 1e-3 || mid[i] != 36) kickOk = false;
            string pad = NotesOn(trA, 2, out on, out mid);
            var padSet = new HashSet<int>(mid);
            bool padOk = on.Count == 3 && padSet.SetEquals(new[] { 71, 67, 65 }) && Math.Abs(on[0]) < 1e-3;
            Line(trA.bars == 4 && Math.Abs(trA.lengthBeats - 16.0) < 1e-6 && trA.startBar == 0 && Mathf.Approximately(trA.bpm, 120f), "preview A: 4 bars = column 0, column 1 twice (×2), column 2 (" + trA.bars + " bars, " + trA.lengthBeats + " beats, " + trA.Seconds.ToString("F1") + " s, " + trA.notes.Count + " notes)");
            Line(leadOk, "preview A: the lead is the cube's path on C maj7 (tile pitches, 8th 8th quarter, looping in its window): " + lead);
            Line(bassOk, "preview A: the bass (a rider: retired by v7 §21) plays only on its own grid, C's root in half notes: " + bass);
            Line(keysOk, "preview A: the keys on A m7's first inversion (C E) sound on both passes of the ×2 column: " + keys);
            Line(kickOk, "preview A: the Moon's kick on every beat (GM 36, every bar restarting): " + (kick.Length > 60 ? kick.Substring(0, 60) + "…" : kick));
            Line(padOk, "preview A: the pad on G7 (the second lane) with its two tones below: " + pad);
            int[] ch = trA.chord;
            Line(ch != null && ch.Length == 4 && ch[0] == 60 && ch[1] == 64 && ch[2] == 67 && ch[3] == 71, "preview A: its opening chord (the leave's impact) is C maj7 (" + (ch != null ? string.Join(" ", ch) : "none") + ")");
        }
        int sched0 = pv.NotesScheduled;
        yield return new WaitForSecondsRealtime(0.2f);
        var st = MainMenu.Stage;
        Color vibeA = Vibe.ColorOf(VibeKind.Sunny);
        Line(st.HoverTint.HasValue && st.HoverTint.Value == vibeA && Mathf.Approximately(st.HoverSpread, MenuGallery.CardSpread) && st.BeatSource != null && shelf.HoverAmount > 0.5f,
             "wall: lit behind the card in the song's vibe colour (" + (st.HoverTint.HasValue ? ColorUtility.ToHtmlStringRGB(st.HoverTint.Value) : "none") + " = sunny " + ColorUtility.ToHtmlStringRGB(vibeA) + "), spread " + st.HoverSpread + ", following the preview's beat");
        int hops0 = st.SourceHops, flashes0 = st.TintFlashes, chords0 = music.ChordsScheduled, loops0 = pv.Loops;
        Synth.StartRecording(2.5f);
        yield return new WaitForSecondsRealtime(2.05f);
        int hops = st.SourceHops - hops0;
        Line(hops >= 3 && hops <= 5 && st.TintFlashes > flashes0, "wall: hopped " + hops + " times in 2 s on the preview's 120 BPM (and flashed " + (st.TintFlashes - flashes0) + " pins in its colour)");
        Line(pv.NotesScheduled > sched0 && music.ChordsScheduled == chords0, "preview: notes keep coming (" + sched0 + " → " + pv.NotesScheduled + " scheduled, owner " + MenuPreview.Owner + "), the menu pad stays held (chords " + chords0 + " → " + music.ChordsScheduled + ")");
        if (captures) { MainMenu.SimulatePointer(new Vector2(Screen.width * 0.92f, Screen.height * 0.08f)); yield return new WaitForSecondsRealtime(0.15f); Shoot("t5_shelf.png"); }
        yield return WaitFor(() => Synth.RecordingDone, 2f);
        string wav = Path.Combine(CaptureDir, "t5_preview.wav");
        if (Synth.RecordingDone) Synth.SaveRecording(wav);
        float rms, peak, act; WavStats(wav, out rms, out peak, out act);
        Line(rms > -45f && peak > -30f && peak < -0.05f && act > 0.8f, "preview sounds: Captures/t5_preview.wav RMS " + rms.ToString("F1") + " dBFS, peak " + peak.ToString("F1") + " dBFS, " + (act * 100f).ToString("F0") + "% of 50 ms windows above −50 dBFS");
        Info("preview A loops so far " + (pv.Loops - loops0) + ", dropped (late) " + pv.Dropped + ", synth " + Synth.Stats());

        // ---- 6. the pointer leaves the card: the preview stops within a beat, nothing is queued after, the menu music comes back
        shelf.SimulateEnter(0);
        yield return null;
        int stops0 = pv.Stops, rel0 = music.Releases, chordsHeld = music.ChordsScheduled; int queuedBefore = Synth.Queued;
        float left = Time.realtimeSinceStartup;
        shelf.SimulateExit(0);
        yield return WaitFor(() => !pv.Playing, 1.5f);
        float stoppedIn = Time.realtimeSinceStartup - left;
        int total0 = pv.TotalScheduled;
        yield return new WaitForSecondsRealtime(0.15f);
        int queuedAfter = Synth.Queued;
        yield return new WaitForSecondsRealtime(0.35f);
        Line(!pv.Playing && pv.Stops == stops0 + 1 && stoppedIn <= 0.5f && shelf.FocusIndex == -1,
             "leaving the card: the preview stopped " + (stoppedIn * 1000f).ToString("F0") + " ms after the pointer left (≤ one beat = 500 ms at 120 BPM), the focus went with it");
        Line(pv.TotalScheduled == total0 && queuedAfter <= 2, "leaving the card: nothing scheduled after the stop (" + total0 + " → " + pv.TotalScheduled + "), the synth's queue emptied (" + queuedBefore + " → " + queuedAfter + " events)");
        yield return WaitFor(() => !music.Held, 1.5f);
        yield return WaitFor(() => music.ChordsScheduled > chordsHeld, 2f);
        Line(!music.Held && music.Releases == rel0 + 1 && music.ChordsScheduled > chordsHeld, "the menu music is back " + (HoldRelease()).ToString("F2") + " s after the stop (held " + music.Held + ", releases +" + (music.Releases - rel0) + ", pad chords while held " + chordsHeld + " → " + music.ChordsScheduled + ")");

        // ---- 7. keyboard previews of songs B and C
        shelf.FocusCard(1);
        yield return WaitFor(() => shelf.PreviewId == Ids[1], 2f);
        var trB = pv.Current;
        Line(shelf.PreviewId == Ids[1] && trB != null && trB.bars == 6 && trB.Seconds <= 16.01 && trB.CountOn(6) == 6 && trB.CountOn(5) == 8,
             "preview B (8 bars at 90 BPM): capped at " + (trB != null ? trB.bars + " bars = " + trB.Seconds.ToString("F1") + " s, strings " + trB.CountOn(6) + " whole notes, bells " + trB.CountOn(5) + " 8ths" : "none"));
        yield return new WaitForSecondsRealtime(0.6f);
        yield return Key(KeyCode.RightArrow);
        yield return WaitFor(() => shelf.PreviewId == Ids[2], 2f);
        var trC = pv.Current;
        Line(shelf.PreviewId == Ids[2] && trC != null && trC.notes.Count > 10 && trC.bars == 6, "preview C (the v1 fixture, legacy steps): " + (trC != null ? trC.bars + " bars, " + trC.notes.Count + " notes" : "none") + "; one preview at a time (starts " + pv.Starts + ", stops " + pv.Stops + ")");
        yield return new WaitForSecondsRealtime(0.5f);

        // ---- 8. Esc returns to the words ("gallery" focused); "back" too
        yield return Key(KeyCode.Escape);
        yield return new WaitForSecondsRealtime(0.45f);
        Line(!MainMenu.ShelfOpen && !pv.Playing && MainMenu.WordsAlpha > 0.99f && shelf.Shown <= 0f && MainMenu.FocusedButton == gw,
             "Esc: back to the words (alpha " + MainMenu.WordsAlpha.ToString("F2") + "), no preview, \"gallery\" focused");
        gw.Invoke();
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.3f);
        shelf.FocusBack();
        yield return Key(KeyCode.Return);
        yield return new WaitForSecondsRealtime(0.4f);
        Line(!MainMenu.ShelfOpen && MainMenu.WordsAlpha > 0.99f, "\"back\" (focused, Enter) closes the shelf as well");

        // ---- 9. open song A over a song with cubes (the fixture): a backup, "HIT IT!", playing from bar 0, framed
        MainMenu.Hide(); yield return null;
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        yield return ShowMenu();
        int bw0 = SongIO.BackupsWritten, go0 = MainMenu.GalleryOpens;
        gw.Invoke();
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.45f);
        shelf.FocusCard(0);
        yield return null;
        float click = Time.realtimeSinceStartup;
        yield return Key(KeyCode.Return);
        bool leaving = MainMenu.State == MainMenu.Phase.Leaving;
        string word = MainMenu.Transition != null ? MainMenu.Transition.Word : "";
        bool squashed = cards.Count > 0 && cards[0] != null && cards[0].Squashed;
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
        float leaveSecs = Time.realtimeSinceStartup - click;
        double beat0 = GlobalClock.SongBeatD;
        if (captures) Shoot("t5_world.png");
        var sm = SongManager.I;
        string newest = null; DateTime newestT = DateTime.MinValue;
        for (int i = 1; i <= SongIO.BackupSlots; i++) { string p = SongIO.BackupPath(i); if (File.Exists(p) && File.GetLastWriteTimeUtc(p) > newestT) { newestT = File.GetLastWriteTimeUtc(p); newest = p; } }
        SongState backed = null; try { if (newest != null) backed = SongState.FromJson(File.ReadAllText(newest)); } catch (Exception) { }
        Line(leaving && word == "HIT IT!" && squashed && MainMenu.GalleryOpens == go0 + 1 && leaveSecs < 1.6f,
             "Enter on a card: the comic leave \"" + word + "\" from the squashed card, back in the world " + leaveSecs.ToString("F2") + " s after the key");
        Line(Gallery.CurrentId == Ids[0] && sm.Islands.Count == 4 && sm.Moons.Count == 1 && SequenceMaster.Cubes.Count == 5 && AllFinalized(),
             "opened: Gallery.CurrentId " + Gallery.CurrentId + ", " + sm.Islands.Count + " islands, " + sm.Moons.Count + " Moon, " + SequenceMaster.Cubes.Count + " cubes (all finalized " + AllFinalized() + "), " + sm.ColumnCount + " columns");
        Line(SongIO.BackupsWritten == bw0 + 1 && backed != null && backed.cubes != null && backed.cubes.Length == fixture.cubes.Length && backed.name == fixture.name,
             "the replaced song had cubes of its own: backed up once (" + (SongIO.BackupsWritten - bw0) + " → " + (newest != null ? Path.GetFileName(newest) : "none") + ", " + (backed != null && backed.cubes != null ? backed.cubes.Length : 0) + " cubes of \"" + (backed != null ? backed.name : "") + "\")");
        yield return new WaitForSecondsRealtime(0.4f);
        double beat1 = GlobalClock.SongBeatD;
        Line(GlobalClock.IsPlaying && beat0 < 1.0 && beat1 > beat0 + 0.4 && Mathf.Approximately(GlobalClock.BPM, 120f), "the song plays from bar 0 once the world is back (beat " + beat0.ToString("F2") + " → " + beat1.ToString("F2") + " 0.4 s later, " + GlobalClock.BPM + " BPM)");
        var cam = MainMenu.WorldCamera; int inView = InView(cam, -1);
        Line(cam != null && cam.enabled && inView == sm.Islands.Count && !MainMenu.IsShown && !WorldInput.IsLockedBy("menu"), "the camera frames the whole song (it fits: " + inView + "/" + sm.Islands.Count + " islands in view, distance " + (OrbitCamera.I != null ? OrbitCamera.I.Dist.ToString("F1") : "?") + " u), the world is live (" + WorldInput.Describe() + ")");
        float hud = HudAlpha();
        Line(hud < 0f || hud > 0.9f, "the HUD is back (alpha " + hud.ToString("F2") + ")");
        Line(Md5(SongIO.Path) == userSave, "the user's save is untouched (md5 " + userSave + ")");

        // ---- 9b. back to the menu: the autosave holds the gallery song, and Continue resumes it
        yield return ShowMenu();
        SongState auto = null; try { auto = SongState.FromJson(File.ReadAllText(SongIO.AutosavePath)); } catch (Exception) { }
        bool autoA = auto != null && auto.name == "t5 sun" && auto.cubes != null && auto.cubes.Length == 5;
        MainMenu.ContinueButton.Invoke();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
        yield return null;
        Line(autoA && Gallery.CurrentId == Ids[0] && sm.Islands.Count == 4 && SequenceMaster.Cubes.Count == 5 && Md5(SongIO.Path) == userSave,
             "the autosave holds the gallery song (\"" + (auto != null ? auto.name : "?") + "\", " + (auto != null && auto.cubes != null ? auto.cubes.Length : 0) + " cubes) and Continue resumes it (" + Gallery.CurrentId + ", " + sm.Islands.Count + " islands); the user's save untouched");

        // ---- 10. open song B over a fresh song (no cubes of the player's own): no backup; a click this time
        MainMenu.Hide(); yield return null;
        SongManager.I.StartFreshSong(MusicTheory.RandomSong(4242, "t5 fresh"));
        yield return null;
        yield return ShowMenu();
        int bw1 = SongIO.BackupsWritten;
        gw.Invoke();
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.45f);
        shelf.SimulateEnter(1);
        yield return null;
        shelf.Cards[1].OnPointerClick(null);
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
        yield return new WaitForSecondsRealtime(0.3f);
        Line(Gallery.CurrentId == Ids[1] && SongIO.BackupsWritten == bw1 && GlobalClock.IsPlaying && sm.Islands.Count == 8,
             "a click on card 1 over a fresh song: opened " + Gallery.CurrentId + " (" + sm.Islands.Count + " islands, playing " + GlobalClock.IsPlaying + "), no backup (the fresh song had no cubes of its own: +" + (SongIO.BackupsWritten - bw1) + ")");

        // ---- 11. song C over song B untouched: no backup (browsing the demos never rotates the player's backups); the impact frozen (capture)
        yield return ShowMenu();
        int bw2 = SongIO.BackupsWritten, skip2 = Gallery.BackupsSkipped;
        bool untouchedB = Gallery.CurrentUntouched;
        gw.Invoke();
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.45f);
        shelf.FocusCard(2);
        yield return new WaitForSecondsRealtime(0.4f);
        if (captures) MenuLeave.FreezeAt = 0.2f;
        yield return Key(KeyCode.Return);
        if (captures)
        {
            var lv = MainMenu.Transition;
            yield return WaitFor(() => lv.T >= 0.2f - 1e-4f, 2f);
            yield return null; yield return null; yield return null;
            Shoot("t5_leave.png");
            Info("t5_leave.png at leave t " + lv.T.ToString("F3") + " (drawing " + lv.DrawingNow + ", sticker " + lv.StickerShown + " \"" + lv.Word + "\")");
            yield return new WaitForSecondsRealtime(0.4f);
            MenuLeave.FreezeAt = null;
        }
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
        yield return new WaitForSecondsRealtime(0.3f);
        Line(untouchedB && Gallery.CurrentId == Ids[2] && SongIO.BackupsWritten == bw2 && Gallery.BackupsSkipped == skip2 + 1 && GlobalClock.IsPlaying,
             "song C over song B as it was opened: opened " + Gallery.CurrentId + ", no backup (+" + (SongIO.BackupsWritten - bw2) + ", skipped +" + (Gallery.BackupsSkipped - skip2) + ")");

        // ---- 11b. an edited gallery song is the player's: song A over song C with one cube changed is backed up
        yield return ShowMenu();
        foreach (var cube in SequenceMaster.Cubes) if (cube != null && !cube.IsOnMoon) { cube.volume = 0.55f; break; }
        History.Push();
        int bw3 = SongIO.BackupsWritten;
        bool touched = !Gallery.CurrentUntouched;
        gw.Invoke();
        yield return WaitFor(() => shelf.Shown >= 1f, 2f);
        yield return new WaitForSecondsRealtime(0.45f);
        shelf.FocusCard(0);
        yield return null;
        yield return Key(KeyCode.Return);
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
        yield return new WaitForSecondsRealtime(0.2f);
        Line(touched && Gallery.CurrentId == Ids[0] && SongIO.BackupsWritten == bw3 + 1, "song A over song C after an edit: the edited demo is backed up (+" + (SongIO.BackupsWritten - bw3) + ")");

        // ---- 11c. a malformed song: its card is dealt but stays silent, Enter only shakes it, the menu stays up
        {
            yield return ShowMenu();
            var broken = new GalleryEntry { id = "t5-broken", title = "broken", after = "nothing · nobody", vibe = (int)VibeKind.Stormy, bpm = 100f, bars = 4, file = "Gallery/t5-broken" };
            var okA = new GalleryEntry { id = Ids[0], title = "Sun Test", after = "Melt · ryo (supercell)", vibe = (int)VibeKind.Sunny, bpm = 120f, bars = 5, file = "Gallery/" + Ids[0] };
            Gallery.UseTestSongs(new[] { broken, okA }, new Dictionary<string, string> { { broken.file, "{\"name\":\"no islands\"}" }, { okA.file, SongA().ToJson() } });
            yield return null;
            string cur0 = Gallery.CurrentId; int islands0 = sm.Islands.Count;
            gw.Invoke();   // (the shelf opens on the loaded song's card, song A, whose preview may start: focus the broken card, then count)
            yield return WaitFor(() => shelf.Shown >= 1f, 2f);
            yield return new WaitForSecondsRealtime(0.3f);
            shelf.FocusCard(0);
            yield return null; yield return null;
            int starts0 = pv.Starts;
            yield return new WaitForSecondsRealtime(0.7f);
            var bc = shelf.Cards.Count > 0 ? shelf.Cards[0] : null;
            bool silent = !pv.Playing && pv.Starts == starts0 && bc != null && !bc.Playable && bc.map == null;
            yield return Key(KeyCode.Return);
            yield return new WaitForSecondsRealtime(0.3f);
            Line(silent && MainMenu.State == MainMenu.Phase.Shown && MainMenu.ShelfOpen && Gallery.CurrentId == cur0 && sm.Islands.Count == islands0,
                 "a malformed song: its card is dealt but silent (no preview, no map), Enter leaves the menu up and the song as it was (" + MainMenu.State + ", current \"" + Gallery.CurrentId + "\")");
            yield return Key(KeyCode.Escape);
            yield return new WaitForSecondsRealtime(0.3f);
            MainMenu.Hide();
            yield return null;
        }

        // ---- 12. the real gallery (package G's songs)
        Gallery.ClearTestSongs();
        Gallery.Reload();
        var real = Gallery.Entries;
        if (!realSongs || real.Count == 0) Info("the real gallery index has " + real.Count + " songs" + (realSongs ? " (package G's songs not landed yet): real-song checks skipped" : " (skipped by request)"));
        else
        {
            var perSong = new StringBuilder(); bool allOk = true;
            foreach (var e in real)
            {
                var s = Gallery.LoadState(e.id);
                var tr = s != null ? MenuPreview.Build(s, Mathf.Max(0, e.hook)) : null;
                int want = tr != null ? Math.Min(4, tr.songBars) : 4;
                bool ok = tr != null && tr.notes.Count > 0 && tr.bars >= want && tr.bars <= 8 && (tr.Seconds <= 16.01 || tr.bars == want);
                if (!ok) allOk = false;
                perSong.Append(e.id).Append(tr != null ? ": " + tr.bars + "/" + tr.songBars + " bars from bar " + tr.startBar + ", " + tr.Seconds.ToString("F1") + " s, " + tr.notes.Count + " notes" : ": unreadable").Append(ok ? "" : " (FAIL)").Append("; ");
            }
            Line(allOk, "real gallery: every song reads and previews 4-8 bars (≤ 16 s): " + perSong.ToString().Trim());
            yield return ShowMenu();
            gw.Invoke();
            yield return WaitFor(() => shelf.Shown >= 1f, 2f);
            yield return new WaitForSecondsRealtime(0.25f + 0.09f * real.Count);
            Line(shelf.Cards.Count == real.Count, "real gallery: one card per song (" + shelf.Cards.Count + " cards, " + real.Count + " songs, scale " + shelf.CardScale.ToString("F2") + ")");
            string heard = ""; bool allHeard = true;
            for (int i = 0; i < shelf.Cards.Count; i++)
            {
                shelf.FocusCard(i);
                int n0 = pv.TotalScheduled;
                yield return WaitFor(() => pv.Playing && pv.Id == real[i].id, 2f);
                yield return new WaitForSecondsRealtime(i == 0 ? 1.4f : 0.9f);
                bool h = pv.Id == real[i].id && pv.TotalScheduled > n0;
                if (!h) allHeard = false;
                heard += real[i].id + " +" + (pv.TotalScheduled - n0) + (h ? "" : " (silent)") + "; ";
                if (i == 0 && captures) { MainMenu.SimulatePointer(new Vector2(Screen.width * 0.92f, Screen.height * 0.08f)); yield return new WaitForSecondsRealtime(0.1f); Shoot("t5_shelf_real.png"); }
                var ct = pv.Current;
                if (ct != null) Info("real preview " + real[i].id + ": bars " + ct.startBar + "-" + (ct.startBar + ct.bars) + " of " + ct.songBars + " (" + ct.Seconds.ToString("F1") + " s at " + ct.bpm + " BPM), " + ct.notes.Count + " notes: keys " + ct.CountOn(0) + " pluck " + ct.CountOn(1) + " pad " + ct.CountOn(2) + " lead " + ct.CountOn(3) + " bass " + ct.CountOn(4) + " bells " + ct.CountOn(5) + " strings " + ct.CountOn(6) + " choir " + ct.CountOn(7) + " piano " + ct.CountOn(8) + " drums " + ct.CountOn(9) + "; opening chord " + (ct.chord != null ? string.Join(" ", ct.chord) : "-"));
                if (i == shelf.Cards.Count - 1)
                {
                    // one real preview recorded (3 s)
                    Synth.StartRecording(3f);
                    yield return WaitFor(() => Synth.RecordingDone, 4f);
                    string rw = Path.Combine(CaptureDir, "t5_preview_real.wav");
                    if (Synth.RecordingDone) Synth.SaveRecording(rw);
                    float rr, rp, ra; WavStats(rw, out rr, out rp, out ra);
                    Line(rr > -45f && rp < -0.05f && ra > 0.6f, "real preview sounds (" + real[i].id + "): Captures/t5_preview_real.wav RMS " + rr.ToString("F1") + " dBFS, peak " + rp.ToString("F1") + " dBFS, " + (ra * 100f).ToString("F0") + "% of 50 ms windows above −50 dBFS");
                }
            }
            Line(allHeard, "real gallery: each card previews its own song (" + heard.Trim() + ")");
            shelf.FocusCard(0);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Key(KeyCode.Return);
            yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Hidden, 4f);
            yield return new WaitForSecondsRealtime(0.1f);
            if (captures) Shoot("t5_world_real.png");
            yield return new WaitForSecondsRealtime(0.5f);
            var s0 = Gallery.LoadState(real[0].id);
            int cubesWant = s0 != null && s0.cubes != null ? s0.cubes.Length : -1;
            Line(Gallery.CurrentId == real[0].id && GlobalClock.IsPlaying && s0 != null && sm.Islands.Count == s0.measures.Length && SequenceMaster.Cubes.Count == cubesWant && AllFinalized(),
                 "real gallery: \"" + real[0].title + "\" opens and plays (" + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + "/" + cubesWant + " cubes, " + sm.ColumnCount + " columns, " + GlobalClock.BPM + " BPM)");
            var fr = MainMenu.LastOpenFrame; int lastCol = -1;
            for (int c = 0; c < sm.ColumnCount; c++) { var cb = sm.ColumnBounds(c); if (fr.Contains(new Vector3(cb.max.x - 0.01f, fr.center.y, cb.center.z)) || fr.max.x >= cb.max.x - 0.01f) lastCol = c; else break; }
            int wantIsl = 0; foreach (var kb in sm.Islands) if (kb != null && kb.column <= lastCol) wantIsl++;
            int seenIsl = InView(MainMenu.WorldCamera, lastCol);
            float dist = OrbitCamera.I != null ? OrbitCamera.I.Dist : 0f, lim = MainMenu.OpenFrameComfort * (OrbitCamera.I != null ? OrbitCamera.I.FovScale : 1f);
            Line(lastCol >= 0 && seenIsl == wantIsl && dist <= lim * 1.02f, "real gallery: the camera frames the song's opening, columns 0-" + lastCol + " of " + sm.ColumnCount + " (" + seenIsl + "/" + wantIsl + " of their islands in view) at " + dist.ToString("F1") + " u (≤ " + lim.ToString("F1") + ": before the fog)");
        }

        // ---- 13. audio, console, the user's save
        GlobalClock.Stop();
        yield return new WaitForSecondsRealtime(0.2f);
        Line(Synth.LateEvents == late0 && Synth.Errors == err0, "synth: late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " over the run; previews dropped " + pv.Dropped + " notes (never sent late)");
        string errs; lock (errors) errs = string.Join(" | ", errors.ToArray());
        int errCount; lock (errors) errCount = errors.Count;
        Line(errCount == 0, "console: " + errCount + " errors during the run" + (errCount > 0 ? ": " + (errs.Length > 400 ? errs.Substring(0, 400) : errs) : ""));
        Line(Md5(SongIO.Path) == userSave, "the user's save is untouched at the end (md5 " + Md5(SongIO.Path) + ")");

        // ---- restore
        Application.logMessageReceived -= OnLog;
        MenuLeave.FreezeAt = null; MenuLeave.FreezeWorld = false;
        Gallery.ClearTestSongs();
        Gallery.ClearCurrent();
        if (MainMenu.IsShown) MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        CubeInspector.CloseImmediate();
        if (Presenter.Active) Presenter.Exit();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        if (Onboarding.Active) Onboarding.Skip();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        V3Fixes.RestoreSaves(saves);
        RestorePref(Onboarding.PrefStep, prefStep); RestorePref(Onboarding.PrefDone, prefDone);
        PlayerPrefs.Save();
        Onboarding.Suppressed = suppressed;
        MainMenu.ReleasePointer();
        Finish();
    }

    static float HoldRelease() => MenuGallery.HoldRelease;

    static float HudAlpha()
    {
        if (UIManager.I == null || UIManager.I.Canvas == null) return -1f;
        var t = UIManager.I.Canvas.transform.Find("HUD");
        var g = t != null ? t.GetComponent<CanvasGroup>() : null;
        return g != null ? g.alpha : -1f;
    }

    /// <summary>Islands of the loaded song whose centre the camera sees (only those of columns ≤ <paramref name="maxCol"/> when ≥ 0).</summary>
    static int InView(Camera cam, int maxCol)
    {
        int k = 0;
        if (cam == null || SongManager.I == null) return 0;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || (maxCol >= 0 && kb.column > maxCol)) continue;
            Vector3 v = cam.WorldToViewportPoint(kb.Center);
            if (v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f) k++;
        }
        return k;
    }

    static bool AllFinalized() { foreach (var c in SequenceMaster.Cubes) if (c == null || !c.isFinalized) return false; return true; }

    static void Finish()
    {
        int pass = 0, fail = 0;
        foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Insert(0, "V5ChecksT: " + pass + " PASS, " + fail + " FAIL\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(CaptureDir); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true;
    }

    static void RestorePref(string key, int v) { if (v < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, v); }
}
