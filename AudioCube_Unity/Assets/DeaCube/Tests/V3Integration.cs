using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// v3 end-to-end run (integration): the menu → New (dice) → first island + tray deck + tutorial → pick a sound → draw a path by clicks
/// → play → inspect + a grid edit → close → tray card → merge → recording → present → exit → Home (autosave) → Continue → the user's
/// song (read only) → present. Asserts locks, counts and Synth late/errors at each stage, counts console errors, writes captures
/// i_*.png. Poll Done / Report. Restores the autosave file and the tutorial prefs; never writes the user's save.
/// </summary>
public static class V3Integration
{
    public static bool Done;
    public static string Report = "";
    static StringBuilder sb;
    static int errors;
    static readonly List<string> errLines = new List<string>();
    static int pass, fail;
    static Dictionary<string, V3Fixes.FileSnap> saves;

    static string Cap(string n) => Path.Combine(V2Checks.CapturePath, n);

    public static string Run(bool captures = true)
    {
        Done = false; Report = "";
        if (SequenceMaster.I == null) return "no SequenceMaster";
        SequenceMaster.I.StartCoroutine(Routine(captures));
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
    }

    static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }

    static IEnumerator Until(Func<bool> cond, float timeout)
    {
        float t = 0f;
        while (t < timeout) { bool ok = false; try { ok = cond(); } catch (Exception) { } if (ok) yield break; t += Time.unscaledDeltaTime; yield return null; }
    }

    static IEnumerator Shot(string file, bool captures)
    {
        if (!captures) yield break;
        ScreenCapture.CaptureScreenshot(Cap(file), 1);
        yield return null; yield return null;
    }

    static string Md5(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var md5 = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static bool OnScreen(Vector3 s) => s.z > 0f && s.x > 4f && s.y > 4f && s.x < Screen.width - 4f && s.y < Screen.height - 4f;

    static IEnumerator Click(Vector3 world)
    {
        var cam = Camera.main;
        Vector3 s = cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero;
        PathManager.I.SimPointer(s, true, true, false);
        yield return null;
        PathManager.I.SimPointer(s, false, false, true);
        yield return null;
    }

    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); errors = 0; errLines.Clear(); pass = 0; fail = 0;
        Application.logMessageReceived += OnLog;
        string userMd5 = Md5(SongIO.Path);
        string autoPath = SongIO.AutosavePath;
        string autoBefore = File.Exists(autoPath) ? File.ReadAllText(autoPath) : null;
        saves = V3Fixes.SnapshotSaves();   // fix F15: New (dice) backs the replaced song up; the backups are put back at the end
        int prefStep = PlayerPrefs.GetInt(Onboarding.PrefStep, 0), prefDone = PlayerPrefs.GetInt(Onboarding.PrefDone, 0);
        int late0 = Synth.LateEvents, err0 = Synth.Errors;
        var sm = SongManager.I; var pm = PathManager.I;
        Onboarding.Suppressed = false;
        PlayerPrefs.SetInt(Onboarding.PrefDone, 0); PlayerPrefs.SetInt(Onboarding.PrefStep, 0);

        // ---- 1. the menu (shown at boot; shown again here if a previous check hid it)
        if (!MainMenu.IsShown) MainMenu.Show();
        yield return Until(() => MainMenu.State == MainMenu.Phase.Shown, 12f);   // the intro reveal ignores buttons until it settles
        yield return Wait(0.5f);
        Check(MainMenu.IsShown && WorldInput.WorldLocked && WorldInput.KeysLocked, "menu shown and owns the input", WorldInput.Describe());
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.42f, Screen.height * 0.62f));
        yield return Wait(0.25f);
        MainMenu.SimulatePointer(new Vector2(Screen.width * 0.55f, Screen.height * 0.6f));
        yield return Wait(0.2f);
        yield return Shot("i_01_menu.png", captures);
        MainMenu.ReleasePointer();

        // ---- 2. New → dice → fresh song (1 island + Moon, deck in the tray, tutorial starts)
        var newBtn = MainMenu.NewButton;
        if (newBtn != null) newBtn.Invoke(); else if (InterfaceController.I != null) InterfaceController.I.Show();
        yield return Until(() => InterfaceController.I != null && InterfaceController.I.Visible, 3f);
        yield return Wait(0.4f);
        yield return Shot("i_02_prompt.png", captures);
        Check(InterfaceController.I != null && InterfaceController.I.Visible && WorldInput.IsLockedBy("prompt"), "New opens the vibe prompt above the menu");
        if (InterfaceController.I != null && InterfaceController.I.DiceButton != null) InterfaceController.I.DiceButton.onClick?.Invoke();
        yield return Until(() => sm.HasSong && !MainMenu.IsShown && !WorldInput.WorldLocked, 8f);
        yield return Wait(1.2f);
        Check(sm.HasSong && sm.Islands.Count == 1 && sm.Moons.Count == 1 && !GlobalClock.IsPlaying, "dice builds a fresh song: 1 island + 1 Moon, stopped",
              sm.Islands.Count + " islands, " + sm.Moons.Count + " moons, playing " + GlobalClock.IsPlaying);
        Check(IslandTray.DeckCount >= 1, "the rest of the progression waits in the tray deck", "deck " + IslandTray.DeckCount);
        Check(!MainMenu.IsShown && !WorldInput.WorldLocked && !WorldInput.KeysLocked, "menu gone, world input free", WorldInput.Describe());
        Check(Onboarding.Active && Onboarding.Step >= 1, "tutorial started", "step " + Onboarding.Step);
        yield return Shot("i_03_world_tutorial.png", captures);

        // ---- 3. a sound and a path drawn by clicks (the v3 fix: one cube, however many tiles)
        pm.SelectInstrument(3);
        yield return null;
        var isl = sm.Islands[0];
        int cubes0 = SequenceMaster.Cubes.Count;
        var tiles = new List<TileInteraction> { isl.GetTile(0, 0), isl.GetTile(1, 0), isl.GetTile(2, 1), isl.GetTile(3, 1) };
        bool visible = Camera.main != null; foreach (var t in tiles) if (t == null || Camera.main == null || !OnScreen(Camera.main.WorldToScreenPoint(t.Top))) visible = false;
        if (!visible) { Check(false, "world camera and island 0 visible for the clicks — aborting the rest"); Finish(userMd5, autoPath, autoBefore, prefStep, prefDone); yield break; }
        Check(visible, "island 0's tiles are on screen for the clicks");
        foreach (var t in tiles) yield return Click(t.Top);
        yield return Click(tiles[tiles.Count - 1].Top);   // the end tile again: done
        yield return Wait(0.4f);
        AudioCube mine = SequenceMaster.Cubes.Count > cubes0 ? SequenceMaster.Cubes[SequenceMaster.Cubes.Count - 1] : null;
        Check(mine != null && SequenceMaster.Cubes.Count == cubes0 + 1 && mine.nodes.Count == 4 && !pm.IsDrawing && mine.instrument == 3,
              "clicking 4 tiles then the last again draws ONE cube with 4 nodes", (SequenceMaster.Cubes.Count - cubes0) + " new cubes, nodes " + (mine != null ? mine.nodes.Count : -1));

        // ---- 4. play
        GlobalClock.Play();
        yield return Wait(2f);
        yield return Shot("i_04_playing.png", captures);

        // ---- 5. inspect, edit the path on the flat grid, close
        if (mine != null)
        {
            CubeInspector.Open(mine);
            yield return Wait(1.0f);
            Check(CubeInspector.IsOpen && pm.selectedCube == mine && WorldInput.IsLockedBy("inspector") && !mine.BodyVisible, "inspector open: selected, world locked, world body hidden");
            yield return Shot("i_05_inspect.png", captures);
            var grid = UIManager.I != null ? UIManager.I.Grid : null;
            int n0 = mine.nodes.Count;
            // v7 §19.1: a path holds as many beats as its grid — when it is full (4 quarters on a fresh one-measure grid) the click is refused
            // and the "capacity reached" chip offers EXPAND; with room it appends
            float capB = PathManager.CapacityOf(mine.Island), usedB = 0f; for (int i = 0; i < mine.nodes.Count; i++) usedB += mine.DurBeats(i);
            bool full = capB > 0f && mine.HasDurations && usedB + mine.DurBeats(mine.nodes.Count - 1) > capB + 1e-4f;
            int ref0 = grid != null ? grid.CapacityRefusals : 0;
            if (grid != null) { grid.SimDownCell(4, 1); yield return null; grid.SimUp(); yield return null; }
            if (full)
                Check(grid != null && mine.nodes.Count == n0 && grid.CapacityRefusals == ref0 + 1 && grid.CapacityChipShown, "a click on an empty grid cell of a FULL grid is refused, the capacity chip offers expand (v7 §19.1)",
                      n0 + " → " + mine.nodes.Count + ", used " + usedB + " of " + capB + " beats, refusals +" + (grid != null ? grid.CapacityRefusals - ref0 : -1) + ", chip " + (grid != null && grid.CapacityChipShown));
            else
                Check(mine.nodes.Count == n0 + 1 && mine.mods.Count == mine.nodes.Count, "a click on an empty grid cell appends a node", n0 + " → " + mine.nodes.Count);
            CubeInspector.Close();
            yield return Wait(1.0f);
            Check(!CubeInspector.IsOpen && !WorldInput.WorldLocked && mine.BodyVisible && pm.selectedCube == null, "inspector closed: cube back, world free");
        }

        // ---- 6. the island tray: place the next deck card
        int deck0 = IslandTray.DeckCount, isl0 = sm.Islands.Count;
        IslandTray.Open();
        yield return Wait(0.7f);
        Check(IslandTray.IsOpen, "tray opens");
        yield return Shot("i_06_tray.png", captures);
        if (IslandTray.I != null) IslandTray.I.SimClick(0);
        yield return Wait(1.0f);
        Check(sm.Islands.Count == isl0 + 1 && IslandTray.DeckCount == deck0 - 1, "a card click places the next island", isl0 + " → " + sm.Islands.Count + " islands, deck " + deck0 + " → " + IslandTray.DeckCount);
        IslandTray.Close();
        yield return Wait(0.3f);

        // ---- 7. merge the new island onto island 0's east edge
        if (sm.Islands.Count >= 2)
        {
            bool merged = sm.MergeIslands(sm.Islands[1], sm.Islands[0], true);
            yield return Wait(1.2f);
            float gap = sm.Islands.Count >= 2 ? Mathf.Abs(sm.Islands[1].px - (sm.Islands[0].px + KeyBlock.IslandWidth)) : 99f;
            Check(merged && sm.IsSeam(0) && sm.Islands[0].group != 0 && gap < 0.02f, "merge: one group, touching edges", "gap " + gap.ToString("F3") + " group " + sm.Islands[0].group);
            if (OrbitCamera.I != null) OrbitCamera.I.FrameIsland(sm.Islands[0], false);
            yield return Wait(1.0f);
            yield return Shot("i_07_merge.png", captures);
        }

        // ---- 8. a recording of the fresh song while it plays
        if (!GlobalClock.IsPlaying) GlobalClock.Play();
        Synth.StartRecording(6f);
        yield return Until(() => Synth.RecordingDone, 9f);
        bool saved = Synth.SaveRecording(Cap("i_fresh_song.wav"));
        Check(saved && Synth.LateEvents == late0 && Synth.Errors == err0, "6 s recording of the fresh song: late 0, errors 0", Synth.Stats());

        // ---- 9. present, then back
        var cam = Camera.main; int mask0 = cam != null ? cam.cullingMask : 0;
        Presenter.Enter();
        yield return Wait(6f);
        Check(Presenter.Active && WorldInput.KeysLocked && Camera.main != null && Camera.main.cullingMask == mask0 && Presenter.Sea != null, "present: active, keys locked, the camera shows the real world (v9: islands rising from the sea)");
        yield return Shot("i_08_present.png", captures);
        yield return Wait(1.6f);
        yield return Shot("i_09_present2.png", captures);
        Presenter.Exit();
        yield return Wait(1.2f);
        Check(!Presenter.Active && !WorldInput.WorldLocked && Camera.main != null && Camera.main.cullingMask == mask0, "present exit restores the camera and the input");
        yield return Shot("i_10_after_present.png", captures);

        // ---- 10. Home (autosave) → Continue
        int islands1 = sm.Islands.Count, cubes1 = SequenceMaster.Cubes.Count;
        DateTime before = File.Exists(autoPath) ? File.GetLastWriteTimeUtc(autoPath) : DateTime.MinValue;
        MainMenu.Show();
        yield return Wait(3f);
        Check(MainMenu.IsShown && File.Exists(autoPath) && File.GetLastWriteTimeUtc(autoPath) > before, "Home: menu shown, autosave written");
        yield return Shot("i_11_home.png", captures);
        var cont = MainMenu.ContinueButton;
        if (cont != null) cont.Invoke();
        yield return Until(() => !MainMenu.IsShown && !WorldInput.WorldLocked, 6f);
        yield return Wait(0.8f);
        Check(!MainMenu.IsShown && sm.Islands.Count == islands1 && SequenceMaster.Cubes.Count == cubes1, "Continue returns to the same song", sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes");

        // ---- 11. the user's song (read only): plays, records, presents
        bool loaded = SongIO.LoadFrom(SongIO.Path);
        yield return Wait(0.5f);
        Check(loaded && sm.Islands.Count == 6 && SequenceMaster.Cubes.Count == 13, "the user's song loads", sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes");
        GlobalClock.Seek(0); GlobalClock.Play();
        Synth.StartRecording(8f);
        yield return Until(() => Synth.RecordingDone, 11f);
        saved = Synth.SaveRecording(Cap("i_user_song.wav"));
        Check(saved && Synth.LateEvents == late0 && Synth.Errors == err0, "8 s recording of the user's song: late 0, errors 0", Synth.Stats());
        Presenter.Enter();
        yield return Wait(5f);
        yield return Shot("i_12_present_user.png", captures);
        Presenter.Exit();
        yield return Wait(1.2f);
        GlobalClock.Stop();

        // ---- 12. tidy up
        Onboarding.Skip();
        yield return null;
        Finish(userMd5, autoPath, autoBefore, prefStep, prefDone);
    }

    static void Finish(string userMd5, string autoPath, string autoBefore, int prefStep, int prefDone)
    {
        Application.logMessageReceived -= OnLog;
        try { if (autoBefore != null) File.WriteAllText(autoPath, autoBefore); else if (File.Exists(autoPath)) File.Delete(autoPath); } catch (Exception) { }
        V3Fixes.RestoreSaves(saves);
        PlayerPrefs.SetInt(Onboarding.PrefStep, prefStep); PlayerPrefs.SetInt(Onboarding.PrefDone, prefDone); PlayerPrefs.Save();
        Check(Md5(SongIO.Path) == userMd5, "the user's save is untouched", userMd5);
        Check(errors == 0, "no console errors or exceptions during the run", errors + (errLines.Count > 0 ? ": " + string.Join(" || ", errLines) : ""));
        sb.Append("SUMMARY ").Append(pass).Append(" pass, ").Append(fail).Append(" fail\n");
        Report = sb.ToString();
        try { File.WriteAllText(Cap("v3_integration_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }
}
