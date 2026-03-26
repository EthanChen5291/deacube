using UnityEngine;

public class GlobalClock : MonoBehaviour
{
    public static bool IsPlaying = false;
    public static float BPM = 180f;

    public static float CurrentBeat = 0f;
    public static float MasterBeatLength = 4f;

    [SerializeField] private float debugBeat;
    [SerializeField] private float debugMasterLength;

    void Update()
    {
        debugBeat = CurrentBeat;
        debugMasterLength = MasterBeatLength;
    }
}
