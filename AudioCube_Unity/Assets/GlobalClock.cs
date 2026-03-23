using UnityEngine;

public class GlobalClock : MonoBehaviour
{
    public static bool IsPlaying = false;
    public static float BPM = 240f;

    public static float CurrentBeat = 0f;
    public static float MasterBeatLength = 4f;

    [SerializeField] private float displayCurrentBeat;

    void Update()
    {
        displayCurrentBeat = CurrentBeat;
    }
}
