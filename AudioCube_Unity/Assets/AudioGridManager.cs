using UnityEngine;

public class AudioGridManager : MonoBehaviour
{
    public GameObject noteTemplate;

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

                tile.name = $"Cube_{x}_{z}";
            }
        }
    }

    float calculateFrequency(int z, int x) {
        int tableRow = ProjectConfig.Rows - 1 - z;
        int semitones = ProjectConfig.baseSemitoneVariations[tableRow, x];
        float frequency = ProjectConfig.refFreq * Mathf.Pow(2f, semitones / 12f);

        if (z == 0 && x == 0) {
            Debug.Log($"Cube 0,0: Semitones: {semitones}, Target Freq: {frequency}");
        }

        return frequency;
    }
}
