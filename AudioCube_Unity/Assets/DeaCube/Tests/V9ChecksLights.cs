using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// v9 (L) Play-mode verification of THE STAGE LIGHTS (StageLights.cs, DeaStage.hlsl, the lead mark): <c>V9ChecksLights.RunAll()</c> starts a
/// coroutine; poll <c>Done</c> or Captures/lights9_report.txt (numbered PASS / FAIL lines, SUMMARY). Part A, a compact song (a lead chord grid on a
/// belt, a plain grid, a keyboard, stairs, a phrase, a Moon): the lead mark round-trips through save / load (JSON), a copy, SetLead's one History
/// entry and its undo, the selection bar's lead; IsLead for every kind (a Moon never); the stage dims only while playing and eases both ways; the
/// beam follows the belt's ride. Part B, the gallery song get-proto: its main-melody lane is lead; the spotlight's measures are exactly the measures
/// a lead grid sounds (re-derived here from the cubes' windows by HitSteps16), sampled live (a fragment, a belt pass, the rewind section, a pop-in
/// keyboard, a bar with no lead: no beam); a pop-in keyboard's beam follows it; the switch off = no light objects, neutral globals, cube glow 1,
/// the key light and ambient untouched; nothing left after stop / replay / a song load; zero allocations per frame in the steady state; the present
/// mode shows it; frame time with the lights on vs off; 0 console errors. Captures lights9_*.png. Never writes the user's save.
/// </summary>
public static class V9ChecksLights
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static StringBuilder sb;
    static bool shots;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "lights9_report.txt");
    static SongManager SM => SongManager.I;
    static string F(double v) => v.ToString("F2");

    static void Line(bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" L9-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush();
    }
    static void Info(string t) { sb.Append("INFO ").Append(t).Append('\n'); Flush(); }
    static void Flush() { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void Check(string name, Func<string> c)
    {
        try { string d = c(); Line(d == null || d.StartsWith("ok"), name, d ?? "ok"); }
        catch (Exception e) { Line(false, name, e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); }
    }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Shot(string file)
    {
        if (!shots) yield break;
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); string p = Path.Combine(V2Checks.CapturePath, file); if (File.Exists(p)) File.Delete(p); ScreenCapture.CaptureScreenshot(p, 1); } catch (Exception) { }
        yield return null; yield return null;
        Info("capture " + file + " at song beat " + F(GlobalClock.SongBeatD) + " (dim " + F(StageLights.Dim) + ", beams " + StageLights.ActiveBeams + ")");
    }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear(); shots = captures;
        sb = new StringBuilder();
        SequenceMaster.I.StartCoroutine(Guarded());
        return "started";
    }

    static IEnumerator Guarded()
    {
        sb.Append("V9ChecksLights ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        bool on0 = StageLights.On;
        var body = Routine();
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { StageLights.SetOn(on0); if (Presenter.Active) Presenter.Exit(); GlobalClock.ClearRegion(); GlobalClock.Stop(); IslandHeader.Hide(); GridSelection.Clear(); if (OrbitCamera.I != null) OrbitCamera.I.followPlayhead = false; } catch (Exception) { }
        try { if (MainMenu.IsShown) MainMenu.Hide(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog;
        Line(errors.Count == 0, "no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush();
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) PathManager.I.PutDown();
        if (Presenter.Active) Presenter.Exit();
        Application.runInBackground = true;
        StageLights.SetOn(true);
    }

    static IEnumerator Routine()
    {
        Prepare();
        yield return null;
        if (StageLights.I == null) { Line(false, "StageLights exists", "no instance"); yield break; }
        yield return PartA();
        yield return PartB();
    }

    // ================================================================== part A: the compact song
    static MeasureState Chord(int col, string name, int root, int[] semis, float pz = 0f, int repeat = 1, bool lead = false) => new MeasureState { chordKey = name, root = root, semis = semis, bars = 1, kind = 0, col = col, repeat = repeat, energy = 2, pz = pz, placed = true, lead = lead };
    static MeasureState Keys(int col, float pz) => new MeasureState { chordKey = "Keys", root = 48, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, pz = pz, placed = true, keyCount = 25 };
    static MeasureState Stairs(int col, float pz) => new MeasureState { chordKey = "Stairs", root = 60, semis = new[] { 0 }, bars = 1, kind = 3, placed = true, col = col, energy = 2, repeat = 1, stairDir = -1, stairSteps = 4, stairRate = 12, stairLead = true, stairType = 0, pz = pz };
    static MeasureState Phrase(int col, float pz) => new MeasureState { chordKey = "Phrase", root = 60, semis = new[] { 0 }, bars = 1, kind = 4, placed = true, col = col, energy = 2, repeat = 1, phraseOffset = 0, phraseBeats = 4, phraseGrid = 12, pz = pz };
    static CubeState Cube(int id, int inst, int measure, int[] xs, int[] zs, int ticks)
    {
        int n = xs.Length;
        var durs = new int[n]; for (int i = 0; i < n; i++) durs[i] = ticks;
        return new CubeState { id = id, instrument = inst, measure = measure, xs = (int[])xs.Clone(), zs = (int[])zs.Clone(), rests = new bool[n], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f, hits = -1, seed = id, moon = -1, twinOf = -1, echoOf = -1 };
    }

    /// <summary>C (lead, ×2 on a belt) + Am | F + keys | G + stairs | C + a phrase; a Moon; 120 bpm, no loop.</summary>
    static SongState TestSong()
    {
        var m = new List<MeasureState>
        {
            Chord(0, "C", 60, new[] { 0, 4, 7 }, 0f, 2, true), Chord(0, "Am", 57, new[] { 0, 3, 7 }, 10f),
            Chord(1, "F", 65, new[] { 0, 4, 7 }), Keys(1, 5f),
            Chord(2, "G", 67, new[] { 0, 4, 7 }), Stairs(2, 5f),
            Chord(3, "C", 60, new[] { 0, 4, 7 }), Phrase(3, 5f),
        };
        var cubes = new List<CubeState>
        {
            Cube(9301, 0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 1 }, 24),
            Cube(9302, 4, 1, new[] { 0, 2, 4, 2 }, new[] { 1, 0, 1, 2 }, 24),
            Cube(9303, 4, 2, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 0, 1 }, 24),
            Cube(9304, 8, 3, new[] { 12, 14, 16, 19 }, new[] { 0, 0, 0, 0 }, 24),
            Cube(9305, 0, 6, new[] { 5, 4, 3, 2 }, new[] { 0, 0, 1, 1 }, 24),
        };
        cubes.Add(new CubeState { id = 9306, instrument = 9, measure = 0, moon = 0, xs = new[] { 3 }, zs = new[] { 0 }, rests = new bool[1], step = 1, gate = 1, volume = 1f, hits = 4, twinOf = -1, echoOf = -1 });
        var moons = new[] { new MeasureState { chordKey = "Moon", root = 36, semis = new[] { 0 }, bars = 1, kind = 1, placed = true, energy = 2, repeat = 1, col = 0, px = 0f, pz = -12f, lead = true } };
        return new SongState
        {
            version = SongState.CurrentVersion, name = "l9 lights", bpm = 120f, beatsPerBar = 4, loop = false, measures = m.ToArray(), cubes = cubes.ToArray(),
            moons = moons, keyTonic = 0, keyMinor = false, sections = new[] { 0 }, tone = 1f, space = 1f, demoSeed = 9
        };
    }

    static IEnumerator Load(SongState st)
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        if (PathManager.I != null) PathManager.I.PutDown();
        SongState.Apply(st);
        History.Reset(); History.Push();
        yield return Frames(4);
        SectionPlinth.RefreshNow();
        KeyHands.RefreshNow();
        yield return Wait(0.6f);
    }

    static KeyBlock Find(Func<KeyBlock, bool> f) { foreach (var kb in SM.Islands) if (kb != null && f(kb)) return kb; return null; }

    static IEnumerator PartA()
    {
        Info("part A: the compact song");
        yield return Load(TestSong());
        var lead = Find(k => k.kind == 0 && k.assignedChord == "C" && k.column == 0);
        var plain = Find(k => k.kind == 0 && k.column == 0 && k != lead);
        Check("the song loads with its lead mark (KeyBlock.lead from MeasureState.lead)", () => lead != null && lead.lead && plain != null && !plain.lead ? "ok" : "lead " + (lead != null ? lead.lead.ToString() : "missing") + ", plain " + (plain != null ? plain.lead.ToString() : "missing"));
        // save / load: the JSON round trip
        Check("lead round-trips through save / load (SongState JSON)", () =>
        {
            var st = SongState.Capture();
            var j = st.ToJson();
            var back = JsonUtility.FromJson<SongState>(j);
            int iLead = SM.Islands.IndexOf(lead);
            bool a = st.measures[iLead].lead && j.Contains("\"lead\":true") && back.measures[iLead].lead && !back.measures[SM.Islands.IndexOf(plain)].lead;
            var md = back.measures[iLead].ToData(iLead);
            var ms2 = MeasureState.From(md);
            bool b = md.lead && ms2.lead && MeasureState.Clone(ms2).lead && SongManager.CopyMeasure(md).lead && lead.ToData().lead;
            return a && b ? "ok: Capture → JSON → FromJson → ToData → From → Clone → CopyMeasure keep it" : "json " + a + ", data " + b;
        });
        // apply the JSON again (a load): the island comes back lead
        {
            var j = SongState.Capture().ToJson();
            yield return Load(JsonUtility.FromJson<SongState>(j));
            lead = Find(k => k.kind == 0 && k.column == 0 && k.lead);
            Check("a reload of that JSON rebuilds the grid lead", () => lead != null && lead.assignedChord == "C" ? "ok" : "no lead grid in column 0 after the reload");
        }
        // copy
        {
            int i = SM.Islands.IndexOf(lead);
            int n0 = SM.Islands.Count;
            int at = SM.DuplicateIsland(i, 0);
            yield return Frames(3);
            var copy = at >= 0 && at < SM.Islands.Count ? SM.Islands[at] : null;
            Check("a copy of a lead grid is lead (DuplicateIsland)", () => copy != null && copy.lead && SM.Islands.Count == n0 + 1 ? "ok: island " + at : "copy " + (copy != null ? copy.lead.ToString() : "none") + ", islands " + n0 + " → " + SM.Islands.Count);
            History.Undo();
            yield return Frames(4);
            lead = Find(k => k.kind == 0 && k.column == 0 && k.lead);
        }
        // SetLead: one History entry, the event, undo
        {
            int i = SM.Islands.IndexOf(lead);
            int u0 = History.UndoCount, ev = 0;
            Action<KeyBlock> h = kb => ev++;
            SM.OnLeadChanged += h;
            SM.SetLead(i, false);
            int u1 = History.UndoCount; bool off = !SM.Islands[i].lead;
            SM.SetLead(i, false);   // no change: no entry
            int u2 = History.UndoCount;
            SM.OnLeadChanged -= h;
            Check("SetLead is one History entry and one OnLeadChanged (a no-op adds none)", () => off && u1 == u0 + 1 && u2 == u1 && ev == 1 ? "ok: " + u0 + " → " + u1 : "off " + off + ", undo " + u0 + " → " + u1 + " → " + u2 + ", events " + ev);
            History.Undo();
            yield return Frames(4);
            var back = Find(k => k.kind == 0 && k.column == 0 && k.assignedChord == "C");
            Check("undo brings the lead mark back", () => back != null && back.lead ? "ok" : "not lead after undo");
            lead = back;
        }
        // IsLead per kind
        Check("IsLead: a chord grid only when marked; keyboards, stairs, phrases always; a Moon never (even marked)", () =>
        {
            var s = new StringBuilder(); bool ok = true;
            foreach (var kb in SM.Islands)
            {
                if (kb == null) continue;
                bool want = kb.kind == 0 ? kb.lead : (kb.kind == 2 || kb.kind == 3 || kb.kind == 4);
                bool got = SongManager.IsLead(kb);
                if (got != want) { ok = false; s.Append(" kind ").Append(kb.kind).Append(" got ").Append(got); }
            }
            int kinds = SM.Islands.Where(k => k != null).Select(k => k.kind).Distinct().Count();
            foreach (var mo in SM.Moons) if (mo != null && SongManager.IsLead(mo)) { ok = false; s.Append(" a Moon is lead"); }
            bool moonMarked = SM.Moons.Count > 0 && SM.Moons[0] != null && SM.Moons[0].lead;
            return ok && kinds >= 4 && SM.Moons.Count > 0 ? "ok: " + kinds + " island kinds + " + SM.Moons.Count + " Moon (marked lead in the file: " + moonMarked + ")" : "mismatch" + s + " (kinds " + kinds + ", moons " + SM.Moons.Count + ")";
        });
        // the selection bar's lead
        {
            var grids = SM.Islands.Where(k => k != null && k.kind == 0 && k.column >= 1).Take(2).ToList();
            GridSelection.Set(grids);
            yield return Frames(2);
            int u0 = History.UndoCount;
            bool did = HudSelection.Do(HudSelection.Act.Lead);
            bool all = grids.All(k => k.lead) && HudSelection.LeadOf();
            int u1 = History.UndoCount;
            HudSelection.Do(HudSelection.Act.Lead);
            bool none = grids.All(k => !k.lead);
            int u2 = History.UndoCount;
            GridSelection.Clear();
            Check("the selection bar's lead marks every selected chord grid (one History entry) and clears them again", () => did && all && none && u1 == u0 + 1 && u2 == u1 + 1 ? "ok: " + grids.Count + " grids" : "did " + did + ", all " + all + ", none " + none + ", undo " + u0 + "/" + u1 + "/" + u2);
            yield return Frames(2);
        }
        // the stage: dims only while playing, eases both ways
        {
            GlobalClock.Stop(); GlobalClock.Seek(0);
            yield return Wait(0.7f);
            float d0 = StageLights.Dim, s0 = StageLights.ShaderDim;
            GlobalClock.Play(); AudioCube.ScheduleAllNow();
            yield return Wait(0.12f);
            float dMid = StageLights.Dim;
            yield return Wait(0.6f);
            float dUp = StageLights.Dim, sUp = StageLights.ShaderDim;
            GlobalClock.Pause();
            yield return Wait(0.12f);
            float dPauseMid = StageLights.Dim;
            yield return Wait(0.6f);
            float dPaused = StageLights.Dim;
            GlobalClock.Play();
            yield return Wait(0.7f);
            float dAgain = StageLights.Dim;
            GlobalClock.Stop();
            yield return Wait(0.12f);
            float dStopMid = StageLights.Dim;
            yield return Wait(0.6f);
            float dStopped = StageLights.Dim, sStopped = StageLights.ShaderDim;
            Check("the stage dims only while the song plays and eases in / out (~0.4 s, never a pop)", () =>
                d0 == 0f && s0 == 0f && dMid > 0.02f && dMid < 0.98f && dUp >= 0.999f && Mathf.Abs(sUp - dUp) < 1e-3f && dPauseMid > 0.02f && dPauseMid < 0.98f && dPaused == 0f
                && dAgain >= 0.999f && dStopMid > 0.02f && dStopMid < 0.98f && dStopped == 0f && sStopped == 0f
                ? "ok: stopped " + F(d0) + ", 0.12 s in " + F(dMid) + ", playing " + F(dUp) + ", pause 0.12 s " + F(dPauseMid) + ", paused " + F(dPaused) + ", stop 0.12 s " + F(dStopMid) + ", stopped " + F(dStopped)
                : "stopped " + d0 + "/" + s0 + ", in " + dMid + ", up " + dUp + "/" + sUp + ", pause " + dPauseMid + " → " + dPaused + ", again " + dAgain + ", stop " + dStopMid + " → " + dStopped + "/" + sStopped);
        }
        // the schedule of the compact song: the lead grid's two passes = one part [0, 8); the keyboard's bar, the stairs' bar; the plain grid none
        Check("the compact song's parts: the lead grid's belt passes [0, 8), the keyboard [4·…], the plain grids none", () =>
        {
            StageLights.RefreshNow();
            var pl = StageLights.PartsOf(lead);
            var pp = StageLights.PartsOf(plain);
            var keys = Find(k => k.kind == 2);
            var pk = keys != null ? StageLights.PartsOf(keys) : new List<Vector2>();
            bool ok = pl.Count == 1 && Mathf.Abs(pl[0].x) < 1e-3f && Mathf.Abs(pl[0].y - 8f) < 1e-3f && pp.Count == 0 && pk.Count >= 1;
            return (ok ? "ok: " : "") + "lead " + string.Join(" ", pl.Select(v => "[" + v.x + "," + v.y + ")").ToArray()) + "; plain " + pp.Count + "; keys " + string.Join(" ", pk.Select(v => "[" + v.x + "," + v.y + ")").ToArray());
        });
        // the beam follows the belt: seek through the pass boundary while playing, compare the beam with the grid every frame (after LateUpdate)
        {
            GlobalClock.Seek(2.0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
            yield return Wait(0.6f);
            float worst = 0f, x0 = float.NaN, x1 = float.NaN; int frames = 0, lit = 0;
            for (int f = 0; f <= 40; f++)
            {
                double b = 3.0 + f * 0.05;
                GlobalClock.Seek(b);
                yield return new WaitForEndOfFrame();
                int slot = -1; for (int k = 0; k < StageLights.MaxSpots; k++) if (StageLights.SpotIsland(k) == lead) slot = k;
                if (slot < 0) continue;
                lit++;
                var beam = StageLights.Beam(slot); var c = lead.VisualCenter;
                float d = new Vector2(beam.position.x - c.x, beam.position.z - c.z).magnitude;
                worst = Mathf.Max(worst, d); frames++;
                if (f == 0) x0 = c.x; x1 = c.x;
            }
            GlobalClock.Stop();
            Check("the beam follows the grid on its belt (every frame through the pass boundary)", () => frames >= 35 && worst < 0.02f && !float.IsNaN(x0) && x1 - x0 > 1f ? "ok: " + frames + " frames, worst " + worst.ToString("F3") + " u, the grid rode " + F(x1 - x0) + " u" : frames + " lit frames of 41, worst " + worst.ToString("F3") + ", x " + F(x0) + " → " + F(x1));
            yield return Wait(0.6f);
        }
    }

    // ================================================================== part B: get-proto
    const string Proto = "get-proto";
    static IEnumerator PartB()
    {
        Info("part B: get-proto");
        Gallery.ClearTestSongs(); Gallery.Reload();
        bool opened = Gallery.Open(Proto, false);
        yield return Frames(4);
        SectionPlinth.RefreshNow(); KeyHands.RefreshNow();
        yield return Wait(1.0f);
        if (!opened || Gallery.CurrentId != Proto) { Line(false, "get-proto opens", "Gallery.Open returned " + opened); yield break; }
        var cam = OrbitCamera.I;
        // its main-melody lane is lead
        var leadGrids = SM.Islands.Where(k => k != null && k.kind == 0 && k.lead).ToList();
        Check("get-proto's main-melody lane (pz 0, the sax / lead voices' chord grids) is marked lead, nothing else", () =>
        {
            int other = leadGrids.Count(k => Mathf.Abs(k.pz) > 0.5f);
            int lane = SM.Islands.Count(k => k != null && k.kind == 0 && Mathf.Abs(k.pz) < 0.5f);
            return leadGrids.Count >= 20 && other == 0 && lane == leadGrids.Count ? "ok: " + leadGrids.Count + " chord grids" : leadGrids.Count + " lead, " + other + " outside the lane, lane has " + lane;
        });
        StageLights.RefreshNow();
        // the spotlight's measures = the measures a lead grid sounds, re-derived here from HitSteps16 (an independent path through Decide)
        var expect = new Dictionary<KeyBlock, HashSet<int>>();
        int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.IsOnMoon || c.Hologram || c.nodes.Count == 0 || !SongManager.IsLead(c.Island)) continue;
            for (int w = 0; w < c.windows.Count; w++)
            {
                var win = c.windows[w];
                if (win.silent) continue;
                var target = win.island != null ? win.island : c.Island;
                foreach (int s16 in c.HitSteps16(w, win.order))
                {
                    int bar = Mathf.FloorToInt((win.start + s16 * 0.25f + 1e-3f) / bpb);
                    HashSet<int> set; if (!expect.TryGetValue(target, out set)) { set = new HashSet<int>(); expect[target] = set; }
                    set.Add(bar);
                }
            }
        }
        Check("the spotlight's parts are exactly the measures a lead grid sounds (every lead grid, every bar)", () =>
        {
            int grids = 0, bars = 0, bad = 0; var s = new StringBuilder();
            var all = new HashSet<KeyBlock>(expect.Keys);
            foreach (var kb in SM.Islands) if (kb != null && StageLights.PartsOf(kb).Count > 0) all.Add(kb);
            foreach (var kb in all)
            {
                var got = new HashSet<int>();
                foreach (var p in StageLights.PartsOf(kb)) for (int b = Mathf.RoundToInt(p.x / bpb); b < Mathf.RoundToInt(p.y / bpb); b++) got.Add(b);
                HashSet<int> want; if (!expect.TryGetValue(kb, out want)) want = new HashSet<int>();
                grids++; bars += want.Count;
                if (!got.SetEquals(want)) { bad++; if (s.Length < 300) s.Append(" col ").Append(kb.column).Append(" kind ").Append(kb.kind).Append(": want ").Append(want.Count).Append(" got ").Append(got.Count); }
            }
            return bad == 0 && grids > 20 ? "ok: " + grids + " grids, " + bars + " bars" : bad + " of " + grids + " differ:" + s;
        });
        // a pop-in keyboard (KeyStage drives it): its spotlight parts cover KeyStage's note parts
        KeyBlock popKb = SM.Islands.FirstOrDefault(k => k != null && k.IsKeyboard && KeyStage.Drives(k) && KeyStage.PartsOf(k).Count > 0 && StageLights.PartsOf(k).Count > 0);
        Check("a pop-in keyboard's spotlight covers each of its KeyStage parts (first note to last note's end)", () =>
        {
            if (popKb == null) return "no driven keyboard with parts";
            var ks = KeyStage.PartsOf(popKb); var sp = StageLights.PartsOf(popKb);
            int covered = 0;
            foreach (var p in ks) if (sp.Any(q => q.x <= p.x + 1e-3f && p.x < q.y)) covered++;   // a spotlight part holds each KeyStage part's first note
            return covered == ks.Count ? "ok: column " + popKb.column + ", " + ks.Count + " KeyStage parts, " + sp.Count + " spotlight parts" : covered + " of " + ks.Count + " covered";
        });

        // ---- live samples: the beat's slots against the pure schedule
        if (cam != null) cam.followPlayhead = true;
        GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(0.7f);
        var fragment = SM.Islands.FirstOrDefault(k => k != null && k.lead && k.column == 1);
        var rewindLead = SM.Islands.FirstOrDefault(k => k != null && k.lead && k.rewind);
        var beltLead = SM.Islands.FirstOrDefault(k => k != null && SongManager.IsLead(k) && k.HasBelt && k.Passes >= 2 && StageLights.PartsOf(k).Count >= 2);
        double noLead = double.NaN;
        for (int b = 0; b * bpb < SM.TotalBeats - bpb; b++)
        {
            bool any = false;
            for (double t = b * bpb - 1.2; t < (b + 1) * bpb + 1.2 && !any; t += 0.25) any = StageLights.AnyLeadAt(t);
            if (!any) { noLead = b * bpb + 2.0; break; }
        }
        var samples = new List<KeyValuePair<string, double>>();
        if (fragment != null && StageLights.PartsOf(fragment).Count > 0) samples.Add(new KeyValuePair<string, double>("the fragment (col 1)", StageLights.PartsOf(fragment)[0].x + 1.5));
        if (beltLead != null) { var ps = StageLights.PartsOf(beltLead); samples.Add(new KeyValuePair<string, double>("a belt's second pass (col " + beltLead.column + ")", ps[ps.Count - 1].x + 1.5)); }
        if (rewindLead != null && StageLights.PartsOf(rewindLead).Count > 0) { var ps = StageLights.PartsOf(rewindLead); samples.Add(new KeyValuePair<string, double>("the rewind section (col " + rewindLead.column + ")", ps[ps.Count - 1].x + 1.5)); }
        if (popKb != null) samples.Add(new KeyValuePair<string, double>("a pop-in keyboard (col " + popKb.column + ")", StageLights.PartsOf(popKb)[0].x + 1.5));
        if (!double.IsNaN(noLead)) samples.Add(new KeyValuePair<string, double>("a bar with no lead", noLead));
        else Info("no bar of get-proto is free of lead light (every bar has a lead part within a beat)");
        foreach (var kv in samples)
        {
            GlobalClock.Seek(kv.Value);
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            double beat = GlobalClock.SongBeatD;
            yield return new WaitForEndOfFrame();
            string label = kv.Key;
            double at = kv.Value;
            Check("live at " + label + ": the lit grids are the schedule's (beat " + F(at) + ")", () =>
            {
                var lit = new List<KeyBlock>();
                for (int k = 0; k < StageLights.MaxSpots; k++) { var s = StageLights.SpotIsland(k); if (s != null) lit.Add(s); }
                int want = 0; foreach (var kb in SM.Islands) if (kb != null && StageLights.ScheduleAt(kb, GlobalClock.SongBeatD) > 0f) want++;
                bool allScheduled = lit.All(kb => StageLights.ScheduleAt(kb, GlobalClock.SongBeatD) > 0f);
                bool count = lit.Count == Mathf.Min(want, StageLights.MaxSpots);
                bool beams = StageLights.ActiveBeams == lit.Count;
                return allScheduled && count && beams ? "ok: " + lit.Count + " lit (" + string.Join(", ", lit.Select(k => "col " + k.column + " kind " + k.kind + (k.lead ? " lead" : "")).ToArray()) + "), " + want + " scheduled"
                    : "lit " + lit.Count + " (all scheduled " + allScheduled + "), scheduled " + want + ", beams " + StageLights.ActiveBeams;
            });
        }
        if (!double.IsNaN(noLead))
        {
            GlobalClock.Seek(noLead);
            yield return Frames(3);
            Check("a bar with no lead grid: no beam, the even dim stage", () => StageLights.ActiveBeams == 0 && StageLights.Dim >= 0.999f ? "ok: beat " + F(noLead) : "beams " + StageLights.ActiveBeams + ", dim " + F(StageLights.Dim));
        }
        // the pop-in keyboard's beam follows it as it rises (every frame of the pop: x / z on the grid, y on the grid or on the water above it)
        if (popKb != null)
        {
            double s0 = StageLights.PartsOf(popKb)[0].x;
            float worst = 0f, y0 = float.NaN, y1 = float.NaN; int frames = 0;
            for (int f = 0; f <= 30; f++)
            {
                GlobalClock.Seek(s0 - 1.0 + f * 0.05);
                yield return new WaitForEndOfFrame();
                int slot = -1; for (int k = 0; k < StageLights.MaxSpots; k++) if (StageLights.SpotIsland(k) == popKb) slot = k;
                if (slot < 0) continue;
                var beam = StageLights.Beam(slot); var c = popKb.VisualCenter;
                float wantY = Mathf.Max(c.y, KeyBlock.SeaSurfaceY + 0.05f);
                worst = Mathf.Max(worst, (beam.position - new Vector3(c.x, wantY, c.z)).magnitude); frames++;
                if (float.IsNaN(y0)) y0 = c.y; y1 = c.y;
            }
            Check("the pop-in keyboard's beam follows it out of the sea", () => frames >= 15 && worst < 0.02f ? "ok: " + frames + " frames, worst " + worst.ToString("F3") + " u, the keyboard rose " + F(y0) + " → " + F(y1) : frames + " lit frames, worst " + worst.ToString("F3"));
        }
        // the cubes of a lit grid glow at full strength; the others ×1
        {
            KeyBlock litKb = null; for (int k = 0; k < StageLights.MaxSpots && litKb == null; k++) if (StageLights.SpotIsland(k) != null && StageLights.SpotAmount(k) > 0.9f) litKb = StageLights.SpotIsland(k);
            var litCube = litKb != null ? SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.Island == litKb) : null;
            var darkCube = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.Island != null && StageLights.LitOf(c.Island) <= 0f && !c.IsOnMoon);
            Check("a lit grid's cubes glow at full strength (CubeGlow > 1); the rest ×1", () => litCube != null && darkCube != null && StageLights.CubeGlow(litCube) > 1.3f && StageLights.CubeGlow(darkCube) == 1f
                ? "ok: lit " + F(StageLights.CubeGlow(litCube)) + ", other " + F(StageLights.CubeGlow(darkCube)) : "lit cube " + (litCube != null) + " " + (litCube != null ? F(StageLights.CubeGlow(litCube)) : "") + ", dark " + (darkCube != null ? F(StageLights.CubeGlow(darkCube)) : "none"));
        }
        // zero allocations per frame in the steady state (this component's LateUpdate), 5 s of play
        {
            GlobalClock.Seek(SM.ColumnStart(Mathf.Min(4, SM.ColumnCount - 1)));
            yield return Wait(0.3f);
            StageLights.ResetGcStats();
            yield return Wait(5f);
            Check("zero allocations per frame in the steady state (5 s of get-proto playing)", () => StageLights.GcFramesMeasured > 60 && StageLights.GcFramesAllocating == 0 ? "ok: " + StageLights.GcFramesMeasured + " frames, " + StageLights.Rebuilds + " schedule builds so far" : StageLights.GcFramesAllocating + " of " + StageLights.GcFramesMeasured + " frames allocated (max " + StageLights.GcMaxBytes + " B)");
        }
        // frame time: lights on vs off, 6 s each from C (column 4)
        {
            var res = new float[2];
            for (int pass = 0; pass < 2; pass++)
            {
                StageLights.SetOn(pass == 0);
                GlobalClock.Seek(SM.ColumnStart(Mathf.Min(4, SM.ColumnCount - 1)));
                yield return Wait(0.8f);
                var frames = new List<float>(); float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 6f) { yield return null; frames.Add(Time.unscaledDeltaTime); }
                res[pass] = frames.Average() * 1000f;
                frames.Sort();
                Info("frame time, lights " + (pass == 0 ? "on" : "off") + ": mean " + res[pass].ToString("F1") + " ms (" + (1000f / res[pass]).ToString("F0") + " fps), p95 " + (frames[(int)(frames.Count * 0.95f)] * 1000f).ToString("F1") + " ms, " + frames.Count + " frames");
            }
            StageLights.SetOn(true);
            Check("frame time on get-proto with the lights on stays within 10 % (+1 ms) of lights off", () => res[0] <= res[1] * 1.1f + 1f ? "ok: on " + res[0].ToString("F1") + " ms, off " + res[1].ToString("F1") + " ms" : "on " + res[0].ToString("F1") + " ms vs off " + res[1].ToString("F1") + " ms");
        }
        // the switch off = today's look: no light objects, neutral globals, cube glow 1, the key light and ambient untouched
        {
            var keyLight = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            float li0 = keyLight != null ? keyLight.intensity : -1f; Color lc0 = keyLight != null ? keyLight.color : Color.clear; Color amb0 = RenderSettings.ambientLight;
            StageLights.SetOn(false);
            yield return Frames(3);
            var pos = Shader.GetGlobalVectorArray("_DeaSpotPos");
            bool zero = pos == null || pos.All(v => v == Vector4.zero);
            var anyCube = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.Island != null);
            Check("lights off while playing: no beam objects active, dim 0, the spot globals zero, cube glow 1, key light and ambient as before", () =>
                StageLights.ActiveBeams == 0 && StageLights.ShaderDim == 0f && StageLights.Dim == 0f && zero && (anyCube == null || StageLights.CubeGlow(anyCube) == 1f)
                && (keyLight == null || (keyLight.intensity == li0 && keyLight.color == lc0)) && RenderSettings.ambientLight == amb0
                ? "ok: key light " + F(li0) + ", ambient " + amb0 : "beams " + StageLights.ActiveBeams + ", dim " + StageLights.ShaderDim + "/" + StageLights.Dim + ", spots zero " + zero);
            bool hudOk = HudPresent.I != null && HudPresent.I.LightsButton != null && HudPresent.I.LightsButton.gameObject.activeInHierarchy;
            if (hudOk) HudPresent.I.LightsButton.onClick();
            yield return Wait(0.7f);
            Check("the HUD's lights switch turns them back on (persisted in PlayerPrefs)", () => hudOk && StageLights.On && PlayerPrefs.GetInt(StageLights.PrefKey, -1) == 1 && StageLights.Dim >= 0.999f ? "ok" : "hud " + hudOk + ", on " + StageLights.On + ", pref " + PlayerPrefs.GetInt(StageLights.PrefKey, -1) + ", dim " + F(StageLights.Dim));
        }
        // nothing left after stop / replay / a song load
        {
            GlobalClock.Stop();
            yield return Wait(0.7f);
            int afterStop = StageLights.ActiveBeams; float dStop = StageLights.ShaderDim;
            GlobalClock.Seek(0); GlobalClock.Play(); AudioCube.ScheduleAllNow();
            yield return Wait(0.7f);
            bool fresh = true; for (int k = 0; k < StageLights.MaxSpots; k++) { var s = StageLights.SpotIsland(k); if (s != null && (!SM.Islands.Contains(s) || StageLights.ScheduleAt(s, GlobalClock.SongBeatD) <= 0f)) fresh = false; }
            GlobalClock.Stop();
            Gallery.Open("crush", false);
            yield return Frames(4);
            int afterLoad = StageLights.ActiveBeams; float dLoad = StageLights.ShaderDim;
            Check("nothing left after stop, replay or a song load (no beams, dim 0; a replay lights only scheduled grids)", () => afterStop == 0 && dStop == 0f && fresh && afterLoad == 0 && dLoad == 0f ? "ok" : "stop " + afterStop + "/" + dStop + ", replay fresh " + fresh + ", load " + afterLoad + "/" + dLoad);
            Gallery.Open(Proto, false);
            yield return Frames(4);
            yield return Wait(0.8f);
        }
        // captures: lights on vs off on the same bars (a lead bar, a bar with no lead), a pop-in keyboard, the header, the HUD switch
        yield return Captures(popKb, noLead);
        // the present mode shows it
        yield return Present();
    }

    static IEnumerator PlayAt(int col, double beat, float wait)
    {
        var cam = OrbitCamera.I;
        if (cam != null) { cam.followPlayhead = true; cam.FrameColumn(Mathf.Clamp(col, 0, SM.ColumnCount - 1), 0.2f); }
        GlobalClock.Seek(beat); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait(wait);
    }

    static IEnumerator Captures(KeyBlock popKb, double noLead)
    {
        if (!shots) yield break;
        // a lead bar: the first lead part of a chord grid in C (column ≥ 4)
        var leadKb = SM.Islands.FirstOrDefault(k => k != null && k.kind == 0 && k.lead && k.column >= 4 && StageLights.PartsOf(k).Count > 0);
        if (leadKb != null)
        {
            double at = StageLights.PartsOf(leadKb)[0].x + 0.25;
            for (int pass = 0; pass < 2; pass++)
            {
                StageLights.SetOn(pass == 0);
                yield return PlayAt(leadKb.column, at, 0.75f);
                yield return Shot(pass == 0 ? "lights9_lead_on.png" : "lights9_lead_off.png");
                GlobalClock.Stop();
                yield return Wait(0.5f);
            }
            StageLights.SetOn(true);
        }
        if (!double.IsNaN(noLead))
        {
            for (int pass = 0; pass < 2; pass++)
            {
                StageLights.SetOn(pass == 0);
                yield return PlayAt(SM.ActiveColumn((float)noLead), noLead - 0.5, 0.75f);
                yield return Shot(pass == 0 ? "lights9_nolead_on.png" : "lights9_nolead_off.png");
                GlobalClock.Stop();
                yield return Wait(0.5f);
            }
            StageLights.SetOn(true);
        }
        popKb = SM.Islands.FirstOrDefault(k => k != null && k.IsKeyboard && KeyStage.Drives(k) && StageLights.PartsOf(k).Count > 0);   // (the song was reloaded since)
        if (popKb != null)
        {
            yield return PlayAt(popKb.column, StageLights.PartsOf(popKb)[0].x + 0.5, 0.75f);
            yield return Shot("lights9_popin.png");
            GlobalClock.Stop();
            yield return Wait(0.5f);
        }
        // the header with the lead button on and off (stopped: the normal look)
        var cam = OrbitCamera.I;
        var hk = SM.Islands.FirstOrDefault(k => k != null && k.kind == 0 && k.lead && k.column >= 4);
        if (hk != null)
        {
            if (cam != null) { cam.followPlayhead = false; cam.FrameColumn(hk.column, 0.2f); }
            yield return Wait(0.6f);
            IslandHeader.Show(hk);
            yield return Wait(0.8f);
            bool shown = IslandHeader.I != null && IslandHeader.I.LeadSpotShown;
            yield return Shot("lights9_header_on.png");
            int i = SM.Islands.IndexOf(hk);
            SM.SetLead(i, false);
            yield return Wait(0.4f);
            yield return Shot("lights9_header_off.png");
            Check("the island header shows the lead spotlight button on a chord grid (gold on, faint off)", () => shown ? "ok (captures lights9_header_on / _off)" : "not shown");
            SM.SetLead(i, true);
            IslandHeader.Hide();
            yield return Wait(0.4f);
        }
        if (cam != null) cam.FrameAll();
        yield return Wait(0.8f);
        yield return Shot("lights9_hud.png");
    }

    static IEnumerator Present()
    {
        GlobalClock.Stop();
        StageLights.SetOn(true);
        Presenter.Enter();
        float t0 = Time.realtimeSinceStartup;
        while (!(Presenter.Clock == Presenter.ClockMode.Live && Presenter.SongBeat > 17.0) && Time.realtimeSinceStartup - t0 < 25f) yield return null;
        yield return null;
        float dim = StageLights.Dim; int beams = StageLights.ActiveBeams;
        KeyBlock lit = null; for (int k = 0; k < StageLights.MaxSpots; k++) if (StageLights.SpotIsland(k) != null) lit = StageLights.SpotIsland(k);
        double beat = Presenter.SongBeat;
        bool sched = lit != null && StageLights.ScheduleAt(lit, beat) > 0f;
        Check("the present mode shows it: the dim stage and the spotlights of its own beat", () => Presenter.Active && dim >= 0.999f && (beams > 0 ? sched : !StageLights.AnyLeadAt(beat))
            ? "ok: beat " + F(beat) + ", " + beams + " beams" + (lit != null ? ", col " + lit.column : "") : "active " + Presenter.Active + ", dim " + F(dim) + ", beams " + beams + ", scheduled " + sched + ", beat " + F(beat) + ", clock " + Presenter.Clock);
        if (shots)
        {
            string p = Path.Combine(V2Checks.CapturePath, "lights9_present.png");
            if (File.Exists(p)) File.Delete(p);
            int n0 = Presenter.CapturesDone;
            Presenter.RequestCapture(p, 1600, 900);
            float w0 = Time.realtimeSinceStartup;
            while (Presenter.CapturesDone == n0 && Time.realtimeSinceStartup - w0 < 3f) yield return null;
            Info("capture lights9_present.png at present beat " + F(Presenter.SongBeat));
        }
        Presenter.Exit();
        float w1 = Time.realtimeSinceStartup;
        while (Presenter.Active && Time.realtimeSinceStartup - w1 < 5f) yield return null;
        yield return Wait(0.8f);
        Check("after the present mode: no beams, dim 0", () => StageLights.ActiveBeams == 0 && StageLights.ShaderDim == 0f ? "ok" : "beams " + StageLights.ActiveBeams + ", dim " + F(StageLights.ShaderDim));
    }
}
