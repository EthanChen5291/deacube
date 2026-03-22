#ifndef GRIDCOMPONENT_H
#define GRIDCOMPONENT_H

#include <juce_gui_basics/juce_gui_basics.h>
#include "test_sampler.h"
#include <vector>
#include "music_model.h"

class GridComponent : public juce::Component {
public:
    GridComponent(TestSampler& s);

    ~GridComponent() override = default;

    void paint(juce::Graphics& g) override;

    void mouseDown(const juce::MouseEvent& e) override;

private:
    MusicModel musicLogic;
    TestSampler& sampler;
    std::vector<bool> tileStates;

    JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(GridComponent)
};

#endif