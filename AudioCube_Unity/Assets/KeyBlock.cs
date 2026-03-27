using UnityEngine;
using System.Collections.Generic;

public class MeasureBlock : MonoBehaviour
{
    public int measureIndex;
    public string assignedChord;
    public int gridWidth = 4; //eventually change this 
    public int gridHeight = 4; // depends on the chord right?
    public List<int> semitoneList; // left to right

    public Transform tilesContainer;
    public GameObject noteTemplate;
    public Transform cubesContainer;


    private List<TileInteraction> myTiles = new List<TileInteraction>();

    public void BuildFromData(SongManager.MeasureData data)
    {
        measureIndex = data.index;
        assignedChord = data.chordKey;

        semitoneList = new List<int>(data.semitones);

        initializeTileGrid();
    }
    void Awake()
    {
        if (HarmonicLibrary.Chords[assignedChord] != null)
        {
            semitoneList = HarmonicLibrary.Chords[assignedChord];   
        }
    }

    public void initializeTileGrid(Vector3 offset) {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++) // do 4 for now of repeated chords but eventually will find alternative "safe" chords
            {
                Vector3 localPos = new Vector3(x * ProjectConfig.Spacing, 0 , z * ProjectConfig.Spacing);
                Vector3 spawnPos = transform.position + localPos;

                GameObject tile = Instantiate(noteTemplate, spawnPos, Quaternion.identity);
                tile.transform.SetParent(tilesContainer);

                TileInteraction script = tile.GetComponent<TileInteraction>();

                if (z < semitoneList.Count)
                {
                    int semitones = semitoneList[z];
                    script.myFrequency = HarmonicLibrary.getFrequencyFromOffset(ProjectConfig.refMIDI, semitones);
                }

                script.gridX = x;
                script.gridZ = z;

                myTiles.Add(script);

                tile.name = $"Tile_{measureIndex}_{x}_{z}";
            }
        }
    }
}
