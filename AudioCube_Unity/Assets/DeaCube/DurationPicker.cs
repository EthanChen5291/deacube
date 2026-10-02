using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SPEC v4 §2.5 / §6, ui_research §4.4: the note-length row — five cubes in the instrument colour standing on an inked shelf, each the size the
/// world's cube takes for that length (px = 30 · SizeOf^1.5: fast = tiny … long = fat). The selected one stands on a cream pedestal inside the
/// ink hexagon (the others drop to 70 % saturation); clicking it again makes it dotted: the satellite dot that orbits dotted cubes in the world
/// orbits it on twos. No notes, no fractions, no numbers — size, the satellite and sound. Hovering a cube hops it and auditions that length on
/// the latest note (drafting) or the selected note (inspecting).
/// Drafting (automatic while PathManager.Draft exists): the row floats in a paper bubble above the draft island's far edge (re-projected every
/// frame, its bottom above every tile of the island, the tail touching that edge; below the island when there is no room above); a click
/// sets PathManager.SetDraftDuration (the latest node + the brush; the [ ] . keys move the selection too); "[ ] ." keycaps show under the row
/// for the first two drafts. Inspecting (Show(cube, node) from the card): it docks under the rhythm strip for the selected note, with a hollow
/// cube for rest on / off; a click sets that note's length through RhythmStrip.ApplyLength (bake a legacy cube, SetDuration, one History entry).
/// v6 (H): drafting, a DONE tick sits at the row's end (where the inspector's rest cube goes): it finishes the draft — on a keyboard a click on the
/// last key adds a repeated note instead of finishing, so the tick (or Enter, a right-click, a click off the island) ends the melody.
/// v7 (D, SPEC v7 §15 "make them select the size of the cube THEN place it down"): drafting, the row is the SIZE OF THE NEXT NOTE — a pick sets
/// PathManager.SetBrush (the hologram at the cursor takes it; the next click places a note that big) and auditions it; a note already placed never
/// changes (select its bead: the inspector's row). It shows the brush, so it always agrees with U2's size row.
/// v7 §19 ("whenever the user is actively creating a cube path, it shouldnt be looping over and over unless they press a key or press ui" / "if
/// max is hit then it says capacity reached in which they'll have to expand the grid"): the row also carries HEAR IT (a small ▶⟲ at its left end:
/// PathManager.ToggleDrawLoop — the loop over the draft's grid in context, on / off; Space does the same), a tab above its left corner with how full
/// the grid is ("3 / 4 beats"; "capacity reached" when a note was refused) and, then, an EXPAND chip above its right corner (+ one measure:
/// PathManager.ExpandDrawGrid). While a melody PHRASE is drawn (a cube in the hand) the row floats over the phrase the same way (<see cref="Where.Phrase"/>:
/// sizes, hear it, fill, expand; no done tick — each stroke is already in the song).
/// </summary>
[DefaultExecutionOrder(-50)]   // PointerOver is fresh before PathManager reads it (a press on the row must not finish the draft)
public class DurationPicker : MonoBehaviour
{
    public enum Where { Hidden, Draft, Inspector, Phrase }   // v7: Phrase = a melody phrase being drawn
    /// <summary>Onboarding event of a length pick (drafting or inspecting).</summary>
    public const string EvLengthPicked = "length.picked";
    /// <summary>PlayerPrefs: drafts that showed the [ ] . keycaps (they show for the first two).</summary>
    public const string PrefKeysShown = "deacube.lengthKeys";
    public const float Gap = 10f, RowW = 300f, RowH = 78f, ShelfY = -22f, KeysH = 30f, AboveIslandPx = 14f;
    /// <summary>Screen bands kept free when there is a choice (the column rail on top, the transport / deck at the bottom; 1080p units).</summary>
    public const float TopBand = 84f, BottomBand = 120f;

    public static DurationPicker I;
    public static bool IsShown { get; private set; }
    /// <summary>The length the row shows as selected (ticks; 24 = one beat).</summary>
    public static int SelectedTicks { get; private set; } = 24;
    /// <summary>The pointer is over the row (PathManager must not treat a press on it as "a press on the HUD finishes the draft").</summary>
    public static bool PointerOver => I != null && I.pointerOver;
    public static Where Mode => I != null ? I.mode : Where.Hidden;
    /// <summary>The cube (draft or inspected) and node the row edits.</summary>
    public static AudioCube Target => I != null ? I.cube : null;
    public static int TargetNode => I != null ? I.node : -1;
    public static bool KeycapsShown => I != null && I.keys != null && I.keys.gameObject.activeInHierarchy;
    /// <summary>Picks applied (tests).</summary>
    public static int PickCount { get; private set; }
    /// <summary>v7: the fill tab's text now ("3 / 4 beats", "capacity reached"; empty when hidden) and whether the EXPAND chip / the hear-it button show.</summary>
    public static string FillText => I != null && I.fillText != null && I.fillTab != null && I.fillTab.gameObject.activeInHierarchy ? I.fillText.text : "";
    public static bool ExpandShown => I != null && I.expandBtn != null && I.expandBtn.gameObject.activeInHierarchy;
    public static bool LoopShown => I != null && I.loopBtn != null && I.loopBtn.gameObject.activeInHierarchy;
    /// <summary>v7 tests: a click on the hear-it button / the EXPAND chip.</summary>
    public static void SimClickLoop() { if (I != null && LoopShown) I.loopBtn.Click(); }
    public static void SimClickExpand() { if (I != null && ExpandShown) I.expandBtn.Click(); }
    /// <summary>v7: the phrase the row floats over in <see cref="Where.Phrase"/> mode.</summary>
    public static KeyBlock PhraseIsland => I != null && I.mode == Where.Phrase ? I.phraseKb : null;
    /// <summary>The row's rect on screen (px) and the island edge it is anchored to (tests).</summary>
    public static Rect RowScreenRect => I != null ? I.ScreenRectOf(I.row) : new Rect();
    public static float AnchorScreenY => I != null ? I.anchorY : float.NaN;
    public static bool PlacedBelow => I != null && I.below;

