using System;
using System.Collections.Generic;
#if !HARMONY_PURE
using UnityEngine;
#endif

/// <summary>
/// v6 (SPEC v6 §2.2): the song's harmony helpers every package reads. The core is pure (ints and arrays, no scene access; unit-tested
/// outside Unity with HARMONY_PURE); the Unity-facing helpers at the bottom only read islands and cubes.
/// JOBS — the number a chord wears on its card, its island and its rail bead (the user: "a number (like 1 2 3 4 in the chord progression …
/// 1234 can also work as 1432 so they should know it can be swapped"): 1 home (I / i), 2 away (ii, IV / ii, iv), 3 heart (iii, vi / III, VI),
/// 4 pull (V, vii / v, VII, and any dominant 7 / 9 anywhere: it wants to go home). Same number = same job = those chords swap freely, and the
/// four jobs in any order make a pop progression (1 2 3 4 = I IV vi V, 1 4 3 2 = I V vi IV, 3 2 1 4 = vi IV I V, 2 4 3 1 = IV V vi I).
/// FIT — how well a pattern's notes sit on a chord (0..1, the card's star): a chord tone scores 1, another note of the key 0.55 unless it sits a
/// half step above a chord tone (an avoid note: 0), anything else 0; weighted by note length and downbeats.
/// ADAPT — a pattern moved onto another chord (paste, carry), "within the genre" (J-pop / Vocaloid comping, the gallery's corpus): a grid pattern
/// keeps its shape on the target grid, shifted by the inversion that keeps it closest in pitch (voice leading; a bass line keeps the root on its
/// first note); a melody moves as a sequence by scale steps (the chord roots' distance), its long / downbeat notes land on chord tones, and it
/// stays in the keyboard's range.
/// v7 (SPEC v7 §2.3): STAIRS (a run that steps down / up into the next chord, six types), HARMONIZE (a support voice a 4th / 5th away that
/// dodges the other notes), ADAPT PEDAL (a pattern flowing through changing chords: what fits stays), VARY (the same grid replayed with small
/// differences) and ANSWER (a phrase that ends at home).
/// </summary>
public static class Harmony
{
    /// <summary>A card whose chord fits the reference pattern at least this well wears a star.</summary>
    public const float StarThreshold = 0.72f;
    /// <summary>Fit score of a key note that is not a chord tone and not an avoid note (a tension).</summary>
    public const float TensionScore = 0.55f;

    static readonly int[] MajorSteps = { 0, 2, 4, 5, 7, 9, 11 };
    static readonly int[] MinorSteps = { 0, 2, 3, 5, 7, 8, 10 };
    // job per scale degree (I ii iii IV V vi vii / i ii III iv v VI VII)
    static readonly int[] JobByDegree = { 1, 2, 3, 2, 4, 3, 4 };
    static readonly string[] JobWords = { "", "home", "away", "heart", "pull" };
    static readonly string[] JobFeelings = { "", "start and end here", "step out", "the feelings", "wants to go home" };

    public static int Pc(int midi) => ((midi % 12) + 12) % 12;
    static int[] StepsOf(bool minor) => minor ? MinorSteps : MajorSteps;

    /// <summary>Scale degree 0..6 of pitch class <paramref name="pc"/> in the key, -1 off the scale.</summary>
    public static int Degree(int tonic, bool minor, int pc)
    {
        int rel = Pc(pc - tonic);
        var st = StepsOf(minor);
        for (int d = 0; d < 7; d++) if (st[d] == rel) return d;
        return -1;
    }

    public static bool InScale(int tonic, bool minor, int pc) => Degree(tonic, minor, pc) >= 0;

    static bool Has(IList<int> semis, int s) { if (semis == null) return false; for (int i = 0; i < semis.Count; i++) if (Pc(semis[i]) == s) return true; return false; }
    public static bool IsDominant(IList<int> semis) => Has(semis, 4) && Has(semis, 10);
    public static bool IsSus(IList<int> semis) => Has(semis, 5) && !Has(semis, 3) && !Has(semis, 4);
    public static bool IsMinorChord(IList<int> semis) => Has(semis, 3) && !Has(semis, 4);

    // ------------------------------------------------------------------ jobs
    /// <summary>The chord's job in the key: 1 home, 2 away, 3 heart, 4 pull (see the class summary). Rules in order: a dominant 7 / 9 → 4; a root
    /// on the scale → its degree's job; off the scale: sus4 → 4, a minor chord → 2 (the borrowed iv), a major chord → 3 (the borrowed bVI / bIII).</summary>
    public static int Job(int tonic, bool minor, int rootMidi, IList<int> semis)
    {
        if (IsDominant(semis)) return 4;
        int d = Degree(tonic, minor, Pc(rootMidi));
        if (d >= 0) return JobByDegree[d];
        if (IsSus(semis)) return 4;
        return IsMinorChord(semis) ? 2 : 3;
    }

    public static string JobWord(int job) => job >= 1 && job <= 4 ? JobWords[job] : "";
    public static string JobFeeling(int job) => job >= 1 && job <= 4 ? JobFeelings[job] : "";
    /// <summary>Two chords can trade places in a progression when they do the same job.</summary>
    public static bool Swappable(int jobA, int jobB) => jobA >= 1 && jobA == jobB;

