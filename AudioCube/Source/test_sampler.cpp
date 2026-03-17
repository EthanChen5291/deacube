
#include <cmath>
#include <vector>
#include <numbers>

const float PI = std::numbers::pi_v<float>;
const float DEFAULT_SAMPLE_RATE = 44100;

class TestSampler {
public:

    TestSampler() {
        playhead = 0;
        isPlaying = false;
    }

    void trigger() {
        playhead = 0;
        isPlaying = true;
    }

    void processBlock(float* outputBuffer, int numSamples) {
        for (int i = 0; i < numSamples; i++) {
            if (isPlaying && playhead < audioData.size()) {
                    outputBuffer[i] = audioData[playhead];
                    playhead++;
            } else {
                outputBuffer[i] = 0.0f;
                isPlaying = false;
            }
        }
    }

    void generateSineWave(int secDuration, float hz) {
        int totalSamples = DEFAULT_SAMPLE_RATE * secDuration;
        
        audioData.resize(totalSamples);

        for (int t = 0; t < audioData.size(); ++t) {
            float sinVal = sin((2.0f * PI) * hz * (static_cast<float>(t)/DEFAULT_SAMPLE_RATE));
            audioData[t] = sinVal * 0.4f;
        }

    }

private:
    int playhead;
    bool isPlaying;
    std::vector<float> audioData;
};
