using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// v8 Play-mode verification of the keyboard's SPHERE HANDS (KeyHands, which replaced v7's cat), the chord ROOT mark on a keyboard's keys and the
/// retired melody card: <c>V8ChecksHands.RunAll()</c> starts a coroutine; poll <c>V8ChecksHands.Done</c> or Captures/h8_report.txt (numbered PASS / FAIL
/// lines). The v7 cat suite's test song: C F G C at 100 bpm, keyboard 0 behind column 0 (a melody with a two-octave leap over a bass voice of half
/// notes), keyboard 1 behind column 2 (one voice crossing the middle). Covers: hands per keyboard (a sphere per voice, at least MinHands), no
/// colliders; the score = the cubes' own timeline, one voice per sphere (the lower voice left), the voice's colour; a landing ON the sounding key at
/// every onset (pure and drawn), a hop in the air between two keys, a sit after the note with the key let go; playing: the key held down during a
/// note and back up after it, every sphere drawn where its pure pose says; stopped: every sphere stays sitting on a key, no jump; an empty-hand
/// press sends a sphere to the key and it stays; the chord's root keys marked (column 0 C, column 2 G); the melody card hidden; frame cost; Synth
/// late / errors; console clean. Captures h8_*.png. Runs with PathManager.AutoHand = false. Never writes the user's save; reloads the fixture at the end.
/// </summary>
public static class V8ChecksHands
{
    public static string Report = "";
    public static bool Done = true;
    static int num;
    static readonly List<string> errors = new List<string>();
    public static string ReportPath => Path.Combine(V2Checks.CapturePath, "h8_report.txt");

    delegate string Check();

