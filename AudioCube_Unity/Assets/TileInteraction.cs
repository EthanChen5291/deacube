using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A pitch tile. Handles its own hover lift, press sink, note flash and (in drum clothes) its kit pictogram.
/// v5 (R, "the shapes are very ambiguous"): chord tiles wear NO glyph — the v3 role markers (root disc, third triangle, fifth square,
/// seventh star, extension ring) meant nothing to a non-musician. A tile's pitch reads as its HEIGHT (PitchLook.Rise per semitone: a
/// staircase) and its BRIGHTNESS (PitchLook.TileColor: low = deeper, high = lighter), and as its sound: hovering it auditions it.
/// While the Drums role is armed or a drum cube is selected on the island (Moons always), the tiles wear "drum clothes": the pictogram of
/// the kit piece they play (<see cref="KitGlyphs"/>: kick drum, snare, closed / open hi-hat, clap, ride …) and a 25 % darker face.
/// Auditions and previews always sound (v4 silenced the tiles of an island that was not lit while playing): they play on the synth's
/// preview bus (owner 9) and the song ducks under them (SynthEngine).
/// v6 (H, "whenever theyre pressing the keys, it should be louder than whatever else is playing so they can actually hear it"): a PRESSED tile
/// (an empty-hand click, a key clicked into a keyboard draft, a click preview) plays through Synth.PressNote with a strong velocity (the song ducks
/// deeper under a press than under a hover) and answers like a key: it sinks, springs back past its rest with a small overshoot, a ring spreads
/// from it (<see cref="PlayPress"/>). Sounds take the group's voice (Instruments.SlotOf(group, voice); -1 = the brush voice).
/// v7 (D; B: "a phrase can have cols x 15 tiles — 8 measures of 16ths = 1920"): a tile AT REST switches its Update off (hover, press, flash,
/// highlight and the key spring settled and applied) and any of them wakes it, so thousands of idle cells cost nothing per frame. Stairs steps
/// and phrase cells keep their own look (no drum clothes), like keyboard keys.
/// </summary>
public class TileInteraction : MonoBehaviour
{
    public enum Highlight { None, Candidate, Path, Start }

    public KeyBlock island;
    public int gridX, gridZ;
    public int midi;
    public ChordRole role;
    public int parentMeasureIndex;
    public float myFrequency;

    // legacy fields
    public float pressDepth = 0.13f;
    public float pressSpeed = 20f;
    public float returnSpeed = 8f;
    public AudioClip previewClip;
    public AudioSource audioSource;

    Renderer rend; MaterialPropertyBlock mpb;
    Color baseColor = Color.white; Color roleBase = Color.white; Color flashColor = Color.white;
    Color restTint = Color.white; float restTintAmt;   // v8: a keyboard's chord-root wash
    float flash, hover, hoverTarget, press, hl, hlTarget;
    float keyT = 9f;   // v6: seconds since the last press look (the key spring)
    Highlight highlight;
    Vector3 restLocal;
    Renderer marker; Material markerMat; Color markerColor;
    bool kitLook, kitApplied;
    /// <summary>v5: the kit pictogram's size on the tile face (fraction of the tile) and its ink.</summary>
    public const float KitGlyphSize = 0.6f;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>World position of the centre of the top face at rest (ignores hover/press animation).</summary>
    public Vector3 Top => transform.parent != null
        ? transform.parent.TransformPoint(restLocal + Vector3.up * (ProjectConfig.TileThickness * 0.5f))
        : transform.position + Vector3.up * (ProjectConfig.TileThickness * 0.5f);

    public float GetPitch() => Mathf.Pow(2f, (midi + SongManager.Transpose - ProjectConfig.refMIDI) / 12f);

    // ---- v3 (SPEC v3 §2.4): what the flat path grid draws for this tile
    /// <summary>The face colour at rest (pitch brightness included; 25 % darker while the tile wears drum clothes).</summary>
    public Color BaseColor => baseColor;
    /// <summary>The glyph on the tile's face: null on a chord tile (v5: pitch is height and brightness, nothing is printed), the pictogram
    /// of the kit piece this tile plays while it wears drum clothes.</summary>
    public string MarkerIcon => kitLook ? KitGlyphFor(VoiceRules.DrumPieceOf(this)) : null;   // v6: a keyboard key shows the piece it plays (A)

    public void Setup(KeyBlock island, int x, int z, int midi, Color color)
    {
        this.island = island; gridX = x; gridZ = z; this.midi = midi;
        parentMeasureIndex = island.measureIndex;
        myFrequency = ProjectConfig.refFreq * GetPitch();
        role = MusicTheory.RoleOf(midi - island.chordRootMIDI);   // kept for the voice rules (the bass anchor); no longer drawn
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        baseColor = color; roleBase = color;
        restLocal = transform.localPosition;
        EnsureHooks();
        kitApplied = false;
        SetKitLook(ShouldKitLook(island));   // ends in Apply()
    }

