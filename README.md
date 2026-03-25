# AudioCube

A Spatial Audio Sequencer. Built with Unity and JUCE (C++)

Compose music by designing motion. AudioCube delivers audio sequencing through a visual, spatial experience, leaving behind worries of intricate music theory.

![Audio Preview](AudioCube/Resources/Sprites/preview.png)

---

AudioCube aims to make music sequencing more visual and interactive.

Most sequencers are timeline-based — this one is spatial.

Players place tiles, draw paths, press play — and the system handles timing, movement, and playback. The goal is to make patterns easier to see and tweak in real time.

---

## How It Works

- A global BPM clock keeps everything in sync  
- Draw paths across adjacent tiles (ie. a square or a circle!)
- A cube moves along that path on beat  
- When it lands on a tile, it plays that tile’s note

Tiles store pitch data, so changing the grid OR key adapts the melody in real time. Tempo (the speed of the song) also can be maneuvered in real time!

---

## Controls

- **Draw Path** — Click adjacent tiles to create a sequence  
- **Play / Pause** — Start or stop the clock  
- **Cancel** — Clear current path  

---

- **Engine:** Unity (URP)  
- **Language:** C#  
- **Text:** TextMeshPro  

---

## Why I Built This

I wanted to explore a different way of thinking about sequencing.

Instead of:

**notes → sound**

this project uses:

**space → sound**

It’s a small system, but it opens up ideas around:
- visual pattern building  
- interactive music tools  
- game-like sequencing  
