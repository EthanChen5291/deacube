# DeaCube v4 — UI language (for packages U1 and U2; K/R/P read §1 and §3)

The user: "the UI right now in the editorial (the buttons and stuff) are a bit too AI generated and read as a bit too artificial. I want you
to analyze similar games like this and how their UI works (like Melatonin for example)."
Research (normative detail, read it fully): scratchpad/v4/ui_research.md — §2 diagnosis, §3 the 12 rules, §4 component specs (sizes at a
1920×1080 reference), §4.9 where every current control goes. Reference images: scratchpad/v4/ui_refs/ (LOOK at the 12 listed in §5 of the
research before designing: melatonin_editor_2.jpg, melatonin_editor_1.jpg, melatonin_editor_7_rest_state.jpg, melatonin_3_progress_rail_crop.jpg,
melatonin_editor_6.jpg, tonecraft_2024_hud_rest.jpg, tonecraft_2024_panels_crop.jpg, townscaper_web_2.jpg, rhythmdoctor_6.jpg,
rhythmdoctor_5.jpg, unbeatable_6_prompts_crop.jpg, minimotorways_3_tray_crop.jpg; downscale with sips -Z 1100 first).

## 1. Direction (one paragraph to keep in your head)
"Melatonin for structure, Spider-Verse for punctuation." The world is the interface. ~90 % calm ink-on-paper: few controls, no boxes around
resting controls, pictures of objects instead of abstract glyphs, context controls attached to their object, asymmetric edges with one job
each, selection drawn (wavy underline, open ring, ink hexagon, tilt) never filled with a new hue. ~10 % comic punch for hero moments only
(PLAY, tempo, PRESENT, mode stickers, lettered onomatopoeia): rotated stickers, hard offset shadows, misregistration, halftone. One motif ties
it together: the mini isometric cube (CubeGlyph). Imperfection is systematic and seeded; lines boil only while hovered/pressed/active; UI
tweens move on twos (Look.Stepped); the UI breathes with the beat. Target ≈ 20 hit targets at rest in the world, ≤ 20 on the inspector face.

## 2. Decisions on the research's open points
- Hover captions: YES. InkCaption: one or two lowercase words after 0.35 s of hover, never at rest, switchable (menu → settings). Every
  icon-only control gets one (InkCaption.Attach(go, "words")).
- Title menu: package T (SPEC §7b) owns MainMenu / MenuStage / MenuButton: it replaces the "breaking apart" exit with the comic panel
  transition and (Should) turns the four pills into the lowercase text menu with a wavy underline. The cube wordmark stays.
- The deck (research §4.2): Must. U1 owns IslandTray.cs (all tray visuals + input); placement goes through IslandGhost.Snapshot() /
  IslandGhost.Place() which K owns (K adds the column modes STACK / INSERT / MERGE / BLOCKED there).
- Menu strip behind the logo cube, column rail, PRESENT sticker, instrument column, bottom-left transport, sound drawer, focus-loop badge:
  Must (U1). Mix-mode tongues and the contextual recentre sticker: Should (scroll-wheel volume on a chip: Must). Punch pads restyle: Should.
- Inspector card as the cube's speech bubble with the "more ⋯" drawer, RhythmStrip, DurationPicker: Must (U2).
- IslandHeader ribbon + the dotted "+ above / + below" ghost islands + CursorKit: Must (U1).
- Keep: the world, the cube fly-to-camera with its halftone burst, the Bangers tempo digits, the cube-built title, the tutorial's balloon
  idea (restyled as InkShape bubbles, lowercase Fredoka).

## 3. The kit (landed at M0 in Assets/DeaCube/Ink/, owner U1 afterwards; U2/K/R may request changes via their report)
- InkShape : MaskableGraphic — Kind {RoundRect, Circle, Ribbon, Bubble, Sticker, Ghost}; fill = Graphic.color; Ink; SetInk(min, max) width
  (heavier on the shadow side); HasShadow / ShadowOffset (+4, −5) / ShadowColor (hard, no blur); Wobble (px); RotJitter (±deg, seeded); Hollow;
  Boil (redraw at 10 fps — on only while hovered/pressed/active); Radius; TailTip / TailWidth (Bubble, local px); SetDash (Ghost). Seed = the
  object's name unless set. InkShape.Create(parent, name, kind, fill, size).
- CubeGlyph : MaskableGraphic — the isometric cube: colour = Graphic.color; Scale (fraction of min(w, h)); Hollow; Ring (ink hexagon =
  selected); GroundShadow; Satellite (dotted-note dot orbiting on twos); Hop(height). CubeGlyph.Create(parent, name, colour, px).
  Picker/strip sizes: px = 30 · AudioCube.SizeOf(beats)^1.5 (≈ 15 / 21 / 30 / 41 / 55 px for 16th … whole).
- InkWave : MaskableGraphic — wavy underline/divider (Amplitude 2, Wavelength 11, Thickness 2.5, Animate = phase steps at 8 fps).
- InkCaption — static Show(target, words) / Hide(target) / Attach(go, words) / Enabled.
- Colours: paper Comic.Cream, ink Comic.Ink, hero accent Comic.Pop, print shadow Comic.PrintShadow, instrument colours Instruments.Colors,
  chord colours from the islands. Fonts: Comic.Font (Fredoka SemiBold) lowercase for words; Comic.DigitFont (Bangers) for the tempo digits and
  onomatopoeia only. No all-caps pills.
- Motion: Look.Stepped / Look.Time12 for UI tweens (drags and the playhead stay smooth); beat pulses via GlobalClock.OnBeat.
- World-anchored UI (island header, + ghosts, length row, bubble tail): position in LateUpdate from Camera.WorldToScreenPoint →
  RectTransformUtility.ScreenPointToLocalPointInRectangle; set anchoredPosition only; no LayoutGroups on moving elements; CanvasGroup fades;
  hide when behind the camera / off-screen.

## 4. Budgets and rules for both UI packages
- No per-frame allocations in steady state; meshes rebuild only on change (or boil at 10 fps); the whole HUD ≤ 1.5 ms/frame (measure).
- Hit targets ≥ 28 px at the 1920×1080 reference; every icon-only control has a caption.
- No digits on screen except the tempo; words only in menus, captions and the tutorial.
- Every gesture that changes the song = exactly one History.Push (by the UI owner of the gesture, unless the called SongManager op pushes).
- Keep the Hints ids the tutorial needs: play, tempo, present, palette (→ the instrument column), swatch.<i> (→ its chip), addIsland (→ the
  deck), pills (→ the column rail), inspector, inspector.grid, inspector.volume, inspector.close, inspector.swatches (→ the instrument chip),
  and register the v4 ids (SPEC §2.5). Retire an id only together with the tutorial step that used it (U1 owns the tutorial).
- Verify by eye: captures at 1920×1080 (Game view set to that size; ScreenCapture method in PROTOCOL.md) of your states, compared side by
  side with the reference images. Numbers alone do not prove a look.

## 5. Tutorial (U1, v4 steps, short lowercase sentences in ink bubbles)
1 draw: click a tile and keep clicking — the hologram plays what you draw · 2 pick a length on the cube row (fat = long) · 3 finish: click the
last tile again · 4 click your cube to open it · 5 stretch a note on the strip · 6 close it · 7 add an island below (+ under an island) ·
8 drag it by its edge within its column · 9 make it lower (▼) · 10 open the deck and place an island · 11 present. Each step's target is a
Hints id; bubbles never block the gesture they teach (no raycasts while the user drags/draws).