    void CreateMarker(string icon, float size, Color color)
    {
        var go = new GameObject("Marker");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * (ProjectConfig.TileThickness * 0.5f + 0.012f);
        go.transform.localScale = new Vector3(size, 1f, size);
        go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.FlatQuad();
        marker = go.AddComponent<MeshRenderer>();
        markerColor = color;
        markerMat = Fx.Alpha(IconFactory.GetTexture(icon), markerColor);
        marker.sharedMaterial = markerMat;
        marker.shadowCastingMode = ShadowCastingMode.Off; marker.receiveShadows = false;
    }

    public void SetHighlight(Highlight h) { if (h == highlight) return; highlight = h; Wake(); }
    public void SetHover(bool on) { float t = on ? 1f : 0f; if (t == hoverTarget) return; hoverTarget = t; Wake(); }
    public void Press(float amount = 1f) { press = Mathf.Max(press, amount); Wake(); }
    /// <summary>v8: a resting wash of <paramref name="c"/> over the face (<paramref name="amount"/> 0 = none): a keyboard's chord-root keys.</summary>
    public void SetRestTint(Color c, float amount)
    {
        if (Mathf.Approximately(amount, restTintAmt) && (amount <= 0f || c == restTint)) return;
        restTint = c; restTintAmt = amount; Apply();
    }
    public void Flash(Color c, float strength = 1f) { flashColor = c; flash = Mathf.Max(flash, strength); Wake(); }

    public void ResetTile() { press = 0f; flash = 0f; hover = 0f; hoverTarget = 0f; hl = 0f; hlTarget = 0f; keyT = 9f; highlight = Highlight.None; transform.localPosition = restLocal; Apply(); }

    /// <summary>v7: true while the tile animates (its Update runs); a tile at rest sleeps until hover / press / flash / highlight wake it (tests).</summary>
    public bool Animating => enabled;
    /// <summary>v7: Updates that found their tile settled and put it to sleep (tests / perf).</summary>
    public static int Sleeps;
    /// <summary>v7 (K's live keyboard range / any live re-lay): the tile's rest pose moves to <paramref name="local"/> (its parent's space); Top and the
    /// hover / press animation follow from there.</summary>
    public void RestAt(Vector3 local) { restLocal = local; transform.localPosition = local; Wake(); }

    /// <summary>v7: wakes the per-frame animation — never the tiles of a sinking husk (SongManager disables them with their island).</summary>
    void Wake() { if (!enabled && (island == null || island.enabled)) enabled = true; }

    void OnDestroy() { if (markerMat != null) Destroy(markerMat); }   // one Fx.Alpha material per tile: freed with the tile, not by Unity

    void Update()
    {
        float dt = Time.deltaTime;
        hover = Mathf.Lerp(hover, hoverTarget, 1f - Mathf.Exp(-dt * 14f));
        press = Mathf.MoveTowards(press, 0f, dt * 5.5f);
        flash *= Mathf.Exp(-dt * 7f);
        hlTarget = highlight == Highlight.None ? 0f : 1f;
        hl = Mathf.Lerp(hl, hlTarget, 1f - Mathf.Exp(-dt * 16f));
        if (keyT < KeySpringSeconds) keyT += dt;
        // v7: settled (within a hair of the targets, no press, no key spring) -> snap to the targets, apply once more and sleep
        bool settled = Mathf.Abs(hover - hoverTarget) < 2e-3f && press <= 0f && flash < 2e-3f && Mathf.Abs(hl - hlTarget) < 2e-3f && keyT >= KeySpringSeconds;
        if (settled) { hover = hoverTarget; flash = 0f; hl = hlTarget; }
        float sink = pressDepth * Ease.InQuad(press) + KeySink;   // v6: + the key spring (sink, spring back, a small overshoot)
        // v8 ("when hovering over keys the piano notes should go down not up"): a keyboard's key dips under the pointer like a finger resting on it
        float hoverY = island != null && island.IsKeyboard ? -hover * KeyHoverDip : hover * 0.09f;
        transform.localPosition = restLocal + Vector3.up * (hoverY - sink);
        Apply();
        if (settled) { enabled = false; Sleeps++; }
    }

