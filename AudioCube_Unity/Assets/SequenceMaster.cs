using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class SequenceMaster : MonoBehaviour
{
    public static float TotalSongLength = 16f;
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) TogglePlayback();

        if (!GlobalClock.IsPlaying) return;

        float beatsPerSecond = GlobalClock.BPM / 60f;
        float tick = beatsPerSecond * Time.deltaTime;
        
        GlobalClock.SongBeat += tick;

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

        var validCubes = cubes.Where(c => c.isFinalized && c.pathNodes != null && c.pathNodes.Count > 0);

        var groupedGrids = validCubes.GroupBy(c => c.assignedGridIndex).OrderBy(g => g.Key);

        float currentAccumulatedTime = 0f;

        foreach (var gridGroup in groupedGrids)
        {
            int maxPathLengthForThisGrid = 0;

            foreach (AudioCube cube in gridGroup)
            {
                cube.startBeatOffset = currentAccumulatedTime;
                
                if (cube.pathNodes.Count > maxPathLengthForThisGrid)
                {
                    maxPathLengthForThisGrid = cube.pathNodes.Count;
                }
            }

            currentAccumulatedTime += maxPathLengthForThisGrid;
        }

        TotalSongLength = currentAccumulatedTime > 0 ? currentAccumulatedTime : 4f;
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
