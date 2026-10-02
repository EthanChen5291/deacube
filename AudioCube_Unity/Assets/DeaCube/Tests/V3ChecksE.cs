using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SPEC v3 §7 (package E, "Look") Play-mode checks. <see cref="Run"/> (coroutine; poll <see cref="Done"/> / <see cref="Report"/>):
/// numbered PASS/FAIL lines — Fx.Lit = DeaCube/Toon with all its passes, prefab cube materials on Toon, no renderer left on URP Lit
/// in the fixture world, MaterialPropertyBlock / SetColor compatibility through the tiles' and cubes' own code paths, the Global
/// Volume overrides, BeatBump, sky / sea / page shaders, the comic effects (pooled, released), Look.Stepped, frame time on the
/// playing fixture, Synth late / errors, V2Checks.RunAll + RunWorld. <see cref="Captures"/> (coroutine; poll
/// <see cref="CapturesDone"/>): Captures/e_world.png (low, with the sky), e_world_top.png, e_closeup.png, e_closeup_matte.png, e_fx.png
/// (landings, close), e_impact.png, e_fx_arrival.png, e_fx_handdrawn.png, and e_menu.png / e_present.png when the menu / presenter
/// exist. Never writes the user's save.
/// </summary>
public static class V3ChecksE
{
    public static bool Done, CapturesDone;
    public static string Report = "", CaptureReport = "";
    static int num;

    static void Line(StringBuilder sb, bool ok, string text) { num++; sb.Append(ok ? "PASS " : "FAIL ").Append(num).Append(". ").Append(text).Append('\n'); }
    static void Info(StringBuilder sb, string text) { sb.Append("INFO ").Append(text).Append('\n'); }
    static void Try(StringBuilder sb, string name, Func<string> check)
    {
        try { string d = check(); Line(sb, d == null, name + (d == null ? "" : ": " + d)); }
        catch (Exception e) { Line(sb, false, name + ": " + e.GetType().Name + " " + e.Message); }
    }

    /// <summary>SPEC v3 test preamble: no menu, no prompt, no tutorial.</summary>
    public static void Prepare()
    {
        MainMenu.Hide();
        WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
        Onboarding.Suppressed = true;
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        WorldInput.Unlock("prompt");
    }

    public static void LoadFixture()
    {
        GlobalClock.Stop();
        SongState.Apply(SongState.FromJson(File.ReadAllText(V2Checks.FixturePath)));
        History.Reset(); History.Push();
    }

    /// <summary>The fixed overview used for e_before / e_world and the frame-time runs: yaw -24, pitch 30, the whole song.</summary>
    public static string WorldView()
    {
        var sm = SongManager.I; var cam = Camera.main;
        if (sm == null || cam == null || OrbitCamera.I == null) return "no world";
        OrbitCamera.I.Suspended = true;
        var b = sm.SongBounds;
        Vector3 c = b.center; c.y = 0f;
        var rot = Quaternion.Euler(30f, -24f, 0f);
        float dist = Mathf.Max(b.size.x, b.size.z) * 0.62f + 8f;
        cam.transform.rotation = rot; cam.transform.position = c - rot * Vector3.forward * dist;
        return "overview " + b.center + " " + b.size + " dist " + dist.ToString("F1");
    }

    /// <summary>The low overview used for e_before_world / e_world: pitch 13 so the horizon and the sky show above the islands.</summary>
    public static string WorldViewLow()
    {
        var sm = SongManager.I; var cam = Camera.main;
        if (sm == null || cam == null || OrbitCamera.I == null) return "no world";
        OrbitCamera.I.Suspended = true;
        var b = sm.SongBounds;
        Vector3 c = b.center; c.y = 0f;
        var rot = Quaternion.Euler(13f, -24f, 0f);
        float dist = Mathf.Max(b.size.x, b.size.z) * 0.55f + 6f;
        cam.transform.rotation = rot; cam.transform.position = c - rot * Vector3.forward * dist + Vector3.up * 2f;
        return "low overview dist " + dist.ToString("F1");
    }