    // ------------------------------------------------------------------ v6 (H): the press
    /// <summary>Depth (world units) of the press sink and the length (s) of the whole press spring.</summary>
    public const float KeyDepth = 0.16f, KeySpringSeconds = 0.55f;
    /// <summary>v8: how far a keyboard key dips under the pointer (a hover; a grid tile lifts instead).</summary>
    public const float KeyHoverDip = 0.05f;
    /// <summary>Presses sent to the synth so far and the last one (tests; owner = the preview owner).</summary>
    public static int PressCount;
    public static VoiceRules.NoteEvent LastPress;
    /// <summary>Tests: every press sent to the synth (the tile and its note).</summary>
    public static System.Action<TileInteraction, VoiceRules.NoteEvent> OnPressed;
    /// <summary>The press spring's displacement now (world units; + = down, − = the overshoot above the rest).</summary>
    public float KeySink => KeyCurve(keyT) * KeyDepth;
    /// <summary>A piano key of a keyboard island whose pitch is a black key (C#, D#, F#, G#, A#).</summary>
    public bool IsBlackKey { get { if (island == null || !island.IsKeyboard) return false; int pc = ((midi % 12) + 12) % 12; return pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10; } }

    /// <summary>The key spring over time (1 = fully sunk): down in 35 ms, held 70 ms, then a damped spring back through the rest with an overshoot
    /// of about a quarter of the depth above it, settled by <see cref="KeySpringSeconds"/>.</summary>
    public static float KeyCurve(float t)
    {
        if (t < 0f || t >= KeySpringSeconds) return 0f;
        if (t < 0.035f) return t / 0.035f;
        if (t < 0.105f) return 1f;
        float u = t - 0.105f;
        return Mathf.Exp(-u * 11f) * Mathf.Cos(u * 24f);
    }

    /// <summary>The press look: the key sinks and springs back, a flash in <paramref name="c"/> and a ring spreading from the tile.</summary>
    public void PressLook(Color c)
    {
        keyT = 0f; Wake();
        Flash(c, 1f);
        Fx.Ripple(Top, c, island != null && island.IsKeyboard ? 0.6f : 0.85f, 0.38f);
    }

    /// <summary>
    /// v6: plays this tile as a PRESS with group <paramref name="instrument"/> (sound <paramref name="voice"/>; -1 = the group's brush voice) at
    /// <paramref name="octave"/>: A's VoiceRules.PressEvent (the tile's pitch through the fold a cube would use — a keyboard key keeps its octave;
    /// drums: the kit piece — at the press velocity) sent with VoiceRules.Press (Synth.PressNote: the preview bus, the song ducked deep under it),
    /// sounding <paramref name="seconds"/>. At once (<paramref name="atDsp"/> 0) or at a DSP time. Shows the press look. Returns the onset (0 = at
    /// once); -1 without the synth (the fallback sample played).
    /// </summary>
    /// v7 (A): <paramref name="layer"/> = the octave layer of the cube this note belongs to, <paramref name="offset"/> = semitones on the tile (a bend);
    /// the defaults leave the note as before.
    public double PlayPress(int instrument, int voice = -1, int octave = 0, double seconds = 0.45, double atDsp = 0.0, int layer = 0, int offset = 0)
    {
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        PressLook(Instruments.Colors[instrument]);
        if (!Synth.Ready)
        {
            if (AudioPool.I != null) AudioPool.I.Play(Instruments.ClipOf(instrument), GetPitch(), 0.9f * Mathf.Max(0.15f, Instruments.Volume[instrument]), Instruments.GroupOf(instrument));
            return -1.0;
        }
        var e = PressEvent(instrument, voice, octave, seconds, atDsp, layer, offset);
        VoiceRules.Press(e);
        e.owner = VoiceRules.OwnerPreview;
        PressCount++; LastPress = e;
        OnPressed?.Invoke(this, e);
        OnSounded?.Invoke(this, e.onDsp);
        return e.onDsp;
    }

    /// <summary>The note a press of this tile plays (see <see cref="PlayPress"/>).</summary>
    public VoiceRules.NoteEvent PressEvent(int instrument, int voice, int octave, double seconds, double atDsp = 0.0, int layer = 0, int offset = 0)
    {
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        int slot = Instruments.SlotOf(instrument, voice >= 0 ? voice : Instruments.BrushVoiceOf(instrument));
        return VoiceRules.PressEvent(slot, this, octave, seconds, atDsp, layer, offset);   // v7 (A): layer + bend
    }

