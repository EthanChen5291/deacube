using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// v7 integration battery: the v6 battery (every suite since v3; pre-v6 suites with PathManager.AutoHand ON) followed by the v7 package suites
/// (V7ChecksK / O / B / A / H / D / W / U1 / U2) and V7Integration with AutoHand OFF — resetting the modes (and the grid selection) between them —
/// then one summary (Captures/v7_all_report.txt). v7 suites are found by name (a static RunAll / Run and a static Done), so this file compiles
/// before they exist. Poll <see cref="Done"/> or the file.
/// </summary>
public static class V7Suites
{
    public static bool Done = true;
    public static string Progress = "";
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);

    struct Suite { public string name, report; public Func<string> start; public Func<bool> done; public float timeout; public bool v6; }

    /// <summary>Runs every suite; <paramref name="only"/> = a comma list of suite-name prefixes to run (empty = all).</summary>
    public static string RunAll(bool captures = false, string only = "")
    {
        if (!Done) return "already running";
        if (SequenceMaster.I == null) return "no SequenceMaster";
        Done = false;
        SequenceMaster.I.StartCoroutine(Routine(captures, only ?? ""));
        return "started";
    }

    static IEnumerator WaitFor(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
    }

    static void Reset(bool autoHand)
    {
        try
        {
            CubeInspector.CloseImmediate();
            if (Presenter.Active) Presenter.Exit();
            FocusLoop.Dismiss();
            GlobalClock.Stop();
            AudioCube.SnapAllHome();                        // v7 §12.1: a stop sends cubes home in real time; suites start from the snapped pose
            if (MainMenu.IsShown) MainMenu.Hide();
            WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
            if (IslandTray.IsOpen) IslandTray.Close();
            if (PathManager.I != null) PathManager.I.PutDown();
            PathManager.AutoHand = autoHand;
            if (Clipboard.HasPattern) Clipboard.Clear();   // a pattern left on the clipboard shows the HUD's clipboard chip (one more hit target)
            GridSelection.Clear();                          // v7: a selection shows the selection bar (more hit targets)
            SongIO.QuitAutosave = false;
        }
        catch (Exception e) { Debug.LogWarning("V7Suites reset: " + e.Message); }
    }

    static bool Wanted(string name, string only)
    {
        if (string.IsNullOrEmpty(only)) return true;
        foreach (var p in only.Split(',')) { var q = p.Trim(); if (q.Length > 0 && name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return true; }
        return false;
    }

    /// <summary>A suite found by type name: its first public static RunAll / Run (bool parameters get <paramref name="captures"/>, strings "",
    /// others their defaults) and its static Done (field or property). Missing → a suite that reports "missing".</summary>
    static Suite ByName(string typeName, string label, string report, bool captures, float timeout)
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType(typeName); if (t != null) break; }
        MethodInfo run = null;
        if (t != null)
            foreach (var n in new[] { "RunAll", "Run" })
            {
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static)) if (m.Name == n) { run = m; break; }
                if (run != null) break;
            }
        Func<bool> done = () => true;
        if (t != null)
        {
            var f = t.GetField("Done", BindingFlags.Public | BindingFlags.Static);
            var p = t.GetProperty("Done", BindingFlags.Public | BindingFlags.Static);
            if (f != null) done = () => (bool)f.GetValue(null);
            else if (p != null) done = () => (bool)p.GetValue(null, null);
        }
        Func<string> start = () => "missing";
        if (run != null)
        {
            var ps = run.GetParameters();
            start = () =>
            {
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt == typeof(bool)) args[i] = captures;
                    else if (pt == typeof(string)) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : "";
                    else args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (pt.IsValueType ? Activator.CreateInstance(pt) : null);
                }
                var r = run.Invoke(null, args);
                return r != null ? r.ToString() : "ok";
            };
        }
        return new Suite { name = label, report = report, start = start, done = done, timeout = timeout, v6 = true };
    }

    static IEnumerator Routine(bool captures, string only)
    {
        SongIO.QuitAutosave = false;
        var suites = new List<Suite>
        {
            new Suite { name = "v3 bundle (D, V2 RunAll/RunWorld/RunIntegration, B, A)", report = "a_suites_report.txt", start = V3ChecksA.RunSuites, done = () => V3ChecksA.SuitesDone, timeout = 1500f },
            new Suite { name = "V3Fixes", report = "v3fixes_report.txt", start = () => V3Fixes.Run(captures), done = () => V3Fixes.Done, timeout = 600f },
            new Suite { name = "V3ChecksC (present)", report = "v3c_report.txt", start = () => V3ChecksC.Run("all"), done = () => V3ChecksC.Done, timeout = 600f },
            new Suite { name = "V3ChecksE (look)", report = "v3checksE_report.txt", start = V3ChecksE.Run, done = () => V3ChecksE.Done, timeout = 400f },
            new Suite { name = "V3Integration", report = "v3_integration_report.txt", start = () => V3Integration.Run(captures), done = () => V3Integration.Done, timeout = 400f },
            new Suite { name = "V4ChecksK (columns)", report = "v4k_report.txt", start = V4ChecksK.RunAll, done = () => V4ChecksK.Done, timeout = 900f },
            new Suite { name = "V4ChecksR (rhythm)", report = "v4r_report.txt", start = () => V4ChecksR.Run(true, captures), done = () => V4ChecksR.Done, timeout = 900f },
            new Suite { name = "V4ChecksU1 (HUD)", report = "u1_report.txt", start = () => V4ChecksU1.Run(captures), done = () => V4ChecksU1.Done, timeout = 900f },
            new Suite { name = "V4ChecksU2 (inspector)", report = "v4checksU2_report.txt", start = () => V4ChecksU2.Run(captures), done = () => V4ChecksU2.Done, timeout = 900f },
            new Suite { name = "V9ChecksPresent (present)", report = "w9_report.txt", start = () => V9ChecksPresent.RunAll(captures), done = () => V9ChecksPresent.Done, timeout = 900f },
            ByName("V9ChecksRiser", "V9ChecksRiser (riser, crash, sections, echoes, stairs)", "riser9_report.txt", captures, 600f),
            ByName("V9ChecksLights", "V9ChecksLights (stage lights: dim stage, melody spotlight)", "lights9_report.txt", captures, 900f),
            new Suite { name = "V4ChecksT (title)", report = "t_report.txt", start = () => V4ChecksT.Run(captures), done = () => V4ChecksT.Done, timeout = 600f },
            new Suite { name = "V4Integration", report = "v4_integration_report.txt", start = () => V4Integration.Run(true), done = () => V4Integration.Done, timeout = 600f },
            new Suite { name = "V5ChecksK (drag, belt, key)", report = "k5_report.txt", start = V5ChecksK.RunAll, done = () => V5ChecksK.Done, timeout = 900f },
            new Suite { name = "V5ChecksR (hearing notes)", report = "r5_report.txt", start = () => V5ChecksR.Run(captures), done = () => V5ChecksR.Done, timeout = 900f },
            new Suite { name = "V5ChecksU (vibe cards, HUD)", report = "u5_report.txt", start = () => V5ChecksU.Run(captures), done = () => V5ChecksU.Done, timeout = 900f },
            new Suite { name = "V5ChecksT (title gallery)", report = "t5_report.txt", start = () => V5ChecksT.Run(captures, true), done = () => V5ChecksT.Done, timeout = 900f },
            new Suite { name = "V5ChecksG (gallery songs)", report = "g5_report.txt", start = () => V5ChecksG.Run(captures, "", null, false), done = () => V5ChecksG.Done, timeout = 1200f },
            new Suite { name = "V5Integration", report = "v5_integration_report.txt", start = () => V5Integration.Run(captures), done = () => V5Integration.Done, timeout = 900f },
            ByName("V6ChecksK", "V6ChecksK (keyboard, carry, tower)", "k6_report.txt", captures, 900f),
            ByName("V6ChecksA", "V6ChecksA (voices, presses)", "a6_report.txt", captures, 900f),
            ByName("V6ChecksH", "V6ChecksH (hand, paste, carried cubes)", "h6_report.txt", captures, 900f),
            ByName("V6ChecksW", "V6ChecksW (tower, carry magic)", "w6_report.txt", captures, 900f),
            ByName("V6ChecksU1", "V6ChecksU1 (cards, header, rail)", "u1_6_report.txt", captures, 900f),
            ByName("V6ChecksU2", "V6ChecksU2 (instruments, cursor, clipboard)", "u2_6_report.txt", captures, 900f),
            ByName("V6Integration", "V6Integration", "v6_integration_report.txt", captures, 900f),
            ByName("V7ChecksK", "V7ChecksK (sections, layout, grounds, hold)", "k7_report.txt", captures, 1200f),
            ByName("V7ChecksO", "V7ChecksO (song ops, selection)", "o7_report.txt", captures, 1200f),
            ByName("V7ChecksB", "V7ChecksB (stairs, phrase, plinths)", "b7_report.txt", captures, 1200f),
            ByName("V7ChecksA", "V7ChecksA (layers, bends, riser)", "a7_report.txt", captures, 1200f),
            ByName("V7ChecksH", "V7ChecksH (cube ideas, motions)", "h7_report.txt", captures, 1200f),
            ByName("V7ChecksD", "V7ChecksD (size first, phrases, marquee)", "d7_report.txt", captures, 1200f),
            ByName("V7ChecksW", "V7ChecksW (world magic)", "w7_report.txt", captures, 1200f),
            ByName("V7ChecksU1", "V7ChecksU1 (cards, headers, sections UI)", "u1_7_report.txt", captures, 1200f),
            ByName("V7ChecksU2", "V7ChecksU2 (cube card, size row, selection bar)", "u2_7_report.txt", captures, 1200f),
            ByName("V8ChecksHands", "V8ChecksHands (the keyboard's sphere hands, root keys, melody card)", "h8_report.txt", captures, 900f),
            ByName("V7ChecksPaths", "V7ChecksPaths (grid paths, card preview)", "gp7_report.txt", captures, 900f),
            ByName("V8ChecksSphere", "V8ChecksSphere (sphere pieces: leap, bounce, roll)", "sp8_report.txt", captures, 600f),
            ByName("V7Integration", "V7Integration", "v7_integration_report.txt", captures, 1200f),
        };
        var sb = new StringBuilder();
        sb.Append("V7Suites ").Append(DateTime.Now.ToString("HH:mm:ss")).Append(string.IsNullOrEmpty(only) ? "" : "  only: " + only).Append('\n');
        int totalPass = 0, totalFail = 0;
        var fails = new StringBuilder();
        foreach (var s in suites)
        {
            if (!Wanted(s.name, only)) continue;
            Progress = s.name;
            Reset(!s.v6);
            yield return null; yield return null;
            string r = "";
            float t0 = Time.realtimeSinceStartup;
            try { File.Delete(Cap(s.report)); } catch (Exception) { }   // a stale report from an earlier run must not count
            try { r = s.start(); } catch (Exception e) { r = "threw: " + (e.InnerException != null ? e.InnerException.Message : e.Message); }
            // give the suite a frame to flip its Done flag down before waiting on it
            yield return null; yield return null;
            yield return WaitFor(s.done, s.timeout);
            float secs = Time.realtimeSinceStartup - t0;
            yield return new WaitForSecondsRealtime(0.5f);
            int p = 0, f = 0;
            string text = "";
            try { text = File.Exists(Cap(s.report)) ? File.ReadAllText(Cap(s.report)) : ""; } catch (Exception) { }
            foreach (var line in text.Split('\n'))
            {
                var l = line.TrimStart();
                var m = System.Text.RegularExpressions.Regex.Match(l, @"^(\d+\.\s*)?(PASS|FAIL)\b");   // "PASS …", "12. PASS …" or "PASS 12. …"
                if (!m.Success) continue;
                if (m.Groups[2].Value == "PASS") p++;
                else { f++; fails.Append("  [").Append(s.name).Append("] ").Append(l.Length > 240 ? l.Substring(0, 240) : l).Append('\n'); }
            }
            if (p + f == 0) { f++; fails.Append("  [").Append(s.name).Append("] FAIL no report lines (").Append(s.report).Append(", start: ").Append(r).Append(")\n"); }
            bool timedOut = false; try { timedOut = !s.done(); } catch (Exception) { }
            totalPass += p; totalFail += f;
            sb.Append(string.Format("{0,-58} {1,4} pass {2,3} fail  {3,6:F0} s  start: {4}{5}\n", s.name, p, f, secs, r, timedOut ? "  (TIMED OUT)" : ""));
            try { File.WriteAllText(Cap("v7_all_report.txt"), sb.ToString() + "...running\n"); } catch (Exception) { }
        }
        Reset(false);
        sb.Append(string.Format("TOTAL {0} pass, {1} fail\n", totalPass, totalFail));
        if (fails.Length > 0) sb.Append("FAILS:\n").Append(fails);
        sb.Append("synth: ").Append(Synth.Stats()).Append('\n');
        try { File.WriteAllText(Cap("v7_all_report.txt"), sb.ToString()); } catch (Exception) { }
        Progress = "done";
        Done = true;
    }
}
