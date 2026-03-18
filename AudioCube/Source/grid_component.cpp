#include <juce_audio_utils/juce_audio_utils.h>
#include "grid_component.h"
#include <vector>
#include <cmath>
#include <numbers>

GridComponent::GridComponent(TestSampler& s) : sampler(s) {
    tileStates.resize(144, false);
}

void GridComponent::mouseDown(const juce::MouseEvent& e) {
    int x = e.x / (getWidth() / 12);
    int y = e.y / (getHeight() / 12);

    if (x >= 0 && x < 12 && y >= 0 && y < 12) {
        int index = (y * 12) + x;

        tileStates[index] = !tileStates[index];

        float hz = 440.0f; // eventually, make this a std::map where I can retrieve the hz with key [index]

        sampler.generateSineWave(1, hz); // eventually generate time duration with bpm calculations
        sampler.trigger();
    }

    repaint();
}

void GridComponent::paint(juce::Graphics& g) {
    g.fillAll(juce::Colours::black);

    auto cellW = getWidth() / 12.0f;
    auto cellH = getHeight() / 12.0f;

    for (int y = 0; y < 12; y++) {
        for (int x = 0; x < 12; x++) {
            int index = (y * 12) + x;

            if (tileStates[index]) {
                g.setColour(juce::Colours::cyan.withAlpha(0.6f));
                g.fillRect(x * cellW, y * cellH, cellW, cellH);
            }

            g.setColour(juce::Colours::white.withAlpha(0.2f));
            g.fillRect(x * cellW, y * cellH, cellW, cellH);
        }
    }
}
