# DeaCube v2 — Final Specification (single source of truth)

Merged from the four proposals (`proposal_clarity.md` = spine, grafted with `proposal_pragmatic.md` engineering, `proposal_toy.md` necklace/Moons/hub, `proposal_producer.md` energy/fill/climate-as-view) under the three judges' mustHaves / mustAvoids. Code citations are to `/Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Assets` as of the current working tree (Sep 29 2026), which already contains the in-flight Synth integration: `AudioCube.TrySchedule` goes through `Synth.Note` with a per-cube `owner` id and a hash-seeded humanise, `TileInteraction.PlayPreview` uses `Synth.Preview`, `Instruments.SynthSlot` maps the six palette indices onto `SynthBank.Defs` slots 0..5, and `DeaCubeBoot.Awake` calls `Synth.Ensure()` + `SetMasterGain(ProjectConfig.SynthMasterGain = 2.2)`. This spec builds on that state and does not redesign the Synth facade (`DeaCube/Audio/SynthEngine.cs`, `SynthBank.cs`).

## 0. Merge decisions where the judges disagreed

| Topic | Judge 1 | Judge 2 | Judge 3 | Decision in this spec |
|---|---|---|---|---|
| Necklace semantics | hit-advance (cube advances one node per hit) | mask over steps, keep step-k = node | hit-advance | **Hit-advance** (2 of 3). Ping-pong/once are defined over the *hit* index, the pass restarts at node 0 every window as today, `k` defaults to `n`, so old songs are bit-identical. See §3.2. |
| Necklace length `n` | steps in the window | steps per bar, ≤ 16, repeats per bar | steps in the window | **Steps per bar** (`BeatsPerBar / StepBeats`, 2..24 with the HUD's meters 3..6), pattern repeats every bar, hit index runs on through the window. Resolves on the bar, never a 64-bead ring. |
| Always-on drums | Moon | stamp-all only (no new island kind in pass 1) | Moon *or* drum Traveller, not both | **Moon** for drums (kept out of the Route index space, §2.1), **Rider only for pitched cubes** (a drum cube cannot be a Rider). Moons and Riders land in milestone M3, after the engine swap and the world, so pass 1 is stamp-all + per-island drums as judge 2 wants. |
| Parked islands / knots | mustHave (clip bin) | mustAvoid in v2 | later, not the first world phase | **P2** (designed in §3.6, not built in v2). Sleep islands cover silence. |
| Repeat ×2/×4, play-it-in, bounce | mustHave | v3 backlog | later phases | Repeat and play-it-in are **P1 in the last milestone (M4)** on top of the windows list; bounce is **P2**. |
| Per-cube UI | halos ≤ 8 with sub-rings | keep the cube panel with a "more" row; halos later | panel with "more" row or halo ≤ 8; world rings only for stickers and a ≤ 6 island halo | **Keep the sliding cube panel** (+ "more" row). World-anchored rings: node sticker ring (hold) and island hub halo (6). Cube halo = P2 once the world is stable. |
| Energy → cutoff | mustAvoid (filter steps at boundaries) | scales velocity and cutoff while lit | scales velocity, density, cutoff, beacon | **No cutoff from Energy.** Energy = velocity × Moon density × pad presence × beam height. Cutoff stays global (Tone dial, funnel punch-in). |
| Cable placement | under the islands | under the islands (sag ≈ −2.5) | not under (platforms hide it) | **Hanging cable** from hub to hub, sag −1.8 at the midpoint, plus the comet flies an *elevated* arc above the same lateral path, so at low pitch angles the moving object is never hidden. |
| Mood/climate application | SetChord in one snapshot *or* reversible view | either | reversible view, row-count preserving maps | **Reversible view** (`semis` untouched) with row-count-preserving maps and a new `Dominant9` quality. |
| Followers | row ±1 at 60 % | row ±1 | nearest chord-tone row ≥ 3 semitones away | **Nearest row above/below whose interval is ≥ 3 semitones** (skips 2nds on 7th/9th chords). |
| Pad costume | tile + two chord tones | tile + tones below/above | tones **below** the tile | **Two chord tones below** at 65 %. |

Everything else in the judges' mustHaves is adopted verbatim; every mustAvoid is honoured (checked item by item in §9.3).

---

## 1. Vision (10 lines)

1. DeaCube is a sea of floating chord islands you place anywhere; a glowing Route strings them in song order and a comet tours it, so "where is the beat" and "which island sings" always have the same answer.
2. You still draw a path over tiles and a block hops along it on the beat; blocks stack instead of colliding; that mechanic is untouched and everything new hangs off it.
3. Rhythm is a necklace of beads, not a number: the path says *what*, the necklace says *when*, and the cube visibly waits between hits.
4. Every tile is a chord tone and exactly one pitched island sounds at any instant, so nothing anyone draws, stacks, clones or performs can clash.
5. Colours are roles with a register each (bass low, bells high, pad in the middle): any combination of paths arrives already arranged.
6. Drums are drawn with the same gesture on round Moons that play under the whole song, so the first thing you hear has a beat.
7. One click makes counterpoint (Twin), harmony (Shadow), motifs (Rider, Stamp), sections (Energy, Fill, Sleep) and weather (Mood, Climate) without a single theory word.
8. The sound is a real-time multisampled SoundFont synth with velocity, releases, one shared space and a limiter: it sounds produced, and it costs nothing in gameplay.
9. Time is always visible: pre-lit rings, marching cable dashes, count-in glow, sky beam and sea ripple on downbeats, at every zoom, with a camera that follows the comet and never cuts.
10. Only BPM is a number on screen; every other control is a glyph, a colour, a shape or a motion, and every control changes something visible before it changes the sound.

---

## 2. World & time model (final)

### 2.1 Objects and definitions

- **Island** = one `KeyBlock` = one chord = one measure of `bars × BeatsPerBar` beats, exactly as today (`KeyBlock.LengthBeats`, `KeyBlock.cs:32`). Islands sit anywhere on the sea plane `y = 0`; **yaw is fixed** for every island (rows always run +z, "up the rows = higher" reads the same everywhere).
- **Hub** = a low polygon prism at the platform's **front-centre** (`KeyBlock.HubPos = Center + (0, 0.15, −(Depth/2 + 0.9))`, radius 0.7, height 0.5, `MeshFactory.Prism(sides, r, h)`), tinted `chordColor`. **Sides = bars + 2** (triangle 1 bar … hexagon 4); one vertex lit per bar played. The hub is at once (a) the island's grab handle, (b) the comet's dock (ring angle 0 is at the hub), (c) the anchor of the island halo, (d) the cable's attachment point (underside). Clicking the hub cycles bars 1→2→3→4→1 (`SongManager.SetBars`), replacing the `minus/plus dotsN` buttons of the measure bar.
- **Route** = the closed cable through every Route island's hub in `SongManager.Islands` list order; the last links back to the first as a dim return link (alpha 0.35). `Route.cs` owns one `LineRenderer` per consecutive pair (24 samples, quadratic curve from `hubA.underside` sagging to `y = −1.8` at the midpoint into `hubB.underside`), material `Fx.Flow`, `_Speed` = `GlobalClock.BeatsPerSecond` × (1 / segmentBeatLength) so exactly one dash marches per beat, frozen when paused (`_Speed = 0`).
- **Comet** = the playhead (`Comet.cs`): additive sphere (r 0.22) + `TrailRenderer` (0.6 s) in `Lerp(activeIsland.chordColor, white, 0.5)`. Position is *derived every frame* from `GlobalClock.SongBeatD` and `SongManager.MeasureStarts`; it is never integrated, so it is always exactly on time and follows a dragged island for free.
- **Moon** = a round percussion island (`KeyBlock.kind = 1`): disc platform (r = 3.9), grid 6 × 4 (rows = kick / snare / closed hat / perc via `SynthBank.DrumForRow(row, 4)` → 36 / 38 / 42 / 46; columns = loudness soft→hard), its own mini ring pointer that laps once per **bar** of the song, no chord, no hub polygon, a small disc handle instead. Moons live in `SongManager.Moons` (a second list) and `SongState.moons` (a second array) — **never in `Islands`, never in `MeasureStarts`**, so `AddMeasure/RemoveMeasure/MoveMeasure` index arithmetic is untouched. Max 2 Moons per song (the second bounces back with a shake).
- **Cube** (`AudioCube`) — as today, plus: it belongs to an island **or** a Moon (`CubeState.moon ≥ 0`), it has a **windows list** (§2.10) instead of `startBeatOffset/measureLength`, a necklace, per-node stickers, octave, twin/shadow/echo flags and a Rider flag.
- **Sea** = an additive fog plane at `y = −4` (Fx), source of the per-beat ripple ring from the lit island.

### 2.2 Placement, moving, snapping

- **Positions live in the model**: `MeasureState.px, pz, placed` (§5). `SongManager.BuildIslands` reads them; `placed == false` ⇒ the **legacy row layout** (today's formula, `SongManager.cs:116-120`) so an old save looks exactly as it did; the next `Capture()` writes the computed positions with `placed = true`. Moons: `MeasureState.px, pz` too; a new Moon spawns at `SongCenter + (0, 0, −(SongBounds.extents.z + 9))`.
- **Picking**: the platform `BoxCollider` (added in `KeyBlock.Build`, tag `Island`) and the hub prism collider (tag `Island`, same KeyBlock) are recognised by `PathManager.Pick` when the hit is neither a tile nor a cube. Left-press on them + drag > 6 px ⇒ `EditorState.DraggingIsland` (new enum member) which suppresses `StartPath`, hover and tile clicks; a press without drag selects the island (`UIManager.SelectMeasure(i, focus:false)`).
- **Drag**: the island root follows the mouse on the `y = 0` plane (`Plane.Raycast`), offset preserved from the grab point; it lifts 0.5 u and tilts 4° toward the motion (visual only, `KeyBlock.dragLift`); **snap 0.5 u**; each drag frame: `Route.Refit(i)` (the two touching segments), `RefreshLine()` on every cube of that island (`AudioCube.cs:166`), `Comet` re-samples. Cubes ride along because `RestPosition` reads `tile.Top` (`AudioCube.cs:237`).
- **Overlap push-out** on release: if `WorldBounds` expanded by 1.5 u intersects another island's or Moon's, slide out along the centre-to-centre vector to the nearest free spot (spiral search on the 0.5 grid, step 0.5, max radius 40) over 0.25 s (`Ease.OutBack`), with a soft `ProceduralAudio.Thud`.
- **Release** ⇒ write `px, pz, placed = true` into the KeyBlock (`MeasureState.From(KeyBlock)` reads them) and `History.Push()` ⇒ moves are undoable and saved for free.
- **Soft world bounds**: none for islands (the camera has them). `FrameAll` always brings everything back.
- **Add** (`plus` in the strip or `duplicate` in the island halo): the new island rises from the sea at the first free spot 12 u beyond the selected island along the Route direction (`SongManager.FindFreeSpot`), inserted **after** the selected island (list order), `Fx.IslandCelebrate`. **Remove**: sinks over 0.4 s then the rebuild.
- **Island drag hum**: `Synth.Preview(Pad, root, 60, 0.25)` re-issued every 0.25 s **only while the clock is stopped**; while playing the drag is silent (harmonic safety applies to UI sounds).

### 2.3 Route order (song order)

- Song order **is** `SongManager.Islands` list order, rendered as the Route; `RecomputeMeasureStarts` (`SongManager.cs:148-168`) keeps walking that list. **Distance is never time.**
- Order changes **only** by:
  1. **Drop-onto-wire**: while dragging an island, if its hub comes within 1.5 u of a Route segment A→B that does not touch the dragged island, that segment **opens a visible gap** (the segment splits into A→ghostHub and ghostHub→B, brightening amber, `Route.ShowGap(seg, pos)`); releasing there calls `SongManager.MoveMeasure(from, to)` (list surgery + `CubeState.measure` remap, the same pattern as `AddMeasure/RemoveMeasure`, `SongManager.cs:215-262`) ⇒ `RebuildFromState` ⇒ `History.Push()`; a light pulse runs the whole Route once (`Route.RunPulse()`), `ProceduralAudio.Whoosh`. Releasing anywhere else changes position only.
  2. **Pill drag** in the overview strip (P1): drag a pill sideways past its neighbour ⇒ the same `MoveMeasure`.
- `MoveMeasure(from, to)` semantics: remove index `from`, insert at `to` (indices in the list *after* removal), remap every cube's `measure`; cubes keep their tiles; Riders and Moons are unaffected (Riders re-derive windows on rebuild).
- **Reorder while playing**: allowed. `RebuildFromState` already preserves play state and beat; additionally, if the lit island moved, `RebuildFromState` re-seeks to `MeasureStarts[newIndexOfLitIsland] + sameLocalBeat` (producer) so the music continues from the same place inside the same chord.

### 2.4 Playhead: the comet's exact rules

Let the lit island be `i` (`SongManager.ActiveMeasureIndex(beat)`), `L = Islands[i].LengthBeats`, `local = SongBeat − MeasureStarts[i]`, `next = (i + 1) % Islands.Count`.

1. **Ring lap** for `local ∈ [0, L − 1)`: the comet rides island `i`'s beat ring at angle `θ = 360° × local / L` measured from the hub (front-centre), clockwise seen from above, at ring radius, `y = 0.35`. So it is the **in-bar pointer**: the segment under it is the white playhead segment.
2. **Flight** for `local ∈ [L − 1, L)`: `t = local − (L − 1)`; lateral position = `Route.PositionAlong(i, t)` (the cable curve from hub i to hub next), height = cable height + `1.2 + 1.6 × sin(π t)` (an elevated arc, never hidden behind platforms). Speed adapts to distance; a far island is a dramatic streak. If `Islands.Count == 1`, the comet simply completes its lap (θ from 0 to 360°) and re-docks.
3. **Arrival** at `local == 0` of island `next` (detected when `ActiveMeasureIndex` changes, or on `OnLoop`): `Fx.Pillar(hub, chordColor, 9, 0.9, 1.6)` (sky beam), `KeyBlock.Pulse(1)`, `Fx.Ripple(Center, chordColor, 3.5, 0.8)` (sea ripple), `Fx.BeatBump()` (bloom +0.2 for 100 ms), `Fx.Starburst(hub, chordColor)`.
4. **Count-in**: from `local ≥ L − 1` the next island's hub pre-glows (`KeyBlock.SetPreGlow(t)`: core intensity 0.2→0.9, hub emission ramps) so you see who is next one beat early.
5. **Paused**: the comet freezes in place; **stopped**: it docks at hub 0 at angle 0.
6. Sleep islands and Repeat knots: the comet still visits and laps (a Repeat ×N island is lapped N times; the knot shows N dots).
7. Moons have their own **mini ring pointer**: a small bright bead lapping the Moon's ring once per bar (`θ = 360° × BeatInBar+BeatPhase / BeatsPerBar`), always running while playing, so the drum layer's clock is visible without a comet.

### 2.5 Global pulse and the rhythm legibility kit

All cues originate from the **lit island** (no obelisk at the origin):

- **Beat ring** (`KeyBlock`, replaces the `_Fill` texture ring, `KeyBlock.cs:147-157, 211-223`): `MeshFactory.SegmentRing(inner 0.972, outer 1.0, segments = bars × BeatsPerBar × 4, gap 0.18)`, per-segment vertex colour, shader `DeaCube/AdditiveVertex`. Colour rules per 16th segment `s`: white if it is the playhead segment; the colour of the cube whose hit lands on `s` (first cube in `SequenceMaster.Cubes` order on that island; two cubes ⇒ the two halves of the segment) computed by the **same resolver** the audio uses (`AudioCube.HitsInWindow(w)` → list of `(step16, node, weight)`); dim grey for 16ths with no hit; beat boundaries slightly brighter. Colours are recomputed only when a cube on the island changes (`AudioCube.OnAnyChanged`), on rebuild, and when the playhead segment advances (one `mesh.colors` write per 16th).
- **Hub polygon** = bars; the vertex for the current bar is lit (`KeyBlock.Update`: `bar = floor(local / BeatsPerBar)`).
- **Cable dashes** march one per beat and freeze on pause (§2.1).
- **Count-in pre-glow** on the next hub (§2.4).
- **Downbeat**: sky beam (`Fx.Pillar` 9 u) + sea ripple + `Fx.BeatBump` (URP Bloom intensity 0.9→1.1 over 100 ms) from the lit island; other beats: a 3 u pillar and the ring tick. The lit island's platform pulse (`KeyBlock.HandleBeat`) stays.
- **Path geometry as music** (`AudioCube.RefreshLine`): `StepBeats / 0.25 − 1` **tick dots** between consecutive beads (a Quarter path shows 3 ticks per gap, a Sixteenth path none, Triplet shows 1 small tick with a triangle mark), **hollow beads** for rest nodes, **bead radius = weight** (ghost 0.6×, normal 1×, accent 1.35×), sticker glyph on the bead (§3.3), and the cube **floats 0.35 u higher per octave** (`octave = +1` ⇒ hovers above its tiles; −1 ⇒ sits lower, sunk into a shadow disc) — height = pitch.
- **Drum clothes**: when the Drums slot is armed (`PathManager.OnInstrumentChanged`) or a drum cube stands on / is selected on a chord island, that island's tile markers swap to kit glyphs (`dot` large kick, `square` snare, `sparkle` closed hat, `ring` open hat, `diamond` perc; 3-row islands: kick/snare/hat) and the tiles darken 25 % (`TileInteraction.SetKitLook(bool)`). Moons wear drum clothes permanently.

### 2.6 Camera (`OrbitCamera`)

- Delete the per-measure focus snap (`OrbitCamera.cs:89-95`) and the `FocusMeasure` call in `PathManager.StartPath` (`PathManager.cs:136`). No camera cut or teleport exists anywhere; `FocusMeasure(immediate:true)` is used only by `StartNewSong` on the very first frame of a song.
- **Leash follow on the comet, ON by default** (`followPlayhead = true` is already the scene default): each `LateUpdate`, if the comet's viewport position leaves the band `x ∈ [0.30, 0.70], y ∈ [0.30, 0.70]`, `focusTarget` moves toward the comet's ground point until it is back in the band (`SmoothDamp` 0.6 s); during a flight the target is the comet itself so the camera glides along the cable. Any user input (orbit, pan, zoom, drawing, island drag) sets the existing 2.5 s cooldown (`userInputCooldown`). `F` / the `target` button toggle follow.
- **Left-drag on empty sea or sky pans** (after 6 px; a plain click still deselects); middle-drag pans as before; right-drag orbits (unchanged); wheel zooms 5..80 toward the cursor's ground point.
- **Double-click an island** (platform/hub/tile) ⇒ `GlideTo(Center)` 0.5 s, no `distT` change (drop the zoom-to-15 jump in `FocusMeasure`, `OrbitCamera.cs:117-119`).
- **Soft bounds**: if the focus leaves `SongBounds` expanded by 30 u, it springs back (0.4 s).
- `O` frame all (unchanged), `Q` home (reset yaw/pitch/dist and glide to the lit island), `A/D` glide to previous/next island in Route order (no `distT` jump).

### 2.7 Overview strip ↔ world mapping

- Pills stay in Route order under the sweeping playhead bar (`UIManager.UpdatePlayhead`). Moons appear as small round pills at the strip's right end after a gap (dim, `kit` glyph), not draggable into the order.
- Hover a pill ⇒ its island's core glow and hub brighten (`KeyBlock.SetHover`); hover an island's hub/platform ⇒ its pill scales 1.15. Pills of islands currently on screen get a thin white underline (`Camera.WorldToViewportPoint` inside `[0,1]`).
- Click a pill ⇒ select + camera glide (as today minus the zoom jump); right-click ⇒ seek (as today). Pill drag-to-reorder = `MoveMeasure` (P1).
- Pills carry the hub polygon shape (`polyN` glyph, N = bars + 2) instead of `dotsN`, the quality glyph and the chord colour; a sleeping island's pill is hollow; energy shows as fill height of the pill (0.4..1.0).

### 2.8 What happens while playing

- Dragging an island: **nothing audible changes** (positions are not time). Cubes hop under the cursor, the comet rides along, a flight in progress re-aims each frame.
- Drop-onto-wire while playing: rebuild with re-seek to the same local beat (§2.3).
- Drawing, stickers, necklace, twin, colour changes: each setter calls `CancelPending()` (⇒ `Synth.CancelOwner(owner)`) exactly as today; the next `Update` re-schedules from the clock within the 0.22 s lookahead.
- Tempo / swing / seek ⇒ `Synth.CancelPendingAfter(now, false)` + every cube's `CancelOwner` (existing wiring); Stop ⇒ `Synth.AllNotesOff(false)` (release tails) then `AllNotesOff(true)` 300 ms later.
- Previews while playing (§4.6): quantised to the next 16th on the lit island; silent (tick + flash) elsewhere.

### 2.9 Harmonic safety guarantee (the proof the code must keep)

Definitions: `W_i = [MeasureStarts[i], MeasureStarts[i] + L_i)` in beats. `RecomputeMeasureStarts` makes the `W_i` **disjoint and covering** `[0, TotalBeats)`; with Repeat ×N (M4) the expanded play order gives each island N disjoint windows, still a partition.

1. **Every pitched NoteOn belongs to exactly one window** `w` (the cube's window list, §2.10) and satisfies `onDsp ∈ [DspTimeOfBeat(w.start), DspTimeOfBeat(w.end))`.
2. **Every pitched NoteOff satisfies** `offDsp = min(gateEnd, windowEndDsp − ε)` with `ε = 5 ms`, and for sustaining slots (Pad, Strings, Choir) `windowEndDsp − one16th`; ties end at the earlier of the cube's next hit and the window end. Enforced in one place: `VoiceRules.Resolve` (§4.3). Grep rule: no `Synth.Note(` outside `VoiceRules.Dispatch` except `Synth.Preview`.
3. **Every pitched midi is a tile of the window's island**: residents read their own tiles; Riders read `w.island.GetTile(x, min(z, rows − 1))`; Twin reads the same tiles; Shadow reads another row of the same island; the Pad costume reads chord tones of the same island; octave ±12 and Lift +12 keep the pitch class; `ClampToRegister` folds by octaves; the low-end law folds by octaves. Transpose is global and applied to every island equally.
4. **Drums are unpitched** (channel 9) and Moons carry no chord.
5. **Previews**: while the clock runs, a pitched preview sounds only if the clicked tile's island is the lit island (`ActiveMeasureIndex(SongBeat) == tile.island.measureIndex`) and is deferred to the next 16th; otherwise the tile flashes and an unpitched `ProceduralAudio.Tick` plays. When stopped nothing else sounds, so any preview is safe. The island-drag hum only when stopped.
6. **Mood/Climate** change every island's effective chord atomically through one rebuild.
7. **Reorder** while playing re-seeks inside the same chord.

Therefore at any DSP instant all pitched notes come from one island's chord. Reverb tails (≤ 2 s) of the previous chord are the only cross-window energy, which is what every real mix has; rule 2's early release for pads keeps them short.

### 2.10 The windows model (formal, replaces `startBeatOffset/measureLength`)

```csharp
public struct Window { public float start, length; public KeyBlock island; public int order; }  // beats; island = where tiles are read
public readonly List<Window> windows = new List<Window>();     // on AudioCube, filled by SongManager.RecomputeMeasureStarts
```

- Resident cube on island `i`: one window per occurrence of `i` in the expanded play order (one in v2 until Repeat lands).
- Rider cube: one window per Route island in play order (`island = that island`).
- Moon cube: one window per **bar** of the song: `start = b × BeatsPerBar, length = BeatsPerBar, island = moon` for `b ∈ [0, TotalBeats / BeatsPerBar)`.
- `UpdatePlayback` finds the active window (`windows[a].start ≤ local < end`, cached index, linear scan on change), runs today's step logic inside it (`ComputeStep`, `StepStartLocalBeat`, hop), and its scheduling loop continues into the **next window** (first step of `windows[a+1]`, or `windows[0] + TotalBeats` on loop) so the 0.22 s lookahead crosses boundaries exactly as the current code's "schedule node 0 at the next pass" branch does (`AudioCube.cs:339-343`).
- The per-window **pass id** for hashing = `(GlobalClock.LoopIndex, window.order)`; `GlobalClock.LoopIndex` is new (`0` on Stop/Seek(0), `++` on `OnLoop`).
- Sleep island: windows on it are marked `silent` (cube stays asleep, no hops, nothing scheduled); the Moon ignores sleep.

---

## 3. Music interface set (final)

Priorities: **P0** must ship in v2, **P1** should (milestone M4), **P2** later (designed, not scheduled). Icon names are `IconFactory` sprite names; "glyph" says how to draw it with the SDF primitives `Circle, Ring, Box, Seg, Tri, Poly, Arc, Head, Star, Wedge, Rot, U, Sub` (`IconFactory.cs:84-176`). "Undo/save field" names the `SongState` field (§5) that makes the control undoable and saveable; every change ends in `History.Push()`. Feedback lists the world animation that explains the control before the sound does.

### 3.1 The table

| # | Control | Scope | Icon (name) + glyph | Musical mechanism | Default / range | Feedback | Field | Pri |
|---|---|---|---|---|---|---|---|---|
| 1 | Necklace (hits) | cube | `NecklaceDial` (UIKit): a ring of `n` `dotSmall` beads, filled = hit, with a `bar` notch at bead 0; HUD icon `necklace` = `Ring(0.7,0.08)` ∪ 6 small `Circle`s on it, 3 filled | E(k, n) via Bjorklund, per bar, rotated by `rot`; the cube advances one node per hit (§3.2) | `hits = −1` (= n); range 1..n | beads fill/hollow live; island ring re-lights; one quiet click per changed bead | `hits` | P0 |
| 2 | Necklace (rotate) | cube | drag the notch around the `NecklaceDial`; keys `[` `]` | pattern rotation `rot ∈ [0, n)` | 0 | the whole bead ring turns; ring re-lights | `rot` | P0 |
| 3 | Bead edit | cube | click a bead of the `NecklaceDial` | toggles that step's hit ⇒ explicit mask (`hits = −2`) | derived | bead toggles; ring re-lights | `mask` | P0 |
| 4 | Step (pace) | cube | `slices1/2/4/8` (exist) + `slices3` (parametric, exists) | `StepLen.Triplet` = 1/3 beat, swing off (the `SwingK` gate `AudioCube.cs:239` tests `StepBeats <= 0.5`, which would include 0.333, so add `&& step != StepLen.Triplet`) | Quarter; 5 values | icons pulse at their rate (exists); tick dots between beads change | `step` (4 = Triplet) | P0 |
| 5 | Gate | cube | `gate0/1/2` (exist) | short 0.3 / normal 0.92 / long = tie to the cube's next hit, all clamped to the window end and the slot policy (§4.3) | Normal | landing pillar length; trail | `gate` | P0 (exists) |
| 6 | Mode | cube | `modeLoop/PingPong/Once` (exist) | over the **hit** index (§3.2) | Loop | as today | `mode` | P0 (exists) |
| 7 | Sticker: rest | node | `rest` (exists); click a bead of the click-selected cube = toggle rest (as today) | node silent; cube still hops | none | hollow bead, hop without flash | `mods[i]=1` (+ `rests[i]` dual-written) | P0 |
| 8 | Sticker ring | node | hold a node 0.35 s ⇒ `PopRing` of 7 glyphs at 34 px around the node: `ghost` = `Ring(0.4,0.07)` small; `accent` = `Circle(0.5)` ∪ 8 short `Seg` rays; `ratchet2` = 2 `Box` bars; `ratchet3` = 3 bars; `coin` = `Circle(0.6)` with the left half `Sub`tracted by a `Box` then `Ring`ed (two-tone disc); `tie` = `hbar` with `Circle` ends; `lift` = `Circle(0.25)` under a `chevronU` | ghost = velocity × 0.45; accent = accent01 + 0.35; ratchet ×2/×3 = 2/3 sub-hits at `step/r`, velocities 1.0/0.82/0.68; coin = fires when `Rhythm.Hash(pass, node, seed) & 1 == 0` (50 %); tie = no note-off until the next hit (clamped); lift = +12 (register-clamped) | none | bead radius = weight; ratchet bead shows bars; coin bead flickers; tie bead joins the next bead with a bar; lift bead gets a chevron; skipped coin pass = "slip": hop, no flash, soft puff | `mods[i]` | P0 |
| 9 | Octave | cube | `octUp` / `octDown` = cube outline (`Box` ring) with `chevronU/D` above/below | midi ± 12 before the register fold | 0; −1..+1 | cube floats 0.35 u higher/lower (height = pitch); first tile re-auditions | `octave` | P0 |
| 10 | Twin (canon) | cube | `twin` = two offset `Box` outlines + `Head` arrow; sub-ring (hold): `slices2`-style "×2", `half`-style "½", reversed `Head` pointing left, `octUp`, `chevronR` (+1 phase) | creates a clone on the **same tiles** (`CubeState.Clone` + modifiers); default = half speed (step index −1) + octave +1; variants: double speed, reversed (`reverse`), +1 hit phase (`phase`) | button | translucent twin drops onto the stack with a thud and immediately plays against the original | new cube (`twinOf`, `reverse`, `phase`, `octave`) | P0 |
| 11 | Shadow (harmony follower) | cube | `shadow` = `Box` outline with a dashed `Box` outline above; cycles off / above / below / both | also plays the nearest chord-tone row above/below whose interval ≥ 3 semitones at 60 % velocity, same slot | 0 | translucent ghost cube hops on the shadow row in sync | `follow` (0..3) | P0 |
| 12 | Echo | cube | `echo` = `Circle(0.5)` then three `Circle`s shrinking 0.36/0.26/0.18 to the right; hold ⇒ `shimmer` toggle (`lift` glyph on the last dot) | 0..2 repeats on the step grid at −35 % each, dropped if past the window end; shimmer = +1 shadow row per repeat | 0 | fading afterimages on the original tile | `echo`, `shimmer` | P0 |
| 13 | Rider | cube (pitched only) | `comet` = `Circle(0.3)` + tapered tail of 3 shrinking `Circle`s | the cube replays its grid offsets on every Route island (rows clamped); windows = all islands | off | the cube grows a tail and leaps onto the comet at the window end; drops onto the next island | `rider` | P0 (M3) |
| 14 | Colour / role | cube | 10 swatches in the cube panel (`dotSmall` on colour, role silhouette stamped) | slot change (§4.1) | slot 0 | cube, beads, line, halo recolour; first tile re-auditions | `instrument` | P0 |
| 15 | Level / mute / delete | cube | slider, `speaker`/`mute`, `trash` (exist) | as today | 1 / on | glow size; frost-dim | `volume`, `muted` | P0 (exists) |
| 16 | Stamp | cube | `stamp` = `Box(0.6,0.35)` on a wavy base (`Arc` pieces); click = to hovered island, hold 0.8 s (existing `holdFill`) = to every island | copies grid offsets relative to node 0 + necklace + stickers to the target island(s), rows clamped, via `PathManager.RestoreCube` | button | ghost path previews on hover; stamped cubes drop in with the celebrate burst | new cubes | P0 |
| 17 | Shape stamps + dice walk | cube (drawing) | `stairsUp`, `stairsDown` (3 stepped `Box`es), `zigzag` (`Seg` zigzag), `mountain` (`Tri`), `dice` (exists) in the pencil ring | lays a 4-6 tile path from the clicked tile (`PathGen`), clamped; dice = seeded random walk respecting `Adjacent`, `seed` saved | none | ghost path under the mouse; drops in | `seed` | P1 |
| 18 | Play-it-in | song/island | `record` = `Ring(0.7,0.09)` + red `Circle(0.3)`; the lit island's ring turns red while armed | while playing, tile clicks on the lit island quantised to the nearest 16th; at the bar wrap the taps become one cube (tapped tiles = nodes, tapped 16ths = necklace `mask`, step = Sixteenth) | off | taps flash and pre-light the ring; the new cube drops in at the wrap | new cube | P1 |
| 19 | Bars | island | the hub polygon itself (`polyN` HUD glyph = `Poly` with N = bars + 2 sides) | 1..4 bars | as generated | prism morphs, ring re-segments | `bars` (exists) | P0 |
| 20 | Chord root | island | 12 `wedge30` wheel (exists) | `SetChord(root)` keeping the effective quality | as generated | recolour, re-pitch, first tiles re-audition | `root` (exists) | P0 (exists) |
| 21 | Mood | island | `sun` (exists) / `cloud` = three overlapping `Circle`s on a `Box` base / `storm` = `cloud` ∪ a small `bolt`; 4th state "as generated" = `ringThin` | reversible quality view (§3.5), row-count preserving | 0 (as generated) | tiles re-glyph (`triUp/triDown/diamond`), platform tint warms/cools, local sky tint, chord strum audition (stopped) or on the next 16th (lit) | `mood` | P0 |
| 22 | Energy | island | `flame` = `Tri`-ish teardrop (`U(Circle, Tri)`) with 4 notch ticks; click cycles 0..3, drag sets | velocity × {0.6, 0.8, 1.0, 1.15}; Moon density (§3.4); pads silent at 0; beam height 4/6/9/12 u | 2 | platform brightness, ember rate, beam height | `energy` | P0 |
| 23 | Fill | island | `flourish` = `Arc(270°)` ending in a small `Star` | last bar of the island: drum cubes (island + Moon) ratchet their last two hits ×2, pitched cubes get one echo, crash (49) on the next downbeat at vel 96 | off | last-bar ring segments flash gold; crash burst on arrival | `fill` | P0 |
| 24 | Sleep (breath) | island | `moon` = `Sub(Circle(0.7), Circle offset)` crescent | island's own cubes and Riders silent and still; length kept; comet still visits; Moon keeps playing at this island's energy | off | island dims, sinks 0.4 u, pill hollow | `sleep` | P0 |
| 25 | Repeat | island | `repeat2/4` = `loop` arc with 2/4 `dotSmall` | expanded play order in `RecomputeMeasureStarts` (island occurs N times in a row) | 1 | Route knot with N dots at the hub; comet laps N times | `repeat` | P1 (M4) |
| 26 | Duplicate / remove | island | `duplicate`, `close` (exist) | as today | – | rises beside / sinks | – | P0 (exists) |
| 27 | Dice (island) | island | `dice` | re-rolls the chord via `MusicTheory.NextChord` inside the current mood | – | tiles shuffle with a chime | `root/semis` | P1 |
| 28 | Moon | song | `kit` = four `Circle`s in a square; strip button "add Moon" | round always-on drum island (§3.4) | none; max 2 | rises from the sea with a splash; its ring pointer starts lapping | `moons[]` | P0 (M3) |
| 29 | Groove dice | moon | `dice` on the Moon halo | rewrites the Moon's cubes' `hits/rot/step` from a template table (straight, shuffle, half-time, four-on-the-floor, breakbeat, waltz) | – | beads re-light; one preview bar | cube fields | P0 (M3) |
| 30 | Climate | song | weather glyph inside a `ringThin` frame (`climateSun/Cloud/Storm`, `climateAuto`) | applies a mood to every island without an override (§3.5), scales sends (0.8/1.0/1.25) and tints the sky | 0 (as generated) | sky lerps 0.6 s, islands re-flower sweep | `climate` | P0 |
| 31 | Tone | song | `eclipse` = `Sub(Circle(0.7), Circle(0.7) shifted 0.45)` dial | `Synth.SetMasterCutoff01(tone)` (global) | 1.0; 0.15..1 | fog thickens as it closes | `tone` | P0 |
| 32 | Space | song | `waves3` = three nested `Arc`s dial | scales every slot's reverb send (`SetSlotReverb(slot, def.reverb × space)`) and chorus × min(space, 1) | 1.0; 0..1.6 | halo radius of every cube grows | `space` | P0 |
| 33 | Swing | song | `wave` (exists, under "more") | as today (8ths/16ths only) | 0 | as today | `swing` | P0 (exists) |
| 34 | Transpose / meter / metronome | song | `note` dial, `dotsN`, `metronome` (exist, under "more") | as today | – | – | – | P0 (exists) |
| 35 | Stutter | song (hold) | `bolt` = `Poly` lightning | re-fires the last 16th's notes every 16th while held (§3.7) | – | camera micro-shake, vignette pulse per 16th | – (performance) | P0 |
| 36 | Funnel | song (hold) | `funnel` = `Poly` trapezoid + `Box` stem | cutoff 1 → 0.15 over one bar while held; springs back on the next downbeat after release | – | fog thickens, sky darkens | – | P0 |
| 37 | Half-time | song (hold) | `half` (exists) | `GlobalClock.SetBPM(BPM/2)` on the next 16th, exact restore on release (next 16th) | – | comet slows, dashes slow | – | P0 |
| 38 | Drop | song (hold) | `dropOut` = `layers` with the top two bars dimmed (`Sub`) | only Bass + Drums audible (temporary slot gains) | – | non-bass cubes dim | – | P0 |
| 39 | Spotlight | cube (hold) | `spot` = a `Tri` cone from the top with a `Circle` at the base; hold the button (cube panel) or key `E` | selected cube 100 %, every other cube's velocity × 0.25 while held | – | others dim to 30 % emission | – | P0 |
| 40 | Pitch previews | tile | none (behaviour) | `Synth.Preview` with the selected slot/cube octave; deferred to the next 16th while playing; silent + tick off the lit island (§4.6) | – | flash + pillar | – | P0 |
| 41 | Cube halo | cube | ring of ≤ 8 `HudButton`s at `WorldToScreenPoint`, hold-to-open sub-rings | replaces the cube panel once the world is stable | – | – | – | P2 |
| 42 | Parked islands | song | dim off-Route islands as a clip bin | – | – | – | `parked` | P2 |
| 43 | Bounce mode | cube | `bounce` = ball + shrinking arcs | 1, ½, ½, ¼, ¼, ¼, ⅛… beat intervals, decaying velocity | – | – | `mode = 3` | P2 |
| 44 | Patch variants per slot | slot | `dots3` under the swatch | `Synth.SetSlotProgram` alternates | – | – | `slotPatch[]` | P2 |

### 3.2 Necklace semantics (hit-advance, exact)

For a cube with step length `sb = StepBeats` inside window `w` (length `L` beats, `bars = L / BeatsPerBar`):

- `n = round(BeatsPerBar / sb)` (steps per bar: Half 2, Quarter 4, Eighth 8, Triplet 12, Sixteenth 16 in 4/4; clamp 1..24). Steps in the window `N = n × bars` (with swing, step starts still come from `StepStartLocalBeat(k)`; the necklace is indexed by `k`).
- `pattern[j] for j ∈ [0, n)`: if `hits == −2` ⇒ bit `j` of `mask`; else `Rhythm.Bjorklund(k = hits < 0 ? n : clamp(hits, 1, n), n)` rotated by `rot` (`pattern[j] = E[(j − rot) mod n]`). `Rhythm.Bjorklund` is the standard Bjorklund/Toussaint algorithm; required identity: `E(3,8) = x..x..x.` (hits at 0, 3, 6), `E(5,8) = x.xx.xx.`, `E(k,n)` with `k == n` all hits.
- Step `k` in the window **is a hit** iff `pattern[k mod n]`. The **hit index** `h(k)` = number of hits among steps `0..k`, minus one (so the first hit has `h = 0`).
- **Node for step k**: `node(k) = NodeForHit(h(k) + phase)` where `NodeForHit` is today's `NodeForStep` (`AudioCube.cs:258`: Loop `h mod nNodes`, PingPong period `2·nNodes − 2`, Once `h < nNodes ? h : −1`), and `reverse` maps `idx → nNodes − 1 − idx`. On a **non-hit step** the cube stays on `node(k)` (= the node of the last hit, or node 0 before the first hit) and **crouches**: `squashKind = 0.35` at the step start, a small lean toward the next node (`AudioCube.leanT`), no press, no flash, no Fx.
- On a **hit step**: `Land(node)` as today (hop arrives from the previous node, press, flash, pillar) unless the node's sticker says rest/slip.
- Since `hits` defaults to `n`, every step is a hit ⇒ `h(k) = k` ⇒ identical to today's behaviour for every existing save.
- The pass restarts at `k = 0`, `h = 0`, node 0 at every window start (today's contract).
- The hop animation between hits: the cube starts hopping at `progress ≥ snapThreshold` of the **step preceding the next hit** (so a rest step before a hit still shows the cube launching in time).
- One pure function drives everything:

```csharp
public struct Hit { public bool fires; public int node; public int weight; public int ratchet; public bool tie, lift, slip; public float velScale; }
/// Pure: reads only cube fields + Rhythm.Hash. Same result for audio (scheduled 0.22 s early), Land visuals and the ring pre-light.
public Hit Decide(int windowIndex, int k, int pass)
```

`fires` = pattern hit ∧ node ≥ 0 ∧ sticker ≠ rest ∧ ¬(sticker = coin ∧ `(Rhythm.Hash(pass, node, seed) & 1) == 1`) ∧ ¬muted ∧ ¬`Instruments.Muted` ∧ ¬window.silent. `slip` = pattern hit ∧ coin failed. `weight` 0 ghost / 1 normal / 2 accent. `pass` = `GlobalClock.LoopIndex * 64 + window.order`.

### 3.3 Stickers (`mods[i]`, one per node)

`0 none · 1 rest · 2 ghost · 3 accent · 4 ratchet2 · 5 ratchet3 · 6 coin · 7 tie · 8 lift`. One sticker per node (one glyph per bead: legible). Click a bead of the click-selected cube toggles rest (0 ↔ 1, today's gesture, `PathManager.cs:109-117`); hold 0.35 s opens the ring with the other seven (`UIManager.OpenStickerRing(cube, node)`), positioned at `Camera.WorldToScreenPoint(marker)` clamped to the safe area, closing on click-away/Esc; each choice auditions the node once with its new behaviour (§4.6 rules). `rests[i]` is written alongside (`rests[i] = mods[i] == 1`) so older builds still read the file; on load `mods == null || mods.Length != nodes` ⇒ `mods[i] = rests[i] ? 1 : 0`.

### 3.4 Moons and the always-on drum layer

- Moon grid 6 × 4; row → piece via `SynthBank.DrumForRow(row, 4)` (kick 36, snare 38, closed hat 42, open hat 46); columns → velocity `volume01 = 0.55 + 0.09 × x` (x = 0..5); column 5 additionally selects the alternate piece for rows 1-3 (clap 39 / open hat 46 / ride 51; row 0 kick fixed velocity 100..110). Kick is never humanised.
- Moon cubes get one window per bar (§2.10); `n` for their necklace = steps per bar exactly like everyone else, so a Moon cube with `hits = 4` on Eighth is "four on the floor".
- **Groove dice** template table (`Rhythm.Templates`), each row = (`hits`, `rot`, `step`) for rows 0..3 (kick, snare, hat, perc) in 4/4 (scaled by `BeatsPerBar / 4`): straight (4,0,Q · 2,1,Q · 8,0,E · 0) · shuffle (4,0,Q · 2,1,Q · 6,0,Tr · 3,1,Tr) · half-time (2,0,Q · 1,2,Q · 8,0,E · 2,3,E) · four-on-the-floor (4,0,Q · 2,1,Q · 8,1,E · 4,2,E) · breakbeat (5,0,S · 3,4,S · 7,0,E · 5,3,S) · waltz (3,0,Q · 2,1,Q · 6,0,E · 3,1,E, for 3/4). `hits = 0` ⇒ that row's cube is muted. Rolling the dice cycles templates deterministically from `Hash(seedSong, rollCount)`; each roll is one `History.Push`.
- **Energy coupling** (lit island's energy applies to the Moon during that island's window): 0 hush ⇒ only row 0 (kick) sounds, snare at ghost; 1 ⇒ hats at ghost weight; 2 ⇒ as drawn; 3 ⇒ hats ratchet ×2 on off-beats, Fill behaviour auto-armed for the island's last bar.
- Demo song (`MusicTheory.RandomSong`) and `StartNewSong` from the generator ship with one Moon carrying a 3-cube four-on-the-floor groove (`kick E(4,4)`, `snare E(2,4) rot 1`, `hat E(8,8)`), so the first thing heard has a beat. Drums remain drawable on chord islands for fills (per-island drum cubes: `DrumForRow(row, rows)`).

### 3.5 Mood and Climate (reversible view)

- `MeasureState.mood ∈ {0 as generated, 1 sun, 2 cloud, 3 storm}`, `SongState.climate ∈ {0, 1, 2, 3}`. Effective family = `mood != 0 ? mood : climate`. Effective quality = `MusicTheory.MoodMap(storedQuality, family)`, **row-count preserving**:

| stored → | sun | cloud | storm |
|---|---|---|---|
| Major7 | Major7 | Minor7 | Dominant7 |
| Minor7 | Major7 | Minor7 | Dominant7 |
| Dominant7 | Major7 | Minor7 | Dominant7 |
| Sus4 | Major7 | Minor7 | Sus4 |
| Major9 | Major9 | Minor9 | **Dominant9** (new: 0,4,7,10,14) |
| Minor9 | Major9 | Minor9 | Dominant9 |

- Applied in `KeyBlock.BuildFromData`: `semitoneList = MusicTheory.Semitones(MoodMap(QualityOf(data.semitones), family))` when `family != 0`, `chordColor` from the effective semis; `MeasureState.semis` is **never rewritten** (`MeasureState.From(KeyBlock)` reads `kb.storedSemis`, a new field holding the untouched data). Chord wheel edits write `semis` = `Semitones(wheelQuality)` (as today) and clear the island's `mood` to 0.
- Changing mood/climate = set the field, `RebuildFromState(Capture())` (preserves play state), `History.Push`. Path nodes never clamp because row counts are preserved.
- `MusicTheory` gains `ChordQuality.Dominant9`, `QualityOf` (contains 4, 10, 14), `Semitones`, `QualityIcon = "diamondDot"`, `ChordName = n + "9"`, and the wheel's `QualityCycle` gains it (7 entries).
- Climate also scales sends (`space × {1.0, 0.8, 1.0, 1.25}` for auto/sun/cloud/storm) and the sky tint (warm / violet / indigo). Per-slot patch variants per climate are P2.

### 3.6 Sections and form

- **Energy** (§3.1 #22): stored per island, read by `VoiceRules` (velocity), `Rhythm.MoonDensity` (Moon weights), `KeyBlock` (glow, ember rate), `Comet` arrival (beam height). No cutoff.
- **Fill** (#23): `AudioCube.Decide` consults `window.island.fill && lastBar(k)`: drum cubes' last two hits become `ratchet2`; pitched cubes get `echo = max(echo, 1)` for those hits; `SongManager` schedules the crash: `VoiceRules.DispatchCrash(nextDownbeatDsp)` = `Synth.Note(DrumSlot, 49, 96, dsp, dsp + 0.1, ownerSong)` with `ownerSong = 7` (reserved id).
- **Sleep** (#24): `window.silent` for the island's residents and Riders.
- **Repeat** (#25, M4): `RecomputeMeasureStarts` builds `PlayOrder = [i repeated repeat_i times]`, `MeasureStarts` per occurrence, `TotalBeats` = Σ; cubes get one window per occurrence; `ActiveMeasureIndex` returns the island of the occurrence; the strip shows the expanded order as one pill with N dots.
- **Parked islands** (P2): `MeasureState.parked` ⇒ excluded from `PlayOrder`, dim, silent, no windows; drag onto the wire joins. Not built in v2.

### 3.7 Performance layer (`Performance.cs`, static)

All four punch-ins are **hold** buttons in the transport (visible while playing) with keyboard shortcuts that do not touch `Z/X/C/V/S/Y` (§6.8). Press and release are quantised to the next 16th (`GlobalClock.DspTimeOfBeat(ceil16(SongBeat))`), applied by a coroutine that waits for that DSP time.

- **Stutter**: `VoiceRules.Dispatch` logs every dispatched `NoteEvent` in a 2-bar ring buffer. On engage, capture the events with `onDsp ∈ [t − one16th, t)`; every 16th while held re-issue them with `onDsp = tick`, `offDsp = tick + 0.6 × one16thSec`, `vel × 0.85`, `owner = 8` (reserved); `Performance.StutterActive = true` makes `VoiceRules.Dispatch` drop cubes' normal notes (visuals continue). Release: `Synth.CancelOwner(8)`, flag off.
- **Funnel**: `Synth.SetMasterCutoff01(lerp(tone, 0.15, t / barSec))` while held; on release, hold the value until the next downbeat then spring back to `tone` over 60 ms.
- **Half-time**: `storedBpm = GlobalClock.BPM; SetBPM(storedBpm / 2)` (phase-preserving); on release `SetBPM(storedBpm)` exactly (float equality asserted in tests). The tempo dial shows the halved number while held (the only number on screen, allowed).
- **Drop**: for every slot except Bass and Drums `Synth.SetSlotGain(slot, 0)`; on release `Instruments.PushToSynth()`. `Instruments.SetVolume` during a drop is deferred (queued, applied on release).
- **Spotlight** (hold): `Performance.SpotlightCube = selected`; `VoiceRules` scales other cubes' velocity × 0.25 and `AudioCube.UpdateVisuals` dims them; release clears. Never on hover.

---

## 4. Audio specification (on top of the Synth facade, unchanged)

Facade (as implemented in `DeaCube/Audio/SynthEngine.cs:475-547`): `Synth.Ensure()`, `Ready`, `NoteOn/NoteOff/Note(slot, midi, vel, onDsp, offDsp, int owner)`, `Preview(slot, midi, vel, seconds)`, `CancelOwner(int)`, `CancelPendingAfter(dsp, alsoSilence)`, `AllNotesOff(immediate)`, `SetSlotGain/Mute/Reverb/Chorus/Pan/Program`, `SetMasterGain(linear)`, `SetMasterCutoff01`. **Owner is an `int`; 0 = anonymous, never cancelled.** Reserved owner ids: `1..6` unused, `7 = song events (crash)`, `8 = stutter`, `9 = previews that must be cancellable` (default previews stay anonymous); cubes use `owner = Mathf.Abs(GetInstanceID()) + 16` (today's `AudioCube.cs:86` uses `+ 1`; change the offset to 16 so a cube can never collide with a reserved id).

### 4.1 Slots (exact `SynthBank.Defs` order, `SynthBank.cs:33-46`; never reordered)

| Slot | Name | bank:patch (GeneralUser GS) | Colour (palette) | Silhouette icon | Register (Defs) | Gate policy | Base velocity (Defs vmin..vmax) | Sends rev/cho (Defs) | Pan |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Keys | 0:4 Tine Electric Piano | blue (existing 0) | `keys` = `Box` with two short black-key `Box` stubs | 41-88 | follows gate | 40..110 | 0.35 / 0.30 | −0.15 |
| 1 | Pluck | 0:24 Nylon Guitar | red (existing 1) | `pluck` = `Ring(0.5)` + diagonal `Seg` pick | 40-84 | natural decay: off ≤ 2 steps | 40..110 | 0.40 / 0.10 | +0.25 |
| 2 | Pad | 0:89 Warm Pad | green (existing 2) | `pad` = three overlapping soft `Circle`s | 48-79 | sustaining: min 1 beat, Short → Normal, release one 16th before window end | 40..105 | 0.60 / 0.30 | 0 |
| 3 | Lead | 0:80 Square Lead | orange (existing 3) | `bolt` | 55-91 | mono (NoteOff previous on NoteOn); follows gate; Long = tie | 45..110 | 0.30 / 0.15 | −0.10 |
| 4 | Bass | 0:33 Fingered Bass, `octaveShift −1` | purple (existing 4) | `bass` = three heavy concentric `Ring`s | 28-55 | mono; Short → min 120 ms; downbeat root rule | 45..112 | 0.15 / 0 | 0 |
| 5 | Bells | 0:11 Vibraphone | yellow (existing 5) | `bell` = `Ring(0.6,0.1)` + `Circle(0.18)` | 60-96 | natural decay: off ≤ 2 steps; Long ignored | 40..110 | 0.60 / 0.15 | +0.30 |
| 6 | Strings | 0:48 String Ensemble 1 | teal `Hue(0.48)` | `strings` = three nested `Arc`s + `wave` | 43-88 | sustaining (as Pad) | 40..105 | 0.60 / 0.20 | −0.25 |
| 7 | Choir | 0:52 Choir Aahs | rose `Hue(0.92)` | `choir` = open `Ring` of five `dotSmall` | 48-84 | sustaining (as Pad) | 40..105 | 0.60 / 0.20 | +0.10 |
| 8 | Piano | 0:0 Acoustic Grand | ivory `(0.95,0.92,0.82)` | `piano` = `Box` with three `bar`s | 36-96 (48-96 after the low-end law) | follows gate | 40..112 | 0.35 / 0 | 0 |
| 9 | Drums | 128:0 Standard Kit, channel 9 | white | `kit` = four `Circle`s in a square | rows → pieces | one-shots, off = on + 0.1 s | 50..115 | 0.15 / 0 | 0 |

- `Instruments` (`Palette.cs`) becomes a 10-row code table: `Count = 10`, `Colors[10]` (slots 0-5 keep the six existing hues **in their existing index order**, read from the prefabs exactly as today for 0-5 and from the table for 6-9), `Icons[10]` (silhouette names above), `SynthSlot = {0,…,9}` (identity), `Volume[10] = 1`, `Muted[10]`, `Pan[10]` from the table; `LoadFrom` stays idempotent; `Clips/Groups` remain for the no-SoundFont fallback (only 0-5 have clips; 6-9 fall back to clip 0 pitched).
- **Legacy remap**: `SongState.IsLegacy` (`version < CurrentVersion`, landed on disk) ⇒ `instrument` unchanged (identity, because the in-flight integration already bound index i → slot i and the six colours keep their index order), so old saves load looking and sounding as the v2 roles of the same colours. A future reordering gets one hook: `Instruments.RemapLegacy(int inst)` in `Palette.cs` (identity today).
- Every `Instruments.Count`-sized loop is updated: `Palette.cs:34-39` (arrays), `PathManager.HandleHotkeys` (`Alpha1..Alpha9, Alpha0` → 0..9), `UIManager.BuildPalette` (2 × 5), `BuildCubePanel` colours (10 swatches), `AudioCube.SetInstrument` clamp, `SongState.instVolume/instMuted` (padded to 10 on restore: `Restore` already tolerates short arrays).
- Per-slot pan/reverb/chorus are pushed once after `Synth.Ensure()` (`Instruments.PushToSynth` gains `SetSlotPan/Reverb/Chorus` from the table × `space`).

### 4.2 How AudioCube schedules (replacing today's `TrySchedule` body)

```csharp
// AudioCube (owner: WP-A). Called from UpdatePlayback's lookahead loop for every step k of window w
// whose start falls inside the 0.22 s lookahead, and once for the first step of the next window.
void TrySchedule(int w, int k, double onDsp)
{
    if (onDsp > GlobalClock.DspNow + Lookahead) return;
    if (onDsp <= lastScheduledDsp + 1e-4) return;        // monotonic guard: one decision per step
    lastScheduledDsp = onDsp;
    int pass = GlobalClock.LoopIndex * 64 + windows[w].order;
    Hit hit = Decide(w, k, pass);
    if (!hit.fires) return;
    TileInteraction tile = TileAt(w, hit.node);          // resident: nodes[node]; rider: island.GetTile(xs[node], min(zs[node], rows-1)); moon: nodes[node]
    int stack = SequenceMaster.CountOn(tile, this);       // other cubes on that tile (stack slot known 0.22 s ahead)
    double stepSec = StepBeats / GlobalClock.BeatsPerSecond;
    double windowEndDsp = GlobalClock.DspTimeOfBeat(windows[w].start + windows[w].length);
    double nextHitDsp = NextHitDsp(w, k, pass);          // for ties; +inf if none in this window
    int n = VoiceRules.Resolve(this, windows[w], tile, hit, stack, pass, onDsp, stepSec, windowEndDsp, nextHitDsp, events);
    VoiceRules.Dispatch(events, n, owner);               // the only place that calls Synth.Note for cubes
}
```

- `Land(w, k)` for visuals calls the same `Decide(w, k, pass)` and uses `hit.fires / slip / weight / ratchet / lift` for squash, flash, pillar height (`1.3 + 1.1 × weightScale × volume × energyScale`), ratchet mini-bounces and the double-height arc for lift. Audio and visuals cannot disagree because both read one pure function seeded by `(pass, node, seed)`.
- `NextHitDsp` scans steps `k+1..` of the same window through `Decide` (bounded by `N`), returning the first firing step's DSP time.
- `CancelPending()` → `Synth.CancelOwner(owner)` + `lastScheduledDsp = −1` (as now). `HandleStop` additionally resets `lastStep`, `windowIdx`.
- The no-SoundFont fallback (`Fallback()` AudioSource, `AudioCube.cs:483-513`) stays as the last-resort path, one source per cube, created lazily, `playOnAwake = false`; it plays `events[0]` only.
- `BuildVoices/UpdateVoices` and the four AudioSources per cube are already gone (integration patch); the five prefabs `Red/Green/Orange/Purple/YellowCube.prefab` get `m_PlayOnAwake: 0` (YAML edit) so no spurious sample fires on spawn; `SpawnCube` keeps instantiating `cubePrefabs[min(instrument, 5)]` and recolours (`ApplyInstrumentLook`) for slots 6-9. The seven `.wav` importers are set to PCM (`compressionFormat: 0`) or the WAVs deleted once the fallback is judged unnecessary (M2 decision).

### 4.3 `VoiceRules.Resolve` — the single audio decision point (new file `DeaCube/VoiceRules.cs`, WP-A)

```csharp
public struct NoteEvent { public int slot, midi, vel; public double onDsp, offDsp; }
public static int Resolve(AudioCube cube, AudioCube.Window w, TileInteraction tile, AudioCube.Hit hit, int stack, int pass,
                          double onDsp, double stepSec, double windowEndDsp, double nextHitDsp, NoteEvent[] outEvents)  // returns count
```

Order of rules (all pure; `Rhythm.Hash(pass, node, seed)` is the only randomness):

1. **Slot & drums**: `slot = Instruments.SlotOf(cube.instrument)`. Drums: `midi = SynthBank.DrumForRow(tile.gridZ, island.rows)` (column 5 ⇒ alternate piece for rows 1-3), `vel` from column `volume01 = 0.55 + 0.09·x`, kick fixed `100 + 10·accent`, `offDsp = onDsp + 0.1`, no humanise on kick, skip rules 2-8.
2. **Pitch**: `midi = tile.midi + SongManager.Transpose + 12·cube.octave + (hit.lift ? 12 : 0)`.
3. **Stack**: if `stack ≥ 1` and this cube is the **top** of the stack (`SequenceMaster.IndexOn(tile, cube) == stack`), `midi += 12` (max +24 total with lift/octave) and `velScale × (1 + 0.25·log2(stack + 1))`.
4. **Bass anchor**: slot Bass and `hit` lands on an integer downbeat (`k`'s local beat ≡ 0 mod BeatsPerBar) and `tile.role ∈ {Seventh, Extension}` ⇒ `midi = island.chordRootMIDI + Transpose` (root); root/3rd/5th tiles play as drawn. Bass is mono: `Dispatch` issues `NoteOff(prev)` at `onDsp` first.
5. **Register**: `midi = SynthBank.ClampToRegister(slot, midi)`; **low-end law**: for every slot except Bass, while `midi < 48` ⇒ `+12`; for every slot except Bells, while `midi > 96` ⇒ `−12`.
6. **Costumes**: Pad ⇒ add the two chord tones **below** the tile (walk the island's rows downward from `tile.gridZ − 1`, wrapping to the top row −12) at `vel × 0.65`, same on/off. Shadow (`follow`) ⇒ nearest row above/below with `|midi − tile.midi| ≥ 3` at `vel × 0.6`. Echo ⇒ repeats at `onDsp + i·stepSec` (`i = 1..echo`) at `vel × 0.65^i`, only if `onDsp + i·stepSec < windowEndDsp`, shimmer ⇒ each repeat uses the next shadow row up. Ratchet ⇒ `r` sub-hits at `onDsp + j·stepSec / r` with `vel × {1, 0.82, 0.68}[j]`.
7. **Velocity**: `volume01 = cube.volume × Instruments.Gain(instrument) × energyScale[island.energy] × (hit.weight == 0 ? 0.45 : 1) × (Performance.SpotlightCube != null && != cube ? 0.25 : 1)`; `accent01 = beatWeight (downbeat 0.30, other beat 0.12, off-step 0) + (hit.weight == 2 ? 0.35 : 0)`; `vel = SynthBank.Velocity(slot, volume01, accent01) × velScale`, humanise `+ ((Hash >> 8) % 13) − 6` except kick, clamp 1..127.
8. **Timing & gate**: `jitter = (Hash & 0xFF) × 8 ms / 255` (never early), `onDsp += jitter`; `gateSec = Short 0.3·stepSec, Normal 0.92·stepSec, Long = (nextHitDsp − 0.01) − onDsp`; slot policy: Bass min 0.12 s; Pad/Strings/Choir min 1 beat and Short → Normal; Pluck/Bells/Piano max 2 steps; Lead Long = tie. **Clamp**: `offDsp = min(onDsp + gateSec, windowEndDsp − 0.005)`, and for Pad/Strings/Choir `min(…, windowEndDsp − one16thSec)`. A tie whose next hit is in the next window ends at the window end.
9. **Energy 0** silences Pad/Strings/Choir on that island (returns 0 events).

`Dispatch(events, n, owner)`: for each event `Synth.Note(slot, midi, vel, onDsp, offDsp, owner)`; for mono slots (Lead, Bass) `Synth.NoteOff(slot, lastMidi[slot][owner], onDsp, owner)` first; logs into `Performance`'s ring buffer; returns early (no `Synth` calls) when `Performance.StutterActive`.

### 4.4 Master chain

- `Synth.SetMasterGain(2.2)` (+6.8 dB; the SF2 peaks at −11 dBFS) into the engine's soft limiter (`SynthEngine.LimiterKnee = 0.7`) — already wired in Boot.
- The engine's host `AudioSource` is routed to `MainMixer/Master` (`SynthEngine.Awake`: `src.outputAudioMixerGroup = Instruments.Groups[0]`, which the BlueCube prefab provides today; if null, stay direct). In `MainMixer.mixer`: **remove the SFX Reverb** on Master (no double reverb) and **add a Compressor** (threshold −8 dB, attack 20 ms, release 200 ms, make-up +2 dB). MeltySynth's own reverb + chorus stay on with the per-slot sends of §4.1 scaled by `space`.
- `Synth.SetMasterCutoff01(tone)` from the Tone dial; the funnel punch-in sweeps it temporarily.
- Metronome (`GlobalClock.cs:170-192`) and UI ticks (`AudioPool`) stay on AudioSources; `AudioListener.volume` remains the master dial.

### 4.5 Slot volume / mute wiring

`Instruments.SetVolume(i, v)` → `Synth.SetSlotGain(SlotOf(i), v)`; `SetMuted` → `SetSlotMute` (already implemented in the integration patch, `Palette.cs:65-66`), so a slider or mute acts immediately on sounding notes. Per-cube `volume` and `muted` go through `VoiceRules` (velocity) and `Decide` (fires), as today.

### 4.6 Previews (what you click is what the cube would play)

`TileInteraction.PlayPreview(instrument)` → `VoiceRules.PreviewEvent(slot, tile, octaveOfSelectedCube)` (same register fold, bass fold, drum piece for the row, pad costume **not** applied) → then:

- clock stopped ⇒ `Synth.Preview(slot, midi, vel, 0.35)` immediately;
- playing and `tile.island` is the lit island (or a Moon, or the slot is Drums) ⇒ `Synth.Note(slot, midi, vel, DspTimeOfBeat(next16th), +0.35 s, owner 9)`;
- playing and the tile's island is **not** lit ⇒ no pitched sound: `tile.Flash` + `Fx.Pillar` + `AudioPool.UI(ProceduralAudio.Tick(), 0.3)`.

Hovering a palette swatch auditions its sound once: when stopped at the slot's register centre (`Synth.Preview(slot, ClampToRegister(slot, 60), 80, 0.3)`); while playing on the next 16th using the **lit island's root** (`ClampToRegister(slot, Islands[LitIsland].chordRootMIDI + Transpose)`) so palette auditions are always in chord; Drums audition the kick.

### 4.7 Fallback when `Synth.Ready == false`

Boot logs "SoundFont missing: falling back to samples" (already). `AudioCube` uses the single lazily-created fallback `AudioSource` with the legacy pitched clip and the hard gate; `TileInteraction.PlayPreview` uses `AudioPool` (already); Moons are silent (no kit clips) and the Moon button shows a `warning` toast; punch-ins funnel/drop are no-ops; everything else (world, necklace, stickers) works. `Synth.Ready` is re-checked each call, never cached.

### 4.8 The VST answer (ship verbatim to the user)

> Unity cannot host VST or AU plugins at runtime: there is no plugin host in the engine, the Native Audio Plugin SDK only lets you compile your own C++ DSP into a mixer effect, and shipping third-party synths inside a game is neither technically nor legally practical (desktop-only, macOS notarisation and library-validation exceptions, VST3 GPL/proprietary licensing, a missing plugin means silence, a crashing plugin takes the whole game down, and no plugin GUI can appear inside the scene). The game-native equivalent of "plugging in better instruments" is exactly what v2 ships: a real-time SoundFont synthesizer inside the game (MeltySynth + GeneralUser GS) — multisampled, velocity-layered instruments with real releases, a bass and a drum kit, 64 voices, about 6 % of one core, zero gameplay cost. If you want one specific VST timbre, render it offline in your DAW every 3 semitones × 3 velocity layers, assemble it into a second `.sf2` with Polyphone, and drop it into `StreamingAssets` — no code change. Bigger free banks (FluidR3, MuseScore General) are likewise a file swap.

---

## 5. Data model changes (`DeaCube/SongState.cs`, WP-E, landed first)

JsonUtility rules: missing fields keep the field initialiser default; missing arrays are `null` (treat `null` and wrong-length arrays as "unset"); no nullable ints, so sentinels are used (`−1` = unset). Every new field is public, serialisable, and copied by `Clone`.

```csharp
[Serializable] public class MeasureState {
    // existing
    public string chordKey; public int root; public int[] semis; public int bars = 1;
    // v2 world
    public float px, pz;          // island root position on the sea (transform.position.x/z)
    public bool placed;           // false ⇒ legacy row layout on build; Capture writes true
    public int kind;              // 0 chord island, 1 Moon (Moons live in SongState.moons, kind kept for symmetry)
    // v2 sections
    public int mood;              // 0 as generated, 1 sun, 2 cloud, 3 storm (per-island override of climate)
    public int energy = 2;        // 0..3
    public bool fill;             // ratchets + crash in the last bar
    public bool sleep;            // silent island (breath)
    public int repeat = 1;        // 1..4 (P1)
    // (P2, not on disk: bool parked)
}
[Serializable] public class CubeState {
    // existing
    public int instrument; public int measure; public int[] xs; public int[] zs; public bool[] rests;
    public int step = 1; public int gate = 1; public int mode = 0; public float volume = 1f; public bool muted;
    // v2
    public int moon = -1;         // ≥ 0 ⇒ the cube lives on SongState.moons[moon] and `measure` is ignored
    public int[] mods;            // per node: 0 none 1 rest 2 ghost 3 accent 4 ratchet2 5 ratchet3 6 coin 7 tie 8 lift; null ⇒ from rests
    public int hits = -1;         // -1 = every step (n), -2 = explicit mask, 1..n Euclid
    public int rot;               // necklace rotation
    public int mask;              // explicit per-bar hit bits (bit j = step j), used when hits == -2
    public int octave;            // -1..+1
    public bool reverse;          // walk the path backwards
    public int phase;             // hit-index offset for twins
    public int follow;            // 0 off, 1 above, 2 below, 3 both
    public int echo;              // 0..2 repeats
    public bool shimmer;          // echo climbs a shadow row per repeat
    public bool rider;            // pitched only; replays on every Route island
    public int seed;              // for coin stickers and the dice walk; 0 ⇒ assigned from a hash of (measure, xs, zs) on load
    public int twinOf = -1;       // cosmetic link to the source cube's id (visual braid), -1 none
    public int id;                // stable per-cube id (assigned on first Capture if 0), used for twinOf and Synth owner mapping
}
[Serializable] public class SongState {
    // existing
    public string name; public float bpm = 100f; public int beatsPerBar = 4; public float swing; public bool loop = true;
    public int transpose; public MeasureState[] measures; public CubeState[] cubes; public float[] instVolume; public bool[] instMuted;
    // v2
    public int version;           // 0 = legacy (v1), 2 = this spec; Capture writes 2
    public MeasureState[] moons;  // 0..2 Moons (px, pz, energy ignored; kind = 1)
    public int climate;           // 0 as generated, 1 sun, 2 cloud, 3 storm
    public float tone = 1f;       // master cutoff 0.15..1
    public float space = 1f;      // send scale 0..1.6
    public int demoSeed;          // seed of the generated song (for the Moon groove dice and dice walks)
    public bool IsLegacy => version < CurrentVersion;   // gates the row layout, mods derivation and identity instrument remap
    // (P2, not on disk: int[] slotPatch)
}
```

**Status**: this schema is already landed on disk (`DeaCube/SongState.cs`, `CurrentVersion = 2`, `IsLegacy`, `CubeState.ModsOrDerived()`, `CubeState.SeedOrDerived()`, `Capture` reads `SongManager.Moons`, `SongManager.Climate/Tone/Space` and `sm.demoSeed`, `MeasureState.From(KeyBlock)` reads `kb.storedSemis`); WP-E owns it from here.

Backward compatibility, in `SongManager.RebuildFromState` / `AudioCube.ApplySettings` / `PathManager.RestoreCube`:

- `IsLegacy`: `instrument` identity (`Instruments.RemapLegacy`); `instVolume/instMuted` of length 6 restored into the first 6 of 10; `measures[i].placed == false` ⇒ row layout; `mods == null` ⇒ `ModsOrDerived()` derives from `rests`; `hits == -1` ⇒ all steps; `moons == null` ⇒ no Moons (no auto-added Moon for old saves; the Moon is added only to newly generated songs); `seed == 0` ⇒ `SeedOrDerived()` = `Rhythm.Hash(measure, xs[0], zs[0])`.
- `Capture()` writes every field, `version = 2`, `rests[i] = mods[i] == 1`, `placed = true`, ids assigned.
- A **v1 fixture** `Assets/DeaCube/Tests/v1_song.json` (captured from the current build before any schema change) is checked in and asserted to load with the same cube count, tiles and rests (§8, WP-E verification).
- `MeasureState.From(KeyBlock)` reads `kb.storedSemis` (untouched data), not the mood-mapped `semitoneList`; `ToData` carries `px, pz, placed, mood, energy, fill, sleep, repeat` through a widened `SongManager.MeasureData` (same field names) so `BuildIslands` can read them.

---

## 6. HUD changes per panel (`UIManager.cs`, WP-D)

The layout the user likes stays: top-left sparkle, top overview strip + measure bar, left tools, bottom palette, bottom-centre transport, right sliding cube panel, modal chord wheel, toast. Only BPM is a number (plus the halved BPM while half-time is held).

| Panel | Exact controls after v2 | Removed / moved | Hotkeys |
|---|---|---|---|
| Top-left | `sparkle` new song (exists) | – | – |
| Overview strip | `plus` add island; pills (chord colour, quality glyph, `polyN` hub shape, energy fill height, hollow when asleep) in Route order with the playhead; gap; Moon pills (`kit`); `kit` button = add Moon (max 2). Hover link pill↔island; on-screen underline; **drag-to-reorder** (P1) | `dotsN` bars glyph on pills → `polyN` | `A/D` previous/next island |
| Measure bar (selected island) | chord button (`QualityIcon`, opens the wheel), `moodBtn` (`ringThin`/`sun`/`cloud`/`storm`, click cycles), `energy` flame (click cycles 0..3, drag sets), `flourish` fill toggle, `moon` sleep toggle, `polyN` bars (click cycles), `repeat2/4` (P1), `duplicate`, `close` | `minus`/`plus` around `dotsN` (→ the hub / `polyN` click) | – |
| Island halo (world, around the hub, opens on hub click, ≤ 6) | `wedge`-wheel opener, mood, energy, fill, duplicate, trash(hold) | – | Esc closes |
| Moon halo (world, ≤ 4) | `dice` groove, energy-follow indicator (display), `trash`(hold), drag handle | – | – |
| Tools | `undo`, `redo`, `save`, `load`, `trash` (hold), `target` follow-comet toggle, `frame` frame-all, **pencil ring** `pencil` (hold-open: `stairsUp`, `stairsDown`, `zigzag`, `mountain`, `dice`) (P1), `record` (P1), `stamp` (click = stamp to hovered island, hold = stamp to all) | – | `Cmd+Z`, `Cmd+Shift+Z`, `Cmd+Y`, `Cmd+S`, `F`, `O`, `Q` |
| Palette | **2 × 5** swatches (colour + silhouette `Icons[i]`, 44 px), `speaker/mute` and vertical slider per slot (kept), hover audition | six identical `cube` glyphs | `1..9, 0` |
| Transport | `rewind`, `play/pause`, `stop`, `loop`, tempo dial (number, tap), beat dots, `climate` dial (4 states), `eclipse` tone dial, `waves3` space dial, **punch-in strip** (`bolt`, `funnel`, `half`, `dropOut`; shown while playing), **`chevronD` "more"** toggle revealing `wave` swing, `note` transpose, `dotsN` meter, `metronome`, `speaker` master volume | swing/transpose/meter/metronome move under "more" (still there, still visible when opened) | `Space/P`, `Home`, `L`, `M`; hold `J` stutter, `K` funnel, `H` half-time, `B` drop |
| Cube panel (slides in on selection) | row 1: `speaker/mute`, level slider, `spot` spotlight (hold), `trash`; row 2: 10 colour swatches (2 × 5, silhouettes); row 3: steps `slices1/2/3/4/8` (5); row 4: **`NecklaceDial`** (n beads, drag = hits, notch drag = rotate, click bead = toggle) ; row 5: `octDown` `octUp` · `twin` (hold: sub-ring) · `shadow` · `echo` (hold: shimmer) · `comet` rider (hidden for Drums); **`chevronD` "more"** row 6: gates `gate0/1/2`, modes `modeLoop/PingPong/Once`, `stamp`; expanded state remembered in `PlayerPrefs` | – | `[` `]` rotate, `-` `=` hits, `↑` `↓` octave, `T` twin, `R` rider, `W` shadow, `E` (hold) spotlight |
| Node sticker ring (world) | 7 glyphs (§3.3) at 34 px around the held bead; click = rest toggle unchanged | six-state click cycling never exists | – |
| Chord wheel (modal) | 12 wedges (exist), centre quality cycles 7 qualities (adds `diamondDot` Dominant9) | – | – |
| Toast | as today | – | – |

Key collisions avoided: no `Z/X/C/V/S/Y` for holds or rings (`Cmd+Z/Cmd+S` muscle memory), no `Tab`, no double-click-only functions, no scroll over world objects (the wheel is zoom everywhere in the world; `HudDial` scroll stays on HUD dials only).

New `UIKit` pieces: `NecklaceDial` (lays out `n` bead `Image`s on a ring of radius 34 px, bead size `clamp(220 / n, 6, 14)` px; drag distance = hits; drag on the notch = rotate; click bead = toggle), `PopRing` (world-anchored ring of `HudButton`s at a screen point, `LateUpdate` positioning, safe-area clamp, fade when off screen), `HoldButton` = `HudButton` with `onHoldStart/onHoldEnd` (punch-ins, spotlight).

New icons (`IconFactory.Shape`, all SDF from primitives): `necklace, keys, pluck, pad, bolt, bass, bell, strings, choir, piano, kit, cloud, storm, climateAuto, climateSun, climateCloud, climateStorm, flame, flourish, moon, repeat2, repeat4, polyN (parametric, N = 3..6), ghost, accent, ratchet2, ratchet3, coin, tie, lift, octUp, octDown, twin, shadow, echo, comet, stamp, stairsUp, stairsDown, zigzag, mountain, record, eclipse, waves3, funnel, dropOut, spot, chevronU, diamondDot, pencil`. Existing kept: `dice, half, sun, wave, note, speaker, mute, target, frame, gate0/1/2, mode*, slicesN, dotsN, wedgeN, rest, sparkle, square, ring, diamond, dot`.

---

## 7. Fx changes (`DeaCube/Fx.cs` + shaders, WP-C)

- `Fx.BeatBump(float amount = 0.2f)`: bloom intensity `0.9 → 0.9 + amount` decaying over 100 ms (`Volume` profile Bloom override cached in `SetupEnvironment`).
- `Fx.SkyBeam(Vector3 pos, Color c, float height)`: a `Pillar` with height 9-12 u, width 1.6, dur 0.9, slower fade curve so overlapping beams show polyphony; called by `Comet` on arrival and by `KeyBlock.HandleBeat` on the lit island's downbeats (height from energy).
- `Fx.SeaRipple(Vector3 pos, Color c)`: `Ripple` scaled 3.5, dur 0.8, on the sea plane `y = −4` (per beat from the lit island; downbeat larger).
- `Fx.Starburst(Vector3 pos, Color c)`: pooled quad with a new 36-spoke `IconFactory` soft texture `"burst"`, rotating 90° and scaling 0.5→1.6 over 0.4 s (Mix Universe's node hit); spawned by `Comet` arrival and by `AudioCube.Land` on accent hits.
- **Sea plane**: additive quad at `y = −4`, 600 u, two scrolling noise layers (`IconFactory` `"noise"` soft texture), tinted by `skyTintTarget`.
- **Pillar defaults** in `AudioCube.Land`: height `1.3 + 1.1 × weightScale × volume × energyScale`, dur 0.7 s, width `0.35 + 0.3 × weight`.
- **Shader** `DeaCube/AdditiveVertex` (copy of `DeaCube/Additive` multiplying by vertex colour) for the segmented beat ring and the Moon ring.
- **Ember dust**: the dust particle rate on the lit island scales with energy (`Fx.SetEmberRate(rate)` called by `KeyBlock` when energy changes or the lit island changes).
- **Sky tint**: `DriveSkyTint` (`Fx.cs:232-240`) now blends the active island's colour with the climate tint (warm `(1,0.85,0.6)`, violet, indigo) at 0.35.
- Existing `IslandCelebrate`, `StartSweep`, `LoopSweep`, `Ripple`, `Pillar`, `Burst`, `Pop` unchanged.
- `Fx.SetSongBounds` also includes Moons (`SongManager.SongBounds` encapsulates `Moons`).

---

## 8. Implementation plan — five parallel work packages with file ownership

Rules for every engineer: shell commands use **absolute paths** (`/Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Assets/...`); `execute_code` is **C# 6** (no `out var`, tuples, local functions, pattern matching, `default` literals; expression-bodied members, `?.`, `nameof`, `$""` are fine); overlay UI is **not visible in MCP screenshots** — capture with `ScreenCapture.CaptureScreenshot("/Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Captures/<name>.png")` with the editor window focused (the player loop freezes unfocused) and assert HUD state through `RectTransform`/component queries; `Synth.*` is called from the main thread only; every `SongState` write ends in `History.Push()`; every package leaves the project compiling and playable at the end of every day; a file has **exactly one owner** and other packages request changes to it through its owner (interfaces below are the contract, agreed before coding).

### 8.0 File ownership (complete)

| Owner | Files (M = modified, N = new) |
|---|---|
| **WP-E State & theory** | M `DeaCube/SongState.cs`, M `DeaCube/MusicTheory.cs`, M `GlobalClock.cs`, M `SequenceMaster.cs`, M `DeaCube/DeaCubeBoot.cs`, N `DeaCube/Rhythm.cs`, N `DeaCube/PathGen.cs`, N `DeaCube/Tests/v1_song.json`, N `DeaCube/Tests/V2Checks.cs` (editor-only assertions callable from `execute_code`), `Scenes/SampleScene.unity` (only if a scene flag must change; none planned) |
| **WP-A Audio & rules** | M `AudioCube.cs`, M `DeaCube/Palette.cs`, M `TileInteraction.cs`, M `DeaCube/Audio/SynthEngine.cs` (mixer routing only), M `DeaCube/Audio/SynthBank.cs` (none planned; values only if a preset is wrong), N `DeaCube/VoiceRules.cs`, N `DeaCube/Performance.cs`, `MainMixer.mixer` (editor edit), the five `*Cube.prefab` (`m_PlayOnAwake: 0`), the seven `.wav.meta` (PCM) |
| **WP-B World & time** | M `SongManager.cs`, M `KeyBlock.cs`, M `PathManager.cs`, M `ProjectConfig.cs`, M `DeaCube/MeshFactory.cs`, N `DeaCube/Route.cs`, N `DeaCube/Comet.cs`, N `DeaCube/IslandDrag.cs` |
| **WP-C Camera & Fx** | M `OrbitCamera.cs`, M `DeaCube/Fx.cs`, N `DeaCube/Resources/Shaders/DeaCube_AdditiveVertex.shader`, N `DeaCube/Onboarding.cs` (M4) |
| **WP-D HUD** | M `UIManager.cs`, M `DeaCube/UIKit.cs`, M `DeaCube/IconFactory.cs`, M `InterfaceController.cs` (only if the prompt overlay needs the new-song Moon toggle; none planned) |

Untouched: `SongGenerator.cs` (except WP-E adding `demoSeed` and the Moon to `Validate`'s output — assigned to WP-E), `HarmonicLibrary.cs`, `AudioGridManager.cs`, `ProceduralAudio.cs`, `Ease.cs`, `MeltySynth/*`.

### 8.1 Milestones (dependency order)

- **M0 (day 1, WP-E alone)**: schema + pure helpers landed, project playable, v1 fixture captured **before** the change. Every other package starts from this commit.
- **M1 (days 2-5, WP-A ∥ WP-D ∥ WP-C-part1 ∥ WP-B-part1)**: ten roles, VoiceRules, necklace, stickers, octave/twin/shadow/echo, cube panel v2, palette 2×5, icons; world positions + drag + hub + cable + comet + segmented ring; camera leash + Fx pulse. Playable: sounds produced, world free, rhythm legible.
- **M2 (days 6-8)**: windows list, Moons, Riders, energy/fill/sleep, mood/climate view, punch-ins/spotlight, island halo, Moon halo, demo song with a Moon. Playable: full v2 P0 set.
- **M3 (days 9-10)**: integration polish, verification pass, captures, migration checks, performance profile (≤ 20 cubes, 2 Moons, 64 voices).
- **M4 (P1, days 11-14)**: repeats, play-it-in, shape stamps/dice walk, pill drag-reorder, wordless onboarding.

### 8.2 WP-E — State, theory, clock, pure rhythm (≈ 520 lines; M0 then support)

Owned files: see 8.0. **Exposes**:

```csharp
// SongState.cs — fields of §5; plus
public class SongState { public const int CurrentVersion = 2; public bool IsLegacy { get; } }   // landed; Capture writes version = 2, placed = true, ids
public class CubeState { public int[] ModsOrDerived(); public int SeedOrDerived(); }       // landed helpers used by AudioCube.ApplySettings
// MusicTheory.cs
public enum ChordQuality { Major7, Minor7, Dominant7, Sus4, Major9, Minor9, Dominant9 }
public static ChordQuality MoodMap(ChordQuality stored, int family);           // §3.5 table; family 0 = identity
public static int[] EffectiveSemis(int[] storedSemis, int mood, int climate); // Semitones(MoodMap(QualityOf(stored), mood != 0 ? mood : climate)), family 0 ⇒ stored
public static SongManager.SongData RandomSong(int seed, string name = "");    // now also fills SongData.demoSeed and SongData.moons (one Moon, §3.4)
// GlobalClock.cs
public static int LoopIndex;                     // 0 on Stop and on Seek(0); ++ inside the OnLoop branch (GlobalClock.cs:75-84)
public static double Next16thBeat();             // ceil(SongBeatD * 4) / 4 (or SongBeatD when stopped)
public static double DspOfNext16th();            // DspTimeOfBeat(Next16thBeat())
// Rhythm.cs (pure static)
public static bool[] Bjorklund(int k, int n);                          // E(k,n), hit at index 0 always when k ≥ 1
public static bool[] Pattern(int hits, int rot, int mask, int n);      // §3.2 (hits -1 ⇒ all, -2 ⇒ mask)
public static uint Hash(int a, int b, int c);                          // FNV-1a + avalanche (moved from AudioCube, identical output)
public static float Hash01(int a, int b, int c);
public struct Template { public string name; public int[] hits, rot, step; }
public static readonly Template[] Templates;                           // §3.4 six grooves, 4 rows each
public static float EnergyScale(int energy);                           // {0.6, 0.8, 1.0, 1.15}
public static int MoonWeight(int energy, int row, bool offBeat);       // §3.4 density rules → weight 0 ghost / 1 / 2 / -1 silent
// PathGen.cs (pure static, M4)
public static List<Vector2Int> Stairs(KeyBlock kb, Vector2Int start, bool up, int len);
public static List<Vector2Int> Zigzag(KeyBlock kb, Vector2Int start, int len);
public static List<Vector2Int> Mountain(KeyBlock kb, Vector2Int start, int len);
public static List<Vector2Int> Walk(KeyBlock kb, Vector2Int start, int len, int seed);   // adjacency-respecting seeded walk
public static List<Vector2Int> Stamp(CubeState src, KeyBlock target);                     // offsets relative to node 0, rows clamped
// SequenceMaster.cs
public static class InputUtil { /* + */ public static bool Held(KeyCode k); }   // hotkey ownership: transport keys (Space/P/Home/L/M) stay in SequenceMaster; WP-B owns editing keys in PathManager; WP-D owns HUD hold keys
```

**Consumes**: nothing new. **Steps**: (1) capture `v1_song.json` from a build *without* the new fields (check out `SongState.cs` from `git stash`/HEAD into a temp copy if needed, or hand-write the fixture in the v1 format: measures with chordKey/root/semis/bars, cubes with instrument/measure/xs/zs/rests/step/gate/mode/volume/muted, six instVolume/instMuted) and copy it into `Assets/DeaCube/Tests/`; (2) schema fields + `Clone/From/ToData` (**done on disk**); (3) `version`/`IsLegacy` (**done**); (4) `Rhythm`, `MoodMap`, `Dominant9`; (5) `LoopIndex`, `Next16thBeat`, `DspOfNext16th`; (6) `RandomSong` Moon + `demoSeed`; (7) `V2Checks.cs` with static assertion methods.

**Verification (execute_code, C# 6)**:
```csharp
var j = System.IO.File.ReadAllText("/Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Assets/DeaCube/Tests/v1_song.json");
var st = SongState.FromJson(j); SongState.Apply(st);
Debug.Assert(SequenceMaster.Cubes.Count == st.cubes.Length, "v1 cubes restored");
Debug.Assert(SongState.Capture().version == 2, "capture writes v2");
var e = Rhythm.Bjorklund(3, 8); Debug.Assert(e[0] && e[3] && e[6] && !e[1] && !e[2] && !e[4] && !e[5] && !e[7], "E(3,8)");
var p = Rhythm.Pattern(3, 1, 0, 8); Debug.Assert(p[1] && p[4] && p[7], "rotation 1");
Debug.Assert(MusicTheory.MoodMap(ChordQuality.Major9, 3) == ChordQuality.Dominant9 && MusicTheory.Semitones(ChordQuality.Dominant9).Length == 5, "row count kept");
Debug.Assert(Rhythm.Hash(1,2,3) == Rhythm.Hash(1,2,3) && Rhythm.Hash(1,2,3) != Rhythm.Hash(1,2,4), "hash");
```
Also: `JsonUtility.ToJson(new CubeState())` contains `"hits":-1`, `"moon":-1`; `Rhythm.Templates.Length == 6`.

### 8.3 WP-A — Audio, roles, resolver, performance (≈ 1,350 lines; M1 core, M2 windows/Moons/Riders/punch-ins)

Owned: `AudioCube.cs` (whole file), `Palette.cs`, `TileInteraction.cs`, `VoiceRules.cs` (N), `Performance.cs` (N), `SynthEngine.cs` (routing line), mixer, prefabs, wav metas.

**Exposes**:
```csharp
// Palette.cs
public static class Instruments { public const int Count = 10; public static readonly Color[] Colors; public static readonly string[] Icons; public static readonly int[] SynthSlot; public static readonly float[] Pan;
    public static int SlotOf(int i); public static bool IsDrums(int i); public static void PushToSynth(); /* + existing Volume/Muted/Gain/SetVolume/SetMuted/ToggleMute/Restore */ }
// AudioCube.cs
public enum StepLen { Half = 0, Quarter = 1, Eighth = 2, Sixteenth = 3, Triplet = 4 }
public struct Window { public float start, length; public KeyBlock island; public int order; public bool silent; }
public struct Hit { public bool fires; public int node; public int weight; public int ratchet; public bool tie, lift, slip; }
public readonly List<Window> windows;                 // filled by SongManager.RecomputeMeasureStarts via SetWindows
public void SetWindows(List<Window> w);               // resets lastStep/windowIdx, CancelPending
public Hit Decide(int windowIndex, int k, int pass);  // pure (§3.2)
public int StepsPerBar { get; }                        // n
public List<int> HitSteps16(int windowIndex, int pass);// 16th indices (0 .. bars*BeatsPerBar*4) of firing hits, for the ring pre-light (WP-B) and the strip
public int owner;                                     // Synth owner id = Mathf.Abs(GetInstanceID()) + 16 (≥ 16, never a reserved id)
public int hits, rot, mask, octave, follow, echo, phase; public bool reverse, shimmer, rider; public int seed; public List<int> mods; public KeyBlock Moon; // + setters:
public void SetHits(int k); SetRot(int r); SetMask(int m); SetMod(int node, int mod); SetOctave(int o); SetFollow(int f); SetEcho(int e); SetShimmer(bool); SetReverse(bool); SetPhase(int); SetRider(bool); // each: CancelPending + RefreshLine + OnAnyChanged
public bool IsOnMoon { get; } public bool IsDrums { get; } public Vector3 RestPositionNow { get; }   // RestPosition(restingTile, StackSlot) for tests
public CubeState ToState(); public void ApplySettings(CubeState s);  // all §5 fields
public float StepStartLocalBeat(int k); public float StepBeats { get; } // as today (+ Triplet 1/3)
// VoiceRules.cs
public struct NoteEvent { public int slot, midi, vel; public double onDsp, offDsp; }
public static int Resolve(AudioCube cube, AudioCube.Window w, TileInteraction tile, AudioCube.Hit hit, int stack, int pass, double onDsp, double stepSec, double windowEndDsp, double nextHitDsp, NoteEvent[] outEvents);
public static void Dispatch(NoteEvent[] ev, int n, int owner);
public static NoteEvent PreviewEvent(int slot, TileInteraction tile, int octave);   // §4.6
public static void DispatchCrash(double dsp);                                       // fill crash, owner 7
public static bool PreviewAllowed(TileInteraction tile, int slot);                  // lit-island rule
// Performance.cs (static)
public static bool StutterActive; public static AudioCube SpotlightCube;
public static void HoldStutter(bool on); HoldFunnel(bool on); HoldHalf(bool on); HoldDrop(bool on); HoldSpotlight(AudioCube c, bool on);   // quantised internally
public static void Log(NoteEvent e);        // called by Dispatch
public static void SetTone(float t01); SetSpace(float s);  // Tone/Space dials → Synth master cutoff / per-slot sends
// TileInteraction.cs
public void SetKitLook(bool on);            // drum clothes glyph swap + darken
public void PlayPreview(int instrument = -1, int octave = 0);
```
**Consumes**: `Rhythm.*`, `GlobalClock.LoopIndex/DspOfNext16th` (E); `SequenceMaster.CountOn/IndexOn` (exist); `KeyBlock.GetTile/rows/chordRootMIDI/energy/fill/sleep/kind` (B); `IconFactory` kit glyph names (D, already exist: `dot, square, sparkle, ring, diamond`).

**Steps**: M1: (1) `Instruments` 10 rows, `PushToSynth` with pan/sends; (2) `VoiceRules.Resolve/Dispatch` with rules 1-9 and the fallback; (3) `AudioCube`: `Decide`, hit-advance `UpdatePlayback` (single window first: `windows = [{start = startBeatOffset, length = measureLength}]` shim so WP-B's `SetWindows` can land later), `StepLen.Triplet`, stickers, octave float height, tick dots and bead sizes in `RefreshLine`, shadow/twin ghost renderers, echo; (4) `TileInteraction.PlayPreview` lit-island rule, `SetKitLook`; (5) mixer edit + prefab `playOnAwake` + PCM. M2: (6) windows list, Rider tile lookup, Moon cubes; (7) `Performance` punch-ins/spotlight; (8) energy/fill/sleep in `Decide`/`Resolve`.

**Verification (execute_code)**:
```csharp
// after dicing a song and drawing a 6-tile Eighth cube on island 0 with hits = 3:
var c = SequenceMaster.Cubes[0]; c.SetHits(3);
var s = c.HitSteps16(0, 0); Debug.Assert(s.Count == 3 && s[0] == 0 && s[1] == 6 && s[2] == 12, "E(3,8) on 8ths → 16ths {0,6,12}");
Debug.Assert(c.Decide(0, 3, 0).node == 1 && c.Decide(0, 1, 0).fires == false, "hit-advance: step 3 = second hit = node 1; step 1 is a wait");
// register + low-end law on a Bass cube on the top row of a maj9 island:
var ev = new VoiceRules.NoteEvent[8]; int n = VoiceRules.Resolve(bassCube, bassCube.windows[0], topTile, bassCube.Decide(0,0,0), 0, 0, GlobalClock.DspNow + 0.1, 0.5, GlobalClock.DspNow + 2.0, double.PositiveInfinity, ev);
Debug.Assert(n == 1 && ev[0].midi >= 28 && ev[0].midi <= 55 && ev[0].midi == SynthBank.ClampToRegister(4, island.chordRootMIDI + SongManager.Transpose), "bass downbeat root, in register");
// window-end clamp on a Long-gate Pad:
Debug.Assert(ev2[0].offDsp <= windowEndDsp - 0.005, "note-off never past window end");
// stutter/half-time restore:
float bpm = GlobalClock.BPM; Performance.HoldHalf(true); /* wait 1 s */ Performance.HoldHalf(false); /* wait 1 s */ Debug.Assert(GlobalClock.BPM == bpm, "half-time restores BPM exactly");
Debug.Assert(Synth.Ready && Synth.Errors == 0 && Synth.LateEvents == 0, "engine clean");
```
Also grep gates: `grep -n "Synth.Note(" /Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Assets/*.cs /Users/ethanchen/audio_to_3d_space/AudioCube_Unity/Assets/DeaCube/*.cs` must list only `VoiceRules.cs`; `grep -n "Random\." AudioCube.cs VoiceRules.cs` must be empty; `grep -n "rests\[" AudioCube.cs` only in `ToState/ApplySettings`. Ear checks: no note on path start, top-row tiles no longer collapse (old pitch clamp), bass audibly low, pads stop before the next chord.

### 8.4 WP-B — World, time, input (≈ 1,500 lines; M1 world, M2 Moons/windows/halos input)

Owned: `SongManager.cs`, `KeyBlock.cs`, `PathManager.cs`, `ProjectConfig.cs`, `MeshFactory.cs`, `Route.cs` (N), `Comet.cs` (N), `IslandDrag.cs` (N).

**Exposes**:
```csharp
// ProjectConfig.cs (additions)
public const float IslandSnap = 0.5f, IslandGap = 1.5f, CableSag = -1.8f, HubRadius = 0.7f, HubHeight = 0.5f, DropOnWireDist = 1.5f, OctaveRise = 0.35f;
// MeshFactory.cs
public static Mesh Prism(int sides, float radius, float height);
public static Mesh Disc(float radius, int segs);
public static Mesh SegmentRing(float inner, float outer, int segments, float gap01);   // 4 verts per segment, colors array writable
// KeyBlock.cs
public int kind;                       // 0 island, 1 Moon
public int mood, energy, repeat; public bool fill, sleep; public float px, pz; public bool placed;
public int[] storedSemis;              // untouched data (MeasureState.From reads this)
public Vector3 HubPos { get; } public int HubSides { get; }   // §2.1; HubSides = bars + 2
public Transform Hub { get; }          // prism (islands) or disc handle (Moons); collider tagged "Island"
public void SetRingSegments(Color[] perSegment);   // called by UpdatePreLight
public void UpdatePreLight();          // enumerates cubes on this island via SequenceMaster.Cubes + AudioCube.HitSteps16 (WP-A)
public void SetPlayheadSegment(int s16);           // white segment; -1 none
public void SetPreGlow(float t01);     // count-in
public void SetHover(bool on); public void SetDragLift(float t01, Vector2 dir);
public bool IsLit { get; }             // SongManager.ActiveMeasureIndex == measureIndex && playing
// SongManager.cs
public readonly List<KeyBlock> Moons; public readonly List<int> PlayOrder;   // PlayOrder = expanded island indices (M4 repeats; identity in v2)
public void MoveMeasure(int from, int to);            // list surgery + CubeState.measure remap + RebuildFromState + re-seek + History.Push
public void MoveIsland(int index, float px, float pz);// sets KeyBlock.px/pz + transform; no rebuild; caller pushes History on release
public int AddMoon(); public void RemoveMoon(int i); public Vector3 FindFreeSpot(Vector3 near, Vector3 dir);
public void SetMood(int at, int mood); SetEnergy(int at, int e); SetFill(int at, bool f); SetSleep(int at, bool s); SetRepeat(int at, int n); SetClimate(int c);   // Capture → field → RebuildFromState (mood/climate/repeat) or direct KeyBlock+cube refresh (energy/fill/sleep) → History.Push
public int LitIsland { get; }                          // ActiveMeasureIndex(SongBeat)
public void RecomputeMeasureStarts();                  // now also builds every cube's windows (§2.10) and calls AudioCube.SetWindows
// Route.cs (MonoBehaviour, one per song, rebuilt in OnSongRebuilt)
public void Rebuild(); public void Refit(int islandIndex); public Vector3 PositionAlong(int fromIndex, float t01); public bool NearestSegment(Vector3 p, out int seg, out float dist, out Vector3 closest);
public void ShowGap(int seg, Vector3 at); public void HideGap(); public void RunPulse(); public void SetPaused(bool p);
// Comet.cs
public static Comet I; public Vector3 Position { get; } public bool InFlight { get; } public int FromIsland, ToIsland; public event Action<int> OnArrive;
// PathManager.cs
public enum EditorState { Idle, PlacingStart, DrawingPath, DraggingIsland, Recording }
public bool StampArmed; public int StampShape;          // -1 none, 0..3 shapes, 4 dice
public void StampTo(KeyBlock target); public void StampToAll();
public event Action<AudioCube, int> OnNodeHeld;         // WP-D opens the sticker ring
public event Action<KeyBlock> OnHubClicked;             // WP-D opens the island halo
```
**Consumes**: `AudioCube.HitSteps16/SetWindows/windows/RefreshLine/SetRider` (A); `Fx.SkyBeam/SeaRipple/BeatBump/Starburst` (C); `OrbitCamera.GlideTo/NotifyInput` (C); `MusicTheory.EffectiveSemis` (E); `UIManager.OpenStickerRing/OpenIslandHalo/SelectMeasure` (D) via the events above.

**Steps**: M1: (1) `BuildIslands` from `px/pz/placed`, delete bridges (`SongManager.cs:123-144`), platform collider + hub prism, `storedSemis` + effective semis; (2) `Route`, `Comet`; (3) `IslandDrag` (state machine invoked from `PathManager.Update`: pick → drag → snap → push-out → drop-on-wire → `MoveMeasure` / `MoveIsland` + `History.Push`); (4) segmented ring + `UpdatePreLight` + playhead segment + count-in; (5) `PathManager`: hotkeys 1-0, node hold → `OnNodeHeld`, hub click → `OnHubClicked`, remove `FocusMeasure` in `StartPath`. M2: (6) windows in `RecomputeMeasureStarts` (residents, riders, moons), `Moons` list + Moon `KeyBlock.Build` (disc, 6×4, kit look, mini ring), `AddMoon`; (7) `SetMood/Energy/Fill/Sleep/Climate`; (8) `SongBounds` with Moons. M4: `PlayOrder` repeats, `Recording` state, stamps.

**Verification (execute_code)**:
```csharp
var sm = SongManager.I; var before = new List<float>(sm.MeasureStarts);
sm.MoveIsland(1, sm.Islands[1].px + 9f, sm.Islands[1].pz + 5f);
// next frame:
for (int i = 0; i < before.Count; i++) Debug.Assert(Mathf.Approximately(before[i], sm.MeasureStarts[i]), "moving never changes time");
foreach (var c in SequenceMaster.Cubes) if (c.Island == sm.Islands[1]) Debug.Assert((c.transform.position - c.RestPositionNow).magnitude < 0.01f, "cubes ride along");
sm.MoveMeasure(2, 0); Debug.Assert(sm.Islands[0].measureIndex == 0 && UIManager.I.PillCount == sm.Islands.Count, "order = pills");
Debug.Assert(sm.Islands[0].Hub.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0 && sm.Islands[0].bars + 2 == sm.Islands[0].HubSides, "hub sides = bars + 2");
// comet timing: at SongBeat = MeasureStarts[i] + 0.5 the comet is on ring i; at MeasureStarts[i+1] - 0.5 it is in flight
GlobalClock.Seek(sm.MeasureStarts[1] - 0.5); Debug.Assert(Comet.I.InFlight, "flying during the last beat");
// rider: one loop of the song schedules hits on every island
var r = SequenceMaster.Cubes[0]; r.SetRider(true); Debug.Assert(r.windows.Count == sm.Islands.Count, "rider windows");
// moon cube: BeatsPerBar / StepBeats hits per bar for TotalBeats / BeatsPerBar bars
```
Captures: `p_frameall.png` after `FrameAll` (cable, hubs, Moon visible), `p_comet_flight.png` at `local == L − 0.5` (editor focused).

### 8.5 WP-C — Camera & Fx (≈ 450 lines; M1)

Owned: `OrbitCamera.cs`, `Fx.cs`, `DeaCube_AdditiveVertex.shader` (N), `Onboarding.cs` (N, M4).

**Exposes**:
```csharp
// OrbitCamera.cs
public void GlideTo(Vector3 focus, float seconds = 0.5f);   // no distT change
public void NotifyInput();                                   // sets the 2.5 s cooldown (called by IslandDrag / drawing)
public Func<Vector3> LeashTarget;                            // set by Comet: () => Comet.I.Position
public bool followPlayhead;                                  // default true (scene already true)
// Fx.cs
public static void BeatBump(float amount = 0.2f); SkyBeam(Vector3 pos, Color c, float height); SeaRipple(Vector3 pos, Color c, float scale = 3.5f); Starburst(Vector3 pos, Color c);
public static void SetEmberRate(float r); public static void SetClimateTint(int climate);
public static Material AdditiveVertex(Texture tex, float intensity);   // material for SegmentRing
```
**Consumes**: `Comet.I.Position/InFlight` (B), `SongManager.SongBounds/Moons` (B), `KeyBlock.energy` (B).

**Steps**: (1) delete the snap (`OrbitCamera.cs:89-95`), viewport-band leash, left-drag sea pan (only when `PathManager` reports no tile/cube/island under the cursor: `PathManager.I.HoverIsEmpty`), double-click glide, soft bounds, `A/D` without `distT`; (2) `BeatBump` (cache the Bloom override), `SkyBeam`, `SeaRipple`, sea plane, `Starburst` + `"burst"` texture (WP-C adds it to `IconFactory`? — no: `IconFactory.cs` is WP-D's; WP-C requests the `"burst"` and `"noise"` soft textures from WP-D in M0 and until then uses `"glow"`); (3) `AdditiveVertex` shader + material factory; (4) climate tint in `DriveSkyTint`; M4 `Onboarding` (seeded demo playing, ghost hand tracing a 3-tile path, try-me pulses).

**Verification**: `execute_code` sets `Comet` far from the focus and asserts `focusTarget` moved only after the comet left the band; `OrbitCamera.I.dist` unchanged across `GlideTo`; a capture on a downbeat shows the beam (`p_downbeat.png`); profiler: `Fx.Update` < 0.3 ms with 30 pillars.

### 8.6 WP-D — HUD (≈ 1,100 lines; M1 panels/icons, M2 halos/rings/punch-ins)

Owned: `UIManager.cs`, `UIKit.cs`, `IconFactory.cs`.

**Exposes**:
```csharp
// UIManager.cs
public static UIManager I; public int PillCount { get; }
public void OpenStickerRing(AudioCube cube, int node); public void OpenIslandHalo(KeyBlock kb); public void OpenMoonHalo(KeyBlock moon); public void CloseRings();
public void SelectMeasure(int i, bool focus);   // made public (exists private)
public static void Toast(string icon, Color c);  // exists
// UIKit.cs
public class NecklaceDial : MonoBehaviour { public int n, hits, rot, mask; public Color color; public Action<int> onHits, onRot; public Action<int> onBeadToggle; public Action onRelease; public void Set(int n, int hits, int rot, int mask, bool notify); }
public class PopRing : MonoBehaviour { public static PopRing Open(Transform hudRoot, Vector3 worldPos, string[] icons, Action<int> onPick, float radiusPx); public void Close(); }
public class HoldButton : HudButton { public Action<bool> onHoldChanged; }
// IconFactory.cs — all names listed in §6, plus soft textures "burst" (36 spokes) and "noise"
```
**Consumes**: `Instruments.Icons/Colors/Count` (A); `AudioCube.Set*`, `hits/rot/mask/StepsPerBar` (A); `SongManager.SetMood/SetEnergy/SetFill/SetSleep/SetClimate/AddMoon/MoveMeasure` (B); `PathManager.OnNodeHeld/OnHubClicked/StampTo/StampToAll/StampArmed` (B); `Performance.Hold*/SetTone/SetSpace` (A); `GlobalClock`/`History` (exist).

**Steps**: M1: (1) icons; (2) palette 2 × 5 with silhouettes + hover audition; (3) cube panel v2 rows incl. `NecklaceDial` and the "more" row; (4) measure bar v2 (mood, energy, fill, sleep, `polyN`); (5) transport: climate/tone/space dials, "more" toggle, punch-in strip (hold buttons); (6) pills `polyN`, hover link, underline, Moon pills + add-Moon button. M2: (7) `PopRing` sticker ring, island halo, Moon halo; (8) spotlight hold button. M4: pill drag reorder, pencil ring, record.

**Verification (execute_code; overlay UI is invisible to screenshots)**:
```csharp
var hud = GameObject.Find("HUDCanvas"); Debug.Assert(hud.transform.Find("Palette").GetComponentsInChildren<HudButton>(true).Length >= 20, "10 swatches + 10 mutes");
Debug.Assert(GameObject.Find("HUDCanvas/Transport/Punch") != null, "punch strip exists");
PathManager.I.Select(SequenceMaster.Cubes[0], true); /* next frame */ var dial = hud.GetComponentInChildren<NecklaceDial>(true); Debug.Assert(dial.n == SequenceMaster.Cubes[0].StepsPerBar, "necklace n = steps per bar");
UIManager.I.OpenStickerRing(SequenceMaster.Cubes[0], 1); Debug.Assert(hud.GetComponentInChildren<PopRing>(true) != null, "sticker ring open");
// numbers on screen: only the tempo label has digits
foreach (var t in hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.text.Length > 0 && t.name != "Label") Debug.LogWarning("text on screen: " + t.name + " = " + t.text);
```
Capture with the canvas temporarily switched to `ScreenSpaceCamera` for `p_hud.png`, then restored.

### 8.7 Cross-package contract summary (agree before M1 starts)

1. `AudioCube.Decide(w, k, pass)` and `HitSteps16(w, pass)` are the only rhythm truth; `KeyBlock.UpdatePreLight` and the strip call them; nobody else reads `mods/hits/mask` for timing.
2. `VoiceRules.Dispatch` is the only caller of `Synth.Note` for cubes; `VoiceRules.PreviewEvent/PreviewAllowed` the only preview path.
3. `SongManager.RecomputeMeasureStarts` is the only writer of `AudioCube.windows`.
4. `History.Push` is called by the *owner of the gesture* (UI button / drag release / hotkey), never inside setters.
5. Owner ids: cubes `Mathf.Abs(GetInstanceID()) + 16` (always ≥ 16), song 7, stutter 8, previews 9, anonymous 0.
6. All new per-frame work is O(cubes on the dragged island) during drags and O(1) otherwise; ring colours change only on events.

### 8.8 Line estimates

| Package | New | Modified | Total |
|---|---|---|---|
| WP-E | 330 | 190 | ≈ 520 |
| WP-A | 560 | 790 | ≈ 1,350 |
| WP-B | 720 | 780 | ≈ 1,500 |
| WP-C | 200 | 250 | ≈ 450 |
| WP-D | 520 | 580 | ≈ 1,100 |
| **Sum** | | | **≈ 4,900** (P0 ≈ 4,100; P1 ≈ 800) |

---

## 9. Risks and mitigations

| # | Risk | Mitigation |
|---|---|---|
| 1 | Audio/visual disagreement (audio scheduled 0.22 s early) | One pure `Decide` seeded by `(pass, node, seed)`; no `UnityEngine.Random` in `AudioCube/VoiceRules` (grep gate); `pass` from `GlobalClock.LoopIndex`. |
| 2 | Notes ringing into the next chord (ties, pads, release tails) | `offDsp = min(gateEnd, windowEnd − 5 ms)` in one place; sustaining slots release a 16th early; `Synth.AllNotesOff` on stop; `CancelPendingAfter` on tempo/seek; test asserts every event's `offDsp ≤ windowEnd`. |
| 3 | Hit-advance necklace changes existing songs | `hits = −1 ⇒ n` makes `h(k) = k`; the v1 fixture is asserted to produce identical `HitSteps16` before/after; ping-pong/once verified on the hit index. |
| 4 | Windows-list refactor regressions (scheduling across boundaries, loop wrap) | Land it in M2 behind the single-window shim (`SetWindows` with one window reproduces today); asserts: a resident cube's first note of pass 2 is scheduled at `DspTimeOfBeat(start + TotalBeats)`; a Rider schedules on every island in one loop; a Moon cube schedules `BeatsPerBar / StepBeats` hits per bar. |
| 5 | Moon/Route index hazards | Moons are a separate list and array (`SongState.moons`, `SongManager.Moons`, `CubeState.moon`); `AddMeasure/RemoveMeasure/MoveMeasure` never see them; `RestoreCube` branches on `moon ≥ 0`. |
| 6 | `RebuildFromState` destroys the world on every edit; new runtime state lost | Every new state is in `SongState` (§5) or recomputed (`windows`, ring colours, Route); positions written on `Capture`; the first rebuild after loading a v1 file writes `placed = true` positions from the row formula. |
| 7 | Drag while cubes are mid-hop (`hopFrom` cached, `AudioCube.cs:378`) | Re-cache `hopFrom` relative to the island root each drag frame (offset-preserving); acceptable one-hop lag otherwise. |
| 8 | Overlay UI invisible in captures / player loop frozen unfocused | Verification via `RectTransform`/component asserts; captures only with the editor focused; HUD captures with the canvas temporarily in `ScreenSpaceCamera`. |
| 9 | Double reverb / quiet or clipping mix | SFX Reverb removed from Master, Compressor added; engine limiter + `SetMasterGain(2.2)`; `Space` dial caps 1.6×; profile peak with 20 cubes + 2 Moons (target −1 dBFS peak, no limiter pumping audible). |
| 10 | Prefab `playOnAwake` spurious note; 4 AudioSources per cube | Already deleted by the integration; prefab YAML `m_PlayOnAwake: 0`; the lazy fallback source only exists without a SoundFont. |
| 11 | Low camera pitch hides the hanging cable | Comet flies an elevated arc; hubs glow; the cable also brightens for 0.3 s at each dash pass near hubs; if still unclear in review, raise the sag to −1.0 (one constant, `ProjectConfig.CableSag`). |
| 12 | Register folds make root/3rd/5th inaudible as drawn for Bass | Only 7th/9th tiles are re-mapped, only on downbeats; test asserts a root-tile bass note keeps the root's pitch class. |
| 13 | Followers producing clusters on 7th/9th chords | Shadow row must be ≥ 3 semitones away (skip 2nds); unit test on a maj9 island (rows 0,4,7,11,14 ⇒ from row 3 (11) the row above (14) is a 3rd ⇒ ok; from row 2 (7) the row above (11) is a 4th ⇒ ok; from row 3 the row below is 7 (a 4th) ⇒ ok; from row 4 (14) the row below (11) is a 3rd ⇒ ok; the 2nd case only occurs with sus/added rows and is skipped). |
| 14 | Mood maps clamping paths | Row-count-preserving maps only (`Dominant9` added); assert `rows` equal before/after every family for every quality. |
| 15 | Performance: ring colour writes, `RefreshLine` per drag frame | Colours only on events and one write per 16th; ≤ 20 cubes ⇒ negligible; profile `KeyBlock.Update` ≤ 0.1 ms per island. |
| 16 | Hotkey collisions with muscle memory | No `Z/X/C/V/S/Y/Tab` holds; punch-ins on `J/K/H/B`; every function has a visible glyph so hotkeys are shortcuts only. |
| 17 | Scope (~4.9k lines) | Five parallel packages with exactly-one-owner files, M0 schema first, single-window shim so M1 is playable without the refactor, P1 items isolated in M4. |
| 18 | `Instruments.LoadFrom` runs twice (`DeaCubeBoot`, `PathManager.Awake`) | Keep it idempotent; the 10-row table is static data; `PushToSynth` is idempotent. |
| 19 | Harmonic safety of UI sounds | `VoiceRules.PreviewAllowed` gate; island-drag hum only when stopped; palette auditions use the lit island's root. |
| 20 | Half-time float drift | Store the exact `float` BPM and restore it (`==` asserted); `SetBPM` is phase-preserving (`GlobalClock.cs:129-136`). |

### 9.1 Deviations from individual judges, with rationale
- Judge 2's "necklace as a step mask" and "n ≤ 16" are not followed (hit-advance won 2:1; `n` = steps per bar ≤ 24 still resolves on the bar and never yields 64-bead rings).
- Judge 1's parked-island clip bin is P2 (two judges asked to defer it); Sleep + Energy 0 give silence and breaks in v2.
- Judge 3's "not under the platforms" cable is answered by the elevated comet arc and a shallower sag rather than a top-side cable that could cross tiles of islands placed between two hubs.
- Judge 1's cube halo (≤ 8) is P2 because judges 2 and 3 forbid deleting the fixed cube panel before a halo system exists; the world-anchored rings that all three accept (sticker ring, island halo ≤ 6) ship in v2.

### 9.2 Verification checklist (run at M3, editor focused)
1. v1 fixture loads: same cube count, tiles, rests; `HitSteps16` identical to the pre-change build for every cube.
2. `E(3,8) → {0,3,6}`; rotation; `Pattern(-1)` all hits; `Pattern(-2, mask)` explicit.
3. Every dispatched event: `onDsp ≥ windowStartDsp`, `offDsp ≤ windowEndDsp − 5 ms`, `midi` ∈ slot register, low-end law holds; Bass downbeat root rule; Pad two tones below.
4. DSP delta between `GlobalClock.DspTimeOfBeat(beat)` and the engine's consumed event time < 0.5 ms (`Synth.Stats()`/`LateEvents == 0`).
5. Move an island while playing: `MeasureStarts` unchanged, no `CancelOwner` issued (count via a debug counter on `AudioCube.CancelPending`), cubes within 0.01 of `RestPosition`.
6. `MoveMeasure` while playing: re-seek lands in the same local beat of the same island.
7. Rider schedules on every island in one loop; Moon cube hits per bar; Sleep silences residents and Riders but not the Moon.
8. Half-time restores BPM exactly; Drop restores slot gains (`Instruments.PushToSynth`); Funnel springs back on the downbeat.
9. Mood/climate: `rows` preserved, `semis` untouched in the JSON, undo restores.
10. HUD: 10 swatches, punch strip present only while playing, `NecklaceDial.n == StepsPerBar`, no text other than the tempo label; captures `p_frameall.png`, `p_comet_flight.png`, `p_downbeat.png`, `p_drumclothes.png`, `p_hud.png`.

### 9.3 mustAvoid checklist (every item, all three judges)
Distance-as-time / zones / rotation: no (§2). Stones: no. Hover-opened node menus: no (click/hold only). Scroll over world objects: no. Hidden-gesture-only functions: no (every function has a glyph). HUD removal: no (palette sliders, strip, transpose/meter/metronome kept under "more"). Origin obelisk: no. Comet parked or on a corner hub: no (rides the ring). Same glyph for two controls: no (funnel = punch-in only; Energy = flame; climate glyphs framed). Halos > 8 / humanize knob: no. Energy → cutoff: no. Unclamped ties: no. Random at schedule time: no. Drum layers depending on chord rows / no always-on layer: no (Moon). Six-state click cycling / 8 slots: no. Double reverb / 4 AudioSources / playOnAwake: no. Follow off by default / cuts / FocusMeasure on StartPath / distT jump: no. Z/X/C/V/S hotkeys: no. Previews off the lit island while playing: no (silent tick). Scope creep in one phase: no (five packages, M0-M4). Reordering `SynthBank.Defs`: no. Parked islands / knots / Moons-with-window-period-in-the-Islands-list: no (Moons are a separate list with ordinary windows). Windows refactor / repeats / riders in the first pass: no (M2/M4). Play-it-in / bounce / 13-item rings / gems / 4-stop climate in v2 P0: no. `CancelOwner(this)`: no (int owner). Words/note names/numbers: only BPM. Both Moons and drum Travellers: no (Rider is pitched-only). Necklace as a mask (judge 3): no. Followers at row ±1 blindly: no. Pad above the tile: no. Destructive mood via SetChord: no. 12-13-control halos: no. Cable hidden under platforms: mitigated (elevated comet, shallow sag). Non-quantised punch-ins: no. Auditions quantised while stopped: no (immediate when stopped). Island-drag hum while playing: no. Vorbis WAVs / AudioSource.pitch path in the main path: no.

---

## Appendix A — Keymap (final)

| Key | Action | Key | Action |
|---|---|---|---|
| Space / P | play-pause | Home | stop |
| L | loop | M | metronome |
| 1-9, 0 | slot 0-9 | Esc / Enter / Delete | as today |
| Cmd+Z / Cmd+Shift+Z / Cmd+Y | undo / redo | Cmd+S | save |
| F | follow comet | O / Q | frame all / home |
| A / D | previous / next island (glide) | [ / ] | necklace rotate |
| - / = | necklace hits | ↑ / ↓ | octave |
| T | twin | W | shadow cycle |
| R | rider | E (hold) | spotlight |
| J (hold) | stutter | K (hold) | funnel |
| H (hold) | half-time | B (hold) | drop |
| G | stamp to hovered island (hold: all) | N | add Moon |

## Appendix B — `IconFactory` recipes for the new glyphs (primitives only)

`keys`: `U(Box(p,0,-0.1,0.7,0.5,0.08), Box(p,-0.3,0.25,0.12,0.25,0.03), Box(p,0.05,0.25,0.12,0.25,0.03))`. `pluck`: `U(Ring(p,-0.1,0,0.5,0.1), Seg(p,0.15,0.15,0.7,0.7,0.1))`. `pad`: `U(Circle(p,-0.35,-0.1,0.45), Circle(p,0.1,0.15,0.5), Circle(p,0.45,-0.15,0.4))` (soft, rendered with a lower SDF edge). `bolt`: `Poly` of 7 points (lightning). `bass`: `U(Ring(p,0,0,0.85,0.16), Ring(p,0,0,0.5,0.16), Circle(p,0,0,0.16))`. `bell`: `U(Ring(p,0,0,0.6,0.1), Circle(p,0,0,0.18))`. `strings`: `U(Arc(p,0,-0.4,0.8,0.08,20,160), Arc(p,0,-0.4,0.55,0.08,20,160), Arc(p,0,-0.4,0.3,0.08,20,160))`. `choir`: five `Circle(0.13)` on a 0.6 ring at 72° steps. `piano`: `Sub(Box(p,0,0,0.8,0.55,0.08), U(Box(p,-0.3,0.1,0.06,0.3,0.02), Box(p,0,0.1,0.06,0.3,0.02), Box(p,0.3,0.1,0.06,0.3,0.02)))`. `kit`: four `Circle(0.22)` at (±0.4, ±0.4). `cloud`: `U(Circle(p,-0.3,-0.1,0.35), Circle(p,0.05,0.12,0.42), Circle(p,0.4,-0.1,0.33), Box(p,0.05,-0.3,0.7,0.2,0.12))`. `storm`: `U(cloud shifted up 0.15, bolt scaled 0.45 at (0.1,-0.55))`. `climate*`: `U(Ring(p,0,0,0.92,0.06), <weather> scaled 0.62)`; `climateAuto` = `Ring` only. `flame`: `U(Circle(p,0,-0.25,0.45), Tri(p,-0.45,-0.25,0.45,-0.25,0,0.8))` with 4 notch `Seg`s on the right edge. `flourish`: `U(Arc(p,-0.1,-0.1,0.5,0.1,-90,180), Star(p+(-0.55,0.35),4,0.25,0.06))`. `moon`: `Sub(Circle(p,0,0,0.7), Circle(p,0.3,0.15,0.62))`. `repeatN`: `U(loop, N × dotSmall on the arc)`. `polyN`: `Poly` regular N-gon r 0.8 minus inner N-gon r 0.6. `ghost`: `Ring(p,0,0,0.4,0.07)`. `accent`: `U(Circle(p,0,0,0.42), 8 × Seg rays 0.55→0.85)`. `ratchet2/3`: 2/3 × `Box(0.1,0.6)` spaced 0.4. `coin`: `U(Ring(p,0,0,0.65,0.08), Sub(Circle(p,0,0,0.6), Box(p,0.5,0,0.5,1)))`. `tie`: `U(hbar, Circle(p,-0.75,0,0.14), Circle(p,0.75,0,0.14))`. `lift`: `U(Circle(p,0,-0.45,0.22), chevronU shifted up 0.2)`. `chevronU`: mirror of `chevronD`. `octUp/octDown`: `U(Sub(Box(p,0,-0.2,0.5,0.5,0.1), Box(p,0,-0.2,0.36,0.36,0.05)), chevronU/D at y ±0.6 scaled 0.5)`. `twin`: `U(Box outline at (-0.2,0.15), Box outline at (0.2,-0.15), Head(p,0.75,-0.15,0,0.3))`. `shadow`: `U(Box outline at (0,-0.3), dashed Box outline at (0,0.35))` (dashes = `Sub` with two bars). `echo`: `Circle`s at x = −0.6, −0.15, 0.25, 0.6 with r 0.3, 0.22, 0.16, 0.11. `comet`: `U(Circle(p,0.3,0.2,0.3), Circle(p,-0.05,-0.05,0.2), Circle(p,-0.35,-0.28,0.13), Circle(p,-0.6,-0.48,0.07))`. `stamp`: `U(Box(p,0,0.2,0.55,0.35,0.1), Box(p,0,-0.15,0.2,0.2,0.05), Arc(p,-0.35,-0.5,0.2,0.07,0,180), Arc(p,0.05,-0.5,0.2,0.07,180,360), Arc(p,0.45,-0.5,0.2,0.07,0,180))`. `stairsUp/Down`, `zigzag`, `mountain`: `Box`/`Seg`/`Tri` silhouettes. `record`: `U(Ring(p,0,0,0.7,0.09), Circle(p,0,0,0.3))`. `eclipse`: `Sub(Circle(p,0,0,0.7), Circle(p,0.45,0,0.7))`. `waves3`: three `Arc`s r 0.3/0.55/0.8, width 0.08, −60..60°. `funnel`: `U(Poly trapezoid (−0.8,0.6)(0.8,0.6)(0.15,−0.1)(−0.15,−0.1), Box(p,0,-0.45,0.15,0.35,0.05))`. `dropOut`: `layers` with the top two bars at 30 % (two-pass draw). `spot`: `U(Tri(p,-0.5,0.7,0.5,0.7,0,-0.4), Circle(p,0,-0.55,0.22))`. `diamondDot`: `U(diamond, Circle(p,0,0.75,0.13))`. `pencil`: exists. Soft textures: `burst` (36 radial spokes, `Mathf.Pow(cos(36θ)…)`), `noise` (value noise, 2 octaves).

## Appendix C — Demo song (first run)

`MusicTheory.RandomSong(seed)` + `moons = [one Moon]` with cubes: kick `E(4,4)` Quarter, snare `E(2,4) rot 1` Quarter, hat `E(8,8)` Eighth at column 2; islands placed on an arc (`radius = 18 + 2.5·n`, `i·360°/n` clockwise, `placed = true`); one Keys cube stamped to all islands (stairs-up 4 tiles, Eighth, `E(5,8)`); one Bass cube on island 0 as a Rider (root-fifth 2 tiles, Quarter); climate 0, energy 2 everywhere, tone 1, space 1; playing on load with the comet touring and follow on.
