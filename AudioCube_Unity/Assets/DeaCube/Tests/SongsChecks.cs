using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// "My songs" (SongLibrary, MenuSongs, the menu word, the HUD strip, ⌘O). Standalone: RunAll(true) in Play mode, report Captures/songs_report.txt,
/// captures songs_*.png. The player's own library is never used: the run works in a sandbox folder (SongLibrary.UseSandbox) and, as a guard,
/// snapshots the WHOLE real songs folder, the current-song PlayerPrefs key and the save slots (V3Fixes.SnapshotSaves) and puts them back at the
/// end — then checks the real folder is byte-for-byte as it was. Covers: the QuitAutosave guard on the real library, the one-time import of the
/// old save, create / save / list order, rename, duplicate, delete to the trash + restore, open (the world's song matches the file), the History
/// unbinding, the autosave guard in the sandbox, the vibe prompt's song landing as an entry, and the menu (word, shelf, rename field, armed trash
/// can, undo, pages, Esc, opening a card through the leave, Continue, the strip's "my songs" row, ⌘O).
/// </summary>
public static class SongsChecks
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "songs_report.txt");
    static SongManager SM => SongManager.I;
    static string RealRoot => Path.Combine(Application.persistentDataPath, SongLibrary.FolderName);

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" SONGS-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string where = (stack ?? "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        string line = msg + " @ " + where;
        if (errors.Count < 20) errors.Add(line.Length > 320 ? line.Substring(0, 320) : line);
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float max) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < max) yield return null; }
    static IEnumerator Shot(string file)
    {
        yield return new WaitForEndOfFrame();
        try { Directory.CreateDirectory(V2Checks.CapturePath); ScreenCapture.CaptureScreenshot(Path.Combine(V2Checks.CapturePath, file), 1); } catch (Exception) { }
        yield return null; yield return null;
    }
    static IEnumerator Key(KeyCode k) { KeyShim.Sim(k, true, true, false); yield return null; KeyShim.Clear(); yield return null; }

    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(Guarded(captures));
        return "started";
    }

    // ------------------------------------------------------------------ the guard: the player's library, prefs and saves are put back
    class FolderSnap { public bool existed; public readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(); public readonly Dictionary<string, DateTime> times = new Dictionary<string, DateTime>(); }

    static FolderSnap SnapFolder(string root)
    {
        var s = new FolderSnap { existed = Directory.Exists(root) };
        if (!s.existed) return s;
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(root.Length).TrimStart('/', '\\');
            s.files[rel] = File.ReadAllBytes(f);
            s.times[rel] = File.GetLastWriteTimeUtc(f);
        }
        return s;
    }

    static void RestoreFolder(FolderSnap s, string root)
    {
        if (!s.existed) { if (Directory.Exists(root)) Directory.Delete(root, true); return; }
        Directory.CreateDirectory(root);
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(root.Length).TrimStart('/', '\\');
            if (!s.files.ContainsKey(rel)) File.Delete(f);
        }
        foreach (var kv in s.files)
        {
            string p = Path.Combine(root, kv.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            if (!File.Exists(p) || !File.ReadAllBytes(p).SequenceEqual(kv.Value)) File.WriteAllBytes(p, kv.Value);
            File.SetLastWriteTimeUtc(p, s.times[kv.Key]);
        }
        foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            if (Directory.GetFileSystemEntries(d).Length == 0 && !s.files.Keys.Any(k => Path.Combine(root, k).StartsWith(d + Path.DirectorySeparatorChar))) Directory.Delete(d);
    }

    static string Md5(byte[] b) { using (var m = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static string Md5File(string p) { try { return File.Exists(p) ? Md5(File.ReadAllBytes(p)) : "none"; } catch (Exception) { return "unreadable"; } }

    /// <summary>A digest of a folder: every file's relative path, md5 and write time (the real library must come back exactly).</summary>
    static string FolderDigest(string root)
    {
        if (!Directory.Exists(root)) return "absent";
        var parts = new List<string>();
        foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            parts.Add(f.Substring(root.Length) + ":" + Md5(File.ReadAllBytes(f)) + ":" + File.GetLastWriteTimeUtc(f).Ticks);
        return parts.Count + " files " + Md5(Encoding.UTF8.GetBytes(string.Join("|", parts.ToArray()))).Substring(0, 12);
    }

    static IEnumerator Guarded(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("SongsChecks ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        SongIO.QuitAutosave = false;
        var saves = V3Fixes.SnapshotSaves();
        FolderSnap real = null; string realDigest = "?";
        bool prefHad = PlayerPrefs.HasKey(SongLibrary.PrefCurrent); string prefWas = PlayerPrefs.GetString(SongLibrary.PrefCurrent, "");
        string mainMd5 = Md5File(SongIO.Path);
        try { real = SnapFolder(RealRoot); realDigest = FolderDigest(RealRoot); }
        catch (Exception e) { Line(sb, false, "snapshot of the player's songs folder", e.Message); }
        Info(sb, "the player's songs folder before: " + realDigest + " (" + RealRoot + "); current-song pref " + (prefHad ? "\"" + prefWas + "\"" : "unset") + "; main save md5 " + mainMd5);
        string sandbox = Path.Combine(Application.temporaryCachePath, "songs_sandbox_" + DateTime.Now.ToString("HHmmss"));
        if (real != null)
        {
            var body = Routine(sb, captures, sandbox);
            while (true)
            {
                object cur;
                try { if (!body.MoveNext()) break; cur = body.Current; }
                catch (Exception e) { Line(sb, false, "the run threw", e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]); break; }
                yield return cur;
            }
        }
        try { RestoreWorld(); } catch (Exception e) { Info(sb, "restore: " + e.Message); }
        SongIO.QuitAutosave = false;
        V3Fixes.RestoreSaves(saves);
        foreach (var dir in new[] { sandbox, sandbox + "_b", sandbox + "_c" }) try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
        foreach (var f in new[] { sandbox + "_legacy.json", sandbox + "_auto_same.json", sandbox + "_auto_diff.json" }) try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { }
        if (real != null)
        {
            try { RestoreFolder(real, RealRoot); } catch (Exception e) { Info(sb, "folder restore: " + e.Message); }
            if (prefHad) PlayerPrefs.SetString(SongLibrary.PrefCurrent, prefWas); else PlayerPrefs.DeleteKey(SongLibrary.PrefCurrent);
            PlayerPrefs.DeleteKey(SongLibrary.PrefCurrent + ".sandbox");
            PlayerPrefs.Save();
            SongLibrary.Reload();
            string after = FolderDigest(RealRoot);
            bool prefOk = PlayerPrefs.HasKey(SongLibrary.PrefCurrent) == prefHad && PlayerPrefs.GetString(SongLibrary.PrefCurrent, "") == prefWas;
            Line(sb, after == realDigest && prefOk && Md5File(SongIO.Path) == mainMd5, "the player's library is exactly as it was (every file, its bytes and time), the current-song pref and the old main save too",
                 "folder " + realDigest + " → " + after + ", pref " + (prefOk ? "same" : "CHANGED") + ", main save " + mainMd5 + " → " + Md5File(SongIO.Path));
        }
        Application.logMessageReceived -= OnLog;
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
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        CubeInspector.CloseImmediate();
        if (PathManager.I != null) PathManager.I.PutDown();
    }

    /// <summary>The menu's canvases take no pointer while the test drives them (a real cursor resting on the Game view would focus whatever card
    /// appears under it and steal the keys' focus); the test clicks through the controls' own handlers.</summary>
    static void MenuPointer(bool on)
    {
        if (MainMenu.I == null) return;
        foreach (var gr in MainMenu.I.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true)) gr.enabled = on;
    }

    static void RestoreWorld()
    {
        KeyShim.Clear();
        MenuPointer(true);
        SongIO.QuitAutosave = false;
        if (InterfaceController.I != null && InterfaceController.I.Visible) InterfaceController.I.Close(false);
        if (UIManager.I != null && UIManager.I.Strip != null) UIManager.I.Strip.CloseNow();
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); WorldInput.Unlock(HudMenuStrip.LockOwner);
        SongLibrary.EndSandbox();
        PathManager.SimOnly = false;
        IslandHeader.Hide(); IslandTray.Close(); GlobalClock.Stop();
        if (UIManager.I != null) { UIManager.I.ClearSelection(); UIManager.I.SetHudVisible(true); }
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
    }

    static KeyBlock ChordAt(int col) => SM.Islands.FirstOrDefault(kb => kb != null && kb.kind == 0 && kb.column == col);
    static int Idx(KeyBlock kb) => SM.Islands.IndexOf(kb);

    static CubeState Cube(KeyBlock kb, int instrument, int[] xs, int[] zs, int[] durs)
    {
        for (int i = 0; i < xs.Length; i++) { xs[i] = Mathf.Clamp(xs[i], 0, kb.cols - 1); zs[i] = Mathf.Clamp(zs[i], 0, kb.rows - 1); }
        return new CubeState { instrument = instrument, measure = Idx(kb), xs = xs, zs = zs, rests = new bool[xs.Length], durs = durs, step = 1, gate = 1, mode = 0, volume = 1f };
    }

    /// <summary>V7ChecksB's test song with a keys path and a bass line on its first grid (a song with the player's own work), History reset.</summary>
    static void LoadWork(int variant)
    {
        V7ChecksB.LoadTest();
        var g0 = ChordAt(0);
        PathManager.I.RestoreCube(Cube(g0, 0, new[] { 0, 1, 2, 3 }, new[] { 0, variant % 3, 2, 1 }, new[] { 24, 24, 24, 24 }));
        PathManager.I.RestoreCube(Cube(g0, Clipboard.BassGroup, new[] { 0, 0 }, new[] { 0, 2 }, new[] { 48, 48 }));
        SequenceMaster.RecalculateTimeline();
        History.Reset(); History.Push();
    }

    /// <summary>One more cube on grid <paramref name="col"/> (an edit), pushed to History.</summary>
    static void AddCube(int col, int z)
    {
        var g = ChordAt(col) ?? ChordAt(0);
        PathManager.I.RestoreCube(Cube(g, 0, new[] { 0, 1, 2 }, new[] { z, z, z }, new[] { 24, 24, 24 }));
        SequenceMaster.RecalculateTimeline();
        History.Push();
    }

    static string Names() => string.Join(", ", SongLibrary.Entries.Select(e => e.name).ToArray());

    /// <summary>A song's shape: grid and cube counts, tempo, key and every cube's grid + path (order-free).</summary>
    static string Shape(SongState s)
    {
        if (s == null || s.measures == null) return "null";
        var cubes = s.cubes ?? new CubeState[0];
        return s.measures.Length + " grids " + cubes.Length + " cubes " + Mathf.RoundToInt(s.bpm) + " bpm key " + s.keyTonic + " | "
               + string.Join(";", cubes.Select(c => c.instrument + ":" + c.measure + ":" + string.Join(",", (c.xs ?? new int[0]).Select(v => v.ToString()).ToArray()) + "/" + string.Join(",", (c.zs ?? new int[0]).Select(v => v.ToString()).ToArray())).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The world's song (<paramref name="live"/>) is the file's (<paramref name="file"/>): the same JSON, else the same shape.</summary>
    static bool SameSong(string live, string file, out string how)
    {
        if (live == file) { how = "identical JSON"; return true; }
        string a = Shape(SongState.FromJson(live)), b = Shape(SongState.FromJson(file));
        bool same = a == b;
        how = same ? "same grids, cubes and paths (the JSON differs in detail)" : "DIFFERENT: " + a + " vs " + b;
        return same;
    }
    static int CubeCount() => SequenceMaster.Cubes.Count(c => c != null && c.isFinalized && c.nodes.Count > 0);
    static string Capture() => SongState.Capture().ToJson();
    static Rect ScreenRectOf(RectTransform rt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, c[0]), b = RectTransformUtility.WorldToScreenPoint(null, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    /// <summary>The page check (the user: "the song gallery should fill up the whole page — DEACUBE should disappear"): the title is down in the
    /// wall (no letter pin lit, none standing higher than the plain wall around it), every card on screen, none overlapping, the top row above
    /// where the title's bottom edge stood.</summary>
    static bool PageOk(IList<Rect> cards, out string detail)
    {
        var st = MainMenu.Stage;
        Rect title = st.TitleRestRect();
        bool inside = true, apart = true; float top = float.MinValue;
        for (int i = 0; i < cards.Count; i++)
        {
            var r = cards[i];
            if (r.xMin < -1f || r.yMin < -1f || r.xMax > Screen.width + 1f || r.yMax > Screen.height + 1f) inside = false;
            top = Mathf.Max(top, r.yMax);
            for (int j = 0; j < i; j++) if (cards[j].Overlaps(r)) apart = false;
        }
        bool sunk = st.TitleDown && st.LetterPinsLit == 0 && st.MaxLetterLift <= st.MaxWallLiftInWord + 0.2f;
        detail = "title down " + st.TitleDown + " (letters lit " + st.LetterPinsLit + ", lift " + st.MaxLetterLift.ToString("F2") + " ≤ wall " + st.MaxWallLiftInWord.ToString("F2")
                 + "), " + cards.Count + " cards on screen " + inside + ", apart " + apart + ", top row top y " + top.ToString("F0") + " > title bottom y " + title.yMin.ToString("F0");
        return sunk && inside && apart && top > title.yMin;
    }

    /// <summary>The title stands again (every letter pin lit as a letter, standing well above the wall).</summary>
    static bool TitleUp(out string detail)
    {
        var st = MainMenu.Stage;
        detail = "title down " + st.TitleDown + ", letters lit " + st.LetterPinsLit + "/" + MenuStage.LetterPinCount + ", lift " + st.MaxLetterLift.ToString("F2");
        return !st.TitleDown && st.LetterPinsLit == MenuStage.LetterPinCount && st.MaxLetterLift > 0.8f;
    }

    static IEnumerator ShowMenu()
    {
        MainMenu.Show();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 6f);
        yield return Wait(0.3f);
    }

    static IEnumerator Routine(StringBuilder sb, bool captures, string sandbox)
    {
        Prepare();
        yield return Frames(2);

        // ---- 1. the guard on the player's own library: SongIO.QuitAutosave off (as every test harness sets it) = the library is neither written nor
        // used — creating, saving, autosaving, renaming, duplicating, deleting, importing all refuse; the menu shows no "my songs"
        LoadWork(0); yield return Frames(3);
        string d0 = FolderDigest(RealRoot);
        int refused0 = SongLibrary.Refused;
        string anyId = SongLibrary.Entries.Count > 0 ? SongLibrary.Entries[0].id : "none";
        bool refusedAll = !SongLibrary.Enabled && SongLibrary.Create("test") == null && !SongLibrary.SaveCurrent() && !SongLibrary.AutoSave()
                          && !SongLibrary.Rename(anyId, "x") && SongLibrary.Duplicate(anyId) == null && !SongLibrary.Delete(anyId) && !SongLibrary.EnsureImported() && !SongLibrary.HasSongs;
        yield return ShowMenu();
        bool wordHidden = MainMenu.SongsButton != null && !MainMenu.SongsButton.gameObject.activeSelf;
        MainMenu.Hide(); yield return Frames(2);
        string d1 = FolderDigest(RealRoot);
        Line(sb, refusedAll && wordHidden && d1 == d0 && SongLibrary.Refused - refused0 >= 6,
             "QuitAutosave off (test harnesses): the player's library is off — create / save / autosave / rename / duplicate / delete / import refuse, the menu shows no \"my songs\", the menu's own autosave leaves the library alone",
             "refused +" + (SongLibrary.Refused - refused0) + ", word hidden " + wordHidden + ", folder " + d0 + " → " + d1);

        // ---- 2. the sandbox: an empty library and an old single save waiting to be imported
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox));
        string legacy = sandbox + "_legacy.json";
        File.Copy(V2Checks.FixturePath, legacy, true);
        var legacyTime = new DateTime(2026, 9, 20, 18, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(legacy, legacyTime);
        string legacyMd5 = Md5File(legacy);

        // ---- 2a. the import also looks at the old autosave (what Continue opened before the library): the same music → one "my song" copied from
        // the newer file; a different, newer song with work → "my song" + "last session", the newer one current
        var autoTime = legacyTime.AddDays(1);
        SongState.Apply(SongState.FromJson(File.ReadAllText(legacy))); History.Reset(); History.Push(); yield return Frames(3);
        string autoSame = sandbox + "_auto_same.json", autoDiff = sandbox + "_auto_diff.json";
        File.WriteAllText(autoSame, Capture()); File.SetLastWriteTimeUtc(autoSame, autoTime);   // the fixture re-saved: the same music, a newer schema
        LoadWork(6); yield return Frames(2);
        File.WriteAllText(autoDiff, Capture()); File.SetLastWriteTimeUtc(autoDiff, autoTime);   // another song with cubes
        SongLibrary.UseSandbox(sandbox + "_c", legacy, autoSame);
        bool impC = SongLibrary.EnsureImported();
        var ec = SongLibrary.Entries.ToList();
        bool sameOk = impC && ec.Count == 1 && ec[0].name == "my song" && Md5File(SongLibrary.PathOf(ec[0].id)) == Md5File(autoSame) && Math.Abs((ec[0].UpdatedUtc - autoTime).TotalSeconds) < 1.0;
        SongLibrary.UseSandbox(sandbox + "_b", legacy, autoDiff);
        bool impB = SongLibrary.EnsureImported();
        var eb = SongLibrary.Entries.ToList();
        bool diffOk = impB && eb.Count == 2 && eb[0].name == "last session" && eb[1].name == "my song" && SongLibrary.CurrentId == eb[0].id
                      && Md5File(SongLibrary.PathOf(eb[0].id)) == Md5File(autoDiff) && Md5File(SongLibrary.PathOf(eb[1].id)) == legacyMd5;
        SongLibrary.EndSandbox();
        Line(sb, sameOk && diffOk && File.Exists(autoSame) && File.Exists(autoDiff) && Md5File(legacy) == legacyMd5,
             "the import and the old autosave: holding the same music it is copied once, from the newer file; a different newer song comes in too as \"last session\" (current); the old files stay",
             "same: " + string.Join(", ", ec.Select(e => e.name).ToArray()) + "; different: " + string.Join(", ", eb.Select(e => e.name).ToArray()));
        foreach (var dir in new[] { sandbox + "_b", sandbox + "_c" }) try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
        foreach (var f in new[] { autoSame, autoDiff }) try { if (File.Exists(f)) File.Delete(f); } catch (Exception) { }
        SongLibrary.UseSandbox(sandbox, legacy);
        Line(sb, SongLibrary.Enabled && SongLibrary.InSandbox && SongLibrary.Count == 0 && SongLibrary.ImportPending && SongLibrary.HasSongs && SongLibrary.CurrentId == "",
             "a sandbox library: on (whatever QuitAutosave says), empty, the old save waits for its import (so \"my songs\" would show)", "root " + sandbox);

        // ---- 3. the one-time import of the old save: a copy named "my song", with the old file's time; the original stays; never twice
        bool imported = SongLibrary.EnsureImported();
        var my = SongLibrary.Entries.FirstOrDefault();
        bool copyOk = my != null && my.name == SongLibrary.ImportName && File.Exists(SongLibrary.PathOf(my.id)) && Md5File(SongLibrary.PathOf(my.id)) == legacyMd5
                      && File.Exists(legacy) && Md5File(legacy) == legacyMd5 && Math.Abs((my.UpdatedUtc - legacyTime).TotalSeconds) < 1.0 && SongLibrary.CurrentId == my.id;
        bool again = SongLibrary.EnsureImported();
        Line(sb, imported && copyOk && !again && SongLibrary.Count == 1 && SongLibrary.Imported,
             "import: the old save becomes \"my song\" — a byte-for-byte copy dated like the original (which stays where it was), the current song; a second import does nothing",
             my != null ? "\"" + my.name + "\" " + my.id + ", " + my.columns + " columns, " + my.ColorCount + " chord colours, bpm " + my.bpm + ", " + SongLibrary.Edited(my) + "; again " + again : "nothing imported");
        Line(sb, my != null && my.columns == 6 && my.ColorCount == 6 && Mathf.RoundToInt(my.bpm) == 121, "the index describes it for its card: 6 columns, 6 chord colours, 121 bpm (the fixture)",
             my != null ? my.columns + " / " + my.ColorCount + " / " + my.bpm + " [" + string.Join(" ", my.colors ?? new string[0]) + "]" : "-");

        // ---- 4. create, save, the list newest first
        LoadWork(1); yield return Frames(3);
        int creates0 = SongLibrary.Creates;
        bool saved = SongIO.Save();   // ⌘S / the strip's save: the world's song is not a library song yet → a new entry "song N"
        var s2 = SongLibrary.LiveEntry;
        bool createdOk = saved && s2 != null && s2.name == "song 2" && SongLibrary.Creates == creates0 + 1 && SongLibrary.CurrentId == s2.id && SongLibrary.Entries[0] == s2
                         && File.ReadAllText(SongLibrary.PathOf(s2.id)) == Capture();
        Line(sb, createdOk, "save (⌘S, SongIO.Save) with no library song: a new entry \"song 2\" holding the world's song exactly, now the current song, first in the list",
             s2 != null ? "\"" + s2.name + "\" " + s2.id + "; list: " + Names() : "no entry; list: " + Names());
        yield return Wait(0.05f);
        AddCube(1, 1); yield return Frames(2);
        DateTime u0 = s2 != null ? s2.UpdatedUtc : DateTime.MinValue;
        int saves0 = SongLibrary.Saves;
        bool saved2 = SongIO.Save();
        bool rewritten = s2 != null && File.ReadAllText(SongLibrary.PathOf(s2.id)) == Capture() && s2.UpdatedUtc > u0 && SongLibrary.Saves == saves0 + 1 && SongLibrary.Count == 2;
        DateTime mt = s2 != null ? File.GetLastWriteTimeUtc(SongLibrary.PathOf(s2.id)) : DateTime.MinValue;
        yield return Wait(0.05f);
        bool saved3 = SongIO.Save();   // unchanged: nothing written, the "edited" time stays true
        bool untouched = s2 != null && File.GetLastWriteTimeUtc(SongLibrary.PathOf(s2.id)) == mt && SongLibrary.Saves == saves0 + 1;
        Line(sb, saved2 && rewritten && saved3 && untouched, "an edit, then save: the same entry's file is rewritten and stamped edited now; saving again unchanged writes nothing",
             "saves +" + (SongLibrary.Saves - saves0) + ", entries " + SongLibrary.Count);
        yield return Wait(0.05f);
        LoadWork(2); yield return Frames(3);
        var rain = SongLibrary.Create("rain test");
        var list = SongLibrary.Entries;
        bool ordered = true; for (int i = 1; i < list.Count; i++) if (list[i - 1].UpdatedUtc < list[i].UpdatedUtc) ordered = false;
        Line(sb, rain != null && SongLibrary.Count == 3 && list[0] == rain && list[1] == s2 && list[2] == my && ordered && SongLibrary.LiveId == rain.id,
             "create(\"rain test\"): a third song; the list is newest first (the imported song, dated like the old save, last)", Names());

        // ---- 5. rename (one line, trimmed, single spaces; empty refused), kept on disk
        bool r1 = s2 != null && SongLibrary.Rename(s2.id, "  night \n  drive  ");
        bool r2 = s2 != null && !SongLibrary.Rename(s2.id, "   ");
        SongLibrary.Reload();
        var s2b = s2 != null ? SongLibrary.Find(s2.id) : null;
        Line(sb, r1 && r2 && s2b != null && s2b.name == "night drive" && SongLibrary.Entries[1].id == s2.id,
             "rename: \"  night \\n  drive  \" → \"night drive\" (read back from library.json); an empty name is refused; the order stays (a rename is not an edit)",
             s2b != null ? "\"" + s2b.name + "\"; " + Names() : "-");

        // ---- 6. duplicate: a copy of the file, "… copy", "… copy 2", shown first
        var c1 = s2b != null ? SongLibrary.Duplicate(s2b.id) : null;
        var c2 = s2b != null ? SongLibrary.Duplicate(s2b.id) : null;
        bool dupOk = c1 != null && c2 != null && c1.name == "night drive copy" && c2.name == "night drive copy 2" && SongLibrary.Count == 5 && SongLibrary.Entries[0] == c2
                     && File.ReadAllText(SongLibrary.PathOf(c1.id)) == File.ReadAllText(SongLibrary.PathOf(s2b.id)) && SongLibrary.LiveId == rain.id;
        Line(sb, dupOk, "duplicate: a byte-for-byte copy named \"night drive copy\", then \"night drive copy 2\", newest first; the world's song stays as it is", Names());

        // ---- 7. delete → the trash (undoable), restore
        string trashed = c2 != null ? c2.id : "";
        bool del = SongLibrary.Delete(trashed);
        bool inTrash = del && !File.Exists(SongLibrary.PathOf(trashed)) && File.Exists(SongLibrary.TrashPathOf(trashed)) && SongLibrary.Find(trashed) == null && SongLibrary.Trash.Any(t => t.id == trashed);
        bool back = SongLibrary.Restore(trashed);
        bool restored = back && File.Exists(SongLibrary.PathOf(trashed)) && !File.Exists(SongLibrary.TrashPathOf(trashed)) && SongLibrary.Find(trashed) != null && SongLibrary.Find(trashed).name == "night drive copy 2";
        bool del2 = SongLibrary.Delete(trashed);
        Line(sb, inTrash && restored && del2 && SongLibrary.Count == 4, "delete moves the song file to songs/trash/ (its row to the trash list); restore brings it back with its name; deleted again for the rest of the run",
             "trash " + SongLibrary.Trash.Count + ", list: " + Names());

        // ---- 8. open: the world's song is the file's song; a song with unsaved work is backed up first
        AddCube(2, 2); yield return Frames(2);   // the world's "rain test" changes (not saved)
        int backups0 = SongIO.BackupsWritten;
        bool opened = s2b != null && SongLibrary.Open(s2b.id);
        yield return Frames(3);
        string how = "";
        bool same = s2b != null && SameSong(Capture(), File.ReadAllText(SongLibrary.PathOf(s2b.id)), out how);
        Line(sb, opened && same && SongLibrary.LiveId == s2b.id && SongLibrary.CurrentId == s2b.id && SongIO.BackupsWritten == backups0 + 1,
             "open(\"night drive\"): the world's song is the file's song, it is the current song; the unsaved edit of the song it replaced went to a backup slot",
             how + "; islands " + SM.Islands.Count + ", cubes " + CubeCount() + ", backups +" + (SongIO.BackupsWritten - backups0));
        bool openedMy = my != null && SongLibrary.Open(my.id);
        yield return Frames(3);
        var fx = SongState.FromJson(File.ReadAllText(SongLibrary.PathOf(my.id)));
        Line(sb, openedMy && SM.Islands.Count == fx.measures.Length && CubeCount() == fx.cubes.Length && Mathf.RoundToInt(GlobalClock.BPM) == 121 && SongLibrary.LiveId == my.id,
             "open(\"my song\", the imported old save): its 6 grids, 13 cubes and 121 bpm are in the world", "islands " + SM.Islands.Count + ", cubes " + CubeCount() + ", bpm " + GlobalClock.BPM);

        // ---- 9. another song replacing the world's (a load, a new song, the gallery, a test) unbinds it: saving then never overwrites the library song
        string myBytes = File.ReadAllText(SongLibrary.PathOf(my.id));
        LoadWork(3); yield return Frames(3);
        bool unbound = SongLibrary.LiveId == "" && SongLibrary.CurrentId == my.id;
        bool savedNew = SongIO.Save();
        var s5 = SongLibrary.LiveEntry;
        Line(sb, unbound && savedNew && s5 != null && s5.id != my.id && File.ReadAllText(SongLibrary.PathOf(my.id)) == myBytes,
             "a History reset (here a test song) unbinds the library song: the next save makes a new entry, \"my song\" is untouched", (s5 != null ? "new \"" + s5.name + "\"" : "-") + "; " + Names());

        // ---- 10. autosave (menu / quit) only while QuitAutosave is on — and only songs worth keeping
        AddCube(1, 0); yield return Frames(2);
        string s5Bytes = s5 != null ? File.ReadAllText(SongLibrary.PathOf(s5.id)) : "";
        bool offA = !SongLibrary.AutoSave();
        int autos0 = MainMenu.Autosaves;
        yield return ShowMenu();   // entering the menu: the autosave slot is written, the library is not (QuitAutosave off)
        bool menuWrote = MainMenu.Autosaves == autos0 + 1;
        MainMenu.Hide(); yield return Frames(2);
        bool offB = s5 != null && File.ReadAllText(SongLibrary.PathOf(s5.id)) == s5Bytes;
        SongIO.QuitAutosave = true;
        bool onA;
        try { onA = SongLibrary.AutoSave(); } finally { SongIO.QuitAutosave = false; }
        bool onB = s5 != null && File.ReadAllText(SongLibrary.PathOf(s5.id)) == Capture();
        Line(sb, offA && menuWrote && offB && onA && onB, "autosave guard: with QuitAutosave off neither AutoSave nor entering the menu touches the library song; with it on, AutoSave writes it",
             "off " + offA + "/" + offB + ", menu autosave slot +" + (MainMenu.Autosaves - autos0) + ", on " + onA + "/" + onB);
        // ---- 10b. deleting the world's song: the menu's autosave does not bring it back as it was — only once it is edited again
        int countD = SongLibrary.Count;
        bool delLive = s5 != null && SongLibrary.Delete(s5.id);
        bool backAsIs, backEdited;
        SongIO.QuitAutosave = true;
        try { backAsIs = SongLibrary.AutoSave(); AddCube(0, 2); backEdited = SongLibrary.AutoSave(); }
        finally { SongIO.QuitAutosave = false; }
        Line(sb, delLive && !backAsIs && backEdited && SongLibrary.Count == countD,
             "the world's song deleted from the library: an autosave does not bring it back as it was; edited again, it is kept as a new song", "as is " + backAsIs + ", edited " + backEdited + "; " + Names());
        SM.StartFreshSong(MusicTheory.RandomSong(777, "songs fresh")); yield return Frames(3);
        int count0 = SongLibrary.Count;
        SongIO.QuitAutosave = true;
        bool freshKept, workKept;
        try
        {
            freshKept = SongLibrary.AutoSave();
            AddCube(0, 1);
            workKept = SongLibrary.AutoSave();
        }
        finally { SongIO.QuitAutosave = false; }
        Line(sb, !freshKept && workKept && SongLibrary.Count == count0 + 1 && SongLibrary.LiveEntry != null && SongLibrary.LiveEntry.name.StartsWith("song "),
             "autosave keeps only work: an untouched fresh song gets no entry; once it has a cube of the player's it gets one (\"song N\")", "fresh " + freshKept + ", with work " + workKept + "; " + Names());

        // ---- 11. a song the vibe prompt makes lands as a new entry right away (the NewSong flow)
        int landed0 = SongLibrary.Landed, count1 = SongLibrary.Count;
        var ic = InterfaceController.I;
        if (ic != null && ic.DiceButton != null)
        {
            ic.Show(); yield return Frames(3);
            ic.DiceButton.onClick?.Invoke();
            yield return WaitFor(() => SongLibrary.Landed > landed0 || !ic.Visible, 8f);
            yield return Frames(3);
        }
        var landed = SongLibrary.LiveEntry;
        Line(sb, SongLibrary.Landed == landed0 + 1 && SongLibrary.Count == count1 + 1 && landed != null && SongLibrary.Entries[0] == landed && File.Exists(SongLibrary.PathOf(landed.id)),
             "the vibe prompt's dice song lands as a new library entry, first in the list, the world's song", (landed != null ? "\"" + landed.name + "\"" : "none") + "; " + Names());
        if (ic != null && ic.Visible) ic.Close(false);
        yield return Frames(2);

        // ---- 12. the menu: "my songs" right after continue, on its own canvas (Words and the menu canvas keep their five words)
        MenuPointer(false);
        var target = SongLibrary.Find(s2b.id);
        SongLibrary.Open(target.id); yield return Frames(3);
        AddCube(2, 1); yield return Frames(2);
        SongIO.Save();   // an edit saved: "night drive" is the newest song again (first on the shelf's first page)
        yield return ShowMenu();
        var word = MainMenu.SongsButton;
        var cont = MainMenu.ContinueButton; var nw = MainMenu.NewButton;
        Canvas.ForceUpdateCanvases(); yield return Frames(2);
        bool wordShown = word != null && word.gameObject.activeInHierarchy && word.label.text == "my songs";
        float yc = cont.ScreenCenter.y, ys = word != null ? word.ScreenCenter.y : 0f, yn = nw.ScreenCenter.y;
        float xc = ScreenRectOf(cont.label.rectTransform).xMin, xs = word != null ? ScreenRectOf(word.label.rectTransform).xMin : -999f;
        int canvasWords = MainMenu.MenuCanvas.GetComponentsInChildren<MenuButton>(true).Length;
        Line(sb, wordShown && yc > ys && ys > yn && Mathf.Abs(xc - xs) < 14f && MainMenu.Words.Length == 5 && canvasWords == 5,
             "the menu shows the lowercase word \"my songs\" right after continue, left-aligned with it; Words and the menu canvas still hold the five words",
             "screen y continue " + yc.ToString("F0") + " > my songs " + ys.ToString("F0") + " > new song " + yn.ToString("F0") + ", left x " + xc.ToString("F0") + " / " + xs.ToString("F0") + ", Words " + MainMenu.Words.Length + ", canvas words " + canvasWords);
        MainMenu.MoveFocus(-9); yield return null;
        for (int i = 0; i < 6 && MainMenu.FocusedButton != cont; i++) { MainMenu.MoveFocus(1); yield return null; }
        MainMenu.MoveFocus(1); yield return null;
        bool onWord = MainMenu.FocusedButton == word;
        MainMenu.MoveFocus(1); yield return null;
        bool onNew = MainMenu.FocusedButton == nw;
        MainMenu.MoveFocus(-1); yield return Wait(0.45f);
        Line(sb, onWord && onNew && MainMenu.FocusedButton == word && word.UnderlineDrawn > 0.99f, "↓ from continue lands on \"my songs\", ↓ again on \"new song\", ↑ back: the focused word wears the wavy underline", "focus " + (MainMenu.FocusedButton != null ? MainMenu.FocusedButton.label.text : "none"));
        if (captures) yield return Shot("songs_menu_words.png");

        // ---- 13. the shelf: "+ new song" first, then a card per song (name, "edited …", the chord strip, the three controls)
        var shelf = MainMenu.SongsShelf;
        word.Invoke();
        yield return Wait(0.9f);
        var cards = shelf.Cards;
        bool firstNew = cards.Count > 0 && cards[0].IsNew && shelf.Items[0] == null;
        bool songsOk = true; string firstFew = "";
        for (int i = 1; i < cards.Count; i++)
        {
            var c = cards[i];
            if (c.Entry == null || c.TitleText != c.Entry.name || !c.EditedText.StartsWith("edited ") || c.RenameButton == null || c.DuplicateButton == null || c.DeleteButton == null || c.chords == null) songsOk = false;
            if (i <= 3) firstFew += "[" + c.TitleText + " · " + c.EditedText + "] ";
        }
        bool dealtAll = cards.All(c => c.Dealt);
        Line(sb, MainMenu.SongsOpen && shelf.Page == 0 && firstNew && songsOk && cards.Count == Mathf.Min(MenuSongs.PerPage, SongLibrary.Count + 1) && dealtAll && MainMenu.WordsAlpha < 0.01f,
             "\"my songs\" opens the shelf: the words give way; \"+ new song\" first, then a card per song with its name, \"edited …\", the chord strip and rename / duplicate / delete",
             cards.Count + " cards on page " + (shelf.Page + 1) + "/" + shelf.Pages + ": " + firstFew.Trim());
        var fc = shelf.FocusedCard;
        Line(sb, fc != null && fc.Entry != null && fc.Entry.id == target.id && fc.UnderlineDrawn > 0.99f && shelf.Tint.HasValue,
             "the world's song is focused when the shelf opens (lifted, underlined; the wall lit in its first chord colour)", fc != null ? fc.TitleText : "none");
        string pd;
        bool page1 = PageOk(cards.Select(c => c.ScreenRect).ToList(), out pd);
        bool noPager = !shelf.PrevButton.gameObject.activeInHierarchy && !shelf.NextButton.gameObject.activeInHierarchy && shelf.PageText == "";
        var bkr = ScreenRectOf(shelf.BackButton.label.rectTransform);
        Line(sb, page1 && noPager && bkr.xMin < Screen.width * 0.12f && bkr.yMax > Screen.height * 0.88f,
             "the shelf fills the page: DEACUBE sinks into the wall, the cards use the screen (2 rows), \"back\" at the top-left; one page, so no ‹ › and no page count",
             pd + "; back at " + bkr.xMin.ToString("F0") + "," + bkr.yMax.ToString("F0") + "; rows " + cards.Select(c => Mathf.RoundToInt(c.ScreenRect.center.y)).Distinct().Count());
        if (captures) yield return Shot("songs_shelf.png");
        // ↑ ↓ between rows, ← → along a row, "back" before the first card
        shelf.FocusItemAt(1); yield return Frames(2);
        int cols0 = cards.Count > 1 ? cards.Count(c => Mathf.Abs(c.ScreenRect.center.y - cards[0].ScreenRect.center.y) < 2f) : 1;
        yield return Key(KeyCode.DownArrow); int dn = shelf.FocusItem;
        yield return Key(KeyCode.UpArrow); int upAgain = shelf.FocusItem;
        yield return Key(KeyCode.RightArrow); int rt = shelf.FocusItem;
        yield return Key(KeyCode.LeftArrow); yield return Key(KeyCode.LeftArrow); int lf = shelf.FocusItem;
        yield return Key(KeyCode.LeftArrow); bool lfBack = shelf.BackFocused;
        yield return Key(KeyCode.DownArrow); int fromBack = shelf.FocusItem;
        yield return Key(KeyCode.UpArrow); bool upBack = shelf.BackFocused;
        Line(sb, dn == 1 + cols0 && upAgain == 1 && rt == 2 && lf == 0 && lfBack && fromBack == 0 && upBack,
             "keys on the page: ↓ goes to the card below (the next row), ↑ back up, → along the row, ← ← to the row's first card, ← once more to \"back\", ↓ from \"back\" to the first card, ↑ from the top row to \"back\"",
             cols0 + " columns: ↓ " + dn + ", ↑ " + upAgain + ", → " + rt + ", ← ← " + lf + ", ← back " + lfBack + ", ↓ " + fromBack + ", ↑ back " + upBack);
        shelf.FocusSong(target.id); yield return Frames(2);

        // ---- 14. rename on the card: the pencil opens the name field (all selected), Enter keeps the new name; the field lets go of the keys
        var rc = shelf.CardOf(target.id);
        rc.RenameButton.onClick(); yield return Frames(3);
        bool fieldUp = rc.FieldActive && shelf.Renaming && InputUtil.TypingInField;
        rc.SimType("lofi rain");
        yield return Wait(0.3f);
        if (captures) yield return Shot("songs_rename.png");
        rc.field.onSubmit.Invoke(rc.field.text);
        yield return Frames(4);
        var rc2 = shelf.CardOf(target.id);
        Line(sb, fieldUp && !shelf.Renaming && SongLibrary.Find(target.id).name == "lofi rain" && rc2 != null && rc2.TitleText == "lofi rain" && !InputUtil.TypingInField,
             "rename on the card: the pencil swaps the name for a text field; Enter keeps \"lofi rain\" (the library and the card), the keys are free again", "field " + fieldUp + ", now \"" + SongLibrary.Find(target.id).name + "\"");
        rc2.RenameButton.onClick(); yield return Frames(3);
        rc2.SimType("zzz");
        shelf.EndRename(rc2, false); yield return Frames(4);
        Line(sb, SongLibrary.Find(target.id).name == "lofi rain" && !shelf.Renaming && !InputUtil.TypingInField, "rename cancelled (Esc): the old name stays", SongLibrary.Find(target.id).name);

        // ---- 15. duplicate on the card: the copy is the first song, focused, popping in
        int items0 = shelf.Items.Count;
        shelf.CardOf(target.id).DuplicateButton.onClick(); yield return Frames(4);
        var dc = shelf.FocusedCard;
        Line(sb, shelf.Items.Count == items0 + 1 && dc != null && dc.Entry != null && dc.Entry.name == "lofi rain copy" && dc.Item == 1,
             "⧉ on the card: \"lofi rain copy\" appears as the first song and takes the focus", dc != null ? dc.TitleText + " (item " + dc.Item + ")" : "none");
        yield return Wait(0.4f);

        // ---- 16. delete on the card: the trash can arms on the first click ("click again", a red ring), deletes on the second; undo brings it back
        string copyId = dc.Entry.id;
        dc.DeleteButton.onClick(); yield return Wait(0.2f);
        var dc2 = shelf.CardOf(copyId);
        bool armed = shelf.ArmedId == copyId && dc2 != null && dc2.Armed && dc2.deleteLabel.text == "delete?" && dc2.EditedText == "click again to delete" && SongLibrary.Find(copyId) != null;
        if (captures) yield return Shot("songs_delete_armed.png");
        dc2.DeleteButton.onClick(); yield return Frames(4);
        bool gone = SongLibrary.Find(copyId) == null && File.Exists(SongLibrary.TrashPathOf(copyId)) && shelf.CardOf(copyId) == null && shelf.UndoShown && shelf.UndoText.Contains("lofi rain copy");
        Line(sb, armed && gone, "the trash can: a first click arms it (\"click again to delete\", \"delete?\", ringed red, nothing deleted), the second deletes — the card goes, \"undo\" shows at the top-right",
             "armed " + armed + ", undo \"" + shelf.UndoText + "\"");
        yield return Wait(0.3f);
        if (captures) yield return Shot("songs_undo.png");
        shelf.UndoButton.Invoke(); yield return Frames(4);
        var uc = shelf.FocusedCard;
        Line(sb, SongLibrary.Find(copyId) != null && !shelf.UndoShown && uc != null && uc.Entry != null && uc.Entry.id == copyId,
             "undo: the deleted song comes back from the trash, focused", uc != null ? uc.TitleText : "none");
        shelf.CardOf(copyId).DeleteButton.onHold(); yield return Frames(4);
        Line(sb, SongLibrary.Find(copyId) == null && shelf.UndoShown, "holding the trash can deletes at once (the other way to confirm)", Names());
        var other = shelf.Cards.FirstOrDefault(c => c.Entry != null && c.Entry.id != target.id);
        if (other != null) { other.DeleteButton.onClick(); yield return Frames(2); shelf.FocusItemAt(0); yield return Frames(2); }
        Line(sb, other != null && shelf.ArmedId == null && SongLibrary.Find(other.Entry.id) != null, "moving the focus away disarms an armed trash can (nothing deleted)", other != null ? other.TitleText : "-");

        // ---- 17. pages: ten cards a page, ‹ › and the keys page through
        while (SongLibrary.Count < MenuSongs.PerPage + 1) { SongLibrary.Duplicate(target.id); yield return Frames(1); }
        shelf.FocusItemAt(0); yield return Frames(2);
        yield return Frames(3);
        int pages = shelf.Pages;
        bool nextShown = shelf.NextButton != null && shelf.NextButton.gameObject.activeInHierarchy;
        shelf.NextButton.onClick(); yield return Wait(0.5f);
        bool onPage2 = shelf.Page == 1 && shelf.Cards.Count > 0 && shelf.Cards[0].Item == MenuSongs.PerPage && shelf.PageText == "2 / " + pages;
        if (captures) yield return Shot("songs_page2.png");
        string pd2;
        bool page2ok = PageOk(shelf.Cards.Select(c => c.ScreenRect).ToList(), out pd2);
        bool pagerOn = shelf.PrevButton.gameObject.activeInHierarchy && !shelf.NextButton.gameObject.activeInHierarchy && shelf.PageText == "2 / " + pages;
        shelf.FocusItemAt(MenuSongs.PerPage); yield return Frames(2);
        yield return Key(KeyCode.LeftArrow);
        int backItem = shelf.FocusItem, backPage = shelf.Page;
        bool keysBack = shelf.Page == 0 && shelf.FocusItem == ShelfGrid.Cols - 1;
        yield return Key(KeyCode.DownArrow); yield return Key(KeyCode.DownArrow);
        int nextItem = shelf.FocusItem, nextPage = shelf.Page;
        bool keysNext = shelf.Page == 1 && shelf.FocusItem >= MenuSongs.PerPage;
        Line(sb, pages >= 2 && nextShown && onPage2 && pagerOn && page2ok && keysBack && keysNext,
             "more than ten: ‹ › and \"2 / " + pages + "\" page through (only now); ← from page 2's first card goes to page 1's row end, ↓ from the bottom row to the next page",
             "pages " + pages + ", items " + shelf.Items.Count + "; › shown " + nextShown + ", page 2 " + onPage2 + ", pager " + pagerOn + ", ← to item " + backItem + " page " + backPage + ", ↓↓ to item " + nextItem + " page " + nextPage + "; " + pd2);

        // ---- 18. Esc returns to the words with "my songs" focused
        yield return Key(KeyCode.Escape);
        yield return Wait(0.4f);
        Line(sb, !MainMenu.SongsOpen && MainMenu.WordsAlpha > 0.99f && MainMenu.FocusedButton == word, "Esc: back to the words, \"my songs\" focused", "words alpha " + MainMenu.WordsAlpha.ToString("F2"));
        yield return Wait(0.6f);
        string tu;
        bool titleBack = TitleUp(out tu);
        if (captures) yield return Shot("songs_title_back.png");
        Line(sb, titleBack, "after \"back\" DEACUBE rises out of the wall again (column by column, no pops)", tu);

        // ---- 18b. the gallery shelf behaves alike: the title sinks, its cards fill the page in balanced rows, ↑ ↓ between rows, back → the title rises
        var gw = MainMenu.GalleryButton;
        var gs = MainMenu.Shelf;
        if (gw != null && gw.gameObject.activeSelf && gs != null)
        {
            for (int i = 0; i < 7 && MainMenu.FocusedButton != gw; i++) { MainMenu.MoveFocus(1); yield return null; }
            gw.Invoke();
            yield return WaitFor(() => gs.Shown >= 1f, 2f);
            yield return Wait(0.25f + 0.09f * gs.Cards.Count);
            string gpd;
            bool gPage = PageOk(gs.Cards.Select(c => c.ScreenRect).ToList(), out gpd);
            var gbk = ScreenRectOf(gs.BackButton.label.rectTransform);
            int gRows = gs.Cards.Select(c => Mathf.RoundToInt(c.ScreenRect.center.y)).Distinct().Count();
            Line(sb, gPage && gbk.xMin < Screen.width * 0.12f && gbk.yMax > Screen.height * 0.88f && (gs.Cards.Count <= ShelfGrid.Cols || gRows >= 2),
                 "the gallery shelf fills the page too: DEACUBE sinks, its " + gs.Cards.Count + " cards in balanced rows (" + gRows + "), \"back\" at the top-left", gpd);
            if (captures) yield return Shot("songs_gallery_page.png");
            gs.FocusCard(0); yield return Frames(2);
            int gcols = gs.Cards.Count(c => Mathf.Abs(c.ScreenRect.center.y - gs.Cards[0].ScreenRect.center.y) < 2f);
            yield return Key(KeyCode.DownArrow); int gdn = gs.FocusIndex;
            yield return Key(KeyCode.UpArrow); int gup = gs.FocusIndex;
            yield return Key(KeyCode.UpArrow); bool gback = gs.BackFocused;
            yield return Key(KeyCode.RightArrow); int gfirst = gs.FocusIndex;
            bool twoRows = gs.Cards.Count > gcols;
            Line(sb, (twoRows ? gdn == Mathf.Min(gcols, gs.Cards.Count - 1) : gdn == 0) && gup == 0 && gback && gfirst == 0,
                 "gallery keys: ↓ to the card below, ↑ back up, ↑ from the top row to \"back\", → from \"back\" to the first card", gcols + " columns: ↓ " + gdn + ", ↑ " + gup + ", back " + gback + ", → " + gfirst);
            yield return Key(KeyCode.Escape);
            yield return Wait(1.0f);
            string gtu;
            bool gTitle = TitleUp(out gtu);
            Line(sb, !MainMenu.ShelfOpen && gTitle, "gallery \"back\": the words return and DEACUBE rises again", gtu);
        }
        else Line(sb, false, "the gallery shelf", "no gallery word / shelf (gallery songs " + Gallery.Entries.Count + ")");

        // ---- 19. "+ new song" opens the vibe prompt over the menu (✕ comes back to the shelf)
        word.Invoke(); yield return Wait(0.6f);
        shelf.FocusItemAt(0); yield return Frames(2);
        shelf.Cards[0].OnPointerClick(null); yield return Frames(3);
        bool prompt = InterfaceController.I != null && InterfaceController.I.Visible && InterfaceController.I.OpenedFromMenu;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        yield return Frames(3);
        Line(sb, prompt && MainMenu.SongsOpen && MainMenu.IsShown, "\"+ new song\" opens the vibe prompt over the menu; closing it leaves the shelf", "prompt " + prompt);

        // ---- 20. clicking a song card opens it into the world through the comic leave
        LoadWork(9); yield return Frames(2);   // something else in the world, so the card has a song to load
        yield return Frames(2);
        var oc = shelf.CardOf(target.id);
        if (oc == null) { shelf.FocusSong(target.id); yield return Frames(2); oc = shelf.CardOf(target.id); }
        int opens0 = MainMenu.SongOpens;
        if (oc != null) oc.OnPointerClick(null);
        yield return Frames(2);
        bool leaving = MainMenu.State == MainMenu.Phase.Leaving && oc != null && oc.Squashed;
        yield return WaitFor(() => !MainMenu.IsShown, 5f);
        yield return Frames(3);
        string how2 = "";
        bool same2 = SameSong(Capture(), File.ReadAllText(SongLibrary.PathOf(target.id)), out how2);
        Line(sb, leaving && !MainMenu.IsShown && MainMenu.SongOpens == opens0 + 1 && SongLibrary.LiveId == target.id && same2,
             "a click on \"lofi rain\": the comic leave (the card squashed in the held frame), then the world plays that song", how2 + "; leaving " + leaving + ", live " + (SongLibrary.LiveEntry != null ? SongLibrary.LiveEntry.name : "none"));
        if (captures) { yield return Wait(0.3f); yield return Shot("songs_opened_world.png"); }

        // ---- 21. Continue from the world resumes the library song as it was left
        AddCube(1, 2); yield return Frames(2);
        string live = Capture();
        yield return ShowMenu();
        MainMenu.ContinueButton.Invoke();
        yield return WaitFor(() => !MainMenu.IsShown, 5f);
        yield return Frames(3);
        Line(sb, Capture() == live && SongLibrary.LiveId == target.id, "Continue (from the world) resumes the library song as it was left (no reload: the unsaved edit is still there)", "live " + (SongLibrary.LiveEntry != null ? SongLibrary.LiveEntry.name : "none"));

        // ---- 22. the HUD strip: the song's name under the title; "my songs" opens the menu's shelf; ⌘O too
        var strip = UIManager.I != null ? UIManager.I.Strip : null;
        if (strip != null)
        {
            strip.Open(); yield return Wait(0.5f);
            string shownName = strip.SongNameShown;
            HudButton row = null; foreach (var b in strip.Items) if (b != null && b.name == "Load") row = b;
            var rowWords = row != null ? row.GetComponentInChildren<TMPro.TextMeshProUGUI>() : null;
            if (captures) yield return Shot("songs_strip.png");
            row?.onClick?.Invoke();
            yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 6f);
            yield return Wait(0.5f);
            Line(sb, shownName == "lofi rain" && rowWords != null && rowWords.text == "my songs" && MainMenu.SongsOpen && !strip.IsOpen,
                 "the strip shows the song's name under the title; its \"my songs\" row (the old \"load\") opens the menu with the shelf up", "name \"" + shownName + "\", row \"" + (rowWords != null ? rowWords.text : "?") + "\"");
            if (captures) yield return Shot("songs_from_strip.png");
            MainMenu.Hide(); yield return Frames(3);
        }
        else Line(sb, false, "the HUD strip", "no strip");
        KeyShim.Sim(KeyCode.LeftCommand, false, true, false); KeyShim.Sim(KeyCode.O, true, true, false);
        UIManager.I.RunHotkeysForTest();
        KeyShim.Clear();
        yield return WaitFor(() => MainMenu.State == MainMenu.Phase.Shown, 6f);
        yield return Frames(2);
        Line(sb, MainMenu.IsShown && MainMenu.SongsOpen, "⌘O opens the menu on \"my songs\"", "menu " + MainMenu.State + ", shelf " + MainMenu.SongsOpen);
        MainMenu.Hide(); yield return Frames(3);

        // ---- 23. an emptied library never re-imports; the word goes
        foreach (var e in SongLibrary.Entries.ToArray()) SongLibrary.Delete(e.id);
        bool reimport = SongLibrary.EnsureImported();
        yield return ShowMenu();
        Line(sb, SongLibrary.Count == 0 && !reimport && !SongLibrary.HasSongs && !MainMenu.SongsButton.gameObject.activeSelf && SongLibrary.CurrentId == "",
             "every song deleted: no re-import of the old save, no \"my songs\" word, no current song", "trash " + SongLibrary.Trash.Count + ", word " + MainMenu.SongsButton.gameObject.activeSelf);
        MainMenu.Hide(); yield return Frames(2);
        Info(sb, "sandbox files at the end: " + FolderDigest(sandbox));
    }
}
