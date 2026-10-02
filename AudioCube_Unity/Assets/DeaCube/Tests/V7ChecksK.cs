using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Package K (v7: layout, timeline and motion) Play-mode verification, SPEC v7 PART II §12–§14 + §2.5 / §2.6 / §2.7 / §2.8 / §18:
/// <c>V7ChecksK.RunAll()</c> starts a coroutine; poll <c>V7ChecksK.Done</c> or Captures/k7_report.txt (numbered PASS / FAIL lines, INFO lines with
/// numbers). Covers SECTIONS (automatic 4-measure sections of an older song, explicit ones restored literally, the landing rule when a column is
/// inserted inside a section / at a boundary / appended, removal, a moved column, undo), the LAYOUT (columns touch inside a section, the gap +
/// ghost measures between sections, widths by bars with the held-chord surface and a keyboard spreading its keys, rewind islands without a belt,
/// a merge across a boundary joining the sections, time → x linear inside a section), PHRASES (kind kept, never a lane, x from their start in time,
/// melody tracks behind the lanes, one window per song pass, offsets), GROUNDS (a down stair of 4 lowers the next columns by 1.36, an up stair,
/// the clamps, the glide, a load snaps, Moons, the hub, the camera), the HOLD RULES (§12: belts at their last slot until the reset then back with
/// a stagger, towers 3.2 / octave staying up then sinking out of sync, a lowered island dipped, Moons holding at their section's last column, a
/// column that starts the loop going home before the loop point, a stop animated in real time), REWIND (RewindPhase samples, pure; the gauge
/// drains), WINDOWS (pass, flow), LAUNCH (target, turn end) and the SAVE / LOAD round trip of every v7 field. Captures k7_*.png. Runs with
/// PathManager.AutoHand = false (the real game). Never writes the user's save; reloads the fixture at the end.
/// </summary>
public static class V7ChecksK
{
    public static string Report = "";
    public static bool Done;
    static int num, fails;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "k7_report.txt");
    public static string ScratchSave => Path.Combine(Application.temporaryCachePath, "v7checksk_roundtrip.json");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++; if (!ok) fails++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" K7-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
    static float W => KeyBlock.IslandWidth;
    static float E => KeyBlock.EdgeInset;
    static string F(float v) => v.ToString("F3");
    static string F(double v) => v.ToString("F3");
    static bool Near(float a, float b, float eps = 1e-3f) => Mathf.Abs(a - b) <= eps;
    static string L(IList<int> l) => "[" + string.Join(",", l.Select(x => x.ToString()).ToArray()) + "]";

    public static string RunAll()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0; fails = 0;
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
        GlobalClock.LoopIndex = 0;
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
    }

    static void LoadFixture()
    {
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        GlobalClock.LoopIndex = 0;
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
    /// <summary>Until no island slides (a rebuild's glide to a new place) — at most <paramref name="timeout"/> s real time — then two frames.</summary>
    static IEnumerator Settled(float timeout = 3f)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout && SM.Islands.Any(k => k != null && (k.Sliding || k.FxLifting || Mathf.Abs(k.FxLift) > 1e-4f || k.GroundGliding || k.Rising || k.Lifting))) yield return null;
        yield return null; yield return null;
    }
    static string Joints() => string.Join(" ", SM.Islands.Take(4).Select(k => k.column + ":pz " + F(k.pz) + " pos " + F(k.transform.position.x) + "," + F(k.transform.position.y) + "," + F(k.transform.position.z) + " w " + F(k.Width) + " W" + (k.JoinedWest ? 1 : 0) + "E" + (k.JoinedEast ? 1 : 0) + " fx " + F(k.FxLift)).ToArray()) + " | selected " + GridSelection.Count;
    static IEnumerator UntilBeat(double b, float timeout = 8f) { float t0 = Time.realtimeSinceStartup; while (GlobalClock.SongBeatD < b && Time.realtimeSinceStartup - t0 < timeout) yield return null; }

    static void Frame(Bounds b, float tight) { if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(b, 0.1f, true, tight); }

    static CubeState CubeOn(int island, int instrument, int[] xs, int[] zs, int step, int hits = -1)
    {
        return new CubeState { instrument = instrument, measure = island, moon = -1, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = 1, mode = 0, volume = 1f, hits = hits, twinOf = -1 };
    }

    // ---- v7 snapshot builders (package O's ops build these too; K's checks build them directly so they test K's layout / timeline alone)
    static MeasureState PhraseState(int col, int beats, int offsetTicks, int bars)
    {
        return new MeasureState { chordKey = "Melody", root = 60, semis = new[] { 0 }, bars = bars, kind = 4, placed = true, col = col, energy = 2, repeat = 1, phraseBeats = beats, phraseOffset = offsetTicks, phraseGrid = 12 };
    }
    static MeasureState StairState(int col, int dir, int steps, int bars, float pz)
    {
        return new MeasureState { chordKey = "Stairs", root = 60, semis = new[] { 0 }, bars = bars, kind = 3, placed = true, col = col, energy = 2, repeat = 1, stairDir = dir, stairSteps = steps, stairRate = 12, stairLead = true, stairType = 0, pz = pz };
    }
    /// <summary>Appends <paramref name="m"/> to the snapshot (the normalisation puts it last in its column; cubes are remapped).</summary>
    static void Add(SongState st, MeasureState m) { var l = st.measures.ToList(); l.Add(m); st.measures = l.ToArray(); }
    static KeyBlock Kind(int kind, int nth = 0) { int k = 0; foreach (var kb in SM.Islands) if (kb != null && kb.kind == kind) { if (k == nth) return kb; k++; } return null; }
    static List<AudioCube> CubesOf(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && !c.rider && !c.IsOnMoon && c.Island == kb).ToList();
    static float Lane(int col) { float b = float.MinValue; foreach (var kb in SM.ColumnIslands(col)) if (kb != null && !kb.IsPhrase) b = Mathf.Max(b, kb.BackEdge); return b; }

    static IEnumerator AllRoutine()
    {
        var sb = new StringBuilder();
        Prepare();
        yield return null;
        Info(sb, "V7ChecksK " + DateTime.Now.ToString("HH:mm:ss") + " — IslandWidth " + F(W) + ", SectionGap " + F(ProjectConfig.SectionGap) + ", SectionBars " + ProjectConfig.SectionBars);

        // ============================================================ SECTIONS (§13.1)
        LoadFixture();
        yield return null;
        Run(sb, "an older song (no sections): one section per 4 measures, computed on load and written by the next snapshot", () =>
        {
            var s = SM.SectionStarts();
            if (SM.SectionCount != 2 || SM.SectionFirst(1) != 4 || SM.SectionLast(0) != 3 || SM.SectionLast(1) != 5) return "sections " + L(s);
            if (SM.SectionBars(0) != 4 || SM.SectionBars(1) != 2) return "bars " + SM.SectionBars(0) + " / " + SM.SectionBars(1);
            if (!Near(SM.SectionStartBeat(1), 16f) || !Near(SM.SectionEndBeat(1), 24f)) return "beats " + F(SM.SectionStartBeat(1)) + " .. " + F(SM.SectionEndBeat(1));
            var st = SongState.Capture();
            if (st.sections == null || L(st.sections) != "[0,4]") return "captured " + (st.sections == null ? "null" : L(st.sections));
            for (int c = 0; c < 6; c++) if (SM.SectionOf(c) != (c < 4 ? 0 : 1)) return "SectionOf(" + c + ") " + SM.SectionOf(c);
            return null;
        });
        int secEvents = 0; Action onSec = () => secEvents++;
        SM.OnSectionsChanged += onSec;
        Run(sb, "explicit sections restore literally (validated: 0 first, ascending, unique, < ColumnCount); OnSectionsChanged when they change", () =>
        {
            var st = SongState.Capture(); st.sections = new[] { 2, 0, 5, 9, 2 };
            secEvents = 0;
            SM.RebuildFromState(st);
            if (L(SM.SectionStarts()) != "[0,2,5]") return "restored " + L(SM.SectionStarts());
            if (secEvents != 1) return "events " + secEvents;
            SM.RebuildFromState(SongState.Capture());
            if (secEvents != 1) return "a rebuild without a change raised it again (" + secEvents + ")";
            return null;
        });
        LoadFixture();
        Run(sb, "a column inserted INSIDE a section joins it (+ after column 1 → section 0 has 5 measures); one History entry", () =>
        {
            int h = History.UndoCount;
            int at = SM.AddMeasure(1);
            if (at < 0 || SM.ColumnOf(at) != 2) return "new island " + at + " col " + SM.ColumnOf(at);
            if (L(SM.SectionStarts()) != "[0,5]") return "sections " + L(SM.SectionStarts());
            if (History.UndoCount != h + 1) return "history " + (History.UndoCount - h);
            return null;
        });
        Run(sb, "undo restores the sections exactly", () =>
        {
            History.Undo();
            return L(SM.SectionStarts()) == "[0,4]" && SM.ColumnCount == 6 ? null : "after undo " + L(SM.SectionStarts()) + " cols " + SM.ColumnCount;
        });
        Run(sb, "at a boundary: the section before while it has < 4 measures, else the one after while it has < 4, else a new section", () =>
        {
            // section 0 is full (4), section 1 has 2: + after column 3 joins section 1 (twice), then both are full: a new section
            SM.AddMeasure(3);
            if (L(SM.SectionStarts()) != "[0,4]" || SM.SectionBars(1) != 3) return "1st: " + L(SM.SectionStarts()) + " bars " + SM.SectionBars(1);
            SM.AddMeasure(3);
            if (L(SM.SectionStarts()) != "[0,4]" || SM.SectionBars(1) != 4) return "2nd: " + L(SM.SectionStarts()) + " bars " + SM.SectionBars(1);
            SM.AddMeasure(3);
            if (L(SM.SectionStarts()) != "[0,4,5]") return "3rd: " + L(SM.SectionStarts());
            return null;
        });
        LoadFixture();
        Run(sb, "appended columns fill the last section to 4 measures, then start a new one (the tray's wand: 3 cards)", () =>
        {
            var cards = new List<SongManager.MeasureData>();
            for (int i = 0; i < 3; i++) cards.Add(MusicTheory.Diatonic(MusicTheory.KeyOfSong(), i + 1));
            int n = SM.PlaceAll(cards);
            if (n != 3 || SM.ColumnCount != 9) return "placed " + n + " cols " + SM.ColumnCount;
            return L(SM.SectionStarts()) == "[0,4,8]" ? null : "sections " + L(SM.SectionStarts());
        });
        LoadFixture();
        Run(sb, "a removed column shifts the later starts; an emptied section disappears", () =>
        {
            var st = SongState.Capture(); st.sections = new[] { 0, 2, 5 };
            SM.RebuildFromState(st); History.Push();
            SM.RemoveMeasure(SM.ColumnFirst(2));   // the first column of section 1
            if (L(SM.SectionStarts()) != "[0,2,4]") return "1st: " + L(SM.SectionStarts());
            SM.RemoveMeasure(SM.ColumnFirst(4));   // section 2's only column
            if (L(SM.SectionStarts()) != "[0,2]" || SM.ColumnCount != 4) return "2nd: " + L(SM.SectionStarts()) + " cols " + SM.ColumnCount;
            return null;
        });
        LoadFixture();
        Run(sb, "a moved column leaves its section and lands by the rule (column 0 → the end: it joins the short last section)", () =>
        {
            SM.MoveColumn(0, 5);
            if (L(SM.SectionStarts()) != "[0,3]") return "sections " + L(SM.SectionStarts());
            if (SM.SectionBars(0) != 3 || SM.SectionBars(1) != 3) return "bars " + SM.SectionBars(0) + " / " + SM.SectionBars(1);
            return null;
        });
        SM.OnSectionsChanged -= onSec;

        // ============================================================ LAYOUT (§13.2)
        LoadFixture();
        yield return null;
        Run(sb, "inside a section the columns TOUCH; between sections the SectionGap (a full section: no ghost measures)", () =>
        {
            for (int c = 0; c < 3; c++) if (!Near(SM.ColumnX(c + 1) - SM.ColumnX(c), W)) return "pitch " + c + " " + F(SM.ColumnX(c + 1) - SM.ColumnX(c));
            if (!Near(SM.ColumnX(4) - SM.ColumnX(3), W + ProjectConfig.SectionGap)) return "gap " + F(SM.ColumnX(4) - SM.ColumnX(3));
            if (!Near(SM.ColumnX(5) - SM.ColumnX(4), W)) return "pitch 4 " + F(SM.ColumnX(5) - SM.ColumnX(4));
            for (int c = 0; c < 5; c++) if (SM.Touching(c) != (c != 3)) return "Touching(" + c + ")";
            return null;
        });
        Run(sb, "a short section reserves its ghost measures before the gap (B draws them): sections [0,2,5] of the fixture", () =>
        {
            var st = SongState.Capture(); st.sections = new[] { 0, 2, 5 };
            SM.RebuildFromState(st);
            float g01 = SM.ColumnX(2) - SM.ColumnX(1), g45 = SM.ColumnX(5) - SM.ColumnX(4);
            if (!Near(g01, W + 2f * W + ProjectConfig.SectionGap)) return "after the 2-measure section " + F(g01) + " want " + F(W * 3 + ProjectConfig.SectionGap);
            if (!Near(g45, W + W + ProjectConfig.SectionGap)) return "after the 3-measure section " + F(g45);
            if (!Near(SM.ColumnX(3) - SM.ColumnX(2), W)) return "inside " + F(SM.ColumnX(3) - SM.ColumnX(2));
            return null;
        });
        LoadFixture();
        Run(sb, "song time maps to x linearly inside a section (XEdgeOfBeat): ½ measure = ½ IslandWidth, a column's start = its west edge", () =>
        {
            float x0 = SM.XEdgeOfBeat(0), x2 = SM.XEdgeOfBeat(2), x4 = SM.XEdgeOfBeat(4), x10 = SM.XEdgeOfBeat(10);
            if (!Near(x2 - x0, W * 0.5f) || !Near(x4, SM.ColumnWestEdge(1)) || !Near(x10 - x0, W * 2.5f)) return F(x0) + " " + F(x2) + " " + F(x4) + " " + F(x10);
            if (!Near(SM.XEdgeOfBeat(16), SM.ColumnWestEdge(4))) return "section 1 start " + F(SM.XEdgeOfBeat(16));
            return null;
        });
        Run(sb, "a 2-measure chord island is two measures wide: its grid in the first, a held-chord surface + a measure divider in the second", () =>
        {
            SM.SetBars(1, 2);
            var kb = SM.Islands[1];
            if (kb.bars != 2 || !Near(kb.Width, 2f * W) || !Near(SM.ColumnWidth(1), 2f * W)) return "width " + F(kb.Width);
            if (!Near(SM.ColumnX(2) - SM.ColumnX(1), 2f * W)) return "next column at " + F(SM.ColumnX(2) - SM.ColumnX(1));
            if (!Near(kb.Center.x - kb.px, W - E)) return "centre offset " + F(kb.Center.x - kb.px);
            var held = kb.HeldMeasures;
            if (held == null) return "no held surface";
            int bars = 0, divs = 0; foreach (Transform t in held) { if (t.name.StartsWith("Held_")) bars++; if (t.name.StartsWith("MeasureDivider_")) divs++; }
            if (bars != kb.rows || divs != 1) return "held bars " + bars + " (rows " + kb.rows + "), dividers " + divs;
            float maxTileX = kb.tiles.Max(t => t.transform.localPosition.x);
            if (maxTileX > W - E) return "a tile outside the first measure: " + F(maxTileX);
            if (SM.SectionBars(0) != 5) return "section bars " + SM.SectionBars(0);
            return null;
        });
        yield return null;
        Frame(SM.FrameSongBounds, 0.8f);
        yield return Wait(0.4f);
        yield return Shot("k7_widths.png");
        Run(sb, "a 2-measure keyboard spreads its 25 keys over both measures (its case spans them)", () =>
        {
            int at = SM.AddKeyboardIsland(3, true);
            if (at < 0) return "no keyboard";
            SM.SetBars(at, 2);
            var kb = SM.Islands.FirstOrDefault(k => k != null && k.IsKeyboard);
            if (kb == null || kb.bars != 2 || !Near(kb.Width, 2f * W)) return "keyboard width " + (kb != null ? F(kb.Width) : "none");
            var last = kb.GetTile(ProjectConfig.KeyboardKeys - 1, 0);
            float lx = last != null ? last.transform.localPosition.x : 0f;
            if (lx < W) return "last key at " + F(lx) + " (not spread)";
            return null;
        });
        LoadFixture();
        Run(sb, "a REWIND island rides no belt and its column needs no belt width (repeat 3: belt 2 slots → rewind: none; passes kept)", () =>
        {
            SM.SetRepeat(1, 3);
            float withBelt = SM.ColumnX(2) - SM.ColumnX(1), total = SM.TotalBeats;
            if (!Near(withBelt, W + 2f * (W + ProjectConfig.BeltGap))) return "belt layout " + F(withBelt);
            if (SM.Islands[1].Belt == null || !SM.Islands[1].HasBelt) return "no belt with repeat 3";
            SM.SetRewind(1, true);
            var kb = SM.Islands[1];
            if (!kb.rewind || kb.HasBelt) return "rewind flag " + kb.rewind + " / belt " + kb.HasBelt;
            if (!Near(SM.ColumnX(2) - SM.ColumnX(1), W)) return "rewind layout " + F(SM.ColumnX(2) - SM.ColumnX(1));
            if (SM.ColumnPasses(1) != 3 || !Near(SM.TotalBeats, total)) return "passes " + SM.ColumnPasses(1) + " total " + F(SM.TotalBeats);
            if (!Near(kb.FootprintBounds.size.x, kb.Width)) return "footprint " + F(kb.FootprintBounds.size.x);
            if (kb.Belt != null && kb.Belt.Slots > 1) return "belt left with " + kb.Belt.Slots + " slots";
            return null;
        });
        LoadFixture();
        Run(sb, "a merge across a section boundary joins the sections (island 4 merged east of island 3: one section of 6 measures)", () =>
        {
            bool ok = SM.MergeIslands(SM.Islands[4], SM.Islands[3], true);
            if (!ok) return "refused";
            if (!SM.Glued(3)) return "not glued";
            if (L(SM.SectionStarts()) != "[0]") return "sections " + L(SM.SectionStarts());
            if (!Near(SM.ColumnX(4) - SM.ColumnX(3), W)) return "pitch " + F(SM.ColumnX(4) - SM.ColumnX(3));
            return null;
        });
        LoadFixture();
        yield return null;
        Frame(SM.FrameSongBounds, 0.85f);
        yield return Wait(0.5f);
        yield return Shot("k7_sections.png");
        Info(sb, "capture k7_sections.png: the fixture's two sections (columns 0-3 touching, the gap, columns 4-5) — k7_widths.png: a 2-measure island + held chord");

        // ============================================================ PHRASES (§14.1 / §14.2)
        LoadFixture();
        {
            var st = SongState.Capture();
            Add(st, PhraseState(1, 8, 0, 1));                      // a 2-measure melody over columns 1-2
            var cs = st.cubes.ToList(); cs.Add(CubeOn(st.measures.Length - 1, 0, new[] { 0, 3, 6 }, new[] { 2, 4, 6 }, (int)StepLen.Quarter)); st.cubes = cs.ToArray();
            SM.RebuildFromState(st); History.Push();
        }
        yield return null;
        Run(sb, "a phrase keeps its kind, never counts as a lane, never stretches its column (bars, passes, length)", () =>
        {
            var ph = Kind(4);
            if (ph == null) return "no phrase after the rebuild";
            if (ph.column != 1 || ph.IsAnchor) return "column " + ph.column + " anchor " + ph.IsAnchor;
            if (SM.LaneCount(1) != 1 || SM.ColumnSize(1) != 2) return "lanes " + SM.LaneCount(1) + " size " + SM.ColumnSize(1);
            if (SM.ColumnBars(1) != 1 || SM.ColumnPasses(1) != 1 || !Near(SM.TotalBeats, 24f)) return "column " + SM.ColumnBars(1) + " bars / " + SM.ColumnPasses(1) + " passes, total " + F(SM.TotalBeats);
            if (!Near(ph.LengthBeats, 8f) || !Near(ph.startBeatOffset, 4f)) return "length " + F(ph.LengthBeats) + " start " + F(ph.startBeatOffset);
            return null;
        });
        Run(sb, "its x = its start in time: the first cell's west edge on column 1's west edge, its time cells exactly two measures (to column 3's)", () =>
        {
            var ph = Kind(4);
            float x0 = ph.px + ph.PhraseCellsX0;
            if (!Near(x0, SM.ColumnWestEdge(1))) return "first cell at " + F(x0) + " want " + F(SM.ColumnWestEdge(1));
            if (!Near(ph.PhraseCellsWidth, 2f * W)) return "cells " + F(ph.PhraseCellsWidth);
            if (!Near(x0 + ph.PhraseCellsWidth, SM.ColumnWestEdge(3))) return "end at " + F(x0 + ph.PhraseCellsWidth) + " want " + F(SM.ColumnWestEdge(3));
            float bx = SM.XEdgeOfBeat(ph.startBeatOffset + 5f);
            if (!Near(bx, x0 + 5f / 8f * ph.PhraseCellsWidth)) return "beat 5 of the phrase at " + F(bx);
            return null;
        });
        Run(sb, "melody TRACKS: behind the back-most lane of its columns (LaneGap); an overlapping phrase takes the next track back", () =>
        {
            var ph = Kind(4);
            float lane = Mathf.Max(Lane(1), Lane(2));
            if (!Near(ph.FrontEdge, lane + ProjectConfig.LaneGap)) return "front " + F(ph.FrontEdge) + " want " + F(lane + ProjectConfig.LaneGap);
            var st = SongState.Capture();
            Add(st, PhraseState(2, 4, 0, 1));                      // overlaps the first one in time (column 2)
            Add(st, PhraseState(4, 4, 0, 1));                      // a phrase in the other section: its own track 0
            SM.RebuildFromState(st);
            var a = Kind(4, 0); var b = Kind(4, 1); var c = Kind(4, 2);
            if (a == null || b == null || c == null) return "phrases " + SM.Islands.Count(k => k != null && k.IsPhrase);
            var first = a.column == 1 ? a : (b.column == 1 ? b : c);
            var second = new[] { a, b, c }.First(k => k.column == 2);
            var third = new[] { a, b, c }.First(k => k.column == 4);
            if (second.FrontEdge < first.BackEdge + ProjectConfig.LaneGap - 1e-3f) return "overlap: " + F(second.FrontEdge) + " vs back " + F(first.BackEdge);
            if (!Near(third.FrontEdge, Lane(4) + ProjectConfig.LaneGap)) return "other section's track 0 at " + F(third.FrontEdge);
            return null;
        });
        Run(sb, "a phrase plays ONE window per song pass over its span (a column repeat does not stretch it); its repeat = windows back to back", () =>
        {
            var ph = SM.Islands.First(k => k != null && k.IsPhrase && k.column == 1);
            var cube = CubesOf(ph).FirstOrDefault();
            if (cube == null) return "no cube on the phrase";
            if (cube.windows.Count != 1 || !Near(cube.windows[0].start, 4f) || !Near(cube.windows[0].length, 8f) || cube.windows[0].pass != 0 || cube.windows[0].island != ph) return "windows " + cube.windows.Count + " start " + F(cube.windows[0].start) + " len " + F(cube.windows[0].length);
            SM.SetRepeat(0, 2);                                    // column 0 now 8 beats: the phrase (column 1) starts at 8
            ph = SM.Islands.First(k => k != null && k.IsPhrase && k.column == 1); cube = CubesOf(ph).FirstOrDefault();
            if (cube == null || cube.windows.Count != 1 || !Near(cube.windows[0].start, 8f)) return "after repeat: " + (cube == null ? "no cube" : cube.windows.Count + " @ " + F(cube.windows[0].start));
            var st = SongState.Capture();
            int pi = SM.Islands.IndexOf(ph); st.measures[pi].repeat = 2;
            SM.RebuildFromState(st);
            ph = SM.Islands.First(k => k != null && k.IsPhrase && k.column == 1); cube = CubesOf(ph).FirstOrDefault();
            if (cube == null || cube.windows.Count != 2 || !Near(cube.windows[1].start, 16f) || cube.windows[1].pass != 1) return "phrase repeat 2: " + (cube == null ? "no cube" : cube.windows.Count + " windows");
            if (SM.ColumnPasses(1) != 1) return "the phrase's repeat added column passes: " + SM.ColumnPasses(1);
            return null;
        });
        LoadFixture();
        Run(sb, "an offset phrase (½ measure in) starts ½ measure later in time and in x", () =>
        {
            var st = SongState.Capture();
            Add(st, PhraseState(1, 4, 2 * ProjectConfig.TicksPerBeat, 1));
            SM.RebuildFromState(st);
            var ph = Kind(4);
            if (ph == null) return "no phrase";
            if (!Near(ph.startBeatOffset, 6f)) return "start " + F(ph.startBeatOffset);
            if (!Near(ph.px + ph.PhraseCellsX0, SM.ColumnWestEdge(1) + W * 0.5f)) return "x " + F(ph.px + ph.PhraseCellsX0);
            return null;
        });
        LoadFixture();
        {
            var st = SongState.Capture();
            Add(st, PhraseState(1, 8, 0, 1));
            Add(st, PhraseState(2, 4, 0, 1));
            SM.RebuildFromState(st);
        }
        yield return null;
        Frame(SM.FrameSongBounds, 0.8f);
        yield return Wait(0.5f);
        yield return Shot("k7_phrase.png");
        Info(sb, "capture k7_phrase.png: a 2-measure phrase over columns 1-2 in its melody track + an overlapping 1-measure phrase a track further back");

        // ============================================================ GROUNDS (§2.5 / §3.3)
        LoadFixture();
        yield return null;
        int groundEvents = 0; Action onGround = () => groundEvents++;
        SM.OnGroundsChanged += onGround;
        {
            var st = SongState.Capture();
            float pz = st.measures[1].pz - KeyBlock.DepthOf(1) - ProjectConfig.LaneGap - 0.5f;
            Add(st, StairState(1, -1, 4, 1, pz));
            groundEvents = 0;
            SM.RebuildFromState(st); History.Push();
        }
        Run(sb, "a DOWN stair of 4 in column 1 lowers the next columns by 1.36 (\"grids spawn in line with the top of the staircase\"); OnGroundsChanged once", () =>
        {
            var s = Kind(3);
            if (s == null || s.column != 1) return "no stairs island";
            if (!Near(SM.StairRiseOf(1), -1.36f)) return "rise " + F(SM.StairRiseOf(1));
            if (!Near(SM.GroundOf(0), 0f) || !Near(SM.GroundOf(1), 0f) || !Near(SM.GroundOf(2), -1.36f) || !Near(SM.GroundOf(5), -1.36f) || !Near(SM.GroundOf(6), -1.36f)) return "grounds " + F(SM.GroundOf(1)) + " " + F(SM.GroundOf(2)) + " " + F(SM.GroundOf(5));
            if (groundEvents != 1) return "events " + groundEvents;
            if (SM.LaneCount(1) != 2) return "lanes " + SM.LaneCount(1);
            return null;
        });
        {
            var k2 = SM.Islands.First(k => k != null && k.column == 2 && !k.IsStairs);
            float y0 = k2.GroundY;
            yield return Wait(0.16f);
            float mid = k2.GroundY;
            yield return Wait(0.75f);
            float end = k2.GroundY;
            float yPlat = k2.transform.position.y - k2.LiftY;
            Line(sb, y0 > -0.05f && mid < -0.02f && mid > -1.34f && Near(end, -1.36f) && Near(yPlat, -1.36f, 0.01f),
                "GroundY GLIDES to its target (ease in-out over GroundGlideSeconds) and the platform follows",
                "shown " + F(y0) + " → " + F(mid) + " (0.16 s) → " + F(end) + "; platform y " + F(yPlat));
        }
        Run(sb, "the hub (cable anchor) and the cubes stand on the ground", () =>
        {
            var k3 = SM.Islands.First(k => k != null && k.column == 3 && !k.IsStairs);
            if (!Near(k3.HubPos.y, 0.15f + k3.LiftY + k3.GroundY, 1e-3f)) return "hub " + F(k3.HubPos.y);
            var cube = CubesOf(k3).FirstOrDefault();
            if (cube != null && cube.nodes.Count > 0 && cube.nodes[0] != null && cube.nodes[0].transform.position.y > k3.transform.position.y + 2f) return "tile far above its island";
            return null;
        });
        Run(sb, "an UP stair lifts the next columns; the clamps (GroundMin −1.6 / GroundMax 4.8)", () =>
        {
            var st = SongState.Capture();
            int si = SM.Islands.IndexOf(Kind(3));
            st.measures[si].stairDir = 1; st.measures[si].stairSteps = 8;
            float pz = st.measures[SM.ColumnFirst(3)].pz - KeyBlock.DepthOf(1) - ProjectConfig.LaneGap - 0.5f;
            Add(st, StairState(3, 1, 8, 1, pz));
            SM.RebuildFromState(st);
            if (!Near(SM.GroundOf(2), 2.72f) || !Near(SM.GroundOf(4), 4.8f)) return "up: " + F(SM.GroundOf(2)) + " / " + F(SM.GroundOf(4));
            st = SongState.Capture();
            foreach (var m in st.measures) if (m.kind == 3) { m.stairDir = -1; m.stairSteps = 8; }
            SM.RebuildFromState(st);
            if (!Near(SM.GroundOf(2), -1.6f) || !Near(SM.GroundOf(4), -1.6f)) return "down: " + F(SM.GroundOf(2)) + " / " + F(SM.GroundOf(4));
            return null;
        });
        {
            // a load of ANOTHER song snaps (no glide); the fixture + a stair saved as another song
            var st = SongState.Capture(); st.name = "k7 ground snap"; st.demoSeed = 4242;
            foreach (var m in st.measures) if (m.kind == 3) { m.stairDir = -1; m.stairSteps = 4; }
            LoadFixture();
            yield return null;
            SongState.Apply(st);
            yield return null;
            var k4 = SM.Islands.First(k => k != null && k.column == 4 && !k.IsStairs);
            Line(sb, Near(k4.GroundY, SM.GroundOf(4)) && SM.GroundOf(4) < -1f, "a LOAD (another song) snaps to the grounds (no glide from the old song's)",
                "column 4 ground " + F(k4.GroundY) + " target " + F(SM.GroundOf(4)));
        }
        Run(sb, "Moons ride the ground of the column they are in line with (interpolated between column centres)", () =>
        {
            int m0 = SM.AddMoon(0), m1 = SM.AddMoon(4);
            if (m0 < 0 || m1 < 0) return "moons " + m0 + " " + m1;
            var a = SM.Moons[m0]; var b = SM.Moons[m1];
            if (!Near(SM.GroundTargetOf(a), SM.GroundOf(0)) || !Near(SM.GroundTargetOf(b), SM.GroundOf(4))) return "targets " + F(SM.GroundTargetOf(a)) + " / " + F(SM.GroundTargetOf(b));
            float mid = SM.GroundAtX((SM.ColumnCenterX(1) + SM.ColumnCenterX(2)) * 0.5f);
            if (!Near(mid, (SM.GroundOf(1) + SM.GroundOf(2)) * 0.5f)) return "between columns 1 and 2: " + F(mid);
            return null;
        });
        {
            yield return Wait(0.8f);
            var b = SM.Moons.Count > 1 ? SM.Moons[1] : null;
            Line(sb, b != null && Near(b.GroundY, SM.GroundOf(4), 0.01f) && Near(b.transform.position.y, SM.GroundOf(4), 0.02f), "a Moon stands on its column's ground (its platform y)",
                b != null ? "moon ground " + F(b.GroundY) + ", y " + F(b.transform.position.y) : "no moon");
        }
        {
            var cam = OrbitCamera.I;
            cam.FrameColumn(4, 0.1f);
            yield return Wait(0.9f);
            float h4 = cam.FocusHeight;
            cam.FrameColumn(0, 0.1f);
            yield return Wait(0.9f);
            float h0 = cam.FocusHeight;
            cam.FrameBounds(SM.FrameSongBounds, 0.1f, true, 0.8f);
            float hAll = cam.FocusHeightTarget;
            Line(sb, Near(h4, SM.GroundOf(4), 0.05f) && Near(h0, 0f, 0.05f) && hAll < -0.1f && hAll > SM.GroundOf(4),
                "the camera frames heights: a lowered column's framing looks at its ground; frame-all spans the heights",
                "column 4 → " + F(h4) + " (ground " + F(SM.GroundOf(4)) + "), column 0 → " + F(h0) + ", all → " + F(hAll));
        }
        yield return Wait(0.5f);
        yield return Shot("k7_grounds.png");
        {
            // a low side view (the rig suspended for the shot): the terraced heights read against the sea
            var rig = OrbitCamera.I; var mc = Camera.main != null ? Camera.main.transform : null;
            if (rig != null && mc != null)
            {
                rig.Suspended = true;
                var gb = SM.FrameSongBounds;
                mc.position = new Vector3(gb.center.x - 4f, 2.2f, gb.min.z - 30f);
                mc.rotation = Quaternion.LookRotation(new Vector3(gb.center.x, -1.2f, gb.center.z) - mc.position, Vector3.up);
                yield return Wait(0.25f);
                yield return Shot("k7_grounds_side.png");
                rig.Suspended = false;
            }
        }
        Info(sb, "capture k7_grounds.png / k7_grounds_side.png: down stairs in columns 1 and 3 (the grounds −1.36, then −1.6 clamped), the Moons on their columns' grounds");
        SM.OnGroundsChanged -= onGround;

        // ============================================================ HOLD THEN RETURN (§12.2–§12.4): pure functions of the beat
        LoadFixture();
        GlobalClock.LoopSong = true;
        SM.SetRepeat(1, 2);   // column 1 = beats 4..12, the song 28 beats
        Run(sb, "BELT: at its last slot after its last pass UNTIL THE SONG RESETS (mid-song, the song's end), then back with a hop, staggered", () =>
        {
            var k1 = SM.Islands[1]; float lift;
            GlobalClock.LoopIndex = 0;
            float a = SM.RideSlot(k1, 6.0, out lift), b = SM.RideSlot(k1, 9.0, out lift), c = SM.RideSlot(k1, 14.0, out lift), d = SM.RideSlot(k1, 27.9, out lift);
            if (!Near(a, 0f) || !Near(b, 1f) || !Near(c, 1f) || !Near(d, 1f)) return "pass 0 " + F(a) + ", pass 1 " + F(b) + ", after " + F(c) + ", song end " + F(d);
            GlobalClock.LoopIndex = 1;
            float stag = SM.ResetStaggerOf(k1);
            float h = SM.RideSlot(k1, Mathf.Max(0.001f, stag - 0.02f), out lift);
            float m = SM.RideSlot(k1, stag + 0.75f, out lift); float mLift = lift;
            float e = SM.RideSlot(k1, stag + ProjectConfig.BeltGlideBeats + 0.05f, out lift);
            GlobalClock.LoopIndex = 0;
            float first = SM.RideSlot(k1, 2.0, out lift);
            Info(sb, "belt (island 1, stagger " + F(stag) + "): pass 0 " + F(a) + " | pass 1 " + F(b) + " | after its turn " + F(c) + " | song end " + F(d) + " | after the loop: " + F(h) + " → " + F(m) + " (hop " + F(mLift) + ") → " + F(e));
            if (h < 0.95f || m <= 0.05f || m >= 0.95f || mLift < 0.2f || !Near(e, 0f)) return "after the reset: " + F(h) + " → " + F(m) + " (lift " + F(mLift) + ") → " + F(e);
            if (!Near(first, 0f)) return "first pass before its turn " + F(first);
            return null;
        });
        Run(sb, "BELTS return slightly out of sync (two belts: different staggers → different slots at the same beat)", () =>
        {
            SM.SetRepeat(4, 2);
            var k1 = SM.Islands[1]; var k4 = SM.Islands[4]; float lift;
            float s1 = SM.ResetStaggerOf(k1), s4 = SM.ResetStaggerOf(k4);
            GlobalClock.LoopIndex = 1;
            double t = Mathf.Min(s1, s4) + 0.6;
            float a = SM.RideSlot(k1, t, out lift), b = SM.RideSlot(k4, t, out lift);
            GlobalClock.LoopIndex = 0;
            Info(sb, "belt staggers " + F(s1) + " / " + F(s4) + ": slots at beat " + F(t) + " after the loop " + F(a) + " / " + F(b));
            return Mathf.Abs(s1 - s4) > 0.02f && Mathf.Abs(a - b) > 0.02f ? null : "in sync: " + F(a) + " / " + F(b);
        });
        LoadFixture();
        GlobalClock.LoopSong = true;
        SM.SetRepeat(0, 2);   // column 0 = beats 0..8 starts the loop
        Run(sb, "a belt whose column STARTS the loop (it plays again right at the reset) glides home in the last beats before the loop point", () =>
        {
            var k0 = SM.Islands[0]; float lift;
            float tot = SM.TotalBeats;
            float a = SM.RideSlot(k0, 9.0, out lift), b = SM.RideSlot(k0, tot - 0.75, out lift), c = SM.RideSlot(k0, tot - 0.01, out lift);
            return Near(a, 1f) && b > 0.05f && b < 0.95f && c < 0.05f ? null : "held " + F(a) + ", " + F(b) + " at −0.75, " + F(c) + " at the loop point";
        });
        LoadFixture();
        GlobalClock.LoopSong = true;
        SM.SetRegister(2, 1);
        Run(sb, "TOWER: 60 % taller (3.2 / octave); up on its turn; STILL UP after it (no bob) until the song resets; then a slow sink, staggered; rest", () =>
        {
            var k2 = SM.Islands[2]; int st; float ph;
            if (!Near(SongManager.TowerTopOf(1), 3.2f) || !Near(SongManager.TowerTopOf(2), 6.4f)) return "top " + F(SongManager.TowerTopOf(1)) + " / " + F(SongManager.TowerTopOf(2));
            GlobalClock.LoopIndex = 0;
            float a = SM.TowerLiftAt(k2, 5.0, out st, out ph); int sa = st;
            float b = SM.TowerLiftAt(k2, 10.0, out st, out ph); int sb2 = st;
            float c = SM.TowerLiftAt(k2, 15.0, out st, out ph); int sc = st; float pc = ph;
            float c2 = SM.TowerLiftAt(k2, 15.5, out st, out ph);
            float d = SM.TowerLiftAt(k2, 23.9, out st, out ph);
            if (!Near(a, 0.3f) || sa != 0) return "before its turn " + F(a) + " state " + sa;
            if (b < 3.2f - 1e-3f || b > 3.2f + ProjectConfig.TowerBob + 1e-3f || sb2 != 2) return "on its turn " + F(b) + " state " + sb2;
            if (!Near(c, 3.2f) || sc != 2 || !Near(pc, 1f) || !Near(c2, 3.2f) || !Near(d, 3.2f)) return "after its turn " + F(c) + " / " + F(c2) + " / " + F(d) + " state " + sc;
            GlobalClock.LoopIndex = 1;
            float stag = SM.ResetStaggerOf(k2);
            float e = SM.TowerLiftAt(k2, Mathf.Max(0.001f, stag - 0.02f), out st, out ph); int se = st;
            float f = SM.TowerLiftAt(k2, stag + 1.5, out st, out ph); int sf = st;
            float g = SM.TowerLiftAt(k2, stag + ProjectConfig.TowerSinkBeats + 0.02, out st, out ph); int sg = st;
            GlobalClock.LoopIndex = 0;
            Info(sb, "tower (island 2, stagger " + F(stag) + "): rest " + F(a) + " | turn " + F(b) + " | after " + F(c) + " " + F(c2) + " " + F(d) + " | after the loop " + F(e) + " → sinking " + F(f) + " → " + F(g));
            if (!Near(e, 3.2f) || se != 2) return "at the loop point " + F(e) + " state " + se;
            if (!Near(f, 1.75f, 0.02f) || sf != 3) return "mid-sink " + F(f) + " state " + sf;
            if (!Near(g, 0.3f) || sg != 0) return "after the sink " + F(g) + " state " + sg;
            return null;
        });
        Run(sb, "TOWERS sink slightly out of sync (islands 2 and 5: their own delays after the reset)", () =>
        {
            SM.SetRegister(5, 1);
            var k2 = SM.Islands[2]; var k5 = SM.Islands[5];
            float s2 = SM.ResetStaggerOf(k2), s5 = SM.ResetStaggerOf(k5);
            GlobalClock.LoopIndex = 1;
            double t = Mathf.Max(s2, s5) + 1.0;
            float a = SM.TowerLiftAt(k2, t), b = SM.TowerLiftAt(k5, t);
            GlobalClock.LoopIndex = 0;
            Info(sb, "tower staggers " + F(s2) + " / " + F(s5) + ": lifts at beat " + F(t) + " after the loop " + F(a) + " / " + F(b));
            return Mathf.Abs(a - b) > 0.05f ? null : "in sync " + F(a) + " / " + F(b);
        });
        Run(sb, "a LOWERED island stays dipped after its turn until the reset, then floats back", () =>
        {
            SM.SetRegister(2, -1);
            var k2 = SM.Islands[2];
            GlobalClock.LoopIndex = 0;
            float a = SM.TowerLiftAt(k2, 15.0);
            GlobalClock.LoopIndex = 1;
            float b = SM.TowerLiftAt(k2, SM.ResetStaggerOf(k2) + ProjectConfig.TowerSinkBeats + 0.05);
            GlobalClock.LoopIndex = 0;
            return Near(a, -0.8f) && Near(b, -0.3f) ? null : "dipped " + F(a) + ", back " + F(b);
        });
        Run(sb, "pure: the same beats give the same lifts / slots twice and after a seek", () =>
        {
            var k2 = SM.Islands[2];
            var beats = new[] { 3.0, 9.3, 13.0, 20.0 };
            var v1 = beats.Select(x => SM.TowerLiftAt(k2, x)).ToArray();
            GlobalClock.Seek(11.0);
            var v2 = beats.Select(x => SM.TowerLiftAt(k2, x)).ToArray();
            GlobalClock.Seek(0.0);
            for (int i = 0; i < beats.Length; i++) if (!Near(v1[i], v2[i], 1e-5f)) return "beat " + beats[i] + ": " + F(v1[i]) + " vs " + F(v2[i]);
            return null;
        });
        LoadFixture();
        GlobalClock.LoopSong = true;
        Run(sb, "MOONS: after its section a Moon HOLDS in line with the section's last column until the reset, then glides home (staggered)", () =>
        {
            int ia = SM.AddMoon(0), ib = SM.AddMoon(3);
            var ma = SM.Moons[ia]; var mb = SM.Moons[ib];
            float homeA = SM.ColumnCenterX(0), lastA = SM.ColumnCenterX(2), homeB = SM.ColumnCenterX(3), lastB = SM.ColumnCenterX(5);
            GlobalClock.LoopIndex = 0;
            float held = SM.MoonFollowX(ma, 13.0);
            if (!Near(held, lastA - homeA)) return "moon A after its section " + F(held) + " want " + F(lastA - homeA);
            // moon A's section starts the loop: it is home by the loop point (it plays again right away)
            float tot = SM.TotalBeats;
            float early = SM.MoonFollowX(ma, tot - 2.5), late = SM.MoonFollowX(ma, tot - 0.02);
            if (!Near(early, lastA - homeA) || Mathf.Abs(late) > 0.25f * Mathf.Abs(lastA - homeA)) return "moon A before the loop point " + F(early) + " → " + F(late);
            // moon B's section ends the song: after the reset it holds at column 5, then glides home
            float inB = SM.MoonFollowX(mb, 23.0);
            GlobalClock.LoopIndex = 1;
            float sB = SM.ResetStaggerOf(mb), dur = SongManager.MoonStepBeats(Mathf.Abs(lastB - homeB));
            float h0 = SM.MoonFollowX(mb, Mathf.Max(0.001f, sB - 0.02f)), h1 = SM.MoonFollowX(mb, sB + dur * 0.5f), h2 = SM.MoonFollowX(mb, sB + dur + 0.05f);
            GlobalClock.LoopIndex = 0;
            Info(sb, "moons: A held " + F(held) + ", before the loop point " + F(early) + " → " + F(late) + "; B in its section " + F(inB) + ", after the loop " + F(h0) + " → " + F(h1) + " → " + F(h2) + " (stagger " + F(sB) + ")");
            if (!Near(inB, lastB - homeB)) return "moon B in its last column " + F(inB);
            if (!Near(h0, lastB - homeB) || h1 <= 0.05f || h1 >= lastB - homeB - 0.05f || !Near(h2, 0f)) return "moon B after the loop " + F(h0) + " → " + F(h1) + " → " + F(h2);
            return null;
        });
        // ---- a STOP animates the trip home in real time (never a snap)
        LoadFixture();
        GlobalClock.LoopSong = true;
        SM.SetRegister(2, 1);
        SM.SetRepeat(1, 2);
        {
            // column 1 (repeat 2) = beats 4..12, column 2 (the tower) = 12..16: at ≈ 17.5 both have had their turn — the belt held at its last slot,
            // the tower still up
            GlobalClock.Seek(16.9);
            GlobalClock.Play();
            yield return Wait(0.7f);
            var k2 = SM.Islands[2]; var k1 = SM.Islands[1];
            float upLift = k2.LiftY, upSlot = k1.RideSlotShown; int upState = k2.TowerState;
            var hb = SM.ColumnBounds(1); hb.Encapsulate(SM.ColumnBounds(3)); hb.Encapsulate(hb.center + Vector3.up * 3.5f);
            Frame(hb, 1.0f);
            yield return Wait(0.3f);
            yield return Shot("k7_hold.png");
            GlobalClock.Stop();
            yield return null; yield return null;
            float lift1 = k2.LiftY, slot1 = k1.RideSlotShown;
            bool trip = k2.ResetTripRunning;
            float spb = (float)(1.0 / GlobalClock.BeatsPerSecond);
            yield return Wait((ProjectConfig.TowerSinkBeats + ProjectConfig.ResetStaggerBeats) * spb + 0.5f);
            float lift2 = k2.LiftY, slot2 = k1.RideSlotShown;
            Line(sb, Near(upLift, 3.2f, 0.01f) && upState == 2 && upSlot > 0.99f && lift1 > 3.1f && slot1 > 0.95f && trip && Near(lift2, 0.3f, 0.02f) && Near(slot2, 0f, 0.02f),
                "a STOP resets the song: the held tower sinks and the belt glides home IN REAL TIME (two frames after the stop they have barely moved)",
                "held (beat ≈ 17.5, both after their turns): lift " + F(upLift) + " state " + upState + ", slot " + F(upSlot) + " → 2 frames after the stop " + F(lift1) + " / " + F(slot1) + " (trip " + trip + ") → later " + F(lift2) + " / " + F(slot2));
        }
        Info(sb, "capture k7_hold.png: beat ≈ 17.5 — column 2's tower STILL UP after its turn (3.2 u), island 1 HELD at its belt's last slot, column 3 playing");

        // ============================================================ REWIND (§2.6) + WINDOWS (§2.6 / §2.7) + LAUNCH (§2.8)
        LoadFixture();
        SM.SetRepeat(1, 3);
        SM.SetRewind(1, true);
        Run(sb, "RewindPhase (pure): the last RewindBeats of every pass but the last of a rewind island; −1 elsewhere and for other islands", () =>
        {
            var k1 = SM.Islands[1]; int p;
            double[] beats = { 7.0, 7.5, 7.75, 11.75, 15.75, 3.0, 16.5 };
            var got = new List<string>();
            float[] want = { -1f, 0f, 0.5f, 0.5f, -1f, -1f, -1f };
            int[] wantP = { -1, 0, 0, 1, -1, -1, -1 };
            for (int i = 0; i < beats.Length; i++)
            {
                float v = SM.RewindPhase(k1, beats[i], out p);
                got.Add(F(beats[i]) + ":" + F(v) + "/" + p);
                if (!Near(v, want[i]) || p != wantP[i]) return "beat " + F(beats[i]) + " → " + F(v) + " pass " + p;
                float v2 = SM.RewindPhase(k1, beats[i], out p);
                if (!Near(v, v2)) return "not pure at " + F(beats[i]);
            }
            if (SM.RewindPhase(SM.Islands[0], 7.75, out p) != -1f) return "a belt island unwinds";
            Info(sb, "RewindPhase samples " + string.Join(" ", got.ToArray()));
            return null;
        });
        Run(sb, "a rewind island's gauge fills per pass and DRAINS back during the unwind (W's time unwinding)", () =>
        {
            var k1 = SM.Islands[1];
            GlobalClock.Seek(7.0); float a = k1.GaugeTarget();
            GlobalClock.Seek(7.75); float b = k1.GaugeTarget();
            GlobalClock.Seek(8.5); float c = k1.GaugeTarget();
            GlobalClock.Seek(15.0); float d = k1.GaugeTarget();
            GlobalClock.Seek(0.0);
            return Near(a, 0.75f) && Near(b, 0.4375f) && Near(c, 0.125f) && Near(d, 0.75f) ? null : F(a) + " " + F(b) + " " + F(c) + " " + F(d);
        });
        LoadFixture();
        Run(sb, "WINDOWS: pass on every window (home passes 0..n−1; carried = the target column's pass) and flow on carried windows; §21: the carry style is ignored (SetCarry(.., 0) still flows, stored as 1)", () =>
        {
            SM.SetRepeat(1, 2);
            var cube = CubesOf(SM.Islands[1]).FirstOrDefault();
            if (cube == null) return "no cube on island 1";
            if (cube.windows.Count != 2 || cube.windows[0].pass != 0 || cube.windows[1].pass != 1) return "home windows " + cube.windows.Count;
            SM.SetCarry(1, 2, 1);
            cube = CubesOf(SM.Islands[1]).FirstOrDefault();
            var carried = cube.windows.Where(w => w.carried).ToList();
            if (carried.Count != 2 || carried.Any(w => !w.flow || w.pass != 0) || carried[0].hop != 1 || carried[1].hop != 2) return "carried " + carried.Count + " flow " + string.Join(",", carried.Select(w => w.flow + "/" + w.pass).ToArray());
            if (cube.windows.Where(w => !w.carried).Any(w => w.flow)) return "a home window flows";
            SM.SetCarry(1, 2, 0);
            cube = CubesOf(SM.Islands[1]).FirstOrDefault();
            carried = cube.windows.Where(w => w.carried).ToList();
            if (carried.Count != 2 || carried.Any(w => !w.flow)) return "§21: a hop style is gone — carried " + carried.Count + " flow " + string.Join(",", carried.Select(w => w.flow.ToString()).ToArray());
            if (SM.Islands[1].carryStyle != 1) return "§21: carryStyle stored " + SM.Islands[1].carryStyle;
            var c0 = CubesOf(SM.Islands[0]).FirstOrDefault();
            if (c0 == null || c0.windows.Any(w => w.pass != 0)) return "island 0 windows";
            return null;
        });
        // ================================================================ §21 THE LONG GRID (cubes stay on their grid; carry = extend)
        LoadFixture();
        yield return null;
        GridSelection.Clear();   // (a selected grid is lifted by W's selection look: its joints open while it floats)
        int lgEvents = 0;
        Action onLg = () => lgEvents++;
        SM.OnLongGridsChanged += onLg;
        SM.SetCarry(0, 2);
        yield return Settled();   // the covered grids glide into the source's lane (0.32 s); the joined look is decided in LateUpdate
        {
            // the joints close as soon as the platforms meet (a transient lift / glide keeps one open until it lands): wait for them, note how long
            string firstLook = Joints() + " | island 2: tower " + F(SM.Islands[2].TowerLift) + " ground " + F(SM.Islands[2].GroundY) + " belt y " + F(SM.Islands[2].BeltOffset.y) + " slide " + SM.Islands[2].SlideOffset.ToString("F3");
            float tj = Time.realtimeSinceStartup;
            Func<bool> allJoined = () => SM.Islands[0].JoinedEast && SM.Islands[1].JoinedWest && SM.Islands[1].JoinedEast && SM.Islands[2].JoinedWest;
            while (Time.realtimeSinceStartup - tj < 3f && !allJoined()) yield return null;
            Info(sb, "§21 the joints after SetCarry + settle: " + (allJoined() ? "joined after " + F(Time.realtimeSinceStartup - tj) + " s" : "NOT joined after 3 s") + " — at first: " + firstLook);
        }
        Run(sb, "§21 LONG GRID: SetCarry(0, 2) joins columns 0-2 into ONE long grid — LongGridOf = [0, 1, 2] (source first) for every member, InLongGrid, the neighbours; column 3 stays out; OnLongGridsChanged", () =>
        {
            var I = SM.Islands;
            var run = SM.LongGridOf(I[0]);
            if (run == null || run.Count != 3 || run[0] != I[0] || run[1] != I[1] || run[2] != I[2]) return "LongGridOf(0) " + (run == null ? "null" : string.Join(",", run.Select(k => k.column.ToString()).ToArray()));
            if (SM.LongGridOf(I[1]) != run || SM.LongGridOf(I[2]) != run) return "members see another list";
            if (SM.LongGridOf(I[3]) != null || I[3].InLongGrid) return "column 3 joined";
            if (!I[0].InLongGrid || !I[1].InLongGrid || !I[2].InLongGrid) return "InLongGrid";
            if (I[0].LongGridWest != null || I[0].LongGridEast != I[1] || I[1].LongGridWest != I[0] || I[1].LongGridEast != I[2] || I[2].LongGridEast != null) return "neighbours";
            if (SM.LongGrids.Count != 1) return "long grids " + SM.LongGrids.Count;
            if (lgEvents < 1) return "OnLongGridsChanged " + lgEvents;
            var t = SM.CarryTargets(0);
            if (t.Count != 2 || t[0] != I[1] || t[1] != I[2]) return "CarryTargets";
            return null;
        });
        Run(sb, "§21 LONG GRID LOOK: the members touch (no gap: platform bounds meet, front edges aligned), stand as deep as the deepest member (the 5-row maj9), their joined ends are OPEN and SQUARE (no end face, the top at full height to the joint: no notch, no inner outline), the outer ends stay rounded; a thin ink divider on each joint", () =>
        {
            var I = SM.Islands;
            if (!I[0].JoinedEast || !I[1].JoinedWest || !I[1].JoinedEast || !I[2].JoinedWest) return "joined " + I[0].JoinedEast + I[1].JoinedWest + I[1].JoinedEast + I[2].JoinedWest + " | " + Joints();
            if (I[0].JoinedWest || I[2].JoinedEast) return "an outer end joined";
            float dMax = Mathf.Max(I[0].Depth, Mathf.Max(I[1].Depth, I[2].Depth));
            for (int k = 0; k < 3; k++)
            {
                if (!Near(I[k].PlatformDepth, dMax)) return "platform depth of " + k + " " + F(I[k].PlatformDepth) + " want " + F(dMax);
                if (!Near(I[k].FrontEdge, I[0].FrontEdge)) return "front edge of " + k;
            }
            var pr = new Renderer[3]; var pm = new Mesh[3];
            for (int k = 0; k < 3; k++) { var p = I[k].transform.Find("Platform"); pr[k] = p != null ? p.GetComponent<Renderer>() : null; pm[k] = p != null ? p.GetComponent<MeshFilter>().sharedMesh : null; if (pr[k] == null || pm[k] == null) return "no platform " + k; }
            for (int k = 0; k < 2; k++)
            {
                float gap = pr[k + 1].bounds.min.x - pr[k].bounds.max.x;
                if (Mathf.Abs(gap) > 0.02f) return "gap " + k + "|" + (k + 1) + " = " + F(gap);
                if (Mathf.Abs(pr[k + 1].bounds.min.z - pr[k].bounds.min.z) > 0.02f || Mathf.Abs(pr[k + 1].bounds.max.z - pr[k].bounds.max.z) > 0.02f) return "platform z " + k;
            }
            Func<Mesh, int, int> endFace = (m, side) => m.normals.Count(n => side < 0 ? n.x < -0.9f : n.x > 0.9f);
            Func<Mesh, float, bool> squareTop = (m, sx) => { float hx = m.bounds.extents.x, cx = m.bounds.center.x, top = m.bounds.max.y; var v = m.vertices; var nn = m.normals; for (int q = 0; q < v.Length; q++) if (Mathf.Abs(v[q].x - (cx + sx * hx)) < 1e-4f && nn[q].y > 0.99f && Mathf.Abs(v[q].y - top) < 1e-4f) return true; return false; };
            if (endFace(pm[0], -1) == 0 || endFace(pm[0], 1) != 0) return "island 0: west end face " + endFace(pm[0], -1) + ", east " + endFace(pm[0], 1);
            if (endFace(pm[1], -1) != 0 || endFace(pm[1], 1) != 0) return "island 1 has an end face";
            if (endFace(pm[2], -1) != 0 || endFace(pm[2], 1) == 0) return "island 2: west " + endFace(pm[2], -1) + ", east " + endFace(pm[2], 1);
            if (!squareTop(pm[0], 1f) || !squareTop(pm[1], -1f) || !squareTop(pm[1], 1f) || !squareTop(pm[2], -1f)) return "a notch: the top does not reach a joint at full height";
            if (squareTop(pm[0], -1f) || squareTop(pm[2], 1f)) return "an outer end is square";
            if (I[0].LongGridDivider != null || I[1].LongGridDivider == null || I[2].LongGridDivider == null || I[3].LongGridDivider != null) return "dividers";
            if (I[0].TrackMaskedVerts == 0 || I[1].TrackMaskedVerts <= I[0].TrackMaskedVerts || I[3].TrackMaskedVerts != 0) return "beat track corner arcs at the joints: masked " + I[0].TrackMaskedVerts + " / " + I[1].TrackMaskedVerts + " / " + I[3].TrackMaskedVerts;
            for (int k = 1; k < 3; k++) { float dx = I[k].LongGridDivider.position.x - pr[k].bounds.min.x; if (Mathf.Abs(dx) > 0.02f) return "divider " + k + " off the joint by " + F(dx); }
            Info(sb, "long grid 0-2: depths " + F(I[0].Depth) + " / " + F(I[1].Depth) + " / " + F(I[2].Depth) + " → platforms " + F(dMax) + "; joints at x " + F(pr[0].bounds.max.x) + " / " + F(pr[1].bounds.max.x) + "; mesh verts " + pm[0].vertexCount + " / " + pm[1].vertexCount + " / " + pm[2].vertexCount + "; beat-track verts hidden at the joints " + I[0].TrackMaskedVerts + " / " + I[1].TrackMaskedVerts + " / " + I[2].TrackMaskedVerts);
            return null;
        });
        {
            // the long grid to look at: columns 0-2 joined (the third one a 5-row maj9: the others stand as deep), column 3 on its own, then the section gap
            var b = SM.ColumnBounds(0); for (int c = 1; c < 4; c++) b.Encapsulate(SM.ColumnBounds(c));
            Frame(b, 0.95f);
            yield return Wait(0.5f);
            yield return Settled();
            yield return Shot("k7_longgrid.png");
            Info(sb, "capture k7_longgrid.png: columns 0-2 joined into one long grid (extend ×2 from column 0), column 3 alone (rounded), the section gap after it");
            var jb = SM.Islands[1].VisualBounds; jb.Encapsulate(SM.Islands[2].VisualBounds);
            jb = new Bounds(new Vector3(SM.Islands[2].WestEdge, jb.center.y, jb.center.z), new Vector3(7f, jb.size.y, jb.size.z));   // the joint, close
            Frame(jb, 0.6f);
            yield return Wait(0.5f);
            yield return Settled();
            yield return Shot("k7_longgrid_joint.png");
            Info(sb, "capture k7_longgrid_joint.png: the joint between columns 1 (Dm7, 4 rows: as deep as the long grid) and 2 (D#maj9, 5 rows) up close — " + Joints());
        }
        {
            var I = SM.Islands;
            I[1].SetPreviewOffset(new Vector3(0f, 0f, 1.2f));
            yield return Wait(0.6f);
            bool apart = !I[0].JoinedEast && !I[1].JoinedWest && !I[1].JoinedEast && !I[2].JoinedWest && I[1].LongGridDivider == null;
            I[1].SetPreviewOffset(Vector3.zero);
            yield return Wait(0.8f);
            bool back = I[0].JoinedEast && I[1].JoinedWest && I[1].JoinedEast && I[2].JoinedWest && I[1].LongGridDivider != null;
            Line(sb, apart && back, "§21 LONG GRID JOINT follows the platforms: a neighbour moved away (a preview offset) rounds both ends at once, back home they join again", "apart " + apart + ", joined again " + back);
        }
        SM.OnLongGridsChanged -= onLg;
        Run(sb, "§21 CLAMPS: a long grid never crosses its section (carry 3 from column 2 → column 3 only; column 3, the section's last → none; carry 3 from column 4 → column 5) and stops at a column with no grid in its lane", () =>
        {
            SM.SetCarry(2, 3);
            var t = SM.CarryTargets(2);
            if (t.Count != 1 || t[0] != SM.Islands[3]) return "from column 2: " + t.Count;
            SM.SetCarry(3, 2);
            if (SM.CarryTargets(3).Count != 0) return "from column 3 (the section's last): " + SM.CarryTargets(3).Count;
            SM.SetCarry(4, 3);
            t = SM.CarryTargets(4);
            if (t.Count != 1 || t[0] != SM.Islands[5]) return "from column 4: " + t.Count;
            // the lane: column 2's grid moved out of column 1's lane (12 u to the front) → column 0's extend stops after column 1
            LoadFixture();
            var st = SongState.Capture();
            st.measures[0].carry = 3;
            st.measures[2].pz -= 12f;
            SM.RebuildFromState(st);
            t = SM.CarryTargets(0);
            if (t.Count != 1 || t[0] != SM.Islands[1]) return "lane: " + string.Join(",", t.Select(k => k.column + "@" + F(k.FrontEdge)).ToArray());
            var c0 = CubesOf(SM.Islands[0]).FirstOrDefault();
            if (c0 == null || c0.windows.Count(w => w.carried) != 1) return "lane: carried windows " + (c0 != null ? c0.windows.Count(w => w.carried) : -1);
            return null;
        });
        Run(sb, "§21 ALIGN: a covered grid half a lane off joins the source's z (the layout aligns the long grid); an in-column z move of any member moves the whole long grid", () =>
        {
            LoadFixture();
            var st = SongState.Capture();
            st.measures[0].carry = 1;
            float z1 = st.measures[1].pz;
            st.measures[1].pz += 1.0f;
            SM.RebuildFromState(st);
            var I = SM.Islands;
            if (SM.CarryTargets(0).Count != 1) return "no target";
            if (!Near(I[1].pz, I[0].pz)) return "not aligned: " + F(I[1].pz) + " vs " + F(I[0].pz) + " (was " + F(z1) + " + 1)";
            float z0 = I[0].pz, want = Mathf.Round((z0 + 2f) / ProjectConfig.IslandSnap) * ProjectConfig.IslandSnap;   // the commit snaps to the drag grid
            SM.MoveIslandInColumn(1, z0 + 2f, true);
            if (!Near(I[0].pz, I[1].pz) || !Near(I[1].pz, want)) return "z move: " + F(I[0].pz) + " / " + F(I[1].pz) + " want " + F(want);
            if (SM.CarryTargets(0).Count != 1) return "z move broke the long grid";
            return null;
        });
        Run(sb, "§21 RIDER: a v4 Rider plays only on its home grid — exactly a resident's windows (with the home's long grid: its carried windows; without: its own passes)", () =>
        {
            LoadFixture();
            SM.SetCarry(1, 1);
            var rider = PathManager.I.RestoreCube(new CubeState { instrument = 4, measure = 1, moon = -1, xs = new[] { 0 }, zs = new[] { 0 }, rests = new bool[1], step = 1, gate = 1, volume = 1f, hits = -1, rider = true, twinOf = -1 });
            SM.RecomputeMeasureStarts();
            var res = CubesOf(SM.Islands[1]).FirstOrDefault();
            if (rider == null || res == null) return "no rider / resident";
            Func<AudioCube, string> sig = c => string.Join(" ", c.windows.Select(w => F(w.start) + "+" + F(w.length) + "@" + (w.island != null ? w.island.column : -1) + (w.carried ? "c" + w.hop : "") + (w.flow ? "f" : "")).ToArray());
            if (sig(rider) != sig(res)) return "rider " + sig(rider) + " vs resident " + sig(res);
            if (rider.windows.Any(w => w.island != SM.Islands[1] && w.island != SM.Islands[2])) return "a window off the long grid";
            SM.SetCarry(1, 0);
            rider = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.rider);
            if (rider == null) return "rider lost";
            if (rider.windows.Count != SM.Islands[1].Passes || rider.windows.Any(w => w.island != SM.Islands[1] || w.carried)) return "without carry: " + sig(rider);
            Info(sb, "rider windows (home 1, no carry): " + sig(rider));
            return null;
        });
        Run(sb, "§21 LOAD: a file's hop-style carry (carryStyle 0) loads as a long grid — carryStyle 1, flow windows, joined", () =>
        {
            LoadFixture();
            var st = SongState.Capture();
            st.measures[0].carry = 2; st.measures[0].carryStyle = 0;
            var json = st.ToJson();
            if (!json.Contains("\"carryStyle\":0")) return "json has no carryStyle 0";
            SongState.Apply(SongState.FromJson(json));
            var kb = SM.Islands[0];
            if (kb.carry != 2 || kb.carryStyle != 1) return "carry " + kb.carry + " style " + kb.carryStyle;
            var c0 = CubesOf(kb).FirstOrDefault();
            if (c0 == null) return "no cube";
            var cw = c0.windows.Where(w => w.carried).ToList();
            if (cw.Count != 2 || cw.Any(w => !w.flow)) return "carried " + cw.Count + " flow " + string.Join(",", cw.Select(w => w.flow.ToString()).ToArray());
            var run = SM.LongGridOf(kb);
            if (run == null || run.Count != 3) return "long grid " + (run != null ? run.Count : 0);
            if (SongState.Capture().measures[0].carryStyle != 1) return "captured style " + SongState.Capture().measures[0].carryStyle;
            return null;
        });
        LoadFixture();
        Run(sb, "LAUNCH: the next column's island (the first column's when the song loops), the turn's end beat; stairs never launch", () =>
        {
            var k1 = SM.Islands[1];
            if (SM.LaunchTarget(k1) != SM.Islands[2] || !Near(SM.TurnEnd(k1), 8f)) return "island 1 → " + (SM.LaunchTarget(k1) != null ? SM.LaunchTarget(k1).column.ToString() : "null") + " at " + F(SM.TurnEnd(k1));
            SM.SetRepeat(1, 2);
            k1 = SM.Islands[1];
            if (!Near(SM.TurnEnd(k1), 12f)) return "repeat 2 turn end " + F(SM.TurnEnd(k1));
            var last = SM.Islands[SM.Islands.Count - 1];
            GlobalClock.LoopSong = true;
            if (SM.LaunchTarget(last) != SM.Islands[0]) return "last column when looping";
            GlobalClock.LoopSong = false;
            bool none = SM.LaunchTarget(last) == null;
            GlobalClock.LoopSong = true;
            if (!none) return "last column without the loop";
            var st = SongState.Capture();
            Add(st, StairState(2, -1, 4, 1, st.measures[SM.ColumnFirst(2)].pz - 4.5f));
            SM.RebuildFromState(st);
            if (SM.LaunchTarget(Kind(3)) != null) return "stairs launch";
            return null;
        });

        // ============================================================ LIVE RESIZE (§19.1)
        LoadFixture();
        yield return null;
        {
            var k1 = SM.Islands[1];
            var tile = k1.GetTile(2, 1);
            int h = History.UndoCount; float total = SM.TotalBeats; float x2 = SM.ColumnX(2);
            int ifR = SM.SectionBarsIfResized(1, 2);
            bool ok = SM.ResizeColumnLive(1, 2);
            yield return Wait(0.4f);
            bool alive = k1 != null && SM.Islands[1] == k1 && tile != null && k1.GetTile(2, 1) == tile;
            Line(sb, ok && alive && k1.bars == 2 && Near(k1.Width, 2f * W) && Near(SM.TotalBeats, total + 4f) && Near(SM.ColumnX(2) - x2, W) && History.UndoCount == h && ifR == 5 && SM.SectionBars(0) == 5 && k1.HeldMeasures != null,
                "ResizeColumnLive (§19.1): a column grows a measure LIVE — the same island and tiles (a draft survives), the timeline + layout follow, no History",
                "ok " + ok + ", same island / tile " + alive + ", bars " + k1.bars + ", width " + F(k1.Width) + ", total " + F(total) + " → " + F(SM.TotalBeats) + ", next column moved " + F(SM.ColumnX(2) - x2) + ", history +" + (History.UndoCount - h) + ", section bars (if) " + ifR);
            SM.ResizeColumnLive(1, 1);
            yield return Wait(0.35f);
            Line(sb, SM.Islands[1] == k1 && k1.bars == 1 && Near(SM.TotalBeats, total) && Near(SM.ColumnX(2), x2) && k1.HeldMeasures == null, "and shrinks back live",
                "bars " + k1.bars + ", total " + F(SM.TotalBeats) + ", column 2 at " + F(SM.ColumnX(2)) + " (was " + F(x2) + ")");
        }

        // ============================================================ the mid-build hooks: W's FxLift, U1's stairs ghost, U2's Shift / Alt arrows
        LoadFixture();
        yield return null;
        Run(sb, "FxLift (W): a purely visual lift moves the platform, its hub (the cable's anchor) and back to exactly 0", () =>
        {
            var kb = SM.Islands[2];
            float y0 = kb.transform.position.y, h0 = kb.HubPos.y;
            kb.SetFxLift(0.5f);
            float y1 = kb.transform.position.y, h1 = kb.HubPos.y; bool flag = kb.FxLifting;
            kb.SetFxLift(0f);
            float y2 = kb.transform.position.y;
            return Near(y1 - y0, 0.5f) && Near(h1 - h0, 0.5f) && flag && Near(y2, y0) ? null : "y " + F(y0) + " → " + F(y1) + " → " + F(y2) + ", hub +" + F(h1 - h0) + ", flag " + flag;
        });
        Run(sb, "the STAIRS card's ghost (U1): one row deep, a stone staircase, never a MERGE slot (beside the song's last island = a new column)", () =>
        {
            var md = new SongManager.MeasureData { chordKey = "Stairs", chordRootMIDI = 60, semitones = new[] { 0 }, bars = 1, kind = 3, stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true, energy = 2, repeat = 1 };
            var g = IslandGhost.Create(md);
            try
            {
                if (!g.IsStairs || !Near(g.Depth, KeyBlock.DepthOf(1))) return "stairs " + g.IsStairs + " depth " + F(g.Depth);
                var last = SM.Islands[SM.Islands.Count - 1];
                g.Track(new Vector3(last.EastEdge + 0.9f + W * 0.5f, 0f, last.Center.z));
                if (g.State == IslandGhost.Mode.Merge) return "offered a merge";
                if (g.State != IslandGhost.Mode.Insert || g.InsertAt != SM.ColumnCount) return "state " + g.State + " insert " + g.InsertAt;
                return null;
            }
            finally { UnityEngine.Object.Destroy(g.gameObject); }
        });
        {
            var cam = OrbitCamera.I;
            Vector3 f0 = cam.Focus;
            KeyShim.Sim(KeyCode.LeftShift, false, true, false);
            OrbitCamera.SimKeyHold(KeyCode.RightArrow, true);
            yield return Wait(0.4f);
            Vector3 f1 = cam.Focus;
            OrbitCamera.SimKeyHold(KeyCode.RightArrow, false);
            KeyShim.Clear();
            yield return Wait(0.3f);
            Vector3 f2 = cam.Focus;
            OrbitCamera.SimKeyHold(KeyCode.RightArrow, true);
            yield return Wait(0.4f);
            Vector3 f3 = cam.Focus;
            OrbitCamera.SimKeyHold(KeyCode.RightArrow, false);
            yield return Wait(0.4f);
            float shiftMove = new Vector2(f1.x - f0.x, f1.z - f0.z).magnitude, plainMove = new Vector2(f3.x - f2.x, f3.z - f2.z).magnitude;
            Line(sb, shiftMove < 0.05f && plainMove > 1f, "Shift + → is a shortcut (U2), never a camera pan; → alone still pans",
                "with Shift " + F(shiftMove) + " u, without " + F(plainMove) + " u");
        }

        // ============================================================ §20.1 the extensible keyboard ("it should also be extensible if needed")
        LoadFixture();
        yield return null;
        int kbi = SM.AddKeyboardIsland(SM.ColumnFirst(1), true);
        yield return Wait(0.7f);
        KeyBlock keys = kbi >= 0 && kbi < SM.Islands.Count ? SM.Islands[kbi] : null;
        AudioCube kcube = null;
        if (keys != null)
        {
            kcube = PathManager.I.RestoreCube(CubeOn(kbi, 0, new[] { 3, 10, 20 }, new[] { 0, 0, 0 }, (int)StepLen.Quarter));
            SM.RecomputeMeasureStarts();
        }
        System.Func<AudioCube, string> pitches = c => c == null ? "none" : string.Join(",", c.nodes.Select(n => n != null ? n.midi.ToString() : "x").ToArray());
        string p0 = pitches(kcube);
        int low0 = keys != null ? keys.chordRootMIDI : 0;
        Run(sb, "a new keyboard: 25 keys (keyCount) from its lowest key (the stored root), every key's pitch = lowest + index", () =>
        {
            if (keys == null || !keys.IsKeyboard) return "no keyboard (" + kbi + ")";
            if (keys.KeyCount != 25 || keys.cols != 25 || keys.tiles.Count != 25) return "keys " + keys.KeyCount + " / " + keys.cols + " / " + keys.tiles.Count;
            for (int k = 0; k < 25; k++) if (keys.GetTile(k, 0) == null || keys.GetTile(k, 0).midi != keys.chordRootMIDI + k) return "key " + k;
            if (kcube == null || p0 != (low0 + 3) + "," + (low0 + 10) + "," + (low0 + 20)) return "cube " + p0;
            return null;
        });
        float w25 = keys != null ? keys.KeyWidthOf(0) : 0f;
        List<TileInteraction> objs25 = keys != null ? new List<TileInteraction>(keys.tiles) : new List<TileInteraction>();
        bool ext1 = false; int hExt = History.UndoCount; float slideTopX = 0f, restTopX = 0f;
        if (keys != null)
        {
            ext1 = SM.ExtendKeysLive(keys, +1);
            var top = keys.GetTile(keys.cols - 1, 0);
            slideTopX = top != null ? top.Top.x : 0f;          // shown this frame (the bed still where the old keys were)
            yield return Wait(0.55f);
            restTopX = top != null ? top.Top.x : 0f;
        }
        Run(sb, "ExtendKeysLive +1 (LIVE: the same island and key objects, no History): 37 keys, the new octave on top; v8: the piano WIDENS at the same key size (\"stretch the piano horizontally not condense the keys\"); every note keeps its pitch", () =>
        {
            if (!ext1) return "refused";
            if (SM.Islands[kbi] != keys || keys.KeyCount != 37 || keys.cols != 37) return "keys " + keys.KeyCount;
            for (int k = 0; k < 25; k++) if (keys.GetTile(k, 0) != objs25[k]) return "key object " + k + " replaced";
            for (int k = 0; k < 37; k++) if (keys.GetTile(k, 0).midi != keys.chordRootMIDI + k || keys.GetTile(k, 0).gridX != k) return "key " + k + " midi / index";
            if (keys.chordRootMIDI != low0 || History.UndoCount != hExt) return "lowest " + keys.chordRootMIDI + ", history +" + (History.UndoCount - hExt);
            if (pitches(kcube) != p0) return "cube pitches " + pitches(kcube) + " (were " + p0 + ")";
            if (Mathf.Abs(keys.KeyWidthOf(0) - w25) > 0.03f) return "key width changed: " + F(keys.KeyWidthOf(0)) + " vs " + F(w25);
            if (keys.Width < KeyBlock.IslandWidth * 1.3f) return "the piano did not widen: " + F(keys.Width);
            return null;
        });
        Info(sb, "extend above: key width " + F(w25) + " → " + F(keys != null ? keys.KeyWidthOf(0) : 0f) + ", the new top key slid from x " + F(slideTopX) + " to " + F(restTopX));
        Run(sb, "ExtendKeysLive −1: every key's index +12 (the same objects), the lowest key −12, 49 keys, a little deeper (> 37 keys), the cube keeps its pitches and its saved xs follow (+12)", () =>
        {
            int[] xs0 = kcube.nodes.Select(n => n.gridX).ToArray();
            var objs37 = new List<TileInteraction>(keys.tiles);
            if (!SM.ExtendKeysLive(keys, -1)) return "refused";
            if (keys.KeyCount != 49 || keys.chordRootMIDI != low0 - 12) return "keys " + keys.KeyCount + " lowest " + keys.chordRootMIDI;
            for (int k = 0; k < 37; k++) if (keys.GetTile(k + 12, 0) != objs37[k] || objs37[k].gridX != k + 12) return "key " + k + " not re-indexed in place";
            if (pitches(kcube) != p0) return "cube pitches " + pitches(kcube);
            var cs = kcube.ToState();
            for (int i = 0; i < xs0.Length; i++) if (cs.xs[i] != xs0[i] + 12) return "saved xs " + string.Join(",", cs.xs.Select(x => x.ToString()).ToArray());
            if (keys.Depth <= KeyBlock.DepthOf(1) + 0.2f) return "depth " + F(keys.Depth);
            var lanes = SM.ColumnIslands(keys.column).Where(k => !k.IsPhrase).OrderBy(k => k.FrontEdge).ToList();
            for (int k = 0; k + 1 < lanes.Count; k++) if (lanes[k + 1].FrontEdge - lanes[k].BackEdge < ProjectConfig.LaneGap - 0.01f) return "lanes not clear after the deeper keyboard";
            return null;
        });
        Run(sb, "ShrinkKeysLive: a free octave goes (49 → 37 → 25, live, pitches kept); an octave with a note is refused", () =>
        {
            if (!SM.ShrinkKeysLive(keys, -1) || keys.KeyCount != 37 || keys.chordRootMIDI != low0) return "bottom octave: " + keys.KeyCount + " lowest " + keys.chordRootMIDI;
            if (!SM.ShrinkKeysLive(keys, +1) || keys.KeyCount != 25) return "top octave: " + keys.KeyCount;
            if (pitches(kcube) != p0) return "cube pitches " + pitches(kcube);
            if (SM.ShrinkKeysLive(keys, +1)) return "dropped the octave that holds the note " + (low0 + 20);
            if (keys.KeyCount != 25 || pitches(kcube) != p0) return "after the refusal " + keys.KeyCount;
            return null;
        });
        // a DRAFT held on the keyboard survives an extension below (its key objects re-indexed, its notes kept)
        {
            var pm = PathManager.I;
            string why = null;
            if (OrbitCamera.I != null) { OrbitCamera.I.FrameBounds(keys.WorldBounds, 0.1f, true, 1.05f); }
            yield return Wait(0.35f);
            pm.PickUpCube(1); pm.SetBrush(12);
            var a = keys.GetTile(8, 0); var b = keys.GetTile(14, 0);
            var cam = Camera.main;
            pm.SimPointer(cam.WorldToScreenPoint(a.Top), true, true, false); pm.SimPointer(cam.WorldToScreenPoint(a.Top), false, false, true);
            yield return null;
            pm.SimPointer(cam.WorldToScreenPoint(b.Top), true, true, false); pm.SimPointer(cam.WorldToScreenPoint(b.Top), false, false, true);
            yield return null;
            var d = pm.Draft;
            string before = d != null ? pitches(d) : "none";
            if (d == null || d.nodes.Count < 2) why = "no draft (" + (d != null ? d.nodes.Count + " nodes" : "none") + ")";
            else
            {
                int g0 = d.nodes[0].gridX;
                bool ok = SM.ExtendKeysLive(keys, -1);
                yield return null;
                if (!ok) why = "refused";
                else if (pm.Draft != d) why = "the draft was replaced";
                else if (pitches(d) != before) why = "draft notes " + pitches(d) + " (were " + before + ")";
                else if (d.nodes[0].gridX != g0 + 12) why = "draft key not re-indexed: " + d.nodes[0].gridX;
            }
            Line(sb, why == null, "a DRAFT on the keyboard survives ExtendKeysLive −1 (the same draft, its key objects re-indexed +12, its notes kept)", why ?? ("draft notes " + before));
            pm.PutDown();
            yield return null;
        }
        Run(sb, "SetKeyRange (one History entry): a wider range keeps every note's PITCH exactly; a narrower one folds out-of-range notes by octaves (pitch class kept)", () =>
        {
            int idx = SM.Islands.IndexOf(keys);
            int h = History.UndoCount;
            SM.SetKeyRange(idx, low0 - 12, 61);
            var kb = SM.Islands[idx];
            System.Func<KeyBlock, AudioCube> theCube = isl => SequenceMaster.Cubes.FirstOrDefault(x => x != null && !x.IsOnMoon && x.Island == isl && x.isFinalized && x.nodes.Count == 3);
            var c = theCube(kb);
            if (kb.KeyCount != 61 || kb.chordRootMIDI != low0 - 12 || History.UndoCount != h + 1) return "range " + kb.chordRootMIDI + " + " + kb.KeyCount + ", history +" + (History.UndoCount - h);
            if (pitches(c) != p0) return "wider: " + pitches(c) + " (were " + p0 + ")";
            SM.SetKeyRange(idx, low0, 13);
            kb = SM.Islands[idx];
            c = theCube(kb);
            var want = new[] { low0 + 3, low0 + 10, low0 + 20 - 12 };
            var got = c != null ? c.nodes.Select(n => n.midi).ToArray() : new int[0];
            if (kb.KeyCount != 13 || got.Length != 3 || got[0] != want[0] || got[1] != want[1] || got[2] != want[2]) return "narrower: " + string.Join(",", got.Select(x => x.ToString()).ToArray());
            History.Undo();
            kb = SM.Islands[idx];
            if (kb.KeyCount != 61) return "undo " + kb.KeyCount;
            return null;
        });
        Run(sb, "keyCount and the lowest key round-trip through save / load (an older file's keyboard reads 25)", () =>
        {
            if (!SongIO.SaveTo(ScratchSave)) return "save failed";
            var st = SongState.FromJson(File.ReadAllText(ScratchSave));
            var km = st.measures.FirstOrDefault(m => m.kind == 2);
            if (km == null || km.keyCount != 61) return "saved keyCount " + (km != null ? km.keyCount : -1);
            LoadFixture();
            SongState.Apply(st);
            var kb = SM.Islands.FirstOrDefault(k => k.IsKeyboard);
            if (kb == null || kb.KeyCount != 61 || kb.chordRootMIDI != low0 - 12) return "loaded " + (kb != null ? kb.KeyCount + " from " + kb.chordRootMIDI : "none");
            int ki = System.Array.IndexOf(st.measures, km);
            km.keyCount = 0; st.cubes = st.cubes.Where(c => c.moon >= 0 || c.measure != ki).ToArray();   // (an older file's keyboard: no cube past its 25 keys)
            SongState.Apply(st);
            kb = SM.Islands.FirstOrDefault(k => k.IsKeyboard);
            return kb != null && kb.KeyCount == 25 ? null : "0 reads " + (kb != null ? kb.KeyCount : -1);
        });
        {
            var kb = SM.Islands.FirstOrDefault(k => k.IsKeyboard);
            if (kb != null)
            {
                int idx = SM.Islands.IndexOf(kb);
                SM.SetKeyRange(idx, low0 - 12, 61);
                yield return Wait(0.5f);
                kb = SM.Islands[idx];
                if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(kb.WorldBounds, 0.1f, true, 1.15f);
                yield return Wait(0.4f);
                yield return Shot("k7_keys61.png");
                Info(sb, "capture k7_keys61.png: a 61-key keyboard (five octaves from " + kb.chordRootMIDI + ") on its one-measure platform, depth " + F(kb.Depth));
            }
        }

        // ============================================================ SAVE / LOAD (§3.7)
        LoadFixture();
        {
            var st = SongState.Capture();
            float pz = st.measures[SM.ColumnFirst(1)].pz - 4.5f;
            Add(st, StairState(1, 1, 6, 1, pz));
            Add(st, PhraseState(2, 8, 48, 1));
            st.sections = new[] { 0, 2, 4 };
            SM.RebuildFromState(st); History.Push();
        }
        Run(sb, "SAVE / LOAD round trip of every v7 field (stairs, carry style, rewind, vary, launch, phrase, section name, sections, cube layer / bend / echoOf)", () =>
        {
            var s = Kind(3); var p = Kind(4);
            if (s == null || p == null) return "stairs " + (s != null) + " phrase " + (p != null);
            SM.SetCarry(0, 1, 1);
            SM.SetRepeat(3, 2); SM.SetRewind(3, true); SM.SetVary(3, 2); SM.SetLaunch(4, true);
            var st = SongState.Capture();
            st.measures[0].secRole = 3;
            var c0 = st.cubes.First(c => c.moon < 0 && c.measure == 0);
            c0.layer = 1; c0.bend = new int[c0.xs.Length]; c0.bend[0] = 2; c0.echoOf = 77;
            SM.RebuildFromState(st);
            if (!SongIO.SaveTo(ScratchSave)) return "save failed";
            var before = SongState.Capture();
            LoadFixture();
            SongState.Apply(SongState.FromJson(File.ReadAllText(ScratchSave)));
            var after = SongState.Capture();
            if (after.measures.Length != before.measures.Length) return "measures " + after.measures.Length + " vs " + before.measures.Length;
            for (int i = 0; i < before.measures.Length; i++)
            {
                var a = before.measures[i]; var b = after.measures[i];
                string m = a.kind + "|" + a.stairType + "|" + a.stairDir + "|" + a.stairSteps + "|" + a.stairRate + "|" + a.stairLead + "|" + a.carry + "|" + a.carryStyle + "|" + a.rewind + "|" + a.vary + "|" + a.launch + "|" + a.phraseOffset + "|" + a.phraseBeats + "|" + a.phraseGrid + "|" + a.col + "|" + a.secRole + "|" + a.repeat;
                string n = b.kind + "|" + b.stairType + "|" + b.stairDir + "|" + b.stairSteps + "|" + b.stairRate + "|" + b.stairLead + "|" + b.carry + "|" + b.carryStyle + "|" + b.rewind + "|" + b.vary + "|" + b.launch + "|" + b.phraseOffset + "|" + b.phraseBeats + "|" + b.phraseGrid + "|" + b.col + "|" + b.secRole + "|" + b.repeat;
                if (m != n) return "measure " + i + ": " + m + " vs " + n;
            }
            if (L(after.sections) != L(before.sections) || L(before.sections) != "[0,2,4]") return "sections " + L(before.sections) + " vs " + L(after.sections);
            var e0 = after.cubes.FirstOrDefault(c => c.echoOf == 77);
            if (e0 == null || e0.layer != 1 || e0.bend == null || e0.bend[0] != 2) return "cube v7 fields";
            if (SM.SectionRole(0) != 3) return "section role " + SM.SectionRole(0);
            if (Kind(3) == null || Kind(4) == null || Kind(4).phraseOffset != 48) return "kinds after load";
            return null;
        });
        LoadFixture();
        Run(sb, "the fixture loads as in v6: ground 0 everywhere, automatic sections, kinds 0, no phrase / stairs", () =>
        {
            for (int c = 0; c < SM.ColumnCount; c++) if (!Near(SM.GroundOf(c), 0f)) return "ground " + c;
            foreach (var kb in SM.Islands) if (kb.kind != 0 || !Near(kb.GroundY, 0f)) return "island " + kb.measureIndex;
            return SM.SectionCount == 2 ? null : "sections " + SM.SectionCount;
        });

        // ============================================================ done
        Restore();
        LoadFixture();
        try { if (File.Exists(ScratchSave)) File.Delete(ScratchSave); } catch (Exception) { }
        Info(sb, "done: " + num + " checks, " + fails + " failed");
        Done = true;
        Report = sb.ToString();
        Flush(sb);
    }

    /// <summary>K's older suites V6ChecksK, V5ChecksK, V4ChecksK through V6Suites (pre-v6 with AutoHand) → Captures/v6_all_report.txt (V3ChecksB runs
    /// inside the v3 bundle there: <see cref="RunV3B"/> runs it alone).</summary>
    public static string RunOlder() { return V6Suites.RunAll(false, "V6ChecksK,V5ChecksK,V4ChecksK"); }

    /// <summary>V3ChecksB (K's v3 suite; pre-v6: PathManager.AutoHand on) on its own → Captures/k7_v3b_report.txt (its PASS / FAIL lines + DONE).</summary>
    public static string RunV3B()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        SequenceMaster.I.StartCoroutine(V3BRoutine());
        return "started";
    }

    static IEnumerator V3BRoutine()
    {
        string path = Path.Combine(V2Checks.CapturePath, "k7_v3b_report.txt");
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(path, "...running\n"); } catch (Exception) { }
        SongIO.QuitAutosave = false;
        PathManager.AutoHand = true;
        V3ChecksB.Prepare();
        V3ChecksB.RunAll();
        float t0 = Time.realtimeSinceStartup;
        while (!V3ChecksB.Done && Time.realtimeSinceStartup - t0 < 300f) yield return null;
        PathManager.AutoHand = false;
        Restore();
        LoadFixture();
        try { File.WriteAllText(path, V3ChecksB.Report + (V3ChecksB.Done ? "\nDONE\n" : "\nTIMEOUT\n")); } catch (Exception) { }
    }
}
