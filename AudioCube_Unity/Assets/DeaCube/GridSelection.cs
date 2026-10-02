using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// v7 (SPEC v7 §16.1–16.2; package O): the grids the player selected with Shift ("hold shift and drag over a section to then select all the
/// grids in that section and like paste or delete or other options") — D's marquee / Shift + click / a section plinth fill it, U2's selection bar
/// and hotkeys act on it through SongOps, W draws the ink outline, the soft lift and the shimmer from <see cref="Selected"/>. Kept in song order
/// (islands, then Moons); Moons only when explicitly given (the marquee covering their centre, a Shift + click).
/// Every structural rebuild re-creates the islands: SongOps re-selects the rebuilt grids of its own ops; any other rebuild (an undo / redo, a
/// header op) keeps the selection on the same places while the song still has the same islands in the same columns, else drops the grids that
/// are gone. <see cref="OnChanged"/> fires on every change of the list.
/// </summary>
public static class GridSelection
{
    static readonly List<KeyBlock> sel = new List<KeyBlock>();
    // where the selection stood (island index, its column and kind; Moon index) and the song's size then: a rebuild of the same islands re-selects there
    static readonly List<int> markIsl = new List<int>(), markCol = new List<int>(), markKind = new List<int>(), markMoon = new List<int>();
    static int markCount = -1, markMoons = -1;
    static SongManager hooked;

    /// <summary>The selected grids in song order (islands, then Moons). The live list: copy it before handing it to an op that re-selects.</summary>
    public static List<KeyBlock> Selected { get { Prune(false); return sel; } }
    public static bool Any { get { Prune(false); return sel.Count > 0; } }
    public static int Count { get { Prune(false); return sel.Count; } }
    public static event Action OnChanged;

    public static bool Contains(KeyBlock kb) => kb != null && sel.Contains(kb);

    /// <summary>Replaces the selection with the live grids of <paramref name="list"/> (duplicates and dead ones dropped).</summary>
    public static void Set(IEnumerable<KeyBlock> list)
    {
        Hook();
        sel.Clear();
        if (list != null) foreach (var kb in list) if (Live(kb) && !sel.Contains(kb)) sel.Add(kb);
        Sort();
        Mark();
        if (OnChanged != null) OnChanged();
    }

    /// <summary>Shift + click: a grid in or out.</summary>
    public static void Toggle(KeyBlock kb)
    {
        if (!Live(kb)) return;
        Hook();
        if (!sel.Remove(kb)) sel.Add(kb);
        Sort();
        Mark();
        if (OnChanged != null) OnChanged();
    }

    public static void Add(KeyBlock kb)
    {
        if (!Live(kb) || sel.Contains(kb)) return;
        Hook();
        sel.Add(kb);
        Sort();
        Mark();
        if (OnChanged != null) OnChanged();
    }

    public static void Remove(KeyBlock kb)
    {
        if (kb == null || !sel.Remove(kb)) return;
        Mark();
        if (OnChanged != null) OnChanged();
    }

    /// <summary>Every grid (chord islands, keyboards, stairs, phrases — not Moons) of section <paramref name="s"/>.</summary>
    public static void SelectSection(int s)
    {
        var sm = SongManager.I;
        var list = new List<KeyBlock>();
        if (sm != null && s >= 0 && s < sm.SectionCount)
        {
            int a = sm.SectionFirst(s), b = sm.SectionLast(s);
            foreach (var kb in sm.Islands) if (kb != null && !kb.IsMoon && kb.column >= a && kb.column <= b) list.Add(kb);
        }
        Set(list);
    }

    public static void Clear()
    {
        if (sel.Count == 0) return;
        sel.Clear();
        Mark();
        if (OnChanged != null) OnChanged();
    }

    /// <summary>The columns the selected islands stand in, ascending (Moons: none).</summary>
    public static List<int> Columns
    {
        get
        {
            var r = new List<int>();
            foreach (var kb in Selected) if (kb != null && !kb.IsMoon && !r.Contains(kb.column)) r.Add(kb.column);
            r.Sort();
            return r;
        }
    }
    /// <summary>The first / last column of the selected islands (−1 when none): the selection bar pastes after <see cref="LastColumn"/>.</summary>
    public static int FirstColumn { get { var c = Columns; return c.Count > 0 ? c[0] : -1; } }
    public static int LastColumn { get { var c = Columns; return c.Count > 0 ? c[c.Count - 1] : -1; } }

