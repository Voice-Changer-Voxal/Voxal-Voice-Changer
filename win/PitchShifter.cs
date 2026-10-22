using System;

namespace FunnyMic.Audio;

/// <summary>
/// Pitch shifter based on Stephan M. Bernsee's FFT pitch shifting algorithm.
/// Operates on mono float[] samples in-place for streaming use.
/// </summary>
public class PitchShifter
{
    private const int FftFrameSize = 2048;
    private const int OsAmp = 4;
    private const int FftFrameSizeHalf = FftFrameSize / 2;
    private const int StepSize = FftFrameSize / OsAmp;

    private readonly float[] _inFifo = new float[FftFrameSize + StepSize];
    private readonly float[] _outFifo = new float[FftFrameSize + StepSize];
    private readonly float[] _fftWorksp = new float[2 * FftFrameSize];
    private readonly float[] _lastPhase = new float[FftFrameSizeHalf + 1];
    private readonly float[] _sumPhase = new float[FftFrameSizeHalf + 1];
    private readonly float[] _outputAccum = new float[2 * FftFrameSize];
    private readonly float[] _anaFreq = new float[FftFrameSize];
    private readonly float[] _anaMagn = new float[FftFrameSize];
    private readonly float[] _synFreq = new float[FftFrameSize];
    private readonly float[] _synMagn = new float[FftFrameSize];
    private readonly float[] _window = new float[FftFrameSize];

    private int _rover;
    private bool _initialized;

