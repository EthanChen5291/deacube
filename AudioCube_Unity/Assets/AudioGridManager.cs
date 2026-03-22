using UnityEngine;

public class AudioGridManager : MonoBehaviour
{
    public GameObject noteTemplate;

    void Start()
    {
        for (int x = 0; x < ProjectConfig.Cols; x++)
        {
            for (int z = 0; z < ProjectConfig.Rows; z++)
            {
                Vector3 spawnPos = new Vector3(x * ProjectConfig.Spacing, 0, z * ProjectConfig.Spacing);

                GameObject newCube = Instantiate(noteTemplate, spawnPos, Quaternion.identity);

                newCube.transform.parent = this.transform;

                newCube.name = $"Cube_{x}_{z}";
            }
        }
    }
}