    void Apply()
    {
        if (rend == null) return;
        Color c = restTintAmt > 0f ? Color.Lerp(baseColor, restTint, restTintAmt) : baseColor;
        Color em = Color.black;
        Color hlColor = highlight == Highlight.Start ? Palette.Ok : Palette.Accent;
        float hlAmt = (highlight == Highlight.Candidate ? 0.32f : (highlight == Highlight.None ? 0f : 0.65f)) * hl;
        em += hlColor * hlAmt;
        c = Color.Lerp(c, hlColor, hlAmt * 0.35f);
        em += baseColor * hover * 0.4f;
        em += flashColor * flash * 2.4f;
        c = Color.Lerp(c, Color.white, flash * 0.55f);
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(EmissionId, em);
        rend.SetPropertyBlock(mpb);
        if (markerMat != null)
        {
            Color mc = markerColor;
            mc = Color.Lerp(mc, Palette.A(Color.white, 0.9f), Mathf.Clamp01(flash * 0.8f + hover * 0.35f + hlAmt * 0.5f));
            markerMat.SetColor(ColorId, mc);
        }
    }

    // ------------------------------------------------------------------ drum clothes (SPEC §3.1 #14, §8.3; v5 pictograms)
    /// <summary>True while the tile wears the kit look.</summary>
    public bool KitLook => kitLook;

    /// <summary>The kit pictogram of a row (column 0's piece: SynthBank.DrumForRow): kick drum, snare, closed hi-hat, open hi-hat, clap …</summary>
    public static string KitGlyph(int row, int rows) => KitGlyphFor(SynthBank.DrumForRow(row, rows));

    /// <summary>v5: the pictogram of the piece tile (<paramref name="gx"/>, <paramref name="gz"/>) really plays (VoiceRules.DrumPiece: column 5
    /// swaps rows 1-3 for clap / open hat / ride).</summary>
    public static string KitGlyph(int gx, int gz, int rows) => KitGlyphFor(VoiceRules.DrumPiece(gx, gz, rows));

    /// <summary>v5: the pictogram of a GM drum key (<see cref="KitGlyphs"/>); a generic drum for keys the kit does not use.</summary>
    public static string KitGlyphFor(int drumKey)
    {
        switch (drumKey)
        {
            case 36: return KitGlyphs.Kick;
            case 38: return KitGlyphs.Snare;
            case 40: return KitGlyphs.Snare;       // v9 (G): the electric snare
            case 42: return KitGlyphs.HatClosed;
            case 44: return KitGlyphs.HatClosed;   // v9 (G): the pedal hat
            case 46: return KitGlyphs.HatOpen;
            case 39: return KitGlyphs.Clap;
            case 37: return KitGlyphs.Rim;
            case 45: return KitGlyphs.TomLow;
            case 48: return KitGlyphs.TomHigh;
            case 49: return KitGlyphs.Crash;
            case 51: return KitGlyphs.Ride;
            case 54: return KitGlyphs.Tambourine;
            case 56: return KitGlyphs.Cowbell;
            case 75: return KitGlyphs.Claves;
            case 80: return KitGlyphs.Triangle;
            default: return KitGlyphs.Drum;
        }
    }

    /// <summary>v5: the pictogram's colour on a face of <paramref name="face"/>: ink on a light face, cream on a dark one (always legible).</summary>
    public static Color KitInk(Color face)
    {
        float h, s, v; Color.RGBToHSV(face, out h, out s, out v);
        return v < 0.5f ? Palette.A(Color.Lerp(face, Comic.Cream, 0.78f), 0.92f) : Palette.A(Color.Lerp(face, Look.InkColor, 0.74f), 0.9f);
    }

    /// <summary>Drum clothes on/off: the pictogram of the tile's kit piece on a 25 % darker face; off restores the chord look (no glyph).</summary>
    public void SetKitLook(bool on)
    {
        if (on == kitLook && kitApplied) return;
        kitLook = on; kitApplied = true;
        if (on)
        {
            string glyph = KitGlyphFor(VoiceRules.DrumPieceOf(this));   // v6: DrumPiece on grids and Moons, the key's piece on a keyboard
            Color c = KitInk(Palette.Mul(roleBase, 0.75f));
            if (marker == null) CreateMarker(glyph, KitGlyphSize, c);
            else { markerMat.mainTexture = IconFactory.GetTexture(glyph); marker.transform.localScale = new Vector3(KitGlyphSize, 1f, KitGlyphSize); markerColor = c; }
            marker.enabled = true;
            baseColor = Palette.Mul(roleBase, 0.75f);
        }
        else
        {
            baseColor = roleBase;
            if (marker != null) marker.enabled = false;
        }
        Apply();
    }

    static bool hooked;
    static void EnsureHooks()
    {
        if (hooked) return;
        hooked = true;
        PathManager.OnInstrumentChanged += OnInstrumentChangedHook;
        PathManager.OnSelectionChanged += OnSelectionChangedHook;
    }
    static void OnInstrumentChangedHook(int instrument) { RefreshKitLook(); }
    static void OnSelectionChangedHook(AudioCube cube) { RefreshKitLook(); }

