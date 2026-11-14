using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Flow (F1, SPEC flow §1): the SPAN engine's Play-mode verification — a cube whose path is longer than its grid walks on onto the next grid(s) of
/// its lane as one melody. <c>FlowChecksEngine.RunAll(captures)</c> starts a coroutine; poll <c>FlowChecksEngine.Done</c> or
/// Captures/flow_engine_report.txt (numbered PASS / FAIL lines, INFO lines). Test songs are built from SongState: four one-bar chord grids in one
/// section (Am7, Dm7, G7, Cmaj7; 4/4, 120 bpm) with pluck cubes. Every audio check compares the notes the synth is handed while the song plays
/// (VoiceRules.Tap) with an expectation the check computes itself from the GRIDS' tiles (grid k's tile at the node's (x, z), folded as VoiceRules
/// folds a chord grid's note) and the note lengths — never from the cube's own RealTile / TileAt. Covers: the windows and the API (GridOfNode,
/// NodeStartBeat, CapacityOf, SpanChain, CanSpan, SpanTargets), eight quarters over two grids, a note ringing over the seam (one event, no second
/// attack), Loop / Once in pattern time, span 2 and a span longer than its chain, a home column repeat, other cubes left alone, SetSpan while the
/// song plays, a span on an extending grid, the legacy step engine, save / load / an old save / undo / redo / copy / paste / twins / octave copies,
/// the look (beads, the line over the seam, the body on the next grid; captures flow_engine_seam.png / flow_engine_walk.png), no console errors,
/// Synth late / errors. Never writes the user's saves (snapshotted and restored); reloads the fixture at the end.
/// </summary>
public static class FlowChecksEngine
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    static int lateExcused;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "flow_engine_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" FE-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); Line(sb, d == null, name, d ?? "ok"); }
        catch (Exception e) { Line(sb, false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    static string F(double v) => v.ToString("F2");
    static SongManager SM => SongManager.I;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test songs
    const int Pluck = 1, Keys = 0;   // pluck: no pad tones, no bass anchor, not sustaining, not mono (a note is one event)
    const int IdA = 9401, IdB = 9402, IdC = 9403, IdD = 9404, IdO = 9405;
    static readonly string[] ChordNames = { "Am7", "Dm7", "G7", "Cmaj7" };
    static readonly int[] ChordRoots = { 57, 62, 55, 60 };
    static readonly int[][] ChordSemis = { new[] { 0, 3, 7, 10 }, new[] { 0, 3, 7, 10 }, new[] { 0, 4, 7, 10 }, new[] { 0, 4, 7, 11 } };

    static MeasureState Chord(int col, int repeat = 1, int carry = 0) => new MeasureState
    {
        chordKey = ChordNames[col], root = ChordRoots[col], semis = (int[])ChordSemis[col].Clone(), bars = 1, kind = 0, col = col, repeat = repeat, energy = 2,
        carry = carry, carryStyle = carry > 0 ? 1 : 0
    };

    /// <summary>A durations cube: node i on tile (xs[i], zs[i]) of grid <paramref name="measure"/>, durs in ticks (24 = a beat).</summary>
    static CubeState Cube(int id, int measure, int inst, int span, int mode, int[] xs, int[] zs, int[] durs) => new CubeState
    {
        instrument = inst, measure = measure, xs = xs, zs = zs, rests = new bool[xs.Length], mods = new int[xs.Length], durs = durs,
        volume = 1f, moon = -1, twinOf = -1, echoOf = -1, id = id, step = 1, gate = 1, mode = mode, hits = -1, span = span
    };
    static int[] Rep(int v, int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

    static SongState Song(string name, MeasureState[] ms, params CubeState[] cubes) => new SongState
    {
        version = SongState.CurrentVersion, name = name, bpm = 120f, beatsPerBar = 4, loop = true, measures = ms, cubes = cubes,
        moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
    };
    static MeasureState[] FourChords(int repeat0 = 1, int carry0 = 0) => new[] { Chord(0, repeat0, carry0), Chord(1), Chord(2), Chord(3) };

    // the paths: row 0 / 1 for the span cube, row 3 for the bystander (no tile shared: rule 3's stack lift never comes into it)
    static readonly int[] AX = { 0, 1, 2, 3, 2, 3, 4, 5 }, AZ = { 0, 0, 0, 0, 1, 1, 1, 1 };
    static readonly int[] OX = { 0, 1, 2, 3 }, OZ = { 3, 3, 3, 3 };
    static CubeState CubeA(int span = 1) => Cube(IdA, 0, Pluck, span, 0, (int[])AX.Clone(), (int[])AZ.Clone(), Rep(24, 8));
    static CubeState CubeO() => Cube(IdO, 0, Keys, 0, 0, (int[])OX.Clone(), (int[])OZ.Clone(), Rep(24, 4));

    static void LoadState(SongState st)
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.ClearRegion();
        GlobalClock.Stop();
        SongState.Apply(st);
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
        AudioCube.SnapAllHome();
    }

    static AudioCube ById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }
    static KeyBlock Grid(int i) => SM != null && i >= 0 && i < SM.Islands.Count ? SM.Islands[i] : null;

    /// <summary>The check's own pitch: grid <paramref name="g"/>'s tile at (x, z), folded as a chord grid's note is (the slot's register fold with
    /// the grid's register).</summary>
    static int Pitch(int g, int x, int z, int inst)
    {
        var kb = Grid(g);
        var t = kb.GetTile(x, z);
        return VoiceRules.Fold(Instruments.SlotOf(inst, 0), t.midi + SongManager.Transpose, kb.register);
    }

    // ------------------------------------------------------------------ recording what the synth is handed
    struct Note { public double beat, len; public int midi, vel, owner; public KeyBlock island; }
    struct Want { public double beat; public int midi; public double len; public int grid; }
    static Want W(double beat, int grid, int x, int z, int inst, double len = -1) => new Want { beat = beat, midi = Pitch(grid, x, z, inst), len = len, grid = grid };

    /// <summary>Plays the song from <paramref name="a"/> to <paramref name="b"/> (beats) and collects every note the cubes hand the synth, in song
    /// beats (the note's on relative to the DSP time of beat a). <paramref name="frame"/> is called every frame with the song beat.</summary>
    static IEnumerator Record(float a, float b, List<Note> log, Action<float> frame = null)
    {
        log.Clear();
        double t0 = 0, bps = GlobalClock.BeatsPerSecond;
        var raw = new List<VoiceRules.NoteEvent>();
        VoiceRules.Tap = e => raw.Add(e);
        GlobalClock.SetRegion(a, b);
        GlobalClock.Seek(a); GlobalClock.Play();
        t0 = GlobalClock.DspTimeOfBeat(a); bps = GlobalClock.BeatsPerSecond;
        AudioCube.ScheduleAllNow();
        float secs = (float)((b - a) / bps), start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < secs + 0.1f)
        {
            if (frame != null && GlobalClock.IsPlaying) { try { frame(GlobalClock.SongBeat); } catch (Exception) { } }
            yield return null;
        }
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        VoiceRules.Tap = null;
        foreach (var e in raw)
        {
            if (e.owner < 16) continue;
            double beat = a + (e.onDsp - t0) * bps;
            if (beat >= b - 0.03) continue;   // the loop's next pass, queued by the lookahead
            log.Add(new Note { beat = beat, len = (e.offDsp - e.onDsp) * bps, midi = e.midi, vel = e.vel, owner = e.owner, island = e.island });
        }
        log.Sort((x, y) => x.beat.CompareTo(y.beat));
        yield return Frames(2);
    }

    static List<Note> Of(List<Note> log, AudioCube c) => c == null ? new List<Note>() : log.Where(n => n.owner == c.owner).ToList();
    static string Show(List<Note> l) => string.Join(" ", l.Select(n => n.midi + "@" + F(n.beat)));
    static string Show(List<Want> l) => string.Join(" ", l.Select(n => n.midi + "@" + F(n.beat)));

    /// <summary>The cube's notes are exactly <paramref name="want"/>: as many, each within 0.06 beat (the engine humanises a note up to 8 ms late)
    /// at the wanted pitch, stamped with the wanted grid, and — where a length is given — that long (± 0.04 beat).</summary>
    static string Match(List<Note> log, AudioCube c, List<Want> want)
    {
        if (c == null) return "no cube";
        var got = Of(log, c);
        if (got.Count == 0 && log.Count == 0) return "the synth got no notes at all — the audio clock may be frozen (environmental): dsp " + AudioSettings.dspTime.ToString("F2");
        if (got.Count != want.Count) return got.Count + " notes, want " + want.Count + ": got " + Show(got) + " | want " + Show(want);
        for (int i = 0; i < want.Count; i++)
        {
            if (Math.Abs(got[i].beat - want[i].beat) > 0.06) return "note " + i + " at beat " + F(got[i].beat) + ", want " + F(want[i].beat) + ": got " + Show(got) + " | want " + Show(want);
            if (got[i].midi != want[i].midi) return "note " + i + " (beat " + F(want[i].beat) + ") played " + got[i].midi + ", want " + want[i].midi + ": got " + Show(got) + " | want " + Show(want);
            if (want[i].grid >= 0 && got[i].island != Grid(want[i].grid)) return "note " + i + " stamped with grid " + (SM != null ? SM.Islands.IndexOf(got[i].island) : -9) + ", want " + want[i].grid;
            if (want[i].len > 0 && Math.Abs(got[i].len - want[i].len) > 0.04) return "note " + i + " (beat " + F(want[i].beat) + ") lasts " + F(got[i].len) + " beats, want " + F(want[i].len);
        }
        return null;
    }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear(); lateExcused = 0;
        SequenceMaster.I.StartCoroutine(AllRoutine(captures));
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void Restore()
    {
        PathManager.SimOnly = false;
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        try { if (Clipboard.HasPattern) Clipboard.Clear(); } catch (Exception) { }
        PathManager.AutoHand = false;
        VoiceRules.Tap = null;
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
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float timeout)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; yield return null; }
    }

    /// <summary>The body is over <paramref name="kb"/>'s platform (x / z, a small margin).</summary>
    static bool Over(KeyBlock kb, Vector3 p)
    {
        if (kb == null) return false;
        var b = kb.WorldBounds;
        return p.x >= b.min.x - 0.2f && p.x <= b.max.x + 0.2f && p.z >= b.min.z - 0.2f && p.z <= b.max.z + 0.2f;
    }
    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return (a - b).magnitude; }

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("FlowChecksEngine ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        Prepare();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        var log = new List<Note>();

        // ================================================================ g. the windows and the API
        LoadState(Song("flow A", FourChords(), CubeA(1), CubeO()));
        yield return Frames(4);
        Run(sb, "g. the test song: four one-bar chord grids in one section, one lane (LaneNext 0 → 1 → 2 → 3 → none), 16 beats", () =>
        {
            if (SM.Islands.Count != 4 || SM.ColumnCount != 4) return "islands " + SM.Islands.Count + " columns " + SM.ColumnCount;
            if (Mathf.Abs(SM.TotalBeats - 16f) > 1e-3f) return "total " + SM.TotalBeats;
            for (int i = 0; i < 4; i++)
            {
                var nx = SM.LaneNext(Grid(i));
                if (i < 3 ? nx != Grid(i + 1) : nx != null) return "LaneNext(" + i + ") = " + (nx == null ? -1 : SM.Islands.IndexOf(nx));
                if (Grid(i).cols < 6 || Grid(i).rows < 4) return "grid " + i + " is " + Grid(i).cols + "x" + Grid(i).rows;
            }
            // the check is only worth something when the two grids' tiles differ where the melody goes
            int diff = 0;
            for (int i = 4; i < 8; i++) if (Pitch(0, AX[i], AZ[i], Pluck) != Pitch(1, AX[i], AZ[i], Pluck)) diff++;
            if (diff < 3) return "grid 0 and grid 1 play the same pitch on " + (4 - diff) + " of the melody's last four tiles: the song cannot tell them apart";
            return null;
        });
        Run(sb, "g. a span-1 cube of eight quarters: its home window (offset 0) then ONE through window on grid 1 (start 4, length 4, offset 4); the span-0 cube beside it keeps one plain window", () =>
        {
            var a = ById(IdA); var o = ById(IdO);
            if (a == null || o == null) return "cubes missing";
            if (a.span != 1 || !a.HasThrough) return "span " + a.span + " through " + a.HasThrough;
            if (a.windows.Count != 2) return a.windows.Count + " windows";
            var w0 = a.windows[0]; var w1 = a.windows[1];
            if (w0.island != Grid(0) || w0.start != 0f || w0.length != 4f || w0.offset != 0f || w0.through || w0.carried) return "home window " + w0.start + "+" + w0.length + " off " + w0.offset + " through " + w0.through;
            if (w1.island != Grid(1) || w1.start != 4f || w1.length != 4f || w1.offset != 4f || !w1.through || w1.carried || w1.hop != 1) return "through window " + w1.start + "+" + w1.length + " off " + w1.offset + " through " + w1.through + " hop " + w1.hop;
            if (a.WindowOffset(0) != 0f || a.WindowOffset(1) != 4f || a.FirstStepIn(0) != 0 || a.FirstStepIn(1) != 4) return "WindowOffset / FirstStepIn " + a.WindowOffset(1) + " / " + a.FirstStepIn(1);
            if (o.span != 0 || o.HasThrough || o.windows.Count != 1 || o.windows[0].offset != 0f || o.windows[0].through) return "the span-0 cube: " + o.windows.Count + " windows, through " + o.HasThrough;
            for (int i = 0; i < o.nodes.Count; i++) if (o.RealTile(i) != o.nodes[i] || o.GridOfNode(i) != Grid(0)) return "span 0: node " + i + " is not on its own tile";
            return null;
        });
        Run(sb, "g. GridOfNode / NodeStartBeat / RealTile: nodes 0–3 start at beats 0–3 on grid 0, nodes 4–7 at 4–7 on grid 1's tile at the same (x, z); VoiceRules.NodeMidi = that tile's pitch; GridAtBeat (beat 4 on = grid 1, past the chain = its last grid)", () =>
        {
            var a = ById(IdA);
            for (int i = 0; i < 8; i++)
            {
                int g = i < 4 ? 0 : 1;
                if (Mathf.Abs(a.NodeStartBeat(i) - i) > 1e-4f) return "node " + i + " starts at " + a.NodeStartBeat(i);
                if (a.GridOfNode(i) != Grid(g)) return "node " + i + " on grid " + SM.Islands.IndexOf(a.GridOfNode(i)) + ", want " + g;
                var want = Grid(g).GetTile(AX[i], AZ[i]);
                if (a.RealTile(i) != want) return "node " + i + ": RealTile is not grid " + g + "'s (" + AX[i] + "," + AZ[i] + ")";
                if (a.nodes[i] != Grid(0).GetTile(AX[i], AZ[i])) return "node " + i + ": nodes[] is not the home grid's tile";
                if (VoiceRules.NodeMidi(a, i) != Pitch(g, AX[i], AZ[i], Pluck)) return "node " + i + ": NodeMidi " + VoiceRules.NodeMidi(a, i) + ", want " + Pitch(g, AX[i], AZ[i], Pluck);
            }
            if (a.GridAtBeat(0f) != Grid(0) || a.GridAtBeat(3.99f) != Grid(0) || a.GridAtBeat(4f) != Grid(1) || a.GridAtBeat(8f) != Grid(1) || ById(IdO).GridAtBeat(6f) != Grid(0))
                return "GridAtBeat: 3.99 → " + SM.Islands.IndexOf(a.GridAtBeat(3.99f)) + ", 4 → " + SM.Islands.IndexOf(a.GridAtBeat(4f)) + ", 8 (past the chain) → " + SM.Islands.IndexOf(a.GridAtBeat(8f));
            return null;
        });
        Run(sb, "g. PathManager.CapacityOf(home, span) = 4 / 8 / 12 / 16 beats from grid 0, 8 from grid 2 with span 3 (the chain ends); CapacityOf(home) unchanged; SpanChain / CanSpan / SpanTargets", () =>
        {
            for (int s = 0; s <= 3; s++) if (Mathf.Abs(PathManager.CapacityOf(Grid(0), s) - 4f * (s + 1)) > 1e-3f) return "CapacityOf(grid 0, " + s + ") = " + PathManager.CapacityOf(Grid(0), s);
            if (Mathf.Abs(PathManager.CapacityOf(Grid(2), 3) - 8f) > 1e-3f) return "CapacityOf(grid 2, 3) = " + PathManager.CapacityOf(Grid(2), 3);
            if (PathManager.CapacityOf(Grid(0)) != 4f || PathManager.CapacityOf(Grid(0), 0) != PathManager.CapacityOf(Grid(0))) return "CapacityOf(grid 0) = " + PathManager.CapacityOf(Grid(0));
            var ch = SM.SpanChain(Grid(0), 2);
            if (ch.Count != 3 || ch[0] != Grid(0) || ch[1] != Grid(1) || ch[2] != Grid(2)) return "SpanChain(grid 0, 2) has " + ch.Count;
            var ch2 = SM.SpanChain(Grid(2), 3);
            if (ch2.Count != 2 || ch2[0] != Grid(2) || ch2[1] != Grid(3)) return "SpanChain(grid 2, 3) has " + ch2.Count;
            if (SM.SpanChain(Grid(3), 0).Count != 1) return "SpanChain(grid 3, 0)";
            if (!SongManager.CanSpan(Grid(0), 3) || !SongManager.CanSpan(Grid(0), 0) || SongManager.CanSpan(Grid(2), 2) || !SongManager.CanSpan(Grid(2), 1) || SongManager.CanSpan(Grid(3), 1) || SongManager.CanSpan(null, 0))
                return "CanSpan: (0,3) " + SongManager.CanSpan(Grid(0), 3) + " (2,2) " + SongManager.CanSpan(Grid(2), 2) + " (2,1) " + SongManager.CanSpan(Grid(2), 1) + " (3,1) " + SongManager.CanSpan(Grid(3), 1);
            var into = new List<KeyBlock> { Grid(3) };
            SM.SpanTargets(ById(IdA), into);
            if (into.Count != 1 || into[0] != Grid(1)) return "SpanTargets(span-1 cube) has " + into.Count;
            SM.SpanTargets(ById(IdO), into);
            if (into.Count != 0) return "SpanTargets(span-0 cube) has " + into.Count;
            return null;
        });

        // ================================================================ a. + i. eight quarters over two grids; the body walks onto grid 1
        {
            var a = ById(IdA);
            int on0 = 0, n0 = 0, on1 = 0, n1 = 0; float worst0 = 0f, worst1 = 0f; int crossed = 0;
            int nHold = 0, onHold = 0, nHome = 0, onHome = 0;
            int cross0 = AudioCube.CrossCount, flights0 = AudioCube.FlightCount;
            bool shot = false, shotDone = false; int lateAtShot = 0;
            if (captures && OrbitCamera.I != null)
            {
                var b = Grid(0).WorldBounds; b.Encapsulate(Grid(1).WorldBounds); b.Expand(new Vector3(1f, 0f, 1f));
                OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.95f);
                yield return Wait(0.8f);
            }
            yield return Record(0f, 16f, log, beat =>
            {
                if (a == null) return;
                Vector3 p = a.transform.position;
                if (beat > 0.3f && beat < 3.4f) { n0++; if (Over(Grid(0), p)) on0++; }
                if (beat > 4.3f && beat < 7.4f) { n1++; if (Over(Grid(1), p)) on1++; }
                if (a.CrossingMeasure) crossed++;
                if (beat > 8.5f && beat < 15f) { nHold++; if (a.Holding && Over(Grid(1), p)) onHold++; }      // after the chain: it holds where the melody ended
                if (beat > 15.5f && beat < 15.98f) { nHome++; if (a.Tripping) onHome++; }                      // ... and travels home right before it plays again
                // standing on its note's tile in the first half of each note (before the hop to the next one)
                float fr = beat - Mathf.Floor(beat); int i = Mathf.FloorToInt(beat);
                if (fr > 0.2f && fr < 0.5f && i >= 0 && i < 8)
                {
                    float d = Flat(p, Grid(i < 4 ? 0 : 1).GetTile(AX[i], AZ[i]).Top);
                    if (i < 4) worst0 = Mathf.Max(worst0, d); else worst1 = Mathf.Max(worst1, d);
                }
                if (captures && !shot && beat > 5.3f && beat < 5.5f)
                {
                    shot = true; lateAtShot = Synth.LateEvents;
                    string f = Path.Combine(V2Checks.CapturePath, "flow_engine_walk.png");
                    try { if (File.Exists(f)) File.Delete(f); ScreenCapture.CaptureScreenshot(f, 1); } catch (Exception) { }
                }
                // a capture taken while playing may make the next notes late: those (up to a beat and a half after it) are counted apart
                if (shot && !shotDone && beat > 7f) { shotDone = true; lateExcused += Synth.LateEvents - lateAtShot; }
            });
            Run(sb, "a. the synth is handed notes 0–3 with grid 0's tile pitches on beats 0–3 and notes 4–7 with grid 1's tile pitches (same x, z) on beats 4–7, each 0.92 beat, stamped with its grid; nothing from it in bars 3–4", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) want.Add(W(i, i < 4 ? 0 : 1, AX[i], AZ[i], Pluck, 0.92));
                string r = Match(log, a, want);
                if (r != null) return r;
                foreach (var n in Of(log, a)) if (n.beat >= 7.9) return "a note at beat " + F(n.beat);
                return null;
            });
            Info(sb, "a. played " + Show(Of(log, a)) + " (home tiles of nodes 4–7 would be " + string.Join(" ", Enumerable.Range(4, 4).Select(i => Pitch(0, AX[i], AZ[i], Pluck))) + ")");
            Run(sb, "i. while bar 1 plays the body is over grid 0 and while bar 2 plays over grid 1, standing on each note's own tile; it steps over the seam (one measure crossing, no flight)", () =>
            {
                if (n0 < 10 || n1 < 10) return "too few frames sampled: " + n0 + " / " + n1;
                if (on0 != n0) return "bar 1: over grid 0 in " + on0 + " of " + n0 + " frames";
                if (on1 != n1) return "bar 2: over grid 1 in " + on1 + " of " + n1 + " frames";
                if (worst0 > 0.15f || worst1 > 0.15f) return "off its note's tile by " + F(worst0) + " (grid 0) / " + F(worst1) + " (grid 1)";
                if (AudioCube.CrossCount - cross0 < 1 || crossed == 0) return "no step over the seam (crossings " + (AudioCube.CrossCount - cross0) + ", frames crossing " + crossed + ")";
                if (AudioCube.FlightCount != flights0 || a.Flying) return "a flight";
                return null;
            });
            Run(sb, "i. after the chain (bars 3–4) the body HOLDS on grid 1 where the melody ended, and travels home along the grids in the last half beat before the song starts over (the existing hold-then-return rule)", () =>
            {
                if (nHold < 20 || onHold != nHold) return "bars 3–4: holding over grid 1 in " + onHold + " of " + nHold + " frames";
                if (nHome < 3 || onHome != nHome) return "the last half beat: on its trip home in " + onHome + " of " + nHome + " frames";
                return null;
            });
            var olog = Of(log, ById(IdO));
            Run(sb, "f. the span-0 cube on grid 0 plays its four quarters on grid 0's tiles in bar 1 and nothing else (with the span cube beside it)", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 4; i++) want.Add(W(i, 0, OX[i], OZ[i], Keys));
                return Match(log, ById(IdO), want);
            });
            // the same song without the span cube
            LoadState(Song("flow A'", FourChords(), CubeO()));
            yield return Frames(4);
            yield return Record(0f, 16f, log);
            var olog2 = Of(log, ById(IdO));
            Run(sb, "f. ... and its notes are the same with and without the span cube (pitch, velocity, beat ± 0.01, length ± 0.01)", () =>
            {
                if (olog.Count == 0) return "no notes";
                if (olog.Count != olog2.Count) return olog.Count + " notes with, " + olog2.Count + " without";
                for (int i = 0; i < olog.Count; i++)
                    if (olog[i].midi != olog2[i].midi || olog[i].vel != olog2[i].vel || Math.Abs(olog[i].beat - olog2[i].beat) > 0.01 || Math.Abs(olog[i].len - olog2[i].len) > 0.01)
                        return "note " + i + ": " + olog[i].midi + " v" + olog[i].vel + " @" + F(olog[i].beat) + " len " + F(olog[i].len) + " with, " + olog2[i].midi + " v" + olog2[i].vel + " @" + F(olog2[i].beat) + " len " + F(olog2[i].len) + " without";
                return null;
            });
        }

        // ================================================================ b. a note ringing over the seam
        {
            // starts 0 1 2 3 3.5 (two beats, over the seam) 5.5 6.5 7.5
            int[] durs = { 24, 24, 24, 12, 48, 24, 24, 12 };
            double[] starts = { 0, 1, 2, 3, 3.5, 5.5, 6.5, 7.5 };
            LoadState(Song("flow B", FourChords(), Cube(IdB, 0, Pluck, 1, 0, (int[])AX.Clone(), (int[])AZ.Clone(), durs)));
            yield return Frames(4);
            var c = ById(IdB);
            int n0 = 0, on0 = 0, n1 = 0, on1 = 0;
            var landed = new HashSet<int>();   // (window, step) pairs: the loop's next pass lands its first step again
            Action<AudioCube, int, int, AudioCube.Hit> onLand = (cube, w, k, hit) => { if (cube == c) landed.Add(w * 1000 + k); };
            AudioCube.OnLanded += onLand;
            yield return Record(0f, 8f, log, beat =>
            {
                if (c == null) return;
                Vector3 p = c.transform.position;
                if (beat > 4.05f && beat < 4.9f) { n0++; if (Over(Grid(0), p) && Flat(p, Grid(0).GetTile(AX[4], AZ[4]).Top) < 0.15f) on0++; }
                if (beat > 5.6f && beat < 6f) { n1++; if (Over(Grid(1), p) && Flat(p, Grid(1).GetTile(AX[5], AZ[5]).Top) < 0.15f) on1++; }
            });
            AudioCube.OnLanded -= onLand;
            Run(sb, "b. a two-beat note starting on beat 3.5 is ONE note event (grid 0's pitch, 1.84 beats: it rings over the seam) — no second attack on beat 4 — and the next note starts on beat 5.5 on grid 1", () =>
            {
                if (c == null) return "no cube";
                if (c.GridOfNode(4) != Grid(0) || c.GridOfNode(5) != Grid(1) || Mathf.Abs(c.NodeStartBeat(4) - 3.5f) > 1e-4f || Mathf.Abs(c.NodeStartBeat(5) - 5.5f) > 1e-4f)
                    return "node 4 on grid " + SM.Islands.IndexOf(c.GridOfNode(4)) + " at " + c.NodeStartBeat(4) + ", node 5 on grid " + SM.Islands.IndexOf(c.GridOfNode(5)) + " at " + c.NodeStartBeat(5);
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) want.Add(W(starts[i], starts[i] < 4 ? 0 : 1, AX[i], AZ[i], Pluck, 0.92 * durs[i] / 24.0));
                string r = Match(log, c, want);
                if (r != null) return r;
                var got = Of(log, c);
                foreach (var n in got) if (Math.Abs(n.beat - 4.0) < 0.2) return "an attack at beat " + F(n.beat);
                if (got[4].beat + got[4].len < 4.5) return "the long note ends at beat " + F(got[4].beat + got[4].len) + ": it does not ring over the seam";
                return null;
            });
            Run(sb, "b. the look agrees: eight landings (steps 0–4 in the home window, 5–7 in the through window; none at the seam), the body stays on the long note's tile on grid 0 through beats 4–4.9 and stands on grid 1's tile after beat 5.5", () =>
            {
                if (landed.Count != 8 || landed.Contains(1004)) return landed.Count + " landings" + (landed.Contains(1004) ? ", one of them the ringing note again in the next window" : "") + ": " + string.Join(" ", landed.OrderBy(v => v));
                for (int k = 0; k < 8; k++) if (!landed.Contains((k < 5 ? 0 : 1) * 1000 + k)) return "step " + k + " never landed in window " + (k < 5 ? 0 : 1);
                if (n0 < 5 || on0 != n0) return "beats 4–4.9: on its grid-0 tile in " + on0 + " of " + n0 + " frames";
                if (n1 < 5 || on1 != n1) return "beats 5.6–6: on its grid-1 tile in " + on1 + " of " + n1 + " frames";
                return null;
            });
            Run(sb, "b. TimelineEvents: the home window's long note is not clipped at the window end (2 beats from 3.5); the through window's events are the three notes that start in it (1.5, 2.5, 3.5 window-local)", () =>
            {
                var ev = new List<AudioCube.TimelineEvent>();
                c.TimelineEvents(0, ev);
                if (ev.Count != 5 || Mathf.Abs(ev[4].start - 3.5f) > 1e-3f || Mathf.Abs(ev[4].len - 2f) > 1e-3f || ev[4].node != 4) return "home: " + ev.Count + " events, last " + (ev.Count > 0 ? ev[ev.Count - 1].start + "+" + ev[ev.Count - 1].len : "-");
                c.TimelineEvents(1, ev);
                if (ev.Count != 3 || ev[0].node != 5 || Mathf.Abs(ev[0].start - 1.5f) > 1e-3f || Mathf.Abs(ev[2].start - 3.5f) > 1e-3f || Mathf.Abs(ev[2].len - 0.5f) > 1e-3f) return "through: " + ev.Count + " events, first " + (ev.Count > 0 ? ev[0].node + "@" + ev[0].start : "-");
                var s16 = c.HitSteps16(1, c.PassFor(1, 0));
                if (s16.Count != 3 || s16[0] != 6 || s16[1] != 10 || s16[2] != 14) return "HitSteps16(through) = " + string.Join(",", s16);
                return null;
            });
        }

        // ================================================================ a loop over just the chain's two columns (what drawing while playing loops)
        {
            LoadState(Song("flow R", FourChords(), CubeA(1)));
            yield return Frames(4);
            var a = ById(IdA);
            int nBack = 0, onBack = 0, lowBack = 0, pass2 = 0, onPass2 = 0; float lastBeat = -1f; bool wrapped = false; int flights0 = AudioCube.FlightCount;
            var raw = new List<VoiceRules.NoteEvent>();
            VoiceRules.Tap = e => raw.Add(e);
            GlobalClock.SetRegion(0f, 8f);
            GlobalClock.Seek(0f); GlobalClock.Play();
            double t0 = GlobalClock.DspTimeOfBeat(0), bps = GlobalClock.BeatsPerSecond;
            AudioCube.ScheduleAllNow();
            float start = Time.realtimeSinceStartup;
            bool shot = false; int lateAtShot = 0;
            while (Time.realtimeSinceStartup - start < 11.9f / (float)bps)   // one pass and three beats and a bit of the next
            {
                float beat = GlobalClock.SongBeat;
                if (beat < lastBeat - 1f) wrapped = true;
                lastBeat = beat;
                if (a != null)
                {
                    Vector3 p = a.transform.position;
                    if (!wrapped && beat > 7.62f && beat < 7.97f) { nBack++; if (a.Tripping) onBack++; if (p.y < Grid(1).GetTile(AX[7], AZ[7]).Top.y + 1.2f) lowBack++; }
                    if (wrapped && beat > 0.2f && beat < 0.5f) { pass2++; if (Flat(p, Grid(0).GetTile(AX[0], AZ[0]).Top) < 0.15f) onPass2++; }
                    if (captures && !shot && !wrapped && beat > 7.72f && beat < 7.9f)
                    {
                        shot = true; lateAtShot = Synth.LateEvents;
                        string f = Path.Combine(V2Checks.CapturePath, "flow_engine_return.png");
                        try { if (File.Exists(f)) File.Delete(f); ScreenCapture.CaptureScreenshot(f, 1); } catch (Exception) { }
                    }
                }
                yield return null;
            }
            if (shot) lateExcused += Synth.LateEvents - lateAtShot;
            GlobalClock.Stop(); GlobalClock.ClearRegion(); VoiceRules.Tap = null;
            yield return Frames(2);
            Run(sb, "a loop over the chain's two columns: at the loop point the body skips back to its first tile along the grids (a trip in small hops, never a flight) and the second pass plays notes 0–2 again on grid 0, on time", () =>
            {
                if (a == null) return "no cube";
                if (nBack < 3 || onBack != nBack) return "the last 0.35 beat: on its trip back in " + onBack + " of " + nBack + " frames";
                if (lowBack != nBack) return "the trip back rose high above the tiles in " + (nBack - lowBack) + " frames";
                if (AudioCube.FlightCount != flights0) return "a flight";
                if (pass2 < 3 || onPass2 != pass2) return "second pass, beat 0.2–0.5: on its first tile in " + onPass2 + " of " + pass2 + " frames";
                var mine = raw.Where(e => e.owner == a.owner).Select(e => new Note { beat = (e.onDsp - t0) * bps, midi = e.midi, island = e.island }).OrderBy(n => n.beat).ToList();
                if (mine.Count < 11) return mine.Count + " notes in a pass and three beats: " + Show(mine);
                for (int i = 0; i < 11; i++)
                {
                    int nd = i % 8;
                    if (Math.Abs(mine[i].beat - i) > 0.06 || mine[i].midi != Pitch(nd < 4 ? 0 : 1, AX[nd], AZ[nd], Pluck)) return "note " + i + ": " + mine[i].midi + "@" + F(mine[i].beat) + ", want " + Pitch(nd < 4 ? 0 : 1, AX[nd], AZ[nd], Pluck) + "@" + i + ": " + Show(mine);
                }
                return null;
            });
        }

        // ================================================================ c. Loop and Once in pattern time
        {
            int[] cx = { 0, 1, 2, 3, 4, 5 }, cz = { 0, 0, 0, 0, 0, 0 };
            int[] dx = { 0, 1, 2, 3, 4 }, dz = { 2, 2, 2, 2, 2 };
            LoadState(Song("flow C", FourChords(), Cube(IdC, 0, Pluck, 1, (int)PathMode.Loop, cx, cz, Rep(24, 6)), Cube(IdD, 0, Pluck, 1, (int)PathMode.Once, dx, dz, Rep(24, 5))));
            yield return Frames(4);
            yield return Record(0f, 8f, log);
            Run(sb, "c. Loop, a 6-beat pattern over an 8-beat chain: beats 0–3 notes 0–3 on grid 0, beats 4–5 notes 4–5 on grid 1, beats 6–7 the pattern's first two notes again — on grid 1's tiles", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) { int nd = i % 6; want.Add(W(i, i < 4 ? 0 : 1, cx[nd], cz[nd], Pluck, 0.92)); }
                return Match(log, ById(IdC), want);
            });
            Run(sb, "c. Once, a 5-beat pattern: notes 0–3 on grid 0, note 4 on grid 1 on beat 4, then silent from beat 5 to the chain's end", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 5; i++) want.Add(W(i, i < 4 ? 0 : 1, dx[i], dz[i], Pluck, 0.92));
                string r = Match(log, ById(IdD), want);
                if (r != null) return r;
                foreach (var n in Of(log, ById(IdD))) if (n.beat > 4.9) return "a note at beat " + F(n.beat);
                return null;
            });
        }

        // ================================================================ d. span 2; a span longer than its chain
        {
            int[] x12 = { 0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0 }, z0 = { 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1 }, z2 = { 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3 };
            LoadState(Song("flow D", FourChords(), Cube(IdC, 0, Pluck, 2, 0, x12, z0, Rep(24, 12)), Cube(IdD, 2, Pluck, 3, 0, (int[])x12.Clone(), z2, Rep(24, 12))));
            yield return Frames(4);
            Run(sb, "d. windows: span 2 from grid 0 = home + through windows on grids 1 and 2 (offsets 4, 8); span 3 from grid 2 = home + ONE through window on grid 3 (the chain ends: it plays what exists)", () =>
            {
                var c2 = ById(IdC); var c3 = ById(IdD);
                if (c2 == null || c3 == null) return "cubes missing";
                if (c2.windows.Count != 3 || c2.windows[1].island != Grid(1) || c2.windows[1].offset != 4f || c2.windows[2].island != Grid(2) || c2.windows[2].offset != 8f || c2.windows[2].start != 8f) return "span 2: " + c2.windows.Count + " windows";
                if (c3.span != 3 || c3.windows.Count != 2 || c3.windows[0].island != Grid(2) || c3.windows[0].start != 8f || c3.windows[1].island != Grid(3) || c3.windows[1].offset != 4f || c3.windows[1].start != 12f) return "span 3 from grid 2: " + c3.windows.Count + " windows";
                if (c2.GridOfNode(9) != Grid(2) || c3.GridOfNode(5) != Grid(3) || c3.GridOfNode(11) != Grid(3)) return "GridOfNode: span 2 node 9 on " + SM.Islands.IndexOf(c2.GridOfNode(9)) + ", span 3 node 11 on " + SM.Islands.IndexOf(c3.GridOfNode(11));
                return null;
            });
            yield return Record(0f, 16f, log);
            Run(sb, "d. span 2 over three grids: twelve quarters, four per grid on that grid's own tiles (beats 0–11), nothing in bar 4", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 12; i++) want.Add(W(i, i / 4, x12[i], z0[i], Pluck, 0.92));
                return Match(log, ById(IdC), want);
            });
            Run(sb, "d. span 3 where the lane has one more grid: bars 3 and 4 play notes 0–7 (grid 2, grid 3), notes 8–11 are silent, no error", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) want.Add(W(8 + i, 2 + i / 4, x12[i], z2[i], Pluck, 0.92));
                string r = Match(log, ById(IdD), want);
                if (r != null) return r;
                return errors.Count == 0 ? null : "console: " + errors[0];
            });
        }

        // ================================================================ e. a home column repeat
        {
            LoadState(Song("flow E", FourChords(2), CubeA(1)));
            yield return Frames(4);
            Run(sb, "e. home column repeat ×2: two home windows (0 and 4, offset 0 both), then the through window on grid 1 at beat 8 (offset 4); the song is 20 beats", () =>
            {
                var a = ById(IdA);
                if (a == null) return "no cube";
                if (Mathf.Abs(SM.TotalBeats - 20f) > 1e-3f) return "total " + SM.TotalBeats;
                if (a.windows.Count != 3) return a.windows.Count + " windows";
                if (a.windows[0].start != 0f || a.windows[1].start != 4f || a.windows[0].offset != 0f || a.windows[1].offset != 0f || a.windows[1].through || a.windows[1].island != Grid(0)) return "home passes " + a.windows[0].start + " / " + a.windows[1].start + " off " + a.windows[1].offset;
                if (a.windows[2].start != 8f || a.windows[2].offset != 4f || !a.windows[2].through || a.windows[2].island != Grid(1)) return "through " + a.windows[2].start + " off " + a.windows[2].offset;
                return null;
            });
            yield return Record(0f, 12f, log);
            Run(sb, "e. ... both passes replay notes 0–3 on grid 0 (beats 0–3 and 4–7), grid 1 follows with notes 4–7 on beats 8–11", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 4; i++) want.Add(W(i, 0, AX[i], AZ[i], Pluck, 0.92));
                for (int i = 0; i < 4; i++) want.Add(W(4 + i, 0, AX[i], AZ[i], Pluck, 0.92));
                for (int i = 4; i < 8; i++) want.Add(W(4 + i, 1, AX[i], AZ[i], Pluck, 0.92));
                return Match(log, ById(IdA), want);
            });
        }

        // ================================================================ SetSpan while the song plays (what the next package's drawing does)
        {
            LoadState(Song("flow L", FourChords(), CubeA(0)));
            yield return Frames(4);
            var a = ById(IdA);
            bool set = false;
            yield return Record(0f, 8f, log, beat =>
            {
                if (set || beat < 1.4f || a == null) return;
                set = true;
                a.SetSpan(1);
            });
            Run(sb, "SetSpan(1) at beat 1.4 of a playing span-0 cube of eight quarters: bar 1 plays notes 0–3 once each (nothing cut, nothing struck twice), bar 2 goes on with notes 4–7 on grid 1", () =>
            {
                if (!set) return "SetSpan never ran";
                if (a.span != 1 || a.windows.Count != 2 || !a.windows[1].through) return "span " + a.span + ", " + a.windows.Count + " windows";
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) want.Add(W(i, i < 4 ? 0 : 1, AX[i], AZ[i], Pluck, 0.92));
                return Match(log, a, want);
            });
            Run(sb, "SetSpan clamps to 0..MaxCarry, does nothing when unchanged, raises OnAnyChanged once per change; back at 0 the cube is a plain cube again", () =>
            {
                int raised = 0; Action<AudioCube> h = c => { if (c == a) raised++; };
                AudioCube.OnAnyChanged += h;
                try
                {
                    a.SetSpan(1); if (raised != 0) return "an unchanged span raised " + raised;
                    a.SetSpan(9); if (a.span != ProjectConfig.MaxCarry) return "span " + a.span + " after SetSpan(9)";
                    if (raised != 1) return "raised " + raised;
                    if (a.windows.Count != 4) return "span 3 from grid 0: " + a.windows.Count + " windows";
                    a.SetSpan(-2); if (a.span != 0 || a.HasThrough || a.windows.Count != 1) return "span " + a.span + " windows " + a.windows.Count + " after SetSpan(-2)";
                    for (int i = 0; i < 8; i++) if (a.RealTile(i) != a.nodes[i]) return "span 0: node " + i + " not on its home tile";
                }
                finally { AudioCube.OnAnyChanged -= h; }
                return null;
            });
        }

        // ================================================================ span on an extending (carry) grid; the legacy step engine
        {
            LoadState(Song("flow X", FourChords(1, 3), CubeA(1), CubeO()));
            yield return Frames(4);
            Run(sb, "a span-1 cube on a grid that also extends over 3 grids keeps its span windows and takes NO carried window: the melody starts over every 2 grids (offsets 0, 4, 0, 4), on each grid's own tiles; the span-0 cube beside it keeps its 3 carried windows", () =>
            {
                var a = ById(IdA); var o = ById(IdO);
                if (a == null || o == null) return "cubes missing";
                if (SM.CarryTargets(0).Count != 3) return "carry targets " + SM.CarryTargets(0).Count;
                if (a.windows.Count != 4) return "span cube: " + a.windows.Count + " windows";
                float[] offs = { 0f, 4f, 0f, 4f };
                for (int i = 0; i < 4; i++)
                {
                    var w = a.windows[i];
                    if (w.carried || w.island != Grid(i) || w.start != 4f * i || w.offset != offs[i] || w.through != (i > 0)) return "window " + i + ": grid " + SM.Islands.IndexOf(w.island) + " start " + w.start + " off " + w.offset + " through " + w.through + " carried " + w.carried;
                }
                if (a.HasCarried) return "HasCarried";
                if (a.TileAt(2, 0) != Grid(2).GetTile(AX[0], AZ[0]) || a.TileAt(3, 5) != Grid(3).GetTile(AX[5], AZ[5])) return "the repeat's tiles are not the grids' own";
                if (a.GridOfNode(5) != Grid(1)) return "GridOfNode(5) on " + SM.Islands.IndexOf(a.GridOfNode(5));
                int carried = 0; foreach (var w in o.windows) if (w.carried) carried++;
                if (o.windows.Count != 4 || carried != 3 || !o.HasCarried) return "span-0 cube: " + o.windows.Count + " windows, " + carried + " carried";
                return null;
            });
            var leg = Cube(IdB, 0, Pluck, 1, 0, (int[])AX.Clone(), (int[])AZ.Clone(), null);   // no lengths: the uniform-step engine (quarters, every step)
            LoadState(Song("flow legacy", FourChords(), leg));
            yield return Frames(4);
            Run(sb, "a legacy (uniform step) cube with span 1 walks on too: the through window's events are steps 4–7 = nodes 4–7 on grid 1's tiles", () =>
            {
                var c = ById(IdB);
                if (c == null) return "no cube";
                if (c.HasDurations) return "it has lengths";
                var ev = new List<AudioCube.TimelineEvent>();
                c.TimelineEvents(1, ev);
                if (ev.Count != 4) return ev.Count + " events in the through window";
                for (int i = 0; i < 4; i++)
                {
                    if (ev[i].node != 4 + i || Mathf.Abs(ev[i].start - i) > 1e-3f) return "event " + i + ": node " + ev[i].node + " at " + ev[i].start;
                    if (c.TileAt(1, ev[i].node) != Grid(1).GetTile(AX[4 + i], AZ[4 + i])) return "event " + i + " is not on grid 1's tile";
                    if (c.GridOfNode(4 + i) != Grid(1) || Mathf.Abs(c.NodeStartBeat(4 + i) - (4 + i)) > 1e-3f) return "node " + (4 + i) + " on grid " + SM.Islands.IndexOf(c.GridOfNode(4 + i)) + " at " + c.NodeStartBeat(4 + i);
                }
                return null;
            });
        }

        // ================================================================ a taller grid down the lane: cells the home grid has no tile for
        {
            var ms = FourChords();
            ms[0] = new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7 }, bars = 1, kind = 0, col = 0, repeat = 1, energy = 2 };   // a triad: 3 rows; grid 1 (Dm7) has 4
            int[] tz = { 0, 0, 0, 0, 3, 3, 3, 3 };
            LoadState(Song("flow tall", ms, Cube(IdC, 0, Pluck, 1, 0, (int[])AX.Clone(), tz, Rep(24, 8)), Cube(IdO, 0, Keys, 0, 0, (int[])OX.Clone(), new[] { 2, 2, 2, 2 }, Rep(24, 4))));
            yield return Frames(4);
            Run(sb, "a 3-row home grid before a 4-row grid: notes 4–7 saved on row 3 keep that CELL (gridZ 3) though the home grid has no such tile (their nodes are its row 2); RealTile = grid 1's row-3 tile; ToState writes the cells back", () =>
            {
                var c = ById(IdC);
                if (c == null) return "no cube";
                if (Grid(0).rows != 3 || Grid(1).rows != 4) return "rows " + Grid(0).rows + " / " + Grid(1).rows;
                if (!c.HasCells) return "no cells";
                for (int i = 4; i < 8; i++)
                {
                    if (c.nodes[i] != Grid(0).GetTile(AX[i], 2)) return "node " + i + " is not the home grid's row-2 tile";
                    if (c.gridX[i] != AX[i] || c.gridZ[i] != 3) return "node " + i + " cell " + c.gridX[i] + "," + c.gridZ[i];
                    if (c.RealTile(i) != Grid(1).GetTile(AX[i], 3)) return "node " + i + " is not realised on grid 1's row 3";
                    if (c.PathTiles()[i] != Grid(1).GetTile(AX[i], 3)) return "PathTiles[" + i + "] is not grid 1's row-3 tile";
                    if (Pitch(1, AX[i], 3, Pluck) == Pitch(1, AX[i], 2, Pluck)) return "rows 2 and 3 of grid 1 sound alike at x " + AX[i] + ": the song cannot tell them apart";
                }
                for (int i = 0; i < 4; i++) if (c.RealTile(i) != c.nodes[i] || c.PathTiles()[i] != c.nodes[i]) return "node " + i + " left its home tile";
                var st = c.ToState();
                if (st.zs[4] != 3 || st.zs[0] != 0 || st.xs[4] != AX[4]) return "ToState zs " + string.Join(",", st.zs);
                if (ById(IdO).HasCells) return "the span-0 cube has cells";
                return null;
            });
            yield return Record(0f, 8f, log);
            Run(sb, "... and the synth is handed grid 1's ROW-3 pitches for notes 4–7 on beats 4–7 (the tiles the player would have clicked there)", () =>
            {
                var want = new List<Want>();
                for (int i = 0; i < 8; i++) want.Add(W(i, i < 4 ? 0 : 1, AX[i], i < 4 ? 0 : 3, Pluck, 0.92));
                return Match(log, ById(IdC), want);
            });
            Run(sb, "cells survive path edits and a rebuild: RemoveNode(0) keeps them on their notes (the note now on beat 3 falls back to the home grid's edge, the others stay on grid 1's row 3); handing SetPathAndDurations grid 1's own tiles sets them; undo restores", () =>
            {
                var c = ById(IdC);
                if (c == null) return "no cube";
                History.Push();
                if (!c.RemoveNode(0)) return "RemoveNode refused";
                if (c.nodes.Count != 7 || !c.HasCells) return "after remove: " + c.nodes.Count + " nodes, cells " + c.HasCells;
                for (int i = 3; i < 7; i++) if (c.gridZ[i] != 3 || c.gridX[i] != AX[i + 1]) return "after remove: node " + i + " cell " + c.gridX[i] + "," + c.gridZ[i];
                if (c.GridOfNode(3) != Grid(0) || c.RealTile(3) != Grid(0).GetTile(AX[4], 2)) return "the note now on beat 3 is not on the home grid's row 2";
                if (c.RealTile(4) != Grid(1).GetTile(AX[5], 3)) return "the note on beat 4 is not on grid 1's row 3";
                var tiles = new List<TileInteraction>();
                for (int i = 0; i < 8; i++) tiles.Add(i < 4 ? Grid(0).GetTile(AX[i], 0) : Grid(1).GetTile(AX[i], 3));
                c.SetPathAndDurations(tiles, Rep(24, 8));
                if (c.nodes.Count != 8 || !c.HasCells) return "after SetPathAndDurations: " + c.nodes.Count + " nodes, cells " + c.HasCells;
                for (int i = 0; i < 8; i++) if (c.nodes[i].island != Grid(0)) return "node " + i + " is not a home tile";
                for (int i = 4; i < 8; i++) if (c.gridZ[i] != 3 || c.RealTile(i) != tiles[i]) return "node " + i + " is not realised on the tile handed in";
                var all0 = new List<TileInteraction>();
                for (int i = 0; i < 8; i++) all0.Add(Grid(0).GetTile(AX[i], i < 4 ? 0 : 2));
                c.SetPathAndDurations(all0, Rep(24, 8));
                if (c.HasCells) return "home tiles only: cells stayed";
                History.Push();
                History.Undo(); History.Undo();
                var c2 = ById(IdC);
                if (c2 == null || c2.nodes.Count != 8 || !c2.HasCells || c2.RealTile(7) != Grid(1).GetTile(AX[7], 3)) return "after undo: " + (c2 == null ? "no cube" : c2.nodes.Count + " nodes, cells " + c2.HasCells);
                return null;
            });
        }

        // ================================================================ h. keeping it
        LoadState(Song("flow A", FourChords(), CubeA(1), CubeO()));
        yield return Frames(4);
        {
            string tmp = Path.Combine(V2Checks.CapturePath, "flow_engine_tmp.json");
            Run(sb, "h. save → load keeps the span (the file carries \"span\":1; the cube comes back with its through window)", () =>
            {
                if (!SongIO.SaveTo(tmp)) return "save failed";
                var txt = File.ReadAllText(tmp);
                if (!txt.Contains("\"span\":1")) return "the file has no \"span\":1";
                ById(IdA).SetSpan(0);
                if (!SongIO.LoadFrom(tmp)) return "load failed";
                var c = ById(IdA);
                if (c == null) return "cube lost";
                if (c.span != 1 || !c.HasThrough || c.windows.Count != 2 || c.nodes.Count != 8) return "span " + c.span + " windows " + c.windows.Count + " nodes " + c.nodes.Count;
                if (ById(IdO) == null || ById(IdO).span != 0) return "the span-0 cube changed";
                return null;
            });
            Run(sb, "h. an old save without the field loads as span 0 (one plain window, every node on its home tile)", () =>
            {
                var txt = File.ReadAllText(tmp);
                var old = Regex.Replace(txt, ",\"span\":-?\\d+", "");
                if (old.Contains("\"span\"")) return "could not strip the field";
                var s = SongState.FromJson(old);
                if (s.cubes[0].span != 0) return "parsed span " + s.cubes[0].span;
                LoadState(s);
                var c = ById(IdA);
                if (c == null) return "cube lost";
                if (c.span != 0 || c.HasThrough || c.windows.Count != 1) return "span " + c.span + " windows " + c.windows.Count;
                for (int i = 0; i < c.nodes.Count; i++) if (c.RealTile(i) != c.nodes[i]) return "node " + i + " off its tile";
                return null;
            });
            try { File.Delete(tmp); } catch (Exception) { }
        }
        LoadState(Song("flow A", FourChords(), CubeA(1), CubeO()));
        yield return Frames(4);
        Run(sb, "h. undo → redo: SetSpan(2) + History.Push; undo rebuilds the cube with span 1 (two windows), redo with span 2 (three)", () =>
        {
            var c = ById(IdA);
            if (c == null) return "no cube";
            History.Push();
            int u0 = History.UndoCount;
            c.SetSpan(2);
            History.Push();
            if (History.UndoCount != u0 + 1) return "pushes " + (History.UndoCount - u0);
            History.Undo();
            var c2 = ById(IdA);
            if (c2 == null || c2.span != 1 || c2.windows.Count != 2) return "after undo: span " + (c2 == null ? -9 : c2.span) + " windows " + (c2 == null ? 0 : c2.windows.Count);
            History.Redo();
            var c3 = ById(IdA);
            if (c3 == null || c3.span != 2 || c3.windows.Count != 3) return "after redo: span " + (c3 == null ? -9 : c3.span) + " windows " + (c3 == null ? 0 : c3.windows.Count);
            History.Undo();
            return null;
        });
        yield return Frames(3);
        Run(sb, "h. copy → paste onto grid 2 keeps the span: the copy plays grid 2 then grid 3 (what chain exists there); CubeState.Clone, a twin and an octave copy keep it", () =>
        {
            var c = ById(IdA);
            if (c == null) return "no cube";
            if (c.span != 1) return "the source has span " + c.span;
            var st = c.ToState();
            if (st.span != 1 || CubeState.Clone(st).span != 1) return "ToState / Clone span " + st.span + " / " + CubeState.Clone(st).span;
            if (c.TwinState(2).span != 1) return "twin span " + c.TwinState(2).span;
            var oc = CubeOps.OctaveState(c, 1);
            if (oc == null || oc.span != 1) return "octave copy span " + (oc == null ? -9 : oc.span);
            if (!Clipboard.Copy(c)) return "copy refused";
            if (Clipboard.State.span != 1) return "the clipboard state has span " + Clipboard.State.span;
            var p = Clipboard.PasteOn(Grid(2));
            if (p == null) return "paste refused";
            if (p.span != 1) return "pasted span " + p.span;
            if (p.windows.Count != 2 || p.windows[0].island != Grid(2) || !p.windows[1].through || p.windows[1].island != Grid(3) || p.windows[1].offset != 4f) return "pasted windows " + p.windows.Count;
            if (c.span != 1 || c.windows.Count != 2) return "the source changed";
            Clipboard.Clear();
            return null;
        });
        Run(sb, "an octave copy of a span cube follows it: placed (CubeOps.OctaveCopy) it has the same through window and every node sounds exactly 12 above the source's realised tile", () =>
        {
            var c = ById(IdA);
            if (c == null) return "no cube";
            var up = CubeOps.OctaveCopy(c, 1);
            if (up == null) return "refused: " + CubeOps.LastDeny;
            if (up.span != 1 || up.windows.Count != 2 || !up.windows[1].through) return "copy: span " + up.span + ", " + up.windows.Count + " windows";
            for (int i = 0; i < 8; i++)
            {
                if (up.RealTile(i) != c.RealTile(i)) return "node " + i + " is realised on another tile";
                if (VoiceRules.NodeMidi(up, i) != VoiceRules.NodeMidi(c, i) + 12) return "node " + i + ": " + VoiceRules.NodeMidi(up, i) + " vs the source's " + VoiceRules.NodeMidi(c, i);
            }
            return null;
        });

        Run(sb, "a nudge, a harmony and a late echo of a span cube follow it: a +1 nudge on node 5 sounds grid 1's tile one scale step up (not the home tile's); the harmony cube and the late echo keep the span and get the same through window", () =>
        {
            var c = ById(IdA);
            if (c == null) return "no cube";
            if (!c.SetNudge(5, 1)) return "SetNudge refused";
            int tile = Grid(1).GetTile(AX[5], AZ[5]).midi, up = tile;
            do { up++; } while (Array.IndexOf(new[] { 0, 2, 4, 5, 7, 9, 11 }, ((up % 12) + 12) % 12) < 0);   // the next note of C major (the song's key)
            int want = VoiceRules.Fold(Instruments.SlotOf(Pluck, 0), up + SongManager.Transpose, Grid(1).register);
            if (VoiceRules.NodeMidi(c, 5) != want) return "nudged node 5 sounds " + VoiceRules.NodeMidi(c, 5) + ", want " + want + " (grid 1's tile " + tile + " + a scale step)";
            c.SetNudge(5, 0);
            var h = CubeOps.Harmonize(c, true);
            if (h == null) return "harmony refused: " + CubeOps.LastDeny;
            if (h.span != 1 || h.windows.Count != 2 || !h.windows[1].through || h.windows[1].island != Grid(1)) return "harmony: span " + h.span + ", " + h.windows.Count + " windows";
            var e = CubeOps.OctaveCopy(c, -1, 24);
            if (e == null) return "late echo refused: " + CubeOps.LastDeny;
            if (e.span != 1 || e.windows.Count != 2 || !e.windows[1].through) return "late echo: span " + e.span + ", " + e.windows.Count + " windows";
            if (Mathf.Abs(e.NodeStartBeat(4) - 4f) > 1e-3f || e.GridOfNode(4) != Grid(1)) return "late echo: its note on beat 4 (the source's note 3, a beat late) is on grid " + SM.Islands.IndexOf(e.GridOfNode(4)) + " at " + e.NodeStartBeat(4);
            return null;
        });

        // ================================================================ i. the look: beads and the line over the seam
        LoadState(Song("flow A", FourChords(), CubeA(1), CubeO()));
        yield return Frames(4);
        {
            var a = ById(IdA);
            Run(sb, "i. BeadPosition: beads 0–3 lie on grid 0's tiles, beads 4–7 on grid 1's tiles (same x, z); the path line runs through all eight in order — it crosses the seam between beads 3 and 4", () =>
            {
                if (a == null) return "no cube";
                for (int i = 0; i < 8; i++)
                {
                    var t = Grid(i < 4 ? 0 : 1).GetTile(AX[i], AZ[i]);
                    if ((a.BeadPosition(i) - t.Top).magnitude > 0.02f) return "bead " + i + " at " + a.BeadPosition(i) + ", its tile's top is " + t.Top;
                }
                if (!Over(Grid(1), a.BeadPosition(4)) || Over(Grid(0), a.BeadPosition(4))) return "bead 4 is not over grid 1";
                LineRenderer line = null;
                foreach (var lr in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
                    if (lr.positionCount == 8 && (lr.GetPosition(0) - (a.BeadPosition(0) + Vector3.up * 0.05f)).magnitude < 0.02f) { line = lr; break; }
                if (line == null) return "no path line with eight points from bead 0";
                for (int i = 0; i < 8; i++) if ((line.GetPosition(i) - (a.BeadPosition(i) + Vector3.up * 0.05f)).magnitude > 0.02f) return "line point " + i + " is not on bead " + i;
                if (!Over(Grid(0), line.GetPosition(3)) || !Over(Grid(1), line.GetPosition(4))) return "points 3 / 4 are not on grid 0 / grid 1";
                return null;
            });
            {
                // the drawn line follows a moved chain grid (the island re-places only its own cubes' lines: the span cube watches its chain)
                LineRenderer line = null;
                if (a != null)
                    foreach (var lr in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
                        if (lr.positionCount == 8 && (lr.GetPosition(0) - (a.BeadPosition(0) + Vector3.up * 0.05f)).magnitude < 0.02f) { line = lr; break; }
                Vector3 l3 = line != null ? line.GetPosition(3) : Vector3.zero, l4 = line != null ? line.GetPosition(4) : Vector3.zero;
                var g1 = Grid(1); Vector3 was = g1.transform.position;
                g1.transform.position = was + new Vector3(0.7f, 0f, 0f);
                yield return Frames(3);
                Vector3 m3 = line != null ? line.GetPosition(3) : Vector3.zero, m4 = line != null ? line.GetPosition(4) : Vector3.zero;
                g1.transform.position = was;
                yield return Frames(3);
                Vector3 r4 = line != null ? line.GetPosition(4) : Vector3.zero;
                Run(sb, "i. the drawn line follows its chain: grid 1 moved 0.7 in x moves the line's point 4 with it within three frames (point 3 on grid 0 stays), and back", () =>
                {
                    if (line == null) return "no path line";
                    if ((m3 - l3).magnitude > 1e-3f) return "point 3 moved";
                    if (Mathf.Abs((m4.x - l4.x) - 0.7f) > 0.02f) return "point 4 moved " + (m4.x - l4.x) + " in x, want 0.7";
                    if ((r4 - l4).magnitude > 0.02f) return "point 4 did not come back";
                    return null;
                });
            }
            if (captures && OrbitCamera.I != null)
            {
                var b = Grid(0).WorldBounds; b.Encapsulate(Grid(1).WorldBounds); b.Expand(new Vector3(1f, 0f, 1f));
                OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.95f);
                yield return Wait(0.9f);
                yield return Shot("flow_engine_seam.png");
            }
        }

        // ================================================================ j. done
        Run(sb, "j. no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3)));
        Run(sb, "j. Synth late / errors unchanged (late notes during the capture taken while playing counted apart)", () =>
        {
            int late = Synth.LateEvents - late0 - lateExcused, err = Synth.Errors - err0;
            return late == 0 && err == 0 ? null : "late +" + late + " (+" + lateExcused + " apart), errors +" + err + " — last late: " + Synth.LastLate;
        });
        Info(sb, "synth: " + Synth.Stats());
        Application.logMessageReceived -= OnLog;
        try { GlobalClock.Stop(); SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); History.Reset(); History.Push(); } catch (Exception e) { Info(sb, "fixture reload: " + e.Message); }
        Restore();
        V3Fixes.RestoreSaves(saves);
        int pass = sb.ToString().Split('\n').Count(l => l.StartsWith("PASS")), fail = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append("DONE ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }
}
