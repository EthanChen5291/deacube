using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package K (v6: the keyboard island, carry windows, the octave tower, job badges on platforms) Play-mode verification, SPEC v6 §3.5:
/// <c>V6ChecksK.RunAll()</c> starts a coroutine; poll <c>V6ChecksK.Done</c> or Captures/k6_report.txt (numbered PASS / FAIL lines, INFO lines with
/// numbers). Covers the keyboard build (25 keys from the key's tonic, white / black counts, real key geometry, the island's width, the tonic's
/// houses, off-scale keys dimmer, the chord-tone dots following the column's chord), AddKeyboardIsland (in a column, as a new column, a full
/// column), keyboards through every op (duplicate, register, merge refused, the free drag's commit, the tray ghost, undo / redo), the save / load
/// round trip (kind 2, carry), the carry windows (count = own passes + Σ target passes, order, hops, the silent rules, the chain's end, families,
/// = CarryTargets, one History entry + OnCarryChanged), the tower (rest / rising / top with the bob / sinking / rest again, a lowered dip, pure,
/// the loop wrap, the stopped preview, the shown lift following while playing and gliding after a seek), the job badges (Harmony.Job, a chord
/// change, sleep, the home sticker), Synth late / errors while playing a carrying, towering song. Captures k6_*.png. Runs with
/// PathManager.AutoHand = false (the real game). Never writes the user's save; reloads the fixture at the end.
/// <c>V6ChecksK.RunBattery(true)</c> then runs K's older suites (V5ChecksK, V4ChecksK, V3ChecksB) with AutoHand = true (Captures/k6_battery.txt).
/// </summary>
public static class V6ChecksK
{
    public static string Report = "";
    public static bool Done;
    static int num;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "k6_report.txt");
    public static string ScratchSave => Path.Combine(Application.temporaryCachePath, "v6checksk_roundtrip.json");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" K6-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, sb.ToString() + (Done ? "" : "...running\n")); } catch (Exception) { } }

    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); Line(sb, d == null, name, d ?? "ok"); }
        catch (Exception e) { Line(sb, false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }

    static string FirstFrame(Exception e)
    {
        var s = e.StackTrace ?? "";
        int nl = s.IndexOf('\n');
        return (nl > 0 ? s.Substring(0, nl) : s).Trim();
    }

    static SongManager SM => SongManager.I;
    static string F(float v) => v.ToString("F3");
    static string F(double v) => v.ToString("F3");

    public static string RunAll()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(AllRoutine());
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void Restore()
    {
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    static IEnumerator Shot(string file)
    {
        yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator UntilBeat(double b, float timeout = 8f) { float t0 = Time.realtimeSinceStartup; while (GlobalClock.SongBeatD < b && Time.realtimeSinceStartup - t0 < timeout) yield return null; }

    static void Frame(Bounds b, float tight) { if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(b, 0.1f, true, tight); }

    static CubeState CubeOn(int island, int instrument, int[] xs, int[] zs, int step, int hits = -1)
    {
        return new CubeState { instrument = instrument, measure = island, moon = -1, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = 1, mode = 0, volume = 1f, hits = hits, twinOf = -1 };
    }

    static List<AudioCube> CubesOf(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && !c.rider && !c.IsOnMoon && c.Island == kb).ToList();
    static string Kinds() { var s = new StringBuilder(); foreach (var kb in SM.Islands) s.Append(kb.kind).Append('@').Append(kb.column).Append('c').Append(kb.carry).Append(' '); return s.ToString().Trim(); }
    static int Pc(int m) => ((m % 12) + 12) % 12;

    /// <summary>The column invariants (v4 / v5): column-major and contiguous, anchors first, a shared x per column, LaneGap clearance, shared starts.</summary>
    static string Invariants()
    {
        var sm = SM;
        int prev = -1;
        for (int i = 0; i < sm.Islands.Count; i++)
        {
            var kb = sm.Islands[i];
            if (kb.column < prev || kb.column > prev + 1) return "not column-major / contiguous at " + i;
            if (kb.IsAnchor != (kb.column != prev)) return "anchor flag at " + i;
            prev = kb.column;
        }
        for (int c = 0; c < sm.ColumnCount; c++)
        {
            var l = sm.ColumnIslands(c).OrderBy(k => k.FrontEdge).ToList();
            foreach (var kb in l) if (Mathf.Abs(kb.px - l[0].px) > 1e-3f) return "column " + c + " x differs";
            for (int k = 0; k + 1 < l.Count; k++) if (l[k + 1].FrontEdge - l[k].BackEdge < ProjectConfig.LaneGap - 0.01f) return "column " + c + " clearance";
            foreach (var kb in l) if (Mathf.Abs(sm.MeasureStarts[sm.Islands.IndexOf(kb)] - sm.ColumnStart(c)) > 1e-4f) return "start in column " + c;
        }
        return null;
    }

    /// <summary>The test song: four one-island columns (bars 1) at 120 BPM, looping, one resident cube (8ths) on every island.</summary>
    static void BuildSong(int seed)
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        if (FocusLoop.Active) FocusLoop.Dismiss();
        var song = MusicTheory.RandomSong(seed, "k6 song");
        foreach (var m in song.measures) m.bars = 1;
        SM.StartFreshSong(song);
        var cards = new List<SongManager.MeasureData>();
        for (int i = 1; cards.Count < 3; i++) cards.Add(song.measures[i % song.measures.Length]);
        SM.PlaceAll(cards);
        Performance.SetNominalBpm(120f);
        GlobalClock.LoopSong = true;
        var pm = PathManager.I;
        int[] inst = { 0, 1, 5, 8 };
        for (int i = 0; i < SM.Islands.Count; i++)
        {
            int rr = Mathf.Max(1, SM.Islands[i].rows);
            pm.RestoreCube(CubeOn(i, inst[i % inst.Length], new[] { i % 3, (i % 3) + 1, (i % 3) + 2 }, new[] { 0, Mathf.Min(1, rr - 1), Mathf.Min(2, rr - 1) }, (int)StepLen.Eighth, 5));
        }
        SM.RecomputeMeasureStarts();
        History.Reset(); History.Push();
    }

    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        Prepare();
        yield return null;
        string userMd5 = Md5(SongIO.Path);

        // ================================================================ A. the keyboard island
        BuildSong(6606);
        yield return null;
        var key = SM.SongKey;
        Info(sb, "song: " + string.Join(" ", SM.Islands.Select(k => k.assignedChord + "@" + k.column)) + " | key " + key + " | lowest key " + SongManager.KeyboardLowestKey());
        int kIdx = -1;
        Run(sb, "AddKeyboardIsland(column 1's island, above): a keyboard (kind 2) joins column 1 LaneGap above it, the chord island stays the anchor, one History entry, it rises from the sea", () =>
        {
            int h = History.UndoCount, a1 = SM.ColumnFirst(1); var anchor = SM.Islands[a1]; string chord = anchor.assignedChord;
            kIdx = SM.AddKeyboardIsland(a1, true);
            if (kIdx < 0) return "refused";
            var kb = SM.Islands[kIdx];
            if (!kb.IsKeyboard || kb.kind != 2) return "kind " + kb.kind;
            if (kb.column != 1 || kb.IsAnchor || SM.ColumnSize(1) != 2 || SM.AnchorOf(1).assignedChord != chord) return "column " + kb.column + " anchor " + kb.IsAnchor + " size " + SM.ColumnSize(1);
            if (kb.FrontEdge < SM.AnchorOf(1).BackEdge + ProjectConfig.LaneGap - 0.01f) return "not above: front " + F(kb.FrontEdge) + " anchor back " + F(SM.AnchorOf(1).BackEdge);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (!kb.Rising) return "no rise-in";
            return Invariants();
        });
        yield return Wait(0.8f);
        KeyBlock K() => kIdx >= 0 && kIdx < SM.Islands.Count ? SM.Islands[kIdx] : null;
        Run(sb, "keyboard build: 25 keys in one row, tile.midi = the lowest key (the key's tonic voiced into 53..64) + index, 15 white / 10 black keys from a white tonic (14 / 11 from a black one)", () =>
        {
            var kb = K(); if (kb == null) return "no keyboard";
            int low = kb.chordRootMIDI;
            if (low < 53 || low > 64 || Pc(low) != key.tonic || low != SongManager.KeyboardLowestKey()) return "lowest " + low + " tonic " + key.tonic;
            if (kb.cols != ProjectConfig.KeyboardKeys || kb.rows != 1 || kb.tiles.Count != ProjectConfig.KeyboardKeys) return "cols " + kb.cols + " rows " + kb.rows + " tiles " + kb.tiles.Count;
            int white = 0;
            for (int k = 0; k < kb.cols; k++)
            {
                var t = kb.GetTile(k, 0);
                if (t == null || t.midi != low + k || t.gridX != k || t.gridZ != 0) return "key " + k + " midi " + (t != null ? t.midi : -1);
                if (!KeyBlock.IsBlackKey(t)) white++;
            }
            int want = KeyBlock.IsBlackKeyMidi(low) ? 14 : 15;
            if (white != want || white != KeyBlock.WhiteKeysFrom(low)) return "white " + white + " want " + want;
            return null;
        });
        Run(sb, "keyboard geometry: IslandWidth wide and DepthOf(1) deep (the column's x), keys inside the case, white keys left to right, black keys raised and set back, a gentle rise to the right", () =>
        {
            var kb = K(); if (kb == null) return "no keyboard";
            if (Mathf.Abs(kb.Width - KeyBlock.IslandWidth) > 1e-4f || Mathf.Abs(kb.Depth - KeyBlock.DepthOf(1)) > 1e-4f) return "size " + F(kb.Width) + " x " + F(kb.Depth);
            if (Mathf.Abs(kb.px - SM.ColumnX(1)) > 1e-3f) return "x " + F(kb.px) + " column " + F(SM.ColumnX(1));
            int low = kb.chordRootMIDI; float lastX = float.MinValue, whiteZ = 0f;
            for (int k = 0; k < kb.cols; k++)
            {
                var t = kb.GetTile(k, 0); var p = t.Top;
                if (p.x < kb.WestEdge || p.x > kb.EastEdge || p.z < kb.FrontEdge || p.z > kb.BackEdge) return "key " + k + " outside the case";
                if (!KeyBlock.IsBlackKey(t)) { if (p.x <= lastX) return "white keys not left to right at " + k; lastX = p.x; whiteZ = p.z; }
            }
            for (int k = 0; k < kb.cols; k++)
            {
                var t = kb.GetTile(k, 0); if (!KeyBlock.IsBlackKey(t)) continue;
                var nb = k + 1 < kb.cols ? kb.GetTile(k + 1, 0) : null; var pb = k > 0 ? kb.GetTile(k - 1, 0) : null;   // its white neighbours (an end key has one)
                float wTop = Mathf.Max(nb != null ? nb.Top.y : float.MinValue, pb != null ? pb.Top.y : float.MinValue);
                if (t.Top.y < wTop + ProjectConfig.BlackKeyRaise * 0.8f) return "black key " + k + " not raised: " + F(t.Top.y) + " vs " + F(wTop);
                if (t.Top.z <= whiteZ + 0.1f) return "black key " + k + " not set back";
                if (pb != null && nb != null && (t.Top.x <= pb.Top.x || t.Top.x >= nb.Top.x)) return "black key " + k + " not between its neighbours";
            }
            float rise = kb.GetTile(kb.cols - 1, 0).Top.y - kb.GetTile(0, 0).Top.y, want = KeyBlock.KeyRiseOf(kb.cols) * (kb.cols - 1);   // keys 0 and 24 are the same pitch class; v8: the whole rise is capped (KeyRiseTotalMax)
            if (Mathf.Abs(rise - want) > 0.05f) return "rise " + F(rise) + " want " + F(want);
            return null;
        });
        Run(sb, "keyboard marks: a tiny house on every tonic key, off-scale keys dimmer, a dot on exactly the melody notes (v8: chord tones + tensions) of its column's chord (ChordOfColumn = the column's chord island), no job badge, no platform house", () =>
        {
            var kb = K(); if (kb == null) return "no keyboard";
            var ch = SM.ChordOfColumn(kb.column);
            if (ch == null || ch.IsKeyboard || ch != SM.AnchorOf(1)) return "ChordOfColumn " + (ch != null ? ch.assignedChord : "null");
            int root; IList<int> semis; Harmony.ChordOf(kb, out root, out semis);
            if (root != ch.chordRootMIDI) return "ChordOf root " + root;
            int low = kb.chordRootMIDI, dots = 0;
            for (int k = 0; k < kb.cols; k++)
            {
                var t = kb.GetTile(k, 0);
                bool tone = semis.Any(s => Pc(s) == Pc(low + k - root));
                // v8: the dots are the melody's notes (Harmony.MelodyPool: the chord tones full size, the genre's tensions smaller)
                var sk = MusicTheory.KeyOfSong(); bool pooled = Harmony.MelodyPool(root, semis, sk.tonic, sk.minor)[Pc(low + k)];
                if (kb.KeyDotShown(k) != pooled) return "dot on key " + k + " = " + kb.KeyDotShown(k) + " want " + pooled;
                if (tone && kb.KeyTensionShown(k)) return "chord tone on key " + k + " wears a tension's dot";
                if (tone) dots++;
                bool house = t.transform.Find("KeyHouse") != null;
                if (house != (Pc(low + k) == key.tonic)) return "house on key " + k;
            }
            // off-scale: every in-scale key of a colour is brighter than every off-scale key of that colour
            float inW = 9f, offW = -1f, inB = 9f, offB = -1f;
            for (int k = 0; k < kb.cols; k++)
            {
                var t = kb.GetTile(k, 0); float v = t.BaseColor.grayscale; bool inScale = Harmony.InScale(key.tonic, key.minor, Pc(low + k));
                if (KeyBlock.IsBlackKey(t)) { if (inScale) inB = Mathf.Min(inB, v); else offB = Mathf.Max(offB, v); }
                else { if (inScale) inW = Mathf.Min(inW, v); else offW = Mathf.Max(offW, v); }
            }
            if ((offW >= 0f && inW <= offW) || (offB >= 0f && inB <= offB)) return "off-scale keys not dimmer: white " + F(inW) + "/" + F(offW) + " black " + F(inB) + "/" + F(offB);
            if (kb.JobShown != 0 || kb.JobBadgeGlyph != null || kb.HomeShown) return "badge / house on a keyboard";
            Info(sb, "keyboard: lowest " + low + ", " + dots + " dotted keys over " + ch.assignedChord + ", white brightness " + F(inW) + " (off " + F(offW) + "), black " + F(inB) + " (off " + F(offB) + ")");
            return null;
        });
        {
            var kb = K();
            if (kb != null)
            {
                var b = SM.ColumnBounds(0); b.Encapsulate(SM.ColumnBounds(1)); b.Encapsulate(SM.ColumnBounds(2));
                Frame(b, 1.05f);
                yield return Wait(0.6f);
                yield return Shot("k6_keyboard.png");
                Frame(kb.WorldBounds, 1.2f);
                yield return Wait(0.6f);
                yield return Shot("k6_keyboard_close.png");
            }
        }
        int endKb = -1;
        Run(sb, "AddKeyboardIsland(-1): a new column after the last (its anchor, the last column's lane), one History entry; a keyboard-only column plays over the chord before it (ChordOfColumn looks back; the dots follow)", () =>
        {
            int nc = SM.ColumnCount, h = History.UndoCount; float lastZ = SM.AnchorOf(nc - 1).pz; var prevChord = SM.AnchorOf(nc - 1);
            endKb = SM.AddKeyboardIsland(-1, false);
            if (endKb < 0) return "refused";
            var kb = SM.Islands[endKb];
            if (!kb.IsKeyboard || SM.ColumnCount != nc + 1 || kb.column != nc || !kb.IsAnchor) return "column " + kb.column + " of " + SM.ColumnCount;
            if (Mathf.Abs(kb.pz - lastZ) > 0.01f) return "lane " + F(kb.pz) + " want " + F(lastZ);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            var ch = SM.ChordOfColumn(kb.column);
            if (ch == null || ch.assignedChord != prevChord.assignedChord || ch.column != nc - 1) return "ChordOfColumn " + (ch != null ? ch.assignedChord + "@" + ch.column : "null");
            int low = kb.chordRootMIDI;
            var sk = MusicTheory.KeyOfSong(); var pool = Harmony.MelodyPool(ch.chordRootMIDI, ch.semitoneList, sk.tonic, sk.minor);   // v8: the melody's notes
            for (int k = 0; k < kb.cols; k++) { if (kb.KeyDotShown(k) != pool[Pc(low + k)]) return "dot " + k; }
            return Invariants();
        });
        Run(sb, "a full column: AddKeyboardIsland on it goes to a new column after the last (the full column unchanged, one History entry)", () =>
        {
            int c = 2; while (SM.ColumnSize(c) < ProjectConfig.MaxLanes) if (SM.AddIslandInColumn(SM.ColumnFirst(c), true) < 0) return "could not fill column 2";
            int nc = SM.ColumnCount, h = History.UndoCount;
            int j = SM.AddKeyboardIsland(SM.ColumnFirst(c), true);
            if (j < 0 || !SM.Islands[j].IsKeyboard || SM.Islands[j].column != nc || SM.ColumnCount != nc + 1 || SM.ColumnSize(c) != ProjectConfig.MaxLanes) return "at " + j + " column " + (j >= 0 ? SM.Islands[j].column : -1);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            for (int u = 0; u < ProjectConfig.MaxLanes; u++) History.Undo();   // back to the song with the two keyboards
            if (SM.ColumnSize(2) != 1 || SM.Islands.Count(k => k.IsKeyboard) != 2) return "undo: size " + SM.ColumnSize(2) + " keyboards " + SM.Islands.Count(k => k.IsKeyboard);
            return Invariants();
        });
        Run(sb, "keyboards through the ops: duplicate right / above stay keyboards (+ beside a keyboard adds a keyboard), register moves every key 12, the chord wheel / mood skip it, a merge with a chord island is refused, the free drag's commit keeps it, undo / redo keep the kind", () =>
        {
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            if (kIdx < 0) return "lost the column-1 keyboard: " + Kinds();
            int d0 = SM.DuplicateIsland(kIdx, 0);
            if (d0 < 0 || !SM.Islands[d0].IsKeyboard || SM.Islands[d0].tiles.Count != ProjectConfig.KeyboardKeys) return "duplicate right: " + Kinds();
            History.Undo();
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            int a = SM.AddIslandInColumn(kIdx, true);
            if (a < 0 || !SM.Islands[a].IsKeyboard) return "+ above a keyboard: " + Kinds();
            History.Undo();
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            var kb = SM.Islands[kIdx]; int m0 = kb.GetTile(3, 0).midi;
            SM.SetRegister(kIdx, 1);
            if (kb.GetTile(3, 0).midi != m0 + 12 || kb.GetTile(0, 0).midi != kb.chordRootMIDI + 12) return "register: " + kb.GetTile(3, 0).midi;
            History.Undo();
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            int h = History.UndoCount;
            SM.SetChord(kIdx, 62, new[] { 0, 3, 7 }); SM.SetMood(kIdx, 2);
            if (History.UndoCount != h || !SM.Islands[kIdx].IsKeyboard) return "SetChord / SetMood touched a keyboard";
            kb = SM.Islands[kIdx];
            if (SM.MergeIslands(kb, SM.AnchorOf(2), false)) return "merged with a chord island";
            float pz = SM.AnchorOf(3).BackEdge + ProjectConfig.LaneGap + KeyBlock.EdgeInset + 0.2f;
            int r = SM.MoveIslandToColumn(kIdx, 3, false, pz);
            if (r < 0 || !SM.Islands[r].IsKeyboard || SM.Islands[r].column != 3 || SM.Islands[r].tiles.Count != ProjectConfig.KeyboardKeys) return "drag commit: " + Kinds();
            string after = Kinds();
            History.Undo(); string mid = Kinds();
            History.Redo();
            if (Kinds() != after) return "redo: " + Kinds() + " vs " + after;
            History.Undo();
            if (Kinds() != mid || SM.Islands.Count(k => k.IsKeyboard) != 2) return "undo: " + Kinds();
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            return Invariants();
        });
        Run(sb, "keyboards sleep (keys on the dim material, the closed eye on the back rail), repeat (a belt, two passes) and delete (the island goes, undo brings the keyboard back) like any island", () =>
        {
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            var kb = SM.Islands[kIdx];
            SM.SetSleep(kIdx, true);
            if (!kb.SleepShown || kb.SleepGlyph == null || !kb.SleepGlyph.gameObject.activeInHierarchy || kb.SleepGlyph.parent != kb.KeyRail) return "sleep look: eye " + (kb.SleepGlyph != null && kb.SleepGlyph.gameObject.activeInHierarchy);
            if (kb.GetTile(4, 0).GetComponent<Renderer>().sharedMaterial != KeyBlock.DimTileMaterial) return "keys not dimmed";
            SM.SetSleep(kIdx, false);
            if (kb.SleepShown || kb.SleepGlyph.gameObject.activeSelf) return "wake";
            SM.SetRepeat(kIdx, 2);
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            kb = SM.Islands[kIdx];
            if (kb.Passes != 2 || kb.Belt == null || SM.ColumnPasses(1) != 2) return "repeat: passes " + kb.Passes + " belt " + (kb.Belt != null);
            History.Undo();
            int n = SM.Islands.Count, keys = SM.Islands.Count(k => k.IsKeyboard);
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            SM.RemoveMeasure(kIdx);
            if (SM.Islands.Count != n - 1 || SM.Islands.Count(k => k.IsKeyboard) != keys - 1) return "delete: " + Kinds();
            History.Undo();
            if (SM.Islands.Count != n || SM.Islands.Count(k => k.IsKeyboard && k.column == 1 && k.tiles.Count == ProjectConfig.KeyboardKeys) != 1) return "undo delete: " + Kinds();
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            return Invariants();
        });
        Run(sb, "the tray card path: IslandGhost with SongManager.KeyboardMeasure() shows a piano, offers no MERGE beside a chord island, and Place (INSERT at the end) builds a keyboard; one History entry", () =>
        {
            var md = SongManager.KeyboardMeasure(1);
            if (md.kind != 2 || md.chordRootMIDI != SongManager.KeyboardLowestKey()) return "KeyboardMeasure kind " + md.kind;
            var g = IslandGhost.Create(md);
            try
            {
                if (!g.IsKeyboard || g.Rows != 1) return "ghost rows " + g.Rows;
                var chordEnd = SM.AnchorOf(2);   // a chord island: push the ghost against its east edge in its lane
                g.Track(new Vector3(chordEnd.EastEdge + SM.BeltExtent(2) + KeyBlock.IslandWidth * 0.5f + 0.2f, 0f, chordEnd.Center.z));
                if (g.State == IslandGhost.Mode.Merge) return "a keyboard ghost offered a merge with " + g.MergeTarget.assignedChord;
                int nc = SM.ColumnCount, h = History.UndoCount;
                var drop = new IslandGhost.Drop { mode = IslandGhost.Mode.Insert, visible = true, insertAt = nc, pz = SM.AnchorOf(nc - 1).pz };
                int at = IslandGhost.Place(drop, md);
                if (at < 0 || !SM.Islands[at].IsKeyboard || SM.Islands[at].column != nc) return "placed " + at + ": " + Kinds();
                if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
                History.Undo();
            }
            finally { UnityEngine.Object.Destroy(g.gameObject); }
            return null;
        });

        // ================================================================ B. save / load round trip (kind 2, carry)
        Run(sb, "save / load round trip (SongIO.SaveTo a scratch file, never the user's save): kinds, carries, columns, the keyboards' lowest keys and 25 keys", () =>
        {
            SM.SetCarry(SM.ColumnFirst(0), 2);
            kIdx = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            SM.SetCarry(kIdx, 1);
            Func<string> fp = () => Kinds() + "|" + string.Join(",", SM.Islands.Where(k => k.IsKeyboard).Select(k => k.chordRootMIDI + ":" + k.tiles.Count)) + "|" + SM.TotalBeats;
            string before = fp();
            if (!SongIO.SaveTo(ScratchSave)) return "SaveTo failed";
            var json = File.ReadAllText(ScratchSave);
            if (!json.Contains("\"kind\":2") || !json.Contains("\"carry\":2")) return "json has no kind 2 / carry 2";
            LoadFixture();
            if (SM.Islands.Any(k => k.IsKeyboard)) return "fixture has keyboards";
            if (!SongIO.LoadFrom(ScratchSave)) return "LoadFrom failed";
            string after = fp();
            try { File.Delete(ScratchSave); } catch (Exception) { }
            if (after != before) return "differs: " + after + " vs " + before;
            return Invariants();
        });

        // ================================================================ C. carry windows
        BuildSong(7707);
        yield return null;
        {
            int i2 = SM.ColumnFirst(2);
            SM.SetRepeat(i2, 2);   // column 2 plays two passes: a target with passes
        }
        int home0 = SM.ColumnFirst(0);
        int carryEvents = 0; KeyBlock carryIsland = null;
        Action<KeyBlock> onCarry = kb => { carryEvents++; carryIsland = kb; };
        SM.OnCarryChanged += onCarry;
        Run(sb, "SetCarry(column 0's island, 2): one History entry, OnCarryChanged once with the island, CarryTargets = the next two columns' islands; layout unchanged", () =>
        {
            int h = History.UndoCount; float x3 = SM.ColumnX(3); float tb = SM.TotalBeats;
            SM.SetCarry(home0, 2);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (carryEvents != 1 || carryIsland != SM.Islands[home0]) return "OnCarryChanged " + carryEvents;
            var t = SM.CarryTargets(home0);
            if (t.Count != 2 || t[0] != SM.AnchorOf(1) || t[1] != SM.AnchorOf(2)) return "targets " + string.Join(",", t.Select(k => k.assignedChord + "@" + k.column));
            if (Mathf.Abs(SM.ColumnX(3) - x3) > 1e-3f || Mathf.Abs(SM.TotalBeats - tb) > 1e-4f) return "layout / timeline changed";
            return null;
        });
        Run(sb, "carry windows: a resident cube gets its own passes, then every pass of each target's column on the target (count = own passes + Σ target passes; carried, hop 1..n, order = the target's column, starts in song order)", () =>
        {
            var home = SM.Islands[home0]; var cube = CubesOf(home).FirstOrDefault();
            if (cube == null) return "no cube";
            var t = SM.CarryTargets(home0);
            int own = home.Passes, want = own; foreach (var k in t) want += SM.ColumnPasses(k.column);
            var w = cube.windows;
            if (w.Count != want) return "windows " + w.Count + " want " + want;
            for (int i = 0; i < own; i++) if (w[i].carried || w[i].island != home || w[i].order != home.column || w[i].hop != 0) return "own window " + i;
            int idx = own;
            for (int hop = 1; hop <= t.Count; hop++)
            {
                var tk = t[hop - 1]; int c = tk.column;
                for (int p = 0; p < SM.ColumnPasses(c); p++, idx++)
                {
                    var wi = w[idx];
                    if (!wi.carried || wi.hop != hop || wi.island != tk || wi.order != c) return "window " + idx + " hop " + wi.hop + " island " + (wi.island != null ? wi.island.assignedChord : "null");
                    if (Mathf.Abs(wi.start - SM.PassStart(c, p)) > 1e-4f || Mathf.Abs(wi.length - SM.PassLength(c)) > 1e-4f) return "window " + idx + " start " + F(wi.start);
                }
            }
            for (int i = 1; i < w.Count; i++) if (w[i].start <= w[i - 1].start) return "not in song order at " + i;
            Info(sb, "carry windows of the column-0 cube: " + string.Join(" ", w.Select(x => F(x.start) + (x.carried ? "h" + x.hop : "") + "@" + x.order)));
            // every other cube of the song keeps only its own windows (only island 0 carries)
            foreach (var c in SequenceMaster.Cubes) if (c != null && c != cube && !c.rider && !c.IsOnMoon && c.windows.Any(x => x.carried)) return "another cube carries";
            return null;
        });
        Run(sb, "carry silent rules: a sleeping target silences only its carried windows; a sleeping home silences every window; waking restores", () =>
        {
            var home = SM.Islands[home0]; var cube = CubesOf(home).First(); var t1 = SM.CarryTargets(home0)[0];
            int ti = SM.Islands.IndexOf(t1);
            SM.SetSleep(ti, true);
            foreach (var w in cube.windows) if (w.silent != (w.carried && w.island == t1)) return "target asleep: window at " + F(w.start) + " silent " + w.silent;
            SM.SetSleep(ti, false);
            SM.SetSleep(home0, true);
            if (cube.windows.Any(w => !w.silent)) return "home asleep: an audible window";
            SM.SetSleep(home0, false);
            if (cube.windows.Any(w => w.silent)) return "awake: a silent window";
            return null;
        });
        Run(sb, "carry chain rules (§21 the long grid; was: a keyboard carries to keyboards first, else chord islands; Riders on every column): it stops at the song's / section's end (carry 3 on the second-to-last column = 1 target) and at a column with no grid of its family in its lane; a keyboard extends over keyboards only; a Rider plays exactly a resident's windows", () =>
        {
            int i2 = SM.ColumnFirst(2);
            SM.SetCarry(i2, 3);
            i2 = SM.ColumnFirst(2);
            var t = SM.CarryTargets(i2);
            if (t.Count != 1 || t[0] != SM.AnchorOf(3)) return "end of song: " + t.Count;
            var cube = CubesOf(SM.Islands[i2]).FirstOrDefault();
            if (cube != null && cube.windows.Count(w => w.carried) != SM.ColumnPasses(3)) return "carried windows " + cube.windows.Count(w => w.carried);
            SM.SetCarry(i2, 0);
            // keyboards: one in column 1 (carry 2) and one in column 2; column 3 holds none -> keyboard, then the chord island
            int k1 = SM.AddKeyboardIsland(SM.ColumnFirst(1), false);
            int k2 = SM.AddKeyboardIsland(SM.ColumnFirst(2), false);
            k1 = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            SM.SetCarry(k1, 2);
            k1 = SM.Islands.FindIndex(k => k.IsKeyboard && k.column == 1);
            var tk = SM.CarryTargets(k1);
            if (tk.Count != 1 || !tk[0].IsKeyboard || tk[0].column != 2) return "§21 keyboard targets " + string.Join(",", tk.Select(k => k.kind + "@" + k.column));
            // a chord island whose next column holds only a keyboard: the chain stops there
            int nc = SM.ColumnCount;
            int k3 = SM.AddKeyboardIsland(-1, false);
            int i3 = SM.ColumnFirst(3);
            SM.SetCarry(i3, 2);
            i3 = SM.ColumnFirst(3);
            if (SM.CarryTargets(i3).Count != 0) return "a chord island carried onto a keyboard-only column";
            // §21: a Rider plays only on its home grid — exactly a resident's windows there (its long grid's carried windows included)
            var rider = PathManager.I.RestoreCube(new CubeState { instrument = 4, measure = SM.ColumnFirst(0), moon = -1, xs = new[] { 0 }, zs = new[] { 0 }, rests = new bool[1], step = 1, gate = 1, volume = 1f, hits = -1, rider = true, twinOf = -1 });
            SM.RecomputeMeasureStarts();
            var res = CubesOf(SM.Islands[SM.ColumnFirst(0)]).FirstOrDefault();
            Func<AudioCube, string> sig = c => string.Join(" ", c.windows.Select(w => F(w.start) + "@" + (w.island != null ? w.island.column : -1) + (w.carried ? "c" : "")).ToArray());
            if (rider == null || res == null || sig(rider) != sig(res)) return "rider windows " + (rider != null ? sig(rider) : "none") + " vs resident " + (res != null ? sig(res) : "none");
            return null;
        });
        SM.OnCarryChanged -= onCarry;
        {
            // the carry chain to look at (numbers only: W draws the bridges)
            var b = SM.ColumnBounds(0); for (int c = 1; c < Mathf.Min(4, SM.ColumnCount); c++) b.Encapsulate(SM.ColumnBounds(c));
            Frame(b, 1.0f);
            yield return Wait(0.6f);
            yield return Shot("k6_carry.png");
        }
        {
            // play the carrying song: Synth late / errors stay 0 (no capture in this span)
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            GlobalClock.Seek(0); GlobalClock.Play();
            yield return UntilBeat(Math.Min(SM.TotalBeats - 0.5, 14.0), 12f);
            GlobalClock.Stop();
            int late = Synth.LateEvents - late0, errs = Synth.Errors - err0;
            Line(sb, late == 0 && errs == 0, "Synth late 0 / errors 0 while a carrying song with keyboards plays", "late +" + late + " errors +" + errs + " | " + Synth.Stats());
        }

        // ================================================================ D. the octave tower
        BuildSong(8808);
        yield return null;
        int ti2 = SM.ColumnFirst(2);
        var tkb = SM.Islands[ti2];
        double s2 = SM.ColumnStart(2), L2 = SM.PassLength(2) * tkb.Passes;
        GlobalClock.Stop();
        SM.SetRegister(ti2, 1);
        Info(sb, "tower island " + tkb.assignedChord + " in column 2: turn " + F(s2) + " .. " + F(s2 + L2) + ", rest " + F(SongManager.TowerRestOf(1)) + ", top " + F(SongManager.TowerTopOf(1)));
        Run(sb, "tower samples (register +1, pure): rest hint before its turn, rising from −0.5 beat, the top (v7: 3.2 u) by +0.25 with a slight bob on the beat; v7 §12.3: STAYS UP after its last pass until the song resets, then sinks slowly (3 beats, staggered), rest again; states 0 / 1 / 2 / 3", () =>
        {
            int st; float ph;
            float rest = SongManager.TowerRestOf(1), top = SongManager.TowerTopOf(1);
            float a = SM.TowerLiftAt(tkb, s2 - 2.0, out st, out ph); if (Mathf.Abs(a - rest) > 1e-4f || st != 0) return "before: " + F(a) + " state " + st;
            float b = SM.TowerLiftAt(tkb, s2 - 0.25, out st, out ph); if (st != 1 || b <= rest + 0.05f || b >= top) return "rising: " + F(b) + " state " + st;
            float b2 = SM.TowerLiftAt(tkb, s2 - 0.45, out st, out ph); if (b2 >= b) return "not rising: " + F(b2) + " -> " + F(b);
            float c = SM.TowerLiftAt(tkb, s2 + 1.5, out st, out ph); if (st != 2 || c < top - 1e-3f || c > top + ProjectConfig.TowerBob + 1e-3f) return "top: " + F(c) + " state " + st;
            float peak = SM.TowerLiftAt(tkb, s2 + 1.08), low = SM.TowerLiftAt(tkb, s2 + 1.95);
            if (peak - low < 0.02f || peak - low > ProjectConfig.TowerBob + 1e-3f) return "bob " + F(peak - low);
            // v7 §12.3 (changed meaning: v6 sank right after the turn): after its turn it holds at the top (still) until the song resets
            float held = SM.TowerLiftAt(tkb, s2 + L2 + 1.5, out st, out ph); if (st != 2 || Mathf.Abs(held - top) > 1e-4f) return "held: " + F(held) + " state " + st;
            int loops = GlobalClock.LoopIndex; GlobalClock.LoopIndex = 1;
            float stag = SM.ResetStaggerOf(tkb);
            float d = SM.TowerLiftAt(tkb, stag + 1.5, out st, out ph); int sd = st;
            float d2 = SM.TowerLiftAt(tkb, stag + 2.5);
            float e = SM.TowerLiftAt(tkb, stag + ProjectConfig.TowerSinkBeats + 0.05, out st, out ph); int se = st;
            GlobalClock.LoopIndex = loops;
            if (sd != 3 || d <= rest + 0.2f || d >= top - 0.2f) return "sinking after the reset: " + F(d) + " state " + sd;
            if (d2 >= d) return "not sinking";
            if (Mathf.Abs(e - rest) > 1e-4f || se != 0) return "after: " + F(e) + " state " + se;
            Info(sb, "tower samples: " + F(a) + " | rising " + F(b2) + " " + F(b) + " | top " + F(c) + " bob " + F(peak - low) + " | held " + F(held) + " | after the reset (stagger " + F(stag) + ") sinking " + F(d) + " " + F(d2) + " | rest " + F(e));
            return null;
        });
        Run(sb, "a lowered island (register −1) dips 0.8 u on its turn, v7: stays dipped until the song resets, then floats back; at rest a hint lower (−0.3); never near the sea", () =>
        {
            SM.SetRegister(ti2, -1);
            float a = SM.TowerLiftAt(tkb, s2 - 2.0), c = SM.TowerLiftAt(tkb, s2 + 1.5), heldDip = SM.TowerLiftAt(tkb, s2 + L2 + 3.2);
            int loops = GlobalClock.LoopIndex; GlobalClock.LoopIndex = 1;
            float e = SM.TowerLiftAt(tkb, SM.ResetStaggerOf(tkb) + ProjectConfig.TowerSinkBeats + 0.05);
            GlobalClock.LoopIndex = loops;
            if (Mathf.Abs(heldDip + 0.8f) > 1e-4f) return "not held dipped after its turn: " + F(heldDip);
            if (Mathf.Abs(a + 0.3f) > 1e-4f || Mathf.Abs(e + 0.3f) > 1e-4f) return "rest " + F(a) + " / " + F(e);
            if (c > -0.8f + 1e-3f || c < -0.8f - ProjectConfig.TowerBob - 1e-3f) return "dip " + F(c);
            SM.SetRegister(ti2, -2);
            float c2 = SM.TowerLiftAt(tkb, s2 + 1.5);
            float underside = c2 - 0.03f - ProjectConfig.PlatformThickness;   // the platform's underside at the deepest dip (the sea is at ≈ −4)
            if (c2 > -1.6f + 1e-3f || underside < -3.2f) return "dip -2 " + F(c2) + " underside " + F(underside);
            SM.SetRegister(ti2, 1);
            return null;
        });
        Run(sb, "pure: the same beat gives the same lift (twice, after a seek elsewhere, after a timeline recompute)", () =>
        {
            var beats = new[] { s2 - 0.3, s2 + 0.1, s2 + 2.33, s2 + L2 + 0.7, s2 + L2 + 2.9 };
            var v1 = beats.Select(b => SM.TowerLiftAt(tkb, b)).ToArray();
            var v2 = beats.Select(b => SM.TowerLiftAt(tkb, b)).ToArray();
            GlobalClock.Seek(3.7);
            SM.RecomputeMeasureStarts();
            var v3 = beats.Select(b => SM.TowerLiftAt(tkb, b)).ToArray();
            GlobalClock.Stop();
            for (int i = 0; i < beats.Length; i++) if (v1[i] != v2[i] || v1[i] != v3[i]) return "beat " + F(beats[i]) + ": " + F(v1[i]) + " " + F(v2[i]) + " " + F(v3[i]);
            return null;
        });
        Run(sb, "the loop wraps: a column-0 tower (v7: it plays again right at the reset) stays up through the loop point; a last-column tower sinks in the first beats after it (from the second loop pass)", () =>
        {
            int i0 = SM.ColumnFirst(0), il = SM.ColumnFirst(SM.ColumnCount - 1);
            SM.SetRegister(i0, 1); SM.SetRegister(il, 1);
            var k0 = SM.Islands[i0]; var kl = SM.Islands[il];
            int st; float ph; double tot = SM.TotalBeats;
            float a = SM.TowerLiftAt(k0, tot - 0.25, out st, out ph);
            if (st != 2 || Mathf.Abs(a - SongManager.TowerTopOf(1)) > 1e-3f) return "column 0 before the loop point: " + F(a) + " state " + st;
            int loops = GlobalClock.LoopIndex;
            GlobalClock.LoopIndex = 1;
            float b = SM.TowerLiftAt(kl, 1.0, out st, out ph);
            GlobalClock.LoopIndex = loops;
            if (st != 3 || b <= SongManager.TowerRestOf(1) + 0.1f) return "last column after the loop point: " + F(b) + " state " + st;
            float c = SM.TowerLiftAt(kl, 1.0);   // the first pass (LoopIndex 0): no sink before it ever rose
            if (Mathf.Abs(c - SongManager.TowerRestOf(1)) > 1e-4f) return "first pass sank: " + F(c);
            SM.SetRegister(i0, 0); SM.SetRegister(il, 0);
            return null;
        });
        {
            // the stopped preview: ▲ while stopped plays the rise and the slow sink once
            GlobalClock.Stop();
            yield return Wait(0.8f);
            SM.SetRegister(ti2, 2);
            bool prev0 = tkb.TowerPreviewing;
            yield return Wait(0.5f);
            float up = tkb.LiftY; int stUp = tkb.TowerState;
            float wait = (float)((ProjectConfig.TowerLead + ProjectConfig.TowerRiseEnd + ProjectConfig.TowerPreviewHold + ProjectConfig.TowerSinkBeats) / GlobalClock.BeatsPerSecond);
            yield return Wait(wait + 0.6f);
            float after = tkb.LiftY;
            Line(sb, prev0 && up > SongManager.TowerTopOf(2) * 0.8f && Mathf.Abs(after - SongManager.TowerRestOf(2)) < 0.01f && !tkb.TowerPreviewing && tkb.TowerState == 0,
                "stopped = the rest hint; ▲ while stopped plays the rise and the slow sink once (the preview of its turn)",
                "previewing " + prev0 + ", 0.5 s in: lift " + F(up) + " (top " + F(SongManager.TowerTopOf(2)) + ", state " + stUp + "), after " + F(wait + 0.6f) + " s: " + F(after) + " (rest " + F(SongManager.TowerRestOf(2)) + ")");
            SM.SetRegister(ti2, 1);
            yield return Wait(0.3f);
        }
        {
            // playing: the shown lift follows the pure lift; the transform, the hub and the cubes ride along; a seek glides there
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            GlobalClock.Seek(Math.Max(0.0, s2 - 2.0)); GlobalClock.Play();
            yield return UntilBeat(s2 + 1.4);
            float want = SM.TowerLiftAt(tkb, GlobalClock.SongBeatD), shown = tkb.LiftY;
            float ty = tkb.transform.position.y, hy = tkb.HubPos.y;
            var cube = CubesOf(tkb).FirstOrDefault();
            float cy = cube != null && cube.nodes.Count > 0 ? cube.nodes[0].Top.y : 0f;
            int st = tkb.TowerState;
            Line(sb, Mathf.Abs(shown - want) < 0.03f && Mathf.Abs(ty - shown) < 0.02f && Mathf.Abs(hy - (0.15f + shown)) < 0.02f && st == 2 && cy > shown,
                "playing: the shown lift = the pure lift on its turn (state up); the island, its hub and its tiles ride it",
                "beat " + F(GlobalClock.SongBeatD) + " lift " + F(shown) + " (pure " + F(want) + "), transform y " + F(ty) + ", hub y " + F(hy) + ", a tile top " + F(cy) + ", state " + st);
            int late = Synth.LateEvents - late0, errs = Synth.Errors - err0;   // before the seek (a seek while playing may fire one step late by design)
            float before = tkb.LiftY;
            GlobalClock.Seek(Math.Max(0.5, s2 - 3.0));   // v7: before its turn (after it the tower now holds up: no glide to see)
            yield return null;
            float oneFrame = tkb.LiftY; int stGlide = tkb.TowerState;
            yield return Wait(0.8f);
            float target = SM.TowerLiftAt(tkb, GlobalClock.SongBeatD), landed = tkb.LiftY;
            GlobalClock.Stop();
            Line(sb, oneFrame > before - (before - target) * 0.5f && Mathf.Abs(landed - target) < 0.01f && stGlide == 3,
                "a seek glides (no snap: one frame later most of the way remains; state sinking) and lands on the pure lift",
                "up " + F(before) + " -> one frame after the seek " + F(oneFrame) + " (state " + stGlide + ") -> 0.8 s " + F(landed) + " (pure " + F(target) + ")");
            Line(sb, late == 0 && errs == 0, "Synth late 0 / errors 0 while a tower rises and holds (no capture, no seek in this span)", "late +" + late + " errors +" + errs);
            yield return Wait(0.8f);
        }
        {
            // captures while playing: mid-rise and at the top; then a lowered island's dip
            var b = SM.ColumnBounds(1); b.Encapsulate(SM.ColumnBounds(2)); b.Encapsulate(SM.ColumnBounds(3));
            b.Encapsulate(b.center + Vector3.up * 4.5f);
            Frame(b, 1.05f);
            GlobalClock.Seek(Math.Max(0.0, s2 - 2.5)); GlobalClock.Play();
            yield return UntilBeat(s2 - 0.18);
            float rl = tkb.LiftY; int rs = tkb.TowerState;
            yield return Shot("k6_tower_rise.png");
            yield return UntilBeat(s2 + 1.7);
            float tl = tkb.LiftY; int ts = tkb.TowerState;
            yield return Shot("k6_tower_top.png");
            GlobalClock.Stop();
            Info(sb, "captures: k6_tower_rise.png lift " + F(rl) + " state " + rs + ", k6_tower_top.png lift " + F(tl) + " state " + ts);
            int i1 = SM.ColumnFirst(1);
            SM.SetRegister(i1, -2);
            var d1 = SM.Islands[i1];
            double s1 = SM.ColumnStart(1);
            yield return Wait(0.4f);
            GlobalClock.Seek(Math.Max(0.0, s1 - 1.5)); GlobalClock.Play();
            yield return UntilBeat(s1 + 1.2);
            { float tw = Time.realtimeSinceStartup; while (d1.TowerState != 2 && Time.realtimeSinceStartup - tw < 0.6f && GlobalClock.SongBeatD < s1 + 3.0) yield return null; }   // (a slow editor frame: the dip catches up smoothly first)
            float dl = d1.LiftY; int ds = d1.TowerState;
            yield return Shot("k6_tower_dip.png");
            GlobalClock.Stop();
            Info(sb, "capture: k6_tower_dip.png lift " + F(dl) + " state " + ds + " (register −2: dip " + F(SongManager.TowerTopOf(-2)) + ")");
            Line(sb, rs == 1 && rl > SongManager.TowerRestOf(1) && ts == 2 && tl > SongManager.TowerTopOf(1) - 0.05f && ds == 2 && dl < SongManager.TowerTopOf(-2) + 0.05f,
                "while playing the tower is mid-rise just before its column, up on its turn, and a lowered island dips on its turn (captured)", "rise " + F(rl) + ", top " + F(tl) + ", dip " + F(dl));
        }

        // ================================================================ E. job badges on the platforms
        BuildSong(6606);
        yield return null;
        {
            int kbAt = SM.AddKeyboardIsland(SM.ColumnFirst(1), true);
            yield return Wait(0.8f);
            Run(sb, "job badges: every chord island wears the badge of its Harmony.Job (1..4, U1's JobBadge.WorldTexture) on its front-left corner; keyboards and Moons wear none", () =>
            {
                var sbj = new StringBuilder();
                foreach (var kb in SM.Islands)
                {
                    int want = kb.IsKeyboard ? 0 : Harmony.Job(kb);
                    if (kb.JobShown != want) return kb.assignedChord + " shows " + kb.JobShown + " want " + want;
                    if (kb.IsKeyboard) { if (kb.JobBadgeGlyph != null) return "a keyboard has a badge"; continue; }
                    var r = kb.JobBadgeGlyph != null ? kb.JobBadgeGlyph.GetComponent<MeshRenderer>() : null;
                    if (r == null || r.sharedMaterial == null || r.sharedMaterial.mainTexture != JobBadge.WorldTexture(want)) return kb.assignedChord + ": badge texture";
                    var lp = kb.JobBadgeGlyph.localPosition;
                    if (lp.x > -KeyBlock.EdgeInset + ProjectConfig.PlatformPad || lp.z > -KeyBlock.EdgeInset + ProjectConfig.PlatformPad) return kb.assignedChord + ": not on the front-left corner";
                    sbj.Append(kb.assignedChord).Append('=').Append(want).Append(' ');
                }
                foreach (var m in SM.Moons) if (m != null && (m.JobBadgeGlyph != null || m.JobShown != 0)) return "a Moon has a badge";
                Info(sb, "jobs: " + sbj.ToString().Trim());
                return null;
            });
            Run(sb, "a chord change re-badges the island (SetChord → its new job), sleep hides the badge, the job-1 badge replaces the home sticker (which stays only on a home island doing another job)", () =>
            {
                int i = SM.ColumnFirst(2);
                var k = MusicTheory.KeyOfSong();
                int tonicRoot = 55 + ((k.tonic - 55) % 12 + 12) % 12, fifthRoot = 55 + ((k.tonic + 7 - 55) % 12 + 12) % 12;
                SM.SetChord(i, fifthRoot, new[] { 0, 4, 7, 10 });   // the V7: pull
                if (SM.Islands[i].JobShown != 4) return "V7 shows " + SM.Islands[i].JobShown;
                SM.SetChord(i, tonicRoot, k.minor ? new[] { 0, 3, 7, 10 } : new[] { 0, 4, 7, 11 });   // the home chord
                var kb = SM.Islands[i];
                if (kb.JobShown != 1 || kb.HomeShown) return "home chord: job " + kb.JobShown + " house " + kb.HomeShown;
                SM.SetChord(i, tonicRoot, new[] { 0, 4, 7, 10 });   // a tonic dominant: home island, job 4 -> the house stays
                kb = SM.Islands[i];
                if (kb.JobShown != 4 || !kb.HomeShown) return "tonic dominant: job " + kb.JobShown + " house " + kb.HomeShown;
                SM.SetSleep(i, true);
                if (kb.JobShown != 0 || kb.HomeShown) return "asleep: badge " + kb.JobShown;
                SM.SetSleep(i, false);
                if (kb.JobShown != 4) return "awake: " + kb.JobShown;
                SM.SetChord(i, tonicRoot, k.minor ? new[] { 0, 3, 7, 10 } : new[] { 0, 4, 7, 11 });
                return null;
            });
            var b = SM.ColumnBounds(0); for (int c = 1; c < SM.ColumnCount; c++) b.Encapsulate(SM.ColumnBounds(c));
            Frame(b, 1.0f);
            yield return Wait(0.6f);
            yield return Shot("k6_badges.png");
            var ib = SM.Islands[SM.ColumnFirst(2)];
            Frame(ib.WorldBounds, 1.3f);
            yield return Wait(0.6f);
            yield return Shot("k6_badge_close.png");
        }

        // ================================================================ G. Moons follow the song (SPEC v6 §11)
        BuildSong(9909);   // the demo Moon (column 0) with its groove
        yield return null;
        int bpbG = Mathf.Max(1, GlobalClock.BeatsPerBar);
        Func<AudioCube, int, int, string> barsOf = (c, b0, b1) =>
        {
            if (c.windows.Count != b1 - b0) return "windows " + c.windows.Count + " want " + (b1 - b0);
            for (int i = 0; i < c.windows.Count; i++)
            {
                var w = c.windows[i]; int b = b0 + i;
                if (Mathf.Abs(w.start - b * bpbG) > 1e-4f || Mathf.Abs(w.length - bpbG) > 1e-4f || w.order != b || w.silent || w.carried) return "window " + i + " start " + F(w.start) + " order " + w.order;
            }
            return null;
        };
        Func<KeyBlock, List<AudioCube>> moonCubes = m => SequenceMaster.Cubes.Where(c => c != null && c.IsOnMoon && c.Moon == m).ToList();
        int drumsInst = 0; for (int i = 0; i < Instruments.Count; i++) if (Instruments.IsDrums(i)) { drumsInst = i; break; }
        Run(sb, "an old song (every Moon at column 0: the demo Moon, then a second one) keeps exactly the v5 windows: one per bar of the whole song", () =>
        {
            if (SM.Moons.Count != 1 || SM.MoonStartColumn(0) != 0) return "moons " + SM.Moons.Count + " start " + SM.MoonStartColumn(0);
            int bars = Mathf.RoundToInt(SM.TotalBeats / bpbG);
            var cs = moonCubes(SM.Moons[0]); if (cs.Count == 0) return "no groove cubes";
            foreach (var c in cs) { string e = barsOf(c, 0, bars); if (e != null) return e; }
            int m1 = SM.AddMoon();
            if (m1 != 1 || SM.MoonStartColumn(1) != 0) return "AddMoon() start " + SM.MoonStartColumn(m1);
            PathManager.I.RestoreCube(new CubeState { instrument = drumsInst, measure = 0, moon = 1, xs = new[] { 2 }, zs = new[] { 1 }, rests = new bool[1], step = (int)StepLen.Quarter, gate = 1, volume = 1f, hits = -1, twinOf = -1 });
            SM.RecomputeMeasureStarts();
            foreach (var m in SM.Moons) foreach (var c in moonCubes(m)) { string e = barsOf(c, 0, bars); if (e != null) return "layered: " + e; }
            float a, b; SM.MoonSection(1, out a, out b);
            if (a != 0f || Mathf.Abs(b - SM.TotalBeats) > 1e-4f) return "section " + F(a) + ".." + F(b);
            return null;
        });
        Run(sb, "two Moons at columns 0 and 2: each plays one window per bar of its SECTION only (0 → column 2's start, column 2 → the end); MoonsAt answers per beat; one History entry", () =>
        {
            int h = History.UndoCount;
            SM.SetMoonColumn(1, 2);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (SM.MoonStartColumn(1) != 2 || SM.MoonEndColumn(0) != 2 || SM.MoonEndColumn(1) != SM.ColumnCount) return "columns " + SM.MoonStartColumn(1) + " ends " + SM.MoonEndColumn(0) + "/" + SM.MoonEndColumn(1);
            int split = Mathf.RoundToInt(SM.ColumnStart(2) / bpbG), bars = Mathf.RoundToInt(SM.TotalBeats / bpbG);
            foreach (var c in moonCubes(SM.Moons[0])) { string e = barsOf(c, 0, split); if (e != null) return "moon 0: " + e; }
            foreach (var c in moonCubes(SM.Moons[1])) { string e = barsOf(c, split, bars); if (e != null) return "moon 1: " + e; }
            var at = new List<KeyBlock>();
            SM.MoonsAt(SM.ColumnStart(1) + 0.5f, at); if (at.Count != 1 || at[0] != SM.Moons[0]) return "MoonsAt column 1: " + at.Count;
            SM.MoonsAt(SM.ColumnStart(3) + 0.5f, at); if (at.Count != 1 || at[0] != SM.Moons[1]) return "MoonsAt column 3: " + at.Count;
            Info(sb, "sections: moon 0 bars 0.." + split + ", moon 1 bars " + split + ".." + bars + " | tracks " + ColumnBands.MoonTrackCount);
            return null;
        });
        Run(sb, "the Moons rest in line with their start columns in the drum lane in front of the islands (never overlapping an island); one dotted track per section on the sea", () =>
        {
            float cx = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, 1).x;
            for (int j = 0; j < SM.Moons.Count; j++)
            {
                var m = SM.Moons[j]; int c = SM.MoonStartColumn(j);
                if (Mathf.Abs(m.Center.x - (SM.ColumnX(c) + cx)) > 0.01f) return "moon " + j + " x " + F(m.Center.x) + " column " + c + " centre " + F(SM.ColumnX(c) + cx);
                var b = m.WorldBounds; b.Expand(ProjectConfig.IslandGap * 2f - 0.05f);
                foreach (var kb in SM.Islands) if (b.Intersects(kb.WorldBounds)) return "moon " + j + " overlaps " + kb.assignedChord;
                if (m.Center.z >= SM.Islands.Min(k => k.FrontEdge)) return "moon " + j + " not in front";
            }
            if (ColumnBands.MoonTrackCount != 2) return "tracks " + ColumnBands.MoonTrackCount;
            float x0, x1, z; ColumnBands.MoonTrackSpan(1, out x0, out x1, out z);
            if (Mathf.Abs(x0 - (SM.ColumnX(2) + cx)) > 0.01f || x1 <= x0) return "track 1 " + F(x0) + ".." + F(x1);
            return null;
        });
        {
            var b = SM.SongBounds; Frame(b, 1.0f);
            yield return Wait(0.6f);
            yield return Shot("k6_moons.png");
        }
        Run(sb, "the follow pose (pure): mid-section a Moon stands in line with the lit column, steps glide in and arrive as a column starts; v7 §12.4: after its section it HOLDS at its section's last column until the song resets; stopped = home", () =>
        {
            var m0 = SM.Moons[0];
            float dx1 = SM.ColumnX(1) - SM.ColumnX(0);
            float a = SM.MoonFollowX(m0, SM.ColumnStart(1) + 1.5);
            if (Mathf.Abs(a - dx1) > 1e-3f) return "mid column 1: " + F(a) + " want " + F(dx1);
            float g = SM.MoonFollowX(m0, SM.ColumnStart(1) - 0.2);
            if (g <= 0.05f || g >= dx1 - 0.05f) return "no glide before column 1: " + F(g);
            float at1 = SM.MoonFollowX(m0, SM.ColumnStart(1));
            if (Mathf.Abs(at1 - dx1) > 1e-3f) return "not arrived at column 1's start: " + F(at1);
            float back = SM.MoonFollowX(m0, SM.ColumnStart(2) + 0.3), home = SM.MoonFollowX(m0, SM.ColumnStart(2) + 3.0);
            if (Mathf.Abs(back - dx1) > 1e-3f || Mathf.Abs(home - dx1) > 1e-3f) return "not held after its section: " + F(back) + ", then " + F(home);   // v7 (was: glides back right after)
            var m1 = SM.Moons[1];
            if (Mathf.Abs(SM.MoonFollowX(m1, SM.ColumnStart(1) + 1.5)) > 1e-4f) return "moon 1 moved outside its section";
            float d3 = SM.MoonFollowX(m1, SM.ColumnStart(3) + 1.0);
            if (Mathf.Abs(d3 - (SM.ColumnX(3) - SM.ColumnX(2))) > 1e-3f) return "moon 1 mid column 3: " + F(d3);
            if (SM.MoonFollowX(m0, 0.0) != 0f) return "stopped not home";
            Info(sb, "follow: column 1 step " + F(dx1) + " u; 0.2 beat before it " + F(g) + "; 0.3 beat after the section " + F(back));
            return null;
        });
        {
            // playing: the shown Moon follows (its centre over the lit column), then home after its section
            var m0 = SM.Moons[0];
            float cx = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, 1).x;
            GlobalClock.Seek(SM.ColumnStart(1) - 1.5); GlobalClock.Play();
            yield return UntilBeat(SM.ColumnStart(1) + 2.2);   // (a slow editor frame makes the Moon catch up smoothly: give it a beat to settle)
            float vx = m0.VisualCenter.x, want = SM.ColumnX(1) + cx;
            var b = SM.ColumnBounds(0); b.Encapsulate(SM.ColumnBounds(1)); b.Encapsulate(SM.ColumnBounds(2)); b.Encapsulate(m0.WorldBounds);
            Frame(b, 1.05f);
            yield return Shot("k6_moon_follow.png");
            yield return UntilBeat(SM.ColumnStart(2) + 2.5);
            float home = m0.MoonRideX, hx = m0.VisualCenter.x, heldX = SM.ColumnX(1) - SM.ColumnX(0);
            GlobalClock.Stop();
            Line(sb, Mathf.Abs(vx - want) < 0.05f && Mathf.Abs(home - heldX) < 0.05f,
                "playing: the Moon's platform is in line with the lit column mid-section (its tiles and cubes ride along); v7: after its section it holds at the section's last column",
                "column 1: Moon centre x " + F(vx) + " (column " + F(want) + "); after the section: offset " + F(home) + " (held " + F(heldX) + ", x " + F(hx) + ")");
        }
        Run(sb, "one Moon at column 1 only: its windows start at column 1 (no drums in column 0)", () =>
        {
            SM.RemoveMoon(1);
            SM.SetMoonColumn(0, 1);
            var cs = moonCubes(SM.Moons[0]); if (cs.Count == 0) return "no cubes";
            int b0 = Mathf.RoundToInt(SM.ColumnStart(1) / bpbG), bars = Mathf.RoundToInt(SM.TotalBeats / bpbG);
            foreach (var c in cs) { string e = barsOf(c, b0, bars); if (e != null) return e; }
            var at = new List<KeyBlock>(); SM.MoonsAt(0.5f, at);
            if (at.Count != 0) return "a Moon plays in column 0";
            return null;
        });
        {
            // and by ear: play column 0 into column 1 — no Moon event before column 1's start, some after it
            long logStart = Performance.LoggedTotal; int late0 = Synth.LateEvents, err0 = Synth.Errors;
            GlobalClock.Seek(0); GlobalClock.Play();
            double c1 = SM.ColumnStart(1);
            yield return UntilBeat(c1 + 2.5);
            double c1Dsp = GlobalClock.DspTimeOfBeat(c1);
            GlobalClock.Stop();
            int early = 0, later = 0;
            for (int i = 0; i < Performance.LoggedCount; i++)
            {
                if (Performance.LoggedTotal - Performance.LoggedCount + i < logStart) continue;
                var e = Performance.Logged(i);
                if (e.owner < 16 || ReferenceEquals(e.island, null) || !e.island.IsMoon) continue;
                if (e.onDsp < c1Dsp - 0.005) early++; else later++;
            }
            int late = Synth.LateEvents - late0, errs = Synth.Errors - err0;
            Line(sb, early == 0 && later > 0 && late == 0 && errs == 0, "by ear: no drum event in column 0, drums from column 1's downbeat on; Synth late / errors 0",
                "before column 1: " + early + ", after: " + later + ", late +" + late + " errors +" + errs);
        }
        Run(sb, "a Moon drag snaps its start column to the column it is in line with (the band lights while dragging), one History entry; Esc puts it back with none", () =>
        {
            var drag = PathManager.I != null ? PathManager.I.Drag : null;
            if (drag == null) return "no drag";
            var m = SM.Moons[0];
            float cx = KeyBlock.CenterOffsetOf(ProjectConfig.numInversions, 1).x;
            Vector3 start = m.Center;
            int h = History.UndoCount;
            drag.SimBegin(m, false, start);
            drag.SimMove(new Vector3(SM.ColumnX(3) + cx + 0.8f, 0f, start.z - 1.0f));
            int col = drag.MoonColumn, band = ColumnBands.RangeColumn;
            drag.SimRelease();
            if (col != 3 || band != 3) return "while dragging: column " + col + " band " + band;
            if (SM.MoonStartColumn(0) != 3 || History.UndoCount != h + 1) return "after the drop: column " + SM.MoonStartColumn(0) + " history +" + (History.UndoCount - h);
            if (Mathf.Abs(m.Center.x - (SM.ColumnX(3) + cx)) > 0.01f) return "not in line: " + F(m.Center.x);
            h = History.UndoCount; Vector3 before = new Vector3(m.px, 0f, m.pz);
            drag.SimBegin(m, false, m.Center);
            drag.SimMove(m.Center + new Vector3(-13f, 0f, 0f));
            drag.Cancel();
            if (History.UndoCount != h || Mathf.Abs(m.px - before.x) > 1e-3f || SM.MoonStartColumn(0) != 3) return "Esc: history +" + (History.UndoCount - h) + " column " + SM.MoonStartColumn(0);
            return null;
        });
        Run(sb, "structural edits keep a Moon on its part of the song: a column inserted in front moves its start column with it, removing that column hands it to the next; undo restores; AddMoon(column) starts there", () =>
        {
            int c0 = SM.MoonStartColumn(0);
            string chord = SM.AnchorOf(c0).assignedChord;
            var md = SongManager.CopyMeasure(SM.AnchorOf(0).ToData()); md.placed = false;
            int at = SM.PlaceIslandAsColumn(md, 0);
            if (at < 0 || SM.MoonStartColumn(0) != c0 + 1 || SM.AnchorOf(SM.MoonStartColumn(0)).assignedChord != chord) return "insert: column " + SM.MoonStartColumn(0);
            History.Undo();
            if (SM.MoonStartColumn(0) != c0) return "undo: " + SM.MoonStartColumn(0);
            int nc = SM.ColumnCount;
            SM.RemoveMeasure(SM.ColumnFirst(c0));
            int want = Mathf.Min(c0, SM.ColumnCount - 1);
            if (SM.MoonStartColumn(0) != want) return "remove: " + SM.MoonStartColumn(0) + " want " + want;
            History.Undo();
            if (SM.MoonStartColumn(0) != c0 || SM.ColumnCount != nc) return "undo remove: " + SM.MoonStartColumn(0);
            int j = SM.AddMoon(1);
            if (j < 0 || SM.MoonStartColumn(j) != 1 || SM.MoonEndColumn(j) != c0) return "AddMoon(1): start " + (j >= 0 ? SM.MoonStartColumn(j) : -1) + " end " + (j >= 0 ? SM.MoonEndColumn(j) : -1);
            var a = SM.Moons[j]; var b2 = a.WorldBounds; b2.Expand(ProjectConfig.IslandGap * 2f - 0.05f);
            foreach (var o in SM.Moons) if (o != a && b2.Intersects(o.WorldBounds)) return "the new Moon overlaps another";
            return null;
        });
        Run(sb, "MaxMoons (" + SongManager.MaxMoons + "): Moons fill up across the columns (several per column stack in the lane), none overlaps an island or another Moon, one more bounces", () =>
        {
            int col = 0;
            while (SM.Moons.Count < SongManager.MaxMoons) { if (SM.AddMoon(col) < 0) return "AddMoon refused at " + SM.Moons.Count; col = (col + 1) % SM.ColumnCount; }
            if (SM.AddMoon(0) >= 0 || SM.Moons.Count != SongManager.MaxMoons) return "the ninth was added";
            for (int x = 0; x < SM.Moons.Count; x++)
            {
                var bx = SM.Moons[x].WorldBounds; bx.Expand(ProjectConfig.IslandGap * 2f - 0.05f);
                foreach (var kb in SM.Islands) if (bx.Intersects(kb.WorldBounds)) return "moon " + x + " overlaps an island";
                for (int y = x + 1; y < SM.Moons.Count; y++) if (bx.Intersects(SM.Moons[y].WorldBounds)) return "moons " + x + " and " + y + " overlap";
            }
            Info(sb, "8 Moons: starts " + string.Join(",", Enumerable.Range(0, SM.Moons.Count).Select(q => SM.MoonStartColumn(q))) + " | tracks " + ColumnBands.MoonTrackCount);
            return null;
        });

        {
            var b = SM.SongBounds; Frame(b, 1.0f);
            yield return Wait(0.6f);
            yield return Shot("k6_moons8.png");
        }

        // ================================================================ F. the user's save
        Line(sb, Md5(SongIO.Path) == userMd5, "the user's save is untouched", "md5 " + Md5(SongIO.Path));

        Restore();
        LoadFixture();
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" passed, ").Append(fail).Append(" failed\n");
        Report = sb.ToString();
        Done = true;
        Flush(sb);
    }

    /// <summary>K's battery in one Play session: V6ChecksK.RunAll (AutoHand off), then — with <paramref name="older"/> — K's older suites
    /// V5ChecksK, V4ChecksK and V3ChecksB with PathManager.AutoHand = true (the pre-v6 suites draw by clicking tiles); progress in
    /// Captures/k6_battery.txt ("DONE" at the end).</summary>
    public static string RunBattery(bool older)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(BatteryRoutine(older, true));
        return "started";
    }

    /// <summary>Only K's older suites (V5ChecksK, V4ChecksK, V3ChecksB) with AutoHand = true (Captures/k6_battery.txt).</summary>
    public static string RunOlder()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(BatteryRoutine(true, false));
        return "started";
    }

    static IEnumerator BatteryRoutine(bool older, bool v6)
    {
        string path = Path.Combine(V2Checks.CapturePath, "k6_battery.txt");
        var log = new StringBuilder();
        Action<string> note = t => { log.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(t).Append('\n'); try { File.WriteAllText(path, log.ToString()); } catch (Exception) { } };
        float t0;
        if (v6)
        {
            note("V6ChecksK " + RunAll());
            t0 = Time.realtimeSinceStartup;
            while (!Done && Time.realtimeSinceStartup - t0 < 400f) yield return null;
            note("V6ChecksK " + (Done ? Report.Split('\n').FirstOrDefault(l => l.StartsWith("SUMMARY")) : "TIMEOUT"));
        }
        if (older)
        {
            Restore();
            PathManager.AutoHand = true;
            yield return null;
            note("V5ChecksK " + V5ChecksK.RunAll());
            t0 = Time.realtimeSinceStartup;
            while (!V5ChecksK.Done && Time.realtimeSinceStartup - t0 < 400f) yield return null;
            note("V5ChecksK " + (V5ChecksK.Done ? V5ChecksK.Report.Split('\n').FirstOrDefault(l => l.StartsWith("SUMMARY")) : "TIMEOUT") + Fails(V5ChecksK.Report));
            PathManager.AutoHand = true;
            note("V4ChecksK " + V4ChecksK.RunAll());
            t0 = Time.realtimeSinceStartup;
            while (!V4ChecksK.Done && Time.realtimeSinceStartup - t0 < 600f) yield return null;
            note("V4ChecksK " + (V4ChecksK.Done ? V4ChecksK.Report.Split('\n').FirstOrDefault(l => l.StartsWith("SUMMARY")) : "TIMEOUT") + Fails(V4ChecksK.Report));
            PathManager.AutoHand = true;
            V3ChecksB.Prepare();
            PathManager.AutoHand = true;
            note("V3ChecksB " + V3ChecksB.RunAll());
            t0 = Time.realtimeSinceStartup;
            while (!V3ChecksB.Done && Time.realtimeSinceStartup - t0 < 600f) yield return null;
            if (V3ChecksB.Done)
            {
                try { File.WriteAllText(Path.Combine(V2Checks.CapturePath, "k6_v3b_report.txt"), V3ChecksB.Report); } catch (Exception) { }
                int bp = 0, bf = 0; foreach (var l in V3ChecksB.Report.Split('\n')) { if (l.StartsWith("PASS")) bp++; else if (l.StartsWith("FAIL")) bf++; }
                note("V3ChecksB " + bp + " passed, " + bf + " failed (Captures/k6_v3b_report.txt)" + Fails(V3ChecksB.Report));
            }
            else note("V3ChecksB TIMEOUT");
        }
        Restore();
        note("DONE");
    }

    static string Fails(string report)
    {
        if (string.IsNullOrEmpty(report)) return "";
        var f = report.Split('\n').Where(l => l.StartsWith("FAIL")).Select(l => l.Length > 260 ? l.Substring(0, 260) : l).ToArray();
        return f.Length == 0 ? "" : "\n   " + string.Join("\n   ", f);
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "none";
        using (var md5 = System.Security.Cryptography.MD5.Create()) { var h = md5.ComputeHash(File.ReadAllBytes(path)); var s = new StringBuilder(); foreach (var x in h) s.Append(x.ToString("x2")); return s.ToString(); }
    }
}