    // ------------------------------------------------------------------ fit (the cards' stars)
    /// <summary>Score of one pitch class over the chord: 1 chord tone, <see cref="TensionScore"/> a key note that is not an avoid note, else 0.</summary>
    public static float NoteScore(int pc, int rootMidi, IList<int> semis, int tonic, bool minor)
    {
        pc = Pc(pc);
        int rel = Pc(pc - rootMidi);
        if (Has(semis, rel)) return 1f;
        if (Has(semis, Pc(rel - 1))) return 0f;                  // a half step above a chord tone: the avoid note
        return InScale(tonic, minor, pc) ? TensionScore : 0f;
    }

    // ------------------------------------------------------------------ v8: the melody's safe notes (a keyboard's dots)
    /// <summary>
    /// v8 (the user: "melody notes can be more varied and experimental than regular grid notes … analyze the given genre and ensure that the notes
    /// allowed on the piano … cover all possible notes that could work"): the pitch classes (index = absolute pc 0..11) a MELODY can land on over
    /// the chord — wider than the grid's chord tones. The genre is the game's: J-pop / Vocaloid with city-pop jazz harmony (the gallery's corpus, the
    /// v7 reference cover: maj7 / m7 colours, circle-of-fifths dominants with b9, borrowed iv / bVI / bVII). Rules: every CHORD TONE; plus the chord's
    /// SCALE minus its avoid notes (a half step above a chord tone) — a chord of the key takes the key's own scale (its mode: the 9th, 11th on minor
    /// chords, 13th, maj7 / b7 and IV's #11 colour), a dominant 7 the city-pop altered mixolydian (9, 13 and the tensions b9 #9 #11 b13 — its 11th
    /// stays out), a half-diminished chord locrian natural 2, a diminished 7 the whole-half scale, a borrowed minor chord dorian (the iv6 colour), a
    /// borrowed major chord lydian, a sus chord mixolydian without the 3rd. <paramref name="tension"/> (when not null) marks the non-chord tones.
    /// </summary>
    public static bool[] MelodyPool(int rootMidi, IList<int> semis, int tonic, bool minor, bool[] tension = null)
    {
        var on = new bool[12];
        int root = Pc(rootMidi);
        bool dom = IsDominant(semis);
        bool dim7 = Has(semis, 3) && Has(semis, 6) && Has(semis, 9) && !Has(semis, 10);
        bool half = Has(semis, 3) && Has(semis, 6) && Has(semis, 10);
        bool diatonic = InScale(tonic, minor, root);
        if (semis != null) for (int i = 0; i < semis.Count && diatonic; i++) if (!InScale(tonic, minor, root + semis[i])) diatonic = false;
        int[] rel;
        if (dim7) rel = WholeHalf;
        else if (half) rel = LocrianNat2;
        else if (dom) rel = AlteredMixo;
        else if (diatonic) rel = null;                       // the key's own scale (the chord's mode)
        else if (IsSus(semis)) rel = SusMixo;
        else if (IsMinorChord(semis)) rel = Dorian;
        else rel = Lydian;
        if (rel == null) { var st = StepsOf(minor); for (int d = 0; d < 7; d++) on[Pc(tonic + st[d])] = true; }
        else foreach (int r in rel) on[Pc(root + r)] = true;
        // the avoid notes: a half step above a chord tone (a dominant keeps its altered tensions b9 and b13)
        for (int pc = 0; pc < 12; pc++)
        {
            if (!on[pc]) continue;
            int r = Pc(pc - root);
            if (Has(semis, r)) continue;
            if (dom && (r == 1 || r == 3 || r == 6 || r == 8)) continue;
            if (Has(semis, Pc(r - 1))) on[pc] = false;
        }
        if (semis != null) for (int i = 0; i < semis.Count; i++) on[Pc(root + semis[i])] = true;   // every chord tone
        if (tension != null) for (int pc = 0; pc < 12; pc++) tension[pc] = on[pc] && !Has(semis, Pc(pc - root));
        return on;
    }
    static readonly int[] WholeHalf = { 0, 2, 3, 5, 6, 8, 9, 11 }, LocrianNat2 = { 0, 2, 3, 5, 6, 8, 10 }, AlteredMixo = { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10 };
    static readonly int[] SusMixo = { 0, 2, 5, 7, 9, 10 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 }, Lydian = { 0, 2, 4, 6, 7, 9, 11 };

    /// <summary>How well notes <paramref name="midi"/> (weights <paramref name="w"/>; null = equal) sit on the chord, 0..1 (0 when there are no notes).</summary>
    public static float Fit(IList<int> midi, IList<float> w, int rootMidi, IList<int> semis, int tonic, bool minor)
    {
        if (midi == null || midi.Count == 0) return 0f;
        double sum = 0, tot = 0;
        for (int i = 0; i < midi.Count; i++)
        {
            double wi = w != null && i < w.Count ? Math.Max(0f, w[i]) : 1.0;
            sum += wi * NoteScore(midi[i], rootMidi, semis, tonic, minor);
            tot += wi;
        }
        return tot > 0 ? (float)(sum / tot) : 0f;
    }

    /// <summary>Note weights for fit / adaptation from note lengths in ticks (24 = a beat): the length, × 1.5 when the note starts on a beat.
    /// <paramref name="durs"/> null or short = equal lengths (a quarter each).</summary>
    public static float[] WeightsOf(IList<int> durs, int count)
    {
        var r = new float[Math.Max(0, count)];
        int t = 0;
        for (int i = 0; i < count; i++)
        {
            int d = durs != null && i < durs.Count && durs[i] > 0 ? durs[i] : 24;
            r[i] = d / 24f * (t % 24 == 0 ? 1.5f : 1f);
            t += d;
        }
        return r;
    }

