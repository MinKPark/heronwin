using HeronWin.Face.ViewModels;
using System.Windows;

namespace HeronWin.Face;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel.Settings;
        OpenAiKeyBox.Password = _viewModel.Settings.OpenAiApiKey;
        AnthropicKeyBox.Password = _viewModel.Settings.AnthropicApiKey;
    }

    private void OpenAiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.OpenAiApiKey = OpenAiKeyBox.Password;
    }

    private void AnthropicKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.AnthropicApiKey = AnthropicKeyBox.Password;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}