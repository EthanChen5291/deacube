#include "test_sampler.h"
#include <iostream>
#include <cmath>
#include <numbers>

static const float PI = std::numbers::pi_v<float>;
static const float DEFAULT_SAMPLE_RATE = 44100.0f;

TestSampler::TestSampler() {
    playhead = 0;
    isPlaying = false;
}

void TestSampler::trigger() {
    playhead = 0;
    isPlaying = true;
}

void TestSampler::processBlock(float* outputBuffer, int numSamples) {
    for (int i = 0; i < numSamples; i++) {
        if (isPlaying && playhead < (int)audioData.size()) {
            outputBuffer[i] = audioData[playhead];
            playhead++;
        } else {
            outputBuffer[i] = 0.0f;
            isPlaying = false;
        }
    }
}

void TestSampler::generateSineWave(int secDuration, float hz) {
    std::cout << hz << std::endl;
    int totalSamples = (int)(DEFAULT_SAMPLE_RATE * secDuration);
    audioData.resize(totalSamples);

    for (int t = 0; t < (int)audioData.size(); ++t) {
        float sinVal = sinf((2.0f * PI) * hz * (static_cast<float>(t) / DEFAULT_SAMPLE_RATE));
        audioData[t] = sinVal * 0.15f;
    }
}
