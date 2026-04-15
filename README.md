## Demo Release Coming Soon!

<img src="AudioCube/Resources/Sprites/diacube_logo.png" alt="DeaCube Logo" width="980">

> Compose music by designing motion
> An AI-assisted Spatial Audio Sequencer built with Unity and JUCE (C++)

DeaCube lets you sequence music without knowing a single note of music theory. Describe a vibe, get a chord progression, then draw paths across a 3D grid — cubes travel those paths on the beat, triggering notes as they go.

## Features

- **AI-Assisted Chords**: Describe a vibe ("japanese pop", "space") and DeaCube generates a full chord progression with BPM, time signature, and voicings
- **3D Grid Sequencer**: Each chord spawns a spatial grid of tiles mapped to that chord's pitches
- **Path Drawing**: Click adjacent tiles to trace cube paths — the order of tiles becomes your melody
- **Real-time Control**: Change key or tempo while the song plays and the grid adapts instantly
- **JUCE Audio Backend**: Low-latency C++ audio engine handles sample playback and mixing
- **Multiple Cube Voices**: Each colored cube has its own synth sample and travels its own path

## Architecture

```
┌─────────────────────────┐
│   Unity Frontend (C#)   │  (3D visuals, input, UI)
│   SongGenerator.cs      │──── Gemini API (chord generation)
│   SongManager.cs        │
│   AudioGridManager.cs   │
│   PathManager.cs        │
│   GlobalClock.cs        │
└────────────┬────────────┘
             │
             │ Audio playback via native plugin bridge
             │
┌────────────▼────────────┐
│   JUCE Backend (C++)    │  (audio engine, sample playback)
│   TestSampler           │
│   Sequencer             │
│   GridComponent         │
│   MusicModel            │
└─────────────────────────┘
```

### Technology Stack

**Unity Frontend:**
- Unity (URP) with C#
- Input System for path drawing and playback controls
- TextMesh Pro for UI
- Gemini API (OpenAI-compatible endpoint) for AI chord generation
- JSON-based song data schema for Unity ↔ AI communication

**JUCE Audio Backend:**
- C++ with JUCE framework
- Custom sampler (`TestSampler`) for per-voice sample playback
- `Sequencer` and `MusicModel` for beat-locked audio scheduling
- `GridComponent` for audio-side grid state

## Quick Start

### Prerequisites

- Unity 6000+ (URP)
- CMake 3.22+ and a C++17 compiler
- JUCE (included as a submodule in `AudioCube/JUCE/`)
- A [Gemini API key](https://aistudio.google.com/app/apikey)

### 1. Clone the Repository

```bash
git clone <repository-url>
cd audio_to_3d_space
```

### 2. Configure the API Key

Create a file at `AudioCube_Unity/Assets/StreamingAssets/api_config.env`:

```env
GEMINI_API_KEY=your-gemini-api-key-here
```

An example file (`api_config.env.example`) is provided — copy and fill it in. This file is gitignored and never committed.

### 3. Build the JUCE Audio Backend

```bash
cd AudioCube
cmake -B build
cmake --build build
```

### 4. Open the Unity Project

Open `AudioCube_Unity/` in Unity Hub. The JUCE native plugin is referenced as a pre-built binary. Press Play to run.

## Project Structure

```
audio_to_3d_space/
├── AudioCube/                  # JUCE C++ audio engine
│   ├── Source/
│   │   ├── main_audio_processor.cpp   # JUCE AudioAppComponent entry
│   │   ├── test_sampler.cpp/h         # Per-voice sample playback
│   │   ├── sequencer.cpp/h            # Beat-locked audio scheduling
│   │   ├── music_model.cpp/h          # Music theory / pitch data
│   │   ├── grid_component.cpp/h       # Audio-side grid state
│   │   └── project_config.h           # Shared constants
│   ├── Resources/
│   │   ├── Sprites/                   # Logo and visual assets
│   │   └── AudioVSTs/                 # Audio plugin assets
│   └── JUCE/                          # JUCE framework (submodule)
│
└── AudioCube_Unity/            # Unity project (frontend)
    └── Assets/
        ├── SongGenerator.cs    # Gemini API calls, JSON parsing
        ├── SongManager.cs      # Parses song data, spawns KeyBlock grids
        ├── AudioGridManager.cs # Manages grid lifecycle and transitions
        ├── AudioCube.cs        # Cube movement along tile paths
        ├── PathManager.cs      # Path drawing logic
        ├── GlobalClock.cs      # Master BPM clock
        ├── HarmonicLibrary.cs  # Pitch/interval lookup tables
        ├── KeyBlock.cs         # Individual grid tile
        ├── UIManager.cs        # Prompt input and playback UI
        ├── InterfaceController.cs
        ├── SequenceMaster.cs
        └── [Color]Cube.prefab  # Blue, Green, Red, Orange, Purple, Yellow
```

## How It Works

### 1. AI Song Generation

The user types a vibe prompt into the UI. `SongGenerator.cs` sends it to Gemini with a strict system prompt, receiving back a JSON chord progression:

```json
{
  "songName": "Deep Space",
  "bpm": 90,
  "timeSignature": "4/4",
  "measures": [
    {
      "index": 1,
      "chordKey": "Cm9",
      "chordRootMIDI": 60,
      "semitones": [0, 3, 7, 10, 14],
      "measureDuration": 4.0
    }
  ]
}
```

**Supported voicings:** triads, 7ths, 9ths, 11ths — any interval layout the model produces.

### 2. Grid Construction

`SongManager.cs` parses the JSON and passes each measure to `AudioGridManager`, which spawns a `KeyBlock` grid. Each tile in a grid is mapped to a pitch from that chord's semitone intervals relative to the root MIDI note.

### 3. Path Drawing & Playback

The user left-clicks adjacent tiles to draw a cube's path. On Play (`P`), `GlobalClock` starts the BPM clock and cubes traverse their paths tile-by-tile. Each tile arrival triggers its note via the JUCE audio backend.

Changing the key remaps all tile pitches in real time. Changing BPM accelerates or slows the clock without stopping playback.

## Controls

| Action | Input |
|---|---|
| Draw path | Left-click adjacent tiles |
| Play / Pause | `P` |
| Cancel path | Left-click (while drawing) |

## Incoming Features

- Real-time tempo slider in UI
- Grid selection (building block style — pick which measures to include)
- Per-cube menus for finer path control
- Full JUCE VST audio integration for richer sounds

## Acknowledgments

- [JUCE](https://juce.com/) for the C++ audio framework
- [Google Gemini](https://aistudio.google.com/) for AI chord generation
- Unity for the 3D sequencer environment
