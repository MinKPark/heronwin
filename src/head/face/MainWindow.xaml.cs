using HeronWin.Face.ViewModels;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows;

namespace HeronWin.Face;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Random _random = new();
    private DispatcherTimer? _blinkTimer;
    private Storyboard? _avatarStoryboard;
    private Storyboard? _mouthStoryboard;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
            viewModel.AttachWindow(this);
            StartBlinkLoop();
            ApplyAnimationState(viewModel.StatusLabel);
        };
        Closed += (_, _) => StopAnimationLoops();
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusLabel))
        {
            Dispatcher.Invoke(() => ApplyAnimationState(_viewModel.StatusLabel));
        }
    }

    private void StartBlinkLoop()
    {
        _blinkTimer ??= new DispatcherTimer();
        _blinkTimer.Tick -= BlinkTimerOnTick;
        _blinkTimer.Tick += BlinkTimerOnTick;
        ScheduleNextBlink();
        _blinkTimer.Start();
    }

    private void StopAnimationLoops()
    {
        _blinkTimer?.Stop();
        _avatarStoryboard?.Stop(this);
        _mouthStoryboard?.Stop(this);
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
    }

    private void BlinkTimerOnTick(object? sender, EventArgs e)
    {
        _blinkTimer?.Stop();
        var blinkStoryboard = new Storyboard();

        AddDoubleAnimation(blinkStoryboard, LeftEyeScaleTransform, new PropertyPath("ScaleY"), 1, 0.12, 0.08);
        AddDoubleAnimation(blinkStoryboard, RightEyeScaleTransform, new PropertyPath("ScaleY"), 1, 0.12, 0.08);

        var reopenLeft = new DoubleAnimation(0.12, 1, TimeSpan.FromSeconds(0.12))
        {
            BeginTime = TimeSpan.FromSeconds(0.09),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(reopenLeft, LeftEyeScaleTransform);
        Storyboard.SetTargetProperty(reopenLeft, new PropertyPath("ScaleY"));
        blinkStoryboard.Children.Add(reopenLeft);

        var reopenRight = new DoubleAnimation(0.12, 1, TimeSpan.FromSeconds(0.12))
        {
            BeginTime = TimeSpan.FromSeconds(0.09),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(reopenRight, RightEyeScaleTransform);
        Storyboard.SetTargetProperty(reopenRight, new PropertyPath("ScaleY"));
        blinkStoryboard.Children.Add(reopenRight);

        blinkStoryboard.Completed += (_, _) => ScheduleNextBlink();
        blinkStoryboard.Begin(this, true);
    }

    private void ScheduleNextBlink()
    {
        if (_blinkTimer is null)
        {
            return;
        }

        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(1800, 4200));
    }

    private void ApplyAnimationState(string status)
    {
        ResetTransforms();
        _avatarStoryboard?.Stop(this);
        _mouthStoryboard?.Stop(this);

        _avatarStoryboard = status switch
        {
            "standby" => CreateBreathingStoryboard(1.025, -3, 0, 0.84, 0.98, 2.8),
            "listening" => CreateBreathingStoryboard(1.08, -6, 0, 0.88, 1.0, 0.72),
            "transcribing" => CreateTranscribingStoryboard(),
            "thinking" => CreateThinkingStoryboard(),
            "acting" => CreateActingStoryboard(),
            "speaking" => CreateSpeakingAvatarStoryboard(),
            "error" => CreateErrorStoryboard(),
            _ => CreateBreathingStoryboard(1.015, -2, 0, 0.7, 0.82, 3.2),
        };
        _avatarStoryboard.Begin(this, true);

        if (status == "speaking")
        {
            _mouthStoryboard = CreateSpeakingMouthStoryboard();
            _mouthStoryboard.Begin(this, true);
        }
    }

    private void ResetTransforms()
    {
        AvatarScaleTransform.ScaleX = 1;
        AvatarScaleTransform.ScaleY = 1;
        AvatarRotateTransform.Angle = 0;
        AvatarTranslateTransform.Y = 0;
        MouthScaleTransform.ScaleY = 1;
        AuraEllipse.Opacity = 0.92;
        LeftEyeScaleTransform.ScaleY = 1;
        RightEyeScaleTransform.ScaleY = 1;
    }

    private Storyboard CreateBreathingStoryboard(double scalePeak, double travelY, double rotateAngle, double minOpacity, double maxOpacity, double seconds)
    {
        var storyboard = new Storyboard();
        AddAutoReverseAnimation(storyboard, AvatarScaleTransform, new PropertyPath("ScaleX"), 1, scalePeak, seconds);
        AddAutoReverseAnimation(storyboard, AvatarScaleTransform, new PropertyPath("ScaleY"), 1, scalePeak, seconds);
        AddAutoReverseAnimation(storyboard, AvatarTranslateTransform, new PropertyPath("Y"), 0, travelY, seconds);
        AddAutoReverseAnimation(storyboard, AvatarRotateTransform, new PropertyPath("Angle"), -rotateAngle, rotateAngle, seconds * 1.2);
        AddAutoReverseAnimation(storyboard, AuraEllipse, new PropertyPath("Opacity"), minOpacity, maxOpacity, seconds);
        return storyboard;
    }

    private Storyboard CreateTranscribingStoryboard()
    {
        var storyboard = CreateBreathingStoryboard(1.05, -4, 1.2, 0.82, 1.0, 0.55);
        AddAutoReverseAnimation(storyboard, AvatarRotateTransform, new PropertyPath("Angle"), -2.4, 2.4, 0.3);
        return storyboard;
    }

    private Storyboard CreateThinkingStoryboard()
    {
        var storyboard = CreateBreathingStoryboard(1.03, -2, 0, 0.82, 0.98, 1.9);
        AddAutoReverseAnimation(storyboard, AvatarRotateTransform, new PropertyPath("Angle"), -3.2, 3.2, 1.4);
        return storyboard;
    }

    private Storyboard CreateActingStoryboard()
    {
        var storyboard = CreateBreathingStoryboard(1.06, -6, 0, 0.86, 1.0, 0.42);
        AddAutoReverseAnimation(storyboard, AvatarRotateTransform, new PropertyPath("Angle"), -1.6, 1.6, 0.42);
        return storyboard;
    }

    private Storyboard CreateSpeakingAvatarStoryboard()
    {
        var storyboard = CreateBreathingStoryboard(1.045, -3, 0.8, 0.86, 1.0, 0.48);
        return storyboard;
    }

    private Storyboard CreateSpeakingMouthStoryboard()
    {
        var storyboard = new Storyboard();
        AddAutoReverseAnimation(storyboard, MouthScaleTransform, new PropertyPath("ScaleY"), 1, 1.65, 0.18);
        return storyboard;
    }

    private Storyboard CreateErrorStoryboard()
    {
        var storyboard = new Storyboard();
        AddAutoReverseAnimation(storyboard, AvatarRotateTransform, new PropertyPath("Angle"), -5, 5, 0.12);
        AddAutoReverseAnimation(storyboard, AvatarTranslateTransform, new PropertyPath("Y"), 0, -3, 0.18);
        AddAutoReverseAnimation(storyboard, AuraEllipse, new PropertyPath("Opacity"), 0.72, 1.0, 0.2);
        return storyboard;
    }

    private static void AddAutoReverseAnimation(Storyboard storyboard, DependencyObject target, PropertyPath property, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private static void AddDoubleAnimation(Storyboard storyboard, DependencyObject target, PropertyPath property, double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}