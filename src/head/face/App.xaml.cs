using HeronWin.Face.Services;
using HeronWin.Face.ViewModels;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using SystemIcons = System.Drawing.SystemIcons;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace HeronWin.Face;

public partial class App : Application
{
    private readonly FaceSettingsService _settingsService = new();
    private NotifyIcon? _notifyIcon;
    private FacePipeClient? _pipeClient;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = await _settingsService.LoadAsync();
        _mainViewModel = new MainViewModel(settings, _settingsService);
        _pipeClient = new FacePipeClient(_mainViewModel);
        _mainWindow = new MainWindow(_mainViewModel);
        _mainWindow.Show();

        InitializeTray();

        _mainViewModel.OpenSettingsRequested += (_, _) => OpenSettings();
        _mainViewModel.ExitRequested += async (_, _) => await ShutdownAsync();
        _mainViewModel.ApplyPinnedState();
        _pipeClient.Start();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_pipeClient is not null)
        {
            await _pipeClient.DisposeAsync();
        }

        base.OnExit(e);
    }

    private void InitializeTray()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "her face",
            Icon = SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.ContextMenuStrip.Items.AddRange(
        [
            BuildMenuItem("Open face", (_, _) => ShowMainWindow()),
            BuildMenuItem("Settings", (_, _) => OpenSettings()),
            BuildMenuItem("Exit", async (_, _) => await ShutdownAsync())
        ]);
    }

    private ToolStripMenuItem BuildMenuItem(string label, EventHandler handler)
    {
        var item = new ToolStripMenuItem(label);
        item.Click += handler;
        return item;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OpenSettings()
    {
        if (_mainViewModel is null)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(_mainViewModel);
        settingsWindow.Owner = _mainWindow;
        settingsWindow.ShowDialog();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.PersistAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save face settings before exit.\n\n{ex.Message}",
                "her face",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Shutdown();
    }
}