    // ------------------------------------------------------------------ adapt: grid patterns
    /// <summary>
    /// A grid pattern (nodes at <paramref name="srcX"/>/<paramref name="srcZ"/> sounding <paramref name="srcMidi"/>, weights <paramref name="w"/>,
    /// on a grid of <paramref name="srcRows"/> rows) placed on the target grid whose tile pitches are <paramref name="tgt"/>[x, z]: rows keep their
    /// index (the source's top row maps to the target's top row), the whole shape shifts by the inversion offset whose pitches stay closest to the
    /// source (voice leading; ties → the smaller shift); <paramref name="bassLike"/> adds a penalty when the first note is not the target root
    /// (<paramref name="tgtRootPc"/>, -1 = no rule). Shifts that push a node off the grid are skipped (all off → the nodes are clamped).
    /// </summary>
    public static void AdaptGrid(IList<int> srcMidi, IList<int> srcX, IList<int> srcZ, IList<float> w, int srcRows, int[,] tgt, bool bassLike, int tgtRootPc, out int[] xs, out int[] zs)
    {
        int n = srcX != null ? srcX.Count : 0;
        xs = new int[n]; zs = new int[n];
        if (n == 0 || tgt == null) return;
        int cols = tgt.GetLength(0), rows = tgt.GetLength(1);
        if (cols <= 0 || rows <= 0) return;
        var mz = new int[n];
        for (int i = 0; i < n; i++)
        {
            int z = srcZ[i];
            mz[i] = srcRows > 1 && z >= srcRows - 1 ? rows - 1 : Math.Max(0, Math.Min(z, rows - 1));
        }
        int best = 0; double bestCost = double.MaxValue; bool anyValid = false;
        for (int pass = 0; pass < 2 && !anyValid; pass++)
        {
            bool clamp = pass == 1;
            for (int s = -(cols - 1); s <= cols - 1; s++)
            {
                bool ok = true;
                if (!clamp) for (int i = 0; i < n; i++) { int x = srcX[i] + s; if (x < 0 || x >= cols) { ok = false; break; } }
                if (!ok) continue;
                double cost = 0, tot = 0;
                for (int i = 0; i < n; i++)
                {
                    int x = Math.Max(0, Math.Min(cols - 1, srcX[i] + s));
                    double wi = w != null && i < w.Count ? Math.Max(0.05f, w[i]) : 1.0;
                    int sm = srcMidi != null && i < srcMidi.Count ? srcMidi[i] : tgt[x, mz[i]];
                    cost += wi * Math.Abs(tgt[x, mz[i]] - sm);
                    tot += wi;
                }
                cost = tot > 0 ? cost / tot : 0;
                cost += 0.01 * Math.Abs(s);
                if (bassLike && tgtRootPc >= 0)
                {
                    int x0 = Math.Max(0, Math.Min(cols - 1, srcX[0] + s));
                    if (Pc(tgt[x0, mz[0]]) != tgtRootPc) cost += 12.0;   // an octave's worth: the root rule wins over voice leading
                }
                if (cost < bestCost - 1e-9) { bestCost = cost; best = s; }
                anyValid = true;
            }
        }
        for (int i = 0; i < n; i++) { xs[i] = Math.Max(0, Math.Min(cols - 1, srcX[i] + best)); zs[i] = mz[i]; }
    }

    // ------------------------------------------------------------------ adapt: melodies
    /// <summary>
    /// A melody (<paramref name="srcMidi"/>, weights <paramref name="w"/>) over chord (<paramref name="srcRoot"/>, <paramref name="srcSemis"/>)
    /// moved onto chord (<paramref name="tgtRoot"/>, <paramref name="tgtSemis"/>) in the key: when both roots are on the scale every scale note moves
    /// by the roots' distance in scale steps (a sequence, -3..+3 steps) and other notes by the roots' interval; otherwise everything moves by the
    /// roots' interval (-6..+5). Notes weighing at least the median that end a half step above a target chord tone snap onto it. The result is
    /// moved by octaves into [<paramref name="lo"/>, <paramref name="hi"/>] (the whole line when it fits, then note by note).
    /// </summary>
    public static int[] AdaptMelody(IList<int> srcMidi, IList<float> w, int srcRoot, IList<int> srcSemis, int tgtRoot, IList<int> tgtSemis, int tonic, bool minor, int lo, int hi)
    {
        int n = srcMidi != null ? srcMidi.Count : 0;
        var r = new int[n];
        if (n == 0) return r;
        int ds = Degree(tonic, minor, Pc(srcRoot)), dt = Degree(tonic, minor, Pc(tgtRoot));
        int semiShift = Pc(tgtRoot - srcRoot); if (semiShift > 5) semiShift -= 12;
        int stepShift = 0; bool diatonic = ds >= 0 && dt >= 0;
        if (diatonic) { stepShift = ((dt - ds) % 7 + 7) % 7; if (stepShift > 3) stepShift -= 7; }
        var st = StepsOf(minor);
        for (int i = 0; i < n; i++)
        {
            int m = srcMidi[i];
            int d = diatonic ? Degree(tonic, minor, Pc(m)) : -1;
            if (d < 0) { r[i] = m + semiShift; continue; }
            int rel = m - tonic;
            int oct = (int)Math.Floor(rel / 12.0);
            int idx = d + stepShift;
            int octMove = (int)Math.Floor(idx / 7.0);
            idx -= octMove * 7;
            r[i] = tonic + 12 * (oct + octMove) + st[idx];
        }
        // long / downbeat notes land on chord tones
        if (tgtSemis != null && tgtSemis.Count > 0)
        {
            var ws = new List<float>(n);
            for (int i = 0; i < n; i++) ws.Add(w != null && i < w.Count ? w[i] : 1f);
            var sorted = new List<float>(ws); sorted.Sort();
            float median = sorted[sorted.Count / 2];
            for (int i = 0; i < n; i++)
            {
                if (ws[i] < median || ws[i] <= 0f) continue;
                int rel = Pc(r[i] - tgtRoot);
                if (Has(tgtSemis, rel)) continue;
                if (Has(tgtSemis, Pc(rel - 1))) r[i] -= 1;          // a half step above a chord tone (the avoid note) → onto it; a half step
                                                                    // below one is a colour (a 9th, a major 7th) and stays
            }
        }
        FitRange(r, lo, hi, srcMidi);
        return r;
    }

