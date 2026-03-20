#include <iostream>
#include <juce_audio_utils/juce_audio_utils.h>
#include "grid_component.h"
#include "project_config.h"
#include <vector>
#include <cmath>
#include <numbers>
#include <algorithm>

GridComponent::GridComponent(TestSampler& s) : sampler(s) {
    tileStates.resize(144, false);
}

void GridComponent::mouseDown(const juce::MouseEvent& e) {
    auto cellLen = getWidth() / std::max(Config::numColumns, Config::numRows);

    float borderSpaceW = (getWidth() - (cellLen * Config::numColumns)) / 2;
    float borderSpaceH = (getHeight() - (cellLen * Config::numRows)) / 2;

    int col = (e.x - borderSpaceW) / cellLen;
    int row = (e.y - borderSpaceH) / cellLen;

    if (col >= 0 && col < Config::numColumns && row >= 0 && row < Config::numRows) {
        int index = (row * 12) + col;

        tileStates[index] = !tileStates[index];

        float hz = 440.0f; // eventually, make this a std::map where I can retrieve the hz with key [index]

        sampler.generateSineWave(1, hz); // eventually generate time duration with bpm calculations
        sampler.trigger();
    }

    repaint();
}

void GridComponent::paint(juce::Graphics& g) {
    g.fillAll(juce::Colours::white.withAlpha(0.1f));

    auto cellLen = getWidth() / 12.0f;

    float borderSpaceW = (getWidth() - (cellLen * Config::numColumns))/2;
    float borderSpaceH = (getHeight() - (cellLen * Config::numRows))/2;

    for (int r = 0; r < Config::numRows; r++) {
        for (int c = 0; c < Config::numColumns; c++) {
            int index = (r * Config::numColumns) + c;
            float width = borderSpaceW + (c * cellLen);
            float height = borderSpaceH + (r * cellLen);

            if (tileStates[index]) {
                g.setColour(juce::Colours::cyan.withAlpha(0.6f));
                g.fillRect(width, height, cellLen * (7.0f/8.0f), cellLen * (7.0f/8.0f));
            } else {
                g.setColour(juce::Colours::white.withAlpha(0.2f));
                g.fillRect(width, height, cellLen * (7.0f/8.0f), cellLen * (7.0f/8.0f));    
            }
        }
    }
}

void GridComponent::initializeFrequencyTable(int rootMIDI) {
    for (int r = 0; r < Config::numRows; r++) {
        for (int c = 0; c < Config::numColumns; c++) {
            int semitoneJump = Config::baseSemitoneVariations[r][c];

            int index = (Config::numColumns * r) + c;
            tileFrequencies[index] = toFrequency(rootMIDI, semitoneJump);
        }
    }
}

float GridComponent::toFrequency(int rootMIDI, int semitoneJump) {
    float freq = 440.0f * std::pow(2.0f, ((rootMIDI + semitoneJump) - 69)/12.0f);
    return freq;
}
