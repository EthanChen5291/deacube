using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// WP-A verification (SPEC §8.3), callable from execute_code in Play mode: <c>return WpAChecks.RunM1();</c> loads the
/// v1 fixture and checks the necklace, stickers, resolver rules and drum clothes; the recording helpers drive the
/// audio checks across frames (PlayAndRecord / SaveRec / ArmRatchet / Disarm / Status).
/// </summary>
public static class WpAChecks
{
    delegate string Check();

    static void Run(StringBuilder sb, string name, Check c)
    {
        try { string d = c(); sb.Append(d == null ? "PASS " : "FAIL ").Append(name).Append(": ").Append(d ?? "ok").Append('\n'); }
        catch (Exception e) { sb.Append("FAIL ").Append(name).Append(": ").Append(e.GetType().Name).Append(' ').Append(e.Message).Append('\n'); }
    }

    static string Ints(IList<int> l) { var s = new StringBuilder(); for (int i = 0; i < l.Count; i++) { if (i > 0) s.Append(','); s.Append(l[i]); } return s.ToString(); }
    static string Set(VoiceRules.NoteEvent[] ev, int n) { var l = new List<int>(); for (int i = 0; i < n; i++) l.Add(ev[i].midi); l.Sort(); return Ints(l); }
    static string Sorted(params int[] v) { var l = new List<int>(v); l.Sort(); return Ints(l); }

    /// <summary>HitSteps16 of the 13 fixture cubes on the pre-change build (captured before WP-A M1 from the running M0 build).</summary>
    static readonly string[] Baseline =
    {
        "0,4,8,12", "0,2,4,6,8,10,12,14", "0,4,8,12", "0,4,8,12", "0,4,8,12", "0,4,8,12", "0,4,8,12", "0,4,8,12",
        "0,4,8,12", "0,4,8,12", "0,4,8,12", "0,4,8,12", "0,4,8,12",
    };

    public static string LoadFixture()
    {
        var st = SongState.FromJson(File.ReadAllText(V2Checks.FixturePath));
        SongState.Apply(st);
        return "cubes=" + SequenceMaster.Cubes.Count + " islands=" + SongManager.I.Islands.Count;
    }

    static AudioCube Make(int instrument, int measure, int[] xs, int[] zs, int step, int gate = 1, int mode = 0)
    {
        var s = new CubeState { instrument = instrument, measure = measure, xs = xs, zs = zs, rests = new bool[xs.Length], step = step, gate = gate, mode = mode, volume = 1f };
        var c = PathManager.I.RestoreCube(s);
        SequenceMaster.RecalculateTimeline();
        return c;
    }

    static void Kill(AudioCube c)
    {
        if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
        SequenceMaster.RecalculateTimeline();
    }

    static int Resolve(AudioCube c, TileInteraction tile, AudioCube.Hit hit, double on, double stepSec, double end, double next, VoiceRules.NoteEvent[] ev, int stack = 0)
        => VoiceRules.Resolve(c, c.windows[0], tile, hit, stack, 0, on, stepSec, end, next, ev);

    static bool AllKit(KeyBlock kb, bool want) { foreach (var t in kb.tiles) if (t.KitLook != want) return false; return true; }

    public static string RunM1()
    {
        var sb = new StringBuilder();
        var sm = SongManager.I;
        if (sm == null) return "FAIL: needs Play mode\n";
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        LoadFixture();
        double now = GlobalClock.DspNow, bps = GlobalClock.BeatsPerSecond;
        var ev = new VoiceRules.NoteEvent[32];

        Run(sb, "M1.1 fixture HitSteps16 identical to the pre-change build (13 cubes, hits -1 => h(k) = k)", () =>
        {
            if (SequenceMaster.Cubes.Count != 13) return "cubes " + SequenceMaster.Cubes.Count;
            for (int i = 0; i < 13; i++)
            {
                var c = SequenceMaster.Cubes[i];
                string got = Ints(c.HitSteps16(0, 0));
                if (got != Baseline[i]) return "cube " + i + " got " + got + " want " + Baseline[i];
                for (int k = 0; k < 16; k++) { var h = c.Decide(0, k, 0); if (!h.hit || !h.fires || h.node != c.NodeForStep(k)) return "cube " + i + " step " + k + " node " + h.node + " want " + c.NodeForStep(k); }
            }
            return null;
        });

        AudioCube t = null;
        Run(sb, "M1.1 hit-advance: SetHits(3) on an Eighth 6-tile cube -> {0,6,12}; Decide(0,3).node == 1; Decide(0,1) waits; rot/reverse/phase/once/mask", () =>
        {
            t = Make(0, 2, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 0, 0, 0, 0, 0 }, (int)StepLen.Eighth);
            if (t == null) return "no cube";
            if (t.StepsPerBar != 8 || t.windows.Count != 1) return "StepsPerBar " + t.StepsPerBar + " windows " + t.windows.Count;
            t.SetHits(3);
            string s = Ints(t.HitSteps16(0, 0));
            if (s != "0,6,12") return "HitSteps16 " + s;
            if (t.Decide(0, 3, 0).node != 1) return "step 3 node " + t.Decide(0, 3, 0).node;
            var w = t.Decide(0, 1, 0);
            if (w.fires || w.hit || w.node != 0) return "step 1: fires " + w.fires + " hit " + w.hit + " node " + w.node;
            if (t.Decide(0, 0, 0).node != 0 || !t.Decide(0, 0, 0).fires) return "step 0";
            if (t.Decide(0, 6, 0).node != 2 || t.Decide(0, 5, 0).node != 1 || t.Decide(0, 5, 0).hit) return "steps 5/6";
            if (t.Decide(0, 8, 0).node != 3) return "the hit index continues across bars: " + t.Decide(0, 8, 0).node;
            if (t.NextPatternHitStep(0, 4f) != 3 || t.NextPatternHitStep(3, 4f) != 6 || t.NextPatternHitStep(6, 4f) != -1) return "NextPatternHitStep";
            t.SetRot(1);
            s = Ints(t.HitSteps16(0, 0));
            if (s != "2,8,14") return "rot 1: " + s;
            t.SetRot(0);
            t.SetReverse(true);
            if (t.Decide(0, 0, 0).node != 5 || t.Decide(0, 3, 0).node != 4 || t.HomeNode != 5) return "reverse";
            t.SetReverse(false);
            t.SetPhase(1);
            if (t.Decide(0, 0, 0).node != 1 || t.Decide(0, 1, 0).node != 1) return "phase";
            t.SetPhase(0);
            t.SetMode(PathMode.Once);
            if (t.Decide(0, 6, 0).node != 2) return "once";
            t.SetMode(PathMode.Loop);
            t.SetHits(-2); t.SetMask(0x81);
            s = Ints(t.HitSteps16(0, 0));
            if (s != "0,14") return "mask: " + s;
            t.SetHits(-1);
            if (Ints(t.HitSteps16(0, 0)) != "0,2,4,6,8,10,12,14") return "hits -1 identity";
            return null;
        });

