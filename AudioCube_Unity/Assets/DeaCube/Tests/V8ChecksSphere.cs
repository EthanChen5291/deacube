using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// SPHERE pieces (the user, 2026-10-01: "when choosing, you can choose between sphere and cube. spheres can bounce and don't have to travel to
/// direct neighbours. that's it. it should also roll as it's bouncing" — DeaCube/SphereBody.cs). On V7ChecksB's test song: the HUD's shape switch,
/// a sphere drawn with two clicks (a leap: 2 nodes, no line fill) next to a cube drawn the same way (the line fills), the arc preview, undo / redo,
/// the save, grid paths, and the bounce in playback: higher than a cube's hop, upright, the seams rolling by distance ÷ radius.
/// Report: Captures/sp8_report.txt; captures sp8_hud.png, sp8_bounce.png.
/// </summary>
public static class V8ChecksSphere
{
    public static string Report = "";
    public static bool Done = true;
    static int num, pushes;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "sp8_report.txt");
    static SongManager SM => SongManager.I;

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" SP-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }
    static void OnHistory() { pushes++; }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(Guarded(captures));
        return "started";
    }

    static IEnumerator Guarded(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("V8ChecksSphere ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog; History.OnChanged += OnHistory;
        var saves = V3Fixes.SnapshotSaves();
        var body = Routine(sb, captures);
        while (true)
        {
            object cur;
            try { if (!body.MoveNext()) break; cur = body.Current; }
            catch (Exception e) { Line(sb, false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
            yield return cur;
        }
        try { Restore(); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        Application.logMessageReceived -= OnLog; History.OnChanged -= OnHistory;
        Line(sb, errors.Count == 0, "no console errors during the run", errors.Count == 0 ? "0" : string.Join(" | ", errors.Take(4).ToArray()));
        int pass = 0, fail = 0; foreach (var l in sb.ToString().Split('\n')) { if (l.StartsWith("PASS")) pass++; else if (l.StartsWith("FAIL")) fail++; }
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Done = true;
        Flush(sb);
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);   // the real mouse is ignored
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) { PathManager.I.PutDown(); PathManager.I.SetBrushShape(false); }
    }

    static void Restore()
    {
        if (PathManager.I != null) { PathManager.I.PutDown(); PathManager.I.SetBrushShape(false); }
        PathManager.SimOnly = false;
        GlobalClock.Stop();
        Clipboard.ClearPaths(); Clipboard.Clear();
    }

    static KeyBlock ChordAt(int col) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == col);
    static Vector3 ScreenOf(TileInteraction t) { var cam = Camera.main; return cam != null && t != null ? cam.WorldToScreenPoint(t.Top) : Vector3.zero; }
    static IEnumerator Click(TileInteraction t)
    {
        Vector3 s = ScreenOf(t);
        PathManager.SimOnly = true; PathManager.SimPos = s;
        PathManager.I.SimPointer(s, true, true, false);
        yield return null;
        PathManager.I.SimPointer(s, false, false, true);
        yield return null;
    }
    static IEnumerator Hover(TileInteraction t)
    {
        Vector3 s = ScreenOf(t);
        PathManager.SimOnly = true; PathManager.SimPos = s;
        for (int i = 0; i < 4; i++) { PathManager.I.SimPointer(s, false, false, false); yield return null; }
    }
    static List<AudioCube> On(KeyBlock kb) => SequenceMaster.Cubes.Where(c => c != null && c.isFinalized && c.nodes.Count > 0 && c.nodes[0] != null && c.nodes[0].island == kb).ToList();
    static bool Ball(AudioCube c) { var mf = c != null ? c.GetComponent<MeshFilter>() : null; return mf != null && mf.sharedMesh == AudioCube.BallMesh; }
    static bool SeamsShown(AudioCube c) { var s = c != null ? c.transform.Find("Seams") : null; var r = s != null ? s.GetComponent<MeshRenderer>() : null; return r != null && r.enabled && s.gameObject.activeInHierarchy; }
    static string Spheres() => SequenceMaster.Cubes.Count(c => c != null && c.sphere) + " (" + string.Join(" ", SequenceMaster.Cubes.Where(c => c != null && c.sphere).Select(c => (c.isFinalized ? "F" : "d") + c.nodes.Count + "@" + (c.nodes.Count > 0 && c.nodes[0] != null && c.nodes[0].island != null ? c.nodes[0].island.name : "?")).ToArray()) + ")";
    static string Cells(AudioCube c) => c == null ? "-" : string.Join(" ", c.nodes.Select(n => n.gridX + "," + n.gridZ).ToArray());

    static IEnumerator Routine(StringBuilder sb, bool captures)
    {
        Prepare();
        V7ChecksB.LoadTest();
        yield return Frames(3);
        var pm = PathManager.I;
        var g0 = ChordAt(0);
        if (g0 == null) { Line(sb, false, "the test song has a chord grid on column 0", "none"); yield break; }
        int cx = g0.cols - 1, cz = g0.rows - 1;
        Info(sb, "grid " + g0.name + " " + g0.cols + "×" + g0.rows + ", brush " + pm.BrushTicks);
        if (OrbitCamera.I != null) OrbitCamera.I.FrameBounds(g0.WorldBounds, 0.1f, true);
        yield return Wait(0.4f);

        // ---- the HUD's shape switch
        var hud = HudInstruments.I;
        var bCube = hud != null ? hud.ShapeButton(false) : null; var bBall = hud != null ? hud.ShapeButton(true) : null;
        bool before = pm.BrushSphere, roundBefore = hud != null && hud.ChipsRound;
        if (bBall != null) bBall.onClick?.Invoke();
        yield return Frames(3);
        bool chipsRound = hud != null && hud.ChipsRound && hud.ChipOf(0).Round && hud.ChipOf(9).Round;
        Line(sb, bCube != null && bBall != null && !before && !roundBefore && pm.BrushSphere && chipsRound,
             "the shape switch above the instrument column chooses cube or ball (cube by default); on ball the chips turn into balls",
             "buttons " + (bCube != null) + "/" + (bBall != null) + ", before sphere " + before + ", after " + pm.BrushSphere + ", chips round " + chipsRound);
        if (captures) yield return Shot("sp8_hud.png");

        // ---- drawing a sphere: two clicks = a leap (2 nodes)
        int u0 = History.UndoCount;
        pm.PickUpCube(0);
        var A = g0.GetTile(0, 0); var B = g0.GetTile(cx, cz); var C = g0.GetTile(cx, 0);
        yield return Click(A);
        var draft = pm.Draft;
        bool draftBall = draft != null && draft.sphere && Ball(draft) && !SeamsShown(draft);
        yield return Hover(C);
        int pts = pm.DraftPreviewPoints; float top = pm.DraftPreviewTop;
        Line(sb, draftBall && pts == 15 && top > C.Top.y + 0.3f,
             "a sphere draft is a ball (a hologram: no seams yet); hovering a far tile previews the leap as a 15-point arc above the tiles",
             "draft ball " + draftBall + ", preview points " + pts + ", arc top +" + (top - C.Top.y).ToString("0.00"));
        yield return Click(B);
        int draftNodes = draft != null ? draft.nodes.Count : -1;
        int p0 = pushes;
        yield return Click(B);   // the end tile again finishes
        yield return Frames(3);
        var ball = On(g0).FirstOrDefault(c => c.sphere);
        Line(sb, draftNodes == 2 && ball != null && ball.nodes.Count == 2 && ball.nodes[0] == A && ball.nodes[1] == B && pushes == p0 + 1,
             "a click on a far tile adds JUST that tile (a leap, no line of tiles): the finished sphere has 2 nodes, one History entry",
             "draft nodes " + draftNodes + ", cells " + Cells(ball) + ", pushes +" + (pushes - p0));
        yield return Wait(0.5f);
        Line(sb, ball != null && Ball(ball) && SeamsShown(ball) && ball.ToState().sphere,
             "the finished sphere is a solid ball with its two crossed seams and saves as sphere",
             "ball " + Ball(ball) + ", seams " + SeamsShown(ball) + ", state " + (ball != null && ball.ToState().sphere));

        // ---- the same two clicks with the cube shape fill the line (as before)
        hud.ShapeButton(false).onClick?.Invoke();
        yield return Frames(3);
        pm.PickUpCube(1);
        int lx = Mathf.Min(3, cx);   // 4 one-beat notes fill the 4-beat grid exactly (a longer line would hit the capacity)
        var A1 = g0.GetTile(0, 1); var B1 = g0.GetTile(lx, 1);
        yield return Click(A1); yield return Click(B1); yield return Click(B1);
        yield return Frames(3);
        var cube = On(g0).FirstOrDefault(c => !c.sphere && c.instrument == 1);
        Line(sb, cube != null && cube.nodes.Count == lx + 1 && !Ball(cube) && !hud.ChipsRound,
             "a cube drawn with the same two clicks still steps through every tile between (the line fills), and the chips are cubes again",
             "cells " + Cells(cube) + ", chips round " + hud.ChipsRound);
        pm.PutDown();
        yield return Frames(2);
        Info(sb, "undo stack: " + u0 + " before the sphere, " + History.UndoCount + " after the cube");

        // ---- undo / redo / the save
        // (an undo / redo rebuilds the islands: the grid and its tiles are looked up again after each)
        History.Undo(); yield return Frames(3);   // the cube
        History.Undo(); yield return Frames(3);   // the sphere
        Info(sb, "after undo ×2: spheres in the world " + Spheres() + ", undo " + History.UndoCount + " redo " + History.RedoCount);
        g0 = ChordAt(0);
        bool gone = g0 != null && On(g0).All(c => !c.sphere);
        History.Redo(); yield return Frames(3);
        Info(sb, "after redo ×1: spheres in the world " + Spheres() + ", undo " + History.UndoCount + " redo " + History.RedoCount);
        g0 = ChordAt(0);
        var back = SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.sphere && c.isFinalized && c.nodes.Count > 0);
        bool backOk = back != null && Ball(back) && back.nodes.Count == 2; string backShown = Cells(back) + " ball " + Ball(back);   // (read now: the next redo rebuilds every cube)
        History.Redo(); yield return Frames(3);
        g0 = ChordAt(0);
        Info(sb, "after redo: " + On(g0).Count + " finished cubes on the grid, " + On(g0).Count(c => c.sphere) + " sphere");
        string json = SongState.Capture().ToJson();
        var loaded = SongState.FromJson(json);
        bool saved = json.Contains("\"sphere\":true") && loaded.cubes.Count(s => s.sphere) == 1 && loaded.cubes.Count(s => !s.sphere) >= 1;
        Line(sb, gone && backOk && saved,
             "undo takes the sphere away, redo brings it back as a ball with its leap; the song file keeps the shape",
             "gone " + gone + ", back " + backShown + ", saved " + saved);

        // ---- the bounce and the roll in playback
        ball = On(g0).FirstOrDefault(c => c.sphere);
        if (ball == null) { Line(sb, false, "a sphere to play", "none"); yield break; }
        A = ball.nodes[0]; B = ball.nodes[1];
        Vector3 a = A.Top, b = B.Top; Vector3 ab = b - a; ab.y = 0f; float abLen = ab.magnitude;
        float gap = AudioCube.GridGap(A, B), want = AudioCube.BounceHeight(gap);
        FocusLoop.Begin(0);   // column 0 loops: a late start never misses the only pass of the leap
        GlobalClock.Seek(0); GlobalClock.Play();
        float t0 = Time.realtimeSinceStartup;
        float restA = float.NaN, restB = float.NaN, rollStart = float.NaN, rollEnd = float.NaN, maxUp = 0f, maxTilt = 0f, rSum = 0f; int rN = 0;
        Quaternion seam0 = Quaternion.identity, seam1 = Quaternion.identity; var seamT = ball.transform.Find("Seams");
        var ys = new List<KeyValuePair<float, float>>();
        bool inFlight = false, flown = false;
        while (Time.realtimeSinceStartup - t0 < 8f && !flown)
        {
            yield return null;
            Vector3 p = ball.transform.position; Vector3 d = p - a; d.y = 0f;
            float u = abLen > 1e-4f ? Vector3.Dot(d, ab) / (abLen * abLen) : 0f;
            float side = (d - ab * u).magnitude;
            if (!inFlight)
            {
                if (u < 0.01f && side < 0.05f) { restA = p.y; rollStart = ball.RolledDegrees; seam0 = seamT != null ? seamT.localRotation : Quaternion.identity; rSum += 0.5f * ball.transform.lossyScale.x; rN++; }
                else if (u > 0.02f && u < 0.6f && !float.IsNaN(restA)) inFlight = true;
            }
            if (inFlight)
            {
                ys.Add(new KeyValuePair<float, float>(u, p.y));
                maxTilt = Mathf.Max(maxTilt, Quaternion.Angle(ball.transform.rotation, Quaternion.identity));
                if (u > 0.99f && side < 0.05f) { flown = true; restB = p.y; rollEnd = ball.RolledDegrees; seam1 = seamT != null ? seamT.localRotation : Quaternion.identity; }
            }
        }
        // settle on B a moment to read its rest height
        if (flown) { yield return Frames(2); restB = Mathf.Min(restB, ball.transform.position.y); }
        foreach (var kv in ys) maxUp = Mathf.Max(maxUp, kv.Value - Mathf.Lerp(restA, restB, Mathf.Clamp01(kv.Key)));
        float r = rN > 0 ? rSum / rN : 0.3f;
        float rolled = rollEnd - rollStart, expect = abLen / Mathf.Max(0.05f, r) * Mathf.Rad2Deg;
        Line(sb, flown && maxUp > ProjectConfig.cubeHopIntensity * 1.2f && maxUp > want * 0.75f,
             "in playback the sphere BOUNCES across the leap — higher than a cube's hop, as high as the leap asks (" + gap.ToString("0.0") + " cells)",
             "flew " + flown + ", bounce " + maxUp.ToString("0.00") + " (cube hop " + ProjectConfig.cubeHopIntensity.ToString("0.00") + ", wanted " + want.ToString("0.00") + "), " + ys.Count + " frames");
        Line(sb, flown && rolled > expect * 0.75f && rolled < expect * 1.35f && Quaternion.Angle(seam0, seam1) > 15f && maxTilt < 2f,
             "it ROLLS while it bounces: the seams turn by distance ÷ radius over the leap while the body stays upright (the squash stays vertical)",
             "rolled " + rolled.ToString("0") + "° (expected ≈ " + expect.ToString("0") + "° for " + abLen.ToString("0.00") + " u at r " + r.ToString("0.00") + "), seams turned " + Quaternion.Angle(seam0, seam1).ToString("0") + "°, max tilt " + maxTilt.ToString("0.0") + "°");
        if (captures)
        {
            // a frame mid-bounce on a later pass
            float t1 = Time.realtimeSinceStartup; bool shot = false;
            while (Time.realtimeSinceStartup - t1 < 8f && !shot)
            {
                yield return null;
                Vector3 p = ball.transform.position; Vector3 d = p - a; d.y = 0f;
                float u = Vector3.Dot(d, ab) / Mathf.Max(1e-4f, abLen * abLen);
                if (u > 0.42f && u < 0.6f) { shot = true; yield return Shot("sp8_bounce.png"); }
            }
            Info(sb, "bounce capture " + shot);
        }
        GlobalClock.Stop(); GlobalClock.Seek(0); FocusLoop.Dismiss();
        yield return Frames(3);

        // ---- grid paths keep the shape
        var g2 = ChordAt(2);
        Clipboard.CopyPaths(g0);
        var madeList = g2 != null ? Clipboard.PastePathsOn(g2) : null; int made = madeList != null ? madeList.Count : -1;
        yield return Frames(3);
        var pasted = g2 != null ? On(g2).FirstOrDefault(c => c.sphere) : null;
        Line(sb, made >= 2 && pasted != null && Ball(pasted) && pasted.nodes.Count == 2,
             "copying a grid's paths onto another chord keeps the sphere a ball with its leap",
             "pasted " + made + ", sphere " + Cells(pasted) + " ball " + Ball(pasted));
    }
}
