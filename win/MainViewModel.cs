using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FunnyMic.Audio;
using NAudio.CoreAudioApi;

namespace FunnyMic.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly DispatcherTimer _meterTimer;

    private MMDevice? _selectedInput;
    private MMDevice? _selectedOutput;
    private bool _isActive;
    private double _gainDb;
    private double _pitchFactor = 1.0;
    private float _inputLevel;
    private float _outputLevel;
    private string _statusText = "Stopped  ·  Ctrl+Shift+M to bypass";
    private bool _effectsBypassed;

    // Echo
    private bool _echoEnabled;
    private double _echoDelayMs = 200;
    private double _echoFeedback = 0.4;

    // Reverb
    private bool _reverbEnabled;
    private double _reverbMix = 0.3;
    private double _reverbDecay = 0.5;

    // Distortion
    private bool _distortionEnabled;
    private double _distortionDrive = 8;

    // Robot
    private bool _robotEnabled;
    private double _robotFrequency = 150;

    public MainViewModel()
    {
        InputDevices = new ObservableCollection<MMDevice>();
        OutputDevices = new ObservableCollection<MMDevice>();

        ToggleCommand = new RelayCommand(Toggle);
        ToggleBypassCommand = new RelayCommand(ToggleBypass);

        PresetChipmunkCommand = new RelayCommand(ApplyChipmunk);
        PresetDeepVoiceCommand = new RelayCommand(ApplyDeepVoice);
        PresetRobotCommand = new RelayCommand(ApplyRobot);
        PresetMegaphoneCommand = new RelayCommand(ApplyMegaphone);
        PresetEchoChamberCommand = new RelayCommand(ApplyEchoChamber);
        PresetResetCommand = new RelayCommand(ApplyReset);

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) =>
        {
            InputLevel = _engine.InputLevel;
            OutputLevel = _engine.OutputLevel;
        };

        RefreshDevices();
    }

    public ObservableCollection<MMDevice> InputDevices { get; }
    public ObservableCollection<MMDevice> OutputDevices { get; }

    public MMDevice? SelectedInput
    {
        get => _selectedInput;
        set { _selectedInput = value; OnPropertyChanged(); }
    }

    public MMDevice? SelectedOutput
    {
        get => _selectedOutput;
        set { _selectedOutput = value; OnPropertyChanged(); }
    }

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleButtonText)); }
    }

    public string ToggleButtonText => IsActive ? "ON" : "OFF";

    public double GainDb
    {
        get => _gainDb;
        set
        {
            _gainDb = value;
            _engine.GainDb = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainDisplayText));
        }
    }

    public string GainDisplayText => $"{_gainDb:+0.0;-0.0;0.0} dB";

    public double PitchFactor
    {
        get => _pitchFactor;
        set
        {
            _pitchFactor = value;
            _engine.PitchFactor = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PitchDisplayText));
        }
    }

    public string PitchDisplayText
    {
        get
        {
            double semitones = 12.0 * Math.Log2(_pitchFactor);
            return $"{semitones:+0.0;-0.0;0.0} st";
        }
    }

    // --- Echo ---
    public bool EchoEnabled
    {
        get => _echoEnabled;
        set { _echoEnabled = value; _engine.EchoEnabled = value; OnPropertyChanged(); }
    }

    public double EchoDelayMs
    {
        get => _echoDelayMs;
        set
        {
            _echoDelayMs = value;
            _engine.EchoDelayMs = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EchoDelayDisplayText));
        }
    }

    public string EchoDelayDisplayText => $"{_echoDelayMs:0} ms";

    public double EchoFeedback
    {
        get => _echoFeedback;
        set
        {
            _echoFeedback = value;
            _engine.EchoFeedback = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EchoFeedbackDisplayText));
        }
    }

    public string EchoFeedbackDisplayText => $"{_echoFeedback:0.00}";

    // --- Reverb ---
    public bool ReverbEnabled
    {
        get => _reverbEnabled;
        set { _reverbEnabled = value; _engine.ReverbEnabled = value; OnPropertyChanged(); }
    }

    public double ReverbMix
    {
        get => _reverbMix;
        set
        {
            _reverbMix = value;
            _engine.ReverbMix = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReverbMixDisplayText));
        }
    }

    public string ReverbMixDisplayText => $"{_reverbMix:0.00}";

    public double ReverbDecay
    {
        get => _reverbDecay;
        set
        {
            _reverbDecay = value;
            _engine.ReverbDecay = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReverbDecayDisplayText));
        }
    }

    public string ReverbDecayDisplayText => $"{_reverbDecay:0.00}";

    // --- Distortion ---
    public bool DistortionEnabled
    {
        get => _distortionEnabled;
        set { _distortionEnabled = value; _engine.DistortionEnabled = value; OnPropertyChanged(); }
    }

    public double DistortionDrive
    {
        get => _distortionDrive;
        set
        {
            _distortionDrive = value;
            _engine.DistortionDrive = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DistortionDriveDisplayText));
        }
    }

    public string DistortionDriveDisplayText => $"{_distortionDrive:0.0}x";

    // --- Robot ---
    public bool RobotEnabled
    {
        get => _robotEnabled;
        set { _robotEnabled = value; _engine.RobotEnabled = value; OnPropertyChanged(); }
    }

    public double RobotFrequency
    {
        get => _robotFrequency;
        set
        {
            _robotFrequency = value;
            _engine.RobotFrequency = (float)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RobotFrequencyDisplayText));
        }
    }

    public string RobotFrequencyDisplayText => $"{_robotFrequency:0} Hz";

    // --- Levels ---
    public float InputLevel
    {
        get => _inputLevel;
        set { _inputLevel = value; OnPropertyChanged(); }
    }

    public float OutputLevel
    {
        get => _outputLevel;
        set { _outputLevel = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool EffectsBypassed
    {
        get => _effectsBypassed;
        set
        {
            _effectsBypassed = value;
            _engine.BypassEnabled = value;
            OnPropertyChanged();
            if (IsActive)
                StatusText = value ? "Bypass" : "Running  ·  Ctrl+Shift+M to bypass";
        }
    }

    public ICommand ToggleCommand { get; }
    public ICommand ToggleBypassCommand { get; }
    public ICommand PresetChipmunkCommand { get; }
    public ICommand PresetDeepVoiceCommand { get; }
    public ICommand PresetRobotCommand { get; }
    public ICommand PresetMegaphoneCommand { get; }
    public ICommand PresetEchoChamberCommand { get; }
    public ICommand PresetResetCommand { get; }

    // --- Presets ---
    private void ApplyChipmunk()
    {
        ApplyReset();
        PitchFactor = 1.8;
    }

    private void ApplyDeepVoice()
    {
        ApplyReset();
        PitchFactor = 0.55;
    }

    private void ApplyRobot()
    {
        ApplyReset();
        RobotEnabled = true;
        RobotFrequency = 150;
        ReverbEnabled = true;
        ReverbMix = 0.2;
        ReverbDecay = 0.3;
    }

    private void ApplyMegaphone()
    {
        ApplyReset();
        DistortionEnabled = true;
        DistortionDrive = 8;
    }

    private void ApplyEchoChamber()
    {
        ApplyReset();
        EchoEnabled = true;
        EchoDelayMs = 300;
        EchoFeedback = 0.5;
        ReverbEnabled = true;
        ReverbMix = 0.4;
        ReverbDecay = 0.5;
    }

    private void ApplyReset()
    {
        GainDb = 0;
        PitchFactor = 1.0;
        EchoEnabled = false;
        EchoDelayMs = 200;
        EchoFeedback = 0.4;
        ReverbEnabled = false;
        ReverbMix = 0.3;
        ReverbDecay = 0.5;
        DistortionEnabled = false;
        DistortionDrive = 8;
        RobotEnabled = false;
        RobotFrequency = 150;
    }

    private void RefreshDevices()
    {
        InputDevices.Clear();
        OutputDevices.Clear();

        foreach (var dev in AudioEngine.GetInputDevices())
            InputDevices.Add(dev);

        foreach (var dev in AudioEngine.GetOutputDevices())
            OutputDevices.Add(dev);

        if (InputDevices.Count > 0) SelectedInput = InputDevices[0];
        if (OutputDevices.Count > 0) SelectedOutput = OutputDevices[0];
    }

    private void Toggle()
    {
        if (IsActive)
        {
            _engine.Stop();
            _meterTimer.Stop();
            IsActive = false;
            StatusText = "Stopped  ·  Ctrl+Shift+M to bypass";
        }
        else
        {
            if (SelectedInput == null || SelectedOutput == null)
            {
                StatusText = "Select input and output devices";
                return;
            }

            try
            {
                _engine.GainDb = (float)_gainDb;
                _engine.PitchFactor = (float)_pitchFactor;
                _engine.Start(SelectedInput, SelectedOutput);
                _meterTimer.Start();
                IsActive = true;
                StatusText = _effectsBypassed ? "Bypass" : "Running  ·  Ctrl+Shift+M to bypass";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
        }
    }

    private void ToggleBypass()
    {
        EffectsBypassed = !EffectsBypassed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _engine.Dispose();
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}
