using UnityEngine;
using System.Collections.Generic;

public class KeyBlock : MonoBehaviour
{
    public int measureIndex;
    public string assignedChord;
    public int chordRootMIDI;
    public int chordHeight; // depends on the chord right?
    public List<int> semitoneList; // left to right

    public float startBeatOffset;

    public Transform tilesContainer;
    public GameObject noteTemplate;
    public Transform cubesContainer;


    private List<TileInteraction> myTiles = new List<TileInteraction>();

    public void BuildFromData(SongManager.MeasureData data)
    {
        measureIndex = data.index;
        assignedChord = data.chordKey;
        chordRootMIDI = data.chordRootMIDI;

        if (chordRootMIDI < 40) 
        {
            chordRootMIDI += 60; 
        }

        semitoneList = new List<int>(data.semitones);
        chordHeight = semitoneList.Count;
        
        initializeInversionGrid();
    }

    public void initializeInversionGrid() 
    {
        semitoneList.Sort();

        List<int> currentInversion = new List<int>(semitoneList);

        for (int x = 0; x < ProjectConfig.numInversions; x++)
        {
            if (x > 0) currentInversion = getInversion(currentInversion);
            
            for (int z = 0; z < chordHeight; z++) // do 4 for now of repeated chords but eventually will find alternative "safe" chords
            {
                Vector3 localPos = new Vector3(x * ProjectConfig.Spacing, 0 , z * ProjectConfig.Spacing);
                Vector3 spawnPos = transform.position + localPos;

                GameObject tile = Instantiate(noteTemplate, spawnPos, Quaternion.identity);
                tile.transform.SetParent(tilesContainer);

                TileInteraction script = tile.GetComponent<TileInteraction>();

                if (z < currentInversion.Count)
                {
                    int absoluteMIDI = chordRootMIDI + currentInversion[z];
                    script.myFrequency = HarmonicLibrary.getFrequencyFromMIDI(absoluteMIDI);
                }

                script.gridX = x;
                script.gridZ = z;

                myTiles.Add(script);

                tile.name = $"Tile_{measureIndex}_{x}_{z}";
            }
        }
    }

    public List<int> getInversion(List<int> semitones)
    {
        List<int> result = new List<int>(semitones);
        
        int lowNote = result[0];
        result.Add(lowNote + 12);
        result.RemoveAt(0);

        result.Sort();
        return result;
    }
}
