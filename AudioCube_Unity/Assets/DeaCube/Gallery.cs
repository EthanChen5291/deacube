using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One gallery song (SPEC v5 §2.3): a demo "level" shipped in Resources/Gallery.</summary>
[Serializable]
public class GalleryEntry
{
    public string id;        // stable id (the file's name)
    public string title;     // lowercase, 1-2 words
    public string after;     // "<original title> · <producer>" (the song it imitates; shown small)
    public int vibe;         // VibeKind of its home chord (the card's colour and glyph)
    public float bpm;
    public int bars;         // song length in bars (passes included)
    public string file;      // Resources path without extension, e.g. "Gallery/sugar-rush"
    public string blurb;     // one short line (optional)
    public int hook;         // v5 T (optional): the bar the title menu's preview starts on (0 = the first bar)
}

/// <summary>
/// SPEC v5 §2.3 — the gallery: demo songs imitating the Japanese corpus (package G writes them, package T shows them in the title menu).
/// <see cref="Entries"/> reads Resources/Gallery/index.json once; <see cref="LoadState"/> parses a song; <see cref="Open"/> makes it the working
/// song (a song with cubes of the player's own is backed up first, never the user's main save), History reset, playing from bar 0.
/// v5 T: <see cref="UseTestSongs"/> serves injected entries and song texts instead of Resources (tests do not depend on the songs being
/// written yet); <see cref="OnEntriesChanged"/> tells the menu when the list changed; <see cref="CurrentId"/> lapses by itself once another song
/// replaced the opened one (it is valid while the working song still carries the opened song's name — edits and undo keep it); opening a
/// gallery song over the gallery song opened before, untouched, backs nothing up (browsing the demos never rotates the player's own songs out
/// of the three backup slots) — once the player changed it, it is theirs and it is backed up like any song with cubes.
/// </summary>
public static class Gallery
{
    [Serializable] class Index { public GalleryEntry[] songs; }

