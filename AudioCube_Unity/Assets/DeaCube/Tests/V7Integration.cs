using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// v7 end-to-end run (integration, SPEC v7 §18 + §19): a fresh song → its SECTIONS (touching grids inside, a gap between, letters, measure
/// numbers, a name) → SIZE FIRST drawing on a grid while the song plays (quiet: the song pauses, no loop; Space hears it) up to its CAPACITY (the
/// refusal or the auto-expand, then EXPAND) → octave copies ↑ / ↓ (layers, ±12, phase-in), a 4th / 5th HARMONY, FLIP → paste as ECHO ↑ / late
/// echo ↓ / ANSWER / STEP → a melody PHRASE drawn cell by cell that GROWS from 1 to 2 measures (one grid over two chords) → STAIRS: a spark fall
/// (the runner, the next column's ground lower) and a climb (the ground after it higher) → REPEAT ×3 with REWIND + VARY → FLOW through the section
/// → LAUNCH into the next section (the fling, the riser + crash) → Shift-select a section: octave ↑, copy, paste after, delete → HOLD THEN RETURN
/// across the loop (cubes hold where they ended, towers +60 % stay up then sink out of sync, belts return at the loop) → the shortcut sheet →
/// present → save / load round trip of every v7 field. Synth late / errors, console errors, the user's save. Captures i7_*.png.
/// Poll Done / Report (Captures/v7_integration_report.txt). Restores the saves and the tutorial prefs; never writes the user's save.
/// </summary>
public static class V7Integration
{
    public static bool Done = true;
    public static string Report = "";
    static StringBuilder sb;
    static int errors, pass, fail;
    static readonly List<string> errLines = new List<string>();
    static Dictionary<string, V3Fixes.FileSnap> saves;
    static readonly StringBuilder lateLog = new StringBuilder();
    static int lateMark, excused, stalls, stallLate;
    static bool catching;              // a capture / seek catch-up is being counted apart (the stall watcher leaves those frames to it)
    static string curSection = "start";
    static bool shots;
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);
    static SongManager SM => SongManager.I;
    static PathManager PM => PathManager.I;

    public static string Run(bool captures = true)
    {
        if (!Done) return "already running";
        if (SequenceMaster.I == null || SongManager.I == null) return "no SequenceMaster";
        Done = false; Report = ""; shots = captures;
        SequenceMaster.I.StartCoroutine(Guarded());
        SequenceMaster.I.StartCoroutine(StallWatch());
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
        try { File.WriteAllText(Cap("v7_integration_report.txt"), Report + "...running\n"); } catch (Exception) { }
    }
    static void Info(string what) { sb.Append("INFO ").Append(what).Append('\n'); Report = sb.ToString(); }
    /// <summary>Logs the late notes since the last mark (not counted apart) under <paramref name="where"/>.</summary>
    static void Note(string where) { int l = Synth.LateEvents; if (l != lateMark) lateLog.Append(where).Append(" +").Append(l - lateMark).Append(" (last: ").Append(Synth.LastLate).Append(") "); lateMark = l; }
    static void Mark(string section) { Note(section); curSection = "after " + section; }
    /// <summary>The protocol's rule for main-thread stalls (a capture, another package's import during Play, a GC): late notes in the frames right
    /// after a frame longer than 0.12 s are counted apart (and reported); captures / seeks count their own.</summary>
    static IEnumerator StallWatch()
    {
        int prev = Synth.LateEvents, after = 0;
        while (!Done)
        {
            yield return null;
            if (!catching && Time.unscaledDeltaTime > 0.12f) { after = 3; stalls++; }
            int now = Synth.LateEvents;
            if (after > 0 && !catching) { if (now > prev) { excused += now - prev; stallLate += now - prev; lateMark = now; } after--; }
            prev = now;
        }
    }
    static string F(float v, string f = "F2") => v.ToString(f);

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Until(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
    }
    static IEnumerator Shot(string file)
    {
        if (!shots) yield break;
        Note(curSection);
        catching = true;
        int l = Synth.LateEvents;
        ScreenCapture.CaptureScreenshot(Cap(file), 1);
        yield return null; yield return null;
        excused += Synth.LateEvents - l;   // a capture mid-play stalls the main thread: late notes around it are counted apart
        lateMark = Synth.LateEvents;
        catching = false;
    }
    /// <summary>Seek (and play): the step under the playhead fires at once (≤ 30 ms late by design) — counted apart.</summary>
    static IEnumerator SeekPlay(double beat)
    {
        Note(curSection);
        catching = true;
        int l = Synth.LateEvents;
        GlobalClock.Seek(beat);
        if (!GlobalClock.IsPlaying) GlobalClock.Play();
        for (int i = 0; i < 4; i++) yield return null;
        excused += Synth.LateEvents - l; lateMark = Synth.LateEvents;
        catching = false;
    }
    static string Md5(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    // ---- camera / pointer
    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static void Frame(Vector3 focus, float dist, float pitch)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, 0f, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }
    static bool FrameTiles(IList<TileInteraction> tiles, float pitch = 60f)
    {
        float[] dists = { 9f, 11f, 13f, 15f, 18f, 22f, 26f, 32f };
        float[] pitches = { pitch, 70f, 78f, 84f };
        Vector3 c = Vector3.zero; int n = 0;
        foreach (var t in tiles) if (t != null) { c += t.Top; n++; }
        if (n == 0) return false;
        c /= n;
        foreach (float p in pitches)
            foreach (float d in dists)
            {
                Frame(c, d, p);
                Physics.SyncTransforms();
                bool ok = true;
                foreach (var t in tiles) { if (t == null) { ok = false; break; } var r = PM.PickAt(Scr(t.Top)); if (r.tile != t) { ok = false; break; } }
                if (ok) return true;
            }
        return false;
    }
    static void Release() { if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false; }
    /// <summary>§21: the grid a cube belongs to — its long grid's members (SongManager.LongGridOf), else its own island (a Moon for a Moon cube).</summary>
    static List<KeyBlock> GridOf(AudioCube c)
    {
        var home = c != null ? c.Island : null;
        if (home == null) return null;
        var lg = SM.LongGridOf(home);
        return lg != null && lg.Count > 0 ? lg : new List<KeyBlock> { home };
    }
    /// <summary>§21: the index of the member of <paramref name="grids"/> under the cube's body (x inside, then within 0.35; z within 0.35 —
    /// 2.6 for an octave layer: the deep layer stands in the reflection band in front of the platform); −1 = off its grid.</summary>
    static int MemberUnder(AudioCube c, IList<KeyBlock> grids)
    {
        if (c == null || grids == null) return -1;
        Vector3 q = c.transform.position;
        float zm = c.layer != 0 ? 2.6f : 0.35f;
        for (int pass = 0; pass < 2; pass++)
        {
            float xm = pass == 0 ? 0f : 0.35f;
            for (int i = 0; i < grids.Count; i++)
            {
                var kb = grids[i]; if (kb == null) continue;
                var vb = kb.VisualBounds;
                if (q.x >= vb.min.x - xm && q.x <= vb.max.x + xm && q.z >= vb.min.z - zm && q.z <= vb.max.z + zm) return i;
            }
        }
        return -1;
    }
    /// <summary>§21: the first finalized cube standing off its grid this frame (null = every cube is on its grid).</summary>
    static string StrayCube()
    {
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.nodes.Count == 0 || c.Island == null) continue;
            if (MemberUnder(c, GridOf(c)) < 0)
                return "cube " + c.id + " (" + c.Island.name + ", layer " + c.layer + ") at " + c.transform.position.ToString("F1") + " vs " + c.Island.VisualBounds.center.ToString("F1");
        }
        return null;
    }
    /// <summary>Frames the whole song at once (OrbitCamera.FrameAll is the O-key toggle: calling it twice leaves the overview again).</summary>
    static void FrameSong() { Release(); if (OrbitCamera.I != null && SM != null && SM.HasSong) OrbitCamera.I.FrameBounds(SM.FrameSongBounds, 0.1f, true); }
    /// <summary>Frames columns <paramref name="c0"/>..<paramref name="c1"/> at once.</summary>
    static void FrameColumns(int c0, int c1)
    {
        Release();
        if (OrbitCamera.I == null || SM == null || !SM.HasSong) return;
        c0 = Mathf.Clamp(c0, 0, SM.ColumnCount - 1); c1 = Mathf.Clamp(c1, c0, SM.ColumnCount - 1);
        var b = SM.ColumnBounds(c0);
        for (int c = c0 + 1; c <= c1; c++) b.Encapsulate(SM.ColumnBounds(c));
        OrbitCamera.I.FrameBounds(b, 0.1f, true);
    }
    static IEnumerator Click(TileInteraction t, bool shift = false)
    {
        Vector3 s = Scr(t.Top);
        PathManager.SimOnly = true; PathManager.SimPos = s;
        PM.SimPointer(s, true, true, false, false, false, false, shift);
        yield return null;
        PM.SimPointer(s, false, false, true, false, false, false, shift);
        yield return null;
    }

    /// <summary>A click on a keyboard key at a point that picks it (toward its front: the black keys cover a white key's back half).</summary>
    static IEnumerator ClickKey(TileInteraction t)
    {
        var cam = Camera.main; Vector3 sp = Scr(t.Top);
        foreach (float f in new[] { 0f, 0.3f, 0.6f, 0.9f })
        {
            var q = cam.WorldToScreenPoint(t.Top - t.island.transform.forward * f);
            if (q.z > 0f && PM.PickAt(q).tile == t) { sp = q; break; }
        }
        PathManager.SimOnly = true; PathManager.SimPos = sp;
        PM.SimPointer(sp, true, true, false);
        yield return null;
        PM.SimPointer(sp, false, false, true);
        yield return null;
    }

    static AudioCube NewestOn(KeyBlock kb)
    {
        AudioCube best = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0 && c.Island == kb && (best == null || c.id > best.id)) best = c;
        return best;
    }
    static AudioCube CubeById(int id) { foreach (var c in SequenceMaster.Cubes) if (c != null && c.id == id) return c; return null; }
    static KeyBlock KindIn(int col, int kind) { foreach (var kb in SM.Islands) if (kb != null && kb.column == col && kb.kind == kind) return kb; return null; }
    static int Idx(KeyBlock kb) => kb != null ? SM.Islands.IndexOf(kb) : -1;
    static string Ints(IEnumerable<int> xs) => string.Join(",", xs.Select(x => x.ToString()).ToArray());
    static List<int> Sounding(AudioCube c)
    {
        var r = new List<int>();
        if (c == null) return r;
        for (int i = 0; i < c.nodes.Count; i++) r.Add(i < c.mods.Count && c.mods[i] == 1 ? -1 : VoiceRules.NodeMidi(c, i));
        return r;
    }

    static IEnumerator Guarded()
    {
        var body = Routine();
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Check(false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        Finish();
    }

    static string userMd5; static int prefStep, prefDone; static bool autoHand0;

    static IEnumerator Routine()
    {
        sb = new StringBuilder(); errors = 0; errLines.Clear(); pass = 0; fail = 0; excused = 0; stalls = 0; stallLate = 0; catching = false; curSection = "start";
        Application.logMessageReceived += OnLog;
        SongIO.QuitAutosave = false;
        saves = V3Fixes.SnapshotSaves();
        userMd5 = Md5(SongIO.Path);
        prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, 0); prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, 0);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        lateLog.Length = 0; lateMark = late0;
        var pm = PM;
        autoHand0 = PathManager.AutoHand;
        PathManager.AutoHand = false;   // the real game
        Onboarding.Suppressed = true;
        if (MainMenu.IsShown) MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop(); GridSelection.Clear();
        if (Clipboard.HasPattern) Clipboard.Clear();
        pm.PutDown();

        // ---- 1. a fresh song, its progression placed; its SECTIONS
        SM.StartFreshSong(MusicTheory.RandomSong(707));
        yield return Wait(1.0f);
        var tray = IslandTray.Ensure();
        if (tray != null) tray.SimTool(2);   // the wand: the whole remaining progression
        yield return Wait(1.4f);
        for (int guard = 0; guard < 6 && SM.ColumnCount < 7; guard++) { SM.DuplicateIsland(Idx(SM.AnchorOf(SM.ColumnCount - 1)), 0); yield return Wait(0.5f); }
        AudioCube.SnapAllHome();
        yield return Wait(0.8f);
        int nc = SM.ColumnCount, ns = SM.SectionCount;
        bool touching = true; string touchInfo = "";
        for (int s = 0; s < ns; s++)
            for (int c = SM.SectionFirst(s); c < SM.SectionLast(s); c++)
            {
                float d = SM.ColumnWestEdge(c + 1) - SM.ColumnEastEdge(c);
                if (Mathf.Abs(d) > 0.06f) { touching = false; touchInfo += " c" + c + "→" + (c + 1) + " " + F(d); }
            }
        float gap = ns >= 2 ? SM.ColumnWestEdge(SM.SectionFirst(1)) - SM.ColumnEastEdge(SM.SectionLast(0)) : 0f;
        Check(nc >= 5 && ns >= 2 && touching && gap >= ProjectConfig.SectionGap - 0.1f && SM.SectionBars(0) == ProjectConfig.SectionBars,
              "sections: 4 measures each, the grids of a section TOUCH, a gap between sections",
              "columns " + nc + ", sections " + ns + " (bars " + string.Join("/", Enumerable.Range(0, ns).Select(s => SM.SectionBars(s).ToString()).ToArray()) + "), gap " + F(gap) + touchInfo);
        Check(SectionPlinth.Count == ns && SectionPlinth.LetterOf(0).Length == 1 && SectionPlinth.NumbersShown(0).Count >= 4,
              "every section stands on a plinth with a letter and measure numbers (B)",
              "plinths " + SectionPlinth.Count + ", letters " + string.Join(" ", Enumerable.Range(0, ns).Select(s => SectionPlinth.LetterOf(s)).ToArray()) + ", numbers of A: " + string.Join(" ", SectionPlinth.NumbersShown(0).ToArray()));
        FrameSong();
        yield return Wait(1.2f);
        yield return Shot("i7_01_sections.png");
        // the section's name (§17.1)
        int hRole = History.UndoCount;
        SongOps.SetSectionRole(0, 3);
        yield return Frames(3);
        Check(SM.SectionRole(0) == 3 && SectionPlinth.RoleShown(0) == 3 && History.UndoCount == hRole + 1, "a section can be named (chorus): the plinth shows it, one History entry",
              "role " + SM.SectionRole(0) + ", shown " + SectionPlinth.RoleShown(0) + ", history +" + (History.UndoCount - hRole));
        Mark("sections");

        // ---- 2. SIZE FIRST + QUIET drawing + CAPACITY on the first grid (§15, §19)
        var a0 = SM.AnchorOf(0);
        var tiles0 = new List<TileInteraction>();
        for (int x = 0; x < a0.cols && tiles0.Count < 4; x++) { var t = a0.GetTile(x, Mathf.Min(1, a0.rows - 1)); if (t != null) tiles0.Add(t); }
        bool framed = tiles0.Count >= 4 && FrameTiles(tiles0);
        Check(framed, "the first grid's tiles are pickable from the test camera", "tiles " + tiles0.Count);
        if (!framed) { yield break; }
        yield return SeekPlay(0);
        yield return Wait(0.6f);
        pm.PickUpCube(0);
        pm.SetBrush(48);                                            // a half note, chosen BEFORE the first click
        yield return Frames(2);
        int h0 = History.UndoCount, refus0 = PathManager.CapacityRefusals, auto0 = PathManager.AutoExpands;
        int bars0 = a0.bars;
        yield return Click(tiles0[0]);
        bool quiet = !GlobalClock.IsPlaying && !FocusLoop.Active;
        yield return Click(tiles0[1]);                               // two halves = the grid's 4 beats: full
        float used = pm.DrawUsedBeats, capB = pm.DrawCapacityBeats;
        pm.ToggleDrawLoop(); yield return Frames(3);
        bool hear = FocusLoop.Active && GlobalClock.IsPlaying;
        pm.ToggleDrawLoop(); yield return Frames(3);
        bool hearOff = !FocusLoop.Active && !GlobalClock.IsPlaying;
        Check(quiet && hear && hearOff, "QUIET drawing: starting a path pauses the playing song and loops nothing; hear-it (Space) loops the draft in context and stops",
              "paused " + quiet + ", hear " + hear + ", off " + hearOff);
        bool allowAuto = SM.SectionBarsIfResized(a0.column, a0.bars + 1) <= ProjectConfig.SectionBars;
        yield return Click(tiles0[2]);                               // past the capacity
        yield return Frames(2);
        bool capOk;
        string capInfo = "used " + F(used, "F1") + " / " + F(capB, "F1") + " beats, auto allowed " + allowAuto;
        if (allowAuto) capOk = PathManager.AutoExpands == auto0 + 1 && a0.bars == bars0 + 1;
        else
        {
            bool refused = PathManager.CapacityRefusals == refus0 + 1 && pm.CapacityReached;
            yield return Shot("i7_02_capacity.png");
            bool expanded = pm.ExpandDrawGrid();
            yield return Wait(0.4f);
            a0 = SM.AnchorOf(0);
            var t2 = a0 != null ? a0.GetTile(tiles0[2].gridX, tiles0[2].gridZ) : null;
            if (t2 != null) yield return Click(t2);
            capOk = refused && expanded && a0 != null && a0.bars == bars0 + 1;
            capInfo += ", refused " + refused + ", expanded " + expanded;
        }
        Check(used >= 3.99f && Mathf.Abs(capB - 4f * bars0) < 0.01f && capOk,
              "CAPACITY: two half notes fill a one-measure grid; the next note " + (allowAuto ? "auto-expands it (its section had room)" : "is refused (\"capacity reached\") inside a full section; EXPAND adds the measure and drawing continues"),
              capInfo + ", bars " + bars0 + " → " + (a0 != null ? a0.bars : -1));
        pm.FinishPath();
        yield return Wait(0.8f);
        a0 = SM.AnchorOf(0);
        var cube = NewestOn(a0);
        Check(cube != null && cube.durs.Count >= 2 && cube.durs[0] == 48 && cube.durs[1] == 48,
              "SIZE FIRST: the size picked before the click is the placed note's length", "durs " + (cube != null ? Ints(cube.durs) : "-"));
        pm.PutDown();
        GlobalClock.Stop();
        Mark("draw");
        if (cube == null) yield break;
        int srcId = cube.id;

        // ---- 3. octave copies, harmony, flip (§2.9, CubeOps)
        var up = CubeOps.OctaveCopy(cube, +1);
        var dn = CubeOps.OctaveCopy(CubeById(srcId), -1);
        cube = CubeById(srcId);
        var su = Sounding(cube); var sUp = Sounding(up); var sDn = Sounding(dn);
        bool oct = up != null && dn != null && up.layer == cube.layer + 1 && dn.layer == cube.layer - 1 && up.echoOf == srcId && dn.echoOf == srcId
                   && sUp.Count == su.Count && sDn.Count == su.Count && Enumerable.Range(0, su.Count).All(i => su[i] < 0 || (sUp[i] - su[i] == 12 && su[i] - sDn[i] == 12));
        Check(oct, "octave copies ↑ / ↓: new cubes in the sky / deep layer that SOUND an octave above / below (every note)",
              "src " + Ints(su) + ", up " + Ints(sUp) + ", down " + Ints(sDn));
        var harm = CubeOps.Harmonize(CubeById(srcId), true);
        cube = CubeById(srcId);
        var sh = Sounding(harm); su = Sounding(cube);
        int perfect = 0, sounding = 0; bool inSet = harm != null && sh.Count == su.Count;
        for (int i = 0; inSet && i < su.Count; i++)
        {
            if (su[i] < 0) continue;
            sounding++; int iv = sh[i] - su[i];
            if (iv == 5 || iv == 7) perfect++;
            if (iv != 3 && iv != 4 && iv != 5 && iv != 7 && iv != 8 && iv != 9) inSet = false;
        }
        Check(harm != null && harm.echoOf == srcId && inSet && perfect * 2 >= sounding,
              "a SUPPORT HARMONY above: 4ths / 5ths where they fit (else 3rds / 6ths), linked to its source", "src " + Ints(su) + ", harmony " + Ints(sh) + ", perfect " + perfect + "/" + sounding);
        cube = CubeById(srcId);
        var xs0 = cube.nodes.Select(t => t.gridX).ToList(); var zs0 = cube.nodes.Select(t => t.gridZ).ToList();
        CubeOps.Flip(cube);
        yield return Frames(2);
        cube = CubeById(srcId);
        bool flipped = Enumerable.Range(0, xs0.Count).All(i => cube.nodes[i].gridX == cube.Island.cols - 1 - xs0[i] && cube.nodes[i].gridZ == cube.Island.rows - 1 - zs0[i]);
        CubeOps.Flip(cube);
        yield return Frames(2);
        cube = CubeById(srcId);
        bool back = Enumerable.Range(0, xs0.Count).All(i => cube.nodes[i].gridX == xs0[i] && cube.nodes[i].gridZ == zs0[i]);
        Check(flipped && back, "FLIP turns the path upside down (x → cols−1−x, z → rows−1−z) and back", "xs " + Ints(xs0));
        // the layers phase in at their notes: solid through the last note of its grid's turn, glass after it (the drawn melody is legato,
        // so the copy stays solid inside the turn — the fade shows where the turn ends)
        int upId = up != null ? up.id : -1;
        var uc = CubeById(upId);
        double uEnd = 0; if (uc != null) foreach (var w in uc.windows) uEnd = Math.Max(uEnd, w.start + w.length);
        yield return SeekPlay((float)uEnd - 1.0f);
        float upMin = 1f, upMax = 0f;
        float tEnd = Time.unscaledTime + 2.6f / Mathf.Max(0.1f, (float)GlobalClock.BeatsPerSecond);
        bool shotTaken = false;
        while (Time.unscaledTime < tEnd)
        {
            var u2 = CubeById(upId);
            if (u2 != null) { upMin = Mathf.Min(upMin, u2.LayerPhase); upMax = Mathf.Max(upMax, u2.LayerPhase); }
            if (!shotTaken && GlobalClock.SongBeat > uEnd - 0.6f) { shotTaken = true; yield return Shot("i7_03_layers.png"); }
            yield return null;
        }
        GlobalClock.Stop();
        Check(upMax >= 0.9f && upMin <= 0.35f, "the sky copy PHASES IN around its notes and fades to glass between them", "phase min " + F(upMin) + " max " + F(upMax));
        Mark("ideas");

        // ---- 4. paste modes: echo ↑, late echo ↓, answer, step ↑ (§2.9, §17.3)
        cube = CubeById(srcId);
        Clipboard.Copy(cube);
        var next = Clipboard.NextGrid(cube.Island);
        var eUp = Clipboard.PasteNext(Clipboard.PasteMode.EchoUp);
        Clipboard.EchoLate = true;
        var eLate = Clipboard.PasteNext(Clipboard.PasteMode.EchoDown);
        Clipboard.EchoLate = false;
        var ans = Clipboard.PasteNext(Clipboard.PasteMode.Answer);
        var step = Clipboard.PasteNext(Clipboard.PasteMode.StepUp);
        int stepId = step != null ? step.id : -1, ansId = ans != null ? ans.id : -1;   // later ops rebuild the song: keep ids, not objects
        yield return Wait(1.0f);
        int lastAns = -1;
        if (ans != null) { var sa = Sounding(ans); for (int i = sa.Count - 1; i >= 0; i--) if (sa[i] >= 0) { lastAns = sa[i]; break; } }
        int rootPc = next != null ? Harmony.Pc(next.chordRootMIDI) : -1;
        bool ansHome = false;
        if (lastAns >= 0 && next != null) { int rel = Harmony.Pc(lastAns - next.chordRootMIDI - SongManager.Transpose); ansHome = rel == 0 || rel == 3 || rel == 4; }
        Check(eUp != null && eUp.layer == cube.layer + 1 && eUp.Island == next && eLate != null && eLate.layer == cube.layer - 1 && eLate.mods.Count > 0 && eLate.mods[0] == 1
              && ans != null && ansHome && step != null,
              "paste modes onto the next grid: an ECHO an octave up (sky), a LATE echo an octave down (a leading rest), an ANSWER that ends home, a STEP up",
              "echo layer " + (eUp != null ? eUp.layer : -9) + ", late first mod " + (eLate != null && eLate.mods.Count > 0 ? eLate.mods[0] : -1) + ", answer ends " + lastAns + " over root pc " + rootPc + " (home " + ansHome + "), step " + (step != null));
        Mark("paste");

        // ---- 5. a melody PHRASE that grows from 1 to 2 measures (§14)
        int pcol = ns >= 2 ? SM.SectionFirst(1) : 1;
        int pidx = SongOps.AddPhrase(pcol, 3);
        yield return Wait(0.6f);
        var ph = KindIn(pcol, 4);
        bool phraseOk = false; string phInfo = "";
        if (ph != null && ph.cols >= 8)
        {
            pm.PickUpCube(3); pm.SetBrush(24);
            GlobalClock.Stop();
            var cells = new List<TileInteraction> { ph.GetTile(0, 4), ph.GetTile(2, 6), ph.GetTile(4, 8), ph.GetTile(ph.cols - 1, 2) };
            if (FrameTiles(cells, 62f))
            {
                int grows0 = PathManager.PhraseGrows;
                yield return Click(ph.GetTile(0, 4));
                ph = KindIn(pcol, 4); yield return Click(ph.GetTile(2, 6));
                ph = KindIn(pcol, 4); yield return Click(ph.GetTile(4, 8));
                yield return Wait(0.5f);
                ph = KindIn(pcol, 4);
                float s0, e0; SM.PhraseSpan(ph, out s0, out e0);
                var pc = PathManager.PhraseCubeOf(ph, 3);
                var px = pc != null ? pc.nodes.Select(t => t.gridX).ToList() : new List<int>();
                bool ascending = px.Count >= 3; for (int i = 1; i < px.Count; i++) if (px[i] < px[i - 1]) ascending = false;
                phraseOk = ph.phraseBeats == 2 * GlobalClock.BeatsPerBar && PathManager.PhraseGrows > grows0 && e0 > SM.ColumnStart(pcol) + SM.PassLength(pcol) + 0.01f && ascending;
                phInfo = "beats " + ph.phraseBeats + ", span " + F(s0, "F1") + ".." + F(e0, "F1") + " (column " + pcol + " ends " + F(SM.ColumnStart(pcol) + SM.PassLength(pcol), "F1") + "), cells x " + Ints(px);
                yield return Shot("i7_05_phrase.png");
            }
            else phInfo = "cells not pickable";
        }
        else phInfo = "no phrase island (index " + pidx + ")";
        Check(phraseOk, "a melody PHRASE drawn cell by cell GREW from 1 to 2 measures as the notes reached its end: ONE grid spanning two chords, the cube walking left → right", phInfo);
        pm.PutDown();
        Mark("phrase");

        // ---- 6. STAIRS: a spark fall (the ground after it lower) and a climb (§2.4, §2.5)
        int scol = ns >= 2 ? SM.SectionLast(0) : 1;
        int sidx = SongOps.AddStairIsland(Idx(SM.AnchorOf(scol)), true, 3);
        yield return Wait(0.6f);
        var st = KindIn(scol, 3);
        if (st != null) { SongOps.SetStair(Idx(st), 2, -1, 4, 12, 1); yield return Wait(0.9f); st = KindIn(scol, 3); }
        bool falling = st != null && st.cols == 4;
        var mids = new List<int>();
        if (st != null) for (int x = 0; x < st.cols; x++) { var t = st.GetTile(x, 0); mids.Add(t != null ? t.midi : -1); }
        for (int i = 1; i < mids.Count; i++) if (mids[i] >= mids[i - 1]) falling = false;
        AudioCube runner = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == st) { runner = c; break; }
        float g0 = SM.GroundOf(scol), g1 = SM.GroundOf(scol + 1);
        yield return Wait(0.8f);
        var after = SM.AnchorOf(scol + 1);
        Check(falling && runner != null && runner.IsRunner && Mathf.Abs(g1 - g0 + 4 * ProjectConfig.StairStepRise) < 0.02f && after != null && Mathf.Abs(after.GroundY - g1) < 0.05f,
              "STAIRS: a spark fall of 4 steps (falling pitches, a runner) and the grids after it stand 1.36 lower (they glide there)",
              "pitches " + Ints(mids) + ", runner " + (runner != null) + ", ground " + F(g0) + " → " + F(g1) + ", next grid shown at " + (after != null ? F(after.GroundY) : "-"));
        if (st != null && runner != null)
        {
            FrameColumns(scol - 1, scol + 1);
            yield return SeekPlay(SM.ColumnStart(scol) + SM.PassLength(scol) - 1.6f);
            yield return Wait(0.7f);
            yield return Shot("i7_06_stairs.png");
            GlobalClock.Stop();
        }
        int ccol = SM.ColumnCount - 2;
        SongOps.AddStairIsland(Idx(SM.AnchorOf(ccol)), true, 3);
        yield return Wait(0.6f);
        var st2 = KindIn(ccol, 3);
        if (st2 != null) { SongOps.SetStair(Idx(st2), 0, 1, 3, 12, 1); yield return Wait(0.9f); st2 = KindIn(ccol, 3); }   // the op rebuilt the song
        float gc0 = SM.GroundOf(ccol), gc1 = SM.GroundOf(ccol + 1);
        Check(st2 != null && gc1 > gc0 + 0.9f, "a CLIMB: the grids after an up stair stand higher (in line with its top)", "ground " + F(gc0) + " → " + F(gc1));
        FrameSong();
        yield return Wait(1.0f);
        yield return Shot("i7_07_heights.png");
        Mark("stairs");

        // ---- 7. REPEAT ×3 with REWIND + VARY on the first grid (§2.6)
        a0 = SM.AnchorOf(0); int ai = Idx(a0);
        SM.SetRepeat(ai, 3); yield return Wait(0.4f);
        SM.SetRewind(Idx(SM.AnchorOf(0)), true); yield return Wait(0.4f);
        SM.SetVary(Idx(SM.AnchorOf(0)), 1); yield return Wait(0.6f);
        a0 = SM.AnchorOf(0);
        float passLen = SM.PassLength(0), c0s = SM.ColumnStart(0);
        int rp; float rph = SM.RewindPhase(a0, c0s + passLen - 0.2f, out rp);
        int rpl; float rlast = SM.RewindPhase(a0, c0s + 3 * passLen - 0.2f, out rpl);
        cube = CubeById(srcId);
        bool varied = false;
        if (cube != null) for (int w = 0; w < cube.windows.Count && !varied; w++) if (cube.windows[w].pass >= 1) for (int n = 0; n < cube.nodes.Count; n++) if (cube.IsVaried(w, n)) { varied = true; break; }
        Check(a0.rewind && a0.vary == 1 && !a0.HasBelt && rph >= 0f && rph < 1f && rp == 0 && rlast < 0f && varied,
              "REPEAT ×3 + REWIND: no belt, time unwinds at the end of every pass but the last; VARY: the later passes play small differences",
              "phase " + F(rph) + " (pass " + rp + "), last pass " + F(rlast) + ", varied note " + varied);
        if (cube != null)
        {
            FrameColumns(0, 0);
            yield return SeekPlay(c0s + passLen - 0.45f);
            yield return Wait(0.18f);
            yield return Shot("i7_08_rewind.png");
            GlobalClock.Stop();
        }
        Mark("rewind");

        // ---- 8. THE LONG GRID (§21: "the grid extending and being longer and the cubes just going from left to right through the grid"),
        //         LAUNCH = a build-up into the next section (§21.4: no fling — cubes stay on their grid)
        a0 = SM.AnchorOf(0);
        int last0 = SM.SectionLast(0);
        SM.SetCarry(Idx(a0), Mathf.Min(ProjectConfig.MaxCarry, last0));
        yield return Wait(0.8f);
        a0 = SM.AnchorOf(0);
        var lg = SM.LongGridOf(a0);
        cube = CubeById(srcId);
        int flowW = 0; if (cube != null) foreach (var w in cube.windows) if (w.flow) flowW++;
        bool joined = lg != null && lg.Count >= 2;
        if (joined) for (int i = 0; i + 1 < lg.Count; i++)
                if (!lg[i].JoinedEast || !lg[i + 1].JoinedWest || Mathf.Abs(lg[i].EastEdge - lg[i + 1].WestEdge) > 0.02f || lg[i + 1].column != lg[i].column + 1) joined = false;
        Check(joined && flowW >= lg.Count - 1,
              "THE LONG GRID: extended over its section, the first grid and the next ones JOIN into one platform, the chord changing measure by measure",
              "members " + (lg != null ? lg.Count : 0) + " (columns " + (lg != null ? Ints(lg.Select(k => k.column)) : "-") + "), carried windows " + flowW);
        if (joined && cube != null)
        {
            // the walk: left → right through the long grid with ordinary steps — never flying, never off it
            FrameColumns(lg[0].column, lg[lg.Count - 1].column);
            float colEnd0 = SM.ColumnStart(lg[0].column) + SM.PassLength(lg[0].column) * SM.ColumnPasses(lg[0].column);
            var lastM = lg[lg.Count - 1];
            float lgEnd = SM.ColumnStart(lastM.column) + SM.PassLength(lastM.column) * SM.ColumnPasses(lastM.column);
            yield return SeekPlay(Mathf.Max(0f, colEnd0 - 1.0f));
            bool flew = false, off = false, backwards = false, shotW = false; int maxM = -1, visited = 0, crossW0 = AudioCube.CrossCount;
            float tUntil = Time.unscaledTime + 40f;
            while (Time.unscaledTime < tUntil && GlobalClock.IsPlaying && GlobalClock.SongBeat < lgEnd - 0.1f && GlobalClock.SongBeat >= colEnd0 - 1.5f)
            {
                var cc = CubeById(srcId);
                if (cc != null)
                {
                    if (cc.Flying || cc.Flinging) flew = true;
                    int mi = MemberUnder(cc, lg);
                    if (mi < 0) off = true;
                    else { if (mi < maxM) backwards = true; if (mi > maxM) { maxM = mi; visited++; } }
                }
                if (!shotW && GlobalClock.SongBeat > colEnd0 + 0.6f) { shotW = true; yield return Shot("i7_09_longgrid.png"); continue; }
                yield return null;
            }
            GlobalClock.Stop();
            int crossedW = AudioCube.CrossCount - crossW0;
            Check(!flew && !off && !backwards && visited >= Mathf.Min(3, lg.Count) && crossedW >= Mathf.Min(2, lg.Count - 1),
                  "the cube WALKS left → right through the long grid with ordinary steps — it never flies and never leaves the grid",
                  "members visited " + visited + "/" + lg.Count + ", measure crossings " + crossedW + ", flew " + flew + ", off the long grid " + off + ", backwards " + backwards);
        }
        var launcher = SM.AnchorOf(last0);
        if (NewestOn(launcher) == null && Clipboard.HasPattern) { Clipboard.PasteOn(launcher); yield return Wait(0.8f); launcher = SM.AnchorOf(last0); }
        SM.SetLaunch(Idx(launcher), true);
        yield return Wait(0.5f);
        launcher = SM.AnchorOf(last0);
        var lIds = new List<int>(); foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.Island == launcher) lIds.Add(c.id);
        float land = SM.TurnEnd(launcher);
        LaunchRiser.ResetLog();
        FrameColumns(Mathf.Max(0, last0 - 1), Mathf.Min(SM.ColumnCount - 1, last0 + 1));
        yield return SeekPlay(Mathf.Max(0f, land - GlobalClock.BeatsPerBar - 1.5f));
        bool launchMoved = false, shotL = false;
        float until = Time.unscaledTime + 12f;
        while (Time.unscaledTime < until && GlobalClock.SongBeat < land + 0.6f)
        {
            foreach (int id in lIds)
            {
                var cc = CubeById(id);
                if (cc != null && (cc.Flinging || cc.Flying || MemberUnder(cc, GridOf(cc)) < 0)) launchMoved = true;
            }
            if (!shotL && GlobalClock.SongBeat > land - 0.5f) { shotL = true; yield return Shot("i7_09_launch.png"); continue; }
            yield return null;
        }
        yield return Wait(0.3f);
        GlobalClock.Stop();
        Check(lIds.Count > 0 && !launchMoved && LaunchRiser.Crashes >= 1 && (LaunchRiser.Sent >= 1 || LaunchRiser.Skipped >= 1),
              "LAUNCH (§21.4, a build-up): a riser swells into the next section and a crash lands on its first beat — the launching grid's cubes stay on their grid",
              "cubes on the launcher " + lIds.Count + ", moved off " + launchMoved + ", risers " + LaunchRiser.Sent + " (skipped " + LaunchRiser.Skipped + "), crashes " + LaunchRiser.Crashes);
        Mark("launch");

        // ---- 9. Shift-select a section: octave ↑, copy, paste after, delete (§16)
        FrameSong();
        yield return Wait(1.0f);
        int s1 = ns >= 2 ? 1 : 0;
        GridSelection.SelectSection(s1);
        yield return Frames(2);
        var sel = new List<KeyBlock>(GridSelection.Selected);
        int whole; bool isWhole = GridSelection.IsWholeSection(out whole);
        var regs0 = sel.Select(k => k.register).ToList();
        int hT = History.UndoCount;
        SongOps.TransposeGrids(sel, 1);
        yield return Wait(0.6f);
        sel = new List<KeyBlock>(GridSelection.Selected);
        var regs1 = sel.Select(k => k.register).ToList();
        bool transposed = sel.Count == regs0.Count && Enumerable.Range(0, sel.Count).All(i => regs1[i] == Mathf.Min(2, regs0[i] + 1)) && History.UndoCount == hT + 1;
        yield return Shot("i7_10_selection.png");
        int colsBefore = SM.ColumnCount;
        SongOps.CopyGrids(sel);
        int lastSel = GridSelection.LastColumn;
        var pasted = SongOps.PasteGrids(lastSel + 1);
        yield return Wait(0.8f);
        int colsPasted = SM.ColumnCount;
        var newSel = new List<KeyBlock>(GridSelection.Selected);
        SongOps.DeleteGrids(newSel);
        yield return Wait(0.8f);
        Check(sel.Count >= 1 && isWhole && transposed && colsPasted > colsBefore && SM.ColumnCount == colsBefore,
              "SELECT a whole section: octave ↑ on every grid (one History entry), copy + paste after (new columns), delete them again",
              "selected " + sel.Count + " (whole section " + isWhole + "), registers " + Ints(regs0) + " → " + Ints(regs1) + ", columns " + colsBefore + " → " + colsPasted + " → " + SM.ColumnCount);
        GridSelection.Clear();
        Mark("select");

        // ---- 10. HOLD THEN RETURN across the loop (§12)
        var tower = SM.AnchorOf(SM.SectionFirst(s1));
        var tower2 = SM.AnchorOf(SM.SectionLast(s1));
        var holder = CubeById(stepId);
        if (holder == null) holder = CubeById(ansId);
        double hEnd = 0; if (holder != null) foreach (var w in holder.windows) hEnd = Math.Max(hEnd, w.start + w.length);
        FrameSong();
        yield return SeekPlay(0);
        float total = GlobalClock.TotalBeats;
        double tS, tE; SM.TurnSpan(tower, out tS, out tE);
        float topSeen = 0f, heldAfter = -1f;
        bool cubeHeld = false; float holdDrift = -1f; Vector3 holdPos = Vector3.zero; bool holdArmed = false;
        float endT = Time.unscaledTime + 70f;
        string stray = null; int strayFrames = 0;
        while (Time.unscaledTime < endT && GlobalClock.SongBeat < total - 0.3f)
        {
            float b = GlobalClock.SongBeat;
            { var sc = StrayCube(); if (sc != null) { strayFrames++; if (stray == null) stray = sc + " at beat " + b.ToString("F2"); } }
            if (tower != null && b > tS + 0.5f && b < tE) topSeen = Mathf.Max(topSeen, tower.TowerLift);
            if (tower != null && b > tE + 2f && heldAfter < 0f) heldAfter = tower.TowerLift;
            if (holder != null && b > hEnd + 1.0f && b < total - 1f)
            {
                if (!holdArmed) { holdArmed = true; cubeHeld = holder.Holding; holdPos = holder.transform.position; }
                else holdDrift = Mathf.Max(holdDrift, Vector3.Distance(new Vector3(holder.transform.position.x, 0f, holder.transform.position.z), new Vector3(holdPos.x, 0f, holdPos.z)));
            }
            yield return null;
        }
        yield return Shot("i7_11_hold.png");
        // across the loop point: the towers sink slightly out of sync; the holding cube travels home
        float l1 = -1f, l2 = -1f; bool tripped = false, home = false;
        float endT2 = Time.unscaledTime + 15f;
        while (Time.unscaledTime < endT2)
        {
            float b = GlobalClock.SongBeat;
            { var sc = StrayCube(); if (sc != null) { strayFrames++; if (stray == null) stray = sc + " at beat " + b.ToString("F2"); } }
            if (b > 0.9f && b < 1.3f && l1 < 0f && tower != null && tower2 != null) { l1 = tower.TowerLift; l2 = tower2.TowerLift; }
            if (holder != null && holder.Tripping) tripped = true;
            // home = back on its home tile and not travelling (AudioCube.Holding stays true outside its turns, also at home)
            if (b > 3.2f && b < total * 0.5f)
            {
                var ht = holder != null ? holder.HomeTile : null;
                if (ht != null) { var hp = holder.TopOf(ht); var cp = holder.transform.position; home = !holder.Tripping && new Vector2(cp.x - hp.x, cp.z - hp.z).magnitude < 0.35f; }
                break;
            }
            yield return null;
        }
        GlobalClock.Stop();
        float rise = tower != null ? tower.register * ProjectConfig.TowerRise : 0f;
        Check(tower != null && tower.register >= 1 && topSeen >= rise - 0.15f && heldAfter >= rise - 0.15f,
              "TOWERS rise 60 % taller (3.2 per octave) on their turn and STAY UP after it, until the song resets",
              "register " + (tower != null ? tower.register : 0) + ", top seen " + F(topSeen) + " (want " + F(rise) + "), 2 beats after its turn " + F(heldAfter));
        Check(l1 >= 0f && Mathf.Abs(l1 - l2) > 0.01f, "after the reset the towers sink slightly OUT OF SYNC", "at beat ≈ 1: " + F(l1) + " vs " + F(l2));
        Check(cubeHeld && holdDrift >= 0f && holdDrift < 0.3f && tripped && home, "a finished grid's cube HOLDS where it ended (no teleport back) and travels home only after the loop point",
              "held " + cubeHeld + " (drift " + F(holdDrift) + " u), trip " + tripped + ", home " + home + ", its last window ends at beat " + hEnd.ToString("F1"));
        Check(strayFrames == 0, "§21: no cube ever leaves its grid (or its long grid) — the whole song and across the loop, with stairs, a phrase, layers, a long grid and a launch",
              stray == null ? "0 stray frames" : strayFrames + " stray frames, first: " + stray);
        Mark("hold");

        // ---- 11. the shortcut sheet, present
        HudShortcuts.Open();
        yield return Frames(3);
        bool sheet = HudShortcuts.IsOpen && HudShortcuts.RowCount >= 40;
        yield return Shot("i7_12_shortcuts.png");
        HudShortcuts.Close();
        yield return Frames(3);
        Check(sheet && !HudShortcuts.IsOpen, "the ? shortcut sheet opens with every shortcut and closes", "rows " + HudShortcuts.RowCount);
        Presenter.Enter();
        yield return Wait(4f);
        yield return Shot("i7_13_present.png");
        Presenter.Exit();
        yield return Wait(2f);
        Check(!Presenter.Active, "present mode runs over the v7 world and returns", "");
        Mark("present");

        // ---- 12. save / load round trip of every v7 field
        string tmp = Cap("v7_integration_song.json");
        var before = SongState.Capture();
        bool saved = SongIO.SaveTo(tmp);
        bool loaded = saved && SongIO.LoadFrom(tmp);
        yield return Wait(1.0f);
        var afterSt = SongState.Capture();
        string sigA = Sig(before), sigB = Sig(afterSt);
        Check(saved && loaded && sigA == sigB, "save / load keeps every v7 field (kinds 3 / 4, stairs, phrases, carry style, rewind, vary, launch, names, sections, layers, bends, echoes)",
              sigA.Length > 200 ? sigA.Substring(0, 200) + "…" : sigA);
        try { File.Delete(tmp); } catch (Exception) { }
        Mark("save");

        // ---- 13. §20 the KEYBOARD and its HANDS (v8: floating spheres replaced the cat): a sphere hops onto each key a keyboard draft places and
        //          stays on the last one; the finished melody is the hands' score; playing, a sphere lands on its key at the note's onset
        GlobalClock.Stop(); pm.PutDown();
        int hostI = SM.Islands.FindIndex(k => k != null && k.kind == 0 && k.column == 0);
        int kAt = -1;
        try { kAt = SM.AddKeyboardIsland(hostI, true); } catch (Exception e) { Check(false, "§20: add a keyboard island", e.GetType().Name + ": " + e.Message); }
        yield return Wait(1.4f);   // it rises from the sea
        var kbd = kAt >= 0 && kAt < SM.Islands.Count ? SM.Islands[kAt] : null;
        if (kbd != null && !kbd.IsKeyboard) kbd = null;
        KeyHands.RefreshNow();
        var hset = kbd != null ? KeyHands.Of(kbd) : null;
        FrameColumns(kbd != null ? kbd.column : 0, kbd != null ? kbd.column : 0);
        yield return Wait(1.2f);   // the orbit camera settles on the column
        int dk0 = KeyHands.DraftKeys;
        bool struck = false, sits = false, lands = false; int melNotes = 0, handNotes = 0; string landWhy = "";
        if (kbd != null && hset != null)
        {
            pm.PickUpCube(8); pm.SetBrush(12);
            int[] keys = { 9, 11, 12, 16 };
            TileInteraction lastKey = null;
            for (int i = 0; i < keys.Length; i++)
            {
                var t = kbd.GetTile(keys[i], 0);
                if (t == null) continue;
                yield return ClickKey(t);
                lastKey = t;
                if (i == 2) yield return Shot("i7_14_hands_draft.png");
                yield return Wait(0.35f);
            }
            struck = KeyHands.DraftKeys - dk0 >= keys.Length;
            sits = lastKey != null && hset.Hands.Any(h => h.Key == lastKey && h.Shown.phase != KeyHands.Phase.Air);
            pm.FinishPath();
            yield return Wait(0.5f);
            pm.PutDown();
            KeyHands.RefreshNow();
            var mel = NewestOn(kbd);
            melNotes = mel != null ? mel.nodes.Count : 0;
            handNotes = hset.NoteCount;
            // playing: some sphere sits on the sounding key right at a note's onset (the pure pose)
            for (int hi = 0; hi < hset.Hands.Count && !lands; hi++)
            {
                var h = hset.Hands[hi];
                for (int ni = 0; ni < h.NoteCount && !lands; ni++)
                {
                    float on, off; TileInteraction tile; bool wr;
                    if (!h.NoteOf(ni, out on, out off, out tile, out wr) || wr) continue;
                    var p = hset.PoseAt(hi, on + 0.001f);
                    lands = p.phase == KeyHands.Phase.Down && p.key == tile;
                    if (!lands) landWhy = "beat " + on.ToString("F2") + ": " + p.phase + (p.key != null ? "@" + p.key.gridX : "");
                }
            }
        }
        Check(kbd != null && hset != null && hset.Hands.Count >= KeyHands.MinHands, "§20 / v8: a keyboard island added to a column gets its sphere hands",
              "keyboard " + (kbd != null) + ", spheres " + (hset != null ? hset.Hands.Count : 0));
        Check(struck && sits, "v8: a sphere hops onto every key the draft places and stays on the last one", "draft keys " + (KeyHands.DraftKeys - dk0) + ", sits " + sits);
        Check(melNotes >= 4 && handNotes >= 4, "§20: the finished melody (4 keys) is in the hands' score", "melody " + melNotes + " notes, hands " + handNotes);
        Check(lands, "v8: playing, a sphere lands on the sounding key at the note's onset", lands ? "ok" : landWhy);
        Release();
        Mark("keyboard");

        // ---- synth
        int late = Synth.LateEvents - late0 - excused, err = Synth.Errors - err0;
        Check(late <= 0 && err == 0, "synth: no late notes and no errors over the whole run (seek / capture catch-ups counted apart)",
              "late " + late + " (apart " + excused + ": " + stallLate + " of them in the frames after " + stalls + " main-thread stalls > 0.12 s), errors " + err + " | " + lateLog);
    }

    /// <summary>The v7 fields of a song, as text (kinds, stairs, phrases, styles, names, sections; cube layers, bends, echoes).</summary>
    static string Sig(SongState s)
    {
        var b = new StringBuilder();
        b.Append("v").Append(s.version).Append(" sec[").Append(s.sections != null ? Ints(s.sections) : "-").Append("] ");
        foreach (var m in s.measures)
            b.Append(m.kind).Append(':').Append(m.col).Append(':').Append(m.bars).Append(':').Append(m.reg).Append(':').Append(m.repeat)
             .Append('/').Append(m.stairType).Append(m.stairDir).Append(m.stairSteps).Append(m.stairRate).Append(m.stairLead ? 'L' : 'S')
             .Append('/').Append(m.phraseOffset).Append(',').Append(m.phraseBeats).Append(',').Append(m.phraseGrid)
             .Append('/').Append(m.carry).Append(m.carryStyle).Append(m.rewind ? 'R' : '-').Append(m.vary).Append(m.launch ? 'L' : '-').Append(m.secRole).Append(' ');
        foreach (var c in s.cubes.OrderBy(c => c.id))
            b.Append('#').Append(c.id).Append('@').Append(c.measure).Append(" l").Append(c.layer).Append(" e").Append(c.echoOf).Append(" b").Append(c.bend != null && c.bend.Any(x => x != 0) ? Ints(c.bend) : "-").Append(" n").Append(c.xs != null ? c.xs.Length : 0).Append(' ');
        return b.ToString();
    }

    static void Finish()
    {
        Application.logMessageReceived -= OnLog;
        try
        {
            PathManager.SimOnly = false; CubeInspector.CloseImmediate(); if (Presenter.Active) Presenter.Exit(); FocusLoop.Dismiss(); GlobalClock.Stop();
            if (IslandTray.IsOpen) IslandTray.Close(); IslandHeader.Hide(); if (PathManager.I != null) PathManager.I.PutDown(); if (Clipboard.HasPattern) Clipboard.Clear();
            if (HudShortcuts.IsOpen) HudShortcuts.Close(); GridSelection.Clear();
            Release();
        }
        catch (Exception) { }
        PathManager.AutoHand = autoHand0;
        Onboarding.Suppressed = false;
        V3Fixes.RestoreSaves(saves);
        PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep); PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone); PlayerPrefs.Save();
        Check(Md5(SongIO.Path) == userMd5, "the user's save is untouched", userMd5);
        Check(errors == 0, "no console errors or exceptions during the run", errors + (errLines.Count > 0 ? ": " + string.Join(" || ", errLines.ToArray()) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v7_integration_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }
}