    /// <summary>The cube size the row draws for a length (px at the 1080p reference).</summary>
    public static float SizePx(float beats) => 30f * Mathf.Pow(AudioCube.SizeOf(beats), 1.5f);
    public static float SlotPx(int size) => SizePx(AudioCube.BaseTicks[Mathf.Clamp(size, 0, 4)] / (float)ProjectConfig.TicksPerBeat);

    public static void Show(AudioCube cube, int node) { Ensure().DoShow(cube, node); }
    public static void Hide() { if (I != null) I.DoHide(); else IsShown = false; }

    public static DurationPicker Ensure()
    {
        if (I == null)
        {
            I = FindAnyObjectByType<DurationPicker>();
            if (I == null) I = new GameObject("DurationPicker").AddComponent<DurationPicker>();
            I.Build();
        }
        return I;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { I = null; IsShown = false; SelectedTicks = 24; PickCount = 0; }

    // ------------------------------------------------------------------ parts
    RectTransform row, keys, restRt;
    CanvasGroup group;
    InkShape bubble;
    InkPath shelf;
    readonly InkButton[] slots = new InkButton[5];
    readonly CubeGlyph[] cubes = new CubeGlyph[5];
    readonly InkShape[] pedestals = new InkShape[5];
    readonly float[] slotX = new float[5];
    InkShape sat;
    InkButton restBtn; CubeGlyph restCube;
    InkButton doneBtn;   // v6: drafting, finishes the draft
    InkButton loopBtn, expandBtn;   // v7 §19: hear it (the loop on / off), + one measure
    RectTransform fillTab; InkShape fillPaper; TextMeshProUGUI fillText; string fillShown;   // v7 §19.1: how full the grid is
    KeyBlock phraseKb;   // v7: the phrase being drawn (Phrase mode)
    bool built;

    // ------------------------------------------------------------------ state
    Where mode = Where.Hidden;
    AudioCube cube; int node = -1, shownTicks = int.MinValue, selSize = 2; bool selDotted;
    bool pointerOver, below; float popT, anchorY = float.NaN;
    AudioCube lastDraft;
    Vector2 lastTail = new Vector2(float.NaN, float.NaN);
    static readonly Vector3[] corners = new Vector3[4];

    void Awake() { if (I == null) I = this; }
    void OnDestroy() { if (I == this) I = null; }

    void Build()
    {
        if (built) return;
        built = true;
        row = InkUI.Node("LengthRow", InkUI.OverlayRoot);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0f);
        row.sizeDelta = new Vector2(RowW, RowH);
        group = row.gameObject.AddComponent<CanvasGroup>();
        bubble = InkShape.Create(row, "Bubble", InkShape.Kind.Bubble, InkUI.Paper, new Vector2(RowW, RowH));
        InkUI.Stretch(bubble.rectTransform);
        bubble.raycastTarget = true; bubble.Radius = 20f; bubble.SetInk(2.2f, 3.6f); bubble.Wobble = 1.2f;
        bubble.ShadowOffset = new Vector2(5f, -6f); bubble.ShadowColor = Comic.PrintShadow; bubble.TailWidth = 26f; bubble.Seed = 8801;
        shelf = InkPath.Create(row, "Shelf", new Vector2(RowW, 20f));
        shelf.UnitPx = 1f; shelf.Wobble = 0.8f; shelf.Seed = 8803;
        // five cubes, bottoms on the shelf (16th … whole)
        float total = 0f;
        for (int i = 0; i < 5; i++) total += SlotPx(i);
        total += 4f * Gap;
        float x = -total * 0.5f;
        string[] words = { "very short", "short", "medium", "long", "very long" };
        for (int i = 0; i < 5; i++)
        {
            int ii = i;
            float px = SlotPx(i);
            slotX[i] = x + px * 0.5f;
            x += px + Gap;
            var b = InkButton.Create(row, "Len" + i, new Vector2(Mathf.Max(30f, px + 8f), 70f), words[i]);
            InkUI.Centre((RectTransform)b.transform, new Vector2(slotX[i], 0f), new Vector2(Mathf.Max(30f, px + 8f), 70f));
            b.hoverScale = 1.06f;
            var ped = InkShape.Create(b.body, "Pedestal", InkShape.Kind.Circle, InkUI.Paper, new Vector2(px * 1.3f + 10f, px * 0.36f + 6f));
            ped.raycastTarget = false; ped.SetInk(1.6f, 2.4f); ped.ShadowOffset = new Vector2(0f, -3f); ped.ShadowColor = InkUI.PaperDeep; ped.Wobble = 0.6f;
            ((RectTransform)ped.transform).anchoredPosition = new Vector2(0f, ShelfY);
            var g = CubeGlyph.Create(b.body, "Cube", Color.white, px);
            g.raycastTarget = false; g.GroundShadow = true;
            ((RectTransform)g.transform).anchoredPosition = new Vector2(0f, ShelfY + px * 0.5f + 2f);
            b.onClick = () => Pick(ii);
            b.onHover = on => { if (on) HoverSlot(ii); };
            slots[i] = b; cubes[i] = g; pedestals[i] = ped;
        }
        shelf.Line(InkUI.Ink, 2.2f, new Vector2(-total * 0.5f - 14f, ShelfY + 1f), new Vector2(total * 0.5f + 14f, ShelfY + 1f));
        sat = InkShape.Create(row, "Satellite", InkShape.Kind.Circle, Color.white, new Vector2(9f, 9f));
        sat.raycastTarget = false; sat.HasShadow = false; sat.SetInk(1.4f, 1.8f); sat.Wobble = 0.2f;
        sat.gameObject.SetActive(false);
        // inspecting: a hollow cube after a gap = rest on / off
        restBtn = InkButton.Create(row, "Rest", new Vector2(36f, 70f), "rest");
        restRt = (RectTransform)restBtn.transform;
        InkUI.Centre(restRt, new Vector2(total * 0.5f + 34f, 0f), new Vector2(36f, 70f));
        restCube = CubeGlyph.Create(restBtn.body, "Cube", Color.white, 26f);
        restCube.raycastTarget = false; restCube.Hollow = true;
        ((RectTransform)restCube.transform).anchoredPosition = new Vector2(0f, ShelfY + 15f);
        restBtn.onClick = () => { if (mode == Where.Inspector && cube != null) { RhythmStrip.ApplyRest(cube, node); PickCount++; shownTicks = int.MinValue; } };
        // v6 drafting: the done tick (a cream disc with an inked check) finishes the draft
        doneBtn = InkButton.Create(row, "Done", new Vector2(36f, 70f), "done");
        InkUI.Centre((RectTransform)doneBtn.transform, new Vector2(total * 0.5f + 34f, 0f), new Vector2(36f, 70f));
        var disc = InkShape.Create(doneBtn.body, "Disc", InkShape.Kind.Circle, InkUI.Paper, new Vector2(28f, 28f));
        disc.raycastTarget = false; disc.SetInk(1.6f, 2.4f); disc.ShadowOffset = new Vector2(0f, -3f); disc.ShadowColor = InkUI.PaperDeep; disc.Wobble = 0.6f;
        ((RectTransform)disc.transform).anchoredPosition = new Vector2(0f, ShelfY + 16f);
        var tick = doneBtn.AddDrawing(new Vector2(28f, 28f));
        tick.UnitPx = 1f; tick.Wobble = 0.5f; tick.Seed = 8811;
        tick.Line(InkUI.Ink, 3.4f, new Vector2(-7f, 0f), new Vector2(-2f, -5.5f), new Vector2(7.5f, 6f));
        ((RectTransform)tick.transform).anchoredPosition = new Vector2(0f, ShelfY + 16f);
        doneBtn.onClick = () => { var pm = PathManager.I; if (mode == Where.Draft && pm != null && pm.IsDrawing) { pm.FinishPath(); DoneCount++; } };
        doneBtn.gameObject.SetActive(false);
        BuildDrawParts(total);
        BuildKeys();
        Hints.Register("draft.lengths", row);
        row.gameObject.SetActive(false);
    }

