<div align="center">

<img src="AudioCube/Resources/Sprites/diacube_logo.png" width="420" alt="DeaCube">

**Compose music by designing motion.**

[![Unity](https://img.shields.io/badge/unity-6000.4%20URP-black.svg)](#installation)
[![JUCE](https://img.shields.io/badge/audio-JUCE%20%C2%B7%20C%2B%2B20-orange.svg)](#how-it-works)
[![Status](https://img.shields.io/badge/status-demo%20release%20pending-yellow.svg)](#limitations)

</div>

## Overview

DeaCube is a spatial audio sequencer. Describe a vibe, get a chord progression, then draw paths across a 3D grid. Coloured cubes travel those paths on the beat and trigger the notes they land on, so a melody is a shape you traced rather than notes you knew. No music theory is required: the grid only ever offers pitches that belong to the current chord.

## How it works

```
Unity frontend (C#, URP)                     JUCE backend (C++20)
SongGenerator, SongManager,   native plugin  TestSampler   per-voice sample playback
AudioGridManager, PathManager ─────────────► Sequencer     beat-locked scheduling
AudioCube, GlobalClock, UIManager            MusicModel    pitch and interval data
                                             GridComponent audio-side grid state
```

| Action | Input |
|---|---|
| Draw path | Left-click adjacent tiles |
| Play / pause | `P` |
| Cancel path | Left-click while drawing |

## Installation

Requirements: Unity 6000.4 with URP, CMake 3.15+ and a C++20 compiler, JUCE (submodule under `AudioCube/JUCE/`), and a [Gemini API key](https://aistudio.google.com/app/apikey).

```sh
git clone --recurse-submodules https://github.com/EthanChen5291/deacube.git
cd deacube
cp AudioCube_Unity/Assets/StreamingAssets/api_config.env.example \
   AudioCube_Unity/Assets/StreamingAssets/api_config.env      # GEMINI_API_KEY=...
```

The env file is gitignored and never committed.

## Quick start

```sh
cd AudioCube && cmake -B build && cmake --build build   # JUCE audio backend
```

Open `AudioCube_Unity/` in Unity Hub and press Play. The native plugin is referenced as a pre-built binary.

## Limitations

- The demo release is not out yet; the JUCE plugin must be built locally and matched to your platform.
- Chord generation depends on a live Gemini key and its output format; malformed JSON is rejected rather than repaired.
- Meter follows the generated song; the grid does not yet support per-measure selection, per-cube menus, or a tempo slider in the UI. Full VST hosting in JUCE is planned.
- There is no automated test suite. Behaviour is verified by playing the scene.

## Repository

| Path | Contents |
|---|---|
| `AudioCube/Source/` | JUCE app: `main_audio_processor`, `test_sampler`, `sequencer`, `music_model`, `grid_component` |
| `AudioCube/Resources/` | Logo, preview art, audio plugin assets |
| `AudioCube_Unity/Assets/` | Song generation, grid and path managers, cube movement, clock, UI, six cube prefabs |
| `AudioCube_Unity/Assets/StreamingAssets/` | `api_config.env.example` |

## Acknowledgements

[JUCE](https://juce.com) for the audio framework, [Google Gemini](https://aistudio.google.com) for chord generation, and Unity for the sequencer environment.
