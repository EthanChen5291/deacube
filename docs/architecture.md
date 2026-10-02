# DeaCube architecture

The module map of the Unity project (`AudioCube_Unity/Assets`). Everything is C# on URP, no native code.

```
Unity (C#, URP), no native code
Front    MainMenu (cube-wall title, comic panel exit, liquid-pixel glass cubes) · Menu/MenuGallery + MenuPreview (the gallery shelf and its hook previews) · InterfaceController (vibe prompt) · Onboarding (12-step comic tutorial, one-off tips) · Hints
HUD      UIManager + Ink/Hud (logo menu strip, column rail with section brackets, instrument groups, clipboard chip and paste fan, size row, selection bar, shortcut sheet, transport, sound drawer) · IslandHeader (+ the stairs and melody-roll headers) · SectionHeader · IslandTray (the deck of vibe cards: numbers, stars; stairs, melody, keyboard and Moon cards) · JobBadge · Vibe + VibeGlyphs · CursorKit
Ink kit  InkShape · CubeGlyph · InkWave · InkPainter · InkCaption (hand-inked stickers, mini cubes, wavy underlines, hover captions)
World    SongManager (columns, sections, grounds, repeat passes and styles, long grids, launch, the hold-then-return rules, drum sections, song key) · KeyBlock + KeyBlock.Kinds (island, keyboard, staircase, melody roll, beat pips, hub gauge, octave tower, ride) · SectionPlinth · StructureLook · KeyHands (the sphere hands that play keyboards) · KeyStage (keyboards and fragments popping in from the sea) · Belt · IslandDrag (free drag) · IslandGhost · Route · Comet · ColumnBands · OrbitCamera
Magic    WorldMagic · StageLights (the dim stage and the melody spotlight) · Tower (pillar, waves) · Magic (trails, appear pops) · LongGridGlow (the long grid's rim and the glow under its walking cubes) · LaunchFx (the build-up into the next section) · Terrace · EchoLayer (glass octave layers) · RewindFx · StairsFx · MirrorFx · SelectionFx · SectionGlow
Edit     PathManager (the hand: presses and placing mode; size first, draw, hologram draft, capacity, melody-roll strokes, the Shift marquee) · SongOps (sections, staircases, melody rolls, many grids at once) · GridSelection · CubeOps (octave copy, harmony, flip) · Clipboard (adapted copy / paste, echo, answer and step pastes) · MelodyLine · PitchLook · FocusLoop · CubeInspector + InspectorCard · PathGridView · RhythmStrip · DurationPicker
Music    AudioCube (per-note lengths, size, smear, long-grid walks, octave layers, harmony bends, rewind, hold-then-return) · VoiceRules (single audio decision point) · Harmony (chord jobs, fit, pattern adaptation, staircase runs, harmonies, answers, variations) · Rhythm · MusicTheory · Performance
Present  Presenter + Present/* (PresentSea: columns rising from the sea on the beat; PresentRig: the slowly falling camera; PresentWater: the splashes)
Look     DeaCube/Toon shader (cel bands, ink outline, halftone) · SkyToon / SeaToon · Hologram · Smear (paint-blob smears) · Fx comic effects
Audio    SynthEngine: MeltySynth rendering the GeneralUser GS SoundFont on the audio thread (79 sounds in 11 instrument groups — the eleventh, fx, for effect sounds like Atmosphere — one channel each, plus an effects channel for the launch riser), sample-accurate event queue, a preview bus the song ducks under · LaunchRiser (a reversed cymbal timed to peak on the landing)
State    SongState (save v6: columns, sections and their names, registers, note lengths, repeats and their styles, the song key, long grids, launches, keyboards and their ranges, staircases, melody rolls, octave layers, harmony bends, sounds, drum sections; autosave, backups, undo) · Gallery (Resources/Gallery)
```