    /// <summary>A close view on the island carrying the most cubes (pitch 40).</summary>
    public static KeyBlock CloseView(float dist, float pitch = 40f, float yaw = -24f)
    {
        var sm = SongManager.I; var cam = Camera.main;
        if (sm == null || cam == null || OrbitCamera.I == null || sm.Islands.Count == 0) return null;
        OrbitCamera.I.Suspended = true;
        KeyBlock best = sm.Islands[0]; int bestN = -1;
        foreach (var kb in sm.Islands)
        {
            int n = 0;
            foreach (var cu in SequenceMaster.Cubes) if (cu != null && cu.Island == kb) n++;
            if (n > bestN) { bestN = n; best = kb; }
        }
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.rotation = rot; cam.transform.position = best.Center + Vector3.up * 0.3f - rot * Vector3.forward * dist;
        return best;
    }

    static bool IsUrpLit(string n) => n == "Universal Render Pipeline/Lit" || n == "Universal Render Pipeline/Simple Lit" || n == "Universal Render Pipeline/Complex Lit" || n == "Universal Render Pipeline/Baked Lit" || n == "Standard";

    /// <summary>Renderers (active or pooled) whose materials still use a stock lit shader; toon = renderers on DeaCube/Toon.</summary>
    public static List<string> LitOffenders(out int toon)
    {
        var list = new List<string>(); toon = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isToon = false;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (m.shader.name == Fx.ToonShaderName) isToon = true;
                if (IsUrpLit(m.shader.name)) list.Add(PathOf(r.transform) + " (" + m.name + ": " + m.shader.name + ")");
            }
            if (isToon) toon++;
        }
        return list;
    }

    static string PathOf(Transform t) { var sb = new StringBuilder(t.name); while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); } return sb.ToString(); }

    // ------------------------------------------------------------------ frame time
    public static float LastFrameMs, LastWorstMs; public static int LastFrames; public static string LastStats = "";

    /// <summary>Average frame time over <paramref name="seconds"/> with the frame cap and vSync off (restored after).</summary>
    public static IEnumerator MeasureFrames(float seconds)
    {
        int prevTarget = Application.targetFrameRate, prevVsync = QualitySettings.vSyncCount;
        Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;
        yield return null; yield return null; yield return null;
        float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount; float worst = 0f;
        while (Time.realtimeSinceStartup - t0 < seconds) { yield return null; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
        float t1 = Time.realtimeSinceStartup; int f1 = Time.frameCount;
        Application.targetFrameRate = prevTarget; QualitySettings.vSyncCount = prevVsync;
        LastFrames = f1 - f0;
        LastFrameMs = (t1 - t0) * 1000f / Mathf.Max(1, LastFrames);
        LastWorstMs = worst * 1000f;
        LastStats = RenderStats();
    }

    public static string RenderStats()
    {
#if UNITY_EDITOR
        return "draw calls " + UnityEditor.UnityStats.drawCalls + ", setpass " + UnityEditor.UnityStats.setPassCalls + ", tris " + UnityEditor.UnityStats.triangles + ", shadow casters " + UnityEditor.UnityStats.shadowCasters + ", render " + (UnityEditor.UnityStats.renderTime * 1000f).ToString("F2") + " ms";
#else
        return "(stats: editor only)";
#endif
    }

    // ------------------------------------------------------------------ checks
    public static string Run()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        Done = false; Report = ""; num = 0;
        SequenceMaster.I.StartCoroutine(RunRoutine());
        return "started";
    }

    static int late0, err0;   // integration: the synth check counts what happens during this run, not the whole session

    static IEnumerator RunRoutine()
    {
        var sb = new StringBuilder();
        late0 = Synth.LateEvents; err0 = Synth.Errors;
        Prepare();
        LoadFixture();
        yield return null; yield return null;
        var sm = SongManager.I;
        Info(sb, "screen " + Screen.width + "x" + Screen.height + ", fixture " + sm.Islands.Count + " islands, " + SequenceMaster.Cubes.Count + " cubes");

        // 1. Fx.Lit = DeaCube/Toon with its five passes
        Try(sb, "Fx.Lit uses DeaCube/Toon (supported; passes ToonForward, InkOutline, ShadowCaster, DepthOnly, DepthNormals)", () =>
        {
            var m = Fx.Lit(Color.red, 0.5f, 0f);
            try
            {
                if (m.shader == null || m.shader.name != Fx.ToonShaderName) return "shader " + (m.shader != null ? m.shader.name : "null");
                if (!m.shader.isSupported) return "not supported";
                string[] passes = { "ToonForward", "InkOutline", "ShadowCaster", "DepthOnly", "DepthNormals" };
                foreach (var p in passes) if (m.FindPass(p) < 0) return "missing pass " + p;
                if (!m.IsKeywordEnabled("_EMISSION")) return "_EMISSION off";
                if (m.GetColor("_BaseColor") != Color.red) return "_BaseColor not kept";
                foreach (var prop in new[] { "_BaseMap", "_BaseColor", "_EmissionColor", "_Smoothness", "_Metallic", "_Color" }) if (!m.HasProperty(prop)) return "no " + prop;
                return null;
            }
            finally { UnityEngine.Object.Destroy(m); }
        });

        Try(sb, "Fx.Lit(..., outline: false) skips the ink pass (SetShaderPassEnabled SRPDefaultUnlit off); Fx.SetOutline turns it back on", () =>
        {
            var m = Fx.Lit(Color.white, 0.4f, 0f, false);
            try
            {
                if (m.GetShaderPassEnabled("SRPDefaultUnlit")) return "pass still enabled";
                if (!m.GetShaderPassEnabled("UniversalForward")) return "lit pass disabled";
                Fx.SetOutline(m, true);
                if (!m.GetShaderPassEnabled("SRPDefaultUnlit")) return "not re-enabled";
                return null;
            }
            finally { UnityEngine.Object.Destroy(m); }
        });

        // 2. the cube prefab materials
        Try(sb, "cube prefab materials on DeaCube/Toon", () =>
        {
            var pm = PathManager.I;
            if (pm == null || pm.cubePrefabs == null) return "no PathManager";
            var bad = new List<string>(); int n = 0;
            foreach (var p in pm.cubePrefabs)
            {
                if (p == null) continue;
                var r = p.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null) continue;
                n++;
                if (r.sharedMaterial.shader.name != Fx.ToonShaderName) bad.Add(r.sharedMaterial.name + "=" + r.sharedMaterial.shader.name);
            }
            return bad.Count == 0 ? (n > 0 ? null : "no prefab materials") : string.Join(", ", bad);
        });

        // 3. nothing left on the stock lit look in the fixture world
        {
            int toon; var off = LitOffenders(out toon);
            Line(sb, off.Count == 0 && toon > 0, "renderers on URP Lit in the fixture world: " + off.Count + " (renderers on Toon: " + toon + ")" + (off.Count > 0 ? " -> " + string.Join("; ", off.GetRange(0, Mathf.Min(8, off.Count))) : ""));
        }

        // 4. MaterialPropertyBlock / SetColor compatibility through the existing code paths
        {
            string err = null; Renderer tr = null; TileInteraction tile = null;
            try
            {
                tile = sm.Islands[0].tiles[0];
                tr = tile.GetComponent<Renderer>();
                tile.Flash(new Color(1f, 0.3f, 0.2f), 1f);
            }
            catch (Exception e) { err = e.GetType().Name + " " + e.Message; }
            yield return null;
            if (err == null)
            {
                var blk = new MaterialPropertyBlock(); tr.GetPropertyBlock(blk);
                Color em = blk.GetColor("_EmissionColor"), bc = blk.GetColor("_BaseColor");
                bool ok = tr.sharedMaterial.shader.name == Fx.ToonShaderName && em.r > 0.5f && bc.maxColorComponent > 0.05f;
                Line(sb, ok, "tile tint via TileInteraction.Flash (MaterialPropertyBlock _BaseColor / _EmissionColor on the Toon tile material): emission " + em + ", base " + bc);
            }
            else Line(sb, false, "tile flash: " + err);

            string cerr = null; AudioCube cube = null; int orig = 0; Color before = Color.clear, after = Color.clear, want = Color.clear;
            try
            {
                foreach (var c in SequenceMaster.Cubes) if (c != null && !c.IsDrums) { cube = c; break; }
                var r = cube.GetComponent<Renderer>();
                orig = cube.instrument;
                before = r.sharedMaterial.GetColor("_BaseColor");
                int other = orig == 4 ? 1 : 4;
                cube.SetInstrument(other);
                after = r.sharedMaterial.GetColor("_BaseColor");
                want = Instruments.Colors[other];
                if (r.sharedMaterial.shader.name != Fx.ToonShaderName) cerr = "cube material shader " + r.sharedMaterial.shader.name;
            }
            catch (Exception e) { cerr = e.GetType().Name + " " + e.Message; }
            yield return null;
            Color emC = Color.clear;
            try { emC = cube.GetComponent<Renderer>().sharedMaterial.GetColor("_EmissionColor"); cube.SetInstrument(orig); } catch (Exception e) { if (cerr == null) cerr = e.Message; }
            bool cok = cerr == null && Vector4.Distance(after, want) < 0.02f && Vector4.Distance(before, after) > 0.05f;
            Line(sb, cok, "cube recolour via AudioCube.SetInstrument (material SetColor _BaseColor; emission driven per frame): " + (cerr ?? ("base " + before + " -> " + after + " (want " + want + "), emission " + emC)));
        }

        // 5. volume overrides
        Try(sb, "Global Volume overrides (tonemap None, contrast/saturation up, split toning, aberration, grain, vignette, low bloom)", () =>
        {
            var vol = UnityEngine.Object.FindAnyObjectByType<Volume>();
            if (vol == null) return "no volume";
            var p = vol.profile;
            Tonemapping tm; ColorAdjustments ca; SplitToning st; ChromaticAberration ab; FilmGrain fg; Vignette vg; Bloom bl;
            if (!p.TryGet(out tm) || tm.mode.value != TonemappingMode.None || !tm.mode.overrideState) return "tonemapping";
            if (!p.TryGet(out ca) || Mathf.Abs(ca.contrast.value - Look.Contrast) > 0.01f || Mathf.Abs(ca.saturation.value - Look.Saturation) > 0.01f) return "color adjustments";
            if (!p.TryGet(out st) || st.shadows.value != Look.SplitShadows || st.highlights.value != Look.SplitHighlights) return "split toning";
            if (!p.TryGet(out ab) || Mathf.Abs(ab.intensity.value - Look.Aberration) > 0.001f) return "aberration";
            if (!p.TryGet(out fg) || Mathf.Abs(fg.intensity.value - Look.Grain) > 0.001f) return "film grain";
            if (!p.TryGet(out vg) || Mathf.Abs(vg.intensity.value - Look.VignetteAmount) > 0.001f) return "vignette";
            if (!p.TryGet(out bl) || Mathf.Abs(bl.threshold.value - Look.BloomThreshold) > 0.001f) return "bloom threshold";
            return null;
        });
        {
            var vol = UnityEngine.Object.FindAnyObjectByType<Volume>();
            Info(sb, "post: " + (vol != null ? Look.DescribePost(vol.profile) : "no volume"));
        }

        // 6. BeatBump still drives the cached Bloom
        {
            float b0 = Fx.BloomNow;
            Fx.BeatBump(0.3f);
            yield return null;
            float b1 = Fx.BloomNow;
            yield return new WaitForSecondsRealtime(0.3f);
            float b2 = Fx.BloomNow;
            Line(sb, b1 > b0 + 0.05f && Mathf.Abs(b2 - Look.BloomIntensity) < 0.01f, "BeatBump: bloom " + b0.ToString("F2") + " -> " + b1.ToString("F2") + " -> " + b2.ToString("F2") + " (base " + Look.BloomIntensity + ")");
        }

        // 7. sky, sea, page
        Try(sb, "sky DeaCube/SkyToon, sea DeaCube/SeaToon, page DeaCube/InkScreen drawn, flat fog = horizon haze", () =>
        {
            var fx = Fx.I;
            if (fx.SkyMaterial == null || fx.SkyMaterial.shader.name != "DeaCube/SkyToon") return "sky " + (fx.SkyMaterial != null ? fx.SkyMaterial.shader.name : "none");
            if (fx.SeaMaterial == null || fx.SeaMaterial.shader.name != "DeaCube/SeaToon") return "sea " + (fx.SeaMaterial != null ? fx.SeaMaterial.shader.name : "none");
            if (LookScreen.I == null || LookScreen.I.Drawn < 1) return "page quads drawn " + (LookScreen.I != null ? LookScreen.I.Drawn : -1);
            if (!RenderSettings.fog || RenderSettings.fogMode != FogMode.Linear) return "fog";
            return null;
        });

        // 8. Look.Stepped
        Try(sb, "Look.Stepped holds on twos (12 fps) and passes through when OnTwos is off", () =>
        {
            bool prev = Look.OnTwos;
            Look.OnTwos = true;
            float a = Look.Stepped(0.49f), b = Look.Stepped(1f / 12f + 0.001f);
            Look.OnTwos = false;
            float c = Look.Stepped(0.49f);
            Look.OnTwos = prev;
            if (Mathf.Abs(a - 5f / 12f) > 1e-4f || Mathf.Abs(b - 1f / 12f) > 1e-4f || Mathf.Abs(c - 0.49f) > 1e-6f) return a + " " + b + " " + c;
            return null;
        });

        // 9. comic effects fire, are pooled and released
        {
            var fx = Fx.I;
            var cube = SequenceMaster.Cubes.Count > 0 ? SequenceMaster.Cubes[0] : null;
            Vector3 p = cube != null ? cube.transform.position : sm.Islands[0].Center;
            string ferr = null;
            try
            {
                Fx.Ripple(p, Color.cyan, 1f, 0.4f); Fx.Pillar(p, Color.cyan, 2f, 0.4f, 0.5f); Fx.SkyBeam(p, Color.magenta, 8f);
                Fx.Starburst(p + Vector3.up, Color.yellow, 1.4f); Fx.Burst(p, Color.green, 12, 2f); Fx.SeaRipple(p, Color.white);
                if (cube != null) Fx.Multiples(cube.transform, cube.Color, 3);
                Fx.SpeedLines(p, Vector3.right, Color.cyan); Fx.KirbyDots(p + Vector3.up, Color.magenta, 1f);
            }
            catch (Exception e) { ferr = e.GetType().Name + " " + e.Message; }
            yield return new WaitForSecondsRealtime(0.1f);
            string live = "ripples " + fx.ActiveRipples + ", pillars " + fx.ActivePillars + ", bursts " + fx.ActiveBursts + ", echoes " + fx.ActiveEchoes + ", streaks " + fx.ActiveStreaks + ", kirby " + fx.ActiveKirby;
            bool fired = ferr == null && fx.ActiveRipples >= 2 && fx.ActivePillars >= 2 && fx.ActiveBursts >= 1 && fx.ActiveStreaks >= 4 && fx.ActiveKirby >= 1 && (cube == null || fx.ActiveEchoes >= 1);
            yield return new WaitForSecondsRealtime(1.3f);
            string after = "ripples " + fx.ActiveRipples + ", pillars " + fx.ActivePillars + ", bursts " + fx.ActiveBursts + ", echoes " + fx.ActiveEchoes + ", streaks " + fx.ActiveStreaks + ", kirby " + fx.ActiveKirby;
            bool released = fx.ActiveRipples == 0 && fx.ActivePillars == 0 && fx.ActiveBursts == 0 && fx.ActiveEchoes == 0 && fx.ActiveStreaks == 0 && fx.ActiveKirby == 0;
            Line(sb, fired && released, "comic FX (ink rings, beams, bursts, confetti, Multiples, SpeedLines, KirbyDots): live [" + live + "] -> after 1.4 s [" + after + "]" + (ferr != null ? " " + ferr : ""));
        }

        // 10. frame time on the playing fixture (overview), 60 fps budget
        {
            WorldView();
            GlobalClock.Seek(0f); GlobalClock.Play();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return MeasureFrames(4f);
            float ms = LastFrameMs, worst = LastWorstMs; int frames = LastFrames; string stats = LastStats;
            Line(sb, ms < 16.7f, "frame time, fixture playing, overview: " + ms.ToString("F2") + " ms avg (" + (1000f / Mathf.Max(0.01f, ms)).ToString("F0") + " fps, " + frames + " frames, worst " + worst.ToString("F1") + " ms); " + stats);
            Look.Ink = false;
            yield return MeasureFrames(3f);
            Info(sb, "same with the ink outline collapsed (Look.Ink = false; the pass still draws): " + LastFrameMs.ToString("F2") + " ms avg; " + LastStats);
            Look.Ink = true;
            GlobalClock.Stop();
        }

        // 11. audio untouched
        Line(sb, Synth.LateEvents == late0 && Synth.Errors == err0, "Synth late +" + (Synth.LateEvents - late0) + ", errors +" + (Synth.Errors - err0) + " during this run | " + Synth.Stats());

        // 12. v2 regression suites
        string all = "", world = "";
        try { all = V2Checks.RunAll(); } catch (Exception e) { all = "FAIL RunAll threw " + e.Message + "\n"; }
        yield return null;
        try { LoadFixture(); } catch (Exception) { }
        yield return null;
        try { world = V2Checks.RunWorld(); } catch (Exception e) { world = "FAIL RunWorld threw " + e.Message + "\n"; }
        int ap = Count(all, "PASS"), af = Count(all, "FAIL"), wp = Count(world, "PASS"), wf = Count(world, "FAIL");
        Line(sb, af == 0 && ap > 0, "V2Checks.RunAll " + ap + "/" + (ap + af) + (af > 0 ? " -> " + FailLines(all) : ""));
        Line(sb, wf == 0 && wp > 0, "V2Checks.RunWorld " + wp + "/" + (wp + wf) + (wf > 0 ? " -> " + FailLines(world) : ""));

        try { LoadFixture(); } catch (Exception) { }
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        Report = sb.ToString();
        try { Directory.CreateDirectory(V2Checks.CapturePath); File.WriteAllText(Path.Combine(V2Checks.CapturePath, "v3checksE_report.txt"), Report); } catch (Exception) { }
        Done = true;
    }

    static int Count(string s, string word)
    {
        int n = 0;
        foreach (var line in s.Split('\n')) if (line.StartsWith(word)) n++;
        return n;
    }

    static string FailLines(string s)
    {
        var sb = new StringBuilder();
        foreach (var line in s.Split('\n')) if (line.StartsWith("FAIL")) sb.Append(line.Trim()).Append(" | ");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ captures
    public static string Captures(bool menuAndPresent = true)
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL: needs Play mode";
        CapturesDone = false; CaptureReport = "";
        SequenceMaster.I.StartCoroutine(CaptureRoutine(menuAndPresent));
        return "started";
    }

    static IEnumerator Shot(string file, bool hud)
    {
        if (hud) V2Checks.HudOnCamera(true);
        yield return null; yield return null;
        Directory.CreateDirectory(V2Checks.CapturePath);
        string p = Path.Combine(V2Checks.CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
        yield return null; yield return null;
        if (hud) V2Checks.HudOnCamera(false);
    }

    static IEnumerator CaptureRoutine(bool menuAndPresent)
    {
        var sb = new StringBuilder();
        Prepare();
        LoadFixture();
        yield return new WaitForSecondsRealtime(0.8f);

        sb.Append(WorldViewLow()).Append('\n');
        yield return new WaitForSecondsRealtime(0.5f);
        yield return Shot("e_world.png", false);
        sb.Append(WorldView()).Append('\n');
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Shot("e_world_top.png", false);

        var kb = CloseView(8.5f, 42f);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Shot("e_closeup.png", false);
        sb.Append("closeup on island ").Append(kb != null ? kb.assignedChord : "-").Append('\n');

        // matte close-up: the cubes' flat faces, dots / hatching in shade, ink weight
        CloseView(7f, 36f, -38f);
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Shot("e_closeup_matte.png", false);

        // playing, close: the cubes' landings (ink rings, beams, accents, confetti) on the busiest island
        var cam = Camera.main;
        CloseView(9.5f, 40f);
        GlobalClock.Seek(0f); GlobalClock.Play();
        yield return new WaitForSecondsRealtime(1.35f);
        yield return Shot("e_fx.png", false);
        sb.Append("e_fx (landings) at beat ").Append(GlobalClock.SongBeat.ToString("F2")).Append(": pillars ").Append(Fx.I.ActivePillars).Append(" bursts ").Append(Fx.I.ActiveBursts).Append(" ripples ").Append(Fx.I.ActiveRipples).Append('\n');
        GlobalClock.Stop();

        // an impact frame, forced so the capture can show it (normally 83 ms on downbeats / accents, rate limited)
        WorldView();
        yield return new WaitForSecondsRealtime(0.3f);
        LookScreen.Request(0, SongManager.I.Islands[Mathf.Min(1, SongManager.I.Islands.Count - 1)].Center, 1f);
        yield return Shot("e_impact.png", false);

        // playing, overview: the comet's arrival (sky beam, sea ring, accent, impact frame)
        WorldView();
        GlobalClock.Seek(0f); GlobalClock.Play();
        var comet = Comet.Ensure();
        bool arrived = false;
        Action<int> h = i => { arrived = true; };
        comet.OnArrive += h;
        float t0 = Time.realtimeSinceStartup;
        while (!arrived && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        comet.OnArrive -= h;
        yield return new WaitForSecondsRealtime(0.05f);   // the beams have grown, the impact frame (83 ms) is still up
        yield return Shot("e_fx_arrival.png", false);
        sb.Append("e_fx_arrival: pillars ").Append(Fx.I.ActivePillars).Append(" bursts ").Append(Fx.I.ActiveBursts).Append(" ripples ").Append(Fx.I.ActiveRipples).Append(" impacts shown ").Append(LookScreen.I != null ? LookScreen.I.ImpactsShown : -1).Append('\n');

        // hand-drawn helpers around a cube: a big burst, multiples, speed lines, Kirby dots
        GlobalClock.Stop();
        var close = CloseView(6f, 34f, -30f);
        AudioCube cube = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && close != null && c.Island == close && (cube == null || c.transform.position.y < cube.transform.position.y)) cube = c;
        if (cube == null && SequenceMaster.Cubes.Count > 0) cube = SequenceMaster.Cubes[0];
        if (cube != null && cam != null)
        {
            // frame this cube: it sits a little left of centre, the effects spread around it
            var rot = Quaternion.Euler(30f, -30f, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = cube.transform.position + rot * Vector3.right * 1.2f + Vector3.up * 0.4f - rot * Vector3.forward * 7f;
        }
        yield return new WaitForSecondsRealtime(0.3f);
        if (cube != null)
        {
            Vector3 p = cube.transform.position;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            // a stand-in cube leaping to the right (the real cubes are driven by their own playback every frame)
            var demo = new GameObject("E_demoCube");
            demo.AddComponent<MeshFilter>().sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var demoMat = Fx.Lit(cube.Color, 0.62f, 0f);
            demo.AddComponent<MeshRenderer>().sharedMaterial = demoMat;
            demo.transform.localScale = cube.transform.lossyScale;
            Vector3 from = p + Vector3.up * 1.2f - right * 2.6f;
            Vector3 to = from + right * 1.8f + Vector3.up * 0.36f;
            demo.transform.position = from;
            Fx.Multiples(demo.transform, cube.Color, 3);                              // echoes trailing the leap
            Fx.KirbyDots(to + Vector3.up * 0.55f + right * 0.6f, cube.Color, 0.6f);   // a big moment's energy dots
            Fx.Ripple(p - Vector3.up * 0.35f, cube.Color, 1f, 0.5f);
            Fx.Pillar(p - Vector3.up * 0.35f + right * 2.2f, cube.Color, 2.4f, 0.6f, 0.55f);
            Fx.Burst(p + Vector3.up * 0.3f, cube.Color, 16, 2.2f);
            for (int i = 0; i < 4; i++)
            {
                demo.transform.position = Vector3.Lerp(from, to, (i + 1) / 4f);
                if (i == 1) Fx.SpeedLines(to - right * 0.35f, right, cube.Color);    // streaks trailing the leap
                yield return new WaitForSecondsRealtime(1f / 24f);
            }
            Fx.Starburst(p + Vector3.up * 0.25f, cube.Color, 1.3f);                 // the accent, centred on the landing cube
            yield return new WaitForSecondsRealtime(0.08f);
            yield return Shot("e_fx_handdrawn.png", false);
            UnityEngine.Object.Destroy(demo); UnityEngine.Object.Destroy(demoMat);
            sb.Append("e_fx_handdrawn: echoes ").Append(Fx.I.ActiveEchoes).Append(" streaks ").Append(Fx.I.ActiveStreaks).Append(" kirby ").Append(Fx.I.ActiveKirby).Append('\n');
        }

        if (menuAndPresent)
        {
            // the main menu (package D), when it exists
            bool menu = false;
            try { MainMenu.Show(); menu = MainMenu.IsShown; } catch (Exception e) { sb.Append("menu: ").Append(e.Message).Append('\n'); }
            if (menu)
            {
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("e_menu.png", false);
                sb.Append("e_menu captured\n");
                MainMenu.Hide(); Prepare();
                yield return new WaitForSecondsRealtime(1f);
            }
            else sb.Append("menu not available\n");
            // the presentation (package C), when it exists
            if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
            bool present = false;
            try { LoadFixture(); Presenter.Enter(); present = Presenter.Active; } catch (Exception e) { sb.Append("present: ").Append(e.Message).Append('\n'); }
            if (present)
            {
                yield return new WaitForSecondsRealtime(5f);
                yield return Shot("e_present.png", false);
                sb.Append("e_present captured\n");
                try { Presenter.Exit(); } catch (Exception) { }
                yield return new WaitForSecondsRealtime(1f);
            }
            else sb.Append("presenter not available\n");
        }

        GlobalClock.Stop();
        if (OrbitCamera.I != null) OrbitCamera.I.Suspended = false;
        Prepare();
        CaptureReport = sb.ToString();
        CapturesDone = true;
    }
}
