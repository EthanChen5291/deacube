using System.Numerics;
using System.Threading.Tasks.Dataflow;
using UnityEngine;

public class SongManager : MonoBehaviour
{

    // You are a music theory API. Based on the user's prompt, 
    // generate a 3 to 8 measure chord progression. You must respond ONLY with a 
    // raw JSON object matching this exact schema. Do not include markdown formatting, 
    // conversational text, or explanations.

    [System.Serializable]
    public class MeasureData
    {
        public int index;
        public string chordKey;
        public int[] semitones;
    }

    [System.Serializable]
    public class SongData
    {
        public string songName;
        public int bpm;
        public MeasureData[] measures;
    }

    [Header("Data")]
    public SongData currentSongData;

    [Header("References")]
    public GameObject measureBlockPrefab;
    public Transform mainCamera;

    [Header("Settings")]
    public float cameraPanSpeed = 5f;

    private List<float> gridStartBeats = new List<float>();
    private float totalSongBeats = 0f;

    private List<MeasureBlock> spawnedBlocks = new List<MeasureBlock>();

    // song manager should parse the LLM output
    // and construct the grids based off of all the chords given

    // should have gridSpacing variable in projectConfig
    // need to move camera (smoothProgress) whenever moving grids (proportional to grid x-length)

    // cubes need to know to play the next grid whenever the first grid finishes
    // have a list of all the grids in order
    // iterate through that list until MasterClock totalGridBeats (new variable that 
    // only resets when grid changes) > grid.Totalbeats. SHOULD BE SYNCED

    // take songdata, iterate through measures, set clock.bpm = bpm.

    // have reference to camera here?

    void Start()
    {
        if (currentSongData != null && currentSongData.bpm > 0) 
        {
            GlobalClock.BPM = bpm;
            InitializeSongGrids();
        }
    }

    void Update()
    {
        if (spawnedBlocked.Count == 0 || mainCamera == null) return;

        //int currentMeasureIndex = Mathf.FloorToInt(GlobalClock.CurrentBeat / #//);

        float targetX = currentMeasureIndex * ProjectConfig.gridSpacing;
        Vector3 targetPos = new Vector3(targetX, mainCamera.position.y, mainCamera.position.z);

        currentMeasureIndex = Mathf.Clamp(currentMeasureIndex, 0, spawnedBlocks.Count - 1);

        mainCamera.position = Vector3.Lerp(mainCamera.position, targetPos, Time.deltaTime * cameraPanSpeed);
    }

    void InitializeSongGrids()
    {
        //maybe the speed of movement should depend on bpm?
        
        for (int i = 0; i < currentSongData.measures.Length; i++)
        {
            MeasureData data = currentSongData.measures[i];

            
            gridLen = measure.gridWidth + Project.gridSpacing;
            Vector3 offset = new Vector3(i * gridLen, 0, 0);

            GameObject blockObj = Instantiate(measureBlockPrefab, spawnPos, Quaternion.identity);

            MeasureBlock blockScript = blockObj.GetComponent<MeasureBlock>();

            blockScript.BuildFromData(data);

            spawnedBlocks.Add(blockScript);
            //how do I change the camera
        }
    }
}
