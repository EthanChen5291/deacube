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

    // EVENTUALLY ALLOW FOR CUSTOM KEYS
    musicLogic.initializeFrequencyTable(59); // middle C in MIDI -> 60, but shifted to the left due to calibration errors
}

void GridComponent::mouseDown(const juce::MouseEvent& e) {
    auto cellLen = getWidth() / std::max(Config::numColumns, Config::numRows);

    float borderSpaceW = (getWidth() - (cellLen * Config::numColumns)) / 2;
    float borderSpaceH = (getHeight() - (cellLen * Config::numRows)) / 2;

    int col = (e.x - borderSpaceW) / cellLen;
    int row = (e.y - borderSpaceH) / cellLen;

    if (Config::isValid(row, col)) {
        int index = (row * Config::numColumns) + col;

        tileStates[index] = !tileStates[index];

        float hz = musicLogic.getFrequencyAt(row, col);

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