    public PitchShifter()
    {
        for (int i = 0; i < FftFrameSize; i++)
            _window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / FftFrameSize)));
    }

    public void Reset()
    {
        _initialized = false;
        _rover = 0;
        Array.Clear(_inFifo);
        Array.Clear(_outFifo);
        Array.Clear(_fftWorksp);
        Array.Clear(_lastPhase);
        Array.Clear(_sumPhase);
        Array.Clear(_outputAccum);
    }

    public void Process(float pitchFactor, float[] samples, int numSamples, float sampleRate)
    {
        if (Math.Abs(pitchFactor - 1.0f) < 0.001f) return;

        double freqPerBin = sampleRate / FftFrameSize;
        double expectedPhaseDiff = 2.0 * Math.PI * StepSize / FftFrameSize;

        if (!_initialized)
        {
            _rover = FftFrameSize;
            _initialized = true;
        }

        for (int i = 0; i < numSamples; i++)
        {
            _inFifo[_rover] = samples[i];
            samples[i] = _outFifo[_rover - FftFrameSize];
            _rover++;

            if (_rover < FftFrameSize + StepSize) continue;
            _rover = FftFrameSize;

            // Window and load into FFT buffer
            for (int k = 0; k < FftFrameSize; k++)
            {
                _fftWorksp[2 * k] = _inFifo[k] * _window[k];
                _fftWorksp[2 * k + 1] = 0f;
            }

            Fft(_fftWorksp, FftFrameSize, false);

            // Analysis: convert to magnitude/frequency pairs
            for (int k = 0; k <= FftFrameSizeHalf; k++)
            {
                double real = _fftWorksp[2 * k];
                double imag = _fftWorksp[2 * k + 1];

                double magn = 2.0 * Math.Sqrt(real * real + imag * imag);
                double phase = Math.Atan2(imag, real);

                // Compute phase difference from last frame
                double phaseDiff = phase - _lastPhase[k];
                _lastPhase[k] = (float)phase;

                // Subtract expected phase advance
                phaseDiff -= k * expectedPhaseDiff;

                // Wrap to [-PI, PI]
                phaseDiff = WrapPhase(phaseDiff);

                // Convert deviation to Hz offset, then to true frequency
                double trueFreq = k * freqPerBin + (phaseDiff / expectedPhaseDiff) * freqPerBin;

                _anaMagn[k] = (float)magn;
                _anaFreq[k] = (float)trueFreq;
            }

            // Pitch shifting: move bins
            Array.Clear(_synMagn, 0, FftFrameSize);
            Array.Clear(_synFreq, 0, FftFrameSize);

            for (int k = 0; k <= FftFrameSizeHalf; k++)
            {
                int newBin = (int)(k * pitchFactor);
                if (newBin <= FftFrameSizeHalf)
                {
                    _synMagn[newBin] += _anaMagn[k];
                    _synFreq[newBin] = _anaFreq[k] * pitchFactor;
                }
            }

            // Synthesis: convert frequency/magnitude back to complex
            for (int k = 0; k <= FftFrameSizeHalf; k++)
            {
                double magn2 = _synMagn[k];
                double trueFreq = _synFreq[k];

                // Deviation from bin center frequency in bins
                double binDeviation = (trueFreq - k * freqPerBin) / freqPerBin;

                // Phase advance = expected + deviation
                double phaseAdvance = k * expectedPhaseDiff + binDeviation * expectedPhaseDiff;

                _sumPhase[k] += (float)phaseAdvance;

                _fftWorksp[2 * k] = (float)(magn2 * Math.Cos(_sumPhase[k]));
                _fftWorksp[2 * k + 1] = (float)(magn2 * Math.Sin(_sumPhase[k]));
            }

            // Zero negative frequencies
            for (int k = FftFrameSizeHalf + 1; k < FftFrameSize; k++)
            {
                _fftWorksp[2 * k] = 0f;
                _fftWorksp[2 * k + 1] = 0f;
            }

            Fft(_fftWorksp, FftFrameSize, true);

            // Window and accumulate output
            for (int k = 0; k < FftFrameSize; k++)
                _outputAccum[k] += _window[k] * _fftWorksp[2 * k] / (FftFrameSizeHalf * OsAmp);

            Array.Copy(_outputAccum, 0, _outFifo, 0, StepSize);
            Array.Copy(_outputAccum, StepSize, _outputAccum, 0, FftFrameSize);
            Array.Clear(_outputAccum, FftFrameSize, StepSize);
            Array.Copy(_inFifo, StepSize, _inFifo, 0, FftFrameSize);
        }
    }

    private static double WrapPhase(double phase)
    {
        return phase - 2.0 * Math.PI * Math.Floor(phase / (2.0 * Math.PI) + 0.5);
    }

    private static void Fft(float[] data, int fftSize, bool inverse)
    {
        int n = fftSize * 2;

        // Bit-reversal permutation
        int j = 0;
        for (int i = 0; i < n - 2; i += 2)
        {
            if (i < j)
            {
                (data[j], data[i]) = (data[i], data[j]);
                (data[j + 1], data[i + 1]) = (data[i + 1], data[j + 1]);
            }
            int m = n / 2;
            while (m >= 2 && j >= m) { j -= m; m /= 2; }
            j += m;
        }

        // Danielson-Lanczos
        int mmax = 2;
        while (n > mmax)
        {
            int istep = mmax * 2;
            double theta = -2.0 * Math.PI / mmax;
            if (inverse) theta = -theta;
            double wtemp = Math.Sin(0.5 * theta);
            double wpr = -2.0 * wtemp * wtemp;
            double wpi = Math.Sin(theta);
            double wr = 1.0, wi = 0.0;

            for (int m2 = 0; m2 < mmax; m2 += 2)
            {
                for (int i2 = m2; i2 < n; i2 += istep)
                {
                    int j2 = i2 + mmax;
                    double tempr = wr * data[j2] - wi * data[j2 + 1];
                    double tempi = wr * data[j2 + 1] + wi * data[j2];
                    data[j2] = (float)(data[i2] - tempr);
                    data[j2 + 1] = (float)(data[i2 + 1] - tempi);
                    data[i2] += (float)tempr;
                    data[i2 + 1] += (float)tempi;
                }
                double wOld = wr;
                wr = wr * wpr - wi * wpi + wr;
                wi = wi * wpr + wOld * wpi + wi;
            }
            mmax = istep;
        }

        // Note: No normalization on inverse FFT.
        // The Bernsee algorithm handles scaling in the output accumulation step.
    }
}