        Run(sb, "M1.2 stickers in Decide: ghost/accent/ratchet/tie/lift/rest/coin (deterministic per pass)", () =>
        {
            if (t == null) return "no cube";
            t.SetMod(0, 2); if (t.Decide(0, 0, 0).weight != 0 || !t.Decide(0, 0, 0).fires) return "ghost";
            t.SetMod(0, 3); if (t.Decide(0, 0, 0).weight != 2) return "accent";
            t.SetMod(0, 4); if (t.Decide(0, 0, 0).ratchet != 2) return "ratchet2";
            t.SetMod(0, 5); if (t.Decide(0, 0, 0).ratchet != 3) return "ratchet3";
            t.SetMod(0, 7); if (!t.Decide(0, 0, 0).tie) return "tie";
            t.SetMod(0, 8); if (!t.Decide(0, 0, 0).lift) return "lift";
            t.SetMod(0, 1);
            var r = t.Decide(0, 0, 0);
            if (r.fires || !r.hit || r.mod != 1 || !t.rests[0] || t.mods[0] != 1) return "rest";
            if (Ints(t.HitSteps16(0, 0)) != "2,4,6,8,10,14") return "rest skips node 0: " + Ints(t.HitSteps16(0, 0));
            t.SetMod(0, 6);
            int fired = 0, slipped = 0;
            for (int pass = 0; pass < 32; pass++)
            {
                bool want = (Rhythm.Hash(pass, 0, t.seed) & 1u) == 0u;
                var a = t.Decide(0, 0, pass); var b = t.Decide(0, 0, pass);
                if (a.fires != want || a.slip != !want || b.fires != a.fires || !a.hit) return "coin pass " + pass;
                if (a.fires) fired++; else slipped++;
            }
            if (fired == 0 || slipped == 0) return "coin never flips";
            t.SetMod(0, 0);
            t.ToggleRest(1); if (t.mods[1] != 1 || !t.rests[1]) return "ToggleRest on";
            t.ToggleRest(1); if (t.mods[1] != 0 || t.rests[1]) return "ToggleRest off";
            var st = t.ToState(); if (st.mods.Length != 6 || st.rests.Length != 6) return "ToState";
            return null;
        });

