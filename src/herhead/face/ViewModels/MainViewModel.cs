using HeronWin.Face.Models;
using HeronWin.Face.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace HeronWin.Face.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly FaceAppSettings _settings;
    private readonly FaceSettingsService _settingsService;
    private Window? _window;
    private bool _isDemoMode;
    private bool _isConnected;
    private string _statusLabel = "offline";
    private string _headline = "Waiting for brain";
    private string _detail = "Start the brain runtime to stream live face state into this companion window.";
    private Brush _accentBrush = BuildBrush("#475569");
    private Color _accentColor = (Color)MediaColorConverter.ConvertFromString("#475569");
    private Geometry _mouthGeometry = Geometry.Parse("M 48 48 Q 80 68 112 48");
    private DispatcherTimer? _demoTimer;
    private int _demoIndex;

    public MainViewModel(FaceAppSettings settings, FaceSettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
        Settings = new SettingsViewModel(settings, settingsService);
        ActivityLines = new ObservableCollection<string>();
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        HideCommand = new RelayCommand(() => _window?.Hide());
        TogglePinnedCommand = new RelayCommand(TogglePinned);
        ToggleDemoCommand = new RelayCommand(ToggleDemoMode);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        AddActivity("face is ready.");
    }

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? ExitRequested;

    public SettingsViewModel Settings { get; }

    public ObservableCollection<string> ActivityLines { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand TogglePinnedCommand { get; }

    public ICommand ToggleDemoCommand { get; }

    public ICommand ExitCommand { get; }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string Headline
    {
        get => _headline;
        private set => SetProperty(ref _headline, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public Brush AccentBrush
    {
        get => _accentBrush;
        private set => SetProperty(ref _accentBrush, value);
    }

    public Color AccentColor
    {
        get => _accentColor;
        private set => SetProperty(ref _accentColor, value);
    }

    public Geometry MouthGeometry
    {
        get => _mouthGeometry;
        private set => SetProperty(ref _mouthGeometry, value);
    }

    public FaceAppSettings SettingsModel => _settings;

    public void AttachWindow(Window window)
    {
        _window = window;
        ApplyPinnedState();
    }

    public void ApplyPinnedState()
    {
        if (_window is not null)
        {
            _window.Topmost = _settings.IsPinned;
        }
    }

    public async Task PersistAsync()
    {
        await _settingsService.SaveAsync(_settings);
    }

    public void SetConnected(bool isConnected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _isConnected = isConnected;
            if (!isConnected && !_isDemoMode)
            {
                ApplySnapshot(new FaceStatusSnapshot(
                    FaceStatusKind.Offline,
                    "Waiting for brain",
                    "No live pipe connection yet. Face will reconnect automatically.",
                    DateTimeOffset.Now));
            }
        });
    }

    public void ApplyMessage(FaceStatusMessage message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _isDemoMode = false;
            var timestamp = DateTimeOffset.TryParse(message.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToLocalTime()
                : DateTimeOffset.Now;
            var snapshot = new FaceStatusSnapshot(
                ParseKind(message.State),
                message.Headline,
                message.Detail,
                timestamp,
                message.Transcript,
                message.ToolName);
            ApplySnapshot(snapshot);
        });
    }

    private void TogglePinned()
    {
        _settings.IsPinned = !_settings.IsPinned;
        ApplyPinnedState();
        AddActivity(_settings.IsPinned ? "window pinned on top." : "window can sit behind other apps.");
    }

    private void ToggleDemoMode()
    {
        if (_isDemoMode)
        {
            _isDemoMode = false;
            _demoTimer?.Stop();
            AddActivity("demo mode off.");
            if (!_isConnected)
            {
                ApplySnapshot(new FaceStatusSnapshot(
                    FaceStatusKind.Offline,
                    "Waiting for brain",
                    "No live pipe connection yet. Face will reconnect automatically.",
                    DateTimeOffset.Now));
            }

            return;
        }

        _isDemoMode = true;
        _demoIndex = 0;
        _demoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _demoTimer.Tick -= DemoTimerOnTick;
        _demoTimer.Tick += DemoTimerOnTick;
        _demoTimer.Start();
        DemoTimerOnTick(this, EventArgs.Empty);
        AddActivity("demo mode on.");
    }

    private void DemoTimerOnTick(object? sender, EventArgs e)
    {
        var states = new[]
        {
            new FaceStatusSnapshot(FaceStatusKind.Standby, "Standby", "Listening quietly for the wake word.", DateTimeOffset.Now, IsDemo: true),
            new FaceStatusSnapshot(FaceStatusKind.Listening, "Listening", "Capturing the user request now.", DateTimeOffset.Now, IsDemo: true),
            new FaceStatusSnapshot(FaceStatusKind.Transcribing, "Transcribing", "Turning speech into text.", DateTimeOffset.Now, IsDemo: true),
            new FaceStatusSnapshot(FaceStatusKind.Thinking, "Thinking", "Planning the next move.", DateTimeOffset.Now, IsDemo: true),
            new FaceStatusSnapshot(FaceStatusKind.Acting, "Acting", "Calling tools and checking the screen.", DateTimeOffset.Now, ToolName: "describe_window", IsDemo: true),
            new FaceStatusSnapshot(FaceStatusKind.Speaking, "Speaking", "Answering back to the user.", DateTimeOffset.Now, Transcript: "Netflix is open, and it is waiting on the profile picker.", IsDemo: true)
        };

        ApplySnapshot(states[_demoIndex % states.Length]);
        _demoIndex += 1;
    }

    private void ApplySnapshot(FaceStatusSnapshot snapshot)
    {
        StatusLabel = snapshot.Kind.ToString().ToLowerInvariant();
        Headline = snapshot.Headline;
        Detail = snapshot.Detail;

        switch (snapshot.Kind)
        {
            case FaceStatusKind.Standby:
                SetVisuals("#1D4ED8", "M 48 50 Q 80 58 112 50");
                break;
            case FaceStatusKind.Listening:
                SetVisuals("#0EA5E9", "M 48 52 Q 80 64 112 52");
                break;
            case FaceStatusKind.Transcribing:
                SetVisuals("#06B6D4", "M 48 50 Q 80 60 112 50");
                break;
            case FaceStatusKind.Thinking:
                SetVisuals("#F59E0B", "M 48 52 Q 80 44 112 52");
                break;
            case FaceStatusKind.Acting:
                SetVisuals("#F97316", "M 48 50 Q 80 62 112 50");
                break;
            case FaceStatusKind.Speaking:
                SetVisuals("#10B981", "M 48 46 Q 80 74 112 46");
                break;
            case FaceStatusKind.Error:
                SetVisuals("#EF4444", "M 48 58 Q 80 40 112 58");
                break;
            case FaceStatusKind.Demo:
                SetVisuals("#A855F7", "M 48 52 Q 80 64 112 52");
                break;
            default:
                SetVisuals("#475569", "M 48 48 Q 80 68 112 48");
                break;
        }

        var activity = snapshot.Transcript is not null
            ? $"{StatusLabel}: {snapshot.Transcript}"
            : snapshot.ToolName is not null
                ? $"{StatusLabel}: {snapshot.ToolName}"
                : $"{StatusLabel}: {snapshot.Detail}";
        AddActivity(activity);
    }

    private void SetVisuals(string colorHex, string mouthGeometry)
    {
        AccentColor = (Color)MediaColorConverter.ConvertFromString(colorHex);
        AccentBrush = BuildBrush(colorHex);
        MouthGeometry = Geometry.Parse(mouthGeometry);
    }

    private void AddActivity(string text)
    {
        ActivityLines.Insert(0, text);
        while (ActivityLines.Count > 5)
        {
            ActivityLines.RemoveAt(ActivityLines.Count - 1);
        }
    }

    private static Brush BuildBrush(string colorHex)
    {
        return new SolidColorBrush((Color)MediaColorConverter.ConvertFromString(colorHex));
    }

    private static FaceStatusKind ParseKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "standby" => FaceStatusKind.Standby,
            "listening" => FaceStatusKind.Listening,
            "transcribing" => FaceStatusKind.Transcribing,
            "thinking" => FaceStatusKind.Thinking,
            "acting" => FaceStatusKind.Acting,
            "speaking" => FaceStatusKind.Speaking,
            "error" => FaceStatusKind.Error,
            "demo" => FaceStatusKind.Demo,
            _ => FaceStatusKind.Offline,
        };
    }
}