    /// <summary>An island wears drum clothes when it is a Moon, when the Drums role is armed in the palette, or when a drum cube is selected on it.</summary>
    public static bool ShouldKitLook(KeyBlock island)
    {
        if (island == null) return false;
        if (island.kind == 1) return true;
        if (island.IsKeyboard) return false;   // v6 (K): a keyboard keeps its piano look (a drum voice on it still plays the key's kit piece)
        if (island.IsStairs || island.IsPhrase) return false;   // v7 (B): stairs steps and phrase cells keep their own look
        var pm = PathManager.I;
        if (pm == null) return false;
        if (Instruments.IsDrums(pm.selectedInstrument)) return true;
        var sel = pm.selectedCube;
        return sel != null && sel.IsDrums && sel.Island == island;
    }

    /// <summary>Re-evaluates the kit look of every tile (selection / palette / cube changes call this).</summary>
    public static void RefreshKitLook()
    {
        var sm = SongManager.I;
        if (sm == null) return;
        foreach (var kb in sm.Islands)
        {
            if (kb == null) continue;
            bool on = ShouldKitLook(kb);
            foreach (var t in kb.tiles) if (t != null) t.SetKitLook(on);
        }
        foreach (var kb in sm.Moons) { if (kb == null) continue; foreach (var t in kb.tiles) if (t != null) t.SetKitLook(true); }
    }

    static readonly VoiceRules.NoteEvent[] previewEv = new VoiceRules.NoteEvent[1];

    /// <summary>v5: the loudness of a hover audition (0..1 of the slot's velocity range; a click previews at 0.85): soft but clearly heard —
    /// while the song plays it ducks under the audition.</summary>
    public const float HoverVolume = 0.7f;
    /// <summary>v5: the spacing (s) of the notes of a run audition (the quick arpeggio of what a click adds while drafting).</summary>
    public const float RunGap = 0.08f;
    /// <summary>Tests: every note an audition / preview of a tile sent to the synth (tile, onset DSP time; 0 = at once), in order.</summary>
    public static System.Action<TileInteraction, double> OnSounded;

    /// <summary>
    /// v5: schedules this tile's note on the synth's preview bus (owner 9: always heard, the song ducks under it) with
    /// <paramref name="instrument"/> (drums play the tile's kit piece) and <paramref name="octave"/>, sounding <paramref name="seconds"/> at
    /// <paramref name="volume01"/>: at <paramref name="atDsp"/> when > 0, else at once when stopped and on the next 16th while playing.
    /// Returns the onset DSP time (0 = at once); -1 when the synth is not ready.
    /// </summary>
    public double Sound(int instrument, int octave, double seconds, float volume01, double atDsp = 0.0, int voice = -1, int layer = 0, int offset = 0)
    {
        if (!Synth.Ready) return -1.0;
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        int slot = Instruments.SlotOf(instrument, voice >= 0 ? voice : Instruments.BrushVoiceOf(instrument));   // v6: the group's sound
        var e = VoiceRules.AuditionEvent(slot, this, octave, seconds, volume01, atDsp, layer, offset);   // v7 (A): layer + bend
        previewEv[0] = e;
        VoiceRules.Dispatch(previewEv, 1, VoiceRules.OwnerPreview);
        OnSounded?.Invoke(this, e.onDsp);
        return e.onDsp;
    }

    /// <summary>
    /// Audition this tile with an instrument (SPEC §4.6; a click / an added node): immediately when stopped, on the next 16th while playing
    /// (v5: whatever island is lit — the song ducks under it). <paramref name="octave"/> is the selected cube's octave (what the cube would play).
    /// v6: a click is a PRESS (<see cref="PlayPress"/>: loud, the key spring and ring; same timing as before), with the group's
    /// <paramref name="voice"/> (-1 = the brush voice).
    /// </summary>
    public void PlayPreview(int instrument = -1, int octave = 0, int voice = -1, int layer = 0, int offset = 0)
    {
        if (instrument < 0) instrument = PathManager.I != null ? PathManager.I.selectedInstrument : 0;
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        Color c = Instruments.Colors[instrument];
        double at = 0.0;
        if (GlobalClock.IsPlaying && Synth.Ready) { double end; VoiceRules.PreviewBeat(out at, out end); }
        PlayPress(instrument, voice, octave, 0.35, at, layer, offset);
        Fx.Pillar(Top, c, 1.2f, 0.4f, 0.45f);
    }

