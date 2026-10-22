using System;
using PortAudioSharp;

namespace FunnyMic.Cross.Audio;

public class AudioEngine : IDisposable
{
    private PortAudioSharp.Stream? _stream;
    private readonly PitchShifter _pitchShifter = new();
    private readonly ReverbEffect _reverb = new();
    private readonly RobotEffect _robot = new();

    private float _gainDb;
    private float _pitchFactor = 1f;
    private int _sampleRate = 44100;

    private const int FramesPerBuffer = 512;

    public float InputLevel { get; private set; }
    public float OutputLevel { get; private set; }
    public bool IsRunning { get; private set; }
    public bool BypassEnabled { get; set; }

    public float GainDb
    {
        get => _gainDb;
        set => _gainDb = value;
    }

    public float PitchFactor
    {
        get => _pitchFactor;
        set => _pitchFactor = Math.Clamp(value, 0.5f, 2.0f);
    }

    // Reverb
    public bool ReverbEnabled { get; set; }
    public float ReverbMix { get => _reverb.Mix; set => _reverb.Mix = value; }
    public float ReverbDecay { get => _reverb.Decay; set => _reverb.Decay = value; }

    // Robot
    public bool RobotEnabled { get; set; }
    public float RobotFrequency { get => _robot.Frequency; set => _robot.Frequency = value; }
    public float RobotMix { get => _robot.Mix; set => _robot.Mix = value; }

    public System.Collections.Generic.List<string> GetInputDevices()
    {
        var devices = new System.Collections.Generic.List<string>();
        PortAudio.Initialize();

        int numDevices = PortAudio.DeviceCount;
        for (int i = 0; i < numDevices; i++)
        {
            DeviceInfo info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0)
            {
                devices.Add(info.name);
            }
        }

        return devices;
    }

    public System.Collections.Generic.List<string> GetOutputDevices()
    {
        var devices = new System.Collections.Generic.List<string>();
        PortAudio.Initialize();

        int numDevices = PortAudio.DeviceCount;
        for (int i = 0; i < numDevices; i++)
        {
            DeviceInfo info = PortAudio.GetDeviceInfo(i);
            if (info.maxOutputChannels > 0)
            {
                devices.Add(info.name);
            }
        }

        return devices;
    }

    public void Start(int inputDeviceIndex, int outputDeviceIndex)
    {
        Stop();
        PortAudio.Initialize();

        var inputParams = new StreamParameters()
        {
            device = inputDeviceIndex,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = PortAudio.GetDeviceInfo(inputDeviceIndex).defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        var outputParams = new StreamParameters()
        {
            device = outputDeviceIndex,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = PortAudio.GetDeviceInfo(outputDeviceIndex).defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        _sampleRate = (int)PortAudio.GetDeviceInfo(inputDeviceIndex).defaultSampleRate;

        // Initialize effects with sample rate
        _reverb.SetSampleRate(_sampleRate);
        _robot.SetSampleRate(_sampleRate);

        _stream = new PortAudioSharp.Stream(
            inputParams,
            outputParams,
            _sampleRate,
            FramesPerBuffer,
            PortAudioSharp.StreamFlags.ClipOff,
            AudioCallback,
            IntPtr.Zero
        );

        _stream.Start();
        IsRunning = true;
    }

    private StreamCallbackResult AudioCallback(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (!IsRunning) return StreamCallbackResult.Complete;

        int sampleCount = (int)frameCount;

        unsafe
        {
            float* inputPtr = (float*)input;
            float* outputPtr = (float*)output;

            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = inputPtr[i];
            }

            float inputPeak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float abs = Math.Abs(samples[i]);
                if (abs > inputPeak) inputPeak = abs;
            }
            InputLevel = inputPeak;

            if (!BypassEnabled)
            {
                // Apply gain
                float gainLinear = (float)Math.Pow(10, _gainDb / 20.0);
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] *= gainLinear;
                }

                // Apply pitch shift
                if (Math.Abs(_pitchFactor - 1.0f) > 0.001f)
                {
                    _pitchShifter.Process(_pitchFactor, samples, sampleCount, _sampleRate);
                }

                // Apply reverb
                if (ReverbEnabled)
                {
                    _reverb.Process(samples, sampleCount);
                }

                // Apply robot
                if (RobotEnabled)
                {
                    _robot.Process(samples, sampleCount);
                }
            }

            float outputPeak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = Math.Clamp(samples[i], -1f, 1f);
                float abs = Math.Abs(samples[i]);
                if (abs > outputPeak) outputPeak = abs;
                outputPtr[i] = samples[i];
            }
            OutputLevel = outputPeak;
        }

        return StreamCallbackResult.Continue;
    }

    public void Stop()
    {
        IsRunning = false;

        if (_stream != null)
        {
            if (_stream.IsActive)
                _stream.Stop();
            _stream.Close();
            _stream.Dispose();
            _stream = null;
        }

        _pitchShifter.Reset();
        _reverb.Reset();
        _robot.Reset();
        InputLevel = 0;
        OutputLevel = 0;
    }

    public void Dispose()
    {
        Stop();
        PortAudio.Terminate();
    }
}
