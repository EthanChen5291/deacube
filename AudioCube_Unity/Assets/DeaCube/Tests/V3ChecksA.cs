using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Package A (SPEC v3 §2) Play-mode checks: <c>V3ChecksA.Run()</c> starts a coroutine; poll <c>V3ChecksA.Done</c> and read
/// <c>V3ChecksA.Report</c> (numbered PASS / FAIL lines). Covers click-mode drawing (one cube), the hover candidate rule
/// (synthetic screen points), the inspector lifecycle (locks, visibility, selection, proxy placement, close, delete, reopen,
/// undo re-target), every grid gesture (lists aligned, one History entry each), the cube hotkeys on the inspected cube and
/// Synth late / errors while inspecting a playing song. Captures: Captures/a_hover.png, a_inspect.png, a_grid_edit.png.
/// The fixture is loaded (never the user's save); the camera is posed directly while OrbitCamera is suspended.
/// </summary>
public static class V3ChecksA
{
    public static string Report = "";
    public static bool Done;
    static int n;
    static StringBuilder sb;
    static int pushes;
    static int invariantBad, invariantFrames; static bool sampling; static string invariantFirst;
    static readonly List<string> events = new List<string>();

    /// <summary>Capture file suffix (e.g. "_small" for a run at the docked Game view size).</summary>
    public static string Suffix = "";

    public static string Run(bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null || UIManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = "";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        CubeInspector.Ensure().StartCoroutine(Routine(captures));
        return "started";
    }

