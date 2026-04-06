using HeronWin.Face.ViewModels;
using System.Windows;

namespace HeronWin.Face;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
            viewModel.AttachWindow(this);
        };
        MouseLeftButtonDown += (_, _) => DragMove();
    }
}