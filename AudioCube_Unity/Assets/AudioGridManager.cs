using UnityEngine;
using System.Collections.Generic;

public class AudioGridManager : MonoBehaviour
{
    public GameObject noteTemplate;
    public List<TileInteraction> allTiles = new List<TileInteraction>();

    void Start()
    {
        initializeGrid();
    }

    void initializeGrid()
    {
        for (int x = 0; x < ProjectConfig.Cols; x++)
        {
            for (int z = 0; z < ProjectConfig.Rows; z++)
            {
                Vector3 spawnPos = new Vector3(x * ProjectConfig.Spacing, 0, z * ProjectConfig.Spacing);

                GameObject tile = Instantiate(noteTemplate, spawnPos, Quaternion.identity);
                
                float freq = calculateFrequency(z, x);

                tile.GetComponent<TileInteraction>().myFrequency = freq;

                tile.transform.parent = this.transform;

                tile.GetComponent<TileInteraction>().gridX = x;
                tile.GetComponent<TileInteraction>().gridZ = z;
                
                TileInteraction script = tile.GetComponent<TileInteraction>();
                allTiles.Add(script);

                tile.name = $"Cube_{x}_{z}";
            }
        }
    }

    float calculateFrequency(int z, int x) {
        int tableRow = ProjectConfig.Rows - 1 - z;
        int semitones = ProjectConfig.baseSemitoneVariations[tableRow, x];
        float frequency = ProjectConfig.refFreq * Mathf.Pow(2f, semitones / 12f);

        if (z == 0 && x == 0) {
            Debug.Log($"cube 0,0: semitones: {semitones}, target freq: {frequency}");
        }

        return frequency;
    }
}