    /// <summary>v7 §19: hear it (a cream disc with an inked play triangle inside a looping arrow) at the row's left end; the fill tab over its top-left
    /// corner; the EXPAND chip (a measure with a plus) over its top-right corner.</summary>
    void BuildDrawParts(float total)
    {
        loopBtn = InkButton.Create(row, "HearIt", new Vector2(36f, 70f), "hear it");
        InkUI.Centre((RectTransform)loopBtn.transform, new Vector2(-total * 0.5f - 34f, 0f), new Vector2(36f, 70f));
        var ld = InkShape.Create(loopBtn.body, "Disc", InkShape.Kind.Circle, InkUI.Paper, new Vector2(30f, 30f));
        ld.raycastTarget = false; ld.SetInk(1.6f, 2.4f); ld.ShadowOffset = new Vector2(0f, -3f); ld.ShadowColor = InkUI.PaperDeep; ld.Wobble = 0.6f;
        ((RectTransform)ld.transform).anchoredPosition = new Vector2(0f, ShelfY + 16f);
        var lg = loopBtn.AddDrawing(new Vector2(30f, 30f));
        lg.UnitPx = 1f; lg.Wobble = 0.4f; lg.Seed = 8831;
        var arc = new List<Vector2>();
        for (int k = 0; k <= 10; k++) { float a = Mathf.Deg2Rad * (40f + 280f * k / 10f); arc.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10.5f); }
        lg.Line(InkUI.Ink, 2.2f, arc.ToArray());
        Vector2 tip = arc[arc.Count - 1];
        lg.Line(InkUI.Ink, 2.2f, tip + new Vector2(-4.5f, 1.5f), tip, tip + new Vector2(0.5f, 4.8f));
        lg.Line(InkUI.Ink, 2.6f, new Vector2(-3f, -4.5f), new Vector2(4.5f, 0f), new Vector2(-3f, 4.5f), new Vector2(-3f, -4.5f));
        ((RectTransform)lg.transform).anchoredPosition = new Vector2(0f, ShelfY + 16f);
        loopBtn.onClick = () => { var pm = PathManager.I; if (pm != null && (mode == Where.Draft || mode == Where.Phrase)) pm.ToggleDrawLoop(); };
        loopBtn.gameObject.SetActive(false);
        Hints.Register("draft.hear", (RectTransform)loopBtn.transform);