    /// <summary>True when the selection is every grid of one section (and nothing else but its Moons): <paramref name="s"/> is that section.</summary>
    public static bool IsWholeSection(out int s)
    {
        s = -1;
        var sm = SongManager.I;
        var cols = Columns;
        if (sm == null || cols.Count == 0) return false;
        int sec = sm.SectionOf(cols[0]);
        int a = sm.SectionFirst(sec), b = sm.SectionLast(sec);
        foreach (var kb in sm.Islands) if (kb != null && kb.column >= a && kb.column <= b && !sel.Contains(kb)) return false;
        foreach (var kb in sel) if (kb != null && !kb.IsMoon && (kb.column < a || kb.column > b)) return false;
        s = sec;
        return true;
    }

    static bool Live(KeyBlock kb)
    {
        var sm = SongManager.I;
        return kb != null && sm != null && (sm.Islands.Contains(kb) || sm.Moons.Contains(kb));
    }

    static bool Prune(bool notify)
    {
        bool changed = false;
        for (int i = sel.Count - 1; i >= 0; i--) if (!Live(sel[i])) { sel.RemoveAt(i); changed = true; }
        if (changed) { Mark(); if (notify && OnChanged != null) OnChanged(); }
        return changed;
    }

    static void Sort()
    {
        var sm = SongManager.I;
        if (sm == null) return;
        sel.Sort((a, b) =>
        {
            int ka = a.IsMoon ? 100000 + sm.Moons.IndexOf(a) : sm.Islands.IndexOf(a), kb = b.IsMoon ? 100000 + sm.Moons.IndexOf(b) : sm.Islands.IndexOf(b);
            return ka.CompareTo(kb);
        });
    }

    static void Mark()
    {
        markIsl.Clear(); markCol.Clear(); markKind.Clear(); markMoon.Clear();
        var sm = SongManager.I;
        if (sm == null) { markCount = markMoons = -1; return; }
        foreach (var kb in sel)
        {
            int i = sm.Islands.IndexOf(kb);
            if (i >= 0) { markIsl.Add(i); markCol.Add(kb.column); markKind.Add(kb.kind); continue; }
            int j = sm.Moons.IndexOf(kb);
            if (j >= 0) markMoon.Add(j);
        }
        markCount = sm.Islands.Count; markMoons = sm.Moons.Count;
    }

    static void Hook()
    {
        var sm = SongManager.I;
        if (sm == hooked) return;
        if (hooked != null) hooked.OnSongRebuilt -= Rebuilt;
        hooked = sm;
        if (sm != null) sm.OnSongRebuilt += Rebuilt;
    }

    /// <summary>A rebuild this class did not ask for (SongOps re-selects its own): the same islands in the same columns stay selected, else the
    /// grids that are gone drop out.</summary>
    static void Rebuilt()
    {
        if (SongOps.Busy || sel.Count == 0) return;
        var sm = SongManager.I;
        if (sm == null) return;
        bool dead = false;
        foreach (var kb in sel) if (!Live(kb)) { dead = true; break; }
        if (!dead) return;
        bool same = sm.Islands.Count == markCount && sm.Moons.Count == markMoons;
        for (int k = 0; k < markIsl.Count && same; k++)
        {
            int i = markIsl[k];
            var kb = i >= 0 && i < sm.Islands.Count ? sm.Islands[i] : null;
            if (kb == null || kb.column != markCol[k] || kb.kind != markKind[k]) same = false;
        }
        if (!same) { Prune(true); return; }
        sel.Clear();
        foreach (int i in markIsl) sel.Add(sm.Islands[i]);
        foreach (int j in markMoon) if (j >= 0 && j < sm.Moons.Count && sm.Moons[j] != null) sel.Add(sm.Moons[j]);
        Sort();
        Mark();
        if (OnChanged != null) OnChanged();
    }
}
