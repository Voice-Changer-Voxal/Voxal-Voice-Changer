using System;

namespace FunnyMic.Audio;

public class EchoEffect
{
    private float[] _buffer = Array.Empty<float>();
    private int _writePos;
    private int _sampleRate;

    public float DelayMs { get; set; } = 200f;
    public float Feedback { get; set; } = 0.4f;
    public float Mix { get; set; } = 0.5f;

    public void SetSampleRate(int sampleRate)
    {
        if (_sampleRate == sampleRate) return;
        _sampleRate = sampleRate;
        // Max buffer for 500ms delay
        _buffer = new float[(int)(sampleRate * 0.5f) + 1];
        _writePos = 0;
    }

    public void Process(float[] samples, int count)
    {
        if (_buffer.Length == 0) return;

        int delaySamples = Math.Clamp((int)(_sampleRate * DelayMs / 1000f), 1, _buffer.Length - 1);
        float feedback = Math.Clamp(Feedback, 0f, 0.8f);
        float mix = Math.Clamp(Mix, 0f, 1f);

        for (int i = 0; i < count; i++)
        {
            int readPos = _writePos - delaySamples;
            if (readPos < 0) readPos += _buffer.Length;

            float delayed = _buffer[readPos];
            float input = samples[i];

            _buffer[_writePos] = input + delayed * feedback;
            samples[i] = input * (1f - mix) + delayed * mix;

            _writePos++;
            if (_writePos >= _buffer.Length) _writePos = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(_buffer);
        _writePos = 0;
    }
}
