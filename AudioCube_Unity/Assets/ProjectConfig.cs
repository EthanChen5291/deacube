using UnityEngine;

public static class ProjectConfig
{
    // grid visuals
    public const int Cols = 12;
    public const int Rows = 7;
    public const float Spacing = 1.2f;

    // cube mechanics
    public const float CubeDropHeight = 2.0f;
    public const float CubeHoverHeight = 0.6f;
    public const float snapThreshold = 0.6f; // the cube begins moving after 60% of the beat
    public const float cubeHopIntensity = 0.25f;

    public static readonly int[,] baseSemitoneVariations = new int[7, 12] // safe harmonic landscape
    {
        // Ab5 to G5
        {20, 24, 26, 27, 31, 32, 31, 27, 26, 24, 22, 19},
        
        // F5 to Eb5
        {17, 20, 22, 24, 27, 29, 27, 24, 22, 20, 19, 15},
        
        // C5 to Bb4
        {12, 15, 17, 19, 22, 24, 22, 19, 17, 15, 14, 10},
        
        // Ab4 to G4
        {8, 12, 14, 15, 19, 20, 19, 15, 14, 12, 10, 7},
        
        // F4 to Eb4
        {5, 8, 10, 12, 15, 17, 15, 12, 10, 8, 7, 3},
        
        // C4 to Bb3
        {0, 3, 5, 7, 10, 12, 10, 7, 5, 3, 2, -2},
        
        // Bb3 to F3 
        {-2,  2, -3, -2, -1, 0, 2, -5, 0, -2, -3, -7}
    };

    // audio
    public const float refFreq = 261.63f; // C4 wav file

    public const float MaxSystemVolume = 0.8f;
}
