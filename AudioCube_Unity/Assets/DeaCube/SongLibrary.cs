using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>One song of the player's library (a row of songs/library.json; its song is songs/&lt;id&gt;.json, a SongState).</summary>
[Serializable]
public class SongEntry
{
    public string id;        // the file's name without ".json"
    public string name;      // the player's name for it ("my song", "song 2", or whatever they renamed it to)
    public string created;   // UTC, ISO 8601 (round-trip "o")
    public string updated;   // UTC: the last time its song changed (a rename keeps it)
    public int columns;      // its length in columns (the grids that play together)
    public float bpm;
    public string[] colors;  // up to 8 chord colours (hex RRGGBB) of its first columns, in song order: the card's strip
    public string deleted;   // UTC when it went to the trash ("" while it is in the library)

    public DateTime CreatedUtc => SongLibrary.ParseTime(created);
    public DateTime UpdatedUtc => SongLibrary.ParseTime(updated);
    public int ColorCount => colors != null ? colors.Length : 0;
    public Color ColorAt(int i)
    {
        Color c;
        if (colors != null && i >= 0 && i < colors.Length && ColorUtility.TryParseHtmlString("#" + colors[i], out c)) return c;
        return new Color(0.74f, 0.64f, 1f);
    }
}

/// <summary>
/// "My songs": the player's songs, kept as files on this machine (no server, no account). A folder <see cref="Root"/>
/// (persistentDataPath/songs/) holds one &lt;id&gt;.json per song — the same SongState JSON the old single save used — and a small index,
/// library.json (<see cref="SongEntry"/>: name, created / updated UTC, column count, tempo, up to 8 chord colours for the card's strip), its
/// songs newest first. A deleted song moves to songs/trash/ and its row to the index's trash list, so <see cref="Restore"/> can bring it back.
/// The index heals itself: a song file it does not know is added, a row whose file is gone is dropped.
///
/// The current song (<see cref="CurrentId"/>, kept in PlayerPrefs) is the one the player works on; <see cref="LiveId"/> is set while the world
/// plays it (opened or saved this session, and not replaced since — a new song, a load, the gallery or a test resets History, and that unbinds
/// it). <see cref="SaveCurrent"/> (⌘S, the strip's "save", SongIO.Save) writes the world's song into its file, or, when the world's song is
/// not a library song yet, into a new entry "song N". A song the vibe prompt makes lands as a new entry right away (the NewSong flow: the
/// shelf's "+ new song" card and the "new song" word open the prompt). <see cref="AutoSave"/> (leaving the world for the menu, quitting) does
/// the same only while SongIO.QuitAutosave is on, and only for a library song or a song with the player's own work in it.
///
/// Test harnesses switch SongIO.QuitAutosave off: the player's library is then neither written nor used (<see cref="Enabled"/> is false: no
/// "my songs" word, Continue and SongIO.Save / Load behave as before the library). Tests of the library itself work in a sandbox folder
/// (<see cref="UseSandbox"/>), where everything but <see cref="AutoSave"/> works whatever the flag says.
/// The first time the library is used, an empty library imports the old single save (SongIO.Path) as "my song" — a copy; the old file stays.
/// </summary>
public static class SongLibrary
{
    public const string FolderName = "songs", IndexName = "library.json", TrashName = "trash";
    public const string PrefCurrent = "deacube.songs.current";
    public const string ImportName = "my song", AutosaveImportName = "last session";
    public const int MaxColors = 8, MaxName = 40, MaxTrash = 100;

    [Serializable]
    class LibraryIndex
    {
        public int version = 1;
        public bool imported;   // the one-time import of the old single save was done (never again, even when the library is emptied)
        public List<SongEntry> songs = new List<SongEntry>();
        public List<SongEntry> trash = new List<SongEntry>();
    }

    static LibraryIndex index;
    static string sandboxRoot, sandboxLegacy, sandboxAuto;
    static string current;   // null = not read from PlayerPrefs yet
    static string bound;     // the library song the world plays now (null: the world's song is not a library song)
    static bool opening, landing;
    static string discarded;   // the world's song as it was when the player deleted it from the library (an autosave never brings it back as is)

    /// <summary>Raised after the list, a name, the current song or the world's song changed (the menu's word and shelf follow).</summary>
    public static event Action OnChanged;

