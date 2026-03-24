using UnityEngine;

public class SequenceMaster : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P)) TogglePlayback();

        if (!GlobalClock.IsPlaying || GlobalClock.MasterBeatLength <= 0) return;

        float beatsPerSecond = GlobalClock.BPM / 60f;
        GlobalClock.CurrentBeat += beatsPerSecond * Time.deltaTime;
        
        if (GlobalClock.CurrentBeat >= GlobalClock.MasterBeatLength)
        {
            GlobalClock.CurrentBeat = 0f;
            ResetAllCubes();
        }
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

        if (GlobalClock.IsPlaying)
        {
            ResetAllCubes();
        }
    }
}
