#nullable enable
// DeaCube v6 (package A): ChannelCount and PercussionChannels added (see Synthesizer.cs).
using System;

namespace MeltySynth
{
    /// <summary>
    /// Specifies a set of parameters for synthesis.
    /// </summary>
    public sealed class SynthesizerSettings
    {
        internal static int DefaultBlockSize = 64;
        internal static int DefaultMaximumPolyphony = 64;
        internal static bool DefaultEnableReverbAndChorus = true;

        private int sampleRate;
        private int blockSize;
        private int maximumPolyphony;
        private bool enableReverbAndChorus;
        private int channelCount = 16;             // DeaCube v6
        private int[]? percussionChannels;         // DeaCube v6: null = {9}

        /// <summary>
        /// Initializes a new instance of synthesizer settings.
        /// </summary>
        /// <param name="sampleRate">The sample rate for synthesis.</param>
        public SynthesizerSettings(int sampleRate)
        {
            CheckSampleRate(sampleRate);

            this.sampleRate = sampleRate;
            this.blockSize = DefaultBlockSize;
            this.maximumPolyphony = DefaultMaximumPolyphony;
            this.enableReverbAndChorus = DefaultEnableReverbAndChorus;
        }

        private static void CheckSampleRate(int value)
        {
            if (!(16000 <= value && value <= 192000))
            {
                throw new ArgumentOutOfRangeException("The sample rate must be between 16000 and 192000.");
            }
        }

        private static void CheckBlockSize(int value)
        {
            if (!(8 <= value && value <= 1024))
            {
                throw new ArgumentOutOfRangeException("The block size must be between 8 and 1024.");
            }
        }

        private static void CheckMaximumPolyphony(int value)
        {
            if (!(8 <= value && value <= 256))
            {
                throw new ArgumentOutOfRangeException("The maximum number of polyphony must be between 8 and 256.");
            }
        }

        /// <summary>
        /// Gets or sets the sample rate for synthesis.
        /// </summary>
        public int SampleRate
        {
            get => sampleRate;

            set
            {
                CheckSampleRate(value);
                sampleRate = value;
            }
        }

        /// <summary>
        /// Gets or sets the block size for rendering waveform.
        /// </summary>
        public int BlockSize
        {
            get => blockSize;

            set
            {
                CheckBlockSize(value);
                blockSize = value;
            }
        }

        /// <summary>
        /// Gets or sets the number of maximum polyphony.
        /// </summary>
        public int MaximumPolyphony
        {
            get => maximumPolyphony;

            set
            {
                CheckMaximumPolyphony(value);
                maximumPolyphony = value;
            }
        }

        /// <summary>
        /// DeaCube v6: the number of MIDI channels (16..256, default 16).
        /// </summary>
        public int ChannelCount
        {
            get => channelCount;

            set
            {
                if (!(16 <= value && value <= 256))
                {
                    throw new ArgumentOutOfRangeException("The number of channels must be between 16 and 256.");
                }
                channelCount = value;
            }
        }

        /// <summary>
        /// DeaCube v6: the percussion channels (bank select adds 128 on them); null = the GM channel 9 only.
        /// </summary>
        public int[]? PercussionChannels
        {
            get => percussionChannels;
            set => percussionChannels = value;
        }

        /// <summary>
        /// Gets or sets whether reverb and chorus are enabled.
        /// </summary>
        public bool EnableReverbAndChorus
        {
            get => enableReverbAndChorus;
            set => enableReverbAndChorus = value;
        }
    }
}
