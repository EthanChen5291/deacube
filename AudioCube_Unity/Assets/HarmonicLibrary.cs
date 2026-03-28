using UnityEngine;
using System.Collections.Generic;

public static class HarmonicLibrary
{

    public static float getFrequencyFromMIDI(int rootMIDI)
    {
        float totalOffsetFromRef = rootMIDI - ProjectConfig.refMIDI;

        return ProjectConfig.refFreq * Mathf.Pow(2f, totalOffsetFromRef / 12f);
    }
}
