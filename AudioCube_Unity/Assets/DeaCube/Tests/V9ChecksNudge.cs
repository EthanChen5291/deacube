using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v9 (N): the per-step NUDGE (DeaCube/Nudge.cs) Play-mode verification: <c>V9ChecksNudge.RunAll(captures)</c> starts a coroutine; poll
/// <c>V9ChecksNudge.Done</c> or Captures/nudge9_report.txt (numbered PASS / FAIL lines, INFO lines). A test song in A minor (Am7 grid, a 25-key
/// keyboard, a Dm7 grid as a paste target; 120 bpm) with a pluck cube of four beats on the Am7 grid (nudges 0 / +1 / −1 / +2) and one on the
/// keyboard (+1 / −1 / 0 / +2), then the same in F major (G +2 → B♭, not B: a key that differs from A minor where the test looks). Covers: Nudge.Semis against an independent scale walk (both keys, ±2, an off-scale
/// tile), the engine's node pitch (VoiceRules.NodeMidi) and the notes the synth is handed while playing (VoiceRules.Tap) on a chord grid and a
/// keyboard, a nudged note stamped bent, save → load, an old save without the field, copy / paste, octave copy, flip, twins, undo, the
/// ▲ / ▼ mark on the inspector grid's bead (and a capture), the inspector's "higher" / "lower" buttons (one History entry per effective click,
/// the ±2 limit refused), the menu card preview (MenuPreview) playing the nudged notes, no console errors. Captures nudge9_*.png.
/// Never writes the user's save (saves snapshotted and restored); reloads the fixture at the end.
/// </summary>
public static class V9ChecksNudge
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "nudge9_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" N9-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
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
    static string F(double v) => v.ToString("F3");
    static SongManager SM => SongManager.I;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ an independent scale walk (the check's own, not Nudge.Semis)
    static readonly int[] MajorPcs = { 0, 2, 4, 5, 7, 9, 11 }, MinorPcs = { 0, 2, 3, 5, 7, 8, 10 };
    static bool InKey(int midi, int tonic, bool minor) { int rel = (((midi - tonic) % 12) + 12) % 12; foreach (int s in minor ? MinorPcs : MajorPcs) if (s == rel) return true; return false; }
    /// <summary>The scale note <paramref name="steps"/> steps above (below) an in-key <paramref name="midi"/>.</summary>
    static int ScaleStep(int midi, int steps, int tonic, bool minor)
    {
        int m = midi, dir = steps > 0 ? 1 : -1;
        for (int k = 0; k < Math.Abs(steps); k++) { do { m += dir; } while (!InKey(m, tonic, minor)); }
        return m;
    }

    // ------------------------------------------------------------------ the test song
    const int Inst = 1;   // pluck: no pad tones, no bass anchor, not sustaining
    const int IdGrid = 9301, IdKeys = 9302;
    static readonly int[] GridNudges = { 0, 1, -1, 2 }, KeyNudges = { 1, -1, 0, 2 };
    static readonly int[] KeyXs = { 0, 2, 4, 7 };   // C D E G on a C keyboard: in A minor and C major alike

    static MeasureState Chord(int col, string name, int root, int[] semis) => new MeasureState { chordKey = name, root = root, semis = semis, bars = 1, kind = 0, col = col, repeat = 1, energy = 2 };
    static MeasureState Keys(int col) => new MeasureState { chordKey = "Keys", root = 60, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, keyCount = 25 };
    static CubeState Cube(int measure, int id, int[] xs, int[] zs, int[] nudges) => new CubeState
    {
        instrument = Inst, measure = measure, xs = xs, zs = zs, rests = new bool[xs.Length], mods = new int[xs.Length], durs = new[] { 24, 24, 24, 24 },
        nudges = nudges, volume = 1f, moon = -1, twinOf = -1, echoOf = -1, id = id, step = 1, gate = 1, mode = 0, hits = -1
    };

    /// <summary>Am7 grid (column 0), a keyboard (column 1), Dm7 grid (column 2); the key A minor or F major (its tiles A C E G and C D E G sit in both).</summary>
    public static SongState TestSong(bool minor)
    {
        var m = new List<MeasureState> { Chord(0, "Am7", 57, new[] { 0, 3, 7, 10 }), Keys(1), Chord(2, "Dm7", 62, new[] { 0, 3, 7, 10 }) };
        var cubes = new List<CubeState>
        {
            Cube(0, IdGrid, new[] { 0, 1, 2, 3 }, new[] { 0, 0, 0, 0 }, (int[])GridNudges.Clone()),
            Cube(1, IdKeys, (int[])KeyXs.Clone(), new[] { 0, 0, 0, 0 }, (int[])KeyNudges.Clone())
        };
        return new SongState
        {
            version = SongState.CurrentVersion, name = minor ? "nudge9 minor" : "nudge9 major", bpm = 120f, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes.ToArray(),
            moons = new MeasureState[0], keyTonic = minor ? 9 : 5, keyMinor = minor, tone = 1f, space = 1f
        };
    }

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
    }

    static AudioCube ById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }
    static KeyBlock IslandAt(int i) => SM != null && i >= 0 && i < SM.Islands.Count ? SM.Islands[i] : null;

    /// <summary>The pitch the engine should play for node <paramref name="i"/> of <paramref name="c"/>: the tile's scale step in the key, then the fold
    /// VoiceRules gives that island (a keyboard keeps its octave).</summary>
    static int Expected(AudioCube c, int i, int[] nudges, int tonic, bool minor)
    {
        var t = c.nodes[i];
        int slot = Instruments.SlotOf(c.instrument, c.voice);
        int b = ScaleStep(t.midi, nudges[i], tonic, minor) + SongManager.Transpose + 12 * c.octave;
        return t.island != null && t.island.IsKeyboard ? VoiceRules.FoldMelody(slot, b, t.island.register) : VoiceRules.Fold(slot, b, t.island != null ? t.island.register : 0);
    }

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
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

    /// <summary>Plays the song from <paramref name="a"/> to <paramref name="b"/> (beats) and collects every event the cubes hand the synth.</summary>
    static IEnumerator Record(float a, float b, List<VoiceRules.NoteEvent> log)
    {
        log.Clear();
        VoiceRules.Tap = e => log.Add(e);
        GlobalClock.SetRegion(a, b);
        GlobalClock.Seek(a); GlobalClock.Play(); AudioCube.ScheduleAllNow();
        yield return Wait((float)((b - a) / GlobalClock.BeatsPerSecond) + 0.35f);
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        VoiceRules.Tap = null;
        yield return Frames(2);
    }

    static string CheckPlayed(List<VoiceRules.NoteEvent> log, AudioCube c, int[] nudges, int tonic, bool minor, bool wantBent)
    {
        var mine = log.Where(e => e.owner == c.owner).OrderBy(e => e.onDsp).ToList();
        if (mine.Count < 4) return "the synth got " + mine.Count + " notes of the cube (need 4)" + (log.Count == 0 ? " — no events at all: the audio clock may be frozen (environmental)" : "");
        var got = new List<int>(); var want = new List<int>();
        // one event per beat: group by onset (a note is one event; costumes would add more at the same onset)
        var byOn = mine.GroupBy(e => Math.Round(e.onDsp * 50.0)).OrderBy(g => g.Key).Take(4).ToList();
        for (int i = 0; i < 4; i++)
        {
            var g = byOn[i].ToList();
            if (g.Count != 1) return "step " + i + " sent " + g.Count + " events at once (" + string.Join(",", g.Select(e => e.midi)) + ")";
            got.Add(g[0].midi); want.Add(Expected(c, i, nudges, tonic, minor));
            if (wantBent && nudges[i] != 0 && !g[0].bent) return "step " + i + " (nudge " + nudges[i] + ") not stamped bent";
            if (nudges[i] == 0 && g[0].bent) return "step " + i + " (no nudge) stamped bent";
        }
        for (int i = 0; i < 4; i++) if (got[i] != want[i]) return "played " + string.Join(" ", got) + " want " + string.Join(" ", want) + " (tiles " + string.Join(" ", c.nodes.Select(t => t.midi)) + ")";
        return null;
    }

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("V9ChecksNudge ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        var saves = V3Fixes.SnapshotSaves();
        Prepare();

        // ---------------------------------------------------------------- the rule
        Run(sb, "Nudge.Semis in C major: E +1 → F, E −1 → D, E +2 → G, B +1 → C, C −1 → B; A minor: B −1 → A, G +1 → A, E +2 → G, A −2 → F", () =>
        {
            var cases = new[] { new[] { 64, 1, 0, 0, 1 }, new[] { 64, -1, 0, 0, -2 }, new[] { 64, 2, 0, 0, 3 }, new[] { 71, 1, 0, 0, 1 }, new[] { 60, -1, 0, 0, -1 },
                                new[] { 71, -1, 9, 1, -2 }, new[] { 67, 1, 9, 1, 2 }, new[] { 64, 2, 9, 1, 3 }, new[] { 69, -2, 9, 1, -4 } };
            foreach (var k in cases)
            {
                int got = Nudge.Semis(k[0], k[1], k[2], k[3] == 1);
                int want = ScaleStep(k[0], k[1], k[2], k[3] == 1) - k[0];
                if (got != k[4] || got != want) return "tile " + k[0] + " steps " + k[1] + " key " + k[2] + (k[3] == 1 ? "m" : "") + ": " + got + " want " + k[4];
            }
            if (Nudge.Semis(64, 0, 0, false) != 0) return "0 steps moved the note";
            if (Nudge.Semis(61, 1, 0, false) != 2) return "off-scale C# +1 in C major: " + Nudge.Semis(61, 1, 0, false) + " want +2 (D#: walks from C, keeps its sharp)";
            if (Nudge.Clamp(5) != 2 || Nudge.Clamp(-7) != -2) return "clamp";
            return null;
        });

        // ---------------------------------------------------------------- the engine, in A minor then C major
        foreach (bool minor in new[] { true, false })
        {
            string key = minor ? "A minor" : "F major";
            int tonic = minor ? 9 : 5;
            LoadState(TestSong(minor));
            yield return Frames(4);
            var cg = ById(IdGrid); var ck = ById(IdKeys);
            Run(sb, key + ": the song loads with its key and both cubes carry their nudges", () =>
            {
                if (SM == null || !SM.HasSongKey || SM.SongKey.tonic != tonic || SM.SongKey.minor != minor) return "song key " + (SM != null && SM.HasSongKey ? SM.SongKey.ToString() : "none");
                if (cg == null || ck == null) return "cubes missing";
                for (int i = 0; i < 4; i++) if (cg.NudgeAt(i) != GridNudges[i] || ck.NudgeAt(i) != KeyNudges[i]) return "node " + i + ": grid " + cg.NudgeAt(i) + " keys " + ck.NudgeAt(i);
                if (!cg.HasNudges || !ck.HasNudges) return "HasNudges false";
                if (!ck.Island.IsKeyboard) return "the keys cube is not on a keyboard";
                return null;
            });
            Run(sb, key + ": VoiceRules.NodeMidi of every node = the tile's scale step, folded (chord grid and keyboard; 0 = the tile)", () =>
            {
                for (int i = 0; i < 4; i++)
                {
                    int g = VoiceRules.NodeMidi(cg, i), w = Expected(cg, i, GridNudges, tonic, minor);
                    if (g != w) return "grid node " + i + " (tile " + cg.nodes[i].midi + ", nudge " + GridNudges[i] + "): " + g + " want " + w;
                    int gk = VoiceRules.NodeMidi(ck, i), wk = Expected(ck, i, KeyNudges, tonic, minor);
                    if (gk != wk) return "keys node " + i + " (key " + ck.nodes[i].midi + ", nudge " + KeyNudges[i] + "): " + gk + " want " + wk;
                }
                if (VoiceRules.NodeMidi(cg, 0) != VoiceRules.Fold(Instruments.SlotOf(Inst, 0), cg.nodes[0].midi, cg.Island.register)) return "node 0 (no nudge) is not the tile";
                return null;
            });
            Info(sb, key + ": grid tiles " + string.Join(" ", cg.nodes.Select(t => t.midi)) + " → " + string.Join(" ", Enumerable.Range(0, 4).Select(i => VoiceRules.NodeMidi(cg, i)))
                 + "; keys " + string.Join(" ", ck.nodes.Select(t => t.midi)) + " → " + string.Join(" ", Enumerable.Range(0, 4).Select(i => VoiceRules.NodeMidi(ck, i))));
            var log = new List<VoiceRules.NoteEvent>();
            yield return Record(SM.ColumnStart(0), SM.ColumnStart(0) + SM.ColumnLength(0), log);
            string r1 = CheckPlayed(log, cg, GridNudges, tonic, minor, true);
            Run(sb, key + ": playing column 0, the synth is handed the nudged notes on the chord grid (+1 / −1 / +2 scale steps; 0 the tile; nudged = bent, alone)" + (minor ? "" : " — G +2 = B♭ here, B in A minor"), () => r1);
            yield return Record(SM.ColumnStart(1), SM.ColumnStart(1) + SM.ColumnLength(1), log);
            string r2 = CheckPlayed(log, ck, KeyNudges, tonic, minor, true);
            Run(sb, key + ": playing column 1, the keyboard cube plays its keys' scale neighbours (FoldMelody keeps the octave)", () => r2);
            // the menu card preview
            var st = SongState.Capture();
            Run(sb, key + ": MenuPreview.Build plays the same nudged notes in the first two bars", () =>
            {
                var tr = MenuPreview.Build(st, 0, 2, 2, 30f);
                if (tr == null) return "no track";
                int slot = Instruments.SlotOf(Inst, 0);
                for (int i = 0; i < 4; i++)
                {
                    int wg = Expected(cg, i, GridNudges, tonic, minor), wk = Expected(ck, i, KeyNudges, tonic, minor);
                    bool fg = tr.notes.Any(n => n.slot == slot && Math.Abs(n.on - i) < 0.02 && n.midi == wg);
                    bool fk = tr.notes.Any(n => n.slot == slot && Math.Abs(n.on - (4 + i)) < 0.02 && n.midi == wk);
                    if (!fg) return "grid step " + i + ": no note " + wg + " at beat " + i + " (has " + string.Join(",", tr.notes.Where(n => Math.Abs(n.on - i) < 0.02).Select(n => n.midi)) + ")";
                    if (!fk) return "keys step " + i + ": no note " + wk + " at beat " + (4 + i) + " (has " + string.Join(",", tr.notes.Where(n => Math.Abs(n.on - (4 + i)) < 0.02).Select(n => n.midi)) + ")";
                }
                return null;
            });
        }

        // ---------------------------------------------------------------- keeping it: save / load, an old save, copy / paste, copies, flip, twins, undo
        LoadState(TestSong(true));
        yield return Frames(3);
        {
            var cg = ById(IdGrid);
            string tmp = Path.Combine(V2Checks.CapturePath, "nudge9_tmp.json");
            Run(sb, "save → load keeps the nudges (and nothing else of the cube changes)", () =>
            {
                if (!SongIO.SaveTo(tmp)) return "save failed";
                var txt = File.ReadAllText(tmp);
                if (!txt.Contains("\"nudges\":[0,1,-1,2]")) return "the file has no nudges array: " + (txt.Length > 300 ? txt.Substring(0, 300) : txt);
                if (!SongIO.LoadFrom(tmp)) return "load failed";
                var c = ById(IdGrid); var k = ById(IdKeys);
                if (c == null || k == null) return "cubes lost";
                for (int i = 0; i < 4; i++) if (c.NudgeAt(i) != GridNudges[i] || k.NudgeAt(i) != KeyNudges[i]) return "node " + i + ": " + c.NudgeAt(i) + " / " + k.NudgeAt(i);
                if (c.nodes.Count != 4 || !c.HasDurations) return "the path changed";
                return null;
            });
            Run(sb, "an old save without the field loads with no nudges (every node plays its tile)", () =>
            {
                var txt = File.ReadAllText(tmp);
                var old = Regex.Replace(txt, ",\"nudges\":\\[[^\\]]*\\]", "");
                if (old.Contains("nudges")) return "could not strip the field";
                var s = SongState.FromJson(old);
                if (s.cubes[0].nudges != null && s.cubes[0].nudges.Length > 0) return "parsed nudges " + s.cubes[0].nudges.Length;
                LoadState(s);
                var c = ById(IdGrid);
                if (c == null) return "cube lost";
                for (int i = 0; i < 4; i++) if (c.NudgeAt(i) != 0) return "node " + i + " nudged " + c.NudgeAt(i);
                if (c.HasNudges || c.nudges.Count != 0) return "a nudge list appeared";
                if (VoiceRules.NodeMidi(c, 1) != VoiceRules.Fold(Instruments.SlotOf(Inst, 0), c.nodes[1].midi, c.Island.register)) return "node 1 is not its tile";
                return null;
            });
            try { File.Delete(tmp); } catch (Exception) { }
            LoadState(TestSong(true));
        }
        yield return Frames(3);
        {
            var cg = ById(IdGrid);
            Run(sb, "copy / paste onto another grid keeps the nudges by node (Clipboard.PasteOn); the source keeps its own", () =>
            {
                var target = IslandAt(2);
                if (cg == null || target == null) return "no cube / target";
                if (!Clipboard.Copy(cg)) return "copy refused";
                if (Clipboard.State.nudges == null || Clipboard.State.nudges.Length != 4) return "the clipboard state has no nudges";
                var p = Clipboard.PasteOn(target);
                if (p == null) return "paste refused";
                for (int i = 0; i < 4; i++) if (p.NudgeAt(i) != GridNudges[i]) return "pasted node " + i + ": " + p.NudgeAt(i);
                for (int i = 0; i < 4; i++) if (cg.NudgeAt(i) != GridNudges[i]) return "source node " + i + " changed: " + cg.NudgeAt(i);
                Clipboard.Clear();
                return null;
            });
            Run(sb, "an octave copy keeps the nudges; a late echo shifts them with their notes (the leading rest has none); twins keep them", () =>
            {
                var c = ById(IdGrid);
                if (c == null) return "no cube";
                var up = CubeOps.OctaveCopy(c, 1);
                if (up == null) return "octave copy refused: " + CubeOps.LastDeny;
                for (int i = 0; i < 4; i++) if (up.NudgeAt(i) != GridNudges[i]) return "copy node " + i + ": " + up.NudgeAt(i);
                var late = CubeOps.OctaveState(c, -1, 24);
                if (late == null || late.nudges == null || late.nudges.Length != late.xs.Length) return "late echo state has no nudges";
                // a leading rest of a beat, then the notes 0..2 (the last beat dropped): rest 0, then 0 / +1 / −1
                if (late.nudges[0] != 0 || late.nudges[1] != 0 || late.nudges[2] != 1 || late.nudges[3] != -1) return "late nudges " + string.Join(" ", late.nudges);
                var tw = c.TwinState(0);
                if (tw.nudges == null || tw.nudges.Length != 4 || tw.nudges[3] != 2) return "twin nudges " + (tw.nudges == null ? "null" : string.Join(" ", tw.nudges));
                return null;
            });
            Run(sb, "flip mirrors the nudges (+1 ↔ −1); reverse keeps each nudge on its note", () =>
            {
                var c = ById(IdGrid);
                if (c == null) return "no cube";
                CubeOps.Flip(c);
                for (int i = 0; i < 4; i++) if (c.NudgeAt(i) != -GridNudges[i]) return "flipped node " + i + ": " + c.NudgeAt(i);
                CubeOps.Flip(c);
                c.ReversePath();
                for (int i = 0; i < 4; i++) if (c.NudgeAt(i) != GridNudges[3 - i]) return "reversed node " + i + ": " + c.NudgeAt(i);
                c.ReversePath();
                return null;
            });
            Run(sb, "undo removes a nudge set with SetNudge + History.Push (the cube is rebuilt with its old value); redo brings it back", () =>
            {
                var c = ById(IdGrid);
                if (c == null) return "no cube";
                History.Push();
                int u0 = History.UndoCount;
                if (!c.SetNudge(0, 2)) return "SetNudge refused";
                History.Push();
                if (History.UndoCount != u0 + 1) return "pushes " + (History.UndoCount - u0);
                History.Undo();
                var c2 = ById(IdGrid);
                if (c2 == null) return "cube lost after undo";
                if (c2.NudgeAt(0) != 0) return "after undo node 0 = " + c2.NudgeAt(0);
                History.Redo();
                var c3 = ById(IdGrid);
                if (c3 == null || c3.NudgeAt(0) != 2) return "after redo node 0 = " + (c3 == null ? -9 : c3.NudgeAt(0));
                return null;
            });
        }

        // ---------------------------------------------------------------- the look: the inspector grid's bead marks, the world beads
        LoadState(TestSong(true));
        yield return Frames(3);
        {
            var cg = ById(IdGrid);
            if (captures && cg != null && OrbitCamera.I != null)
            {
                var b = cg.Island.WorldBounds; b.Expand(new Vector3(1.5f, 0f, 1.5f));
                OrbitCamera.I.FrameBounds(b, 0.1f, true, 0.9f);
                yield return Wait(0.9f);
                yield return Shot("nudge9_world.png");
            }
            CubeInspector.Open(cg);
            yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open, 5f);
            yield return Frames(3);
            Run(sb, "inspector: the grid shows a ▲ / ▼ mark beside every nudged bead (3 of 4 nodes), none on the plain one; the marks are lazy (built for the 3 nudged beads only)", () =>
            {
                var grid = InspectorCard.Grid;
                if (grid == null) return "no grid";
                int on = 0, all = 0;
                foreach (var im in grid.GetComponentsInChildren<Image>(true)) if (im.name.StartsWith("Nudge")) { all++; if (im.gameObject.activeInHierarchy && im.sprite != null) on++; }
                if (all != 3) return "nudge marks built " + all + " (lazy: one per nudged bead, never one per bead)";
                if (on != 3) return "marks shown " + on;
                return null;
            });
            if (captures) yield return Shot("nudge9_card.png");
            // the buttons
            var strip = InspectorCard.Strip;
            if (strip != null) strip.SelectNode(1);
            yield return Frames(3);
            Run(sb, "inspector: a selected note shows the higher ▲ / lower ▼ buttons; nothing selected hides them", () =>
            {
                if (!InspectorCard.NoteSelected) return "no note selected";
                if (!InspectorCard.NudgeShown) return "buttons hidden with a note selected";
                if (InspectorCard.NudgeButton(1) == null || InspectorCard.NudgeButton(-1) == null) return "buttons missing";
                return null;
            });
            if (captures) yield return Shot("nudge9_inspector.png");
            Run(sb, "inspector: higher / lower nudge the selected step one scale step a click (+1 → +2 → the limit refused → +1 → 0 → −1), one History entry per effective click, the step auditions", () =>
            {
                var c = InspectorCard.Cube; var up = InspectorCard.NudgeButton(1); var dn = InspectorCard.NudgeButton(-1);
                if (c == null || up == null || dn == null) return "no cube / buttons";
                int node = 1;
                if (c.NudgeAt(node) != 1) return "node 1 starts at " + c.NudgeAt(node);
                History.Push();
                int u0 = History.UndoCount, k0 = InspectorCard.NudgeClicks, a0 = InkUI.AuditionCount;
                up.Click();
                if (c.NudgeAt(node) != 2) return "after higher: " + c.NudgeAt(node);
                if (History.UndoCount != u0 + 1) return "pushes after 1 click: " + (History.UndoCount - u0);
                if (InkUI.AuditionCount != a0 + 1) return "auditions " + (InkUI.AuditionCount - a0);
                up.Click();   // the limit
                if (c.NudgeAt(node) != 2 || History.UndoCount != u0 + 1) return "the limit was not refused: " + c.NudgeAt(node) + " pushes " + (History.UndoCount - u0);
                dn.Click(); dn.Click(); dn.Click();
                if (c.NudgeAt(node) != -1) return "after 3 × lower: " + c.NudgeAt(node);
                if (History.UndoCount != u0 + 4) return "pushes after 4 effective clicks: " + (History.UndoCount - u0);
                if (InspectorCard.NudgeClicks != k0 + 4) return "clicks " + (InspectorCard.NudgeClicks - k0);
                if (VoiceRules.NodeMidi(c, node) != Expected(c, node, new[] { 0, -1, 0, 0 }, 9, true)) return "the engine does not play the new nudge";
                History.Undo();
                var c2 = ById(IdGrid);
                if (c2 == null || c2.NudgeAt(node) != 0) return "undo: node 1 = " + (c2 == null ? -9 : c2.NudgeAt(node)) + " want 0";
                return null;
            });
            yield return Frames(2);
            CubeInspector.CloseImmediate();
            yield return Frames(2);
        }

        // ---------------------------------------------------------------- done
        Run(sb, "no console errors during the suite", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3)));
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
