#include "music_model.h"
#include "project_config.h"
#include <cmath>

MusicModel::MusicModel() {
    frequencyTable.resize(Config::numColumns * Config::numRows, 0.0f);
}

void MusicModel::initializeFrequencyTable(int rootMIDI) {
    for (int r = 0; r < Config::numRows; r++) {
        for (int c = 0; c < Config::numColumns; c++) {
            int semitoneJump = Config::baseSemitoneVariations[r][c];

            int index = (Config::numColumns * r) + c;
            frequencyTable[index] = toFrequency(rootMIDI + semitoneJump);
        }
    }
}

float MusicModel::getFrequencyAt(int row, int col) const {
    if (!Config::isValid(row, col)) {
        return 440.0f;
    }

    int index = (row * Config::numColumns) + col;
    return frequencyTable[index];
}

float MusicModel::toFrequency(int noteMIDI) const {
    float freq = 440.0f * std::pow(2.0f, (noteMIDI - 69)/12.0f);
    return freq;
}