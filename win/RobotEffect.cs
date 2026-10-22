using System;

namespace FunnyMic.Audio;

public class RobotEffect
{
    private double _phase;
    private int _sampleRate;

    public float Frequency { get; set; } = 150f;
    public float Mix { get; set; } = 0.7f;

    public void SetSampleRate(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public void Process(float[] samples, int count)
    {
        if (_sampleRate == 0) return;

        float freq = Math.Clamp(Frequency, 50f, 500f);
        float mix = Math.Clamp(Mix, 0f, 1f);
        double phaseInc = 2.0 * Math.PI * freq / _sampleRate;

        for (int i = 0; i < count; i++)
        {
            float dry = samples[i];
            float modulated = dry * (float)Math.Sin(_phase);

            samples[i] = dry * (1f - mix) + modulated * mix;

            _phase += phaseInc;
            if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
        }
    }

    public void Reset()
    {
        _phase = 0;
    }
}
