using UnityEngine;
using System.Collections.Generic;

public class KeyBlock : MonoBehaviour
{
    public int measureIndex;
    public string assignedChord;
    public List<int> semitoneList; // left to right

    public Transform tilesContainer;
    public Transform cubesContainer;

    public GameObject noteTemplate;

    private List<TileInteraction> myTiles = new List<TileInteraction>();
    void Awake()
    {
        if (HarmonicLibrary.Chords[assignedChord] != null)
        {
            semitoneList = HarmonicLibrary.Chords[assignedChord];   
        }
    }

    public void initializeTileGrid() {
        for (int x = 0; x < semitoneList.Count; x++)
        {
            for (int z = 0; z < 4; z++) // do 4 for now of repeated chords but eventually will find alternative "safe" chords
            {
                Vector3 spawnPos = new Vector3(x * ProjectConfig.Spacing, 0, z * ProjectConfig.Spacing);

                GameObject tile = Instantiate(noteTemplate, spawnPos, Quaternion.identity);
                tile.transform.SetParent(tilesContainer);

                int semitones = semitoneList[z];
                float frequency = HarmonicLibrary.getFrequencyFromOffset(ProjectConfig.refMIDI, semitones);

                tile.GetComponent<TileInteraction>().myFrequency = frequency;

                tile.GetComponent<TileInteraction>().gridX = x;
                tile.GetComponent<TileInteraction>().gridZ = z;

                TileInteraction script = tile.GetComponent<TileInteraction>();
                myTiles.Add(script);

                tile.name = $"Tile_{measureIndex}_{x}_{z}";
            }
        }
    }
}
