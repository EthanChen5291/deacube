using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v6 end-to-end run (integration, SPEC v6 §10 + §11): a fresh song with its deck placed → the EMPTY hand clicks a tile (it only plays, louder than
/// the playing song; nothing is created) → pick a cube up from the instrument column, draw, the cube stays in the hand, the chip puts it down →
/// copy → paste onto the next grid (adapted to its chord, the flight, one History entry, undo) → carry the grid ×2 (windows on the next two chords,
/// the bridge, the cube flies at the chord change) → a keyboard island: a melody by clicks (single keys, a repeat) → the deck's stars fit that
/// melody, every card wears its job number, a hovered card marks the swappable islands → raise a grid (the tower rises on its turn, waves under it,
/// sinks slowly after) → a sound from a group (the new cube plays it) → a second drum moon (sections; the playing moon follows the song) → present
/// → save / load round trip (keyboard, carry, voices, moon columns). Synth late / errors, console errors, the user's save. Captures i6_*.png.
/// Poll Done / Report (Captures/v6_integration_report.txt). Restores the saves and the tutorial prefs; never writes the user's save.
/// </summary>
public static class V6Integration
{
    public static bool Done = true;
    public static string Report = "";
    static StringBuilder sb;
    static int errors, pass, fail;
    static readonly List<string> errLines = new List<string>();
    static Dictionary<string, V3Fixes.FileSnap> saves;
    static readonly StringBuilder lateLog = new StringBuilder();
    static int lateMark;
    /// <summary>Late notes right after a seek WHILE PLAYING (the step under the playhead fires at once, ≤ 30 ms late by design) or a rebuild while
    /// playing (the same catch-up): counted apart from the run's own late notes.</summary>
    static int seekLate;
    static IEnumerator SeekPlaying(double beat)
    {
        int l = Synth.LateEvents;
        GlobalClock.Seek(beat);
        if (!GlobalClock.IsPlaying) GlobalClock.Play();
        for (int i = 0; i < 4; i++) yield return null;
        seekLate += Synth.LateEvents - l;
    }
    static void Mark(string section) { int l = Synth.LateEvents; if (l != lateMark) lateLog.Append(section).Append(" +").Append(l - lateMark).Append(" (last: ").Append(Synth.LastLate).Append(") "); lateMark = l; }
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);
    static SongManager SM => SongManager.I;

    public static string Run(bool captures = true)
    {
        if (!Done) return "already running";
        if (SequenceMaster.I == null || SongManager.I == null) return "no SequenceMaster";
        Done = false; Report = "";
        SequenceMaster.I.StartCoroutine(Routine(captures));
        return "started";
    }

    static void OnLog(string msg, string stack, LogType t)
    {
        if (t != LogType.Error && t != LogType.Exception && t != LogType.Assert) return;
        errors++;
        if (errLines.Count < 16) errLines.Add(t + ": " + msg + (string.IsNullOrEmpty(stack) ? "" : " @ " + stack.Split('\n')[0]));
    }

    static void Check(bool ok, string what, string detail = "")
    {
        if (ok) pass++; else fail++;
        sb.Append(ok ? "PASS " : "FAIL ").Append(what);
        if (!string.IsNullOrEmpty(detail)) sb.Append(" | ").Append(detail);
        sb.Append('\n');
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v6_integration_report.txt"), Report + "...running\n"); } catch (Exception) { }
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); Report = sb.ToString(); }

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
    static IEnumerator Until(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
    }
    /// <summary>v7 §21: the cube's body stands over island <paramref name="kb"/>'s footprint (x / z inside its visual bounds + 0.3).</summary>
    static bool OnIsland(AudioCube c, KeyBlock kb)
    {
        if (c == null || kb == null) return false;
        Vector3 q = c.transform.position; var vb = kb.VisualBounds;
        return q.x > vb.min.x - 0.3f && q.x < vb.max.x + 0.3f && q.z > vb.min.z - 0.3f && q.z < vb.max.z + 0.3f;
    }

    static IEnumerator Shot(string file, bool captures)
    {
        if (!captures) yield break;
        ScreenCapture.CaptureScreenshot(Cap(file), 1);
        yield return null; yield return null;
    }
    static string Md5(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static bool OnScreen(Vector3 s) => s.z > 0f && s.x > 60f && s.y > 110f && s.x < Screen.width - 60f && s.y < Screen.height - 110f;
    static Vector3 ScreenOf(TileInteraction t) { var cam = Camera.main; return cam != null && t != null ? cam.WorldToScreenPoint(t.Top) : Vector3.zero; }

    static IEnumerator Click(TileInteraction t)
    {
        Vector3 s = ScreenOf(t);
        PathManager.SimOnly = true; PathManager.SimPos = s;   // the frame's own pointer step stays on the clicked tile
        PathManager.I.SimPointer(s, true, true, false);
        yield return null;
        PathManager.I.SimPointer(s, false, false, true);
        yield return null;
    }

    static int FinalizedCubes() { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0) n++; return n; }
    static bool Occupied(TileInteraction t) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.nodes.Contains(t)) return true; return false; }
    static AudioCube NewestOn(KeyBlock kb)
    {
        AudioCube best = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0 && c.Island == kb && (best == null || c.id > best.id)) best = c;
        return best;
    }
    /// <summary>A free on-screen tile of <paramref name="kb"/> with <paramref name="span"/> more free tiles to its right on its row.</summary>
    static TileInteraction FreeTile(KeyBlock kb, int span)
    {
        if (kb == null) return null;
        for (int z = 0; z < kb.rows; z++)
            for (int x = 0; x + span < kb.cols; x++)
            {
                bool ok = true;
                for (int k = 0; k <= span && ok; k++) { var u = kb.GetTile(x + k, z); if (u == null || Occupied(u) || !OnScreen(ScreenOf(u))) ok = false; }
                if (ok) return kb.GetTile(x, z);
            }
        return null;
    }
    static string Midis(AudioCube c) { var l = new List<string>(); if (c != null) foreach (var n in c.nodes) l.Add(n != null ? n.midi.ToString() : "?"); return string.Join(",", l.ToArray()); }
    static string Midis(IList<TileInteraction> ts) { var l = new List<string>(); if (ts != null) foreach (var n in ts) l.Add(n != null ? n.midi.ToString() : "?"); return string.Join(",", l.ToArray()); }
    static IEnumerator Focus(KeyBlock kb)
    {
        int i = kb != null ? SM.Islands.IndexOf(kb) : -1;
        if (OrbitCamera.I != null && i >= 0) OrbitCamera.I.FocusMeasure(i, true);
        yield return Wait(1.3f);
    }

    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); errors = 0; errLines.Clear(); pass = 0; fail = 0;
        Application.logMessageReceived += OnLog;
        SongIO.QuitAutosave = false;
        saves = V3Fixes.SnapshotSaves();
        string userMd5 = Md5(SongIO.Path);
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, 0), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, 0);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        lateLog.Length = 0; lateMark = late0; seekLate = 0;
        var pm = PathManager.I;
        bool autoHand0 = PathManager.AutoHand;
        PathManager.AutoHand = false;   // the real game: an empty hand never draws
        Onboarding.Suppressed = true;
        if (MainMenu.IsShown) MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop();
        if (Clipboard.HasPattern) Clipboard.Clear();
        pm.PutDown();

        // ---- 0. a fresh song, its deck placed (4+ columns)
        SM.StartFreshSong(MusicTheory.RandomSong(606));
        yield return Wait(1.0f);
        var tray = IslandTray.Ensure();
        if (tray != null) tray.SimTool(2);   // the wand: the whole remaining progression
        yield return Wait(1.4f);
        Check(SM.ColumnCount >= 4 && SM.Moons.Count >= 1, "a fresh song with its progression placed", "columns " + SM.ColumnCount + ", moons " + SM.Moons.Count + ", key " + SM.SongKey);
        var a0 = SM.AnchorOf(0);
        yield return Focus(a0);

        // ---- 1. the empty hand: a click on a tile only plays it; while the song plays the press is louder than the song
        var t0 = FreeTile(a0, 3);
        if (t0 == null) { Check(false, "a free tile on screen", "island 0"); Finish(userMd5, prefStep, prefDone, autoHand0); yield break; }
        int cubes0 = FinalizedCubes(), h0 = History.UndoCount, p0 = PathManager.PressCount, sp0 = Synth.PressCount;
        yield return Click(t0);
        yield return Wait(0.3f);
        Check(pm.Hand == PathManager.HandKind.Empty && FinalizedCubes() == cubes0 && History.UndoCount == h0 && !pm.IsDrawing && PathManager.PressCount == p0 + 1 && Synth.PressCount > sp0,
              "the empty hand: clicking a tile plays it and places nothing", "cubes " + cubes0 + " → " + FinalizedCubes() + ", history " + h0 + " → " + History.UndoCount + ", presses +" + (PathManager.PressCount - p0) + ", synth presses +" + (Synth.PressCount - sp0));
        yield return SeekPlaying(0);
        yield return Wait(1.2f);
        Synth.ResetDuckMin();
        yield return Click(t0);
        yield return Wait(0.35f);
        float duck = Synth.DuckMin, boost = Synth.BoostMax;
        Check(duck <= 0.2f && boost >= 1.5f, "while the song plays, a pressed tile is louder than everything else (the song ducks, the press is boosted)",
              "song gain " + duck.ToString("F3") + " (" + (20f * Mathf.Log10(Mathf.Max(1e-4f, duck))).ToString("F1") + " dB), press boost " + (20f * Mathf.Log10(Mathf.Max(1e-4f, boost))).ToString("F1") + " dB");
        GlobalClock.Stop();
        yield return Wait(0.4f);
        Mark("press");

        // ---- 2. pick a cube up from the instrument column: clicks draw, the cube stays in the hand, the chip puts it down
        HudInstruments.ToggleHand(0, -1, true);
        yield return null;
        Check(pm.Hand == PathManager.HandKind.Cube && pm.selectedInstrument == 0, "clicking the keys chip puts a keys cube in the hand", "hand " + pm.Hand);
        int hd = History.UndoCount, cb = FinalizedCubes();
        var end = a0.GetTile(t0.gridX + 3, t0.gridZ);
        yield return Click(t0);
        yield return Click(end);
        yield return Click(end);   // the end tile again finishes
        yield return Wait(0.9f);
        var drawn = NewestOn(a0);
        Check(FinalizedCubes() == cb + 1 && History.UndoCount == hd + 1 && drawn != null && drawn.nodes.Count == 4 && pm.Hand == PathManager.HandKind.Cube,
              "with a cube in the hand clicks draw a path, and the cube stays in the hand", "nodes " + (drawn != null ? drawn.nodes.Count : 0) + ", history +" + (History.UndoCount - hd) + ", hand " + pm.Hand);
        yield return Shot("i6_01_drawn.png", captures);
        HudInstruments.ToggleHand(0, -1, true);
        yield return null;
        Check(pm.Hand == PathManager.HandKind.Empty, "clicking the held chip again puts the cube down", "hand " + pm.Hand);
        FocusLoop.Dismiss();
        Mark("hand");

        // ---- 3. copy → paste onto the next grid: adapted to its chord, the flight, one History entry, undo
        bool copied = drawn != null && Clipboard.Copy(drawn);
        var next = Clipboard.NextGrid(a0);
        var expect = next != null ? Clipboard.AdaptedFor(next) : null;
        int hp = History.UndoCount, cp = FinalizedCubes();
        var pasted = copied ? Clipboard.PasteNext() : null;
        // v7 §21: a paste no longer flies a cube across — it APPEARS on its grid; the pasted cube never leaves the target's footprint
        bool pasteStayed = true;
        float pEnd = Time.unscaledTime + Clipboard.FlightSeconds * 0.4f;
        while (Time.unscaledTime < pEnd) { if (!OnIsland(pasted, next)) pasteStayed = false; yield return null; }
        yield return Shot("i6_02_paste_appear.png", captures);
        pEnd = Time.unscaledTime + Clipboard.FlightSeconds + 0.5f;
        while (Time.unscaledTime < pEnd) { if (!OnIsland(pasted, next)) pasteStayed = false; yield return null; }
        bool sameTiles = pasted != null && expect != null && pasted.nodes.Count == expect.xs.Length;
        if (sameTiles) for (int i = 0; i < pasted.nodes.Count; i++) if (pasted.nodes[i] == null || pasted.nodes[i].gridX != expect.xs[i] || pasted.nodes[i].gridZ != expect.zs[i]) sameTiles = false;
        float move = 0f;
        if (pasted != null && drawn != null && pasted.nodes.Count == drawn.nodes.Count) { for (int i = 0; i < pasted.nodes.Count; i++) move += Mathf.Abs(pasted.nodes[i].midi - drawn.nodes[i].midi); move /= Mathf.Max(1, pasted.nodes.Count); }
        Check(copied && next != null && pasted != null && pasted.Island == next && next.column == a0.column + 1 && FinalizedCubes() == cp + 1 && History.UndoCount == hp + 1 && pasteStayed,
              "paste → next grid: the pattern appears on the next chord's grid without flying across (v7 §21; one History entry)", "next column " + (next != null ? next.column : -1) + ", " + (next != null ? next.assignedChord : "") + ", history +" + (History.UndoCount - hp));
        Check(sameTiles && move <= 5f, "the pasted notes are the pattern adapted to that chord (its shape, voice-led: close in pitch)",
              "source " + Midis(drawn) + " (" + a0.assignedChord + ") → " + Midis(pasted) + " (" + (next != null ? next.assignedChord : "") + "), mean move " + move.ToString("F1") + " semitones");
        History.Undo();
        yield return Wait(0.7f);
        Check(FinalizedCubes() == cp, "undo takes the pasted cube away", "cubes " + FinalizedCubes());
        Clipboard.Clear();
        Mark("paste");

        // ---- 4. carry the grid ×2: windows on the next two chords (adapted), the bridge, the cube flies at the chord change
        a0 = SM.AnchorOf(0);
        int i0 = SM.Islands.IndexOf(a0);
        SM.SetCarry(i0, 2);
        yield return Wait(1.4f);
        a0 = SM.AnchorOf(0);
        var targets = SM.CarryTargets(SM.Islands.IndexOf(a0));
        var carrier = NewestOn(a0);
        int carriedW = 0, expectW = 0;
        if (carrier != null) foreach (var w in carrier.windows) if (w.carried) carriedW++;
        foreach (var tk in targets) expectW += SM.ColumnPasses(tk.column);
        var bridge = WorldMagic.I != null ? WorldMagic.I.BridgeOf(a0) : null;
        Check(a0.carry == 2 && targets.Count == 2 && carrier != null && carriedW == expectW && expectW >= 2, "carry ×2: the grid's pattern also plays on the next two chords",
              "targets " + targets.Count + ", carried windows " + carriedW + "/" + expectW);
        Check(bridge == null, "v7 §21: no carry bridge — no magic arcs (the carried grids join into one long grid instead)", bridge != null ? "arcs " + bridge.HopCount : "no bridge");
        if (carrier != null && targets.Count > 0)
        {
            var on1 = carrier.TilesOn(targets[0]);
            bool tilesOf1 = on1.Count == carrier.nodes.Count; foreach (var t in on1) if (t == null || t.island != targets[0]) tilesOf1 = false;
            Check(tilesOf1, "on the next chord it plays the same pattern adapted to that grid", Midis(carrier) + " → " + Midis(on1) + " (" + targets[0].assignedChord + ")");
            yield return Focus(targets[0]);
            float c1 = SM.ColumnStart(targets[0].column);
            yield return SeekPlaying(Mathf.Max(0f, c1 - 2f));
            // v7 §21: across the chord change the carrier walks on along the long grid (a0 + its targets): never flying, never off it
            bool flying = false, offGrid = false, shot = false;
            int cross0 = AudioCube.CrossCount;
            float tUntil = Time.unscaledTime + 12f;
            while (Time.unscaledTime < tUntil && GlobalClock.SongBeat < c1 + 1.0f)
            {
                if (carrier.Flying) flying = true;
                bool on = OnIsland(carrier, a0); foreach (var tk in targets) on |= OnIsland(carrier, tk);
                if (!on) offGrid = true;
                if (!shot && GlobalClock.SongBeat >= c1 - 0.25f) { shot = true; yield return Shot("i6_03_carry_walk.png", captures); continue; }
                yield return null;
            }
            bool onT1 = OnIsland(carrier, targets[0]);
            int crossed = AudioCube.CrossCount - cross0;
            Check(!flying && !offGrid && onT1 && crossed >= 1, "playing (v7 §21): at the chord change the cube steps on along the long grid and plays on the next measure — it never flies or leaves the grid",
                  "flew " + flying + ", off the long grid " + offGrid + ", on the next measure after the change " + onT1 + ", measure crossings " + crossed);
            GlobalClock.Stop();
            yield return Wait(0.8f);
        }
        Mark("carry");

        // ---- 5. a keyboard island: a melody by clicks; the stars on the cards fit it; job numbers; swaps
        var anchor1 = SM.AnchorOf(1);
        int kIdx = SM.AddKeyboardIsland(SM.Islands.IndexOf(anchor1), false);
        yield return Wait(1.2f);
        var kbd = kIdx >= 0 && kIdx < SM.Islands.Count ? SM.Islands[kIdx] : null;
        Check(kbd != null && kbd.IsKeyboard && kbd.cols == ProjectConfig.KeyboardKeys && kbd.rows == 1 && kbd.column == SM.AnchorOf(1).column,
              "a keyboard island: one row of 25 piano keys in the second part of the song", kbd != null ? "column " + kbd.column + ", keys " + kbd.cols + "×" + kbd.rows + ", lowest " + (kbd.GetTile(0, 0) != null ? kbd.GetTile(0, 0).midi : -1) : "none");
        if (kbd != null)
        {
            yield return Focus(kbd);
            pm.PickUpCube(3);   // lead
            pm.SetBrush(12);    // v7 §19.1: a path holds as many beats as its grid — five eighths fit the one-measure keyboard (five quarters would not)
            var keys = new[] { 2, 6, 9, 9, 14 };   // v7 §20.1: not the end keys (a note there adds an octave on that side and the keys shift)
            int onScr = 0; foreach (int k in keys) if (OnScreen(ScreenOf(kbd.GetTile(k, 0)))) onScr++;
            var pk = pm.PickAt(ScreenOf(kbd.GetTile(keys[0], 0)));
            Info("keyboard keys on screen " + onScr + "/" + keys.Length + ", first key pick: rule " + pk.rule + (pk.tile != null ? " tile " + pk.tile.name : " no tile") + (pk.cube != null ? " cube" : "") + ", hand " + pm.Hand);
            bool first = true;
            foreach (int k in keys)
            {
                yield return Click(kbd.GetTile(k, 0));
                if (first) { Info("after the first key: drawing " + pm.IsDrawing + ", draft nodes " + (pm.Draft != null ? pm.Draft.nodes.Count : -1)); first = false; }
            }
            pm.FinishPath();
            yield return Wait(0.8f);
            // a melody longer than its island grows the column (PathManager.FitColumnToCube): the islands were rebuilt — find the keyboard again
            KeyBlock rebuilt = null;
            foreach (var kb in SM.Islands) if (kb != null && kb.IsKeyboard) { rebuilt = kb; break; }
            if (rebuilt != null) kbd = rebuilt;
            var mel = NewestOn(kbd);
            int lo = kbd.GetTile(0, 0) != null ? kbd.GetTile(0, 0).midi : 0;
            bool shape = mel != null && mel.nodes.Count == 5;
            if (shape) for (int i = 0; i < 5; i++) if (mel.nodes[i] == null || mel.nodes[i].midi != lo + keys[i]) shape = false;
            Check(shape, "clicks on the keys add exactly those notes — a leap, a repeated note — not the keys in between", "melody " + Midis(mel));
            yield return Shot("i6_04_keyboard_melody.png", captures);
            pm.PutDown();
            FocusLoop.Dismiss();
            // the deck: stars for the cards that fit the melody, every card's job number, a hovered card marks its swappable islands
            IslandHeader.Show(kbd);
            yield return Wait(0.5f);
            IslandTray.Open();
            yield return Wait(1.0f);
            if (tray != null) tray.RefreshStarsNow();
            yield return null; yield return null;
            var midi = new List<int>(); var wts = new List<float>();
            Harmony.NotesOfIsland(kbd, midi, wts);
            int cards = 0, jobsOk = 0, starsOk = 0, starred = 0, judged = 0;
            if (tray != null)
                foreach (var c in tray.Cards)
                {
                    if (c == null || c.data == null || c.data.kind != 0) continue;
                    cards++;
                    if (c.job == Harmony.Job(c.data)) jobsOk++;
                    float f = Harmony.Fit(midi, wts, c.data);
                    if (Mathf.Abs(f - Harmony.StarThreshold) < 0.02f) continue;   // on the line: either is fine
                    judged++;
                    if (c.starred == (f >= Harmony.StarThreshold)) starsOk++;
                    if (c.starred) starred++;
                }
            Check(tray != null && tray.StarRef == IslandTray.StarSource.Island && tray.StarIsland == kbd && judged > 0 && starsOk == judged && starred > 0,
                  "the deck's stars mark the chord cards that fit the keyboard's melody", tray != null ? "reference " + tray.StarRef + ", stars " + starred + ", agree " + starsOk + "/" + judged + " (melody " + string.Join(",", midi.ConvertAll(m => m.ToString()).ToArray()) + ")" : "no tray");
            Check(cards > 0 && jobsOk == cards, "every chord card wears its job number (1 home, 2 away, 3 heart, 4 pull)", jobsOk + "/" + cards);
            yield return Shot("i6_05_deck_stars.png", captures);
            // the swap chips stand over the same-job islands that are on screen (the deck tracks them all)
            int swapCard = -1, want = 0, total = 0;
            var camS = Camera.main;
            if (tray != null && camS != null)
                for (int i = 0; i < tray.Cards.Count && swapCard < 0; i++)
                {
                    var c = tray.Cards[i]; if (c == null || c.data == null || c.job <= 0) continue;
                    int same = 0, seen = 0;
                    foreach (var kb in SM.Islands)
                    {
                        if (kb == null || kb.IsMoon || kb.IsKeyboard || Harmony.Job(kb) != c.job) continue;
                        same++;
                        var sp = camS.WorldToScreenPoint(kb.VisualCenter + Vector3.up * 1.3f);
                        if (sp.z > 0f && sp.x >= 0f && sp.y >= 0f && sp.x <= Screen.width && sp.y <= Screen.height) seen++;
                    }
                    if (seen > 0) { swapCard = i; want = seen; total = same; }
                }
            if (swapCard >= 0)
            {
                tray.SimSwapHover(swapCard);
                yield return Wait(0.5f);
                Check(tray.SwapJob == tray.Cards[swapCard].job && tray.SwapIslands.Count == total && tray.SwapChipsShown == want, "hovering a card marks the islands with the same number: they can swap",
                      "job " + tray.SwapJob + ", swap chips " + tray.SwapChipsShown + "/" + want + " on screen, islands " + tray.SwapIslands.Count + "/" + total);
                yield return Shot("i6_06_swap_hint.png", captures);
                tray.SimSwapHover(-1);
            }
            else Check(false, "a card whose number matches an island", "none");
            IslandTray.Close(); IslandHeader.Hide();
            yield return Wait(0.5f);
            int badgeOk = 0, badgeN = 0;
            foreach (var kb in SM.Islands) { if (kb == null) continue; badgeN++; if (kb.JobShown == (kb.IsKeyboard ? 0 : Harmony.Job(kb))) badgeOk++; }
            Check(badgeN > 0 && badgeOk == badgeN, "the grids wear their job numbers too (keyboards none)", badgeOk + "/" + badgeN);
        }
        Mark("keyboard");

        // ---- 6. raise a grid: it rises like a tower on its turn (waves under it), sinks slowly after
        int tc = SM.ColumnCount > 2 ? 2 : SM.ColumnCount - 1;
        var tIsl = SM.AnchorOf(tc);
        int ti = SM.Islands.IndexOf(tIsl);
        SM.SetRegister(ti, 1);
        yield return Wait(3.4f);   // the stopped preview: up and slowly back down
        tIsl = SM.Islands[ti];
        float rest = tIsl.TowerLift;
        yield return Focus(tIsl);
        float cs = SM.ColumnStart(tc), ce = cs + SM.ColumnLength(tc);
        yield return SeekPlaying(Mathf.Max(0f, cs - 2f));
        yield return Until(() => GlobalClock.SongBeat >= cs + 1.0f, 12f);
        float up = tIsl.TowerLift;
        var tower = WorldMagic.I != null ? WorldMagic.I.TowerOf(tIsl) : null;
        float pillarGap = tower != null ? Mathf.Abs(tower.PillarTop - tIsl.UndersideY) : 99f;
        int rings = tower != null ? tower.TotalRings : 0;
        yield return Shot("i6_07_tower.png", captures);
        yield return Until(() => GlobalClock.SongBeat >= ce + 1.2f || GlobalClock.SongBeat < cs - 3f, 20f);
        float sinking = tIsl.TowerLift;
        yield return Until(() => GlobalClock.SongBeat >= ce + 3.6f || GlobalClock.SongBeat < cs - 3f, 8f);
        float after = tIsl.TowerLift;
        GlobalClock.Stop();
        // v7 §12 (the user: "towers only go back after the song resets"): it stays up after its turn; the slow sink after the loop is V7Integration's
        Check(Mathf.Abs(rest - SongManager.TowerRestOf(1)) < 0.08f && up > 0.8f * SongManager.TowerTopOf(1) && sinking > up - 0.2f && after > up - 0.2f,
              "raising a grid an octave: it rises like a tower on its turn and stays up after it (v7 §12: back only when the song loops)", "rest " + rest.ToString("F2") + ", on its turn " + up.ToString("F2") + ", 1.2 beats after " + sinking.ToString("F2") + ", 3.6 beats after " + after.ToString("F2"));
        Check(tower != null && pillarGap < 0.25f && rings > 0, "a pillar holds it up out of the sea and waves ring out from under it", tower != null ? "pillar top vs underside " + pillarGap.ToString("F2") + ", rings " + rings : "no tower");
        yield return Wait(0.6f);
        Mark("tower");

        // ---- 7. a sound from a group: the new cube plays it
        int grp = 1, voice = Mathf.Min(4, Instruments.VoiceCount(grp) - 1);
        HudInstruments.ToggleHand(grp, voice, false);
        yield return null;
        var a3 = SM.AnchorOf(SM.ColumnCount - 1);
        yield return Focus(a3);
        var v0 = FreeTile(a3, 1);
        if (v0 != null)
        {
            yield return Click(v0);
            var v1 = a3.GetTile(v0.gridX + 1, v0.gridZ);
            yield return Click(v1);
            yield return Click(v1);
            yield return Wait(0.8f);
        }
        var vc = NewestOn(a3);
        Check(voice > 0 && vc != null && vc.instrument == grp && vc.voice == voice && Instruments.SlotOf(grp, voice) != Instruments.SlotOf(grp, 0),
              "picking a sound from the pluck group: the new cube plays that sound", "voice " + voice + " = \"" + Instruments.VoiceName(grp, voice) + "\" (" + Instruments.VoiceCount(grp) + " in the group), slot " + Instruments.SlotOf(grp, voice));
        pm.PutDown(); FocusLoop.Dismiss();
        Mark("voice");

        // ---- 8. §11: a second drum moon from part 3 on; the first stops there; the playing moon follows the song
        int m1 = SM.AddMoon(Mathf.Min(2, SM.ColumnCount - 1));
        yield return Wait(1.2f);
        float s0, e0, s1 = -1f, e1 = -1f;
        bool sec0 = SM.MoonSection(0, out s0, out e0), sec1 = false;
        if (m1 >= 0) sec1 = SM.MoonSection(m1, out s1, out e1);
        float split = SM.ColumnStart(Mathf.Min(2, SM.ColumnCount - 1));
        Check(m1 >= 1 && sec0 && sec1 && Mathf.Abs(s0) < 0.01f && Mathf.Abs(e0 - split) < 0.01f && Mathf.Abs(s1 - split) < 0.01f && Mathf.Abs(e1 - GlobalClock.TotalBeats) < 0.01f,
              "a drum moon placed at part 3: the first moon plays until there, the new one from there to the end", "moon 0 " + s0 + ".." + e0 + ", moon " + m1 + " " + s1 + ".." + e1 + " of " + GlobalClock.TotalBeats);
        var rail = HudColumnRail.I;
        Check(rail != null && rail.DrumSectionCount == 2, "the column rail shows the two drum sections", rail != null ? "sections " + rail.DrumSectionCount : "no rail");
        if (SM.Moons.Count > 0)
        {
            yield return SeekPlaying(SM.ColumnStart(1) + 1.0f);
            yield return Wait(1.0f);
            var moon = SM.Moons[0];
            float dx = Mathf.Abs(moon.VisualCenter.x - SM.ColumnBounds(1).center.x);
            yield return Shot("i6_08_moon_follow.png", captures);
            Check(dx < 1.5f, "while its section plays, the drum moon follows the song (in line with the part playing)", "moon x " + moon.VisualCenter.x.ToString("F1") + " vs part 2 x " + SM.ColumnBounds(1).center.x.ToString("F1"));
            GlobalClock.Stop();
            yield return Wait(0.8f);
        }
        Mark("moons");

        // ---- 9. present: the thrown-cube presentation plays the new song
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Presenter.Enter();
        yield return Until(() => Presenter.Active, 3f);
        yield return Wait(3.0f);
        var sea = Presenter.Sea;
        yield return Shot("i6_09_present.png", captures);
        int risers = 0, islandsAndMoons = SongManager.I.Islands.Count + SongManager.I.Moons.Count;
        if (sea != null) { for (int i = 0; i < sea.Count; i++) if (sea[i].kind != PresentSea.Kind.Floor) risers++; risers += sea.StagedCount; }
        Check(Presenter.Active && sea != null && GlobalClock.IsPlaying && risers == islandsAndMoons, "present plays the new song (keyboard, carry, moons): every island and Moon rises from the sea with its column", "risers " + risers + " of " + islandsAndMoons + " islands and Moons, " + (sea != null ? sea.StagedCount : 0) + " keyboards left to KeyStage");
        Presenter.Exit();
        yield return Until(() => !Presenter.Active, 4f);
        yield return Wait(0.6f);
        Mark("present");

        // ---- 10. save / load round trip: keyboard (kind 2), carry, voices, moon columns
        string path = Cap("v6_integration_song.json");
        var b4 = SongState.Capture();
        bool saved = SongIO.SaveTo(path);
        bool loaded = saved && SongIO.LoadFrom(path);
        yield return Wait(1.2f);
        var af = SongState.Capture();
        Func<SongState, string> sig = st =>
        {
            int k2 = 0, carry = 0, voices = 0; var mc = new List<string>();
            foreach (var m in st.measures) if (m != null) { if (m.kind == 2) k2++; carry += m.carry; }
            foreach (var c in st.cubes) if (c != null) voices += c.voice;
            if (st.moons != null) foreach (var m in st.moons) if (m != null) mc.Add(m.col.ToString());
            return "keyboards " + k2 + ", carry " + carry + ", voice sum " + voices + ", moon columns [" + string.Join(",", mc.ToArray()) + "], islands " + st.measures.Length + ", cubes " + st.cubes.Length;
        };
        Check(saved && loaded && sig(b4) == sig(af), "save / load keeps the keyboard, the carry, the sounds and the drum sections", sig(af));
        try { File.Delete(path); } catch (Exception) { }

        if (captures) Info("synth over the run (captures on: each screenshot stalls the main thread): " + Synth.Stats() + " | seek catch-ups " + seekLate + " | late by section: " + (lateLog.Length > 0 ? lateLog.ToString() : "none"));
        else Check(Synth.LateEvents - seekLate == late0, "synth: no late notes over the whole run (the note under a seek while playing is fired at once: counted apart)",
                   Synth.Stats() + " | seek catch-ups " + seekLate + " | late by section: " + (lateLog.Length > 0 ? lateLog.ToString() : "none"));
        Check(Synth.Errors == err0, "synth: no errors over the whole run", Synth.Stats());
        Finish(userMd5, prefStep, prefDone, autoHand0);
    }

    static void Finish(string userMd5, int prefStep, int prefDone, bool autoHand0)
    {
        Application.logMessageReceived -= OnLog;
        try
        {
            PathManager.SimOnly = false; CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit(); FocusLoop.Dismiss(); GlobalClock.Stop();
            if (IslandTray.IsOpen) IslandTray.Close(); IslandHeader.Hide(); if (PathManager.I != null) PathManager.I.PutDown(); if (Clipboard.HasPattern) Clipboard.Clear();
        }
        catch (Exception) { }
        PathManager.AutoHand = autoHand0;
        Onboarding.Suppressed = false;
        V3Fixes.RestoreSaves(saves);
        PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep); PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone); PlayerPrefs.Save();
        Check(Md5(SongIO.Path) == userMd5, "the user's save is untouched", userMd5);
        Check(errors == 0, "no console errors or exceptions during the run", errors + (errLines.Count > 0 ? ": " + string.Join(" || ", errLines) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v6_integration_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }
}
