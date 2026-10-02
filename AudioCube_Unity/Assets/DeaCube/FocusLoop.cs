using System;
using UnityEngine;

/// <summary>
/// SPEC v4 §2.4 / §4 R4: the focus loop. Drawing a path or inspecting a cube loops that island's column (a GlobalClock region) so the
/// edit is heard in context, over and over; it stays until dismissed (Esc with nothing else to close, a click on empty sea, the HUD
/// badge, Present, the menu, a new song). Dismissing restores the transport: stopped if it was stopped when the focus began, else the
/// whole song keeps playing from where it is.
/// Begin(column): region = the column's span; a stopped clock seeks to the region start and plays; a clock playing outside the region
/// jumps to its start (with the play latency, so its first notes are not late). Beginning on another column moves the loop (the
/// transport memory of the first Begin is kept).
/// <see cref="Tick"/> (PathManager.Update, every frame) keeps the region on the column's span through bar / layout edits and releases
/// the loop when the column is gone or the presentation / the menu take the clock.
/// v7 (D, SPEC v7 §14.4): drawing on a melody PHRASE loops the phrase's own span (<see cref="BeginPhrase"/>: it may cross several columns, and it
/// follows the phrase as it grows while being drawn); <see cref="Column"/> is then the phrase's start column.
/// </summary>
public static class FocusLoop
{
    public static bool Active { get; private set; }
    /// <summary>The looped column (-1 when inactive).</summary>
    public static int Column { get; private set; } = -1;
    /// <summary>v7: the looped melody phrase (its index in SongManager.Islands), -1 = a column loop.</summary>
    public static int Phrase { get; private set; } = -1;
    public static event Action OnChanged;
    /// <summary>The transport was stopped when the focus began: <see cref="Dismiss"/> stops it again.</summary>
    public static bool WasStopped { get; private set; }
    /// <summary>Frame of the last <see cref="ClaimEsc"/> (-10 none).</summary>
    public static int EscClaimFrame { get; private set; } = -10;

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Active = false; Column = -1; Phrase = -1; WasStopped = false; EscClaimFrame = -10; hooked = false; }

    /// <summary>Loops <paramref name="column"/> (see the class summary). Ignored without a song or for a column that does not exist.</summary>
    public static void Begin(int column)
    {
        var sm = SongManager.I;
        double start, end;
        if (sm == null || !sm.HasSong || !Span(column, out start, out end)) return;
        Hook();
        bool changed = !Active || Column != column || Phrase >= 0;
        if (!Active) WasStopped = !GlobalClock.IsPlaying;
        Active = true; Column = column; Phrase = -1;
        StartRegion(start, end);
        if (changed) OnChanged?.Invoke();
    }

    /// <summary>v7 (§14.4): loops melody phrase <paramref name="island"/> (its index in SongManager.Islands) over its span, however many columns it
    /// crosses; like <see cref="Begin"/> otherwise (a stopped clock plays from the phrase's start, the transport memory of the first Begin is kept).</summary>
    public static void BeginPhrase(int island)
    {
        double start, end;
        if (!PhraseSpan(island, out start, out end)) return;
        Hook();
        var kb = SongManager.I.Islands[island];
        bool changed = !Active || Phrase != island;
        if (!Active) WasStopped = !GlobalClock.IsPlaying;
        Active = true; Column = kb.column; Phrase = island;
        StartRegion(start, end);
        if (changed) OnChanged?.Invoke();
    }

    /// <summary>v7: song-beat span [start, end) of melody phrase <paramref name="island"/> (false when that island is not a phrase).</summary>
    public static bool PhraseSpan(int island, out double start, out double end)
    {
        start = end = 0.0;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || island < 0 || island >= sm.Islands.Count) return false;
        var kb = sm.Islands[island];
        if (kb == null || !kb.IsPhrase) return false;
        float a, b;
        sm.PhraseSpan(kb, out a, out b);
        if (b <= a) return false;
        start = a; end = b;
        return true;
    }

    static void StartRegion(double start, double end)
    {
        GlobalClock.SetRegion(start, end);
        if (!GlobalClock.IsPlaying)
        {
            GlobalClock.Seek(start);
            GlobalClock.Play();
        }
        else
        {
            double beat = GlobalClock.BeatNow;
            if (beat < start - 1e-6 || beat >= end) JumpTo(start);
        }
    }

    /// <summary>Moves a playing clock to <paramref name="beat"/> the way Play starts it (GlobalClock.PlayLatency ahead), so the notes on
    /// that beat are scheduled in time instead of late (a plain Seek while playing puts the beat at "now").</summary>
    static void JumpTo(double beat)
    {
        if (!GlobalClock.IsPlaying) { GlobalClock.Seek(beat); return; }
        GlobalClock.Pause();
        GlobalClock.Seek(beat);
        GlobalClock.Play();
    }

    /// <summary>Ends the focus loop: the region goes and the transport is restored (stopped if it was stopped when the focus began, notes
    /// ring out; else the whole song keeps playing from where it is).</summary>
    public static void Dismiss()
    {
        if (!Active) return;
        bool stop = WasStopped;
        Release();
        if (stop) GlobalClock.Stop();   // paused inside the loop counts too: the transport goes back to stopped
    }

    /// <summary>Ends the focus loop without touching the transport (the presentation and the menu take the clock over themselves).</summary>
    public static void Release()
    {
        if (!Active) return;
        Active = false; Column = -1; Phrase = -1; WasStopped = false;
        GlobalClock.ClearRegion();
        OnChanged?.Invoke();
    }

    /// <summary>A UI that consumed Esc this frame claims it, so the same key press does not also dismiss the focus loop
    /// (PathManager decides in LateUpdate). The tray, the chord wheel, rings, grid popovers, the inspector, the prompt, the presenter and
    /// the menu are recognised without a claim.</summary>
    public static void ClaimEsc() { EscClaimFrame = Time.frameCount; }
    public static bool EscClaimedRecently => Time.frameCount - EscClaimFrame <= 1;

    /// <summary>Song-beat span [start, end) of <paramref name="column"/> (false when there is no such column).</summary>
    public static bool Span(int column, out double start, out double end)
    {
        start = end = 0.0;
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong || column < 0 || column >= sm.ColumnCount) return false;
        float len = sm.ColumnLength(column);
        if (len <= 0f) return false;
        start = sm.ColumnStart(column);
        end = start + len;
        return true;
    }

    /// <summary>Every frame (PathManager.Update): keeps the region on the column's current span (bars, layout and tempo-map edits), releases
    /// the loop when the column is gone, and hands the clock over to the presentation (Release) or the menu (Dismiss).</summary>
    public static void Tick()
    {
        if (!Active) return;
        if (Presenter.Active) { Release(); return; }
        if (MainMenu.IsShown) { Dismiss(); return; }
        double s, e;
        if (!(Phrase >= 0 ? PhraseSpan(Phrase, out s, out e) : Span(Column, out s, out e))) { Release(); return; }
        if (!GlobalClock.HasRegion || Math.Abs(s - GlobalClock.RegionStart) > 1e-6 || Math.Abs(e - GlobalClock.RegionEnd) > 1e-6)
        {
            GlobalClock.SetRegion(s, e);
            if (GlobalClock.IsPlaying) { double beat = GlobalClock.BeatNow; if (beat < s - 1e-6 || beat >= e) JumpTo(s); }
            OnChanged?.Invoke();
        }
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        History.OnChanged += HandleHistory;
    }

    // a new song (SongManager.BeginSong) and a load (SongIO.LoadFrom) reset History: the focus of the old song ends with it
    static void HandleHistory() { if (Active && History.UndoCount == 0) Dismiss(); }
}
