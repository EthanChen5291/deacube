#ifndef TESTSAMPLER_H
#define TESTSAMPLER_H

#include <vector>
#include <cmath>
#include <numbers>

class TestSampler {
public:
    TestSampler();

    void trigger();

    void processBlock(float* outputBuffer, int numSamples);

    void generateSineWave(int secDuration, float hz);

private:
    int playhead;
    bool isPlaying;
    std::vector<float> audioData;

    const float kSampleRate = 44100.0f;
};

#endif
