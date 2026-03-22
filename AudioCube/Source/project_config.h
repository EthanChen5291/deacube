#include <juce_gui_basics/juce_gui_basics.h>

namespace Config {
    static constexpr int numColumns = 12;
    static constexpr int numRows = 7;
    //static constexpr float tilePadding = 2.0f;

    static constexpr int baseSemitoneVariations[7][12] = {
    // Ab5 to G5
        { 20, 24, 26, 27, 31, 32, 31, 27, 26, 24, 22, 19 },
        
        // F5 to Eb5
        { 17, 20, 22, 24, 27, 29, 27, 24, 22, 20, 19, 15 },
        
        // C5 to Bb4
        { 12, 15, 17, 19, 22, 24, 22, 19, 17, 15, 14, 10 },
        
        // Ab4 to G4
        {  8, 12, 14, 15, 19, 20, 19, 15, 14, 12, 10,  7 },
        
        // F4 to Eb4
        {  5,  8, 10, 12, 15, 17, 15, 12, 10,  8,  7,  3 },
        
        // C4 to Bb3
        {  0,  3,  5,  7, 10, 12, 10,  7,  5,  3,  2, -2 },
        
        // Bb3 to F3 
        { -2,  2, -3, -2, -1,  0,  2, -5,  0, -2, -3, -7 }
    };
    
    static bool isValid(int row, int col) {
        return (col >= 0 && col < Config::numColumns && row >= 0 && row < Config::numRows);
    }

    const juce::Colour tileColor = juce::Colours::cyan;
    const juce::Colour bgColor = juce::Colours::black;
}