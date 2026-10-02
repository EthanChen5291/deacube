using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Play-mode regression checks for the v3 review fixes (F1-F23, S1-S10): <c>V3Fixes.Run()</c> starts a coroutine; poll
/// <c>V3Fixes.Done</c> / <c>V3Fixes.Progress</c> and read <c>V3Fixes.Report</c> (also written to Captures/v3fixes_report.txt).
/// The synchronous checks are public methods returning PASS / FAIL lines (SeamPresses, DragNeedsPointerMotion, PlayingPicks,
/// RightClick, CmdZMidDraw, HudPressMidDraw pick through physics: call them at least a frame after <see cref="LoadFixture"/>, when
/// the old islands' colliders are gone; HubAndOverview, DragCancelKeys, BackupRotation, AtomicSave, WheelHintBubble load their own
/// song); the timed ones are coroutines inside the run. The fixture is loaded (never the user's save); backups, the autosave and the prefs
/// the run touches are put back as they were; the user's save is only read (Continue fallback) and its md5 is checked.
/// </summary>
public static class V3Fixes
{
    public static string Report = "";
    public static bool Done;
    public static string Progress = "";
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "v3fixes_report.txt");

    static StringBuilder sb;
    static int pass, fail, pushes, logErrors, logWarnings;
    static readonly List<string> logLines = new List<string>();
    static readonly List<string> events = new List<string>();

    public static string Run(bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null || UIManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = ""; Progress = "start";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        CubeInspector.Ensure().StartCoroutine(Routine());
        return "started";
    }

    // ------------------------------------------------------------------ helpers
    static string L(bool ok, string id, string name, string detail = null)
    {
        return (ok ? "PASS " : "FAIL ") + id + " " + name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + "\n";
    }

    static void Add(string lines)
    {
        if (string.IsNullOrEmpty(lines)) return;
        foreach (var l in lines.Split('\n'))
        {
            if (l.Length == 0) continue;
            if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++;
            sb.Append(l).Append('\n');
        }
        Report = sb.ToString();
    }

    static void OnHistory() { pushes++; }
    static void OnEvent(string e) { events.Add(e); }
    static void OnLog(string msg, string stack, LogType t)
    {
        if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { logErrors++; if (logLines.Count < 12) logLines.Add(t + ": " + msg); }
        else if (t == LogType.Warning)
        {
            // v7: the synth's watchdog reports the audio environment (the host stopped / the callback went silent and was revived): counted apart
            if (msg != null && (msg.StartsWith("SynthEngine: the host AudioSource had stopped") || msg.StartsWith("SynthEngine: the audio callback"))) { envWarnings++; return; }
            logWarnings++; if (logLines.Count < 12) logLines.Add("Warning: " + msg);
        }
    }
    static int envWarnings;

    /// <summary>The test preamble (menu hidden, locks off, tutorial suppressed, inspector / tray / prompt closed, the real mouse ignored).</summary>
    public static void Prepare()
    {
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock("tray"); Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (IslandTray.IsOpen) IslandTray.Close();
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (PathManager.I != null) PathManager.I.BrushTicks = 24;   // v4: the note-length brush persists between checks; draw quarters
        if (PathManager.I != null) { if (PathManager.I.IsDrawing) PathManager.I.CancelPath(); if (PathManager.I.Drag != null && PathManager.I.Drag.Busy) PathManager.I.Drag.Cancel(); }
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        KeyShim.Clear();
    }

    /// <summary>Loads the fixture (6 islands, 13 cubes, 121 BPM) with a fresh History. The replaced islands are destroyed at the end of
    /// the frame: pick through physics a frame later.</summary>
    public static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop(); GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void FreshSong(int seed)
    {
        CubeInspector.CloseImmediate();
        GlobalClock.Stop();
        SongManager.I.StartFreshSong(MusicTheory.RandomSong(seed, "fix test " + seed));
        GlobalClock.Stop(); GlobalClock.Seek(0);
    }

    /// <summary>Poses the main camera directly (the rig suspended) so screen points are exact this frame.</summary>
    static void Frame(Vector3 focus, float dist, float yaw = -24f, float pitch = 52f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static int Finalized() { int k = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized) k++; return k; }
    static AudioCube LastFinalized() { for (int i = SequenceMaster.Cubes.Count - 1; i >= 0; i--) { var c = SequenceMaster.Cubes[i]; if (c != null && c.isFinalized) return c; } return null; }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static string Cells(AudioCube c)
    {
        var s = new StringBuilder();
        for (int i = 0; c != null && i < c.nodes.Count; i++) { if (i > 0) s.Append(' '); s.Append(c.nodes[i].gridX).Append(',').Append(c.nodes[i].gridZ); }
        return s.ToString();
    }

    /// <summary>An island (not a Moon) with <paramref name="len"/> free tiles in a row (x0 .. x0 + len - 1 on row z).</summary>
    static bool FreeRow(int len, out KeyBlock isl, out int z, out int x0)
    {
        isl = null; z = -1; x0 = -1;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || kb.IsMoon || kb.cols < len) continue;
            for (int zz = 0; zz < kb.rows; zz++)
                for (int x = 0; x + len - 1 < kb.cols; x++)
                {
                    bool free = true;
                    for (int k = 0; k < len && free; k++) if (PathManager.TopCubeOn(kb.GetTile(x + k, zz)) != null) free = false;
                    if (free) { isl = kb; z = zz; x0 = x; return true; }
                }
        }
        return false;
    }

    /// <summary>A free row of <paramref name="len"/> tiles whose tops pick themselves from a steep view along +z on its middle (the camera is
    /// left posed there): no cube in front of any of them.</summary>
    static bool PickableRow(int len, out KeyBlock isl, out int z, out int x0)
    {
        isl = null; z = -1; x0 = -1;
        var pm = PathManager.I;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || kb.IsMoon || kb.cols < len) continue;
            for (int zz = 0; zz < kb.rows; zz++)
                for (int x = 0; x + len - 1 < kb.cols; x++)
                {
                    bool ok = true;
                    for (int k = 0; k < len && ok; k++) if (PathManager.TopCubeOn(kb.GetTile(x + k, zz)) != null) ok = false;
                    if (!ok) continue;
                    Frame(kb.GetTile(x + len / 2, zz).Top, 9f, 0f, 62f);
                    for (int k = 0; k < len && ok; k++) { var t = kb.GetTile(x + k, zz); if (pm.PickAt(Scr(t.Top)).tile != t) ok = false; }
                    if (ok) { isl = kb; z = zz; x0 = x; return true; }
                }
        }
        return false;
    }

    static AudioCube PitchedCube(int minNodes)
    {
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && c.Island != null && c.Island.cols >= 5 && c.nodes.Count >= minNodes && c.windows.Count > 0) return c;
        return null;
    }

    /// <summary>True when the first world collider the ray meets (cubes skipped) is <paramref name="isl"/>'s platform (not a tile, not the hub).</summary>
    static bool FirstIsPlatform(KeyBlock isl, Ray ray)
    {
        var hs = Physics.RaycastAll(ray, 600f);
        Array.Sort(hs, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hs)
        {
            if (h.collider == null || h.collider.GetComponentInParent<AudioCube>() != null) continue;
            if (h.collider.GetComponentInParent<TileInteraction>() != null) return false;
            var kb = h.collider.GetComponentInParent<KeyBlock>();
            if (kb != isl) return false;
            return kb.Hub == null || !(h.collider.transform == kb.Hub || h.collider.transform.IsChildOf(kb.Hub));
        }
        return false;
    }

    /// <summary>A screen point on the seam between tile (x, z) and (x + 1, z) whose ray meets the platform first (the bug case) and that the
    /// picking maps to <paramref name="want"/>; false (and <paramref name="want"/>'s top) when this view has none.</summary>
    static bool SeamPoint(KeyBlock isl, int x, int z, TileInteraction want, out Vector3 screen)
    {
        var cam = Camera.main; float S = ProjectConfig.Spacing;
        for (int iz = 0; iz < 9; iz++)
            for (int ix = 0; ix < 5; ix++)
            {
                float lx = x * S + 0.52f + ix * 0.02f, lz = z * S - 0.4f + iz * 0.1f;   // inside the 0.12 u gap between the two tiles
                Vector3 sp = cam.WorldToScreenPoint(isl.transform.TransformPoint(new Vector3(lx, -0.03f, lz)));
                if (!FirstIsPlatform(isl, cam.ScreenPointToRay(sp))) continue;
                var r = PathManager.I.PickAt(sp);
                if (r.tile != want || !r.seam) continue;
                screen = sp; return true;
            }
        screen = cam.WorldToScreenPoint(want.Top);
        return false;
    }

    static void CmdKey(KeyCode k, bool shift = false)
    {
        KeyShim.Sim(KeyCode.LeftCommand, false, true, false);
        if (shift) KeyShim.Sim(KeyCode.LeftShift, false, true, false);
        KeyShim.Sim(k, true, true, false);
        PathManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float timeout) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; yield return null; } }

    /// <summary>The fixture, two frames old (the replaced islands and their colliders are gone): physics picks see only the new song.</summary>
    static IEnumerator Loaded() { Prepare(); LoadFixture(); yield return null; yield return null; }

    /// <summary>Runs a coroutine body, turning an exception into a FAIL line instead of killing the run.</summary>
    static IEnumerator Safe(string name, IEnumerator body)
    {
        Progress = name;
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) yield break; cur = body.Current; }
            catch (Exception e) { Add(L(false, name, "threw", e.GetType().Name + ": " + e.Message)); yield break; }
            yield return cur;
        }
    }

    static string SafeSync(string name, Func<string> f)
    {
        Progress = name;
        try { return f(); } catch (Exception e) { return L(false, name, "threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); }
    }

    // ------------------------------------------------------------------ backups / autosave (the run leaves them as they were)
    public struct FileSnap { public byte[] data; public DateTime mtime; }
    public static Dictionary<string, FileSnap> SnapshotSaves()
    {
        var d = new Dictionary<string, FileSnap>();
        var paths = new List<string> { SongIO.AutosavePath };
        for (int i = 1; i <= SongIO.BackupSlots; i++) paths.Add(SongIO.BackupPath(i));
        foreach (var p in paths) d[p] = File.Exists(p) ? new FileSnap { data = File.ReadAllBytes(p), mtime = File.GetLastWriteTimeUtc(p) } : new FileSnap();
        return d;
    }
    public static void RestoreSaves(Dictionary<string, FileSnap> d)
    {
        if (d == null) return;
        foreach (var kv in d)
        {
            try
            {
                if (kv.Value.data == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                else { File.WriteAllBytes(kv.Key, kv.Value.data); File.SetLastWriteTimeUtc(kv.Key, kv.Value.mtime); }
                if (File.Exists(kv.Key + ".tmp")) File.Delete(kv.Key + ".tmp");
            }
            catch (Exception) { }
        }
    }

    // ================================================================== F1: seam presses
    /// <summary>F1: while drawing, presses on the seams between tiles (rays that meet the platform) extend the path; the same island's
    /// margin does nothing; the end tile again finishes: ONE cube with every node. Idle, a seam press is a tile (not an island handle).</summary>
    public static string SeamPresses()
    {
        var o = new StringBuilder();
        Prepare();
        var pm = PathManager.I;
        KeyBlock isl; int z, x0;
        if (!FreeRow(4, out isl, out z, out x0)) return L(false, "F1", "seam presses", "no free row of 4 tiles in the fixture");
        float S = ProjectConfig.Spacing;
        Frame(isl.transform.TransformPoint(new Vector3((x0 + 1.5f) * S, 0f, z * S)), 9f, 0f, 62f);
        // idle: a seam press is a tile, never an island handle
        int idleSeams = 0, idleTiles = 0, idleCubes = 0;
        for (int k = 0; k < 3; k++)
            for (int iz = 0; iz < 9; iz++)
                for (int ix = 0; ix < 5; ix++)
                {
                    Vector3 sp = Scr(isl.transform.TransformPoint(new Vector3((x0 + k) * S + 0.52f + ix * 0.02f, -0.03f, z * S - 0.4f + iz * 0.1f)));
                    if (!FirstIsPlatform(isl, Camera.main.ScreenPointToRay(sp))) continue;
                    idleSeams++;
                    var r = pm.PickAt(sp);
                    if (r.tile != null && r.island == null && r.seam) idleTiles++;
                    else if (r.cube != null && r.island == null) idleCubes++;   // a cube in front of the seam / on the tile it stands for wins, as on a tile
                }
        o.Append(L(idleSeams > 0 && idleTiles + idleCubes == idleSeams && idleTiles > 0, "F1", "idle: a press whose ray meets the platform between tiles is the nearest tile (or the cube on / in front of it), never an island handle",
                   idleSeams + " seam points: " + idleTiles + " tiles, " + idleCubes + " cubes, " + (idleSeams - idleTiles - idleCubes) + " island handles"));
        int cubes0 = Finalized(), push0 = pushes;
        Vector3 ps = Scr(isl.GetTile(x0, z).Top);
        pm.SimPointer(ps, true, true, false); pm.SimPointer(ps, false, false, true);
        bool drawing = pm.IsDrawing;
        int viaPlatform = 0; var log = new StringBuilder();
        for (int k = 1; k <= 3 && pm.IsDrawing; k++)
        {
            Vector3 sp; bool seam = SeamPoint(isl, x0 + k - 1, z, isl.GetTile(x0 + k, z), out sp);
            if (seam) viaPlatform++;
            pm.SimPointer(sp, true, true, false); pm.SimPointer(sp, false, false, true);
            log.Append(seam ? " seam" : " top").Append("->").Append(pm.currentPathTiles.Count);
        }
        // the same island's outer margin while drawing: neither finish nor extend
        Vector3 margin = Scr(isl.transform.TransformPoint(new Vector3((x0 + 1) * S, -0.03f, -0.5f - ProjectConfig.PlatformPad * 0.5f)));
        var mr = pm.PickAt(margin);
        int nBefore = pm.currentPathTiles.Count;
        pm.SimPointer(margin, true, true, false); pm.SimPointer(margin, false, false, true);
        bool marginNothing = mr.tile == null && mr.island == isl && pm.IsDrawing && pm.currentPathTiles.Count == nBefore;
        o.Append(L(marginNothing, "F1", "while drawing, a press on the same island's margin neither finishes nor extends", "pick tile " + (mr.tile != null) + " island " + (mr.island == isl) + ", drawing " + pm.IsDrawing + ", nodes " + nBefore + " -> " + pm.currentPathTiles.Count));
        Vector3 pe = Scr(isl.GetTile(x0 + 3, z).Top);
        pm.SimPointer(pe, true, true, false); pm.SimPointer(pe, false, false, true);
        var made = LastFinalized();
        bool ok = drawing && !pm.IsDrawing && Finalized() == cubes0 + 1 && made != null && made.nodes.Count == 4 && pushes - push0 == 1 && viaPlatform >= 1;
        for (int k = 0; ok && k < 4; k++) ok = made.nodes[k].island == isl && made.nodes[k].gridX == x0 + k && made.nodes[k].gridZ == z;
        o.Append(L(ok, "F1", "click A, then 3 presses on the seams between tiles (rays on the platform), then the end tile = ONE cube with all 4 nodes",
                   "cubes +" + (Finalized() - cubes0) + " nodes [" + Cells(made) + "] pushes " + (pushes - push0) + ", " + viaPlatform + "/3 presses met the platform (the old code finished there):" + log));
        return o.ToString();
    }

    // ================================================================== F5: a drag extends only after the pointer moved
    public static string DragNeedsPointerMotion()
    {
        Prepare();
        var pm = PathManager.I;
        KeyBlock isl; int z, x0;
        if (!FreeRow(3, out isl, out z, out x0)) return L(false, "F5", "drag needs pointer motion", "no free row");
        float S = ProjectConfig.Spacing;
        Vector3 f = isl.transform.TransformPoint(new Vector3((x0 + 1) * S, 0f, z * S));
        Frame(f, 9f, 0f, 62f);
        Vector3 pa = Scr(isl.GetTile(x0, z).Top);
        pm.SimPointer(pa, true, true, false);                     // press: a new path on tile x0
        Frame(f + isl.transform.right * S, 9f, 0f, 62f);           // the view slides one tile: tile x0 + 1 is under the still pointer now
        var under = pm.PickAt(pa);
        pm.SimPointer(pa, false, true, false);
        int still = pm.currentPathTiles.Count;
        pm.SimPointer(pa + new Vector3(3f, 0f, 0f), false, true, false);   // the pointer itself moves
        int moved = pm.currentPathTiles.Count;
        pm.SimPointer(pa + new Vector3(3f, 0f, 0f), false, false, true);
        if (pm.IsDrawing) pm.CancelPath();
        return L(under.tile != null && under.tile != isl.GetTile(x0, z) && still == 1 && moved >= 2, "F5", "a view sliding under a still pointer draws nothing; the pointer moving extends",
                 "tile under the still pointer " + (under.tile != null ? under.tile.gridX + "," + under.tile.gridZ : "none") + ", nodes still " + still + ", after moving " + moved);
    }

    // ================================================================== F6: while playing only the cube body picks; the outline does not re-pop
    public static string PlayingPicks()
    {
        var o = new StringBuilder();
        Prepare();
        var pm = PathManager.I;
        var c0 = PitchedCube(2);
        if (c0 == null) return L(false, "F6", "playing picks", "no pitched cube");
        var isl = c0.Island;
        Frame(isl.Center, 12f);
        var pts = new List<Vector3>();
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.Island != isl) continue;
            var t = c.CurrentOrHomeTile;
            if (t != null) for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4f; pts.Add(Scr(t.Top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.44f)); }
            Rect rc; if (pm.ScreenRectOf(c, out rc)) for (int k = 0; k < 16; k++) { float a = k * Mathf.PI / 8f; pts.Add(new Vector3(rc.center.x + Mathf.Cos(a) * (rc.width * 0.5f + 12f), rc.center.y + Mathf.Sin(a) * (rc.height * 0.5f + 12f), 0f)); }
        }
        int stoppedNear = 0; foreach (var p in pts) { var r = pm.PickAt(p); if (r.rule == 2 || r.rule == 3) stoppedNear++; }
        var body = Scr(c0.transform.position);
        GlobalClock.Seek(0); GlobalClock.Play();
        int playingNear = 0; foreach (var p in pts) { var r = pm.PickAt(p); if (r.rule == 2 || r.rule == 3) playingNear++; }
        var rb = pm.PickAt(body);
        GlobalClock.Stop();
        o.Append(L(stoppedNear > 0 && playingNear == 0 && rb.rule == 1 && rb.cube == c0, "F6", "while playing only the ray on a cube body picks it (rules 2 / 3 off); stopped they apply",
                   pts.Count + " points near cubes: rule 2/3 stopped " + stoppedNear + ", playing " + playingNear + "; body pick rule " + rb.rule));
        // the outline: shown again for the same cube within 0.3 s it does not pop again
        AudioCube other = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c != c0 && c.BodyVisible) { other = c; break; }
        CubeOutline.Show(c0);
        int p0 = CubeOutline.Pops;
        CubeOutline.Hide(); CubeOutline.Show(c0);
        int p1 = CubeOutline.Pops;
        if (other != null) CubeOutline.Show(other);
        int p2 = CubeOutline.Pops;
        CubeOutline.Hide();
        o.Append(L(p1 == p0 && (other == null || p2 == p1 + 1), "F6", "the outline shown again for the same cube within 0.3 s does not re-pop (another cube pops)", "pops " + p0 + " -> " + p1 + " -> " + p2));
        return o.ToString();
    }

    // ================================================================== F7: right-click deletes only a cube body over land
    public static string RightClick()
    {
        Prepare();
        var pm = PathManager.I;
        var c0 = PitchedCube(1);
        if (c0 == null) return L(false, "F7", "right-click", "no cube");
        Frame(c0.Island.Center, 13f);
        // a rule-3 point (12 px outside a cube's rect where no tile is under the ray)
        Vector3 near = Vector3.zero; bool found = false;
        foreach (var kb in SongManager.I.Islands)
        {
            if (kb == null || found) continue;
            Frame(kb.Center, 13f);
            foreach (var c in SequenceMaster.Cubes)
            {
                if (found || c == null || !c.isFinalized || c.Island != kb) continue;
                Rect rc; if (!pm.ScreenRectOf(c, out rc)) continue;
                for (int k = 0; k < 32 && !found; k++)
                {
                    float a = k * Mathf.PI / 16f;
                    var sp = new Vector3(rc.center.x + Mathf.Cos(a) * (rc.width * 0.5f + 12f), rc.center.y + Mathf.Sin(a) * (rc.height * 0.5f + 12f), 0f);
                    if (pm.PickAt(sp).rule == 3) { near = sp; found = true; }
                }
            }
        }
        int n0 = Finalized();
        // a real right-click is a press + release: the press zeroes the orbit's drag distance; simulated orbits of earlier suites left it high
        if (OrbitCamera.I != null) { OrbitCamera.I.SimOrbitBegin(near); OrbitCamera.I.SimOrbitEnd(); }
        if (found) pm.SimPointer(near, false, false, false, true);
        int afterNear = Finalized();
        Frame(c0.Island.Center, 13f);
        var body = Scr(c0.transform.position);
        var rb = pm.PickAt(body);
        if (OrbitCamera.I != null) { OrbitCamera.I.SimOrbitBegin(body); OrbitCamera.I.SimOrbitEnd(); }
        pm.SimPointer(body, false, false, false, true);
        int afterBody = Finalized();
        return L(found && afterNear == n0 && rb.rule == 1 && !rb.overSea && afterBody == n0 - 1, "F7", "right-click near a cube (rect + 16 px) deletes nothing; right-click on the cube body deletes it",
                 "near point " + found + ": " + n0 + " -> " + afterNear + "; body rule " + rb.rule + ": -> " + afterBody);
    }

    // ================================================================== F8 + F9
    public static string HubAndOverview()
    {
        var o = new StringBuilder();
        Prepare(); LoadFixture();
        var sm = SongManager.I; var drag = PathManager.I.Drag;
        var kb = sm.Islands[0]; int bars0 = kb.bars, p0 = pushes;
        drag.SimClick(kb, true);
        kb = sm.Islands[0];
        o.Append(L(kb.bars == bars0 && pushes == p0 && UIManager.I.SelectedMeasure == 0, "F8", "a hub click selects + frames only (no bar cycling, no History entry)", "bars " + bars0 + " -> " + kb.bars + ", pushes " + (pushes - p0) + ", selected " + UIManager.I.SelectedMeasure));
        var cam = OrbitCamera.I;
        cam.Suspended = false;
        if (cam.overview) cam.FrameAll();
        cam.FrameAll();
        bool on = cam.overview;
        var b = sm.Islands[1];
        drag.SimBegin(b, false, b.Center);
        bool cleared = !cam.overview;
        drag.Cancel();
        o.Append(L(on && cleared, "F9", "an island drag clears the overview (it would chase the bounds the drag moves)", "overview on " + on + ", after the drag began " + cam.overview));
        return o.ToString();
    }

    // ================================================================== S2: Cmd+Z mid-draw, HUD press mid-draw
    public static string CmdZMidDraw()
    {
        Prepare();
        var pm = PathManager.I;
        KeyBlock isl; int z, x0;
        if (!PickableRow(3, out isl, out z, out x0)) return L(false, "S2", "Cmd+Z mid-draw", "no free row of 3 tiles that pick themselves");
        int f0 = Finalized(), p0 = pushes, u0 = History.UndoCount;
        for (int k = 0; k < 3; k++) { var sp = Scr(isl.GetTile(x0 + k, z).Top); pm.SimPointer(sp, true, true, false); pm.SimPointer(sp, false, false, true); }
        int n3 = pm.currentPathTiles.Count;
        CmdKey(KeyCode.Z);
        int n2 = pm.currentPathTiles.Count; bool drawing2 = pm.IsDrawing;
        CmdKey(KeyCode.Z, true);   // Cmd+Shift+Z: ignored while drawing
        CmdKey(KeyCode.Y);         // Cmd+Y: ignored while drawing
        int nRedo = pm.currentPathTiles.Count;
        CmdKey(KeyCode.Z);
        int n1 = pm.currentPathTiles.Count;
        CmdKey(KeyCode.Z);         // the last node: the draft is cancelled
        bool cancelled = !pm.IsDrawing;
        bool ok = n3 == 3 && n2 == 2 && drawing2 && nRedo == 2 && n1 == 1 && cancelled && Finalized() == f0 && pushes == p0 && History.UndoCount == u0;
        return L(ok, "S2", "Cmd+Z mid-draw removes the draft's last node (one node left: the draft goes); Cmd+Shift+Z / Cmd+Y wait; no History entry",
                 "nodes " + n3 + " -> " + n2 + " (redo keys: " + nRedo + ") -> " + n1 + " -> drawing " + pm.IsDrawing + ", finalized +" + (Finalized() - f0) + ", pushes " + (pushes - p0) + ", undo stack " + u0 + " -> " + History.UndoCount);
    }

    public static string HudPressMidDraw()
    {
        Prepare();
        var pm = PathManager.I;
        KeyBlock isl; int z, x0;
        if (!PickableRow(2, out isl, out z, out x0)) return L(false, "S2", "HUD press mid-draw", "no free row of 2 tiles that pick themselves");
        int f0 = Finalized();
        for (int k = 0; k < 2; k++) { var sp = Scr(isl.GetTile(x0 + k, z).Top); pm.SimPointer(sp, true, true, false); pm.SimPointer(sp, false, false, true); }
        bool drawing = pm.IsDrawing; int p0 = pushes;
        var hud = new Vector3(30f, 30f, 0f);
        pm.SimPointer(hud, true, true, false, false, false, true);
        pm.SimPointer(hud, false, false, true, false, false, true);
        var made = LastFinalized();
        bool ok = drawing && !pm.IsDrawing && Finalized() == f0 + 1 && made != null && made.nodes.Count == 2 && pushes == p0 + 1;
        return L(ok, "S2", "a press over the HUD mid-draw finishes the path first (a rebuild behind the button can no longer drop it)",
                 "drawing " + drawing + " -> " + pm.IsDrawing + ", finalized +" + (Finalized() - f0) + " nodes " + (made != null ? made.nodes.Count : -1) + ", pushes " + (pushes - p0));
    }

    /// <summary>S2: N (add a Moon) while drawing finishes the draft first: the drawn path survives whatever N rebuilds.</summary>
    public static string NMidDraw()
    {
        Prepare();
        var pm = PathManager.I;
        KeyBlock isl; int z, x0;
        if (!PickableRow(2, out isl, out z, out x0)) return L(false, "S2", "N mid-draw", "no free row of 2 tiles that pick themselves");
        int idx = SongManager.I.Islands.IndexOf(isl), moons0 = SongManager.I.Moons.Count;
        for (int k = 0; k < 2; k++) { var sp = Scr(isl.GetTile(x0 + k, z).Top); pm.SimPointer(sp, true, true, false); pm.SimPointer(sp, false, false, true); }
        bool drawing = pm.IsDrawing;
        KeyShim.Sim(KeyCode.N, true, true, false); UIManager.I.RunHotkeysForTest(); KeyShim.Clear();
        bool kept = false;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || !c.isFinalized || c.nodes.Count != 2 || c.IsOnMoon) continue;
            bool same = true;
            for (int k = 0; k < 2 && same; k++) same = c.nodes[k] != null && c.nodes[k].island != null && c.nodes[k].island.measureIndex == idx && c.nodes[k].gridX == x0 + k && c.nodes[k].gridZ == z;
            if (same) { kept = true; break; }
        }
        return L(drawing && !pm.IsDrawing && kept, "S2", "N while drawing finishes the draft first (the path survives the Moon rebuild)", "drawing " + drawing + " -> " + pm.IsDrawing + ", path kept " + kept + ", moons " + moons0 + " -> " + SongManager.I.Moons.Count);
    }

    /// <summary>F23: the vibe prompt carries no words: the empty field shows an icon placeholder and the blinking caret.</summary>
    public static string PromptWords()
    {
        var ic = InterfaceController.I;
        if (ic == null) return L(false, "F23", "prompt words", "no prompt");
        ic.Show();
        var input = ic.GetComponentInChildren<TMPro.TMP_InputField>(true);
        var ph = input != null ? input.placeholder : null;
        bool icon = ph is UnityEngine.UI.Image;
        int words = 0; var found = new StringBuilder();
        foreach (var t in ic.GetComponentsInChildren<TMPro.TMP_Text>(true))
            if ((input == null || t != input.textComponent) && !string.IsNullOrEmpty(t.text)) { words++; found.Append(" \"").Append(t.text).Append('"'); }
        bool caret = input != null && input.caretBlinkRate > 0f;
        ic.Hide();
        return L(icon && words == 0 && caret, "F23", "the vibe prompt shows no words: an icon placeholder and the blinking caret", "placeholder " + (ph != null ? ph.GetType().Name : "none") + ", texts with words " + words + found + ", caret blink " + (input != null ? input.caretBlinkRate : 0f));
    }

    // ================================================================== S6: undo / N during an island drag
    public static string DragCancelKeys()
    {
        var o = new StringBuilder();
        Prepare(); LoadFixture();
        var sm = SongManager.I; var drag = PathManager.I.Drag;
        for (int pass = 0; pass < 2; pass++)
        {
            // v7 (SPEC v7 §13.2): inside a section the columns touch and a merge glues only across a GAP — a section boundary before column 1 first
            var stSec = SongState.Capture(); var secs = new System.Collections.Generic.List<int>(SongManager.SectionsOf(stSec)); if (!secs.Contains(1)) secs.Add(1); secs.Sort();
            stSec.sections = secs.ToArray(); sm.RebuildFromState(stSec); History.Push();
            var a = sm.Islands[0]; var b = sm.Islands[1];
            drag.SimBegin(b, false, b.Center);
            drag.SimMove(new Vector3(a.EastEdge + 0.9f + KeyBlock.IslandWidth * 0.5f, 0f, a.Center.z + 0.3f));
            bool zone = drag.MergeTarget != null && sm.Route.MergeSheetVisible;
            string key;
            if (pass == 0) { CmdKey(KeyCode.Z); key = "Cmd+Z"; }
            else { KeyShim.Sim(KeyCode.N, true, true, false); UIManager.I.RunHotkeysForTest(); KeyShim.Clear(); key = "N"; }
            bool gone = !drag.Busy && !sm.Route.MergeSheetVisible;
            if (drag.Busy) drag.Cancel();
            o.Append(L(zone && gone, "S6", key + " during an island drag cancels it and leaves no merge sheet behind", "zone " + zone + ", drag busy " + drag.Busy + ", sheet " + sm.Route.MergeSheetVisible));
            LoadFixture();
        }
        return o.ToString();
    }

    // ================================================================== F15: rotating backups never touch the user's save
    public static string BackupRotation()
    {
        var o = new StringBuilder();
        Prepare(); LoadFixture();
        var snap = SnapshotSaves();
        string userMd5 = Md5(SongIO.Path), autoMd5 = Md5(SongIO.AutosavePath);
        try
        {
            for (int i = 1; i <= SongIO.BackupSlots; i++) { var p = SongIO.BackupPath(i); if (File.Exists(p)) File.Delete(p); }
            string json = SongState.Capture().ToJson();
            int w0 = SongIO.BackupsWritten;
            var slots = new List<string>();
            for (int k = 0; k < 4; k++) { slots.Add(SongIO.Backup()); if (k == 2) System.Threading.Thread.Sleep(20); }
            bool rotated = slots[0] == SongIO.BackupPath(1) && slots[1] == SongIO.BackupPath(2) && slots[2] == SongIO.BackupPath(3) && slots[3] == SongIO.BackupPath(1);
            bool content = true; for (int i = 1; i <= 3; i++) content &= File.Exists(SongIO.BackupPath(i)) && File.ReadAllText(SongIO.BackupPath(i)) == json;
            bool noTmp = true; for (int i = 1; i <= 3; i++) noTmp &= !File.Exists(SongIO.BackupPath(i) + ".tmp");
            o.Append(L(rotated && content && noTmp && SongIO.BackupsWritten == w0 + 4, "F15", "backups rotate 1 -> 2 -> 3 -> 1 (the oldest is overwritten), each a copy of the live song",
                       string.Join(", ", slots.ConvertAll(s => s != null ? Path.GetFileName(s) : "null").ToArray())));
            FreshSong(4242);   // island 0 + the demo Moon groove only: nothing of the player's to keep
            string none = SongIO.Backup();
            o.Append(L(none == null, "F15", "a fresh song without cubes of the player's own is not backed up", none ?? "none"));
            o.Append(L(Md5(SongIO.Path) == userMd5 && Md5(SongIO.AutosavePath) == autoMd5, "F15", "backup writes never touch the user's save (md5 " + userMd5 + ") or the autosave"));
        }
        finally { RestoreSaves(snap); }
        return o.ToString();
    }

    // ================================================================== S10: atomic save round trip
    public static string AtomicSave()
    {
        Prepare(); LoadFixture();
        string userMd5 = Md5(SongIO.Path);
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, "fix_atomic_save.json");
        try { if (File.Exists(p)) File.Delete(p); if (File.Exists(p + ".tmp")) File.Delete(p + ".tmp"); } catch (Exception) { }
        bool first = SongIO.SaveTo(p);                            // a new slot: written, then moved in
        string j1 = SongState.Capture().ToJson();
        bool same1 = File.Exists(p) && File.ReadAllText(p) == j1;
        bool loop0 = GlobalClock.LoopSong;
        GlobalClock.LoopSong = !loop0;
        bool second = SongIO.SaveTo(p);                           // an existing slot: replaced whole
        string j2 = SongState.Capture().ToJson();
        bool same2 = File.ReadAllText(p) == j2 && j2 != j1;
        bool noTmp = !File.Exists(p + ".tmp");
        int islands = SongManager.I.Islands.Count, cubes = Finalized();
        GlobalClock.LoopSong = loop0;
        bool loaded = SongIO.LoadFrom(p);
        bool round = loaded && GlobalClock.LoopSong == !loop0 && SongManager.I.Islands.Count == islands && Finalized() == cubes;
        GlobalClock.LoopSong = loop0;
        try { File.Delete(p); } catch (Exception) { }
        return L(first && second && same1 && same2 && noTmp && round && Md5(SongIO.Path) == userMd5, "S10", "atomic save: new slot and overwrite both hold the whole song (temp file swapped in, none left), load round trip",
                 "first " + first + " second " + second + " same " + same1 + "/" + same2 + " no tmp " + noTmp + " round trip " + round);
    }

    // ================================================================== S4 + F21 + F20 (a fresh one-island song)
    public static string WheelHintBubble()
    {
        var o = new StringBuilder();
        Prepare(); LoadFixture();
        var ui = UIManager.I;
        ui.SelectMeasure(SongManager.I.Islands.Count - 1, false);
        var chord = Hints.RectOf("chord"); var chordBtn = chord != null ? chord.GetComponent<HudButton>() : null;
        if (chordBtn != null && chordBtn.onClick != null) chordBtn.onClick();
        bool opened = ui.WheelOpen;
        FreshSong(777);                                            // a rebuild: the wheel's island index is gone
        bool closed = !ui.WheelOpen;
        var q = ui.HudRoot.Find("ChordWheel/Wheel/Quality"); var qb = q != null ? q.GetComponent<HudButton>() : null;
        string threw = null; try { if (qb != null && qb.onClick != null) qb.onClick(); } catch (Exception e) { threw = e.GetType().Name; }
        o.Append(L(opened && closed && threw == null && !ui.WheelOpen, "S4", "the chord wheel closes when a rebuild leaves its island index stale; its picks are bounds-checked",
                   "opened " + opened + ", closed after the rebuild " + closed + ", quality click " + (threw ?? "ok")));
        Vector3 last = Hints.WorldOf("world:islandLast"), first = Hints.WorldOf("world:island0");
        o.Append(L(SongManager.I.Islands.Count == 1 && float.IsNaN(last.x) && !float.IsNaN(first.x), "F21", "with one island the \"last island\" hint target is missing (island 0 stays)", "islands " + SongManager.I.Islands.Count + ", islandLast " + last));
        // F20: a tutorial balloon lets the pointer through while a tray card is dragged
        var bub = new TutorialBubble();
        bub.Build();
        bub.SetContent("fixtest", "test", null, false, false, 0, 0);
        bub.Show(true); bub.Tick(0.5f);
        var grp = bub.Canvas.GetComponent<CanvasGroup>();
        bool blocks0 = grp.blocksRaycasts;
        IslandTray.Open();
        var tray = IslandTray.I;
        var ghost = tray != null && IslandTray.IsOpen ? tray.SimDragStart(0) : null;
        bub.Tick(0.02f);
        bool through = !grp.blocksRaycasts;
        if (tray != null && tray.Dragging) tray.SimDragRelease();
        bub.Tick(0.02f);
        bool back = grp.blocksRaycasts;
        UnityEngine.Object.Destroy(bub.Canvas.gameObject);
        IslandTray.Close();
        WorldInput.Unlock("tray");
        o.Append(L(blocks0 && ghost != null && through && back, "F20", "the tutorial balloon takes no raycasts while a tray card is dragged (the release lands in the world)", "blocks " + blocks0 + " -> while dragging " + !through + " -> after " + back));
        return o.ToString();
    }

    // ================================================================== timed checks
    static IEnumerator TrayEsc()
    {
        Prepare(); LoadFixture();
        yield return null;
        IslandTray.Open();
        yield return null; yield return null;
        bool open = IslandTray.IsOpen;
        KeyShim.Sim(KeyCode.Escape, true, true, false);
        yield return null;
        KeyShim.Clear();
        yield return null;
        Add(L(open && !IslandTray.IsOpen, "F18", "Esc closes the island tray", "open " + open + " -> " + IslandTray.IsOpen));
    }

    static IEnumerator TempoScroll()
    {
        Prepare(); LoadFixture();
        yield return null;
        var r = Hints.RectOf("tempo"); var dial = r != null ? r.GetComponent<HudDial>() : null;
        if (dial == null) { Add(L(false, "F2", "tempo dial scroll", "no tempo dial")); yield break; }
        float bpm0 = GlobalClock.BPM, v0 = dial.value; int p0 = pushes;
        for (int k = 0; k < 12; k++)
        {
            dial.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, 0.25f) });
            if (k % 4 == 1) dial.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(1f, 0.2f) });   // a sideways swipe: ignored
        }
        float v1 = dial.value; int during = pushes - p0; bool pending = dial.ScrollPending;
        yield return Wait(0.55f);
        int after = pushes - p0;
        Add(L(Mathf.Abs(v1 - (v0 + 3f)) < 1e-3f && during == 0 && pending && after == 1 && !dial.ScrollPending, "F2", "trackpad scroll on a dial accumulates (12 x 0.25 = 3 steps, sideways ignored) and pushes History once ~0.35 s after the last event",
              "value " + v0 + " -> " + v1 + ", pushes during " + during + ", after 0.55 s " + after));
        GlobalClock.SetBPM(bpm0); dial.Set(bpm0, false); History.Push();
    }

    static IEnumerator Inspector()
    {
        Prepare(); LoadFixture();
        yield return null;
        var pm = PathManager.I; var ui = UIManager.I;
        var c = PitchedCube(3);
        if (c == null) { Add(L(false, "F13", "inspector checks", "no pitched cube with 3 nodes")); yield break; }
        Frame(c.Island.Center, 12f);
        CubeInspector.Open(c);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open && ui.CardShown > 0.97f, 4f);
        bool open = CubeInspector.IsOpen && CubeInspector.Current == c;
        // F19 / S8: no tray while inspecting (no flicker, no TrayOpened)
        int ev0 = events.FindAll(e => e == Onboarding.Ev.TrayOpened).Count;
        IslandTray.Open(); IslandTray.Toggle();
        yield return null;
        int ev1 = events.FindAll(e => e == Onboarding.Ev.TrayOpened).Count;
        Add(L(open && !IslandTray.IsOpen && ev1 == ev0, "F19", "the tray does not open (I / + / Toggle) while inspecting and fires no TrayOpened", "open " + IslandTray.IsOpen + ", events +" + (ev1 - ev0)));
        // F22: a palette click recolours the inspected cube, the brush stays
        var pal = ui.HudRoot.Find("Palette");
        var picks = new List<HudButton>();
        if (pal != null) foreach (var b in pal.GetComponentsInChildren<HudButton>(true)) if (b.name == "Pick") picks.Add(b);
        int brush = pm.selectedInstrument, k = -1;
        for (int i = 0; i < picks.Count && k < 0; i++) if (i != c.instrument && i != brush && !Instruments.IsDrums(i)) k = i;
        int p0 = pushes, inst0 = c.instrument;
        if (k >= 0) picks[k].onClick();
        Add(L(k >= 0 && c.instrument == k && pm.selectedInstrument == brush && pushes == p0 + 1, "F22", "a palette click while inspecting recolours the inspected cube (the brush stays), one History entry",
              "instrument " + inst0 + " -> " + c.instrument + " (picked " + k + "), brush " + brush + " -> " + pm.selectedInstrument + ", pushes " + (pushes - p0)));
        var g = ui.Grid;
        // F13: hold 0.5 s opens the popover; dragging on closes it and moves the node
        int mx = -1, mz = -1;
        for (int zz = 0; zz < g.Rows && mx < 0; zz++) for (int xx = 0; xx < g.Cols && mx < 0; xx++) { bool on = false; foreach (var t in c.nodes) if (t != null && t.gridX == xx && t.gridZ == zz) on = true; if (!on) { mx = xx; mz = zz; } }
        p0 = pushes;
        g.SimDownNode(1); g.SimHold();
        bool pop = g.Popover != null;
        g.SimMoveCell(mx, mz);
        bool closedMoved = g.Popover == null && c.nodes[1].gridX == mx && c.nodes[1].gridZ == mz;
        g.SimUp();
        Add(L(PathGridView.HoldSeconds >= 0.5f - 1e-4f && pop && closedMoved && pushes == p0 + 1, "F13", "grid: the hold (0.5 s) opens the sticker popover; dragging on closes it and moves the node (one History entry)",
              "popover " + pop + ", closed + moved " + closedMoved + " [" + Cells(c) + "], pushes " + (pushes - p0)));
        yield return null;
        // F18 / S5: a grid gesture cut short by the card closing is committed
        int ex = -1, ez = -1;
        for (int zz = 0; zz < g.Rows && ex < 0; zz++) for (int xx = 0; xx < g.Cols && ex < 0; xx++) { bool on = false; foreach (var t in c.nodes) if (t != null && t.gridX == xx && t.gridZ == zz) on = true; if (!on) { ex = xx; ez = zz; } }
        int n0 = c.nodes.Count; p0 = pushes;
        g.SimDownCell(ex, ez);
        g.gameObject.SetActive(false);
        bool committed = pushes == p0 + 1 && c.nodes.Count == n0 + 1;
        g.gameObject.SetActive(true);
        yield return null;
        Add(L(committed, "F18", "grid: a gesture cut short by the card closing (OnDisable) is committed, not dropped", "nodes " + n0 + " -> " + c.nodes.Count + ", pushes " + (pushes - p0)));
        // S5: the volume slider cut short
        var vr = Hints.RectOf("inspector.volume"); var vol = vr != null ? vr.GetComponent<HudSlider>() : null;
        if (vol != null && vol.track != null)
        {
            float before = c.volume; p0 = pushes;
            var tr = vol.track; Rect rr = tr.rect;
            Vector3 w = tr.TransformPoint(new Vector3(rr.xMin + rr.width * (before > 0.5f ? 0.25f : 0.75f), rr.center.y, 0f));
            vol.OnPointerDown(new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, w) });
            float mid = c.volume;
            vol.gameObject.SetActive(false);
            bool pushed = pushes == p0 + 1;
            vol.gameObject.SetActive(true);
            Add(L(Mathf.Abs(mid - before) > 0.05f && pushed, "S5", "the volume slider cut short by the card closing still pushes History", "volume " + before.ToString("F2") + " -> " + mid.ToString("F2") + ", pushes " + (pushes - p0)));
        }
        else Add(L(false, "S5", "volume slider", "not found"));
        // F2: the necklace scroll accumulates, one push after the pause; S5: cut short it still pushes
        var dr = Hints.RectOf("inspector.necklace"); var dial = dr != null ? dr.GetComponent<NecklaceDial>() : null;
        if (dial != null)
        {
            int h0 = dial.EffectiveHits, n = c.StepsPerBar, dir = h0 > n / 2 ? -1 : 1;
            p0 = pushes;
            for (int s = 0; s < 8; s++)
            {
                dial.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, 0.25f * dir) });
                if (s == 3) dial.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(-1f, 0.3f) });   // sideways: ignored
            }
            int h1 = dial.EffectiveHits; int during = pushes - p0; bool pending = dial.ScrollPending;
            yield return Wait(0.55f);
            int after = pushes - p0;
            Add(L(h1 == Mathf.Clamp(h0 + 2 * dir, 1, n) && during == 0 && pending && after == 1, "F2", "necklace scroll: 8 x 0.25 = 2 steps (sideways ignored), one History entry ~0.35 s after the last event",
                  "hits " + h0 + " -> " + h1 + " (n " + n + "), pushes during " + during + ", after " + after));
            p0 = pushes;
            for (int s = 0; s < 4; s++) dial.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, -0.25f * dir) });
            bool pend2 = dial.ScrollPending;
            dial.gameObject.SetActive(false);
            int cut = pushes - p0;
            dial.gameObject.SetActive(true);
            Add(L(pend2 && cut == 1, "S5", "a necklace scroll cut short by the card closing still pushes History once", "pending " + pend2 + ", pushes " + cut));
        }
        else Add(L(false, "F2", "necklace", "not found"));
        // F11: the landing flash is capped and never strobes; landings (and squashes) go on
        c.SetStep(StepLen.Sixteenth); c.SetHits(-1); History.Push();
        int landings = 0;
        Action<AudioCube, int, int, AudioCube.Hit> onLand = (cc, w, st, hit) => { if (cc == c && hit.fires) landings++; };
        AudioCube.OnLanded += onLand;
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        FocusLoop.Dismiss();   // v4: opening the inspector starts the focus loop; this check plays the cube's own window straight
        GlobalClock.Seek(c.windows[0].start); GlobalClock.Play();
        float maxGlow = 0f, prev = 0f, lastOnset = -9f, minGap = 9f; int onsets = 0;
        float t0 = Time.unscaledTime;   // the flash gate's own clock (frame time), so one slow frame cannot shorten a measured gap
        while (Time.unscaledTime - t0 < 2.5f)
        {
            yield return null;
            float gl = CubeInspector.FlashGlow;
            maxGlow = Mathf.Max(maxGlow, gl);
            if (gl > prev + 0.04f) { float now = Time.unscaledTime; if (lastOnset > 0f) minGap = Mathf.Min(minGap, now - lastOnset); lastOnset = now; onsets++; }
            prev = gl;
        }
        GlobalClock.Stop();
        AudioCube.OnLanded -= onLand;
        Add(L(maxGlow <= CubeInspector.FlashAddMax + 1e-4f && onsets >= 2 && (onsets < 2 || minGap >= CubeInspector.FlashGap - 0.03f) && landings >= onsets + 3, "F11", "inspector: the landing flash adds at most 0.3 and never twice within 0.33 s (16ths: most landings squash without a flash)",
              "landings " + landings + ", flashes " + onsets + ", min gap " + minGap.ToString("F2") + " s, max glow add " + maxGlow.ToString("F3")));
        Add(L(Synth.LateEvents == late0 && Synth.Errors == err0, "F11", "Synth late / errors 0 while the inspected cube plays", Synth.Stats()));
        CubeInspector.CloseImmediate();
        yield return null;
    }

    static IEnumerator CameraChecks()
    {
        Prepare(); LoadFixture();
        var cam = OrbitCamera.I;
        cam.Suspended = false;
        cam.FocusMeasure(0, true);
        yield return null; yield return null;
        Vector2 oc = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        // F12: a fast flick coasts at most 110°/s; yaw per pixel is DPI-aware
        float yaw0 = cam.Yaw; float dx = 0f; float yawAtStart = float.NaN, dxAtStart = 0f;
        cam.SimOrbitBegin(oc);
        for (int k = 1; k <= 10; k++)
        {
            dx += 40f; cam.SimOrbitMove(oc + new Vector2(dx, 0f));
            yield return null;
            if (float.IsNaN(yawAtStart) && cam.Orbiting) { yawAtStart = cam.Yaw; dxAtStart = dx; }
        }
        float perPx = float.IsNaN(yawAtStart) ? 0f : Mathf.DeltaAngle(yawAtStart, cam.Yaw) / Mathf.Max(1f, dx - dxAtStart);
        cam.SimOrbitEnd();
        float yv = cam.YawVelocity;
        float wantPerPx = OrbitCamera.OrbitDegPerPx / OrbitCamera.DpiScale;
        Add(L(Mathf.Abs(yv) <= OrbitCamera.MaxReleaseYawSpeed + 0.01f && Mathf.Abs(yv) > 20f && Mathf.Abs(perPx - wantPerPx) < 0.02f * wantPerPx + 1e-4f, "F12", "orbit: a fast flick coasts at most 110°/s; degrees per pixel scale with the DPI",
              "release " + yv.ToString("F0") + "°/s, " + perPx.ToString("F4") + "°/px (want " + wantPerPx.ToString("F4") + ", dpi " + Screen.dpi + ", scale " + OrbitCamera.DpiScale.ToString("F2") + ", drag threshold " + OrbitCamera.DragThresholdPx.ToString("F1") + " px)"));
        yield return WaitFor(() => cam.YawVelocity == 0f, 3f);
        // F4: Q / E held orbit ±90°/s
        float y0 = cam.Yaw; float tq = Time.realtimeSinceStartup;
        OrbitCamera.SimKeyHold(KeyCode.Q, true);
        yield return Wait(0.5f);
        OrbitCamera.SimKeyHold(KeyCode.Q, false);
        float rateQ = Mathf.DeltaAngle(y0, cam.Yaw) / (Time.realtimeSinceStartup - tq);
        yield return null;
        float y1 = cam.Yaw; float te = Time.realtimeSinceStartup;
        OrbitCamera.SimKeyHold(KeyCode.E, true);
        yield return Wait(0.5f);
        OrbitCamera.SimKeyHold(KeyCode.E, false);
        float rateE = Mathf.DeltaAngle(y1, cam.Yaw) / (Time.realtimeSinceStartup - te);
        Add(L(rateQ < -70f && rateQ > -100f && rateE > 70f && rateE < 100f, "F4", "Q / E held orbit the view at about ∓90°/s", "Q " + rateQ.ToString("F0") + "°/s, E " + rateE.ToString("F0") + "°/s"));
        // F4: C resets the view (Q no longer does)
        OrbitCamera.SimKeyDown(OrbitCamera.ResetViewKey);
        yield return Wait(1.0f);
        Add(L(OrbitCamera.ResetViewKey == KeyCode.C && Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, OrbitCamera.HomeYaw)) < 0.5f && Mathf.Abs(cam.Pitch - OrbitCamera.HomePitch) < 0.5f && Mathf.Abs(cam.Dist - cam.HomeDistance) < 0.3f,
              "F4", "C resets the view (yaw / pitch / distance home)", "yaw " + cam.Yaw.ToString("F1") + " pitch " + cam.Pitch.ToString("F1") + " dist " + cam.Dist.ToString("F1")));
        // F4: a scroll gesture keeps its first axis; a pause starts a new gesture
        yield return Wait(0.3f);
        float d0 = cam.DistT; Vector3 f0 = cam.Focus;
        cam.SimScroll(new Vector2(1f, 0.3f), oc);
        yield return null;
        cam.SimScroll(new Vector2(0.2f, 1f), oc);                  // same gesture: still a sideways pan, no zoom
        yield return null;
        float d1 = cam.DistT; float panned = (cam.Focus - f0).magnitude;
        yield return Wait(0.3f);
        cam.SimScroll(new Vector2(0.1f, 1f), oc);                  // a new gesture: zoom
        yield return null;
        float d2 = cam.DistT;
        Add(L(Mathf.Abs(d1 - d0) < 1e-3f && panned > 0.05f && d2 < d1 - 1e-3f, "F4", "a trackpad scroll gesture keeps the axis it started on (a pause starts a new one)",
              "dist " + d0.ToString("F2") + " -> " + d1.ToString("F2") + " (panned " + panned.ToString("F2") + " u) -> after a pause " + d2.ToString("F2")));
        cam.ResetView();
        yield return Wait(0.6f);
    }

    static IEnumerator Generator()
    {
        Prepare(); LoadFixture();
        yield return null;
        var ic = InterfaceController.I; var gen = ic != null ? ic.generator : null;
        if (ic == null || gen == null || ic.DiceButton == null || ic.CloseButton == null) { Add(L(false, "F3", "generator cancel", "no prompt / generator")); yield break; }
        var snap = SnapshotSaves();
        // F3 / S1: ✕ while the dice song is on its way cancels it
        string before = SongState.Capture().ToJson(); int undo0 = History.UndoCount, w0 = SongIO.BackupsWritten, d0 = gen.Dropped;
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return null;
        ic.Show();
        yield return null;
        ic.DiceButton.onClick();
        bool busy = ic.Busy && gen.IsGenerating;
        int id = gen.RequestId;
        ic.CloseButton.onClick();
        bool cancelled = !gen.IsGenerating && !ic.Busy && !ic.Visible && !WorldInput.IsLockedBy("prompt");
        yield return Wait(0.9f);
        bool unchanged = SongState.Capture().ToJson() == before && History.UndoCount == undo0 && GlobalClock.IsPlaying && !MainMenu.IsShown && SongIO.BackupsWritten == w0;
        Add(L(busy && cancelled && unchanged, "F3", "✕ while generating cancels: the song, History and playback stay (no swap, no stop, no leave)",
              "busy " + busy + ", cancelled " + cancelled + ", song same " + (SongState.Capture().ToJson() == before) + ", undo " + undo0 + " -> " + History.UndoCount + ", playing " + GlobalClock.IsPlaying));
        // a late online answer for the cancelled request is dropped (no network: a finished response is fed through the same path)
        string content = "{\\\"songName\\\":\\\"late\\\",\\\"bpm\\\":99,\\\"timeSignature\\\":\\\"4/4\\\",\\\"measures\\\":[{\\\"index\\\":1,\\\"chordKey\\\":\\\"Cmaj7\\\",\\\"chordRootMIDI\\\":60,\\\"semitones\\\":[0,4,7,11],\\\"measureDuration\\\":4.0}]}";
        string raw = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + content + "\"}}]}";
        bool applied = gen.SimResponse(raw, id);
        Add(L(!applied && gen.Dropped > d0 && SongState.Capture().ToJson() == before, "S1", "a late result for a cancelled request id is dropped (the song stays)", "applied " + applied + ", dropped " + (gen.Dropped - d0)));
        GlobalClock.Stop();
        // F15: a dice song that replaces a song with cubes keeps a backup of it first
        LoadFixture();
        yield return null;
        string fixture = SongState.Capture().ToJson(); int w1 = SongIO.BackupsWritten;
        ic.Show();
        yield return null;
        ic.DiceButton.onClick();
        yield return WaitFor(() => !gen.IsGenerating && !ic.Busy, 4f);
        yield return null;
        string newest = null; DateTime best = DateTime.MinValue;
        for (int i = 1; i <= SongIO.BackupSlots; i++) { var p = SongIO.BackupPath(i); if (File.Exists(p) && File.GetLastWriteTimeUtc(p) > best) { best = File.GetLastWriteTimeUtc(p); newest = p; } }
        bool kept = SongIO.BackupsWritten == w1 + 1 && newest != null && File.ReadAllText(newest) == fixture && SongManager.I.Islands.Count == 1;
        Add(L(kept, "F15", "the HUD sparkle's dice song replaces a song with cubes only after copying it to a backup", "backups +" + (SongIO.BackupsWritten - w1) + ", newest " + (newest != null ? Path.GetFileName(newest) : "none") + ", islands now " + SongManager.I.Islands.Count));
        if (ic.Visible) ic.Close(false);
        RestoreSaves(snap);
        GlobalClock.Stop();
    }

    static IEnumerator PresenterChecks()
    {
        Prepare(); FreshSong(515);
        yield return null;
        var field = typeof(Presenter).GetField("replaying", BindingFlags.NonPublic | BindingFlags.Instance);
        Presenter.Enter();
        yield return Wait(0.3f);
        bool active = Presenter.Active;
        if (Presenter.I != null && field != null) field.SetValue(Presenter.I, true);   // a replay cut short by the exit
        Presenter.Exit();
        yield return WaitFor(() => !Presenter.Active && !WorldInput.IsLockedBy("present"), 5f);
        yield return Wait(0.6f);
        Presenter.Enter();
        bool leak = Presenter.I != null && field != null && (bool)field.GetValue(Presenter.I);
        Add(L(active && field != null && !leak && Presenter.Active, "S7", "a replay left running at exit does not leak into the next presentation", "replaying after re-entry " + leak));
        // S9: no one-off tips over the presentation
        bool sup = Onboarding.Suppressed; int done = PlayerPrefs.GetInt(Onboarding.PrefDone, 0), tip = PlayerPrefs.GetInt(Onboarding.PrefTip + "play", -1);
        Onboarding.Suppressed = false; PlayerPrefs.SetInt(Onboarding.PrefDone, 1); PlayerPrefs.DeleteKey(Onboarding.PrefTip + "play");
        Onboarding.Notify(Onboarding.Ev.PlayStarted);
        bool noTip = Onboarding.ActiveTip == null && PlayerPrefs.GetInt(Onboarding.PrefTip + "play", 0) == 0;
        Onboarding.Suppressed = sup; PlayerPrefs.SetInt(Onboarding.PrefDone, done);
        if (tip < 0) PlayerPrefs.DeleteKey(Onboarding.PrefTip + "play"); else PlayerPrefs.SetInt(Onboarding.PrefTip + "play", tip);
        PlayerPrefs.Save();
        Add(L(noTip, "S9", "a one-off tip is not shown (or used up) over the presentation", "tip " + (Onboarding.ActiveTip ?? "none")));
        Presenter.Exit();
        yield return WaitFor(() => !Presenter.Active && !WorldInput.IsLockedBy("present"), 5f);
        yield return Wait(0.8f);
        GlobalClock.Stop();
    }

    static IEnumerator ContinueFallback()
    {
        Prepare(); LoadFixture();
        yield return null;
        string userMd5 = Md5(SongIO.Path);
        var snap = SnapshotSaves();
        MainMenu.Show();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 12f);
        yield return Wait(0.3f);
        bool shown = MainMenu.IsShown;
        File.WriteAllText(SongIO.AutosavePath, "{}");              // a broken newest slot (parses, holds no song: no log noise)
        File.SetLastWriteTimeUtc(SongIO.AutosavePath, DateTime.UtcNow);
        var cont = MainMenu.ContinueButton;
        if (cont != null) cont.Invoke();
        yield return WaitFor(() => !MainMenu.IsShown && !WorldInput.WorldLocked, 6f);
        yield return Wait(0.4f);
        bool loaded = !MainMenu.IsShown && SongManager.I.Islands.Count == 6 && Finalized() == 13;
        Add(L(shown && loaded && Md5(SongIO.Path) == userMd5, "S10", "Continue: a newest slot that fails to load falls back to the other slot (the user's save, read only)",
              "menu " + shown + ", loaded " + SongManager.I.Islands.Count + " islands / " + Finalized() + " cubes, user save md5 same " + (Md5(SongIO.Path) == userMd5)));
        RestoreSaves(snap);
        GlobalClock.Stop();
        Prepare();
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine()
    {
        sb = new StringBuilder(); pass = 0; fail = 0; pushes = 0; logErrors = 0; logWarnings = 0; envWarnings = 0; logLines.Clear(); events.Clear();
        bool suppressed = Onboarding.Suppressed;
        string userMd5 = Md5(SongIO.Path);
        var saves = SnapshotSaves();
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        History.OnChanged += OnHistory; Onboarding.OnEvent += OnEvent; Application.logMessageReceived += OnLog;
        Prepare();
        yield return null; yield return null;
        sb.Append("INFO screen ").Append(Screen.width).Append('x').Append(Screen.height).Append(", dpi ").Append(Screen.dpi).Append(", DpiScale ").Append(OrbitCamera.DpiScale.ToString("F2"))
          .Append(", drag threshold ").Append(OrbitCamera.DragThresholdPx.ToString("F1")).Append(" px\n");

        yield return Loaded(); Add(SafeSync("F1", SeamPresses));
        yield return Loaded(); Add(SafeSync("F5", DragNeedsPointerMotion));
        yield return Loaded(); Add(SafeSync("F6", PlayingPicks));
        yield return Loaded(); Add(SafeSync("F7", RightClick));
        Add(SafeSync("F8/F9", HubAndOverview)); yield return null;
        yield return Loaded(); Add(SafeSync("S2a", CmdZMidDraw));
        yield return Loaded(); Add(SafeSync("S2b", HudPressMidDraw)); yield return null;
        yield return Loaded(); Add(SafeSync("S2c", NMidDraw)); yield return null;
        Add(SafeSync("F23", PromptWords)); yield return null;
        Add(SafeSync("S6", DragCancelKeys)); yield return null;
        Add(SafeSync("F15", BackupRotation)); yield return null;
        Add(SafeSync("S10", AtomicSave)); yield return null;
        Add(SafeSync("S4/F21/F20", WheelHintBubble)); yield return null;
        yield return Safe("F18 tray", TrayEsc());
        yield return Safe("F2 tempo", TempoScroll());
        yield return Safe("inspector", Inspector());
        yield return Safe("camera", CameraChecks());
        yield return Safe("generator", Generator());
        yield return Safe("presenter", PresenterChecks());
        yield return Safe("continue", ContinueFallback());

        Progress = "finish";
        Prepare();
        LoadFixture();
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.FocusMeasure(0, true); }
        PathManager.SimOnly = false;
        History.OnChanged -= OnHistory; Onboarding.OnEvent -= OnEvent; Application.logMessageReceived -= OnLog;
        RestoreSaves(saves);
        Onboarding.Suppressed = suppressed;
        Add(L(Synth.LateEvents == late0 && Synth.Errors == err0, "RUN", "Synth late / errors unchanged over the run", Synth.Stats()));
        Add(L(Md5(SongIO.Path) == userMd5, "RUN", "the user's save is untouched", userMd5));
        Add(L(logErrors == 0 && logWarnings == 0, "RUN", "no console errors or warnings during the run", logErrors + " errors, " + logWarnings + " warnings" + (envWarnings > 0 ? " (+ " + envWarnings + " synth audio-environment watchdog warnings, not counted)" : "") + (logLines.Count > 0 ? ": " + string.Join(" || ", logLines.ToArray()) : "")));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true; Progress = "done";
    }
}
