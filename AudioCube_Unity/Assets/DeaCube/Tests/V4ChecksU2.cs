using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Package U2 (SPEC v4 §6) Play-mode checks: <c>V4ChecksU2.Run()</c> starts a coroutine; poll <c>V4ChecksU2.Done</c> or the report file
/// Captures/v4checksU2_report.txt (numbered PASS / FAIL lines; PEND = waits for package R's durations engine / focus loop). Covers the card
/// (the speech bubble beside the proxy, ≤ 20 hit targets on its face, UIManager's old panel never with it, the focus loop on open), the
/// rhythm strip against AudioCube.TimelineEvents for a legacy and a durations cube (x = start, width = length, rests hollow, repeats faded,
/// dotted satellites), its edits (select, stretch, rest, delete — one History entry each, the first edit of a legacy cube bakes), Esc and
/// Backspace routing, every drawer control, the chip (popover, mute, spotlight), fader, tools and the start-ring drag, the length row in the
/// inspector and above a draft island (never over its tiles; sets the draft length; auditions), the hint ids, and no per-frame GC.
/// Captures (Game view 1920 x 1080): u2_insp_legacy.png, u2_insp_durations.png, u2_drawer.png, u2_popover.png, u2_picker_draft.png.
/// Loads the fixture, never the user's save; the autosave on quit is switched off; PlayerPrefs it touches are put back.
/// v6: run with PathManager.AutoHand = true (the draft checks draw by clicking tiles with an empty hand, as before v6). Changed meanings:
/// a group pick in the chip's popover keeps it up on the group's voices (when the group has more than one); with a note selected the card's
/// cube tear-off steps aside (the strip's tear-off deletes the note), so the face stays ≤ 20 with v6's copy / paste → next.
/// </summary>
public static class V4ChecksU2
{
    public static string Report = "";
    public static bool Done;
    static int n;
    static StringBuilder sb;
    static int pushes;
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "v4checksU2_report.txt");

    public static string Run(bool captures = true)
    {
        if (SongManager.I == null || PathManager.I == null) return "FAIL needs Play mode";
        Done = false; Report = "";
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch (Exception) { }
        CubeInspector.Ensure().StartCoroutine(Routine(captures));
        return "started";
    }

    static void Line(bool ok, string name, string detail = null)
    {
        n++;
        sb.Append(ok ? "PASS " : "FAIL ").Append("U2.").Append(n).Append(' ').Append(name);
        if (!string.IsNullOrEmpty(detail)) sb.Append(": ").Append(detail);
        sb.Append('\n');
    }
    static void Pend(string s) { sb.Append("PEND ").Append(s).Append('\n'); }
    static void Info(string s) { sb.Append("INFO ").Append(s).Append('\n'); }
    static void OnHistory() { pushes++; }

    // ------------------------------------------------------------------ helpers
    static void LoadFixture()
    {
        CubeInspector.CloseImmediate();
        if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.CancelPath();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
        GlobalClock.Stop();
        GlobalClock.Seek(0);
        SequenceMaster.ResetAllCubes();
    }

    static void Frame(Vector3 focus, float dist, float yaw = -24f, float pitch = 52f)
    {
        var cam = Camera.main;
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = true;
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot;
        cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
    }

    static AudioCube PitchedCube(Func<AudioCube, bool> extra = null)
    {
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && !c.IsDrums && c.Island != null && c.nodes.Count >= 2 && (extra == null || extra(c))) return c;
        return null;
    }

    static readonly Vector3[] corners = new Vector3[4];
    static Rect ScreenRect(RectTransform r)
    {
        r.GetWorldCorners(corners);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++) { Vector2 p = RectTransformUtility.WorldToScreenPoint(null, corners[i]); x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    /// <summary>The top UI raycast target at a screen point is <paramref name="target"/> or one of its children (what a real click reaches).</summary>
    static bool TopHit(Transform target, Vector2 screen, out string got)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        got = "no event system";
        if (es == null || target == null) return false;
        var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = screen };
        var list = new List<UnityEngine.EventSystems.RaycastResult>();
        es.RaycastAll(ped, list);
        if (list.Count == 0) { got = "nothing"; return false; }
        var go = list[0].gameObject;
        got = go.name;
        return go.transform == target || go.transform.IsChildOf(target);
    }

    static Vector2 CentreOf(RectTransform r)
    {
        r.GetWorldCorners(corners);
        return RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
    }

    static void Shoot(string file)
    {
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        Info("capture " + p + " (" + Screen.width + "x" + Screen.height + ")");
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator WaitFor(Func<bool> cond, float timeout) { float t0 = Time.realtimeSinceStartup; while (!cond() && Time.realtimeSinceStartup - t0 < timeout) yield return null; }
    static IEnumerator OpenCard(AudioCube c)
    {
        CubeInspector.Open(c);
        yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Open && InspectorCard.Shown >= 1f, 4f);
        yield return Wait(0.3f);
    }

    static void Key(KeyCode k)
    {
        KeyShim.Sim(k, true, true, false);
        CubeInspector.RunInputForTest();
        KeyShim.Clear();
    }

    /// <summary>The strip against a fresh TimelineEvents of its window: same events; one block per event in view at x = start, width =
    /// length (1 px gaps, clipped at the view's end); rests hollow; repeats (index ≥ one pass) faded; cube sizes grow with the length.</summary>
    static void StripMatches(RhythmStrip s, AudioCube c, string label)
    {
        var ev = new List<AudioCube.TimelineEvent>();
        c.TimelineEvents(s.Window, ev);
        bool same = ev.Count == s.Events.Count;
        for (int i = 0; same && i < ev.Count; i++)
            same = ev[i].node == s.Events[i].node && ev[i].k == s.Events[i].k && Mathf.Abs(ev[i].start - s.Events[i].start) < 1e-4f && Mathf.Abs(ev[i].len - s.Events[i].len) < 1e-4f && ev[i].rest == s.Events[i].rest;
        float xEnd = s.XOfBeat(s.VisibleBeats);
        int visible = 0; foreach (var e in ev) if (s.XOfBeat(e.start) + 1f < xEnd - 2f) visible++;
        bool geo = s.BlockCount == visible; string bad = null;
        for (int i = 0; geo && i < s.BlockCount; i++)
        {
            float x0 = s.XOfBeat(ev[i].start) + 1f, x1 = Mathf.Min(s.XOfBeat(ev[i].start + ev[i].len) - 1f, xEnd);
            x1 = Mathf.Max(x1, x0 + 3f);
            if (Mathf.Abs(s.BlockCentre(i).x - (x0 + x1) * 0.5f) > 1.5f || Mathf.Abs(s.BlockWidth(i) - (x1 - x0)) > 1.5f) { geo = false; bad = "block " + i + " at " + s.BlockCentre(i).x.ToString("F1") + " w " + s.BlockWidth(i).ToString("F1") + " want " + ((x0 + x1) * 0.5f).ToString("F1") + " w " + (x1 - x0).ToString("F1"); }
        }
        bool fade = true, rests = true, grow = true;
        for (int i = 0; i < s.BlockCount && i < ev.Count; i++)
        {
            if (s.BlockFaded(i) != (i >= s.Period)) fade = false;
            if (s.BlockHollow(i) != ev[i].rest) rests = false;
            for (int j = 0; j < s.BlockCount && j < ev.Count; j++)
            {
                bool plain = ev[i].mod == 0 && ev[j].mod == 0;
                if (plain && ev[j].len > ev[i].len + 1e-3f && s.BlockCubePx(j) + 0.01f < s.BlockCubePx(i) && s.BlockWidth(j) > s.BlockCubePx(i) + 6f) grow = false;
            }
        }
        Line(same && geo && fade && rests && grow, "strip (" + label + "): one block per TimelineEvents event in view, x = start, width = length, rests hollow, repeats faded, longer notes bigger cubes",
             ev.Count + " events, " + s.BlockCount + " blocks (" + visible + " in view), one pass " + s.Period + " events / " + s.CycleBeats.ToString("F2") + " beats, " + s.PxPerBeat.ToString("F0") + " px/beat"
             + (same ? "" : ", events differ") + (bad != null ? ", " + bad : "") + (fade ? "" : ", fade wrong") + (rests ? "" : ", rest look wrong") + (grow ? "" : ", cube sizes not by length"));
    }

    /// <summary>True when R's durations engine is in (a baked legacy cube runs on durations; the M0 stub leaves it legacy).</summary>
    static bool ProbeR()
    {
        var c = PitchedCube(x => !x.HasDurations && x.nodes.Count >= 2);
        if (c == null) return false;
        c.BakeDurations();
        bool r = c.HasDurations;
        LoadFixture();
        return r;
    }

    // ------------------------------------------------------------------ the run
    static IEnumerator Routine(bool captures)
    {
        sb = new StringBuilder(); n = 0;
        bool suppressed = Onboarding.Suppressed;
        SongIO.QuitAutosave = false;
        MainMenu.Hide(); WorldInput.Unlock("menu"); WorldInput.Unlock("prompt"); Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        History.OnChanged += OnHistory;
        int keysPref = PlayerPrefs.GetInt(DurationPicker.PrefKeysShown, 0);
        var pm = PathManager.I; var sm = SongManager.I;
        bool rLanded = false;
        try { LoadFixture(); rLanded = ProbeR(); } catch (Exception e) { sb.Append("FAIL fixture: ").Append(e.Message).Append('\n'); }
        yield return null; yield return null;
        PathManager.SimOnly = true; PathManager.SimPos = new Vector3(-50f, -50f, 0f);
        Info("screen " + Screen.width + "x" + Screen.height + "; fixture " + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes; R's durations engine " + (rLanded ? "in" : "not in yet (M0 stubs)"));

        // ================================================================== 1. the card on a legacy cube
        var lc = PitchedCube(c => c.Island.cols >= 5 && c.nodes.Count >= 3 && !c.HasDurations);
        if (lc == null) Line(false, "legacy inspect", "no legacy pitched cube with 3 nodes");
        else
        {
            Frame(lc.Island.Center, 13f); yield return null;
            yield return OpenCard(lc);
            var card = InspectorCard.Root; var strip = InspectorCard.Strip; var grid = InspectorCard.Grid;
            yield return new WaitForEndOfFrame();   // after LateUpdate: what is drawn this frame
            var old = UIManager.I != null ? UIManager.I.Card : null;
            bool oldHidden = old == null || old == card || !old.gameObject.activeInHierarchy || old.localScale == Vector3.zero;
            bool live = InspectorCard.Live && card.gameObject.activeSelf && InspectorCard.Cube == lc && strip.Cube == lc && grid.Cube == lc;
            Line(live && oldHidden, "open: the v4 card pops in bound to the cube (grid, strip); UIManager's old panel never shows with it",
                 "live " + live + ", old panel " + (old == null ? "gone" : (old.gameObject.activeInHierarchy ? "scale " + old.localScale.x : "inactive")));
            int col = lc.Island.column;
            Line(CubeInspector.LastFocusColumn == col, "open: FocusLoop.Begin(the cube's column)", "asked " + CubeInspector.LastFocusColumn + ", the cube's column " + col);
            if (FocusLoop.Active) Line(FocusLoop.Column == col, "the focus loop plays the cube's column while it is inspected", "FocusLoop.Column " + FocusLoop.Column);
            else Pend("the focus loop itself (FocusLoop.Begin is R's; still the M0 stub)");
            int face = InspectorCard.FaceTargetCount();
            Line(face >= 10 && face <= 20, "card face: <= 20 hit targets at rest", face + " targets");
            Vector2 tip = InspectorCard.TailTipScreen; Vector3 ps = CubeInspector.ProxyScreen; float r = CubeInspector.FocusCubeRadiusPx;
            float dTip = (tip - new Vector2(ps.x, ps.y)).magnitude;
            Rect cr = ScreenRect(card);
            Line(r > 10f && dTip <= r * 1.05f && cr.xMin > ps.x + r * 0.5f, "the card is the cube's speech bubble: beside the proxy, its tail tip on the cube",
                 "tip " + dTip.ToString("F0") + " px from the proxy centre (radius " + r.ToString("F0") + "), card left " + cr.xMin.ToString("F0") + ", proxy x " + ps.x.ToString("F0"));
            Line(cr.xMin >= 0f && cr.xMax <= Screen.width && cr.yMin >= 0f && cr.yMax <= Screen.height, "the card is fully on screen", cr.ToString());
            StripMatches(strip, lc, "legacy");
            Rect hr;
            bool hints = Hints.TryGetScreenRect("inspector.rhythm", out hr) && Hints.RectOf("inspector.rhythm") == (RectTransform)strip.transform
                         && Hints.RectOf("inspector.swatches") == (RectTransform)InspectorCard.ChipButton.transform && Hints.RectOf("inspector.grid") == (RectTransform)grid.transform
                         && Hints.RectOf("inspector.volume") != null && Hints.RectOf("inspector.volume").GetComponent<HudSlider>() != null && Hints.RectOf("inspector") == card
                         && Hints.RectOf("inspector.close") == (RectTransform)InspectorCard.CloseButton.transform && Hints.Has("draft.lengths");
            Line(hints, "hint ids: inspector (+ .grid .volume .close .swatches → the chip, .rhythm → the strip) and draft.lengths");
            // what a real click reaches: the top raycast target at each face control's centre is that control
            {
                var targets = new List<Transform> { InspectorCard.ChipButton.transform, Hints.RectOf("inspector.volume"), InspectorCard.PencilButton.transform, InspectorCard.ReverseButton.transform,
                    InspectorCard.ModeButton(0).transform, InspectorCard.ModeButton(1).transform, InspectorCard.ModeButton(2).transform, InspectorCard.CloseButton.transform,
                    InspectorCard.DeleteButton.transform, InspectorCard.MoreTab.transform };
                string miss = null; int ok = 0;
                foreach (var t in targets)
                {
                    string got;
                    Vector2 pt = CentreOf((RectTransform)t);
                    if (t == InspectorCard.MoreTab.transform) pt += new Vector2(8f, 0f);   // the tongue's part outside the card
                    if (TopHit(t, pt, out got)) ok++; else if (miss == null) miss = t.name + " -> " + got;
                }
                string g1, s1;
                bool gridHit = TopHit(grid.transform, RectTransformUtility.WorldToScreenPoint(null, grid.transform.TransformPoint(grid.CellPos(0, 0))), out g1);
                bool stripHit = TopHit(strip.transform, RectTransformUtility.WorldToScreenPoint(null, strip.transform.TransformPoint(strip.BlockCentre(0))), out s1);
                Line(ok == targets.Count && gridHit && stripHit, "a real click reaches every face control (the top raycast target at its centre), the grid cells and the strip blocks",
                     ok + " / " + targets.Count + (miss != null ? ", first miss " + miss : "") + ", grid " + g1 + ", strip " + s1);
            }
            if (captures) { Shoot("u2_insp_legacy.png"); yield return null; yield return null; }

            // ---- select a block: its note plays, the length row docks under the strip, its bead highlights in the grid and the world
            int e1 = strip.BlockCount > 1 ? 1 : 0;
            int au0 = InkUI.AuditionCount;
            strip.SelectEvent(e1, true);
            yield return null; yield return null;
            int node = strip.SelectedNote;
            bool rowOk = DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Inspector && DurationPicker.Target == lc && DurationPicker.TargetNode == node;
            var wm = InspectorCard.WorldMark;
            bool wmOk = wm != null && wm.gameObject.activeInHierarchy && node >= 0 && (wm.position - lc.nodes[node].Top).magnitude < 0.25f;
            Line(node == strip.Events[e1].node && rowOk && grid.HighlightNode == node && wmOk && InkUI.AuditionCount > au0,
                 "select a block: its note plays, the length row docks under the strip, its bead highlights in the grid and on the world tile",
                 "node " + node + ", row " + rowOk + ", grid " + grid.HighlightNode + ", world mark " + wmOk + ", auditions +" + (InkUI.AuditionCount - au0));
            face = InspectorCard.FaceTargetCount();
            Line(face <= 22, "card face with a selected note (length row + rest cube + tear-off + v9 higher / lower): <= 22 hit targets", face + " targets");
            Info("length row on the legacy cube: size " + DurationPicker.SelectedSize + (DurationPicker.SelectedDotted ? " dotted" : "") + " = " + DurationPicker.SelectedTicks + " ticks (its step " + lc.step + ")");

            // ---- right-click = rest on / off (the first edit of a legacy cube bakes)
            int bake0 = RhythmStrip.BakeCalls, p0 = pushes, mod0 = lc.ModOf(node);
            Vector2 at = strip.BlockCentre(e1);
            strip.SimDown(at, 1); strip.SimUp(at, 1);
            yield return null; yield return null;
            Line(lc.ModOf(node) == (mod0 == 1 ? 0 : 1) && pushes - p0 == 1 && RhythmStrip.BakeCalls == bake0 + 1,
                 "right-click a block = rest on / off, one History entry; the first strip edit of a legacy cube calls BakeDurations",
                 "mod " + mod0 + " -> " + lc.ModOf(node) + ", pushes " + (pushes - p0) + ", bakes +" + (RhythmStrip.BakeCalls - bake0) + ", durations " + lc.HasDurations);
            if (rLanded) Line(lc.HasDurations && lc.durs.Count == lc.nodes.Count, "after the bake the cube runs on per-note lengths (durs aligned)", lc.durs.Count + " / " + lc.nodes.Count);
            else Pend("the bake itself (AudioCube.BakeDurations is R's)");
            int er = -1; for (int i = 0; i < strip.BlockCount; i++) if (strip.Events[i].node == node) { er = i; break; }
            Line(er >= 0 && strip.BlockHollow(er) == (lc.ModOf(node) == 1), "the strip redraws the rest (a hollow dotted block)");
            bake0 = RhythmStrip.BakeCalls; p0 = pushes;
            strip.ToggleRestAt(node);
            yield return null;
            Line(lc.ModOf(node) == mod0 && pushes - p0 == 1 && (!rLanded || RhythmStrip.BakeCalls == bake0), "rest back: one History entry (no second bake once baked)", "pushes " + (pushes - p0) + ", bakes +" + (RhythmStrip.BakeCalls - bake0));

            // ---- stretch a block's right edge through the lengths
            if (rLanded)
            {
                yield return null;
                int t0 = strip.TicksOf(0), want = t0 == 48 ? 24 : 48, sn = strip.Events[0].node;
                p0 = pushes; au0 = InkUI.AuditionCount;
                bool st = strip.SimStretch(0, want);
                yield return null; yield return null;
                Line(st && lc.durs[sn] == want && pushes - p0 == 1 && InkUI.AuditionCount > au0, "drag a block's right edge: the note snaps to a new length (auditioned), one History entry on release",
                     t0 + " -> " + lc.durs[sn] + " ticks, pushes " + (pushes - p0) + ", auditions +" + (InkUI.AuditionCount - au0));
                float wantW = want / 24f * strip.PxPerBeat - 2f;
                Line(Mathf.Abs(strip.BlockWidth(0) - wantW) <= 2f || strip.BlockWidth(0) < wantW, "the stretched block is as wide as its new length", strip.BlockWidth(0).ToString("F1") + " px vs " + wantW.ToString("F1"));
                p0 = pushes;
                strip.SimStretch(0, 1000);
                yield return null;
                Line(lc.durs[sn] == 144 && pushes - p0 == 1, "a stretch past the longest length stops at the dotted whole (144 ticks)", lc.durs[sn] + "");
            }
            else Pend("stretch (AudioCube.SetDuration is R's; the drag path runs, the length cannot change yet)");

            // ---- Backspace with a note selected deletes that note; the tear-off too; the last note stays
            strip.SelectEvent(0, false); yield return null;
            int nn = lc.nodes.Count, cubes0 = SequenceMaster.Cubes.Count; p0 = pushes;
            KeyShim.Sim(KeyCode.Backspace, true, true, false); PathManager.I.RunHotkeysForTest(); KeyShim.Clear();
            yield return null; yield return null;
            Line(lc != null && lc.nodes.Count == nn - 1 && pushes - p0 == 1 && CubeInspector.IsOpen && CubeInspector.Current == lc && SequenceMaster.Cubes.Count == cubes0,
                 "Backspace with a note selected deletes that note (not the cube), one History entry", "nodes " + nn + " -> " + (lc != null ? lc.nodes.Count : -1) + ", pushes " + (pushes - p0));
            if (strip.SelectedNote < 0) strip.SelectEvent(0, false);
            yield return null;
            nn = lc.nodes.Count; p0 = pushes;
            bool tearShown = strip.TearShown;
            strip.SimDown(strip.TearPos, 0); strip.SimUp(strip.TearPos, 0);
            yield return null; yield return null;
            Line(tearShown && lc.nodes.Count == nn - 1 && pushes - p0 == 1, "the tear-off on the selected block deletes the note, one History entry", "nodes " + nn + " -> " + lc.nodes.Count);
            while (lc.nodes.Count > 1) { strip.SelectEvent(0, false); strip.DeleteSelected(); yield return null; }
            strip.SelectEvent(0, false); yield return null;
            p0 = pushes;
            strip.DeleteSelected();
            yield return null;
            Line(lc.nodes.Count == 1 && pushes - p0 == 0 && CubeInspector.IsOpen, "the last note stays (refused, no History entry)");

            // ---- Esc: the drawer first, then the selected note, then the inspector
            InspectorCard.MoreTab.Click(); yield return Wait(0.3f);
            strip.SelectEvent(0, false); yield return null;
            bool d1 = InspectorCard.DrawerOpen;
            Key(KeyCode.Escape); yield return null;
            bool d2 = !InspectorCard.DrawerOpen && CubeInspector.State == CubeInspector.Phase.Open;
            Key(KeyCode.Escape); yield return null;
            bool d3 = strip.SelectedNote < 0 && CubeInspector.State == CubeInspector.Phase.Open && !DurationPicker.IsShown;
            Key(KeyCode.Escape); yield return null;
            bool d4 = CubeInspector.State == CubeInspector.Phase.Closing;
            Line(d1 && d2 && d3 && d4, "Esc closes the drawer, then deselects the note (the length row goes), then sends the cube back", d1 + " " + d2 + " " + d3 + " " + d4);
            yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
        }

        // ================================================================== 2. the drawer and the face controls (legacy cube)
        LoadFixture(); yield return null;
        var dc = PitchedCube(c => c.Island.cols >= 5 && c.nodes.Count >= 3 && !c.HasDurations);
        if (dc != null)
        {
            Frame(dc.Island.Center, 13f); yield return null;
            yield return OpenCard(dc);
            InspectorCard.MoreTab.Click();
            yield return WaitFor(() => InspectorCard.DrawerShown >= 1f, 1.5f); yield return Wait(0.25f);
            var dial = InspectorCard.Dial;
            // v7 §21 ("just keep cubes on each grid"): the rider card is retired — a cube never leaves its grid
            Line(InspectorCard.DrawerOpen && InspectorCard.DrawerShown >= 1f && dial != null && dial.gameObject.activeInHierarchy && InspectorCard.DrawerButton("pace2").gameObject.activeInHierarchy
                 && InspectorCard.DrawerButton("rider") == null,
                 "the more tab pulls the drawer out: twin, shadow, echo, stamp, gate, octave (§21: no rider card); the necklace and the pace row for a legacy cube");
            if (captures) { Shoot("u2_drawer.png"); yield return null; yield return null; }
            int p0 = pushes;
            int f0 = dc.follow; InspectorCard.DrawerButton("shadow").Click();
            bool shadowOk = dc.follow == (f0 + 1) % 4 && pushes - p0 == 1;
            int e0 = dc.echo; p0 = pushes; InspectorCard.DrawerButton("echo").Click();
            bool echoOk = dc.echo == (e0 + 1) % 3 && pushes - p0 == 1;
            bool sh0 = dc.shimmer; p0 = pushes; InspectorCard.DrawerButton("echo").SimHold();
            bool shimmerOk = dc.shimmer != sh0 && pushes - p0 == 1;
            bool riderOk = InspectorCard.DrawerButton("rider") == null;   // §21: the rider is retired (no card, nothing to toggle)
            int g0 = (int)dc.gate, gw = g0 == 0 ? 2 : 0; p0 = pushes; InspectorCard.DrawerButton("gate" + gw).Click();
            bool gateOk = (int)dc.gate == gw && pushes - p0 == 1;
            int o0 = dc.octave; p0 = pushes; InspectorCard.DrawerButton(o0 < 1 ? "up" : "down").Click();
            bool octOk = dc.octave == (o0 < 1 ? o0 + 1 : o0 - 1) && pushes - p0 == 1;
            int cubes0 = SequenceMaster.Cubes.Count; p0 = pushes; InspectorCard.DrawerButton("twin").Click();
            AudioCube twin = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c != dc && c.twinOf == dc.id) twin = c;
            bool twinOk = twin != null && SequenceMaster.Cubes.Count == cubes0 + 1 && pushes - p0 == 1;
            InspectorCard.DrawerButton("twin").SimHold(); yield return null;
            bool variants = InspectorCard.PopoverOpen;
            Key(KeyCode.Escape); yield return null;
            bool variantsClosed = !InspectorCard.PopoverOpen && CubeInspector.State == CubeInspector.Phase.Open;
            cubes0 = SequenceMaster.Cubes.Count; p0 = pushes; InspectorCard.DrawerButton("stamp").Click();
            bool stampOk = SequenceMaster.Cubes.Count == cubes0 + 1 && pushes - p0 == 1;
            var st0 = dc.step; int pk = st0 == StepLen.Eighth ? 1 : 2; p0 = pushes; InspectorCard.DrawerButton("pace" + pk).Click();
            bool paceOk = dc.step != st0 && pushes - p0 == 1;
            Line(shadowOk && echoOk && shimmerOk && riderOk && gateOk && octOk && twinOk && stampOk && paceOk && variants && variantsClosed,
                 "drawer controls: shadow, echo (hold: shimmer), gate, octave, twin (hold: the variants), stamp, pace — one History entry each (§21: the rider card is gone)",
                 "shadow " + shadowOk + " echo " + echoOk + " shimmer " + shimmerOk + " rider gone " + riderOk + " gate " + gateOk + " octave " + octOk + " twin " + twinOk + " variants " + variants + "/" + variantsClosed + " stamp " + stampOk + " pace " + paceOk);
            InspectorCard.MoreTab.Click(); yield return Wait(0.3f);

            // ---- the chip: popover (recolour), right-click mute, hold spotlight
            var chip = InspectorCard.ChipButton;
            chip.Click(); yield return null;
            bool popOpen = InspectorCard.PopoverOpen;
            if (captures) { yield return Wait(0.2f); Shoot("u2_popover.png"); yield return null; yield return null; }
            int k = -1; for (int i = 0; i < Instruments.Count && k < 0; i++) if (i != dc.instrument && !Instruments.IsDrums(i)) k = i;
            p0 = pushes;
            InspectorCard.InstrumentButton(k).Click(); yield return null;
            // v6 (SPEC v6 §8.3): a group with several voices keeps the popover up on its voices (pick one next); a single-voice group closes it as in v4
            bool recol = dc.instrument == k && pushes - p0 == 1 && (Instruments.VoiceCount(k) >= 2 ? InspectorCard.PopoverOpen : !InspectorCard.PopoverOpen);
            if (InspectorCard.PopoverOpen) { chip.Click(); yield return null; }
            bool m0 = dc.muted; p0 = pushes;
            chip.onRightClick(); yield return null;
            bool muteOk = dc.muted != m0 && pushes - p0 == 1;
            chip.onRightClick(); yield return null;
            chip.onHoldChanged(true); yield return Wait(0.3f);    // while playing, a spotlight lands on the next 16th (Performance)
            bool spotOn = Performance.SpotlightCube == dc;
            chip.onHoldChanged(false); yield return Wait(0.3f);
            bool spotOff = Performance.SpotlightCube == null;
            Line(popOpen && recol && muteOk && spotOn && spotOff, "instrument chip: click = the 10 instrument cubes (a pick recolours, one entry), right-click = mute (one entry), hold = spotlight",
                 "popover " + popOpen + ", recolour " + recol + ", mute " + muteOk + ", spotlight " + spotOn + "/" + spotOff);

            // ---- fader, path modes, reverse, pencil, start-ring drag
            var fader = InspectorCard.Fader;
            float v0 = dc.volume, vw = v0 > 0.5f ? 0.3f : 0.8f; p0 = pushes;
            fader.Set(vw, true); fader.onRelease();
            bool faderOk = Mathf.Abs(dc.volume - vw) < 1e-3f && pushes - p0 == 1;
            p0 = pushes; InspectorCard.ModeButton(1).Click();
            bool modeOk = dc.mode == PathMode.PingPong && pushes - p0 == 1;
            InspectorCard.ModeButton(0).Click();
            var before = new List<TileInteraction>(dc.nodes); before.Reverse(); p0 = pushes;
            InspectorCard.ReverseButton.Click();
            bool revOk = pushes - p0 == 1; for (int i = 0; revOk && i < before.Count; i++) revOk = dc.nodes[i] == before[i];
            InspectorCard.PencilButton.Click(); bool armed = InspectorCard.Grid.PencilArmed; InspectorCard.PencilButton.Click();
            bool pencilOk = armed && !InspectorCard.Grid.PencilArmed;
            var grid = InspectorCard.Grid;
            int sdx = 0, sdz = 0;
            int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            for (int d = 0; d < 4; d++) if (dc.CanShift(dirs[d, 0], dirs[d, 1])) { sdx = dirs[d, 0]; sdz = dirs[d, 1]; break; }
            var xs = new List<int>(dc.gridX); var zs = new List<int>(dc.gridZ);
            int x0 = dc.nodes[0].gridX, z0 = dc.nodes[0].gridZ; p0 = pushes;
            bool ring = grid.SimDownRing();
            grid.SimMoveCell(x0 + sdx, z0 + sdz); grid.SimUp();
            bool shifted = ring && (sdx != 0 || sdz != 0) && pushes - p0 == 1;
            for (int i = 0; shifted && i < xs.Count; i++) shifted = dc.gridX[i] == xs[i] + sdx && dc.gridZ[i] == zs[i] + sdz;
            Line(faderOk && modeOk && revOk && pencilOk && shifted, "face tools: the ink fader (volume, one entry on release), path modes, reverse, pencil, dragging the start ring moves the whole path (one entry)",
                 "fader " + faderOk + " mode " + modeOk + " reverse " + revOk + " pencil " + pencilOk + " ring drag " + shifted + " by " + sdx + "," + sdz);
            // every plain bead is sized by its own note: a legacy cube's step for all of them, a durations cube's per-note length (integration: the
            // cube may arrive already baked from an earlier suite)
            yield return null; yield return null;   // the grid redraws its overlay on the frame after the ring-drag edit
            var bs = grid.BeadSizes; int beadBad = 0, beadChecked = 0; string beadInfo = "";
            for (int i = 0; i < bs.Count && i < dc.nodes.Count; i++)
            {
                if (dc.ModOf(i) != 0) continue;
                float want = PathGridView.BeadPx * AudioCube.SizeOf(dc.HasDurations ? dc.DurBeats(i) : dc.StepBeats);
                beadChecked++; if (Mathf.Abs(bs[i] - want) >= 0.5f) { beadBad++; if (beadInfo.Length == 0) beadInfo = " first off: bead " + i + " " + bs[i].ToString("F1") + " vs " + want.ToString("F1"); }
            }
            Line(bs.Count == dc.nodes.Count && beadBad == 0, "grid beads are sized by note length (a legacy cube: all its step's size; a durations cube: each note's)",
                 beadChecked + " beads checked (" + bs.Count + " beads, " + dc.nodes.Count + " nodes), " + (dc.HasDurations ? "durations" : "legacy") + " cube" + beadInfo);
            CubeInspector.Close(); yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
        }
        else Line(false, "drawer checks", "no legacy cube");

        // ================================================================== 3. a durations cube: strip, satellites, the length row sets lengths
        LoadFixture(); yield return null;
        var uc = PitchedCube(c => c.Island.cols >= 5 && c.nodes.Count >= 4);
        if (uc != null)
        {
            var durs = new List<int>();
            int[] pattern = { 24, 12, 12, 36, 48, 6, 6, 18 };
            for (int i = 0; i < uc.nodes.Count; i++) durs.Add(pattern[i % pattern.Length]);
            uc.SetAllDurations(durs);
            if (!uc.HasDurations) Pend("durations cube: AudioCube.SetAllDurations is R's (still the M0 stub) — strip / length-row checks on a durations cube wait for R");
            else
            {
                History.Push();
                Frame(uc.Island.Center, 13f); yield return null;
                yield return OpenCard(uc);
                var strip = InspectorCard.Strip;
                yield return null;
                StripMatches(strip, uc, "durations");
                int dotted = -1; for (int i = 0; i < strip.BlockCount && i < strip.Period; i++) if (strip.Events[i].node >= 0 && uc.durs[strip.Events[i].node] == 36) { dotted = i; break; }
                Line(dotted >= 0 && strip.BlockDotted(dotted), "a dotted note's block carries the orbiting satellite", dotted >= 0 ? "block " + dotted : "no dotted note in view");
                strip.SelectEvent(0, true); yield return null; yield return null;
                int node = strip.SelectedNote;
                int p0 = pushes, pk0 = DurationPicker.PickCount;
                DurationPicker.SimClick(3); yield return null;
                bool half = uc.durs[node] == 48 && pushes - p0 == 1;
                p0 = pushes;
                DurationPicker.SimClick(3); yield return null; yield return null;
                bool dotHalf = uc.durs[node] == 72 && pushes - p0 == 1 && DurationPicker.SelectedDotted && DurationPicker.SatelliteShown;
                Line(half && dotHalf && DurationPicker.PickCount == pk0 + 2, "length row (inspecting): a size sets the note's length (one entry); the selected size again = dotted, the satellite shows",
                     "durs[" + node + "] = " + uc.durs[node] + ", dotted " + DurationPicker.SelectedDotted);
                yield return Wait(0.15f);
                int au0 = InkUI.AuditionCount;
                DurationPicker.SimHover(1);
                Line(InkUI.AuditionCount == au0 + 1, "length row: hovering a size auditions that length on the selected note");
                DurationPicker.SimClick(1); yield return null; yield return Wait(0.4f);
                if (captures) { Shoot("u2_insp_durations.png"); yield return null; yield return null; }
                p0 = pushes; int m0 = uc.ModOf(node);
                DurationPicker.SimClickRest(); yield return null;
                Line(uc.ModOf(node) == (m0 == 1 ? 0 : 1) && pushes - p0 == 1, "length row: the hollow cube = rest on / off (one entry)");
                CubeInspector.Close(); yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
            }
        }

        // ================================================================== 4. the length row above a draft island
        LoadFixture(); yield return null;
        {
            KeyBlock isl = null; int zRow = -1, x0 = -1;
            foreach (var kb in sm.Islands)
            {
                if (kb == null || kb.IsMoon || kb.cols < 5) continue;
                for (int z = 0; z < kb.rows && zRow < 0; z++)
                    for (int x = 0; x + 3 < kb.cols && zRow < 0; x++)
                    {
                        bool free = true;
                        for (int k = 0; k < 4; k++) if (PathManager.TopCubeOn(kb.GetTile(x + k, z)) != null) { free = false; break; }
                        if (free) { zRow = z; x0 = x; }
                    }
                if (zRow >= 0) { isl = kb; break; }
            }
            if (isl == null) Line(false, "draft length row", "no free row of 4 tiles");
            else
            {
                Frame(isl.Center, 16f); yield return null;
                PlayerPrefs.SetInt(DurationPicker.PrefKeysShown, 0);
                var cam = Camera.main;
                Vector3 t0 = cam.WorldToScreenPoint(isl.GetTile(x0, zRow).Top), t1 = cam.WorldToScreenPoint(isl.GetTile(x0 + 1, zRow).Top);
                pm.SimPointer(t0, true, true, false); pm.SimPointer(t0, false, false, true);
                yield return null; yield return null;
                bool shown = pm.IsDrawing && DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Draft && DurationPicker.Target == pm.Draft && DurationPicker.KeycapsShown;
                Line(shown, "a draft shows the length row above its island, with [ ] . keycaps for the first drafts", "drawing " + pm.IsDrawing + ", mode " + DurationPicker.Mode + ", keycaps " + DurationPicker.KeycapsShown);
                yield return null;
                Rect rr = DurationPicker.RowScreenRect; int inside = 0, corners4 = 0;
                float h = ProjectConfig.TileSize * 0.5f;
                foreach (var t in isl.tiles)
                {
                    if (t == null) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 s = cam.WorldToScreenPoint(t.Top + new Vector3((k & 1) == 0 ? -h : h, 0f, (k & 2) == 0 ? -h : h));
                        corners4++;
                        if (s.z > 0f && rr.Contains(new Vector2(s.x, s.y))) inside++;
                    }
                }
                bool side = DurationPicker.PlacedBelow ? rr.yMax <= DurationPicker.AnchorScreenY : rr.yMin >= DurationPicker.AnchorScreenY;
                Line(rr.width > 50f && inside == 0 && side, "the row is anchored to the island's far edge and never over its tiles", "row " + rr + ", tile corners inside " + inside + " / " + corners4 + ", " + (DurationPicker.PlacedBelow ? "below" : "above") + " edge y " + DurationPicker.AnchorScreenY.ToString("F0"));
                yield return Wait(0.25f);   // the row's pop-in has settled
                int reach = 0; string rmiss = null;
                for (int k = 0; k < 5; k++) { string got; if (TopHit(DurationPicker.Slot(k).transform, CentreOf((RectTransform)DurationPicker.Slot(k).transform), out got)) reach++; else if (rmiss == null) rmiss = k + " -> " + got; }
                Line(reach == 5, "a real click reaches each of the five length cubes above the draft island", reach + " / 5" + (rmiss != null ? ", miss " + rmiss : ""));
                pm.BrushTicks = 24; yield return null;   // integration: the brush persists between suites; start from a quarter so size 3 is a fresh pick
                int p0 = pushes, au0 = InkUI.AuditionCount;
                DurationPicker.SimClick(3); yield return null;
                bool half = pm.BrushTicks == 48;
                DurationPicker.SimClick(3); yield return null;
                bool dot = pm.BrushTicks == 72 && DurationPicker.SelectedDotted;
                DurationPicker.SimClick(0); yield return null;
                bool fast = pm.BrushTicks == 6 && DurationPicker.SelectedTicks == 6;
                Line(half && dot && fast && pushes == p0 && InkUI.AuditionCount >= au0 + 3, "a click sets the brush — the next note's size (v7 §15, package D: PathManager.SetBrush; the selected size again = dotted), auditioned, no History entry while drafting",
                     "brush " + pm.BrushTicks + ", auditions +" + (InkUI.AuditionCount - au0) + ", pushes " + (pushes - p0));
                yield return Wait(0.15f);
                au0 = InkUI.AuditionCount;
                DurationPicker.SimHover(4);
                Line(InkUI.AuditionCount == au0 + 1, "hovering a size auditions it on the draft's latest note");
                pm.SimPointer(t1, true, true, false); pm.SimPointer(t1, false, false, true);
                yield return null; yield return null;
                Line(pm.IsDrawing && DurationPicker.Mode == DurationPicker.Where.Draft && pm.Draft != null && DurationPicker.TargetNode == pm.Draft.nodes.Count - 1, "the row follows the draft's latest node", "node " + DurationPicker.TargetNode);
                DurationPicker.SimClick(1); yield return null; DurationPicker.SimClick(1); yield return null;   // a dotted 8th: the satellite shows in the capture
                yield return Wait(0.3f);
                if (captures) { Shoot("u2_picker_draft.png"); yield return null; yield return null; }
                pm.CancelPath(); yield return null; yield return null;
                Line(!DurationPicker.IsShown && DurationPicker.Mode == DurationPicker.Where.Hidden, "the row leaves with the draft");
                // keycaps: the first two drafts only
                bool[] keycaps = new bool[2];
                for (int d = 0; d < 2; d++)
                {
                    pm.SimPointer(t0, true, true, false); pm.SimPointer(t0, false, false, true);
                    yield return null; yield return null;
                    keycaps[d] = DurationPicker.KeycapsShown;
                    pm.CancelPath(); yield return null; yield return null;
                }
                Line(keycaps[0] && !keycaps[1], "the [ ] . keycaps show for the first two drafts only", "2nd " + keycaps[0] + ", 3rd " + keycaps[1]);
            }
        }

        // ================================================================== 5. no per-frame GC, frame cost
        LoadFixture(); yield return null;
        var gc = PitchedCube(c => c.Island.cols >= 5 && c.nodes.Count >= 3);
        if (gc != null)
        {
            Frame(gc.Island.Center, 13f); yield return null;
            GlobalClock.Seek(sm.MeasureStarts[Mathf.Clamp(gc.assignedGridIndex, 0, sm.MeasureStarts.Count - 1)]);
            GlobalClock.LoopSong = true;
            GlobalClock.Play();
            yield return OpenCard(gc);
            InspectorCard.Strip.SelectEvent(0, false);
            yield return Wait(1.0f);
            var strip = InspectorCard.Strip; var grid = InspectorCard.Grid;
            for (int i = 0; i < 3; i++) { InspectorCard.StepForTest(); strip.StepForTest(); grid.StepForTest(); DurationPicker.StepForTest(); }   // warm
            var sw = Stopwatch.StartNew();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++) { InspectorCard.StepForTest(); strip.StepForTest(); grid.StepForTest(); DurationPicker.StepForTest(); }
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / 120.0;
            Line(a1 - a0 == 0, "no per-frame GC: 120 frames of the card, strip, grid and length row (playing, a note selected) allocate nothing", (a1 - a0) + " bytes");
            Line(ms < 1.0, "their per-frame work stays well inside the HUD budget (< 1 ms)", ms.ToString("F3") + " ms / frame (editor)");
            long b0 = GC.GetAllocatedBytesForCurrentThread(); int frames = 0; float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 1f) { frames++; yield return null; }
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            Info("whole frames with the card open and playing (every package, canvas rebuilds included): " + ((b1 - b0) / Mathf.Max(1, frames)) + " B / frame over " + frames + " frames");
            Info("strip while playing: bounces " + strip.BounceCount + ", playhead line " + strip.PlayLineShown + " at x " + strip.PlayLineX.ToString("F0") + ", cube at " + strip.PlayCubePos);
            Line(strip.BounceCount > 0 && strip.PlayLineShown, "while playing: the playhead runs and the playing block bounces on its landing", "bounces " + strip.BounceCount);
            GlobalClock.Stop();
            CubeInspector.Close(); yield return WaitFor(() => CubeInspector.State == CubeInspector.Phase.Closed, 3f);
        }

        // ---- leave things as found
        CubeInspector.CloseImmediate();
        FocusLoop.Dismiss();
        GlobalClock.Stop();
        try { LoadFixture(); } catch (Exception) { }
        PathManager.SimOnly = false; KeyShim.Clear();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        History.OnChanged -= OnHistory;
        Onboarding.Suppressed = suppressed;
        PlayerPrefs.SetInt(DurationPicker.PrefKeysShown, keysPref);
        Info("end: locks " + WorldInput.Describe() + ", inspector " + CubeInspector.State + ", picker " + DurationPicker.Mode);
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report); } catch (Exception) { }
        Done = true;
    }
}
