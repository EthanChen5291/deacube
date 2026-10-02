using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v5 integration battery: every suite of the project, one after another in one Play session — the v4 battery's suites (the v3 bundle, V3Fixes,
/// V3ChecksC, V3ChecksE, V3Integration, V4ChecksK / R / U1 / U2 / P / T, V4Integration), then V5ChecksK / R / U / P / T / G and V5Integration —
/// resetting the modes between them, then one summary: per suite its PASS / FAIL counts (from its report file) and every FAIL line
/// (Captures/v5_all_report.txt). Poll <see cref="Done"/> or the file. Each suite restores the saves it touches; this adds nothing to them.
/// </summary>
public static class V5Suites
{
    public static bool Done = true;
    public static string Progress = "";
    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);

    struct Suite { public string name, report; public Func<string> start; public Func<bool> done; public float timeout; }

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

    static void Reset()
    {
        try
        {
            CubeInspector.CloseImmediate();
            if (Presenter.Active) Presenter.Exit();
            FocusLoop.Dismiss();
            GlobalClock.Stop();
            if (MainMenu.IsShown) MainMenu.Hide();
            WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
            if (IslandTray.IsOpen) IslandTray.Close();
            SongIO.QuitAutosave = false;
        }
        catch (Exception e) { Debug.LogWarning("V5Suites reset: " + e.Message); }
    }

    static bool Wanted(string name, string only)
    {
        if (string.IsNullOrEmpty(only)) return true;
        foreach (var p in only.Split(',')) { var q = p.Trim(); if (q.Length > 0 && name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return true; }
        return false;
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
            new Suite { name = "V4ChecksT (title)", report = "t_report.txt", start = () => V4ChecksT.Run(captures), done = () => V4ChecksT.Done, timeout = 600f },
            new Suite { name = "V4Integration", report = "v4_integration_report.txt", start = () => V4Integration.Run(true), done = () => V4Integration.Done, timeout = 600f },
            new Suite { name = "V5ChecksK (drag, belt, key)", report = "k5_report.txt", start = V5ChecksK.RunAll, done = () => V5ChecksK.Done, timeout = 900f },
            new Suite { name = "V5ChecksR (hearing notes)", report = "r5_report.txt", start = () => V5ChecksR.Run(captures), done = () => V5ChecksR.Done, timeout = 900f },
            new Suite { name = "V5ChecksU (vibe cards, HUD)", report = "u5_report.txt", start = () => V5ChecksU.Run(captures), done = () => V5ChecksU.Done, timeout = 900f },
            new Suite { name = "V5ChecksT (title gallery)", report = "t5_report.txt", start = () => V5ChecksT.Run(captures, true), done = () => V5ChecksT.Done, timeout = 900f },
            new Suite { name = "V5ChecksG (gallery songs)", report = "g5_report.txt", start = () => V5ChecksG.Run(captures, "", null, false), done = () => V5ChecksG.Done, timeout = 1200f },
            new Suite { name = "V5Integration", report = "v5_integration_report.txt", start = () => V5Integration.Run(captures), done = () => V5Integration.Done, timeout = 900f },
        };
        var sb = new StringBuilder();
        sb.Append("V5Suites ").Append(DateTime.Now.ToString("HH:mm:ss")).Append(string.IsNullOrEmpty(only) ? "" : "  only: " + only).Append('\n');
        int totalPass = 0, totalFail = 0;
        var fails = new StringBuilder();
        foreach (var s in suites)
        {
            if (!Wanted(s.name, only)) continue;
            Progress = s.name;
            Reset();
            yield return null; yield return null;
            string r = "";
            float t0 = Time.realtimeSinceStartup;
            try { r = s.start(); } catch (Exception e) { r = "threw: " + e.Message; }
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
            bool timedOut = false; try { timedOut = !s.done(); } catch (Exception) { }
            totalPass += p; totalFail += f;
            sb.Append(string.Format("{0,-58} {1,4} pass {2,3} fail  {3,6:F0} s  start: {4}{5}\n", s.name, p, f, secs, r, timedOut ? "  (TIMED OUT)" : ""));
            try { File.WriteAllText(Cap("v5_all_report.txt"), sb.ToString() + "...running\n"); } catch (Exception) { }
        }
        Reset();
        sb.Append(string.Format("TOTAL {0} pass, {1} fail\n", totalPass, totalFail));
        if (fails.Length > 0) sb.Append("FAILS:\n").Append(fails);
        sb.Append("synth: ").Append(Synth.Stats()).Append('\n');
        try { File.WriteAllText(Cap("v5_all_report.txt"), sb.ToString()); } catch (Exception) { }
        Progress = "done";
        Done = true;
    }
}