    /// <summary>Moves the line by octaves into [lo, hi] (the octave closest to <paramref name="reference"/>'s mean when the line fits), then folds
    /// single notes that still stick out.</summary>
    public static void FitRange(int[] line, int lo, int hi, IList<int> reference = null)
    {
        if (line == null || line.Length == 0 || hi < lo) return;
        int mn = int.MaxValue, mx = int.MinValue; double mean = 0;
        foreach (int m in line) { mn = Math.Min(mn, m); mx = Math.Max(mx, m); mean += m; }
        mean /= line.Length;
        double refMean = mean;
        if (reference != null && reference.Count > 0) { refMean = 0; foreach (int m in reference) refMean += m; refMean /= reference.Count; }
        int bestK = 0; double bestD = double.MaxValue; bool fits = false;
        for (int k = -4; k <= 4; k++)
        {
            bool inside = mn + 12 * k >= lo && mx + 12 * k <= hi;
            double dist = Math.Abs(mean + 12 * k - refMean);
            if (inside && (!fits || dist < bestD)) { fits = true; bestD = dist; bestK = k; }
            else if (!fits && dist < bestD) { bestD = dist; bestK = k; }
        }
        for (int i = 0; i < line.Length; i++)
        {
            int m = line[i] + 12 * bestK;
            while (m < lo) m += 12;
            while (m > hi) m -= 12;
            if (m < lo) m = lo;
            line[i] = m;
        }
    }

    /// <summary>The tile of <paramref name="tgt"/> sounding <paramref name="midi"/> (else the nearest pitch; ties → lower), preferring columns near
    /// <paramref name="preferX"/> (-1 = none) so a converted line stays compact.</summary>
    public static void NearestTile(int midi, int[,] tgt, int preferX, out int x, out int z)
    {
        x = 0; z = 0;
        if (tgt == null) return;
        int cols = tgt.GetLength(0), rows = tgt.GetLength(1);
        long best = long.MaxValue;
        for (int cx = 0; cx < cols; cx++)
            for (int cz = 0; cz < rows; cz++)
            {
                int d = tgt[cx, cz] - midi;
                long cost = (long)Math.Abs(d) * 100 + (d > 0 ? 1 : 0) * 10 + (preferX >= 0 ? Math.Abs(cx - preferX) : cx);
                if (cost < best) { best = cost; x = cx; z = cz; }
            }
    }

    /// <summary>A line of pitches placed on a grid tile by tile (<see cref="NearestTile"/>, each node preferring the previous node's column).</summary>
    public static void MelodyToGrid(IList<int> midi, int[,] tgt, out int[] xs, out int[] zs)
    {
        int n = midi != null ? midi.Count : 0;
        xs = new int[n]; zs = new int[n];
        int px = -1;
        for (int i = 0; i < n; i++) { NearestTile(midi[i], tgt, px, out xs[i], out zs[i]); px = xs[i]; }
    }

    // ------------------------------------------------------------------ v7 (SPEC v7 §2.3): stairs, support harmony, the pedal, variations
    // What the user's reference recording does with ONE eight-chord loop, turned into tools: a run that steps down into the next chord (Stairs),
    // a second voice a 4th / 5th away (Harmonize), a pattern that flows through changing chords moving only what has to move (AdaptPedal), the
    // same grid replayed with small differences (Vary) and a phrase that ends at home (Answer).

    static readonly string[] StairWords = { "chord", "scale", "spark", "slide", "bright", "walk" };
    static readonly int[] PentaSteps = { 0, 2, 4, 7, 9 };

    /// <summary>The stairs type's caption: 0 chord, 1 scale, 2 spark, 3 slide, 4 bright, 5 walk.</summary>
    public static string StairTypeWord(int type) => type >= 0 && type < StairWords.Length ? StairWords[type] : "";

    /// <summary>How well a stairs type suits a chord, 0..1 (the stairs header's star goes to the best): spark is the ♭9 fall of a dominant (1 on a
    /// dominant, 0.3 elsewhere); chord 0.85; scale 0.8; bright (the major pentatonic) 0.9 on home / heart chords (<paramref name="job"/> 1 / 3),
    /// else 0.7; slide 0.6; walk 0.7.</summary>
    public static float StairFit(int type, IList<int> semis, int job = 0)
    {
        switch (type)
        {
            case 0: return 0.85f;
            case 1: return 0.8f;
            case 2: return IsDominant(semis) ? 1f : 0.3f;
            case 3: return 0.6f;
            case 4: return job == 1 || job == 3 ? 0.9f : 0.7f;
            case 5: return 0.7f;
        }
        return 0f;
    }