    // counters (tests)
    public static int Creates { get; private set; }
    public static int Saves { get; private set; }
    public static int Opens { get; private set; }
    public static int Renames { get; private set; }
    public static int Duplicates { get; private set; }
    public static int Deletes { get; private set; }
    public static int Restores { get; private set; }
    public static int Imports { get; private set; }
    /// <summary>Songs the vibe prompt made that landed as new entries.</summary>
    public static int Landed { get; private set; }
    /// <summary>Writes refused because the library was off (SongIO.QuitAutosave off, no sandbox).</summary>
    public static int Refused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        index = null; sandboxRoot = sandboxLegacy = sandboxAuto = null; current = null; bound = null; discarded = null; opening = landing = false; OnChanged = null;
        Creates = Saves = Opens = Renames = Duplicates = Deletes = Restores = Imports = Landed = Refused = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook() { History.OnChanged -= OnHistory; History.OnChanged += OnHistory; }

    // ------------------------------------------------------------------ where
    public static string Root => sandboxRoot ?? System.IO.Path.Combine(Application.persistentDataPath, FolderName);
    public static string IndexPath => System.IO.Path.Combine(Root, IndexName);
    public static string TrashRoot => System.IO.Path.Combine(Root, TrashName);
    public static string PathOf(string id) => System.IO.Path.Combine(Root, id + ".json");
    public static string TrashPathOf(string id) => System.IO.Path.Combine(TrashRoot, id + ".json");
    /// <summary>The old single save the one-time import copies (SongIO.Path; a test file in a sandbox).</summary>
    public static string LegacyPath => sandboxRoot != null ? sandboxLegacy : SongIO.Path;
    /// <summary>The old autosave slot the one-time import also looks at (SongIO.AutosavePath; a test file, or none, in a sandbox).</summary>
    public static string LegacyAutosavePath => sandboxRoot != null ? sandboxAuto : SongIO.AutosavePath;
    static string PrefKey => sandboxRoot != null ? PrefCurrent + ".sandbox" : PrefCurrent;

    /// <summary>The library is in use: always in a test sandbox; the player's own library only while SongIO.QuitAutosave is on (test harnesses
    /// switch it off, and then the library is neither written nor shown).</summary>
    public static bool Enabled => sandboxRoot != null || SongIO.QuitAutosave;
    public static bool InSandbox => sandboxRoot != null;

    // ------------------------------------------------------------------ the list
    /// <summary>The songs, newest first (by the time their song last changed).</summary>
    public static IReadOnlyList<SongEntry> Entries { get { Load(); return index.songs; } }
    /// <summary>Deleted songs (newest deletion first) whose files wait in songs/trash/.</summary>
    public static IReadOnlyList<SongEntry> Trash { get { Load(); return index.trash; } }
    public static int Count => Entries.Count;
    /// <summary>The old single save waits for its one-time import (an empty library that never imported, the old file present).</summary>
    public static bool ImportPending
    {
        get
        {
            Load();
            if (index.imported || index.songs.Count > 0) return false;
            string a = LegacyPath, b = LegacyAutosavePath;
            return (!string.IsNullOrEmpty(a) && File.Exists(a)) || (!string.IsNullOrEmpty(b) && File.Exists(b) && HasWork(ReadState(b)));
        }
    }
    public static bool Imported { get { Load(); return index.imported; } }
    /// <summary>There are songs to show (the menu's "my songs" word): the library is on and has songs, or the old save waits for its import.</summary>
    public static bool HasSongs => Enabled && (Count > 0 || ImportPending);

