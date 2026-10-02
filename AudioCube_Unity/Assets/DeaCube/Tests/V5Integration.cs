using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v5 end-to-end run (integration, SPEC v5 §10): the title menu → "gallery" → a demo's card previews its hook → open it (the comic exit, the
/// song playing, its key kept, a repeated island riding its belt) → drag an island LEFT into an earlier column and into a new column between two
/// (one History entry each, playing on; undo) → the island header's repeat button (×2: the next column makes space, the island plays twice and rides
/// one slot per pass; every island of the column ×2 = the column plays twice; stopped = home; undo) → hear before placing (resting on a tile plays
/// it, stopped and while another column plays with the song ducked; drafting plays the run a click adds, the melody card) → the deck's vibe cards →
/// present (quicker, the camera alive) → Home → Continue restores it. Synth late / errors, console errors, the user's save. Captures i5_*.png.
/// Poll Done / Report (Captures/v5_integration_report.txt). Restores the saves and the tutorial prefs; never writes the user's save.
/// </summary>
public static class V5Integration
{
    public static bool Done = true;
    public static string Report = "";
    static StringBuilder sb;
    static int errors, pass, fail;
    static readonly List<string> errLines = new List<string>();
    static Dictionary<string, V3Fixes.FileSnap> saves;
    static readonly StringBuilder lateLog = new StringBuilder();
    static int lateMark;
    /// <summary>Late notes right after a seek WHILE PLAYING: by design the step the seek lands inside is fired at once (up to 30 ms after its start,
    /// so the note under the playhead is heard); they are counted apart from the run's own late notes.</summary>
    static int seekLate;
    static IEnumerator SeekPlaying(double beat)
    {
        int l = Synth.LateEvents;
        GlobalClock.Seek(beat);
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
        try { File.WriteAllText(Cap("v5_integration_report.txt"), Report + "...running\n"); } catch (Exception) { }
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); Report = sb.ToString(); }

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
    static IEnumerator Until(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
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
    static bool OnScreen(Vector3 s) => s.z > 0f && s.x > 40f && s.y > 90f && s.x < Screen.width - 40f && s.y < Screen.height - 90f;
    static Vector3 ScreenOf(TileInteraction t) { var cam = Camera.main; return cam != null && t != null ? cam.WorldToScreenPoint(t.Top) : Vector3.zero; }

    static IEnumerator Click(TileInteraction t)
    {
        Vector3 s = ScreenOf(t);
        if (PathManager.SimOnly) PathManager.SimPos = s;   // the frame's own pointer step stays on the clicked tile
        PathManager.I.SimPointer(s, true, true, false);
        yield return null;
        PathManager.I.SimPointer(s, false, false, true);
        yield return null;
    }
    /// <summary>The pointer rests on <paramref name="t"/> (PathManager.SimOnly: the real mouse is ignored, so it cannot jump the pointer away).</summary>
    static IEnumerator Hover(TileInteraction t, float seconds)
    {
        Vector3 s = ScreenOf(t);
        PathManager.SimOnly = true;
        PathManager.SimPos = s + new Vector3(24f, 18f, 0f);   // arrive from beside the tile: an entry the pointer made
        yield return null; yield return null;
        PathManager.SimPos = s;
        float e = 0f;
        while (e < seconds) { e += Time.unscaledDeltaTime; yield return null; }
    }

    static int FinalizedCubes() { int n = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0) n++; return n; }
    static int CountInCol(SongState st, int col) { int n = 0; foreach (var m in st.measures) if (m != null && m.col == col) n++; return n; }
    static int Columns(SongState st) { int c = 0; foreach (var m in st.measures) if (m != null) c = Mathf.Max(c, m.col + 1); return c; }
    static int RepeatSum(SongState st) { int r = 0; foreach (var m in st.measures) if (m != null) r += Mathf.Max(1, m.repeat); return r; }
    static bool Occupied(TileInteraction t) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.nodes.Contains(t)) return true; return false; }

    /// <summary>An island of column ≥ <paramref name="minCol"/> whose column holds at least two islands (so it survives the move), no group.</summary>
    static KeyBlock MovableFrom(int minCol)
    {
        var sm = SM;
        for (int c = minCol; c < sm.ColumnCount; c++)
        {
            var list = sm.ColumnIslands(c);
            if (list.Count < 2) continue;
            var kb = list[list.Count - 1];
            if (kb != null && !kb.IsMoon && kb.group == 0) return kb;
        }
        for (int c = minCol; c < sm.ColumnCount; c++) { var a = sm.AnchorOf(c); if (a != null && a.group == 0) return a; }
        return null;
    }

    /// <summary>A free tile of <paramref name="kb"/> on screen whose row has at least <paramref name="span"/> free-or-not tiles to its right.</summary>
    static TileInteraction FreeTile(KeyBlock kb, int span)
    {
        if (kb == null) return null;
        for (int z = 0; z < kb.rows; z++)
            for (int x = 0; x + span < kb.cols; x++)
            {
                var t = kb.GetTile(x, z); var u = kb.GetTile(x + span, z);
                if (t == null || u == null || Occupied(t) || Occupied(u)) continue;
                if (!OnScreen(ScreenOf(t)) || !OnScreen(ScreenOf(u))) continue;
                return t;
            }
        return null;
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
        Onboarding.Suppressed = true;   // the tutorial has its own suites; its bubbles would sit on the tiles hovered here

        // ---- 1. the title menu → gallery → a card previews its hook → open it
        if (!MainMenu.IsShown) MainMenu.Show();
        yield return Until(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return Wait(0.5f);
        var entries = Gallery.Entries;
        var gw = MainMenu.GalleryButton;
        Check(MainMenu.IsShown && entries.Count >= 6 && gw != null && gw.gameObject.activeInHierarchy, "the title menu shows \"gallery\" with the demo songs", "songs " + entries.Count);
        if (gw != null) gw.Invoke();
        yield return Until(() => MainMenu.ShelfOpen, 3f);
        yield return Wait(0.9f);
        var shelf = MainMenu.Shelf;
        Check(shelf != null && shelf.IsOpen && shelf.Cards.Count == entries.Count, "the shelf deals one vibe card per song", shelf != null ? shelf.Cards.Count + " cards" : "no shelf");
        // the demo to open: the first whose song repeats an island (the belt), else the first
        int pick = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var st = Gallery.LoadState(entries[i].id);
            bool rep = false; if (st != null) foreach (var m in st.measures) if (m != null && m.repeat > 1) rep = true;
            if (rep) { pick = i; break; }
        }
        var entry = entries.Count > 0 ? entries[pick] : null;
        if (shelf != null && entry != null) shelf.FocusCard(pick);
        yield return Wait(1.8f);
        int notes = shelf != null && shelf.Preview != null ? shelf.Preview.NotesScheduled : 0;
        Check(entry != null && shelf != null && shelf.PreviewId == entry.id && notes > 0, "focusing a card plays its hook", entry != null ? entry.id + ", notes " + notes : "no entry");
        yield return Shot("i5_01_shelf.png", captures);
        if (entry == null) { Finish(userMd5, prefStep, prefDone); yield break; }
        var stF = Gallery.LoadState(entry.id);
        Vector2 at = shelf != null && pick < shelf.Cards.Count ? shelf.Cards[pick].ScreenCenter : new Vector2(Screen.width * 0.5f, Screen.height * 0.4f);
        MainMenu.I.OpenFromGallery(entry, at);
        yield return Until(() => !MainMenu.IsShown && GlobalClock.IsPlaying, 8f);
        yield return Wait(1.2f);
        var sm = SM;
        Check(!MainMenu.IsShown && Gallery.CurrentId == entry.id && stF != null && sm.Islands.Count == stF.measures.Length && FinalizedCubes() == stF.cubes.Length && GlobalClock.IsPlaying,
              "opening the card loads that song into the world, playing", entry.id + ": islands " + sm.Islands.Count + "/" + (stF != null ? stF.measures.Length : -1) + ", cubes " + FinalizedCubes() + "/" + (stF != null ? stF.cubes.Length : -1) + ", columns " + sm.ColumnCount + ", " + GlobalClock.BPM.ToString("F0") + " bpm");
        Check(sm.HasSongKey && stF != null && sm.SongKey.tonic == stF.keyTonic && sm.SongKey.minor == stF.keyMinor, "the song keeps its own key", sm.SongKey.ToString());
        Info("after the open: late " + (Synth.LateEvents - late0) + " (last: " + Synth.LastLate + ")");
        yield return Shot("i5_02_world.png", captures);
        KeyBlock rider = null;
        foreach (var kb in sm.Islands) if (kb != null && kb.Passes > 1) { rider = kb; break; }
        if (rider != null)
        {
            int col = rider.column;
            yield return SeekPlaying(sm.PassStart(col, 1) + sm.PassLength(col) * 0.5f);
            yield return Wait(0.6f);
            Check(Mathf.Abs(rider.RideSlotShown - 1f) < 0.06f, "the demo's repeated island rides its belt: its second pass plays one slot along", "column " + col + ", passes " + sm.ColumnPasses(col) + ", slot " + rider.RideSlotShown.ToString("F2"));
            yield return Shot("i5_03_demo_belt.png", captures);
        }
        else Info("the opened demo repeats no island (the repeat section below lays a belt itself)");
        Mark("gallery");

        // ---- 2. drag an island LEFT into an earlier column, then into a new column between two (one History entry each, playing on)
        yield return SeekPlaying(0);
        var drag = pm.Drag;
        var mover = MovableFrom(2);
        if (mover != null && sm.ColumnCount >= 3)
        {
            var before = SongState.Capture();
            int n0 = CountInCol(before, 0), cols0 = Columns(before), h0 = History.UndoCount, cubes0 = before.cubes.Length;
            var a0 = sm.AnchorOf(0);
            Vector3 target = new Vector3(a0.Center.x + 0.3f, 0f, a0.Center.z + 0.6f);
            drag.SimBegin(mover, false, mover.Center);
            yield return null;
            drag.SimMove(target); yield return null; drag.SimMove(target);
            yield return Wait(0.25f);
            yield return Shot("i5_04_drag_left.png", captures);
            drag.SimRelease();
            yield return Wait(0.9f);
            var after = SongState.Capture();
            Check(CountInCol(after, 0) == n0 + 1 && after.measures.Length == before.measures.Length && after.cubes.Length == cubes0 && History.UndoCount == h0 + 1 && GlobalClock.IsPlaying,
                  "dragging an island LEFT drops it into an earlier column (one History entry, cubes kept, playing on)",
                  "column 0 islands " + n0 + " → " + CountInCol(after, 0) + ", columns " + cols0 + " → " + Columns(after) + ", undo " + h0 + " → " + History.UndoCount);
            History.Undo();
            yield return Wait(0.8f);
            var undone = SongState.Capture();
            Check(CountInCol(undone, 0) == n0 && Columns(undone) == cols0, "undo puts it back in its column", "column 0 islands " + CountInCol(undone, 0) + ", columns " + Columns(undone));
            // into the gap between column 0 and column 1 → a new column there
            mover = MovableFrom(2);
            if (mover != null)
            {
                int cols1 = sm.ColumnCount, h1 = History.UndoCount;
                var b0 = sm.ColumnBounds(0); var b1 = sm.ColumnBounds(1);
                float gapX = (b0.max.x + b1.min.x) * 0.5f;
                Vector3 slot = new Vector3(gapX, 0f, sm.AnchorOf(0).Center.z);
                drag.SimBegin(mover, false, mover.Center);
                yield return null;
                drag.SimMove(slot); yield return null; drag.SimMove(slot);
                yield return Wait(0.25f);
                yield return Shot("i5_05_drag_newcol.png", captures);
                drag.SimRelease();
                yield return Wait(0.9f);
                Check(sm.ColumnCount == cols1 + 1 && History.UndoCount == h1 + 1 && GlobalClock.IsPlaying, "dropping it between two columns makes a new column there (earlier in the song)",
                      "columns " + cols1 + " → " + sm.ColumnCount + ", gap x " + gapX.ToString("F1"));
                History.Undo();
                yield return Wait(0.8f);
                Check(sm.ColumnCount == cols1, "undo closes the new column again", "columns " + sm.ColumnCount);
            }
        }
        else Check(false, "the demo has an island to drag (column ≥ 2)", "columns " + sm.ColumnCount);
        Mark("drag");

        // ---- 3. repeat: the header's button (×2): a belt; the next column makes space; the island plays twice and rides; all ×2 = twice
        int rc = -1;
        for (int c = 0; c + 1 < sm.ColumnCount; c++) { if (sm.ColumnPasses(c) == 1 && sm.ColumnIslands(c).Count >= 2) { rc = c; break; } }
        if (rc < 0) for (int c = 0; c + 1 < sm.ColumnCount; c++) if (sm.ColumnPasses(c) == 1) { rc = c; break; }
        if (rc >= 0 && IslandHeader.I != null)
        {
            var kbA = sm.ColumnIslands(rc)[0];
            float x1 = sm.ColumnX(rc + 1), tot0 = GlobalClock.TotalBeats, pass0 = sm.PassLength(rc);
            int h2 = History.UndoCount;
            IslandHeader.Show(kbA);
            yield return Wait(0.35f);
            var rb = IslandHeader.I.Repeat;
            if (rb != null && rb.onClick != null) rb.onClick();
            yield return Wait(1.0f);
            kbA = sm.ColumnIslands(rc)[0];
            int w2 = 0, w1 = 0, oth = 0;
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || !c.isFinalized || c.rider || c.IsOnMoon) continue;
                if (c.Island == kbA) { if (c.windows.Count == 2) w2++; else w1 += 1000; }
                else if (c.Island != null && c.Island.column == rc) { oth++; if (c.windows.Count == 1) w1++; }
            }
            Check(kbA.Passes == 2 && sm.ColumnPasses(rc) == 2 && sm.ColumnX(rc + 1) > x1 + 0.9f * KeyBlock.SlotPitch && Mathf.Abs(GlobalClock.TotalBeats - (tot0 + pass0)) < 0.01f && History.UndoCount == h2 + 1,
                  "the header's repeat ×2 lays a belt: the next column moves over to make space, the song grows by one pass",
                  "column " + rc + ", next x " + x1.ToString("F1") + " → " + sm.ColumnX(rc + 1).ToString("F1") + " (slot " + KeyBlock.SlotPitch.ToString("F1") + "), beats " + tot0 + " → " + GlobalClock.TotalBeats);
            Check(w2 > 0 && w1 == oth, "the repeating island's cubes play both passes, its column-mates play once and rest", "cubes ×2 " + w2 + ", others one window " + w1 + "/" + oth);
            if (!GlobalClock.IsPlaying) GlobalClock.Play();
            yield return SeekPlaying(sm.PassStart(rc, 1) + sm.PassLength(rc) * 0.5f);
            yield return Wait(0.6f);
            kbA = sm.ColumnIslands(rc)[0];
            Check(Mathf.Abs(kbA.RideSlotShown - 1f) < 0.06f && Mathf.Abs(kbA.BeltOffset.x - KeyBlock.SlotPitch) < 0.2f, "while playing, the second pass plays one slot along the belt", "slot " + kbA.RideSlotShown.ToString("F2") + ", x offset " + kbA.BeltOffset.x.ToString("F2"));
            yield return Shot("i5_06_belt_ride.png", captures);
            // every island of the column ×2 = the column plays twice
            int clicks = 1;
            var others = sm.ColumnIslands(rc);
            IslandHeader.Hide();
            yield return Wait(0.3f);
            for (int k = 1; k < others.Count; k++)
            {
                // the header's own path was exercised on the first island; its column-mates go through the same op directly
                var kb = sm.ColumnIslands(rc)[k];
                sm.SetRepeat(sm.Islands.IndexOf(kb), 2);
                clicks++;
                yield return Wait(0.8f);
            }
            bool allTwice = true; foreach (var kb in sm.ColumnIslands(rc)) if (kb.Passes != 2) allTwice = false;
            int twiceCubes = 0, cubesIn = 0;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && !c.rider && !c.IsOnMoon && c.Island != null && c.Island.column == rc) { cubesIn++; if (c.windows.Count == 2) twiceCubes++; }
            Check(allTwice && Mathf.Abs(sm.ColumnLength(rc) - 2f * sm.PassLength(rc)) < 0.01f && twiceCubes == cubesIn, "every island of the column at ×2: the column simply plays twice",
                  "islands " + sm.ColumnIslands(rc).Count + ", length " + sm.ColumnLength(rc) + " = 2 × " + sm.PassLength(rc) + ", cubes twice " + twiceCubes + "/" + cubesIn);
            GlobalClock.Stop();
            yield return Wait(0.8f);
            float maxSlot = 0f; foreach (var kb in sm.ColumnIslands(rc)) maxSlot = Mathf.Max(maxSlot, Mathf.Abs(kb.RideSlotShown));
            Check(maxSlot < 0.02f, "stopped, the islands rest at the head of their belts", "max slot " + maxSlot.ToString("F3"));
            for (int k = 0; k < clicks; k++) { History.Undo(); yield return Wait(0.5f); }
            Check(sm.ColumnPasses(rc) == 1, "undo takes the belts away again", "passes " + sm.ColumnPasses(rc));
        }
        else Check(false, "a column to repeat", "columns " + sm.ColumnCount);
        Mark("repeat");

        // ---- 4. hear before placing: resting on a tile plays it (stopped; while another column plays, ducked); drafting plays the run
        GlobalClock.Stop();
        if (OrbitCamera.I != null) OrbitCamera.I.FocusMeasure(0, true);
        yield return Wait(1.4f);
        var h = sm.AnchorOf(0);
        var t0 = FreeTile(h, 3);
        if (t0 != null)
        {
            int ia0 = PathManager.IdleAuditionCount;
            yield return Hover(t0, 0.45f);
            Check(PathManager.IdleAuditionCount == ia0 + 1, "resting on a tile (stopped) plays its note once", "idle auditions +" + (PathManager.IdleAuditionCount - ia0));
            // playing another column: the tile still sounds, the song ducks under it
            var off = h.GetTile(t0.gridX, Mathf.Min(h.rows - 1, t0.gridZ + 1));
            if (off == null || Occupied(off) || off == t0) off = h.GetTile(Mathf.Min(h.cols - 1, t0.gridX + 1), t0.gridZ);
            // v6: starting play inside notes fires the notes under the playhead at once (the seek catch-up of a start): counted apart like a seek
            int lp = Synth.LateEvents;
            GlobalClock.Seek(sm.ColumnCount > 2 ? sm.ColumnStart(2) + 0.25f : 0f); GlobalClock.Play();
            for (int f = 0; f < 4; f++) yield return null;
            seekLate += Synth.LateEvents - lp;
            yield return Wait(0.3f);
            int ia1 = PathManager.IdleAuditionCount, d0 = Synth.DuckTriggers;
            yield return Hover(off, 0.5f);
            Check(sm.LitColumn != 0 && PathManager.IdleAuditionCount == ia1 + 1 && Synth.DuckTriggers > d0, "while another column plays, resting on a tile still plays it, the song ducking under it",
                  "lit column " + sm.LitColumn + ", auditions +" + (PathManager.IdleAuditionCount - ia1) + ", ducks +" + (Synth.DuckTriggers - d0));
            GlobalClock.Stop();
            yield return Wait(0.4f);
            // drafting: a click starts the hologram, hovering three tiles on plays the run a click would add
            yield return Click(t0);
            yield return Wait(0.3f);
            var far = h.GetTile(t0.gridX + 3, t0.gridZ);
            int ra0 = PathManager.RunAuditionCount;
            yield return Hover(far, 0.6f);
            int runN = pm.LastAuditionTiles != null ? pm.LastAuditionTiles.Count : 0;
            Check(pm.IsDrawing && PathManager.RunAuditionCount >= ra0 + 1 && runN == 3, "drafting: hovering three tiles on plays the run a click would add", "drawing " + pm.IsDrawing + ", runs +" + (PathManager.RunAuditionCount - ra0) + ", notes " + runN);
            Check(MelodyLine.Shown, "the melody card draws the tune while drafting");
            yield return Shot("i5_07_draft_run.png", captures);
            pm.CancelPath(false);
            yield return Wait(0.4f);
            FocusLoop.Dismiss();
            PathManager.SimOnly = false;
        }
        else Check(false, "a free tile on screen to hover", "island 0");
        Mark("hear");

        // ---- 5. the deck: every card wears its chord's vibe; the home sticker marks the key's home chord
        IslandTray.Open();
        yield return Wait(1.0f);
        int ok = 0, n = 0, homes = 0;
        if (IslandTray.I != null)
            foreach (var c in IslandTray.I.Cards)
            {
                if (c == null || c.data == null) continue;
                n++;
                var semis = MusicTheory.EffectiveSemis(c.data.semitones, 0, SongManager.Climate);
                bool home = Vibe.IsHome(c.data.chordRootMIDI);
                if (home) homes++;
                if (c.vibe == Vibe.Of(semis) && c.home == home && c.glyph == Vibe.Icon(c.vibe)) ok++;
            }
        yield return Shot("i5_08_deck.png", captures);
        Check(n > 0 && ok == n && homes > 0, "the deck's cards wear their chords' vibes; the home sticker is on the key's home chord", ok + "/" + n + " cards, " + homes + " home");
        IslandTray.Close();
        yield return Wait(0.5f);

        // ---- 6. present: quicker, the camera alive
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Presenter.Enter();
        yield return Until(() => Presenter.Active, 3f);
        yield return Wait(1.5f);
        var cam = Camera.main;
        var ys = new List<float>(); var xs = new List<float>(); var rolls = new List<float>(); var ts = new List<float>();
        float tt = 0f;
        while (tt < 3f && cam != null)
        {
            ys.Add(cam.transform.position.y); xs.Add(cam.transform.position.x);
            float r = cam.transform.eulerAngles.z; if (r > 180f) r -= 360f; rolls.Add(r); ts.Add(Time.unscaledTime);
            tt += Time.unscaledDeltaTime; yield return null;
        }
        var sea = Presenter.Sea;
        yield return Shot("i5_09_present.png", captures);
        float desc = ys.Count > 1 ? ys[0] - ys[ys.Count - 1] : 0f, rise = 0f;
        for (int i = 1; i < ys.Count; i++) rise = Mathf.Max(rise, ys[i] - ys[i - 1]);
        float rMin = 999f, rMax = -999f, xMin = 999f, xMax = -999f;
        foreach (var r in rolls) { rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r); }
        foreach (var x in xs) { xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x); }
        Check(Presenter.Active && sea != null && desc > 0.05f && rise < 0.06f, "present (v9): the camera falls slowly and steadily with the song",
              "fell " + desc.ToString("F2") + " u over " + ys.Count + " frames, the most it rose in a frame " + rise.ToString("F3") + " u");
        Check(xMax - xMin > 0.15f, "present: the camera moves with the song along its columns (it is not a constant elevator)", "roll " + rMin.ToString("F2") + ".." + rMax.ToString("F2") + "°, x " + xMin.ToString("F2") + ".." + xMax.ToString("F2"));
        Presenter.Exit();
        yield return Until(() => !Presenter.Active, 4f);
        yield return Wait(0.6f);
        Check(!Presenter.Active && !WorldInput.WorldLocked, "present exits back to the world");
        Mark("present");

        // ---- 7. Home (autosave) → Continue: columns, repeats, key and cubes survive
        GlobalClock.Stop();
        int ri = -1; for (int i = 0; i < sm.Islands.Count; i++) if (sm.Islands[i] != null && !sm.Islands[i].IsMoon && sm.Islands[i].Passes == 1) { ri = i; break; }
        if (ri >= 0) { sm.SetRepeat(ri, 3); yield return Wait(0.6f); }
        var b4 = SongState.Capture();
        MainMenu.Show();
        yield return Until(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return Wait(0.4f);
        MainMenu.ContinueButton.Invoke();
        yield return Until(() => !MainMenu.IsShown, 8f);
        yield return Wait(1.0f);
        var af = SongState.Capture();
        Check(!MainMenu.IsShown && af.measures.Length == b4.measures.Length && Columns(af) == Columns(b4) && RepeatSum(af) == RepeatSum(b4) && af.keyTonic == b4.keyTonic && af.keyMinor == b4.keyMinor && af.cubes.Length == b4.cubes.Length,
              "Home then Continue restores the song: columns, repeats, key, cubes", "islands " + b4.measures.Length + "/" + af.measures.Length + ", columns " + Columns(b4) + "/" + Columns(af) + ", repeats " + RepeatSum(b4) + "/" + RepeatSum(af) + ", key " + b4.keyTonic + "/" + af.keyTonic + ", cubes " + b4.cubes.Length + "/" + af.cubes.Length);
        yield return Shot("i5_10_continue.png", captures);
        Mark("continue");
        // a screenshot stalls the main thread for a few hundred ms while the song plays (the cubes cannot queue their notes meanwhile): with
        // captures on, late notes are reported, not failed
        if (captures) Info("synth over the run (captures on: each screenshot stalls the main thread): " + Synth.Stats() + " | late by section: " + (lateLog.Length > 0 ? lateLog.ToString() : "none"));
        else Check(Synth.LateEvents - seekLate == late0 && Synth.Errors == err0, "synth: no late notes and no errors over the whole run (the note under a seek while playing is fired at once: counted apart)",
                   Synth.Stats() + " | seek catch-ups " + seekLate + " | late by section: " + (lateLog.Length > 0 ? lateLog.ToString() : "none"));
        Check(Synth.Errors == err0, "synth: no errors over the whole run", Synth.Stats());
        Finish(userMd5, prefStep, prefDone);
    }

    static void Finish(string userMd5, int prefStep, int prefDone)
    {
        Application.logMessageReceived -= OnLog;
        try { PathManager.SimOnly = false; CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit(); FocusLoop.Dismiss(); GlobalClock.Stop(); if (IslandTray.IsOpen) IslandTray.Close(); IslandHeader.Hide(); } catch (Exception) { }
        Onboarding.Suppressed = false;
        V3Fixes.RestoreSaves(saves);
        PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep); PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone); PlayerPrefs.Save();
        Check(Md5(SongIO.Path) == userMd5, "the user's save is untouched", userMd5);
        Check(errors == 0, "no console errors or exceptions during the run", errors + (errLines.Count > 0 ? ": " + string.Join(" || ", errLines) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v5_integration_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }
}
