using UnityEngine;
using System.Collections.Generic;

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
        public float measureDuration;
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
    public GameObject keyBlockPrefab;
    public Transform mainCamera;

    [Header("Settings")]
    public float cameraPanSpeed = 5f;

    private List<float> gridStartBeats = new List<float>();
    private float totalSongBeats = 0f;

    private List<KeyBlock> spawnedBlocks = new List<KeyBlock>();

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
            GlobalClock.BPM = currentSongData.bpm;
            InitializeSongGrids();
        }
    }

    void Update()
    {
        if (spawnedBlocks.Count == 0) return;

        float currentMasterBeat = GlobalClock.SongBeat;
        int activeGridIndex = 0;

        for (int i = 0; i < gridStartBeats.Count; i++)
        {
            if (currentMasterBeat >= gridStartBeats[i])
            {
                activeGridIndex = i;
            }
        }

        float targetX = spawnedBlocks[activeGridIndex].transform.position.x;
        Vector3 targetPos = new Vector3(targetX, mainCamera.position.y, mainCamera.position.z);
        mainCamera.position = Vector3.Lerp(mainCamera.position, targetPos, Time.deltaTime * cameraPanSpeed);
    }

    void InitializeSongGrids()
    {
        //maybe the speed of movement should depend on bpm?
        float currentBeatTracker = 0f;
        float currentXOffset = 0f;

        for (int i = 0; i < currentSongData.measures.Length; i++)
        {
            MeasureData data = currentSongData.measures[i];

            gridStartBeats.Add(currentBeatTracker);

            //gridLen = measure.gridWidth + Project.gridSpacing;
            Vector3 spawnPos = new Vector3(currentXOffset, 0, 0);
            GameObject blockObj = Instantiate(keyBlockPrefab, spawnPos, Quaternion.identity);
            KeyBlock blockScript = blockObj.GetComponent<KeyBlock>();

            blockScript.BuildFromData(data);
            spawnedBlocks.Add(blockScript);

            currentBeatTracker += data.measureDuration;

            float blockWidth = data.semitones.Length * ProjectConfig.Spacing;
            currentXOffset += blockWidth + ProjectConfig.gridSpacing;
        }

    totalSongBeats = currentBeatTracker;
    }
}
