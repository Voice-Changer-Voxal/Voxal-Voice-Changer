using System;

namespace FunnyMic.Audio;

public class DistortionEffect
{
    // Simple single-pole high-pass and low-pass for band-pass (300Hz - 3kHz)
    private float _hpState;
    private float _lpState;
    private float _hpCoeff;
    private float _lpCoeff;
    private int _sampleRate;

    public float Drive { get; set; } = 8f;
    public float Mix { get; set; } = 0.7f;

    public void SetSampleRate(int sampleRate)
    {
        if (_sampleRate == sampleRate) return;
        _sampleRate = sampleRate;
        // High-pass at ~300Hz
        _hpCoeff = 1f - (float)(2.0 * Math.PI * 300.0 / sampleRate);
        // Low-pass at ~3kHz
        _lpCoeff = (float)(2.0 * Math.PI * 3000.0 / sampleRate);
        _hpCoeff = Math.Clamp(_hpCoeff, 0f, 0.9999f);
        _lpCoeff = Math.Clamp(_lpCoeff, 0.0001f, 1f);
    }

    public void Process(float[] samples, int count)
    {
        float drive = Math.Clamp(Drive, 1f, 20f);
        float mix = Math.Clamp(Mix, 0f, 1f);

        for (int i = 0; i < count; i++)
        {
            float dry = samples[i];

            // Apply drive + tanh soft-clip
            float wet = (float)Math.Tanh(dry * drive);

            // Simple band-pass: high-pass then low-pass
            _hpState = _hpCoeff * (_hpState + wet - dry);
            float hp = _hpState;

            _lpState += _lpCoeff * (hp - _lpState);
            wet = _lpState;

            samples[i] = dry * (1f - mix) + wet * mix;
        }
    }

    public void Reset()
    {
        _hpState = 0;
        _lpState = 0;
    }
}