        Run(sb, "M1.3 bass anchor: 7th tile on the downbeat -> chord root in register; beat 1 as drawn; min gate 120 ms; root tile keeps its pitch class", () =>
        {
            var b = Make(4, 0, new[] { 0 }, new[] { 3 }, (int)StepLen.Quarter);
            try
            {
                var tile = b.nodes[0];
                if (tile.role != ChordRole.Seventh) return "tile role " + tile.role;
                var hit = b.Decide(0, 0, 0);
                double on = now + 0.1;
                int n = Resolve(b, tile, hit, on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                int root = SynthBank.ClampToRegister(4, b.Island.chordRootMIDI + SongManager.Transpose);
                if (n != 1 || ev[0].midi != root || ev[0].midi < 28 || ev[0].midi > 55) return "downbeat: n " + n + " midi " + ev[0].midi + " want " + root;
                n = Resolve(b, tile, hit, on, 0.5, on + 3.0 / bps, double.PositiveInfinity, ev);
                if (n != 1 || ((ev[0].midi - tile.midi - SongManager.Transpose) % 12 + 12) % 12 != 0) return "beat 1 pitch class: " + ev[0].midi + " tile " + tile.midi;
                if (ev[0].offDsp - ev[0].onDsp < 0.12 - 1e-9) return "bass min gate " + (ev[0].offDsp - ev[0].onDsp);
                b.gate = Gate.Short;
                Resolve(b, tile, hit, on, 0.5, on + 3.0 / bps, double.PositiveInfinity, ev);
                if (ev[0].offDsp - ev[0].onDsp < 0.12 - 1e-9) return "bass Short min 120 ms: " + (ev[0].offDsp - ev[0].onDsp);
            }
            finally { Kill(b); }
            var rt = Make(4, 2, new[] { 0 }, new[] { 0 }, (int)StepLen.Quarter);   // (0,0) of island 2 is the root (the test cube t rests there too; stack = 0 is passed explicitly)
            try
            {
                var tile = rt.nodes[0];
                if (tile.role != ChordRole.Root) return "root tile role " + tile.role;
                double on = now + 0.1;
                int n = Resolve(rt, tile, rt.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                if (n != 1 || ((ev[0].midi - rt.Island.chordRootMIDI) % 12 + 12) % 12 != 0) return "root tile: " + ev[0].midi;
            }
            finally { Kill(rt); }
            return null;
        });

        Run(sb, "M1.3 pad costume: the two chord tones below (wrapping an octave down at the bottom row), same on/off, quieter, in register", () =>
        {
            var p = Make(2, 3, new[] { 0 }, new[] { 2 }, (int)StepLen.Quarter);
            try
            {
                double on = now + 0.1;
                var kb = p.Island; var tile = p.nodes[0];
                int n = Resolve(p, tile, p.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                int main = VoiceRules.Fold(2, tile.midi + SongManager.Transpose);
                int b1 = VoiceRules.Fold(2, kb.GetTile(0, 1).midi + SongManager.Transpose), b0 = VoiceRules.Fold(2, kb.GetTile(0, 0).midi + SongManager.Transpose);
                if (n != 3 || Set(ev, n) != Sorted(main, b1, b0)) return "row 2: n " + n + " " + Set(ev, n) + " want " + Sorted(main, b1, b0);
                for (int i = 0; i < n; i++) if (ev[i].midi < 48 || ev[i].midi > 79 || Math.Abs(ev[i].offDsp - ev[0].offDsp) > 1e-9 || Math.Abs(ev[i].onDsp - ev[0].onDsp) > 1e-9) return "tone " + i + " midi " + ev[i].midi;
                if (ev[1].vel > ev[0].vel || ev[2].vel > ev[0].vel) return "costume louder than the hit";
            }
            finally { Kill(p); }
            var q = Make(2, 3, new[] { 0 }, new[] { 0 }, (int)StepLen.Quarter);
            try
            {
                double on = now + 0.1;
                var kb = q.Island; var tile = q.nodes[0];
                int n = Resolve(q, tile, q.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                int main = VoiceRules.Fold(2, tile.midi + SongManager.Transpose);
                int w3 = VoiceRules.Fold(2, kb.GetTile(0, 3).midi - 12 + SongManager.Transpose), w2 = VoiceRules.Fold(2, kb.GetTile(0, 2).midi - 12 + SongManager.Transpose);
                if (n != 3 || Set(ev, n) != Sorted(main, w3, w2)) return "row 0 wrap: n " + n + " " + Set(ev, n) + " want " + Sorted(main, w3, w2);
            }
            finally { Kill(q); }
            return null;
        });

        Run(sb, "M1.3 stack: the top cube of the three on tile (4,0) of island 0 plays +12 and louder", () =>
        {
            var kb = sm.Islands[0]; var tile = kb.GetTile(4, 0);
            var on = new List<AudioCube>();
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.nodes.Count > 0 && c.nodes[0] == tile && SequenceMaster.IndexOn(tile, c) >= 0) on.Add(c);
            if (on.Count < 2) return "stack of " + on.Count;
            AudioCube top = null, bottom = null;
            foreach (var c in on) { int idx = SequenceMaster.IndexOn(tile, c); if (idx == 0) bottom = c; if (idx == on.Count - 1) top = c; }
            if (top == null || bottom == null || top == bottom) return "top/bottom";
            double on0 = now + 0.1;
            float vTop = top.volume, vBot = bottom.volume;
            top.volume = 0.3f; bottom.volume = 0.3f;                       // below the 127 ceiling so the +40 % of a 2-stack is visible
            int nt, nb, mt, vt, mb, vb;
            try
            {
                nt = Resolve(top, tile, top.Decide(0, 0, 0), on0, 0.5, on0 + 4.0 / bps, double.PositiveInfinity, ev, SequenceMaster.CountOn(tile, top)); mt = ev[0].midi; vt = ev[0].vel;
                nb = Resolve(bottom, tile, bottom.Decide(0, 0, 0), on0, 0.5, on0 + 4.0 / bps, double.PositiveInfinity, ev, SequenceMaster.CountOn(tile, bottom)); mb = ev[0].midi; vb = ev[0].vel;
            }
            finally { top.volume = vTop; bottom.volume = vBot; }
            if (nt != 1 || nb != 1) return "events " + nt + "/" + nb;
            if (mt != mb + 12) return "top " + mt + " bottom " + mb;
            if (vt <= vb + 6) return "top vel " + vt + " bottom " + vb;
            return null;
        });

        Run(sb, "M1.3 energy: 0 silences the pads; velocity follows the island's energy", () =>
        {
            var kb = sm.Islands[3]; int e0 = kb.energy;
            AudioCube pad = null; foreach (var c in SequenceMaster.Cubes) if (c != null && c.instrument == 2 && c.Island == kb) { pad = c; break; }
            if (pad == null) return "no pad on island 3";
            try
            {
                double on = now + 0.1;
                kb.energy = 2; int n2 = Resolve(pad, pad.nodes[0], pad.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                kb.energy = 0; int n0 = Resolve(pad, pad.nodes[0], pad.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                if (n2 < 1 || n0 != 0) return "pad events at energy 2: " + n2 + ", at 0: " + n0;
                var keys = SequenceMaster.Cubes[0]; var k0 = keys.Island; int ek = k0.energy;
                try
                {
                    k0.energy = 3; Resolve(keys, keys.nodes[0], keys.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev); int v3 = ev[0].vel;
                    k0.energy = 0; int nk = Resolve(keys, keys.nodes[0], keys.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev); int v0 = ev[0].vel;
                    if (nk != 1 || v0 >= v3) return "keys energy 0 vel " + v0 + " energy 3 vel " + v3;
                }
                finally { k0.energy = ek; }
            }
            finally { kb.energy = e0; }
            return null;
        });

        Run(sb, "M1.2/3 ratchet3: three sub-hits at step/3 with velocities 1 / 0.82 / 0.68, each off before the next", () =>
        {
            t.SetMod(0, 5);
            double on = now + 0.1, stepSec = 0.5 / bps;
            int n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, on + 4.0 / bps, on + stepSec, ev);
            t.SetMod(0, 0);
            if (n != 3) return "n " + n;
            for (int j = 1; j < 3; j++) if (Math.Abs((ev[j].onDsp - ev[0].onDsp) - j * stepSec / 3) > 1e-6) return "spacing " + j + ": " + (ev[j].onDsp - ev[0].onDsp);
            if (ev[1].vel != Mathf.Clamp(Mathf.RoundToInt(ev[0].vel * 0.82f), 1, 127) || ev[2].vel != Mathf.Clamp(Mathf.RoundToInt(ev[0].vel * 0.68f), 1, 127)) return "vels " + ev[0].vel + "/" + ev[1].vel + "/" + ev[2].vel;
            if (ev[0].offDsp > ev[1].onDsp || ev[1].offDsp > ev[2].onDsp) return "sub-hits overlap";
            return null;
        });

        Run(sb, "M1.2/3 tie: off at the next hit - 10 ms; without a next hit at the window end - 5 ms", () =>
        {
            t.SetMod(0, 7);
            double on = now + 0.1, stepSec = 0.5 / bps, end = on + 4.0 / bps;
            int n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, end, on + 1.0, ev);
            if (n != 1 || Math.Abs(ev[0].offDsp - (on + 1.0 - 0.01)) > 1e-6) return "tie to the next hit: " + (ev[0].offDsp - on);
            n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, end, double.PositiveInfinity, ev);
            t.SetMod(0, 0);
            if (n != 1 || Math.Abs(ev[0].offDsp - (end - 0.005)) > 1e-6) return "tie to the window end: " + (end - ev[0].offDsp);
            return null;
        });

        Run(sb, "M1.2/3 lift: +12 before the register fold (pitch class kept)", () =>
        {
            t.SetMod(0, 8);
            double on = now + 0.1;
            int n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, 0.25, on + 4.0 / bps, double.PositiveInfinity, ev);
            t.SetMod(0, 0);
            int want = VoiceRules.Fold(0, t.nodes[0].midi + SongManager.Transpose + 12);
            if (n != 1 || ev[0].midi != want) return "midi " + ev[0].midi + " want " + want;
            return null;
        });

        Run(sb, "M1.3 echo: repeats at +step at -35 % each, dropped past the window end; shimmer climbs one shadow row per repeat", () =>
        {
            t.SetEcho(2);
            double on = now + 0.1, stepSec = 0.5 / bps, end = on + 4.0 / bps;
            int n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, end, on + stepSec, ev);
            if (n != 3) return "n " + n;
            for (int i = 1; i < 3; i++) if (Math.Abs((ev[i].onDsp - ev[0].onDsp) - i * stepSec) > 1e-6 || ev[i].vel >= ev[i - 1].vel || ev[i].midi != ev[0].midi) return "repeat " + i + " on " + (ev[i].onDsp - ev[0].onDsp) + " vel " + ev[i].vel;
            n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, on + 1.5 * stepSec, on + stepSec, ev);
            if (n != 2) return "the second repeat past the window end should drop: n " + n;
            t.SetShimmer(true);
            n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, end, on + stepSec, ev);
            t.SetShimmer(false); t.SetEcho(0);
            var kb = t.Island;
            int up1 = VoiceRules.Fold(0, kb.GetTile(0, 1).midi + SongManager.Transpose), up2 = VoiceRules.Fold(0, kb.GetTile(0, 2).midi + SongManager.Transpose);
            if (n != 3 || ev[1].midi != up1 || ev[2].midi != up2) return "shimmer: " + ev[0].midi + "," + ev[1].midi + "," + ev[2].midi + " want " + up1 + "," + up2;
            return null;
        });

        Run(sb, "M1.3 shadow follow: nearest rows above/below at >= 3 semitones, quieter; no follower ever sits on a 2nd", () =>
        {
            t.SetFollow(3);
            double on = now + 0.1, stepSec = 0.5 / bps;
            int n = Resolve(t, t.nodes[0], t.Decide(0, 0, 0), on, stepSec, on + 4.0 / bps, on + stepSec, ev);
            t.SetFollow(0);
            int o; var up = VoiceRules.ShadowTile(t.nodes[0], true, out o); int upM = VoiceRules.Fold(0, up.midi + o + SongManager.Transpose);
            int o2; var dn = VoiceRules.ShadowTile(t.nodes[0], false, out o2); int dnM = VoiceRules.Fold(0, dn.midi + o2 + SongManager.Transpose);
            int main = VoiceRules.Fold(0, t.nodes[0].midi + SongManager.Transpose);
            if (n != 3 || Set(ev, n) != Sorted(main, upM, dnM)) return "n " + n + " " + Set(ev, n) + " want " + Sorted(main, upM, dnM);
            for (int i = 1; i < n; i++) if (ev[i].vel > ev[0].vel) return "follower louder";
            foreach (var isl in sm.Islands) foreach (var tile in isl.tiles)
            {
                int oo; var a = VoiceRules.ShadowTile(tile, true, out oo); if (a != null && Mathf.Abs(a.midi + oo - tile.midi) < 3) return "2nd above on " + tile.name;
                var b = VoiceRules.ShadowTile(tile, false, out oo); if (b != null && Mathf.Abs(b.midi + oo - tile.midi) < 3) return "2nd below on " + tile.name;
                if (a == null || b == null) return "no shadow for " + tile.name;
            }
            return null;
        });

        Run(sb, "M1.3 invariant: every event off <= windowEnd - 5 ms (sustaining: - one 16th), in register, low-end law, vel 1..127, never early (cubes x gates x stickers x follow x echo)", () =>
        {
            int checkedEvents = 0;
            double one16 = 0.25 / bps;
            var cubes = new List<AudioCube>(SequenceMaster.Cubes);
            foreach (var c in cubes)
            {
                if (c == null || c.nodes.Count == 0) continue;
                Gate g0 = c.gate; int f0 = c.follow, e0 = c.echo; int m0 = c.ModOf(0);
                try
                {
                    for (int g = 0; g < 3; g++) for (int mod = 0; mod < 9; mod++) for (int f = 0; f < 4; f += 3) for (int e = 0; e < 3; e++)
                    {
                        c.gate = (Gate)g; c.follow = f; c.echo = e; c.mods[0] = mod;
                        for (int pass = 0; pass < 2; pass++)
                        {
                            var hit = c.Decide(0, 0, pass);
                            if (!hit.fires) continue;
                            double stepSec = c.StepBeats / bps;
                            for (int wl = 1; wl <= 3; wl++)
                            {
                                double on = now + 0.05, end = on + wl * stepSec * 0.9 + 0.01, next = on + stepSec;
                                int n = VoiceRules.Resolve(c, c.windows[0], c.nodes[0], hit, 0, pass, on, stepSec, end, next, ev);
                                int slot = Instruments.SlotOf(c.instrument, c.voice);   // v6 (A): the cube's voice
                                var def = SynthBank.Def(slot);
                                for (int i = 0; i < n; i++)
                                {
                                    var x = ev[i]; checkedEvents++;
                                    double cap = end - 0.005;
                                    if (VoiceRules.IsSustaining(slot) && x.onDsp + 0.03 <= end - one16 + 1e-9) cap = end - one16;
                                    if (x.offDsp > cap + 1e-9) return c.id + " g" + g + " mod" + mod + " f" + f + " e" + e + " wl" + wl + " ev" + i + " off past the cap by " + (x.offDsp - cap);
                                    if (x.offDsp <= x.onDsp) return "zero-length note";
                                    if (x.onDsp < on - 1e-9) return "early note";
                                    if (x.vel < 1 || x.vel > 127) return "vel " + x.vel;
                                    if (!def.drums && (x.midi < Mathf.Min(def.lowMidi, def.highMidi) || x.midi > Mathf.Max(def.lowMidi, def.highMidi))) return "out of register " + x.midi + " slot " + slot;
                                    if (!def.drums && !VoiceRules.IsBass(slot) && x.midi < 48) return "low-end law " + x.midi;   // v6: by group
                                }
                            }
                        }
                    }
                }
                finally { c.gate = g0; c.follow = f0; c.echo = e0; c.mods[0] = m0; c.rests[0] = m0 == 1; }
            }
            return checkedEvents > 100 ? null : "only " + checkedEvents + " events checked";
        });

        Run(sb, "M1.5 drums on a chord island: row -> piece, kick fixed 100 (+10 accent, no humanise/jitter), one-shot off = on + 0.1, drums never ride", () =>
        {
            var d = Make(9, 2, new[] { 2, 0 }, new[] { 1, 0 }, (int)StepLen.Quarter);
            try
            {
                if (!d.IsDrums || d.rider) return "flags";
                double on = now + 0.1;
                int n = Resolve(d, d.nodes[0], d.Decide(0, 0, 0), on, 0.5, on + 4.0 / bps, double.PositiveInfinity, ev);
                if (n != 1 || ev[0].midi != 38 || ev[0].slot != SynthBank.DrumSlot) return "snare: n " + n + " midi " + ev[0].midi;
                if (Math.Abs((ev[0].offDsp - ev[0].onDsp) - 0.1) > 1e-6) return "one-shot gate " + (ev[0].offDsp - ev[0].onDsp);
                int wantVel = SynthBank.Velocity(SynthBank.DrumSlot, (0.55f + 0.09f * 2f) * Rhythm.EnergyScale(d.Island.energy), 0.30f);
                if (Math.Abs(ev[0].vel - wantVel) > 6) return "snare vel " + ev[0].vel + " want ~" + wantVel;
                var h1 = d.Decide(0, 1, 0);
                n = Resolve(d, d.nodes[1], h1, on, 0.5, on + 3.0 / bps, double.PositiveInfinity, ev);
                if (n != 1 || ev[0].midi != 36 || ev[0].vel != 100 || ev[0].onDsp != on) return "kick: midi " + ev[0].midi + " vel " + ev[0].vel + " jitter " + (ev[0].onDsp - on);
                d.SetMod(1, 3);
                Resolve(d, d.nodes[1], d.Decide(0, 1, 0), on, 0.5, on + 3.0 / bps, double.PositiveInfinity, ev);
                if (ev[0].vel != 110) return "accented kick " + ev[0].vel;
                d.SetRider(true); if (d.rider) return "drums can't ride";
                var pe = VoiceRules.PreviewEvent(SynthBank.DrumSlot, d.nodes[0], 0);
                if (pe.midi != 38) return "preview piece " + pe.midi;
            }
            finally { Kill(d); }
            return null;
        });

        Run(sb, "M2.9 fill: last bar -> drum cubes' last two hits ratchet x2, pitched cubes echo >= 1; off again when fill clears", () =>
        {
            var kb = sm.Islands[2]; bool f0 = kb.fill;
            var d = Make(9, 2, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 1, 1 }, (int)StepLen.Quarter);
            try
            {
                kb.fill = true;
                if (d.Decide(0, 2, 0).ratchet != 2 || d.Decide(0, 3, 0).ratchet != 2 || d.Decide(0, 0, 0).ratchet != 1 || d.Decide(0, 1, 0).ratchet != 1)
                    return "drum ratchets " + d.Decide(0, 0, 0).ratchet + d.Decide(0, 1, 0).ratchet + d.Decide(0, 2, 0).ratchet + d.Decide(0, 3, 0).ratchet;
                if (t.Decide(0, 0, 0).echo != 1) return "pitched echo " + t.Decide(0, 0, 0).echo;
                kb.fill = false;
                if (d.Decide(0, 3, 0).ratchet != 1 || t.Decide(0, 0, 0).echo != 0) return "fill off";
            }
            finally { kb.fill = f0; Kill(d); }
            return null;
        });

        Run(sb, "M1.5 kit look: selecting a drum cube dresses its island; arming Drums dresses every island; both revert; glyphs by row", () =>
        {
            int inst = PathManager.I.selectedInstrument;
            PathManager.I.SelectInstrument(0);
            var d = Make(9, 2, new[] { 3, 4 }, new[] { 2, 2 }, (int)StepLen.Quarter);
            try
            {
                var kb2 = sm.Islands[2]; var kb0 = sm.Islands[0];
                PathManager.I.Select(d, true);
                if (!AllKit(kb2, true)) return "island 2 not dressed on select";
                if (!AllKit(kb0, false)) return "island 0 dressed";
                PathManager.I.Deselect();
                if (!AllKit(kb2, false)) return "island 2 still dressed after deselect";
                PathManager.I.SelectInstrument(9);
                if (!AllKit(kb0, true) || !AllKit(kb2, true)) return "arming Drums";
                PathManager.I.SelectInstrument(0);
                if (!AllKit(kb0, false)) return "disarm";
                // v5 (R): the kit glyphs are drum pictograms now (kick drum, snare, closed / open hi-hat, clap) instead of dot / square / sparkle / ring / diamond
                if (TileInteraction.KitGlyph(0, 4) != KitGlyphs.Kick || TileInteraction.KitGlyph(1, 4) != KitGlyphs.Snare || TileInteraction.KitGlyph(2, 4) != KitGlyphs.HatClosed
                    || TileInteraction.KitGlyph(3, 4) != KitGlyphs.HatOpen || TileInteraction.KitGlyph(4, 5) != KitGlyphs.Clap) return "glyphs";
            }
            finally { Kill(d); PathManager.I.SelectInstrument(inst); CubeInspector.CloseImmediate(); FocusLoop.Dismiss(); GlobalClock.Stop(); }   // v4: the selection's inspector engaged the focus loop (it plays): stopped again
            return null;
        });

        // v7 (SPEC v7 §21 — "just keep cubes on each grid"; A updated this assertion): v4's Rider window mapped the path's grid offsets onto every
        // other island (rows clamped); §21 retired riders' flights — the flag stays in the file, ignored: every window of a rider is on its own grid
        // and plays its home tiles
        Run(sb, "M1.4 octave floats the rest position +-0.35; twin state; §21: a rider plays only on its own grid (every window on its home island, TileAt = its home tiles)", () =>
        {
            float y0 = t.RestPositionNow.y;
            t.SetOctave(1); float y1 = t.RestPositionNow.y; t.SetOctave(-1); float ym = t.RestPositionNow.y; t.SetOctave(0);
            if (Math.Abs(y1 - y0 - 0.35f) > 1e-4f || Math.Abs(y0 - ym - 0.35f) > 1e-4f) return "octave heights " + y0 + " " + y1 + " " + ym;
            t.rider = true; SequenceMaster.RecalculateTimeline();
            try
            {
                var tw = t.TwinState(0); if (tw.twinOf != t.id || tw.id != 0 || tw.step != (int)StepLen.Quarter || tw.octave != 1) return "twin";
                if (t.windows.Count == 0) return "no windows";
                for (int w = 0; w < t.windows.Count; w++)
                {
                    if (t.windows[w].island != t.Island) return "§21: rider window " + w + " on '" + (t.windows[w].island != null ? t.windows[w].island.assignedChord : "null") + "', not its own grid";
                    for (int n = 0; n < t.nodes.Count; n++) if (t.TileAt(w, n) != t.nodes[n]) return "§21: TileAt(" + w + "," + n + ") is not its home tile";
                }
            }
            finally { t.rider = false; SequenceMaster.RecalculateTimeline(); }
            if (t.TileAt(0, 2) != t.nodes[2]) return "TileAt home";
            return null;
        });

        Run(sb, "M1.6 Instruments.Audition (stopped) for every role; PreviewAllowed when stopped", () =>
        {
            for (int i = 0; i < Instruments.Count; i++) Instruments.Audition(i);
            if (!VoiceRules.PreviewAllowed(sm.Islands[1].GetTile(0, 0), 0)) return "preview allowed when stopped";
            return null;
        });

        Kill(t);
        sb.Append("cubes after cleanup: ").Append(SequenceMaster.Cubes.Count).Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ play-mode helpers (one execute_code call each)
    public static string PlayAndRecord(float seconds)
    {
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        GlobalClock.Seek(0);
        Synth.StartRecording(seconds);
        GlobalClock.Play();
        return "recording " + seconds + " s from beat 0 | " + Synth.Stats();
    }

    public static string SaveRec(string path)
    {
        bool ok = Synth.SaveRecording(path);
        string stats = Synth.Stats();
        int loop = GlobalClock.LoopIndex; float beat = GlobalClock.SongBeat;
        GlobalClock.Stop();
        return (ok ? "saved " : "FAILED ") + path + " | " + stats + " | loopIndex=" + loop + " beat=" + beat.ToString("F2");
    }

    /// <summary>Mutes every cube except <paramref name="keep"/> and puts a ratchet3 sticker on its node 0.</summary>
    public static string ArmRatchet(int keep)
    {
        for (int i = 0; i < SequenceMaster.Cubes.Count; i++) { var c = SequenceMaster.Cubes[i]; if (c == null) continue; c.SetMuted(i != keep); }
        var k = SequenceMaster.Cubes[keep];
        k.SetMod(0, 5);
        return "cube " + keep + " inst " + k.instrument + " nodes " + k.nodes.Count + " step " + k.step + " window " + k.windows[0].start + "+" + k.windows[0].length + " hits16 " + Ints(k.HitSteps16(0, 0));
    }

    public static string Disarm()
    {
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; c.SetMuted(false); for (int i = 0; i < c.nodes.Count; i++) if (c.ModOf(i) != 0) c.SetMod(i, 0); }
        return "ok";
    }

    public static string Status()
    {
        return "playing=" + GlobalClock.IsPlaying + " beat=" + GlobalClock.SongBeat.ToString("F2") + " loop=" + GlobalClock.LoopIndex
            + " stutterActive=" + Performance.StutterActive + " held=" + Performance.StutterHeld + " pending=" + Performance.PendingCount
            + " captured=" + Performance.StutterCapturedCount + " bpm=" + GlobalClock.BPM + " logged=" + Performance.LoggedCount
            + " spot=" + (Performance.SpotlightCube != null) + " | " + Synth.Stats();
    }

    // ------------------------------------------------------------------ M2: windows / Riders / Moons / performance
    static double playT0;
    static int moonIdx = -1;

    // ---- v7 (A): main-thread stalls. Another package's file import or an MCP call can freeze the editor's main thread longer than the cubes' 0.22 s
    // lookahead (eight engineers edit during the v7 build's Play sessions); the steps due meanwhile are never scheduled — skipped, not sent late — so a
    // timed count over such a freeze comes out SHORT by design (the protocol counts a ScreenCapture's stall apart the same way). The armed checks
    // record every frame longer than 0.2 s as a DSP interval; a window that overlaps one and is short (never long) is reported as stalled, not failed;
    // at least one window must be exact.
    static readonly List<double> stallFrom = new List<double>(), stallTo = new List<double>();
    static int stallGen;
    static System.Collections.IEnumerator StallRecorder()
    {
        int gen = ++stallGen;
        while (gen == stallGen)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.2f) { double t = AudioSettings.dspTime; stallFrom.Add(t - dt); stallTo.Add(t); }
        }
    }
    static void ArmStalls() { stallFrom.Clear(); stallTo.Clear(); if (SequenceMaster.I != null) SequenceMaster.I.StartCoroutine(StallRecorder()); }
    static void DisarmStalls() { stallGen++; }
    /// <summary>True when a recorded freeze (+ the 50 ms after it) overlaps the DSP span [from, to).</summary>
    static bool StalledIn(double from, double to)
    {
        for (int i = 0; i < stallFrom.Count; i++) if (stallTo[i] + 0.05 > from && stallFrom[i] < to) return true;
        return false;
    }

    /// <summary>Mutes every cube but one (-1 = none muted).</summary>
    public static string Solo(int keep)
    {
        for (int i = 0; i < SequenceMaster.Cubes.Count; i++) { var c = SequenceMaster.Cubes[i]; if (c == null) continue; c.SetMuted(keep >= 0 && i != keep); }
        return "solo " + keep;
    }

    static List<int> FiringSteps(AudioCube c, int w, int pass)
    {
        var l = new List<int>();
        float len = c.windows[w].length;
        for (int k = 0; k < 4096; k++) { if (c.StepStartLocalBeat(k) >= len - 1e-4f) break; if (c.Decide(w, k, pass).fires) l.Add(k); }
        return l;
    }

    public static string RunM2Static()
    {
        var sb = new StringBuilder();
        var sm = SongManager.I;
        if (sm == null) return "FAIL: needs Play mode\n";
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        LoadFixture();

        // v7 (SPEC v7 §21; A updated this assertion): v4's "one rider window per island in play order, TileAt mapped onto each" is retired — a cube never
        // leaves its grid: a rider's windows are exactly its home windows
        Run(sb, "M2.7 §21: a rider gets only its home windows (none on another island); TileAt = its home tiles; HitSteps16 per window; off again", () =>
        {
            var c = SequenceMaster.Cubes[0];
            int home = c.windows.Count;
            c.rider = true; SequenceMaster.RecalculateTimeline();
            try
            {
                if (c.windows.Count != home) return "§21: rider windows " + c.windows.Count + " (home " + home + ")";
                for (int w = 0; w < c.windows.Count; w++)
                {
                    var win = c.windows[w];
                    if (win.island != c.Island) return "§21: window " + w + " on another island";
                    for (int node = 0; node < c.nodes.Count; node++) if (c.TileAt(w, node) != c.nodes[node]) return "TileAt(" + w + "," + node + ") is not its home tile";
                    if (Ints(c.HitSteps16(w, w)) != "0,4,8,12") return "HitSteps16 window " + w + ": " + Ints(c.HitSteps16(w, w));
                }
            }
            finally { c.rider = false; SequenceMaster.RecalculateTimeline(); }
            if (c.windows.Count != home) return "rider off: windows " + c.windows.Count;
            return null;
        });

        Run(sb, "M2.7 Moon cubes: one window per bar, drums, TileAt home; MoonWeight from the lit island's energy (hush: kick only, snare ghost; 3: off-beat hats ratchet)", () =>
        {
            int m = sm.AddMoon();
            if (m < 0) return "AddMoon " + m;
            try
            {
                sm.RollMoonGroove(m);
                var moon = sm.Moons[m];
                AudioCube kick = null, snare = null, hat = null;
                foreach (var c in SequenceMaster.Cubes) { if (c == null || c.Moon != moon || c.nodes.Count == 0) continue; int row = c.nodes[0].gridZ; if (row == 0) kick = c; else if (row == 1) snare = c; else if (row == 2) hat = c; }
                if (kick == null) return "no kick cube on the Moon";
                int bars = Mathf.RoundToInt(sm.TotalBeats / GlobalClock.BeatsPerBar);
                if (kick.windows.Count != bars || !kick.IsOnMoon || !kick.IsDrums || kick.moon != m) return "kick windows " + kick.windows.Count + " onMoon " + kick.IsOnMoon + " drums " + kick.IsDrums + " moon " + kick.moon;
                if (kick.TileAt(2, 0) != kick.nodes[0]) return "moon TileAt";
                if (FiringSteps(kick, 0, 0).Count == 0) return "kick never fires";
                var kb0 = sm.Islands[0]; int e0 = kb0.energy;
                try
                {
                    kb0.energy = 0;
                    if (FiringSteps(kick, 0, 0).Count == 0) return "hush: kick silent";
                    if (snare != null) foreach (int k in FiringSteps(snare, 0, 0)) if (snare.Decide(0, k, 0).weight != 0) return "hush: snare not ghost";
                    if (hat != null && FiringSteps(hat, 0, 0).Count != 0) return "hush: hats audible";
                    kb0.energy = 3;
                    if (hat != null)
                    {
                        bool any = false;
                        foreach (int k in FiringSteps(hat, 0, 0))
                        {
                            float local = hat.StepStartLocalBeat(k); bool off = Mathf.Abs(local - Mathf.Round(local)) > 1e-3f;
                            if (off && hat.Decide(0, k, 0).ratchet < 2) return "energy 3: off-beat hat without ratchet at step " + k;
                            if (off) any = true;
                        }
                        if (!any) return "energy 3: no off-beat hat";
                    }
                }
                finally { kb0.energy = e0; }
            }
            finally { sm.RemoveMoon(m); }
            if (sm.Moons.Count != 0) return "moon not removed";
            return null;
        });

        return sb.ToString();
    }

    /// <summary>Solo cube 0 as a Rider and start playing from beat 0 (RiderCheck reads the log a few seconds later). v7 §21: the flag is set directly
    /// (the cube card's rider toggle is retired; the flag stays in the file, ignored by the timeline).</summary>
    public static string RiderArm()
    {
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        LoadFixture();
        Solo(0);
        var c = SequenceMaster.Cubes[0];
        c.rider = true; SequenceMaster.RecalculateTimeline();
        ArmStalls();
        GlobalClock.Seek(0); GlobalClock.Play();
        playT0 = GlobalClock.DspTimeOfBeat(0);
        return "rider windows " + c.windows.Count + " t0 " + playT0.ToString("F3");
    }

    /// <summary>Cube 0's note-ons since RiderArm over the first <paramref name="windowsToCheck"/> islands' time: v4 checked one window per island (4 notes,
    /// the first within the 8 ms jitter, that island's tiles); v7 §21 (A updated this check — "just keep cubes on each grid"): the rider plays its
    /// HOME window(s) only — 4 notes each, the first within the 8 ms jitter, its home tiles' pitches (+12 when stacked) — and NO note on the next
    /// islands' time.</summary>
    public static string RiderCheck(int windowsToCheck)
    {
        var c = SequenceMaster.Cubes[0];
        var sm = SongManager.I;
        double bps = GlobalClock.BeatsPerSecond;
        var sb = new StringBuilder(); string fail = null; int n = Performance.LoggedCount;
        double spanEnd = playT0 + (sm != null && windowsToCheck < sm.ColumnCount ? sm.ColumnStart(windowsToCheck) : GlobalClock.TotalBeats) / bps;
        int homeWindows = 0, stalledWindows = 0, exactWindows = 0;
        for (int w = 0; w < c.windows.Count; w++)
        {
            var win = c.windows[w];
            double ws = playT0 + win.start / bps, we = ws + win.length / bps;
            if (ws >= spanEnd - 1e-3) continue;
            if (win.island != c.Island) { fail = "§21: window " + w + " on another island"; continue; }
            homeWindows++;
            int count = 0, wrong = 0; double firstRel = -1;
            for (int i = 0; i < n; i++)
            {
                var e = Performance.Logged(i);
                if (e.slot != 0 || e.owner != c.owner || e.onDsp < ws - 1e-3 || e.onDsp >= we - 1e-3) continue;
                count++;
                if (firstRel < 0) firstRel = e.onDsp - ws;
                bool ok = false;
                for (int node = 0; node < c.nodes.Count; node++) { var t = c.TileAt(w, node); if (t == null) continue; int m0 = VoiceRules.Fold(0, t.midi + SongManager.Transpose); if (e.midi == m0 || e.midi == VoiceRules.Fold(0, t.midi + SongManager.Transpose + 12)) ok = true; }
                if (!ok) wrong++;
            }
            sb.Append("w").Append(w).Append('=').Append(count).Append(" first+").Append(firstRel.ToString("F3")).Append(" wrong").Append(wrong).Append("; ");
            bool bad = count != 4 || wrong != 0 || firstRel < -1e-3 || firstRel > 0.0085;
            if (bad && count < 4 && wrong == 0 && StalledIn(ws, we)) { stalledWindows++; sb.Append("(w").Append(w).Append(" stalled: the main thread froze in it) "); }
            else if (bad) fail = "window " + w;
            else exactWindows++;
        }
        if (homeWindows > 0 && exactWindows == 0 && fail == null) fail = "every window stalled (rerun)";
        // §21: nothing on the other islands' time
        int outside = 0;
        for (int i = 0; i < n; i++)
        {
            var e = Performance.Logged(i);
            if (e.slot != 0 || e.owner != c.owner || e.onDsp < playT0 - 1e-3 || e.onDsp >= spanEnd - 1e-3) continue;
            bool inside = false;
            foreach (var win in c.windows) { double ws = playT0 + win.start / bps, we = ws + win.length / bps; if (e.onDsp >= ws - 1e-3 && e.onDsp < we - 1e-3) { inside = true; break; } }
            if (!inside) outside++;
        }
        sb.Append("home windows ").Append(homeWindows).Append(", notes off its grid's time ").Append(outside).Append("; ");
        if (outside > 0) fail = "§21: " + outside + " notes outside its own grid's windows";
        if (homeWindows == 0) fail = "no home window in the checked span";
        return (fail == null ? "PASS" : "FAIL " + fail) + " rider scheduling (§21: its own grid only): " + sb + "| " + Synth.Stats();
    }

    public static string RiderDisarm()
    {
        DisarmStalls();
        GlobalClock.Stop();
        var c = SequenceMaster.Cubes[0]; c.rider = false; Solo(-1); SequenceMaster.RecalculateTimeline();
        return "rider off, windows " + c.windows.Count;
    }

    /// <summary>Adds a Moon with a rolled groove, mutes every other cube and plays from beat 0 (MoonCheck reads the log later).</summary>
    public static string MoonArm()
    {
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        LoadFixture();
        var sm = SongManager.I;
        moonIdx = sm.AddMoon();
        if (moonIdx < 0) return "FAIL AddMoon";
        sm.RollMoonGroove(moonIdx);
        var sb = new StringBuilder(); int moonCubes = 0;
        foreach (var c in SequenceMaster.Cubes)
        {
            if (c == null) continue;
            c.SetMuted(!c.IsOnMoon);
            if (c.IsOnMoon) { moonCubes++; sb.Append("row").Append(c.nodes[0].gridZ).Append(" step").Append((int)c.step).Append(" hits").Append(c.hits).Append(" rot").Append(c.rot).Append(" windows").Append(c.windows.Count).Append(" hits16:").Append(Ints(c.HitSteps16(0, 0))).Append("; "); }
        }
        ArmStalls();
        GlobalClock.Seek(0); GlobalClock.Play();
        playT0 = GlobalClock.DspTimeOfBeat(0);
        return "moon " + moonIdx + " cubes " + moonCubes + ": " + sb;
    }

    /// <summary>Drum note-ons per bar since MoonArm vs the sum over the Moon's cubes of their firing steps (x ratchet) in that bar's window.</summary>
    public static string MoonCheck(int barsToCheck)
    {
        double bps = GlobalClock.BeatsPerSecond; int bpb = GlobalClock.BeatsPerBar;
        var sb = new StringBuilder(); string fail = null; int n = Performance.LoggedCount, stalled = 0;
        for (int b = 0; b < barsToCheck; b++)
        {
            double bs = playT0 + b * bpb / bps, be = bs + bpb / bps;
            int count = 0;
            for (int i = 0; i < n; i++) { var e = Performance.Logged(i); if (e.slot == SynthBank.DrumSlot && e.onDsp >= bs - 1e-3 && e.onDsp < be - 1e-3) count++; }
            int want = 0;
            foreach (var c in SequenceMaster.Cubes) if (c != null && c.IsOnMoon && b < c.windows.Count) foreach (int k in FiringSteps(c, b, b)) want += c.Decide(b, k, b).ratchet;
            sb.Append("bar").Append(b).Append('=').Append(count).Append("/want").Append(want).Append("; ");
            if (want > 0 && count < want && StalledIn(bs, be)) { stalled++; sb.Append("(bar ").Append(b).Append(" stalled: the main thread froze in it) "); }
            else if (count != want || want == 0) fail = "bar " + b;
        }
        if (stalled >= barsToCheck && fail == null) fail = "every bar stalled (rerun)";
        return (fail == null ? "PASS" : "FAIL " + fail) + " moon scheduling: " + sb + "| " + Synth.Stats();
    }

    public static string MoonDisarm()
    {
        DisarmStalls();
        GlobalClock.Stop();
        var sm = SongManager.I;
        if (moonIdx >= 0 && moonIdx < sm.Moons.Count) sm.RemoveMoon(moonIdx);
        moonIdx = -1;
        Solo(-1);
        return "moons " + sm.Moons.Count + " cubes " + SequenceMaster.Cubes.Count;
    }

    public static string PlayAll()
    {
        if (GlobalClock.IsPlaying) GlobalClock.Stop();
        LoadFixture();
        GlobalClock.Seek(0); GlobalClock.Play();
        playT0 = GlobalClock.DspTimeOfBeat(0);
        return "playing " + SequenceMaster.Cubes.Count + " cubes";
    }

    public static string StutterEngage() { Performance.HoldStutter(true); return "engage requested at " + GlobalClock.DspNow.ToString("F3") + " next16 " + GlobalClock.DspOfNext16th().ToString("F3") + " pending " + Performance.PendingCount; }

    public static string StutterCheckEngaged()
    {
        double eng = Performance.StutterEngagedAtDsp;
        long after = Performance.LoggedTotal - Performance.StutterLogTotalAtEngage;
        bool ok = Performance.StutterActive && Performance.StutterHeld && eng > 0 && Performance.StutterCapturedCount > 0 && after == 0 && GlobalClock.DspNow > eng;
        return (ok ? "PASS" : "FAIL") + " stutter engaged: active=" + Performance.StutterActive + " captured=" + Performance.StutterCapturedCount + " engagedAt=" + eng.ToString("F3") + " now=" + GlobalClock.DspNow.ToString("F3") + " cubeEventsSinceEngage=" + after + " | " + Synth.Stats();
    }

    public static string StutterRelease() { Performance.HoldStutter(false); return "release requested at " + GlobalClock.DspNow.ToString("F3") + " pending " + Performance.PendingCount; }

    public static string StutterCheckReleased()
    {
        double rel = Performance.StutterReleasedAtDsp; int n = Performance.LoggedCount; int after = 0; double firstAfter = -1;
        for (int i = 0; i < n; i++) { var e = Performance.Logged(i); if (rel > 0 && e.onDsp >= rel - 1e-3) { after++; if (firstAfter < 0 || e.onDsp < firstAfter) firstAfter = e.onDsp; } }
        bool ok = !Performance.StutterActive && !Performance.StutterHeld && rel > 0 && after > 0 && Performance.PendingCount == 0;
        return (ok ? "PASS" : "FAIL") + " stutter released: active=" + Performance.StutterActive + " releasedAt=" + rel.ToString("F3") + " cubeEventsAfterRelease=" + after + " firstAfter+" + (firstAfter - rel).ToString("F3") + " | " + Synth.Stats();
    }

    public static string RecKeepPlaying(float seconds) { Synth.StartRecording(seconds); return "recording " + seconds + " s"; }
    public static string SaveKeepPlaying(string path) { bool ok = Synth.SaveRecording(path); return (ok ? "saved " : "FAILED ") + path + " | " + Synth.Stats(); }
    public static string Half(bool on) { Performance.HoldHalf(on); return "half " + on + " bpm " + GlobalClock.BPM + " pending " + Performance.PendingCount; }
    public static string Spot(bool on) { var c = SequenceMaster.Cubes[0]; Performance.HoldSpotlight(c, on); return "spot " + on + " pending " + Performance.PendingCount; }

    /// <summary>Max distance of every cube from its rest position (a hop in progress shows as a larger value) and the count of cubes.</summary>
    public static string CubePose()
    {
        float worst = 0f; int n = 0;
        foreach (var c in SequenceMaster.Cubes) { if (c == null) continue; n++; worst = Mathf.Max(worst, (c.transform.position - c.RestPositionNow).magnitude); }
        return "cubes=" + n + " maxOffset=" + worst.ToString("F3") + " beat=" + GlobalClock.SongBeat.ToString("F2");
    }
}
