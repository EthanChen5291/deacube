using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// v9 (R; the user: "the transitions should be cooler … just more effects", Geometry Dash's "not just the music but the effects too"): SECTION
/// CHANGES everywhere, not only launches. On every section's first downbeat a lighter version of the crash: an ink RIPPLE across the new column's
/// floor (its plinth top), the column's LANES lighting up front to back on 16ths (every tile of the lane flashing, a ring at its centre, a small
/// bounce), the letter plinth pulsing (SectionPlinth does that itself, a pure function of the beat). An energy 3 section hits harder than energy 0
/// (a wider, longer ripple, a second ring, a bloom bump). The rings and bounces are pure functions of the song beat (pause / seek / replay exact);
/// the lane flashes fire on their 16th's crossing. Pooled rings, one MaterialPropertyBlock: no per-frame GC. Plays in the present mode too.
/// Synced from the song and stepped by <see cref="WorldMagic"/>.
/// </summary>
public class SectionFx
{
    /// <summary>The ripple's life (beats), the lanes' 16th step (beats), the lanes' bounce (u at strength 1) and the pulse window (beats).</summary>
    public static float RingBeats = 1.5f, LaneStep = 0.25f, LaneBounce = 0.16f, Window = 2f;
    const int RingCap = 10;

    /// <summary>Counters since Play (tests): section downbeats pulsed (all / per section), lanes lit, bloom bumps.</summary>
    public int Pulses, LaneLights, Bumps;
    public int LastSection = -1;
    public int PulsesOf(int s) => s >= 0 && s < pulses.Length ? pulses[s] : 0;
    /// <summary>True while a section start's effects show; rings shown this frame; sections known.</summary>
    public bool Active { get; private set; }
    public int ActiveRings { get; private set; }
    public int Count => used;
    /// <summary>The start beat / energy of section <paramref name="s"/> (tests).</summary>
    public double StartOf(int s) => s >= 0 && s < used ? secs[s].start : -1.0;
    public int EnergyOf(int s) => s >= 0 && s < used ? secs[s].energy : -1;
    /// <summary>True when a pool grew this frame (an allocation the steady state never sees).</summary>
    public bool Grew { get; private set; }

    class Sec
    {
        public double start; public int col, energy, section; public float cx, cz, spread; public Color color;
        public readonly List<KeyBlock> lanes = new List<KeyBlock>(8);
        public bool armed = true; public int litMask;   // the start's one-shot and the lanes' flashes fire once per pass (re-armed well before the start)
    }
    readonly List<Sec> secs = new List<Sec>();
    int used;
    int[] pulses = new int[16];
    Transform root; Material ringMat;
    readonly List<Transform> rings = new List<Transform>(); readonly List<MeshRenderer> ringR = new List<MeshRenderer>();
    int ringUsed;
    MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_Color"), IntensityId = Shader.PropertyToID("_Intensity");

    public void Build(Transform parent)
    {
        if (root != null) return;
        var go = new GameObject("SectionFx");
        go.transform.SetParent(parent, false);
        root = go.transform;
        ringMat = Fx.InkMaterial(1); ringMat.renderQueue = 3003;
        mpb = new MaterialPropertyBlock();
    }

    /// <summary>Re-reads the song's sections: their start beats, first columns, energies, lanes (front to back) and floor centres.</summary>
    public void Sync(SongManager sm)
    {
        used = 0;
        if (sm == null || !sm.HasSong) return;
        var st = sm.SectionStarts();
        if (pulses.Length < st.Count) Array.Resize(ref pulses, st.Count + 8);
        for (int s = 0; s < st.Count; s++)
        {
            Sec sec;
            if (s < secs.Count) sec = secs[s]; else { sec = new Sec(); secs.Add(sec); }
            int col = st[s];
            var anchor = sm.AnchorOf(col);
            sec.section = s; sec.col = col; sec.start = sm.ColumnStart(col);
            sec.energy = anchor != null ? Mathf.Clamp(anchor.energy, 0, 3) : 2;
            sm.ColumnIslands(col, sec.lanes);
            for (int i = sec.lanes.Count - 1; i >= 0; i--) if (sec.lanes[i] == null || sec.lanes[i].IsMoon) sec.lanes.RemoveAt(i);
            for (int i = 1; i < sec.lanes.Count; i++)
            {
                var k = sec.lanes[i]; int j = i - 1;
                while (j >= 0 && sec.lanes[j].pz > k.pz) { sec.lanes[j + 1] = sec.lanes[j]; j--; }
                sec.lanes[j + 1] = k;
            }
            var b = sm.ColumnBounds(col);
            sec.cx = sm.ColumnCenterX(col); sec.cz = b.center.z;
            sec.spread = sm.ColumnWidth(col) * 0.5f + 3.5f + sec.energy * 1.2f;
            string letter = SectionPlinth.LetterOf(s);
            Color tint = string.IsNullOrEmpty(letter) ? (anchor != null ? anchor.chordColor : Color.white) : SectionPlinth.LetterTint(letter);
            sec.color = Color.Lerp(tint, Color.white, 0.35f);
            used++;
        }
    }