    static List<GalleryEntry> entries;
    static List<GalleryEntry> injected;
    static Dictionary<string, string> injectedSongs;
    static string openedId = "", openedName, openedJson;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries = null; injected = null; injectedSongs = null; openedId = ""; openedName = null; openedJson = null; BackupsSkipped = 0; OnOpened = null; OnEntriesChanged = null; }

    /// <summary>The gallery song opened last ("" = none, or the player made / loaded another song since).</summary>
    public static string CurrentId
    {
        get
        {
            if (string.IsNullOrEmpty(openedId)) return "";
            var sm = SongManager.I;
            if (sm == null || !sm.HasSong || sm.currentSongData == null || sm.currentSongData.songName != openedName) return "";
            return openedId;
        }
    }
    /// <summary>Opens that skipped the backup because the song they replaced was the gallery song opened before, untouched (tests).</summary>
    public static int BackupsSkipped { get; private set; }
    /// <summary>True while the working song is the gallery song opened last, exactly as it was opened (nothing of the player's in it).</summary>
    public static bool CurrentUntouched
    {
        get
        {
            if (CurrentId == "" || string.IsNullOrEmpty(openedJson)) return false;
            try { return SongState.Capture().ToJson() == openedJson; } catch (Exception) { return false; }
        }
    }
    /// <summary>Raised after <see cref="Open"/> applied a song (its id).</summary>
    public static event Action<string> OnOpened;
    /// <summary>v5 T: raised when the list may have changed (<see cref="Reload"/>, <see cref="UseTestSongs"/>, <see cref="ClearTestSongs"/>).</summary>
    public static event Action OnEntriesChanged;

    public static IReadOnlyList<GalleryEntry> Entries { get { if (injected != null) return injected; if (entries == null) Load(); return entries; } }

    /// <summary>True while tests serve their own songs (<see cref="UseTestSongs"/>).</summary>
    public static bool UsingTestSongs => injected != null;

    /// <summary>Re-reads the index (tests, and after the builder wrote new songs in the editor).</summary>
    public static void Reload()
    {
        Load();
        OnEntriesChanged?.Invoke();
    }

    static void Load()
    {
        entries = new List<GalleryEntry>();
        try
        {
            var ta = Resources.Load<TextAsset>("Gallery/index");
            if (ta == null) return;
            var idx = JsonUtility.FromJson<Index>(ta.text);
            if (idx != null && idx.songs != null) foreach (var e in idx.songs) if (e != null && !string.IsNullOrEmpty(e.id) && !string.IsNullOrEmpty(e.file)) entries.Add(e);
        }
        catch (Exception e) { Debug.LogWarning("Gallery index: " + e.Message); }
    }

    /// <summary>Tests (package T): serve <paramref name="list"/> as the gallery and <paramref name="songs"/> (an entry's file or id → its SongState
    /// JSON) as the song texts, instead of Resources/Gallery, until <see cref="ClearTestSongs"/>. An empty list = a gallery without songs.</summary>
    public static void UseTestSongs(IList<GalleryEntry> list, IDictionary<string, string> songs)
    {
        injected = new List<GalleryEntry>();
        if (list != null) foreach (var e in list) if (e != null && !string.IsNullOrEmpty(e.id)) injected.Add(e);
        injectedSongs = songs != null ? new Dictionary<string, string>(songs) : new Dictionary<string, string>();
        OnEntriesChanged?.Invoke();
    }

    /// <summary>Back to the real index (Resources/Gallery).</summary>
    public static void ClearTestSongs()
    {
        if (injected == null && injectedSongs == null) return;
        injected = null; injectedSongs = null;
        OnEntriesChanged?.Invoke();
    }

    public static GalleryEntry Find(string id)
    {
        foreach (var e in Entries) if (e.id == id) return e;
        return null;
    }

    /// <summary>The song text of <paramref name="e"/>: an injected one (tests), else the TextAsset at <see cref="GalleryEntry.file"/>.</summary>
    static string TextOf(GalleryEntry e)
    {
        string text;
        if (injectedSongs != null && ((!string.IsNullOrEmpty(e.file) && injectedSongs.TryGetValue(e.file, out text)) || injectedSongs.TryGetValue(e.id, out text))) return text;
        if (string.IsNullOrEmpty(e.file)) return null;
        var ta = Resources.Load<TextAsset>(e.file);
        return ta != null ? ta.text : null;
    }

    /// <summary>A fresh SongState of gallery song <paramref name="id"/> (null when it is missing or malformed).</summary>
    public static SongState LoadState(string id)
    {
        var e = Find(id);
        if (e == null) return null;
        try
        {
            string text = TextOf(e);
            if (string.IsNullOrEmpty(text)) return null;
            var st = SongState.FromJson(text);
            if (st == null || st.measures == null || st.measures.Length == 0) return null;
            if (string.IsNullOrEmpty(st.name)) st.name = e.title;
            return st;
        }
        catch (Exception ex) { Debug.LogWarning("Gallery song " + id + ": " + ex.Message); return null; }
    }

    /// <summary>Makes gallery song <paramref name="id"/> the working song: backs up a song with cubes (SongIO.Backup), applies it, resets History,
    /// plays from bar 0 when <paramref name="play"/>. False when the song could not be read (nothing changes then).</summary>
    public static bool Open(string id, bool play = true)
    {
        var st = LoadState(id);
        if (st == null || SongManager.I == null) return false;
        if (FocusLoop.Active) FocusLoop.Dismiss();
        GlobalClock.Stop();
        if (CurrentUntouched) BackupsSkipped++;
        else SongIO.Backup();
        SongState.Apply(st);
        History.Reset();
        History.Push();
        openedId = id;
        openedName = SongManager.I.currentSongData != null ? SongManager.I.currentSongData.songName : st.name;
        try { openedJson = SongState.Capture().ToJson(); } catch (Exception) { openedJson = null; }
        GlobalClock.Seek(0);
        if (play) { GlobalClock.Play(); AudioCube.ScheduleAllNow(); }   // queued before the new world's heavy first frames
        OnOpened?.Invoke(id);
        return true;
    }

    /// <summary>Forgets the current gallery id (a new / loaded song replaced it).</summary>
    public static void ClearCurrent() { openedId = ""; openedName = null; openedJson = null; }
}
