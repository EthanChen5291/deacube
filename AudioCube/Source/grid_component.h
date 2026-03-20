#ifndef GRIDCOMPONENT_H
#define GRIDCOMPONENT_H

#include <juce_gui_basics/juce_gui_basics.h>
#include "test_sampler.h"
#include <vector>

class GridComponent : public juce::Component {
public:
    GridComponent(TestSampler& s);

    ~GridComponent() override = default;

    void paint(juce::Graphics& g) override;

    void mouseDown(const juce::MouseEvent& e) override;

    void initializeFrequencyTable(int rootMIDI);

    float toFrequency(int rootMIDI, int semitoneJump);

private:
    TestSampler& sampler;
    std::vector<bool> tileStates;
    float tileFrequencies[96];

    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(GridComponent)
};

#endif