    float FloorY(Sec sec)
    {
        float y = SectionPlinth.TopOf(sec.section, 0);
        if (Mathf.Abs(y) < 1e-6f && SectionPlinth.Count <= sec.section)
        {
            var a = sec.lanes.Count > 0 ? sec.lanes[0] : null;
            y = (a != null ? a.transform.position.y : 0f) - SectionPlinth.TopDrop;
        }
        return y + 0.04f;
    }

    Transform Ring(int i)
    {
        while (rings.Count <= i)
        {
            var go = new GameObject("sectionRing");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = MagicMeshes.FlatRing(0.84f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ringMat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
            go.SetActive(false);
            rings.Add(go.transform); ringR.Add(mr);
            Grew = true;
        }
        return rings[i];
    }

    void ShowRing(Sec sec, float a, float floorY, float strength)
    {
        if (ringUsed >= RingCap || a < 0f || a >= 1f) return;
        var t = Ring(ringUsed);
        float d = Mathf.Lerp(0.6f, sec.spread, Ease.OutCubic(a)) * 2f;
        t.position = new Vector3(sec.cx, floorY, sec.cz);
        t.localScale = new Vector3(d, 1f, d);
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        mpb.Clear();
        mpb.SetColor(ColorId, sec.color);
        mpb.SetFloat(IntensityId, Mathf.Pow(1f - a, 1.3f) * strength);
        ringR[ringUsed].SetPropertyBlock(mpb);
        ringUsed++;
    }

    /// <summary>One frame: <paramref name="beat"/> / <paramref name="prev"/> the song beat now and last frame (WorldMagic.FxBeat).</summary>
    public void Step(bool world, double beat, double prev, bool playing, WorldMagic wm)
    {
        Grew = false;
        if (root == null) return;
        bool running = world && (playing || beat > 1e-6);
        Active = false; ringUsed = 0;
        double loopLen = GlobalClock.LoopLengthBeats;
        for (int s = 0; s < used && running; s++)
        {
            var sec = secs[s];
            double age = beat - sec.start, prevAge = prev - sec.start;
            // the song's wrap: the first section's start comes round again
            if (age < 0.0 && loopLen > 1.0 && sec.start <= GlobalClock.LoopStartBeat + 1e-3) { age += loopLen; prevAge += loopLen; }
            if (age < -0.5 || age >= Window) { sec.armed = true; sec.litMask = 0; continue; }
            if (age < 0.0) continue;
            Active = true;
            float strength = 0.55f + 0.15f * sec.energy;
            bool crossed = playing && sec.armed && (prevAge < 0.0 || prevAge > age || age < 1.0);
            if (crossed)
            {
                sec.armed = false;
                Pulses++; if (s < pulses.Length) pulses[s]++; LastSection = s;
                if (sec.energy >= 3) { Fx.BeatBump(0.3f); Bumps++; }
            }
            float floorY = FloorY(sec);
            ShowRing(sec, (float)(age / RingBeats), floorY, strength);
            if (sec.energy >= 2) ShowRing(sec, (float)((age - 0.3) / RingBeats), floorY, strength * 0.7f);
            // the lanes light front to back on 16ths
            for (int i = 0; i < sec.lanes.Count; i++)
            {
                var kb = sec.lanes[i];
                if (kb == null) continue;
                double ti = i * LaneStep;
                if (age < ti) break;
                if (playing && i < 31 && (sec.litMask & (1 << i)) == 0 && age - ti < 0.5) { sec.litMask |= 1 << i; LightLane(kb, sec, strength, wm); }
                float v = (float)((age - ti) / 0.6);
                if (v >= 1f || wm == null || sec.start < 1e-3) continue;   // (the song's own start: no bounce — the follow and the clock are settling there)
                var lf = wm.LaunchOf(kb);
                if (lf != null && (lf.Live || lf.Swell >= 0f)) continue;   // a launching grid tenses through its swell instead (no stacking; it stays put: §21.4)
                wm.AddLiftStepped(kb, LaneBounce * strength * Mathf.Sin(v * Mathf.PI) * (1f - v));
            }
        }
        ActiveRings = ringUsed;
        for (int i = ringUsed; i < rings.Count; i++) if (rings[i].gameObject.activeSelf) rings[i].gameObject.SetActive(false);
    }

    void LightLane(KeyBlock kb, Sec sec, float strength, WorldMagic wm)
    {
        LaneLights++;
        Color pale = Color.Lerp(kb.chordColor, Color.white, 0.55f);
        // the lane's FIRST measure lights (a long grid's later measures stay: fewer tiles woken in one frame — the burst must not stall a frame)
        float west = kb.WestEdge + (kb.transform.position.x - kb.px) + KeyBlock.IslandWidth;
        int lit = 0;
        foreach (var t in kb.tiles) if (t != null && t.Top.x < west && lit < 24) { t.Flash(pale, 0.8f * strength); lit++; }
        Vector3 c = kb.VisualCenter + Vector3.up * (0.35f + kb.FxLift);
        Fx.Ripple(c, sec.color, Mathf.Max(0.8f, kb.Width * 0.45f), 0.38f);
        if (sec.energy >= 2 && wm != null)
            for (int i = 0; i < 4; i++) wm.Sparkle(c + Vector3.up * 0.2f, UnityEngine.Random.insideUnitSphere * 0.7f + Vector3.up * 1.4f, i % 2 == 0 ? Color.white : pale, 0.16f, 0.4f);
    }
}