    static void Line(bool ok, string name, string detail = null)
    {
        n++;
        sb.Append(ok ? "PASS " : "FAIL ").Append("A").Append(n).Append(' ').Append(name);
        if (!string.IsNullOrEmpty(detail)) sb.Append(": ").Append(detail);
        sb.Append('\n');
    }
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); }

    static void OnHistory() { pushes++; }
    static void OnEvent(string e) { events.Add(e); }

    // ------------------------------------------------------------------ helpers
    static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();   // v4: opening the inspector begins the focus loop (it stays until dismissed)
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop();
        GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void Frame(Vector3 focus, float dist, float yaw = -24f, float pitch = 52f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    static Vector3 Scr(Vector3 world) => Camera.main.WorldToScreenPoint(world);
    static bool OnScreen(Vector3 s) => s.z > 0f && s.x > 4f && s.y > 4f && s.x < Screen.width - 4f && s.y < Screen.height - 4f;

    static string Aligned(AudioCube c)
    {
        int k = c.nodes.Count;
        if (c.mods.Count != k || c.rests.Count != k || c.gridX.Count != k || c.gridZ.Count != k) return "lists " + k + "/" + c.mods.Count + "/" + c.rests.Count + "/" + c.gridX.Count + "/" + c.gridZ.Count;
        for (int i = 0; i < k; i++)
        {
            var t = c.nodes[i];
            if (t == null) return "null node " + i;
            if (c.rests[i] != (c.mods[i] == 1)) return "rest " + i;
            if (c.gridX[i] != t.gridX || c.gridZ[i] != t.gridZ) return "grid offsets " + i;
        }
        var st = c.ToState();
        for (int i = 0; i < k; i++) if (st.xs[i] != c.gridX[i] || st.zs[i] != c.gridZ[i] || st.mods[i] != c.mods[i]) return "state " + i;
        return null;
    }

    static string Cells(AudioCube c)
    {
        var s = new StringBuilder();
        for (int i = 0; i < c.nodes.Count; i++) { if (i > 0) s.Append(' '); s.Append(c.nodes[i].gridX).Append(',').Append(c.nodes[i].gridZ); if (c.ModOf(i) != 0) s.Append('#').Append(c.ModOf(i)); }
        return s.ToString();
    }

    static bool OnPath(AudioCube c, int x, int z) { foreach (var t in c.nodes) if (t != null && t.gridX == x && t.gridZ == z) return true; return false; }

    /// <summary>Screen distance (px) between the proxy's centre and the focus backdrop's centre (NaN when either is missing).</summary>
    static float CentreErrPx(Camera cam)
    {
        var p = CubeInspector.Proxy; var f = CubeInspector.Focus;
        if (p == null || f == null) return float.NaN;
        Vector3 a = cam.WorldToScreenPoint(p.position), b = cam.WorldToScreenPoint(f.position);
        return new Vector2(a.x - b.x, a.y - b.y).magnitude;
    }

    static AudioCube PitchedCube(Func<AudioCube, bool> extra = null)
    {
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && c.Island != null && c.nodes.Count >= 2 && (extra == null || extra(c))) return c;
        return null;
    }

    static int CountNamed(string name) { int k = 0; foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (go.name == name) k++; return k; }

    static void Shoot(string file)
    {
        Directory.CreateDirectory(V2Checks.CapturePath);
        if (!string.IsNullOrEmpty(Suffix)) file = file.Replace(".png", Suffix + ".png");
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        Info("capture " + p + " (" + Screen.width + "x" + Screen.height + ")");
    }

    static IEnumerator Sampler()
    {
        while (sampling)
        {
            invariantFrames++;
            bool sel = PathManager.I != null && PathManager.I.selectedCube != null;
            if (sel != CubeInspector.IsOpen) { invariantBad++; if (invariantFirst == null) invariantFirst = "frame " + invariantFrames + ": selected " + sel + " open " + CubeInspector.IsOpen + " state " + CubeInspector.State; }
            yield return null;
        }
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator ShootAfter(string file, float s) { yield return Wait(s); Shoot(file); }
    /// <summary>Waits for a condition (game time can lag real time when the editor stalls a frame, so flights are awaited by state).</summary>
    static IEnumerator WaitFor(Func<bool> cond, float timeout) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < timeout) yield return null; }
    static IEnumerator Opened() { yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open, 4f); }
    static IEnumerator Closed() { yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 4f); }
    static IEnumerator CardIn() { yield return WaitFor(() => InspectorCard.Shown > 0.97f && CubeInspector.Dim > 0.99f, 4f); }   // v4: the card is InspectorCard (U2)
    /// <summary>Where the report is also written when the run ends (the Captures folder, never the user's save).</summary>
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "v3checksA_report.txt");

    static void Key(KeyCode k)
    {
        KeyShim.Sim(k, true, true, false);
        UIManager.I.RunHotkeysForTest();
        PathManager.I.RunHotkeysForTest();
        KeyShim.Clear();
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); n = 0; events.Clear();
        invariantBad = 0; invariantFrames = 0; invariantFirst = null;
        bool suppressed = Onboarding.Suppressed;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        History.OnChanged += OnHistory; Onboarding.OnEvent += OnEvent;
        var cam = Camera.main;
        var pm = PathManager.I;
        try { LoadFixture(); } catch (Exception e) { sb.Append("FAIL fixture: ").Append(e.Message).Append('\n'); }
        yield return null; yield return null;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);   // the real mouse stays out of the way
        var sm = SongManager.I;
        Info("fixture: " + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes; screen " + Screen.width + "x" + Screen.height);

        // ================================================================== 1. drawing (SPEC v3 §2.6): click mode and drag mode make ONE cube each
        {
            KeyBlock isl = null; int zRow = -1, x0 = -1;
            foreach (var kb in sm.Islands)
            {
                if (kb == null || kb.IsMoon || kb.cols < 5) continue;
                for (int z = 0; z < kb.rows && zRow < 0; z++)
                    for (int x = 0; x + 4 < kb.cols && zRow < 0; x++)
                    {
                        bool free = true;
                        for (int k = 0; k < 5; k++) if (PathManager.TopCubeOn(kb.GetTile(x + k, z)) != null) { free = false; break; }
                        if (free) { zRow = z; x0 = x; }
                    }
                if (zRow >= 0) { isl = kb; break; }
            }
            if (isl == null) Line(false, "click-mode drawing", "no free row of 5 tiles in the fixture");
            else
            {
                Frame(isl.Center, 12f);
                yield return null;
                var pts = new Vector3[5];
                for (int k = 0; k < 5; k++) pts[k] = Scr(isl.GetTile(x0 + k, zRow).Top);
                var pr = pm.PickAt(pts[0]);
                int islIdx = sm.Islands.IndexOf(isl);   // v4: taken before drawing (a melody longer than its island grows the column and rebuilds the islands)
                int cubes0 = SequenceMaster.Cubes.Count; int push0 = pushes;
                pm.SimPointer(pts[0], true, true, false); pm.SimPointer(pts[0], false, false, true);
                bool drawing1 = pm.IsDrawing;
                pm.SimPointer(pts[1], true, true, false); pm.SimPointer(pts[1], false, false, true);
                pm.SimPointer(pts[4], true, true, false); pm.SimPointer(pts[4], false, false, true);
                bool drawing3 = pm.IsDrawing;
                pm.SimPointer(pts[4], true, true, false); pm.SimPointer(pts[4], false, false, true);
                AudioCube made = SequenceMaster.Cubes.Count > 0 ? SequenceMaster.Cubes[SequenceMaster.Cubes.Count - 1] : null;
                string cells = made != null ? Cells(made) : "none";
                bool ok = pr.rule == 4 && pr.tile == isl.GetTile(x0, zRow) && drawing1 && drawing3 && !pm.IsDrawing && SequenceMaster.Cubes.Count == cubes0 + 1
                          && made != null && made.nodes.Count == 5 && made.isFinalized && pm.selectedCube == null && pushes - push0 == 1;
                for (int k = 0; ok && k < 5; k++) ok = made.nodes[k].gridX == x0 + k && made.nodes[k].gridZ == zRow;
                Line(ok, "click-mode drawing: click A, its neighbour, a tile 3 further, the end again = ONE cube, 5 nodes in a line, not selected, one History entry",
                     "cubes +" + (SequenceMaster.Cubes.Count - cubes0) + " nodes [" + cells + "] pushes " + (pushes - push0) + " start rule " + pr.rule);
                // drag stroke on the same row: press, drag across two tiles (a fast drag that skips one is filled), release = one cube
                LoadFixture(); yield return null;
                isl = sm.Islands[islIdx];
                Frame(isl.Center, 12f); yield return null;
                for (int k = 0; k < 5; k++) pts[k] = Scr(isl.GetTile(x0 + k, zRow).Top);
                cubes0 = SequenceMaster.Cubes.Count; push0 = pushes;
                pm.SimPointer(pts[0], true, true, false);
                pm.SimPointer(pts[1], false, true, false);
                pm.SimPointer(pts[3], false, true, false);   // skips tile 2: filled along the line
                pm.SimPointer(pts[3], false, false, true);
                made = SequenceMaster.Cubes.Count > 0 ? SequenceMaster.Cubes[SequenceMaster.Cubes.Count - 1] : null;
                ok = !pm.IsDrawing && SequenceMaster.Cubes.Count == cubes0 + 1 && made != null && made.nodes.Count == 4 && pushes - push0 == 1;
                Line(ok, "drag-mode drawing: press + drag across tiles (one skipped) + release = ONE cube with the skipped tile filled",
                     "cubes +" + (SequenceMaster.Cubes.Count - cubes0) + " nodes [" + (made != null ? Cells(made) : "none") + "] pushes " + (pushes - push0));
            }
        }

        // ================================================================== 2. hover candidate rule (SPEC v3 §2.1)
        LoadFixture(); yield return null; yield return null;
        AudioCube hc = PitchedCube(c => SequenceMaster.IndexOn(c.CurrentOrHomeTile, c) == 0);
        if (hc == null) Line(false, "hover rule", "no resting pitched cube");
        else
        {
            var tile = hc.CurrentOrHomeTile;
            Frame(tile.Top, 12f);
            yield return null; yield return null;
            var r1 = pm.PickAt(Scr(hc.transform.position));
            Line(r1.cube == hc && r1.rule == 1 && r1.tile == null, "hover: the cube under the ray is the candidate", "rule " + r1.rule);
            // a point on the cube's tile that misses the cube body: the top cube on that tile
            int rule2 = -1; AudioCube c2 = null; int tried = 0;
            for (int k = 0; k < 16 && rule2 != 2; k++)
            {
                float a = k * Mathf.PI * 2f / 16f;
                var w = tile.Top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.44f;
                var sp = Scr(w);
                RaycastHit h;
                if (!Physics.Raycast(cam.ScreenPointToRay(sp), out h, 600f) || h.collider.GetComponentInParent<TileInteraction>() != tile) continue;
                tried++;
                var r2 = pm.PickAt(sp); rule2 = r2.rule; c2 = r2.cube;
            }
            Line(rule2 == 2 && c2 == hc, "hover: a point on an occupied tile beside the cube picks the top cube on that tile", "samples " + tried + " rule " + rule2);
            // a free tile under the ray wins (its hover highlight, no candidate)
            TileInteraction free = null;
            foreach (var t in tile.island.tiles) { if (t == null || PathManager.TopCubeOn(t) != null) continue; var pr = pm.PickAt(Scr(t.Top)); if (pr.tile == t && pr.cube == null && pr.rule == 4) { free = t; break; } }
            Line(free != null, "hover: a free tile under the ray wins (tile returned, no candidate)", free != null ? "tile " + free.gridX + "," + free.gridZ : "none on the island");
            // Alt: the tile under a cube (stacking a new path)
            var r5 = pm.PickAt(Scr(hc.transform.position), true);
            Line(r5.rule == 5 && r5.tile == tile && r5.cube == null, "hover: Alt + a cube gives its tile (a new path stacks there)", "rule " + r5.rule);
            // the real pointer path: a click (press + release in place) on the candidate opens the inspector; a drag that
            // starts on a cube does nothing; Alt + press on a cube's tile starts a new path there (stacking)
            var cp = Scr(hc.transform.position);
            pm.SimPointer(cp, true, true, false); pm.SimPointer(cp, false, false, true);
            bool opened = CubeInspector.IsOpen && CubeInspector.Current == hc;
            CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); yield return null;   // v4: the opening began the focus loop (the clock plays: PickAt's rules 2 / 3 rest while playing)
            Line(opened, "a click (press + release within 6 px) on the candidate cube opens the inspector");
            pm.SimPointer(cp, true, true, false); pm.SimPointer(cp + new Vector3(40f, 0f, 0f), false, true, false); pm.SimPointer(cp + new Vector3(40f, 0f, 0f), false, false, true);
            Line(!CubeInspector.IsOpen && !pm.IsDrawing && pm.selectedCube == null, "a drag that starts on a cube does nothing (no inspector, no path)");
            int cubesA = SequenceMaster.Cubes.Count;
            pm.SimPointer(cp, true, true, false, false, true); pm.SimPointer(cp, false, false, true, false, true);
            bool stacking = pm.IsDrawing && SequenceMaster.Cubes.Count == cubesA + 1 && pm.currentPathTiles.Count == 1 && pm.currentPathTiles[0] == tile;
            pm.CancelPath(); FocusLoop.Dismiss(); yield return null;   // v4: a draft begins the focus loop (it plays until dismissed; PickAt's rules 2 / 3 rest while playing)
            Line(stacking, "Alt + press on a cube starts a new path on its tile (it stacks there)");
            // the frame loop: the candidate is outlined; a free tile has no outline; a locked world has no candidate
            PathManager.SimPos = Scr(hc.transform.position);
            yield return null; yield return WaitFor(() => pm.Candidate == hc && CubeOutline.Fade > 0.99f, 2f);
            bool outlined = pm.Candidate == hc && CubeOutline.Target == hc && CubeOutline.Visible && CubeOutline.Fade > 0.95f;
            if (captures) { yield return Wait(0.9f); Shoot("a_hover.png"); yield return null; yield return null; }   // the previews of the tests above fade first
            Line(outlined, "hover: the frame loop outlines the candidate (marching outline visible, faded in)", "candidate " + (pm.Candidate != null) + " target " + (CubeOutline.Target == hc) + " fade " + CubeOutline.Fade.ToString("F2"));
            if (free != null) { PathManager.SimPos = Scr(free.Top); yield return null; yield return WaitFor(() => !CubeOutline.Visible, 2f); }
            Line(free == null || (pm.Candidate == null && pm.HoverTile == free && !CubeOutline.Visible), "hover: over a free tile the tile highlights and the outline fades out", "candidate " + (pm.Candidate != null) + " outline " + CubeOutline.Visible);
            PathManager.SimPos = Scr(hc.transform.position); yield return null;
            WorldInput.Lock("test"); yield return null;
            bool lockedNone = pm.Candidate == null && CubeOutline.Target == null;
            WorldInput.Unlock("test");
            Line(lockedNone, "hover: no candidate while the world is locked");
            // the 16 px screen-rect rule: just outside a cube where the ray meets only the platform margin / the sea (no free tile)
            int rule3 = -1; AudioCube c3 = null; int samples = 0; string where = "";
            foreach (var kb in sm.Islands)
            {
                if (kb == null || rule3 == 3) continue;
                Frame(kb.Center, 13f); yield return null; yield return null;
                foreach (var c in SequenceMaster.Cubes)
                {
                    if (c == null || !c.isFinalized || !c.BodyVisible || c.Island != kb) continue;
                    Rect rc; if (!pm.ScreenRectOf(c, out rc)) continue;
                    for (int k = 0; k < 32 && rule3 != 3; k++)
                    {
                        float a = k * Mathf.PI * 2f / 32f;
                        var sp = new Vector3(rc.center.x + Mathf.Cos(a) * (rc.width * 0.5f + 12f), rc.center.y + Mathf.Sin(a) * (rc.height * 0.5f + 12f), 0f);
                        var r3 = pm.PickAt(sp); samples++;
                        if (r3.rule != 3) continue;
                        Rect rr;
                        if (r3.cube != null && pm.ScreenRectOf(r3.cube, out rr) && sp.x >= rr.xMin - PathManager.NearPx && sp.x <= rr.xMax + PathManager.NearPx && sp.y >= rr.yMin - PathManager.NearPx && sp.y <= rr.yMax + PathManager.NearPx)
                        { rule3 = 3; c3 = r3.cube; where = "island " + kb.measureIndex + (c3 == c ? " (the sampled cube)" : " (a nearer rect centre)"); }
                    }
                    if (rule3 == 3) break;
                }
            }
            Line(rule3 == 3 && c3 != null, "hover: 12 px outside a cube's screen rect, where the ray hits no free tile, the cube is picked (rect + 16 px, nearest centre)", "samples " + samples + " " + where);
        }

        // ================================================================== 3. the inspector lifecycle (SPEC v3 §2.2)
        LoadFixture(); yield return null; yield return null;
        sampling = true; CubeInspector.Ensure().StartCoroutine(Sampler());
        var ic = PitchedCube(c => c.Island != null && c.Island.cols >= 5);
        if (ic == null) Line(false, "inspector lifecycle", "no pitched cube");
        else
        {
            Frame(ic.Island.Center, 13f); yield return null;
            PathManager.SimPos = new Vector3(-50f, -50f, 0f);
            int ev0 = events.Count;
            CubeInspector.Open(ic);
            var proxy = CubeInspector.Proxy;
            if (captures) CubeInspector.Ensure().StartCoroutine(ShootAfter("a_fly.png", 0.4f));   // late in the flight: spin, trail, dim fading in, the focus opening
            bool atOnce = pm.selectedCube == ic && WorldInput.IsLockedBy("inspector") && !ic.BodyVisible && ic.PathVisible && proxy != null && CubeInspector.IsOpen && CubeInspector.Current == ic && CubeInspector.State == CubeInspector.Phase.Opening;
            Line(atOnce, "open: selects the cube, locks the world (inspector), hides the body (path stays), spawns the proxy",
                 "selected " + (pm.selectedCube == ic) + " locked " + WorldInput.IsLockedBy("inspector") + " body " + ic.BodyVisible + " path " + ic.PathVisible + " proxy " + (proxy != null));
            yield return WaitFor(() => CubeInspector.State != CubeInspector.Phase.Opening || CubeInspector.PhaseTime >= CubeInspector.FlyIn * 0.5f, 2f);
            float cIn = CentreErrPx(cam); string cInAt = CubeInspector.State + " t " + CubeInspector.PhaseTime.ToString("F2");
            yield return Opened();
            proxy = CubeInspector.Proxy;
            Vector3 vp = proxy != null ? cam.WorldToViewportPoint(proxy.position) : Vector3.zero;
            float dx = vp.x - CubeInspector.TargetViewport.x, dy = vp.y - CubeInspector.TargetViewport.y;
            // projected height share of the posed proxy
            float share = 0f;
            if (proxy != null)
            {
                float y0 = float.MaxValue, y1 = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    var corner = proxy.TransformPoint(new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f));
                    var v = cam.WorldToViewportPoint(corner); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
                }
                share = y1 - y0;
            }
            bool landed = CubeInspector.State == CubeInspector.Phase.Open && proxy != null && Mathf.Abs(dx) <= 0.02f && Mathf.Abs(dy) <= 0.02f && Mathf.Abs(vp.z - CubeInspector.TargetDistance) < 0.12f;
            Line(landed, "fly: after 0.55 s the proxy sits at viewport (0.28, 0.52), 3.0 u in front of the camera (within 2 %)",
                 "viewport " + vp.x.ToString("F3") + "," + vp.y.ToString("F3") + " z " + vp.z.ToString("F2") + " height share " + share.ToString("F3"));
            Line(share > 0.19f && share < 0.29f, "fly: the posed proxy spans ~24 % of the screen height", share.ToString("F3"));
            yield return CardIn();
            var card = InspectorCard.Root;
            bool cardIn = card != null && card.gameObject.activeSelf && InspectorCard.Shown > 0.97f && CubeInspector.Dim > 0.99f;
            var grid = InspectorCard.Grid;
            Line(cardIn && grid != null && grid.Cube == ic, "open: the card slid in, the dim is up, the flat grid shows the cube's path",
                 "card " + (card != null && card.gameObject.activeSelf) + " shown " + InspectorCard.Shown.ToString("F2") + " dim " + CubeInspector.Dim.ToString("F2") + " grid " + (grid != null ? grid.Cols + "x" + grid.Rows + " beads " + grid.BeadPositions.Count : "none"));
            Rect hr;
            bool hints = Hints.TryGetScreenRect("inspector", out hr) && Hints.TryGetScreenRect("inspector.grid", out hr) && Hints.TryGetScreenRect("inspector.necklace", out hr)
                         && Hints.TryGetScreenRect("inspector.swatches", out hr) && Hints.TryGetScreenRect("inspector.close", out hr) && Hints.TryGetScreenRect("inspector.volume", out hr)
                         && Hints.TryGetScreenRect("inspector.rhythm", out hr);
            Line(hints, "hint targets inspector, .grid, .necklace, .swatches, .close, .volume, .rhythm resolve on screen");
            bool inspectedEv = events.IndexOf(Onboarding.Ev.CubeInspected, ev0) >= 0;
            Line(inspectedEv, "onboarding: cube.inspected on open");
            yield return Wait(0.35f);   // the focus lines finish shooting out
            float cIdle = CentreErrPx(cam);
            float ratio = CubeInspector.FocusCubeRadiusPx / Mathf.Max(1f, share * 0.5f * cam.pixelHeight);
            if (captures) { Shoot("a_inspect.png"); yield return null; yield return null; Shoot("a_inspect_v2.png"); yield return null; yield return null; }
            // close with the card's button
            int ev1 = events.Count;
            CubeInspector.Close();
            bool closing = CubeInspector.State == CubeInspector.Phase.Closing && pm.selectedCube == ic && CubeInspector.IsOpen;
            yield return WaitFor(() => CubeInspector.State != CubeInspector.Phase.Closing || CubeInspector.PhaseTime >= CubeInspector.Retract + CubeInspector.FlyOut * 0.5f, 3f);
            float cOut = CentreErrPx(cam); string cOutAt = CubeInspector.State + " t " + CubeInspector.PhaseTime.ToString("F2");
            Line(cIn <= 2f && cIdle <= 2f && cOut <= 2f, "focus backdrop centred on the cube (on the camera-to-cube ray): within 2 px at the fly-in midpoint, idle and the return midpoint",
                 "fly-in " + cIn.ToString("F3") + " px (" + cInAt + "), idle " + cIdle.ToString("F3") + " px, return " + cOut.ToString("F3") + " px (" + cOutAt + "); cube radius " + CubeInspector.FocusCubeRadiusPx.ToString("F0") + " px = " + ratio.ToString("F2") + " x the posed half height");
            yield return Closed();
            bool closed = CubeInspector.State == CubeInspector.Phase.Closed && CubeInspector.Proxy == null && ic.BodyVisible && ic.PathVisible && !WorldInput.IsLockedBy("inspector")
                          && pm.selectedCube == null && !CubeInspector.IsOpen && CountNamed("InspectorProxy") == 0 && events.IndexOf(Onboarding.Ev.InspectorClosed, ev1) >= 0;
            Line(closing && closed, "close: the proxy flies back 0.45 s (still selected meanwhile), then it is destroyed, the cube shows, unlocked, deselected, inspector.closed",
                 "closing " + closing + " state " + CubeInspector.State + " proxy " + (CubeInspector.Proxy != null) + " body " + ic.BodyVisible + " locked " + WorldInput.IsLockedBy("inspector") + " selected " + (pm.selectedCube != null));
            yield return WaitFor(() => !InspectorCard.Root.gameObject.activeSelf && CubeInspector.Dim <= 0.001f, 3f);
            Line(!InspectorCard.Root.gameObject.activeSelf && CubeInspector.Dim <= 0.001f, "close: the card popped out and the dim is gone");
            // the pointer while open: a drag on the proxy spins it (inertia), a click on it pokes it, a press over the HUD is
            // ignored, a click on the dim sends the cube back, and so does a right-click on the dim
            CubeInspector.Open(ic); yield return Opened();
            var ps = CubeInspector.ProxyScreen; float yaw0 = CubeInspector.SpinYaw;
            CubeInspector.SimPointer(ps, true, true, false);
            CubeInspector.SimPointer(ps + new Vector3(30f, 0f, 0f), false, true, false);
            CubeInspector.SimPointer(ps + new Vector3(60f, 0f, 0f), false, true, false);
            float yaw1 = CubeInspector.SpinYaw;
            CubeInspector.SimPointer(ps + new Vector3(60f, 0f, 0f), false, false, true);
            bool spun = yaw1 < yaw0 - 20f && Mathf.Abs(CubeInspector.SpinVelocity) > 1f && CubeInspector.State == CubeInspector.Phase.Open;
            ps = CubeInspector.ProxyScreen;
            CubeInspector.SimPointer(ps, true, true, false); CubeInspector.SimPointer(ps, false, false, true);
            bool poked = CubeInspector.State == CubeInspector.Phase.Open;
            var dimPt = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            CubeInspector.SimPointer(dimPt, true, true, false, false, false, true); CubeInspector.SimPointer(dimPt, false, false, true, false, false, true);
            bool uiIgnored = CubeInspector.State == CubeInspector.Phase.Open;
            CubeInspector.SimPointer(dimPt, true, true, false); CubeInspector.SimPointer(dimPt, false, false, true);
            bool dimClosed = CubeInspector.State == CubeInspector.Phase.Closing;
            yield return Closed();
            CubeInspector.Open(ic); yield return Opened();
            CubeInspector.SimPointer(dimPt, false, false, false, true, false); CubeInspector.SimPointer(dimPt, false, false, false, false, true);
            bool rightClosed = CubeInspector.State == CubeInspector.Phase.Closing;
            yield return Closed();
            Line(spun && poked && uiIgnored && dimClosed && rightClosed && CubeInspector.State == CubeInspector.Phase.Closed,
                 "pointer while open: drag spins the proxy with inertia, a click pokes it, the HUD is ignored, a click / right-click on the dim sends it back",
                 "spin " + (yaw1 - yaw0).ToString("F0") + " vel " + CubeInspector.SpinVelocity.ToString("F0") + " poke " + poked + " ui " + uiIgnored + " dim " + dimClosed + " right " + rightClosed);
            // Esc closes (through the same key path the inspector reads)
            CubeInspector.Open(ic); yield return Opened();
            KeyShim.Sim(KeyCode.Escape, true, true, false); CubeInspector.RunInputForTest(); KeyShim.Clear();
            bool escClosing = CubeInspector.State == CubeInspector.Phase.Closing;
            yield return Closed();
            Line(escClosing && CubeInspector.State == CubeInspector.Phase.Closed && ic.BodyVisible && pm.selectedCube == null, "Esc sends the cube back");
            // a selection made elsewhere opens the inspector (selection ⇔ inspector); a plain deselect flies it back
            sampling = false; yield return null;
            var other = PitchedCube(c => c != ic);
            pm.Select(other, true);
            bool auto = CubeInspector.IsOpen && CubeInspector.Current == other && WorldInput.IsLockedBy("inspector") && !other.BodyVisible;
            yield return Opened();
            pm.Deselect(); yield return null; yield return null;
            bool backing = CubeInspector.State == CubeInspector.Phase.Closing;
            yield return Closed();
            Line(auto && backing && CubeInspector.State == CubeInspector.Phase.Closed && other.BodyVisible && !WorldInput.IsLockedBy("inspector"), "PathManager.Select opens the inspector; a plain Deselect flies it back and unlocks");
            sampling = true; CubeInspector.Ensure().StartCoroutine(Sampler());
            // opening another cube while open: close then open, never two proxies
            CubeInspector.Open(ic); yield return Wait(0.2f);
            CubeInspector.Open(other); yield return null;
            int proxies = CountNamed("InspectorProxy");
            Line(proxies == 1 && CubeInspector.Current == other && ic.BodyVisible && !other.BodyVisible && pm.selectedCube == other, "opening another cube while open: the first snaps back, one proxy", "proxies " + proxies);
            yield return Opened();
            // undo while inspecting: the rebuilt cube with the same id is re-targeted (the inspector stays)
            var gv = InspectorCard.Grid; int id = other.id; int nodes0 = other.nodes.Count;
            int ex = -1, ez = -1;
            for (int z = 0; z < gv.Rows && ex < 0; z++) for (int x = 0; x < gv.Cols && ex < 0; x++) if (!OnPath(other, x, z)) { ex = x; ez = z; }
            gv.SimDownCell(ex, ez); gv.SimUp();
            bool grew = other.nodes.Count == nodes0 + 1;
            History.Undo(); yield return null; yield return null;
            var now = CubeInspector.Current;
            Line(grew && now != null && now != other && now.id == id && now.nodes.Count == nodes0 && CubeInspector.IsOpen && !now.BodyVisible && pm.selectedCube == now && CubeInspector.Proxy != null && gv.Cube == now,
                 "undo while inspecting: the rebuilt cube (same id) is re-targeted; proxy, card and grid stay", "now " + (now != null ? now.id + " nodes " + now.nodes.Count : "null"));
            // delete from the card: the proxy pops, the cube is deleted (one History entry), no return flight
            var victim = CubeInspector.Current; int cubesBefore = SequenceMaster.Cubes.Count; int p0 = pushes; int ev2 = events.Count;
            CubeInspector.DeleteCurrent();
            bool popping = CubeInspector.State == CubeInspector.Phase.Popping && pm.selectedCube == null && !CubeInspector.IsOpen;
            yield return null;
            bool gone = victim == null && SequenceMaster.Cubes.Count == cubesBefore - 1 && pushes - p0 == 1;
            yield return Closed();
            Line(popping && gone && CubeInspector.State == CubeInspector.Phase.Closed && CubeInspector.Proxy == null && !WorldInput.IsLockedBy("inspector") && events.IndexOf(Onboarding.Ev.InspectorClosed, ev2) >= 0,
                 "delete: the proxy pops, the cube is deleted with one History entry, no return flight, unlocked", "popping " + popping + " gone " + gone + " pushes " + (pushes - p0));
        }

        // ================================================================== 4. grid gestures (SPEC v3 §2.4)
        LoadFixture(); yield return null; yield return null;
        var gc = PitchedCube(c => c.Island != null && c.Island.cols >= 5 && c.Island.rows >= 3 && c.nodes.Count >= 2);
        if (gc == null) Line(false, "grid gestures", "no suitable cube");
        else
        {
            Frame(gc.Island.Center, 13f); yield return null;
            CubeInspector.Open(gc); yield return Opened(); yield return CardIn();
            var g = InspectorCard.Grid;
            int ev0 = events.Count;
            Line(g != null && g.Cube == gc && g.Cols == gc.Island.cols && g.Rows == gc.Island.rows && g.BeadPositions.Count == gc.nodes.Count, "grid binds the inspected cube: island cols x rows, one bead per node",
                 g != null ? g.Cols + "x" + g.Rows + " beads " + g.BeadPositions.Count + " nodes " + gc.nodes.Count : "no grid");
            // a. click an empty cell = append
            int ex = -1, ez = -1;
            for (int z = 0; z < g.Rows && ex < 0; z++) for (int x = 0; x < g.Cols && ex < 0; x++) if (!OnPath(gc, x, z)) { ex = x; ez = z; }
            int n0 = gc.nodes.Count, p0 = pushes;
            g.SimDownCell(ex, ez); g.SimUp();
            var last = gc.nodes[gc.nodes.Count - 1];
            string al = Aligned(gc);
            Line(gc.nodes.Count == n0 + 1 && last.gridX == ex && last.gridZ == ez && gc.ModOf(gc.nodes.Count - 1) == 0 && pushes - p0 == 1 && al == null, "grid: click an empty cell appends a node", "[" + Cells(gc) + "] pushes " + (pushes - p0) + " " + al);
            Line(events.IndexOf(Onboarding.Ev.GridEdited, ev0) >= 0, "onboarding: grid.edited on the first committed edit");
            // b. drag from the last bead = extend (each entered cell appended)
            int lx = ex, lz = ez; int ddx = 0, ddz = 0, steps = 0;
            int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            for (int d = 0; d < 4; d++)
            {
                int k = 0;
                while (k < 3 && lx + dirs[d, 0] * (k + 1) >= 0 && lx + dirs[d, 0] * (k + 1) < g.Cols && lz + dirs[d, 1] * (k + 1) >= 0 && lz + dirs[d, 1] * (k + 1) < g.Rows) k++;
                if (k > steps) { steps = k; ddx = dirs[d, 0]; ddz = dirs[d, 1]; }
            }
            n0 = gc.nodes.Count; p0 = pushes;
            g.SimDownNode(gc.nodes.Count - 1);
            for (int k = 1; k <= steps; k++) g.SimMoveCell(lx + ddx * k, lz + ddz * k);
            g.SimUp();
            al = Aligned(gc);
            Line(steps > 0 && gc.nodes.Count == n0 + steps && pushes - p0 == 1 && al == null, "grid: drag from the last bead extends through every entered cell", "+" + (gc.nodes.Count - n0) + " of " + steps + " pushes " + (pushes - p0) + " " + al);
            // c. hold a bead = sticker popover; pick accent
            p0 = pushes;
            g.SimDownNode(1); g.SimHold();
            bool pop = g.Popover != null;
            if (pop) g.Popover.Pick(3);
            g.SimUp();
            yield return null;
            Line(pop && gc.ModOf(1) == 3 && pushes - p0 == 1 && Aligned(gc) == null, "grid: hold a bead " + PathGridView.HoldSeconds + " s opens the sticker popover; a pick sets the sticker (one History entry)", "popover " + pop + " mod " + gc.ModOf(1) + " pushes " + (pushes - p0));
            // d. drag another bead = move that node; its sticker moves with it
            int mx = -1, mz = -1;
            for (int z = 0; z < g.Rows && mx < 0; z++) for (int x = 0; x < g.Cols && mx < 0; x++) if (!OnPath(gc, x, z)) { mx = x; mz = z; }
            var before = new List<TileInteraction>(gc.nodes); n0 = gc.nodes.Count; p0 = pushes;
            g.SimDownNode(1); g.SimMoveCell(mx, mz); g.SimUp();
            bool othersSame = true; for (int i = 0; i < gc.nodes.Count && i < before.Count; i++) if (i != 1 && gc.nodes[i] != before[i]) othersSame = false;
            al = Aligned(gc);
            Line(mx >= 0 && gc.nodes.Count == n0 && gc.nodes[1].gridX == mx && gc.nodes[1].gridZ == mz && gc.ModOf(1) == 3 && othersSame && pushes - p0 == 1 && al == null,
                 "grid: drag a middle bead moves that node and keeps its sticker", "[" + Cells(gc) + "] pushes " + (pushes - p0) + " " + al);
            // e. click a bead = rest on / off
            int m2 = gc.ModOf(2); p0 = pushes;
            g.SimDownNode(2); g.SimUp();
            Line(gc.ModOf(2) == (m2 == 1 ? 0 : 1) && gc.rests[2] == (gc.ModOf(2) == 1) && pushes - p0 == 1, "grid: click a bead toggles its rest", "mod " + m2 + " -> " + gc.ModOf(2));
            // f. right-click a bead = remove it (stickers stay aligned)
            var modsBefore = new List<int>(gc.mods); n0 = gc.nodes.Count; p0 = pushes;
            g.SimDownNode(2, 1); g.SimUp(1);
            bool shifted = true; for (int i = 2; i < gc.nodes.Count; i++) if (gc.ModOf(i) != modsBefore[i + 1]) shifted = false;
            al = Aligned(gc);
            Line(gc.nodes.Count == n0 - 1 && gc.ModOf(1) == 3 && shifted && pushes - p0 == 1 && al == null, "grid: right-click a bead removes the node; the other stickers stay on their nodes", "[" + Cells(gc) + "] " + al);
            // g. reverse
            var rev = new List<TileInteraction>(gc.nodes); rev.Reverse(); var revMods = new List<int>(gc.mods); revMods.Reverse(); p0 = pushes;
            bool revOk = g.Reverse();
            bool same = revOk && gc.nodes.Count == rev.Count; for (int i = 0; same && i < rev.Count; i++) same = gc.nodes[i] == rev[i] && gc.ModOf(i) == revMods[i];
            Line(same && pushes - p0 == 1 && Aligned(gc) == null, "path tool: reverse flips the node order with the stickers", "[" + Cells(gc) + "]");
            // h. shift by one cell (when every node stays inside), refused otherwise
            int sdx = 0, sdz = 0;
            int[,] sh = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            for (int d = 0; d < 4; d++) if (gc.CanShift(sh[d, 0], sh[d, 1])) { sdx = sh[d, 0]; sdz = sh[d, 1]; break; }
            var xs = new List<int>(gc.gridX); var zs = new List<int>(gc.gridZ); p0 = pushes;
            bool shOk = (sdx != 0 || sdz != 0) && g.Shift(sdx, sdz);
            bool moved = shOk; for (int i = 0; moved && i < xs.Count; i++) moved = gc.gridX[i] == xs[i] + sdx && gc.gridZ[i] == zs[i] + sdz;
            Line(moved && pushes - p0 == 1 && Aligned(gc) == null, "path tool: shift moves the whole path one cell", "by " + sdx + "," + sdz + " [" + Cells(gc) + "]");
            var snap = Cells(gc); p0 = pushes;
            bool refused = !g.Shift(g.Cols, 0);
            Line(refused && Cells(gc) == snap && pushes - p0 == 0, "path tool: a shift that would leave the grid is refused (no change, no History entry)");
            // i. pencil: the next press clears the path to that cell, the drag appends, release ends
            p0 = pushes;
            g.TogglePencil();
            bool armed = g.PencilArmed;
            g.SimDownCell(0, 0);
            bool cleared = gc.nodes.Count == 1 && gc.nodes[0].gridX == 0 && gc.nodes[0].gridZ == 0;
            g.SimMoveCell(1, 0); g.SimMoveCell(2, 1); g.SimUp();
            bool pencilOk = armed && cleared && !g.PencilArmed && gc.nodes.Count == 3 && gc.nodes[1].gridX == 1 && gc.nodes[2].gridX == 2 && gc.nodes[2].gridZ == 1 && gc.mods.TrueForAll(m => m == 0);
            Line(pencilOk && pushes - p0 == 1 && Aligned(gc) == null, "path tool: pencil redraw (press clears to the cell, drag appends, release ends, one History entry)", "[" + Cells(gc) + "] pushes " + (pushes - p0));
            // j. path mode buttons live on the card too; the grid redraws for each mode
            gc.SetMode(PathMode.PingPong); yield return null; gc.SetMode(PathMode.Once); yield return null; gc.SetMode(PathMode.Loop); yield return null;
            Line(g.BeadPositions.Count == gc.nodes.Count, "grid redraws for loop / ping-pong / once");
            // a few more edits for the capture: a longer path with stickers
            g.SimDownNode(gc.nodes.Count - 1); g.SimMoveCell(3, 1); g.SimMoveCell(3, 2); g.SimMoveCell(2, 2); g.SimUp();
            g.SimDownNode(1); g.SimHold(); if (g.Popover != null) g.Popover.Pick(4); g.SimUp();
            yield return null;
            g.SimDownNode(3); g.SimUp();
            yield return Wait(0.3f);
            if (captures) { Shoot("a_grid_edit.png"); yield return null; yield return null; }
            Info("grid after edits: [" + Cells(gc) + "]");
            if (captures)
            {
                gc.SetMode(PathMode.PingPong); yield return Wait(0.15f); Shoot("a_grid_pingpong.png"); yield return null; yield return null;
                gc.SetMode(PathMode.Once); yield return Wait(0.15f); Shoot("a_grid_once.png"); yield return null; yield return null;
                gc.SetMode(PathMode.Loop); yield return null;
            }

            // ================================================================== 5. hotkeys act on the inspected cube (SPEC v3 §2.3)
            var hk = CubeInspector.Current;
            int cubes0 = SequenceMaster.Cubes.Count; p0 = pushes;
            Key(KeyCode.T);
            AudioCube twin = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c != hk && c.twinOf == hk.id) twin = c;
            Line(twin != null && SequenceMaster.Cubes.Count == cubes0 + 1 && pushes - p0 == 1, "hotkey T twins the inspected cube");
            bool rider0 = hk.rider; p0 = pushes;
            Key(KeyCode.R); bool riderOn = hk.rider != rider0; Key(KeyCode.R);
            Line(!riderOn && hk.rider == rider0 && pushes - p0 == 0, "v7 §21: riders are retired (a cube stays on its grid) — R no longer toggles a rider, no History entry");
            int f0 = hk.follow; p0 = pushes; Key(KeyCode.W);
            Line(hk.follow == (f0 + 1) % 4 && pushes - p0 == 1, "hotkey W cycles its shadow");
            int o0 = hk.octave; p0 = pushes; Key(o0 < 1 ? KeyCode.UpArrow : KeyCode.DownArrow);
            Line(hk.octave == (o0 < 1 ? o0 + 1 : o0 - 1) && pushes - p0 == 1, "hotkey arrow up/down changes its octave");
            int r0 = hk.rot; p0 = pushes; Key(KeyCode.RightBracket);
            Line(hk.rot == (r0 + 1) % hk.StepsPerBar && pushes - p0 == 1, "hotkey ] turns its necklace");
            int h0 = hk.hits; p0 = pushes; Key(KeyCode.Minus); int h1 = hk.hits; Key(KeyCode.Equals);
            Line(h1 != h0 && hk.hits == h0 && pushes - p0 == 2, "hotkeys - / = change its hits", h0 + " -> " + h1 + " -> " + hk.hits);
            int inst0 = hk.instrument, pal0 = pm.selectedInstrument; int want = inst0 == 2 ? 3 : 2; p0 = pushes;
            Key(want == 2 ? KeyCode.Alpha3 : KeyCode.Alpha4);
            Line(hk.instrument == want && pm.selectedInstrument == pal0 && pushes - p0 == 1, "number keys recolour the inspected cube (the palette keeps its slot)", inst0 + " -> " + hk.instrument);
            cubes0 = SequenceMaster.Cubes.Count; p0 = pushes;
            KeyShim.Sim(KeyCode.G, true, true, false); UIManager.I.RunHotkeysForTest(); KeyShim.Clear();
            KeyShim.Sim(KeyCode.G, false, false, true); UIManager.I.RunHotkeysForTest(); KeyShim.Clear();
            Line(SequenceMaster.Cubes.Count == cubes0 + 1 && pushes - p0 == 1, "hotkey G stamps the inspected cube onto another island");
            // v4: opening the inspector begins the focus loop (the column plays), and a spotlight lands on the next 16th while playing
            KeyShim.Sim(KeyCode.E, true, true, false); UIManager.I.RunHotkeysForTest();
            yield return Wait(0.3f);
            bool spotOn = Performance.SpotlightCube == hk;
            KeyShim.Clear(); UIManager.I.RunHotkeysForTest();
            yield return Wait(0.3f);
            Line(spotOn && Performance.SpotlightCube == null, "hotkey E (held) spotlights the inspected cube; release restores", "on " + spotOn + ", after release " + (Performance.SpotlightCube == null) + ", playing " + GlobalClock.IsPlaying);
            Line(CubeInspector.Current == hk && CubeInspector.IsOpen && WorldInput.IsLockedBy("inspector"), "the inspector stays on the cube through the hotkeys");
            cubes0 = SequenceMaster.Cubes.Count; p0 = pushes;
            KeyShim.Sim(KeyCode.Delete, true, true, false); PathManager.I.RunHotkeysForTest(); KeyShim.Clear();
            bool delPop = CubeInspector.State == CubeInspector.Phase.Popping;
            yield return Closed();
            Line(delPop && SequenceMaster.Cubes.Count == cubes0 - 1 && pushes - p0 == 1 && CubeInspector.State == CubeInspector.Phase.Closed, "Delete key deletes the inspected cube (pop, one History entry)");
        }

        // ================================================================== 6. audio while inspecting a playing song
        LoadFixture(); yield return null;
        var ac = PitchedCube(c => c.Island != null && c.Island.cols >= 5);
        if (ac == null) Line(false, "audio while inspecting", "no cube");
        else
        {
            Frame(ac.Island.Center, 13f);
            int landings = 0, nowSeen = 0;
            Action<AudioCube, int, int, AudioCube.Hit> onLand = (c, w, k, h) => { if (c == CubeInspector.Current) landings++; };
            AudioCube.OnLanded += onLand;
            GlobalClock.Seek(sm.MeasureStarts[Mathf.Clamp(ac.assignedGridIndex, 0, sm.MeasureStarts.Count - 1)]);
            GlobalClock.LoopSong = true;
            GlobalClock.Play();
            yield return Wait(1.0f);   // measured from here: the inspector opening, flying and every edit while the song plays
            int late0 = Synth.LateEvents, err0 = Synth.Errors;
            CubeInspector.Open(ac); yield return Opened();
            var g = InspectorCard.Grid;
            for (int k = 0; k < 4; k++)
            {
                var cur = CubeInspector.Current; if (cur == null) break;
                int ex = -1, ez = -1;
                for (int z = 0; z < g.Rows && ex < 0; z++) for (int x = 0; x < g.Cols && ex < 0; x++) if (!OnPath(cur, (x + k) % g.Cols, z)) { ex = (x + k) % g.Cols; ez = z; }
                if (ex >= 0) { g.SimDownCell(ex, ez); g.SimUp(); }
                if (k == 1) Key(KeyCode.RightBracket);
                if (k == 2 && cur.nodes.Count > 2) { g.SimDownNode(1); g.SimMoveCell(ex >= 0 ? ex : 0, ez >= 0 ? (ez + 1) % g.Rows : 0); g.SimUp(); }
                float tk = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tk < 0.9f) { if (g.CurrentNode >= 0) nowSeen++; yield return null; }
            }
            int late1 = Synth.LateEvents, err1 = Synth.Errors;
            AudioCube.OnLanded -= onLand;
            Line(late1 - late0 == 0 && err1 - err0 == 0 && landings > 0, "audio: Synth late 0 / errors 0 while inspecting and editing a playing song; the proxy's cube keeps landing",
                 "late +" + (late1 - late0) + " errors +" + (err1 - err0) + " landings " + landings + " | " + Synth.Stats());
            Line(nowSeen > 0, "grid: while the song plays, the bead of the node the cube is on glows (TryGetPose)", nowSeen + " frames");
            GlobalClock.Stop();
            CubeInspector.Close(); yield return Closed();
        }

        // ================================================================== 7. a Moon cube: the 6 x 4 kit grid
        LoadFixture(); yield return null;
        {
            int m = sm.AddMoon();
            AudioCube mc = null;
            if (m >= 0) mc = pm.RestoreCube(new CubeState { instrument = 9, measure = 0, moon = m, xs = new[] { 1, 2, 3 }, zs = new[] { 0, 1, 2 }, rests = new[] { false, false, false }, step = (int)StepLen.Quarter, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1 });
            if (mc == null) Line(false, "Moon kit grid", "no Moon cube");
            else
            {
                sm.RecomputeMeasureStarts();
                Frame(mc.Island.Center, 14f); yield return null;
                CubeInspector.Open(mc); yield return Opened(); yield return CardIn();
                var g = InspectorCard.Grid;
                bool kit = g.Cols == ProjectConfig.MoonCols && g.Rows == ProjectConfig.MoonRows;
                for (int z = 0; kit && z < g.Rows; z++) kit = g.CellGlyph(0, z) == TileInteraction.KitGlyph(z, g.Rows);
                // v4: the swatches live in the instrument chip's popover; on a Moon cube only the drums cube there takes clicks
                InspectorCard.ChipButton.Click(); yield return null;
                bool swatchesOff = InspectorCard.PopoverOpen;
                for (int k = 0; k < Instruments.Count; k++) if (!Instruments.IsDrums(k) && InspectorCard.InstrumentButton(k).Interactable) swatchesOff = false;
                InspectorCard.ChipButton.Click(); yield return null;
                if (captures) { Shoot("a_moon.png"); yield return null; yield return null; }
                Line(kit && swatchesOff && g.BeadPositions.Count == 3, "Moon cube: the grid is the 6 x 4 kit grid with kit glyphs per row; the colour swatches are locked (drums only)",
                     g.Cols + "x" + g.Rows + " glyph row0 " + g.CellGlyph(0, 0) + " swatches off " + swatchesOff);
                CubeInspector.Close(); yield return Closed();
            }
        }

        sampling = false; yield return null;
        Line(invariantBad == 0, "invariant: selectedCube != null <=> inspector open / opening / closing, every sampled frame", invariantFrames + " frames" + (invariantFirst != null ? ", first bad " + invariantFirst : ""));

        // ---- leave things as found: fixture loaded, stopped, camera back to the orbit rig, no locks, no hidden cubes
        CubeInspector.CloseImmediate();
        try { LoadFixture(); } catch (Exception) { }
        PathManager.SimOnly = false; KeyShim.Clear();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        History.OnChanged -= OnHistory; Onboarding.OnEvent -= OnEvent;
        Onboarding.Suppressed = suppressed;
        int hidden = 0; foreach (var c in SequenceMaster.Cubes) if (c != null && (!c.BodyVisible || !c.PathVisible)) hidden++;
        Info("end: locks " + WorldInput.Describe() + ", hidden cubes " + hidden + ", inspector " + CubeInspector.State);
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true;
    }

    // ================================================================== the comic HUD restyle (captures + style checks)
    public static string HudReport = "";
    public static bool HudDone;
    public static string HudReportPath => Path.Combine(V2Checks.CapturePath, "v3checksA_hud" + Suffix + "_report.txt");

    /// <summary>Comic HUD pass: style assertions on the live HUD (panels, buttons, fonts, text count, hover / press on twos) and
    /// the captures hud_world / hud_inspector / hud_tray / hud_prompt / hud_menu (+ <see cref="Suffix"/>). Poll HudDone or the
    /// report file. Loads the fixture, never the user's save; the autosave the menu writes is put back as it was.</summary>
    public static string RunHud(bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null || UIManager.I == null) return "FAIL needs Play mode";
        HudDone = false; HudReport = "";
        try { if (File.Exists(HudReportPath)) File.Delete(HudReportPath); } catch (Exception) { }
        CubeInspector.Ensure().StartCoroutine(HudRoutine(captures));
        return "started";
    }

    /// <summary>Texts on screen with visible characters (TMP input fields keep a zero-width space in an empty text).</summary>
    static string BarInfo(RectTransform hud)
    {
        var bar = hud.Find("OverviewWrap/MeasureBar") as RectTransform;
        if (bar == null) return "no bar";
        var sh = bar.GetComponent<ComicShape>();
        var island = bar.Find("Island") as RectTransform;
        return "rect " + bar.rect.size + " drawn " + (sh != null ? sh.DrawnRect.size.ToString() : "-") + " pref " + LayoutUtility.GetPreferredWidth(bar) + "x" + LayoutUtility.GetPreferredHeight(bar)
               + " scale " + bar.localScale + " island " + (island != null ? island.gameObject.activeSelf + " " + island.rect.size : "-") + " alpha " + bar.GetComponent<CanvasGroup>().alpha
               + " fitter " + (bar.GetComponent<ContentSizeFitter>() != null && bar.GetComponent<ContentSizeFitter>().enabled) + " sel " + UIManager.I.SelectedMeasure;
    }

    static int TextCount(Component root) { if (root == null) return -1; int k = 0; foreach (var t in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text) && t.text.Replace("\u200B", "").Trim().Length > 0) k++; return k; }

    static IEnumerator HudRoutine(bool captures)
    {
        sb = new StringBuilder(); n = 0;
        bool suppressed = Onboarding.Suppressed;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        IslandTray.Close();
        try { LoadFixture(); } catch (Exception e) { sb.Append("FAIL fixture: ").Append(e.Message).Append('\n'); }
        yield return null; yield return null;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        var hud = UIManager.I.HudRoot;
        Info("screen " + Screen.width + "x" + Screen.height + ", fonts installed " + Comic.FontsInstalled + " (UI " + (Comic.Font != null ? Comic.Font.name : "none") + ", digits " + (Comic.DigitFont != null ? Comic.DigitFont.name : "none") + ")");

        // ---- panels: flat fills, 3 px ink border, hard (+5, -6) shadow; bars indigo, the card paper
        {
            string bad = null; int count = 0;
            foreach (string path in new[] { "TopLeft", "OverviewWrap/Overview", "OverviewWrap/MeasureBar", "Tools", "Palette", "Transport" })
            {
                var t = hud.Find(path); var s = t != null ? t.GetComponent<ComicShape>() : null;
                if (s == null) { bad = bad ?? path + ": no ComicShape"; continue; }
                count++;
                bool ok = Mathf.Abs(s.border - 3f) < 0.01f && s.shadowOffset == new Vector2(5f, -6f) && s.shadowColor.a > 0.8f && Mathf.Abs(s.radius - 14f) < 0.01f
                          && Mathf.Abs(s.color.r - 0.180f) < 0.01f && Mathf.Abs(s.color.g - 0.145f) < 0.01f && Mathf.Abs(s.color.b - 0.376f) < 0.01f && Mathf.Abs(s.color.a - 0.94f) < 0.01f;
                if (!ok) bad = bad ?? path + ": border " + s.border + " shadow " + s.shadowOffset + " radius " + s.radius + " fill " + s.color;
            }
            // v4 (U2): the inspector card is InspectorCard's paper bubble: cream #FFF3E6, an ink rim heavier on the shadow side, a hard print shadow
            var paper = InspectorCard.Bubble;
            bool cardOk = paper != null && paper.Shape == InkShape.Kind.Bubble && Mathf.Abs(paper.color.r - 1f) < 0.01f && Mathf.Abs(paper.color.g - 0.953f) < 0.01f && Mathf.Abs(paper.color.b - 0.902f) < 0.01f
                          && paper.HasShadow && paper.ShadowOffset.x > 0f && paper.ShadowOffset.y < 0f;
            // v4: the six indigo HUD bars are retired by design (U1's frameless HUD; V4ChecksU1 checks it) — only the card half stays
            Info("v3 HUD bars found: " + count + " (retired in v4, checked by V4ChecksU1)" + (bad != null ? "; " + bad : ""));
            Line(cardOk, "the inspector card is a cream paper speech bubble (#FFF3E6) with a hard print shadow (v4)", "card " + cardOk);
        }
        // ---- buttons: a disc / mini cube with an ink ring and a hard shadow, an inked glyph; no layout change
        {
            int buttons = 0, comic = 0, inked = 0, cubes = 0; string first = null;
            foreach (var b in hud.GetComponentsInChildren<HudButton>(true))
            {
                if (b.shape == null) continue;   // pills / wedges / the wheel catcher draw themselves
                buttons++;
                bool ok = b.shape.border >= 1.5f && b.shape.shadowOffset.x > 0f && b.shape.shadowOffset.y < 0f && b.icon != null;
                if (ok) comic++; else if (first == null) first = b.name;
                if (b.icon != null && b.icon.sprite != null && b.icon.sprite.name.StartsWith("ink_")) inked++;
                if (b.shape.cube > 0f) cubes++;
                var le = b.GetComponent<LayoutElement>();
                if (le != null && (Mathf.Abs(le.preferredWidth - b.baseSize) > 0.01f || Mathf.Abs(le.preferredHeight - b.baseSize) > 0.01f) && first == null) first = b.name + " layout";
            }
            // v4: the disc buttons are retired by design (U1's stickers / ink controls; V4ChecksU1)
            Info("v3 comic disc buttons: retired in v4 — " + buttons + " HudButtons with a v3 shape left, " + comic + " comic, " + inked + " inked, " + cubes + " mini cubes" + (first != null ? ", first " + first : ""));
        }
        // ---- lettering: Fredoka / Bangers with the ink outline + hard shadow; no new words on screen
        {
            var tempo = hud.Find("Transport/Main/Tempo/Label");
            var tl = tempo != null ? tempo.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (tl == null)   // v4 HUD (U1): the Bangers tempo digits wherever the transport put them
                foreach (var t in hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.font != null && t.font.name.Contains("Bangers") && t.gameObject.activeInHierarchy) { tl = t; break; }
            var mat = tl != null ? tl.fontSharedMaterial : null;
            bool inkOn = mat != null && mat.IsKeywordEnabled("UNDERLAY_ON") && mat.HasProperty("_OutlineWidth") && mat.GetFloat("_OutlineWidth") > 0.1f;
            bool bangers = tl != null && tl.font != null && tl.font.name.Contains("Bangers");
            Line(tl != null && (bangers || !Comic.FontsInstalled) && inkOn, "tempo digits in Bangers with an ink outline + hard offset underlay (no glow)",
                 tl != null ? tl.font.name + " '" + tl.text + "' outline " + (mat != null && mat.HasProperty("_OutlineWidth") ? mat.GetFloat("_OutlineWidth").ToString("F2") : "-") + " underlay " + (mat != null && mat.IsKeywordEnabled("UNDERLAY_ON")) : "no tempo label");
            int hudTexts = TextCount(hud);
            Line(hudTexts == 1, "words on screen: the HUD shows one text (the tempo digits), as before", hudTexts + " texts");
        }
        // ---- behaviour on twos: hover grows to 1.08 in 12 fps steps with a boiling ring, a press slides onto the shadow and pops back
        {
            var b = hud.Find("Tools/Frame") != null ? hud.Find("Tools/Frame").GetComponent<HudButton>() : null;
            if (b == null) Info("hover / press on twos: no v3 Frame button (the v4 HUD, U1)");
            else
            {
                var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                var seen = new HashSet<float>(); var jit = new HashSet<Vector2>();
                b.OnPointerEnter(ped);
                float t0 = Time.realtimeSinceStartup; int frames = 0;
                while (Time.realtimeSinceStartup - t0 < 0.6f) { seen.Add(Mathf.Round(b.transform.localScale.x * 10000f)); jit.Add(b.shape.inkJitter); frames++; yield return null; }
                float hoverScale = b.transform.localScale.x;
                b.OnPointerDown(ped); yield return null;
                Vector2 pressed = b.shape.bodyOffset, iconAt = b.icon.rectTransform.anchoredPosition;
                b.OnPointerUp(ped); b.OnPointerExit(ped);
                yield return Wait(0.35f);
                Vector2 back = b.shape.bodyOffset;
                float maxDrawings = 0.6f * Look.TwosFps + 2f;
                Line(Mathf.Abs(hoverScale - 1.08f) < 0.012f && seen.Count <= maxDrawings && jit.Count >= 3 && pressed == b.shape.shadowOffset && iconAt == pressed && back == Vector2.zero,
                     "hover 1.08 drawn on twos (<= 12 drawings / s) with a boiling ink ring; a press slides the disc + glyph onto the shadow and it pops back",
                     "scale " + hoverScale.ToString("F3") + ", " + seen.Count + " scale drawings in " + frames + " frames, " + jit.Count + " ring poses, pressed " + pressed + " glyph " + iconAt + ", after " + back);
            }
        }

        // ---- captures
        if (OrbitCamera.I != null) { OrbitCamera.I.Suspended = false; OrbitCamera.I.ResetView(); }
        UIManager.I.SelectMeasure(1, false);
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return Wait(0.6f);
        {
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 2f) { frames++; yield return null; }
            Info("frame time while playing with the full comic HUD: " + (2000f / Mathf.Max(1, frames)).ToString("F2") + " ms avg over " + frames + " frames (editor)");
        }
        {
            var active = hud.GetComponentsInChildren<Image>(false);
            int badges = 0; foreach (var im in active) if (im.name == "Badge" && im.gameObject.activeInHierarchy) badges++;
            Info("island pills + burst badge: retired in v4 (the column rail, U1) — " + badges + " badges");
        }
        if (captures) { Shoot("hud_world.png"); yield return null; yield return null; }
        {
            var bar = hud.Find("OverviewWrap/MeasureBar") as RectTransform;
            var isl = bar != null ? bar.Find("Island") as RectTransform : null;
            var bs = bar != null ? bar.GetComponent<ComicShape>() : null;
            bool fits = bar != null && isl != null && Mathf.Abs(bar.rect.width - (isl.rect.width + 12f)) < 1f && Mathf.Abs(bar.rect.height - (isl.rect.height + 12f)) < 1f && bs != null && bs.DrawnRect.size == bar.rect.size;
            Info("measure bar fit: retired in v4 (the island header, U1) — " + (bar == null ? "no bar" : "fits " + fits));
        }
        GlobalClock.Stop(); yield return null;
        Info("measure bar after stop: " + BarInfo(hud));
        // extra looks (not asked for, kept for review): the chord wheel and the sticker ring
        {
            var chord = hud.Find("OverviewWrap/MeasureBar/Island/Chord");
            var chordBtn = chord != null ? chord.GetComponent<HudButton>() : null;
            if (chordBtn != null && chordBtn.onClick != null)
            {
                chordBtn.onClick();
                yield return WaitFor(() => UIManager.I.WheelOpen, 1f); yield return Wait(0.5f);
                Info("measure bar with the wheel open: " + BarInfo(hud));
                if (captures) { Shoot("hud_wheel.png"); yield return null; yield return null; }
                var wheel = hud.Find("ChordWheel"); var wb = wheel != null ? wheel.GetComponent<HudButton>() : null;
                if (wb != null && wb.onClick != null) wb.onClick();
                yield return Wait(0.4f);
            }
            var rc = PitchedCube();
            if (rc != null)
            {
                Frame(rc.Island.Center, 12f); yield return null;
                UIManager.I.OpenStickerRing(rc, 1);
                yield return Wait(0.45f);
                if (captures) { Shoot("hud_ring.png"); yield return null; yield return null; }
                UIManager.I.CloseRings();
            }
        }

        var ic = PitchedCube(c => c.Island != null && c.Island.cols >= 5);
        if (ic != null)
        {
            CubeInspector.Open(ic); yield return Opened(); yield return CardIn(); yield return Wait(0.4f);
            // v4 (U2): ink controls on the paper (no discs, no boxes); ≤ 20 hit targets on the face
            int inks = InspectorCard.Root.GetComponentsInChildren<InkButton>(true).Length, face = InspectorCard.FaceTargetCount();
            int discs = InspectorCard.Root.GetComponentsInChildren<HudButton>(true).Length;
            Line(inks >= 20 && discs == 0 && face <= 20, "the inspector card's controls are ink drawings on the paper (no HUD discs), <= 20 hit targets on its face", inks + " ink controls, " + discs + " discs, face " + face);
            if (captures) { Shoot("hud_inspector.png"); yield return null; yield return null; }
            CubeInspector.Close(); yield return Closed();
        }
        else Line(false, "inspector capture", "no pitched cube");

        IslandTray.Open();
        yield return WaitFor(() => IslandTray.I != null && IslandTray.I.ShownAmount > 0.98f, 3f);
        yield return Wait(0.3f);
        {
            // v4 (U1): the deck's hand of ink cards (TrayCard with an InkShape body)
            var hand = IslandTray.I != null ? IslandTray.I.Shelf : null;
            int cards = 0, inked = 0;
            if (hand != null) foreach (var tc in hand.GetComponentsInChildren<TrayCard>(true)) { cards++; if (tc.GetComponentInChildren<InkShape>(true) != null) inked++; }
            int next = IslandTray.I != null ? IslandTray.I.CountIn(0) : -1;
            Line(hand != null && next == 4 && cards >= 4 && inked == cards, "tray (v4 deck): the hand holds the next cards, every card an ink card", "next " + next + ", cards " + cards + ", inked " + inked);
        }
        if (captures) { Shoot("hud_tray.png"); yield return null; yield return null; }
        IslandTray.Close(); yield return Wait(0.4f);

        if (InterfaceController.I != null)
        {
            InterfaceController.I.Show();
            yield return Wait(0.6f);
            int promptTexts = TextCount(InterfaceController.I.Canvas);
            // fix F23 (report_fix.md) replaced the example words with a pencil glyph + the caret: the prompt shows no words until you type
            Line(promptTexts == 0, "prompt: no words on it (F23: the empty field shows a pencil glyph and the caret)", promptTexts + " texts");
            if (captures) { Shoot("hud_prompt.png"); yield return null; yield return null; }
            InterfaceController.I.Hide(); WorldInput.Unlock("prompt");
            yield return Wait(0.3f);
        }

        string autoPath = SongIO.AutosavePath;
        string autoBefore = null;
        try { autoBefore = File.Exists(autoPath) ? File.ReadAllText(autoPath) : null; } catch (Exception) { }
        MainMenu.Show();
        yield return Wait(3.2f);
        if (MainMenu.MenuCanvas != null)
        {
            int menuTexts = TextCount(MainMenu.MenuCanvas);
            // v4 (package T): the title menu is lowercase words (MenuButton.CreateText), no pills; v5: five with "gallery" (hidden while the
            // gallery has no songs), so the shown texts are the shown words
            int words = 0; foreach (var mb in MainMenu.MenuCanvas.GetComponentsInChildren<MenuButton>(true)) if (mb.IsText && mb.fill == null) words++;
            int shown = 0; foreach (var w in MainMenu.Words) if (w != null && w.gameObject.activeInHierarchy) shown++;
            Line(menuTexts == shown && words == MainMenu.Words.Length && shown >= 4, "menu: lowercase words only (v5: five with the gallery), no pills", menuTexts + " texts, " + shown + " shown, " + words + " words");
        }
        if (captures) { Shoot("hud_menu.png"); yield return null; yield return null; }
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        try { if (autoBefore != null) File.WriteAllText(autoPath, autoBefore); else if (File.Exists(autoPath)) File.Delete(autoPath); } catch (Exception) { }
        yield return null;

        // ---- leave things as found
        CubeInspector.CloseImmediate();
        try { LoadFixture(); } catch (Exception) { }
        PathManager.SimOnly = false; KeyShim.Clear();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        UIManager.I.SetHudVisible(true);
        Onboarding.Suppressed = suppressed;
        HudReport = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(HudReportPath, HudReport); } catch (Exception) { }
        HudDone = true;
    }

    // ================================================================== every suite in one go (the restyle's regression run)
    public static bool SuitesDone;
    public static string SuitesReportPath => Path.Combine(V2Checks.CapturePath, "a_suites_report.txt");
    /// <summary>Runs V2Checks.RunAll + RunWorld, V3ChecksB.RunAll, V3ChecksD.Run (captures) and V3ChecksA.Run (captures) one after
    /// the other and writes every report to Captures/a_suites_report.txt. The autosave D's run writes is put back as it was.</summary>
    public static string RunSuites()
    {
        if (SongManager.I == null || PathManager.I == null || UIManager.I == null) return "FAIL needs Play mode";
        SuitesDone = false;
        try { if (File.Exists(SuitesReportPath)) File.Delete(SuitesReportPath); } catch (Exception) { }
        CubeInspector.Ensure().StartCoroutine(SuitesRoutine());
        return "started";
    }

    static IEnumerator SuitesRoutine()
    {
        var all = new StringBuilder();
        string autoPath = SongIO.AutosavePath, autoBefore = null;
        try { autoBefore = File.Exists(autoPath) ? File.ReadAllText(autoPath) : null; } catch (Exception) { }
        var saves = V3Fixes.SnapshotSaves();   // fix F15: D's New / Learn flows may write rotating backups; they are put back at the end
        // D first: it reads the boot state (menu up, HUD hidden)
        all.Append("==== V3ChecksD.Run ").Append(V3ChecksD.Run(true)).Append('\n');
        yield return WaitFor(() => V3ChecksD.Done, 300f);
        all.Append(V3ChecksD.Report).Append('\n');
        try { if (autoBefore != null) File.WriteAllText(autoPath, autoBefore); else if (File.Exists(autoPath)) File.Delete(autoPath); } catch (Exception) { }
        // D's prompt flow leaves its (hidden) input field selected, which InputUtil.TypingInField reads as typing: the key
        // checks below would all be blocked. A real click anywhere clears it; the runner clears it the same way.
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        V3ChecksB.Prepare();
        yield return null; yield return null;
        string r;
        try { r = V2Checks.RunAll(); } catch (Exception e) { r = "FAIL RunAll threw " + e.Message + "\n"; }
        all.Append("==== V2Checks.RunAll\n").Append(r).Append('\n');
        yield return null; yield return null;
        try { r = V2Checks.RunWorld(); } catch (Exception e) { r = "FAIL RunWorld threw " + e.Message + "\n"; }
        all.Append("==== V2Checks.RunWorld\n").Append(r).Append('\n');
        yield return null; yield return null;
        V3ChecksB.Prepare();
        all.Append("==== V3ChecksB.RunAll ").Append(V3ChecksB.RunAll()).Append('\n');
        yield return WaitFor(() => V3ChecksB.Done, 240f);
        all.Append(V3ChecksB.Report).Append('\n');
        yield return null;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        all.Append("==== V3ChecksA.Run ").Append(Run(true)).Append('\n');
        yield return WaitFor(() => Done, 300f);
        all.Append(Report).Append('\n');
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(SuitesReportPath, all.ToString()); } catch (Exception) { }
        // v2 integration: plays + records, sweeps every HudButton.onClick (reads the user's save, never writes it)
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        all.Append("==== V2Checks.RunIntegration ").Append(V2Checks.RunIntegration()).Append('\n');
        yield return WaitFor(() => V2Checks.IntegrationDone, 240f);
        all.Append(V2Checks.IntegrationReport).Append('\n');
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(SuitesReportPath, all.ToString() + "==== end\n"); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        SuitesDone = true;
    }
}
