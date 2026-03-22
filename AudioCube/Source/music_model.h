#ifndef MUSIC_MODEL_H 
#define MUSIC_MODEL_H

#include <vector>

class MusicModel {
public:
    MusicModel();

    void initializeFrequencyTable(int rootMIDI);

    float getFrequencyAt(int rootMIDI, int semitoneJump) const;
private:
    float toFrequency(int midiNote) const;
    std::vector<float> frequencyTable;
};

#endif