using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package B (islands) Play-mode verification, SPEC v3 §3: <c>V3ChecksB.RunAll()</c> starts a coroutine; poll <c>V3ChecksB.Done</c> and read
/// <c>V3ChecksB.Report</c> (numbered PASS/FAIL lines). Covers the fresh song + deck, the tray (cards, click, ghost free / insert / blocked,
/// tools), merge east / west (order, touching platforms, ids, cubes, windows, History), undo / redo and save / load round trips, split,
/// group drag, tear-out, the magnetic merge zone, the seam (no cable, low comet glide), the island camera (follow off, still while
/// playing, zero-lag pan, inertia, zoom toward the cursor, Tab, WASD, home) and harmonic safety while merging during playback.
/// Captures (Captures/): b_tray.png, b_ghost.png, b_merge.png, b_split.png. Never writes the user's save.
/// v4 (package K): the ghost checks follow the column slots (STACK / INSERT / MERGE / BLOCKED instead of free sea / cable / overlap) and the
/// duplicate tool lands in a new column after the focused island's glued run; everything else is unchanged (one-island columns behave as v3).
/// </summary>
public static class V3ChecksB
{
    public static string Report = "";
    public static bool Done;
    static int num;
    static readonly List<string> events = new List<string>();
    public static string ScratchSave => Path.Combine(Application.temporaryCachePath, "v3checksb_roundtrip.json");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" B").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
    }

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

    /// <summary>SPEC v3 test preamble: no menu, no prompt, no tutorial.</summary>
    public static void Prepare()
    {
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray");
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        WorldInput.Unlock("prompt");
    }

    public static string RunAll()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(AllRoutine());
        return "started";
    }

    static SongManager SM => SongManager.I;
    static float W => KeyBlock.IslandWidth;

    static bool Clear(KeyBlock kb, float margin = 1.4f)
    {
        var b = kb.WorldBounds; b.Expand(margin * 2f);
        var tight = kb.WorldBounds; tight.Expand(-0.04f);
        foreach (var o in SM.Islands)
        {
            if (o == null || o == kb || o.group != 0) continue;
            // v7 (SPEC v7 §13.2): the columns of a section touch by design — a neighbour there only must not OVERLAP
            bool touching = Mathf.Abs(o.column - kb.column) == 1 && SM.Touching(Mathf.Min(o.column, kb.column));
            if (touching ? tight.Intersects(o.WorldBounds) : b.Intersects(o.WorldBounds)) return false;
        }
        foreach (var o in SM.Moons) if (o != null && b.Intersects(o.WorldBounds)) return false;
        return true;
    }

    static string Chords() { var s = new StringBuilder(); foreach (var kb in SM.Islands) s.Append(kb.assignedChord).Append(' '); return s.ToString().Trim(); }

    static Dictionary<string, int> CubesPerChord()
    {
        var d = new Dictionary<string, int>();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.IsOnMoon || c.rider || c.Island == null) continue;
            string k = c.Island.assignedChord + "#" + SM.Islands.IndexOf(c.Island);
            k = c.Island.assignedChord;
            int v; d.TryGetValue(k, out v); d[k] = v + 1;
        }
        return d;
    }

    static string SameCounts(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        foreach (var kv in a) { int v; if (!b.TryGetValue(kv.Key, out v) || v != kv.Value) return "cubes on " + kv.Key + ": " + kv.Value + " -> " + v; }
        foreach (var kv in b) if (!a.ContainsKey(kv.Key)) return "cubes appeared on " + kv.Key;
        return null;
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    static IEnumerator Shot(string file, bool hud)
    {
        if (hud) V2Checks.HudOnCamera(true);
        yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
        if (hud) V2Checks.HudOnCamera(false);
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        Prepare();
        events.Clear();
        Action<string> onEv = e => events.Add(e);
        Onboarding.OnEvent += onEv;
        yield return null;

        // ================================================================ theory (pure)
        Run(sb, "MusicTheory: key, 7 diatonic sevenths (roots 55..66), next ranking, borrowed colours, minor mirror", () =>
        {
            var c = new MusicTheory.SongKey(0, false);
            var set = MusicTheory.DiatonicSet(c);
            string[] want = { "Cmaj7", "Dm7", "Em7", "Fmaj7", "G7", "Am7", "Bsus4" };
            for (int i = 0; i < 7; i++) if (set[i].chordKey != want[i]) return "C major degree " + i + " = " + set[i].chordKey;
            foreach (var m in set) if (m.chordRootMIDI < 55 || m.chordRootMIDI > 66) return "root out of 55..66: " + m.chordRootMIDI;
            var nx = MusicTheory.SuggestNext(c, 60, 4);
            if (nx[0].chordKey != "Fmaj7" || nx[1].chordKey != "Am7" || nx[2].chordKey != "G7" || nx[3].chordKey != "Dm7") return "after I: " + nx[0].chordKey + "," + nx[1].chordKey + "," + nx[2].chordKey + "," + nx[3].chordKey;
            var afterV = MusicTheory.SuggestNext(c, 67, 2);
            if (afterV[0].chordKey != "Cmaj7" || afterV[1].chordKey != "Am7") return "after V";
            var b = MusicTheory.Borrowed(c);
            if (b[0].chordKey != "A#7" || b[1].chordKey != "Fm7" || b[2].chordKey != "G#maj7") return "borrowed " + b[0].chordKey + "," + b[1].chordKey + "," + b[2].chordKey;
            var am = new MusicTheory.SongKey(9, true);
            var ms = MusicTheory.DiatonicSet(am);
            if (ms[0].chordKey != "Am7" || ms[1].chordKey != "Bsus4" || ms[2].chordKey != "Cmaj7" || ms[6].chordKey != "G7") return "A minor " + ms[0].chordKey + "," + ms[1].chordKey + "," + ms[2].chordKey + "," + ms[6].chordKey;
            var mb = MusicTheory.Borrowed(am);
            if (mb[0].chordKey != "E7" || mb[1].chordKey != "D7" || mb[2].chordKey != "A#maj7") return "minor borrowed";
            var k2 = MusicTheory.KeyOf(57, new[] { 0, 3, 7, 10 });
            if (k2.tonic != 9 || !k2.minor) return "KeyOf Am7 = " + k2;
            var off = MusicTheory.SuggestNext(c, 61, 4);   // C#: off the scale -> I, IV, V, vi
            if (off[0].chordKey != "Cmaj7" || off[1].chordKey != "Fmaj7" || off[2].chordKey != "G7" || off[3].chordKey != "Am7") return "off-key ranking";
            return null;
        });

        Run(sb, "NormalizeGroups: runs of one dissolve, a reused id renumbers, members laid out edge to edge (px + W, same pz), idempotent", () =>
        {
            Func<int, float, float, MeasureState> M = (g, x, z) => new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7, 11 }, bars = 1, group = g, px = x, pz = z, placed = true };
            var ms = new[] { M(3, 0f, 0f), M(3, 20f, 5f), M(0, 40f, 0f), M(3, 60f, 0f), M(7, 80f, 1f), M(7, 0f, 0f), M(7, 0f, 0f), M(9, 5f, 5f) };
            bool changed = SongManager.NormalizeGroups(ms);
            if (!changed) return "no change reported";
            if (ms[0].group != 3 || ms[1].group != 3) return "run 0-1 id " + ms[0].group + "," + ms[1].group;
            if (Mathf.Abs(ms[1].px - W) > 1e-4f || Mathf.Abs(ms[1].pz) > 1e-4f) return "member 1 at " + ms[1].px + "," + ms[1].pz;
            if (ms[3].group != 0) return "a lone 3 after a gap must dissolve: " + ms[3].group;
            if (ms[4].group != 7 || ms[5].group != 7 || ms[6].group != 7) return "run 4-6 id";
            if (Mathf.Abs(ms[5].px - (80f + W)) > 1e-4f || Mathf.Abs(ms[6].px - (80f + 2f * W)) > 1e-4f || Mathf.Abs(ms[6].pz - 1f) > 1e-4f) return "run 4-6 layout";
            if (ms[7].group != 0) return "single 9 not dissolved";
            var again = new[] { M(2, 0f, 0f), M(2, 0f, 0f), M(0, 30f, 0f), M(2, 50f, 0f), M(2, 0f, 0f) };
            SongManager.NormalizeGroups(again);
            if (again[3].group == again[0].group || again[3].group == 0 || again[4].group != again[3].group) return "a second run with the same id was not renumbered: " + again[0].group + "/" + again[3].group;
            if (SongManager.NormalizeGroups(again)) return "not idempotent";
            return null;
        });

        // ================================================================ fresh song + tray
        GlobalClock.Stop();
        var song = MusicTheory.RandomSong(4242, "b fresh 4242");
        int n = song.measures.Length;
        var names = new List<string>(); foreach (var m in song.measures) names.Add(m.chordKey);
        Run(sb, "StartFreshSong: island 0 at the origin + the demo Moon (3 Moon cubes, no Keys/Bass), stopped, deck = measures[1..], History reset, SongStarted", () =>
        {
            events.Clear();
            SM.StartFreshSong(song);
            if (SM.Islands.Count != 1) return "islands " + SM.Islands.Count;
            if (SM.Moons.Count != 1) return "moons " + SM.Moons.Count;
            int cubes = 0, moon = 0; foreach (var c in SequenceMaster.Cubes) if (c != null) { cubes++; if (c.IsOnMoon) moon++; }
            if (cubes != 3 || moon != 3) return "cubes " + cubes + " (moon " + moon + ")";
            if (GlobalClock.IsPlaying) return "playing";
            if (SM.Islands[0].assignedChord != names[0]) return "island 0 " + SM.Islands[0].assignedChord + " want " + names[0];
            var c0 = SM.Islands[0].Center; if (new Vector2(c0.x, c0.z).magnitude > 0.01f) return "island 0 centre " + c0;
            if (IslandTray.DeckCount != n - 1) return "deck " + IslandTray.DeckCount + " want " + (n - 1);
            var deck = IslandTray.Deck; for (int i = 0; i < deck.Length; i++) if (deck[i].chordKey != names[i + 1]) return "deck order at " + i;
            if (History.UndoCount != 1 || History.RedoCount != 0) return "history " + History.UndoCount + "/" + History.RedoCount;
            if (!events.Contains(Onboarding.Ev.SongStarted)) return "no SongStarted";
            if (OrbitCamera.I == null) return "no camera";
            if (OrbitCamera.I.followPlayhead) return "follow on by default";
            var cam = Camera.main;
            foreach (var kb in new[] { SM.Islands[0], SM.Moons[0] })
            {
                var vp = cam.WorldToViewportPoint(kb.Center);
                if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return "not framed: " + kb.name + " at viewport " + vp;
            }
            return null;
        });

        Run(sb, "tray: Next 4 (deck first with order dots), Key 7, Colour 3; hint targets tray / tray.next0 / tray.wand / tray.button; TrayOpened", () =>
        {
            events.Clear();
            IslandTray.Open();
            var t = IslandTray.I; if (t == null) return "no tray";
            t.RebuildNow();
            if (!IslandTray.IsOpen) return "not open";
            if (t.CountIn(0) != 4 || t.CountIn(1) != 7 || t.CountIn(2) != 3) return "cards " + t.CountIn(0) + "/" + t.CountIn(1) + "/" + t.CountIn(2);
            var deck = IslandTray.Deck;
            for (int i = 0; i < 3 && i < deck.Length; i++) if (t.Cards[i].deckIndex != i || !MusicTheory.SameChord(t.Cards[i].data, deck[i])) return "Next card " + i + " is not deck[" + i + "]";
            foreach (var id in new[] { "tray", "tray.next0", "tray.wand", "tray.button" }) if (!Hints.Has(id)) return "hint " + id + " missing";
            if (!events.Contains(Onboarding.Ev.TrayOpened)) return "no TrayOpened";
            return null;
        });
        yield return Wait(0.6f);
        yield return Shot("b_tray.png", true);
        Run(sb, "capture b_tray.png (HUD drawn through the camera)", () => File.Exists(Path.Combine(V2Checks.CapturePath, "b_tray.png")) ? null : "missing");

        Run(sb, "card hover: stopped = an arpeggio audition (Synth.Preview); playing = a tick + a pulse only (harmonic safety)", () =>
        {
            var t = IslandTray.I; t.RebuildNow();
            int a0 = t.AuditionCount, q0 = t.QuietHoverCount;
            GlobalClock.Stop();
            t.SimHover(1);
            if (t.AuditionCount != a0 + 1 || t.QuietHoverCount != q0) return "stopped: auditions +" + (t.AuditionCount - a0) + " quiet +" + (t.QuietHoverCount - q0);
            GlobalClock.Play();
            t.SimHover(2);
            bool ok = t.AuditionCount == a0 + 1 && t.QuietHoverCount == q0 + 1;
            GlobalClock.Stop(); GlobalClock.Seek(0);
            return ok ? null : "playing: auditions +" + (t.AuditionCount - a0) + " quiet +" + (t.QuietHoverCount - q0);
        });

        Run(sb, "card click: placed right after the focused island (song order, MeasureStarts), deck -1, one History entry, IslandPlaced", () =>
        {
            events.Clear();
            var t = IslandTray.I; t.RebuildNow();
            UIManager.I.SelectMeasure(0, false);
            int h = History.UndoCount, d = IslandTray.DeckCount;
            var md = t.Cards[0].data;
            int at = t.SimClick(0);
            if (at != 1) return "placed at " + at;
            if (SM.Islands.Count != 2) return "islands " + SM.Islands.Count;
            if (!MusicTheory.SameChord(SM.Islands[1].ToData(), md)) return "chord " + SM.Islands[1].assignedChord;
            if (IslandTray.DeckCount != d - 1) return "deck " + IslandTray.DeckCount;
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (Mathf.Abs(SM.MeasureStarts[1] - SM.Islands[0].LengthBeats) > 1e-4f) return "MeasureStarts " + SM.MeasureStarts[1];
            if (!Clear(SM.Islands[1])) return "overlaps";
            if (!events.Contains(Onboarding.Ev.IslandPlaced)) return "no IslandPlaced";
            return null;
        });

        Vector3 drop = Vector3.zero; IslandGhost ghost = null;
        // v4: the ghost snaps to column slots (the v3 free-sea / cable / overlap states became STACK / INSERT / BLOCKED)
        Run(sb, "ghost over column 0 (behind island 0): STACK in the empty slot LaneGap above island 0, the world locked by the tray", () =>
        {
            var t = IslandTray.I; t.RebuildNow();
            var i0 = SM.Islands[0];
            drop = new Vector3(i0.Center.x + 0.4f, 0f, i0.BackEdge + 4f);
            ghost = t.SimDragStart(4);   // the first Key card
            if (ghost == null) return "no ghost";
            t.SimDragAt(drop);
            if (ghost.State != IslandGhost.Mode.Stack) return "state " + ghost.State;
            if (ghost.Column != i0.column) return "column " + ghost.Column;
            float wantZ = i0.BackEdge + ProjectConfig.LaneGap + KeyBlock.EdgeInset;
            if (Mathf.Abs(ghost.Root.z - wantZ) > 0.01f || Mathf.Abs(ghost.Root.x - i0.px) > 0.01f) return "slot root " + ghost.Root + " want x " + i0.px + " z " + wantZ;
            if (!WorldInput.IsLockedBy("tray")) return "world not locked";
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(new Bounds(new Vector3(i0.Center.x, 0f, i0.Center.z + 4f), new Vector3(24f, 3f, 20f)), 0.1f, true);
            return null;
        });
        yield return Wait(0.35f);
        yield return Shot("b_ghost.png", true);
        Run(sb, "ghost release on the STACK slot: a second island in column 0 (same start), LaneGap behind island 0, one History entry, lock released", () =>
        {
            var t = IslandTray.I;
            int count = SM.Islands.Count, cols = SM.ColumnCount, h = History.UndoCount;
            var md = ghost != null ? ghost.Data : null;
            int at = t.SimDragRelease();
            if (at != 1) return "placed at " + at;
            if (SM.Islands.Count != count + 1 || SM.ColumnCount != cols) return "islands " + SM.Islands.Count + " columns " + SM.ColumnCount;
            if (md != null && !MusicTheory.SameChord(SM.Islands[at].ToData(), md)) return "chord";
            var a = SM.Islands[0]; var b = SM.Islands[1];
            if (b.column != a.column || Mathf.Abs(SM.MeasureStarts[1] - SM.MeasureStarts[0]) > 1e-4f) return "not in column 0: column " + b.column + " start " + SM.MeasureStarts[1];
            if (Mathf.Abs(b.FrontEdge - (a.BackEdge + ProjectConfig.LaneGap)) > 0.01f || Mathf.Abs(b.px - a.px) > 0.01f) return "lane: front " + b.FrontEdge + " vs " + (a.BackEdge + ProjectConfig.LaneGap);
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (WorldInput.WorldLocked) return "still locked: " + WorldInput.Describe();
            if (!File.Exists(Path.Combine(V2Checks.CapturePath, "b_ghost.png"))) return "b_ghost.png missing";
            return null;
        });

        Run(sb, "ghost insert: in the gap between columns 0 and 1 (an empty lane) -> INSERT, the gap preview on segment 0, a new column at position 1", () =>
        {
            var t = IslandTray.I; t.RebuildNow();
            float e = KeyBlock.EdgeInset;
            float bx = (SM.ColumnX(0) - e + W + SM.ColumnX(1) - e) * 0.5f;
            var b0 = SM.ColumnBounds(0); b0.Encapsulate(SM.ColumnBounds(1));
            string c1 = SM.AnchorOf(1).assignedChord;
            var g = t.SimDragStart(5);
            if (g == null) return "no ghost";
            Vector3 p = new Vector3(bx, 0f, b0.max.z + 9f);
            t.SimDragAt(p);
            if (g.State != IslandGhost.Mode.Insert) { t.SimDragRelease(); return "state " + g.State + " at " + p; }
            if (g.InsertAt != 1) { t.SimDragRelease(); return "InsertAt " + g.InsertAt; }
            if (SM.Route.GapSegment != 0) { t.SimDragRelease(); return "gap preview on segment " + SM.Route.GapSegment; }
            int count = SM.Islands.Count, cols = SM.ColumnCount, h = History.UndoCount;
            int at = t.SimDragRelease();
            if (at < 0 || SM.Islands[at].column != 1 || !SM.Islands[at].IsAnchor) return "placed at " + at + " (column " + (at >= 0 ? SM.Islands[at].column : -1) + ")";
            if (SM.Islands.Count != count + 1 || SM.ColumnCount != cols + 1 || SM.AnchorOf(2).assignedChord != c1) return "order " + Chords();
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            return null;
        });

        Run(sb, "ghost over a full column: BLOCKED (red), release flies back: no island, no History, deck unchanged, lock released", () =>
        {
            var t = IslandTray.I; t.RebuildNow();
            while (SM.ColumnSize(0) < ProjectConfig.MaxLanes) if (SM.AddIslandInColumn(SM.ColumnFirst(0), false) < 0) return "could not fill column 0";
            int count = SM.Islands.Count, h = History.UndoCount, d = IslandTray.DeckCount;
            var g = t.SimDragStart(0);
            if (g == null) return "no ghost";
            t.SimDragAt(SM.Islands[0].Center);
            if (g.State != IslandGhost.Mode.Blocked) { t.SimDragRelease(); return "state " + g.State; }
            int at = t.SimDragRelease();
            if (at != -1 || SM.Islands.Count != count) return "placed " + at;
            if (History.UndoCount != h || IslandTray.DeckCount != d) return "history/deck changed";
            if (WorldInput.WorldLocked) return "still locked";
            while (SM.ColumnSize(0) > 2) History.Undo();   // back to two islands in column 0
            return null;
        });

        Run(sb, "ghost merge: near the last island's east end -> Merge (slot touching), release joins the group in one History entry + IslandsMerged", () =>
        {
            events.Clear();
            var t = IslandTray.I; t.RebuildNow();
            int ti = SM.Islands.Count - 1;   // the island dropped on the open sea behind the song: nothing east of it
            var a = SM.Islands[ti];
            var g = t.SimDragStart(6);
            if (g == null) return "no ghost";
            Vector3 want = new Vector3(a.EastEdge + 0.9f + W * 0.5f, 0f, a.Center.z);
            t.SimDragAt(want);
            if (g.State != IslandGhost.Mode.Merge) { t.SimDragRelease(); return "state " + g.State + " at " + want + " | " + SM.GroupReport(); }
            if (g.MergeTarget != a || !g.MergeEast) { t.SimDragRelease(); return "target"; }
            if (Mathf.Abs(g.Root.x - (a.px + W)) > 0.01f || Mathf.Abs(g.Root.z - a.pz) > 0.01f) { t.SimDragRelease(); return "slot " + g.Root; }
            int count = SM.Islands.Count, h = History.UndoCount;
            int at = t.SimDragRelease();
            if (at != ti + 1 || SM.Islands.Count != count + 1) return "placed at " + at;
            if (SM.Islands[ti].group == 0 || SM.Islands[ti + 1].group != SM.Islands[ti].group) return "groups " + SM.GroupReport();
            if (Mathf.Abs(SM.Islands[ti].EastEdge - SM.Islands[ti + 1].WestEdge) > 0.01f) return "not touching";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (!events.Contains(Onboarding.Ev.IslandsMerged)) return "no IslandsMerged";
            return null;
        });

        Run(sb, "tools: dice +1 island, duplicate +1 (after the focused island's group), wand places the whole deck on free sea (one History each)", () =>
        {
            var t = IslandTray.I;
            int count = SM.Islands.Count, h = History.UndoCount;
            t.SimTool(0);
            if (SM.Islands.Count != count + 1 || History.UndoCount != h + 1) return "dice: islands " + SM.Islands.Count + " history +" + (History.UndoCount - h);
            UIManager.I.SelectMeasure(0, false);
            count = SM.Islands.Count; h = History.UndoCount;
            int dupCol = SM.GlueRunEnd(SM.ColumnOf(0)) + 1;   // v4: a new column right after island 0's glued run
            t.SimTool(1);
            if (SM.Islands.Count != count + 1 || History.UndoCount != h + 1) return "duplicate";
            var dup = SM.AnchorOf(dupCol);
            if (dup == null || dup.assignedChord != SM.Islands[0].assignedChord || dup.group != 0 || SM.ColumnSize(dupCol) != 1) return "duplicate not in a new column after the group: " + Chords();
            int d = IslandTray.DeckCount;
            count = SM.Islands.Count; h = History.UndoCount;
            t.SimTool(2);
            if (d > 0 && (SM.Islands.Count != count + d || IslandTray.DeckCount != 0 || History.UndoCount != h + 1)) return "wand: +" + (SM.Islands.Count - count) + " deck " + IslandTray.DeckCount + " history +" + (History.UndoCount - h);
            for (int i = count; i < SM.Islands.Count; i++) if (!Clear(SM.Islands[i], 1.4f)) return "wand island " + i + " overlaps";
            History.Undo();
            if (IslandTray.DeckCount != d || SM.Islands.Count != count) return "undo of the wand: deck " + IslandTray.DeckCount + " islands " + SM.Islands.Count;
            History.Redo();
            if (IslandTray.DeckCount != 0 || SM.Islands.Count != count + d) return "redo of the wand";
            return null;
        });
        IslandTray.Close();
        yield return null;

        // ================================================================ merges on the user's fixture (6 islands, 13 cubes)
        LoadFixture();
        yield return null;
        string c0 = "", c1 = "";
        Run(sb, "merge east: MergeIslands(1 -> east of 0): order kept, touching (x and front rows), one id, cubes + windows kept, seam without cable, History +1", () =>
        {
            events.Clear();
            string before = Chords(); var cubes = CubesPerChord(); int total = SequenceMaster.Cubes.Count; int h = History.UndoCount;
            c0 = SM.Islands[0].assignedChord; c1 = SM.Islands[1].assignedChord;
            if (OrbitCamera.I != null)
            {
                var i0 = SM.Islands[0];
                OrbitCamera.I.FrameBounds(new Bounds(new Vector3(i0.EastEdge, 0f, i0.FrontEdge + i0.Depth * 0.5f), new Vector3(9f, 2f, 7f)), 0.1f, true);
            }
            if (!SM.MergeIslands(SM.Islands[1], SM.Islands[0], true)) return "refused";
            if (Chords() != before) return "order " + Chords();
            var a = SM.Islands[0]; var b = SM.Islands[1];
            if (a.group == 0 || b.group != a.group) return "groups " + SM.GroupReport();
            if (Mathf.Abs(a.EastEdge - b.WestEdge) > 0.01f || Mathf.Abs(a.pz - b.pz) > 0.01f) return "not touching: " + a.EastEdge + " / " + b.WestEdge + " pz " + a.pz + " / " + b.pz;
            if (SequenceMaster.Cubes.Count != total) return "cubes " + SequenceMaster.Cubes.Count;
            string sc = SameCounts(cubes, CubesPerChord()); if (sc != null) return sc;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == b && !c.rider && (c.windows.Count != 1 || Mathf.Abs(c.windows[0].start - SM.MeasureStarts[1]) > 1e-4f)) return "window of a cube on island 1";
            if (!SM.IsSeam(0) || SM.Route.CableVisible(0) || !SM.Route.CableVisible(1)) return "cables: seam " + SM.IsSeam(0) + " cable0 " + SM.Route.CableVisible(0) + " cable1 " + SM.Route.CableVisible(1);
            if (!a.IsGroupMember || !b.IsGroupMember) return "member look";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (!events.Contains(Onboarding.Ev.IslandsMerged)) return "no IslandsMerged";
            var gp = Route.GlidePoint(a, b, 0.5f);
            if (gp.y > 1f) return "glide height " + gp.y;
            return null;
        });
        yield return Wait(0.2f);
        yield return Shot("b_merge_anim.png", false);   // mid-animation: the slide-in, the seam flash and the outlines drawing in
        yield return Wait(0.8f);
        yield return Shot("b_merge.png", false);

        Run(sb, "comet over the seam: seek into island 0's last beat (a low glide) and island 2's last beat (the cable arc)", () =>
        {
            GlobalClock.Stop();
            GlobalClock.Seek(SM.MeasureStarts[0] + SM.Islands[0].LengthBeats - 0.5f);
            return null;
        });
        yield return null; yield return null;
        float seamY = Comet.I != null ? Comet.I.Position.y : -1f; bool seamFlight = Comet.I != null && Comet.I.InFlight;
        yield return Shot("b_glide.png", false);   // the comet crossing the seam low (the last beat of island 0)
        GlobalClock.Seek(SM.MeasureStarts[2] + SM.Islands[2].LengthBeats - 0.5f);
        yield return null; yield return null;
        float cableY = Comet.I != null ? Comet.I.Position.y : -1f;
        GlobalClock.Stop(); GlobalClock.Seek(0);
        Line(sb, seamFlight && seamY < 0.75f && cableY > 0.85f && cableY - seamY > 0.3f, "comet heights", "seam y " + seamY.ToString("F2") + " (in flight " + seamFlight + "), cable y " + cableY.ToString("F2"));

        string c3 = "", c5 = "";
        Run(sb, "merge west with reorder: MergeIslands(3 -> west of 5): order c0 c1 c2 c4 c3 c5, touching, one id, cubes remapped, windows follow, History +1", () =>
        {
            var before = new List<string>(); foreach (var kb in SM.Islands) before.Add(kb.assignedChord);
            var cubes = CubesPerChord(); int h = History.UndoCount;
            c3 = before[3]; c5 = before[5];
            var i5 = SM.Islands[5];
            SM.MoveIsland(i5, i5.px + 12f, i5.pz);   // room on island 5's west side, so the moved island lands on open sea
            if (!SM.MergeIslands(SM.Islands[3], SM.Islands[5], false)) return "refused";
            string want = before[0] + " " + before[1] + " " + before[2] + " " + before[4] + " " + before[3] + " " + before[5];
            if (Chords() != want) return "order " + Chords() + " want " + want;
            var a = SM.Islands[4]; var b = SM.Islands[5];
            if (a.group == 0 || b.group != a.group || a.group == SM.Islands[0].group) return "groups " + SM.GroupReport();
            if (Mathf.Abs(a.EastEdge - b.WestEdge) > 0.01f || Mathf.Abs(a.pz - b.pz) > 0.01f) return "not touching";
            string sc = SameCounts(cubes, CubesPerChord()); if (sc != null) return sc;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == a && !c.rider && (c.assignedGridIndex != 4 || Mathf.Abs(c.windows[0].start - SM.MeasureStarts[4]) > 1e-4f)) return "cube on the moved island: index " + c.assignedGridIndex;
            float t = 0f; for (int i = 0; i < SM.Islands.Count; i++) { if (Mathf.Abs(SM.MeasureStarts[i] - t) > 1e-4f) return "MeasureStarts " + i; t += SM.Islands[i].LengthBeats; }
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            return null;
        });

        Run(sb, "undo / redo round trip keeps order, groups and positions", () =>
        {
            string merged = Chords(); string groups = SM.GroupReport();
            History.Undo();
            if (SM.Islands[3].assignedChord != c3 || SM.Islands[3].group != 0) return "undo: " + Chords() + " | " + SM.GroupReport();
            if (SM.Islands[0].group == 0 || SM.Islands[1].group != SM.Islands[0].group) return "undo lost the first group";
            History.Redo();
            if (Chords() != merged || SM.GroupReport() != groups) return "redo: " + SM.GroupReport() + " vs " + groups;
            return null;
        });

        Run(sb, "save / load round trip (SongIO.SaveTo scratch, never the user's save) keeps groups and positions", () =>
        {
            string groups = SM.GroupReport(); string order = Chords();
            if (!SongIO.SaveTo(ScratchSave)) return "SaveTo failed";
            LoadFixture();
            if (SM.GroupReport() == groups) return "fixture reload did not change anything (test is blind)";
            if (!SongIO.LoadFrom(ScratchSave)) return "LoadFrom failed";
            if (Chords() != order) return "order " + Chords();
            if (SM.GroupReport() != groups) return "groups/positions " + SM.GroupReport() + " vs " + groups;
            if (!SM.IsSeam(0) || !SM.IsSeam(4) || SM.Route.CableVisible(0) || SM.Route.CableVisible(4)) return "seams after load";
            return null;
        });

        Vector2 scissorsPx = new Vector2(-1f, -1f);
        Run(sb, "scissors hover: framing seam 4, the pointer on its front end shows the glyph (hot); elsewhere it hides", () =>
        {
            events.Clear();
            var drag0 = PathManager.I != null ? PathManager.I.Drag : null;
            if (drag0 == null) return "no IslandDrag";
            var a4 = SM.Islands[4];
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(new Bounds(new Vector3(a4.EastEdge, 0f, a4.Center.z), new Vector3(14f, 2f, 9f)), 0.1f, true);
            Vector3 glyphMid = new Vector3(a4.EastEdge, 0.25f + 0.45f, a4.FrontEdge - 0.62f);
            Vector3 sp = Camera.main.WorldToScreenPoint(glyphMid);
            if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) return "glyph off screen " + sp;
            int hov = drag0.SimSeam(new Vector2(sp.x, sp.y), false);
            if (hov != 4 || !SM.Route.ScissorsVisible) return "hover: seam " + hov + " glyph " + SM.Route.ScissorsVisible;
            scissorsPx = new Vector2(sp.x, sp.y);
            return null;
        });
        // the glyph stays while the pointer hovers: keep hovering for a few frames and capture it
        if (PathManager.I != null && scissorsPx.x >= 0f)
        {
            for (int k = 0; k < 12; k++) { PathManager.I.Drag.SimSeam(scissorsPx, false); yield return null; }
            PathManager.I.Drag.SimSeam(scissorsPx, false);
            Directory.CreateDirectory(V2Checks.CapturePath);
            string sc = Path.Combine(V2Checks.CapturePath, "b_scissors.png");
            if (File.Exists(sc)) File.Delete(sc);
            ScreenCapture.CaptureScreenshot(sc, 1);
            yield return null;
        }
        Run(sb, "scissors click: splits the group there", () =>
        {
            var drag0 = PathManager.I.Drag;
            Vector3 sp = new Vector3(scissorsPx.x, scissorsPx.y, 1f);
            int far = drag0.SimSeam(new Vector2(sp.x + 400f, sp.y + 300f), false);
            if (far == 4) return "the scissors follow the pointer everywhere";
            int h = History.UndoCount;
            float x5 = SM.Islands[5].px, z5 = SM.Islands[5].pz;
            drag0.SimSeam(new Vector2(sp.x, sp.y), true);
            if (SM.IsSeam(4)) return "the click did not split";
            if (SM.Islands[4].group != 0 || SM.Islands[5].group != 0) return "groups " + SM.GroupReport();
            float want5 = x5 + (SM.Touching(SM.Islands[4].column) ? 0f : ProjectConfig.SplitGap);   // v7 §13.2: inside a section the split columns keep touching
            if (Mathf.Abs(SM.Islands[5].px - want5) > 0.01f || Mathf.Abs(SM.Islands[5].pz - z5) > 0.01f) return "moved to " + SM.Islands[5].px + " from " + x5 + " want " + want5;
            if (SM.IsSeam(4) || !SM.Route.CableVisible(4)) return "cable";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (!events.Contains(Onboarding.Ev.IslandSplit)) return "no IslandSplit";
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(new Bounds(new Vector3(SM.Islands[5].WestEdge, 0f, SM.Islands[5].Center.z), new Vector3(16f, 2f, 8f)), 0.1f, true);
            return null;
        });
        yield return Wait(0.7f);
        yield return Shot("b_split.png", false);

        var drag = PathManager.I != null ? PathManager.I.Drag : null;
        Run(sb, "group drag: a platform drag on a member moves the whole group rigidly (still touching, same id), one History entry", () =>
        {
            if (drag == null) return "no IslandDrag";
            var a = SM.Islands[0]; var b = SM.Islands[1];
            int gid = a.group; int h = History.UndoCount;
            float ax = a.px, az = a.pz, bx = b.px, bz = b.pz;
            drag.SimBegin(a, false, a.Center);
            if (drag.Moving.Count != 2) { drag.Cancel(); return "moving " + drag.Moving.Count; }
            drag.SimMove(a.Center + new Vector3(0f, 0f, 10f));
            drag.SimRelease();
            float dx = a.px - ax, dz = a.pz - az;
            if (Mathf.Abs(dz - 10f) > 0.26f) return "moved dz " + dz;
            if (Mathf.Abs(b.px - bx - dx) > 1e-3f || Mathf.Abs(b.pz - bz - dz) > 1e-3f) return "not rigid";
            if (a.group != gid || b.group != gid || Mathf.Abs(a.EastEdge - b.WestEdge) > 0.01f) return "group broken";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            return null;
        });

        Run(sb, "tear-out: a hub drag on a member leaves the group (a group of one dissolves), cable back, one History entry", () =>
        {
            if (drag == null) return "no IslandDrag";
            var a = SM.Islands[0]; var b = SM.Islands[1];
            int h = History.UndoCount;
            drag.SimBegin(b, true, b.HubPos);
            if (!drag.TornOut || drag.Moving.Count != 1) { drag.Cancel(); return "not torn out"; }
            drag.SimMove(b.HubPos + new Vector3(4f, 0f, 9f));
            drag.SimRelease();
            if (a.group != 0 || b.group != 0) return "groups " + SM.GroupReport();
            if (SM.IsSeam(0) || !SM.Route.CableVisible(0)) return "cable";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            return null;
        });

        int zoneH = 0; bool zoneOk = false;
        Run(sb, "merge zone while dragging: an edge within 1.4 u (40 % overlap) offers the merge (light sheet on the target edge, magnet to the slot)", () =>
        {
            if (drag == null) return "no IslandDrag";
            // v7 (SPEC v7 §13.2): inside a section the columns touch and a merge glues only across a GAP — a section boundary before column 1 first
            var stSec = SongState.Capture(); var secs = new List<int>(SongManager.SectionsOf(stSec)); if (!secs.Contains(1)) secs.Add(1); secs.Sort();
            stSec.sections = secs.ToArray(); SM.RebuildFromState(stSec); History.Push();
            var a = SM.Islands[0]; var b = SM.Islands[1];
            zoneH = History.UndoCount;
            drag.SimBegin(b, false, b.Center);
            Vector3 want = new Vector3(a.EastEdge + 0.9f + W * 0.5f, 0f, a.Center.z + 0.3f);
            drag.SimMove(want);
            if (drag.MergeTarget != a || !drag.MergeEast) { drag.Cancel(); return "target " + (drag.MergeTarget != null ? drag.MergeTarget.name : "none") + " gap " + drag.NearestGap; }
            if (!SM.Route.MergeSheetVisible) { drag.Cancel(); return "no light sheet"; }
            if (Mathf.Abs(b.WestEdge - a.EastEdge) > 0.01f) { drag.Cancel(); return "magnet: west edge " + b.WestEdge + " vs " + a.EastEdge; }
            if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(new Bounds(new Vector3(a.EastEdge, 0f, a.Center.z), new Vector3(15f, 2f, 9f)), 0.1f, true);
            zoneOk = true;
            return null;
        });
        if (zoneOk) { yield return Wait(0.25f); yield return Shot("b_zone.png", false); }
        Run(sb, "merge zone release: merges once (one History entry), the light sheet goes away", () =>
        {
            if (!zoneOk) return "no zone";
            int h = zoneH;
            drag.SimRelease();
            var a = SM.Islands[0]; var b = SM.Islands[1];   // the merge rebuilt the islands
            if (a.group == 0 || b.group != a.group || !SM.IsSeam(0)) return "not merged: " + SM.GroupReport();
            if (Mathf.Abs(a.EastEdge - b.WestEdge) > 0.01f) return "not touching";
            if (History.UndoCount != h + 1) return "history +" + (History.UndoCount - h);
            if (SM.Route.MergeSheetVisible) return "sheet left on";
            return null;
        });

        Run(sb, "duplicate / add after a group member land after the group's last member (the group stays whole)", () =>
        {
            if (!SM.IsSeam(0)) return "no group to test: " + SM.GroupReport();
            string c0n = SM.Islands[0].assignedChord;
            int n0 = SM.Islands.Count;
            int at = SM.DuplicateMeasure(0);
            if (at != 2) return "duplicate at " + at;
            if (!SM.IsSeam(0) || SM.Islands[2].group != 0 || SM.Islands[2].assignedChord != c0n) return "after duplicate: " + SM.GroupReport();
            int at2 = SM.AddMeasure(1);
            if (at2 != 2 || !SM.IsSeam(0) || SM.Islands[2].group != 0) return "add at " + at2 + ": " + SM.GroupReport();
            if (SM.Islands.Count != n0 + 2) return "count " + SM.Islands.Count;
            return null;
        });

        Run(sb, "MoveBlock: a group moves as one in the song order and stays a group; undo restores", () =>
        {
            int n = SM.Islands.Count;
            string g0 = SM.Islands[0].assignedChord, g1 = SM.Islands[1].assignedChord;
            SM.MoveBlock(0, 2, n - 2);
            if (SM.Islands[n - 2].assignedChord != g0 || SM.Islands[n - 1].assignedChord != g1) return "order " + Chords();
            if (!SM.IsSeam(n - 2)) return "group lost: " + SM.GroupReport();
            History.Undo();
            if (SM.Islands[0].assignedChord != g0 || !SM.IsSeam(0)) return "undo: " + Chords();
            return null;
        });

        Run(sb, "removing the middle member of a 3-island group closes the gap (the rest stay one group, touching)", () =>
        {
            if (!SM.MergeIslands(SM.Islands[2], SM.Islands[1], true)) return "merge refused";
            if (!SM.IsSeam(0) || !SM.IsSeam(1)) return "3-group: " + SM.GroupReport();
            string c2 = SM.Islands[2].assignedChord;
            SM.RemoveMeasure(1);
            if (SM.Islands[1].assignedChord != c2) return "order " + Chords();
            if (!SM.IsSeam(0) || Mathf.Abs(SM.Islands[0].EastEdge - SM.Islands[1].WestEdge) > 0.01f) return "gap not closed: " + SM.GroupReport();
            return null;
        });

        Run(sb, "v2 islands untouched: a rebuild keeps every group-0 island's position; the fixture's cubes still load (13)", () =>
        {
            LoadFixture();
            var pos = new List<Vector2>(); foreach (var kb in SM.Islands) pos.Add(new Vector2(kb.px, kb.pz));
            SM.RebuildFromState(SongState.Capture());
            for (int i = 0; i < SM.Islands.Count; i++) if ((new Vector2(SM.Islands[i].px, SM.Islands[i].pz) - pos[i]).magnitude > 1e-4f || SM.Islands[i].group != 0) return "island " + i;
            int cubes = 0; foreach (var c in SequenceMaster.Cubes) if (c != null) cubes++;
            if (cubes != 13) return "cubes " + cubes;
            return null;
        });

        // ================================================================ camera (SPEC v3 §3.4)
        var cam = OrbitCamera.I;
        var ucam = Camera.main;
        if (cam == null || ucam == null) { Line(sb, false, "camera", "no OrbitCamera / main camera"); }
        else
        {
            cam.FocusMeasure(0, true);
            yield return null;
            Run(sb, "follow off by default", () => cam.followPlayhead ? "followPlayhead is on" : null);

            // island focus: a click on a platform selects it and frames it (group aware); a double-click frames tighter
            var dragC = PathManager.I != null ? PathManager.I.Drag : null;
            float dOne = 0f, dTwo = 0f; string clickWhy = null;
            if (dragC == null) clickWhy = "no IslandDrag";
            else
            {
                dragC.SimClick(SM.Islands[2], false);
                dOne = cam.DistT;
                var gb2 = SM.GroupBounds(SM.Islands[2]); gb2.Expand(3f);
                if (UIManager.I != null && UIManager.I.SelectedMeasure != 2) clickWhy = "selected " + UIManager.I.SelectedMeasure;
                else if (!gb2.Contains(new Vector3(cam.FocusTarget.x, gb2.center.y, cam.FocusTarget.z))) clickWhy = "focus target " + cam.FocusTarget;
                dragC.SimClick(SM.Islands[2], false);
                dTwo = cam.DistT;
                if (clickWhy == null && Mathf.Abs(dTwo - Mathf.Max((OrbitCamera.MinDist + 2f) * cam.FovScale, dOne * 0.7f)) > 0.05f) clickWhy = "double-click distance " + dTwo + " (single " + dOne + ")";
            }
            Line(sb, clickWhy == null, "island click: select + glide to frame it; double-click frames tighter", clickWhy ?? ("distance " + dOne.ToString("F1") + " -> " + dTwo.ToString("F1")));
            yield return Wait(0.8f);

            // orbit: direct while dragging, yaw inertia (τ 0.3 s) after the release
            Vector2 oc = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float yaw0 = cam.Yaw; float dx = 0f; float orbitErr = 0f;
            // fix F12 / F17: degrees per pixel scale with the DPI and the orbit takes hold past the DPI-aware drag threshold
            float perPx = OrbitCamera.OrbitDegPerPx / OrbitCamera.DpiScale, dx0 = -1f;
            cam.SimOrbitBegin(oc);
            for (int k = 1; k <= 16; k++)
            {
                dx += 8f; cam.SimOrbitMove(oc + new Vector2(dx, 0f));
                yield return null;
                if (dx0 < 0f && cam.Orbiting) dx0 = dx - 8f;
                if (dx0 >= 0f) orbitErr = Mathf.Max(orbitErr, Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, yaw0 + perPx * (dx - dx0))));
            }
            if (dx0 < 0f) orbitErr = 99f;
            cam.SimOrbitEnd();
            float yv0 = cam.YawVelocity; float yawAtRelease = cam.Yaw; float to0 = Time.realtimeSinceStartup;
            while (cam.YawVelocity != 0f && Time.realtimeSinceStartup - to0 < 3f) yield return null;
            float coastDeg = Mathf.Abs(Mathf.DeltaAngle(yawAtRelease, cam.Yaw));
            float expectDeg = Mathf.Max(0f, Mathf.Abs(yv0) - OrbitCamera.YawStopSpeed) * OrbitCamera.YawTau;
            Line(sb, orbitErr < 0.01f && Mathf.Abs(yv0) > 10f && Mathf.Abs(coastDeg - expectDeg) <= 0.25f * expectDeg + 0.5f, "orbit: yaw follows the pointer directly (0 lag), yaw inertia after the release (τ 0.3 s)",
                "max lag " + orbitErr.ToString("F3") + "°, release " + yv0.ToString("F0") + "°/s, coast " + coastDeg.ToString("F1") + "° (expected " + expectDeg.ToString("F1") + "°)");
            cam.ResetView();
            yield return Wait(1.6f);

            // trackpad sideways scroll pans along the camera's right
            Vector3 s0 = cam.Focus; float sd = cam.Dist;
            cam.SimScroll(new Vector2(1f, 0f), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return null; yield return null;
            Vector3 sdel = cam.Focus - s0;
            float yr = cam.Yaw * Mathf.Deg2Rad; Vector3 rightV = new Vector3(Mathf.Cos(yr), 0f, -Mathf.Sin(yr));
            Line(sb, Mathf.Abs(Vector3.Dot(sdel, rightV) - sd * 0.06f) < 0.03f && Mathf.Abs(cam.Dist - sd) < 0.05f, "trackpad sideways scroll pans along the camera's right (no zoom)", "moved " + Vector3.Dot(sdel, rightV).ToString("F3") + " u (want " + (sd * 0.06f).ToString("F3") + ")");

            // Save / RestoreView round trip; Suspended freezes the rig
            cam.SaveView();
            Vector3 svF = cam.FocusTarget; float svY = cam.Yaw, svP = cam.Pitch, svD = cam.DistT;
            cam.StepIsland(1);
            yield return Wait(0.3f);
            cam.RestoreView(true);
            yield return null;
            float rF = (cam.Focus - svF).magnitude, rY = Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, svY)), rP = Mathf.Abs(cam.Pitch - svP), rD = Mathf.Abs(cam.Dist - svD);
            bool restored = rF < 1e-3f && rY < 1e-2f && rP < 1e-2f && rD < 1e-3f;
            string restoreDetail = "focus " + rF.ToString("E1") + " yaw " + rY.ToString("E1") + " pitch " + rP.ToString("E1") + " dist " + rD.ToString("E1");
            cam.Suspended = true;
            Vector3 susp = ucam.transform.position;
            OrbitCamera.SimKeyHold(KeyCode.D, true);
            yield return Wait(0.3f);
            OrbitCamera.SimKeyHold(KeyCode.D, false);
            bool frozen = (ucam.transform.position - susp).magnitude < 1e-5f;
            cam.Suspended = false;
            yield return Wait(0.5f);
            Line(sb, restored && frozen, "SaveView / RestoreView(immediate) round trip; Suspended = the rig does nothing", "restored " + restored + " (" + restoreDetail + "), frozen while suspended " + frozen);

            // soft bounds: a view thrown far past the song springs back
            var sbnd = SM.SongBounds;
            cam.GlideTo(new Vector3(sbnd.max.x + OrbitCamera.SoftBoundsPad + 25f, 0f, sbnd.center.z), 0.2f);
            yield return Wait(3.2f);
            var inner = SM.SongBounds; inner.Expand(OrbitCamera.SoftBoundsPad * 2f);
            Vector3 cp = inner.ClosestPoint(new Vector3(cam.Focus.x, inner.center.y, cam.Focus.z));
            float outside = new Vector2(cam.Focus.x - cp.x, cam.Focus.z - cp.z).magnitude;
            Line(sb, outside < 0.5f, "soft bounds: a view 25 u past the 60 u pad springs back", "outside by " + outside.ToString("F2") + " u after 3.2 s");
            cam.FocusMeasure(0, true);
            yield return null;

            // no motion while playing
            GlobalClock.Seek(0); GlobalClock.Play();
            yield return Wait(0.2f);
            Vector3 p0 = ucam.transform.position; Quaternion r0 = ucam.transform.rotation; float worst = 0f;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.6f) { worst = Mathf.Max(worst, (ucam.transform.position - p0).magnitude, Quaternion.Angle(r0, ucam.transform.rotation)); yield return null; }
            GlobalClock.Stop();
            Line(sb, worst < 1e-4f, "no camera motion while playing (1.6 s, follow off)", "max drift " + worst.ToString("E2"));

            // zero-lag grab pan
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
            cam.SimPanBegin(c);
            Vector3 grab; cam.GroundUnder(c, out grab);
            float maxErr = 0f; int frames = 0;
            for (int k = 1; k <= 30; k++)
            {
                Vector2 p = c + new Vector2(7f * k, 2.5f * k);
                cam.SimPanMove(p);
                yield return null;
                Vector3 g; cam.GroundUnder(p, out g);
                maxErr = Mathf.Max(maxErr, new Vector2(g.x - grab.x, g.z - grab.z).magnitude);
                frames++;
            }
            Line(sb, maxErr < 0.01f, "grab-the-ground pan: the grabbed point stays under the pointer every frame (lag 0 frames)", frames + " frames, max error " + maxErr.ToString("F4") + " u");

            // release inertia: steady pointer speed, then release
            Vector2 last = c + new Vector2(7f * 30f, 2.5f * 30f);
            for (int k = 1; k <= 20; k++) { last += new Vector2(9f, 0f); cam.SimPanMove(last); yield return null; }
            Vector3 fBefore = cam.Focus;
            cam.SimPanEnd();
            float v0 = cam.PanVelocity.magnitude;
            Vector3 fStart = cam.Focus; float tStart = Time.realtimeSinceStartup; float vAtTau = -1f;
            while (cam.PanVelocity.sqrMagnitude > 0f && Time.realtimeSinceStartup - tStart < 4f)
            {
                yield return null;
                if (vAtTau < 0f && Time.realtimeSinceStartup - tStart >= OrbitCamera.PanTau) vAtTau = cam.PanVelocity.magnitude;
            }
            float travel = new Vector2(cam.Focus.x - fStart.x, cam.Focus.z - fStart.z).magnitude;
            float expect = Mathf.Max(0f, v0 - OrbitCamera.PanStopSpeed) * OrbitCamera.PanTau;
            float ratio = vAtTau / Mathf.Max(1e-4f, v0);
            bool inertiaOk = v0 > 0.5f && Mathf.Abs(travel - expect) <= 0.2f * expect + 0.05f && ratio > 0.25f && ratio < 0.5f && cam.PanVelocity == Vector3.zero;
            Line(sb, inertiaOk, "release inertia decays (τ 0.35 s)", "release speed " + v0.ToString("F2") + " u/s, coast " + travel.ToString("F2") + " u (expected " + expect.ToString("F2") + "), v(τ)/v0 " + ratio.ToString("F2") + ", stopped " + (cam.PanVelocity == Vector3.zero) + " after " + (Time.realtimeSinceStartup - tStart).ToString("F2") + " s");

            // zoom toward the cursor
            Vector2 zp = new Vector2(Screen.width * 0.68f, Screen.height * 0.38f);
            Vector3 zg; cam.GroundUnder(zp, out zg);
            float d0 = cam.DistT;
            cam.SimScroll(new Vector2(0f, 1f), zp);
            yield return Wait(0.6f);
            Vector3 zg2; cam.GroundUnder(zp, out zg2);
            float zerr = new Vector2(zg2.x - zg.x, zg2.z - zg.z).magnitude;
            Line(sb, Mathf.Abs(cam.DistT - d0 * OrbitCamera.ZoomInStep) < 0.02f && Mathf.Abs(cam.Dist - cam.DistT) < 0.02f && zerr < 0.05f, "wheel zoom x0.88 per notch toward the cursor (16/s)", "dist " + d0.ToString("F2") + " -> " + cam.Dist.ToString("F2") + ", point under the cursor moved " + zerr.ToString("F3") + " u");

            // Tab steps islands (group aware)
            cam.FocusMeasure(0, true);
            yield return null;
            OrbitCamera.SimKeyDown(KeyCode.Tab);
            yield return null; yield return null;
            int fm = cam.FocusedMeasure;
            var target = SM.Islands[Mathf.Clamp(fm, 0, SM.Islands.Count - 1)];
            var gb = SM.GroupBounds(target); gb.Expand(3f);
            bool tabOk = fm == 1 && gb.Contains(new Vector3(cam.FocusTarget.x, gb.center.y, cam.FocusTarget.z));
            OrbitCamera.SimKeyDown(KeyCode.Tab);
            yield return null; yield return null;
            tabOk &= cam.FocusedMeasure == 2;
            Line(sb, tabOk, "Tab steps to the next island (glide + frame)", "focused " + fm + " then " + cam.FocusedMeasure + ", target " + cam.FocusTarget.ToString("F1"));
            yield return Wait(0.8f);

            // WASD pan with a short ramp
            Vector3 k0 = cam.Focus;
            OrbitCamera.SimKeyHold(KeyCode.D, true);
            yield return Wait(0.45f);
            // (measured against the rig's own frame time — it integrates Time.unscaledDeltaTime clamped to 0.1 s: a loaded editor at < 10 fps
            // would otherwise read slow; the frame read here is applied by the rig's LateUpdate after this coroutine step)
            Vector3 k1 = cam.Focus; float kt = Time.realtimeSinceStartup, acc = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            while (true) { yield return null; if (Time.realtimeSinceStartup - kt >= 0.3f) break; acc += Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f); }
            Vector3 k2 = cam.Focus; float speed = new Vector2(k2.x - k1.x, k2.z - k1.z).magnitude / Mathf.Max(1e-3f, acc);
            OrbitCamera.SimKeyHold(KeyCode.D, false);
            float wantSpeed = OrbitCamera.KeyPanSpeed * cam.Dist;
            yield return Wait(0.85f);
            Vector3 k3 = cam.Focus; yield return Wait(0.15f);
            bool stopped = (cam.Focus - k3).magnitude < 0.02f;
            float yawRad = cam.Yaw * Mathf.Deg2Rad;
            float along = Vector3.Dot((k2 - k0).normalized, new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)));
            Line(sb, Mathf.Abs(speed - wantSpeed) < 0.15f * wantSpeed && stopped && along > 0.95f, "D pans right at 0.9 x distance per second, stops after release", "speed " + speed.ToString("F2") + " (want " + wantSpeed.ToString("F2") + "), direction " + along.ToString("F2") + ", stopped " + stopped);

            // C reset view (fix F4: Q / E orbit now)
            OrbitCamera.SimKeyDown(OrbitCamera.ResetViewKey);
            yield return Wait(0.9f);
            Line(sb, Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, OrbitCamera.HomeYaw)) < 0.5f && Mathf.Abs(cam.Pitch - OrbitCamera.HomePitch) < 0.5f && Mathf.Abs(cam.Dist - cam.HomeDistance) < 0.3f, "C reset view: yaw / pitch / distance reset, glide to the island", "yaw " + cam.Yaw.ToString("F1") + " pitch " + cam.Pitch.ToString("F1") + " dist " + cam.Dist.ToString("F1"));

            // follow (F) — the user (2026-10-01): "the camera follow should be constant rather than jumping per measure": the target moves with the
            // playhead on (nearly) every frame at a steady speed, forward through the song, never a per-measure lurch
            cam.ToggleFollow();
            GlobalClock.Seek(0); GlobalClock.Play();
            // v9: the follow holds on the first waypoint until column 0's middle (by design); sample once the target is under way, and leave
            // out frames after an editor stall (the follow re-syncs its beat after a > 0.25 s frame: one honest jump, not a per-measure lurch)
            // (measured on the camera's own focus, what the player sees: the follow target is read mid-frame by this coroutine and can show the
            // rig's internal hand-over between its waypoint and the smoothed focus; the focus itself is SmoothDamped and lurches only if the target does)
            yield return Wait(0.3f);
            { Vector3 held = cam.FocusTarget; float th = Time.realtimeSinceStartup; while ((cam.FocusTarget - held).magnitude < 0.05f && Time.realtimeSinceStartup - th < 2f) yield return null; }
            yield return Wait(0.4f);   // the focus has caught the moving target up
            // sampled at render time (Application.onBeforeRender), once per rendered frame: a coroutine's mid-frame reads see values the rig has
            // not finished for this frame (its LateUpdate follows) and show false jumps
            var followSpeeds = new List<float>(); Vector3 lastTarget = cam.Focus, firstTarget = lastTarget; int followFrames = 0, targetMoves = 0;
            UnityEngine.Events.UnityAction sample = () =>
            {
                float d = (cam.Focus - lastTarget).magnitude; lastTarget = cam.Focus;
                if (Time.unscaledDeltaTime > 0.12f) return;   // a stalled editor frame: not the follow's doing
                followFrames++;
                if (d > 1e-4f) { targetMoves++; followSpeeds.Add(d / Mathf.Max(1e-3f, Time.unscaledDeltaTime)); }
            };
            Application.onBeforeRender += sample;
            yield return Wait(2.6f);
            Application.onBeforeRender -= sample;
            GlobalClock.Stop();
            cam.ToggleFollow();
            followSpeeds.Sort();
            float medSpeed = followSpeeds.Count > 0 ? followSpeeds[followSpeeds.Count / 2] : 0f, topSpeed = followSpeeds.Count > 0 ? followSpeeds[followSpeeds.Count - 1] : 0f;
            int jumps = 0; foreach (float v in followSpeeds) if (v > medSpeed * 3f + 0.5f) jumps++;   // a per-measure lurch = a jump every column (2+ in 2.6 s); one is the follow catching the clock
            bool forward = lastTarget.x > firstTarget.x + 0.5f;
            Line(sb, !cam.followPlayhead && followFrames > 0 && targetMoves >= followFrames * 0.8f && forward && jumps <= 1, "island follow (F): a constant follow — the target moves with the playhead every frame at a steady speed (no per-measure jumps)",
                 targetMoves + "/" + followFrames + " frames moved, speed median " + medSpeed.ToString("F2") + " max " + topSpeed.ToString("F2") + ", jumps " + jumps + ", forward " + forward);
        }

        // ================================================================ harmonic safety while merging during playback
        LoadFixture();
        yield return null;
        GlobalClock.Stop(); GlobalClock.Seek(0);
        yield return null;
        int lateBefore = Synth.LateEvents, errBefore = Synth.Errors; long logStart = Performance.LoggedTotal;
        GlobalClock.Play();
        float tp = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < 5.5 && Time.realtimeSinceStartup - tp < 8f) yield return null;
        string litChord = SM.Islands[SM.LitIsland].assignedChord; double local0 = GlobalClock.SongBeatD - SM.MeasureStarts[SM.LitIsland];
        bool m1 = SM.MergeIslands(SM.Islands[4], SM.Islands[1], true);   // reorders the song while playing (island 4 moves right after 1)
        int litAfter = SM.ActiveMeasureIndex(GlobalClock.SongBeat);
        double local1 = GlobalClock.SongBeatD - SM.MeasureStarts[litAfter];
        bool sameChord = SM.Islands[litAfter].assignedChord == litChord;
        yield return Wait(1.2f);
        int lit2 = SM.LitIsland;
        bool m2 = SM.MergeIslands(SM.Islands[lit2], SM.Islands[(lit2 + 3) % SM.Islands.Count], false);   // the lit island itself moves and merges
        yield return Wait(3f);
        GlobalClock.Stop();
        int late = Synth.LateEvents - lateBefore, errs = Synth.Errors - errBefore;
        Line(sb, m1 && m2 && sameChord && Math.Abs(local1 - local0) < 0.1, "merge while playing keeps the lit island's local beat", "lit " + litChord + " local " + local0.ToString("F2") + " -> " + local1.ToString("F2") + " (same chord " + sameChord + ")");
        Line(sb, late == 0 && errs == 0, "Synth late 0 / errors 0 while merging during playback", "late +" + late + " errors +" + errs + " | " + Synth.Stats());
        int checkedEv = 0, bad = 0; string first = null;
        for (int i = 0; i < Performance.LoggedCount; i++)
        {
            var e = Performance.Logged(i);
            if (e.owner < 16 || e.winEndDsp <= 0.0 || Performance.LoggedTotal - Performance.LoggedCount + i < logStart) continue;
            checkedEv++;
            string why = null;
            if (e.onDsp < e.winStartDsp - 1e-6 || e.onDsp >= e.winEndDsp) why = "on outside its window";
            else if (e.offDsp > e.winEndDsp - 0.005 + 1e-9) why = "off past windowEnd - 5 ms";
            else if (!SynthBank.Def(e.slot).drums && !ReferenceEquals(e.island, null))
            {
                int pc = (((e.midi - e.island.chordRootMIDI - SongManager.Transpose) % 12) + 12) % 12;
                bool tone = false; foreach (int s in e.island.semitoneList) if (((s % 12) + 12) % 12 == pc) { tone = true; break; }
                if (!tone) why = "midi " + e.midi + " not a tone of " + e.island.assignedChord;
            }
            if (why != null) { bad++; if (first == null) first = why; }
        }
        Line(sb, checkedEv > 0 && bad == 0, "harmonic safety over the merges: every cube event inside its window, chord tones only", checkedEv + " events checked, " + bad + " bad" + (first != null ? " (" + first + ")" : ""));

        // leave the fixture loaded and stopped
        LoadFixture();
        Onboarding.OnEvent -= onEv;
        Report = sb.ToString();
        Done = true;
    }
}
