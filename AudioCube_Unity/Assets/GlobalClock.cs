using UnityEngine;

public class GlobalClock : MonoBehaviour
{
    public static bool IsPlaying = false;
    public static float BPM = 180f;

    public static float SongBeat = 0f;
    public static float CurrentBeat = 0f; //resets every grid
    public static float MasterBeatLength = 4f;

    [SerializeField] private float debugBeat;
    [SerializeField] private float debugSongBeat;
    [SerializeField] private float debugMasterLength;

    void Update()
    {
        debugBeat = CurrentBeat;
        debugMasterLength = MasterBeatLength;
        debugSongBeat = SongBeat;
    }

    public static void ResetClock()
    {
        CurrentBeat = 0f;
        MasterBeatLength = 4f;
        SongBeat = 0f;
    }
}
