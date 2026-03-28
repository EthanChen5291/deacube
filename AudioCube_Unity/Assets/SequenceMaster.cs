using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SequenceMaster : MonoBehaviour
{
    public static float TotalSongLength = 16f;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) TogglePlayback();

        // REMOVED the MasterBeatLength check so the clock can always tick
        if (!GlobalClock.IsPlaying) return;

        float beatsPerSecond = GlobalClock.BPM / 60f;
        float tick = beatsPerSecond * Time.deltaTime;
        
        GlobalClock.SongBeat += tick;

        // Use TotalSongLength (which we calculated) to loop, NOT MasterBeatLength
        if (GlobalClock.SongBeat >= TotalSongLength)
        {
            GlobalClock.SongBeat = 0f;
            ResetAllCubes();
        }
    }

public static void RecalculateTimeline()
    {
        AudioCube[] cubes = Object.FindObjectsByType<AudioCube>(FindObjectsInactive.Exclude);
        if (cubes.Length == 0) return;

        int maxGridIndex = 0;
        Dictionary<int, int> gridDurations = new Dictionary<int, int>();

        foreach(var cube in cubes)
        {
            if (cube.pathNodes == null || cube.pathNodes.Count == 0) continue;
            int gridIndex = Mathf.RoundToInt(cube.pathNodes[0].x / ProjectConfig.gridSpacing);
            if (gridIndex > maxGridIndex) maxGridIndex = gridIndex;

            int pathLength = cube.pathNodes.Count;
            if (!gridDurations.ContainsKey(gridIndex) || pathLength > gridDurations[gridIndex])
                gridDurations[gridIndex] = pathLength;
        }

        float currentAccumulatedTime = 0f;
        Dictionary<int, float> gridStartOffsets = new Dictionary<int, float>();

        // FIXED: Only ONE loop to calculate offsets
        for (int i = 0; i <= maxGridIndex; i++)
        {
            gridStartOffsets[i] = currentAccumulatedTime;
            if (gridDurations.ContainsKey(i))
                currentAccumulatedTime += gridDurations[i]; 
        }

        foreach(var cube in cubes)
        {
            if (cube.pathNodes == null || cube.pathNodes.Count == 0) continue;
            int gridIndex = Mathf.RoundToInt(cube.pathNodes[0].x / ProjectConfig.gridSpacing);
            cube.startBeatOffset = gridStartOffsets[gridIndex];
        }

        // Set the loop point
        TotalSongLength = currentAccumulatedTime > 0 ? currentAccumulatedTime : 4f;
        Debug.Log($"Timeline Recalculated! Song Length: {TotalSongLength}");
    }

    void ResetAllCubes()
    {
        AudioCube[] cubes = Object.FindObjectsByType<AudioCube>(FindObjectsInactive.Exclude);
        foreach(var c in cubes)
        {
            c.ResetToStart();
        }
    }

    void TogglePlayback()
    {
        GlobalClock.IsPlaying = !GlobalClock.IsPlaying;

        if (GlobalClock.IsPlaying && GlobalClock.SongBeat <= 0.1f)
        {
            ResetAllCubes();
        }
    }
}
