using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Who owns the pointer and the keyboard (SPEC v3 §1). The menu, the presentation and the vibe prompt lock the world AND the
/// hotkeys; the cube inspector locks world picking and the camera but leaves the cube hotkeys live. Every world input reader
/// (PathManager, IslandDrag, OrbitCamera, UIManager hotkeys, SequenceMaster) checks these flags. Locks are keyed by owner so
/// two owners never release each other's lock.
/// </summary>
public static class WorldInput
{
    static readonly HashSet<string> world = new HashSet<string>();
    static readonly HashSet<string> keys = new HashSet<string>();

    /// <summary>No world picking, island drags or camera input (smoothing/inertia keep running).</summary>
    public static bool WorldLocked => world.Count > 0;
    /// <summary>No world or HUD hotkeys (the lock owner reads its own keys).</summary>
    public static bool KeysLocked => keys.Count > 0;

    public static void Lock(string who, bool alsoKeys = false)
    {
        if (string.IsNullOrEmpty(who)) return;
        world.Add(who);
        if (alsoKeys) keys.Add(who);
    }
    public static void Unlock(string who)
    {
        if (string.IsNullOrEmpty(who)) return;
        world.Remove(who); keys.Remove(who);
    }
    public static bool IsLockedBy(string who) => who != null && world.Contains(who);
    public static void Reset() { world.Clear(); keys.Clear(); }
    public static string Describe() => "world[" + string.Join(",", world) + "] keys[" + string.Join(",", keys) + "]";

    // play mode may start without a domain reload: never inherit a lock from the previous session
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnLoad() { Reset(); }
}

/// <summary>Named layers of the v3 stages: the menu wall and the presentation cascade (TagManager "Menu" / "Present"; fallback 31 / 30).</summary>
public static class DeaLayers
{
    static int present = -2, menu = -2;
    public static int Present { get { if (present == -2) { present = LayerMask.NameToLayer("Present"); if (present < 0) present = 30; } return present; } }
    public static int Menu { get { if (menu == -2) { menu = LayerMask.NameToLayer("Menu"); if (menu < 0) menu = 31; } return menu; } }
    public static void SetLayerRecursive(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }
}