        fillTab = InkUI.Node("Fill", row);
        fillTab.anchorMin = fillTab.anchorMax = new Vector2(0f, 1f);
        fillTab.pivot = new Vector2(0f, 0f);
        fillTab.sizeDelta = new Vector2(118f, 22f);
        fillTab.anchoredPosition = new Vector2(14f, -2f);
        fillPaper = InkShape.Create(fillTab, "Paper", InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(118f, 22f));
        InkUI.Stretch(fillPaper.rectTransform);
        fillPaper.raycastTarget = false; fillPaper.Radius = 8f; fillPaper.SetInk(1.6f, 2.2f); fillPaper.Wobble = 0.6f; fillPaper.ShadowOffset = new Vector2(2f, -3f); fillPaper.ShadowColor = Comic.PrintShadow;
        fillText = UIKit.Text(fillTab, "Text", "", 14f, InkUI.Ink);
        fillText.font = Comic.Font; fillText.raycastTarget = false; fillText.alignment = TextAlignmentOptions.Center;
        InkUI.Stretch(fillText.rectTransform);
        fillTab.gameObject.SetActive(false);

        expandBtn = InkButton.Create(row, "Expand", new Vector2(64f, 26f), "expand");
        var ert = (RectTransform)expandBtn.transform;
        ert.anchorMin = ert.anchorMax = new Vector2(1f, 1f); ert.pivot = new Vector2(1f, 0f);
        ert.sizeDelta = new Vector2(64f, 26f); ert.anchoredPosition = new Vector2(-14f, -2f);
        var chip = InkShape.Create(expandBtn.body, "Chip", InkShape.Kind.RoundRect, Palette.Accent, new Vector2(62f, 24f));
        chip.raycastTarget = false; chip.Radius = 9f; chip.SetInk(1.8f, 2.4f); chip.Wobble = 0.6f; chip.ShadowOffset = new Vector2(2f, -3f); chip.ShadowColor = Comic.PrintShadow;
        var eg = expandBtn.AddDrawing(new Vector2(62f, 24f));
        eg.UnitPx = 1f; eg.Wobble = 0.4f; eg.Seed = 8833;
        // a measure (a small bar with four beat ticks) and a plus: "one more measure"
        eg.Line(InkUI.Ink, 2f, new Vector2(-24f, -6f), new Vector2(2f, -6f), new Vector2(2f, 6f), new Vector2(-24f, 6f), new Vector2(-24f, -6f));
        for (int k = 1; k < 4; k++) eg.Line(InkUI.Ink, 1.4f, new Vector2(-24f + 6.5f * k, -6f), new Vector2(-24f + 6.5f * k, -2f));
        eg.Line(InkUI.Ink, 2.6f, new Vector2(10f, 0f), new Vector2(22f, 0f));
        eg.Line(InkUI.Ink, 2.6f, new Vector2(16f, -6f), new Vector2(16f, 6f));
        expandBtn.onClick = () => { var pm = PathManager.I; if (pm != null && pm.ExpandDrawGrid()) shownTicks = int.MinValue; };
        expandBtn.gameObject.SetActive(false);
        Hints.Register("draft.expand", ert);
    }

    /// <summary>v7 §19: the fill tab's text, the EXPAND chip and the hear-it button's state (text rebuilt only on a change).</summary>
    void RefreshDrawParts()
    {
        var pm = PathManager.I;
        bool drawing = pm != null && (mode == Where.Draft || mode == Where.Phrase);
        if (loopBtn.gameObject.activeSelf != drawing) loopBtn.gameObject.SetActive(drawing);
        if (drawing) loopBtn.Active = pm.DrawLoopOn;
        string t = "";
        bool full = drawing && pm.CapacityReached;
        float cap = drawing ? pm.DrawCapacityBeats : 0f;
        if (full) t = "capacity reached";
        else if (cap > 0f) t = Beats(pm.DrawUsedBeats) + " / " + Beats(cap) + " beats";
        bool tab = t.Length > 0;
        if (fillTab.gameObject.activeSelf != tab) fillTab.gameObject.SetActive(tab);
        if (tab && t != fillShown)
        {
            fillShown = t; fillText.text = t;
            fillText.color = full ? Palette.Danger : InkUI.Ink;
            fillPaper.color = full ? Color.Lerp(InkUI.Paper, Palette.Danger, 0.18f) : InkUI.Paper;
        }
        if (expandBtn.gameObject.activeSelf != full) expandBtn.gameObject.SetActive(full);
    }

    static string Beats(float b)
    {
        float r = Mathf.Round(b * 2f) / 2f;
        return Mathf.Abs(r - Mathf.Round(r)) < 1e-3f ? Mathf.RoundToInt(r).ToString() : r.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The [ ] . keycaps (a lip for thickness, lowercase Fredoka) with tiny pictures: shorter, longer, dotted.</summary>
    void BuildKeys()
    {
        keys = InkUI.Node("Keys", row);
        InkUI.Centre(keys, new Vector2(0f, ShelfY - 22f), new Vector2(190f, KeysH));
        string[] caps = { "[", "]", "." };
        for (int i = 0; i < 3; i++)
        {
            float cx = -62f + i * 62f;
            var cap = InkShape.Create(keys, "Key" + i, InkShape.Kind.RoundRect, InkUI.Paper, new Vector2(22f, 22f));
            cap.raycastTarget = false; cap.Radius = 5f; cap.SetInk(1.4f, 2f); cap.ShadowOffset = new Vector2(0f, -3f); cap.ShadowColor = InkUI.PaperDeep; cap.Wobble = 0.5f;
            InkUI.Centre(cap.rectTransform, new Vector2(cx - 10f, 0f), new Vector2(22f, 22f));
            var t = UIKit.Text(cap.transform, "Glyph", caps[i], 17f, InkUI.Ink);
            t.font = Comic.Font; t.raycastTarget = false;
            InkUI.Stretch(t.rectTransform);
            float px = i == 0 ? 9f : 16f;
            var g = CubeGlyph.Create(keys, "Pic" + i, Palette.A(InkUI.Ink, 0.85f), px);
            g.raycastTarget = false; g.Satellite = false;
            ((RectTransform)g.transform).anchoredPosition = new Vector2(cx + 12f, 0f);
            if (i == 2)
            {
                var d = InkShape.Create(keys, "Dot", InkShape.Kind.Circle, InkUI.Paper, new Vector2(6f, 6f));
                d.raycastTarget = false; d.HasShadow = false; d.SetInk(1.2f, 1.4f);
                InkUI.Centre(d.rectTransform, new Vector2(cx + 22f, 6f), new Vector2(6f, 6f));
            }
        }
        keys.gameObject.SetActive(false);
    }

    // ================================================================== show / hide
    void DoShow(AudioCube c, int n)
    {
        if (c == null) { DoHide(); return; }
        var pm = PathManager.I;
        if (pm != null && pm.Draft == c) { EnterDraft(c); return; }
        cube = c; node = n;
        mode = Where.Inspector;
        var slot = InspectorCard.LengthSlot;
        row.SetParent(slot != null ? slot : InkUI.OverlayRoot, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0.5f);
        row.anchoredPosition = new Vector2(-26f, -2f);   // the five cubes + the rest cube, centred
        row.localRotation = Quaternion.identity;
        bubble.gameObject.SetActive(false);
        restBtn.gameObject.SetActive(true);
        doneBtn.gameObject.SetActive(false);
        keys.gameObject.SetActive(false);
        SizeRow();
        row.sizeDelta = new Vector2(RowW, RowH);
        group.alpha = 1f; group.blocksRaycasts = true;
        Open();
    }

    void EnterDraft(AudioCube d)
    {
        bool fresh = d != lastDraft;
        lastDraft = d;
        cube = d; node = d.nodes.Count - 1;
        mode = Where.Draft;
        row.SetParent(InkUI.OverlayRoot, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0f);
        row.localRotation = Quaternion.identity;
        bubble.gameObject.SetActive(true);
        restBtn.gameObject.SetActive(false);
        doneBtn.gameObject.SetActive(true);
        if (fresh)
        {
            int seen = PlayerPrefs.GetInt(PrefKeysShown, 0);
            keys.gameObject.SetActive(seen < 2);
            if (seen < 2) PlayerPrefs.SetInt(PrefKeysShown, seen + 1);
        }
        lastTail = new Vector2(float.NaN, float.NaN);
        SizeRow();
        Open();
        Place();
    }

    /// <summary>v7: a melody phrase is being drawn — the row floats over it (sizes, hear it, fill, expand; no done tick).</summary>
    void EnterPhrase(KeyBlock kb)
    {
        var pm = PathManager.I;
        phraseKb = kb;
        cube = pm != null ? PathManager.PhraseCubeOf(kb, pm.selectedInstrument) : null; node = -1;
        mode = Where.Phrase;
        row.SetParent(InkUI.OverlayRoot, false);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
        row.pivot = new Vector2(0.5f, 0f);
        row.localRotation = Quaternion.identity;
        bubble.gameObject.SetActive(true);
        restBtn.gameObject.SetActive(false);
        doneBtn.gameObject.SetActive(false);
        keys.gameObject.SetActive(false);
        lastTail = new Vector2(float.NaN, float.NaN);
        SizeRow();
        Open();
        Place();
    }

    void SizeRow()
    {
        bool withKeys = keys.gameObject.activeSelf;
        row.sizeDelta = new Vector2(RowW, RowH + (withKeys ? KeysH : 0f));
        // content sits in the upper RowH; the keycaps under it
        float lift = withKeys ? KeysH * 0.5f : 0f;
        foreach (var b in slots) { var r = (RectTransform)b.transform; r.anchoredPosition = new Vector2(r.anchoredPosition.x, lift); }
        restRt.anchoredPosition = new Vector2(restRt.anchoredPosition.x, lift);
        var doneRt = (RectTransform)doneBtn.transform; doneRt.anchoredPosition = new Vector2(doneRt.anchoredPosition.x, lift);
        shelf.rectTransform.anchoredPosition = new Vector2(0f, lift);
        keys.anchoredPosition = new Vector2(0f, -RowH * 0.5f + 4f);   // the keycaps fill the bottom KeysH of the bubble
    }

    void Open()
    {
        bool was = IsShown;
        IsShown = true;
        if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
        if (!was) popT = 0f;
        shownTicks = int.MinValue;
        Refresh();
    }

    void DoHide()
    {
        if (mode == Where.Hidden && !IsShown) return;
        mode = Where.Hidden; IsShown = false; cube = null; node = -1; pointerOver = false; phraseKb = null;
        if (row != null && row.gameObject.activeSelf) row.gameObject.SetActive(false);
        if (row != null && row.parent != InkUI.OverlayRoot) row.SetParent(InkUI.OverlayRoot, false);   // never left inside a card that may go away
    }

    // ================================================================== per frame
    void Update()
    {
        if (!built) return;
        var pm = PathManager.I;
        var d = pm != null ? pm.Draft : null;
        bool free = !WorldInput.WorldLocked && !Presenter.Active && !MainMenu.IsShown;
        bool draftOk = d != null && d.nodes.Count > 0 && free;
        var ph = !draftOk && free && pm != null && pm.Hand == PathManager.HandKind.Cube ? pm.DrawPhrase : null;   // v7: a phrase being drawn
        if (draftOk && (mode != Where.Draft || cube != d)) EnterDraft(d);
        else if (ph != null && (mode != Where.Phrase || phraseKb != ph)) EnterPhrase(ph);
        else if (mode == Where.Draft && !draftOk) { DoHide(); if (d == null) lastDraft = null; }
        else if (mode == Where.Phrase && ph == null) DoHide();
        if (mode == Where.Phrase && pm != null && (cube == null || !cube)) cube = PathManager.PhraseCubeOf(phraseKb, pm.selectedInstrument);
        if (mode == Where.Inspector && (!CubeInspector.IsOpen || CubeInspector.Current != cube || cube == null || node >= cube.nodes.Count)) DoHide();
        if (mode == Where.Hidden) { pointerOver = false; return; }
        if (mode == Where.Draft) node = cube.nodes.Count - 1;
        int ticks = CurrentTicks();
        if (ticks != shownTicks) Refresh();
        RefreshDrawParts();
        pointerOver = group.alpha > 0.5f && RectTransformUtility.RectangleContainsScreenPoint(row, Input.mousePosition, null);
    }

    void LateUpdate()
    {
        InkUI.TickHops();
        if (mode == Where.Hidden) return;
        float dt = Time.unscaledDeltaTime;
        if (mode == Where.Draft || mode == Where.Phrase) Place();
        // pop in on twos: 0.6 → 1.08 → 1
        popT += dt;
        float s = popT < 1f / 12f ? 0.6f : (popT < 2f / 12f ? 1.08f : 1f);
        if (row.localScale.x != s) row.localScale = new Vector3(s, s, 1f);
        // the dotted satellite orbits the selected cube on twos
        if (sat.gameObject.activeSelf)
        {
            float a = Mathf.Floor(Time.unscaledTime * Look.TwosFps) / Look.TwosFps * Mathf.PI * 1.2f;
            float px = SlotPx(selSize), r = px * 0.5f;
            var brt = (RectTransform)slots[selSize].transform;
            Vector2 c = brt.anchoredPosition + new Vector2(0f, ShelfY + px * 0.5f + 2f);
            sat.rectTransform.anchoredPosition = c + new Vector2(Mathf.Cos(a) * r * 1.35f, Mathf.Sin(a) * r * 0.55f + r * 0.3f);
        }
    }

    int CurrentTicks()
    {
        if (mode == Where.Draft || mode == Where.Phrase) return PathManager.I != null ? PathManager.I.BrushTicks : 24;
        if (cube == null || node < 0 || node >= cube.nodes.Count) return 24;
        if (cube.HasDurations && node < cube.durs.Count) return cube.durs[node];
        var s = RhythmStrip.Active;
        if (s != null && s.Cube == cube && s.SelectedEvent >= 0) return s.TicksOf(s.SelectedEvent);
        return RhythmStrip.Snap(cube.StepBeats * ProjectConfig.TicksPerBeat);
    }

    /// <summary>Selection look: the pedestal + hexagon under the selected size, the satellite when dotted, the others at 70 % saturation.</summary>
    void Refresh()
    {
        int ticks = CurrentTicks();
        shownTicks = ticks;
        int sz; bool dot;
        if (!AudioCube.TryDecode(ticks, out sz, out dot)) { AudioCube.TryDecode(RhythmStrip.Snap(ticks), out sz, out dot); }
        selSize = Mathf.Clamp(sz, 0, 4); selDotted = dot;
        SelectedTicks = ticks;
        var pmc = PathManager.I;
        Color col = cube != null ? Comic.Opaque(cube.Color) : (mode == Where.Phrase && pmc != null ? Comic.Opaque(Instruments.Colors[Mathf.Clamp(pmc.selectedInstrument, 0, Instruments.Count - 1)]) : Palette.Accent);
        for (int i = 0; i < 5; i++)
        {
            bool on = i == selSize;
            cubes[i].color = on ? col : InkUI.Desaturate(col, 0.7f);
            cubes[i].Ring = on;
            pedestals[i].gameObject.SetActive(on);
            slots[i].Active = on;
        }
        sat.gameObject.SetActive(selDotted);
        if (selDotted) sat.color = Color.Lerp(col, Color.white, 0.25f);
        bool rest = mode == Where.Inspector && cube != null && cube.ModOf(node) == 1;
        restCube.color = col; restCube.Ring = rest;
    }

    // ================================================================== picks
    int TicksFor(int size, bool click)
    {
        bool dotted = false;
        if (click && size == selSize) dotted = !selDotted;       // the selected size again: dotted on / off
        else if (!click && size == selSize) dotted = selDotted;  // hovering the selected size: its current length
        return AudioCube.TicksOf(size, dotted);
    }

    void HoverSlot(int size)
    {
        if (mode == Where.Phrase) { InkUI.Hop(cubes[size], 0.3f); if (PathManager.I != null) PathManager.I.AuditionTicks(TicksFor(size, false)); return; }   // v7
        if (cube == null) return;
        InkUI.Hop(cubes[size], 0.3f);
        int ticks = TicksFor(size, false);
        InkUI.AuditionNote(cube, Mathf.Clamp(node, 0, cube.nodes.Count - 1), ticks / (float)ProjectConfig.TicksPerBeat, true);
    }

    void Pick(int size)
    {
        if (mode == Where.Phrase)
        {
            // v7: drawing on a phrase, the row sets the brush (the next note) — a cube there or not yet
            var pmp = PathManager.I;
            if (pmp == null) return;
            int tp = TicksFor(size, true);
            pmp.SetBrush(tp, true);
            Onboarding.Notify(EvLengthPicked);
            PickCount++;
            InkUI.Hop(cubes[size], 0.45f);
            shownTicks = int.MinValue;
            SelectedTicks = tp; selSize = size;
            return;
        }
        if (cube == null) return;
        int ticks = TicksFor(size, true);
        if (mode == Where.Draft)
        {
            var pm = PathManager.I;
            if (pm == null || pm.Draft == null) return;
            pm.SetBrush(ticks);   // v7 (§15): the NEXT note's size (v4 also resized the latest note)
            InkUI.AuditionNote(pm.Draft, pm.Draft.nodes.Count - 1, ticks / (float)ProjectConfig.TicksPerBeat);
            Onboarding.Notify(EvLengthPicked);
        }
        else if (mode == Where.Inspector) RhythmStrip.ApplyLength(cube, node, ticks);   // bake (legacy) + SetDuration + one History entry + audition
        else return;
        PickCount++;
        InkUI.Hop(cubes[size], 0.45f);
        shownTicks = int.MinValue;
        if (mode == Where.Draft || !cube.HasDurations) { SelectedTicks = ticks; selSize = size; }
    }

    // ================================================================== world anchoring (drafting)
    /// <summary>Above the draft island's far edge: the row's bottom sits <see cref="AboveIslandPx"/> above the highest projected tile corner of
    /// the island (so it never covers a tile), centred on the island; below the island when there is no room above; hidden when the island
    /// is behind the camera.</summary>
    void Place()
    {
        var cam = Camera.main;
        var isl = mode == Where.Phrase ? phraseKb : (cube != null ? cube.Island : null);
        if (cam == null || isl == null) { group.alpha = 0f; return; }
        float top = float.NegativeInfinity, bot = float.PositiveInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float h = ProjectConfig.TileSize * 0.5f;
        bool any = false;
        if (isl.tiles.Count > 96)
        {
            // v7: a long phrase has hundreds of cells — its corner cells (the roll is a flat rectangle) stand for them all
            int cx0 = 0, cx1 = Mathf.Max(0, isl.cols - 1), cz0 = 0, cz1 = Mathf.Max(0, isl.rows - 1);
            var cs = new[] { isl.GetTile(cx0, cz0), isl.GetTile(cx1, cz0), isl.GetTile(cx0, cz1), isl.GetTile(cx1, cz1) };
            foreach (var t in cs)
            {
                if (t == null) continue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 s = cam.WorldToScreenPoint(t.Top + new Vector3((k & 1) == 0 ? -h : h, 0f, (k & 2) == 0 ? -h : h));
                    if (s.z <= 0f) continue;
                    any = true;
                    top = Mathf.Max(top, s.y); bot = Mathf.Min(bot, s.y); minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                }
            }
        }
        else foreach (var t in isl.tiles)
        {
            if (t == null) continue;
            Vector3 p = t.Top;
            for (int k = 0; k < 4; k++)
            {
                Vector3 s = cam.WorldToScreenPoint(p + new Vector3((k & 1) == 0 ? -h : h, 0f, (k & 2) == 0 ? -h : h));
                if (s.z <= 0f) continue;
                any = true;
                top = Mathf.Max(top, s.y); bot = Mathf.Min(bot, s.y); minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
            }
        }
        if (!any || maxX < 0f || minX > Screen.width || bot > Screen.height || top < 0f) { group.alpha = 0f; group.blocksRaycasts = false; return; }
        group.alpha = 1f; group.blocksRaycasts = true;
        var root = InkUI.OverlayRoot;
        float upp = InkUI.UnitsPerPixel;
        float rowH = row.sizeDelta.y;
        float cx = Mathf.Clamp((minX + maxX) * 0.5f, 0f, Screen.width);
        // above the far edge when it fits under the column rail; else under the near edge when that fits above the transport band; else above
        bool fitsAbove = top + (AboveIslandPx + rowH + 8f + TopBand) / upp <= Screen.height;
        bool fitsBelow = bot - (AboveIslandPx + rowH + 8f + BottomBand) / upp >= 0f;
        below = !fitsAbove && fitsBelow;
        anchorY = below ? bot : top;
        Vector2 edge;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(cx, anchorY), null, out edge);
        Rect rr = root.rect;
        float x = Mathf.Clamp(edge.x, rr.xMin + RowW * 0.5f + 12f, rr.xMax - RowW * 0.5f - 12f);
        float y = below ? edge.y - AboveIslandPx : edge.y + AboveIslandPx;
        y = below ? Mathf.Max(y, rr.yMin + rowH + 6f) : Mathf.Min(y, rr.yMax - rowH - 6f);   // on screen, whatever the camera does
        row.pivot = new Vector2(0.5f, below ? 1f : 0f);
        row.anchoredPosition = new Vector2(x, y);
        // the tail points at the island's edge, 4 px short of it (re-aimed only when it moved: no mesh rebuild at rest)
        Vector2 centre = new Vector2(x, below ? y - rowH * 0.5f : y + rowH * 0.5f);
        Vector2 tip = edge - centre;
        if (tip.sqrMagnitude > 64f) tip -= tip.normalized * 4f;
        if (float.IsNaN(lastTail.x) || (tip - lastTail).sqrMagnitude > 9f) { lastTail = tip; bubble.TailTip = tip; }
    }

    Rect ScreenRectOf(RectTransform r)
    {
        if (r == null || !r.gameObject.activeInHierarchy) return new Rect();
        r.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, corners[0]), b = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ================================================================== tests
    /// <summary>Tests: the pointer resting on the cube of <paramref name="size"/> (0 fast … 4 long): it hops and auditions.</summary>
    public static void SimHover(int size) { if (I != null && size >= 0 && size < 5) I.slots[size].SimHover(true); }
    /// <summary>Tests: a click on the cube of <paramref name="size"/> (the selected one again = dotted on / off).</summary>
    public static void SimClick(int size) { if (I != null && size >= 0 && size < 5) I.slots[size].Click(); }
    public static void SimClickRest() { if (I != null && I.restBtn != null) I.restBtn.Click(); }
    /// <summary>Tests: a click on the draft row's done tick (finishes the draft); drafts finished by it so far.</summary>
    public static void SimClickDone() { if (I != null && I.doneBtn != null && I.doneBtn.gameObject.activeInHierarchy) I.doneBtn.Click(); }
    public static int DoneCount { get; private set; }
    public static bool DoneShown => I != null && I.doneBtn != null && I.doneBtn.gameObject.activeInHierarchy;
    public static int SelectedSize => I != null ? I.selSize : -1;
    public static bool SelectedDotted => I != null && I.selDotted;
    public static float CubePx(int size) => I != null && size >= 0 && size < 5 ? ((RectTransform)I.cubes[size].transform).sizeDelta.x : 0f;
    public static bool SatelliteShown => I != null && I.sat != null && I.sat.gameObject.activeInHierarchy;
    /// <summary>Tests: one frame of the row's own work (GC measurements).</summary>
    public static void StepForTest() { if (I != null) { I.Update(); I.LateUpdate(); } }
    public static InkButton Slot(int size) => I != null && size >= 0 && size < 5 ? I.slots[size] : null;
}
