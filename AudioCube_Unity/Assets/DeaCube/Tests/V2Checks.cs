using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>
/// SPEC §8.2 verification, callable from execute_code in Play mode: <c>return V2Checks.RunAll();</c>.
/// Every check is isolated (exceptions are caught) and reported as one "PASS/FAIL name: detail" line.
/// <see cref="RunWorld"/> exercises the M0 world contract (MoveMeasure, MoveIsland, StampToAll, section setters + undo);
/// <see cref="RunWorldFrame2"/> completes the MoveIsland check one frame later.
/// </summary>
public static class V2Checks
{
    public static string FixturePath => Path.Combine(Application.dataPath, "DeaCube/Tests/v1_song.json");

    delegate string Check();   // null = pass, otherwise the failure detail

    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); sb.Append(d == null ? "PASS " : "FAIL ").Append(name).Append(": ").Append(d ?? "ok").Append('\n'); }
        catch (Exception e) { sb.Append("FAIL ").Append(name).Append(": ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n'); }
    }

    static string Bits(bool[] b) { var s = new StringBuilder(); foreach (bool v in b) s.Append(v ? 'x' : '.'); return s.ToString(); }
    static string Ints(IList<int> l) { var s = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) s.Append(','); s.Append(l[i]); } return s.ToString(); }

    public static string RunAll()
    {
        var sb = new StringBuilder();
        SongState fixture = null;

        Run(sb, "fixture load (13 cubes, 6 islands, tiles+rests round trip)", () =>
        {
            if (!File.Exists(FixturePath)) return "missing " + FixturePath;
            var st = SongState.FromJson(File.ReadAllText(FixturePath));
            if (st == null || st.measures == null || st.cubes == null) return "parse failed";
            if (!st.IsLegacy) return "fixture is not legacy (version " + st.version + ")";
            if (SongManager.I == null) return "needs Play mode (SongManager.I is null)";
            SongState.Apply(st);
            fixture = st;
            int cubes = 0; foreach (var c in SequenceMaster.Cubes) if (c != null) cubes++;
            if (cubes != st.cubes.Length) return "cubes " + cubes + " != " + st.cubes.Length;
            if (SongManager.I.Islands.Count != st.measures.Length) return "islands " + SongManager.I.Islands.Count + " != " + st.measures.Length;
            var cap = SongState.Capture();
            if (cap.cubes.Length != st.cubes.Length) return "captured cubes " + cap.cubes.Length;
            for (int i = 0; i < st.cubes.Length; i++)
            {
                var a = st.cubes[i]; var b = cap.cubes[i];
                if (a.measure != b.measure || a.instrument != b.instrument || a.xs.Length != b.xs.Length) return "cube " + i + " shape differs";
                for (int k = 0; k < a.xs.Length; k++) if (a.xs[k] != b.xs[k] || a.zs[k] != b.zs[k] || a.rests[k] != b.rests[k]) return "cube " + i + " node " + k + " differs";
            }
            return null;
        });

        Run(sb, "capture writes the current version (v4: 3) + placed positions", () =>
        {
            if (SongManager.I == null) return "needs Play mode";
            var cap = SongState.Capture();
            if (cap.version != SongState.CurrentVersion || cap.IsLegacy) return "version " + cap.version + " (want " + SongState.CurrentVersion + ")";   // v4 bumped CurrentVersion to 3; legacy = version < 2
            if (cap.measures.Length > 0 && !cap.measures[0].placed) return "placed not written";
            return null;
        });

        Run(sb, "v1 cubes: HitSteps16 = every step (hits -1 identity)", () =>
        {
            if (fixture == null) return "fixture not loaded";
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null) continue;
                if (c.windows.Count != 1) return "cube " + c.id + " has " + c.windows.Count + " windows";
                var got = c.HitSteps16(0, 0);
                var want = new List<int>();
                int n = Mathf.RoundToInt(c.windows[0].length / c.StepBeats);
                for (int k = 0; k < n; k++) want.Add(Mathf.RoundToInt(k * c.StepBeats * 4f));
                if (Ints(got) != Ints(want)) return "cube " + c.id + " got " + Ints(got) + " want " + Ints(want);
                if (c.StepsPerBar != Mathf.RoundToInt(GlobalClock.BeatsPerBar / c.StepBeats)) return "StepsPerBar " + c.StepsPerBar;
            }
            return null;
        });

        Run(sb, "Bjorklund E(3,8) E(5,8) k>=n k<=0", () =>
        {
            var e = Rhythm.Bjorklund(3, 8);
            if (Bits(e) != "x..x..x.") return "E(3,8) = " + Bits(e);
            var f = Rhythm.Bjorklund(5, 8);
            if (Bits(f) != "x.xx.xx.") return "E(5,8) = " + Bits(f);
            if (Bits(Rhythm.Bjorklund(4, 4)) != "xxxx" || Bits(Rhythm.Bjorklund(9, 4)) != "xxxx") return "k>=n";
            if (Bits(Rhythm.Bjorklund(0, 4)) != "...." || Bits(Rhythm.Bjorklund(-2, 4)) != "....") return "k<=0";
            if (Bits(Rhythm.Bjorklund(2, 8)) != "x...x..." || Bits(Rhythm.Bjorklund(3, 4)) != "xxx." || Bits(Rhythm.Bjorklund(3, 7)) != "x.x.x..") return "other identities";
            for (int n = 1; n <= 24; n++) for (int k = 1; k <= n; k++) { var b = Rhythm.Bjorklund(k, n); int c = 0; foreach (bool v in b) if (v) c++; if (c != k || !b[0]) return "E(" + k + "," + n + ") count " + c + " first " + b[0]; }
            return null;
        });

        Run(sb, "Pattern rotation / all / mask", () =>
        {
            var p = Rhythm.Pattern(3, 1, 0, 8);
            if (!(p[1] && p[4] && p[7]) || p[0] || p[2] || p[3] || p[5] || p[6]) return "rot 1 = " + Bits(p);
            var all = Rhythm.Pattern(-1, 5, 0, 8); foreach (bool v in all) if (!v) return "hits -1 not all";
            var m = Rhythm.Pattern(-2, 0, 0x0A, 4);
            if (Bits(m) != ".x.x") return "mask = " + Bits(m);
            if (Bits(Rhythm.Pattern(4, 0, 0, 4)) != "xxxx" || Bits(Rhythm.Pattern(40, 0, 0, 4)) != "xxxx") return "clamp";
            return null;
        });

        Run(sb, "MoodMap row counts for every quality x family + Dominant9", () =>
        {
            foreach (ChordQuality q in Enum.GetValues(typeof(ChordQuality)))
            {
                int rows = MusicTheory.Semitones(q).Length;
                for (int f = 0; f < 4; f++)
                {
                    var mq = MusicTheory.MoodMap(q, f);
                    if (f == 0 && mq != q) return q + " family 0 not identity";
                    if (MusicTheory.Semitones(mq).Length != rows) return q + " x " + f + " -> " + mq + " changes rows";
                    var eff = MusicTheory.EffectiveSemis(MusicTheory.Semitones(q), f, 0);
                    if (eff.Length != rows) return "EffectiveSemis " + q + " mood " + f + " rows " + eff.Length;
                }
            }
            if (MusicTheory.MoodMap(ChordQuality.Major9, 3) != ChordQuality.Dominant9) return "Major9 storm";
            if (MusicTheory.MoodMap(ChordQuality.Minor7, 1) != ChordQuality.Major7 || MusicTheory.MoodMap(ChordQuality.Sus4, 3) != ChordQuality.Sus4 || MusicTheory.MoodMap(ChordQuality.Major7, 2) != ChordQuality.Minor7) return "table";
            if (MusicTheory.Semitones(ChordQuality.Dominant9).Length != 5) return "Dominant9 rows";
            if (MusicTheory.QualityOf(new[] { 0, 4, 7, 10, 14 }) != ChordQuality.Dominant9) return "QualityOf 0,4,7,10,14";
            if (MusicTheory.QualityOf(new[] { 0, 4, 7, 10 }) != ChordQuality.Dominant7) return "Dominant7 detection changed";
            // v5 (M0 vibes): QualityIcon is the chord's vibe glyph — a dominant 9th is Spicy (v2: "diamondDot")
            if (MusicTheory.QualityIcon(ChordQuality.Dominant9) != Vibe.Icon(VibeKind.Spicy) || !MusicTheory.ChordName(60, new[] { 0, 4, 7, 10, 14 }).EndsWith("9")) return "icon/name";
            var untouched = new[] { 0, 3, 7, 10 }; var view = MusicTheory.EffectiveSemis(untouched, 0, 3);
            if (untouched[1] != 3) return "EffectiveSemis mutated its input";
            if (view[1] != 4) return "climate storm view of m7 should be dominant";
            return null;
        });

        Run(sb, "Hash deterministic + distinct + Hash01 range", () =>
        {
            if (Rhythm.Hash(1, 2, 3) != Rhythm.Hash(1, 2, 3) || Rhythm.Hash(1, 2, 3) == Rhythm.Hash(1, 2, 4)) return "hash";
            for (int i = 0; i < 1000; i++) { float v = Rhythm.Hash01(i, i * 7, 3); if (v < 0f || v >= 1f) return "Hash01 " + v; }
            return null;
        });

        Run(sb, "Templates: 6 grooves x 4 rows; EnergyScale; MoonWeight", () =>
        {
            if (Rhythm.Templates.Length != 6) return "count " + Rhythm.Templates.Length;
            foreach (var t in Rhythm.Templates) if (t.hits.Length != 4 || t.rot.Length != 4 || t.step.Length != 4) return t.name + " rows";
            if (Rhythm.EnergyScale(0) != 0.6f || Rhythm.EnergyScale(2) != 1.0f || Rhythm.EnergyScale(3) != 1.15f || Rhythm.EnergyScale(9) != 1.15f) return "EnergyScale";
            if (Rhythm.MoonWeight(0, 2, false) != -1 || Rhythm.MoonWeight(0, 0, false) != 1 || Rhythm.MoonWeight(1, 2, false) != 0 || Rhythm.MoonWeight(2, 3, true) != 1 || Rhythm.MoonWeight(3, 2, true) != 2) return "MoonWeight";
            return null;
        });

        Run(sb, "JsonUtility defaults (hits -1, moon -1, energy 2, repeat 1)", () =>
        {
            string c = JsonUtility.ToJson(new CubeState());
            if (!c.Contains("\"hits\":-1") || !c.Contains("\"moon\":-1") || !c.Contains("\"twinOf\":-1")) return c;
            string m = JsonUtility.ToJson(new MeasureState());
            if (!m.Contains("\"energy\":2") || !m.Contains("\"repeat\":1")) return m;
            return null;
        });

        Run(sb, "GlobalClock LoopIndex / Next16th", () =>
        {
            if (GlobalClock.IsPlaying) return "run while stopped";
            if (GlobalClock.LoopIndex != 0) return "LoopIndex " + GlobalClock.LoopIndex;
            if (Math.Abs(GlobalClock.Next16thBeat() - GlobalClock.SongBeatD) > 1e-9) return "Next16thBeat when stopped";
            if (Math.Abs(GlobalClock.DspOfNext16th() - GlobalClock.DspTimeOfBeat(GlobalClock.SongBeatD)) > 1e-6) return "DspOfNext16th";
            return null;
        });

        Run(sb, "MeshFactory Prism / Disc / SegmentRing", () =>
        {
            var p = MeshFactory.Prism(6, 0.7f, 0.5f);
            if (p.vertexCount != 6 * 6 + 2 || p.triangles.Length != 4 * 6 * 3) return "prism verts " + p.vertexCount + " tris " + p.triangles.Length / 3;
            if (p.bounds.min.y < -1e-4f || Mathf.Abs(p.bounds.max.y - 0.5f) > 1e-4f) return "prism y range " + p.bounds;
            var d = MeshFactory.Disc(3.9f, 48);
            if (d.vertexCount != 49 || d.triangles.Length != 48 * 3) return "disc";
            var r = MeshFactory.SegmentRing(0.972f, 1f, 16, 0.18f);
            if (r.vertexCount != 64 || r.triangles.Length != 16 * 6 || r.colors.Length != 64 || r.colors[0] != Color.white) return "segment ring";
            return null;
        });

        Run(sb, "Instruments: 10 rows, icons, pans, drums slot", () =>
        {
            if (Instruments.Count != 11 || Instruments.Colors.Length != 11 || Instruments.Icons.Length != 11 || Instruments.Pan.Length != 11 || Instruments.Volume.Length != 11 || Instruments.Muted.Length != 11) return "sizes";   // v9: + fx
            if (!Instruments.IsDrums(9) || Instruments.IsDrums(8) || Instruments.SlotOf(9) != SynthBank.DrumSlot || Instruments.SlotOf(4) != 4) return "slots";
            // v4 (U1's pastel palette): the drums are a light, near-neutral pearl (v2 had pure white); no role is black
            float dh, ds, dv; Color.RGBToHSV(Instruments.Colors[9], out dh, out ds, out dv);
            if (ds > 0.2f || dv < 0.85f) return "drums colour " + Instruments.Colors[9] + " is not a light neutral";
            for (int i = 0; i < Instruments.Count; i++) if (Instruments.Colors[i] == Color.black) return "colour " + i + " is black";
            return null;
        });

        Run(sb, "VoiceRules.Resolve: register, low-end law, window clamp, Long gate, sustaining clamp", () =>
        {
            if (fixture == null) return "fixture not loaded";
            AudioCube keys = null, pad = null;
            foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; if (keys == null && c.instrument == 0) keys = c; if (pad == null && c.instrument == 2) pad = c; }
            if (keys == null || pad == null) return "need a Keys and a Pad cube";
            var ev = new VoiceRules.NoteEvent[8];
            double now = GlobalClock.DspNow;
            double on = now + 0.1, end = now + 2.0;
            var hit = keys.Decide(0, 0, 0);
            if (!hit.fires || hit.node != 0) return "Decide step 0";
            int n = VoiceRules.Resolve(keys, keys.windows[0], keys.nodes[0], hit, 0, 0, on, 0.5, end, on + 0.5, ev);
            if (n != 1) return "keys events " + n;
            if (ev[0].midi < 48 || ev[0].midi < 41 || ev[0].midi > 88) return "keys midi " + ev[0].midi;
            if ((ev[0].midi - (keys.nodes[0].midi + SongManager.Transpose)) % 12 != 0) return "pitch class changed";
            if (ev[0].onDsp < on || ev[0].onDsp > on + 0.0081) return "jitter " + (ev[0].onDsp - on);
            if (ev[0].offDsp > end - 0.005 || ev[0].offDsp <= ev[0].onDsp) return "off " + ev[0].offDsp;
            if (Math.Abs((ev[0].offDsp - ev[0].onDsp) - 0.46) > 1e-6) return "normal gate " + (ev[0].offDsp - ev[0].onDsp);
            if (ev[0].vel < 1 || ev[0].vel > 127) return "vel " + ev[0].vel;
            Gate g = keys.gate; keys.gate = Gate.Long;
            n = VoiceRules.Resolve(keys, keys.windows[0], keys.nodes[0], hit, 0, 0, on, 0.5, end, on + 0.5, ev);
            keys.gate = g;
            if (Math.Abs(ev[0].offDsp - (on + 0.5 - 0.01)) > 1e-6) return "long gate ties to the next hit: off " + (ev[0].offDsp - on);
            keys.gate = Gate.Long;
            n = VoiceRules.Resolve(keys, keys.windows[0], keys.nodes[0], hit, 0, 0, on, 0.5, end, double.PositiveInfinity, ev);
            keys.gate = g;
            if (Math.Abs(ev[0].offDsp - (end - 0.005)) > 1e-6) return "long gate without next hit ends at the window end: " + (ev[0].offDsp - end);
            var ph = pad.Decide(0, 0, 0);
            Gate pg = pad.gate; pad.gate = Gate.Long;
            n = VoiceRules.Resolve(pad, pad.windows[0], pad.nodes[0], ph, 0, 0, on, 0.5, end, double.PositiveInfinity, ev);
            pad.gate = pg;
            double one16 = 0.25 / GlobalClock.BeatsPerSecond;
            if (Math.Abs(ev[0].offDsp - (end - one16)) > 1e-6) return "pad releases one 16th early: " + (end - ev[0].offDsp);
            if (VoiceRules.Fold(4, 40) < 28 || VoiceRules.Fold(4, 40) > 55) return "bass register";
            if (VoiceRules.Fold(1, 40) != 52 || VoiceRules.Fold(8, 36) != 48) return "low-end law";
            if (!VoiceRules.PreviewAllowed(keys.nodes[0], 0)) return "preview allowed when stopped";
            var pe = VoiceRules.PreviewEvent(0, keys.nodes[0], 1);
            if (pe.midi != VoiceRules.Fold(0, keys.nodes[0].midi + SongManager.Transpose + 12)) return "preview octave";
            return null;
        });

        return sb.ToString();
    }

    static string chordsBefore;
    static float island1Px, island1Pz;

    /// <summary>World contract checks (Play mode, after RunAll loaded the fixture). Leaves the song as it found it.</summary>
    public static string RunWorld()
    {
        var sb = new StringBuilder();
        var sm = SongManager.I;
        if (sm == null || !sm.HasSong) return "FAIL world: needs Play mode with a song\n";
        History.Push();
        int cubesBefore = SequenceMaster.Cubes.Count;

        Run(sb, "MoveMeasure(2,0) keeps cubes + pills, remaps, MoveMeasure(0,2) restores", () =>
        {
            var names = new List<string>(); foreach (var kb in sm.Islands) names.Add(kb.assignedChord);
            string third = names[2];
            var cubesOn2 = new List<int>(); foreach (var c in SequenceMaster.Cubes) if (c.assignedGridIndex == 2) cubesOn2.Add(c.id);
            sm.MoveMeasure(2, 0);
            if (SequenceMaster.Cubes.Count != cubesBefore) return "cubes " + SequenceMaster.Cubes.Count;
            if (sm.Islands.Count != names.Count || sm.Islands[0].measureIndex != 0) return "islands";
            if (UIManager.I != null && UIManager.I.PillCount != sm.Islands.Count) return "pills " + UIManager.I.PillCount;
            if (sm.Islands[0].assignedChord != third || sm.Islands[1].assignedChord != names[0]) return "order after move: " + sm.Islands[0].assignedChord + "," + sm.Islands[1].assignedChord;
            foreach (var c in SequenceMaster.Cubes) if (cubesOn2.Contains(c.id) && c.assignedGridIndex != 0) return "cube " + c.id + " not remapped to 0";
            for (int i = 0; i < sm.PlayOrder.Count; i++) if (sm.PlayOrder[i] != i) return "PlayOrder identity";
            sm.MoveMeasure(0, 2);
            for (int i = 0; i < names.Count; i++) if (sm.Islands[i].assignedChord != names[i]) return "restore failed at " + i;
            if (SequenceMaster.Cubes.Count != cubesBefore) return "cubes after restore " + SequenceMaster.Cubes.Count;
            return null;
        });

        Run(sb, "MoveIsland(1, +9, +5): MeasureStarts unchanged, position + placed", () =>
        {
            var before = new List<float>(sm.MeasureStarts);
            var kb = sm.Islands[1];
            island1Px = kb.px; island1Pz = kb.pz;
            float px = kb.px + 9f, pz = kb.pz + 5f;
            sm.MoveIsland(1, px, pz);
            for (int i = 0; i < before.Count; i++) if (!Mathf.Approximately(before[i], sm.MeasureStarts[i])) return "MeasureStarts changed";
            if (!Mathf.Approximately(kb.px, px) || !Mathf.Approximately(kb.pz, pz) || !kb.placed) return "position " + kb.px + "," + kb.pz + " placed " + kb.placed;
            if (Math.Abs(SongState.Capture().measures[1].px - px) > 1e-4f) return "capture does not carry px";
            return null;
        });

        Run(sb, "StampToAll from a selected cube adds one cube per other island; undo restores", () =>
        {
            AudioCube src = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.nodes.Count >= 2) { src = c; break; }
            if (src == null) return "no cube";
            PathManager.I.Select(src, true);
            int n = SequenceMaster.Cubes.Count;
            PathManager.I.StampToAll();
            int want = n + sm.Islands.Count - 1;
            if (SequenceMaster.Cubes.Count != want) return "cubes " + SequenceMaster.Cubes.Count + " want " + want;
            foreach (var kb in sm.Islands)
            {
                if (kb == src.Island) continue;
                bool found = false;
                foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == kb && c.instrument == src.instrument && c.nodes.Count == src.nodes.Count && c.step == src.step) { found = true; break; }
                if (!found) return "no stamp on island " + kb.measureIndex;
            }
            History.Push();
            History.Undo();
            // v4: the selection opened the inspector, whose focus loop plays the cube's column: back to a stopped transport for the checks below
            CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop();
            if (SequenceMaster.Cubes.Count != n) return "undo left " + SequenceMaster.Cubes.Count;
            return null;
        });

        Run(sb, "SetEnergy / SetMood / SetSleep / SetFill / SetRepeat rebuild, undo restores", () =>
        {
            var kb0 = sm.Islands[0];
            int rows = kb0.rows; var storedQ = MusicTheory.QualityOf(kb0.storedSemis);
            sm.SetEnergy(0, 3);
            if (sm.Islands[0].energy != 3) return "energy";
            sm.SetMood(0, 3);
            if (sm.Islands[0].mood != 3 || sm.Islands[0].rows != rows) return "mood rows " + sm.Islands[0].rows;
            if (sm.Islands[0].quality != MusicTheory.MoodMap(storedQ, 3)) return "mood quality " + sm.Islands[0].quality;
            if (MusicTheory.QualityOf(sm.Islands[0].storedSemis) != storedQ) return "storedSemis rewritten";
            if (SequenceMaster.Cubes.Count != cubesBefore) return "cubes after mood " + SequenceMaster.Cubes.Count;
            sm.SetSleep(0, true);
            if (!sm.Islands[0].sleep) return "sleep";
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.assignedGridIndex == 0 && (c.windows.Count != 1 || !c.windows[0].silent)) return "sleeping window not silent";
            sm.SetFill(0, true);
            if (!sm.Islands[0].fill) return "fill";
            sm.SetRepeat(0, 2);
            if (sm.Islands[0].repeat != 2) return "repeat";
            for (int i = 0; i < 5; i++) History.Undo();
            var k = sm.Islands[0];
            if (k.energy != 2 || k.mood != 0 || k.sleep || k.fill || k.repeat != 1) return "undo: energy " + k.energy + " mood " + k.mood + " sleep " + k.sleep + " fill " + k.fill + " repeat " + k.repeat;
            if (SequenceMaster.Cubes.Count != cubesBefore) return "cubes after undo " + SequenceMaster.Cubes.Count;
            return null;
        });

        Run(sb, "SetClimate(3) rebuilds every island as a view; SetClimate(0) restores", () =>
        {
            var before = new List<ChordQuality>(); foreach (var kb in sm.Islands) before.Add(kb.quality);
            var rows = new List<int>(); foreach (var kb in sm.Islands) rows.Add(kb.rows);
            SongManager.SetClimate(3);
            for (int i = 0; i < sm.Islands.Count; i++)
            {
                if (sm.Islands[i].rows != rows[i]) return "rows changed on island " + i;
                if (sm.Islands[i].quality != MusicTheory.MoodMap(before[i], 3)) return "island " + i + " quality " + sm.Islands[i].quality;
            }
            if (SongState.Capture().climate != 3) return "climate not captured";
            SongManager.SetClimate(0);
            for (int i = 0; i < sm.Islands.Count; i++) if (sm.Islands[i].quality != before[i]) return "restore island " + i;
            if (SequenceMaster.Cubes.Count != cubesBefore) return "cubes " + SequenceMaster.Cubes.Count;
            return null;
        });

        Run(sb, "Performance: half-time restores BPM exactly, drop/funnel/spotlight toggle", () =>
        {
            float bpm = GlobalClock.BPM;
            Performance.HoldHalf(true);
            if (!Mathf.Approximately(GlobalClock.BPM, Mathf.Clamp(bpm * 0.5f, 40f, 240f))) return "half bpm " + GlobalClock.BPM;
            Performance.HoldHalf(false);
            if (GlobalClock.BPM != bpm) return "restore " + GlobalClock.BPM + " != " + bpm;
            Performance.HoldDrop(true); if (!Performance.DropActive) return "drop"; Performance.HoldDrop(false);
            Performance.HoldFunnel(true); Performance.Tick(); Performance.HoldFunnel(false); Performance.Tick();
            var c0 = SequenceMaster.Cubes[0];
            Performance.HoldSpotlight(c0, true); if (Performance.SpotlightCube != c0) return "spotlight"; Performance.HoldSpotlight(c0, false);
            if (Performance.SpotlightCube != null) return "spotlight release";
            Performance.HoldStutter(true); if (!Performance.StutterActive) return "stutter"; Performance.HoldStutter(false);
            return null;
        });

        Run(sb, "TwinState variants", () =>
        {
            var c = SequenceMaster.Cubes[0];
            var t0 = c.TwinState(0);
            if (t0.twinOf != c.id || t0.id != 0 || t0.step != Mathf.Max(0, (int)c.step - 1) || t0.octave != Mathf.Min(1, c.octave + 1)) return "variant 0";
            var t1 = c.TwinState(1);
            if (t1.step != Mathf.Min(3, (int)c.step + 1)) return "variant 1 step " + t1.step;
            if (c.TwinState(2).reverse != !c.reverse) return "variant 2";
            if (c.TwinState(3).phase != c.phase + 1) return "variant 3";
            return null;
        });

        // last action: move island 1 back to where it started (the undo above may already have restored it); RunWorldFrame2 verifies its cubes followed (needs one frame)
        Run(sb, "MoveIsland back (ride-along verified by RunWorldFrame2)", () =>
        {
            var kb = sm.Islands[1];
            sm.MoveIsland(1, island1Px, island1Pz);
            return null;
        });
        CubeInspector.CloseImmediate();   // v3: Select opens the inspector; leave the world unlocked for the checks that follow

        return sb.ToString();
    }

    /// <summary>One frame after RunWorld: the cubes of island 1 followed it (max distance to RestPositionNow).</summary>
    public static string RunWorldFrame2()
    {
        var sm = SongManager.I;
        if (sm == null || sm.Islands.Count < 2) return "FAIL frame2: no song\n";
        float worst = 0f; int count = 0;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null || c.Island != sm.Islands[1]) continue;
            count++;
            worst = Mathf.Max(worst, (c.transform.position - c.RestPositionNow).magnitude);
        }
        return (worst < 0.01f ? "PASS" : "FAIL") + " cubes ride along with MoveIsland: " + count + " cubes, max offset " + worst.ToString("F4") + "\n";
    }

    // ================================================================== integration (SPEC 9.2, run at M3 by the INTEG package)
    /// <summary>Result of the last RunIntegration (filled when IntegrationDone).</summary>
    public static string IntegrationReport = "";
    public static bool IntegrationDone;
    /// <summary>Last capture helper result.</summary>
    public static string LastCapture = "";
    public static string CapturePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Captures"));

    /// <summary>
    /// Starts the SPEC 9.2 integration run (Play mode): loads the user's saved song (the fixture when there is no save), plays
    /// and records 12 s (Captures/integ_song.wav) while moving an island and a measure, asserts the harmonic safety of every
    /// logged cube event and the synth counters, then runs the static checks (Rider, Moon, mood/climate, HUD, every
    /// HudButton.onClick, load paths, random undo/redo) and reloads the saved song. Poll IntegrationDone / IntegrationReport.
    /// </summary>
    public static string RunIntegration()
    {
        if (SequenceMaster.I == null || SongManager.I == null) return "FAIL integration: needs Play mode\n";
        IntegrationDone = false; IntegrationReport = "";
        SequenceMaster.I.StartCoroutine(IntegrationRoutine());
        return "started";
    }


    static string Digits(string s) { if (string.IsNullOrEmpty(s)) return null; foreach (char ch in s) if (char.IsDigit(ch)) return s; return null; }
    /// <summary>The necklace dial: v4 U2's inspector card (legacy cubes only), else a v2 HUD dial.</summary>
    static NecklaceDial HudDial()
    {
        if (InspectorCard.Live && InspectorCard.Dial != null) return InspectorCard.Dial;
        var hud = GameObject.Find("HUDCanvas");
        return hud != null ? hud.GetComponentInChildren<NecklaceDial>(true) : null;
    }
    static string PathOf(Transform t) { var sb = new StringBuilder(t.name); while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); } return sb.ToString(); }

    static IEnumerator IntegrationRoutine()
    {
        var sb = new StringBuilder();
        var sm = SongManager.I;
        // ---- 1. the user's saved song (the fixture when there is no save)
        bool loaded = false;
        try { loaded = SongIO.Load(); } catch (Exception e) { sb.Append("FAIL SongIO.Load: ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n'); }
        if (!loaded) { SongState.Apply(SongState.FromJson(File.ReadAllText(FixturePath))); History.Reset(); History.Push(); }
        sb.Append("PASS ").Append(loaded ? "saved song loaded: " : "fixture loaded (no save): ").Append(sm.Islands.Count).Append(" islands, ")
          .Append(SequenceMaster.Cubes.Count).Append(" cubes, ").Append(sm.Moons.Count).Append(" moons, ").Append(GlobalClock.BPM).Append(" bpm\n");
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        yield return null;

        // ---- 2. 12 s play + recording, with an island move and a measure move while playing
        GlobalClock.Stop(); GlobalClock.Seek(0);
        yield return null;
        long logStart = Performance.LoggedTotal;
        int lateBefore = Synth.LateEvents, errBefore = Synth.Errors;
        string wav = Path.Combine(CapturePath, "integ_song.wav");
        Synth.StartRecording(12f);
        GlobalClock.Play();
        yield return null;
        Run(sb, "punch strip active while playing", () =>
        {
            var hud = GameObject.Find("HUDCanvas"); if (hud == null) return "no HUDCanvas";
            var punch = hud.transform.Find("HUD/Transport/Punch"); if (punch == null) return "no punch strip";
            return punch.gameObject.activeSelf ? null : "inactive while playing";
        });

        while (!Synth.RecordingDone) yield return null;
        yield return null;
        int lateAfter = Synth.LateEvents, errAfter = Synth.Errors;
        string stats = Synth.Stats();
        bool saved = Synth.SaveRecording(wav);
        sb.Append(saved ? "PASS " : "FAIL ").Append("recording saved: ").Append(wav).Append(" | ").Append(stats).Append('\n');
        Run(sb, "Synth late 0 / errors 0 over the 12 s play", () => lateAfter - lateBefore == 0 && errAfter - errBefore == 0 ? null : "late +" + (lateAfter - lateBefore) + " errors +" + (errAfter - errBefore));

        // harmonic safety (SPEC 2.9) over the dispatch log
        Run(sb, "harmonic safety: every logged cube event inside its window, off <= windowEnd - 5 ms, pitch class a chord tone of the window's island", () =>
        {
            int checkedEv = 0, bad = 0, drums = 0; string first = null;
            for (int i = 0; i < Performance.LoggedCount; i++)
            {
                var e = Performance.Logged(i);
                if (e.owner < 16 || e.winEndDsp <= 0.0) continue;   // previews / auditions carry no window
                checkedEv++;
                // v6 (A): the shared test (VoiceRules.HarmonicCheck) — the v2 guarantee, with the low-end law by GROUP (every bass voice may go
                // below 48, shifted with a lowered island) and keyboard islands' keys exempt from the chord-tone law (SPEC v6 §4.3)
                string why = VoiceRules.HarmonicCheck(e);
                if (why == null && SynthBank.Def(e.slot).drums) drums++;
                if (why != null) { bad++; if (first == null) first = "event " + i + " slot " + e.slot + " midi " + e.midi + ": " + why; }
            }
            if (checkedEv == 0) return "no cube events logged (" + Performance.LoggedCount + " in the buffer, " + (Performance.LoggedTotal - logStart) + " since play)";
            return bad == 0 ? null : bad + " of " + checkedEv + " events violate the guarantee; first: " + first;
        });
        sb.Append("INFO logged events since play: ").Append(Performance.LoggedTotal - logStart).Append(" (buffer ").Append(Performance.LoggedCount).Append(")\n");

        // ---- 2b. world edits while the song keeps playing (after the clean recording)
        // MoveIsland while playing (item 5): a non-lit island moves; no CancelOwner, MeasureStarts unchanged, cubes ride along
        KeyBlock moved = null; float mpx = 0f, mpz = 0f; int cancelBefore = 0; var startsBefore = new List<float>();
        string moveWhy = null;
        var localBefore = new Dictionary<AudioCube, Vector3>();   // v7: each cube in its island's space before the move
        try
        {
            int n = sm.Islands.Count;
            int lit = Mathf.Clamp(sm.LitIsland, 0, n - 1);
            moved = sm.Islands[(lit + 2) % n];
            for (int step = 0; step < n - 1; step++)   // the first non-lit island (from lit + 2 on) that carries cubes: the user's song has empty islands
            {
                int k = 1 + (step + 1) % (n - 1);
                var cand = sm.Islands[(lit + k) % n]; if (cand == null) continue;
                bool has = false; foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == cand) { has = true; break; }
                if (has) { moved = cand; break; }
            }
            mpx = moved.px; mpz = moved.pz;
            cancelBefore = AudioCube.CancelCount;
            startsBefore.AddRange(sm.MeasureStarts);
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.Island == moved) localBefore[c] = moved.transform.InverseTransformPoint(c.transform.position);
            sm.MoveIsland(moved, mpx + 6f, mpz + 4f);
        }
        catch (Exception e) { moveWhy = e.GetType().Name + " " + e.Message; }
        yield return null; yield return null;
        // v7 (SPEC v7 §12.1, A updated this assertion): a cube outside its window HOLDS where its last window left it and travels home by MOVING
        // (H's hold-then-return), so it is not always at RestPositionNow; the meaning — the cubes ride along with their island — is checked in the
        // island's space: a cube resting there is within 0.01 of RestPositionNow, a holding / travelling one moved with the island (≤ 0.25 u of
        // its own motion over the two frames; left behind it would be 7.2 u off)
        Run(sb, "MoveIsland while playing: no CancelOwner, MeasureStarts unchanged, the cubes ride along (resting ones within 0.01 of RestPositionNow; v7: holding / travelling ones moved with the island)", () =>
        {
            if (moveWhy != null) return moveWhy;
            if (!GlobalClock.IsPlaying) return "stopped";
            if (AudioCube.CancelCount != cancelBefore) return "CancelPending ran " + (AudioCube.CancelCount - cancelBefore) + " times";
            for (int i = 0; i < startsBefore.Count; i++) if (!Mathf.Approximately(startsBefore[i], sm.MeasureStarts[i])) return "MeasureStarts changed";
            if (!Mathf.Approximately(moved.px, mpx + 6f) || !Mathf.Approximately(moved.pz, mpz + 4f)) return "position not applied";
            float worst = 0f, worstMoving = 0f; int cnt = 0, moving = 0;
            foreach (var c in SequenceMaster.Cubes)
            {
                if (c == null || c.Island != moved) continue;
                cnt++;
                bool inMotion = c.Holding || c.Tripping || c.Gliding || c.Flinging;
                Vector3 before;
                if (inMotion && localBefore.TryGetValue(c, out before)) { moving++; worstMoving = Mathf.Max(worstMoving, (moved.transform.InverseTransformPoint(c.transform.position) - before).magnitude); }
                else worst = Mathf.Max(worst, (c.transform.position - c.RestPositionNow).magnitude);
            }
            if (cnt == 0) return "no cubes on the moved island";
            if (worst > 0.01f) return cnt + " cubes, max offset of a resting cube " + worst.ToString("F4");
            if (worstMoving > 0.25f) return moving + " holding / travelling cubes left behind: " + worstMoving.ToString("F3") + " u in the island's space";
            sm.MoveIsland(moved, mpx, mpz);
            return null;
        });

        // MoveMeasure while playing (item 6): the lit island keeps its local beat
        Run(sb, "MoveMeasure while playing re-seeks into the same local beat of the same island", () =>
        {
            int n = sm.Islands.Count; if (n < 3) return "needs 3 islands";
            int lit = Mathf.Clamp(sm.ActiveMeasureIndex(GlobalClock.SongBeat), 0, n - 1);
            double local = GlobalClock.SongBeatD - sm.MeasureStarts[lit];
            string chord = sm.Islands[lit].assignedChord;
            int from = lit == 0 ? n - 1 : 0, to = lit == 0 ? 0 : n - 1, newLit = lit == 0 ? 1 : lit - 1;
            sm.MoveMeasure(from, to);
            if (!GlobalClock.IsPlaying) return "stopped by the move";
            if (sm.Islands[newLit].assignedChord != chord) return "lit island not at " + newLit;
            double local2 = GlobalClock.SongBeatD - sm.MeasureStarts[newLit];
            if (Math.Abs(local2 - local) > 1e-3) return "local beat " + local.ToString("F3") + " -> " + local2.ToString("F3");
            if (sm.ActiveMeasureIndex(GlobalClock.SongBeat) != newLit) return "playhead not on the moved island";
            History.Undo();
            if (sm.Islands[lit].assignedChord != chord) return "undo did not restore the order";
            return null;
        });

        GlobalClock.Stop();
        yield return null;
        var snap = SongState.Capture();

        // ---- 3. static checks (stopped)
        // v7 (SPEC v7 §21 — "just keep cubes on each grid"; A updated this assertion): v4's Rider had one window per island in Route order (its path
        // replayed on every island); §21 retired the flights: the flag stays in the file and is ignored — a rider's windows are its home island's
        Run(sb, "Rider (§21): a rider plays only on its own grid (its windows = its home windows, all on its island); sleep silences residents and the rider's own window, never the Moon", () =>
        {
            AudioCube c = null; foreach (var x in SequenceMaster.Cubes) if (x != null && !x.IsDrums && !x.IsOnMoon && !x.rider) { c = x; break; }
            if (c == null) return "no pitched cube";
            KeyBlock own = c.Island; int ownIdx = sm.Islands.IndexOf(own), home = c.windows.Count;
            if (ownIdx < 0) return "no home island";
            c.rider = true; sm.RecomputeMeasureStarts();
            try
            {
                if (c.windows.Count != home) return "§21: rider windows " + c.windows.Count + " (home " + home + ")";
                foreach (var w in c.windows) if (w.island != own) return "§21: a rider window on '" + (w.island != null ? w.island.assignedChord : "null") + "', not its own grid";
                sm.SetSleep(ownIdx, true);
                foreach (var w in c.windows) if (!w.silent) return "sleep did not silence the rider's own window";
                foreach (var r in SequenceMaster.Cubes)
                    if (r != null && r.moon < 0 && r.Island == own) foreach (var w in r.windows) if (!w.silent) return "resident of the sleeping island not silent";
                sm.SetSleep(ownIdx, false);
                foreach (var w in c.windows) if (w.silent) return "still silent after waking";
            }
            finally { c.rider = false; sm.RecomputeMeasureStarts(); }
            if (c.windows.Count != home) return "windows after clearing the rider: " + c.windows.Count;
            return null;
        });

        Run(sb, "Moon cube: one window per bar (never silent), BeatsPerBar/StepBeats hits per bar, drums forced", () =>
        {
            int m = sm.AddMoon();
            if (m < 0) return "AddMoon refused with " + sm.Moons.Count + " moons";
            var cs = new CubeState { instrument = 0, measure = 0, moon = m, xs = new[] { 3 }, zs = new[] { 0 }, rests = new[] { false }, step = (int)StepLen.Quarter, gate = 1, mode = 0, volume = 1f, hits = -1, twinOf = -1 };
            var c = PathManager.I.RestoreCube(cs);
            if (c == null) return "RestoreCube on the Moon failed";
            sm.RecomputeMeasureStarts();
            if (!c.IsDrums || !c.IsOnMoon || c.Moon != sm.Moons[m]) return "not a drum cube on the Moon";
            int bpb = Mathf.Max(1, GlobalClock.BeatsPerBar);
            int wantWindows = Mathf.Max(1, Mathf.RoundToInt(sm.TotalBeats / bpb));
            if (c.windows.Count != wantWindows) return "windows " + c.windows.Count + " want " + wantWindows;
            foreach (var w in c.windows) if (w.silent || w.island != sm.Moons[m] || !Mathf.Approximately(w.length, bpb)) return "window shape";
            int wantHits = Mathf.RoundToInt(bpb / c.StepBeats);
            var steps = c.HitSteps16(0, 0);
            if (steps.Count != wantHits) return "HitSteps16 " + Ints(steps) + " want " + wantHits + " hits";
            int fires = 0; for (int k = 0; k < c.StepsInWindow(0); k++) if (c.Decide(0, k, 0).fires) fires++;
            if (fires != wantHits) return "Decide fires " + fires + " per bar";
            sm.SetSleep(0, true);
            foreach (var w in c.windows) if (w.silent) return "sleep silenced the Moon";
            sm.SetSleep(0, false);
            sm.RemoveMoon(m);
            if (sm.Moons.Count != 0) return "RemoveMoon left " + sm.Moons.Count;
            return null;
        });

        Run(sb, "mood / climate keep rows and leave semis untouched in the JSON; undo restores", () =>
        {
            var before = SongState.Capture();
            var rows = new List<int>(); foreach (var kb in sm.Islands) rows.Add(kb.rows);
            sm.SetMood(0, 3);
            SongManager.SetClimate(2);
            var after = SongState.Capture();
            if (after.measures.Length != before.measures.Length) return "measures";
            for (int i = 0; i < before.measures.Length; i++)
            {
                if (Ints(before.measures[i].semis) != Ints(after.measures[i].semis)) return "semis rewritten on island " + i + ": " + Ints(after.measures[i].semis);
                if (sm.Islands[i].rows != rows[i]) return "rows changed on island " + i;
            }
            if (sm.Islands[0].mood != 3 || SongManager.Climate != 2 || after.climate != 2 || after.measures[0].mood != 3) return "state not applied";
            if (sm.Islands[0].quality != MusicTheory.MoodMap(MusicTheory.QualityOf(before.measures[0].semis), 3)) return "island 0 view " + sm.Islands[0].quality;
            History.Undo(); History.Undo();
            if (sm.Islands[0].mood != 0 || SongManager.Climate != 0) return "undo: mood " + sm.Islands[0].mood + " climate " + SongManager.Climate;
            return null;
        });

        // v4: the necklace lives in U2's inspector card ("more" drawer, legacy cubes only: InspectorCard.Dial) and the card follows the
        // cube in its own LateUpdate, so the dial is read a few frames after each change
        AudioCube hudCube = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.nodes.Count >= 2 && !x.IsDrums && !x.HasDurations) { hudCube = x; break; }
        int expectN0 = -1, dialN0 = -1, dialN8 = -1; bool hitsReach = false, rotReach = false, dialRefreshed = false, maskToggle = false, dialFound = false;
        var hudGo0 = GameObject.Find("HUDCanvas");
        var punch0 = hudGo0 != null ? hudGo0.transform.Find("HUD/Transport/Punch") : null;
        bool punchFound = punch0 != null, punchWhileStopped = punch0 != null && punch0.gameObject.activeSelf && !GlobalClock.IsPlaying;   // read before the inspector's focus loop starts the clock
        if (hudCube != null)
        {
            var hc = hudCube; var st0 = hc.step; expectN0 = hc.StepsPerBar;
            PathManager.I.Select(hc, true);
            float w0 = Time.realtimeSinceStartup;   // the card pops in after the fly-in: wait until it is up (as V3Fixes does)
            while (!(CubeInspector.IsOpen && InspectorCard.Shown >= 0.99f) && Time.realtimeSinceStartup - w0 < 4f) yield return null;
            if (InspectorCard.I != null) InspectorCard.I.SetDrawer(true);
            w0 = Time.realtimeSinceStartup;
            while (InspectorCard.DrawerShown < 0.99f && Time.realtimeSinceStartup - w0 < 1.5f) yield return null;
            for (int f = 0; f < 3; f++) yield return null;
            var dial = HudDial(); dialFound = dial != null;
            if (dial != null)
            {
                dialN0 = dial.n;
                hc.SetStep(StepLen.Eighth);
                for (int f = 0; f < 3; f++) yield return null;
                dialN8 = dial.n;
                dial.onHits(3); hitsReach = hc.hits == 3;
                dial.onRot(2); rotReach = hc.rot == 2;
                for (int f = 0; f < 3; f++) yield return null;
                dialRefreshed = dial.hits == 3 && dial.rot == 2;
                dial.onBeadToggle(0); maskToggle = hc.hits == -2;
            }
            hc.SetHits(-1); hc.SetRot(0); hc.SetMask(0); hc.SetStep(st0);
        }
        Run(sb, "HUD: a swatch + a mute per group (v9: 11), punch strip hidden when stopped, NecklaceDial.n == StepsPerBar (follows SetStep), dial setters reach the cube, sticker ring of 7 opens and picks, digits only on the tempo label among the v2 controls (v5: the ×n repeat sticker; v7: the v7 UI may count)", () =>
        {
            var hud = GameObject.Find("HUDCanvas"); if (hud == null) return "no HUDCanvas";
            var palette = hud.transform.Find("HUD/Palette"); if (palette == null) return "no Palette";
            int picks = 0, mutes = 0; foreach (var b in palette.GetComponentsInChildren<HudButton>(true)) { if (b.name == "Pick") picks++; else if (b.name == "Mute") mutes++; }
            if (picks != Instruments.Count || mutes != Instruments.Count) return "swatches " + picks + " mutes " + mutes + " (groups " + Instruments.Count + ")";   // v9: a swatch + a mute per group (11 with fx)
            if (!punchFound) return "no punch strip";
            if (punchWhileStopped) return "punch strip active while stopped";
            var c = hudCube;
            if (c == null) return "no legacy cube";
            if (!dialFound) return "no NecklaceDial (v4: InspectorCard.Dial for a legacy cube)";
            if (dialN0 != expectN0) return "dial n " + dialN0 + " != StepsPerBar " + expectN0;
            if (dialN8 != 8) return "dial n after SetStep(Eighth) " + dialN8;
            if (!hitsReach) return "onHits did not reach SetHits";
            if (!rotReach) return "onRot did not reach SetRot";
            if (!dialRefreshed) return "dial not refreshed from the cube";
            if (!maskToggle) return "bead toggle did not switch to the explicit mask";
            UIManager.I.OpenStickerRing(c, 1);
            var ring = hud.GetComponentInChildren<PopRing>(true); if (ring == null) return "sticker ring did not open";
            if (ring.Buttons.Count != 7) return "sticker ring has " + ring.Buttons.Count + " glyphs";
            ring.Pick(2);
            if (c.ModOf(1) != 4) return "pick 2 (ratchet2) gave mod " + c.ModOf(1);
            c.SetMod(1, 0);
            UIManager.I.CloseRings();
            foreach (var t in hud.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (Digits(t.text) == null) continue;
                // v5 (SPEC §2.5 / §7, package U): the island header's repeat button wears a "×n" sticker — the one other digit allowed
                if (PathOf(t.transform).Contains("/IslandHeader/") && t.text.Trim().StartsWith("\u00d7")) continue;
                // v7 (SPEC v7 §0 / §13.4 / §7.2, A updated this assertion): the v7 UI language allows digits — counts and captions the user asked for
                // (the rail's measure numbers, "1 measure", the stairs header's steps, the phrase lengths); the v2 rule — icons, no numbers — still holds
                // for the v2 controls it was written for: the palette, the punch strip, the necklace dial and the sticker ring
                string tp = PathOf(t.transform);
                bool v2Control = tp.Contains("/Palette/") || tp.Contains("Punch") || tp.Contains("Dial") || tp.Contains("PopRing") || tp.Contains("Sticker");
                if (!v2Control) continue;
                if (t.name != "Label" || t.transform.parent == null || t.transform.parent.name != "Tempo") return "digits on a v2 control: " + tp + " = '" + t.text + "'";
            }
            return null;
        });
        PathManager.I.Deselect();
        CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop();   // v4: the selection's inspector engaged the focus loop
        yield return null;

        // every HudButton.onClick (non-structural first, then the structural ones in a sensible order)
        var errors = new List<string>(); int invoked = 0;
        {
            var hudGo = GameObject.Find("HUDCanvas");
            var root = hudGo != null ? hudGo.transform.Find("HUD") : null;
            if (root == null) sb.Append("FAIL HudButton sweep: no HUD root\n");
            else
            {
                AudioCube c = null; foreach (var x in SequenceMaster.Cubes) if (x != null && x.nodes.Count >= 2 && !x.IsDrums) { c = x; break; }
                if (c != null) PathManager.I.Select(c, true);
                UIManager.I.SelectMeasure(0, false);
                // v3: Home (opens the menu + writes the autosave) and Present (the waterfall takes over the camera and the keys) are
                // pressed once each at the end, closed again, and the autosave the Home press wrote is put back as it was
                var skip = new HashSet<string> { "NewSong", "Save", "Load", "Remove", "Delete", "Trash", "Dice", "AddMeasure", "AddMoon", "Duplicate", "Undo", "Redo", "Rewind", "Play", "Stop", "Chord", "Quality", "ChordWheel", "MoreToggle", "Home", "Present" };
                invoked += InvokeButtons(root, null, skip, errors);
                yield return null;
                invoked += InvokeNamed(root, "MoreToggle", errors, 2);   // both "more" toggles, twice: the PlayerPrefs state is left as found
                yield return null;
                invoked += InvokeNamed(root, "MoreToggle", errors, 2);
                yield return null;
                foreach (string name in new[] { "Chord", "Wedge3", "Quality", "ChordWheel", "AddMeasure", "Duplicate", "Remove", "AddMoon", "Moon0", "Dice", "Trash", "Delete", "Undo", "Redo", "Rewind", "Play", "Stop", "Load", "NewSong" })
                {
                    invoked += InvokeNamed(root, name, errors, 1);
                    if (name == "NewSong" && InterfaceController.I != null) InterfaceController.I.Hide();
                    yield return null;
                }
                string autoPath = SongIO.AutosavePath;
                string autoBefore = File.Exists(autoPath) ? File.ReadAllText(autoPath) : null;
                invoked += InvokeNamed(root, "Present", errors, 1);
                yield return null; yield return null;
                if (Presenter.Active) Presenter.Exit();
                yield return null;
                invoked += InvokeNamed(root, "Home", errors, 1);
                yield return null; yield return null;
                if (MainMenu.IsShown) MainMenu.Hide();
                WorldInput.Unlock("menu"); WorldInput.Unlock("prompt");
                try { if (autoBefore != null) File.WriteAllText(autoPath, autoBefore); else if (File.Exists(autoPath)) File.Delete(autoPath); }
                catch (Exception e) { errors.Add("autosave restore: " + e.Message); }
                yield return null;
            }
        }
        sb.Append(errors.Count == 0 ? "PASS " : "FAIL ").Append("every HudButton.onClick runs without exceptions: ").Append(invoked).Append(" invocations");
        foreach (var e in errors) sb.Append("\n    ").Append(e);
        sb.Append('\n');
        GlobalClock.Stop();
        yield return null;

        // ---- 4. load paths
        Run(sb, "load paths: v1 fixture, v2 save written by Capture, a song with a Moon and Moon cubes, 20 random undo/redo steps", () =>
        {
            var v1 = SongState.FromJson(File.ReadAllText(FixturePath));
            SongState.Apply(v1);
            if (sm.Islands.Count != v1.measures.Length || SequenceMaster.Cubes.Count != v1.cubes.Length) return "v1: " + sm.Islands.Count + " islands " + SequenceMaster.Cubes.Count + " cubes";
            var v2 = SongState.Capture();
            if (v2.version != SongState.CurrentVersion) return "capture version " + v2.version + " (want " + SongState.CurrentVersion + ")";   // v4: CurrentVersion 3
            string json = v2.ToJson();
            var back = SongState.FromJson(json);
            if (back.IsLegacy || back.measures.Length != v2.measures.Length || back.cubes.Length != v2.cubes.Length) return "v2 json round trip";
            SongState.Apply(back);
            if (sm.Islands.Count != v2.measures.Length || SequenceMaster.Cubes.Count != v2.cubes.Length) return "v2 apply: " + sm.Islands.Count + " islands " + SequenceMaster.Cubes.Count + " cubes";
            for (int i = 0; i < sm.Islands.Count; i++) if (!sm.Islands[i].placed || Math.Abs(sm.Islands[i].px - v2.measures[i].px) > 1e-4f) return "v2 apply lost island " + i + "'s position";
            int m = sm.AddMoon();
            if (m < 0) return "AddMoon";
            var mc = PathManager.I.RestoreCube(new CubeState { instrument = 9, measure = 0, moon = m, xs = new[] { 3, 4 }, zs = new[] { 1, 1 }, rests = new[] { false, false }, step = (int)StepLen.Quarter, gate = 1, mode = 0, volume = 1f, hits = 2, twinOf = -1 });
            if (mc == null) return "Moon cube";
            sm.RecomputeMeasureStarts(); History.Push();
            var withMoon = SongState.Capture();
            if (withMoon.moons == null || withMoon.moons.Length != 1 || withMoon.moons[0].kind != 1) return "moons not captured";
            int moonCubes = 0; foreach (var cs in withMoon.cubes) if (cs.moon == 0) moonCubes++;
            if (moonCubes != 1) return "moon cubes captured " + moonCubes;
            SongState.Apply(SongState.FromJson(withMoon.ToJson()));
            if (sm.Moons.Count != 1) return "moons after reload " + sm.Moons.Count;
            int live = 0; foreach (var x in SequenceMaster.Cubes) if (x != null && x.IsOnMoon && x.Moon == sm.Moons[0] && x.windows.Count > 1) live++;
            if (live != 1) return "moon cubes after reload " + live;
            var rng = new System.Random(7);
            int undos = 0, redos = 0;
            for (int i = 0; i < 20; i++)
            {
                if (rng.Next(2) == 0) { History.Undo(); undos++; } else { History.Redo(); redos++; }
                if (!sm.HasSong) return "song lost at step " + i;
            }
            return null;
        });
        yield return null;

        // ---- 5. leave the user's song loaded and stopped
        try { SongState.Apply(snap); History.Reset(); History.Push(); if (!SongIO.Load()) { SongState.Apply(snap); History.Reset(); History.Push(); } }
        catch (Exception e) { sb.Append("FAIL reload at the end: ").Append(e.Message).Append('\n'); }
        GlobalClock.Stop();
        CubeInspector.CloseImmediate();
        if (Presenter.Active) Presenter.Exit();
        if (MainMenu.IsShown) MainMenu.Hide();
        if (PathManager.I != null) { PathManager.I.Deselect(); PathManager.I.SelectInstrument(0); }
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        sb.Append("INFO end: ").Append(sm.Islands.Count).Append(" islands, ").Append(SequenceMaster.Cubes.Count).Append(" cubes, ").Append(sm.Moons.Count).Append(" moons, playing ").Append(GlobalClock.IsPlaying).Append('\n');
        IntegrationReport = sb.ToString();
        IntegrationDone = true;
    }

    /// <summary>Invokes onClick of every HudButton under <paramref name="root"/> (names in <paramref name="skip"/> and Wedge* excluded, or only <paramref name="only"/>), catching exceptions.</summary>
    static int InvokeButtons(Transform root, HashSet<string> only, HashSet<string> skip, List<string> errors)
    {
        var list = new List<KeyValuePair<string, Action>>();
        foreach (var b in root.GetComponentsInChildren<HudButton>(true))
        {
            if (b == null || b.onClick == null) continue;
            string n = b.name;
            if (skip != null && (skip.Contains(n) || n.StartsWith("Wedge"))) continue;
            if (only != null && !only.Contains(n)) continue;
            list.Add(new KeyValuePair<string, Action>(PathOf(b.transform), b.onClick));
        }
        int count = 0;
        foreach (var kv in list)
        {
            try { kv.Value(); count++; }
            catch (Exception e) { errors.Add(kv.Key + ": " + e.GetType().Name + " " + e.Message); }
        }
        return count;
    }

    /// <summary>Invokes onClick of every live HudButton named <paramref name="name"/>, <paramref name="times"/> times each.</summary>
    static int InvokeNamed(Transform root, string name, List<string> errors, int times)
    {
        int count = 0;
        foreach (var b in root.GetComponentsInChildren<HudButton>(true))
        {
            if (b == null || b.name != name || b.onClick == null) continue;
            string path = PathOf(b.transform);
            for (int i = 0; i < times; i++)
            {
                try { b.onClick(); count++; }
                catch (Exception e) { errors.Add(path + ": " + e.GetType().Name + " " + e.Message); }
            }
        }
        return count;
    }

    // ================================================================== tempo / transpose probe (FIX package: harmonic safety across tempo changes)
    public static string TempoReport = ""; public static bool TempoDone;

    /// <summary>
    /// Play mode: seeks to the first island that carries a Pad cube, plays it at half tempo for 1 s (its pads sustain toward a window end
    /// ~3.75 beats away), then restores the tempo so the window now ends ~1 s later: every live note-off must have moved before the new
    /// window end, the clock must not jump, no late/errored synth events; a transpose change while playing must release every live note.
    /// Records Captures/fix_tempo.wav (6 s from the seek). Poll TempoDone / TempoReport.
    /// </summary>
    public static string RunTempoCheck()
    {
        if (SequenceMaster.I == null || SongManager.I == null || !SongManager.I.HasSong) return "FAIL tempo: needs Play mode with a song\n";
        TempoDone = false; TempoReport = "";
        SequenceMaster.I.StartCoroutine(TempoRoutine());
        return "started";
    }

    static IEnumerator TempoRoutine()
    {
        var sb = new StringBuilder();
        var sm = SongManager.I;
        var snap = SongState.Capture();
        AudioCube pad = null;
        foreach (var c in SequenceMaster.Cubes) if (c != null && c.instrument == 2 && !c.IsOnMoon && !c.rider && c.nodes.Count > 0) { pad = c; break; }
        if (pad == null || pad.assignedGridIndex < 0 || pad.assignedGridIndex >= sm.MeasureStarts.Count) { TempoReport = "FAIL tempo: no Pad cube on an island\n"; TempoDone = true; yield break; }
        int padIsland = pad.assignedGridIndex;
        // two Long notes per window (Half steps): the second one sustains to the window end - one 16th, the case a faster tempo must cut
        pad.SetStep(StepLen.Half); pad.SetGate(Gate.Long);
        GlobalClock.Stop();
        float bpm0 = GlobalClock.BPM;
        GlobalClock.Seek(sm.MeasureStarts[padIsland]);
        int lateBefore = Synth.LateEvents, errBefore = Synth.Errors;
        GlobalClock.SetBPM(bpm0 * 0.5f);
        Synth.StartRecording(6f);
        double recDsp = GlobalClock.DspNow;
        GlobalClock.Play();
        double targetBeat = sm.MeasureStarts[padIsland] + 2.5;
        float t0 = Time.realtimeSinceStartup;
        while (GlobalClock.SongBeatD < targetBeat && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        string before = AudioCube.LiveReport();
        double worstBefore = AudioCube.WorstLiveOverrun();
        double beatBefore = GlobalClock.BeatNow, dspBefore = GlobalClock.DspNow;
        double winEndDspBefore = GlobalClock.DspTimeOfBeat(sm.MeasureStarts[padIsland] + sm.Islands[padIsland].LengthBeats);
        GlobalClock.SetBPM(bpm0);                     // the window end moves closer: the sounding pad must not ring past it
        double beatAfter = GlobalClock.BeatNow, dspAfter = GlobalClock.DspNow;
        double winEndDspAfter = GlobalClock.DspTimeOfBeat(sm.MeasureStarts[padIsland] + sm.Islands[padIsland].LengthBeats);
        string after = AudioCube.LiveReport();
        double worstAfter = AudioCube.WorstLiveOverrun();
        Run(sb, "tempo x2 while a Long pad sustains toward the window end: the end moved earlier and every live note-off sits before it", () =>
        {
            if (winEndDspAfter >= winEndDspBefore - 0.3) return "window end did not move earlier: " + (winEndDspBefore - winEndDspAfter).ToString("F3") + " s";
            if (worstBefore <= -1.0) return "no live notes before the change (" + before + ")";
            if (worstBefore > 1e-3) return "live notes already past their window before the change: " + before;
            if (worstAfter <= -1.0) return "no live notes after the change (" + after + ")";
            if (worstAfter > 1e-3) return "note-off past the new window end by " + worstAfter.ToString("F4") + " s (" + after + ")";
            return null;
        });
        Run(sb, "SetBPM while playing keeps the beat continuous (no rewind by the intra-frame drift)", () =>
        {
            double dt = dspAfter - dspBefore;
            double expect = beatBefore + dt * bpm0 * 0.5 / 60.0;
            return Math.Abs(beatAfter - expect) < 1e-6 ? null : "beat " + beatBefore.ToString("F5") + " -> " + beatAfter.ToString("F5") + " (dsp advanced " + dt.ToString("F5") + " s)";
        });
        sb.Append("INFO live before: ").Append(before).Append(" | after: ").Append(after)
          .Append(" | window end moved ").Append((winEndDspBefore - winEndDspAfter).ToString("F3")).Append(" s earlier, new end at ")
          .Append((winEndDspAfter - recDsp).ToString("F3")).Append(" s into the recording (old end ").Append((winEndDspBefore - recDsp).ToString("F3")).Append(" s)\n");
        yield return new WaitForSecondsRealtime(1.2f);
        // a transpose while playing: every cube releases its notes (CancelPending) and re-schedules under the new pitch
        int cancels = AudioCube.CancelCount;
        SongManager.SetTranspose(2);
        int cubes = 0; foreach (var c in SequenceMaster.Cubes) if (c != null) cubes++;
        string liveAfterTranspose = AudioCube.LiveReport();
        Run(sb, "SetTranspose while playing cancels every cube's notes", () => AudioCube.CancelCount - cancels >= cubes ? (liveAfterTranspose.StartsWith("live=0") ? null : "live notes left: " + liveAfterTranspose) : "CancelPending ran " + (AudioCube.CancelCount - cancels) + " times for " + cubes + " cubes");
        SongManager.SetTranspose(0);
        while (!Synth.RecordingDone) yield return null;
        yield return null;
        string wav = Path.Combine(CapturePath, "fix_tempo.wav");
        bool saved = Synth.SaveRecording(wav);
        sb.Append(saved ? "PASS " : "FAIL ").Append("recording saved: ").Append(wav).Append(" | ").Append(Synth.Stats()).Append('\n');
        Run(sb, "Synth late 0 / errors 0 across the tempo and transpose changes", () => Synth.LateEvents - lateBefore == 0 && Synth.Errors - errBefore == 0 ? null : "late +" + (Synth.LateEvents - lateBefore) + " errors +" + (Synth.Errors - errBefore));
        GlobalClock.Stop();
        SongState.Apply(snap);                        // the pad's step / gate back as they were (no History entry)
        sb.Append("INFO restored: bpm ").Append(GlobalClock.BPM).Append(", pad step ").Append(pad != null ? pad.step.ToString() : "n/a").Append('\n');
        TempoReport = sb.ToString();
        TempoDone = true;
    }

    // ================================================================== capture + audio helpers (Play mode; one execute_code call each)
    /// <summary>Draws the overlay HUD through the main camera (ScreenCapture misses ScreenSpaceOverlay canvases) or restores the overlay.</summary>
    public static string HudOnCamera(bool on)
    {
        var ui = UIManager.I; if (ui == null || ui.Canvas == null) return "no canvas";
        if (on) { ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = Camera.main; ui.Canvas.planeDistance = 1f; }
        else ui.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return "hud on camera " + on;
    }
    static void Shoot(string file)
    {
        Directory.CreateDirectory(CapturePath);
        string p = Path.Combine(CapturePath, file);
        if (File.Exists(p)) File.Delete(p);
        ScreenCapture.CaptureScreenshot(p, 1);
    }
    /// <summary>Captures the Game view next frame.</summary>
    public static string Capture(string file) { SequenceMaster.I.StartCoroutine(CaptureRoutine(file, 0f)); return "queued " + file; }
    /// <summary>Captures after <paramref name="delay"/> seconds.</summary>
    public static string CaptureAfter(string file, float delay) { SequenceMaster.I.StartCoroutine(CaptureRoutine(file, delay)); return "queued " + file + " in " + delay + " s"; }
    static IEnumerator CaptureRoutine(string file, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        yield return null;
        Shoot(file);
        LastCapture = file + " at beat " + GlobalClock.SongBeat.ToString("F2");
    }
    /// <summary>Captures in the middle of the comet's next flight (last beat of an island).</summary>
    public static string CaptureFlight(string file) { LastCapture = ""; SequenceMaster.I.StartCoroutine(FlightRoutine(file)); return "waiting for a flight"; }
    static IEnumerator FlightRoutine(string file)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 40f && !(Comet.I != null && Comet.I.InFlight && Comet.I.FlightT >= 0.45f && Comet.I.FlightT <= 0.8f)) yield return null;
        Shoot(file);
        LastCapture = file + (Comet.I != null ? " flightT " + Comet.I.FlightT.ToString("F2") + " from " + Comet.I.FromIsland + " to " + Comet.I.ToIsland : " (no comet)") + " beat " + GlobalClock.SongBeat.ToString("F2");
    }
    /// <summary>Captures ~0.12 s after the comet's next arrival (sky beam, ripple, starburst, hub vertex).</summary>
    public static string CaptureDownbeat(string file) { LastCapture = ""; SequenceMaster.I.StartCoroutine(DownbeatRoutine(file)); return "waiting for an arrival"; }
    static IEnumerator DownbeatRoutine(string file)
    {
        var comet = Comet.Ensure();
        bool arrived = false; int at = -1;
        Action<int> h = i => { arrived = true; at = i; };
        comet.OnArrive += h;
        float t0 = Time.realtimeSinceStartup;
        while (!arrived && Time.realtimeSinceStartup - t0 < 40f) yield return null;
        comet.OnArrive -= h;
        yield return new WaitForSecondsRealtime(0.12f);
        Shoot(file);
        LastCapture = file + " after arrival at island " + at + " beat " + GlobalClock.SongBeat.ToString("F2") + " pillars " + Fx.I.ActivePillars + " bursts " + Fx.I.ActiveBursts + " ripples " + Fx.I.ActiveRipples;
    }

    /// <summary>Stops, seeks 0, records <paramref name="seconds"/> and plays (the recording ends by itself; SaveRec writes it).</summary>
    public static string PlayAndRecord(float seconds)
    {
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        GlobalClock.Seek(0);
        Synth.StartRecording(seconds);
        GlobalClock.Play();
        return "recording " + seconds + " s from beat 0 | " + Synth.Stats();
    }
    public static string SaveRec(string file)
    {
        if (!Synth.RecordingDone) return "recording not done yet";
        string p = Path.Combine(CapturePath, file);
        bool ok = Synth.SaveRecording(p);
        return (ok ? "saved " : "FAILED ") + p + " | " + Synth.Stats();
    }
    /// <summary>A fresh offline song (MusicTheory.RandomSong(seed) + StartNewSong: arc layout, demo Moon + groove, Keys stamped, Bass Rider; playing on load), recording <paramref name="seconds"/> from its first beat.</summary>
    public static string DemoStart(int seed, float seconds)
    {
        var song = MusicTheory.RandomSong(seed, "integ demo " + seed);
        SongManager.I.StartNewSong(song);
        if (InterfaceController.I != null) InterfaceController.I.Hide();
        var sm = SongManager.I;
        int moonCubes = 0, riders = 0, keys = 0;
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; if (c.IsOnMoon) moonCubes++; if (c.rider) riders++; if (c.instrument == 0) keys++; }
        if (seconds > 0f) Synth.StartRecording(seconds);
        return "demo: " + sm.Islands.Count + " islands, " + sm.Moons.Count + " moons, " + SequenceMaster.Cubes.Count + " cubes (moon " + moonCubes + ", riders " + riders + ", keys " + keys + "), bpm " + GlobalClock.BPM + ", playing " + GlobalClock.IsPlaying + ", route segs " + (sm.Route != null ? sm.Route.SegmentCount : -1) + ", comet " + (Comet.I != null);
    }
    /// <summary>Mutes every role but Drums (or restores) for an onset count.</summary>
    public static string DrumsOnly(bool on)
    {
        for (int i = 0; i < Instruments.Count; i++) if (!Instruments.IsDrums(i)) Instruments.SetMuted(i, on);
        return "drums only " + on;
    }
}