    /// <summary>
    /// SPEC v4 R3 / v5: plays this tile's note once for <paramref name="seconds"/> with a soft flash and no pillar: the hover audition and the
    /// note-length audition. The preview bus (owner 9): immediate when stopped, the next 16th while playing, always heard (v5: the song ducks
    /// under it — v4 only flashed the tiles of an island that was not lit). False only when the synth is missing (a sample played instead).
    /// </summary>
    public bool Audition(int instrument, int octave, float seconds, float flashAmount = 0.4f) => Audition(instrument, octave, seconds, flashAmount, 0.85f);

    /// <summary><see cref="Audition(int, int, float, float)"/> at <paramref name="volume01"/> (hover auditions use <see cref="HoverVolume"/>).</summary>
    public bool Audition(int instrument, int octave, float seconds, float flashAmount, float volume01)
    {
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        Color c = Instruments.Colors[instrument];
        bool sounded = Synth.Ready;
        if (sounded) Sound(instrument, octave, Mathf.Clamp(seconds, 0.05f, 3f), volume01);
        else AudioPool.I.Play(Instruments.ClipOf(instrument), GetPitch(), 0.6f * Mathf.Max(0.15f, Instruments.Volume[instrument]), Instruments.GroupOf(instrument));
        Flash(c, flashAmount);
        Press(0.2f);
        return sounded;
    }

    /// <summary>
    /// v5 (R): auditions a RUN — the tiles a click adds while drafting, in order — as a quick arpeggio: <see cref="RunGap"/> apart, the others
    /// short and a little softer, the last (the target) held <paramref name="targetSeconds"/>; from now (+30 ms) when stopped, from the next 16th
    /// while playing. A run of one tile is a single note. <paramref name="onsets"/> receives each note's onset DSP time (in run order).
    /// </summary>
    public static void AuditionRun(IList<TileInteraction> run, int instrument, int octave, float targetSeconds, List<double> onsets = null, int voice = -1)
    {
        if (onsets != null) onsets.Clear();
        if (run == null || run.Count == 0) return;
        instrument = Mathf.Clamp(instrument, 0, Instruments.Count - 1);
        int n = run.Count;
        if (n == 1 || !Synth.Ready)
        {
            var t1 = run[n - 1];
            if (t1 == null) return;
            double on = Synth.Ready ? t1.Sound(instrument, octave, Mathf.Clamp(targetSeconds, 0.05f, 3f), HoverVolume, 0.0, voice) : 0.0;
            if (!Synth.Ready) t1.Audition(instrument, octave, targetSeconds, 0.4f, HoverVolume);
            if (onsets != null) onsets.Add(on);
            return;
        }
        double t0;
        if (GlobalClock.IsPlaying) { double end; VoiceRules.PreviewBeat(out t0, out end); }
        else t0 = GlobalClock.DspNow + 0.03;
        for (int i = 0; i < n; i++)
        {
            var t = run[i];
            if (t == null) continue;
            bool last = i == n - 1;
            double at = t0 + i * (double)RunGap;
            double on = t.Sound(instrument, octave, last ? Mathf.Clamp(targetSeconds, 0.05f, 3f) : RunGap * 1.6, last ? HoverVolume : HoverVolume * 0.82f, at, voice);
            if (onsets != null) onsets.Add(on);
        }
    }

    // ---- legacy API
    public void ResetColor() { }
    public void SetColor(Color newCol) { }
    public void SetNote(float frequency) { myFrequency = frequency; }
    public void PlayNote() { PlayPreview(); }
    public void PlayPreviewNote() { PlayPreview(); }
}

// ==== KitGlyphs begin
/// <summary>
/// v5 (R): the drum pictograms the tiles wear in drum clothes (Moons always) and the inspector grid draws — pictures of the kit pieces
/// instead of v4's dot / square / sparkle / ring / diamond: kick drum (front view on its spurs), snare (with crossed sticks), closed and
/// open hi-hat (the two cymbals together / apart on the stand), clap (two hands), rim (a stick across a rim), low / high tom, crash,
/// ride, tambourine, cowbell, claves, triangle. Registered through IconFactory.Register before the scene loads.
/// </summary>
public static class KitGlyphs
{
    public const string Kick = "kitKick", Snare = "kitSnare", HatClosed = "kitHatClosed", HatOpen = "kitHatOpen", Clap = "kitClap", Rim = "kitRim",
        TomLow = "kitTomLow", TomHigh = "kitTomHigh", Crash = "kitCrash", Ride = "kitRide", Tambourine = "kitTambourine", Cowbell = "kitCowbell",
        Claves = "kitClaves", Triangle = "kitTriangle", Drum = "kitDrum";
    /// <summary>Every pictogram name (the presentation's glyph atlas and the tests list them).</summary>
    public static readonly string[] Names = { Kick, Snare, HatClosed, HatOpen, Clap, Rim, TomLow, TomHigh, Crash, Ride, Tambourine, Cowbell, Claves, Triangle, Drum };

