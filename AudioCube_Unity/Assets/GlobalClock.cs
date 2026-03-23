using UnityEngine;

public class GlobalClock : MonoBehaviour
{
    public static bool IsPlaying = false;
    public static float BPM = 120f;

    public static float CurrentBeat = 0f;
    public static float MasterBeatLength = 4f;

    [SerializeField] private float displayCurrentBeat;

    void Update()
    {
        if(MasterBeatLength != 4) Debug.Log("Current Length: " + MasterBeatLength);
        
        if (IsPlaying)
        {
            CurrentBeat += (BPM / 60f) * Time.deltaTime;
            
            if (CurrentBeat >= MasterBeatLength) 
            {
                CurrentBeat = 0;
            }
        }

        displayCurrentBeat = CurrentBeat;
    }
}