    /// <summary>The pitch classes a stairs type steps on (see <see cref="Stairs"/>).</summary>
    public static bool[] StairPool(int type, int rootMidi, IList<int> semis, int tonic, bool minor)
    {
        var pcs = new bool[12];
        switch (type)
        {
            case 1: case 5: foreach (int s in StepsOf(minor)) pcs[Pc(tonic + s)] = true; break;
            case 2:
                if (IsDominant(semis)) { int third = rootMidi + 4; for (int k = 0; k < 4; k++) pcs[Pc(third + 3 * k)] = true; }
                else if (semis != null) foreach (int s in semis) pcs[Pc(rootMidi + s)] = true;
                break;
            case 3: for (int i = 0; i < 12; i++) pcs[i] = true; break;
            case 4: { int maj = minor ? tonic + 3 : tonic; foreach (int s in PentaSteps) pcs[Pc(maj + s)] = true; } break;
            default: if (semis != null) foreach (int s in semis) pcs[Pc(rootMidi + s)] = true; break;
        }
        bool any = false; for (int i = 0; i < 12; i++) any |= pcs[i];
        if (!any) pcs[Pc(rootMidi)] = true;
        return pcs;
    }

    /// <summary>
    /// A stairs run of <paramref name="steps"/> notes, strictly falling (<paramref name="dir"/> −1, 0 reads as −1) or rising (+1), over the chord
    /// (<paramref name="rootMidi"/>, <paramref name="semis"/>) and LEADING INTO the next chord (<paramref name="nextRoot"/>, <paramref name="nextSemis"/>).
    /// Each type steps on its pool (<see cref="StairPool"/>): 0 chord = the chord's tones, 1 scale = the key's scale, 2 spark = the diminished
    /// seventh on a dominant's 3rd (the ♭9 fall; other chords: their tones), 3 slide = chromatic, 4 bright = the key's major pentatonic, 5 walk =
    /// the key's scale landing on the next ROOT (a bass walk). The run approaches a landing L — a tone of the next chord — from above (falling) /
    /// below (rising): its last step is the pool pitch next to L. L is chosen so the first step sits nearest <paramref name="topHint"/> with every
    /// step inside [<paramref name="lo"/>, <paramref name="hi"/>] (ties: a root / 3rd landing, then the lower). Pure.
    /// </summary>
    public static int[] Stairs(int type, int dir, int steps, int rootMidi, IList<int> semis, int nextRoot, IList<int> nextSemis, int tonic, bool minor, int topHint, int lo, int hi)
    {
        steps = Math.Max(1, Math.Min(16, steps));
        if (dir == 0) dir = -1;
        dir = dir < 0 ? -1 : 1;
        if (hi < lo) { int t = lo; lo = hi; hi = t; }
        var pool = StairPool(type, rootMidi, semis, tonic, minor);
        var land = new bool[12];
        var strong = new bool[12];
        IList<int> ns = nextSemis != null && nextSemis.Count > 0 ? nextSemis : (IList<int>)new[] { 0, 4, 7 };
        if (type == 5) land[Pc(nextRoot)] = true;
        else foreach (int s in ns) land[Pc(nextRoot + s)] = true;
        strong[Pc(nextRoot)] = true;
        foreach (int s in ns) { int r = Pc(s); if (r == 3 || r == 4) strong[Pc(nextRoot + s)] = true; }

        int[] best = null; long bestCost = long.MaxValue;
        for (int pass = 0; pass < 2 && best == null; pass++)
        {
            bool inside = pass == 0;
            for (int L = lo - 14; L <= hi + 14; L++)
            {
                if (!land[Pc(L)]) continue;
                var run = new int[steps];
                int p = L, k = 0;
                for (int guard = 0; guard < 16 * 12 && k < steps; guard++)
                {
                    p -= dir;                                      // falling: pitches above L; rising: below
                    if (pool[Pc(p)]) run[steps - 1 - k++] = p;
                }
                if (k < steps) continue;
                bool ok = true;
                if (inside) foreach (int m in run) if (m < lo || m > hi) { ok = false; break; }
                if (!ok) continue;
                long cost = (long)Math.Abs(run[0] - topHint) * 100 + (strong[Pc(L)] ? 0 : 10) + (L - (lo - 14)) / 12;
                if (!inside) { int o = 0; foreach (int m in run) o += Math.Max(0, lo - m) + Math.Max(0, m - hi); cost += (long)o * 1000; }
                if (cost < bestCost) { bestCost = cost; best = run; }
            }
        }
        if (best == null) { best = new int[steps]; for (int i = 0; i < steps; i++) best[i] = Math.Max(lo, Math.Min(hi, topHint + dir * i)); }
        return best;
    }

