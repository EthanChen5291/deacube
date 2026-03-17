#include <juce_audio_utils/juce_audio_utils.h>
#include <vector>
#include <cmath>
#include <numbers>
#include <test_sampler.h>

class MainAudioProcessor : public juce::AudioAppComponent 
{
public:
    MainAudioProcessor() {
        sampler.generateSineWave(10, 440.0f);
        sampler.trigger();

        setAudioChannels(0, 2); // 0 input 2 output
    }

    ~MainAudioProcessor() { shutdownAudio(); }

    void getNextAudioBlock(const juce::AudioSourceChannelInfo& bufferToFill) override 
    {
        bufferToFill.clearActiveBufferRegion();

        float* left = bufferToFill.buffer->getWritePointer(0, bufferToFill.startSample);
        
        sampler.processBlock(left, bufferToFill.numSamples);

        if (bufferToFill.buffer->getNumChannels() > 1) {
            float* right = bufferToFill.buffer->getWritePointer(1, bufferToFill.startSample);
            std::copy(left, left + bufferToFill.numSamples, right);
        }
    }

    void prepareToPlay(int, double) override {}
    void releaseResources() override {}

private:
    TestSampler sampler;
};

// start app 
class MainApplication : public juce::JUCEApplication {
public:
    const juce::String getApplicationName() override { return "AudioCube"; }
    const juce::String getApplicationVersion() override { return "1.0.0"; }
    void initialise(const juce::String&) override { window = std::make_unique<MainWindow>(getApplicationName()); }
    void shutdown() override { window.reset(); }

    class MainWindow : public juce::DocumentWindow {
    public:
        MainWindow(juce::String name) : DocumentWindow(name, juce::Colours::darkgrey, allButtons) {
            setUsingNativeTitleBar(true);
            setContentOwned(new MainAudioProcessor(), true);
            setResizable(true, true);
            centreWithSize(400, 300);
            setVisible(true);
        }
        void closeButtonPressed() override { juce::JUCEApplication::getInstance()->systemRequestedQuit(); }
    private:
        JUCE_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(MainWindow)
    };
private:
    std::unique_ptr<MainWindow> window;
};

START_JUCE_APPLICATION(MainApplication)