    // an ellipse (approximate distance: fine for the rasterizer's anti-aliasing), its outline, a rotation about a point
    static float Ell(Vector2 p, float cx, float cy, float rx, float ry) { var q = new Vector2((p.x - cx) / rx, (p.y - cy) / ry); return (q.magnitude - 1f) * Mathf.Min(rx, ry); }
    static float EllRing(Vector2 p, float cx, float cy, float rx, float ry, float w) => Mathf.Abs(Ell(p, cx, cy, rx, ry)) - w;
    static Vector2 Rot(Vector2 p, float cx, float cy, float deg)
    {
        float c = Mathf.Cos(-deg * Mathf.Deg2Rad), s = Mathf.Sin(-deg * Mathf.Deg2Rad), x = p.x - cx, y = p.y - cy;
        return new Vector2(cx + x * c - y * s, cy + x * s + y * c);
    }
    static float U(params float[] v) => IconFactory.P.U(v);
    static float Sub(float a, float b) => IconFactory.P.Sub(a, b);
    static float Seg(Vector2 p, float ax, float ay, float bx, float by, float w) => IconFactory.P.Seg(p, ax, ay, bx, by, w);
    static float Circle(Vector2 p, float cx, float cy, float r) => IconFactory.P.Circle(p, cx, cy, r);
    static float Box(Vector2 p, float cx, float cy, float hx, float hy, float r = 0f) => IconFactory.P.Box(p, cx, cy, hx, hy, r);