    /// <summary>
    /// A SUPPORT VOICE for <paramref name="src"/> (−1 = a rest, kept) over the chord: per note the perfect interval first — above +7 then +5, below
    /// −5 then −7 ("5ths or 4ths basically") — then the sixths / thirds (±8, ±9, ±3, ±4). Scored by <see cref="NoteScore"/> (+0.35 for a perfect
    /// interval that fits), minus 0.8 per CLASH with <paramref name="others"/>[i] (the other pitches sounding at that note: a minor second, a
    /// major seventh or a minor ninth to any of them), minus 0.2 for doubling one of them, minus 0.02 per semitone of leap from the previous
    /// harmony note, plus 0.15 for keeping the previous interval (parallel motion reads as one voice); outside [<paramref name="lo"/>,
    /// <paramref name="hi"/>] −2. Pure.
    /// </summary>
    public static int[] Harmonize(IList<int> src, IList<float> w, int rootMidi, IList<int> semis, int tonic, bool minor, IList<IList<int>> others, bool above, int lo, int hi)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        int[] ivs = above ? new[] { 7, 5, 8, 9, 3, 4 } : new[] { -5, -7, -8, -9, -3, -4 };
        int prev = -1, prevIv = 0;
        for (int i = 0; i < n; i++)
        {
            int m = src[i];
            if (m < 0) { r[i] = -1; continue; }
            IList<int> oth = others != null && i < others.Count ? others[i] : null;
            double bestS = double.MinValue; int bestP = m + ivs[0], bestIv = ivs[0];
            for (int k = 0; k < ivs.Length; k++)
            {
                int iv = ivs[k], c = m + iv;
                double s = NoteScore(Pc(c), rootMidi, semis, tonic, minor);
                if ((iv == 5 || iv == -5 || iv == 7 || iv == -7) && s >= TensionScore) s += 0.35;
                s -= 0.01 * k;                                     // the listed order breaks ties
                if (oth != null)
                    foreach (int o in oth)
                    {
                        if (o < 0) continue;
                        int d = Pc(c - o);
                        if (d == 1 || d == 11) s -= 0.8;
                        if (c == o) s -= 0.2;
                    }
                if (prev >= 0) { s -= 0.02 * Math.Abs(c - prev); if (iv == prevIv) s += 0.15; }
                if (c < lo || c > hi) s -= 2.0;
                if (s > bestS) { bestS = s; bestP = c; bestIv = iv; }
            }
            r[i] = bestP; prev = bestP; prevIv = bestIv;
        }
        return r;
    }

    /// <summary>
    /// v7 (SPEC v7 §17.3): a SEQUENCE — the same shape <paramref name="steps"/> scale steps higher (+) / lower (−) in the key (a note off the scale
    /// moves by the semitones its nearest scale note moved). −1 (a rest) stays. Pure.
    /// </summary>
    public static int[] StepShift(IList<int> src, int steps, int tonic, bool minor)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        var st = StepsOf(minor);
        for (int i = 0; i < n; i++)
        {
            int m = src[i];
            if (m < 0) { r[i] = -1; continue; }
            int d = Degree(tonic, minor, Pc(m)), off = 0;
            if (d < 0) { d = Degree(tonic, minor, Pc(m - 1)); off = 1; if (d < 0) { d = Degree(tonic, minor, Pc(m + 1)); off = -1; } }
            if (d < 0) { r[i] = m; continue; }
            int baseM = m - off, rel = baseM - tonic;
            int oct = (int)Math.Floor(rel / 12.0);
            int idx = d + steps, octMove = (int)Math.Floor(idx / 7.0);
            idx -= octMove * 7;
            r[i] = tonic + 12 * (oct + octMove) + st[idx] + off;
        }
        return r;
    }

    static bool InPool(IList<int> pool, int m) { if (pool == null) return true; for (int i = 0; i < pool.Count; i++) if (pool[i] == m) return true; return false; }

    /// <summary>
    /// A pattern moved onto the next chord by PEDAL (the flow): a note that is in <paramref name="pool"/> (null = any pitch) and fits the target chord
    /// (<see cref="NoteScore"/> ≥ <see cref="TensionScore"/>) stays; any other note moves to the nearest pool pitch that fits (ties: the smaller
    /// move, then down; nothing fits within an octave → the nearest pool pitch). −1 (a rest) stays. Pure.
    /// </summary>
    public static int[] AdaptPedal(IList<int> src, IList<float> w, int tgtRoot, IList<int> tgtSemis, int tonic, bool minor, IList<int> pool)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        for (int i = 0; i < n; i++)
        {
            int m = src[i];
            if (m < 0) { r[i] = -1; continue; }
            if (InPool(pool, m) && NoteScore(Pc(m), tgtRoot, tgtSemis, tonic, minor) >= TensionScore) { r[i] = m; continue; }
            int found = int.MinValue;
            for (int d = 1; d <= 12 && found == int.MinValue; d++)
            {
                if (InPool(pool, m - d) && NoteScore(Pc(m - d), tgtRoot, tgtSemis, tonic, minor) >= TensionScore) found = m - d;
                else if (InPool(pool, m + d) && NoteScore(Pc(m + d), tgtRoot, tgtSemis, tonic, minor) >= TensionScore) found = m + d;
            }
            if (found == int.MinValue && pool != null && pool.Count > 0)
            {
                int bd = int.MaxValue;
                foreach (int p in pool) { int d = Math.Abs(p - m); if (d < bd || (d == bd && p < found)) { bd = d; found = p; } }
            }
            r[i] = found == int.MinValue ? m : found;
        }
        return r;
    }

    static int Hash(int a, int b, int c) { unchecked { uint h = 2166136261u; h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u; h = (h ^ (uint)c) * 16777619u; h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15; return (int)(h & 0x7fffffff); } }

    /// <summary>
    /// The same pattern replayed WITH SMALL DIFFERENCES (the recording's repeated loops): pass 0 or <paramref name="amount"/> 0 → unchanged.
    /// amount ≥ 1 → deterministic by (<paramref name="seed"/>, <paramref name="pass"/>): one or two light notes change (never the first note;
    /// lighter / repeated notes first) — an octave flip (±12, in <paramref name="pool"/>) or a neighbour (the next pool pitch up / down that fits
    /// the chord); amount 2 on the LAST pass (<paramref name="passes"/> − 1) also answers (<see cref="Answer"/>). Pure.
    /// </summary>
    public static int[] Vary(IList<int> src, IList<float> w, int pass, int passes, int seed, int amount, int rootMidi, IList<int> semis, int tonic, bool minor, IList<int> pool)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = src[i];
        if (pass <= 0 || amount <= 0 || n < 2) return r;
        // light notes first: weight, then repeated notes, then later notes; a seeded shuffle among equals
        var cand = new List<int>();
        for (int i = 1; i < n; i++) if (src[i] >= 0) cand.Add(i);
        if (cand.Count == 0) return r;
        cand.Sort((a, b) =>
        {
            double wa = w != null && a < w.Count ? w[a] : 1.0, wb = w != null && b < w.Count ? w[b] : 1.0;
            if (src[a] == src[a - 1]) wa -= 0.5;
            if (src[b] == src[b - 1]) wb -= 0.5;
            int c = wa.CompareTo(wb);
            return c != 0 ? c : Hash(seed, pass, a).CompareTo(Hash(seed, pass, b));
        });
        int changes = Math.Min(cand.Count, 1 + Hash(seed, pass, 7) % 2);
        int pick = Hash(seed, pass, 11) % Math.Max(1, Math.Min(cand.Count, 3));   // start among the three lightest: passes differ
        int lowP = int.MaxValue, highP = int.MinValue;
        if (pool != null) foreach (int p in pool) { lowP = Math.Min(lowP, p); highP = Math.Max(highP, p); }
        for (int c = 0; c < changes; c++)
        {
            int i = cand[(pick + c) % cand.Count];
            int m = src[i];
            bool octave = Hash(seed, pass, 100 + i) % 2 == 0;
            int nv = int.MinValue;
            if (octave)
            {
                int up = m + 12, dn = m - 12;
                bool upOk = InPool(pool, up) && (pool != null || up <= 96), dnOk = InPool(pool, dn) && (pool != null || dn >= 36);
                double mid = pool != null && pool.Count > 0 ? (lowP + highP) * 0.5 : 66;
                if (upOk && dnOk) nv = Math.Abs(up - mid) <= Math.Abs(dn - mid) ? up : dn;
                else if (upOk) nv = up; else if (dnOk) nv = dn;
            }
            if (nv == int.MinValue)
            {
                bool upFirst = Hash(seed, pass, 200 + i) % 2 == 0;
                for (int d = 1; d <= 12 && nv == int.MinValue; d++)
                {
                    int a = upFirst ? m + d : m - d, b = upFirst ? m - d : m + d;
                    if (InPool(pool, a) && NoteScore(Pc(a), rootMidi, semis, tonic, minor) >= TensionScore) nv = a;
                    else if (InPool(pool, b) && NoteScore(Pc(b), rootMidi, semis, tonic, minor) >= TensionScore) nv = b;
                }
            }
            if (nv != int.MinValue) r[i] = nv;
        }
        if (amount >= 2 && pass == passes - 1) r = Answer(r, w, rootMidi, semis, pool);
        return r;
    }

    /// <summary>A phrase that ends at home: the last sounding note moves to the nearest root / 3rd of the chord in <paramref name="pool"/> (null =
    /// any pitch; ties → the root, then the lower). Pure.</summary>
    public static int[] Answer(IList<int> src, IList<float> w, int rootMidi, IList<int> semis, IList<int> pool)
    {
        int n = src != null ? src.Count : 0;
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = src[i];
        int last = -1;
        for (int i = n - 1; i >= 0; i--) if (src[i] >= 0) { last = i; break; }
        if (last < 0) return r;
        int m = src[last], rootPc = Pc(rootMidi), thirdPc = -1;
        if (semis != null) foreach (int s in semis) { int q = Pc(s); if (q == 3 || q == 4) { thirdPc = Pc(rootMidi + s); break; } }
        int best = m; long bestC = long.MaxValue;
        for (int c = m - 12; c <= m + 12; c++)
        {
            int pc = Pc(c);
            if (pc != rootPc && pc != thirdPc) continue;
            if (!InPool(pool, c)) continue;
            long cost = (long)Math.Abs(c - m) * 10 + (pc == rootPc ? 0 : 3) + (c < m ? 0 : 1);
            if (cost < bestC) { bestC = cost; best = c; }
        }
        r[last] = best;
        return r;
    }

