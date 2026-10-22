using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FunnyMic.Audio;

public class AudioEngine : IDisposable
{
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _bufferedProvider;
    private WaveFormat? _captureFormat;

    private readonly PitchShifter _pitchShifter = new();
    private readonly EchoEffect _echo = new();
    private readonly ReverbEffect _reverb = new();
    private readonly DistortionEffect _distortion = new();
    private readonly RobotEffect _robot = new();

    private float _gainDb;
    private float _pitchFactor = 1f;

    public float InputLevel { get; private set; }
    public float OutputLevel { get; private set; }
    public bool IsRunning { get; private set; }
    public bool BypassEnabled { get; set; }

    public event Action? LevelsUpdated;

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

    // Echo
    public bool EchoEnabled { get; set; }
    public float EchoDelayMs { get => _echo.DelayMs; set => _echo.DelayMs = value; }
    public float EchoFeedback { get => _echo.Feedback; set => _echo.Feedback = value; }
    public float EchoMix { get => _echo.Mix; set => _echo.Mix = value; }

    // Reverb
    public bool ReverbEnabled { get; set; }
    public float ReverbMix { get => _reverb.Mix; set => _reverb.Mix = value; }
    public float ReverbDecay { get => _reverb.Decay; set => _reverb.Decay = value; }

    // Distortion
    public bool DistortionEnabled { get; set; }
    public float DistortionDrive { get => _distortion.Drive; set => _distortion.Drive = value; }
    public float DistortionMix { get => _distortion.Mix; set => _distortion.Mix = value; }

    // Robot
    public bool RobotEnabled { get; set; }
    public float RobotFrequency { get => _robot.Frequency; set => _robot.Frequency = value; }
    public float RobotMix { get => _robot.Mix; set => _robot.Mix = value; }

    public static MMDeviceCollection GetInputDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
    }

    public static MMDeviceCollection GetOutputDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    }

    public void Start(MMDevice inputDevice, MMDevice outputDevice)
    {
        Stop();

        _capture = new WasapiCapture(inputDevice);
        // Use the device's native format - don't force a format
        _captureFormat = _capture.WaveFormat;

        _bufferedProvider = new BufferedWaveProvider(_captureFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = 1024 * 1024
        };

        // Initialize effects with sample rate
        int sr = _captureFormat.SampleRate;
        _echo.SetSampleRate(sr);
        _reverb.SetSampleRate(sr);
        _distortion.SetSampleRate(sr);
        _robot.SetSampleRate(sr);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 50);
        _output.Init(_bufferedProvider);

        _output.Play();
        _capture.StartRecording();
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;

        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }

        if (_output != null)
        {
            try { _output.Stop(); } catch { }
            _output.Dispose();
            _output = null;
        }

        _bufferedProvider = null;
        _captureFormat = null;
        _pitchShifter.Reset();
        _echo.Reset();
        _reverb.Reset();
        _distortion.Reset();
        _robot.Reset();
        InputLevel = 0;
        OutputLevel = 0;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_bufferedProvider == null || !IsRunning || _captureFormat == null) return;
        if (e.BytesRecorded == 0) return;

        int channels = _captureFormat.Channels;
        int bitsPerSample = _captureFormat.BitsPerSample;
        bool isFloat = _captureFormat.Encoding == WaveFormatEncoding.IeeeFloat;
        int bytesPerSample = bitsPerSample / 8;
        int bytesPerFrame = bytesPerSample * channels;
        int frameCount = e.BytesRecorded / bytesPerFrame;

        if (frameCount == 0) return;

        // Convert to mono float samples regardless of input format
        float[] samples = new float[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            int offset = i * bytesPerFrame;
            float sum = 0f;

            for (int ch = 0; ch < channels; ch++)
            {
                int chOffset = offset + ch * bytesPerSample;
                if (isFloat && bytesPerSample == 4)
                {
                    sum += BitConverter.ToSingle(e.Buffer, chOffset);
                }
                else if (bytesPerSample == 2)
                {
                    sum += BitConverter.ToInt16(e.Buffer, chOffset) / 32768f;
                }
                else if (bytesPerSample == 3)
                {
                    int val = e.Buffer[chOffset] | (e.Buffer[chOffset + 1] << 8) | (e.Buffer[chOffset + 2] << 16);
                    if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
                    sum += val / 8388608f;
                }
            }

            samples[i] = sum / channels;
        }

        // Track input peak
        float inputPeak = 0f;
        for (int i = 0; i < frameCount; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > inputPeak) inputPeak = abs;
        }
        InputLevel = inputPeak;

        if (!BypassEnabled)
        {
            // Apply gain
            float gainLinear = (float)Math.Pow(10, _gainDb / 20.0);
            for (int i = 0; i < frameCount; i++)
            {
                samples[i] *= gainLinear;
            }

            // Apply pitch shift
            if (Math.Abs(_pitchFactor - 1.0f) > 0.001f)
            {
                _pitchShifter.Process(_pitchFactor, samples, frameCount, _captureFormat.SampleRate);
            }

            // Apply echo
            if (EchoEnabled)
            {
                _echo.Process(samples, frameCount);
            }

            // Apply reverb
            if (ReverbEnabled)
            {
                _reverb.Process(samples, frameCount);
            }

            // Apply distortion
            if (DistortionEnabled)
            {
                _distortion.Process(samples, frameCount);
            }

            // Apply robot
            if (RobotEnabled)
            {
                _robot.Process(samples, frameCount);
            }
        }

        // Clamp and track output peak
        float outputPeak = 0f;
        for (int i = 0; i < frameCount; i++)
        {
            samples[i] = Math.Clamp(samples[i], -1f, 1f);
            float abs = Math.Abs(samples[i]);
            if (abs > outputPeak) outputPeak = abs;
        }
        OutputLevel = outputPeak;

        // Convert back to the capture format for output
        byte[] outputBytes = new byte[e.BytesRecorded];

        for (int i = 0; i < frameCount; i++)
        {
            int offset = i * bytesPerFrame;
            float val = samples[i];

            for (int ch = 0; ch < channels; ch++)
            {
                int chOffset = offset + ch * bytesPerSample;
                if (isFloat && bytesPerSample == 4)
                {
                    BitConverter.GetBytes(val).CopyTo(outputBytes, chOffset);
                }
                else if (bytesPerSample == 2)
                {
                    short s = (short)(val * 32767);
                    BitConverter.GetBytes(s).CopyTo(outputBytes, chOffset);
                }
                else if (bytesPerSample == 3)
                {
                    int s = (int)(val * 8388607);
                    outputBytes[chOffset] = (byte)(s & 0xFF);
                    outputBytes[chOffset + 1] = (byte)((s >> 8) & 0xFF);
                    outputBytes[chOffset + 2] = (byte)((s >> 16) & 0xFF);
                }
            }
        }

        _bufferedProvider.AddSamples(outputBytes, 0, outputBytes.Length);
        LevelsUpdated?.Invoke();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