    /// <summary>A drum seen from the side: an open head (ellipse outline) on a solid shell with a rounded bottom.</summary>
    static float SideDrum(Vector2 p, float cx, float top, float rx, float ry, float depth, float w)
    {
        float head = EllRing(p, cx, top, rx, ry, w);
        float shell = U(Box(p, cx, top - depth * 0.5f, rx, depth * 0.5f), Ell(p, cx, top - depth, rx, ry));
        return U(head, Sub(shell, Ell(p, cx, top, rx - w * 0.5f, ry - w * 0.5f) - w * 1.6f));
    }
    /// <summary>A cymbal (a thin lens) with its bell on top.</summary>
    static float Cymbal(Vector2 p, float cy, float rx, float ry, float bell) => U(Ell(p, 0f, cy, rx, ry), Circle(p, 0f, cy + ry * 0.6f, bell));
    /// <summary>The cymbal stand: a pole and a tripod's two visible feet.</summary>
    static float Stand(Vector2 p, float fromY) => U(Seg(p, 0f, fromY, 0f, -0.76f, 0.055f), Seg(p, 0f, -0.76f, -0.44f, -0.95f, 0.055f), Seg(p, 0f, -0.76f, 0.44f, -0.95f, 0.055f));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Register()
    {
        // kick: the big round front head on its two spurs, the port hole in the middle
        IconFactory.Register(Kick, p => U(IconFactory.P.Ring(p, 0f, 0.1f, 0.66f, 0.12f), Circle(p, 0f, 0.1f, 0.2f),
            Seg(p, -0.4f, -0.42f, -0.7f, -0.9f, 0.075f), Seg(p, 0.4f, -0.42f, 0.7f, -0.9f, 0.075f)));
        // snare: a shallow drum with two crossed sticks above it
        IconFactory.Register(Snare, p => U(SideDrum(p, 0f, -0.08f, 0.74f, 0.2f, 0.46f, 0.075f),
            Seg(p, -0.64f, 0.9f, 0.2f, 0.24f, 0.058f), Seg(p, 0.64f, 0.9f, -0.2f, 0.24f, 0.058f), Circle(p, 0.2f, 0.24f, 0.085f), Circle(p, -0.2f, 0.24f, 0.085f)));
        // closed hi-hat: the two cymbals pressed together on the stand (one lens)
        IconFactory.Register(HatClosed, p => U(Cymbal(p, 0.26f, 0.82f, 0.1f, 0.12f), Ell(p, 0f, 0.1f, 0.82f, 0.1f), Seg(p, 0f, 0.3f, 0f, 0.62f, 0.045f), Stand(p, 0.1f)));
        // open hi-hat: the two cymbals apart, ringing
        IconFactory.Register(HatOpen, p => U(Cymbal(p, 0.46f, 0.82f, 0.09f, 0.11f), Ell(p, 0f, 0.0f, 0.82f, 0.09f), Seg(p, 0f, 0.5f, 0f, 0.8f, 0.045f), Stand(p, 0f),
            Seg(p, 0.9f, 0.16f, 0.98f, 0.3f, 0.04f), Seg(p, -0.9f, 0.16f, -0.98f, 0.3f, 0.04f)));
        // clap: two palms meeting at the top, three impact lines above
        IconFactory.Register(Clap, p =>
        {
            var l = Rot(p, -0.3f, -0.1f, -22f); var r = Rot(p, 0.3f, -0.1f, 22f);
            return U(Box(l, -0.3f, -0.1f, 0.2f, 0.46f, 0.19f), Box(r, 0.3f, -0.1f, 0.2f, 0.46f, 0.19f),
                Seg(p, 0f, 0.62f, 0f, 0.92f, 0.05f), Seg(p, -0.3f, 0.56f, -0.5f, 0.8f, 0.05f), Seg(p, 0.3f, 0.56f, 0.5f, 0.8f, 0.05f));
        });
        // rim: one stick lying across a small drum's rim
        IconFactory.Register(Rim, p => U(SideDrum(p, 0f, -0.02f, 0.66f, 0.18f, 0.44f, 0.07f), Seg(p, -0.86f, 0.02f, 0.62f, 0.62f, 0.06f), Circle(p, 0.62f, 0.62f, 0.085f)));
        // low tom: a deep floor tom on legs
        IconFactory.Register(TomLow, p => U(SideDrum(p, 0f, 0.42f, 0.64f, 0.18f, 0.84f, 0.07f), Seg(p, -0.52f, -0.4f, -0.7f, -0.95f, 0.06f), Seg(p, 0.52f, -0.4f, 0.7f, -0.95f, 0.06f)));
        // high tom: a small deep drum on a mount
        IconFactory.Register(TomHigh, p => U(SideDrum(p, 0f, 0.5f, 0.46f, 0.14f, 0.62f, 0.065f), Seg(p, 0f, -0.2f, 0f, -0.9f, 0.06f), Seg(p, -0.3f, -0.9f, 0.3f, -0.9f, 0.06f)));
        // crash: a tilted cymbal on its stand with a burst at its edge
        IconFactory.Register(Crash, p =>
        {
            var q = Rot(p, 0f, 0.32f, -16f);
            return U(Cymbal(q, 0.32f, 0.8f, 0.09f, 0.11f), Stand(p, 0.3f),
                Seg(p, 0.76f, 0.66f, 0.9f, 0.9f, 0.045f), Seg(p, 0.88f, 0.46f, 1.0f, 0.56f, 0.045f), Seg(p, 0.56f, 0.72f, 0.6f, 0.96f, 0.045f));
        });
        // ride: a big flat cymbal with a big bell
        IconFactory.Register(Ride, p => U(Ell(p, 0f, 0.3f, 0.9f, 0.1f), Circle(p, 0f, 0.36f, 0.2f), Stand(p, 0.3f)));
        // tambourine: a frame with its jingles
        IconFactory.Register(Tambourine, p =>
        {
            float d = IconFactory.P.Ring(p, 0f, 0f, 0.6f, 0.09f);
            for (int i = 0; i < 6; i++) { float a = (30f + i * 60f) * Mathf.Deg2Rad; d = Mathf.Min(d, Circle(p, Mathf.Cos(a) * 0.6f, Mathf.Sin(a) * 0.6f, 0.15f)); }
            return d;
        });
        // cowbell: the flared bell, its handle and its open mouth
        IconFactory.Register(Cowbell, p => U(Sub(IconFactory.P.Poly(p, new Vector2(-0.3f, 0.5f), new Vector2(0.3f, 0.5f), new Vector2(0.54f, -0.58f), new Vector2(-0.54f, -0.58f)) - 0.04f,
            Ell(p, 0f, -0.6f, 0.4f, 0.1f)), IconFactory.P.Ring(p, 0f, 0.66f, 0.13f, 0.05f)));
        // claves: two thick sticks crossed
        IconFactory.Register(Claves, p => U(Seg(p, -0.7f, -0.62f, 0.7f, 0.5f, 0.12f), Seg(p, 0.7f, -0.62f, -0.7f, 0.5f, 0.12f)));
        // triangle: open at one corner, on its string, with the beater
        IconFactory.Register(Triangle, p => U(Seg(p, 0f, 0.56f, 0.64f, -0.54f, 0.065f), Seg(p, 0.64f, -0.54f, -0.64f, -0.54f, 0.065f), Seg(p, -0.64f, -0.54f, -0.14f, 0.32f, 0.065f),
            Seg(p, 0f, 0.56f, 0f, 0.92f, 0.035f), Seg(p, 0.42f, 0.14f, 0.94f, 0.52f, 0.05f)));
        // any other piece: a plain drum
        IconFactory.Register(Drum, p => SideDrum(p, 0f, 0.2f, 0.72f, 0.2f, 0.62f, 0.075f));
    }
}
// ==== KitGlyphs end
