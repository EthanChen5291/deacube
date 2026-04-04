## 📌 Demo Release in June!

<img src="AudioCube/Resources/Sprites/diacube_logo.png" alt="DeaCube Preview" width="570">

A Spatial Audio Sequencer. Built with Unity and JUCE (C++)

Compose music by designing motion. DeaCube aims to support audio sequencing through a highly intuitive experience, requiring no music theory experience at all.

---

**How it works**

- Grids are mapped to the user's desired musical context via a prompt (ie. "japanese pop", "space")
- Multiple grids are given to the user (each a unique compliment of the musical context)
- User picks the grids they like in the order they like, then draws paths across adjacent tiles on the grids
- Cubes then moves along those paths on beat, triggering the tile's notes.

Tiles store pitch data, so changing the grid OR key adapts the melody in real time. Tempo (the speed of the song) also can be maneuvered in real time!

---

- **Draw Path (Left-Click)** — Click adjacent tiles to create a sequence  
- **Play / Pause (P)** — Start or stop the clock  
- **Cancel (Left-Click)** — Clear current path  

---

- **Engine:** Unity (URP) alongside C++ (JUCE)
- **Language:** C# and C++

---

## Incoming Features:

- Real-time tempo control
- Grid selection (building block style)
- Individualized cube menus for better control over cube paths
- Improved audio players (JUCE audio VST incorporation)