#if !HARMONY_PURE
    // ------------------------------------------------------------------ Unity-facing helpers (read the scene)
    /// <summary>The job of an island's chord in the song key (0 for Moons, keyboards and stairs).</summary>
    public static int Job(KeyBlock kb)
    {
        if (kb == null || kb.IsMoon || kb.IsKeyboard || kb.IsStairs || kb.IsPhrase) return 0;
        var k = MusicTheory.KeyOfSong();
        return Job(k.tonic, k.minor, kb.chordRootMIDI, SemisOf(kb));
    }

    /// <summary>The job of a card's / measure's chord in the song key (0 for a Moon or a keyboard).</summary>
    public static int Job(SongManager.MeasureData md)
    {
        if (md == null || md.kind != 0) return 0;
        var k = MusicTheory.KeyOfSong();
        return Job(k.tonic, k.minor, md.chordRootMIDI, md.semitones ?? new[] { 0, 4, 7 });
    }

    /// <summary>The chord tones an island sounds (its stored chord; semitoneList when nothing is stored).</summary>
    public static IList<int> SemisOf(KeyBlock kb) => kb.storedSemis != null && kb.storedSemis.Length > 0 ? (IList<int>)kb.storedSemis : kb.semitoneList;

    /// <summary>The chord a pattern on <paramref name="kb"/> sits over: a chord island's own chord; a keyboard's (v7: a stairs island's) = its column's
    /// chord island (SongManager.ChordOfColumn), else the key's home chord. False for Moons / null.</summary>
    public static bool ChordOf(KeyBlock kb, out int rootMidi, out IList<int> semis)
    {
        rootMidi = 60; semis = null;
        if (kb == null || kb.IsMoon) return false;
        if (!kb.IsKeyboard && !kb.IsStairs && !kb.IsPhrase) { rootMidi = kb.chordRootMIDI; semis = kb.semitoneList; return true; }
        var sm = SongManager.I;
        // v9: a keyboard / stairs on a later measure of its column borrows the chord grid sounding on ITS measure (nearest lane), else the column's first
        var ch = sm != null ? (sm.ChordAt(kb.column, SongManager.OffsetBars(kb), kb.pz) ?? sm.ChordOfColumn(kb.column)) : null;
        if (ch != null) { rootMidi = ch.chordRootMIDI; semis = ch.semitoneList; return true; }
        var k = MusicTheory.KeyOfSong();
        var home = MusicTheory.Diatonic(k, 0);
        rootMidi = home.chordRootMIDI; semis = home.semitones;
        return true;
    }

    /// <summary>The island's tile pitches [x, z] as they sound (register included).</summary>
    public static int[,] MidiTable(KeyBlock kb)
    {
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return new int[0, 0];
        var t = new int[kb.cols, kb.rows];
        for (int x = 0; x < kb.cols; x++)
            for (int z = 0; z < kb.rows; z++) { var tile = kb.GetTile(x, z); t[x, z] = tile != null ? tile.midi : 60; }
        return t;
    }

    /// <summary>The notes a finished cube plays on its home island (tile pitches, register included), weighted by <see cref="WeightsOf"/>.
    /// Drum cubes add nothing. Appends to the lists; returns the count added.</summary>
    public static int NotesOf(AudioCube c, List<int> midi, List<float> w)
    {
        if (c == null || c.IsDrums || c.nodes.Count == 0) return 0;
        var ws = WeightsOf(c.durs.Count == c.nodes.Count ? c.durs : null, c.nodes.Count);
        int added = 0;
        for (int i = 0; i < c.nodes.Count; i++)
        {
            var t = c.nodes[i]; if (t == null) continue;
            bool rest = i < c.mods.Count && c.mods[i] == 1;
            if (rest) continue;
            midi.Add(t.midi + 12 * Mathf.Clamp(c.octave, -1, 1));
            w.Add(ws[i] * (i < c.mods.Count && c.mods[i] == 2 ? 0.5f : 1f));
            added++;
        }
        return added;
    }

    /// <summary>All pitched notes of the finished cubes living on <paramref name="kb"/> (its residents). Returns the count.</summary>
    public static int NotesOfIsland(KeyBlock kb, List<int> midi, List<float> w)
    {
        if (kb == null) return 0;
        int n = 0;
        foreach (var c in SequenceMaster.Cubes)
            if (c != null && c.isFinalized && !c.IsOnMoon && c.Island == kb) n += NotesOf(c, midi, w);
        return n;
    }

    /// <summary>Fit of notes over a card's chord in the song key (the star rule: ≥ <see cref="StarThreshold"/>).</summary>
    public static float Fit(IList<int> midi, IList<float> w, SongManager.MeasureData md)
    {
        if (md == null) return 0f;
        var k = MusicTheory.KeyOfSong();
        return Fit(midi, w, md.chordRootMIDI, md.semitones ?? new[] { 0, 4, 7 }, k.tonic, k.minor);
    }
    /// <summary>v7: the chord after <paramref name="kb"/>'s column — the next column's chord (at the last column the first column's), else the key's
    /// home chord. False for null.</summary>
    public static bool ChordAfter(KeyBlock kb, out int rootMidi, out IList<int> semis)
    {
        rootMidi = 60; semis = null;
        if (kb == null) return false;
        var sm = SongManager.I;
        if (sm != null && sm.ColumnCount > 0)
        {
            // v9: the chord sounding right after the island's own measures (the column's grid there, else the next column's first: the lead-in's target)
            var ch = sm.ChordAfterIsland(kb);
            if (ch != null) { rootMidi = ch.chordRootMIDI; semis = ch.semitoneList; return true; }
        }
        var k = MusicTheory.KeyOfSong();
        var home = MusicTheory.Diatonic(k, 0);
        rootMidi = home.chordRootMIDI; semis = home.semitones;
        return true;
    }

    /// <summary>v7: the island's playable pitches (its tiles as they sound, register included), sorted, distinct.</summary>
    public static List<int> Pool(KeyBlock kb)
    {
        var r = new List<int>();
        if (kb == null || kb.cols <= 0 || kb.rows <= 0) return r;
        for (int x = 0; x < kb.cols; x++)
            for (int z = 0; z < kb.rows; z++) { var t = kb.GetTile(x, z); if (t != null && !r.Contains(t.midi)) r.Add(t.midi); }
        r.Sort();
        return r;
    }
#endif
}