    public static SongEntry Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Load();
        foreach (var e in index.songs) if (e.id == id) return e;
        return null;
    }
    public static int IndexOf(string id) { Load(); for (int i = 0; i < index.songs.Count; i++) if (index.songs[i].id == id) return i; return -1; }

    // ------------------------------------------------------------------ the current song
    /// <summary>The song the player works on ("" none), kept in PlayerPrefs (<see cref="PrefCurrent"/>); Continue opens it.</summary>
    public static string CurrentId
    {
        get
        {
            if (current == null) current = PlayerPrefs.GetString(PrefKey, "");
            if (current != "" && Find(current) == null) current = "";   // a deleted or missing song is not current
            return current;
        }
    }
    public static SongEntry Current => Find(CurrentId);
    public static bool CurrentExists { get { string c = CurrentId; return c != "" && File.Exists(PathOf(c)); } }
    /// <summary>The library song the world plays now ("" when the world's song is not one: a new song, a gallery demo, a test song).</summary>
    public static string LiveId => bound != null && SongManager.I != null && SongManager.I.HasSong && Find(bound) != null ? bound : "";
    public static SongEntry LiveEntry => Find(LiveId);

    static void SetCurrent(string id)
    {
        current = id ?? "";
        if (!Enabled) return;
        PlayerPrefs.SetString(PrefKey, current);
        PlayerPrefs.Save();
    }
    static void Bind(string id) { bound = id; landing = false; discarded = null; SetCurrent(id); }

    /// <summary>The world's song is exactly <paramref name="id"/>'s file (Continue resumed it): it saves into that file from now on.</summary>
    public static void Adopt(string id) { if (Find(id) != null && bound != id) { Bind(id); Raise(); } }

    /// <summary>A History reset means another song replaced the world's (a new song, a load, the gallery, a test): it is no longer the library
    /// song. A song the vibe prompt made (the prompt busy while it lands) becomes a new entry on its first snapshot.</summary>
    static void OnHistory()
    {
        if (History.UndoCount == 0)
        {
            if (opening) return;
            bool was = bound != null;
            bound = null; discarded = null;
            var ic = InterfaceController.I;
            landing = ic != null && ic.Visible && ic.Busy;
            if (was) Raise();
            return;
        }
        if (!landing) return;
        landing = false;
        if (!Enabled || SongManager.I == null || !SongManager.I.HasSong) return;
        if (Create(NextName()) != null) Landed++;
    }

    // ------------------------------------------------------------------ operations
    /// <summary>The NewSong flow (the shelf's "+ new song", the "new song" word, the HUD): the vibe prompt opens (over the menu when it is up, else
    /// over the world); the song it makes lands as a new library entry "song N" and becomes the current song (<see cref="OnHistory"/>). Cancelling
    /// the prompt changes nothing. False when there is no prompt in the scene.</summary>
    public static bool NewSong()
    {
        var ic = InterfaceController.I;
        if (ic == null) return false;
        ic.Show();
        return true;
    }

    /// <summary>A new entry holding the world's song, named <paramref name="name"/> ("song N" when empty); it becomes the current song.</summary>
    public static SongEntry Create(string name)
    {
        if (!Enabled) { Refused++; return null; }
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return null;
        EnsureImported();
        SongState st; string json;
        try { st = SongState.Capture(); json = st.ToJson(); }
        catch (Exception e) { Debug.LogWarning("Song capture failed: " + e.Message); return null; }
        var entry = AddEntry(CleanName(name) ?? NextName(), json, st, DateTime.UtcNow);
        if (entry == null) return null;
        Creates++;
        Bind(entry.id);
        Raise();
        return entry;
    }

    /// <summary>⌘S, the strip's "save", SongIO.Save: the world's song into its library file (unchanged = no write), or into a new entry "song N"
    /// when it is not a library song yet. False when nothing could be saved (no song, the library off, a disk error).</summary>
    public static bool SaveCurrent()
    {
        if (!Enabled) { Refused++; return false; }
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return false;
        EnsureImported();
        string live = LiveId;
        if (live == "") return Create(NextName()) != null;
        SongState st; string json;
        try { st = SongState.Capture(); json = st.ToJson(); }
        catch (Exception e) { Debug.LogWarning("Song capture failed: " + e.Message); return false; }
        return WriteSong(live, json, st);
    }

    /// <summary>Leaving the world for the menu, and quitting: like <see cref="SaveCurrent"/>, but only while SongIO.QuitAutosave is on (test
    /// harnesses switch it off and never write into the player's library), and a song that is not a library song yet gets an entry only when
    /// it holds the player's own work (an untouched fresh song or gallery demo is not kept).</summary>
    public static bool AutoSave()
    {
        if (!SongIO.QuitAutosave || !Enabled) { Refused++; return false; }
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return false;
        if (LiveId == "")
        {
            if (!LiveHasOwnWork) return false;
            if (discarded != null) { try { if (SongState.Capture().ToJson() == discarded) return false; } catch (Exception) { return false; } }   // deleted, not edited since
        }
        return SaveCurrent();
    }

    /// <summary>The world's song holds the player's work: cubes on its grids (a Moon's demo groove alone does not count), or more than one grid;
    /// a gallery demo exactly as it was opened does not.</summary>
    public static bool LiveHasOwnWork
    {
        get
        {
            var sm = SongManager.I;
            if (sm == null || !sm.HasSong) return false;
            if (Gallery.CurrentUntouched) return false;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.isFinalized && c.nodes.Count > 0 && !c.IsOnMoon) return true;
            int grids = 0;
            foreach (var kb in sm.Islands) if (kb != null) grids++;
            return grids > 1;
        }
    }

    /// <summary>The world's song is saved in <paramref name="id"/>'s file exactly as it is now.</summary>
    public static bool LiveSavedIn(string id)
    {
        if (string.IsNullOrEmpty(id) || SongManager.I == null || !SongManager.I.HasSong) return false;
        try { string p = PathOf(id); return File.Exists(p) && File.ReadAllText(p) == SongState.Capture().ToJson(); }
        catch (Exception) { return false; }
    }

    /// <summary>Loads song <paramref name="id"/> into the world (History reset); it becomes the current song and the world's library song. A
    /// world song with the player's work that the library does not hold is backed up first (SongIO.Backup). False when it cannot be read.</summary>
    public static bool Open(string id)
    {
        var e = Find(id);
        if (e == null || SongManager.I == null) return false;
        string p = PathOf(id);
        if (!File.Exists(p)) return false;
        string live = LiveId;
        if (live != id && LiveHasOwnWork && !LiveSavedIn(live)) SongIO.Backup();   // F15: never lose a song the library does not hold
        if (FocusLoop.Active) FocusLoop.Dismiss();
        bool ok;
        opening = true;
        try { ok = SongIO.LoadFrom(p); }
        finally { opening = false; }
        if (!ok) return false;
        Gallery.ClearCurrent();
        Bind(id);
        Opens++;
        Raise();
        return true;
    }

    /// <summary>SongIO.Load: the current library song, else the old single save.</summary>
    public static bool LoadCurrent()
    {
        string cur = CurrentId;
        if (cur != "" && Open(cur)) return true;
        return SongIO.LoadFrom(SongIO.Path);
    }

    public static bool Rename(string id, string name)
    {
        if (!Enabled) { Refused++; return false; }
        var e = Find(id);
        string n = CleanName(name);
        if (e == null || n == null) return false;
        if (n == e.name) return true;
        e.name = n;
        if (!SaveIndexSafe()) return false;
        Renames++;
        Raise();
        return true;
    }

    /// <summary>A copy of song <paramref name="id"/>'s file as a new entry "&lt;name&gt; copy" (newest, so it shows first). The world's song stays as it is.</summary>
    public static SongEntry Duplicate(string id)
    {
        if (!Enabled) { Refused++; return null; }
        var e = Find(id);
        if (e == null) return null;
        string json;
        try { json = File.ReadAllText(PathOf(id)); }
        catch (Exception ex) { Debug.LogWarning("Song copy failed: " + ex.Message); return null; }
        SongState st = null;
        try { st = SongState.FromJson(json); } catch (Exception) { }
        var copy = AddEntry(CopyName(e.name), json, st, DateTime.UtcNow);
        if (copy == null) return null;
        Duplicates++;
        Raise();
        return copy;
    }

    /// <summary>Moves song <paramref name="id"/> to songs/trash/ (its row to the trash list): <see cref="Restore"/> undoes it. Deleting the world's
    /// song leaves the world as it is, no longer a library song: an explicit save makes it a new entry, an autosave only once it was edited again.</summary>
    public static bool Delete(string id)
    {
        if (!Enabled) { Refused++; return false; }
        var e = Find(id);
        if (e == null) return false;
        bool wasCurrent = CurrentId == id;
        try
        {
            Directory.CreateDirectory(TrashRoot);
            string src = PathOf(id), dst = TrashPathOf(id);
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }
        catch (Exception ex) { Debug.LogWarning("Song delete failed: " + ex.Message); return false; }
        index.songs.Remove(e);
        e.deleted = Stamp(DateTime.UtcNow);
        index.trash.Insert(0, e);
        while (index.trash.Count > MaxTrash) index.trash.RemoveAt(index.trash.Count - 1);
        if (bound == id)
        {
            bound = null;
            try { discarded = SongState.Capture().ToJson(); } catch (Exception) { discarded = null; }
        }
        if (wasCurrent) SetCurrent("");
        SaveIndexSafe();
        Deletes++;
        Raise();
        return true;
    }

    /// <summary>Brings a deleted song back from the trash (its name and times as they were).</summary>
    public static bool Restore(string id)
    {
        if (!Enabled) { Refused++; return false; }
        Load();
        SongEntry e = null;
        foreach (var t in index.trash) if (t.id == id) { e = t; break; }
        if (e == null) return false;
        string src = TrashPathOf(id), dst = PathOf(id);
        try
        {
            if (!File.Exists(src)) { index.trash.Remove(e); SaveIndexSafe(); return false; }
            if (File.Exists(dst)) return false;
            File.Move(src, dst);
        }
        catch (Exception ex) { Debug.LogWarning("Song restore failed: " + ex.Message); return false; }
        index.trash.Remove(e);
        e.deleted = "";
        index.songs.Add(e);
        Sort();
        SaveIndexSafe();
        Restores++;
        Raise();
        return true;
    }

    /// <summary>The one-time import, the first time the library is used (before its first write, when the menu opens the shelf or continues;
    /// never while the library is off): an empty library that never imported takes the old single save (<see cref="LegacyPath"/>) as "my song" — a
    /// copy dated like the original, which stays where it was. The autosave the old Continue opened (<see cref="LegacyAutosavePath"/>) is not
    /// lost either: holding the same music, the newer of the two files is the one copied; holding a different song with the player's work that
    /// is newer than the old save, it comes in too, as "last session" ("my song" when there is no old save). The newest becomes the current
    /// song. True when something was imported.</summary>
    public static bool EnsureImported()
    {
        if (!Enabled) return false;
        Load();
        if (index.imported) return false;
        index.imported = true;
        int got = 0;
        if (index.songs.Count == 0)
        {
            string main = LegacyPath, auto = LegacyAutosavePath;
            SongState ms = File.Exists(main) ? ReadState(main) : null;
            SongState asv = !string.IsNullOrEmpty(auto) && File.Exists(auto) ? ReadState(auto) : null;
            if (asv != null && !HasWork(asv)) asv = null;   // an untouched fresh song in the autosave is not kept
            DateTime mt = ms != null ? File.GetLastWriteTimeUtc(main) : DateTime.MinValue, at = asv != null ? File.GetLastWriteTimeUtc(auto) : DateTime.MinValue;
            if (ms != null && asv != null && SameMusic(ms, asv))
            {
                if (ImportFile(at > mt ? auto : main, at > mt ? asv : ms, ImportName) != null) got++;   // one song: its newest state
            }
            else
            {
                if (ms != null && ImportFile(main, ms, ImportName) != null) got++;
                if (asv != null && (ms == null || at > mt) && ImportFile(auto, asv, ms == null ? ImportName : AutosaveImportName) != null) got++;
            }
            Sort();
            if (got > 0 && CurrentId == "") SetCurrent(index.songs[0].id);   // the newest of them
        }
        SaveIndexSafe();
        Imports += got;
        Raise();
        return got > 0;
    }

    /// <summary>A copy of an old slot as a new entry, dated like the file (the file itself is left where it is).</summary>
    static SongEntry ImportFile(string path, SongState st, string name)
    {
        try
        {
            DateTime when = File.GetLastWriteTimeUtc(path);
            string id = NewId(when);
            Directory.CreateDirectory(Root);
            File.Copy(path, PathOf(id), false);
            File.SetLastWriteTimeUtc(PathOf(id), when);
            var e = new SongEntry { id = id, name = name, created = Stamp(when), updated = Stamp(when), deleted = "" };
            Describe(e, st);
            index.songs.Add(e);
            return e;
        }
        catch (Exception ex) { Debug.LogWarning("Importing " + System.IO.Path.GetFileName(path) + " failed: " + ex.Message); return null; }
    }

    /// <summary>A song with the player's work in it: more than one grid, or a cube on a grid (a Moon's groove alone does not count).</summary>
    static bool HasWork(SongState st)
    {
        if (st == null || st.measures == null) return false;
        if (st.measures.Length > 1) return true;
        if (st.cubes != null) foreach (var c in st.cubes) if (c != null && c.moon < 0 && c.xs != null && c.xs.Length > 0) return true;
        return false;
    }

    /// <summary>The same music: the same chords in order (name, root, notes, bars) and the same cubes on the grids (instrument, grid, path) —
    /// the layout, the mix, the Moons' groove and the file's schema aside.</summary>
    static bool SameMusic(SongState a, SongState b)
    {
        if (a == null || b == null || a.measures == null || b.measures == null || a.measures.Length != b.measures.Length) return false;
        for (int i = 0; i < a.measures.Length; i++)
        {
            var x = a.measures[i]; var y = b.measures[i];
            if (x == null || y == null) { if (x != y) return false; continue; }
            if (x.chordKey != y.chordKey || x.root != y.root || x.bars != y.bars || !SameInts(x.semis, y.semis)) return false;
        }
        return CubeKeys(a) == CubeKeys(b);
    }

    static bool SameInts(int[] a, int[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static string CubeKeys(SongState s)
    {
        var keys = new List<string>();
        if (s.cubes != null)
            foreach (var c in s.cubes)
                if (c != null && c.moon < 0)
                    keys.Add(c.instrument + ":" + c.measure + ":" + string.Join(",", Array.ConvertAll(c.xs ?? new int[0], v => v.ToString())) + "/" + string.Join(",", Array.ConvertAll(c.zs ?? new int[0], v => v.ToString())));
        keys.Sort(StringComparer.Ordinal);
        return string.Join(";", keys.ToArray());
    }

    // ------------------------------------------------------------------ tests
    /// <summary>Tests: the library lives in <paramref name="root"/> (its own current-song key; the old save and autosave the import looks at are
    /// <paramref name="legacyPath"/> and <paramref name="autosavePath"/>) until <see cref="EndSandbox"/>. The player's library is not touched meanwhile.</summary>
    public static void UseSandbox(string root, string legacyPath, string autosavePath = null)
    {
        sandboxRoot = root; sandboxLegacy = legacyPath; sandboxAuto = autosavePath;
        index = null; current = null; bound = null; discarded = null; landing = false;
        Raise();
    }
    public static void EndSandbox()
    {
        if (sandboxRoot == null) return;
        PlayerPrefs.DeleteKey(PrefCurrent + ".sandbox");
        sandboxRoot = sandboxLegacy = sandboxAuto = null;
        index = null; current = null; bound = null; discarded = null; landing = false;
        Raise();
    }
    /// <summary>Forgets the cached index (it is read again from disk on the next use).</summary>
    public static void Reload() { index = null; current = null; Raise(); }

    // ------------------------------------------------------------------ names and times
    public static string NextName()
    {
        Load();
        int n = index.songs.Count + 1;
        while (NameTaken("song " + n)) n++;
        return "song " + n;
    }

    static bool NameTaken(string n) { foreach (var e in index.songs) if (string.Equals(e.name, n, StringComparison.OrdinalIgnoreCase)) return true; return false; }

    /// <summary>"&lt;name&gt; copy", then "&lt;name&gt; copy 2", … (a copy of a copy stays "… copy N", never "copy copy").</summary>
    public static string CopyName(string name)
    {
        Load();
        string b = string.IsNullOrEmpty(name) ? "song" : name;
        int k = b.LastIndexOf(" copy", StringComparison.Ordinal);
        if (k > 0)
        {
            string tail = b.Substring(k + 5).Trim();
            int dummy;
            if (tail.Length == 0 || int.TryParse(tail, out dummy)) b = b.Substring(0, k);
        }
        string c = b + " copy";
        for (int i = 2; NameTaken(c); i++) c = b + " copy " + i;
        return CleanName(c) ?? "song copy";
    }

    /// <summary>One line, trimmed, single spaces, at most <see cref="MaxName"/> characters; null when nothing is left.</summary>
    public static string CleanName(string s)
    {
        if (s == null) return null;
        s = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        if (s.Length > MaxName) s = s.Substring(0, MaxName).TrimEnd();
        return s.Length == 0 ? null : s;
    }

    /// <summary>"edited just now", "edited 5 min ago", "edited 3 hours ago", "edited yesterday", "edited 4 days ago", "edited sep 12".</summary>
    public static string Edited(SongEntry e) => e == null ? "" : "edited " + Ago(e.UpdatedUtc, DateTime.UtcNow);

    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        if (utc == DateTime.MinValue) return "a while ago";
        var d = nowUtc - utc;
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
        if (d.TotalHours < 24) { int h = (int)d.TotalHours; return h == 1 ? "1 hour ago" : h + " hours ago"; }
        DateTime local = utc.ToLocalTime(), today = nowUtc.ToLocalTime().Date;
        int days = (int)Math.Round((today - local.Date).TotalDays);
        if (days <= 1) return "yesterday";
        if (days < 7) return days + " days ago";
        string m = local.ToString("MMM", CultureInfo.InvariantCulture).ToLowerInvariant() + " " + local.Day;
        return local.Year == today.Year ? m : m + " " + local.Year;
    }

    public static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    public static DateTime ParseTime(string s)
    {
        DateTime t;
        if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return t;
        return DateTime.MinValue;
    }

    // ------------------------------------------------------------------ the index on disk
    static void Load()
    {
        if (index != null) return;
        LibraryIndex read = null;
        try { if (File.Exists(IndexPath)) read = JsonUtility.FromJson<LibraryIndex>(File.ReadAllText(IndexPath)); }
        catch (Exception e) { Debug.LogWarning("Song library index unreadable (rebuilt from the song files): " + e.Message); read = null; }
        index = read ?? new LibraryIndex();
        if (index.songs == null) index.songs = new List<SongEntry>();
        if (index.trash == null) index.trash = new List<SongEntry>();
        bool changed = Reconcile();
        Sort();
        if (changed && Enabled && Directory.Exists(Root)) SaveIndexSafe();
    }

    /// <summary>The index agrees with the folder: rows without a file go, song files without a row come in (named "song N", their file's
    /// time), trash rows without a trash file go, rows without a description get one.</summary>
    static bool Reconcile()
    {
        bool changed = false;
        var seen = new HashSet<string>();
        for (int i = index.songs.Count - 1; i >= 0; i--)
        {
            var e = index.songs[i];
            if (e == null || string.IsNullOrEmpty(e.id) || !seen.Add(e.id) || !File.Exists(PathOf(e.id))) { index.songs.RemoveAt(i); changed = true; continue; }
            if (string.IsNullOrEmpty(e.name)) { e.name = "song"; changed = true; }
            if (e.colors == null) { Describe(e, ReadState(PathOf(e.id))); changed = true; }
            if (e.deleted == null) e.deleted = "";
        }
        for (int i = index.trash.Count - 1; i >= 0; i--)
        {
            var e = index.trash[i];
            if (e == null || string.IsNullOrEmpty(e.id) || !File.Exists(TrashPathOf(e.id))) { index.trash.RemoveAt(i); changed = true; }
        }
        try
        {
            if (Directory.Exists(Root))
                foreach (var f in Directory.GetFiles(Root, "*.json"))
                {
                    string file = System.IO.Path.GetFileName(f);
                    if (file == IndexName) continue;
                    string id = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (seen.Contains(id)) continue;
                    var st = ReadState(f);
                    if (st == null) continue;
                    DateTime when = File.GetLastWriteTimeUtc(f);
                    var e = new SongEntry { id = id, created = Stamp(when), updated = Stamp(when), deleted = "" };
                    e.name = NextNameAmong(index.songs);
                    Describe(e, st);
                    index.songs.Add(e); seen.Add(id);
                    changed = true;
                }
        }
        catch (Exception ex) { Debug.LogWarning("Song library scan: " + ex.Message); }
        return changed;
    }

    static string NextNameAmong(List<SongEntry> list)
    {
        int n = list.Count + 1;
        for (; ; n++)
        {
            string c = "song " + n; bool taken = false;
            foreach (var e in list) if (e != null && string.Equals(e.name, c, StringComparison.OrdinalIgnoreCase)) { taken = true; break; }
            if (!taken) return c;
        }
    }

    static SongState ReadState(string path)
    {
        try
        {
            var st = SongState.FromJson(File.ReadAllText(path));
            return st != null && st.measures != null && st.measures.Length > 0 ? st : null;
        }
        catch (Exception) { return null; }
    }

    static void Sort()
    {
        index.songs.Sort((a, b) =>
        {
            int c = b.UpdatedUtc.CompareTo(a.UpdatedUtc);
            if (c != 0) return c;
            c = b.CreatedUtc.CompareTo(a.CreatedUtc);
            return c != 0 ? c : string.CompareOrdinal(b.id, a.id);
        });
    }

    static bool SaveIndexSafe()
    {
        if (!Enabled) return false;
        try
        {
            Directory.CreateDirectory(Root);
            SongIO.WriteAtomic(IndexPath, JsonUtility.ToJson(index, true));
            return true;
        }
        catch (Exception e) { Debug.LogWarning("Song library index save failed: " + e.Message); return false; }
    }

    static SongEntry AddEntry(string name, string json, SongState st, DateTime whenUtc)
    {
        Load();
        whenUtc = AfterNewest(whenUtc);
        string id = NewId(whenUtc);
        try
        {
            Directory.CreateDirectory(Root);
            SongIO.WriteAtomic(PathOf(id), json);
        }
        catch (Exception e) { Debug.LogWarning("Song save failed: " + e.Message); return null; }
        var entry = new SongEntry { id = id, name = name, created = Stamp(whenUtc), updated = Stamp(whenUtc), deleted = "" };
        Describe(entry, st);
        index.songs.Add(entry);
        Sort();
        SaveIndexSafe();
        return entry;
    }

    /// <summary>Writes <paramref name="json"/> into song <paramref name="id"/>'s file and stamps it edited now — unless the file already holds
    /// exactly that (then nothing is written: its "edited" time stays true).</summary>
    static bool WriteSong(string id, string json, SongState st)
    {
        var e = Find(id);
        if (e == null) return false;
        string p = PathOf(id);
        try
        {
            if (File.Exists(p) && File.ReadAllText(p) == json) return true;
            Directory.CreateDirectory(Root);
            SongIO.WriteAtomic(p, json);
        }
        catch (Exception ex) { Debug.LogWarning("Song save failed: " + ex.Message); return false; }
        e.updated = Stamp(AfterNewest(DateTime.UtcNow));
        Describe(e, st);
        Sort();
        SaveIndexSafe();
        Saves++;
        Raise();
        return true;
    }

    /// <summary>The card's facts of a song: its column count, tempo and the chord colour of each of its first 8 columns (the column's chord
    /// grid, else its first grid).</summary>
    static void Describe(SongEntry e, SongState st)
    {
        if (st == null || st.measures == null || st.measures.Length == 0) { e.columns = 0; e.colors = new string[0]; return; }
        var ms = new List<MeasureState>();
        foreach (var m in st.measures) if (m != null) ms.Add(m);
        var arr = ms.ToArray();
        var col = MenuPreview.ColumnsOf(arr);
        int C = 0;
        foreach (int c in col) C = Mathf.Max(C, c + 1);
        e.columns = C;
        e.bpm = st.bpm;
        var list = new List<string>();
        for (int c = 0; c < C && list.Count < MaxColors; c++)
        {
            MeasureState pick = null;
            for (int i = 0; i < arr.Length; i++)
                if (col[i] == c && (pick == null || (pick.kind != 0 && arr[i].kind == 0))) pick = arr[i];
            if (pick == null) continue;
            var semis = pick.semis != null && pick.semis.Length > 0 ? pick.semis : new[] { 0, 4, 7 };
            list.Add(ColorUtility.ToHtmlStringRGB(Comic.Opaque(MusicTheory.ChordColor(pick.root, semis))));
        }
        e.colors = list.ToArray();
    }

    /// <summary><paramref name="utc"/>, nudged just past the newest song's time when the clock has not moved on since (two songs stamped in
    /// the same instant would otherwise sort by chance): what was made or saved last is always first.</summary>
    static DateTime AfterNewest(DateTime utc)
    {
        if (index != null && index.songs.Count > 0)
        {
            var newest = index.songs[0].UpdatedUtc;
            if (utc <= newest) utc = newest.AddTicks(10);
        }
        return utc;
    }

    static string NewId(DateTime whenUtc)
    {
        for (; ; )
        {
            string id = whenUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
            if (!File.Exists(PathOf(id)) && !File.Exists(TrashPathOf(id))) return id;
        }
    }

    static void Raise()
    {
        var h = OnChanged;
        if (h == null) return;
        try { h(); } catch (Exception e) { Debug.LogException(e); }
    }
}