    static void Line(StringBuilder sb, bool ok, string name, string detail)
    {
        num++;
        sb.Append(ok ? "PASS" : "FAIL").Append(" H8-").Append(num.ToString("00")).Append(' ').Append(name).Append(": ").Append(detail).Append('\n');
        Flush(sb);
    }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); Flush(sb); }
    static void Flush(StringBuilder sb) { Report = sb.ToString(); try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(ReportPath, Report + (Done ? "" : "...running\n")); } catch (Exception) { } }
    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); Line(sb, d == null || d.StartsWith("ok"), name, d ?? "ok"); }
        catch (Exception e) { Line(sb, false, name, e.GetType().Name + " " + e.Message + " @ " + FirstFrame(e)); }
    }
    static string FirstFrame(Exception e) { var s = e.StackTrace ?? ""; int nl = s.IndexOf('\n'); return (nl > 0 ? s.Substring(0, nl) : s).Trim(); }
    static string F(float v) => v.ToString("F3");
    static SongManager SM => SongManager.I;

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (errors.Count < 20) errors.Add(msg.Length > 200 ? msg.Substring(0, 200) : msg);
    }

    // ------------------------------------------------------------------ the test song (v7's cat song)
    public const int Bpm = 100;
    public static readonly int[] MelodyA = { 12, 14, 16, 19, 0, 24, 21, 16 };
    public static readonly int[] BassB = { 3, 8 };
    public static readonly int[] MelodyC = { 2, 4, 7, 20, 22, 19, 5, 12 };
    public const int IdA = 8301, IdB = 8302, IdC = 8303;

    static MeasureState Chord(int col, string name, int root, int[] semis, float pz = 0f) => new MeasureState { chordKey = name, root = root, semis = semis, bars = 1, kind = 0, col = col, repeat = 1, energy = 2, pz = pz, placed = true };
    static MeasureState Keys(int col) => new MeasureState { chordKey = "Keys", root = 48, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, pz = 5f, placed = true, keyCount = 25 };
    static CubeState Cube(int id, int inst, int measure, int[] xs, int ticks)
    {
        int n = xs.Length;
        return new CubeState { id = id, instrument = inst, measure = measure, xs = (int[])xs.Clone(), zs = new int[n], rests = new bool[n], durs = Enumerable.Repeat(ticks, n).ToArray(), step = 1, gate = 1, mode = 0, volume = 1f, hits = -1, seed = id, moon = -1, twinOf = -1, echoOf = -1 };
    }

    public static SongState TestSong()
    {
        var m = new List<MeasureState>
        {
            Chord(0, "C", 60, new[] { 0, 4, 7 }), Keys(0), Chord(0, "Am", 57, new[] { 0, 3, 7 }, 10f),
            Chord(1, "F", 65, new[] { 0, 4, 7 }),
            Chord(2, "G", 67, new[] { 0, 4, 7 }), Keys(2),
            Chord(3, "C", 60, new[] { 0, 4, 7 }),
        };
        var cubes = new[] { Cube(IdA, 8, 1, MelodyA, 12), Cube(IdB, 4, 1, BassB, 48), Cube(IdC, 8, 5, MelodyC, 12) };
        return new SongState
        {
            version = SongState.CurrentVersion, name = "h8 hands", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes,
            moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f
        };
    }

    static void LoadTest()
    {
        GlobalClock.Stop(); GlobalClock.ClearRegion();
        SongState.Apply(TestSong());
        History.Reset(); History.Push();
    }

    static List<KeyBlock> Keyboards() => SM.Islands.Where(kb => kb != null && kb.IsKeyboard).ToList();
    static AudioCube CubeById(int id) => SequenceMaster.Cubes.FirstOrDefault(c => c != null && c.id == id);

    /// <summary>The notes cube <paramref name="c"/> plays on keyboard <paramref name="kb"/>, read independently through AudioCube.TimelineEvents.</summary>
    static List<KeyValuePair<float, TileInteraction>> Expected(AudioCube c, KeyBlock kb)
    {
        var r = new List<KeyValuePair<float, TileInteraction>>();
        var evs = new List<AudioCube.TimelineEvent>();
        for (int w = 0; w < c.windows.Count; w++)
        {
            if (c.windows[w].silent) continue;
            c.TimelineEvents(w, evs);
            foreach (var e in evs)
            {
                if (e.rest) continue;
                var t = c.TileAt(w, e.node);
                if (t != null && t.island == kb) r.Add(new KeyValuePair<float, TileInteraction>(c.windows[w].start + e.start, t));
            }
        }
        return r;
    }

    /// <summary>The sphere of <paramref name="s"/> whose score holds the note at <paramref name="on"/> on <paramref name="t"/> (−1 none).</summary>
    static int HandOf(KeyHands.Set s, float on, TileInteraction t)
    {
        for (int h = 0; h < s.Hands.Count; h++)
            for (int i = 0; i < s.Hands[h].NoteCount; i++)
            {
                float o, f; TileInteraction tt; bool wr;
                if (s.Hands[h].NoteOf(i, out o, out f, out tt, out wr) && !wr && tt == t && Mathf.Abs(o - on) < 1e-3f) return h;
            }
        return -1;
    }

    static Camera Cam => OrbitCamera.I != null ? OrbitCamera.I.GetComponent<Camera>() : Camera.main;

    // ------------------------------------------------------------------ runner
    public static string RunAll(bool captures = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        if (!Done) return "already running";
        Done = false; Report = ""; num = 0; errors.Clear();
        SequenceMaster.I.StartCoroutine(AllRoutine(captures));
        return "started";
    }

    static void Prepare()
    {
        SongIO.QuitAutosave = false;
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        PathManager.AutoHand = false;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
    }

    static void Restore()
    {
        try { foreach (var s in KeyHands.Sets) s.Unfreeze(); } catch (Exception) { }
        try { if (PathManager.I != null && PathManager.I.IsDrawing) PathManager.I.CancelPath(); } catch (Exception) { }
        try { CubeInspector.CloseImmediate(); } catch (Exception) { }
        try { if (Presenter.Active) Presenter.Exit(); } catch (Exception) { }
        try { FocusLoop.Dismiss(); } catch (Exception) { }
        GlobalClock.Stop();
        GlobalClock.ClearRegion();
        try { if (PathManager.I != null) PathManager.I.PutDown(); } catch (Exception) { }
        PathManager.AutoHand = false;
        PathManager.SimOnly = false;
    }

    static IEnumerator Shot(string file)
    {
        yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator UntilBeat(double b, float timeout = 10f) { float t0 = Time.realtimeSinceStartup; while (GlobalClock.SongBeatD < b && Time.realtimeSinceStartup - t0 < timeout) yield return null; }
    static int lateExcused;
    static IEnumerator SeekPlaying(double b, float settle = 0.25f) { int lt = Synth.LateEvents; GlobalClock.Seek(b); yield return Wait(settle); lateExcused += Synth.LateEvents - lt; }
    static IEnumerator ShotExcused(string file) { int lt = Synth.LateEvents; yield return Shot(file); yield return Wait(0.3f); lateExcused += Synth.LateEvents - lt; }

    /// <summary>A screen point where the hand picks key <paramref name="t"/> now (its top, else nearer its front end, where no black key covers it).</summary>
    static bool PickPoint(TileInteraction t, out Vector3 sp)
    {
        var cam = Cam;
        foreach (float f in new[] { 0f, 0.2f, 0.35f, 0.5f, 0.65f, -0.2f })
        {
            sp = cam.WorldToScreenPoint(t.Top - t.island.transform.forward * f);
            if (sp.z > 0f && PathManager.I.PickAt(sp).tile == t) return true;
        }
        sp = Vector3.zero;
        return false;
    }

    static IEnumerator Click(Vector3 sp)
    {
        var pm = PathManager.I;
        pm.SimPointer(sp, true, true, false); pm.SimPointer(sp, false, false, true);
        yield return null;
    }

    static void Frame(KeyBlock kb, float tight = 0.95f)
    {
        if (OrbitCamera.I == null || kb == null) return;
        OrbitCamera.I.FrameBounds(kb.WorldBounds, 0.1f, true, tight);
    }

    /// <summary>The key's sink right now (world units, + = down).</summary>
    static float SinkOf(TileInteraction t) => -Vector3.Dot(t.transform.position + t.transform.up * (ProjectConfig.TileThickness * 0.5f) - t.Top, t.island.transform.up);

    static IEnumerator AllRoutine(bool captures)
    {
        var sb = new StringBuilder();
        sb.Append("V8ChecksHands ").Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');
        Application.logMessageReceived += OnLog;
        Prepare();
        int late0 = Synth.LateEvents, err0 = Synth.Errors; lateExcused = 0;
        LoadTest();
        yield return Frames(3);
        KeyHands.RefreshNow();
        Frame(Keyboards().FirstOrDefault());
        yield return Wait(0.6f);
        var kbs = Keyboards();
        KeyBlock k0 = kbs.Count > 0 ? kbs[0] : null, k1 = kbs.Count > 1 ? kbs[1] : null;
        var cA = CubeById(IdA); var cB = CubeById(IdB); var cC = CubeById(IdC);
        var s0 = KeyHands.Of(k0); var s1 = KeyHands.Of(k1);

        // ---------------------------------------------------------------- hands per keyboard
        Run(sb, "hands per keyboard: 2 keyboards → 2 sets; keyboard 0 (two voices) has 2 spheres, keyboard 1 (one voice) the minimum " + KeyHands.MinHands, () =>
        {
            if (KeyHands.I == null) return "no KeyHands manager";
            if (kbs.Count != 2) return "keyboards " + kbs.Count;
            if (KeyHands.Count != 2 || s0 == null || s1 == null || s0 == s1) return "sets " + KeyHands.Count;
            if (cA == null || cB == null || cC == null) return "test cubes missing";
            if (s0.Hands.Count != 2 || s0.Voices != 2) return "keyboard 0: " + s0.Hands.Count + " spheres, " + s0.Voices + " voices";
            if (s1.Hands.Count != KeyHands.MinHands || s1.Voices != 1) return "keyboard 1: " + s1.Hands.Count + " spheres, " + s1.Voices + " voices";
            return "ok: radius " + F(s0.Radius) + " u";
        });
        if (s0 == null || s1 == null || cA == null || cB == null || cC == null) { Finish(sb); yield break; }
        Run(sb, "no colliders on the spheres (never block a pick), none under its island; no cat left in the scene", () =>
        {
            int n = KeyHands.I.GetComponentsInChildren<Collider>(true).Length;
            if (n != 0) return n + " colliders";
            foreach (var s in KeyHands.Sets) foreach (var h in s.Hands) { if (h.Root == null) return "no root"; if (h.Root.IsChildOf(s.Island.transform)) return "a sphere under its island"; }
            if (GameObject.Find("KeyCats") != null) return "a KeyCats object exists";
            return null;
        });

        // ---------------------------------------------------------------- the score, the voices
        var expA = Expected(cA, k0); var expB = Expected(cB, k0); var expC = Expected(cC, k1);
        Run(sb, "the score is the cubes' timeline: every note of the melody, the bass and keyboard 1's melody is on a sphere; none dropped", () =>
        {
            if (expA.Count != MelodyA.Length || expB.Count != BassB.Length || expC.Count != MelodyC.Length) return "expected " + expA.Count + "/" + expB.Count + "/" + expC.Count;
            foreach (var e in expA.Concat(expB)) if (HandOf(s0, e.Key, e.Value) < 0) return "keyboard 0 misses the note at " + F(e.Key) + " key " + e.Value.gridX;
            foreach (var e in expC) if (HandOf(s1, e.Key, e.Value) < 0) return "keyboard 1 misses the note at " + F(e.Key) + " key " + e.Value.gridX;
            if (s0.NoteCount != expA.Count + expB.Count || s1.NoteCount != expC.Count) return "note counts " + s0.NoteCount + " / " + s1.NoteCount;
            if (s0.Dropped != 0 || s1.Dropped != 0) return "dropped " + s0.Dropped + " / " + s1.Dropped;
            return "ok: " + s0.NoteCount + " + " + s1.NoteCount + " notes";
        });
        Run(sb, "one voice per sphere: the bass (lower) on sphere 0, the melody on sphere 1; keyboard 1's melody all on one sphere", () =>
        {
            foreach (var e in expB) if (HandOf(s0, e.Key, e.Value) != 0) return "bass note at " + F(e.Key) + " on sphere " + HandOf(s0, e.Key, e.Value);
            foreach (var e in expA) if (HandOf(s0, e.Key, e.Value) != 1) return "melody note at " + F(e.Key) + " on sphere " + HandOf(s0, e.Key, e.Value);
            var hs = expC.Select(e => HandOf(s1, e.Key, e.Value)).Distinct().ToList();
            if (hs.Count != 1) return "keyboard 1's melody on spheres " + string.Join(",", hs);
            if (s0.Hands[0].Voice != IdB || s0.Hands[1].Voice != IdA) return "voices " + s0.Hands[0].Voice + ", " + s0.Hands[1].Voice;
            return null;
        });
        Run(sb, "colours: a voice's sphere wears its cube's colour (a little toward cream), a spare sphere cream", () =>
        {
            Color want0 = Color.Lerp(cB.Color, Comic.Cream, KeyHands.CreamLift), want1 = Color.Lerp(cA.Color, Comic.Cream, KeyHands.CreamLift);
            if (!Near(s0.Hands[0].Color, want0) || !Near(s0.Hands[1].Color, want1)) return "keyboard 0 colours " + s0.Hands[0].Color + " / " + s0.Hands[1].Color;
            int spare = s1.Hands.Count(h => h.Voice == 0);
            if (spare != s1.Hands.Count - 1) return "keyboard 1 spares " + spare;
            foreach (var h in s1.Hands) if (h.Voice == 0 && !Near(h.Color, KeyHands.SpareColor)) return "a spare sphere is " + h.Color;
            return null;
        });

        // ---------------------------------------------------------------- the bounce (pure)
        var all = expA.Select(e => new { s = s0, e }).Concat(expB.Select(e => new { s = s0, e })).Concat(expC.Select(e => new { s = s1, e })).ToList();
        Run(sb, "a landing ON the sounding key at every onset (pure: Down on the key; drawn: the sphere right over that key, resting on its top), incl. the leap 0 → 24", () =>
        {
            int n = 0; float worst = 0f;
            foreach (var x in all)
            {
                float on = x.e.Key; var key = x.e.Value; int h = HandOf(x.s, on, key);
                var p = x.s.PoseAt(h, on + 1e-3f);
                if (p.phase != KeyHands.Phase.Down || p.key != key) return "at " + F(on) + " key " + key.gridX + ": " + p.phase + (p.key != null ? "@" + p.key.gridX : "");
                if (x.s.Island.KeyAt(p.contact) != key) return "at " + F(on) + ": KeyAt(contact) is not key " + key.gridX;
                x.s.DrawAt(on + 1e-3f);
                var hand = x.s.Hands[h];
                if (x.s.Island.KeyAt(hand.Center) != key) return "at " + F(on) + ": the drawn sphere is not over key " + key.gridX;
                float bottom = Vector3.Dot(hand.Center - key.Top, x.s.Island.transform.up) - hand.Radius * hand.AxisScale;
                worst = Mathf.Max(worst, Mathf.Abs(bottom));
                if (Mathf.Abs(bottom) > 0.06f) return "at " + F(on) + ": the sphere's bottom is " + F(bottom) + " u off the key top";
                n++;
            }
            return "ok: " + n + " onsets, the bottom within " + F(worst) + " u of the key";
        });
        Run(sb, "a hop between two keys: half way it is in the AIR, above both keys, flying to the next key; it stretches along a fast hop", () =>
        {
            int n = 0; float maxStretch = 0f;
            var h1 = s0.Hands[1];
            for (int i = 0; i + 1 < h1.NoteCount; i++)
            {
                float on0, off0, on1, off1; TileInteraction t0, t1; bool w0, w1;
                h1.NoteOf(i, out on0, out off0, out t0, out w0); h1.NoteOf(i + 1, out on1, out off1, out t1, out w1);
                if (w0 || w1 || t0 == t1) continue;
                float mid = on1 - 0.12f * (on1 - on0);   // inside the hop (it takes the last half of the gap at most)
                var p = s0.PoseAt(1, mid);
                if (p.phase != KeyHands.Phase.Air || p.key != t1 || p.from != t0) return "between " + F(on0) + " and " + F(on1) + ": " + p.phase;
                if (Vector3.Dot(p.contact, k0.transform.up) <= Mathf.Max(Vector3.Dot(t0.Top, k0.transform.up), Vector3.Dot(t1.Top, k0.transform.up))) return "the hop at " + F(mid) + " is not above the keys";
                s0.DrawAt(mid);
                maxStretch = Mathf.Max(maxStretch, h1.AxisScale);
                n++;
            }
            if (n < 4) return "only " + n + " hops";
            return maxStretch > 1.05f ? "ok: " + n + " hops, stretched up to × " + F(maxStretch) : "never stretched (× " + F(maxStretch) + ")";
        });
        Run(sb, "after a note: the sphere SITS on the key it played (the key let go: no hold) until it crouches for the next hop", () =>
        {
            var h0 = s0.Hands[0];   // the bass: half notes with gaps
            int n = 0;
            for (int i = 0; i < h0.NoteCount; i++)
            {
                float on, off; TileInteraction t; bool w;
                h0.NoteOf(i, out on, out off, out t, out w);
                if (w) continue;
                var during = s0.PoseAt(0, Mathf.Lerp(on, off, 0.5f));
                if (during.phase != KeyHands.Phase.Down || !during.holds || during.key != t) return "during the note at " + F(on) + ": " + during.phase + " holds " + during.holds;
                float nextOn = i + 1 < h0.NoteCount ? NoteOn(h0, i + 1) : float.PositiveInfinity;
                if (nextOn - off < 0.6f) continue;
                var after = s0.PoseAt(0, off + 0.05f);
                if (after.phase != KeyHands.Phase.Sit || after.holds || after.key != t) return "after the note at " + F(on) + ": " + after.phase + (after.key != null ? "@" + after.key.gridX : "") + " holds " + after.holds;
                n++;
            }
            return n > 0 ? "ok: " + n + " sits" : "no note with a gap after it";
        });

        // ---------------------------------------------------------------- playing
        Frame(k0);
        yield return Wait(0.5f);
        int lt = Synth.LateEvents; GlobalClock.Play(); yield return Wait(0.25f); lateExcused += Synth.LateEvents - lt;
        {
            // a grid note with a gap after it (≥ 1 beat): its key held down while it sounds, back up after
            KeyHands.Hand h0 = null; float bOn = 0f, bOff = 0f, nextOn = 0f; TileInteraction bKey = null;
            foreach (var hh in s0.Hands)
                for (int i = 0; i < hh.NoteCount && h0 == null; i++)
                {
                    float on, off; TileInteraction t; bool w;
                    if (!hh.NoteOf(i, out on, out off, out t, out w) || w || on < 1f) continue;
                    float nx = NoteOn(hh, i + 1);
                    if (nx - off >= 1f) { h0 = hh; bOn = on; bOff = off; bKey = t; nextOn = nx; }
                }
            if (h0 == null) { h0 = s0.Hands[0]; float on, off; TileInteraction t; bool w; h0.NoteOf(FirstUnwrapped(h0), out on, out off, out t, out w); bOn = on; bOff = off; bKey = t; nextOn = NoteOn(h0, FirstUnwrapped(h0) + 1); }
            Info(sb, "held-key note: beat " + F(bOn) + "–" + F(bOff) + " key " + (bKey != null ? bKey.gridX : -1) + ", next " + F(nextOn));
            double ck0 = GlobalClock.SongBeatD; yield return Wait(0.4f);
            clockFrozen = GlobalClock.SongBeatD - ck0 < 0.05;
            if (clockFrozen) Info(sb, "NOT VERIFIED: the audio clock (AudioSettings.dspTime) is frozen — the editor lost its audio output; the playback checks (held key) cannot run");
            yield return SeekPlaying(bOn - 0.5f, 0.05f);
            if (!clockFrozen) yield return UntilBeat(Mathf.Lerp(bOn, bOff, 0.5f));
            float held = SinkOf(bKey);
            var shownDuring = h0.Shown;
            if (captures) yield return ShotExcused("h8_hands_playing.png");
            if (!clockFrozen) yield return UntilBeat(bOff + 0.4f * (float)GlobalClock.BeatsPerSecond);   // 0.4 s after the note: the key back up
            float after = SinkOf(bKey);
            var shownAfter = h0.Shown;
            if (!clockFrozen) Run(sb, "playing: the bass's key is held DOWN under its sphere while the note sounds and back UP after it, the sphere sitting on it", () =>
            {
                if (shownDuring.phase != KeyHands.Phase.Down || shownDuring.key != bKey) return "during: " + shownDuring.phase;
                if (held < 0.02f) return "the key sinks only " + F(held) + " u during the note";
                if (GlobalClock.SongBeatD < nextOn - 0.15f && (shownAfter.phase != KeyHands.Phase.Sit && shownAfter.phase != KeyHands.Phase.Crouch)) return "after: " + shownAfter.phase;
                if (after > 0.01f) return "the key is still " + F(after) + " u down after the note";
                return "ok: down " + F(held) + " u, then " + F(after) + " u";
            });
            // every sphere drawn where the pure pose says (after a seek's glide)
            var fails = new List<string>();
            foreach (var b in new[] { 1.0f, 2.3f, 3.55f, 9.1f, 12.5f })
            {
                yield return SeekPlaying(b, KeyHands.GlideS + 0.15f);
                foreach (var s in new[] { s0, s1 })
                    for (int i = 0; i < s.Hands.Count; i++)
                    {
                        var sh = s.Hands[i].Shown;
                        if (float.IsNaN(sh.on) && sh.phase != KeyHands.Phase.Sit) continue;
                        float beatNow = (float)GlobalClock.SongBeatD;
                        var q = s.PoseAt(i, beatNow);
                        if (q.phase == KeyHands.Phase.Air || sh.phase == KeyHands.Phase.Air) continue;   // a hop in flight: drawn on twos
                        if (q.key != sh.key) fails.Add("seek " + F(b) + " sphere " + i + ": drawn on " + (sh.key != null ? sh.key.gridX.ToString() : "-") + ", pure " + (q.key != null ? q.key.gridX.ToString() : "-"));
                    }
            }
            Run(sb, "seek-safe: after a seek (and its glide) every sphere is on the key its pure pose says", () => fails.Count == 0 ? null : string.Join(" | ", fails.Take(3)));
            // frame cost
            double worst = 0; for (int i = 0; i < 30; i++) { yield return null; worst = Math.Max(worst, KeyHands.MsLast); }
            Run(sb, "frame cost: ≤ 0.3 ms per keyboard on average, ≤ 1.5 ms worst frame (all keyboards)", () =>
                KeyHands.MsPerKeyboard <= 0.3 && worst <= 1.5 ? "ok: " + KeyHands.MsPerKeyboard.ToString("F3") + " ms per keyboard, worst " + worst.ToString("F3") + " ms" : "per keyboard " + KeyHands.MsPerKeyboard.ToString("F3") + " ms, worst " + worst.ToString("F3"));
        }
        // stop: every sphere stays on a key, no jump
        {
            var pre = KeyHands.Sets.SelectMany(s => s.Hands).Select(h => h.Center).ToList();
            GlobalClock.Stop();
            yield return null;
            var post = KeyHands.Sets.SelectMany(s => s.Hands).Select(h => h.Center).ToList();
            float jump = 0f; for (int i = 0; i < Mathf.Min(pre.Count, post.Count); i++) jump = Mathf.Max(jump, Vector3.Distance(pre[i], post[i]));
            yield return Wait(KeyHands.GlideS + 0.3f);
            Run(sb, "stopped: every sphere SITS on a key (the last one it played) without a jump", () =>
            {
                foreach (var s in KeyHands.Sets)
                    foreach (var h in s.Hands)
                    {
                        if (h.Shown.phase != KeyHands.Phase.Sit || h.Key == null || h.Key != h.RestKey) return "a sphere is " + h.Shown.phase;
                        if (s.Island.KeyAt(h.Center) != h.Key) return "a sphere is not over its key";
                    }
                return jump <= s0.Radius * 1.5f ? "ok: the first frame moved ≤ " + F(jump) + " u" : "jumped " + F(jump) + " u on stop";
            });
            if (captures) yield return Shot("h8_hands_rest.png");
        }

        // ---------------------------------------------------------------- an empty-hand press: a sphere goes there and stays
        {
            var pm = PathManager.I;
            Frame(k1, 1f);
            yield return Wait(0.5f);
            TileInteraction key = null; Vector3 sp = Vector3.zero, q;
            foreach (int k in new[] { 16, 9, 11, 0, 7 })
            {
                var t = k1.GetTile(k, 0);
                if (t != null && !s1.Hands.Any(h => h.Key == t) && PickPoint(t, out q)) { key = t; sp = q; break; }
            }
            int lp0 = KeyHands.LivePresses;
            if (pm != null && key != null)
            {
                pm.PutDown();
                PathManager.SimOnly = true;
                yield return Click(sp);
                yield return Wait(KeyHands.LiveHopS + KeyHands.LiveHoldS + 0.25f);
                PathManager.SimOnly = false;
            }
            Run(sb, "an empty-hand press of a key: a sphere hops there, presses it and STAYS sitting on it", () =>
            {
                if (key == null) return "no pickable free key";
                if (KeyHands.LivePresses <= lp0) return "no live press";
                var h = s1.Hands.FirstOrDefault(x => x.Key == key);
                if (h == null) return "no sphere on key " + key.gridX + " (" + string.Join(",", s1.Hands.Select(x => x.Key != null ? x.Key.gridX.ToString() : "-")) + ")";
                if (h.Shown.phase != KeyHands.Phase.Sit || h.RestKey != key) return "the sphere is " + h.Shown.phase;
                return "ok: key " + key.gridX;
            });
        }

        // ---------------------------------------------------------------- the chord root mark; the melody card
        Run(sb, "root keys: keyboard 0 (column 0, C) marks its C keys (ring + wash) and no E / G key; keyboard 1 (column 2, G) its G keys", () =>
        {
            foreach (var x in new[] { new { kb = k0, root = 0 }, new { kb = k1, root = 7 } })
            {
                int marked = 0;
                for (int k = 0; k < x.kb.tiles.Count; k++)
                {
                    var t = x.kb.GetTile(k, 0); if (t == null) continue;
                    int pc = ((t.midi % 12) + 12) % 12;
                    bool want = pc == x.root;
                    if (x.kb.KeyRootShown(k) != want) return "keyboard col " + x.kb.column + " key " + k + " (pc " + pc + ") root mark " + x.kb.KeyRootShown(k);
                    if (want) marked++;
                    if (!want && x.kb.KeyTensionShown(k) == (pc == (x.root + 4) % 12 || pc == (x.root + 7) % 12) && x.kb.KeyDotShown(k)) return "key " + k + " (pc " + pc + "): a chord tone / tension mix-up";
                }
                if (marked < 2) return "only " + marked + " root keys marked";
            }
            return null;
        });
        Run(sb, "the melody notes (v8): over C in C major the dots cover C D E G A B (the 9th, 13th and maj7 too) and never F (the avoid note); D A B wear the smaller tension dot", () =>
        {
            var want = new[] { 0, 2, 4, 7, 9, 11 }; var tens = new[] { 2, 9, 11 };
            for (int k = 0; k < k0.tiles.Count; k++)
            {
                var t = k0.GetTile(k, 0); if (t == null) continue;
                int pc = ((t.midi % 12) + 12) % 12;
                if (k0.KeyDotShown(k) != want.Contains(pc)) return "key " + k + " (pc " + pc + ") dot " + k0.KeyDotShown(k);
                if (k0.KeyDotShown(k) && k0.KeyTensionShown(k) != tens.Contains(pc)) return "key " + k + " (pc " + pc + ") tension " + k0.KeyTensionShown(k);
            }
            return null;
        });
        Run(sb, "the melody card is retired from the tray (hidden); SimTool(6) still adds a phrase for the v7 suites", () =>
        {
            var tray = IslandTray.I;
            if (tray == null) return "ok: no tray built (nothing to show)";
            var card = tray.MelodyCard;
            if (card == null) return "ok: no melody card";
            return card.gameObject.activeSelf ? "the melody card shows" : null;
        });

        // ---------------------------------------------------------------- v8: the stage — a keyboard's part pops in from the sea and sinks after it
        {
            KeyHands.RefreshNow();
            var parts = KeyStage.PartsOf(k1);
            float p0 = parts.Count > 0 ? parts[0].x : -1f, p1 = parts.Count > 0 ? parts[0].y : -1f;
            float bps = (float)GlobalClock.BeatsPerSecond, popB = KeyStage.PopS * bps;
            Run(sb, "the stage: keyboard 1 plays one PART (its column's notes) with a gap in the loop → the stage drives it; pure depth: under before, popped (with an overshoot) by its lead, up through it, under after", () =>
            {
                if (!KeyStage.Drives(k1)) return "not driven (parts " + parts.Count + ")";
                if (parts.Count != 1) return parts.Count + " parts";
                float before = KeyStage.DepthAt(k1, p0 - KeyStage.LeadBeats - popB - 0.3f), lead = KeyStage.DepthAt(k1, p0 - KeyStage.LeadBeats + 0.01f), mid = KeyStage.DepthAt(k1, (p0 + p1) * 0.5f);
                float after = KeyStage.DepthAt(k1, p1 + KeyStage.TailBeats + KeyStage.SinkS * bps + 0.2f), over = 1f;
                for (float b = p0 - KeyStage.LeadBeats - popB; b < p0 - KeyStage.LeadBeats; b += 0.01f) over = Mathf.Min(over, KeyStage.DepthAt(k1, b));
                if (before < 0.999f || Mathf.Abs(lead) > 1e-3f || Mathf.Abs(mid) > 1e-3f || after < 0.999f) return "depth before " + F(before) + ", at the lead " + F(lead) + ", mid " + F(mid) + ", after " + F(after);
                if (over > -0.02f) return "no overshoot on the pop (min " + F(over) + ")";
                return "ok: part " + F(p0) + "–" + F(p1) + ", overshoot " + F(over);
            });
            // playing: under outside its part, up in it (after the ease), no clicks while under, a pop counted on the way in
            int pops0 = KeyStage.Pops;
            GlobalClock.Play(); yield return SeekPlaying(1.0, KeyStage.EaseS + 0.25f);   // SeekPlaying counts its own catch-up apart
            float dUnder = k1.PartDepth; var kc = k1.GetTile(9, 0) != null ? k1.GetTile(9, 0).GetComponent<Collider>() : null; bool pickUnder = kc != null && kc.enabled;
            bool clock = !clockFrozen;
            if (clock) { yield return SeekPlaying(p0 - KeyStage.LeadBeats - popB - 1.0f, 0.1f); yield return UntilBeat((p0 + p1) * 0.5f); }
            else yield return SeekPlaying((p0 + p1) * 0.5f, KeyStage.EaseS + 0.25f);
            float dUp = k1.PartDepth; bool pickUp = kc != null && kc.enabled; int popped = KeyStage.Pops - pops0;
            if (captures) yield return ShotExcused("h8_stage_up.png");
            GlobalClock.Stop();
            yield return Wait(KeyStage.EaseS + 0.3f);
            float dStopped = k1.PartDepth;
            Run(sb, "playing, keyboard 1 is UNDER the sea outside its part (no clicks) and UP in it" + (clock ? ", popping in on the way (a splash)" : "") + "; stopped, every keyboard is up", () =>
            {
                if (dUnder < 0.99f || pickUnder) return "outside its part: depth " + F(dUnder) + ", clickable " + pickUnder;
                if (Mathf.Abs(dUp) > 0.01f || !pickUp) return "in its part: depth " + F(dUp) + ", clickable " + pickUp;
                if (clock && popped < 1) return "no pop counted on the way in";
                if (Mathf.Abs(dStopped) > 0.01f) return "stopped: depth " + F(dStopped);
                return "ok" + (clock ? ": " + popped + " pop(s)" : " (seeked: the audio clock is frozen)");
            });
        }
        // the sea API (any island kind): HideUnderSea / PopIn / SinkOut / SurfaceNow / the SeaDepth pose input
        {
            var isl = SM.Islands.FirstOrDefault(k => k != null && k.kind == 0);
            float y0 = isl != null ? isl.transform.position.y : 0f;
            string why = null;
            if (isl != null)
            {
                isl.HideUnderSea(); yield return null;
                if (!isl.UnderSea || Mathf.Abs(isl.SeaDrop - isl.HideDepth) > 0.05f || isl.HideDepth < KeyBlock.SeaHide) why = "HideUnderSea: under " + isl.UnderSea + ", drop " + F(isl.SeaDrop) + " (hide depth " + F(isl.HideDepth) + ")";
                else
                {
                    // ALL of it under the surface (its tallest tiles too): the highest point of its renderers
                    float topY = float.MinValue; foreach (var r in isl.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) topY = Mathf.Max(topY, r.bounds.max.y);
                    if (topY > KeyBlock.SeaSurfaceY - 0.1f) why = "HideUnderSea: its top pokes out at y " + F(topY) + " (the sea at " + F(KeyBlock.SeaSurfaceY) + ")";
                    if (why == null && isl.GetComponentsInChildren<Collider>().Any(c => c.enabled)) why = "HideUnderSea left colliders on";
                }   // (it lists going under: the root's y is not the drop)
                isl.PopIn(0.4f); yield return Wait(0.6f);
                if (why == null && (isl.UnderSea || Mathf.Abs(isl.transform.position.y - y0) > 0.02f || !isl.GetComponentsInChildren<Collider>().Any(c => c.enabled))) why = "PopIn: y " + F(isl.transform.position.y) + " (want " + F(y0) + ")";
                isl.SinkOut(0.4f); yield return Wait(0.6f);
                if (why == null && !isl.UnderSea) why = "SinkOut: not under";
                isl.SurfaceNow(); yield return null;
                if (why == null && (isl.UnderSea || Mathf.Abs(isl.transform.position.y - y0) > 0.02f)) why = "SurfaceNow: y " + F(isl.transform.position.y);
                isl.SeaDepth = 1f; yield return null;
                float yd = y0 - isl.SeaDrop;
                isl.SeaDepth = 0f; yield return null;
                if (why == null && (Mathf.Abs(yd - (y0 - isl.HideDepth)) > 0.05f || Mathf.Abs(isl.transform.position.y - y0) > 0.02f)) why = "SeaDepth: 1 → y " + F(yd) + ", 0 → " + F(isl.transform.position.y);
            }
            Run(sb, "the sea API on a chord island: HideUnderSea (instant, no clicks), PopIn, SinkOut, SurfaceNow and the SeaDepth pose input", () => isl == null ? "no chord island" : why);
        }

        // the sink LISTS like a boat and the water shows it (waves, foam, a splash on the way back up); level again in its place
        {
            int w0 = KeyBlock.WaterWaves, f0 = KeyBlock.WaterFoams, sp0 = KeyBlock.WaterSplashes;
            float maxTilt = 0f;
            k0.SinkOut(0.8f);
            for (float t = 0f; t < 1.0f; t += Time.unscaledDeltaTime) { maxTilt = Mathf.Max(maxTilt, Vector3.Angle(k0.transform.up, Vector3.up)); yield return null; }
            if (captures) { k0.SurfaceNow(); k0.SeaDepth = 0.45f; }
            if (captures) { Frame(k0, 0.9f); yield return Wait(0.8f); yield return Shot("h8_sink_list.png"); k0.SeaDepth = 0f; k0.HideUnderSea(); }
            int waves = KeyBlock.WaterWaves - w0, foams = KeyBlock.WaterFoams - f0;
            k0.PopIn(0.6f);
            yield return Wait(0.9f);
            int splashes = KeyBlock.WaterSplashes - sp0;
            float levelAfter = Vector3.Angle(k0.transform.up, Vector3.up);
            Run(sb, "sinking, a keyboard LISTS like a boat (up to ListDeg / RollDeg, not straight down) with ring waves off its edges, foam where it goes under, a splash coming back up, level again in its place", () =>
            {
                if (maxTilt < 3f) return "it went down level (max tilt " + F(maxTilt) + "°)";
                if (maxTilt > KeyBlock.ListDeg + KeyBlock.RollDeg + 1f) return "it tilted " + F(maxTilt) + "°";
                if (waves < 3) return "only " + waves + " waves";
                if (foams < 1) return "no foam as it went under";
                if (splashes < 1) return "no splash on the way up";
                if (levelAfter > 0.1f || k0.UnderSea) return "after the pop: tilt " + F(levelAfter) + "°, under " + k0.UnderSea;
                return "ok: tilt up to " + F(maxTilt) + "°, " + waves + " waves, foam " + foams + ", splash " + splashes;
            });
        }

        // ---------------------------------------------------------------- v8: taller keys again; keys dip on hover
        Run(sb, "taller keys (\"almost how it was before\"): white keys reach down to the case, black keys BlackKeyRaise above them, the old gentle rise (≤ KeyRiseTotalMax)", () =>
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < k0.tiles.Count; k++)
            {
                var t = k0.GetTile(k, 0); if (t == null) continue;
                var bc = t.GetComponent<BoxCollider>(); if (bc == null) return "key " + k + " has no collider";
                float top = k0.transform.InverseTransformPoint(t.Top).y;
                float want = t.IsBlackKey ? ProjectConfig.TileThickness : top - KeyBlock.KeyFloorOf(k, k0.KeyCount);
                if (Mathf.Abs(bc.size.y - want) > 1e-3f) return "key " + k + " is " + F(bc.size.y) + " tall (want " + F(want) + ")";
                if (!t.IsBlackKey) { lo = Mathf.Min(lo, top); hi = Mathf.Max(hi, top); }
            }
            float wantRise = KeyBlock.KeyRiseOf(k0.KeyCount) * (k0.KeyCount - 1);   // 0.02 a semitone, the whole rise capped at KeyRiseTotalMax
            if (Mathf.Abs(hi - lo - wantRise) > 0.02f || hi - lo > ProjectConfig.KeyRiseTotalMax + 0.01f) return "the keys rise " + F(hi - lo) + " (want " + F(wantRise) + ")";
            return "ok: white keys " + F(lo) + " … " + F(hi) + " u tall";
        });
        {
            var key = k0.GetTile(9, 0);
            float before = key != null ? key.transform.localPosition.y : 0f;
            if (key != null) key.SetHover(true);
            yield return Wait(0.5f);
            float during = key != null ? key.transform.localPosition.y : 0f;
            if (key != null) key.SetHover(false);
            yield return Wait(0.5f);
            Run(sb, "hover: a keyboard key DIPS under the pointer (KeyHoverDip) and comes back up after", () =>
                key == null ? "no key" : during < before - TileInteraction.KeyHoverDip * 0.8f && Mathf.Abs(key.transform.localPosition.y - before) < 1e-3f ? null
                    : "rest " + F(before) + ", hovered " + F(during) + ", after " + F(key.transform.localPosition.y));
        }

        // ---------------------------------------------------------------- v8: +octave widens the piano; the next one is an organ's upper manual
        {
            float w0 = k1.Width, kw0 = k1.KeyWidthOf(0), d0 = k1.Depth;
            int n0 = k1.KeyCount;
            bool up1 = SM.ExtendKeysLive(k1, 1);
            yield return Wait(0.6f);
            float w1 = k1.Width, kw1 = k1.KeyWidthOf(0);
            var col2 = SM.ColumnWidth(k1.column);
            Run(sb, "+1 octave (25 → 37 keys): the piano gets WIDER at the same key size (its column makes room), not narrower keys", () =>
            {
                if (!up1 || k1.KeyCount != n0 + 12) return "extend " + up1 + ", keys " + k1.KeyCount;
                if (w1 < w0 * 1.3f) return "width " + F(w0) + " → " + F(w1);
                if (Mathf.Abs(kw1 - kw0) > 0.03f) return "key width " + F(kw0) + " → " + F(kw1);
                if (col2 < w1 - 1e-3f) return "column width " + F(col2) + " < the piano " + F(w1);
                var b0 = k0.WorldBounds; var b1 = k1.WorldBounds;
                foreach (var o in SM.Islands) if (o != null && o != k1 && o.WorldBounds.Intersects(b1) && Mathf.Abs(o.WorldBounds.center.z - b1.center.z) < 0.5f * (o.Depth + k1.Depth) - 0.1f && o.column != k1.column)
                    return "the wide piano overlaps island " + SM.Islands.IndexOf(o) + " (column " + o.column + ")";
                return "ok: " + F(w0) + " → " + F(w1) + " u wide, keys " + F(kw1) + " u";
            });
            bool up2 = SM.ExtendKeysLive(k1, 1);
            yield return Wait(0.6f);
            Frame(k1, 0.9f);
            yield return Wait(0.8f);
            if (captures) yield return Shot("h8_organ.png");
            Run(sb, "+2 octaves (49 keys): an ORGAN — the extra keys on an upper manual behind, a tier up, the same key size; the width stays", () =>
            {
                int n = k1.KeyCount;
                if (!up2 || n != n0 + 24) return "extend " + up2 + ", keys " + n;
                if (Mathf.Abs(k1.Width - w1) > 1e-3f) return "width " + F(w1) + " → " + F(k1.Width);
                if (k1.Depth <= d0 + 0.5f) return "depth " + F(d0) + " → " + F(k1.Depth);
                var t = k1.transform; int upper = 0;
                float frontLow = float.MinValue, frontUp = float.MaxValue, yLow = float.MinValue, yUp = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    var key = k1.GetTile(k, 0); if (key == null || key.IsBlackKey) continue;
                    var l = t.InverseTransformPoint(key.Top);
                    if (KeyBlock.OnUpperManual(k, n)) { upper++; frontUp = Mathf.Min(frontUp, l.z); yUp = Mathf.Min(yUp, l.y); }
                    else { frontLow = Mathf.Max(frontLow, l.z); yLow = Mathf.Max(yLow, l.y); }
                    if (Mathf.Abs(k1.KeyWidthOf(k) - kw1) > 0.03f) return "key " + k + " is " + F(k1.KeyWidthOf(k)) + " wide";
                }
                if (upper < 6) return upper + " white keys on the upper manual";
                if (frontUp <= frontLow) return "the upper manual is not behind the lower one";
                if (yUp < yLow + KeyBlock.TierRaise * 0.5f) return "the upper manual is not raised (" + F(yUp) + " vs " + F(yLow) + ")";
                if (k1.transform.Find("OrganStep") == null) return "no riser under the upper manual";
                return "ok: " + upper + " white keys up a tier, depth " + F(d0) + " → " + F(k1.Depth);
            });
            SM.ShrinkKeysLive(k1, 1); SM.ShrinkKeysLive(k1, 1);
            yield return Wait(0.6f);
            Run(sb, "back to 25 keys: the piano's width and depth return", () => k1.KeyCount == n0 && Mathf.Abs(k1.Width - w0) < 1e-3f && Mathf.Abs(k1.Depth - d0) < 1e-3f ? null
                : "keys " + k1.KeyCount + ", width " + F(k1.Width) + ", depth " + F(k1.Depth));
        }

        // ---------------------------------------------------------------- v8: a repeating keyboard: dragged at a constant speed past its checkpoints
        {
            var st = TestSong();
            var km = st.measures.First(m => m.kind == 2 && m.col == 2); km.repeat = 3;
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            var kr = Keyboards().FirstOrDefault(k => k.column == 2);
            var belt = kr != null ? kr.Belt : null;
            Frame(kr, 0.8f);
            yield return Wait(0.8f);
            Vector3[] c0 = belt != null ? Enumerable.Range(0, 3).Select(i => belt.CheckpointCenter(i)).ToArray() : new Vector3[0];
            Run(sb, "a keyboard repeated 3× gets 3 CHECKPOINTS (shallow circles on the floor one slot apart) and no rubber belt", () =>
            {
                if (kr == null) return "no keyboard on column 2";
                if (belt == null || !belt.Checkpoints) return "no checkpoints (belt " + (belt != null) + ")";
                if (belt.CheckpointCount != 3) return belt.CheckpointCount + " circles";
                if (belt.transform.Find("Body") != null) return "a rubber belt body exists";
                for (int i = 0; i < 3; i++) if (Mathf.Abs(c0[i].y - belt.CheckpointY) > 0.01f || c0[i].y >= kr.transform.position.y) return "circle " + i + " at y " + F(c0[i].y);
                for (int i = 1; i < 3; i++) if (Mathf.Abs(c0[i].x - c0[i - 1].x - kr.BeltPitch) > 0.01f) return "circles " + (i - 1) + "→" + i + " " + F(c0[i].x - c0[i - 1].x) + " apart";
                return "ok: circles at x " + F(c0[0].x) + ", " + F(c0[1].x) + ", " + F(c0[2].x);
            });
            // pure: a constant speed through the middle of its turn, the first circle at its start, the last as it ends
            int col = kr != null ? kr.column : 0;
            double cs0 = SM.ColumnStart(col), pl = SM.PassLength(col), end = cs0 + 3 * pl;
            string pureWhy = null;
            if (kr != null)
            {
                GlobalClock.Stop(); GlobalClock.Seek(cs0 + 0.5 * pl);
                float l;
                float a = kr.KeyboardRideSlot(cs0 + 0.3 * (end - cs0), out l), b = kr.KeyboardRideSlot(cs0 + 0.5 * (end - cs0), out l), c = kr.KeyboardRideSlot(cs0 + 0.7 * (end - cs0), out l);
                float atPass = kr.KeyboardRideSlot(cs0 + pl, out l), start = kr.KeyboardRideSlot(cs0 + 1e-3, out l), last = kr.KeyboardRideSlot(end - 1e-3, out l);
                if (Mathf.Abs((b - a) - (c - b)) > 1e-3f) pureWhy = "not a constant speed: " + F(a) + ", " + F(b) + ", " + F(c);
                else if (atPass < 0.2f || atPass > 0.95f) pureWhy = "at the first pass boundary it stands at slot " + F(atPass) + " (a hop, not a drag)";
                else if (start > 0.01f || last < 1.98f) pureWhy = "start " + F(start) + ", end " + F(last);
            }
            Run(sb, "the drag is at a CONSTANT speed across its turn (no hop at each measure): from the first circle at its start to the last as it ends", () => kr == null ? "no keyboard" : pureWhy);
            // drawn: step the beat through the middle of the turn (stopped: the ride is a pure function of the beat)
            float minYaw = 0f, maxYaw = -99f, drift = 0f; string driftWhy = ""; int ripples0 = Belt.WakeRipples; bool shot = false;
            if (kr != null && belt != null)
            {
                GlobalClock.Seek(cs0 + 0.3 * (end - cs0));
                yield return Wait(0.6f);
                for (int i = 0; i < 3; i++) c0[i] = belt.CheckpointCenter(i);
                for (double bt = cs0 + 0.3 * (end - cs0); bt < cs0 + 0.7 * (end - cs0); bt += 0.03)
                {
                    GlobalClock.Seek(bt);
                    yield return null;
                    minYaw = Mathf.Min(minYaw, kr.DragYawShown); maxYaw = Mathf.Max(maxYaw, kr.DragYawShown);
                    for (int i = 0; i < 3; i++) { float dd = Vector3.Distance(belt.CheckpointCenter(i), c0[i]); if (dd > drift) { drift = dd; driftWhy = "circle " + i + " Δ " + (belt.CheckpointCenter(i) - c0[i]).ToString("F3"); } }
                    if (captures && !shot && bt > cs0 + 0.5 * (end - cs0)) { shot = true; yield return Shot("h8_checkpoint_drag.png"); }
                }
            }
            Run(sb, "dragged, it LEANS counter-clockwise with a gentle sway (never past SwayBase + SwayAmp); the circles never move", () =>
            {
                if (kr == null || belt == null) return "no checkpoints";
                if (minYaw > -1.5f) return "it never leaned CCW (min " + F(minYaw) + "°)";
                if (minYaw < -(Belt.SwayBase + Belt.SwayAmp) - 0.3f) return "it leaned " + F(minYaw) + "°";
                if (maxYaw - minYaw < 0.4f) return "no sway (" + F(minYaw) + "° … " + F(maxYaw) + "°)";
                if (drift > 1e-3f) return "the circles moved " + F(drift) + " u (" + driftWhy + ")";
                return "ok: " + F(minYaw) + "° … " + F(maxYaw) + "°";
            });
            // the wake: playing (it needs the audio clock); else noted
            if (!clockFrozen && kr != null)
            {
                int lt2 = Synth.LateEvents; GlobalClock.Play(); GlobalClock.Seek(cs0 + 0.4 * (end - cs0)); yield return Wait(1.2f); lateExcused += Synth.LateEvents - lt2;
                int made = Belt.WakeRipples - ripples0;
                GlobalClock.Stop();
                Run(sb, "playing, the dragged keyboard leaves a WAKE (ripples round its hull)", () => made >= 4 ? "ok: " + made + " ripples in 1.2 s" : "only " + made + " ripples");
            }
            else Info(sb, "NOT VERIFIED: the wake (it needs the audio clock)");
            GlobalClock.Seek(0.0);
            yield return Wait(0.6f);
        }

        // ---------------------------------------------------------------- v8: stage RUNS — a gallery song's lanes (one keyboard per voice per column)
        {
            var m = new List<MeasureState>();
            var cubes = new List<CubeState>();
            int id = 8400;
            System.Func<int, float, int, int> addKeys = (col, pz, inst) =>
            {
                m.Add(new MeasureState { chordKey = "Keys", root = 48, semis = new[] { 0 }, bars = 1, kind = 2, col = col, repeat = 1, energy = 2, pz = pz, placed = true, keyCount = 25 });
                cubes.Add(Cube(id++, inst, m.Count - 1, new[] { 12, 14, 16, 19 }, 24));
                return m.Count - 1;
            };
            for (int c = 0; c < 4; c++) m.Add(Chord(c, "C", 60, new[] { 0, 4, 7 }));
            for (int c = 0; c < 4; c++) addKeys(c, 5f, 8);   // lane A: the piano, every column
            addKeys(0, 10f, 5);                                // lane B: strings in column 0 …
            addKeys(1, 10f, 2); addKeys(2, 10f, 2);            // … then an e.piano run over columns 1–2
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 runs", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes.ToArray(), moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            KeyHands.RefreshNow();
            yield return Frames(2);
            var rkbs = Keyboards();
            KeyBlock A1 = rkbs.FirstOrDefault(k => k.column == 1 && k.pz < 7f), B0 = rkbs.FirstOrDefault(k => k.column == 0 && k.pz > 7f);
            KeyBlock B1 = rkbs.FirstOrDefault(k => k.column == 1 && k.pz > 7f), B2 = rkbs.FirstOrDefault(k => k.column == 2 && k.pz > 7f);
            Run(sb, "stage RUNS: a lane's keyboards in consecutive columns with the same voice are one part — the piano lane (every column) never sinks; the e.piano run (columns 1–2) rises once and sinks once; the strings before it are their own part", () =>
            {
                if (A1 == null || B0 == null || B1 == null || B2 == null) return "keyboards missing (" + rkbs.Count + ")";
                if (KeyStage.RunMembers(A1).Count != 4) return "the piano lane's run has " + KeyStage.RunMembers(A1).Count + " keyboards";
                if (KeyStage.Drives(A1)) return "the piano lane is driven (it would sink between sections)";
                var rb = KeyStage.RunMembers(B1);
                if (rb.Count != 2 || !rb.Contains(B2) || rb.Contains(B0)) return "the e.piano run: " + rb.Count + " keyboards" + (rb.Contains(B0) ? " incl. the strings" : "");
                if (!KeyStage.Drives(B1) || !KeyStage.Drives(B2)) return "the e.piano run is not driven";
                float bpb = 4f;
                float b2InCol1 = KeyStage.DepthAt(B2, 1.5f * bpb), b1InCol2 = KeyStage.DepthAt(B1, 2.5f * bpb), b1InCol3 = KeyStage.DepthAt(B1, 3.7f * bpb), b1InCol0 = KeyStage.DepthAt(B1, 0.3f * bpb);
                if (Mathf.Abs(b2InCol1) > 1e-3f || Mathf.Abs(b1InCol2) > 1e-3f) return "not up across the run: column 2's keyboard during column 1 " + F(b2InCol1) + ", column 1's during column 2 " + F(b1InCol2);
                if (b1InCol3 < 0.999f || b1InCol0 < 0.999f) return "not under outside the run: column 3 " + F(b1InCol3) + ", column 0 " + F(b1InCol0);
                return "ok: piano lane run of 4 (never driven), e.piano run of 2";
            });
        }

        // ---------------------------------------------------------------- v8: a long keyboard keeps normal key sizes, centred in its column
        {
            var st = TestSong();
            var km = st.measures.First(mm => mm.kind == 2 && mm.col == 2); km.bars = 4;
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            var kl = Keyboards().FirstOrDefault(k => k.column == 2);
            var k1b = Keyboards().FirstOrDefault(k => k.column == 0);
            if (captures && kl != null) { Frame(kl, 0.9f); yield return Wait(0.8f); yield return Shot("h8_long_keyboard.png"); }
            Run(sb, "a 4-bar keyboard: the layout reserves the column, its keys keep a 1-bar keyboard's size and sit centred; v9: its platform and case HUG the keys (no long bench), its spheres sit on them", () =>
            {
                if (kl == null || k1b == null) return "keyboards missing";
                if (kl.bars != 4 || Mathf.Abs(kl.Width - 4f * KeyBlock.IslandWidth) > 1e-3f) return "bars " + kl.bars + ", width " + F(kl.Width);
                if (Mathf.Abs(kl.KeyWidthOf(0) - k1b.KeyWidthOf(0)) > 0.03f) return "key width " + F(kl.KeyWidthOf(0)) + " vs a 1-bar keyboard's " + F(k1b.KeyWidthOf(0));
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < kl.tiles.Count; k++) { var t = kl.GetTile(k, 0); if (t == null) continue; float x = kl.transform.InverseTransformPoint(t.Top).x; lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                float mid = -KeyBlock.EdgeInset + kl.Width * 0.5f;
                if (Mathf.Abs((lo + hi) * 0.5f - mid) > 0.2f) return "keys centred at " + F((lo + hi) * 0.5f) + ", the column's middle " + F(mid);
                var cheekR = kl.transform.Find("CheekR");
                if (cheekR == null || cheekR.localPosition.x > -KeyBlock.EdgeInset + kl.Width * 0.75f) return "the case does not hug the keys (right cheek at " + (cheekR != null ? F(cheekR.localPosition.x) : "-") + ")";
                var plat = kl.transform.Find("Platform"); var pm = plat != null ? plat.GetComponent<MeshFilter>() : null;
                float pw = pm != null && pm.sharedMesh != null ? pm.sharedMesh.bounds.size.x : -1f;
                if (Mathf.Abs(pw - kl.KeysCaseWidth) > 0.05f || pw > 1.6f * KeyBlock.IslandWidth) return "the platform is " + F(pw) + " wide (the case " + F(kl.KeysCaseWidth) + ", the column " + F(kl.Width) + ")";
                if (plat != null && Mathf.Abs(plat.localPosition.x - (-KeyBlock.EdgeInset + kl.Width * 0.5f)) > 0.05f) return "the platform is not centred";
                var hs = KeyHands.Of(kl);
                if (hs == null || hs.Radius > KeyHands.RMax + 1e-3f || hs.Radius > 0.3f) return "sphere radius " + (hs != null ? F(hs.Radius) : "-");
                return "ok: keys " + F(lo) + " … " + F(hi) + " in a " + F(kl.Width) + " u column";
            });
        }

        // ---------------------------------------------------------------- v9: a repeated column of grids shorter than it (per-grid measures)
        {
            var m = new List<MeasureState>();
            m.Add(new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7 }, bars = 4, kind = 0, col = 0, repeat = 2, energy = 2, pz = 0f, placed = true });
            for (int b = 0; b < 4; b++) m.Add(new MeasureState { chordKey = "C", root = 48, semis = new[] { 0, 4, 7 }, bars = 1, kind = 0, col = 0, repeat = 2, energy = 2, pz = 10f, placed = true, barOffset = b });
            m.Add(new MeasureState { chordKey = "G", root = 67, semis = new[] { 0, 4, 7 }, bars = 1, kind = 0, col = 1, repeat = 1, energy = 2, pz = 0f, placed = true });
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 belt rows", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = new CubeState[0], moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            yield return Wait(0.7f);   // the belts settle on their rows
            var shorts = SM.Islands.Where(k => k != null && k.column == 0 && k.pz > 5f).OrderBy(k => k.barOffset).ToList();
            var big = SM.Islands.FirstOrDefault(k => k != null && k.column == 0 && k.pz < 5f);
            float colW = SM.ColumnWidth(0);
            string pre = null;
            if (shorts.Count != 4 || big == null) pre = "islands: " + shorts.Count + " short, big " + (big != null);
            else
            {
                foreach (var k in shorts) if (Mathf.Abs(k.BeltPitch - (colW + ProjectConfig.BeltGap)) > 1e-3f) { pre = "grid at offset " + k.barOffset + ": slot " + F(k.BeltPitch) + " (the column's " + F(colW + ProjectConfig.BeltGap) + ")"; break; }
                int drawn = shorts.Count(k => k.Belt != null && k.Belt.Drawn);
                if (pre == null && (drawn != 1 || shorts[0].Belt == null || !shorts[0].Belt.Drawn)) pre = drawn + " short grids draw a belt (want only the first)";
            }
            float[] x0 = shorts.Select(k => k.transform.position.x).ToArray(); float bx0 = big != null ? big.transform.position.x : 0f;
            double pass2 = SM.ColumnStart(0) + SM.PassLength(0) * 1.5;
            GlobalClock.Seek(pass2); yield return Wait(1.0f);
            string ride = null;
            if (pre == null)
            {
                float want = colW + ProjectConfig.BeltGap, bd = big.transform.position.x - bx0;
                for (int i = 0; i < shorts.Count; i++) { float d = shorts[i].transform.position.x - x0[i]; if (Mathf.Abs(d - want) > 0.05f || Mathf.Abs(d - bd) > 0.05f) { ride = "pass 2: the grid at offset " + shorts[i].barOffset + " moved " + F(d) + " (the 4-bar grid " + F(bd) + ", want " + F(want) + ")"; break; } }
            }
            if (captures) { OrbitCamera.I.FrameBounds(big != null ? big.FootprintBounds : new Bounds(), 0.1f, true, 1f); yield return Wait(0.8f); yield return Shot("h8_belt_rows.png"); }
            GlobalClock.Seek(0.0); yield return Wait(0.5f);
            Run(sb, "a repeated 4-bar column with four 1-bar grids side by side: every grid rides the COLUMN's slot (pass 2: all one column on, with the 4-bar grid), one belt drawn under the row", () => pre ?? ride);
        }

        // ---------------------------------------------------------------- v9: FRAGMENTS — a short grid in a longer column pops in for its measures
        {
            var m = new List<MeasureState>();
            var cubes = new List<CubeState>();
            int id = 8500;
            System.Func<int, int, int, float, int, int> grid = (col, bars, off, pz, inst) =>
            {
                m.Add(new MeasureState { chordKey = "C", root = 48, semis = new[] { 0, 4, 7 }, bars = bars, kind = 0, col = col, repeat = 1, energy = 2, pz = pz, placed = true, barOffset = off });
                if (inst >= 0) cubes.Add(Cube(id++, inst, m.Count - 1, new[] { 0, 1, 2, 3 }, 24));
                return m.Count - 1;
            };
            for (int c = 0; c < 3; c++) grid(c, 4, 0, 0f, 33);   // lane 0: a bass grid in every column (never pops)
            grid(0, 1, 1, 10f, 5);                               // lane 10: a 1-bar fragment on column 0's 2nd measure
            grid(0, 1, 2, 20f, -1);                              // lane 20: an EMPTY fragment (stays up)
            grid(1, 1, 3, 10f, 2);                               // lane 10: a fragment on column 1's last measure …
            grid(2, 4, 0, 10f, 2);                               // … leading into a full grid of the same voice
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 fragments", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes.ToArray(), moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            System.Func<int, float, KeyBlock> at = (col, pz) => SM.Islands.FirstOrDefault(k => k != null && k.column == col && Mathf.Abs(k.pz - pz) < 1f);
            KeyBlock bass = at(0, 0f), fa = at(0, 10f), fe = at(0, 20f), fb = at(1, 10f), full = at(2, 10f);
            Run(sb, "v9 FRAGMENTS: a short grid in a longer column pops up from the sea for its measures and sinks after them; one leading into a full grid of the same voice rises once and stays up through it; a lane with a grid in every column and an empty fragment never pop", () =>
            {
                if (bass == null || fa == null || fe == null || fb == null || full == null) return "islands missing (" + SM.Islands.Count + ")";
                if (!KeyStage.IsFragment(fa) || !KeyStage.IsFragment(fb) || KeyStage.IsFragment(bass) || KeyStage.IsFragment(full)) return "fragment flags: " + KeyStage.IsFragment(fa) + KeyStage.IsFragment(fb) + KeyStage.IsFragment(bass) + KeyStage.IsFragment(full);
                if (KeyStage.Drives(bass) || KeyStage.RunMembers(bass).Count != 3) return "the bass lane: driven " + KeyStage.Drives(bass) + ", run " + KeyStage.RunMembers(bass).Count;
                if (KeyStage.Drives(fe)) return "the empty fragment is driven";
                if (!KeyStage.Drives(fa)) return "the fragment is not driven";
                var rb = KeyStage.RunMembers(fb);
                if (rb.Count != 2 || !rb.Contains(full) || KeyStage.RunMembers(fa).Contains(fb)) return "runs: the lead-in's has " + rb.Count + (KeyStage.RunMembers(fa).Contains(fb) ? ", the other voice's joined" : "");
                if (!KeyStage.Drives(full)) return "the full grid after the lead-in is not driven";
                float bpb = 4f;
                float aOn = KeyStage.DepthAt(fa, 1.5f * bpb), aBefore = KeyStage.DepthAt(fa, 0.3f * bpb), aAfter = KeyStage.DepthAt(fa, 3.2f * bpb);
                if (Mathf.Abs(aOn) > 1e-3f || aBefore < 0.999f || aAfter < 0.999f) return "the fragment: on its measure " + F(aOn) + ", before " + F(aBefore) + ", after " + F(aAfter);
                float lead = KeyStage.DepthAt(full, 7.5f * bpb), mid = KeyStage.DepthAt(full, 10f * bpb), early = KeyStage.DepthAt(full, 5f * bpb);
                if (Mathf.Abs(lead) > 1e-3f || Mathf.Abs(mid) > 1e-3f || early < 0.999f) return "the lead-in run: at the lead-in " + F(lead) + ", in the full grid " + F(mid) + ", before " + F(early);
                return "ok: fragment up " + F(aOn) + " / under " + F(aBefore) + ", lead-in run of 2, bass run of 3 never driven";
            });
            // playing: the fragment really goes under and comes up (PartDepth, colliders)
            if (!clockFrozen && fa != null && bass != null)
            {
                int lt9 = Synth.LateEvents; GlobalClock.Play(); GlobalClock.Seek(0.2 * 4.0); yield return Wait(KeyStage.EaseS + 0.3f);
                float under = fa.PartDepth;
                yield return UntilBeat(1.5 * 4.0);
                float up = fa.PartDepth; bool bassUp = bass.PartDepth < 1e-3f;
                GlobalClock.Stop(); lateExcused += Synth.LateEvents - lt9;
                Run(sb, "playing, the fragment is under the sea before its measure and up on it (the bass grid stays up)", () => under > 0.95f && up < 0.05f && bassUp ? "ok: " + F(under) + " → " + F(up) : "depth before " + F(under) + ", on its measure " + F(up) + ", bass up " + bassUp);
            }
            else Info(sb, "NOT VERIFIED: the fragment's live pose (it needs the audio clock)");
            GlobalClock.Seek(0.0); yield return Wait(0.5f);
        }

        // ---------------------------------------------------------------- v9 round 3: a lane of 1-bar grids that plays nearly the whole song never pops
        {
            var m = new List<MeasureState>();
            var cubes = new List<CubeState>();
            int id = 8600;
            System.Action<int, int, float, int[]> grid = (col, off, pz, insts) =>
            {
                m.Add(new MeasureState { chordKey = "C", root = 48, semis = new[] { 0, 4, 7 }, bars = 1, kind = 0, col = col, repeat = 1, energy = 2, pz = pz, placed = true, barOffset = off });
                foreach (int inst in insts) cubes.Add(Cube(id++, inst, m.Count - 1, new[] { 0, 1, 2, 3 }, 24));
            };
            // lane 0: the bass in 1-bar grids, every bar of 3 four-bar columns but bar 6 (a one-bar break), bar 9 adds a fill voice
            for (int b = 0; b < 12; b++) if (b != 5) grid(b / 4, b % 4, 0f, b == 8 ? new[] { 4, 7 } : new[] { 4 });
            // lane 10: a melody in bars 2–5 only (a third of the song)
            for (int b = 1; b < 5; b++) grid(b / 4, b % 4, 10f, new[] { 5 });
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 lanes", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = cubes.ToArray(), moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            var bass = SM.Islands.Where(k => k != null && Mathf.Abs(k.pz) < 1f).OrderBy(k => k.startBeatOffset).ToList();
            var mel = SM.Islands.Where(k => k != null && Mathf.Abs(k.pz - 10f) < 1f).OrderBy(k => k.startBeatOffset).ToList();
            Run(sb, "v9 round 3: a lane of 1-bar bass grids across 3 columns (a one-bar break, a bar with an extra fill voice) is ONE run that never pops; a melody lane playing a third of the song still pops", () =>
            {
                if (bass.Count != 11 || mel.Count != 4) return "islands: bass " + bass.Count + ", melody " + mel.Count;
                var rb = KeyStage.RunMembers(bass[0]);
                if (rb.Count != 11) return "the bass run has " + rb.Count + " grids (want 11: the break and the fill do not cut it)";
                int driven = bass.Count(k => KeyStage.Drives(k));
                if (driven > 0) return driven + " bass grids are driven (the lane plays 11 of 12 bars)";
                float inBreak = KeyStage.DepthAt(bass[5], 5.5f * 4f);
                if (inBreak > 1e-3f) return "the bass sinks in its break (" + F(inBreak) + ")";
                if (KeyStage.RunMembers(mel[0]).Count != 4 || !KeyStage.Drives(mel[0])) return "the melody lane: run " + KeyStage.RunMembers(mel[0]).Count + ", driven " + KeyStage.Drives(mel[0]);
                return "ok: bass run of 11 never driven, melody run of 4 driven";
            });
        }

        // ---------------------------------------------------------------- v9: the STAIRS look — a deep staircase whose steps follow their pitch, aimed at its chord
        {
            var m = new List<MeasureState>();
            m.Add(new MeasureState { chordKey = "Stairs", root = 60, semis = new[] { 0 }, bars = 1, kind = 3, col = 0, repeat = 1, energy = 2, pz = 0f, placed = true, stairDir = -1, stairSteps = 5, stairLead = true });
            m.Add(Chord(0, "C", 60, new[] { 0, 4, 7 }, 9f));
            m.Add(Chord(1, "F", 65, new[] { 0, 4, 7 }, 0f));
            m.Add(Chord(1, "Am", 57, new[] { 0, 3, 7 }, 9f));
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 stairs", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = new CubeState[0], moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4);
            var sk = SM.Islands.FirstOrDefault(k => k != null && k.IsStairs);
            if (sk != null) sk.RefreshStairs();
            yield return Frames(2);
            if (captures && sk != null) { OrbitCamera.I.FrameBounds(sk.FootprintBounds, 0.35f, true, 1f); yield return Wait(0.8f); yield return Shot("h8_stairs.png"); }
            Run(sb, "v9 STAIRS: as deep as a chord grid (not a strip), every step a block down to the base whose height follows its pitch (the ends at the v7 heights), the last step's arrow aimed at the chord it falls into (the next column's grid in its lane)", () =>
            {
                if (sk == null) return "no stairs island";
                if (sk.Depth < KeyBlock.DepthOf(KeyBlock.StairRows) - 1e-3f) return "depth " + F(sk.Depth) + " (a " + KeyBlock.StairRows + "-row grid is " + F(KeyBlock.DepthOf(KeyBlock.StairRows)) + ")";
                int n = sk.cols;
                var p = sk.StairPitchesShown;
                if (p == null || p.Length != n) return "pitches " + (p != null ? p.Length.ToString() : "null") + " for " + n + " steps";
                var t0 = sk.GetTile(0, 0); var bc = t0 != null ? t0.GetComponent<BoxCollider>() : null;
                if (bc == null || bc.size.z < KeyBlock.DepthOf(KeyBlock.StairRows) - KeyBlock.StairLedge - KeyBlock.StairBackPad - 0.05f) return "step 0 is " + (bc != null ? F(bc.size.z) : "-") + " deep";
                if (Mathf.Abs(sk.StairTopY(0) - KeyBlock.StairTopOf(-1, 0)) > 1e-3f || Mathf.Abs(sk.StairTopY(n - 1) - KeyBlock.StairTopOf(-1, n - 1)) > 1e-3f) return "the ends moved: " + F(sk.StairTopY(0)) + " / " + F(sk.StairTopY(n - 1));
                string tops = "";
                for (int i = 0; i < n; i++)
                {
                    tops += F(sk.StairTopY(i)) + "(" + p[i] + ") ";
                    float want = p[n - 1] != p[0] ? Mathf.Lerp(sk.StairTopY(0), sk.StairTopY(n - 1), Mathf.Clamp01((p[i] - p[0]) / (float)(p[n - 1] - p[0]))) : sk.StairTopY(i);
                    if (Mathf.Abs(sk.StairTopY(i) - want) > 1e-3f) return "step " + i + " top " + F(sk.StairTopY(i)) + ", its pitch says " + F(want) + ": " + tops;
                    var ti = sk.GetTile(i, 0);
                    if (ti == null || Mathf.Abs(sk.transform.InverseTransformPoint(ti.Top).y - sk.StairTopY(i)) > 0.03f) return "step " + i + "'s tile is not at its top";
                }
                var target = sk.LeadInTarget();
                var arrow = sk.GetTile(n - 1, 0).transform.Find("LeadIn");
                if (target == null || target.column != 1 || Mathf.Abs(target.pz) > 1f) return "lead-in target " + (target != null ? "column " + target.column + " pz " + F(target.pz) : "none");
                if (arrow == null || !arrow.gameObject.activeInHierarchy || Mathf.Abs(sk.LeadInYaw) > KeyBlock.LeadInMaxYaw + 1e-3f) return "the lead-in arrow " + (arrow == null ? "is missing" : "yaw " + F(sk.LeadInYaw));
                return "ok: depth " + F(sk.Depth) + ", tops " + tops + "→ " + target.assignedChord + " (yaw " + F(sk.LeadInYaw) + "°)";
            });
        }

        // ---------------------------------------------------------------- v9: BELT SLOTS — the rail lies only under the island; the slots ahead are dotted outlines on the floor
        {
            var m = new List<MeasureState>();
            m.Add(new MeasureState { chordKey = "C", root = 60, semis = new[] { 0, 4, 7 }, bars = 1, kind = 0, col = 0, repeat = 3, energy = 2, pz = 0f, placed = true });
            m.Add(Chord(1, "G", 67, new[] { 0, 4, 7 }));
            var st = new SongState { version = SongState.CurrentVersion, name = "h8 belt slots", bpm = Bpm, beatsPerBar = 4, loop = true, measures = m.ToArray(), cubes = new CubeState[0], moons = new MeasureState[0], keyTonic = 0, keyMinor = false, tone = 1f, space = 1f };
            GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
            yield return Frames(4); yield return Wait(0.6f);
            var bk = SM.Islands.FirstOrDefault(k => k != null && k.column == 0);
            var belt = bk != null ? bk.Belt : null;
            Vector2 span0 = belt != null ? belt.RailSpanLocal : Vector2.zero;
            float pitch = bk != null ? bk.BeltPitch : 0f, e = KeyBlock.EdgeInset;
            int feetOn = belt != null ? belt.GetComponentsInChildren<LineRenderer>(false).Length : 0;
            float slotsY = belt != null ? belt.SlotsY : float.NaN, floorY = belt != null ? belt.CheckpointY : float.NaN;
            if (captures && bk != null) { OrbitCamera.I.FrameBounds(new Bounds(bk.transform.position + new Vector3(pitch, 0f, bk.Depth * 0.5f), new Vector3(pitch * 3f, 1f, bk.Depth)), 0.15f, true, 1f); yield return Wait(0.8f); yield return Shot("h8_belt_slots.png"); }
            GlobalClock.Seek(SM.ColumnStart(0) + SM.PassLength(0) * 1.5); yield return Wait(1.0f);
            Vector2 span1 = belt != null ? belt.RailSpanLocal : Vector2.zero;
            float worldMid = belt != null ? belt.transform.TransformPoint(new Vector3((span1.x + span1.y) * 0.5f, 0f, 0f)).x : 0f;
            float islandMid = bk != null ? bk.transform.TransformPoint(new Vector3(-e + bk.Width * 0.5f, 0f, 0f)).x : 0f;
            if (captures && bk != null) yield return Shot("h8_belt_slots_pass2.png");
            GlobalClock.Seek(0.0); yield return Wait(0.5f);
            Run(sb, "v9 BELT SLOTS: a ×3 island's rail is ONE slot long under the island (at home, then under it in pass 2), its three slots dotted outlines on the floor", () =>
            {
                if (bk == null || belt == null) return "no belted island";
                if (span0.x < -e - 0.01f || span0.y > -e + bk.Width + 0.01f) return "at home the rail spans " + F(span0.x) + " … " + F(span0.y) + " (the slot " + F(-e) + " … " + F(-e + bk.Width) + ")";
                if (Mathf.Abs((span1.x - span0.x) - pitch) > 0.05f) return "pass 2: the rail moved " + F(span1.x - span0.x) + " (a slot is " + F(pitch) + ")";
                if (Mathf.Abs(worldMid - islandMid) > 0.1f) return "pass 2: the rail's middle " + F(worldMid) + ", the island's " + F(islandMid);
                if (feetOn != 3) return feetOn + " dotted slot outlines (want 3)";
                if (Mathf.Abs(slotsY - floorY) > 0.02f) return "the outlines lie at y " + F(slotsY) + ", the floor " + F(floorY);
                return "ok: rail " + F(span0.x) + " … " + F(span0.y) + " → +" + F(span1.x - span0.x) + ", 3 dotted slots on the floor at " + F(slotsY);
            });
        }

        // ---------------------------------------------------------------- audio, console
        int excused = lateExcused;
        Run(sb, "Synth late / errors unchanged (counted apart: " + excused + " around the test's own seeks / play starts / captures)", () =>
            Synth.LateEvents - excused == late0 && Synth.Errors == err0 ? "ok: " + Synth.Stats() : "late " + (Synth.LateEvents - late0 - excused) + " errors " + (Synth.Errors - err0) + " — last late: " + Synth.LastLate);
        Finish(sb);
    }

    /// <summary>v9 perf (S17): the gallery's get-proto playing — frame time, KeyStage's LateUpdate and every island's UpdateSea + UpdateWater (ms per
    /// frame: mean, p95, max) over <paramref name="frames"/> frames with the stage on, then off. Report: Captures/h8_perf.txt (ends with SUMMARY).</summary>
    public static string Perf(int frames = 600)
    {
        if (SequenceMaster.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(PerfRoutine(frames));
        return "started";
    }

    static IEnumerator PerfRoutine(int frames)
    {
        string path = Path.Combine(V2Checks.CapturePath, "h8_perf.txt");
        var sb = new StringBuilder("V8 perf " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        try { File.WriteAllText(path, sb + "...running\n"); } catch (Exception) { }
        if (!Gallery.Open("get-proto", false)) { sb.Append("SUMMARY no get-proto\n"); File.WriteAllText(path, sb.ToString()); yield break; }
        yield return Wait(2f);
        GlobalClock.Seek(0); GlobalClock.Play();
        yield return Wait(3f);
        int driven = SM.Islands.Count(k => k != null && KeyStage.Drives(k));
        sb.Append("islands ").Append(SM.Islands.Count).Append(", driven ").Append(driven).Append('\n');
        double tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        string summary = "";
        foreach (bool on in new[] { true, false })
        {
            KeyStage.Enabled = on;
            yield return Wait(1f);
            var dt = new List<double>(); var ks = new List<double>(); var sea = new List<double>();
            long k0 = KeyStage.Ticks, s0 = KeyBlock.SeaTicks, l0 = KeyBlock.LineTicks; int rb0 = KeyStage.RunRebuilds, hm0 = KeyBlock.HideMeasures, lr0 = KeyBlock.LineRefreshes;
            var ln = new List<double>();
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                long k1 = KeyStage.Ticks, s1 = KeyBlock.SeaTicks, l1 = KeyBlock.LineTicks;
                dt.Add(Time.unscaledDeltaTime * 1000.0); ks.Add((k1 - k0) * tick); sea.Add((s1 - s0) * tick); ln.Add((l1 - l0) * tick); k0 = k1; s0 = s1; l0 = l1;
            }
            System.Func<List<double>, string> st = l => { var o = l.OrderBy(x => x).ToList(); return "mean " + o.Average().ToString("F2") + " p95 " + o[(int)(o.Count * 0.95)].ToString("F2") + " max " + o[o.Count - 1].ToString("F2"); };
            string line = "stage " + (on ? "ON " : "OFF") + ": frame ms " + st(dt) + " | KeyStage ms " + st(ks) + " | UpdateSea+Water ms " + st(sea) + " | cube-line refresh ms " + st(ln) + " (" + (KeyBlock.LineRefreshes - lr0) + " calls)" + " | run rebuilds " + (KeyStage.RunRebuilds - rb0) + ", HideDepth measures " + (KeyBlock.HideMeasures - hm0);
            sb.Append(line).Append('\n');
            summary += (on ? "on " : "off ") + "frame " + dt.Average().ToString("F1") + " ks " + ks.Average().ToString("F3") + " sea " + sea.Average().ToString("F3") + "; ";
            try { File.WriteAllText(path, sb + "...running\n"); } catch (Exception) { }
        }
        KeyStage.Enabled = true;
        GlobalClock.Stop();
        sb.Append("SUMMARY ").Append(summary).Append('\n');
        try { File.WriteAllText(path, sb.ToString()); } catch (Exception) { }
    }

    /// <summary>Captures: the test song with keyboard 1 repeated 3×, stopped at <paramref name="beatAfterBoundary"/> beats from its first pass boundary
    /// (negative = before it: mid-drag around −0.1), framed to show every checkpoint; writes Captures/<paramref name="file"/>.</summary>
    public static string CaptureCheckpoints(float beatAfterBoundary, string file)
    {
        if (SequenceMaster.I == null) return "needs Play mode";
        SequenceMaster.I.StartCoroutine(CaptureRoutine(beatAfterBoundary, file));
        return "started";
    }
    static IEnumerator CaptureRoutine(float off, string file)
    {
        Prepare();
        var st = TestSong(); st.measures.First(m => m.kind == 2 && m.col == 2).repeat = 3;
        GlobalClock.Stop(); SongState.Apply(st); History.Reset(); History.Push();
        yield return Frames(4);
        var kr = Keyboards().FirstOrDefault(k => k.column == 2);
        if (kr == null || kr.Belt == null) yield break;
        var b = kr.WorldBounds; b.Encapsulate(kr.Belt.CheckpointCenter(2) + Vector3.right * 4f); b.Encapsulate(kr.Belt.CheckpointCenter(0) - Vector3.right * 4f);
        OrbitCamera.I.FrameBounds(b, 0.1f, true, 1f);
        yield return Wait(1.2f);
        double boundary = SM.ColumnStart(kr.column) + SM.PassLength(kr.column);
        if (off > 50f) { GlobalClock.Play(); GlobalClock.Seek(boundary - 0.2); yield return Wait(0.9f); yield return Shot(file); GlobalClock.Stop(); yield break; }   // playing: the drag and its wake
        for (double bt = boundary - 1.0; bt <= boundary + off; bt += 0.02) { GlobalClock.Seek(bt); yield return null; }
        yield return Shot(file);
    }

    static bool clockFrozen;
    static float NoteOn(KeyHands.Hand h, int i) { float on, off; TileInteraction t; bool w; return h.NoteOf(i, out on, out off, out t, out w) ? on : float.PositiveInfinity; }
    static int FirstUnwrapped(KeyHands.Hand h)
    {
        for (int i = 0; i < h.NoteCount; i++) { float on, off; TileInteraction t; bool w; if (h.NoteOf(i, out on, out off, out t, out w) && !w) return i; }
        return 0;
    }
    static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    static void Finish(StringBuilder sb)
    {
        Run(sb, "console clean (no errors / exceptions during the run)", () => errors.Count == 0 ? null : errors.Count + ": " + string.Join(" | ", errors.Take(3)));
        Application.logMessageReceived -= OnLog;
        Restore();
        try { SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath))); History.Reset(); History.Push(); } catch (Exception e) { Info(sb, "fixture reload: " + e.Message); }
        int fails = sb.ToString().Split('\n').Count(l => l.StartsWith("FAIL"));
        sb.Append(fails == 0 ? "ALL PASS (" + num + " checks)\n" : fails + " FAIL of " + num + "\n");
        Done = true;
        Flush(sb);
    }
}
