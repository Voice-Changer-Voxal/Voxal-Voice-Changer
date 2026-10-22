using System;

namespace FunnyMic.Audio;

public class ReverbEffect
{
    // 4 delay lines at different prime-ish ms values
    private static readonly float[] TapDelaysMs = { 29f, 37f, 43f, 53f };

    private float[][] _delayLines = Array.Empty<float[]>();
    private int[] _writePositions = new int[4];
    private int _sampleRate;

    public float Mix { get; set; } = 0.3f;
    public float Decay { get; set; } = 0.5f;

    public void SetSampleRate(int sampleRate)
    {
        if (_sampleRate == sampleRate) return;
        _sampleRate = sampleRate;

        _delayLines = new float[4][];
        for (int t = 0; t < 4; t++)
        {
            int len = (int)(sampleRate * TapDelaysMs[t] / 1000f) + 1;
            _delayLines[t] = new float[len];
            _writePositions[t] = 0;
        }
    }

    public void Process(float[] samples, int count)
    {
        if (_delayLines.Length == 0) return;

        float decay = Math.Clamp(Decay, 0f, 0.9f);
        float mix = Math.Clamp(Mix, 0f, 1f);

        for (int i = 0; i < count; i++)
        {
            float input = samples[i];
            float reverbSum = 0f;

            for (int t = 0; t < 4; t++)
            {
                var line = _delayLines[t];
                int wp = _writePositions[t];

                float delayed = line[wp];
                reverbSum += delayed;

                line[wp] = input + delayed * decay;

                _writePositions[t] = (wp + 1) % line.Length;
            }

            reverbSum *= 0.25f; // average the 4 taps
            samples[i] = input * (1f - mix) + reverbSum * mix;
        }
    }

    public void Reset()
    {
        for (int t = 0; t < _delayLines.Length; t++)
        {
            Array.Clear(_delayLines[t]);
            _writePositions[t] = 0;
        }
    }
}
