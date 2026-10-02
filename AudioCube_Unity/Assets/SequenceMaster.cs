using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Registry of live cubes, tile occupancy (so cubes stack instead of overlapping),
/// timeline recalculation and global transport hotkeys.
/// v7 (A, SPEC v7 §5.1: "layer cubes never occupy tiles"): a cube in an octave layer (AudioCube.IsLayered — an octave copy / echo riding above or
/// below its grid) never occupies a tile and never counts in a stack (no stacking with the grid's cubes: no stack height, no stack octave lift);
/// the launch riser (LaunchRiser) is ticked here every frame.
/// </summary>
public class SequenceMaster : MonoBehaviour
{
    public static SequenceMaster I;
    public static float TotalSongLength => GlobalClock.TotalBeats;

    public static readonly List<AudioCube> Cubes = new List<AudioCube>();
    static readonly Dictionary<TileInteraction, List<AudioCube>> occupancy = new Dictionary<TileInteraction, List<AudioCube>>();
    public static event Action<TileInteraction> OnOccupancyChanged;

    void Awake() { I = this; }
    void OnEnable() { GlobalClock.OnStop += HandleStop; }
    void OnDisable() { GlobalClock.OnStop -= HandleStop; }
    void HandleStop() { ResetAllCubes(); }

    void Update()
    {
        Performance.Tick();
        LaunchRiser.Tick();   // v7: the launch riser + landing crash (SPEC v7 §4.4)
        if (InputUtil.TypingInField) return;
        // v3 (SPEC §1, §4.4): P presents (Space stays play / pause); the menu, the presentation and the prompt own the keys while they lock them
        if (Input.GetKeyDown(KeyCode.P) && !InputUtil.Cmd && !WorldInput.KeysLocked && !Presenter.Active) { Presenter.Enter(); return; }
        if (WorldInput.KeysLocked) return;
        if (Input.GetKeyDown(KeyCode.Space) && !PathManager.OwnsSpace) GlobalClock.Toggle();   // v7 §19.2 (D): while drawing, Space = hear the draft (PathManager)
        if (Input.GetKeyDown(KeyCode.Home)) GlobalClock.Stop();
        if (Input.GetKeyDown(KeyCode.L)) { GlobalClock.LoopSong = !GlobalClock.LoopSong; History.Push(); }   // loop lives in the snapshot
        if (Input.GetKeyDown(KeyCode.M)) GlobalClock.SetMetronome(!GlobalClock.Metronome);
    }

    public static void Register(AudioCube c) { if (c != null && !Cubes.Contains(c)) Cubes.Add(c); }
    public static void Unregister(AudioCube c)
    {
        Cubes.Remove(c);
        foreach (var kv in occupancy) if (kv.Value.Remove(c)) OnOccupancyChanged?.Invoke(kv.Key);
    }

    public static void RecalculateTimeline() { if (SongManager.I != null) SongManager.I.RecomputeMeasureStarts(); }

    public static void ResetAllCubes() { foreach (var c in Cubes) if (c != null) c.ResetToStart(); }

    // ---------------- tile occupancy (stacking)
    /// <summary>v7: true for a cube that lives in its octave layer (never on a tile's stack).</summary>
    static bool Layered(AudioCube c) => c != null && c.IsLayered;

    /// <summary>Puts <paramref name="cube"/> on <paramref name="tile"/>'s stack; returns its index there. v7: a layer cube never occupies (it is taken off any
    /// stack it was on — its layer changed — and gets −1).</summary>
    public static int Occupy(TileInteraction tile, AudioCube cube)
    {
        if (tile == null || cube == null) return 0;
        if (Layered(cube)) { ReleaseEverywhere(cube); return -1; }
        if (!occupancy.TryGetValue(tile, out var list)) { list = new List<AudioCube>(); occupancy[tile] = list; }
        int i = list.IndexOf(cube);
        if (i >= 0) return i;
        list.Add(cube);
        OnOccupancyChanged?.Invoke(tile);
        return list.Count - 1;
    }
    public static void Release(TileInteraction tile, AudioCube cube)
    {
        if (tile == null || cube == null) return;
        if (occupancy.TryGetValue(tile, out var list) && list.Remove(cube)) OnOccupancyChanged?.Invoke(tile);
    }
    /// <summary>v7: takes <paramref name="cube"/> off every stack (a cube whose layer became non-zero).</summary>
    static void ReleaseEverywhere(AudioCube cube)
    {
        foreach (var kv in occupancy) if (kv.Value.Remove(cube)) OnOccupancyChanged?.Invoke(kv.Key);
    }
    /// <summary>The stack index of <paramref name="cube"/> on <paramref name="tile"/> (−1 when not on it). v7: layer cubes are not counted (−1 for one).</summary>
    public static int IndexOn(TileInteraction tile, AudioCube cube)
    {
        if (tile == null || Layered(cube) || !occupancy.TryGetValue(tile, out var list)) return -1;
        int n = 0;
        foreach (var c in list) { if (c == cube) return n; if (c != null && !c.IsLayered) n++; }
        return -1;
    }
    /// <summary>Cubes on <paramref name="tile"/>'s stack but <paramref name="excluding"/>. v7: layer cubes never count.</summary>
    public static int CountOn(TileInteraction tile, AudioCube excluding = null)
    {
        if (tile == null || !occupancy.TryGetValue(tile, out var list)) return 0;
        int n = 0; foreach (var c in list) if (c != null && c != excluding && !c.IsLayered) n++;
        return n;
    }
    public static void ClearOccupancy() { occupancy.Clear(); }

    /// <summary>SPEC v4 R2: height of the first <paramref name="slots"/> cubes stacked on <paramref name="tile"/> (<paramref name="excluding"/>
    /// skipped), each at its visual size (AudioCube.SizeFactor x CubeSize) plus the gap: where the next cube's floor sits above the tile.</summary>
    public static float StackHeight(TileInteraction tile, int slots, AudioCube excluding = null)
    {
        if (tile == null || slots <= 0 || !occupancy.TryGetValue(tile, out var list)) return Mathf.Max(0, slots) * (ProjectConfig.CubeSize + ProjectConfig.CubeGap);
        float h = 0f; int n = 0;
        for (int i = 0; i < list.Count && n < slots; i++)
        {
            var c = list[i];
            if (c == null || c == excluding || c.IsLayered) continue;   // v7: a layer cube is not in the stack
            h += c.SizeFactor * ProjectConfig.CubeSize + ProjectConfig.CubeGap;
            n++;
        }
        return h + (slots - n) * (ProjectConfig.CubeSize + ProjectConfig.CubeGap);   // slots beyond the listed cubes: v3 cube heights
    }
}

public static class InputUtil
{
    public static bool TypingInField
    {
        get
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var go = es.currentSelectedGameObject;
            if (!go.activeInHierarchy) return false;   // v9: a field hidden with its panel (the prompt, a rename) still "selected" must not eat Esc / Backspace
            var tf = go.GetComponent<TMPro.TMP_InputField>();
            if (tf != null) return tf.isFocused || tf.interactable;
            return go.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
    // KeyShim.Held = the real key, or a test's simulated one (tests drive Cmd+Z mid-draw the way a player does)
    public static bool Cmd => KeyShim.Held(KeyCode.LeftControl) || KeyShim.Held(KeyCode.RightControl) || KeyShim.Held(KeyCode.LeftCommand) || KeyShim.Held(KeyCode.RightCommand);
    public static bool Shift => KeyShim.Held(KeyCode.LeftShift) || KeyShim.Held(KeyCode.RightShift);
    public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    public static bool Held(KeyCode k) => Input.GetKey(k);
}
