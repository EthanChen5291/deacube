using UnityEngine;
using System.Collections.Generic;

public static class HarmonicLibrary
{

    public static Dictionary<string, List<int>> Chords = new Dictionary<string, List<int>>
    {
        { "maj7", new List<int> { 0, 2, 4, 7, 9, 11, 12 } }, 
        
        { "min9", new List<int> { 0, 2, 3, 5, 7, 8, 10 } },
        
        { "dark", new List<int> { 0, 1, 4, 5, 7, 8, 10 } },
        
        { "space", new List<int> { 0, 2, 4, 6, 8, 10, 12 } }
    }; // EVENTUALLY NEED TO DETERMINE KEY VIA SEMITONES

    // octave?? key?? how to determine

    public static float getFrequencyFromOffset(float rootMIDI, int semitones)
    {
        return ProjectConfig.refFreq * Mathf.Pow(2f, (rootMIDI + semitones) / 12f);
    }
}
